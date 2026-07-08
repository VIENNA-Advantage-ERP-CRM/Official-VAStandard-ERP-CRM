/// <summary>
/// Module Name : VASLogic
/// Purpose     : Internal Use / Material Issue Overview tab panel data (read
///               side). Returns header identity, warehouse, KPI aggregates
///               (issued value / quantity issued / not-fully-issued count /
///               total lines), the derived origin (production order /
///               requisition / manual issue) and the issue lines for a selected
///               internal-use material issue (M_Inventory where IsInternalUse is
///               'Y'). Per-line issued value and available stock are derived from
///               line quantities, item valuation and current storage.
/// Chronological development:
///   VAI163   2026-07-07  Created. Optional module columns (CurrentCostPrice,
///                        VA024_UnitPrice, VAMFG_M_WorkOrder_ID) are guarded
///                        through AD_Column so the panel works whether or not
///                        those modules are installed.
/// </summary>

using System;
using System.Collections.Generic;
using System.Data;
using System.Data.SqlClient;
using System.Text;
using VAdvantage.DataBase;
using VAdvantage.Logging;
using VAdvantage.Model;
using VAdvantage.Utility;

namespace VASLogic.Models
{
    public class VAS_102_OverviewInternalUseModel
    {
        private static readonly VLogger _log =
            VLogger.GetVLogger(typeof(VAS_102_OverviewInternalUseModel).FullName);

