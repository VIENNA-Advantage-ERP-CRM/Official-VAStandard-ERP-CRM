/************************************************************
 * Module Name    : VAS
 * Purpose        : Pending Invoices Widget
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
    public class VAS_PendingInvoicesWidgetController : Controller
    {
        string strQuery = "";

        [Authorize]
        public ActionResult Index()
        {
            return View();
        }

        /// <summary>
        /// Returns pending invoice KPIs (Needs Attention, GRN Mismatch, PO Not Raised,
        /// Ready to Pay) and the upcoming payment due list for the next 14 days.
        /// Uses 6 DB round-trips: 1 currency + 4 KPI categories + 1 due-soon list.
        /// MRole applied only to the join-free C_Invoice base query in each round-trip.
        /// </summary>
        public JsonResult GetPendingInvoices()
        {
            string retJSON = "";
            if (Session["ctx"] != null)
            {
                Ctx ctx = Session["ctx"] as Ctx;
                PendingInvoicesResult result = BuildResult(ctx);
                retJSON = JsonConvert.SerializeObject(result);
            }
            return Json(retJSON, JsonRequestBehavior.AllowGet);
        }

        private PendingInvoicesResult BuildResult(Ctx ctx)
        {
            var result = new PendingInvoicesResult
            {
                DueItems = new List<DueItem>()
            };

            int clientId = ctx.GetAD_Client_ID();
            int orgId    = ctx.GetAD_Org_ID();
            SqlParameter[] schemaParams = { new SqlParameter("@ClientID", clientId) };
            SqlParameter[] dataParams   = { new SqlParameter("@ClientID", clientId), new SqlParameter("@OrgID", orgId) };
            DateTime now = DateTime.Now;
            int todayInt   = (now.Year * 12 + now.Month) * 31 + now.Day;
            int plus14Int  = (now.AddDays(14).Year * 12 + now.AddDays(14).Month) * 31 + now.AddDays(14).Day;

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

            string sym = result.CurSymbol;

            // Round-trip 2 — Needs Attention: invoices not yet completed/closed (DR, IP, WC, NA).
            // MRole on join-free C_Invoice base.
            string baseAA = @"SELECT i.C_Invoice_ID, COALESCE(currencyConvert(i.GrandTotal, i.C_Currency_ID, " + schemaCurrencyId + @", i.DateAcct, i.C_ConversionType_ID, i.AD_Client_ID, i.AD_Org_ID), 0) AS GrandTotal
                  FROM C_Invoice i
                 WHERE i.IsSOTrx = 'N'
                   AND i.IsReturnTrx = 'N'
                   AND i.DocStatus IN ('DR', 'IP', 'WC', 'NA')
                   AND i.IsActive = 'Y'
                   AND i.AD_Client_ID = @ClientID
                   AND i.AD_Org_ID = @OrgID";
            baseAA = MRole.GetDefault(ctx).AddAccessSQL(baseAA, "i", MRole.SQL_FULLYQUALIFIED, MRole.SQL_RO);

            strQuery = @"SELECT COUNT(1) AS InvCount, SUM(aa.GrandTotal) AS TotalAmt
                  FROM (" + baseAA + @") aa";

            DataSet dsAA = DB.ExecuteDataset(strQuery, dataParams, null);
            if (dsAA != null && dsAA.Tables.Count > 0 && dsAA.Tables[0].Rows.Count > 0)
            {
                result.AwaitingApprovalCount = Util.GetValueOfInt(dsAA.Tables[0].Rows[0]["InvCount"]);
                result.AwaitingApprovalAmt   = Util.GetValueOfDecimal(dsAA.Tables[0].Rows[0]["TotalAmt"]);
            }

            // Round-trip 3 — GRN Mismatch: completed invoice lines with no matching M_MatchInv.
            // MRole on join-free C_Invoice base; C_InvoiceLine + NOT EXISTS in outer query.
            string baseGRN = @"SELECT i.C_Invoice_ID, i.C_Currency_ID, i.DateAcct, i.C_ConversionType_ID, i.AD_Client_ID, i.AD_Org_ID
                  FROM C_Invoice i
                 WHERE i.IsSOTrx = 'N'
                   AND i.IsReturnTrx = 'N'
                   AND i.DocStatus IN ('CO', 'CL')
                   AND i.IsActive = 'Y'
                   AND i.AD_Client_ID = @ClientID
                   AND i.AD_Org_ID = @OrgID";
            baseGRN = MRole.GetDefault(ctx).AddAccessSQL(baseGRN, "i", MRole.SQL_FULLYQUALIFIED, MRole.SQL_RO);

            strQuery = @"SELECT COUNT(DISTINCT g.C_Invoice_ID) AS InvCount, SUM(COALESCE(currencyConvert(il.LineNetAmt, g.C_Currency_ID, " + schemaCurrencyId + @", g.DateAcct, g.C_ConversionType_ID, g.AD_Client_ID, g.AD_Org_ID), 0)) AS TotalAmt
                  FROM (" + baseGRN + @") g
                 INNER JOIN C_InvoiceLine il ON (il.C_Invoice_ID = g.C_Invoice_ID)
                 WHERE il.IsActive = 'Y'
                   AND il.M_Product_ID IS NOT NULL
                   AND NOT EXISTS (SELECT 1 FROM M_MatchInv mi WHERE mi.C_InvoiceLine_ID = il.C_InvoiceLine_ID AND mi.IsActive = 'Y')";

            DataSet dsGRN = DB.ExecuteDataset(strQuery, dataParams, null);
            if (dsGRN != null && dsGRN.Tables.Count > 0 && dsGRN.Tables[0].Rows.Count > 0)
            {
                result.GrnMismatchCount = Util.GetValueOfInt(dsGRN.Tables[0].Rows[0]["InvCount"]);
                result.GrnMismatchAmt   = Util.GetValueOfDecimal(dsGRN.Tables[0].Rows[0]["TotalAmt"]);
            }

            // Round-trip 4 — PO Not Raised: invoices with no linked Purchase Order (C_Order_ID IS NULL).
            // C_Order_ID filter is on the base table so MRole can still be applied cleanly.
            string basePNR = @"SELECT i.C_Invoice_ID, COALESCE(currencyConvert(i.GrandTotal, i.C_Currency_ID, " + schemaCurrencyId + @", i.DateAcct, i.C_ConversionType_ID, i.AD_Client_ID, i.AD_Org_ID), 0) AS GrandTotal
                  FROM C_Invoice i
                 WHERE i.IsSOTrx = 'N'
                   AND i.IsReturnTrx = 'N'
                   AND i.DocStatus IN ('CO', 'CL', 'IP')
                   AND i.IsActive = 'Y'
                   AND i.C_Order_ID IS NULL
                   AND i.AD_Client_ID = @ClientID
                   AND i.AD_Org_ID = @OrgID";
            basePNR = MRole.GetDefault(ctx).AddAccessSQL(basePNR, "i", MRole.SQL_FULLYQUALIFIED, MRole.SQL_RO);

            strQuery = @"SELECT COUNT(1) AS InvCount, SUM(pnr.GrandTotal) AS TotalAmt
                  FROM (" + basePNR + @") pnr";

            DataSet dsPNR = DB.ExecuteDataset(strQuery, dataParams, null);
            if (dsPNR != null && dsPNR.Tables.Count > 0 && dsPNR.Tables[0].Rows.Count > 0)
            {
                result.PoNotRaisedCount = Util.GetValueOfInt(dsPNR.Tables[0].Rows[0]["InvCount"]);
                result.PoNotRaisedAmt   = Util.GetValueOfDecimal(dsPNR.Tables[0].Rows[0]["TotalAmt"]);
            }

            // Round-trip 5 — Ready to Pay: completed invoices with outstanding open amount.
            string baseRTP = @"SELECT i.C_Invoice_ID, COALESCE(currencyConvert(i.VA009_OpenAmount, i.C_Currency_ID, " + schemaCurrencyId + @", i.DateAcct, i.C_ConversionType_ID, i.AD_Client_ID, i.AD_Org_ID), 0) AS OpenAmt
                  FROM C_Invoice i
                 WHERE i.IsSOTrx = 'N'
                   AND i.IsReturnTrx = 'N'
                   AND i.DocStatus IN ('CO', 'CL')
                   AND i.IsActive = 'Y'
                   AND i.VA009_OpenAmount > 0
                   AND i.AD_Client_ID = @ClientID
                   AND i.AD_Org_ID = @OrgID";
            baseRTP = MRole.GetDefault(ctx).AddAccessSQL(baseRTP, "i", MRole.SQL_FULLYQUALIFIED, MRole.SQL_RO);

            strQuery = @"SELECT COUNT(1) AS InvCount, SUM(rtp.OpenAmt) AS TotalAmt
                  FROM (" + baseRTP + @") rtp";

            DataSet dsRTP = DB.ExecuteDataset(strQuery, dataParams, null);
            if (dsRTP != null && dsRTP.Tables.Count > 0 && dsRTP.Tables[0].Rows.Count > 0)
            {
                result.ReadyToPayCount = Util.GetValueOfInt(dsRTP.Tables[0].Rows[0]["InvCount"]);
                result.ReadyToPayAmt   = Util.GetValueOfDecimal(dsRTP.Tables[0].Rows[0]["TotalAmt"]);
            }

            result.TotalPending = result.AwaitingApprovalCount + result.GrnMismatchCount
                                + result.PoNotRaisedCount      + result.ReadyToPayCount;

            // Round-trip 6 — Upcoming due payments (next 14 days), sorted by DueDate ascending.
            // MRole on join-free C_Invoice base; C_BPartner join and date filter in outer query.
            string baseDue = @"SELECT i.C_Invoice_ID, i.C_BPartner_ID, COALESCE(currencyConvert(i.VA009_OpenAmount, i.C_Currency_ID, " + schemaCurrencyId + @", i.DateAcct, i.C_ConversionType_ID, i.AD_Client_ID, i.AD_Org_ID), 0) AS OpenAmt, i.DueDate
                  FROM C_Invoice i
                 WHERE i.IsSOTrx = 'N'
                   AND i.IsReturnTrx = 'N'
                   AND i.DocStatus IN ('CO', 'CL')
                   AND i.IsActive = 'Y'
                   AND i.VA009_OpenAmount > 0
                   AND i.DueDate IS NOT NULL
                   AND i.AD_Client_ID = @ClientID
                   AND i.AD_Org_ID = @OrgID";
            baseDue = MRole.GetDefault(ctx).AddAccessSQL(baseDue, "i", MRole.SQL_FULLYQUALIFIED, MRole.SQL_RO);

            strQuery = @"SELECT bp.Name AS VendorName, d.OpenAmt, d.DueDate
                  FROM (" + baseDue + @") d
                 INNER JOIN C_BPartner bp ON (d.C_BPartner_ID = bp.C_BPartner_ID)
                 WHERE ((EXTRACT(YEAR FROM d.DueDate) * 12 + EXTRACT(MONTH FROM d.DueDate)) * 31 + EXTRACT(DAY FROM d.DueDate)) >= " + todayInt + @"
                   AND ((EXTRACT(YEAR FROM d.DueDate) * 12 + EXTRACT(MONTH FROM d.DueDate)) * 31 + EXTRACT(DAY FROM d.DueDate)) <= " + plus14Int + @"
                   AND bp.IsActive = 'Y'
                 ORDER BY ((EXTRACT(YEAR FROM d.DueDate) * 12 + EXTRACT(MONTH FROM d.DueDate)) * 31 + EXTRACT(DAY FROM d.DueDate)) ASC";

            DataSet dsDue = DB.ExecuteDataset(strQuery, dataParams, null);
            if (dsDue != null && dsDue.Tables.Count > 0)
            {
                int rowCount = 0;
                foreach (DataRow row in dsDue.Tables[0].Rows)
                {
                    if (rowCount >= 5) { break; }
                    DateTime? dueDateNullable = Util.GetValueOfDateTime(row["DueDate"]);
                    if (dueDateNullable == null) { continue; }
                    DateTime dueDate   = dueDateNullable.Value;
                    int      daysUntil = (dueDate.Date - now.Date).Days;
                    result.DueItems.Add(new DueItem
                    {
                        VendorName   = Util.GetValueOfString(row["VendorName"]),
                        DueDateStr   = dueDate.ToString("MMM d, yyyy"),
                        OpenAmt      = Util.GetValueOfDecimal(row["OpenAmt"]),
                        DaysUntilDue = daysUntil
                    });
                    rowCount++;
                }
            }

            return result;
        }

        private static string FormatAmount(decimal amount, string sym, int precision)
        {
            if (amount >= 10000000m) { return sym + Math.Round(amount / 10000000m, 1) + "Cr"; }
            if (amount >= 100000m)   { return sym + Math.Round(amount / 100000m,   1) + "L";  }
            if (amount >= 1000m)     { return sym + Math.Round(amount / 1000m,      1) + "K";  }
            return sym + Math.Round(amount, precision);
        }

        public class PendingInvoicesResult
        {
            public string        CurSymbol              { get; set; }
            public int           StdPrecision           { get; set; }
            public int           TotalPending           { get; set; }
            public int           AwaitingApprovalCount  { get; set; }
            public decimal       AwaitingApprovalAmt    { get; set; }
            public int           GrnMismatchCount       { get; set; }
            public decimal       GrnMismatchAmt         { get; set; }
            public int           PoNotRaisedCount       { get; set; }
            public decimal       PoNotRaisedAmt         { get; set; }
            public int           ReadyToPayCount        { get; set; }
            public decimal       ReadyToPayAmt          { get; set; }
            public List<DueItem> DueItems               { get; set; }
        }

        public class DueItem
        {
            public string  VendorName   { get; set; }
            public string  DueDateStr   { get; set; }
            public decimal OpenAmt      { get; set; }
            public int     DaysUntilDue { get; set; }
        }
    }
}
