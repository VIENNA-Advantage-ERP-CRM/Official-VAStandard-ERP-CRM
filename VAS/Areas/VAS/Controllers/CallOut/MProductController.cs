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
    public class MProductController : Controller
    {
        public ActionResult Index()
        {
            return View();
        }

        public JsonResult GetProduct(string fields)
        {

            string retJSON = "";
            if (Session["ctx"] != null)
            {
                VAdvantage.Utility.Ctx ctx = Session["ctx"] as Ctx;
                MProductModel objProduct = new MProductModel();
                retJSON = JsonConvert.SerializeObject(objProduct.GetProduct(ctx, fields));
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
        public JsonResult GetC_UOM_ID(string fields)
        {

            string retJSON = "";
            if (Session["ctx"] != null)
            {
                VAdvantage.Utility.Ctx ctx = Session["ctx"] as Ctx;
                MProductModel objProduct = new MProductModel();
                retJSON = JsonConvert.SerializeObject(objProduct.GetC_UOM_ID(ctx, fields));
            }
            return Json(retJSON, JsonRequestBehavior.AllowGet);
        }
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
        /// Get AttributeSet
        /// </summary>
        /// <param name="fields">C_Product_ID</param>
        /// <returns>AttributeSet_ID</returns>
        public JsonResult GetAttributeSet(string fields)
        {
            string retJSON = "";
            if (Session["ctx"] != null)
            {
                Ctx ctx = Session["ctx"] as Ctx;
                MProductModel objProduct = new MProductModel();
                retJSON = JsonConvert.SerializeObject(objProduct.GetAttributeSet(ctx, fields));
            }
            return Json(retJSON, JsonRequestBehavior.AllowGet);
        }

        /// <summary>
        /// Get Product Attribute
        /// </summary>
        /// <param name="fields">C_Product_ID</param>
        /// <returns>Attribute_ID</returns>
        public JsonResult GetProductAttribute(string fields)
        {
            string retJSON = "";
            if (Session["ctx"] != null)
            {
                Ctx ctx = Session["ctx"] as Ctx;
                MProductModel objProduct = new MProductModel();
                retJSON = JsonConvert.SerializeObject(objProduct.GetProductAttribute(ctx, fields));
            }
            return Json(retJSON, JsonRequestBehavior.AllowGet);
        }

        /// <summary>
        /// Get resource assignment details
        /// </summary>
        /// <param name="fields">ResourceAssignment_ID</param>
        /// <returns>Result</returns>
        public JsonResult GetResourceAssignmntDet(string fields)
        {
            string retJSON = "";
            if (Session["ctx"] != null)
            {
                Ctx ctx = Session["ctx"] as Ctx;
                MProductModel objProduct = new MProductModel();

                retJSON = JsonConvert.SerializeObject(objProduct.GetResourceAssignmntDet(ctx, Util.GetValueOfInt(fields)));
            }
            return Json(retJSON, JsonRequestBehavior.AllowGet);
        }

        /// Counts of Transaction
        /// </summary>
        /// <param name="fields">Product ID</param>
        /// <returns>Transction Count</returns>
        public JsonResult GetTransactionCount(string fields)
        {
            string retJSON = "";
            if (Session["ctx"] != null)
            {
                Ctx ctx = Session["ctx"] as Ctx;
                MProductModel objProduct = new MProductModel();
                retJSON = JsonConvert.SerializeObject(objProduct.GetTransactionCount(ctx, Util.GetValueOfInt(fields)));
            }
            return Json(retJSON, JsonRequestBehavior.AllowGet);
        }
        /// <summary>
        /// GetPOUOM
        /// </summary>
        /// <param name="fields">fields</param>
        /// <returns>JSON Datas</returns>
        public JsonResult GetPOUOM(string fields)
        {
            MProductModel model = new MProductModel();
            var value = model.GetPOUOM(fields);
            return Json(JsonConvert.SerializeObject(value), JsonRequestBehavior.AllowGet);
        }
        /// <summary>
        /// GetUOMID
        /// </summary>
        /// <param name="M_Product_ID">M_Product_ID</param>
        /// <returns>JSON Data</returns>
        public JsonResult GetUOMID(string fields)
        {
            MProductModel model = new MProductModel();
            var value = model.GetUOMID(fields);
            return Json(JsonConvert.SerializeObject(value), JsonRequestBehavior.AllowGet);
        }
        /// <summary>
        /// GetManufacturer
        /// </summary>
        /// <param name="fields">fields</param>
        /// <returns>JSON Data</returns>
        public JsonResult GetManufacturer(string fields)
        {
            MProductModel model = new MProductModel();
            var value = model.GetManufacturer(fields);
            return Json(JsonConvert.SerializeObject(value), JsonRequestBehavior.AllowGet);
        }

    }
}