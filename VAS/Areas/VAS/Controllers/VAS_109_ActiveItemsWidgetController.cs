using Newtonsoft.Json;
using System;
using System.Data;
using System.Data.SqlClient;
using System.Web.Mvc;
using VAdvantage.DataBase;
using VAdvantage.Logging;
using VAdvantage.Model;
using VAdvantage.Utility;
using VIS.Filters;

namespace VAS.Controllers
{
    /// <summary>
    /// Module Name : VAS_109_ActiveItemsWidget
    /// Purpose     : Supplies the "Active Items" KPI for the Product / Item Master
    ///               dashboard - the count of active, non-discontinued items, with
    ///               the discontinued count as the meta caption.
    ///               Widget number 109 - reassign on hand-off.
    /// Chronological development:
    ///   109         2026-07-15 Created
    /// </summary>
    public class VAS_109_ActiveItemsWidgetController : Controller
    {
        private static readonly VLogger Log = VLogger.GetVLogger(typeof(VAS_109_ActiveItemsWidgetController).FullName);

        /// <summary>
        /// Returns the active-item and discontinued-item counts.
        /// </summary>
        [AjaxAuthorizeAttribute]
        [AjaxSessionFilterAttribute]
        public JsonResult GetActiveItems()
        {
            Ctx ctx = Session["ctx"] as Ctx;
            if (ctx == null)
            {
                return Json("", JsonRequestBehavior.AllowGet);
            }

            try
            {
                ActiveItemsResult result = GetActiveItemsData(ctx);
                string json = JsonConvert.SerializeObject(result);
                return Json(json, JsonRequestBehavior.AllowGet);
            }
            catch (Exception ex)
            {
                Log.Log(Level.SEVERE, "VAS_109_ActiveItemsWidget.GetActiveItems", ex);
                string json = JsonConvert.SerializeObject(new { error = Msg.GetMsg(ctx, "Error") ?? "Error" });
                return Json(json, JsonRequestBehavior.AllowGet);
            }
        }

        /// <summary>
        /// Counts active items (active and not discontinued) and, for the meta
        /// caption, the discontinued items within the same active catalogue.
        /// </summary>
        /// <param name="ctx">Current application context.</param>
        /// <returns>Active and discontinued counts.</returns>
        private ActiveItemsResult GetActiveItemsData(Ctx ctx)
        {
            ActiveItemsResult result = new ActiveItemsResult();
            if (ctx == null) { return result; }

            string sql = @"
                SELECT SUM(CASE WHEN Product.Discontinued IS NULL OR Product.Discontinued=N'N' THEN 1 ELSE 0 END) AS Active_Count,
                       SUM(CASE WHEN Product.Discontinued=N'Y' THEN 1 ELSE 0 END) AS Discontinued_Count
                FROM M_Product Product
                WHERE Product.IsActive=N'Y'
                  AND Product.AD_Client_ID=@AD_Client_ID
                  AND Product.AD_Org_ID IN (0,COALESCE(NULLIF(@AD_Org_ID,0),Product.AD_Org_ID))";

            sql = MRole.GetDefault(ctx).AddAccessSQL(
                sql,
                "Product",
                MRole.SQL_FULLYQUALIFIED,
                MRole.SQL_RO
            );

            SqlParameter[] parameters = new SqlParameter[]
            {
                new SqlParameter("@AD_Client_ID", ctx.GetAD_Client_ID()),
                new SqlParameter("@AD_Org_ID", ctx.GetAD_Org_ID())
            };

            IDataReader reader = null;
            try
            {
                reader = DB.ExecuteReader(sql, parameters);
                if (reader != null && reader.Read())
                {
                    result.active_count = Util.GetValueOfInt(reader["Active_Count"]);
                    result.discontinued_count = Util.GetValueOfInt(reader["Discontinued_Count"]);
                }
            }
            finally
            {
                if (reader != null) { reader.Close(); reader.Dispose(); }
            }

            return result;
        }

        /// <summary>Active Items KPI response.</summary>
        private class ActiveItemsResult
        {
            public int active_count { get; set; }
            public int discontinued_count { get; set; }
        }
    }
}
