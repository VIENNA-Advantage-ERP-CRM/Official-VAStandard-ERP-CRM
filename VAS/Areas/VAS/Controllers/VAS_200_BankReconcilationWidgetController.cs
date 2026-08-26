/******************************************************
 * Module Name    : VAS
 * Purpose        : Bank Reconciliation dashboard widget endpoints
 * chronological  : Development
 * Created Date   : 2026-08-20
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
    /// Module Name : VAS_200_BankReconcilationWidget
    /// Purpose     : Thin AJAX endpoints for the Bank Reconciliation widget. All
    ///               business logic lives in
    ///               VASLogic.Models.VAS_200_BankReconcilationModel; these actions
    ///               only resolve the session context, pass the selected ids through
    ///               and serialize the model result. The tenant always comes from the
    ///               authenticated context - the client never supplies AD_Client_ID -
    ///               and the selected period is re-validated server-side (still
    ///               active, still open, still on the primary calendar) before any
    ///               payment is read.
    ///               Three actions: bootstrap on load, the whole card on a period
    ///               change, and one page of detail when a status row or a bank
    ///               account is opened. Detail rows are never fetched with the card.
    /// Chronological development:
    ///   VAI154      2026-08-20 Created
    /// </summary>
    public class VAS_200_BankReconcilationWidgetController : Controller
    {
        private static readonly VLogger Log = VLogger.GetVLogger(typeof(VAS_200_BankReconcilationWidgetController).FullName);

        /// <summary>
        /// Returns the selectable open periods of the primary calendar, the period to
        /// preselect, and that period's status buckets and bank accounts.
        /// </summary>
        /// <returns>JSON-serialized ReconciliationBootstrap, "" without a session, or { error }.</returns>
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
                    VAS_200_BankReconcilationModel model = new VAS_200_BankReconcilationModel();
                    retJSON = JsonConvert.SerializeObject(model.GetBootstrap(ctx));
                }
                catch (Exception ex)
                {
                    Log.Log(Level.SEVERE, "VAS_200_BankReconcilationWidget.GetBootstrap", ex);
                    retJSON = JsonConvert.SerializeObject(new { error = true });
                }
            }

            return Json(retJSON, JsonRequestBehavior.AllowGet);
        }

        /// <summary>
        /// Returns the status buckets and bank accounts of one period.
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
                    VAS_200_BankReconcilationModel model = new VAS_200_BankReconcilationModel();
                    retJSON = JsonConvert.SerializeObject(model.GetPeriodData(ctx, periodId));
                }
                catch (Exception ex)
                {
                    Log.Log(Level.SEVERE, "VAS_200_BankReconcilationWidget.GetPeriodData", ex);
                    retJSON = JsonConvert.SerializeObject(new { error = true });
                }
            }

            return Json(retJSON, JsonRequestBehavior.AllowGet);
        }

        /// <summary>
        /// Returns one page of the payments behind a status bucket or behind one bank
        /// account, plus the total count. The page size is clamped by the model, so a
        /// crafted request cannot pull the whole table in one response.
        /// </summary>
        /// <param name="periodId">C_Period_ID selected by the user.</param>
        /// <param name="category">RECONCILED, UNRECONCILED, INPROGRESS or ACCOUNT.</param>
        /// <param name="bankAccountId">C_BankAccount_ID; required for ACCOUNT.</param>
        /// <param name="pageNo">1-based page number.</param>
        /// <param name="pageSize">Requested rows per page.</param>
        /// <returns>JSON-serialized PaymentPage, or { error }.</returns>
        [AjaxAuthorizeAttribute]
        [AjaxSessionFilterAttribute]
        public JsonResult GetPayments(int periodId, string category, int bankAccountId,
            int pageNo, int pageSize)
        {
            string retJSON = "";

            if (Session["ctx"] != null)
            {
                Ctx ctx = Session["ctx"] as Ctx;
                try
                {
                    VAS_200_BankReconcilationModel model = new VAS_200_BankReconcilationModel();
                    retJSON = JsonConvert.SerializeObject(
                        model.GetPayments(ctx, periodId, category, bankAccountId, pageNo, pageSize));
                }
                catch (Exception ex)
                {
                    Log.Log(Level.SEVERE, "VAS_200_BankReconcilationWidget.GetPayments", ex);
                    retJSON = JsonConvert.SerializeObject(new { error = true });
                }
            }

            return Json(retJSON, JsonRequestBehavior.AllowGet);
        }
    }
}
