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
    /// <summary>
    /// Module Name : Pending Inspection (Material Receipt / GRN dashboard KPI)
    /// Purpose     : KPI = COUNT(DISTINCT M_InOutLineConfirm) of vendor-GRN receipt
    ///               confirmation lines that are flagged for quality check
    ///               (VA010_QualCheckMArk = 'Y') but whose QA actual value is still
    ///               empty (no VA010_ShipConfParameters result yet) - i.e. lines
    ///               still on QA hold / awaiting inspection. Read-only.
    ///               MRole is applied to the primary fetched table
    ///               (M_InOutLineConfirm); the other tables are join/filter only.
    /// Chronological development:
    ///   &lt;EmpCode&gt;   2026-06-18 Created
    /// </summary>
    public class VAS_084_PendingInspectionWidgetController : Controller
    {
        /// <summary>
        /// KPI tile data: count of GRN confirmation lines awaiting QA inspection.
        /// </summary>
        /// <returns>JSON { pendingInspection }.</returns>
        [AjaxAuthorizeAttribute]
        [AjaxSessionFilterAttribute]
        public JsonResult GetPendingInspection()
        {
            if (Session["ctx"] == null)
            {
                return Json(new
                {
                    error = Msg.GetMsg(Env.GetCtx(), "SessionExpired") ?? "Session Expired"
                }, JsonRequestBehavior.AllowGet);
            }

            Ctx ctx = Session["ctx"] as Ctx;

            string sql = @"
                SELECT COUNT(DISTINCT LineConfirm.M_InOutLineConfirm_ID) AS Pending_Inspection_Count
                FROM M_InOutLineConfirm LineConfirm
                INNER JOIN M_InOutConfirm Confirm ON (Confirm.M_InOutConfirm_ID=LineConfirm.M_InOutConfirm_ID)
                INNER JOIN M_InOutLine InOutLine ON (InOutLine.M_InOutLine_ID=LineConfirm.M_InOutLine_ID)
                INNER JOIN M_InOut InOut ON (InOut.M_InOut_ID=InOutLine.M_InOut_ID)
                LEFT OUTER JOIN VA010_ShipConfParameters QAParam ON (QAParam.M_InOutLineConfirm_ID=LineConfirm.M_InOutLineConfirm_ID AND QAParam.IsActive='Y')
                WHERE LineConfirm.IsActive='Y'
                  AND Confirm.IsActive='Y'
                  AND InOutLine.IsActive='Y'
                  AND InOut.IsActive='Y'
                  AND InOut.IsSOTrx='N'
                  AND InOut.MovementType='V+'
                  AND LineConfirm.VA010_QualCheckMArk='Y'
                  AND (QAParam.VA010_ActualValue IS NULL OR QAParam.VA010_ActualValue='')";

            /* MRole supplies tenant + organization access. Applied only to the
               primary fetched table (M_InOutLineConfirm); the joined Confirm /
               InOutLine / InOut / QAParam tables are filter/comparison sources
               (shared Prompt_Instructions CTE/self-join rule). */
            sql = MRole.GetDefault(ctx).AddAccessSQL(
                sql,
                "LineConfirm",
                MRole.SQL_FULLYQUALIFIED,
                MRole.SQL_RO
            );

            IDataReader dr = null;

            try
            {
                int pendingInspection = 0;

                dr = DB.ExecuteReader(sql);
                if (dr != null && dr.Read())
                {
                    pendingInspection = Util.GetValueOfInt(dr["Pending_Inspection_Count"]);
                }

                var result = new
                {
                    pendingInspection = pendingInspection
                };

                return Json(JsonConvert.SerializeObject(result), JsonRequestBehavior.AllowGet);
            }
            catch (Exception ex)
            {
                return Json(new { error = ex.Message }, JsonRequestBehavior.AllowGet);
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
    }
}
