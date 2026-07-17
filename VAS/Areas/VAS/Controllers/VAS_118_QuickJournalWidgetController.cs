/******************************************************
 * Module Name    : VAS
 * Purpose        : Quick GL Journal dashboard widget endpoints (Widget VAS_118)
 * chronological  : Development
 * Created Date   : 2026-07-17
 * Created by     : VAI_145
 ******************************************************/

using Newtonsoft.Json;
using System.Globalization;
using System.Web.Mvc;
using VAdvantage.Utility;
using VASLogic.Models;
using VIS.Filters;

namespace VAS.Controllers
{
    /// <summary>
    /// Module Name : VAS_118_QuickJournal
    /// Purpose     : Thin AJAX endpoints for the Quick GL Journal dashboard
    ///               widget. All lookups and the create/post business logic live
    ///               in VASLogic.Models.VAS_118_QuickJournalModel; this controller
    ///               only resolves the session context, does light input shaping
    ///               (invariant decimal parse for the amount) and serializes the
    ///               model result. Reads are GET; the create is HttpPost.
    /// Chronological development:
    ///   VAI_145     2026-07-17 Created
    /// </summary>
    public class VAS_118_QuickJournalWidgetController : Controller
    {
        /// <summary>
        /// One round-trip payload for opening the modal: role-accessible
        /// organizations, active accounting schemas (with currency meta),
        /// GL-Journal document types and the seed defaults (org / schema / date).
        /// </summary>
        /// <returns>JSON-serialized InitData, or "" when no session.</returns>
        [AjaxAuthorizeAttribute]
        [AjaxSessionFilterAttribute]
        public JsonResult GetInitData()
        {
            string retJSON = "";
            if (Session["ctx"] != null)
            {
                Ctx ctx = Session["ctx"] as Ctx;
                VAS_118_QuickJournalModel model = new VAS_118_QuickJournalModel();
                retJSON = JsonConvert.SerializeObject(model.GetInitData(ctx));
            }
            return Json(retJSON, JsonRequestBehavior.AllowGet);
        }

        /// <summary>
        /// Active, non-summary ledger accounts of the given accounting schema's
        /// account element, optionally filtered by a code/name search and paged.
        /// Backs both the debit and the credit picker.
        /// </summary>
        /// <param name="cAcctSchemaId">C_AcctSchema_ID whose accounts are listed.</param>
        /// <param name="search">Optional filter on account code / name.</param>
        /// <param name="pageNo">1-based page number.</param>
        /// <returns>JSON-serialized account list, or "" when no session.</returns>
        [AjaxAuthorizeAttribute]
        [AjaxSessionFilterAttribute]
        public JsonResult GetAccounts(int cAcctSchemaId = 0, string search = "", int pageNo = 1)
        {
            string retJSON = "";
            if (Session["ctx"] != null)
            {
                Ctx ctx = Session["ctx"] as Ctx;
                VAS_118_QuickJournalModel model = new VAS_118_QuickJournalModel();
                retJSON = JsonConvert.SerializeObject(model.GetAccounts(ctx, cAcctSchemaId, search, pageNo));
            }
            return Json(retJSON, JsonRequestBehavior.AllowGet);
        }

        /// <summary>
        /// Cost/profit-center organizations accessible to the role (the optional
        /// AD_OrgTrx_ID picker).
        /// </summary>
        /// <returns>JSON-serialized org list, or "" when no session.</returns>
        [AjaxAuthorizeAttribute]
        [AjaxSessionFilterAttribute]
        public JsonResult GetCostCenters()
        {
            string retJSON = "";
            if (Session["ctx"] != null)
            {
                Ctx ctx = Session["ctx"] as Ctx;
                VAS_118_QuickJournalModel model = new VAS_118_QuickJournalModel();
                retJSON = JsonConvert.SerializeObject(model.GetCostCenters(ctx));
            }
            return Json(retJSON, JsonRequestBehavior.AllowGet);
        }

        /// <summary>
        /// Creates the two-line GL journal from the modal input and either leaves
        /// it Drafted or completes it, in a single transaction. The amount arrives
        /// as an invariant-culture string and is parsed here so the decimal is not
        /// mangled by the server culture; every derived accounting value is
        /// resolved and validated in the model, never trusted from the browser.
        /// </summary>
        /// <param name="adOrgId">Header AD_Org_ID.</param>
        /// <param name="dateAcct">Accounting date (yyyy-MM-dd).</param>
        /// <param name="cAcctSchemaId">C_AcctSchema_ID.</param>
        /// <param name="cDocTypeId">GL-Journal C_DocType_ID.</param>
        /// <param name="description">Journal description.</param>
        /// <param name="debitAccountId">Debit account (C_ElementValue_ID).</param>
        /// <param name="creditAccountId">Credit account (C_ElementValue_ID).</param>
        /// <param name="amount">Amount as an invariant-culture decimal string.</param>
        /// <param name="adOrgTrxId">Optional cost/profit-center AD_OrgTrx_ID (0 when none).</param>
        /// <param name="action">"draft" or "post".</param>
        /// <returns>JSON-serialized QuickJournalResponse, or "" when no session.</returns>
        [HttpPost]
        [AjaxAuthorizeAttribute]
        [AjaxSessionFilterAttribute]
        public JsonResult CreateQuickJournal(int adOrgId = 0, string dateAcct = "", int cAcctSchemaId = 0,
            int cDocTypeId = 0, string description = "", int debitAccountId = 0, int creditAccountId = 0,
            string amount = "", int adOrgTrxId = 0, string action = "draft")
        {
            string retJSON = "";
            if (Session["ctx"] != null)
            {
                Ctx ctx = Session["ctx"] as Ctx;

                decimal parsedAmount;
                if (!decimal.TryParse(amount, NumberStyles.Any, CultureInfo.InvariantCulture, out parsedAmount))
                {
                    parsedAmount = 0m;
                }

                VAS_118_QuickJournalModel.QuickJournalRequest req = new VAS_118_QuickJournalModel.QuickJournalRequest
                {
                    AD_Org_ID = adOrgId,
                    DateAcct = dateAcct,
                    C_AcctSchema_ID = cAcctSchemaId,
                    C_DocType_ID = cDocTypeId,
                    Description = description,
                    DebitAccount_ID = debitAccountId,
                    CreditAccount_ID = creditAccountId,
                    Amount = parsedAmount,
                    AD_OrgTrx_ID = adOrgTrxId,
                    Action = action
                };

                VAS_118_QuickJournalModel model = new VAS_118_QuickJournalModel();
                retJSON = JsonConvert.SerializeObject(model.CreateQuickJournal(ctx, req));
            }
            return Json(retJSON);
        }
    }
}
