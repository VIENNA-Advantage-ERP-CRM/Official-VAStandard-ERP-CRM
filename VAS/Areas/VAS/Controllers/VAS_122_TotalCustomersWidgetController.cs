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
    ///   VAI052      2026-08-10 Drill-through rows now carry the customer's contact
    ///                          people (all of them, capped) instead of the sales rep
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

        // Contact names listed per customer in the drill-through row meta. Beyond this
        // the row reports "+N more" rather than growing the payload without bound.
        private const int MaxContactsPerCustomer = 5;

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
        /// Same authorized scope (MRole on C_BPartner) as the count. Each row carries
        /// the customer's contact people (AD_User rows on the partner), not the
        /// sales rep - see GetContactsByCustomer.
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
                           COALESCE(bp.ActualLifeTimeValue, 0) AS Customer_Value
                    FROM C_BPartner bp
                    WHERE bp.IsCustomer = 'Y'
                      AND bp.IsActive = 'Y'
                      AND bp.AD_Client_ID = @Client_ID";
                rowsSql = MRole.GetDefault(ctx).AddAccessSQL(rowsSql, "bp", MRole.SQL_FULLYQUALIFIED, MRole.SQL_RO);

                string sql = @"
                    SELECT s.Customer_Id,
                           s.Customer_Name,
                           s.Customer_Value,
                           COUNT(1) OVER () AS Total_Rows
                    FROM (
                        " + rowsSql + @"
                    ) s
                    ORDER BY s.Customer_Value DESC, s.Customer_Name ASC
                    OFFSET " + offset + @" ROWS FETCH NEXT " + limit + @" ROWS ONLY";

                System.Collections.Generic.List<CustomerRow> rows = new System.Collections.Generic.List<CustomerRow>();
                int total = 0;

                IDataReader dr = null;
                try
                {
                    dr = DB.ExecuteReader(sql, new SqlParameter[] { new SqlParameter("@Client_ID", ctx.GetAD_Client_ID()) });
                    while (dr != null && dr.Read())
                    {
                        total = Util.GetValueOfInt(dr["Total_Rows"]);
                        rows.Add(new CustomerRow
                        {
                            CustomerId = Util.GetValueOfInt(dr["Customer_Id"]),
                            CustomerName = Util.GetValueOfString(dr["Customer_Name"]),
                            CustomerValue = Util.GetValueOfDecimal(dr["Customer_Value"])
                        });
                    }
                }
                finally { CloseReader(dr); }

                // Contacts for just this page of customers (at most `limit` ids), so
                // the row meta shows who to call rather than the internal sales rep.
                System.Collections.Generic.Dictionary<int, ContactSet> contacts = GetContactsByCustomer(ctx, rows);

                System.Collections.Generic.List<object> items = new System.Collections.Generic.List<object>();
                foreach (CustomerRow row in rows)
                {
                    ContactSet set;
                    if (!contacts.TryGetValue(row.CustomerId, out set)) { set = new ContactSet(); }
                    items.Add(new
                    {
                        customerId = row.CustomerId,
                        customerName = row.CustomerName,
                        contacts = set.Names,
                        contactCount = set.TotalCount,
                        customerValue = row.CustomerValue
                    });
                }

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
        /// Loads the contact people (AD_User rows attached to the partner) for the
        /// supplied page of customers. Ranked primary-first using the same rule as
        /// VAS_120 / VAS_126 - a contact with an e-mail, then most recently updated -
        /// and capped at MaxContactsPerCustomer names per customer so one partner with
        /// a large address book cannot bloat the payload; the untruncated count is
        /// returned alongside so the UI can say how many were not listed.
        /// </summary>
        /// <param name="ctx">Authenticated request context.</param>
        /// <param name="rows">The already-paged customers to load contacts for.</param>
        /// <returns>Contacts keyed by C_BPartner_ID; empty when the page has no rows.</returns>
        private System.Collections.Generic.Dictionary<int, ContactSet> GetContactsByCustomer(
            Ctx ctx,
            System.Collections.Generic.List<CustomerRow> rows)
        {
            System.Collections.Generic.Dictionary<int, ContactSet> result =
                new System.Collections.Generic.Dictionary<int, ContactSet>();
            if (ctx == null || rows == null || rows.Count == 0) { return result; }

            // Server-derived ids from the page just read - never request input.
            System.Collections.Generic.List<string> ids = new System.Collections.Generic.List<string>();
            foreach (CustomerRow row in rows)
            {
                if (row.CustomerId > 0) { ids.Add(row.CustomerId.ToString()); }
            }
            if (ids.Count == 0) { return result; }

            // Secondary source: NOT MRole-filtered, matching VAS_120 / VAS_126. Two
            // reasons. (1) Access is already enforced upstream - the ids below come
            // from the MRole-filtered customer page, so only contacts of customers the
            // role may see are ever requested. (2) AddAccessSQL splices its predicate
            // in ahead of the first ORDER BY in the string, which here belongs to the
            // ROW_NUMBER() window - it would inject "WHERE ..." inside OVER(...) and
            // produce a syntax error.
            string contactsSql = @"
                SELECT Contact.C_BPartner_ID AS Customer_Id,
                       Contact.Name AS Contact_Name,
                       ROW_NUMBER() OVER (
                           PARTITION BY Contact.C_BPartner_ID
                           ORDER BY CASE WHEN Contact.EMail IS NOT NULL THEN 0 ELSE 1 END,
                                    Contact.Updated DESC,
                                    Contact.Name ASC,
                                    Contact.AD_User_ID ASC
                       ) AS Contact_Rank,
                       COUNT(1) OVER (PARTITION BY Contact.C_BPartner_ID) AS Contact_Total
                FROM AD_User Contact
                WHERE Contact.IsActive = 'Y'
                  AND Contact.AD_Client_ID = @Client_ID
                  AND Contact.Name IS NOT NULL
                  AND Contact.C_BPartner_ID IN (" + string.Join(",", ids) + @")";

            string sql = @"
                SELECT c.Customer_Id,
                       c.Contact_Name,
                       c.Contact_Total
                FROM (
                    " + contactsSql + @"
                ) c
                WHERE c.Contact_Rank <= " + MaxContactsPerCustomer + @"
                ORDER BY c.Customer_Id ASC, c.Contact_Rank ASC";

            IDataReader dr = null;
            try
            {
                dr = DB.ExecuteReader(sql, new SqlParameter[] { new SqlParameter("@Client_ID", ctx.GetAD_Client_ID()) });
                while (dr != null && dr.Read())
                {
                    int customerId = Util.GetValueOfInt(dr["Customer_Id"]);
                    string name = Util.GetValueOfString(dr["Contact_Name"]);
                    if (customerId <= 0 || string.IsNullOrEmpty(name)) { continue; }

                    ContactSet set;
                    if (!result.TryGetValue(customerId, out set))
                    {
                        set = new ContactSet();
                        result[customerId] = set;
                    }
                    set.Names.Add(name);
                    set.TotalCount = Util.GetValueOfInt(dr["Contact_Total"]);
                }
            }
            finally
            {
                CloseReader(dr);
            }

            return result;
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

        /// <summary>One drill-through customer, before contacts are attached.</summary>
        private class CustomerRow
        {
            public int CustomerId { get; set; }
            public string CustomerName { get; set; }
            public decimal CustomerValue { get; set; }
        }

        /// <summary>
        /// The listed contact names for a customer plus the untruncated contact count,
        /// so the UI can render "+N more" when the list was capped.
        /// </summary>
        private class ContactSet
        {
            public ContactSet()
            {
                Names = new System.Collections.Generic.List<string>();
            }

            public System.Collections.Generic.List<string> Names { get; private set; }
            public int TotalCount { get; set; }
        }
    }
}
