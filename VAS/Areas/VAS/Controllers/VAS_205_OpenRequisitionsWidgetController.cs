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
    /// Module Name : Open Requisitions Widget (Purchase Order Dashboard)
    /// Widget ID   : VAS_205_OpenRequisitionsWidget
    /// Purpose     : 3x3 Dashboard Widget that displays approved, completed requisitions 
    ///               (M_Requisition, DocStatus='CO') with remaining pending quantities (Qty > QtyOrdered).
    ///               Drives the 3-step conversion flow:
    ///                 Step 1: Select one requisition from the grid.
    ///                 Step 2: Select pending lines from that requisition and enforce a single vendor.
    ///                 Step 3: Collect PO header details (Page 1) and line parameters (Page 2),
    ///                         then transactionally create the Purchase Order (C_Order, C_OrderLine)
    ///                         linked back to source M_RequisitionLine records.
    /// Chronological development:
    ///   Builder Agent 15   2026-08-17 Created
    /// </summary>
    public class VAS_205_OpenRequisitionsWidgetController : Controller
    {
        private static readonly VLogger Log = VLogger.GetVLogger(typeof(VAS_205_OpenRequisitionsWidgetController).FullName);

        /// <summary>
        /// Resolves the Purchase Order window ID (VAS_PurchaseOrder or Purchase Order) for record navigation.
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
                Log.Log(Level.SEVERE, "VAS_205_OpenRequisitionsWidget.GetPurchaseOrderWindowId", ex);
                return Fail(ex.Message);
            }
        }

        /// <summary>
        /// Reads all completed purchase requisitions (DocStatus='CO') that have remaining pending quantity.
        /// ANSI-compliant portable SQL query mapping to M_Requisition, M_RequisitionLine and M_PriceList.
        /// </summary>
        /// <returns>JSON { rows[], summary: { totalRequisitions, readyToPO, partlyOrdered, totalPendingQty, totalPendingLines } }</returns>
        [AjaxAuthorizeAttribute]
        [AjaxSessionFilterAttribute]
        public JsonResult GetOpenRequisitions()
        {
            if (Session["ctx"] == null)
            {
                return Fail(Msg.GetMsg(Env.GetCtx(), "SessionExpired") ?? "Session Expired");
            }

            Ctx ctx = Session["ctx"] as Ctx;
            int clientId = ctx.GetAD_Client_ID();

            string rawSql = @"
                SELECT
                    r.M_Requisition_ID AS requisition_id,
                    r.DocumentNo AS requisition_number,
                    r.DateRequired AS needed_by,
                    r.DocStatus AS requisition_doc_status,
                    r.AD_Org_ID AS organization_id,
                    r.M_Warehouse_ID AS warehouse_id,
                    r.M_PriceList_ID AS price_list_id,
                    r.C_BPartner_ID AS header_vendor_id,
                    r.C_IncoTerm_ID AS incoterm_id,
                    pl.C_Currency_ID AS currency_id,
                    COUNT(rl.M_RequisitionLine_ID) AS line_count,
                    SUM(COALESCE(rl.Qty, 0)) AS requisition_qty,
                    SUM(COALESCE(rl.QtyOrdered, 0)) AS already_ordered_qty,
                    SUM(
                        CASE
                            WHEN COALESCE(rl.Qty, 0) > COALESCE(rl.QtyOrdered, 0)
                            THEN COALESCE(rl.Qty, 0) - COALESCE(rl.QtyOrdered, 0)
                            ELSE 0
                        END
                    ) AS pending_qty,
                    SUM(
                        CASE
                            WHEN COALESCE(rl.Qty, 0) > COALESCE(rl.QtyOrdered, 0)
                            THEN 1 ELSE 0
                        END
                    ) AS pending_line_count
                FROM M_Requisition r
                INNER JOIN M_RequisitionLine rl
                    ON rl.M_Requisition_ID = r.M_Requisition_ID
                   AND rl.IsActive = 'Y'
                LEFT JOIN M_PriceList pl
                    ON pl.M_PriceList_ID = r.M_PriceList_ID
                WHERE r.AD_Client_ID = @AD_Client_ID
                  AND r.IsActive = 'Y'
                  AND r.DocStatus = 'CO'
                GROUP BY
                    r.M_Requisition_ID,
                    r.DocumentNo,
                    r.DateRequired,
                    r.DocStatus,
                    r.AD_Org_ID,
                    r.M_Warehouse_ID,
                    r.M_PriceList_ID,
                    r.C_BPartner_ID,
                    r.C_IncoTerm_ID,
                    pl.C_Currency_ID
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
                new SqlParameter("@AD_Client_ID", clientId)
            };

            List<object> rows = new List<object>();
            int totalRequisitions = 0;
            int readyToPOCount = 0;
            int partlyOrderedCount = 0;
            decimal totalPendingQty = 0;
            int totalPendingLines = 0;

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
                    int pendingLineCount = Util.GetValueOfInt(dr["pending_line_count"]);
                    int orgId = Util.GetValueOfInt(dr["organization_id"]);
                    int warehouseId = Util.GetValueOfInt(dr["warehouse_id"]);
                    int priceListId = Util.GetValueOfInt(dr["price_list_id"]);
                    int headerVendorId = Util.GetValueOfInt(dr["header_vendor_id"]);
                    int incotermId = Util.GetValueOfInt(dr["incoterm_id"]);
                    int currencyId = Util.GetValueOfInt(dr["currency_id"]);

                    string derivedStatus = alreadyOrderedQty > 0 ? "Partly ordered" : "Ready to PO";
                    string statusChip = alreadyOrderedQty > 0 ? "chip-warn" : "chip-ok";

                    totalRequisitions++;
                    if (alreadyOrderedQty > 0)
                    {
                        partlyOrderedCount++;
                    }
                    else
                    {
                        readyToPOCount++;
                    }
                    totalPendingQty += pendingQty;
                    totalPendingLines += pendingLineCount;

                    rows.Add(new
                    {
                        requisitionId = reqId,
                        requisitionNumber = reqNo,
                        neededBy = needDate.HasValue ? needDate.Value.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture) : "",
                        neededByShort = needDate.HasValue ? needDate.Value.ToString("dd MMM", CultureInfo.InvariantCulture) : "",
                        neededByDisplay = needDate.HasValue ? needDate.Value.ToString("dd MMM yyyy", CultureInfo.InvariantCulture) : "",
                        lineCount = lineCount,
                        pendingLineCount = pendingLineCount,
                        requisitionQty = reqQty,
                        alreadyOrderedQty = alreadyOrderedQty,
                        pendingQty = pendingQty,
                        status = derivedStatus,
                        statusChip = statusChip,
                        organizationId = orgId,
                        warehouseId = warehouseId,
                        priceListId = priceListId,
                        headerVendorId = headerVendorId,
                        incotermId = incotermId,
                        currencyId = currencyId
                    });
                }

                return Ok(new
                {
                    rows = rows,
                    summary = new
                    {
                        totalRequisitions = totalRequisitions,
                        readyToPO = readyToPOCount,
                        partlyOrdered = partlyOrderedCount,
                        totalPendingQty = totalPendingQty,
                        totalPendingLines = totalPendingLines
                    }
                });
            }
            catch (Exception ex)
            {
                Log.Log(Level.SEVERE, "VAS_205_OpenRequisitionsWidget.GetOpenRequisitions", ex);
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
        /// Returns active pending lines of a specific requisition (M_RequisitionLine),
        /// alongside preferred vendor lookups from M_Product_PO.
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
                    rl.M_RequisitionLine_ID AS requisition_line_id,
                    rl.M_Requisition_ID AS requisition_id,
                    rl.Line AS line_no,
                    rl.M_Product_ID AS product_id,
                    COALESCE(p.Name, rl.Description, N'') AS product_name,
                    COALESCE(p.Value, N'') AS product_code,
                    rl.M_AttributeSetInstance_ID AS attribute_set_instance_id,
                    COALESCE(asi.Description, N'Standard specification') AS attribute_description,
                    COALESCE(rl.C_UOM_ID, p.C_UOM_ID, 0) AS uom_id,
                    COALESCE(u.UOMSymbol, u.Name, N'') AS uom_name,
                    COALESCE(rl.Qty, 0) AS requested_qty,
                    COALESCE(rl.QtyOrdered, 0) AS already_ordered_qty,
                    CASE
                        WHEN COALESCE(rl.Qty, 0) > COALESCE(rl.QtyOrdered, 0)
                        THEN COALESCE(rl.Qty, 0) - COALESCE(rl.QtyOrdered, 0)
                        ELSE 0
                    END AS pending_qty,
                    COALESCE(rl.PriceActual, 0) AS requisition_rate,
                    COALESCE(rl.Description, N'') AS description,
                    COALESCE(rl.PrintDescription, N'') AS print_description,
                    COALESCE(rl.C_BPartner_ID, r.C_BPartner_ID, 0) AS line_vendor_id,
                    COALESCE(bp.Name, N'') AS line_vendor_name
                FROM M_RequisitionLine rl
                INNER JOIN M_Requisition r
                    ON r.M_Requisition_ID = rl.M_Requisition_ID
                INNER JOIN M_Product p
                    ON p.M_Product_ID = rl.M_Product_ID
                LEFT JOIN M_AttributeSetInstance asi
                    ON asi.M_AttributeSetInstance_ID = rl.M_AttributeSetInstance_ID
                LEFT JOIN C_UOM u
                    ON u.C_UOM_ID = COALESCE(rl.C_UOM_ID, p.C_UOM_ID)
                LEFT JOIN C_BPartner bp
                    ON bp.C_BPartner_ID = COALESCE(rl.C_BPartner_ID, r.C_BPartner_ID)
                WHERE rl.M_Requisition_ID = @M_Requisition_ID
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
                new SqlParameter("@M_Requisition_ID", requisitionId)
            };

            List<RequisitionLineDto> lineList = new List<RequisitionLineDto>();
            List<int> productIds = new List<int>();

            IDataReader dr = null;
            try
            {
                dr = DB.ExecuteReader(sql, parameters.ToArray());
                while (dr != null && dr.Read())
                {
                    int pId = Util.GetValueOfInt(dr["product_id"]);
                    if (pId > 0 && !productIds.Contains(pId))
                    {
                        productIds.Add(pId);
                    }

                    lineList.Add(new RequisitionLineDto
                    {
                        requisitionLineId = Util.GetValueOfInt(dr["requisition_line_id"]),
                        requisitionId = Util.GetValueOfInt(dr["requisition_id"]),
                        lineNo = Util.GetValueOfInt(dr["line_no"]),
                        productId = pId,
                        productName = Util.GetValueOfString(dr["product_name"]),
                        productCode = Util.GetValueOfString(dr["product_code"]),
                        attributeSetInstanceId = Util.GetValueOfInt(dr["attribute_set_instance_id"]),
                        attributeDescription = Util.GetValueOfString(dr["attribute_description"]),
                        uomId = Util.GetValueOfInt(dr["uom_id"]),
                        uomName = Util.GetValueOfString(dr["uom_name"]),
                        requestedQty = Util.GetValueOfDecimal(dr["requested_qty"]),
                        alreadyOrderedQty = Util.GetValueOfDecimal(dr["already_ordered_qty"]),
                        pendingQty = Util.GetValueOfDecimal(dr["pending_qty"]),
                        requisitionRate = Util.GetValueOfDecimal(dr["requisition_rate"]),
                        description = Util.GetValueOfString(dr["description"]),
                        printDescription = Util.GetValueOfString(dr["print_description"]),
                        lineVendorId = Util.GetValueOfInt(dr["line_vendor_id"]),
                        lineVendorName = Util.GetValueOfString(dr["line_vendor_name"])
                    });
                }
            }
            catch (Exception ex)
            {
                Log.Log(Level.SEVERE, "VAS_205_OpenRequisitionsWidget.GetRequisitionLines", ex);
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

            // Lookup preferred vendors from M_Product_PO for all product IDs
            Dictionary<int, ProductPoVendorDto> preferredVendors = GetProductPreferredVendors(ctx, productIds);

            List<object> lines = new List<object>();
            foreach (RequisitionLineDto l in lineList)
            {
                int effectiveVendorId = l.lineVendorId;
                string effectiveVendorName = l.lineVendorName;
                decimal effectiveRate = l.requisitionRate;

                if (preferredVendors.ContainsKey(l.productId))
                {
                    ProductPoVendorDto poVendor = preferredVendors[l.productId];
                    if (effectiveVendorId <= 0)
                    {
                        effectiveVendorId = poVendor.vendorId;
                        effectiveVendorName = poVendor.vendorName;
                    }
                    if (effectiveRate <= 0 && poVendor.vendorPrice > 0)
                    {
                        effectiveRate = poVendor.vendorPrice;
                    }
                }

                lines.Add(new
                {
                    requisitionLineId = l.requisitionLineId,
                    requisitionId = l.requisitionId,
                    lineNo = l.lineNo,
                    productId = l.productId,
                    productName = l.productName,
                    productCode = l.productCode,
                    attributeSetInstanceId = l.attributeSetInstanceId,
                    attributeDescription = l.attributeDescription,
                    uomId = l.uomId,
                    uomName = l.uomName,
                    requestedQty = l.requestedQty,
                    alreadyOrderedQty = l.alreadyOrderedQty,
                    pendingQty = l.pendingQty,
                    orderQty = l.pendingQty,
                    requisitionRate = l.requisitionRate,
                    rate = effectiveRate,
                    amount = l.pendingQty * effectiveRate,
                    description = l.description,
                    printDescription = l.printDescription,
                    vendorId = effectiveVendorId,
                    vendorName = effectiveVendorName
                });
            }

            return Ok(new { lines = lines });
        }

        /// <summary>
        /// Returns lookup references required for the PO creation form (vendors, warehouses, payment terms, doc types, taxes, currencies, incoterms, etc.).
        /// </summary>
        /// <param name="requisitionId">Selected M_Requisition_ID</param>
        /// <param name="vendorId">Selected C_BPartner_ID</param>
        /// <returns>JSON lookups object</returns>
        [AjaxAuthorizeAttribute]
        [AjaxSessionFilterAttribute]
        public JsonResult GetFormLookups(int requisitionId = 0, int vendorId = 0)
        {
            if (Session["ctx"] == null)
            {
                return Fail(Msg.GetMsg(Env.GetCtx(), "SessionExpired") ?? "Session Expired");
            }

            Ctx ctx = Session["ctx"] as Ctx;
            int clientId = ctx.GetAD_Client_ID();

            try
            {
                // 1. Vendors (active purchase vendors)
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

                // 2. Vendor Locations (addresses for selected vendor)
                List<object> vendorLocations = new List<object>();
                if (vendorId > 0)
                {
                    string locSql = @"
                        SELECT C_BPartner_Location_ID AS id, Name AS name
                        FROM C_BPartner_Location
                        WHERE IsActive='Y' AND C_BPartner_ID=@C_BPartner_ID
                        ORDER BY Name ASC";
                    using (IDataReader dr = DB.ExecuteReader(locSql, new SqlParameter[] { new SqlParameter("@C_BPartner_ID", vendorId) }))
                    {
                        while (dr != null && dr.Read())
                        {
                            vendorLocations.Add(new
                            {
                                id = Util.GetValueOfInt(dr["id"]),
                                name = Util.GetValueOfString(dr["name"])
                            });
                        }
                    }
                }

                // 3. Vendor Contacts (AD_User for selected vendor)
                List<object> vendorContacts = new List<object>();
                if (vendorId > 0)
                {
                    string conSql = @"
                        SELECT AD_User_ID AS id, Name AS name, Phone AS phone
                        FROM AD_User
                        WHERE IsActive='Y' AND C_BPartner_ID=@C_BPartner_ID
                        ORDER BY Name ASC";
                    using (IDataReader dr = DB.ExecuteReader(conSql, new SqlParameter[] { new SqlParameter("@C_BPartner_ID", vendorId) }))
                    {
                        while (dr != null && dr.Read())
                        {
                            vendorContacts.Add(new
                            {
                                id = Util.GetValueOfInt(dr["id"]),
                                name = Util.GetValueOfString(dr["name"]),
                                phone = Util.GetValueOfString(dr["phone"])
                            });
                        }
                    }
                }

                // 4. Warehouses
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

                // 5. Payment Terms
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

                // 6. Target Document Types (PO DocBaseType = 'POO')
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

                // 7. Taxes
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

                // 8. Price Lists (Purchase Price Lists, IsSOPriceList = 'N')
                List<object> priceLists = new List<object>();
                string plSql = @"
                    SELECT M_PriceList_ID AS id, Name AS name, C_Currency_ID AS currency_id
                    FROM M_PriceList
                    WHERE IsActive='Y' AND IsSOPriceList='N' AND AD_Client_ID=@AD_Client_ID
                    ORDER BY Name ASC";
                using (IDataReader dr = DB.ExecuteReader(plSql, new SqlParameter[] { new SqlParameter("@AD_Client_ID", clientId) }))
                {
                    while (dr != null && dr.Read())
                    {
                        priceLists.Add(new
                        {
                            id = Util.GetValueOfInt(dr["id"]),
                            name = Util.GetValueOfString(dr["name"]),
                            currencyId = Util.GetValueOfInt(dr["currency_id"])
                        });
                    }
                }

                // 9. Currencies
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

                // 10. Currency Rate Types (Conversion Types)
                List<object> conversionTypes = new List<object>();
                string convSql = @"
                    SELECT C_ConversionType_ID AS id, Name AS name
                    FROM C_ConversionType
                    WHERE IsActive='Y' AND AD_Client_ID IN (0, @AD_Client_ID)
                    ORDER BY Name ASC";
                using (IDataReader dr = DB.ExecuteReader(convSql, new SqlParameter[] { new SqlParameter("@AD_Client_ID", clientId) }))
                {
                    while (dr != null && dr.Read())
                    {
                        conversionTypes.Add(new
                        {
                            id = Util.GetValueOfInt(dr["id"]),
                            name = Util.GetValueOfString(dr["name"])
                        });
                    }
                }

                // 11. Incoterms
                List<object> incoterms = new List<object>();
                string incoSql = @"
                    SELECT C_IncoTerm_ID AS id, Name AS name
                    FROM C_IncoTerm
                    WHERE IsActive='Y' AND AD_Client_ID IN (0, @AD_Client_ID)
                    ORDER BY Name ASC";
                using (IDataReader dr = DB.ExecuteReader(incoSql, new SqlParameter[] { new SqlParameter("@AD_Client_ID", clientId) }))
                {
                    while (dr != null && dr.Read())
                    {
                        incoterms.Add(new
                        {
                            id = Util.GetValueOfInt(dr["id"]),
                            name = Util.GetValueOfString(dr["name"])
                        });
                    }
                }

                // 12. Payment Methods (Reference List or default options)
                List<object> paymentMethods = new List<object>();
                string payMethSql = @"
                    SELECT Value AS id, Name AS name
                    FROM AD_Ref_List
                    WHERE AD_Reference_ID = 195 AND IsActive='Y'
                    ORDER BY Name ASC";
                using (IDataReader dr = DB.ExecuteReader(payMethSql, null))
                {
                    while (dr != null && dr.Read())
                    {
                        paymentMethods.Add(new
                        {
                            id = Util.GetValueOfString(dr["id"]),
                            name = Util.GetValueOfString(dr["name"])
                        });
                    }
                }
                if (paymentMethods.Count == 0)
                {
                    paymentMethods.Add(new { id = "P", name = "Payment Rule" });
                    paymentMethods.Add(new { id = "T", name = "Direct Deposit / Wire" });
                    paymentMethods.Add(new { id = "K", name = "Credit Card" });
                    paymentMethods.Add(new { id = "S", name = "Check" });
                }

                return Ok(new
                {
                    vendors = vendors,
                    vendorLocations = vendorLocations,
                    vendorContacts = vendorContacts,
                    warehouses = warehouses,
                    paymentTerms = paymentTerms,
                    docTypes = docTypes,
                    taxes = taxes,
                    priceLists = priceLists,
                    currencies = currencies,
                    conversionTypes = conversionTypes,
                    incoterms = incoterms,
                    paymentMethods = paymentMethods
                });
            }
            catch (Exception ex)
            {
                Log.Log(Level.SEVERE, "VAS_205_OpenRequisitionsWidget.GetFormLookups", ex);
                return Fail(ex.Message);
            }
        }

        /// <summary>
        /// Retrieves vendor locations and contacts for a chosen vendor (used when vendor is toggled/changed).
        /// </summary>
        /// <param name="vendorId">C_BPartner_ID</param>
        /// <returns>JSON { locations[], contacts[] }</returns>
        [AjaxAuthorizeAttribute]
        [AjaxSessionFilterAttribute]
        public JsonResult GetVendorLocationsAndContacts(int vendorId = 0)
        {
            if (Session["ctx"] == null)
            {
                return Fail(Msg.GetMsg(Env.GetCtx(), "SessionExpired") ?? "Session Expired");
            }

            if (vendorId <= 0)
            {
                return Ok(new { locations = new List<object>(), contacts = new List<object>() });
            }

            try
            {
                List<object> locations = new List<object>();
                string locSql = @"
                    SELECT C_BPartner_Location_ID AS id, Name AS name
                    FROM C_BPartner_Location
                    WHERE IsActive='Y' AND C_BPartner_ID=@C_BPartner_ID
                    ORDER BY Name ASC";
                using (IDataReader dr = DB.ExecuteReader(locSql, new SqlParameter[] { new SqlParameter("@C_BPartner_ID", vendorId) }))
                {
                    while (dr != null && dr.Read())
                    {
                        locations.Add(new
                        {
                            id = Util.GetValueOfInt(dr["id"]),
                            name = Util.GetValueOfString(dr["name"])
                        });
                    }
                }

                List<object> contacts = new List<object>();
                string conSql = @"
                    SELECT AD_User_ID AS id, Name AS name, Phone AS phone
                    FROM AD_User
                    WHERE IsActive='Y' AND C_BPartner_ID=@C_BPartner_ID
                    ORDER BY Name ASC";
                using (IDataReader dr = DB.ExecuteReader(conSql, new SqlParameter[] { new SqlParameter("@C_BPartner_ID", vendorId) }))
                {
                    while (dr != null && dr.Read())
                    {
                        contacts.Add(new
                        {
                            id = Util.GetValueOfInt(dr["id"]),
                            name = Util.GetValueOfString(dr["name"]),
                            phone = Util.GetValueOfString(dr["phone"])
                        });
                    }
                }

                return Ok(new { locations = locations, contacts = contacts });
            }
            catch (Exception ex)
            {
                Log.Log(Level.SEVERE, "VAS_205_OpenRequisitionsWidget.GetVendorLocationsAndContacts", ex);
                return Fail(ex.Message);
            }
        }

        /// <summary>
        /// Retrieves alternate UOM conversion options for a given product.
        /// </summary>
        /// <param name="productId">M_Product_ID</param>
        /// <param name="baseUomId">C_UOM_ID</param>
        /// <returns>JSON { uoms[] }</returns>
        [AjaxAuthorizeAttribute]
        [AjaxSessionFilterAttribute]
        public JsonResult GetProductAlternateUOMs(int productId = 0, int baseUomId = 0)
        {
            if (Session["ctx"] == null)
            {
                return Fail(Msg.GetMsg(Env.GetCtx(), "SessionExpired") ?? "Session Expired");
            }

            try
            {
                List<object> uoms = new List<object>();
                if (productId > 0)
                {
                    string uomSql = @"
                        SELECT u.C_UOM_ID AS id, COALESCE(u.UOMSymbol, u.Name) AS name
                        FROM C_UOM_Conversion c
                        INNER JOIN C_UOM u ON u.C_UOM_ID = c.C_UOM_To_ID AND u.IsActive = 'Y'
                        WHERE c.IsActive = 'Y' AND c.M_Product_ID = @M_Product_ID
                        UNION
                        SELECT u.C_UOM_ID AS id, COALESCE(u.UOMSymbol, u.Name) AS name
                        FROM C_UOM u
                        WHERE u.C_UOM_ID = @Base_UOM_ID AND u.IsActive = 'Y'";

                    using (IDataReader dr = DB.ExecuteReader(uomSql, new SqlParameter[]
                    {
                        new SqlParameter("@M_Product_ID", productId),
                        new SqlParameter("@Base_UOM_ID", baseUomId)
                    }))
                    {
                        while (dr != null && dr.Read())
                        {
                            uoms.Add(new
                            {
                                id = Util.GetValueOfInt(dr["id"]),
                                name = Util.GetValueOfString(dr["name"])
                            });
                        }
                    }
                }
                return Ok(new { uoms = uoms });
            }
            catch (Exception ex)
            {
                Log.Log(Level.SEVERE, "VAS_205_OpenRequisitionsWidget.GetProductAlternateUOMs", ex);
                return Fail(ex.Message);
            }
        }

        /// <summary>
        /// Transactionally creates a new Purchase Order (C_Order, C_OrderLine) from the selected requisition lines.
        /// Enforces single-vendor and single-requisition validation, organization parity, and line links.
        /// </summary>
        /// <param name="requisitionId">Source M_Requisition_ID</param>
        /// <param name="vendorId">Target C_BPartner_ID</param>
        /// <param name="vendorLocationId">C_BPartner_Location_ID</param>
        /// <param name="vendorContactId">AD_User_ID</param>
        /// <param name="warehouseId">M_Warehouse_ID</param>
        /// <param name="docTypeId">C_DocType_ID (Target PO doc type)</param>
        /// <param name="paymentTermId">C_PaymentTerm_ID</param>
        /// <param name="paymentMethod">PaymentMethod code</param>
        /// <param name="priceListId">M_PriceList_ID</param>
        /// <param name="currencyId">C_Currency_ID</param>
        /// <param name="conversionTypeId">C_ConversionType_ID</param>
        /// <param name="incotermId">C_IncoTerm_ID</param>
        /// <param name="orderReference">POReference string</param>
        /// <param name="dateOrdered">PO Date string</param>
        /// <param name="datePromised">Promised Date string</param>
        /// <param name="priority">Priority string (3=Low, 5=Medium/Normal, 7=High, 1=Urgent)</param>
        /// <param name="description">Header Description</param>
        /// <param name="defaultTaxId">Header fallback C_Tax_ID for lines</param>
        /// <param name="linesJson">JSON serialized array of line payloads</param>
        /// <returns>JSON { success, orderId, documentNo, message }</returns>
        [HttpPost]
        [AjaxAuthorizeAttribute]
        [AjaxSessionFilterAttribute]
        public JsonResult CreatePurchaseOrder(
            int requisitionId = 0,
            int vendorId = 0,
            int vendorLocationId = 0,
            int vendorContactId = 0,
            int warehouseId = 0,
            int docTypeId = 0,
            int paymentTermId = 0,
            string paymentMethod = null,
            int priceListId = 0,
            int currencyId = 0,
            int conversionTypeId = 0,
            int incotermId = 0,
            string orderReference = null,
            string dateOrdered = null,
            string datePromised = null,
            string priority = null,
            string description = null,
            int defaultTaxId = 0,
            string linesJson = null)
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
            if (vendorId <= 0)
            {
                return Fail(Msg.GetMsg(ctx, "VAS_VendorRequired") ?? "Vendor is required.");
            }

            List<POLinePayload> lineInputs = string.IsNullOrWhiteSpace(linesJson)
                ? new List<POLinePayload>()
                : JsonConvert.DeserializeObject<List<POLinePayload>>(linesJson);

            if (lineInputs == null || lineInputs.Count == 0)
            {
                return Fail(Msg.GetMsg(ctx, "VAS_SelectLinesToOrder") ?? "Please select at least one line to order.");
            }

            Trx trx = null;
            try
            {
                trx = Trx.Get("VAS_205_PO_" + DateTime.Now.Ticks);

                // 1. Validate source requisition exists, is active, completed, and extract AD_Org_ID
                string reqCheckSql = @"
                    SELECT AD_Org_ID, M_Warehouse_ID, M_PriceList_ID, DocStatus
                    FROM M_Requisition
                    WHERE M_Requisition_ID = @M_Requisition_ID AND IsActive = 'Y'";

                int reqOrgId = 0;
                int reqWhId = 0;
                int reqPlId = 0;
                string reqDocStatus = "";

                using (IDataReader drReq = DB.ExecuteReader(reqCheckSql, new SqlParameter[] { new SqlParameter("@M_Requisition_ID", requisitionId) }, trx))
                {
                    if (drReq != null && drReq.Read())
                    {
                        reqOrgId = Util.GetValueOfInt(drReq["AD_Org_ID"]);
                        reqWhId = Util.GetValueOfInt(drReq["M_Warehouse_ID"]);
                        reqPlId = Util.GetValueOfInt(drReq["M_PriceList_ID"]);
                        reqDocStatus = Util.GetValueOfString(drReq["DocStatus"]);
                    }
                }

                if (reqOrgId <= 0 || reqDocStatus != "CO")
                {
                    trx.Rollback();
                    return Fail(Msg.GetMsg(ctx, "VAS_InvalidRequisitionStatus") ?? "Requisition must be approved and completed (DocStatus = 'CO').");
                }

                // 2. Validate warehouse fallback
                if (warehouseId <= 0)
                {
                    warehouseId = reqWhId > 0 ? reqWhId : GetDefaultWarehouse(ctx, reqOrgId);
                }

                // 3. Validate target DocType
                if (docTypeId <= 0)
                {
                    string dtSql = @"
                        SELECT C_DocType_ID
                        FROM C_DocType
                        WHERE DocBaseType='POO' AND IsActive='Y' AND AD_Client_ID IN (0, @AD_Client_ID)
                        ORDER BY AD_Org_ID DESC, C_DocType_ID ASC";
                    docTypeId = Util.GetValueOfInt(DB.ExecuteScalar(dtSql, new SqlParameter[] { new SqlParameter("@AD_Client_ID", ctx.GetAD_Client_ID()) }, trx));
                }

                // 4. Validate vendor location fallback
                if (vendorLocationId <= 0)
                {
                    string locSql = "SELECT C_BPartner_Location_ID FROM C_BPartner_Location WHERE C_BPartner_ID=@C_BPartner_ID AND IsActive='Y' ORDER BY IsBillTo DESC, C_BPartner_Location_ID ASC";
                    vendorLocationId = Util.GetValueOfInt(DB.ExecuteScalar(locSql, new SqlParameter[] { new SqlParameter("@C_BPartner_ID", vendorId) }, trx));
                }

                // 5. Parse Dates
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

                // 6. Create C_Order Header
                MOrder order = new MOrder(ctx, 0, trx);
                order.SetAD_Client_ID(ctx.GetAD_Client_ID());
                order.SetAD_Org_ID(reqOrgId); // STRICT RULE: Must equal selected M_Requisition.AD_Org_ID
                order.SetIsSOTrx(false);
                order.SetIsReturnTrx(false);
                order.SetC_BPartner_ID(vendorId);

                if (vendorLocationId > 0)
                {
                    order.SetC_BPartner_Location_ID(vendorLocationId);
                }
                if (vendorContactId > 0)
                {
                    order.SetAD_User_ID(vendorContactId);
                }
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
                if (priceListId > 0)
                {
                    order.SetM_PriceList_ID(priceListId);
                }
                else if (reqPlId > 0)
                {
                    order.SetM_PriceList_ID(reqPlId);
                }
                if (currencyId > 0)
                {
                    order.SetC_Currency_ID(currencyId);
                }
                if (conversionTypeId > 0)
                {
                    order.SetC_ConversionType_ID(conversionTypeId);
                }
                if (incotermId > 0 && order.Get_ColumnIndex("C_IncoTerm_ID") >= 0)
                {
                    order.Set_Value("C_IncoTerm_ID", incotermId);
                }
                if (!string.IsNullOrEmpty(paymentMethod) && order.Get_ColumnIndex("PaymentMethod") >= 0)
                {
                    order.Set_Value("PaymentMethod", paymentMethod);
                }
                if (!string.IsNullOrEmpty(orderReference))
                {
                    order.SetPOReference(orderReference);
                }
                if (!string.IsNullOrEmpty(priority))
                {
                    order.SetPriorityRule(priority);
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

                // 7. Create C_OrderLine rows
                int lineSeq = 10;
                foreach (POLinePayload item in lineInputs)
                {
                    if (item == null || item.Qty <= 0) { continue; }

                    // Re-validate remaining pending quantity immediately before insert
                    if (item.RequisitionLineId > 0)
                    {
                        string checkLineQtySql = @"
                            SELECT COALESCE(Qty, 0) AS ReqQty, COALESCE(QtyOrdered, 0) AS QtyOrdered
                            FROM M_RequisitionLine
                            WHERE M_RequisitionLine_ID = @M_RequisitionLine_ID AND M_Requisition_ID = @M_Requisition_ID AND IsActive = 'Y'";

                        decimal curReqQty = 0;
                        decimal curOrderedQty = 0;
                        using (IDataReader drLine = DB.ExecuteReader(checkLineQtySql, new SqlParameter[]
                        {
                            new SqlParameter("@M_RequisitionLine_ID", item.RequisitionLineId),
                            new SqlParameter("@M_Requisition_ID", requisitionId)
                        }, trx))
                        {
                            if (drLine != null && drLine.Read())
                            {
                                curReqQty = Util.GetValueOfDecimal(drLine["ReqQty"]);
                                curOrderedQty = Util.GetValueOfDecimal(drLine["QtyOrdered"]);
                            }
                        }

                        decimal remainingPending = curReqQty - curOrderedQty;
                        if (remainingPending <= 0 || item.Qty > remainingPending)
                        {
                            trx.Rollback();
                            return Fail(string.Format(Msg.GetMsg(ctx, "VAS_RequisitionLineQtyExceeded") ?? "Order quantity for line {0} exceeds remaining pending quantity ({1}).", item.LineNo, remainingPending));
                        }
                    }

                    MOrderLine ol = new MOrderLine(order);
                    ol.SetLine(lineSeq);
                    lineSeq += 10;

                    if (item.ProductId > 0)
                    {
                        ol.SetM_Product_ID(item.ProductId);
                    }
                    if (item.AttributeSetInstanceId > 0)
                    {
                        ol.SetM_AttributeSetInstance_ID(item.AttributeSetInstanceId);
                    }
                    if (item.UomId > 0)
                    {
                        ol.SetC_UOM_ID(item.UomId);
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

                    int lineTaxId = item.TaxId > 0 ? item.TaxId : defaultTaxId;
                    if (lineTaxId > 0)
                    {
                        ol.SetC_Tax_ID(lineTaxId);
                    }

                    if (!string.IsNullOrEmpty(item.DatePromised))
                    {
                        DateTime linePromise;
                        if (DateTime.TryParse(item.DatePromised, CultureInfo.InvariantCulture, DateTimeStyles.None, out linePromise))
                        {
                            ol.SetDatePromised(linePromise);
                        }
                    }
                    else
                    {
                        ol.SetDatePromised(promiseDate);
                    }

                    if (!string.IsNullOrEmpty(item.Description))
                    {
                        ol.SetDescription(item.Description);
                    }
                    if (!string.IsNullOrEmpty(item.PrintDescription) && ol.Get_ColumnIndex("PrintDescription") >= 0)
                    {
                        ol.Set_Value("PrintDescription", item.PrintDescription);
                    }

                    // Mandatory source link to M_RequisitionLine_ID
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

                    // Update QtyOrdered on M_RequisitionLine inside same transaction
                    if (item.RequisitionLineId > 0)
                    {
                        string updateReqLineSql = @"
                            UPDATE M_RequisitionLine
                            SET QtyOrdered = COALESCE(QtyOrdered, 0) + @OrderQty,
                                Updated = @Updated,
                                UpdatedBy = @UpdatedBy
                            WHERE M_RequisitionLine_ID = @M_RequisitionLine_ID";

                        DB.ExecuteQuery(updateReqLineSql, new SqlParameter[]
                        {
                            new SqlParameter("@OrderQty", item.Qty),
                            new SqlParameter("@Updated", DateTime.Now),
                            new SqlParameter("@UpdatedBy", ctx.GetAD_User_ID()),
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
                Log.Log(Level.SEVERE, "VAS_205_OpenRequisitionsWidget.CreatePurchaseOrder", ex);
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

        private Dictionary<int, ProductPoVendorDto> GetProductPreferredVendors(Ctx ctx, List<int> productIds)
        {
            Dictionary<int, ProductPoVendorDto> dict = new Dictionary<int, ProductPoVendorDto>();
            if (productIds == null || productIds.Count == 0)
            {
                return dict;
            }

            string idList = string.Join(",", productIds);
            string sql = string.Format(@"
                SELECT
                    mpo.M_Product_ID AS product_id,
                    mpo.C_BPartner_ID AS vendor_id,
                    bp.Name AS vendor_name,
                    mpo.PricePO AS vendor_price,
                    mpo.C_UOM_ID AS vendor_uom_id
                FROM M_Product_PO mpo
                INNER JOIN C_BPartner bp
                    ON bp.C_BPartner_ID = mpo.C_BPartner_ID
                WHERE mpo.IsActive = 'Y'
                  AND mpo.IsCurrentVendor = 'Y'
                  AND mpo.M_Product_ID IN ({0})
                ORDER BY mpo.M_Product_ID, bp.Name", idList);

            IDataReader dr = null;
            try
            {
                dr = DB.ExecuteReader(sql, null);
                while (dr != null && dr.Read())
                {
                    int pId = Util.GetValueOfInt(dr["product_id"]);
                    if (!dict.ContainsKey(pId))
                    {
                        dict[pId] = new ProductPoVendorDto
                        {
                            productId = pId,
                            vendorId = Util.GetValueOfInt(dr["vendor_id"]),
                            vendorName = Util.GetValueOfString(dr["vendor_name"]),
                            vendorPrice = Util.GetValueOfDecimal(dr["vendor_price"]),
                            vendorUomId = Util.GetValueOfInt(dr["vendor_uom_id"])
                        };
                    }
                }
            }
            catch (Exception ex)
            {
                Log.Log(Level.WARNING, "VAS_205_OpenRequisitionsWidget.GetProductPreferredVendors", ex);
            }
            finally
            {
                if (dr != null)
                {
                    dr.Close();
                    dr.Dispose();
                }
            }

            return dict;
        }

        private int GetDefaultWarehouse(Ctx ctx, int orgId)
        {
            string sql = "SELECT M_Warehouse_ID FROM M_Warehouse WHERE IsActive='Y' AND AD_Client_ID=@AD_Client_ID ORDER BY AD_Org_ID DESC, M_Warehouse_ID ASC";
            return Util.GetValueOfInt(DB.ExecuteScalar(sql, new SqlParameter[]
            {
                new SqlParameter("@AD_Client_ID", ctx.GetAD_Client_ID())
            }, null));
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

        private sealed class RequisitionLineDto
        {
            public int requisitionLineId { get; set; }
            public int requisitionId { get; set; }
            public int lineNo { get; set; }
            public int productId { get; set; }
            public string productName { get; set; }
            public string productCode { get; set; }
            public int attributeSetInstanceId { get; set; }
            public string attributeDescription { get; set; }
            public int uomId { get; set; }
            public string uomName { get; set; }
            public decimal requestedQty { get; set; }
            public decimal alreadyOrderedQty { get; set; }
            public decimal pendingQty { get; set; }
            public decimal requisitionRate { get; set; }
            public string description { get; set; }
            public string printDescription { get; set; }
            public int lineVendorId { get; set; }
            public string lineVendorName { get; set; }
        }

        private sealed class ProductPoVendorDto
        {
            public int productId { get; set; }
            public int vendorId { get; set; }
            public string vendorName { get; set; }
            public decimal vendorPrice { get; set; }
            public int vendorUomId { get; set; }
        }

        private sealed class POLinePayload
        {
            [JsonProperty("requisitionLineId")]
            public int RequisitionLineId { get; set; }

            [JsonProperty("lineNo")]
            public int LineNo { get; set; }

            [JsonProperty("productId")]
            public int ProductId { get; set; }

            [JsonProperty("attributeSetInstanceId")]
            public int AttributeSetInstanceId { get; set; }

            [JsonProperty("uomId")]
            public int UomId { get; set; }

            [JsonProperty("qty")]
            public decimal Qty { get; set; }

            [JsonProperty("rate")]
            public decimal Rate { get; set; }

            [JsonProperty("taxId")]
            public int TaxId { get; set; }

            [JsonProperty("datePromised")]
            public string DatePromised { get; set; }

            [JsonProperty("description")]
            public string Description { get; set; }

            [JsonProperty("printDescription")]
            public string PrintDescription { get; set; }
        }
    }
}
