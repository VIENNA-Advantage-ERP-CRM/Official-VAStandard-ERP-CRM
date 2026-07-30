/************************************************************
 * Module Name    : VAS
 * Purpose        : Controller for the GL Journal Search widget.
 *                  Full-width dashboard search bar that searches GL
 *                  journals (GL_Journal) and returns the most relevant
 *                  matches. Clicking a result zooms to the journal.
 * chronological  : Development
 * Created Date   : 13 June 2026
 * Created by     : Claude (VAS widget pattern)
 *
 * Notes:
 *   - Read-only. Only SELECT queries are executed.
 *   - The free-text term is bound (@Q1 + relevance params @QExact /
 *     @QStart / @QDoc + line-match @Q2). Client id and row cap are
 *     validated integers, then inlined (no user text in SQL).
 *   - MRole.AddAccessSQL is applied to the main physical table only
 *     (the join-free base sub-select aliased j). IsActive = 'Y'.
 *   - The AMOUNT is shown in the journal's OWN document currency
 *     (C_Currency_ID) - NOT converted to the functional currency -
 *     so the currency symbol / precision is resolved PER ROW.
 *   - Searchable fields: DocumentNo, Description, GLReference,
 *     Combination(Name), VAS_Label, DocType, GL Category, batch,
 *     Accounting Schema name, Posting Type name, DocStatus keyword,
 *     Posted keyword, amount (TotalDr / TotalCr), and journal-line
 *     business partner + ledger account (C_ElementValue value/name).
 *   - Runs on Oracle and PostgreSQL.
 ***********************************************************/
using CoreLibrary.DataBase;
using Newtonsoft.Json;
using System;
using System.Collections.Generic;
using System.Data;
using System.Data.SqlClient;
using System.Web.Mvc;
using VAdvantage.Model;
using VAdvantage.Utility;
using VASLogic.Models;

namespace VAS.Areas.VAS.Controllers
{
    public class VAS_070_GLJournalSearchWidgetController : Controller
    {
        string strQuery = "";
        private static int _windowId = -1;

        // AD_Reference_ID of the "PostingType" list (Actual / Budget / ...).
        private const int POSTINGTYPE_REFERENCE_ID = 125;

        [Authorize]
        public ActionResult Index()
        {
            return View();
        }

        public JsonResult Search(string query, int maxRows = 25, int offset = 0)
        {
            string retJSON = "";
            if (Session["ctx"] != null)
            {
                Ctx ctx = Session["ctx"] as Ctx;
                DocSearchResult result = BuildResult(ctx, query, maxRows, offset);
                retJSON = JsonConvert.SerializeObject(result);
            }
            return Json(retJSON, JsonRequestBehavior.AllowGet);
        }

