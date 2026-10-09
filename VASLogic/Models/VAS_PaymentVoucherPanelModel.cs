/******************************************************
 * Module Name    : VASLogic
 * Purpose        : Payment Voucher tab panel data
 * chronological  : Development
 * Created Date   : 4 May 2026
 * Created by     : VAI154
 ******************************************************/

using System;
using System.Collections.Generic;
using System.Data;
using System.Data.SqlClient;
using VAdvantage.DataBase;
using VAdvantage.Logging;
using VAdvantage.Model;
using VAdvantage.Utility;

namespace VASLogic.Models
{
    /// <summary>
    /// Server-side data shaping for the Payment Voucher detail panel.
    /// Operates against C_Payment as the primary record (IsReceipt = 'N')
    /// and stitches in payee, bank, allocations, approval workflow, and
    /// recent activity from existing Onfinity tables.
    /// </summary>
    public class VAS_PaymentVoucherPanelModel
    {
        /// <summary>
        /// Returns the full Payment Voucher panel payload for a single
        /// C_Payment record: header, payee, amount/currency, scheduled date,
        /// approval workflow, allocated invoices, payment notes metadata,
        /// recent activity, and the resolved permission booleans.
        /// </summary>
        public PaymentVoucherData GetPaymentVoucher(Ctx ctx, int C_Payment_ID)
        {
            PaymentVoucherData result = new PaymentVoucherData();
            if (C_Payment_ID <= 0) return result;

            if (!LoadHeader(ctx, C_Payment_ID, result))
                return result;

            LoadYtdPaid(result);
            LoadApprovalWorkflow(result);
            LoadAllocations(result);
            LoadActivity(result);
            LoadPermissions(ctx, result);

            return result;
        }

        // ---------- Header / payee / bank / metadata ----------

