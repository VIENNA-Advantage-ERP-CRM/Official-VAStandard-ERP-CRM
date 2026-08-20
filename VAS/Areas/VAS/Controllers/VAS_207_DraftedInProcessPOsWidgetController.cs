/************************************************************
 * Module Name    : VAS
 * Purpose        : Controller for Drafted / In-Process POs Widget (Purchase Order Dashboard)
 *                  Provides aggregate counts, server-side converted values, and
 *                  drill-down document records for Drafted ('DR') and In Progress ('IP') POs.
 * Chronological  : Development
 * Created Date   : 17 August 2026
 * Widget ID      : VAS_207_DraftedInProcessPOsWidget
 ***********************************************************/

using Newtonsoft.Json;
using System;
using System.Collections.Generic;
using System.Data;
using System.Data.SqlClient;
using System.Globalization;
using System.Web.Mvc;
using VAdvantage.Classes;
using VAdvantage.DataBase;
using VAdvantage.Logging;
using VAdvantage.Model;
using VAdvantage.Utility;
using VIS.Filters;

namespace VIS.Controllers
{
    /// <summary>
    /// Controller for Widget 05: Drafted / In-Process POs.
    /// Internal PO work queue containing only Drafted ('DR') and In Progress ('IP') Purchase Orders.
    /// </summary>
    public class VAS_207_DraftedInProcessPOsWidgetController : Controller
    {
        private static readonly VLogger _log = VLogger.GetVLogger(typeof(VAS_207_DraftedInProcessPOsWidgetController).FullName);

        [Authorize]
        public ActionResult Index()
        {
            return View();
        }

