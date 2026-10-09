/******************************************************
 * Module Name    : VAS
 * Purpose        : Unreconciled Bank Lines dashboard widget endpoints
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
    /// Module Name : VAS_238_UnreconciledBankLineWidget
    /// Purpose     : Thin AJAX endpoint for the Unreconciled Bank Lines widget. All
    ///               business logic lives in
    ///               VASLogic.Models.VAS_238_UnreconciledBankLineModel; this action only
    ///               resolves the session context and serializes the model result.
    ///
    ///               The browser sends three values and none of them is authoritative: a
    ///               bank account id, a page number and a page size. The model clamps the
    ///               last two, and the account id is only ever an extra equality on top of
    ///               the tenant filter and MRole's own access clause - an id the role
    ///               cannot see returns nothing rather than someone else's lines. The
    ///               tenant, today's date, the unreconciled predicate and the role's
    ///               organization access are all resolved server-side. Row labels carry a
    ///               masked account tail; the filter's options carry the number in full, so
    ///               one account can be told from another at the same bank - and only
    ///               accounts the role may see are ever listed.
    ///
    ///               ONE call per page or filter change. The response carries the page, the
    ///               total row count and the filter's options, because the last two are
    ///               properties of the whole set that a single page could not work out for
    ///               itself.
    ///
    ///               No exception detail reaches the browser - a failure serializes as
    ///               { error: true } and is logged with its stack trace server-side.
    /// Chronological development:
    ///   VAI154      2026-09-03 Created
    /// </summary>
    public class VAS_238_UnreconciledBankLineWidgetController : Controller
    {
        private static readonly VLogger Log = VLogger.GetVLogger(typeof(VAS_238_UnreconciledBankLineWidgetController).FullName);

        /// <summary>
        /// Returns one page of unreconciled statement lines, the total the pager reports,
        /// and the accounts the filter can offer.
        /// </summary>
        /// <param name="bankAccountId">C_BankAccount_ID to restrict to, or 0 for all.</param>
        /// <param name="pageNo">1-based page; clamped by the model.</param>
        /// <param name="pageSize">Rows per page; clamped by the model to [1,12].</param>
        /// <returns>JSON-serialized UnreconciledResult, "" without a session, or { error }.</returns>
        [AjaxAuthorizeAttribute]
        [AjaxSessionFilterAttribute]
        public JsonResult GetLines(int bankAccountId = 0, int pageNo = 1,
            int pageSize = VAS_238_UnreconciledBankLineModel.DEFAULT_PageSize)
        {
            string retJSON = "";

            if (Session["ctx"] != null)
            {
                Ctx ctx = Session["ctx"] as Ctx;
                try
                {
                    VAS_238_UnreconciledBankLineModel model = new VAS_238_UnreconciledBankLineModel();
                    retJSON = JsonConvert.SerializeObject(
                        model.GetLines(ctx, bankAccountId, pageNo, pageSize));
                }
                catch (Exception ex)
                {
                    Log.Log(Level.SEVERE, "VAS_238_UnreconciledBankLineWidget.GetLines", ex);
                    retJSON = JsonConvert.SerializeObject(new { error = true });
                }
            }

            return Json(retJSON, JsonRequestBehavior.AllowGet);
        }
    }
}
