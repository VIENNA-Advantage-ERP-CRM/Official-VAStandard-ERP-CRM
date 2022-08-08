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
    public class MFrameworkController : Controller
    {
        // GET: VAS/MFramework
        public ActionResult Index()
        {
            return View();
        }
        /// <summary>
        ///This method used to Update Group By Check
        /// </summary>
        /// <param name="fields"></param>
        /// <returns></returns>
        public JsonResult UpdateGroupByChecked(string fields)
        {
            string retJSON = "";
            if (Session["ctx"] != null)
            {
                VAdvantage.Utility.Ctx ctx = Session["ctx"] as Ctx;
                MFrameworkModel objDocType = new MFrameworkModel();
                retJSON = JsonConvert.SerializeObject(objDocType.UpdateGroupByChecked(ctx, fields));
            }
            return Json(retJSON, JsonRequestBehavior.AllowGet);
        }
        /// <summary>
        ///This method used to Get Workflow Type
        /// </summary>
        /// <returns>Ad_Table id</returns>
        public JsonResult GetWorkflowType()
        {
            string retJSON = "";
            if (Session["ctx"] != null)
            {
                VAdvantage.Utility.Ctx ctx = Session["ctx"] as Ctx;
                MFrameworkModel objDocType = new MFrameworkModel();
                retJSON = JsonConvert.SerializeObject(objDocType.GetWorkflowType(ctx));
            }
            return Json(retJSON, JsonRequestBehavior.AllowGet);
        }
        /// <summary>
        ///This method used to Get Workflow Type
        /// </summary>
        /// <returns>Ad_Table id</returns>
        public JsonResult GetIsGenericAttribute(string fields)
        {
            string retJSON = "";
            if (Session["ctx"] != null)
            {
                VAdvantage.Utility.Ctx ctx = Session["ctx"] as Ctx;
                MFrameworkModel objDocType = new MFrameworkModel();
                retJSON = JsonConvert.SerializeObject(objDocType.GetIsGenericAttribute(ctx, fields));
            }
            return Json(retJSON, JsonRequestBehavior.AllowGet);
        }
    }
}