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
 *   - The free-text term is a bind parameter (@Q1). Client id, schema
 *     currency id and row cap are validated integers, then inlined.
 *   - MRole.AddAccessSQL applied to the main physical table only
 *     (the join-free base sub-select aliased j). IsActive = 'Y';
 *     TotalDr converted to functional currency, StdPrecision read
 *     from system. GL journals have no business partner, so the
 *     Description is used as the result title.
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

namespace VAS.Areas.VAS.Controllers
{
    public class VAS_070_GLJournalSearchWidgetController : Controller
    {
        string strQuery = "";
        private static int _windowId = -1;

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

            string term = (query ?? "").Trim().ToLower();
            if (term.Length < 2) { return result; }
            if (maxRows < 1) { maxRows = 1; }
            if (maxRows > 50) { maxRows = 50; }
            if (offset < 0) { offset = 0; }
            if (offset > 100000) { offset = 100000; }

            int clientId = ctx.GetAD_Client_ID();

            int schemaCurrencyId = 0;
            strQuery = @"SELECT cs.C_Currency_ID, c.CurSymbol, c.StdPrecision
                    FROM C_AcctSchema cs
                    INNER JOIN AD_ClientInfo ci ON (ci.C_AcctSchema1_ID = cs.C_AcctSchema_ID)
                    INNER JOIN C_Currency c ON (cs.C_Currency_ID = c.C_Currency_ID)
                   WHERE ci.AD_Client_ID = " + clientId + @"
                     AND ci.IsActive = 'Y'
                     AND cs.IsActive = 'Y'
                     AND c.IsActive = 'Y'";
            strQuery = MRole.GetDefault(ctx).AddAccessSQL(strQuery, "cs", MRole.SQL_FULLYQUALIFIED, MRole.SQL_RO);

            DataSet cDs = DB.ExecuteDataset(strQuery, null, null);
            if (cDs != null && cDs.Tables.Count > 0 && cDs.Tables[0].Rows.Count > 0)
            {
                schemaCurrencyId    = Util.GetValueOfInt(cDs.Tables[0].Rows[0]["C_Currency_ID"]);
                result.CurSymbol    = Util.GetValueOfString(cDs.Tables[0].Rows[0]["CurSymbol"]);
                result.StdPrecision = Util.GetValueOfInt(cDs.Tables[0].Rows[0]["StdPrecision"]);
            }
            if (schemaCurrencyId == 0) { return result; }

            bool isPg    = DB.IsPostgreSQL();
            string sep   = isPg ? "' '" : "N' '";
            string empty = isPg ? "''"  : "N''";

            string baseSql = @"SELECT j.GL_Journal_ID, j.DocumentNo, j.Description, j.GLReference, j.Combination,
                       j.CombinationName, j.VAS_Label, j.TotalDr, j.C_Currency_ID, j.DateAcct, j.C_ConversionType_ID,
                       j.C_DocType_ID, j.GL_Category_ID, j.GL_JournalBatch_ID, j.DocStatus,
                       j.AD_Client_ID, j.AD_Org_ID, j.DateDoc
                  FROM GL_Journal j
                 WHERE j.IsActive = 'Y' AND j.AD_Client_ID = " + clientId;
            baseSql = MRole.GetDefault(ctx).AddAccessSQL(baseSql, "j", MRole.SQL_FULLYQUALIFIED, MRole.SQL_RO);

