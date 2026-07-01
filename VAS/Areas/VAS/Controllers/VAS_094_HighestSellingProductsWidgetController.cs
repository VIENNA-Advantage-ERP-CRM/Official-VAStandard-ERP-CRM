using Newtonsoft.Json;
using System;
using System.Collections.Generic;
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
    /// Module Name : VAS_094_HighestSellingProductsWidget
    /// Purpose     : Supplies accounting-year sales rankings for the Overall Inventory dashboard.
    /// Chronological development:
    ///   VAI154      2026-06-22 Created
    /// </summary>
    public class VAS_094_HighestSellingProductsWidgetController : Controller
    {
        private static readonly VLogger Log = VLogger.GetVLogger(typeof(VAS_094_HighestSellingProductsWidgetController).FullName);

        /// <summary>
        /// Returns the ten highest-selling products for the current accounting year.
        /// </summary>
        /// <returns>Serialized highest-selling product data.</returns>
        [AjaxAuthorizeAttribute]
        [AjaxSessionFilterAttribute]
        public JsonResult GetHighestSellingProducts()
        {
            Ctx ctx = Session["ctx"] as Ctx;
            if (ctx == null) { return Json("", JsonRequestBehavior.AllowGet); }

            try
            {
                HighestSellingProductsResult result = GetHighestSellingProductsData(ctx);
                return Json(JsonConvert.SerializeObject(result), JsonRequestBehavior.AllowGet);
            }
            catch (Exception ex)
            {
                return ErrorResult(ctx, ex);
            }
        }

        /// <summary>
        /// Resolves accounting periods, schema currency, and secured sales totals.
        /// </summary>
        /// <param name="ctx">Current application context.</param>
        /// <returns>Highest-selling product data and currency metadata.</returns>
        private HighestSellingProductsResult GetHighestSellingProductsData(Ctx ctx)
        {
            HighestSellingProductsResult result = new HighestSellingProductsResult
            {
                rows = new List<HighestSellingProduct>()
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
                WHERE InvoiceLine.IsActive=N'Y'
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
                WHERE Invoice.IsActive=N'Y'
                  AND Invoice.IsSOTrx=N'Y'
                  AND Invoice.DocStatus=N'CO'
                  AND Invoice.AD_Client_ID=@Invoice_Client_ID
                  AND Invoice.AD_Org_ID IN (0,COALESCE(NULLIF(@Invoice_Org_ID,0),Invoice.AD_Org_ID))
                  AND Invoice.DateInvoiced>=@Previous_Year_Start1
                  AND Invoice.DateInvoiced<@Current_Year_End1";

            string orderLineSql = @"
                SELECT OrderLine.C_OrderLine_ID,
                       OrderLine.C_Order_ID
                FROM C_OrderLine OrderLine
                WHERE OrderLine.IsActive=N'Y'
                  AND OrderLine.AD_Client_ID=@OrderLine_Client_ID
                  AND OrderLine.AD_Org_ID IN (0,COALESCE(NULLIF(@OrderLine_Org_ID,0),OrderLine.AD_Org_ID))";

            string orderSql = @"
                SELECT SalesOrder.C_Order_ID
                FROM C_Order SalesOrder
                WHERE SalesOrder.IsActive=N'Y'
                  AND SalesOrder.IsSOTrx=N'Y'
                  AND SalesOrder.AD_Client_ID=@Order_Client_ID
                  AND SalesOrder.AD_Org_ID IN (0,COALESCE(NULLIF(@Order_Org_ID,0),SalesOrder.AD_Org_ID))";

            string productSql = @"
                SELECT Product.M_Product_ID,
                       Product.Name
                FROM M_Product Product
                WHERE Product.IsActive=N'Y'
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
                           SUM(
                               CASE
                                   WHEN Invoices.DateInvoiced>=@Current_Year_Start1 AND Invoices.DateInvoiced<@Current_Year_End2
                                   THEN COALESCE(CURRENCYCONVERT(InvoiceLines.LineNetAmt,Invoices.C_Currency_ID,@Schema_Currency_ID1,Invoices.DateInvoiced,Invoices.C_ConversionType_ID,Invoices.AD_Client_ID,Invoices.AD_Org_ID),0)
                                   ELSE 0
                               END
                           ) AS Current_Year_Value,
                           SUM(
                               CASE
                                   WHEN Invoices.DateInvoiced>=@Current_Year_Start2 AND Invoices.DateInvoiced<@Current_Year_End3
                                   THEN COALESCE(InvoiceLines.QtyInvoiced,InvoiceLines.QtyEntered,0)
                                   ELSE 0
                               END
                           ) AS Current_Year_Units,
                           SUM(
                               CASE
                                   WHEN Invoices.DateInvoiced>=@Previous_Year_Start2 AND Invoices.DateInvoiced<@Previous_Year_End1
                                   THEN COALESCE(CURRENCYCONVERT(InvoiceLines.LineNetAmt,Invoices.C_Currency_ID,@Schema_Currency_ID2,Invoices.DateInvoiced,Invoices.C_ConversionType_ID,Invoices.AD_Client_ID,Invoices.AD_Org_ID),0)
                                   ELSE 0
                               END
                           ) AS Previous_Year_Value,
                           SUM(
                               CASE
                                   WHEN Invoices.DateInvoiced>=@Previous_Year_Start3 AND Invoices.DateInvoiced<@Previous_Year_End2
                                   THEN COALESCE(InvoiceLines.QtyInvoiced,InvoiceLines.QtyEntered,0)
                                   ELSE 0
                               END
                           ) AS Previous_Year_Units
                    FROM InvoiceLines
                    INNER JOIN Invoices ON (Invoices.C_Invoice_ID=InvoiceLines.C_Invoice_ID)
                    INNER JOIN OrderLines ON (OrderLines.C_OrderLine_ID=InvoiceLines.C_OrderLine_ID)
                    INNER JOIN SalesOrders ON (SalesOrders.C_Order_ID=OrderLines.C_Order_ID)
                    INNER JOIN Products ON (Products.M_Product_ID=InvoiceLines.M_Product_ID)
                    GROUP BY Products.M_Product_ID,
                             Products.Name
                )
                SELECT Sales.M_Product_ID,
                       Sales.Product_Name,
                       Sales.Current_Year_Value,
                       Sales.Current_Year_Units,
                       Sales.Previous_Year_Value,
                       Sales.Previous_Year_Units
                FROM Sales
                WHERE Sales.Current_Year_Value>0
                ORDER BY Sales.Current_Year_Value DESC
                FETCH FIRST 10 ROWS ONLY",
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
                new SqlParameter("@Previous_Year_Start1", SqlDbType.DateTime) { Value = financialYears.PreviousStart },
                new SqlParameter("@Current_Year_End1", SqlDbType.DateTime) { Value = financialYears.CurrentEndExclusive },
                new SqlParameter("@OrderLine_Client_ID", ctx.GetAD_Client_ID()),
                new SqlParameter("@OrderLine_Org_ID", ctx.GetAD_Org_ID()),
                new SqlParameter("@Order_Client_ID", ctx.GetAD_Client_ID()),
                new SqlParameter("@Order_Org_ID", ctx.GetAD_Org_ID()),
                new SqlParameter("@Product_Client_ID", ctx.GetAD_Client_ID()),
                new SqlParameter("@Product_Org_ID", ctx.GetAD_Org_ID()),
                new SqlParameter("@Current_Year_Start1", SqlDbType.DateTime) { Value = financialYears.CurrentStart },
                new SqlParameter("@Current_Year_End2", SqlDbType.DateTime) { Value = financialYears.CurrentEndExclusive },
                new SqlParameter("@Schema_Currency_ID1", currency.CurrencyId),
                new SqlParameter("@Current_Year_Start2", SqlDbType.DateTime) { Value = financialYears.CurrentStart },
                new SqlParameter("@Current_Year_End3", SqlDbType.DateTime) { Value = financialYears.CurrentEndExclusive },
                new SqlParameter("@Previous_Year_Start2", SqlDbType.DateTime) { Value = financialYears.PreviousStart },
                new SqlParameter("@Previous_Year_End1", SqlDbType.DateTime) { Value = financialYears.PreviousEndExclusive },
                new SqlParameter("@Schema_Currency_ID2", currency.CurrencyId),
                new SqlParameter("@Previous_Year_Start3", SqlDbType.DateTime) { Value = financialYears.PreviousStart },
                new SqlParameter("@Previous_Year_End2", SqlDbType.DateTime) { Value = financialYears.PreviousEndExclusive }
            };

            IDataReader reader = null;
            try
            {
                reader = DB.ExecuteReader(sql, parameters);
                while (reader != null && reader.Read())
                {
                    result.rows.Add(new HighestSellingProduct
                    {
                        product_id = Util.GetValueOfInt(reader["M_Product_ID"]),
                        product_name = Util.GetValueOfString(reader["Product_Name"]),
                        current_year_value = Util.GetValueOfDecimal(reader["Current_Year_Value"]),
                        current_year_units = Util.GetValueOfDecimal(reader["Current_Year_Units"]),
                        previous_year_value = Util.GetValueOfDecimal(reader["Previous_Year_Value"]),
                        previous_year_units = Util.GetValueOfDecimal(reader["Previous_Year_Units"])
                    });
                }
            }
            finally
            {
                CloseReader(reader);
            }

            return result;
        }

        /// <summary>
        /// Resolves current and previous accounting-year boundaries from Period Control.
        /// </summary>
        /// <param name="ctx">Current application context.</param>
        /// <returns>Current and previous accounting-year date ranges.</returns>
        private FinancialYearRange GetFinancialYearRange(Ctx ctx)
        {
            FinancialYearRange range = new FinancialYearRange();
            int calendarId = GetCalendarId(ctx);
            if (calendarId == 0) { return range; }

            int currentYearId = GetCurrentYearId(ctx, calendarId);
            if (currentYearId == 0) { return range; }

            YearBounds currentYear = GetYearBounds(ctx, calendarId, currentYearId);
            if (!currentYear.IsValid) { return range; }

            YearBounds previousYear = GetPreviousYearBounds(ctx, calendarId, currentYear.Start);
            range.CurrentStart = currentYear.Start;
            range.CurrentEndExclusive = currentYear.End.Date.AddDays(1);
            range.PreviousStart = previousYear.IsValid ? previousYear.Start : currentYear.Start;
            range.PreviousEndExclusive = previousYear.IsValid ? previousYear.End.Date.AddDays(1) : currentYear.Start;
            range.HasCurrentYear = true;
            return range;
        }

        /// <summary>
        /// Resolves the organization calendar, falling back to the tenant calendar.
        /// </summary>
        /// <param name="ctx">Current application context.</param>
        /// <returns>Configured calendar identifier.</returns>
        private int GetCalendarId(Ctx ctx)
        {
            int calendarId = 0;
            if (ctx.GetAD_Org_ID() > 0)
            {
                string orgSql = @"
                    SELECT OrgInfo.C_Calendar_ID
                    FROM AD_OrgInfo OrgInfo
                    WHERE OrgInfo.IsActive=N'Y'
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
                WHERE ClientInfo.IsActive=N'Y'
                  AND ClientInfo.AD_Client_ID=@Client_ID";

            clientSql = AddAccessSql(ctx, clientSql, "ClientInfo");
            return Util.GetValueOfInt(DB.ExecuteScalar(
                clientSql,
                new SqlParameter[] { new SqlParameter("@Client_ID", ctx.GetAD_Client_ID()) },
                null
            ));
        }

        /// <summary>
        /// Finds the accounting year containing the current active period.
        /// </summary>
        /// <param name="ctx">Current application context.</param>
        /// <param name="calendarId">Configured accounting calendar.</param>
        /// <returns>Current accounting-year identifier.</returns>
        private int GetCurrentYearId(Ctx ctx, int calendarId)
        {
            string sql = @"
                SELECT Period.C_Year_ID
                FROM C_Period Period
                INNER JOIN C_Year YearInfo ON (YearInfo.C_Year_ID=Period.C_Year_ID AND YearInfo.IsActive=N'Y')
                WHERE Period.IsActive=N'Y'
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

        /// <summary>
        /// Gets the first and last active period dates for one accounting year.
        /// </summary>
        /// <param name="ctx">Current application context.</param>
        /// <param name="calendarId">Configured accounting calendar.</param>
        /// <param name="yearId">Accounting-year identifier.</param>
        /// <returns>Accounting-year date bounds.</returns>
        private YearBounds GetYearBounds(Ctx ctx, int calendarId, int yearId)
        {
            string sql = @"
                SELECT MIN(Period.StartDate) AS Start_Date,
                       MAX(Period.EndDate) AS End_Date
                FROM C_Period Period
                INNER JOIN C_Year YearInfo ON (YearInfo.C_Year_ID=Period.C_Year_ID AND YearInfo.IsActive=N'Y')
                WHERE Period.IsActive=N'Y'
                  AND Period.C_Year_ID=@C_Year_ID
                  AND Period.AD_Client_ID=@Period_Client_ID
                  AND Period.AD_Org_ID IN (0,COALESCE(NULLIF(@Period_Org_ID,0),Period.AD_Org_ID))
                  AND YearInfo.AD_Client_ID=@Year_Client_ID
                  AND YearInfo.AD_Org_ID IN (0,COALESCE(NULLIF(@Year_Org_ID,0),YearInfo.AD_Org_ID))
                  AND YearInfo.C_Calendar_ID=@Calendar_ID";

            sql = AddAccessSql(ctx, sql, "Period");
            return ReadYearBounds(
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
        }

        /// <summary>
        /// Gets the latest configured accounting year ending before the current year.
        /// </summary>
        /// <param name="ctx">Current application context.</param>
        /// <param name="calendarId">Configured accounting calendar.</param>
        /// <param name="currentYearStart">Current accounting-year start date.</param>
        /// <returns>Previous accounting-year date bounds.</returns>
        private YearBounds GetPreviousYearBounds(Ctx ctx, int calendarId, DateTime currentYearStart)
        {
            string sql = @"
                SELECT MIN(Period.StartDate) AS Start_Date,
                       MAX(Period.EndDate) AS End_Date
                FROM C_Year YearInfo
                INNER JOIN C_Period Period ON (Period.C_Year_ID=YearInfo.C_Year_ID AND Period.IsActive=N'Y')
                WHERE YearInfo.IsActive=N'Y'
                  AND YearInfo.C_Calendar_ID=@Calendar_ID
                  AND YearInfo.AD_Client_ID=@Year_Client_ID
                  AND YearInfo.AD_Org_ID IN (0,COALESCE(NULLIF(@Year_Org_ID,0),YearInfo.AD_Org_ID))
                  AND Period.AD_Client_ID=@Period_Client_ID
                  AND Period.AD_Org_ID IN (0,COALESCE(NULLIF(@Period_Org_ID,0),Period.AD_Org_ID))
                  AND Period.EndDate<@Current_Year_Start";

            sql = AddAccessSql(ctx, sql, "YearInfo");
            sql += @"
                GROUP BY YearInfo.C_Year_ID
                ORDER BY MAX(Period.EndDate) DESC
                FETCH FIRST 1 ROW ONLY";

            return ReadYearBounds(
                sql,
                new SqlParameter[]
                {
                    new SqlParameter("@Calendar_ID", calendarId),
                    new SqlParameter("@Year_Client_ID", ctx.GetAD_Client_ID()),
                    new SqlParameter("@Year_Org_ID", ctx.GetAD_Org_ID()),
                    new SqlParameter("@Period_Client_ID", ctx.GetAD_Client_ID()),
                    new SqlParameter("@Period_Org_ID", ctx.GetAD_Org_ID()),
                    new SqlParameter("@Current_Year_Start", SqlDbType.DateTime) { Value = currentYearStart }
                }
            );
        }

        /// <summary>
        /// Reads named start and end date columns into a year-bound value.
        /// </summary>
        /// <param name="sql">Parameterized accounting-year query.</param>
        /// <param name="parameters">Query parameters.</param>
        /// <returns>Accounting-year date bounds.</returns>
        private YearBounds ReadYearBounds(string sql, SqlParameter[] parameters)
        {
            IDataReader reader = null;
            try
            {
                reader = DB.ExecuteReader(sql, parameters);
                if (reader == null || !reader.Read()) { return new YearBounds(); }

                DateTime? start = Util.GetValueOfDateTime(reader["Start_Date"]);
                DateTime? end = Util.GetValueOfDateTime(reader["End_Date"]);
                if (!start.HasValue || !end.HasValue) { return new YearBounds(); }

                return new YearBounds
                {
                    Start = start.Value,
                    End = end.Value,
                    IsValid = true
                };
            }
            finally
            {
                CloseReader(reader);
            }
        }

        /// <summary>
        /// Gets the tenant accounting-schema currency and its standard precision.
        /// </summary>
        /// <param name="ctx">Current application context.</param>
        /// <returns>Schema currency metadata.</returns>
        private SchemaCurrency GetSchemaCurrency(Ctx ctx)
        {
            SchemaCurrency currency = new SchemaCurrency();
            string sql = @"
                SELECT AcctSchema.C_Currency_ID,
                       Currency.StdPrecision,
                       CASE
                           WHEN Currency.CurSymbol IS NOT NULL THEN Currency.CurSymbol
                           ELSE Currency.ISO_Code
                       END AS Currency_Symbol,
                       Currency.ISO_Code AS Currency_ISO
                FROM AD_ClientInfo ClientInfo
                INNER JOIN C_AcctSchema AcctSchema ON (AcctSchema.C_AcctSchema_ID=ClientInfo.C_AcctSchema1_ID AND AcctSchema.IsActive=N'Y')
                INNER JOIN C_Currency Currency ON (Currency.C_Currency_ID=AcctSchema.C_Currency_ID AND Currency.IsActive=N'Y')
                WHERE ClientInfo.IsActive=N'Y'
                  AND ClientInfo.AD_Client_ID=@AD_Client_ID";

            sql = AddAccessSql(ctx, sql, "ClientInfo");
            IDataReader reader = null;
            try
            {
                reader = DB.ExecuteReader(
                    sql,
                    new SqlParameter[] { new SqlParameter("@AD_Client_ID", ctx.GetAD_Client_ID()) }
                );
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

        /// <summary>
        /// Adds read-only role access to a query whose named alias is a physical table.
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

        /// <summary>
        /// Closes and disposes a database reader.
        /// </summary>
        /// <param name="reader">Reader to close.</param>
        private void CloseReader(IDataReader reader)
        {
            if (reader == null) { return; }
            reader.Close();
            reader.Dispose();
        }

        /// <summary>
        /// Logs a controller error and returns a localized error payload.
        /// </summary>
        /// <param name="ctx">Current application context.</param>
        /// <param name="ex">Unhandled controller exception.</param>
        /// <returns>Localized JSON error.</returns>
        private JsonResult ErrorResult(Ctx ctx, Exception ex)
        {
            Log.Log(Level.SEVERE, "VAS_094_HighestSellingProductsWidget.GetHighestSellingProducts", ex);
            string json = JsonConvert.SerializeObject(new { error = Msg.GetMsg(ctx, "Error") ?? "Error" });
            return Json(json, JsonRequestBehavior.AllowGet);
        }

        /// <summary>Schema currency metadata.</summary>
        private class SchemaCurrency
        {
            public int CurrencyId { get; set; }
            public string Symbol { get; set; }
            public string IsoCode { get; set; }
            public int StdPrecision { get; set; }
        }

        /// <summary>Configured accounting-year bounds.</summary>
        private class YearBounds
        {
            public DateTime Start { get; set; }
            public DateTime End { get; set; }
            public bool IsValid { get; set; }
        }

        /// <summary>Current and previous accounting-year date ranges.</summary>
        private class FinancialYearRange
        {
            public DateTime CurrentStart { get; set; }
            public DateTime CurrentEndExclusive { get; set; }
            public DateTime PreviousStart { get; set; }
            public DateTime PreviousEndExclusive { get; set; }
            public bool HasCurrentYear { get; set; }
        }

        /// <summary>Highest-selling products response.</summary>
        private class HighestSellingProductsResult
        {
            public string currency_symbol { get; set; }
            public string currency_iso { get; set; }
            public int std_precision { get; set; }
            public List<HighestSellingProduct> rows { get; set; }
        }

        /// <summary>One ranked product's current and previous accounting-year totals.</summary>
        private class HighestSellingProduct
        {
            public int product_id { get; set; }
            public string product_name { get; set; }
            public decimal current_year_value { get; set; }
            public decimal current_year_units { get; set; }
            public decimal previous_year_value { get; set; }
            public decimal previous_year_units { get; set; }
        }
    }
}
