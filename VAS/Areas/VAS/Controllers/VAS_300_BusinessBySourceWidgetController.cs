/******************************************************
 * Module Name    : VAS
 * Purpose        : Customers module Business by Source widget endpoints
 * chronological  : Development
 * Created Date   : 2026-09-23
 * Created by     : VAI052
 ******************************************************/

using Newtonsoft.Json;
using System;
using System.Collections.Generic;
using System.Data;
using System.Data.SqlClient;
using System.Globalization;
using System.Web.Mvc;
using VAdvantage.DataBase;
using VAdvantage.Logging;
using VAdvantage.Model;
using VAdvantage.Utility;
using VIS.Filters;

namespace VAS.Controllers
{
    /// <summary>
    /// Module Name : VAS_300_BusinessBySourceWidget
    /// Purpose     : 3x2 ranked distribution - business generated per customer
    ///               acquisition source (R_Source, the same master table
    ///               C_BPartner.R_Source_ID and MLead.R_Source_ID already use),
    ///               over a 3- or 6-month trailing window. "Business" = Booked
    ///               sales orders, the identical eligibility rule
    ///               VAS_273_OrderValueBookedMTDWidget uses: active, IsSOTrx=
    ///               'Y', non-return, not a quotation, DocStatus IN ('CO','CL'),
    ///               value = TotalLines (pre-tax) converted to the tenant
    ///               accounting currency. Sources are read from the tenant's own
    ///               R_Source master data (AD_Client_ID IN (0, tenant)) rather
    ///               than a fixed name list, so this reconciles with whatever
    ///               sources the tenant has actually configured. MRole (tenant +
    ///               org + record access) is applied independently to each of
    ///               the two physical tables this reads - C_BPartner (customer
    ///               count) and C_Order (business value) - never to R_Source or
    ///               a CTE alias.
    /// Chronological development:
    ///   VAI052      2026-09-23 Created
    /// </summary>
    public class VAS_300_BusinessBySourceWidgetController : Controller
    {
        private static readonly VLogger Log = VLogger.GetVLogger(typeof(VAS_300_BusinessBySourceWidgetController).FullName);

        private const int WidgetPageSize = 7;
        private const int MaxListPageSize = 25;

        // Bar/swatch colours, cycled by source rank so the same source keeps the
        // same colour between requests. Matches the palette the Customers by
        // Segment distribution already uses.
        private static readonly string[] SourceColors = { "#0083DA", "#5F4AA6", "#0B6B45", "#D78B10", "#A4441C", "#3FA9F5", "#41617A" };

        private static string SchemaCurrencySql(string clientIdSql)
        {
            return @"
            SELECT ci.AD_Client_ID AS AD_Client_ID,
                   cs.C_Currency_ID AS Acct_Currency_ID,
                   cur.StdPrecision AS Std_Precision,
                   cur.ISO_Code AS ISO_Code,
                   CASE WHEN cur.CurSymbol IS NOT NULL THEN cur.CurSymbol ELSE cur.ISO_Code END AS Cur_Symbol
            FROM AD_ClientInfo ci
            INNER JOIN C_AcctSchema cs ON (cs.C_AcctSchema_ID=ci.C_AcctSchema1_ID AND cs.IsActive = 'Y')
            INNER JOIN C_Currency cur ON (cur.C_Currency_ID=cs.C_Currency_ID AND cur.IsActive = 'Y')
            WHERE ci.IsActive = 'Y'
              AND ci.AD_Client_ID = " + clientIdSql;
        }

        /// <summary>Inlines an int, never bound - see the ORA-01008 note on VAS_139/140/299.</summary>
        private static string IntSql(int value)
        {
            return value.ToString(CultureInfo.InvariantCulture);
        }

        /// <summary>A whole-day date literal in the connected dialect. Matches VAS_140.ToSqlDate.</summary>
        private static string ToSqlDate(DateTime date)
        {
            DateTime day = date.Date;
            if (DB.IsOracle())
            {
                return "TO_DATE('" + day.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture) + "','YYYY-MM-DD')";
            }
            return DB.TO_DATE(day, true);
        }

        private class Literals
        {
            public string ClientId;
            public string ConversionTypeId;
            public string PeriodStart;
        }

