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
    public class MProfitTaxController : Controller
    {
        // GET: VAS/MProfitTax
        public ActionResult Index()
        {
            return View();
        }
        /// <summary>
        /// Getting Profit/Loss and Year From C_Profittax table
        /// </summary>
        /// <param name="fields"></param>
        /// <returns></returns>
        public JsonResult GetBeforeTax(string fields)
        {

            string retJSON = "";
            if (Session["ctx"] != null)
            {
                VAdvantage.Utility.Ctx ctx = Session["ctx"] as Ctx;
                MProfitTaxModel objtax = new MProfitTaxModel();
                retJSON = JsonConvert.SerializeObject(objtax.GetBeforeTax(ctx, fields));
            }
            return Json(retJSON, JsonRequestBehavior.AllowGet);
        }
    }
}