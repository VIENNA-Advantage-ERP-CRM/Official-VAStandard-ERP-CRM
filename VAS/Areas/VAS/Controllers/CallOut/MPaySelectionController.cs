using Newtonsoft.Json;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Web;
using System.Web.Mvc;
using VAdvantage.Model;
using VAdvantage.Utility;
using VASLogic.Models;

namespace VAS.Controllers
{
    public class MPaySelectionController : Controller
    {
        // GET: VAS/MPaySelection
        public ActionResult Index()
        {
            return View();
        }
        /// <summary>
        /// Getting Invoice
        /// </summary>
        /// <param name="fields"></param>
        /// <returns></returns>
        public JsonResult GetInvoice(string fields)
        {

            string retJSON = "";
            if (Session["ctx"] != null)
            {
                VAdvantage.Utility.Ctx ctx = Session["ctx"] as Ctx;
                MPaySelectionModel ObjpaySelection = new MPaySelectionModel();
                retJSON = JsonConvert.SerializeObject(ObjpaySelection.GetInvoice(ctx, fields));
            }
            //return Json(new { result = retJSON, error = retError }, JsonRequestBehavior.AllowGet);
            return Json(retJSON, JsonRequestBehavior.AllowGet);
        }
    }
}