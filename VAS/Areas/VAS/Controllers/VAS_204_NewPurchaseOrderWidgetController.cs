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
    /// Module Name : New Purchase Order Widget (Purchase Order Dashboard Quick Action)
    /// Widget ID   : VAS_204_NewPurchaseOrderWidget
    /// Purpose     : Quick-action widget (1x1) to start the requisition-to-purchase-order flow.
    ///               Loads approved, open purchase requisitions (M_Requisition, DocStatus='CO')
    ///               with remaining pending quantities, extracts lines, and creates purchase orders
    ///               (C_Order, IsSOTrx='N') linked to source requisition lines.
    /// Chronological development:
    ///   Builder Agent 2   2026-08-17 Created
    /// </summary>
    public class VAS_204_NewPurchaseOrderWidgetController : Controller
    {
        private static readonly VLogger Log = VLogger.GetVLogger(typeof(VAS_204_NewPurchaseOrderWidgetController).FullName);

        /// <summary>
        /// Resolves the Purchase Order window ID (VAS_PurchaseOrder or Purchase Order).
        /// </summary>
        /// <returns>JSON { windowId }</returns>
        [AjaxAuthorizeAttribute]
        [AjaxSessionFilterAttribute]
        public JsonResult GetPurchaseOrderWindowId()
        {
            if (Session["ctx"] == null)
            {
                return Fail(Msg.GetMsg(Env.GetCtx(), "SessionExpired") ?? "Session Expired");
            }

            Ctx ctx = Session["ctx"] as Ctx;
            try
            {
                int windowId = GetWindowIdByName(ctx, "VAS_PurchaseOrder");
                if (windowId <= 0)
                {
                    windowId = GetWindowIdByName(ctx, "Purchase Order");
                }
                return Ok(new { windowId = windowId });
            }
            catch (Exception ex)
            {
                Log.Log(Level.SEVERE, "VAS_204_NewPurchaseOrderWidget.GetPurchaseOrderWindowId", ex);
                return Fail(ex.Message);
            }
        }

        /// <summary>
        /// Reads all open, completed requisitions (DocStatus='CO') that have remaining pending quantity.
        /// ANSI-compliant portable SQL query mapping to M_Requisition and M_RequisitionLine.
        /// </summary>
        /// <returns>JSON { rows[], summary }</returns>
        [AjaxAuthorizeAttribute]
        [AjaxSessionFilterAttribute]
        public JsonResult GetOpenRequisitions()
        {
            if (Session["ctx"] == null)
            {
                return Fail(Msg.GetMsg(Env.GetCtx(), "SessionExpired") ?? "Session Expired");
            }

            Ctx ctx = Session["ctx"] as Ctx;

            string rawSql = @"
                SELECT
                    r.M_Requisition_ID AS requisition_id,
                    r.DocumentNo AS requisition_number,
                    r.DateRequired AS needed_by,
                    r.DocStatus AS requisition_doc_status,
                    r.AD_Org_ID AS organization_id,
                    COUNT(rl.M_RequisitionLine_ID) AS line_count,
                    SUM(COALESCE(rl.Qty, 0)) AS requisition_qty,
                    SUM(COALESCE(rl.QtyOrdered, 0)) AS already_ordered_qty,
                    SUM(
                        CASE
                            WHEN COALESCE(rl.Qty, 0) > COALESCE(rl.QtyOrdered, 0)
                            THEN COALESCE(rl.Qty, 0) - COALESCE(rl.QtyOrdered, 0)
                            ELSE 0
                        END
                    ) AS pending_qty
                FROM M_Requisition r
                INNER JOIN M_RequisitionLine rl
                    ON rl.M_Requisition_ID = r.M_Requisition_ID
                   AND rl.IsActive = 'Y'
                WHERE r.AD_Client_ID = @AD_Client_ID
                  AND r.IsActive = 'Y'
                  AND r.DocStatus = 'CO'
                GROUP BY
                    r.M_Requisition_ID,
                    r.DocumentNo,
                    r.DateRequired,
                    r.DocStatus,
                    r.AD_Org_ID
                HAVING SUM(
                    CASE
                        WHEN COALESCE(rl.Qty, 0) > COALESCE(rl.QtyOrdered, 0)
                        THEN COALESCE(rl.Qty, 0) - COALESCE(rl.QtyOrdered, 0)
                        ELSE 0
                    END
                ) > 0
                ORDER BY r.DateRequired, r.DocumentNo";

            string sql = MRole.GetDefault(ctx).AddAccessSQL(
                rawSql,
                "r",
                MRole.SQL_FULLYQUALIFIED,
                MRole.SQL_RO
            );

            List<SqlParameter> parameters = new List<SqlParameter>
            {
                new SqlParameter("@AD_Client_ID", ctx.GetAD_Client_ID())
            };

            List<object> rows = new List<object>();
            int totalOpenRequisitions = 0;
            int readyToPOCount = 0;
            int partlyOrderedCount = 0;
            decimal totalPendingQty = 0;

            IDataReader dr = null;
            try
            {
                dr = DB.ExecuteReader(sql, parameters.ToArray());
                while (dr != null && dr.Read())
                {
                    int reqId = Util.GetValueOfInt(dr["requisition_id"]);
                    string reqNo = Util.GetValueOfString(dr["requisition_number"]);
                    DateTime? needDate = Util.GetValueOfDateTime(dr["needed_by"]);
                    int lineCount = Util.GetValueOfInt(dr["line_count"]);
                    decimal reqQty = Util.GetValueOfDecimal(dr["requisition_qty"]);
                    decimal alreadyOrderedQty = Util.GetValueOfDecimal(dr["already_ordered_qty"]);
                    decimal pendingQty = Util.GetValueOfDecimal(dr["pending_qty"]);
                    int orgId = Util.GetValueOfInt(dr["organization_id"]);

                    string derivedStatus = alreadyOrderedQty > 0 ? "Partly ordered" : "Ready to PO";
                    string statusChip = alreadyOrderedQty > 0 ? "chip-warn" : "chip-ok";

                    totalOpenRequisitions++;
                    if (alreadyOrderedQty > 0)
                    {
                        partlyOrderedCount++;
                    }
                    else
                    {
                        readyToPOCount++;
                    }
                    totalPendingQty += pendingQty;

                    rows.Add(new
                    {
                        requisitionId = reqId,
                        requisitionNumber = reqNo,
                        neededBy = needDate.HasValue ? needDate.Value.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture) : "",
                        neededByDisplay = needDate.HasValue ? needDate.Value.ToString("dd MMM yyyy", CultureInfo.InvariantCulture) : "",
                        lineCount = lineCount,
                        requisitionQty = reqQty,
                        alreadyOrderedQty = alreadyOrderedQty,
                        pendingQty = pendingQty,
                        status = derivedStatus,
                        statusChip = statusChip,
                        organizationId = orgId
                    });
                }

                return Ok(new
                {
                    rows = rows,
                    summary = new
                    {
                        openRequisitions = totalOpenRequisitions,
                        readyToPO = readyToPOCount,
                        partlyOrdered = partlyOrderedCount,
                        pendingQty = totalPendingQty
                    }
                });
            }
            catch (Exception ex)
            {
                Log.Log(Level.SEVERE, "VAS_204_NewPurchaseOrderWidget.GetOpenRequisitions", ex);
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
        /// Returns active pending lines of a specific requisition (M_RequisitionLine).
        /// </summary>
        /// <param name="requisitionId">M_Requisition_ID</param>
        /// <returns>JSON { lines[] }</returns>
        [AjaxAuthorizeAttribute]
        [AjaxSessionFilterAttribute]
        public JsonResult GetRequisitionLines(int requisitionId = 0)
        {
            if (Session["ctx"] == null)
            {
                return Fail(Msg.GetMsg(Env.GetCtx(), "SessionExpired") ?? "Session Expired");
            }

            Ctx ctx = Session["ctx"] as Ctx;
            if (requisitionId <= 0)
            {
                return Fail(Msg.GetMsg(ctx, "VAS_RequisitionRequired") ?? "Requisition ID is required.");
            }

            string rawSql = @"
                SELECT
                    rl.M_RequisitionLine_ID AS line_id,
                    rl.M_Requisition_ID AS requisition_id,
                    rl.Line AS line_no,
                    rl.M_Product_ID AS product_id,
                    COALESCE(p.Name, rl.Description, N'') AS product_name,
                    COALESCE(p.Value, N'') AS product_code,
                    COALESCE(u.UOMSymbol, u.Name, N'') AS uom,
                    COALESCE(rl.Qty, 0) AS req_qty,
                    COALESCE(rl.QtyOrdered, 0) AS already_ordered_qty,
                    CASE
                        WHEN COALESCE(rl.Qty, 0) > COALESCE(rl.QtyOrdered, 0)
                        THEN COALESCE(rl.Qty, 0) - COALESCE(rl.QtyOrdered, 0)
                        ELSE 0
                    END AS pending_qty,
                    COALESCE(rl.PriceActual, p.PriceStd, 0) AS rate,
                    COALESCE(bp.Name, N'') AS vendor_name,
                    COALESCE(rl.C_BPartner_ID, p.C_BPartner_ID, 0) AS vendor_id,
                    COALESCE(rl.Description, N'') AS line_description,
                    COALESCE(rl.Description, N'Standard') AS attribute_info
                FROM M_RequisitionLine rl
                INNER JOIN M_Requisition r ON (r.M_Requisition_ID = rl.M_Requisition_ID)
                LEFT JOIN M_Product p ON (p.M_Product_ID = rl.M_Product_ID AND p.IsActive = 'Y')
                LEFT JOIN C_UOM u ON (u.C_UOM_ID = COALESCE(rl.C_UOM_ID, p.C_UOM_ID) AND u.IsActive = 'Y')
                LEFT JOIN C_BPartner bp ON (bp.C_BPartner_ID = COALESCE(rl.C_BPartner_ID, p.C_BPartner_ID) AND bp.IsActive = 'Y')
                WHERE rl.M_Requisition_ID = @Requisition_ID
                  AND rl.IsActive = 'Y'
                  AND r.IsActive = 'Y'
                  AND r.DocStatus = 'CO'
                  AND COALESCE(rl.Qty, 0) > COALESCE(rl.QtyOrdered, 0)
                ORDER BY rl.Line ASC";

            string sql = MRole.GetDefault(ctx).AddAccessSQL(
                rawSql,
                "r",
                MRole.SQL_FULLYQUALIFIED,
                MRole.SQL_RO
            );

            List<SqlParameter> parameters = new List<SqlParameter>
            {
                new SqlParameter("@Requisition_ID", requisitionId)
            };

            List<object> lines = new List<object>();
            IDataReader dr = null;
            try
            {
                dr = DB.ExecuteReader(sql, parameters.ToArray());
                while (dr != null && dr.Read())
                {
                    decimal reqQty = Util.GetValueOfDecimal(dr["req_qty"]);
                    decimal orderedQty = Util.GetValueOfDecimal(dr["already_ordered_qty"]);
                    decimal pendingQty = Util.GetValueOfDecimal(dr["pending_qty"]);
                    decimal rate = Util.GetValueOfDecimal(dr["rate"]);

                    lines.Add(new
                    {
                        lineId = Util.GetValueOfInt(dr["line_id"]),
                        requisitionId = Util.GetValueOfInt(dr["requisition_id"]),
                        lineNo = Util.GetValueOfInt(dr["line_no"]),
                        productId = Util.GetValueOfInt(dr["product_id"]),
                        productName = Util.GetValueOfString(dr["product_name"]),
                        productCode = Util.GetValueOfString(dr["product_code"]),
                        uom = Util.GetValueOfString(dr["uom"]),
                        reqQty = reqQty,
                        alreadyOrderedQty = orderedQty,
                        pendingQty = pendingQty,
                        orderQty = pendingQty,
                        rate = rate,
                        amount = pendingQty * rate,
                        vendorName = Util.GetValueOfString(dr["vendor_name"]),
                        vendorId = Util.GetValueOfInt(dr["vendor_id"]),
                        description = Util.GetValueOfString(dr["line_description"]),
                        attribute = Util.GetValueOfString(dr["attribute_info"])
                    });
                }

                return Ok(new { lines = lines });
            }
            catch (Exception ex)
            {
                Log.Log(Level.SEVERE, "VAS_204_NewPurchaseOrderWidget.GetRequisitionLines", ex);
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
        /// Returns lookup references required for the PO creation form (vendors, warehouses, payment terms, doc types, taxes, currencies).
        /// </summary>
        /// <returns>JSON { vendors[], warehouses[], paymentTerms[], docTypes[], taxes[], currencies[] }</returns>
        [AjaxAuthorizeAttribute]
        [AjaxSessionFilterAttribute]
        public JsonResult GetFormLookups()
        {
            if (Session["ctx"] == null)
            {
                return Fail(Msg.GetMsg(Env.GetCtx(), "SessionExpired") ?? "Session Expired");
            }

            Ctx ctx = Session["ctx"] as Ctx;
            int clientId = ctx.GetAD_Client_ID();

            try
            {
                // Vendors
                List<object> vendors = new List<object>();
                string vendorSql = @"
                    SELECT C_BPartner_ID AS id, Name AS name, Value AS code
                    FROM C_BPartner
                    WHERE IsActive='Y' AND IsVendor='Y' AND AD_Client_ID=@AD_Client_ID
                    ORDER BY Name ASC";
                using (IDataReader dr = DB.ExecuteReader(vendorSql, new SqlParameter[] { new SqlParameter("@AD_Client_ID", clientId) }))
                {
                    while (dr != null && dr.Read())
                    {
                        vendors.Add(new
                        {
                            id = Util.GetValueOfInt(dr["id"]),
                            name = Util.GetValueOfString(dr["name"]),
                            code = Util.GetValueOfString(dr["code"])
                        });
                    }
                }

                // Warehouses
                List<object> warehouses = new List<object>();
                string whSql = @"
                    SELECT M_Warehouse_ID AS id, Name AS name, Value AS code
                    FROM M_Warehouse
                    WHERE IsActive='Y' AND AD_Client_ID=@AD_Client_ID
                    ORDER BY Name ASC";
                using (IDataReader dr = DB.ExecuteReader(whSql, new SqlParameter[] { new SqlParameter("@AD_Client_ID", clientId) }))
                {
                    while (dr != null && dr.Read())
                    {
                        warehouses.Add(new
                        {
                            id = Util.GetValueOfInt(dr["id"]),
                            name = Util.GetValueOfString(dr["name"]),
                            code = Util.GetValueOfString(dr["code"])
                        });
                    }
                }

                // Payment Terms
                List<object> paymentTerms = new List<object>();
                string termSql = @"
                    SELECT C_PaymentTerm_ID AS id, Name AS name
                    FROM C_PaymentTerm
                    WHERE IsActive='Y' AND AD_Client_ID IN (0, @AD_Client_ID)
                    ORDER BY Name ASC";
                using (IDataReader dr = DB.ExecuteReader(termSql, new SqlParameter[] { new SqlParameter("@AD_Client_ID", clientId) }))
                {
                    while (dr != null && dr.Read())
                    {
                        paymentTerms.Add(new
                        {
                            id = Util.GetValueOfInt(dr["id"]),
                            name = Util.GetValueOfString(dr["name"])
                        });
                    }
                }

                // DocTypes (Purchase Order DocBaseType = 'POO')
                List<object> docTypes = new List<object>();
                string docTypeSql = @"
                    SELECT C_DocType_ID AS id, Name AS name
                    FROM C_DocType
                    WHERE IsActive='Y' AND DocBaseType='POO' AND AD_Client_ID IN (0, @AD_Client_ID)
                    ORDER BY Name ASC";
                using (IDataReader dr = DB.ExecuteReader(docTypeSql, new SqlParameter[] { new SqlParameter("@AD_Client_ID", clientId) }))
                {
                    while (dr != null && dr.Read())
                    {
                        docTypes.Add(new
                        {
                            id = Util.GetValueOfInt(dr["id"]),
                            name = Util.GetValueOfString(dr["name"])
                        });
                    }
                }

                // Taxes
                List<object> taxes = new List<object>();
                string taxSql = @"
                    SELECT C_Tax_ID AS id, Name AS name, Rate AS rate
                    FROM C_Tax
                    WHERE IsActive='Y' AND AD_Client_ID IN (0, @AD_Client_ID)
                    ORDER BY Name ASC";
                using (IDataReader dr = DB.ExecuteReader(taxSql, new SqlParameter[] { new SqlParameter("@AD_Client_ID", clientId) }))
                {
                    while (dr != null && dr.Read())
                    {
                        taxes.Add(new
                        {
                            id = Util.GetValueOfInt(dr["id"]),
                            name = Util.GetValueOfString(dr["name"]),
                            rate = Util.GetValueOfDecimal(dr["rate"])
                        });
                    }
                }

                // Currencies
                List<object> currencies = new List<object>();
                string curSql = @"
                    SELECT C_Currency_ID AS id, ISO_Code AS code, CurSymbol AS symbol
                    FROM C_Currency
                    WHERE IsActive='Y'
                    ORDER BY ISO_Code ASC";
                using (IDataReader dr = DB.ExecuteReader(curSql, null))
                {
                    while (dr != null && dr.Read())
                    {
                        currencies.Add(new
                        {
                            id = Util.GetValueOfInt(dr["id"]),
                            code = Util.GetValueOfString(dr["code"]),
                            symbol = Util.GetValueOfString(dr["symbol"])
                        });
                    }
                }

                return Ok(new
                {
                    vendors = vendors,
                    warehouses = warehouses,
                    paymentTerms = paymentTerms,
                    docTypes = docTypes,
                    taxes = taxes,
                    currencies = currencies
                });
            }
            catch (Exception ex)
            {
                Log.Log(Level.SEVERE, "VAS_204_NewPurchaseOrderWidget.GetFormLookups", ex);
                return Fail(ex.Message);
            }
        }

        /// <summary>
        /// Creates a new Purchase Order from selected requisition lines inside a single database transaction.
        /// </summary>
        /// <param name="requisitionId">Source M_Requisition_ID</param>
        /// <param name="vendorId">Vendor C_BPartner_ID</param>
        /// <param name="warehouseId">Target M_Warehouse_ID</param>
        /// <param name="docTypeId">C_DocType_ID (optional)</param>
        /// <param name="paymentTermId">C_PaymentTerm_ID (optional)</param>
        /// <param name="dateOrdered">PO date string</param>
        /// <param name="datePromised">Promised date string</param>
        /// <param name="description">PO description</param>
        /// <param name="linesJson">JSON array of selected line inputs</param>
        /// <returns>JSON { success, orderId, documentNo, message }</returns>
        [HttpPost]
        [AjaxAuthorizeAttribute]
        [AjaxSessionFilterAttribute]
        public JsonResult CreatePurchaseOrder(
            int requisitionId = 0,
            int vendorId = 0,
            int warehouseId = 0,
            int docTypeId = 0,
            int paymentTermId = 0,
            string dateOrdered = null,
            string datePromised = null,
            string description = null,
            string linesJson = null)
        {
            if (Session["ctx"] == null)
            {
                return Fail(Msg.GetMsg(Env.GetCtx(), "SessionExpired") ?? "Session Expired");
            }

            Ctx ctx = Session["ctx"] as Ctx;

            if (vendorId <= 0)
            {
                return Fail(Msg.GetMsg(ctx, "VAS_VendorRequired") ?? "Vendor is required.");
            }

            List<POLineInput> lineInputs = string.IsNullOrWhiteSpace(linesJson)
                ? new List<POLineInput>()
                : JsonConvert.DeserializeObject<List<POLineInput>>(linesJson);

            if (lineInputs == null || lineInputs.Count == 0)
            {
                return Fail(Msg.GetMsg(ctx, "VAS_SelectLinesToOrder") ?? "Please select at least one line to order.");
            }

            Trx trx = null;
            try
            {
                trx = Trx.Get("VAS_NewPO_" + DateTime.Now.Ticks);

                // Fetch requisition organization if source requisition provided
                int reqOrgId = ctx.GetAD_Org_ID();
                if (requisitionId > 0)
                {
                    string orgSql = "SELECT AD_Org_ID FROM M_Requisition WHERE M_Requisition_ID = @M_Requisition_ID";
                    reqOrgId = Util.GetValueOfInt(DB.ExecuteScalar(orgSql, new SqlParameter[] { new SqlParameter("@M_Requisition_ID", requisitionId) }, trx));
                    if (reqOrgId <= 0) { reqOrgId = ctx.GetAD_Org_ID(); }
                }

                // Resolve doc type if not provided
                if (docTypeId <= 0)
                {
                    string dtSql = @"
                        SELECT C_DocType_ID
                        FROM C_DocType
                        WHERE DocBaseType='POO' AND IsActive='Y' AND AD_Client_ID IN (0, @AD_Client_ID)
                        ORDER BY AD_Org_ID DESC, C_DocType_ID ASC";
                    docTypeId = Util.GetValueOfInt(DB.ExecuteScalar(dtSql, new SqlParameter[] { new SqlParameter("@AD_Client_ID", ctx.GetAD_Client_ID()) }, trx));
                }

                // Resolve default warehouse if not provided
                if (warehouseId <= 0)
                {
                    string whSql = "SELECT M_Warehouse_ID FROM M_Warehouse WHERE IsActive='Y' AND AD_Client_ID=@AD_Client_ID ORDER BY M_Warehouse_ID ASC";
                    warehouseId = Util.GetValueOfInt(DB.ExecuteScalar(whSql, new SqlParameter[] { new SqlParameter("@AD_Client_ID", ctx.GetAD_Client_ID()) }, trx));
                }

                DateTime orderDate = DateTime.Now;
                if (!string.IsNullOrEmpty(dateOrdered))
                {
                    DateTime parsed;
                    if (DateTime.TryParse(dateOrdered, CultureInfo.InvariantCulture, DateTimeStyles.None, out parsed))
                    {
                        orderDate = parsed;
                    }
                }

                DateTime promiseDate = orderDate.AddDays(7);
                if (!string.IsNullOrEmpty(datePromised))
                {
                    DateTime parsed;
                    if (DateTime.TryParse(datePromised, CultureInfo.InvariantCulture, DateTimeStyles.None, out parsed))
                    {
                        promiseDate = parsed;
                    }
                }

                // Create Order Header
                MOrder order = new MOrder(ctx, 0, trx);
                order.SetAD_Client_ID(ctx.GetAD_Client_ID());
                order.SetAD_Org_ID(reqOrgId);
                order.SetIsSOTrx(false);
                order.SetIsReturnTrx(false);
                order.SetC_BPartner_ID(vendorId);

                if (warehouseId > 0)
                {
                    order.SetM_Warehouse_ID(warehouseId);
                }
                if (docTypeId > 0)
                {
                    order.SetC_DocType_ID(docTypeId);
                    order.SetC_DocTypeTarget_ID(docTypeId);
                }
                if (paymentTermId > 0)
                {
                    order.SetC_PaymentTerm_ID(paymentTermId);
                }

                order.SetDateOrdered(orderDate);
                order.SetDatePromised(promiseDate);
                if (!string.IsNullOrEmpty(description))
                {
                    order.SetDescription(description);
                }

                order.SetDocStatus(MOrder.DOCSTATUS_Drafted);
                order.SetDocAction(MOrder.DOCACTION_Complete);

                if (!order.Save(trx))
                {
                    trx.Rollback();
                    string error = GetSaveError(ctx, "VAS_PONotSaved", "Purchase order could not be saved.");
                    return Fail(error);
                }

                // Create Order Lines
                int lineSeq = 10;
                foreach (POLineInput item in lineInputs)
                {
                    if (item == null || item.Qty <= 0) { continue; }

                    MOrderLine ol = new MOrderLine(order);
                    ol.SetLine(lineSeq);
                    lineSeq += 10;

                    if (item.ProductId > 0)
                    {
                        ol.SetM_Product_ID(item.ProductId);
                    }
                    ol.SetQty(item.Qty);
                    ol.SetQtyOrdered(item.Qty);
                    ol.SetQtyEntered(item.Qty);

                    if (item.Rate > 0)
                    {
                        ol.SetPrice(item.Rate);
                        ol.SetPriceActual(item.Rate);
                        ol.SetPriceEntered(item.Rate);
                        ol.SetPriceList(item.Rate);
                        ol.SetLineNetAmt(item.Qty * item.Rate);
                    }

                    if (item.TaxId > 0)
                    {
                        ol.SetC_Tax_ID(item.TaxId);
                    }

                    if (!string.IsNullOrEmpty(item.Description))
                    {
                        ol.SetDescription(item.Description);
                    }

                    // Link back to requisition line
                    if (item.RequisitionLineId > 0 && ol.Get_ColumnIndex("M_RequisitionLine_ID") >= 0)
                    {
                        ol.Set_Value("M_RequisitionLine_ID", item.RequisitionLineId);
                    }

                    if (!ol.Save(trx))
                    {
                        trx.Rollback();
                        string error = GetSaveError(ctx, "VAS_POLineNotSaved", "Purchase order line could not be saved.");
                        return Fail(error);
                    }

                    // Update QtyOrdered on M_RequisitionLine if present
                    if (item.RequisitionLineId > 0)
                    {
                        string updateReqLineSql = @"
                            UPDATE M_RequisitionLine
                            SET QtyOrdered = COALESCE(QtyOrdered, 0) + @OrderQty
                            WHERE M_RequisitionLine_ID = @M_RequisitionLine_ID";
                        DB.ExecuteQuery(updateReqLineSql, new SqlParameter[]
                        {
                            new SqlParameter("@OrderQty", item.Qty),
                            new SqlParameter("@M_RequisitionLine_ID", item.RequisitionLineId)
                        }, trx);
                    }
                }

                trx.Commit();

                return Ok(new
                {
                    success = true,
                    orderId = order.GetC_Order_ID(),
                    documentNo = order.GetDocumentNo(),
                    message = Msg.GetMsg(ctx, "VAS_POSaved") ?? "Purchase Order created successfully."
                });
            }
            catch (Exception ex)
            {
                if (trx != null)
                {
                    trx.Rollback();
                }
                Log.Log(Level.SEVERE, "VAS_204_NewPurchaseOrderWidget.CreatePurchaseOrder", ex);
                return Fail(ex.Message);
            }
            finally
            {
                if (trx != null)
                {
                    trx.Close();
                }
            }
        }

        private int GetWindowIdByName(Ctx ctx, string windowName)
        {
            string sql = @"
                SELECT ADWindow.AD_Window_ID
                FROM AD_Window ADWindow
                WHERE ADWindow.IsActive='Y'
                  AND ADWindow.Name=@Window_Name
                  AND ADWindow.AD_Client_ID IN (0, @AD_Client_ID)";

            sql = MRole.GetDefault(ctx).AddAccessSQL(
                sql,
                "ADWindow",
                MRole.SQL_FULLYQUALIFIED,
                MRole.SQL_RO
            );
            sql += " ORDER BY ADWindow.AD_Window_ID DESC";

            return Util.GetValueOfInt(DB.ExecuteScalar(sql, new SqlParameter[]
            {
                new SqlParameter("@Window_Name", windowName),
                new SqlParameter("@AD_Client_ID", ctx.GetAD_Client_ID())
            }, null));
        }

        private string GetSaveError(Ctx ctx, string fallbackKey, string fallback)
        {
            ValueNamePair pp = VLogger.RetrieveError();
            string error = pp != null ? pp.GetName() : "";

            if (string.IsNullOrEmpty(error))
            {
                error = pp != null ? Msg.GetMsg(ctx, pp.GetValue()) : "";
            }
            if (string.IsNullOrEmpty(error))
            {
                error = Msg.GetMsg(ctx, fallbackKey);
            }
            return string.IsNullOrEmpty(error) ? fallback : error;
        }

        private JsonResult Ok(object result)
        {
            return Json(JsonConvert.SerializeObject(result), JsonRequestBehavior.AllowGet);
        }

        private JsonResult Fail(string message)
        {
            return Json(JsonConvert.SerializeObject(new
            {
                success = false,
                error = message,
                message = message
            }), JsonRequestBehavior.AllowGet);
        }

        private sealed class POLineInput
        {
            [JsonProperty("requisitionLineId")]
            public int RequisitionLineId { get; set; }

            [JsonProperty("productId")]
            public int ProductId { get; set; }

            [JsonProperty("qty")]
            public decimal Qty { get; set; }

            [JsonProperty("rate")]
            public decimal Rate { get; set; }

            [JsonProperty("taxId")]
            public int TaxId { get; set; }

            [JsonProperty("description")]
            public string Description { get; set; }
        }
    }
}
