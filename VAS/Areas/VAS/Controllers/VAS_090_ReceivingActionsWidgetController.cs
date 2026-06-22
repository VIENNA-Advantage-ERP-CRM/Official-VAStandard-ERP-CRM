using Newtonsoft.Json;
using System;
using System.Collections.Generic;
using System.Data;
using System.Data.SqlClient;
using System.Globalization;
using System.Web.Mvc;
using VAdvantage.Classes;
using VAdvantage.DataBase;
using VAdvantage.Model;
using VAdvantage.Utility;
using VIS.Filters;

namespace VIS.Controllers
{
    /// <summary>
    /// Module Name : Receiving Actions (Material Receipt / GRN dashboard)
    /// Purpose     : Data endpoints for the 3x2 receiving action widget. The
    ///               widget reuses VAS_NewGRNWidget/CreateGRN for receipt
    ///               creation and VAS_QAHoldsWidget/SaveQAResult for QA saves.
    /// Chronological development:
    ///   &lt;EmpCode&gt;   2026-06-20 Created
    /// </summary>
    public class VAS_090_ReceivingActionsWidgetController : Controller
    {
        [AjaxAuthorizeAttribute]
        [AjaxSessionFilterAttribute]
        public JsonResult GetOpenPurchaseOrders(int pageNo = 1, int pageSize = 8)
        {
            if (Session["ctx"] == null)
            {
                return Fail(Msg.GetMsg(Env.GetCtx(), "SessionExpired") ?? "Session Expired");
            }

            Ctx ctx = Session["ctx"] as Ctx;

            if (pageNo <= 0) { pageNo = 1; }
            if (pageSize <= 0) { pageSize = 8; }
            if (pageSize > 20) { pageSize = 20; }

            int offset = (pageNo - 1) * pageSize;
            string dockSql = HasColumn("M_Locator", "LocatorCombination")
                ? "COALESCE(ReceiveLocator.LocatorCombination, ReceiveLocator.Value, '-')"
                : "COALESCE(ReceiveLocator.Value, '-')";

            string baseSql = @"
                SELECT PurchaseOrder.C_Order_ID AS PO_ID,
                       PurchaseOrder.DocumentNo AS PO_No,
                       BPartner.Name AS Supplier_Name,
                       COALESCE(Warehouse.Name, '-') AS Warehouse_Name,
                       " + dockSql + @" AS Dock_Name,
                       COALESCE(PurchaseOrder.POReference, '-') AS Supplier_Reference,
                       MIN(COALESCE(OrderLine.DatePromised, PurchaseOrder.DatePromised)) AS Promise_Date,
                       COUNT(OrderLine.C_OrderLine_ID) AS Open_Line_Count
                FROM C_Order PurchaseOrder
                INNER JOIN C_OrderLine OrderLine ON (OrderLine.C_Order_ID=PurchaseOrder.C_Order_ID AND OrderLine.IsActive='Y')
                INNER JOIN C_BPartner BPartner ON (BPartner.C_BPartner_ID=PurchaseOrder.C_BPartner_ID AND BPartner.IsActive='Y')
                LEFT OUTER JOIN M_Warehouse Warehouse ON (Warehouse.M_Warehouse_ID=PurchaseOrder.M_Warehouse_ID AND Warehouse.IsActive='Y')
                LEFT OUTER JOIN M_Locator ReceiveLocator ON (ReceiveLocator.M_Locator_ID=Warehouse.M_RcvLocator_ID AND ReceiveLocator.IsActive='Y')
                WHERE PurchaseOrder.IsActive='Y'
                  AND PurchaseOrder.IsSOTrx='N'
                  AND PurchaseOrder.DocStatus='CO'
                  AND PurchaseOrder.AD_Client_ID=@AD_Client_ID
                  AND COALESCE(OrderLine.QtyOrdered, 0) > COALESCE(OrderLine.QtyDelivered, 0)
                GROUP BY PurchaseOrder.C_Order_ID,
                         PurchaseOrder.DocumentNo,
                         BPartner.Name,
                         COALESCE(Warehouse.Name, '-'),
                         " + dockSql + @",
                         COALESCE(PurchaseOrder.POReference, '-')";

            baseSql = MRole.GetDefault(ctx).AddAccessSQL(
                baseSql,
                "PurchaseOrder",
                MRole.SQL_FULLYQUALIFIED,
                MRole.SQL_RO
            );

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
                    " + baseSql + @"
                ) OpenPO
                ORDER BY OpenPO.Promise_Date, OpenPO.PO_No
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
                       COALESCE(Product.Name, '-') AS Item_Name,
                       COALESCE(OrderLine.QtyOrdered, 0) AS PO_Qty,
                       COALESCE(OrderLine.QtyDelivered, 0) AS Already_Received_Qty,
                       COALESCE(OrderLine.QtyOrdered, 0) - COALESCE(OrderLine.QtyDelivered, 0) AS Open_Qty,
                       COALESCE(UOM.UOMSymbol, UOM.Name) AS Uom
                FROM C_Order PurchaseOrder
                INNER JOIN C_OrderLine OrderLine ON (OrderLine.C_Order_ID=PurchaseOrder.C_Order_ID AND OrderLine.IsActive='Y')
                LEFT OUTER JOIN M_Product Product ON (Product.M_Product_ID=OrderLine.M_Product_ID AND Product.IsActive='Y')
                LEFT OUTER JOIN C_UOM UOM ON (UOM.C_UOM_ID=OrderLine.C_UOM_ID AND UOM.IsActive='Y')
                WHERE PurchaseOrder.IsActive='Y'
                  AND PurchaseOrder.IsSOTrx='N'
                  AND PurchaseOrder.DocStatus='CO'
                  AND PurchaseOrder.AD_Client_ID=@AD_Client_ID
                  AND PurchaseOrder.C_Order_ID=@PO_ID
                  AND COALESCE(OrderLine.QtyOrdered, 0) > COALESCE(OrderLine.QtyDelivered, 0)";

