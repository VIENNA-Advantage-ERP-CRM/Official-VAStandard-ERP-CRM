/******************************************************
 * Module Name    : VAS
 * Purpose        : Utilization by Dimension dashboard widget endpoints
 * chronological  : Development
 * Created Date   : 2026-09-09
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
    /// Module Name : VAS_253_UtlizationbyDimensionWidget
    /// Purpose     : Thin AJAX endpoint for the Utilization by Dimension widget. All business
    ///               logic lives in VASLogic.Models.VAS_253_UtlizationbyDimensionModel; this
    ///               action only resolves the session context and serializes the model
    ///               result.
    ///
    ///               NOTHING THE BROWSER SENDS IS AUTHORITATIVE. The client supplies a
    ///               financial year, an accounting dimension and a page position; the model
    ///               re-resolves the year against the tenant's own primary calendar, the
    ///               dimension against the primary accounting schema's own active elements,
    ///               clamps the page numbers, and takes the tenant, the accounting schema and
    ///               the role's organization access from the server side alone. A year id or
    ///               an element type the tenant does not have falls back to the default
    ///               rather than reaching Fact_Acct - and the dimension never becomes part of
    ///               a column name until the model has matched it against that list.
    ///
    ///               ONE call per load, per filter change and per page turn: the response
    ///               carries the page, the year list, the dimension list and the total row
    ///               count, because everything after the first is a property of the whole set
    ///               that a single page could not work out for itself.
    ///
    ///               No exception detail reaches the browser - a failure serializes as
    ///               { error: true } and is logged with its stack trace server-side.
    /// Chronological development:
    ///   VAI154      2026-09-09 Created
    /// </summary>
    public class VAS_253_UtlizationbyDimensionWidgetController : Controller
    {
        private static readonly VLogger Log = VLogger.GetVLogger(typeof(VAS_253_UtlizationbyDimensionWidgetController).FullName);

        /// <summary>
        /// Returns one page of dimension values ranked by budget utilization, the financial
        /// years and accounting dimensions the filters can offer, and the pager's total.
        /// </summary>
        /// <param name="yearId">C_Year_ID to read, or 0 to default to the financial year
        /// containing today.</param>
        /// <param name="dimension">C_AcctSchema_Element.ElementType to group by, or empty to
        /// default to the schema's first element in SeqNo order.</param>
        /// <param name="pageNo">1-based page; clamped by the model.</param>
        /// <param name="pageSize">Rows per page; clamped by the model to [1,12].</param>
        /// <returns>JSON-serialized UtilizationResult, "" without a session, or { error }.</returns>
        [AjaxAuthorizeAttribute]
        [AjaxSessionFilterAttribute]
        public JsonResult GetRows(int yearId = 0, string dimension = "", int pageNo = 1,
            int pageSize = VAS_253_UtlizationbyDimensionModel.DEFAULT_PageSize)
        {
            string retJSON = "";

            if (Session["ctx"] != null)
            {
                Ctx ctx = Session["ctx"] as Ctx;
                try
                {
                    VAS_253_UtlizationbyDimensionModel model = new VAS_253_UtlizationbyDimensionModel();
                    retJSON = JsonConvert.SerializeObject(
                        model.GetRows(ctx, yearId, dimension, pageNo, pageSize));
                }
                catch (Exception ex)
                {
                    Log.Log(Level.SEVERE, "VAS_253_UtlizationbyDimensionWidget.GetRows", ex);
                    retJSON = JsonConvert.SerializeObject(new { error = true });
                }
            }

            return Json(retJSON, JsonRequestBehavior.AllowGet);
        }
    }
}
