using Newtonsoft.Json;
using System;
using System.Collections.Generic;
using System.Data;
using System.Data.SqlClient;
using System.Globalization;
using System.Linq;
using System.Web.Mvc;
using VAdvantage.DataBase;
using VAdvantage.Model;
using VAdvantage.Utility;
using VIS.Filters;

namespace VAS.Controllers
{
    /// <summary>
    /// Module Name : VAS_148_DueFulfilmentsWidget (Delivery Order dashboard)
    /// Purpose     : Backend for the 3x2 "Due Fulfilments" list widget and its
    ///               "Generate Delivery Order" modal. A fulfilment is an open
    ///               Sales Order requirement (C_Order/C_OrderLine): a completed
    ///               sales order with at least one active, product line still
    ///               undelivered (QtyOrdered > QtyDelivered) whose promised date
    ///               - COALESCE(line DatePromised, order DatePromised) - is in
    ///               the PAST (strictly before today). The modal converts one
    ///               fulfilment into a DRAFT customer Delivery Order (M_InOut +
    ///               M_InOutLine, DocStatus 'DR', IsSOTrx 'Y', MovementType
    ///               'C-'); it is never completed/released here. MRole is applied
    ///               to the primary fetched table (C_Order) on every read; all
    ///               input is parameterized; the read SQL uses only ANSI
    ///               constructs (COALESCE, CURRENT_DATE, ROW_NUMBER, OFFSET/FETCH)
    ///               so it runs unchanged on Oracle and PostgreSQL. Document
    ///               creation goes through the existing MInOut/MInOutLine model
    ///               layer inside one transaction - no shared code is modified.
    /// Widget number 148.
    /// Chronological development:
    ///   &lt;EmpCode&gt;   2026-07-22 Created
    /// </summary>
    public class VAS_148_DueFulfilmentsWidgetController : Controller
    {
        // The promised-date direction that defines this widget. Due = overdue
        // (promised date strictly before today). The sibling "Expected
        // Fulfilments" widget is the same query with ">=".
        private const string DueDateFilter = "COALESCE(ol.DatePromised, o.DatePromised) < CURRENT_DATE";

