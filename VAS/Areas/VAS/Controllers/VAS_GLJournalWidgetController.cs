using CoreLibrary.DataBase;
using Newtonsoft.Json;
using System;
using System.Data;
using System.Data.SqlClient;
using System.Web.Mvc;
using VAdvantage.Model;
using VAdvantage.Utility;

namespace VAS.Controllers
{
    public class VAS_GLJournalWidgetController : Controller
    {
        /// <summary>
        /// Returns the count of actual GL Journal entries (PostingType='A')
        /// posted in the current calendar month, together with month labels.
        /// </summary>
        public JsonResult GetMonthlyEntryCount()
        {
            if (Session["ctx"] == null)
            {
                return Json("", JsonRequestBehavior.AllowGet);
            }

            Ctx ctx         = Session["ctx"] as Ctx;
            int currentMonth = DateTime.Now.Month;
            int currentYear  = DateTime.Now.Year;
            int entryCount   = 0;

            string sql = "SELECT COUNT(GL_Journal_ID) AS EntryCount FROM GL_Journal"
                       + " WHERE PostingType = 'A'"
                       + " AND IsActive = 'Y'"
                       + " AND EXTRACT(MONTH FROM DateAcct) = @CurrentMonth"
                       + " AND EXTRACT(YEAR FROM DateAcct) = @CurrentYear";

            sql = MRole.GetDefault(ctx).AddAccessSQL(sql, "GL_Journal", MRole.SQL_FULLYQUALIFIED, MRole.SQL_RO);

            SqlParameter[] sqlParams =
            {
                new SqlParameter("@CurrentMonth", currentMonth),
                new SqlParameter("@CurrentYear",  currentYear)
            };
            DataSet ds = DB.ExecuteDataset(sql, sqlParams, null);
            if (ds != null && ds.Tables[0].Rows.Count > 0)
            {
                entryCount = Util.GetValueOfInt(ds.Tables[0].Rows[0]["EntryCount"]);
            }

            var result = new
            {
                EntryCount = entryCount,
                MonthName  = DateTime.Now.ToString("MMMM"),
                MonthAbbr  = DateTime.Now.ToString("MMM")
            };

            return Json(JsonConvert.SerializeObject(result), JsonRequestBehavior.AllowGet);
        }

        /// <summary>
        /// Returns the count of GL Journal documents that are not yet posted
        /// (DocStatus IN Draft/Complete/Closed AND Posted = 'N').
        /// </summary>
        public JsonResult GetUnpostedCount()
        {
            if (Session["ctx"] == null)
            {
                return Json("", JsonRequestBehavior.AllowGet);
            }

            Ctx ctx          = Session["ctx"] as Ctx;
            int unpostedCount = 0;

            string sql = "SELECT COUNT(GL_Journal_ID) AS UnpostedCount FROM GL_Journal"
                       + " WHERE Posted = 'N'"
                       + " AND DocStatus NOT IN ('VO')"
                       + " AND IsActive = 'Y'";

            sql = MRole.GetDefault(ctx).AddAccessSQL(sql, "GL_Journal", MRole.SQL_FULLYQUALIFIED, MRole.SQL_RO);

            DataSet ds = DB.ExecuteDataset(sql, null, null);
            if (ds != null && ds.Tables[0].Rows.Count > 0)
            {
                unpostedCount = Util.GetValueOfInt(ds.Tables[0].Rows[0]["UnpostedCount"]);
            }

            return Json(JsonConvert.SerializeObject(new { UnpostedCount = unpostedCount }), JsonRequestBehavior.AllowGet);
        }

        /// <summary>
        /// Returns the percentage of posted documents across GL_Journal and GL_JournalBatch.
        /// PostedCount / TotalCount * 100, rounded to the nearest integer.
        /// </summary>
        public JsonResult GetPostedPercentage()
        {
            if (Session["ctx"] == null)
            {
                return Json("", JsonRequestBehavior.AllowGet);
            }

            Ctx ctx = Session["ctx"] as Ctx;

            string sql = "SELECT SUM(CASE WHEN GL_Journal.Posted = 'Y' THEN 1 ELSE 0 END) AS PostedCount,"
                       + " COUNT(1) AS TotalCount"
                       + " FROM GL_Journal"
                       + " WHERE GL_Journal.IsActive = 'Y'";

            sql = MRole.GetDefault(ctx).AddAccessSQL(sql, "GL_Journal", MRole.SQL_FULLYQUALIFIED, MRole.SQL_RO);

            int postedCount = 0;
            int totalCount  = 0;

            DataSet ds = DB.ExecuteDataset(sql, null, null);
            if (ds != null && ds.Tables[0].Rows.Count > 0)
            {
                postedCount = Util.GetValueOfInt(ds.Tables[0].Rows[0]["PostedCount"]);
                totalCount  = Util.GetValueOfInt(ds.Tables[0].Rows[0]["TotalCount"]);
            }

            int percentage = totalCount > 0
                ? (int)Math.Round((double)postedCount / totalCount * 100)
                : 0;

            var result = new
            {
                PostedCount = postedCount,
                TotalCount  = totalCount,
                Percentage  = percentage
            };

            return Json(JsonConvert.SerializeObject(result), JsonRequestBehavior.AllowGet);
        }
    }
}
