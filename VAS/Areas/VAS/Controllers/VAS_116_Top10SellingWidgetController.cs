using Newtonsoft.Json;
using System;
using System.Collections.Generic;
using System.Data;
using System.Data.SqlClient;
using System.Linq;
using System.Web.Mvc;
using VAdvantage.DataBase;
using VAdvantage.Logging;
using VAdvantage.Model;
using VAdvantage.Utility;
using VIS.Filters;

namespace VAS.Controllers
{
    /// <summary>
    /// Module Name : VAS_116_Top10SellingWidget
    /// Purpose     : Supplies the "Top 10 Selling" ranked bar list for the Product
    ///               section - the ten highest AND ten lowest selling products by
    ///               current accounting-year revenue (with units and SKU), so one
    ///               card can flip between a High and Low series. Same completed
    ///               AR-invoice sales / accounting-year / currency logic as VAS_094
    ///               and VAS_116_TopSellers; grouped per product. Placeholder
    ///               number 000 - reassign on hand-off.
    /// Chronological development:
    ///   116         2026-07-16 Created
    /// </summary>
    public class VAS_116_Top10SellingWidgetController : Controller
    {
        private static readonly VLogger Log = VLogger.GetVLogger(typeof(VAS_116_Top10SellingWidgetController).FullName);

        private const int TopRecordsLimit = 10;

        /// <summary>
        /// Returns the ten highest- and ten lowest-selling products for the current
        /// accounting year, plus schema currency metadata.
        /// </summary>
        [AjaxAuthorizeAttribute]
        [AjaxSessionFilterAttribute]
        public JsonResult GetTop10Selling()
        {
            Ctx ctx = Session["ctx"] as Ctx;
            if (ctx == null) { return Json("", JsonRequestBehavior.AllowGet); }

            try
            {
                Top10Result result = GetTop10Data(ctx);
                return Json(JsonConvert.SerializeObject(result), JsonRequestBehavior.AllowGet);
            }
            catch (Exception ex)
            {
                Log.Log(Level.SEVERE, "VAS_116_Top10SellingWidget.GetTop10Selling", ex);
                string message = (Msg.GetMsg(ctx, "Error") ?? "Error") + ": " + ex.Message;
                return Json(JsonConvert.SerializeObject(new { error = message }), JsonRequestBehavior.AllowGet);
            }
        }

