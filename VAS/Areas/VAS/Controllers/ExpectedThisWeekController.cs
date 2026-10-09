using Newtonsoft.Json;
using System;
using System.Collections.Generic;
using System.Data;
using System.Data.SqlClient;
using System.Globalization;
using System.Web.Mvc;
using VAdvantage.Classes;
using VAdvantage.DataBase;
using VAdvantage.Model;
using VAdvantage.Utility;
using VIS.Filters;

namespace VIS.Controllers
{
    public class ExpectedThisWeekController : Controller
    {
        /// <summary>
        /// Returns the KPI total for AR invoice schedules due in the next 7 days,
        /// converted to the Accounting Schema (base) currency, plus the base
        /// currency symbol and the count of due schedules.
        /// </summary>
        [AjaxAuthorizeAttribute]
        [AjaxSessionFilterAttribute]
        public JsonResult GetExpectedThisWeek()
        {
            if (Session["ctx"] == null)
            {
                return Json(new
                {
                    error = Msg.GetMsg(Env.GetCtx(), "SessionExpired") ?? "Session Expired"
                }, JsonRequestBehavior.AllowGet);
            }

            Ctx ctx = Session["ctx"] as Ctx;

            DateTime startDate = DateTime.Today;
            DateTime endDate = startDate.AddDays(7);

            string schemaCurrencySql = @"
                SELECT ClientInfo.AD_Client_ID,
                       AcctSchema.C_Currency_ID AS C_Currency_ID,
                       Currency.StdPrecision,
                       Currency.ISO_Code AS ISO_Code,
                       CASE WHEN Currency.CurSymbol IS NOT NULL THEN Currency.CurSymbol ELSE Currency.ISO_Code END AS Cur_Symbol
                FROM AD_ClientInfo ClientInfo
                INNER JOIN C_AcctSchema AcctSchema ON (ClientInfo.C_AcctSchema1_ID=AcctSchema.C_AcctSchema_ID)
                INNER JOIN C_Currency Currency ON (AcctSchema.C_Currency_ID=Currency.C_Currency_ID)
                WHERE ClientInfo.AD_Client_ID=" + ctx.GetAD_Client_ID();

            /* Expected receipts = AR invoice pay schedules + sales-order (VA009)
               pay schedules due in the window. Each source yields one converted
               (base-currency) Base_Amount per schedule row; MRole is applied only
               on each source's own physical document table. */
            string invoiceDueSql = @"
                SELECT CASE
                           WHEN Invoice.C_Currency_ID = SchemaCurrency.C_Currency_ID
                           THEN CASE WHEN Invoice.IsReturnTrx = 'N' THEN COALESCE(InvoicePaySchedule.DueAmt, 0) ELSE -1 * COALESCE(InvoicePaySchedule.DueAmt, 0) END
                           ELSE CurrencyConvert(
                               CASE WHEN Invoice.IsReturnTrx = 'N' THEN COALESCE(InvoicePaySchedule.DueAmt, 0) ELSE -1 * COALESCE(InvoicePaySchedule.DueAmt, 0) END,
                               Invoice.C_Currency_ID, SchemaCurrency.C_Currency_ID, Invoice.DateAcct, Invoice.C_ConversionType_ID, Invoice.AD_Client_ID, Invoice.AD_Org_ID)
                       END AS Base_Amount
                FROM C_InvoicePaySchedule InvoicePaySchedule
                INNER JOIN C_Invoice Invoice ON (InvoicePaySchedule.C_Invoice_ID=Invoice.C_Invoice_ID)
                INNER JOIN SchemaCurrency SchemaCurrency ON (SchemaCurrency.AD_Client_ID=Invoice.AD_Client_ID)
                WHERE Invoice.IsSoTrx = 'Y'
                  AND Invoice.IsActive = 'Y'
                  AND Invoice.DocStatus IN ('CO', 'CL')
                  AND InvoicePaySchedule.IsActive = 'Y'
                  AND InvoicePaySchedule.VA009_IsPaid = 'N'
                  AND " + TruncColumn("InvoicePaySchedule.DueDate") + @" >= " + ToSqlDate(startDate) + @"
                  AND " + TruncColumn("InvoicePaySchedule.DueDate") + @" < " + ToSqlDate(endDate);

            /* MRole applied only on the physical C_Invoice table (alias `Invoice`). */
            invoiceDueSql = MRole.GetDefault(ctx).AddAccessSQL(
                invoiceDueSql,
                "Invoice",
                MRole.SQL_FULLYQUALIFIED,
                MRole.SQL_RO
            );

            /* Sales-order pay schedules (IsSOTrx='Y') due in the same window.
               DueAmt is in the order currency; convert on the order's accounting
               date + conversion type. Non-financial order sub-types excluded. */
            string orderDueSql = @"
                SELECT CASE
                           WHEN OrderHdr.C_Currency_ID = SchemaCurrency.C_Currency_ID
                           THEN CASE WHEN OrderHdr.IsReturnTrx = 'N' THEN COALESCE(OrderSchedule.DueAmt, 0) ELSE -1 * COALESCE(OrderSchedule.DueAmt, 0) END 
                           ELSE CurrencyConvert(
                               CASE WHEN OrderHdr.IsReturnTrx = 'N' THEN COALESCE(OrderSchedule.DueAmt, 0) ELSE -1 * COALESCE(OrderSchedule.DueAmt, 0) END ,
                               OrderHdr.C_Currency_ID, SchemaCurrency.C_Currency_ID, OrderHdr.DateOrdered, OrderHdr.C_ConversionType_ID, 
                               OrderHdr.AD_Client_ID, OrderHdr.AD_Org_ID)
                       END AS Base_Amount
                FROM VA009_OrderPaySchedule OrderSchedule
                INNER JOIN C_Order OrderHdr ON (OrderSchedule.C_Order_ID=OrderHdr.C_Order_ID)
                INNER JOIN C_DocType OrderDocType ON (OrderDocType.C_DocType_ID=OrderHdr.C_DocType_ID)
                INNER JOIN SchemaCurrency SchemaCurrency ON (SchemaCurrency.AD_Client_ID=OrderHdr.AD_Client_ID)
                WHERE OrderHdr.IsSoTrx = 'Y'
                  AND OrderHdr.IsActive = 'Y'
                  AND OrderHdr.DocStatus IN ('CO', 'CL')
                  AND OrderSchedule.VA009_IsPaid = 'N'
                  AND COALESCE(OrderDocType.DocSubTypeSO, ' ') NOT IN ('BO', 'ON', 'OB')
                  AND " + TruncColumn("OrderSchedule.DueDate") + @" >= " + ToSqlDate(startDate) + @"
                  AND " + TruncColumn("OrderSchedule.DueDate") + @" < " + ToSqlDate(endDate);

            /* MRole applied only on the physical C_Order table (alias `OrderHdr`). */
            orderDueSql = MRole.GetDefault(ctx).AddAccessSQL(
                orderDueSql,
                "OrderHdr",
                MRole.SQL_FULLYQUALIFIED,
                MRole.SQL_RO
            );

            /* DueItems unions both sources; ExpectedData aggregates once (SUM=0 /
               COUNT=0 when nothing is due) so the CROSS JOIN with the single-row
               SchemaCurrency CTE always carries the base-currency symbol. */
            string sql = @"
                WITH SchemaCurrency AS (
                    " + schemaCurrencySql + @"
                ),
                DueItems AS (
                    " + invoiceDueSql + @"
                    UNION ALL
                    " + orderDueSql + @"
                ),
                ExpectedData AS (
                    SELECT COALESCE(SUM(DueItems.Base_Amount), 0) AS Expected_Amount,
                           COUNT(1) AS Schedule_Count
                    FROM DueItems
                )
                SELECT SchemaCurrency.C_Currency_ID,
                       SchemaCurrency.Cur_Symbol,
                       SchemaCurrency.ISO_Code,
                       SchemaCurrency.StdPrecision AS Std_Precision,
                       ExpectedData.Schedule_Count,
                       ROUND(
                           COALESCE(ExpectedData.Expected_Amount, 0),
                           SchemaCurrency.StdPrecision
                       ) AS Expected_Amount
                FROM SchemaCurrency
                CROSS JOIN ExpectedData";

            decimal expectedAmount = 0;
            int currencyId = 0;
            int scheduleCount = 0;
            /* Base-currency symbol (accounting schema currency); the KPI amount
               above is already converted to this currency via CurrencyConvert.
               The CROSS JOIN with the single-row SchemaCurrency CTE keeps the
               symbol/ISO/precision populated even when nothing is due (zero). */
            string currencySymbol = "";
            string isoCode = "";
            int stdPrecision = 2;

            IDataReader dr = null;

            try
            {
                dr = DB.ExecuteReader(sql);

                if (dr != null && dr.Read())
                {
                    currencyId = Util.GetValueOfInt(dr["C_Currency_ID"]);
                    expectedAmount = Util.GetValueOfDecimal(dr["Expected_Amount"]);
                    scheduleCount = Util.GetValueOfInt(dr["Schedule_Count"]);
                    currencySymbol = Util.GetValueOfString(dr["Cur_Symbol"]);
                    isoCode = Util.GetValueOfString(dr["ISO_Code"]);
                    if (dr["Std_Precision"] != null && dr["Std_Precision"] != System.DBNull.Value)
                    {
                        stdPrecision = Util.GetValueOfInt(dr["Std_Precision"]);
                    }
                }

                var result = new
                {
                    expectedAmountThisWeek = expectedAmount,
                    cCurrencyId = currencyId,
                    scheduleCount = scheduleCount,
                    symbol = currencySymbol,
                    isoCode = isoCode,
                    stdPrecision = stdPrecision,
                    fromDate = startDate.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture),
                    toDate = endDate.AddDays(-1).ToString("yyyy-MM-dd", CultureInfo.InvariantCulture)
                };

                return Json(JsonConvert.SerializeObject(result), JsonRequestBehavior.AllowGet);
            }
            catch (Exception ex)
            {
                return Json(new
                {
                    error = ex.Message
                }, JsonRequestBehavior.AllowGet);
            }
            finally
            {
                if (dr != null)
                {
                    dr.Close();
                    dr.Dispose();
                }
            }
        }

