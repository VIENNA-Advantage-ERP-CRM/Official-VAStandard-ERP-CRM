using Newtonsoft.Json;
using System;
using System.Collections.Generic;
using System.Data;
using System.Data.SqlClient;
using System.Globalization;
using System.Linq;
using System.Web.Mvc;
using VAdvantage.DataBase;
using VAdvantage.Model;
using VAdvantage.Utility;
using VIS.Filters;

namespace VAS.Controllers
{
    /// <summary>
    /// Module Name : VAS_131_TopCustomersWidget (Product Master dashboard)
    /// Purpose     : Data endpoints for the 2x3 "Top Customers" ranked-bar
    ///               widget - ranks customers by COUNT(DISTINCT product)
    ///               delivered through completed/closed customer shipments
    ///               (M_InOut MovementType 'C-', IsSOTrx='Y', non-return,
    ///               DocStatus CO/CL) in a selected fiscal year, and drills
    ///               into the exact products and quantities (base UOM)
    ///               delivered to one customer. Sales orders/invoices are
    ///               intentionally NOT used - only completed deliveries, per
    ///               the confirmed business correction. The browser only ever
    ///               sends a YearId; the server re-resolves Start/EndDate from
    ///               C_Period and derives EndDateExclusive itself, so no
    ///               client-supplied date is trusted. MRole is applied to the
    ///               primary fetched table on each query (M_InOut for
    ///               ranking/detail, C_Year for the fiscal-year list); all
    ///               input is parameterized; the SQL uses only COALESCE / CASE
    ///               (no NVL, DECODE, LISTAGG, FETCH/LIMIT/OFFSET, DB date
    ///               formatting or DB-specific upsert), so it runs unchanged on
    ///               Oracle and PostgreSQL. Attribute rows are grouped into
    ///               chips per product in C#, never aggregated in SQL. The Top
    ///               Customers and Top Vendors widgets each have their own
    ///               separate frontend module (no shared file); this
    ///               controller and VAS_130_TopVendorsWidgetController stay
    ///               separate because their underlying business definitions
    ///               (delivery vs. receipt movement) differ.
    /// Widget size : 2 columns x 3 rows.
    /// Widget number 131.
    /// Chronological development:
    ///   &lt;EmpCode&gt;   2026-07-20 Created
    /// </summary>
    public class VAS_131_TopCustomersWidgetController : Controller
    {
        /// <summary>
        /// Returns the configured fiscal years (most recent first) and the id
        /// of the year containing today's business date.
        /// </summary>
        /// <returns>JSON { years[], currentYearId }.</returns>
        [AjaxAuthorizeAttribute]
        [AjaxSessionFilterAttribute]
        public JsonResult GetFiscalYears()
        {
            if (Session["ctx"] == null)
            {
                return Fail(Msg.GetMsg(Env.GetCtx(), "SessionExpired") ?? "Session Expired");
            }

            Ctx ctx = Session["ctx"] as Ctx;

            try
            {
                List<YearRow> years = LoadFiscalYears(ctx);
                DateTime today = DateTime.Now.Date;
                YearRow current = years.FirstOrDefault(y => today >= y.StartDate && today <= y.EndDate) ?? years.FirstOrDefault();

                return Ok(new
                {
                    years = years.Select(y => new { yearId = y.YearId, label = y.Label }).ToList(),
                    currentYearId = current != null ? current.YearId : 0
                });
            }
            catch (Exception ex)
            {
                return Fail(ex.Message);
            }
        }

