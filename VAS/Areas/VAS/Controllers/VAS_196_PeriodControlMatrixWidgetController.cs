/******************************************************
 * Module Name    : VAS
 * Purpose        : Period Control Matrix dashboard widget endpoints
 * chronological  : Development
 * Created Date   : 2026-08-19
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
    /// Module Name : VAS_196_PeriodControlMatrixWidget
    /// Purpose     : Thin AJAX endpoints for the Period Control Matrix widget.
    ///               All business logic lives in
    ///               VASLogic.Models.VAS_196_PeriodControlMatrixModel; these actions
    ///               only resolve the session context, coerce the incoming ids and
    ///               serialize the model result. The tenant always comes from the
    ///               authenticated context - the client never supplies AD_Client_ID -
    ///               and every selected id is re-validated server-side inside the
    ///               model before a status change is executed.
    ///               One action per cascade level so the browser never requests the
    ///               whole Calendar/Year/Period/PeriodControl hierarchy at once.
    /// Chronological development:
    ///   VAI154      2026-08-19 Created
    /// </summary>
    public class VAS_196_PeriodControlMatrixWidgetController : Controller
    {
        private static readonly VLogger Log = VLogger.GetVLogger(typeof(VAS_196_PeriodControlMatrixWidgetController).FullName);

        /// <summary>
        /// Returns the accessible calendars plus the default path (years of the
        /// default calendar, periods of the default year) and the ids to preselect.
        /// </summary>
        /// <returns>JSON-serialized MatrixBootstrap, "" without a session, or { error }.</returns>
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
                    VAS_196_PeriodControlMatrixModel model = new VAS_196_PeriodControlMatrixModel();
                    retJSON = JsonConvert.SerializeObject(model.GetBootstrap(ctx));
                }
                catch (Exception ex)
                {
                    Log.Log(Level.SEVERE, "VAS_196_PeriodControlMatrixWidget.GetBootstrap", ex);
                    retJSON = JsonConvert.SerializeObject(new { error = true });
                }
            }

            return Json(retJSON, JsonRequestBehavior.AllowGet);
        }

        /// <summary>
        /// Returns the active fiscal years of one calendar.
        /// </summary>
        /// <param name="calendarId">C_Calendar_ID selected by the user.</param>
        /// <returns>JSON-serialized list of LookupItem, or { error }.</returns>
        [AjaxAuthorizeAttribute]
        [AjaxSessionFilterAttribute]
        public JsonResult GetYears(int calendarId)
        {
            string retJSON = "";

            if (Session["ctx"] != null)
            {
                Ctx ctx = Session["ctx"] as Ctx;
                try
                {
                    VAS_196_PeriodControlMatrixModel model = new VAS_196_PeriodControlMatrixModel();
                    retJSON = JsonConvert.SerializeObject(model.GetYears(ctx, calendarId));
                }
                catch (Exception ex)
                {
                    Log.Log(Level.SEVERE, "VAS_196_PeriodControlMatrixWidget.GetYears", ex);
                    retJSON = JsonConvert.SerializeObject(new { error = true });
                }
            }

            return Json(retJSON, JsonRequestBehavior.AllowGet);
        }

        /// <summary>
        /// Returns the active standard periods of one fiscal year.
        /// </summary>
        /// <param name="yearId">C_Year_ID selected by the user.</param>
        /// <returns>JSON-serialized list of LookupItem, or { error }.</returns>
        [AjaxAuthorizeAttribute]
        [AjaxSessionFilterAttribute]
        public JsonResult GetPeriods(int yearId)
        {
            string retJSON = "";

            if (Session["ctx"] != null)
            {
                Ctx ctx = Session["ctx"] as Ctx;
                try
                {
                    VAS_196_PeriodControlMatrixModel model = new VAS_196_PeriodControlMatrixModel();
                    retJSON = JsonConvert.SerializeObject(model.GetPeriods(ctx, yearId));
                }
                catch (Exception ex)
                {
                    Log.Log(Level.SEVERE, "VAS_196_PeriodControlMatrixWidget.GetPeriods", ex);
                    retJSON = JsonConvert.SerializeObject(new { error = true });
                }
            }

            return Json(retJSON, JsonRequestBehavior.AllowGet);
        }

        /// <summary>
        /// Returns every active period control of one period, one row per document
        /// base type. The set is bounded by the number of document base types, so it
        /// is served whole and paged on the client.
        /// </summary>
        /// <param name="periodId">C_Period_ID selected by the user.</param>
        /// <returns>JSON-serialized list of PeriodControlRow, or { error }.</returns>
        [AjaxAuthorizeAttribute]
        [AjaxSessionFilterAttribute]
        public JsonResult GetPeriodControls(int periodId)
        {
            string retJSON = "";

            if (Session["ctx"] != null)
            {
                Ctx ctx = Session["ctx"] as Ctx;
                try
                {
                    VAS_196_PeriodControlMatrixModel model = new VAS_196_PeriodControlMatrixModel();
                    retJSON = JsonConvert.SerializeObject(model.GetPeriodControls(ctx, periodId));
                }
                catch (Exception ex)
                {
                    Log.Log(Level.SEVERE, "VAS_196_PeriodControlMatrixWidget.GetPeriodControls", ex);
                    retJSON = JsonConvert.SerializeObject(new { error = true });
                }
            }

            return Json(retJSON, JsonRequestBehavior.AllowGet);
        }

        /// <summary>
        /// Opens or closes one period control by setting PeriodAction and executing
        /// the standard process; PeriodStatus itself is never written here. POST only
        /// because it changes data.
        /// </summary>
        /// <param name="calendarId">C_Calendar_ID the client had selected.</param>
        /// <param name="yearId">C_Year_ID the client had selected.</param>
        /// <param name="periodId">C_Period_ID the client had selected.</param>
        /// <param name="periodControlId">C_PeriodControl_ID of the clicked row.</param>
        /// <returns>JSON-serialized StatusChangeResult, or { error }.</returns>
        [HttpPost]
        [AjaxAuthorizeAttribute]
        [AjaxSessionFilterAttribute]
        public JsonResult ChangePeriodStatus(int calendarId, int yearId, int periodId, int periodControlId)
        {
            string retJSON = "";

            if (Session["ctx"] != null)
            {
                Ctx ctx = Session["ctx"] as Ctx;
                try
                {
                    VAS_196_PeriodControlMatrixModel model = new VAS_196_PeriodControlMatrixModel();
                    retJSON = JsonConvert.SerializeObject(
                        model.ChangePeriodStatus(ctx, calendarId, yearId, periodId, periodControlId));
                }
                catch (Exception ex)
                {
                    Log.Log(Level.SEVERE, "VAS_196_PeriodControlMatrixWidget.ChangePeriodStatus", ex);
                    retJSON = JsonConvert.SerializeObject(new { error = true });
                }
            }

            return Json(retJSON, JsonRequestBehavior.AllowGet);
        }
    }
}
