/********************************************************
 * Project Name   : VAdvantage
 * Class Name     : MRfQResponseController.cs
 * Purpose        : for fetch quantity
 * Chronological    Development
 * Priyanka Sharma     15-Dec-2023
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

namespace VIS.Controllers
{
    public class MRFQResponseController : Controller
    {
        // GET: VAS/MRFQResponse
        public ActionResult Index()
        {
            return View();
        }

        /// <summary>
        ///  VAI051: changes done for fetching the qty from C_RfQLineQty
        /// </summary>
        /// <param name="fields">fields</param>
        /// <returns>Data in JSON Format</returns>
        public JsonResult GetPrice(string fields)
        {
            string retJson = "";
            if (Session["ctx"] != null)
            {
                Ctx ctx = Session["ctx"] as Ctx;
                MRfqResponseModel obj = new MRfqResponseModel();
                
                retJson = JsonConvert.SerializeObject(obj.GetPriceDetail(ctx, fields));
            }
               return Json(retJson, JsonRequestBehavior.AllowGet);
        }
    }
}
