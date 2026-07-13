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
    /// Module Name : Material Receipt Register (Material Receipt / GRN dashboard)
    /// Purpose     : 3x2 list of recent vendor goods receipts (M_InOut,
    ///               MovementType 'V+', IsSOTrx 'N', excluding RE/VO) with the
    ///               linked PO, supplier, customer/project, received date,
    ///               put-away date (latest processed M_InOutConfirm.Updated),
    ///               item count and total received quantity. Row click opens a
    ///               detail modal whose line table comes from GetReceiptLines.
    ///               Server-side paged via OFFSET/FETCH (Oracle 12c+ / PostgreSQL).
    ///               MRole is applied to the primary receipt table and to the
    ///               receipt-line detail query.
    /// Chronological development:
    ///   &lt;EmpCode&gt;   2026-06-18 Created
    /// </summary>
    public class VAS_081_MaterialReceiptRegisterWidgetController : Controller
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
        /// One page of the receipt register (header-level), newest first.
        /// Review #20: limited to one calendar month - the current month by
        /// default, or the year/month picked in the widget's top-right filter.
        /// </summary>
        /// <param name="pageNo">1-based page number.</param>
        /// <param name="pageSize">Rows per page (default 5).</param>
        /// <param name="year">Selected year (0 = current).</param>
        /// <param name="month">Selected month 1-12 (0 = current).</param>
        /// <returns>JSON { rows[], pageNo, pageSize, totalRecords, totalPages, year, month }.</returns>
        [AjaxAuthorizeAttribute]
        [AjaxSessionFilterAttribute]
        public JsonResult GetReceipts(int pageNo = 1, int pageSize = 5, int year = 0, int month = 0)
        {
            if (Session["ctx"] == null)
            {
                return Json(new
                {
                    error = Msg.GetMsg(Env.GetCtx(), "SessionExpired") ?? "Session Expired"
                }, JsonRequestBehavior.AllowGet);
            }

            Ctx ctx = Session["ctx"] as Ctx;

            if (pageNo <= 0) { pageNo = 1; }
            if (pageSize <= 0) { pageSize = 5; }
            if (pageSize > 50) { pageSize = 50; }
            int offset = (pageNo - 1) * pageSize;

            // Review #20: resolve the month window (defaults to the current month).
            DateTime today = DateTime.Today;
            if (year < 2000 || year > 2100) { year = today.Year; }
            if (month < 1 || month > 12) { month = today.Month; }
            DateTime monthStart = new DateTime(year, month, 1);
            DateTime monthEndExclusive = monthStart.AddMonths(1);

            string rawSql = @"
                SELECT InOut.M_InOut_ID,
                       InOut.DocumentNo,
                       PurchaseOrder.DocumentNo AS PO_DocumentNo,
                       BPartner.Name AS BPartner_Name,
                       COALESCE(Project.Name, " + NLiteral("-") + @") AS Project_Name,
                       Warehouse.Name AS Warehouse_Name,
                       SalesRep.Name AS SalesRep_Name,
                       InOut.MovementDate,
                       ConfirmationData.Put_Away_On,
                       InOutLine.M_InOutLine_ID,
                       InOutLine.MovementQty
                FROM M_InOut InOut
                INNER JOIN M_InOutLine InOutLine ON (InOutLine.M_InOut_ID=InOut.M_InOut_ID AND InOutLine.IsActive='Y')
                INNER JOIN C_BPartner BPartner ON (BPartner.C_BPartner_ID=InOut.C_BPartner_ID AND BPartner.IsActive='Y')
                LEFT OUTER JOIN C_Order PurchaseOrder ON (PurchaseOrder.C_Order_ID=InOut.C_Order_ID AND PurchaseOrder.IsActive='Y')
                LEFT OUTER JOIN C_Project Project ON (Project.C_Project_ID=InOut.C_Project_ID AND Project.IsActive='Y')
                LEFT OUTER JOIN M_Warehouse Warehouse ON (Warehouse.M_Warehouse_ID=InOut.M_Warehouse_ID AND Warehouse.IsActive='Y')
                LEFT OUTER JOIN AD_User SalesRep ON (SalesRep.AD_User_ID=InOut.SalesRep_ID AND SalesRep.IsActive='Y')
                LEFT OUTER JOIN (
                    SELECT Confirm.M_InOut_ID,
                           MAX(Confirm.Updated) AS Put_Away_On
                    FROM M_InOutConfirm Confirm
                    WHERE Confirm.IsActive='Y'
                      AND (Confirm.Processed='Y' OR Confirm.DocStatus='CO')
                    GROUP BY Confirm.M_InOut_ID
                ) ConfirmationData ON (ConfirmationData.M_InOut_ID=InOut.M_InOut_ID)
                WHERE InOut.IsActive='Y'
                  AND InOut.IsSOTrx='N'
                  AND InOut.MovementType='V+'
                  AND InOut.DocStatus NOT IN ('RE', 'VO')
                  AND InOut.AD_Client_ID=@AD_Client_ID
                  AND InOut.MovementDate>=@Month_From
                  AND InOut.MovementDate<@Month_To";

            rawSql = MRole.GetDefault(ctx).AddAccessSQL(
                rawSql,
                "InOut",
                MRole.SQL_FULLYQUALIFIED,
                MRole.SQL_RO
            );

            string sql = @"
                SELECT RegisterData.Receipt_Id,
                       RegisterData.Receipt_No,
                       RegisterData.Linked_PO_No,
                       RegisterData.Supplier,
                       RegisterData.Customer_Project,
                       RegisterData.Warehouse_Name,
                       RegisterData.SalesRep_Name,
                       RegisterData.Received_On,
                       RegisterData.Put_Away_On,
                       RegisterData.Item_Count,
                       RegisterData.Total_Received_Qty,
                       COUNT(1) OVER () AS TotalRecords
                FROM (
                    SELECT RawData.M_InOut_ID AS Receipt_Id,
                           RawData.DocumentNo AS Receipt_No,
                           RawData.PO_DocumentNo AS Linked_PO_No,
                           RawData.BPartner_Name AS Supplier,
                           RawData.Project_Name AS Customer_Project,
                           RawData.Warehouse_Name,
                           RawData.SalesRep_Name,
                           RawData.MovementDate AS Received_On,
                           RawData.Put_Away_On,
                           COUNT(RawData.M_InOutLine_ID) AS Item_Count,
                           SUM(COALESCE(RawData.MovementQty, 0)) AS Total_Received_Qty
                    FROM (
                        " + rawSql + @"
                    ) RawData
                    GROUP BY RawData.M_InOut_ID,
                             RawData.DocumentNo,
                             RawData.PO_DocumentNo,
                             RawData.BPartner_Name,
                             RawData.Project_Name,
                             RawData.Warehouse_Name,
                             RawData.SalesRep_Name,
                             RawData.MovementDate,
                             RawData.Put_Away_On
                ) RegisterData
                ORDER BY RegisterData.Received_On DESC, RegisterData.Receipt_No DESC
                OFFSET @Offset ROWS FETCH NEXT @PageSize ROWS ONLY";

            // Parameter order matches placeholder appearance (positional binding).
            List<SqlParameter> parameters = new List<SqlParameter>();
            parameters.Add(new SqlParameter("@AD_Client_ID", ctx.GetAD_Client_ID()));
            parameters.Add(new SqlParameter("@Month_From", SqlDbType.DateTime) { Value = monthStart });
            parameters.Add(new SqlParameter("@Month_To", SqlDbType.DateTime) { Value = monthEndExclusive });
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

                    DateTime? receivedOn = Util.GetValueOfDateTime(dr["Received_On"]);
                    DateTime? putAwayOn = Util.GetValueOfDateTime(dr["Put_Away_On"]);

                    rows.Add(new
                    {
                        receiptId = Util.GetValueOfInt(dr["Receipt_Id"]),
                        receiptNo = Util.GetValueOfString(dr["Receipt_No"]),
                        linkedPoNo = Util.GetValueOfString(dr["Linked_PO_No"]),
                        supplier = Util.GetValueOfString(dr["Supplier"]),
                        customerProject = Util.GetValueOfString(dr["Customer_Project"]),
                        warehouseName = Util.GetValueOfString(dr["Warehouse_Name"]),
                        salesRepName = Util.GetValueOfString(dr["SalesRep_Name"]),
                        receivedOn = receivedOn.HasValue ? receivedOn.Value.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture) : "",
                        putAwayOn = putAwayOn.HasValue ? putAwayOn.Value.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture) : "",
                        itemCount = Util.GetValueOfInt(dr["Item_Count"]),
                        totalReceivedQty = Util.GetValueOfDecimal(dr["Total_Received_Qty"])
                    });
                }

                var result = new
                {
                    rows = rows,
                    pageNo = pageNo,
                    pageSize = pageSize,
                    totalRecords = totalRecords,
                    totalPages = pageSize == 0 ? 0 : Convert.ToInt32(Math.Ceiling((decimal)totalRecords / pageSize)),
                    year = year,
                    month = month
                };

                return Json(JsonConvert.SerializeObject(result), JsonRequestBehavior.AllowGet);
            }
            catch (Exception ex)
            {
                return Json(new { error = ex.Message }, JsonRequestBehavior.AllowGet);
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
        /// Receipt detail lines (Item, PO Qty, Received, UoM) for one receipt.
        /// </summary>
        /// <param name="receiptId">M_InOut_ID of the selected receipt.</param>
        /// <returns>JSON { rows[] }.</returns>
        [AjaxAuthorizeAttribute]
        [AjaxSessionFilterAttribute]
        public JsonResult GetReceiptLines(int receiptId = 0)
        {
            if (Session["ctx"] == null)
            {
                return Json(new
                {
                    error = Msg.GetMsg(Env.GetCtx(), "SessionExpired") ?? "Session Expired"
                }, JsonRequestBehavior.AllowGet);
            }

            Ctx ctx = Session["ctx"] as Ctx;

            string linesSql = @"
                SELECT Product.Name AS Item_Name,
                       COALESCE(OrderLine.QtyOrdered, 0) AS PO_Qty,
                       COALESCE(InOutLine.MovementQty, 0) AS Received_Qty,
                       COALESCE(UOM.UOMSymbol, UOM.Name) AS Uom,
                       InOutLine.Line AS Line_No
                FROM M_InOutLine InOutLine
                INNER JOIN M_InOut InOut ON (InOut.M_InOut_ID=InOutLine.M_InOut_ID AND InOut.IsActive='Y')
                INNER JOIN M_Product Product ON (Product.M_Product_ID=InOutLine.M_Product_ID AND Product.IsActive='Y')
                LEFT OUTER JOIN C_OrderLine OrderLine ON (OrderLine.C_OrderLine_ID=InOutLine.C_OrderLine_ID AND OrderLine.IsActive='Y')
                LEFT OUTER JOIN C_UOM UOM ON (UOM.C_UOM_ID=InOutLine.C_UOM_ID AND UOM.IsActive='Y')
                WHERE InOutLine.IsActive='Y'
                  AND InOut.IsSOTrx='N'
                  AND InOut.MovementType='V+'
                  AND InOut.DocStatus NOT IN ('RE', 'VO')
                  AND InOutLine.M_InOut_ID=@ReceiptId
                  AND InOutLine.AD_Client_ID=@AD_Client_ID";

            linesSql = MRole.GetDefault(ctx).AddAccessSQL(
                linesSql,
                "InOutLine",
                MRole.SQL_FULLYQUALIFIED,
                MRole.SQL_RO
            );

            linesSql += @"
                ORDER BY InOutLine.Line";

            List<SqlParameter> parameters = new List<SqlParameter>();
            parameters.Add(new SqlParameter("@ReceiptId", receiptId));
            parameters.Add(new SqlParameter("@AD_Client_ID", ctx.GetAD_Client_ID()));

            List<object> rows = new List<object>();
            IDataReader dr = null;

            try
            {
                dr = DB.ExecuteReader(linesSql, parameters.ToArray());

                while (dr != null && dr.Read())
                {
                    rows.Add(new
                    {
                        itemName = Util.GetValueOfString(dr["Item_Name"]),
                        poQty = Util.GetValueOfDecimal(dr["PO_Qty"]),
                        receivedQty = Util.GetValueOfDecimal(dr["Received_Qty"]),
                        uom = Util.GetValueOfString(dr["Uom"])
                    });
                }

                return Json(JsonConvert.SerializeObject(new { rows = rows }), JsonRequestBehavior.AllowGet);
            }
            catch (Exception ex)
            {
                return Json(new { error = ex.Message }, JsonRequestBehavior.AllowGet);
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
    }
}
