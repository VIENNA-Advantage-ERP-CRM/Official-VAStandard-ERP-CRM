﻿using System;
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
    public class MProductController:Controller
    {
        public ActionResult Index()
        {
            return View();
        }
        /// <summary>
        /// Getting Product Details
        /// </summary>
        /// <param name="fields"></param>
        /// <returns>Product</returns>
        public JsonResult GetProduct(string fields)
        {
            
            string retJSON = "";
            if (Session["ctx"] != null)
            {
                VAdvantage.Utility.Ctx ctx = Session["ctx"] as Ctx;
                MProductModel objProduct = new MProductModel();
                retJSON = JsonConvert.SerializeObject(objProduct.GetProduct(ctx,fields));
            }          
            return Json(retJSON, JsonRequestBehavior.AllowGet);
        }    
        public JsonResult GetUOMPrecision(string fields)
        {
            
            string retJSON = "";
            if (Session["ctx"] != null)
            {
                VAdvantage.Utility.Ctx ctx = Session["ctx"] as Ctx;
                MProductModel objProduct = new MProductModel();
                retJSON = JsonConvert.SerializeObject(objProduct.GetUOMPrecision(ctx, fields));
            }           
            return Json(retJSON, JsonRequestBehavior.AllowGet);
        }
        /// <summary>
        /// Getting UOM_ID 
        /// </summary>
        /// <param name="fields"></param>
        /// <returns>ID</returns>
        public JsonResult GetC_UOM_ID(string fields)
        {
           
            string retJSON = "";
            if (Session["ctx"] != null)
            {
                VAdvantage.Utility.Ctx ctx = Session["ctx"] as Ctx;
                MProductModel objProduct = new MProductModel();
                retJSON = JsonConvert.SerializeObject(objProduct.GetC_UOM_ID(ctx,fields));
            }            
            return Json(retJSON, JsonRequestBehavior.AllowGet);
        }
        /// <summary>
        /// Getting Product Types
        /// </summary>
        /// <param name="fields"></param>
        /// <returns>Product Types</returns>
        public JsonResult GetProductType(string fields)
        {
           
            string retJSON = "";
            if (Session["ctx"] != null)
            {
                VAdvantage.Utility.Ctx ctx = Session["ctx"] as Ctx;
                MProductModel objProduct = new MProductModel();
                retJSON = JsonConvert.SerializeObject(objProduct.GetProductType(ctx, fields));
            }
            return Json(retJSON, JsonRequestBehavior.AllowGet);
        }
        // Added by amit
        public JsonResult GetTaxCategory(string fields)
        {
            string retJSON = "";
            if (Session["ctx"] != null)
            {
                VAdvantage.Utility.Ctx ctx = Session["ctx"] as Ctx;
                MProductModel objProduct = new MProductModel();
                retJSON = JsonConvert.SerializeObject(objProduct.GetTaxCategory(ctx, fields));
            }
            return Json(retJSON, JsonRequestBehavior.AllowGet);
        }
        //End
        /// <summary>
        /// Get C_REVENUERECOGNITION_ID
        /// </summary>
        /// <param name="fields">C_Product_ID</param>
        /// <returns>C_REVENUERECOGNITION_ID</returns>
        public JsonResult GetRevenuRecognition(string fields)
        {
            string retJSON = ""; 
            if (Session["ctx"] != null)
            {
                Ctx ctx = Session["ctx"] as Ctx;
                MProductModel objProduct = new MProductModel();
                retJSON = JsonConvert.SerializeObject(objProduct.GetRevenuRecognition(ctx, fields));
            }
            return Json(retJSON, JsonRequestBehavior.AllowGet);
        }
        /// <summary>
        /// Counts of Transaction
        /// </summary>
        /// <param name="fields"></param>
        /// <returns>Transction</returns>
        public JsonResult GetUOMCount(string fields)
        {
            string retJSON = "";
            if (Session["ctx"] != null)
            {
                Ctx ctx = Session["ctx"] as Ctx;
                MProductModel objProduct = new MProductModel();
                retJSON = JsonConvert.SerializeObject(objProduct.GetUOMCount(ctx,Util.GetValueOfInt(fields)));
            }
            return Json(retJSON, JsonRequestBehavior.AllowGet);
        }
    }
}