        /// <summary>
        /// Returns summary KPI metrics and the full list of Drafted and In Progress purchase orders
        /// with amounts converted server-side into the client's accounting schema functional currency.
        /// </summary>
        [AjaxAuthorizeAttribute]
        [AjaxSessionFilterAttribute]
        public JsonResult GetDraftedInProcessPOsData()
        {
            if (Session["ctx"] == null)
            {
                return Json(new { error = Msg.GetMsg(Env.GetCtx(), "SessionExpired") ?? "Session Expired" }, JsonRequestBehavior.AllowGet);
            }

            Ctx ctx = Session["ctx"] as Ctx;
            if (ctx == null)
            {
                return Json(new { error = "Invalid Context" }, JsonRequestBehavior.AllowGet);
            }

            try
            {
                int clientId = ctx.GetAD_Client_ID();
                int orgId = ctx.GetAD_Org_ID();

                // Step 1: Resolve functional base currency from the accounting schema
                CurrencyInfo baseCurrency = GetAccountingCurrency(ctx, clientId);

                // Step 2: Query Drafted and In Progress purchase orders
                string sql = @"
                    SELECT
                        o.C_Order_ID            AS purchase_order_id,
                        o.DocumentNo            AS purchase_order_number,
                        o.DateOrdered           AS order_date,
                        o.Created               AS created_on,
                        o.DocStatus             AS document_status,
                        o.C_Currency_ID         AS currency_id,
                        o.C_ConversionType_ID   AS conversion_type_id,
                        bp.Name                 AS vendor_name,
                        rep.Name                AS representative_name,
                        COUNT(ol.C_OrderLine_ID) AS line_count,
                        SUM(COALESCE(ol.LineNetAmt, 0)) AS po_value_document_currency
                    FROM C_Order o
                    LEFT JOIN C_OrderLine ol
                        ON ol.C_Order_ID = o.C_Order_ID
                       AND ol.IsActive = 'Y'
                    INNER JOIN C_BPartner bp
                        ON bp.C_BPartner_ID = o.C_BPartner_ID
                    LEFT JOIN AD_User rep
                        ON rep.AD_User_ID = o.SalesRep_ID
                    WHERE o.AD_Client_ID = " + clientId + @"
                      AND o.IsActive = 'Y'
                      AND o.IsSOTrx = 'N'
                      AND COALESCE(o.IsReturnTrx, 'N') = 'N'
                      AND o.DocStatus IN ('DR', 'IP')
                    GROUP BY
                        o.C_Order_ID,
                        o.DocumentNo,
                        o.DateOrdered,
                        o.Created,
                        o.DocStatus,
                        o.C_Currency_ID,
                        o.C_ConversionType_ID,
                        bp.Name,
                        rep.Name
                    ORDER BY o.Created DESC, o.DocumentNo DESC";

                sql = MRole.GetDefault(ctx).AddAccessSQL(sql, "o", MRole.SQL_FULLYQUALIFIED, MRole.SQL_RO);

                List<DraftedPORecord> records = new List<DraftedPORecord>();
                int draftedCount = 0;
                int inProgressCount = 0;
                decimal totalValueConverted = 0;

                IDataReader dr = null;
                try
                {
                    dr = DB.ExecuteReader(sql);
                    while (dr != null && dr.Read())
                    {
                        int orderId = Util.GetValueOfInt(dr["purchase_order_id"]);
                        string docNo = Util.GetValueOfString(dr["purchase_order_number"]);
                        DateTime? orderDate = Util.GetValueOfDateTime(dr["order_date"]);
                        DateTime? createdOn = Util.GetValueOfDateTime(dr["created_on"]);
                        string docStatus = Util.GetValueOfString(dr["document_status"]);
                        int currencyId = Util.GetValueOfInt(dr["currency_id"]);
                        int convTypeId = Util.GetValueOfInt(dr["conversion_type_id"]);
                        string vendorName = Util.GetValueOfString(dr["vendor_name"]);
                        string repName = Util.GetValueOfString(dr["representative_name"]);
                        int lineCount = Util.GetValueOfInt(dr["line_count"]);
                        decimal lineNetAmt = Util.GetValueOfDecimal(dr["po_value_document_currency"]);

                        // Status tracking
                        if (docStatus == "DR")
                        {
                            draftedCount++;
                        }
                        else if (docStatus == "IP")
                        {
                            inProgressCount++;
                        }

                        // Server-side Currency Conversion
                        decimal convertedAmt = lineNetAmt;
                        if (baseCurrency.CurrencyId > 0 && currencyId > 0 && currencyId != baseCurrency.CurrencyId)
                        {
                            try
                            {
                                convertedAmt = MConversionRate.Convert(
                                    ctx,
                                    lineNetAmt,
                                    currencyId,
                                    baseCurrency.CurrencyId,
                                    orderDate.HasValue ? orderDate.Value : DateTime.Now,
                                    convTypeId,
                                    clientId,
                                    orgId
                                );
                            }
                            catch (Exception exConv)
                            {
                                _log.Warning("VAS_207: Rate conversion failed for C_Order_ID " + orderId + ": " + exConv.Message);
                                convertedAmt = lineNetAmt;
                            }
                        }

                        totalValueConverted += convertedAmt;

                        records.Add(new DraftedPORecord
                        {
                            PurchaseOrderId = orderId,
                            PurchaseOrderNumber = docNo,
                            OrderDate = orderDate.HasValue ? orderDate.Value.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture) : "",
                            OrderDateFormatted = orderDate.HasValue ? orderDate.Value.ToString("dd MMM yyyy", CultureInfo.InvariantCulture) : "",
                            CreatedOn = createdOn.HasValue ? createdOn.Value.ToString("yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture) : "",
                            CreatedOnFormatted = createdOn.HasValue ? createdOn.Value.ToString("dd MMM yyyy", CultureInfo.InvariantCulture) : "",
                            DocStatus = docStatus,
                            DocStatusName = docStatus == "DR" ? (Msg.GetMsg(ctx, "Drafted") ?? "Drafted") : (Msg.GetMsg(ctx, "InProgress") ?? "In Progress"),
                            CurrencyId = currencyId,
                            VendorName = vendorName,
                            RepresentativeName = string.IsNullOrEmpty(repName) ? "—" : repName,
                            LineCount = lineCount,
                            DocValue = lineNetAmt,
                            ConvertedValue = convertedAmt
                        });
                    }
                }
                finally
                {
                    if (dr != null)
                    {
                        dr.Close();
                        dr.Dispose();
                    }
                }

                var response = new
                {
                    success = true,
                    totalDocuments = records.Count,
                    draftedCount = draftedCount,
                    inProgressCount = inProgressCount,
                    totalValue = totalValueConverted,
                    currencySymbol = baseCurrency.Symbol,
                    currencyIso = baseCurrency.IsoCode,
                    precision = baseCurrency.Precision,
                    records = records
                };

                return Json(JsonConvert.SerializeObject(response), JsonRequestBehavior.AllowGet);
            }
            catch (Exception ex)
            {
                _log.Severe("VAS_207_DraftedInProcessPOsWidget.GetDraftedInProcessPOsData: " + ex.ToString());
                return Json(new { error = ex.Message }, JsonRequestBehavior.AllowGet);
            }
        }