        /// <summary>
        /// Returns customers ranked by distinct products delivered in one
        /// fiscal year, most products first.
        /// </summary>
        /// <param name="yearId">C_Year_ID selected by the user.</param>
        /// <returns>JSON { yearId, label, partners[] }.</returns>
        [AjaxAuthorizeAttribute]
        [AjaxSessionFilterAttribute]
        public JsonResult GetRankedPartners(int yearId)
        {
            if (Session["ctx"] == null)
            {
                return Fail(Msg.GetMsg(Env.GetCtx(), "SessionExpired") ?? "Session Expired");
            }

            Ctx ctx = Session["ctx"] as Ctx;

            YearRow year = LoadYearBounds(ctx, yearId);
            if (year == null)
            {
                return Fail("Unknown fiscal year.");
            }

            try
            {
                DateTime endExclusive = year.EndDate.Date.AddDays(1);

                string sql = @"
                    SELECT bp.C_BPartner_ID AS PartnerId,
                           bp.Name AS PartnerName,
                           COUNT(DISTINCT il.M_Product_ID) AS ProductCount,
                           MAX(io.MovementDate) AS LastDate
                    FROM M_InOut io
                    JOIN M_InOutLine il ON (il.M_InOut_ID = io.M_InOut_ID AND il.AD_Client_ID = io.AD_Client_ID)
                    JOIN C_BPartner bp ON (bp.C_BPartner_ID = io.C_BPartner_ID AND bp.AD_Client_ID = io.AD_Client_ID)
                    WHERE io.AD_Client_ID = @AD_Client_ID
                      AND io.IsActive = 'Y'
                      AND il.IsActive = 'Y'
                      AND io.MovementType = 'C-'
                      AND io.IsSOTrx = 'Y'
                      AND COALESCE(io.IsReturnTrx, 'N') = 'N'
                      AND io.DocStatus IN ('CO', 'CL')
                      AND bp.IsCustomer = 'Y'
                      AND il.M_Product_ID IS NOT NULL
                      AND COALESCE(il.MovementQty, 0) <> 0
                      AND io.MovementDate >= @StartDate
                      AND io.MovementDate < @EndDateExclusive";

                // AddAccessSQL appends its predicate to the END, so GROUP BY /
                // ORDER BY are appended after the call (widget rule #1).
                sql = MRole.GetDefault(ctx).AddAccessSQL(sql, "io", MRole.SQL_FULLYQUALIFIED, MRole.SQL_RO);
                sql += @"
                    GROUP BY bp.C_BPartner_ID, bp.Name
                    ORDER BY ProductCount DESC, LastDate DESC, bp.Name";

                List<object> partners = new List<object>();
                IDataReader dr = null;
                try
                {
                    dr = DB.ExecuteReader(sql, new SqlParameter[]
                    {
                        new SqlParameter("@AD_Client_ID", ctx.GetAD_Client_ID()),
                        new SqlParameter("@StartDate", SqlDbType.DateTime) { Value = year.StartDate },
                        new SqlParameter("@EndDateExclusive", SqlDbType.DateTime) { Value = endExclusive }
                    });

                    while (dr != null && dr.Read())
                    {
                        DateTime? lastDate = Util.GetValueOfDateTime(dr["LastDate"]);
                        partners.Add(new
                        {
                            partnerId = Util.GetValueOfInt(dr["PartnerId"]),
                            name = Util.GetValueOfString(dr["PartnerName"]),
                            productCount = Util.GetValueOfInt(dr["ProductCount"]),
                            lastDate = lastDate.HasValue ? lastDate.Value.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture) : ""
                        });
                    }
                }
                finally
                {
                    if (dr != null) { dr.Close(); dr.Dispose(); }
                }

                return Ok(new { yearId = year.YearId, label = year.Label, partners = partners });
            }
            catch (Exception ex)
            {
                return Fail(ex.Message);
            }
        }

