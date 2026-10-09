/************************************************************
 * Module Name    : VAS
 * Purpose        : Vendor Credit Utilisation Widget
 * Created Date   : 14 May 2026
 * Created by     : Humam Yousif
 ***********************************************************/
using CoreLibrary.DataBase;
using Newtonsoft.Json;
using System;
using System.Collections.Generic;
using System.Data;
using System.Data.SqlClient;
using System.Web.Mvc;
using VAdvantage.Model;
using VAdvantage.Utility;

namespace VAS.Areas.VAS.Controllers
{
    public class VAS_026_VendorCreditUtilisationWidgetController : Controller
    {
        string strQuery = "";

        // Server-side paging bounds (mirrors VAS_020). The client sends an ADAPTIVE
        // pageSize measured from the card height; clamp it to a sane range here.
        const int MinPageSize = 1;
        const int MaxPageSize = 50;
        const int DefPageSize = 8;   // initial guess before the client measures capacity

        [Authorize]
        public ActionResult Index()
        {
            return View();
        }

        /// <summary>
        /// Returns one server-side page of vendor credit utilisation: open payables vs.
        /// credit limit (SO_CreditLimit) per active vendor, sorted by utilisation
        /// percentage descending. Paging mirrors VAS_020: a COUNT(1) over the same set
        /// gives the total (and breach count) for the footer pager, the requested page is
        /// clamped to that total, then one page is fetched (PostgreSQL LIMIT/OFFSET,
        /// Oracle ROWNUM). BreachCount is the total across all vendors (not just the page).
        /// Uses 3 DB round-trips: currency + count + one page.
        /// MRole applied only to the join-free C_Invoice base query.
        /// C_BPartner drives the outer query; invoice aggregation is a LEFT JOIN subquery.
        /// </summary>
        public JsonResult GetVendorCreditUtilisation(int pageNo = 1, int pageSize = DefPageSize)
        {
            string retJSON = "";
            if (Session["ctx"] != null)
            {
                Ctx ctx = Session["ctx"] as Ctx;
                VendorCreditResult result = BuildResult(ctx, pageNo, pageSize);
                retJSON = JsonConvert.SerializeObject(result);
            }
            return Json(retJSON, JsonRequestBehavior.AllowGet);
        }

