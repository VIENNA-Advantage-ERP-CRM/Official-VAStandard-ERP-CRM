using System.Data.SqlClient;
using VAdvantage.DataBase;
using VAdvantage.Model;
using VAdvantage.Utility;

namespace VASLogic.Models
{
    /// <summary>
    /// Module Name : VAS_NewStockAdjustmentWidget
    /// Purpose     : Resolves the portable Physical Inventory window reference.
    /// Chronological development:
    ///   VAI154      2026-06-22 Created
    /// </summary>
    public class VAS_NewStockAdjustmentWidgetModel
    {
        private const string PhysicalInventoryWindowExportId = "VAS_1000222";

        /// <summary>Returns the active Physical Inventory window available to the role.</summary>
        public int GetInventoryCountWindowId(Ctx ctx)
        {
            if (ctx == null) { return 0; }

            string sql = @"
                SELECT ADWindow.AD_Window_ID
                FROM AD_Window ADWindow
                WHERE ADWindow.IsActive=N'Y'
                  AND ADWindow.Export_ID=@Export_ID";

            sql = MRole.GetDefault(ctx).AddAccessSQL(
                sql,
                "ADWindow",
                MRole.SQL_FULLYQUALIFIED,
                MRole.SQL_RO
            );

            SqlParameter[] parameters = new SqlParameter[]
            {
                new SqlParameter("@Export_ID", PhysicalInventoryWindowExportId)
            };

            return Util.GetValueOfInt(DB.ExecuteScalar(sql, parameters, null));
        }
    }
}