        /// <summary>
        /// Returns one page of the drill-down list of invoices/schedules due in
        /// the next 7 days. Amounts are returned in the *invoice* currency
        /// (no conversion to base) so the modal can show the original number.
        /// Server-side paged via OFFSET/FETCH so very large weeks don't ship
        /// thousands of rows to the browser.
        /// </summary>
        [AjaxAuthorizeAttribute]
        [AjaxSessionFilterAttribute]
        public JsonResult GetExpectedThisWeekRows(int pageNo = 1, int pageSize = 10)
        {
            if (Session["ctx"] == null)
            {
                return Json(new
                {
                    error = Msg.GetMsg(Env.GetCtx(), "SessionExpired") ?? "Session Expired"
                }, JsonRequestBehavior.AllowGet);
            }

            Ctx ctx = Session["ctx"] as Ctx;

            if (pageNo <= 0) { pageNo = 1; }
            if (pageSize <= 0) { pageSize = 10; }

            DateTime startDate = DateTime.Today;
            DateTime endDate = startDate.AddDays(7);

            int offset = (pageNo - 1) * pageSize;

            List<SqlParameter> parameters = new List<SqlParameter>();
            parameters.Add(new SqlParameter("@Offset", offset));
            parameters.Add(new SqlParameter("@PageSize", pageSize));

            /* Drill-down rows come from two sources — AR invoice pay schedules and
               sales-order (VA009) pay schedules — unioned. Document_Type (C_DocType
               name) distinguishes them; Doc_Date is the invoice/order date. Amounts
               stay in the document currency (no conversion) so the modal shows the
               original number. MRole is applied per source on its physical table. */
            string invoiceRowsSql = @"
                SELECT Invoice.C_Invoice_ID AS Record_ID,
                       Invoice.DocumentNo AS Document_No,
                       InvoiceDocType.Name AS Document_Type,
                       Invoice.DateInvoiced AS Doc_Date,
                       InvoicePaySchedule.DueDate AS Due_Date,
                       CASE WHEN Invoice.IsReturnTrx = 'N' THEN InvoicePaySchedule.DueAmt ELSE -1*InvoicePaySchedule.DueAmt END AS Due_Amount,
                       BPartner.Name AS Customer_Name,
                       Currency.ISO_Code AS Doc_Currency,
                       CASE WHEN Currency.CurSymbol IS NOT NULL THEN Currency.CurSymbol ELSE Currency.ISO_Code END AS Doc_Currency_Symbol
                FROM C_InvoicePaySchedule InvoicePaySchedule
                INNER JOIN C_Invoice Invoice ON (InvoicePaySchedule.C_Invoice_ID=Invoice.C_Invoice_ID)
                INNER JOIN C_BPartner BPartner ON (Invoice.C_BPartner_ID=BPartner.C_BPartner_ID)
                INNER JOIN C_Currency Currency ON (Invoice.C_Currency_ID=Currency.C_Currency_ID)
                INNER JOIN C_DocType InvoiceDocType ON (InvoiceDocType.C_DocType_ID=Invoice.C_DocType_ID)
                WHERE Invoice.IsSoTrx = 'Y'
                  AND Invoice.IsActive = 'Y'
                  AND Invoice.DocStatus IN ('CO', 'CL')
                  AND InvoicePaySchedule.IsActive = 'Y'
                  AND InvoicePaySchedule.VA009_IsPaid = 'N'
                  AND " + TruncColumn("InvoicePaySchedule.DueDate") + @" >= " + ToSqlDate(startDate) + @"
                  AND " + TruncColumn("InvoicePaySchedule.DueDate") + @" < " + ToSqlDate(endDate);

            invoiceRowsSql = MRole.GetDefault(ctx).AddAccessSQL(
                invoiceRowsSql,
                "Invoice",
                MRole.SQL_FULLYQUALIFIED,
                MRole.SQL_RO
            );

            string orderRowsSql = @"
                SELECT OrderHdr.C_Order_ID AS Record_ID,
                       OrderHdr.DocumentNo AS Document_No,
                       OrderDocType.Name AS Document_Type,
                       OrderHdr.DateOrdered AS Doc_Date,
                       OrderSchedule.DueDate AS Due_Date,
                       CASE WHEN OrderHdr.IsReturnTrx = 'N' THEN COALESCE(OrderSchedule.DueAmt, 0) ELSE -1 * COALESCE(OrderSchedule.DueAmt, 0) END AS Due_Amount,
                       BPartner.Name AS Customer_Name,
                       Currency.ISO_Code AS Doc_Currency,
                       CASE WHEN Currency.CurSymbol IS NOT NULL THEN Currency.CurSymbol ELSE Currency.ISO_Code END AS Doc_Currency_Symbol
                FROM VA009_OrderPaySchedule OrderSchedule
                INNER JOIN C_Order OrderHdr ON (OrderSchedule.C_Order_ID=OrderHdr.C_Order_ID)
                INNER JOIN C_BPartner BPartner ON (OrderHdr.C_BPartner_ID=BPartner.C_BPartner_ID)
                INNER JOIN C_Currency Currency ON (OrderHdr.C_Currency_ID=Currency.C_Currency_ID)
                INNER JOIN C_DocType OrderDocType ON (OrderDocType.C_DocType_ID=OrderHdr.C_DocType_ID)
                WHERE OrderHdr.IsSoTrx = 'Y'
                  AND OrderHdr.IsActive = 'Y'
                  AND OrderHdr.DocStatus IN ('CO', 'CL')
                  AND OrderSchedule.VA009_IsPaid = 'N'
                  AND COALESCE(OrderDocType.DocSubTypeSO, ' ') NOT IN ('BO', 'ON', 'OB')
                  AND " + TruncColumn("OrderSchedule.DueDate") + @" >= " + ToSqlDate(startDate) + @"
                  AND " + TruncColumn("OrderSchedule.DueDate") + @" < " + ToSqlDate(endDate);

            orderRowsSql = MRole.GetDefault(ctx).AddAccessSQL(
                orderRowsSql,
                "OrderHdr",
                MRole.SQL_FULLYQUALIFIED,
                MRole.SQL_RO
            );

            /* CountData CTE produces a single TotalRecords value cross-joined to
               every page row so the JS pager has totalRecords/totalPages without
               a second round-trip. OFFSET/FETCH NEXT is portable across Oracle
               12c+ and PostgreSQL. */
            string sql = @"
                WITH ExpectedRowsData AS (
                    " + invoiceRowsSql + @"
                    UNION ALL
                    " + orderRowsSql + @"
                ),
                CountData AS (
                    SELECT COUNT(1) AS TotalRecords
                    FROM ExpectedRowsData
                )
                SELECT ExpectedRowsData.Record_ID,
                       ExpectedRowsData.Document_No,
                       ExpectedRowsData.Document_Type,
                       ExpectedRowsData.Doc_Date,
                       ExpectedRowsData.Due_Date,
                       ExpectedRowsData.Due_Amount,
                       ExpectedRowsData.Customer_Name,
                       ExpectedRowsData.Doc_Currency,
                       ExpectedRowsData.Doc_Currency_Symbol,
                       CountData.TotalRecords
                FROM ExpectedRowsData
                CROSS JOIN CountData
                ORDER BY ExpectedRowsData.Due_Date ASC, ExpectedRowsData.Document_No ASC
                OFFSET @Offset ROWS FETCH NEXT @PageSize ROWS ONLY";

            List<object> rows = new List<object>();
            int totalRecords = 0;

            IDataReader dr = null;

            try
            {
                dr = DB.ExecuteReader(sql, parameters.ToArray());

                while (dr != null && dr.Read())
                {
                    totalRecords = Util.GetValueOfInt(dr["TotalRecords"]);

                    DateTime? dueDate = Util.GetValueOfDateTime(dr["Due_Date"]);
                    DateTime? docDate = Util.GetValueOfDateTime(dr["Doc_Date"]);

                    rows.Add(new
                    {
                        recordId = Util.GetValueOfInt(dr["Record_ID"]),
                        documentNo = Util.GetValueOfString(dr["Document_No"]),
                        documentType = Util.GetValueOfString(dr["Document_Type"]),
                        documentDate = docDate.HasValue
                            ? docDate.Value.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture)
                            : "",
                        dueDate = dueDate.HasValue
                            ? dueDate.Value.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture)
                            : "",
                        amount = Util.GetValueOfDecimal(dr["Due_Amount"]),
                        customer = Util.GetValueOfString(dr["Customer_Name"]),
                        invoiceCurrency = Util.GetValueOfString(dr["Doc_Currency"]),
                        invoiceCurrencySymbol = Util.GetValueOfString(dr["Doc_Currency_Symbol"])
                    });
                }

