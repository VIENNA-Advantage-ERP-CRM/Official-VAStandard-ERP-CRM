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
    public class CollectionEfficiencyController : Controller
    {
        /// <summary>
        /// Collection-efficiency KPI tile. Returns four roll-ups for the
        /// trailing 30 days, assembled per PROMPT.md ("Widget 10 — Collection
        /// efficiency"):
        ///   • CollectionEfficiencyPercent — on-time-collected ÷ total-due
        ///   • DsoDays                     — amount-weighted days-to-collect
        ///   • DsoTargetDays               — amount-weighted target days
        ///   • OverdueAmount + count       — all unpaid sales schedules past due
        /// All money is rolled up in the client's accounting-schema (primary)
        /// currency via CurrencyConvert. Day gaps use date_part('epoch', …)/86400
        /// on PostgreSQL (DATE−DATE yields an INTERVAL here, which breaks
        /// ROUND(...,n)) and TRUNC−TRUNC on Oracle. MRole is applied
        /// independently on the primary physical table (C_Invoice) of each
        /// user-data CTE; reference CTEs and the final combined SELECT are not
        /// re-secured, per the project rule for CTE-based queries.
        /// </summary>
        [AjaxAuthorizeAttribute]
        [AjaxSessionFilterAttribute]
        public JsonResult GetCollectionEfficiency(string filterType, string fromDate, string toDate)
        {
            if (Session["ctx"] == null)
            {
                return Json(new
                {
                    error = Msg.GetMsg(Env.GetCtx(), "SessionExpired") ?? "Session Expired"
                }, JsonRequestBehavior.AllowGet);
            }

            Ctx ctx = Session["ctx"] as Ctx;

            int clientId = ctx.GetAD_Client_ID();
            DateTime todayDate = DateTime.Today;
            /* Trailing-30-day window for the Efficiency % and DSO roll-ups
               (the widget always requests Last30Days). */
            DateTime windowStart = todayDate.AddDays(-30);

            /* Reference CTEs (client-scoped, single row each). */
            string primarySchemaSql = @"
                SELECT ClientInfo.AD_Client_ID AS AD_Client_ID,
                       AcctSchema.C_Currency_ID AS Primary_Currency_ID
                FROM AD_ClientInfo ClientInfo
                INNER JOIN C_AcctSchema AcctSchema ON (AcctSchema.C_AcctSchema_ID=ClientInfo.C_AcctSchema1_ID)
                WHERE ClientInfo.AD_Client_ID=" + clientId;

            string schemaCurrencySql = @"
                SELECT ClientInfo.AD_Client_ID AS AD_Client_ID,
                       AcctSchema.C_Currency_ID AS Acct_Currency_ID,
                       Currency.StdPrecision AS StdPrecision,
                       CASE WHEN Currency.CurSymbol IS NOT NULL THEN Currency.CurSymbol ELSE Currency.ISO_Code END AS Cur_Symbol
                FROM AD_ClientInfo ClientInfo
                INNER JOIN C_AcctSchema AcctSchema ON (AcctSchema.C_AcctSchema_ID=ClientInfo.C_AcctSchema1_ID)
                INNER JOIN C_Currency Currency ON (Currency.C_Currency_ID=AcctSchema.C_Currency_ID)
                WHERE ClientInfo.AD_Client_ID=" + clientId;

            /* Efficiency — due schedules in the window (primary fetch → MRole on
               C_Invoice). DueAmt converted to primary currency. */
            string dueSchedulesSql = @"
                SELECT Invoice.C_Invoice_ID AS C_Invoice_ID,
                       InvoicePaySchedule.C_InvoicePaySchedule_ID AS C_InvoicePaySchedule_ID,
                       Invoice.IsReturnTrx AS IsReturnTrx,
                       Invoice.GrandTotal AS GrandTotal,
                       InvoicePaySchedule.DueDate AS DueDate,
                       InvoicePaySchedule.DueAmt AS DueAmt,
                       CASE WHEN Invoice.C_Currency_ID=PrimarySchema.Primary_Currency_ID
                            THEN InvoicePaySchedule.DueAmt
                            ELSE CurrencyConvert(InvoicePaySchedule.DueAmt, Invoice.C_Currency_ID, PrimarySchema.Primary_Currency_ID, COALESCE(Invoice.DateAcct, Invoice.DateInvoiced, " + ToSqlDate(todayDate) + @"), Invoice.C_ConversionType_ID, Invoice.AD_Client_ID, Invoice.AD_Org_ID)
                       END AS DueAmt_Converted
                FROM C_Invoice Invoice
                INNER JOIN C_InvoicePaySchedule InvoicePaySchedule ON (InvoicePaySchedule.C_Invoice_ID=Invoice.C_Invoice_ID)
                INNER JOIN PrimarySchema PrimarySchema ON (PrimarySchema.AD_Client_ID=Invoice.AD_Client_ID)
                WHERE Invoice.IsSOTrx='Y'
                  AND Invoice.IsActive='Y'
                  AND Invoice.DocStatus IN ('CO', 'CL')
                  AND " + TruncColumn("InvoicePaySchedule.DueDate") + @" >= " + ToSqlDate(windowStart) + @"
                  AND " + TruncColumn("InvoicePaySchedule.DueDate") + @" <= " + ToSqlDate(todayDate);

            dueSchedulesSql = MRole.GetDefault(ctx).AddAccessSQL(dueSchedulesSql, "Invoice", MRole.SQL_FULLYQUALIFIED, MRole.SQL_RO);

            /* Allocations per invoice + allocation day (schema currency).
               Implicitly scoped to MRole-allowed invoices through the join to
               the secured DueSchedules CTE downstream — left unsecured here per
               the primary-table-only rule. */
            string invoiceAllocationsSql = @"
                SELECT AllocationLine.C_Invoice_ID AS C_Invoice_ID,
                       " + DateValueSql("AllocationHdr.DateAcct") + @" AS AllocationDate,
                       SUM(CASE WHEN Invoice.C_Currency_ID=PrimarySchema.Primary_Currency_ID
                                THEN AllocationLine.Amount
                                ELSE CurrencyConvert(AllocationLine.Amount, Invoice.C_Currency_ID, PrimarySchema.Primary_Currency_ID, COALESCE(AllocationHdr.DateAcct, Invoice.DateAcct, Invoice.DateInvoiced, " + ToSqlDate(todayDate) + @"), Invoice.C_ConversionType_ID, Invoice.AD_Client_ID, Invoice.AD_Org_ID)
                           END) AS AllocatedAmt_Converted
                FROM C_Invoice Invoice
                INNER JOIN C_InvoicePaySchedule InvoicePaySchedule ON (InvoicePaySchedule.C_Invoice_ID=Invoice.C_Invoice_ID)
                INNER JOIN C_AllocationLine AllocationLine ON (AllocationLine.C_Invoice_ID=Invoice.C_Invoice_ID)
                INNER JOIN C_AllocationHdr AllocationHdr ON (AllocationHdr.C_AllocationHdr_ID=AllocationLine.C_AllocationHdr_ID)
                INNER JOIN PrimarySchema PrimarySchema ON (PrimarySchema.AD_Client_ID=Invoice.AD_Client_ID)
                WHERE AllocationHdr.DocStatus IN ('CO', 'CL')
                  AND Invoice.IsSOTrx='Y'
                  AND Invoice.IsReturnTrx= 'N' 
                  AND (NVL(AllocationLine.C_Payment_ID, 0) != 0 OR NVL(AllocationLine.C_CashLine_ID, 0) != 0)
                  AND Invoice.DocStatus IN ('CO', 'CL')
                  AND " + TruncColumn("AllocationHdr.DateAcct") + @" >= " + ToSqlDate(windowStart) + @"
                  AND " + TruncColumn("AllocationHdr.DateAcct") + @" <= " + ToSqlDate(todayDate) + @"
                GROUP BY AllocationLine.C_Invoice_ID, " + DateValueSql("AllocationHdr.DateAcct");

            /* Receipt invoices — sales invoices with an allocation posted in
               the last 30 days, one row per invoice carrying its representative
               settlement date (the latest allocation DateAcct). Primary fetch →
               MRole on C_Invoice; MRole is applied before GROUP BY is appended
               so the access predicate lands in the WHERE clause. */
            string receiptInvoicesSql = @"
                SELECT Invoice.C_Invoice_ID AS C_Invoice_ID,
                       Invoice.DateInvoiced AS DateInvoiced,
                       (" + DateValueSql("AllocationHdr.DateAcct") + @") AS AllocationDate,
                allocationline.C_InvoicePaySchedule_ID
                FROM C_Invoice Invoice
                INNER JOIN C_AllocationLine AllocationLine ON (AllocationLine.C_Invoice_ID=Invoice.C_Invoice_ID)
                INNER JOIN C_AllocationHdr AllocationHdr ON (AllocationHdr.C_AllocationHdr_ID=AllocationLine.C_AllocationHdr_ID)
                INNER JOIN C_InvoicePaySchedule ci on (ci.C_Invoice_ID = invoice.C_Invoice_ID 
                and ci.C_InvoicePaySchedule_ID = allocationline.C_InvoicePaySchedule_ID)
                WHERE AllocationHdr.DocStatus IN ('CO', 'CL')
                  AND " + DateValueSql("AllocationHdr.DateAcct") + @" >= " + ToSqlDate(windowStart) + @"
                  AND " + DateValueSql("AllocationHdr.DateAcct") + @" <= " + ToSqlDate(todayDate) + @"
                  AND Invoice.IsSOTrx='Y'
                  AND Invoice.IsReturnTrx = 'N' 
                  AND Invoice.IsActive='Y'
                  AND Invoice.DocStatus IN ('CO', 'CL')";

            receiptInvoicesSql = MRole.GetDefault(ctx).AddAccessSQL(receiptInvoicesSql, "Invoice", MRole.SQL_FULLYQUALIFIED, MRole.SQL_RO);

            /* Overdue total + count — all unpaid sales schedules past due,
               converted to schema currency (signed by IsReturnTrx), primary
               fetch → MRole on C_Invoice. */
            string overdueAggSql = @"
                SELECT ROUND(COALESCE(SUM(CASE WHEN Invoice.IsReturnTrx='Y'
                                               THEN -CurrencyConvert(COALESCE(InvoicePaySchedule.DueAmt, 0), Invoice.C_Currency_ID, SchemaCurrency.Acct_Currency_ID, Invoice.DateAcct, Invoice.C_ConversionType_ID, Invoice.AD_Client_ID, Invoice.AD_Org_ID)
                                               ELSE CurrencyConvert(COALESCE(InvoicePaySchedule.DueAmt, 0), Invoice.C_Currency_ID, SchemaCurrency.Acct_Currency_ID, Invoice.DateAcct, Invoice.C_ConversionType_ID, Invoice.AD_Client_ID, Invoice.AD_Org_ID)
                                          END), 0), MAX(SchemaCurrency.StdPrecision)) AS Total_Overdue_Outstanding_Amount,
                       COUNT(DISTINCT Invoice.C_Invoice_ID) AS Overdue_Invoice_Count,
                       MAX(SchemaCurrency.Acct_Currency_ID) AS C_Currency_ID,
                       MAX(SchemaCurrency.Cur_Symbol) AS Cur_Symbol
                FROM C_Invoice Invoice
                INNER JOIN C_InvoicePaySchedule InvoicePaySchedule ON (InvoicePaySchedule.C_Invoice_ID=Invoice.C_Invoice_ID)
                INNER JOIN SchemaCurrency SchemaCurrency ON (SchemaCurrency.AD_Client_ID=Invoice.AD_Client_ID)
                WHERE InvoicePaySchedule.VA009_IsPaid='N'
                  AND Invoice.IsSOTrx='Y'
                  AND Invoice.IsReturnTrx= 'N' 
                  AND Invoice.IsActive='Y'
                  AND Invoice.DocStatus IN ('CO', 'CL')
                  AND " + TruncColumn("InvoicePaySchedule.DueDate") + @" >= " + ToSqlDate(windowStart) + @"
                  AND " + TruncColumn("InvoicePaySchedule.DueDate") + @" <= " + ToSqlDate(todayDate);

            overdueAggSql = MRole.GetDefault(ctx).AddAccessSQL(overdueAggSql, "Invoice", MRole.SQL_FULLYQUALIFIED, MRole.SQL_RO);

            string sql = @"
                WITH PrimarySchema AS (
                    " + primarySchemaSql + @"
                ),
                SchemaCurrency AS (
                    " + schemaCurrencySql + @"
                ),
                DueSchedules AS (
                    " + dueSchedulesSql + @"
                ),
                InvoiceAllocations AS (
                    " + invoiceAllocationsSql + @"
                ),
                ScheduleCollection AS (
                    SELECT DueSchedules.C_Invoice_ID AS C_Invoice_ID,
                           CASE WHEN DueSchedules.IsReturnTrx='Y' THEN -1 ELSE 1 END * DueSchedules.DueAmt_Converted AS TotalDueAmount,
                           SUM(CASE WHEN InvoiceAllocations.AllocationDate <= " + DateValueSql("DueSchedules.DueDate") + @"
                                    THEN CASE WHEN DueSchedules.GrandTotal=0 THEN 0
                                              ELSE InvoiceAllocations.AllocatedAmt_Converted * DueSchedules.DueAmt / DueSchedules.GrandTotal
                                         END
                                    ELSE 0
                               END) AS OnTimeCollectedAmount
                    FROM InvoiceAllocations InvoiceAllocations
                    LEFT OUTER JOIN DueSchedules DueSchedules ON (InvoiceAllocations.C_Invoice_ID=DueSchedules.C_Invoice_ID)
                    GROUP BY DueSchedules.C_Invoice_ID, DueSchedules.C_InvoicePaySchedule_ID, DueSchedules.IsReturnTrx, DueSchedules.DueAmt, DueSchedules.DueAmt_Converted, DueSchedules.GrandTotal
                ),
                EfficiencyCalc AS (
                    SELECT ROUND(SUM(ScheduleCollection.OnTimeCollectedAmount) * 100 / NULLIF(SUM(ScheduleCollection.TotalDueAmount), 0), 2) AS CollectionEfficiencyPercent
                    FROM ScheduleCollection ScheduleCollection
                ),
                ReceiptInvoices AS (
                    " + receiptInvoicesSql + @"
                ),
                ReceiptSchedules AS (
                    SELECT ReceiptInvoices.C_Invoice_ID AS C_Invoice_ID,
                           ReceiptInvoices.DateInvoiced AS DateInvoiced,
                           ReceiptInvoices.AllocationDate AS AllocationDate,
                           InvoicePaySchedule.DueDate AS DueDate,
                           InvoicePaySchedule.DueAmt AS DueAmt
                    FROM ReceiptInvoices ReceiptInvoices
                    INNER JOIN C_InvoicePaySchedule InvoicePaySchedule ON (InvoicePaySchedule.C_Invoice_ID=ReceiptInvoices.C_Invoice_ID 
                                                        AND InvoicePaySchedule.C_InvoicePaySchedule_ID = ReceiptInvoices.C_InvoicePaySchedule_ID)
                    WHERE InvoicePaySchedule.IsActive='Y'
                ),
                DsoCalc AS (
                    SELECT ROUND(SUM(ReceiptSchedules.DueAmt * (" + DayDiffSql("ReceiptSchedules.AllocationDate", "ReceiptSchedules.DateInvoiced") + @")) / NULLIF(SUM(ReceiptSchedules.DueAmt), 0), 0) AS DsoDays
                    FROM ReceiptSchedules ReceiptSchedules
                ),
                TargetCalc AS (
                    SELECT ROUND(SUM(ReceiptSchedules.DueAmt * (" + DayDiffSql("ReceiptSchedules.DueDate", "ReceiptSchedules.DateInvoiced") + @")) / NULLIF(SUM(ReceiptSchedules.DueAmt), 0), 0) AS DsoTargetDays
                    FROM ReceiptSchedules ReceiptSchedules
                ),
                OverdueAgg AS (
                    " + overdueAggSql + @"
                )
                SELECT OverdueAgg.C_Currency_ID AS C_Currency_ID,
                       OverdueAgg.Cur_Symbol AS CurSymbol,
                       COALESCE(OverdueAgg.Total_Overdue_Outstanding_Amount, 0) AS OverdueAmount,
                       COALESCE(OverdueAgg.Overdue_Invoice_Count, 0) AS OverdueInvoiceCount,
                       COALESCE(DsoCalc.DsoDays, 0) AS DsoDays,
                       COALESCE(TargetCalc.DsoTargetDays, 0) AS DsoTargetDays,
                       CASE WHEN EfficiencyCalc.CollectionEfficiencyPercent IS NULL THEN 0
                            ELSE GREATEST(0, LEAST(100, EfficiencyCalc.CollectionEfficiencyPercent))
                       END AS CollectionEfficiencyPercent
                FROM EfficiencyCalc
                CROSS JOIN DsoCalc
                CROSS JOIN TargetCalc
                CROSS JOIN OverdueAgg";

            int currencyId = 0;
            string curSymbol = "";
            decimal overdueAmount = 0;
            int overdueInvoiceCount = 0;
            int dsoDays = 0;
            int dsoTargetDays = 0;
            decimal collectionEfficiencyPercent = 0;

            IDataReader dr = null;

            try
            {
                dr = DB.ExecuteReader(sql);

                if (dr != null && dr.Read())
                {
                    currencyId = Util.GetValueOfInt(dr["C_Currency_ID"]);
                    curSymbol = Util.GetValueOfString(dr["CurSymbol"]);
                    overdueAmount = Util.GetValueOfDecimal(dr["OverdueAmount"]);
                    overdueInvoiceCount = Util.GetValueOfInt(dr["OverdueInvoiceCount"]);
                    dsoDays = Util.GetValueOfInt(dr["DsoDays"]);
                    dsoTargetDays = Util.GetValueOfInt(dr["DsoTargetDays"]);
                    collectionEfficiencyPercent = Util.GetValueOfDecimal(dr["CollectionEfficiencyPercent"]);
                }
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

            /* Collection status bucket — drives the donut tint chip in the
               UI. Matches the PROMPT.md mapping: >=90 ON-TIME, >=75 WATCH,
               below WATCH = DELAYED. */
            string collectionStatus = "DELAYED";
            if (collectionEfficiencyPercent >= 90) { collectionStatus = "ON-TIME"; }
            else if (collectionEfficiencyPercent >= 75) { collectionStatus = "WATCH"; }

            var result = new
            {
                cCurrencyId = currencyId,
                curSymbol = curSymbol,
                overdueAmount = overdueAmount,
                overdueInvoiceCount = overdueInvoiceCount,
                dsoDays = dsoDays,
                dsoTargetDays = dsoTargetDays,
                collectionEfficiencyPercent = collectionEfficiencyPercent,
                collectionStatus = collectionStatus,
                filterType = filterType
            };

            return Json(JsonConvert.SerializeObject(result), JsonRequestBehavior.AllowGet);
        }

        /// <summary>
        /// Returns one page of overdue invoice aging rows for the drill-down
        /// modal. Rows are unpaid sales schedules due in the trailing 30 days
        /// (VA009_IsPaid &lt;&gt; 'Y', today-30 &lt;= DueDate &lt; today),
        /// matching the main widget window. Each row's amount stays in the
        /// invoice's OWN currency — there
        /// is no base/schema-currency conversion. The query is dialect-portable:
        /// day-diff (age) uses TRUNC(date)-TRUNC(date) on Oracle and
        /// date_part('epoch', ...)/86400 on PostgreSQL — DateAcct/DueDate behave
        /// as timestamps in this PostgreSQL deployment, so DATE-DATE would yield
        /// an INTERVAL. MRole is applied only on the main physical table
        /// (C_Invoice) inside the AgingRows CTE — the outer combined query and
        /// the Totals CTE are not re-secured per project rule for CTE-based
        /// queries.
        /// </summary>
        /// <param name="pageNo">1-based page number.</param>
        /// <param name="pageSize">Rows per page (default 10 — fills the modal).</param>
        /// <returns>JSON { rows[], pageNo, pageSize, totalRecords, totalPages }.</returns>
        [AjaxAuthorizeAttribute]
        [AjaxSessionFilterAttribute]
        public JsonResult GetOverdueAgingRows(int pageNo = 1, int pageSize = 10)
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
            int offset = (pageNo - 1) * pageSize;
            DateTime today = DateTime.Today;
            /* Trailing-30-day window — the modal shows only the last 30 days
               of overdue schedules, matching the main widget. */
            DateTime windowStart = today.AddDays(-30);

            /* Per-row aging tuple, kept in the invoice's OWN currency (no
               base/schema-currency conversion). MRole on Invoice (the main
               physical table of the user's drill-down); InvoicePaySchedule,
               BPartner and Currency are clean lookup joins. Unpaid sales
               schedules due within the last 30 days (windowStart &lt;= DueDate
               &lt; today) are returned. */
            string agingRowsSql = @"
                SELECT Invoice.C_Invoice_ID AS Invoice_ID,
                       InvoicePaySchedule.C_InvoicePaySchedule_ID AS Schedule_ID,
                       Invoice.DocumentNo AS Document_No,
                       BPartner.Name AS Customer_Name,
                       InvoicePaySchedule.DueDate AS Due_Date,
                       " + OverdueAgeDaysSql(today) + @" AS Age_Days,
                       Currency.ISO_Code AS Currency_ISO,
                       CASE WHEN Currency.CurSymbol IS NOT NULL THEN Currency.CurSymbol ELSE Currency.ISO_Code END AS Cur_Symbol,
                       Currency.StdPrecision AS Std_Precision,
                       CASE WHEN Invoice.IsReturnTrx = 'Y'
                            THEN -COALESCE(InvoicePaySchedule.DueAmt, 0)
                            ELSE COALESCE(InvoicePaySchedule.DueAmt, 0)
                       END AS Due_Amount
                FROM C_Invoice Invoice
                INNER JOIN C_InvoicePaySchedule InvoicePaySchedule ON (InvoicePaySchedule.C_Invoice_ID=Invoice.C_Invoice_ID)
                INNER JOIN C_BPartner BPartner ON (BPartner.C_BPartner_ID=Invoice.C_BPartner_ID)
                INNER JOIN C_Currency Currency ON (Currency.C_Currency_ID=Invoice.C_Currency_ID)
                WHERE Invoice.IsSoTrx = 'Y'
                  AND Invoice.IsActive = 'Y'
                  AND Invoice.DocStatus IN ('CO', 'CL')
                  AND InvoicePaySchedule.IsActive = 'Y'
                  AND COALESCE(InvoicePaySchedule.VA009_IsPaid, 'N') <> 'Y'
                  AND " + TruncColumn("InvoicePaySchedule.DueDate") + @" >= " + ToSqlDate(windowStart) + @"
                  AND " + TruncColumn("InvoicePaySchedule.DueDate") + @" < " + ToSqlDate(today);

            agingRowsSql = MRole.GetDefault(ctx).AddAccessSQL(
                agingRowsSql,
                "Invoice",
                MRole.SQL_FULLYQUALIFIED,
                MRole.SQL_RO
            );

            /* Total record count is computed once from the secured AgingRows
               CTE and CROSS-joined to every page row so a single round-trip
               delivers the page + accurate totalRecords for the pager. Per
               project rule the outer combined query is NOT re-secured. */
            string sql = @"
                WITH AgingRows AS (
                    " + agingRowsSql + @"
                ),
                Totals AS (
                    SELECT COUNT(1) AS Total_Records
                    FROM AgingRows
                )
                SELECT AgingRows.Invoice_ID,
                       AgingRows.Schedule_ID,
                       AgingRows.Document_No,
                       AgingRows.Customer_Name,
                       AgingRows.Due_Date,
                       AgingRows.Age_Days,
                       AgingRows.Currency_ISO AS Row_Currency_ISO,
                       AgingRows.Cur_Symbol AS Row_Cur_Symbol,
                       AgingRows.Std_Precision AS Row_Std_Precision,
                       AgingRows.Due_Amount,
                       Totals.Total_Records
                FROM AgingRows
                CROSS JOIN Totals
                ORDER BY AgingRows.Due_Date DESC, AgingRows.Document_No
                OFFSET @Offset ROWS FETCH NEXT @PageSize ROWS ONLY";

            List<SqlParameter> parameters = new List<SqlParameter>();
            parameters.Add(new SqlParameter("@Offset", offset));
            parameters.Add(new SqlParameter("@PageSize", pageSize));

            List<object> rows = new List<object>();
            int totalRecords = 0;

            IDataReader dr = null;
            try
            {
                dr = DB.ExecuteReader(sql, parameters.ToArray());
                while (dr != null && dr.Read())
                {
                    totalRecords = Util.GetValueOfInt(dr["Total_Records"]);

                    DateTime? dueDate = Util.GetValueOfDateTime(dr["Due_Date"]);
                    int rowPrecision = Util.GetValueOfInt(dr["Row_Std_Precision"]);

                    decimal dueAmount = Math.Round(
                        Util.GetValueOfDecimal(dr["Due_Amount"]),
                        rowPrecision,
                        MidpointRounding.AwayFromZero
                    );

                    rows.Add(new
                    {
                        invoiceId = Util.GetValueOfInt(dr["Invoice_ID"]),
                        scheduleId = Util.GetValueOfInt(dr["Schedule_ID"]),
                        documentNo = Util.GetValueOfString(dr["Document_No"]),
                        customer = Util.GetValueOfString(dr["Customer_Name"]),
                        dueDate = dueDate.HasValue
                            ? dueDate.Value.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture)
                            : "",
                        ageDays = Util.GetValueOfInt(dr["Age_Days"]),
                        currencyIso = Util.GetValueOfString(dr["Row_Currency_ISO"]),
                        curSymbol = Util.GetValueOfString(dr["Row_Cur_Symbol"]),
                        stdPrecision = rowPrecision,
                        dueAmount = dueAmount
                    });
                }
            }
            catch (Exception ex)
            {
                return Json(new { error = ex.Message }, JsonRequestBehavior.AllowGet);
            }
            finally
            {
                if (dr != null) { dr.Close(); dr.Dispose(); }
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

        /// <summary>
        /// Day-diff (today − InvoicePaySchedule.DueDate) expressed
        /// portably. On Oracle TRUNC subtraction yields whole days. On
        /// PostgreSQL DateAcct/DueDate behave as timestamps in this
        /// deployment, so we convert (timestamp − timestamp) → seconds →
        /// days via date_part('epoch', …) / 86400 and cast to NUMERIC so
        /// the result compares cleanly downstream.
        /// </summary>
        internal static string OverdueAgeDaysSql(DateTime today)
        {
            if (DB.IsPostgreSQL())
            {
                return "CAST(date_part('epoch', (CAST(" + ToSqlDate(today) + " AS TIMESTAMP) - CAST(InvoicePaySchedule.DueDate AS TIMESTAMP))) / 86400 AS NUMERIC)";
            }
            return "(TRUNC(" + ToSqlDate(today) + ") - TRUNC(InvoicePaySchedule.DueDate))";
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

        /// <summary>
        /// Returns a whole-day DATE value for a (possibly timestamp) column:
        /// CAST(col AS DATE) on PostgreSQL, TRUNC(col) on Oracle. Use this when
        /// grouping by day or comparing two day values — never subtract two of
        /// these directly on PostgreSQL (DATE − DATE yields an INTERVAL here);
        /// use <see cref="DayDiffSql"/> for day gaps.
        /// </summary>
        internal static string DateValueSql(string columnExpression)
        {
            if (DB.IsOracle())
            {
                return "TRUNC(" + columnExpression + ")";
            }

            return "CAST(" + columnExpression + " AS DATE)";
        }

        /// <summary>
        /// Whole-day gap (later − earlier) as a numeric day count, portable
        /// across dialects. On Oracle TRUNC subtraction yields whole days. On
        /// PostgreSQL date columns behave as timestamps, so DATE − DATE returns
        /// an INTERVAL — feeding that into ROUND(...,n) raises
        /// "function round(interval, integer) does not exist". Both sides are
        /// normalised to midnight and the difference taken via
        /// date_part('epoch', …) / 86400, cast to NUMERIC, so the surrounding
        /// SUM()/ROUND() operate on a numeric day value.
        /// </summary>
        /// <param name="laterExpression">the later date/timestamp expression.</param>
        /// <param name="earlierExpression">the earlier date/timestamp expression.</param>
        /// <returns>SQL fragment evaluating to a numeric day count.</returns>
        internal static string DayDiffSql(string laterExpression, string earlierExpression)
        {
            if (DB.IsOracle())
            {
                return "(TRUNC(" + laterExpression + ") - TRUNC(" + earlierExpression + "))";
            }

            return "CAST(date_part('epoch', (CAST(CAST(" + laterExpression + " AS DATE) AS TIMESTAMP) - CAST(CAST(" + earlierExpression + " AS DATE) AS TIMESTAMP))) / 86400 AS NUMERIC)";
        }
    }
}
