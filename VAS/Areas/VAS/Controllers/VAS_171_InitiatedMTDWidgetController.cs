using Newtonsoft.Json;
using System;
using System.Data;
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
                    asOf = today.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture)
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
