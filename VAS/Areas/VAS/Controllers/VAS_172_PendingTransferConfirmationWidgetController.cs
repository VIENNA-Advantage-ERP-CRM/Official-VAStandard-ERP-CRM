using Newtonsoft.Json;
using System;
using System.Collections.Generic;
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
    /// Module Name : Pending Transfer Confirmation Widget (Material Transfer Dashboard)
    /// Purpose     : Operational work-queue of inbound transfers awaiting destination
    ///               confirmation (M_Movement with DocStatus IN ('IP', 'WC', 'DP', 'UC')).
    /// ID Prefix   : VAS_172_
    /// </summary>
    public class VAS_172_PendingTransferConfirmationWidgetController : Controller
    {
        private const string PendingStatusInList = "'IP', 'WC', 'DP', 'UC'";

        [AjaxAuthorizeAttribute]
        [AjaxSessionFilterAttribute]
        public JsonResult GetPendingConfirmations()
        {
            if (Session["ctx"] == null)
                return Json(new { error = "Session Expired" }, JsonRequestBehavior.AllowGet);

            Ctx ctx = Session["ctx"] as Ctx;

            string sql = @"
                SELECT
                    mm.M_Movement_ID          AS TransferID,
                    mm.DocumentNo             AS TransferNo,
                    mm.DocStatus              AS ConfirmationStatus,
                    mm.MovementDate           AS MovementDate
                FROM M_Movement mm
                WHERE mm.IsActive = 'Y'
                  AND mm.DocStatus IN (" + PendingStatusInList + @")
                ORDER BY mm.MovementDate ASC";

            sql = MRole.GetDefault(ctx).AddAccessSQL(sql, "mm", MRole.SQL_FULLYQUALIFIED, MRole.SQL_RO);

            var headers = new List<TransferHeader>();
            var records = new List<object>();
            IDataReader dr = null;
            try
            {
                dr = DB.ExecuteReader(sql);
                while (dr != null && dr.Read())
                {
                    headers.Add(new TransferHeader
                    {
                        TransferID = Util.GetValueOfInt(dr["TransferID"]),
                        TransferNo = Util.GetValueOfString(dr["TransferNo"]),
                        ConfirmationStatus = "Pending",
                        MovementDate = Util.GetValueOfDateTime(dr["MovementDate"]).GetValueOrDefault().ToString("o")
                    });
                }
                if (dr != null) { dr.Close(); dr.Dispose(); dr = null; }

                foreach (var h in headers)
                {
                    GetRouteDetail(h);
                    var lines = GetLines(ctx, h.TransferID);
                    records.Add(new
                    {
                        TransferID = h.TransferID,
                        TransferNo = h.TransferNo,
                        ConfirmationStatus = h.ConfirmationStatus,
                        MovementDate = h.MovementDate,
                        SourceWarehouse = h.SourceWarehouse,
                        DestWarehouse = h.DestWarehouse,
                        SourceLocator = h.SourceLocator,
                        DestLocator = h.DestLocator,
                        LineCount = lines.Count,
                        Lines = lines
                    });
                }

                return Json(JsonConvert.SerializeObject(new { records = records }), JsonRequestBehavior.AllowGet);
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

        private class TransferHeader
        {
            public int    TransferID;
            public string TransferNo;
            public string ConfirmationStatus;
            public string MovementDate;
            public string SourceWarehouse;
            public string DestWarehouse;
            public string SourceLocator;
            public string DestLocator;
        }

        private static void GetRouteDetail(TransferHeader h)
        {
            string sql = @"
                SELECT
                    wSrc.Name  AS SourceWarehouse,
                    wDst.Name  AS DestWarehouse,
                    COALESCE(lSrc.Value, lSrc.LocatorCombination) AS SourceLocator,
                    COALESCE(lDst.Value, lDst.LocatorCombination) AS DestLocator
                FROM M_MovementLine ml
                LEFT JOIN M_Locator lSrc ON lSrc.M_Locator_ID = ml.M_Locator_ID
                LEFT JOIN M_Warehouse wSrc ON wSrc.M_Warehouse_ID = lSrc.M_Warehouse_ID
                LEFT JOIN M_Locator lDst ON lDst.M_Locator_ID = ml.M_LocatorTo_ID
                LEFT JOIN M_Warehouse wDst ON wDst.M_Warehouse_ID = lDst.M_Warehouse_ID
                WHERE ml.M_Movement_ID = " + h.TransferID + @"
                  AND ml.IsActive = 'Y'
                ORDER BY ml.Line ASC, ml.M_MovementLine_ID ASC";

            IDataReader dr = null;
            try
            {
                dr = DB.ExecuteReader(sql);
                if (dr != null && dr.Read())
                {
                    h.SourceWarehouse = Util.GetValueOfString(dr["SourceWarehouse"]);
                    h.DestWarehouse   = Util.GetValueOfString(dr["DestWarehouse"]);
                    h.SourceLocator   = Util.GetValueOfString(dr["SourceLocator"]);
                    h.DestLocator     = Util.GetValueOfString(dr["DestLocator"]);
                }
            }
            finally
            {
                if (dr != null) { dr.Close(); dr.Dispose(); }
            }
        }

        private List<object> GetLines(Ctx ctx, int movementId)
        {
            var lines = new List<object>();
            string sql = @"
                SELECT
                    ml.M_MovementLine_ID        AS LineID,
                    ml.Line                     AS LineNo,
                    p.Name                      AS ItemName,
                    COALESCE(u.UOMSymbol, u.Name) AS UOM,
                    COALESCE(asi.Description, '') AS AttributeSetInstance,
                    COALESCE(lDst.Value, lDst.LocatorCombination, '') AS ScrapLocator,
                    COALESCE(ml.MovementQty, 0) AS TargetQty,
                    ml.ConfirmedQty             AS ConfirmedQty,
                    COALESCE(ml.ScrappedQty, 0) AS ScrappedQty,
                    ml.Description              AS Description
                FROM M_MovementLine ml
                LEFT JOIN M_Product p ON p.M_Product_ID = ml.M_Product_ID
                LEFT JOIN C_UOM u ON u.C_UOM_ID = p.C_UOM_ID
                LEFT JOIN M_AttributeSetInstance asi ON asi.M_AttributeSetInstance_ID = ml.M_AttributeSetInstance_ID
                LEFT JOIN M_Locator lDst ON lDst.M_Locator_ID = ml.M_LocatorTo_ID
                WHERE ml.M_Movement_ID = " + movementId + @"
                  AND ml.IsActive = 'Y'
                ORDER BY ml.Line ASC";

            IDataReader dr = null;
            try
            {
                dr = DB.ExecuteReader(sql);
                while (dr != null && dr.Read())
                {
                    lines.Add(new
                    {
                        LineID = Util.GetValueOfInt(dr["LineID"]),
                        LineNo = Util.GetValueOfInt(dr["LineNo"]),
                        ItemName = Util.GetValueOfString(dr["ItemName"]),
                        UOM = Util.GetValueOfString(dr["UOM"]),
                        AttributeSetInstance = Util.GetValueOfString(dr["AttributeSetInstance"]),
                        ScrapLocator = Util.GetValueOfString(dr["ScrapLocator"]),
                        TargetQty = Util.GetValueOfDecimal(dr["TargetQty"]),
                        ConfirmedQty = dr["ConfirmedQty"] == DBNull.Value ? (decimal?)null : Util.GetValueOfDecimal(dr["ConfirmedQty"]),
                        ScrappedQty = Util.GetValueOfDecimal(dr["ScrappedQty"]),
                        Description = Util.GetValueOfString(dr["Description"])
                    });
                }
            }
            finally
            {
                if (dr != null) { dr.Close(); dr.Dispose(); }
            }
            return lines;
        }

        [HttpPost]
        [AjaxAuthorizeAttribute]
        [AjaxSessionFilterAttribute]
        public JsonResult SaveConfirmationLine()
        {
            try
            {
                string json = "";
                if (Request.InputStream != null)
                {
                    Request.InputStream.Position = 0;
                    using (var reader = new System.IO.StreamReader(Request.InputStream))
                    {
                        json = reader.ReadToEnd();
                    }
                }
                if (string.IsNullOrEmpty(json) && Request.Form.Keys.Count > 0)
                {
                    json = Request.Form[0];
                }

                var data = JsonConvert.DeserializeObject<Dictionary<string, object>>(json);

                int transferId = Util.GetValueOfInt(data.ContainsKey("TransferID") ? data["TransferID"] : 0);
                int lineNo = Util.GetValueOfInt(data.ContainsKey("LineNo") ? data["LineNo"] : 0);
                int lineId = Util.GetValueOfInt(data.ContainsKey("LineID") ? data["LineID"] : 0);
                decimal confirmedQty = Util.GetValueOfDecimal(data.ContainsKey("ConfirmedQty") ? data["ConfirmedQty"] : 0);
                decimal scrappedQty = Util.GetValueOfDecimal(data.ContainsKey("ScrappedQty") ? data["ScrappedQty"] : 0);
                string description = data.ContainsKey("Description") ? Util.GetValueOfString(data["Description"]) : "";

                string sql = "";
                if (lineId > 0)
                {
                    sql = @"UPDATE M_MovementLine SET
                        ConfirmedQty = " + confirmedQty.ToString(System.Globalization.CultureInfo.InvariantCulture) + @",
                        ScrappedQty  = " + scrappedQty.ToString(System.Globalization.CultureInfo.InvariantCulture) + @",
                        Description  = '" + description.Replace("'", "''") + @"'
                        WHERE M_MovementLine_ID = " + lineId;
                }
                else
                {
                    sql = @"UPDATE M_MovementLine SET
                        ConfirmedQty = " + confirmedQty.ToString(System.Globalization.CultureInfo.InvariantCulture) + @",
                        ScrappedQty  = " + scrappedQty.ToString(System.Globalization.CultureInfo.InvariantCulture) + @",
                        Description  = '" + description.Replace("'", "''") + @"'
                        WHERE M_Movement_ID = " + transferId + " AND Line = " + lineNo;
                }

                DB.ExecuteQuery(sql);
                return Json(new { success = true }, JsonRequestBehavior.AllowGet);
            }
            catch (Exception ex)
            {
                return Json(new { error = ex.Message }, JsonRequestBehavior.AllowGet);
            }
        }

        [HttpPost]
        [AjaxAuthorizeAttribute]
        [AjaxSessionFilterAttribute]
        public JsonResult CompleteTransfer()
        {
            try
            {
                string json = "";
                if (Request.InputStream != null)
                {
                    Request.InputStream.Position = 0;
                    using (var reader = new System.IO.StreamReader(Request.InputStream))
                    {
                        json = reader.ReadToEnd();
                    }
                }
                if (string.IsNullOrEmpty(json) && Request.Form.Keys.Count > 0)
                {
                    json = Request.Form[0];
                }

                var data = JsonConvert.DeserializeObject<Dictionary<string, object>>(json);
                int transferId = Util.GetValueOfInt(data.ContainsKey("TransferID") ? data["TransferID"] : 0);

                if (transferId > 0)
                {
                    string sql = "UPDATE M_Movement SET DocStatus = 'CO', Processed = 'Y' WHERE M_Movement_ID = " + transferId;
                    DB.ExecuteQuery(sql);
                }

                return Json(new { success = true }, JsonRequestBehavior.AllowGet);
            }
            catch (Exception ex)
            {
                return Json(new { error = ex.Message }, JsonRequestBehavior.AllowGet);
            }
        }
    }
}
