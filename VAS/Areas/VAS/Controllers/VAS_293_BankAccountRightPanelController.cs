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
///                 GetBankAccountDocDetail — rows of the C_BankAccountDoc inline
///                                           detail table.
///                 GetStatementClassDetail — rows of the VA012_BankStatementClass
///                                           inline detail table.
///
///               The overview paints in a single pass. The two detail endpoints are
///               separate on purpose: their rows are only wanted once the user
///               expands that configuration row, and expanding one must not reload
///               the panel. The client caches each answer per account, so a row
///               that is opened, closed and opened again costs one request.
///
///               No id arriving from the browser is trusted on its own. Every read
///               goes through MRole on its own main table, so an account - or a
///               configuration row - the role cannot see comes back empty;
///               AD_Window_ID only ever narrows which of the six known configuration
///               tables may be listed. Every id reaches SQL as a bound parameter.
///               The row click that SWITCHES TAB (the four rows that navigate) runs
///               on the client through the framework's own tab change - it needs no
///               endpoint here.
/// Chronological development:
///   VAI145   2026-09-22  Created.
///   VAI145   2026-09-23  Inline detail endpoints added for the two configuration
///                        rows that expand in place.
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

        /// <summary>
        /// Returns the Bank Account Document rows behind the configuration row of the
        /// same name, for the panel's inline detail table. Called only when the user
        /// expands that row.
        /// </summary>
        /// <param name="C_BankAccount_ID">Selected bank account id.</param>
        /// <returns>JSON-serialized
        /// <see cref="VAS_293_BankAccountRightPanelModel.ConfigDetail{T}"/> of
        /// <see cref="VAS_293_BankAccountRightPanelModel.BankAccountDocDetailRow"/>.</returns>
        public JsonResult GetBankAccountDocDetail(int C_BankAccount_ID)
        {
            string retJSON = "";
            if (Session["ctx"] != null)
            {
                Ctx ctx = Session["ctx"] as Ctx;
                VAS_293_BankAccountRightPanelModel model = new VAS_293_BankAccountRightPanelModel();
                retJSON = JsonConvert.SerializeObject(
                    model.GetBankAccountDocDetail(ctx, C_BankAccount_ID));
            }
            return Json(retJSON, JsonRequestBehavior.AllowGet);
        }

        /// <summary>
        /// Returns the Statement Class rows behind the configuration row of the same
        /// name, for the panel's inline detail table. Called only when the user
        /// expands that row.
        /// </summary>
        /// <param name="C_BankAccount_ID">Selected bank account id.</param>
        /// <returns>JSON-serialized
        /// <see cref="VAS_293_BankAccountRightPanelModel.ConfigDetail{T}"/> of
        /// <see cref="VAS_293_BankAccountRightPanelModel.StatementClassDetailRow"/>.</returns>
        public JsonResult GetStatementClassDetail(int C_BankAccount_ID)
        {
            string retJSON = "";
            if (Session["ctx"] != null)
            {
                Ctx ctx = Session["ctx"] as Ctx;
                VAS_293_BankAccountRightPanelModel model = new VAS_293_BankAccountRightPanelModel();
                retJSON = JsonConvert.SerializeObject(
                    model.GetStatementClassDetail(ctx, C_BankAccount_ID));
            }
            return Json(retJSON, JsonRequestBehavior.AllowGet);
        }
    }
}
