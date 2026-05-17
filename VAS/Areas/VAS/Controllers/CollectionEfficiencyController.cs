using Newtonsoft.Json;
using System;
using System.Data;
using System.Web.Mvc;
using VAdvantage.Classes;
using VAdvantage.DataBase;
using VAdvantage.Model;
using VAdvantage.Utility;
using VIS.Filters;

namespace VIS.Controllers
{
    public class CollectionEfficiencyController : Controller
    {
        /// <summary>
        /// Returns collection efficiency, DSO, overdue amount and overdue invoice count.
        /// </summary>
        [AjaxAuthorizeAttribute]
        [AjaxSessionFilterAttribute]
        public JsonResult GetCollectionEfficiency(string filterType, string fromDate, string toDate)
        {
            if (Session["ctx"] == null)
            {
                return Json(new { error = Msg.GetMsg(Env.GetCtx(), "SessionExpired") ?? "Session Expired" }, JsonRequestBehavior.AllowGet);
            }

            Ctx ctx = Session["ctx"] as Ctx;

            DateTime startDate;
            DateTime endDate;
            GetDateRange(filterType, fromDate, toDate, out startDate, out endDate);

            string startDateSql = DB.TO_DATE(startDate, true);
            string endDateSql = DB.TO_DATE(endDate, true);
            string todaySql = DB.TO_DATE(DateTime.Today, true);

            string schemaCurrencySql = @"
                SELECT ClientInfo.AD_Client_ID,
                       AcctSchema.C_Currency_ID AS C_Currency_ID,
                       Currency.StdPrecision
                FROM AD_ClientInfo ClientInfo
                INNER JOIN C_AcctSchema AcctSchema ON (ClientInfo.C_AcctSchema1_ID=AcctSchema.C_AcctSchema_ID)
                INNER JOIN C_Currency Currency ON (AcctSchema.C_Currency_ID=Currency.C_Currency_ID)";

            string invoiceScheduleSql = @"
                SELECT SchemaCurrency.C_Currency_ID,
                       SchemaCurrency.StdPrecision,
                       SUM(
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
                           END
                       ) AS TotalDueAmount,
                       SUM(
                           CASE
                               WHEN InvoicePaySchedule.VA009_IsPaid='N'
                               AND InvoicePaySchedule.DueDate<" + todaySql + @" THEN
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
                                   END
                               ELSE 0
                           END
                       ) AS OverdueAmount,
                       COUNT(DISTINCT CASE
                           WHEN InvoicePaySchedule.VA009_IsPaid='N'
                           AND InvoicePaySchedule.DueDate<" + todaySql + @" THEN Invoice.C_Invoice_ID
                           ELSE NULL
                       END) AS OverdueInvoiceCount
                FROM C_Invoice Invoice
                INNER JOIN C_InvoicePaySchedule InvoicePaySchedule ON (InvoicePaySchedule.C_Invoice_ID=Invoice.C_Invoice_ID)
                INNER JOIN SchemaCurrency SchemaCurrency ON (SchemaCurrency.AD_Client_ID=Invoice.AD_Client_ID)
                WHERE Invoice.IsSoTrx='Y'
                AND Invoice.IsActive='Y'
                AND Invoice.DocStatus IN ('CO', 'CL')
                AND InvoicePaySchedule.IsActive='Y'
                AND InvoicePaySchedule.DueDate>=" + startDateSql + @"
                AND InvoicePaySchedule.DueDate<" + endDateSql;

            invoiceScheduleSql = MRole.GetDefault(ctx).AddAccessSQL(
                invoiceScheduleSql,
                "Invoice",
                MRole.SQL_FULLYQUALIFIED,
                MRole.SQL_RO
            );

            invoiceScheduleSql += @"
                GROUP BY SchemaCurrency.C_Currency_ID,
                         SchemaCurrency.StdPrecision";

            string dsoSql = @"
                SELECT SchemaCurrency.C_Currency_ID,
                       CASE
                           WHEN SUM(
                               CASE
                                   WHEN AllocationHdr.C_Currency_ID=SchemaCurrency.C_Currency_ID THEN COALESCE(AllocationLine.Amount, 0)
                                   ELSE CurrencyConvert(
                                       COALESCE(AllocationLine.Amount, 0),
                                       AllocationHdr.C_Currency_ID,
                                       SchemaCurrency.C_Currency_ID,
                                       AllocationHdr.DateAcct,
                                       AllocationHdr.C_ConversionType_ID,
                                       AllocationHdr.AD_Client_ID,
                                       AllocationHdr.AD_Org_ID
                                   )
                               END
                           )=0 THEN 0
                           ELSE ROUND(
                               SUM(
                                   CASE
                                       WHEN AllocationHdr.C_Currency_ID=SchemaCurrency.C_Currency_ID THEN COALESCE(AllocationLine.Amount, 0)
                                       ELSE CurrencyConvert(
                                           COALESCE(AllocationLine.Amount, 0),
                                           AllocationHdr.C_Currency_ID,
                                           SchemaCurrency.C_Currency_ID,
                                           AllocationHdr.DateAcct,
                                           AllocationHdr.C_ConversionType_ID,
                                           AllocationHdr.AD_Client_ID,
                                           AllocationHdr.AD_Org_ID
                                       )
                                   END * (AllocationHdr.DateAcct-Invoice.DateInvoiced)
                               ) / SUM(
                                   CASE
                                       WHEN AllocationHdr.C_Currency_ID=SchemaCurrency.C_Currency_ID THEN COALESCE(AllocationLine.Amount, 0)
                                       ELSE CurrencyConvert(
                                           COALESCE(AllocationLine.Amount, 0),
                                           AllocationHdr.C_Currency_ID,
                                           SchemaCurrency.C_Currency_ID,
                                           AllocationHdr.DateAcct,
                                           AllocationHdr.C_ConversionType_ID,
                                           AllocationHdr.AD_Client_ID,
                                           AllocationHdr.AD_Org_ID
                                       )
                                   END
                               ),
                               0
                           )
                       END AS DsoDays
                FROM C_Invoice Invoice
                INNER JOIN C_AllocationLine AllocationLine ON (AllocationLine.C_Invoice_ID=Invoice.C_Invoice_ID AND AllocationLine.IsActive='Y')
                INNER JOIN C_AllocationHdr AllocationHdr ON (AllocationLine.C_AllocationHdr_ID=AllocationHdr.C_AllocationHdr_ID)
                INNER JOIN SchemaCurrency SchemaCurrency ON (SchemaCurrency.AD_Client_ID=Invoice.AD_Client_ID)
                WHERE Invoice.IsSoTrx='Y'
                AND Invoice.IsActive='Y'
                AND Invoice.DocStatus IN ('CO', 'CL')
                AND AllocationHdr.IsActive='Y'
                AND AllocationHdr.DocStatus IN ('CO', 'CL')
                AND AllocationHdr.DateAcct>=" + startDateSql + @"
                AND AllocationHdr.DateAcct<" + endDateSql;

            dsoSql = MRole.GetDefault(ctx).AddAccessSQL(
                dsoSql,
                "Invoice",
                MRole.SQL_FULLYQUALIFIED,
                MRole.SQL_RO
            );

            dsoSql += @"
                GROUP BY SchemaCurrency.C_Currency_ID";

            string sql = @"
                WITH SchemaCurrency AS (
                    " + schemaCurrencySql + @"
                ),
                InvoiceScheduleData AS (
                    " + invoiceScheduleSql + @"
                ),
                DsoData AS (
                    " + dsoSql + @"
                )
                SELECT InvoiceScheduleData.C_Currency_ID,
                       ROUND(COALESCE(InvoiceScheduleData.OverdueAmount, 0), InvoiceScheduleData.StdPrecision) AS OverdueAmount,
                       InvoiceScheduleData.OverdueInvoiceCount,
                       COALESCE(DsoData.DsoDays, 0) AS DsoDays,
                       CASE
                           WHEN COALESCE(InvoiceScheduleData.TotalDueAmount, 0)=0 THEN 100
                           ELSE ROUND(((InvoiceScheduleData.TotalDueAmount-InvoiceScheduleData.OverdueAmount)*100.0)/InvoiceScheduleData.TotalDueAmount, 0)
                       END AS CollectionEfficiencyPercent
                FROM InvoiceScheduleData
                LEFT OUTER JOIN DsoData ON (InvoiceScheduleData.C_Currency_ID=DsoData.C_Currency_ID)";

            int currencyId = 0;
            decimal overdueAmount = 0;
            int overdueInvoiceCount = 0;
            int dsoDays = 0;
            decimal collectionEfficiencyPercent = 0;

            IDataReader dr = null;
            try
            {
                dr = DB.ExecuteReader(sql);

                if (dr != null && dr.Read())
                {
                    currencyId = Util.GetValueOfInt(dr["C_Currency_ID"]);
                    overdueAmount = Util.GetValueOfDecimal(dr["OverdueAmount"]);
                    overdueInvoiceCount = Util.GetValueOfInt(dr["OverdueInvoiceCount"]);
                    dsoDays = Util.GetValueOfInt(dr["DsoDays"]);
                    collectionEfficiencyPercent = Util.GetValueOfDecimal(dr["CollectionEfficiencyPercent"]);
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
                cCurrencyId = currencyId,
                overdueAmount = overdueAmount,
                overdueInvoiceCount = overdueInvoiceCount,
                dsoDays = dsoDays,
                dsoTargetDays = 22,
                collectionEfficiencyPercent = collectionEfficiencyPercent,
                filterType = filterType
            };

            return Json(JsonConvert.SerializeObject(result), JsonRequestBehavior.AllowGet);
        }

        private void GetDateRange(string filterType, string fromDate, string toDate, out DateTime startDate, out DateTime endDate)
        {
            DateTime today = DateTime.Today;
            string selectedFilter = string.IsNullOrEmpty(filterType) ? "ThisMonth" : filterType;

            if (selectedFilter == "LastMonth")
            {
                DateTime thisMonthStart = new DateTime(today.Year, today.Month, 1);
                startDate = thisMonthStart.AddMonths(-1);
                endDate = thisMonthStart;
                return;
            }

            if (selectedFilter == "LastQuarter")
            {
                int currentQuarter = ((today.Month - 1) / 3) + 1;
                DateTime currentQuarterStart = new DateTime(today.Year, ((currentQuarter - 1) * 3) + 1, 1);
                startDate = currentQuarterStart.AddMonths(-3);
                endDate = currentQuarterStart;
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

            startDate = new DateTime(today.Year, today.Month, 1);
            endDate = startDate.AddMonths(1);
        }
    }
}