        /// <summary>
        /// Returns the products delivered to one customer in one fiscal year,
        /// with delivered quantity (base UOM) and deduplicated attribute chips.
        /// </summary>
        /// <param name="partnerId">C_BPartner_ID of the customer.</param>
        /// <param name="yearId">C_Year_ID selected by the user.</param>
        /// <returns>JSON { partnerId, name, yearId, label, lastDate, items[] }.</returns>
        [AjaxAuthorizeAttribute]
        [AjaxSessionFilterAttribute]
        public JsonResult GetPartnerProducts(int partnerId, int yearId)
        {
            if (Session["ctx"] == null)
            {
                return Fail(Msg.GetMsg(Env.GetCtx(), "SessionExpired") ?? "Session Expired");
            }

            Ctx ctx = Session["ctx"] as Ctx;

            if (partnerId <= 0)
            {
                return Fail("Unknown customer.");
            }

            YearRow year = LoadYearBounds(ctx, yearId);
            if (year == null)
            {
                return Fail("Unknown fiscal year.");
            }

            try
            {
                DateTime endExclusive = year.EndDate.Date.AddDays(1);

                string innerSql = @"
                    SELECT p.M_Product_ID AS ProductId,
                           p.Name AS ProductName,
                           p.Value AS ProductCode,
                           p.C_UOM_ID AS UomId,
                           p.M_AttributeSetInstance_ID AS AttributeSetInstanceId,
                           SUM(il.MovementQty) AS Qty,
                           MAX(io.MovementDate) AS LastDate
                    FROM M_InOut io
                    JOIN M_InOutLine il ON (il.M_InOut_ID = io.M_InOut_ID AND il.AD_Client_ID = io.AD_Client_ID)
                    JOIN M_Product p ON (p.M_Product_ID = il.M_Product_ID AND p.AD_Client_ID = io.AD_Client_ID)
                    WHERE io.AD_Client_ID = @AD_Client_ID
                      AND io.C_BPartner_ID = @PartnerId
                      AND io.IsActive = 'Y'
                      AND il.IsActive = 'Y'
                      AND io.MovementType = 'C-'
                      AND io.IsSOTrx = 'Y'
                      AND COALESCE(io.IsReturnTrx, 'N') = 'N'
                      AND io.DocStatus IN ('CO', 'CL')
                      AND COALESCE(il.MovementQty, 0) <> 0
                      AND io.MovementDate >= @StartDate
                      AND io.MovementDate < @EndDateExclusive";

                innerSql = MRole.GetDefault(ctx).AddAccessSQL(innerSql, "io", MRole.SQL_FULLYQUALIFIED, MRole.SQL_RO);
                innerSql += @"
                    GROUP BY p.M_Product_ID, p.Name, p.Value, p.C_UOM_ID, p.M_AttributeSetInstance_ID";

                string sql = @"
                    WITH ProdQty AS (
                        " + innerSql + @"
                    )
                    SELECT q.ProductId, q.ProductName, q.ProductCode, q.Qty, q.LastDate,
                           u.Name AS UomName, u.UOMSymbol AS UomSymbol,
                           ai.M_AttributeInstance_ID AS AttributeInstanceId,
                           a.M_Attribute_ID AS AttributeId,
                           a.Name AS AttributeName,
                           av.Name AS AttributeListValue,
                           ai.Value AS AttributeTextValue,
                           ai.ValueNumber AS AttributeNumberValue
                    FROM ProdQty q
                    LEFT JOIN C_UOM u ON (u.C_UOM_ID = q.UomId)
                    LEFT JOIN M_AttributeInstance ai ON (ai.M_AttributeSetInstance_ID = q.AttributeSetInstanceId AND ai.IsActive = 'Y')
                    LEFT JOIN M_Attribute a ON (a.M_Attribute_ID = ai.M_Attribute_ID AND a.IsActive = 'Y')
                    LEFT JOIN M_AttributeValue av ON (av.M_AttributeValue_ID = ai.M_AttributeValue_ID AND av.M_Attribute_ID = ai.M_Attribute_ID AND av.IsActive = 'Y')
                    ORDER BY q.ProductName, q.ProductCode, a.Name, av.Name";

                List<int> order = new List<int>();
                Dictionary<int, DetailRow> byId = new Dictionary<int, DetailRow>();
                DateTime? overallLastDate = null;

                IDataReader dr = null;
                try
                {
                    dr = DB.ExecuteReader(sql, new SqlParameter[]
                    {
                        new SqlParameter("@AD_Client_ID", ctx.GetAD_Client_ID()),
                        new SqlParameter("@PartnerId", partnerId),
                        new SqlParameter("@StartDate", SqlDbType.DateTime) { Value = year.StartDate },
                        new SqlParameter("@EndDateExclusive", SqlDbType.DateTime) { Value = endExclusive }
                    });

                    while (dr != null && dr.Read())
                    {
                        int productId = Util.GetValueOfInt(dr["ProductId"]);

                        DetailRow row;
                        if (!byId.TryGetValue(productId, out row))
                        {
                            string uomSymbol = Util.GetValueOfString(dr["UomSymbol"]);
                            string uomName = Util.GetValueOfString(dr["UomName"]);
                            row = new DetailRow
                            {
                                productId = productId,
                                name = Util.GetValueOfString(dr["ProductName"]),
                                code = Util.GetValueOfString(dr["ProductCode"]),
                                qty = Util.GetValueOfDecimal(dr["Qty"]),
                                uom = !string.IsNullOrEmpty(uomSymbol) ? uomSymbol : uomName,
                                attributes = new List<Attr>(),
                                seenChips = new HashSet<string>()
                            };
                            byId[productId] = row;
                            order.Add(productId);

                            DateTime? rowLastDate = Util.GetValueOfDateTime(dr["LastDate"]);
                            if (rowLastDate.HasValue && (!overallLastDate.HasValue || rowLastDate.Value > overallLastDate.Value))
                            {
                                overallLastDate = rowLastDate.Value;
                            }
                        }

                        string label = Util.GetValueOfString(dr["AttributeName"]);
                        if (string.IsNullOrEmpty(label)) { continue; }

                        string value = Util.GetValueOfString(dr["AttributeListValue"]);
                        if (string.IsNullOrEmpty(value)) { value = Util.GetValueOfString(dr["AttributeTextValue"]); }
                        if (string.IsNullOrEmpty(value) && dr["AttributeNumberValue"] != DBNull.Value)
                        {
                            value = Util.GetValueOfDecimal(dr["AttributeNumberValue"]).ToString("0.####", CultureInfo.InvariantCulture);
                        }
                        if (string.IsNullOrEmpty(value)) { continue; }

                        string chipKey = label + ":" + value;
                        if (row.seenChips.Contains(chipKey)) { continue; }
                        row.seenChips.Add(chipKey);
                        row.attributes.Add(new Attr { label = label, value = value });
                    }
                }
                finally
                {
                    if (dr != null) { dr.Close(); dr.Dispose(); }
                }

                List<object> items = order.Select(id =>
                {
                    DetailRow row = byId[id];
                    return (object)new
                    {
                        productId = row.productId,
                        name = row.name,
                        code = row.code,
                        qty = row.qty,
                        uom = row.uom,
                        attributes = row.attributes
                    };
                }).ToList();

                string partnerName = LoadPartnerName(ctx, partnerId);

                return Ok(new
                {
                    partnerId = partnerId,
                    name = partnerName,
                    yearId = year.YearId,
                    label = year.Label,
                    lastDate = overallLastDate.HasValue ? overallLastDate.Value.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture) : "",
                    items = items
                });
            }
            catch (Exception ex)
            {
                return Fail(ex.Message);
            }
        }

        /// <summary>Configured fiscal years with resolved date bounds, most recent first.</summary>
        private List<YearRow> LoadFiscalYears(Ctx ctx)
        {
            string sql = @"
                SELECT y.C_Year_ID AS YearId,
                       y.FiscalYear AS FiscalYear,
                       y.Description AS YearDescription,
                       MIN(p.StartDate) AS StartDate,
                       MAX(p.EndDate) AS EndDate
                FROM AD_ClientInfo ci
                JOIN C_Year y ON (y.C_Calendar_ID = ci.C_Calendar_ID AND y.AD_Client_ID = ci.AD_Client_ID)
                JOIN C_Period p ON (p.C_Year_ID = y.C_Year_ID AND p.AD_Client_ID = y.AD_Client_ID)
                WHERE ci.AD_Client_ID = @AD_Client_ID
                  AND y.IsActive = 'Y'
                  AND p.IsActive = 'Y'
                  AND p.PeriodType = 'S'";

            sql = MRole.GetDefault(ctx).AddAccessSQL(sql, "y", MRole.SQL_FULLYQUALIFIED, MRole.SQL_RO);
            sql += @"
                GROUP BY y.C_Year_ID, y.FiscalYear, y.Description
                ORDER BY MIN(p.StartDate) DESC";

            List<YearRow> years = new List<YearRow>();
            IDataReader dr = null;
            try
            {
                dr = DB.ExecuteReader(sql, new SqlParameter[]
                {
                    new SqlParameter("@AD_Client_ID", ctx.GetAD_Client_ID())
                });

                while (dr != null && dr.Read())
                {
                    DateTime? start = Util.GetValueOfDateTime(dr["StartDate"]);
                    DateTime? end = Util.GetValueOfDateTime(dr["EndDate"]);
                    if (!start.HasValue || !end.HasValue) { continue; }

                    string description = Util.GetValueOfString(dr["YearDescription"]);
                    string fiscalYear = Util.GetValueOfString(dr["FiscalYear"]);
                    years.Add(new YearRow
                    {
                        YearId = Util.GetValueOfInt(dr["YearId"]),
                        Label = !string.IsNullOrEmpty(description) ? description : "FY " + fiscalYear,
                        StartDate = start.Value,
                        EndDate = end.Value
                    });
                }
            }
            finally
            {
                if (dr != null) { dr.Close(); dr.Dispose(); }
            }

            return years;
        }

        /// <summary>Resolved date bounds for one fiscal year, or null if not found/accessible.</summary>
        private YearRow LoadYearBounds(Ctx ctx, int yearId)
        {
            if (yearId <= 0) { return null; }
            return LoadFiscalYears(ctx).FirstOrDefault(y => y.YearId == yearId);
        }

        /// <summary>Display name of a business partner, tenant- and role-scoped.</summary>
        private string LoadPartnerName(Ctx ctx, int partnerId)
        {
            string sql = @"
                SELECT bp.Name AS PartnerName
                FROM C_BPartner bp
                WHERE bp.AD_Client_ID = @AD_Client_ID
                  AND bp.C_BPartner_ID = @PartnerId";

            sql = MRole.GetDefault(ctx).AddAccessSQL(sql, "bp", MRole.SQL_FULLYQUALIFIED, MRole.SQL_RO);

            return Util.GetValueOfString(DB.ExecuteScalar(sql, new SqlParameter[]
            {
                new SqlParameter("@AD_Client_ID", ctx.GetAD_Client_ID()),
                new SqlParameter("@PartnerId", partnerId)
            }, null));
        }

        /// <summary>One resolved fiscal year with date bounds.</summary>
        private class YearRow
        {
            public int YearId;
            public string Label;
            public DateTime StartDate;
            public DateTime EndDate;
        }

        /// <summary>Intermediate accumulator for one product while grouping attribute rows.</summary>
        private class DetailRow
        {
            public int productId;
            public string name;
            public string code;
            public decimal qty;
            public string uom;
            public List<Attr> attributes;
            public HashSet<string> seenChips;
        }

        /// <summary>One resolved attribute chip.</summary>
        private class Attr
        {
            public string label { get; set; }
            public string value { get; set; }
        }

        /// <summary>Wraps a success payload as a serialized JSON result.</summary>
        private JsonResult Ok(object result)
        {
            return Json(JsonConvert.SerializeObject(result), JsonRequestBehavior.AllowGet);
        }

        /// <summary>Wraps a failure message as a serialized JSON result.</summary>
        private JsonResult Fail(string message)
        {
            return Json(JsonConvert.SerializeObject(new
            {
                success = false,
                error = message,
                message = message
            }), JsonRequestBehavior.AllowGet);
        }
    }
}
