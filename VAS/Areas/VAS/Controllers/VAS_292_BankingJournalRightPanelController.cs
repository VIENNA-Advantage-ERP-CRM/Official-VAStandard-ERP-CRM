/// <summary>
/// Module Name : VAS
/// Purpose     : Banking Journal right panel endpoint. Serves the
///               VAS.VAS_292_BankingJournalRightPanel tab panel:
///
///                 GetJournalOverview — the read payload for the selected
///                                      C_BankStatement (header + bank account +
///                                      currency, line aggregate, first page of
///                                      lines, Fact_Acct impact when posted,
///                                      workflow steps + posting moment for the
///                                      audit trail).
///                 GetJournalLines    — one further page of lines (20 per
///                                      request) for the panel's pager.
///
///               The statement id arriving from the browser is never trusted on
///               its own: the model reads C_BankStatement under MRole, so an id the
///               role cannot see comes back as an empty payload. The navigation
///               actions (View account, View all lines, View accounting) run on
///               the client through the framework's own zoom, tab switch and
///               account viewer - none needs an endpoint here.
/// Chronological development:
///   VAI145   2026-09-21  Created.
///   VAI145   2026-09-21  GetJournalLines added for server-side paging.
/// </summary>

using Newtonsoft.Json;
using System.Web.Mvc;
using VAdvantage.Utility;
using VASLogic.Models;

namespace VAS.Controllers
{
    public class VAS_292_BankingJournalRightPanelController : Controller
    {
        public ActionResult Index()
        {
            return View();
        }

        /// <summary>
        /// Returns the panel payload for the selected banking journal.
        /// </summary>
        /// <param name="C_BankStatement_ID">Selected banking journal id.</param>
        /// <returns>JSON-serialized
        /// <see cref="VAS_292_BankingJournalRightPanelModel.BankingJournalPanelData"/>.</returns>
        public JsonResult GetJournalOverview(int C_BankStatement_ID)
        {
            string retJSON = "";
            if (Session["ctx"] != null)
            {
                Ctx ctx = Session["ctx"] as Ctx;
                VAS_292_BankingJournalRightPanelModel model = new VAS_292_BankingJournalRightPanelModel();
                retJSON = JsonConvert.SerializeObject(
                    model.GetJournalOverview(ctx, C_BankStatement_ID));
            }
            return Json(retJSON, JsonRequestBehavior.AllowGet);
        }

        /// <summary>
        /// One page of the statement's lines, for the Journal lines section's pager.
        /// Only the rows are returned — the count and the totals came with the
        /// initial payload and do not change between pages.
        /// </summary>
        /// <param name="C_BankStatement_ID">Selected banking journal id.</param>
        /// <param name="page">Zero-based page index.</param>
        /// <param name="pageSize">Rows per page.</param>
        /// <returns>JSON-serialized
        /// <see cref="VAS_292_BankingJournalRightPanelModel.JournalLinesPage"/>.</returns>
        public JsonResult GetJournalLines(int C_BankStatement_ID, int page, int pageSize)
        {
            string retJSON = "";
            if (Session["ctx"] != null)
            {
                Ctx ctx = Session["ctx"] as Ctx;
                VAS_292_BankingJournalRightPanelModel model = new VAS_292_BankingJournalRightPanelModel();
                retJSON = JsonConvert.SerializeObject(
                    model.GetJournalLines(ctx, C_BankStatement_ID, page, pageSize));
            }
            return Json(retJSON, JsonRequestBehavior.AllowGet);
        }
    }
}
