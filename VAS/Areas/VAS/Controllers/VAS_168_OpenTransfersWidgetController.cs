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
    /// Module Name : Open Transfers KPI Widget (Material Transfer dashboard)
    /// Purpose     : KPI = COUNT(DISTINCT M_Movement_ID) of approved stock transfer documents
    ///               awaiting dispatch (DocStatus IN ('IP', 'WC', 'IN')). Drafts ('DR')
    ///               are excluded per §4 tie-breaker rule.
    /// ID Prefix   : VAS_168_
    /// </summary>
    public class VAS_168_OpenTransfersWidgetController : Controller
    {
        // Excludes 'DR' (Drafted) per spec section 4 tie-breaker rule
        private const string OpenStatusInList = "'IP', 'WC', 'IN'";

        /// <summary>
        /// Gets count of open outbound stock transfers awaiting dispatch.
        /// </summary>
        /// <returns>JSON { count, asOf }</returns>
        [AjaxAuthorizeAttribute]
        [AjaxSessionFilterAttribute]
        public JsonResult GetOpenTransfersData()
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

            string sql = @"
                SELECT COUNT(DISTINCT MMovement.M_Movement_ID) AS Open_Transfer_Count
                FROM M_Movement MMovement
                WHERE MMovement.IsActive = 'Y'
                  AND MMovement.DocStatus IN (" + OpenStatusInList + @")";

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
                    count = Util.GetValueOfInt(dr["Open_Transfer_Count"]);
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
                    dr = null;
                }
            }
        }
    }
}
