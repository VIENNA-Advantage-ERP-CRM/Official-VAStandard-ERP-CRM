/************************************************************
 * Module Name    : VAS
 * Purpose        : VAS_305 - Requisition Search (9x1) on the Requisition screen.
 *                  Finds requisitions by document number, description, requester (creator when no requester), warehouse,
 *                  product name / code / SKU or charge of a line. Results open the requisition
 *                  popup; the search never filters the other widgets on the screen.
 *                  LOWER(col) LIKE with bound parameters - same on Oracle and PostgreSQL. No COALESCE on text
 *                  columns: they are NVARCHAR2 on Oracle and a plain literal raises ORA-12704; a NULL simply does not match.
 * Author         : VAS
 * Date           : 05 November 2026
 ***********************************************************/

using Newtonsoft.Json;
using System;
using System.Collections.Generic;
using System.Data;
using System.Data.SqlClient;
using System.Linq;
using System.Text;
using System.Web.Mvc;
using VAdvantage.DataBase;
using VAdvantage.Logging;
using VAdvantage.Utility;
using VIS.Filters;

namespace VIS.Controllers
{
    public class VAS_305_RequisitionSearchWidgetController : Controller
    {
        private const int MaxMatches = 500;
        private const int PageSize = 15;

        /// <summary>Requisitions matching the term, newest first, one page at a time.</summary>
        /// <param name="query">Search text (at least 2 characters)</param>
        /// <param name="offset">Rows to skip (paging)</param>
        /// <param name="maxRows">Page size</param>
        [AjaxAuthorizeAttribute]
        [AjaxSessionFilterAttribute]
        public JsonResult Search(string query = null, int offset = 0, int maxRows = PageSize)
        {
            Ctx ctx = Session["ctx"] as Ctx;
            if (ctx == null)
            {
                return VAS_ReqWidgetData.Error(Env.GetCtx(), "SessionExpired", "Session expired.");
            }
            string term = (query ?? "").Trim();
            if (term.Length < 2)
            {
                return Json(JsonConvert.SerializeObject(new { total = 0, items = new object[0] }), JsonRequestBehavior.AllowGet);
            }
            if (maxRows <= 0 || maxRows > 50) { maxRows = PageSize; }
            if (offset < 0) { offset = 0; }

            try
            {
                string like = "%" + term.ToLowerInvariant() + "%";
                bool hasUser = VAS_ReqWidgetData.HasColumn("M_Requisition", "AD_User_ID");

                StringBuilder sql = new StringBuilder(@"
                    SELECT r.M_Requisition_ID, r.DocumentNo, r.DocStatus, r.DateDoc, r.DateRequired, r.TotalLines,
                           w.Name AS WarehouseName, cu.Name AS CreatedByName");
                sql.Append(hasUser ? ", u.Name AS RequesterName" : ", cu.Name AS RequesterName");
                sql.Append(@"
                    FROM M_Requisition r
                    LEFT JOIN M_Warehouse w ON w.M_Warehouse_ID = r.M_Warehouse_ID
                    LEFT JOIN AD_User cu ON cu.AD_User_ID = r.CreatedBy");
                if (hasUser) { sql.Append(" LEFT JOIN AD_User u ON u.AD_User_ID = r.AD_User_ID"); }
                sql.Append(@"
                    WHERE r.AD_Client_ID = @RQ_Client AND r.IsActive = 'Y'
                      AND r.M_Requisition_ID IN (" + VAS_ReqWidgetData.AccessSql(ctx) + @")
                      AND (LOWER(r.DocumentNo) LIKE @RQ_S1
                           OR LOWER(r.Description) LIKE @RQ_S2
                           OR LOWER(w.Name) LIKE @RQ_S4");
                // requester; the creator counts only when no requester is set (same rule as My Requisitions)
                if (hasUser) { sql.Append(" OR LOWER(u.Name) LIKE @RQ_S5 OR (r.AD_User_ID IS NULL AND LOWER(cu.Name) LIKE @RQ_S3)"); }
                else { sql.Append(" OR LOWER(cu.Name) LIKE @RQ_S3"); }
                sql.Append(@"
                           OR EXISTS (SELECT 1 FROM M_RequisitionLine sl
                                      LEFT JOIN M_Product sp ON sp.M_Product_ID = sl.M_Product_ID
                                      LEFT JOIN C_Charge sc ON sc.C_Charge_ID = sl.C_Charge_ID
                                      WHERE sl.M_Requisition_ID = r.M_Requisition_ID AND sl.IsActive = 'Y'
                                        AND (LOWER(sp.Name) LIKE @RQ_S6
                                             OR LOWER(sp.Value) LIKE @RQ_S7
                                             OR LOWER(sp.SKU) LIKE @RQ_S8
                                             OR LOWER(sc.Name) LIKE @RQ_S9)))
                    ORDER BY r.DateDoc DESC, r.DocumentNo DESC");

                List<SqlParameter> p = new List<SqlParameter> { new SqlParameter("@RQ_Client", ctx.GetAD_Client_ID()) };
                for (int i = 1; i <= 9; i++)
                {
                    if (i == 5 && !hasUser) { continue; }
                    p.Add(new SqlParameter("@RQ_S" + i, like));
                }

                DataSet ds = DB.ExecuteDataset(sql.ToString(), p.ToArray(), null);
                List<DataRow> all = (ds != null && ds.Tables.Count > 0) ? ds.Tables[0].Rows.Cast<DataRow>().Take(MaxMatches).ToList() : new List<DataRow>();
                List<DataRow> page = all.Skip(offset).Take(maxRows).ToList();

                // matched product per requisition on this page (ids are integers read above)
                Dictionary<int, string> matchedItem = new Dictionary<int, string>();
                Dictionary<int, int> lineCount = new Dictionary<int, int>();
                if (page.Count > 0)
                {
                    string ids = string.Join(",", page.Select(r => Util.GetValueOfInt(r["M_Requisition_ID"])));
                    DataSet lds = DB.ExecuteDataset(@"SELECT sl.M_Requisition_ID, sp.Name, sp.Value, sp.SKU, sc.Name AS ChargeName
                            FROM M_RequisitionLine sl
                            LEFT JOIN M_Product sp ON sp.M_Product_ID = sl.M_Product_ID
                            LEFT JOIN C_Charge sc ON sc.C_Charge_ID = sl.C_Charge_ID
                            WHERE sl.IsActive = 'Y' AND sl.M_Requisition_ID IN (" + ids + ") ORDER BY sl.M_Requisition_ID, sl.Line", null, null);
                    string t = term.ToLowerInvariant();
                    if (lds != null && lds.Tables.Count > 0)
                    {
                        foreach (DataRow lr in lds.Tables[0].Rows)
                        {
                            int rid = Util.GetValueOfInt(lr["M_Requisition_ID"]);
                            lineCount[rid] = (lineCount.ContainsKey(rid) ? lineCount[rid] : 0) + 1;
                            string name = Util.GetValueOfString(lr["Name"]);
                            string item = string.IsNullOrEmpty(name) ? Util.GetValueOfString(lr["ChargeName"]) : name;
                            if (!matchedItem.ContainsKey(rid))
                            {
                                matchedItem[rid] = item; // first line as default
                            }
                            bool hit = (name ?? "").ToLowerInvariant().Contains(t)
                                || Util.GetValueOfString(lr["Value"]).ToLowerInvariant().Contains(t)
                                || Util.GetValueOfString(lr["SKU"]).ToLowerInvariant().Contains(t)
                                || Util.GetValueOfString(lr["ChargeName"]).ToLowerInvariant().Contains(t);
                            if (hit && !matchedItem.ContainsKey(-rid))
                            {
                                matchedItem[-rid] = item; // first matching line wins
                            }
                        }
                    }
                }

                var items = page.Select(r =>
                {
                    int rid = Util.GetValueOfInt(r["M_Requisition_ID"]);
                    string requester = Util.GetValueOfString(r["RequesterName"]);
                    if (string.IsNullOrEmpty(requester)) { requester = Util.GetValueOfString(r["CreatedByName"]); }
                    string docStatus = Util.GetValueOfString(r["DocStatus"]);
                    return new
                    {
                        requisitionId = rid,
                        documentNo = Util.GetValueOfString(r["DocumentNo"]),
                        docStatus,
                        docStatusName = VAS_ReqWidgetData.DocStatusName(ctx, docStatus),
                        dateDoc = VAS_ReqWidgetData.D(Util.GetValueOfDateTime(r["DateDoc"])),
                        needBy = VAS_ReqWidgetData.D(Util.GetValueOfDateTime(r["DateRequired"])),
                        requester,
                        warehouse = Util.GetValueOfString(r["WarehouseName"]),
                        item = matchedItem.ContainsKey(-rid) ? matchedItem[-rid] : (matchedItem.ContainsKey(rid) ? matchedItem[rid] : ""),
                        itemMatched = matchedItem.ContainsKey(-rid),
                        lines = lineCount.ContainsKey(rid) ? lineCount[rid] : 0,
                        total = Util.GetValueOfDecimal(r["TotalLines"])
                    };
                }).ToList();

                return Json(JsonConvert.SerializeObject(new { total = all.Count, offset, items }), JsonRequestBehavior.AllowGet);
            }
            catch (Exception ex)
            {
                VAS_ReqWidgetData.Log.Log(Level.SEVERE, "VAS_305_RequisitionSearchWidget.Search", ex);
                return VAS_ReqWidgetData.Error(ctx, "VAS_305_LoadError", "Could not load the data.");
            }
        }
    }
}
