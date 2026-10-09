/******************************************************
 * Module Name    : VAS
 * Purpose        : Service Contracts module Unbilled Revenue KPI widget endpoints
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
    /// Module Name : VAS_246_UnbilledRevenueWidget
    /// Purpose     : Data endpoints for the 2x1 clickable "Unbilled revenue" KPI
    ///               tile on the Service Contracts dashboard: (1) the headline
    ///               scalar pair - the total unbilled amount (base currency)
    ///               across live contracts, and the count of live contracts still
    ///               carrying an unbilled balance, and (2) the paged (@7, largest
    ///               unbilled first) drill list the tile opens on click. Money
    ///               comes from the sanctioned rollup view
    ///               CR_SERVICECONTRACT_V.UNBILLED_AMT (joined
    ///               ON (v.C_Contract_ID = co.C_Contract_ID)); MRole still anchors
    ///               on the main physical table C_Contract (alias "co") only -
    ///               never the view alias "v" - per kpi-unbilled.queries.md §A.2.
    ///               Live = completed (co.Processed='Y' - see the 2026-09-07 note
    ///               on LiveWhereSql), not cancelled (IsCancel='N'), not yet ended
    ///               (EndDate >= CURRENT_DATE), scoped to AD_Client_ID IN (0, the
    ///               tenant's own) so System-level (client 0) contracts are
    ///               included too. A drill row's detail (its Generate invoice /
    ///               Renew / Open record actions, and its attention cards) reuses
    ///               the already-built VAS_241_ContractSearchWidget/GetContract,
    ///               RunRenew and RunGenerateInvoice endpoints rather than
    ///               duplicating that logic - the same shared-endpoint reuse
    ///               VAS_120's Log-activity quick action already established
    ///               against VAS_126, and VAS_244/VAS_245 already reused too.
    ///               Read-only here; no writes.
    /// Chronological development:
    ///   VAI052      2026-09-07 Created
    ///   VAI052      2026-09-07 Live now reads co.Processed='Y' instead of
    ///                          co.DocStatus='CO' (same fix as VAS_243/VAS_244,
    ///                          verified against real data), and AD_Client_ID
    ///                          scope widened to include client 0.
    ///   VAI052      2026-09-08 The drill list's Unbilled/Billed columns are now
    ///                          converted to the tenant base currency via
    ///                          CurrencyConvert (the summary tile already was),
    ///                          instead of each contract's own transaction
    ///                          currency.
    /// </summary>
    public class VAS_246_UnbilledRevenueWidgetController : Controller
    {
        private static readonly VLogger Log = VLogger.GetVLogger(typeof(VAS_246_UnbilledRevenueWidgetController).FullName);

        private const int DrillPageSize = 7;

        // Upper bound for the "Open in browser" TabWhereClause IN-list (GetContractIdsData) -
        // this KPI's live population is naturally small; this just bounds a pathological case.
        private const int MaxZoomIds = 1000;

        /// <summary>
        /// Headline scalars: total unbilled (base currency) + count of live
        /// contracts with UNBILLED_AMT &gt; 0.
        /// </summary>
        /// <returns>JSON { UnbilledTotalBase, UnbilledContractCount, CurrencyIso, CurrencySymbol, CurrencyPrecision } or { Error }.</returns>
        [AjaxAuthorizeAttribute]
        [AjaxSessionFilterAttribute]
        public JsonResult GetUnbilledSummary()
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
                Log.Log(Level.SEVERE, "VAS_246_UnbilledRevenueWidget.GetUnbilledSummary", ex);
                return ErrorResult(ctx);
            }
        }

        /// <summary>
        /// Paged (@7) drill list for the "Unbilled contracts" modal, largest
        /// unbilled first - the identical live-and-unbilled predicate the KPI
        /// scalars use.
        /// </summary>
        /// <param name="offset">Zero-based paging offset, advanced by seven.</param>
        /// <returns>JSON { Rows:[...], Total } or { Error }.</returns>
        [AjaxAuthorizeAttribute]
        [AjaxSessionFilterAttribute]
        public JsonResult GetUnbilledContracts(int offset = 0)
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
                Log.Log(Level.SEVERE, "VAS_246_UnbilledRevenueWidget.GetUnbilledContracts", ex);
                return ErrorResult(ctx);
            }
        }

        /// <summary>
        /// Every matching C_Contract_ID for the live-and-unbilled predicate (not
        /// just the current @7 drill page), capped at <see cref="MaxZoomIds"/> -
        /// so "Open in browser" can filter the Service Contract window down to
        /// exactly this KPI's population via a TabWhereClause IN-list, instead of
        /// reconstructing the CR_SERVICECONTRACT_V join as a raw client-side
        /// where fragment (not a shape the classic Tab where-clause can express -
        /// it has no join target).
        /// </summary>
        /// <returns>JSON { Ids:[...] } or { Error }.</returns>
        [AjaxAuthorizeAttribute]
        [AjaxSessionFilterAttribute]
        public JsonResult GetUnbilledContractIds()
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
                Log.Log(Level.SEVERE, "VAS_246_UnbilledRevenueWidget.GetUnbilledContractIds", ex);
                return ErrorResult(ctx);
            }
        }

        /// <summary>The tenant's accounting-schema (base) currency - same resolution the sibling Service Contracts KPIs already use.</summary>
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
        /// The shared live predicate, WHERE-only - every caller applies MRole to
        /// alias "co" itself right after calling this. The sanctioned
        /// CR_SERVICECONTRACT_V join is part of the FROM, not the predicate,
        /// since some callers add their own extra WHERE (v.UNBILLED_AMT &gt; 0)
        /// on top of this shared live scope.
        /// 2026-09-07: "live" is now read off co.Processed='Y' rather than
        /// co.DocStatus=:DocStatus_CO - verified against real data, the same fix
        /// already applied to VAS_243/VAS_244 (completed service contracts here
        /// aren't consistently carrying DocStatus='CO'). Also scopes
        /// AD_Client_ID to (0, :AD_Client_ID) so System-level (client 0) rows are
        /// included alongside the tenant's own, per the same verification.
        /// </summary>
        private static string LiveWhereSql()
        {
            return @"
                   co.AD_Client_ID IN (0, @AD_Client_ID)
               AND co.IsActive = 'Y'
               AND co.Processed = 'Y'
               AND co.IsCancel = 'N'
               AND co.EndDate >= CURRENT_DATE";
        }

        private SqlParameter[] BuildPredicateParameters(Ctx ctx, int baseCurrencyId)
        {
            return new[]
            {
                new SqlParameter("@BaseCurrency_ID", baseCurrencyId),
                new SqlParameter("@AD_Client_ID", ctx.GetAD_Client_ID())
            };
        }

        private UnbilledSummary GetSummaryData(Ctx ctx)
        {
            UnbilledSummary result = new UnbilledSummary();
            if (ctx == null) { return result; }

            int baseCurrencyId; string iso, symbol; int precision;
            ResolveBaseCurrency(ctx, out baseCurrencyId, out iso, out symbol, out precision);
            result.CurrencyIso = iso;
            result.CurrencySymbol = symbol;
            result.CurrencyPrecision = precision;

            string sql = @"
                SELECT SUM( CurrencyConvert(v.UNBILLED_AMT, co.C_Currency_ID, @BaseCurrency_ID,
                              CURRENT_DATE, co.C_ConversionType_ID, co.AD_Client_ID, co.AD_Org_ID) ) AS Unbilled_Total_Base,
                       SUM( CASE WHEN v.UNBILLED_AMT > 0 THEN 1 ELSE 0 END ) AS Unbilled_Contract_Count
                  FROM C_Contract co
                  JOIN CR_SERVICECONTRACT_V v ON ( v.C_Contract_ID = co.C_Contract_ID )
                 WHERE " + LiveWhereSql();

            // MRole anchors on co (the main physical table) - never on the view alias v.
            sql = MRole.GetDefault(ctx).AddAccessSQL(sql, "co", MRole.SQL_FULLYQUALIFIED, MRole.SQL_RO);

            IDataReader dr = null;
            try
            {
                dr = DB.ExecuteReader(sql, BuildPredicateParameters(ctx, baseCurrencyId));
                if (dr != null && dr.Read())
                {
                    result.UnbilledTotalBase = dr["Unbilled_Total_Base"] == DBNull.Value ? 0m : Util.GetValueOfDecimal(dr["Unbilled_Total_Base"]);
                    result.UnbilledContractCount = dr["Unbilled_Contract_Count"] == DBNull.Value ? 0 : Util.GetValueOfInt(dr["Unbilled_Contract_Count"]);
                }
            }
            finally
            {
                CloseReader(dr);
            }

            return result;
        }

        private UnbilledDrillResult GetDrillData(Ctx ctx, int offset)
        {
            UnbilledDrillResult result = new UnbilledDrillResult { Rows = new List<UnbilledDrillRow>() };
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
                       CurrencyConvert(v.UNBILLED_AMT, co.C_Currency_ID, @BaseCurrency_ID_1,
                              CURRENT_DATE, co.C_ConversionType_ID, co.AD_Client_ID, co.AD_Org_ID) AS Unbilled_Amt,
                       CurrencyConvert(v.BILLED_AMOUNT, co.C_Currency_ID, @BaseCurrency_ID_2,
                              CURRENT_DATE, co.C_ConversionType_ID, co.AD_Client_ID, co.AD_Org_ID) AS Billed_Amt,
                       co.RenewalType    AS Renewal_Type_Code,
                       co.EndDate        AS End_Date,
                       CAST(DAYSBETWEEN(co.EndDate, CURRENT_DATE) AS INTEGER) AS Days_To_End
                  FROM C_Contract co
                  JOIN CR_SERVICECONTRACT_V v ON ( v.C_Contract_ID = co.C_Contract_ID )
                  LEFT JOIN C_BPartner bp  ON ( bp.C_BPartner_ID = co.C_BPartner_ID )
                  LEFT JOIN M_Product  pr  ON ( pr.M_Product_ID  = co.M_Product_ID )
                 WHERE " + LiveWhereSql() + @"
                   AND v.UNBILLED_AMT > 0";

            baseSql = MRole.GetDefault(ctx).AddAccessSQL(baseSql, "co", MRole.SQL_FULLYQUALIFIED, MRole.SQL_RO);

            string listSql = baseSql + @"
                ORDER BY v.UNBILLED_AMT DESC
                OFFSET @Off ROWS FETCH NEXT @Lim ROWS ONLY";

            string countSql = "SELECT COUNT(1) AS Total_Count FROM ( " + baseSql + " ) cnt_wrap";

            IDataReader dr = null;
            try
            {
                List<SqlParameter> parameters = new List<SqlParameter>(BuildPredicateParameters(ctx, 0));
                parameters.Add(new SqlParameter("@BaseCurrency_ID_1", baseCurrencyId));
                parameters.Add(new SqlParameter("@BaseCurrency_ID_2", baseCurrencyId));
                parameters.Add(new SqlParameter("@Off", offset));
                parameters.Add(new SqlParameter("@Lim", DrillPageSize));

                dr = DB.ExecuteReader(listSql, parameters.ToArray());
                while (dr != null && dr.Read())
                {
                    DateTime? endDate = dr["End_Date"] == DBNull.Value ? (DateTime?)null : Util.GetValueOfDateTime(dr["End_Date"]);
                    string renewalTypeCode = Util.GetValueOfString(dr["Renewal_Type_Code"]);

                    result.Rows.Add(new UnbilledDrillRow
                    {
                        ContractId = Util.GetValueOfInt(dr["Contract_Id"]),
                        DocumentNo = Util.GetValueOfString(dr["Document_No"]),
                        CustomerName = Util.GetValueOfString(dr["Customer_Name"]),
                        ProductName = Util.GetValueOfString(dr["Product_Name"]),
                        UnbilledAmt = dr["Unbilled_Amt"] == DBNull.Value ? 0m : Util.GetValueOfDecimal(dr["Unbilled_Amt"]),
                        BilledAmt = dr["Billed_Amt"] == DBNull.Value ? 0m : Util.GetValueOfDecimal(dr["Billed_Amt"]),
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
                List<SqlParameter> countParameters = new List<SqlParameter>(BuildPredicateParameters(ctx, 0));
                countParameters.Add(new SqlParameter("@BaseCurrency_ID_1", baseCurrencyId));
                countParameters.Add(new SqlParameter("@BaseCurrency_ID_2", baseCurrencyId));

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
        /// Every C_Contract_ID matching the live-and-unbilled predicate, largest
        /// unbilled first, capped at <see cref="MaxZoomIds"/>. Backs "Open in
        /// browser"'s TabWhereClause IN-list - see <see cref="GetUnbilledContractIds"/>.
        /// </summary>
        private List<int> GetContractIdsData(Ctx ctx)
        {
            List<int> ids = new List<int>();
            if (ctx == null) { return ids; }

            string sql = @"
                SELECT co.C_Contract_ID AS Contract_Id
                  FROM C_Contract co
                  JOIN CR_SERVICECONTRACT_V v ON ( v.C_Contract_ID = co.C_Contract_ID )
                 WHERE " + LiveWhereSql() + @"
                   AND v.UNBILLED_AMT > 0
                 ORDER BY v.UNBILLED_AMT DESC
                 OFFSET 0 ROWS FETCH NEXT @MaxIds ROWS ONLY";

            // MRole anchors on co (the main physical table) - never on the view alias v.
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

        /// <summary>The tenant's own label for a stored code, or the code itself when the dictionary carries no entry (never invented English).</summary>
        private static string DecodeLabel(Dictionary<string, string> map, string code)
        {
            if (string.IsNullOrEmpty(code)) { return ""; }
            string label;
            return map != null && map.TryGetValue(code, out label) ? label : code;
        }

        private static readonly ConcurrentDictionary<string, Dictionary<string, string>> DecodeMapCache =
            new ConcurrentDictionary<string, Dictionary<string, string>>(StringComparer.OrdinalIgnoreCase);

        /// <summary>AD_Ref_List(_Trl) Code -&gt; Label map for one column, cached per column+language (kpi-unbilled.queries.md §D-3). Never a hard-coded A/M -&gt; Auto/Manual map.</summary>
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

        private class UnbilledSummary
        {
            public decimal UnbilledTotalBase { get; set; }
            public int UnbilledContractCount { get; set; }
            public string CurrencyIso { get; set; }
            public string CurrencySymbol { get; set; }
            public int CurrencyPrecision { get; set; } = 2;
        }

        private class UnbilledDrillResult
        {
            public List<UnbilledDrillRow> Rows { get; set; }
            public int Total { get; set; }
        }

        private class UnbilledDrillRow
        {
            public int ContractId { get; set; }
            public string DocumentNo { get; set; }
            public string CustomerName { get; set; }
            public string ProductName { get; set; }
            public decimal UnbilledAmt { get; set; }
            public decimal BilledAmt { get; set; }
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
