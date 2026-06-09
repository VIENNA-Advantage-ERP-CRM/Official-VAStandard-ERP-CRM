using System;
using System.Collections.Generic;
using System.Data;
using System.Web.Mvc;
using VAdvantage.Classes;
using VAdvantage.DataBase;
using VAdvantage.Model;
using VAdvantage.Utility;
using VIS.Filters;

namespace VAS.Controllers
{
    /*
   * Labels / Message Keys
   * 1 | Recent payments | VAS_032_MessageRecentPayments
   * 2 | + New payment   | VAS_032_MessageNewPayment
   * 3 | Review          | VAS_032_MessageReview
   * 4 | Bounced         | VAS_032_MessageBounced
   * 5 | Cleared         | VAS_032_MessageCleared
   * 6 | In transit      | VAS_032_MessageInTransit
   * 7 | Not Specified   | VAS_032_MessageNotSpecified
   */
    public class VAS_032_RecentAPPaymentsWidgetController : Controller
    {
        [AjaxAuthorizeAttribute]
        [AjaxSessionFilterAttribute]
        public JsonResult GetRecentAPPayments()
        {
            if (Session["ctx"] == null)
            {
                return Json(new
                {
                    error = "Session Expired",
                    errorText = "Session Expired"
                }, JsonRequestBehavior.AllowGet);
            }

            Ctx ctx = Session["ctx"] as Ctx;

            try
            {
                string sql = BuildRecentPaymentsSql(ctx);

                using (IDataReader dr = DB.ExecuteReader(sql))
                {
                    List<object> payments = new List<object>();
                    List<string> autoMatchedRefs = new List<string>();
                    int autoMatchedCount = 0;

                    while (dr.Read() && payments.Count < 7)
                    {
                        AddPaymentRow(ctx, dr, payments, autoMatchedRefs, ref autoMatchedCount);
                    }

                    return Json(new
                    {
                        title = GetMsg(ctx, "VAS_032_MessageRecentPayments", "Recent payments"),
                        newPaymentText = GetMsg(ctx, "VAS_032_MessageNewPayment", "+ New payment"),
                        autoMatchedCount = autoMatchedCount,
                        autoMatchedRefs = autoMatchedRefs,
                        payments = payments
                    }, JsonRequestBehavior.AllowGet);
                }
            }
            catch (Exception ex)
            {
                return Json(new
                {
                    error = ex.Message
                }, JsonRequestBehavior.AllowGet);
            }
        }

        private string BuildRecentPaymentsSql(Ctx ctx)
        {
            string schemaCurrencyCte = @"
SchemaCurrency AS (
SELECT ClientInfo.AD_Client_ID,
AcctSchema.C_Currency_ID AS C_Currency_ID,
Currency.StdPrecision,
Currency.ISO_Code AS ISO_Code,
COALESCE(Currency.CurSymbol, Currency.ISO_Code) AS Cur_Symbol
FROM AD_ClientInfo ClientInfo
INNER JOIN C_AcctSchema AcctSchema ON (ClientInfo.C_AcctSchema1_ID=AcctSchema.C_AcctSchema_ID)
INNER JOIN C_Currency Currency ON (AcctSchema.C_Currency_ID=Currency.C_Currency_ID)
)";

            string paymentAccessSql = @"
SELECT p.C_Payment_ID
FROM C_Payment p
WHERE p.IsActive='Y'
AND p.IsReceipt='N'
AND p.DocStatus IN ('CO', 'CL')";

            /*
             * Apply MRole only on the main physical table C_Payment p.
             * Do not apply MRole on the final WITH query.
             * Do not apply MRole on CTE aliases.
             * Do not apply MRole on joined aliases.
             */
            paymentAccessSql = MRole.GetDefault(ctx).AddAccessSQL(
                paymentAccessSql,
                "p",
                MRole.SQL_FULLYQUALIFIED,
                MRole.SQL_RO
            );

            string filteredPaymentCte = @"
FilteredPayment AS (
" + paymentAccessSql + @"
)";

            string finalSql = @"
WITH " + schemaCurrencyCte + @",
" + filteredPaymentCte + @"
SELECT p.C_Payment_ID,
p.DateAcct AS PaymentDate,
bp.Name AS VendorName,
pm.VA009_Name AS PaymentMethodName,
COALESCE(MAX(inv.DocumentNo), MAX(ord.DocumentNo), p.DocumentNo) AS ReferenceNo,
CASE WHEN MAX(inv.DocumentNo) IS NOT NULL OR MAX(ord.DocumentNo) IS NOT NULL THEN 'Y' ELSE 'N' END AS HasBusinessRef,
p.DocStatus,
p.IsReconciled,
p.VA009_ExecutionStatus,
ROUND(CAST(COALESCE(CASE WHEN p.C_Currency_ID=SchemaCurrency.C_Currency_ID THEN COALESCE(p.PayAmt, 0) ELSE CurrencyConvert(COALESCE(p.PayAmt, 0), p.C_Currency_ID, SchemaCurrency.C_Currency_ID, p.DateAcct, p.C_ConversionType_ID, p.AD_Client_ID, p.AD_Org_ID) END, 0) AS NUMERIC), CAST(MAX(SchemaCurrency.StdPrecision) AS INTEGER)) AS Amount,
MAX(SchemaCurrency.C_Currency_ID) AS C_Currency_ID,
MAX(SchemaCurrency.StdPrecision) AS StdPrecision,
MAX(SchemaCurrency.ISO_Code) AS CurrencyISO,
MAX(SchemaCurrency.Cur_Symbol) AS CurrencySymbol
FROM C_Payment p
INNER JOIN FilteredPayment fp ON (p.C_Payment_ID=fp.C_Payment_ID)
INNER JOIN SchemaCurrency SchemaCurrency ON (SchemaCurrency.AD_Client_ID=p.AD_Client_ID)
LEFT OUTER JOIN C_BPartner bp ON (p.C_BPartner_ID=bp.C_BPartner_ID)
LEFT OUTER JOIN VA009_PaymentMethod pm ON (p.VA009_PaymentMethod_ID=pm.VA009_PaymentMethod_ID)
LEFT OUTER JOIN C_AllocationLine al ON (p.C_Payment_ID=al.C_Payment_ID)
LEFT OUTER JOIN C_Invoice inv ON (al.C_Invoice_ID=inv.C_Invoice_ID)
LEFT OUTER JOIN C_Order ord ON (inv.C_Order_ID=ord.C_Order_ID)
GROUP BY p.C_Payment_ID,
p.DateAcct,
bp.Name,
pm.VA009_Name,
p.DocumentNo,
p.DocStatus,
p.IsReconciled,
p.VA009_ExecutionStatus,
p.PayAmt,
p.C_Currency_ID,
p.C_ConversionType_ID,
p.AD_Client_ID,
p.AD_Org_ID,
SchemaCurrency.C_Currency_ID
ORDER BY p.DateAcct DESC, p.C_Payment_ID DESC";

            return finalSql;
        }