                var result = new
                {
                    rows = rows,
                    pageNo = pageNo,
                    pageSize = pageSize,
                    totalRecords = totalRecords,
                    totalPages = pageSize == 0 ? 0 : Convert.ToInt32(Math.Ceiling((decimal)totalRecords / pageSize)),
                    fromDate = startDate.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture),
                    toDate = endDate.AddDays(-1).ToString("yyyy-MM-dd", CultureInfo.InvariantCulture)
                };

                return Json(JsonConvert.SerializeObject(result), JsonRequestBehavior.AllowGet);
            }
            catch (Exception ex)
            {
                return Json(new
                {
                    error = ex.Message
                }, JsonRequestBehavior.AllowGet);
            }
            finally
            {
                if (dr != null)
                {
                    dr.Close();
                    dr.Dispose();
                }
            }
        }

        private static readonly Dictionary<string, (string MessageKey, string DefaultName)> PaymentRuleNames =
             new Dictionary<string, (string MessageKey, string DefaultName)>
             {
                    { "B", ("DirectDebit", "Direct Debit") },
                    { "K", ("Cheque", "Cheque") },
                    { "S", ("Check", "Check") },
                    { "T", ("BankTransfer", "Bank Transfer") },
                    { "P", ("OnCredit", "On Credit") }
             };

        private string GetPaymentRuleName(Ctx ctx, string paymentRule)
        {
            if (string.IsNullOrEmpty(paymentRule))
            {
                return "";
            }

            if (PaymentRuleNames.TryGetValue(paymentRule, out var paymentRuleInfo))
            {
                return GetMsg(ctx, paymentRuleInfo.MessageKey, paymentRuleInfo.DefaultName);
            }

            return paymentRule;
        }

        private string GetMsg(Ctx ctx, string key, string fallback)
        {
            string msg = Msg.GetMsg(ctx, key);
            if (string.IsNullOrEmpty(msg) || (msg.StartsWith("[") && msg.EndsWith("]")))
            {
                return fallback;
            }
            return msg;
        }

        internal static string ToSqlDate(DateTime date)
        {
            DateTime day = date.Date;

            if (DB.IsOracle())
            {
                return "TO_DATE('"
                    + day.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture)
                    + "','YYYY-MM-DD')";
            }

            return DB.TO_DATE(day, true);
        }

        internal static string TruncColumn(string columnExpression)
        {
            if (DB.IsOracle())
            {
                return "TRUNC(" + columnExpression + ")";
            }

            return columnExpression;
        }
    }
}
