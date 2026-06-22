using System.Data.SqlClient;
using VAdvantage.DataBase;
using VAdvantage.Model;
using VAdvantage.Utility;

namespace VASLogic.Models
{
    /// <summary>
    /// Module Name : VAS_TotalStockQtyWidget
    /// Purpose     : Loads accessible active on-hand quantity.
    /// Chronological development:
    ///   VAI154      2026-06-21 Created
    ///   VAI154      2026-06-22 Compliance update
    /// </summary>
    public class VAS_TotalStockQtyWidgetModel
    {
        /// <summary>
        /// Sums active on-hand stock visible to the current role.
        /// </summary>
        /// <param name="ctx">Current session context.</param>
        /// <returns>Total on-hand quantity.</returns>
        public decimal GetTotalStockQty(Ctx ctx)
        {
            if (ctx == null) { return 0; }

            string sql = @"
                SELECT COALESCE(SUM(Storage.QtyOnHand),0) AS Total_Stock_Qty
                FROM M_Storage Storage
                WHERE Storage.IsActive=N'Y'
                  AND Storage.AD_Client_ID=@AD_Client_ID
                  AND Storage.AD_Org_ID IN (0,COALESCE(NULLIF(@AD_Org_ID,0),Storage.AD_Org_ID))";

            sql = MRole.GetDefault(ctx).AddAccessSQL(
                sql,
                "Storage",
                MRole.SQL_FULLYQUALIFIED,
                MRole.SQL_RO
            );

            SqlParameter[] parameters = new SqlParameter[]
            {
                new SqlParameter("@AD_Client_ID", ctx.GetAD_Client_ID()),
                new SqlParameter("@AD_Org_ID", ctx.GetAD_Org_ID())
            };

            return Util.GetValueOfDecimal(DB.ExecuteScalar(sql, parameters, null));
        }
    }
}
