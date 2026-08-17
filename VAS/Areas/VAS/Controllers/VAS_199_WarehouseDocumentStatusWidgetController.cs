using Newtonsoft.Json;
using System;
using System.Collections.Generic;
using System.Data;
using System.Web.Mvc;
using VAdvantage.DataBase;
using VAdvantage.Logging;
using VAdvantage.Model;
using VAdvantage.Utility;
using VIS.Filters;

namespace VAS.Controllers
{
    /// <summary>
    /// Module Name : Purchase Order Dashboard - Widget 09
    /// Purpose     : Warehouse wise PO · Document Status Widget
    ///               Returns monthly matrix of header receiving warehouses against real
    ///               C_Order document statuses (Drafted, In Process, Completed, Closed).
    ///               Includes drill-down endpoints for warehouse PO lists and PO lines.
    /// Prefix      : VAS_199_
    /// 
    /// TABLE & FIELD MAPPING (Data Contract):
    /// - Purchase Order Header : C_Order (C_Order_ID, DocumentNo, DateOrdered, DatePromised, GrandTotal,
    ///                                    DocStatus, C_BPartner_ID, M_Warehouse_ID, SalesRep_ID,
    ///                                    C_Currency_ID, Created, CreatedBy, IsSOTrx, IsReturnTrx, IsActive)
    /// - Purchase Order Lines  : C_OrderLine (C_OrderLine_ID, C_Order_ID, Line, M_Product_ID,
    ///                                       M_AttributeSetInstance_ID, C_UOM_ID, QtyOrdered,
    ///                                       QtyDelivered, PriceActual, LineNetAmt, IsActive)
    /// - Receiving Warehouse   : M_Warehouse (M_Warehouse_ID, Name, Value)
    /// - Vendor                : C_BPartner (C_BPartner_ID, Name, Value)
    /// - Sales Representative  : AD_User (AD_User_ID, Name)
    /// - Product               : M_Product (M_Product_ID, Name, Value)
    /// - Attributes            : M_AttributeSetInstance (M_AttributeSetInstance_ID, Description)
    /// - Unit of Measure       : C_UOM (C_UOM_ID, UOMSymbol, Name)
    /// - Currency              : C_Currency (C_Currency_ID, CurSymbol, ISO_Code)
    /// </summary>
    [AjaxAuthorize]
    [AjaxSessionFilter]
    public class VAS_199_WarehouseDocumentStatusWidgetController : Controller
    {
        private static readonly VLogger _log = VLogger.GetVLogger(typeof(VAS_199_WarehouseDocumentStatusWidgetController));

        /// <summary>
        /// Gets available distinct years from purchase order records for the year filter.
        /// </summary>
        [HttpGet]
        public JsonResult GetAvailableYears()
        {
            Ctx ctx = Session["ctx"] as Ctx;
            if (ctx == null)
            {
                return Json(new { error = "Unauthorized context." }, JsonRequestBehavior.AllowGet);
            }

            List<int> years = new List<int>();
            IDataReader dr = null;
            try
            {
                string sql = @"
                    SELECT DISTINCT EXTRACT(YEAR FROM o.DateOrdered) AS OrderYear 
                    FROM C_Order o 
                    WHERE o.IsActive = 'Y' 
                      AND o.IsSOTrx = 'N' 
                      AND COALESCE(o.IsReturnTrx, 'N') = 'N' 
                      AND o.DocStatus IN ('DR', 'IP', 'CO', 'CL') 
                      AND o.DateOrdered IS NOT NULL";

                sql = MRole.GetDefault(ctx).AddAccessSQL(sql, "o", MRole.SQL_FULLYQUALIFIED, MRole.SQL_RO);
                sql += " ORDER BY OrderYear DESC";

                dr = DB.ExecuteReader(sql, null, null);
                while (dr != null && dr.Read())
                {
                    int yr = Util.GetValueOfInt(dr["OrderYear"]);
                    if (yr > 0 && !years.Contains(yr))
                    {
                        years.Add(yr);
                    }
                }

                int currentYear = DateTime.Now.Year;
                if (!years.Contains(currentYear))
                {
                    years.Insert(0, currentYear);
                }
                if (!years.Contains(currentYear - 1))
                {
                    years.Add(currentYear - 1);
                }
                if (!years.Contains(currentYear - 2))
                {
                    years.Add(currentYear - 2);
                }
            }
            catch (Exception ex)
            {
                _log.Severe("VAS_199_WarehouseDocumentStatusWidgetController.GetAvailableYears: " + ex.Message);
            }
            finally
            {
                if (dr != null) { dr.Close(); dr.Dispose(); }
            }

            return Json(new { years = years }, JsonRequestBehavior.AllowGet);
        }

