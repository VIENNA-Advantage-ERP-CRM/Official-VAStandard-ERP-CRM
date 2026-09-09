/******************************************************
 * Module Name    : VAS
 * Purpose        : Bank Charges dashboard widget endpoints
 * chronological  : Development
 * Created Date   : 2026-09-03
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
    /// Module Name : VAS_233_BankChargesWidget
    /// Purpose     : Thin AJAX endpoints for the Bank Charges widget. All business logic
    ///               lives in VASLogic.Models.VAS_233_BankChargesModel; these actions only
    ///               resolve the session context, pass the selected period through and
    ///               serialize the model result.
    ///
    ///               The browser sends nothing authoritative. The tenant comes from the
    ///               authenticated context; the calendar, the fiscal year, the preceding
    ///               period, the accounting schema currency and the role's organization
    ///               access are all resolved server-side. The ONE value the client does
    ///               send - the period id - is re-validated by the model before a single
    ///               C_Payment or C_BankStatementLine row is read: it must still be
    ///               active, accessible, already started and on the tenant's primary
    ///               calendar.
    ///
    ///               No exception detail reaches the browser - a failure serializes as
    ///               { error: true } and is logged with its stack trace server-side.
    /// Chronological development:
    ///   VAI154      2026-09-03 Created
    /// </summary>
    public class VAS_233_BankChargesWidgetController : Controller
    {
        private static readonly VLogger Log = VLogger.GetVLogger(typeof(VAS_233_BankChargesWidgetController).FullName);

        /// <summary>
        /// Returns the base currency, the current fiscal year's started periods, the
        /// period to preselect and that period's figures.
        /// </summary>
        /// <returns>JSON-serialized BankChargesBootstrap, "" without a session, or { error }.</returns>
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
                    VAS_233_BankChargesModel model = new VAS_233_BankChargesModel();
                    retJSON = JsonConvert.SerializeObject(model.GetBootstrap(ctx));
                }
                catch (Exception ex)
                {
                    Log.Log(Level.SEVERE, "VAS_233_BankChargesWidget.GetBootstrap", ex);
                    retJSON = JsonConvert.SerializeObject(new { error = true });
                }
            }

            return Json(retJSON, JsonRequestBehavior.AllowGet);
        }

        /// <summary>
        /// Returns the total charges, entry count and period-on-period delta of one period.
        /// </summary>
        /// <param name="periodId">C_Period_ID selected by the user.</param>
        /// <returns>JSON-serialized BankChargesData, "" without a session, or { error }.</returns>
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
                    VAS_233_BankChargesModel model = new VAS_233_BankChargesModel();
                    retJSON = JsonConvert.SerializeObject(model.GetPeriodData(ctx, periodId));
                }
                catch (Exception ex)
                {
                    Log.Log(Level.SEVERE, "VAS_233_BankChargesWidget.GetPeriodData", ex);
                    retJSON = JsonConvert.SerializeObject(new { error = true });
                }
            }

            return Json(retJSON, JsonRequestBehavior.AllowGet);
        }
    }
}
