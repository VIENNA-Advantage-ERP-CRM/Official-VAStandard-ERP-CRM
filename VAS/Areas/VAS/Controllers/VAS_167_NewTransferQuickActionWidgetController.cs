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
    /// Module Name : New Transfer Quick Action Widget (Material Transfer dashboard)
    /// Purpose     : Quick Action 1x1 entry point tile launching the New Stock Transfer creation flow.
    /// ID Prefix   : VAS_167_
    /// </summary>
    public class VAS_167_NewTransferQuickActionWidgetController : Controller
    {
        /// <summary>
        /// Gets default creation context (window ID for Material Transfer / M_Movement).
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

            // Fetch Material Transfer AD_Window_ID (default 256 or lookup by window name)
            int windowId = Util.GetValueOfInt(DB.ExecuteScalar("SELECT AD_Window_ID FROM AD_Window WHERE (Name = 'Material Transfer' OR Name = 'M_Movement' OR Name = 'Movements') AND IsActive = 'Y' ORDER BY AD_Window_ID ASC", null, null));

            var result = new
            {
                windowId = windowId > 0 ? windowId : 256
            };

            return Json(JsonConvert.SerializeObject(result), JsonRequestBehavior.AllowGet);
        }
    }
}
