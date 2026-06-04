/******************************************************
 * Module Name    : VAS
 * Purpose        : Top Customers by Collections dashboard widget endpoint
 * chronological  : Development
 * Created Date   : 2026-06-02
 * Created by     : VAI154
 ******************************************************/

using Newtonsoft.Json;
using System.Web.Mvc;
using VAdvantage.Utility;
using VASLogic.Models;
using VIS.Filters;

namespace VAS.Controllers
{
    /// <summary>
    /// Module Name : VAS_013_TopCustomerReceipts
    /// Purpose     : Thin AJAX endpoint for the Top Customers by Collections
    ///               dashboard widget. All business logic lives in
    ///               VASLogic.Models.VAS_013_TopCustomerReceiptsModel; this
    ///               controller only resolves the session context and serializes
    ///               the model result.
    /// Chronological development:
    ///   VAI154      2026-06-02 Created
    /// </summary>
    public class VAS_013_TopCustomerReceiptsController : Controller
    {
        /// <summary>
        /// Returns the top N customers by AR collection amount (last 30 days),
        /// highest first, in the base/accounting currency.
        /// </summary>
        /// <param name="topN">Number of customers to return (default 5).</param>
        /// <returns>JSON-serialized TopCustomerCollections, or "" when no session.</returns>
        [AjaxAuthorizeAttribute]
        [AjaxSessionFilterAttribute]
        public JsonResult GetTopCustomers(int topN = 5)
        {
            string retJSON = "";

            if (Session["ctx"] != null)
            {
                Ctx ctx = Session["ctx"] as Ctx;
                VAS_013_TopCustomerReceiptsModel model = new VAS_013_TopCustomerReceiptsModel();
                retJSON = JsonConvert.SerializeObject(model.GetTopCustomers(ctx, topN));
            }

            return Json(retJSON, JsonRequestBehavior.AllowGet);
        }
    }
}
