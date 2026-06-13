using System;
using System.Collections.Generic;
using System.Data;
using System.Globalization;
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
     * 1 | Upcoming runs | VAS_033_MessageUpcomingRuns
     * 2 | Next 7 days   | VAS_033_MessageNext7Days
     * 3 | payment       | VAS_033_MessagePayment
     * 4 | payments      | VAS_033_MessagePayments
     * 5 | YTD           | VAS_033_MessageYTD
     * 6 | Month         | VAS_033_MessageMonth
     */
    public class VAS_033_UpcomingAPRunsWidgetController : Controller
    {
        public JsonResult GetUpcomingAPRuns()
        {
            string dateFilter = "YTD";

            if (Session["ctx"] == null)
            {
                return Json(new
                {
                    error = "Session Expired",
                    errorText = "Session Expired"
                }, JsonRequestBehavior.AllowGet);
            }

            Ctx ctx = Session["ctx"] as Ctx;

            try
            {
                string sql = BuildUpcomingAPRunsSql(ctx, dateFilter);

                using (IDataReader dr = DB.ExecuteReader(sql))
                {
                    List<object> runs = new List<object>();

                    while (dr.Read() && runs.Count < 30)
                    {
                        DateTime? runDate = Util.GetValueOfDateTime(dr["RunDate"]);
                        int paymentCount = Util.GetValueOfInt(dr["PaymentCount"]);

                        runs.Add(new
                        {
                            paymentMethodName = GetPaymentMethodName(ctx, Util.GetValueOfString(dr["PaymentMethodName"])),
                            runDate = FormatDate(runDate),
                            runDateText = FormatRunDate(runDate),
                            paymentCount = paymentCount,
                            paymentCountText = GetPaymentCountText(ctx, paymentCount),
                            amount = Util.GetValueOfDecimal(dr["Amount"]),
                            cCurrencyId = Util.GetValueOfInt(dr["C_Currency_ID"]),
                            currencyISO = Util.GetValueOfString(dr["CurrencyISO"]),
                            currencySymbol = Util.GetValueOfString(dr["CurrencySymbol"]),
                            stdPrecision = Util.GetValueOfInt(dr["StdPrecision"])
                        });
                    }

                    return Json(new
                    {
                        title = GetMsg(ctx, "VAS_033_MessageUpcomingRuns", "Upcoming runs"),
                        periodText = GetPeriodText(ctx, dateFilter),
                        runs = runs
                    }, JsonRequestBehavior.AllowGet);
                }
            }
            catch (Exception ex)
            {
                return Json(new
                {
                    error = ex.Message,
                    errorText = ex.Message
                }, JsonRequestBehavior.AllowGet);
            }
        }

        private string BuildUpcomingAPRunsSql(Ctx ctx, string dateFilter)
        {
            string dateCondition = GetDateConditionSql(dateFilter);

            string schemaCurrencyCte = @"
SchemaCurrency AS (
    SELECT 
        ClientInfo.AD_Client_ID,
        AcctSchema.C_Currency_ID,
        Currency.StdPrecision,
        Currency.ISO_Code,
        COALESCE(Currency.CurSymbol, Currency.ISO_Code) AS Cur_Symbol
    FROM AD_ClientInfo ClientInfo
    INNER JOIN C_AcctSchema AcctSchema 
        ON ClientInfo.C_AcctSchema1_ID = AcctSchema.C_AcctSchema_ID
    INNER JOIN C_Currency Currency 
        ON AcctSchema.C_Currency_ID = Currency.C_Currency_ID
)";

            string paymentAccessSql = @"
SELECT p.C_Payment_ID
FROM C_Payment p
WHERE p.IsActive = 'Y'
AND p.IsReceipt = 'N'
" + dateCondition + @"
AND p.DocStatus NOT IN ('VO', 'RE')";

            /*
             * MRole must be applied only on the main physical table C_Payment p.
             * Do not apply MRole on the final WITH query.
             * Do not apply MRole on CTE aliases.
             * Do not apply MRole on joined aliases.
             */
            paymentAccessSql = MRole.GetDefault(ctx).AddAccessSQL(
                paymentAccessSql,
                "p",
                MRole.SQL_FULLYQUALIFIED,
                MRole.SQL_RO
            );

            string filteredPaymentCte = @"
FilteredPayment AS (
" + paymentAccessSql + @"
)";

            string finalSql = @"
WITH " + schemaCurrencyCte + @",
" + filteredPaymentCte + @"
SELECT 
    p.DateTrx AS RunDate,
    pm.VA009_Name AS PaymentMethodName,
    COUNT(1) AS PaymentCount,

    ROUND(
        CAST(
            COALESCE(
                SUM(
                    CASE 
                        WHEN p.C_Currency_ID = SchemaCurrency.C_Currency_ID 
                        THEN COALESCE(p.PayAmt, 0)
                        ELSE CurrencyConvert(
                            COALESCE(p.PayAmt, 0),
                            p.C_Currency_ID,
                            SchemaCurrency.C_Currency_ID,
                            p.DateTrx,
                            p.C_ConversionType_ID,
                            p.AD_Client_ID,
                            p.AD_Org_ID
                        )
                    END
                ), 
            0) AS NUMERIC
        ),
        CAST(MAX(SchemaCurrency.StdPrecision) AS INTEGER)
    ) AS Amount,

    MAX(SchemaCurrency.C_Currency_ID) AS C_Currency_ID,
    MAX(SchemaCurrency.StdPrecision) AS StdPrecision,
    MAX(SchemaCurrency.ISO_Code) AS CurrencyISO,
    MAX(SchemaCurrency.Cur_Symbol) AS CurrencySymbol

FROM C_Payment p
INNER JOIN FilteredPayment fp 
    ON p.C_Payment_ID = fp.C_Payment_ID
INNER JOIN SchemaCurrency SchemaCurrency 
    ON SchemaCurrency.AD_Client_ID = p.AD_Client_ID
LEFT OUTER JOIN VA009_PaymentMethod pm 
    ON p.VA009_PaymentMethod_ID = pm.VA009_PaymentMethod_ID

GROUP BY 
    p.DateTrx,
    pm.VA009_Name

ORDER BY 
    p.DateTrx ASC,
    pm.VA009_Name ASC";

            return finalSql;
        }

        private string GetDateConditionSql(string dateFilter)
        {
            dateFilter = string.IsNullOrEmpty(dateFilter)
                ? "NEXT7D"
                : dateFilter.ToUpperInvariant();

            if (dateFilter == "YTD")
            {
                return @"
AND p.DateTrx >= (
    SELECT MIN(pr.StartDate)
    FROM AD_ClientInfo ci
    INNER JOIN C_Year yr 
        ON yr.C_Calendar_ID = ci.C_Calendar_ID
    INNER JOIN C_Period cur 
        ON cur.C_Year_ID = yr.C_Year_ID
    INNER JOIN C_Period pr 
        ON pr.C_Year_ID = cur.C_Year_ID
    WHERE ci.AD_Client_ID = p.AD_Client_ID
    AND ci.IsActive = 'Y'
    AND cur.IsActive = 'Y'
    AND pr.IsActive = 'Y'
    AND " + GetCurrentDateSql() + @" BETWEEN cur.StartDate AND cur.EndDate
)
AND p.DateTrx < " + GetTomorrowDateSql();
            }

            if (dateFilter == "MONTH")
            {
                return @"
AND p.DateTrx >= (
    SELECT cur.StartDate
    FROM AD_ClientInfo ci
    INNER JOIN C_Year yr 
        ON yr.C_Calendar_ID = ci.C_Calendar_ID
    INNER JOIN C_Period cur 
        ON cur.C_Year_ID = yr.C_Year_ID
    WHERE ci.AD_Client_ID = p.AD_Client_ID
    AND ci.IsActive = 'Y'
    AND cur.IsActive = 'Y'
    AND " + GetCurrentDateSql() + @" BETWEEN cur.StartDate AND cur.EndDate
)
AND p.DateTrx <= (
    SELECT cur.EndDate
    FROM AD_ClientInfo ci
    INNER JOIN C_Year yr 
        ON yr.C_Calendar_ID = ci.C_Calendar_ID
    INNER JOIN C_Period cur 
        ON cur.C_Year_ID = yr.C_Year_ID
    WHERE ci.AD_Client_ID = p.AD_Client_ID
    AND ci.IsActive = 'Y'
    AND cur.IsActive = 'Y'
    AND " + GetCurrentDateSql() + @" BETWEEN cur.StartDate AND cur.EndDate
)";
            }

            return @"
AND p.DateTrx >= " + GetCurrentDateSql() + @"
AND p.DateTrx < " + GetNext7DateSql();
        }

        private string GetCurrentDateSql()
        {
            return GetDateValue(DateTime.Today);
        }

        private string GetTomorrowDateSql()
        {
            return GetDateValue(DateTime.Today.AddDays(1));
        }

        private string GetNext7DateSql()
        {
            return GetDateValue(DateTime.Today.AddDays(7));
        }

        private string GetDateValue(DateTime date)
        {
            string dateText = date.ToString("yyyy-MM-dd");

            if (DB.IsOracle())
            {
                return "TO_DATE('" + dateText + "', 'YYYY-MM-DD')";
            }

            return "'" + dateText + "'";
        }

        private string GetPeriodText(Ctx ctx, string dateFilter)
        {
            dateFilter = string.IsNullOrEmpty(dateFilter)
                ? "NEXT7D"
                : dateFilter.ToUpperInvariant();

            if (dateFilter == "YTD")
            {
                return GetMsg(ctx, "VAS_033_MessageYTD", "YTD");
            }

            if (dateFilter == "MONTH")
            {
                return GetMsg(ctx, "VAS_033_MessageMonth", "MONTH");
            }

            return GetMsg(ctx, "VAS_033_MessageNext7Days", "NEXT 7 DAYS");
        }

        private string GetPaymentCountText(Ctx ctx, int paymentCount)
        {
            if (paymentCount == 1)
            {
                return paymentCount + " " + GetMsg(ctx, "VAS_033_MessagePayment", "payment");
            }

            return paymentCount + " " + GetMsg(ctx, "VAS_033_MessagePayments", "payments");
        }

        private string GetPaymentMethodName(Ctx ctx, string paymentMethodName)
        {
            if (string.IsNullOrEmpty(paymentMethodName))
            {
                return GetMsg(ctx, "VAS_032_MessageNotSpecified", "Not Specified");
            }

            return paymentMethodName;
        }

        private string FormatDate(DateTime? date)
        {
            if (date == null)
            {
                return string.Empty;
            }

            return date.Value.ToString("yyyy-MM-dd");
        }

        private string FormatRunDate(DateTime? date)
        {
            if (date == null)
            {
                return string.Empty;
            }

            return date.Value.ToString("ddd dd MMM", CultureInfo.InvariantCulture);
        }

        private string GetMsg(Ctx ctx, string key, string fallback)
        {
            string msg = Msg.GetMsg(ctx, key);

            return !string.IsNullOrEmpty(msg) && msg != "[" + key + "]"
                ? msg
                : fallback;
        }
    }
}
