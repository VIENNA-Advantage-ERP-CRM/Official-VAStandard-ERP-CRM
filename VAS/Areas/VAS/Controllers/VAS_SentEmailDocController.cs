/*******************************************************
 * Module Name    : VAS_Standard
 * Purpose        : Resolve recipient (Name + EMail) for the
 *                  VA112 PrintViewer share/email panel from
 *                  any record (AD_Table_ID + RecordID).
 * Class          : VAS_SentEmailDocController
 * Chronological Development
 * Created Date   : 08-May-2026
 ******************************************************/
using Newtonsoft.Json;
using System.Web.Mvc;
using VAdvantage.Utility;
using VASLogic.Models;

namespace VAS.Areas.VAS.Controllers
{
    /// <summary>
    /// Backend for the VAS_SentEmailDoc reusable form. Returns the
    /// recipient (Name, EMailID) the caller should pre-fill into the
    /// VA112 share/email panel. If the caller already has both values
    /// the model short-circuits and returns them unchanged.
    /// </summary>
    public class VAS_SentEmailDocController : Controller
    {
        // GET: VAS/VAS_SentEmailDoc
        public ActionResult Index()
        {
            return View();
        }

        /// <summary>
        /// Resolve recipient Name + EMail for a record. When Name or
        /// EMailID are blank, the model walks the source table's
        /// C_BPartner_ID to AD_User and fills in the missing pieces.
        /// </summary>
        public JsonResult GetRecipientInfo(int AD_Table_ID, int RecordID, string Name, string EMailID)
        {
            string retJSON = "";
            if (Session["ctx"] != null)
            {
                Ctx ctx = Session["ctx"] as Ctx;
                VAS_SentEmailDocModel obj = new VAS_SentEmailDocModel();
                retJSON = JsonConvert.SerializeObject(obj.GetRecipient(ctx, AD_Table_ID, RecordID, Name, EMailID));
            }
            return Json(retJSON, JsonRequestBehavior.AllowGet);
        }
    }
}
