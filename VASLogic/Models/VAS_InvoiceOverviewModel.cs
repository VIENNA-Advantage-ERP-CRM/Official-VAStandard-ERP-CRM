/******************************************************
 * Module Name    : VASLogic
 * Purpose        : Invoice Overview tab panel data
 * chronological  : Development
 * Created Date   : 30 April 2026
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
    public class VAS_InvoiceOverviewModel
    {
        /// <summary>
        /// Returns the overview details for the selected C_Invoice record:
        /// header (DocumentNo, DateInvoiced), customer + contact, next unpaid
        /// due date, consolidated outstanding amount, and the count of open
        /// invoices for the same business partner.
        /// </summary>
        public InvoiceOverviewData GetInvoiceOverview(Ctx ctx, int C_Invoice_ID)
        {
            InvoiceOverviewData result = new InvoiceOverviewData();
            if (C_Invoice_ID <= 0) return result;

            string sql = @"SELECT
                              i.C_Invoice_ID,
                              i.DocumentNo,
                              i.DateInvoiced,
                              i.C_BPartner_ID,
                              i.Created,
                              i.Updated,
                              i.IsApproved,
                              i.IsPrinted,
                              i.DocStatus,
                              i.TotalLines,
                              i.GrandTotal,
                              bp.Name           AS BPName,
                              bpc.Name          AS ContactName,
                              bpc.Title         AS ContactTitle,
                              cu.Name           AS CreatedByName,
                              uu.Name           AS UpdatedByName,
                              cur.CurSymbol,
                              cur.ISO_Code,
                              cur.StdPrecision,
                              (SELECT MIN(ips.DueDate)
                                 FROM C_InvoicePaySchedule ips
                                WHERE ips.C_Invoice_ID = i.C_Invoice_ID
                                  AND ips.VA009_IsPaid = 'N')                 AS NextDueDate,
                              (SELECT NVL(SUM(ips.DueAmt), 0)
                                 FROM C_InvoicePaySchedule ips
                                WHERE ips.C_Invoice_ID = i.C_Invoice_ID
                                  AND ips.VA009_IsPaid = 'N')                 AS Outstanding,
                              (SELECT COUNT(*)
                                 FROM C_Invoice ci
                                WHERE ci.IsSoTrx      = 'Y'
                                  AND ci.IsReturnTrx  = 'N'
                                  AND ci.IsPaid       = 'N'
                                  AND ci.C_BPartner_ID = i.C_BPartner_ID)     AS OpenInvoiceCount,
                              (SELECT COUNT(*)
                                 FROM C_InvoicePaySchedule s
                                WHERE s.C_Invoice_ID  = i.C_Invoice_ID)       AS ScheduleCount,
                              (SELECT COUNT(*)
                                 FROM C_InvoicePaySchedule s
                                WHERE s.C_Invoice_ID  = i.C_Invoice_ID
                                  AND s.VA009_IsPaid  = 'Y')                  AS PaidScheduleCount,
                              (SELECT MAX(ah.DateAcct)
                                 FROM C_AllocationHdr ah
                                 INNER JOIN C_AllocationLine al
                                    ON (ah.C_AllocationHdr_ID = al.C_AllocationHdr_ID)
                                 INNER JOIN C_InvoicePaySchedule ips
                                    ON (al.C_InvoicePaySchedule_ID = ips.C_InvoicePaySchedule_ID)
                                WHERE ips.C_Invoice_ID = i.C_Invoice_ID
                                  AND ips.VA009_IsPaid = 'Y'
                                  AND ah.DocStatus IN ('CO','CL'))            AS PaidDate,
                              (SELECT MAX(wfea.Created)
                                 FROM AD_WF_EventAudit wfea
                                 INNER JOIN AD_WF_Process wfp
                                    ON (wfp.AD_WF_Process_ID = wfea.AD_WF_Process_ID)
                                WHERE wfp.Record_ID  = i.C_Invoice_ID
                                  AND wfp.AD_Table_ID =
                                      (SELECT AD_Table_ID FROM AD_Table
                                        WHERE TableName = 'C_Invoice'))       AS WFLastEventDate,
                              TRUNC(CURRENT_DATE) AS SystemDate
                            FROM C_Invoice i
                            INNER JOIN C_BPartner bp  ON (i.C_BPartner_ID = bp.C_BPartner_ID)
                            LEFT  JOIN AD_User    bpc ON (i.AD_User_ID    = bpc.AD_User_ID)
                            LEFT  JOIN AD_User    cu  ON (i.CreatedBy     = cu.AD_User_ID)
                            LEFT  JOIN AD_User    uu  ON (i.UpdatedBy     = uu.AD_User_ID)
                            INNER JOIN C_Currency cur ON (i.C_Currency_ID  = cur.C_Currency_ID)
                            WHERE i.C_Invoice_ID = @C_Invoice_ID";

            sql = MRole.GetDefault(ctx).AddAccessSQL(
                sql, "i", MRole.SQL_FULLYQUALIFIED, MRole.SQL_RO);

            SqlParameter[] param = new SqlParameter[]
            {
                new SqlParameter("@C_Invoice_ID", C_Invoice_ID)
            };

            DataSet ds = DB.ExecuteDataset(sql, param, null);
            if (ds == null || ds.Tables.Count == 0 || ds.Tables[0].Rows.Count == 0)
                return result;

            DataRow r = ds.Tables[0].Rows[0];
            result.C_Invoice_ID    = Util.GetValueOfInt(r["C_Invoice_ID"]);
            result.DocumentNo      = Util.GetValueOfString(r["DocumentNo"]);
            result.DateInvoiced    = Util.GetValueOfDateTime(r["DateInvoiced"]);
            result.C_BPartner_ID   = Util.GetValueOfInt(r["C_BPartner_ID"]);
            result.DocStatus       = Util.GetValueOfString(r["DocStatus"]);
            result.BPName          = Util.GetValueOfString(r["BPName"]);
            result.ContactName     = Util.GetValueOfString(r["ContactName"]);
            result.ContactTitle    = Util.GetValueOfString(r["ContactTitle"]);
            result.CurSymbol       = Util.GetValueOfString(r["CurSymbol"]);
            result.ISO_Code        = Util.GetValueOfString(r["ISO_Code"]);
            result.StdPrecision    = Util.GetValueOfInt(r["StdPrecision"]);
            result.OpenInvoiceCount = Util.GetValueOfInt(r["OpenInvoiceCount"]);
            result.Outstanding     = Util.GetValueOfDecimal(r["Outstanding"]);

            // ----- Status timeline -----
            result.Created         = Util.GetValueOfDateTime(r["Created"]);
            result.CreatedByName   = Util.GetValueOfString(r["CreatedByName"]);
            result.IsApproved      = Util.GetValueOfString(r["IsApproved"]) == "Y";

            DateTime? wfDate = Util.GetValueOfDateTime(r["WFLastEventDate"]);
            result.ApprovedDate    = wfDate.HasValue ? wfDate : Util.GetValueOfDateTime(r["Updated"]);
            result.ApprovedByName  = Util.GetValueOfString(r["UpdatedByName"]);

            // Sent: per spec — marked True (no specific source defined).
            result.IsSent          = true;

            int totalSched = Util.GetValueOfInt(r["ScheduleCount"]);
            int paidSched  = Util.GetValueOfInt(r["PaidScheduleCount"]);
            result.IsFullyPaid     = totalSched > 0 && totalSched == paidSched;
            result.PaidDate        = Util.GetValueOfDateTime(r["PaidDate"]);

            result.IsClosed        = result.IsApproved && result.IsSent && result.IsFullyPaid;

            // ----- Header totals -----
            result.TotalLines      = Util.GetValueOfDecimal(r["TotalLines"]);
            result.GrandTotal      = Util.GetValueOfDecimal(r["GrandTotal"]);
            result.TaxAmt          = result.GrandTotal - result.TotalLines;

            // ----- Invoice lines -----
            result.Lines = LoadLines(C_Invoice_ID, result.StdPrecision);

            // ----- Recurring-eligibility roll-up -----
            int recurringEligible = 0;
            int physicalItems     = 0;
            int recurringTotal    = 0;
            foreach (InvoiceLineData ln in result.Lines)
            {
                if (ln.IsDescription) continue;
                recurringTotal++;
                if (ln.IsRecurringEligible) recurringEligible++;
                else if (ln.M_Product_ID > 0 &&
                         (ln.ProductType == "I" || ln.ProductType == "R"))
                {
                    physicalItems++;
                }
            }
            result.EligibleRecurringCount = recurringEligible;
            result.PhysicalItemsCount     = physicalItems;
            result.RecurringLineCount     = recurringTotal;

            // ----- Notes (CM_Chat / CM_ChatEntry) -----
            result.Notes = LoadNotes(C_Invoice_ID);
            int visibleOnPdf = 0;
            foreach (NoteData n in result.Notes)
                if (n.IsVisibleToCustomer) visibleOnPdf++;
            result.VisibleOnPDFCount = visibleOnPdf;

            // ----- Payment risk (historical AR invoices for the same customer) -----
            LoadPaymentRisk(result, C_Invoice_ID);

            object due = r["NextDueDate"];
            object today = r["SystemDate"];
            if (due != null && due != DBNull.Value)
            {
                DateTime dueDate    = Util.GetValueOfDateTime(due).GetValueOrDefault();
                DateTime systemDate = (today != null && today != DBNull.Value)
                                      ? Util.GetValueOfDateTime(today).GetValueOrDefault(DateTime.Today)
                                      : DateTime.Today;

                result.DueDate = dueDate;
                int diff = (int)Math.Round((dueDate.Date - systemDate.Date).TotalDays);
                if (diff >= 0)
                {
                    result.IsOverdue        = false;
                    result.DaysDifference   = diff;
                }
                else
                {
                    result.IsOverdue        = true;
                    result.DaysDifference   = -diff;
                }
            }

            return result;
        }

        /// <summary>
        /// Loads C_InvoiceLine rows for the given invoice with product/charge
        /// metadata, UOM symbol, and price/qty precision.
        /// </summary>
        private List<InvoiceLineData> LoadLines(int C_Invoice_ID, int defaultPrecision)
        {
            List<InvoiceLineData> lines = new List<InvoiceLineData>();

            string sql = @"SELECT
                              il.C_InvoiceLine_ID,
                              il.Line,
                              il.QtyInvoiced,
                              il.PriceActual,
                              il.LineNetAmt,
                              il.Description    AS LineDescription,
                              il.IsDescription,
                              il.M_Product_ID,
                              il.C_Charge_ID,
                              p.Name            AS ProductName,
                              p.ProductType     AS ProductType,
                              ch.Name           AS ChargeName,
                              uom.UOMSymbol     AS UOMSymbol,
                              uom.StdPrecision  AS UOMPrecision,
                              NVL(pl.PricePrecision, 2) AS PricePrecision,
                              CASE
                                 WHEN il.M_Product_ID > 0
                                      THEN p.ProductType
                                 WHEN (il.M_Product_ID IS NULL OR il.M_Product_ID = 0)
                                      AND il.C_Charge_ID > 0
                                      THEN 'E'
                                 ELSE NULL
                              END                                            AS TypeCode,
                              (SELECT rl.Name
                                 FROM AD_Ref_List rl
                                WHERE rl.AD_Reference_ID =
                                      (SELECT col.AD_Reference_Value_ID
                                         FROM AD_Column col
                                        WHERE col.AD_Table_ID =
                                              (SELECT AD_Table_ID FROM AD_Table
                                                WHERE TableName = 'M_Product')
                                          AND col.ColumnName = 'ProductType')
                                  AND rl.Value = CASE
                                                    WHEN il.M_Product_ID > 0
                                                         THEN p.ProductType
                                                    WHEN (il.M_Product_ID IS NULL OR il.M_Product_ID = 0)
                                                         AND il.C_Charge_ID > 0
                                                         THEN 'E'
                                                    ELSE NULL
                                                 END)                        AS TypeLabel
                           FROM C_InvoiceLine il
                           LEFT JOIN M_Product   p   ON (il.M_Product_ID  = p.M_Product_ID)
                           LEFT JOIN C_Charge    ch  ON (il.C_Charge_ID   = ch.C_Charge_ID)
                           LEFT JOIN C_UOM       uom ON (il.C_UOM_ID      = uom.C_UOM_ID)
                           LEFT JOIN C_Invoice   i   ON (il.C_Invoice_ID  = i.C_Invoice_ID)
                           LEFT JOIN M_PriceList pl  ON (i.M_PriceList_ID = pl.M_PriceList_ID)
                           WHERE il.C_Invoice_ID = @C_Invoice_ID
                           ORDER BY il.Line";

            SqlParameter[] param = new SqlParameter[]
            {
                new SqlParameter("@C_Invoice_ID", C_Invoice_ID)
            };

            DataSet ds = DB.ExecuteDataset(sql, param, null);
            if (ds == null || ds.Tables.Count == 0)
                return lines;

            foreach (DataRow r in ds.Tables[0].Rows)
            {
                InvoiceLineData ln = new InvoiceLineData();
                ln.C_InvoiceLine_ID = Util.GetValueOfInt(r["C_InvoiceLine_ID"]);
                ln.Line             = Util.GetValueOfInt(r["Line"]);
                ln.QtyInvoiced      = Util.GetValueOfDecimal(r["QtyInvoiced"]);
                ln.PriceActual      = Util.GetValueOfDecimal(r["PriceActual"]);
                ln.LineNetAmt       = Util.GetValueOfDecimal(r["LineNetAmt"]);
                ln.Description      = Util.GetValueOfString(r["LineDescription"]);
                ln.IsDescription    = Util.GetValueOfString(r["IsDescription"]) == "Y";
                ln.M_Product_ID     = Util.GetValueOfInt(r["M_Product_ID"]);
                ln.C_Charge_ID      = Util.GetValueOfInt(r["C_Charge_ID"]);
                ln.ProductName      = Util.GetValueOfString(r["ProductName"]);
                ln.ProductType      = Util.GetValueOfString(r["ProductType"]);
                ln.ChargeName       = Util.GetValueOfString(r["ChargeName"]);
                ln.UOMSymbol        = Util.GetValueOfString(r["UOMSymbol"]);
                ln.UOMPrecision     = Util.GetValueOfInt(r["UOMPrecision"]);
                ln.PricePrecision   = Util.GetValueOfInt(r["PricePrecision"]);
                ln.TypeCode         = Util.GetValueOfString(r["TypeCode"]);
                ln.TypeLabel        = Util.GetValueOfString(r["TypeLabel"]);

                if (string.IsNullOrEmpty(ln.ProductName) && !string.IsNullOrEmpty(ln.ChargeName))
                    ln.ProductName = ln.ChargeName;

                // Recurring eligibility:
                //  - Product set + ProductType = Service ('S') or Expense ('E') => eligible
                //  - No product but Charge set                                  => treated as Expense, eligible
                //  - Otherwise (Item / Resource / Online, or empty)             => not eligible
                if (ln.M_Product_ID > 0)
                {
                    ln.IsRecurringEligible = (ln.ProductType == "S" || ln.ProductType == "E");
                }
                else if (ln.C_Charge_ID > 0)
                {
                    ln.IsRecurringEligible = true;
                }

                lines.Add(ln);
            }
            return lines;
        }

        /// <summary>
        /// Duplicates an existing C_Invoice into a fresh draft using the
        /// platform's <see cref="MInvoice.CopyFrom"/> helper (which also
        /// copies lines internally). Refuses when the source invoice is
        /// voided or reversed.
        /// </summary>
        public DuplicateInvoiceResult DuplicateInvoice(Ctx ctx, int C_Invoice_ID)
        {
            DuplicateInvoiceResult result = new DuplicateInvoiceResult();

            if (C_Invoice_ID <= 0)
            {
                result.Message = "Invalid invoice id";
                return result;
            }

            Trx trx = Trx.GetTrx(Trx.CreateTrxName("DuplicateInvoice"));
            try
            {
                MInvoice from = new MInvoice(ctx, C_Invoice_ID, trx);
                if (from.GetC_Invoice_ID() <= 0)
                {
                    result.Message = "Invoice not found";
                    return result;
                }

                string docStatus = from.GetDocStatus();
                if (docStatus == "VO" || docStatus == "RE")
                {
                    result.Message = Msg.GetMsg(ctx, "VAS_DuplicateNotAllowedVoidedReversed");
                    return result;
                }

                MInvoice to = MInvoice.CopyFrom(
                    from,
                    DateTime.Today,
                    from.GetC_DocType_ID(),
                    /* counter */ false,
                    trx,
                    /* setOrder */ false);

                trx.Commit();

                result.Success         = true;
                result.NewC_Invoice_ID = to.GetC_Invoice_ID();
                result.NewDocumentNo   = to.GetDocumentNo();
            }
            catch (Exception ex)
            {
                try { trx.Rollback(); } catch { /* ignore */ }
                result.Message = ex.Message;
            }
            finally
            {
                try { trx.Close(); } catch { /* ignore */ }
            }

            return result;
        }

        public class DuplicateInvoiceResult
        {
            public bool   Success         { get; set; }
            public int    NewC_Invoice_ID { get; set; }
            public string NewDocumentNo   { get; set; }
            public string Message         { get; set; }
        }

        /// <summary>
        /// Returns the metadata required to populate the Record Payment dialog
        /// for the given invoice: bank accounts, active payment methods (with
        /// their VA009_PaymentBaseType), conversion types, and resolved
        /// defaults (bank account + conversion type + outstanding amount).
        /// </summary>
        public RecordPaymentMeta GetRecordPaymentMeta(Ctx ctx, int C_Invoice_ID)
        {
            RecordPaymentMeta meta = new RecordPaymentMeta();
            if (C_Invoice_ID <= 0) return meta;

            MRole role = MRole.GetDefault(ctx);

            // Pull the invoice context first.
            string invSql = @"SELECT i.C_BPartner_ID,
                                     i.C_Currency_ID,
                                     i.C_ConversionType_ID,
                                     i.AD_Org_ID,
                                     cur.CurSymbol,
                                     cur.ISO_Code,
                                     cur.StdPrecision,
                                     (SELECT NVL(SUM(ips.DueAmt), 0)
                                        FROM C_InvoicePaySchedule ips
                                       WHERE ips.C_Invoice_ID = i.C_Invoice_ID
                                         AND ips.VA009_IsPaid = 'N')           AS Outstanding
                                FROM C_Invoice  i
                                INNER JOIN C_Currency cur ON (i.C_Currency_ID = cur.C_Currency_ID)
                                WHERE i.C_Invoice_ID = @C_Invoice_ID";
            invSql = role.AddAccessSQL(invSql, "i", MRole.SQL_FULLYQUALIFIED, MRole.SQL_RO);

            SqlParameter[] invParam = new SqlParameter[]
            {
                new SqlParameter("@C_Invoice_ID", C_Invoice_ID)
            };
            DataSet invDs = DB.ExecuteDataset(invSql, invParam, null);
            if (invDs == null || invDs.Tables.Count == 0 || invDs.Tables[0].Rows.Count == 0)
                return meta;

            DataRow ir = invDs.Tables[0].Rows[0];
            meta.C_BPartner_ID         = Util.GetValueOfInt(ir["C_BPartner_ID"]);
            meta.C_Currency_ID         = Util.GetValueOfInt(ir["C_Currency_ID"]);
            meta.DefaultC_ConversionType_ID = Util.GetValueOfInt(ir["C_ConversionType_ID"]);
            meta.AD_Org_ID             = Util.GetValueOfInt(ir["AD_Org_ID"]);
            meta.CurSymbol             = Util.GetValueOfString(ir["CurSymbol"]);
            meta.ISO_Code              = Util.GetValueOfString(ir["ISO_Code"]);
            meta.StdPrecision          = Util.GetValueOfInt(ir["StdPrecision"]);
            meta.Outstanding           = Util.GetValueOfDecimal(ir["Outstanding"]);

            // Bank accounts (all active).
            string baSql = @"SELECT ba.C_BankAccount_ID,
                                    ba.AccountNo,
                                    b.Name        AS BankName,
                                    ba.IsDefault
                               FROM C_BankAccount ba
                               INNER JOIN C_Bank b ON (ba.C_Bank_ID = b.C_Bank_ID)
                               WHERE ba.IsActive = 'Y'
                               ORDER BY ba.IsDefault DESC, b.Name";
            baSql = role.AddAccessSQL(baSql, "ba", MRole.SQL_FULLYQUALIFIED, MRole.SQL_RO);

            DataSet baDs = DB.ExecuteDataset(baSql, null, null);
            meta.BankAccounts = new List<BankAccountOption>();
            if (baDs != null && baDs.Tables.Count > 0)
            {
                foreach (DataRow r in baDs.Tables[0].Rows)
                {
                    BankAccountOption opt = new BankAccountOption();
                    opt.C_BankAccount_ID = Util.GetValueOfInt(r["C_BankAccount_ID"]);
                    opt.BankName         = Util.GetValueOfString(r["BankName"]);
                    opt.AccountNo        = Util.GetValueOfString(r["AccountNo"]);
                    opt.IsDefault        = Util.GetValueOfString(r["IsDefault"]) == "Y";
                    meta.BankAccounts.Add(opt);
                    if (opt.IsDefault && meta.DefaultC_BankAccount_ID == 0)
                        meta.DefaultC_BankAccount_ID = opt.C_BankAccount_ID;
                }
                if (meta.DefaultC_BankAccount_ID == 0 && meta.BankAccounts.Count > 0)
                    meta.DefaultC_BankAccount_ID = meta.BankAccounts[0].C_BankAccount_ID;
            }

            // Active payment methods. Display name comes from VA009_Name.
            string pmSql = @"SELECT pm.VA009_PaymentMethod_ID,
                                    pm.VA009_Name,
                                    pm.VA009_PaymentBaseType
                               FROM VA009_PaymentMethod pm
                               WHERE pm.IsActive = 'Y'
                               ORDER BY pm.VA009_Name";
            pmSql = role.AddAccessSQL(pmSql, "pm", MRole.SQL_FULLYQUALIFIED, MRole.SQL_RO);

            DataSet pmDs = DB.ExecuteDataset(pmSql, null, null);
            meta.PaymentMethods = new List<PaymentMethodOption>();
            if (pmDs != null && pmDs.Tables.Count > 0)
            {
                foreach (DataRow r in pmDs.Tables[0].Rows)
                {
                    PaymentMethodOption opt = new PaymentMethodOption();
                    opt.VA009_PaymentMethod_ID = Util.GetValueOfInt(r["VA009_PaymentMethod_ID"]);
                    opt.Name                   = Util.GetValueOfString(r["VA009_Name"]);
                    opt.BaseType               = Util.GetValueOfString(r["VA009_PaymentBaseType"]);
                    meta.PaymentMethods.Add(opt);
                }
            }

            // Currencies marked as "my currency" (active).
            string curSql = @"SELECT c.C_Currency_ID,
                                     c.ISO_Code,
                                     c.CurSymbol,
                                     c.StdPrecision
                                FROM C_Currency c
                                WHERE c.IsMyCurrency = 'Y'
                                  AND c.IsActive     = 'Y'
                                ORDER BY c.ISO_Code";
            curSql = role.AddAccessSQL(curSql, "c", MRole.SQL_FULLYQUALIFIED, MRole.SQL_RO);

            DataSet curDs = DB.ExecuteDataset(curSql, null, null);
            meta.Currencies = new List<CurrencyOption>();
            if (curDs != null && curDs.Tables.Count > 0)
            {
                foreach (DataRow r in curDs.Tables[0].Rows)
                {
                    CurrencyOption opt = new CurrencyOption();
                    opt.C_Currency_ID = Util.GetValueOfInt(r["C_Currency_ID"]);
                    opt.ISO_Code      = Util.GetValueOfString(r["ISO_Code"]);
                    opt.CurSymbol     = Util.GetValueOfString(r["CurSymbol"]);
                    opt.StdPrecision  = Util.GetValueOfInt(r["StdPrecision"]);
                    meta.Currencies.Add(opt);
                }
            }

            // Conversion types (active for client).
            string ctSql = @"SELECT ct.C_ConversionType_ID,
                                    ct.Name,
                                    ct.IsDefault
                               FROM C_ConversionType ct
                               WHERE ct.IsActive     = 'Y'
                                 AND ct.AD_Client_ID IN (0, @AD_Client_ID)
                               ORDER BY ct.IsDefault DESC, ct.Name";
            ctSql = role.AddAccessSQL(ctSql, "ct", MRole.SQL_FULLYQUALIFIED, MRole.SQL_RO);

            SqlParameter[] ctParam = new SqlParameter[]
            {
                new SqlParameter("@AD_Client_ID", ctx.GetAD_Client_ID())
            };
            DataSet ctDs = DB.ExecuteDataset(ctSql, ctParam, null);
            meta.ConversionTypes = new List<ConversionTypeOption>();
            if (ctDs != null && ctDs.Tables.Count > 0)
            {
                foreach (DataRow r in ctDs.Tables[0].Rows)
                {
                    ConversionTypeOption opt = new ConversionTypeOption();
                    opt.C_ConversionType_ID = Util.GetValueOfInt(r["C_ConversionType_ID"]);
                    opt.Name                = Util.GetValueOfString(r["Name"]);
                    opt.IsDefault           = Util.GetValueOfString(r["IsDefault"]) == "Y";
                    meta.ConversionTypes.Add(opt);
                    if (meta.DefaultC_ConversionType_ID == 0 && opt.IsDefault)
                        meta.DefaultC_ConversionType_ID = opt.C_ConversionType_ID;
                }
                if (meta.DefaultC_ConversionType_ID == 0 && meta.ConversionTypes.Count > 0)
                    meta.DefaultC_ConversionType_ID = meta.ConversionTypes[0].C_ConversionType_ID;
            }

            return meta;
        }

        public class RecordPaymentMeta
        {
            public int    C_BPartner_ID                  { get; set; }
            public int    C_Currency_ID                  { get; set; }
            public int    DefaultC_ConversionType_ID     { get; set; }
            public int    AD_Org_ID                      { get; set; }
            public string CurSymbol                      { get; set; }
            public string ISO_Code                       { get; set; }
            public int    StdPrecision                   { get; set; }
            public decimal Outstanding                   { get; set; }
            public int    DefaultC_BankAccount_ID        { get; set; }

            public List<BankAccountOption>     BankAccounts     { get; set; }
            public List<PaymentMethodOption>   PaymentMethods   { get; set; }
            public List<ConversionTypeOption>  ConversionTypes  { get; set; }
            public List<CurrencyOption>        Currencies       { get; set; }
        }

        public class CurrencyOption
        {
            public int    C_Currency_ID { get; set; }
            public string ISO_Code      { get; set; }
            public string CurSymbol     { get; set; }
            public int    StdPrecision  { get; set; }
        }

        public class BankAccountOption
        {
            public int    C_BankAccount_ID { get; set; }
            public string BankName         { get; set; }
            public string AccountNo        { get; set; }
            public bool   IsDefault        { get; set; }
        }

        public class PaymentMethodOption
        {
            public int    VA009_PaymentMethod_ID { get; set; }
            public string Name                   { get; set; }
            public string BaseType               { get; set; } // 'K' = Check
        }

        public class ConversionTypeOption
        {
            public int    C_ConversionType_ID { get; set; }
            public string Name                { get; set; }
            public bool   IsDefault           { get; set; }
        }

        /// <summary>
        /// Creates an AR Receipt (C_Payment) for the given invoice and a
        /// C_PaymentAllocate row per unpaid invoice pay schedule. Splits the
        /// requested amount proportionally across the schedules. Sets the
        /// Check tender + CheckNo/CheckDate when the chosen payment method's
        /// VA009_PaymentBaseType = 'K'. Saved as Drafted so the user can
        /// review and complete via the standard AR Receipt flow.
        /// </summary>
        public RecordPaymentResult RecordPayment(Ctx ctx, RecordPaymentRequest req)
        {
            RecordPaymentResult result = new RecordPaymentResult();

            if (req == null || req.C_Invoice_ID <= 0)
            {
                result.Message = "Invalid request";
                return result;
            }

            Trx trx = Trx.GetTrx(Trx.CreateTrxName("RecordPayment"));
            try
            {
                MInvoice inv = new MInvoice(ctx, req.C_Invoice_ID, trx);
                if (inv.GetC_Invoice_ID() <= 0)
                {
                    result.Message = "Invoice not found";
                    return result;
                }

                string docStatus = inv.GetDocStatus();
                if (docStatus != "CO" && docStatus != "CL")
                {
                    result.Message = "Invoice must be Completed or Closed to record a payment";
                    return result;
                }

                // Resolve outstanding amount to validate the requested PayAmt.
                string outSql = @"SELECT NVL(SUM(ips.DueAmt), 0)
                                    FROM C_InvoicePaySchedule ips
                                   WHERE ips.C_Invoice_ID = @C_Invoice_ID
                                     AND ips.VA009_IsPaid = 'N'";
                decimal outstanding = Util.GetValueOfDecimal(DB.ExecuteScalar(
                    outSql,
                    new SqlParameter[] { new SqlParameter("@C_Invoice_ID", req.C_Invoice_ID) },
                    trx));
                if (outstanding <= 0)
                {
                    result.Message = "Invoice has no outstanding balance";
                    return result;
                }

                decimal payAmt = req.PayAmt;
                if (req.IsFull) payAmt = outstanding;
                if (payAmt <= 0)
                {
                    result.Message = "Payment amount must be greater than zero";
                    return result;
                }
                if (payAmt > outstanding)
                {
                    result.Message = "Payment amount cannot be greater than the outstanding amount.";
                    return result;
                }

                // Resolve payment method base type for tender/check fields.
                string baseType = "";
                if (req.VA009_PaymentMethod_ID > 0)
                {
                    baseType = Util.GetValueOfString(DB.ExecuteScalar(
                        "SELECT VA009_PaymentBaseType FROM VA009_PaymentMethod WHERE VA009_PaymentMethod_ID = @id",
                        new SqlParameter[] { new SqlParameter("@id", req.VA009_PaymentMethod_ID) },
                        trx));
                }

                // Pick AR Receipt doc type (DocBaseType = 'ARR').
                string dtSql = @"SELECT C_DocType_ID
                                   FROM C_DocType
                                  WHERE IsActive = 'Y'
                                    AND DocBaseType = 'ARR'
                                    AND AD_Client_ID = @AD_Client_ID
                                    AND AD_Org_ID IN (0, @AD_Org_ID)
                                  ORDER BY IsDefault DESC, AD_Org_ID DESC";
                int arrDocType = Util.GetValueOfInt(DB.ExecuteScalar(
                    dtSql,
                    new SqlParameter[]
                    {
                        new SqlParameter("@AD_Client_ID", ctx.GetAD_Client_ID()),
                        new SqlParameter("@AD_Org_ID",    inv.GetAD_Org_ID())
                    },
                    trx));

                MPayment payment = new MPayment(ctx, 0, trx);
                payment.SetAD_Client_ID(ctx.GetAD_Client_ID());
                payment.SetAD_Org_ID(inv.GetAD_Org_ID());
                payment.SetIsReceipt(true);
                if (arrDocType > 0)
                    payment.SetC_DocType_ID(arrDocType);

                payment.SetC_BPartner_ID(inv.GetC_BPartner_ID());
                payment.SetC_BPartner_Location_ID(inv.GetC_BPartner_Location_ID());

                int currencyID = req.C_Currency_ID > 0 ? req.C_Currency_ID : inv.GetC_Currency_ID();
                payment.SetC_Currency_ID(currencyID);
                if (req.C_ConversionType_ID > 0)
                    payment.SetC_ConversionType_ID(req.C_ConversionType_ID);
                else if (inv.GetC_ConversionType_ID() > 0)
                    payment.SetC_ConversionType_ID(inv.GetC_ConversionType_ID());

                if (req.C_BankAccount_ID > 0)
                    payment.SetC_BankAccount_ID(req.C_BankAccount_ID);

                DateTime txDate = req.DateTrx ?? DateTime.Today;
                payment.SetDateTrx(txDate);
                payment.SetDateAcct(txDate);

                payment.SetPayAmt(payAmt);

                // OverUnderAmt = Outstanding - PayAmt (positive = under-payment).
                decimal overUnder = outstanding - payAmt;
                payment.SetOverUnderAmt(overUnder);
                payment.SetIsOverUnderPayment(overUnder != 0);

                if (req.VA009_PaymentMethod_ID > 0)
                    payment.SetVA009_PaymentMethod_ID(req.VA009_PaymentMethod_ID);

                // VA009_PaymentBaseType code 'S' = Check (per the codebase
                // remap in MPaymentModel.GetBPartnerDetail). Set the standard
                // tender type accordingly.
                if (baseType == "S")
                {
                    payment.SetTenderType(MPayment.TENDERTYPE_Check);
                    if (!string.IsNullOrEmpty(req.CheckNo))
                        payment.SetCheckNo(req.CheckNo);
                    if (req.CheckDate.HasValue)
                        payment.SetCheckDate(req.CheckDate);
                }
                else
                {
                    payment.SetTenderType(MPayment.TENDERTYPE_DirectDeposit);
                }

                if (!string.IsNullOrEmpty(req.ReferenceNo))
                    payment.Set_Value("DocumentNo", req.ReferenceNo);

                payment.SetDocStatus(MPayment.DOCSTATUS_InProgress);
                payment.SetDocAction(MPayment.DOCACTION_Complete);

                // Determine whether we set the invoice on the header or use
                // C_PaymentAllocate rows (multi-schedule case).
                string schedSql = @"SELECT C_InvoicePaySchedule_ID, DueAmt
                                      FROM C_InvoicePaySchedule
                                     WHERE C_Invoice_ID = @C_Invoice_ID
                                       AND VA009_IsPaid = 'N'
                                     ORDER BY DueDate";
                DataSet schedDs = DB.ExecuteDataset(
                    schedSql,
                    new SqlParameter[] { new SqlParameter("@C_Invoice_ID", req.C_Invoice_ID) },
                    trx);

                int schedCount = (schedDs != null && schedDs.Tables.Count > 0)
                                 ? schedDs.Tables[0].Rows.Count : 0;

                if (schedCount == 1)
                {
                    payment.SetC_Invoice_ID(req.C_Invoice_ID);
                    payment.SetC_InvoicePaySchedule_ID(
                        Util.GetValueOfInt(schedDs.Tables[0].Rows[0]["C_InvoicePaySchedule_ID"]));
                }

                if (!payment.Save(trx))
                {
                    ValueNamePair pp = VLogger.RetrieveError();
                    result.Message = "Could not save payment" +
                                     (pp != null ? ": " + pp.GetName() : "");
                    trx.Rollback();
                    return result;
                }

                // Multi-schedule: create C_PaymentAllocate rows split proportionally.
                if (schedCount > 1)
                {
                    decimal remaining = payAmt;
                    int rowsCount = schedDs.Tables[0].Rows.Count;
                    for (int i = 0; i < rowsCount; i++)
                    {
                        DataRow sr = schedDs.Tables[0].Rows[i];
                        decimal due = Util.GetValueOfDecimal(sr["DueAmt"]);
                        decimal alloc;
                        if (i == rowsCount - 1)
                            alloc = remaining;
                        else
                            alloc = (outstanding > 0)
                                    ? Math.Round(payAmt * due / outstanding, 2)
                                    : 0m;
                        if (alloc <= 0) continue;
                        if (alloc > due) alloc = due;
                        remaining -= alloc;

                        MPaymentAllocate pa = new MPaymentAllocate(ctx, 0, trx);
                        pa.SetAD_Client_ID(payment.GetAD_Client_ID());
                        pa.SetAD_Org_ID(payment.GetAD_Org_ID());
                        pa.SetC_Payment_ID(payment.GetC_Payment_ID());
                        pa.SetC_Invoice_ID(req.C_Invoice_ID);
                        pa.SetC_InvoicePaySchedule_ID(
                            Util.GetValueOfInt(sr["C_InvoicePaySchedule_ID"]));
                        pa.SetAmount(alloc);
                        pa.SetInvoiceAmt(due);
                        if (!pa.Save(trx))
                        {
                            ValueNamePair pp = VLogger.RetrieveError();
                            result.Message = "Could not save payment allocation" +
                                             (pp != null ? ": " + pp.GetName() : "");
                            trx.Rollback();
                            return result;
                        }
                    }
                }

                // Auto-complete the payment so DocStatus = 'CO' on creation.
                bool processed;
                try
                {
                    processed = payment.ProcessIt(MPayment.DOCACTION_Complete);
                }
                catch (Exception pex)
                {
                    processed = false;
                    result.Message = "Payment created but completion failed: " + pex.Message;
                }
                if (!processed)
                {
                    if (string.IsNullOrEmpty(result.Message))
                    {
                        ValueNamePair pp = VLogger.RetrieveError();
                        result.Message = "Payment created but could not be completed" +
                                         (pp != null ? ": " + pp.GetName() : "");
                    }
                    trx.Rollback();
                    return result;
                }
                if (!payment.Save(trx))
                {
                    ValueNamePair pp = VLogger.RetrieveError();
                    result.Message = "Could not save completed payment" +
                                     (pp != null ? ": " + pp.GetName() : "");
                    trx.Rollback();
                    return result;
                }

                trx.Commit();
                result.Success      = true;
                result.C_Payment_ID = payment.GetC_Payment_ID();
                result.DocumentNo   = payment.GetDocumentNo();
                result.PayAmt       = payAmt;
            }
            catch (Exception ex)
            {
                try { trx.Rollback(); } catch { /* ignore */ }
                result.Message = ex.Message;
            }
            finally
            {
                try { trx.Close(); } catch { /* ignore */ }
            }

            return result;
        }

        public class RecordPaymentRequest
        {
            public int      C_Invoice_ID            { get; set; }
            public bool     IsFull                  { get; set; }
            public decimal  PayAmt                  { get; set; }
            public int      C_Currency_ID           { get; set; }
            public int      C_ConversionType_ID     { get; set; }
            public int      C_BankAccount_ID        { get; set; }
            public int      VA009_PaymentMethod_ID  { get; set; }
            public DateTime? DateTrx                { get; set; }
            public string   ReferenceNo             { get; set; }
            public string   CheckNo                 { get; set; }
            public DateTime? CheckDate              { get; set; }
        }

        public class RecordPaymentResult
        {
            public bool    Success     { get; set; }
            public int     C_Payment_ID { get; set; }
            public string  DocumentNo  { get; set; }
            public decimal PayAmt      { get; set; }
            public string  Message     { get; set; }
        }

        /// <summary>
        /// Calculates the customer's payment risk based on their historical paid
        /// AR invoices. Sets <c>PaymentRiskLevel</c>, <c>AvgDaysToPay</c>,
        /// <c>AvgDaysAfterDue</c>, and <c>PaidInvoiceCount</c> on <paramref name="result"/>.
        /// Risk thresholds use the average days a payment lands after the invoice
        /// due date.
        /// </summary>
        private void LoadPaymentRisk(InvoiceOverviewData result, int C_Invoice_ID)
        {
            if (result.C_BPartner_ID <= 0)
            {
                result.PaymentRiskLevel = "none";
                return;
            }

            string sql = @"WITH PaidInvoices AS (
                              SELECT
                                  i.C_Invoice_ID,
                                  i.DateInvoiced,
                                  (SELECT MIN(ips.DueDate)
                                     FROM C_InvoicePaySchedule ips
                                    WHERE ips.C_Invoice_ID = i.C_Invoice_ID)         AS DueDate,
                                  (SELECT MAX(ah.DateAcct)
                                     FROM C_AllocationHdr ah
                                     INNER JOIN C_AllocationLine al
                                        ON (ah.C_AllocationHdr_ID = al.C_AllocationHdr_ID)
                                     INNER JOIN C_InvoicePaySchedule ips
                                        ON (al.C_InvoicePaySchedule_ID = ips.C_InvoicePaySchedule_ID)
                                    WHERE ips.C_Invoice_ID = i.C_Invoice_ID
                                      AND ah.DocStatus IN ('CO','CL'))               AS LastPaymentDate
                                FROM C_Invoice i
                                WHERE i.C_BPartner_ID = @C_BPartner_ID
                                  AND i.IsSoTrx       = 'Y'
                                  AND i.IsReturnTrx   = 'N'
                                  AND i.IsPaid        = 'Y'
                                  AND i.DocStatus IN ('CO','CL')
                                  AND i.C_Invoice_ID <> @C_Invoice_ID
                           )
                           SELECT
                              COUNT(*)                                       AS PaidInvoiceCount,
                              AVG(LastPaymentDate - DateInvoiced)            AS AvgDaysToPay,
                              AVG(LastPaymentDate - DueDate)                 AS AvgDaysAfterDue
                           FROM PaidInvoices
                           WHERE LastPaymentDate IS NOT NULL
                             AND DateInvoiced  IS NOT NULL
                             AND DueDate       IS NOT NULL";

            SqlParameter[] param = new SqlParameter[]
            {
                new SqlParameter("@C_BPartner_ID", result.C_BPartner_ID),
                new SqlParameter("@C_Invoice_ID",  C_Invoice_ID)
            };

            DataSet ds = DB.ExecuteDataset(sql, param, null);
            if (ds == null || ds.Tables.Count == 0 || ds.Tables[0].Rows.Count == 0)
            {
                result.PaymentRiskLevel = "none";
                return;
            }

            DataRow r = ds.Tables[0].Rows[0];
            result.PaidInvoiceCount = Util.GetValueOfInt(r["PaidInvoiceCount"]);
            if (result.PaidInvoiceCount <= 0)
            {
                result.PaymentRiskLevel = "none";
                return;
            }

            decimal avgDaysToPay   = Util.GetValueOfDecimal(r["AvgDaysToPay"]);
            decimal avgDaysAfter   = Util.GetValueOfDecimal(r["AvgDaysAfterDue"]);
            result.AvgDaysToPay    = (int)Math.Round(avgDaysToPay);
            result.AvgDaysAfterDue = (int)Math.Round(avgDaysAfter);

            if      (avgDaysAfter <= 0) result.PaymentRiskLevel = "low";
            else if (avgDaysAfter <= 7) result.PaymentRiskLevel = "medium";
            else                        result.PaymentRiskLevel = "high";
        }

        /// <summary>
        /// Loads CM_ChatEntry rows for the given invoice via CM_Chat
        /// (AD_Table_ID = C_Invoice's table id, Record_ID = invoice id).
        /// </summary>
        private List<NoteData> LoadNotes(int C_Invoice_ID)
        {
            List<NoteData> notes = new List<NoteData>();

            string sql = @"SELECT
                              ce.CM_ChatEntry_ID,
                              ce.CM_Chat_ID,
                              ce.AD_User_ID,
                              ce.Subject,
                              ce.CharacterData,
                              ce.ConfidentialType,
                              ce.Created,
                              ce.Updated,
                              u.Name              AS UserName
                           FROM CM_ChatEntry ce
                           INNER JOIN CM_Chat ch ON (ce.CM_Chat_ID = ch.CM_Chat_ID)
                           LEFT  JOIN AD_User u  ON (ce.AD_User_ID  = u.AD_User_ID)
                           WHERE ch.AD_Table_ID =
                                 (SELECT AD_Table_ID FROM AD_Table WHERE TableName = 'C_Invoice')
                             AND ch.Record_ID = @C_Invoice_ID
                             AND ce.IsActive = 'Y'
                           ORDER BY ce.Created";

            SqlParameter[] param = new SqlParameter[]
            {
                new SqlParameter("@C_Invoice_ID", C_Invoice_ID)
            };

            DataSet ds = DB.ExecuteDataset(sql, param, null);
            if (ds == null || ds.Tables.Count == 0)
                return notes;

            foreach (DataRow r in ds.Tables[0].Rows)
            {
                NoteData n = new NoteData();
                n.CM_ChatEntry_ID    = Util.GetValueOfInt(r["CM_ChatEntry_ID"]);
                n.AD_User_ID         = Util.GetValueOfInt(r["AD_User_ID"]);
                n.UserName           = Util.GetValueOfString(r["UserName"]);
                n.Subject            = Util.GetValueOfString(r["Subject"]);
                n.CharacterData      = Util.GetValueOfString(r["CharacterData"]);
                n.ConfidentialType   = Util.GetValueOfString(r["ConfidentialType"]);
                n.Created            = Util.GetValueOfDateTime(r["Created"]);
                n.Updated            = Util.GetValueOfDateTime(r["Updated"]);

                n.IsVisibleToCustomer = n.ConfidentialType == "A";
                if (n.Created.HasValue && n.Updated.HasValue)
                    n.IsEdited = n.Updated.Value.Subtract(n.Created.Value).TotalSeconds > 1;

                notes.Add(n);
            }
            return notes;
        }

        public class NoteData
        {
            public int       CM_ChatEntry_ID    { get; set; }
            public int       AD_User_ID         { get; set; }
            public string    UserName           { get; set; }
            public string    Subject            { get; set; }
            public string    CharacterData      { get; set; }
            public string    ConfidentialType   { get; set; }
            public DateTime? Created            { get; set; }
            public DateTime? Updated            { get; set; }
            public bool      IsVisibleToCustomer { get; set; }
            public bool      IsEdited           { get; set; }
        }

        public class InvoiceLineData
        {
            public int     C_InvoiceLine_ID { get; set; }
            public int     Line             { get; set; }
            public decimal QtyInvoiced      { get; set; }
            public decimal PriceActual      { get; set; }
            public decimal LineNetAmt       { get; set; }
            public string  Description      { get; set; }
            public bool    IsDescription    { get; set; }
            public int     M_Product_ID     { get; set; }
            public int     C_Charge_ID      { get; set; }
            public string  ProductName      { get; set; }
            public string  ProductType      { get; set; }
            public string  ChargeName       { get; set; }
            public string  UOMSymbol        { get; set; }
            public int     UOMPrecision     { get; set; }
            public int     PricePrecision   { get; set; }
            public string  TypeCode         { get; set; }
            public string  TypeLabel        { get; set; }
            public bool    IsRecurringEligible { get; set; }
        }

        public class InvoiceOverviewData
        {
            public int       C_Invoice_ID     { get; set; }
            public string    DocumentNo       { get; set; }
            public DateTime? DateInvoiced     { get; set; }
            public int       C_BPartner_ID    { get; set; }
            public string    DocStatus        { get; set; }
            public string    BPName           { get; set; }
            public string    ContactName      { get; set; }
            public string    ContactTitle     { get; set; }
            public string    CurSymbol        { get; set; }
            public string    ISO_Code         { get; set; }
            public int       StdPrecision     { get; set; }
            public int       OpenInvoiceCount { get; set; }
            public DateTime? DueDate          { get; set; }
            public int       DaysDifference   { get; set; }
            public bool      IsOverdue        { get; set; }
            public decimal   Outstanding      { get; set; }

            // Status timeline
            public DateTime? Created          { get; set; }
            public string    CreatedByName    { get; set; }
            public bool      IsApproved       { get; set; }
            public DateTime? ApprovedDate     { get; set; }
            public string    ApprovedByName   { get; set; }
            public bool      IsSent           { get; set; }
            public bool      IsFullyPaid      { get; set; }
            public DateTime? PaidDate         { get; set; }
            public bool      IsClosed         { get; set; }

            // Line items + totals
            public decimal              TotalLines { get; set; }
            public decimal              TaxAmt     { get; set; }
            public decimal              GrandTotal { get; set; }
            public List<InvoiceLineData> Lines     { get; set; }

            // Notes
            public List<NoteData> Notes              { get; set; }
            public int            VisibleOnPDFCount  { get; set; }

            // Recurring eligibility roll-up
            public int EligibleRecurringCount { get; set; }
            public int PhysicalItemsCount    { get; set; }
            public int RecurringLineCount    { get; set; }

            // Payment risk
            public string PaymentRiskLevel { get; set; } // low | medium | high | none
            public int    AvgDaysToPay     { get; set; }
            public int    AvgDaysAfterDue  { get; set; }
            public int    PaidInvoiceCount { get; set; }
        }
    }
}