        /// <summary>
        /// Returns the full overview payload for the selected internal-use issue.
        /// MRole access filtering is applied ONLY on the main physical table
        /// (M_Inventory alias "inv"); the child line query inherits the parent's
        /// authorization and is not separately role-filtered.
        /// </summary>
        /// <param name="ctx">User context (client/org/role).</param>
        /// <param name="M_Inventory_ID">Selected internal-use issue id.</param>
        /// <returns>Populated <see cref="InternalUseOverviewData"/>; an empty
        /// instance when the id is invalid or no accessible row is found.</returns>
        public InternalUseOverviewData GetInternalUseOverview(Ctx ctx, int M_Inventory_ID)
        {
            InternalUseOverviewData result = new InternalUseOverviewData();
            if (M_Inventory_ID <= 0) return result;

            // Optional module columns — resolved once so the SQL below only
            // references columns that actually exist in this schema.
            bool hasCurrentCost = ColumnExists("M_InventoryLine", "CurrentCostPrice");
            bool hasUnitPrice   = ColumnExists("M_InventoryLine", "VA024_UnitPrice");
            bool hasWorkOrder   = ColumnExists("M_InventoryLine", "VAMFG_M_WorkOrder_ID");

            // COALESCE([l.CurrentCostPrice], [l.VA024_UnitPrice], 0)
            string rateExpr = BuildRateExpr(hasCurrentCost, hasUnitPrice);
            // Manufacturing work-order id (schema-aware) used to classify origin.
            string woExpr = hasWorkOrder ? "l.VAMFG_M_WorkOrder_ID" : "NULL";

            string sql = @"SELECT
                              inv.M_Inventory_ID,
                              inv.DocumentNo,
                              inv.DocStatus,
                              inv.Processed,
                              inv.Posted,
                              inv.MovementDate,
                              inv.Description,
                              wh.Name          AS WarehouseName,
                              creator.Name     AS IssuedBy,
                              (SELECT COUNT(*)
                                 FROM M_InventoryLine l
                                WHERE l.M_Inventory_ID = inv.M_Inventory_ID
                                  AND l.IsActive       = 'Y')                   AS LineCount,
                              (SELECT NVL(SUM(NVL(l.QtyEntered, 0)), 0)
                                 FROM M_InventoryLine l
                                WHERE l.M_Inventory_ID = inv.M_Inventory_ID
                                  AND l.IsActive       = 'Y')                   AS RequestedQty,
                              (SELECT NVL(SUM(NVL(l.QtyInternalUse, 0)), 0)
                                 FROM M_InventoryLine l
                                WHERE l.M_Inventory_ID = inv.M_Inventory_ID
                                  AND l.IsActive       = 'Y')                   AS IssuedQty,
                              (SELECT NVL(SUM(NVL(l.QtyInternalUse, 0) * " + rateExpr + @"), 0)
                                 FROM M_InventoryLine l
                                WHERE l.M_Inventory_ID = inv.M_Inventory_ID
                                  AND l.IsActive       = 'Y')                   AS TotalValue,
                              (SELECT NVL(SUM(CASE WHEN NVL(l.QtyInternalUse, 0) < NVL(l.QtyEntered, 0)
                                                   THEN 1 ELSE 0 END), 0)
                                 FROM M_InventoryLine l
                                WHERE l.M_Inventory_ID = inv.M_Inventory_ID
                                  AND l.IsActive       = 'Y')                   AS NotFullCount,
                              (SELECT MAX(l.M_RequisitionLine_ID)
                                 FROM M_InventoryLine l
                                WHERE l.M_Inventory_ID = inv.M_Inventory_ID
                                  AND l.IsActive       = 'Y')                   AS SampleRequisitionLineID,
                              (SELECT MAX(" + woExpr + @")
                                 FROM M_InventoryLine l
                                WHERE l.M_Inventory_ID = inv.M_Inventory_ID
                                  AND l.IsActive       = 'Y')                   AS SampleWorkOrderID
                            FROM M_Inventory inv
                            LEFT OUTER JOIN M_Warehouse wh   ON (inv.M_Warehouse_ID = wh.M_Warehouse_ID)
                            LEFT OUTER JOIN AD_User creator  ON (inv.CreatedBy      = creator.AD_User_ID)
                            WHERE inv.M_Inventory_ID = @M_Inventory_ID
                              AND inv.IsActive       = 'Y'
                              AND COALESCE(inv.IsInternalUse, 'N') = 'Y'";

            // MRole access only on the primary physical table the user is fetching.
            sql = MRole.GetDefault(ctx).AddAccessSQL(
                sql, "inv", MRole.SQL_FULLYQUALIFIED, MRole.SQL_RO);

            SqlParameter[] param = new SqlParameter[]
            {
                new SqlParameter("@M_Inventory_ID", M_Inventory_ID)
            };

            DataSet ds = DB.ExecuteDataset(sql, param, null);
            if (ds == null || ds.Tables.Count == 0 || ds.Tables[0].Rows.Count == 0)
                return result;

            DataRow r = ds.Tables[0].Rows[0];

            // ----- Header / identity -----
            result.M_Inventory_ID = Util.GetValueOfInt(r["M_Inventory_ID"]);
            result.DocumentNo     = Util.GetValueOfString(r["DocumentNo"]);
            result.StatusCode     = Util.GetValueOfString(r["DocStatus"]);
            result.Processed      = Util.GetValueOfString(r["Processed"]) == "Y";
            result.Posted         = Util.GetValueOfString(r["Posted"]) == "Y";
            result.MovementDate   = Util.GetValueOfDateTime(r["MovementDate"]);
            result.Description    = Util.GetValueOfString(r["Description"]);
            result.WarehouseName  = Util.GetValueOfString(r["WarehouseName"]);
            result.IssuedBy       = Util.GetValueOfString(r["IssuedBy"]);

            // ----- KPI aggregates -----
            result.LineCount    = Util.GetValueOfInt(r["LineCount"]);
            result.RequestedQty = Util.GetValueOfDecimal(r["RequestedQty"]);
            result.IssuedQty    = Util.GetValueOfDecimal(r["IssuedQty"]);
            result.TotalValue   = Util.GetValueOfDecimal(r["TotalValue"]);
            result.NotFullCount = Util.GetValueOfInt(r["NotFullCount"]);

            // ----- Origin: derived from the linked source ids on the lines -----
            // Production/work order wins over requisition; otherwise a manual issue.
            result.HasWorkOrder   = Util.GetValueOfInt(r["SampleWorkOrderID"]) > 0;
            result.HasRequisition = Util.GetValueOfInt(r["SampleRequisitionLineID"]) > 0;
            result.OriginCode     = result.HasWorkOrder ? "PRODUCTION"
                                  : (result.HasRequisition ? "REQUISITION" : "MANUAL");

            // Issued value is expressed in the accounting currency; the panel
            // renders INR (₹) with standard 2-dp precision.
            result.StdPrecision = 2;

            // ----- Issue lines -----
            result.Lines = LoadLines(M_Inventory_ID, rateExpr, woExpr);

            return result;
        }

