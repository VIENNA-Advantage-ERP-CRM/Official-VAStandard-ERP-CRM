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
    public class MInvoiceBatchLineController:Controller
    {

        public ActionResult Index()
        {
            return View();
        }
        /// <summary>
        /// Getting InvoiceBatchLine
        /// </summary>
        /// <param name="fields"></param>
        /// <returns></returns>
        public JsonResult GetInvoiceBatchLine(string fields)
        {
            
            string retJSON = "";
            if (Session["ctx"] != null)
            {
                VAdvantage.Utility.Ctx ctx = Session["ctx"] as Ctx;
                MInvoiceBatchLineModel objInvoiceBatchLine = new MInvoiceBatchLineModel();
                retJSON = JsonConvert.SerializeObject(objInvoiceBatchLine.GetInvoiceBatchLine(ctx,fields));
            }          
            //return Json(new { result = retJSON, error = retError }, JsonRequestBehavior.AllowGet);
            return Json(retJSON , JsonRequestBehavior.AllowGet);
        }
        /// <summary>
        /// Getting Charge Details
        /// </summary>
        /// <param name="fields"></param>
        /// <returns></returns>
        public JsonResult GetCharges(string fields)
        {
            string retJSON = "";
            if (Session["ctx"] != null)
            {
                VAdvantage.Utility.Ctx ctx = Session["ctx"] as Ctx;
                MInvoiceBatchLineModel objBatchLine = new MInvoiceBatchLineModel();
                retJSON = JsonConvert.SerializeObject(objBatchLine.GetCharges(Util.GetValueOfInt(fields)));
            }
            return Json(retJSON, JsonRequestBehavior.AllowGet);

        }
        /// <summary>
        /// Invoice BAtch ID
        /// </summary>
        /// <param name="fields"></param>
        /// <returns></returns>
        public JsonResult GetMaxLines(string fields)
        {
            string retJSON = "";
            if (Session["ctx"] != null)
            {
                VAdvantage.Utility.Ctx ctx = Session["ctx"] as Ctx;
                MInvoiceBatchLineModel objBatchLine = new MInvoiceBatchLineModel();
                retJSON = JsonConvert.SerializeObject(objBatchLine.GetMaxLines(Util.GetValueOfInt(fields)));
            }
            return Json(retJSON, JsonRequestBehavior.AllowGet);

        }
    }
}