        /// <summary>period is validated by the caller against "biz3"/"biz6" before this runs.</summary>
        private static Literals BuildLiterals(Ctx ctx, string period)
        {
            int months = period == "biz6" ? 6 : 3;
            return new Literals
            {
                ClientId = IntSql(ctx.GetAD_Client_ID()),
                ConversionTypeId = IntSql(MConversionType.GetDefault(ctx.GetAD_Client_ID())),
                PeriodStart = ToSqlDate(DateTime.Today.AddMonths(-months))
            };
        }

        /// <summary>Booked sales order value converted to the tenant accounting currency. Matches VAS_273's eligibility rule.</summary>
        private string BookedAmtExpr(Literals lit)
        {
            return @"CASE WHEN o.C_Currency_ID = sc.Acct_Currency_ID THEN COALESCE(o.TotalLines, 0)
                          ELSE CurrencyConvert(COALESCE(o.TotalLines, 0), o.C_Currency_ID, sc.Acct_Currency_ID, o.DateOrdered, "
                          + lit.ConversionTypeId + @", o.AD_Client_ID, o.AD_Org_ID) END";
        }

        private string BookedOrdersSql(Literals lit)
        {
            return @"
                SELECT o.C_BPartner_ID AS Bp_Id,
                       o.C_Order_ID AS Order_Id,
                       " + BookedAmtExpr(lit) + @" AS Amt
                FROM C_Order o
                INNER JOIN schema_currency sc ON (sc.AD_Client_ID=o.AD_Client_ID)
                WHERE o.IsActive = 'Y'
                  AND o.AD_Client_ID = " + lit.ClientId + @"
                  AND o.IsSOTrx = 'Y'
                  AND COALESCE(o.IsReturnTrx, 'N') = 'N'
                  AND COALESCE(o.IsSalesQuotation, 'N') = 'N'
                  AND o.DocStatus IN ('CO', 'CL')
                  AND o.DateOrdered >= " + lit.PeriodStart;
        }

        private string CustomerBaseSql(Literals lit)
        {
            return @"
                SELECT bp.C_BPartner_ID AS Bp_Id,
                       bp.Name AS Bp_Name,
                       bp.R_Source_ID AS R_Source_ID
                FROM C_BPartner bp
                WHERE bp.IsActive = 'Y'
                  AND bp.IsCustomer = 'Y'
                  AND bp.AD_Client_ID = " + lit.ClientId + @"
                  AND bp.R_Source_ID IS NOT NULL";
        }

        /// <summary>Every active source the tenant has configured (system + tenant scope). Not MRole'd - a master list, not a record set.</summary>
        private const string SourceListSql = @"
            SELECT rs.R_Source_ID AS R_Source_ID,
                   rs.Name AS Source_Name
            FROM R_Source rs
            WHERE rs.IsActive = 'Y'
              AND rs.AD_Client_ID IN (0, @Client_ID)
            ORDER BY rs.Name ASC, rs.R_Source_ID ASC";

        /// <summary>
        /// Per-source distribution for the selected period: customer count and
        /// booked business value, ranked by value desc.
        /// </summary>
        /// <param name="period">"biz3" (default) or "biz6".</param>
        /// <returns>JSON { period, total, items:[{sourceId,name,color,count,value}], currency_* } or { error }.</returns>
        [AjaxAuthorizeAttribute]
        [AjaxSessionFilterAttribute]
        public JsonResult GetDistribution(string period = "biz3")
        {
            if (Session["ctx"] == null)
            {
                return Json(new { error = Msg.GetMsg(Env.GetCtx(), "SessionExpired") ?? "Session Expired" }, JsonRequestBehavior.AllowGet);
            }

            string key = (period == "biz6") ? "biz6" : "biz3";
            Ctx ctx = Session["ctx"] as Ctx;

            try
            {
                Literals lit = BuildLiterals(ctx, key);

                string currencySymbol = "", isoCode = "";
                int stdPrecision = 2;
                IDataReader cdr = null;
                try
                {
                    cdr = DB.ExecuteReader(SchemaCurrencySql("@Client_ID"), new SqlParameter[] { new SqlParameter("@Client_ID", ctx.GetAD_Client_ID()) });
                    if (cdr != null && cdr.Read())
                    {
                        currencySymbol = Util.GetValueOfString(cdr["Cur_Symbol"]);
                        isoCode = Util.GetValueOfString(cdr["ISO_Code"]);
                        if (cdr["Std_Precision"] != null && cdr["Std_Precision"] != DBNull.Value)
                        {
                            stdPrecision = Util.GetValueOfInt(cdr["Std_Precision"]);
                        }
                    }
                }
                finally { CloseReader(cdr); }

                string customerBase = MRole.GetDefault(ctx).AddAccessSQL(CustomerBaseSql(lit), "bp", MRole.SQL_FULLYQUALIFIED, MRole.SQL_RO);
                string bookedOrders = MRole.GetDefault(ctx).AddAccessSQL(BookedOrdersSql(lit), "o", MRole.SQL_FULLYQUALIFIED, MRole.SQL_RO);

                string sql = @"
                    WITH schema_currency AS (
                        " + SchemaCurrencySql(lit.ClientId) + @"
                    ),
                    source_list AS (
                        " + SourceListSql + @"
                    ),
                    customer_base AS (
                        " + customerBase + @"
                    ),
                    booked_orders AS (
                        " + bookedOrders + @"
                    )
                    SELECT sl.R_Source_ID AS Source_Id,
                           sl.Source_Name AS Source_Name,
                           COUNT(DISTINCT cb.Bp_Id) AS Cust_Count,
                           COALESCE(SUM(bo.Amt), 0) AS Biz_Value
                    FROM source_list sl
                    LEFT OUTER JOIN customer_base cb ON (cb.R_Source_ID=sl.R_Source_ID)
                    LEFT OUTER JOIN booked_orders bo ON (bo.Bp_Id=cb.Bp_Id)
                    GROUP BY sl.R_Source_ID, sl.Source_Name
                    ORDER BY COALESCE(SUM(bo.Amt), 0) DESC, sl.Source_Name ASC";

                List<object> items = new List<object>();
                decimal total = 0;
                int rank = 0;

                IDataReader dr = null;
                try
                {
                    dr = DB.ExecuteReader(sql, new SqlParameter[] { new SqlParameter("@Client_ID", ctx.GetAD_Client_ID()) });
                    while (dr != null && dr.Read())
                    {
                        decimal value = Math.Round(Util.GetValueOfDecimal(dr["Biz_Value"]), stdPrecision);
                        total += value;
                        items.Add(new
                        {
                            sourceId = Util.GetValueOfInt(dr["Source_Id"]),
                            name = Util.GetValueOfString(dr["Source_Name"]),
                            color = SourceColors[rank % SourceColors.Length],
                            count = Util.GetValueOfInt(dr["Cust_Count"]),
                            value = value
                        });
                        rank++;
                    }
                }
                finally { CloseReader(dr); }

                var result = new
                {
                    period = key,
                    total = total,
                    items = items,
                    currency_symbol = currencySymbol,
                    currency_iso = isoCode,
                    std_precision = stdPrecision
                };
                return Json(JsonConvert.SerializeObject(result), JsonRequestBehavior.AllowGet);
            }
            catch (Exception ex)
            {
                Log.Log(Level.SEVERE, "VAS_300_BusinessBySourceWidget.GetDistribution", ex);
                return Json(new { error = ex.Message }, JsonRequestBehavior.AllowGet);
            }
        }

        /// <summary>
        /// Every customer of one source, ranked by the selected period's booked
        /// business desc (a customer with no business in the window still
        /// appears, at zero, so the list matches the tile's customer count).
        /// </summary>
        /// <param name="sourceId">R_Source_ID selected from the distribution.</param>
        /// <param name="period">"biz3" (default) or "biz6".</param>
        /// <param name="offset">Zero-based paging offset.</param>
        /// <param name="limit">Page size (7 for the widget, up to 25 for the full list).</param>
        /// <returns>JSON { items:[...], total, offset, limit, currency_* } or { error }.</returns>
        [AjaxAuthorizeAttribute]
        [AjaxSessionFilterAttribute]
        public JsonResult GetSourceList(int sourceId, string period = "biz3", int offset = 0, int limit = WidgetPageSize)
        {
            if (Session["ctx"] == null)
            {
                return Json(new { error = Msg.GetMsg(Env.GetCtx(), "SessionExpired") ?? "Session Expired" }, JsonRequestBehavior.AllowGet);
            }

            if (sourceId <= 0)
            {
                return Json(new { error = "Invalid source" }, JsonRequestBehavior.AllowGet);
            }

            string key = (period == "biz6") ? "biz6" : "biz3";
            if (offset < 0) { offset = 0; }
            if (limit <= 0 || limit > MaxListPageSize) { limit = WidgetPageSize; }

            Ctx ctx = Session["ctx"] as Ctx;

            try
            {
                Literals lit = BuildLiterals(ctx, key);

                string customerBody = @"
                    SELECT bp.C_BPartner_ID AS Bp_Id,
                           bp.Name AS Bp_Name,
                           COALESCE(owner.Name, N'') AS Owner_Name
                    FROM C_BPartner bp
                    LEFT OUTER JOIN AD_User owner ON (owner.AD_User_ID=bp.SalesRep_ID AND owner.AD_Client_ID=bp.AD_Client_ID AND owner.IsActive = 'Y')
                    WHERE bp.IsActive = 'Y'
                      AND bp.IsCustomer = 'Y'
                      AND bp.AD_Client_ID = " + lit.ClientId + @"
                      AND bp.R_Source_ID = " + IntSql(sourceId);
                customerBody = MRole.GetDefault(ctx).AddAccessSQL(customerBody, "bp", MRole.SQL_FULLYQUALIFIED, MRole.SQL_RO);

                string bookedOrders = MRole.GetDefault(ctx).AddAccessSQL(BookedOrdersSql(lit), "o", MRole.SQL_FULLYQUALIFIED, MRole.SQL_RO);

                string sql = @"
                    WITH schema_currency AS (
                        " + SchemaCurrencySql(lit.ClientId) + @"
                    ),
                    customer_body AS (
                        " + customerBody + @"
                    ),
                    booked_orders AS (
                        " + bookedOrders + @"
                    ),
                    by_customer AS (
                        SELECT cb.Bp_Id AS Bp_Id,
                               cb.Bp_Name AS Bp_Name,
                               cb.Owner_Name AS Owner_Name,
                               COALESCE(SUM(bo.Amt), 0) AS Amount
                        FROM customer_body cb
                        LEFT OUTER JOIN booked_orders bo ON (bo.Bp_Id=cb.Bp_Id)
                        GROUP BY cb.Bp_Id, cb.Bp_Name, cb.Owner_Name
                    )
                    SELECT c.Bp_Id, c.Bp_Name, c.Owner_Name, ROUND(c.Amount, sc.Std_Precision) AS Amount,
                           sc.Cur_Symbol, sc.ISO_Code, sc.Std_Precision,
                           COUNT(1) OVER () AS Total_Rows
                    FROM by_customer c
                    CROSS JOIN schema_currency sc
                    ORDER BY c.Amount DESC, c.Bp_Name ASC, c.Bp_Id ASC
                    OFFSET " + offset + @" ROWS FETCH NEXT " + limit + @" ROWS ONLY";

                List<object> items = new List<object>();
                int total = 0;
                string currencySymbol = "", isoCode = "";
                int stdPrecision = 2;

                IDataReader dr = null;
                try
                {
                    dr = DB.ExecuteReader(sql);
                    while (dr != null && dr.Read())
                    {
                        total = Util.GetValueOfInt(dr["Total_Rows"]);
                        currencySymbol = Util.GetValueOfString(dr["Cur_Symbol"]);
                        isoCode = Util.GetValueOfString(dr["ISO_Code"]);
                        if (dr["Std_Precision"] != null && dr["Std_Precision"] != DBNull.Value)
                        {
                            stdPrecision = Util.GetValueOfInt(dr["Std_Precision"]);
                        }
                        items.Add(new
                        {
                            customerId = Util.GetValueOfInt(dr["Bp_Id"]),
                            customerName = Util.GetValueOfString(dr["Bp_Name"]),
                            ownerName = Util.GetValueOfString(dr["Owner_Name"]),
                            amount = Util.GetValueOfDecimal(dr["Amount"])
                        });
                    }
                }
                finally { CloseReader(dr); }

                var result = new
                {
                    items = items,
                    total = total,
                    offset = offset,
                    limit = limit,
                    currency_symbol = currencySymbol,
                    currency_iso = isoCode,
                    std_precision = stdPrecision
                };
                return Json(JsonConvert.SerializeObject(result), JsonRequestBehavior.AllowGet);
            }
            catch (Exception ex)
            {
                Log.Log(Level.SEVERE, "VAS_300_BusinessBySourceWidget.GetSourceList", ex);
                return Json(new { error = ex.Message }, JsonRequestBehavior.AllowGet);
            }
        }

        private void CloseReader(IDataReader reader)
        {
            if (reader == null) { return; }
            reader.Close();
            reader.Dispose();
        }
    }
}
