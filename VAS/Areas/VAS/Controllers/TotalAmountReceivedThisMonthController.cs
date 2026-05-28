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
    public class TotalAmountReceivedThisMonthController : Controller
    {
        /// <summary>
        /// Returns total AR receipt amount received in the current month,
        /// converted to Accounting Schema (base) currency, with its symbol
        /// and the count of receipts in the period.
        /// </summary>
        [AjaxAuthorizeAttribute]
        [AjaxSessionFilterAttribute]
        public JsonResult GetAmountReceivedThisMonth()
        {
            if (Session["ctx"] == null)
            {
                return Json(new
                {
                    error = Msg.GetMsg(Env.GetCtx(), "SessionExpired") ?? "Session Expired"
                }, JsonRequestBehavior.AllowGet);
            }

            Ctx ctx = Session["ctx"] as Ctx;

            DateTime monthStart = new DateTime(DateTime.Today.Year, DateTime.Today.Month, 1);
            DateTime nextMonthStart = monthStart.AddMonths(1);

            string schemaCurrencySql = @"
                SELECT ClientInfo.AD_Client_ID,
                       AcctSchema.C_Currency_ID AS C_Currency_ID,
                       Currency.StdPrecision,
                       CASE WHEN Currency.CurSymbol IS NOT NULL THEN Currency.CurSymbol ELSE Currency.ISO_Code END AS Cur_Symbol
                FROM AD_ClientInfo ClientInfo
                INNER JOIN C_AcctSchema AcctSchema ON (ClientInfo.C_AcctSchema1_ID=AcctSchema.C_AcctSchema_ID)
                INNER JOIN C_Currency Currency ON (AcctSchema.C_Currency_ID=Currency.C_Currency_ID)";

            string receivedThisMonthSql = @"
                SELECT SchemaCurrency.C_Currency_ID,
                       SchemaCurrency.StdPrecision,
                       SchemaCurrency.Cur_Symbol,
                       SUM(
                           CASE
                               WHEN Payment.C_Currency_ID = SchemaCurrency.C_Currency_ID
                               THEN COALESCE(Payment.PaymentAmount, 0)
                               ELSE CurrencyConvert(
                                   COALESCE(Payment.PaymentAmount, 0),
                                   Payment.C_Currency_ID,
                                   SchemaCurrency.C_Currency_ID,
                                   Payment.DateAcct,
                                   Payment.C_ConversionType_ID,
                                   Payment.AD_Client_ID,
                                   Payment.AD_Org_ID
                               )
                           END
                       ) AS TotalAmountReceived,
                       COUNT(*) AS Receipt_Count
                FROM C_Payment Payment
                INNER JOIN SchemaCurrency SchemaCurrency ON (SchemaCurrency.AD_Client_ID=Payment.AD_Client_ID)
                WHERE Payment.IsReceipt = 'Y'
                  AND Payment.IsActive = 'Y'
                  AND Payment.DocStatus IN ('CO', 'CL')
                  AND " + TruncColumn("Payment.DateAcct") + @" >= " + ToSqlDate(monthStart) + @"
                  AND " + TruncColumn("Payment.DateAcct") + @" < " + ToSqlDate(nextMonthStart);

            /*
             * MRole CTE rule: apply access SQL only on the CTE body where the main physical
             * table lives (C_Payment, primary alias `Payment`). Never on the outer combined
             * query, never on the CTE alias SchemaCurrency.
             */
            receivedThisMonthSql = MRole.GetDefault(ctx).AddAccessSQL(
                receivedThisMonthSql,
                "Payment",
                MRole.SQL_FULLYQUALIFIED,
                MRole.SQL_RO
            );

            receivedThisMonthSql += @"
                GROUP BY SchemaCurrency.C_Currency_ID,
                         SchemaCurrency.StdPrecision,
                         SchemaCurrency.Cur_Symbol";

            string sql = @"
                WITH SchemaCurrency AS (
                    " + schemaCurrencySql + @"
                ),
                ReceivedThisMonth AS (
                    " + receivedThisMonthSql + @"
                )
                SELECT ReceivedThisMonth.C_Currency_ID,
                       ReceivedThisMonth.Cur_Symbol,
                       ReceivedThisMonth.Receipt_Count,
                       ROUND(
                           COALESCE(ReceivedThisMonth.TotalAmountReceived, 0),
                           ReceivedThisMonth.StdPrecision
                       ) AS TotalAmountReceivedThisMonth
                FROM ReceivedThisMonth";

            decimal totalAmountReceivedThisMonth = 0;
            int currencyId = 0;
            int receiptCount = 0;
            /* Base-currency symbol (accounting schema currency); amount above is already
               converted to this currency by CurrencyConvert. */
            string currencySymbol = "";

            IDataReader dr = null;

            try
            {
                dr = DB.ExecuteReader(sql);

                if (dr != null && dr.Read())
                {
                    currencyId = Util.GetValueOfInt(dr["C_Currency_ID"]);
                    totalAmountReceivedThisMonth = Util.GetValueOfDecimal(dr["TotalAmountReceivedThisMonth"]);
                    receiptCount = Util.GetValueOfInt(dr["Receipt_Count"]);
                    currencySymbol = Util.GetValueOfString(dr["Cur_Symbol"]);
                }

                var result = new
                {
                    cCurrencyId = currencyId,
                    totalAmountReceivedThisMonth = totalAmountReceivedThisMonth,
                    receiptCount = receiptCount,
                    symbol = currencySymbol,
                    period = monthStart.ToString("MMMM yyyy", CultureInfo.InvariantCulture)
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
        /// Returns one page of the drill-down receipt list for the dialog: AR
        /// receipts posted in the current month with Date, Receipt No., Customer,
        /// Bank Name + Account No., Amount, Payment Currency and Allocated.
        /// Server-side paged via OFFSET/FETCH so very large months don't ship
        /// thousands of rows to the browser.
        /// </summary>
        [AjaxAuthorizeAttribute]
        [AjaxSessionFilterAttribute]
        public JsonResult GetReceivedThisMonthRows(int pageNo = 1, int pageSize = 10)
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

            DateTime monthStart = new DateTime(DateTime.Today.Year, DateTime.Today.Month, 1);
            DateTime nextMonthStart = monthStart.AddMonths(1);

            int offset = (pageNo - 1) * pageSize;

            List<SqlParameter> parameters = new List<SqlParameter>();
            parameters.Add(new SqlParameter("@Offset", offset));
            parameters.Add(new SqlParameter("@PageSize", pageSize));

            /* Base receipts SQL — MRole is applied to this CTE body only, so
               the access predicate sits on the physical C_Payment table
               (alias `Payment`). Never on a CTE alias or the outer query. */
            string receiptsBaseSql = @"
                SELECT Payment.C_Payment_ID AS Payment_ID,
                       Payment.DateAcct AS Date_Acct,
                       Payment.DocumentNo AS Document_No,
                       BPartner.Name AS Customer_Name,
                       COALESCE(Bank.Name, BankAccount.Name) AS Bank_Name,
                       COALESCE(BankAccount.AccountNo, '') AS Account_No,
                       Payment.PaymentAmount AS Pay_Amount,
                       Currency.ISO_Code AS Payment_Currency,
                       CASE WHEN Currency.CurSymbol IS NOT NULL THEN Currency.CurSymbol ELSE Currency.ISO_Code END AS Payment_Currency_Symbol,
                       CASE WHEN Payment.IsAllocated = 'Y' THEN 'Y' ELSE 'N' END AS Is_Allocated
                FROM C_Payment Payment
                INNER JOIN C_BPartner BPartner ON (Payment.C_BPartner_ID=BPartner.C_BPartner_ID)
                LEFT OUTER JOIN C_BankAccount BankAccount ON (Payment.C_BankAccount_ID=BankAccount.C_BankAccount_ID)
                LEFT OUTER JOIN C_Bank Bank ON (BankAccount.C_Bank_ID=Bank.C_Bank_ID)
                INNER JOIN C_Currency Currency ON (Payment.C_Currency_ID=Currency.C_Currency_ID)
                WHERE Payment.IsReceipt = 'Y'
                  AND Payment.IsActive = 'Y'
                  AND Payment.DocStatus IN ('CO', 'CL')
                  AND " + TruncColumn("Payment.DateAcct") + @" >= " + ToSqlDate(monthStart) + @"
                  AND " + TruncColumn("Payment.DateAcct") + @" < " + ToSqlDate(nextMonthStart);

            receiptsBaseSql = MRole.GetDefault(ctx).AddAccessSQL(
                receiptsBaseSql,
                "Payment",
                MRole.SQL_FULLYQUALIFIED,
                MRole.SQL_RO
            );

            /* CountData CTE produces a single TotalRecords value that is
               cross-joined into the result so each returned page row carries
               the same totalRecords for the JS pager. OFFSET/FETCH NEXT is
               portable across Oracle 12c+ and PostgreSQL. */
            string sql = @"
                WITH ReceiptsData AS (
                    " + receiptsBaseSql + @"
                ),
                CountData AS (
                    SELECT COUNT(1) AS TotalRecords
                    FROM ReceiptsData
                )
                SELECT ReceiptsData.Payment_ID,
                       ReceiptsData.Date_Acct,
                       ReceiptsData.Document_No,
                       ReceiptsData.Customer_Name,
                       ReceiptsData.Bank_Name,
                       ReceiptsData.Account_No,
                       ReceiptsData.Pay_Amount,
                       ReceiptsData.Payment_Currency,
                       ReceiptsData.Payment_Currency_Symbol,
                       ReceiptsData.Is_Allocated,
                       CountData.TotalRecords
                FROM ReceiptsData
                CROSS JOIN CountData
                ORDER BY ReceiptsData.Date_Acct DESC, ReceiptsData.Document_No DESC
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

                    DateTime? dateAcct = Util.GetValueOfDateTime(dr["Date_Acct"]);

                    rows.Add(new
                    {
                        paymentId = Util.GetValueOfInt(dr["Payment_ID"]),
                        date = dateAcct.HasValue
                            ? dateAcct.Value.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture)
                            : "",
                        documentNo = Util.GetValueOfString(dr["Document_No"]),
                        customer = Util.GetValueOfString(dr["Customer_Name"]),
                        bankName = Util.GetValueOfString(dr["Bank_Name"]),
                        accountNo = Util.GetValueOfString(dr["Account_No"]),
                        amount = Util.GetValueOfDecimal(dr["Pay_Amount"]),
                        paymentCurrency = Util.GetValueOfString(dr["Payment_Currency"]),
                        paymentCurrencySymbol = Util.GetValueOfString(dr["Payment_Currency_Symbol"]),
                        allocated = Util.GetValueOfString(dr["Is_Allocated"]) == "Y"
                    });
                }

                var result = new
                {
                    rows = rows,
                    pageNo = pageNo,
                    pageSize = pageSize,
                    totalRecords = totalRecords,
                    totalPages = pageSize == 0 ? 0 : Convert.ToInt32(Math.Ceiling((decimal)totalRecords / pageSize)),
                    period = monthStart.ToString("MMMM yyyy", CultureInfo.InvariantCulture)
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
