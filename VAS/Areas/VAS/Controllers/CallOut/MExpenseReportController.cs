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
    public class MExpenseReportController:Controller
    {
        /// <summary>
        /// Get the curency of the pricelist
        /// </summary>
        /// <param name="fields">Time expense ID</param>
        /// <returns>Currency ID</returns>
        public JsonResult GetPriceListCurrency(string fields)
        {

            string retJSON = "";
            if (Session["ctx"] != null)
            {
                VAdvantage.Utility.Ctx ctx = Session["ctx"] as Ctx;
                MTimeExpense timeExpense = new MTimeExpense(ctx, Util.GetValueOfInt(fields), null);
                MPriceList priceList = new MPriceList(ctx, timeExpense.GetM_PriceList_ID(), null);
                retJSON = JsonConvert.SerializeObject(priceList.GetC_Currency_ID());
            }
            return Json(retJSON, JsonRequestBehavior.AllowGet);
        }
        /// <summary>
        /// Get price of product
        /// </summary>
        /// <param name="fields">List of Parameters</param>
        /// <returns>Data in JSON Format</returns>
        public JsonResult GetPrices(string fields)
        {
            String retJSON = "";
            if (Session["ctx"] != null)
            {
                VAdvantage.Utility.Ctx ctx = Session["ctx"] as Ctx;
                MExpenseReportModel objExpense = new MExpenseReportModel();
                retJSON = JsonConvert.SerializeObject(objExpense.GetPrices(ctx, fields));
            }
            return Json(retJSON, JsonRequestBehavior.AllowGet);
        }
        /// <summary>
        /// Get standard price of product
        /// </summary>
        /// <param name="fields">List of Parameters</param>
        /// <returns>Data in JSON format</returns>
        public JsonResult GetStandardPrice(string fields)
        {
            String retJSON = "";
            if (Session["ctx"] != null)
            {
                VAdvantage.Utility.Ctx ctx = Session["ctx"] as Ctx;
                MExpenseReportModel objExpense = new MExpenseReportModel();
                retJSON = JsonConvert.SerializeObject(objExpense.GetStandardPrice(ctx, fields));
            }
            return Json(retJSON, JsonRequestBehavior.AllowGet);
        }

        /// <summary>
        /// Get ChargeAmount
        /// </summary>
        /// <param name="fields">Parameters</param>
        /// <returns>Data in JSON Format</returns>
        public JsonResult GetChargeAmount(string fields)
        {
            String retJSON = "";
            if (Session["ctx"] != null)
            {
                VAdvantage.Utility.Ctx ctx = Session["ctx"] as Ctx;
                MExpenseReportModel objExpense = new MExpenseReportModel();
                retJSON = JsonConvert.SerializeObject(objExpense.GetChargeAmount(ctx, fields));
            }
            return Json(retJSON, JsonRequestBehavior.AllowGet);
        }
        /// <summary>
        /// GetProfiletype
        /// </summary>
        /// <param name="fields">fields</param>
        /// <returns>JSON Data</returns>
        public JsonResult GetProfiletype(string fields)
        {
            MExpenseReportModel model = new MExpenseReportModel();
            var value = model.GetProfiletype(fields);
            return Json(JsonConvert.SerializeObject(value), JsonRequestBehavior.AllowGet);
        }

        /// <summary>
        /// VAI094:for fetching customer id from database according to requestid or project id  selected in 
        /// request field  or project field
        /// in Report line tab in time and expense report window
        /// </summary>
        /// <param name="fields">string fields </param>
        /// <returns>customer id</returns>
        public JsonResult LoadCustomerData(string fields)
        {
            MExpenseReportModel obj = new MExpenseReportModel();
            Dictionary<string, object> _result = null;
            if (Session["ctx"] != null)
            {
                _result = obj.LoadCustomerData(fields);
            }
            return Json(JsonConvert.SerializeObject(_result), JsonRequestBehavior.AllowGet);
        }

        /// <summary>
        /// VAI094:for fetching M_PRODUCT_ID,C_UOM_ID from database according to Id  selected in 
        /// projectphase or project task or product field in Report line tab in time and expense report window
        /// </summary>
        /// <param name="fields">string fields </param>
        /// <returns>M_PRODUCT_ID,C_UOM_ID</returns>
        public JsonResult LoadProductData(string fields)
        {
            MExpenseReportModel obj = new MExpenseReportModel();
            Dictionary<string, object> result = null;
            if (Session["ctx"] != null)
            {
                var ctx = Session["ctx"] as Ctx;
                result = obj.LoadProductData(fields);
            }
            return Json(JsonConvert.SerializeObject(result), JsonRequestBehavior.AllowGet);
        }
    }
}