using Newtonsoft.Json;
using System;
using System.Data;
using System.Data.SqlClient;
using System.Web.Mvc;
using VAdvantage.Classes;
using VAdvantage.DataBase;
using VAdvantage.Model;
using VAdvantage.Utility;
using VIS.Filters;

namespace VIS.Controllers
{
    /// <summary>
    /// Module Name : Counted MTD Widget (Inventory Count / Physical Inventory dashboard KPI)
    /// Purpose     : Read-only 2x1 summary card showing total count LINES completed month-to-date
    ///               (M_Inventory, M_InventoryLine) and distinct products counted.
    /// Prefix      : VAS_000_
    ///
    /// ORGANISATION SCOPE
    /// Both endpoints filter on the logged-in organisation. The source prompt
    /// (01-counted-mtd-claude-prompt.txt) requires it in three places - the approved mapping
    /// ("Organization: M_Inventory.AD_Org_ID"), the reference SQL ("AND i.AD_Org_ID = :p_org_id")
    /// and the acceptance checks ("Both metrics use CO/CL, MovementDate, client, organization,
    /// active, and IsInternalUse filters identically") - but it was missing from both queries,
    /// which therefore aggregated across every organisation the ROLE can read rather than the one
    /// the user is logged into.
    ///
    /// The literal "= :p_org_id" is not usable as written: a login on the "*" organisation has
    /// AD_Org_ID 0, and an equality test would then match only records literally owned by org 0.
    /// The IN (0, COALESCE(NULLIF(...))) form is the idiom already used by VAS_073, VAS_078,
    /// VAS_079 and VAS_080 - it scopes to the login organisation, and falls through to no
    /// restriction when the login organisation is "*".
    ///
    /// The HEADLINE REMAINS A COUNT OF LINES, not of documents. The prompt states this three
    /// times (goal, approved business rules, acceptance checks). A count-sheet screen listing
    /// 46 documents and this card reading 53 is therefore not a contradiction - 46 documents
    /// carry 53 lines between them.
    /// </summary>
    public class VAS_156_CountedMTDWidgetController : Controller
    {
        /// <summary>
        /// Gets month-to-date counted lines and distinct products count for the active context.
        /// </summary>
        /// <returns>JSON { countedLines, productsCounted }</returns>
        [AjaxAuthorizeAttribute]
        [AjaxSessionFilterAttribute]
        public JsonResult GetCountedMTDData()
        {
            if (Session["ctx"] == null)
            {
                return Json(new
                {
                    error = Msg.GetMsg(Env.GetCtx(), "SessionExpired") ?? "Session Expired"
                }, JsonRequestBehavior.AllowGet);
            }

            Ctx ctx = Session["ctx"] as Ctx;

            DateTime now = DateTime.Now;
            DateTime monthStart = new DateTime(now.Year, now.Month, 1);
            DateTime nextMonthStart = monthStart.AddMonths(1);

            string sql = @"
                SELECT
                    COUNT(*) AS counted_lines,
                    COUNT(DISTINCT il.M_Product_ID) AS products_counted
                FROM M_Inventory i
                JOIN M_InventoryLine il
                    ON il.M_Inventory_ID = i.M_Inventory_ID
                WHERE i.IsActive = 'Y'
                  AND il.IsActive = 'Y'
                  AND COALESCE(i.IsInternalUse, 'N') = 'N'
                  AND i.DocStatus IN ('CO', 'CL')
                  AND i.MovementDate >= @MonthStart
                  AND i.MovementDate < @NextMonthStart
                  AND i.AD_Org_ID IN (0, COALESCE(NULLIF(@OrgId, 0), i.AD_Org_ID))";

            sql = MRole.GetDefault(ctx).AddAccessSQL(
                sql,
                "i",
                MRole.SQL_FULLYQUALIFIED,
                MRole.SQL_RO
            );

            SqlParameter[] parameters =
            {
                new SqlParameter("@MonthStart", monthStart),
                new SqlParameter("@NextMonthStart", nextMonthStart),
                new SqlParameter("@OrgId", ctx.GetAD_Org_ID())
            };

            IDataReader dr = null;

            try
            {
                int countedLines = 0;
                int productsCounted = 0;

                dr = DB.ExecuteReader(sql, parameters);
                if (dr != null && dr.Read())
                {
                    countedLines = Util.GetValueOfInt(dr["counted_lines"]);
                    productsCounted = Util.GetValueOfInt(dr["products_counted"]);
                }

                var result = new
                {
                    countedLines = countedLines,
                    productsCounted = productsCounted
                };

                return Json(JsonConvert.SerializeObject(result), JsonRequestBehavior.AllowGet);
            }
            catch (Exception ex)
            {
                return Json(new { error = ex.Message }, JsonRequestBehavior.AllowGet);
            }
            finally
            {
                if (dr != null)
                {
                    dr.Close();
                    dr.Dispose();
                }
            }
        }

