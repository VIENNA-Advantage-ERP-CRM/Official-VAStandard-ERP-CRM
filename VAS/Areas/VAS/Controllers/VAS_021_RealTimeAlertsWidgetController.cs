/************************************************************
 * Module Name    : VAS
 * Purpose        : Real-Time Alerts Widget
 * Created Date   : 14 May 2026
 * Created by     : Humam Yousif
 *
 * AD_Message keys used in this file (add via System Messages):
 *   VAS_021_PaymentOverdue        => "Payment overdue"
 *   VAS_021_OneDayPastDue         => "1 day past due date"
 *   VAS_021_DaysPastDue           => "days past due date"
 *   VAS_021_InvAwaitingApproval   => "invoices awaiting approval"
 *   VAS_021_BlockedApprovalQueue  => "blocked in approval queue"
 *   VAS_021_GrnMismatch           => "GRN mismatch"
 *   VAS_021_Invoices              => "invoices"
 *   VAS_021_PendingReconciliation => "pending reconciliation"
 *   VAS_021_DpoRising             => "DPO rising — currently "
 *   VAS_021_Days                  => "days"
 *   VAS_021_DpoTarget             => "Target 30d. Gap: "
 *   VAS_021_InvoiceReceived       => "invoice received"
 *   VAS_021_ReadyForApproval      => "ready for approval"
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
    public class VAS_021_RealTimeAlertsWidgetController : Controller
    {
        string strQuery = "";

        [Authorize]
        public ActionResult Index()
        {
            return View();
        }

        /// <summary>
        /// Returns a list of real-time purchase-payables alert items built from live DB data.
        /// Uses 6 DB round-trips: 1 currency + 5 alert-category queries.
        /// MRole applied only to the join-free C_Invoice base query in each round-trip.
        /// Outer queries add JOINs, EXTRACT arithmetic, and NOT EXISTS checks outside MRole scope.
        /// </summary>
        public JsonResult GetAlerts()
        {
            string retJSON = "";
            if (Session["ctx"] != null)
            {
                Ctx ctx = Session["ctx"] as Ctx;
                RealTimeAlertsResult result = BuildResult(ctx);
                retJSON = JsonConvert.SerializeObject(result);
            }
            return Json(retJSON, JsonRequestBehavior.AllowGet);
        }

        private RealTimeAlertsResult BuildResult(Ctx ctx)
        {
            var result = new RealTimeAlertsResult
            {
                Alerts = new List<AlertItem>()
            };

            int clientId  = ctx.GetAD_Client_ID();
            SqlParameter[] dataParams   = { new SqlParameter("@ClientID", clientId) };
            DateTime now  = DateTime.Now;
            int todayInt  = (now.Year * 12 + now.Month) * 31 + now.Day;

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

            string sym = result.CurSymbol;

            // Round-trip 2 — Top 2 overdue vendors: unpaid DueAmt past due date from C_InvoicePaySchedule.
            // MRole on join-free C_Invoice base; C_InvoicePaySchedule + C_BPartner joins in outer query.
            string baseOverdue = @"SELECT i.C_Invoice_ID, i.C_Currency_ID, i.DateAcct, i.C_ConversionType_ID, i.AD_Client_ID, i.AD_Org_ID, i.C_BPartner_ID, i.IsReturnTrx
                  FROM C_Invoice i
                 WHERE i.IsSOTrx = 'N'
                   AND i.IsExpenseInvoice = 'N'
                   AND i.DocStatus IN ('CO', 'CL')
                   AND i.IsActive = 'Y'
                   AND i.AD_Client_ID = @ClientID";
            baseOverdue = MRole.GetDefault(ctx).AddAccessSQL(baseOverdue, "i", MRole.SQL_FULLYQUALIFIED, MRole.SQL_RO);

            strQuery = @"SELECT bp.Name AS VendorName,
                       SUM(CASE WHEN ov.IsReturnTrx = 'N'
                                THEN COALESCE(currencyConvert(ips.DueAmt, ov.C_Currency_ID, " + schemaCurrencyId + @", ov.DateAcct, ov.C_ConversionType_ID, ov.AD_Client_ID, ov.AD_Org_ID), 0)
                                WHEN ov.IsReturnTrx = 'Y'
                                THEN -COALESCE(currencyConvert(ips.DueAmt, ov.C_Currency_ID, " + schemaCurrencyId + @", ov.DateAcct, ov.C_ConversionType_ID, ov.AD_Client_ID, ov.AD_Org_ID), 0)
                                ELSE 0 END) AS OverdueAmt,
                       COUNT(DISTINCT ov.C_Invoice_ID) AS InvCount,
                       (" + todayInt + @" - MIN(((EXTRACT(YEAR FROM ips.DueDate) * 12 + EXTRACT(MONTH FROM ips.DueDate)) * 31 + EXTRACT(DAY FROM ips.DueDate)))) AS MaxDaysLate
                  FROM (" + baseOverdue + @") ov
                 INNER JOIN C_InvoicePaySchedule ips ON (ips.C_Invoice_ID = ov.C_Invoice_ID)
                 INNER JOIN C_BPartner bp ON (ov.C_BPartner_ID = bp.C_BPartner_ID)
                 WHERE ips.IsActive = 'Y'
                   AND ips.VA009_IsPaid = 'N'
                   AND ips.DueAmt > 0
                   AND ips.DueDate IS NOT NULL
                   AND ((EXTRACT(YEAR FROM ips.DueDate) * 12 + EXTRACT(MONTH FROM ips.DueDate)) * 31 + EXTRACT(DAY FROM ips.DueDate)) < " + todayInt + @"
                   AND bp.IsActive = 'Y'
                 GROUP BY bp.Name
                 ORDER BY OverdueAmt DESC";

            DataSet dsOverdue = DB.ExecuteDataset(strQuery, dataParams, null);
            if (dsOverdue != null && dsOverdue.Tables.Count > 0)
            {
                int rowCount = 0;
                foreach (DataRow row in dsOverdue.Tables[0].Rows)
                {
                    if (rowCount >= 2) { break; }
                    string vendor   = Util.GetValueOfString(row["VendorName"]);
                    decimal amt     = Util.GetValueOfDecimal(row["OverdueAmt"]);
                    int daysLate    = Math.Max(0, Util.GetValueOfInt(row["MaxDaysLate"]));
                    string dayLabel = daysLate == 1
                        ? Msg.GetMsg(ctx, "VAS_021_OneDayPastDue")
                        : daysLate + " " + Msg.GetMsg(ctx, "VAS_021_DaysPastDue");
                    result.Alerts.Add(new AlertItem
                    {
                        Type     = "err",
                        Icon     = daysLate > 10 ? "🔴" : "⚠️",
                        Title    = Msg.GetMsg(ctx, "VAS_021_PaymentOverdue") + " — " + vendor,
                        Subtitle = FormatAmount(amt, sym, result.StdPrecision) + " | " + dayLabel
                    });
                    rowCount++;
                }
            }

            // Round-trip 3 — Invoices pending approval (DocStatus = 'IP' = In Process / approval workflow).
            // No C_InvoicePaySchedule join — in-process invoices have no payment schedule yet.
            // GrandTotal with IsReturnTrx sign used as the blocked amount.
            string basePending = @"SELECT i.C_Invoice_ID, i.C_Currency_ID, i.DateAcct, i.C_ConversionType_ID, i.AD_Client_ID, i.AD_Org_ID, i.GrandTotal, i.IsReturnTrx
                  FROM C_Invoice i
                 WHERE i.IsSOTrx = 'N'
                   AND i.IsExpenseInvoice = 'N'
                   AND i.DocStatus = 'IP'
                   AND i.IsActive = 'Y'
                   AND i.AD_Client_ID = @ClientID";
            basePending = MRole.GetDefault(ctx).AddAccessSQL(basePending, "i", MRole.SQL_FULLYQUALIFIED, MRole.SQL_RO);

            strQuery = @"SELECT COUNT(1) AS InvCount,
                       SUM(CASE WHEN p.IsReturnTrx = 'N'
                                THEN COALESCE(currencyConvert(p.GrandTotal, p.C_Currency_ID, " + schemaCurrencyId + @", p.DateAcct, p.C_ConversionType_ID, p.AD_Client_ID, p.AD_Org_ID), 0)
                                WHEN p.IsReturnTrx = 'Y'
                                THEN -COALESCE(currencyConvert(p.GrandTotal, p.C_Currency_ID, " + schemaCurrencyId + @", p.DateAcct, p.C_ConversionType_ID, p.AD_Client_ID, p.AD_Org_ID), 0)
                                ELSE 0 END) AS TotalAmt
                  FROM (" + basePending + @") p";

            DataSet dsPending = DB.ExecuteDataset(strQuery, dataParams, null);
            if (dsPending != null && dsPending.Tables.Count > 0 && dsPending.Tables[0].Rows.Count > 0)
            {
                int cnt       = Util.GetValueOfInt(dsPending.Tables[0].Rows[0]["InvCount"]);
                decimal total = Util.GetValueOfDecimal(dsPending.Tables[0].Rows[0]["TotalAmt"]);
                if (cnt > 0)
                {
                    result.Alerts.Add(new AlertItem
                    {
                        Type     = "warn",
                        Icon     = "📋",
                        Title    = cnt + " " + Msg.GetMsg(ctx, "VAS_021_InvAwaitingApproval"),
                        Subtitle = FormatAmount(total, sym, result.StdPrecision) + " " + Msg.GetMsg(ctx, "VAS_021_BlockedApprovalQueue")
                    });
                }
            }

            // Round-trip 4 — GRN mismatch: vendor invoice lines without a matching M_MatchInv record.
            // MRole on join-free C_Invoice base; C_InvoiceLine join and NOT EXISTS check in outer query.
            // IsReturnTrx sign applied to LineNetAmt; no payment schedule needed (amount from invoice lines).
            string baseGRN = @"SELECT i.C_Invoice_ID, i.C_Currency_ID, i.DateAcct, i.C_ConversionType_ID, i.AD_Client_ID, i.AD_Org_ID, i.IsReturnTrx
                  FROM C_Invoice i
                 WHERE i.IsSOTrx = 'N'
                   AND i.IsExpenseInvoice = 'N'
                   AND i.DocStatus IN ('CO', 'CL')
                   AND i.IsActive = 'Y'
                   AND i.AD_Client_ID = @ClientID";
            baseGRN = MRole.GetDefault(ctx).AddAccessSQL(baseGRN, "i", MRole.SQL_FULLYQUALIFIED, MRole.SQL_RO);

            strQuery = @"SELECT COUNT(DISTINCT base_i.C_Invoice_ID) AS InvCount,
                       SUM(CASE WHEN base_i.IsReturnTrx = 'N'
                                THEN COALESCE(currencyConvert(il.LineNetAmt, base_i.C_Currency_ID, " + schemaCurrencyId + @", base_i.DateAcct, base_i.C_ConversionType_ID, base_i.AD_Client_ID, base_i.AD_Org_ID), 0)
                                WHEN base_i.IsReturnTrx = 'Y'
                                THEN -COALESCE(currencyConvert(il.LineNetAmt, base_i.C_Currency_ID, " + schemaCurrencyId + @", base_i.DateAcct, base_i.C_ConversionType_ID, base_i.AD_Client_ID, base_i.AD_Org_ID), 0)
                                ELSE 0 END) AS TotalAmt
                  FROM (" + baseGRN + @") base_i
                 INNER JOIN C_InvoiceLine il ON (il.C_Invoice_ID = base_i.C_Invoice_ID)
                 WHERE il.IsActive = 'Y'
                   AND il.M_Product_ID IS NOT NULL
                   AND NOT EXISTS (SELECT 1 FROM M_MatchInv mi WHERE mi.C_InvoiceLine_ID = il.C_InvoiceLine_ID AND mi.IsActive = 'Y')";

            DataSet dsGRN = DB.ExecuteDataset(strQuery, dataParams, null);
            if (dsGRN != null && dsGRN.Tables.Count > 0 && dsGRN.Tables[0].Rows.Count > 0)
            {
                int cnt       = Util.GetValueOfInt(dsGRN.Tables[0].Rows[0]["InvCount"]);
                decimal total = Util.GetValueOfDecimal(dsGRN.Tables[0].Rows[0]["TotalAmt"]);
                if (cnt > 0)
                {
                    result.Alerts.Add(new AlertItem
                    {
                        Type     = "warn",
                        Icon     = "🔗",
                        Title    = Msg.GetMsg(ctx, "VAS_021_GrnMismatch") + " — " + cnt + Msg.GetMsg(ctx, "VAS_021_Invoices"),
                        Subtitle = FormatAmount(total, sym, result.StdPrecision) + " " + Msg.GetMsg(ctx, "VAS_021_PendingReconciliation")
                    });
                }
            }

            // Round-trip 5 — DPO: average days unpaid invoices have been outstanding this year.
            // Joins C_InvoicePaySchedule to filter VA009_IsPaid = 'N'. EXTRACT arithmetic in outer query.
            string baseDPO = @"SELECT i.C_Invoice_ID, i.DateInvoiced
                  FROM C_Invoice i
                 WHERE i.IsSOTrx = 'N'
                   AND i.IsExpenseInvoice = 'N'
                   AND i.DocStatus IN ('CO', 'CL')
                   AND i.IsActive = 'Y'
                   AND i.AD_Client_ID = @ClientID
                   AND EXTRACT(YEAR FROM i.DateInvoiced) = " + now.Year;
            baseDPO = MRole.GetDefault(ctx).AddAccessSQL(baseDPO, "i", MRole.SQL_FULLYQUALIFIED, MRole.SQL_RO);

            strQuery = @"SELECT AVG(" + todayInt + @" - ((EXTRACT(YEAR FROM d.DateInvoiced) * 12 + EXTRACT(MONTH FROM d.DateInvoiced)) * 31 + EXTRACT(DAY FROM d.DateInvoiced))) AS AvgDPO,
                       COUNT(DISTINCT d.C_Invoice_ID) AS InvCount
                  FROM (" + baseDPO + @") d
                 INNER JOIN C_InvoicePaySchedule ips ON (ips.C_Invoice_ID = d.C_Invoice_ID)
                 WHERE ips.IsActive = 'Y'
                   AND ips.VA009_IsPaid = 'N'
                   AND ips.DueAmt > 0";

            DataSet dsDPO = DB.ExecuteDataset(strQuery, dataParams, null);
            if (dsDPO != null && dsDPO.Tables.Count > 0 && dsDPO.Tables[0].Rows.Count > 0)
            {
                int cnt        = Util.GetValueOfInt(dsDPO.Tables[0].Rows[0]["InvCount"]);
                decimal avgDPO = Util.GetValueOfDecimal(dsDPO.Tables[0].Rows[0]["AvgDPO"]);
                // avgDPO is in the (Y*12+M)*31+D integer encoding; difference approximates real days (±1-2 days/month)
                int dpoDays = (int)Math.Round((double)avgDPO, 0);
                if (cnt > 0 && dpoDays > 0)
                {
                    int gap        = dpoDays - 30;
                    string gapStr  = gap > 0 ? "+" + gap : gap.ToString();
                    string aType   = dpoDays > 30 ? "info" : "ok";
                    result.Alerts.Add(new AlertItem
                    {
                        Type     = aType,
                        Icon     = "📈",
                        Title    = Msg.GetMsg(ctx, "VAS_021_DpoRising") + dpoDays + Msg.GetMsg(ctx, "VAS_021_Days"),
                        Subtitle = Msg.GetMsg(ctx, "VAS_021_DpoTarget") + gapStr + Msg.GetMsg(ctx, "VAS_021_Days")
                    });
                }
            }

            // Round-trip 6 — Most recent vendor invoice from the last 7 days with unpaid DueAmt.
            // Joins C_InvoicePaySchedule (VA009_IsPaid = 'N') + C_BPartner in outer query.
            int sevenDaysAgoInt = (now.AddDays(-7).Year * 12 + now.AddDays(-7).Month) * 31 + now.AddDays(-7).Day;
            string baseReady = @"SELECT i.C_Invoice_ID, i.C_Currency_ID, i.DateAcct, i.C_ConversionType_ID, i.AD_Client_ID, i.AD_Org_ID, i.C_BPartner_ID, i.IsReturnTrx
                  FROM C_Invoice i
                 WHERE i.IsSOTrx = 'N'
                   AND i.IsExpenseInvoice = 'N'
                   AND i.DocStatus = 'CO'
                   AND i.IsActive = 'Y'
                   AND i.AD_Client_ID = @ClientID";
            baseReady = MRole.GetDefault(ctx).AddAccessSQL(baseReady, "i", MRole.SQL_FULLYQUALIFIED, MRole.SQL_RO);

            strQuery = @"SELECT bp.Name AS VendorName,
                       SUM(CASE WHEN r.IsReturnTrx = 'N'
                                THEN COALESCE(currencyConvert(ips.DueAmt, r.C_Currency_ID, " + schemaCurrencyId + @", r.DateAcct, r.C_ConversionType_ID, r.AD_Client_ID, r.AD_Org_ID), 0)
                                WHEN r.IsReturnTrx = 'Y'
                                THEN -COALESCE(currencyConvert(ips.DueAmt, r.C_Currency_ID, " + schemaCurrencyId + @", r.DateAcct, r.C_ConversionType_ID, r.AD_Client_ID, r.AD_Org_ID), 0)
                                ELSE 0 END) AS DueAmt
                  FROM (" + baseReady + @") r
                 INNER JOIN C_InvoicePaySchedule ips ON (ips.C_Invoice_ID = r.C_Invoice_ID)
                 INNER JOIN C_BPartner bp ON (r.C_BPartner_ID = bp.C_BPartner_ID)
                 WHERE ips.IsActive = 'Y'
                   AND ips.VA009_IsPaid = 'N'
                   AND ips.DueAmt > 0
                   AND ((EXTRACT(YEAR FROM r.DateAcct) * 12 + EXTRACT(MONTH FROM r.DateAcct)) * 31 + EXTRACT(DAY FROM r.DateAcct)) >= " + sevenDaysAgoInt + @"
                   AND bp.IsActive = 'Y'
                 GROUP BY bp.Name
                 ORDER BY MAX(((EXTRACT(YEAR FROM r.DateAcct) * 12 + EXTRACT(MONTH FROM r.DateAcct)) * 31 + EXTRACT(DAY FROM r.DateAcct))) DESC";

            DataSet dsReady = DB.ExecuteDataset(strQuery, dataParams, null);
            if (dsReady != null && dsReady.Tables.Count > 0 && dsReady.Tables[0].Rows.Count > 0)
            {
                string vendor = Util.GetValueOfString(dsReady.Tables[0].Rows[0]["VendorName"]);
                decimal amt   = Util.GetValueOfDecimal(dsReady.Tables[0].Rows[0]["DueAmt"]);
                result.Alerts.Add(new AlertItem
                {
                    Type     = "ok",
                    Icon     = "✅",
                    Title    = vendor + " " + Msg.GetMsg(ctx, "VAS_021_InvoiceReceived"),
                    Subtitle = FormatAmount(amt, sym, result.StdPrecision) + " — " + Msg.GetMsg(ctx, "VAS_021_ReadyForApproval")
                });
            }

            return result;
        }

        private static string FormatAmount(decimal amount, string sym, int precision)
        {
            if (amount >= 10000000m)    { return sym + Math.Round(amount / 10000000m, 1) + "Cr"; }
            if (amount >= 100000m)      { return sym + Math.Round(amount / 100000m,   1) + "L";  }
            if (amount >= 1000m)        { return sym + Math.Round(amount / 1000m,      1) + "K";  }
            return sym + Math.Round(amount, precision);
        }

        public class RealTimeAlertsResult
        {
            public string         CurSymbol    { get; set; }
            public int            StdPrecision { get; set; }
            public List<AlertItem> Alerts      { get; set; }
        }

        public class AlertItem
        {
            public string Type     { get; set; }
            public string Icon     { get; set; }
            public string Title    { get; set; }
            public string Subtitle { get; set; }
        }
    }
}
