using Newtonsoft.Json;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Web;
using System.Web.Mvc;
using VAdvantage.Utility;
using VASLogic.Models;
using VIS.Filters;

namespace VAS.Areas.VAS.Controllers
{
    public class VAS_RequestController : Controller
    {
        //get request
        [AjaxAuthorizeAttribute]
        [AjaxSessionFilterAttribute]
        public JsonResult GetJSONHomeRequest(int pagesize, int page, Boolean isTabDataRef)
        {
            int count = 0;
            List<HomeRequest> lst = null;
            string error = "";
            if (Session["ctx"] != null)
            {
                VAS_RequestModel objHomeHelp = new VAS_RequestModel();
                Ctx ct = Session["ctx"] as Ctx;
                lst = new List<HomeRequest>();
                if (isTabDataRef)
                {
                    count = objHomeHelp.getRequestCnt(ct);
                }
                lst = objHomeHelp.getHomeRequest(ct, pagesize, page);
            }
            else
            {
                error = "Session Expired";
            }
            return Json(new { count = count, data = JsonConvert.SerializeObject(lst), error = error }, JsonRequestBehavior.AllowGet);
        }
    }
}