        /// <summary>
        /// Per-document breakdown behind the headline number: one row per completed/closed inventory
        /// count document in the current month, with its line count and distinct-product count.
        /// Feeds the drill-down modal, and lets the two headline figures be reconciled against the
        /// documents that produce them (SUM of LineCount = countedLines; the product totals do NOT
        /// sum, because one product can appear on several documents - see the note below).
        /// </summary>
        [AjaxAuthorizeAttribute]
        [AjaxSessionFilterAttribute]
        public JsonResult GetCountedMTDDetails()
        {
            if (Session["ctx"] == null)
            {
                return Json(new { error = Msg.GetMsg(Env.GetCtx(), "SessionExpired") ?? "Session Expired" }, JsonRequestBehavior.AllowGet);
            }

            Ctx ctx = Session["ctx"] as Ctx;

            DateTime now = DateTime.Now;
            DateTime monthStart = new DateTime(now.Year, now.Month, 1);
            DateTime nextMonthStart = monthStart.AddMonths(1);

            // Role access is applied to the inner plain header SELECT. Applying it to the outer
            // aggregate would append the predicate after GROUP BY / ORDER BY and break the statement.
            string accessibleHeaders = @"
                    SELECT inv.M_Inventory_ID, inv.DocumentNo, inv.MovementDate, inv.DocStatus, inv.M_Warehouse_ID
                    FROM M_Inventory inv
                    WHERE inv.IsActive = 'Y'
                      AND COALESCE(inv.IsInternalUse, 'N') = 'N'
                      AND inv.DocStatus IN ('CO', 'CL')
                      AND inv.MovementDate >= @MonthStart
                      AND inv.MovementDate < @NextMonthStart
                      AND inv.AD_Org_ID IN (0, COALESCE(NULLIF(@OrgId, 0), inv.AD_Org_ID))";

            accessibleHeaders = MRole.GetDefault(ctx).AddAccessSQL(accessibleHeaders, "inv", MRole.SQL_FULLYQUALIFIED, MRole.SQL_RO);

            string sql = @"
                SELECT
                    i.M_Inventory_ID,
                    i.DocumentNo,
                    w.Name AS WarehouseName,
                    i.MovementDate,
                    i.DocStatus,
                    COUNT(il.M_InventoryLine_ID) AS LineCount,
                    COUNT(DISTINCT il.M_Product_ID) AS ProductCount
                FROM (" + accessibleHeaders + @") i
                JOIN M_InventoryLine il
                    ON il.M_Inventory_ID = i.M_Inventory_ID
                   AND il.IsActive = 'Y'
                LEFT JOIN M_Warehouse w
                    ON w.M_Warehouse_ID = i.M_Warehouse_ID
                GROUP BY i.M_Inventory_ID, i.DocumentNo, w.Name, i.MovementDate, i.DocStatus
                ORDER BY i.MovementDate DESC, i.M_Inventory_ID DESC";

            SqlParameter[] parameters =
            {
                new SqlParameter("@MonthStart", monthStart),
                new SqlParameter("@NextMonthStart", nextMonthStart),
                new SqlParameter("@OrgId", ctx.GetAD_Org_ID())
            };

            IDataReader dr = null;
            var rows = new System.Collections.Generic.List<object>();

            try
            {
                dr = DB.ExecuteReader(sql, parameters);
                while (dr != null && dr.Read())
                {
                    DateTime? movementDate = Util.GetValueOfDateTime(dr["MovementDate"]);

                    rows.Add(new
                    {
                        InventoryId = Util.GetValueOfInt(dr["M_Inventory_ID"]),
                        DocumentNo = Util.GetValueOfString(dr["DocumentNo"]),
                        Warehouse = Util.GetValueOfString(dr["WarehouseName"]),
                        MovementDate = (movementDate.HasValue && movementDate.Value != DateTime.MinValue)
                            ? movementDate.Value.ToString("dd MMM") : "",
                        DocStatus = Util.GetValueOfString(dr["DocStatus"]),
                        Lines = Util.GetValueOfInt(dr["LineCount"]),
                        Products = Util.GetValueOfInt(dr["ProductCount"])
                    });
                }

                return Json(JsonConvert.SerializeObject(new { data = rows }), JsonRequestBehavior.AllowGet);
            }
            catch (Exception ex)
            {
                return Json(new { error = ex.Message }, JsonRequestBehavior.AllowGet);
            }
            finally
            {
                if (dr != null)
                {
                    dr.Close();
                    dr.Dispose();
                }
            }
        }
    }
}

