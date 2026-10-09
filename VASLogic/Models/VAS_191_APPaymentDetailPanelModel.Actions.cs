/// <summary>
/// Module Name : VASLogic
/// Purpose     : Payment Overview tab panel — document actions (write side) of
///               VAS_191_APPaymentDetailPanelModel.
///
///               Complete and Reverse both run the standard document engine
///               (DocumentEngine.CompleteOrReverse), the same path the DocAction
///               button on the payment window drives. No custom completion or
///               reversal mechanism is implemented here.
///
///               The AD_Process_ID is NEVER hard-coded: it is resolved from the
///               application dictionary through
///                   C_Payment -> AD_Table -> DocAction AD_Column -> AD_Process_ID
///               and cached for the life of the process, because that metadata is
///               static and the lookup would otherwise repeat on every click.
///
///               Button visibility on the client is a convenience only — every
///               action re-reads the payment and re-validates its state here
///               before touching the document engine, so a stale panel cannot
///               complete an already-completed payment or reverse a draft.
/// Chronological development:
///   VAI145   2026-08-17  Created.
/// </summary>

using System;
using System.Data.SqlClient;
using VAdvantage.DataBase;
using VAdvantage.Logging;
using VAdvantage.Model;
using VAdvantage.Process;
using VAdvantage.Utility;
using VAdvantage.VOS;

namespace VASLogic.Models
{
    public partial class VAS_191_APPaymentDetailPanelModel
    {
        /// <summary>C_Payment's AD_Table_ID, resolved once per process (dictionary
        /// metadata is static; 0 means "not resolved yet").</summary>
        private static int _paymentTableId;

        /// <summary>AD_Process_ID behind C_Payment.DocAction, resolved once per
        /// process (0 means "not resolved yet").</summary>
        private static int _docActionProcessId;

        private static readonly object _metaLock = new object();

        // ----------------------------------------------------------------- //
        //  Action availability                                               //
        // ----------------------------------------------------------------- //

        /// <summary>
        /// Decides which document actions this payment currently offers. The same
        /// routine drives the panel's buttons and the server-side re-validation, so
        /// the two can never disagree.
        ///
        ///   Complete      — drafted or in-progress documents only.
        ///   Reverse       — completed documents; a closed document only when the
        ///                   document itself still offers Reverse-Correct as its
        ///                   DocAction (the engine's own availability, not a rule
        ///                   invented here). Never on drafted / reversed / voided.
        ///   Apply Advance — a completed or closed payment that still has an amount
        ///                   available for allocation. IsPrepayment is deliberately
        ///                   NOT part of this test: it describes advance behaviour,
        ///                   not remaining allocation availability.
        /// </summary>
        /// <param name="data">Payload whose status fields have already been read.</param>
        private static void ApplyActionFlags(PaymentOverviewData data)
        {
            if (data == null || data.C_Payment_ID <= 0)
            {
                return;
            }

            string status = data.DocStatus;

            data.CanComplete = status == DocActionConstants.STATUS_Drafted
                            || status == DocActionConstants.STATUS_InProgress;

            data.CanReverse = status == DocActionConstants.STATUS_Completed
                           || (status == DocActionConstants.STATUS_Closed
                               && data.DocAction == DocActionConstants.ACTION_Reverse_Correct);

            bool allocatable = status == DocActionConstants.STATUS_Completed
                            || status == DocActionConstants.STATUS_Closed;

            data.CanApplyAdvance = allocatable
                                && !data.IsAllocated
                                && data.AvailableSigned != 0;
        }

        // ----------------------------------------------------------------- //
        //  Complete                                                          //
        // ----------------------------------------------------------------- //

        /// <summary>
        /// Completes a drafted / in-progress payment through the standard document
        /// engine. The payment is re-read and re-validated here regardless of what
        /// the panel believed when it drew the button.
        /// </summary>
        /// <param name="ctx">User context (client / org / role).</param>
        /// <param name="C_Payment_ID">Payment to complete.</param>
        /// <returns><see cref="PaymentActionResult"/> with Success, the resulting
        /// DocStatus and a user-facing message.</returns>
        public PaymentActionResult CompletePayment(Ctx ctx, int C_Payment_ID)
        {
            return RunDocumentAction(ctx, C_Payment_ID, DocActionConstants.ACTION_Complete);
        }

