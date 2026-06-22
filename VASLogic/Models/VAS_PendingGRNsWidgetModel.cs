using System.Data.SqlClient;
using VAdvantage.DataBase;
using VAdvantage.Model;
using VAdvantage.Utility;

namespace VASLogic.Models
{
    /// <summary>
    /// Module Name : VAS_PendingGRNsWidget
    /// Purpose     : Counts overdue active purchase-order lines without a completed GRN.
    /// Chronological development:
    ///   VAI154      2026-06-21 Created
    ///   VAI154      2026-06-22 Compliance update
    /// </summary>
    public class VAS_PendingGRNsWidgetModel
    {
        /// <summary>
        /// Counts accessible overdue purchase-order lines awaiting a GRN.
        /// </summary>
        /// <param name="ctx">Current session context.</param>
        /// <returns>Pending GRN count.</returns>
        public int GetPendingGRNCount(Ctx ctx)
        {
            if (ctx == null) { return 0; }

            string orderSql = @"
                SELECT PurchaseOrder.C_Order_ID,
                       PurchaseOrder.DatePromised
                FROM C_Order PurchaseOrder
                WHERE PurchaseOrder.IsActive=N'Y'
                  AND PurchaseOrder.IsSOTrx=N'N'
                  AND PurchaseOrder.DocStatus=N'CO'
                  AND PurchaseOrder.AD_Client_ID=@Order_Client_ID
                  AND PurchaseOrder.AD_Org_ID IN (0,COALESCE(NULLIF(@Order_Org_ID,0),PurchaseOrder.AD_Org_ID))";

            orderSql = MRole.GetDefault(ctx).AddAccessSQL(
                orderSql,
                "PurchaseOrder",
                MRole.SQL_FULLYQUALIFIED,
                MRole.SQL_RO
            );

            string orderLineSql = @"
                SELECT OrderLine.C_OrderLine_ID,
                       OrderLine.C_Order_ID,
                       OrderLine.DatePromised,
                       OrderLine.QtyOrdered,
                       OrderLine.QtyDelivered
                FROM C_OrderLine OrderLine
                WHERE OrderLine.IsActive=N'Y'
                  AND OrderLine.AD_Client_ID=@OrderLine_Client_ID
                  AND OrderLine.AD_Org_ID IN (0,COALESCE(NULLIF(@OrderLine_Org_ID,0),OrderLine.AD_Org_ID))";

            orderLineSql = MRole.GetDefault(ctx).AddAccessSQL(
                orderLineSql,
                "OrderLine",
                MRole.SQL_FULLYQUALIFIED,
                MRole.SQL_RO
            );

            string receiptSql = @"
                SELECT Receipt.M_InOut_ID
                FROM M_InOut Receipt
                WHERE Receipt.IsActive=N'Y'
                  AND Receipt.DocStatus IN (N'CO',N'CL')
                  AND Receipt.MovementType=N'V+'
                  AND Receipt.AD_Client_ID=@Receipt_Client_ID
                  AND Receipt.AD_Org_ID IN (0,COALESCE(NULLIF(@Receipt_Org_ID,0),Receipt.AD_Org_ID))";

            receiptSql = MRole.GetDefault(ctx).AddAccessSQL(
                receiptSql,
                "Receipt",
                MRole.SQL_FULLYQUALIFIED,
                MRole.SQL_RO
            );

            string receiptLineSql = @"
                SELECT ReceiptLine.M_InOut_ID,
                       ReceiptLine.C_OrderLine_ID
                FROM M_InOutLine ReceiptLine
                WHERE ReceiptLine.IsActive=N'Y'
                  AND ReceiptLine.AD_Client_ID=@ReceiptLine_Client_ID
                  AND ReceiptLine.AD_Org_ID IN (0,COALESCE(NULLIF(@ReceiptLine_Org_ID,0),ReceiptLine.AD_Org_ID))";

            receiptLineSql = MRole.GetDefault(ctx).AddAccessSQL(
                receiptLineSql,
                "ReceiptLine",
                MRole.SQL_FULLYQUALIFIED,
                MRole.SQL_RO
            );

            string sql = @"
                WITH PurchaseOrders AS (
                    " + orderSql + @"
                ),
                PurchaseOrderLines AS (
                    " + orderLineSql + @"
                ),
                CompletedReceipts AS (
                    " + receiptSql + @"
                ),
                CompletedReceiptLines AS (
                    " + receiptLineSql + @"
                )
                SELECT COUNT(*) AS Pending_GRN_Count
                FROM PurchaseOrderLines
                INNER JOIN PurchaseOrders ON (PurchaseOrders.C_Order_ID=PurchaseOrderLines.C_Order_ID)
                WHERE COALESCE(PurchaseOrderLines.QtyOrdered,0)>COALESCE(PurchaseOrderLines.QtyDelivered,0)
                  AND COALESCE(PurchaseOrderLines.DatePromised,PurchaseOrders.DatePromised)<CURRENT_DATE
                  AND NOT EXISTS (
                      SELECT 1
                      FROM CompletedReceiptLines
                      INNER JOIN CompletedReceipts ON (CompletedReceipts.M_InOut_ID=CompletedReceiptLines.M_InOut_ID)
                      WHERE CompletedReceiptLines.C_OrderLine_ID=PurchaseOrderLines.C_OrderLine_ID
                  )";

            SqlParameter[] parameters = new SqlParameter[]
            {
                new SqlParameter("@Order_Client_ID", ctx.GetAD_Client_ID()),
                new SqlParameter("@Order_Org_ID", ctx.GetAD_Org_ID()),
                new SqlParameter("@OrderLine_Client_ID", ctx.GetAD_Client_ID()),
                new SqlParameter("@OrderLine_Org_ID", ctx.GetAD_Org_ID()),
                new SqlParameter("@Receipt_Client_ID", ctx.GetAD_Client_ID()),
                new SqlParameter("@Receipt_Org_ID", ctx.GetAD_Org_ID()),
                new SqlParameter("@ReceiptLine_Client_ID", ctx.GetAD_Client_ID()),
                new SqlParameter("@ReceiptLine_Org_ID", ctx.GetAD_Org_ID())
            };

            return Util.GetValueOfInt(DB.ExecuteScalar(sql, parameters, null));
        }
    }
}
