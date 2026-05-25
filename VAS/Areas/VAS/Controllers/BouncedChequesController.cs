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
    public class BouncedChequesController : Controller
    {
        /// <summary>
        /// Returns count of bounced cheques from AR receipts.
        /// </summary>
        [AjaxAuthorizeAttribute]
        [AjaxSessionFilterAttribute]
        public JsonResult GetBouncedCheques()
        {
            if (Session["ctx"] == null)
            {
                return Json(new { error = Msg.GetMsg(Env.GetCtx(), "SessionExpired") ?? "Session Expired" }, JsonRequestBehavior.AllowGet);
            }

            Ctx ctx = Session["ctx"] as Ctx;

            string bouncedChequesSql = @"
                SELECT COUNT(DISTINCT Payment.C_Payment_ID) AS BouncedChequeCount
                FROM C_Payment Payment
                WHERE Payment.IsReceipt='Y'
                AND Payment.IsActive='Y'
                AND Payment.TenderType='K'
                AND (
                    Payment.VA009_IsCancelled='Y'
                    OR Payment.IsReversal='Y'
                    OR Payment.ReversalDoc_ID IS NOT NULL
                    OR Payment.DocStatus IN ('VO', 'RE')
                )";

            bouncedChequesSql = MRole.GetDefault(ctx).AddAccessSQL(
                bouncedChequesSql,
                "Payment",
                MRole.SQL_FULLYQUALIFIED,
                MRole.SQL_RO
            );

            string sql = @"
                WITH BouncedCheques AS (
                    " + bouncedChequesSql + @"
                )
                SELECT BouncedCheques.BouncedChequeCount
                FROM BouncedCheques";

            int bouncedChequeCount = 0;

            IDataReader dr = null;
            try
            {
                dr = DB.ExecuteReader(sql);

                if (dr != null && dr.Read())
                {
                    bouncedChequeCount = Util.GetValueOfInt(dr["BouncedChequeCount"]);
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
                bouncedChequeCount = bouncedChequeCount
            };

            return Json(JsonConvert.SerializeObject(result), JsonRequestBehavior.AllowGet);
        }
    }
}