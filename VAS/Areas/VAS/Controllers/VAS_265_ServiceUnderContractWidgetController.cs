/******************************************************
 * Module Name    : VAS
 * Purpose        : Service Contracts module Service under contract widget endpoints
 * chronological  : Development
 * Created Date   : 2026-09-09
 * Created by     : VAI052
 ******************************************************/

using Newtonsoft.Json;
using System;
using System.Collections.Generic;
using System.Data;
using System.Data.SqlClient;
using System.Web.Mvc;
using VAdvantage.DataBase;
using VAdvantage.Logging;
using VAdvantage.Model;
using VAdvantage.Utility;
using VIS.Filters;

namespace VAS.Controllers
{
    /// <summary>
    /// Module Name : VAS_265_ServiceUnderContractWidget
    /// Purpose     : Data endpoints for the c3 x r2 "Service under contract"
    ///               triage-list widget on the Service Contracts dashboard:
    ///               (1) the header banner - the open-ticket total and covered-
    ///               asset total across the widget's own row population, (2)
    ///               the paged (@4, most-loaded-first) rows the widget body
    ///               always shows (also reused for the header "All" list
    ///               modal), and (3) every matching Contract_ID (capped) so
    ///               "Open in browser" can filter the Service Contract window
    ///               down to exactly this population via a TabWhereClause
    ///               IN-list. Set = live (co.Processed='Y' AND IsCancel='N'
    ///               AND EndDate&gt;=CURRENT_DATE) contracts that carry at
    ///               least one open ticket (R_Status.IsOpen='Y'), sorted by
    ///               open-ticket count DESC then EndDate ASC. Coverage counts
    ///               (open tickets, covered assets) are correlated
    ///               subqueries, never joins, so MRole anchors cleanly on
    ///               "co" with one row per contract (service-under-
    ///               contract.queries.md A.2/E). A row's detail (its Renew /
    ///               Generate invoice actions) reuses the already-built
    ///               VAS_241_ContractSearchWidget/GetContract, RunRenew and
    ///               RunGenerateInvoice endpoints rather than duplicating that
    ///               logic - the same shared-endpoint reuse VAS_120 set with
    ///               VAS_126 and VAS_244/245/246/258/259/264 already reused
    ///               too. Read-only here; no writes.
    /// Chronological development:
    ///   VAI052      2026-09-09 Created
    ///   VAI052      2026-09-09 "Live" reads co.Processed='Y' instead of
    ///                          co.DocStatus=:DocStatus_CO - the Service
    ///                          Contract window itself does not rely on
    ///                          DocStatus for this (verified against real
    ///                          data, the same fix already applied to
    ///                          VAS_243/244/245/246/258/259/260/261/262/263/264).
    ///   VAI052      2026-09-09 The open-ticket correlation does NOT use
    ///                          R_Request.C_Contract_ID as the build spec
    ///                          assumes: VAS_241_ContractSearchWidget already
    ///                          established, against the real schema, that
    ///                          R_Request carries no C_Contract_ID column at
    ///                          all (it fell back to a customer-level
    ///                          C_BPartner_ID count). Per explicit direction,
    ///                          this widget instead correlates a ticket to a
    ///                          contract through BOTH R_Request.C_BPartner_ID
    ///                          = co.C_BPartner_ID AND R_Request.M_Product_ID
    ///                          = co.M_Product_ID - a closer per-contract
    ///                          proxy than VAS_241's pure customer-level
    ///                          count (a customer with several contracts for
    ///                          different products no longer double-counts
    ///                          every contract with the same total; it can
    ///                          still over-count when two live contracts for
    ///                          the SAME customer+product overlap, since
    ///                          neither R_Request column can disambiguate
    ///                          further without a direct contract FK).
    ///   VAI052      2026-09-09 VA075_WorkOrder's optional coverage count
    ///                          (build spec section 3/queries.md D-2, flagged
    ///                          up-front as unverified) is omitted entirely -
    ///                          that module (VA075_ServiceAndMaintenance)
    ///                          is not part of this solution/schema at all
    ///                          (no VA075_WorkOrder table exists here), so
    ///                          there is no FK to even attempt, per the
    ///                          spec's own "degrade gracefully - drop it"
    ///                          instruction.
    /// </summary>
    public class VAS_265_ServiceUnderContractWidgetController : Controller
    {
        private static readonly VLogger Log = VLogger.GetVLogger(typeof(VAS_265_ServiceUnderContractWidgetController).FullName);