        // ----------------------------------------------------------------- //
        //  Reverse                                                           //
        // ----------------------------------------------------------------- //

        /// <summary>
        /// Reverses a completed payment through the standard document engine
        /// (Reverse-Correct — the action the payment document supports). No custom
        /// reversal is performed.
        /// </summary>
        /// <param name="ctx">User context (client / org / role).</param>
        /// <param name="C_Payment_ID">Payment to reverse.</param>
        /// <returns><see cref="PaymentActionResult"/> with Success, the resulting
        /// DocStatus and a user-facing message.</returns>
        public PaymentActionResult ReversePayment(Ctx ctx, int C_Payment_ID)
        {
            return RunDocumentAction(ctx, C_Payment_ID, DocActionConstants.ACTION_Reverse_Correct);
        }

        /// <summary>
        /// Shared body of Complete / Reverse: validate the record and its state,
        /// run DocumentEngine.CompleteOrReverse with the dictionary-resolved
        /// process, then re-read the document and report the status it actually
        /// reached. The engine reporting no error is not by itself proof that the
        /// document moved, so the final status is what decides Success.
        /// </summary>
        /// <param name="ctx">User context.</param>
        /// <param name="C_Payment_ID">Payment to act on.</param>
        /// <param name="docAction">DocAction code ("CO" or "RC").</param>
        /// <returns>Result carrying Success, DocStatus, DocumentNo and a message.</returns>
        private PaymentActionResult RunDocumentAction(Ctx ctx, int C_Payment_ID, string docAction)
        {
            PaymentActionResult result = new PaymentActionResult();
            result.Success = false;
            result.C_Payment_ID = C_Payment_ID;

            if (ctx == null || C_Payment_ID <= 0)
            {
                result.Message = Msg.GetMsg(ctx, "FillMandatory");
                return result;
            }

            try
            {
                MPayment payment = new MPayment(ctx, C_Payment_ID, null);

                /* The record must exist and belong to the session client — an id
                   from the browser is never trusted on its own. */
                if (payment.Get_ID() == 0 || payment.GetAD_Client_ID() != ctx.GetAD_Client_ID())
                {
                    result.Message = Msg.GetMsg(ctx, "VIS_NoRecordFound");
                    return result;
                }

                string status = payment.GetDocStatus();
                result.DocumentNo = payment.GetDocumentNo();
                result.DocStatus = status;

                if (docAction == DocActionConstants.ACTION_Complete)
                {
                    /* Drafted / in-progress only. An already completed, closed,
                       reversed or voided payment is refused rather than re-run. */
                    if (status != DocActionConstants.STATUS_Drafted
                        && status != DocActionConstants.STATUS_InProgress)
                    {
                        result.Message = Msg.GetMsg(ctx, "VAS_191_CannotCompletePayment");
                        return result;
                    }
                }
                else
                {
                    /* Reverse: completed documents, and a closed one only when the
                       document still offers Reverse-Correct itself. */
                    bool reversible = status == DocActionConstants.STATUS_Completed
                                   || (status == DocActionConstants.STATUS_Closed
                                       && payment.GetDocAction() == DocActionConstants.ACTION_Reverse_Correct);
                    if (!reversible)
                    {
                        result.Message = Msg.GetMsg(ctx, "VAS_191_CannotReversePayment");
                        return result;
                    }
                }

                int tableId = MPayment.Table_ID;
                int processId = GetDocActionProcessId(tableId);
                if (tableId <= 0 || processId <= 0)
                {
                    /* Without the dictionary metadata there is no supported way to
                       run the action, and inventing an id would be worse. */
                    result.Message = Msg.GetMsg(ctx, "VAS_191_DocActionProcessMissing");
                    return result;
                }

                string engineMsg = DocumentEngine.CompleteOrReverse(
                    ctx,
                    "C_Payment",
                    tableId,
                    C_Payment_ID,
                    processId,
                    docAction);

                /* Re-read: the workflow ran outside this call's object graph. */
                payment = new MPayment(ctx, C_Payment_ID, null);
                string newStatus = payment.GetDocStatus();
                result.DocStatus = newStatus;
                result.DocStatusName = GetListReferenceName(ctx, "C_Payment", "DocStatus", newStatus);
                result.DocumentNo = payment.GetDocumentNo();

                bool reached = docAction == DocActionConstants.ACTION_Complete
                    ? (newStatus == DocActionConstants.STATUS_Completed
                       || newStatus == DocActionConstants.STATUS_Closed)
                    : (newStatus == DocActionConstants.STATUS_Reversed
                       || newStatus == DocActionConstants.STATUS_Voided);

                if (!reached)
                {
                    string reason = engineMsg;
                    if (string.IsNullOrEmpty(reason))
                    {
                        reason = payment.GetProcessMsg();
                    }
                    if (string.IsNullOrEmpty(reason))
                    {
                        reason = Msg.GetMsg(ctx, "DocNotCompleted");
                    }
                    result.Message = reason;
                    return result;
                }

                result.Success = true;
                result.Message = docAction == DocActionConstants.ACTION_Complete
                    ? Msg.GetMsg(ctx, "VAS_191_PaymentCompleted")
                    : Msg.GetMsg(ctx, "VAS_191_PaymentReversed");
                return result;
            }
            catch (Exception ex)
            {
                /* The raw exception goes to the log; the user gets the platform's
                   own "document not completed" wording. */
                _log.Severe("VAS_191 RunDocumentAction(" + C_Payment_ID + ", " + docAction + "): " + ex.Message);
                result.Message = Msg.GetMsg(ctx, "DocNotCompleted");
                return result;
            }
        }

