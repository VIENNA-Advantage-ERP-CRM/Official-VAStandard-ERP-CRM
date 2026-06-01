/************************************************************
 * Module Name    : VAS
 * Purpose        : Top Five Vendors by Spend Widget
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
    public class VAS_TopFiveVendorsWidgetController : Controller
    {
        string strQuery = "";

        [Authorize]
        public ActionResult Index()
        {
            return View();
        }

        /// <summary>
        /// Returns top 5 vendors by purchase spend for the current Indian fiscal year (April–March),
        /// with prior-year amounts for YoY comparison.
        /// Uses 2 DB round-trips: 1 currency + 1 vendor aggregation.
        /// MRole applied only to the join-free C_Invoice base query.
        /// C_BPartner and C_BP_Group joins are in the outer query outside MRole scope.
        /// </summary>
        public JsonResult GetTopFiveVendors()
        {
            string retJSON = "";
            if (Session["ctx"] != null)
            {
                Ctx ctx = Session["ctx"] as Ctx;
                TopFiveVendorsResult result = BuildResult(ctx);
                retJSON = JsonConvert.SerializeObject(result);
            }
            return Json(retJSON, JsonRequestBehavior.AllowGet);
        }

        private TopFiveVendorsResult BuildResult(Ctx ctx)
        {
            var result = new TopFiveVendorsResult
            {
                Vendors = new List<VendorItem>()
            };

            int clientId = ctx.GetAD_Client_ID();
            int orgId    = ctx.GetAD_Org_ID();
            SqlParameter[] schemaParams = { new SqlParameter("@ClientID", clientId) };
            SqlParameter[] dataParams   = { new SqlParameter("@ClientID", clientId), new SqlParameter("@OrgID", orgId) };
            DateTime now = DateTime.Now;

            // Indian fiscal year: April 1 – March 31
            int fyStartYear = now.Month >= 4 ? now.Year : now.Year - 1;
            int fyStart     = (fyStartYear       * 12 + 4) * 31 + 1;
            int fyEnd       = ((fyStartYear + 1)  * 12 + 3) * 31 + 31;
            int prevFyStart = ((fyStartYear - 1)  * 12 + 4) * 31 + 1;
            int prevFyEnd   = (fyStartYear         * 12 + 3) * 31 + 31;
            result.FyLabel  = "FY " + fyStartYear + "–" + (fyStartYear + 1).ToString().Substring(2);

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

            // Round-trip 2 — vendor spend aggregation for current FY and prior FY.
            // MRole on join-free C_Invoice base; C_BPartner + C_BP_Group joins in outer query.
            string baseVendor = @"SELECT i.C_Invoice_ID, COALESCE(currencyConvert(i.GrandTotal, i.C_Currency_ID, " + schemaCurrencyId + @", i.DateAcct, i.C_ConversionType_ID, i.AD_Client_ID, i.AD_Org_ID), 0) AS GrandTotal, i.C_BPartner_ID, i.DateInvoiced
                  FROM C_Invoice i
                 WHERE i.IsSOTrx = 'N'
                   AND i.DocStatus IN ('CO', 'CL')
                   AND i.IsActive = 'Y'
                   AND i.AD_Client_ID = @ClientID
                   AND i.AD_Org_ID = @OrgID";
            baseVendor = MRole.GetDefault(ctx).AddAccessSQL(baseVendor, "i", MRole.SQL_FULLYQUALIFIED, MRole.SQL_RO);

            strQuery = @"SELECT bp.Name AS VendorName,
                       bpg.Name AS Category,
                       SUM(CASE WHEN ((EXTRACT(YEAR FROM v.DateInvoiced) * 12 + EXTRACT(MONTH FROM v.DateInvoiced)) * 31 + EXTRACT(DAY FROM v.DateInvoiced)) >= " + fyStart + @"
                                 AND ((EXTRACT(YEAR FROM v.DateInvoiced) * 12 + EXTRACT(MONTH FROM v.DateInvoiced)) * 31 + EXTRACT(DAY FROM v.DateInvoiced)) <= " + fyEnd + @"
                                THEN v.GrandTotal ELSE 0 END) AS CurrAmt,
                       SUM(CASE WHEN ((EXTRACT(YEAR FROM v.DateInvoiced) * 12 + EXTRACT(MONTH FROM v.DateInvoiced)) * 31 + EXTRACT(DAY FROM v.DateInvoiced)) >= " + prevFyStart + @"
                                 AND ((EXTRACT(YEAR FROM v.DateInvoiced) * 12 + EXTRACT(MONTH FROM v.DateInvoiced)) * 31 + EXTRACT(DAY FROM v.DateInvoiced)) <= " + prevFyEnd + @"
                                THEN v.GrandTotal ELSE 0 END) AS PrevAmt
                  FROM (" + baseVendor + @") v
                 INNER JOIN C_BPartner bp ON (v.C_BPartner_ID = bp.C_BPartner_ID)
                  LEFT OUTER JOIN C_BP_Group bpg ON (bp.C_BP_Group_ID = bpg.C_BP_Group_ID AND bpg.IsActive = 'Y')
                 WHERE ((EXTRACT(YEAR FROM v.DateInvoiced) * 12 + EXTRACT(MONTH FROM v.DateInvoiced)) * 31 + EXTRACT(DAY FROM v.DateInvoiced)) >= " + prevFyStart + @"
                   AND ((EXTRACT(YEAR FROM v.DateInvoiced) * 12 + EXTRACT(MONTH FROM v.DateInvoiced)) * 31 + EXTRACT(DAY FROM v.DateInvoiced)) <= " + fyEnd + @"
                   AND bp.IsActive = 'Y'
                 GROUP BY bp.Name, bpg.Name
                 ORDER BY CurrAmt DESC";

            DataSet dsVendors = DB.ExecuteDataset(strQuery, dataParams, null);
            if (dsVendors != null && dsVendors.Tables.Count > 0)
            {
                int rank = 1;
                foreach (DataRow row in dsVendors.Tables[0].Rows)
                {
                    if (rank > 5) { break; }
                    string  vendorName = Util.GetValueOfString(row["VendorName"]);
                    string  category   = Util.GetValueOfString(row["Category"]);
                    decimal currAmt    = Util.GetValueOfDecimal(row["CurrAmt"]);
                    decimal prevAmt    = Util.GetValueOfDecimal(row["PrevAmt"]);
                    decimal yoyPct     = prevAmt != 0
                        ? Math.Round((currAmt - prevAmt) / prevAmt * 100, 1)
                        : 0;
                    result.Vendors.Add(new VendorItem
                    {
                        Rank     = rank,
                        Name     = vendorName,
                        Category = string.IsNullOrEmpty(category) ? "—" : category,
                        Initials = GetInitials(vendorName),
                        CurrAmt  = currAmt,
                        PrevAmt  = prevAmt,
                        YoyPct   = yoyPct
                    });
                    rank++;
                }
            }

            return result;
        }

        private static string GetInitials(string name)
        {
            if (string.IsNullOrWhiteSpace(name)) { return "?"; }
            var parts = name.Trim().Split(new char[] { ' ', '-', '.' }, StringSplitOptions.RemoveEmptyEntries);
            if (parts.Length >= 2)
            {
                return (parts[0][0].ToString() + parts[1][0].ToString()).ToUpper();
            }
            return name.Substring(0, Math.Min(2, name.Length)).ToUpper();
        }

        private static string FormatAmount(decimal amount, string sym, int precision)
        {
            if (amount >= 10000000m) { return sym + Math.Round(amount / 10000000m, 1) + "Cr"; }
            if (amount >= 100000m)   { return sym + Math.Round(amount / 100000m,   1) + "L";  }
            if (amount >= 1000m)     { return sym + Math.Round(amount / 1000m,      1) + "K";  }
            return sym + Math.Round(amount, precision);
        }

        public class TopFiveVendorsResult
        {
            public string           CurSymbol    { get; set; }
            public int              StdPrecision { get; set; }
            public string           FyLabel      { get; set; }
            public List<VendorItem> Vendors      { get; set; }
        }

        public class VendorItem
        {
            public int     Rank     { get; set; }
            public string  Name     { get; set; }
            public string  Category { get; set; }
            public string  Initials { get; set; }
            public decimal CurrAmt  { get; set; }
            public decimal PrevAmt  { get; set; }
            public decimal YoyPct   { get; set; }
        }
    }
}
