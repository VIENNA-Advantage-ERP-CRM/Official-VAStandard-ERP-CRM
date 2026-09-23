/// <summary>
/// Module Name : VAS
/// Purpose     : Bank Account right panel endpoint. Serves the
///               VAS.VAS_293_BankAccountRightPanel tab panel:
///
///                 GetAccountOverview — the read payload for the selected
///                                      C_BankAccount (identity + bank + address +
///                                      currency, financial position, the latest
///                                      account line with the reconciliation state,
///                                      and one linked-configuration row per ACTIVE
///                                      child tab of the hosting window).
///
///               One endpoint is enough: the panel paints in a single pass and has
///               nothing it pages or reloads section by section.
///
///               Neither id arriving from the browser is trusted on its own. The
///               model reads C_BankAccount under MRole, so an account id the role
///               cannot see comes back as an empty payload; AD_Window_ID only ever
///               narrows which of the six known configuration tables may be listed,
///               and reaches SQL as a bound parameter. The row click itself (switch
///               the hosting window to that tab) runs on the client through the
///               framework's own tab change - it needs no endpoint here.
/// Chronological development:
///   VAI145   2026-09-22  Created.
/// </summary>

using Newtonsoft.Json;
using System.Web.Mvc;
using VAdvantage.Utility;
using VASLogic.Models;

namespace VAS.Controllers
{
    public class VAS_293_BankAccountRightPanelController : Controller
    {
        public ActionResult Index()
        {
            return View();
        }

        /// <summary>
        /// Returns the panel payload for the selected bank account.
        /// </summary>
        /// <param name="C_BankAccount_ID">Selected bank account id.</param>
        /// <param name="AD_Window_ID">Hosting window, read from the panel's own tab on
        /// the client so no window id is ever hard-coded. 0 leaves the Linked
        /// Configuration section out.</param>
        /// <returns>JSON-serialized
        /// <see cref="VAS_293_BankAccountRightPanelModel.BankAccountPanelData"/>.</returns>
        public JsonResult GetAccountOverview(int C_BankAccount_ID, int AD_Window_ID)
        {
            string retJSON = "";
            if (Session["ctx"] != null)
            {
                Ctx ctx = Session["ctx"] as Ctx;
                VAS_293_BankAccountRightPanelModel model = new VAS_293_BankAccountRightPanelModel();
                retJSON = JsonConvert.SerializeObject(
                    model.GetAccountOverview(ctx, C_BankAccount_ID, AD_Window_ID));
            }
            return Json(retJSON, JsonRequestBehavior.AllowGet);
        }
    }
}
