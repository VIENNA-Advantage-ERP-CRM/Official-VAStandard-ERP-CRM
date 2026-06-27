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
    /// Module Name : Receipt Queue (Material Receipt / GRN dashboard)
    /// Purpose     : 6x2 read-only queue of vendor material receipts with
    ///               supplier, linked PO, received quantity, plain receipt
    ///               status, receiver and timestamp. Row click opens a detail
    ///               modal whose line table comes from GetReceiptQueueLines.
    ///               Server-side paged via OFFSET/FETCH. MRole is applied to
    ///               the primary fetched table (M_InOut) in the header query.
    /// Chronological development:
    ///   &lt;EmpCode&gt;   2026-06-18 Created
    /// </summary>
    public class VAS_088_ReceiptQueueWidgetController : Controller
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
        /// One page of the live receipt queue, newest first.
        /// </summary>
        /// <param name="pageNo">1-based page number.</param>
        /// <param name="pageSize">Rows per page.</param>
        /// <returns>JSON { rows[], pageNo, pageSize, totalRecords, totalPages }.</returns>
        [AjaxAuthorizeAttribute]
        [AjaxSessionFilterAttribute]
        public JsonResult GetReceiptQueue(int pageNo = 1, int pageSize = 5)
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
            if (pageSize > 5) { pageSize = 5; }

            int offset = (pageNo - 1) * pageSize;
            bool hasQualityCheckColumn = HasQualityCheckColumn();
            string qualityCheckSql = hasQualityCheckColumn
                ? "MAX(CASE WHEN COALESCE(LineConfirm.VA010_QualCheckMArk, 'N')='Y' THEN 1 ELSE 0 END)"
                : "0";

            string headerSql = @"
                SELECT InOut.M_InOut_ID AS GRN_ID,
                       InOut.DocumentNo AS GRN_No,
                       BPartner.Name AS Supplier_Name,
                       COALESCE(PurchaseOrder.DocumentNo, " + NLiteral("-") + @") AS PO_No,
                       InOut.DocStatus AS Doc_Status,
                       COALESCE(UserInfo.Name, " + NLiteral("-") + @") AS Received_By,
                       COALESCE(InOut.DateReceived, InOut.MovementDate, InOut.Created) AS Received_Time
                FROM M_InOut InOut
                INNER JOIN C_BPartner BPartner ON (BPartner.C_BPartner_ID=InOut.C_BPartner_ID AND BPartner.IsActive='Y')
                LEFT OUTER JOIN C_Order PurchaseOrder ON (PurchaseOrder.C_Order_ID=InOut.C_Order_ID AND PurchaseOrder.IsActive='Y')
                LEFT OUTER JOIN AD_User UserInfo ON (UserInfo.AD_User_ID=InOut.CreatedBy AND UserInfo.IsActive='Y')
                WHERE InOut.IsActive='Y'
                  AND InOut.MovementType='V+'
                  AND InOut.AD_Client_ID=@AD_Client_ID";

            headerSql = MRole.GetDefault(ctx).AddAccessSQL(
                headerSql,
                "InOut",
                MRole.SQL_FULLYQUALIFIED,
                MRole.SQL_RO
            );

            string sql = @"
                SELECT QueueData.GRN_ID,
                       QueueData.GRN_No,
                       QueueData.Supplier_Name,
                       QueueData.PO_No,
                       QueueData.Received_Qty,
                       QueueData.Doc_Status,
                       QueueData.Received_By,
                       QueueData.Received_Time,
                       QueueData.Has_Mismatch,
                       QueueData.Has_Quality_Check,
                       QueueData.TotalRecords
                FROM (
                    SELECT HeaderData.GRN_ID,
                           HeaderData.GRN_No,
                           HeaderData.Supplier_Name,
                           HeaderData.PO_No,
                           COALESCE(SUM(COALESCE(InOutLine.MovementQty, 0)), 0) AS Received_Qty,
                           HeaderData.Doc_Status,
                           HeaderData.Received_By,
                           HeaderData.Received_Time,
                           MAX(CASE WHEN COALESCE(LineConfirm.DifferenceQty, 0) <> 0 THEN 1 ELSE 0 END) AS Has_Mismatch,
                           " + qualityCheckSql + @" AS Has_Quality_Check,
                           COUNT(1) OVER () AS TotalRecords
                    FROM (
                        " + headerSql + @"
                    ) HeaderData
                    LEFT OUTER JOIN M_InOutLine InOutLine ON (InOutLine.M_InOut_ID=HeaderData.GRN_ID AND InOutLine.IsActive='Y' AND InOutLine.AD_Client_ID=@Line_AD_Client_ID)
                    LEFT OUTER JOIN M_InOutLineConfirm LineConfirm ON (LineConfirm.M_InOutLine_ID=InOutLine.M_InOutLine_ID AND LineConfirm.IsActive='Y')
                    GROUP BY HeaderData.GRN_ID,
                             HeaderData.GRN_No,
                             HeaderData.Supplier_Name,
                             HeaderData.PO_No,
                             HeaderData.Doc_Status,
                             HeaderData.Received_By,
                             HeaderData.Received_Time
                ) QueueData
                ORDER BY QueueData.Received_Time DESC, QueueData.GRN_No DESC
                OFFSET @Offset ROWS FETCH NEXT @PageSize ROWS ONLY";

            List<SqlParameter> parameters = new List<SqlParameter>();
            parameters.Add(new SqlParameter("@AD_Client_ID", ctx.GetAD_Client_ID()));
            parameters.Add(new SqlParameter("@Line_AD_Client_ID", ctx.GetAD_Client_ID()));
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
                    DateTime? receivedTime = Util.GetValueOfDateTime(dr["Received_Time"]);

                    rows.Add(new
                    {
                        grnId = Util.GetValueOfInt(dr["GRN_ID"]),
                        grnNo = Util.GetValueOfString(dr["GRN_No"]),
                        supplier = Util.GetValueOfString(dr["Supplier_Name"]),
                        poNo = Util.GetValueOfString(dr["PO_No"]),
                        receivedQty = Util.GetValueOfDecimal(dr["Received_Qty"]),
                        statusCode = GetStatusCode(
                            Util.GetValueOfInt(dr["Has_Mismatch"]),
                            Util.GetValueOfInt(dr["Has_Quality_Check"]),
                            Util.GetValueOfString(dr["Doc_Status"])
                        ),
                        receivedBy = Util.GetValueOfString(dr["Received_By"]),
                        receivedTime = receivedTime.HasValue ? receivedTime.Value.ToString("yyyy-MM-ddTHH:mm:ss", CultureInfo.InvariantCulture) : ""
                    });
                }

                var result = new
                {
                    rows = rows,
                    pageNo = pageNo,
                    pageSize = pageSize,
                    totalRecords = totalRecords,
                    totalPages = pageSize == 0 ? 0 : Convert.ToInt32(Math.Ceiling((decimal)totalRecords / pageSize))
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
        /// Receipt queue detail lines for one GRN.
        /// </summary>
        /// <param name="grnId">M_InOut_ID of the selected GRN.</param>
        /// <returns>JSON { rows[] }.</returns>
        [AjaxAuthorizeAttribute]
        [AjaxSessionFilterAttribute]
        public JsonResult GetReceiptQueueLines(int grnId = 0)
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
                SELECT InOutLine.M_InOutLine_ID AS GRN_Line_ID,
                       COALESCE(Product.Name, " + NLiteral("-") + @") AS Item_Name,
                       COALESCE(OrderLine.QtyOrdered, 0) AS Ordered_Qty,
                       COALESCE(InOutLine.MovementQty, 0) AS Received_Qty
                FROM M_InOut InOut
                INNER JOIN M_InOutLine InOutLine ON (InOutLine.M_InOut_ID=InOut.M_InOut_ID AND InOutLine.IsActive='Y')
                LEFT OUTER JOIN C_OrderLine OrderLine ON (OrderLine.C_OrderLine_ID=InOutLine.C_OrderLine_ID AND OrderLine.IsActive='Y')
                LEFT OUTER JOIN M_Product Product ON (Product.M_Product_ID=InOutLine.M_Product_ID AND Product.IsActive='Y')
                WHERE InOut.IsActive='Y'
                  AND InOut.MovementType='V+'
                  AND InOut.M_InOut_ID=@GRN_ID
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
            parameters.Add(new SqlParameter("@GRN_ID", grnId));
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
                        grnLineId = Util.GetValueOfInt(dr["GRN_Line_ID"]),
                        itemName = Util.GetValueOfString(dr["Item_Name"]),
                        orderedQty = Util.GetValueOfDecimal(dr["Ordered_Qty"]),
                        receivedQty = Util.GetValueOfDecimal(dr["Received_Qty"])
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

        private string GetStatusCode(int hasMismatch, int hasQualityCheck, string docStatus)
        {
            if (hasMismatch == 1) { return "mismatch"; }
            if (hasQualityCheck == 1) { return "quality"; }
            if (docStatus == "DR" || docStatus == "IP" || docStatus == "IN") { return "unloading"; }
            return "stored";
        }

        private bool HasQualityCheckColumn()
        {
            string sql;

            if (DB.IsPostgreSQL())
            {
                sql = @"
                    SELECT COUNT(1)
                    FROM information_schema.columns
                    WHERE UPPER(table_name)=UPPER('M_InOutLineConfirm')
                      AND UPPER(column_name)=UPPER('VA010_QualCheckMArk')";
            }
            else
            {
                sql = @"
                    SELECT COUNT(1)
                    FROM USER_TAB_COLUMNS
                    WHERE TABLE_NAME=UPPER('M_InOutLineConfirm')
                      AND COLUMN_NAME=UPPER('VA010_QualCheckMArk')";
            }

            try
            {
                return Util.GetValueOfInt(DB.ExecuteScalar(sql)) > 0;
            }
            catch
            {
                // VA010 is optional; if metadata is unavailable, keep the queue usable without that status.
                return false;
            }
        }
    }
}
