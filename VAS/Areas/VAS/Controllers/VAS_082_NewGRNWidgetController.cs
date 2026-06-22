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
    /// New GRN quick-action widget.
    /// Opens purchase orders, loads open PO lines, and creates a Material Receipt
    /// through the server-side M_InOut/M_InOutLine flow.
    /// </summary>
    public class VAS_082_NewGRNWidgetController : Controller
    {
        [AjaxAuthorizeAttribute]
        [AjaxSessionFilterAttribute]
        public JsonResult GetOpenPurchaseOrders(int pageNo = 1, int pageSize = 20)
        {
            if (Session["ctx"] == null)
            {
                return Fail(Msg.GetMsg(Env.GetCtx(), "SessionExpired") ?? "Session Expired");
            }

            Ctx ctx = Session["ctx"] as Ctx;

            if (pageNo <= 0) { pageNo = 1; }
            if (pageSize <= 0) { pageSize = 20; }
            if (pageSize > 50) { pageSize = 50; }

            int offset = (pageNo - 1) * pageSize;

            string baseSql = @"
                SELECT o.C_Order_ID AS PO_ID,
                       o.DocumentNo AS PO_NO,
                       bp.Name AS Supplier,
                       wh.Name AS Warehouse_Name,
                       MIN(COALESCE(ol.DatePromised, o.DatePromised)) AS Promise_Date,
                       COUNT(ol.C_OrderLine_ID) AS Open_Line_Count
                FROM C_Order o
                INNER JOIN C_OrderLine ol ON ol.C_Order_ID=o.C_Order_ID
                INNER JOIN C_BPartner bp ON (bp.C_BPartner_ID=o.C_BPartner_ID AND bp.IsActive='Y')
                LEFT OUTER JOIN M_Warehouse wh ON (wh.M_Warehouse_ID=COALESCE(ol.M_Warehouse_ID, o.M_Warehouse_ID) AND wh.IsActive='Y')
                WHERE o.IsActive='Y'
                  AND ol.IsActive='Y'
                  AND o.IsSOTrx='N'
                  AND o.DocStatus='CO'
                  AND o.AD_Client_ID=@AD_Client_ID
                  AND COALESCE(ol.QtyOrdered, 0) > COALESCE(ol.QtyDelivered, 0)
                GROUP BY o.C_Order_ID,
                         o.DocumentNo,
                         bp.Name,
                         wh.Name";

            baseSql = MRole.GetDefault(ctx).AddAccessSQL(
                baseSql,
                "o",
                MRole.SQL_FULLYQUALIFIED,
                MRole.SQL_RO
            );

            string sql = @"
                SELECT OpenPO.PO_ID,
                       OpenPO.PO_NO,
                       OpenPO.Supplier,
                       OpenPO.Warehouse_Name,
                       OpenPO.Promise_Date,
                       OpenPO.Open_Line_Count,
                       COUNT(1) OVER () AS TotalRecords
                FROM (
                    " + baseSql + @"
                ) OpenPO
                ORDER BY OpenPO.Promise_Date, OpenPO.PO_NO
                OFFSET @Offset ROWS FETCH NEXT @PageSize ROWS ONLY";

            List<SqlParameter> parameters = new List<SqlParameter>();
            parameters.Add(new SqlParameter("@AD_Client_ID", ctx.GetAD_Client_ID()));
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
                        warehouseName = Util.GetValueOfString(dr["Warehouse_Name"]),
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
                INNER JOIN C_OrderLine ol ON ol.C_Order_ID=o.C_Order_ID
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

            List<NewGRNLineInput> inputs = string.IsNullOrWhiteSpace(linesJson)
                ? new List<NewGRNLineInput>()
                : JsonConvert.DeserializeObject<List<NewGRNLineInput>>(linesJson);
            if (inputs == null)
            {
                inputs = new List<NewGRNLineInput>();
            }
            Dictionary<int, decimal> qtyByLine = new Dictionary<int, decimal>();

            foreach (NewGRNLineInput input in inputs)
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

            Dictionary<int, NewGRNLineInfo> openLines = GetOpenLineInfo(ctx, poId, qtyByLine.Keys);

            if (openLines.Count != qtyByLine.Count)
            {
                return Fail("One or more selected PO lines are no longer open.");
            }

            foreach (KeyValuePair<int, decimal> selectedLine in qtyByLine)
            {
                NewGRNLineInfo lineInfo = openLines[selectedLine.Key];
                if (selectedLine.Value > lineInfo.OpenQty)
                {
                    return Fail("Received quantity cannot be greater than open quantity.");
                }
            }

            Trx trx = null;

            try
            {
                trx = Trx.Get("VAS_NewGRN" + DateTime.Now.Ticks);

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

        private Dictionary<int, NewGRNLineInfo> GetOpenLineInfo(Ctx ctx, int poId, IEnumerable<int> lineIds)
        {
            Dictionary<int, NewGRNLineInfo> lines = new Dictionary<int, NewGRNLineInfo>();
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
                INNER JOIN C_OrderLine ol ON ol.C_Order_ID=o.C_Order_ID
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
                    lines[lineId] = new NewGRNLineInfo
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

        private int GetDocTypeId(int orgId, int clientId)
        {
            string sql = @"
                SELECT C_DocType_ID
                FROM C_DocType
                WHERE DocBaseType='MMR'
                  AND AD_Client_ID=@AD_Client_ID
                  AND IsActive='Y'
                  AND AD_Org_ID IN (0, @AD_Org_ID)
                  AND IsSOTrx='N'
                  AND IsReturnTrx='N'
                ORDER BY AD_Org_ID DESC";

            List<SqlParameter> parameters = new List<SqlParameter>();
            parameters.Add(new SqlParameter("@AD_Client_ID", clientId));
            parameters.Add(new SqlParameter("@AD_Org_ID", orgId));

            return Util.GetValueOfInt(DB.ExecuteScalar(sql, parameters.ToArray(), null));
        }

        private int GetDefaultLocatorId(int warehouseId, Trx trx)
        {
            if (warehouseId <= 0) { return 0; }

            string sql = @"
                SELECT M_Locator_ID
                FROM M_Locator
                WHERE IsActive='Y'
                  AND M_Warehouse_ID=@M_Warehouse_ID
                ORDER BY IsDefault DESC";

            SqlParameter[] parameters = { new SqlParameter("@M_Warehouse_ID", warehouseId) };
            return Util.GetValueOfInt(DB.ExecuteScalar(sql, parameters, trx));
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

        private sealed class NewGRNLineInput
        {
            [JsonProperty("poLineId")]
            public int PoLineId { get; set; }

            [JsonProperty("receivedQty")]
            public decimal ReceivedQty { get; set; }
        }

        private sealed class NewGRNLineInfo
        {
            public int PoLineId { get; set; }
            public int WarehouseId { get; set; }
            public decimal OpenQty { get; set; }
        }
    }
}
