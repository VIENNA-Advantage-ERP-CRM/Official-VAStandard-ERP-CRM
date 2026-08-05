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
    /// Module Name : Transfers by Warehouse Widget (Material Transfer Dashboard)
    /// Purpose     : Returns per-warehouse aggregated transfer metrics (total quantity
    ///               moved, movement count, share %) for a given month/year, together
    ///               with the per-transfer detail rows.
    ///               Only posted/completed movements (DocStatus IN ('CO','CL')) are counted.
    /// ID Prefix   : VAS_173_
    /// </summary>
    public class VAS_173_TransfersByWarehouseWidgetController : Controller
    {
        [AjaxAuthorizeAttribute]
        [AjaxSessionFilterAttribute]
        public JsonResult GetWarehouseTransfers(int month, int year)
        {
            if (Session["ctx"] == null)
                return Json(new { error = "Session Expired" }, JsonRequestBehavior.AllowGet);

            Ctx ctx = Session["ctx"] as Ctx;

            string movSql = @"
                SELECT
                    mm.M_Movement_ID      AS MovementID,
                    mm.DocumentNo         AS DocumentNo,
                    mm.MovementDate       AS MovementDate,
                    COALESCE(u.Name, N'')   AS CreatedBy,
                    COALESCE(usr.Name, N'') AS RequestedBy
                FROM M_Movement mm
                LEFT JOIN AD_User u   ON u.AD_User_ID = mm.CreatedBy
                LEFT JOIN AD_User usr ON usr.AD_User_ID = mm.UpdatedBy
                WHERE mm.IsActive = 'Y'
                  AND mm.DocStatus IN ('CO', 'CL')
                  AND EXTRACT(MONTH FROM mm.MovementDate) = " + month + @"
                  AND EXTRACT(YEAR  FROM mm.MovementDate) = " + year;

            movSql = MRole.GetDefault(ctx).AddAccessSQL(movSql, "mm", MRole.SQL_FULLYQUALIFIED, MRole.SQL_RO);

            var movements = new List<MovementRow>();
            IDataReader dr = null;
            try
            {
                dr = DB.ExecuteReader(movSql);
                while (dr != null && dr.Read())
                {
                    movements.Add(new MovementRow
                    {
                        MovementID     = Util.GetValueOfInt(dr["MovementID"]),
                        DocumentNo     = Util.GetValueOfString(dr["DocumentNo"]),
                        MovementDate   = Util.GetValueOfDateTime(dr["MovementDate"]).GetValueOrDefault(),
                        DoneBy         = Util.GetValueOfString(dr["CreatedBy"]),
                        RequestedBy    = Util.GetValueOfString(dr["RequestedBy"])
                    });
                }
                if (dr != null) { dr.Close(); dr.Dispose(); dr = null; }

                foreach (var m in movements)
                {
                    GetMovementLineDetail(m);
                }
            }
            catch (Exception ex)
            {
                return Json(new { error = ex.Message }, JsonRequestBehavior.AllowGet);
            }
            finally
            {
                if (dr != null) { dr.Close(); dr.Dispose(); }
            }

            var warehouseMap = new Dictionary<int, WarehouseAgg>();

            foreach (var m in movements)
            {
                if (m.SrcWarehouseID > 0)
                {
                    AggregateWarehouse(warehouseMap, m.SrcWarehouseID, m.SrcWarehouse, m);
                }
                if (m.DstWarehouseID > 0 && m.DstWarehouseID != m.SrcWarehouseID)
                {
                    AggregateWarehouse(warehouseMap, m.DstWarehouseID, m.DstWarehouse, m);
                }
            }

            decimal grandTotal = 0;
            decimal maxQty = 0;
            foreach (var agg in warehouseMap.Values)
            {
                grandTotal += agg.TotalQty;
                if (agg.TotalQty > maxQty) { maxQty = agg.TotalQty; }
            }

            var warehouses = new List<object>();
            foreach (var agg in warehouseMap.Values)
            {
                decimal share = grandTotal > 0 ? Math.Round(agg.TotalQty / grandTotal * 100, 1) : 0;
                int barFill = maxQty > 0 ? (int)Math.Round(agg.TotalQty / maxQty * 100) : 0;

                var transfers = new List<object>();
                foreach (var m in agg.Movements)
                {
                    transfers.Add(new
                    {
                        FromWH       = m.SrcWarehouse,
                        ToWH         = m.DstWarehouse,
                        FromLocator  = m.FromLocator,
                        ToLocator    = m.ToLocator,
                        MoveDate     = m.MovementDate.ToString("o"),
                        Products     = m.ProductCount + (m.ProductCount == 1 ? " product" : " products"),
                        Qty          = m.TotalQty,
                        DoneBy       = m.DoneBy,
                        RequestedBy  = m.RequestedBy
                    });
                }

                warehouses.Add(new
                {
                    WarehouseID   = agg.WarehouseID,
                    WarehouseName = agg.WarehouseName,
                    TotalQty      = agg.TotalQty,
                    Moves         = agg.Moves,
                    Share         = share,
                    BarFill       = barFill,
                    Transfers     = transfers
                });
            }

            return Json(JsonConvert.SerializeObject(new { warehouses = warehouses }), JsonRequestBehavior.AllowGet);
        }

        private static void GetMovementLineDetail(MovementRow m)
        {
            string sql = @"
                SELECT
                    (
                        SELECT COALESCE(SUM(mlq.MovementQty), 0)
                        FROM M_MovementLine mlq
                        WHERE mlq.M_Movement_ID = ml.M_Movement_ID
                          AND mlq.IsActive = 'Y'
                    )                    AS TotalLineQty,
                    (
                        SELECT COUNT(DISTINCT mlp.M_Product_ID)
                        FROM M_MovementLine mlp
                        WHERE mlp.M_Movement_ID = ml.M_Movement_ID
                          AND mlp.IsActive = 'Y'
                    )                    AS ProductCount,
                    wSrc.M_Warehouse_ID  AS SrcWarehouseID,
                    wSrc.Name            AS SrcWarehouseName,
                    wDst.M_Warehouse_ID  AS DstWarehouseID,
                    wDst.Name            AS DstWarehouseName,
                    lSrc.Value           AS FromLocator,
                    lDst.Value           AS ToLocator
                FROM M_MovementLine ml
                LEFT JOIN M_Locator lSrc ON lSrc.M_Locator_ID = ml.M_Locator_ID
                LEFT JOIN M_Warehouse wSrc ON wSrc.M_Warehouse_ID = lSrc.M_Warehouse_ID
                LEFT JOIN M_Locator lDst ON lDst.M_Locator_ID = ml.M_LocatorTo_ID
                LEFT JOIN M_Warehouse wDst ON wDst.M_Warehouse_ID = lDst.M_Warehouse_ID
                WHERE ml.M_Movement_ID = " + m.MovementID + @"
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
                    m.TotalQty       = Util.GetValueOfDecimal(dr["TotalLineQty"]);
                    m.ProductCount   = Util.GetValueOfInt(dr["ProductCount"]);
                    m.SrcWarehouseID = Util.GetValueOfInt(dr["SrcWarehouseID"]);
                    m.SrcWarehouse   = Util.GetValueOfString(dr["SrcWarehouseName"]);
                    m.DstWarehouseID = Util.GetValueOfInt(dr["DstWarehouseID"]);
                    m.DstWarehouse   = Util.GetValueOfString(dr["DstWarehouseName"]);
                    m.FromLocator    = Util.GetValueOfString(dr["FromLocator"]);
                    m.ToLocator      = Util.GetValueOfString(dr["ToLocator"]);
                }
            }
            finally
            {
                if (dr != null) { dr.Close(); dr.Dispose(); }
            }
        }

        private static void AggregateWarehouse(Dictionary<int, WarehouseAgg> map, int whId, string whName, MovementRow m)
        {
            if (!map.ContainsKey(whId))
            {
                map[whId] = new WarehouseAgg { WarehouseID = whId, WarehouseName = whName };
            }
            var agg = map[whId];
            if (!agg.SeenMovements.Contains(m.MovementID))
            {
                agg.SeenMovements.Add(m.MovementID);
                agg.Moves++;
                agg.TotalQty += m.TotalQty;
                agg.Movements.Add(m);
            }
        }

        private class MovementRow
        {
            public int      MovementID;
            public string   DocumentNo;
            public DateTime MovementDate;
            public int      SrcWarehouseID;
            public string   SrcWarehouse;
            public int      DstWarehouseID;
            public string   DstWarehouse;
            public string   FromLocator;
            public string   ToLocator;
            public string   DoneBy;
            public string   RequestedBy;
            public decimal  TotalQty;
            public int      ProductCount;
        }

        private class WarehouseAgg
        {
            public int            WarehouseID;
            public string         WarehouseName;
            public decimal        TotalQty;
            public int            Moves;
            public HashSet<int>   SeenMovements = new HashSet<int>();
            public List<MovementRow> Movements  = new List<MovementRow>();
        }
    }
}