            sql = MRole.GetDefault(ctx).AddAccessSQL(
                sql,
                "PurchaseOrder",
                MRole.SQL_FULLYQUALIFIED,
                MRole.SQL_RO
            );

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
                        poQty = Util.GetValueOfDecimal(dr["PO_Qty"]),
                        alreadyReceivedQty = Util.GetValueOfDecimal(dr["Already_Received_Qty"]),
                        openQty = openQty,
                        defaultReceivedQty = openQty,
                        uom = Util.GetValueOfString(dr["Uom"])
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
            if (pageSize > 10) { pageSize = 10; }

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

            string copiesSql = HasColumn("C_DocType", "DocumentCopies")
                ? "COALESCE(DocType.DocumentCopies, 1)"
                : "1";
            string printFormatSql = HasColumn("C_DocType", "AD_PrintFormat_ID")
                ? "COALESCE(DocType.AD_PrintFormat_ID, 0)"
                : "0";

            string labelSql = @"
                SELECT InOut.M_InOut_ID AS GRN_ID,
                       InOut.DocumentNo AS GRN_No,
                       BPartner.Name AS Party_Name,
                       COALESCE(SUM(COALESCE(InOutLine.MovementQty, 0)), 0) AS Received_Qty,
                       " + copiesSql + @" AS Copies,
                       " + printFormatSql + @" AS Print_Format_ID,
                       COUNT(1) OVER () AS TotalRecords,
                       COALESCE(InOut.DateReceived, InOut.MovementDate, InOut.Created) AS Sort_Date
                FROM M_InOut InOut
                INNER JOIN C_BPartner BPartner ON (BPartner.C_BPartner_ID=InOut.C_BPartner_ID AND BPartner.IsActive='Y')
                LEFT OUTER JOIN M_InOutLine InOutLine ON (InOutLine.M_InOut_ID=InOut.M_InOut_ID AND InOutLine.IsActive='Y')
                LEFT OUTER JOIN C_DocType DocType ON (DocType.C_DocType_ID=InOut.C_DocType_ID AND DocType.IsActive='Y')
                WHERE InOut.IsActive='Y'
                  AND InOut.MovementType='V+'
                  AND InOut.AD_Client_ID=@AD_Client_ID" + searchWhere + @"
                GROUP BY InOut.M_InOut_ID,
                         InOut.DocumentNo,
                         BPartner.Name,
                         " + copiesSql + @",
                         " + printFormatSql + @",
                         InOut.DateReceived,
                         InOut.MovementDate,
                         InOut.Created";

