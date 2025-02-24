using Newtonsoft.Json;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Web;
using System.Web.Mvc;
using VAdvantage.Utility;
using VASLogic.Models;

namespace VAS.Controllers
{
    public class VAS_PaymentWidgetController : Controller
    {
        // GET: VAS/VAS_Widget
        public ActionResult Index()
        {
            return View();
        }
        /// <summary>
        /// Get Bank Balance details
        /// </summary>
        /// <param name="fields"></param>
        /// <returns></returns>
        public JsonResult GetBankBalance()
        {
            string retJSON = "";
            if (Session["ctx"] != null)
            {
                Ctx ctx = Session["ctx"] as Ctx;
                VAS_PaymentWidgetModel obj = new VAS_PaymentWidgetModel();
                retJSON = JsonConvert.SerializeObject(obj.GetBankBalance(ctx));
            }
            return Json(retJSON, JsonRequestBehavior.AllowGet);
        }
        /// <summary>
        /// Get Cash Book Balance details
        /// </summary>
        /// <param name="fields"></param>
        /// <returns></returns>
        public JsonResult GetCashBookBalance()
        {
            string retJSON = "";
            if (Session["ctx"] != null)
            {
                Ctx ctx = Session["ctx"] as Ctx;
                VAS_PaymentWidgetModel obj = new VAS_PaymentWidgetModel();
                retJSON = JsonConvert.SerializeObject(obj.GetCashBookBalance(ctx));
            }
            return Json(retJSON, JsonRequestBehavior.AllowGet);
        }
    }
}