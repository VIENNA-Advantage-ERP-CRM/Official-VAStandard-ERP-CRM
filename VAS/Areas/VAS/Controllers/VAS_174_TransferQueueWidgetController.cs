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
    /// Module Name : Transfer Queue Widget (Material Transfer Dashboard)
    /// Purpose     : Returns all non-completed (open) material transfer records with
    ///               embedded line details. Client-side JS handles month/year filtering.
    ///               Excludes DocStatus = 'CO' (Completed) so only actionable documents
    ///               appear in the queue.
    /// ID Prefix   : VAS_174_
    /// </summary>
    public class VAS_174_TransferQueueWidgetController : Controller
    {
        [AjaxAuthorizeAttribute]
        [AjaxSessionFilterAttribute]
        public JsonResult GetTransferQueue()
        {
            if (Session["ctx"] == null)
                return Json(new { error = "Session Expired" }, JsonRequestBehavior.AllowGet);

            Ctx ctx = Session["ctx"] as Ctx;

            string sql = @"
                SELECT
                    mm.M_Movement_ID        AS MovementID,
                    mm.DocumentNo           AS DocumentNo,
                    mm.DocStatus            AS DocStatus,
                    mm.MovementDate         AS MovementDate,
                    COALESCE(dt.Name, N'')   AS DocTypeName,
                    COALESCE(cu.Name, N'')   AS CreatedBy,
                    COALESCE(ru.Name, N'')   AS RequestedBy
                FROM M_Movement mm
                LEFT JOIN C_DocType dt ON dt.C_DocType_ID = mm.C_DocType_ID
                LEFT JOIN AD_User cu   ON cu.AD_User_ID = mm.CreatedBy
                LEFT JOIN AD_User ru   ON ru.AD_User_ID = mm.UpdatedBy
                WHERE mm.IsActive = 'Y'
                  AND mm.DocStatus NOT IN ('CO')
                ORDER BY mm.MovementDate DESC";

            sql = MRole.GetDefault(ctx).AddAccessSQL(sql, "mm", MRole.SQL_FULLYQUALIFIED, MRole.SQL_RO);

            var headers = new List<QueueHeader>();
            var records = new List<object>();
            IDataReader dr = null;
            try
            {
                dr = DB.ExecuteReader(sql);
                while (dr != null && dr.Read())
                {
                    headers.Add(new QueueHeader
                    {
                        MovementID   = Util.GetValueOfInt(dr["MovementID"]),
                        DocumentNo   = Util.GetValueOfString(dr["DocumentNo"]),
                        DocStatus    = Util.GetValueOfString(dr["DocStatus"]),
                        MovementDate = Util.GetValueOfDateTime(dr["MovementDate"]).GetValueOrDefault().ToString("o"),
                        DocTypeName  = Util.GetValueOfString(dr["DocTypeName"]),
                        CreatedBy    = Util.GetValueOfString(dr["CreatedBy"]),
                        RequestedBy  = Util.GetValueOfString(dr["RequestedBy"])
                    });
                }
                if (dr != null) { dr.Close(); dr.Dispose(); dr = null; }

                foreach (var h in headers)
                {
                    GetRouteDetail(h);
                    var lines = GetMovementLines(h.MovementID);
                    records.Add(new
                    {
                        MovementID   = h.MovementID,
                        DocumentNo   = h.DocumentNo,
                        DocStatus    = h.DocStatus,
                        MovementDate = h.MovementDate,
                        DocTypeName  = h.DocTypeName,
                        FromWarehouse = h.FromWarehouse,
                        ToWarehouse  = h.ToWarehouse,
                        FromLocator  = h.FromLocator,
                        ToLocator    = h.ToLocator,
                        CreatedBy    = h.CreatedBy,
                        RequestedBy  = h.RequestedBy,
                        LineCount    = lines.Count,
                        Lines        = lines
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

        private class QueueHeader
        {
            public int    MovementID;
            public string DocumentNo;
            public string DocStatus;
            public string MovementDate;
            public string DocTypeName;
            public string CreatedBy;
            public string RequestedBy;
            public string FromWarehouse;
            public string ToWarehouse;
            public string FromLocator;
            public string ToLocator;
        }

        private static void GetRouteDetail(QueueHeader h)
        {
            string sql = @"
                SELECT
                    wSrc.Name  AS FromWarehouse,
                    wDst.Name  AS ToWarehouse,
                    lSrc.Value AS FromLocator,
                    lDst.Value AS ToLocator
                FROM M_MovementLine ml
                LEFT JOIN M_Locator lSrc ON lSrc.M_Locator_ID = ml.M_Locator_ID
                LEFT JOIN M_Warehouse wSrc ON wSrc.M_Warehouse_ID = lSrc.M_Warehouse_ID
                LEFT JOIN M_Locator lDst ON lDst.M_Locator_ID = ml.M_LocatorTo_ID
                LEFT JOIN M_Warehouse wDst ON wDst.M_Warehouse_ID = lDst.M_Warehouse_ID
                WHERE ml.M_Movement_ID = " + h.MovementID + @"
                  AND ml.IsActive = 'Y'
                  AND ml.Line = (
                      SELECT MIN(mlMin.Line)
                      FROM M_MovementLine mlMin
                      WHERE mlMin.M_Movement_ID = ml.M_Movement_ID
                        AND mlMin.IsActive = 'Y'
                  )";

            IDataReader dr = null;
            try
            {
                dr = DB.ExecuteReader(sql);
                if (dr != null && dr.Read())
                {
                    h.FromWarehouse = Util.GetValueOfString(dr["FromWarehouse"]);
                    h.ToWarehouse   = Util.GetValueOfString(dr["ToWarehouse"]);
                    h.FromLocator   = Util.GetValueOfString(dr["FromLocator"]);
                    h.ToLocator     = Util.GetValueOfString(dr["ToLocator"]);
                }
            }
            finally
            {
                if (dr != null) { dr.Close(); dr.Dispose(); }
            }
        }

        private List<object> GetMovementLines(int movementId)
        {
            var lines = new List<object>();
            string sql = @"
                SELECT
                    ml.Line             AS LineNo,
                    p.Name              AS ItemName,
                    u.UOMSymbol         AS UOM,
                    ml.MovementQty      AS Sent,
                    ml.ConfirmedQty     AS Received,
                    ai.Description      AS AttributeSetInstance
                FROM M_MovementLine ml
                LEFT JOIN M_Product p              ON p.M_Product_ID = ml.M_Product_ID
                LEFT JOIN C_UOM u                  ON u.C_UOM_ID = p.C_UOM_ID
                LEFT JOIN M_AttributeSetInstance ai ON ai.M_AttributeSetInstance_ID = ml.M_AttributeSetInstance_ID
                WHERE ml.M_Movement_ID = " + movementId + @"
                  AND ml.IsActive = 'Y'
                ORDER BY ml.Line ASC";

            IDataReader dr = null;
            try
            {
                dr = DB.ExecuteReader(sql);
                while (dr != null && dr.Read())
                {
                    decimal sent = Util.GetValueOfDecimal(dr["Sent"]);
                    decimal? received = dr["Received"] == DBNull.Value
                        ? (decimal?)null
                        : Util.GetValueOfDecimal(dr["Received"]);

                    string lineStatus = "Pending";
                    if (received.HasValue)
                    {
                        if (received.Value == sent)       { lineStatus = "OK"; }
                        else if (received.Value < sent)   { lineStatus = "Short"; }
                        else                              { lineStatus = "OK"; }
                    }

                    lines.Add(new
                    {
                        LineNo               = Util.GetValueOfInt(dr["LineNo"]),
                        ItemName             = Util.GetValueOfString(dr["ItemName"]),
                        UOM                  = Util.GetValueOfString(dr["UOM"]),
                        AttributeSetInstance = Util.GetValueOfString(dr["AttributeSetInstance"]),
                        Sent                 = sent,
                        Received             = (object)received ?? DBNull.Value,
                        LineStatus           = lineStatus
                    });
                }
            }
            finally
            {
                if (dr != null) { dr.Close(); dr.Dispose(); }
            }
            return lines;
        }
    }
}