        private Top10Result GetTop10Data(Ctx ctx)
        {
            Top10Result result = new Top10Result
            {
                high = new List<Top10Product>(),
                low = new List<Top10Product>()
            };
            if (ctx == null) { return result; }

            SchemaCurrency currency = GetSchemaCurrency(ctx);
            result.currency_symbol = currency.Symbol;
            result.currency_iso = currency.IsoCode;
            result.std_precision = currency.StdPrecision;
            if (currency.CurrencyId == 0) { return result; }

            FinancialYearRange financialYears = GetFinancialYearRange(ctx);
            if (!financialYears.HasCurrentYear) { return result; }

            string invoiceLineSql = @"
                SELECT InvoiceLine.C_InvoiceLine_ID,
                       InvoiceLine.C_Invoice_ID,
                       InvoiceLine.C_OrderLine_ID,
                       InvoiceLine.M_Product_ID,
                       InvoiceLine.LineNetAmt,
                       InvoiceLine.QtyInvoiced,
                       InvoiceLine.QtyEntered
                FROM C_InvoiceLine InvoiceLine
                WHERE InvoiceLine.IsActive='Y'
                  AND InvoiceLine.AD_Client_ID=@InvoiceLine_Client_ID
                  AND InvoiceLine.AD_Org_ID IN (0,COALESCE(NULLIF(@InvoiceLine_Org_ID,0),InvoiceLine.AD_Org_ID))";

            string invoiceSql = @"
                SELECT Invoice.C_Invoice_ID,
                       Invoice.C_Currency_ID,
                       Invoice.C_ConversionType_ID,
                       Invoice.AD_Client_ID,
                       Invoice.AD_Org_ID,
                       Invoice.DateInvoiced
                FROM C_Invoice Invoice
                WHERE Invoice.IsActive='Y'
                  AND Invoice.IsSOTrx='Y'
                  AND Invoice.DocStatus='CO'
                  AND Invoice.AD_Client_ID=@Invoice_Client_ID
                  AND Invoice.AD_Org_ID IN (0,COALESCE(NULLIF(@Invoice_Org_ID,0),Invoice.AD_Org_ID))
                  AND Invoice.DateInvoiced>=@Current_Year_Start0
                  AND Invoice.DateInvoiced<@Current_Year_End0";

            string orderLineSql = @"
                SELECT OrderLine.C_OrderLine_ID,
                       OrderLine.C_Order_ID
                FROM C_OrderLine OrderLine
                WHERE OrderLine.IsActive='Y'
                  AND OrderLine.AD_Client_ID=@OrderLine_Client_ID
                  AND OrderLine.AD_Org_ID IN (0,COALESCE(NULLIF(@OrderLine_Org_ID,0),OrderLine.AD_Org_ID))";

            string orderSql = @"
                SELECT SalesOrder.C_Order_ID
                FROM C_Order SalesOrder
                WHERE SalesOrder.IsActive='Y'
                  AND SalesOrder.IsSOTrx='Y'
                  AND SalesOrder.AD_Client_ID=@Order_Client_ID
                  AND SalesOrder.AD_Org_ID IN (0,COALESCE(NULLIF(@Order_Org_ID,0),SalesOrder.AD_Org_ID))";

            string productSql = @"
                SELECT Product.M_Product_ID,
                       Product.Name,
                       COALESCE(Product.SKU, Product.Value) AS Product_Code
                FROM M_Product Product
                WHERE Product.IsActive='Y'
                  AND (Product.Discontinued IS NULL OR Product.Discontinued='N')
                  AND Product.AD_Client_ID=@Product_Client_ID
                  AND Product.AD_Org_ID IN (0,COALESCE(NULLIF(@Product_Org_ID,0),Product.AD_Org_ID))";

            invoiceLineSql = AddAccessSql(ctx, invoiceLineSql, "InvoiceLine");
            invoiceSql = AddAccessSql(ctx, invoiceSql, "Invoice");
            orderLineSql = AddAccessSql(ctx, orderLineSql, "OrderLine");
            orderSql = AddAccessSql(ctx, orderSql, "SalesOrder");
            productSql = AddAccessSql(ctx, productSql, "Product");

            string sql = string.Format(@"
                WITH InvoiceLines AS (
                    {0}
                ),
                Invoices AS (
                    {1}
                ),
                OrderLines AS (
                    {2}
                ),
                SalesOrders AS (
                    {3}
                ),
                Products AS (
                    {4}
                ),
                Sales AS (
                    SELECT Products.M_Product_ID,
                           Products.Name AS Product_Name,
                           Products.Product_Code,
                           SUM(COALESCE(CURRENCYCONVERT(InvoiceLines.LineNetAmt,Invoices.C_Currency_ID,@Schema_Currency_ID,Invoices.DateInvoiced,Invoices.C_ConversionType_ID,Invoices.AD_Client_ID,Invoices.AD_Org_ID),0)) AS Revenue,
                           SUM(COALESCE(InvoiceLines.QtyInvoiced,InvoiceLines.QtyEntered,0)) AS Units
                    FROM InvoiceLines
                    INNER JOIN Invoices ON (Invoices.C_Invoice_ID=InvoiceLines.C_Invoice_ID)
                    INNER JOIN OrderLines ON (OrderLines.C_OrderLine_ID=InvoiceLines.C_OrderLine_ID)
                    INNER JOIN SalesOrders ON (SalesOrders.C_Order_ID=OrderLines.C_Order_ID)
                    INNER JOIN Products ON (Products.M_Product_ID=InvoiceLines.M_Product_ID)
                    GROUP BY Products.M_Product_ID,
                             Products.Name,
                             Products.Product_Code
                )
                SELECT Sales.M_Product_ID,
                       Sales.Product_Name,
                       Sales.Product_Code,
                       Sales.Revenue,
                       Sales.Units
                FROM Sales
                WHERE Sales.Revenue>0
                ORDER BY Sales.Revenue DESC",
                invoiceLineSql,
                invoiceSql,
                orderLineSql,
                orderSql,
                productSql
            );

            SqlParameter[] parameters = new SqlParameter[]
            {
                new SqlParameter("@InvoiceLine_Client_ID", ctx.GetAD_Client_ID()),
                new SqlParameter("@InvoiceLine_Org_ID", ctx.GetAD_Org_ID()),
                new SqlParameter("@Invoice_Client_ID", ctx.GetAD_Client_ID()),
                new SqlParameter("@Invoice_Org_ID", ctx.GetAD_Org_ID()),
                new SqlParameter("@Current_Year_Start0", SqlDbType.DateTime) { Value = financialYears.CurrentStart },
                new SqlParameter("@Current_Year_End0", SqlDbType.DateTime) { Value = financialYears.CurrentEndExclusive },
                new SqlParameter("@OrderLine_Client_ID", ctx.GetAD_Client_ID()),
                new SqlParameter("@OrderLine_Org_ID", ctx.GetAD_Org_ID()),
                new SqlParameter("@Order_Client_ID", ctx.GetAD_Client_ID()),
                new SqlParameter("@Order_Org_ID", ctx.GetAD_Org_ID()),
                new SqlParameter("@Product_Client_ID", ctx.GetAD_Client_ID()),
                new SqlParameter("@Product_Org_ID", ctx.GetAD_Org_ID()),
                new SqlParameter("@Schema_Currency_ID", currency.CurrencyId)
            };

            List<Top10Product> all = new List<Top10Product>();
            IDataReader reader = null;
            try
            {
                reader = DB.ExecuteReader(sql, parameters);
                while (reader != null && reader.Read())
                {
                    all.Add(new Top10Product
                    {
                        product_id = Util.GetValueOfInt(reader["M_Product_ID"]),
                        product_name = Util.GetValueOfString(reader["Product_Name"]),
                        sku = Util.GetValueOfString(reader["Product_Code"]),
                        revenue = Util.GetValueOfDecimal(reader["Revenue"]),
                        units = Util.GetValueOfDecimal(reader["Units"])
                    });
                }
            }
            finally
            {
                CloseReader(reader);
            }

            result.high = all.Take(TopRecordsLimit).ToList();
            result.low = all.OrderBy(row => row.revenue).Take(TopRecordsLimit).ToList();
            return result;
        }

