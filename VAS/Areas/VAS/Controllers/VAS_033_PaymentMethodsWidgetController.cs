using System;
using System.Collections.Generic;
using System.Data;
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
            IDataReader dr = null;

            try
            {
                DateTime today = DateTime.Today;

                if (string.IsNullOrEmpty(periodFilter))
                {
                    periodFilter = PeriodFilterMonth;
                }

                periodFilter = periodFilter.ToUpper();

                bool isYTD = periodFilter == PeriodFilterYTD;

                bool hasPaymentMethod = HasPaymentMethodColumn();
                bool hasPaymentMethodName = hasPaymentMethod && HasPaymentMethodNameColumn();
                bool hasPaymentMethodValue = hasPaymentMethod && HasPaymentMethodValueColumn();

                string paymentMethodSelect = "p.PaymentRule";
                string paymentMethodGroupBy = "p.PaymentRule";
                string paymentMethodJoin = string.Empty;

                if (hasPaymentMethod)
                {
                    paymentMethodJoin = @"
                    LEFT OUTER JOIN VA009_PaymentMethod pm ON (p.VA009_PaymentMethod_ID = pm.VA009_PaymentMethod_ID)";

                    if (hasPaymentMethodName)
                    {
                        paymentMethodSelect = "pm.Name";
                        paymentMethodGroupBy = "pm.Name";
                    }
                    else if (hasPaymentMethodValue)
                    {
                        paymentMethodSelect = "pm.Value";
                        paymentMethodGroupBy = "pm.Value";
                    }
                    else
                    {
                        paymentMethodSelect = "p.PaymentRule";
                        paymentMethodGroupBy = "p.PaymentRule";
                        paymentMethodJoin = string.Empty;
                    }
                }

                string dateSql = GetDatabaseDate(today);

                string schemaCurrencySql = @"
                    SELECT ClientInfo.AD_Client_ID,
                           AcctSchema.C_Currency_ID AS C_Currency_ID,
                           Currency.StdPrecision,
                           Currency.ISO_Code AS ISO_Code,
                           CASE
                               WHEN Currency.CurSymbol IS NOT NULL THEN Currency.CurSymbol
                               ELSE Currency.ISO_Code
                           END AS Cur_Symbol
                    FROM AD_ClientInfo ClientInfo
                    INNER JOIN C_AcctSchema AcctSchema ON (ClientInfo.C_AcctSchema1_ID = AcctSchema.C_AcctSchema_ID)
                    INNER JOIN C_Currency Currency ON (AcctSchema.C_Currency_ID = Currency.C_Currency_ID)
                    WHERE ClientInfo.AD_Client_ID = " + ctx.GetAD_Client_ID();

                string currentPeriodSql = @"
                    SELECT Period.C_Period_ID,
                           Period.C_Year_ID,
                           Period.StartDate,
                           Period.EndDate
                    FROM AD_ClientInfo ClientInfo
                    INNER JOIN C_Year YearData ON (YearData.C_Calendar_ID = ClientInfo.C_Calendar_ID)
                    INNER JOIN C_Period Period ON (Period.C_Year_ID = YearData.C_Year_ID)
                    WHERE ClientInfo.IsActive = 'Y'
                    AND ClientInfo.AD_Client_ID = " + ctx.GetAD_Client_ID() + @"
                    AND " + dateSql + @" BETWEEN Period.StartDate AND Period.EndDate";

                string periodRangeSql;

                if (isYTD)
                {
                    periodRangeSql = @"
                    SELECT MIN(Period.StartDate) AS StartDate,
                           MAX(CurrentPeriod.EndDate) AS EndDate
                    FROM CurrentPeriod CurrentPeriod
                    INNER JOIN C_Period Period ON (Period.C_Year_ID = CurrentPeriod.C_Year_ID)
                    WHERE Period.StartDate <= CurrentPeriod.EndDate";
                }
                else
                {
                    periodRangeSql = @"
                    SELECT CurrentPeriod.StartDate,
                           CurrentPeriod.EndDate
                    FROM CurrentPeriod CurrentPeriod";
                }

                string paymentBaseSql = @"
                    SELECT " + paymentMethodSelect + @" AS PaymentMethodName,
                           COUNT(1) AS PaymentCount,
                           ROUND(
                               COALESCE(
                                   SUM(
                                       CASE
                                           WHEN p.C_Currency_ID = SchemaCurrency.C_Currency_ID THEN COALESCE(p.PayAmt, 0)
                                           ELSE CurrencyConvert(
                                               COALESCE(p.PayAmt, 0),
                                               p.C_Currency_ID,
                                               SchemaCurrency.C_Currency_ID,
                                               p.DateAcct,
                                               p.C_ConversionType_ID,
                                               p.AD_Client_ID,
                                               p.AD_Org_ID
                                           )
                                       END
                                   ),
                                   0
                               ),
                               MAX(SchemaCurrency.StdPrecision)
                           ) AS PaymentAmount
                    FROM C_Payment p
                    INNER JOIN SchemaCurrency SchemaCurrency ON (SchemaCurrency.AD_Client_ID = p.AD_Client_ID)
                    " + paymentMethodJoin + @"
                    INNER JOIN PeriodRange PeriodRange ON (p.DateAcct BETWEEN PeriodRange.StartDate AND PeriodRange.EndDate)
                    WHERE p.IsActive = 'Y'
                    AND p.IsReceipt = 'N'
                    AND p.DocStatus IN ('CO', 'CL')";

                /*
                 * MRole Rule:
                 * Apply role access only on the main physical table alias.
                 * Main physical table: C_Payment p
                 *
                 * Do not apply MRole on:
                 * - Final WITH query
                 * - SchemaCurrency CTE
                 * - CurrentPeriod CTE
                 * - PeriodRange CTE
                 * - VA009_PaymentMethod pm join alias
                 */
                paymentBaseSql = MRole.GetDefault(ctx).AddAccessSQL(
                    paymentBaseSql,
                    "p",
                    MRole.SQL_FULLYQUALIFIED,
                    MRole.SQL_RO
                );

                string sql = @"
                    WITH SchemaCurrency AS (
                        " + schemaCurrencySql + @"
                    ),
                    CurrentPeriod AS (
                        " + currentPeriodSql + @"
                    ),
                    PeriodRange AS (
                        " + periodRangeSql + @"
                    )
                    SELECT PaymentData.PaymentMethodName,
                           PaymentData.PaymentCount,
                           PaymentData.PaymentAmount
                    FROM (" + paymentBaseSql + @"
                        GROUP BY " + paymentMethodGroupBy + @"
                    ) PaymentData
                    ORDER BY PaymentData.PaymentAmount DESC";

                dr = DB.ExecuteReader(sql);

                List<PaymentMethodSummary> rows = new List<PaymentMethodSummary>();
                decimal totalAmount = 0;

                while (dr.Read())
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

                DateRangeResult dateRange = GetPeriodDateRange(ctx, today, isYTD);

                return Json(new
                {
                    title = GetMsg(ctx, "VAS_033_MessagePaymentMethods", "Payment methods"),
                    description = GetMsg(ctx, "VAS_033_MessagePaymentMethodWhy", "UPI is cheapest · shift sub-₹2L payments where possible"),
                    totalAmount = totalAmount,
                    dateFrom = FormatDate(dateRange.DateFrom),
                    dateTo = FormatDate(dateRange.DateTo),
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

        private bool HasPaymentMethodColumn()
        {
            string sql = @"
                SELECT COUNT(1)
                FROM AD_Table t
                INNER JOIN AD_Column c ON (t.AD_Table_ID = c.AD_Table_ID)
                WHERE t.TableName = 'C_Payment'
                AND c.ColumnName = 'VA009_PaymentMethod_ID'";

            return Util.GetValueOfInt(DB.ExecuteScalar(sql)) > 0;
        }

        private bool HasPaymentMethodNameColumn()
        {
            string sql = @"
                SELECT COUNT(1)
                FROM AD_Table t
                INNER JOIN AD_Column c ON (t.AD_Table_ID = c.AD_Table_ID)
                WHERE t.TableName = 'VA009_PaymentMethod'
                AND c.ColumnName = 'Name'";

            return Util.GetValueOfInt(DB.ExecuteScalar(sql)) > 0;
        }

        private bool HasPaymentMethodValueColumn()
        {
            string sql = @"
                SELECT COUNT(1)
                FROM AD_Table t
                INNER JOIN AD_Column c ON (t.AD_Table_ID = c.AD_Table_ID)
                WHERE t.TableName = 'VA009_PaymentMethod'
                AND c.ColumnName = 'Value'";

            return Util.GetValueOfInt(DB.ExecuteScalar(sql)) > 0;
        }

        private DateRangeResult GetPeriodDateRange(Ctx ctx, DateTime dateAcct, bool isYTD)
        {
            string dateSql = GetDatabaseDate(dateAcct);

            string sql;

            if (isYTD)
            {
                sql = @"
                    WITH CurrentPeriod AS (
                        SELECT Period.C_Year_ID,
                               Period.EndDate
                        FROM AD_ClientInfo ClientInfo
                        INNER JOIN C_Year YearData ON (YearData.C_Calendar_ID = ClientInfo.C_Calendar_ID)
                        INNER JOIN C_Period Period ON (Period.C_Year_ID = YearData.C_Year_ID)
                        WHERE ClientInfo.IsActive = 'Y'
                        AND ClientInfo.AD_Client_ID = " + ctx.GetAD_Client_ID() + @"
                        AND " + dateSql + @" BETWEEN Period.StartDate AND Period.EndDate
                    )
                    SELECT MIN(Period.StartDate) AS DateFrom,
                           MAX(CurrentPeriod.EndDate) AS DateTo
                    FROM CurrentPeriod CurrentPeriod
                    INNER JOIN C_Period Period ON (Period.C_Year_ID = CurrentPeriod.C_Year_ID)
                    WHERE Period.StartDate <= CurrentPeriod.EndDate";
            }
            else
            {
                sql = @"
                    SELECT Period.StartDate AS DateFrom,
                           Period.EndDate AS DateTo
                    FROM AD_ClientInfo ClientInfo
                    INNER JOIN C_Year YearData ON (YearData.C_Calendar_ID = ClientInfo.C_Calendar_ID)
                    INNER JOIN C_Period Period ON (Period.C_Year_ID = YearData.C_Year_ID)
                    WHERE ClientInfo.IsActive = 'Y'
                    AND ClientInfo.AD_Client_ID = " + ctx.GetAD_Client_ID() + @"
                    AND " + dateSql + @" BETWEEN Period.StartDate AND Period.EndDate";
            }

            IDataReader dr = null;

            try
            {
                dr = DB.ExecuteReader(sql);

                if (dr.Read())
                {
                    return new DateRangeResult
                    {
                        DateFrom = Util.GetValueOfDateTime(dr["DateFrom"]) ?? DateTime.Now,
                        DateTo = Util.GetValueOfDateTime(dr["DateTo"]) ?? DateTime.Now
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

            DateTime fallbackDateFrom = new DateTime(dateAcct.Year, dateAcct.Month, 1);
            DateTime fallbackDateTo = fallbackDateFrom.AddMonths(1).AddDays(-1);

            return new DateRangeResult
            {
                DateFrom = fallbackDateFrom,
                DateTo = fallbackDateTo
            };
        }

        private string GetDatabaseDate(DateTime date)
        {
            string dateText = FormatDate(date);

            if (DB.IsOracle())
            {
                return "TO_DATE('" + dateText + "', 'YYYY-MM-DD')";
            }

            return "DATE '" + dateText + "'";
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
            public DateTime DateFrom { get; set; }
            public DateTime DateTo { get; set; }
        }
    }
}