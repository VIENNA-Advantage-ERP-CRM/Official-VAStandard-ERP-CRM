/*
 * Zoom Window Controller
 *
 * Shared server side of VAS.ZoomUtil (Scripts/app/util/VAS_ZoomUtil.js): resolves an
 * AD_Window_ID from a window name so a widget/panel can zoom to a record without
 * owning its own lookup endpoint.
 *
 * Resolution order is PoReceiptTabPanelModel.GetWindowId():
 *   new window name -> old window name -> VAS_ZoomScreenConfig mapping.
 *
 * Labels / Message Keys - none (no user-facing text).
 */

using System;
using Newtonsoft.Json;
using System.Web.Mvc;
using VAdvantage.Logging;
using VAdvantage.Utility;
using VASLogic.Models;

namespace VAS.Controllers
{
    public class VAS_ZoomWindowController : Controller
    {
        /// <summary>
        /// Active AD_Window_ID for a window name, or 0 when none matches.
        /// </summary>
        /// <param name="newWindowName">Preferred (new) AD_Window.Name.</param>
        /// <param name="oldWindowName">Fallback (legacy) AD_Window.Name.</param>
        /// <returns>JSON string { windowId } inside a JSON response.</returns>
        public JsonResult GetWindowId(string newWindowName, string oldWindowName)
        {
            int windowId = 0;

            if (Session["ctx"] != null)
            {
                string newScreen = Util.GetValueOfString(newWindowName);
                string oldScreen = Util.GetValueOfString(oldWindowName);

                if (!string.IsNullOrEmpty(newScreen) || !string.IsNullOrEmpty(oldScreen))
                {
                    try
                    {
                        PoReceiptTabPanelModel windowResolver = new PoReceiptTabPanelModel();
                        windowId = windowResolver.GetWindowId(newScreen, oldScreen);
                    }
                    catch (Exception ex)
                    {
                        VLogger.Get().SaveError("VAS_ZoomWindow_GetWindowId", ex);
                        windowId = 0;
                    }
                }
            }

            return Json(JsonConvert.SerializeObject(new { windowId = windowId }), JsonRequestBehavior.AllowGet);
        }
    }
}