        /// <summary>Resolves current and previous accounting-year boundaries from Period Control.</summary>
        private FinancialYearRange GetFinancialYearRange(Ctx ctx)
        {
            FinancialYearRange range = new FinancialYearRange();
            int calendarId = GetCalendarId(ctx);
            if (calendarId == 0) { return range; }

            int currentYearId = GetCurrentYearId(ctx, calendarId);
            if (currentYearId == 0) { return range; }

            YearBounds currentYear = GetYearBounds(ctx, calendarId, currentYearId);
            if (!currentYear.IsValid) { return range; }

            range.CurrentStart = currentYear.Start;
            range.CurrentEndExclusive = currentYear.End.Date.AddDays(1);
            range.HasCurrentYear = true;
            return range;
        }

        private int GetCalendarId(Ctx ctx)
        {
            int calendarId = 0;
            if (ctx.GetAD_Org_ID() > 0)
            {
                string orgSql = @"
                    SELECT OrgInfo.C_Calendar_ID
                    FROM AD_OrgInfo OrgInfo
                    WHERE OrgInfo.IsActive='Y'
                      AND OrgInfo.AD_Client_ID=@Org_Client_ID
                      AND OrgInfo.AD_Org_ID=@Org_ID";

                orgSql = AddAccessSql(ctx, orgSql, "OrgInfo");
                calendarId = Util.GetValueOfInt(DB.ExecuteScalar(
                    orgSql,
                    new SqlParameter[]
                    {
                        new SqlParameter("@Org_Client_ID", ctx.GetAD_Client_ID()),
                        new SqlParameter("@Org_ID", ctx.GetAD_Org_ID())
                    },
                    null
                ));
            }

            if (calendarId > 0) { return calendarId; }

            string clientSql = @"
                SELECT ClientInfo.C_Calendar_ID
                FROM AD_ClientInfo ClientInfo
                WHERE ClientInfo.IsActive='Y'
                  AND ClientInfo.AD_Client_ID=@Client_ID";

            clientSql = AddAccessSql(ctx, clientSql, "ClientInfo");
            return Util.GetValueOfInt(DB.ExecuteScalar(
                clientSql,
                new SqlParameter[] { new SqlParameter("@Client_ID", ctx.GetAD_Client_ID()) },
                null
            ));
        }

