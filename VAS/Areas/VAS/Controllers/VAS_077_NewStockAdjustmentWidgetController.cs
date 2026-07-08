using Newtonsoft.Json;
using System;
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
    /// Module Name : VAS_077_NewStockAdjustmentWidget
    /// Purpose     : Supplies the portable Physical Inventory window reference.
    /// Chronological development:
    ///   VAI154      2026-06-22 Created
    /// </summary>
    public class VAS_077_NewStockAdjustmentWidgetController : Controller
    {
        private static readonly VLogger Log = VLogger.GetVLogger(typeof(VAS_077_NewStockAdjustmentWidgetController).FullName);

        private const string PhysicalInventoryWindowExportId = "VAS_1000222";

        /// <summary>Returns the active Physical Inventory window ID resolved by Export_ID.</summary>
        [AjaxAuthorizeAttribute]
        [AjaxSessionFilterAttribute]
        public JsonResult GetInventoryCountWindowId()
        {
            Ctx ctx = Session["ctx"] as Ctx;
            if (ctx == null) { return Json("", JsonRequestBehavior.AllowGet); }

            try
            {
                string json = JsonConvert.SerializeObject(new
                {
                    window_id = GetInventoryCountWindowIdData(ctx)
                });
                return Json(json, JsonRequestBehavior.AllowGet);
            }
            catch (Exception ex)
            {
                Log.Log(Level.SEVERE, "VAS_077_NewStockAdjustmentWidget.GetInventoryCountWindowId", ex);
                string json = JsonConvert.SerializeObject(new { error = Msg.GetMsg(ctx, "Error") ?? "Error" });
                return Json(json, JsonRequestBehavior.AllowGet);
            }
        }

        private int GetInventoryCountWindowIdData(Ctx ctx)
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