        /// <summary>
        /// Retrieves line details for a given purchase order (for line modal drill-down).
        /// </summary>
        [AjaxAuthorizeAttribute]
        [AjaxSessionFilterAttribute]
        public JsonResult GetOrderLines(int C_Order_ID)
        {
            if (Session["ctx"] == null)
            {
                return Json(new { error = Msg.GetMsg(Env.GetCtx(), "SessionExpired") ?? "Session Expired" }, JsonRequestBehavior.AllowGet);
            }

            Ctx ctx = Session["ctx"] as Ctx;
            if (ctx == null || C_Order_ID <= 0)
            {
                return Json(new { error = "Invalid Request" }, JsonRequestBehavior.AllowGet);
            }

            try
            {
                string sql = @"
                    SELECT
                        ol.C_OrderLine_ID             AS line_id,
                        ol.Line                       AS line_no,
                        COALESCE(p.Name, ol.Description, '—') AS product_name,
                        COALESCE(p.Value, '')         AS product_code,
                        COALESCE(asi.Description, '') AS attribute_desc,
                        COALESCE(u.UOMSymbol, u.Name, '') AS uom_symbol,
                        COALESCE(ol.QtyOrdered, 0)    AS qty_ordered,
                        COALESCE(ol.QtyDelivered, 0)  AS qty_delivered,
                        CASE WHEN COALESCE(ol.QtyOrdered, 0) > COALESCE(ol.QtyDelivered, 0)
                             THEN COALESCE(ol.QtyOrdered, 0) - COALESCE(ol.QtyDelivered, 0)
                             ELSE 0 END               AS qty_pending,
                        COALESCE(ol.PriceActual, 0)   AS price_actual,
                        COALESCE(ol.LineNetAmt, 0)    AS line_net_amt,
                        o.DocStatus                   AS doc_status,
                        cur.CurSymbol                 AS cur_symbol,
                        cur.ISO_Code                  AS cur_iso
                    FROM C_OrderLine ol
                    INNER JOIN C_Order o ON o.C_Order_ID = ol.C_Order_ID
                    LEFT JOIN M_Product p ON p.M_Product_ID = ol.M_Product_ID
                    LEFT JOIN C_UOM u ON u.C_UOM_ID = ol.C_UOM_ID
                    LEFT JOIN M_AttributeSetInstance asi ON asi.M_AttributeSetInstance_ID = ol.M_AttributeSetInstance_ID
                    LEFT JOIN C_Currency cur ON cur.C_Currency_ID = o.C_Currency_ID
                    WHERE ol.C_Order_ID = " + C_Order_ID + @"
                      AND ol.IsActive = 'Y'
                    ORDER BY ol.Line ASC, ol.C_OrderLine_ID ASC";

                sql = MRole.GetDefault(ctx).AddAccessSQL(sql, "ol", MRole.SQL_FULLYQUALIFIED, MRole.SQL_RO);

                List<object> lines = new List<object>();
                IDataReader dr = null;

                try
                {
                    dr = DB.ExecuteReader(sql);
                    while (dr != null && dr.Read())
                    {
                        decimal qtyOrdered = Util.GetValueOfDecimal(dr["qty_ordered"]);
                        decimal qtyDelivered = Util.GetValueOfDecimal(dr["qty_delivered"]);
                        decimal qtyPending = Util.GetValueOfDecimal(dr["qty_pending"]);
                        string parentDocStatus = Util.GetValueOfString(dr["doc_status"]);

                        string lineStatus = "Pending";
                        if (parentDocStatus == "DR")
                        {
                            lineStatus = "Drafted";
                        }
                        else if (qtyDelivered >= qtyOrdered && qtyOrdered > 0)
                        {
                            lineStatus = "Received";
                        }
                        else if (qtyDelivered > 0 && qtyDelivered < qtyOrdered)
                        {
                            lineStatus = "Partial received";
                        }

                        lines.Add(new
                        {
                            LineId = Util.GetValueOfInt(dr["line_id"]),
                            LineNo = Util.GetValueOfInt(dr["line_no"]),
                            ProductName = Util.GetValueOfString(dr["product_name"]),
                            ProductCode = Util.GetValueOfString(dr["product_code"]),
                            AttributeDesc = Util.GetValueOfString(dr["attribute_desc"]),
                            UOM = Util.GetValueOfString(dr["uom_symbol"]),
                            QtyOrdered = qtyOrdered,
                            QtyDelivered = qtyDelivered,
                            QtyPending = qtyPending,
                            PriceActual = Util.GetValueOfDecimal(dr["price_actual"]),
                            LineNetAmt = Util.GetValueOfDecimal(dr["line_net_amt"]),
                            LineStatus = lineStatus,
                            CurrencySymbol = Util.GetValueOfString(dr["cur_symbol"]),
                            CurrencyIso = Util.GetValueOfString(dr["cur_iso"])
                        });
                    }
                }
                finally
                {
                    if (dr != null)
                    {
                        dr.Close();
                        dr.Dispose();
                    }
                }

                return Json(JsonConvert.SerializeObject(new { success = true, lines = lines }), JsonRequestBehavior.AllowGet);
            }
            catch (Exception ex)
            {
                _log.Severe("VAS_207_DraftedInProcessPOsWidget.GetOrderLines: " + ex.ToString());
                return Json(new { error = ex.Message }, JsonRequestBehavior.AllowGet);
            }
        }

