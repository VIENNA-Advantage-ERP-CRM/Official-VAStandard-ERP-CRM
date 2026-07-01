using Newtonsoft.Json;

/*
 * Auto Allocated AP Payment Widget Controller
 *
 * Labels / Message Keys
 * #  | Current Text                       | Message Key
 * ---+------------------------------------+--------------------------------
 * 1  | Session Expired                    | SessionExpired
 * 2  | Current Financial Period           | VAS_CurrentFinancialPeriod
 * 3  | Current Financial Period Not Found | VAS_CurrentPeriodNotFound
 * 4  | Unable To Load Payment Information | VAS_PaymentLoadError
 * 5  | Allocated                          | VAS_Allocated
 * 6  | Unallocated                        | VAS_Unallocated
 */

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
                return Json(
                    new
                    {
                        success = false,
                        error = Msg.GetMsg(
                            Env.GetCtx(),
                            "SessionExpired"
                        ) ?? "Session Expired",
                        hasData = false
                    },
                    JsonRequestBehavior.AllowGet
                );
            }

            Ctx ctx = Session["ctx"] as Ctx;
            IDataReader dr = null;

            try
            {
                List<SqlParameter> parameters =
                    new List<SqlParameter>
                    {
                        new SqlParameter(
                            "@AD_Client_ID",
                            ctx.GetAD_Client_ID()
                        )
                    };

                string securedPaymentsSql = @"
                    SELECT Payment.C_Payment_ID,
                           Payment.AD_Client_ID,
                           Payment.AD_Org_ID,
                           Payment.DateAcct,
                           Payment.DocumentNo,
                           Payment.C_BPartner_ID,
                           Payment.C_BankAccount_ID,
                           Payment.C_Currency_ID,
                           Payment.PayAmt,
                           Payment.IsAllocated
                    FROM C_Payment Payment
                    WHERE Payment.IsReceipt = 'N'
                    AND Payment.IsActive = 'Y'
                    AND Payment.DocStatus IN ('CO', 'CL')";

                securedPaymentsSql = MRole.GetDefault(ctx).AddAccessSQL(
                    securedPaymentsSql,
                    "Payment",
                    MRole.SQL_FULLYQUALIFIED,
                    MRole.SQL_RO
                );

                string queryParametersSql;

                if (DB.IsOracle())
                {
                    queryParametersSql = @"
                        SELECT @AD_Client_ID AS AD_Client_ID
                        FROM DUAL";
                }
                else
                {
                    queryParametersSql = @"
                        SELECT @AD_Client_ID AS AD_Client_ID";
                }

                string currentDateExpression = DB.IsOracle()
                    ? "TRUNC(CURRENT_DATE)"
                    : "CURRENT_DATE";

                string periodEndExclusiveExpression = DB.IsOracle()
                    ? "Period.EndDate + 1"
                    : "Period.EndDate + INTERVAL '1 day'";

                string currentPeriodEndExclusiveExpression = DB.IsOracle()
                    ? "CurrentPeriod.DateTo + 1"
                    : "CurrentPeriod.DateTo + INTERVAL '1 day'";

                string percentageExpression;

                if (DB.IsOracle())
                {
                    percentageExpression = @"
                        ROUND(
                            CAST(
                                COALESCE(
                                    SUM(PaymentsData.Is_Matched),
                                    0
                                )
                                AS NUMBER
                            )
                            * 100
                            / COUNT(1),
                            2
                        )";
                }
                else
                {
                    percentageExpression = @"
                        ROUND(
                            CAST(
                                COALESCE(
                                    SUM(PaymentsData.Is_Matched),
                                    0
                                )
                                * 100.0
                                / COUNT(1)
                                AS NUMERIC
                            ),
                            2
                        )";
                }

                string sql = @"
                    WITH QueryParameters AS
                    (
                        " + queryParametersSql + @"
                    ),
                    CurrentPeriodCandidates AS
                    (
                        SELECT Period.C_Period_ID,
                               Period.StartDate AS DateFrom,
                               Period.EndDate AS DateTo,
                               ROW_NUMBER() OVER
                               (
                                   ORDER BY Period.StartDate DESC,
                                            Period.C_Period_ID DESC
                               ) AS PeriodRowNumber
                        FROM AD_ClientInfo ClientInfo
                        INNER JOIN C_Year YearInfo ON
                        (
                            YearInfo.C_Calendar_ID =
                            ClientInfo.C_Calendar_ID
                        )
                        INNER JOIN C_Period Period ON
                        (
                            Period.C_Year_ID =
                            YearInfo.C_Year_ID
                        )
                        INNER JOIN QueryParameters QueryParameters ON
                        (
                            QueryParameters.AD_Client_ID =
                            ClientInfo.AD_Client_ID
                        )
                        WHERE ClientInfo.IsActive = 'Y'
                        AND YearInfo.IsActive = 'Y'
                        AND Period.IsActive = 'Y'
                        AND " + currentDateExpression + @" >=
                            Period.StartDate
                        AND " + currentDateExpression + @" <
                            " + periodEndExclusiveExpression + @"
                    ),
                    CurrentPeriod AS
                    (
                        SELECT CurrentPeriodCandidates.C_Period_ID,
                               CurrentPeriodCandidates.DateFrom,
                               CurrentPeriodCandidates.DateTo
                        FROM CurrentPeriodCandidates
                        WHERE CurrentPeriodCandidates.PeriodRowNumber = 1
                    ),
                    SecuredPayments AS
                    (
                        " + securedPaymentsSql + @"
                    ),
                    PaymentsData AS
                    (
                        SELECT CASE
                                   WHEN SecuredPayments.IsAllocated = 'Y'
                                   THEN 1
                                   ELSE 0
                               END AS Is_Matched
                        FROM SecuredPayments
                        INNER JOIN CurrentPeriod ON
                        (
                            SecuredPayments.DateAcct >=
                                CurrentPeriod.DateFrom
                            AND SecuredPayments.DateAcct <
                                " + currentPeriodEndExclusiveExpression + @"
                        )
                    ),
                    PeriodStatus AS
                    (
                        SELECT COUNT(1) AS PeriodCount,
                               MIN(CurrentPeriod.DateFrom) AS DateFrom,
                               MAX(CurrentPeriod.DateTo) AS DateTo
                        FROM CurrentPeriod
                    ),
                    PaymentSummary AS
                    (
                        SELECT COUNT(1) AS TotalPayments,
                               COALESCE(
                                   SUM(PaymentsData.Is_Matched),
                                   0
                               ) AS MatchedPayments,
                               CASE
                                   WHEN COUNT(1) > 0 THEN
                                       " + percentageExpression + @"
                                   ELSE 0
                               END AS AutoAllocatedPercent
                        FROM PaymentsData
                    )
                    SELECT PeriodStatus.PeriodCount,
                           PeriodStatus.DateFrom,
                           PeriodStatus.DateTo,
                           PaymentSummary.TotalPayments,
                           PaymentSummary.MatchedPayments,
                           PaymentSummary.AutoAllocatedPercent
                    FROM PeriodStatus
                    CROSS JOIN PaymentSummary";

                int periodCount = 0;
                int totalPayments = 0;
                int matchedPayments = 0;
                decimal autoAllocatedPercent = 0;

                DateTime? dateFrom = null;
                DateTime? dateTo = null;

                dr = DB.ExecuteReader(
                    sql,
                    parameters.ToArray()
                );

                if (dr != null && dr.Read())
                {
                    periodCount = GetSafeInt(
                        dr["PeriodCount"]
                    );

                    totalPayments = GetSafeInt(
                        dr["TotalPayments"]
                    );

                    matchedPayments = GetSafeInt(
                        dr["MatchedPayments"]
                    );

                    autoAllocatedPercent = GetSafeDecimal(
                        dr["AutoAllocatedPercent"],
                        2
                    );

                    dateFrom = Util.GetValueOfDateTime(
                        dr["DateFrom"]
                    );

                    dateTo = Util.GetValueOfDateTime(
                        dr["DateTo"]
                    );
                }

                if (periodCount <= 0 ||
                    !dateFrom.HasValue ||
                    !dateTo.HasValue)
                {
                    return Json(
                        new
                        {
                            success = false,
                            error = GetMsg(
                                ctx,
                                "VAS_CurrentPeriodNotFound",
                                "Current Financial Period Not Found"
                            ),
                            hasData = false
                        },
                        JsonRequestBehavior.AllowGet
                    );
                }

                int unmatchedPayments =
                    totalPayments - matchedPayments;

                if (unmatchedPayments < 0)
                {
                    unmatchedPayments = 0;
                }

                var result = new
                {
                    success = true,
                    error = "",
                    hasData = totalPayments > 0,
                    autoAllocatedPercent = autoAllocatedPercent,
                    totalPayments = totalPayments,
                    matchedPayments = matchedPayments,
                    unmatchedPayments = unmatchedPayments,
                    fromDate = FormatDate(
                        dateFrom.Value
                    ),
                    toDate = FormatDate(
                        dateTo.Value
                    ),
                    period = GetMsg(
                        ctx,
                        "VAS_CurrentFinancialPeriod",
                        "Current Financial Period"
                    )
                };

                return Json(
                    JsonConvert.SerializeObject(result),
                    JsonRequestBehavior.AllowGet
                );
            }
            catch (Exception ex)
            {
                LogException(
                    "GetAutoAllocatedAPPayments",
                    ex
                );

                return Json(
                    new
                    {
                        success = false,
                        error = GetMsg(
                            ctx,
                            "VAS_PaymentLoadError",
                            "Unable To Load Payment Information"
                        ),
                        hasData = false
                    },
                    JsonRequestBehavior.AllowGet
                );
            }
            finally
            {
                CloseReader(dr);
            }
        }

        [AjaxAuthorizeAttribute]
        [AjaxSessionFilterAttribute]
        public JsonResult GetAutoAllocatedAPPaymentRows(
            int pageNo = 1,
            int pageSize = 10,
            string filter = "allocated"
        )
        {
            if (Session["ctx"] == null)
            {
                return Json(
                    new
                    {
                        success = false,
                        error = Msg.GetMsg(
                            Env.GetCtx(),
                            "SessionExpired"
                        ) ?? "Session Expired",
                        hasData = false
                    },
                    JsonRequestBehavior.AllowGet
                );
            }

            Ctx ctx = Session["ctx"] as Ctx;
            IDataReader dr = null;

            if (pageNo <= 0)
            {
                pageNo = 1;
            }

            if (pageSize <= 0)
            {
                pageSize = 10;
            }

            if (pageSize > 100)
            {
                pageSize = 100;
            }

            try
            {
                string filterKey =
                    string.IsNullOrWhiteSpace(filter)
                        ? "allocated"
                        : filter.Trim().ToLowerInvariant();

                int startRow =
                    ((pageNo - 1) * pageSize) + 1;

                int endRow =
                    pageNo * pageSize;

                string filterPredicate = "";

                if (filterKey == "allocated")
                {
                    filterPredicate = @"
                        AND SecuredPayments.IsAllocated = 'Y'";
                }
                else if (filterKey == "unallocated")
                {
                    filterPredicate = @"
                        AND
                        (
                            SecuredPayments.IsAllocated IS NULL
                            OR SecuredPayments.IsAllocated <> 'Y'
                        )";
                }
                else
                {
                    filterKey = "all";
                }

                List<SqlParameter> parameters =
                    new List<SqlParameter>
                    {
                        new SqlParameter(
                            "@AD_Client_ID",
                            ctx.GetAD_Client_ID()
                        ),
                        new SqlParameter(
                            "@StartRow",
                            startRow
                        ),
                        new SqlParameter(
                            "@EndRow",
                            endRow
                        )
                    };

                string securedPaymentsSql = @"
                    SELECT Payment.C_Payment_ID,
                           Payment.AD_Client_ID,
                           Payment.AD_Org_ID,
                           Payment.DateAcct,
                           Payment.DocumentNo,
                           Payment.C_BPartner_ID,
                           Payment.C_BankAccount_ID,
                           Payment.C_Currency_ID,
                           Payment.PayAmt,
                           Payment.IsAllocated
                    FROM C_Payment Payment
                    WHERE Payment.IsReceipt = 'N'
                    AND Payment.IsActive = 'Y'
                    AND Payment.DocStatus IN ('CO', 'CL')";

                securedPaymentsSql = MRole.GetDefault(ctx).AddAccessSQL(
                    securedPaymentsSql,
                    "Payment",
                    MRole.SQL_FULLYQUALIFIED,
                    MRole.SQL_RO
                );

                string queryParametersSql;

                if (DB.IsOracle())
                {
                    queryParametersSql = @"
                        SELECT @AD_Client_ID AS AD_Client_ID,
                               @StartRow AS StartRow,
                               @EndRow AS EndRow
                        FROM DUAL";
                }
                else
                {
                    queryParametersSql = @"
                        SELECT @AD_Client_ID AS AD_Client_ID,
                               @StartRow AS StartRow,
                               @EndRow AS EndRow";
                }

                string currentDateExpression = DB.IsOracle()
                    ? "TRUNC(CURRENT_DATE)"
                    : "CURRENT_DATE";

                string periodEndExclusiveExpression = DB.IsOracle()
                    ? "Period.EndDate + 1"
                    : "Period.EndDate + INTERVAL '1 day'";

                string currentPeriodEndExclusiveExpression = DB.IsOracle()
                    ? "CurrentPeriod.DateTo + 1"
                    : "CurrentPeriod.DateTo + INTERVAL '1 day'";

                string sql = @"
                    WITH QueryParameters AS
                    (
                        " + queryParametersSql + @"
                    ),
                    CurrentPeriodCandidates AS
                    (
                        SELECT Period.C_Period_ID,
                               Period.StartDate AS DateFrom,
                               Period.EndDate AS DateTo,
                               ROW_NUMBER() OVER
                               (
                                   ORDER BY Period.StartDate DESC,
                                            Period.C_Period_ID DESC
                               ) AS PeriodRowNumber
                        FROM AD_ClientInfo ClientInfo
                        INNER JOIN C_Year YearInfo ON
                        (
                            YearInfo.C_Calendar_ID =
                            ClientInfo.C_Calendar_ID
                        )
                        INNER JOIN C_Period Period ON
                        (
                            Period.C_Year_ID =
                            YearInfo.C_Year_ID
                        )
                        INNER JOIN QueryParameters QueryParameters ON
                        (
                            QueryParameters.AD_Client_ID =
                            ClientInfo.AD_Client_ID
                        )
                        WHERE ClientInfo.IsActive = 'Y'
                        AND YearInfo.IsActive = 'Y'
                        AND Period.IsActive = 'Y'
                        AND " + currentDateExpression + @" >=
                            Period.StartDate
                        AND " + currentDateExpression + @" <
                            " + periodEndExclusiveExpression + @"
                    ),
                    CurrentPeriod AS
                    (
                        SELECT CurrentPeriodCandidates.C_Period_ID,
                               CurrentPeriodCandidates.DateFrom,
                               CurrentPeriodCandidates.DateTo
                        FROM CurrentPeriodCandidates
                        WHERE CurrentPeriodCandidates.PeriodRowNumber = 1
                    ),
                    SecuredPayments AS
                    (
                        " + securedPaymentsSql + @"
                    ),
                    PaymentsData AS
                    (
                        SELECT SecuredPayments.C_Payment_ID AS Payment_ID,
                               SecuredPayments.DateAcct AS Date_Acct,
                               SecuredPayments.DocumentNo AS Document_No,
                               BPartner.Name AS Vendor_Name,
                               Bank.Name AS Bank_Name,
                               BankAccount.AccountNo AS Account_No,
                               Currency.ISO_Code AS Payment_Currency,
                               Currency.CurSymbol AS Payment_Currency_Symbol,
                               SecuredPayments.IsAllocated AS Is_Allocated,
                               SecuredPayments.PayAmt AS Pay_Amount
                        FROM SecuredPayments
                        INNER JOIN CurrentPeriod ON
                        (
                            SecuredPayments.DateAcct >=
                                CurrentPeriod.DateFrom
                            AND SecuredPayments.DateAcct <
                                " + currentPeriodEndExclusiveExpression + @"
                        )
                        LEFT OUTER JOIN C_BPartner BPartner ON
                        (
                            SecuredPayments.C_BPartner_ID =
                            BPartner.C_BPartner_ID
                        )
                        LEFT OUTER JOIN C_BankAccount BankAccount ON
                        (
                            SecuredPayments.C_BankAccount_ID =
                            BankAccount.C_BankAccount_ID
                        )
                        LEFT OUTER JOIN C_Bank Bank ON
                        (
                            BankAccount.C_Bank_ID =
                            Bank.C_Bank_ID
                        )
                        LEFT OUTER JOIN C_Currency Currency ON
                        (
                            SecuredPayments.C_Currency_ID =
                            Currency.C_Currency_ID
                        )
                        WHERE 1 = 1
                        " + filterPredicate + @"
                    ),
                    NumberedPayments AS
                    (
                        SELECT PaymentsData.Payment_ID,
                               PaymentsData.Date_Acct,
                               PaymentsData.Document_No,
                               PaymentsData.Vendor_Name,
                               PaymentsData.Bank_Name,
                               PaymentsData.Account_No,
                               PaymentsData.Payment_Currency,
                               PaymentsData.Payment_Currency_Symbol,
                               PaymentsData.Is_Allocated,
                               PaymentsData.Pay_Amount,
                               ROW_NUMBER() OVER
                               (
                                   ORDER BY PaymentsData.Date_Acct DESC,
                                            PaymentsData.Payment_ID DESC
                               ) AS RowNumber,
                               COUNT(1) OVER() AS TotalRecords
                        FROM PaymentsData
                    ),
                    PagedPayments AS
                    (
                        SELECT NumberedPayments.Payment_ID,
                               NumberedPayments.Date_Acct,
                               NumberedPayments.Document_No,
                               NumberedPayments.Vendor_Name,
                               NumberedPayments.Bank_Name,
                               NumberedPayments.Account_No,
                               NumberedPayments.Payment_Currency,
                               NumberedPayments.Payment_Currency_Symbol,
                               NumberedPayments.Is_Allocated,
                               NumberedPayments.Pay_Amount,
                               NumberedPayments.TotalRecords,
                               NumberedPayments.RowNumber
                        FROM NumberedPayments
                        CROSS JOIN QueryParameters QueryParameters
                        WHERE NumberedPayments.RowNumber >=
                            QueryParameters.StartRow
                        AND NumberedPayments.RowNumber <=
                            QueryParameters.EndRow
                    ),
                    PeriodStatus AS
                    (
                        SELECT COUNT(1) AS PeriodCount,
                               MIN(CurrentPeriod.DateFrom) AS DateFrom,
                               MAX(CurrentPeriod.DateTo) AS DateTo
                        FROM CurrentPeriod
                    )
                    SELECT PagedPayments.Payment_ID,
                           PagedPayments.Date_Acct,
                           PagedPayments.Document_No,
                           PagedPayments.Vendor_Name,
                           PagedPayments.Bank_Name,
                           PagedPayments.Account_No,
                           PagedPayments.Payment_Currency,
                           PagedPayments.Payment_Currency_Symbol,
                           PagedPayments.Is_Allocated,
                           PagedPayments.Pay_Amount,
                           PagedPayments.TotalRecords,
                           PagedPayments.RowNumber,
                           PeriodStatus.PeriodCount,
                           PeriodStatus.DateFrom,
                           PeriodStatus.DateTo
                    FROM PeriodStatus
                    LEFT OUTER JOIN PagedPayments ON
                    (
                        PeriodStatus.PeriodCount > 0
                    )
                    ORDER BY PagedPayments.RowNumber";

                List<object> rows =
                    new List<object>();

                int totalRecords = 0;
                int periodCount = 0;

                DateTime? periodDateFrom = null;
                DateTime? periodDateTo = null;

                dr = DB.ExecuteReader(
                    sql,
                    parameters.ToArray()
                );

                while (dr != null && dr.Read())
                {
                    periodCount = GetSafeInt(
                        dr["PeriodCount"]
                    );

                    if (!periodDateFrom.HasValue)
                    {
                        periodDateFrom =
                            Util.GetValueOfDateTime(
                                dr["DateFrom"]
                            );
                    }

                    if (!periodDateTo.HasValue)
                    {
                        periodDateTo =
                            Util.GetValueOfDateTime(
                                dr["DateTo"]
                            );
                    }

                    if (dr["Payment_ID"] == null ||
                        dr["Payment_ID"] == DBNull.Value)
                    {
                        continue;
                    }

                    totalRecords = GetSafeInt(
                        dr["TotalRecords"]
                    );

                    DateTime? dateAcct =
                        Util.GetValueOfDateTime(
                            dr["Date_Acct"]
                        );

                    string isAllocated =
                        Util.GetValueOfString(
                            dr["Is_Allocated"]
                        );

                    string statusName =
                        isAllocated == "Y"
                            ? GetMsg(
                                ctx,
                                "VAS_Allocated",
                                "Allocated"
                            )
                            : GetMsg(
                                ctx,
                                "VAS_Unallocated",
                                "Unallocated"
                            );

                    rows.Add(
                        new
                        {
                            paymentId = GetSafeInt(
                                dr["Payment_ID"]
                            ),
                            date = dateAcct.HasValue
                                ? FormatDate(dateAcct.Value)
                                : "",
                            documentNo =
                                Util.GetValueOfString(
                                    dr["Document_No"]
                                ),
                            vendor =
                                Util.GetValueOfString(
                                    dr["Vendor_Name"]
                                ),
                            bankName =
                                Util.GetValueOfString(
                                    dr["Bank_Name"]
                                ),
                            accountNo =
                                Util.GetValueOfString(
                                    dr["Account_No"]
                                ),
                            paymentCurrency =
                                Util.GetValueOfString(
                                    dr["Payment_Currency"]
                                ),
                            paymentCurrencySymbol =
                                Util.GetValueOfString(
                                    dr["Payment_Currency_Symbol"]
                                ),
                            matched =
                                isAllocated == "Y",
                            statusValue =
                                isAllocated,
                            statusName =
                                statusName,
                            amount = GetSafeDecimal(
                                dr["Pay_Amount"],
                                GetStandardPrecision(ctx)
                            )
                        }
                    );
                }

                if (periodCount <= 0 ||
                    !periodDateFrom.HasValue ||
                    !periodDateTo.HasValue)
                {
                    return Json(
                        new
                        {
                            success = false,
                            error = GetMsg(
                                ctx,
                                "VAS_CurrentPeriodNotFound",
                                "Current Financial Period Not Found"
                            ),
                            hasData = false
                        },
                        JsonRequestBehavior.AllowGet
                    );
                }

                int totalPages =
                    totalRecords <= 0
                        ? 0
                        : Convert.ToInt32(
                            Math.Ceiling(
                                (decimal)totalRecords /
                                pageSize
                            )
                        );

                var result = new
                {
                    success = true,
                    error = "",
                    hasData = rows.Count > 0,
                    rows = rows,
                    pageNo = pageNo,
                    pageSize = pageSize,
                    totalRecords = totalRecords,
                    totalPages = totalPages,
                    filter = filterKey,
                    fromDate = FormatDate(
                        periodDateFrom.Value
                    ),
                    toDate = FormatDate(
                        periodDateTo.Value
                    ),
                    period = GetMsg(
                        ctx,
                        "VAS_CurrentFinancialPeriod",
                        "Current Financial Period"
                    )
                };

                return Json(
                    JsonConvert.SerializeObject(result),
                    JsonRequestBehavior.AllowGet
                );
            }
            catch (Exception ex)
            {
                LogException(
                    "GetAutoAllocatedAPPaymentRows",
                    ex
                );

                return Json(
                    new
                    {
                        success = false,
                        error = GetMsg(
                            ctx,
                            "VAS_PaymentLoadError",
                            "Unable To Load Payment Information"
                        ),
                        hasData = false
                    },
                    JsonRequestBehavior.AllowGet
                );
            }
            finally
            {
                CloseReader(dr);
            }
        }

        private string FormatDate(DateTime date)
        {
            return date.ToString(
                "yyyy-MM-dd",
                CultureInfo.InvariantCulture
            );
        }

        private int GetStandardPrecision(Ctx ctx)
        {
            int precision = 2;

            try
            {
                precision = ctx.GetStdPrecision();
            }
            catch
            {
                precision = 2;
            }

            if (precision < 0)
            {
                precision = 2;
            }

            return precision;
        }

        private int GetSafeInt(object value)
        {
            if (value == null ||
                value == DBNull.Value)
            {
                return 0;
            }

            int result;

            if (int.TryParse(
                Convert.ToString(
                    value,
                    CultureInfo.InvariantCulture
                ),
                NumberStyles.Any,
                CultureInfo.InvariantCulture,
                out result
            ))
            {
                return result;
            }

            return 0;
        }

        private decimal GetSafeDecimal(
            object value,
            int precision
        )
        {
            if (value == null ||
                value == DBNull.Value)
            {
                return 0;
            }

            if (precision < 0)
            {
                precision = 2;
            }

            string valueText =
                Convert.ToString(
                    value,
                    CultureInfo.InvariantCulture
                );

            decimal decimalValue;

            if (decimal.TryParse(
                valueText,
                NumberStyles.Any,
                CultureInfo.InvariantCulture,
                out decimalValue
            ))
            {
                return Math.Round(
                    decimalValue,
                    precision,
                    MidpointRounding.AwayFromZero
                );
            }

            double doubleValue;

            if (double.TryParse(
                valueText,
                NumberStyles.Any,
                CultureInfo.InvariantCulture,
                out doubleValue
            ))
            {
                try
                {
                    double roundedValue =
                        Math.Round(
                            doubleValue,
                            precision,
                            MidpointRounding.AwayFromZero
                        );

                    return Convert.ToDecimal(
                        roundedValue,
                        CultureInfo.InvariantCulture
                    );
                }
                catch (OverflowException)
                {
                    return 0;
                }
                catch (FormatException)
                {
                    return 0;
                }
                catch (InvalidCastException)
                {
                    return 0;
                }
            }

            return 0;
        }

        private void CloseReader(IDataReader dr)
        {
            if (dr == null)
            {
                return;
            }

            dr.Close();
            dr.Dispose();
        }

        private void LogException(
            string methodName,
            Exception ex
        )
        {
            try
            {
                System.Diagnostics.Trace.TraceError(
                    "VAS_056_AutoAllocatedAPPaymentWidgetController."
                    + methodName
                    + ": "
                    + ex
                );
            }
            catch
            {
                // Logging must not interrupt the JSON response.
            }
        }

        private string GetMsg(
            Ctx ctx,
            string key,
            string fallback
        )
        {
            string msg = Msg.GetMsg(
                ctx,
                key
            );

            if (string.IsNullOrEmpty(msg) ||
                msg == key ||
                msg == "[" + key + "]")
            {
                return fallback;
            }

            return msg;
        }
    }
}