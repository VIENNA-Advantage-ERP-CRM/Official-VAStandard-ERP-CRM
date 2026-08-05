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
/// </summary>

using Newtonsoft.Json;
using System;
using System.Web.Mvc;
using VAdvantage.DataBase;
using VAdvantage.Logging;
using VAdvantage.Model;
using VAdvantage.Process;
using VAdvantage.ProcessEngine;
using VAdvantage.Utility;
using VASLogic.Models;
using ViennaAdvantage.Process;

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
        /// hardcoded.
        ///
        /// The receipt's accounting period is validated first, exactly as the
        /// document itself does in PrepareIt, so a closed period is reported as
        /// "@PeriodClosed@" before anything is written — the main window's
        /// behaviour. After the run the document's own status decides the answer:
        /// the workflow returning no error does not mean the receipt completed
        /// (a failed validation leaves it drafted / invalid), and the panel must
        /// not be told otherwise.
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

                // Same check, same message as MInOut.PrepareIt — run here so the
                // reason reaches the user instead of the record silently staying
                // where it was.
                string periodError = ValidatePeriod(ctx, receipt);
                if (!string.IsNullOrEmpty(periodError)) return Fail(periodError);

                string err = DocumentEngine.CompleteOrReverse(
                    ctx, "M_InOut", GetTableId("M_InOut"), M_InOut_ID,
                    GetProcessIdByValue(ctx, "M_InOut Process"), MInOut.DOCACTION_Complete);
                if (!string.IsNullOrEmpty(err)) return Fail(Translate(ctx, err));

                // The document, not the process, is the authority on what happened.
                MInOut done = new MInOut(ctx, M_InOut_ID, null);
                string status = done.GetDocStatus();
                if (status != "CO" && status != "CL")
                    return Fail(NotCompletedMessage(ctx, status));

                return Ok(new
                {
                    success = true,
                    docStatus = status,
                    message = "Goods receipt " + done.GetDocumentNo() + " completed."
                });
            }
            catch (Exception ex)
            {
                return Fail(ex.Message);
            }
        }

        /// <summary>
        /// Returns the translated "@PeriodClosed@" message when the receipt's
        /// accounting period is not open for its document base type, else "".
        /// Mirrors the check MInOut.PrepareIt runs (DateAcct + DocBaseType + the
        /// document's organisation). A lookup problem returns "" so this can only
        /// ever add a message the document would have raised anyway — never block
        /// a completion the document would have allowed.
        /// </summary>
        private string ValidatePeriod(Ctx ctx, MInOut receipt)
        {
            try
            {
                MDocType dt = MDocType.Get(ctx, receipt.GetC_DocType_ID());
                if (dt == null || string.IsNullOrEmpty(dt.GetDocBaseType())) return "";

                if (!MPeriod.IsOpen(ctx, receipt.GetDateAcct(), dt.GetDocBaseType(),
                                    receipt.GetAD_Org_ID()))
                {
                    return Translate(ctx, "@PeriodClosed@");
                }
            }
            catch { }
            return "";
        }

        /// <summary>
        /// The message for a receipt that ran the completion but did not come out
        /// completed. Prefers whatever the document / workflow last logged (the
        /// real reason — a closed period, a missing confirmation, no lines), and
        /// names the status it ended in so the reader is never left guessing.
        /// </summary>
        private string NotCompletedMessage(Ctx ctx, string status)
        {
            string reason = "";
            try
            {
                ValueNamePair vnp = VLogger.RetrieveError();
                if (vnp != null)
                {
                    reason = vnp.GetName();
                    if (string.IsNullOrEmpty(reason)) reason = vnp.GetValue();
                }
            }
            catch { }

            if (!string.IsNullOrEmpty(reason)) return Translate(ctx, reason);

            string statusText = StatusText(status);
            return string.IsNullOrEmpty(statusText)
                ? "The goods receipt could not be completed."
                : "The goods receipt could not be completed — it is still " + statusText + ".";
        }

        /// <summary>Reader-facing name for the DocStatus codes a failed completion leaves behind.</summary>
        private string StatusText(string status)
        {
            switch (status)
            {
                case "DR": return "drafted";
                case "IP": return "in progress";
                case "IN": return "invalid";
                case "NA": return "not approved";
                case "AP": return "approved";
                case "WC": return "waiting confirmation";
                case "WP": return "waiting payment";
                default:   return "";
            }
        }

        /// <summary>
        /// Resolves the platform's @Tag@ placeholders in a document message
        /// ("@PeriodClosed@" -> "Period Closed"). Falls back to the raw text when
        /// the message cannot be translated — a readable tag beats no message.
        /// </summary>
        private string Translate(Ctx ctx, string message)
        {
            if (string.IsNullOrEmpty(message)) return message;
            try
            {
                string translated = Msg.ParseTranslation(ctx, message);
                if (!string.IsNullOrEmpty(translated)) return translated;
            }
            catch { }
            return message;
        }

        /// <summary>
        /// Generates the AP invoice for the receipt from the parameters the panel's
        /// Generate Invoice dialog collected — target document type, the vendor's
        /// invoice reference and the generate-charges flag — by running the core
        /// InOutCreateInvoice process. That is the same process, with the same
        /// parameters and the same mandatory fields, that the main screen's
        /// Generate Invoice dialog runs, so both produce the same document.
        ///
        /// The invoice is receipt-scoped: it covers this receipt's uninvoiced
        /// lines, whether or not a purchase order sits behind it. (It replaces the
        /// earlier order-driven C_InvoiceCreate call, which invoiced the whole
        /// ORDER's uninvoiced quantity — not what a reader asks for from a single
        /// receipt's panel, and not what the main screen does.) It is left drafted,
        /// again as on the main screen, and its document no comes back so the panel
        /// can name it.
        /// </summary>
        /// <param name="M_InOut_ID">Selected goods receipt id.</param>
        /// <param name="C_DocType_ID">Target invoice document type chosen in the dialog.</param>
        /// <param name="InvoiceReference">The vendor's own invoice reference (mandatory, as on the main screen).</param>
        /// <param name="GenerateCharges">Spread the receipt's charges across the invoice lines.</param>
        [HttpPost]
        public JsonResult GenerateInvoice(int M_InOut_ID, int C_DocType_ID = 0,
                                          string InvoiceReference = null,
                                          bool GenerateCharges = false)
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

                // The main screen asks for both before it will generate.
                if (C_DocType_ID <= 0) return Fail("Select the invoice document type.");
                if (string.IsNullOrEmpty(InvoiceReference) || InvoiceReference.Trim().Length == 0)
                    return Fail("Enter the invoice reference.");

                int C_Invoice_ID = 0;
                try
                {
                    InOutCreateInvoice generator = new InOutCreateInvoice();
                    generator.SetParameter(InvoiceReference.Trim(), C_DocType_ID,
                                           GenerateCharges, M_InOut_ID, ctx);
                    generator.Generate();
                    C_Invoice_ID = generator.C_Invoice_ID;
                }
                catch (Exception ex)
                {
                    // The process reports its refusals by throwing — nothing left
                    // to invoice, a provisional invoice already raised, mixed
                    // payment terms across orders. That message IS the answer.
                    return Fail(Translate(ctx, ex.Message));
                }

                if (C_Invoice_ID <= 0)
                    return Fail("No invoice could be generated from this goods receipt.");

                MInvoice invoice = new MInvoice(ctx, C_Invoice_ID, null);
                string invNo = invoice.GetDocumentNo();

                return Ok(new
                {
                    success = true,
                    C_Invoice_ID = C_Invoice_ID,
                    invoiceNo = invNo,
                    docStatus = invoice.GetDocStatus(),
                    message = string.IsNullOrEmpty(invNo)
                        ? "Invoice generated."
                        : "Invoice " + invNo + " generated."
                });
            }
            catch (Exception ex)
            {
                return Fail(ex.Message);
            }
        }

        /// <summary>
        /// The invoice document types the Generate Invoice dialog offers for this
        /// receipt, under the same rule the main screen's dialog applies:
        /// an invoice or credit-memo base type, matching the receipt's sales /
        /// return flags, visible to the receipt's organisation and not an expense
        /// invoice. The type the receipt's own document type nominates
        /// (C_DocTypeInvoice_ID) comes back as the default selection.
        /// </summary>
        /// <param name="M_InOut_ID">Selected goods receipt id.</param>
        public JsonResult GetInvoiceDocTypes(int M_InOut_ID)
        {
            if (Session["ctx"] == null) return Fail("Session expired. Please sign in again.");
            Ctx ctx = Session["ctx"] as Ctx;
            try
            {
                if (M_InOut_ID <= 0) return Fail("Invalid goods receipt.");

                MInOut receipt = new MInOut(ctx, M_InOut_ID, null);
                if (receipt.Get_ID() <= 0) return Fail("Goods receipt not found.");

                string isSOTrx     = receipt.IsSOTrx() ? "Y" : "N";
                string isReturnTrx = receipt.IsReturnTrx() ? "Y" : "N";

                // Default: whatever the receipt's document type nominates.
                int defaultDocTypeId = Util.GetValueOfInt(DB.ExecuteScalar(
                    "SELECT C_DocTypeInvoice_ID FROM C_DocType WHERE C_DocType_ID=@C_DocType_ID",
                    new System.Data.SqlClient.SqlParameter[]
                    {
                        new System.Data.SqlClient.SqlParameter("@C_DocType_ID", receipt.GetC_DocType_ID())
                    }, null));

                System.Collections.Generic.List<object> types =
                    new System.Collections.Generic.List<object>();

                string sql = @"SELECT dt.C_DocType_ID, dt.Name, dt.DocBaseType
                                 FROM C_DocType dt
                                WHERE dt.DocBaseType IN ('API', 'APC', 'ARI', 'ARC')
                                  AND dt.IsSOTrx     = @IsSOTrx
                                  AND NVL(dt.IsReturnTrx, 'N') = @IsReturnTrx
                                  AND dt.AD_Client_ID = @AD_Client_ID
                                  AND dt.AD_Org_ID IN (0, @AD_Org_ID)
                                  AND NVL(dt.IsExpenseInvoice, 'N') = 'N'
                                  AND dt.IsActive = 'Y'
                                ORDER BY dt.Name";
                System.Data.DataSet ds = DB.ExecuteDataset(sql,
                    new System.Data.SqlClient.SqlParameter[]
                    {
                        new System.Data.SqlClient.SqlParameter("@IsSOTrx", isSOTrx),
                        new System.Data.SqlClient.SqlParameter("@IsReturnTrx", isReturnTrx),
                        new System.Data.SqlClient.SqlParameter("@AD_Client_ID", ctx.GetAD_Client_ID()),
                        new System.Data.SqlClient.SqlParameter("@AD_Org_ID", receipt.GetAD_Org_ID())
                    }, null);

                if (ds != null && ds.Tables.Count > 0)
                {
                    foreach (System.Data.DataRow r in ds.Tables[0].Rows)
                    {
                        types.Add(new
                        {
                            C_DocType_ID = Util.GetValueOfInt(r["C_DocType_ID"]),
                            Name         = Util.GetValueOfString(r["Name"]),
                            DocBaseType  = Util.GetValueOfString(r["DocBaseType"])
                        });
                    }
                }

                return Ok(new
                {
                    success = true,
                    docTypes = types,
                    defaultDocTypeId = defaultDocTypeId,
                    documentNo = receipt.GetDocumentNo()
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
        /// Resolves a table's document-action process the way the dictionary
        /// itself records it: AD_Column.AD_Process_ID on that table's DocAction
        /// column. This is how the platform finds a document's process, so it
        /// holds even where the process record was renamed or re-seeded with a
        /// different Value (0 when not found).
        /// </summary>
        private int GetDocActionProcessId(string tableName)
        {
            try
            {
                return Util.GetValueOfInt(DB.ExecuteScalar(
                    @"SELECT c.AD_Process_ID FROM AD_Column c
                       INNER JOIN AD_Table t ON (t.AD_Table_ID = c.AD_Table_ID)
                       WHERE c.ColumnName = 'DocAction' AND c.IsActive = 'Y'
                         AND t.TableName = @TableName",
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
