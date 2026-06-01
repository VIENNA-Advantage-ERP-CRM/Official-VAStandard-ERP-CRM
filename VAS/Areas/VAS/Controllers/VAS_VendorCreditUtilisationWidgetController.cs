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
    public class VAS_VendorCreditUtilisationWidgetController : Controller
    {
        string strQuery = "";

        [Authorize]
        public ActionResult Index()
        {
            return View();
        }

        /// <summary>
        /// Returns vendor credit utilisation: open payables vs. credit limit (SO_CreditLimit)
        /// per active vendor, sorted by utilisation percentage descending.
        /// Uses 2 DB round-trips: 1 currency + 1 utilisation aggregation.
        /// MRole applied only to the join-free C_Invoice base query.
        /// C_BPartner drives the outer query; invoice aggregation is a LEFT JOIN subquery.
        /// </summary>
        public JsonResult GetVendorCreditUtilisation()
        {
            string retJSON = "";
            if (Session["ctx"] != null)
            {
                Ctx ctx = Session["ctx"] as Ctx;
                VendorCreditResult result = BuildResult(ctx);
                retJSON = JsonConvert.SerializeObject(result);
            }
            return Json(retJSON, JsonRequestBehavior.AllowGet);
        }

        private VendorCreditResult BuildResult(Ctx ctx)
        {
            var result = new VendorCreditResult
            {
                Vendors = new List<CreditItem>()
            };

            int clientId = ctx.GetAD_Client_ID();
            int orgId    = ctx.GetAD_Org_ID();
            SqlParameter[] schemaParams = { new SqlParameter("@ClientID", clientId) };
            SqlParameter[] dataParams   = { new SqlParameter("@ClientID", clientId), new SqlParameter("@OrgID", orgId) };

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

            DataSet cDs = DB.ExecuteDataset(strQuery, schemaParams, null);
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
            string baseInv = @"SELECT i.C_Invoice_ID, i.C_BPartner_ID, COALESCE(currencyConvert(i.VA009_OpenAmount, i.C_Currency_ID, " + schemaCurrencyId + @", i.DateAcct, i.C_ConversionType_ID, i.AD_Client_ID, i.AD_Org_ID), 0) AS OpenAmt
                  FROM C_Invoice i
                 WHERE i.IsSOTrx = 'N'
                   AND i.IsReturnTrx = 'N'
                   AND i.DocStatus IN ('CO', 'CL')
                   AND i.IsActive = 'Y'
                   AND i.VA009_OpenAmount > 0
                   AND i.AD_Client_ID = @ClientID
                   AND i.AD_Org_ID = @OrgID";
            baseInv = MRole.GetDefault(ctx).AddAccessSQL(baseInv, "i", MRole.SQL_FULLYQUALIFIED, MRole.SQL_RO);

            strQuery = @"SELECT bp.Name AS VendorName,
                       bp.SO_CreditLimit AS CreditLimit,
                       COALESCE(inv_s.CreditUsed, 0) AS CreditUsed
                  FROM C_BPartner bp
                   LEFT OUTER JOIN (SELECT v.C_BPartner_ID, SUM(v.OpenAmt) AS CreditUsed
                                      FROM (" + baseInv + @") v
                                     GROUP BY v.C_BPartner_ID) inv_s ON (inv_s.C_BPartner_ID = bp.C_BPartner_ID)
                 WHERE bp.IsVendor = 'Y'
                   AND bp.IsActive = 'Y'
                   AND COALESCE(inv_s.CreditUsed, 0) > 0
                   AND bp.AD_Client_ID = @ClientID
                 ORDER BY CASE WHEN bp.SO_CreditLimit > 0
                               THEN COALESCE(inv_s.CreditUsed, 0) / bp.SO_CreditLimit
                               ELSE 0 END DESC";

            DataSet dsCredit = DB.ExecuteDataset(strQuery, dataParams, null);
            if (dsCredit != null && dsCredit.Tables.Count > 0)
            {
                int breachCount = 0;
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
                        if (isBreached) { breachCount++; }
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
                result.BreachCount = breachCount;
            }

            return result;
        }

        public class VendorCreditResult
        {
            public string          CurSymbol    { get; set; }
            public int             StdPrecision { get; set; }
            public int             BreachCount  { get; set; }
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
