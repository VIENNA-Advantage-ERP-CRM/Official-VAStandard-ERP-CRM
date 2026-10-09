/******************************************************
 * Module Name    : VAS
 * Purpose        : Service Contracts module Renewal-action-needed KPI widget endpoints
 * chronological  : Development
 * Created Date   : 2026-09-07
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
    /// Module Name : VAS_245_RenewalActionWidget
    /// Purpose     : Data endpoints for the 2x1 clickable "Renewal action needed"
    ///               KPI tile on the Service Contracts dashboard: (1) the headline
    ///               scalar pair - the COUNT of live contracts with no successor
    ///               yet, and a sub-count already past their own notice date, and
    ///               (2) the paged (@7, most-overdue-notice first) drill list the
    ///               tile opens on click. Every condition is DERIVED, not stored:
    ///               live (co.Processed='Y' - see the 2026-09-07 note on
    ///               RenewalActionWhereSql - IsCancel='N', EndDate >= CURRENT_DATE)
    ///               and no active successor yet (a correlated NOT EXISTS probe on
    ///               C_Contract.Ref_Contract_ID against the SAME table - not an
    ///               MRole alias). MRole is applied to the single physical table
    ///               alias "co" only. A drill row's detail (its Renew / Generate
    ///               invoice actions, and its "Renewal notice" attention card)
    ///               reuses the already-built VAS_241_ContractSearchWidget/
    ///               GetContract, RunRenew and RunGenerateInvoice endpoints rather
    ///               than duplicating that logic - the same shared-endpoint reuse
    ///               VAS_120's Log-activity quick action already established
    ///               against VAS_126, and VAS_244 already reused here too. Read-
    ///               only here; no writes.
    /// Chronological development:
    ///   VAI052      2026-09-07 Created
    ///   VAI052      2026-09-07 Live now reads co.Processed='Y' instead of
    ///                          co.DocStatus='CO' (same fix as VAS_243/VAS_244/
    ///                          VAS_246, verified against real data).
    ///   VAI052      2026-09-08 Updated logic: dropped the RenewalType='M' filter
    ///                          and the DAYSBETWEEN(...) &lt;= CancelBeforeDays
    ///                          notice-window filter - the count is now every live
    ///                          contract with no successor, not just manual ones
    ///                          within their notice window. NoticeOverdueCount
    ///                          stays an informational sub-count only.
    ///   VAI052      2026-09-08 The drill list's Value column is now converted to
    ///                          the tenant base currency via CurrencyConvert
    ///                          (same resolution as VAS_243/244/246), instead of
    ///                          each contract's own transaction currency.
    ///   VAI052      2026-09-10 GetSummaryData's Notice_Overdue_Count now reads
    ///                          DAYSBETWEEN(co.EndDate, CURRENT_DATE) instead of
    ///                          DAYSBETWEEN(co.EndDate, CURRENT_DATE) - same
    ///                          argument-order correction as VAS_244's
    ///                          LiveExpiringWhereSql, per explicit, tested spec.
    ///   VAI052      2026-09-10 Notice_Overdue_Count briefly required
    ///                          RenewalType=Manual too, then reverted same day
    ///                          per explicit spec: counts every live contract
    ///                          past its notice window regardless of renewal
    ///                          type, matching VAS_261_NeedsAttentionWidget's
    ///                          own NoticeOverdue tile (also reverted).
    /// </summary>
    public class VAS_245_RenewalActionWidgetController : Controller
    {
        private static readonly VLogger Log = VLogger.GetVLogger(typeof(VAS_245_RenewalActionWidgetController).FullName);

        private const int DrillPageSize = 7;

        // Upper bound for the "Open in browser" TabWhereClause IN-list (GetContractIdsData) -
        // this KPI's live population is naturally small; this just bounds a pathological case.
        private const int MaxZoomIds = 1000;

        /// <summary>
        /// Headline scalars: the manual-notice-window count and the sub-count
        /// already past the notice date.
        /// </summary>
        /// <returns>JSON { RenewalActionCount, NoticeOverdueCount } or { Error }.</returns>
        [AjaxAuthorizeAttribute]
        [AjaxSessionFilterAttribute]
        public JsonResult GetRenewalActionSummary()
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
                Log.Log(Level.SEVERE, "VAS_245_RenewalActionWidget.GetRenewalActionSummary", ex);
                return ErrorResult(ctx);
            }
        }

        /// <summary>
        /// Paged (@7) drill list for the "Renewal action needed" modal, most-
        /// overdue-notice first - the identical predicate the KPI scalars use.
        /// </summary>
        /// <param name="offset">Zero-based paging offset, advanced by seven.</param>
        /// <returns>JSON { Rows:[...], Total } or { Error }.</returns>
        [AjaxAuthorizeAttribute]
        [AjaxSessionFilterAttribute]
        public JsonResult GetRenewalActionContracts(int offset = 0)
        {
            if (Session["ctx"] == null) { return Json("", JsonRequestBehavior.AllowGet); }

            Ctx ctx = Session["ctx"] as Ctx;

            if (offset < 0) { offset = 0; }

            try
            {
                string json = JsonConvert.SerializeObject(GetDrillData(ctx, offset));
                return Json(json, JsonRequestBehavior.AllowGet);
            }
            catch (Exception ex)
            {
                Log.Log(Level.SEVERE, "VAS_245_RenewalActionWidget.GetRenewalActionContracts", ex);
                return ErrorResult(ctx);
            }
        }

        /// <summary>
        /// Every matching C_Contract_ID for the renewal-action predicate (not just
        /// the current @7 drill page), capped at <see cref="MaxZoomIds"/> - so
        /// "Open in browser" can filter the Service Contract window down to
        /// exactly this KPI's population via a TabWhereClause IN-list, instead of
        /// reconstructing the correlated NOT EXISTS successor probe as a raw
        /// client-side where fragment (not a shape the classic Tab where-clause
        /// is built to run).
        /// </summary>
        /// <returns>JSON { Ids:[...] } or { Error }.</returns>
        [AjaxAuthorizeAttribute]
        [AjaxSessionFilterAttribute]
        public JsonResult GetRenewalActionContractIds()
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
                Log.Log(Level.SEVERE, "VAS_245_RenewalActionWidget.GetRenewalActionContractIds", ex);
                return ErrorResult(ctx);
            }
        }

        /// <summary>
        /// The shared predicate, WHERE-only - every caller applies MRole to alias
        /// "co" itself right after calling this. The successor probe is a plain
        /// correlated NOT EXISTS on the same table and is not an MRole alias.
        /// 2026-09-07: "live" is read off co.Processed='Y' rather than
        /// co.DocStatus=:DocStatus_CO - verified against real data, the same fix
        /// already applied to VAS_243/VAS_244/VAS_246 (completed service
        /// contracts here aren't consistently carrying DocStatus='CO').
        /// 2026-09-08: updated logic - dropped the co.RenewalType='M' filter and
        /// the DAYSBETWEEN(...) &lt;= CancelBeforeDays notice-window filter. The
        /// count is now every live contract with no successor yet, regardless of
        /// renewal type or how far off its notice date is; NoticeOverdueCount
        /// (the SUM CASE below) still reads CancelBeforeDays, but only as an
        /// informational sub-count, no longer as a WHERE restriction.
        /// </summary>
        private static string RenewalActionWhereSql()
        {
            return @"
                   co.AD_Client_ID = @AD_Client_ID
               AND co.IsActive = 'Y'
               AND co.Processed = 'Y'
               AND co.IsCancel = 'N'
               AND co.EndDate >= CURRENT_DATE
               AND NOT EXISTS (
                     SELECT 1 FROM C_Contract succ
                      WHERE succ.Ref_Contract_ID = co.C_Contract_ID
                        AND succ.IsActive = 'Y'
                        AND succ.AD_Client_ID = co.AD_Client_ID )";
        }

        private SqlParameter[] BuildPredicateParameters(Ctx ctx)
        {
            return new[]
            {
                new SqlParameter("@AD_Client_ID", ctx.GetAD_Client_ID())
            };
        }

        /// <summary>
        /// The tenant's accounting-schema (base) currency - same AD_ClientInfo ->
        /// C_AcctSchema1 -> C_Currency resolution the sibling KPI widgets
        /// (VAS_243/244/246) already use.
        /// </summary>
        private static string SchemaCurrencySql()
        {
            return @"
                SELECT cs.C_Currency_ID AS Acct_Currency_ID,
                       cur.StdPrecision AS Std_Precision,
                       cur.ISO_Code     AS ISO_Code,
                       CASE WHEN cur.CurSymbol IS NOT NULL THEN cur.CurSymbol ELSE cur.ISO_Code END AS Cur_Symbol
                  FROM AD_ClientInfo ci
                  JOIN C_AcctSchema cs ON ( cs.C_AcctSchema_ID = ci.C_AcctSchema1_ID AND cs.IsActive = 'Y' )
                  JOIN C_Currency cur  ON ( cur.C_Currency_ID  = cs.C_Currency_ID    AND cur.IsActive = 'Y' )
                 WHERE ci.IsActive = 'Y'
                   AND ci.AD_Client_ID = @AD_Client_ID";
        }

        /// <summary>Resolves the base currency id/iso/symbol/precision for this tenant, or a zeroed default when the schema currency cannot be resolved.</summary>
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

        private RenewalActionSummary GetSummaryData(Ctx ctx)
        {
            RenewalActionSummary result = new RenewalActionSummary();
            if (ctx == null) { return result; }

            string sql = @"
                SELECT COUNT(co.C_Contract_ID) AS Renewal_Action_Count,
                       SUM( CASE WHEN DAYSBETWEEN(co.EndDate, CURRENT_DATE) < co.CancelBeforeDays
                                 THEN 1 ELSE 0 END ) AS Notice_Overdue_Count
                  FROM C_Contract co
                 WHERE " + RenewalActionWhereSql();

            sql = MRole.GetDefault(ctx).AddAccessSQL(sql, "co", MRole.SQL_FULLYQUALIFIED, MRole.SQL_RO);

            IDataReader dr = null;
            try
            {
                dr = DB.ExecuteReader(sql, BuildPredicateParameters(ctx));
                if (dr != null && dr.Read())
                {
                    result.RenewalActionCount = Util.GetValueOfInt(dr["Renewal_Action_Count"]);
                    result.NoticeOverdueCount = dr["Notice_Overdue_Count"] == DBNull.Value ? 0 : Util.GetValueOfInt(dr["Notice_Overdue_Count"]);
                }
            }
            finally
            {
                CloseReader(dr);
            }

            return result;
        }

        private RenewalActionDrillResult GetDrillData(Ctx ctx, int offset)
        {
            RenewalActionDrillResult result = new RenewalActionDrillResult { Rows = new List<RenewalActionDrillRow>() };
            if (ctx == null) { return result; }

            string language = GetLanguage(ctx);
            Dictionary<string, string> renewalTypeMap = GetDecodeMap("C_Contract", "RenewalType", language);

            int baseCurrencyId; string baseIso, baseSymbol; int basePrecision;
            ResolveBaseCurrency(ctx, out baseCurrencyId, out baseIso, out baseSymbol, out basePrecision);

            string baseSql = @"
                SELECT co.C_Contract_ID  AS Contract_Id,
                       co.DocumentNo     AS Document_No,
                       COALESCE(bp.Name, N'') AS Customer_Name,
                       COALESCE(pr.Name, N'') AS Product_Name,
                       CurrencyConvert(co.GrandTotal, co.C_Currency_ID, @BaseCurrency_ID,
                              CURRENT_DATE, co.C_ConversionType_ID, co.AD_Client_ID, co.AD_Org_ID) AS Grand_Total,
                       co.RenewalType    AS Renewal_Type_Code,
                       co.CancelBeforeDays AS Cancel_Before_Days,
                       co.EndDate        AS End_Date,
                       CAST(DAYSBETWEEN(co.EndDate, CURRENT_DATE) AS INTEGER) AS Days_To_End,
                       CAST(DAYSBETWEEN(co.EndDate, CURRENT_DATE) - co.CancelBeforeDays AS INTEGER) AS Notice_Slack_Days
                  FROM C_Contract co
                  LEFT JOIN C_BPartner bp  ON ( bp.C_BPartner_ID = co.C_BPartner_ID )
                  LEFT JOIN M_Product  pr  ON ( pr.M_Product_ID  = co.M_Product_ID )
                 WHERE " + RenewalActionWhereSql();

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
                parameters.Add(new SqlParameter("@Lim", DrillPageSize));

                dr = DB.ExecuteReader(listSql, parameters.ToArray());
                while (dr != null && dr.Read())
                {
                    string renewalTypeCode = Util.GetValueOfString(dr["Renewal_Type_Code"]);
                    DateTime? endDate = dr["End_Date"] == DBNull.Value ? (DateTime?)null : Util.GetValueOfDateTime(dr["End_Date"]);

                    result.Rows.Add(new RenewalActionDrillRow
                    {
                        ContractId = Util.GetValueOfInt(dr["Contract_Id"]),
                        DocumentNo = Util.GetValueOfString(dr["Document_No"]),
                        CustomerName = Util.GetValueOfString(dr["Customer_Name"]),
                        ProductName = Util.GetValueOfString(dr["Product_Name"]),
                        GrandTotal = dr["Grand_Total"] == DBNull.Value ? 0m : Util.GetValueOfDecimal(dr["Grand_Total"]),
                        CurrencyIso = baseIso,
                        CurrencySymbol = baseSymbol,
                        CurrencyPrecision = basePrecision,
                        RenewalTypeCode = renewalTypeCode,
                        RenewalType = DecodeLabel(renewalTypeMap, renewalTypeCode),
                        CancelBeforeDays = dr["Cancel_Before_Days"] == DBNull.Value ? 0 : Util.GetValueOfInt(dr["Cancel_Before_Days"]),
                        EndDate = endDate.HasValue ? endDate.Value.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture) : "",
                        DaysToEnd = dr["Days_To_End"] == DBNull.Value ? 0 : Util.GetValueOfInt(dr["Days_To_End"]),
                        NoticeSlackDays = dr["Notice_Slack_Days"] == DBNull.Value ? 0 : Util.GetValueOfInt(dr["Notice_Slack_Days"])
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
        /// Every C_Contract_ID matching the renewal-action predicate, most-
        /// overdue-notice first, capped at <see cref="MaxZoomIds"/>. Backs
        /// "Open in browser"'s TabWhereClause IN-list - see
        /// <see cref="GetRenewalActionContractIds"/>.
        /// </summary>
        private List<int> GetContractIdsData(Ctx ctx)
        {
            List<int> ids = new List<int>();
            if (ctx == null) { return ids; }

            string sql = @"
                SELECT co.C_Contract_ID AS Contract_Id
                  FROM C_Contract co
                 WHERE " + RenewalActionWhereSql() + @"
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

        /// <summary>The tenant's own label for a stored code, or the code itself when the dictionary carries no entry (never invented English).</summary>
        private static string DecodeLabel(Dictionary<string, string> map, string code)
        {
            if (string.IsNullOrEmpty(code)) { return ""; }
            string label;
            return map != null && map.TryGetValue(code, out label) ? label : code;
        }

        private static readonly ConcurrentDictionary<string, Dictionary<string, string>> DecodeMapCache =
            new ConcurrentDictionary<string, Dictionary<string, string>>(StringComparer.OrdinalIgnoreCase);

        /// <summary>AD_Ref_List(_Trl) Code -&gt; Label map for one column, cached per column+language (kpi-renewal-action.queries.md §D-3). Never a hard-coded A/M -&gt; Auto/Manual map.</summary>
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
                      JOIN AD_Column col ON ( col.AD_Reference_Value_ID = rl.AD_Reference_ID )
                      LEFT JOIN AD_Ref_List_Trl rlt ON ( rlt.AD_Ref_List_ID = rl.AD_Ref_List_ID AND rlt.AD_Language = @AD_Language )
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

        private string GetLanguage(Ctx ctx)
        {
            string language = ctx == null ? string.Empty : ctx.GetAD_Language();
            return string.IsNullOrWhiteSpace(language) ? "en_US" : language;
        }

        private void CloseReader(IDataReader reader)
        {
            if (reader == null) { return; }
            reader.Close();
            reader.Dispose();
        }

        private JsonResult ErrorResult(Ctx ctx)
        {
            string message = Msg.GetMsg(ctx, "Error") ?? "Error";
            string json = JsonConvert.SerializeObject(new { Error = message });
            return Json(json, JsonRequestBehavior.AllowGet);
        }

        private class RenewalActionSummary
        {
            public int RenewalActionCount { get; set; }
            public int NoticeOverdueCount { get; set; }
        }

        private class RenewalActionDrillResult
        {
            public List<RenewalActionDrillRow> Rows { get; set; }
            public int Total { get; set; }
        }

        private class RenewalActionDrillRow
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
            public int NoticeSlackDays { get; set; }
        }
    }
}
