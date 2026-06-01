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
    public class PaidThisMonthAPPaymentController : Controller
    {
        [AjaxAuthorizeAttribute]
        [AjaxSessionFilterAttribute]
        public JsonResult GetPaidThisMonth()
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
                DateTime today = DateTime.Today;
                DateTime dateFrom = new DateTime(today.Year, today.Month, 1);
                DateTime dateTo = dateFrom.AddMonths(1);

                string dateFromText = dateFrom.ToString("yyyy-MM-dd");
                string dateToText = dateTo.ToString("yyyy-MM-dd");

                string dateFilter = string.Empty;

                if (DB.IsOracle())
                {
                    dateFilter = @"
                        AND p.DateAcct >= TO_DATE('" + dateFromText + @"', 'YYYY-MM-DD')
                        AND p.DateAcct < TO_DATE('" + dateToText + @"', 'YYYY-MM-DD')
                    ";
                }
                else
                {
                    dateFilter = @"
                        AND p.DateAcct >= DATE '" + dateFromText + @"'
                        AND p.DateAcct < DATE '" + dateToText + @"'
                    ";
                }

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

                string paidThisMonthSql = @"
                    SELECT
                        ROUND(
                            COALESCE(
                                SUM(
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
                                    END
                                ),
                                0
                            ),
                            MAX(SchemaCurrency.StdPrecision)
                        ) AS PaidThisMonth,
                        MAX(SchemaCurrency.C_Currency_ID) AS C_Currency_ID,
                        MAX(SchemaCurrency.ISO_Code) AS CurrencyISO,
                        MAX(SchemaCurrency.Cur_Symbol) AS CurrencySymbol
                    FROM C_Payment p
                    INNER JOIN SchemaCurrency SchemaCurrency
                        ON SchemaCurrency.AD_Client_ID = p.AD_Client_ID
                    WHERE p.IsActive = 'Y'
                    AND p.IsReceipt = @IsReceipt
                    AND p.DocStatus IN ('CO', 'CL')
                " + dateFilter;

                paidThisMonthSql = MRole.GetDefault(ctx).AddAccessSQL(
                    paidThisMonthSql,
                    "p",
                    MRole.SQL_FULLYQUALIFIED,
                    MRole.SQL_RO
                );

                string sql = @"
                    WITH SchemaCurrency AS (
                        " + schemaCurrencySql + @"
                    )
                    " + paidThisMonthSql;

                List<SqlParameter> parameters = new List<SqlParameter>
                {
                    new SqlParameter("@IsReceipt", "N")
                };

                dr = DB.ExecuteReader(sql, parameters.ToArray());

                decimal paidThisMonth = 0;
                int cCurrencyId = 0;
                string currencyISO = string.Empty;
                string currencySymbol = string.Empty;

                if (dr.Read())
                {
                    paidThisMonth = Util.GetValueOfDecimal(dr["PaidThisMonth"]);
                    cCurrencyId = Util.GetValueOfInt(dr["C_Currency_ID"]);
                    currencyISO = Util.GetValueOfString(dr["CurrencyISO"]);
                    currencySymbol = Util.GetValueOfString(dr["CurrencySymbol"]);
                }

                return Json(new
                {
                    title = GetMsg(ctx, "VAS_PaidThisMonth", "Paid this month"),
                    badge = GetMsg(ctx, "VAS_Why", "WHY"),
                    description = GetMsg(ctx, "VAS_OutgoingPaymentsPostedSoFar", "Outgoing payments posted so far"),
                    paidThisMonth = paidThisMonth,
                    cCurrencyId = cCurrencyId,
                    currencyISO = currencyISO,
                    currencySymbol = currencySymbol,
                    dateFrom = dateFromText,
                    dateTo = dateTo.AddDays(-1).ToString("yyyy-MM-dd")
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

        private string GetMsg(Ctx ctx, string key, string fallback)
        {
            string msg = Msg.GetMsg(ctx, key);

            return !string.IsNullOrEmpty(msg) && msg != "[" + key + "]"
                ? msg
                : fallback;
        }
    }
}