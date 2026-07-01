/************************************************************
 * Module Name    : VAS
 * Purpose        : Controller for the Material Receipt (GRN) Search widget.
 *                  Full-width dashboard search bar that searches vendor
 *                  goods receipts (M_InOut, IsSOTrx = 'N', MovementType
 *                  'V+') across multiple fields and returns the most
 *                  relevant matches. Clicking a result zooms to the receipt.
 * chronological  : Development
 *   <EmpCode>   2026-06-25 Created
 *
 * Notes:
 *   - Read-only. Only SELECT queries are executed.
 *   - The free-text term is bound (@Q1 + relevance params @QExact /
 *     @QStart / @QDoc). Client id and row cap are validated integers,
 *     then inlined (no user text in SQL - rule 2 / SQL injection).
 *   - MRole.AddAccessSQL is applied to the main physical table only
 *     (the join-free base sub-select aliased io) - rule 1.
 *   - IsActive = 'Y' on every table (rule 6). No currency: a receipt has
 *     no monetary total, so Amount carries the total received quantity and
 *     CurSymbol is left empty.
 *   - Runs on Oracle and PostgreSQL (dialect-branched literals / paging).
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
    public class VAS_091_MaterialReceiptSearchWidgetController : Controller
    {
        string strQuery = "";
        private static int _windowId = -1; // resolved once per process (-1 = not resolved)

        [Authorize]
        public ActionResult Index()
        {
            return View();
        }

        /// <summary>
        /// Searches vendor goods receipts (GRNs) for the given free-text term and
        /// returns the most relevant hits and the AD_Window_ID to zoom to.
        /// </summary>
        /// <param name="query">Free-text search term (min 2 chars).</param>
        /// <param name="maxRows">Page size (1-50).</param>
        /// <param name="offset">Row offset for infinite scroll.</param>
        /// <returns>Serialized DocSearchResult JSON.</returns>
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

        /// <summary>
        /// Builds the ranked result set: receipts whose document no., supplier,
        /// linked PO, PO reference, description, document type or warehouse match
        /// the term, ordered by relevance then most recent.
        /// </summary>
        /// <param name="ctx">Session context.</param>
        /// <param name="query">Free-text search term.</param>
        /// <param name="maxRows">Page size.</param>
        /// <param name="offset">Row offset.</param>
        /// <returns>The populated DocSearchResult.</returns>
        private DocSearchResult BuildResult(Ctx ctx, string query, int maxRows, int offset)
        {
            DocSearchResult result = new DocSearchResult();
            result.Items = new List<SearchHit>();
            result.WindowId = ResolveWindowId();
            result.CurSymbol = "";   // a receipt has no currency amount
            result.StdPrecision = 2; // quantity display precision

            string term = (query ?? "").Trim().ToLower();
            if (term.Length < 2) { return result; }
            if (maxRows < 1) { maxRows = 1; }
            if (maxRows > 50) { maxRows = 50; }
            if (offset < 0) { offset = 0; }
            if (offset > 100000) { offset = 100000; }

            int clientId = ctx.GetAD_Client_ID();

            bool isPg    = DB.IsPostgreSQL();
            string sep   = isPg ? "' '" : "N' '";
            string empty = isPg ? "''"  : "N''";

            // MRole on the join-free single-table base (vendor goods receipts).
            string baseSql = @"SELECT io.M_InOut_ID, io.DocumentNo, io.Description, io.POReference,
                       io.C_BPartner_ID, io.C_Order_ID, io.C_DocType_ID, io.M_Warehouse_ID,
                       io.MovementDate, io.DocStatus, io.AD_Client_ID, io.AD_Org_ID
                  FROM M_InOut io
                 WHERE io.IsSOTrx = 'N' AND io.MovementType = 'V+' AND io.IsActive = 'Y' AND io.AD_Client_ID = " + clientId;
            baseSql = MRole.GetDefault(ctx).AddAccessSQL(baseSql, "io", MRole.SQL_FULLYQUALIFIED, MRole.SQL_RO);

            // Lookup tables (supplier / linked PO / doc type / warehouse) are joined
            // outside MRole scope and their text added to the searchable string, so
            // the user can search by supplier name, PO no., doc type or warehouse.
            string core = @"SELECT b.M_InOut_ID AS RecordId, b.DocumentNo AS DocumentNo, bp.Name AS Title,
                       COALESCE((SELECT SUM(iol.MovementQty) FROM M_InOutLine iol WHERE iol.M_InOut_ID = b.M_InOut_ID AND iol.IsActive = 'Y'), 0) AS Amount,
                       b.MovementDate AS DocDate, b.DocStatus AS DocStatus,
                       CASE WHEN LOWER(b.DocumentNo) = @QExact THEN 4
                            WHEN LOWER(b.DocumentNo) LIKE @QStart THEN 3
                            WHEN LOWER(b.DocumentNo) LIKE @QDoc THEN 2
                            ELSE 1 END AS Relevance
                  FROM (" + baseSql + @") b
                 INNER JOIN C_BPartner bp ON (bp.C_BPartner_ID = b.C_BPartner_ID)
                 LEFT OUTER JOIN C_Order ord ON (ord.C_Order_ID = b.C_Order_ID)
                 LEFT OUTER JOIN C_DocType dt ON (dt.C_DocType_ID = b.C_DocType_ID)
                 LEFT OUTER JOIN M_Warehouse wh ON (wh.M_Warehouse_ID = b.M_Warehouse_ID)
                 WHERE LOWER(b.DocumentNo || " + sep + " || COALESCE(b.POReference, " + empty + ") || " + sep + " || COALESCE(b.Description, " + empty + ") || " + sep + " || COALESCE(bp.Name, " + empty + ") || " + sep + " || COALESCE(bp.Value, " + empty + ") || " + sep + " || COALESCE(ord.DocumentNo, " + empty + ") || " + sep + " || COALESCE(dt.Name, " + empty + ") || " + sep + " || COALESCE(wh.Name, " + empty + @")) LIKE @Q1";

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

        /// <summary>Header window for zoom: VAS_MaterialReceipt, else standard "Material Receipt".</summary>
        /// <returns>AD_Window_ID, or 0 when not found.</returns>
        private int ResolveWindowId()
        {
            if (_windowId >= 0) { return _windowId; }
            int id = GetWindowIdByName("VAS_MaterialReceipt");
            if (id == 0) { id = GetWindowIdByName("Material Receipt"); }
            _windowId = id;
            return _windowId;
        }

        /// <summary>Resolves an AD_Window_ID by exact (active) window name.</summary>
        /// <param name="name">Window name.</param>
        /// <returns>The minimum matching AD_Window_ID, or 0.</returns>
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
