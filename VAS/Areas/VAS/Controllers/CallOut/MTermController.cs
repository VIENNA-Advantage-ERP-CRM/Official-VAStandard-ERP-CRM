/*******************************************************
    * Module Name    : Standard module
    * Purpose        : Term Controller
    * Chronological  : Development
    * VAI094         : 10/4/2024
******************************************************/
using System;
using System.Collections.Generic;
using System.Linq;
using System.Web;
using System.Web.Mvc;
using Newtonsoft.Json;
using VAdvantage.Model;
using VAdvantage.Utility;
using VAS.Models;

namespace VAS.Controllers
{
    public class MTermController : Controller
    {
        public ActionResult Index()
        {
            return View();
        }

        /// <summary>
        /// to get term description data for term description field in 
        /// term assignment tab in terms  window  from term details
        /// field in term master window
        /// </summary>
        /// <param name="fields"></param>
        /// <returns>term description</returns>
        public JsonResult GetTermDescription(string fields)
        {
            string retJSON = "";
            if (Session["ctx"] != null)
            {
                MTermModel term = new MTermModel();
                retJSON = JsonConvert.SerializeObject(term.GetTermDescription(fields));
            }
            return Json(retJSON, JsonRequestBehavior.AllowGet);
        }
    }
}