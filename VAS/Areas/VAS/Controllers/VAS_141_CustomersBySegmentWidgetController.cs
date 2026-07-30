/******************************************************
 * Module Name    : VAS
 * Purpose        : Customers module Customers-by-Segment widget endpoints
 * chronological  : Development
 * Created Date   : 2026-07-23
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
    /// Module Name : VAS_141_CustomersBySegmentWidget
    /// Purpose     : 3x1 distribution widget - active customers grouped by segment,
    ///               where "segment" is an Onfinity target list (C_MasterTargetList,
    ///               joined via the C_TargetList membership table). The header shows
    ///               the distinct segmented and unsegmented customer counts; each row
    ///               is a target list with its distinct customer count and a share bar
    ///               (share of the segmented total). A segment drills to its customers
    ///               (ranked by customer value); "Segment ->" opens the top unsegmented
    ///               customers plus the active target lists for a bulk assignment.
    ///               Customer value = COALESCE(PotentialLifeTimeValue,
    ///               ActualLifeTimeValue, 0) (no native ARR column here). A customer
    ///               may belong to several lists, so per-segment counts can exceed the
    ///               distinct segmented total. MRole (tenant + org + record access) is
    ///               applied to the main physical table of each query/CTE.
    /// Chronological development:
    ///   VAI052      2026-07-23 Created
    /// </summary>
    public class VAS_141_CustomersBySegmentWidgetController : Controller
    {
        private static readonly VLogger Log = VLogger.GetVLogger(typeof(VAS_141_CustomersBySegmentWidgetController).FullName);

        private const int WidgetPageSize = 7;
        private const int MaxListPageSize = 25;
        private const int MaxAssignCustomers = 500;

        /// <summary>Customer value used for ranking/display (no native ARR column).</summary>
        private const string ValueExpr = "COALESCE(bp.PotentialLifeTimeValue, bp.ActualLifeTimeValue, 0)";

        /// <summary>Single-row tenant accounting currency (for value formatting).</summary>
        private const string SchemaCurrencySql = @"
            SELECT cur.StdPrecision AS Std_Precision,
                   cur.ISO_Code AS ISO_Code,
                   CASE WHEN cur.CurSymbol IS NOT NULL THEN cur.CurSymbol ELSE cur.ISO_Code END AS Cur_Symbol
            FROM AD_ClientInfo ci
            INNER JOIN C_AcctSchema cs ON (cs.C_AcctSchema_ID=ci.C_AcctSchema1_ID AND cs.IsActive = 'Y')
            INNER JOIN C_Currency cur ON (cur.C_Currency_ID=cs.C_Currency_ID AND cur.IsActive = 'Y')
            WHERE ci.IsActive = 'Y'
              AND ci.AD_Client_ID = @Client_ID";

        /// <summary>Reads the tenant accounting currency (symbol/iso/precision).</summary>
        private void ReadCurrency(Ctx ctx, out string symbol, out string iso, out int precision)
        {
            symbol = ""; iso = ""; precision = 2;
            IDataReader dr = null;
            try
            {
                dr = DB.ExecuteReader(SchemaCurrencySql, new SqlParameter[] { new SqlParameter("@Client_ID", ctx.GetAD_Client_ID()) });
                if (dr != null && dr.Read())
                {
                    symbol = Util.GetValueOfString(dr["Cur_Symbol"]);
                    iso = Util.GetValueOfString(dr["ISO_Code"]);
                    if (dr["Std_Precision"] != null && dr["Std_Precision"] != DBNull.Value)
                    {
                        precision = Util.GetValueOfInt(dr["Std_Precision"]);
                    }
                }
            }
            finally { CloseReader(dr); }
        }

        /// <summary>
        /// Header totals (distinct segmented / unsegmented / total) plus the per-segment
        /// distribution. Share percentage is computed in C# to avoid dialect rounding.
        /// </summary>
        /// <returns>JSON { total, segmented, unsegmented, segments:[...] } or { error }.</returns>
        [AjaxAuthorizeAttribute]
        [AjaxSessionFilterAttribute]
        public JsonResult GetSegments()
        {
            if (Session["ctx"] == null)
            {
                return Json(new { error = Msg.GetMsg(Env.GetCtx(), "SessionExpired") ?? "Session Expired" }, JsonRequestBehavior.AllowGet);
            }

            Ctx ctx = Session["ctx"] as Ctx;

            try
            {
                // Eligible customers (MRole on bp) - shared by totals and distribution.
                string eligibleSql = @"
                    SELECT bp.C_BPartner_ID AS C_BPartner_ID
                    FROM C_BPartner bp
                    WHERE bp.IsActive = 'Y'
                      AND bp.IsCustomer = 'Y'
                      AND bp.AD_Client_ID = @Client_ID";
                eligibleSql = MRole.GetDefault(ctx).AddAccessSQL(eligibleSql, "bp", MRole.SQL_FULLYQUALIFIED, MRole.SQL_RO);

                // Active memberships restricted to accessible customers + active lists
                // (MRole on tl).
                string membSql = @"
                    SELECT DISTINCT tl.C_BPartner_ID AS C_BPartner_ID,
                                    tl.C_MasterTargetList_ID AS C_MasterTargetList_ID
                    FROM C_TargetList tl
                    INNER JOIN eligible e ON (e.C_BPartner_ID = tl.C_BPartner_ID)
                    INNER JOIN C_MasterTargetList mtl0 ON (mtl0.C_MasterTargetList_ID=tl.C_MasterTargetList_ID AND mtl0.AD_Client_ID=tl.AD_Client_ID AND mtl0.IsActive = 'Y')
                    WHERE tl.IsActive = 'Y'
                      AND tl.AD_Client_ID = @Client_ID";
                membSql = MRole.GetDefault(ctx).AddAccessSQL(membSql, "tl", MRole.SQL_FULLYQUALIFIED, MRole.SQL_RO);

                // Totals: distinct eligible + distinct segmented (single row via scalars).
                string totalsSql = @"
                    WITH eligible AS (
                        " + eligibleSql + @"
                    ),
                    memb AS (
                        " + membSql + @"
                    )
                    SELECT (SELECT COUNT(*) FROM eligible) AS Total_Customers,
                           (SELECT COUNT(DISTINCT m.C_BPartner_ID) FROM memb m) AS Segmented_Customers";

                int total = 0, segmented = 0;
                IDataReader tdr = null;
                try
                {
                    tdr = DB.ExecuteReader(totalsSql, new SqlParameter[] { new SqlParameter("@Client_ID", ctx.GetAD_Client_ID()) });
                    if (tdr != null && tdr.Read())
                    {
                        total = Util.GetValueOfInt(tdr["Total_Customers"]);
                        segmented = Util.GetValueOfInt(tdr["Segmented_Customers"]);
                    }
                }
                finally { CloseReader(tdr); }

                // Distribution: per active list distinct customer count (MRole on mtl).
                string distWhereSql = @"
                    SELECT mtl.C_MasterTargetList_ID AS Segment_Id,
                           mtl.Name AS Segment_Name,
                           COUNT(DISTINCT m.C_BPartner_ID) AS Customer_Count
                    FROM C_MasterTargetList mtl
                    LEFT OUTER JOIN memb m ON (m.C_MasterTargetList_ID = mtl.C_MasterTargetList_ID)
                    WHERE mtl.IsActive = 'Y'
                      AND mtl.AD_Client_ID = @Client_ID";
                distWhereSql = MRole.GetDefault(ctx).AddAccessSQL(distWhereSql, "mtl", MRole.SQL_FULLYQUALIFIED, MRole.SQL_RO);

                string distSql = @"
                    WITH eligible AS (
                        " + eligibleSql + @"
                    ),
                    memb AS (
                        " + membSql + @"
                    )
                    " + distWhereSql + @"
                    GROUP BY mtl.C_MasterTargetList_ID, mtl.Name
                    ORDER BY Customer_Count DESC, mtl.Name ASC";

                List<object> segments = new List<object>();
                IDataReader dr = null;
                try
                {
                    dr = DB.ExecuteReader(distSql, new SqlParameter[] { new SqlParameter("@Client_ID", ctx.GetAD_Client_ID()) });
                    while (dr != null && dr.Read())
                    {
                        int count = Util.GetValueOfInt(dr["Customer_Count"]);
                        int sharePercent = segmented > 0 ? (int)Math.Round(100.0 * count / segmented) : 0;
                        if (sharePercent > 100) { sharePercent = 100; }
                        segments.Add(new
                        {
                            id = Util.GetValueOfInt(dr["Segment_Id"]),
                            name = Util.GetValueOfString(dr["Segment_Name"]),
                            customerCount = count,
                            sharePercent = sharePercent
                        });
                    }
                }
                finally { CloseReader(dr); }

                int unsegmented = total - segmented;
                if (unsegmented < 0) { unsegmented = 0; }

                var result = new
                {
                    total = total,
                    segmented = segmented,
                    unsegmented = unsegmented,
                    segments = segments
                };
                return Json(JsonConvert.SerializeObject(result), JsonRequestBehavior.AllowGet);
            }
            catch (Exception ex)
            {
                Log.Log(Level.SEVERE, "VAS_141_CustomersBySegmentWidget.GetSegments", ex);
                return Json(new { error = ex.Message }, JsonRequestBehavior.AllowGet);
            }
        }

        /// <summary>
        /// Customers in one segment (target list), ranked by customer value. Paged.
        /// </summary>
        /// <param name="C_MasterTargetList_ID">Selected target list.</param>
        /// <param name="offset">Zero-based paging offset.</param>
        /// <param name="limit">Page size, up to 25.</param>
        /// <returns>JSON { items:[...], total, offset, limit, currency_* } or { error }.</returns>
        [AjaxAuthorizeAttribute]
        [AjaxSessionFilterAttribute]
        public JsonResult GetSegmentCustomers(int C_MasterTargetList_ID, int offset = 0, int limit = WidgetPageSize)
        {
            if (Session["ctx"] == null)
            {
                return Json(new { error = Msg.GetMsg(Env.GetCtx(), "SessionExpired") ?? "Session Expired" }, JsonRequestBehavior.AllowGet);
            }

            if (C_MasterTargetList_ID <= 0)
            {
                return Json(new { error = "Invalid segment" }, JsonRequestBehavior.AllowGet);
            }

            Ctx ctx = Session["ctx"] as Ctx;

            if (offset < 0) { offset = 0; }
            if (limit <= 0 || limit > MaxListPageSize) { limit = WidgetPageSize; }

            try
            {
                string symbol, iso; int precision;
                ReadCurrency(ctx, out symbol, out iso, out precision);

                // Customers who are active members of this list (EXISTS avoids
                // duplicate rows on multi-membership). MRole on bp.
                string rowsSql = @"
                    SELECT bp.C_BPartner_ID AS Customer_Id,
                           bp.Value AS Search_Key,
                           bp.Name AS Customer_Name,
                           COALESCE(bp.EMail, N'') AS Email,
                           COALESCE(bp.Phone, N'') AS Phone,
                           COALESCE(owner.Name, N'') AS Owner_Name,
                           " + ValueExpr + @" AS Customer_Value
                    FROM C_BPartner bp
                    LEFT OUTER JOIN AD_User owner ON (owner.AD_User_ID=bp.SalesRep_ID AND owner.AD_Client_ID=bp.AD_Client_ID AND owner.IsActive = 'Y')
                    WHERE bp.IsActive = 'Y'
                      AND bp.IsCustomer = 'Y'
                      AND bp.AD_Client_ID = @Client_ID
                      AND EXISTS (
                          SELECT 1 FROM C_TargetList tl
                          INNER JOIN C_MasterTargetList mtl ON (mtl.C_MasterTargetList_ID=tl.C_MasterTargetList_ID AND mtl.AD_Client_ID=tl.AD_Client_ID AND mtl.IsActive = 'Y')
                          WHERE tl.C_BPartner_ID=bp.C_BPartner_ID
                            AND tl.IsActive = 'Y'
                            AND tl.AD_Client_ID = @Client_ID
                            AND tl.C_MasterTargetList_ID = @Seg_ID
                      )";
                rowsSql = MRole.GetDefault(ctx).AddAccessSQL(rowsSql, "bp", MRole.SQL_FULLYQUALIFIED, MRole.SQL_RO);

                string sql = @"
                    SELECT s.Customer_Id,
                           s.Search_Key,
                           s.Customer_Name,
                           s.Email,
                           s.Phone,
                           s.Owner_Name,
                           s.Customer_Value,
                           COUNT(1) OVER () AS Total_Rows
                    FROM (
                        " + rowsSql + @"
                    ) s
                    ORDER BY s.Customer_Value DESC, s.Customer_Name ASC
                    OFFSET " + offset + @" ROWS FETCH NEXT " + limit + @" ROWS ONLY";

                List<object> items = new List<object>();
                int total = 0;
                IDataReader dr = null;
                try
                {
                    dr = DB.ExecuteReader(sql, new SqlParameter[]
                    {
                        new SqlParameter("@Client_ID", ctx.GetAD_Client_ID()),
                        new SqlParameter("@Seg_ID", C_MasterTargetList_ID)
                    });
                    while (dr != null && dr.Read())
                    {
                        total = Util.GetValueOfInt(dr["Total_Rows"]);
                        items.Add(new
                        {
                            customerId = Util.GetValueOfInt(dr["Customer_Id"]),
                            searchKey = Util.GetValueOfString(dr["Search_Key"]),
                            customerName = Util.GetValueOfString(dr["Customer_Name"]),
                            email = Util.GetValueOfString(dr["Email"]),
                            phone = Util.GetValueOfString(dr["Phone"]),
                            ownerName = Util.GetValueOfString(dr["Owner_Name"]),
                            customerValue = Util.GetValueOfDecimal(dr["Customer_Value"])
                        });
                    }
                }
                finally { CloseReader(dr); }

                var result = new
                {
                    items = items,
                    total = total,
                    offset = offset,
                    limit = limit,
                    currency_symbol = symbol,
                    currency_iso = iso,
                    std_precision = precision
                };
                return Json(JsonConvert.SerializeObject(result), JsonRequestBehavior.AllowGet);
            }
            catch (Exception ex)
            {
                Log.Log(Level.SEVERE, "VAS_141_CustomersBySegmentWidget.GetSegmentCustomers", ex);
                return Json(new { error = ex.Message }, JsonRequestBehavior.AllowGet);
            }
        }

        /// <summary>
        /// The "Segment ->" modal source: a page of top unsegmented customers (no active
        /// membership in any active list) plus the active target lists for the selector.
        /// </summary>
        /// <param name="offset">Zero-based paging offset.</param>
        /// <param name="limit">Page size, up to 25.</param>
        /// <returns>JSON { customers:[...], total, segments:[...], currency_* } or { error }.</returns>
        [AjaxAuthorizeAttribute]
        [AjaxSessionFilterAttribute]
        public JsonResult GetUnsegmented(int offset = 0, int limit = 12)
        {
            if (Session["ctx"] == null)
            {
                return Json(new { error = Msg.GetMsg(Env.GetCtx(), "SessionExpired") ?? "Session Expired" }, JsonRequestBehavior.AllowGet);
            }

            Ctx ctx = Session["ctx"] as Ctx;

            if (offset < 0) { offset = 0; }
            if (limit <= 0 || limit > MaxListPageSize) { limit = 12; }

            try
            {
                string symbol, iso; int precision;
                ReadCurrency(ctx, out symbol, out iso, out precision);

                // Unsegmented eligible customers (MRole on bp).
                string rowsSql = @"
                    SELECT bp.C_BPartner_ID AS Customer_Id,
                           bp.Value AS Search_Key,
                           bp.Name AS Customer_Name,
                           COALESCE(bp.EMail, N'') AS Email,
                           COALESCE(owner.Name, N'') AS Owner_Name,
                           " + ValueExpr + @" AS Customer_Value
                    FROM C_BPartner bp
                    LEFT OUTER JOIN AD_User owner ON (owner.AD_User_ID=bp.SalesRep_ID AND owner.AD_Client_ID=bp.AD_Client_ID AND owner.IsActive = 'Y')
                    WHERE bp.IsActive = 'Y'
                      AND bp.IsCustomer = 'Y'
                      AND bp.AD_Client_ID = @Client_ID
                      AND NOT EXISTS (
                          SELECT 1 FROM C_TargetList tl
                          INNER JOIN C_MasterTargetList mtl ON (mtl.C_MasterTargetList_ID=tl.C_MasterTargetList_ID AND mtl.AD_Client_ID=tl.AD_Client_ID AND mtl.IsActive = 'Y')
                          WHERE tl.C_BPartner_ID=bp.C_BPartner_ID
                            AND tl.IsActive = 'Y'
                            AND tl.AD_Client_ID = @Client_ID
                      )";
                rowsSql = MRole.GetDefault(ctx).AddAccessSQL(rowsSql, "bp", MRole.SQL_FULLYQUALIFIED, MRole.SQL_RO);

                string sql = @"
                    SELECT s.Customer_Id,
                           s.Search_Key,
                           s.Customer_Name,
                           s.Email,
                           s.Owner_Name,
                           s.Customer_Value,
                           COUNT(1) OVER () AS Total_Rows
                    FROM (
                        " + rowsSql + @"
                    ) s
                    ORDER BY s.Customer_Value DESC, s.Customer_Name ASC
                    OFFSET " + offset + @" ROWS FETCH NEXT " + limit + @" ROWS ONLY";

                List<object> customers = new List<object>();
                int total = 0;
                IDataReader dr = null;
                try
                {
                    dr = DB.ExecuteReader(sql, new SqlParameter[] { new SqlParameter("@Client_ID", ctx.GetAD_Client_ID()) });
                    while (dr != null && dr.Read())
                    {
                        total = Util.GetValueOfInt(dr["Total_Rows"]);
                        customers.Add(new
                        {
                            customerId = Util.GetValueOfInt(dr["Customer_Id"]),
                            searchKey = Util.GetValueOfString(dr["Search_Key"]),
                            customerName = Util.GetValueOfString(dr["Customer_Name"]),
                            email = Util.GetValueOfString(dr["Email"]),
                            ownerName = Util.GetValueOfString(dr["Owner_Name"]),
                            customerValue = Util.GetValueOfDecimal(dr["Customer_Value"])
                        });
                    }
                }
                finally { CloseReader(dr); }

                // Active target lists for the assignment selector (MRole on mtl).
                string segSql = @"
                    SELECT mtl.C_MasterTargetList_ID AS Segment_Id,
                           mtl.Name AS Segment_Name
                    FROM C_MasterTargetList mtl
                    WHERE mtl.IsActive = 'Y'
                      AND mtl.AD_Client_ID = @Client_ID";
                segSql = MRole.GetDefault(ctx).AddAccessSQL(segSql, "mtl", MRole.SQL_FULLYQUALIFIED, MRole.SQL_RO);
                segSql += @"
                    ORDER BY mtl.Name ASC";

                List<object> segments = new List<object>();
                IDataReader sdr = null;
                try
                {
                    sdr = DB.ExecuteReader(segSql, new SqlParameter[] { new SqlParameter("@Client_ID", ctx.GetAD_Client_ID()) });
                    while (sdr != null && sdr.Read())
                    {
                        segments.Add(new
                        {
                            id = Util.GetValueOfInt(sdr["Segment_Id"]),
                            name = Util.GetValueOfString(sdr["Segment_Name"])
                        });
                    }
                }
                finally { CloseReader(sdr); }

                var result = new
                {
                    customers = customers,
                    total = total,
                    offset = offset,
                    limit = limit,
                    segments = segments,
                    currency_symbol = symbol,
                    currency_iso = iso,
                    std_precision = precision
                };
                return Json(JsonConvert.SerializeObject(result), JsonRequestBehavior.AllowGet);
            }
            catch (Exception ex)
            {
                Log.Log(Level.SEVERE, "VAS_141_CustomersBySegmentWidget.GetUnsegmented", ex);
                return Json(new { error = ex.Message }, JsonRequestBehavior.AllowGet);
            }
        }

        /// <summary>
        /// Bulk-assigns the selected customers to a target list. Idempotent: only
        /// eligible, accessible customers without an existing active membership in that
        /// list are inserted (via the C_TargetList model, one framework-generated id
        /// each). Tenant/role access is re-applied server-side; the client-supplied ids
        /// are never trusted.
        /// </summary>
        /// <param name="C_MasterTargetList_ID">Target list to assign into.</param>
        /// <param name="customerIds">Comma-separated C_BPartner_ID values.</param>
        /// <returns>JSON { requested, inserted, alreadyAssigned } or { error }.</returns>
        [AjaxAuthorizeAttribute]
        [AjaxSessionFilterAttribute]
        [ValidateInput(false)]
        public JsonResult AssignSegment(int C_MasterTargetList_ID, string customerIds)
        {
            if (Session["ctx"] == null)
            {
                return Json(new { error = Msg.GetMsg(Env.GetCtx(), "SessionExpired") ?? "Session Expired" }, JsonRequestBehavior.AllowGet);
            }

            if (C_MasterTargetList_ID <= 0)
            {
                return Json(new { error = "Invalid segment" }, JsonRequestBehavior.AllowGet);
            }

            Ctx ctx = Session["ctx"] as Ctx;

            // Parse + de-duplicate the requested customer ids (validated ints only).
            List<int> requested = new List<int>();
            if (!string.IsNullOrEmpty(customerIds))
            {
                string[] parts = customerIds.Split(',');
                foreach (string part in parts)
                {
                    int id = Util.GetValueOfInt(part.Trim());
                    if (id > 0 && !requested.Contains(id)) { requested.Add(id); }
                    if (requested.Count >= MaxAssignCustomers) { break; }
                }
            }

            if (requested.Count == 0)
            {
                return Json(new { error = Msg.GetMsg(ctx, "VAS_141_NoCustomers") ?? "Select at least one customer." }, JsonRequestBehavior.AllowGet);
            }

            try
            {
                // Verify the target list is active and belongs to the tenant (MRole).
                string listSql = @"
                    SELECT COUNT(1) AS Cnt
                    FROM C_MasterTargetList mtl
                    WHERE mtl.IsActive = 'Y'
                      AND mtl.AD_Client_ID = @Client_ID
                      AND mtl.C_MasterTargetList_ID = @Seg_ID";
                listSql = MRole.GetDefault(ctx).AddAccessSQL(listSql, "mtl", MRole.SQL_FULLYQUALIFIED, MRole.SQL_RO);
                int listOk = Util.GetValueOfInt(DB.ExecuteScalar(listSql, new SqlParameter[]
                {
                    new SqlParameter("@Client_ID", ctx.GetAD_Client_ID()),
                    new SqlParameter("@Seg_ID", C_MasterTargetList_ID)
                }, null));
                if (listOk <= 0)
                {
                    return Json(new { error = Msg.GetMsg(ctx, "VAS_141_InvalidSegment") ?? "That target list is not available." }, JsonRequestBehavior.AllowGet);
                }

                // Re-resolve the eligible, accessible customers that are NOT already
                // active members of this list. Ids are validated ints (safe to inline).
                string idList = string.Join(",", requested);
                string eligibleSql = @"
                    SELECT bp.C_BPartner_ID AS C_BPartner_ID,
                           bp.AD_Org_ID AS AD_Org_ID
                    FROM C_BPartner bp
                    WHERE bp.IsActive = 'Y'
                      AND bp.IsCustomer = 'Y'
                      AND bp.AD_Client_ID = @Client_ID
                      AND bp.C_BPartner_ID IN (" + idList + @")
                      AND NOT EXISTS (
                          SELECT 1 FROM C_TargetList tl
                          WHERE tl.C_BPartner_ID=bp.C_BPartner_ID
                            AND tl.C_MasterTargetList_ID = @Seg_ID
                            AND tl.IsActive = 'Y'
                            AND tl.AD_Client_ID = @Client_ID
                      )";
                eligibleSql = MRole.GetDefault(ctx).AddAccessSQL(eligibleSql, "bp", MRole.SQL_FULLYQUALIFIED, MRole.SQL_RO);

                List<int[]> toInsert = new List<int[]>();  // { bpId, orgId }
                IDataReader dr = null;
                try
                {
                    dr = DB.ExecuteReader(eligibleSql, new SqlParameter[]
                    {
                        new SqlParameter("@Client_ID", ctx.GetAD_Client_ID()),
                        new SqlParameter("@Seg_ID", C_MasterTargetList_ID)
                    });
                    while (dr != null && dr.Read())
                    {
                        toInsert.Add(new int[] { Util.GetValueOfInt(dr["C_BPartner_ID"]), Util.GetValueOfInt(dr["AD_Org_ID"]) });
                    }
                }
                finally { CloseReader(dr); }

                int inserted = 0;
                foreach (int[] row in toInsert)
                {
                    X_C_TargetList membership = new X_C_TargetList(ctx, 0, null);
                    membership.SetAD_Org_ID(row[1]);
                    membership.SetC_BPartner_ID(row[0]);
                    membership.SetC_MasterTargetList_ID(C_MasterTargetList_ID);
                    membership.SetIsActive(true);
                    if (membership.Save()) { inserted++; }
                }

                int alreadyAssigned = requested.Count - toInsert.Count;
                if (alreadyAssigned < 0) { alreadyAssigned = 0; }

                var result = new
                {
                    requested = requested.Count,
                    inserted = inserted,
                    alreadyAssigned = alreadyAssigned
                };
                return Json(JsonConvert.SerializeObject(result), JsonRequestBehavior.AllowGet);
            }
            catch (Exception ex)
            {
                Log.Log(Level.SEVERE, "VAS_141_CustomersBySegmentWidget.AssignSegment", ex);
                return Json(new { error = ex.Message }, JsonRequestBehavior.AllowGet);
            }
        }

        private void CloseReader(IDataReader reader)
        {
            if (reader == null) { return; }
            reader.Close();
            reader.Dispose();
        }
    }
}
