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
        // GET: VAS/AlertRuleSQLGenerate
        [Authorize]
        public ActionResult Index()
        {
            return View();
        }
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