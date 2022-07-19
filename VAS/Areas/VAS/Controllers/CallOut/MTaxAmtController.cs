using Newtonsoft.Json;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Web;
using System.Web.Mvc;
using VAdvantage.Utility;
using VASLogic.Models.Callouts;

namespace VAS.Areas.VAS.Controllers.CallOut
{
    public class MTaxAmtController : Controller
    {
        // GET: VAS/MTaxAmt
        public ActionResult Index()
        {
            return View();
        }
        /// <summary>
        /// Get Rate
        /// </summary>
        /// <param name="Id"></param>
        /// <returns>Json</returns>
        public JsonResult GetRate(string fields)
        {

            string retJSON = "";
            if (Session["ctx"] != null)
            {
                VAdvantage.Utility.Ctx ctx = Session["ctx"] as Ctx;
                MTaxAmtModel objAmt = new MTaxAmtModel();
                retJSON = JsonConvert.SerializeObject(objAmt.GetRate(Util.GetValueOfInt(fields)));
            }
            return Json(retJSON, JsonRequestBehavior.AllowGet);
        }
        /// <summary>
        /// Getting Exp Amount 
        /// </summary>
        /// <param name="Id"></param>
        /// <returns></returns>
        public JsonResult GetExpAmt(int Id)
        {

            string retJSON = "";
            if (Session["ctx"] != null)
            {
                VAdvantage.Utility.Ctx ctx = Session["ctx"] as Ctx;
                MTaxAmtModel objAmt = new MTaxAmtModel();
                retJSON = JsonConvert.SerializeObject(objAmt.GetExpAmt(Id));
            }
            return Json(retJSON, JsonRequestBehavior.AllowGet);
        }
    }
}