        #region Helper Classes and Methods

        private CurrencyInfo GetAccountingCurrency(Ctx ctx, int clientId)
        {
            CurrencyInfo info = new CurrencyInfo { CurrencyId = 0, Symbol = "₹", IsoCode = "INR", Precision = 2 };

            string sql = @"
                SELECT cs.C_Currency_ID, c.CurSymbol, c.ISO_Code, COALESCE(c.StdPrecision, 2) AS StdPrecision
                FROM C_AcctSchema cs
                INNER JOIN AD_ClientInfo ci ON (ci.C_AcctSchema1_ID = cs.C_AcctSchema_ID)
                INNER JOIN C_Currency c ON (cs.C_Currency_ID = c.C_Currency_ID)
                WHERE ci.AD_Client_ID = " + clientId + @"
                  AND ci.IsActive = 'Y'
                  AND cs.IsActive = 'Y'
                  AND c.IsActive = 'Y'";

            sql = MRole.GetDefault(ctx).AddAccessSQL(sql, "cs", MRole.SQL_FULLYQUALIFIED, MRole.SQL_RO);

            IDataReader dr = null;
            try
            {
                dr = DB.ExecuteReader(sql);
                if (dr != null && dr.Read())
                {
                    info.CurrencyId = Util.GetValueOfInt(dr["C_Currency_ID"]);
                    info.Symbol = Util.GetValueOfString(dr["CurSymbol"]);
                    info.IsoCode = Util.GetValueOfString(dr["ISO_Code"]);
                    info.Precision = Util.GetValueOfInt(dr["StdPrecision"]);
                }
            }
            catch (Exception ex)
            {
                _log.Warning("VAS_207: Error fetching accounting currency: " + ex.Message);
            }
            finally
            {
                if (dr != null)
                {
                    dr.Close();
                    dr.Dispose();
                }
            }

            return info;
        }

        private class CurrencyInfo
        {
            public int CurrencyId { get; set; }
            public string Symbol { get; set; }
            public string IsoCode { get; set; }
            public int Precision { get; set; }
        }

        private class DraftedPORecord
        {
            public int PurchaseOrderId { get; set; }
            public string PurchaseOrderNumber { get; set; }
            public string OrderDate { get; set; }
            public string OrderDateFormatted { get; set; }
            public string CreatedOn { get; set; }
            public string CreatedOnFormatted { get; set; }
            public string DocStatus { get; set; }
            public string DocStatusName { get; set; }
            public int CurrencyId { get; set; }
            public string VendorName { get; set; }
            public string RepresentativeName { get; set; }
            public int LineCount { get; set; }
            public decimal DocValue { get; set; }
            public decimal ConvertedValue { get; set; }
        }

        #endregion
    }
}
