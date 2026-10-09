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
    /// Module Name : Today's receipts (AR Receipt widget, branch 45)
    /// Purpose     : KPI = total AR receipt amount (PaymentAmount) recorded TODAY,
    ///               converted to the accounting-schema (base) currency, plus the
    ///               receipt count and distinct customer count. Drill-down lists
    ///               each receipt (Date, Receipt No., Customer, Bank account,
    ///               Payment Currency, Amount) in its own document currency.
    ///               SQL source: TodayCollection.txt (KPI + Modal).
    /// Chronological development:
    ///   VIS_045     2026-06-02 Created
    /// </summary>
    public class VAS_012_TodayReceiptsController : Controller
    {
        /// <summary>
        /// KPI tile: today's total receipt amount in base currency, with the
        /// receipt count and distinct customer count. MRole is applied only to
        /// the CTE body that holds the physical C_Payment table (alias Payment),
        /// never to the CTE alias SchemaCurrency nor to the outer query.
        /// </summary>
        /// <returns>JSON { cCurrencyId, amount, receiptCount, customerCount, symbol }.</returns>
        [AjaxAuthorizeAttribute]
        [AjaxSessionFilterAttribute]
        public JsonResult GetTodayReceipts()
        {
            if (Session["ctx"] == null)
            {
                return Json(new
                {
                    error = Msg.GetMsg(Env.GetCtx(), "SessionExpired") ?? "Session Expired"
                }, JsonRequestBehavior.AllowGet);
            }

            Ctx ctx = Session["ctx"] as Ctx;

            DateTime dayStart = DateTime.Today;
            DateTime nextDayStart = dayStart.AddDays(1);

            string schemaCurrencySql = @"
                SELECT ClientInfo.AD_Client_ID,
                       AcctSchema.C_Currency_ID AS C_Currency_ID,
                       Currency.StdPrecision,
                       Currency.ISO_Code AS ISO_Code,
                       CASE WHEN Currency.CurSymbol IS NOT NULL THEN Currency.CurSymbol ELSE Currency.ISO_Code END AS Cur_Symbol
                FROM AD_ClientInfo ClientInfo
                INNER JOIN C_AcctSchema AcctSchema ON (ClientInfo.C_AcctSchema1_ID=AcctSchema.C_AcctSchema_ID)
                INNER JOIN C_Currency Currency ON (AcctSchema.C_Currency_ID=Currency.C_Currency_ID)
                WHERE ClientInfo.AD_Client_ID=" + ctx.GetAD_Client_ID();

            /* Today's receipts. Amount uses PaymentAmount (net of withholding) per the
               supplied source query, converted to the schema currency when the
               receipt is in another currency. Aggregated with NO GROUP BY so it
               always yields exactly one row (SUM/COUNT = 0 when nothing matches);
               CROSS JOINing it with the single-row SchemaCurrency CTE below then
               always carries the base-currency symbol/ISO/precision — even at zero
               receipts (mirrors OverdueController). */
            string todayReceiptsSql = @"
                SELECT COALESCE(SUM(
                           CASE
                               WHEN Payment.C_Currency_ID = SchemaCurrency.C_Currency_ID
                               THEN COALESCE(Payment.PaymentAmount, 0)
                               ELSE CurrencyConvert(
                                   COALESCE(Payment.PaymentAmount, 0),
                                   Payment.C_Currency_ID,
                                   SchemaCurrency.C_Currency_ID,
                                   Payment.DateTrx,
                                   Payment.C_ConversionType_ID,
                                   Payment.AD_Client_ID,
                                   Payment.AD_Org_ID
                               )
                           END
                       ), 0) AS TotalAmountReceived,
                       COUNT(Payment.C_Payment_ID) AS Receipt_Count,
                       COUNT(DISTINCT Payment.C_BPartner_ID) AS Customer_Count
                FROM C_Payment Payment
                INNER JOIN SchemaCurrency SchemaCurrency ON (SchemaCurrency.AD_Client_ID=Payment.AD_Client_ID)
                WHERE Payment.IsReceipt = 'Y'
                  AND Payment.IsActive = 'Y'
                  AND Payment.DocStatus IN ('CO', 'CL')
                  AND " + TruncColumn("Payment.DateTrx") + @" >= " + ToSqlDate(dayStart) + @"
                  AND " + TruncColumn("Payment.DateTrx") + @" < " + ToSqlDate(nextDayStart);

            todayReceiptsSql = MRole.GetDefault(ctx).AddAccessSQL(
                todayReceiptsSql,
                "Payment",
                MRole.SQL_FULLYQUALIFIED,
                MRole.SQL_RO
            );

            string sql = @"
                WITH SchemaCurrency AS (
                    " + schemaCurrencySql + @"
                ),
                TodayReceipts AS (
                    " + todayReceiptsSql + @"
                )
                SELECT SchemaCurrency.C_Currency_ID,
                       SchemaCurrency.Cur_Symbol,
                       SchemaCurrency.ISO_Code,
                       SchemaCurrency.StdPrecision,
                       TodayReceipts.Receipt_Count,
                       TodayReceipts.Customer_Count,
                       ROUND(
                           COALESCE(TodayReceipts.TotalAmountReceived, 0),
                           SchemaCurrency.StdPrecision
                       ) AS TodayReceiptAmount
                FROM SchemaCurrency
                CROSS JOIN TodayReceipts";

            decimal amount = 0;
            int currencyId = 0;
            int receiptCount = 0;
            int customerCount = 0;
            /* Base-currency symbol/ISO/precision (accounting schema); the amount
               above is already converted to this currency by CurrencyConvert. The
               CROSS JOIN with the single-row SchemaCurrency CTE keeps these
               populated even when there are no receipts today. */
            string currencySymbol = "";
            string isoCode = "";
            int stdPrecision = 2;

            IDataReader dr = null;

            try
            {
                dr = DB.ExecuteReader(sql);

                if (dr != null && dr.Read())
                {
                    currencyId = Util.GetValueOfInt(dr["C_Currency_ID"]);
                    amount = Util.GetValueOfDecimal(dr["TodayReceiptAmount"]);
                    receiptCount = Util.GetValueOfInt(dr["Receipt_Count"]);
                    customerCount = Util.GetValueOfInt(dr["Customer_Count"]);
                    currencySymbol = Util.GetValueOfString(dr["Cur_Symbol"]);
                    isoCode = Util.GetValueOfString(dr["ISO_Code"]);
                    if (dr["StdPrecision"] != null && dr["StdPrecision"] != System.DBNull.Value)
                    {
                        stdPrecision = Util.GetValueOfInt(dr["StdPrecision"]);
                    }
                }

                var result = new
                {
                    cCurrencyId = currencyId,
                    amount = amount,
                    receiptCount = receiptCount,
                    customerCount = customerCount,
                    symbol = currencySymbol,
                    isoCode = isoCode,
                    stdPrecision = stdPrecision
                };

                return Json(JsonConvert.SerializeObject(result), JsonRequestBehavior.AllowGet);
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

        /// <summary>
        /// One page of the drill-down list: today's receipts with Date, Receipt
        /// No., Customer, Bank account, Payment Currency and Amount. The per-row
        /// amount stays in the receipt's OWN (document) currency with its symbol.
        /// Server-side paged via OFFSET/FETCH (portable across Oracle 12c+ and
        /// PostgreSQL). MRole is applied to the ReceiptsData CTE body only.
        /// </summary>
        /// <param name="pageNo">1-based page number.</param>
        /// <param name="pageSize">Rows per page (default 10).</param>
        /// <returns>JSON { rows[], pageNo, pageSize, totalRecords, totalPages }.</returns>
        [AjaxAuthorizeAttribute]
        [AjaxSessionFilterAttribute]
        public JsonResult GetTodayReceiptRows(int pageNo = 1, int pageSize = 10)
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

            DateTime dayStart = DateTime.Today;
            DateTime nextDayStart = dayStart.AddDays(1);

            int offset = (pageNo - 1) * pageSize;

            string receiptsBaseSql = @"
                SELECT Payment.C_Payment_ID AS Payment_ID,
                       Payment.DateTrx AS Trx_Date,
                       Payment.DocumentNo AS Document_No,
                       COALESCE(BPartner.Name, N'') AS Customer_Name,
                       COALESCE(Bank.Name, BankAccount.Name) AS Bank_Name,
                       BankAccount.AccountNo AS Account_No,
                       Payment.PaymentAmount AS Pay_Amount,
                       Currency.ISO_Code AS Payment_Currency,
                       CASE WHEN Currency.CurSymbol IS NOT NULL THEN Currency.CurSymbol ELSE Currency.ISO_Code END AS Payment_Currency_Symbol,
                       Currency.StdPrecision AS Std_Precision
                FROM C_Payment Payment
                LEFT JOIN C_BPartner BPartner ON (BPartner.C_BPartner_ID=Payment.C_BPartner_ID)
                INNER JOIN C_BankAccount BankAccount ON (BankAccount.C_BankAccount_ID=Payment.C_BankAccount_ID)
                INNER JOIN C_Bank Bank ON (Bank.C_Bank_ID=BankAccount.C_Bank_ID)
                INNER JOIN C_Currency Currency ON (Currency.C_Currency_ID=Payment.C_Currency_ID)
                WHERE Payment.IsReceipt = 'Y'
                  AND Payment.IsActive = 'Y'
                  AND Payment.DocStatus IN ('CO', 'CL')
                  AND " + TruncColumn("Payment.DateTrx") + @" >= " + ToSqlDate(dayStart) + @"
                  AND " + TruncColumn("Payment.DateTrx") + @" < " + ToSqlDate(nextDayStart);

            receiptsBaseSql = MRole.GetDefault(ctx).AddAccessSQL(
                receiptsBaseSql,
                "Payment",
                MRole.SQL_FULLYQUALIFIED,
                MRole.SQL_RO
            );

            string sql = @"
                WITH ReceiptsData AS (
                    " + receiptsBaseSql + @"
                ),
                CountData AS (
                    SELECT COUNT(1) AS TotalRecords
                    FROM ReceiptsData
                )
                SELECT ReceiptsData.Payment_ID,
                       ReceiptsData.Trx_Date,
                       ReceiptsData.Document_No,
                       ReceiptsData.Customer_Name,
                       ReceiptsData.Bank_Name,
                       ReceiptsData.Account_No,
                       ReceiptsData.Pay_Amount,
                       ReceiptsData.Payment_Currency,
                       ReceiptsData.Payment_Currency_Symbol,
                       ReceiptsData.Std_Precision,
                       CountData.TotalRecords
                FROM ReceiptsData
                CROSS JOIN CountData
                ORDER BY ReceiptsData.Trx_Date DESC, ReceiptsData.Document_No DESC
                OFFSET @Offset ROWS FETCH NEXT @PageSize ROWS ONLY";

            List<SqlParameter> parameters = new List<SqlParameter>();
            parameters.Add(new SqlParameter("@Offset", offset));
            parameters.Add(new SqlParameter("@PageSize", pageSize));

            List<object> rows = new List<object>();
            int totalRecords = 0;

            IDataReader dr = null;

            try
            {
                dr = DB.ExecuteReader(sql, parameters.ToArray());

                while (dr != null && dr.Read())
                {
                    totalRecords = Util.GetValueOfInt(dr["TotalRecords"]);

                    DateTime? trxDate = Util.GetValueOfDateTime(dr["Trx_Date"]);
                    int rowPrecision = Util.GetValueOfInt(dr["Std_Precision"]);

                    decimal amount = Math.Round(
                        Util.GetValueOfDecimal(dr["Pay_Amount"]),
                        rowPrecision,
                        MidpointRounding.AwayFromZero
                    );

                    rows.Add(new
                    {
                        paymentId = Util.GetValueOfInt(dr["Payment_ID"]),
                        date = trxDate.HasValue
                            ? trxDate.Value.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture)
                            : "",
                        documentNo = Util.GetValueOfString(dr["Document_No"]),
                        customer = Util.GetValueOfString(dr["Customer_Name"]),
                        bankName = Util.GetValueOfString(dr["Bank_Name"]),
                        accountNo = Util.GetValueOfString(dr["Account_No"]),
                        amount = amount,
                        currencyIso = Util.GetValueOfString(dr["Payment_Currency"]),
                        curSymbol = Util.GetValueOfString(dr["Payment_Currency_Symbol"]),
                        stdPrecision = rowPrecision
                    });
                }

                var result = new
                {
                    rows = rows,
                    pageNo = pageNo,
                    pageSize = pageSize,
                    totalRecords = totalRecords,
                    totalPages = pageSize == 0 ? 0 : Convert.ToInt32(Math.Ceiling((decimal)totalRecords / pageSize))
                };

                return Json(JsonConvert.SerializeObject(result), JsonRequestBehavior.AllowGet);
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

        /// <summary>
        /// Renders a date literal for the active database dialect.
        /// </summary>
        /// <param name="date">Date to render (time part ignored).</param>
        /// <returns>Dialect-specific date literal string.</returns>
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

        /// <summary>
        /// Wraps a date column in TRUNC on Oracle so the day comparison ignores
        /// the time component; other dialects compare the column directly.
        /// </summary>
        /// <param name="columnExpression">Fully-qualified date column.</param>
        /// <returns>Column expression, TRUNC-wrapped on Oracle.</returns>
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