        private VendorCreditResult BuildResult(Ctx ctx, int pageNo, int pageSize)
        {
            if (pageSize < MinPageSize) { pageSize = MinPageSize; }
            if (pageSize > MaxPageSize) { pageSize = MaxPageSize; }
            if (pageNo < 1) { pageNo = 1; }

            var result = new VendorCreditResult
            {
                PageNo   = 1,
                PageSize = pageSize,
                Vendors  = new List<CreditItem>()
            };

            int clientId = ctx.GetAD_Client_ID();
            SqlParameter[] dataParams   = { new SqlParameter("@ClientID", clientId) };

            // Round-trip 1 — functional currency from accounting schema
            int schemaCurrencyId = 0;
            strQuery = @"SELECT cs.C_Currency_ID, c.CurSymbol, c.StdPrecision
                    FROM C_AcctSchema cs
                    INNER JOIN AD_ClientInfo ci ON (ci.C_AcctSchema1_ID = cs.C_AcctSchema_ID)
                    INNER JOIN C_Currency c ON (cs.C_Currency_ID = c.C_Currency_ID)
                   WHERE ci.AD_Client_ID = @ClientID
                     AND ci.IsActive = 'Y'
                     AND cs.IsActive = 'Y'
                     AND c.IsActive = 'Y'";
            strQuery = MRole.GetDefault(ctx).AddAccessSQL(strQuery, "cs", MRole.SQL_FULLYQUALIFIED, MRole.SQL_RO);

            DataSet cDs = DB.ExecuteDataset(strQuery, dataParams, null);
            if (cDs != null && cDs.Tables.Count > 0 && cDs.Tables[0].Rows.Count > 0)
            {
                schemaCurrencyId    = Util.GetValueOfInt(cDs.Tables[0].Rows[0]["C_Currency_ID"]);
                result.CurSymbol    = Util.GetValueOfString(cDs.Tables[0].Rows[0]["CurSymbol"]);
                result.StdPrecision = Util.GetValueOfInt(cDs.Tables[0].Rows[0]["StdPrecision"]);
            }

            if (schemaCurrencyId == 0) { return result; }

            // Round-trip 2 — credit utilisation per vendor.
            // MRole applied to join-free C_Invoice base.
            // C_BPartner (with SO_CreditLimit > 0) drives the outer SELECT.
            // Open invoice totals are aggregated in a LEFT JOIN subquery.
            string baseInv = @"SELECT i.C_Invoice_ID, i.C_BPartner_ID, i.C_Currency_ID, i.DateAcct, i.C_ConversionType_ID, i.AD_Client_ID, i.AD_Org_ID, i.IsReturnTrx
                  FROM C_Invoice i
                 WHERE i.IsSOTrx = 'N'
                   AND i.IsExpenseInvoice = 'N'
                   AND i.DocStatus IN ('CO', 'CL')
                   AND i.IsActive = 'Y'
                   AND i.AD_Client_ID = @ClientID";
            baseInv = MRole.GetDefault(ctx).AddAccessSQL(baseInv, "i", MRole.SQL_FULLYQUALIFIED, MRole.SQL_RO);

            // Core set (no ORDER BY / paging): one row per vendor with open payables.
            // UtilRatio is the sort key; BPId is the stable tiebreaker.
            string coreSql = @"SELECT bp.Name AS VendorName,
                       bp.SO_CreditLimit AS CreditLimit,
                       COALESCE(inv_s.CreditUsed, 0) AS CreditUsed,
                       bp.C_BPartner_ID AS BPId,
                       CASE WHEN bp.SO_CreditLimit > 0
                            THEN COALESCE(inv_s.CreditUsed, 0) / bp.SO_CreditLimit
                            ELSE 0 END AS UtilRatio
                  FROM C_BPartner bp
                   LEFT OUTER JOIN (SELECT v.C_BPartner_ID,
                                           SUM(CASE WHEN v.IsReturnTrx = 'N'
                                                    THEN COALESCE(currencyConvert(ips.DueAmt, v.C_Currency_ID, " + schemaCurrencyId + @", v.DateAcct, v.C_ConversionType_ID, v.AD_Client_ID, v.AD_Org_ID), 0)
                                                    ELSE -COALESCE(currencyConvert(ips.DueAmt, v.C_Currency_ID, " + schemaCurrencyId + @", v.DateAcct, v.C_ConversionType_ID, v.AD_Client_ID, v.AD_Org_ID), 0)
                                                    END) AS CreditUsed
                                      FROM (" + baseInv + @") v
                                     INNER JOIN C_InvoicePaySchedule ips ON (ips.C_Invoice_ID = v.C_Invoice_ID)
                                     WHERE ips.IsActive = 'Y'
                                       AND ips.VA009_IsPaid = 'N'
                                       AND ips.DueAmt > 0
                                     GROUP BY v.C_BPartner_ID) inv_s ON (inv_s.C_BPartner_ID = bp.C_BPartner_ID)
                 WHERE bp.IsVendor = 'Y'
                   AND bp.IsActive = 'Y'
                   AND COALESCE(inv_s.CreditUsed, 0) > 0
                   AND bp.AD_Client_ID = @ClientID";

            // Total row count (for the pager) + total breach count (for the header badge),
            // both over the whole set so they stay correct on every page.
            int totalCount  = 0;
            int breachCount = 0;
            strQuery = @"SELECT COUNT(1) AS Cnt,
                       SUM(CASE WHEN dc.CreditLimit > 0 AND dc.CreditUsed > dc.CreditLimit
                                THEN 1 ELSE 0 END) AS BreachCnt
                  FROM (" + coreSql + @") dc";
            DataSet cntDs = DB.ExecuteDataset(strQuery, dataParams, null);
            if (cntDs != null && cntDs.Tables.Count > 0 && cntDs.Tables[0].Rows.Count > 0)
            {
                totalCount  = Util.GetValueOfInt(cntDs.Tables[0].Rows[0]["Cnt"]);
                breachCount = Util.GetValueOfInt(cntDs.Tables[0].Rows[0]["BreachCnt"]);
            }
            result.TotalCount  = totalCount;
            result.BreachCount = breachCount;
            result.TotalPages  = (totalCount + pageSize - 1) / pageSize;

            // Clamp the requested page to the count BEFORE building the offset — otherwise a
            // page beyond the end (e.g. after a zoom-out grows the page size and shrinks the
            // page count) would query past the data and return an empty page.
            if (result.TotalPages > 0 && pageNo > result.TotalPages) { pageNo = result.TotalPages; }
            if (pageNo < 1) { pageNo = 1; }
            result.PageNo = pageNo;

            if (totalCount == 0) { return result; }

            int offset = (pageNo - 1) * pageSize;
            string ordered = coreSql + " ORDER BY UtilRatio DESC, BPId ASC";
            if (DB.IsPostgreSQL())
            {
                strQuery = ordered + " LIMIT " + pageSize + " OFFSET " + offset;
            }
            else
            {
                strQuery = "SELECT * FROM (SELECT t.*, ROWNUM rn FROM (" + ordered + ") t WHERE ROWNUM <= " + (offset + pageSize) + ") WHERE rn > " + offset;
            }

            DataSet dsCredit = DB.ExecuteDataset(strQuery, dataParams, null);
            if (dsCredit != null && dsCredit.Tables.Count > 0)
            {
                foreach (DataRow row in dsCredit.Tables[0].Rows)
                {
                    decimal creditLimit = Util.GetValueOfDecimal(row["CreditLimit"]);
                    decimal creditUsed  = Util.GetValueOfDecimal(row["CreditUsed"]);
                    decimal utilPct;
                    bool    isBreached;
                    if (creditLimit > 0)
                    {
                        utilPct    = Math.Round(creditUsed / creditLimit * 100, 0);
                        isBreached = creditUsed > creditLimit;
                    }
                    else
                    {
                        utilPct    = 0;
                        isBreached = false;
                    }
                    result.Vendors.Add(new CreditItem
                    {
                        VendorName  = Util.GetValueOfString(row["VendorName"]),
                        CreditLimit = creditLimit,
                        CreditUsed  = creditUsed,
                        UtilPct     = utilPct,
                        IsBreached  = isBreached
                    });
                }
            }

            return result;
        }

        public class VendorCreditResult
        {
            public string          CurSymbol    { get; set; }
            public int             StdPrecision { get; set; }
            public int             BreachCount  { get; set; }
            public int             TotalCount   { get; set; }
            public int             TotalPages   { get; set; }
            public int             PageNo       { get; set; }
            public int             PageSize     { get; set; }
            public List<CreditItem> Vendors     { get; set; }
        }

        public class CreditItem
        {
            public string  VendorName  { get; set; }
            public decimal CreditLimit { get; set; }
            public decimal CreditUsed  { get; set; }
            public decimal UtilPct     { get; set; }
            public bool    IsBreached  { get; set; }
        }
    }
}
