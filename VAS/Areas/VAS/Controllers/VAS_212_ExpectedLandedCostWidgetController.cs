/************************************************************
 * Module Name    : VAS
 * Purpose        : Expected Landed Cost on PO Widget (Purchase Order Dashboard)
 *                  Provides aggregated expected landed cost amounts by cost element
 *                  (M_CostElement) for open purchase orders in the selected period.
 * Prefix         : VAS_212_
 * Chronological  : Development
 * Created Date   : 17 Aug 2026
 * Created by     : AI Builder Agent 10
 ***********************************************************/
using CoreLibrary.DataBase;
using Newtonsoft.Json;
using System;
using System.Collections.Generic;
using System.Data;
using System.Data.SqlClient;
using System.Linq;
using System.Web.Mvc;
using VAdvantage.Classes;
using VAdvantage.Logging;
using VAdvantage.Model;
using VAdvantage.Utility;
using VIS.Filters;

namespace VIS.Controllers
{
    /// <summary>
    /// Controller for VAS_212_ExpectedLandedCostWidget (Expected Landed Cost on PO).
    /// Read-only expected cost summary grouped by dynamic M_CostElement for open POs.
    /// Converts amounts server-side to the tenant accounting currency.
    /// </summary>
    public class VAS_212_ExpectedLandedCostWidgetController : Controller
    {
        private static readonly VLogger _log = VLogger.GetVLogger(typeof(VAS_212_ExpectedLandedCostWidgetController).FullName);

        [Authorize]
        public ActionResult Index()
        {
            return View();
        }