        /// <summary>
        /// Builds the unit-rate SQL expression from whichever optional cost
        /// columns exist on M_InventoryLine, ending at 0.
        /// </summary>
        private string BuildRateExpr(bool hasCurrentCost, bool hasUnitPrice)
        {
            List<string> cols = new List<string>();
            if (hasCurrentCost) cols.Add("l.CurrentCostPrice");
            if (hasUnitPrice)   cols.Add("l.VA024_UnitPrice");
            if (cols.Count == 0) return "0";

            StringBuilder sb = new StringBuilder("COALESCE(");
            sb.Append(string.Join(", ", cols));
            sb.Append(", 0)");
            return sb.ToString();
        }

        /// <summary>
        /// Loads M_InventoryLine rows for the issue with product, locator and UOM
        /// metadata, requested (QtyEntered) / issued (QtyInternalUse) quantities,
        /// available stock at the locator (summed from M_Storage) and a unit rate /
        /// line value. Child of an already authorized issue, so no separate MRole
        /// filter is applied here.
        /// </summary>
        /// <param name="M_Inventory_ID">Owning internal-use issue id.</param>
        /// <param name="rateExpr">Unit-rate SQL expression (schema-aware).</param>
        /// <param name="woExpr">Work-order-id SQL expression (schema-aware).</param>
        /// <returns>Ordered list of line rows (may be empty).</returns>
        private List<InternalUseLineData> LoadLines(
            int M_Inventory_ID, string rateExpr, string woExpr)
        {
            List<InternalUseLineData> lines = new List<InternalUseLineData>();

            string sql = @"SELECT
                              l.M_InventoryLine_ID,
                              l.Line,
                              l.Description     AS LineDescription,
                              l.M_Product_ID,
                              NVL(l.QtyEntered, 0)     AS RequestedQty,
                              NVL(l.QtyInternalUse, 0) AS IssuedQty,
                              NVL(st.AvailableQty, 0)  AS AvailableQty,
                              p.Value           AS ProductCode,
                              p.Name            AS ProductName,
                              loc.Value         AS LocatorCode,
                              COALESCE(loc.LocatorCombination, loc.Bin, loc.Value) AS LocatorName,
                              u.Name            AS UOMName,
                              NVL(u.StdPrecision, 0) AS UOMPrecision,
                              " + rateExpr + @"                        AS UnitRate,
                              NVL(l.QtyInternalUse, 0) * " + rateExpr + @" AS LineValue,
                              l.M_RequisitionLine_ID AS RequisitionLineID,
                              " + woExpr + @"                          AS WorkOrderID
                           FROM M_InventoryLine l
                           LEFT OUTER JOIN M_Product p   ON (p.M_Product_ID   = l.M_Product_ID)
                           LEFT OUTER JOIN C_UOM     u   ON (u.C_UOM_ID        = l.C_UOM_ID)
                           LEFT OUTER JOIN M_Locator loc ON (loc.M_Locator_ID  = l.M_Locator_ID)
                           LEFT OUTER JOIN (SELECT s.M_Product_ID,
                                                   s.M_Locator_ID,
                                                   NVL(SUM(NVL(s.QtyOnHand, 0)), 0) AS AvailableQty
                                              FROM M_Storage s
                                             WHERE s.IsActive = 'Y'
                                             GROUP BY s.M_Product_ID, s.M_Locator_ID) st
                                  ON (st.M_Product_ID = l.M_Product_ID
                                  AND st.M_Locator_ID = l.M_Locator_ID)
                           WHERE l.M_Inventory_ID = @M_Inventory_ID
                             AND l.IsActive       = 'Y'
                           ORDER BY l.Line";

            SqlParameter[] param = new SqlParameter[]
            {
                new SqlParameter("@M_Inventory_ID", M_Inventory_ID)
            };

            DataSet ds = DB.ExecuteDataset(sql, param, null);
            if (ds == null || ds.Tables.Count == 0)
                return lines;

            foreach (DataRow r in ds.Tables[0].Rows)
            {
                InternalUseLineData ln = new InternalUseLineData();
                ln.M_InventoryLine_ID = Util.GetValueOfInt(r["M_InventoryLine_ID"]);
                ln.Line               = Util.GetValueOfInt(r["Line"]);
                ln.Description        = Util.GetValueOfString(r["LineDescription"]);
                ln.M_Product_ID       = Util.GetValueOfInt(r["M_Product_ID"]);
                ln.RequestedQty       = Util.GetValueOfDecimal(r["RequestedQty"]);
                ln.IssuedQty          = Util.GetValueOfDecimal(r["IssuedQty"]);
                ln.AvailableQty       = Util.GetValueOfDecimal(r["AvailableQty"]);
                ln.ProductCode        = Util.GetValueOfString(r["ProductCode"]);
                ln.ProductName        = Util.GetValueOfString(r["ProductName"]);
                ln.LocatorCode        = Util.GetValueOfString(r["LocatorCode"]);
                ln.LocatorName        = Util.GetValueOfString(r["LocatorName"]);
                ln.UOMName            = Util.GetValueOfString(r["UOMName"]);
                ln.UOMPrecision       = Util.GetValueOfInt(r["UOMPrecision"]);
                ln.UnitRate           = Util.GetValueOfDecimal(r["UnitRate"]);
                ln.LineValue          = Util.GetValueOfDecimal(r["LineValue"]);
                ln.RequisitionLineID  = Util.GetValueOfInt(r["RequisitionLineID"]);
                ln.WorkOrderID        = Util.GetValueOfInt(r["WorkOrderID"]);

                lines.Add(ln);
            }
            return lines;
        }

