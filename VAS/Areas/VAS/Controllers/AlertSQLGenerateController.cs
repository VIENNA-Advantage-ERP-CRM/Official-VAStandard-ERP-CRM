/*******************************************************
       * Module Name    : VAS
       * Purpose        : Get values for SQLGenerator in TabAlerRule.
       * chronological development.
       * Created Date   : 21 Nov 2023
       * Created by     : Ruby
      ******************************************************/
using Newtonsoft.Json;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Web;
using System.Web.Mvc;
using VAdvantage.Model;
using VAdvantage.Utility;
using VIS.Models;

namespace VAS.Areas.VAS.Controllers
{
    public class AlertSQLGenerateController : Controller
    {
        [Authorize]
        public ActionResult Index()
        {
            return View();
        }

        /// <summary>
        /// Get Windows
        /// </summary>
        /// <returns>Window Name/Tab Name/TableId</returns>
        public JsonResult GetWindows()
        {
            string retJSON = "";
            if (Session["ctx"] != null)
            {
                VAdvantage.Utility.Ctx ctx = Session["ctx"] as Ctx;
                AlertSqlGenerate objForecast = new AlertSqlGenerate();
                List<Windows> result = objForecast.GetWindows(ctx);
                retJSON = JsonConvert.SerializeObject(result);
            }
            return Json(retJSON, JsonRequestBehavior.AllowGet);
        }

        /// <summary>
        ///  Geting Table
        /// </summary>
        /// <param name="ctx"></param>
        /// <param name="tabID"></param>
        /// <returns>TableName/TableID</returns>
        public JsonResult GetTable(int tabID)
        {
            string retJSON = "";
            if (Session["ctx"] != null)
            {
                VAdvantage.Utility.Ctx ctx = Session["ctx"] as Ctx;
                AlertSqlGenerate objForecast = new AlertSqlGenerate();
                List<Tabs> result= objForecast.GetTable(ctx, tabID);
                retJSON = JsonConvert.SerializeObject(result);
            }
            return Json(retJSON, JsonRequestBehavior.AllowGet);
        }

        /// <summary>
        /// Geting result of Query
        /// </summary>
        /// <param name="ctx"></param>
        /// <param name="query"></param>
        /// <param name="pageNo"></param>
        /// <param name="pageSize"></param>
        /// <returns>ListofRecords</returns>
        public JsonResult GetResult(string query, int pageNo,int pageSize)
        {
            if (Session["Ctx"] != null)
            {              
                var ctx = Session["ctx"] as Ctx;
                AlertSqlGenerate objForecast = new AlertSqlGenerate();
                var jsonResult = Json(JsonConvert.SerializeObject(objForecast.GetResult(ctx, query, pageNo, pageSize)), JsonRequestBehavior.AllowGet);
                jsonResult.MaxJsonLength = int.MaxValue;
                return jsonResult;
            }
            return Json(JsonConvert.SerializeObject("Session is null"), JsonRequestBehavior.AllowGet);          
        }

        /// <summary>
        /// Geting Columns
        /// </summary>
        /// <param name="ctx"></param>
        /// <param name="tableID"></param>
        /// <returns>ColumnList</returns>
        public JsonResult GetColumns(int tableID)
        {
            string retJSON = "";
            if (Session["ctx"] != null)
            {
                VAdvantage.Utility.Ctx ctx = Session["ctx"] as Ctx;
                AlertSqlGenerate objForecast = new AlertSqlGenerate();
                retJSON = JsonConvert.SerializeObject(objForecast.GetColumns(ctx, tableID));
            }
            return Json(retJSON, JsonRequestBehavior.AllowGet);
        }

        /// <summary>
        /// Save query in AlertRule Window
        /// </summary>
        /// <param name="ctx"></param>
        /// <param name="query"></param>
        /// <param name="tableName"></param>
        /// <param name="TableID"></param>
        /// <param name="alertID"></param>
        /// <param name="alertRuleID"></param>
        /// <returns>saved/notsaved</returns>
        public JsonResult SaveQuery(string query,string tableName,int TableID,int alertID,int alertRuleID)
        {
            string retJSON = "";
            if (Session["ctx"] != null)
            {
                VAdvantage.Utility.Ctx ctx = Session["ctx"] as Ctx;
                AlertSqlGenerate objForecast = new AlertSqlGenerate();
                retJSON = JsonConvert.SerializeObject(objForecast.SaveQuery(ctx, query, tableName, TableID, alertID, alertRuleID));
            }
            return Json(retJSON, JsonRequestBehavior.AllowGet);
        }

        /// <summary>
        /// Update record of AlertRule
        /// </summary>
        /// <param name="ctx"></param>
        /// <param name="query"></param>
        /// <param name="TableID"></param>
        /// <param name="alertID"></param>
        /// <param name="alertRuleID"></param>
        /// <returns>Updated/NotUpdated</returns>
        public JsonResult UpdateQuery(string query,int TableID, int alertID,int alertRuleID)
        {
            string retJSON = "";
            if (Session["ctx"] != null)
            {
                VAdvantage.Utility.Ctx ctx = Session["ctx"] as Ctx;
                AlertSqlGenerate objForecast = new AlertSqlGenerate();
                retJSON = JsonConvert.SerializeObject(objForecast.UpdateQuery(ctx, query, TableID, alertID, alertRuleID));
            }
            return Json(retJSON, JsonRequestBehavior.AllowGet);
        }

        /// <summary>
        /// Getting AlertRule RecordInfo
        /// </summary>
        /// <param name="ctx"></param>
        /// <param name="alertID"></param>
        /// <param name="alertRuleID"></param>
        /// <returns>RecordInfo</returns>
        public JsonResult GetAlertData(int alertID,int alertRuleID)
        {
            string retJSON = "";
            if (Session["ctx"] != null)
            {
                VAdvantage.Utility.Ctx ctx = Session["ctx"] as Ctx;
                AlertSqlGenerate objForecast = new AlertSqlGenerate();
                retJSON = JsonConvert.SerializeObject(objForecast.GetAlertData(ctx, alertID, alertRuleID));
            }
            return Json(retJSON, JsonRequestBehavior.AllowGet);
        }      
    }
}