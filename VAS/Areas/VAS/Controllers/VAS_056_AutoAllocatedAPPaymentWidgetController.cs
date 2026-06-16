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

namespace VAS.Controllers
{
    public class VAS_056_AutoAllocatedAPPaymentWidgetController : Controller
    {
        [AjaxAuthorizeAttribute]
        [AjaxSessionFilterAttribute]
        public JsonResult GetAutoAllocatedAPPayments()
        {
            if (Session["ctx"] == null)
            {
                return Json(new { error = Msg.GetMsg(Env.GetCtx(), "SessionExpired") ?? "Session Expired" }, JsonRequestBehavior.AllowGet);
            }

            Ctx ctx = Session["ctx"] as Ctx;
            DateTime rangeStart = DateTime.Today.AddDays(-29);
            DateTime rangeEnd = DateTime.Today.AddDays(1);

            string paymentsBaseSql = @"
                SELECT CASE WHEN Payment.IsAllocated = 'Y' THEN 1 ELSE 0 END AS Is_Matched
                FROM C_Payment Payment
                WHERE Payment.IsReceipt = 'N'
                  AND Payment.IsActive = 'Y'
                  AND Payment.DocStatus IN ('CO', 'CL')
                  AND " + TruncColumn("Payment.DateAcct") + @" >= " + ToSqlDate(rangeStart) + @"
                  AND " + TruncColumn("Payment.DateAcct") + @" < " + ToSqlDate(rangeEnd);

            paymentsBaseSql = MRole.GetDefault(ctx).AddAccessSQL(
                paymentsBaseSql,
                "Payment",
                MRole.SQL_FULLYQUALIFIED,
                MRole.SQL_RO
            );

            string sql = @"
                WITH PaymentsData AS (
                    " + paymentsBaseSql + @"
                )
                SELECT COUNT(1) AS TotalPayments,
                       COALESCE(SUM(PaymentsData.Is_Matched), 0) AS MatchedPayments,
                       CASE
                           WHEN COUNT(1) > 0
                           THEN ROUND(COALESCE(SUM(PaymentsData.Is_Matched), 0) * 100.0 / COUNT(1), 2)
                           ELSE 0
                       END AS AutoAllocatedPercent
                FROM PaymentsData";

            IDataReader dr = null;

            try
            {
                int totalPayments = 0;
                int matchedPayments = 0;
                decimal autoAllocatedPercent = 0;

                dr = DB.ExecuteReader(sql);

                if (dr != null && dr.Read())
                {
                    totalPayments = Util.GetValueOfInt(dr["TotalPayments"]);
                    matchedPayments = Util.GetValueOfInt(dr["MatchedPayments"]);
                    autoAllocatedPercent = Util.GetValueOfDecimal(dr["AutoAllocatedPercent"]);
                }

                int unmatchedPayments = totalPayments - matchedPayments;
                if (unmatchedPayments < 0) { unmatchedPayments = 0; }

                var result = new
                {
                    autoAllocatedPercent = autoAllocatedPercent,
                    totalPayments = totalPayments,
                    matchedPayments = matchedPayments,
                    unmatchedPayments = unmatchedPayments,
                    fromDate = rangeStart.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture),
                    toDate = DateTime.Today.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture),
                    period = GetMsg(ctx, "VAS_Last30Days", "Last 30 days")
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

        [AjaxAuthorizeAttribute]
        [AjaxSessionFilterAttribute]
        public JsonResult GetAutoAllocatedAPPaymentRows(int pageNo = 1, int pageSize = 10, string filter = "allocated")
        {
            if (Session["ctx"] == null)
            {
                return Json(new { error = Msg.GetMsg(Env.GetCtx(), "SessionExpired") ?? "Session Expired" }, JsonRequestBehavior.AllowGet);
            }

            Ctx ctx = Session["ctx"] as Ctx;

            if (pageNo <= 0) { pageNo = 1; }
            if (pageSize <= 0) { pageSize = 10; }

            string filterKey = string.IsNullOrEmpty(filter) ? "allocated" : filter.Trim().ToLowerInvariant();
            DateTime rangeStart = DateTime.Today.AddDays(-29);
            DateTime rangeEnd = DateTime.Today.AddDays(1);
            int offset = (pageNo - 1) * pageSize;

            List<SqlParameter> parameters = new List<SqlParameter>();
            parameters.Add(new SqlParameter("@Offset", offset));
            parameters.Add(new SqlParameter("@PageSize", pageSize));

            string filterPredicate = "";
            if (filterKey == "allocated")
            {
                filterPredicate = "\n                  AND Payment.IsAllocated = 'Y'";
            }
            else if (filterKey == "unallocated")
            {
                filterPredicate = "\n                  AND (Payment.IsAllocated IS NULL OR Payment.IsAllocated <> 'Y')";
            }

            string rowsBaseSql = @"
                SELECT Payment.C_Payment_ID AS Payment_ID,
                       Payment.DateAcct AS Date_Acct,
                       Payment.DocumentNo AS Document_No,
                       BPartner.Name AS Vendor_Name,
                       COALESCE(Bank.Name, BankAccount.Name) AS Bank_Name,
                       COALESCE(BankAccount.AccountNo, N'') AS Account_No,
                       Payment.PaymentAmount AS Pay_Amount,
                       Currency.ISO_Code AS Payment_Currency,
                       CASE WHEN Currency.CurSymbol IS NOT NULL THEN Currency.CurSymbol ELSE Currency.ISO_Code END AS Payment_Currency_Symbol,
                       CASE WHEN Payment.IsAllocated = 'Y' THEN 'Y' ELSE 'N' END AS Is_Matched
                FROM C_Payment Payment
                INNER JOIN C_BankAccount BankAccount ON (Payment.C_BankAccount_ID = BankAccount.C_BankAccount_ID)
                INNER JOIN C_Bank Bank ON (BankAccount.C_Bank_ID = Bank.C_Bank_ID)
                INNER JOIN C_Currency Currency ON (Payment.C_Currency_ID = Currency.C_Currency_ID)
                LEFT JOIN C_BPartner BPartner ON (Payment.C_BPartner_ID = BPartner.C_BPartner_ID)
                WHERE Payment.IsReceipt = 'N'
                  AND Payment.IsActive = 'Y'
                  AND Payment.DocStatus IN ('CO', 'CL')
                  AND " + TruncColumn("Payment.DateAcct") + @" >= " + ToSqlDate(rangeStart) + @"
                  AND " + TruncColumn("Payment.DateAcct") + @" < " + ToSqlDate(rangeEnd) +
                  filterPredicate;

            rowsBaseSql = MRole.GetDefault(ctx).AddAccessSQL(
                rowsBaseSql,
                "Payment",
                MRole.SQL_FULLYQUALIFIED,
                MRole.SQL_RO
            );

            string sql = @"
                WITH PaymentsData AS (
                    " + rowsBaseSql + @"
                ),
                CountData AS (
                    SELECT COUNT(1) AS TotalRecords
                    FROM PaymentsData
                )
                SELECT PaymentsData.Payment_ID,
                       PaymentsData.Date_Acct,
                       PaymentsData.Document_No,
                       PaymentsData.Vendor_Name,
                       PaymentsData.Bank_Name,
                       PaymentsData.Account_No,
                       PaymentsData.Pay_Amount,
                       PaymentsData.Payment_Currency,
                       PaymentsData.Payment_Currency_Symbol,
                       PaymentsData.Is_Matched,
                       CountData.TotalRecords
                FROM PaymentsData
                CROSS JOIN CountData
                ORDER BY PaymentsData.Date_Acct DESC, PaymentsData.Document_No DESC
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
                        date = dateAcct.HasValue ? dateAcct.Value.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture) : "",
                        documentNo = Util.GetValueOfString(dr["Document_No"]),
                        vendor = Util.GetValueOfString(dr["Vendor_Name"]),
                        bankName = Util.GetValueOfString(dr["Bank_Name"]),
                        accountNo = Util.GetValueOfString(dr["Account_No"]),
                        amount = Util.GetValueOfDecimal(dr["Pay_Amount"]),
                        paymentCurrency = Util.GetValueOfString(dr["Payment_Currency"]),
                        paymentCurrencySymbol = Util.GetValueOfString(dr["Payment_Currency_Symbol"]),
                        matched = Util.GetValueOfString(dr["Is_Matched"]) == "Y"
                    });
                }

                var result = new
                {
                    rows = rows,
                    pageNo = pageNo,
                    pageSize = pageSize,
                    totalRecords = totalRecords,
                    totalPages = pageSize == 0 ? 0 : Convert.ToInt32(Math.Ceiling((decimal)totalRecords / pageSize)),
                    filter = filterKey,
                    fromDate = rangeStart.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture),
                    toDate = DateTime.Today.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture),
                    period = GetMsg(ctx, "VAS_Last30Days", "Last 30 days")
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

        private static string ToSqlDate(DateTime date)
        {
            DateTime day = date.Date;

            if (DB.IsOracle())
            {
                return "TO_DATE('" + day.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture) + "','YYYY-MM-DD')";
            }

            return DB.TO_DATE(day, true);
        }

        private static string TruncColumn(string columnExpression)
        {
            return DB.IsOracle() ? "TRUNC(" + columnExpression + ")" : columnExpression;
        }

        private string GetMsg(Ctx ctx, string key, string fallback)
        {
            string msg = Msg.GetMsg(ctx, key);
            return !string.IsNullOrEmpty(msg) && msg != "[" + key + "]" ? msg : fallback;
        }
    }
}