        /// <summary>
        /// One page of due fulfilments (overdue open sales orders), earliest
        /// promised date first.
        /// </summary>
        /// <param name="pageNo">1-based page number.</param>
        /// <param name="pageSize">Rows per page (max 50).</param>
        /// <returns>JSON { rows[], pageNo, pageSize, totalRecords, totalPages }.</returns>
        [AjaxAuthorizeAttribute]
        [AjaxSessionFilterAttribute]
        public JsonResult GetFulfilments(int pageNo = 1, int pageSize = 4)
        {
            if (Session["ctx"] == null)
            {
                return Fail(Msg.GetMsg(Env.GetCtx(), "SessionExpired") ?? "Session Expired");
            }

            Ctx ctx = Session["ctx"] as Ctx;

            if (pageNo <= 0) { pageNo = 1; }
            if (pageSize <= 0) { pageSize = 4; }
            if (pageSize > 50) { pageSize = 50; }
            int offset = (pageNo - 1) * pageSize;

            string rawSql = @"
                SELECT o.C_Order_ID AS Fulfilment_ID,
                       o.DocumentNo AS Fulfilment_No,
                       bp.Name AS Customer_Name,
                       loc.Address1 AS Addr1,
                       loc.Address2 AS Addr2,
                       loc.City AS City,
                       o.M_Warehouse_ID AS Warehouse_ID,
                       wh.Name AS Warehouse_Name,
                       MIN(COALESCE(ol.DatePromised, o.DatePromised)) AS Promised_Date,
                       COUNT(ol.C_OrderLine_ID) AS Line_Count,
                       SUM(COALESCE(ol.LineNetAmt, 0)) AS Fulfilment_Value
                FROM C_Order o
                JOIN C_OrderLine ol ON ol.C_Order_ID = o.C_Order_ID
                JOIN C_BPartner bp ON bp.C_BPartner_ID = o.C_BPartner_ID
                LEFT JOIN C_BPartner_Location bpl ON bpl.C_BPartner_Location_ID = o.C_BPartner_Location_ID
                LEFT JOIN C_Location loc ON loc.C_Location_ID = bpl.C_Location_ID
                LEFT JOIN M_Warehouse wh ON wh.M_Warehouse_ID = o.M_Warehouse_ID
                WHERE o.IsSOTrx = 'Y'
                  AND o.DocStatus = 'CO'
                  AND o.IsActive = 'Y'
                  AND ol.IsActive = 'Y'
                  AND ol.M_Product_ID IS NOT NULL
                  AND COALESCE(ol.QtyOrdered, 0) > COALESCE(ol.QtyDelivered, 0)
                  AND " + DueDateFilter + @"";

            // AddAccessSQL appends its access predicate to the END of the string, so
            // apply it to the SELECT...WHERE BEFORE the GROUP BY is attached -
            // otherwise the predicate lands after GROUP BY (ORA-00907 -> the widget
            // shows "Data unavailable" even though 23 rows match).
            rawSql = MRole.GetDefault(ctx).AddAccessSQL(rawSql, "o", MRole.SQL_FULLYQUALIFIED, MRole.SQL_RO);
            rawSql += @"
                GROUP BY o.C_Order_ID, o.DocumentNo, bp.Name, loc.Address1, loc.Address2, loc.City,
                         o.M_Warehouse_ID, wh.Name";

            string sql = @"
                SELECT Page.Fulfilment_ID, Page.Fulfilment_No, Page.Customer_Name,
                       Page.Addr1, Page.Addr2, Page.City, Page.Warehouse_ID, Page.Warehouse_Name,
                       Page.Promised_Date, Page.Line_Count, Page.Fulfilment_Value,
                       COUNT(1) OVER () AS TotalRecords
                FROM ( " + rawSql + @" ) Page
                ORDER BY Page.Promised_Date ASC, Page.Fulfilment_No ASC
                OFFSET @Offset ROWS FETCH NEXT @PageSize ROWS ONLY";

            List<object> rows = new List<object>();
            int totalRecords = 0;
            IDataReader dr = null;
            try
            {
                dr = DB.ExecuteReader(sql, new SqlParameter[]
                {
                    new SqlParameter("@Offset", offset),
                    new SqlParameter("@PageSize", pageSize)
                });
                while (dr != null && dr.Read())
                {
                    totalRecords = Util.GetValueOfInt(dr["TotalRecords"]);
                    rows.Add(new
                    {
                        fulfilmentId = Util.GetValueOfInt(dr["Fulfilment_ID"]),
                        fulfilmentNo = Util.GetValueOfString(dr["Fulfilment_No"]),
                        customerName = Util.GetValueOfString(dr["Customer_Name"]),
                        shipToAddress = BuildAddress(dr),
                        warehouseId = Util.GetValueOfInt(dr["Warehouse_ID"]),
                        warehouseName = Util.GetValueOfString(dr["Warehouse_Name"]),
                        lineCount = Util.GetValueOfInt(dr["Line_Count"]),
                        fulfilmentValue = Util.GetValueOfDecimal(dr["Fulfilment_Value"])
                    });
                }
            }
            catch (Exception ex)
            {
                return Fail(ex.Message);
            }
            finally
            {
                if (dr != null) { dr.Close(); dr.Dispose(); }
            }

            int totalPages = pageSize > 0 ? (int)Math.Ceiling((double)totalRecords / pageSize) : 0;
            return Ok(new { rows = rows, pageNo = pageNo, pageSize = pageSize, totalRecords = totalRecords, totalPages = totalPages, currency = GetCurrencyInfo(ctx) });
        }

