/******************************************************
 * Module Name    : VAS
 * Purpose        : Service Contracts module Expiring-90-days KPI widget endpoints
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
    /// Module Name : VAS_244_ContractsExpiringWidget
    /// Purpose     : Data endpoints for the 2x1 clickable "Expiring ≤90 days" KPI
    ///               tile on the Service Contracts dashboard: (1) the headline
    ///               scalar pair - the COUNT of live contracts ending within 90
    ///               days and their combined base-currency value at stake, and
    ///               (2) the paged (@7, soonest-ending first) drill list the tile
    ///               opens on click. Live/expiring are DERIVED, not stored -
    ///               completed (co.Processed='Y' - see the 2026-09-07 note on
    ///               LiveExpiringWhereSql), not cancelled (IsCancel='N'), not yet
    ///               ended (EndDate >= CURRENT_DATE) and due within 90 days
    ///               (DAYSBETWEEN(CURRENT_DATE, EndDate) &lt;= 90) - so both
    ///               reconcile with the Service Contract window's own expiring
    ///               filter and the VAS_140 Customers-dashboard widget's ≤90-day
    ///               bucket. Scoped to AD_Client_ID IN (0, the tenant's own) so
    ///               System-level (client 0) contracts are included too. MRole is
    ///               applied to the single physical table alias "co" only.
    ///               RenewalType is decoded through the tenant's own
    ///               AD_Ref_List/AD_Ref_List_Trl dictionary, never a hard-coded
    ///               A/M -&gt; Auto/Manual map. A drill row's detail (and its
    ///               Renew / Generate invoice actions) reuses the already-built
    ///               VAS_241_ContractSearchWidget/GetContract, RunRenew and
    ///               RunGenerateInvoice endpoints rather than duplicating that
    ///               logic - the same shared-endpoint reuse VAS_120's Log-
    ///               activity quick action already established against VAS_126.
    ///               Read-only here; no writes.
    /// Chronological development:
    ///   VAI052      2026-09-07 Created
    ///   VAI052      2026-09-07 Live now reads co.Processed='Y' instead of
    ///                          co.DocStatus='CO' (verified against real data -
    ///                          completed contracts here aren't consistently
    ///                          carrying DocStatus='CO'), and AD_Client_ID scope
    ///                          widened to include client 0 alongside the tenant.
    ///   VAI052      2026-09-08 The drill list's Value column is now converted to
    ///                          the tenant base currency (same CurrencyConvert
    ///                          the summary tile already used), instead of each
    ///                          contract's own transaction currency - the list
    ///                          was mixing currencies row to row.
    ///   VAI052      2026-09-09 DAYSBETWEEN argument order flipped to (co.EndDate,
    ///                          CURRENT_DATE) per explicit, tested WHERE-clause spec.
    ///                          The spec's own text repeated "AD_Client_ID IN (0,
    ///                          :param)" a second time at the end - that duplicate
    ///                          is dropped here (kept once, at the top) since a
    ///                          second occurrence of the same @AD_Client_ID
    ///                          placeholder in one statement is both functionally
    ///                          redundant (identical condition) and the exact
    ///                          repeated-parameter shape that trips Oracle's
    ///                          positional binding (ORA-01008) elsewhere in this
    ///                          codebase.
    ///   VAI052      2026-09-10 The drill row's own Days_To_End display value now
    ///                          also reads DAYSBETWEEN(co.EndDate, CURRENT_DATE) -
    ///                          same argument-order correction as the WHERE clause,
    ///                          fixing rows showing a negative "Ends" day count for
    ///                          contracts that have not ended yet (confirmed
    ///                          DAYSBETWEEN(a,b) = a-b in this install).
    /// </summary>
    public class VAS_244_ContractsExpiringWidgetController : Controller
    {
        private static readonly VLogger Log = VLogger.GetVLogger(typeof(VAS_244_ContractsExpiringWidgetController).FullName);

        // The product rule for this tile (kpi-expiring.queries.md §E) - kept
        // consistent with the renewals pipeline's own ≤90 reconciliation.
        private const int ExpiringWindowDays = 90;

        private const int DrillPageSize = 7;

        // Upper bound for the "Open in browser" TabWhereClause IN-list (GetExpiringContractIdsData) -
        // this KPI's live population is naturally small; this just bounds a pathological case.
        private const int MaxZoomIds = 1000;

        /// <summary>
        /// Headline scalars for the KPI tile: live-and-expiring count + the
        /// combined value at stake in the tenant's base (accounting-schema)
        /// currency.
        /// </summary>
        /// <returns>JSON { ExpiringCount, ExpiringValueBase, CurrencyIso, CurrencySymbol, CurrencyPrecision } or { Error }.</returns>
        [AjaxAuthorizeAttribute]
        [AjaxSessionFilterAttribute]
        public JsonResult GetExpiringSummary()
        {
            if (Session["ctx"] == null) { return Json("", JsonRequestBehavior.AllowGet); }

            Ctx ctx = Session["ctx"] as Ctx;

            try
            {
                string json = JsonConvert.SerializeObject(GetExpiringSummaryData(ctx));
                return Json(json, JsonRequestBehavior.AllowGet);
            }
            catch (Exception ex)
            {
                Log.Log(Level.SEVERE, "VAS_244_ContractsExpiringWidget.GetExpiringSummary", ex);
                return ErrorResult(ctx);
            }
        }

        /// <summary>
        /// Paged (@7) drill list for the "Contracts expiring ≤90 days" modal,
        /// soonest-ending first - the identical live-and-expiring predicate the
        /// KPI scalars use.
        /// </summary>
        /// <param name="offset">Zero-based paging offset, advanced by seven.</param>
        /// <returns>JSON { Rows:[...], Total } or { Error }.</returns>
        [AjaxAuthorizeAttribute]
        [AjaxSessionFilterAttribute]
        public JsonResult GetExpiringContracts(int offset = 0)
        {
            if (Session["ctx"] == null) { return Json("", JsonRequestBehavior.AllowGet); }

            Ctx ctx = Session["ctx"] as Ctx;

            if (offset < 0) { offset = 0; }

            try
            {
                string json = JsonConvert.SerializeObject(GetExpiringContractsData(ctx, offset));
                return Json(json, JsonRequestBehavior.AllowGet);
            }
            catch (Exception ex)
            {
                Log.Log(Level.SEVERE, "VAS_244_ContractsExpiringWidget.GetExpiringContracts", ex);
                return ErrorResult(ctx);
            }
        }

        /// <summary>
        /// Every matching C_Contract_ID for the live-and-expiring predicate (not
        /// just the current @7 drill page), capped at <see cref="MaxZoomIds"/> -
        /// so "Open in browser" can filter the Service Contract window down to
        /// exactly this KPI's population via a TabWhereClause IN-list, instead of
        /// reconstructing the DAYSBETWEEN/CURRENT_DATE predicate as a raw client-
        /// side where fragment (the same cross-DB relative-date-arithmetic risk
        /// VAS_140 already documents).
        /// </summary>
        /// <returns>JSON { Ids:[...] } or { Error }.</returns>
        [AjaxAuthorizeAttribute]
        [AjaxSessionFilterAttribute]
        public JsonResult GetExpiringContractIds()
        {
            if (Session["ctx"] == null) { return Json("", JsonRequestBehavior.AllowGet); }

            Ctx ctx = Session["ctx"] as Ctx;

            try
            {
                string json = JsonConvert.SerializeObject(new { Ids = GetExpiringContractIdsData(ctx) });
                return Json(json, JsonRequestBehavior.AllowGet);
            }
            catch (Exception ex)
            {
                Log.Log(Level.SEVERE, "VAS_244_ContractsExpiringWidget.GetExpiringContractIds", ex);
                return ErrorResult(ctx);
            }
        }

        /// <summary>
        /// The tenant's accounting-schema (base) currency - same resolution the
        /// sibling VAS_140 / VAS_243 Service Contracts KPIs already use, so every
        /// base-currency figure on this dashboard agrees.
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

        /// <summary>
        /// The shared live-and-expiring predicate, WHERE-only - every caller
        /// applies MRole to alias "co" itself right after calling this.
        /// 2026-09-07: "live" is now read off co.Processed='Y' rather than
        /// co.DocStatus=:DocStatus_CO - verified against real data, where
        /// completed service contracts are marked via Processed (the same flag
        /// VAS_140's own core C_Contract eligibility already keys off) rather
        /// than consistently carrying DocStatus='CO'; the DocStatus check was
        /// silently excluding real completed contracts. Also scopes
        /// AD_Client_ID to (0, :AD_Client_ID) so System-level (client 0) rows
        /// are included alongside the tenant's own, per the same verification.
        /// 2026-09-09: DAYSBETWEEN argument order flipped to (co.EndDate,
        /// CURRENT_DATE) per explicit, tested WHERE-clause spec. The spec's
        /// own text repeated "AD_Client_ID IN (0, :param)" a second time at
        /// the end - that duplicate is dropped here (kept once, at the top)
        /// since a second occurrence of the same @AD_Client_ID placeholder
        /// in one statement is both functionally redundant (identical
        /// condition) and the exact repeated-parameter shape that trips
        /// Oracle's positional binding (ORA-01008) elsewhere in this codebase.
        /// </summary>
        private static string LiveExpiringWhereSql()
        {
            return @"
                   co.AD_Client_ID IN (0, @AD_Client_ID)
               AND co.IsActive = 'Y'
               AND co.IsCancel = 'N'
               AND co.Processed = 'Y'
               AND co.EndDate >= CURRENT_DATE
               AND DAYSBETWEEN(co.EndDate, CURRENT_DATE) <= @ExpiringDays";
        }

        private ExpiringSummary GetExpiringSummaryData(Ctx ctx)
        {
            ExpiringSummary result = new ExpiringSummary();
            if (ctx == null) { return result; }

            int baseCurrencyId; string iso, symbol; int precision;
            ResolveBaseCurrency(ctx, out baseCurrencyId, out iso, out symbol, out precision);
            result.CurrencyIso = iso;
            result.CurrencySymbol = symbol;
            result.CurrencyPrecision = precision;

            string sql = @"
                SELECT COUNT(co.C_Contract_ID) AS Expiring_Count,
                       SUM( CurrencyConvert(co.GrandTotal, co.C_Currency_ID, @BaseCurrency_ID,
                              CURRENT_DATE, co.C_ConversionType_ID, co.AD_Client_ID, co.AD_Org_ID) ) AS Expiring_Value_Base
                  FROM C_Contract co
                 WHERE " + LiveExpiringWhereSql();

            sql = MRole.GetDefault(ctx).AddAccessSQL(sql, "co", MRole.SQL_FULLYQUALIFIED, MRole.SQL_RO);

            IDataReader dr = null;
            try
            {
                dr = DB.ExecuteReader(sql, BuildPredicateParameters(ctx, baseCurrencyId));
                if (dr != null && dr.Read())
                {
                    result.ExpiringCount = Util.GetValueOfInt(dr["Expiring_Count"]);
                    result.ExpiringValueBase = dr["Expiring_Value_Base"] == DBNull.Value ? 0m : Util.GetValueOfDecimal(dr["Expiring_Value_Base"]);
                }
            }
            finally
            {
                CloseReader(dr);
            }

            return result;
        }

        private ExpiringDrillResult GetExpiringContractsData(Ctx ctx, int offset)
        {
            ExpiringDrillResult result = new ExpiringDrillResult { Rows = new List<ExpiringDrillRow>() };
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
                       co.EndDate        AS End_Date,
                       CAST(DAYSBETWEEN(co.EndDate, CURRENT_DATE) AS INTEGER) AS Days_To_End
                  FROM C_Contract co
                  LEFT JOIN C_BPartner bp  ON ( bp.C_BPartner_ID = co.C_BPartner_ID )
                  LEFT JOIN M_Product  pr  ON ( pr.M_Product_ID  = co.M_Product_ID )
                 WHERE " + LiveExpiringWhereSql();

            baseSql = MRole.GetDefault(ctx).AddAccessSQL(baseSql, "co", MRole.SQL_FULLYQUALIFIED, MRole.SQL_RO);

            string listSql = baseSql + @"
                ORDER BY co.EndDate ASC
                OFFSET @Off ROWS FETCH NEXT @Lim ROWS ONLY";

            string countSql = "SELECT COUNT(1) AS Total_Count FROM ( " + baseSql + " ) cnt_wrap";

            IDataReader dr = null;
            try
            {
                List<SqlParameter> parameters = new List<SqlParameter>(BuildPredicateParameters(ctx, baseCurrencyId));
                parameters.Add(new SqlParameter("@Off", offset));
                parameters.Add(new SqlParameter("@Lim", DrillPageSize));

                dr = DB.ExecuteReader(listSql, parameters.ToArray());
                while (dr != null && dr.Read())
                {
                    string renewalTypeCode = Util.GetValueOfString(dr["Renewal_Type_Code"]);
                    DateTime? endDate = dr["End_Date"] == DBNull.Value ? (DateTime?)null : Util.GetValueOfDateTime(dr["End_Date"]);

                    result.Rows.Add(new ExpiringDrillRow
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
                        EndDate = endDate.HasValue ? endDate.Value.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture) : "",
                        DaysToEnd = dr["Days_To_End"] == DBNull.Value ? 0 : Util.GetValueOfInt(dr["Days_To_End"])
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
                countReader = DB.ExecuteReader(countSql, BuildPredicateParameters(ctx, baseCurrencyId));
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
        /// Every C_Contract_ID matching the live-and-expiring predicate, soonest-
        /// ending first, capped at <see cref="MaxZoomIds"/>. Backs "Open in
        /// browser"'s TabWhereClause IN-list - see <see cref="GetExpiringContractIds"/>.
        /// </summary>
        private List<int> GetExpiringContractIdsData(Ctx ctx)
        {
            List<int> ids = new List<int>();
            if (ctx == null) { return ids; }

            string sql = @"
                SELECT co.C_Contract_ID AS Contract_Id
                  FROM C_Contract co
                 WHERE " + LiveExpiringWhereSql() + @"
                 ORDER BY co.EndDate ASC
                 OFFSET 0 ROWS FETCH NEXT @MaxIds ROWS ONLY";

            sql = MRole.GetDefault(ctx).AddAccessSQL(sql, "co", MRole.SQL_FULLYQUALIFIED, MRole.SQL_RO);

            IDataReader dr = null;
            try
            {
                List<SqlParameter> parameters = new List<SqlParameter>(BuildPredicateParameters(ctx, 0));
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
        /// Fresh parameter array for one command execution - every placeholder in
        /// <see cref="LiveExpiringWhereSql"/> (plus @BaseCurrency_ID where used)
        /// occurs exactly once in each assembled statement, so no name repeats
        /// under Oracle's positional binding.
        /// </summary>
        private SqlParameter[] BuildPredicateParameters(Ctx ctx, int baseCurrencyId)
        {
            return new[]
            {
                new SqlParameter("@BaseCurrency_ID", baseCurrencyId),
                new SqlParameter("@AD_Client_ID", ctx.GetAD_Client_ID()),
                new SqlParameter("@ExpiringDays", ExpiringWindowDays)
            };
        }

        /// <summary>The tenant's own label for a stored code, or the code itself when the dictionary carries no entry (never invented English).</summary>
        private static string DecodeLabel(Dictionary<string, string> map, string code)
        {
            if (string.IsNullOrEmpty(code)) { return ""; }
            string label;
            return map != null && map.TryGetValue(code, out label) ? label : code;
        }

        /// <summary>
        /// AD_Ref_List(_Trl) Code -> Label map for one C_Contract column, cached
        /// per column+language (kpi-expiring.queries.md §D-3). Never a hard-coded
        /// A/M -&gt; Auto/Manual map - resolved from the tenant's own dictionary.
        /// </summary>
        private static readonly ConcurrentDictionary<string, Dictionary<string, string>> DecodeMapCache =
            new ConcurrentDictionary<string, Dictionary<string, string>>(StringComparer.OrdinalIgnoreCase);

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

        private class ExpiringSummary
        {
            public int ExpiringCount { get; set; }
            public decimal ExpiringValueBase { get; set; }
            public string CurrencyIso { get; set; }
            public string CurrencySymbol { get; set; }
            public int CurrencyPrecision { get; set; } = 2;
        }

        private class ExpiringDrillResult
        {
            public List<ExpiringDrillRow> Rows { get; set; }
            public int Total { get; set; }
        }

        private class ExpiringDrillRow
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
            public string EndDate { get; set; }
            public int DaysToEnd { get; set; }
        }
    }
}
