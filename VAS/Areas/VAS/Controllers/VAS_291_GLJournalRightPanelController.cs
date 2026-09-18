/// <summary>
/// Module Name : VAS
/// Purpose     : Journal details right panel endpoint. Serves the
///               VAS.VAS_291_GLJournalRightPanel tab panel:
///
///                 GetJournalOverview — the read payload for the selected
///                                      GL_Journal (header, period control,
///                                      first page of lines with resolved
///                                      account and dimensions, workflow
///                                      steps, posting moment).
///                 GetJournalLines    — one further page of lines (50 per
///                                      request) for the panel's pager.
///
///               The journal id arriving from the browser is never trusted on its
///               own: the model reads GL_Journal under MRole, so an id the role
///               cannot see comes back as an empty payload. Print / download run
///               through the framework's own JsonData/GeneratePrint off the
///               hosting tab's print process, and record-open through
///               VAS_ZoomWindow — neither needs an endpoint here.
/// Chronological development:
///   VAI145   2026-09-18  Created.
/// </summary>

using Newtonsoft.Json;
using System.Web.Mvc;
using VAdvantage.Utility;
using VASLogic.Models;

namespace VAS.Controllers
{
    public class VAS_291_GLJournalRightPanelController : Controller
    {
        public ActionResult Index()
        {
            return View();
        }

        /// <summary>
        /// Returns the panel payload for the selected journal.
        /// </summary>
        /// <param name="GL_Journal_ID">Selected journal id.</param>
        /// <returns>JSON-serialized
        /// <see cref="VAS_291_GLJournalRightPanelModel.JournalPanelData"/>.</returns>
        public JsonResult GetJournalOverview(int GL_Journal_ID)
        {
            string retJSON = "";
            if (Session["ctx"] != null)
            {
                Ctx ctx = Session["ctx"] as Ctx;
                VAS_291_GLJournalRightPanelModel model = new VAS_291_GLJournalRightPanelModel();
                retJSON = JsonConvert.SerializeObject(
                    model.GetJournalOverview(ctx, GL_Journal_ID));
            }
            return Json(retJSON, JsonRequestBehavior.AllowGet);
        }

        /// <summary>
        /// One page of the journal's lines, for the Journal lines section's pager.
        /// Only the rows are returned — the count and the Dr / Cr totals came with
        /// the initial payload and do not change between pages.
        /// </summary>
        /// <param name="GL_Journal_ID">Selected journal id.</param>
        /// <param name="page">Zero-based page index.</param>
        /// <param name="pageSize">Rows per page.</param>
        /// <returns>JSON-serialized
        /// <see cref="VAS_291_GLJournalRightPanelModel.JournalLinesPage"/>.</returns>
        public JsonResult GetJournalLines(int GL_Journal_ID, int page, int pageSize)
        {
            string retJSON = "";
            if (Session["ctx"] != null)
            {
                Ctx ctx = Session["ctx"] as Ctx;
                VAS_291_GLJournalRightPanelModel model = new VAS_291_GLJournalRightPanelModel();
                retJSON = JsonConvert.SerializeObject(
                    model.GetJournalLines(ctx, GL_Journal_ID, page, pageSize));
            }
            return Json(retJSON, JsonRequestBehavior.AllowGet);
        }
    }
}
