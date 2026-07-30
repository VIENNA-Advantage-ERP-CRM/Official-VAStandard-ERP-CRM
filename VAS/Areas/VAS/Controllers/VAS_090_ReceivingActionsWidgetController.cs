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
using VAdvantage.Process;
using VAdvantage.Utility;
using VIS.Filters;

namespace VIS.Controllers
{
    /// <summary>
    /// Module Name : Receiving Actions (Material Receipt / GRN dashboard)
    /// Purpose     : Data endpoints for the 3x2 receiving action widget. The
    ///               widget reuses VAS_082_NewGRNWidget/CreateGRN for receipt
    ///               creation and VAS_086_QAHoldsWidget/GetQAHolds + SaveQAResult
    ///               for the QA inspection load/save.
    /// Chronological development:
    ///   &lt;EmpCode&gt;   2026-06-20 Created
    ///   &lt;EmpCode&gt;   2026-07-18 Receive Against PO shows only item products
    ///               (ProductType 'I'): the PO list keeps only orders with at
    ///               least one open item line, the line list and the create
    ///               validation exclude non-item lines.
    /// </summary>
    public class VAS_090_ReceivingActionsWidgetController : Controller
    {
        /// <summary>
        /// Renders a string literal compatible with the active database: Oracle uses
        /// the national-character N'...' prefix, PostgreSQL a plain quoted literal
        /// (PostgreSQL does not support the N'...' syntax).
        /// </summary>
        /// <param name="text">Literal text (no quotes).</param>
        /// <returns>A DB-appropriate quoted literal.</returns>
        private static string NLiteral(string text)
        {
            return DB.IsPostgreSQL() ? "'" + text + "'" : "N'" + text + "'";
        }