        private int GetCurrentYearId(Ctx ctx, int calendarId)
        {
            string sql = @"
                SELECT Period.C_Year_ID
                FROM C_Period Period
                INNER JOIN C_Year YearInfo ON (YearInfo.C_Year_ID=Period.C_Year_ID AND YearInfo.IsActive='Y')
                WHERE Period.IsActive='Y'
                  AND Period.AD_Client_ID=@Period_Client_ID
                  AND Period.AD_Org_ID IN (0,COALESCE(NULLIF(@Period_Org_ID,0),Period.AD_Org_ID))
                  AND YearInfo.AD_Client_ID=@Year_Client_ID
                  AND YearInfo.AD_Org_ID IN (0,COALESCE(NULLIF(@Year_Org_ID,0),YearInfo.AD_Org_ID))
                  AND YearInfo.C_Calendar_ID=@Calendar_ID
                  AND CURRENT_DATE BETWEEN Period.StartDate AND Period.EndDate";

            sql = AddAccessSql(ctx, sql, "Period");
            sql += " ORDER BY Period.StartDate DESC FETCH FIRST 1 ROW ONLY";

            return Util.GetValueOfInt(DB.ExecuteScalar(
                sql,
                new SqlParameter[]
                {
                    new SqlParameter("@Period_Client_ID", ctx.GetAD_Client_ID()),
                    new SqlParameter("@Period_Org_ID", ctx.GetAD_Org_ID()),
                    new SqlParameter("@Year_Client_ID", ctx.GetAD_Client_ID()),
                    new SqlParameter("@Year_Org_ID", ctx.GetAD_Org_ID()),
                    new SqlParameter("@Calendar_ID", calendarId)
                },
                null
            ));
        }

        private YearBounds GetYearBounds(Ctx ctx, int calendarId, int yearId)
        {
            string sql = @"
                SELECT MIN(Period.StartDate) AS Start_Date,
                       MAX(Period.EndDate) AS End_Date
                FROM C_Period Period
                INNER JOIN C_Year YearInfo ON (YearInfo.C_Year_ID=Period.C_Year_ID AND YearInfo.IsActive='Y')
                WHERE Period.IsActive='Y'
                  AND Period.C_Year_ID=@C_Year_ID
                  AND Period.AD_Client_ID=@Period_Client_ID
                  AND Period.AD_Org_ID IN (0,COALESCE(NULLIF(@Period_Org_ID,0),Period.AD_Org_ID))
                  AND YearInfo.AD_Client_ID=@Year_Client_ID
                  AND YearInfo.AD_Org_ID IN (0,COALESCE(NULLIF(@Year_Org_ID,0),YearInfo.AD_Org_ID))
                  AND YearInfo.C_Calendar_ID=@Calendar_ID";

            sql = AddAccessSql(ctx, sql, "Period");
            IDataReader reader = null;
            try
            {
                reader = DB.ExecuteReader(
                    sql,
                    new SqlParameter[]
                    {
                        new SqlParameter("@C_Year_ID", yearId),
                        new SqlParameter("@Period_Client_ID", ctx.GetAD_Client_ID()),
                        new SqlParameter("@Period_Org_ID", ctx.GetAD_Org_ID()),
                        new SqlParameter("@Year_Client_ID", ctx.GetAD_Client_ID()),
                        new SqlParameter("@Year_Org_ID", ctx.GetAD_Org_ID()),
                        new SqlParameter("@Calendar_ID", calendarId)
                    }
                );
                if (reader == null || !reader.Read()) { return new YearBounds(); }

                DateTime? start = Util.GetValueOfDateTime(reader["Start_Date"]);
                DateTime? end = Util.GetValueOfDateTime(reader["End_Date"]);
                if (!start.HasValue || !end.HasValue) { return new YearBounds(); }

                return new YearBounds { Start = start.Value, End = end.Value, IsValid = true };
            }
            finally
            {
                CloseReader(reader);
            }
        }

