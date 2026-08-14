using Newtonsoft.Json;
using System;
using System.Collections.Generic;
using System.Data;
using System.Data.SqlClient;
using System.Globalization;
using System.Web.Mvc;
using VAdvantage.Classes;
using VAdvantage.DataBase;
using VAdvantage.Model;
using VAdvantage.Utility;
using VIS.Filters;

namespace VIS.Controllers
{
    /// <summary>
    /// Type-ahead search for AR receipts. Mirrors the structure of
    /// InvoicesController.SearchInvoices but targets C_Payment with
    /// IsReceipt = 'Y' and returns the fields VAS_ReceiptSearch needs
    /// to render its suggestion list (customer, amount, status, reconciled,
    /// allocated, bank, bank account, currency, linked invoice document no).
    /// </summary>
    public class ReceiptsController : Controller
    {
        /// <summary>
        /// Parses an ISO (yyyy-MM-dd) date sent by the widget's date-range inputs. Returns null for
        /// an empty / malformed value, so a bad filter simply does not narrow the search rather than
        /// failing it. Invariant culture - the HTML date input always posts ISO.
        /// </summary>
        private static DateTime? ParseIsoDate(string value)
        {
            DateTime parsed;
            if (!string.IsNullOrEmpty(value) &&
                DateTime.TryParseExact(value.Trim(), "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out parsed))
            {
                return parsed.Date;
            }
            return null;
        }

        /// <summary>
        /// Parses an amount bound from the widget's amount range. The client reads those through the
        /// framework VAmountTextBox, whose getValue() already resolves the user's decimal separator
        /// and returns a plain number - so the wire format is always invariant ("1234.5").
        /// </summary>
        private static decimal? ParseAmount(string value)
        {
            decimal parsed;
            if (!string.IsNullOrEmpty(value) &&
                decimal.TryParse(value.Trim(), NumberStyles.Any, CultureInfo.InvariantCulture, out parsed))
            {
                return parsed;
            }
            return null;
        }

        /// <summary>
        /// Free-text receipt search, optionally NARROWED by the widget's funnel filters:
        ///   acctFrom/acctTo - Account Date  (C_Payment.DateAcct)
        ///   bankAccountId   - Bank Account  (C_Payment.C_BankAccount_ID)
        ///   amtFrom/amtTo   - amount band on ABS(PayAmt), as the free-text amount match uses
        ///   currencyId      - C_Payment.C_Currency_ID
        /// A filter on its own is a valid search: q may be empty when at least one filter is set.
        /// </summary>
        [AjaxAuthorizeAttribute]
        [AjaxSessionFilterAttribute]
        public JsonResult SearchReceipts(string q, int max = 10, int page = 1,
            string acctFrom = null, string acctTo = null, int bankAccountId = 0,
            string amtFrom = null, string amtTo = null, int currencyId = 0)
        {
            if (Session["ctx"] == null)
            {
                return Json(new { error = Msg.GetMsg(Env.GetCtx(), "SessionExpired") ?? "Session Expired" }, JsonRequestBehavior.AllowGet);
            }

            Ctx ctx = Session["ctx"] as Ctx;
            int clientId = ctx.GetAD_Client_ID();

            /* Filters. The upper date bound becomes the exclusive start of the following day, so
               the range stays inclusive of that whole day even when the column carries a time. */
            DateTime? acctFromDate = ParseIsoDate(acctFrom);
            DateTime? acctToDate   = ParseIsoDate(acctTo);
            decimal? amtFromValue  = ParseAmount(amtFrom);
            decimal? amtToValue    = ParseAmount(amtTo);
            if (currencyId < 0) { currencyId = 0; }
            if (bankAccountId < 0) { bankAccountId = 0; }
            /* 0 in BOTH amount bounds means "no amount filter", not "receipts of exactly zero" -
               otherwise a zeroed-out pair would silently return nothing. */
            if (amtFromValue.HasValue && amtToValue.HasValue && amtFromValue.Value == 0m && amtToValue.Value == 0m)
            {
                amtFromValue = null;
                amtToValue   = null;
            }
            bool hasFilter = acctFromDate.HasValue || acctToDate.HasValue || bankAccountId > 0
                             || amtFromValue.HasValue || amtToValue.HasValue || currencyId > 0;

            q = (q ?? "").Trim();
            /* A filter on its own IS a search; only a bare empty term with no filter is a no-op. */
            bool hasTerm = q.Length > 0;
            if (!hasTerm && !hasFilter)
            {
                return Json(JsonConvert.SerializeObject(new { rows = new List<object>() }), JsonRequestBehavior.AllowGet);
            }
            if (max <= 0 || max > 25)
            {
                max = 25;
            }
            /* 1-based page for scroll paging (25 rows per page). */
            if (page < 1)
            {
                page = 1;
            }

            /* All user input goes through SqlParameter; only fixed status-code
               literals are inlined. Oracle binds positionally, so each LIKE
               clause has its own placeholder fed with the same value, and
               @Max (the FETCH bind) is added last after the SQL is assembled. */
            List<SqlParameter> parameters = new List<SqlParameter>();
            List<string> ors = new List<string>();
            /* Term predicates are built ONLY when there is a term: on a filter-only search the
               whole OR block is dropped from the SQL, so binding @Like* / @Amt would leave
               parameters with no placeholder - fatal under Oracle's positional binding. */
            if (hasTerm)
            {
                BuildTermPredicates(q, parameters, ors);
            }

            string termSql = ors.Count > 0 ? "\n                  AND (" + string.Join(" OR ", ors) + ")" : "";

            /* Filters AND onto the term match (or stand alone when there is none). Date bounds are
               half-open (>= from, < to+1day): no TRUNC / date_trunc, so the same SQL runs on
               Oracle and PostgreSQL. The parameters follow the placeholder order below - and all
               of them precede @Offset / @Max, which are added last. */
            string filterSql = "";
            if (acctFromDate.HasValue)
            {
                filterSql += "\n                  AND Payment.DateAcct >= @AcctFrom";
                parameters.Add(new SqlParameter("@AcctFrom", SqlDbType.DateTime) { Value = acctFromDate.Value });
            }
            if (acctToDate.HasValue)
            {
                filterSql += "\n                  AND Payment.DateAcct < @AcctToExcl";
                parameters.Add(new SqlParameter("@AcctToExcl", SqlDbType.DateTime) { Value = acctToDate.Value.AddDays(1) });
            }
            if (bankAccountId > 0)
            {
                filterSql += "\n                  AND Payment.C_BankAccount_ID = @BankAccountId";
                parameters.Add(new SqlParameter("@BankAccountId", bankAccountId));
            }
            /* Band on ABS(PayAmt) - the receipt's own currency, exactly like the free-text amount
               match and the amount shown in the row. */
            if (amtFromValue.HasValue)
            {
                filterSql += "\n                  AND ABS(Payment.PayAmt) >= @AmtFrom";
                parameters.Add(new SqlParameter("@AmtFrom", Math.Abs(amtFromValue.Value)));
            }
            if (amtToValue.HasValue)
            {
                filterSql += "\n                  AND ABS(Payment.PayAmt) <= @AmtTo";
                parameters.Add(new SqlParameter("@AmtTo", Math.Abs(amtToValue.Value)));
            }
            if (currencyId > 0)
            {
                filterSql += "\n                  AND Payment.C_Currency_ID = @CurrencyId";
                parameters.Add(new SqlParameter("@CurrencyId", currencyId));
            }
            return SearchReceiptsCore(ctx, clientId, max, page, termSql + filterSql, parameters);
        }

        /// <summary>
        /// The free-text half of the WHERE: document no, customer, bank, bank account, currency,
        /// linked invoice (direct and via C_PaymentAllocate), document-status / reconciled /
        /// allocated keywords, and an exact amount when the term parses as a number. Each
        /// predicate is OR'd - they ADD hits rather than narrowing them. Parameters are appended
        /// in placeholder order (Oracle binds by position).
        /// </summary>
        private static void BuildTermPredicates(string q, List<SqlParameter> parameters, List<string> ors)
        {
            string likeVal = "%" + q.ToUpper() + "%";

            parameters.Add(new SqlParameter("@LikeDocNo", likeVal));
            ors.Add("UPPER(Payment.DocumentNo) LIKE @LikeDocNo");

            parameters.Add(new SqlParameter("@LikeCust", likeVal));
            ors.Add("UPPER(BPartner.Name) LIKE @LikeCust");

            parameters.Add(new SqlParameter("@LikeBank", likeVal));
            ors.Add("UPPER(Bank.Name) LIKE @LikeBank");

            parameters.Add(new SqlParameter("@LikeAcct", likeVal));
            ors.Add("UPPER(BankAccount.AccountNo) LIKE @LikeAcct");

            parameters.Add(new SqlParameter("@LikeCur", likeVal));
            ors.Add("UPPER(Currency.ISO_Code) LIKE @LikeCur");

            parameters.Add(new SqlParameter("@LikeInv", likeVal));
            ors.Add("UPPER(COALESCE(Invoice.DocumentNo, N'')) LIKE @LikeInv");

            /* Some receipts are not linked through C_Payment.C_Invoice_ID — the
               invoice is held in C_PaymentAllocate (one payment can settle many
               invoices). Search those allocations with an EXISTS so we don't
               multiply rows by joining the table directly. */
            parameters.Add(new SqlParameter("@LikeInvAlloc", likeVal));
            ors.Add(@"EXISTS (
                          SELECT 1
                          FROM C_PaymentAllocate PaymentAllocate2
                          INNER JOIN C_Invoice AllocInvoice ON (PaymentAllocate2.C_Invoice_ID=AllocInvoice.C_Invoice_ID)
                          WHERE PaymentAllocate2.C_Payment_ID = Payment.C_Payment_ID
                            AND PaymentAllocate2.IsActive = 'Y'
                            AND AllocInvoice.IsActive = 'Y'
                            AND UPPER(AllocInvoice.DocumentNo) LIKE @LikeInvAlloc
                      )");

            /* DocStatus keyword mapping — codes are constants, safe to inline. */
            string ql = q.ToLower();
            List<string> codes = new List<string>();
            if (ql.Contains("draft")) { codes.Add("'DR'"); }
            if (ql.Contains("progress")) { codes.Add("'IP'"); }
            if (ql.Contains("complete")) { codes.Add("'CO'"); }
            if (ql.Contains("reverse")) { codes.Add("'RE'"); codes.Add("'VO'"); }
            if (ql.Contains("close")) { codes.Add("'CL'"); }
            if (ql.Contains("approv")) { codes.Add("'AP'"); }
            if (ql.Contains("invalid")) { codes.Add("'IN'"); }
            if (ql.Contains("waiting")) { codes.Add("'WP'"); codes.Add("'WC'"); }
            if (codes.Count > 0)
            {
                ors.Add("Payment.DocStatus IN (" + string.Join(", ", codes) + ")");
            }

            /* Reconciled / allocated keywords — check 'un*' variants first. */
            if (ql.Contains("unreconciled") || ql.Contains("not reconciled"))
            {
                ors.Add("Payment.IsReconciled = 'N'");
            }
            else if (ql.Contains("reconciled"))
            {
                ors.Add("Payment.IsReconciled = 'Y'");
            }

            if (ql.Contains("unallocated") || ql.Contains("not allocated"))
            {
                ors.Add("Payment.IsAllocated = 'N'");
            }
            else if (ql.Contains("allocated"))
            {
                ors.Add("Payment.IsAllocated = 'Y'");
            }

            /* Amount match when the term parses as a number (commas/symbols stripped).
               Sign-agnostic: compare on absolute values so a receipt of +500 or -500
               both match whether the user types "500" or "-500". */
            decimal amt;
            if (decimal.TryParse(q.Replace(",", "").Replace("$", "").Trim(), out amt))
            {
                parameters.Add(new SqlParameter("@Amt", Math.Abs(amt)));
                ors.Add("(ABS(Payment.PayAmt) = @Amt OR ABS(Payment.PaymentAmount) = @Amt)");
            }
        }

        /// <summary>
        /// Runs the assembled search. <paramref name="whereSql"/> is the already-parameterised
        /// term + filter tail appended to the inner query's WHERE; <paramref name="parameters"/>
        /// carries its binds in placeholder order (@Offset / @Max are added here, last).
        /// </summary>
        private JsonResult SearchReceiptsCore(Ctx ctx, int clientId, int max, int page,
            string whereSql, List<SqlParameter> parameters)
        {
            /* Inner query carries no nested C_Invoice subquery in its SELECT, so
               MRole.AddAccessSQL sees only the main physical FROM (C_Payment /
               alias Payment plus its joined lookup tables). The
               allocated-invoice document-number fallback is resolved by the
               OUTER wrapper below, AFTER MRole has been applied — otherwise
               MRole picks up the inner C_Invoice alias and appends an access
               predicate referring to ALLOCINVOICEDISPLAY.C_INVOICE_ID to the
               outer WHERE, where that alias is not in scope. */
            string innerSelectSql = @"
                SELECT Payment.C_Payment_ID AS Payment_ID,
                       Payment.DocumentNo AS Document_No,
                       BPartner.Name AS Customer_Name,
                       Payment.PaymentAmount AS Pay_Amount,
                       Payment.DocStatus AS Doc_Status,
                       Payment.IsReconciled AS Is_Reconciled,
                       Payment.IsAllocated AS Is_Allocated,
                       Bank.Name AS Bank_Name,
                       BankAccount.AccountNo AS Account_No,
                       Currency.ISO_Code AS Currency_ISO,
                       CASE WHEN Currency.CurSymbol IS NOT NULL THEN Currency.CurSymbol ELSE Currency.ISO_Code END AS Cur_Symbol,
                       Invoice.DocumentNo AS Direct_Invoice_Document_No,
                       Payment.DateAcct AS Date_Acct
                FROM C_Payment Payment
                INNER JOIN C_Currency Currency ON (Payment.C_Currency_ID=Currency.C_Currency_ID)
                INNER JOIN C_BankAccount BankAccount ON (Payment.C_BankAccount_ID=BankAccount.C_BankAccount_ID)
                INNER JOIN C_Bank Bank ON (BankAccount.C_Bank_ID=Bank.C_Bank_ID)
                LEFT OUTER JOIN C_BPartner BPartner ON (Payment.C_BPartner_ID=BPartner.C_BPartner_ID)
                LEFT OUTER JOIN C_Invoice Invoice ON (Payment.C_Invoice_ID=Invoice.C_Invoice_ID)
                WHERE Payment.IsReceipt = 'Y'
                  AND Payment.IsActive = 'Y'
                  AND Payment.AD_Client_ID = " + clientId + whereSql;

            /* MRole only on the main physical table (C_Payment / alias Payment). */
            innerSelectSql = MRole.GetDefault(ctx).AddAccessSQL(
                innerSelectSql,
                "Payment",
                MRole.SQL_FULLYQUALIFIED,
                MRole.SQL_RO
            );

            /* Outer wrapper resolves the allocated-invoice fallback document
               number via a correlated subquery on the inlined-secured rows.
               MRole is not re-applied here; the inner query is already filtered
               and the correlated lookup runs in the user's role context. */
            string sql = @"
                SELECT Receipts.Payment_ID,
                       Receipts.Document_No,
                       Receipts.Customer_Name,
                       Receipts.Pay_Amount,
                       Receipts.Doc_Status,
                       Receipts.Is_Reconciled,
                       Receipts.Is_Allocated,
                       Receipts.Bank_Name,
                       Receipts.Account_No,
                       Receipts.Currency_ISO,
                       Receipts.Cur_Symbol,
                       COALESCE(
                           Receipts.Direct_Invoice_Document_No,
                           (
                               SELECT MIN(AllocInvoiceDisplay.DocumentNo)
                               FROM C_PaymentAllocate PaymentAllocateDisplay
                               INNER JOIN C_Invoice AllocInvoiceDisplay
                                   ON (PaymentAllocateDisplay.C_Invoice_ID=AllocInvoiceDisplay.C_Invoice_ID)
                               WHERE PaymentAllocateDisplay.C_Payment_ID = Receipts.Payment_ID
                                 AND PaymentAllocateDisplay.IsActive = 'Y'
                                 AND AllocInvoiceDisplay.IsActive = 'Y'
                           ),
                           N''
                       ) AS Invoice_Document_No,
                       Receipts.Date_Acct
                FROM (" + innerSelectSql + @") Receipts
                ORDER BY Receipts.Date_Acct DESC, Receipts.Payment_ID DESC
                OFFSET @Offset ROWS FETCH NEXT @Max ROWS ONLY";

            /* Scroll paging: skip the pages already loaded, then fetch ONE row more
               than the page size so we can tell the client whether another page
               exists (hasMore) without a separate COUNT query. @Offset then @Max
               appear last in the SQL — Oracle's positional binding requires them to
               be added last, in that order. */
            parameters.Add(new SqlParameter("@Offset", (page - 1) * max));
            parameters.Add(new SqlParameter("@Max", max + 1));

            List<object> rows = new List<object>();
            IDataReader dr = null;
            try
            {
                dr = DB.ExecuteReader(sql, parameters.ToArray());
                while (dr != null && dr.Read())
                {
                    DateTime? dateAcct = Util.GetValueOfDateTime(dr["Date_Acct"]);

                    rows.Add(new
                    {
                        cPaymentId = Util.GetValueOfInt(dr["Payment_ID"]),
                        documentNo = Util.GetValueOfString(dr["Document_No"]),
                        customerName = Util.GetValueOfString(dr["Customer_Name"]),
                        payAmount = Util.GetValueOfDecimal(dr["Pay_Amount"]),
                        docStatus = Util.GetValueOfString(dr["Doc_Status"]),
                        isReconciled = Util.GetValueOfString(dr["Is_Reconciled"]) == "Y",
                        isAllocated = Util.GetValueOfString(dr["Is_Allocated"]) == "Y",
                        bankName = Util.GetValueOfString(dr["Bank_Name"]),
                        accountNo = Util.GetValueOfString(dr["Account_No"]),
                        currencyIso = Util.GetValueOfString(dr["Currency_ISO"]),
                        curSymbol = Util.GetValueOfString(dr["Cur_Symbol"]),
                        invoiceDocumentNo = Util.GetValueOfString(dr["Invoice_Document_No"]),
                        dateAcct = dateAcct.HasValue
                            ? dateAcct.Value.ToString("MMM dd, yyyy", CultureInfo.InvariantCulture)
                            : ""
                    });
                }

                /* The extra (max+1)th row only signals another page exists; trim it
                   so the client always receives at most a full page of `max` rows. */
                bool hasMore = rows.Count > max;
                if (hasMore)
                {
                    rows.RemoveAt(rows.Count - 1);
                }

                return Json(JsonConvert.SerializeObject(new { rows = rows, page = page, pageSize = max, hasMore = hasMore }), JsonRequestBehavior.AllowGet);
            }
            catch (Exception ex)
            {
                return Json(new { error = ex.Message }, JsonRequestBehavior.AllowGet);
            }
            finally
            {
                if (dr != null)
                {
                    dr.Close();
                    dr.Dispose();
                }
            }
        }
    }
}
