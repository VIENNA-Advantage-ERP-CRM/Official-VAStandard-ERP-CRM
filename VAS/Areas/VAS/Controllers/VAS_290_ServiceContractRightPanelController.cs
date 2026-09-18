/********************************************************
 * Module Name    : CRM Extension VAS
 * Purpose        : Service Contract Right Detail Panel — controller
 * Employee Code  : VAI154
 * Date           : 17-Sep-2026
 ******************************************************/
using System;
using System.Web.Mvc;
using Newtonsoft.Json;
using VAdvantage.Utility;
using VAS.Models;

namespace VAS.Controllers
{
    /// <summary>
    /// Module Name   : CRM Extension VAS
    /// Purpose       : Service Contract Right Detail Panel controller.
    ///                 Exposes two endpoints consumed by the right-side panel JS:
    ///                 GetContractOverview (header data) and GetContractSchedules
    ///                 (invoice schedule rows). All responses are double-serialized
    ///                 via JsonConvert.SerializeObject so the client can call
    ///                 jQuery.parseJSON() on the already-parsed string.
    /// Chronological development:
    ///   VAI154  17-Sep-2026  Created
    /// </summary>
    public class VAS_290_ServiceContractRightPanelController : Controller
    {
        // ─────────────────────────────────────────────────────────
        // §1  Contract overview
        // ─────────────────────────────────────────────────────────

        /// <summary>
        /// Returns all contract header data required by the right-side detail panel:
        /// core contract fields, joined lookup names (customer, product, UOM, currency,
        /// price list, payment term, billing location, frequency), and translated
        /// reference values for DocStatus, ContractType, and RenewalType.
        /// </summary>
        /// <param name="contractId">C_Contract_ID of the selected service contract.</param>
        /// <returns>Double-serialized JSON with contract overview data.</returns>
        [HttpPost]
        public ActionResult GetContractOverview(int contractId)
        {
            int safeId = Util.GetValueOfInt(contractId);
            if (safeId <= 0)
                return Json(
                    JsonConvert.SerializeObject(new { error = "Invalid contract ID" }),
                    JsonRequestBehavior.AllowGet);

            Ctx ctx    = (Ctx)Session["ctx"];
            var model  = new VAS_290_ServiceContractRightPanelModel();
            var result = model.GetContractOverview(ctx, safeId);
            return Json(JsonConvert.SerializeObject(result), JsonRequestBehavior.AllowGet);
        }

        // ─────────────────────────────────────────────────────────
        // §2  Contract schedules
        // ─────────────────────────────────────────────────────────

        /// <summary>
        /// Returns all active C_ContractSchedule rows for the specified contract.
        /// Each row includes period start/end, stored amounts, invoice reference
        /// (when available), and a derived BillingStatus of Invoiced / Due / Scheduled.
        /// Schedule status is derived using the application date from the server session,
        /// not from a client-supplied date parameter, to prevent manipulation.
        /// </summary>
        /// <param name="contractId">C_Contract_ID of the selected service contract.</param>
        /// <returns>Double-serialized JSON with an <c>items</c> array of schedule rows.</returns>
        [HttpPost]
        public ActionResult GetContractSchedules(int contractId)
        {
            int safeId = Util.GetValueOfInt(contractId);
            if (safeId <= 0)
                return Json(
                    JsonConvert.SerializeObject(new { items = new object[0] }),
                    JsonRequestBehavior.AllowGet);

            Ctx ctx    = (Ctx)Session["ctx"];
            var model  = new VAS_290_ServiceContractRightPanelModel();
            var result = model.GetContractSchedules(ctx, safeId);
            return Json(JsonConvert.SerializeObject(result), JsonRequestBehavior.AllowGet);
        }

        // ─────────────────────────────────────────────────────────
        // §3  Window ID lookup — zoom helper
        // ─────────────────────────────────────────────────────────

        /// <summary>
        /// Returns the AD_Window_ID for the given physical table name so the client
        /// can navigate directly to the VA window that maintains the record.
        /// </summary>
        /// <param name="fields">Physical table name (e.g. "C_Invoice").</param>
        /// <returns>Double-serialized JSON integer — the window ID, or 0 if not found.</returns>
        [HttpPost]
        public ActionResult GetWindowIdByTable(string fields)
        {
            Ctx ctx   = (Ctx)Session["ctx"];
            var model = new VAS_290_ServiceContractRightPanelModel();
            int wId   = model.GetWindowIdByTable(ctx, Util.GetValueOfString(fields));
            return Json(JsonConvert.SerializeObject(wId), JsonRequestBehavior.AllowGet);
        }
    }
}
