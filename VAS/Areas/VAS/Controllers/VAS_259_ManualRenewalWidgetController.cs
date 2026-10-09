/******************************************************
 * Module Name    : VAS
 * Purpose        : Service Contracts module Manual renewals — act before notice widget endpoints
 * chronological  : Development
 * Created Date   : 2026-09-08
 * Created by     : VAI052
 ******************************************************/

using Newtonsoft.Json;
using System;
using System.Collections.Concurrent;
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
    /// Module Name : VAS_259_ManualRenewalWidget
    /// Purpose     : Data endpoints for the c4 x r3 "Manual renewals - act before
    ///               notice" action-list widget on the Service Contracts dashboard:
    ///               (1) the header banner - the COUNT of live, manual-renewal
    ///               contracts with no successor that are at or approaching their
    ///               notice deadline, plus the sub-count already past the notice
    ///               date, (2) the paged (@7, most-overdue/closest-to-notice first)
    ///               rows the widget body always shows (also reused, with a larger
    ///               page, for the header "All" list modal), and (3) every matching
    ///               Contract_ID (capped) so "Open in browser" can filter the
    ///               Service Contract window down to exactly this population via a
    ///               TabWhereClause IN-list. Set = RenewalType = 'M' (bound
    ///               :RenewalType_Manual) AND live (Processed='Y' AND IsCancel='N'
    ///               AND EndDate&gt;=CURRENT_DATE) AND no successor (correlated NOT
    ///               EXISTS on C_Contract.Ref_Contract_ID - not an MRole alias) AND
    ///               DAYSBETWEEN(CURRENT_DATE, EndDate) &lt;= CancelBeforeDays + 30
    ///               (manual-renewals.queries.md A.7's NoticeGraceDays lead-in).
    ///               "Overdue" (daysToEnd &lt; CancelBeforeDays) vs "Act soon" is
    ///               derived per row in C#, never stored. MRole is applied to the
    ///               single physical table alias "co" only. A row's detail (its
    ///               Renew / Generate invoice actions) reuses the already-built
    ///               VAS_241_ContractSearchWidget/GetContract, RunRenew and
    ///               RunGenerateInvoice endpoints rather than duplicating that
    ///               logic - the same shared-endpoint reuse VAS_120's Log-activity
    ///               quick action already established against VAS_126, and
    ///               VAS_244/VAS_245/VAS_246/VAS_258 already reused too. Read-only
    ///               here; no writes.
    /// Chronological development:
    ///   VAI052      2026-09-08 Created
    ///   VAI052      2026-09-08 "Live" now reads co.Processed='Y' instead of
    ///                          co.DocStatus=:DocStatus_Completed - the Service
    ///                          Contract window itself does not rely on DocStatus
    ///                          for this (verified against real data, the same
    ///                          fix already applied to VAS_243/244/245/246/258).
    /// </summary>
    public class VAS_259_ManualRenewalWidgetController : Controller
    {
        private static readonly VLogger Log = VLogger.GetVLogger(typeof(VAS_259_ManualRenewalWidgetController).FullName);

        // manual-renewals.queries.md A.7/C - the 30-day lead-in before the notice deadline.
        private const int NoticeGraceDays = 30;

        private const int DrillPageSize = 7;

        // Upper bound for the "Open in browser" TabWhereClause IN-list (GetContractIdsData) -
        // this widget's live population is naturally small; this just bounds a pathological case.
        private const int MaxZoomIds = 1000;

        /// <summary>
        /// Header banner scalars: the manual-notice-window count and the sub-count
        /// already past the notice date.
        /// </summary>
        /// <returns>JSON { ManualCount, NoticeOverdueCount } or { Error }.</returns>
        [AjaxAuthorizeAttribute]
        [AjaxSessionFilterAttribute]
        public JsonResult GetManualRenewalSummary()
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
                Log.Log(Level.SEVERE, "VAS_259_ManualRenewalWidget.GetManualRenewalSummary", ex);
                return ErrorResult(ctx);
            }
        }

        /// <summary>
        /// Paged rows for the widget, most-overdue/closest-to-notice first - the
        /// identical query backs both the widget's own always-visible @7 body and
        /// (with a larger "limit") the header "All" list modal.
        /// </summary>
        /// <param name="offset">Zero-based paging offset.</param>
        /// <param name="limit">Page size - 7 for the widget body, larger for the "All" list modal (capped at <see cref="MaxZoomIds"/>).</param>
        /// <returns>JSON { Rows:[...], Total } or { Error }.</returns>
        [AjaxAuthorizeAttribute]
        [AjaxSessionFilterAttribute]
        public JsonResult GetManualRenewalContracts(int offset = 0, int limit = DrillPageSize)
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
                Log.Log(Level.SEVERE, "VAS_259_ManualRenewalWidget.GetManualRenewalContracts", ex);
                return ErrorResult(ctx);
            }
        }

        /// <summary>
        /// Every matching C_Contract_ID for the manual-notice predicate (not just
        /// one page), capped at <see cref="MaxZoomIds"/> - so "Open in browser" can
        /// filter the Service Contract window down to exactly this widget's
        /// population via a TabWhereClause IN-list.
        /// </summary>
        /// <returns>JSON { Ids:[...] } or { Error }.</returns>
        [AjaxAuthorizeAttribute]
        [AjaxSessionFilterAttribute]
        public JsonResult GetManualRenewalContractIds()
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
                Log.Log(Level.SEVERE, "VAS_259_ManualRenewalWidget.GetManualRenewalContractIds", ex);
                return ErrorResult(ctx);
            }
        }

        /// <summary>
        /// The shared manual-notice predicate, WHERE-only - every caller applies
        /// MRole to alias "co" itself right after calling this. The successor probe
        /// is a plain correlated NOT EXISTS on the same table and is not an MRole
        /// alias (manual-renewals.queries.md A.2). "Live" is Processed='Y' (not
        /// DocStatus=MContract.DOCSTATUS_Completed - the Service Contract window
        /// does not rely on that column, verified against real data, the same fix
        /// already applied to VAS_243/244/245/246/258) AND IsCancel = 'N' AND
        /// EndDate &gt;= CURRENT_DATE; the widget set additionally requires
        /// RenewalType = 'M' and caps DAYSBETWEEN(CURRENT_DATE, EndDate) at
        /// CancelBeforeDays + @NoticeGraceDays (30-day lead-in).
        /// </summary>
        /// <returns>WHERE-clause text (binds @AD_Client_ID, @RenewalType_Manual, @NoticeGraceDays).</returns>
        private static string ManualNoticeWhereSql()
        {
            return @"
                   co.AD_Client_ID = @AD_Client_ID
               AND co.IsActive = 'Y'
               AND co.RenewalType = @RenewalType_Manual
               AND co.Processed = 'Y'
               AND co.IsCancel = 'N'
               AND co.EndDate >= CURRENT_DATE
               AND DAYSBETWEEN(co.EndDate, CURRENT_DATE) <= ( co.CancelBeforeDays + @NoticeGraceDays )
               AND NOT EXISTS (
                     SELECT 1 FROM C_Contract succ
                      WHERE succ.Ref_Contract_ID = co.C_Contract_ID
                        AND succ.AD_Client_ID = co.AD_Client_ID
                        AND succ.IsActive = 'Y' )";
        }

        /// <summary>
        /// Fresh parameter array for one command execution against
        /// <see cref="ManualNoticeWhereSql"/> - every placeholder occurs exactly
        /// once in each assembled statement, so no name repeats under Oracle's
        /// positional binding.
        /// </summary>
        /// <param name="ctx">Session context.</param>
        private SqlParameter[] BuildPredicateParameters(Ctx ctx)
        {
            return new[]
            {
                new SqlParameter("@AD_Client_ID", ctx.GetAD_Client_ID()),
                new SqlParameter("@RenewalType_Manual", X_C_Contract.RENEWALTYPE_Manual),
                new SqlParameter("@NoticeGraceDays", NoticeGraceDays)
            };
        }

        /// <summary>
        /// Resolves the count + past-notice sub-count of the manual-notice set.
        /// </summary>
        /// <param name="ctx">Session context.</param>
        private ManualRenewalSummary GetSummaryData(Ctx ctx)
        {
            ManualRenewalSummary result = new ManualRenewalSummary();
            if (ctx == null) { return result; }

            string sql = @"
                SELECT COUNT(co.C_Contract_ID) AS Manual_Count,
                       SUM( CASE WHEN DAYSBETWEEN(co.EndDate, CURRENT_DATE) < co.CancelBeforeDays
                                 THEN 1 ELSE 0 END ) AS Notice_Overdue_Count
                  FROM C_Contract co
                 WHERE " + ManualNoticeWhereSql();

            sql = MRole.GetDefault(ctx).AddAccessSQL(sql, "co", MRole.SQL_FULLYQUALIFIED, MRole.SQL_RO);

            IDataReader dr = null;
            try
            {
                dr = DB.ExecuteReader(sql, BuildPredicateParameters(ctx));
                if (dr != null && dr.Read())
                {
                    result.ManualCount = Util.GetValueOfInt(dr["Manual_Count"]);
                    result.NoticeOverdueCount = dr["Notice_Overdue_Count"] == DBNull.Value ? 0 : Util.GetValueOfInt(dr["Notice_Overdue_Count"]);
                }
            }
            finally
            {
                CloseReader(dr);
            }

            return result;
        }

        /// <summary>
        /// Paged manual-notice rows, most-overdue/closest-to-notice first
        /// (manual-renewals.queries.md D-2/D-4) - the base SQL is WHERE-only until
        /// MRole is applied to alias "co", then ORDER BY / OFFSET..FETCH are
        /// appended.
        /// </summary>
        /// <param name="ctx">Session context.</param>
        /// <param name="offset">Zero-based paging offset.</param>
        /// <param name="limit">Page size.</param>
        private ManualRenewalDrillResult GetDrillData(Ctx ctx, int offset, int limit)
        {
            ManualRenewalDrillResult result = new ManualRenewalDrillResult { Rows = new List<ManualRenewalDrillRow>() };
            if (ctx == null) { return result; }

            int baseCurrencyId; string baseIso, baseSymbol; int basePrecision;
            ResolveBaseCurrency(ctx, out baseCurrencyId, out baseIso, out baseSymbol, out basePrecision);

            string baseSql = @"
                SELECT co.C_Contract_ID      AS Contract_Id,
                       co.DocumentNo         AS Document_No,
                       COALESCE(bp.Name, N'') AS Customer_Name,
                       COALESCE(pr.Name, N'') AS Product_Name,
                       co.CancelBeforeDays   AS Cancel_Before_Days,
                       co.EndDate            AS End_Date,
                       CAST(DAYSBETWEEN(co.EndDate, CURRENT_DATE) AS INTEGER) AS Days_To_End,
                       CurrencyConvert(co.GrandTotal, co.C_Currency_ID, @BaseCurrency_ID,
                              CURRENT_DATE, co.C_ConversionType_ID, co.AD_Client_ID, co.AD_Org_ID) AS Grand_Total_Base
                  FROM C_Contract co
                  LEFT OUTER JOIN C_BPartner bp ON ( bp.C_BPartner_ID = co.C_BPartner_ID )
                  LEFT OUTER JOIN M_Product  pr ON ( pr.M_Product_ID  = co.M_Product_ID )
                 WHERE " + ManualNoticeWhereSql();

            baseSql = MRole.GetDefault(ctx).AddAccessSQL(baseSql, "co", MRole.SQL_FULLYQUALIFIED, MRole.SQL_RO);

            string listSql = baseSql + @"
                ORDER BY ( DAYSBETWEEN(co.EndDate, CURRENT_DATE) - co.CancelBeforeDays ) ASC
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
                    DateTime? endDate = dr["End_Date"] == DBNull.Value ? (DateTime?)null : Util.GetValueOfDateTime(dr["End_Date"]);
                    int daysToEnd = dr["Days_To_End"] == DBNull.Value ? 0 : Util.GetValueOfInt(dr["Days_To_End"]);
                    int cancelBeforeDays = dr["Cancel_Before_Days"] == DBNull.Value ? 0 : Util.GetValueOfInt(dr["Cancel_Before_Days"]);
                    int noticeDeadlineDays = daysToEnd - cancelBeforeDays;

                    result.Rows.Add(new ManualRenewalDrillRow
                    {
                        ContractId = Util.GetValueOfInt(dr["Contract_Id"]),
                        DocumentNo = Util.GetValueOfString(dr["Document_No"]),
                        CustomerName = Util.GetValueOfString(dr["Customer_Name"]),
                        ProductName = Util.GetValueOfString(dr["Product_Name"]),
                        GrandTotal = dr["Grand_Total_Base"] == DBNull.Value ? 0m : Util.GetValueOfDecimal(dr["Grand_Total_Base"]),
                        CurrencyIso = baseIso,
                        CurrencySymbol = baseSymbol,
                        CurrencyPrecision = basePrecision,
                        CancelBeforeDays = cancelBeforeDays,
                        EndDate = endDate.HasValue ? endDate.Value.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture) : "",
                        DaysToEnd = daysToEnd,
                        // Derived in C# (manual-renewals.queries.md A.7) - never stored.
                        NoticeDeadlineDays = noticeDeadlineDays,
                        NoticeOverdue = daysToEnd < cancelBeforeDays
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
        /// Every C_Contract_ID matching the manual-notice predicate, most-
        /// overdue/closest-to-notice first, capped at <see cref="MaxZoomIds"/>.
        /// Backs "Open in browser"'s TabWhereClause IN-list - see
        /// <see cref="GetManualRenewalContractIds"/>.
        /// </summary>
        /// <param name="ctx">Session context.</param>
        private List<int> GetContractIdsData(Ctx ctx)
        {
            List<int> ids = new List<int>();
            if (ctx == null) { return ids; }

            string sql = @"
                SELECT co.C_Contract_ID AS Contract_Id
                  FROM C_Contract co
                 WHERE " + ManualNoticeWhereSql() + @"
                 ORDER BY ( DAYSBETWEEN(co.EndDate, CURRENT_DATE) - co.CancelBeforeDays ) ASC
                 OFFSET 0 ROWS FETCH NEXT @MaxIds ROWS ONLY";

            sql = MRole.GetDefault(ctx).AddAccessSQL(sql, "co", MRole.SQL_FULLYQUALIFIED, MRole.SQL_RO);

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
        /// Contracts KPIs (VAS_243/244/245/246/258) already use.
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

        private class ManualRenewalSummary
        {
            public int ManualCount { get; set; }
            public int NoticeOverdueCount { get; set; }
        }

        private class ManualRenewalDrillResult
        {
            public List<ManualRenewalDrillRow> Rows { get; set; }
            public int Total { get; set; }
        }

        private class ManualRenewalDrillRow
        {
            public int ContractId { get; set; }
            public string DocumentNo { get; set; }
            public string CustomerName { get; set; }
            public string ProductName { get; set; }
            public decimal GrandTotal { get; set; }
            public string CurrencyIso { get; set; }
            public string CurrencySymbol { get; set; }
            public int CurrencyPrecision { get; set; }
            public int CancelBeforeDays { get; set; }
            public string EndDate { get; set; }
            public int DaysToEnd { get; set; }
            public int NoticeDeadlineDays { get; set; }
            public bool NoticeOverdue { get; set; }
        }
    }
}
