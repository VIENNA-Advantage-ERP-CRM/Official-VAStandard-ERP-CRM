/******************************************************
 * Module Name    : VAS
 * Purpose        : Mandatory Close Checklist dashboard widget endpoints
 * chronological  : Development
 * Created Date   : 2026-08-24
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
    /// Module Name : VAS_195_MandatoryChecklistWidget
    /// Purpose     : Thin AJAX endpoints for the Mandatory Close Checklist widget. All
    ///               business logic lives in
    ///               VASLogic.Models.VAS_195_MandatoryChecklistModel; these actions only
    ///               resolve the session context, pass the selected values through and
    ///               serialize the model result.
    ///
    ///               The browser sends three things and none of them is authoritative:
    ///               a period id, a check code and a page. The tenant, the calendar, the
    ///               accounting schema, the organization scope, every account mapping
    ///               and every table name are resolved server-side. The period is
    ///               re-validated (still active, open, standard, on the primary
    ///               calendar) and the check code is matched against the server-side
    ///               registry before a single query is built - so a crafted request can
    ///               neither widen the data nor steer the server at a table of its
    ///               choosing.
    ///
    ///               Three actions: bootstrap on load, re-evaluate on a period change,
    ///               and one page of records when a checklist row is opened. Detail rows
    ///               are never fetched with the checklist.
    ///
    ///               No exception detail reaches the browser - a failure serializes as
    ///               { error: true } and is logged with its stack trace server-side.
    /// Chronological development:
    ///   VAI154      2026-08-24 Created
    /// </summary>
    public class VAS_195_MandatoryChecklistWidgetController : Controller
    {
        private static readonly VLogger Log = VLogger.GetVLogger(typeof(VAS_195_MandatoryChecklistWidgetController).FullName);

        /// <summary>
        /// Returns the tenant's accounting context, the selectable open periods of the
        /// primary calendar, the period to preselect, and that period's 23 evaluated
        /// checks with the close verdict.
        /// </summary>
        /// <returns>JSON-serialized ChecklistBootstrap, "" without a session, or { error }.</returns>
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
                    VAS_195_MandatoryChecklistModel model = new VAS_195_MandatoryChecklistModel();
                    retJSON = JsonConvert.SerializeObject(model.GetBootstrap(ctx));
                }
                catch (Exception ex)
                {
                    Log.Log(Level.SEVERE, "VAS_195_MandatoryChecklistWidget.GetBootstrap", ex);
                    retJSON = JsonConvert.SerializeObject(new { error = true });
                }
            }

            return Json(retJSON, JsonRequestBehavior.AllowGet);
        }

        /// <summary>
        /// Re-evaluates all 23 checks for one period.
        /// </summary>
        /// <param name="periodId">C_Period_ID selected by the user.</param>
        /// <returns>JSON-serialized PeriodData, or { error }.</returns>
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
                    VAS_195_MandatoryChecklistModel model = new VAS_195_MandatoryChecklistModel();
                    retJSON = JsonConvert.SerializeObject(model.GetPeriodData(ctx, periodId));
                }
                catch (Exception ex)
                {
                    Log.Log(Level.SEVERE, "VAS_195_MandatoryChecklistWidget.GetPeriodData", ex);
                    retJSON = JsonConvert.SerializeObject(new { error = true });
                }
            }

            return Json(retJSON, JsonRequestBehavior.AllowGet);
        }

        /// <summary>
        /// Returns one page of the records behind a checklist row, together with the
        /// column set that check declares. The check code is validated against the
        /// server-side registry and the page size is clamped by the model.
        /// </summary>
        /// <param name="periodId">C_Period_ID selected by the user.</param>
        /// <param name="checkCode">MPC_CLOSE_* code of the clicked row.</param>
        /// <param name="pageNo">1-based page number.</param>
        /// <param name="pageSize">Requested rows per page.</param>
        /// <returns>JSON-serialized DetailPage, or { error }.</returns>
        [AjaxAuthorizeAttribute]
        [AjaxSessionFilterAttribute]
        public JsonResult GetDetail(int periodId, string checkCode, int pageNo, int pageSize)
        {
            string retJSON = "";

            if (Session["ctx"] != null)
            {
                Ctx ctx = Session["ctx"] as Ctx;
                try
                {
                    VAS_195_MandatoryChecklistModel model = new VAS_195_MandatoryChecklistModel();
                    retJSON = JsonConvert.SerializeObject(
                        model.GetDetail(ctx, periodId, checkCode, pageNo, pageSize));
                }
                catch (Exception ex)
                {
                    Log.Log(Level.SEVERE, "VAS_195_MandatoryChecklistWidget.GetDetail", ex);
                    retJSON = JsonConvert.SerializeObject(new { error = true });
                }
            }

            return Json(retJSON, JsonRequestBehavior.AllowGet);
        }
    }
}
