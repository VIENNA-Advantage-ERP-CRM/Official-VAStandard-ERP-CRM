﻿/*******************************************************
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
        /// Get all Windows for Alert SqlGenerator
        /// </summary>
        /// <returns>AD_Window Name/Ad_Tab Name/AD_Table_ID</returns>
        public JsonResult GetWindows()
        {
            string retJSON = "";
            if (Session["ctx"] != null)
            {
                Ctx ctx = Session["ctx"] as Ctx;
                AlertSqlGenerate obj = new AlertSqlGenerate();
                List<Windows> result = obj.GetWindows(ctx);
                retJSON = JsonConvert.SerializeObject(result);
            }
            return Json(retJSON, JsonRequestBehavior.AllowGet);
        }

        /// <summary>
        ///  Get Table information From Tab
        /// </summary>
        /// <param name="tabID">AD_Tab_ID</param>
        /// <returns>TableName/AD_Table_ID</returns>
        public JsonResult GetTable(int tabID)
        {
            string retJSON = "";
            if (Session["ctx"] != null)
            {
                Ctx ctx = Session["ctx"] as Ctx;
                AlertSqlGenerate obj = new AlertSqlGenerate();
                List<Tabs> result= obj.GetTable(ctx, tabID);
                retJSON = JsonConvert.SerializeObject(result);
            }
            return Json(retJSON, JsonRequestBehavior.AllowGet);
        }

        /// <summary>
        /// Get Result Of Query
        /// </summary>
        /// <param name="query">Query</param>
        /// <param name="pageNo">Page No</param>
        /// <param name="pageSize">page Size</param>
        /// <returns>ListofRecords</returns>
        public JsonResult GetResult(string query, int pageNo,int pageSize)
        {
            if (Session["Ctx"] != null)
            {
                Ctx ctx = Session["ctx"] as Ctx;
                AlertSqlGenerate obj = new AlertSqlGenerate();
                var jsonResult = Json(JsonConvert.SerializeObject(obj.GetResult(ctx, query, pageNo, pageSize)), JsonRequestBehavior.AllowGet);
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
        public JsonResult GetColumns(int tableID)
        {
            string retJSON = "";
            if (Session["ctx"] != null)
            {
                Ctx ctx = Session["ctx"] as Ctx;
                AlertSqlGenerate obj = new AlertSqlGenerate();
                retJSON = JsonConvert.SerializeObject(obj.GetColumns(ctx, tableID));
            }
            return Json(retJSON, JsonRequestBehavior.AllowGet);
        }

        /// <summary>
        /// Saving SqlGenerator query in AlertRule Window
        /// </summary>
        /// <param name="query">Query</param>
        /// <param name="tableName">AD_Table Name</param>
        /// <param name="tableID">AD_Table_ID</param>
        /// <param name="alertID">AD_Alert_ID</param>
        /// <param name="alertRuleID">AD_AlertRule_ID</param>
        /// <returns>saved/notsaved</returns>
        public JsonResult SaveQuery(string query,string tableName,int tableID,int alertID,int alertRuleID)
        {
            string retJSON = "";
            if (Session["ctx"] != null)
            {
                Ctx ctx = Session["ctx"] as Ctx;
                AlertSqlGenerate obj = new AlertSqlGenerate();
                retJSON = JsonConvert.SerializeObject(obj.SaveQuery(ctx, query, tableName, tableID, alertID, alertRuleID));
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
                retJSON = JsonConvert.SerializeObject(obj.UpdateQuery(ctx, query, tableID, alertID, alertRuleID));
            }
            return Json(retJSON, JsonRequestBehavior.AllowGet);
        }

        /// <summary>
        /// Get AlertRule RecordInfo for TabSqlGenerator
        /// </summary>
        /// <param name="alertID">AD_Alert_ID</param>
        /// <param name="alertRuleID">AD_AlertRule_ID</param>
        /// <returns>RecordInfo</returns>
        public JsonResult GetAlertData(int alertID,int alertRuleID)
        {
            string retJSON = "";
            if (Session["ctx"] != null)
            {
                Ctx ctx = Session["ctx"] as Ctx;
                AlertSqlGenerate obj = new AlertSqlGenerate();
                retJSON = JsonConvert.SerializeObject(obj.GetAlertData(ctx, alertID, alertRuleID));
            }
            return Json(retJSON, JsonRequestBehavior.AllowGet);
        }      
    }
}