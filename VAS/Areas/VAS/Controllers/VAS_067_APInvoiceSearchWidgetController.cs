/************************************************************
 * Module Name    : VAS
 * Purpose        : Controller for the AP Invoice Search widget.
 *                  Full-width dashboard search bar that searches
 *                  vendor (AP) invoices (C_Invoice, IsSOTrx = 'N')
 *                  and returns the most relevant matches. Clicking a
 *                  result zooms to the invoice record.
 * chronological  : Development
 * Created Date   : 13 June 2026
 * Created by     : Claude (VAS widget pattern)
 *
 * Notes:
 *   - Read-only. Only SELECT queries are executed.
 *   - The free-text term is bound (@Q1 + relevance params @QExact /
 *     @QStart / @QDoc). Client id, schema currency id and row cap are
 *     validated integers, then inlined (no user text in SQL - rule 2).
 *   - MRole.AddAccessSQL is applied to the main physical table only
 *     (the join-free base sub-select aliased i) - rule 1.
 *   - IsActive = 'Y' on the query (rule 6); amounts converted to the
 *     client functional (schema) currency and StdPrecision read from
 *     it (rule 14). Results are ordered by relevance, then most recent.
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
    public class VAS_067_APInvoiceSearchWidgetController : Controller
    {
        string strQuery = "";
        private static int _windowId = -1; // resolved once per process (-1 = not resolved)

        [Authorize]
        public ActionResult Index()
        {
            return View();
        }

        /// <summary>
        /// Searches vendor (AP) invoices for the given free-text term and returns
        /// the most relevant hits, the functional currency symbol/precision, and the
        /// AD_Window_ID to zoom to.
        /// </summary>
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

            // Step 1 - functional currency from the primary accounting schema (rule 14).
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

            // MRole on the join-free single-table base (vendor invoices).
            string baseSql = @"SELECT i.C_Invoice_ID, i.DocumentNo, i.Description, i.POReference, i.InvoiceReference,
                       i.C_BPartner_ID, i.GrandTotal, i.C_Currency_ID, i.DateAcct, i.C_ConversionType_ID,
                       i.C_DocTypeTarget_ID, i.SalesRep_ID, i.DocStatus, i.AD_Client_ID, i.AD_Org_ID, i.DateInvoiced
                  FROM C_Invoice i
                 WHERE i.IsSOTrx = 'N' AND i.IsActive = 'Y' AND i.AD_Client_ID = " + clientId;
            baseSql = MRole.GetDefault(ctx).AddAccessSQL(baseSql, "i", MRole.SQL_FULLYQUALIFIED, MRole.SQL_RO);

            // Lookup tables (C_DocType / AD_User salesrep) are joined outside MRole
            // scope and their names added to the searchable text so the user can
            // search by document-type name or buyer - not by id.
            string core = @"SELECT b.C_Invoice_ID AS RecordId, b.DocumentNo AS DocumentNo, bp.Name AS Title,
                       COALESCE(currencyConvert(b.GrandTotal, b.C_Currency_ID, " + schemaCurrencyId + @", b.DateAcct, b.C_ConversionType_ID, b.AD_Client_ID, b.AD_Org_ID), 0) AS Amount,
                       b.DateInvoiced AS DocDate, b.DocStatus AS DocStatus,
                       CASE WHEN LOWER(b.DocumentNo) = @QExact THEN 4
                            WHEN LOWER(b.DocumentNo) LIKE @QStart THEN 3
                            WHEN LOWER(b.DocumentNo) LIKE @QDoc THEN 2
                            ELSE 1 END AS Relevance
                  FROM (" + baseSql + @") b
                 INNER JOIN C_BPartner bp ON (bp.C_BPartner_ID = b.C_BPartner_ID)
                 LEFT OUTER JOIN C_DocType dt ON (dt.C_DocType_ID = b.C_DocTypeTarget_ID)
                 LEFT OUTER JOIN AD_User u ON (u.AD_User_ID = b.SalesRep_ID)
                 WHERE LOWER(b.DocumentNo || " + sep + " || COALESCE(b.POReference, " + empty + ") || " + sep + " || COALESCE(b.InvoiceReference, " + empty + ") || " + sep + " || COALESCE(b.Description, " + empty + ") || " + sep + " || COALESCE(bp.Name, " + empty + ") || " + sep + " || COALESCE(bp.Value, " + empty + ") || " + sep + " || COALESCE(dt.Name, " + empty + ") || " + sep + " || COALESCE(dt.PrintName, " + empty + ") || " + sep + " || COALESCE(u.Name, " + empty + @")) LIKE @Q1";

            if (isPg)
            {
                strQuery = core + " ORDER BY Relevance DESC, DocDate DESC, RecordId DESC LIMIT " + maxRows + " OFFSET " + offset;
            }
            else
            {
                strQuery = "SELECT * FROM (SELECT t.*, ROWNUM AS rn FROM (" + core + " ORDER BY Relevance DESC, DocDate DESC, RecordId DESC) t WHERE ROWNUM <= " + (offset + maxRows) + ") WHERE rn > " + offset;
            }

            // Relevance params (doc-no exact / prefix / contains) come first textually
            // (in the SELECT CASE), then @Q1 in the WHERE - array order matches.
            string like = "%" + term + "%";
            SqlParameter[] p = {
                new SqlParameter("@QExact", term),
                new SqlParameter("@QStart", term + "%"),
                new SqlParameter("@QDoc", like),
                new SqlParameter("@Q1", like)
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

        /// <summary>Header window for zoom: VAS_APInvoice, else standard "Invoice (Vendor)".</summary>
        private int ResolveWindowId()
        {
            if (_windowId >= 0) { return _windowId; }
            int id = GetWindowIdByName("VAS_APInvoice");
            if (id == 0) { id = GetWindowIdByName("Invoice (Vendor)"); }
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