        /// <summary>
        /// Modal bootstrap for one fulfilment: header defaults plus every dropdown
        /// (document types, warehouses, freight categories, priority + shipping-
        /// method reference lists) and the line list / locators for the default
        /// warehouse.
        /// </summary>
        /// <param name="orderId">C_Order_ID of the fulfilment.</param>
        /// <returns>JSON modal payload.</returns>
        [AjaxAuthorizeAttribute]
        [AjaxSessionFilterAttribute]
        public JsonResult GetModalData(int orderId)
        {
            if (Session["ctx"] == null)
            {
                return Fail(Msg.GetMsg(Env.GetCtx(), "SessionExpired") ?? "Session Expired");
            }
            Ctx ctx = Session["ctx"] as Ctx;
            if (orderId <= 0) { return Fail("Invalid fulfilment."); }

            try
            {
                HeaderInfo header = GetHeader(ctx, orderId);
                if (header == null) { return Fail("Fulfilment is no longer available."); }

                int defaultWarehouseId = header.defaultWarehouseId;

                return Ok(new
                {
                    header = header,
                    docTypes = GetDocTypes(ctx),
                    warehouses = GetWarehouses(ctx),
                    freightCategories = GetFreightCategories(ctx),
                    priorities = GetRefList(ctx, "PriorityRule"),
                    shippingMethods = GetRefList(ctx, "DeliveryViaRule"),
                    locators = GetLocators(ctx, defaultWarehouseId),
                    lines = GetModalLines(ctx, orderId, defaultWarehouseId)
                });
            }
            catch (Exception ex)
            {
                return Fail(ex.Message);
            }
        }

        /// <summary>
        /// Locators and re-computed line availability for a chosen warehouse
        /// (used when the user switches warehouse in the modal).
        /// </summary>
        /// <param name="orderId">C_Order_ID of the fulfilment.</param>
        /// <param name="warehouseId">Selected M_Warehouse_ID.</param>
        /// <returns>JSON { locators[], lines[] }.</returns>
        [AjaxAuthorizeAttribute]
        [AjaxSessionFilterAttribute]
        public JsonResult GetWarehouseData(int orderId, int warehouseId)
        {
            if (Session["ctx"] == null)
            {
                return Fail(Msg.GetMsg(Env.GetCtx(), "SessionExpired") ?? "Session Expired");
            }
            Ctx ctx = Session["ctx"] as Ctx;
            if (orderId <= 0 || warehouseId <= 0) { return Fail("Invalid selection."); }

            try
            {
                return Ok(new
                {
                    locators = GetLocators(ctx, warehouseId),
                    lines = GetModalLines(ctx, orderId, warehouseId)
                });
            }
            catch (Exception ex)
            {
                return Fail(ex.Message);
            }
        }

        /// <summary>
        /// On-hand quantity of one product in one locator (used when the user
        /// changes a line's locator).
        /// </summary>
        /// <param name="productId">M_Product_ID.</param>
        /// <param name="locatorId">M_Locator_ID.</param>
        /// <returns>JSON { onHandQty }.</returns>
        [AjaxAuthorizeAttribute]
        [AjaxSessionFilterAttribute]
        public JsonResult GetLocatorOnHand(int productId, int locatorId)
        {
            if (Session["ctx"] == null)
            {
                return Fail(Msg.GetMsg(Env.GetCtx(), "SessionExpired") ?? "Session Expired");
            }
            if (productId <= 0 || locatorId <= 0) { return Ok(new { onHandQty = 0 }); }

            try
            {
                string sql = @"
                    SELECT COALESCE(SUM(s.QtyOnHand), 0) AS On_Hand_Qty
                    FROM M_Storage s
                    WHERE s.IsActive = 'Y'
                      AND s.M_Product_ID = @Product_ID
                      AND s.M_Locator_ID = @Locator_ID";
                decimal qty = Util.GetValueOfDecimal(DB.ExecuteScalar(sql, new SqlParameter[]
                {
                    new SqlParameter("@Product_ID", productId),
                    new SqlParameter("@Locator_ID", locatorId)
                }, null));
                return Ok(new { onHandQty = qty });
            }
            catch (Exception ex)
            {
                return Fail(ex.Message);
            }
        }

