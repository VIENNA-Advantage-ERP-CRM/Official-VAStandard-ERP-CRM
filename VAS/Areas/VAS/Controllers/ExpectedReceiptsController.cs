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
    public class ExpectedReceiptsController : Controller
    {
        /// <summary>
        /// Returns AR invoices/pay schedules expected to be received, with pagination and date filters.
        /// Default range is next 7 days.
        /// Supported filters: Next7Days, NextMonth, Custom.
        /// Compatible with Oracle and PostgreSQL.
        /// </summary>
        [AjaxAuthorizeAttribute]
        [AjaxSessionFilterAttribute]
        public JsonResult GetExpectedReceipts(string filterType, string fromDate, string toDate, string searchText, int pageNo = 1, int pageSize = 5)
        {
            if (Session["ctx"] == null)
            {
                return Json(new
                {
                    error = Msg.GetMsg(Env.GetCtx(), "SessionExpired") ?? "Session Expired"
                }, JsonRequestBehavior.AllowGet);
            }

            Ctx ctx = Session["ctx"] as Ctx;

            if (pageNo <= 0)
            {
                pageNo = 1;
            }

            if (pageSize <= 0)
            {
                pageSize = 5;
            }

            DateTime startDate;
            DateTime endDate;

            GetDateRange(filterType, fromDate, toDate, out startDate, out endDate);

            int offset = (pageNo - 1) * pageSize;

            List<SqlParameter> parameters = new List<SqlParameter>();

            parameters.Add(new SqlParameter("@Offset", offset));
            parameters.Add(new SqlParameter("@PageSize", pageSize));

            string schemaCurrencySql = @"
                SELECT ClientInfo.AD_Client_ID,
                       AcctSchema.C_Currency_ID AS C_Currency_ID,
                       Currency.StdPrecision
                FROM AD_ClientInfo ClientInfo
                INNER JOIN C_AcctSchema AcctSchema
                    ON ClientInfo.C_AcctSchema1_ID = AcctSchema.C_AcctSchema_ID
                INNER JOIN C_Currency Currency
                    ON AcctSchema.C_Currency_ID = Currency.C_Currency_ID";

            string expectedReceiptsBaseSql = @"
                SELECT Invoice.C_Invoice_ID,
                       Invoice.DocumentNo,
                       BusinessPartner.Name AS CustomerName,
                       InvoicePaySchedule.DueDate,
                       SchemaCurrency.C_Currency_ID,
                       SchemaCurrency.StdPrecision,

                       Invoice.PaymentRule,
                       PaymentRuleList.Name AS PaymentRuleName,

                       CASE
                           WHEN Invoice.C_Currency_ID = SchemaCurrency.C_Currency_ID THEN COALESCE(InvoicePaySchedule.DueAmt, 0)
                           ELSE CurrencyConvert(
                               COALESCE(InvoicePaySchedule.DueAmt, 0),
                               Invoice.C_Currency_ID,
                               SchemaCurrency.C_Currency_ID,
                               Invoice.DateAcct,
                               Invoice.C_ConversionType_ID,
                               Invoice.AD_Client_ID,
                               Invoice.AD_Org_ID
                           )
                       END AS ExpectedAmount

                FROM C_Invoice Invoice

                INNER JOIN C_InvoicePaySchedule InvoicePaySchedule
                    ON InvoicePaySchedule.C_Invoice_ID = Invoice.C_Invoice_ID

                INNER JOIN C_BPartner BusinessPartner
                    ON Invoice.C_BPartner_ID = BusinessPartner.C_BPartner_ID

                INNER JOIN SchemaCurrency SchemaCurrency
                    ON SchemaCurrency.AD_Client_ID = Invoice.AD_Client_ID

                LEFT JOIN AD_Table InvoiceTable
                    ON InvoiceTable.TableName = 'C_Invoice'

                LEFT JOIN AD_Column PaymentRuleColumn
                    ON PaymentRuleColumn.AD_Table_ID = InvoiceTable.AD_Table_ID
                   AND PaymentRuleColumn.ColumnName = 'PaymentRule'

                LEFT JOIN AD_Ref_List PaymentRuleList
                    ON PaymentRuleList.AD_Reference_ID = PaymentRuleColumn.AD_Reference_Value_ID
                   AND PaymentRuleList.Value = Invoice.PaymentRule
                   AND PaymentRuleList.IsActive = 'Y'

                WHERE Invoice.IsSoTrx = 'Y'
                  AND Invoice.IsActive = 'Y'
                  AND Invoice.DocStatus IN ('CO', 'CL')
                  AND InvoicePaySchedule.IsActive = 'Y'
                  AND InvoicePaySchedule.VA009_IsPaid = 'N'
                  AND " + TruncColumn("InvoicePaySchedule.DueDate") + @" >= " + ToSqlDate(startDate) + @"
                  AND " + TruncColumn("InvoicePaySchedule.DueDate") + @" < " + ToSqlDate(endDate);

            if (!string.IsNullOrEmpty(searchText))
            {
                expectedReceiptsBaseSql += @"
                  AND (
                      UPPER(Invoice.DocumentNo) LIKE @SearchText
                      OR UPPER(BusinessPartner.Name) LIKE @SearchText
                      OR UPPER(Invoice.PaymentRule) LIKE @SearchText
                  )";

                parameters.Add(new SqlParameter("@SearchText", "%" + searchText.Trim().ToUpper() + "%"));
            }

            expectedReceiptsBaseSql = MRole.GetDefault(ctx).AddAccessSQL(
                expectedReceiptsBaseSql,
                "Invoice",
                MRole.SQL_FULLYQUALIFIED,
                MRole.SQL_RO
            );

            string sql = @"
                WITH SchemaCurrency AS (
                    " + schemaCurrencySql + @"
                ),
                ExpectedReceiptData AS (
                    " + expectedReceiptsBaseSql + @"
                ),
                CountData AS (
                    SELECT COUNT(1) AS TotalRecords
                    FROM ExpectedReceiptData
                )
                SELECT ExpectedReceiptData.C_Invoice_ID,
                       ExpectedReceiptData.DocumentNo,
                       ExpectedReceiptData.CustomerName,
                       ExpectedReceiptData.DueDate,
                       ExpectedReceiptData.C_Currency_ID,
                       ExpectedReceiptData.StdPrecision,
                       ROUND(
                           COALESCE(ExpectedReceiptData.ExpectedAmount, 0),
                           ExpectedReceiptData.StdPrecision
                       ) AS ExpectedAmount,
                       ExpectedReceiptData.PaymentRule,
                       ExpectedReceiptData.PaymentRuleName,
                       CountData.TotalRecords
                FROM ExpectedReceiptData
                CROSS JOIN CountData
                ORDER BY ExpectedReceiptData.DueDate,
                         ExpectedReceiptData.DocumentNo
                OFFSET @Offset ROWS FETCH NEXT @PageSize ROWS ONLY";

            var rows = new List<object>();
            int totalRecords = 0;

            IDataReader dr = null;

            try
            {
                dr = DB.ExecuteReader(sql, parameters.ToArray());

                while (dr != null && dr.Read())
                {
                    totalRecords = Util.GetValueOfInt(dr["TotalRecords"]);

                    DateTime dueDate = DateTime.MinValue;

                    if (dr["DueDate"] != null && dr["DueDate"] != DBNull.Value)
                    {
                        dueDate = Convert.ToDateTime(dr["DueDate"]);
                    }

                    string paymentRule = dr["PaymentRule"] == null || dr["PaymentRule"] == DBNull.Value
                        ? ""
                        : dr["PaymentRule"].ToString();

                    string paymentMethodName = dr["PaymentRuleName"] == null || dr["PaymentRuleName"] == DBNull.Value
                        ? ""
                        : dr["PaymentRuleName"].ToString();

                    if (string.IsNullOrEmpty(paymentMethodName))
                    {
                        paymentMethodName = GetPaymentRuleName(ctx, paymentRule);
                    }

                    rows.Add(new
                    {
                        cInvoiceId = Util.GetValueOfInt(dr["C_Invoice_ID"]),
                        documentNo = dr["DocumentNo"] == null ? "" : dr["DocumentNo"].ToString(),
                        customerName = dr["CustomerName"] == null ? "" : dr["CustomerName"].ToString(),
                        dueDate = dueDate == DateTime.MinValue ? "" : dueDate.ToString("yyyy-MM-dd"),
                        cCurrencyId = Util.GetValueOfInt(dr["C_Currency_ID"]),
                        stdPrecision = Util.GetValueOfInt(dr["StdPrecision"]),
                        expectedAmount = Util.GetValueOfDecimal(dr["ExpectedAmount"]),
                        paymentRule = paymentRule,
                        paymentMethodName = paymentMethodName
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
            if (PaymentRuleNames.TryGetValue(paymentRule, out var paymentRuleInfo))
            {
                return GetMsg(ctx, paymentRuleInfo.MessageKey, paymentRuleInfo.DefaultName);
            }

            return GetMsg(ctx, "Expected", "Expected");
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

        private void GetDateRange(string filterType, string fromDate, string toDate, out DateTime startDate, out DateTime endDate)
        {
            DateTime today = DateTime.Today;
            string selectedFilter = string.IsNullOrEmpty(filterType) ? "Next7Days" : filterType;

            if (selectedFilter == "Next7Days")
            {
                startDate = today;
                endDate = today.AddDays(7);
                return;
            }

            if (selectedFilter == "NextMonth")
            {
                DateTime nextMonthStart = new DateTime(today.Year, today.Month, 1).AddMonths(1);
                DateTime nextMonthEnd = nextMonthStart.AddMonths(1);

                startDate = nextMonthStart;
                endDate = nextMonthEnd;
                return;
            }

            if (selectedFilter == "Custom")
            {
                DateTime? parsedFromDate = Util.GetValueOfDateTime(fromDate);
                DateTime? parsedToDate = Util.GetValueOfDateTime(toDate);

                if (parsedFromDate.HasValue && parsedToDate.HasValue)
                {
                    startDate = parsedFromDate.Value.Date;
                    endDate = parsedToDate.Value.Date.AddDays(1);
                    return;
                }
            }

            startDate = today;
            endDate = today.AddDays(7);
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