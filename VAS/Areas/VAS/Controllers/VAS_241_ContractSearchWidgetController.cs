/******************************************************
 * Module Name    : VAS
 * Purpose        : Service Contracts module Contract search widget endpoints
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
using VAdvantage.Print;
using VAdvantage.ProcessEngine;
using VAdvantage.Utility;
using VIS.Filters;

namespace VAS.Controllers
{
    /// <summary>
    /// Module Name : VAS_241_ContractSearchWidget
    /// Purpose     : Data endpoints for the 9x1 "Contract search" finder on the
    ///               Service Contracts dashboard - a type-ahead search over the
    ///               whole accessible C_Contract portfolio (any lifecycle state,
    ///               not live-only) matching DocumentNo, customer name, product
    ///               name and the DECODED DocStatus / ContractType labels (never
    ///               a raw code). Dropdown returns at most 7 hits plus the total
    ///               match count for "See all N matches"; the same query pages
    ///               @7 for that list. A hit resolves a richer detail payload for
    ///               the drill-down modal: header (customer/contact/rep/frequency/
    ///               cycles), a value/status/type/renewal/ends/notice-days/billed/
    ///               unbilled stat grid, a "needs attention" billing-overdue figure
    ///               and the customer's open-ticket count, and the full
    ///               C_ContractSchedule billing schedule. MRole is applied to the
    ///               single physical table alias "co" only for the header/search
    ///               queries; the AD_Ref_List/AD_Ref_List_Trl decode joins/EXISTS
    ///               do not change that anchor. Status/type labels are resolved
    ///               from the tenant's own application dictionary
    ///               (AD_Column -> AD_Ref_List -> AD_Ref_List_Trl, keyed by
    ///               AD_Column_ID) and cached per column+language - no list
    ///               code is ever hard-coded or shipped raw to the client.
    ///               The detail modal's Renew / Generate invoice buttons run the
    ///               real C_Contract.RenewContract / GenerateInvoice button
    ///               processes through the standard AD_PInstance + ProcessInfo +
    ///               ProcessCtl engine - the AD_Process_ID is resolved from those
    ///               columns' own AD_Column.AD_Process_ID (never hard-coded, never
    ///               a raw DML write here), exactly as the classic window's own
    ///               button would run them.
    /// Chronological development:
    ///   VAI052      2026-09-07 Created
    ///   VAI052      2026-09-07 Added the rich detail payload (contact/rep/
    ///                          frequency/cycles/notice days/billed/unbilled,
    ///                          billing-overdue figure, customer open-ticket
    ///                          count, C_ContractSchedule rows) and wired Renew /
    ///                          Generate invoice to the real button processes.
    ///   VAI052      2026-09-08 Search rows, the detail header's Value and its
    ///                          C_ContractSchedule-derived Billed/Unbilled/
    ///                          overdue/period amounts are now converted to the
    ///                          tenant's base (accounting-schema) currency via
    ///                          CurrencyConvert instead of each contract's own
    ///                          transaction currency - matches the KPI tiles
    ///                          (VAS_243/244/246), which already reported in
    ///                          base currency, and avoids mixed-currency values
    ///                          in one list.
    ///   VAI052      2026-09-09 RunGenerateInvoice briefly resolved a
    ///                          "ContractInvoice" button column instead of
    ///                          "GenerateInvoice" - a misreading of "run
    ///                          ContractInvoice process" as a column-name
    ///                          instruction; "ContractInvoice" is actually
    ///                          the underlying AD_Process.Value that column
    ///                          GenerateInvoice's own AD_Process_ID already
    ///                          resolves to (confirmed directly against the
    ///                          live AD_Column/AD_Process dictionary: no
    ///                          column literally named "ContractInvoice"
    ///                          exists on C_Contract). Reverted to
    ///                          "GenerateInvoice", which was correct all
    ///                          along. RunRenew's "RenewContract" is also
    ///                          confirmed correct against the live dictionary
    ///                          (AD_Process_ID 1000162, "Renew Contract") -
    ///                          an observed [VAS_241_ProcessNotConfigured]
    ///                          on Renew was very likely this controller's
    ///                          own static ColumnProcessIdCache holding a
    ///                          stale 0 from earlier testing, not a real
    ///                          dictionary gap; an app-pool recycle clears it.
    /// </summary>
    public class VAS_241_ContractSearchWidgetController : Controller
    {
        private static readonly VLogger Log = VLogger.GetVLogger(typeof(VAS_241_ContractSearchWidgetController).FullName);

        // Dropdown + "See all" page are both bounded to seven rows (design spec).
        private const int PageSize = 7;

        // Zoom-to-record target: Service Contract window (Export_ID VAS_1000262).
        // Given directly by the build spec rather than resolved by window name -
        // VAS.ZoomUtil.zoomToRecord takes an AD_Window_ID > 0 as a direct zoom,
        // skipping the VAS_ZoomWindow/GetWindowId round-trip other widgets need
        // when they only know the window's display name.
        private const int ContractWindowId = 1000248;

        // Per-column, per-language AD_Ref_List(_Trl) decode cache (Code -> Label).
        // Resolved once per "TableName.ColumnName|Language" key and reused - the
        // tenant's own dictionary never needs a per-request round trip once warm.
        private static readonly ConcurrentDictionary<string, Dictionary<string, string>> DecodeMapCache =
            new ConcurrentDictionary<string, Dictionary<string, string>>(StringComparer.OrdinalIgnoreCase);

        /// <summary>
        /// Type-ahead search for the finder's dropdown: at most seven hits plus
        /// the total match count that drives "See all N matches".
        /// </summary>
        /// <param name="q">Raw user search text.</param>
        /// <returns>JSON { Rows:[...], Total } or { Error }.</returns>
        [AjaxAuthorizeAttribute]
        [AjaxSessionFilterAttribute]
        [ValidateInput(false)]
        public JsonResult SearchContracts(string q)
        {
            if (Session["ctx"] == null) { return Json("", JsonRequestBehavior.AllowGet); }

            Ctx ctx = Session["ctx"] as Ctx;

            try
            {
                string json = JsonConvert.SerializeObject(SearchContractsData(ctx, q, 0, PageSize));
                return Json(json, JsonRequestBehavior.AllowGet);
            }
            catch (Exception ex)
            {
                Log.Log(Level.SEVERE, "VAS_241_ContractSearchWidget.SearchContracts", ex);
                return ErrorResult(ctx);
            }
        }

        /// <summary>
        /// Paged contract list for the "See all N matches" modal - the identical
        /// predicate as <see cref="SearchContracts"/>, paged seven at a time.
        /// </summary>
        /// <param name="q">Same search text the dropdown was showing.</param>
        /// <param name="offset">Zero-based paging offset, advanced by seven.</param>
        /// <returns>JSON { Rows:[...], Total } or { Error }.</returns>
        [AjaxAuthorizeAttribute]
        [AjaxSessionFilterAttribute]
        [ValidateInput(false)]
        public JsonResult GetContracts(string q, int offset = 0)
        {
            if (Session["ctx"] == null) { return Json("", JsonRequestBehavior.AllowGet); }

            Ctx ctx = Session["ctx"] as Ctx;

            if (offset < 0) { offset = 0; }

            try
            {
                string json = JsonConvert.SerializeObject(SearchContractsData(ctx, q, offset, PageSize));
                return Json(json, JsonRequestBehavior.AllowGet);
            }
            catch (Exception ex)
            {
                Log.Log(Level.SEVERE, "VAS_241_ContractSearchWidget.GetContracts", ex);
                return ErrorResult(ctx);
            }
        }

        /// <summary>
        /// Full detail of one contract for the drill-down modal: header, the
        /// value/status/type/renewal/ends/notice-days/billed/unbilled stat grid,
        /// a "needs attention" billing-overdue figure, the customer's open-ticket
        /// count and the full C_ContractSchedule billing schedule. An inaccessible
        /// or non-existent id yields ContractId 0, never an error carrying
        /// internals.
        /// </summary>
        /// <param name="id">C_Contract_ID.</param>
        /// <returns>JSON { Contract:{...}, WindowId } or { Error }.</returns>
        [AjaxAuthorizeAttribute]
        [AjaxSessionFilterAttribute]
        public JsonResult GetContract(int id = 0)
        {
            if (Session["ctx"] == null) { return Json("", JsonRequestBehavior.AllowGet); }

            Ctx ctx = Session["ctx"] as Ctx;

            try
            {
                ContractDetail detail = GetContractDetailData(ctx, id);
                string json = JsonConvert.SerializeObject(new { Contract = detail, WindowId = ContractWindowId });
                return Json(json, JsonRequestBehavior.AllowGet);
            }
            catch (Exception ex)
            {
                Log.Log(Level.SEVERE, "VAS_241_ContractSearchWidget.GetContract", ex);
                return ErrorResult(ctx);
            }
        }

        /// <summary>
        /// Runs the real C_Contract.RenewContract button process (RenewContract)
        /// against one contract through the standard process engine.
        /// </summary>
        /// <param name="id">C_Contract_ID.</param>
        /// <returns>JSON { Success, Message }.</returns>
        [HttpPost]
        [AjaxAuthorizeAttribute]
        [AjaxSessionFilterAttribute]
        [ValidateInput(false)]
        public JsonResult RunRenew(int id = 0)
        {
            return RunContractAction(id, "RenewContract");
        }

        /// <summary>
        /// Runs the real C_Contract.GenerateInvoice button process
        /// (AD_Process_ID resolves to the "ContractInvoice" process) against
        /// one contract through the standard process engine.
        /// </summary>
        /// <param name="id">C_Contract_ID.</param>
        /// <returns>JSON { Success, Message }.</returns>
        [HttpPost]
        [AjaxAuthorizeAttribute]
        [AjaxSessionFilterAttribute]
        [ValidateInput(false)]
        public JsonResult RunGenerateInvoice(int id = 0)
        {
            return RunContractAction(id, "GenerateInvoice");
        }

        /// <summary>
        /// Shared plumbing for the two mutating row actions: verify the contract
        /// exists and is accessible to this role (never trust the posted id alone),
        /// resolve the button column's own AD_Process_ID (AD_Table -> AD_Column,
        /// same dictionary-driven lookup VAS_196's period open/close uses - never a
        /// hard-coded process id or a raw DML write), then run it through
        /// AD_PInstance + ProcessInfo + ProcessCtl exactly as the classic window's
        /// own button would.
        /// </summary>
        /// <param name="contractId">C_Contract_ID the action targets.</param>
        /// <param name="buttonColumnName">The C_Contract button column (RenewContract / GenerateInvoice) whose AD_Column.AD_Process_ID identifies the process to run - GenerateInvoice's own AD_Process_ID resolves to the "ContractInvoice" process (AD_Process.Value), confirmed against the live dictionary 2026-09-09.</param>
        private JsonResult RunContractAction(int contractId, string buttonColumnName)
        {
            if (Session["ctx"] == null)
            {
                return Json(new { Success = false, Message = Msg.GetMsg(Env.GetCtx(), "SessionExpired") ?? "Session Expired" }, JsonRequestBehavior.AllowGet);
            }

            Ctx ctx = Session["ctx"] as Ctx;

            if (contractId <= 0 || !ContractExists(ctx, contractId))
            {
                return Json(new { Success = false, Message = Msg.GetMsg(ctx, "VAS_241_ContractNotFound") ?? "Contract not found or not accessible." }, JsonRequestBehavior.AllowGet);
            }

            try
            {
                int processId = GetColumnProcessId("C_Contract", buttonColumnName);
                int tableId = MTable.Get_Table_ID("C_Contract");
                ProcessRunResult result = RunProcess(ctx, processId, contractId, tableId);
                return Json(new { Success = result.Success, Message = result.Message }, JsonRequestBehavior.AllowGet);
            }
            catch (Exception ex)
            {
                Log.Log(Level.SEVERE, "VAS_241_ContractSearchWidget.RunContractAction(" + buttonColumnName + ")", ex);
                return Json(new { Success = false, Message = ex.Message }, JsonRequestBehavior.AllowGet);
            }
        }

        /// <summary>Whether the id is an active, MRole-accessible C_Contract row for this tenant - the posted id is never trusted on its own before a mutating action runs against it.</summary>
        private bool ContractExists(Ctx ctx, int contractId)
        {
            string sql = "SELECT 1 FROM C_Contract co WHERE co.C_Contract_ID = @Contract_ID AND co.AD_Client_ID = @AD_Client_ID AND co.IsActive = 'Y'";
            sql = MRole.GetDefault(ctx).AddAccessSQL(sql, "co", MRole.SQL_FULLYQUALIFIED, MRole.SQL_RO);

            object value = DB.ExecuteScalar(sql, new[]
            {
                new SqlParameter("@Contract_ID", contractId),
                new SqlParameter("@AD_Client_ID", ctx.GetAD_Client_ID())
            }, null);

            return value != null && value != DBNull.Value;
        }

        /// <summary>
        /// AD_Process_ID wired to one table/column's button (AD_Table -> AD_Column,
        /// keyed by AD_Column.AD_Process_ID), cached per table+column. The same
        /// dictionary-driven resolution VAS_196's period open/close process uses -
        /// never a hard-coded process id, since it differs per tenant/import.
        /// </summary>
        private static readonly ConcurrentDictionary<string, int> ColumnProcessIdCache = new ConcurrentDictionary<string, int>(StringComparer.OrdinalIgnoreCase);

        private static int GetColumnProcessId(string tableName, string columnName)
        {
            string cacheKey = tableName + "." + columnName;

            int cached;
            if (ColumnProcessIdCache.TryGetValue(cacheKey, out cached)) { return cached; }

            string sql = @"
                SELECT c.AD_Process_ID AS AD_Process_ID
                  FROM AD_Table t
                  JOIN AD_Column c ON ( c.AD_Table_ID = t.AD_Table_ID )
                 WHERE t.TableName = @Tbl
                   AND c.ColumnName = @Col
                   AND t.IsActive = 'Y'
                   AND c.IsActive = 'Y'
                   AND c.AD_Process_ID IS NOT NULL";

            int processId = Util.GetValueOfInt(DB.ExecuteScalar(sql, new[]
            {
                new SqlParameter("@Tbl", SqlDbType.NVarChar) { Value = tableName },
                new SqlParameter("@Col", SqlDbType.NVarChar) { Value = columnName }
            }, null));

            ColumnProcessIdCache[cacheKey] = processId;
            return processId;
        }

        /// <summary>
        /// Runs one server process against a single record through the standard
        /// AD_PInstance + ProcessInfo + ProcessCtl engine - the process class is
        /// never called directly. Uses ProcessInfo's 4-arg (title, processId,
        /// tableId, recordId) constructor and ProcessCtl's parameterless
        /// constructor + Process(pi, ctx, out report, out reportEngine) call,
        /// not the windowNo-bound ProcessCtl(ctx, windowNo, pi, trx).Run()
        /// shape VAS_196's RunPeriodControlProcess uses - the same
        /// title/table/record-in-constructor + Process(...) shape this
        /// codebase's own ModelLibrary.MCash.GenerateCashReport already uses,
        /// and the shape VA061_VACRMExtension's LeadRightPanelModel process
        /// runners (CreateRequest / AddToTargetList / GenerateOpportunity /
        /// GenerateProspect) use for ad-hoc, non-window-bound process
        /// execution from a plain AJAX controller action (2026-09-09, per
        /// explicit reference).
        /// </summary>
        private ProcessRunResult RunProcess(Ctx ctx, int processId, int recordId, int tableId)
        {
            if (processId <= 0)
            {
                return new ProcessRunResult { Success = false, Message = Msg.GetMsg(ctx, "VAS_241_ProcessNotConfigured") ?? "This action is not configured." };
            }

            try
            {
                MPInstance instance = new MPInstance(ctx, processId, recordId);
                if (!instance.Save())
                {
                    return new ProcessRunResult { Success = false, Message = Msg.GetMsg(ctx, "VAS_241_ProcessStartFailed") ?? "Could not start the process." };
                }

                ProcessInfo pi = new ProcessInfo("", processId, tableId, recordId);
                pi.SetAD_PInstance_ID(instance.GetAD_PInstance_ID());
                pi.SetAD_Client_ID(ctx.GetAD_Client_ID());
                pi.SetAD_Org_ID(ctx.GetAD_Org_ID());
                pi.SetAD_User_ID(ctx.GetAD_User_ID());

                byte[] report = null;
                ReportEngine_N reportEngine = null;
                ProcessCtl ctl = new ProcessCtl();
                ctl.Process(pi, ctx, out report, out reportEngine);

                string summary = pi.GetSummary();
                if (pi.IsError())
                {
                    return new ProcessRunResult { Success = false, Message = string.IsNullOrEmpty(summary) ? (Msg.GetMsg(ctx, "VAS_241_ProcessFailed") ?? "The process reported an error.") : summary };
                }

                return new ProcessRunResult { Success = true, Message = summary };
            }
            catch (Exception ex)
            {
                Log.Log(Level.SEVERE, "VAS_241_ContractSearchWidget.RunProcess", ex);
                return new ProcessRunResult { Success = false, Message = ex.Message };
            }
        }

        /// <summary>
        /// The shared search predicate (contract-search.queries.md §D-1/D-2/detail):
        /// DocumentNo / customer / product / decoded DocStatus / decoded ContractType
        /// / sales rep / decoded renewal type / billing frequency / currency /
        /// payment term, case-insensitive, LIKE-metacharacter escaped. MRole has NOT
        /// been applied yet - every caller applies it to alias "co" itself, right
        /// after calling this, so the one predicate text can be reused for the list,
        /// its count and the single-id detail lookup without ever drifting apart.
        /// Sales rep / billing frequency / currency / payment term are plain FK
        /// lookups (AD_User / C_Frequency / C_Currency / C_PaymentTerm - confirmed
        /// against the live install's own AD_Column dictionary and this codebase's
        /// existing SalesRep_ID/C_Frequency_ID/C_PaymentTerm_ID joins, 2026-09-09),
        /// matched by name (currency by ISO_Code); renewal type is a List reference
        /// (AD_Ref_List/AD_Ref_List_Trl) and is decoded the same way DocStatus and
        /// ContractType already are - never a hardcoded A/M -&gt; Auto/Manual match.
        /// </summary>
        private static string BuildProjectionSql()
        {
            return @"
                SELECT co.C_Contract_ID  AS Contract_Id,
                       co.DocumentNo     AS Document_No,
                       COALESCE(bp.Name, N'') AS Customer_Name,
                       COALESCE(pr.Name, N'') AS Product_Name,
                       CurrencyConvert(co.GrandTotal, co.C_Currency_ID, @BaseCurrency_ID,
                              CURRENT_DATE, co.C_ConversionType_ID, co.AD_Client_ID, co.AD_Org_ID) AS Grand_Total,
                       co.ContractType   AS Contract_Type_Code,
                       co.DocStatus      AS Doc_Status_Code,
                       co.IsCancel       AS Is_Cancel,
                       co.Processed      AS Is_Processed,
                       co.RenewalType    AS Renewal_Type_Code,
                       co.EndDate        AS End_Date,
                       CAST(DAYSBETWEEN(co.EndDate, CURRENT_DATE) AS INTEGER) AS Days_To_End
                  FROM C_Contract co
                  LEFT JOIN C_BPartner    bp  ON ( bp.C_BPartner_ID    = co.C_BPartner_ID )
                  LEFT JOIN M_Product     pr  ON ( pr.M_Product_ID     = co.M_Product_ID )
                  LEFT JOIN AD_User       sr  ON ( sr.AD_User_ID       = co.SalesRep_ID )
                  LEFT JOIN C_Frequency   fr  ON ( fr.C_Frequency_ID   = co.C_Frequency_ID )
                  LEFT JOIN C_Currency    cur ON ( cur.C_Currency_ID   = co.C_Currency_ID )
                  LEFT JOIN C_PaymentTerm pt  ON ( pt.C_PaymentTerm_ID = co.C_PaymentTerm_ID )
                 WHERE co.AD_Client_ID = @AD_Client_ID
                   AND co.IsActive = 'Y'
                   AND (
                        UPPER(co.DocumentNo) LIKE UPPER(@Search_DocumentNo) ESCAPE '\'
                     OR UPPER(COALESCE(bp.Name, N'')) LIKE UPPER(@Search_Customer) ESCAPE '\'
                     OR UPPER(COALESCE(pr.Name, N'')) LIKE UPPER(@Search_Product) ESCAPE '\'
                     OR UPPER(COALESCE(sr.Name, N'')) LIKE UPPER(@Search_SalesRep) ESCAPE '\'
                     OR UPPER(COALESCE(fr.Name, N'')) LIKE UPPER(@Search_Frequency) ESCAPE '\'
                     OR UPPER(COALESCE(cur.ISO_Code, N'')) LIKE UPPER(@Search_Currency) ESCAPE '\'
                     OR UPPER(COALESCE(pt.Name, N'')) LIKE UPPER(@Search_PaymentTerm) ESCAPE '\'
                     OR EXISTS (
                          SELECT 1
                            FROM AD_Ref_List rl
                            JOIN AD_Column col ON ( col.AD_Reference_Value_ID = rl.AD_Reference_ID )
                            LEFT JOIN AD_Ref_List_Trl rlt ON ( rlt.AD_Ref_List_ID = rl.AD_Ref_List_ID AND rlt.AD_Language = @AD_Language_Status )
                           WHERE col.AD_Column_ID = @AD_Column_ID_DocStatus
                             AND rl.Value = co.DocStatus
                             AND UPPER(COALESCE(rlt.Name, rl.Name, rl.Value)) LIKE UPPER(@Search_Status) ESCAPE '\' )
                     OR EXISTS (
                          SELECT 1
                            FROM AD_Ref_List rl2
                            JOIN AD_Column col2 ON ( col2.AD_Reference_Value_ID = rl2.AD_Reference_ID )
                            LEFT JOIN AD_Ref_List_Trl rlt2 ON ( rlt2.AD_Ref_List_ID = rl2.AD_Ref_List_ID AND rlt2.AD_Language = @AD_Language_Type )
                           WHERE col2.AD_Column_ID = @AD_Column_ID_ContractType
                             AND rl2.Value = co.ContractType
                             AND UPPER(COALESCE(rlt2.Name, rl2.Name, rl2.Value)) LIKE UPPER(@Search_Type) ESCAPE '\' )
                     OR EXISTS (
                          SELECT 1
                            FROM AD_Ref_List rl3
                            JOIN AD_Column col3 ON ( col3.AD_Reference_Value_ID = rl3.AD_Reference_ID )
                            LEFT JOIN AD_Ref_List_Trl rlt3 ON ( rlt3.AD_Ref_List_ID = rl3.AD_Ref_List_ID AND rlt3.AD_Language = @AD_Language_Renewal )
                           WHERE col3.AD_Column_ID = @AD_Column_ID_RenewalType
                             AND rl3.Value = co.RenewalType
                             AND UPPER(COALESCE(rlt3.Name, rl3.Name, rl3.Value)) LIKE UPPER(@Search_Renewal) ESCAPE '\' )
                   )";
        }

        /// <summary>
        /// Fresh parameter array for one command execution - every placeholder in
        /// <see cref="BuildProjectionSql"/> occurs exactly once in the assembled text,
        /// so no name repeats (the ORA-01008 positional-binding trap documented on the
        /// sibling search widgets does not apply here).
        /// </summary>
        private SqlParameter[] BuildSearchParameters(int clientId, string likeValue, string language, int docStatusColumnId, int contractTypeColumnId, int renewalTypeColumnId, int baseCurrencyId)
        {
            return new[]
            {
                new SqlParameter("@AD_Client_ID", clientId),
                new SqlParameter("@Search_DocumentNo", SqlDbType.NVarChar) { Value = likeValue },
                new SqlParameter("@Search_Customer", SqlDbType.NVarChar) { Value = likeValue },
                new SqlParameter("@Search_Product", SqlDbType.NVarChar) { Value = likeValue },
                new SqlParameter("@Search_SalesRep", SqlDbType.NVarChar) { Value = likeValue },
                new SqlParameter("@Search_Frequency", SqlDbType.NVarChar) { Value = likeValue },
                new SqlParameter("@Search_Currency", SqlDbType.NVarChar) { Value = likeValue },
                new SqlParameter("@Search_PaymentTerm", SqlDbType.NVarChar) { Value = likeValue },
                new SqlParameter("@AD_Language_Status", SqlDbType.NVarChar) { Value = language },
                new SqlParameter("@AD_Column_ID_DocStatus", docStatusColumnId),
                new SqlParameter("@Search_Status", SqlDbType.NVarChar) { Value = likeValue },
                new SqlParameter("@AD_Language_Type", SqlDbType.NVarChar) { Value = language },
                new SqlParameter("@AD_Column_ID_ContractType", contractTypeColumnId),
                new SqlParameter("@Search_Type", SqlDbType.NVarChar) { Value = likeValue },
                new SqlParameter("@AD_Language_Renewal", SqlDbType.NVarChar) { Value = language },
                new SqlParameter("@AD_Column_ID_RenewalType", renewalTypeColumnId),
                new SqlParameter("@Search_Renewal", SqlDbType.NVarChar) { Value = likeValue },
                new SqlParameter("@BaseCurrency_ID", baseCurrencyId)
            };
        }

        /// <summary>
        /// The tenant's accounting-schema (base) currency - same AD_ClientInfo ->
        /// C_AcctSchema1 -> C_Currency resolution the sibling KPI widgets
        /// (VAS_243/244/246) already use, so every "Value" figure shown for a
        /// contract - the search row, the detail modal's Value/Billed/Unbilled/
        /// schedule amounts - reconciles to the same tenant currency instead of
        /// each contract's own transaction currency.
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
        /// Runs the shared search predicate for both the dropdown (offset 0) and
        /// the "See all" paged list, then decodes DocStatus/ContractType/RenewalType
        /// through the cached AD_Ref_List(_Trl) maps before the rows ever leave the
        /// server - raw codes are never shipped to the client.
        /// </summary>
        private ContractSearchResult SearchContractsData(Ctx ctx, string searchText, int offset, int limit)
        {
            ContractSearchResult result = new ContractSearchResult
            {
                Rows = new List<ContractSearchRow>(),
                Total = 0
            };

            if (ctx == null) { return result; }

            searchText = (searchText ?? "").Trim();
            if (searchText.Length == 0) { return result; }

            if (limit <= 0 || limit > PageSize) { limit = PageSize; }

            int clientId = ctx.GetAD_Client_ID();
            string language = GetLanguage(ctx);
            string likeValue = "%" + EscapeLike(searchText.ToUpperInvariant()) + "%";

            int docStatusColumnId = GetColumnId("C_Contract", "DocStatus");
            int contractTypeColumnId = GetColumnId("C_Contract", "ContractType");
            int renewalTypeColumnId = GetColumnId("C_Contract", "RenewalType");

            int baseCurrencyId; string baseCurrencyIso, baseCurrencySymbol; int baseCurrencyPrecision;
            ResolveBaseCurrency(ctx, out baseCurrencyId, out baseCurrencyIso, out baseCurrencySymbol, out baseCurrencyPrecision);

            string baseSql = MRole.GetDefault(ctx).AddAccessSQL(BuildProjectionSql(), "co", MRole.SQL_FULLYQUALIFIED, MRole.SQL_RO);

            string suggestSql = baseSql + @"
                ORDER BY co.EndDate ASC
                OFFSET @Off ROWS FETCH NEXT @Lim ROWS ONLY";

            string countSql = "SELECT COUNT(1) AS Total_Count FROM ( " + baseSql + " ) cnt_wrap";

            Dictionary<string, string> docStatusMap = GetDecodeMap("C_Contract", "DocStatus", language);
            Dictionary<string, string> contractTypeMap = GetDecodeMap("C_Contract", "ContractType", language);
            Dictionary<string, string> renewalTypeMap = GetDecodeMap("C_Contract", "RenewalType", language);

            IDataReader dr = null;
            try
            {
                List<SqlParameter> suggestParams = new List<SqlParameter>(
                    BuildSearchParameters(clientId, likeValue, language, docStatusColumnId, contractTypeColumnId, renewalTypeColumnId, baseCurrencyId));
                suggestParams.Add(new SqlParameter("@Off", offset));
                suggestParams.Add(new SqlParameter("@Lim", limit));

                dr = DB.ExecuteReader(suggestSql, suggestParams.ToArray());
                while (dr != null && dr.Read())
                {
                    result.Rows.Add(ReadRow(ctx, dr, docStatusMap, contractTypeMap, renewalTypeMap, baseCurrencyIso, baseCurrencySymbol, baseCurrencyPrecision));
                }
            }
            finally
            {
                CloseReader(dr);
            }

            IDataReader countReader = null;
            try
            {
                countReader = DB.ExecuteReader(countSql,
                    BuildSearchParameters(clientId, likeValue, language, docStatusColumnId, contractTypeColumnId, renewalTypeColumnId, baseCurrencyId));
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
        /// The full drill-down payload for one contract, in three MRole-scoped
        /// steps: (1) the header - a direct single-row lookup by id, MRole-secured
        /// on alias "co", enriched with the billing contact (Bill_User_ID),
        /// representative (SalesRep_ID) and billing frequency (C_Frequency_ID);
        /// (2) its C_ContractSchedule billing periods, a child of that already-
        /// verified header (no independent MRole - the same "secondary source"
        /// treatment the search predicate already gives C_BPartner/M_Product);
        /// Billed/Unbilled/the earliest-overdue period are all derived from those
        /// rows in C#, never a second query; (3) the customer's open-ticket count
        /// (R_Request has no C_Contract_ID in this schema, so this is scoped to
        /// the contract's C_BPartner_ID, mirroring VAS_126's open-ticket count).
        /// Returns ContractId 0 (never an exception detail) when the id is missing
        /// or inaccessible.
        /// </summary>
        private ContractDetail GetContractDetailData(Ctx ctx, int contractId)
        {
            ContractDetail detail = new ContractDetail { Schedule = new List<ScheduleRow>() };

            if (ctx == null || contractId <= 0) { return detail; }

            string language = GetLanguage(ctx);
            Dictionary<string, string> docStatusMap = GetDecodeMap("C_Contract", "DocStatus", language);
            Dictionary<string, string> contractTypeMap = GetDecodeMap("C_Contract", "ContractType", language);
            Dictionary<string, string> renewalTypeMap = GetDecodeMap("C_Contract", "RenewalType", language);

            int baseCurrencyId; string baseCurrencyIso, baseCurrencySymbol; int baseCurrencyPrecision;
            ResolveBaseCurrency(ctx, out baseCurrencyId, out baseCurrencyIso, out baseCurrencySymbol, out baseCurrencyPrecision);

            string headerSql = @"
                SELECT co.C_Contract_ID  AS Contract_Id,
                       co.DocumentNo     AS Document_No,
                       co.C_BPartner_ID  AS Customer_Id,
                       COALESCE(bp.Name, N'') AS Customer_Name,
                       COALESCE(pr.Name, N'') AS Product_Name,
                       COALESCE(contact.Name, N'') AS Contact_Name,
                       COALESCE(rep.Name, N'') AS Sales_Rep_Name,
                       COALESCE(freq.Name, N'') AS Frequency_Name,
                       co.Cycles          AS Cycles,
                       co.CancelBeforeDays AS Notice_Days,
                       CurrencyConvert(co.GrandTotal, co.C_Currency_ID, @BaseCurrency_ID,
                              CURRENT_DATE, co.C_ConversionType_ID, co.AD_Client_ID, co.AD_Org_ID) AS Grand_Total,
                       co.C_Currency_ID  AS Currency_Id,
                       co.C_ConversionType_ID AS Conversion_Type_Id,
                       co.AD_Org_ID      AS Ad_Org_Id,
                       co.ContractType   AS Contract_Type_Code,
                       co.DocStatus      AS Doc_Status_Code,
                       co.IsCancel       AS Is_Cancel,
                       co.Processed      AS Is_Processed,
                       co.RenewalType    AS Renewal_Type_Code,
                       co.EndDate        AS End_Date,
                       CAST(DAYSBETWEEN(co.EndDate, CURRENT_DATE) AS INTEGER) AS Days_To_End,
                       CASE WHEN EXISTS (
                              SELECT 1 FROM C_Contract succ
                               WHERE succ.Ref_Contract_ID = co.C_Contract_ID
                                 AND succ.IsActive = 'Y'
                                 AND succ.AD_Client_ID = co.AD_Client_ID )
                            THEN 'Y' ELSE 'N' END AS Has_Successor
                  FROM C_Contract co
                  LEFT JOIN C_BPartner bp   ON ( bp.C_BPartner_ID   = co.C_BPartner_ID )
                  LEFT JOIN M_Product  pr   ON ( pr.M_Product_ID    = co.M_Product_ID )
                  LEFT JOIN AD_User contact ON ( contact.AD_User_ID = co.Bill_User_ID )
                  LEFT JOIN AD_User rep     ON ( rep.AD_User_ID     = co.SalesRep_ID )
                  LEFT JOIN C_Frequency freq ON ( freq.C_Frequency_ID = co.C_Frequency_ID )
                 WHERE co.C_Contract_ID = @Contract_ID
                   AND co.AD_Client_ID = @AD_Client_ID
                   AND co.IsActive = 'Y'";

            headerSql = MRole.GetDefault(ctx).AddAccessSQL(headerSql, "co", MRole.SQL_FULLYQUALIFIED, MRole.SQL_RO);

            int contractCurrencyId = 0, conversionTypeId = 0, orgId = 0;

            IDataReader dr = null;
            try
            {
                dr = DB.ExecuteReader(headerSql, new[]
                {
                    new SqlParameter("@Contract_ID", contractId),
                    new SqlParameter("@AD_Client_ID", ctx.GetAD_Client_ID()),
                    new SqlParameter("@BaseCurrency_ID", baseCurrencyId)
                });

                if (dr == null || !dr.Read()) { return detail; }

                string docStatusCode = Util.GetValueOfString(dr["Doc_Status_Code"]);
                string contractTypeCode = Util.GetValueOfString(dr["Contract_Type_Code"]);
                string renewalTypeCode = Util.GetValueOfString(dr["Renewal_Type_Code"]);
                DateTime? endDate = dr["End_Date"] == DBNull.Value ? (DateTime?)null : Util.GetValueOfDateTime(dr["End_Date"]);

                contractCurrencyId = Util.GetValueOfInt(dr["Currency_Id"]);
                conversionTypeId = dr["Conversion_Type_Id"] == DBNull.Value ? 0 : Util.GetValueOfInt(dr["Conversion_Type_Id"]);
                orgId = Util.GetValueOfInt(dr["Ad_Org_Id"]);

                detail.ContractId = Util.GetValueOfInt(dr["Contract_Id"]);
                detail.DocumentNo = Util.GetValueOfString(dr["Document_No"]);
                detail.CustomerId = Util.GetValueOfInt(dr["Customer_Id"]);
                detail.CustomerName = Util.GetValueOfString(dr["Customer_Name"]);
                detail.ProductName = Util.GetValueOfString(dr["Product_Name"]);
                detail.ContactName = Util.GetValueOfString(dr["Contact_Name"]);
                detail.SalesRepName = Util.GetValueOfString(dr["Sales_Rep_Name"]);
                detail.FrequencyName = Util.GetValueOfString(dr["Frequency_Name"]);
                detail.Cycles = dr["Cycles"] == DBNull.Value ? (int?)null : Util.GetValueOfInt(dr["Cycles"]);
                detail.NoticeDays = dr["Notice_Days"] == DBNull.Value ? (int?)null : Util.GetValueOfInt(dr["Notice_Days"]);
                detail.GrandTotal = dr["Grand_Total"] == DBNull.Value ? 0m : Util.GetValueOfDecimal(dr["Grand_Total"]);
                detail.CurrencyIso = baseCurrencyIso;
                detail.CurrencySymbol = baseCurrencySymbol;
                detail.CurrencyPrecision = baseCurrencyPrecision;
                detail.ContractTypeCode = contractTypeCode;
                detail.ContractType = DecodeLabel(contractTypeMap, contractTypeCode);
                detail.DocStatusCode = docStatusCode;
                detail.DocStatus = DecodeLabel(docStatusMap, docStatusCode);
                detail.RenewalTypeCode = renewalTypeCode;
                detail.RenewalType = DecodeLabel(renewalTypeMap, renewalTypeCode);
                detail.EndDate = endDate.HasValue ? endDate.Value.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture) : "";
                detail.DaysToEnd = dr["Days_To_End"] == DBNull.Value ? 0 : Util.GetValueOfInt(dr["Days_To_End"]);
                detail.HasSuccessor = "Y".Equals(Util.GetValueOfString(dr["Has_Successor"]), StringComparison.OrdinalIgnoreCase);

                bool isCancel = "Y".Equals(Util.GetValueOfString(dr["Is_Cancel"]), StringComparison.OrdinalIgnoreCase);
                bool isProcessed = "Y".Equals(Util.GetValueOfString(dr["Is_Processed"]), StringComparison.OrdinalIgnoreCase);
                string lifecycleCode = ComputeLifecycleStatusCode(isCancel, docStatusCode, isProcessed, detail.DaysToEnd);
                detail.LifecycleStatusCode = lifecycleCode;
                detail.LifecycleStatus = LifecycleStatusLabel(ctx, lifecycleCode, detail.DocStatus);

                // Manual-renewal notice window (kpi-renewal-action.queries.md §D-2's
                // NoticeSlackDays): daysToEnd - CancelBeforeDays. <=0 means the notice
                // window is open (0 = due today, <0 = overdue); the detail modal's
                // "Renewal notice" attention card reads this together with
                // RenewalTypeCode='M' and !HasSuccessor. Left null when there is no
                // notice length to compare against.
                detail.NoticeSlackDays = detail.NoticeDays.HasValue ? (int?)(detail.DaysToEnd - detail.NoticeDays.Value) : null;
            }
            finally
            {
                CloseReader(dr);
            }

            if (detail.ContractId <= 0) { return detail; }

            LoadSchedule(ctx, detail, contractCurrencyId, conversionTypeId, orgId, baseCurrencyId);
            detail.OpenTicketCount = GetOpenTicketCount(ctx, detail.CustomerId);

            return detail;
        }

        /// <summary>
        /// C_ContractSchedule rows for one contract (a child of the already-verified
        /// header, so no independent MRole - see <see cref="GetContractDetailData"/>),
        /// oldest period first. Billed/Unbilled and the earliest overdue unbilled
        /// period (FROMDATE already passed, C_Invoice_ID still null - the same test
        /// CreateContractInvoice itself uses to pick up a period) are derived here,
        /// not in a second query.
        /// </summary>
        private void LoadSchedule(Ctx ctx, ContractDetail detail, int contractCurrencyId, int conversionTypeId, int orgId, int baseCurrencyId)
        {
            string sql = @"
                SELECT cs.C_ContractSchedule_ID AS Schedule_Id,
                       cs.FROMDATE  AS From_Date,
                       cs.EndDate   AS Sched_End_Date,
                       CurrencyConvert(cs.GrandTotal, @Contract_Currency_ID, @BaseCurrency_ID,
                              CURRENT_DATE, @Conversion_Type_ID, cs.AD_Client_ID, @AD_Org_ID) AS Grand_Total,
                       cs.C_Invoice_ID AS Invoice_Id
                  FROM C_ContractSchedule cs
                 WHERE cs.C_Contract_ID = @Contract_ID
                   AND cs.AD_Client_ID = @AD_Client_ID
                   AND cs.IsActive = 'Y'
                 ORDER BY cs.FROMDATE ASC, cs.C_ContractSchedule_ID ASC";

            IDataReader dr = null;
            try
            {
                dr = DB.ExecuteReader(sql, new[]
                {
                    new SqlParameter("@Contract_ID", detail.ContractId),
                    new SqlParameter("@AD_Client_ID", ctx.GetAD_Client_ID()),
                    new SqlParameter("@Contract_Currency_ID", contractCurrencyId),
                    new SqlParameter("@BaseCurrency_ID", baseCurrencyId),
                    new SqlParameter("@Conversion_Type_ID", conversionTypeId),
                    new SqlParameter("@AD_Org_ID", orgId)
                });

                int periodNumber = 0;
                DateTime today = DateTime.Today;
                DateTime? earliestOverdueFromDate = null;

                while (dr != null && dr.Read())
                {
                    periodNumber++;
                    decimal amount = dr["Grand_Total"] == DBNull.Value ? 0m : Util.GetValueOfDecimal(dr["Grand_Total"]);
                    bool invoiced = dr["Invoice_Id"] != DBNull.Value && Util.GetValueOfInt(dr["Invoice_Id"]) > 0;
                    DateTime? fromDate = dr["From_Date"] == DBNull.Value ? (DateTime?)null : Util.GetValueOfDateTime(dr["From_Date"]);
                    DateTime? toDate = dr["Sched_End_Date"] == DBNull.Value ? (DateTime?)null : Util.GetValueOfDateTime(dr["Sched_End_Date"]);

                    detail.Schedule.Add(new ScheduleRow
                    {
                        Period = periodNumber,
                        FrequencyName = detail.FrequencyName,
                        Amount = amount,
                        Invoiced = invoiced,
                        FromDate = fromDate.HasValue ? fromDate.Value.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture) : "",
                        ToDate = toDate.HasValue ? toDate.Value.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture) : ""
                    });

                    if (invoiced)
                    {
                        detail.Billed += amount;
                    }
                    else
                    {
                        detail.Unbilled += amount;
                        if (fromDate.HasValue && fromDate.Value.Date < today
                            && (!earliestOverdueFromDate.HasValue || fromDate.Value.Date < earliestOverdueFromDate.Value))
                        {
                            earliestOverdueFromDate = fromDate.Value.Date;
                            detail.OverdueDays = (int)(today - fromDate.Value.Date).TotalDays;
                            detail.OverdueAmount = amount;
                        }
                    }
                }
            }
            finally
            {
                CloseReader(dr);
            }
        }

        /// <summary>
        /// Open ticket count for the contract's customer (VAS_126's own open-ticket
        /// predicate: R_Status.IsOpen='Y' and not closed). R_Request carries no
        /// C_Contract_ID in this schema, so this is a customer-level figure, not a
        /// per-contract one - the widget labels it accordingly. MRole is applied to
        /// the main physical table alias "r".
        /// </summary>
        private int GetOpenTicketCount(Ctx ctx, int bPartnerId)
        {
            if (bPartnerId <= 0) { return 0; }

            string sql = @"
                SELECT COUNT(DISTINCT r.R_Request_ID) AS Open_Ticket_Count
                  FROM R_Request r
                  JOIN R_Status s ON ( s.R_Status_ID = r.R_Status_ID AND s.AD_Client_ID = r.AD_Client_ID AND s.IsActive = 'Y' )
                 WHERE r.IsActive = 'Y'
                   AND r.AD_Client_ID = @AD_Client_ID
                   AND r.C_BPartner_ID = @BPartner_ID
                   AND s.IsOpen = 'Y'
                   AND COALESCE(s.IsClosed, 'N') = 'N'";

            sql = MRole.GetDefault(ctx).AddAccessSQL(sql, "r", MRole.SQL_FULLYQUALIFIED, MRole.SQL_RO);

            IDataReader dr = null;
            try
            {
                dr = DB.ExecuteReader(sql, new[]
                {
                    new SqlParameter("@AD_Client_ID", ctx.GetAD_Client_ID()),
                    new SqlParameter("@BPartner_ID", bPartnerId)
                });
                if (dr != null && dr.Read())
                {
                    return Util.GetValueOfInt(dr["Open_Ticket_Count"]);
                }
            }
            finally
            {
                CloseReader(dr);
            }

            return 0;
        }

        /// <summary>Reads one row of the shared projection, decoding all three list columns. GrandTotal arrives already converted to the tenant base currency (see <see cref="BuildProjectionSql"/>); baseCurrencyIso/Symbol/Precision are the resolved display currency for every row.</summary>
        private ContractSearchRow ReadRow(Ctx ctx, IDataReader dr, Dictionary<string, string> docStatusMap, Dictionary<string, string> contractTypeMap, Dictionary<string, string> renewalTypeMap, string baseCurrencyIso, string baseCurrencySymbol, int baseCurrencyPrecision)
        {
            string docStatusCode = Util.GetValueOfString(dr["Doc_Status_Code"]);
            string contractTypeCode = Util.GetValueOfString(dr["Contract_Type_Code"]);
            string renewalTypeCode = Util.GetValueOfString(dr["Renewal_Type_Code"]);
            bool isCancel = "Y".Equals(Util.GetValueOfString(dr["Is_Cancel"]), StringComparison.OrdinalIgnoreCase);
            bool isProcessed = "Y".Equals(Util.GetValueOfString(dr["Is_Processed"]), StringComparison.OrdinalIgnoreCase);

            DateTime? endDate = dr["End_Date"] == DBNull.Value ? (DateTime?)null : Util.GetValueOfDateTime(dr["End_Date"]);
            int daysToEnd = dr["Days_To_End"] == DBNull.Value ? 0 : Util.GetValueOfInt(dr["Days_To_End"]);

            string docStatusLabel = DecodeLabel(docStatusMap, docStatusCode);
            string lifecycleCode = ComputeLifecycleStatusCode(isCancel, docStatusCode, isProcessed, daysToEnd);

            return new ContractSearchRow
            {
                ContractId = Util.GetValueOfInt(dr["Contract_Id"]),
                DocumentNo = Util.GetValueOfString(dr["Document_No"]),
                CustomerName = Util.GetValueOfString(dr["Customer_Name"]),
                ProductName = Util.GetValueOfString(dr["Product_Name"]),
                GrandTotal = dr["Grand_Total"] == DBNull.Value ? 0m : Util.GetValueOfDecimal(dr["Grand_Total"]),
                CurrencyIso = baseCurrencyIso,
                CurrencySymbol = baseCurrencySymbol,
                CurrencyPrecision = baseCurrencyPrecision,
                ContractTypeCode = contractTypeCode,
                ContractType = DecodeLabel(contractTypeMap, contractTypeCode),
                DocStatusCode = docStatusCode,
                DocStatus = docStatusLabel,
                LifecycleStatusCode = lifecycleCode,
                LifecycleStatus = LifecycleStatusLabel(ctx, lifecycleCode, docStatusLabel),
                RenewalTypeCode = renewalTypeCode,
                RenewalType = DecodeLabel(renewalTypeMap, renewalTypeCode),
                EndDate = endDate.HasValue ? endDate.Value.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture) : "",
                DaysToEnd = daysToEnd
            };
        }

        /// <summary>
        /// The tile/detail "Status" badge is a computed LIFECYCLE read-out, not
        /// the raw DocStatus decode - a live (Processed='Y') contract reads
        /// Active / Expiring (≤90 days, matching VAS_244's own threshold) /
        /// Ended by EndDate; IsCancel='Y' always reads Cancelled; Voided/Reversed
        /// are their own terminal states; any not-yet-processed contract is
        /// DRAFT, so the raw decoded DocStatus label is shown instead (see
        /// <see cref="LifecycleStatusLabel"/>). Gates on Processed='Y', NOT
        /// DocStatus=MContract.DOCSTATUS_Completed - verified against real data,
        /// same reason every other Service Contracts widget already reads
        /// Processed instead of DocStatus='CO' (see VAS_244's own note): real
        /// completed contracts on this install don't reliably carry DocStatus=
        /// 'CO', so gating on it here was mislabelling live, already-executed
        /// contracts as "Drafted" in this shared detail dialog.
        /// </summary>
        private static string ComputeLifecycleStatusCode(bool isCancel, string docStatusCode, bool isProcessed, int daysToEnd)
        {
            if (isCancel) { return "CANCELLED"; }

            string code = (docStatusCode ?? "").ToUpperInvariant();
            if (code == "VO") { return "VOIDED"; }
            if (code == "RE") { return "REVERSED"; }
            if (!isProcessed) { return "DRAFT"; }

            if (daysToEnd < 0) { return "ENDED"; }
            if (daysToEnd <= 90) { return "EXPIRING"; }
            return "ACTIVE";
        }

        /// <summary>Display text for one lifecycle status code - DRAFT falls back to the real decoded DocStatus label (it is not a lifecycle band).</summary>
        private static string LifecycleStatusLabel(Ctx ctx, string lifecycleCode, string docStatusLabel)
        {
            switch (lifecycleCode)
            {
                case "CANCELLED": return SafeMsg(ctx, "VAS_241_StatusCancelled", "Cancelled");
                case "VOIDED": return SafeMsg(ctx, "VAS_241_StatusVoided", "Voided");
                case "REVERSED": return SafeMsg(ctx, "VAS_241_StatusReversed", "Reversed");
                case "ENDED": return SafeMsg(ctx, "VAS_241_StatusEnded", "Ended");
                case "EXPIRING": return SafeMsg(ctx, "VAS_241_StatusExpiring", "Expiring");
                case "ACTIVE": return SafeMsg(ctx, "VAS_241_StatusActive", "Active");
                default: return docStatusLabel;
            }
        }

        /// <summary>
        /// Msg.GetMsg does not return null for an AD_Message key that isn't
        /// registered - it returns the key itself wrapped in placeholder markup
        /// (e.g. "[VAS_241_StatusActive]"), so the usual "?? fallback" pattern
        /// never engages and that raw placeholder was rendering straight into
        /// the Status badge/stat. Treat any result that still contains the key
        /// text as "not found" and use the English fallback instead.
        /// </summary>
        private static string SafeMsg(Ctx ctx, string key, string fallback)
        {
            string msg = Msg.GetMsg(ctx, key);
            if (string.IsNullOrEmpty(msg) || msg.IndexOf(key, StringComparison.OrdinalIgnoreCase) >= 0) { return fallback; }
            return msg;
        }

        /// <summary>The tenant's own label for a stored code, or the code itself when the dictionary carries no entry (never invented English).</summary>
        private static string DecodeLabel(Dictionary<string, string> map, string code)
        {
            if (string.IsNullOrEmpty(code)) { return ""; }
            string label;
            return map != null && map.TryGetValue(code, out label) ? label : code;
        }

        /// <summary>
        /// AD_Ref_List(_Trl) Code -> Label map for one C_Contract column, cached per
        /// column+language (contract-search.queries.md §D-3). Never a hard-coded
        /// list of codes - resolved from the tenant's own application dictionary.
        /// </summary>
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

        /// <summary>AD_Column_ID for one table/column pair, resolved once and cached alongside the decode maps (never hard-coded).</summary>
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

        /// <summary>Escapes the LIKE metacharacters (and the escape character itself) so user text is matched literally under an explicit ESCAPE '\' clause.</summary>
        private static string EscapeLike(string text)
        {
            if (string.IsNullOrEmpty(text)) { return text; }
            return text.Replace("\\", "\\\\").Replace("%", "\\%").Replace("_", "\\_");
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

        private class ContractSearchResult
        {
            public List<ContractSearchRow> Rows { get; set; }
            public int Total { get; set; }
        }

        private class ContractSearchRow
        {
            public int ContractId { get; set; }
            public string DocumentNo { get; set; }
            public string CustomerName { get; set; }
            public string ProductName { get; set; }
            public decimal GrandTotal { get; set; }
            public string CurrencyIso { get; set; }
            public string CurrencySymbol { get; set; }
            public int CurrencyPrecision { get; set; }
            public string ContractTypeCode { get; set; }
            public string ContractType { get; set; }
            public string DocStatusCode { get; set; }
            public string DocStatus { get; set; }
            public string LifecycleStatusCode { get; set; }
            public string LifecycleStatus { get; set; }
            public string RenewalTypeCode { get; set; }
            public string RenewalType { get; set; }
            public string EndDate { get; set; }
            public int DaysToEnd { get; set; }
        }

        private class ContractDetail
        {
            public int ContractId { get; set; }
            public string DocumentNo { get; set; }
            public int CustomerId { get; set; }
            public string CustomerName { get; set; }
            public string ProductName { get; set; }
            public string ContactName { get; set; }
            public string SalesRepName { get; set; }
            public string FrequencyName { get; set; }
            public int? Cycles { get; set; }
            public int? NoticeDays { get; set; }
            public decimal GrandTotal { get; set; }
            public string CurrencyIso { get; set; }
            public string CurrencySymbol { get; set; }
            public int CurrencyPrecision { get; set; }
            public string ContractTypeCode { get; set; }
            public string ContractType { get; set; }
            public string DocStatusCode { get; set; }
            public string DocStatus { get; set; }
            public string LifecycleStatusCode { get; set; }
            public string LifecycleStatus { get; set; }
            public string RenewalTypeCode { get; set; }
            public string RenewalType { get; set; }
            public string EndDate { get; set; }
            public int DaysToEnd { get; set; }
            public decimal Billed { get; set; }
            public decimal Unbilled { get; set; }
            public int OverdueDays { get; set; }
            public decimal OverdueAmount { get; set; }
            public int OpenTicketCount { get; set; }
            public bool HasSuccessor { get; set; }
            public int? NoticeSlackDays { get; set; }
            public List<ScheduleRow> Schedule { get; set; }
        }

        private class ScheduleRow
        {
            public int Period { get; set; }
            public string FrequencyName { get; set; }
            public decimal Amount { get; set; }
            public bool Invoiced { get; set; }
            public string FromDate { get; set; }
            public string ToDate { get; set; }
        }

        private class ProcessRunResult
        {
            public bool Success { get; set; }
            public string Message { get; set; }
        }
    }
}