        private DocSearchResult BuildResult(Ctx ctx, string query, int maxRows, int offset)
        {
            DocSearchResult result = new DocSearchResult();
            result.Items = new List<SearchHit>();
            result.WindowId = ResolveWindowId();

            // Keep the term as typed for the LIKE params - the SQL wraps BOTH the column and
            // the parameter in LOWER() so the match is case-insensitive using the DB's own
            // collation. `ql` is only for the C#-side status / posted / amount keyword checks.
            string term = (query ?? "").Trim();
            if (term.Length < 2) { return result; }
            string ql = term.ToLowerInvariant();
            if (maxRows < 1) { maxRows = 1; }
            if (maxRows > 50) { maxRows = 50; }
            if (offset < 0) { offset = 0; }
            if (offset > 100000) { offset = 100000; }

            int clientId = ctx.GetAD_Client_ID();

            bool isPg    = DB.IsPostgreSQL();
            string sep   = isPg ? "' '" : "N' '";
            string empty = isPg ? "''"  : "N''";

            // MRole on the join-free single-table base. Amount stays in the journal's own
            // document currency (TotalDr / TotalCr / C_Currency_ID) - no conversion.
            string baseSql = @"SELECT j.GL_Journal_ID, j.DocumentNo, j.Description, j.GLReference, j.Combination,
                       j.CombinationName, j.VAS_Label, j.TotalDr, j.TotalCr, j.C_Currency_ID, j.DateAcct,
                       j.C_DocType_ID, j.GL_Category_ID, j.GL_JournalBatch_ID, j.DocStatus,
                       j.C_AcctSchema_ID, j.PostingType, j.Posted,
                       j.AD_Client_ID, j.AD_Org_ID, j.DateDoc
                  FROM GL_Journal j
                 WHERE j.IsActive = 'Y' AND j.AD_Client_ID = " + clientId;
            baseSql = MRole.GetDefault(ctx).AddAccessSQL(baseSql, "j", MRole.SQL_FULLYQUALIFIED, MRole.SQL_RO);

            // Beyond the free-text match, the term can also find journals by document STATUS
            // (keyword -> DocStatus code), POSTED flag (posted / unposted), or exact AMOUNT
            // (numeric term matches TotalDr or TotalCr). Status codes and the posted flag are
            // fixed literals (safe to inline); the amount is bound (@Amt).
            var extraOrs = new List<string>();
            var codes = new List<string>();
            if (ql.Contains("draft"))    { codes.Add("'DR'"); }
            if (ql.Contains("progress")) { codes.Add("'IP'"); }
            if (ql.Contains("close"))    { codes.Add("'CL'"); }
            if (ql.Contains("approv"))   { codes.Add("'AP'"); }
            if (ql.Contains("complete")) { codes.Add("'CO'"); }
            if (ql.Contains("reverse"))  { codes.Add("'RE'"); codes.Add("'VO'"); }
            if (ql.Contains("void"))     { codes.Add("'VO'"); }
            if (ql.Contains("invalid"))  { codes.Add("'IN'"); }
            if (ql.Contains("waiting"))  { codes.Add("'WP'"); codes.Add("'WC'"); }
            if (codes.Count > 0) { extraOrs.Add("b.DocStatus IN (" + string.Join(", ", codes) + ")"); }
            // "unposted" / "not posted" checked first (both contain "posted").
            if (ql.Contains("unposted") || ql.Contains("not posted")) { extraOrs.Add("b.Posted = 'N'"); }
            else if (ql.Contains("posted"))                           { extraOrs.Add("b.Posted = 'Y'"); }
            decimal amt;
            bool hasAmt = decimal.TryParse(ql.Replace(",", "").Replace("$", "").Trim(), out amt);
            if (hasAmt) { extraOrs.Add("(ABS(b.TotalDr) = @Amt OR ABS(b.TotalCr) = @Amt)"); }

            // Lookup tables (C_DocType / GL_Category / GL_JournalBatch / C_AcctSchema /
            // AD_Ref_List posting type) join outside MRole scope and feed the searchable text
            // so the user can search by document-type name, GL category, parent batch,
            // accounting schema, or posting-type name - not by id.
            string textMatch = "LOWER(b.DocumentNo || " + sep +
                 " || COALESCE(b.Description, " + empty + ") || " + sep +
                 " || COALESCE(b.GLReference, " + empty + ") || " + sep +
                 " || COALESCE(b.Combination, " + empty + ") || " + sep +
                 " || COALESCE(b.CombinationName, " + empty + ") || " + sep +
                 " || COALESCE(b.VAS_Label, " + empty + ") || " + sep +
                 " || COALESCE(dt.Name, " + empty + ") || " + sep +
                 " || COALESCE(dt.PrintName, " + empty + ") || " + sep +
                 " || COALESCE(gc.Name, " + empty + ") || " + sep +
                 " || COALESCE(acs.Name, " + empty + ") || " + sep +
                 " || COALESCE(rl.Name, " + empty + ") || " + sep +
                 " || COALESCE(jb.DocumentNo, " + empty + ") || " + sep +
                 " || COALESCE(jb.Description, " + empty + ")) LIKE LOWER(@Q1)";

            // Journal-line match: business partner OR ledger account (C_ElementValue) reached
            // through GL_JournalLine.Account_ID (the natural-account element value id).
            string lineExists = @"EXISTS (SELECT 1 FROM GL_JournalLine jl
                                LEFT OUTER JOIN C_BPartner lbp ON (lbp.C_BPartner_ID = jl.C_BPartner_ID)
                                LEFT OUTER JOIN C_ElementValue ev ON (ev.C_ElementValue_ID = jl.Account_ID)
                                WHERE jl.GL_Journal_ID = b.GL_Journal_ID AND jl.IsActive = 'Y'
                                  AND LOWER(COALESCE(lbp.Name, " + empty + ") || " + sep +
                                  " || COALESCE(lbp.Value, " + empty + ") || " + sep +
                                  " || COALESCE(ev.Value, " + empty + ") || " + sep +
                                  " || COALESCE(ev.Name, " + empty + ")) LIKE LOWER(@Q2))";

            // Order in SQL text (matters for Oracle positional binding): @Q1 (header text),
            // then the optional @Amt inside extraOrs, then @Q2 (journal-line EXISTS).
            var ors = new List<string>();
            ors.Add(textMatch);
            ors.AddRange(extraOrs);
            ors.Add(lineExists);
            string matchClause = "(" + string.Join(" OR ", ors) + ")";

            string core = @"SELECT b.GL_Journal_ID AS RecordId, b.DocumentNo AS DocumentNo, b.Description AS Title,
                       b.TotalDr AS Amount, b.DateDoc AS DocDate, b.DocStatus AS DocStatus, b.Posted AS Posted,
                       cur.CurSymbol AS CurSymbol, cur.StdPrecision AS StdPrecision,
                       acs.Name AS AcctSchema, rl.Name AS PostingTypeName,
                       CASE WHEN LOWER(b.DocumentNo) = LOWER(@QExact) THEN 4
                            WHEN LOWER(b.DocumentNo) LIKE LOWER(@QStart) THEN 3
                            WHEN LOWER(b.DocumentNo) LIKE LOWER(@QDoc) THEN 2
                            ELSE 1 END AS Relevance
                  FROM (" + baseSql + @") b
                 INNER JOIN C_DocType dt ON (dt.C_DocType_ID = b.C_DocType_ID)
                 INNER JOIN GL_Category gc ON (gc.GL_Category_ID = b.GL_Category_ID)
                 LEFT OUTER JOIN GL_JournalBatch jb ON (jb.GL_JournalBatch_ID = b.GL_JournalBatch_ID)
                 LEFT OUTER JOIN C_Currency cur ON (cur.C_Currency_ID = b.C_Currency_ID)
                 LEFT OUTER JOIN C_AcctSchema acs ON (acs.C_AcctSchema_ID = b.C_AcctSchema_ID)
                 LEFT OUTER JOIN AD_Ref_List rl ON (rl.AD_Reference_ID = " + POSTINGTYPE_REFERENCE_ID + @"
                                                    AND rl.Value = b.PostingType AND rl.IsActive = 'Y')
                 WHERE " + matchClause;

            // Fetch ONE row more than the page size so we can tell the client whether another
            // page exists (HasMore) without a separate COUNT - the extra row is trimmed below.
            int fetch = maxRows + 1;
            if (isPg)
            {
                strQuery = core + " ORDER BY Relevance DESC, DocDate DESC, RecordId DESC LIMIT " + fetch + " OFFSET " + offset;
            }
            else
            {
                strQuery = "SELECT * FROM (SELECT t.*, ROWNUM AS rn FROM (" + core + " ORDER BY Relevance DESC, DocDate DESC, RecordId DESC) t WHERE ROWNUM <= " + (offset + fetch) + ") WHERE rn > " + offset;
            }

            // Relevance params (doc-no exact / prefix / contains) come first textually
            // (in the SELECT CASE), then @Q1 (header WHERE), then the optional @Amt, then
            // @Q2 (journal-line EXISTS). Oracle binds by POSITION - keep this order.
            string like = "%" + term + "%";
            var p = new List<SqlParameter>
            {
                new SqlParameter("@QExact", term),
                new SqlParameter("@QStart", term + "%"),
                new SqlParameter("@QDoc", like),
                new SqlParameter("@Q1", like)
            };
            if (hasAmt) { p.Add(new SqlParameter("@Amt", Math.Abs(amt))); }
            p.Add(new SqlParameter("@Q2", like));

            DataSet ds = DB.ExecuteDataset(strQuery, p.ToArray(), null);
            if (ds != null && ds.Tables.Count > 0)
            {
                foreach (DataRow row in ds.Tables[0].Rows)
                {
                    string docNo = Util.GetValueOfString(row["DocumentNo"]);
                    string title = Util.GetValueOfString(row["Title"]);
                    if (string.IsNullOrEmpty(title)) { title = docNo; }
                    DateTime? d = Util.GetValueOfDateTime(row["DocDate"]);

                    result.Items.Add(new SearchHit
                    {
                        RecordId     = Util.GetValueOfInt(row["RecordId"]),
                        DocumentNo   = docNo,
                        Title        = title,
                        Amount       = Util.GetValueOfDecimal(row["Amount"]),
                        CurSymbol    = Util.GetValueOfString(row["CurSymbol"]),
                        StdPrecision = Util.GetValueOfInt(row["StdPrecision"]),
                        DocDate      = d.HasValue ? d.Value.ToString("yyyy-MM-dd") : "",
                        DocStatus    = Util.GetValueOfString(row["DocStatus"]),
                        Posted       = Util.GetValueOfString(row["Posted"]) == "Y",
                        AcctSchema   = Util.GetValueOfString(row["AcctSchema"]),
                        PostingType  = Util.GetValueOfString(row["PostingTypeName"])
                    });
                }
            }
            // Trim the sentinel extra row; its presence signals another page exists.
            result.HasMore = result.Items.Count > maxRows;
            if (result.HasMore) { result.Items.RemoveAt(result.Items.Count - 1); }

            // Second pass: attach the ledger account codes (C_ElementValue.Value) of each
            // journal's lines for display. The ids come from our own MRole-guarded result set,
            // so they are safe to inline; done in one batched query (dual-DB, no string_agg).
            AttachLedgerCodes(result.Items);
            return result;
        }

        /// <summary>
        /// Loads the distinct ledger account codes (C_ElementValue.Value via
        /// GL_JournalLine.Account_ID) for the given hits in a single query and sets
        /// LedgerCode on each (comma-joined, capped for display).
        /// </summary>
        private void AttachLedgerCodes(List<SearchHit> items)
        {
            if (items == null || items.Count == 0) { return; }

            var ids = new List<string>();
            var byId = new Dictionary<int, SearchHit>();
            foreach (SearchHit hit in items)
            {
                if (hit.RecordId > 0 && !byId.ContainsKey(hit.RecordId))
                {
                    byId[hit.RecordId] = hit;
                    ids.Add(hit.RecordId.ToString());
                }
            }
            if (ids.Count == 0) { return; }

            string sql = @"SELECT jl.GL_Journal_ID, ev.Value AS Code
                             FROM GL_JournalLine jl
                             INNER JOIN C_ElementValue ev ON (ev.C_ElementValue_ID = jl.Account_ID)
                            WHERE jl.IsActive = 'Y' AND jl.GL_Journal_ID IN (" + string.Join(", ", ids) + @")
                            ORDER BY jl.GL_Journal_ID, jl.Line";

            var codesById = new Dictionary<int, List<string>>();
            DataSet ds = DB.ExecuteDataset(sql, null, null);
            if (ds != null && ds.Tables.Count > 0)
            {
                foreach (DataRow row in ds.Tables[0].Rows)
                {
                    int jid = Util.GetValueOfInt(row["GL_Journal_ID"]);
                    string code = Util.GetValueOfString(row["Code"]);
                    if (string.IsNullOrEmpty(code)) { continue; }
                    if (!codesById.ContainsKey(jid)) { codesById[jid] = new List<string>(); }
                    List<string> list = codesById[jid];
                    if (!list.Contains(code) && list.Count < 8) { list.Add(code); }
                }
            }
            foreach (var kv in codesById)
            {
                SearchHit hit;
                if (byId.TryGetValue(kv.Key, out hit))
                {
                    hit.LedgerCode = string.Join(", ", kv.Value);
                }
            }
        }

        /// <summary>Header window for zoom: VAS_GLJournal, else standard "GL Journal".</summary>
        private int ResolveWindowId()
        {
            if (_windowId >= 0) { return _windowId; }
            int id = new PoReceiptTabPanelModel().GetWindowId("VAS_GLJournal", "GL Journal"); 
            _windowId = id;
            return _windowId;
        }

        public class DocSearchResult
        {
            public int             WindowId { get; set; }
            public bool            HasMore  { get; set; }   // another page exists (scroll paging)
            public List<SearchHit> Items    { get; set; }
        }

        public class SearchHit
        {
            public int     RecordId     { get; set; }
            public string  DocumentNo   { get; set; }
            public string  Title        { get; set; }
            public decimal Amount       { get; set; }   // TotalDr in the journal's document currency
            public string  CurSymbol    { get; set; }   // document currency symbol (per row)
            public int     StdPrecision { get; set; }   // document currency precision (per row)
            public string  DocDate      { get; set; }
            public string  DocStatus    { get; set; }
            public bool    Posted       { get; set; }
            public string  AcctSchema   { get; set; }   // C_AcctSchema.Name
            public string  PostingType  { get; set; }   // AD_Ref_List.Name (Actual / Budget / ...)
            public string  LedgerCode   { get; set; }   // C_ElementValue codes of the journal lines
        }
    }
}
