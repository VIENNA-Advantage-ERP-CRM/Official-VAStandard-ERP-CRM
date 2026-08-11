/// <summary>
/// Module Name : VAS
/// Purpose     : Product Overview right tab panel endpoint. Returns the
///               read-only overview payload for the selected M_Product record
///               consumed by the VAS.VAS_190_ProductOverviewRightPanel tab
///               panel, plus the window-id lookup the panel's row navigation
///               needs.
///
///               The panel is read-only: this controller exposes no write
///               action. The product id arriving from the browser is never
///               trusted on its own — the model reads M_Product under MRole, so
///               an id the role cannot see comes back as an empty payload.
/// Chronological development:
///   VAI163   2026-08-10  Created.
/// </summary>

using Newtonsoft.Json;
using System;
using System.Data.SqlClient;
using System.Web.Mvc;
using VAdvantage.DataBase;
using VAdvantage.Logging;
using VAdvantage.Model;
using VAdvantage.Utility;
using VASLogic.Models;

namespace VAS.Controllers
{
    public class VAS_190_ProductOverviewRightPanelController : Controller
    {
        private static readonly VLogger _log =
            VLogger.GetVLogger(typeof(VAS_190_ProductOverviewRightPanelController).FullName);

        public ActionResult Index()
        {
            return View();
        }

        /// <summary>
        /// Returns the overview payload for the selected product.
        /// </summary>
        /// <param name="M_Product_ID">Selected product id.</param>
        /// <returns>JSON-serialized
        /// <see cref="VAS_190_ProductOverviewRightPanelModel.ProductOverviewData"/>.</returns>
        public JsonResult GetProductOverview(int M_Product_ID)
        {
            string retJSON = "";
            if (Session["ctx"] != null)
            {
                Ctx ctx = Session["ctx"] as Ctx;
                VAS_190_ProductOverviewRightPanelModel model =
                    new VAS_190_ProductOverviewRightPanelModel();
                retJSON = JsonConvert.SerializeObject(
                    model.GetProductOverview(ctx, M_Product_ID));
            }
            return Json(retJSON, JsonRequestBehavior.AllowGet);
        }

        /// <summary>
        /// Resolves an AD_Window_ID from a window NAME, for the panel's
        /// record-open path. Window ids differ between environments, so the panel
        /// never carries a numeric one — it names the window and asks here.
        ///
        /// Restricted to windows this tenant can see, preferring the tenant's own
        /// row over the system one. Whether the ROLE may open it stays the
        /// platform's call, made when the window is started.
        /// </summary>
        /// <param name="windowName">AD_Window.Name to resolve.</param>
        /// <returns>JSON carrying the window id; 0 when the name resolves to nothing.</returns>
        public JsonResult GetWindowId(string windowName)
        {
            int windowId = 0;
            if (Session["ctx"] != null && !string.IsNullOrEmpty(windowName))
            {
                Ctx ctx = Session["ctx"] as Ctx;
                try
                {
                    string sql = @"SELECT w.AD_Window_ID
                                   FROM AD_Window w
                                   WHERE w.Name=@Name
                                     AND w.IsActive='Y'
                                     AND w.AD_Client_ID IN (0, @AD_Client_ID)
                                   ORDER BY w.AD_Client_ID DESC";
                    // Every SELECT against a physical table carries the access
                    // filter, one-row lookups included.
                    sql = MRole.GetDefault(ctx).AddAccessSQL(
                        sql, "w", MRole.SQL_FULLYQUALIFIED, MRole.SQL_RO);

                    // Two binds, each occurring once, in the order they appear.
                    SqlParameter[] param = new SqlParameter[]
                    {
                        new SqlParameter("@Name", windowName.Trim()),
                        new SqlParameter("@AD_Client_ID", ctx.GetAD_Client_ID())
                    };

                    System.Data.DataSet ds = DB.ExecuteDataset(sql, param, null);
                    if (ds != null && ds.Tables.Count > 0 && ds.Tables[0].Rows.Count > 0)
                    {
                        windowId = Util.GetValueOfInt(ds.Tables[0].Rows[0]["AD_Window_ID"]);
                    }
                }
                catch (Exception ex)
                {
                    // Non-fatal: the panel falls back to the table's default zoom
                    // target, so a failed lookup costs a nicety, not the panel.
                    _log.Severe("VAS_190 GetWindowId(" + windowName + "): " + ex.Message);
                }
            }
            return Json(JsonConvert.SerializeObject(new { windowId = windowId }),
                        JsonRequestBehavior.AllowGet);
        }
    }
}