        // ----------------------------------------------------------------- //
        //  Dictionary metadata                                               //
        // ----------------------------------------------------------------- //

        /// <summary>
        /// AD_Process_ID attached to the C_Payment.DocAction column — the process
        /// the document-action button runs. Resolved from
        /// AD_Table -> AD_Column(DocAction) -> AD_Process_ID and cached, so the
        /// panel never depends on an environment-specific numeric id.
        /// </summary>
        /// <param name="tableId">C_Payment's AD_Table_ID.</param>
        /// <returns>AD_Process_ID, or 0 when the column carries no process.</returns>
        private static int GetDocActionProcessId(int tableId)
        {
            if (_docActionProcessId > 0)
            {
                return _docActionProcessId;
            }
            if (tableId <= 0)
            {
                return 0;
            }

            lock (_metaLock)
            {
                if (_docActionProcessId > 0)
                {
                    return _docActionProcessId;
                }

                try
                {
                    string sql = @"SELECT col.AD_Process_ID
                                     FROM AD_Column col
                                    WHERE col.AD_Table_ID=@AD_Table_ID
                                      AND col.ColumnName=@ColumnName
                                      AND col.IsActive='Y'";

                    /* Two binds, each occurring once, in the order they appear. */
                    _docActionProcessId = Util.GetValueOfInt(DB.ExecuteScalar(sql, new SqlParameter[]
                    {
                        new SqlParameter("@AD_Table_ID", tableId),
                        new SqlParameter("@ColumnName", "DocAction")
                    }, null));
                }
                catch (Exception ex)
                {
                    _log.Severe("VAS_191 GetDocActionProcessId: " + ex.Message);
                    _docActionProcessId = 0;
                }

                return _docActionProcessId;
            }
        }

        // ----------------------------------------------------------------- //
        //  Payload                                                           //
        // ----------------------------------------------------------------- //

        /// <summary>Outcome of a Complete / Reverse run.</summary>
        public class PaymentActionResult
        {
            public bool Success { get; set; }
            public int C_Payment_ID { get; set; }
            public string DocumentNo { get; set; }
            /// <summary>Status the document actually ended in.</summary>
            public string DocStatus { get; set; }
            public string DocStatusName { get; set; }
            /// <summary>User-facing message — never a raw database exception.</summary>
            public string Message { get; set; }
        }
    }
}
