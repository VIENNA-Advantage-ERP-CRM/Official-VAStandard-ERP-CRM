using Newtonsoft.Json;
using System;
using System.Collections.Generic;
using System.Data;
using System.Web.Mvc;
using VAdvantage.Classes;
using VAdvantage.DataBase;
using VAdvantage.Model;
using VAdvantage.Utility;
using VIS.Filters;

namespace VIS.Controllers
{
    public class ExpectedReceiptsController : Controller
    {
        /// <summary>
        /// Returns AR invoices/pay schedules expected to be received, with pagination and filters.
        /// </summary>
        [AjaxAuthorizeAttribute]
        [AjaxSessionFilterAttribute]
        public JsonResult GetExpectedReceipts(string filterType, string fromDate, string toDate, string searchText, int pageNo = 1, int pageSize = 5)
        {
            if (Session["ctx"] == null)
            {
                return Json(new { error = Msg.GetMsg(Env.GetCtx(), "SessionExpired") ?? "Session Expired" }, JsonRequestBehavior.AllowGet);
            }

            Ctx ctx = Session["ctx"] as Ctx;

            if (pageNo <= 0)
            {
                pageNo = 1;
            }

            if (pageSize <= 0)
            {
                pageSize = 5;
            }

            DateTime startDate;
            DateTime endDate;
            GetDateRange(filterType, fromDate, toDate, out startDate, out endDate);

            string startDateSql = DB.TO_DATE(startDate, true);
            string endDateSql = DB.TO_DATE(endDate, true);

            int offset = (pageNo - 1) * pageSize;

            string schemaCurrencySql = @"
                SELECT ClientInfo.AD_Client_ID,
                       AcctSchema.C_Currency_ID AS C_Currency_ID,
                       Currency.StdPrecision
                FROM AD_ClientInfo ClientInfo
                INNER JOIN C_AcctSchema AcctSchema ON (ClientInfo.C_AcctSchema1_ID=AcctSchema.C_AcctSchema_ID)
                INNER JOIN C_Currency Currency ON (AcctSchema.C_Currency_ID=Currency.C_Currency_ID)";

            string expectedReceiptsBaseSql = @"
                SELECT Invoice.C_Invoice_ID,
                       Invoice.DocumentNo,
                       BusinessPartner.Name AS CustomerName,
                       InvoicePaySchedule.DueDate,
                       SchemaCurrency.C_Currency_ID,
                       SchemaCurrency.StdPrecision,
                       CASE
                           WHEN Invoice.C_Currency_ID=SchemaCurrency.C_Currency_ID THEN COALESCE(InvoicePaySchedule.DueAmt, 0)
                           ELSE CurrencyConvert(
                               COALESCE(InvoicePaySchedule.DueAmt, 0),
                               Invoice.C_Currency_ID,
                               SchemaCurrency.C_Currency_ID,
                               Invoice.DateAcct,
                               Invoice.C_ConversionType_ID,
                               Invoice.AD_Client_ID,
                               Invoice.AD_Org_ID
                           )
                       END AS ExpectedAmount,
                       CASE
                           WHEN Invoice.PaymentRule='B' THEN 'Direct Debit'
                           WHEN Invoice.PaymentRule='K' THEN 'Cheque'
                           WHEN Invoice.PaymentRule='S' THEN 'Check'
                           WHEN Invoice.PaymentRule='T' THEN 'Bank Transfer'
                           WHEN Invoice.PaymentRule='P' THEN 'On Credit'
                           ELSE 'Expected'
                       END AS PaymentMethodName
                FROM C_Invoice Invoice
                INNER JOIN C_InvoicePaySchedule InvoicePaySchedule ON (InvoicePaySchedule.C_Invoice_ID=Invoice.C_Invoice_ID)
                INNER JOIN C_BPartner BusinessPartner ON (Invoice.C_BPartner_ID=BusinessPartner.C_BPartner_ID)
                INNER JOIN SchemaCurrency SchemaCurrency ON (SchemaCurrency.AD_Client_ID=Invoice.AD_Client_ID)
                WHERE Invoice.IsSoTrx='Y'
                AND Invoice.IsActive='Y'
                AND Invoice.DocStatus IN ('CO', 'CL')
                AND InvoicePaySchedule.IsActive='Y'
                AND InvoicePaySchedule.VA009_IsPaid='N'
                AND InvoicePaySchedule.DueDate>=" + startDateSql + @"
                AND InvoicePaySchedule.DueDate<" + endDateSql;

            if (!string.IsNullOrEmpty(searchText))
            {
                string safeSearchText = searchText.Replace("'", "''").Trim().ToUpper();

                expectedReceiptsBaseSql += @"
                AND (
                    UPPER(Invoice.DocumentNo) LIKE '%" + safeSearchText + @"%'
                    OR UPPER(BusinessPartner.Name) LIKE '%" + safeSearchText + @"%'
                )";
            }

            expectedReceiptsBaseSql = MRole.GetDefault(ctx).AddAccessSQL(
                expectedReceiptsBaseSql,
                "Invoice",
                MRole.SQL_FULLYQUALIFIED,
                MRole.SQL_RO
            );

            string sql = @"
                WITH SchemaCurrency AS (
                    " + schemaCurrencySql + @"
                ),
                ExpectedReceiptData AS (
                    " + expectedReceiptsBaseSql + @"
                ),
                CountData AS (
                    SELECT COUNT(1) AS TotalRecords
                    FROM ExpectedReceiptData
                )
                SELECT ExpectedReceiptData.C_Invoice_ID,
                       ExpectedReceiptData.DocumentNo,
                       ExpectedReceiptData.CustomerName,
                       ExpectedReceiptData.DueDate,
                       ExpectedReceiptData.C_Currency_ID,
                       ROUND(COALESCE(ExpectedReceiptData.ExpectedAmount, 0), ExpectedReceiptData.StdPrecision) AS ExpectedAmount,
                       ExpectedReceiptData.PaymentMethodName,
                       CountData.TotalRecords
                FROM ExpectedReceiptData
                CROSS JOIN CountData
                ORDER BY ExpectedReceiptData.DueDate,
                         ExpectedReceiptData.DocumentNo
                OFFSET " + offset + @" ROWS FETCH NEXT " + pageSize + @" ROWS ONLY";

            var rows = new List<object>();
            int totalRecords = 0;

            IDataReader dr = null;
            try
            {
                dr = DB.ExecuteReader(sql);

                while (dr != null && dr.Read())
                {
                    totalRecords = Util.GetValueOfInt(dr["TotalRecords"]);

                    DateTime dueDate = DateTime.MinValue;

                    if (dr["DueDate"] != null && dr["DueDate"] != DBNull.Value)
                    {
                        dueDate = Convert.ToDateTime(dr["DueDate"]);
                    }

                    rows.Add(new
                    {
                        cInvoiceId = Util.GetValueOfInt(dr["C_Invoice_ID"]),
                        documentNo = dr["DocumentNo"] == null ? "" : dr["DocumentNo"].ToString(),
                        customerName = dr["CustomerName"] == null ? "" : dr["CustomerName"].ToString(),
                        dueDate = dueDate == DateTime.MinValue ? "" : dueDate.ToString("yyyy-MM-dd"),
                        cCurrencyId = Util.GetValueOfInt(dr["C_Currency_ID"]),
                        expectedAmount = Util.GetValueOfDecimal(dr["ExpectedAmount"]),
                        paymentMethodName = dr["PaymentMethodName"] == null ? "" : dr["PaymentMethodName"].ToString()
                    });
                }

            }
            finally
            {
                if (dr != null)
                {
                    dr.Close();
                }
            }

            var result = new
            {
                rows = rows,
                pageNo = pageNo,
                pageSize = pageSize,
                totalRecords = totalRecords,
                totalPages = pageSize == 0 ? 0 : Convert.ToInt32(Math.Ceiling((decimal)totalRecords / pageSize))
            };

            return Json(JsonConvert.SerializeObject(result), JsonRequestBehavior.AllowGet);
        }

        private void GetDateRange(string filterType, string fromDate, string toDate, out DateTime startDate, out DateTime endDate)
        {
            DateTime today = DateTime.Today;
            string selectedFilter = string.IsNullOrEmpty(filterType) ? "Next7Days" : filterType;

            if (selectedFilter == "ThisMonth")
            {
                startDate = new DateTime(today.Year, today.Month, 1);
                endDate = startDate.AddMonths(1);
                return;
            }

            if (selectedFilter == "Custom")
            {
                DateTime parsedFromDate;
                DateTime parsedToDate;

                if (DateTime.TryParse(fromDate, out parsedFromDate) && DateTime.TryParse(toDate, out parsedToDate))
                {
                    startDate = parsedFromDate.Date;
                    endDate = parsedToDate.Date.AddDays(1);
                    return;
                }
            }

            startDate = today;
            endDate = today.AddDays(7);
        }
    }
}