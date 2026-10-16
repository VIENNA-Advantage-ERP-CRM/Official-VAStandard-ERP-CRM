using Newtonsoft.Json;
using System;
using System.Collections.Generic;
using System.Data;
using System.Globalization;
using System.Web.Mvc;
using VAdvantage.Classes;
using VAdvantage.DataBase;
using VAdvantage.Model;
using VAdvantage.Utility;
using VIS.Filters;

namespace VIS.Controllers
{
    /// <summary>
    /// Module Name : Transfer Receiving Inspection Widget (Material Transfer dashboard)
    /// Purpose     : Returns the quality inspection parameters captured on Material Transfer
    ///               confirmation lines (VA010_MoveConfParameters): parameter, acceptable
    ///               range, actual value, quantity to verify, date and remarks. A failed
    ///               numeric result is flagged but never blocks the confirmation.
    /// ID Prefix   : VAS_175_
    /// </summary>
    public class VAS_175_MaterialTransferReceivingInspectionController : Controller
    {
        private const int MaxRows = 300;

        [AjaxAuthorizeAttribute]
        [AjaxSessionFilterAttribute]
        public JsonResult GetInspections()
        {
            if (Session["ctx"] == null)
            {
                return Json(new { error = "Session Expired" }, JsonRequestBehavior.AllowGet);
            }

            Ctx ctx = Session["ctx"] as Ctx;

            string sql = @"
                SELECT mc.M_MovementConfirm_ID AS ConfirmID,
                       mc.DocumentNo           AS ConfirmNo,
                       mm.DocumentNo           AS TransferNo,
                       COALESCE(p.Name, N'')   AS ProductName,
                       COALESCE(qp.VA010_ParameterName, N'') AS ParameterName,
                       COALESCE(tl.VA010_ParameterValue, N'') AS ListValue,
                       cp.MinValue             AS MinValue,
                       cp.MaxValue             AS MaxValue,
                       cp.VA010_ActualValue    AS ActualValue,
                       cp.VA010_QuantityToVerify AS QtyToVerify,
                       cp.VA010_QAQCDate       AS InspectionDate,
                       cp.Remark               AS Remark
                FROM VA010_MoveConfParameters cp
                INNER JOIN M_MovementLineConfirm lc ON lc.M_MovementLineConfirm_ID = cp.M_MovementLineConfirm_ID
                INNER JOIN M_MovementConfirm mc ON mc.M_MovementConfirm_ID = lc.M_MovementConfirm_ID
                LEFT JOIN M_Movement mm ON mm.M_Movement_ID = mc.M_Movement_ID
                LEFT JOIN M_Product p ON p.M_Product_ID = cp.M_Product_ID
                LEFT JOIN VA010_QualityParameters qp ON qp.VA010_QualityParameters_ID = cp.VA010_QualityParameters_ID
                LEFT JOIN VA010_TestPrmtrList tl ON tl.VA010_TestPrmtrList_ID = cp.VA010_TestPrmtrList_ID
                WHERE cp.IsActive = 'Y'
                ORDER BY cp.VA010_MoveConfParameters_ID DESC";

            sql = MRole.GetDefault(ctx).AddAccessSQL(sql, "mc", MRole.SQL_FULLYQUALIFIED, MRole.SQL_RO);

            var rows = new List<object>();
            IDataReader dr = null;
            try
            {
                dr = DB.ExecuteReader(sql);
                while (dr != null && dr.Read() && rows.Count < MaxRows)
                {
                    string actual = Util.GetValueOfString(dr["ActualValue"]);
                    decimal min = Util.GetValueOfDecimal(dr["MinValue"]);
                    decimal max = Util.GetValueOfDecimal(dr["MaxValue"]);
                    string listValue = Util.GetValueOfString(dr["ListValue"]);

                    // Actual = typed actual value, else the list value picked for the parameter.
                    // Numeric range (Min/Max set + numeric actual) -> Pass / Fail.
                    // Any other recorded value -> Recorded. Nothing recorded yet -> Pending.
                    string shown = !string.IsNullOrEmpty(actual) ? actual : listValue;
                    string result = "Pending";
                    if (!string.IsNullOrEmpty(shown))
                    {
                        decimal a;
                        if ((min != 0 || max != 0) &&
                            decimal.TryParse(shown, NumberStyles.Any, CultureInfo.InvariantCulture, out a))
                        {
                            result = (a >= min && (max == 0 || a <= max)) ? "Pass" : "Fail";
                        }
                        else
                        {
                            result = "Recorded";
                        }
                    }

                    DateTime? dt = Util.GetValueOfDateTime(dr["InspectionDate"]);
                    rows.Add(new
                    {
                        ConfirmID = Util.GetValueOfInt(dr["ConfirmID"]),
                        ConfirmNo = Util.GetValueOfString(dr["ConfirmNo"]),
                        TransferNo = Util.GetValueOfString(dr["TransferNo"]),
                        ProductName = Util.GetValueOfString(dr["ProductName"]),
                        ParameterName = Util.GetValueOfString(dr["ParameterName"]),
                        MinValue = min,
                        MaxValue = max,
                        ActualValue = shown,
                        QtyToVerify = Util.GetValueOfDecimal(dr["QtyToVerify"]),
                        InspectionDate = dt.HasValue ? dt.Value.ToString("o") : "",
                        Remark = Util.GetValueOfString(dr["Remark"]),
                        Result = result
                    });
                }

                return Json(JsonConvert.SerializeObject(new { rows = rows }), JsonRequestBehavior.AllowGet);
            }
            catch (Exception ex)
            {
                return Json(new { error = ex.Message }, JsonRequestBehavior.AllowGet);
            }
            finally
            {
                if (dr != null) { dr.Close(); dr.Dispose(); }
            }
        }
    }
}