        /// <summary>
        /// Creates a DRAFT customer Delivery Order (M_InOut + M_InOutLine) from a
        /// fulfilment and the user's line selection. The document is left in
        /// Draft (DocStatus 'DR') - never completed or released here.
        /// </summary>
        /// <returns>JSON confirmation payload.</returns>
        [AjaxAuthorizeAttribute]
        [AjaxSessionFilterAttribute]
        [HttpPost]
        public JsonResult CreateDeliveryOrder(int orderId, int warehouseId, int docTypeId,
            string priorityRule, string deliveryViaRule, int freightCategoryId,
            int noPackages, decimal grossWeight, decimal tareWeight, string linesJson)
        {
            if (Session["ctx"] == null)
            {
                return Fail(Msg.GetMsg(Env.GetCtx(), "SessionExpired") ?? "Session Expired");
            }
            Ctx ctx = Session["ctx"] as Ctx;
            if (orderId <= 0) { return Fail("Invalid fulfilment."); }

            List<DeliveryLineInput> inputs;
            try
            {
                inputs = JsonConvert.DeserializeObject<List<DeliveryLineInput>>(linesJson ?? "[]")
                         ?? new List<DeliveryLineInput>();
            }
            catch { return Fail("Invalid line selection."); }

            inputs = inputs.Where(l => l != null && l.orderLineId > 0 && l.qty > 0).ToList();
            if (inputs.Count == 0) { return Fail("Select at least one line to include."); }

            Trx trx = null;
            try
            {
                trx = Trx.Get("VAS_NewDO" + DateTime.Now.Ticks);

                MOrder order = new MOrder(ctx, orderId, trx);
                if (order.Get_ID() == 0 || !order.IsSOTrx() || order.GetDocStatus() != MOrder.DOCSTATUS_Completed)
                {
                    trx.Rollback();
                    return Fail("Fulfilment is not available for delivery.");
                }

                int shipWarehouseId = warehouseId > 0 ? warehouseId : order.GetM_Warehouse_ID();
                if (shipWarehouseId <= 0)
                {
                    trx.Rollback();
                    return Fail("A ship-from warehouse is required.");
                }

                if (docTypeId <= 0) { docTypeId = GetShipmentDocTypeId(ctx, order.GetAD_Org_ID()); }
                if (docTypeId <= 0)
                {
                    trx.Rollback();
                    return Fail("Delivery Order document type was not found.");
                }

                MInOut ship = new MInOut(order, docTypeId, DateTime.Now);
                ship.SetAD_Client_ID(ctx.GetAD_Client_ID());
                ship.SetAD_Org_ID(order.GetAD_Org_ID());
                ship.SetIsSOTrx(true);
                ship.SetIsReturnTrx(false);
                ship.SetMovementType(MInOut.MOVEMENTTYPE_CustomerShipment);
                ship.SetC_DocType_ID(docTypeId);
                ship.SetM_Warehouse_ID(shipWarehouseId);
                ship.SetC_Order_ID(orderId);
                ship.SetMovementDate(DateTime.Now);
                SetIfPresent(ship, "C_BPartner_Location_ID", order.GetC_BPartner_Location_ID());
                if (!string.IsNullOrEmpty(priorityRule)) { SetIfPresent(ship, "PriorityRule", priorityRule); }
                if (!string.IsNullOrEmpty(deliveryViaRule)) { SetIfPresent(ship, "DeliveryViaRule", deliveryViaRule); }
                if (freightCategoryId > 0) { SetIfPresent(ship, "M_FreightCategory_ID", freightCategoryId); }
                if (noPackages > 0) { SetIfPresent(ship, "NoPackages", noPackages); }
                if (grossWeight > 0) { SetIfPresent(ship, "VAS_GrossWeight", grossWeight); }
                if (tareWeight > 0) { SetIfPresent(ship, "VAS_TareWeight", tareWeight); }
                // Force Draft - the modal never releases the document.
                ship.SetDocStatus(MInOut.DOCSTATUS_Drafted);
                ship.SetDocAction(MInOut.DOCACTION_Complete);

                if (!ship.Save(trx))
                {
                    trx.Rollback();
                    return Fail("Delivery Order could not be created.");
                }

                int seq = 10;
                int savedLines = 0;
                decimal totalQty = 0;
                foreach (DeliveryLineInput input in inputs)
                {
                    MOrderLine orderLine = new MOrderLine(ctx, input.orderLineId, trx);
                    if (orderLine.Get_ID() == 0 || orderLine.GetC_Order_ID() != orderId)
                    {
                        trx.Rollback();
                        return Fail("One or more selected lines are no longer available.");
                    }

                    decimal openQty = orderLine.GetQtyOrdered() - orderLine.GetQtyDelivered();
                    if (openQty <= 0) { continue; }
                    decimal qty = input.qty > openQty ? openQty : input.qty;

                    int lineLocatorId = input.locatorId > 0 ? input.locatorId : GetDefaultLocatorId(shipWarehouseId, trx);

                    MInOutLine shipLine = new MInOutLine(ship);
                    shipLine.SetOrderLine(orderLine, lineLocatorId, qty);
                    shipLine.SetQty(qty);
                    shipLine.SetQtyEntered(qty);
                    shipLine.SetLine(seq);
                    if (lineLocatorId > 0) { shipLine.SetM_Locator_ID(lineLocatorId); }

                    if (!shipLine.Save(trx))
                    {
                        trx.Rollback();
                        return Fail("Delivery Order line could not be saved.");
                    }
                    seq += 10;
                    savedLines++;
                    totalQty += qty;
                }

                if (savedLines == 0)
                {
                    trx.Rollback();
                    return Fail("No valid lines to include.");
                }

                trx.Commit();

                return Ok(new
                {
                    success = true,
                    deliveryOrderId = ship.GetM_InOut_ID(),
                    deliveryOrderNo = ship.GetDocumentNo(),
                    lineCount = savedLines,
                    totalQty = totalQty
                });
            }
            catch (Exception ex)
            {
                if (trx != null) { trx.Rollback(); }
                return Fail(ex.Message);
            }
            finally
            {
                if (trx != null) { trx.Close(); }
            }
        }