        private SchemaCurrency GetSchemaCurrency(Ctx ctx)
        {
            SchemaCurrency currency = new SchemaCurrency();
            string sql = @"
                SELECT AcctSchema.C_Currency_ID,
                       Currency.StdPrecision,
                       CASE WHEN Currency.CurSymbol IS NOT NULL THEN Currency.CurSymbol ELSE Currency.ISO_Code END AS Currency_Symbol,
                       Currency.ISO_Code AS Currency_ISO
                FROM AD_ClientInfo ClientInfo
                INNER JOIN C_AcctSchema AcctSchema ON (AcctSchema.C_AcctSchema_ID=ClientInfo.C_AcctSchema1_ID AND AcctSchema.IsActive='Y')
                INNER JOIN C_Currency Currency ON (Currency.C_Currency_ID=AcctSchema.C_Currency_ID AND Currency.IsActive='Y')
                WHERE ClientInfo.IsActive='Y'
                  AND ClientInfo.AD_Client_ID=@AD_Client_ID";

            sql = AddAccessSql(ctx, sql, "ClientInfo");
            IDataReader reader = null;
            try
            {
                reader = DB.ExecuteReader(sql, new SqlParameter[] { new SqlParameter("@AD_Client_ID", ctx.GetAD_Client_ID()) });
                if (reader == null || !reader.Read()) { return currency; }

                currency.CurrencyId = Util.GetValueOfInt(reader["C_Currency_ID"]);
                currency.Symbol = Util.GetValueOfString(reader["Currency_Symbol"]);
                currency.IsoCode = Util.GetValueOfString(reader["Currency_ISO"]);
                currency.StdPrecision = Util.GetValueOfInt(reader["StdPrecision"]);
                return currency;
            }
            finally
            {
                CloseReader(reader);
            }
        }

        private string AddAccessSql(Ctx ctx, string sql, string tableAlias)
        {
            return MRole.GetDefault(ctx).AddAccessSQL(sql, tableAlias, MRole.SQL_FULLYQUALIFIED, MRole.SQL_RO);
        }

        private void CloseReader(IDataReader reader)
        {
            if (reader == null) { return; }
            reader.Close();
            reader.Dispose();
        }

        private class SchemaCurrency
        {
            public int CurrencyId { get; set; }
            public string Symbol { get; set; }
            public string IsoCode { get; set; }
            public int StdPrecision { get; set; }
        }

        private class YearBounds
        {
            public DateTime Start { get; set; }
            public DateTime End { get; set; }
            public bool IsValid { get; set; }
        }

        private class FinancialYearRange
        {
            public DateTime CurrentStart { get; set; }
            public DateTime CurrentEndExclusive { get; set; }
            public bool HasCurrentYear { get; set; }
        }

        private class Top10Result
        {
            public string currency_symbol { get; set; }
            public string currency_iso { get; set; }
            public int std_precision { get; set; }
            public List<Top10Product> high { get; set; }
            public List<Top10Product> low { get; set; }
        }

        private class Top10Product
        {
            public int product_id { get; set; }
            public string product_name { get; set; }
            public string sku { get; set; }
            public decimal revenue { get; set; }
            public decimal units { get; set; }
        }
    }
}