        private void AddPaymentRow(
            Ctx ctx,
            IDataReader dr,
            List<object> payments,
            List<string> autoMatchedRefs,
            ref int autoMatchedCount)
        {
            string referenceNo = Util.GetValueOfString(dr["ReferenceNo"]);
            string hasBusinessRef = Util.GetValueOfString(dr["HasBusinessRef"]);

            if (hasBusinessRef == "Y")
            {
                autoMatchedCount++;

                if (!string.IsNullOrEmpty(referenceNo) && autoMatchedRefs.Count < 3)
                {
                    autoMatchedRefs.Add(referenceNo);
                }
            }

            string isReconciled = Util.GetValueOfString(dr["IsReconciled"]);
            string executionStatus = Util.GetValueOfString(dr["VA009_ExecutionStatus"]);

            string statusType = GetStatusType(isReconciled, executionStatus);
            string statusKey = GetStatusMessageKey(ctx , statusType);

            payments.Add(new
            {
                paymentId = Util.GetValueOfInt(dr["C_Payment_ID"]),
                paymentDate = FormatDate(Util.GetValueOfDateTime(dr["PaymentDate"])),
                vendorName = Util.GetValueOfString(dr["VendorName"]),
                paymentMethodName = GetPaymentMethodName(ctx, Util.GetValueOfString(dr["PaymentMethodName"])),
                referenceNo = referenceNo,
                docStatus = Util.GetValueOfString(dr["DocStatus"]),
                statusType = statusType,
                statusKey = statusKey,
                statusName = Msg.GetMsg(ctx, statusKey),
                amount = Util.GetValueOfDecimal(dr["Amount"]),
                cCurrencyId = Util.GetValueOfInt(dr["C_Currency_ID"]),
                currencyISO = Util.GetValueOfString(dr["CurrencyISO"]),
                currencySymbol = Util.GetValueOfString(dr["CurrencySymbol"]),
                stdPrecision = Util.GetValueOfInt(dr["StdPrecision"])
            });
        }

        private string GetStatusType(string isReconciled, string executionStatus)
        {
            if (executionStatus == "B")
            {
                return "bounced";
            }

            if (isReconciled == "Y")
            {
                return "cleared";
            }

            return "intransit";
        }

        private string GetStatusMessageKey(Ctx ctx, string statusType)
        {
            if (statusType == "bounced")
            {
                return GetMsg(ctx, "VAS_032_MessageBounced", "Bounced"); 
            }

            if (statusType == "cleared")
            { 
                return GetMsg(ctx, "VAS_032_MessageCleared", "Cleared");
            }

            return GetMsg(ctx, "VAS_032_MessageInTransit", "InTransit");
        }

        private string GetPaymentMethodName(Ctx ctx, string paymentMethodName)
        {
            if (string.IsNullOrEmpty(paymentMethodName))
            {
                return GetMsg(ctx, "VAS_032_MessageNotSpecified", "Not Specified");
            }

            return paymentMethodName;
        }

        private string FormatDate(DateTime? date)
        {
            return date?.ToString("yyyy-MM-dd");
        }

        private string GetMsg(Ctx ctx, string key, string fallback)
        {
            string msg = Msg.GetMsg(ctx, key);

            return !string.IsNullOrEmpty(msg) && msg != "[" + key + "]"
                ? msg
                : fallback;
        }
    }
}