        /// <summary>
        /// One page of completed vendor purchase orders that still have open
        /// quantity, with supplier, warehouse, receiving dock and promised date.
        /// </summary>
        /// <param name="pageNo">1-based page number.</param>
        /// <param name="pageSize">Rows per page (max 20).</param>
        /// <returns>JSON { rows[], pageNo, pageSize, totalRecords, totalPages }.</returns>
        [AjaxAuthorizeAttribute]
        [AjaxSessionFilterAttribute]
        public JsonResult GetOpenPurchaseOrders(int pageNo = 1, int pageSize = 8, string searchText = "")
        {
            if (Session["ctx"] == null)
            {
                return Fail(Msg.GetMsg(Env.GetCtx(), "SessionExpired") ?? "Session Expired");
            }

            Ctx ctx = Session["ctx"] as Ctx;

            if (pageNo <= 0) { pageNo = 1; }
            if (pageSize <= 0) { pageSize = 8; }
            if (pageSize > 50) { pageSize = 50; }

            int offset = (pageNo - 1) * pageSize;
            string dockSql = HasColumn("M_Locator", "LocatorCombination")
                ? "COALESCE(ReceiveLocator.LocatorCombination, ReceiveLocator.Value, " + NLiteral("-") + ")"
                : "COALESCE(ReceiveLocator.Value, " + NLiteral("-") + ")";

            string rawSql = @"
                SELECT PurchaseOrder.C_Order_ID AS PO_ID,
                       PurchaseOrder.DocumentNo AS PO_No,
                       BPartner.Name AS Supplier_Name,
                       COALESCE(Warehouse.Name, " + NLiteral("-") + @") AS Warehouse_Name,
                       " + dockSql + @" AS Dock_Name,
                       COALESCE(PurchaseOrder.POReference, " + NLiteral("-") + @") AS Supplier_Reference,
                       COALESCE(OrderLine.DatePromised, PurchaseOrder.DatePromised) AS Line_Promise_Date,
                       PurchaseOrder.DateOrdered AS PO_Date,
                       OrderLine.C_OrderLine_ID AS PO_Line_ID
                FROM C_Order PurchaseOrder
                INNER JOIN C_OrderLine OrderLine ON (OrderLine.C_Order_ID=PurchaseOrder.C_Order_ID AND OrderLine.IsActive='Y')
                /* Correction 2026-07-18: only ITEM products are receivable -
                   the join keeps only ProductType 'I' lines, so a PO appears
                   only when at least one open line is an item, and the open
                   line count counts item lines only. */
                INNER JOIN M_Product ItemProduct ON (ItemProduct.M_Product_ID=OrderLine.M_Product_ID AND ItemProduct.IsActive='Y' AND ItemProduct.ProductType='I')
                INNER JOIN C_BPartner BPartner ON (BPartner.C_BPartner_ID=PurchaseOrder.C_BPartner_ID AND BPartner.IsActive='Y')
                LEFT OUTER JOIN M_Warehouse Warehouse ON (Warehouse.M_Warehouse_ID=PurchaseOrder.M_Warehouse_ID AND Warehouse.IsActive='Y')
                LEFT OUTER JOIN M_Locator ReceiveLocator ON (ReceiveLocator.M_Locator_ID=Warehouse.M_RcvLocator_ID AND ReceiveLocator.IsActive='Y')
                WHERE PurchaseOrder.IsActive='Y'
                  AND PurchaseOrder.IsSOTrx='N'
                  AND PurchaseOrder.DocStatus='CO'
                  AND PurchaseOrder.AD_Client_ID=@AD_Client_ID
                  AND (COALESCE(OrderLine.QtyOrdered, 0) - COALESCE(OrderLine.QtyDelivered, 0) - __VAS_UNPOSTED_GRN_QTY__) > 0";

            // Search over the PO number or supplier name (parameterized;
            // appended before role security so it stays inside the WHERE).
            string trimmedSearch = (searchText ?? "").Trim();
            if (trimmedSearch.Length > 0)
            {
                rawSql += @"
                  AND (UPPER(PurchaseOrder.DocumentNo) LIKE @PO_Search1 OR UPPER(BPartner.Name) LIKE @PO_Search2)";
            }

            rawSql = MRole.GetDefault(ctx).AddAccessSQL(
                rawSql,
                "PurchaseOrder",
                MRole.SQL_FULLYQUALIFIED,
                MRole.SQL_RO
            );

            // Net off quantities already on unposted GRNs (draft / in-progress
            // receipts) so a PO fully covered by an awaiting-confirmation receipt
            // no longer counts as open. Injected after the access SQL so MRole
            // does not push predicates for the subquery's M_InOutLine alias into
            // the outer where-clause (ORA-00904).
            rawSql = rawSql.Replace("__VAS_UNPOSTED_GRN_QTY__", @"(
                      SELECT COALESCE(SUM(il.MovementQty), 0)
                      FROM M_InOut i
                      INNER JOIN M_InOutLine il ON (i.M_InOut_ID=il.M_InOut_ID)
                      WHERE il.C_OrderLine_ID=OrderLine.C_OrderLine_ID
                        AND il.IsActive='Y'
                        AND i.DocStatus NOT IN ('RE','VO','CL','CO')
                  )");

            string sql = @"
                SELECT OpenPO.PO_ID,
                       OpenPO.PO_No,
                       OpenPO.Supplier_Name,
                       OpenPO.Warehouse_Name,
                       OpenPO.Dock_Name,
                       OpenPO.Supplier_Reference,
                       OpenPO.Promise_Date,
                       OpenPO.Open_Line_Count,
                       COUNT(1) OVER () AS TotalRecords
                FROM (
                    SELECT RawData.PO_ID,
                           RawData.PO_No,
                           RawData.Supplier_Name,
                           RawData.Warehouse_Name,
                           RawData.Dock_Name,
                           RawData.Supplier_Reference,
                           MIN(RawData.Line_Promise_Date) AS Promise_Date,
                           MIN(RawData.PO_Date) AS PO_Date,
                           COUNT(RawData.PO_Line_ID) AS Open_Line_Count
                    FROM (
                        " + rawSql + @"
                    ) RawData
                    GROUP BY RawData.PO_ID,
                             RawData.PO_No,
                             RawData.Supplier_Name,
                             RawData.Warehouse_Name,
                             RawData.Dock_Name,
                             RawData.Supplier_Reference
                ) OpenPO
                -- Review #48: PO date (DateOrdered), oldest first; document
                -- number as the deterministic tiebreak for stable paging.
                ORDER BY OpenPO.PO_Date, OpenPO.PO_No
                OFFSET @Offset ROWS FETCH NEXT @PageSize ROWS ONLY";

            // Positional binding: parameters ordered exactly as they appear in
            // the final SQL (@AD_Client_ID, search params, then paging).
            List<SqlParameter> parameters = new List<SqlParameter>();
            parameters.Add(new SqlParameter("@AD_Client_ID", ctx.GetAD_Client_ID()));
            if (trimmedSearch.Length > 0)
            {
                string like = "%" + trimmedSearch.ToUpperInvariant() + "%";
                parameters.Add(new SqlParameter("@PO_Search1", like));
                parameters.Add(new SqlParameter("@PO_Search2", like));
            }
            parameters.Add(new SqlParameter("@Offset", offset));
            parameters.Add(new SqlParameter("@PageSize", pageSize));

            List<object> rows = new List<object>();
            int totalRecords = 0;
            IDataReader dr = null;

            try
            {
                dr = DB.ExecuteReader(sql, parameters.ToArray());

                while (dr != null && dr.Read())
                {
                    totalRecords = Util.GetValueOfInt(dr["TotalRecords"]);
                    DateTime? promiseDate = Util.GetValueOfDateTime(dr["Promise_Date"]);

                    rows.Add(new
                    {
                        poId = Util.GetValueOfInt(dr["PO_ID"]),
                        poNo = Util.GetValueOfString(dr["PO_No"]),
                        supplier = Util.GetValueOfString(dr["Supplier_Name"]),
                        warehouseName = Util.GetValueOfString(dr["Warehouse_Name"]),
                        dockName = Util.GetValueOfString(dr["Dock_Name"]),
                        supplierReference = Util.GetValueOfString(dr["Supplier_Reference"]),
                        promiseDate = promiseDate.HasValue ? promiseDate.Value.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture) : "",
                        openLineCount = Util.GetValueOfInt(dr["Open_Line_Count"])
                    });
                }

                return Ok(new
                {
                    rows = rows,
                    pageNo = pageNo,
                    pageSize = pageSize,
                    totalRecords = totalRecords,
                    totalPages = pageSize == 0 ? 0 : Convert.ToInt32(Math.Ceiling((decimal)totalRecords / pageSize))
                });
            }
            catch (Exception ex)
            {
                return Fail(ex.Message);
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
        /// Open (un-received) order lines for one purchase order, with the default
        /// received quantity pre-set to the remaining open quantity.
        /// </summary>
        /// <param name="poId">C_Order_ID of the selected purchase order.</param>
        /// <returns>JSON { rows[] }.</returns>
        [AjaxAuthorizeAttribute]
        [AjaxSessionFilterAttribute]
        public JsonResult GetPurchaseOrderLines(int poId = 0)
        {
            if (Session["ctx"] == null)
            {
                return Fail(Msg.GetMsg(Env.GetCtx(), "SessionExpired") ?? "Session Expired");
            }

            Ctx ctx = Session["ctx"] as Ctx;

            string sql = @"
                SELECT OrderLine.C_OrderLine_ID AS PO_Line_ID,
                       OrderLine.Line AS Line_No,
                       COALESCE(Product.Name, " + NLiteral("-") + @") AS Item_Name,
                       AttributeInstance.Description AS Attribute_Name,
                       COALESCE(OrderLine.QtyOrdered, 0) AS PO_Qty,
                       COALESCE(OrderLine.QtyDelivered, 0) AS Already_Received_Qty,
                       COALESCE(OrderLine.QtyOrdered, 0) - COALESCE(OrderLine.QtyDelivered, 0) - __VAS_UNPOSTED_GRN_QTY__ AS Open_Qty,
                       UOM.Name AS Uom
                FROM C_Order PurchaseOrder
                INNER JOIN C_OrderLine OrderLine ON (OrderLine.C_Order_ID=PurchaseOrder.C_Order_ID AND OrderLine.IsActive='Y')
                /* Correction 2026-07-18: only ITEM products (ProductType 'I')
                   are listed for receiving - charge / service lines are out. */
                INNER JOIN M_Product Product ON (Product.M_Product_ID=OrderLine.M_Product_ID AND Product.IsActive='Y' AND Product.ProductType='I')
                LEFT OUTER JOIN M_AttributeSetInstance AttributeInstance ON (AttributeInstance.M_AttributeSetInstance_ID=OrderLine.M_AttributeSetInstance_ID)
                LEFT OUTER JOIN C_UOM UOM ON (UOM.C_UOM_ID=OrderLine.C_UOM_ID AND UOM.IsActive='Y')
                WHERE PurchaseOrder.IsActive='Y'
                  AND PurchaseOrder.IsSOTrx='N'
                  AND PurchaseOrder.DocStatus='CO'
                  AND PurchaseOrder.AD_Client_ID=@AD_Client_ID
                  AND PurchaseOrder.C_Order_ID=@PO_ID
                  AND (COALESCE(OrderLine.QtyOrdered, 0) - COALESCE(OrderLine.QtyDelivered, 0) - __VAS_UNPOSTED_GRN_QTY__) > 0";

            sql = MRole.GetDefault(ctx).AddAccessSQL(
                sql,
                "PurchaseOrder",
                MRole.SQL_FULLYQUALIFIED,
                MRole.SQL_RO
            );

            // Open qty also nets off quantities already sitting on unposted GRNs
            // (draft / in-progress receipts, e.g. a "with confirmation" receipt
            // awaiting its confirmation) so the form never offers to receive a
            // quantity twice. Injected after the access SQL is applied so MRole
            // does not try to add predicates for the subquery's M_InOutLine alias
            // to the outer where-clause (ORA-00904).
            sql = sql.Replace("__VAS_UNPOSTED_GRN_QTY__", @"(
                      SELECT COALESCE(SUM(il.MovementQty), 0)
                      FROM M_InOut i
                      INNER JOIN M_InOutLine il ON (i.M_InOut_ID=il.M_InOut_ID)
                      WHERE il.C_OrderLine_ID=OrderLine.C_OrderLine_ID
                        AND il.IsActive='Y'
                        AND i.DocStatus NOT IN ('RE','VO','CL','CO')
                  )");

            sql += @"
                ORDER BY OrderLine.Line";

            List<SqlParameter> parameters = new List<SqlParameter>();
            parameters.Add(new SqlParameter("@AD_Client_ID", ctx.GetAD_Client_ID()));
            parameters.Add(new SqlParameter("@PO_ID", poId));

            List<object> rows = new List<object>();
            IDataReader dr = null;

            try
            {
                dr = DB.ExecuteReader(sql, parameters.ToArray());

                while (dr != null && dr.Read())
                {
                    decimal openQty = Util.GetValueOfDecimal(dr["Open_Qty"]);

                    rows.Add(new
                    {
                        poLineId = Util.GetValueOfInt(dr["PO_Line_ID"]),
                        lineNo = Util.GetValueOfInt(dr["Line_No"]),
                        itemName = Util.GetValueOfString(dr["Item_Name"]),
                        attributeName = Util.GetValueOfString(dr["Attribute_Name"]),
                        poQty = Util.GetValueOfDecimal(dr["PO_Qty"]),
                        alreadyReceivedQty = Util.GetValueOfDecimal(dr["Already_Received_Qty"]),
                        openQty = openQty,
                        defaultReceivedQty = openQty,
                        uom = Util.GetValueOfString(dr["Uom"])
                    });
                }

                dr.Close();
                dr.Dispose();
                dr = null;

                // Review #49: header data for the receive form - the warehouses
                // of the org the PO was created in (line locators follow the
                // selected warehouse).
                int poOrgId = 0;
                int defaultWarehouseId = 0;

                string headerSql = @"
                    SELECT PurchaseOrder.AD_Org_ID AS PO_Org_ID,
                           COALESCE(PurchaseOrder.M_Warehouse_ID, 0) AS PO_Warehouse_ID
                    FROM C_Order PurchaseOrder
                    WHERE PurchaseOrder.C_Order_ID=@Header_PO_ID
                      AND PurchaseOrder.AD_Client_ID=@Header_Client_ID";

                dr = DB.ExecuteReader(headerSql, new SqlParameter[]
                {
                    new SqlParameter("@Header_PO_ID", poId),
                    new SqlParameter("@Header_Client_ID", ctx.GetAD_Client_ID())
                });
                if (dr != null && dr.Read())
                {
                    poOrgId = Util.GetValueOfInt(dr["PO_Org_ID"]);
                    defaultWarehouseId = Util.GetValueOfInt(dr["PO_Warehouse_ID"]);
                }
                dr.Close();
                dr.Dispose();
                dr = null;

                // The Document Type is chosen on the receive form and drives the
                // created GRN. Offer every active Material Receipt type
                // (DocBaseType 'MMR', non-return) of the client/org, e.g.
                // "MM Receipt" and "MM Receipt with Confirmation". The plain
                // receipt (lowest C_DocType_ID for the most specific org) is
                // preselected: ordering by AD_Org_ID DESC, C_DocType_ID makes the
                // default deterministic (the old single-row lookup had no tiebreak
                // and could return the wrong receipt type).
                List<object> docTypes = new List<object>();
                int defaultDocTypeId = 0;

                dr = DB.ExecuteReader(@"
                    SELECT DocType.C_DocType_ID AS DocType_ID,
                           DocType.Name AS DocType_Name
                    FROM C_DocType DocType
                    WHERE DocType.DocBaseType='MMR'
                      AND DocType.AD_Client_ID=@DocType_Client_ID
                      AND DocType.IsActive='Y'
                      AND DocType.AD_Org_ID IN (0, @DocType_Org_ID)
                      AND DocType.IsSOTrx='N'
                      AND DocType.IsReturnTrx='N'
                    ORDER BY DocType.AD_Org_ID DESC, DocType.C_DocType_ID",
                    new SqlParameter[]
                    {
                        new SqlParameter("@DocType_Client_ID", ctx.GetAD_Client_ID()),
                        new SqlParameter("@DocType_Org_ID", poOrgId)
                    });
                while (dr != null && dr.Read())
                {
                    int listDocTypeId = Util.GetValueOfInt(dr["DocType_ID"]);
                    if (defaultDocTypeId == 0) { defaultDocTypeId = listDocTypeId; }
                    docTypes.Add(new
                    {
                        docTypeId = listDocTypeId,
                        docTypeName = Util.GetValueOfString(dr["DocType_Name"])
                    });
                }
                dr.Close();
                dr.Dispose();
                dr = null;

                List<object> warehouses = new List<object>();
                bool defaultInList = false;
                string warehouseSql = @"
                    SELECT Warehouse.M_Warehouse_ID AS Warehouse_ID,
                           Warehouse.Name AS Warehouse_Name
                    FROM M_Warehouse Warehouse
                    WHERE Warehouse.IsActive='Y'
                      AND Warehouse.AD_Client_ID=@WH_Client_ID
                      AND Warehouse.AD_Org_ID=@WH_Org_ID
                    ORDER BY Warehouse.Name";

                dr = DB.ExecuteReader(warehouseSql, new SqlParameter[]
                {
                    new SqlParameter("@WH_Client_ID", ctx.GetAD_Client_ID()),
                    new SqlParameter("@WH_Org_ID", poOrgId)
                });
                while (dr != null && dr.Read())
                {
                    int warehouseId = Util.GetValueOfInt(dr["Warehouse_ID"]);
                    if (warehouseId == defaultWarehouseId) { defaultInList = true; }
                    warehouses.Add(new
                    {
                        warehouseId = warehouseId,
                        warehouseName = Util.GetValueOfString(dr["Warehouse_Name"])
                    });
                }

                // The PO's own warehouse always stays selectable even when it
                // belongs to another org (legacy data).
                if (defaultWarehouseId > 0 && !defaultInList)
                {
                    string poWarehouseName = Util.GetValueOfString(DB.ExecuteScalar(
                        "SELECT Name FROM M_Warehouse WHERE M_Warehouse_ID=@PO_WH_ID",
                        new SqlParameter[] { new SqlParameter("@PO_WH_ID", defaultWarehouseId) },
                        null));
                    warehouses.Insert(0, new { warehouseId = defaultWarehouseId, warehouseName = poWarehouseName });
                }

                return Ok(new
                {
                    rows = rows,
                    docTypes = docTypes,
                    defaultDocTypeId = defaultDocTypeId,
                    poOrgId = poOrgId,
                    defaultWarehouseId = defaultWarehouseId,
                    warehouses = warehouses
                });
            }
            catch (Exception ex)
            {
                return Fail(ex.Message);
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
        /// Active locators of one warehouse for the line-level locator choice
        /// (review #49). The warehouse default locator is flagged so the client
        /// can preselect it.
        /// </summary>
        /// <param name="warehouseId">M_Warehouse_ID selected on the form.</param>
        /// <returns>JSON { rows[] } of locatorId / locatorName / isDefault.</returns>
        [AjaxAuthorizeAttribute]
        [AjaxSessionFilterAttribute]
        public JsonResult GetWarehouseLocators(int warehouseId = 0)
        {
            if (Session["ctx"] == null)
            {
                return Fail(Msg.GetMsg(Env.GetCtx(), "SessionExpired") ?? "Session Expired");
            }

            Ctx ctx = Session["ctx"] as Ctx;
            if (warehouseId <= 0) { return Ok(new { rows = new List<object>() }); }

            string locatorNameSql = HasColumn("M_Locator", "LocatorCombination")
                ? "COALESCE(Locator.LocatorCombination, Locator.Value)"
                : "Locator.Value";

            string sql = @"
                SELECT Locator.M_Locator_ID AS Locator_ID,
                       " + locatorNameSql + @" AS Locator_Name,
                       Locator.IsDefault AS Is_Default
                FROM M_Locator Locator
                WHERE Locator.IsActive='Y'
                  AND Locator.AD_Client_ID=@AD_Client_ID
                  AND Locator.M_Warehouse_ID=@Warehouse_ID
                ORDER BY Locator.IsDefault DESC, " + locatorNameSql;

            List<object> rows = new List<object>();
            IDataReader dr = null;

            try
            {
                dr = DB.ExecuteReader(sql, new SqlParameter[]
                {
                    new SqlParameter("@AD_Client_ID", ctx.GetAD_Client_ID()),
                    new SqlParameter("@Warehouse_ID", warehouseId)
                });
                while (dr != null && dr.Read())
                {
                    rows.Add(new
                    {
                        locatorId = Util.GetValueOfInt(dr["Locator_ID"]),
                        locatorName = Util.GetValueOfString(dr["Locator_Name"]),
                        isDefault = Util.GetValueOfString(dr["Is_Default"]) == "Y"
                    });
                }

                return Ok(new { rows = rows });
            }
            catch (Exception ex)
            {
                return Fail(ex.Message);
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
        /// Creates and completes a Material Receipt (GRN) for a purchase order using
        /// the Document Type chosen on the receive form. Re-validates each line
        /// against the live open quantity, saves the receipt and its lines through
        /// the M_InOut / M_InOutLine models inside a single transaction (rolled back
        /// on any failure), then runs the standard document completion
        /// (DOCACTION_Complete). Returns the resulting document status so the client
        /// can show it truthfully rather than a hardcoded label.
        /// </summary>
        /// <param name="poId">C_Order_ID of the purchase order being received.</param>
        /// <param name="linesJson">JSON array of { poLineId, receivedQty, locatorId }.</param>
        /// <param name="warehouseId">Receiving warehouse chosen on the form (0 = PO's own).</param>
        /// <param name="docTypeId">Chosen Material Receipt C_DocType_ID (0 = default).</param>
        /// <returns>JSON { success, grnId, grnNo, docStatus, docStatusName } or { error }.</returns>
        [HttpPost]
        [AjaxAuthorizeAttribute]
        [AjaxSessionFilterAttribute]
        public JsonResult CreateGRN(int poId = 0, string linesJson = null, int warehouseId = 0, int docTypeId = 0)
        {
            if (Session["ctx"] == null)
            {
                return Fail(Msg.GetMsg(Env.GetCtx(), "SessionExpired") ?? "Session Expired");
            }

            Ctx ctx = Session["ctx"] as Ctx;

            if (poId <= 0)
            {
                return Fail("Purchase Order is required.");
            }

            List<ReceiveLineInput> inputs = string.IsNullOrWhiteSpace(linesJson)
                ? new List<ReceiveLineInput>()
                : JsonConvert.DeserializeObject<List<ReceiveLineInput>>(linesJson);
            if (inputs == null) { inputs = new List<ReceiveLineInput>(); }

            // Aggregate typed quantities per PO line and remember the chosen locator.
            Dictionary<int, decimal> qtyByLine = new Dictionary<int, decimal>();
            Dictionary<int, int> locatorByLine = new Dictionary<int, int>();

            foreach (ReceiveLineInput input in inputs)
            {
                if (input == null || input.PoLineId <= 0) { continue; }
                if (input.ReceivedQty < 0) { return Fail("Received quantity cannot be negative."); }
                if (input.ReceivedQty == 0) { continue; }

                if (qtyByLine.ContainsKey(input.PoLineId))
                {
                    qtyByLine[input.PoLineId] += input.ReceivedQty;
                }
                else
                {
                    qtyByLine[input.PoLineId] = input.ReceivedQty;
                }

                if (input.LocatorId > 0)
                {
                    locatorByLine[input.PoLineId] = input.LocatorId;
                }
            }

            if (qtyByLine.Count == 0)
            {
                return Fail("Enter received quantity for at least one line.");
            }

            List<int> lineIds = new List<int>(qtyByLine.Keys);
            Dictionary<int, ReceiveOpenLine> openLines = GetOpenLineInfo(ctx, poId, lineIds);

            if (openLines.Count != qtyByLine.Count)
            {
                return Fail("One or more selected PO lines are no longer open.");
            }

            foreach (KeyValuePair<int, decimal> selectedLine in qtyByLine)
            {
                if (selectedLine.Value > openLines[selectedLine.Key].OpenQty)
                {
                    return Fail("Received quantity cannot be greater than open quantity.");
                }
            }

            Trx trx = null;

            try
            {
                trx = Trx.Get("VAS_090_NewGRN" + DateTime.Now.Ticks);

                MOrder order = new MOrder(ctx, poId, trx);
                if (order.Get_ID() == 0 || order.IsSOTrx() || order.GetDocStatus() != MOrder.DOCSTATUS_Completed)
                {
                    trx.Rollback();
                    return Fail("Purchase Order is not available for receiving.");
                }

                int receiptWarehouseId = warehouseId > 0 ? warehouseId : order.GetM_Warehouse_ID();
                if (receiptWarehouseId <= 0)
                {
                    foreach (KeyValuePair<int, ReceiveOpenLine> openLine in openLines)
                    {
                        receiptWarehouseId = openLine.Value.WarehouseId;
                        break;
                    }
                }

                int locatorId = GetDefaultLocatorId(receiptWarehouseId, trx);

                // Use the Document Type chosen on the form when it is a valid
                // Material Receipt type for this client/org, otherwise the
                // deterministic default (plain receipt, most specific org).
                int resolvedDocTypeId = ResolveReceiptDocTypeId(ctx, order.GetAD_Org_ID(), docTypeId);
                if (resolvedDocTypeId <= 0)
                {
                    trx.Rollback();
                    return Fail("Material Receipt document type was not found.");
                }

                MInOut receipt = new MInOut(order, resolvedDocTypeId, DateTime.Now);
                receipt.SetAD_Client_ID(ctx.GetAD_Client_ID());
                receipt.SetAD_Org_ID(order.GetAD_Org_ID());
                receipt.SetIsSOTrx(false);
                receipt.SetIsReturnTrx(false);
                receipt.SetMovementType(MInOut.MOVEMENTTYPE_VendorReceipts);
                receipt.SetC_DocType_ID(resolvedDocTypeId);
                receipt.SetM_Warehouse_ID(receiptWarehouseId);
                receipt.SetC_Order_ID(poId);
                if (locatorId > 0)
                {
                    receipt.Set_Value("M_Locator_ID", locatorId);
                }

                if (!receipt.Save(trx))
                {
                    trx.Rollback();
                    return Fail(GetSaveError(ctx, "VAS_GRNNotSaved", "GRN could not be saved."));
                }

                foreach (KeyValuePair<int, decimal> selectedLine in qtyByLine)
                {
                    MOrderLine orderLine = new MOrderLine(ctx, selectedLine.Key, trx);
                    if (orderLine.Get_ID() == 0)
                    {
                        trx.Rollback();
                        return Fail("One or more selected PO lines are no longer available.");
                    }

                    decimal receivedQty = selectedLine.Value;
                    int lineLocatorId = locatorByLine.ContainsKey(selectedLine.Key) && locatorByLine[selectedLine.Key] > 0
                        ? locatorByLine[selectedLine.Key]
                        : locatorId;

                    MInOutLine receiptLine = new MInOutLine(receipt);
                    receiptLine.SetOrderLine(orderLine, lineLocatorId, receivedQty);
                    receiptLine.SetQty(receivedQty);

                    if (orderLine.GetQtyOrdered() != 0 && orderLine.GetQtyEntered() != orderLine.GetQtyOrdered())
                    {
                        receiptLine.SetQtyEntered(decimal.Round(
                            decimal.Divide(decimal.Multiply(receivedQty, orderLine.GetQtyEntered()), orderLine.GetQtyOrdered()),
                            12,
                            MidpointRounding.AwayFromZero));
                    }

                    if (receiptLine.Get_ColumnIndex("PrintDescription") >= 0)
                    {
                        receiptLine.Set_Value("PrintDescription", orderLine.Get_Value("PrintDescription"));
                    }

                    if (!receiptLine.Save(trx))
                    {
                        trx.Rollback();
                        return Fail(GetSaveError(ctx, "VAS_GRNNotSaved", "GRN line could not be saved."));
                    }
                }

                // The receipt and its lines are saved in this transaction as a
                // DRAFT; commit them, then run the standard document completion
                // through the document process (DocumentEngine.CompleteOrReverse -
                // the same path the core runs on Complete). That process executes
                // outside this transaction, so the record is committed first. The
                // AD_Table_ID and AD_Process_ID are resolved from the dictionary
                // at runtime by name/Value - never hardcoded.
                trx.Commit();
                trx.Close();
                trx = null;

                int receiptId = receipt.GetM_InOut_ID();
                string completeError = DocumentEngine.CompleteOrReverse(
                    ctx,
                    "M_InOut",
                    GetTableId("M_InOut"),
                    receiptId,
                    GetProcessIdByValue(ctx, "M_InOut Process"),
                    MInOut.DOCACTION_Complete);

                if (!string.IsNullOrEmpty(completeError))
                {
                    return Fail(completeError);
                }

                // Reload for the final document status after completion.
                MInOut completedReceipt = new MInOut(ctx, receiptId, null);
                string docStatus = completedReceipt.GetDocStatus();
                return Ok(new
                {
                    success = true,
                    shipmentId = receiptId,
                    grnId = receiptId,
                    grnNo = completedReceipt.GetDocumentNo(),
                    docStatus = docStatus,
                    docStatusName = DocStatusLabel(ctx, docStatus),
                    message = Msg.GetMsg(ctx, "VAS_GRNSaved") ?? "GRN created."
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

        /// <summary>
        /// Validates a requested Material Receipt document type against the active
        /// client/org; returns it when valid, otherwise the deterministic default
        /// (plain receipt, most specific org, lowest C_DocType_ID as the tiebreak).
        /// </summary>
        private int ResolveReceiptDocTypeId(Ctx ctx, int orgId, int requestedDocTypeId)
        {
            if (requestedDocTypeId > 0)
            {
                int valid = Util.GetValueOfInt(DB.ExecuteScalar(@"
                    SELECT C_DocType_ID
                    FROM C_DocType
                    WHERE C_DocType_ID=@DocType_ID
                      AND DocBaseType='MMR'
                      AND IsActive='Y'
                      AND IsSOTrx='N'
                      AND IsReturnTrx='N'
                      AND AD_Client_ID=@AD_Client_ID
                      AND AD_Org_ID IN (0, @AD_Org_ID)",
                    new SqlParameter[]
                    {
                        new SqlParameter("@DocType_ID", requestedDocTypeId),
                        new SqlParameter("@AD_Client_ID", ctx.GetAD_Client_ID()),
                        new SqlParameter("@AD_Org_ID", orgId)
                    }, null));
                if (valid > 0) { return valid; }
            }

            return Util.GetValueOfInt(DB.ExecuteScalar(@"
                SELECT C_DocType_ID
                FROM C_DocType
                WHERE DocBaseType='MMR'
                  AND IsActive='Y'
                  AND IsSOTrx='N'
                  AND IsReturnTrx='N'
                  AND AD_Client_ID=@AD_Client_ID
                  AND AD_Org_ID IN (0, @AD_Org_ID)
                ORDER BY AD_Org_ID DESC, C_DocType_ID
                FETCH FIRST 1 ROW ONLY",
                new SqlParameter[]
                {
                    new SqlParameter("@AD_Client_ID", ctx.GetAD_Client_ID()),
                    new SqlParameter("@AD_Org_ID", orgId)
                }, null));
        }

        /// <summary>
        /// Re-reads the live open quantity and warehouse for the selected PO lines,
        /// keyed by C_OrderLine_ID, so CreateGRN validates against current data.
        /// </summary>
        private Dictionary<int, ReceiveOpenLine> GetOpenLineInfo(Ctx ctx, int poId, List<int> lineIds)
        {
            Dictionary<int, ReceiveOpenLine> lines = new Dictionary<int, ReceiveOpenLine>();

            List<int> selectedLineIds = new List<int>();
            if (lineIds != null)
            {
                foreach (int id in lineIds)
                {
                    if (id > 0 && !selectedLineIds.Contains(id)) { selectedLineIds.Add(id); }
                }
            }
            if (selectedLineIds.Count == 0) { return lines; }

            List<SqlParameter> parameters = new List<SqlParameter>();
            parameters.Add(new SqlParameter("@AD_Client_ID", ctx.GetAD_Client_ID()));
            parameters.Add(new SqlParameter("@PO_ID", poId));

            List<string> lineIdParameters = new List<string>();
            for (int i = 0; i < selectedLineIds.Count; i++)
            {
                string parameterName = "@PO_Line_ID" + i.ToString(CultureInfo.InvariantCulture);
                lineIdParameters.Add(parameterName);
                parameters.Add(new SqlParameter(parameterName, selectedLineIds[i]));
            }

            string sql = @"
                SELECT ol.C_OrderLine_ID AS PO_Line_ID,
                       COALESCE(ol.M_Warehouse_ID, o.M_Warehouse_ID) AS Warehouse_ID,
                       COALESCE(ol.QtyOrdered, 0) - COALESCE(ol.QtyDelivered, 0) - __VAS_UNPOSTED_GRN_QTY__ AS Open_Qty
                FROM C_Order o
                INNER JOIN C_OrderLine ol ON (ol.C_Order_ID=o.C_Order_ID)
                /* Correction 2026-07-18: create-time validation mirrors the
                   display - only ITEM product lines are receivable. */
                INNER JOIN M_Product p ON (p.M_Product_ID=ol.M_Product_ID AND p.IsActive='Y' AND p.ProductType='I')
                WHERE o.IsActive='Y'
                  AND ol.IsActive='Y'
                  AND o.IsSOTrx='N'
                  AND o.DocStatus='CO'
                  AND o.AD_Client_ID=@AD_Client_ID
                  AND o.C_Order_ID=@PO_ID
                  AND ol.C_OrderLine_ID IN (" + string.Join(",", lineIdParameters) + @")
                  AND (COALESCE(ol.QtyOrdered, 0) - COALESCE(ol.QtyDelivered, 0) - __VAS_UNPOSTED_GRN_QTY__) > 0";

            sql = MRole.GetDefault(ctx).AddAccessSQL(sql, "o", MRole.SQL_FULLYQUALIFIED, MRole.SQL_RO);

            // Same in-progress-GRN netting as the display query, so CreateGRN
            // validates each line against the truly-open quantity and cannot
            // accept a duplicate/over-receipt. Injected after the access SQL.
            sql = sql.Replace("__VAS_UNPOSTED_GRN_QTY__", @"(
                      SELECT COALESCE(SUM(il.MovementQty), 0)
                      FROM M_InOut i
                      INNER JOIN M_InOutLine il ON (i.M_InOut_ID=il.M_InOut_ID)
                      WHERE il.C_OrderLine_ID=ol.C_OrderLine_ID
                        AND il.IsActive='Y'
                        AND i.DocStatus NOT IN ('RE','VO','CL','CO')
                  )");

            IDataReader dr = null;
            try
            {
                dr = DB.ExecuteReader(sql, parameters.ToArray());
                while (dr != null && dr.Read())
                {
                    int lineId = Util.GetValueOfInt(dr["PO_Line_ID"]);
                    lines[lineId] = new ReceiveOpenLine
                    {
                        WarehouseId = Util.GetValueOfInt(dr["Warehouse_ID"]),
                        OpenQty = Util.GetValueOfDecimal(dr["Open_Qty"])
                    };
                }
            }
            finally
            {
                if (dr != null) { dr.Close(); dr.Dispose(); }
            }

            return lines;
        }

        /// <summary>Default locator of a warehouse (any active one as fallback).</summary>
        private int GetDefaultLocatorId(int warehouseId, Trx trx)
        {
            if (warehouseId <= 0) { return 0; }

            return Util.GetValueOfInt(DB.ExecuteScalar(@"
                SELECT Locator.M_Locator_ID
                FROM M_Locator Locator
                WHERE Locator.IsActive='Y'
                  AND Locator.M_Warehouse_ID=@M_Warehouse_ID
                ORDER BY Locator.IsDefault DESC",
                new SqlParameter[] { new SqlParameter("@M_Warehouse_ID", warehouseId) }, trx));
        }

        /// <summary>Human-readable label for a document status code.</summary>
        private string DocStatusLabel(Ctx ctx, string docStatus)
        {
            switch (docStatus)
            {
                case "CO": return Msg.GetMsg(ctx, "Completed") ?? "Completed";
                case "CL": return Msg.GetMsg(ctx, "Closed") ?? "Closed";
                case "IP": return Msg.GetMsg(ctx, "InProgress") ?? "In Progress";
                case "DR": return Msg.GetMsg(ctx, "Drafted") ?? "Drafted";
                case "IN": return Msg.GetMsg(ctx, "Invalid") ?? "Invalid";
                default: return docStatus ?? "";
            }
        }

        /// <summary>
        /// Builds a user-facing save/complete error: the logged model error if
        /// present, else the resolved fallback message key, else the literal
        /// fallback text. Unresolved keys (Msg.GetMsg returns "[Key]") are treated
        /// as missing so the caller never surfaces a raw "[VAS_GRNNotSaved]".
        /// </summary>
        private string GetSaveError(Ctx ctx, string fallbackKey, string fallback)
        {
            ValueNamePair pp = VLogger.RetrieveError();
            string error = pp != null ? pp.GetName() : "";

            if (string.IsNullOrEmpty(error) && pp != null)
            {
                error = ResolveMessage(ctx, pp.GetValue());
            }

            if (string.IsNullOrEmpty(error))
            {
                error = ResolveMessage(ctx, fallbackKey);
            }

            return string.IsNullOrEmpty(error) ? fallback : error;
        }

        /// <summary>Resolves an AD_Message key, returning "" when it does not resolve.</summary>
        private string ResolveMessage(Ctx ctx, string key)
        {
            if (string.IsNullOrEmpty(key)) { return ""; }
            string text = Msg.GetMsg(ctx, key);
            if (string.IsNullOrEmpty(text) || text.StartsWith("[")) { return ""; }
            return text;
        }

        /// <summary>Received-line input from the receive form.</summary>
        private sealed class ReceiveLineInput
        {
            public int PoLineId { get; set; }
            public decimal ReceivedQty { get; set; }
            public int LocatorId { get; set; }
        }

        /// <summary>Live open-quantity snapshot for one PO line.</summary>
        private sealed class ReceiveOpenLine
        {
            public int WarehouseId { get; set; }
            public decimal OpenQty { get; set; }
        }

        /// <summary>
        /// One page of vendor GRNs for label printing, filtered by an optional
        /// document-number / supplier-name search.
        /// </summary>
        /// <param name="searchText">Optional GRN number or supplier name fragment.</param>
        /// <param name="pageNo">1-based page number.</param>
        /// <param name="pageSize">Rows per page (max 10).</param>
        /// <returns>JSON { rows[], pageNo, pageSize, totalRecords, totalPages }.</returns>
        [AjaxAuthorizeAttribute]
        [AjaxSessionFilterAttribute]
        public JsonResult SearchGRNLabels(string searchText = "", int pageNo = 1, int pageSize = 5)
        {
            if (Session["ctx"] == null)
            {
                return Fail(Msg.GetMsg(Env.GetCtx(), "SessionExpired") ?? "Session Expired");
            }

            Ctx ctx = Session["ctx"] as Ctx;

            if (pageNo <= 0) { pageNo = 1; }
            if (pageSize <= 0) { pageSize = 5; }
            if (pageSize > 50) { pageSize = 50; }

            int offset = (pageNo - 1) * pageSize;
            string trimmedSearch = (searchText ?? "").Trim();
            bool hasSearch = !string.IsNullOrEmpty(trimmedSearch);
            string searchWhere = "";

            if (hasSearch)
            {
                searchWhere = @"
                      AND (UPPER(InOut.DocumentNo) LIKE UPPER(@Search_DocumentNo)
                           OR UPPER(BPartner.Name) LIKE UPPER(@Search_PartnerName))";
            }

            string rawLabelSql = @"
                SELECT InOut.M_InOut_ID AS GRN_ID,
                       InOut.DocumentNo AS GRN_No,
                       BPartner.Name AS Party_Name,
                       COALESCE(InOutLine.MovementQty, 0) AS Line_Qty,
                       COALESCE(InOut.DateReceived, InOut.MovementDate, InOut.Created) AS Sort_Date
                FROM M_InOut InOut
                INNER JOIN C_BPartner BPartner ON (BPartner.C_BPartner_ID=InOut.C_BPartner_ID AND BPartner.IsActive='Y')
                LEFT OUTER JOIN M_InOutLine InOutLine ON (InOutLine.M_InOut_ID=InOut.M_InOut_ID AND InOutLine.IsActive='Y')
                LEFT OUTER JOIN C_DocType DocType ON (DocType.C_DocType_ID=InOut.C_DocType_ID AND DocType.IsActive='Y')
                WHERE InOut.IsActive='Y'
                  AND InOut.MovementType='V+'
                  AND InOut.AD_Client_ID=@AD_Client_ID" + searchWhere;

            rawLabelSql = MRole.GetDefault(ctx).AddAccessSQL(
                rawLabelSql,
                "InOut",
                MRole.SQL_FULLYQUALIFIED,
                MRole.SQL_RO
            );

            string sql = @"
                SELECT LabelData.GRN_ID,
                       LabelData.GRN_No,
                       LabelData.Party_Name,
                       LabelData.Received_Qty,
                       LabelData.TotalRecords
                FROM (
                    SELECT RawData.GRN_ID,
                           RawData.GRN_No,
                           RawData.Party_Name,
                           COALESCE(SUM(RawData.Line_Qty), 0) AS Received_Qty,
                           MAX(RawData.Sort_Date) AS Sort_Date,
                           COUNT(1) OVER () AS TotalRecords
                    FROM (
                        " + rawLabelSql + @"
                    ) RawData
                    GROUP BY RawData.GRN_ID,
                             RawData.GRN_No,
                             RawData.Party_Name
                ) LabelData
                ORDER BY LabelData.Sort_Date DESC, LabelData.GRN_No DESC
                OFFSET @Offset ROWS FETCH NEXT @PageSize ROWS ONLY";

            List<SqlParameter> parameters = new List<SqlParameter>();
            parameters.Add(new SqlParameter("@AD_Client_ID", ctx.GetAD_Client_ID()));
            if (hasSearch)
            {
                string searchLike = "%" + trimmedSearch + "%";
                parameters.Add(new SqlParameter("@Search_DocumentNo", searchLike));
                parameters.Add(new SqlParameter("@Search_PartnerName", searchLike));
            }
            parameters.Add(new SqlParameter("@Offset", offset));
            parameters.Add(new SqlParameter("@PageSize", pageSize));

            List<object> rows = new List<object>();
            int totalRecords = 0;
            IDataReader dr = null;

            try
            {
                dr = DB.ExecuteReader(sql, parameters.ToArray());

                while (dr != null && dr.Read())
                {
                    totalRecords = Util.GetValueOfInt(dr["TotalRecords"]);

                    rows.Add(new
                    {
                        grnId = Util.GetValueOfInt(dr["GRN_ID"]),
                        grnNo = Util.GetValueOfString(dr["GRN_No"]),
                        partyName = Util.GetValueOfString(dr["Party_Name"]),
                        receivedQty = Util.GetValueOfDecimal(dr["Received_Qty"]),
                    });
                }

                return Ok(new
                {
                    rows = rows,
                    pageNo = pageNo,
                    pageSize = pageSize,
                    totalRecords = totalRecords,
                    totalPages = pageSize == 0 ? 0 : Convert.ToInt32(Math.Ceiling((decimal)totalRecords / pageSize))
                });
            }
            catch (Exception ex)
            {
                return Fail(ex.Message);
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
        /// Resolves the print metadata (copies, print format) for one GRN and returns
        /// a queued-label confirmation payload.
        /// </summary>
        /// <param name="grnId">M_InOut_ID of the GRN to print.</param>
        /// <returns>JSON { success, grnId, grnNo, ... } or { error }.</returns>
        [HttpPost]
        [AjaxAuthorizeAttribute]
        [AjaxSessionFilterAttribute]
        public JsonResult QueueGRNLabelPrint(int grnId = 0)
        {
            if (Session["ctx"] == null)
            {
                return Fail(Msg.GetMsg(Env.GetCtx(), "SessionExpired") ?? "Session Expired");
            }

            if (grnId <= 0)
            {
                return Fail("GRN is required.");
            }

            Ctx ctx = Session["ctx"] as Ctx;

            try
            {
                int printProcessId = GetGRNPrintProcessId(ctx);
                int inOutTableId = GetTableId("M_InOut");

                return Ok(new
                {
                    AD_Process_ID = printProcessId,
                    AD_Table_ID = inOutTableId
                });
            }
            catch (Exception ex)
            {
                return Fail(ex.Message);
            }
        }

        /* ────────────────────────────────────────────────────────────────────
         * Review #30: Complete GRN Confirmation flow (list → detail → line).
         * Confirmation status: Completed = DocStatus CO/CL, In Dispute =
         * IsInDispute 'Y', otherwise Pending. Completion runs the standard
         * document engine (MInOutConfirm.ProcessIt), never a direct update.
         * ──────────────────────────────────────────────────────────────────── */

        /// <summary>Maps a confirmation's DocStatus + dispute flag to the flow status.</summary>
        private static string ConfirmationStatus(string docStatus, string inDispute)
        {
            if (docStatus == "CO" || docStatus == "CL") { return "completed"; }
            if (inDispute == "Y") { return "dispute"; }
            return "pending";
        }

        /// <summary>
        /// One page of vendor GRN confirmations (newest first, open ones before
        /// completed ones kept for traceability).
        /// </summary>
        /// <param name="pageNo">1-based page number.</param>
        /// <param name="pageSize">Rows per page.</param>
        /// <returns>JSON { rows[], pageNo, pageSize, totalRecords, totalPages, pendingCount }.</returns>
        [AjaxAuthorizeAttribute]
        [AjaxSessionFilterAttribute]
        public JsonResult GetGRNConfirmations(int pageNo = 1, int pageSize = 6)
        {
            if (Session["ctx"] == null)
            {
                return Fail(Msg.GetMsg(Env.GetCtx(), "SessionExpired") ?? "Session Expired");
            }

            Ctx ctx = Session["ctx"] as Ctx;

            if (pageNo <= 0) { pageNo = 1; }
            if (pageSize <= 0) { pageSize = 6; }
            if (pageSize > 50) { pageSize = 50; }

            int offset = (pageNo - 1) * pageSize;

            string rawSql = @"
                SELECT Confirm.M_InOutConfirm_ID AS Confirm_ID,
                       Confirm.DocumentNo AS Confirm_No,
                       Confirm.DocStatus AS Doc_Status,
                       COALESCE(CAST(Confirm.IsInDispute AS VARCHAR(1)),'N') AS In_Dispute,
                       Confirm.Created AS Created_Date,
                       InOut.DocumentNo AS GRN_No,
                       BPartner.Name AS Supplier_Name,
                       LineConfirm.M_InOutLineConfirm_ID AS Line_Confirm_ID
                FROM M_InOutConfirm Confirm
                INNER JOIN M_InOut InOut ON (InOut.M_InOut_ID=Confirm.M_InOut_ID AND InOut.IsActive='Y')
                INNER JOIN C_BPartner BPartner ON (BPartner.C_BPartner_ID=InOut.C_BPartner_ID AND BPartner.IsActive='Y')
                LEFT OUTER JOIN M_InOutLineConfirm LineConfirm ON (LineConfirm.M_InOutConfirm_ID=Confirm.M_InOutConfirm_ID AND LineConfirm.IsActive='Y')
                WHERE Confirm.IsActive='Y'
                  AND InOut.IsSOTrx='N'
                  AND InOut.MovementType='V+'
                  AND Confirm.AD_Client_ID=@AD_Client_ID
                  AND Confirm.DocStatus IN ('DR','IP')";

            rawSql = MRole.GetDefault(ctx).AddAccessSQL(
                rawSql,
                "Confirm",
                MRole.SQL_FULLYQUALIFIED,
                MRole.SQL_RO
            );

            string sql = @"
                SELECT ConfirmData.Confirm_ID,
                       ConfirmData.Confirm_No,
                       ConfirmData.Doc_Status,
                       ConfirmData.In_Dispute,
                       ConfirmData.GRN_No,
                       ConfirmData.Supplier_Name,
                       ConfirmData.Line_Count,
                       COUNT(1) OVER () AS TotalRecords,
                       SUM(CASE WHEN ConfirmData.Doc_Status IN ('CO','CL') THEN 0 ELSE 1 END) OVER () AS PendingCount
                FROM (
                    SELECT RawData.Confirm_ID,
                           RawData.Confirm_No,
                           RawData.Doc_Status,
                           RawData.In_Dispute,
                           RawData.Created_Date,
                           RawData.GRN_No,
                           RawData.Supplier_Name,
                           COUNT(RawData.Line_Confirm_ID) AS Line_Count
                    FROM (
                        " + rawSql + @"
                    ) RawData
                    GROUP BY RawData.Confirm_ID,
                             RawData.Confirm_No,
                             RawData.Doc_Status,
                             RawData.In_Dispute,
                             RawData.Created_Date,
                             RawData.GRN_No,
                             RawData.Supplier_Name
                ) ConfirmData
                ORDER BY CASE WHEN ConfirmData.Doc_Status IN ('CO','CL') THEN 1 ELSE 0 END,
                         ConfirmData.Created_Date DESC,
                         ConfirmData.Confirm_ID DESC
                OFFSET @Offset ROWS FETCH NEXT @PageSize ROWS ONLY";

            List<SqlParameter> parameters = new List<SqlParameter>();
            parameters.Add(new SqlParameter("@AD_Client_ID", ctx.GetAD_Client_ID()));
            parameters.Add(new SqlParameter("@Offset", offset));
            parameters.Add(new SqlParameter("@PageSize", pageSize));

            List<object> rows = new List<object>();
            int totalRecords = 0;
            int pendingCount = 0;
            IDataReader dr = null;

            try
            {
                dr = DB.ExecuteReader(sql, parameters.ToArray());

                while (dr != null && dr.Read())
                {
                    totalRecords = Util.GetValueOfInt(dr["TotalRecords"]);
                    pendingCount = Util.GetValueOfInt(dr["PendingCount"]);

                    rows.Add(new
                    {
                        confirmId = Util.GetValueOfInt(dr["Confirm_ID"]),
                        confirmNo = Util.GetValueOfString(dr["Confirm_No"]),
                        grnNo = Util.GetValueOfString(dr["GRN_No"]),
                        supplier = Util.GetValueOfString(dr["Supplier_Name"]),
                        lineCount = Util.GetValueOfInt(dr["Line_Count"]),
                        status = ConfirmationStatus(Util.GetValueOfString(dr["Doc_Status"]), Util.GetValueOfString(dr["In_Dispute"]))
                    });
                }

                return Ok(new
                {
                    rows = rows,
                    pageNo = pageNo,
                    pageSize = pageSize,
                    totalRecords = totalRecords,
                    totalPages = pageSize == 0 ? 0 : Convert.ToInt32(Math.Ceiling((decimal)totalRecords / pageSize)),
                    pendingCount = pendingCount
                });
            }
            catch (Exception ex)
            {
                return Fail(ex.Message);
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
        /// One GRN confirmation's header context and its lines for the detail state.
        /// </summary>
        /// <param name="confirmId">M_InOutConfirm_ID.</param>
        /// <returns>JSON { header, lines[] }.</returns>
        [AjaxAuthorizeAttribute]
        [AjaxSessionFilterAttribute]
        public JsonResult GetGRNConfirmationDetail(int confirmId = 0)
        {
            if (Session["ctx"] == null)
            {
                return Fail(Msg.GetMsg(Env.GetCtx(), "SessionExpired") ?? "Session Expired");
            }

            Ctx ctx = Session["ctx"] as Ctx;
            if (confirmId <= 0) { return Fail(Msg.GetMsg(ctx, "NotFound") ?? "Not found"); }

            string headerSql = @"
                SELECT Confirm.M_InOutConfirm_ID AS Confirm_ID,
                       Confirm.DocumentNo AS Confirm_No,
                       Confirm.DocStatus AS Doc_Status,
                       COALESCE(CAST(Confirm.IsInDispute AS VARCHAR(1)),'N') AS In_Dispute,
                       InOut.DocumentNo AS GRN_No,
                       BPartner.Name AS Supplier_Name,
                       Warehouse.Name AS Warehouse_Name,
                       COALESCE(InOut.MovementDate, Confirm.Created) AS Doc_Date
                FROM M_InOutConfirm Confirm
                INNER JOIN M_InOut InOut ON (InOut.M_InOut_ID=Confirm.M_InOut_ID AND InOut.IsActive='Y')
                INNER JOIN C_BPartner BPartner ON (BPartner.C_BPartner_ID=InOut.C_BPartner_ID AND BPartner.IsActive='Y')
                LEFT OUTER JOIN M_Warehouse Warehouse ON (Warehouse.M_Warehouse_ID=InOut.M_Warehouse_ID AND Warehouse.IsActive='Y')
                WHERE Confirm.IsActive='Y'
                  AND Confirm.M_InOutConfirm_ID=@Confirm_ID
                  AND Confirm.AD_Client_ID=@AD_Client_ID";

            headerSql = MRole.GetDefault(ctx).AddAccessSQL(
                headerSql,
                "Confirm",
                MRole.SQL_FULLYQUALIFIED,
                MRole.SQL_RO
            );

            string linesSql = @"
                SELECT LineConfirm.M_InOutLineConfirm_ID AS Line_Confirm_ID,
                       InOutLine.Line AS Line_No,
                       Product.Name AS Product_Name,
                       UomInfo.Name AS UOM_Name,
                       AttributeInstance.Description AS Attribute_Description,
                       Locator.Value AS Locator_Value,
                       LineConfirm.TargetQty AS Target_Qty,
                       LineConfirm.ConfirmedQty AS Confirmed_Qty,
                       LineConfirm.ScrappedQty AS Scrapped_Qty,
                       LineConfirm.DifferenceQty AS Difference_Qty,
                       LineConfirm.Description AS Line_Description,
                       COALESCE(CAST(LineConfirm.Processed AS VARCHAR(1)),'N') AS Line_Processed
                FROM M_InOutLineConfirm LineConfirm
                INNER JOIN M_InOutLine InOutLine ON (InOutLine.M_InOutLine_ID=LineConfirm.M_InOutLine_ID AND InOutLine.IsActive='Y')
                LEFT OUTER JOIN M_Product Product ON (Product.M_Product_ID=InOutLine.M_Product_ID AND Product.IsActive='Y')
                LEFT OUTER JOIN C_UOM UomInfo ON (UomInfo.C_UOM_ID=InOutLine.C_UOM_ID AND UomInfo.IsActive='Y')
                LEFT OUTER JOIN M_AttributeSetInstance AttributeInstance ON (AttributeInstance.M_AttributeSetInstance_ID=InOutLine.M_AttributeSetInstance_ID)
                LEFT OUTER JOIN M_Locator Locator ON (Locator.M_Locator_ID=InOutLine.M_Locator_ID AND Locator.IsActive='Y')
                WHERE LineConfirm.IsActive='Y'
                  AND LineConfirm.M_InOutConfirm_ID=@Line_Confirm_Parent_ID
                  AND LineConfirm.AD_Client_ID=@Line_AD_Client_ID";

            linesSql = MRole.GetDefault(ctx).AddAccessSQL(
                linesSql,
                "LineConfirm",
                MRole.SQL_FULLYQUALIFIED,
                MRole.SQL_RO
            );
            linesSql += @"
                ORDER BY InOutLine.Line, LineConfirm.M_InOutLineConfirm_ID";

            IDataReader dr = null;

            try
            {
                object header = null;
                dr = DB.ExecuteReader(headerSql, new SqlParameter[]
                {
                    new SqlParameter("@Confirm_ID", confirmId),
                    new SqlParameter("@AD_Client_ID", ctx.GetAD_Client_ID())
                });

                if (dr != null && dr.Read())
                {
                    DateTime? docDate = Util.GetValueOfDateTime(dr["Doc_Date"]);
                    header = new
                    {
                        confirmId = Util.GetValueOfInt(dr["Confirm_ID"]),
                        confirmNo = Util.GetValueOfString(dr["Confirm_No"]),
                        grnNo = Util.GetValueOfString(dr["GRN_No"]),
                        supplier = Util.GetValueOfString(dr["Supplier_Name"]),
                        warehouseName = Util.GetValueOfString(dr["Warehouse_Name"]),
                        docDate = docDate.HasValue ? docDate.Value.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture) : "",
                        status = ConfirmationStatus(Util.GetValueOfString(dr["Doc_Status"]), Util.GetValueOfString(dr["In_Dispute"]))
                    };
                }

                dr.Close();
                dr.Dispose();
                dr = null;

                if (header == null) { return Fail(Msg.GetMsg(ctx, "NotFound") ?? "Not found"); }

                List<object> lines = new List<object>();
                dr = DB.ExecuteReader(linesSql, new SqlParameter[]
                {
                    new SqlParameter("@Line_Confirm_Parent_ID", confirmId),
                    new SqlParameter("@Line_AD_Client_ID", ctx.GetAD_Client_ID())
                });

                while (dr != null && dr.Read())
                {
                    lines.Add(new
                    {
                        lineConfirmId = Util.GetValueOfInt(dr["Line_Confirm_ID"]),
                        lineNo = Util.GetValueOfInt(dr["Line_No"]),
                        productName = Util.GetValueOfString(dr["Product_Name"]),
                        uomName = Util.GetValueOfString(dr["UOM_Name"]),
                        attributeSetInstance = Util.GetValueOfString(dr["Attribute_Description"]),
                        locatorValue = Util.GetValueOfString(dr["Locator_Value"]),
                        targetQty = Util.GetValueOfDecimal(dr["Target_Qty"]),
                        confirmedQty = Util.GetValueOfDecimal(dr["Confirmed_Qty"]),
                        scrappedQty = Util.GetValueOfDecimal(dr["Scrapped_Qty"]),
                        differenceQty = Util.GetValueOfDecimal(dr["Difference_Qty"]),
                        description = Util.GetValueOfString(dr["Line_Description"]),
                        processed = Util.GetValueOfString(dr["Line_Processed"]) == "Y"
                    });
                }

                return Ok(new { header = header, lines = lines });
            }
            catch (Exception ex)
            {
                return Fail(ex.Message);
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
        /// Saves one confirmation line's entry fields. DifferenceQty is computed
        /// by the model (Target - Confirmed - Scrapped) on save.
        /// </summary>
        [AjaxAuthorizeAttribute]
        [AjaxSessionFilterAttribute]
        [HttpPost]
        public JsonResult SaveGRNConfirmationLine(int lineConfirmId = 0, string targetQty = "", string confirmedQty = "", string scrappedQty = "", string description = "")
        {
            if (Session["ctx"] == null)
            {
                return Fail(Msg.GetMsg(Env.GetCtx(), "SessionExpired") ?? "Session Expired");
            }

            Ctx ctx = Session["ctx"] as Ctx;
            if (lineConfirmId <= 0) { return Fail(Msg.GetMsg(ctx, "NotFound") ?? "Not found"); }

            decimal target, confirmed, scrapped;
            if (!decimal.TryParse(targetQty, System.Globalization.NumberStyles.Any, CultureInfo.InvariantCulture, out target)
                || !decimal.TryParse(confirmedQty, System.Globalization.NumberStyles.Any, CultureInfo.InvariantCulture, out confirmed)
                || !decimal.TryParse(String.IsNullOrEmpty(scrappedQty) ? "0" : scrappedQty, System.Globalization.NumberStyles.Any, CultureInfo.InvariantCulture, out scrapped)
                || target < 0 || confirmed < 0 || scrapped < 0)
            {
                return Fail(Msg.GetMsg(ctx, "VAS_090_InvalidQuantity") ?? "Quantities must be zero or positive numbers.");
            }

            try
            {
                MInOutLineConfirm line = new MInOutLineConfirm(ctx, lineConfirmId, null);
                if (line.Get_ID() <= 0 || line.GetAD_Client_ID() != ctx.GetAD_Client_ID())
                {
                    return Fail(Msg.GetMsg(ctx, "NotFound") ?? "Not found");
                }

                MInOutConfirm parent = new MInOutConfirm(ctx, line.GetM_InOutConfirm_ID(), null);
                if (parent.IsProcessed() || parent.GetDocStatus() == "CO" || parent.GetDocStatus() == "CL")
                {
                    return Fail(Msg.GetMsg(ctx, "VAS_090_ConfirmationCompleted") ?? "This GRN confirmation is already completed.");
                }

                line.SetTargetQty(target);
                line.SetConfirmedQty(confirmed);
                line.SetScrappedQty(scrapped);
                line.SetDescription(description ?? "");

                if (!line.Save())
                {
                    return Fail(Msg.GetMsg(ctx, "SaveError") ?? "Save failed.");
                }

                return Ok(new
                {
                    success = true,
                    lineConfirmId = lineConfirmId,
                    differenceQty = line.GetDifferenceQty()
                });
            }
            catch (Exception ex)
            {
                return Fail(ex.Message);
            }
        }

        /// <summary>
        /// Completes the whole GRN confirmation through the document engine.
        /// </summary>
        [AjaxAuthorizeAttribute]
        [AjaxSessionFilterAttribute]
        [HttpPost]
        public JsonResult CompleteGRNConfirmation(int confirmId = 0)
        {
            if (Session["ctx"] == null)
            {
                return Fail(Msg.GetMsg(Env.GetCtx(), "SessionExpired") ?? "Session Expired");
            }

            Ctx ctx = Session["ctx"] as Ctx;
            if (confirmId <= 0) { return Fail(Msg.GetMsg(ctx, "NotFound") ?? "Not found"); }

            try
            {
                MInOutConfirm confirm = new MInOutConfirm(ctx, confirmId, null);
                if (confirm.Get_ID() <= 0 || confirm.GetAD_Client_ID() != ctx.GetAD_Client_ID())
                {
                    return Fail(Msg.GetMsg(ctx, "NotFound") ?? "Not found");
                }

                if (confirm.GetDocStatus() == "CO" || confirm.GetDocStatus() == "CL")
                {
                    return Fail(Msg.GetMsg(ctx, "VAS_090_ConfirmationCompleted") ?? "This GRN confirmation is already completed.");
                }

                bool processed = confirm.ProcessIt(X_M_InOutConfirm.DOCACTION_Complete);
                confirm.Save();

                if (!processed || !(confirm.GetDocStatus() == "CO" || confirm.GetDocStatus() == "CL"))
                {
                    string processMsg = confirm.GetProcessMsg();
                    return Fail(!String.IsNullOrEmpty(processMsg)
                        ? processMsg
                        : (Msg.GetMsg(ctx, "VAS_090_CompleteFailed") ?? "GRN confirmation could not be completed."));
                }

                return Ok(new { success = true, status = "completed" });
            }
            catch (Exception ex)
            {
                return Fail(ex.Message);
            }
        }

        /// <summary>
        /// Marks the whole GRN confirmation as In Dispute (status only; lines
        /// stay open for review per the flow spec).
        /// </summary>
        [AjaxAuthorizeAttribute]
        [AjaxSessionFilterAttribute]
        [HttpPost]
        public JsonResult DisputeGRNConfirmation(int confirmId = 0)
        {
            if (Session["ctx"] == null)
            {
                return Fail(Msg.GetMsg(Env.GetCtx(), "SessionExpired") ?? "Session Expired");
            }

            Ctx ctx = Session["ctx"] as Ctx;
            if (confirmId <= 0) { return Fail(Msg.GetMsg(ctx, "NotFound") ?? "Not found"); }

            try
            {
                MInOutConfirm confirm = new MInOutConfirm(ctx, confirmId, null);
                if (confirm.Get_ID() <= 0 || confirm.GetAD_Client_ID() != ctx.GetAD_Client_ID())
                {
                    return Fail(Msg.GetMsg(ctx, "NotFound") ?? "Not found");
                }

                if (confirm.GetDocStatus() == "CO" || confirm.GetDocStatus() == "CL")
                {
                    return Fail(Msg.GetMsg(ctx, "VAS_090_ConfirmationCompleted") ?? "This GRN confirmation is already completed.");
                }

                confirm.SetIsInDispute(true);
                if (!confirm.Save())
                {
                    return Fail(Msg.GetMsg(ctx, "SaveError") ?? "Save failed.");
                }

                return Ok(new { success = true, status = "dispute" });
            }
            catch (Exception ex)
            {
                return Fail(ex.Message);
            }
        }

        /// <summary>
        /// Resolves the print AD_Process_ID from the Material Receipt (GRN) tab's
        /// AD_Tab.AD_Process_ID - the same field the framework's tab objects expose
        /// client-side as getAD_Process_ID() (see VAS_065_APInvoicePanel.js), instead
        /// of a Document Type column or a hardcoded AD_Process.Value. Prefers the tab
        /// scoped to the "VAS_MaterialReceipt" / "Material Receipt" window, then falls
        /// back to any active window carrying an M_InOut tab. Returns 0 when no tab has
        /// a report process configured, so the print confirmation can still be shown.
        /// </summary>
        /// <param name="ctx">Session context (for the active client).</param>
        /// <returns>AD_Process_ID, or 0 when not found.</returns>
        private int GetGRNPrintProcessId(Ctx ctx)
        {
            try
            {
                int tableId = GetTableId("M_InOut");
                if (tableId <= 0) { return 0; }

                int windowId = GetReceivingWindowId(ctx);
                if (windowId > 0)
                {
                    SqlParameter[] windowParameters =
                    {
                        new SqlParameter("@AD_Table_ID", tableId),
                        new SqlParameter("@AD_Window_ID", windowId)
                    };

                    int processId = Util.GetValueOfInt(DB.ExecuteScalar(@"
                        SELECT AD_Tab.AD_Process_ID
                        FROM AD_Tab AD_Tab                        
                        WHERE AD_Tab.AD_Table_ID = @AD_Table_ID
                          AND AD_Tab.AD_Window_ID = @AD_Window_ID
                          AND AD_Tab.AD_Process_ID IS NOT NULL
                          AND AD_Tab.AD_Process_ID > 0
                          AND AD_Tab.IsActive = 'Y'                          
                        ORDER BY AD_Tab.SeqNo", windowParameters, null));

                    if (processId > 0)
                    {
                        return processId;
                    }
                }
                return 0;
            }
            catch
            {
                return 0;
            }
        }

        /// <summary>Active, role-accessible AD_Window id for the Material Receipt (GRN) window, or 0.</summary>
        /// <param name="ctx">Session context (for the active client).</param>
        /// <returns>AD_Window_ID, or 0 when not found.</returns>
        private int GetReceivingWindowId(Ctx ctx)
        {
            int windowId = GetWindowIdByName(ctx, "VAS_MaterialReceipt");
            return windowId;
        }

        /// <summary>Active, role-accessible AD_Window id for one window name, or 0.</summary>
        /// <param name="ctx">Session context (for the active client).</param>
        /// <param name="windowName">AD_Window.Name to resolve.</param>
        /// <returns>The highest matching AD_Window_ID visible to the role, or 0.</returns>
        private int GetWindowIdByName(Ctx ctx, string windowName)
        {
            string sql = @"
                SELECT GrnWindow.AD_Window_ID
                FROM AD_Window GrnWindow
                WHERE GrnWindow.IsActive='Y'
                AND GrnWindow.Name=@Window_Name";
            return Util.GetValueOfInt(DB.ExecuteScalar(sql, new SqlParameter[]
            {
                new SqlParameter("@Window_Name", windowName),
            }, null));
        }

        /// <summary>Resolves the AD_Table_ID for a physical table name (0 when missing).</summary>
        /// <param name="tableName">Physical table name, e.g. "M_InOut".</param>
        /// <returns>AD_Table_ID, or 0 when not found.</returns>
        private int GetTableId(string tableName)
        {
            try
            {
                return Util.GetValueOfInt(DB.ExecuteScalar(
                    @"SELECT AD_Table_ID FROM AD_Table WHERE TableName=@TableName AND IsActive='Y'",
                    new SqlParameter[] { new SqlParameter("@TableName", tableName) }, null));
            }
            catch
            {
                return 0;
            }
        }

        /// <summary>
        /// Resolves the AD_Process_ID of a document process by its (stable) Value -
        /// e.g. "M_InOut Process" - preferring a client-specific record, then the
        /// system one. Resolving by Value keeps the completion dynamic (the numeric
        /// id can differ per database).
        /// </summary>
        /// <param name="ctx">Session context (for the active client).</param>
        /// <param name="processValue">AD_Process.Value key.</param>
        /// <returns>AD_Process_ID, or 0 when not found.</returns>
        private int GetProcessIdByValue(Ctx ctx, string processValue)
        {
            try
            {
                return Util.GetValueOfInt(DB.ExecuteScalar(
                    @"SELECT AD_Process_ID
                      FROM AD_Process
                      WHERE Value=@Value
                        AND IsActive='Y'
                        AND AD_Client_ID IN (0, @AD_Client_ID)
                      ORDER BY AD_Client_ID DESC
                      FETCH FIRST 1 ROW ONLY",
                    new SqlParameter[]
                    {
                        new SqlParameter("@Value", processValue),
                        new SqlParameter("@AD_Client_ID", ctx.GetAD_Client_ID())
                    }, null));
            }
            catch
            {
                return 0;
            }
        }

        /// <summary>
        /// Whether a column exists on a table in the active database (PostgreSQL or
        /// Oracle), so optional columns can be referenced safely.
        /// </summary>
        /// <param name="tableName">Physical table name.</param>
        /// <param name="columnName">Column name to test.</param>
        /// <returns>True when the column exists.</returns>
        private bool HasColumn(string tableName, string columnName)
        {
            string sql;
            if (DB.IsPostgreSQL())
            {
                sql = @"
                    SELECT COUNT(1)
                    FROM information_schema.columns
                    WHERE UPPER(table_name)=UPPER(@TableName)
                      AND UPPER(column_name)=UPPER(@ColumnName)";
            }
            else
            {
                sql = @"
                    SELECT COUNT(1)
                    FROM USER_TAB_COLUMNS
                    WHERE TABLE_NAME=UPPER(@TableName)
                      AND COLUMN_NAME=UPPER(@ColumnName)";
            }

            SqlParameter[] parameters =
            {
                new SqlParameter("@TableName", tableName),
                new SqlParameter("@ColumnName", columnName)
            };

            try
            {
                return Util.GetValueOfInt(DB.ExecuteScalar(sql, parameters, null)) > 0;
            }
            catch
            {
                return false;
            }
        }

        /// <summary>Wraps a success payload as a serialized JSON result.</summary>
        /// <param name="result">Anonymous payload object to serialize.</param>
        /// <returns>JSON result.</returns>
        private JsonResult Ok(object result)
        {
            return Json(JsonConvert.SerializeObject(result), JsonRequestBehavior.AllowGet);
        }

        /// <summary>Wraps a failure message as a serialized JSON result.</summary>
        /// <param name="message">User-facing error/message text.</param>
        /// <returns>JSON result with success:false.</returns>
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
