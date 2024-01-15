/*******************************************************
       * Module Name    : VAS
       * Purpose        : Tab Panel For AP Matched PO and MatchedReceipt
       * chronological  : Development
       * Created Date   : 12 January 2024
       * Created by     : VAI066
      ******************************************************/
using Newtonsoft.Json;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Web;
using System.Web.Mvc;
using VAdvantage.Utility;
using VASLogic.Models;

namespace VAS.Areas.VAS.Controllers
{
    public class PoReceiptController : Controller
    {
        // GET: VAS/InvoiceTabPanel
        [Authorize]
        public ActionResult Index()
        {
            return View();
        }
        /// <summary>
        /// Used to get Data for Tab Panel
        /// </summary>
        /// <param name="parentID">parentID will be order Line ID</param>
        /// <returns>return the data needed for tab panel</returns>
        public JsonResult GetData(int parentID)
        {
            string retJSON = "";
            if (Session["ctx"] != null)
            {
                Ctx ctx = Session["ctx"] as Ctx;
                PoReceiptTabPanelModel obj = new PoReceiptTabPanelModel();
                List<TabPanel> result = obj.GetInvoiceLineData(ctx, parentID);
                retJSON = JsonConvert.SerializeObject(result);
            }
            return Json(retJSON, JsonRequestBehavior.AllowGet);
        }
    }
}