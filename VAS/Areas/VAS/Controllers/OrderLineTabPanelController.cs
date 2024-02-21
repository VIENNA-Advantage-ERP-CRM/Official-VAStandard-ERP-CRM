/*******************************************************
       * Module Name    : VAS
       * Purpose        : Tab Panel For PO Lines tab of Purchage Order window
       * chronological  : Development
       * Created Date   : 20 February 2024
       * Created by     : VIS430
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
    public class OrderLineTabPanelController : Controller
    {
        // GET: VAS/OrderLineTabPanel
        public ActionResult Index()
        {
            return View();
        }

        /// <summary>
        /// VIS430: Get the RequitionLines  tab data for PO lines tab record of Purchase order
        /// </summary>
        /// <param name="OrderLineId"></param>
        /// <returns></returns>
        public JsonResult GetRequitionLinesData(int OrderLineId)
        {
            string retJSON = "";
            if (Session["ctx"] != null)

            {
                Ctx ctx = Session["ctx"] as Ctx;
                OrderLineTabPanelModel obj = new OrderLineTabPanelModel();
                List<RequitionTabPanel> result = obj.GetRequitionLinesData(ctx, OrderLineId);
                retJSON = JsonConvert.SerializeObject(result);
            }
            return Json(retJSON, JsonRequestBehavior.AllowGet);
        }
        /// <summary>
        /// VIS430: Get the Matching tab data for PO lines tab record of Purchase order
        /// </summary>
        /// <param name="OrderLineId"></param>
        /// <returns></returns>
        public JsonResult GetMatchingData(int OrderLineId)
        {
            string retJSON = "";
            if (Session["ctx"] != null)

            {
                Ctx ctx = Session["ctx"] as Ctx;
                OrderLineTabPanelModel obj = new OrderLineTabPanelModel();
                List<MatchingTabPanel> result = obj.GetMatchingData(ctx, OrderLineId);
                retJSON = JsonConvert.SerializeObject(result);
            }
            return Json(retJSON, JsonRequestBehavior.AllowGet);
        }

    }
}