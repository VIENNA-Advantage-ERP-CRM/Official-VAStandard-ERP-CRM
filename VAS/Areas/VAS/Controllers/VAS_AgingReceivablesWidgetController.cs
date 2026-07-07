/******************************************************
 * Module Name    : VAS
 * Purpose        : Aging Receivables widget endpoint
 * chronological  : Development
 * Created Date   : 28 April 2026
 * Created by     : VAI154
 ******************************************************/

using Newtonsoft.Json;
using System.Web.Mvc;
using VAdvantage.Utility;
using VASLogic.Models;

namespace VAS.Controllers
{
    public class VAS_AgingReceivablesWidgetController : Controller
    {
        public ActionResult Index()
        {
            return View();
        }

        /// <summary>
        /// Returns the aging buckets for receivables in the client's accounting currency.
        /// </summary>
        public JsonResult GetAgingReceivables()
        {
            string retJSON = "";
            if (Session["ctx"] != null)
            {
                Ctx ctx = Session["ctx"] as Ctx;
                VAS_AgingReceivablesWidgetModel model = new VAS_AgingReceivablesWidgetModel();
                retJSON = JsonConvert.SerializeObject(model.GetAgingReceivables(ctx));
            }
            return Json(retJSON, JsonRequestBehavior.AllowGet);
        }
    }
}