            // Lookup tables (C_DocType / GL_Category / GL_JournalBatch) join outside
            // MRole scope and feed the searchable text so the user can search by
            // document-type name, GL category, or the parent batch no./description.
            string core = @"SELECT b.GL_Journal_ID AS RecordId, b.DocumentNo AS DocumentNo, b.Description AS Title,
                       COALESCE(currencyConvert(b.TotalDr, b.C_Currency_ID, " + schemaCurrencyId + 
                       @", b.DateAcct, b.C_ConversionType_ID, b.AD_Client_ID, b.AD_Org_ID), 0) AS Amount,
                       b.DateDoc AS DocDate, b.DocStatus AS DocStatus,
                       CASE WHEN LOWER(b.DocumentNo) = @QExact THEN 4
                            WHEN LOWER(b.DocumentNo) LIKE @QStart THEN 3
                            WHEN LOWER(b.DocumentNo) LIKE @QDoc THEN 2
                            ELSE 1 END AS Relevance
                  FROM (" + baseSql + @") b
                 INNER JOIN C_DocType dt ON (dt.C_DocType_ID = b.C_DocType_ID)
                INNER JOIN GL_Category gc ON (gc.GL_Category_ID = b.GL_Category_ID)
                 LEFT OUTER JOIN GL_JournalBatch jb ON (jb.GL_JournalBatch_ID = b.GL_JournalBatch_ID)
                 WHERE LOWER(b.DocumentNo || " + sep + 
                 " || COALESCE(b.Description, " + empty + ") || " + sep + 
                 " || COALESCE(b.GLReference, " + empty + ") || " + sep + 
                 " || COALESCE(b.Combination, " + empty + ") || " + sep + 
                 " || COALESCE(b.CombinationName, " + empty + ") || " + sep + 
                 " || COALESCE(b.VAS_Label, " + empty + ") || " + sep + 
                 " || COALESCE(dt.Name, " + empty + ") || " + sep + 
                 " || COALESCE(dt.PrintName, " + empty + ") || " + sep + 
                 " || COALESCE(gc.Name, " + empty + ") || " + sep + 
                 " || COALESCE(jb.DocumentNo, " + empty + ") || " + sep + 
                 " || COALESCE(jb.Description, " + empty + @")) LIKE @Q1
                    OR EXISTS (SELECT 1 FROM GL_JournalLine jl
                                INNER JOIN C_BPartner lbp ON (lbp.C_BPartner_ID = jl.C_BPartner_ID)
                                WHERE jl.GL_Journal_ID = b.GL_Journal_ID AND jl.IsActive = 'Y'
                                  AND LOWER(COALESCE(lbp.Name, " + empty + ") || " + sep + " || COALESCE(lbp.Value, " + empty + @")) LIKE @Q2)";

            if (isPg)
            {
                strQuery = core + " ORDER BY Relevance DESC, DocDate DESC, RecordId DESC LIMIT " + maxRows + " OFFSET " + offset;
            }
            else
            {
                strQuery = "SELECT * FROM (SELECT t.*, ROWNUM AS rn FROM (" + core + " ORDER BY Relevance DESC, DocDate DESC, RecordId DESC) t WHERE ROWNUM <= " + (offset + maxRows) + ") WHERE rn > " + offset;
            }

            // Order matches textual appearance: relevance params (SELECT CASE), then
            // @Q1 (header WHERE), then @Q2 (journal-line vendor EXISTS).
            string like = "%" + term + "%";
            SqlParameter[] p = {
                new SqlParameter("@QExact", term),
                new SqlParameter("@QStart", term + "%"),
                new SqlParameter("@QDoc", like),
                new SqlParameter("@Q1", like),
                new SqlParameter("@Q2", like)
            };
            DataSet ds = DB.ExecuteDataset(strQuery, p, null);
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
                        RecordId   = Util.GetValueOfInt(row["RecordId"]),
                        DocumentNo = docNo,
                        Title      = title,
                        Amount     = Util.GetValueOfDecimal(row["Amount"]),
                        DocDate    = d.HasValue ? d.Value.ToString("yyyy-MM-dd") : "",
                        DocStatus  = Util.GetValueOfString(row["DocStatus"])
                    });
                }
            }
            return result;
        }

        /// <summary>Header window for zoom: VAS_GLJournal, else standard "GL Journal".</summary>
        private int ResolveWindowId()
        {
            if (_windowId >= 0) { return _windowId; }
            int id = GetWindowIdByName("VAS_GLJournal");
            if (id == 0) { id = GetWindowIdByName("GL Journal"); }
            _windowId = id;
            return _windowId;
        }

        private int GetWindowIdByName(string name)
        {
            string q = "SELECT MIN(AD_Window_ID) AS WID FROM AD_Window WHERE Name = @Name AND IsActive = 'Y'";
            SqlParameter[] p = { new SqlParameter("@Name", name) };
            return Util.GetValueOfInt(DB.ExecuteScalar(q, p, null));
        }

        public class DocSearchResult
        {
            public string         CurSymbol    { get; set; }
            public int            StdPrecision { get; set; }
            public int            WindowId     { get; set; }
            public List<SearchHit> Items       { get; set; }
        }

        public class SearchHit
        {
            public int     RecordId   { get; set; }
            public string  DocumentNo { get; set; }
            public string  Title      { get; set; }
            public decimal Amount     { get; set; }
            public string  DocDate    { get; set; }
            public string  DocStatus  { get; set; }
        }
    }
}
