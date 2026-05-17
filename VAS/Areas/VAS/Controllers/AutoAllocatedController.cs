using Newtonsoft.Json;
using System;
using System.Data;
using System.Web.Mvc;
using VAdvantage.Classes;
using VAdvantage.DataBase;
using VAdvantage.Model;
using VAdvantage.Utility;
using VIS.Filters;

namespace VIS.Controllers
{
    public class AutoAllocatedController : Controller
    {
        /// <summary>
        /// Returns percentage of AR receipts auto-allocated/matched to invoices.
        /// </summary>
        [AjaxAuthorizeAttribute]
        [AjaxSessionFilterAttribute]
        public JsonResult GetAutoAllocated()
        {
            if (Session["ctx"] == null)
            {
                return Json(new { error = Msg.GetMsg(Env.GetCtx(), "SessionExpired") ?? "Session Expired" }, JsonRequestBehavior.AllowGet);
            }

            Ctx ctx = Session["ctx"] as Ctx;


            string autoAllocatedSql = @"
                    SELECT COUNT(DISTINCT Payment.C_Payment_ID) AS TotalReceiptCount,
                           COUNT(DISTINCT CASE
                               WHEN AllocationHdr.C_AllocationHdr_ID IS NOT NULL
                               AND Invoice.C_Invoice_ID IS NOT NULL THEN Payment.C_Payment_ID
                               ELSE NULL
                           END) AS AutoAllocatedReceiptCount
                    FROM C_Payment Payment
                    LEFT OUTER JOIN C_AllocationLine AllocationLine ON (AllocationLine.C_Payment_ID=Payment.C_Payment_ID AND AllocationLine.IsActive='Y')
                    LEFT OUTER JOIN C_AllocationHdr AllocationHdr ON (AllocationLine.C_AllocationHdr_ID=AllocationHdr.C_AllocationHdr_ID AND AllocationHdr.IsActive='Y' AND AllocationHdr.DocStatus IN ('CO', 'CL') AND AllocationHdr.IsManual='N')
                    LEFT OUTER JOIN C_Invoice Invoice ON (AllocationLine.C_Invoice_ID=Invoice.C_Invoice_ID AND Invoice.IsSoTrx='Y' AND Invoice.IsActive='Y' AND Invoice.DocStatus IN ('CO', 'CL'))
                    WHERE Payment.IsReceipt='Y'
                    AND Payment.IsActive='Y'
                    AND Payment.DocStatus IN ('CO', 'CL')
                    AND Payment.Posted='Y'";

            autoAllocatedSql = MRole.GetDefault(ctx).AddAccessSQL(
                autoAllocatedSql,
                "Payment",
                MRole.SQL_FULLYQUALIFIED,
                MRole.SQL_RO
            );

            string sql = @"
                WITH AutoAllocated AS (
                    " + autoAllocatedSql + @"
                )
                SELECT AutoAllocated.TotalReceiptCount,
                       AutoAllocated.AutoAllocatedReceiptCount,
                       CASE
                           WHEN AutoAllocated.TotalReceiptCount=0 THEN 0
                           ELSE ROUND((AutoAllocated.AutoAllocatedReceiptCount*100.0)/AutoAllocated.TotalReceiptCount, 0)
                       END AS AutoAllocatedPercent
                FROM AutoAllocated";

            decimal autoAllocatedPercent = 0;
            int totalReceiptCount = 0;
            int autoAllocatedReceiptCount = 0;

            IDataReader dr = null;
            try
            {
                dr = DB.ExecuteReader(sql);

                if (dr != null && dr.Read())
                {
                    totalReceiptCount = Util.GetValueOfInt(dr["TotalReceiptCount"]);
                    autoAllocatedReceiptCount = Util.GetValueOfInt(dr["AutoAllocatedReceiptCount"]);
                    autoAllocatedPercent = Util.GetValueOfDecimal(dr["AutoAllocatedPercent"]);
                }
            }
            finally
            {
                if (dr != null)
                {
                    dr.Close();
                }
            }

            var result = new
            {
                totalReceiptCount = totalReceiptCount,
                autoAllocatedReceiptCount = autoAllocatedReceiptCount,
                autoAllocatedPercent = autoAllocatedPercent
            };

            return Json(JsonConvert.SerializeObject(result), JsonRequestBehavior.AllowGet);
        }
    }
}