        private const int DrillPageSize = 4;

        // Upper bound for the "Open in browser" TabWhereClause IN-list (GetContractIdsData) -
        // this widget's population is naturally small; this just bounds a pathological case.
        private const int MaxZoomIds = 1000;

        /// <summary>
        /// Header banner scalars: the open-ticket total and covered-asset
        /// total across this widget's own row population.
        /// </summary>
        /// <returns>JSON { OpenTicketTotal, AssetTotal } or { Error }.</returns>
        [AjaxAuthorizeAttribute]
        [AjaxSessionFilterAttribute]
        public JsonResult GetServiceUnderContractSummary()
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
                Log.Log(Level.SEVERE, "VAS_265_ServiceUnderContractWidget.GetServiceUnderContractSummary", ex);
                return ErrorResult(ctx);
            }
        }

        /// <summary>
        /// Paged rows for the widget, most-loaded (open-ticket count DESC)
        /// first - the identical query backs both the widget's own always-
        /// visible @4 body and the header "All" list modal.
        /// </summary>
        /// <param name="offset">Zero-based paging offset.</param>
        /// <param name="limit">Page size - 4 for both the widget body and the "All" list modal (capped at <see cref="MaxZoomIds"/>).</param>
        /// <returns>JSON { Rows:[...], Total } or { Error }.</returns>
        [AjaxAuthorizeAttribute]
        [AjaxSessionFilterAttribute]
        public JsonResult GetServiceUnderContractContracts(int offset = 0, int limit = DrillPageSize)
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
                Log.Log(Level.SEVERE, "VAS_265_ServiceUnderContractWidget.GetServiceUnderContractContracts", ex);
                return ErrorResult(ctx);
            }
        }

        /// <summary>
        /// Every matching C_Contract_ID for the live-with-open-ticket
        /// predicate (not just one page), capped at <see cref="MaxZoomIds"/> -
        /// so "Open in browser" can filter the Service Contract window down
        /// to exactly this widget's population via a TabWhereClause IN-list.
        /// </summary>
        /// <returns>JSON { Ids:[...] } or { Error }.</returns>
        [AjaxAuthorizeAttribute]
        [AjaxSessionFilterAttribute]
        public JsonResult GetServiceUnderContractContractIds()
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
                Log.Log(Level.SEVERE, "VAS_265_ServiceUnderContractWidget.GetServiceUnderContractContractIds", ex);
                return ErrorResult(ctx);
            }
        }

        /// <summary>
        /// The correlated open-ticket EXISTS probe shared by every query
        /// below - a ticket is attributed to a contract by matching BOTH
        /// R_Request.C_BPartner_ID and R_Request.M_Product_ID against the
        /// contract's own columns (R_Request carries no C_Contract_ID in
        /// this schema - see the class file-header note). Not an MRole
        /// alias; the tenant filter is repeated on the driving table itself
        /// (service-under-contract.queries.md A.3).
        /// </summary>
        /// <param name="correlationAlias">The C_Contract alias in scope at the call site ("co" for the row-set predicate, the same alias reused verbatim for the per-row correlated COUNT).</param>
        private static string OpenTicketExistsSql(string correlationAlias)
        {
            return @"
                EXISTS ( SELECT 1 FROM R_Request rq
                           INNER JOIN R_Status rs ON ( rs.R_Status_ID = rq.R_Status_ID AND rs.AD_Client_ID = rq.AD_Client_ID AND rs.IsActive = 'Y' )
                          WHERE rq.C_BPartner_ID = " + correlationAlias + @".C_BPartner_ID
                            AND rq.M_Product_ID  = " + correlationAlias + @".M_Product_ID
                            AND rq.AD_Client_ID  = " + correlationAlias + @".AD_Client_ID
                            AND rq.IsActive = 'Y'
                            AND rs.IsOpen = 'Y' )";
        }

        /// <summary>
        /// The shared live-with-open-ticket predicate, WHERE-only - every
        /// caller applies MRole to alias "co" itself right after calling
        /// this. "Live" is Processed='Y' (not
        /// DocStatus=MContract.DOCSTATUS_Completed - the Service Contract
        /// window does not rely on that column, verified against real data,
        /// the same fix already applied to
        /// VAS_243/244/245/246/258/259/260/261/262/263/264) AND IsCancel='N'
        /// AND EndDate &gt;= CURRENT_DATE.
        /// </summary>
        /// <returns>WHERE-clause text (binds @AD_Client_ID).</returns>
        private static string LiveWithTicketWhereSql()
        {
            return @"
                   co.AD_Client_ID = @AD_Client_ID
               AND co.IsActive = 'Y'
               AND co.Processed = 'Y'
               AND co.IsCancel = 'N'
               AND co.EndDate >= CURRENT_DATE
               AND " + OpenTicketExistsSql("co");
        }

        /// <summary>
        /// The correlated open-ticket COUNT for one contract row, using the
        /// identical correlation as <see cref="OpenTicketExistsSql"/>.
        /// </summary>
        private static string OpenTicketCountSql()
        {
            return @"
                ( SELECT COUNT(DISTINCT rq.R_Request_ID)
                    FROM R_Request rq
                    INNER JOIN R_Status rs ON ( rs.R_Status_ID = rq.R_Status_ID AND rs.AD_Client_ID = rq.AD_Client_ID AND rs.IsActive = 'Y' )
                   WHERE rq.C_BPartner_ID = co.C_BPartner_ID
                     AND rq.M_Product_ID  = co.M_Product_ID
                     AND rq.AD_Client_ID  = co.AD_Client_ID
                     AND rq.IsActive = 'Y'
                     AND rs.IsOpen = 'Y' )";
        }

        /// <summary>The correlated covered-asset COUNT for one contract row (A_Asset.C_Contract_ID is a direct FK).</summary>
        private static string AssetCountSql()
        {
            return @"
                ( SELECT COUNT(*)
                    FROM A_Asset a
                   WHERE a.C_Contract_ID = co.C_Contract_ID
                     AND a.AD_Client_ID = co.AD_Client_ID
                     AND a.IsActive = 'Y' )";
        }

        /// <summary>
        /// Fresh parameter array for one command execution against
        /// <see cref="LiveWithTicketWhereSql"/> - every placeholder occurs
        /// exactly once in each assembled statement, so no name repeats
        /// under Oracle's positional binding.
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
        /// Resolves the open-ticket total and covered-asset total across
        /// this widget's own live-with-open-ticket row population.
        /// </summary>
        /// <param name="ctx">Session context.</param>
        private ServiceUnderContractSummary GetSummaryData(Ctx ctx)
        {
            ServiceUnderContractSummary result = new ServiceUnderContractSummary();
            if (ctx == null) { return result; }

            string sql = @"
                SELECT SUM( " + OpenTicketCountSql() + @" ) AS Open_Ticket_Total,
                       SUM( " + AssetCountSql() + @" ) AS Asset_Total
                  FROM C_Contract co
                 WHERE " + LiveWithTicketWhereSql();

            sql = MRole.GetDefault(ctx).AddAccessSQL(sql, "co", MRole.SQL_FULLYQUALIFIED, MRole.SQL_RO);

            IDataReader dr = null;
            try
            {
                dr = DB.ExecuteReader(sql, BuildPredicateParameters(ctx));
                if (dr != null && dr.Read())
                {
                    result.OpenTicketTotal = dr["Open_Ticket_Total"] == DBNull.Value ? 0 : Util.GetValueOfInt(dr["Open_Ticket_Total"]);
                    result.AssetTotal = dr["Asset_Total"] == DBNull.Value ? 0 : Util.GetValueOfInt(dr["Asset_Total"]);
                }
            }
            finally
            {
                CloseReader(dr);
            }

            return result;
        }

        /// <summary>
        /// Paged live-with-open-ticket rows, most-loaded first
        /// (service-under-contract.queries.md D-2) - the base SQL is WHERE-
        /// only until MRole is applied to alias "co", then ORDER BY /
        /// OFFSET..FETCH are appended.
        /// </summary>
        /// <param name="ctx">Session context.</param>
        /// <param name="offset">Zero-based paging offset.</param>
        /// <param name="limit">Page size.</param>
        private ServiceUnderContractDrillResult GetDrillData(Ctx ctx, int offset, int limit)
        {
            ServiceUnderContractDrillResult result = new ServiceUnderContractDrillResult { Rows = new List<ServiceUnderContractDrillRow>() };
            if (ctx == null) { return result; }

            string baseSql = @"
                SELECT co.C_Contract_ID      AS Contract_Id,
                       co.DocumentNo         AS Document_No,
                       COALESCE(bp.Name, N'') AS Customer_Name,
                       " + OpenTicketCountSql() + @" AS Open_Ticket_Count,
                       " + AssetCountSql() + @" AS Asset_Count
                  FROM C_Contract co
                  LEFT OUTER JOIN C_BPartner bp ON ( bp.C_BPartner_ID = co.C_BPartner_ID )
                 WHERE " + LiveWithTicketWhereSql();

            baseSql = MRole.GetDefault(ctx).AddAccessSQL(baseSql, "co", MRole.SQL_FULLYQUALIFIED, MRole.SQL_RO);

            string listSql = baseSql + @"
                ORDER BY Open_Ticket_Count DESC, co.EndDate ASC
                OFFSET @Off ROWS FETCH NEXT @Lim ROWS ONLY";

            string countSql = "SELECT COUNT(1) AS Total_Count FROM ( " + baseSql + " ) cnt_wrap";

            IDataReader dr = null;
            try
            {
                List<SqlParameter> parameters = new List<SqlParameter>(BuildPredicateParameters(ctx));
                parameters.Add(new SqlParameter("@Off", offset));
                parameters.Add(new SqlParameter("@Lim", limit));

                dr = DB.ExecuteReader(listSql, parameters.ToArray());
                while (dr != null && dr.Read())
                {
                    result.Rows.Add(new ServiceUnderContractDrillRow
                    {
                        ContractId = Util.GetValueOfInt(dr["Contract_Id"]),
                        DocumentNo = Util.GetValueOfString(dr["Document_No"]),
                        CustomerName = Util.GetValueOfString(dr["Customer_Name"]),
                        OpenTicketCount = dr["Open_Ticket_Count"] == DBNull.Value ? 0 : Util.GetValueOfInt(dr["Open_Ticket_Count"]),
                        AssetCount = dr["Asset_Count"] == DBNull.Value ? 0 : Util.GetValueOfInt(dr["Asset_Count"])
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
                countReader = DB.ExecuteReader(countSql, BuildPredicateParameters(ctx));
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
        /// Every C_Contract_ID matching the live-with-open-ticket predicate,
        /// most-loaded first, capped at <see cref="MaxZoomIds"/>. Backs
        /// "Open in browser"'s TabWhereClause IN-list - see
        /// <see cref="GetServiceUnderContractContractIds"/>.
        /// </summary>
        /// <param name="ctx">Session context.</param>
        private List<int> GetContractIdsData(Ctx ctx)
        {
            List<int> ids = new List<int>();
            if (ctx == null) { return ids; }

            string sql = @"
                SELECT co.C_Contract_ID AS Contract_Id,
                       " + OpenTicketCountSql() + @" AS Open_Ticket_Count
                  FROM C_Contract co
                 WHERE " + LiveWithTicketWhereSql();

            sql = MRole.GetDefault(ctx).AddAccessSQL(sql, "co", MRole.SQL_FULLYQUALIFIED, MRole.SQL_RO);

            sql += @"
                ORDER BY Open_Ticket_Count DESC, co.EndDate ASC
                OFFSET 0 ROWS FETCH NEXT @MaxIds ROWS ONLY";

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

        private class ServiceUnderContractSummary
        {
            public int OpenTicketTotal { get; set; }
            public int AssetTotal { get; set; }
        }

        private class ServiceUnderContractDrillResult
        {
            public List<ServiceUnderContractDrillRow> Rows { get; set; }
            public int Total { get; set; }
        }

        private class ServiceUnderContractDrillRow
        {
            public int ContractId { get; set; }
            public string DocumentNo { get; set; }
            public string CustomerName { get; set; }
            public int OpenTicketCount { get; set; }
            public int AssetCount { get; set; }
        }
    }
}
