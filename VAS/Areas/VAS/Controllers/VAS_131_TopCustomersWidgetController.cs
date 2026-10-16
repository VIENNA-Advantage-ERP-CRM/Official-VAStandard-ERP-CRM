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
    ///   Claude       2026-10-15 Widget_Bug_Fix Sr 43: customers are now ranked by the Grand Total
    ///                           (converted to the accounting-schema currency) of their completed /
    ///                           closed, fully delivered sales orders - last delivery falling in the
    ///                           selected fiscal year - instead of by distinct products shipped. This
    ///                           supersedes the "completed deliveries only, sales orders NOT used"
    ///                           rule above for the ranking; the drill-in product list is still
    ///                           built from completed shipments.
    ///   Claude       2026-10-15 Widget_Bug_Fix Sr 41: drill-in shows the full UOM name (Each) not the symbol (Ea).
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
        /// Returns customers ranked by the Grand Total of their fully delivered
        /// sales orders in one fiscal year, highest total first.
        /// </summary>
        /// <param name="yearId">C_Year_ID selected by the user.</param>
        /// <returns>JSON { yearId, label, currencySymbol, currencyIso, stdPrecision, partners[] }.</returns>
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
                BaseCurrency currency = LoadBaseCurrency(ctx);

                // Widget_Bug_Fix Sr 43: customers are ranked by the Grand Total of their
                // fully delivered, completed sales orders (not by distinct products shipped).
                // The role-filtered order set is its own block (AddAccessSQL appends its
                // predicate to the END of the text it is given), and the delivery-date /
                // delivered-quantity aggregates live in separate unfiltered CTEs, so no
                // GROUP BY ever sits inside the statement handed to AddAccessSQL. Each
                // order's GrandTotal is converted to the accounting-schema currency.
                string orderSql = @"
                    SELECT o.C_Order_ID, o.C_BPartner_ID, o.GrandTotal, o.C_Currency_ID,
                           o.C_ConversionType_ID, o.AD_Client_ID, o.AD_Org_ID, o.DateOrdered
                    FROM C_Order o
                    WHERE o.AD_Client_ID = @AD_Client_ID1
                      AND o.IsActive = 'Y'
                      AND o.IsSOTrx = 'Y'
                      AND COALESCE(o.IsReturnTrx, 'N') = 'N'
                      AND COALESCE(o.IsSalesQuotation, 'N') = 'N'
                      AND o.DocStatus IN ('CO', 'CL')";
                orderSql = MRole.GetDefault(ctx).AddAccessSQL(orderSql, "o", MRole.SQL_FULLYQUALIFIED, MRole.SQL_RO);

                string sql = @"
                    WITH Ord AS (
                        " + orderSql + @"
                    ),
                    OrdQty AS (
                        SELECT ol.C_Order_ID,
                               SUM(COALESCE(ol.QtyDelivered, 0)) AS QtyDelivered,
                               SUM(COALESCE(ol.QtyOrdered, 0)) AS QtyOrdered
                        FROM C_OrderLine ol
                        WHERE ol.AD_Client_ID = @AD_Client_ID2
                        GROUP BY ol.C_Order_ID
                    ),
                    Dlv AS (
                        SELECT io.C_Order_ID, MAX(io.MovementDate) AS LastMove
                        FROM M_InOut io
                        WHERE io.AD_Client_ID = @AD_Client_ID3
                          AND io.IsActive = 'Y'
                          AND io.MovementType = 'C-'
                          AND io.IsSOTrx = 'Y'
                          AND COALESCE(io.IsReturnTrx, 'N') = 'N'
                          AND io.DocStatus IN ('CO', 'CL')
                          AND io.C_Order_ID IS NOT NULL
                        GROUP BY io.C_Order_ID
                    )
                    SELECT bp.C_BPartner_ID AS PartnerId,
                           bp.Name AS PartnerName,
                           SUM(COALESCE(CurrencyConvert(Ord.GrandTotal, Ord.C_Currency_ID, @BaseCurrency_ID,
                                   Ord.DateOrdered, Ord.C_ConversionType_ID, Ord.AD_Client_ID, Ord.AD_Org_ID), 0)) AS TotalAmount,
                           MAX(Dlv.LastMove) AS LastDate
                    FROM Ord
                    JOIN OrdQty ON (OrdQty.C_Order_ID = Ord.C_Order_ID)
                    JOIN Dlv ON (Dlv.C_Order_ID = Ord.C_Order_ID)
                    JOIN C_BPartner bp ON (bp.C_BPartner_ID = Ord.C_BPartner_ID AND bp.AD_Client_ID = Ord.AD_Client_ID)
                    WHERE bp.IsCustomer = 'Y'
                      AND OrdQty.QtyDelivered > 0
                      AND OrdQty.QtyDelivered >= OrdQty.QtyOrdered
                      AND Dlv.LastMove >= @StartDate
                      AND Dlv.LastMove < @EndDateExclusive
                    GROUP BY bp.C_BPartner_ID, bp.Name
                    ORDER BY TotalAmount DESC, LastDate DESC, bp.Name";

                List<object> partners = new List<object>();
                IDataReader dr = null;
                try
                {
                    // DB.ExecuteReader binds POSITIONALLY (left-to-right placeholder order in the final
                    // text), so the client id needed in three blocks has three distinct placeholders.
                    dr = DB.ExecuteReader(sql, new SqlParameter[]
                    {
                        new SqlParameter("@AD_Client_ID1", ctx.GetAD_Client_ID()),
                        new SqlParameter("@AD_Client_ID2", ctx.GetAD_Client_ID()),
                        new SqlParameter("@AD_Client_ID3", ctx.GetAD_Client_ID()),
                        new SqlParameter("@BaseCurrency_ID", currency.CurrencyId),
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
                            totalAmount = Util.GetValueOfDecimal(dr["TotalAmount"]),
                            lastDate = lastDate.HasValue ? lastDate.Value.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture) : ""
                        });
                    }
                }
                finally
                {
                    if (dr != null) { dr.Close(); dr.Dispose(); }
                }

                return Ok(new
                {
                    yearId = year.YearId,
                    label = year.Label,
                    currencySymbol = currency.Symbol,
                    currencyIso = currency.IsoCode,
                    stdPrecision = currency.StdPrecision,
                    partners = partners
                });
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
                                // Full UOM name ("Each"), not the symbol ("Ea") - Widget_Bug_Fix Sr 41.
                                uom = !string.IsNullOrEmpty(uomName) ? uomName : uomSymbol,
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

        /// <summary>
        /// Accounting-schema currency (id, symbol, ISO code, standard precision) that the
        /// ranked Grand Totals are converted to. Same source as the other Product Master widgets.
        /// </summary>
        private BaseCurrency LoadBaseCurrency(Ctx ctx)
        {
            BaseCurrency currency = new BaseCurrency();

            string sql = @"
                SELECT asch.C_Currency_ID AS CurrencyId,
                       cur.StdPrecision AS StdPrecision,
                       cur.ISO_Code AS IsoCode,
                       cur.CurSymbol AS CurSymbol
                FROM AD_ClientInfo ci
                JOIN C_AcctSchema asch ON (asch.C_AcctSchema_ID = ci.C_AcctSchema1_ID AND asch.IsActive = 'Y')
                JOIN C_Currency cur ON (cur.C_Currency_ID = asch.C_Currency_ID AND cur.IsActive = 'Y')
                WHERE ci.IsActive = 'Y'
                  AND ci.AD_Client_ID = @AD_Client_ID";

            sql = MRole.GetDefault(ctx).AddAccessSQL(sql, "ci", MRole.SQL_FULLYQUALIFIED, MRole.SQL_RO);

            IDataReader dr = null;
            try
            {
                dr = DB.ExecuteReader(sql, new SqlParameter[]
                {
                    new SqlParameter("@AD_Client_ID", ctx.GetAD_Client_ID())
                });

                if (dr != null && dr.Read())
                {
                    currency.CurrencyId = Util.GetValueOfInt(dr["CurrencyId"]);
                    currency.StdPrecision = Util.GetValueOfInt(dr["StdPrecision"]);
                    currency.IsoCode = Util.GetValueOfString(dr["IsoCode"]);
                    string symbol = Util.GetValueOfString(dr["CurSymbol"]);
                    currency.Symbol = !string.IsNullOrEmpty(symbol) ? symbol : currency.IsoCode;
                }
            }
            finally
            {
                if (dr != null) { dr.Close(); dr.Dispose(); }
            }

            return currency;
        }

        /// <summary>Accounting-schema currency used to total Grand Totals.</summary>
        private class BaseCurrency
        {
            public int CurrencyId;
            public int StdPrecision = 2;
            public string IsoCode = "";
            public string Symbol = "";
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