        /* ---------------- private read helpers ---------------- */

        /// <summary>
        /// The system currency (the session's base currency, $C_Currency_ID) as
        /// ISO code + symbol, so the widget shows amounts in the tenant's real
        /// currency instead of a hardcoded rupee. Returns empties if unavailable.
        /// </summary>
        private object GetCurrencyInfo(Ctx ctx)
        {
            string iso = "";
            string symbol = "";
            int currencyId = ctx.GetContextAsInt("$C_Currency_ID");
            if (currencyId > 0)
            {
                IDataReader cdr = null;
                try
                {
                    cdr = DB.ExecuteReader(
                        "SELECT ISO_Code, CurSymbol FROM C_Currency WHERE C_Currency_ID = @Cur",
                        new SqlParameter[] { new SqlParameter("@Cur", currencyId) });
                    if (cdr != null && cdr.Read())
                    {
                        iso = Util.GetValueOfString(cdr["ISO_Code"]);
                        symbol = Util.GetValueOfString(cdr["CurSymbol"]);
                    }
                }
                finally { if (cdr != null) { cdr.Close(); cdr.Dispose(); } }
            }
            return new { iso = iso, symbol = symbol };
        }

        private HeaderInfo GetHeader(Ctx ctx, int orderId)
        {
            string sql = @"
                SELECT o.C_Order_ID, o.DocumentNo, o.C_BPartner_ID, bp.Name AS Customer_Name,
                       o.C_BPartner_Location_ID, o.M_Warehouse_ID AS Default_Warehouse_ID,
                       wh.Name AS Default_Warehouse_Name, o.PriorityRule AS Default_Priority_Rule
                FROM C_Order o
                JOIN C_BPartner bp ON bp.C_BPartner_ID = o.C_BPartner_ID
                LEFT JOIN M_Warehouse wh ON wh.M_Warehouse_ID = o.M_Warehouse_ID
                WHERE o.C_Order_ID = @Order_ID";
            sql = MRole.GetDefault(ctx).AddAccessSQL(sql, "o", MRole.SQL_FULLYQUALIFIED, MRole.SQL_RO);

            IDataReader dr = null;
            try
            {
                dr = DB.ExecuteReader(sql, new SqlParameter[] { new SqlParameter("@Order_ID", orderId) });
                if (dr != null && dr.Read())
                {
                    return new HeaderInfo
                    {
                        fulfilmentId = Util.GetValueOfInt(dr["C_Order_ID"]),
                        fulfilmentNo = Util.GetValueOfString(dr["DocumentNo"]),
                        customerName = Util.GetValueOfString(dr["Customer_Name"]),
                        defaultWarehouseId = Util.GetValueOfInt(dr["Default_Warehouse_ID"]),
                        defaultWarehouseName = Util.GetValueOfString(dr["Default_Warehouse_Name"]),
                        defaultPriorityRule = Util.GetValueOfString(dr["Default_Priority_Rule"])
                    };
                }
            }
            finally
            {
                if (dr != null) { dr.Close(); dr.Dispose(); }
            }
            return null;
        }

