﻿﻿﻿/************************************************************
 * Module Name    : VAS
 * Purpose        : Controller for PO Queue Widget (Widget 14: VAS_216_POQueueWidget)
 *                  Main operational worklist of live purchase orders
 *                  ordered by expected delivery date (DatePromised).
 *                  Excludes completed, closed, voided, and reversed POs (CO, CL, VO, RE).
 * Author         : Builder Agent 14
 * Date           : 17 August 2026
 ***********************************************************/

using Newtonsoft.Json;
using System;
using System.Collections.Generic;
using System.Data;
using System.Data.SqlClient;
using System.Linq;
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
    /// Controller for Widget 14: PO Queue (VAS_216_POQueueWidget)
    /// </summary>
    public class VAS_216_POQueueWidgetController : Controller
    {
        private static readonly VLogger _log = VLogger.GetVLogger(typeof(VAS_216_POQueueWidgetController).FullName);

        [Authorize]
        public ActionResult Index()
        {
            return View();
        }

        /// <summary>
        /// Retrieves the paginated live purchase order queue filtered by expected delivery month/year.
        /// </summary>
        /// <param name="pageNo">1-based page number (default 1).</param>
        /// <param name="pageSize">Number of rows per page (default 7).</param>
        /// <param name="month">1-based month (1..12).</param>
        /// <param name="year">4-digit year (e.g. 2026).</param>
        /// <returns>JSON object with records, pagination metadata, and tenant currency information.</returns>
        [AjaxAuthorizeAttribute]
        [AjaxSessionFilterAttribute]
        public JsonResult GetPOQueueData(int pageNo = 1, int pageSize = 7, int month = 0, int year = 0)
        {
            if (Session["ctx"] == null)
            {
                return Json(new
                {
                    error = Msg.GetMsg(Env.GetCtx(), "SessionExpired") ?? "Session Expired",
                    success = false
                }, JsonRequestBehavior.AllowGet);
            }

            Ctx ctx = Session["ctx"] as Ctx;
            if (ctx == null)
            {
                return Json(new
                {
                    error = Msg.GetMsg(Env.GetCtx(), "Error") ?? "Context is null",
                    success = false
                }, JsonRequestBehavior.AllowGet);
            }

            if (pageNo <= 0) { pageNo = 1; }
            if (pageSize <= 0) { pageSize = 7; }
            if (pageSize > 50) { pageSize = 50; }

            int offset = (pageNo - 1) * pageSize;
            DateTime today = DateTime.Today;
            if (year < 1900 || year > 2100) { year = today.Year; }
            if (month < 1 || month > 12) { month = today.Month; }

            DateTime monthStart = new DateTime(year, month, 1, 0, 0, 0);
            DateTime monthEndExclusive = monthStart.AddMonths(1);

            int clientId = ctx.GetAD_Client_ID();

            try
            {
                // 1. Resolve Functional Accounting Schema Currency
                int schemaCurrencyId = 0;
                string schemaCurSymbol = "";
                string schemaCurIso = "";
                int schemaStdPrecision = 2;

                string curSql = @"
                    SELECT cs.C_Currency_ID, c.CurSymbol, c.ISO_Code, c.StdPrecision
                    FROM C_AcctSchema cs
                    INNER JOIN AD_ClientInfo ci ON (ci.C_AcctSchema1_ID = cs.C_AcctSchema_ID)
                    INNER JOIN C_Currency c ON (cs.C_Currency_ID = c.C_Currency_ID)
                    WHERE ci.AD_Client_ID = @AD_Client_ID
                      AND ci.IsActive = 'Y'
                      AND cs.IsActive = 'Y'
                      AND c.IsActive = 'Y'";

                SqlParameter[] curParams = { new SqlParameter("@AD_Client_ID", clientId) };
                DataSet curDs = DB.ExecuteDataset(curSql, curParams, null);
                if (curDs != null && curDs.Tables.Count > 0 && curDs.Tables[0].Rows.Count > 0)
                {
                    DataRow r = curDs.Tables[0].Rows[0];
                    schemaCurrencyId = Util.GetValueOfInt(r["C_Currency_ID"]);
                    schemaCurSymbol = Util.GetValueOfString(r["CurSymbol"]);
                    schemaCurIso = Util.GetValueOfString(r["ISO_Code"]);
                    schemaStdPrecision = Util.GetValueOfInt(r["StdPrecision"]);
                }

                if (schemaCurrencyId == 0)
                {
                    schemaCurrencyId = ctx.GetContextAsInt("$C_Currency_ID");
                }

                // 2. Count Total Records Matching Filter
                string countSql = @"
                    SELECT COUNT(1)
                    FROM C_Order o
                    INNER JOIN C_BPartner bp ON (bp.C_BPartner_ID = o.C_BPartner_ID)
                    WHERE o.AD_Client_ID = @AD_Client_ID
                      AND o.IsActive = 'Y'
                      AND o.IsSOTrx = 'N'
                      AND COALESCE(o.IsReturnTrx, 'N') = 'N'
                      AND o.DocStatus NOT IN ('CO', 'CL', 'VO', 'RE')
                      AND o.DatePromised >= @MonthStart
                      AND o.DatePromised < @MonthEndExclusive";

                countSql = MRole.GetDefault(ctx).AddAccessSQL(countSql, "o", MRole.SQL_FULLYQUALIFIED, MRole.SQL_RO);

                SqlParameter[] countParams = {
                    new SqlParameter("@AD_Client_ID", clientId),
                    new SqlParameter("@MonthStart", SqlDbType.DateTime) { Value = monthStart },
                    new SqlParameter("@MonthEndExclusive", SqlDbType.DateTime) { Value = monthEndExclusive }
                };

                int totalRecords = Util.GetValueOfInt(DB.ExecuteScalar(countSql, countParams, null));
                int totalPages = totalRecords > 0 ? (int)Math.Ceiling((double)totalRecords / pageSize) : 1;

                // 3. Query Paged Live PO Queue Records
                string mainSql = @"
                    SELECT
                        o.C_Order_ID AS purchase_order_id,
                        o.DocumentNo AS purchase_order_number,
                        o.DateOrdered AS order_date,
                        o.DatePromised AS promised_date,
                        o.DocStatus AS document_status,
                        COALESCE(o.GrandTotal, 0) AS po_value,
                        o.C_Currency_ID AS currency_id,
                        c.CurSymbol AS doc_cur_symbol,
                        c.ISO_Code AS doc_cur_iso,
                        c.StdPrecision AS doc_std_precision,
                        bp.Name AS vendor_name,
                        w.Name AS warehouse_name,
                        rep.Name AS representative_name,
                        rq.first_requisition_number,
                        o.Created AS created_date,
                        created_by.Name AS created_by_name,
                        o.POReference AS order_reference,
                        pt.Name AS payment_term_name,
                        o.C_IncoTerm_ID AS incoterm_id,
                        o.PriorityRule AS priority_rule
                    FROM C_Order o
                    INNER JOIN C_BPartner bp
                        ON bp.C_BPartner_ID = o.C_BPartner_ID
                    LEFT JOIN C_Currency c
                        ON c.C_Currency_ID = o.C_Currency_ID
                    LEFT JOIN M_Warehouse w
                        ON w.M_Warehouse_ID = o.M_Warehouse_ID
                    LEFT JOIN AD_User rep
                        ON rep.AD_User_ID = o.SalesRep_ID
                    LEFT JOIN AD_User created_by
                        ON created_by.AD_User_ID = o.CreatedBy
                    LEFT JOIN C_PaymentTerm pt
                        ON pt.C_PaymentTerm_ID = o.C_PaymentTerm_ID
                    LEFT JOIN (
                        SELECT
                            ol.C_Order_ID,
                            MIN(r.DocumentNo) AS first_requisition_number
                        FROM C_OrderLine ol
                        INNER JOIN M_RequisitionLine rl
                            ON rl.M_RequisitionLine_ID = ol.M_RequisitionLine_ID
                        INNER JOIN M_Requisition r
                            ON r.M_Requisition_ID = rl.M_Requisition_ID
                        WHERE ol.IsActive = 'Y'
                          AND rl.IsActive = 'Y'
                          AND r.IsActive = 'Y'
                        GROUP BY ol.C_Order_ID
                    ) rq
                        ON rq.C_Order_ID = o.C_Order_ID
                    WHERE o.AD_Client_ID = @AD_Client_ID
                      AND o.IsActive = 'Y'
                      AND o.IsSOTrx = 'N'
                      AND COALESCE(o.IsReturnTrx, 'N') = 'N'
                      AND o.DocStatus NOT IN ('CO', 'CL', 'VO', 'RE')
                      AND o.C_Order_ID IN (@P_ORDER_ACCESS@)
                      AND o.DatePromised >= @MonthStart
                      AND o.DatePromised < @MonthEndExclusive
                    ORDER BY o.DatePromised ASC, o.DocumentNo ASC
                    OFFSET " + offset + " ROWS FETCH NEXT " + pageSize + @" ROWS ONLY";

                // MRole.AddAccessSQL cannot parse this statement (GROUP BY / derived tables /
                // several JOIN..ON clauses). AccessSqlParser mis-locates the insertion point and
                // appends the access predicates after GROUP BY / HAVING / ORDER BY, producing
                // ORA-00933 / ORA-00979 / ORA-00904. Apply the same role access through a simple,
                // parseable sub-query on C_Order instead.
                string orderAccessSql = MRole.GetDefault(ctx).AddAccessSQL(
                    "SELECT accessOrd.C_Order_ID FROM C_Order accessOrd WHERE accessOrd.AD_Client_ID = " + clientId,
                    "accessOrd", MRole.SQL_FULLYQUALIFIED, MRole.SQL_RO);
                mainSql = mainSql.Replace("@P_ORDER_ACCESS@", orderAccessSql);

                SqlParameter[] mainParams = {
                    new SqlParameter("@AD_Client_ID", clientId),
                    new SqlParameter("@MonthStart", SqlDbType.DateTime) { Value = monthStart },
                    new SqlParameter("@MonthEndExclusive", SqlDbType.DateTime) { Value = monthEndExclusive }
                };

                List<POQueueRecord> records = new List<POQueueRecord>();
                List<int> orderIds = new List<int>();

                IDataReader dr = null;
                try
                {
                    dr = DB.ExecuteReader(mainSql, mainParams);
                    while (dr != null && dr.Read())
                    {
                        int orderId = Util.GetValueOfInt(dr["purchase_order_id"]);
                        orderIds.Add(orderId);

                        records.Add(new POQueueRecord
                        {
                            PurchaseOrderID = orderId,
                            DocumentNo = Util.GetValueOfString(dr["purchase_order_number"]),
                            OrderDate = Util.GetValueOfDateTime(dr["order_date"]),
                            PromisedDate = Util.GetValueOfDateTime(dr["promised_date"]),
                            DocStatus = Util.GetValueOfString(dr["document_status"]),
                            POValue = Util.GetValueOfDecimal(dr["po_value"]),
                            CurrencyID = Util.GetValueOfInt(dr["currency_id"]),
                            CurrencySymbol = Util.GetValueOfString(dr["doc_cur_symbol"]),
                            CurrencyISO = Util.GetValueOfString(dr["doc_cur_iso"]),
                            CurrencyPrecision = Util.GetValueOfInt(dr["doc_std_precision"]),
                            VendorName = Util.GetValueOfString(dr["vendor_name"]),
                            WarehouseName = Util.GetValueOfString(dr["warehouse_name"]),
                            RepresentativeName = Util.GetValueOfString(dr["representative_name"]),
                            FirstRequisition = Util.GetValueOfString(dr["first_requisition_number"]),
                            CreatedDate = Util.GetValueOfDateTime(dr["created_date"]),
                            CreatedByName = Util.GetValueOfString(dr["created_by_name"]),
                            OrderReference = Util.GetValueOfString(dr["order_reference"]),
                            PaymentTermName = Util.GetValueOfString(dr["payment_term_name"]),
                            IncotermID = Util.GetValueOfInt(dr["incoterm_id"]),
                            PriorityRule = Util.GetValueOfString(dr["priority_rule"])
                        });
                    }
                }
                finally
                {
                    if (dr != null) { dr.Close(); dr.Dispose(); }
                }

                // 4. Batch Requisition Tooltip Query for Current Page Orders
                if (orderIds.Count > 0)
                {
                    string orderIdList = string.Join(",", orderIds);
                    string reqTooltipSql = @"
                        SELECT
                            ol.C_Order_ID AS purchase_order_id,
                            r.DocumentNo AS requisition_number
                        FROM C_OrderLine ol
                        INNER JOIN M_RequisitionLine rl
                            ON rl.M_RequisitionLine_ID = ol.M_RequisitionLine_ID
                        INNER JOIN M_Requisition r
                            ON r.M_Requisition_ID = rl.M_Requisition_ID
                        WHERE ol.IsActive = 'Y'
                          AND rl.IsActive = 'Y'
                          AND r.IsActive = 'Y'
                          AND ol.C_Order_ID IN (" + orderIdList + @")
                        GROUP BY
                            ol.C_Order_ID,
                            r.DocumentNo
                        ORDER BY
                            ol.C_Order_ID,
                            r.DocumentNo";

                    Dictionary<int, List<string>> reqDict = new Dictionary<int, List<string>>();
                    IDataReader reqDr = null;
                    try
                    {
                        reqDr = DB.ExecuteReader(reqTooltipSql);
                        while (reqDr != null && reqDr.Read())
                        {
                            int oid = Util.GetValueOfInt(reqDr["purchase_order_id"]);
                            string reqNo = Util.GetValueOfString(reqDr["requisition_number"]);

                            if (!reqDict.ContainsKey(oid))
                            {
                                reqDict[oid] = new List<string>();
                            }
                            if (!string.IsNullOrEmpty(reqNo) && !reqDict[oid].Contains(reqNo))
                            {
                                reqDict[oid].Add(reqNo);
                            }
                        }
                    }
                    finally
                    {
                        if (reqDr != null) { reqDr.Close(); reqDr.Dispose(); }
                    }

                    // Attach full requisitions and first requisition
                    foreach (var rec in records)
                    {
                        if (reqDict.ContainsKey(rec.PurchaseOrderID) && reqDict[rec.PurchaseOrderID].Count > 0)
                        {
                            rec.AllRequisitions = string.Join(", ", reqDict[rec.PurchaseOrderID]);
                            if (string.IsNullOrEmpty(rec.FirstRequisition))
                            {
                                rec.FirstRequisition = reqDict[rec.PurchaseOrderID][0];
                            }
                        }
                        else
                        {
                            rec.AllRequisitions = rec.FirstRequisition ?? "";
                        }
                    }
                }

                return Json(JsonConvert.SerializeObject(new
                {
                    success = true,
                    pageNo = pageNo,
                    pageSize = pageSize,
                    totalRecords = totalRecords,
                    totalPages = totalPages,
                    year = year,
                    month = month,
                    currency = new
                    {
                        CurrencyID = schemaCurrencyId,
                        CurSymbol = schemaCurSymbol,
                        ISO_Code = schemaCurIso,
                        StdPrecision = schemaStdPrecision
                    },
                    records = records
                }), JsonRequestBehavior.AllowGet);
            }
            catch (Exception ex)
            {
                _log.Severe("Error in GetPOQueueData: " + ex.Message + " -> " + ex.StackTrace);
                return Json(new
                {
                    error = Msg.GetMsg(Env.GetCtx(), "Error") ?? "An error occurred while fetching PO queue data.",
                    details = ex.Message,
                    success = false
                }, JsonRequestBehavior.AllowGet);
            }
        }

        /// <summary>
        /// Retrieves the full PO header details for the PO record modal drill-down.
        /// </summary>
        /// <param name="C_Order_ID">Purchase Order primary key.</param>
        /// <returns>JSON object with comprehensive header data and audit attributes.</returns>
        [AjaxAuthorizeAttribute]
        [AjaxSessionFilterAttribute]
        public JsonResult GetPODetail(int C_Order_ID)
        {
            if (Session["ctx"] == null)
            {
                return Json(new { error = Msg.GetMsg(Env.GetCtx(), "SessionExpired") ?? "Session Expired", success = false }, JsonRequestBehavior.AllowGet);
            }

            Ctx ctx = Session["ctx"] as Ctx;
            if (ctx == null || C_Order_ID <= 0)
            {
                return Json(new { error = Msg.GetMsg(Env.GetCtx(), "RecordNotFound") ?? "Invalid Purchase Order ID", success = false }, JsonRequestBehavior.AllowGet);
            }

            try
            {
                string sql = @"
                    SELECT
                        o.C_Order_ID AS purchase_order_id,
                        o.DocumentNo AS purchase_order_number,
                        o.DateOrdered AS order_date,
                        o.DatePromised AS promised_date,
                        o.DocStatus AS document_status,
                        COALESCE(o.GrandTotal, 0) AS po_value,
                        o.C_Currency_ID AS currency_id,
                        c.CurSymbol AS doc_cur_symbol,
                        c.ISO_Code AS doc_cur_iso,
                        c.StdPrecision AS doc_std_precision,
                        bp.Name AS vendor_name,
                        w.Name AS warehouse_name,
                        rep.Name AS representative_name,
                        o.Created AS created_date,
                        created_by.Name AS created_by_name,
                        o.POReference AS order_reference,
                        pt.Name AS payment_term_name,
                        o.C_IncoTerm_ID AS incoterm_id,
                        o.PriorityRule AS priority_rule
                    FROM C_Order o
                    INNER JOIN C_BPartner bp ON bp.C_BPartner_ID = o.C_BPartner_ID
                    LEFT JOIN C_Currency c ON c.C_Currency_ID = o.C_Currency_ID
                    LEFT JOIN M_Warehouse w ON w.M_Warehouse_ID = o.M_Warehouse_ID
                    LEFT JOIN AD_User rep ON rep.AD_User_ID = o.SalesRep_ID
                    LEFT JOIN AD_User created_by ON created_by.AD_User_ID = o.CreatedBy
                    LEFT JOIN C_PaymentTerm pt ON pt.C_PaymentTerm_ID = o.C_PaymentTerm_ID
                    WHERE o.C_Order_ID = @C_Order_ID
                      AND o.IsActive = 'Y'
                      AND o.AD_Client_ID = " + ctx.GetAD_Client_ID();

                sql = MRole.GetDefault(ctx).AddAccessSQL(sql, "o", MRole.SQL_FULLYQUALIFIED, MRole.SQL_RO);

                SqlParameter[] sqlParams = { new SqlParameter("@C_Order_ID", C_Order_ID) };

                POQueueRecord header = null;
                IDataReader dr = null;
                try
                {
                    dr = DB.ExecuteReader(sql, sqlParams);
                    if (dr != null && dr.Read())
                    {
                        header = new POQueueRecord
                        {
                            PurchaseOrderID = Util.GetValueOfInt(dr["purchase_order_id"]),
                            DocumentNo = Util.GetValueOfString(dr["purchase_order_number"]),
                            OrderDate = Util.GetValueOfDateTime(dr["order_date"]),
                            PromisedDate = Util.GetValueOfDateTime(dr["promised_date"]),
                            DocStatus = Util.GetValueOfString(dr["document_status"]),
                            POValue = Util.GetValueOfDecimal(dr["po_value"]),
                            CurrencyID = Util.GetValueOfInt(dr["currency_id"]),
                            CurrencySymbol = Util.GetValueOfString(dr["doc_cur_symbol"]),
                            CurrencyISO = Util.GetValueOfString(dr["doc_cur_iso"]),
                            CurrencyPrecision = Util.GetValueOfInt(dr["doc_std_precision"]),
                            VendorName = Util.GetValueOfString(dr["vendor_name"]),
                            WarehouseName = Util.GetValueOfString(dr["warehouse_name"]),
                            RepresentativeName = Util.GetValueOfString(dr["representative_name"]),
                            CreatedDate = Util.GetValueOfDateTime(dr["created_date"]),
                            CreatedByName = Util.GetValueOfString(dr["created_by_name"]),
                            OrderReference = Util.GetValueOfString(dr["order_reference"]),
                            PaymentTermName = Util.GetValueOfString(dr["payment_term_name"]),
                            IncotermID = Util.GetValueOfInt(dr["incoterm_id"]),
                            PriorityRule = Util.GetValueOfString(dr["priority_rule"])
                        };
                    }
                }
                finally
                {
                    if (dr != null) { dr.Close(); dr.Dispose(); }
                }

                if (header == null)
                {
                    return Json(new { error = Msg.GetMsg(Env.GetCtx(), "RecordNotFound") ?? "Purchase Order not found", success = false }, JsonRequestBehavior.AllowGet);
                }

                // Query summary lines delivery status for header
                string delivSql = @"
                    SELECT
                        SUM(COALESCE(QtyOrdered, 0)) AS total_ordered,
                        SUM(COALESCE(QtyDelivered, 0)) AS total_delivered
                    FROM C_OrderLine
                    WHERE C_Order_ID = @C_Order_ID
                      AND IsActive = 'Y'";

                decimal totalOrdered = 0;
                decimal totalDelivered = 0;
                IDataReader dDr = null;
                try
                {
                    dDr = DB.ExecuteReader(delivSql, sqlParams);
                    if (dDr != null && dDr.Read())
                    {
                        totalOrdered = Util.GetValueOfDecimal(dDr["total_ordered"]);
                        totalDelivered = Util.GetValueOfDecimal(dDr["total_delivered"]);
                    }
                }
                finally
                {
                    if (dDr != null) { dDr.Close(); dDr.Dispose(); }
                }

                string deliveryStatus = "Pending";
                if (header.DocStatus == "CL" || header.DocStatus == "VO" || header.DocStatus == "RE")
                {
                    deliveryStatus = "Not applicable";
                }
                else if (totalDelivered >= totalOrdered && totalOrdered > 0)
                {
                    deliveryStatus = "Fully delivered";
                }
                else if (totalDelivered > 0)
                {
                    deliveryStatus = "Partial";
                }
                else
                {
                    deliveryStatus = "Pending";
                }

                return Json(JsonConvert.SerializeObject(new
                {
                    success = true,
                    header = header,
                    deliveryStatus = deliveryStatus,
                    totalOrderedQty = totalOrdered,
                    totalDeliveredQty = totalDelivered
                }), JsonRequestBehavior.AllowGet);
            }
            catch (Exception ex)
            {
                _log.Severe("Error in GetPODetail: " + ex.Message);
                return Json(new { error = ex.Message, success = false }, JsonRequestBehavior.AllowGet);
            }
        }

        /// <summary>
        /// Retrieves the line items for a specific purchase order.
        /// </summary>
        /// <param name="C_Order_ID">Purchase Order primary key.</param>
        /// <returns>JSON object with array of line items including quantities, rates, amounts, and derived status.</returns>
        [AjaxAuthorizeAttribute]
        [AjaxSessionFilterAttribute]
        public JsonResult GetPOLines(int C_Order_ID)
        {
            if (Session["ctx"] == null)
            {
                return Json(new { error = Msg.GetMsg(Env.GetCtx(), "SessionExpired") ?? "Session Expired", success = false }, JsonRequestBehavior.AllowGet);
            }

            Ctx ctx = Session["ctx"] as Ctx;
            if (ctx == null || C_Order_ID <= 0)
            {
                return Json(new { error = Msg.GetMsg(Env.GetCtx(), "RecordNotFound") ?? "Invalid Purchase Order ID", success = false }, JsonRequestBehavior.AllowGet);
            }

            try
            {
                string sql = @"
                    SELECT
                        ol.C_OrderLine_ID AS purchase_order_line_id,
                        ol.Line AS line_no,
                        -- A charge line, or a product that is not of Item type, carries no
                        -- stock movement: the widget shows its name, UOM, ordered, rate and
                        -- amount, and dashes for received / pending / line status.
                        CASE WHEN COALESCE(ol.C_Charge_ID, 0) > 0
                             THEN COALESCE(ch.Name, N'')
                             ELSE p.Name END AS product_name,
                        CASE WHEN COALESCE(ol.C_Charge_ID, 0) > 0 THEN 'Y'
                             WHEN ol.M_Product_ID IS NOT NULL AND COALESCE(p.ProductType, 'I') <> 'I' THEN 'Y'
                             ELSE 'N' END AS IsNonStock,
                        p.Value AS product_code,
                        CASE WHEN COALESCE(ol.M_AttributeSetInstance_ID, 0) > 0
                             THEN COALESCE(asi.Description, N'')
                             ELSE N'' END AS attribute_description,
                        COALESCE(u.UOMSymbol, u.Name) AS uom,
                        COALESCE(ol.QtyOrdered, 0) AS ordered_qty,
                        -- QtyEntered is expressed in the line's own C_UOM_ID (the UOM the buyer
                        -- picked); QtyOrdered / QtyDelivered are in the product's base UOM. The
                        -- widget shows the selected UOM, so quantities are scaled to it.
                        COALESCE(ol.QtyEntered, ol.QtyOrdered, 0) AS QtyEntered,
                        COALESCE(ol.QtyDelivered, 0) AS received_qty,
                        COALESCE(ol.QtyOrdered, 0) - COALESCE(ol.QtyDelivered, 0) AS pending_qty,
                        COALESCE(ol.PriceActual, 0) AS rate,
                        COALESCE(ol.LineNetAmt, 0) AS line_amount,
                        ol.DatePromised AS promised_date,
                        o.DocStatus AS doc_status
                    FROM C_OrderLine ol
                    INNER JOIN C_Order o ON o.C_Order_ID = ol.C_Order_ID
                    LEFT JOIN M_Product p ON p.M_Product_ID = ol.M_Product_ID
                    LEFT JOIN C_Charge ch ON (ch.C_Charge_ID = ol.C_Charge_ID)
                    LEFT JOIN M_AttributeSetInstance asi ON asi.M_AttributeSetInstance_ID = ol.M_AttributeSetInstance_ID
                    LEFT JOIN C_UOM u ON u.C_UOM_ID = ol.C_UOM_ID
                    WHERE ol.IsActive = 'Y'
                      AND ol.C_Order_ID = @C_Order_ID
                      AND ol.AD_Client_ID = " + ctx.GetAD_Client_ID() + @"
                    ORDER BY ol.Line ASC";

                sql = MRole.GetDefault(ctx).AddAccessSQL(sql, "ol", MRole.SQL_FULLYQUALIFIED, MRole.SQL_RO);

                SqlParameter[] sqlParams = { new SqlParameter("@C_Order_ID", C_Order_ID) };

                List<POLineRecord> lines = new List<POLineRecord>();
                IDataReader dr = null;
                try
                {
                    dr = DB.ExecuteReader(sql, sqlParams);
                    while (dr != null && dr.Read())
                    {
                        decimal ordered = Util.GetValueOfDecimal(dr["ordered_qty"]);
                        decimal received = Util.GetValueOfDecimal(dr["received_qty"]);

                        // Quantities are shown in the UOM the line was entered in. QtyEntered is in the
                        // line's own C_UOM_ID; QtyOrdered / QtyDelivered are in the product's base UOM,
                        // so delivered is scaled by this line's own entered/ordered ratio. Header
                        // roll-ups above stay in the base UOM - summing mixed UOMs is meaningless.
                        decimal enteredQtyUom = Util.GetValueOfDecimal(dr["QtyEntered"]);
                        decimal uomRatio = (ordered != 0) ? (enteredQtyUom / ordered) : 1m;
                        ordered = enteredQtyUom;
                        received = received * uomRatio;
                        decimal pending = ordered - received;
                        if (pending < 0) { pending = 0; }
                        string docStatus = Util.GetValueOfString(dr["doc_status"]);

                        string lineStatus = "Pending";
                        if (docStatus == "DR")
                        {
                            lineStatus = "Drafted";
                        }
                        else if (docStatus == "VO" || docStatus == "RE")
                        {
                            lineStatus = "Voided";
                        }
                        else if (received >= ordered && ordered > 0)
                        {
                            lineStatus = "Received";
                        }
                        else if (received > 0)
                        {
                            lineStatus = "Partial received";
                        }
                        else
                        {
                            lineStatus = "Pending";
                        }

                        lines.Add(new POLineRecord
                        {
                            OrderLineID = Util.GetValueOfInt(dr["purchase_order_line_id"]),
                            LineNo = Util.GetValueOfInt(dr["line_no"]),
                            ProductName = Util.GetValueOfString(dr["product_name"]),
                            ProductCode = Util.GetValueOfString(dr["product_code"]),
                            AttributeDescription = Util.GetValueOfString(dr["attribute_description"]),
                            UOM = Util.GetValueOfString(dr["uom"]),
                            OrderedQty = ordered,
                            ReceivedQty = received,
                            PendingQty = pending,
                            Rate = Util.GetValueOfDecimal(dr["rate"]),
                            LineAmount = Util.GetValueOfDecimal(dr["line_amount"]),
                            PromisedDate = Util.GetValueOfDateTime(dr["promised_date"]),
                            // Charge / non-Item lines are never received - the client renders dashes
                            // for received, pending and line status.
                            IsNonStock = Util.GetValueOfString(dr["IsNonStock"]) == "Y",
                            LineStatus = lineStatus
                        });
                    }
                }
                finally
                {
                    if (dr != null) { dr.Close(); dr.Dispose(); }
                }

                return Json(JsonConvert.SerializeObject(new
                {
                    success = true,
                    lines = lines
                }), JsonRequestBehavior.AllowGet);
            }
            catch (Exception ex)
            {
                _log.Severe("Error in GetPOLines: " + ex.Message);
                return Json(new { error = ex.Message, success = false }, JsonRequestBehavior.AllowGet);
            }
        }

        #region Helper DTO Classes

        public class POQueueRecord
        {
            public int PurchaseOrderID { get; set; }
            public string DocumentNo { get; set; }
            public DateTime? OrderDate { get; set; }
            public DateTime? PromisedDate { get; set; }
            public string DocStatus { get; set; }
            public decimal POValue { get; set; }
            public int CurrencyID { get; set; }
            public string CurrencySymbol { get; set; }
            public string CurrencyISO { get; set; }
            public int CurrencyPrecision { get; set; }
            public string VendorName { get; set; }
            public string WarehouseName { get; set; }
            public string RepresentativeName { get; set; }
            public string FirstRequisition { get; set; }
            public string AllRequisitions { get; set; }
            public DateTime? CreatedDate { get; set; }
            public string CreatedByName { get; set; }
            public string OrderReference { get; set; }
            public string PaymentTermName { get; set; }
            public int IncotermID { get; set; }
            public string PriorityRule { get; set; }
        }

        public class POLineRecord
        {
            public bool IsNonStock { get; set; }
            public int OrderLineID { get; set; }
            public int LineNo { get; set; }
            public string ProductName { get; set; }
            public string ProductCode { get; set; }
            public string AttributeDescription { get; set; }
            public string UOM { get; set; }
            public decimal OrderedQty { get; set; }
            public decimal ReceivedQty { get; set; }
            public decimal PendingQty { get; set; }
            public decimal Rate { get; set; }
            public decimal LineAmount { get; set; }
            public DateTime? PromisedDate { get; set; }
            public string LineStatus { get; set; }
        }

        #endregion
    }
}
