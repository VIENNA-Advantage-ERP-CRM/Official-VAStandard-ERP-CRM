/******************************************************
 * Module Name    : VAS
 * Purpose        : Service Contracts module Billing progress widget endpoints
 * chronological  : Development
 * Created Date   : 2026-09-09
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
    /// Module Name : VAS_266_BillingProgressWidget
    /// Purpose     : Data endpoints for the c3 x r2 "Billing progress"
    ///               read-only summary widget on the Service Contracts
    ///               dashboard: (1) the single summary rollup - billed vs
    ///               unbilled value (base currency) across the live
    ///               portfolio, the unbilled-contracts count and the
    ///               billing-overdue-contracts count (percent billed is
    ///               computed here in C#, never in SQL, per the build
    ///               spec), and (2) the three drill lists the legend/stat
    ///               tiles open (unbilled / billing overdue / billed),
    ///               each paged @7 with its own "Open in browser" IN-list.
    ///               Set = live (co.Processed='Y' AND IsCancel='N' AND
    ///               EndDate&gt;=CURRENT_DATE) contracts joined to the sole
    ///               sanctioned money-rollup view CR_SERVICECONTRACT_V;
    ///               billing-overdue is derived from the C_ContractSchedule
    ///               child (C_Invoice_ID IS NULL AND FROMDATE &lt;
    ///               CURRENT_DATE), never a hardcoded schedule/status code.
    ///               Every summed/drilled amount goes through
    ///               CurrencyConvert to base currency. A drill row's detail
    ///               (Renew / Generate invoice actions) reuses the already-
    ///               built VAS_241_ContractSearchWidget/GetContract,
    ///               RunRenew and RunGenerateInvoice endpoints rather than
    ///               duplicating that logic - the same shared-endpoint reuse
    ///               VAS_120 set with VAS_126 and
    ///               VAS_244/245/246/258/259/264/265 already reused too.
    ///               Read-only here; no writes.
    /// Chronological development:
    ///   VAI052      2026-09-09 Created
    ///   VAI052      2026-09-09 "Live" reads co.Processed='Y' instead of
    ///                          co.DocStatus=:DocStatus_CO - the Service
    ///                          Contract window itself does not rely on
    ///                          DocStatus for this (verified against real
    ///                          data, the same fix already applied to
    ///                          VAS_243/244/245/246/258/259/260/261/262/263/264/265).
    /// </summary>
    public class VAS_266_BillingProgressWidgetController : Controller
    {
        private static readonly VLogger Log = VLogger.GetVLogger(typeof(VAS_266_BillingProgressWidgetController).FullName);

        private const int DrillPageSize = 7;

        // Active/Expiring split for the drill list's own "Status" column
        // (matches VAS_260's own ExpiringWindowDays) - every row this widget
        // ever drills into is already live (LiveWhereSql), so only these two
        // bands can ever appear here (never Draft/Expired/Cancelled).
        private const int ExpiringWindowDays = 90;

        // Upper bound for the "Open in browser" TabWhereClause IN-list (GetContractIdsData) -
        // each drill's live population is naturally small; this just bounds a pathological case.
        private const int MaxZoomIds = 1000;

        private const string DrillUnbilled = "unbilled";
        private const string DrillOverdue = "overdue";
        private const string DrillBilled = "billed";

        private static readonly HashSet<string> DrillKeys = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            DrillUnbilled, DrillOverdue, DrillBilled
        };

        /// <summary>
        /// The single summary rollup: billed/unbilled totals (base
        /// currency), percent billed (computed here in C#, never in SQL)
        /// and the two drill counts.
        /// </summary>
        /// <returns>JSON { BilledBase, UnbilledBase, PctBilled, UnbilledContracts, BillingOverdueContracts, currency } or { Error }.</returns>
        [AjaxAuthorizeAttribute]
        [AjaxSessionFilterAttribute]
        public JsonResult GetBillingProgressSummary()
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
                Log.Log(Level.SEVERE, "VAS_266_BillingProgressWidget.GetBillingProgressSummary", ex);
                return ErrorResult(ctx);
            }
        }

        /// <summary>
        /// Paged rows for one of the three drill lists (unbilled / overdue /
        /// billed) the legend and stat tiles open.
        /// </summary>
        /// <param name="kind">"unbilled", "overdue" or "billed" - anything else falls back to "unbilled".</param>
        /// <param name="offset">Zero-based paging offset.</param>
        /// <param name="limit">Page size (capped at <see cref="MaxZoomIds"/>).</param>
        /// <returns>JSON { Rows:[...], Total } or { Error }.</returns>
        [AjaxAuthorizeAttribute]
        [AjaxSessionFilterAttribute]
        public JsonResult GetBillingProgressDrill(string kind, int offset = 0, int limit = DrillPageSize)
        {
            if (Session["ctx"] == null) { return Json("", JsonRequestBehavior.AllowGet); }

            Ctx ctx = Session["ctx"] as Ctx;

            string safeKind = DrillKeys.Contains(kind ?? "") ? kind : DrillUnbilled;
            if (offset < 0) { offset = 0; }
            if (limit <= 0 || limit > MaxZoomIds) { limit = DrillPageSize; }

            try
            {
                string json = JsonConvert.SerializeObject(GetDrillData(ctx, safeKind, offset, limit));
                return Json(json, JsonRequestBehavior.AllowGet);
            }
            catch (Exception ex)
            {
                Log.Log(Level.SEVERE, "VAS_266_BillingProgressWidget.GetBillingProgressDrill(" + safeKind + ")", ex);
                return ErrorResult(ctx);
            }
        }

        /// <summary>
        /// Every matching C_Contract_ID for one drill kind (not just one
        /// page), capped at <see cref="MaxZoomIds"/> - so that drill's "Open
        /// in browser" can filter the Service Contract window down to
        /// exactly its population via a TabWhereClause IN-list.
        /// </summary>
        /// <param name="kind">"unbilled", "overdue" or "billed" - anything else falls back to "unbilled".</param>
        /// <returns>JSON { Ids:[...] } or { Error }.</returns>
        [AjaxAuthorizeAttribute]
        [AjaxSessionFilterAttribute]
        public JsonResult GetBillingProgressDrillIds(string kind)
        {
            if (Session["ctx"] == null) { return Json("", JsonRequestBehavior.AllowGet); }

            Ctx ctx = Session["ctx"] as Ctx;
            string safeKind = DrillKeys.Contains(kind ?? "") ? kind : DrillUnbilled;

            try
            {
                string json = JsonConvert.SerializeObject(new { Ids = GetContractIdsData(ctx, safeKind) });
                return Json(json, JsonRequestBehavior.AllowGet);
            }
            catch (Exception ex)
            {
                Log.Log(Level.SEVERE, "VAS_266_BillingProgressWidget.GetBillingProgressDrillIds(" + safeKind + ")", ex);
                return ErrorResult(ctx);
            }
        }

        /// <summary>
        /// The shared live-portfolio predicate, WHERE-only - every caller
        /// applies MRole to alias "co" itself right after calling this.
        /// "Live" is Processed='Y' (not
        /// DocStatus=MContract.DOCSTATUS_Completed - the Service Contract
        /// window does not rely on that column, verified against real data,
        /// the same fix already applied to
        /// VAS_243/244/245/246/258/259/260/261/262/263/264/265) AND
        /// IsCancel='N' AND EndDate &gt;= CURRENT_DATE.
        /// </summary>
        /// <returns>WHERE-clause text (binds @AD_Client_ID).</returns>
        private static string LiveWhereSql()
        {
            return @"
                   co.AD_Client_ID = @AD_Client_ID
               AND co.IsActive = 'Y'
               AND co.Processed = 'Y'
               AND co.IsCancel = 'N'
               AND co.EndDate >= CURRENT_DATE";
        }

        /// <summary>The correlated billing-overdue EXISTS probe (billing-progress.queries.md A.7) - an uninvoiced C_ContractSchedule period whose FROMDATE is already past.</summary>
        private static string BillingOverdueExistsSql()
        {
            return @"
                EXISTS ( SELECT 1 FROM C_ContractSchedule cs
                          WHERE cs.C_Contract_ID = co.C_Contract_ID
                            AND cs.C_Invoice_ID IS NULL
                            AND cs.FROMDATE < CURRENT_DATE
                            AND cs.AD_Client_ID = co.AD_Client_ID
                            AND cs.IsActive = 'Y' )";
        }

        /// <summary>
        /// Fresh parameter array for one command execution against
        /// <see cref="LiveWhereSql"/> - every placeholder occurs exactly
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

        /// <summary>Resolves the summary rollup, percent billed computed here in C# (never in SQL) with a divide-by-zero guard.</summary>
        /// <param name="ctx">Session context.</param>
        private BillingProgressSummary GetSummaryData(Ctx ctx)
        {
            BillingProgressSummary result = new BillingProgressSummary();
            if (ctx == null) { return result; }

            int baseCurrencyId; string baseIso, baseSymbol; int basePrecision;
            ResolveBaseCurrency(ctx, out baseCurrencyId, out baseIso, out baseSymbol, out basePrecision);

            result.CurrencyIso = baseIso;
            result.CurrencySymbol = baseSymbol;
            result.CurrencyPrecision = basePrecision;

            string sql = @"
                SELECT SUM( CurrencyConvert(v.BILLED_AMOUNT, co.C_Currency_ID, @BaseCurrency_ID,
                              CURRENT_DATE, co.C_ConversionType_ID, co.AD_Client_ID, co.AD_Org_ID) ) AS Billed_Base,
                       SUM( CurrencyConvert(v.UNBILLED_AMT, co.C_Currency_ID, @BaseCurrency_ID,
                              CURRENT_DATE, co.C_ConversionType_ID, co.AD_Client_ID, co.AD_Org_ID) ) AS Unbilled_Base,
                       SUM( CASE WHEN v.UNBILLED_AMT > 0 THEN 1 ELSE 0 END )                          AS Unbilled_Contracts,
                       SUM( CASE WHEN " + BillingOverdueExistsSql() + @" THEN 1 ELSE 0 END )          AS Billing_Overdue_Contracts
                  FROM C_Contract co
                  LEFT OUTER JOIN CR_SERVICECONTRACT_V v ON ( v.C_Contract_ID = co.C_Contract_ID )
                 WHERE " + LiveWhereSql();

            sql = MRole.GetDefault(ctx).AddAccessSQL(sql, "co", MRole.SQL_FULLYQUALIFIED, MRole.SQL_RO);

            IDataReader dr = null;
            try
            {
                List<SqlParameter> parameters = new List<SqlParameter>(BuildPredicateParameters(ctx));
                parameters.Add(new SqlParameter("@BaseCurrency_ID", baseCurrencyId));

                dr = DB.ExecuteReader(sql, parameters.ToArray());
                if (dr != null && dr.Read())
                {
                    result.BilledBase = dr["Billed_Base"] == DBNull.Value ? 0m : Util.GetValueOfDecimal(dr["Billed_Base"]);
                    result.UnbilledBase = dr["Unbilled_Base"] == DBNull.Value ? 0m : Util.GetValueOfDecimal(dr["Unbilled_Base"]);
                    result.UnbilledContracts = dr["Unbilled_Contracts"] == DBNull.Value ? 0 : Util.GetValueOfInt(dr["Unbilled_Contracts"]);
                    result.BillingOverdueContracts = dr["Billing_Overdue_Contracts"] == DBNull.Value ? 0 : Util.GetValueOfInt(dr["Billing_Overdue_Contracts"]);
                }
            }
            finally
            {
                CloseReader(dr);
            }

            decimal total = result.BilledBase + result.UnbilledBase;
            result.PctBilled = total > 0m ? (int)Math.Round(result.BilledBase / total * 100m, MidpointRounding.AwayFromZero) : 0;

            return result;
        }

        /// <summary>
        /// Paged rows for one drill kind (billing-progress.queries.md D-2/
        /// D-3/D-4 supply the WHERE/ORDER; the display shape itself is the
        /// combined dashboard mock's own universal drill-list row - Contract
        /// / Value / Ends / Renewal / Status - the same shape every other
        /// Service-Contracts drill list in this dashboard uses, not the
        /// narrower per-kind money/date-only shape the queries.md SELECT
        /// lists literally show (2026-09-09 fix - the mock's own rendered
        /// list dialogs for "Unbilled contracts" / "Billing overdue" both
        /// carry Value/Ends/Renewal/Status columns that the original build
        /// omitted). "Value" is the contract's own GrandTotal (base
        /// currency), matching the mock's c.grandTotal - not the drill's
        /// Unbilled/Billed figure, which only drives the WHERE/ORDER here,
        /// never a displayed column. The base SQL is WHERE-only until MRole
        /// is applied to alias "co", then ORDER BY / OFFSET..FETCH are
        /// appended.
        /// </summary>
        /// <param name="ctx">Session context.</param>
        /// <param name="kind">One of <see cref="DrillKeys"/> (already validated by the caller).</param>
        /// <param name="offset">Zero-based paging offset.</param>
        /// <param name="limit">Page size.</param>
        private BillingProgressDrillResult GetDrillData(Ctx ctx, string kind, int offset, int limit)
        {
            BillingProgressDrillResult result = new BillingProgressDrillResult { Rows = new List<BillingProgressDrillRow>() };
            if (ctx == null) { return result; }

            bool isOverdue = string.Equals(kind, DrillOverdue, StringComparison.OrdinalIgnoreCase);
            bool isBilled = string.Equals(kind, DrillBilled, StringComparison.OrdinalIgnoreCase);

            int baseCurrencyId; string baseIso, baseSymbol; int basePrecision;
            ResolveBaseCurrency(ctx, out baseCurrencyId, out baseIso, out baseSymbol, out basePrecision);

            string language = GetLanguage(ctx);
            Dictionary<string, string> renewalTypeMap = GetDecodeMap("C_Contract", "RenewalType", language);

            string commonSelect = @"
                       co.C_Contract_ID      AS Contract_Id,
                       co.DocumentNo         AS Document_No,
                       COALESCE(bp.Name, N'') AS Customer_Name,
                       COALESCE(pr.Name, N'') AS Product_Name,
                       co.RenewalType        AS Renewal_Type_Code,
                       co.EndDate            AS End_Date,
                       CAST(DAYSBETWEEN(co.EndDate, CURRENT_DATE) AS INTEGER) AS Days_To_End,
                       CurrencyConvert(co.GrandTotal, co.C_Currency_ID, @BaseCurrency_ID,
                              CURRENT_DATE, co.C_ConversionType_ID, co.AD_Client_ID, co.AD_Org_ID) AS Grand_Total_Base";

            string baseSql;
            string orderSql;

            if (isOverdue)
            {
                baseSql = @"
                    SELECT " + commonSelect + @",
                           ( SELECT MIN(cs.FROMDATE) FROM C_ContractSchedule cs
                              WHERE cs.C_Contract_ID = co.C_Contract_ID
                                AND cs.C_Invoice_ID IS NULL
                                AND cs.FROMDATE < CURRENT_DATE
                                AND cs.AD_Client_ID = co.AD_Client_ID
                                AND cs.IsActive = 'Y' )         AS Sort_Key
                      FROM C_Contract co
                      LEFT OUTER JOIN C_BPartner bp ON ( bp.C_BPartner_ID = co.C_BPartner_ID )
                      LEFT OUTER JOIN M_Product  pr ON ( pr.M_Product_ID  = co.M_Product_ID )
                     WHERE " + LiveWhereSql() + @"
                       AND " + BillingOverdueExistsSql();

                orderSql = "ORDER BY Sort_Key ASC";
            }
            else
            {
                string amountFilter = isBilled ? "v.BILLED_AMOUNT > 0" : "v.UNBILLED_AMT > 0";
                string sortExpr = isBilled ? "v.BILLED_AMOUNT" : "v.UNBILLED_AMT";

                baseSql = @"
                    SELECT " + commonSelect + @",
                           " + sortExpr + @" AS Sort_Key
                      FROM C_Contract co
                      LEFT OUTER JOIN CR_SERVICECONTRACT_V v ON ( v.C_Contract_ID = co.C_Contract_ID )
                      LEFT OUTER JOIN C_BPartner bp ON ( bp.C_BPartner_ID = co.C_BPartner_ID )
                      LEFT OUTER JOIN M_Product  pr ON ( pr.M_Product_ID  = co.M_Product_ID )
                     WHERE " + LiveWhereSql() + @"
                       AND " + amountFilter;

                orderSql = "ORDER BY Sort_Key DESC";
            }

            baseSql = MRole.GetDefault(ctx).AddAccessSQL(baseSql, "co", MRole.SQL_FULLYQUALIFIED, MRole.SQL_RO);

            string listSql = baseSql + " " + orderSql + " OFFSET @Off ROWS FETCH NEXT @Lim ROWS ONLY";
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
                    int daysToEnd = dr["Days_To_End"] == DBNull.Value ? 0 : Util.GetValueOfInt(dr["Days_To_End"]);
                    DateTime? endDate = dr["End_Date"] == DBNull.Value ? (DateTime?)null : Util.GetValueOfDateTime(dr["End_Date"]);

                    result.Rows.Add(new BillingProgressDrillRow
                    {
                        ContractId = Util.GetValueOfInt(dr["Contract_Id"]),
                        DocumentNo = Util.GetValueOfString(dr["Document_No"]),
                        CustomerName = Util.GetValueOfString(dr["Customer_Name"]),
                        ProductName = Util.GetValueOfString(dr["Product_Name"]),
                        RenewalTypeCode = renewalTypeCode,
                        RenewalTypeLabel = DecodeLabel(renewalTypeMap, renewalTypeCode),
                        EndDate = endDate.HasValue ? endDate.Value.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture) : "",
                        DaysToEnd = daysToEnd,
                        // Derived, not stored (billing-progress.queries.md A.7) - every row
                        // here is already live, so only these two bands can ever appear.
                        LifecycleStatusCode = daysToEnd <= ExpiringWindowDays ? "EXPIRING" : "ACTIVE",
                        GrandTotalBase = dr["Grand_Total_Base"] == DBNull.Value ? 0m : Util.GetValueOfDecimal(dr["Grand_Total_Base"]),
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
        /// Every C_Contract_ID matching one drill kind, in the same order as
        /// <see cref="GetDrillData"/>, capped at <see cref="MaxZoomIds"/>.
        /// Backs that drill's "Open in browser" TabWhereClause IN-list - see
        /// <see cref="GetBillingProgressDrillIds"/>.
        /// </summary>
        /// <param name="ctx">Session context.</param>
        /// <param name="kind">One of <see cref="DrillKeys"/> (already validated by the caller).</param>
        private List<int> GetContractIdsData(Ctx ctx, string kind)
        {
            List<int> ids = new List<int>();
            if (ctx == null) { return ids; }

            bool isOverdue = string.Equals(kind, DrillOverdue, StringComparison.OrdinalIgnoreCase);

            string sql;
            string orderSql;

            if (isOverdue)
            {
                sql = @"
                    SELECT co.C_Contract_ID AS Contract_Id,
                           ( SELECT MIN(cs.FROMDATE) FROM C_ContractSchedule cs
                              WHERE cs.C_Contract_ID = co.C_Contract_ID
                                AND cs.C_Invoice_ID IS NULL
                                AND cs.FROMDATE < CURRENT_DATE
                                AND cs.AD_Client_ID = co.AD_Client_ID
                                AND cs.IsActive = 'Y' )         AS Oldest_Unbilled_From
                      FROM C_Contract co
                     WHERE " + LiveWhereSql() + @"
                       AND " + BillingOverdueExistsSql();

                orderSql = "ORDER BY Oldest_Unbilled_From ASC";
            }
            else
            {
                string amountFilter = string.Equals(kind, DrillBilled, StringComparison.OrdinalIgnoreCase)
                    ? "v.BILLED_AMOUNT > 0"
                    : "v.UNBILLED_AMT > 0";
                string orderExpr = string.Equals(kind, DrillBilled, StringComparison.OrdinalIgnoreCase)
                    ? "v.BILLED_AMOUNT"
                    : "v.UNBILLED_AMT";

                sql = @"
                    SELECT co.C_Contract_ID AS Contract_Id
                      FROM C_Contract co
                      LEFT OUTER JOIN CR_SERVICECONTRACT_V v ON ( v.C_Contract_ID = co.C_Contract_ID )
                     WHERE " + LiveWhereSql() + @"
                       AND " + amountFilter;

                orderSql = "ORDER BY " + orderExpr + " DESC";
            }

            sql = MRole.GetDefault(ctx).AddAccessSQL(sql, "co", MRole.SQL_FULLYQUALIFIED, MRole.SQL_RO);
            sql += " " + orderSql + " OFFSET 0 ROWS FETCH NEXT @MaxIds ROWS ONLY";

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

        /// <summary>AD_Ref_List(_Trl) Code -&gt; Label map for one column, cached per column+language - the same per-column decode-map pattern VAS_258/260 already use. Never a hard-coded A/M -&gt; Auto/Manual map.</summary>
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
        /// Service Contracts KPIs (VAS_243/244/245/246/258/259/264) already
        /// use.
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

        private class BillingProgressSummary
        {
            public decimal BilledBase { get; set; }
            public decimal UnbilledBase { get; set; }
            public int PctBilled { get; set; }
            public int UnbilledContracts { get; set; }
            public int BillingOverdueContracts { get; set; }
            public string CurrencyIso { get; set; }
            public string CurrencySymbol { get; set; }
            public int CurrencyPrecision { get; set; }
        }

        private class BillingProgressDrillResult
        {
            public List<BillingProgressDrillRow> Rows { get; set; }
            public int Total { get; set; }
        }

        private class BillingProgressDrillRow
        {
            public int ContractId { get; set; }
            public string DocumentNo { get; set; }
            public string CustomerName { get; set; }
            public string ProductName { get; set; }
            public string RenewalTypeCode { get; set; }
            public string RenewalTypeLabel { get; set; }
            public string EndDate { get; set; }
            public int DaysToEnd { get; set; }
            public string LifecycleStatusCode { get; set; }
            public decimal GrandTotalBase { get; set; }
            public string CurrencyIso { get; set; }
            public string CurrencySymbol { get; set; }
            public int CurrencyPrecision { get; set; }
        }
    }
}
