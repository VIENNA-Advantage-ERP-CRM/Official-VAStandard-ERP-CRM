/******************************************************
 * Module Name    : VAS
 * Purpose        : Service Contracts module Needs attention four-tile widget endpoints
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
    /// Module Name : VAS_261_NeedsAttentionWidget
    /// Purpose     : Data endpoints for the c3 x r2 "Needs attention" 2x2 tile
    ///               widget on the Service Contracts dashboard: (1) one row of
    ///               four COUNT(CASE ...) tile totals - Manual renewal due /
    ///               Notice overdue / Draft not activated / Billing overdue - over
    ///               four DERIVED (not mutually-exclusive by design - Notice
    ///               overdue is a stricter subset of Manual renewal due, kept as
    ///               its own alarm tile) conditions, (2) a paged (@7) drill list
    ///               for whichever tile the user clicks, reusing that tile's own
    ///               CASE predicate as the WHERE, and (3) every matching
    ///               Contract_ID for a tile (capped) so "Open in browser" can
    ///               filter the Service Contract window down to exactly that
    ///               tile's population via a TabWhereClause IN-list. Manual
    ///               renewal due = RenewalType='M' (bound :RenewalType_Manual) AND
    ///               live (Processed='Y' AND IsCancel='N' AND EndDate&gt;=today) AND
    ///               no successor (correlated NOT EXISTS on
    ///               C_Contract.Ref_Contract_ID - not an MRole alias) AND
    ///               0&lt;=daysToEnd&lt;=CancelBeforeDays; Notice overdue is the same
    ///               live/no-successor/daysToEnd&lt;CancelBeforeDays condition but
    ///               (2026-09-10) NOT restricted to RenewalType='M' - a missed
    ///               notice window matters regardless of renewal type; Draft not
    ///               activated =
    ///               Processed&lt;&gt;'Y'; Billing overdue = a correlated EXISTS on
    ///               C_ContractSchedule for an uninvoiced (C_Invoice_ID IS NULL)
    ///               period whose FROMDATE has already passed - also not an MRole
    ///               alias. 2026-09-08: "live"/"draft" read co.Processed rather
    ///               than co.DocStatus=:DocStatus_Completed/:DocStatus_Drafted -
    ///               the Service Contract window itself does not rely on that
    ///               column (verified against real data, the same fix already
    ///               applied to VAS_243/244/245/246/258/259/260 - applied here
    ///               proactively on first build rather than waiting for a
    ///               follow-up correction). RenewalType/DocStatus are still
    ///               decoded through the tenant's own AD_Ref_List/AD_Ref_List_Trl
    ///               dictionary for drill-list display. MRole is applied to the
    ///               single physical table alias "co" only. A drill row's detail
    ///               (its Renew / Generate invoice actions) reuses the already-
    ///               built VAS_241_ContractSearchWidget/GetContract, RunRenew and
    ///               RunGenerateInvoice endpoints rather than duplicating that
    ///               logic - the same shared-endpoint reuse VAS_120's Log-activity
    ///               quick action already established against VAS_126, and
    ///               VAS_244/245/246/258/259/260 already reused too. The widget
    ///               itself is read-only; no writes.
    /// Chronological development:
    ///   VAI052      2026-09-08 Created
    ///   VAI052      2026-09-10 NoticeOverdue (both the tile drill and the
    ///                          summary count) dropped its RenewalType=Manual
    ///                          scope, per explicit spec - it briefly required it
    ///                          earlier the same day to reconcile with
    ///                          VAS_245_RenewalActionWidget, which has now also
    ///                          reverted to the unscoped formula. ManualDue is
    ///                          unaffected - it is specifically about manual
    ///                          renewals and keeps the RenewalType=Manual scope.
    /// </summary>
    public class VAS_261_NeedsAttentionWidgetController : Controller
    {
        private static readonly VLogger Log = VLogger.GetVLogger(typeof(VAS_261_NeedsAttentionWidgetController).FullName);

        private const int DrillPageSize = 7;

        // Upper bound for the "Open in browser" TabWhereClause IN-list (GetContractIdsData) -
        // a single tile's population is naturally small; this just bounds a pathological case.
        private const int MaxZoomIds = 1000;

        // The four sanctioned tile keys - validated against this whitelist before ever
        // reaching TileWhereSql, so an unrecognised key can never select an unintended predicate.
        private static readonly HashSet<string> TileKeys = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            "ManualDue", "NoticeOverdue", "Draft", "BillingOverdue"
        };

        /// <summary>
        /// Tile scalars: the four independently-derived counts.
        /// </summary>
        /// <returns>JSON { ManualRenewalDue, NoticeOverdue, DraftNotActivated, BillingOverdue } or { Error }.</returns>
        [AjaxAuthorizeAttribute]
        [AjaxSessionFilterAttribute]
        public JsonResult GetNeedsAttentionSummary()
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
                Log.Log(Level.SEVERE, "VAS_261_NeedsAttentionWidget.GetNeedsAttentionSummary", ex);
                return ErrorResult(ctx);
            }
        }

        /// <summary>
        /// Paged drill rows for one clicked tile, using that tile's own CASE
        /// predicate as the WHERE (needs-attention.queries.md D-2).
        /// </summary>
        /// <param name="tile">One of ManualDue/NoticeOverdue/Draft/BillingOverdue.</param>
        /// <param name="offset">Zero-based paging offset.</param>
        /// <param name="limit">Page size (capped at <see cref="MaxZoomIds"/>).</param>
        /// <returns>JSON { Rows:[...], Total } or { Error }.</returns>
        [AjaxAuthorizeAttribute]
        [AjaxSessionFilterAttribute]
        public JsonResult GetNeedsAttentionDrill(string tile, int offset = 0, int limit = DrillPageSize)
        {
            if (Session["ctx"] == null) { return Json("", JsonRequestBehavior.AllowGet); }

            Ctx ctx = Session["ctx"] as Ctx;

            if (!TileKeys.Contains(tile ?? "")) { return ErrorResult(ctx); }
            if (offset < 0) { offset = 0; }
            if (limit <= 0 || limit > MaxZoomIds) { limit = DrillPageSize; }

            try
            {
                string json = JsonConvert.SerializeObject(GetDrillData(ctx, tile, offset, limit));
                return Json(json, JsonRequestBehavior.AllowGet);
            }
            catch (Exception ex)
            {
                Log.Log(Level.SEVERE, "VAS_261_NeedsAttentionWidget.GetNeedsAttentionDrill", ex);
                return ErrorResult(ctx);
            }
        }

        /// <summary>
        /// Every matching C_Contract_ID for one tile (not just one page), capped at
        /// <see cref="MaxZoomIds"/> - so "Open in browser" can filter the Service
        /// Contract window down to exactly that tile's population via a
        /// TabWhereClause IN-list.
        /// </summary>
        /// <param name="tile">One of ManualDue/NoticeOverdue/Draft/BillingOverdue.</param>
        /// <returns>JSON { Ids:[...] } or { Error }.</returns>
        [AjaxAuthorizeAttribute]
        [AjaxSessionFilterAttribute]
        public JsonResult GetNeedsAttentionIds(string tile)
        {
            if (Session["ctx"] == null) { return Json("", JsonRequestBehavior.AllowGet); }

            Ctx ctx = Session["ctx"] as Ctx;

            if (!TileKeys.Contains(tile ?? "")) { return ErrorResult(ctx); }

            try
            {
                string json = JsonConvert.SerializeObject(new { Ids = GetContractIdsData(ctx, tile) });
                return Json(json, JsonRequestBehavior.AllowGet);
            }
            catch (Exception ex)
            {
                Log.Log(Level.SEVERE, "VAS_261_NeedsAttentionWidget.GetNeedsAttentionIds", ex);
                return ErrorResult(ctx);
            }
        }

        /// <summary>
        /// One tile's own WHERE-only predicate (needs-attention.queries.md D-2's
        /// per-tile block) - every caller applies MRole to alias "co" itself right
        /// after calling this. <paramref name="tile"/> must already be validated
        /// against <see cref="TileKeys"/> by the caller. The successor probe
        /// (ManualDue/NoticeOverdue) and the billing-schedule probe (BillingOverdue)
        /// are plain correlated subqueries on "co" and are not MRole aliases.
        /// </summary>
        /// <param name="tile">One of ManualDue/NoticeOverdue/Draft/BillingOverdue.</param>
        /// <returns>WHERE-clause text (binds a subset of @AD_Client_ID, @RenewalType_Manual depending on the tile).</returns>
        private static string TileWhereSql(string tile)
        {
            string tenantScope = "co.AD_Client_ID = @AD_Client_ID AND co.IsActive = 'Y'";

            if (string.Equals(tile, "ManualDue", StringComparison.OrdinalIgnoreCase))
            {
                return tenantScope + @"
               AND co.RenewalType = @RenewalType_Manual
               AND co.Processed = 'Y'
               AND co.IsCancel = 'N'
               AND co.EndDate >= CURRENT_DATE
               AND DAYSBETWEEN(co.EndDate, CURRENT_DATE) <= co.CancelBeforeDays
               AND NOT EXISTS (
                     SELECT 1 FROM C_Contract succ
                      WHERE succ.Ref_Contract_ID = co.C_Contract_ID
                        AND succ.AD_Client_ID = co.AD_Client_ID
                        AND succ.IsActive = 'Y' )";
            }

            if (string.Equals(tile, "NoticeOverdue", StringComparison.OrdinalIgnoreCase))
            {
                return tenantScope + @"
               AND co.Processed = 'Y'
               AND co.IsCancel = 'N'
               AND co.EndDate >= CURRENT_DATE
               AND DAYSBETWEEN(co.EndDate, CURRENT_DATE) < co.CancelBeforeDays
               AND NOT EXISTS (
                     SELECT 1 FROM C_Contract succ
                      WHERE succ.Ref_Contract_ID = co.C_Contract_ID
                        AND succ.AD_Client_ID = co.AD_Client_ID
                        AND succ.IsActive = 'Y' )";
            }

            if (string.Equals(tile, "Draft", StringComparison.OrdinalIgnoreCase))
            {
                return tenantScope + @"
               AND co.Processed <> 'Y'";
            }

            // BillingOverdue (the default/fourth tile).
            return tenantScope + @"
               AND EXISTS (
                     SELECT 1 FROM C_ContractSchedule cs
                      WHERE cs.C_Contract_ID = co.C_Contract_ID
                        AND cs.AD_Client_ID = co.AD_Client_ID
                        AND cs.IsActive = 'Y'
                        AND cs.C_Invoice_ID IS NULL
                        AND cs.FROMDATE < CURRENT_DATE )";
        }

        /// <summary>
        /// Ended-soonest / most-overdue ORDER BY for one tile's drill list
        /// (needs-attention.queries.md D-2's per-tile ORDER note).
        /// </summary>
        /// <param name="tile">One of ManualDue/NoticeOverdue/Draft/BillingOverdue.</param>
        private static string TileOrderSql(string tile)
        {
            if (string.Equals(tile, "ManualDue", StringComparison.OrdinalIgnoreCase)
                || string.Equals(tile, "NoticeOverdue", StringComparison.OrdinalIgnoreCase))
            {
                return "( DAYSBETWEEN(co.EndDate, CURRENT_DATE) - co.CancelBeforeDays ) ASC";
            }

            return "co.EndDate ASC";
        }

        /// <summary>
        /// Fresh parameter array for one command execution against a single tile's
        /// <see cref="TileWhereSql"/> - every placeholder occurs exactly once in
        /// each assembled statement (a single tile's predicate never repeats
        /// @RenewalType_Manual), so no name repeats under Oracle's positional
        /// binding.
        /// </summary>
        /// <param name="ctx">Session context.</param>
        private SqlParameter[] BuildPredicateParameters(Ctx ctx)
        {
            return new[]
            {
                new SqlParameter("@AD_Client_ID", ctx.GetAD_Client_ID()),
                new SqlParameter("@RenewalType_Manual", X_C_Contract.RENEWALTYPE_Manual)
            };
        }

        /// <summary>
        /// Resolves the four tile counts in one COUNT(CASE ...) query
        /// (needs-attention.queries.md D-1). 2026-09-10: NoticeOverdue dropped its
        /// RenewalType=Manual scope (per explicit spec, reverted same-day change) -
        /// it now counts every live contract past its notice window regardless of
        /// renewal type, matching VAS_245_RenewalActionWidget's own
        /// Notice_Overdue_Count (also reverted). Only ManualDue still scopes to
        /// @RenewalType_Manual, since it is specifically about manual renewals.
        /// </summary>
        /// <param name="ctx">Session context.</param>
        private NeedsAttentionSummary GetSummaryData(Ctx ctx)
        {
            NeedsAttentionSummary result = new NeedsAttentionSummary();
            if (ctx == null) { return result; }

            string sql = @"
                SELECT
                  COUNT(CASE WHEN co.RenewalType = @RenewalType_Manual
                                  AND co.Processed = 'Y'
                                  AND co.IsCancel = 'N'
                                  AND co.EndDate >= CURRENT_DATE
                                  AND DAYSBETWEEN(co.EndDate, CURRENT_DATE) <= co.CancelBeforeDays
                                  AND NOT EXISTS ( SELECT 1 FROM C_Contract succ
                                                    WHERE succ.Ref_Contract_ID = co.C_Contract_ID
                                                      AND succ.AD_Client_ID = co.AD_Client_ID
                                                      AND succ.IsActive = 'Y' )
                             THEN 1 END) AS Manual_Renewal_Due,
                  COUNT(CASE WHEN co.Processed = 'Y'
                                  AND co.IsCancel = 'N'
                                  AND co.EndDate >= CURRENT_DATE
                                  AND DAYSBETWEEN(co.EndDate, CURRENT_DATE) < co.CancelBeforeDays
                                  AND NOT EXISTS ( SELECT 1 FROM C_Contract succ
                                                    WHERE succ.Ref_Contract_ID = co.C_Contract_ID
                                                      AND succ.AD_Client_ID = co.AD_Client_ID
                                                      AND succ.IsActive = 'Y' )
                             THEN 1 END) AS Notice_Overdue,
                  COUNT(CASE WHEN co.Processed <> 'Y'
                             THEN 1 END) AS Draft_Not_Activated,
                  COUNT(CASE WHEN EXISTS ( SELECT 1 FROM C_ContractSchedule cs
                                           WHERE cs.C_Contract_ID = co.C_Contract_ID
                                             AND cs.AD_Client_ID = co.AD_Client_ID
                                             AND cs.IsActive = 'Y'
                                             AND cs.C_Invoice_ID IS NULL
                                             AND cs.FROMDATE < CURRENT_DATE )
                             THEN 1 END) AS Billing_Overdue
                  FROM C_Contract co
                 WHERE co.AD_Client_ID = @AD_Client_ID AND co.IsActive = 'Y'";

            sql = MRole.GetDefault(ctx).AddAccessSQL(sql, "co", MRole.SQL_FULLYQUALIFIED, MRole.SQL_RO);

            IDataReader dr = null;
            try
            {
                dr = DB.ExecuteReader(sql, new[]
                {
                    new SqlParameter("@AD_Client_ID", ctx.GetAD_Client_ID()),
                    new SqlParameter("@RenewalType_Manual", X_C_Contract.RENEWALTYPE_Manual)
                });
                if (dr != null && dr.Read())
                {
                    result.ManualRenewalDue = Util.GetValueOfInt(dr["Manual_Renewal_Due"]);
                    result.NoticeOverdue = Util.GetValueOfInt(dr["Notice_Overdue"]);
                    result.DraftNotActivated = Util.GetValueOfInt(dr["Draft_Not_Activated"]);
                    result.BillingOverdue = Util.GetValueOfInt(dr["Billing_Overdue"]);
                }
            }
            finally
            {
                CloseReader(dr);
            }

            return result;
        }

        /// <summary>
        /// Paged drill rows for one tile (needs-attention.queries.md D-2) - the
        /// base SQL is WHERE-only until MRole is applied to alias "co", then
        /// ORDER BY / OFFSET..FETCH are appended.
        /// </summary>
        /// <param name="ctx">Session context.</param>
        /// <param name="tile">One of ManualDue/NoticeOverdue/Draft/BillingOverdue (already validated).</param>
        /// <param name="offset">Zero-based paging offset.</param>
        /// <param name="limit">Page size.</param>
        private NeedsAttentionDrillResult GetDrillData(Ctx ctx, string tile, int offset, int limit)
        {
            NeedsAttentionDrillResult result = new NeedsAttentionDrillResult { Rows = new List<NeedsAttentionDrillRow>() };
            if (ctx == null) { return result; }

            string language = GetLanguage(ctx);
            Dictionary<string, string> renewalTypeMap = GetDecodeMap("C_Contract", "RenewalType", language);
            Dictionary<string, string> docStatusMap = GetDecodeMap("C_Contract", "DocStatus", language);

            int baseCurrencyId; string baseIso, baseSymbol; int basePrecision;
            ResolveBaseCurrency(ctx, out baseCurrencyId, out baseIso, out baseSymbol, out basePrecision);

            string baseSql = @"
                SELECT co.C_Contract_ID      AS Contract_Id,
                       co.DocumentNo         AS Document_No,
                       COALESCE(bp.Name, N'') AS Customer_Name,
                       COALESCE(pr.Name, N'') AS Product_Name,
                       co.EndDate            AS End_Date,
                       co.CancelBeforeDays   AS Cancel_Before_Days,
                       co.RenewalType        AS Renewal_Type_Code,
                       co.DocStatus          AS Doc_Status_Code,
                       CAST(DAYSBETWEEN(co.EndDate, CURRENT_DATE) AS INTEGER) AS Days_To_End,
                       CurrencyConvert(co.GrandTotal, co.C_Currency_ID, @BaseCurrency_ID,
                              CURRENT_DATE, co.C_ConversionType_ID, co.AD_Client_ID, co.AD_Org_ID) AS Grand_Total_Base
                  FROM C_Contract co
                  LEFT OUTER JOIN C_BPartner bp ON ( bp.C_BPartner_ID = co.C_BPartner_ID )
                  LEFT OUTER JOIN M_Product  pr ON ( pr.M_Product_ID  = co.M_Product_ID )
                 WHERE " + TileWhereSql(tile);

            baseSql = MRole.GetDefault(ctx).AddAccessSQL(baseSql, "co", MRole.SQL_FULLYQUALIFIED, MRole.SQL_RO);

            string listSql = baseSql + @"
                ORDER BY " + TileOrderSql(tile) + @"
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
                    string docStatusCode = Util.GetValueOfString(dr["Doc_Status_Code"]);
                    DateTime? endDate = dr["End_Date"] == DBNull.Value ? (DateTime?)null : Util.GetValueOfDateTime(dr["End_Date"]);
                    int daysToEnd = dr["Days_To_End"] == DBNull.Value ? 0 : Util.GetValueOfInt(dr["Days_To_End"]);
                    int cancelBeforeDays = dr["Cancel_Before_Days"] == DBNull.Value ? 0 : Util.GetValueOfInt(dr["Cancel_Before_Days"]);

                    result.Rows.Add(new NeedsAttentionDrillRow
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
                        CancelBeforeDays = cancelBeforeDays,
                        EndDate = endDate.HasValue ? endDate.Value.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture) : "",
                        DaysToEnd = daysToEnd,
                        // Derived in C# (needs-attention.queries.md A.7) - never stored.
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
        /// Every C_Contract_ID matching one tile, in the same order as its drill
        /// list, capped at <see cref="MaxZoomIds"/>. Backs "Open in browser"'s
        /// TabWhereClause IN-list - see <see cref="GetNeedsAttentionIds"/>.
        /// </summary>
        /// <param name="ctx">Session context.</param>
        /// <param name="tile">One of ManualDue/NoticeOverdue/Draft/BillingOverdue (already validated).</param>
        private List<int> GetContractIdsData(Ctx ctx, string tile)
        {
            List<int> ids = new List<int>();
            if (ctx == null) { return ids; }

            string sql = @"
                SELECT co.C_Contract_ID AS Contract_Id
                  FROM C_Contract co
                 WHERE " + TileWhereSql(tile) + @"
                 ORDER BY " + TileOrderSql(tile) + @"
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
        /// Contracts KPIs (VAS_243/244/245/246/258/259/260) already use.
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

        /// <summary>AD_Ref_List(_Trl) Code -&gt; Label map for one column, cached per column+language (needs-attention.queries.md D-3). Never a hard-coded code -&gt; label map.</summary>
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

        private class NeedsAttentionSummary
        {
            public int ManualRenewalDue { get; set; }
            public int NoticeOverdue { get; set; }
            public int DraftNotActivated { get; set; }
            public int BillingOverdue { get; set; }
        }

        private class NeedsAttentionDrillResult
        {
            public List<NeedsAttentionDrillRow> Rows { get; set; }
            public int Total { get; set; }
        }

        private class NeedsAttentionDrillRow
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
            public int CancelBeforeDays { get; set; }
            public string EndDate { get; set; }
            public int DaysToEnd { get; set; }
            public int NoticeDeadlineDays { get; set; }
        }
    }
}
