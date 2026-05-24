using System;
using System.Collections.Generic;
using System.Data;
using System.Data.SqlClient;
using System.Web.Mvc;
using VAdvantage.Classes;
using VAdvantage.DataBase;
using VAdvantage.Model;
using VAdvantage.Utility;
using VIS.Filters;

namespace VIS.Controllers
{
    public class RecentAPPaymentsController : Controller
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
            IDataReader dr = null;

            try
            {
                bool hasPaymentMethod = HasPaymentMethodColumn();

                string paymentMethodSelect = hasPaymentMethod
                    ? @"
                        pm.Name AS PaymentMethodName,"
                    : @"
                        p.PaymentRule AS PaymentMethodName,";

                string paymentMethodJoin = hasPaymentMethod
                    ? @"
                    LEFT OUTER JOIN VA009_PaymentMethod pm ON (p.VA009_PaymentMethod_ID=pm.VA009_PaymentMethod_ID)"
                    : string.Empty;

                string sql = @"
                    SELECT
                        p.C_Payment_ID,
                        p.DateAcct AS PaymentDate,
                        bp.Name AS VendorName,"
                        + paymentMethodSelect + @"
                        COALESCE(MAX(inv.DocumentNo), MAX(ord.DocumentNo), p.DocumentNo) AS ReferenceNo,
                        CASE
                            WHEN MAX(inv.DocumentNo) IS NOT NULL OR MAX(ord.DocumentNo) IS NOT NULL THEN 'Y'
                            ELSE 'N'
                        END AS HasBusinessRef,
                        p.DocStatus,
                        p.PayAmt AS Amount,
                        p.C_Currency_ID,
                        cur.ISO_Code AS CurrencyISO,
                        cur.CurSymbol AS CurrencySymbol
                    FROM C_Payment p
                    LEFT OUTER JOIN C_BPartner bp ON (p.C_BPartner_ID=bp.C_BPartner_ID)
                    LEFT OUTER JOIN C_Currency cur ON (p.C_Currency_ID=cur.C_Currency_ID)
                    LEFT OUTER JOIN C_AllocationLine al ON (p.C_Payment_ID=al.C_Payment_ID)
                    LEFT OUTER JOIN C_Invoice inv ON (al.C_Invoice_ID=inv.C_Invoice_ID)
                    LEFT OUTER JOIN C_Order ord ON (inv.C_Order_ID=ord.C_Order_ID)"
                    + paymentMethodJoin + @"
                    WHERE p.IsActive='Y'
                    AND p.IsReceipt=@IsReceipt
                ";

                sql = MRole.GetDefault(ctx).AddAccessSQL(sql, "p", MRole.SQL_FULLYQUALIFIED, MRole.SQL_RO);

                sql += @"
                    GROUP BY
                        p.C_Payment_ID,
                        p.DateAcct,
                        bp.Name,
                        " + (hasPaymentMethod ? "pm.Name," : "p.PaymentRule,") + @"
                        p.DocumentNo,
                        p.DocStatus,
                        p.PayAmt,
                        p.C_Currency_ID,
                        cur.ISO_Code,
                        cur.CurSymbol
                    ORDER BY p.DateAcct DESC, p.C_Payment_ID DESC
                ";

                List<SqlParameter> parameters = new List<SqlParameter>
                {
                    new SqlParameter("@IsReceipt", "N")
                };

                dr = DB.ExecuteReader(sql, parameters.ToArray());

                List<object> payments = new List<object>();
                List<string> autoMatchedRefs = new List<string>();
                int autoMatchedCount = 0;

                while (dr.Read() && payments.Count < 7)
                {
                    string docStatus = Util.GetValueOfString(dr["DocStatus"]);
                    string statusType = GetStatusType(docStatus);
                    string statusName = GetStatusName(ctx, statusType);
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

                    payments.Add(new
                    {
                        paymentId = Util.GetValueOfInt(dr["C_Payment_ID"]),
                        paymentDate = Util.GetValueOfDateTime(dr["PaymentDate"]),
                        vendorName = Util.GetValueOfString(dr["VendorName"]),
                        paymentMethodName = GetPaymentMethodName(ctx, Util.GetValueOfString(dr["PaymentMethodName"])),
                        referenceNo = referenceNo,
                        docStatus = docStatus,
                        statusType = statusType,
                        statusName = statusName,
                        amount = Util.GetValueOfDecimal(dr["Amount"]),
                        cCurrencyId = Util.GetValueOfInt(dr["C_Currency_ID"]),
                        currencyISO = Util.GetValueOfString(dr["CurrencyISO"]),
                        currencySymbol = Util.GetValueOfString(dr["CurrencySymbol"])
                    });
                }

                return Json(new
                {
                    title = GetMsg(ctx, "VAS_RecentPayments", "Recent payments"),
                    newPaymentText = GetMsg(ctx, "VAS_NewPayment", "+ New payment"),
                    reviewText = GetMsg(ctx, "VAS_Review", "Review"),
                    autoMatchedCount = autoMatchedCount,
                    autoMatchedRefs = autoMatchedRefs,
                    payments = payments
                }, JsonRequestBehavior.AllowGet);
            }
            catch (Exception ex)
            {
                return Json(new
                {
                    error = ex.Message
                }, JsonRequestBehavior.AllowGet);
            }
            finally
            {
                if (dr != null)
                {
                    dr.Close();
                    dr.Dispose();
                }
            }
        }

        private bool HasPaymentMethodColumn()
        {
            string sql = @"
                SELECT COUNT(1)
                FROM AD_Table t
                INNER JOIN AD_Column c ON (t.AD_Table_ID=c.AD_Table_ID)
                WHERE t.TableName='C_Payment'
                AND c.ColumnName='VA009_PaymentMethod_ID'
            ";

            return Util.GetValueOfInt(DB.ExecuteScalar(sql)) > 0;
        }

        private string GetStatusType(string docStatus)
        {
            if (docStatus == "RE" || docStatus == "VO")
            {
                return "bounced";
            }

            if (docStatus == "CO" || docStatus == "CL")
            {
                return "cleared";
            }

            return "intransit";
        }

        private string GetStatusName(Ctx ctx, string statusType)
        {
            if (statusType == "bounced")
            {
                return GetMsg(ctx, "VAS_Bounced", "Bounced");
            }

            if (statusType == "intransit")
            {
                return GetMsg(ctx, "VAS_InTransit", "In transit");
            }

            return GetMsg(ctx, "VAS_Cleared", "Cleared");
        }

        private string GetPaymentMethodName(Ctx ctx, string paymentMethodName)
        {
            if (string.IsNullOrEmpty(paymentMethodName))
            {
                return GetMsg(ctx, "VAS_NotSpecified", "Not Specified");
            }

            return paymentMethodName;
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