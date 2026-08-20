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
///   VAI163   2026-08-12  Added GetWindow_ID: resolves a window by NAME for the
///                        panel's record-open path, so a document on a
///                        dual-purpose table opens its SALES-side screen — an AR
///                        invoice was opening the AP Invoice window. Ported from
///                        VAS_092. The two write actions are no longer reached
///                        from the panel, which is read-only now; both endpoints
///                        are left in place.
///   VAI163   2026-08-12  Added GetWindowIdByTable, the last resort behind it: the
///                        window a TABLE opens in, read from the dictionary. The
///                        Contract chip needs it — C_Contract and
///                        VAS_ContractMaster are maintained by module windows
///                        whose names cannot be hard-coded here. Ported from
///                        VAS_102.
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
        /// Resolves a window's AD_Window_ID from its name, for the panel's
        /// record-open path. Every document this panel opens is a SALES-side record
        /// of a dual-purpose table (C_Invoice, C_Payment, M_InOut), so the window is
        /// named rather than left to the table's default zoom target — which
        /// resolved the purchase side and opened an AR invoice on the AP Invoice
        /// screen. Ported from VAS_092.
        /// </summary>
        /// <param name="fields">Window name (AD_Window.Name), as sent by
        /// VIS.dataContext.getJSONRecord.</param>
        /// <returns>The window id, or 0 when the name is unknown to this client.</returns>
        public JsonResult GetWindow_ID(string fields)
        {
            int windowId = 0;
            if (Session["ctx"] != null)
            {
                Ctx ctx = Session["ctx"] as Ctx;
                VAS_106_OverviewSalesOrderModel model = new VAS_106_OverviewSalesOrderModel();
                windowId = model.GetWindowId(ctx, fields);
            }
            return Json(JsonConvert.SerializeObject(windowId), JsonRequestBehavior.AllowGet);
        }

        /// <summary>
        /// Resolves the window a TABLE's records open in, for a record whose screen
        /// cannot be named on the client — a contract (C_Contract /
        /// VAS_ContractMaster) is maintained by a module window whose name cannot
        /// be hard-coded, and the browser-side zoom lookup only knows tables the
        /// client has cached.
        /// </summary>
        /// <param name="fields">Physical table name, as sent by
        /// VIS.dataContext.getJSONRecord.</param>
        /// <returns>The window id, or 0 when the table has no window.</returns>
        public JsonResult GetWindowIdByTable(string fields)
        {
            int windowId = 0;
            if (Session["ctx"] != null)
            {
                Ctx ctx = Session["ctx"] as Ctx;
                VAS_106_OverviewSalesOrderModel model = new VAS_106_OverviewSalesOrderModel();
                windowId = model.GetWindowIdByTable(ctx, fields);
            }
            return Json(JsonConvert.SerializeObject(windowId), JsonRequestBehavior.AllowGet);
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
