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
    /// Module Name : VAS_179_OpenMaterialIssuesWidget
    /// Purpose     : Supplies the KPI metric count of open/unfulfilled material issue documents.
    /// Chronological development:
    ///   AI-Dev      2026-08-02 Created
    /// </summary>
    public class VAS_179_OpenMaterialIssuesWidgetController : Controller
    {
        private static readonly VLogger Log = VLogger.GetVLogger(typeof(VAS_179_OpenMaterialIssuesWidgetController).FullName);

        /// <summary>Returns the aggregate count of open material issue documents.</summary>
        [AjaxAuthorizeAttribute]
        [AjaxSessionFilterAttribute]
        public JsonResult GetOpenMaterialIssuesCount()
        {
            Ctx ctx = Session["ctx"] as Ctx;
            if (ctx == null) { return Json("", JsonRequestBehavior.AllowGet); }

            try
            {
                int count = GetOpenMaterialIssuesCountData(ctx);
                string json = JsonConvert.SerializeObject(new
                {
                    count = count,
                    success = true
                });
                return Json(json, JsonRequestBehavior.AllowGet);
            }
            catch (Exception ex)
            {
                Log.Log(Level.SEVERE, "VAS_179_OpenMaterialIssuesWidget.GetOpenMaterialIssuesCount", ex);
                string json = JsonConvert.SerializeObject(new { error = Msg.GetMsg(ctx, "Error") ?? "Error" });
                return Json(json, JsonRequestBehavior.AllowGet);
            }
        }

        private int GetOpenMaterialIssuesCountData(Ctx ctx)
        {
            if (ctx == null) { return 0; }

            // M_Inventory backs BOTH Physical Inventory and Internal Use (Material Issue)
            // documents - IsInternalUse = 'Y' is what separates them. Without that filter this
            // KPI counts physical inventory counts as material issues (29 instead of 15 on
            // FSMTesting6). DocStatus: DR Drafted, IP In Progress, WC Waiting Confirmation,
            // IN Invalid - i.e. raised but not yet fulfilled.
            string sql = $@"
                SELECT COUNT(inv.M_Inventory_ID)
                FROM M_Inventory inv
                WHERE inv.IsActive = 'Y'
                  AND inv.IsInternalUse = 'Y'
                  AND inv.DocStatus IN ('DR', 'IP', 'WC', 'IN')";

            sql = MRole.GetDefault(ctx).AddAccessSQL(
                sql,
                "inv",
                MRole.SQL_FULLYQUALIFIED,
                MRole.SQL_RO
            );

            return Util.GetValueOfInt(DB.ExecuteScalar(sql, null, null));
        }
    }
}
