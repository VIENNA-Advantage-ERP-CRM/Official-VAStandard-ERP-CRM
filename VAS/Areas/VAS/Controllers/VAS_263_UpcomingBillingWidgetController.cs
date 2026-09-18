/******************************************************
 * Module Name    : VAS
 * Purpose        : Service Contracts module Upcoming billing centerpiece widget endpoints
 * chronological  : Development
 * Created Date   : 2026-09-08
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
    /// Module Name : VAS_263_UpcomingBillingWidget
    /// Purpose     : Data endpoints for the c5 x r3 centerpiece "Upcoming
    ///               billing" list widget on the Service Contracts dashboard:
    ///               (1) the header banner - the COUNT of uninvoiced billing-
    ///               schedule periods (C_ContractSchedule with C_Invoice_ID IS
    ///               NULL) of live contracts due in the overdue-25d..due-60d
    ///               window, plus their combined base-currency total, (2) the
    ///               paged (@7, most-overdue first) rows the widget body always
    ///               shows (also reused, with a larger page, for the header
    ///               "All" list modal), and (3) every matching Contract_ID
    ///               (capped) so "Open in browser" can filter the Service
    ///               Contract window down to exactly this population via a
    ///               TabWhereClause IN-list. The DRIVING CHILD is
    ///               C_ContractSchedule ("cs"), joined to its live parent
    ///               C_Contract ("co") - live = Processed='Y' AND IsCancel='N'
    ///               AND EndDate&gt;=CURRENT_DATE (not DocStatus=MContract.
    ///               DOCSTATUS_Completed - the Service Contract window itself
    ///               does not rely on that column, verified against real data,
    ///               the same fix already applied to VAS_243/244/245/246/258/
    ///               259/260/261/262 - applied here proactively on first build
    ///               rather than waiting for a follow-up correction). Per
    ///               upcoming-billing.queries.md's MRole anchoring note, MRole
    ///               is applied to the C_Contract alias "co" ONLY, even though
    ///               "cs" is the driving child in the FROM clause - a schedule
    ///               period is visible only through its parent contract.
    ///               Frequency is decoded from C_Frequency.Name (a plain join,
    ///               not an AD_Ref_List code). A row's detail (its Renew /
    ///               Generate invoice actions) reuses the already-built
    ///               VAS_241_ContractSearchWidget/GetContract, RunRenew and
    ///               RunGenerateInvoice endpoints rather than duplicating that
    ///               logic - the same shared-endpoint reuse VAS_120's Log-
    ///               activity quick action already established against
    ///               VAS_126, and VAS_244/245/246/258/259/261/262 already
    ///               reused too; the per-row "Invoice" button posts to that
    ///               same RunGenerateInvoice(contractId) - the underlying
    ///               CreateContractInvoice process already picks up whichever
    ///               period(s) are due for that contract, so no separate
    ///               schedule-scoped write path is needed. Read-only here; no
    ///               writes.
    /// Chronological development:
    ///   VAI052      2026-09-08 Created
    /// </summary>
    public class VAS_263_UpcomingBillingWidgetController : Controller
    {
        private static readonly VLogger Log = VLogger.GetVLogger(typeof(VAS_263_UpcomingBillingWidgetController).FullName);

        // The product rule for this widget (upcoming-billing.prompt.md §4 / queries.md §A.7/C) -
        // overdue up to 25 days, due within 60.
        private const int WindowFromDays = -25;
        private const int WindowToDays = 60;

        private const int DrillPageSize = 7;

        // Upper bound for the "Open in browser" TabWhereClause IN-list (GetContractIdsData) -
        // this widget's live population is naturally small; this just bounds a pathological case.
        private const int MaxZoomIds = 1000;

        /// <summary>
        /// Header banner scalars: uninvoiced-and-in-window period count plus the
        /// combined value in the tenant's base (accounting-schema) currency.
        /// </summary>
        /// <returns>JSON { DuePeriodCount, DueAmountBase, CurrencyIso, CurrencySymbol, CurrencyPrecision } or { Error }.</returns>
        [AjaxAuthorizeAttribute]
        [AjaxSessionFilterAttribute]
        public JsonResult GetUpcomingBillingSummary()
        {
            if (Session["ctx"] == null) { return Json("", JsonRequestBehavior.AllowGet); }

            Ctx ctx = Session["ctx"] as Ctx;

            try
            {
                string json = JsonConvert.SerializeObject(GetSummaryData(ctx));
                return Json(json, JsonRequestBehavior.AllowGet);
            }
            catch (Exception ex)
            {
                Log.Log(Level.SEVERE, "VAS_263_UpcomingBillingWidget.GetUpcomingBillingSummary", ex);
                return ErrorResult(ctx);
            }
        }

        /// <summary>
        /// Paged rows, most-overdue first - the identical query backs both the
        /// widget's own always-visible @7 body and (with a larger "limit") the
        /// header "All" list modal.
        /// </summary>
        /// <param name="offset">Zero-based paging offset.</param>
        /// <param name="limit">Page size - 7 for the widget body, larger for the "All" list modal (capped at <see cref="MaxZoomIds"/>).</param>
        /// <returns>JSON { Rows:[...], Total } or { Error }.</returns>
        [AjaxAuthorizeAttribute]
        [AjaxSessionFilterAttribute]
        public JsonResult GetUpcomingBillingContracts(int offset = 0, int limit = DrillPageSize)
        {
            if (Session["ctx"] == null) { return Json("", JsonRequestBehavior.AllowGet); }

            Ctx ctx = Session["ctx"] as Ctx;

            if (offset < 0) { offset = 0; }
            if (limit <= 0 || limit > MaxZoomIds) { limit = DrillPageSize; }

            try
            {
                string json = JsonConvert.SerializeObject(GetDrillData(ctx, offset, limit));
                return Json(json, JsonRequestBehavior.AllowGet);
            }
            catch (Exception ex)
            {
                Log.Log(Level.SEVERE, "VAS_263_UpcomingBillingWidget.GetUpcomingBillingContracts", ex);
                return ErrorResult(ctx);
            }
        }

        /// <summary>
        /// Every matching C_Contract_ID for the upcoming-billing predicate (not
        /// just one page - one row per due schedule period, so a contract with
        /// two due periods appears twice, matching the drill list), capped at
        /// <see cref="MaxZoomIds"/> - so "Open in browser" can filter the
        /// Service Contract window down to exactly this widget's population via
        /// a TabWhereClause IN-list.
        /// </summary>
        /// <returns>JSON { Ids:[...] } or { Error }.</returns>
        [AjaxAuthorizeAttribute]
        [AjaxSessionFilterAttribute]
        public JsonResult GetUpcomingBillingContractIds()
        {
            if (Session["ctx"] == null) { return Json("", JsonRequestBehavior.AllowGet); }

            Ctx ctx = Session["ctx"] as Ctx;

            try
            {
                string json = JsonConvert.SerializeObject(new { Ids = GetContractIdsData(ctx) });
                return Json(json, JsonRequestBehavior.AllowGet);
            }
            catch (Exception ex)
            {
                Log.Log(Level.SEVERE, "VAS_263_UpcomingBillingWidget.GetUpcomingBillingContractIds", ex);
                return ErrorResult(ctx);
            }
        }

        /// <summary>
        /// The shared uninvoiced-and-in-window predicate, WHERE-only - every
        /// caller applies MRole to the C_Contract alias "co" itself right after
        /// calling this (upcoming-billing.queries.md A.2's anchoring note - "cs"
        /// is the driving child but is never the MRole alias). "Live" is
        /// Processed='Y' (not DocStatus=MContract.DOCSTATUS_Completed - the
        /// Service Contract window itself does not rely on that column, verified
        /// against real data, the same fix already applied to VAS_243/244/245/
        /// 246/258/259/260/261/262) AND IsCancel = 'N' AND EndDate &gt;=
        /// CURRENT_DATE; the schedule period must be uninvoiced
        /// (C_Invoice_ID IS NULL) and its FROMDATE within
        /// [@WindowFrom, @WindowTo] days of today.
        /// </summary>
        /// <returns>WHERE-clause text (binds @AD_Client_ID, @WindowFrom, @WindowTo).</returns>
        private static string DueWhereSql()
        {
            return @"
                   co.AD_Client_ID = @AD_Client_ID
               AND co.IsActive = 'Y'
               AND cs.IsActive = 'Y'
               AND co.Processed = 'Y'
               AND co.IsCancel = 'N'
               AND co.EndDate >= CURRENT_DATE
               AND cs.C_Invoice_ID IS NULL
               AND DAYSBETWEEN(cs.FROMDATE, CURRENT_DATE) BETWEEN @WindowFrom AND @WindowTo";
        }

        /// <summary>
        /// Fresh parameter array for one command execution against
        /// <see cref="DueWhereSql"/> - every placeholder occurs exactly once in
        /// each assembled statement, so no name repeats under Oracle's
        /// positional binding.
        /// </summary>
        /// <param name="ctx">Session context.</param>
        private SqlParameter[] BuildPredicateParameters(Ctx ctx)
        {
            return new[]
            {
                new SqlParameter("@AD_Client_ID", ctx.GetAD_Client_ID()),
                new SqlParameter("@WindowFrom", WindowFromDays),
                new SqlParameter("@WindowTo", WindowToDays)
            };
        }

        /// <summary>
        /// Resolves the header banner: uninvoiced-and-in-window period count plus
        /// the base-currency value at stake. The schedule amount inherits the
        /// PARENT contract's currency/conversion - CurrencyConvert against "co",
        /// never raw cs.TotalAmt.
        /// </summary>
        /// <param name="ctx">Session context.</param>
        private BillingSummary GetSummaryData(Ctx ctx)
        {
            BillingSummary result = new BillingSummary();
            if (ctx == null) { return result; }

            int baseCurrencyId; string iso, symbol; int precision;
            ResolveBaseCurrency(ctx, out baseCurrencyId, out iso, out symbol, out precision);
            result.CurrencyIso = iso;
            result.CurrencySymbol = symbol;
            result.CurrencyPrecision = precision;

            string sql = @"
                SELECT COUNT(cs.C_ContractSchedule_ID) AS Due_Period_Count,
                       SUM( CurrencyConvert(cs.TotalAmt, co.C_Currency_ID, @BaseCurrency_ID,
                              CURRENT_DATE, co.C_ConversionType_ID, co.AD_Client_ID, co.AD_Org_ID) ) AS Due_Amount_Base
                  FROM C_Contract co
                  INNER JOIN C_ContractSchedule cs ON ( cs.C_Contract_ID = co.C_Contract_ID )
                 WHERE " + DueWhereSql();

            sql = MRole.GetDefault(ctx).AddAccessSQL(sql, "co", MRole.SQL_FULLYQUALIFIED, MRole.SQL_RO);

            IDataReader dr = null;
            try
            {
                List<SqlParameter> parameters = new List<SqlParameter>(BuildPredicateParameters(ctx));
                parameters.Add(new SqlParameter("@BaseCurrency_ID", baseCurrencyId));

                dr = DB.ExecuteReader(sql, parameters.ToArray());
                if (dr != null && dr.Read())
                {
                    result.DuePeriodCount = Util.GetValueOfInt(dr["Due_Period_Count"]);
                    result.DueAmountBase = dr["Due_Amount_Base"] == DBNull.Value ? 0m : Util.GetValueOfDecimal(dr["Due_Amount_Base"]);
                }
            }
            finally
            {
                CloseReader(dr);
            }

            return result;
        }

        /// <summary>
        /// Paged due-billing rows, most-overdue first (upcoming-billing.queries.md
        /// D-2) - the base SQL is WHERE-only until MRole is applied to alias
        /// "co", then ORDER BY / OFFSET..FETCH are appended.
        /// </summary>
        /// <param name="ctx">Session context.</param>
        /// <param name="offset">Zero-based paging offset.</param>
        /// <param name="limit">Page size.</param>
        private BillingDrillResult GetDrillData(Ctx ctx, int offset, int limit)
        {
            BillingDrillResult result = new BillingDrillResult { Rows = new List<BillingDrillRow>() };
            if (ctx == null) { return result; }

            int baseCurrencyId; string baseIso, baseSymbol; int basePrecision;
            ResolveBaseCurrency(ctx, out baseCurrencyId, out baseIso, out baseSymbol, out basePrecision);

            string baseSql = @"
                SELECT co.C_Contract_ID         AS Contract_Id,
                       cs.C_ContractSchedule_ID AS Schedule_Id,
                       co.DocumentNo            AS Document_No,
                       COALESCE(bp.Name, N'') AS Customer_Name,
                       COALESCE(fr.Name, N'') AS Frequency_Label,
                       cs.FROMDATE              AS From_Date,
                       CAST(DAYSBETWEEN(cs.FROMDATE, CURRENT_DATE) AS INTEGER) AS Days_To_Invoice,
                       CurrencyConvert(cs.TotalAmt, co.C_Currency_ID, @BaseCurrency_ID,
                              CURRENT_DATE, co.C_ConversionType_ID, co.AD_Client_ID, co.AD_Org_ID) AS Next_Invoice_Base
                  FROM C_Contract co
                  INNER JOIN C_ContractSchedule cs ON ( cs.C_Contract_ID = co.C_Contract_ID )
                  LEFT OUTER JOIN C_BPartner  bp ON ( bp.C_BPartner_ID = co.C_BPartner_ID )
                  LEFT OUTER JOIN C_Frequency fr ON ( fr.C_Frequency_ID = co.C_Frequency_ID )
                 WHERE " + DueWhereSql();

            baseSql = MRole.GetDefault(ctx).AddAccessSQL(baseSql, "co", MRole.SQL_FULLYQUALIFIED, MRole.SQL_RO);

            string listSql = baseSql + @"
                ORDER BY cs.FROMDATE ASC
                OFFSET @Off ROWS FETCH NEXT @Lim ROWS ONLY";

            string countSql = "SELECT COUNT(1) AS Total_Count FROM ( " + baseSql + " ) cnt_wrap";

            IDataReader dr = null;
            try
            {
                List<SqlParameter> parameters = new List<SqlParameter>(BuildPredicateParameters(ctx));
                parameters.Add(new SqlParameter("@BaseCurrency_ID", baseCurrencyId));
                parameters.Add(new SqlParameter("@Off", offset));
                parameters.Add(new SqlParameter("@Lim", limit));

                dr = DB.ExecuteReader(listSql, parameters.ToArray());
                while (dr != null && dr.Read())
                {
                    DateTime? fromDate = dr["From_Date"] == DBNull.Value ? (DateTime?)null : Util.GetValueOfDateTime(dr["From_Date"]);

                    result.Rows.Add(new BillingDrillRow
                    {
                        ContractId = Util.GetValueOfInt(dr["Contract_Id"]),
                        ScheduleId = Util.GetValueOfInt(dr["Schedule_Id"]),
                        DocumentNo = Util.GetValueOfString(dr["Document_No"]),
                        CustomerName = Util.GetValueOfString(dr["Customer_Name"]),
                        FrequencyLabel = Util.GetValueOfString(dr["Frequency_Label"]),
                        FromDate = fromDate.HasValue ? fromDate.Value.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture) : "",
                        DaysToInvoice = dr["Days_To_Invoice"] == DBNull.Value ? 0 : Util.GetValueOfInt(dr["Days_To_Invoice"]),
                        NextInvoiceBase = dr["Next_Invoice_Base"] == DBNull.Value ? 0m : Util.GetValueOfDecimal(dr["Next_Invoice_Base"]),
                        CurrencyIso = baseIso,
                        CurrencySymbol = baseSymbol,
                        CurrencyPrecision = basePrecision
                    });
                }
            }
            finally
            {
                CloseReader(dr);
            }

            IDataReader countReader = null;
            try
            {
                List<SqlParameter> countParameters = new List<SqlParameter>(BuildPredicateParameters(ctx));
                countParameters.Add(new SqlParameter("@BaseCurrency_ID", baseCurrencyId));

                countReader = DB.ExecuteReader(countSql, countParameters.ToArray());
                if (countReader != null && countReader.Read())
                {
                    result.Total = Util.GetValueOfInt(countReader["Total_Count"]);
                }
            }
            finally
            {
                CloseReader(countReader);
            }

            return result;
        }

        /// <summary>
        /// Every C_Contract_ID matching the upcoming-billing predicate (one entry
        /// per due schedule period, so a contract can repeat), most-overdue
        /// first, capped at <see cref="MaxZoomIds"/>. Backs "Open in browser"'s
        /// TabWhereClause IN-list - see <see cref="GetUpcomingBillingContractIds"/>.
        /// </summary>
        /// <param name="ctx">Session context.</param>
        private List<int> GetContractIdsData(Ctx ctx)
        {
            List<int> ids = new List<int>();
            if (ctx == null) { return ids; }

            string sql = @"
                SELECT co.C_Contract_ID AS Contract_Id
                  FROM C_Contract co
                  INNER JOIN C_ContractSchedule cs ON ( cs.C_Contract_ID = co.C_Contract_ID )
                 WHERE " + DueWhereSql();

            sql = MRole.GetDefault(ctx).AddAccessSQL(sql, "co", MRole.SQL_FULLYQUALIFIED, MRole.SQL_RO);

            sql = sql + @"
                ORDER BY cs.FROMDATE ASC
                OFFSET 0 ROWS FETCH NEXT @MaxIds ROWS ONLY";

            IDataReader dr = null;
            try
            {
                List<SqlParameter> parameters = new List<SqlParameter>(BuildPredicateParameters(ctx));
                parameters.Add(new SqlParameter("@MaxIds", MaxZoomIds));

                dr = DB.ExecuteReader(sql, parameters.ToArray());
                while (dr != null && dr.Read())
                {
                    ids.Add(Util.GetValueOfInt(dr["Contract_Id"]));
                }
            }
            finally
            {
                CloseReader(dr);
            }

            return ids;
        }

        /// <summary>
        /// The tenant's accounting-schema (base) currency - AD_ClientInfo -&gt;
        /// C_AcctSchema1 -&gt; C_Currency, the same resolution the sibling Service
        /// Contracts KPIs (VAS_243/244/245/246/258/259/260/261/262) already use.
        /// </summary>
        /// <returns>Parameterised SELECT text (binds @AD_Client_ID).</returns>
        private static string SchemaCurrencySql()
        {
            return @"
                SELECT cs.C_Currency_ID AS Acct_Currency_ID,
                       cur.StdPrecision AS Std_Precision,
                       cur.ISO_Code     AS ISO_Code,
                       CASE WHEN cur.CurSymbol IS NOT NULL THEN cur.CurSymbol ELSE cur.ISO_Code END AS Cur_Symbol
                  FROM AD_ClientInfo ci
                  INNER JOIN C_AcctSchema cs ON ( cs.C_AcctSchema_ID = ci.C_AcctSchema1_ID AND cs.IsActive = 'Y' )
                  INNER JOIN C_Currency cur  ON ( cur.C_Currency_ID  = cs.C_Currency_ID    AND cur.IsActive = 'Y' )
                 WHERE ci.IsActive = 'Y'
                   AND ci.AD_Client_ID = @AD_Client_ID";
        }

        /// <summary>Resolves the base currency id/iso/symbol/precision for this tenant, or a zeroed default when the schema currency cannot be resolved.</summary>
        /// <param name="ctx">Session context.</param>
        /// <param name="baseCurrencyId">Resolved C_Currency_ID of the tenant's accounting schema.</param>
        /// <param name="iso">Resolved ISO code.</param>
        /// <param name="symbol">Resolved display symbol (CurSymbol, falling back to ISO code).</param>
        /// <param name="precision">Resolved standard display precision.</param>
        private void ResolveBaseCurrency(Ctx ctx, out int baseCurrencyId, out string iso, out string symbol, out int precision)
        {
            baseCurrencyId = 0;
            iso = "";
            symbol = "";
            precision = 2;

            IDataReader dr = null;
            try
            {
                dr = DB.ExecuteReader(SchemaCurrencySql(), new[] { new SqlParameter("@AD_Client_ID", ctx.GetAD_Client_ID()) });
                if (dr != null && dr.Read())
                {
                    baseCurrencyId = Util.GetValueOfInt(dr["Acct_Currency_ID"]);
                    iso = Util.GetValueOfString(dr["ISO_Code"]);
                    symbol = Util.GetValueOfString(dr["Cur_Symbol"]);
                    precision = dr["Std_Precision"] == DBNull.Value ? 2 : Util.GetValueOfInt(dr["Std_Precision"]);
                }
            }
            finally
            {
                CloseReader(dr);
            }
        }

        /// <summary>Closes and disposes a DataReader if open - every reader opened by this controller passes through here so no DB connection leaks.</summary>
        /// <param name="reader">Reader to close, or null.</param>
        private void CloseReader(IDataReader reader)
        {
            if (reader == null) { return; }
            reader.Close();
            reader.Dispose();
        }

        /// <summary>Builds the tenant-localised generic error JSON payload for a failed endpoint call.</summary>
        /// <param name="ctx">Session context (for message localisation).</param>
        private JsonResult ErrorResult(Ctx ctx)
        {
            string message = Msg.GetMsg(ctx, "Error") ?? "Error";
            string json = JsonConvert.SerializeObject(new { Error = message });
            return Json(json, JsonRequestBehavior.AllowGet);
        }

        private class BillingSummary
        {
            public int DuePeriodCount { get; set; }
            public decimal DueAmountBase { get; set; }
            public string CurrencyIso { get; set; }
            public string CurrencySymbol { get; set; }
            public int CurrencyPrecision { get; set; } = 2;
        }

        private class BillingDrillResult
        {
            public List<BillingDrillRow> Rows { get; set; }
            public int Total { get; set; }
        }

        private class BillingDrillRow
        {
            public int ContractId { get; set; }
            public int ScheduleId { get; set; }
            public string DocumentNo { get; set; }
            public string CustomerName { get; set; }
            public string FrequencyLabel { get; set; }
            public string FromDate { get; set; }
            public int DaysToInvoice { get; set; }
            public decimal NextInvoiceBase { get; set; }
            public string CurrencyIso { get; set; }
            public string CurrencySymbol { get; set; }
            public int CurrencyPrecision { get; set; }
        }
    }
}
