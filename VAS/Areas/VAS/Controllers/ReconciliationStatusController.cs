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
    public class ReconciliationStatusController : Controller
    {
        [AjaxAuthorizeAttribute]
        [AjaxSessionFilterAttribute]
        public JsonResult GetReconciliationStatus()
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

                string sql = @"
                    SELECT
                        COUNT(1) AS TotalPayments,
                        SUM(
                            CASE
                                WHEN COALESCE(p.IsReconciled,'N')='Y'
                                AND EXISTS (
                                    SELECT 1
                                    FROM C_AllocationLine al
                                    INNER JOIN C_AllocationHdr ah ON (al.C_AllocationHdr_ID=ah.C_AllocationHdr_ID)
                                    INNER JOIN C_Invoice inv ON (al.C_Invoice_ID=inv.C_Invoice_ID)
                                    WHERE al.C_Payment_ID=p.C_Payment_ID
                                    AND ah.IsActive='Y'
                                    AND ah.DocStatus IN ('CO', 'CL')
                                    AND inv.IsActive='Y'
                                    AND inv.IsSOTrx='N'
                                )
                                THEN 1
                                ELSE 0
                            END
                        ) AS MatchedPayments
                    FROM C_Payment p
                    WHERE p.IsActive='Y'
                    AND p.IsReceipt=@IsReceipt
                    AND p.DocStatus IN ('CO', 'CL')
                    AND p.DateAcct>=@DateFrom
                    AND p.DateAcct<@DateTo
                ";

                sql = MRole.GetDefault(ctx).AddAccessSQL(sql, "p", MRole.SQL_FULLYQUALIFIED, MRole.SQL_RO);

                List<SqlParameter> parameters = new List<SqlParameter>
                {
                    new SqlParameter("@IsReceipt", "N"),
                    new SqlParameter("@DateFrom", dateFrom),
                    new SqlParameter("@DateTo", dateTo)
                };

                dr = DB.ExecuteReader(sql, parameters.ToArray());

                int totalPayments = 0;
                int matchedPayments = 0;
                int manualMatchCount = 0;
                decimal matchedPercentage = 0;

                if (dr.Read())
                {
                    totalPayments = Util.GetValueOfInt(dr["TotalPayments"]);
                    matchedPayments = Util.GetValueOfInt(dr["MatchedPayments"]);
                }

                if (totalPayments > 0)
                {
                    matchedPercentage = decimal.Round((matchedPayments * 100M) / totalPayments, 2);
                    manualMatchCount = totalPayments - matchedPayments;
                }

                return Json(new
                {
                    title = GetMsg(ctx, "VAS_ReconciliationStatus", "Reconciliation status"),
                    subTitle = GetMsg(ctx, "VAS_MatchedToBillsBank", "Matched to bills + bank"),
                    matchedLabel = GetMsg(ctx, "VAS_Matched", "Matched"),
                    matchedPayments = matchedPayments,
                    totalPayments = totalPayments,
                    manualMatchCount = manualMatchCount,
                    matchedPercentage = matchedPercentage,
                    dateFrom = dateFrom,
                    dateTo = dateTo.AddDays(-1)
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