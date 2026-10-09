/******************************************************
 * Module Name    : VAS
 * Purpose        : Unbudgeted Actuals dashboard widget endpoints
 * chronological  : Development
 * Created Date   : 2026-09-08
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
    /// Module Name : VAS_256_UnBudgetedActualWidget
    /// Purpose     : Thin AJAX endpoints for the Unbudgeted Actuals widget. All business
    ///               logic lives in VASLogic.Models.VAS_256_UnBudgetedActualModel; these
    ///               actions only resolve the session context and serialize the model
    ///               result.
    ///
    ///               NOTHING THE BROWSER SENDS IS AUTHORITATIVE. The client supplies a
    ///               financial year, an account, a transaction organization and a page
    ///               position; the model re-resolves the year against the tenant's own
    ///               primary calendar, refuses an account that is not an active Expense
    ///               account, clamps both page numbers, and takes the tenant, the primary
    ///               accounting schema and the role's organization access from the server
    ///               side alone. An id the role cannot see returns nothing rather than
    ///               someone else's ledger.
    ///
    ///               GetRows is ONE call per load, per year change and per page turn: the
    ///               response carries the page, the year list, the total row count and the
    ///               total amount, because the last three are properties of the whole set
    ///               that a single page could not work out for itself.
    ///
    ///               No exception detail reaches the browser - a failure serializes as
    ///               { error: true } and is logged with its stack trace server-side.
    /// Chronological development:
    ///   VAI154      2026-09-08 Created
    /// </summary>
    public class VAS_256_UnBudgetedActualWidgetController : Controller
    {
        private static readonly VLogger Log = VLogger.GetVLogger(typeof(VAS_256_UnBudgetedActualWidgetController).FullName);

        /// <summary>
        /// Returns one page of unbudgeted Account / Dimension rows, the financial years
        /// the filter can offer, the pager's total and the subtitle's total amount.
        /// </summary>
        /// <param name="yearId">C_Year_ID to read, or 0 to default to the financial year
        /// containing today.</param>
        /// <param name="pageNo">1-based page; clamped by the model.</param>
        /// <param name="pageSize">Rows per page; clamped by the model to [1,12].</param>
        /// <returns>JSON-serialized UnbudgetedResult, "" without a session, or { error }.</returns>
        [AjaxAuthorizeAttribute]
        [AjaxSessionFilterAttribute]
        public JsonResult GetRows(int yearId = 0, int pageNo = 1,
            int pageSize = VAS_256_UnBudgetedActualModel.DEFAULT_PageSize)
        {
            string retJSON = "";

            if (Session["ctx"] != null)
            {
                Ctx ctx = Session["ctx"] as Ctx;
                try
                {
                    VAS_256_UnBudgetedActualModel model = new VAS_256_UnBudgetedActualModel();
                    retJSON = JsonConvert.SerializeObject(
                        model.GetRows(ctx, yearId, pageNo, pageSize));
                }
                catch (Exception ex)
                {
                    Log.Log(Level.SEVERE, "VAS_256_UnBudgetedActualWidget.GetRows", ex);
                    retJSON = JsonConvert.SerializeObject(new { error = true });
                }
            }

            return Json(retJSON, JsonRequestBehavior.AllowGet);
        }

        /// <summary>
        /// Returns one page of the Actual postings behind a single widget row, under
        /// exactly the filters that row was built from.
        /// </summary>
        /// <param name="yearId">C_Year_ID the row was read for.</param>
        /// <param name="accountId">C_ElementValue_ID of the row's account.</param>
        /// <param name="orgTrxId">AD_OrgTrx_ID of the row; ignored when isOrgTrxNull is Y.</param>
        /// <param name="isOrgTrxNull">"Y" when the row's dimension is "no transaction
        /// organization" rather than a real one - AD_Org_ID 0 is a REAL organization
        /// ('*'), so the two cases cannot share one value.</param>
        /// <param name="pageNo">1-based page; clamped by the model.</param>
        /// <param name="pageSize">Rows per page; clamped by the model to [1,100].</param>
        /// <returns>JSON-serialized TransactionPage, "" without a session, or { error }.</returns>
        [AjaxAuthorizeAttribute]
        [AjaxSessionFilterAttribute]
        public JsonResult GetTransactions(int yearId = 0, int accountId = 0, int orgTrxId = 0,
            string isOrgTrxNull = "N", int pageNo = 1,
            int pageSize = VAS_256_UnBudgetedActualModel.DEFAULT_TrxPageSize)
        {
            string retJSON = "";

            if (Session["ctx"] != null)
            {
                Ctx ctx = Session["ctx"] as Ctx;
                try
                {
                    bool orgTrxIsNull = "Y".Equals(isOrgTrxNull, StringComparison.OrdinalIgnoreCase);

                    VAS_256_UnBudgetedActualModel model = new VAS_256_UnBudgetedActualModel();
                    retJSON = JsonConvert.SerializeObject(
                        model.GetTransactions(ctx, yearId, accountId, orgTrxId, orgTrxIsNull,
                            pageNo, pageSize));
                }
                catch (Exception ex)
                {
                    Log.Log(Level.SEVERE, "VAS_256_UnBudgetedActualWidget.GetTransactions", ex);
                    retJSON = JsonConvert.SerializeObject(new { error = true });
                }
            }

            return Json(retJSON, JsonRequestBehavior.AllowGet);
        }
    }
}