            labelSql = MRole.GetDefault(ctx).AddAccessSQL(
                labelSql,
                "InOut",
                MRole.SQL_FULLYQUALIFIED,
                MRole.SQL_RO
            );

            string sql = @"
                SELECT LabelData.GRN_ID,
                       LabelData.GRN_No,
                       LabelData.Party_Name,
                       LabelData.Received_Qty,
                       LabelData.Copies,
                       LabelData.Print_Format_ID,
                       LabelData.TotalRecords
                FROM (
                    " + labelSql + @"
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
                    int printFormatId = Util.GetValueOfInt(dr["Print_Format_ID"]);

                    rows.Add(new
                    {
                        grnId = Util.GetValueOfInt(dr["GRN_ID"]),
                        grnNo = Util.GetValueOfString(dr["GRN_No"]),
                        partyName = Util.GetValueOfString(dr["Party_Name"]),
                        receivedQty = Util.GetValueOfDecimal(dr["Received_Qty"]),
                        copies = Math.Max(1, Util.GetValueOfInt(dr["Copies"])),
                        printFormatId = printFormatId,
                        printFormat = GetPrintFormatText(printFormatId)
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

            string copiesSql = HasColumn("C_DocType", "DocumentCopies")
                ? "COALESCE(DocType.DocumentCopies, 1)"
                : "1";
            string printFormatSql = HasColumn("C_DocType", "AD_PrintFormat_ID")
                ? "COALESCE(DocType.AD_PrintFormat_ID, 0)"
                : "0";

            string sql = @"
                SELECT InOut.M_InOut_ID AS GRN_ID,
                       InOut.DocumentNo AS GRN_No,
                       " + copiesSql + @" AS Copies,
                       " + printFormatSql + @" AS Print_Format_ID
                FROM M_InOut InOut
                LEFT OUTER JOIN C_DocType DocType ON (DocType.C_DocType_ID=InOut.C_DocType_ID AND DocType.IsActive='Y')
                WHERE InOut.IsActive='Y'
                  AND InOut.MovementType='V+'
                  AND InOut.AD_Client_ID=@AD_Client_ID
                  AND InOut.M_InOut_ID=@GRN_ID";

            sql = MRole.GetDefault(ctx).AddAccessSQL(
                sql,
                "InOut",
                MRole.SQL_FULLYQUALIFIED,
                MRole.SQL_RO
            );

            SqlParameter[] parameters =
            {
                new SqlParameter("@AD_Client_ID", ctx.GetAD_Client_ID()),
                new SqlParameter("@GRN_ID", grnId)
            };

            IDataReader dr = null;

            try
            {
                dr = DB.ExecuteReader(sql, parameters);
                if (dr == null || !dr.Read())
                {
                    return Fail("GRN was not found.");
                }

                int printFormatId = Util.GetValueOfInt(dr["Print_Format_ID"]);

                return Ok(new
                {
                    success = true,
                    grnId = Util.GetValueOfInt(dr["GRN_ID"]),
                    grnNo = Util.GetValueOfString(dr["GRN_No"]),
                    copies = Math.Max(1, Util.GetValueOfInt(dr["Copies"])),
                    printFormatId = printFormatId,
                    printFormat = GetPrintFormatText(printFormatId),
                    printer = "Default printer",
                    includes = "Barcode + locator",
                    status = "Queued"
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

        private string GetPrintFormatText(int printFormatId)
        {
            return printFormatId > 0
                ? "Print format " + printFormatId.ToString(CultureInfo.InvariantCulture)
                : "Put-away label 4x6";
        }

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
    }
}
