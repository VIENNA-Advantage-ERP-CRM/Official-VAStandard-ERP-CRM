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
    /// Module Name : VAS_092_QuickOperationsWidget
    /// Purpose     : Resolves the existing inventory forms used by Quick Operations.
    /// Chronological development:
    ///   VAI154      2026-06-23 Created
    /// </summary>
    public class VAS_092_QuickOperationsWidgetController : Controller
    {
        private static readonly VLogger Log = VLogger.GetVLogger(typeof(VAS_092_QuickOperationsWidgetController).FullName);

        private const string StockManagementFormName = "Stock Management Form";
        private const string MaterialIssueFormName = "Material Issue Form";

        /// <summary>
        /// Returns the active form IDs after resolving each form through its Export_ID.
        /// </summary>
        /// <returns>Serialized form identifiers.</returns>
        [AjaxAuthorizeAttribute]
        [AjaxSessionFilterAttribute]
        public JsonResult GetFormIds()
        {
            Ctx ctx = Session["ctx"] as Ctx;
            if (ctx == null) { return Json("", JsonRequestBehavior.AllowGet); }

            try
            {
                string json = JsonConvert.SerializeObject(new
                {
                    stock_management_form_id = GetFormId(ctx, StockManagementFormName),
                    material_issue_form_id = GetFormId(ctx, MaterialIssueFormName)
                });
                return Json(json, JsonRequestBehavior.AllowGet);
            }
            catch (Exception ex)
            {
                Log.Log(Level.SEVERE, "VAS_092_QuickOperationsWidget.GetFormIds", ex);
                string json = JsonConvert.SerializeObject(new { error = Msg.GetMsg(ctx, "Error") ?? "Error" });
                return Json(json, JsonRequestBehavior.AllowGet);
            }
        }

        /// <summary>
        /// Resolves a fixed form name to Export_ID, then selects the form by that portable identifier.
        /// </summary>
        /// <param name="ctx">Current application context.</param>
        /// <param name="formName">Fixed application form name.</param>
        /// <returns>Active AD_Form identifier or zero.</returns>
        private int GetFormId(Ctx ctx, string formName)
        {
            if (ctx == null || string.IsNullOrWhiteSpace(formName)) { return 0; }

            string formLookupSql = @"
                SELECT FormLookup.Export_ID
                FROM AD_Form FormLookup
                WHERE FormLookup.IsActive=N'Y'
                  AND FormLookup.Name=@Form_Name
                  AND FormLookup.AD_Client_ID IN (0,@Lookup_Client_ID)
                  AND FormLookup.AD_Org_ID IN (0,COALESCE(NULLIF(@Lookup_Org_ID,0),FormLookup.AD_Org_ID))";

            formLookupSql = AddAccessSql(ctx, formLookupSql, "FormLookup");

            string sql = @"
                SELECT TargetForm.AD_Form_ID
                FROM AD_Form TargetForm
                WHERE TargetForm.IsActive=N'Y'
                  AND TargetForm.AD_Client_ID IN (0,@Target_Client_ID)
                  AND TargetForm.AD_Org_ID IN (0,COALESCE(NULLIF(@Target_Org_ID,0),TargetForm.AD_Org_ID))";

            sql = AddAccessSql(ctx, sql, "TargetForm");
            sql += @"
                  AND TargetForm.Export_ID IN (" + formLookupSql.Trim() + @")
                ORDER BY TargetForm.AD_Form_ID
                FETCH FIRST 1 ROW ONLY";

            SqlParameter[] parameters = new SqlParameter[]
            {
                new SqlParameter("@Form_Name", formName),
                new SqlParameter("@Lookup_Client_ID", ctx.GetAD_Client_ID()),
                new SqlParameter("@Lookup_Org_ID", ctx.GetAD_Org_ID()),
                new SqlParameter("@Target_Client_ID", ctx.GetAD_Client_ID()),
                new SqlParameter("@Target_Org_ID", ctx.GetAD_Org_ID())
            };

            return Util.GetValueOfInt(DB.ExecuteScalar(sql, parameters, null));
        }

        /// <summary>
        /// Adds read-only role access to a physical AD_Form query.
        /// </summary>
        /// <param name="ctx">Current application context.</param>
        /// <param name="sql">Physical-table query.</param>
        /// <param name="tableAlias">Main physical-table alias.</param>
        /// <returns>Role-secured SQL.</returns>
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
