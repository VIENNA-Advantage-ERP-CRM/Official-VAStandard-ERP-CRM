using System;
using System.Collections.Generic;
using System.Linq;
using System.Web;
using System.Web.Mvc;
using Newtonsoft.Json;
using VAdvantage.Model;
using VAdvantage.Utility;
using VIS.Models;


namespace VIS.Controllers
{
    public class MRequestTypeController: Controller
    {


        public ActionResult Index()
        {
            return View();
        }

        public JsonResult GetDefaultR_Status_ID(string fields)
        {
            
            string retJSON = "";
            if (Session["ctx"] != null)
            {
                VAdvantage.Utility.Ctx ctx = Session["ctx"] as Ctx;
                MRequestTypeModel rt = new MRequestTypeModel();
                int R_Status_ID = rt.GetDefaultR_Status_ID(ctx,fields);
                retJSON = JsonConvert.SerializeObject(R_Status_ID);
            }        
            return Json(retJSON , JsonRequestBehavior.AllowGet);
        }

        /// <summary>
        /// Get Mail text on Request
        /// </summary>
        /// <param name="fields">MailText_ID</param>
        /// <returns>Result</returns>
        public JsonResult GetMailText(string fields)
        {
            String retJSON = "";
            if (Session["ctx"] != null)
            {
                VAdvantage.Utility.Ctx ctx = Session["ctx"] as Ctx;
                MRequestTypeModel rt = new MRequestTypeModel();
                retJSON = JsonConvert.SerializeObject(rt.GetMailText(Util.GetValueOfInt(fields)));
            }
            return Json(retJSON, JsonRequestBehavior.AllowGet);
        }

        /// <summary>
        /// Get Response text on Request
        /// </summary>
        /// <param name="fields">Response ID</param>
        /// <returns>Result</returns>
        public JsonResult GetResponseText(string fields)
        {
            String retJSON = "";
            if (Session["ctx"] != null)
            {
                VAdvantage.Utility.Ctx ctx = Session["ctx"] as Ctx;
                MRequestTypeModel rt = new MRequestTypeModel();
                retJSON = JsonConvert.SerializeObject(rt.GetResponseText(Util.GetValueOfInt(fields)));
            }
            return Json(retJSON, JsonRequestBehavior.AllowGet);
        }
    }
}