        /// <summary>
        /// Query 1: Warehouse wise document status grouped matrix.
        /// Pivots grouped rows into one warehouse record per active warehouse.
        /// </summary>
        [HttpGet]
        public JsonResult GetWarehouseDocumentStatus(int month, int year)
        {
            Ctx ctx = Session["ctx"] as Ctx;
            if (ctx == null)
            {
                return Json(new { error = "Unauthorized context." }, JsonRequestBehavior.AllowGet);
            }

            var warehouseMap = new Dictionary<int, WarehouseStatusRow>();
            int grandTotal = 0;
            IDataReader dr = null;

            try
            {
                DateTime startDate = new DateTime(year, month, 1);
                DateTime endDateExclusive = startDate.AddMonths(1);

                string sql = @"
                    SELECT
                        w.M_Warehouse_ID AS WarehouseID,
                        w.Name           AS WarehouseName,
                        o.DocStatus      AS DocStatus,
                        COUNT(o.C_Order_ID) AS DocumentCount
                    FROM C_Order o
                    INNER JOIN M_Warehouse w ON (w.M_Warehouse_ID = o.M_Warehouse_ID)
                    WHERE o.IsActive = 'Y'
                      AND o.IsSOTrx = 'N'
                      AND COALESCE(o.IsReturnTrx, 'N') = 'N'
                      AND o.DocStatus IN ('DR', 'IP', 'CO', 'CL')
                      AND o.DateOrdered >= " + DB.TO_DATE(startDate, true) + @"
                      AND o.DateOrdered < " + DB.TO_DATE(endDateExclusive, true);

                sql = MRole.GetDefault(ctx).AddAccessSQL(sql, "o", MRole.SQL_FULLYQUALIFIED, MRole.SQL_RO);
                sql += " GROUP BY w.M_Warehouse_ID, w.Name, o.DocStatus ORDER BY w.Name ASC, o.DocStatus ASC";

                dr = DB.ExecuteReader(sql, null, null);
                while (dr != null && dr.Read())
                {
                    int whId = Util.GetValueOfInt(dr["WarehouseID"]);
                    string whName = Util.GetValueOfString(dr["WarehouseName"]);
                    string status = Util.GetValueOfString(dr["DocStatus"]);
                    int count = Util.GetValueOfInt(dr["DocumentCount"]);

                    if (!warehouseMap.ContainsKey(whId))
                    {
                        warehouseMap[whId] = new WarehouseStatusRow
                        {
                            WarehouseID   = whId,
                            WarehouseName = whName,
                            Drafted       = 0,
                            InProcess     = 0,
                            Completed     = 0,
                            Closed        = 0,
                            Total         = 0
                        };
                    }

                    var row = warehouseMap[whId];
                    if (status.Equals("DR", StringComparison.OrdinalIgnoreCase))
                    {
                        row.Drafted += count;
                    }
                    else if (status.Equals("IP", StringComparison.OrdinalIgnoreCase))
                    {
                        row.InProcess += count;
                    }
                    else if (status.Equals("CO", StringComparison.OrdinalIgnoreCase))
                    {
                        row.Completed += count;
                    }
                    else if (status.Equals("CL", StringComparison.OrdinalIgnoreCase))
                    {
                        row.Closed += count;
                    }

                    row.Total += count;
                    grandTotal += count;
                }
            }
            catch (Exception ex)
            {
                _log.Severe("VAS_199_WarehouseDocumentStatusWidgetController.GetWarehouseDocumentStatus: " + ex.Message);
                return Json(new { error = ex.Message }, JsonRequestBehavior.AllowGet);
            }
            finally
            {
                if (dr != null) { dr.Close(); dr.Dispose(); }
            }

            var list = new List<object>();
            foreach (var item in warehouseMap.Values)
            {
                list.Add(new
                {
                    warehouseId   = item.WarehouseID,
                    warehouseName = item.WarehouseName,
                    drafted       = item.Drafted,
                    inProcess     = item.InProcess,
                    completed     = item.Completed,
                    closed        = item.Closed,
                    total         = item.Total
                });
            }

            return Json(new
            {
                warehouses     = list,
                totalDocuments = grandTotal
            }, JsonRequestBehavior.AllowGet);
        }

