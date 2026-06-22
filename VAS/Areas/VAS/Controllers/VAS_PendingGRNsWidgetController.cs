using Newtonsoft.Json;
using System;
using System.Web.Mvc;
using VAdvantage.Logging;
using VAdvantage.Utility;
using VASLogic.Models;
using VIS.Filters;

namespace VAS.Controllers
{
    /// <summary>
    /// Module Name : VAS_PendingGRNsWidget
    /// Purpose     : Supplies the Pending GRNs KPI.
    /// Chronological development:
    ///   VAI154      2026-06-21 Created
    ///   VAI154      2026-06-22 Compliance update
    /// </summary>
    public class VAS_PendingGRNsWidgetController : Controller
    {
        private static readonly VLogger Log = VLogger.GetVLogger(typeof(VAS_PendingGRNsWidgetController).FullName);

        /// <summary>
        /// Returns the secured pending-GRN count.
        /// </summary>
        /// <returns>JSON pending-GRN count.</returns>
        [AjaxAuthorizeAttribute]
        [AjaxSessionFilterAttribute]
        public JsonResult GetPendingGRNCount()
        {
            if (Session["ctx"] == null)
            {
                return Json("", JsonRequestBehavior.AllowGet);
            }

            Ctx ctx = Session["ctx"] as Ctx;

            try
            {
                VAS_PendingGRNsWidgetModel model = new VAS_PendingGRNsWidgetModel();
                int pendingGRNCount = model.GetPendingGRNCount(ctx);
                string json = JsonConvert.SerializeObject(new { pending_grn_count = pendingGRNCount });
                return Json(json, JsonRequestBehavior.AllowGet);
            }
            catch (Exception ex)
            {
                Log.Log(Level.SEVERE, "VAS_PendingGRNsWidget.GetPendingGRNCount", ex);
                string json = JsonConvert.SerializeObject(new { error = Msg.GetMsg(ctx, "Error") ?? "Error" });
                return Json(json, JsonRequestBehavior.AllowGet);
            }
        }
    }
}
