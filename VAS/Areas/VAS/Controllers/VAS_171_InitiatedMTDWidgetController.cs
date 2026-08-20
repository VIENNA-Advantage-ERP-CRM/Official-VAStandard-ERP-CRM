using Newtonsoft.Json;
using System;
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
    /// <summary>
    /// Module Name : Transfer Initiated MTD KPI Widget (Material Transfer dashboard)
    /// Purpose     : KPI = COUNT(DISTINCT M_Movement_ID) of stock transfer documents
    ///               created in the current calendar month to date.
    ///               Cancelled transfers (DocStatus IN ('VO','RE')) are excluded to
    ///               mirror how the existing Material Transfer register treats cancelled
    ///               records — they are not counted as operational workload.
    /// ID Prefix   : VAS_171_
    /// </summary>
    public class VAS_171_InitiatedMTDWidgetController : Controller
    {
        /// <summary>
        /// Excluded statuses — cancelled/voided are not workload events per the
        /// Material Transfer module's reporting convention.
        /// </summary>
        private const string ExcludedStatusInList = "'VO', 'RE'";

        /// <summary>
        /// Gets count of stock transfers initiated in the current calendar month.
        /// </summary>
        /// <returns>JSON { count, asOf }</returns>
// ===== NEW CODE START — currency format (agent C05, 2026-08-19) =====
        /// <summary>
        /// Gets count of stock transfers initiated in the current calendar month.
        /// </summary>
        /// <returns>JSON { count, asOf, currency }</returns>
        [AjaxAuthorizeAttribute]
        [AjaxSessionFilterAttribute]
        public JsonResult GetInitiatedMTDData()
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

            // First day of current month
            DateTime monthStart = new DateTime(today.Year, today.Month, 1);
            // Day after today (exclusive upper bound — same as "< tomorrow")
            DateTime nextDayStart = today.AddDays(1);

            string monthStartLiteral = ToSqlDate(monthStart);
            string nextDayLiteral = ToSqlDate(nextDayStart);

            string sql = @"
                SELECT COUNT(DISTINCT MMovement.M_Movement_ID) AS Initiated_MTD_Count
                FROM M_Movement MMovement
                WHERE MMovement.IsActive = 'Y'
                  AND MMovement.DocStatus NOT IN (" + ExcludedStatusInList + @")
                  AND " + TruncColumn("MMovement.Created") + @" >= " + monthStartLiteral + @"
                  AND " + TruncColumn("MMovement.Created") + @" < " + nextDayLiteral;

            sql = MRole.GetDefault(ctx).AddAccessSQL(
                sql,
                "MMovement",
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
                    count = Util.GetValueOfInt(dr["Initiated_MTD_Count"]);
                }

                var result = new
                {
                    count = count,
                    asOf = today.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture),
                    currency = GetCurrencyInfo(ctx)
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
                    dr = null;
                }
            }
        }

        /// <summary>
        /// Retrieves currency information (ISO code and symbol) based on $C_Currency_ID or C_AcctSchema fallback.
        /// </summary>
        private object GetCurrencyInfo(Ctx ctx)
        {
            string iso = "";
            string symbol = "";
            int currencyId = ctx.GetContextAsInt("$C_Currency_ID");
            if (currencyId <= 0)
            {
                string fallbackSql = @"SELECT AcctSchema.C_Currency_ID
                                       FROM AD_ClientInfo ClientInfo
                                       INNER JOIN C_AcctSchema AcctSchema ON (AcctSchema.C_AcctSchema_ID = ClientInfo.C_AcctSchema1_ID AND AcctSchema.IsActive = 'Y')
                                       WHERE ClientInfo.AD_Client_ID = @AD_Client_ID";
                currencyId = Util.GetValueOfInt(DB.ExecuteScalar(fallbackSql, new SqlParameter[] { new SqlParameter("@AD_Client_ID", ctx.GetAD_Client_ID()) }, null));
            }

            if (currencyId > 0)
            {
                IDataReader cdr = null;
                try
                {
                    cdr = DB.ExecuteReader(
                        "SELECT ISO_Code, CurSymbol FROM C_Currency WHERE C_Currency_ID = @Cur AND IsActive = 'Y'",
                        new SqlParameter[] { new SqlParameter("@Cur", currencyId) });
                    if (cdr != null && cdr.Read())
                    {
                        iso = Util.GetValueOfString(cdr["ISO_Code"]);
                        symbol = Util.GetValueOfString(cdr["CurSymbol"]);
                    }
                }
                finally { if (cdr != null) { cdr.Close(); cdr.Dispose(); } }
            }
            return new { iso = iso, symbol = symbol };
        }
// ===== NEW CODE END — currency format =====
// ----- OLD CODE (kept for rollback, do not delete) -----
//        [AjaxAuthorizeAttribute]
//        [AjaxSessionFilterAttribute]
//        public JsonResult GetInitiatedMTDData()
//        {
//            if (Session["ctx"] == null)
//            {
//                return Json(new
//                {
//                    error = Msg.GetMsg(Env.GetCtx(), "SessionExpired") ?? "Session Expired"
//                }, JsonRequestBehavior.AllowGet);
//            }
//
//            Ctx ctx = Session["ctx"] as Ctx;
//            DateTime today = DateTime.Today;
//
//            // First day of current month
//            DateTime monthStart = new DateTime(today.Year, today.Month, 1);
//            // Day after today (exclusive upper bound — same as "< tomorrow")
//            DateTime nextDayStart = today.AddDays(1);
//
//            string monthStartLiteral = ToSqlDate(monthStart);
//            string nextDayLiteral = ToSqlDate(nextDayStart);
//
//            string sql = @"
//                SELECT COUNT(DISTINCT MMovement.M_Movement_ID) AS Initiated_MTD_Count
//                FROM M_Movement MMovement
//                WHERE MMovement.IsActive = 'Y'
//                  AND MMovement.DocStatus NOT IN (" + ExcludedStatusInList + @")
//                  AND " + TruncColumn("MMovement.Created") + @" >= " + monthStartLiteral + @"
//                  AND " + TruncColumn("MMovement.Created") + @" < " + nextDayLiteral;
//
//            sql = MRole.GetDefault(ctx).AddAccessSQL(
//                sql,
//                "MMovement",
//                MRole.SQL_FULLYQUALIFIED,
//                MRole.SQL_RO
//            );
//
//            IDataReader dr = null;
//
//            try
//            {
//                int count = 0;
//
//                dr = DB.ExecuteReader(sql);
//                if (dr != null && dr.Read())
//                {
//                    count = Util.GetValueOfInt(dr["Initiated_MTD_Count"]);
//                }
//
//                var result = new
//                {
//                    count = count,
//                    asOf = today.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture)
//                };
//
//                return Json(JsonConvert.SerializeObject(result), JsonRequestBehavior.AllowGet);
//            }
//            catch (Exception ex)
//            {
//                return Json(new { error = ex.Message }, JsonRequestBehavior.AllowGet);
//            }
//            finally
//            {
//                if (dr != null)
//                {
//                    dr.Close();
//                    dr = null;
//                }
//            }
//        }
// ----- END OLD CODE -----

        private static string ToSqlDate(DateTime date)
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

        private static string TruncColumn(string columnExpression)
        {
            if (DB.IsOracle())
            {
                return "TRUNC(" + columnExpression + ")";
            }
            return columnExpression;
        }
    }
}