        private bool LoadHeader(Ctx ctx, int C_Payment_ID, PaymentVoucherData result)
        {
            // Pull C_Payment plus joined payee, currency, payee bank, company bank,
            // cost-centre, payment terms (best-effort via primary linked invoice),
            // PO reference (payment field, then linked invoice/order fallback).
            string sql = @"SELECT
                              p.C_Payment_ID,
                              p.AD_Client_ID,
                              p.AD_Org_ID,
                              p.DocumentNo,
                              p.Created,
                              p.CreatedBy,
                              p.DocStatus,
                              p.IsApproved,
                              p.Processed,
                              p.Posted,
                              p.PayAmt,
                              p.PaymentAmount,
                              p.C_BPartner_ID,
                              p.C_Currency_ID,
                              p.C_BankAccount_ID,
                              p.C_BP_BankAccount_ID,
                              p.C_Activity_ID,
                              p.User1_ID,
                              p.User2_ID,
                              p.PONum,
                              p.POReference,
                              p.TenderType,
                              p.VA009_PaymentMethod_ID,
                              p.VA009_ExecutionStatus,
                              p.CheckDate,
                              p.DateTrx,
                              p.DateAcct,
                              p.Description                         AS PaymentNotes,
                              p.C_Invoice_ID                        AS PrimaryInvoiceId,
                              creator.Name                          AS CreatedByName,
                              bp.Name                               AS PayeeName,
                              cur.ISO_Code,
                              cur.CurSymbol,
                              cur.StdPrecision,
                              payee_ba.AccountNo                    AS PayeeAccountNo,
                              payee_bk.Name                         AS PayeeBankName,
                              co_ba.AccountNo                       AS CompanyAccountNo,
                              co_bk.Name                            AS CompanyBankName,
                              act.Value                             AS CostCenterCode,
                              act.Name                              AS CostCenterName,
                              pm.VA009_Name                         AS PaymentMethodName,
                              pm.VA009_PaymentBaseType              AS PaymentMethodBaseType,
                              inv.POReference                       AS InvoicePOReference,
                              term.Name                             AS PaymentTerms,
                              ord.DocumentNo                        AS LinkedOrderDocNo,
                              psx.PayDate                           AS SelectionPayDate,
                              psx.PaymentRule                       AS SelectionPaymentRule
                            FROM C_Payment p
                            LEFT JOIN AD_User      creator ON (p.CreatedBy        = creator.AD_User_ID)
                            LEFT JOIN C_BPartner   bp      ON (p.C_BPartner_ID    = bp.C_BPartner_ID)
                            LEFT JOIN C_Currency   cur     ON (p.C_Currency_ID    = cur.C_Currency_ID)
                            LEFT JOIN C_BP_BankAccount payee_ba ON (p.C_BP_BankAccount_ID = payee_ba.C_BP_BankAccount_ID)
                            LEFT JOIN C_Bank       payee_bk ON (payee_ba.C_Bank_ID = payee_bk.C_Bank_ID)
                            LEFT JOIN C_BankAccount co_ba   ON (p.C_BankAccount_ID = co_ba.C_BankAccount_ID)
                            LEFT JOIN C_Bank       co_bk    ON (co_ba.C_Bank_ID    = co_bk.C_Bank_ID)
                            LEFT JOIN C_Activity   act      ON (p.C_Activity_ID    = act.C_Activity_ID)
                            LEFT JOIN VA009_PaymentMethod pm ON (p.VA009_PaymentMethod_ID = pm.VA009_PaymentMethod_ID)
                            LEFT JOIN C_Invoice    inv      ON (p.C_Invoice_ID     = inv.C_Invoice_ID)
                            LEFT JOIN C_PaymentTerm term    ON (inv.C_PaymentTerm_ID = term.C_PaymentTerm_ID)
                            LEFT JOIN C_Order      ord      ON (inv.C_Order_ID     = ord.C_Order_ID)
                            LEFT JOIN (
                                SELECT psc.C_Payment_ID,
                                       MAX(ps.PayDate)        AS PayDate,
                                       MAX(psc.PaymentRule)   AS PaymentRule
                                FROM C_PaySelectionCheck psc
                                LEFT JOIN C_PaySelection ps
                                       ON (ps.C_PaySelection_ID = psc.C_PaySelection_ID
                                           AND ps.IsActive = 'Y')
                                WHERE psc.IsActive = 'Y'
                                GROUP BY psc.C_Payment_ID
                            ) psx ON (psx.C_Payment_ID = p.C_Payment_ID)
                            WHERE p.C_Payment_ID = @C_Payment_ID
                              AND p.IsActive     = 'Y'
                              AND p.IsReceipt    = 'N'";

            sql = MRole.GetDefault(ctx).AddAccessSQL(
                sql, "p", MRole.SQL_FULLYQUALIFIED, MRole.SQL_RO);

            SqlParameter[] param = new SqlParameter[]
            {
                new SqlParameter("@C_Payment_ID", C_Payment_ID)
            };

            DataSet ds = DB.ExecuteDataset(sql, param, null);
            if (ds == null || ds.Tables.Count == 0 || ds.Tables[0].Rows.Count == 0)
                return false;

            DataRow r = ds.Tables[0].Rows[0];
            result.C_Payment_ID    = Util.GetValueOfInt(r["C_Payment_ID"]);
            result.AD_Client_ID    = Util.GetValueOfInt(r["AD_Client_ID"]);
            result.AD_Org_ID       = Util.GetValueOfInt(r["AD_Org_ID"]);
            result.VoucherNumber   = Util.GetValueOfString(r["DocumentNo"]);
            result.CreatedAt       = Util.GetValueOfDateTime(r["Created"]);
            result.CreatedBy       = Util.GetValueOfInt(r["CreatedBy"]);
            result.CreatedByName   = Util.GetValueOfString(r["CreatedByName"]);
            result.DocStatus       = Util.GetValueOfString(r["DocStatus"]);
            result.IsApproved      = Util.GetValueOfString(r["IsApproved"]) == "Y";
            result.IsProcessed     = Util.GetValueOfString(r["Processed"]) == "Y";
            result.GLPostingStatus = Util.GetValueOfString(r["Posted"]);
            result.ExecutionStatus = Util.GetValueOfString(r["VA009_ExecutionStatus"]);

            decimal payAmt  = Util.GetValueOfDecimal(r["PaymentAmount"]);
            if (payAmt == 0)
                payAmt = Util.GetValueOfDecimal(r["PayAmt"]);
            result.Amount = payAmt;

            result.PayeeId         = Util.GetValueOfInt(r["C_BPartner_ID"]);
            result.PayeeName       = Util.GetValueOfString(r["PayeeName"]);
            result.C_Currency_ID   = Util.GetValueOfInt(r["C_Currency_ID"]);
            result.ISO_Code        = Util.GetValueOfString(r["ISO_Code"]);
            result.CurSymbol       = Util.GetValueOfString(r["CurSymbol"]);
            result.StdPrecision    = Util.GetValueOfInt(r["StdPrecision"]);

            result.PayeeBankName        = Util.GetValueOfString(r["PayeeBankName"]);
            string payeeAcct            = Util.GetValueOfString(r["PayeeAccountNo"]);
            result.PayeeAccountNoLast4  = MaskedTail(payeeAcct, 4);
            result.CompanyBankName      = Util.GetValueOfString(r["CompanyBankName"]);
            string coAcct               = Util.GetValueOfString(r["CompanyAccountNo"]);
            result.CompanyAccountNoLast4 = MaskedTail(coAcct, 4);

            result.PaymentMethodName = ResolvePaymentMethodLabel(
                Util.GetValueOfString(r["PaymentMethodName"]),
                Util.GetValueOfString(r["TenderType"]),
                Util.GetValueOfString(r["SelectionPaymentRule"]));

            // Scheduled payment date: pay-selection PayDate, then CheckDate, then DateTrx, then DateAcct.
            DateTime? payDate = Util.GetValueOfDateTime(r["SelectionPayDate"]);
            if (!payDate.HasValue) payDate = Util.GetValueOfDateTime(r["CheckDate"]);
            if (!payDate.HasValue) payDate = Util.GetValueOfDateTime(r["DateTrx"]);
            if (!payDate.HasValue) payDate = Util.GetValueOfDateTime(r["DateAcct"]);
            result.ScheduledPaymentDate = payDate;

            result.PaymentNotes      = Util.GetValueOfString(r["PaymentNotes"]);
            result.PaymentTerms      = Util.GetValueOfString(r["PaymentTerms"]);
            result.CostCenterCode    = Util.GetValueOfString(r["CostCenterCode"]);
            result.CostCenterName    = Util.GetValueOfString(r["CostCenterName"]);

            // PO reference: payment-level fields first, then linked invoice/order.
            string poRef = Util.GetValueOfString(r["PONum"]);
            if (string.IsNullOrEmpty(poRef)) poRef = Util.GetValueOfString(r["POReference"]);
            if (string.IsNullOrEmpty(poRef)) poRef = Util.GetValueOfString(r["InvoicePOReference"]);
            if (string.IsNullOrEmpty(poRef)) poRef = Util.GetValueOfString(r["LinkedOrderDocNo"]);
            result.PurchaseOrderReference = poRef;

            // UI status mapping. Released wins over Approved wins over In-workflow wins over Draft.
            if (result.IsProcessed && (result.DocStatus == "CO" || result.DocStatus == "CL"))
            {
                result.UiStatus    = "released";
                result.UiSubStatus = Msg.GetMsg(ctx, "VAS_PVStateReleased");
            }
            else if (result.IsApproved)
            {
                result.UiStatus    = "approved";
                result.UiSubStatus = Msg.GetMsg(ctx, "VAS_PVAwaitingRelease");
            }
            else if (result.DocStatus == "DR")
            {
                result.UiStatus    = "draft";
                result.UiSubStatus = Msg.GetMsg(ctx, "VAS_Draft");
            }
            else
            {
                result.UiStatus    = "submitted";
                result.UiSubStatus = Msg.GetMsg(ctx, "VAS_PVStatus_Submitted");
            }

            return true;
        }

