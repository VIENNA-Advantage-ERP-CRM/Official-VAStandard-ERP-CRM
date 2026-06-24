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
    /// Module Name : Receipt Aging (Material Receipt / GRN dashboard)
    /// Purpose     : 3x2 age-bar chart of completed vendor receipts that are
    ///               still waiting because no completed/processed put-away
    ///               confirmation exists. Age is calculated in JavaScript from
    ///               MovementDate, as required by the widget documentation.
    /// Chronological development:
    ///   &lt;EmpCode&gt;   2026-06-20 Created
    /// </summary>
    public class VAS_087_ReceiptAgingWidgetController : Controller
    {
        /// <summary>
        /// One page of not-yet-stored vendor receipts, oldest first.
        /// </summary>
        /// <param name="pageNo">1-based page number.</param>
        /// <param name="pageSize">Rows per page.</param>
        /// <returns>JSON { rows[], pageNo, pageSize, totalRecords, totalPages, oldestReceivedOn }.</returns>
        [AjaxAuthorizeAttribute]
        [AjaxSessionFilterAttribute]
        public JsonResult GetReceiptAging(int pageNo = 1, int pageSize = 4)
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
            if (pageSize <= 0) { pageSize = 4; }
            if (pageSize > 4) { pageSize = 4; }

            int offset = (pageNo - 1) * pageSize;
            bool hasLineConfirmTable = HasTable("M_InOutLineConfirm");
            bool hasQualityCheckColumn = hasLineConfirmTable && HasColumn("M_InOutLineConfirm", "VA010_QualCheckMark");

            string lineConfirmJoin = hasLineConfirmTable
                ? "LEFT OUTER JOIN M_InOutLineConfirm LineConfirm ON (LineConfirm.M_InOutLine_ID=InOutLine.M_InOutLine_ID AND LineConfirm.IsActive='Y')"
                : "";
            string qualityCheckSql = hasQualityCheckColumn
                ? "MAX(CASE WHEN COALESCE(LineConfirm.VA010_QualCheckMark, 'N')='Y' THEN 1 ELSE 0 END)"
                : "0";
            string differenceSql = hasLineConfirmTable
                ? "SUM(COALESCE(LineConfirm.DifferenceQty, 0))"
                : "0";

            string headerSql = @"
                SELECT InOut.M_InOut_ID AS Receipt_Id,
                       InOut.DocumentNo AS GRN_No,
                       BPartner.Name AS Supplier,
                       COALESCE(PurchaseOrder.DocumentNo, N'-') AS Linked_PO_No,
                       InOut.MovementDate AS Received_On
                FROM M_InOut InOut
                INNER JOIN C_BPartner BPartner ON (BPartner.C_BPartner_ID=InOut.C_BPartner_ID AND BPartner.IsActive='Y')
                LEFT OUTER JOIN C_Order PurchaseOrder ON (PurchaseOrder.C_Order_ID=InOut.C_Order_ID AND PurchaseOrder.IsActive='Y')
                WHERE InOut.IsActive='Y'
                  AND InOut.IsSOTrx='N'
                  AND InOut.MovementType='V+'
                  AND InOut.DocStatus='CO'
                  AND InOut.AD_Client_ID=@AD_Client_ID
                  AND NOT EXISTS (
                      SELECT 1
                      FROM M_InOutConfirm Confirm
                      WHERE Confirm.M_InOut_ID=InOut.M_InOut_ID
                        AND Confirm.IsActive='Y'
                        AND (Confirm.Processed='Y' OR Confirm.DocStatus='CO')
                  )";

            headerSql = MRole.GetDefault(ctx).AddAccessSQL(
                headerSql,
                "InOut",
                MRole.SQL_FULLYQUALIFIED,
                MRole.SQL_RO
            );

            string sql = @"
                SELECT AgingData.Receipt_Id,
                       AgingData.GRN_No,
                       AgingData.Supplier,
                       AgingData.Linked_PO_No,
                       AgingData.First_Item_Name,
                       AgingData.Line_Count,
                       AgingData.Total_Qty,
                       AgingData.Uom,
                       AgingData.Received_On,
                       AgingData.Has_Quality_Check,
                       AgingData.Difference_Qty,
                       AgingData.TotalRecords,
                       AgingData.Oldest_Received_On
                FROM (
                    SELECT HeaderData.Receipt_Id,
                           HeaderData.GRN_No,
                           HeaderData.Supplier,
                           HeaderData.Linked_PO_No,
                           MIN(Product.Name) AS First_Item_Name,
                           COUNT(InOutLine.M_InOutLine_ID) AS Line_Count,
                           SUM(COALESCE(InOutLine.MovementQty, 0)) AS Total_Qty,
                           MIN(COALESCE(UOM.UOMSymbol, UOM.Name)) AS Uom,
                           HeaderData.Received_On,
                           " + qualityCheckSql + @" AS Has_Quality_Check,
                           " + differenceSql + @" AS Difference_Qty,
                           COUNT(1) OVER () AS TotalRecords,
                           MIN(HeaderData.Received_On) OVER () AS Oldest_Received_On
                    FROM (
                        " + headerSql + @"
                    ) HeaderData
                    INNER JOIN M_InOutLine InOutLine ON (InOutLine.M_InOut_ID=HeaderData.Receipt_Id AND InOutLine.IsActive='Y' AND InOutLine.AD_Client_ID=@Line_AD_Client_ID)
                    INNER JOIN M_Product Product ON (Product.M_Product_ID=InOutLine.M_Product_ID AND Product.IsActive='Y')
                    LEFT OUTER JOIN C_UOM UOM ON (UOM.C_UOM_ID=InOutLine.C_UOM_ID AND UOM.IsActive='Y')
                    " + lineConfirmJoin + @"
                    GROUP BY HeaderData.Receipt_Id,
                             HeaderData.GRN_No,
                             HeaderData.Supplier,
                             HeaderData.Linked_PO_No,
                             HeaderData.Received_On
                ) AgingData
                ORDER BY AgingData.Received_On ASC, AgingData.GRN_No ASC
                OFFSET @Offset ROWS FETCH NEXT @PageSize ROWS ONLY";

            List<SqlParameter> parameters = new List<SqlParameter>();
            parameters.Add(new SqlParameter("@AD_Client_ID", ctx.GetAD_Client_ID()));
            parameters.Add(new SqlParameter("@Line_AD_Client_ID", ctx.GetAD_Client_ID()));
            parameters.Add(new SqlParameter("@Offset", offset));
            parameters.Add(new SqlParameter("@PageSize", pageSize));

            List<object> rows = new List<object>();
            int totalRecords = 0;
            string oldestReceivedOnValue = "";
            IDataReader dr = null;

            try
            {
                dr = DB.ExecuteReader(sql, parameters.ToArray());

                while (dr != null && dr.Read())
                {
                    totalRecords = Util.GetValueOfInt(dr["TotalRecords"]);

                    DateTime? receivedOn = Util.GetValueOfDateTime(dr["Received_On"]);
                    DateTime? oldestReceivedOn = Util.GetValueOfDateTime(dr["Oldest_Received_On"]);
                    if (oldestReceivedOn.HasValue)
                    {
                        oldestReceivedOnValue = oldestReceivedOn.Value.ToString("yyyy-MM-ddTHH:mm:ss", CultureInfo.InvariantCulture);
                    }

                    int hasQualityCheck = Util.GetValueOfInt(dr["Has_Quality_Check"]);
                    decimal differenceQty = Util.GetValueOfDecimal(dr["Difference_Qty"]);

                    rows.Add(new
                    {
                        receiptId = Util.GetValueOfInt(dr["Receipt_Id"]),
                        grnNo = Util.GetValueOfString(dr["GRN_No"]),
                        linkedPoNo = Util.GetValueOfString(dr["Linked_PO_No"]),
                        supplier = Util.GetValueOfString(dr["Supplier"]),
                        firstItemName = Util.GetValueOfString(dr["First_Item_Name"]),
                        lineCount = Util.GetValueOfInt(dr["Line_Count"]),
                        totalQty = Util.GetValueOfDecimal(dr["Total_Qty"]),
                        uom = Util.GetValueOfString(dr["Uom"]),
                        receivedOn = receivedOn.HasValue ? receivedOn.Value.ToString("yyyy-MM-ddTHH:mm:ss", CultureInfo.InvariantCulture) : "",
                        hasQualityCheck = hasQualityCheck,
                        differenceQty = differenceQty,
                        statusCode = GetStatusCode(hasQualityCheck, differenceQty)
                    });
                }

                return Json(JsonConvert.SerializeObject(new
                {
                    rows = rows,
                    pageNo = pageNo,
                    pageSize = pageSize,
                    totalRecords = totalRecords,
                    totalPages = pageSize == 0 ? 0 : Convert.ToInt32(Math.Ceiling((decimal)totalRecords / pageSize)),
                    oldestReceivedOn = oldestReceivedOnValue
                }), JsonRequestBehavior.AllowGet);
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
        /// Detail lines for one aged receipt.
        /// </summary>
        /// <param name="receiptId">M_InOut_ID of the selected receipt.</param>
        /// <returns>JSON { rows[] }.</returns>
        [AjaxAuthorizeAttribute]
        [AjaxSessionFilterAttribute]
        public JsonResult GetReceiptAgingLines(int receiptId = 0)
        {
            if (Session["ctx"] == null)
            {
                return Json(new
                {
                    error = Msg.GetMsg(Env.GetCtx(), "SessionExpired") ?? "Session Expired"
                }, JsonRequestBehavior.AllowGet);
            }

            Ctx ctx = Session["ctx"] as Ctx;
            bool hasLineConfirmTable = HasTable("M_InOutLineConfirm");
            bool hasQualityCheckColumn = hasLineConfirmTable && HasColumn("M_InOutLineConfirm", "VA010_QualCheckMark");

            string lineConfirmJoin = hasLineConfirmTable
                ? "LEFT OUTER JOIN M_InOutLineConfirm LineConfirm ON (LineConfirm.M_InOutLine_ID=InOutLine.M_InOutLine_ID AND LineConfirm.IsActive='Y')"
                : "";
            string locatorJoin = hasLineConfirmTable
                ? "LEFT OUTER JOIN M_Locator Locator ON (Locator.M_Locator_ID=LineConfirm.M_Locator_ID AND Locator.IsActive='Y')"
                : "";
            string locatorSql = hasLineConfirmTable
                ? "COALESCE(Locator.Value, N'-')"
                : "N'-'";
            string statusSql = "'Unloading'";

            if (hasLineConfirmTable && hasQualityCheckColumn)
            {
                statusSql = @"
                    CASE
                        WHEN COALESCE(LineConfirm.VA010_QualCheckMark, 'N')='Y' THEN 'Quality check'
                        WHEN COALESCE(LineConfirm.DifferenceQty, 0) <> 0 THEN 'Count mismatch'
                        ELSE 'Unloading'
                    END";
            }
            else if (hasLineConfirmTable)
            {
                statusSql = @"
                    CASE
                        WHEN COALESCE(LineConfirm.DifferenceQty, 0) <> 0 THEN 'Count mismatch'
                        ELSE 'Unloading'
                    END";
            }

            string linesSql = @"
                SELECT Product.Name AS Item_Name,
                       COALESCE(InOutLine.MovementQty, 0) AS Received_Qty,
                       COALESCE(UOM.UOMSymbol, UOM.Name) AS Uom,
                       " + locatorSql + @" AS Locator_Code,
                       " + statusSql + @" AS Line_Status
                FROM M_InOut InOut
                INNER JOIN M_InOutLine InOutLine ON (InOutLine.M_InOut_ID=InOut.M_InOut_ID AND InOutLine.IsActive='Y')
                INNER JOIN M_Product Product ON (Product.M_Product_ID=InOutLine.M_Product_ID AND Product.IsActive='Y')
                LEFT OUTER JOIN C_UOM UOM ON (UOM.C_UOM_ID=InOutLine.C_UOM_ID AND UOM.IsActive='Y')
                " + lineConfirmJoin + @"
                " + locatorJoin + @"
                WHERE InOut.IsActive='Y'
                  AND InOut.IsSOTrx='N'
                  AND InOut.MovementType='V+'
                  AND InOut.M_InOut_ID=@ReceiptId
                  AND InOut.AD_Client_ID=@AD_Client_ID";

            linesSql = MRole.GetDefault(ctx).AddAccessSQL(
                linesSql,
                "InOut",
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
                        receivedQty = Util.GetValueOfDecimal(dr["Received_Qty"]),
                        uom = Util.GetValueOfString(dr["Uom"]),
                        locatorCode = Util.GetValueOfString(dr["Locator_Code"]),
                        status = Util.GetValueOfString(dr["Line_Status"])
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

        private string GetStatusCode(int hasQualityCheck, decimal differenceQty)
        {
            if (hasQualityCheck == 1) { return "quality"; }
            if (differenceQty != decimal.Zero) { return "mismatch"; }
            return "unloading";
        }

        private bool HasTable(string tableName)
        {
            string sql;
            if (DB.IsPostgreSQL())
            {
                sql = @"
                    SELECT COUNT(1)
                    FROM information_schema.tables
                    WHERE UPPER(table_name)=UPPER(@TableName)";
            }
            else
            {
                sql = @"
                    SELECT COUNT(1)
                    FROM USER_TABLES
                    WHERE TABLE_NAME=UPPER(@TableName)";
            }

            SqlParameter[] parameters = { new SqlParameter("@TableName", tableName) };

            try
            {
                return Util.GetValueOfInt(DB.ExecuteScalar(sql, parameters, null)) > 0;
            }
            catch
            {
                return false;
            }
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
    }
}