        private class HeaderInfo
        {
            public int fulfilmentId { get; set; }
            public string fulfilmentNo { get; set; }
            public string customerName { get; set; }
            public int defaultWarehouseId { get; set; }
            public string defaultWarehouseName { get; set; }
            public string defaultPriorityRule { get; set; }
        }

        private List<object> GetDocTypes(Ctx ctx)
        {
            string sql = @"
                SELECT dt.C_DocType_ID, dt.Name
                FROM C_DocType dt
                WHERE dt.IsActive = 'Y' AND dt.IsSOTrx = 'Y' AND dt.DocBaseType = 'MMS'
                  AND dt.AD_Client_ID IN (0, @AD_Client_ID)
                ORDER BY dt.Name";
            return ReadIdName(ctx, sql, "C_DocType_ID", true);
        }

        private List<object> GetWarehouses(Ctx ctx)
        {
            string sql = @"
                SELECT w.M_Warehouse_ID, w.Name
                FROM M_Warehouse w
                WHERE w.IsActive = 'Y' AND w.AD_Client_ID IN (0, @AD_Client_ID)
                ORDER BY w.Name";
            return ReadIdName(ctx, sql, "M_Warehouse_ID", true);
        }

        private List<object> GetFreightCategories(Ctx ctx)
        {
            string sql = @"
                SELECT fc.M_FreightCategory_ID, fc.Name
                FROM M_FreightCategory fc
                WHERE fc.IsActive = 'Y' AND fc.AD_Client_ID IN (0, @AD_Client_ID)
                ORDER BY fc.Name";
            return ReadIdName(ctx, sql, "M_FreightCategory_ID", true);
        }

        private List<object> ReadIdName(Ctx ctx, string sql, string idColumn, bool withClient)
        {
            List<object> list = new List<object>();
            IDataReader dr = null;
            try
            {
                SqlParameter[] p = withClient
                    ? new SqlParameter[] { new SqlParameter("@AD_Client_ID", ctx.GetAD_Client_ID()) }
                    : new SqlParameter[0];
                dr = DB.ExecuteReader(sql, p);
                while (dr != null && dr.Read())
                {
                    list.Add(new { id = Util.GetValueOfInt(dr[idColumn]), name = Util.GetValueOfString(dr["Name"]) });
                }
            }
            finally
            {
                if (dr != null) { dr.Close(); dr.Dispose(); }
            }
            return list;
        }

        /// <summary>Reference-list (value/label) for an M_InOut list column, e.g. PriorityRule / DeliveryViaRule.</summary>
        private List<object> GetRefList(Ctx ctx, string columnName)
        {
            string sql = @"
                SELECT rl.Value, rl.Name
                FROM AD_Column c
                JOIN AD_Ref_List rl ON rl.AD_Reference_ID = c.AD_Reference_Value_ID AND rl.IsActive = 'Y'
                WHERE c.AD_Table_ID = (SELECT AD_Table_ID FROM AD_Table WHERE TableName = 'M_InOut')
                  AND c.ColumnName = @Column_Name
                ORDER BY rl.Value";
            List<object> list = new List<object>();
            IDataReader dr = null;
            try
            {
                dr = DB.ExecuteReader(sql, new SqlParameter[] { new SqlParameter("@Column_Name", columnName) });
                while (dr != null && dr.Read())
                {
                    list.Add(new { value = Util.GetValueOfString(dr["Value"]), name = Util.GetValueOfString(dr["Name"]) });
                }
            }
            finally
            {
                if (dr != null) { dr.Close(); dr.Dispose(); }
            }
            return list;
        }

