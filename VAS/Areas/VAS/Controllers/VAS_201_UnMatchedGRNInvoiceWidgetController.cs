/******************************************************
 * Module Name    : VAS
 * Purpose        : Unmatched GRNs & Invoices dashboard widget endpoints
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
    /// Module Name : VAS_201_UnMatchedGRNInvoiceWidget
    /// Purpose     : Thin AJAX endpoints for the Unmatched GRNs & Invoices widget.
    ///               All business logic lives in
    ///               VASLogic.Models.VAS_201_UnMatchedGRNInvoiceModel; these actions
    ///               only resolve the session context, pass the selected ids through
    ///               and serialize the model result. The tenant always comes from the
    ///               authenticated context - the client never supplies AD_Client_ID -
    ///               and the selected period is re-validated server-side (still
    ///               active, still open, still on the primary calendar) before any
    ///               document is read.
    ///               Three actions: bootstrap on load, the three exception figures on
    ///               a period change, and one page of records when an exception is
    ///               opened. Records are never fetched with the figures.
    /// Chronological development:
    ///   VAI154      2026-08-20 Created
    /// </summary>
    public class VAS_201_UnMatchedGRNInvoiceWidgetController : Controller
    {
        private static readonly VLogger Log = VLogger.GetVLogger(typeof(VAS_201_UnMatchedGRNInvoiceWidgetController).FullName);

        /// <summary>
        /// Returns the selectable open periods of the primary calendar, the period to
        /// preselect, and that period's three exception figures.
        /// </summary>
        /// <returns>JSON-serialized MatchBootstrap, "" without a session, or { error }.</returns>
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
                    VAS_201_UnMatchedGRNInvoiceModel model = new VAS_201_UnMatchedGRNInvoiceModel();
                    retJSON = JsonConvert.SerializeObject(model.GetBootstrap(ctx));
                }
                catch (Exception ex)
                {
                    Log.Log(Level.SEVERE, "VAS_201_UnMatchedGRNInvoiceWidget.GetBootstrap", ex);
                    retJSON = JsonConvert.SerializeObject(new { error = true });
                }
            }

            return Json(retJSON, JsonRequestBehavior.AllowGet);
        }

        /// <summary>
        /// Returns the three exception figures of one period.
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
                    VAS_201_UnMatchedGRNInvoiceModel model = new VAS_201_UnMatchedGRNInvoiceModel();
                    retJSON = JsonConvert.SerializeObject(model.GetPeriodData(ctx, periodId));
                }
                catch (Exception ex)
                {
                    Log.Log(Level.SEVERE, "VAS_201_UnMatchedGRNInvoiceWidget.GetPeriodData", ex);
                    retJSON = JsonConvert.SerializeObject(new { error = true });
                }
            }

            return Json(retJSON, JsonRequestBehavior.AllowGet);
        }

        /// <summary>
        /// Returns one page of the documents behind an exception, plus the total
        /// count. The page size is clamped by the model, so a crafted request cannot
        /// pull a whole table in one response.
        /// </summary>
        /// <param name="periodId">C_Period_ID selected by the user.</param>
        /// <param name="category">GRNNOTINV, INVNOGRN or MISMATCH.</param>
        /// <param name="pageNo">1-based page number.</param>
        /// <param name="pageSize">Requested rows per page.</param>
        /// <returns>JSON-serialized RecordPage, or { error }.</returns>
        [AjaxAuthorizeAttribute]
        [AjaxSessionFilterAttribute]
        public JsonResult GetRecords(int periodId, string category, int pageNo, int pageSize)
        {
            string retJSON = "";

            if (Session["ctx"] != null)
            {
                Ctx ctx = Session["ctx"] as Ctx;
                try
                {
                    VAS_201_UnMatchedGRNInvoiceModel model = new VAS_201_UnMatchedGRNInvoiceModel();
                    retJSON = JsonConvert.SerializeObject(
                        model.GetRecords(ctx, periodId, category, pageNo, pageSize));
                }
                catch (Exception ex)
                {
                    Log.Log(Level.SEVERE, "VAS_201_UnMatchedGRNInvoiceWidget.GetRecords", ex);
                    retJSON = JsonConvert.SerializeObject(new { error = true });
                }
            }

            return Json(retJSON, JsonRequestBehavior.AllowGet);
        }
    }
}
