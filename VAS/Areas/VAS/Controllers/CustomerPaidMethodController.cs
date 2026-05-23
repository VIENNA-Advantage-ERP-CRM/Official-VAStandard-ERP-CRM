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
    public class CustomerPaidMethodController : Controller
    {
        /// <summary>
        /// Returns AR receipt payment method distribution by paid amount.
        /// Only completed/closed receipts are included.
        /// Compatible with Oracle and PostgreSQL.
        /// </summary>
        [AjaxAuthorizeAttribute]
        [AjaxSessionFilterAttribute]
        public JsonResult GetCustomerPaidMethod()
        {
            if (Session["ctx"] == null)
            {
                return Json(new
                {
                    error = Msg.GetMsg(Env.GetCtx(), "SessionExpired") ?? "Session Expired"
                }, JsonRequestBehavior.AllowGet);
            }

            Ctx ctx = Session["ctx"] as Ctx;

            List<SqlParameter> parameters = new List<SqlParameter>();

            parameters.Add(new SqlParameter("@ADUserID", ctx.GetAD_User_ID()));

            string paymentMethodSql = @"
                SELECT PaymentMethod.VA009_Name AS PaymentMethodName,
                       Payment.TenderType AS TenderType,
                       SUM(COALESCE(Payment.PayAmt, 0)) AS MethodAmount

                FROM C_Payment Payment

                LEFT OUTER JOIN VA009_PaymentMethod PaymentMethod
                    ON Payment.VA009_PaymentMethod_ID = PaymentMethod.VA009_PaymentMethod_ID
                   AND PaymentMethod.IsActive = 'Y'

                WHERE Payment.IsReceipt = 'Y'
                  AND Payment.IsActive = 'Y'
                  AND Payment.DocStatus IN ('CO', 'CL')";

            paymentMethodSql = MRole.GetDefault(ctx).AddAccessSQL(
                paymentMethodSql,
                "Payment",
                MRole.SQL_FULLYQUALIFIED,
                MRole.SQL_RO
            );

            paymentMethodSql += @"
                  AND (
                      PaymentMethod.VA009_PaymentMethod_ID IS NULL
                      OR PaymentMethod.VA009_PaymentMethod_ID NOT IN (
                          SELECT PrivateAccess.Record_ID
                          FROM AD_Private_Access PrivateAccess
                          INNER JOIN AD_Table TableInfo
                              ON TableInfo.AD_Table_ID = PrivateAccess.AD_Table_ID
                          WHERE TableInfo.TableName = 'VA009_PaymentMethod'
                            AND PrivateAccess.AD_User_ID <> @ADUserID
                            AND PrivateAccess.IsActive = 'Y'
                      )
                  )

                GROUP BY PaymentMethod.VA009_Name,
                         Payment.TenderType";

            string sql = @"
                WITH PaymentMethodData AS (
                    " + paymentMethodSql + @"
                ),
                TotalData AS (
                    SELECT SUM(MethodAmount) AS TotalAmount
                    FROM PaymentMethodData
                )
                SELECT PaymentMethodData.PaymentMethodName,
                       PaymentMethodData.TenderType,
                       PaymentMethodData.MethodAmount,
                       CASE
                           WHEN COALESCE(TotalData.TotalAmount, 0) = 0 THEN 0
                           ELSE ROUND((PaymentMethodData.MethodAmount * 100.0) / TotalData.TotalAmount, 0)
                       END AS PaymentMethodPercent
                FROM PaymentMethodData
                CROSS JOIN TotalData
                ORDER BY PaymentMethodPercent DESC";

            var rows = new List<object>();

            IDataReader dr = null;

            try
            {
                dr = DB.ExecuteReader(sql, parameters.ToArray());

                while (dr != null && dr.Read())
                {
                    string paymentMethodName = "";

                    if (dr["PaymentMethodName"] != null && dr["PaymentMethodName"] != DBNull.Value)
                    {
                        paymentMethodName = dr["PaymentMethodName"].ToString();
                    }

                    if (string.IsNullOrEmpty(paymentMethodName))
                    {
                        string tenderType = "";

                        if (dr["TenderType"] != null && dr["TenderType"] != DBNull.Value)
                        {
                            tenderType = dr["TenderType"].ToString();
                        }

                        paymentMethodName = GetTenderTypeName(ctx , tenderType);
                    }

                    rows.Add(new
                    {
                        paymentMethodName = paymentMethodName,
                        methodAmount = Util.GetValueOfDecimal(dr["MethodAmount"]),
                        paymentMethodPercent = Util.GetValueOfDecimal(dr["PaymentMethodPercent"])
                    });
                }

                return Json(rows, JsonRequestBehavior.AllowGet);
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


        private static readonly Dictionary<string, (string MessageKey, string DefaultName)> TenderTypeNames =
            new Dictionary<string, (string MessageKey, string DefaultName)>
            {
                { "K", ("Cheque", "Cheque") },
                { "C", ("Card", "Card") },
                { "A", ("ACH", "ACH") },
                { "D", ("DirectDebit", "Direct Debit") },
                { "T", ("BankTransfer", "Bank Transfer") }
            };

        private string GetTenderTypeName(Ctx ctx, string tenderType)
        {
            if (TenderTypeNames.TryGetValue(tenderType, out var tenderTypeInfo))
            {
                return GetMsg(ctx, tenderTypeInfo.MessageKey, tenderTypeInfo.DefaultName);
            }

            return GetMsg(ctx, "Other", "Other");
        }

        private string GetMsg(Ctx ctx, string key, string fallback)
        {
            string msg = Msg.GetMsg(ctx, key);

            if (string.IsNullOrEmpty(msg) || (msg.StartsWith("[") && msg.EndsWith("]")))
            {
                return fallback;
            }

            return msg;
        }


        //private string GetTenderTypeName(string tenderType)
        //{
        //    if (tenderType == "K")
        //    {
        //        return "Cheque";
        //    }

        //    if (tenderType == "C")
        //    {
        //        return "Card";
        //    }

        //    if (tenderType == "A")
        //    {
        //        return "ACH";
        //    }

        //    if (tenderType == "D")
        //    {
        //        return "Direct Debit";
        //    }

        //    if (tenderType == "T")
        //    {
        //        return "Bank Transfer";
        //    }

        //    return "Other";
        //}
    }
}