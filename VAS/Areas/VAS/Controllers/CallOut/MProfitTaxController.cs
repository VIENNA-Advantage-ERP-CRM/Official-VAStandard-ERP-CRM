/********************************************************
 * Project Name   :    VIS
 * Class Name     :    MProfitTax
 * Purpose        :    Used for Income Tax callout
 * Chronological       Development
 * VIS_0045            20/07/2022
  ******************************************************/

using Newtonsoft.Json;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Web;
using System.Web.Mvc;
using VAdvantage.Utility;
using VIS.Models;

namespace VIS.Controllers
{
    public class MProfitTaxController : Controller
    {
        // GET: VAS/MProfitTax
        public ActionResult Index()
        {
            return View();
        }

        /// <summary>
        /// Get value of C_Year_ID, C_ProfitAndLoss_ID, ProfitBeforeTax
        /// </summary>
        /// <param name="fields">C_ProfitLoss_ID</param>
        /// <returns>GetProfitLossDetails</returns>
        public JsonResult GetProfitLossDetails(string fields)
        {

            string retJSON = "";
            if (Session["ctx"] != null)
            {
                VAdvantage.Utility.Ctx ctx = Session["ctx"] as Ctx;
                MProfitTaxModel objtax = new MProfitTaxModel();
                retJSON = JsonConvert.SerializeObject(objtax.GetProfitLossDetails(ctx, fields));
            }
            return Json(retJSON, JsonRequestBehavior.AllowGet);
        }
    }
}