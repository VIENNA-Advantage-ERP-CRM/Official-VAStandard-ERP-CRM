/******************************************************
 * Module Name    : VAS
 * Purpose        : Net Movement dashboard widget endpoints
 * chronological  : Development
 * Created Date   : 2026-09-02
 * Created by     : VAI154
 ******************************************************/

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
    /// Module Name : VAS_231_NetMovementWidget
    /// Purpose     : Thin AJAX endpoints for the Net Movement widget. All business logic
    ///               lives in VASLogic.Models.VAS_231_NetMovementModel; these actions only
    ///               resolve the session context, pass the selected period through and
    ///               serialize the model result.
    ///
    ///               The browser sends nothing authoritative. The tenant comes from the
    ///               authenticated context; the calendar, the fiscal year, the accounting
    ///               schema currency and the role's organization access are all resolved
    ///               server-side. The ONE value the client does send - the period id - is
    ///               re-validated by the model before a single C_Payment row is read: it
    ///               must still be active, accessible, already started and on the tenant's
    ///               primary calendar.
    ///
    ///               No exception detail reaches the browser - a failure serializes as
    ///               { error: true } and is logged with its stack trace server-side.
    /// Chronological development:
    ///   VAI154      2026-09-02 Created
    /// </summary>
    public class VAS_231_NetMovementWidgetController : Controller
    {
        private static readonly VLogger Log = VLogger.GetVLogger(typeof(VAS_231_NetMovementWidgetController).FullName);

        /// <summary>
        /// Returns the base currency, the current fiscal year's started periods, the
        /// period to preselect and that period's figures.
        /// </summary>
        /// <returns>JSON-serialized NetMovementBootstrap, "" without a session, or { error }.</returns>
        [AjaxAuthorizeAttribute]
        [AjaxSessionFilterAttribute]
        public JsonResult GetBootstrap()
        {
            string retJSON = "";

            if (Session["ctx"] != null)
            {
                Ctx ctx = Session["ctx"] as Ctx;
                try
                {
                    VAS_231_NetMovementModel model = new VAS_231_NetMovementModel();
                    retJSON = JsonConvert.SerializeObject(model.GetBootstrap(ctx));
                }
                catch (Exception ex)
                {
                    Log.Log(Level.SEVERE, "VAS_231_NetMovementWidget.GetBootstrap", ex);
                    retJSON = JsonConvert.SerializeObject(new { error = true });
                }
            }

            return Json(retJSON, JsonRequestBehavior.AllowGet);
        }

        /// <summary>
        /// Returns the receipts / payments / net figures of one period.
        /// </summary>
        /// <param name="periodId">C_Period_ID selected by the user.</param>
        /// <returns>JSON-serialized NetMovementData, "" without a session, or { error }.</returns>
        [AjaxAuthorizeAttribute]
        [AjaxSessionFilterAttribute]
        public JsonResult GetPeriodData(int periodId)
        {
            string retJSON = "";

            if (Session["ctx"] != null)
            {
                Ctx ctx = Session["ctx"] as Ctx;
                try
                {
                    VAS_231_NetMovementModel model = new VAS_231_NetMovementModel();
                    retJSON = JsonConvert.SerializeObject(model.GetPeriodData(ctx, periodId));
                }
                catch (Exception ex)
                {
                    Log.Log(Level.SEVERE, "VAS_231_NetMovementWidget.GetPeriodData", ex);
                    retJSON = JsonConvert.SerializeObject(new { error = true });
                }
            }

            return Json(retJSON, JsonRequestBehavior.AllowGet);
        }
    }
}