        /// <summary>
        /// Query 2: Warehouse purchase order drill-down detail list.
        /// Returns all POs for the selected warehouse and month.
        /// </summary>
        [HttpGet]
        public JsonResult GetWarehouseOrders(int warehouseId, int month, int year)
        {
            Ctx ctx = Session["ctx"] as Ctx;
            if (ctx == null)
            {
                return Json(new { error = "Unauthorized context." }, JsonRequestBehavior.AllowGet);
            }

            var orders = new List<object>();
            int countDrafted = 0;
            int countInProcess = 0;
            int countCompleted = 0;
            int countClosed = 0;
            IDataReader dr = null;

            try
            {
                DateTime startDate = new DateTime(year, month, 1);
                DateTime endDateExclusive = startDate.AddMonths(1);

                string sql = @"
                    SELECT
                        o.C_Order_ID    AS OrderID,
                        o.DocumentNo    AS DocumentNo,
                        o.DateOrdered   AS DateOrdered,
                        o.DatePromised  AS DatePromised,
                        COALESCE(bp.Name, N'—') AS VendorName,
                        w.Name          AS WarehouseName,
                        COALESCE(usr.Name, N'—') AS SalesRepName,
                        o.GrandTotal    AS OrderTotal,
                        o.DocStatus     AS DocStatus,
                        COALESCE(c.CurSymbol, c.ISO_Code, N'') AS CurrencySymbol,
                        COALESCE(lines.QtyOrdered, 0)   AS QtyOrdered,
                        COALESCE(lines.QtyDelivered, 0) AS QtyDelivered,
                        COALESCE(lines.LineCount, 0)    AS LineCount
                    FROM C_Order o
                    INNER JOIN M_Warehouse w ON (w.M_Warehouse_ID = o.M_Warehouse_ID)
                    LEFT JOIN C_BPartner bp  ON (bp.C_BPartner_ID = o.C_BPartner_ID)
                    LEFT JOIN AD_User usr    ON (usr.AD_User_ID = o.SalesRep_ID)
                    LEFT JOIN C_Currency c   ON (c.C_Currency_ID = o.C_Currency_ID)
                    LEFT JOIN (
                        SELECT
                            ol.C_Order_ID,
                            SUM(COALESCE(ol.QtyOrdered, 0))   AS QtyOrdered,
                            SUM(COALESCE(ol.QtyDelivered, 0)) AS QtyDelivered,
                            COUNT(ol.C_OrderLine_ID)          AS LineCount
                        FROM C_OrderLine ol
                        WHERE ol.IsActive = 'Y'
                        GROUP BY ol.C_Order_ID
                    ) lines ON (lines.C_Order_ID = o.C_Order_ID)
                    WHERE o.IsActive = 'Y'
                      AND o.IsSOTrx = 'N'
                      AND COALESCE(o.IsReturnTrx, 'N') = 'N'
                      AND o.DocStatus IN ('DR', 'IP', 'CO', 'CL')
                      AND o.M_Warehouse_ID = " + warehouseId + @"
                      AND o.DateOrdered >= " + DB.TO_DATE(startDate, true) + @"
                      AND o.DateOrdered < " + DB.TO_DATE(endDateExclusive, true);

                sql = MRole.GetDefault(ctx).AddAccessSQL(sql, "o", MRole.SQL_FULLYQUALIFIED, MRole.SQL_RO);
                sql += " ORDER BY o.DateOrdered DESC, o.DocumentNo DESC";

                dr = DB.ExecuteReader(sql, null, null);
                while (dr != null && dr.Read())
                {
                    string docStatus = Util.GetValueOfString(dr["DocStatus"]);
                    decimal qtyOrd   = Util.GetValueOfDecimal(dr["QtyOrdered"]);
                    decimal qtyDel   = Util.GetValueOfDecimal(dr["QtyDelivered"]);

                    string docStatusName = "Drafted";
                    string docStatusChip = "chip-neutral";

                    if (docStatus.Equals("DR", StringComparison.OrdinalIgnoreCase))
                    {
                        docStatusName = "Drafted";
                        docStatusChip = "chip-neutral";
                        countDrafted++;
                    }
                    else if (docStatus.Equals("IP", StringComparison.OrdinalIgnoreCase))
                    {
                        docStatusName = "In Process";
                        docStatusChip = "chip-prop";
                        countInProcess++;
                    }
                    else if (docStatus.Equals("CO", StringComparison.OrdinalIgnoreCase))
                    {
                        docStatusName = "Completed";
                        docStatusChip = "chip-ok";
                        countCompleted++;
                    }
                    else if (docStatus.Equals("CL", StringComparison.OrdinalIgnoreCase))
                    {
                        docStatusName = "Closed";
                        docStatusChip = "chip-ok";
                        countClosed++;
                    }

                    string deliveryStatus = "Pending";
                    string deliveryChip   = "chip-neutral";

                    if (docStatus.Equals("CL", StringComparison.OrdinalIgnoreCase) || docStatus.Equals("VO", StringComparison.OrdinalIgnoreCase))
                    {
                        deliveryStatus = "Not applicable";
                        deliveryChip   = "chip-neutral";
                    }
                    else if (qtyDel >= qtyOrd && qtyOrd > 0)
                    {
                        deliveryStatus = "Fully delivered";
                        deliveryChip   = "chip-ok";
                    }
                    else if (qtyDel > 0 && qtyDel < qtyOrd)
                    {
                        deliveryStatus = "Partial";
                        deliveryChip   = "chip-warn";
                    }
                    else
                    {
                        deliveryStatus = "Pending";
                        deliveryChip   = "chip-neutral";
                    }

                    orders.Add(new
                    {
                        orderId        = Util.GetValueOfInt(dr["OrderID"]),
                        documentNo     = Util.GetValueOfString(dr["DocumentNo"]),
                        dateOrdered    = Util.GetValueOfDateTime(dr["DateOrdered"]),
                        datePromised   = Util.GetValueOfDateTime(dr["DatePromised"]),
                        vendorName     = Util.GetValueOfString(dr["VendorName"]),
                        warehouseName  = Util.GetValueOfString(dr["WarehouseName"]),
                        salesRepName   = Util.GetValueOfString(dr["SalesRepName"]),
                        orderTotal     = Util.GetValueOfDecimal(dr["OrderTotal"]),
                        currencySymbol = Util.GetValueOfString(dr["CurrencySymbol"]),
                        docStatusCode  = docStatus,
                        docStatusName  = docStatusName,
                        docStatusChip  = docStatusChip,
                        deliveryStatus = deliveryStatus,
                        deliveryChip   = deliveryChip,
                        qtyOrdered     = qtyOrd,
                        qtyDelivered   = qtyDel,
                        qtyPending     = Math.Max(0, qtyOrd - qtyDel),
                        lineCount      = Util.GetValueOfInt(dr["LineCount"])
                    });
                }
            }
            catch (Exception ex)
            {
                _log.Severe("VAS_199_WarehouseDocumentStatusWidgetController.GetWarehouseOrders: " + ex.Message);
                return Json(new { error = ex.Message }, JsonRequestBehavior.AllowGet);
            }
            finally
            {
                if (dr != null) { dr.Close(); dr.Dispose(); }
            }

            return Json(new
            {
                orders         = orders,
                totalDocuments = orders.Count,
                drafted        = countDrafted,
                inProcess      = countInProcess,
                completed      = countCompleted,
                closed         = countClosed
            }, JsonRequestBehavior.AllowGet);
        }

