/******************************************************
 * Module Name    : VAS
 * Purpose        : Service Contracts module Contracts by status distribution widget endpoints
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
    /// Module Name : VAS_260_ContractsByStatusWidget
    /// Purpose     : Data endpoints for the c3 x r2 "Contracts by status"
    ///               distribution widget on the Service Contracts dashboard: (1)
    ///               one row of five COUNT(CASE ...) band totals - Active /
    ///               Expiring (&lt;=90) / Draft / Expired / Cancelled - over five
    ///               mutually-exclusive DERIVED lifecycle bands (never a stored
    ///               column), (2) a paged (@7) drill list for whichever band the
    ///               user clicks, reusing that band's own CASE predicate as the
    ///               WHERE, and (3) every matching Contract_ID for a band (capped)
    ///               so "Open in browser" can filter the Service Contract window
    ///               down to exactly that band's population via a TabWhereClause
    ///               IN-list. Band precedence exactly matches
    ///               contracts-by-status.queries.md D.1/A.7: Cancelled
    ///               (IsCancel='Y' OR DocStatus IN (VO,CL)) -&gt; Draft
    ///               (Processed&lt;&gt;'Y') -&gt; Expired (Processed='Y', EndDate&lt;today, no
    ///               successor) -&gt; Expiring (Processed='Y', EndDate&gt;=today,
    ///               daysToEnd&lt;=90) -&gt; Active (Processed='Y', EndDate&gt;=today,
    ///               daysToEnd&gt;90). "Live/completed" reads Processed='Y' rather
    ///               than DocStatus=MContract.DOCSTATUS_Completed - the Service
    ///               Contract window itself does not rely on that column (verified
    ///               against real data, the same fix already applied to
    ///               VAS_243/244/245/246/258/259); the Voided/Closed codes used by
    ///               Cancelled/Draft remain bound framework constants
    ///               (X_C_Contract.DOCSTATUS_Voided/Closed), never literals in
    ///               prose. A completed, past-end contract that already has a live
    ///               successor (a correlated NOT EXISTS probe on
    ///               C_Contract.Ref_Contract_ID - never an MRole alias) falls into
    ///               no band, matching the reality that it has been renewed away.
    ///               The drill list's raw DocStatus is still decoded through the
    ///               tenant's own AD_Ref_List/AD_Ref_List_Trl dictionary for
    ///               display. MRole is applied to the single physical table alias
    ///               "co" only. A drill row's detail reuses the already-built
    ///               VAS_241_ContractSearchWidget/GetContract endpoint rather than
    ///               duplicating that logic - the same shared-endpoint reuse
    ///               VAS_120's Log-activity quick action already established
    ///               against VAS_126, and VAS_244/245/246/258/259 already reused
    ///               too. Read-only throughout; no writes.
    /// Chronological development:
    ///   VAI052      2026-09-08 Created
    ///   VAI052      2026-09-08 "Live/completed" now reads co.Processed='Y'
    ///                          instead of co.DocStatus=:DocStatus_Completed in
    ///                          every band predicate (Draft/Expired/Expiring/
    ///                          Active) and the summary COUNT(CASE...) query - the
    ///                          Service Contract window does not rely on
    ///                          DocStatus for this.
    /// </summary>
    public class VAS_260_ContractsByStatusWidgetController : Controller
    {
        private static readonly VLogger Log = VLogger.GetVLogger(typeof(VAS_260_ContractsByStatusWidgetController).FullName);

        // The product rule for the Expiring/Active split (contracts-by-status.queries.md A.7/C).
        private const int ExpiringWindowDays = 90;

        private const int DrillPageSize = 7;

        // Upper bound for the "Open in browser" TabWhereClause IN-list (GetContractIdsData) -
        // a single band's population is naturally small; this just bounds a pathological case.
        private const int MaxZoomIds = 1000;

        // The five sanctioned band keys - validated against this whitelist before ever
        // reaching BandWhereSql, so an unrecognised key can never select an unintended predicate.
        private static readonly HashSet<string> BandKeys = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            "Active", "Expiring", "Draft", "Expired", "Cancelled"
        };

        /// <summary>
        /// Distribution scalars: the five mutually-exclusive band counts plus the
        /// portfolio total (summed in C# from the five, per the widget's own bar-
        /// denominator rule).
        /// </summary>
        /// <returns>JSON { ActiveCount, ExpiringCount, DraftCount, ExpiredCount, CancelledCount, Total } or { Error }.</returns>
        [AjaxAuthorizeAttribute]
        [AjaxSessionFilterAttribute]
        public JsonResult GetContractsByStatusSummary()
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
                Log.Log(Level.SEVERE, "VAS_260_ContractsByStatusWidget.GetContractsByStatusSummary", ex);
                return ErrorResult(ctx);
            }
        }

        /// <summary>
        /// Paged drill rows for one clicked band, using that band's own CASE
        /// predicate as the WHERE (contracts-by-status.queries.md D-2).
        /// </summary>
        /// <param name="band">One of Active/Expiring/Draft/Expired/Cancelled.</param>
        /// <param name="offset">Zero-based paging offset.</param>
        /// <param name="limit">Page size (capped at <see cref="MaxZoomIds"/>).</param>
        /// <returns>JSON { Rows:[...], Total } or { Error }.</returns>
        [AjaxAuthorizeAttribute]
        [AjaxSessionFilterAttribute]
        public JsonResult GetContractsByStatusDrill(string band, int offset = 0, int limit = DrillPageSize)
        {
            if (Session["ctx"] == null) { return Json("", JsonRequestBehavior.AllowGet); }

            Ctx ctx = Session["ctx"] as Ctx;

            if (!BandKeys.Contains(band ?? "")) { return ErrorResult(ctx); }
            if (offset < 0) { offset = 0; }
            if (limit <= 0 || limit > MaxZoomIds) { limit = DrillPageSize; }

            try
            {
                string json = JsonConvert.SerializeObject(GetDrillData(ctx, band, offset, limit));
                return Json(json, JsonRequestBehavior.AllowGet);
            }
            catch (Exception ex)
            {
                Log.Log(Level.SEVERE, "VAS_260_ContractsByStatusWidget.GetContractsByStatusDrill", ex);
                return ErrorResult(ctx);
            }
        }

        /// <summary>
        /// Every matching C_Contract_ID for one band (not just one page), capped at
        /// <see cref="MaxZoomIds"/> - so "Open in browser" can filter the Service
        /// Contract window down to exactly that band's population via a
        /// TabWhereClause IN-list.
        /// </summary>
        /// <param name="band">One of Active/Expiring/Draft/Expired/Cancelled.</param>
        /// <returns>JSON { Ids:[...] } or { Error }.</returns>
        [AjaxAuthorizeAttribute]
        [AjaxSessionFilterAttribute]
        public JsonResult GetContractsByStatusIds(string band)
        {
            if (Session["ctx"] == null) { return Json("", JsonRequestBehavior.AllowGet); }

            Ctx ctx = Session["ctx"] as Ctx;

            if (!BandKeys.Contains(band ?? "")) { return ErrorResult(ctx); }

            try
            {
                string json = JsonConvert.SerializeObject(new { Ids = GetContractIdsData(ctx, band) });
                return Json(json, JsonRequestBehavior.AllowGet);
            }
            catch (Exception ex)
            {
                Log.Log(Level.SEVERE, "VAS_260_ContractsByStatusWidget.GetContractsByStatusIds", ex);
                return ErrorResult(ctx);
            }
        }

        /// <summary>
        /// One band's own WHERE-only predicate (contracts-by-status.queries.md
        /// D-2's per-band block) - every caller applies MRole to alias "co" itself
        /// right after calling this. <paramref name="band"/> must already be
        /// validated against <see cref="BandKeys"/> by the caller.
        /// </summary>
        /// <param name="band">One of Active/Expiring/Draft/Expired/Cancelled.</param>
        /// <returns>WHERE-clause text (binds a subset of @AD_Client_ID, @DocStatus_Voided, @DocStatus_Closed, @ExpiringWindowDays depending on the band).</returns>
        private static string BandWhereSql(string band)
        {
            string tenantScope = "co.AD_Client_ID = @AD_Client_ID AND co.IsActive = 'Y'";

            if (string.Equals(band, "Cancelled", StringComparison.OrdinalIgnoreCase))
            {
                return tenantScope + @"
               AND ( co.IsCancel = 'Y' OR co.DocStatus IN ( @DocStatus_Voided, @DocStatus_Closed ) )";
            }

            if (string.Equals(band, "Draft", StringComparison.OrdinalIgnoreCase))
            {
                return tenantScope + @"
               AND co.IsCancel = 'N'
               AND co.DocStatus NOT IN ( @DocStatus_Voided, @DocStatus_Closed )
               AND co.Processed <> 'Y'";
            }

            if (string.Equals(band, "Expired", StringComparison.OrdinalIgnoreCase))
            {
                return tenantScope + @"
               AND co.IsCancel = 'N'
               AND co.Processed = 'Y'
               AND co.EndDate < CURRENT_DATE
               AND NOT EXISTS (
                     SELECT 1 FROM C_Contract succ
                      WHERE succ.Ref_Contract_ID = co.C_Contract_ID
                        AND succ.AD_Client_ID = co.AD_Client_ID
                        AND succ.IsActive = 'Y' )";
            }

            if (string.Equals(band, "Expiring", StringComparison.OrdinalIgnoreCase))
            {
                return tenantScope + @"
               AND co.IsCancel = 'N'
               AND co.Processed = 'Y'
               AND co.EndDate >= CURRENT_DATE
               AND DAYSBETWEEN(co.EndDate, CURRENT_DATE) <= @ExpiringWindowDays";
            }

            // Active (the default/fifth band).
            return tenantScope + @"
               AND co.IsCancel = 'N'
               AND co.Processed = 'Y'
               AND co.EndDate >= CURRENT_DATE
               AND DAYSBETWEEN(co.EndDate, CURRENT_DATE) > @ExpiringWindowDays";
        }

        /// <summary>
        /// Fresh parameter array for one command execution - every bind referenced
        /// by any <see cref="BandWhereSql"/> branch is always supplied (an unused
        /// declared parameter for a band whose predicate does not reference it is
        /// harmless), and each occurs exactly once in the assembled statement text,
        /// so no name repeats under Oracle's positional binding.
        /// </summary>
        /// <param name="ctx">Session context.</param>
        private SqlParameter[] BuildPredicateParameters(Ctx ctx)
        {
            return new[]
            {
                new SqlParameter("@AD_Client_ID", ctx.GetAD_Client_ID()),
                new SqlParameter("@DocStatus_Voided", X_C_Contract.DOCSTATUS_Voided),
                new SqlParameter("@DocStatus_Closed", X_C_Contract.DOCSTATUS_Closed),
                new SqlParameter("@ExpiringWindowDays", ExpiringWindowDays)
            };
        }

        /// <summary>
        /// Resolves the five band counts in one COUNT(CASE ...) query
        /// (contracts-by-status.queries.md D-1), then sums them in C# for the
        /// portfolio total the bar denominator uses.
        /// </summary>
        /// <param name="ctx">Session context.</param>
        private DistributionSummary GetSummaryData(Ctx ctx)
        {
            DistributionSummary result = new DistributionSummary();
            if (ctx == null) { return result; }

            // The DocStatus_Voided/Closed binds are each repeated across two CASE
            // WHEN branches - each occurrence gets its OWN uniquely-suffixed
            // parameter name (same underlying value) rather than reusing one name,
            // so this still binds correctly under Oracle's positional parameter
            // resolution (the same rule this codebase's other multi-occurrence
            // queries follow - see VAS_246's dual CurrencyConvert
            // @BaseCurrency_ID_1/_2). "Live/completed" reads co.Processed='Y' - a
            // plain literal, not a bound code - instead of DocStatus=
            // MContract.DOCSTATUS_Completed: the Service Contract window itself
            // does not rely on that column (verified against real data, the same
            // fix already applied to VAS_243/244/245/246/258/259).
            string sql = @"
                SELECT
                  COUNT(CASE WHEN ( co.IsCancel = 'Y'
                                    OR co.DocStatus IN ( @DocStatus_Voided_1, @DocStatus_Closed_1 ) )
                             THEN 1 END) AS Cancelled_Count,
                  COUNT(CASE WHEN co.IsCancel = 'N'
                                  AND co.DocStatus NOT IN ( @DocStatus_Voided_2, @DocStatus_Closed_2 )
                                  AND co.Processed <> 'Y'
                             THEN 1 END) AS Draft_Count,
                  COUNT(CASE WHEN co.IsCancel = 'N'
                                  AND co.Processed = 'Y'
                                  AND co.EndDate < CURRENT_DATE
                                  AND NOT EXISTS ( SELECT 1 FROM C_Contract succ
                                                    WHERE succ.Ref_Contract_ID = co.C_Contract_ID
                                                      AND succ.AD_Client_ID = co.AD_Client_ID
                                                      AND succ.IsActive = 'Y' )
                             THEN 1 END) AS Expired_Count,
                  COUNT(CASE WHEN co.IsCancel = 'N'
                                  AND co.Processed = 'Y'
                                  AND co.EndDate >= CURRENT_DATE
                                  AND DAYSBETWEEN(co.EndDate, CURRENT_DATE) <= @ExpiringWindowDays_1
                             THEN 1 END) AS Expiring_Count,
                  COUNT(CASE WHEN co.IsCancel = 'N'
                                  AND co.Processed = 'Y'
                                  AND co.EndDate >= CURRENT_DATE
                                  AND DAYSBETWEEN(co.EndDate, CURRENT_DATE) > @ExpiringWindowDays_2
                             THEN 1 END) AS Active_Count
                  FROM C_Contract co
                 WHERE co.AD_Client_ID = @AD_Client_ID AND co.IsActive = 'Y'";

            sql = MRole.GetDefault(ctx).AddAccessSQL(sql, "co", MRole.SQL_FULLYQUALIFIED, MRole.SQL_RO);

            IDataReader dr = null;
            try
            {
                dr = DB.ExecuteReader(sql, new[]
                {
                    new SqlParameter("@AD_Client_ID", ctx.GetAD_Client_ID()),
                    new SqlParameter("@DocStatus_Voided_1", X_C_Contract.DOCSTATUS_Voided),
                    new SqlParameter("@DocStatus_Closed_1", X_C_Contract.DOCSTATUS_Closed),
                    new SqlParameter("@DocStatus_Voided_2", X_C_Contract.DOCSTATUS_Voided),
                    new SqlParameter("@DocStatus_Closed_2", X_C_Contract.DOCSTATUS_Closed),
                    new SqlParameter("@ExpiringWindowDays_1", ExpiringWindowDays),
                    new SqlParameter("@ExpiringWindowDays_2", ExpiringWindowDays)
                });
                if (dr != null && dr.Read())
                {
                    result.ActiveCount = Util.GetValueOfInt(dr["Active_Count"]);
                    result.ExpiringCount = Util.GetValueOfInt(dr["Expiring_Count"]);
                    result.DraftCount = Util.GetValueOfInt(dr["Draft_Count"]);
                    result.ExpiredCount = Util.GetValueOfInt(dr["Expired_Count"]);
                    result.CancelledCount = Util.GetValueOfInt(dr["Cancelled_Count"]);
                }
            }
            finally
            {
                CloseReader(dr);
            }

            result.Total = result.ActiveCount + result.ExpiringCount + result.DraftCount + result.ExpiredCount + result.CancelledCount;
            return result;
        }

        /// <summary>
        /// Paged drill rows for one band, ended-soonest first (contracts-by-
        /// status.queries.md D-2) - the base SQL is WHERE-only until MRole is
        /// applied to alias "co", then ORDER BY / OFFSET..FETCH are appended.
        /// </summary>
        /// <param name="ctx">Session context.</param>
        /// <param name="band">One of Active/Expiring/Draft/Expired/Cancelled (already validated).</param>
        /// <param name="offset">Zero-based paging offset.</param>
        /// <param name="limit">Page size.</param>
        private DistributionDrillResult GetDrillData(Ctx ctx, string band, int offset, int limit)
        {
            DistributionDrillResult result = new DistributionDrillResult { Rows = new List<DistributionDrillRow>() };
            if (ctx == null) { return result; }

            string language = GetLanguage(ctx);
            Dictionary<string, string> docStatusMap = GetDecodeMap("C_Contract", "DocStatus", language);
            Dictionary<string, string> renewalTypeMap = GetDecodeMap("C_Contract", "RenewalType", language);

            int baseCurrencyId; string baseIso, baseSymbol; int basePrecision;
            ResolveBaseCurrency(ctx, out baseCurrencyId, out baseIso, out baseSymbol, out basePrecision);

            string baseSql = @"
                SELECT co.C_Contract_ID      AS Contract_Id,
                       co.DocumentNo         AS Document_No,
                       COALESCE(bp.Name, N'') AS Customer_Name,
                       COALESCE(pr.Name, N'') AS Product_Name,
                       co.EndDate            AS End_Date,
                       co.RenewalType        AS Renewal_Type_Code,
                       co.DocStatus          AS Doc_Status_Code,
                       CAST(DAYSBETWEEN(co.EndDate, CURRENT_DATE) AS INTEGER) AS Days_To_End,
                       CurrencyConvert(co.GrandTotal, co.C_Currency_ID, @BaseCurrency_ID,
                              CURRENT_DATE, co.C_ConversionType_ID, co.AD_Client_ID, co.AD_Org_ID) AS Grand_Total_Base
                  FROM C_Contract co
                  LEFT OUTER JOIN C_BPartner bp ON ( bp.C_BPartner_ID = co.C_BPartner_ID )
                  LEFT OUTER JOIN M_Product  pr ON ( pr.M_Product_ID  = co.M_Product_ID )
                 WHERE " + BandWhereSql(band);

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
                    string docStatusCode = Util.GetValueOfString(dr["Doc_Status_Code"]);
                    string renewalTypeCode = Util.GetValueOfString(dr["Renewal_Type_Code"]);
                    DateTime? endDate = dr["End_Date"] == DBNull.Value ? (DateTime?)null : Util.GetValueOfDateTime(dr["End_Date"]);
                    int daysToEnd = dr["Days_To_End"] == DBNull.Value ? 0 : Util.GetValueOfInt(dr["Days_To_End"]);

                    result.Rows.Add(new DistributionDrillRow
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
                        DocStatusCode = docStatusCode,
                        DocStatus = DecodeLabel(docStatusMap, docStatusCode),
                        EndDate = endDate.HasValue ? endDate.Value.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture) : "",
                        DaysToEnd = daysToEnd
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
        /// Every C_Contract_ID matching one band, ended-soonest first, capped at
        /// <see cref="MaxZoomIds"/>. Backs "Open in browser"'s TabWhereClause
        /// IN-list - see <see cref="GetContractsByStatusIds"/>.
        /// </summary>
        /// <param name="ctx">Session context.</param>
        /// <param name="band">One of Active/Expiring/Draft/Expired/Cancelled (already validated).</param>
        private List<int> GetContractIdsData(Ctx ctx, string band)
        {
            List<int> ids = new List<int>();
            if (ctx == null) { return ids; }

            string sql = @"
                SELECT co.C_Contract_ID AS Contract_Id
                  FROM C_Contract co
                 WHERE " + BandWhereSql(band) + @"
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

        /// <summary>
        /// The tenant's accounting-schema (base) currency - AD_ClientInfo -&gt;
        /// C_AcctSchema1 -&gt; C_Currency, the same resolution the sibling Service
        /// Contracts KPIs (VAS_243/244/245/246/258/259) already use.
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

        /// <summary>AD_Ref_List(_Trl) Code -&gt; Label map for one column, cached per column+language (contracts-by-status.queries.md D-3). Never a hard-coded code -&gt; label map.</summary>
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

        /// <summary>Resolves and caches an AD_Column_ID by table/column name.</summary>
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

        private class DistributionSummary
        {
            public int ActiveCount { get; set; }
            public int ExpiringCount { get; set; }
            public int DraftCount { get; set; }
            public int ExpiredCount { get; set; }
            public int CancelledCount { get; set; }
            public int Total { get; set; }
        }

        private class DistributionDrillResult
        {
            public List<DistributionDrillRow> Rows { get; set; }
            public int Total { get; set; }
        }

        private class DistributionDrillRow
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
            public string DocStatusCode { get; set; }
            public string DocStatus { get; set; }
            public string EndDate { get; set; }
            public int DaysToEnd { get; set; }
        }
    }
}
