/******************************************************
 * Module Name    : VAS
 * Purpose        : Customers module Delayed Payments KPI widget endpoint
 * chronological  : Development
 * Created Date   : 2026-07-21
 * Created by     : VAI052
 ******************************************************/

using Newtonsoft.Json;
using System;
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
    /// Module Name : VAS_124_DelayedPaymentsWidget
    /// Purpose     : Static 2x1 KPI tile - the total OUTSTANDING amount on overdue
    ///               customer receivables and the count of distinct customers in
    ///               arrears, in the logged-in tenant and the organizations the
    ///               role may access. Overdue = active C_InvoicePaySchedule rows still
    ///               unpaid (VA009_IsPaid='N') whose DueDate is before today, on
    ///               completed/closed customer sales invoices (DocStatus IN
    ///               ('CO','CL'), IsSOTrx='Y', IsReturnTrx='N', IsCustomer='Y') with a
    ///               positive outstanding. Outstanding = DueAmt - VA009_PaidAmntInvce
    ///               - VA009_Variance (correct for partial payments) converted to the
    ///               tenant accounting currency via CurrencyConvert. This definition is
    ///               identical to the VAS_138 Delayed Payments list, so the KPI total
    ///               and customer count reconcile exactly with that list. MRole (tenant
    ///               + org + record access) is applied to the main physical table
    ///               C_InvoicePaySchedule only.
    /// Chronological development:
    ///   VAI052      2026-07-21 Created
    ///   VAI052      2026-07-23 Aligned overdue definition to the VAS_138 list (net outstanding, customer-only, open>0, no returns) so both reconcile
    /// </summary>
    public class VAS_124_DelayedPaymentsWidgetController : Controller
    {
        private static readonly VLogger Log = VLogger.GetVLogger(typeof(VAS_124_DelayedPaymentsWidgetController).FullName);

        /// <summary>
        /// KPI tile data: base-currency total of overdue customer receivables plus
        /// the distinct overdue customer and invoice counts, within the authorized
        /// scope, with the tenant currency for client-side formatting.
        /// </summary>
        /// <returns>
        /// JSON { overdue_amount, overdue_customer_count, overdue_invoice_count,
        /// currency_symbol, currency_iso, std_precision } or { error }.
        /// </returns>
        [AjaxAuthorizeAttribute]
        [AjaxSessionFilterAttribute]
        public JsonResult GetDelayedPayments()
        {
            if (Session["ctx"] == null)
            {
                return Json(new
                {
                    error = Msg.GetMsg(Env.GetCtx(), "SessionExpired") ?? "Session Expired"
                }, JsonRequestBehavior.AllowGet);
            }

            Ctx ctx = Session["ctx"] as Ctx;

            try
            {
                // Overdue = due strictly before today. Dialect-specific date
                // truncation keeps the comparison stable across a DueDate that may
                // carry a time component (mirrors OverdueController).
                string overdueDateCondition = DB.IsPostgreSQL()
                    ? " AND CAST(ips.DueDate AS DATE) < CAST(CURRENT_DATE AS DATE)"
                    : " AND TRUNC(ips.DueDate) < TRUNC(SYSDATE)";

                // Single-row tenant accounting currency (symbol, ISO, precision).
                string schemaCurrencySql = @"
                    SELECT ci.AD_Client_ID AS AD_Client_ID,
                           cs.C_Currency_ID AS Acct_Currency_ID,
                           cur.StdPrecision AS Std_Precision,
                           cur.ISO_Code AS ISO_Code,
                           CASE WHEN cur.CurSymbol IS NOT NULL THEN cur.CurSymbol ELSE cur.ISO_Code END AS Cur_Symbol
                    FROM AD_ClientInfo ci
                    INNER JOIN C_AcctSchema cs ON (cs.C_AcctSchema_ID=ci.C_AcctSchema1_ID AND cs.IsActive = 'Y')
                    INNER JOIN C_Currency cur ON (cur.C_Currency_ID=cs.C_Currency_ID AND cur.IsActive = 'Y')
                    WHERE ci.IsActive = 'Y'
                      AND ci.AD_Client_ID = @Client_ID";

                // Outstanding installment amount, correct for partial payments, and
                // its conversion to the tenant accounting currency. Identical to the
                // VAS_138 list widget so the KPI total and customer count reconcile
                // exactly with that list.
                string openAmtExpr =
                    "(COALESCE(ips.DueAmt, 0) - COALESCE(ips.VA009_PaidAmntInvce, 0) - COALESCE(ips.VA009_Variance, 0))";
                string baseAmtExpr =
                    "CurrencyConvert(" + openAmtExpr + ", i.C_Currency_ID, sc.Acct_Currency_ID, i.DateAcct, i.C_ConversionType_ID, i.AD_Client_ID, i.AD_Org_ID)";

                // Aggregate the overdue figures in one subquery. An aggregate with
                // no GROUP BY always yields exactly one row (SUM/COUNT = 0 when
                // nothing matches), so CROSS JOINing it with the single-row currency
                // CTE always carries the currency - even at zero overdue.
                string overdueSql = @"
                    SELECT COALESCE(SUM(" + baseAmtExpr + @"), 0) AS Total_Raw,
                           COUNT(DISTINCT i.C_Invoice_ID) AS Overdue_Invoice_Count,
                           COUNT(DISTINCT i.C_BPartner_ID) AS Overdue_Customer_Count
                    FROM C_InvoicePaySchedule ips
                    INNER JOIN C_Invoice i ON (ips.C_Invoice_ID=i.C_Invoice_ID AND i.IsActive = 'Y')
                    INNER JOIN C_BPartner bp ON (bp.C_BPartner_ID=i.C_BPartner_ID AND bp.AD_Client_ID=i.AD_Client_ID AND bp.IsActive = 'Y' AND bp.IsCustomer = 'Y')
                    INNER JOIN schema_currency sc ON (sc.AD_Client_ID=i.AD_Client_ID)
                    WHERE ips.IsActive = 'Y'
                      AND ips.VA009_IsPaid = 'N'" + overdueDateCondition + @"
                      AND i.DocStatus IN ('CO', 'CL')
                      AND i.IsSOTrx = 'Y'
                      AND i.IsReturnTrx = 'N'
                      AND i.AD_Client_ID = @Client_ID
                      AND ROUND(" + openAmtExpr + @", COALESCE(sc.Std_Precision, 2)) > 0";

                // MRole supplies tenant + organization + record access on the main
                // physical table alias "ips" (CTE MRole rule: main data source only,
                // not the secondary C_Invoice join or the currency CTE).
                overdueSql = MRole.GetDefault(ctx).AddAccessSQL(
                    overdueSql,
                    "ips",
                    MRole.SQL_FULLYQUALIFIED,
                    MRole.SQL_RO
                );

                string sql = @"
                    WITH schema_currency AS (
                        " + schemaCurrencySql + @"
                    ),
                    overdue AS (
                        " + overdueSql + @"
                    )
                    SELECT ROUND(o.Total_Raw, sc.Std_Precision) AS Overdue_Amount,
                           o.Overdue_Invoice_Count AS Overdue_Invoice_Count,
                           o.Overdue_Customer_Count AS Overdue_Customer_Count,
                           sc.Cur_Symbol AS Cur_Symbol,
                           sc.ISO_Code AS ISO_Code,
                           sc.Std_Precision AS Std_Precision
                    FROM schema_currency sc
                    CROSS JOIN overdue o";

                decimal overdueAmount = 0;
                int overdueInvoiceCount = 0;
                int overdueCustomerCount = 0;
                string currencySymbol = "";
                string isoCode = "";
                int stdPrecision = 2;

                IDataReader dr = null;
                try
                {
                    dr = DB.ExecuteReader(
                        sql,
                        new SqlParameter[] { new SqlParameter("@Client_ID", ctx.GetAD_Client_ID()) }
                    );

                    if (dr != null && dr.Read())
                    {
                        overdueAmount = Util.GetValueOfDecimal(dr["Overdue_Amount"]);
                        overdueInvoiceCount = Util.GetValueOfInt(dr["Overdue_Invoice_Count"]);
                        overdueCustomerCount = Util.GetValueOfInt(dr["Overdue_Customer_Count"]);
                        currencySymbol = Util.GetValueOfString(dr["Cur_Symbol"]);
                        isoCode = Util.GetValueOfString(dr["ISO_Code"]);
                        if (dr["Std_Precision"] != null && dr["Std_Precision"] != DBNull.Value)
                        {
                            stdPrecision = Util.GetValueOfInt(dr["Std_Precision"]);
                        }
                    }
                }
                finally
                {
                    if (dr != null)
                    {
                        dr.Close();
                        dr.Dispose();
                    }
                }

                var result = new
                {
                    overdue_amount = overdueAmount,
                    overdue_customer_count = overdueCustomerCount,
                    overdue_invoice_count = overdueInvoiceCount,
                    currency_symbol = currencySymbol,
                    currency_iso = isoCode,
                    std_precision = stdPrecision
                };

                return Json(JsonConvert.SerializeObject(result), JsonRequestBehavior.AllowGet);
            }
            catch (Exception ex)
            {
                Log.Log(Level.SEVERE, "VAS_124_DelayedPaymentsWidget.GetDelayedPayments", ex);
                return Json(new { error = ex.Message }, JsonRequestBehavior.AllowGet);
            }
        }
    }
}
