/******************************************************
 * Module Name    : VAS
 * Purpose        : Open / Unprocessed Documents dashboard widget endpoints
 * chronological  : Development
 * Created Date   : 2026-08-21
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
    /// Module Name : VAS_197_UnProcessedDocumentsWidget
    /// Purpose     : Thin AJAX endpoints for the Open / Unprocessed Documents widget.
    ///               All business logic lives in
    ///               VASLogic.Models.VAS_197_UnProcessedDocumentsModel; these actions
    ///               only resolve the session context, pass the selected ids through
    ///               and serialize the model result. The tenant always comes from the
    ///               authenticated context - the client never supplies AD_Client_ID -
    ///               and both the selected period and the selected screen are
    ///               re-validated server-side before any document is read: the period
    ///               against the primary calendar's open periods, the screen against
    ///               what Application Dictionary discovery actually returned.
    ///               The client only ever SENDS an AD_Table_ID and an AD_Window_ID - a
    ///               card row is a screen, so the pair is its whole identity. It
    ///               receives the key column name of the screen it opened, because the
    ///               framework's Zoom needs it to build the query - but nothing the
    ///               client sends is ever used as a table name, a column name or a
    ///               record filter, so a tampered request can only name a screen the
    ///               dictionary already published.
    ///               Three actions: bootstrap on load, the screen figures on a period
    ///               change, and one page of records when a screen is opened. Records
    ///               are never fetched with the figures.
    /// Chronological development:
    ///   VAI154      2026-08-21 Created
    /// </summary>
    public class VAS_197_UnProcessedDocumentsWidgetController : Controller
    {
        private static readonly VLogger Log = VLogger.GetVLogger(typeof(VAS_197_UnProcessedDocumentsWidgetController).FullName);

        /// <summary>
        /// Returns the selectable open periods of the primary calendar, the period to
        /// preselect, and that period's open document screens.
        /// </summary>
        /// <returns>JSON-serialized UnProcessedBootstrap, "" without a session, or { error }.</returns>
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
                    VAS_197_UnProcessedDocumentsModel model = new VAS_197_UnProcessedDocumentsModel();
                    retJSON = JsonConvert.SerializeObject(model.GetBootstrap(ctx));
                }
                catch (Exception ex)
                {
                    Log.Log(Level.SEVERE, "VAS_197_UnProcessedDocumentsWidget.GetBootstrap", ex);
                    retJSON = JsonConvert.SerializeObject(new { error = true });
                }
            }

            return Json(retJSON, JsonRequestBehavior.AllowGet);
        }

        /// <summary>
        /// Returns the open document figures of one period, per screen.
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
                    VAS_197_UnProcessedDocumentsModel model = new VAS_197_UnProcessedDocumentsModel();
                    retJSON = JsonConvert.SerializeObject(model.GetPeriodData(ctx, periodId));
                }
                catch (Exception ex)
                {
                    Log.Log(Level.SEVERE, "VAS_197_UnProcessedDocumentsWidget.GetPeriodData", ex);
                    retJSON = JsonConvert.SerializeObject(new { error = true });
                }
            }

            return Json(retJSON, JsonRequestBehavior.AllowGet);
        }

        /// <summary>
        /// Returns one page of the open documents of one screen, plus the total count.
        /// The page size is clamped by the model, so a crafted request cannot pull a
        /// whole table in one response, and the table / window pair is checked against
        /// discovery, so a pair the dictionary did not hand out reaches no query at
        /// all - the screen's record filter comes from the dictionary, never from the
        /// request.
        /// </summary>
        /// <param name="periodId">C_Period_ID selected by the user.</param>
        /// <param name="tableId">AD_Table_ID of the screen opened.</param>
        /// <param name="windowId">AD_Window_ID of the screen opened.</param>
        /// <param name="pageNo">1-based page number.</param>
        /// <param name="pageSize">Requested rows per page.</param>
        /// <returns>JSON-serialized RecordPage, or { error }.</returns>
        [AjaxAuthorizeAttribute]
        [AjaxSessionFilterAttribute]
        public JsonResult GetRecords(int periodId, int tableId, int windowId, int pageNo, int pageSize)
        {
            string retJSON = "";

            if (Session["ctx"] != null)
            {
                Ctx ctx = Session["ctx"] as Ctx;
                try
                {
                    VAS_197_UnProcessedDocumentsModel model = new VAS_197_UnProcessedDocumentsModel();
                    retJSON = JsonConvert.SerializeObject(
                        model.GetRecords(ctx, periodId, tableId, windowId, pageNo, pageSize));
                }
                catch (Exception ex)
                {
                    Log.Log(Level.SEVERE, "VAS_197_UnProcessedDocumentsWidget.GetRecords", ex);
                    retJSON = JsonConvert.SerializeObject(new { error = true });
                }
            }

            return Json(retJSON, JsonRequestBehavior.AllowGet);
        }
    }
}