        private static string MaskedTail(string accountNo, int n)
        {
            if (string.IsNullOrEmpty(accountNo)) return "";
            if (accountNo.Length <= n) return accountNo;
            return accountNo.Substring(accountNo.Length - n);
        }

        private static string ResolvePaymentMethodLabel(string methodName, string tenderType, string paymentRule)
        {
            if (!string.IsNullOrEmpty(methodName)) return methodName;
            // VA009 base types are not exposed as a master table here; fall back
            // to TenderType / PaymentRule single-letter codes recorded on the row.
            if (!string.IsNullOrEmpty(tenderType)) return tenderType;
            if (!string.IsNullOrEmpty(paymentRule)) return paymentRule;
            return "";
        }

        // ---------- YTD paid for this payee ----------

        private void LoadYtdPaid(PaymentVoucherData result)
        {
            if (result.PayeeId <= 0 || result.C_Currency_ID <= 0) return;

            string sql = @"SELECT NVL(SUM(
                                CASE
                                    WHEN NVL(p.PaymentAmount, 0) <> 0 THEN p.PaymentAmount
                                    ELSE NVL(p.PayAmt, 0)
                                END), 0)                                  AS YtdPaid
                            FROM C_Payment p
                            WHERE p.IsActive       = 'Y'
                              AND p.IsReceipt      = 'N'
                              AND p.DocStatus      IN ('CO', 'CL')
                              AND p.C_BPartner_ID  = @C_BPartner_ID
                              AND p.C_Currency_ID  = @C_Currency_ID
                              AND p.AD_Client_ID   = @AD_Client_ID
                              AND TRUNC(p.DateTrx) >= TRUNC(CURRENT_DATE, 'YYYY')";

            SqlParameter[] param = new SqlParameter[]
            {
                new SqlParameter("@C_BPartner_ID", result.PayeeId),
                new SqlParameter("@C_Currency_ID", result.C_Currency_ID),
                new SqlParameter("@AD_Client_ID",  result.AD_Client_ID)
            };

