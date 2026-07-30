/// <summary>
/// Module Name : VAS
/// Purpose     : Goods Receipt Note (GRN) Overview tab panel endpoint. Returns
///               the read-only overview payload for the selected goods receipt
///               (M_InOut, IsSOTrx = 'N') consumed by the
///               VAS.VAS_099_OverviewGRN tab panel, plus the two document
///               actions the panel offers (Complete GRN / Generate Invoice).
/// Chronological development:
///   VAI163   2026-07-06  Created
///   VAI163   2026-07-27  Added CompleteGRN + GenerateInvoice actions so the
///                        panel buttons are functional. Completion runs the
///                        standard DocumentEngine.CompleteOrReverse ("CO");
///                        invoice generation runs the core C_InvoiceCreate
///                        process for the receipt's linked purchase order (the
///                        AD_Process_ID is resolved by Value, not hardcoded).
/// </summary>

using Newtonsoft.Json;
using System;
using System.Web.Mvc;
using VAdvantage.DataBase;
using VAdvantage.Model;
using VAdvantage.Process;
using VAdvantage.ProcessEngine;
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
        /// Completes the selected goods receipt (DocAction "CO") through the
        /// standard document process — the same path the core toolbar Complete
        /// runs (DocumentEngine.CompleteOrReverse). The AD_Table_ID and
        /// AD_Process_ID are resolved from the dictionary at runtime, never
        /// hardcoded. Returns the resulting document status on success.
        /// </summary>
        /// <param name="M_InOut_ID">Selected goods receipt id.</param>
        [HttpPost]
        public JsonResult CompleteGRN(int M_InOut_ID)
        {
            if (Session["ctx"] == null) return Fail("Session expired. Please sign in again.");
            Ctx ctx = Session["ctx"] as Ctx;
            try
            {
                if (M_InOut_ID <= 0) return Fail("Invalid goods receipt.");

                MInOut receipt = new MInOut(ctx, M_InOut_ID, null);
                if (receipt.Get_ID() <= 0) return Fail("Goods receipt not found.");

                string ds = receipt.GetDocStatus();
                if (ds == "CO" || ds == "CL")
                    return Fail("This goods receipt is already completed.");
                if (ds == "VO" || ds == "RE")
                    return Fail("This goods receipt is voided / reversed and cannot be completed.");

                string err = DocumentEngine.CompleteOrReverse(
                    ctx, "M_InOut", GetTableId("M_InOut"), M_InOut_ID,
                    GetProcessIdByValue(ctx, "M_InOut Process"), MInOut.DOCACTION_Complete);
                if (!string.IsNullOrEmpty(err)) return Fail(err);

                MInOut done = new MInOut(ctx, M_InOut_ID, null);
                return Ok(new
                {
                    success = true,
                    docStatus = done.GetDocStatus(),
                    message = "Goods receipt completed."
                });
            }
            catch (Exception ex)
            {
                return Fail(ex.Message);
            }
        }

        /// <summary>
        /// Generates the AP invoice for the receipt's linked purchase order by
        /// running the core C_InvoiceCreate process (resolved by Value, falling
        /// back to the well-known id), the same mechanism the "Generate Invoices"
        /// screen uses. The receipt must be completed and carry a purchase order.
        /// The generated invoice covers the order's uninvoiced received quantity
        /// (order-scoped, not strictly this single receipt).
        /// </summary>
        /// <param name="M_InOut_ID">Selected goods receipt id.</param>
        [HttpPost]
        public JsonResult GenerateInvoice(int M_InOut_ID)
        {
            if (Session["ctx"] == null) return Fail("Session expired. Please sign in again.");
            Ctx ctx = Session["ctx"] as Ctx;
            try
            {
                if (M_InOut_ID <= 0) return Fail("Invalid goods receipt.");

                MInOut receipt = new MInOut(ctx, M_InOut_ID, null);
                if (receipt.Get_ID() <= 0) return Fail("Goods receipt not found.");

                string ds = receipt.GetDocStatus();
                if (ds != "CO" && ds != "CL")
                    return Fail("Complete the goods receipt before generating an invoice.");

                int C_Order_ID = receipt.GetC_Order_ID();
                if (C_Order_ID <= 0)
                    return Fail("This receipt has no purchase order, so no invoice can be generated from it.");

                int processId = GetProcessIdByValue(ctx, "C_InvoiceCreate");
                if (processId <= 0) processId = 134;   // C_InvoiceCreate (well-known id fallback)

                MPInstance instance = new MPInstance(ctx, processId, 0);
                if (!instance.Save()) return Fail("Could not start the invoice generation process.");

                ProcessInfo pi = new ProcessInfo("", processId);
                pi.SetAD_PInstance_ID(instance.GetAD_PInstance_ID());
                pi.SetAD_Client_ID(ctx.GetAD_Client_ID());

                // C_InvoiceCreate parameters (order-driven): Selection = N,
                // complete the created invoice (CO), for this receipt's order.
                MPInstancePara p1 = new MPInstancePara(instance, 10);
                p1.setParameter("Selection", "N");
                if (!p1.Save()) return Fail("Could not prepare the invoice process (Selection).");

                MPInstancePara p2 = new MPInstancePara(instance, 20);
                p2.setParameter("DocAction", "CO");
                if (!p2.Save()) return Fail("Could not prepare the invoice process (DocAction).");

                MPInstancePara p3 = new MPInstancePara(instance, 30);
                p3.setParameter("C_Order_ID", C_Order_ID.ToString());
                if (!p3.Save()) return Fail("Could not prepare the invoice process (Order).");

                ProcessCtl worker = new ProcessCtl(ctx, null, pi, null);
                worker.Run();

                if (pi.IsError())
                {
                    string summary = pi.GetSummary();
                    return Fail(string.IsNullOrEmpty(summary) ? "Invoice generation failed." : summary);
                }

                string invNo = new VAS_099_OverviewGRNModel().GetLatestInvoiceDocNoForOrder(C_Order_ID);
                return Ok(new
                {
                    success = true,
                    invoiceNo = invNo,
                    message = string.IsNullOrEmpty(invNo)
                        ? "Invoice generated."
                        : "Invoice generated (" + invNo + ")."
                });
            }
            catch (Exception ex)
            {
                return Fail(ex.Message);
            }
        }

        // --------------------------------------------------------------------- //
        //  Helpers (dictionary lookups + JSON envelopes)                        //
        // --------------------------------------------------------------------- //

        /// <summary>Resolves an AD_Table_ID by table name (0 when not found).</summary>
        private int GetTableId(string tableName)
        {
            try
            {
                return Util.GetValueOfInt(DB.ExecuteScalar(
                    "SELECT AD_Table_ID FROM AD_Table WHERE TableName=@TableName AND IsActive='Y'",
                    new System.Data.SqlClient.SqlParameter[]
                    {
                        new System.Data.SqlClient.SqlParameter("@TableName", tableName)
                    }, null));
            }
            catch { return 0; }
        }

        /// <summary>
        /// Resolves an AD_Process_ID by its stable Value, preferring a
        /// client-specific record over the system one (0 when not found).
        /// </summary>
        private int GetProcessIdByValue(Ctx ctx, string processValue)
        {
            try
            {
                return Util.GetValueOfInt(DB.ExecuteScalar(
                    @"SELECT AD_Process_ID FROM AD_Process
                       WHERE Value=@Value AND IsActive='Y'
                         AND AD_Client_ID IN (0, @AD_Client_ID)
                       ORDER BY AD_Client_ID DESC FETCH FIRST 1 ROW ONLY",
                    new System.Data.SqlClient.SqlParameter[]
                    {
                        new System.Data.SqlClient.SqlParameter("@Value", processValue),
                        new System.Data.SqlClient.SqlParameter("@AD_Client_ID", ctx.GetAD_Client_ID())
                    }, null));
            }
            catch { return 0; }
        }

        /// <summary>Serializes a success payload.</summary>
        private JsonResult Ok(object result)
        {
            return Json(JsonConvert.SerializeObject(result), JsonRequestBehavior.AllowGet);
        }

        /// <summary>Serializes a failure message (success:false).</summary>
        private JsonResult Fail(string message)
        {
            return Json(JsonConvert.SerializeObject(new
            {
                success = false,
                error = message,
                message = message
            }), JsonRequestBehavior.AllowGet);
        }
    }
}