        /// <summary>
        /// Returns true when the given column exists on the given table, using
        /// the AD_Column dictionary. A DB issue degrades to "absent" (false) so a
        /// lookup failure never breaks the overview query.
        /// </summary>
        private bool ColumnExists(string tableName, string columnName)
        {
            try
            {
                string sql = @"SELECT COUNT(*) FROM AD_Column
                                WHERE UPPER(ColumnName) = UPPER(@ColumnName)
                                  AND AD_Table_ID = (SELECT AD_Table_ID FROM AD_Table
                                                      WHERE UPPER(TableName) = UPPER(@TableName))";
                SqlParameter[] param = new SqlParameter[]
                {
                    new SqlParameter("@ColumnName", columnName),
                    new SqlParameter("@TableName", tableName)
                };
                return Util.GetValueOfInt(DB.ExecuteScalar(sql, param, null)) > 0;
            }
            catch (Exception ex)
            {
                _log.Severe("ColumnExists (" + tableName + "." + columnName + "): " + ex.Message);
                return false;
            }
        }

        // ----------------------------------------------------------------- //
        //  Data carriers                                                     //
        // ----------------------------------------------------------------- //

        public class InternalUseLineData
        {
            public int      M_InventoryLine_ID { get; set; }
            public int      Line               { get; set; }
            public string   Description        { get; set; }
            public int      M_Product_ID       { get; set; }
            public string   ProductCode        { get; set; }   // SKU
            public string   ProductName        { get; set; }
            public string   LocatorCode        { get; set; }
            public string   LocatorName        { get; set; }
            public string   UOMName            { get; set; }
            public int      UOMPrecision       { get; set; }
            public decimal  RequestedQty       { get; set; }   // QtyEntered
            public decimal  IssuedQty          { get; set; }   // QtyInternalUse
            public decimal  AvailableQty       { get; set; }   // M_Storage on-hand
            public decimal  UnitRate           { get; set; }
            public decimal  LineValue          { get; set; }
            public int      RequisitionLineID  { get; set; }
            public int      WorkOrderID        { get; set; }
        }

        public class InternalUseOverviewData
        {
            // Header / identity
            public int       M_Inventory_ID { get; set; }
            public string    DocumentNo     { get; set; }
            public string    StatusCode     { get; set; }   // DocStatus code
            public bool      Processed      { get; set; }
            public bool      Posted         { get; set; }
            public DateTime? MovementDate   { get; set; }
            public string    Description    { get; set; }
            public string    WarehouseName  { get; set; }
            public string    IssuedBy       { get; set; }

            // Origin (derived from the linked source ids on the lines)
            public string    OriginCode     { get; set; }   // PRODUCTION | REQUISITION | MANUAL
            public bool      HasWorkOrder   { get; set; }
            public bool      HasRequisition { get; set; }

            // Currency
            public int       StdPrecision   { get; set; }

            // KPI aggregates
            public int       LineCount    { get; set; }
            public decimal   RequestedQty { get; set; }
            public decimal   IssuedQty    { get; set; }
            public decimal   TotalValue   { get; set; }
            public int       NotFullCount { get; set; }

            // Collections
            public List<InternalUseLineData> Lines { get; set; }
        }
    }
}
