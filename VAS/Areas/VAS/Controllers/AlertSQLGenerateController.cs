/*******************************************************
       * Module Name    : VAS
       * Purpose        : Get values for SQLGenerator in TabAlerRule.
       * chronological development.
       * Created Date   : 21 Nov 2023
       * Created by     : VAI055
      ******************************************************/
using Newtonsoft.Json;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Web;
using System.Web.Mvc;
using VAdvantage.Model;
using VAdvantage.Utility;
using VIS.Classes;
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
        ///  Get Table information From Tab
        /// </summary>
        /// <param name="tabID">AD_Tab_ID</param>
        /// <param name="windowNo">Window number</param>
        /// <returns>TableName/AD_Table_ID</returns>
        public JsonResult GetTable(int tabID,int windowNo)
        {
            if (Session["ctx"] != null)
            {
                Ctx ctx = Session["ctx"] as Ctx;
                AlertSqlGenerate obj = new AlertSqlGenerate();
                List<Tabs> result= obj.GetTable(ctx, tabID, windowNo);
                var jsonResult = Json(JsonConvert.SerializeObject(result), JsonRequestBehavior.AllowGet);
                jsonResult.MaxJsonLength = int.MaxValue;
                return jsonResult;
            }
            return Json(JsonConvert.SerializeObject("Session is null"), JsonRequestBehavior.AllowGet);          
        }

        /// <summary>
        /// Get Result Of Query
        /// </summary>
        /// <param name="query">Query</param>
        /// <param name="pageNo">Page No</param>
        /// <param name="pageSize">page Size</param>
        /// <param name="tableName">Table Name</param>
        /// <returns>ListofRecords</returns>
        public JsonResult GetResult(string query, int pageNo,int pageSize,string tableName, int recordCount)
        {
            if (Session["Ctx"] != null)
            {
                Ctx ctx = Session["ctx"] as Ctx;
                AlertSqlGenerate obj = new AlertSqlGenerate();
                if (!string.IsNullOrEmpty(query))
                    query = SecureEngineBridge.DecryptByClientKey(query, ctx.GetSecureKey());
                var jsonResult = Json(JsonConvert.SerializeObject(obj.GetResult(ctx, query, pageNo, pageSize, tableName, recordCount)), JsonRequestBehavior.AllowGet);
                jsonResult.MaxJsonLength = int.MaxValue;
                return jsonResult;
            }
            return Json(JsonConvert.SerializeObject("Session is null"), JsonRequestBehavior.AllowGet);          
        }

        /// <summary>
        /// Get All Columns From Table
        /// </summary>
        /// <param name="tableID">AD_Table_ID</param>
        /// <returns>ColumnInfornationList</returns>
        public JsonResult GetColumns(int tableID,int tabID)
        {
            string retJSON = "";
            if (Session["ctx"] != null)
            {
                Ctx ctx = Session["ctx"] as Ctx;
                AlertSqlGenerate obj = new AlertSqlGenerate();
                retJSON = JsonConvert.SerializeObject(obj.GetColumns(ctx, tableID, tabID));
            }
            return Json(retJSON, JsonRequestBehavior.AllowGet);
        }   

        /// <summary>
        /// Update record of AlertRule by TabSqlGenerator 
        /// </summary>
        /// <param name="query">Query</param>
        /// <param name="tableID">AD_Table_ID</param>
        /// <param name="alertID">AD_Alert_ID</param>
        /// <param name="alertRuleID">AD_AlertRule_ID</param>
        /// <returns>Updated/NotUpdated</returns>
        public JsonResult UpdateQuery(string query,int tableID, int alertID,int alertRuleID)
        {
            string retJSON = "";
            if (Session["ctx"] != null)
            {
                Ctx ctx = Session["ctx"] as Ctx;
                AlertSqlGenerate obj = new AlertSqlGenerate();
                if (!string.IsNullOrEmpty(query))
                    query = SecureEngineBridge.DecryptByClientKey(query, ctx.GetSecureKey());
                if (!QueryValidator.IsValid(query))
                {
                    return Json(null);
                }
                retJSON = JsonConvert.SerializeObject(obj.UpdateQuery(ctx, query, tableID, alertID, alertRuleID));
            }
            return Json(retJSON, JsonRequestBehavior.AllowGet);
        }

        /// <summary>
        /// Get AlertRule RecordInfo for TabSqlGenerator
        /// </summary>
        /// <param name="alertRuleID">AD_AlertRule_ID</param>
        /// <returns>RecordInfo</returns>
        public JsonResult GetAlertData(int alertRuleID)
        {
            string retJSON = "";
            if (Session["ctx"] != null)
            {
                Ctx ctx = Session["ctx"] as Ctx;
                AlertSqlGenerate obj = new AlertSqlGenerate();
                retJSON = JsonConvert.SerializeObject(obj.GetAlertData(ctx, alertRuleID));
            }
            return Json(retJSON, JsonRequestBehavior.AllowGet);
        }

        /// <summary>
        /// Getting names from table if name column exists 
        /// </summary>
        /// <param name="columnName">Column Name</param>
        /// <param name="tableName">Table Name</param>
        /// <param name="displayType"> Dispaly type</param>
        /// <returns>Names</returns>
        public JsonResult GetIdsName(string columnName,string tableName, int displayType,string whereClause,bool isNameExist,int columnID, int refrenceValueID,int windowNo)
        {
            string retJSON = "";
            if (Session["ctx"] != null)
            {
                Ctx ctx = Session["ctx"] as Ctx;
                AlertSqlGenerate obj = new AlertSqlGenerate();
                retJSON = JsonConvert.SerializeObject(obj.GetIdsName(ctx, columnName, tableName, displayType, whereClause, isNameExist, columnID, refrenceValueID, windowNo));
            }
            return Json(retJSON, JsonRequestBehavior.AllowGet);
        }
        
        /// <summary>
        /// Getting subquery from loopup  to show names instead of Ids
        /// </summary>
        /// <param name="windowNo">Window number</param>
        /// <param name="columnDatatype">DataType</param>
        /// <param name="columnID">ad_Column_ID</param>
        /// <param name="columnName">Column Name</param>
        /// <param name="refrenceID">AD_Refrence_ID</param>
        /// <param name="isParent">Is parent link column</param>
        /// <param name="tableName">Table Name</param>
        /// <returns>subquery</returns>
        public JsonResult GetLookup(int windowNo, int columnDatatype, int columnID, string columnName, int refrenceID,bool isParent,string tableName)
        {
            string retJSON = "";
            if (Session["ctx"] != null)
            {
                Ctx ctx = Session["ctx"] as Ctx;
                AlertSqlGenerate obj = new AlertSqlGenerate();
                retJSON = JsonConvert.SerializeObject(obj.GetLookup(ctx, windowNo, columnDatatype, columnID, columnName, refrenceID, isParent, tableName));
            }
            return Json(retJSON, JsonRequestBehavior.AllowGet);
        }
    }
}