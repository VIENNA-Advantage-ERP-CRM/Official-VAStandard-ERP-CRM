using System;
using System.Collections.Generic;
using System.Data;
using System.Data.SqlClient;
using System.Web.Mvc;
using VAdvantage.Classes;
using VAdvantage.DataBase;
using VAdvantage.Model;
using VAdvantage.Utility;
using VIS.Filters;

namespace VAS.Controllers
{
    /*
     * Labels / Message Keys
     * 1 | Payment methods                                        | VAS_033_MessagePaymentMethods
     * 2 | UPI is cheapest - shift sub-2L payments where possible | VAS_033_MessagePaymentMethodWhy
     * 3 | Not Specified                                          | VAS_033_MessageNotSpecified
     */
    public class VAS_033_PaymentMethodsWidgetController : Controller
    {
        private const string PeriodFilterMonth = "MONTH";
        private const string PeriodFilterYTD = "YTD";

        [AjaxAuthorizeAttribute]
        [AjaxSessionFilterAttribute]
        public JsonResult GetPaymentMethods()
        {
            string periodFilter = PeriodFilterMonth;

            if (Session["ctx"] == null)
            {
                return Json(new
                {
                    error = "Session Expired",
                    errorText = "Session Expired"
                }, JsonRequestBehavior.AllowGet);
            }

            Ctx ctx = Session["ctx"] as Ctx;

            if (ctx == null)
            {
                return Json(new
                {
                    error = "Session Expired",
                    errorText = "Session Expired"
                }, JsonRequestBehavior.AllowGet);
            }

            IDataReader dr = null;

            try
            {
                if (string.IsNullOrEmpty(periodFilter))
                {
                    periodFilter = PeriodFilterMonth;
                }

                periodFilter = periodFilter.ToUpper();

                bool isYTD = periodFilter == PeriodFilterYTD;

                SqlQueryData queryData = BuildPaymentMethodsSql(ctx, isYTD);

                dr = DB.ExecuteReader(queryData.Sql, queryData.Parameters, null);

                List<PaymentMethodSummary> rows = new List<PaymentMethodSummary>();
                decimal totalAmount = 0;

                while (dr != null && dr.Read())
                {
                    decimal paymentAmount = Util.GetValueOfDecimal(dr["PaymentAmount"]);

                    rows.Add(new PaymentMethodSummary
                    {
                        PaymentMethodName = Util.GetValueOfString(dr["PaymentMethodName"]),
                        PaymentCount = Util.GetValueOfInt(dr["PaymentCount"]),
                        PaymentAmount = paymentAmount
                    });

                    totalAmount += paymentAmount;
                }

                List<object> methods = new List<object>();

                foreach (PaymentMethodSummary row in rows)
                {
                    string paymentMethodName = row.PaymentMethodName;

                    if (string.IsNullOrEmpty(paymentMethodName))
                    {
                        paymentMethodName = GetMsg(ctx, "VAS_033_MessageNotSpecified", "Not Specified");
                    }

                    decimal percentage = 0;

                    if (totalAmount > 0)
                    {
                        percentage = decimal.Round((row.PaymentAmount * 100M) / totalAmount, 2);
                    }

                    methods.Add(new
                    {
                        paymentMethodName = paymentMethodName,
                        paymentCount = row.PaymentCount,
                        paymentAmount = row.PaymentAmount,
                        percentage = percentage
                    });
                }

                DateRangeResult dateRange = GetPeriodDateRange(ctx, isYTD);

                return Json(new
                {
                    title = GetMsg(ctx, "VAS_033_MessagePaymentMethods", "Payment methods"),
                    description = GetMsg(ctx, "VAS_033_MessagePaymentMethodWhy", "UPI is cheapest · shift sub-₹2L payments where possible"),
                    totalAmount = totalAmount,
                    dateFrom = dateRange != null && dateRange.DateFrom.HasValue ? FormatDate(dateRange.DateFrom.Value) : "",
                    dateTo = dateRange != null && dateRange.DateTo.HasValue ? FormatDate(dateRange.DateTo.Value) : "",
                    periodFilter = isYTD ? PeriodFilterYTD : PeriodFilterMonth,
                    methods = methods
                }, JsonRequestBehavior.AllowGet);
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

        private SqlQueryData BuildPaymentMethodsSql(Ctx ctx, bool isYTD)
        {
            bool hasPaymentMethod = HasPaymentMethodColumn();
            bool hasPaymentMethodName = hasPaymentMethod && HasPaymentMethodNameColumn();
            bool hasPaymentMethodValue = hasPaymentMethod && HasPaymentMethodValueColumn();
            bool hasPaymentRule = HasPaymentRuleColumn();

            string paymentMethodSelect = GetEmptyTextSql();
            string paymentMethodGroupBy = GetEmptyTextSql();
            string paymentMethodJoin = string.Empty;

            if (hasPaymentMethod)
            {
                paymentMethodJoin = @"
LEFT OUTER JOIN VA009_PaymentMethod PaymentMethod ON (Payment.VA009_PaymentMethod_ID = PaymentMethod.VA009_PaymentMethod_ID)";

                if (hasPaymentMethodName)
                {
                    paymentMethodSelect = GetTextSql("PaymentMethod.Name");
                    paymentMethodGroupBy = GetTextSql("PaymentMethod.Name");
                }
                else if (hasPaymentMethodValue)
                {
                    paymentMethodSelect = GetTextSql("PaymentMethod.Value");
                    paymentMethodGroupBy = GetTextSql("PaymentMethod.Value");
                }
                else if (hasPaymentRule)
                {
                    paymentMethodSelect = GetTextSql("Payment.PaymentRule");
                    paymentMethodGroupBy = GetTextSql("Payment.PaymentRule");
                    paymentMethodJoin = string.Empty;
                }
            }
            else if (hasPaymentRule)
            {
                paymentMethodSelect = GetTextSql("Payment.PaymentRule");
                paymentMethodGroupBy = GetTextSql("Payment.PaymentRule");
            }

            string schemaCurrencySql = @"
SchemaCurrency AS
(
SELECT
ClientInfo.AD_Client_ID,
AcctSchema.C_Currency_ID AS C_Currency_ID,
Currency.StdPrecision,
" + GetTextSql("Currency.ISO_Code") + @" AS ISO_Code,
CASE WHEN Currency.CurSymbol IS NOT NULL THEN " + GetTextSql("Currency.CurSymbol") + @" ELSE " + GetTextSql("Currency.ISO_Code") + @" END AS Cur_Symbol
FROM AD_ClientInfo ClientInfo
INNER JOIN C_AcctSchema AcctSchema ON (ClientInfo.C_AcctSchema1_ID = AcctSchema.C_AcctSchema_ID)
INNER JOIN C_Currency Currency ON (AcctSchema.C_Currency_ID = Currency.C_Currency_ID)
WHERE ClientInfo.IsActive = 'Y'
AND ClientInfo.AD_Client_ID = @AD_Client_ID
)";

            string currentPeriodSql = @"
CurrentPeriod AS
(
SELECT
Period.C_Period_ID,
Period.C_Year_ID,
Period.StartDate,
Period.EndDate
FROM AD_ClientInfo ClientInfo
INNER JOIN C_Year YearData ON (YearData.C_Calendar_ID = ClientInfo.C_Calendar_ID)
INNER JOIN C_Period Period ON (Period.C_Year_ID = YearData.C_Year_ID)
WHERE ClientInfo.IsActive = 'Y'
AND ClientInfo.AD_Client_ID = @AD_Client_ID
AND " + GetCurrentTimestampSql() + @" >= CAST(Period.StartDate AS TIMESTAMP)
AND " + GetCurrentTimestampSql() + @" < " + GetDateToExclusiveSql("Period.EndDate") + @"
)";

            string periodRangeSql;

            if (isYTD)
            {
                periodRangeSql = @"
PeriodRange AS
(
SELECT
MIN(Period.StartDate) AS StartDate,
MAX(CurrentPeriod.EndDate) AS EndDate,
MAX(" + GetDateToExclusiveSql("CurrentPeriod.EndDate") + @") AS EndDateExclusive
FROM CurrentPeriod CurrentPeriod
INNER JOIN C_Period Period ON (Period.C_Year_ID = CurrentPeriod.C_Year_ID)
WHERE Period.StartDate <= CurrentPeriod.EndDate
)";
            }
            else
            {
                periodRangeSql = @"
PeriodRange AS
(
SELECT
CurrentPeriod.StartDate AS StartDate,
CurrentPeriod.EndDate AS EndDate,
" + GetDateToExclusiveSql("CurrentPeriod.EndDate") + @" AS EndDateExclusive
FROM CurrentPeriod CurrentPeriod
)";
            }

            string paymentAccessSql = @"
SELECT
Payment.C_Payment_ID,
Payment.AD_Client_ID,
Payment.AD_Org_ID,
Payment.C_Currency_ID,
Payment.C_ConversionType_ID,
Payment.DateAcct,
Payment.PayAmt"
+ (hasPaymentRule ? @",
Payment.PaymentRule" : string.Empty)
+ (hasPaymentMethod ? @",
Payment.VA009_PaymentMethod_ID" : string.Empty) + @"
FROM C_Payment Payment
WHERE Payment.IsActive = 'Y'
AND Payment.IsReceipt = 'N'
AND Payment.DocStatus IN ('CO', 'CL')";

            paymentAccessSql = MRole.GetDefault(ctx).AddAccessSQL(
                paymentAccessSql,
                "Payment",
                MRole.SQL_FULLYQUALIFIED,
                MRole.SQL_RO
            );

            string paymentFilteredSql = @"
PaymentFiltered AS
(
" + paymentAccessSql + @"
)";

            string paymentDataSql = @"
PaymentData AS
(
SELECT
" + paymentMethodSelect + @" AS PaymentMethodName,
COUNT(1) AS PaymentCount,
ROUND(
COALESCE(
SUM(
CASE WHEN Payment.C_Currency_ID = SchemaCurrency.C_Currency_ID THEN COALESCE(Payment.PayAmt, 0) ELSE CurrencyConvert(COALESCE(Payment.PayAmt, 0), Payment.C_Currency_ID, SchemaCurrency.C_Currency_ID, Payment.DateAcct, Payment.C_ConversionType_ID, Payment.AD_Client_ID, Payment.AD_Org_ID) END
),
0
),
MAX(SchemaCurrency.StdPrecision)
) AS PaymentAmount
FROM PaymentFiltered Payment
INNER JOIN SchemaCurrency SchemaCurrency ON (SchemaCurrency.AD_Client_ID = Payment.AD_Client_ID)
INNER JOIN PeriodRange PeriodRange ON (Payment.DateAcct >= PeriodRange.StartDate AND Payment.DateAcct < PeriodRange.EndDateExclusive)" + paymentMethodJoin + @"
GROUP BY " + paymentMethodGroupBy + @"
)";

            string sql = @"
WITH " + schemaCurrencySql + @",
" + currentPeriodSql + @",
" + periodRangeSql + @",
" + paymentFilteredSql + @",
" + paymentDataSql + @"
SELECT
PaymentData.PaymentMethodName,
PaymentData.PaymentCount,
PaymentData.PaymentAmount
FROM PaymentData PaymentData
ORDER BY PaymentData.PaymentAmount DESC";

            SqlParameter[] parameters = new SqlParameter[]
            {
                new SqlParameter("@AD_Client_ID", ctx.GetAD_Client_ID())
            };

            return new SqlQueryData
            {
                Sql = sql,
                Parameters = parameters
            };
        }

        private DateRangeResult GetPeriodDateRange(Ctx ctx, bool isYTD)
        {
            string currentPeriodSql = @"
CurrentPeriod AS
(
SELECT
Period.C_Year_ID,
Period.StartDate,
Period.EndDate
FROM AD_ClientInfo ClientInfo
INNER JOIN C_Year YearData ON (YearData.C_Calendar_ID = ClientInfo.C_Calendar_ID)
INNER JOIN C_Period Period ON (Period.C_Year_ID = YearData.C_Year_ID)
WHERE ClientInfo.IsActive = 'Y'
AND ClientInfo.AD_Client_ID = @AD_Client_ID
AND " + GetCurrentTimestampSql() + @" >= CAST(Period.StartDate AS TIMESTAMP)
AND " + GetCurrentTimestampSql() + @" < " + GetDateToExclusiveSql("Period.EndDate") + @"
)";

            string sql;

            if (isYTD)
            {
                sql = @"
WITH " + currentPeriodSql + @"
SELECT
MIN(Period.StartDate) AS DateFrom,
MAX(CurrentPeriod.EndDate) AS DateTo
FROM CurrentPeriod CurrentPeriod
INNER JOIN C_Period Period ON (Period.C_Year_ID = CurrentPeriod.C_Year_ID)
WHERE Period.StartDate <= CurrentPeriod.EndDate";
            }
            else
            {
                sql = @"
WITH " + currentPeriodSql + @"
SELECT
CurrentPeriod.StartDate AS DateFrom,
CurrentPeriod.EndDate AS DateTo
FROM CurrentPeriod CurrentPeriod";
            }

            SqlParameter[] parameters = new SqlParameter[]
            {
                new SqlParameter("@AD_Client_ID", ctx.GetAD_Client_ID())
            };

            IDataReader dr = null;

            try
            {
                dr = DB.ExecuteReader(sql, parameters, null);

                if (dr != null && dr.Read())
                {
                    return new DateRangeResult
                    {
                        DateFrom = dr["DateFrom"] != DBNull.Value ? Util.GetValueOfDateTime(dr["DateFrom"]) : null,
                        DateTo = dr["DateTo"] != DBNull.Value ? Util.GetValueOfDateTime(dr["DateTo"]) : null
                    };
                }
            }
            finally
            {
                if (dr != null)
                {
                    dr.Close();
                    dr.Dispose();
                }
            }

            return new DateRangeResult
            {
                DateFrom = null,
                DateTo = null
            };
        }

        private string GetCurrentTimestampSql()
        {
            return "CAST(CURRENT_TIMESTAMP AS TIMESTAMP)";
        }

        private string GetDateToExclusiveSql(string columnName)
        {
            return "CAST(" + columnName + " AS TIMESTAMP) + INTERVAL '1' DAY";
        }

        private string GetTextSql(string columnName)
        {
            return "TRIM(CAST(" + columnName + " AS CHAR(255)))";
        }

        private string GetEmptyTextSql()
        {
            return "CAST(NULL AS CHAR(1))";
        }

        private bool HasPaymentMethodColumn()
        {
            string sql = @"
SELECT
COUNT(1)
FROM AD_Table TableData
INNER JOIN AD_Column ColumnData ON (TableData.AD_Table_ID = ColumnData.AD_Table_ID)
WHERE TableData.TableName = 'C_Payment'
AND ColumnData.ColumnName = 'VA009_PaymentMethod_ID'";

            return Util.GetValueOfInt(DB.ExecuteScalar(sql)) > 0;
        }

        private bool HasPaymentRuleColumn()
        {
            string sql = @"
SELECT
COUNT(1)
FROM AD_Table TableData
INNER JOIN AD_Column ColumnData ON (TableData.AD_Table_ID = ColumnData.AD_Table_ID)
WHERE TableData.TableName = 'C_Payment'
AND ColumnData.ColumnName = 'PaymentRule'";

            return Util.GetValueOfInt(DB.ExecuteScalar(sql)) > 0;
        }

        private bool HasPaymentMethodNameColumn()
        {
            string sql = @"
SELECT
COUNT(1)
FROM AD_Table TableData
INNER JOIN AD_Column ColumnData ON (TableData.AD_Table_ID = ColumnData.AD_Table_ID)
WHERE TableData.TableName = 'VA009_PaymentMethod'
AND ColumnData.ColumnName = 'Name'";

            return Util.GetValueOfInt(DB.ExecuteScalar(sql)) > 0;
        }

        private bool HasPaymentMethodValueColumn()
        {
            string sql = @"
SELECT
COUNT(1)
FROM AD_Table TableData
INNER JOIN AD_Column ColumnData ON (TableData.AD_Table_ID = ColumnData.AD_Table_ID)
WHERE TableData.TableName = 'VA009_PaymentMethod'
AND ColumnData.ColumnName = 'Value'";

            return Util.GetValueOfInt(DB.ExecuteScalar(sql)) > 0;
        }

        private string FormatDate(DateTime date)
        {
            return date.ToString("yyyy-MM-dd");
        }

        private string GetMsg(Ctx ctx, string key, string fallback)
        {
            string msg = Msg.GetMsg(ctx, key);

            return !string.IsNullOrEmpty(msg) && msg != "[" + key + "]"
                ? msg
                : fallback;
        }

        private class PaymentMethodSummary
        {
            public string PaymentMethodName { get; set; }
            public int PaymentCount { get; set; }
            public decimal PaymentAmount { get; set; }
        }

        private class DateRangeResult
        {
            public DateTime? DateFrom { get; set; }
            public DateTime? DateTo { get; set; }
        }

        private class SqlQueryData
        {
            public string Sql { get; set; }
            public SqlParameter[] Parameters { get; set; }
        }
    }
}