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
    public class VAS_092_ExpectedGRNWidgetController : Controller
    {
        /// <summary>
        /// One page of completed vendor purchase orders whose expected (promised)
        /// date is today or later and that still have open quantity, soonest first.
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

            string rawSql = @"
                SELECT o.C_Order_ID AS PO_ID,
                       o.DocumentNo AS PO_NO,
                       bp.Name AS Supplier,
                       TRIM(COALESCE(loc.Address1, '') || ' ' || COALESCE(loc.City, '')) AS Address_Line,
                       wh.Name AS Warehouse_Name,
                       cur.CurSymbol AS Cur_Symbol,
                       cur.StdPrecision AS Std_Precision,
                       COALESCE(ol.DatePromised, o.DatePromised) AS Line_Promise_Date,
                       ol.C_OrderLine_ID AS PO_Line_ID,
                       COALESCE(ol.LineNetAmt, 0) AS Line_Net_Amt
                FROM C_Order o
                INNER JOIN C_OrderLine ol ON (ol.C_Order_ID=o.C_Order_ID AND ol.IsActive='Y')
                INNER JOIN C_BPartner bp ON (bp.C_BPartner_ID=o.C_BPartner_ID AND bp.IsActive='Y')
                LEFT OUTER JOIN C_BPartner_Location bpl ON (bpl.C_BPartner_Location_ID=o.C_BPartner_Location_ID AND bpl.IsActive='Y')
                LEFT OUTER JOIN C_Location loc ON (loc.C_Location_ID=bpl.C_Location_ID)
                LEFT OUTER JOIN M_Warehouse wh ON (wh.M_Warehouse_ID=COALESCE(ol.M_Warehouse_ID, o.M_Warehouse_ID) AND wh.IsActive='Y')
                LEFT OUTER JOIN C_Currency cur ON (cur.C_Currency_ID=o.C_Currency_ID AND cur.IsActive='Y')
                WHERE o.IsActive='Y'
                  AND ol.IsActive='Y'
                  AND o.IsSOTrx='N'
                  AND o.DocStatus='CO'
                  AND o.AD_Client_ID=@AD_Client_ID
                  AND COALESCE(ol.QtyOrdered, 0) > COALESCE(ol.QtyDelivered, 0)
                  AND COALESCE(ol.DatePromised, o.DatePromised) >= @Today";

            rawSql = MRole.GetDefault(ctx).AddAccessSQL(
                rawSql,
                "o",
                MRole.SQL_FULLYQUALIFIED,
                MRole.SQL_RO
            );

            string sql = @"
                SELECT ExpectedPO.PO_ID,
                       ExpectedPO.PO_NO,
                       ExpectedPO.Supplier,
                       ExpectedPO.Address_Line,
                       ExpectedPO.Warehouse_Name,
                       ExpectedPO.Cur_Symbol,
                       ExpectedPO.Std_Precision,
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
                           RawData.Cur_Symbol,
                           RawData.Std_Precision,
                           MIN(RawData.Line_Promise_Date) AS Promise_Date,
                           COUNT(RawData.PO_Line_ID) AS Line_Count,
                           SUM(RawData.Line_Net_Amt) AS PO_Value
                    FROM (
                        " + rawSql + @"
                    ) RawData
                    GROUP BY RawData.PO_ID,
                             RawData.PO_NO,
                             RawData.Supplier,
                             RawData.Address_Line,
                             RawData.Warehouse_Name,
                             RawData.Cur_Symbol,
                             RawData.Std_Precision
                ) ExpectedPO
                ORDER BY ExpectedPO.Promise_Date, ExpectedPO.PO_NO
                OFFSET @Offset ROWS FETCH NEXT @PageSize ROWS ONLY";

            List<SqlParameter> parameters = new List<SqlParameter>();
            parameters.Add(new SqlParameter("@AD_Client_ID", ctx.GetAD_Client_ID()));
            parameters.Add(new SqlParameter("@Today", DateTime.Today));
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
                        poNo = Util.GetValueOfString(dr["PO_NO"]),
                        supplier = Util.GetValueOfString(dr["Supplier"]),
                        addressLine = Util.GetValueOfString(dr["Address_Line"]),
                        warehouseName = Util.GetValueOfString(dr["Warehouse_Name"]),
                        curSymbol = Util.GetValueOfString(dr["Cur_Symbol"]),
                        stdPrecision = Util.GetValueOfInt(dr["Std_Precision"]),
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

            string lineSql = @"
                SELECT ol.C_OrderLine_ID AS PO_Line_ID,
                       p.Name AS Item_Name,
                       COALESCE(ol.QtyOrdered, 0) AS PO_Qty,
                       COALESCE(ol.QtyDelivered, 0) AS Already_Received_Qty,
                       COALESCE(ol.QtyOrdered, 0) - COALESCE(ol.QtyDelivered, 0) AS Default_Received_Qty,
                       COALESCE(u.UOMSymbol, u.Name) AS UOM
                FROM C_Order o
                INNER JOIN C_OrderLine ol ON (ol.C_Order_ID=o.C_Order_ID)
                INNER JOIN M_Product p ON (p.M_Product_ID=ol.M_Product_ID AND p.IsActive='Y')
                LEFT OUTER JOIN C_UOM u ON (u.C_UOM_ID=ol.C_UOM_ID AND u.IsActive='Y')
                WHERE o.IsActive='Y'
                  AND ol.IsActive='Y'
                  AND o.IsSOTrx='N'
                  AND o.DocStatus='CO'
                  AND o.AD_Client_ID=@AD_Client_ID
                  AND ol.C_Order_ID=@PO_ID
                  AND COALESCE(ol.QtyOrdered, 0) > COALESCE(ol.QtyDelivered, 0)";

            lineSql = MRole.GetDefault(ctx).AddAccessSQL(
                lineSql,
                "o",
                MRole.SQL_FULLYQUALIFIED,
                MRole.SQL_RO
            );

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

                receipt.SetDocAction(MInOut.DOCACTION_Complete);
                if (!receipt.ProcessIt(MInOut.DOCACTION_Complete))
                {
                    string processMessage = receipt.GetProcessMsg();
                    trx.Rollback();
                    return Fail(!string.IsNullOrEmpty(processMessage) ? processMessage : "GRN could not be completed.");
                }

                if (!receipt.Save(trx))
                {
                    trx.Rollback();
                    return Fail(GetSaveError(ctx, "VAS_GRNNotSaved", "GRN could not be completed."));
                }

                trx.Commit();

                return Ok(new
                {
                    success = true,
                    shipmentId = receipt.GetM_InOut_ID(),
                    grnId = receipt.GetM_InOut_ID(),
                    grnNo = receipt.GetDocumentNo(),
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
            string sql = @"
                SELECT DocType.C_DocType_ID
                FROM C_DocType DocType
                WHERE DocType.DocBaseType='MMR'
                  AND DocType.AD_Client_ID=@AD_Client_ID
                  AND DocType.IsActive='Y'
                  AND DocType.AD_Org_ID IN (0, @AD_Org_ID)
                  AND DocType.IsSOTrx='N'
                  AND DocType.IsReturnTrx='N'
                ORDER BY DocType.AD_Org_ID DESC";

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
