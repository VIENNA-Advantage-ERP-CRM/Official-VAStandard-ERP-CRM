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
    /// Module Name : VAS_110_CategoriesWidget
    /// Purpose     : Supplies the "Categories" KPI for the Product / Item Master
    ///               dashboard - the distinct count of active product categories,
    ///               with the distinct count of parent categories ("product
    ///               families", confirmed by the dev as M_Product_Category_Parent_ID)
    ///               as the meta caption. Widget number 110 - reassign on hand-off.
    /// Chronological development:
    ///   110         2026-07-15 Created
    /// </summary>
    public class VAS_110_CategoriesWidgetController : Controller
    {
        private static readonly VLogger Log = VLogger.GetVLogger(typeof(VAS_110_CategoriesWidgetController).FullName);

        /// <summary>
        /// Returns the category and product-family (parent-category) counts.
        /// </summary>
        [AjaxAuthorizeAttribute]
        [AjaxSessionFilterAttribute]
        public JsonResult GetCategories()
        {
            Ctx ctx = Session["ctx"] as Ctx;
            if (ctx == null)
            {
                return Json("", JsonRequestBehavior.AllowGet);
            }

            try
            {
                CategoriesResult result = GetCategoriesData(ctx);
                string json = JsonConvert.SerializeObject(result);
                return Json(json, JsonRequestBehavior.AllowGet);
            }
            catch (Exception ex)
            {
                Log.Log(Level.SEVERE, "VAS_110_CategoriesWidget.GetCategories", ex);
                string json = JsonConvert.SerializeObject(new { error = Msg.GetMsg(ctx, "Error") ?? "Error" });
                return Json(json, JsonRequestBehavior.AllowGet);
            }
        }

        /// <summary>
        /// Counts distinct active product categories and, for the meta caption, the
        /// distinct parent categories they roll up to ("product families").
        /// COUNT(DISTINCT parent) ignores the NULL parents automatically on both
        /// Oracle and PostgreSQL.
        /// </summary>
        /// <param name="ctx">Current application context.</param>
        /// <returns>Category and family counts.</returns>
        private CategoriesResult GetCategoriesData(Ctx ctx)
        {
            CategoriesResult result = new CategoriesResult();
            if (ctx == null) { return result; }

            string sql = @"
                SELECT COUNT(DISTINCT Category.M_Product_Category_ID) AS Category_Count,
                       COUNT(DISTINCT Category.M_Product_Category_Parent_ID) AS Family_Count
                FROM M_Product_Category Category
                WHERE Category.IsActive=N'Y'
                  AND Category.AD_Client_ID=@AD_Client_ID
                  AND Category.AD_Org_ID IN (0,COALESCE(NULLIF(@AD_Org_ID,0),Category.AD_Org_ID))";

            sql = MRole.GetDefault(ctx).AddAccessSQL(
                sql,
                "Category",
                MRole.SQL_FULLYQUALIFIED,
                MRole.SQL_RO
            );

            SqlParameter[] parameters = new SqlParameter[]
            {
                new SqlParameter("@AD_Client_ID", ctx.GetAD_Client_ID()),
                new SqlParameter("@AD_Org_ID", ctx.GetAD_Org_ID())
            };

            IDataReader reader = null;
            try
            {
                reader = DB.ExecuteReader(sql, parameters);
                if (reader != null && reader.Read())
                {
                    result.category_count = Util.GetValueOfInt(reader["Category_Count"]);
                    result.family_count = Util.GetValueOfInt(reader["Family_Count"]);
                }
            }
            finally
            {
                if (reader != null) { reader.Close(); reader.Dispose(); }
            }

            return result;
        }

        /// <summary>Categories KPI response.</summary>
        private class CategoriesResult
        {
            public int category_count { get; set; }
            public int family_count { get; set; }
        }
    }
}