        private List<object> GetLocators(Ctx ctx, int warehouseId)
        {
            List<object> list = new List<object>();
            if (warehouseId <= 0) { return list; }
            string sql = @"
                SELECT l.M_Locator_ID, l.Value AS Locator_Code,
                       COALESCE(l.LocatorCombination, l.Value) AS Locator_Name
                FROM M_Locator l
                WHERE l.IsActive = 'Y' AND l.M_Warehouse_ID = @Warehouse_ID
                ORDER BY COALESCE(l.PriorityNo, 999999), l.Value";
            IDataReader dr = null;
            try
            {
                dr = DB.ExecuteReader(sql, new SqlParameter[] { new SqlParameter("@Warehouse_ID", warehouseId) });
                while (dr != null && dr.Read())
                {
                    list.Add(new
                    {
                        locatorId = Util.GetValueOfInt(dr["M_Locator_ID"]),
                        locatorCode = Util.GetValueOfString(dr["Locator_Code"]),
                        locatorName = Util.GetValueOfString(dr["Locator_Name"])
                    });
                }
            }
            finally
            {
                if (dr != null) { dr.Close(); dr.Dispose(); }
            }
            return list;
        }

        private List<object> GetModalLines(Ctx ctx, int orderId, int warehouseId)
        {
            string sql = @"
                WITH stock_by_locator AS (
                    SELECT s.M_Product_ID, l.M_Locator_ID, l.M_Warehouse_ID,
                           l.Value AS Locator_Code,
                           COALESCE(l.LocatorCombination, l.Value) AS Locator_Name,
                           SUM(COALESCE(s.QtyOnHand, 0)) AS On_Hand_Qty
                    FROM M_Locator l
                    LEFT JOIN M_Storage s ON s.M_Locator_ID = l.M_Locator_ID AND s.IsActive = 'Y'
                    WHERE l.IsActive = 'Y' AND l.M_Warehouse_ID = @Warehouse_ID
                    GROUP BY s.M_Product_ID, l.M_Locator_ID, l.M_Warehouse_ID, l.Value, l.LocatorCombination
                ),
                ranked_stock AS (
                    SELECT stock_by_locator.*,
                           ROW_NUMBER() OVER (PARTITION BY M_Product_ID ORDER BY On_Hand_Qty DESC, Locator_Code ASC) AS rn
                    FROM stock_by_locator
                )
                SELECT ol.C_OrderLine_ID, ol.M_Product_ID, p.Name AS Product_Name,
                       ol.C_UOM_ID, u.Name AS UOM_Name,
                       COALESCE(ol.QtyOrdered, 0) - COALESCE(ol.QtyDelivered, 0) AS Required_Qty,
                       rs.M_Locator_ID AS Default_Locator_ID,
                       rs.Locator_Code AS Default_Locator_Code,
                       rs.Locator_Name AS Default_Locator_Name,
                       COALESCE(rs.On_Hand_Qty, 0) AS On_Hand_Qty
                FROM C_OrderLine ol
                JOIN M_Product p ON p.M_Product_ID = ol.M_Product_ID
                LEFT JOIN C_UOM u ON u.C_UOM_ID = ol.C_UOM_ID
                LEFT JOIN ranked_stock rs ON rs.M_Product_ID = ol.M_Product_ID AND rs.rn = 1
                WHERE ol.C_Order_ID = @Order_ID
                  AND ol.IsActive = 'Y'
                  AND ol.M_Product_ID IS NOT NULL
                  AND COALESCE(ol.QtyOrdered, 0) > COALESCE(ol.QtyDelivered, 0)
                  AND COALESCE(ol.DatePromised, (SELECT DatePromised FROM C_Order WHERE C_Order_ID = @Order_ID)) < CURRENT_DATE
                ORDER BY ol.Line ASC, ol.C_OrderLine_ID ASC";

            List<object> list = new List<object>();
            IDataReader dr = null;
            try
            {
                dr = DB.ExecuteReader(sql, new SqlParameter[]
                {
                    new SqlParameter("@Warehouse_ID", warehouseId),
                    new SqlParameter("@Order_ID", orderId)
                });
                while (dr != null && dr.Read())
                {
                    decimal required = Util.GetValueOfDecimal(dr["Required_Qty"]);
                    decimal onHand = Util.GetValueOfDecimal(dr["On_Hand_Qty"]);
                    list.Add(new
                    {
                        orderLineId = Util.GetValueOfInt(dr["C_OrderLine_ID"]),
                        productId = Util.GetValueOfInt(dr["M_Product_ID"]),
                        productName = Util.GetValueOfString(dr["Product_Name"]),
                        uomName = Util.GetValueOfString(dr["UOM_Name"]),
                        requiredQty = required,
                        defaultLocatorId = Util.GetValueOfInt(dr["Default_Locator_ID"]),
                        onHandQty = onHand,
                        stockStatus = onHand >= required ? "Available" : "Short"
                    });
                }
            }
            finally
            {
                if (dr != null) { dr.Close(); dr.Dispose(); }
            }
            return list;
        }

