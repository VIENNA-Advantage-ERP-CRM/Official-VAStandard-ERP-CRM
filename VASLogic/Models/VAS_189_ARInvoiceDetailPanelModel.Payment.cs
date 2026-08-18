/******************************************************
 * Module Name    : VASLogic
 * Purpose        : AR Invoice detail tab panel - Record Payment /
 *                  Allocate Credit Note modal meta and write actions
 *                  (apply on-account receipts & credits, record a
 *                  customer receipt, allocate an AR credit note).
 * chronological  : Development
 *   VAI_145        Created  04 August 2026
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
    /// Receipt / allocation half of the AR invoice detail panel model. Mirrors
    /// the AP panel behaviour with the sales-side polarity: payments are
    /// receipts (IsReceipt='Y') and the payment document type is ARR.
    /// </summary>
    public partial class VAS_189_ARInvoiceDetailPanelModel
    {
        private static VLogger _log = VLogger.GetVLogger(typeof(VAS_189_ARInvoiceDetailPanelModel).FullName);

        #region Modal meta

        /// <summary>
        /// Returns the data needed by the Record Payment / Allocate Credit Note
        /// modal: the customer position, available on-account receipts (AR
        /// invoice mode) or open AR invoices (credit-note mode), plus payment
        /// methods, bank accounts, currencies and conversion types.
        /// </summary>
        /// <param name="ctx">session context</param>
        /// <param name="C_Invoice_ID">target invoice</param>
        /// <param name="IsSOTrx">sales transaction flag supplied by the caller</param>
        /// <returns>modal meta</returns>
        public PaymentModalMeta GetPaymentModalMeta(Ctx ctx, int C_Invoice_ID, bool IsSOTrx)
        {
            PaymentModalMeta meta = new PaymentModalMeta();
            ARInvoicePanelData head = new ARInvoicePanelData();
            LoadHeader(ctx, C_Invoice_ID, IsSOTrx, head);
            if (head.C_Invoice_ID <= 0)
            {
                return meta;
            }

            MRole role = MRole.GetDefault(ctx);

            meta.C_Invoice_ID = head.C_Invoice_ID;
            meta.C_BPartner_ID = head.C_BPartner_ID;
            meta.C_Currency_ID = head.C_Currency_ID;
            meta.CurSymbol = head.CurSymbol;
            meta.ISO_Code = head.CurISO;
            meta.StdPrecision = head.StdPrecision;
            meta.GrossInvoice = head.GrandTotal;
            meta.Withholding = head.WithholdingAmount;
            meta.NetReceivable = head.NetReceivable;
            meta.IsARCreditNote = head.IsARCreditNote;
            meta.IsSOTrx = head.IsSOTrx;
            meta.RecordMode = head.IsARCreditNote ? "AR_CREDIT_NOTE_ALLOCATION" : "AR_INVOICE_RECEIPT";
            meta.CustomerOutstanding = GetCustomerOutstanding(ctx, head.AD_Client_ID, head.C_BPartner_ID, IsSOTrx);

            // AR invoices and AR credit notes use the SAME modal (on-account credits +
            // record receipt); only the amount sign differs, and that is applied when
            // the allocation / payment is created. The UI works with a positive open
            // amount, so a credit note's open is taken as an absolute value here.
            if (head.IsARCreditNote)
            {
                meta.CreditNoteAmount = Math.Max(0m, Math.Abs(head.NetReceivable) - GetAllocatedAmount(C_Invoice_ID));
                meta.NetOpenAmount = meta.CreditNoteAmount;
            }
            else
            {
                meta.NetOpenAmount = head.OpenAmount;
            }

            // Nothing left to settle: the invoice is flagged paid or every pay schedule
            // is marked paid. The modal renders the New Receipt section read-only.
            meta.IsFullySettled = meta.NetOpenAmount <= 0m || (!head.IsARCreditNote && head.IsPaid);

            // First page of on-account receipts (server-side paged). The page rows are
            // converted for display; the total count and "available to apply" come from
            // a full aggregate pass so they are not limited to the page.
            meta.OnAccountPayments = LoadOnAccountPayments(ctx, head.AD_Client_ID, head.C_BPartner_ID,
                head.IsARCreditNote, 0, ONACCOUNT_PAGE_SIZE);
            foreach (AvailableCreditRow c in meta.OnAccountPayments)
            {
                ConvertRowToInvoiceCurrency(ctx, c, head);
            }
            int oaTotal;
            decimal oaAvail;
            LoadOnAccountAggregate(ctx, head.AD_Client_ID, head.C_BPartner_ID, head.IsARCreditNote,
                head, out oaTotal, out oaAvail);
            meta.OnAccountPaymentsTotal = oaTotal;
            meta.AvailableToApply = oaAvail;

            meta.BankAccounts = LoadBankAccounts(role, head.AD_Org_ID);
            meta.PaymentMethods = LoadPaymentMethods(role, head.AD_Org_ID);
            meta.Currencies = LoadCurrencies(head.AD_Client_ID);
            meta.ConversionTypes = LoadConversionTypes(role, head.AD_Client_ID, head.AD_Org_ID);
            meta.C_ConversionType_ID = head.C_ConversionType_ID;
            return meta;
        }

        /// <summary>Active "my currency" list of the client for the modal selector.</summary>
        /// <param name="AD_Client_ID">tenant</param>
        /// <returns>currency options</returns>
        private List<CurrencyOption> LoadCurrencies(int AD_Client_ID)
        {
            List<CurrencyOption> list = new List<CurrencyOption>();
            string sql = @"SELECT cur.C_Currency_ID, cur.ISO_Code, cur.CurSymbol, cur.StdPrecision
                             FROM C_Currency cur
                            WHERE cur.IsActive='Y'
                              AND cur.IsMyCurrency='Y'
                              AND cur.AD_Client_ID IN (0, @AD_Client_ID)
                            ORDER BY cur.ISO_Code";
            DataSet ds = DB.ExecuteDataset(sql,
                new SqlParameter[] { new SqlParameter("@AD_Client_ID", AD_Client_ID) }, null);
            if (ds == null || ds.Tables.Count == 0)
            {
                return list;
            }
            foreach (DataRow r in ds.Tables[0].Rows)
            {
                list.Add(new CurrencyOption
                {
                    C_Currency_ID = Util.GetValueOfInt(r["C_Currency_ID"]),
                    ISO_Code = Util.GetValueOfString(r["ISO_Code"]),
                    CurSymbol = Util.GetValueOfString(r["CurSymbol"]),
                    StdPrecision = Util.GetValueOfInt(r["StdPrecision"])
                });
            }
            return list;
        }

        /// <summary>Active conversion (rate) types of the invoice org, role-restricted.</summary>
        /// <param name="role">session role</param>
        /// <param name="AD_Client_ID">tenant</param>
        /// <param name="AD_Org_ID">invoice organization</param>
        /// <returns>conversion type options</returns>
        private List<ConversionTypeOption> LoadConversionTypes(MRole role, int AD_Client_ID, int AD_Org_ID)
        {
            List<ConversionTypeOption> list = new List<ConversionTypeOption>();
            string sql = @"SELECT ct.C_ConversionType_ID, ct.Name, COALESCE(ct.IsDefault, 'N') AS IsDefault
                             FROM C_ConversionType ct
                            WHERE ct.IsActive='Y'
                              AND ct.AD_Client_ID IN (0, @AD_Client_ID)
                              AND ct.AD_Org_ID IN (0, @AD_Org_ID)
                            ORDER BY IsDefault DESC, Name";
            sql = role.AddAccessSQL(sql, "ct", MRole.SQL_FULLYQUALIFIED, MRole.SQL_RO);
            DataSet ds = DB.ExecuteDataset(sql, new SqlParameter[]
            {
                new SqlParameter("@AD_Client_ID", AD_Client_ID),
                new SqlParameter("@AD_Org_ID", AD_Org_ID)
            }, null);
            if (ds == null || ds.Tables.Count == 0)
            {
                return list;
            }
            foreach (DataRow r in ds.Tables[0].Rows)
            {
                list.Add(new ConversionTypeOption
                {
                    C_ConversionType_ID = Util.GetValueOfInt(r["C_ConversionType_ID"]),
                    Name = Util.GetValueOfString(r["Name"]),
                    IsDefault = Util.GetValueOfString(r["IsDefault"]) == "Y"
                });
            }
            return list;
        }

        /// <summary>
        /// Sets <see cref="AvailableCreditRow.AvailableAmountInv"/> = the row's
        /// available amount converted into the invoice currency on the row's
        /// accounting date. When no rate exists the converted amount is 0.
        /// </summary>
        /// <param name="ctx">session context</param>
        /// <param name="row">source row</param>
        /// <param name="head">invoice header context</param>
        private void ConvertRowToInvoiceCurrency(Ctx ctx, AvailableCreditRow row, ARInvoicePanelData head)
        {
            if (row.C_Currency_ID <= 0 || row.C_Currency_ID == head.C_Currency_ID)
            {
                row.AvailableAmountInv = row.AvailableAmount;
                return;
            }
            decimal rate = MConversionRate.GetRate(row.C_Currency_ID, head.C_Currency_ID, row.DateAcct,
                row.C_ConversionType_ID, head.AD_Client_ID, row.AD_Org_ID);
            row.AvailableAmountInv = (rate == 0)
                ? 0m
                : MConversionRate.Convert(ctx, row.AvailableAmount, row.C_Currency_ID, head.C_Currency_ID,
                    row.DateAcct, row.C_ConversionType_ID, head.AD_Client_ID, row.AD_Org_ID);
        }

        /// <summary>Sum of open AR schedules for the customer (outstanding position).</summary>
        /// <param name="ctx">session context</param>
        /// <param name="AD_Client_ID">tenant</param>
        /// <param name="C_BPartner_ID">customer</param>
        /// <param name="IsSOTrx">sales transaction flag supplied by the caller</param>
        /// <returns>outstanding amount</returns>
        private decimal GetCustomerOutstanding(Ctx ctx, int AD_Client_ID, int C_BPartner_ID, bool IsSOTrx)
        {
            string sql = @"SELECT COALESCE(SUM(COALESCE(ips.DueAmt,
                                  COALESCE(i.GrandTotalAfterWithholding, i.GrandTotal))), 0)
                             FROM C_Invoice i
                             INNER JOIN C_InvoicePaySchedule ips ON (i.C_Invoice_ID=ips.C_Invoice_ID)
                            WHERE i.AD_Client_ID=@AD_Client_ID
                              AND i.C_BPartner_ID=@C_BPartner_ID
                              AND i.IsSOTrx=@IsSOTrx
                              AND i.IsActive='Y'
                              AND i.DocStatus IN ('CO','CL')
                              AND COALESCE(i.IsPaid, 'N')='N'
                              AND COALESCE(ips.VA009_IsPaid, 'N')='N'";
            sql = MRole.GetDefault(ctx).AddAccessSQL(sql, "i", MRole.SQL_FULLYQUALIFIED, MRole.SQL_RO);
            return Util.GetValueOfDecimal(DB.ExecuteScalar(sql, new SqlParameter[]
            {
                new SqlParameter("@AD_Client_ID", AD_Client_ID),
                new SqlParameter("@C_BPartner_ID", C_BPartner_ID),
                new SqlParameter("@IsSOTrx", IsSOTrx ? "Y" : "N")
            }, null));
        }

        /// <summary>
        /// SQL filter that keeps the displayable on-account receipts. The sign of
        /// the available amount selects the set: invoice mode keeps avail &gt; 0
        /// (money the customer paid in advance); credit-note mode keeps
        /// avail &lt;= 0. Pushed into SQL so paging and the row count are correct.
        /// </summary>
        /// <param name="IsCreditNote">credit-note mode</param>
        /// <returns>SQL predicate</returns>
        private string OnAccountSignFilter(bool IsCreditNote)
        {
            return IsCreditNote
                ? " AND ALLOCPAYMENTAVAILABLE(p.C_Payment_ID) <= 0"
                : " AND ALLOCPAYMENTAVAILABLE(p.C_Payment_ID) > 0";
        }

        /// <summary>Available (unallocated) on-account customer receipts (one page).</summary>
        /// <param name="ctx">session context</param>
        /// <param name="AD_Client_ID">tenant</param>
        /// <param name="C_BPartner_ID">customer</param>
        /// <param name="IsCreditNote">credit-note mode</param>
        /// <param name="page">zero-based page index</param>
        /// <param name="pageSize">rows per page</param>
        /// <returns>available credit rows</returns>
        private List<AvailableCreditRow> LoadOnAccountPayments(Ctx ctx, int AD_Client_ID, int C_BPartner_ID,
            bool IsCreditNote, int page, int pageSize)
        {
            List<AvailableCreditRow> list = new List<AvailableCreditRow>();
            string sql = @"SELECT
                              p.AD_Org_ID,
                              p.C_Payment_ID,
                              p.DocumentNo,
                              pdt.Name AS DocTypeName,
                              p.DateTrx,
                              p.DateAcct,
                              p.C_Currency_ID,
                              p.C_ConversionType_ID,
                              pcur.CurSymbol AS CurSymbol,
                              pcur.ISO_Code AS ISO_Code,
                              pcur.StdPrecision AS StdPrecision,
                              (ABS(COALESCE(p.VAS_UnAllocatedAmount, 0))) AS VAS_UnAllocatedAmount,
                              ALLOCPAYMENTAVAILABLE(p.C_Payment_ID) AS AvailableAmount
                           FROM C_Payment p
                           INNER JOIN C_Currency pcur ON (p.C_Currency_ID=pcur.C_Currency_ID)
                           LEFT OUTER JOIN C_DocType pdt ON (p.C_DocType_ID=pdt.C_DocType_ID)
                           WHERE p.AD_Client_ID=@AD_Client_ID
                             AND p.C_BPartner_ID=@C_BPartner_ID
                             AND p.IsReceipt='Y'
                             AND p.IsActive='Y'
                             AND p.DocStatus IN ('CO','CL')
                             AND COALESCE(p.IsAllocated, 'N')='N'
                             AND p.C_Invoice_ID IS NULL
                             AND p.C_Charge_ID IS NULL
                             AND p.C_Order_ID IS NULL";
            sql += OnAccountSignFilter(IsCreditNote);
            sql += " ORDER BY p.DateAcct, p.DocumentNo, p.C_Payment_ID";
            sql = MRole.GetDefault(ctx).AddAccessSQL(sql, "p", MRole.SQL_FULLYQUALIFIED, MRole.SQL_RO);
            sql += PagingSuffix(page, pageSize);

            DataSet ds = DB.ExecuteDataset(sql, new SqlParameter[]
            {
                new SqlParameter("@AD_Client_ID", AD_Client_ID),
                new SqlParameter("@C_BPartner_ID", C_BPartner_ID)
            }, null);
            if (ds == null || ds.Tables.Count == 0)
            {
                return list;
            }

            foreach (DataRow r in ds.Tables[0].Rows)
            {
                decimal avail = Util.GetValueOfDecimal(r["AvailableAmount"]);
                list.Add(new AvailableCreditRow
                {
                    SourceType = "PAYMENT",
                    Id = Util.GetValueOfInt(r["C_Payment_ID"]),
                    DocTypeName = Util.GetValueOfString(r["DocTypeName"]),
                    DocumentNo = Util.GetValueOfString(r["DocumentNo"]),
                    Date = Util.GetValueOfDateTime(r["DateTrx"]),
                    DateAcct = Util.GetValueOfDateTime(r["DateAcct"]),
                    AvailableAmount = Math.Abs(avail),
                    AD_Org_ID = Util.GetValueOfInt(r["AD_Org_ID"]),
                    C_Currency_ID = Util.GetValueOfInt(r["C_Currency_ID"]),
                    C_ConversionType_ID = Util.GetValueOfInt(r["C_ConversionType_ID"]),
                    CurSymbol = Util.GetValueOfString(r["CurSymbol"]),
                    ISO_Code = Util.GetValueOfString(r["ISO_Code"]),
                    StdPrecision = Util.GetValueOfInt(r["StdPrecision"])
                });
            }
            return list;
        }

        /// <summary>
        /// Aggregate over ALL displayable on-account receipts (independent of the
        /// page): total row count and the "available to apply" sum converted into
        /// the invoice currency.
        /// </summary>
        /// <param name="ctx">session context</param>
        /// <param name="AD_Client_ID">tenant</param>
        /// <param name="C_BPartner_ID">customer</param>
        /// <param name="IsCreditNote">credit-note mode</param>
        /// <param name="head">invoice header context</param>
        /// <param name="total">out: row count</param>
        /// <param name="availToApply">out: available sum in invoice currency</param>
        private void LoadOnAccountAggregate(Ctx ctx, int AD_Client_ID, int C_BPartner_ID, bool IsCreditNote,
            ARInvoicePanelData head, out int total, out decimal availToApply)
        {
            total = 0;
            availToApply = 0m;
            string sql = @"SELECT p.AD_Org_ID, p.C_Currency_ID, p.C_ConversionType_ID, p.DateAcct,
                                  ALLOCPAYMENTAVAILABLE(p.C_Payment_ID) AS AvailableAmount
                             FROM C_Payment p
                            WHERE p.AD_Client_ID=@AD_Client_ID
                              AND p.C_BPartner_ID=@C_BPartner_ID
                              AND p.IsReceipt='Y'
                              AND p.IsActive='Y'
                              AND p.DocStatus IN ('CO','CL')
                              AND COALESCE(p.IsAllocated, 'N')='N'
                              AND p.C_Invoice_ID IS NULL
                              AND p.C_Charge_ID IS NULL
                              AND p.C_Order_ID IS NULL";
            sql += OnAccountSignFilter(IsCreditNote);
            sql = MRole.GetDefault(ctx).AddAccessSQL(sql, "p", MRole.SQL_FULLYQUALIFIED, MRole.SQL_RO);
            DataSet ds = DB.ExecuteDataset(sql, new SqlParameter[]
            {
                new SqlParameter("@AD_Client_ID", AD_Client_ID),
                new SqlParameter("@C_BPartner_ID", C_BPartner_ID)
            }, null);
            if (ds == null || ds.Tables.Count == 0)
            {
                return;
            }

            foreach (DataRow r in ds.Tables[0].Rows)
            {
                total++;
                AvailableCreditRow tmp = new AvailableCreditRow
                {
                    AvailableAmount = Math.Abs(Util.GetValueOfDecimal(r["AvailableAmount"])),
                    AD_Org_ID = Util.GetValueOfInt(r["AD_Org_ID"]),
                    C_Currency_ID = Util.GetValueOfInt(r["C_Currency_ID"]),
                    C_ConversionType_ID = Util.GetValueOfInt(r["C_ConversionType_ID"]),
                    DateAcct = Util.GetValueOfDateTime(r["DateAcct"])
                };
                ConvertRowToInvoiceCurrency(ctx, tmp, head);
                availToApply += tmp.AvailableAmountInv;
            }
        }

        /// <summary>
        /// Page of on-account receipt rows (converted for display) for the pager.
        /// The customer, invoice currency and credit-note flag are passed from the
        /// modal meta so the invoice header is NOT re-queried per page;
        /// AD_Client_ID comes from the trusted session ctx, never the request.
        /// </summary>
        /// <param name="ctx">session context</param>
        /// <param name="C_BPartner_ID">customer</param>
        /// <param name="IsCreditNote">credit-note mode</param>
        /// <param name="C_Currency_ID">invoice currency</param>
        /// <param name="page">zero-based page index</param>
        /// <param name="pageSize">rows per page</param>
        /// <returns>available credit rows</returns>
        public List<AvailableCreditRow> GetOnAccountPaymentsPage(Ctx ctx, int C_BPartner_ID, bool IsCreditNote,
            int C_Currency_ID, int page, int pageSize)
        {
            if (C_BPartner_ID <= 0)
            {
                return new List<AvailableCreditRow>();
            }
            ARInvoicePanelData head = new ARInvoicePanelData
            {
                AD_Client_ID = ctx.GetAD_Client_ID(),
                C_Currency_ID = C_Currency_ID
            };
            List<AvailableCreditRow> rows = LoadOnAccountPayments(ctx, head.AD_Client_ID, C_BPartner_ID,
                IsCreditNote, page, pageSize > 0 ? pageSize : ONACCOUNT_PAGE_SIZE);
            foreach (AvailableCreditRow c in rows)
            {
                ConvertRowToInvoiceCurrency(ctx, c, head);
            }
            return rows;
        }

        /// <summary>Active bank accounts of the invoice organization, role-restricted.</summary>
        /// <param name="role">session role</param>
        /// <param name="AD_Org_ID">invoice organization</param>
        /// <returns>bank account options</returns>
        private List<BankAccountOption> LoadBankAccounts(MRole role, int AD_Org_ID)
        {
            List<BankAccountOption> list = new List<BankAccountOption>();
            string sql = @"SELECT ba.C_BankAccount_ID, ba.AccountNo, b.Name AS BankName,
                                  ba.C_Currency_ID, cur.ISO_Code AS CurrencyISO,
                                  cur.CurSymbol AS CurSymbol, cur.StdPrecision AS StdPrecision, ba.IsDefault
                             FROM C_BankAccount ba
                             INNER JOIN C_Bank b ON (ba.C_Bank_ID=b.C_Bank_ID)
                             INNER JOIN C_Currency cur ON (ba.C_Currency_ID=cur.C_Currency_ID)
                            WHERE ba.IsActive='Y'
                              AND ba.AD_Org_ID IN (0, @AD_Org_ID)
                            ORDER BY ba.IsDefault DESC, b.Name";
            sql = role.AddAccessSQL(sql, "ba", MRole.SQL_FULLYQUALIFIED, MRole.SQL_RO);

            DataSet ds = DB.ExecuteDataset(sql,
                new SqlParameter[] { new SqlParameter("@AD_Org_ID", AD_Org_ID) }, null);
            if (ds == null || ds.Tables.Count == 0)
            {
                return list;
            }
            foreach (DataRow r in ds.Tables[0].Rows)
            {
                list.Add(new BankAccountOption
                {
                    C_BankAccount_ID = Util.GetValueOfInt(r["C_BankAccount_ID"]),
                    BankName = Util.GetValueOfString(r["BankName"]),
                    AccountNo = Util.GetValueOfString(r["AccountNo"]),
                    C_Currency_ID = Util.GetValueOfInt(r["C_Currency_ID"]),
                    CurrencyISO = Util.GetValueOfString(r["CurrencyISO"]),
                    CurSymbol = Util.GetValueOfString(r["CurSymbol"]),
                    StdPrecision = Util.GetValueOfInt(r["StdPrecision"]),
                    IsDefault = Util.GetValueOfString(r["IsDefault"]) == "Y"
                });
            }
            return list;
        }

        /// <summary>Active payment methods (with base type for tender resolution).</summary>
        /// <param name="role">session role</param>
        /// <param name="AD_Org_ID">invoice organization</param>
        /// <returns>payment method options</returns>
        private List<PaymentMethodOption> LoadPaymentMethods(MRole role, int AD_Org_ID)
        {
            List<PaymentMethodOption> list = new List<PaymentMethodOption>();
            string sql = @"SELECT pm.VA009_PaymentMethod_ID, pm.VA009_Name, pm.VA009_PaymentBaseType
                             FROM VA009_PaymentMethod pm
                            WHERE pm.IsActive='Y'
                              AND pm.VA009_PaymentBaseType NOT IN ('B','C')
                              AND pm.AD_Org_ID IN (0, @AD_Org_ID)
                            ORDER BY pm.VA009_Name";
            try
            {
                sql = role.AddAccessSQL(sql, "pm", MRole.SQL_FULLYQUALIFIED, MRole.SQL_RO);
                DataSet ds = DB.ExecuteDataset(sql,
                    new SqlParameter[] { new SqlParameter("@AD_Org_ID", AD_Org_ID) }, null);
                if (ds == null || ds.Tables.Count == 0)
                {
                    return list;
                }
                foreach (DataRow r in ds.Tables[0].Rows)
                {
                    list.Add(new PaymentMethodOption
                    {
                        VA009_PaymentMethod_ID = Util.GetValueOfInt(r["VA009_PaymentMethod_ID"]),
                        Name = Util.GetValueOfString(r["VA009_Name"]),
                        BaseType = Util.GetValueOfString(r["VA009_PaymentBaseType"])
                    });
                }
            }
            catch (Exception ex)
            {
                log.Info("VAS_189 payment methods skipped: " + ex.Message);
            }
            return list;
        }

        /// <summary>
        /// Converts an invoice open amount into a chosen target currency on a
        /// given date using a chosen conversion type. Returns the converted
        /// amount plus the target currency metadata so the modal can re-format
        /// its amount fields.
        /// </summary>
        /// <param name="ctx">session context</param>
        /// <param name="req">invoice, target currency, conversion type, amount and date</param>
        /// <returns>converted amount + target currency metadata</returns>
        public ConvertAmountResult ConvertOpenAmount(Ctx ctx, ConvertAmountRequest req)
        {
            ConvertAmountResult res = new ConvertAmountResult();
            if (req == null || req.C_Invoice_ID <= 0 || req.C_Currency_ID <= 0)
            {
                res.Message = Msg.GetMsg(ctx, "VAS_189_InvalidRequest");
                return res;
            }

            DataSet ds = DB.ExecuteDataset(
                @"SELECT i.C_Currency_ID, i.AD_Client_ID, i.AD_Org_ID
                    FROM C_Invoice i WHERE i.C_Invoice_ID=@C_Invoice_ID",
                new SqlParameter[] { new SqlParameter("@C_Invoice_ID", req.C_Invoice_ID) }, null);
            if (ds == null || ds.Tables.Count == 0 || ds.Tables[0].Rows.Count == 0)
            {
                res.Message = Msg.GetMsg(ctx, "VAS_189_InvoiceNotFound");
                return res;
            }
            DataRow ir = ds.Tables[0].Rows[0];
            int invCurrency = Util.GetValueOfInt(ir["C_Currency_ID"]);
            int adClient = Util.GetValueOfInt(ir["AD_Client_ID"]);
            int adOrg = Util.GetValueOfInt(ir["AD_Org_ID"]);

            int targetCurrency = req.C_Currency_ID;
            int convType = req.C_ConversionType_ID;   // 0 -> default conversion type

            MCurrency targetCur = MCurrency.Get(ctx, targetCurrency);
            res.C_Currency_ID = targetCurrency;
            res.CurSymbol = targetCur.GetCurSymbol();
            res.ISO_Code = targetCur.GetISO_Code();
            res.StdPrecision = targetCur.GetStdPrecision();

            DateTime convDate = req.Date ?? DateTime.Today;

            if (targetCurrency == invCurrency)
            {
                res.IsSameCurrency = true;
                res.Rate = 1m;
                res.ConvertedAmount = req.Amount;
                res.Success = true;
                return res;
            }

            decimal rate = MConversionRate.GetRate(invCurrency, targetCurrency, convDate, convType, adClient, adOrg);
            if (rate == 0)
            {
                res.Message = Msg.GetMsg(ctx, "VAS_189_NoConversionRate");
                return res;
            }
            res.Rate = rate;
            res.ConvertedAmount = MConversionRate.Convert(ctx, req.Amount, invCurrency, targetCurrency,
                convDate, convType, adClient, adOrg);
            res.Success = true;
            return res;
        }

        #endregion

        #region Write actions

        /// <summary>
        /// Creates a single balanced allocation that applies the selected
        /// on-account customer receipts to the target AR invoice, up to the
        /// invoice open amount.
        /// </summary>
        /// <param name="ctx">session context</param>
        /// <param name="req">target invoice + selected sources</param>
        /// <returns>applied + remaining amounts and the allocation document no.</returns>
        public AllocationResult ApplyCredits(Ctx ctx, ApplyCreditsRequest req)
        {
            AllocationResult result = new AllocationResult();
            if (req == null || req.C_Invoice_ID <= 0 || req.Sources == null || req.Sources.Count == 0)
            {
                result.Message = Msg.GetMsg(ctx, "VAS_189_NothingSelected");
                return result;
            }

            Trx trx = Trx.GetTrx(Trx.CreateTrxName("VAS189ApplyCredits"));
            try
            {
                MInvoice inv = new MInvoice(ctx, req.C_Invoice_ID, trx);
                if (inv.GetC_Invoice_ID() <= 0 || !inv.IsSOTrx())
                {
                    result.Message = Msg.GetMsg(ctx, "VAS_189_InvoiceNotFound");
                    trx.Rollback();
                    return result;
                }

                int invCurrency = inv.GetC_Currency_ID();
                decimal open = Math.Abs(inv.GetGrandTotal(false)) - GetAllocatedAmount(req.C_Invoice_ID);
                if (open <= 0)
                {
                    result.Message = Msg.GetMsg(ctx, "VAS_189_NoOpenBalance");
                    trx.Rollback();
                    return result;
                }

                // The allocation is created in the source (receipt) currency, dated to
                // the source document date, booked in the source organization and using
                // the source conversion type. The UI restricts a selection to a single
                // currency + conversion type, so these come from the selected sources.
                int allocCurrency;
                DateTime allocDate;
                int allocOrg;
                int allocConvType;
                if (!ResolveSourceCurrencyDate(req.Sources, trx, out allocCurrency, out allocDate, out allocOrg, out allocConvType))
                {
                    result.Message = Msg.GetMsg(ctx, "VAS_189_SingleCurrencyOnly");
                    trx.Rollback();
                    return result;
                }
                if (allocOrg <= 0)
                {
                    allocOrg = inv.GetAD_Org_ID();
                }
                int useConvType = allocConvType > 0 ? allocConvType : inv.GetC_ConversionType_ID();

                // Invoice open expressed in the allocation (receipt) currency. Source
                // amounts already arrive in that currency, so ONLY the invoice open and
                // the schedule dues are converted - never the source amount twice.
                decimal openAlloc = open;
                if (allocCurrency != invCurrency)
                {
                    if (MConversionRate.GetRate(invCurrency, allocCurrency, allocDate, useConvType,
                            inv.GetAD_Client_ID(), allocOrg) == 0)
                    {
                        result.Message = Msg.GetMsg(ctx, "VAS_189_NoConversionRate");
                        trx.Rollback();
                        return result;
                    }
                    openAlloc = MConversionRate.Convert(ctx, open, invCurrency, allocCurrency, allocDate, useConvType,
                        inv.GetAD_Client_ID(), allocOrg);
                }

                // Open pay schedules (due-date order). Each is loaded so it can be split
                // BEFORE its allocation line; schedRemaining tracks the unallocated
                // balance in the allocation (receipt) currency.
                List<MInvoicePaySchedule> schedObjs = new List<MInvoicePaySchedule>();
                List<decimal> schedRemaining = new List<decimal>();
                DataSet schedDs = DB.ExecuteDataset(
                    @"SELECT ips.C_InvoicePaySchedule_ID, ips.DueAmt FROM C_InvoicePaySchedule ips
                       WHERE ips.C_Invoice_ID=@C_Invoice_ID AND COALESCE(ips.VA009_IsPaid,'N')='N'
                       ORDER BY ips.DueDate, ips.C_InvoicePaySchedule_ID",
                    new SqlParameter[] { new SqlParameter("@C_Invoice_ID", req.C_Invoice_ID) }, trx);
                if (schedDs != null && schedDs.Tables.Count > 0)
                {
                    foreach (DataRow sr in schedDs.Tables[0].Rows)
                    {
                        decimal due = Util.GetValueOfDecimal(sr["DueAmt"]);
                        if (allocCurrency != invCurrency)
                        {
                            due = MConversionRate.Convert(ctx, due, invCurrency, allocCurrency, allocDate, useConvType,
                                inv.GetAD_Client_ID(), allocOrg);
                        }
                        schedObjs.Add(new MInvoicePaySchedule(ctx, Util.GetValueOfInt(sr["C_InvoicePaySchedule_ID"]), trx));
                        schedRemaining.Add(due);
                    }
                }

                MAllocationHdr hdr = new MAllocationHdr(ctx, true, allocDate,
                    allocCurrency, Msg.GetMsg(ctx, "VAS_189_AllocApplyCredits"), trx);
                hdr.SetAD_Org_ID(allocOrg);
                if (useConvType > 0)
                {
                    hdr.SetC_ConversionType_ID(useConvType);
                }
                if (!hdr.Save(trx))
                {
                    result.Message = RetrieveErr(ctx, "VAS_189_AllocationFailed");
                    trx.Rollback();
                    return result;
                }

                decimal appliedAlloc = 0m, remainingAlloc = openAlloc;
                int schedIdx = 0;
                foreach (AllocationSource src in req.Sources)
                {
                    if (remainingAlloc <= 0)
                    {
                        break;
                    }
                    decimal useAlloc = Math.Min(src.Amount, remainingAlloc);
                    if (useAlloc <= 0)
                    {
                        continue;
                    }

                    decimal srcRem = useAlloc;
                    while (srcRem > 0 && schedIdx < schedObjs.Count)
                    {
                        if (schedRemaining[schedIdx] <= 0)
                        {
                            schedIdx++;
                            continue;
                        }
                        decimal portion = Math.Min(srcRem, schedRemaining[schedIdx]);
                        bool fullConsume = portion >= schedRemaining[schedIdx] - (decimal)0.0001;
                        decimal portionInv = portion;
                        if (allocCurrency != invCurrency)
                        {
                            portionInv = MConversionRate.Convert(ctx, portion, allocCurrency, invCurrency, allocDate, useConvType,
                                inv.GetAD_Client_ID(), allocOrg);
                        }

                        bool ok;
                        int refSchedId = PrepareScheduleLine(ctx, schedObjs, schedIdx, portionInv, fullConsume, trx, result, out ok);
                        if (!ok)
                        {
                            return result;   // PrepareScheduleLine already rolled back
                        }
                        if (!CreateReceiptAllocLine(ctx, hdr, inv, src, portion, refSchedId, trx, result))
                        {
                            return result;   // CreateReceiptAllocLine already rolled back
                        }
                        schedRemaining[schedIdx] -= portion;
                        srcRem -= portion;
                        if (schedRemaining[schedIdx] <= 0)
                        {
                            schedIdx++;
                        }
                    }

                    appliedAlloc += useAlloc;
                    remainingAlloc -= useAlloc;
                }

                if (appliedAlloc <= 0)
                {
                    result.Message = Msg.GetMsg(ctx, "VAS_189_NothingSelected");
                    trx.Rollback();
                    return result;
                }

                if (!CompleteAllocation(hdr, trx, ctx, result))
                {
                    return result;   // CompleteAllocation already rolled back
                }

                // Applied amount returned in the invoice currency for the UI settlement
                // (a single back-conversion of the total, not of each source amount).
                decimal appliedInv = appliedAlloc;
                if (allocCurrency != invCurrency)
                {
                    appliedInv = MConversionRate.Convert(ctx, appliedAlloc, allocCurrency, invCurrency, allocDate, useConvType,
                        inv.GetAD_Client_ID(), allocOrg);
                }

                trx.Commit();
                result.Success = true;
                result.DocumentNo = hdr.GetDocumentNo();
                result.AppliedAmount = appliedInv;
                result.RemainingAmount = Math.Max(0m, open - appliedInv);
            }
            catch (Exception ex)
            {
                try { if (trx != null) { trx.Rollback(); } } catch { /* ignore */ }
                result.Message = ex.Message;
            }
            finally
            {
                // trx was started -> it must be closed and nulled before returning
                // (runs on every exit path, including the early validation returns).
                if (trx != null)
                {
                    try { trx.Close(); } catch { /* ignore */ }
                    trx = null;
                }
            }
            return result;
        }

        /// <summary>
        /// Allocates the current AR credit note to one or more selected open AR
        /// invoices of the same customer through a single balanced allocation.
        /// </summary>
        /// <param name="ctx">session context</param>
        /// <param name="req">credit note id, selected open invoices and cash discount</param>
        /// <returns>applied + remaining credit and the allocation document no.</returns>
        public AllocationResult AllocateCreditNote(Ctx ctx, AllocateCreditNoteRequest req)
        {
            AllocationResult result = new AllocationResult();
            if (req == null || req.C_Invoice_ID <= 0 || req.C_Invoice_IDs == null || req.C_Invoice_IDs.Count == 0)
            {
                result.Message = Msg.GetMsg(ctx, "VAS_189_NothingSelected");
                return result;
            }

            Trx trx = Trx.GetTrx(Trx.CreateTrxName("VAS189AllocateCN"));
            try
            {
                MInvoice cn = new MInvoice(ctx, req.C_Invoice_ID, trx);
                if (cn.GetC_Invoice_ID() <= 0 || !cn.IsSOTrx() || !cn.IsReturnTrx())
                {
                    result.Message = Msg.GetMsg(ctx, "VAS_189_InvoiceNotFound");
                    trx.Rollback();
                    return result;
                }

                decimal creditOpen = Math.Abs(cn.GetGrandTotal(false)) - GetAllocatedAmount(req.C_Invoice_ID);
                if (creditOpen <= 0)
                {
                    result.Message = Msg.GetMsg(ctx, "VAS_189_NoOpenBalance");
                    trx.Rollback();
                    return result;
                }

                // The allocation is created in the credit-note currency; the target open
                // invoices (and their schedules) are converted into it on the allocation
                // date using the credit-note conversion type.
                int allocCurrency = cn.GetC_Currency_ID();
                DateTime allocDate = DateTime.Today;
                int allocConvType = cn.GetC_ConversionType_ID();

                MAllocationHdr hdr = new MAllocationHdr(ctx, true, allocDate,
                    allocCurrency, Msg.GetMsg(ctx, "VAS_189_AllocCreditNote"), trx);
                hdr.SetAD_Org_ID(cn.GetAD_Org_ID());
                if (allocConvType > 0)
                {
                    hdr.SetC_ConversionType_ID(allocConvType);
                }
                if (!hdr.Save(trx))
                {
                    result.Message = RetrieveErr(ctx, "VAS_189_AllocationFailed");
                    trx.Rollback();
                    return result;
                }

                // The cash discount is a second settlement pool alongside the credit: it
                // writes the target invoices down without consuming the credit note, so
                // it is tracked separately from `remaining` and never reduces `applied`.
                decimal applied = 0m, remaining = creditOpen;
                decimal discApplied = 0m, remDisc = req.DiscountAmt > 0 ? req.DiscountAmt : 0m;
                foreach (int openInvId in req.C_Invoice_IDs)
                {
                    if (remaining <= 0 && remDisc <= 0)
                    {
                        break;
                    }

                    DataSet tds = DB.ExecuteDataset(
                        @"SELECT i.C_Currency_ID, i.AD_Client_ID, i.AD_Org_ID
                            FROM C_Invoice i WHERE i.C_Invoice_ID=@id",
                        new SqlParameter[] { new SqlParameter("@id", openInvId) }, trx);
                    if (tds == null || tds.Tables.Count == 0 || tds.Tables[0].Rows.Count == 0)
                    {
                        continue;
                    }
                    int tCur = Util.GetValueOfInt(tds.Tables[0].Rows[0]["C_Currency_ID"]);
                    int tClient = Util.GetValueOfInt(tds.Tables[0].Rows[0]["AD_Client_ID"]);
                    int tOrg = Util.GetValueOfInt(tds.Tables[0].Rows[0]["AD_Org_ID"]);

                    decimal tOpen = Math.Abs(GetInvoiceOpen(openInvId, trx));   // target currency
                    if (tOpen <= 0)
                    {
                        continue;
                    }

                    decimal tOpenAlloc = tOpen;
                    if (tCur != allocCurrency)
                    {
                        if (MConversionRate.GetRate(tCur, allocCurrency, allocDate, allocConvType, tClient, tOrg) == 0)
                        {
                            result.Message = Msg.GetMsg(ctx, "VAS_189_NoConversionRate");
                            trx.Rollback();
                            return result;
                        }
                        tOpenAlloc = MConversionRate.Convert(ctx, tOpen, tCur, allocCurrency, allocDate, allocConvType, tClient, tOrg);
                    }

                    // The discount settles this invoice exactly as the credit does, so the
                    // two are spent together and capped at the invoice open: discount
                    // first, credit for the rest. Sizing on the credit alone left the
                    // discount with nothing to land on.
                    decimal discUse = Math.Min(remDisc, tOpenAlloc);
                    decimal use = Math.Min(remaining, tOpenAlloc - discUse);
                    if (use <= 0 && discUse <= 0)
                    {
                        continue;
                    }

                    List<MInvoicePaySchedule> schedObjs = new List<MInvoicePaySchedule>();
                    List<decimal> schedRemaining = new List<decimal>();
                    DataSet schedDs = DB.ExecuteDataset(
                        @"SELECT ips.C_InvoicePaySchedule_ID, ips.DueAmt FROM C_InvoicePaySchedule ips
                           WHERE ips.C_Invoice_ID=@id AND COALESCE(ips.VA009_IsPaid,'N')='N'
                           ORDER BY ips.DueDate, ips.C_InvoicePaySchedule_ID",
                        new SqlParameter[] { new SqlParameter("@id", openInvId) }, trx);
                    if (schedDs != null && schedDs.Tables.Count > 0)
                    {
                        foreach (DataRow sr in schedDs.Tables[0].Rows)
                        {
                            decimal due = Util.GetValueOfDecimal(sr["DueAmt"]);
                            if (tCur != allocCurrency)
                            {
                                due = MConversionRate.Convert(ctx, due, tCur, allocCurrency, allocDate, allocConvType, tClient, tOrg);
                            }
                            schedObjs.Add(new MInvoicePaySchedule(ctx, Util.GetValueOfInt(sr["C_InvoicePaySchedule_ID"]), trx));
                            schedRemaining.Add(due);
                        }
                    }

                    decimal srcRem = use;
                    decimal discRem = discUse;
                    int schedIdx = 0;
                    while ((srcRem > 0 || discRem > 0) && schedIdx < schedObjs.Count)
                    {
                        if (schedRemaining[schedIdx] <= 0)
                        {
                            schedIdx++;
                            continue;
                        }
                        // Within a schedule the discount goes first and the credit takes
                        // the rest of the due, so the pair never settles more than the
                        // schedule is worth and no line is left over-applied.
                        decimal discPortion = Math.Min(discRem, schedRemaining[schedIdx]);
                        decimal portion = Math.Min(srcRem, schedRemaining[schedIdx] - discPortion);
                        decimal settled = portion + discPortion;
                        if (settled <= 0)
                        {
                            break;
                        }
                        bool fullConsume = settled >= schedRemaining[schedIdx] - (decimal)0.0001;
                        // The schedule is resized to what the credit AND the discount settle
                        // together - sizing it to the credit alone would leave the
                        // discounted part sitting open on the remainder schedule.
                        decimal settledInv = settled;
                        if (tCur != allocCurrency)
                        {
                            settledInv = MConversionRate.Convert(ctx, settled, allocCurrency, tCur, allocDate, allocConvType, tClient, tOrg);
                        }
                        bool ok;
                        int refSchedId = PrepareScheduleLine(ctx, schedObjs, schedIdx, settledInv, fullConsume, trx, result, out ok);
                        if (!ok)
                        {
                            return result;
                        }
                        if (!CreateCreditNoteAllocLines(ctx, hdr, cn.GetC_BPartner_ID(), openInvId, req.C_Invoice_ID,
                                portion, discPortion, refSchedId, trx, result))
                        {
                            return result;
                        }
                        schedRemaining[schedIdx] -= settled;
                        srcRem -= portion;
                        discRem -= discPortion;
                        if (schedRemaining[schedIdx] <= 0)
                        {
                            schedIdx++;
                        }
                    }
                    if (srcRem > 0 || discRem > 0)
                    {
                        if (!CreateCreditNoteAllocLines(ctx, hdr, cn.GetC_BPartner_ID(), openInvId, req.C_Invoice_ID,
                                srcRem, discRem, 0, trx, result))
                        {
                            return result;
                        }
                    }

                    applied += use;
                    remaining -= use;
                    discApplied += discUse;
                    remDisc -= discUse;
                }

                // A discount-only allocation still settles something, so it counts here -
                // testing the credit alone would roll back a legitimate write-down.
                if (applied <= 0 && discApplied <= 0)
                {
                    result.Message = Msg.GetMsg(ctx, "VAS_189_NothingSelected");
                    trx.Rollback();
                    return result;
                }

                if (!CompleteAllocation(hdr, trx, ctx, result))
                {
                    return result;
                }

                trx.Commit();
                result.Success = true;
                result.DocumentNo = hdr.GetDocumentNo();
                result.AppliedAmount = applied;
                result.RemainingAmount = Math.Max(0m, creditOpen - applied);
            }
            catch (Exception ex)
            {
                try { if (trx != null) { trx.Rollback(); } } catch { /* ignore */ }
                result.Message = ex.Message;
            }
            finally
            {
                if (trx != null)
                {
                    try { trx.Close(); } catch { /* ignore */ }
                    trx = null;
                }
            }
            return result;
        }

        /// <summary>
        /// Creates and completes a customer receipt (C_Payment, IsReceipt='Y')
        /// for the AR invoice open amount and allocates it against the invoice
        /// schedule(s).
        /// </summary>
        /// <param name="ctx">session context</param>
        /// <param name="req">receipt fields</param>
        /// <returns>payment document no. and amount</returns>
        public RecordPaymentResult RecordPayment(Ctx ctx, RecordPaymentRequest req)
        {
            RecordPaymentResult result = new RecordPaymentResult();
            if (req == null || req.C_Invoice_ID <= 0)
            {
                result.Message = Msg.GetMsg(ctx, "VAS_189_InvalidRequest");
                return result;
            }

            Trx trx = Trx.GetTrx(Trx.CreateTrxName("VAS189RecordPayment"));
            try
            {
                MInvoice inv = new MInvoice(ctx, req.C_Invoice_ID, trx);
                if (inv.GetC_Invoice_ID() <= 0 || !inv.IsSOTrx())
                {
                    result.Message = Msg.GetMsg(ctx, "VAS_189_InvoiceNotFound");
                    trx.Rollback();
                    return result;
                }
                if (inv.GetDocStatus() != "CO" && inv.GetDocStatus() != "CL")
                {
                    result.Message = Msg.GetMsg(ctx, "VAS_189_InvoiceNotCompleted");
                    trx.Rollback();
                    return result;
                }

                decimal open = GetInvoiceOpen(req.C_Invoice_ID, trx);
                if (open <= 0)
                {
                    result.Message = Msg.GetMsg(ctx, "VAS_189_NoOpenBalance");
                    trx.Rollback();
                    return result;
                }

                // The receipt is recorded in the bank-account currency. When it differs
                // from the invoice currency, the invoice open and every schedule due are
                // converted on the payment date using the chosen conversion type.
                int invCurrency = inv.GetC_Currency_ID();
                int payCurrency = req.C_Currency_ID > 0 ? req.C_Currency_ID : invCurrency;
                DateTime txDate = req.DateTrx ?? DateTime.Today;
                int convTypeId = req.C_ConversionType_ID > 0 ? req.C_ConversionType_ID : inv.GetC_ConversionType_ID();

                decimal openPay = open;
                if (payCurrency != invCurrency)
                {
                    decimal payRate = MConversionRate.GetRate(invCurrency, payCurrency, txDate, convTypeId,
                        inv.GetAD_Client_ID(), inv.GetAD_Org_ID());
                    if (payRate == 0)
                    {
                        result.Message = Msg.GetMsg(ctx, "VAS_189_NoConversionRate");
                        trx.Rollback();
                        return result;
                    }
                    openPay = MConversionRate.Convert(ctx, open, invCurrency, payCurrency, txDate, convTypeId,
                        inv.GetAD_Client_ID(), inv.GetAD_Org_ID());
                }

                decimal payAmt = req.PayAmt > 0 ? req.PayAmt : openPay;
                if (payAmt <= 0)
                {
                    result.Message = Msg.GetMsg(ctx, "VAS_189_AmountMustBePositive");
                    trx.Rollback();
                    return result;
                }
                if (payAmt - openPay > (decimal)0.0001)
                {
                    result.Message = Msg.GetMsg(ctx, "VAS_189_AmountExceedsOpen");
                    trx.Rollback();
                    return result;
                }

                // AR receipt document type.
                int arrDocType = Util.GetValueOfInt(DB.ExecuteScalar(
                    @"SELECT dt.C_DocType_ID FROM C_DocType dt
                       WHERE dt.IsActive='Y' AND dt.DocBaseType='ARR'
                         AND dt.AD_Client_ID=@AD_Client_ID AND dt.AD_Org_ID IN (0, @AD_Org_ID)
                       ORDER BY dt.IsDefault DESC, dt.AD_Org_ID DESC",
                    new SqlParameter[]
                    {
                        new SqlParameter("@AD_Client_ID", ctx.GetAD_Client_ID()),
                        new SqlParameter("@AD_Org_ID", inv.GetAD_Org_ID())
                    }, trx));

                string baseType = "";
                if (req.VA009_PaymentMethod_ID > 0)
                {
                    baseType = Util.GetValueOfString(DB.ExecuteScalar(
                        @"SELECT pm.VA009_PaymentBaseType FROM VA009_PaymentMethod pm
                           WHERE pm.VA009_PaymentMethod_ID=@id",
                        new SqlParameter[] { new SqlParameter("@id", req.VA009_PaymentMethod_ID) }, trx));
                }

                MPayment payment = new MPayment(ctx, 0, trx);
                payment.SetAD_Client_ID(inv.GetAD_Client_ID());
                payment.SetAD_Org_ID(inv.GetAD_Org_ID());
                payment.SetIsReceipt(true);                  // AR -> incoming
                if (arrDocType > 0)
                {
                    payment.SetC_DocType_ID(arrDocType);
                }
                if (req.C_BankAccount_ID > 0)
                {
                    payment.SetC_BankAccount_ID(req.C_BankAccount_ID);
                }
                payment.SetC_BPartner_ID(inv.GetC_BPartner_ID());
                payment.SetC_BPartner_Location_ID(inv.GetC_BPartner_Location_ID());
                payment.SetC_Currency_ID(payCurrency);
                if (convTypeId > 0)
                {
                    payment.SetC_ConversionType_ID(convTypeId);
                }
                if (req.VA009_PaymentMethod_ID > 0)
                {
                    payment.SetVA009_PaymentMethod_ID(req.VA009_PaymentMethod_ID);
                }

                payment.SetDateTrx(txDate);
                payment.SetDateAcct(txDate);
                payment.SetPayAmt(inv.IsReturnTrx() ? decimal.Negate(payAmt) : payAmt);
                if (req.DiscountAmt > 0)
                {
                    payment.SetDiscountAmt(inv.IsReturnTrx() ? decimal.Negate(req.DiscountAmt) : req.DiscountAmt);
                }

                // VA009_PaymentBaseType 'S' = Check (it maps to tender type 'K').
                if (baseType == "S")
                {
                    if (!req.CheckDate.HasValue || string.IsNullOrEmpty(req.ReferenceNo))
                    {
                        result.Message = Msg.GetMsg(ctx, "VAS_189_CheckDateRefRequired");
                        trx.Rollback();
                        return result;
                    }
                    payment.SetTenderType(MPayment.TENDERTYPE_Check);
                    payment.SetCheckDate(req.CheckDate.Value);
                    payment.SetCheckNo(req.ReferenceNo);
                }
                else if (!string.IsNullOrEmpty(req.ReferenceNo))
                {
                    payment.SetTrxNo(req.ReferenceNo);
                }

                // Resolve the amount against unpaid pay schedules in due-date order. The
                // amount cascades: each schedule is filled up to its DueAmt before the
                // next one is touched.
                DataSet schedDs = DB.ExecuteDataset(
                    @"SELECT ips.C_InvoicePaySchedule_ID, ips.DueAmt FROM C_InvoicePaySchedule ips
                       WHERE ips.C_Invoice_ID=@C_Invoice_ID AND COALESCE(ips.VA009_IsPaid,'N')='N'
                       ORDER BY ips.DueDate, ips.C_InvoicePaySchedule_ID",
                    new SqlParameter[] { new SqlParameter("@C_Invoice_ID", req.C_Invoice_ID) }, trx);

                List<int> planSchedId = new List<int>();
                List<decimal> planDue = new List<decimal>();
                List<decimal> planAlloc = new List<decimal>();
                List<decimal> planDisc = new List<decimal>();
                if (schedDs != null && schedDs.Tables.Count > 0)
                {
                    // The cash discount settles a schedule exactly as the cash does, so the
                    // cascade has to spend both together. Planning on the pay amount alone
                    // stopped at the first schedule the cash covered and then pushed the
                    // whole discount onto it, taking that schedule past its due and turning
                    // its OverUnderAmt negative on a positive receipt.
                    // Each schedule absorbs discount first and payment for the rest of its
                    // due, and never more than its own due - so Amount + Discount <= DueAmt
                    // on every line and the shortfall keeps the sign of the receipt.
                    decimal remPay = payAmt;
                    decimal remDisc = req.DiscountAmt > 0 ? req.DiscountAmt : 0;
                    foreach (DataRow sr in schedDs.Tables[0].Rows)
                    {
                        if (remPay <= 0 && remDisc <= 0)
                        {
                            break;
                        }
                        decimal due = Util.GetValueOfDecimal(sr["DueAmt"]);
                        // Schedule dues are stored in the invoice currency; express them in
                        // the payment currency so the cascade and the over/under amounts
                        // stay consistent with the recorded receipt.
                        if (payCurrency != invCurrency)
                        {
                            due = MConversionRate.Convert(ctx, due, invCurrency, payCurrency, txDate, convTypeId,
                                inv.GetAD_Client_ID(), inv.GetAD_Org_ID());
                        }
                        // A credit-note schedule carries a negative due. The plan works in
                        // magnitudes throughout; the return sign goes back on at set-time.
                        due = Math.Abs(due);
                        if (due <= 0)
                        {
                            continue;
                        }
                        decimal disc = Math.Min(remDisc, due);
                        decimal alloc = Math.Min(remPay, due - disc);
                        if (alloc <= 0 && disc <= 0)
                        {
                            continue;
                        }
                        planSchedId.Add(Util.GetValueOfInt(sr["C_InvoicePaySchedule_ID"]));
                        planDue.Add(due);
                        planAlloc.Add(alloc);
                        planDisc.Add(disc);
                        remPay -= alloc;
                        remDisc -= disc;
                    }
                }

                if (planSchedId.Count <= 1)
                {
                    // A single schedule is settled directly on the payment header.
                    payment.SetC_Invoice_ID(req.C_Invoice_ID);
                    if (planSchedId.Count == 1)
                    {
                        payment.SetC_InvoicePaySchedule_ID(planSchedId[0]);
                        // OverUnder = DueAmt - Amount - Discount (>0 underpayment). The
                        // discount the cascade actually placed on this schedule is used,
                        // not the whole requested amount, so the shortfall can never come
                        // out with the opposite sign to the receipt.
                        decimal ou = planDue[0] - planAlloc[0] - planDisc[0];
                        payment.SetOverUnderAmt(inv.IsReturnTrx() ? decimal.Negate(ou) : ou);
                        payment.SetIsOverUnderPayment(ou != 0);
                    }
                }
                else
                {
                    // The amount spans several schedules -> applied via payment allocate
                    // lines below; the header carries no invoice/schedule link. The
                    // discount is distributed onto those lines (MPayment.AllocateIt uses
                    // the per-line discount), so the header discount is cleared here to
                    // avoid counting it twice.
                    payment.SetOverUnderAmt(0);
                    payment.SetIsOverUnderPayment(false);
                    payment.SetDiscountAmt(0);
                }

                payment.SetDocStatus(MPayment.DOCSTATUS_InProgress);
                payment.SetDocAction(MPayment.DOCACTION_Complete);
                if (!payment.Save(trx))
                {
                    result.Message = RetrieveErr(ctx, "VAS_189_PaymentSaveFailed");
                    trx.Rollback();
                    return result;
                }

                if (planSchedId.Count > 1)
                {
                    // The cascade above already split both the cash and the discount across
                    // these schedules; each line just writes back its own planned share.
                    // Shares are positive magnitudes - the credit-note sign is applied when
                    // the value is set.
                    for (int i = 0; i < planSchedId.Count; i++)
                    {
                        decimal due = planDue[i];
                        decimal alloc = planAlloc[i];
                        decimal disc = planDisc[i];

                        MPaymentAllocate pa = new MPaymentAllocate(ctx, 0, trx);
                        pa.SetAD_Client_ID(payment.GetAD_Client_ID());
                        pa.SetAD_Org_ID(payment.GetAD_Org_ID());
                        pa.SetC_Payment_ID(payment.GetC_Payment_ID());
                        pa.SetC_Invoice_ID(req.C_Invoice_ID);
                        pa.SetC_InvoicePaySchedule_ID(planSchedId[i]);
                        pa.SetAmount(inv.IsReturnTrx() ? decimal.Negate(alloc) : alloc);
                        pa.SetDiscountAmt(inv.IsReturnTrx() ? decimal.Negate(disc) : disc);
                        // Keep Amount + Discount + OverUnderAmt = InvoiceAmt balanced
                        // (OverUnderAmt = the shortfall, 0 when fully settled). Amount and
                        // Discount are capped at the due, so the shortfall is never negative
                        // and always carries the same sign as PayAmt.
                        decimal ou = due - alloc - disc;
                        pa.SetOverUnderAmt(inv.IsReturnTrx() ? decimal.Negate(ou) : ou);
                        pa.SetInvoiceAmt(pa.GetAmount() + pa.GetDiscountAmt() + pa.GetWriteOffAmt() + pa.GetOverUnderAmt());
                        if (!pa.Save(trx))
                        {
                            result.Message = RetrieveErr(ctx, "VAS_189_PaymentSaveFailed");
                            trx.Rollback();
                            return result;
                        }
                    }
                }

                bool processed;
                try
                {
                    processed = payment.ProcessIt(MPayment.DOCACTION_Complete);
                }
                catch (Exception pex)
                {
                    processed = false;
                    result.Message = pex.Message;
                }
                if (!processed)
                {
                    result.Message = payment.GetProcessMsg();
                    if (string.IsNullOrEmpty(result.Message))
                    {
                        result.Message = RetrieveErr(ctx, "VAS_189_PaymentCompleteFailed");
                    }
                    trx.Rollback();
                    return result;
                }
                payment.Save(trx);

                trx.Commit();
                result.Success = true;
                result.C_Payment_ID = payment.GetC_Payment_ID();
                result.DocumentNo = payment.GetDocumentNo();
                result.PayAmt = payAmt;
            }
            catch (Exception ex)
            {
                try { if (trx != null) { trx.Rollback(); } } catch { /* ignore */ }
                result.Message = ex.Message;
            }
            finally
            {
                if (trx != null)
                {
                    try { trx.Close(); } catch { /* ignore */ }
                    trx = null;
                }
            }
            return result;
        }

        #endregion

        #region Write helpers

        /// <summary>
        /// Prepares the invoice pay-schedule an allocation line will reference,
        /// splitting it BEFORE the line is created. When the portion only partly
        /// settles the schedule, the EXISTING schedule is resized to the paid
        /// portion (invoice currency) - the line references it - and a NEW
        /// schedule carries the remaining open balance, replacing the list slot
        /// so the next portion splits the remainder.
        /// </summary>
        /// <param name="ctx">session context</param>
        /// <param name="schedObjs">open schedules</param>
        /// <param name="idx">index of the schedule being settled</param>
        /// <param name="portionInv">portion in invoice currency</param>
        /// <param name="fullConsume">portion settles the whole remaining due</param>
        /// <param name="trx">active transaction</param>
        /// <param name="result">result carrying the failure message</param>
        /// <param name="ok">out: false when the save failed (already rolled back)</param>
        /// <returns>C_InvoicePaySchedule_ID for the allocation line (0 on failure)</returns>
        private int PrepareScheduleLine(Ctx ctx, List<MInvoicePaySchedule> schedObjs, int idx, decimal portionInv,
            bool fullConsume, Trx trx, AllocationResult result, out bool ok)
        {
            ok = true;
            MInvoicePaySchedule sch = schedObjs[idx];
            if (fullConsume)
            {
                // Whole remaining due settled - reference the schedule itself (no resize,
                // so a cross-currency full settlement does not alter the schedule).
                return sch.GetC_InvoicePaySchedule_ID();
            }

            decimal curDue = sch.GetDueAmt();

            MInvoicePaySchedule remainder = new MInvoicePaySchedule(ctx, 0, trx);
            PO.CopyValues(sch, remainder);
            remainder.SetAD_Client_ID(sch.GetAD_Client_ID());
            remainder.SetAD_Org_ID(sch.GetAD_Org_ID());
            remainder.ByPassValidatePayScheduleCondition(true);
            remainder.SetDueAmt(curDue - portionInv);
            if (!remainder.Save(trx))
            {
                result.Message = RetrieveErr(ctx, "VAS_189_AllocationFailed");
                trx.Rollback();
                ok = false;
                return 0;
            }

            sch.ByPassValidatePayScheduleCondition(true);
            sch.SetDueAmt(portionInv);
            if (!sch.Save(trx))
            {
                result.Message = RetrieveErr(ctx, "VAS_189_AllocationFailed");
                trx.Rollback();
                ok = false;
                return 0;
            }

            int refId = sch.GetC_InvoicePaySchedule_ID();
            schedObjs[idx] = remainder;   // next portion splits the remainder
            return refId;
        }

        /// <summary>
        /// Resolves the common currency, source document date, organization and
        /// conversion type for the selected allocation sources. Returns false
        /// when the selection mixes currencies or conversion types (the UI
        /// already restricts this, so it is a defensive guard).
        /// </summary>
        /// <param name="sources">selected sources</param>
        /// <param name="trx">active transaction</param>
        /// <param name="currency">out: allocation currency</param>
        /// <param name="date">out: allocation date</param>
        /// <param name="org">out: allocation organization</param>
        /// <param name="convType">out: conversion type</param>
        /// <returns>true when a single basis was resolved</returns>
        private bool ResolveSourceCurrencyDate(List<AllocationSource> sources, Trx trx, out int currency,
            out DateTime date, out int org, out int convType)
        {
            currency = 0;
            date = DateTime.Today;
            org = 0;
            convType = 0;
            bool first = true;
            foreach (AllocationSource src in sources)
            {
                int cur, srcOrg, srcConv;
                DateTime? dt;
                if (src.SourceType == "PAYMENT")
                {
                    DataSet ds = DB.ExecuteDataset(
                        @"SELECT p.C_Currency_ID, p.DateTrx, p.AD_Org_ID,
                                 COALESCE(p.C_ConversionType_ID, 0) AS C_ConversionType_ID
                            FROM C_Payment p WHERE p.C_Payment_ID=@id",
                        new SqlParameter[] { new SqlParameter("@id", src.Id) }, trx);
                    if (ds == null || ds.Tables.Count == 0 || ds.Tables[0].Rows.Count == 0)
                    {
                        continue;
                    }
                    cur = Util.GetValueOfInt(ds.Tables[0].Rows[0]["C_Currency_ID"]);
                    dt = Util.GetValueOfDateTime(ds.Tables[0].Rows[0]["DateTrx"]);
                    srcOrg = Util.GetValueOfInt(ds.Tables[0].Rows[0]["AD_Org_ID"]);
                    srcConv = Util.GetValueOfInt(ds.Tables[0].Rows[0]["C_ConversionType_ID"]);
                }
                else
                {
                    DataSet ds = DB.ExecuteDataset(
                        @"SELECT i.C_Currency_ID, i.DateInvoiced, i.AD_Org_ID,
                                 COALESCE(i.C_ConversionType_ID, 0) AS C_ConversionType_ID
                            FROM C_Invoice i WHERE i.C_Invoice_ID=@id",
                        new SqlParameter[] { new SqlParameter("@id", src.Id) }, trx);
                    if (ds == null || ds.Tables.Count == 0 || ds.Tables[0].Rows.Count == 0)
                    {
                        continue;
                    }
                    cur = Util.GetValueOfInt(ds.Tables[0].Rows[0]["C_Currency_ID"]);
                    dt = Util.GetValueOfDateTime(ds.Tables[0].Rows[0]["DateInvoiced"]);
                    srcOrg = Util.GetValueOfInt(ds.Tables[0].Rows[0]["AD_Org_ID"]);
                    srcConv = Util.GetValueOfInt(ds.Tables[0].Rows[0]["C_ConversionType_ID"]);
                }
                if (first)
                {
                    currency = cur;
                    if (dt.HasValue)
                    {
                        date = dt.Value;
                    }
                    org = srcOrg;
                    convType = srcConv;
                    first = false;
                }
                else if (cur != currency || srcConv != convType)
                {
                    return false;   // mixed currency / conversion type in one allocation
                }
            }
            return currency > 0;
        }

        /// <summary>
        /// Creates the allocation line that applies one on-account receipt to the
        /// target invoice. The posting derives DR/CR from the invoice + payment
        /// references, so on the sales side the applied amount is positive.
        /// </summary>
        /// <param name="ctx">session context</param>
        /// <param name="hdr">allocation header</param>
        /// <param name="inv">target invoice</param>
        /// <param name="src">source receipt</param>
        /// <param name="amount">amount in allocation currency</param>
        /// <param name="C_InvoicePaySchedule_ID">settled schedule (0 when none)</param>
        /// <param name="trx">active transaction</param>
        /// <param name="result">result carrying the failure message</param>
        /// <returns>false (and rolled back) on save failure</returns>
        private bool CreateReceiptAllocLine(Ctx ctx, MAllocationHdr hdr, MInvoice inv, AllocationSource src,
            decimal amount, int C_InvoicePaySchedule_ID, Trx trx, AllocationResult result)
        {
            MAllocationLine line = new MAllocationLine(hdr,
                inv.IsReturnTrx() ? decimal.Negate(amount) : amount, Env.ZERO, Env.ZERO, Env.ZERO);
            line.SetDocInfo(inv.GetC_BPartner_ID(), 0, inv.GetC_Invoice_ID());
            line.SetPaymentInfo(src.Id, 0);
            if (C_InvoicePaySchedule_ID > 0)
            {
                line.SetC_InvoicePaySchedule_ID(C_InvoicePaySchedule_ID);
            }
            if (!line.Save(trx))
            {
                result.Message = RetrieveErr(ctx, "VAS_189_AllocationFailed");
                trx.Rollback();
                return false;
            }
            return true;
        }

        /// <summary>
        /// Creates the balanced allocation line pair for a credit-note
        /// allocation: a positive line on the target invoice (optionally
        /// referencing the schedule it settles) and a negative line on the
        /// credit note, cross-referenced so the netting is tracked and posted.
        /// </summary>
        /// <param name="ctx">session context</param>
        /// <param name="hdr">allocation header</param>
        /// <param name="C_BPartner_ID">customer</param>
        /// <param name="targetInvoiceId">invoice being settled</param>
        /// <param name="creditNoteId">credit note being consumed</param>
        /// <param name="amount">credit amount in allocation currency</param>
        /// <param name="discountAmt">cash discount in allocation currency (0 when none)</param>
        /// <param name="C_InvoicePaySchedule_ID">settled schedule (0 when none)</param>
        /// <param name="trx">active transaction</param>
        /// <param name="result">result carrying the failure message</param>
        /// <returns>false (and rolled back) on save failure</returns>
        private bool CreateCreditNoteAllocLines(Ctx ctx, MAllocationHdr hdr, int C_BPartner_ID,
            int targetInvoiceId, int creditNoteId, decimal amount, decimal discountAmt,
            int C_InvoicePaySchedule_ID, Trx trx, AllocationResult result)
        {
            if (amount == 0 && discountAmt == 0)
            {
                return true;
            }

            // The invoice side carries the cash discount: it writes the invoice down
            // next to the credit. The credit-note side carries the credit alone - a
            // discount does not consume the credit note - so the two Amounts still net
            // to zero and the allocation stays balanced.
            MAllocationLine inLine = new MAllocationLine(hdr, amount, discountAmt, Env.ZERO, Env.ZERO);
            inLine.SetDocInfo(C_BPartner_ID, 0, targetInvoiceId);
            inLine.SetRef_C_Invoice_ID(creditNoteId);
            if (C_InvoicePaySchedule_ID > 0)
            {
                inLine.SetC_InvoicePaySchedule_ID(C_InvoicePaySchedule_ID);
            }
            if (!inLine.Save(trx))
            {
                result.Message = RetrieveErr(ctx, "VAS_189_AllocationFailed");
                trx.Rollback();
                return false;
            }

            // A schedule settled purely by discount consumes no credit, so it gets no
            // counter line - a zero-amount one would only clutter the allocation.
            if (amount == 0)
            {
                return true;
            }

            MAllocationLine cnLine = new MAllocationLine(hdr, decimal.Negate(amount), Env.ZERO, Env.ZERO, Env.ZERO);
            cnLine.SetDocInfo(C_BPartner_ID, 0, creditNoteId);
            cnLine.SetRef_C_Invoice_ID(targetInvoiceId);
            if (!cnLine.Save(trx))
            {
                result.Message = RetrieveErr(ctx, "VAS_189_AllocationFailed");
                trx.Rollback();
                return false;
            }
            return true;
        }

        /// <summary>
        /// Open amount for an invoice from its unpaid schedules, falling back to
        /// grand total less anything already allocated.
        /// </summary>
        /// <param name="C_Invoice_ID">target invoice</param>
        /// <param name="trx">active transaction</param>
        /// <returns>open amount in invoice currency</returns>
        private decimal GetInvoiceOpen(int C_Invoice_ID, Trx trx)
        {
            decimal sched = Util.GetValueOfDecimal(DB.ExecuteScalar(
                @"SELECT COALESCE(SUM(ips.DueAmt), 0) FROM C_InvoicePaySchedule ips
                   WHERE ips.C_Invoice_ID=@C_Invoice_ID AND COALESCE(ips.VA009_IsPaid,'N')='N'",
                new SqlParameter[] { new SqlParameter("@C_Invoice_ID", C_Invoice_ID) }, trx));
            if (sched > 0)
            {
                return sched;
            }

            decimal grand = Util.GetValueOfDecimal(DB.ExecuteScalar(
                @"SELECT COALESCE(i.GrandTotalAfterWithholding, i.GrandTotal) FROM C_Invoice i
                   WHERE i.C_Invoice_ID=@C_Invoice_ID",
                new SqlParameter[] { new SqlParameter("@C_Invoice_ID", C_Invoice_ID) }, trx));
            return Math.Max(0m, Math.Abs(grand) - GetAllocatedAmount(C_Invoice_ID));
        }

        /// <summary>Processes (completes) an allocation header, rolling back on failure.</summary>
        /// <param name="hdr">allocation header</param>
        /// <param name="trx">active transaction</param>
        /// <param name="ctx">session context</param>
        /// <param name="result">result carrying the failure message</param>
        /// <returns>true when completed</returns>
        private bool CompleteAllocation(MAllocationHdr hdr, Trx trx, Ctx ctx, AllocationResult result)
        {
            bool ok;
            try
            {
                ok = hdr.ProcessIt(MAllocationHdr.DOCACTION_Complete);
            }
            catch (Exception ex)
            {
                ok = false;
                result.Message = ex.Message;
            }
            if (!ok)
            {
                if (string.IsNullOrEmpty(result.Message))
                {
                    result.Message = RetrieveErr(ctx, "VAS_189_AllocationFailed");
                }
                trx.Rollback();
                return false;
            }
            hdr.Save(trx);
            return true;
        }

        /// <summary>Builds an error string from the logger, falling back to a message key.</summary>
        /// <param name="ctx">session context</param>
        /// <param name="fallbackKey">AD_Message key used when the logger is empty</param>
        /// <returns>user-facing error text</returns>
        private string RetrieveErr(Ctx ctx, string fallbackKey)
        {
            ValueNamePair pp = VLogger.RetrieveError();
            string baseMsg = Msg.GetMsg(ctx, fallbackKey);
            return (pp != null && !string.IsNullOrEmpty(pp.GetName()))
                   ? baseMsg + ": " + pp.GetName() : baseMsg;
        }

        #endregion

        #region DTOs (modal / write)

        public class PaymentModalMeta
        {
            public int C_Invoice_ID { get; set; }
            public int C_BPartner_ID { get; set; }
            public int C_Currency_ID { get; set; }
            public int C_ConversionType_ID { get; set; }
            public string CurSymbol { get; set; }
            public string ISO_Code { get; set; }
            public int StdPrecision { get; set; }
            public string RecordMode { get; set; }
            public bool IsARCreditNote { get; set; }
            public bool IsSOTrx { get; set; }
            public decimal CustomerOutstanding { get; set; }
            public decimal GrossInvoice { get; set; }
            public decimal Withholding { get; set; }
            public decimal NetReceivable { get; set; }
            /// <summary>Open amount still to settle; 0 is a valid value.</summary>
            public decimal? NetOpenAmount { get; set; }
            public bool IsFullySettled { get; set; }
            public decimal CreditNoteAmount { get; set; }
            public decimal AvailableToApply { get; set; }
            public List<AvailableCreditRow> OnAccountPayments { get; set; }   // first page only
            public int OnAccountPaymentsTotal { get; set; }
            public List<OpenInvoiceRow> OpenInvoices { get; set; }
            public List<BankAccountOption> BankAccounts { get; set; }
            public List<PaymentMethodOption> PaymentMethods { get; set; }
            public List<CurrencyOption> Currencies { get; set; }
            public List<ConversionTypeOption> ConversionTypes { get; set; }
        }

        public class AvailableCreditRow
        {
            public string SourceType { get; set; }   // PAYMENT | CREDITNOTE
            public int Id { get; set; }
            public string DocTypeName { get; set; }
            public string DocumentNo { get; set; }
            public DateTime? Date { get; set; }
            public DateTime? DateAcct { get; set; }
            public decimal AvailableAmount { get; set; }
            /// <summary>Available amount expressed in the invoice currency.</summary>
            public decimal AvailableAmountInv { get; set; }
            public int AD_Org_ID { get; set; }
            public int C_Currency_ID { get; set; }
            public int C_ConversionType_ID { get; set; }
            public string CurSymbol { get; set; }
            public string ISO_Code { get; set; }
            public int StdPrecision { get; set; }
        }

        public class OpenInvoiceRow
        {
            public int C_Invoice_ID { get; set; }
            public string DocumentNo { get; set; }
            public DateTime? DueDate { get; set; }
            public decimal OpenAmount { get; set; }
        }

        public class BankAccountOption
        {
            public int C_BankAccount_ID { get; set; }
            public string BankName { get; set; }
            public string AccountNo { get; set; }
            public int C_Currency_ID { get; set; }
            public string CurrencyISO { get; set; }
            public string CurSymbol { get; set; }
            public int StdPrecision { get; set; }
            public bool IsDefault { get; set; }
        }

        public class PaymentMethodOption
        {
            public int VA009_PaymentMethod_ID { get; set; }
            public string Name { get; set; }
            public string BaseType { get; set; }
        }

        public class CurrencyOption
        {
            public int C_Currency_ID { get; set; }
            public string ISO_Code { get; set; }
            public string CurSymbol { get; set; }
            public int StdPrecision { get; set; }
        }

        public class ConversionTypeOption
        {
            public int C_ConversionType_ID { get; set; }
            public string Name { get; set; }
            public bool IsDefault { get; set; }
        }

        public class ConvertAmountRequest
        {
            public int C_Invoice_ID { get; set; }
            public int C_Currency_ID { get; set; }
            public int C_ConversionType_ID { get; set; }
            public decimal Amount { get; set; }
            public DateTime? Date { get; set; }
        }

        public class ConvertAmountResult
        {
            public bool Success { get; set; }
            public string Message { get; set; }
            public int C_Currency_ID { get; set; }
            public string CurSymbol { get; set; }
            public string ISO_Code { get; set; }
            public int StdPrecision { get; set; }
            public decimal Rate { get; set; }
            public decimal ConvertedAmount { get; set; }
            public bool IsSameCurrency { get; set; }
        }

        public class AllocationSource
        {
            public string SourceType { get; set; }
            public int Id { get; set; }
            public decimal Amount { get; set; }
        }

        public class ApplyCreditsRequest
        {
            public int C_Invoice_ID { get; set; }
            public List<AllocationSource> Sources { get; set; }
        }

        public class AllocateCreditNoteRequest
        {
            public int C_Invoice_ID { get; set; }
            public List<int> C_Invoice_IDs { get; set; }
            /// <summary>Cash discount written off the selected invoices, in the
            /// credit-note currency. Settles alongside the credit; it does not
            /// consume the credit note.</summary>
            public decimal DiscountAmt { get; set; }
        }

        public class AllocationResult
        {
            public bool Success { get; set; }
            public string Message { get; set; }
            public string DocumentNo { get; set; }
            public decimal AppliedAmount { get; set; }
            public decimal RemainingAmount { get; set; }
        }

        public class RecordPaymentRequest
        {
            public int C_Invoice_ID { get; set; }
            public decimal PayAmt { get; set; }
            public int C_Currency_ID { get; set; }
            public int C_ConversionType_ID { get; set; }
            public int C_BankAccount_ID { get; set; }
            public int VA009_PaymentMethod_ID { get; set; }
            public DateTime? DateTrx { get; set; }
            public decimal DiscountAmt { get; set; }
            public string ReferenceNo { get; set; }
            public DateTime? CheckDate { get; set; }
        }

        public class RecordPaymentResult
        {
            public bool Success { get; set; }
            public string Message { get; set; }
            public int C_Payment_ID { get; set; }
            public string DocumentNo { get; set; }
            public decimal PayAmt { get; set; }
        }

        #endregion
    }
}
