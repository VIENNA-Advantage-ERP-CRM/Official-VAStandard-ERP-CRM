/******************************************************
 * Module Name    : VAS
 * Purpose        : Service Contracts module Recently lapsed - not renewed widget endpoints
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
    /// Module Name : VAS_264_RecentlyLapsedWidget
    /// Purpose     : Data endpoints for the c4 x r3 "Recently lapsed - not
    ///               renewed" recovery-list widget on the Service Contracts
    ///               dashboard: (1) the header banner - the COUNT of lapsed
    ///               contracts plus the total value lost (base currency),
    ///               (2) the paged (@7, most-recent lapse first) rows the
    ///               widget body always shows (also reused, with a larger
    ///               page, for the header "All" list modal), and (3) every
    ///               matching Contract_ID (capped) so "Open in browser" can
    ///               filter the Service Contract window down to exactly this
    ///               population via a TabWhereClause IN-list. Set = live-once
    ///               (co.Processed='Y') AND IsCancel='N' AND EndDate&lt;
    ///               CURRENT_DATE (ended) AND no successor (correlated NOT
    ///               EXISTS on C_Contract.Ref_Contract_ID - not an MRole
    ///               alias, recently-lapsed.queries.md A.7). A cancelled
    ///               contract is never "lapsed"; a contract whose renewal
    ///               already created a successor is excluded. RenewalType is
    ///               decoded via AD_Column -&gt; AD_Ref_List -&gt;
    ///               AD_Ref_List_Trl (cached per column+language, never a
    ///               hard-coded A/M -&gt; Auto/Manual map) - the same
    ///               per-column decode-map pattern VAS_258 already uses. A
    ///               row's detail (its Reactivate/Renew / Generate invoice
    ///               actions) reuses the already-built
    ///               VAS_241_ContractSearchWidget/GetContract, RunRenew and
    ///               RunGenerateInvoice endpoints rather than duplicating that
    ///               logic - the same shared-endpoint reuse VAS_120 set with
    ///               VAS_126 and VAS_244/245/246/258/259 already reused too.
    ///               Read-only here; no writes.
    /// Chronological development:
    ///   VAI052      2026-09-08 Created
    ///   VAI052      2026-09-08 "Live-once" reads co.Processed='Y' instead of
    ///                          co.DocStatus=:DocStatus_CO - the Service
    ///                          Contract window itself does not rely on
    ///                          DocStatus for this (verified against real
    ///                          data, the same fix already applied to
    ///                          VAS_243/244/245/246/258/259/260/261/262/263).
    ///   VAI052      2026-09-08 Per-row Reactivate and the detail modal's
    ///                          "Reactivate"/"Renew" action both run the real
    ///                          RenewContract process (VAS_241/RunRenew),
    ///                          NOT the ModelLibrary "ReActiveContract"
    ///                          process the build spec names: that process
    ///                          (ModelLibrary/Process/ReActiveContract.cs)
    ///                          unfreezes an unrelated VAS_ContractMaster /
    ///                          VAS_ContractLine / VAS_ContractOwner record
    ///                          set - it does not touch C_Contract at all, so
    ///                          wiring it here would either fail to resolve
    ///                          (no such C_Contract button column) or, if a
    ///                          coincidentally-named column existed, mutate
    ///                          the wrong table entirely. The pixel-source-of-
    ///                          truth mock (recently-lapsed.html openRenew())
    ///                          confirms this: it reuses the exact same
    ///                          RenewContract confirm/toast for a lapsed
    ///                          contract's "Reactivate" as every other
    ///                          contract's "Renew" ("Renewal created ·
    ///                          RenewContract", not "· ReActiveContract").
    /// </summary>
    public class VAS_264_RecentlyLapsedWidgetController : Controller
    {
        private static readonly VLogger Log = VLogger.GetVLogger(typeof(VAS_264_RecentlyLapsedWidgetController).FullName);

        private const int DrillPageSize = 7;

        // Upper bound for the "Open in browser" TabWhereClause IN-list (GetContractIdsData) -
        // this widget's lapsed population is naturally small; this just bounds a pathological case.
        private const int MaxZoomIds = 1000;

        /// <summary>
        /// Header banner scalars: the lapsed-contract count and the total
        /// value lost (base currency).
        /// </summary>
        /// <returns>JSON { LapsedCount, ValueLostBase, CurrencyIso, CurrencySymbol, CurrencyPrecision } or { Error }.</returns>
        [AjaxAuthorizeAttribute]
        [AjaxSessionFilterAttribute]
        public JsonResult GetRecentlyLapsedSummary()
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
                Log.Log(Level.SEVERE, "VAS_264_RecentlyLapsedWidget.GetRecentlyLapsedSummary", ex);
                return ErrorResult(ctx);
            }
        }

        /// <summary>
        /// Paged rows for the widget, most-recent lapse first - the identical
        /// query backs both the widget's own always-visible @7 body and (with
        /// a larger "limit") the header "All" list modal.
        /// </summary>
        /// <param name="offset">Zero-based paging offset.</param>
        /// <param name="limit">Page size - 7 for the widget body, larger for the "All" list modal (capped at <see cref="MaxZoomIds"/>).</param>
        /// <returns>JSON { Rows:[...], Total } or { Error }.</returns>
        [AjaxAuthorizeAttribute]
        [AjaxSessionFilterAttribute]
        public JsonResult GetRecentlyLapsedContracts(int offset = 0, int limit = DrillPageSize)
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
                Log.Log(Level.SEVERE, "VAS_264_RecentlyLapsedWidget.GetRecentlyLapsedContracts", ex);
                return ErrorResult(ctx);
            }
        }

        /// <summary>
        /// Every matching C_Contract_ID for the lapsed predicate (not just one
        /// page), capped at <see cref="MaxZoomIds"/> - so "Open in browser"
        /// can filter the Service Contract window down to exactly this
        /// widget's population via a TabWhereClause IN-list.
        /// </summary>
        /// <returns>JSON { Ids:[...] } or { Error }.</returns>
        [AjaxAuthorizeAttribute]
        [AjaxSessionFilterAttribute]
        public JsonResult GetRecentlyLapsedContractIds()
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
                Log.Log(Level.SEVERE, "VAS_264_RecentlyLapsedWidget.GetRecentlyLapsedContractIds", ex);
                return ErrorResult(ctx);
            }
        }

        /// <summary>
        /// The shared lapsed predicate, WHERE-only - every caller applies
        /// MRole to alias "co" itself right after calling this. The successor
        /// probe is a plain correlated NOT EXISTS on the same table and is
        /// not an MRole alias (recently-lapsed.queries.md A.2). "Live-once"
        /// is Processed='Y' (not DocStatus=MContract.DOCSTATUS_Completed -
        /// the Service Contract window does not rely on that column,
        /// verified against real data, the same fix already applied to
        /// VAS_243/244/245/246/258/259/260/261/262/263) AND IsCancel='N' AND
        /// EndDate &lt; CURRENT_DATE (already ended) AND no successor.
        /// </summary>
        /// <returns>WHERE-clause text (binds @AD_Client_ID).</returns>
        private static string LapsedWhereSql()
        {
            return @"
                   co.AD_Client_ID = @AD_Client_ID
               AND co.IsActive = 'Y'
               AND co.Processed = 'Y'
               AND co.IsCancel = 'N'
               AND co.EndDate < CURRENT_DATE
               AND NOT EXISTS (
                     SELECT 1 FROM C_Contract succ
                      WHERE succ.Ref_Contract_ID = co.C_Contract_ID
                        AND succ.AD_Client_ID = co.AD_Client_ID
                        AND succ.IsActive = 'Y' )";
        }

        /// <summary>
        /// Fresh parameter array for one command execution against
        /// <see cref="LapsedWhereSql"/> - every placeholder occurs exactly
        /// once in each assembled statement, so no name repeats under
        /// Oracle's positional binding.
        /// </summary>
        /// <param name="ctx">Session context.</param>
        private SqlParameter[] BuildPredicateParameters(Ctx ctx)
        {
            return new[]
            {
                new SqlParameter("@AD_Client_ID", ctx.GetAD_Client_ID())
            };
        }

        /// <summary>
        /// Resolves the lapsed-contract count and total value lost (base
        /// currency).
        /// </summary>
        /// <param name="ctx">Session context.</param>
        private RecentlyLapsedSummary GetSummaryData(Ctx ctx)
        {
            RecentlyLapsedSummary result = new RecentlyLapsedSummary();
            if (ctx == null) { return result; }

            int baseCurrencyId; string baseIso, baseSymbol; int basePrecision;
            ResolveBaseCurrency(ctx, out baseCurrencyId, out baseIso, out baseSymbol, out basePrecision);

            result.CurrencyIso = baseIso;
            result.CurrencySymbol = baseSymbol;
            result.CurrencyPrecision = basePrecision;

            string sql = @"
                SELECT COUNT(co.C_Contract_ID) AS Lapsed_Count,
                       SUM( CurrencyConvert(co.GrandTotal, co.C_Currency_ID, @BaseCurrency_ID,
                              CURRENT_DATE, co.C_ConversionType_ID, co.AD_Client_ID, co.AD_Org_ID) ) AS Value_Lost_Base
                  FROM C_Contract co
                 WHERE " + LapsedWhereSql();

            sql = MRole.GetDefault(ctx).AddAccessSQL(sql, "co", MRole.SQL_FULLYQUALIFIED, MRole.SQL_RO);

            IDataReader dr = null;
            try
            {
                List<SqlParameter> parameters = new List<SqlParameter>(BuildPredicateParameters(ctx));
                parameters.Add(new SqlParameter("@BaseCurrency_ID", baseCurrencyId));

                dr = DB.ExecuteReader(sql, parameters.ToArray());
                if (dr != null && dr.Read())
                {
                    result.LapsedCount = Util.GetValueOfInt(dr["Lapsed_Count"]);
                    result.ValueLostBase = dr["Value_Lost_Base"] == DBNull.Value ? 0m : Util.GetValueOfDecimal(dr["Value_Lost_Base"]);
                }
            }
            finally
            {
                CloseReader(dr);
            }

            return result;
        }

        /// <summary>
        /// Paged lapsed rows, most-recent lapse first
        /// (recently-lapsed.queries.md D-2) - the base SQL is WHERE-only
        /// until MRole is applied to alias "co", then ORDER BY /
        /// OFFSET..FETCH are appended.
        /// </summary>
        /// <param name="ctx">Session context.</param>
        /// <param name="offset">Zero-based paging offset.</param>
        /// <param name="limit">Page size.</param>
        private RecentlyLapsedDrillResult GetDrillData(Ctx ctx, int offset, int limit)
        {
            RecentlyLapsedDrillResult result = new RecentlyLapsedDrillResult { Rows = new List<RecentlyLapsedDrillRow>() };
            if (ctx == null) { return result; }

            int baseCurrencyId; string baseIso, baseSymbol; int basePrecision;
            ResolveBaseCurrency(ctx, out baseCurrencyId, out baseIso, out baseSymbol, out basePrecision);

            string language = GetLanguage(ctx);
            Dictionary<string, string> renewalTypeMap = GetDecodeMap("C_Contract", "RenewalType", language);

            string baseSql = @"
                SELECT co.C_Contract_ID      AS Contract_Id,
                       co.DocumentNo         AS Document_No,
                       COALESCE(bp.Name, N'') AS Customer_Name,
                       COALESCE(pr.Name, N'') AS Product_Name,
                       co.RenewalType        AS Renewal_Type_Code,
                       co.EndDate            AS End_Date,
                       CAST(DAYSBETWEEN(co.EndDate, CURRENT_DATE) AS INTEGER) AS Days_To_End,
                       CurrencyConvert(co.GrandTotal, co.C_Currency_ID, @BaseCurrency_ID,
                              CURRENT_DATE, co.C_ConversionType_ID, co.AD_Client_ID, co.AD_Org_ID) AS Grand_Total_Base
                  FROM C_Contract co
                  LEFT OUTER JOIN C_BPartner bp ON ( bp.C_BPartner_ID = co.C_BPartner_ID )
                  LEFT OUTER JOIN M_Product  pr ON ( pr.M_Product_ID  = co.M_Product_ID )
                 WHERE " + LapsedWhereSql();

            baseSql = MRole.GetDefault(ctx).AddAccessSQL(baseSql, "co", MRole.SQL_FULLYQUALIFIED, MRole.SQL_RO);

            string listSql = baseSql + @"
                ORDER BY co.EndDate DESC
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
                    string renewalTypeCode = Util.GetValueOfString(dr["Renewal_Type_Code"]);

                    result.Rows.Add(new RecentlyLapsedDrillRow
                    {
                        ContractId = Util.GetValueOfInt(dr["Contract_Id"]),
                        DocumentNo = Util.GetValueOfString(dr["Document_No"]),
                        CustomerName = Util.GetValueOfString(dr["Customer_Name"]),
                        ProductName = Util.GetValueOfString(dr["Product_Name"]),
                        RenewalTypeCode = renewalTypeCode,
                        RenewalTypeLabel = DecodeLabel(renewalTypeMap, renewalTypeCode),
                        GrandTotal = dr["Grand_Total_Base"] == DBNull.Value ? 0m : Util.GetValueOfDecimal(dr["Grand_Total_Base"]),
                        CurrencyIso = baseIso,
                        CurrencySymbol = baseSymbol,
                        CurrencyPrecision = basePrecision,
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
        /// Every C_Contract_ID matching the lapsed predicate, most-recent
        /// lapse first, capped at <see cref="MaxZoomIds"/>. Backs "Open in
        /// browser"'s TabWhereClause IN-list - see
        /// <see cref="GetRecentlyLapsedContractIds"/>.
        /// </summary>
        /// <param name="ctx">Session context.</param>
        private List<int> GetContractIdsData(Ctx ctx)
        {
            List<int> ids = new List<int>();
            if (ctx == null) { return ids; }

            string sql = @"
                SELECT co.C_Contract_ID AS Contract_Id
                  FROM C_Contract co
                 WHERE " + LapsedWhereSql() + @"
                 ORDER BY co.EndDate DESC
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

        /// <summary>Looks up a decoded label from a Code -&gt; Label map, falling back to the raw code when unmapped or blank when the code itself is blank.</summary>
        /// <param name="map">Decode map from <see cref="GetDecodeMap"/>.</param>
        /// <param name="code">Stored List code.</param>
        private static string DecodeLabel(Dictionary<string, string> map, string code)
        {
            if (string.IsNullOrEmpty(code)) { return ""; }
            string label;
            return map != null && map.TryGetValue(code, out label) ? label : code;
        }

        private static readonly ConcurrentDictionary<string, Dictionary<string, string>> DecodeMapCache =
            new ConcurrentDictionary<string, Dictionary<string, string>>(StringComparer.OrdinalIgnoreCase);

        /// <summary>AD_Ref_List(_Trl) Code -&gt; Label map for one column, cached per column+language (recently-lapsed.queries.md A.6). Never a hard-coded A/M -&gt; Auto/Manual map.</summary>
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

        /// <summary>
        /// The tenant's accounting-schema (base) currency - AD_ClientInfo -&gt;
        /// C_AcctSchema1 -&gt; C_Currency, the same resolution the sibling
        /// Service Contracts KPIs (VAS_243/244/245/246/258/259) already use.
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

        private class RecentlyLapsedSummary
        {
            public int LapsedCount { get; set; }
            public decimal ValueLostBase { get; set; }
            public string CurrencyIso { get; set; }
            public string CurrencySymbol { get; set; }
            public int CurrencyPrecision { get; set; }
        }

        private class RecentlyLapsedDrillResult
        {
            public List<RecentlyLapsedDrillRow> Rows { get; set; }
            public int Total { get; set; }
        }

        private class RecentlyLapsedDrillRow
        {
            public int ContractId { get; set; }
            public string DocumentNo { get; set; }
            public string CustomerName { get; set; }
            public string ProductName { get; set; }
            public string RenewalTypeCode { get; set; }
            public string RenewalTypeLabel { get; set; }
            public decimal GrandTotal { get; set; }
            public string CurrencyIso { get; set; }
            public string CurrencySymbol { get; set; }
            public int CurrencyPrecision { get; set; }
            public string EndDate { get; set; }
            public int DaysToEnd { get; set; }
        }
    }
}
