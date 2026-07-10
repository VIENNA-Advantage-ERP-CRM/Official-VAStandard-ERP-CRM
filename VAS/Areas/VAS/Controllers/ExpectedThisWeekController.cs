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
    public class ExpectedThisWeekController : Controller
    {
        /// <summary>
        /// Returns the KPI total for AR invoice schedules due in the next 7 days,
        /// converted to the Accounting Schema (base) currency, plus the base
        /// currency symbol and the count of due schedules.
        /// </summary>
        [AjaxAuthorizeAttribute]
        [AjaxSessionFilterAttribute]
        public JsonResult GetExpectedThisWeek()
        {
            if (Session["ctx"] == null)
            {
                return Json(new
                {
                    error = Msg.GetMsg(Env.GetCtx(), "SessionExpired") ?? "Session Expired"
                }, JsonRequestBehavior.AllowGet);
            }

            Ctx ctx = Session["ctx"] as Ctx;

            DateTime startDate = DateTime.Today;
            DateTime endDate = startDate.AddDays(7);

            string schemaCurrencySql = @"
                SELECT ClientInfo.AD_Client_ID,
                       AcctSchema.C_Currency_ID AS C_Currency_ID,
                       Currency.StdPrecision,
                       CASE WHEN Currency.CurSymbol IS NOT NULL THEN Currency.CurSymbol ELSE Currency.ISO_Code END AS Cur_Symbol
                FROM AD_ClientInfo ClientInfo
                INNER JOIN C_AcctSchema AcctSchema ON (ClientInfo.C_AcctSchema1_ID=AcctSchema.C_AcctSchema_ID)
                INNER JOIN C_Currency Currency ON (AcctSchema.C_Currency_ID=Currency.C_Currency_ID)";

            string expectedSql = @"
                SELECT SchemaCurrency.C_Currency_ID,
                       SchemaCurrency.StdPrecision,
                       SchemaCurrency.Cur_Symbol,
                       SUM(
                           CASE
                               WHEN Invoice.C_Currency_ID = SchemaCurrency.C_Currency_ID
                               THEN CASE WHEN Invoice.IsReturnTrx = 'N' THEN COALESCE(InvoicePaySchedule.DueAmt, 0) ELSE -1 * COALESCE(InvoicePaySchedule.DueAmt, 0) END
                               ELSE CurrencyConvert(
                                   CASE WHEN Invoice.IsReturnTrx = 'N' THEN COALESCE(InvoicePaySchedule.DueAmt, 0) ELSE -1 * COALESCE(InvoicePaySchedule.DueAmt, 0) END,
                                   Invoice.C_Currency_ID,
                                   SchemaCurrency.C_Currency_ID,
                                   Invoice.DateAcct,
                                   Invoice.C_ConversionType_ID,
                                   Invoice.AD_Client_ID,
                                   Invoice.AD_Org_ID
                               )
                           END
                       ) AS Expected_Amount,
                       COUNT(1) AS Schedule_Count
                FROM C_InvoicePaySchedule InvoicePaySchedule
                INNER JOIN C_Invoice Invoice ON (InvoicePaySchedule.C_Invoice_ID=Invoice.C_Invoice_ID)
                INNER JOIN SchemaCurrency SchemaCurrency ON (SchemaCurrency.AD_Client_ID=Invoice.AD_Client_ID)
                WHERE Invoice.IsSoTrx = 'Y'
                  AND Invoice.IsActive = 'Y'
                  AND Invoice.DocStatus IN ('CO', 'CL')
                  AND InvoicePaySchedule.IsActive = 'Y'
                  AND InvoicePaySchedule.VA009_IsPaid = 'N'
                  AND " + TruncColumn("InvoicePaySchedule.DueDate") + @" >= " + ToSqlDate(startDate) + @"
                  AND " + TruncColumn("InvoicePaySchedule.DueDate") + @" < " + ToSqlDate(endDate);

            /* MRole CTE rule: apply access SQL only on the body where the main
               physical table lives (C_Invoice, primary alias `Invoice`). Never
               on the outer combined query nor on the CTE alias SchemaCurrency. */
            expectedSql = MRole.GetDefault(ctx).AddAccessSQL(
                expectedSql,
                "Invoice",
                MRole.SQL_FULLYQUALIFIED,
                MRole.SQL_RO
            );

            expectedSql += @"
                GROUP BY SchemaCurrency.C_Currency_ID,
                         SchemaCurrency.StdPrecision,
                         SchemaCurrency.Cur_Symbol";

            string sql = @"
                WITH SchemaCurrency AS (
                    " + schemaCurrencySql + @"
                ),
                ExpectedData AS (
                    " + expectedSql + @"
                )
                SELECT ExpectedData.C_Currency_ID,
                       ExpectedData.Cur_Symbol,
                       ExpectedData.Schedule_Count,
                       ROUND(
                           COALESCE(ExpectedData.Expected_Amount, 0),
                           ExpectedData.StdPrecision
                       ) AS Expected_Amount
                FROM ExpectedData";

            decimal expectedAmount = 0;
            int currencyId = 0;
            int scheduleCount = 0;
            /* Base-currency symbol (accounting schema currency); the KPI amount
               above is already converted to this currency via CurrencyConvert. */
            string currencySymbol = "";

            IDataReader dr = null;

            try
            {
                dr = DB.ExecuteReader(sql);

                if (dr != null && dr.Read())
                {
                    currencyId = Util.GetValueOfInt(dr["C_Currency_ID"]);
                    expectedAmount = Util.GetValueOfDecimal(dr["Expected_Amount"]);
                    scheduleCount = Util.GetValueOfInt(dr["Schedule_Count"]);
                    currencySymbol = Util.GetValueOfString(dr["Cur_Symbol"]);
                }

                var result = new
                {
                    expectedAmountThisWeek = expectedAmount,
                    cCurrencyId = currencyId,
                    scheduleCount = scheduleCount,
                    symbol = currencySymbol,
                    fromDate = startDate.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture),
                    toDate = endDate.AddDays(-1).ToString("yyyy-MM-dd", CultureInfo.InvariantCulture)
                };

                return Json(JsonConvert.SerializeObject(result), JsonRequestBehavior.AllowGet);
            }
            catch (Exception ex)
            {
                return Json(new
                {
                    error = ex.Message
                }, JsonRequestBehavior.AllowGet);
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

        /// <summary>
        /// Returns one page of the drill-down list of invoices/schedules due in
        /// the next 7 days. Amounts are returned in the *invoice* currency
        /// (no conversion to base) so the modal can show the original number.
        /// Server-side paged via OFFSET/FETCH so very large weeks don't ship
        /// thousands of rows to the browser.
        /// </summary>
        [AjaxAuthorizeAttribute]
        [AjaxSessionFilterAttribute]
        public JsonResult GetExpectedThisWeekRows(int pageNo = 1, int pageSize = 10)
        {
            if (Session["ctx"] == null)
            {
                return Json(new
                {
                    error = Msg.GetMsg(Env.GetCtx(), "SessionExpired") ?? "Session Expired"
                }, JsonRequestBehavior.AllowGet);
            }

            Ctx ctx = Session["ctx"] as Ctx;

            if (pageNo <= 0) { pageNo = 1; }
            if (pageSize <= 0) { pageSize = 10; }

            DateTime startDate = DateTime.Today;
            DateTime endDate = startDate.AddDays(7);

            int offset = (pageNo - 1) * pageSize;

            List<SqlParameter> parameters = new List<SqlParameter>();
            parameters.Add(new SqlParameter("@Offset", offset));
            parameters.Add(new SqlParameter("@PageSize", pageSize));

            string rowsBaseSql = @"
                SELECT Invoice.C_Invoice_ID AS Invoice_ID,
                       Invoice.DocumentNo AS Document_No,
                       Invoice.DateInvoiced AS Invoice_Date,
                       InvoicePaySchedule.DueDate AS Due_Date,
                       CASE WHEN Invoice.IsReturnTrx = 'N' THEN InvoicePaySchedule.DueAmt ELSE -1*InvoicePaySchedule.DueAmt END AS Due_Amount,
                       BPartner.Name AS Customer_Name,
                       Currency.ISO_Code AS Invoice_Currency,
                       CASE WHEN Currency.CurSymbol IS NOT NULL THEN Currency.CurSymbol ELSE Currency.ISO_Code END AS Invoice_Currency_Symbol
                FROM C_InvoicePaySchedule InvoicePaySchedule
                INNER JOIN C_Invoice Invoice ON (InvoicePaySchedule.C_Invoice_ID=Invoice.C_Invoice_ID)
                INNER JOIN C_BPartner BPartner ON (Invoice.C_BPartner_ID=BPartner.C_BPartner_ID)
                INNER JOIN C_Currency Currency ON (Invoice.C_Currency_ID=Currency.C_Currency_ID)
                WHERE Invoice.IsSoTrx = 'Y'
                  AND Invoice.IsActive = 'Y'
                  AND Invoice.DocStatus IN ('CO', 'CL')
                  AND InvoicePaySchedule.IsActive = 'Y'
                  AND InvoicePaySchedule.VA009_IsPaid = 'N'
                  AND " + TruncColumn("InvoicePaySchedule.DueDate") + @" >= " + ToSqlDate(startDate) + @"
                  AND " + TruncColumn("InvoicePaySchedule.DueDate") + @" < " + ToSqlDate(endDate);

            rowsBaseSql = MRole.GetDefault(ctx).AddAccessSQL(
                rowsBaseSql,
                "Invoice",
                MRole.SQL_FULLYQUALIFIED,
                MRole.SQL_RO
            );

            /* CountData CTE produces a single TotalRecords value cross-joined to
               every page row so the JS pager has totalRecords/totalPages without
               a second round-trip. OFFSET/FETCH NEXT is portable across Oracle
               12c+ and PostgreSQL. */
            string sql = @"
                WITH ExpectedRowsData AS (
                    " + rowsBaseSql + @"
                ),
                CountData AS (
                    SELECT COUNT(1) AS TotalRecords
                    FROM ExpectedRowsData
                )
                SELECT ExpectedRowsData.Invoice_ID,
                       ExpectedRowsData.Document_No,
                       ExpectedRowsData.Invoice_Date,
                       ExpectedRowsData.Due_Date,
                       ExpectedRowsData.Due_Amount,
                       ExpectedRowsData.Customer_Name,
                       ExpectedRowsData.Invoice_Currency,
                       ExpectedRowsData.Invoice_Currency_Symbol,
                       CountData.TotalRecords
                FROM ExpectedRowsData
                CROSS JOIN CountData
                ORDER BY ExpectedRowsData.Due_Date ASC, ExpectedRowsData.Document_No ASC
                OFFSET @Offset ROWS FETCH NEXT @PageSize ROWS ONLY";

            List<object> rows = new List<object>();
            int totalRecords = 0;

            IDataReader dr = null;

            try
            {
                dr = DB.ExecuteReader(sql, parameters.ToArray());

                while (dr != null && dr.Read())
                {
                    totalRecords = Util.GetValueOfInt(dr["TotalRecords"]);

                    DateTime? dueDate = Util.GetValueOfDateTime(dr["Due_Date"]);
                    DateTime? invoiceDate = Util.GetValueOfDateTime(dr["Invoice_Date"]);

                    rows.Add(new
                    {
                        invoiceId = Util.GetValueOfInt(dr["Invoice_ID"]),
                        documentNo = Util.GetValueOfString(dr["Document_No"]),
                        invoiceDate = invoiceDate.HasValue
                            ? invoiceDate.Value.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture)
                            : "",
                        dueDate = dueDate.HasValue
                            ? dueDate.Value.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture)
                            : "",
                        amount = Util.GetValueOfDecimal(dr["Due_Amount"]),
                        customer = Util.GetValueOfString(dr["Customer_Name"]),
                        invoiceCurrency = Util.GetValueOfString(dr["Invoice_Currency"]),
                        invoiceCurrencySymbol = Util.GetValueOfString(dr["Invoice_Currency_Symbol"])
                    });
                }

                var result = new
                {
                    rows = rows,
                    pageNo = pageNo,
                    pageSize = pageSize,
                    totalRecords = totalRecords,
                    totalPages = pageSize == 0 ? 0 : Convert.ToInt32(Math.Ceiling((decimal)totalRecords / pageSize)),
                    fromDate = startDate.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture),
                    toDate = endDate.AddDays(-1).ToString("yyyy-MM-dd", CultureInfo.InvariantCulture)
                };

                return Json(JsonConvert.SerializeObject(result), JsonRequestBehavior.AllowGet);
            }
            catch (Exception ex)
            {
                return Json(new
                {
                    error = ex.Message
                }, JsonRequestBehavior.AllowGet);
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

        private static readonly Dictionary<string, (string MessageKey, string DefaultName)> PaymentRuleNames =
             new Dictionary<string, (string MessageKey, string DefaultName)>
             {
                    { "B", ("DirectDebit", "Direct Debit") },
                    { "K", ("Cheque", "Cheque") },
                    { "S", ("Check", "Check") },
                    { "T", ("BankTransfer", "Bank Transfer") },
                    { "P", ("OnCredit", "On Credit") }
             };

        private string GetPaymentRuleName(Ctx ctx, string paymentRule)
        {
            if (string.IsNullOrEmpty(paymentRule))
            {
                return "";
            }

            if (PaymentRuleNames.TryGetValue(paymentRule, out var paymentRuleInfo))
            {
                return GetMsg(ctx, paymentRuleInfo.MessageKey, paymentRuleInfo.DefaultName);
            }

            return paymentRule;
        }

        private string GetMsg(Ctx ctx, string key, string fallback)
        {
            string msg = Msg.GetMsg(ctx, key);
            if (string.IsNullOrEmpty(msg) || (msg.StartsWith("[") && msg.EndsWith("]")))
            {
                return fallback;
            }
            return msg;
        }

        internal static string ToSqlDate(DateTime date)
        {
            DateTime day = date.Date;

            if (DB.IsOracle())
            {
                return "TO_DATE('"
                    + day.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture)
                    + "','YYYY-MM-DD')";
            }

            return DB.TO_DATE(day, true);
        }

        internal static string TruncColumn(string columnExpression)
        {
            if (DB.IsOracle())
            {
                return "TRUNC(" + columnExpression + ")";
            }

            return columnExpression;
        }
    }
}
