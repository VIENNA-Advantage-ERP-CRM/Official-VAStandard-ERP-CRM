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
    public class AutoAllocatedController : Controller
    {
        /// <summary>
        /// Returns the percentage of AR receipts in the last 30 days
        /// (including today) that are allocated, plus total/matched counts
        /// so the KPI card can show both ratio and absolute numbers.
        /// </summary>
        [AjaxAuthorizeAttribute]
        [AjaxSessionFilterAttribute]
        public JsonResult GetAutoAllocated()
        {
            if (Session["ctx"] == null)
            {
                return Json(new
                {
                    error = Msg.GetMsg(Env.GetCtx(), "SessionExpired") ?? "Session Expired"
                }, JsonRequestBehavior.AllowGet);
            }

            Ctx ctx = Session["ctx"] as Ctx;

            /* "Last 30 days including today": start is 29 days ago so that the
               inclusive [start, end) range covers today + the previous 29 days.
               end is tomorrow because the WHERE uses `< end` on the truncated
               date, so all of today's receipts are included. */
            DateTime rangeStart = DateTime.Today.AddDays(-29);
            DateTime rangeEnd = DateTime.Today.AddDays(1);

            /* CTE body lives on the physical C_Payment table; MRole is applied
               only here. A receipt counts as "matched" when IsAllocated = 'Y'. */
            string receiptsBaseSql = @"
                SELECT CASE
                           WHEN Payment.IsAllocated = 'Y'
                           THEN 1
                           ELSE 0
                       END AS Is_Matched
                FROM C_Payment Payment
                WHERE Payment.IsReceipt = 'Y'
                  AND Payment.IsActive = 'Y'
                  AND Payment.DocStatus IN ('CO', 'CL')
                  AND " + TruncColumn("Payment.DateAcct") + @" >= " + ToSqlDate(rangeStart) + @"
                  AND " + TruncColumn("Payment.DateAcct") + @" < " + ToSqlDate(rangeEnd);

            receiptsBaseSql = MRole.GetDefault(ctx).AddAccessSQL(
                receiptsBaseSql,
                "Payment",
                MRole.SQL_FULLYQUALIFIED,
                MRole.SQL_RO
            );

            string sql = @"
                WITH ReceiptsThisMonth AS (
                    " + receiptsBaseSql + @"
                )
                SELECT COUNT(1) AS Total_Receipts,
                       SUM(ReceiptsThisMonth.Is_Matched) AS Matched_Receipts,
                       CASE
                           WHEN COUNT(1) > 0
                           THEN ROUND(SUM(ReceiptsThisMonth.Is_Matched) * 100.0 / COUNT(1), 2)
                           ELSE 0
                       END AS Auto_Allocated_Percent
                FROM ReceiptsThisMonth";

            decimal autoAllocatedPercent = 0;
            int totalReceipts = 0;
            int matchedReceipts = 0;

            IDataReader dr = null;

            try
            {
                dr = DB.ExecuteReader(sql);

                if (dr != null && dr.Read())
                {
                    totalReceipts = Util.GetValueOfInt(dr["Total_Receipts"]);
                    matchedReceipts = Util.GetValueOfInt(dr["Matched_Receipts"]);
                    autoAllocatedPercent = Util.GetValueOfDecimal(dr["Auto_Allocated_Percent"]);
                }

                int unmatchedReceipts = totalReceipts - matchedReceipts;
                if (unmatchedReceipts < 0) { unmatchedReceipts = 0; }

                var result = new
                {
                    autoAllocatedPercent = autoAllocatedPercent,
                    totalReceipts = totalReceipts,
                    matchedReceipts = matchedReceipts,
                    unmatchedReceipts = unmatchedReceipts,
                    fromDate = rangeStart.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture),
                    toDate = DateTime.Today.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture),
                    period = "Last 30 days"
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
        /// Returns one page of the drill-down list of AR receipts from the
        /// last 30 days (including today). Filter:
        ///   "allocated"   → receipts matched to invoices (default)
        ///   "unallocated" → receipts not yet matched
        ///   "all"         → no filter (legacy)
        /// Amount stays in the payment currency (no conversion). Server-side
        /// paged via OFFSET/FETCH so each tab fetches its own page.
        /// </summary>
        [AjaxAuthorizeAttribute]
        [AjaxSessionFilterAttribute]
        public JsonResult GetAutoAllocatedRows(int pageNo = 1, int pageSize = 10, string filter = "allocated")
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

            string filterKey = string.IsNullOrEmpty(filter) ? "allocated" : filter.Trim().ToLowerInvariant();

            /* Same "last 30 days including today" window as GetAutoAllocated. */
            DateTime rangeStart = DateTime.Today.AddDays(-29);
            DateTime rangeEnd = DateTime.Today.AddDays(1);

            int offset = (pageNo - 1) * pageSize;

            List<SqlParameter> parameters = new List<SqlParameter>();
            parameters.Add(new SqlParameter("@Offset", offset));
            parameters.Add(new SqlParameter("@PageSize", pageSize));

            /* The tab filter is a constant SQL fragment chosen from a small
               whitelist of keys — never interpolated from raw user input — so
               this stays safe even though it isn't parameterised. */
            string filterPredicate = "";
            if (filterKey == "allocated")
            {
                filterPredicate = "\n                  AND (Payment.IsAllocated = 'Y')";
            }
            else if (filterKey == "unallocated")
            {
                filterPredicate = "\n                  AND (Payment.IsAllocated IS NULL OR Payment.IsAllocated <> 'Y')";
            }

            string rowsBaseSql = @"
                SELECT Payment.C_Payment_ID AS Payment_ID,
                       Payment.DateAcct AS Date_Acct,
                       Payment.DocumentNo AS Document_No,
                       BPartner.Name AS Customer_Name,
                       COALESCE(Bank.Name, BankAccount.Name) AS Bank_Name,
                       COALESCE(BankAccount.AccountNo, N'') AS Account_No,
                       Payment.PaymentAmount AS Pay_Amount,
                       Currency.ISO_Code AS Payment_Currency,
                       CASE WHEN Currency.CurSymbol IS NOT NULL THEN Currency.CurSymbol ELSE Currency.ISO_Code END AS Payment_Currency_Symbol,
                       CASE
                           WHEN Payment.IsAllocated = 'Y'
                           THEN 'Y'
                           ELSE 'N'
                       END AS Is_Matched
                FROM C_Payment Payment
                INNER JOIN C_BankAccount BankAccount ON (Payment.C_BankAccount_ID=BankAccount.C_BankAccount_ID)
                INNER JOIN C_Bank Bank ON (BankAccount.C_Bank_ID=Bank.C_Bank_ID)
                INNER JOIN C_Currency Currency ON (Payment.C_Currency_ID=Currency.C_Currency_ID)
                LEFT JOIN C_BPartner BPartner ON (Payment.C_BPartner_ID=BPartner.C_BPartner_ID)
                WHERE Payment.IsReceipt = 'Y'
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

            /* CountData CTE returns a single TotalRecords value cross-joined to
               every page row, so one round-trip delivers the page rows plus
               totalRecords/totalPages for the pager. Portable OFFSET/FETCH. */
            string sql = @"
                WITH ReceiptsData AS (
                    " + rowsBaseSql + @"
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
                       ReceiptsData.Is_Matched,
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
                    period = "Last 30 days"
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