        private int GetShipmentDocTypeId(Ctx ctx, int orgId)
        {
            string sql = @"
                SELECT dt.C_DocType_ID
                FROM C_DocType dt
                WHERE dt.IsActive = 'Y' AND dt.IsSOTrx = 'Y' AND dt.DocBaseType = 'MMS'
                  AND dt.AD_Client_ID = @AD_Client_ID
                  AND dt.AD_Org_ID IN (0, @AD_Org_ID)
                ORDER BY dt.AD_Org_ID DESC, dt.C_DocType_ID ASC";
            return Util.GetValueOfInt(DB.ExecuteScalar(sql, new SqlParameter[]
            {
                new SqlParameter("@AD_Client_ID", ctx.GetAD_Client_ID()),
                new SqlParameter("@AD_Org_ID", orgId)
            }, null));
        }

        private int GetDefaultLocatorId(int warehouseId, Trx trx)
        {
            if (warehouseId <= 0) { return 0; }
            string sql = @"
                SELECT Locator.M_Locator_ID
                FROM M_Locator Locator
                WHERE Locator.IsActive = 'Y' AND Locator.M_Warehouse_ID = @M_Warehouse_ID
                ORDER BY Locator.IsDefault DESC";
            return Util.GetValueOfInt(DB.ExecuteScalar(sql, new SqlParameter[] { new SqlParameter("@M_Warehouse_ID", warehouseId) }, trx));
        }

        /// <summary>Sets a column only when it exists on the record (schema-guarded).</summary>
        private void SetIfPresent(MInOut inout, string columnName, object value)
        {
            if (inout.Get_ColumnIndex(columnName) >= 0)
            {
                inout.Set_Value(columnName, value);
            }
        }

        private string BuildAddress(IDataReader dr)
        {
            List<string> parts = new List<string>();
            string a1 = Util.GetValueOfString(dr["Addr1"]);
            string a2 = Util.GetValueOfString(dr["Addr2"]);
            string city = Util.GetValueOfString(dr["City"]);
            if (!string.IsNullOrEmpty(a1)) { parts.Add(a1.Trim()); }
            if (!string.IsNullOrEmpty(a2)) { parts.Add(a2.Trim()); }
            if (!string.IsNullOrEmpty(city)) { parts.Add(city.Trim()); }
            return string.Join(", ", parts);
        }

        private class DeliveryLineInput
        {
            public int orderLineId { get; set; }
            public int locatorId { get; set; }
            public decimal qty { get; set; }
        }

        /// <summary>Wraps a success payload as a serialized JSON result.</summary>
        private JsonResult Ok(object result)
        {
            return Json(JsonConvert.SerializeObject(result), JsonRequestBehavior.AllowGet);
        }

        /// <summary>Wraps a failure message as a serialized JSON result.</summary>
        private JsonResult Fail(string message)
        {
            return Json(JsonConvert.SerializeObject(new
            {
                success = false,
                error = message,
                message = message
            }), JsonRequestBehavior.AllowGet);
        }
    }
}
