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
    public class MPriceListController:Controller
    {


        public ActionResult Index()
        {
            return View();
        }
        public JsonResult GetPriceList(string fields)
        {
            
            string retJSON = "";
            if (Session["ctx"] != null)
            {
                VAdvantage.Utility.Ctx ctx = Session["ctx"] as Ctx;
                MPriceListModel objPriceList = new MPriceListModel();
                retJSON = JsonConvert.SerializeObject(objPriceList.GetPriceList(ctx,fields));
            }          
            return Json(retJSON, JsonRequestBehavior.AllowGet);
        }

        /// <summary>
        /// Get Price List Data
        /// </summary>
        /// <param name="ctx">Context</param>
        /// <param name="fields">Parameters</param>
        /// <returns>List of Data</returns>
        public JsonResult GetPriceListData(string fields)
        {

            string retJSON = "";
            if (Session["ctx"] != null)
            {
                VAdvantage.Utility.Ctx ctx = Session["ctx"] as Ctx;
                MPriceListModel objPriceList = new MPriceListModel();
                retJSON = JsonConvert.SerializeObject(objPriceList.GetPriceListData(ctx, fields));
            }
            return Json(retJSON, JsonRequestBehavior.AllowGet);
        }

        /// <summary>
        /// Get Price List Data
        /// </summary>
        /// <param name="ctx">Context</param>
        /// <param name="fields">Parameters</param>
        /// <returns>List of Data</returns>
        public JsonResult GetPriceListDataForProvisionalInvoice(string fields)
        {

            string retJSON = "";
            if (Session["ctx"] != null)
            {
                VAdvantage.Utility.Ctx ctx = Session["ctx"] as Ctx;
                MPriceListModel objPriceList = new MPriceListModel();
                retJSON = JsonConvert.SerializeObject(objPriceList.GetPriceListDataForProvisionalInvoice(ctx, fields));
            }
            return Json(retJSON, JsonRequestBehavior.AllowGet);
        }
        /// <summary>
        /// Getting Unit OF Measurment
        /// </summary>
        /// <param name="fields"></param>
        /// <returns></returns>
        public JsonResult GetPriceListUOM(string fields)
        {

            string retJSON = "";
            if (Session["ctx"] != null)
            {
                VAdvantage.Utility.Ctx ctx = Session["ctx"] as Ctx;
                MPriceListModel objPriceList = new MPriceListModel();
                retJSON = JsonConvert.SerializeObject(objPriceList.GetPriceListUOM(ctx ,Util.GetValueOfInt(fields)));
            }
            return Json(retJSON, JsonRequestBehavior.AllowGet);
        }


    }
}