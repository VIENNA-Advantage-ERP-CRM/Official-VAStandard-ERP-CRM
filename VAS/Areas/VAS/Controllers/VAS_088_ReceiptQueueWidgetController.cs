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
        /// One page of the live receipt queue, sorted by movement date - oldest first.
        /// </summary>
        /// <param name="pageNo">1-based page number.</param>
        /// <param name="pageSize">Rows per page.</param>
        /// <param name="year">Selected year (0 = current).</param>
        /// <param name="month">Selected month 1-12 (0 = current).</param>
        /// <returns>JSON { rows[], pageNo, pageSize, totalRecords, totalPages, year, month }.</returns>
        [AjaxAuthorizeAttribute]
        [AjaxSessionFilterAttribute]
        public JsonResult GetReceiptQueue(int pageNo = 1, int pageSize = 5, int year = 0, int month = 0)
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
            DateTime today = DateTime.Today;
            if (year < 2000 || year > 2100) { year = today.Year; }
            if (month < 1 || month > 12) { month = today.Month; }
            DateTime monthStart = new DateTime(year, month, 1);
            DateTime monthEndExclusive = monthStart.AddMonths(1);

            /* QA sheet GRN #39 (2026-09-15): the popup's "Created on" is the receipt's creation moment in
               the viewer's local time. Created is stamped by the database clock (DB 2 runs in UTC, users in
               +03), so the server sends how many hours ago it was created - measured on that same clock -
               and the browser subtracts it from its own now. */
            string createdAgeExpr = DB.IsPostgreSQL()
                ? "(EXTRACT(EPOCH FROM (LOCALTIMESTAMP - InOut.Created)) / 3600)"
                : "((SYSDATE - InOut.Created) * 24)";

            string headerSql = @"
                SELECT InOut.M_InOut_ID AS GRN_ID,
                       InOut.DocumentNo AS GRN_No,
                       BPartner.Name AS Supplier_Name,
                       COALESCE(PurchaseOrder.DocumentNo, " + NLiteral("-") + @") AS PO_No,
                       InOut.DocStatus AS Doc_Status,
                       COALESCE(UserInfo.Name, " + NLiteral("-") + @") AS Received_By,
                       COALESCE(InOut.DateReceived, InOut.MovementDate, InOut.Created) AS Received_Time,
                       COALESCE(InOut.MovementDate, InOut.DateReceived, InOut.Created) AS Movement_Date,
                       " + createdAgeExpr + @" AS Created_Hours_Ago
                FROM M_InOut InOut
                INNER JOIN C_BPartner BPartner ON (BPartner.C_BPartner_ID=InOut.C_BPartner_ID AND BPartner.IsActive='Y')
                LEFT OUTER JOIN C_Order PurchaseOrder ON (PurchaseOrder.C_Order_ID=InOut.C_Order_ID AND PurchaseOrder.IsActive='Y')
                LEFT OUTER JOIN AD_User UserInfo ON (UserInfo.AD_User_ID=InOut.CreatedBy AND UserInfo.IsActive='Y')
                WHERE InOut.IsActive='Y'
                  AND InOut.MovementType='V+'
                  AND InOut.AD_Client_ID=@AD_Client_ID
                  AND COALESCE(InOut.DateReceived, InOut.MovementDate, InOut.Created)>=@Month_From
                  AND COALESCE(InOut.DateReceived, InOut.MovementDate, InOut.Created)<@Month_To";

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
                       QueueData.Created_Hours_Ago,
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
                           HeaderData.Movement_Date,
                           HeaderData.Created_Hours_Ago,
                           COUNT(1) OVER () AS TotalRecords
                    FROM (
                        " + headerSql + @"
                    ) HeaderData
                    LEFT OUTER JOIN M_InOutLine InOutLine ON (InOutLine.M_InOut_ID=HeaderData.GRN_ID AND InOutLine.IsActive='Y' AND InOutLine.AD_Client_ID=@Line_AD_Client_ID)
                    GROUP BY HeaderData.GRN_ID,
                             HeaderData.GRN_No,
                             HeaderData.Supplier_Name,
                             HeaderData.PO_No,
                             HeaderData.Doc_Status,
                             HeaderData.Received_By,
                             HeaderData.Received_Time,
                             HeaderData.Movement_Date,
                             HeaderData.Created_Hours_Ago
                ) QueueData
                ORDER BY QueueData.Movement_Date ASC, QueueData.GRN_No ASC
                OFFSET @Offset ROWS FETCH NEXT @PageSize ROWS ONLY";

            List<SqlParameter> parameters = new List<SqlParameter>();
            parameters.Add(new SqlParameter("@AD_Client_ID", ctx.GetAD_Client_ID()));
            parameters.Add(new SqlParameter("@Month_From", SqlDbType.DateTime) { Value = monthStart });
            parameters.Add(new SqlParameter("@Month_To", SqlDbType.DateTime) { Value = monthEndExclusive });
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
                    string docStatus = Util.GetValueOfString(dr["Doc_Status"]);

                    rows.Add(new
                    {
                        grnId = Util.GetValueOfInt(dr["GRN_ID"]),
                        grnNo = Util.GetValueOfString(dr["GRN_No"]),
                        supplier = Util.GetValueOfString(dr["Supplier_Name"]),
                        poNo = Util.GetValueOfString(dr["PO_No"]),
                        receivedQty = Util.GetValueOfDecimal(dr["Received_Qty"]),
                        statusCode = docStatus,
                        statusText = GetDocStatusName(ctx, docStatus),
                        receivedBy = Util.GetValueOfString(dr["Received_By"]),
                        receivedTime = receivedTime.HasValue ? receivedTime.Value.ToString("yyyy-MM-ddTHH:mm:ss", CultureInfo.InvariantCulture) : "",
                        createdHoursAgo = Math.Round(Math.Max(0, Util.GetValueOfDecimal(dr["Created_Hours_Ago"])), 4)
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

            /* QA sheet GRN #38/#41/#42 (2026-09-15):
               - Attribute_Name feeds the popup's Attribute column (shown only when a line has one);
               - Ordered / Received are in the line's own UOM (QtyEntered, e.g. 3,000 ml) with that UOM
                 returned beside them - OrderLine.QtyOrdered and MovementQty are product-UOM quantities;
               - the line status needs the quantity received against the PO line across ALL its GRNs,
                 which is read in a second query below. */
            string linesSql = @"
                SELECT InOutLine.M_InOutLine_ID AS GRN_Line_ID,
                       InOutLine.C_OrderLine_ID AS Order_Line_ID,
                       COALESCE(Product.Name, " + NLiteral("-") + @") AS Item_Name,
                       AttrInstance.Description AS Attribute_Name,
                       COALESCE(UOM.UOMSymbol, UOM.Name) AS Uom,
                       COALESCE(OrderLine.QtyEntered, OrderLine.QtyOrdered, 0) AS Ordered_Qty,
                       COALESCE(OrderLine.QtyOrdered, 0) AS Ordered_Base_Qty,
                       COALESCE(InOutLine.QtyEntered, InOutLine.MovementQty, 0) AS Received_Qty
                FROM M_InOut InOut
                INNER JOIN M_InOutLine InOutLine ON (InOutLine.M_InOut_ID=InOut.M_InOut_ID AND InOutLine.IsActive='Y')
                LEFT OUTER JOIN C_OrderLine OrderLine ON (OrderLine.C_OrderLine_ID=InOutLine.C_OrderLine_ID AND OrderLine.IsActive='Y')
                LEFT OUTER JOIN M_Product Product ON (Product.M_Product_ID=InOutLine.M_Product_ID AND Product.IsActive='Y')
                LEFT OUTER JOIN C_UOM UOM ON (UOM.C_UOM_ID=InOutLine.C_UOM_ID)
                LEFT OUTER JOIN M_AttributeSetInstance AttrInstance ON (AttrInstance.M_AttributeSetInstance_ID=InOutLine.M_AttributeSetInstance_ID)
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

            try
            {
                DataSet lineDs = DB.ExecuteDataset(linesSql, parameters.ToArray(), null);
                DataRowCollection lineRows = (lineDs != null && lineDs.Tables.Count > 0) ? lineDs.Tables[0].Rows : null;

                // Quantity received per PO line over every GRN (reversed / voided excluded), in the product UOM.
                // Order line ids are integers read from the database - safe to inline.
                Dictionary<int, decimal> cumulativeBaseByOrderLine = new Dictionary<int, decimal>();
                List<string> orderLineIds = new List<string>();
                if (lineRows != null)
                {
                    foreach (DataRow row in lineRows)
                    {
                        int orderLineId = Util.GetValueOfInt(row["Order_Line_ID"]);
                        if (orderLineId > 0 && !orderLineIds.Contains(orderLineId.ToString(CultureInfo.InvariantCulture)))
                        {
                            orderLineIds.Add(orderLineId.ToString(CultureInfo.InvariantCulture));
                        }
                    }
                }
                if (orderLineIds.Count > 0)
                {
                    string cumulativeSql = @"
                        SELECT ReceiptLine.C_OrderLine_ID AS Order_Line_ID,
                               SUM(COALESCE(ReceiptLine.MovementQty, 0)) AS Cumulative_Qty
                        FROM M_InOutLine ReceiptLine
                        INNER JOIN M_InOut Receipt ON (Receipt.M_InOut_ID=ReceiptLine.M_InOut_ID)
                        WHERE ReceiptLine.IsActive='Y'
                          AND Receipt.IsActive='Y'
                          AND Receipt.IsSOTrx='N'
                          AND Receipt.MovementType='V+'
                          AND Receipt.DocStatus NOT IN ('RE','VO')
                          AND ReceiptLine.C_OrderLine_ID IN (" + string.Join(",", orderLineIds) + @")
                        GROUP BY ReceiptLine.C_OrderLine_ID";
                    DataSet cumulativeDs = DB.ExecuteDataset(cumulativeSql, null, null);
                    if (cumulativeDs != null && cumulativeDs.Tables.Count > 0)
                    {
                        foreach (DataRow row in cumulativeDs.Tables[0].Rows)
                        {
                            cumulativeBaseByOrderLine[Util.GetValueOfInt(row["Order_Line_ID"])] = Util.GetValueOfDecimal(row["Cumulative_Qty"]);
                        }
                    }
                }

                if (lineRows != null)
                {
                    foreach (DataRow row in lineRows)
                    {
                        int orderLineId = Util.GetValueOfInt(row["Order_Line_ID"]);
                        decimal orderedQty = Util.GetValueOfDecimal(row["Ordered_Qty"]);
                        decimal orderedBaseQty = Util.GetValueOfDecimal(row["Ordered_Base_Qty"]);
                        decimal receivedQty = Util.GetValueOfDecimal(row["Received_Qty"]);

                        // Cumulative quantity rescaled into the PO line's UOM; without a PO line only this GRN counts.
                        decimal cumulativeQty = receivedQty;
                        decimal cumulativeBase;
                        if (orderLineId > 0 && cumulativeBaseByOrderLine.TryGetValue(orderLineId, out cumulativeBase))
                        {
                            cumulativeQty = orderedBaseQty != 0
                                ? Math.Round(cumulativeBase * orderedQty / orderedBaseQty, 6, MidpointRounding.AwayFromZero)
                                : cumulativeBase;
                        }

                        rows.Add(new
                        {
                            grnLineId = Util.GetValueOfInt(row["GRN_Line_ID"]),
                            itemName = Util.GetValueOfString(row["Item_Name"]),
                            attributeName = Util.GetValueOfString(row["Attribute_Name"]),
                            uom = Util.GetValueOfString(row["Uom"]),
                            orderedQty = orderedQty,
                            receivedQty = receivedQty,
                            cumulativeReceivedQty = cumulativeQty
                        });
                    }
                }

                return Json(JsonConvert.SerializeObject(new { rows = rows }), JsonRequestBehavior.AllowGet);
            }
            catch (Exception ex)
            {
                return Json(new { error = ex.Message }, JsonRequestBehavior.AllowGet);
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
    }
}
