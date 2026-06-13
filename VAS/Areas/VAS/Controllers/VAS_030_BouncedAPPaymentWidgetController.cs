using System;
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
    /// <summary>
    /// Module Name : VAS Dashboard
    /// Purpose     : Provides bounced AP payment KPI widget data.
    /// Chronological development:
    ///   <EmployeeCode>   Created Date
    /// </summary>
    /*
     * Labels / Message Keys
     * 1 | Bounced       | VAS_030_MessageBounced
     * 2 | Action        | VAS_030_MessageAction
     * 3 | Need re-issue | VAS_030_MessageNeedReissue
     */
    public class VAS_030_BouncedAPPaymentWidgetController : Controller
    {
        /// <summary>
        /// Gets outgoing AP payments marked as bounced or rejected during the current financial period.
        /// Period is based on C_Period calendar linked with AD_ClientInfo.
        /// </summary>
        /// <returns>Bounced AP payment count and reporting date range.</returns>
        [AjaxAuthorizeAttribute]
        [AjaxSessionFilterAttribute]
        public JsonResult GetBouncedAPPayments()
        {
            if (Session["ctx"] == null)
            {
                return Json(new
                {
                    error = true,
                    errorText = "Session Expired"
                }, JsonRequestBehavior.AllowGet);
            }

            Ctx ctx = Session["ctx"] as Ctx;

            if (ctx == null)
            {
                return Json(new
                {
                    error = true,
                    errorText = "Session Expired"
                }, JsonRequestBehavior.AllowGet);
            }

            IDataReader dr = null;

            try
            {
                SqlQueryData queryData = BuildBouncedAPPaymentsSql(ctx);

                dr = DB.ExecuteReader(queryData.Sql, queryData.Parameters, null);

                int bouncedPaymentCount = 0;
                DateTime? dateFrom = null;
                DateTime? dateTo = null;

                if (dr != null && dr.Read())
                {
                    bouncedPaymentCount = Util.GetValueOfInt(dr["BouncedPaymentCount"]);

                    if (dr["DateFrom"] != DBNull.Value)
                    {
                        dateFrom = Util.GetValueOfDateTime(dr["DateFrom"]);
                    }

                    if (dr["DateTo"] != DBNull.Value)
                    {
                        dateTo = Util.GetValueOfDateTime(dr["DateTo"]);
                    }
                }

                return Json(new
                {
                    title = GetMsg(ctx, "VAS_030_MessageBounced", "Bounced"),
                    badge = GetMsg(ctx, "VAS_030_MessageAction", "Action"),
                    description = GetMsg(ctx, "VAS_030_MessageNeedReissue", "Need re-issue"),
                    value = bouncedPaymentCount,
                    bouncedPaymentCount = bouncedPaymentCount,
                    dateFrom = dateFrom.HasValue ? FormatDate(dateFrom.Value) : "",
                    dateTo = dateTo.HasValue ? FormatDate(dateTo.Value) : ""
                }, JsonRequestBehavior.AllowGet);
            }
            catch (Exception ex)
            {
                return Json(new
                {
                    error = true,
                    errorText = ex.Message
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

        private SqlQueryData BuildBouncedAPPaymentsSql(Ctx ctx)
        {
            string bouncedStatusFilter = HasPaymentExecutionStatusColumn()
                ? "AND Payment.VA009_ExecutionStatus IN ('" + X_C_Payment.VA009_EXECUTIONSTATUS_Bounced + "', '" + X_C_Payment.VA009_EXECUTIONSTATUS_Rejected + "')"
                : "AND 1 = 2";

            string periodRangeSql = @"
PeriodRange AS
(
SELECT
MIN(Period.StartDate) AS DateFrom,
MAX(Period.EndDate) AS DateTo,
MAX(CAST(Period.EndDate AS TIMESTAMP) + INTERVAL '1' DAY) AS DateToExclusive
FROM AD_ClientInfo ClientInfo
INNER JOIN C_Year YearData ON (YearData.C_Calendar_ID = ClientInfo.C_Calendar_ID)
INNER JOIN C_Period Period ON (Period.C_Year_ID = YearData.C_Year_ID)
WHERE ClientInfo.IsActive = 'Y'
AND ClientInfo.AD_Client_ID = @AD_Client_ID
AND CAST(CURRENT_TIMESTAMP AS TIMESTAMP) >= CAST(Period.StartDate AS TIMESTAMP)
AND CAST(CURRENT_TIMESTAMP AS TIMESTAMP) < CAST(Period.EndDate AS TIMESTAMP) + INTERVAL '1' DAY
)";

            string paymentAccessSql = @"
SELECT
Payment.C_Payment_ID,
Payment.DateAcct
FROM C_Payment Payment
WHERE Payment.IsActive = 'Y'
AND Payment.IsReceipt = 'N'
" + bouncedStatusFilter;

            /*
             * MRole Handling:
             * Apply MRole only on the main physical table C_Payment Payment.
             * Do not apply MRole on the final WITH query.
             * Do not apply MRole on CTE aliases.
             * Do not apply MRole on joined aliases.
             */
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

            string sql = @"
WITH " + periodRangeSql + @",
" + paymentFilteredSql + @"
SELECT
COUNT(DISTINCT Payment.C_Payment_ID) AS BouncedPaymentCount,
MIN(PeriodRange.DateFrom) AS DateFrom,
MAX(PeriodRange.DateTo) AS DateTo
FROM PaymentFiltered Payment
INNER JOIN PeriodRange ON (
    Payment.DateAcct >= PeriodRange.DateFrom
    AND Payment.DateAcct < PeriodRange.DateToExclusive
)";

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

        private bool HasPaymentExecutionStatusColumn()
        {
            string sql = @"
SELECT
COUNT(1)
FROM AD_Table TableData
INNER JOIN AD_Column ColumnData ON (TableData.AD_Table_ID = ColumnData.AD_Table_ID)
WHERE TableData.TableName = 'C_Payment'
AND ColumnData.ColumnName = 'VA009_ExecutionStatus'";

            return Util.GetValueOfInt(DB.ExecuteScalar(sql)) > 0;
        }

        /// <summary>
        /// Gets translated message text by key with fallback.
        /// </summary>
        /// <param name="ctx">User context.</param>
        /// <param name="key">Message key.</param>
        /// <param name="fallback">Fallback text.</param>
        /// <returns>Translated or fallback message text.</returns>
        private string GetMsg(Ctx ctx, string key, string fallback)
        {
            string msg = Msg.GetMsg(ctx, key);

            return !string.IsNullOrEmpty(msg) && msg != "[" + key + "]"
                ? msg
                : fallback;
        }

        /// <summary>
        /// Formats date values returned to the widget.
        /// </summary>
        /// <param name="date">Date value.</param>
        /// <returns>Date formatted as yyyy-MM-dd.</returns>
        private string FormatDate(DateTime date)
        {
            return date.ToString("yyyy-MM-dd");
        }

        private class SqlQueryData
        {
            public string Sql { get; set; }
            public SqlParameter[] Parameters { get; set; }
        }
    }
}