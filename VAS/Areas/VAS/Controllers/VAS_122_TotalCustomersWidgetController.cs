/******************************************************
 * Module Name    : VAS
 * Purpose        : Customers module Total Customers KPI widget endpoint
 * chronological  : Development
 * Created Date   : 2026-07-20
 * Created by     : VAI052
 ******************************************************/

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
    /// Module Name : VAS_122_TotalCustomersWidget
    /// Purpose     : Static 2x1 KPI tile - the count of active business partners
    ///               marked as customers within the logged-in tenant and the
    ///               organizations the role may access, plus a truthful combined
    ///               "lifetime value" subline (SUM of C_BPartner.ActualLifeTimeValue).
    ///               The supplied application dictionary has NO recurring-revenue
    ///               column, so the subline is never labelled ARR. Tenant +
    ///               organization + record access come from MRole applied to the
    ///               single physical table C_BPartner.
    /// Chronological development:
    ///   VAI052      2026-07-20 Created
    /// </summary>
    public class VAS_122_TotalCustomersWidgetController : Controller
    {
        private static readonly VLogger Log = VLogger.GetVLogger(typeof(VAS_122_TotalCustomersWidgetController).FullName);

        // Value-subline mode. Default is the schema-safe "lifetime_value"
        // (SUM of ActualLifeTimeValue). "count_only" hides the amount subline.
        // "arr" is only valid once an approved recurring-revenue column exists and
        // ArrColumn below is set to that verified column name; until then the code
        // falls back to lifetime_value so no non-ARR measure is ever shown as ARR.
        private const string ValueModeLifetimeValue = "lifetime_value";
        private const string ValueModeCountOnly = "count_only";
        private const string ValueModeArr = "arr";
        private const string ValueMode = ValueModeLifetimeValue;

        // Approved annual-recurring-revenue column on C_BPartner. Intentionally
        // EMPTY: the supplied metadata does not prove one exists. Populate only
        // after the column/view is installed and validated in the application
        // dictionary (a recommended name is VAS_ARR). A hard-coded server constant,
        // never a value accepted from the browser.
        private const string ArrColumn = "";

        /// <summary>
        /// KPI tile data: the count of active customers in the authorized scope and
        /// the combined value subline (mode-dependent), with tenant currency info.
        /// </summary>
        /// <returns>
        /// JSON { total_customers, total_customer_value, value_mode,
        /// currency_symbol, currency_iso, std_precision } or { error }.
        /// </returns>
        [AjaxAuthorizeAttribute]
        [AjaxSessionFilterAttribute]
        public JsonResult GetTotalCustomers()
        {
            if (Session["ctx"] == null)
            {
                return Json(new
                {
                    error = Msg.GetMsg(Env.GetCtx(), "SessionExpired") ?? "Session Expired"
                }, JsonRequestBehavior.AllowGet);
            }

            Ctx ctx = Session["ctx"] as Ctx;

            try
            {
                string effectiveMode = ResolveValueMode();

                // The value column is a fixed server-side identifier (never from the
                // request). count_only computes no amount.
                string valueSelect;
                if (effectiveMode == ValueModeCountOnly)
                {
                    valueSelect = "0 AS Total_Customer_Value";
                }
                else if (effectiveMode == ValueModeArr)
                {
                    valueSelect = "COALESCE(SUM(bp." + ArrColumn + "), 0) AS Total_Customer_Value";
                }
                else
                {
                    valueSelect = "COALESCE(SUM(bp.ActualLifeTimeValue), 0) AS Total_Customer_Value";
                }

                string sql = @"
                    SELECT COUNT(bp.C_BPartner_ID) AS Total_Customers,
                           " + valueSelect + @"
                    FROM C_BPartner bp
                    WHERE bp.IsCustomer = 'Y'
                      AND bp.IsActive = 'Y'
                      AND bp.AD_Client_ID = @Client_ID";

                // MRole supplies tenant + organization + record access on the only
                // physical table, applied to the main table alias "bp".
                sql = MRole.GetDefault(ctx).AddAccessSQL(
                    sql,
                    "bp",
                    MRole.SQL_FULLYQUALIFIED,
                    MRole.SQL_RO
                );

                int totalCustomers = 0;
                decimal totalCustomerValue = 0;

                IDataReader dr = null;
                try
                {
                    dr = DB.ExecuteReader(
                        sql,
                        new SqlParameter[] { new SqlParameter("@Client_ID", ctx.GetAD_Client_ID()) }
                    );

                    if (dr != null && dr.Read())
                    {
                        totalCustomers = Util.GetValueOfInt(dr["Total_Customers"]);
                        totalCustomerValue = Util.GetValueOfDecimal(dr["Total_Customer_Value"]);
                    }
                }
                finally
                {
                    CloseReader(dr);
                }

                SchemaCurrency currency = GetSchemaCurrency(ctx);

                var result = new
                {
                    total_customers = totalCustomers,
                    total_customer_value = totalCustomerValue,
                    value_mode = effectiveMode,
                    currency_symbol = currency.Symbol,
                    currency_iso = currency.IsoCode,
                    std_precision = currency.StdPrecision
                };

                return Json(JsonConvert.SerializeObject(result), JsonRequestBehavior.AllowGet);
            }
            catch (Exception ex)
            {
                Log.Log(Level.SEVERE, "VAS_122_TotalCustomersWidget.GetTotalCustomers", ex);
                return Json(new { error = ex.Message }, JsonRequestBehavior.AllowGet);
            }
        }

        /// <summary>
        /// Drill-through list: the active customers contributing to the KPI count,
        /// ranked by combined lifetime value (ActualLifeTimeValue) descending, paged.
        /// Same authorized scope (MRole on C_BPartner) as the count.
        /// </summary>
        /// <param name="offset">Zero-based paging offset.</param>
        /// <param name="limit">Page size; up to 25.</param>
        /// <returns>JSON { items:[...], total, offset, limit, currency_* } or { error }.</returns>
        [AjaxAuthorizeAttribute]
        [AjaxSessionFilterAttribute]
        public JsonResult GetCustomers(int offset = 0, int limit = 7)
        {
            if (Session["ctx"] == null)
            {
                return Json(new { error = Msg.GetMsg(Env.GetCtx(), "SessionExpired") ?? "Session Expired" }, JsonRequestBehavior.AllowGet);
            }

            Ctx ctx = Session["ctx"] as Ctx;

            if (offset < 0) { offset = 0; }
            if (limit <= 0 || limit > 25) { limit = 7; }

            try
            {
                string rowsSql = @"
                    SELECT bp.C_BPartner_ID AS Customer_Id,
                           bp.Name AS Customer_Name,
                           COALESCE(owner.Name, N'') AS Owner_Name,
                           COALESCE(bp.ActualLifeTimeValue, 0) AS Customer_Value
                    FROM C_BPartner bp
                    LEFT OUTER JOIN AD_User owner ON (owner.AD_User_ID=bp.SalesRep_ID AND owner.AD_Client_ID=bp.AD_Client_ID AND owner.IsActive = 'Y')
                    WHERE bp.IsCustomer = 'Y'
                      AND bp.IsActive = 'Y'
                      AND bp.AD_Client_ID = @Client_ID";
                rowsSql = MRole.GetDefault(ctx).AddAccessSQL(rowsSql, "bp", MRole.SQL_FULLYQUALIFIED, MRole.SQL_RO);

                string sql = @"
                    SELECT s.Customer_Id,
                           s.Customer_Name,
                           s.Owner_Name,
                           s.Customer_Value,
                           COUNT(1) OVER () AS Total_Rows
                    FROM (
                        " + rowsSql + @"
                    ) s
                    ORDER BY s.Customer_Value DESC, s.Customer_Name ASC
                    OFFSET " + offset + @" ROWS FETCH NEXT " + limit + @" ROWS ONLY";

                System.Collections.Generic.List<object> items = new System.Collections.Generic.List<object>();
                int total = 0;

                IDataReader dr = null;
                try
                {
                    dr = DB.ExecuteReader(sql, new SqlParameter[] { new SqlParameter("@Client_ID", ctx.GetAD_Client_ID()) });
                    while (dr != null && dr.Read())
                    {
                        total = Util.GetValueOfInt(dr["Total_Rows"]);
                        items.Add(new
                        {
                            customerId = Util.GetValueOfInt(dr["Customer_Id"]),
                            customerName = Util.GetValueOfString(dr["Customer_Name"]),
                            ownerName = Util.GetValueOfString(dr["Owner_Name"]),
                            customerValue = Util.GetValueOfDecimal(dr["Customer_Value"])
                        });
                    }
                }
                finally { CloseReader(dr); }

                SchemaCurrency currency = GetSchemaCurrency(ctx);

                var result = new
                {
                    items = items,
                    total = total,
                    offset = offset,
                    limit = limit,
                    currency_symbol = currency.Symbol,
                    currency_iso = currency.IsoCode,
                    std_precision = currency.StdPrecision
                };
                return Json(JsonConvert.SerializeObject(result), JsonRequestBehavior.AllowGet);
            }
            catch (Exception ex)
            {
                Log.Log(Level.SEVERE, "VAS_122_TotalCustomersWidget.GetCustomers", ex);
                return Json(new { error = ex.Message }, JsonRequestBehavior.AllowGet);
            }
        }

        /// <summary>
        /// Resolves the effective value mode, degrading "arr" to "lifetime_value"
        /// when no approved ARR column is configured so a non-ARR measure is never
        /// presented as ARR.
        /// </summary>
        /// <returns>The value mode actually used by the query.</returns>
        private string ResolveValueMode()
        {
            if (ValueMode == ValueModeArr && !string.IsNullOrEmpty(ArrColumn))
            {
                return ValueModeArr;
            }
            if (ValueMode == ValueModeCountOnly)
            {
                return ValueModeCountOnly;
            }
            return ValueModeLifetimeValue;
        }

        /// <summary>
        /// Reads the tenant's primary accounting-schema currency (symbol, ISO,
        /// standard precision) for client-side amount formatting.
        /// </summary>
        /// <param name="ctx">Authenticated request context.</param>
        /// <returns>Populated currency info; empty on failure.</returns>
        private SchemaCurrency GetSchemaCurrency(Ctx ctx)
        {
            SchemaCurrency currency = new SchemaCurrency();
            if (ctx == null) { return currency; }

            string sql = @"
                SELECT Currency.StdPrecision AS Std_Precision,
                       CASE
                           WHEN Currency.CurSymbol IS NOT NULL THEN Currency.CurSymbol
                           ELSE Currency.ISO_Code
                       END AS Currency_Symbol,
                       Currency.ISO_Code AS Currency_ISO
                FROM AD_ClientInfo ClientInfo
                INNER JOIN C_AcctSchema AcctSchema ON (AcctSchema.C_AcctSchema_ID=ClientInfo.C_AcctSchema1_ID AND AcctSchema.IsActive = 'Y')
                INNER JOIN C_Currency Currency ON (Currency.C_Currency_ID=AcctSchema.C_Currency_ID AND Currency.IsActive = 'Y')
                WHERE ClientInfo.IsActive = 'Y'
                  AND ClientInfo.AD_Client_ID = @Client_ID";

            sql = MRole.GetDefault(ctx).AddAccessSQL(
                sql,
                "ClientInfo",
                MRole.SQL_FULLYQUALIFIED,
                MRole.SQL_RO
            );

            IDataReader dr = null;
            try
            {
                dr = DB.ExecuteReader(
                    sql,
                    new SqlParameter[] { new SqlParameter("@Client_ID", ctx.GetAD_Client_ID()) }
                );

                if (dr != null && dr.Read())
                {
                    currency.StdPrecision = Util.GetValueOfInt(dr["Std_Precision"]);
                    currency.Symbol = Util.GetValueOfString(dr["Currency_Symbol"]);
                    currency.IsoCode = Util.GetValueOfString(dr["Currency_ISO"]);
                }
            }
            finally
            {
                CloseReader(dr);
            }

            return currency;
        }

        private void CloseReader(IDataReader reader)
        {
            if (reader == null) { return; }
            reader.Close();
            reader.Dispose();
        }

        private class SchemaCurrency
        {
            public string Symbol { get; set; }
            public string IsoCode { get; set; }
            public int StdPrecision { get; set; }
        }
    }
}
