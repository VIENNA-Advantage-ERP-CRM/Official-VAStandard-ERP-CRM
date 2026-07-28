using Newtonsoft.Json;
using System;
using System.Data;
using System.Web.Mvc;
using VAdvantage.Classes;
using VAdvantage.DataBase;
using VAdvantage.Utility;
using VIS.Filters;

namespace VIS.Controllers
{
    /// <summary>
    /// Module Name : New Cycle Count Quick Action Widget (Inventory Count / Physical Inventory dashboard)
    /// Purpose     : Quick Action 1x1 entry point tile launching the New Cycle Count creation flow.
    /// Prefix      : VAS_000_
    /// </summary>
    public class VAS_166_NewCycleCountWidgetController : Controller
    {
        /// <summary>
        /// Gets default creation context (e.g. window ID for Physical Inventory / Cycle Count).
        /// </summary>
        /// <returns>JSON { windowId }</returns>
        [AjaxAuthorizeAttribute]
        [AjaxSessionFilterAttribute]
        public JsonResult GetWindowContext()
        {
            if (Session["ctx"] == null)
            {
                return Json(new
                {
                    error = Msg.GetMsg(Env.GetCtx(), "SessionExpired") ?? "Session Expired"
                }, JsonRequestBehavior.AllowGet);
            }

            Ctx ctx = Session["ctx"] as Ctx;

            // Fetch standard Physical Inventory AD_Window_ID (default 168)
            int windowId = Util.GetValueOfInt(DB.ExecuteScalar("SELECT AD_Window_ID FROM AD_Window WHERE (Name = 'Physical Inventory' OR Name = 'M_Inventory') AND IsActive = 'Y' ORDER BY AD_Window_ID ASC", null, null));


            var result = new
            {
                windowId = windowId > 0 ? windowId : 168
            };

            return Json(JsonConvert.SerializeObject(result), JsonRequestBehavior.AllowGet);
        }
    }
}
