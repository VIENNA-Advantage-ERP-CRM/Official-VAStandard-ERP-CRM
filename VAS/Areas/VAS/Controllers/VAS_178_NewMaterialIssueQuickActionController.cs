using System.Data.SqlClient;
using Newtonsoft.Json;
using System;
using System.Web.Mvc;
using VAdvantage.DataBase;
using VAdvantage.Logging;
using VAdvantage.Model;
using VAdvantage.Utility;
using VIS.Filters;

namespace VIS.Controllers
{
    /// <summary>
    /// Module Name : VAS_178_NewMaterialIssueQuickAction
    /// Purpose     : Supplies the Material Issue / Internal Use window reference for 1x1 quick action launcher.
    /// Chronological development:
    ///   AI-Dev      2026-08-02 Created
    /// </summary>
    public class VAS_178_NewMaterialIssueQuickActionController : Controller
    {
        private static readonly VLogger Log = VLogger.GetVLogger(typeof(VAS_178_NewMaterialIssueQuickActionController).FullName);

        // Export_ID is stable across databases (AD_Window_ID is not): VAS_1000221 resolves to
        // VAS_InternalUseInventory on every VA instance. VAS_1000223 is the VAS_Production window.
        private const string MaterialIssueWindowExportId = "VAS_1000221";

        /// <summary>Returns the active Material Issue / Internal Use window ID.</summary>
        [AjaxAuthorizeAttribute]
        [AjaxSessionFilterAttribute]
        public JsonResult GetMaterialIssueWindowId()
        {
            Ctx ctx = Session["ctx"] as Ctx;
            if (ctx == null) { return Json("", JsonRequestBehavior.AllowGet); }

            try
            {
                string json = JsonConvert.SerializeObject(new
                {
                    windowId = GetMaterialIssueWindowIdData(ctx)
                });
                return Json(json, JsonRequestBehavior.AllowGet);
            }
            catch (Exception ex)
            {
                Log.Log(Level.SEVERE, "VAS_178_NewMaterialIssueQuickAction.GetMaterialIssueWindowId", ex);
                string json = JsonConvert.SerializeObject(new { error = Msg.GetMsg(ctx, "Error") ?? "Error" });
                return Json(json, JsonRequestBehavior.AllowGet);
            }
        }

        private int GetMaterialIssueWindowIdData(Ctx ctx)
        {
            if (ctx == null) { return 0; }

            string sql = $@"
                SELECT ADWindow.AD_Window_ID
                FROM AD_Window ADWindow
                WHERE ADWindow.IsActive='Y'
                  AND (ADWindow.Export_ID = @Export_ID
                       OR ADWindow.Name = 'VAS_InternalUseInventory'
                       OR ADWindow.Name = 'Internal Use Inventory'
                       OR ADWindow.Name = 'Material Issue'
                       OR ADWindow.Name = 'Internal Use'
                       OR ADWindow.Name = 'Inventory Use')
                ORDER BY ADWindow.AD_Window_ID ASC";

            sql = MRole.GetDefault(ctx).AddAccessSQL(
                sql,
                "ADWindow",
                MRole.SQL_FULLYQUALIFIED,
                MRole.SQL_RO
            );

            SqlParameter[] parameters = new SqlParameter[]
            {
                new SqlParameter("@Export_ID", MaterialIssueWindowExportId)
            };

            int windowId = Util.GetValueOfInt(DB.ExecuteScalar(sql, parameters, null));
            if (windowId <= 0)
            {
                // Fallback for instances that renamed the window. Match on a space-insensitive
                // "InternalUse" token so both 'VAS_InternalUseInventory' and 'Internal Use Inventory'
                // resolve. A broad '%Issue%'/'%Inventory%' LIKE must NOT be used here - it matches
                // 'Perpetual Inventory' (AD_Window_ID 175) first and opens the wrong window.
                string fallbackSql = @"
                    SELECT ADWindow.AD_Window_ID
                    FROM AD_Window ADWindow
                    WHERE ADWindow.IsActive='Y'
                      AND REPLACE(UPPER(ADWindow.Name), ' ', '') LIKE '%INTERNALUSE%'
                    ORDER BY ADWindow.AD_Window_ID ASC";

                fallbackSql = MRole.GetDefault(ctx).AddAccessSQL(
                    fallbackSql,
                    "ADWindow",
                    MRole.SQL_FULLYQUALIFIED,
                    MRole.SQL_RO
                );

                windowId = Util.GetValueOfInt(DB.ExecuteScalar(fallbackSql, null, null));
            }

            return windowId;
        }
    }
}
