using Newtonsoft.Json;
using System;
using System.Data;
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
    /// Module Name : VAS_112_SlowMoversWidget
    /// Purpose     : Supplies the "Slow Movers" KPI for the Product / Item Master
    ///               dashboard - the count of items that are laying in the
    ///               warehouse (on-hand > 0) but were not sold or issued in the
    ///               last 30 days (customer shipments 'C-' and inventory issues
    ///               'I-' on M_Transaction). Inactive and discontinued products
    ///               are excluded. Same slow definition as the Moving Analysis
    ///               widget. Widget number 112 - reassign on hand-off.
    /// Chronological development:
    ///   112         2026-07-15 Created
    /// </summary>
    public class VAS_112_SlowMoversWidgetController : Controller
    {
        private static readonly VLogger Log = VLogger.GetVLogger(typeof(VAS_112_SlowMoversWidgetController).FullName);

        // Slow = no issue/sale in this many days (per the confirmed logic).
        private const int SLOW_WINDOW_DAYS = 30;

        /// <summary>
        /// Returns the count of slow-mover items.
        /// </summary>
        [AjaxAuthorizeAttribute]
        [AjaxSessionFilterAttribute]
        public JsonResult GetSlowMovers()
        {
            Ctx ctx = Session["ctx"] as Ctx;
            if (ctx == null)
            {
                return Json("", JsonRequestBehavior.AllowGet);
            }

            try
            {
                int slowMoversCount = GetSlowMoversData(ctx);
                string json = JsonConvert.SerializeObject(new { slow_movers_count = slowMoversCount, window_days = SLOW_WINDOW_DAYS });
                return Json(json, JsonRequestBehavior.AllowGet);
            }
            catch (Exception ex)
            {
                Log.Log(Level.SEVERE, "VAS_112_SlowMoversWidget.GetSlowMovers", ex);
                string json = JsonConvert.SerializeObject(new { error = Msg.GetMsg(ctx, "Error") ?? "Error" });
                return Json(json, JsonRequestBehavior.AllowGet);
            }
        }

        /// <summary>
        /// Counts active, non-discontinued items with on-hand stock but no
        /// outbound issue/sale in the last 30 days. MRole is applied to each
        /// physical-table block and GROUP BY is appended AFTER the access
        /// predicate (VAS_073 / ORA-00907 lesson). Plain ASCII literals only for
        /// Oracle + PostgreSQL compatibility.
        /// </summary>
        /// <param name="ctx">Current application context.</param>
        /// <returns>Slow-mover item count.</returns>
        private int GetSlowMoversData(Ctx ctx)
        {
            if (ctx == null) { return 0; }

            DateTime cutoff = DateTime.Now.Date.AddDays(-SLOW_WINDOW_DAYS);

            // Issued/sold quantity per product in the window (MovementQty < 0 =
            // stock leaving); only customer shipments and inventory issues count.
            string issuesSql = @"
                SELECT Trans.M_Product_ID,
                       SUM(CASE WHEN Trans.MovementQty<0 AND Trans.MovementType IN ('C-','I-') THEN -Trans.MovementQty ELSE 0 END) AS Issued_Qty
                FROM M_Transaction Trans
                WHERE Trans.IsActive='Y'
                  AND Trans.AD_Client_ID=@Issue_Client_ID
                  AND Trans.AD_Org_ID IN (0,COALESCE(NULLIF(@Issue_Org_ID,0),Trans.AD_Org_ID))
                  AND Trans.MovementDate>=@Issue_Cutoff";

            string stockSql = @"
                SELECT Storage.M_Product_ID,
                       SUM(COALESCE(Storage.QtyOnHand,0)) AS On_Hand
                FROM M_Storage Storage
                WHERE Storage.IsActive='Y'
                  AND Storage.AD_Client_ID=@Stock_Client_ID
                  AND Storage.AD_Org_ID IN (0,COALESCE(NULLIF(@Stock_Org_ID,0),Storage.AD_Org_ID))";

            string productSql = @"
                SELECT Product.M_Product_ID
                FROM M_Product Product
                WHERE Product.IsActive='Y'
                  AND Product.AD_Client_ID=@Product_Client_ID
                  AND Product.AD_Org_ID IN (0,COALESCE(NULLIF(@Product_Org_ID,0),Product.AD_Org_ID))
                  AND (Product.Discontinued IS NULL OR Product.Discontinued='N')";

            issuesSql = AddAccessSql(ctx, issuesSql, "Trans") + " GROUP BY Trans.M_Product_ID";
            stockSql = AddAccessSql(ctx, stockSql, "Storage") + " GROUP BY Storage.M_Product_ID";
            productSql = AddAccessSql(ctx, productSql, "Product");

            string sql = string.Format(@"
                WITH Issues AS (
                    {0}
                ),
                Stock AS (
                    {1}
                ),
                Products AS (
                    {2}
                )
                SELECT COUNT(*) AS Slow_Movers_Count
                FROM Products
                LEFT OUTER JOIN Issues ON (Issues.M_Product_ID=Products.M_Product_ID)
                LEFT OUTER JOIN Stock ON (Stock.M_Product_ID=Products.M_Product_ID)
                WHERE COALESCE(Stock.On_Hand,0)>0
                  AND COALESCE(Issues.Issued_Qty,0)=0",
                issuesSql,
                stockSql,
                productSql
            );

            SqlParameter[] parameters = new SqlParameter[]
            {
                new SqlParameter("@Issue_Client_ID", ctx.GetAD_Client_ID()),
                new SqlParameter("@Issue_Org_ID", ctx.GetAD_Org_ID()),
                new SqlParameter("@Issue_Cutoff", SqlDbType.DateTime) { Value = cutoff },
                new SqlParameter("@Stock_Client_ID", ctx.GetAD_Client_ID()),
                new SqlParameter("@Stock_Org_ID", ctx.GetAD_Org_ID()),
                new SqlParameter("@Product_Client_ID", ctx.GetAD_Client_ID()),
                new SqlParameter("@Product_Org_ID", ctx.GetAD_Org_ID())
            };

            return Util.GetValueOfInt(DB.ExecuteScalar(sql, parameters, null));
        }

        /// <summary>
        /// Adds read-only role access to a query whose named alias is a physical table.
        /// </summary>
        private string AddAccessSql(Ctx ctx, string sql, string tableAlias)
        {
            return MRole.GetDefault(ctx).AddAccessSQL(
                sql,
                tableAlias,
                MRole.SQL_FULLYQUALIFIED,
                MRole.SQL_RO
            );
        }
    }
}
