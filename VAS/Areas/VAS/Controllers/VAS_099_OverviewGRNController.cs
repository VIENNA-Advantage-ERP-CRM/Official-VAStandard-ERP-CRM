/// <summary>
/// Module Name : VAS
/// Purpose     : Goods Receipt Note (GRN) Overview tab panel endpoint. Returns
///               the read-only overview payload for the selected goods receipt
///               (M_InOut, IsSOTrx = 'N') consumed by the
///               VAS.VAS_099_OverviewGRN tab panel.
/// Chronological development:
///   VAI163   2026-07-06  Created
///   VAI163   2026-07-27  Added CompleteGRN + GenerateInvoice actions so the
///                        panel buttons are functional. Completion runs the
///                        standard DocumentEngine.CompleteOrReverse ("CO");
///                        invoice generation runs the core C_InvoiceCreate
///                        process for the receipt's linked purchase order (the
///                        AD_Process_ID is resolved by Value, not hardcoded).
///   VAI163   2026-08-03  - Complete now runs the same period validation the
///                          main window does (MPeriod.IsOpen on the receipt's
///                          accounting date + document base type + org) and
///                          answers with the platform's own "@PeriodClosed@"
///                          message, instead of leaving the record drafted with
///                          nothing on screen.
///                        - Completion is verified against the document's status
///                          after the run: the panel is only told the receipt
///                          completed when it actually did, and otherwise gets
///                          the reason and the status it ended in. The workflow
///                          reporting no error is not proof of completion.
///                        - Generate Invoice covers a manually created receipt
///                          (no purchase order): the invoice is generated from
///                          the receipt itself through InOutCreateInvoice, the
///                          same process the Purchase Receipt panel uses, then
///                          completed. A receipt with a PO keeps the existing
///                          order-driven C_InvoiceCreate path.
///   VAI163   2026-08-03  - Generate Invoice now takes the main screen's dialog
///                          parameters (C_DocType_ID, InvoiceReference,
///                          GenerateCharges) and runs InOutCreateInvoice for
///                          EVERY receipt, with or without a purchase order —
///                          one receipt-scoped path that matches what the main
///                          screen produces, replacing the order-driven
///                          C_InvoiceCreate branch. The created invoice's id and
///                          document no come back so the panel can name it.
///                        - Added GetInvoiceDocTypes, the document-type list that
///                          dialog offers, filtered by the same rule the main
///                          screen applies and carrying the receipt's own
///                          default.
///   VAI163   2026-08-11  Removed the document actions and everything that served
///                        only them: CompleteGRN, GenerateInvoice,
///                        GetInvoiceDocTypes and their helpers (ValidatePeriod,
///                        NotCompletedMessage, StatusText, Translate, GetTableId,
///                        GetDocActionProcessId, GetProcessIdByValue, Ok, Fail).
///                        The panel's Complete GRN / Generate Invoice buttons are
///                        gone — both actions belong to the receipt's own screen,
///                        where they carry the document's full validation — and
///                        nothing else called these endpoints. The controller is
///                        read-only again, as it was when it was created, and the
///                        usings that only the actions needed went with them.
///   VAI163   2026-08-12  Added GetWindow_ID: resolves a window by NAME for the
///                        panel's record-open path, so a record whose screen is
///                        not the table's default zoom target (a receipt
///                        confirmation, which opens VAS_ShipReceiptConfirm) can
///                        still be opened. Ported from VAS_092.
/// </summary>

using Newtonsoft.Json;
using System.Web.Mvc;
using VAdvantage.Utility;
using VASLogic.Models;

namespace VAS.Controllers
{
    public class VAS_099_OverviewGRNController : Controller
    {
        public ActionResult Index()
        {
            return View();
        }

        /// <summary>
        /// Returns the overview details for the selected M_InOut goods receipt.
        /// </summary>
        /// <param name="M_InOut_ID">Selected goods receipt id.</param>
        /// <returns>JSON-serialized <see cref="VAS_099_OverviewGRNModel.GRNOverviewData"/>.</returns>
        public JsonResult GetGRNOverview(int M_InOut_ID)
        {
            string retJSON = "";
            if (Session["ctx"] != null)
            {
                Ctx ctx = Session["ctx"] as Ctx;
                VAS_099_OverviewGRNModel model = new VAS_099_OverviewGRNModel();
                retJSON = JsonConvert.SerializeObject(
                    model.GetGRNOverview(ctx, M_InOut_ID));
            }
            return Json(retJSON, JsonRequestBehavior.AllowGet);
        }

        /// <summary>
        /// Resolves a window's AD_Window_ID from its name, for the panel's
        /// record-open path. Used where the record's screen is not the table's
        /// default zoom target — a receipt confirmation opens the
        /// VAS_ShipReceiptConfirm window, not whatever M_InOutConfirm's zoom
        /// target resolves to.
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
                VAS_099_OverviewGRNModel model = new VAS_099_OverviewGRNModel();
                windowId = model.GetWindowId(ctx, fields);
            }
            return Json(JsonConvert.SerializeObject(windowId), JsonRequestBehavior.AllowGet);
        }
    }
}
