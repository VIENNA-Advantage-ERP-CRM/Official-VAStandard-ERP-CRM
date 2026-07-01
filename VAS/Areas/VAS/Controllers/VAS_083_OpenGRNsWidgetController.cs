using Newtonsoft.Json;
using System;
using System.Globalization;
using System.Data;
using System.Web.Mvc;
using VAdvantage.Classes;
using VAdvantage.DataBase;
using VAdvantage.Model;
using VAdvantage.Utility;
using VIS.Filters;

namespace VIS.Controllers
{
    /// <summary>
    /// Module Name : Open GRNs till Today (Material Receipt / GRN dashboard KPI)
    /// Purpose     : KPI = COUNT(DISTINCT M_InOut.M_InOut_ID) of vendor Goods
    ///               Receipt Notes that are still OPEN as of the server date.
    ///               "Open" = a non-terminal, non-draft DocStatus
    ///               (AP, IN, IP, NA, WC, WP). Terminal (CO, CL, RE, VO) and
    ///               draft (DR) GRNs are excluded. The header count never joins
    ///               M_InOutLine. Tenant/organization scope comes from MRole,
    ///               applied to the single physical table M_InOut.
    /// Chronological development:
    ///   &lt;EmpCode&gt;   2026-06-17 Created
    /// </summary>
    public class VAS_083_OpenGRNsWidgetController : Controller
    {
        /* Open-GRN DocStatus set (non-terminal, non-draft). These are ASCII
           literals, so plain quoted values are portable across Oracle/PostgreSQL. */
        private const string OpenStatusInList = "'AP', 'IN', 'IP', 'NA', 'WC', 'WP'";

        /// <summary>
        /// KPI tile data: the count of open vendor GRNs whose MovementDate is on
        /// or before the server's current date, within the user's role scope.
        /// </summary>
        /// <returns>
        /// JSON { count, asOf, filterDateSql, scopeLabel } where filterDateSql is a
        /// dialect-correct date literal the client reuses to build the drill-through
        /// filter so the opened GRN list matches the counted predicate exactly.
        /// </returns>
        [AjaxAuthorizeAttribute]
        [AjaxSessionFilterAttribute]
        public JsonResult GetOpenGRNCount()
        {
            if (Session["ctx"] == null)
            {
                return Json(new
                {
                    error = Msg.GetMsg(Env.GetCtx(), "SessionExpired") ?? "Session Expired"
                }, JsonRequestBehavior.AllowGet);
            }

            Ctx ctx = Session["ctx"] as Ctx;

            DateTime today = DateTime.Today;
            /* Count "on or before today" robustly across a date column that may carry
               a time component: TRUNC(date) < tomorrow == date <= today. */
            DateTime nextDayStart = today.AddDays(1);
            string nextDayLiteral = ToSqlDate(nextDayStart);

            string sql = @"
                SELECT COUNT(DISTINCT MInOut.M_InOut_ID) AS Open_GRN_Count
                FROM M_InOut MInOut
                WHERE MInOut.IsActive = 'Y'
                  AND MInOut.IsSOTrx = 'N'
                  AND MInOut.MovementType = 'V+'
                  AND MInOut.DocStatus IN (" + OpenStatusInList + @")
                  AND " + TruncColumn("MInOut.MovementDate") + @" < " + nextDayLiteral;

            /* MRole supplies tenant + organization access on the only physical table.
               Applied to the main table alias (notation + CTE rules in the shared
               Prompt_Instructions). */
            sql = MRole.GetDefault(ctx).AddAccessSQL(
                sql,
                "MInOut",
                MRole.SQL_FULLYQUALIFIED,
                MRole.SQL_RO
            );

            IDataReader dr = null;

            try
            {
                int count = 0;

                dr = DB.ExecuteReader(sql);
                if (dr != null && dr.Read())
                {
                    count = Util.GetValueOfInt(dr["Open_GRN_Count"]);
                }

                var result = new
                {
                    count = count,
                    asOf = today.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture),
                    /* Dialect-correct literal for "< tomorrow" reused by the client
                       drill-through filter (so the list equals the count). */
                    filterDateSql = nextDayLiteral,
                    scopeLabel = ""
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
        /// Wraps a date column in TRUNC on Oracle so the day comparison ignores the
        /// time component; other dialects compare the column directly.
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
