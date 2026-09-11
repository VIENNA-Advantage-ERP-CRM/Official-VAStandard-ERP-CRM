/******************************************************
 * Module Name    : VAS
 * Purpose        : Service Contracts module Renewals pipeline centerpiece widget endpoints
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
    /// Module Name : VAS_258_RenewalsPipelineWidget
    /// Purpose     : Data endpoints for the c5 x r3 centerpiece "Renewals pipeline"
    ///               list widget on the Service Contracts dashboard: (1) the header
    ///               banner - the COUNT of live contracts ending within 6 months and
    ///               their combined base-currency value, (2) the paged (@7, soonest-
    ///               ending first) rows the widget body always shows (also reused,
    ///               with a larger page, for the header "All" list modal - per
    ///               renewals-pipeline.queries.md D-4), and (3) every matching
    ///               Contract_ID (capped) so "Open in browser" can filter the Service
    ///               Contract window down to exactly this population via a
    ///               TabWhereClause IN-list, instead of reconstructing the
    ///               DAYSBETWEEN/CURRENT_DATE predicate as a raw client-side where
    ///               fragment. Live = Processed='Y' AND IsCancel='N' AND
    ///               EndDate >= CURRENT_DATE, ending within the pipeline window
    ///               (DAYSBETWEEN(CURRENT_DATE, EndDate) &lt;= 180). RenewalType is
    ///               decoded through the tenant's own AD_Ref_List/AD_Ref_List_Trl
    ///               dictionary, never a hard-coded A/M -&gt; Auto/Manual map. A row's
    ///               detail (its Renew / Generate invoice actions) reuses the
    ///               already-built VAS_241_ContractSearchWidget/GetContract, RunRenew
    ///               and RunGenerateInvoice endpoints rather than duplicating that
    ///               logic - the same shared-endpoint reuse VAS_120's Log-activity
    ///               quick action already established against VAS_126, and
    ///               VAS_244/VAS_245/VAS_246 already reused too. MRole is applied to
    ///               the single physical table alias "co" only. Read-only here; no
    ///               writes.
    /// Chronological development:
    ///   VAI052      2026-09-08 Created
    ///   VAI052      2026-09-08 "Live" now reads co.Processed='Y' instead of
    ///                          co.DocStatus=:DocStatus_Completed - the Service
    ///                          Contract window itself does not rely on DocStatus
    ///                          for this (verified against real data, the same
    ///                          fix already applied to VAS_243/244/245/246).
    /// </summary>
    public class VAS_258_RenewalsPipelineWidgetController : Controller
    {
        private static readonly VLogger Log = VLogger.GetVLogger(typeof(VAS_258_RenewalsPipelineWidgetController).FullName);

        // The product rule for this widget (renewals-pipeline.prompt.md §1 / queries.md §A.7) - ~6 months.
        private const int PipelineWindowDays = 180;

        private const int DrillPageSize = 7;

        // Upper bound for the "Open in browser" TabWhereClause IN-list (GetContractIdsData) -
        // this pipeline's live population is naturally small; this just bounds a pathological case.
        private const int MaxZoomIds = 1000;

        /// <summary>
        /// Header banner scalars for the widget: live-and-ending-within-window count
        /// plus the combined value in the tenant's base (accounting-schema) currency.
        /// </summary>
        /// <returns>JSON { PipelineCount, PipelineValueBase, CurrencyIso, CurrencySymbol, CurrencyPrecision } or { Error }.</returns>
        [AjaxAuthorizeAttribute]
        [AjaxSessionFilterAttribute]
        public JsonResult GetRenewalsPipelineSummary()
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
                Log.Log(Level.SEVERE, "VAS_258_RenewalsPipelineWidget.GetRenewalsPipelineSummary", ex);
                return ErrorResult(ctx);
            }
        }

        /// <summary>
        /// Paged rows for the pipeline, soonest-ending first - the identical query
        /// backs both the widget's own always-visible @7 body and (with a larger
        /// "limit") the header "All" list modal, per renewals-pipeline.queries.md D-4.
        /// </summary>
        /// <param name="offset">Zero-based paging offset.</param>
        /// <param name="limit">Page size - 7 for the widget body, larger for the "All" list modal (capped at <see cref="MaxZoomIds"/>).</param>
        /// <returns>JSON { Rows:[...], Total } or { Error }.</returns>
        [AjaxAuthorizeAttribute]
        [AjaxSessionFilterAttribute]
        public JsonResult GetRenewalsPipelineContracts(int offset = 0, int limit = DrillPageSize)
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
                Log.Log(Level.SEVERE, "VAS_258_RenewalsPipelineWidget.GetRenewalsPipelineContracts", ex);
                return ErrorResult(ctx);
            }
        }

        /// <summary>
        /// Every matching C_Contract_ID for the pipeline predicate (not just one
        /// page), capped at <see cref="MaxZoomIds"/> - so "Open in browser" can
        /// filter the Service Contract window down to exactly this widget's
        /// population via a TabWhereClause IN-list.
        /// </summary>
        /// <returns>JSON { Ids:[...] } or { Error }.</returns>
        [AjaxAuthorizeAttribute]
        [AjaxSessionFilterAttribute]
        public JsonResult GetRenewalsPipelineContractIds()
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
                Log.Log(Level.SEVERE, "VAS_258_RenewalsPipelineWidget.GetRenewalsPipelineContractIds", ex);
                return ErrorResult(ctx);
            }
        }

        /// <summary>
        /// The tenant's accounting-schema (base) currency - AD_ClientInfo -&gt;
        /// C_AcctSchema1 -&gt; C_Currency, the same resolution the sibling Service
        /// Contracts KPIs (VAS_243/244/245/246) already use, so every base-currency
        /// figure on this dashboard agrees.
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

        /// <summary>
        /// The shared live-and-in-window predicate, WHERE-only - every caller
        /// applies MRole to alias "co" itself right after calling this (renewals-
        /// pipeline.queries.md A.2/A.7). "Live" is Processed='Y' (not
        /// DocStatus=MContract.DOCSTATUS_Completed - the Service Contract window
        /// does not rely on that column, verified against real data, the same fix
        /// already applied to VAS_243/244/245/246) AND IsCancel = 'N' AND
        /// EndDate &gt;= CURRENT_DATE; the pipeline set additionally caps
        /// DAYSBETWEEN(CURRENT_DATE, EndDate) at @PipelineWindowDays (180, ~6 months).
        /// </summary>
        /// <returns>WHERE-clause text (binds @AD_Client_ID, @PipelineWindowDays).</returns>
        private static string PipelineWhereSql()
        {
            return @"
                   co.AD_Client_ID = @AD_Client_ID
               AND co.IsActive = 'Y'
               AND co.Processed = 'Y'
               AND co.IsCancel = 'N'
               AND co.EndDate >= CURRENT_DATE
               AND DAYSBETWEEN(co.EndDate, CURRENT_DATE) <= @PipelineWindowDays";
        }

        /// <summary>
        /// Fresh parameter array for one command execution against
        /// <see cref="PipelineWhereSql"/> - every placeholder occurs exactly once in
        /// each assembled statement, so no name repeats under Oracle's positional
        /// binding.
        /// </summary>
        /// <param name="ctx">Session context.</param>
        private SqlParameter[] BuildPredicateParameters(Ctx ctx)
        {
            return new[]
            {
                new SqlParameter("@AD_Client_ID", ctx.GetAD_Client_ID()),
                new SqlParameter("@PipelineWindowDays", PipelineWindowDays)
            };
        }

        /// <summary>
        /// Resolves the header banner: live-and-in-window count plus the
        /// base-currency value at stake. GrandTotal is converted per-row to the
        /// base currency via CurrencyConvert before summing - never a raw
        /// cross-currency SUM.
        /// </summary>
        /// <param name="ctx">Session context.</param>
        private PipelineSummary GetSummaryData(Ctx ctx)
        {
            PipelineSummary result = new PipelineSummary();
            if (ctx == null) { return result; }

            int baseCurrencyId; string iso, symbol; int precision;
            ResolveBaseCurrency(ctx, out baseCurrencyId, out iso, out symbol, out precision);
            result.CurrencyIso = iso;
            result.CurrencySymbol = symbol;
            result.CurrencyPrecision = precision;

            string sql = @"
                SELECT COUNT(co.C_Contract_ID) AS Pipeline_Count,
                       SUM( CurrencyConvert(co.GrandTotal, co.C_Currency_ID, @BaseCurrency_ID,
                              CURRENT_DATE, co.C_ConversionType_ID, co.AD_Client_ID, co.AD_Org_ID) ) AS Pipeline_Value_Base
                  FROM C_Contract co
                 WHERE " + PipelineWhereSql();

            sql = MRole.GetDefault(ctx).AddAccessSQL(sql, "co", MRole.SQL_FULLYQUALIFIED, MRole.SQL_RO);

            IDataReader dr = null;
            try
            {
                List<SqlParameter> parameters = new List<SqlParameter>(BuildPredicateParameters(ctx));
                parameters.Add(new SqlParameter("@BaseCurrency_ID", baseCurrencyId));

                dr = DB.ExecuteReader(sql, parameters.ToArray());
                if (dr != null && dr.Read())
                {
                    result.PipelineCount = Util.GetValueOfInt(dr["Pipeline_Count"]);
                    result.PipelineValueBase = dr["Pipeline_Value_Base"] == DBNull.Value ? 0m : Util.GetValueOfDecimal(dr["Pipeline_Value_Base"]);
                }
            }
            finally
            {
                CloseReader(dr);
            }

            return result;
        }

        /// <summary>
        /// Paged pipeline rows, soonest-ending first (renewals-pipeline.queries.md
        /// D-2/D-4) - the base SQL is WHERE-only until MRole is applied to alias
        /// "co", then ORDER BY / OFFSET..FETCH are appended.
        /// </summary>
        /// <param name="ctx">Session context.</param>
        /// <param name="offset">Zero-based paging offset.</param>
        /// <param name="limit">Page size.</param>
        private PipelineDrillResult GetDrillData(Ctx ctx, int offset, int limit)
        {
            PipelineDrillResult result = new PipelineDrillResult { Rows = new List<PipelineDrillRow>() };
            if (ctx == null) { return result; }

            string language = GetLanguage(ctx);
            Dictionary<string, string> renewalTypeMap = GetDecodeMap("C_Contract", "RenewalType", language);

            int baseCurrencyId; string baseIso, baseSymbol; int basePrecision;
            ResolveBaseCurrency(ctx, out baseCurrencyId, out baseIso, out baseSymbol, out basePrecision);

            string baseSql = @"
                SELECT co.C_Contract_ID      AS Contract_Id,
                       co.DocumentNo         AS Document_No,
                       COALESCE(bp.Name, N'') AS Customer_Name,
                       COALESCE(pr.Name, N'') AS Product_Name,
                       co.RenewalType        AS Renewal_Type_Code,
                       co.CancelBeforeDays   AS Cancel_Before_Days,
                       co.EndDate            AS End_Date,
                       CAST(DAYSBETWEEN(co.EndDate, CURRENT_DATE) AS INTEGER) AS Days_To_End,
                       CurrencyConvert(co.GrandTotal, co.C_Currency_ID, @BaseCurrency_ID,
                              CURRENT_DATE, co.C_ConversionType_ID, co.AD_Client_ID, co.AD_Org_ID) AS Grand_Total_Base
                  FROM C_Contract co
                  LEFT OUTER JOIN C_BPartner bp ON ( bp.C_BPartner_ID = co.C_BPartner_ID )
                  LEFT OUTER JOIN M_Product  pr ON ( pr.M_Product_ID  = co.M_Product_ID )
                 WHERE " + PipelineWhereSql();

            baseSql = MRole.GetDefault(ctx).AddAccessSQL(baseSql, "co", MRole.SQL_FULLYQUALIFIED, MRole.SQL_RO);

            string listSql = baseSql + @"
                ORDER BY co.EndDate ASC
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
                    string renewalTypeCode = Util.GetValueOfString(dr["Renewal_Type_Code"]);
                    DateTime? endDate = dr["End_Date"] == DBNull.Value ? (DateTime?)null : Util.GetValueOfDateTime(dr["End_Date"]);
                    int daysToEnd = dr["Days_To_End"] == DBNull.Value ? 0 : Util.GetValueOfInt(dr["Days_To_End"]);
                    int cancelBeforeDays = dr["Cancel_Before_Days"] == DBNull.Value ? 0 : Util.GetValueOfInt(dr["Cancel_Before_Days"]);

                    result.Rows.Add(new PipelineDrillRow
                    {
                        ContractId = Util.GetValueOfInt(dr["Contract_Id"]),
                        DocumentNo = Util.GetValueOfString(dr["Document_No"]),
                        CustomerName = Util.GetValueOfString(dr["Customer_Name"]),
                        ProductName = Util.GetValueOfString(dr["Product_Name"]),
                        GrandTotal = dr["Grand_Total_Base"] == DBNull.Value ? 0m : Util.GetValueOfDecimal(dr["Grand_Total_Base"]),
                        CurrencyIso = baseIso,
                        CurrencySymbol = baseSymbol,
                        CurrencyPrecision = basePrecision,
                        RenewalTypeCode = renewalTypeCode,
                        RenewalType = DecodeLabel(renewalTypeMap, renewalTypeCode),
                        CancelBeforeDays = cancelBeforeDays,
                        EndDate = endDate.HasValue ? endDate.Value.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture) : "",
                        DaysToEnd = daysToEnd,
                        // Derived in C# (renewals-pipeline.queries.md A.7) - never stored: the manual-notice
                        // deadline slack, EndDate - CancelBeforeDays relative to today.
                        NoticeDeadlineDays = daysToEnd - cancelBeforeDays
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
        /// Every C_Contract_ID matching the pipeline predicate, soonest-ending
        /// first, capped at <see cref="MaxZoomIds"/>. Backs "Open in browser"'s
        /// TabWhereClause IN-list - see <see cref="GetRenewalsPipelineContractIds"/>.
        /// </summary>
        /// <param name="ctx">Session context.</param>
        private List<int> GetContractIdsData(Ctx ctx)
        {
            List<int> ids = new List<int>();
            if (ctx == null) { return ids; }

            string sql = @"
                SELECT co.C_Contract_ID AS Contract_Id
                  FROM C_Contract co
                 WHERE " + PipelineWhereSql() + @"
                 ORDER BY co.EndDate ASC
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

        /// <summary>The tenant's own label for a stored code, or the code itself when the dictionary carries no entry (never invented English).</summary>
        /// <param name="map">Code -&gt; label dictionary from <see cref="GetDecodeMap"/>.</param>
        /// <param name="code">Stored List code.</param>
        private static string DecodeLabel(Dictionary<string, string> map, string code)
        {
            if (string.IsNullOrEmpty(code)) { return ""; }
            string label;
            return map != null && map.TryGetValue(code, out label) ? label : code;
        }

        private static readonly ConcurrentDictionary<string, Dictionary<string, string>> DecodeMapCache =
            new ConcurrentDictionary<string, Dictionary<string, string>>(StringComparer.OrdinalIgnoreCase);

        /// <summary>AD_Ref_List(_Trl) Code -&gt; Label map for one column, cached per column+language (renewals-pipeline.queries.md D-3). Never a hard-coded A/M -&gt; Auto/Manual map.</summary>
        /// <param name="tableName">Owning AD_Table.TableName.</param>
        /// <param name="columnName">Owning AD_Column.ColumnName.</param>
        /// <param name="language">AD_Language for the translated label.</param>
        private Dictionary<string, string> GetDecodeMap(string tableName, string columnName, string language)
        {
            string cacheKey = tableName + "." + columnName + "|" + language;

            Dictionary<string, string> cached;
            if (DecodeMapCache.TryGetValue(cacheKey, out cached)) { return cached; }

            Dictionary<string, string> map = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            int columnId = GetColumnId(tableName, columnName);

            if (columnId > 0)
            {
                string sql = @"
                    SELECT rl.Value AS Code, COALESCE(rlt.Name, rl.Name, rl.Value) AS Label
                      FROM AD_Ref_List rl
                      INNER JOIN AD_Column col ON ( col.AD_Reference_Value_ID = rl.AD_Reference_ID )
                      LEFT OUTER JOIN AD_Ref_List_Trl rlt ON ( rlt.AD_Ref_List_ID = rl.AD_Ref_List_ID AND rlt.AD_Language = @AD_Language )
                     WHERE col.AD_Column_ID = @AD_Column_ID
                     ORDER BY rl.Value";

                IDataReader dr = null;
                try
                {
                    dr = DB.ExecuteReader(sql, new[]
                    {
                        new SqlParameter("@AD_Column_ID", columnId),
                        new SqlParameter("@AD_Language", SqlDbType.NVarChar) { Value = language }
                    });
                    while (dr != null && dr.Read())
                    {
                        string code = Util.GetValueOfString(dr["Code"]);
                        if (!string.IsNullOrEmpty(code))
                        {
                            map[code] = Util.GetValueOfString(dr["Label"]);
                        }
                    }
                }
                finally
                {
                    CloseReader(dr);
                }
            }

            DecodeMapCache[cacheKey] = map;
            return map;
        }

        private static readonly ConcurrentDictionary<string, int> ColumnIdCache = new ConcurrentDictionary<string, int>(StringComparer.OrdinalIgnoreCase);

        /// <summary>Resolves and caches an AD_Column_ID by table/column name (Export_ID resolution not needed here - the column is looked up by its stable dictionary identity, cached per process).</summary>
        /// <param name="tableName">AD_Table.TableName.</param>
        /// <param name="columnName">AD_Column.ColumnName.</param>
        private static int GetColumnId(string tableName, string columnName)
        {
            string cacheKey = tableName + "." + columnName;

            int cached;
            if (ColumnIdCache.TryGetValue(cacheKey, out cached)) { return cached; }

            int columnId = Util.GetValueOfInt(DB.ExecuteScalar(
                "SELECT AD_Column_ID FROM AD_Column WHERE ColumnName = @Col AND AD_Table_ID = (SELECT AD_Table_ID FROM AD_Table WHERE TableName = @Tbl)",
                new[]
                {
                    new SqlParameter("@Col", SqlDbType.NVarChar) { Value = columnName },
                    new SqlParameter("@Tbl", SqlDbType.NVarChar) { Value = tableName }
                },
                null));

            ColumnIdCache[cacheKey] = columnId;
            return columnId;
        }

        /// <summary>Resolves the session's AD_Language, defaulting to en_US when unset.</summary>
        /// <param name="ctx">Session context.</param>
        private string GetLanguage(Ctx ctx)
        {
            string language = ctx == null ? string.Empty : ctx.GetAD_Language();
            return string.IsNullOrWhiteSpace(language) ? "en_US" : language;
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

        private class PipelineSummary
        {
            public int PipelineCount { get; set; }
            public decimal PipelineValueBase { get; set; }
            public string CurrencyIso { get; set; }
            public string CurrencySymbol { get; set; }
            public int CurrencyPrecision { get; set; } = 2;
        }

        private class PipelineDrillResult
        {
            public List<PipelineDrillRow> Rows { get; set; }
            public int Total { get; set; }
        }

        private class PipelineDrillRow
        {
            public int ContractId { get; set; }
            public string DocumentNo { get; set; }
            public string CustomerName { get; set; }
            public string ProductName { get; set; }
            public decimal GrandTotal { get; set; }
            public string CurrencyIso { get; set; }
            public string CurrencySymbol { get; set; }
            public int CurrencyPrecision { get; set; }
            public string RenewalTypeCode { get; set; }
            public string RenewalType { get; set; }
            public int CancelBeforeDays { get; set; }
            public string EndDate { get; set; }
            public int DaysToEnd { get; set; }
            public int NoticeDeadlineDays { get; set; }
        }
    }
}
