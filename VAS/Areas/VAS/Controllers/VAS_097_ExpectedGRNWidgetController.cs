using Newtonsoft.Json;
using System;
using System.Collections.Generic;
using System.Data;
using System.Data.SqlClient;
using System.Globalization;
using System.Linq;
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
    /// Module Name : Expected GRN (Material Receipt / GRN dashboard)
    /// Purpose     : 3x2 glass document list of completed vendor purchase orders
    ///               whose promised (expected) date is today or in the future and
    ///               that still have open quantity to receive. Each row shows the
    ///               PO number, supplier, ship-to address, destination warehouse,
    ///               line count and PO value (formatted with the order currency's
    ///               own standard precision read from the system, never hard-coded).
    ///               A row click loads the PO's open lines and lets the user enter a
    ///               received quantity per line, then creates and completes a
    ///               Material Receipt (GRN) through the server-side M_InOut /
    ///               M_InOutLine model flow inside a single transaction.
    ///               MRole is applied to the primary fetched table (C_Order) on every
    ///               read query; all input is parameterized; queries run on both
    ///               Oracle (12c+) and PostgreSQL via OFFSET/FETCH and ANSI '||'.
    /// Chronological development:
    ///   VAI050   2024-09-20 Created (legacy ProductController-backed form widget)
    ///   VAI147   2026-06-29 Rebuilt on the Onfinity GRN-dashboard pattern with a
    ///                       dedicated controller, MRole, parameterized SQL and the
    ///                       shared GRN line-entry create flow.
    /// </summary>
    public class VAS_097_ExpectedGRNWidgetController : Controller
    {
        /// <summary>
        /// One page of completed vendor purchase orders whose promised date is
        /// today or later and that still have open (un-received, not already on
        /// an unposted GRN) quantity - same rules as the existing
        /// VAS.VAS_ExpectedGRNWidget, newest promise date first (review #26).
        /// </summary>
        /// <param name="pageNo">1-based page number.</param>
        /// <param name="pageSize">Rows per page (default 5, max 50).</param>
        /// <returns>JSON { rows[], pageNo, pageSize, totalRecords, totalPages }.</returns>
        [AjaxAuthorizeAttribute]
        [AjaxSessionFilterAttribute]
        public JsonResult GetExpectedPurchaseOrders(int pageNo = 1, int pageSize = 5)
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

            // Review #26 (follow-up): same SQL semantics as the existing
            // VAS.VAS_ExpectedGRNWidget (Product/GetExpectedDelivery, Type 'EG'):
            // completed vendor POs promised TODAY OR LATER, no returns /
            // quotations / blanket orders, open qty also nets off quantities
            // already sitting on unposted GRNs, item products only unless the
            // tenant allows non-item lines, newest promise date first.
            bool allowNonItem = Util.GetValueOfString(ctx.GetContext("$AllowNonItem")).Equals("Y");

            string rawSql = @"
                SELECT o.C_Order_ID AS PO_ID,
                       o.DocumentNo AS PO_NO,
                       cb.Name AS Supplier,
                       l.Name AS Address_Line,
                       w.Name AS Warehouse_Name,
                       o.DatePromised AS Promise_Date,
                       COALESCE(CURRENCYCONVERT(o.GrandTotal, o.C_Currency_ID, @Base_Currency_ID, o.DateAcct, o.C_ConversionType_ID, o.AD_Client_ID, o.AD_Org_ID), 0) AS PO_Value,
                       ol.C_OrderLine_ID AS PO_Line_ID
                FROM C_Order o
                INNER JOIN C_OrderLine ol ON (ol.C_Order_ID=o.C_Order_ID)"
                + (!allowNonItem ? @"
                INNER JOIN M_Product ItemProduct ON (ItemProduct.M_Product_ID=ol.M_Product_ID AND ItemProduct.ProductType='I')" : "") + @"
                INNER JOIN M_Warehouse w ON (w.M_Warehouse_ID=o.M_Warehouse_ID)
                INNER JOIN C_BPartner cb ON (cb.C_BPartner_ID=o.C_BPartner_ID)
                INNER JOIN C_BPartner_Location l ON (l.C_BPartner_Location_ID=o.C_BPartner_Location_ID)
                WHERE o.AD_Client_ID=@AD_Client_ID
                  AND o.DocStatus='CO'
                  AND o.DatePromised>=@Promised_From
                  AND o.IsSOTrx='N'
                  AND o.IsReturnTrx='N'
                  AND o.IsSalesQuotation='N'
                  AND o.IsBlanketTrx='N'
                  AND (COALESCE(ol.QtyOrdered, 0) - COALESCE(ol.QtyDelivered, 0) - __VAS_UNPOSTED_GRN_QTY__) > 0";

            // MRole must not see the unposted-GRN netting subquery: on roles
            // with record-access rules AddAccessSQL appends a predicate for
            // every table it finds - including the subquery's M_InOutLine
            // alias - to the OUTER where-clause, where the alias is out of
            // scope (ORA-00904 "il"."M_InOutLine_ID"). The subquery is
            // injected after the access SQL is applied.
            rawSql = MRole.GetDefault(ctx).AddAccessSQL(
                rawSql,
                "o",
                MRole.SQL_FULLYQUALIFIED,
                MRole.SQL_RO
            );

            rawSql = rawSql.Replace("__VAS_UNPOSTED_GRN_QTY__", @"(
                      SELECT COALESCE(SUM(il.MovementQty), 0)
                      FROM M_InOut i
                      INNER JOIN M_InOutLine il ON (i.M_InOut_ID=il.M_InOut_ID)
                      WHERE il.C_OrderLine_ID=ol.C_OrderLine_ID
                        AND il.IsActive='Y'
                        AND i.DocStatus NOT IN ('RE','VO','CL','CO')
                  )");

            string sql = @"
                SELECT ExpectedPO.PO_ID,
                       ExpectedPO.PO_NO,
                       ExpectedPO.Supplier,
                       ExpectedPO.Address_Line,
                       ExpectedPO.Warehouse_Name,
                       ExpectedPO.Promise_Date,
                       ExpectedPO.Line_Count,
                       ExpectedPO.PO_Value,
                       COUNT(1) OVER () AS TotalRecords
                FROM (
                    SELECT RawData.PO_ID,
                           RawData.PO_NO,
                           RawData.Supplier,
                           RawData.Address_Line,
                           RawData.Warehouse_Name,
                           RawData.Promise_Date,
                           RawData.PO_Value,
                           COUNT(RawData.PO_Line_ID) AS Line_Count
                    FROM (
                        " + rawSql + @"
                    ) RawData
                    GROUP BY RawData.PO_ID,
                             RawData.PO_NO,
                             RawData.Supplier,
                             RawData.Address_Line,
                             RawData.Warehouse_Name,
                             RawData.Promise_Date,
                             RawData.PO_Value
                ) ExpectedPO
                ORDER BY ExpectedPO.Promise_Date DESC, ExpectedPO.PO_NO
                OFFSET @Offset ROWS FETCH NEXT @PageSize ROWS ONLY";

            // Parameters in order of appearance (the DB layer binds positionally).
            List<SqlParameter> parameters = new List<SqlParameter>();
            parameters.Add(new SqlParameter("@Base_Currency_ID", ctx.GetContextAsInt("$C_Currency_ID")));
            parameters.Add(new SqlParameter("@AD_Client_ID", ctx.GetAD_Client_ID()));
            parameters.Add(new SqlParameter("@Promised_From", SqlDbType.DateTime) { Value = DateTime.Today });
            parameters.Add(new SqlParameter("@Offset", offset));
            parameters.Add(new SqlParameter("@PageSize", pageSize));

            // Legacy behaviour: the value is shown in the session/base currency,
            // so every row carries that one currency's symbol and precision.
            string baseCurSymbol = "";
            string baseCurIso = "";
            int baseStdPrecision = 2;
            IDataReader curReader = null;
            try
            {
                curReader = DB.ExecuteReader(
                    @"SELECT CurSymbol, ISO_Code, StdPrecision
                      FROM C_Currency
                      WHERE C_Currency_ID=@Cur_ID",
                    new SqlParameter[] { new SqlParameter("@Cur_ID", ctx.GetContextAsInt("$C_Currency_ID")) });
                if (curReader != null && curReader.Read())
                {
                    baseCurSymbol = Util.GetValueOfString(curReader["CurSymbol"]);
                    baseCurIso = Util.GetValueOfString(curReader["ISO_Code"]);
                    baseStdPrecision = Util.GetValueOfInt(curReader["StdPrecision"]);
                    if (String.IsNullOrEmpty(baseCurSymbol)) { baseCurSymbol = baseCurIso; }
                }
            }
            finally
            {
                if (curReader != null) { curReader.Close(); curReader.Dispose(); }
            }

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
                        poNo = Util.GetValueOfString(dr["PO_NO"]),
                        supplier = Util.GetValueOfString(dr["Supplier"]),
                        addressLine = Util.GetValueOfString(dr["Address_Line"]),
                        warehouseName = Util.GetValueOfString(dr["Warehouse_Name"]),
                        curSymbol = baseCurSymbol,
                        currencyIso = baseCurIso,
                        stdPrecision = baseStdPrecision,
                        promiseDate = promiseDate.HasValue ? promiseDate.Value.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture) : "",
                        lineCount = Util.GetValueOfInt(dr["Line_Count"]),
                        poValue = Util.GetValueOfDecimal(dr["PO_Value"])
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

            // Review #26 (follow-up): open quantity nets off quantities already on
            // unposted GRNs, exactly like the existing VAS.VAS_ExpectedGRNWidget.
            bool allowNonItem = Util.GetValueOfString(ctx.GetContext("$AllowNonItem")).Equals("Y");

            string lineSql = @"
                SELECT ol.C_OrderLine_ID AS PO_Line_ID,
                       p.Name AS Item_Name,
                       asi.Description AS Attribute_Name,
                       COALESCE(ol.QtyOrdered, 0) AS PO_Qty,
                       COALESCE(ol.QtyDelivered, 0) AS Already_Received_Qty,
                       COALESCE(ol.QtyOrdered, 0) - COALESCE(ol.QtyDelivered, 0) - __VAS_UNPOSTED_GRN_QTY__ AS Default_Received_Qty,
                       u.Name AS UOM
                FROM C_Order o
                INNER JOIN C_OrderLine ol ON (ol.C_Order_ID=o.C_Order_ID)
                INNER JOIN M_Product p ON (p.M_Product_ID=ol.M_Product_ID" + (!allowNonItem ? " AND p.ProductType='I'" : "") + @")
                LEFT OUTER JOIN M_AttributeSetInstance asi ON (asi.M_AttributeSetInstance_ID=ol.M_AttributeSetInstance_ID)
                LEFT OUTER JOIN C_UOM u ON (u.C_UOM_ID=ol.C_UOM_ID)
                WHERE o.IsSOTrx='N'
                  AND o.DocStatus='CO'
                  AND o.AD_Client_ID=@AD_Client_ID
                  AND ol.C_Order_ID=@PO_ID
                  AND (COALESCE(ol.QtyOrdered, 0) - COALESCE(ol.QtyDelivered, 0) - __VAS_UNPOSTED_GRN_QTY__) > 0";

            // Same ORA-00904 guard as the list query: the netting subquery is
            // injected only after MRole has added its access predicates.
            lineSql = MRole.GetDefault(ctx).AddAccessSQL(
                lineSql,
                "o",
                MRole.SQL_FULLYQUALIFIED,
                MRole.SQL_RO
            );

            lineSql = lineSql.Replace("__VAS_UNPOSTED_GRN_QTY__", @"(
                      SELECT COALESCE(SUM(il.MovementQty), 0)
                      FROM M_InOut i
                      INNER JOIN M_InOutLine il ON (i.M_InOut_ID=il.M_InOut_ID)
                      WHERE il.C_OrderLine_ID=ol.C_OrderLine_ID
                        AND il.IsActive='Y'
                        AND i.DocStatus NOT IN ('RE','VO','CL','CO')
                  )");

            string sql = lineSql + @"
                ORDER BY ol.Line";

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
                    decimal poQty = Util.GetValueOfDecimal(dr["PO_Qty"]);
                    decimal alreadyReceivedQty = Util.GetValueOfDecimal(dr["Already_Received_Qty"]);
                    decimal defaultReceivedQty = Util.GetValueOfDecimal(dr["Default_Received_Qty"]);

                    rows.Add(new
                    {
                        poLineId = Util.GetValueOfInt(dr["PO_Line_ID"]),
                        itemName = Util.GetValueOfString(dr["Item_Name"]),
                        attributeName = Util.GetValueOfString(dr["Attribute_Name"]),
                        poQty = poQty,
                        alreadyReceivedQty = alreadyReceivedQty,
                        defaultReceivedQty = defaultReceivedQty,
                        openQty = defaultReceivedQty,
                        uom = Util.GetValueOfString(dr["UOM"])
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
        /// Creates and completes a Material Receipt (GRN) for the given purchase
        /// order and received quantities. Re-validates each line against the live
        /// open quantity, then saves the receipt and its lines through the M_InOut /
        /// M_InOutLine model classes inside a single transaction (rolled back on any
        /// failure).
        /// </summary>
        /// <param name="poId">C_Order_ID of the purchase order being received.</param>
        /// <param name="linesJson">JSON array of { poLineId, receivedQty } inputs.</param>
        /// <returns>JSON { success, grnId, grnNo, message } or { success:false, error }.</returns>
        [HttpPost]
        [AjaxAuthorizeAttribute]
        [AjaxSessionFilterAttribute]
        public JsonResult CreateGRN(int poId = 0, string linesJson = null)
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

            List<ExpectedGRNLineInput> inputs = string.IsNullOrWhiteSpace(linesJson)
                ? new List<ExpectedGRNLineInput>()
                : JsonConvert.DeserializeObject<List<ExpectedGRNLineInput>>(linesJson);
            if (inputs == null)
            {
                inputs = new List<ExpectedGRNLineInput>();
            }
            Dictionary<int, decimal> qtyByLine = new Dictionary<int, decimal>();

            foreach (ExpectedGRNLineInput input in inputs)
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
            }

            if (qtyByLine.Count == 0)
            {
                return Fail("Enter received quantity for at least one line.");
            }

            Dictionary<int, ExpectedGRNLineInfo> openLines = GetOpenLineInfo(ctx, poId, qtyByLine.Keys);

            if (openLines.Count != qtyByLine.Count)
            {
                return Fail("One or more selected PO lines are no longer open.");
            }

            foreach (KeyValuePair<int, decimal> selectedLine in qtyByLine)
            {
                ExpectedGRNLineInfo lineInfo = openLines[selectedLine.Key];
                if (selectedLine.Value > lineInfo.OpenQty)
                {
                    return Fail("Received quantity cannot be greater than open quantity.");
                }
            }

            Trx trx = null;

            try
            {
                trx = Trx.Get("VAS_ExpectedGRN" + DateTime.Now.Ticks);

                MOrder order = new MOrder(ctx, poId, trx);
                if (order.Get_ID() == 0 || order.IsSOTrx() || order.GetDocStatus() != MOrder.DOCSTATUS_Completed)
                {
                    trx.Rollback();
                    return Fail("Purchase Order is not available for receiving.");
                }

                int warehouseId = order.GetM_Warehouse_ID();
                if (warehouseId <= 0)
                {
                    warehouseId = openLines.Values.First().WarehouseId;
                }

                int locatorId = GetDefaultLocatorId(warehouseId, trx);
                int docTypeId = GetDocTypeId(order.GetAD_Org_ID(), ctx.GetAD_Client_ID());

                if (docTypeId <= 0)
                {
                    trx.Rollback();
                    return Fail("Material Receipt document type was not found.");
                }

                MInOut receipt = new MInOut(order, docTypeId, DateTime.Now);
                receipt.SetAD_Client_ID(ctx.GetAD_Client_ID());
                receipt.SetAD_Org_ID(order.GetAD_Org_ID());
                receipt.SetIsSOTrx(false);
                receipt.SetIsReturnTrx(false);
                receipt.SetMovementType(MInOut.MOVEMENTTYPE_VendorReceipts);
                receipt.SetC_DocType_ID(docTypeId);
                receipt.SetM_Warehouse_ID(warehouseId);
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
                    MInOutLine receiptLine = new MInOutLine(receipt);
                    receiptLine.SetOrderLine(orderLine, locatorId, receivedQty);
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

                // The receipt + lines are saved in this transaction as a DRAFT;
                // commit them, then run the standard document completion through
                // the document process (DocumentEngine.CompleteOrReverse - the same
                // path the core runs on Complete). That process executes outside
                // this transaction, so the record is committed first. The
                // AD_Table_ID and AD_Process_ID are resolved from the dictionary at
                // runtime by name/Value - never hardcoded.
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

                MInOut completedReceipt = new MInOut(ctx, receiptId, null);
                return Ok(new
                {
                    success = true,
                    shipmentId = receiptId,
                    grnId = receiptId,
                    grnNo = completedReceipt.GetDocumentNo(),
                    message = Msg.GetMsg(ctx, "VAS_GRNSaved") ?? "GRN created."
                });
            }
            catch (Exception ex)
            {
                if (trx != null)
                {
                    trx.Rollback();
                }

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

        /// <summary>
        /// Re-reads the live open quantity and warehouse for the selected PO lines,
        /// keyed by C_OrderLine_ID, so CreateGRN can validate against current data.
        /// </summary>
        /// <param name="ctx">Session context.</param>
        /// <param name="poId">C_Order_ID of the purchase order.</param>
        /// <param name="lineIds">Selected C_OrderLine_ID values.</param>
        /// <returns>Map of C_OrderLine_ID to its open-line info.</returns>
        private Dictionary<int, ExpectedGRNLineInfo> GetOpenLineInfo(Ctx ctx, int poId, IEnumerable<int> lineIds)
        {
            Dictionary<int, ExpectedGRNLineInfo> lines = new Dictionary<int, ExpectedGRNLineInfo>();
            List<int> selectedLineIds = lineIds == null
                ? new List<int>()
                : lineIds.Where(id => id > 0).Distinct().ToList();

            if (selectedLineIds.Count == 0)
            {
                return lines;
            }

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
                       COALESCE(ol.QtyOrdered, 0) - COALESCE(ol.QtyDelivered, 0) AS Open_Qty
                FROM C_Order o
                INNER JOIN C_OrderLine ol ON (ol.C_Order_ID=o.C_Order_ID)
                INNER JOIN M_Product p ON (p.M_Product_ID=ol.M_Product_ID AND p.IsActive='Y')
                WHERE o.IsActive='Y'
                  AND ol.IsActive='Y'
                  AND o.IsSOTrx='N'
                  AND o.DocStatus='CO'
                  AND o.AD_Client_ID=@AD_Client_ID
                  AND o.C_Order_ID=@PO_ID
                  AND ol.C_OrderLine_ID IN (" + string.Join(",", lineIdParameters) + @")
                  AND COALESCE(ol.QtyOrdered, 0) > COALESCE(ol.QtyDelivered, 0)";

            sql = MRole.GetDefault(ctx).AddAccessSQL(
                sql,
                "o",
                MRole.SQL_FULLYQUALIFIED,
                MRole.SQL_RO
            );

            IDataReader dr = null;
            try
            {
                dr = DB.ExecuteReader(sql, parameters.ToArray());
                while (dr != null && dr.Read())
                {
                    int lineId = Util.GetValueOfInt(dr["PO_Line_ID"]);
                    lines[lineId] = new ExpectedGRNLineInfo
                    {
                        PoLineId = lineId,
                        WarehouseId = Util.GetValueOfInt(dr["Warehouse_ID"]),
                        OpenQty = Util.GetValueOfDecimal(dr["Open_Qty"])
                    };
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

            return lines;
        }

        /// <summary>
        /// Resolves the Material Receipt (DocBaseType 'MMR') document type for the
        /// given organization, preferring an org-specific type over the shared one.
        /// </summary>
        /// <param name="orgId">AD_Org_ID of the receipt.</param>
        /// <param name="clientId">AD_Client_ID context.</param>
        /// <returns>C_DocType_ID, or 0 when none is configured.</returns>
        private int GetDocTypeId(int orgId, int clientId)
        {
            // Prefer an org-specific receipt type; C_DocType_ID is the deterministic
            // tiebreak so the plain "MM Receipt" (lower id) is chosen over the
            // "MM Receipt with Confirmation" one - the latter completes to In
            // Progress (awaiting confirmation) instead of Completed. Without the
            // tiebreak the pick was arbitrary and GRNs could land un-completed.
            string sql = @"
                SELECT DocType.C_DocType_ID
                FROM C_DocType DocType
                WHERE DocType.DocBaseType='MMR'
                  AND DocType.AD_Client_ID=@AD_Client_ID
                  AND DocType.IsActive='Y'
                  AND DocType.AD_Org_ID IN (0, @AD_Org_ID)
                  AND DocType.IsSOTrx='N'
                  AND DocType.IsReturnTrx='N'
                ORDER BY DocType.AD_Org_ID DESC, DocType.C_DocType_ID";

            List<SqlParameter> parameters = new List<SqlParameter>();
            parameters.Add(new SqlParameter("@AD_Client_ID", clientId));
            parameters.Add(new SqlParameter("@AD_Org_ID", orgId));

            return Util.GetValueOfInt(DB.ExecuteScalar(sql, parameters.ToArray(), null));
        }

        /// <summary>
        /// Returns the default locator for a warehouse (falling back to any active
        /// locator), used as the receipt-line locator.
        /// </summary>
        /// <param name="warehouseId">M_Warehouse_ID of the receipt.</param>
        /// <param name="trx">Active transaction.</param>
        /// <returns>M_Locator_ID, or 0 when none is found.</returns>
        private int GetDefaultLocatorId(int warehouseId, Trx trx)
        {
            if (warehouseId <= 0) { return 0; }

            string sql = @"
                SELECT Locator.M_Locator_ID
                FROM M_Locator Locator
                WHERE Locator.IsActive='Y'
                  AND Locator.M_Warehouse_ID=@M_Warehouse_ID
                ORDER BY Locator.IsDefault DESC";

            SqlParameter[] parameters = { new SqlParameter("@M_Warehouse_ID", warehouseId) };
            return Util.GetValueOfInt(DB.ExecuteScalar(sql, parameters, trx));
        }

        /// <summary>
        /// Builds a user-facing save error: the logged model error if present, else
        /// the resolved fallback message key, else the literal fallback text.
        /// </summary>
        /// <param name="ctx">Session context.</param>
        /// <param name="fallbackKey">AD_Message key used when no model error exists.</param>
        /// <param name="fallback">Literal text used when the key does not resolve.</param>
        /// <returns>The resolved error message.</returns>
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

        /// <summary>Resolves the AD_Table_ID for a physical table name (0 when missing).</summary>
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

        private sealed class ExpectedGRNLineInput
        {
            [JsonProperty("poLineId")]
            public int PoLineId { get; set; }

            [JsonProperty("receivedQty")]
            public decimal ReceivedQty { get; set; }
        }

        private sealed class ExpectedGRNLineInfo
        {
            public int PoLineId { get; set; }
            public int WarehouseId { get; set; }
            public decimal OpenQty { get; set; }
        }
    }
}