        /// <summary>
        /// Query 3: Detail lines of a specific Purchase Order.
        /// Returns order header summary + active lines.
        /// </summary>
        [HttpGet]
        public JsonResult GetOrderLines(int orderId)
        {
            Ctx ctx = Session["ctx"] as Ctx;
            if (ctx == null)
            {
                return Json(new { error = "Unauthorized context." }, JsonRequestBehavior.AllowGet);
            }

            object orderHeader = null;
            var lines = new List<object>();
            string parentDocStatus = "DR";
            decimal totalQtyOrdered = 0;
            decimal totalQtyDelivered = 0;
            IDataReader dr = null;

            try
            {
                // 1. Fetch Header Information
                string headerSql = @"
                    SELECT
                        o.C_Order_ID    AS OrderID,
                        o.DocumentNo    AS DocumentNo,
                        o.DateOrdered   AS DateOrdered,
                        o.DatePromised  AS DatePromised,
                        o.Created       AS CreatedOn,
                        o.GrandTotal    AS GrandTotal,
                        o.DocStatus     AS DocStatus,
                        COALESCE(bp.Name, N'—') AS VendorName,
                        w.Name          AS WarehouseName,
                        COALESCE(uCreated.Name, N'—') AS CreatedByName,
                        COALESCE(c.CurSymbol, c.ISO_Code, N'') AS CurrencySymbol
                    FROM C_Order o
                    INNER JOIN M_Warehouse w ON (w.M_Warehouse_ID = o.M_Warehouse_ID)
                    LEFT JOIN C_BPartner bp  ON (bp.C_BPartner_ID = o.C_BPartner_ID)
                    LEFT JOIN AD_User uCreated ON (uCreated.AD_User_ID = o.CreatedBy)
                    LEFT JOIN C_Currency c   ON (c.C_Currency_ID = o.C_Currency_ID)
                    WHERE o.C_Order_ID = " + orderId;

                headerSql = MRole.GetDefault(ctx).AddAccessSQL(headerSql, "o", MRole.SQL_FULLYQUALIFIED, MRole.SQL_RO);

                dr = DB.ExecuteReader(headerSql, null, null);
                if (dr != null && dr.Read())
                {
                    parentDocStatus = Util.GetValueOfString(dr["DocStatus"]);
                    string docStatusName = "Drafted";
                    string docStatusChip = "chip-neutral";

                    if (parentDocStatus.Equals("DR", StringComparison.OrdinalIgnoreCase))
                    {
                        docStatusName = "Drafted";
                        docStatusChip = "chip-neutral";
                    }
                    else if (parentDocStatus.Equals("IP", StringComparison.OrdinalIgnoreCase))
                    {
                        docStatusName = "In Process";
                        docStatusChip = "chip-prop";
                    }
                    else if (parentDocStatus.Equals("CO", StringComparison.OrdinalIgnoreCase))
                    {
                        docStatusName = "Completed";
                        docStatusChip = "chip-ok";
                    }
                    else if (parentDocStatus.Equals("CL", StringComparison.OrdinalIgnoreCase))
                    {
                        docStatusName = "Closed";
                        docStatusChip = "chip-ok";
                    }

                    orderHeader = new
                    {
                        orderId        = Util.GetValueOfInt(dr["OrderID"]),
                        documentNo     = Util.GetValueOfString(dr["DocumentNo"]),
                        dateOrdered    = Util.GetValueOfDateTime(dr["DateOrdered"]),
                        datePromised   = Util.GetValueOfDateTime(dr["DatePromised"]),
                        createdOn      = Util.GetValueOfDateTime(dr["CreatedOn"]),
                        grandTotal     = Util.GetValueOfDecimal(dr["GrandTotal"]),
                        vendorName     = Util.GetValueOfString(dr["VendorName"]),
                        warehouseName  = Util.GetValueOfString(dr["WarehouseName"]),
                        createdByName  = Util.GetValueOfString(dr["CreatedByName"]),
                        currencySymbol = Util.GetValueOfString(dr["CurrencySymbol"]),
                        docStatusCode  = parentDocStatus,
                        docStatusName  = docStatusName,
                        docStatusChip  = docStatusChip
                    };
                }
                if (dr != null) { dr.Close(); dr.Dispose(); dr = null; }

                // 2. Fetch Line Items
                string lineSql = @"
                    SELECT
                        ol.C_OrderLine_ID AS OrderLineID,
                        ol.Line           AS LineNo,
                        COALESCE(p.Name, N'Standard Product')  AS ProductName,
                        COALESCE(asi.Description, N'Standard') AS AttributeDesc,
                        COALESCE(uom.UOMSymbol, uom.Name, N'') AS UomName,
                        COALESCE(ol.QtyOrdered, 0)   AS QtyOrdered,
                        COALESCE(ol.QtyDelivered, 0) AS QtyDelivered,
                        COALESCE(ol.PriceActual, 0)  AS PriceActual,
                        COALESCE(ol.LineNetAmt, 0)   AS LineNetAmt
                    FROM C_OrderLine ol
                    LEFT JOIN M_Product p ON (p.M_Product_ID = ol.M_Product_ID)
                    LEFT JOIN M_AttributeSetInstance asi ON (asi.M_AttributeSetInstance_ID = ol.M_AttributeSetInstance_ID)
                    LEFT JOIN C_UOM uom ON (uom.C_UOM_ID = ol.C_UOM_ID)
                    WHERE ol.C_Order_ID = " + orderId + @"
                      AND ol.IsActive = 'Y'
                    ORDER BY ol.Line ASC";

                dr = DB.ExecuteReader(lineSql, null, null);
                int lineIndex = 1;
                while (dr != null && dr.Read())
                {
                    decimal ord = Util.GetValueOfDecimal(dr["QtyOrdered"]);
                    decimal del = Util.GetValueOfDecimal(dr["QtyDelivered"]);
                    totalQtyOrdered += ord;
                    totalQtyDelivered += del;

                    string lineStatus = "Pending";
                    string lineChip   = "chip-neutral";

                    if (parentDocStatus.Equals("DR", StringComparison.OrdinalIgnoreCase))
                    {
                        lineStatus = "Drafted";
                        lineChip   = "chip-neutral";
                    }
                    else if (del >= ord && ord > 0)
                    {
                        lineStatus = "Received";
                        lineChip   = "chip-ok";
                    }
                    else if (del > 0 && del < ord)
                    {
                        lineStatus = "Partial received";
                        lineChip   = "chip-warn";
                    }
                    else
                    {
                        lineStatus = "Pending";
                        lineChip   = "chip-neutral";
                    }

                    lines.Add(new
                    {
                        lineIndex     = lineIndex++,
                        lineNo        = Util.GetValueOfInt(dr["LineNo"]),
                        productName   = Util.GetValueOfString(dr["ProductName"]),
                        attributeDesc = Util.GetValueOfString(dr["AttributeDesc"]),
                        uomName       = Util.GetValueOfString(dr["UomName"]),
                        qtyOrdered    = ord,
                        qtyDelivered  = del,
                        qtyPending    = Math.Max(0, ord - del),
                        priceActual   = Util.GetValueOfDecimal(dr["PriceActual"]),
                        lineNetAmt    = Util.GetValueOfDecimal(dr["LineNetAmt"]),
                        lineStatus    = lineStatus,
                        lineChip      = lineChip
                    });
                }
            }
            catch (Exception ex)
            {
                _log.Severe("VAS_199_WarehouseDocumentStatusWidgetController.GetOrderLines: " + ex.Message);
                return Json(new { error = ex.Message }, JsonRequestBehavior.AllowGet);
            }
            finally
            {
                if (dr != null) { dr.Close(); dr.Dispose(); }
            }

            string delStatus = "Pending";
            string delChip = "chip-neutral";
            if (parentDocStatus.Equals("CL", StringComparison.OrdinalIgnoreCase) || parentDocStatus.Equals("VO", StringComparison.OrdinalIgnoreCase))
            {
                delStatus = "Not applicable";
                delChip = "chip-neutral";
            }
            else if (totalQtyDelivered >= totalQtyOrdered && totalQtyOrdered > 0)
            {
                delStatus = "Fully delivered";
                delChip = "chip-ok";
            }
            else if (totalQtyDelivered > 0 && totalQtyDelivered < totalQtyOrdered)
            {
                delStatus = "Partial";
                delChip = "chip-warn";
            }

            return Json(new
            {
                header            = orderHeader,
                lines             = lines,
                totalLines        = lines.Count,
                totalQtyOrdered   = totalQtyOrdered,
                totalQtyDelivered = totalQtyDelivered,
                totalQtyPending   = Math.Max(0, totalQtyOrdered - totalQtyDelivered),
                deliveryStatus    = delStatus,
                deliveryChip      = delChip
            }, JsonRequestBehavior.AllowGet);
        }

        private class WarehouseStatusRow
        {
            public int    WarehouseID;
            public string WarehouseName;
            public int    Drafted;
            public int    InProcess;
            public int    Completed;
            public int    Closed;
            public int    Total;
        }
    }
}
