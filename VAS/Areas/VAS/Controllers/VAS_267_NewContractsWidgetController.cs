/******************************************************
 * Module Name    : VAS
 * Purpose        : Service Contracts module New contracts - this quarter widget endpoints
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
    /// Module Name : VAS_267_NewContractsWidget
    /// Purpose     : Data endpoints for the c3 x r2 "New contracts - this
    ///               quarter" portfolio-growth widget on the Service
    ///               Contracts dashboard: (1) the header banner - the COUNT
    ///               of newly-started contracts plus their summed base-
    ///               currency value, (2) the paged (@4 for the widget body,
    ///               @7 for the header "All" list modal per the build
    ///               spec's own distinct page sizes, newest-start first)
    ///               rows, and (3) every matching Contract_ID (capped) so
    ///               "Open in browser" can filter the Service Contract
    ///               window down to exactly this population via a
    ///               TabWhereClause IN-list. Set = completed
    ///               (co.Processed='Y') contracts whose StartDate falls in
    ///               the last rolling quarter (DAYSBETWEEN(StartDate,
    ///               CURRENT_DATE) BETWEEN 0 AND 90 - the lower bound
    ///               excludes future-dated starts). ContractType and
    ///               RenewalType are decoded via the AD_Ref_List cache
    ///               pattern VAS_258/260/266 already use; the "All" list
    ///               modal's own Status column is a derived lifecycle band
    ///               (Active/Expiring/Expired/Cancelled - Draft cannot occur
    ///               here since Processed='Y' is already guaranteed),
    ///               mirroring VAS_260's own precedence exactly. A row's
    ///               detail (Renew / Generate invoice actions) reuses the
    ///               already-built VAS_241_ContractSearchWidget/GetContract,
    ///               RunRenew and RunGenerateInvoice endpoints rather than
    ///               duplicating that logic - the same shared-endpoint reuse
    ///               VAS_120 set with VAS_126 and
    ///               VAS_244/245/246/258/259/264/265/266 already reused too.
    ///               Read-only here; no writes.
    /// Chronological development:
    ///   VAI052      2026-09-09 Created
    ///   VAI052      2026-09-09 "Completed" reads co.Processed='Y' instead of
    ///                          co.DocStatus=:DocStatus_CO - the Service
    ///                          Contract window itself does not rely on
    ///                          DocStatus for this (verified against real
    ///                          data, the same fix already applied to
    ///                          VAS_243/244/245/246/258/259/260/261/262/263/264/265/266).
    ///   VAI052      2026-09-09 The "All" list modal reuses the same 5-column
    ///                          shape (Contract / Value / Ends / Renewal /
    ///                          Status) every other Service-Contracts drill
    ///                          list in this dashboard shares - VAS_266's own
    ///                          list-modal fix (2026-09-09) established that
    ///                          the combined dashboard mock's shared
    ///                          openListItems()/renderList() always renders
    ///                          that shape regardless of which widget's "All"
    ///                          link opened it, so the row query here selects
    ///                          RenewalType/EndDate/DocStatus/IsCancel too,
    ///                          not just the fields the widget's own compact
    ///                          body row needs.
    /// </summary>
    public class VAS_267_NewContractsWidgetController : Controller
    {
        private static readonly VLogger Log = VLogger.GetVLogger(typeof(VAS_267_NewContractsWidgetController).FullName);

        // The rolling-quarter window for "new" (new-contracts.queries.md A.7).
        private const int NewWindowDays = 90;

        // Active/Expiring split for the derived Status band (matches VAS_260's own ExpiringWindowDays).
        private const int ExpiringWindowDays = 90;

        private const int BodyPageSize = 4;
        private const int ListPageSize = 7;

        // Upper bound for the "Open in browser" TabWhereClause IN-list (GetContractIdsData) -
        // this widget's population is naturally small; this just bounds a pathological case.
        private const int MaxZoomIds = 1000;

        /// <summary>
        /// Header banner scalars: the new-contract count and their summed
        /// base-currency value.
        /// </summary>
        /// <returns>JSON { NewCount, NewValueBase, currency } or { Error }.</returns>
        [AjaxAuthorizeAttribute]
        [AjaxSessionFilterAttribute]
        public JsonResult GetNewContractsSummary()
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
                Log.Log(Level.SEVERE, "VAS_267_NewContractsWidget.GetNewContractsSummary", ex);
                return ErrorResult(ctx);
            }
        }

        /// <summary>
        /// Paged new-contract rows, newest start first - the identical query
        /// backs both the widget's own always-visible @4 body and (at a
        /// larger limit) the header "All" list modal.
        /// </summary>
        /// <param name="offset">Zero-based paging offset.</param>
        /// <param name="limit">Page size - 4 for the widget body, 7 for the "All" list modal (capped at <see cref="MaxZoomIds"/>).</param>
        /// <returns>JSON { Rows:[...], Total } or { Error }.</returns>
        [AjaxAuthorizeAttribute]
        [AjaxSessionFilterAttribute]
        public JsonResult GetNewContractsContracts(int offset = 0, int limit = BodyPageSize)
        {
            if (Session["ctx"] == null) { return Json("", JsonRequestBehavior.AllowGet); }

            Ctx ctx = Session["ctx"] as Ctx;

            if (offset < 0) { offset = 0; }
            if (limit <= 0 || limit > MaxZoomIds) { limit = BodyPageSize; }

            try
            {
                string json = JsonConvert.SerializeObject(GetDrillData(ctx, offset, limit));
                return Json(json, JsonRequestBehavior.AllowGet);
            }
            catch (Exception ex)
            {
                Log.Log(Level.SEVERE, "VAS_267_NewContractsWidget.GetNewContractsContracts", ex);
                return ErrorResult(ctx);
            }
        }

        /// <summary>
        /// Every matching C_Contract_ID for the new-contract predicate (not
        /// just one page), capped at <see cref="MaxZoomIds"/> - so "Open in
        /// browser" can filter the Service Contract window down to exactly
        /// this widget's population via a TabWhereClause IN-list.
        /// </summary>
        /// <returns>JSON { Ids:[...] } or { Error }.</returns>
        [AjaxAuthorizeAttribute]
        [AjaxSessionFilterAttribute]
        public JsonResult GetNewContractsContractIds()
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
                Log.Log(Level.SEVERE, "VAS_267_NewContractsWidget.GetNewContractsContractIds", ex);
                return ErrorResult(ctx);
            }
        }

        /// <summary>
        /// The shared new-contract predicate, WHERE-only - every caller
        /// applies MRole to alias "co" itself right after calling this.
        /// "Completed" is Processed='Y' (not
        /// DocStatus=MContract.DOCSTATUS_Completed - the Service Contract
        /// window does not rely on that column, verified against real data,
        /// the same fix already applied to
        /// VAS_243/244/245/246/258/259/260/261/262/263/264/265/266); "new"
        /// additionally requires DAYSBETWEEN(StartDate, CURRENT_DATE)
        /// BETWEEN 0 AND <see cref="NewWindowDays"/> (excludes future-dated
        /// starts).
        /// </summary>
        /// <returns>WHERE-clause text (binds @AD_Client_ID, @NewWindowDays).</returns>
        private static string NewContractWhereSql()
        {
            return @"
                   co.AD_Client_ID = @AD_Client_ID
               AND co.IsActive = 'Y'
               AND co.Processed = 'Y'
               AND CAST(DAYSBETWEEN(CURRENT_DATE,co.StartDate) AS INTEGER) BETWEEN 0 AND @NewWindowDays";
        }

        /// <summary>
        /// Fresh parameter array for one command execution against
        /// <see cref="NewContractWhereSql"/> - every placeholder occurs
        /// exactly once in each assembled statement, so no name repeats
        /// under Oracle's positional binding.
        /// </summary>
        /// <param name="ctx">Session context.</param>
        private SqlParameter[] BuildPredicateParameters(Ctx ctx)
        {
            return new[]
            {
                new SqlParameter("@AD_Client_ID", ctx.GetAD_Client_ID()),
                new SqlParameter("@NewWindowDays", NewWindowDays)
            };
        }

        /// <summary>Resolves the new-contract count and summed base-currency value.</summary>
        /// <param name="ctx">Session context.</param>
        private NewContractsSummary GetSummaryData(Ctx ctx)
        {
            NewContractsSummary result = new NewContractsSummary();
            if (ctx == null) { return result; }

            int baseCurrencyId; string baseIso, baseSymbol; int basePrecision;
            ResolveBaseCurrency(ctx, out baseCurrencyId, out baseIso, out baseSymbol, out basePrecision);

            result.CurrencyIso = baseIso;
            result.CurrencySymbol = baseSymbol;
            result.CurrencyPrecision = basePrecision;

            string sql = @"
                SELECT COUNT(co.C_Contract_ID) AS New_Count,
                       SUM( CurrencyConvert(co.GrandTotal, co.C_Currency_ID, @BaseCurrency_ID,
                              CURRENT_DATE, co.C_ConversionType_ID, co.AD_Client_ID, co.AD_Org_ID) ) AS New_Value_Base
                  FROM C_Contract co
                 WHERE " + NewContractWhereSql();

            sql = MRole.GetDefault(ctx).AddAccessSQL(sql, "co", MRole.SQL_FULLYQUALIFIED, MRole.SQL_RO);

            IDataReader dr = null;
            try
            {
                List<SqlParameter> parameters = new List<SqlParameter>(BuildPredicateParameters(ctx));
                parameters.Add(new SqlParameter("@BaseCurrency_ID", baseCurrencyId));

                dr = DB.ExecuteReader(sql, parameters.ToArray());
                if (dr != null && dr.Read())
                {
                    result.NewCount = Util.GetValueOfInt(dr["New_Count"]);
                    result.NewValueBase = dr["New_Value_Base"] == DBNull.Value ? 0m : Util.GetValueOfDecimal(dr["New_Value_Base"]);
                }
            }
            finally
            {
                CloseReader(dr);
            }

            return result;
        }

        /// <summary>
        /// Paged new-contract rows, newest start first (new-contracts.
        /// queries.md D-2) - the base SQL is WHERE-only until MRole is
        /// applied to alias "co", then ORDER BY / OFFSET..FETCH are
        /// appended. The row shape also carries the fields the "All" list
        /// modal's shared 5-column display needs (RenewalType/EndDate/
        /// DocStatus/IsCancel), not just the widget's own compact body row -
        /// see the class file-header note.
        /// </summary>
        /// <param name="ctx">Session context.</param>
        /// <param name="offset">Zero-based paging offset.</param>
        /// <param name="limit">Page size.</param>
        private NewContractsDrillResult GetDrillData(Ctx ctx, int offset, int limit)
        {
            NewContractsDrillResult result = new NewContractsDrillResult { Rows = new List<NewContractsDrillRow>() };
            if (ctx == null) { return result; }

            int baseCurrencyId; string baseIso, baseSymbol; int basePrecision;
            ResolveBaseCurrency(ctx, out baseCurrencyId, out baseIso, out baseSymbol, out basePrecision);

            string language = GetLanguage(ctx);
            Dictionary<string, string> contractTypeMap = GetDecodeMap("C_Contract", "ContractType", language);
            Dictionary<string, string> renewalTypeMap = GetDecodeMap("C_Contract", "RenewalType", language);

            string baseSql = @"
                SELECT co.C_Contract_ID      AS Contract_Id,
                       co.DocumentNo         AS Document_No,
                       COALESCE(bp.Name, N'') AS Customer_Name,
                       COALESCE(pr.Name, N'') AS Product_Name,
                       co.ContractType       AS Contract_Type_Code,
                       co.RenewalType        AS Renewal_Type_Code,
                       co.DocStatus          AS Doc_Status_Code,
                       co.IsCancel           AS Is_Cancel,
                       co.StartDate          AS Start_Date,
                       co.EndDate            AS End_Date,
                       CAST(DAYSBETWEEN(CURRENT_DATE, co.StartDate) AS INTEGER) AS Started_Days_Ago,
                       CAST(DAYSBETWEEN(co.EndDate, CURRENT_DATE) AS INTEGER)   AS Days_To_End,
                       CurrencyConvert(co.GrandTotal, co.C_Currency_ID, @BaseCurrency_ID,
                              CURRENT_DATE, co.C_ConversionType_ID, co.AD_Client_ID, co.AD_Org_ID) AS Grand_Total_Base
                  FROM C_Contract co
                  LEFT OUTER JOIN C_BPartner bp ON ( bp.C_BPartner_ID = co.C_BPartner_ID )
                  LEFT OUTER JOIN M_Product  pr ON ( pr.M_Product_ID  = co.M_Product_ID )
                 WHERE " + NewContractWhereSql();

            baseSql = MRole.GetDefault(ctx).AddAccessSQL(baseSql, "co", MRole.SQL_FULLYQUALIFIED, MRole.SQL_RO);

            string listSql = baseSql + @"
                ORDER BY co.StartDate DESC
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
                    string contractTypeCode = Util.GetValueOfString(dr["Contract_Type_Code"]);
                    string renewalTypeCode = Util.GetValueOfString(dr["Renewal_Type_Code"]);
                    string docStatusCode = Util.GetValueOfString(dr["Doc_Status_Code"]);
                    string isCancel = Util.GetValueOfString(dr["Is_Cancel"]);
                    DateTime? endDate = dr["End_Date"] == DBNull.Value ? (DateTime?)null : Util.GetValueOfDateTime(dr["End_Date"]);
                    int daysToEnd = dr["Days_To_End"] == DBNull.Value ? 0 : Util.GetValueOfInt(dr["Days_To_End"]);

                    result.Rows.Add(new NewContractsDrillRow
                    {
                        ContractId = Util.GetValueOfInt(dr["Contract_Id"]),
                        DocumentNo = Util.GetValueOfString(dr["Document_No"]),
                        CustomerName = Util.GetValueOfString(dr["Customer_Name"]),
                        ProductName = Util.GetValueOfString(dr["Product_Name"]),
                        ContractTypeCode = contractTypeCode,
                        ContractTypeLabel = DecodeLabel(contractTypeMap, contractTypeCode),
                        RenewalTypeCode = renewalTypeCode,
                        RenewalTypeLabel = DecodeLabel(renewalTypeMap, renewalTypeCode),
                        StartedDaysAgo = dr["Started_Days_Ago"] == DBNull.Value ? 0 : Util.GetValueOfInt(dr["Started_Days_Ago"]),
                        EndDate = endDate.HasValue ? endDate.Value.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture) : "",
                        DaysToEnd = daysToEnd,
                        // Derived, not stored (mirrors VAS_260's own precedence) - Draft cannot
                        // occur here since NewContractWhereSql already requires Processed='Y'.
                        LifecycleStatusCode = ComputeLifecycleStatusCode(isCancel, docStatusCode, daysToEnd),
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

        /// <summary>Derived lifecycle band (mirrors VAS_260's own Cancelled -&gt; Expired -&gt; Expiring -&gt; Active precedence, minus Draft since Processed='Y' is already guaranteed by the caller's WHERE).</summary>
        /// <param name="isCancel">Raw C_Contract.IsCancel ('Y'/'N').</param>
        /// <param name="docStatusCode">Raw C_Contract.DocStatus.</param>
        /// <param name="daysToEnd">DAYSBETWEEN(CURRENT_DATE, EndDate).</param>
        private static string ComputeLifecycleStatusCode(string isCancel, string docStatusCode, int daysToEnd)
        {
            if (string.Equals(isCancel, "Y", StringComparison.OrdinalIgnoreCase)
                || string.Equals(docStatusCode, X_C_Contract.DOCSTATUS_Voided, StringComparison.OrdinalIgnoreCase)
                || string.Equals(docStatusCode, X_C_Contract.DOCSTATUS_Closed, StringComparison.OrdinalIgnoreCase))
            {
                return "CANCELLED";
            }
            if (daysToEnd < 0) { return "ENDED"; }
            if (daysToEnd <= ExpiringWindowDays) { return "EXPIRING"; }
            return "ACTIVE";
        }

        /// <summary>
        /// Every C_Contract_ID matching the new-contract predicate, newest
        /// start first, capped at <see cref="MaxZoomIds"/>. Backs "Open in
        /// browser"'s TabWhereClause IN-list - see
        /// <see cref="GetNewContractsContractIds"/>.
        /// </summary>
        /// <param name="ctx">Session context.</param>
        private List<int> GetContractIdsData(Ctx ctx)
        {
            List<int> ids = new List<int>();
            if (ctx == null) { return ids; }

            string sql = @"
                SELECT co.C_Contract_ID AS Contract_Id
                  FROM C_Contract co
                 WHERE " + NewContractWhereSql() + @"
                 ORDER BY co.StartDate DESC
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

        /// <summary>AD_Ref_List(_Trl) Code -&gt; Label map for one column, cached per column+language (new-contracts.queries.md D-3). Never a hard-coded map.</summary>
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
        /// Service Contracts KPIs (VAS_243/244/245/246/258/259/264/266)
        /// already use.
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

        private class NewContractsSummary
        {
            public int NewCount { get; set; }
            public decimal NewValueBase { get; set; }
            public string CurrencyIso { get; set; }
            public string CurrencySymbol { get; set; }
            public int CurrencyPrecision { get; set; }
        }

        private class NewContractsDrillResult
        {
            public List<NewContractsDrillRow> Rows { get; set; }
            public int Total { get; set; }
        }

        private class NewContractsDrillRow
        {
            public int ContractId { get; set; }
            public string DocumentNo { get; set; }
            public string CustomerName { get; set; }
            public string ProductName { get; set; }
            public string ContractTypeCode { get; set; }
            public string ContractTypeLabel { get; set; }
            public string RenewalTypeCode { get; set; }
            public string RenewalTypeLabel { get; set; }
            public int StartedDaysAgo { get; set; }
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
