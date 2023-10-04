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
    public class MCurrencyController:Controller
    {
        public ActionResult Index()
        {
            return View();
        }

        public JsonResult GetCurrency(string fields)
        {
           
            string retJSON = "";
            if (Session["ctx"] != null)
            {
                VAdvantage.Utility.Ctx ctx = Session["ctx"] as Ctx;
                MCurrencyModel objCurrency = new MCurrencyModel();
                retJSON = JsonConvert.SerializeObject(objCurrency.GetCurrency(ctx,fields));
            }            
            return Json(retJSON, JsonRequestBehavior.AllowGet);
        }

        /// <summary>
        /// 04/10/2023 Bud ID:2488 :- This method returns Precision of currency present on bank
        /// </summary>
        /// <param name="ctx"></param>
        /// <param name="C_BankAccount_ID"> Contain value of C_BankAccount_ID</param>
        /// <returns>JSON result</returns>
        /// <author>VIS_427</author>
        public JsonResult GetBankCurrencyPrecision(string fields)
        {

            string retJSON = "";
            if (Session["ctx"] != null)
            {
                VAdvantage.Utility.Ctx ctx = Session["ctx"] as Ctx;
                MCurrencyModel objCurrency = new MCurrencyModel();
                retJSON = JsonConvert.SerializeObject(objCurrency.GetBankCurrencyPrecision(ctx, fields));
            }
            return Json(retJSON, JsonRequestBehavior.AllowGet);
        }
    }
}