            try
            {
                object scalar = DB.ExecuteScalar(sql, param, null);
                result.YtdPaidAmount = Util.GetValueOfDecimal(scalar);
            }
            catch (Exception ex)
            {
                VLogger.Get().Severe("VAS_PaymentVoucherPanel YTD query failed: " + ex.Message);
                result.YtdPaidAmount = 0m;
            }
        }

        // ---------- Approval workflow ----------

        private void LoadApprovalWorkflow(PaymentVoucherData result)
        {
            result.WorkflowSteps = new List<WorkflowStepData>();

            // Pull AD_WF_EventAudit entries for this payment record. Active activities
            // (still in flight) come back with a synthetic completion_status = 'active'.
            string sql = @"SELECT
                              'event'                                AS RowType,
                              ea.AD_WF_EventAudit_ID                 AS SourceId,
                              ea.Created                             AS StepTimestamp,
                              ea.EventType                           AS EventType,
                              ea.WFState                             AS WFState,
                              node.Name                              AS NodeName,
                              node.ApprovalLeval                     AS ApprovalLevel,
                              actor.Name                             AS ActorName,
                              actor.Title                            AS ActorTitle,
                              role.Name                              AS RoleName
                           FROM AD_WF_EventAudit ea
                           INNER JOIN AD_WF_Process wp ON (wp.AD_WF_Process_ID = ea.AD_WF_Process_ID)
                           LEFT  JOIN AD_WF_Node node  ON (node.AD_WF_Node_ID  = ea.AD_WF_Node_ID)
                           LEFT  JOIN AD_User actor    ON (actor.AD_User_ID    = NVL(ea.AD_User_ID, ea.CreatedBy))
                           LEFT  JOIN AD_WF_Responsible resp ON (resp.AD_WF_Responsible_ID = node.AD_WF_Responsible_ID)
                           LEFT  JOIN AD_Role role     ON (role.AD_Role_ID = resp.AD_Role_ID)
                           WHERE wp.Record_ID  = @Record_ID
                             AND wp.AD_Table_ID = (SELECT AD_Table_ID FROM AD_Table WHERE TableName = 'C_Payment')
                           UNION ALL
                           SELECT
                              'activity'                             AS RowType,
                              act.AD_WF_Activity_ID                  AS SourceId,
                              act.Created                            AS StepTimestamp,
                              NULL                                   AS EventType,
                              act.WFState                            AS WFState,
                              node.Name                              AS NodeName,
                              node.ApprovalLeval                     AS ApprovalLevel,
                              actor.Name                             AS ActorName,
                              actor.Title                            AS ActorTitle,
                              role.Name                              AS RoleName
                           FROM AD_WF_Activity act
                           INNER JOIN AD_WF_Process wp ON (wp.AD_WF_Process_ID = act.AD_WF_Process_ID)
                           LEFT  JOIN AD_WF_Node node  ON (node.AD_WF_Node_ID  = act.AD_WF_Node_ID)
                           LEFT  JOIN AD_User actor    ON (actor.AD_User_ID    = NVL(act.AD_User_ID, act.CreatedBy))
                           LEFT  JOIN AD_WF_Responsible resp ON (resp.AD_WF_Responsible_ID = act.AD_WF_Responsible_ID)
                           LEFT  JOIN AD_Role role     ON (role.AD_Role_ID = resp.AD_Role_ID)
                           WHERE wp.Record_ID  = @Record_ID
                             AND wp.AD_Table_ID = (SELECT AD_Table_ID FROM AD_Table WHERE TableName = 'C_Payment')
                             AND act.IsActive  = 'Y'
                             AND act.WFState NOT IN ('CC', 'CL', 'CO')
                           ORDER BY 4, 7";

            SqlParameter[] param = new SqlParameter[]
            {
                new SqlParameter("@Record_ID", result.C_Payment_ID)
            };

            DataSet ds;
            try
            {
                ds = DB.ExecuteDataset(sql, param, null);
            }
            catch (Exception ex)
            {
                // GAP: AD_WF_* schema may differ across tenants. Fall back to coarse stage.
                VLogger.Get().Severe("VAS_PaymentVoucherPanel workflow query failed: " + ex.Message);
                BuildCoarseWorkflowStages(result);
                return;
            }

            int approverCount    = 0;
            int approvedCount    = 0;
            string finalApprover = null;
            string finalTitle    = null;

            if (ds != null && ds.Tables.Count > 0)
            {
                foreach (DataRow r in ds.Tables[0].Rows)
                {
                    WorkflowStepData step = new WorkflowStepData();
                    step.RawLabel       = Util.GetValueOfString(r["NodeName"]);
                    step.Label          = MapStepLabel(step.RawLabel, Util.GetValueOfString(r["EventType"]));
                    step.RowType        = Util.GetValueOfString(r["RowType"]);
                    step.Status         = step.RowType == "activity" ? "active" : "complete";
                    step.ActorName      = Util.GetValueOfString(r["ActorName"]);
                    step.ActorRoleTitle = Util.GetValueOfString(r["ActorTitle"]);
                    if (string.IsNullOrEmpty(step.ActorRoleTitle))
                        step.ActorRoleTitle = Util.GetValueOfString(r["RoleName"]);
                    step.Timestamp      = Util.GetValueOfDateTime(r["StepTimestamp"]);

                    if (step.Label == "Approved" || step.RawLabel != null && step.RawLabel.ToLower().Contains("approve"))
                    {
                        approverCount++;
                        if (step.Status == "complete")
                        {
                            approvedCount++;
                            finalApprover = step.ActorName;
                            finalTitle    = step.ActorRoleTitle;
                        }
                    }
                    result.WorkflowSteps.Add(step);
                }
            }

            // Fall back to coarse stages if no workflow rows existed.
            if (result.WorkflowSteps.Count == 0)
            {
                BuildCoarseWorkflowStages(result);
            }

            // Approver counts: prefer workflow-derived; fall back to approval flag.
            result.ApproverCount = approverCount > 0 ? approverCount : (result.IsApproved ? 1 : 0);
            result.ApprovedCount = approvedCount > 0 ? approvedCount : (result.IsApproved ? 1 : 0);
            result.FinalApproverName  = finalApprover;
            result.FinalApproverTitle = finalTitle;

            // Release queue: surface only when the voucher is approved + waiting.
            if (result.UiStatus == "approved")
            {
                result.ReleaseQueue = BuildReleaseQueue(result);
            }
        }

        // GAP: No explicit Drafted/Submitted/Reviewed/Approved/Released stage table
        // exists. We synthesize the five lifecycle stage labels from DocStatus +
        // IsApproved + Processed when no AD_WF_* rows are available.
        private void BuildCoarseWorkflowStages(PaymentVoucherData result)
        {
            result.WorkflowSteps.Clear();

            string status = result.DocStatus ?? "";
            bool draftDone     = true;
            bool submitDone    = status != "DR";
            bool reviewedDone  = result.IsApproved || result.IsProcessed;
            bool approvedDone  = result.IsApproved || result.IsProcessed;
            bool releasedDone  = result.IsProcessed && (status == "CO" || status == "CL");

            AddCoarseStep(result, "Drafted",   draftDone,    result.CreatedAt, result.CreatedByName);
            AddCoarseStep(result, "Submitted", submitDone,   null, null);
            AddCoarseStep(result, "Reviewed",  reviewedDone, null, null);
            AddCoarseStep(result, "Approved",  approvedDone, null, null);
            AddCoarseStep(result, "Released",  releasedDone, null, null);
        }

        private void AddCoarseStep(PaymentVoucherData result, string label, bool done,
                                    DateTime? ts, string actor)
        {
            string status;
            if (done) status = "complete";
            else if (IsNextActiveStage(result, label)) status = "active";
            else status = "pending";

            result.WorkflowSteps.Add(new WorkflowStepData
            {
                Label     = label,
                RawLabel  = label,
                Status    = status,
                ActorName = actor,
                Timestamp = ts
            });
        }

        private static bool IsNextActiveStage(PaymentVoucherData result, string label)
        {
            // The first non-complete stage is "active"; the rest are "pending".
            string status = result.DocStatus ?? "";
            if (label == "Submitted" && status == "DR") return true;
            if (label == "Reviewed"  && status != "DR" && !result.IsApproved && !result.IsProcessed) return true;
            if (label == "Approved"  && status != "DR" && !result.IsApproved && !result.IsProcessed) return false;
            if (label == "Released"  && result.IsApproved && !result.IsProcessed) return true;
            return false;
        }

        private static string MapStepLabel(string nodeName, string eventType)
        {
            string s = ((nodeName ?? "") + " " + (eventType ?? "")).ToLower();
            if (s.Contains("draft"))   return "Drafted";
            if (s.Contains("submit"))  return "Submitted";
            if (s.Contains("review"))  return "Reviewed";
            if (s.Contains("release")) return "Released";
            if (s.Contains("approve")) return "Approved";
            return string.IsNullOrEmpty(nodeName) ? "Workflow" : nodeName;
        }

        // GAP: No dedicated Treasury queue / bank-file-pending table was found.
        // Surface a generic queue description and compute waiting days from the
        // approval/created timestamp.
        private ReleaseQueueData BuildReleaseQueue(PaymentVoucherData result)
        {
            ReleaseQueueData q = new ReleaseQueueData();
            q.QueueName = "Treasury queue";
            q.PendingArtifactDescription = "bank file pending";

            DateTime since = DateTime.Today;
            // Use the latest "Approved" workflow step timestamp if present.
            for (int i = result.WorkflowSteps.Count - 1; i >= 0; i--)
            {
                if (result.WorkflowSteps[i].Label == "Approved"
                    && result.WorkflowSteps[i].Status == "complete"
                    && result.WorkflowSteps[i].Timestamp.HasValue)
                {
                    since = result.WorkflowSteps[i].Timestamp.Value.Date;
                    break;
                }
            }
            int diff = (int)Math.Round((DateTime.Today - since).TotalDays);
            q.WaitingDays = diff < 0 ? 0 : diff;
            return q;
        }

        // ---------- Allocations ----------

        private void LoadAllocations(PaymentVoucherData result)
        {
            result.Allocations = new List<VoucherAllocationData>();

            // Committed allocations come from C_AllocationLine. If none exist,
            // fall back to draft C_PaymentAllocate; if still empty, fall back to
            // the direct invoice on C_Payment.C_Invoice_ID. Three UNIONed CTE-style
            // selects below handle that priority chain.
            string sql = @"WITH committed_alloc AS (
                              SELECT al.C_AllocationLine_ID  AS AllocationId,
                                     al.C_Invoice_ID,
                                     al.Amount               AS AllocatedAmount,
                                     'C_AllocationLine'      AS AllocationSource
                                FROM C_AllocationLine al
                               WHERE al.C_Payment_ID = @C_Payment_ID
                                 AND al.IsActive    = 'Y'
                           ), draft_alloc AS (
                              SELECT pa.C_PaymentAllocate_ID AS AllocationId,
                                     pa.C_Invoice_ID,
                                     pa.Amount               AS AllocatedAmount,
                                     'C_PaymentAllocate'     AS AllocationSource
                                FROM C_PaymentAllocate pa
                               WHERE pa.C_Payment_ID = @C_Payment_ID
                                 AND pa.IsActive    = 'Y'
                                 AND NOT EXISTS (
                                       SELECT 1 FROM C_AllocationLine al
                                        WHERE al.C_Payment_ID = pa.C_Payment_ID
                                          AND al.IsActive    = 'Y')
                           ), direct_alloc AS (
                              SELECT p.C_Payment_ID          AS AllocationId,
                                     p.C_Invoice_ID,
                                     CASE WHEN NVL(p.PaymentAmount, 0) <> 0 THEN p.PaymentAmount
                                          ELSE NVL(p.PayAmt, 0) END AS AllocatedAmount,
                                     'C_Payment'             AS AllocationSource
                                FROM C_Payment p
                               WHERE p.C_Payment_ID = @C_Payment_ID
                                 AND p.C_Invoice_ID IS NOT NULL
                                 AND NOT EXISTS (SELECT 1 FROM C_AllocationLine al
                                                   WHERE al.C_Payment_ID = p.C_Payment_ID
                                                     AND al.IsActive = 'Y')
                                 AND NOT EXISTS (SELECT 1 FROM C_PaymentAllocate pa
                                                   WHERE pa.C_Payment_ID = p.C_Payment_ID
                                                     AND pa.IsActive = 'Y')
                           ), allocation_rows AS (
                              SELECT * FROM committed_alloc
                              UNION ALL
                              SELECT * FROM draft_alloc
                              UNION ALL
                              SELECT * FROM direct_alloc
                           ), invoice_open AS (
                              SELECT ips.C_Invoice_ID,
                                     SUM(NVL(ips.VA009_OpenAmnt, ips.DueAmt))  AS RemainingFromSchedule,
                                     MAX(ips.VA009_IsPaid)                     AS ScheduleIsPaid
                                FROM C_InvoicePaySchedule ips
                               WHERE ips.IsActive = 'Y'
                               GROUP BY ips.C_Invoice_ID
                           )
                           SELECT
                              ar.AllocationId,
                              ar.AllocationSource,
                              i.C_Invoice_ID,
                              i.DocumentNo                AS InvoiceNumber,
                              i.Description               AS InvoiceDescription,
                              i.DateInvoiced              AS InvoiceDate,
                              i.GrandTotal                AS InvoiceTotalAmount,
                              ar.AllocatedAmount,
                              i.IsPaid                    AS InvoiceIsPaid,
                              io.ScheduleIsPaid,
                              io.RemainingFromSchedule
                           FROM allocation_rows ar
                           INNER JOIN C_Invoice i ON (i.C_Invoice_ID = ar.C_Invoice_ID)
                           LEFT  JOIN invoice_open io ON (io.C_Invoice_ID = i.C_Invoice_ID)
                           ORDER BY i.DateInvoiced, i.DocumentNo";

            SqlParameter[] param = new SqlParameter[]
            {
                new SqlParameter("@C_Payment_ID", result.C_Payment_ID)
            };

            DataSet ds;
            try
            {
                ds = DB.ExecuteDataset(sql, param, null);
            }
            catch (Exception ex)
            {
                VLogger.Get().Severe("VAS_PaymentVoucherPanel allocations query failed: " + ex.Message);
                return;
            }
            if (ds == null || ds.Tables.Count == 0) return;

            decimal allocTotal = 0m;
            foreach (DataRow r in ds.Tables[0].Rows)
            {
                VoucherAllocationData a = new VoucherAllocationData();
                a.AllocationId        = Util.GetValueOfInt(r["AllocationId"]);
                a.AllocationSource    = Util.GetValueOfString(r["AllocationSource"]);
                a.InvoiceId           = Util.GetValueOfInt(r["C_Invoice_ID"]);
                a.InvoiceNumber       = Util.GetValueOfString(r["InvoiceNumber"]);
                a.Description         = Util.GetValueOfString(r["InvoiceDescription"]);
                a.InvoiceDate         = Util.GetValueOfDateTime(r["InvoiceDate"]);
                a.InvoiceTotalAmount  = Util.GetValueOfDecimal(r["InvoiceTotalAmount"]);
                a.AllocatedAmount     = Util.GetValueOfDecimal(r["AllocatedAmount"]);

                // Remaining balance: prefer C_InvoicePaySchedule open amounts; fall
                // back to invoice GrandTotal minus this allocation if no schedule.
                if (r["RemainingFromSchedule"] != DBNull.Value)
                    a.RemainingBalance = Util.GetValueOfDecimal(r["RemainingFromSchedule"]);
                else
                    a.RemainingBalance = a.InvoiceTotalAmount - a.AllocatedAmount;

                bool invoicePaid  = Util.GetValueOfString(r["InvoiceIsPaid"]) == "Y";
                bool schedulePaid = Util.GetValueOfString(r["ScheduleIsPaid"]) == "Y";

                if (invoicePaid || schedulePaid || a.RemainingBalance <= 0)
                    a.SettlementStatus = "closed";
                else if (a.AllocatedAmount > 0)
                    a.SettlementStatus = "partial";
                else
                    a.SettlementStatus = "open";

                allocTotal += a.AllocatedAmount;
                result.Allocations.Add(a);
            }

            result.AllocatedTotal = allocTotal;
            decimal voucherAmt = result.Amount;
            if (voucherAmt > 0 && allocTotal == voucherAmt)
                result.MatchStatus = "fully_matched";
            else if (allocTotal > 0)
                result.MatchStatus = "partially_matched";
            else
                result.MatchStatus = "unmatched";
        }

        // ---------- Activity log (newest 5) ----------

        private void LoadActivity(PaymentVoucherData result)
        {
            result.Activity = new List<VoucherActivityData>();

            // Compose recent events from workflow audit, allocation creation,
            // and the action log. Newest first; cap at 5.
            // GAP: No single voucher activity table. Composed across audit/log tables.
            string sql = @"SELECT * FROM (
                              SELECT
                                  CASE
                                      WHEN LOWER(NVL(node.Name, ea.EventType)) LIKE '%release%' THEN 'released'
                                      WHEN LOWER(NVL(node.Name, ea.EventType)) LIKE '%reject%'  THEN 'rejected'
                                      WHEN LOWER(NVL(node.Name, ea.EventType)) LIKE '%approve%' THEN 'approved'
                                      WHEN LOWER(NVL(node.Name, ea.EventType)) LIKE '%review%'  THEN 'reviewed'
                                      ELSE 'reviewed'
                                  END                                  AS EventType,
                                  actor.Name                           AS ActorName,
                                  NVL(node.Name, ea.EventType)         AS NodeLabel,
                                  ea.Created                           AS EventTimestamp
                              FROM AD_WF_EventAudit ea
                              INNER JOIN AD_WF_Process wp ON (wp.AD_WF_Process_ID = ea.AD_WF_Process_ID)
                              LEFT  JOIN AD_WF_Node node  ON (node.AD_WF_Node_ID = ea.AD_WF_Node_ID)
                              LEFT  JOIN AD_User actor    ON (actor.AD_User_ID    = NVL(ea.AD_User_ID, ea.CreatedBy))
                              WHERE wp.Record_ID  = @Record_ID
                                AND wp.AD_Table_ID = (SELECT AD_Table_ID FROM AD_Table WHERE TableName = 'C_Payment')
                              UNION ALL
                              SELECT 'allocation_changed' AS EventType,
                                     actor.Name                                                 AS ActorName,
                                     'Allocation revised'                                       AS NodeLabel,
                                     al.Created                                                 AS EventTimestamp
                                FROM C_AllocationLine al
                                LEFT JOIN AD_User actor ON (actor.AD_User_ID = al.CreatedBy)
                               WHERE al.C_Payment_ID = @Record_ID
                                 AND al.IsActive    = 'Y'
                              UNION ALL
                              SELECT 'created' AS EventType,
                                     creator.Name                                               AS ActorName,
                                     'Created'                                                  AS NodeLabel,
                                     p.Created                                                  AS EventTimestamp
                                FROM C_Payment p
                                LEFT JOIN AD_User creator ON (creator.AD_User_ID = p.CreatedBy)
                               WHERE p.C_Payment_ID = @Record_ID
                          ) events
                          ORDER BY EventTimestamp DESC";

            SqlParameter[] param = new SqlParameter[]
            {
                new SqlParameter("@Record_ID", result.C_Payment_ID)
            };

            DataSet ds;
            try
            {
                ds = DB.ExecuteDataset(sql, param, null);
            }
            catch (Exception ex)
            {
                VLogger.Get().Severe("VAS_PaymentVoucherPanel activity query failed: " + ex.Message);
                return;
            }
            if (ds == null || ds.Tables.Count == 0) return;

            int max = 5;
            int count = 0;
            foreach (DataRow r in ds.Tables[0].Rows)
            {
                if (count >= max) break;
                VoucherActivityData a = new VoucherActivityData();
                a.EventType = Util.GetValueOfString(r["EventType"]);
                a.ActorName = Util.GetValueOfString(r["ActorName"]);
                a.Label     = BuildActivityLabel(a.EventType, a.ActorName,
                                                 Util.GetValueOfString(r["NodeLabel"]));
                a.Timestamp = Util.GetValueOfDateTime(r["EventTimestamp"]);
                result.Activity.Add(a);
                count++;
            }
        }

        private static string BuildActivityLabel(string eventType, string actor, string nodeLabel)
        {
            string verb;
            switch (eventType)
            {
                case "released":           verb = "Released";   break;
                case "approved":           verb = "Approved";   break;
                case "reviewed":           verb = "Reviewed";   break;
                case "rejected":           verb = "Rejected";   break;
                case "allocation_changed": verb = "Allocation revised"; break;
                case "note_edited":        verb = "Notes edited"; break;
                case "created":            verb = "Created";    break;
                default:                   verb = nodeLabel ?? eventType ?? "Event"; break;
            }
            if (!string.IsNullOrEmpty(actor))
                return verb + " by " + actor;
            return verb;
        }

        // ---------- Permissions (treasury_release, edit_payment_notes) ----------

        // GAP: No literal treasury_release / edit_payment_notes permission keys
        // exist in the schema. Resolve them via existing role/doc-action/process
        // access. This is a best-effort resolution and may need tightening per
        // tenant configuration.
        private void LoadPermissions(Ctx ctx, PaymentVoucherData result)
        {
            result.CanRelease     = false;
            result.CanEditNotes   = false;

            try
            {
                MRole role = MRole.GetDefault(ctx);
                if (role == null) return;

                // Treasury release proxy: any role-granted update access on C_Payment.
                int paymentTableId = (int)Util.GetValueOfInt(DB.ExecuteScalar(
                    "SELECT AD_Table_ID FROM AD_Table WHERE TableName = 'C_Payment'", null, null));
                if (paymentTableId > 0)
                {
                    bool canUpdate = role.CanUpdate(ctx.GetAD_Client_ID(),
                                                    result.AD_Org_ID, paymentTableId,
                                                    result.C_Payment_ID, false);
                    result.CanRelease   = canUpdate && result.UiStatus == "approved";
                    result.CanEditNotes = canUpdate && result.UiStatus != "released";
                }
            }
            catch (Exception ex)
            {
                VLogger.Get().Severe("VAS_PaymentVoucherPanel permissions resolve failed: " + ex.Message);
            }
        }

        // ---------- Release now mutation ----------

        /// <summary>
        /// Triggers the release of an approved payment voucher by completing
        /// the C_Payment document via the standard MPayment.ProcessIt path.
        /// Returns the refreshed payload on success so the client can re-render
        /// without a separate fetch.
        /// </summary>
        public ReleaseNowResult ReleaseNow(Ctx ctx, int C_Payment_ID)
        {
            ReleaseNowResult result = new ReleaseNowResult();
            if (C_Payment_ID <= 0)
            {
                result.Message = Msg.GetMsg(ctx, "VAS_PVReleaseFailed");
                return result;
            }

            Trx trx = Trx.GetTrx(Trx.CreateTrxName("VASPVRelease"));
            try
            {
                MPayment payment = new MPayment(ctx, C_Payment_ID, trx);
                if (payment.GetC_Payment_ID() <= 0)
                {
                    result.Message = Msg.GetMsg(ctx, "VAS_PVReleaseNotFound");
                    return result;
                }
                if (payment.IsReceipt())
                {
                    result.Message = Msg.GetMsg(ctx, "VAS_PVReleaseNotAllowed");
                    return result;
                }
                if (!payment.IsApproved())
                {
                    result.Message = Msg.GetMsg(ctx, "VAS_PVReleaseNotApproved");
                    return result;
                }

                bool processed;
                try
                {
                    processed = payment.ProcessIt(MPayment.DOCACTION_Complete);
                }
                catch (Exception pex)
                {
                    processed = false;
                    result.Message = Msg.GetMsg(ctx, "VAS_PVReleaseFailed") + ": " + pex.Message;
                }
                if (!processed)
                {
                    if (string.IsNullOrEmpty(result.Message))
                    {
                        ValueNamePair pp = VLogger.RetrieveError();
                        result.Message = Msg.GetMsg(ctx, "VAS_PVReleaseFailed") +
                                         (pp != null ? ": " + pp.GetName() : "");
                    }
                    trx.Rollback();
                    return result;
                }
                if (!payment.Save(trx))
                {
                    ValueNamePair pp = VLogger.RetrieveError();
                    result.Message = Msg.GetMsg(ctx, "VAS_PVReleaseFailed") +
                                     (pp != null ? ": " + pp.GetName() : "");
                    trx.Rollback();
                    return result;
                }

                trx.Commit();
                result.Success     = true;
                result.DocumentNo  = payment.GetDocumentNo();
                result.DocStatus   = payment.GetDocStatus();
            }
            catch (Exception ex)
            {
                try { trx.Rollback(); } catch { /* ignore */ }
                result.Message = Msg.GetMsg(ctx, "VAS_PVReleaseFailed") + ": " + ex.Message;
            }
            finally
            {
                try { trx.Close(); } catch { /* ignore */ }
            }

            return result;
        }

        // ---------- DTOs ----------

        public class PaymentVoucherData
        {
            public int       C_Payment_ID    { get; set; }
            public int       AD_Client_ID    { get; set; }
            public int       AD_Org_ID       { get; set; }
            public string    VoucherNumber   { get; set; }
            public DateTime? CreatedAt       { get; set; }
            public int       CreatedBy       { get; set; }
            public string    CreatedByName   { get; set; }
            public string    DocStatus       { get; set; }
            public bool      IsApproved      { get; set; }
            public bool      IsProcessed     { get; set; }
            public string    UiStatus        { get; set; }
            public string    UiSubStatus     { get; set; }
            public string    GLPostingStatus { get; set; }
            public string    ExecutionStatus { get; set; }

            // Payee
            public int     PayeeId               { get; set; }
            public string  PayeeName             { get; set; }
            public string  PayeeBankName         { get; set; }
            public string  PayeeAccountNoLast4   { get; set; }
            public string  CompanyBankName       { get; set; }
            public string  CompanyAccountNoLast4 { get; set; }
            public decimal YtdPaidAmount         { get; set; }

            // Amount / currency / scheduled date
            public decimal   Amount             { get; set; }
            public int       C_Currency_ID      { get; set; }
            public string    ISO_Code           { get; set; }
            public string    CurSymbol          { get; set; }
            public int       StdPrecision       { get; set; }
            public string    PaymentMethodName  { get; set; }
            public DateTime? ScheduledPaymentDate { get; set; }

            // Approval roll-up + final approver
            public int    ApprovedCount        { get; set; }
            public int    ApproverCount        { get; set; }
            public string FinalApproverName    { get; set; }
            public string FinalApproverTitle   { get; set; }

            // Notes / metadata
            public string PaymentNotes              { get; set; }
            public string PaymentTerms              { get; set; }
            public string CostCenterCode            { get; set; }
            public string CostCenterName            { get; set; }
            public string PurchaseOrderReference    { get; set; }

            // Permissions
            public bool CanRelease   { get; set; }
            public bool CanEditNotes { get; set; }

            // Section data
            public List<WorkflowStepData>     WorkflowSteps  { get; set; }
            public ReleaseQueueData           ReleaseQueue   { get; set; }
            public List<VoucherAllocationData> Allocations   { get; set; }
            public decimal                    AllocatedTotal { get; set; }
            public string                     MatchStatus    { get; set; }
            public List<VoucherActivityData>  Activity       { get; set; }
        }

        public class WorkflowStepData
        {
            public string    Label          { get; set; }
            public string    RawLabel       { get; set; }
            public string    Status         { get; set; } // complete | active | pending
            public string    RowType        { get; set; }
            public string    ActorName      { get; set; }
            public string    ActorRoleTitle { get; set; }
            public DateTime? Timestamp      { get; set; }
        }

        public class ReleaseQueueData
        {
            public string QueueName                   { get; set; }
            public string PendingArtifactDescription  { get; set; }
            public int    WaitingDays                 { get; set; }
        }

        public class VoucherAllocationData
        {
            public int       AllocationId       { get; set; }
            public string    AllocationSource   { get; set; }
            public int       InvoiceId          { get; set; }
            public string    InvoiceNumber      { get; set; }
            public string    Description        { get; set; }
            public DateTime? InvoiceDate        { get; set; }
            public decimal   InvoiceTotalAmount { get; set; }
            public decimal   AllocatedAmount    { get; set; }
            public decimal   RemainingBalance   { get; set; }
            public string    SettlementStatus   { get; set; } // open | partial | closed
        }

        public class VoucherActivityData
        {
            public string    EventType { get; set; }
            public string    ActorName { get; set; }
            public string    Label     { get; set; }
            public DateTime? Timestamp { get; set; }
        }

        public class ReleaseNowResult
        {
            public bool   Success    { get; set; }
            public string DocumentNo { get; set; }
            public string DocStatus  { get; set; }
            public string Message    { get; set; }
        }
    }
}
