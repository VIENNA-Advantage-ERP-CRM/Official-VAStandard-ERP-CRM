using Newtonsoft.Json;
using System;
using System.Data.SqlClient;
using System.Web.Mvc;
using VAdvantage.DataBase;
using VAdvantage.Logging;
using VAdvantage.Model;
using VAdvantage.Utility;
using VIS.Filters;

namespace VAS.Controllers
{
    /// <summary>
    /// Module Name : VAS_076_TotalStockQtyWidget
    /// Purpose     : Supplies the Total Stock Qty KPI.
    /// Chronological development:
    ///   VAI154      2026-06-21 Created
    ///   VAI154      2026-06-22 Compliance update
    /// </summary>
    public class VAS_076_TotalStockQtyWidgetController : Controller
    {
        private static readonly VLogger Log = VLogger.GetVLogger(typeof(VAS_076_TotalStockQtyWidgetController).FullName);

        /// <summary>
        /// Returns secured active on-hand quantity.
        /// </summary>
        [AjaxAuthorizeAttribute]
        [AjaxSessionFilterAttribute]
        public JsonResult GetTotalStockQty()
        {
            if (Session["ctx"] == null)
            {
                return Json("", JsonRequestBehavior.AllowGet);
            }

            Ctx ctx = Session["ctx"] as Ctx;

            try
            {
                decimal totalStockQty = GetTotalStockQtyData(ctx);
                string json = JsonConvert.SerializeObject(new { total_stock_qty = totalStockQty });
                return Json(json, JsonRequestBehavior.AllowGet);
            }
            catch (Exception ex)
            {
                Log.Log(Level.SEVERE, "VAS_076_TotalStockQtyWidget.GetTotalStockQty", ex);
                string json = JsonConvert.SerializeObject(new { error = Msg.GetMsg(ctx, "Error") ?? "Error" });
                return Json(json, JsonRequestBehavior.AllowGet);
            }
        }

        private decimal GetTotalStockQtyData(Ctx ctx)
        {
            if (ctx == null) { return 0; }

            string sql = @"
                SELECT COALESCE(SUM(Storage.QtyOnHand),0) AS Total_Stock_Qty
                FROM M_Storage Storage
                WHERE Storage.IsActive=N'Y'
                  AND Storage.AD_Client_ID=@AD_Client_ID
                  AND Storage.AD_Org_ID IN (0,COALESCE(NULLIF(@AD_Org_ID,0),Storage.AD_Org_ID))";

            sql = MRole.GetDefault(ctx).AddAccessSQL(
                sql,
                "Storage",
                MRole.SQL_FULLYQUALIFIED,
                MRole.SQL_RO
            );

            SqlParameter[] parameters = new SqlParameter[]
            {
                new SqlParameter("@AD_Client_ID", ctx.GetAD_Client_ID()),
                new SqlParameter("@AD_Org_ID", ctx.GetAD_Org_ID())
            };

            return Util.GetValueOfDecimal(DB.ExecuteScalar(sql, parameters, null));
        }
    }
}
