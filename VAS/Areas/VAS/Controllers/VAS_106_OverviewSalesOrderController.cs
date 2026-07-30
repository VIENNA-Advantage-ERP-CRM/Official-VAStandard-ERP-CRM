/// <summary>
/// Module Name : VAS
/// Purpose     : Sales Order Overview tab panel endpoint. Returns the overview
///               payload for the selected sales order (C_Order, IsSOTrx = 'Y')
///               consumed by the VAS.VAS_106_OverviewSalesOrder tab panel, and
///               exposes two write actions used by the panel's action bar:
///               Complete Sales Order and Create Contract (from a service /
///               charge line). Document creation runs server-side through the
///               platform model / process engine — never from the browser.
/// Chronological development:
///   VAI163   2026-07-08  Created
/// </summary>

using System;
using System.Globalization;
using Newtonsoft.Json;
using System.Web.Mvc;
using VAdvantage.Utility;
using VASLogic.Models;

namespace VAS.Controllers
{
    public class VAS_106_OverviewSalesOrderController : Controller
    {
        public ActionResult Index()
        {
            return View();
        }

        /// <summary>
        /// Returns the overview details for the selected C_Order sales order.
        /// </summary>
        /// <param name="C_Order_ID">Selected sales order id.</param>
        /// <returns>JSON-serialized <see cref="VAS_106_OverviewSalesOrderModel.SalesOrderOverviewData"/>.</returns>
        public JsonResult GetSalesOrderOverview(int C_Order_ID)
        {
            string retJSON = "";
            if (Session["ctx"] != null)
            {
                Ctx ctx = Session["ctx"] as Ctx;
                VAS_106_OverviewSalesOrderModel model = new VAS_106_OverviewSalesOrderModel();
                retJSON = JsonConvert.SerializeObject(
                    model.GetSalesOrderOverview(ctx, C_Order_ID));
            }
            return Json(retJSON, JsonRequestBehavior.AllowGet);
        }

        /// <summary>
        /// Completes the selected sales order (document action CO). One-way:
        /// refuses when the order is already completed / closed / voided.
        /// </summary>
        [HttpPost]
        public JsonResult CompleteSalesOrder(int C_Order_ID)
        {
            string retJSON = "";
            if (Session["ctx"] != null)
            {
                Ctx ctx = Session["ctx"] as Ctx;
                VAS_106_OverviewSalesOrderModel model = new VAS_106_OverviewSalesOrderModel();
                retJSON = JsonConvert.SerializeObject(
                    model.CompleteSalesOrder(ctx, C_Order_ID));
            }
            return Json(retJSON, JsonRequestBehavior.AllowGet);
        }

        /// <summary>
        /// Creates a draft C_Contract from the given service / charge order line.
        /// Dates arrive as DD-MM-YYYY strings from the inline contract form.
        /// </summary>
        [HttpPost]
        public JsonResult CreateContract(int C_Order_ID, int C_OrderLine_ID,
            int C_Frequency_ID, int noOfCycle, decimal qtyPerCycle,
            string startDate, string endDate)
        {
            string retJSON = "";
            if (Session["ctx"] != null)
            {
                Ctx ctx = Session["ctx"] as Ctx;
                VAS_106_OverviewSalesOrderModel model = new VAS_106_OverviewSalesOrderModel();
                retJSON = JsonConvert.SerializeObject(
                    model.CreateContract(ctx, C_Order_ID, C_OrderLine_ID, C_Frequency_ID,
                        noOfCycle, qtyPerCycle, ParseDate(startDate), ParseDate(endDate)));
            }
            return Json(retJSON, JsonRequestBehavior.AllowGet);
        }

        /// <summary>Parses a DD-MM-YYYY (or ISO) date string; null when blank / invalid.</summary>
        private DateTime? ParseDate(string value)
        {
            if (string.IsNullOrEmpty(value)) return null;
            string[] formats = { "dd-MM-yyyy", "d-M-yyyy", "yyyy-MM-dd", "MM/dd/yyyy", "dd/MM/yyyy" };
            DateTime parsed;
            if (DateTime.TryParseExact(value.Trim(), formats, CultureInfo.InvariantCulture,
                    DateTimeStyles.None, out parsed))
                return parsed;
            if (DateTime.TryParse(value.Trim(), out parsed))
                return parsed;
            return null;
        }
    }
}
