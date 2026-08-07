/************************************************************
 * Module Name    : VAS
 * Purpose        : Controller for the Cash Journal Search widget.
 *                  Full-width dashboard search bar that searches cash
 *                  journals (C_Cash) and returns the most relevant
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
 *     (the join-free base sub-select aliased c). IsActive = 'Y';
 *     EndingBalance converted to functional currency (C_Cash carries
 *     its own C_Currency_ID; no conversion type column so NULL is
 *     passed -> default rate type). StdPrecision read from system.
 *   - C_CashBook is joined to make the cashbook name searchable and to return
 *     it (CashBookName) for display on the result row.
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
    public class VAS_069_CashJournalSearchWidgetController : Controller
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

            string baseSql = @"SELECT c.C_Cash_ID, c.DocumentNo, c.Name, c.Description, c.VAS_Label, c.C_CashBook_ID,
                       c.EndingBalance, c.C_Currency_ID, c.DateAcct, c.C_DocType_ID, c.DocStatus, c.AD_Client_ID, c.AD_Org_ID, c.StatementDate
                  FROM C_Cash c
                 WHERE c.IsActive = 'Y' AND c.AD_Client_ID = " + clientId;
            baseSql = MRole.GetDefault(ctx).AddAccessSQL(baseSql, "c", MRole.SQL_FULLYQUALIFIED, MRole.SQL_RO);

            // Lookup tables (C_DocType / C_Currency / cashbook) join outside MRole and
            // feed the searchable text (doc-type name, currency code, cashbook name).
            // The cash journal's partner lives on its lines (C_CashLine), so a line
            // vendor match is added via EXISTS using a second bound term (@Q2 = @Q1).
            string core = @"SELECT b.C_Cash_ID AS RecordId, b.DocumentNo AS DocumentNo, b.Name AS Title,
                       cb.Name AS CashBookName,
                       COALESCE(currencyConvert(b.EndingBalance, b.C_Currency_ID, " + schemaCurrencyId + @", b.DateAcct, NULL, b.AD_Client_ID, b.AD_Org_ID), 0) AS Amount,
                       b.StatementDate AS DocDate, b.DocStatus AS DocStatus,
                       CASE WHEN LOWER(b.DocumentNo) = @QExact THEN 4
                            WHEN LOWER(b.DocumentNo) LIKE @QStart THEN 3
                            WHEN LOWER(b.DocumentNo) LIKE @QDoc THEN 2
                            ELSE 1 END AS Relevance,
                       (SELECT MIN(inv.DocumentNo) FROM C_CashLine cl3
                          INNER JOIN C_Invoice inv ON (inv.C_Invoice_ID = cl3.C_Invoice_ID)
                          WHERE cl3.C_Cash_ID = b.C_Cash_ID AND cl3.IsActive = 'Y' AND inv.IsActive = 'Y'
                            AND LOWER(COALESCE(inv.DocumentNo, " + empty + ") || " + sep + " || COALESCE(inv.InvoiceReference, " + empty + ") || " + sep + " || COALESCE(inv.POReference, " + empty + @")) LIKE @QInv) AS MatchedInvoiceNo
                  FROM (" + baseSql + @") b
                 LEFT OUTER JOIN C_CashBook cb ON (cb.C_CashBook_ID = b.C_CashBook_ID)
                 LEFT OUTER JOIN C_DocType dt ON (dt.C_DocType_ID = b.C_DocType_ID)
                 LEFT OUTER JOIN C_Currency cur ON (cur.C_Currency_ID = b.C_Currency_ID)
                 WHERE LOWER(b.DocumentNo || " + sep + " || COALESCE(b.Name, " + empty + ") || " + sep + " || COALESCE(b.Description, " + empty + ") || " + sep + " || COALESCE(b.VAS_Label, " + empty + ") || " + sep + " || COALESCE(cb.Name, " + empty + ") || " + sep + " || COALESCE(dt.Name, " + empty + ") || " + sep + " || COALESCE(dt.PrintName, " + empty + ") || " + sep + " || COALESCE(cur.ISO_Code, " + empty + @")) LIKE @Q1
                    OR EXISTS (SELECT 1 FROM C_CashLine cl
                                INNER JOIN C_BPartner lbp ON (lbp.C_BPartner_ID = cl.C_BPartner_ID)
                                WHERE cl.C_Cash_ID = b.C_Cash_ID AND cl.IsActive = 'Y'
                                  AND LOWER(COALESCE(lbp.Name, " + empty + ") || " + sep + " || COALESCE(lbp.Value, " + empty + @")) LIKE @Q2)
                    OR EXISTS (SELECT 1 FROM C_CashLine cl2
                                INNER JOIN C_Invoice inv ON (inv.C_Invoice_ID = cl2.C_Invoice_ID)
                                WHERE cl2.C_Cash_ID = b.C_Cash_ID AND cl2.IsActive = 'Y' AND inv.IsActive = 'Y'
                                  AND LOWER(COALESCE(inv.DocumentNo, " + empty + ") || " + sep + " || COALESCE(inv.InvoiceReference, " + empty + ") || " + sep + " || COALESCE(inv.POReference, " + empty + @")) LIKE @Q3)";

            if (isPg)
            {
                strQuery = core + " ORDER BY Relevance DESC, DocDate DESC, RecordId DESC LIMIT " + maxRows + " OFFSET " + offset;
            }
            else
            {
                strQuery = "SELECT * FROM (SELECT t.*, ROWNUM AS rn FROM (" + core + " ORDER BY Relevance DESC, DocDate DESC, RecordId DESC) t WHERE ROWNUM <= " + (offset + maxRows) + ") WHERE rn > " + offset;
            }

            // Order matches textual appearance: relevance params (SELECT CASE), then
            // @Q1 (header WHERE), then @Q2 (cash-line vendor EXISTS).
            string like = "%" + term + "%";
            SqlParameter[] p = {
                new SqlParameter("@QExact", term),
                new SqlParameter("@QStart", term + "%"),
                new SqlParameter("@QDoc", like),
                new SqlParameter("@QInv", like),
                new SqlParameter("@Q1", like),
                new SqlParameter("@Q2", like),
                new SqlParameter("@Q3", like)
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
                        CashBookName = Util.GetValueOfString(row["CashBookName"]),
                        Amount     = Util.GetValueOfDecimal(row["Amount"]),
                        DocDate    = d.HasValue ? d.Value.ToString("yyyy-MM-dd") : "",
                        DocStatus  = Util.GetValueOfString(row["DocStatus"]),
                        MatchedInvoiceNo = Util.GetValueOfString(row["MatchedInvoiceNo"])
                    });
                }
            }
            return result;
        }

        /// <summary>Header window for zoom: VAS_CashJournal, else standard "Cash Journal".</summary>
        private int ResolveWindowId()
        {
            if (_windowId >= 0) { return _windowId; }
            int id = GetWindowIdByName("VAS_CashJournal");
            if (id == 0) { id = GetWindowIdByName("Cash Journal"); }
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
            /// <summary>C_CashBook.Name of the journal's cash book (shown on the result row).</summary>
            public string  CashBookName { get; set; }
            public decimal Amount     { get; set; }
            public string  DocDate    { get; set; }
            public string  DocStatus  { get; set; }
            public string  MatchedInvoiceNo { get; set; }
        }
    }
}
