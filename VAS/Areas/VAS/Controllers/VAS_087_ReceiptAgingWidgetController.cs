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
    /// Purpose     : 3x2 age-bar chart of vendor receipts (GRNs) that are created
    ///               but not yet completed (DocStatus Drafted / In Progress), per
    ///               VA review #18. Age is calculated in JavaScript from
    ///               MovementDate.
    /// Chronological development:
    ///   &lt;EmpCode&gt;   2026-06-20 Created
    ///   VAI154      2026-07-06 Review #18: show only DR/IP receipts (was CO +
    ///                          no completed put-away confirmation)
    /// </summary>
    public class VAS_087_ReceiptAgingWidgetController : Controller
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
            if (pageSize > 50) { pageSize = 50; }

            int offset = (pageNo - 1) * pageSize;

            string headerSql = @"
                SELECT InOut.M_InOut_ID AS Receipt_Id,
                       InOut.DocumentNo AS GRN_No,
                       BPartner.Name AS Supplier,
                       COALESCE(PurchaseOrder.DocumentNo, " + NLiteral("-") + @") AS Linked_PO_No,
                       InOut.MovementDate AS Received_On,
                       InOut.DocStatus AS Doc_Status,
                       Warehouse.Name AS Warehouse_Name
                FROM M_InOut InOut
                INNER JOIN C_BPartner BPartner ON (BPartner.C_BPartner_ID=InOut.C_BPartner_ID AND BPartner.IsActive='Y')
                LEFT OUTER JOIN M_Warehouse Warehouse ON (Warehouse.M_Warehouse_ID=InOut.M_Warehouse_ID AND Warehouse.IsActive='Y')
                LEFT OUTER JOIN C_Order PurchaseOrder ON (PurchaseOrder.C_Order_ID=InOut.C_Order_ID AND PurchaseOrder.IsActive='Y')
                WHERE InOut.IsActive='Y'
                  AND InOut.IsSOTrx='N'
                  AND InOut.MovementType='V+'
                  AND InOut.DocStatus IN ('DR','IP')
                  AND InOut.AD_Client_ID=@AD_Client_ID";

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
                       AgingData.Doc_Status,
                       AgingData.Warehouse_Name,
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
                           HeaderData.Doc_Status,
                           HeaderData.Warehouse_Name,
                           COUNT(1) OVER () AS TotalRecords,
                           MIN(HeaderData.Received_On) OVER () AS Oldest_Received_On
                    FROM (
                        " + headerSql + @"
                    ) HeaderData
                    INNER JOIN M_InOutLine InOutLine ON (InOutLine.M_InOut_ID=HeaderData.Receipt_Id AND InOutLine.IsActive='Y' AND InOutLine.AD_Client_ID=@Line_AD_Client_ID)
                    INNER JOIN M_Product Product ON (Product.M_Product_ID=InOutLine.M_Product_ID AND Product.IsActive='Y')
                    LEFT OUTER JOIN C_UOM UOM ON (UOM.C_UOM_ID=InOutLine.C_UOM_ID AND UOM.IsActive='Y')
                    GROUP BY HeaderData.Receipt_Id,
                             HeaderData.GRN_No,
                             HeaderData.Supplier,
                             HeaderData.Linked_PO_No,
                             HeaderData.Received_On,
                             HeaderData.Doc_Status,
                             HeaderData.Warehouse_Name
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

                    string docStatus = Util.GetValueOfString(dr["Doc_Status"]);

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
                        warehouse = Util.GetValueOfString(dr["Warehouse_Name"]),
                        statusCode = docStatus,
                        statusText = GetDocStatusName(ctx, docStatus)
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

            string lineConfirmJoin = hasLineConfirmTable
                ? "LEFT OUTER JOIN M_InOutLineConfirm LineConfirm ON (LineConfirm.M_InOutLine_ID=InOutLine.M_InOutLine_ID AND LineConfirm.IsActive='Y')"
                : "";
            // The locator was being read from M_InOutLineConfirm - a CONFIRMATION record that in
            // practice does not exist (DB 1: 0 confirmation rows against 303 receipt lines), so the
            // Locator column could only ever render "-". M_InOutLine carries M_Locator_ID directly
            // and it is populated on 303/303 lines, so that is the real source. The confirmation
            // locator is kept only as a secondary fallback where the table exists.
            string locatorJoin =
                "LEFT OUTER JOIN M_Locator Locator ON (Locator.M_Locator_ID=InOutLine.M_Locator_ID AND Locator.IsActive='Y')"
                + (hasLineConfirmTable
                    ? " LEFT OUTER JOIN M_Locator LocatorConfirm ON (LocatorConfirm.M_Locator_ID=LineConfirm.M_Locator_ID AND LocatorConfirm.IsActive='Y')"
                    : "");
            // Selected raw - no COALESCE against a string literal. Locator.Value is a national
            // character set column and mixing it with a literal raises ORA-12704. The JS already
            // renders "-" for an empty value.
            string locatorSql = hasLineConfirmTable
                ? "COALESCE(Locator.Value, LocatorConfirm.Value)"
                : "Locator.Value";

            string linesSql = @"
                SELECT Product.Name AS Item_Name,
                       COALESCE(InOutLine.MovementQty, 0) AS Received_Qty,
                       COALESCE(UOM.UOMSymbol, UOM.Name) AS Uom,
                       " + locatorSql + @" AS Locator_Code,
                       InOut.DocStatus AS Doc_Status
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
                        status = GetDocStatusName(ctx, Util.GetValueOfString(dr["Doc_Status"]))
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

        /// <summary>
        /// Resolves a receipt's document-status code (M_InOut.DocStatus) to its
        /// human-readable name from the system's own DocStatus reference list
        /// (AD_Reference_ID 131) - the same source MInOut.GetDocStatusName uses.
        /// Falls back to the raw code if the list name cannot be resolved.
        /// </summary>
        /// <param name="ctx">Session context.</param>
        /// <param name="docStatus">Raw DocStatus code (e.g. CO, DR, IP).</param>
        /// <returns>The system status name, or the raw code as a fallback.</returns>
        private string GetDocStatusName(Ctx ctx, string docStatus)
        {
            if (string.IsNullOrEmpty(docStatus)) { return ""; }

            try
            {
                string name = MRefList.GetListName(ctx, 131, docStatus);
                return string.IsNullOrEmpty(name) ? docStatus : name;
            }
            catch
            {
                return docStatus;
            }
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
    }
}
