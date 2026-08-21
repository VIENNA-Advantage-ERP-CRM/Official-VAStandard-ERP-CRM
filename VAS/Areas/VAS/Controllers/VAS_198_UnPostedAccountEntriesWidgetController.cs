/******************************************************
 * Module Name    : VAS
 * Purpose        : Unposted Accounting Entries dashboard widget endpoints
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
    /// Module Name : VAS_198_UnPostedAccountEntriesWidget
    /// Purpose     : Thin AJAX endpoints for the Unposted Accounting Entries widget.
    ///               All business logic lives in
    ///               VASLogic.Models.VAS_198_UnPostedAccountEntriesModel; these actions
    ///               only resolve the session context, pass the selected ids through
    ///               and serialize the model result. The tenant always comes from the
    ///               authenticated context - the client never supplies AD_Client_ID -
    ///               and both the selected period and the selected transaction type
    ///               are re-validated server-side before any document is read: the
    ///               period against the primary calendar's open periods, the type
    ///               against what Application Dictionary discovery actually returned.
    ///               The client only ever SENDS an AD_Table_ID and an AD_Window_ID -
    ///               a card row is a screen, so the pair is its whole identity. It
    ///               receives the key column name of the screen it opened, because the
    ///               framework's Zoom needs it to build the query - but nothing the
    ///               client sends is ever used as a table name, a column name or a
    ///               record filter, so a tampered request can only name a screen the
    ///               dictionary already published.
    ///               Three actions: bootstrap on load, the transaction-type figures on
    ///               a period change, and one page of records when a type is opened.
    ///               Records are never fetched with the figures.
    /// Chronological development:
    ///   VAI154      2026-08-21 Created
    /// </summary>
    public class VAS_198_UnPostedAccountEntriesWidgetController : Controller
    {
        private static readonly VLogger Log = VLogger.GetVLogger(typeof(VAS_198_UnPostedAccountEntriesWidgetController).FullName);

        /// <summary>
        /// Returns the selectable open periods of the primary calendar, the period to
        /// preselect, and that period's unposted transaction types.
        /// </summary>
        /// <returns>JSON-serialized UnPostedBootstrap, "" without a session, or { error }.</returns>
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
                    VAS_198_UnPostedAccountEntriesModel model = new VAS_198_UnPostedAccountEntriesModel();
                    retJSON = JsonConvert.SerializeObject(model.GetBootstrap(ctx));
                }
                catch (Exception ex)
                {
                    Log.Log(Level.SEVERE, "VAS_198_UnPostedAccountEntriesWidget.GetBootstrap", ex);
                    retJSON = JsonConvert.SerializeObject(new { error = true });
                }
            }

            return Json(retJSON, JsonRequestBehavior.AllowGet);
        }

        /// <summary>
        /// Returns the unposted transaction-type figures of one period.
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
                    VAS_198_UnPostedAccountEntriesModel model = new VAS_198_UnPostedAccountEntriesModel();
                    retJSON = JsonConvert.SerializeObject(model.GetPeriodData(ctx, periodId));
                }
                catch (Exception ex)
                {
                    Log.Log(Level.SEVERE, "VAS_198_UnPostedAccountEntriesWidget.GetPeriodData", ex);
                    retJSON = JsonConvert.SerializeObject(new { error = true });
                }
            }

            return Json(retJSON, JsonRequestBehavior.AllowGet);
        }

        /// <summary>
        /// Returns one page of the unposted documents of one transaction type, plus
        /// the total count. The page size is clamped by the model, so a crafted
        /// request cannot pull a whole table in one response, and the table / window
        /// pair is checked against discovery, so a pair the dictionary did not hand
        /// out reaches no query at all - the screen's record filter comes from the
        /// dictionary, never from the request.
        /// </summary>
        /// <param name="periodId">C_Period_ID selected by the user.</param>
        /// <param name="tableId">AD_Table_ID of the transaction type opened.</param>
        /// <param name="windowId">AD_Window_ID of the row opened - a card row is a
        /// SCREEN, so the table alone does not identify it.</param>
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
                    VAS_198_UnPostedAccountEntriesModel model = new VAS_198_UnPostedAccountEntriesModel();
                    retJSON = JsonConvert.SerializeObject(
                        model.GetRecords(ctx, periodId, tableId, windowId, pageNo, pageSize));
                }
                catch (Exception ex)
                {
                    Log.Log(Level.SEVERE, "VAS_198_UnPostedAccountEntriesWidget.GetRecords", ex);
                    retJSON = JsonConvert.SerializeObject(new { error = true });
                }
            }

            return Json(retJSON, JsonRequestBehavior.AllowGet);
        }
    }
}
