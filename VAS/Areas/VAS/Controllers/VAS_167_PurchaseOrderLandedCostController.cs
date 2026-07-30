/// <summary>
/// Module Name : VAS
/// Purpose     : Expected Landed Cost tab panel endpoints for the Purchase
///               Order window (C_Order, IsSOTrx = 'N'), consumed by the
///               VAS.VAS_167_PurchaseOrderLandedCost tab panel. Read returns
///               the entries, their generated distribution lines and the form
///               lookups; the two POST actions create / update / delete an
///               entry. Every rule (purchase order, drafted only, valid
///               lookups, amount greater than zero) lives in the model and is
///               re-checked there against the database — these actions only
///               pass values through.
/// Chronological development:
///   VAI163   2026-07-30  Created
/// </summary>

using Newtonsoft.Json;
using System.Globalization;
using System.Web.Mvc;
using VAdvantage.Utility;
using VASLogic.Models;

namespace VAS.Controllers
{
    public class VAS_167_PurchaseOrderLandedCostController : Controller
    {
        public ActionResult Index()
        {
            return View();
        }

        /// <summary>
        /// Returns the Expected Landed Cost payload for the selected purchase order.
        /// </summary>
        /// <param name="C_Order_ID">Selected purchase order id.</param>
        /// <returns>JSON-serialized
        /// <see cref="VAS_167_PurchaseOrderLandedCostModel.LandedCostPanelData"/>.</returns>
        public JsonResult GetLandedCostPanel(int C_Order_ID)
        {
            string retJSON = "";
            if (Session["ctx"] != null)
            {
                Ctx ctx = Session["ctx"] as Ctx;
                VAS_167_PurchaseOrderLandedCostModel model =
                    new VAS_167_PurchaseOrderLandedCostModel();
                retJSON = JsonConvert.SerializeObject(
                    model.GetLandedCostPanel(ctx, C_Order_ID));
            }
            return Json(retJSON, JsonRequestBehavior.AllowGet);
        }

        /// <summary>
        /// Creates (C_ExpectedCost_ID = 0) or updates one expected landed cost
        /// entry. Allowed only while the parent purchase order is drafted — the
        /// model verifies that against the database, so a request from a
        /// manipulated client is rejected regardless of what the UI showed.
        ///
        /// The amount arrives as text and is parsed with the invariant culture
        /// (the client always normalises it to "1234.5"), so the server's own
        /// culture setting can never reinterpret a decimal point as a thousands
        /// separator. Unparseable text becomes 0 and is rejected by the model's
        /// "greater than zero" rule rather than being silently stored.
        /// </summary>
        [HttpPost]
        public JsonResult SaveExpectedCost(int C_ExpectedCost_ID, int C_Order_ID,
            string LandedCostDistribution, int M_CostElement_ID, string Description,
            string Amt, int C_Currency_ID, int C_ConversionType_ID)
        {
            string retJSON = "";
            if (Session["ctx"] != null)
            {
                Ctx ctx = Session["ctx"] as Ctx;
                decimal amount;
                if (!decimal.TryParse(Amt, NumberStyles.Any,
                        CultureInfo.InvariantCulture, out amount))
                {
                    amount = 0;
                }

                VAS_167_PurchaseOrderLandedCostModel model =
                    new VAS_167_PurchaseOrderLandedCostModel();
                retJSON = JsonConvert.SerializeObject(
                    model.SaveExpectedCost(ctx, C_ExpectedCost_ID, C_Order_ID,
                        LandedCostDistribution, M_CostElement_ID, Description,
                        amount, C_Currency_ID, C_ConversionType_ID));
            }
            return Json(retJSON, JsonRequestBehavior.AllowGet);
        }

        /// <summary>
        /// Removes one expected landed cost entry. Allowed only while the parent
        /// purchase order is drafted (re-checked in the model).
        /// </summary>
        [HttpPost]
        public JsonResult DeleteExpectedCost(int C_ExpectedCost_ID)
        {
            string retJSON = "";
            if (Session["ctx"] != null)
            {
                Ctx ctx = Session["ctx"] as Ctx;
                VAS_167_PurchaseOrderLandedCostModel model =
                    new VAS_167_PurchaseOrderLandedCostModel();
                retJSON = JsonConvert.SerializeObject(
                    model.DeleteExpectedCost(ctx, C_ExpectedCost_ID));
            }
            return Json(retJSON, JsonRequestBehavior.AllowGet);
        }
    }
}
