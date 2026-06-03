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

namespace VAS.Controllers
{
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
            IDataReader dr = null;

            try
            {
                bool hasPaymentMethod = HasPaymentMethodColumn();
                bool hasPaymentMethodName = hasPaymentMethod && HasPaymentMethodNameColumn();
                bool hasPaymentMethodValue = hasPaymentMethod && HasPaymentMethodValueColumn();

                string paymentMethodDisplayColumn = string.Empty;

                if (hasPaymentMethod)
                {
                    if (hasPaymentMethodName)
                    {
                        paymentMethodDisplayColumn = "pm.Name";
                    }
                    else if (hasPaymentMethodValue)
                    {
                        paymentMethodDisplayColumn = "pm.Value";
                    }
                }

                string paymentMethodSelect = hasPaymentMethod && !string.IsNullOrEmpty(paymentMethodDisplayColumn)
                    ? @"
                        " + paymentMethodDisplayColumn + @" AS PaymentMethodName,"
                    : @"
                        p.PaymentRule AS PaymentMethodName,";

                string paymentMethodJoin = hasPaymentMethod && !string.IsNullOrEmpty(paymentMethodDisplayColumn)
                    ? @"
                    LEFT OUTER JOIN VA009_PaymentMethod pm ON (p.VA009_PaymentMethod_ID = pm.VA009_PaymentMethod_ID)"
                    : string.Empty;

                string schemaCurrencySql = @"
                    SELECT ClientInfo.AD_Client_ID,
                           AcctSchema.C_Currency_ID AS C_Currency_ID,
                           Currency.StdPrecision,
                           Currency.ISO_Code AS ISO_Code,
                           CASE
                               WHEN Currency.CurSymbol IS NOT NULL THEN Currency.CurSymbol
                               ELSE Currency.ISO_Code
                           END AS Cur_Symbol
                    FROM AD_ClientInfo ClientInfo
                    INNER JOIN C_AcctSchema AcctSchema
                        ON ClientInfo.C_AcctSchema1_ID = AcctSchema.C_AcctSchema_ID
                    INNER JOIN C_Currency Currency
                        ON AcctSchema.C_Currency_ID = Currency.C_Currency_ID";

                string recentPaymentsSql = @"
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

                        ROUND(
                            COALESCE(
                                CASE
                                    WHEN p.C_Currency_ID = SchemaCurrency.C_Currency_ID THEN COALESCE(p.PayAmt, 0)
                                    ELSE CurrencyConvert(
                                        COALESCE(p.PayAmt, 0),
                                        p.C_Currency_ID,
                                        SchemaCurrency.C_Currency_ID,
                                        p.DateAcct,
                                        p.C_ConversionType_ID,
                                        p.AD_Client_ID,
                                        p.AD_Org_ID
                                    )
                                END,
                                0
                            ),
                            MAX(SchemaCurrency.StdPrecision)
                        ) AS Amount,

                        MAX(SchemaCurrency.C_Currency_ID) AS C_Currency_ID,
                        MAX(SchemaCurrency.ISO_Code) AS CurrencyISO,
                        MAX(SchemaCurrency.Cur_Symbol) AS CurrencySymbol

                    FROM C_Payment p
                    INNER JOIN SchemaCurrency SchemaCurrency
                        ON SchemaCurrency.AD_Client_ID = p.AD_Client_ID
                    LEFT OUTER JOIN C_BPartner bp ON (p.C_BPartner_ID = bp.C_BPartner_ID)
                    LEFT OUTER JOIN C_AllocationLine al ON (p.C_Payment_ID = al.C_Payment_ID)
                    LEFT OUTER JOIN C_Invoice inv ON (al.C_Invoice_ID = inv.C_Invoice_ID)
                    LEFT OUTER JOIN C_Order ord ON (inv.C_Order_ID = ord.C_Order_ID)"
                    + paymentMethodJoin + @"
                    WHERE p.IsActive = 'Y'
                    AND p.IsReceipt = 'N'
                ";

                recentPaymentsSql = MRole.GetDefault(ctx).AddAccessSQL(
                    recentPaymentsSql,
                    "p",
                    MRole.SQL_FULLYQUALIFIED,
                    MRole.SQL_RO
                );

                recentPaymentsSql += @"
                    GROUP BY
                        p.C_Payment_ID,
                        p.DateAcct,
                        bp.Name,
                        " + (hasPaymentMethod && !string.IsNullOrEmpty(paymentMethodDisplayColumn) ? paymentMethodDisplayColumn + "," : "p.PaymentRule,") + @"
                        p.DocumentNo,
                        p.DocStatus,
                        p.PayAmt,
                        p.C_Currency_ID,
                        p.C_ConversionType_ID,
                        p.AD_Client_ID,
                        p.AD_Org_ID,
                        SchemaCurrency.C_Currency_ID
                    ORDER BY p.DateAcct DESC, p.C_Payment_ID DESC
                ";

                string sql = @"
                    WITH SchemaCurrency AS (
                        " + schemaCurrencySql + @"
                    )
                    " + recentPaymentsSql;

                dr = DB.ExecuteReader(sql);

                List<object> payments = new List<object>();
                List<string> autoMatchedRefs = new List<string>();
                int autoMatchedCount = 0;

                while (dr.Read() && payments.Count < 7)
                {
                    string docStatus = Util.GetValueOfString(dr["DocStatus"]);
                    string statusType = GetStatusClass(docStatus);
                    string statusKey = GetStatusMessageKey(docStatus);
                    string statusName = GetMsg(ctx, statusKey, Msg.GetMsg(ctx, statusKey));
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
                        paymentDate = FormatDate(Util.GetValueOfDateTime(dr["PaymentDate"])),
                        vendorName = Util.GetValueOfString(dr["VendorName"]),
                        paymentMethodName = GetPaymentMethodName(ctx, Util.GetValueOfString(dr["PaymentMethodName"])),
                        referenceNo = referenceNo,
                        docStatus = docStatus,
                        statusType = statusType,
                        statusKey = statusKey,
                        statusName = statusName,
                        amount = Util.GetValueOfDecimal(dr["Amount"]),
                        cCurrencyId = Util.GetValueOfInt(dr["C_Currency_ID"]),
                        currencyISO = Util.GetValueOfString(dr["CurrencyISO"]),
                        currencySymbol = Util.GetValueOfString(dr["CurrencySymbol"])
                    });
                }

                return Json(new
                {
                    title = GetMsg(ctx, "VAS_032_MessageRecentPayments", "Recent payments"),
                    newPaymentText = GetMsg(ctx, "VAS_032_MessageNewPayment", "+ New payment"),
                    reviewText = GetMsg(ctx, "VAS_032_MessageReview", "Review"),
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
                INNER JOIN AD_Column c ON (t.AD_Table_ID = c.AD_Table_ID)
                WHERE t.TableName = 'C_Payment'
                AND c.ColumnName = 'VA009_PaymentMethod_ID'
            ";

            return Util.GetValueOfInt(DB.ExecuteScalar(sql)) > 0;
        }

        private bool HasPaymentMethodNameColumn()
        {
            string sql = @"
                SELECT COUNT(1)
                FROM AD_Table t
                INNER JOIN AD_Column c ON (t.AD_Table_ID = c.AD_Table_ID)
                WHERE t.TableName = 'VA009_PaymentMethod'
                AND c.ColumnName = 'Name'
            ";

            return Util.GetValueOfInt(DB.ExecuteScalar(sql)) > 0;
        }

        private bool HasPaymentMethodValueColumn()
        {
            string sql = @"
                SELECT COUNT(1)
                FROM AD_Table t
                INNER JOIN AD_Column c ON (t.AD_Table_ID = c.AD_Table_ID)
                WHERE t.TableName = 'VA009_PaymentMethod'
                AND c.ColumnName = 'Value'
            ";

            return Util.GetValueOfInt(DB.ExecuteScalar(sql)) > 0;
        }

        private string GetStatusClass(string docStatus)
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

        private string GetStatusMessageKey(string docStatus)
        {
            if (docStatus == "RE" || docStatus == "VO")
            {
                return "VAS_032_MessageBounced";
            }

            if (docStatus == "CO" || docStatus == "CL")
            {
                return "VAS_032_MessageCleared";
            }

            return "VAS_032_MessageInTransit";
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
