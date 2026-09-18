/******************************************************
 * Module Name    : VAS
 * Purpose        : Bank Balance dashboard widget endpoints
 * chronological  : Development
 * Created Date   : 2026-09-04
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
    /// Module Name : VAS_230_BankBalanceWidget
    /// Purpose     : Thin AJAX endpoint for the Bank Balance widget. All business logic lives
    ///               in VASLogic.Models.VAS_230_BankBalanceModel; this action only resolves the
    ///               session context and serializes the result.
    ///
    ///               The browser sends ONE value and it is not authoritative: a bank account
    ///               id. The model honours it only if it appears in the role's own
    ///               MRole-filtered account list, so editing the request payload can select
    ///               another account the user was already entitled to and nothing else. The
    ///               tenant, the as-of date and the organisation access are resolved
    ///               server-side.
    ///
    ///               ONE call per paint - the initial load, a Refresh and an account change
    ///               alike. The response carries the balance AND the selector's options,
    ///               because the pill must never be able to name an account the figure below
    ///               it is not for.
    ///
    ///               No exception detail reaches the browser - a failure serializes as
    ///               { error: true } and is logged with its stack trace server-side.
    /// Chronological development:
    ///   VAI154      2026-09-04 Created
    /// </summary>
    public class VAS_230_BankBalanceWidgetController : Controller
    {
        private static readonly VLogger Log = VLogger.GetVLogger(typeof(VAS_230_BankBalanceWidgetController).FullName);

        /// <summary>
        /// Returns the selected account's latest balance and the accounts the selector can
        /// offer.
        /// </summary>
        /// <param name="bankAccountId">C_BankAccount_ID to report on, or 0 for the first
        /// accessible account. Validated by the model against the role's own account list.</param>
        /// <returns>JSON-serialized BankBalanceResult, "" without a session, or { error }.</returns>
        [AjaxAuthorizeAttribute]
        [AjaxSessionFilterAttribute]
        public JsonResult GetBankBalance(int bankAccountId = 0)
        {
            string retJSON = "";

            if (Session["ctx"] != null)
            {
                Ctx ctx = Session["ctx"] as Ctx;
                try
                {
                    VAS_230_BankBalanceModel model = new VAS_230_BankBalanceModel();
                    retJSON = JsonConvert.SerializeObject(model.GetBankBalance(ctx, bankAccountId));
                }
                catch (Exception ex)
                {
                    Log.Log(Level.SEVERE, "VAS_230_BankBalanceWidget.GetBankBalance", ex);
                    retJSON = JsonConvert.SerializeObject(new { error = true });
                }
            }

            return Json(retJSON, JsonRequestBehavior.AllowGet);
        }
    }
}