        /// <summary>
        /// Returns expected landed cost data grouped by cost element for open purchase orders
        /// within the specified month and year.
        /// </summary>
        /// <param name="month">1-12 month index</param>
        /// <param name="year">4-digit year</param>
        /// <returns>JSON result with total expected cost, open PO count, currency info, and cost elements list.</returns>
        [AjaxAuthorizeAttribute]
        [AjaxSessionFilterAttribute]
        public JsonResult GetExpectedLandedCost(int month, int year)
        {
            if (Session["ctx"] == null)
            {
                return Json(new
                {
                    error = Msg.GetMsg(Env.GetCtx(), "SessionExpired") ?? "Session Expired"
                }, JsonRequestBehavior.AllowGet);
            }

            Ctx ctx = Session["ctx"] as Ctx;
            int clientId = ctx.GetAD_Client_ID();

            int selectedYear = year > 0 ? year : DateTime.Now.Year;
            int selectedMonth = month > 0 ? month : DateTime.Now.Month;
            DateTime monthStart = new DateTime(selectedYear, selectedMonth, 1);
            DateTime monthEndExclusive = monthStart.AddMonths(1);

            try
            {
                // Step 1: Functional accounting currency from accounting schema
                int schemaCurrencyId = 0;
                string curSymbol = "";
                string curIso = "";
                int stdPrecision = 2;

                string curSql = @"SELECT cs.C_Currency_ID, c.CurSymbol, c.ISO_Code, c.StdPrecision
                                    FROM C_AcctSchema cs
                                   INNER JOIN AD_ClientInfo ci ON (ci.C_AcctSchema1_ID = cs.C_AcctSchema_ID)
                                   INNER JOIN C_Currency c ON (cs.C_Currency_ID = c.C_Currency_ID)
                                   WHERE ci.AD_Client_ID = @ClientID
                                     AND ci.IsActive = 'Y'
                                     AND cs.IsActive = 'Y'
                                     AND c.IsActive = 'Y'";

                curSql = MRole.GetDefault(ctx).AddAccessSQL(curSql, "cs", MRole.SQL_FULLYQUALIFIED, MRole.SQL_RO);

                SqlParameter[] clientParam = new SqlParameter[] { new SqlParameter("@ClientID", clientId) };
                DataSet cDs = DB.ExecuteDataset(curSql, clientParam, null);
                if (cDs != null && cDs.Tables.Count > 0 && cDs.Tables[0].Rows.Count > 0)
                {
                    schemaCurrencyId = Util.GetValueOfInt(cDs.Tables[0].Rows[0]["C_Currency_ID"]);
                    curSymbol = Util.GetValueOfString(cDs.Tables[0].Rows[0]["CurSymbol"]);
                    curIso = Util.GetValueOfString(cDs.Tables[0].Rows[0]["ISO_Code"]);
                    stdPrecision = Util.GetValueOfInt(cDs.Tables[0].Rows[0]["StdPrecision"]);
                }

                // Step 2: Portable query for Expected Landed Cost on Open POs
                string sql = @"
                    WITH order_qty AS (
                        SELECT
                            o.C_Order_ID,
                            SUM(COALESCE(ol.QtyOrdered, 0)) AS ordered_qty,
                            SUM(COALESCE(ol.QtyDelivered, 0)) AS delivered_qty
                        FROM C_Order o
                        LEFT JOIN C_OrderLine ol
                            ON ol.C_Order_ID = o.C_Order_ID
                           AND ol.IsActive = 'Y'
                        WHERE o.AD_Client_ID = @ClientID
                          AND o.IsActive = 'Y'
                          AND o.IsSOTrx = 'N'
                          AND COALESCE(o.IsReturnTrx, 'N') = 'N'
                          AND o.DateOrdered >= @MonthStart
                          AND o.DateOrdered < @MonthEndExclusive
                          AND o.DocStatus IN ('DR', 'IP', 'CO')
                        GROUP BY o.C_Order_ID
                    )
                    SELECT
                        ec.C_ExpectedCost_ID AS expected_cost_id,
                        ec.C_Order_ID AS purchase_order_id,
                        o.DateOrdered AS order_date,
                        o.DocStatus AS document_status,
                        o.AD_Org_ID AS org_id,
                        ec.M_CostElement_ID AS cost_element_id,
                        ce.Name AS cost_element_name,
                        ec.Amt AS entered_amount,
                        ec.C_Currency_ID AS entered_currency_id,
                        ec.C_ConversionType_ID AS conversion_type_id,
                        oq.ordered_qty,
                        oq.delivered_qty
                    FROM C_ExpectedCost ec
                    INNER JOIN C_Order o
                        ON o.C_Order_ID = ec.C_Order_ID
                    INNER JOIN order_qty oq
                        ON oq.C_Order_ID = o.C_Order_ID
                    INNER JOIN M_CostElement ce
                        ON ce.M_CostElement_ID = ec.M_CostElement_ID
                       AND ce.IsActive = 'Y'
                    WHERE ec.IsActive = 'Y'
                      AND (
                           o.DocStatus IN ('DR', 'IP')
                           OR (
                               o.DocStatus = 'CO'
                               AND COALESCE(oq.ordered_qty, 0) > COALESCE(oq.delivered_qty, 0)
                           )
                      )
                    ORDER BY ce.Name, ec.C_ExpectedCost_ID";

                SqlParameter[] queryParams = new SqlParameter[]
                {
                    new SqlParameter("@ClientID", clientId),
                    new SqlParameter("@MonthStart", monthStart),
                    new SqlParameter("@MonthEndExclusive", monthEndExclusive)
                };

                Dictionary<int, CostElementItem> elementMap = new Dictionary<int, CostElementItem>();
                HashSet<int> distinctOrderIds = new HashSet<int>();
                decimal grandTotal = 0;

                DataSet ds = DB.ExecuteDataset(sql, queryParams, null);
                if (ds != null && ds.Tables.Count > 0)
                {
                    foreach (DataRow row in ds.Tables[0].Rows)
                    {
                        int orderId = Util.GetValueOfInt(row["purchase_order_id"]);
                        int costElementId = Util.GetValueOfInt(row["cost_element_id"]);
                        string costElementName = Util.GetValueOfString(row["cost_element_name"]);
                        decimal enteredAmt = Util.GetValueOfDecimal(row["entered_amount"]);
                        int enteredCurId = Util.GetValueOfInt(row["entered_currency_id"]);
                        int convTypeId = Util.GetValueOfInt(row["conversion_type_id"]);
                        DateTime orderDate = Util.GetValueOfDateTime(row["order_date"]).Value;
                        int orgId = Util.GetValueOfInt(row["org_id"]);

                        decimal convertedAmt = enteredAmt;
                        if (enteredCurId > 0 && schemaCurrencyId > 0 && enteredCurId != schemaCurrencyId)
                        {
                            try
                            {
                                convertedAmt = MConversionRate.Convert(
                                    ctx, enteredAmt, enteredCurId, schemaCurrencyId,
                                    orderDate, convTypeId, clientId, orgId);
                            }
                            catch (Exception ex)
                            {
                                _log.Warning("MConversionRate.Convert failed for C_ExpectedCost_ID " + Util.GetValueOfInt(row["expected_cost_id"]) + ": " + ex.Message);
                                convertedAmt = enteredAmt;
                            }
                        }

                        grandTotal += convertedAmt;
                        distinctOrderIds.Add(orderId);

                        if (!elementMap.ContainsKey(costElementId))
                        {
                            elementMap[costElementId] = new CostElementItem
                            {
                                costElementId = costElementId,
                                costElementName = costElementName,
                                totalAmount = 0
                            };
                        }
                        elementMap[costElementId].totalAmount += convertedAmt;
                    }
                }

                List<CostElementItem> sortedElements = elementMap.Values
                    .OrderByDescending(e => e.totalAmount)
                    .ThenBy(e => e.costElementName)
                    .ToList();

                var result = new
                {
                    totalExpectedCost = grandTotal,
                    openPOCount = distinctOrderIds.Count,
                    curSymbol = curSymbol,
                    curIso = curIso,
                    stdPrecision = stdPrecision,
                    costElements = sortedElements,
                    success = true
                };

                return Json(JsonConvert.SerializeObject(result), JsonRequestBehavior.AllowGet);
            }
            catch (Exception ex)
            {
                _log.Log(Level.SEVERE, "VAS_212_ExpectedLandedCostWidget.GetExpectedLandedCost", ex);
                return Json(JsonConvert.SerializeObject(new
                {
                    error = Msg.GetMsg(ctx, "Error") ?? "Error",
                    message = ex.Message
                }), JsonRequestBehavior.AllowGet);
            }
        }

        private class CostElementItem
        {
            public int costElementId { get; set; }
            public string costElementName { get; set; }
            public decimal totalAmount { get; set; }
        }
    }
}
