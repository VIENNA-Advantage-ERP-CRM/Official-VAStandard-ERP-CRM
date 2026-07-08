/// <summary>
/// Module Name : VASLogic
/// Purpose     : Material Transfer Overview tab panel data (read side). Returns
///               header identity, source / destination warehouses, the derived
///               transfer type (inter- vs intra-warehouse), KPI aggregates (line
///               count / transfer quantity / transfer value / confirmation
///               count), the derived origin flags (requisition / production
///               order) and the transfer lines for a selected stock movement
///               (M_Movement). Per-line value is derived from the movement
///               quantity and item valuation.
/// Chronological development:
///   VAI163   2026-07-07  Created. Optional module columns (CurrentCostPrice,
///                        VA024_UnitPrice, VAMFG_M_WorkOrder_ID,
///                        DTD001_MWarehouseSource_ID) and the optional
///                        M_MovementConfirm table are guarded through the AD
///                        dictionary so the panel works whether or not those
///                        modules are installed.
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
    public class VAS_103_MaterialTransferModel
    {
        private static readonly VLogger _log =
            VLogger.GetVLogger(typeof(VAS_103_MaterialTransferModel).FullName);

        /// <summary>
        /// Returns the full overview payload for the selected material transfer.
        /// MRole access filtering is applied ONLY on the main physical table
        /// (M_Movement alias "m"); the child line query inherits the parent's
        /// authorization and is not separately role-filtered.
        /// </summary>
        /// <param name="ctx">User context (client/org/role).</param>
        /// <param name="M_Movement_ID">Selected stock movement id.</param>
        /// <returns>Populated <see cref="MaterialTransferOverviewData"/>; an empty
        /// instance when the id is invalid or no accessible row is found.</returns>
        public MaterialTransferOverviewData GetMaterialTransferOverview(Ctx ctx, int M_Movement_ID)
        {
            MaterialTransferOverviewData result = new MaterialTransferOverviewData();
            if (M_Movement_ID <= 0) return result;

            // Optional module columns / table — resolved once so the SQL below
            // only references objects that actually exist in this schema.
            bool hasCurrentCost = ColumnExists("M_MovementLine", "CurrentCostPrice");
            bool hasUnitPrice   = ColumnExists("M_MovementLine", "VA024_UnitPrice");
            bool hasWorkOrder   = ColumnExists("M_MovementLine", "VAMFG_M_WorkOrder_ID");
            bool hasSourceWh    = ColumnExists("M_Movement", "DTD001_MWarehouseSource_ID");
            bool hasConfirm     = ColumnExists("M_MovementConfirm", "M_Movement_ID");

            // COALESCE([l.CurrentCostPrice], [l.VA024_UnitPrice], 0)
            string rateExpr = BuildRateExpr(hasCurrentCost, hasUnitPrice);
            // Manufacturing work-order id (schema-aware) used to classify origin.
            string woExpr = hasWorkOrder ? "l.VAMFG_M_WorkOrder_ID" : "NULL";
            // Source warehouse id (schema-aware); absent module => no source split.
            string srcWhExpr = hasSourceWh ? "m.DTD001_MWarehouseSource_ID" : "NULL";
            // Confirmation count (schema-aware).
            string confirmExpr = hasConfirm
                ? @"(SELECT COUNT(*) FROM M_MovementConfirm c
                      WHERE c.M_Movement_ID = m.M_Movement_ID
                        AND c.IsActive      = 'Y')"
                : "0";

            string sql = @"SELECT
                              m.M_Movement_ID,
                              m.DocumentNo,
                              m.DocStatus,
                              m.Processed,
                              m.Posted,
                              m.MovementDate,
                              m.Description,
                              from_wh.Name     AS FromWarehouseName,
                              to_wh.Name       AS ToWarehouseName,
                              creator.Name     AS RequestedBy,
                              CASE WHEN " + srcWhExpr + @" = m.M_Warehouse_ID
                                   THEN 'INTRA' ELSE 'INTER' END          AS TransferTypeCode,
                              (SELECT COUNT(*)
                                 FROM M_MovementLine l
                                WHERE l.M_Movement_ID = m.M_Movement_ID
                                  AND l.IsActive      = 'Y')              AS LineCount,
                              (SELECT NVL(SUM(NVL(l.MovementQty, 0)), 0)
                                 FROM M_MovementLine l
                                WHERE l.M_Movement_ID = m.M_Movement_ID
                                  AND l.IsActive      = 'Y')              AS TransferQty,
                              (SELECT NVL(SUM(NVL(l.MovementQty, 0) * " + rateExpr + @"), 0)
                                 FROM M_MovementLine l
                                WHERE l.M_Movement_ID = m.M_Movement_ID
                                  AND l.IsActive      = 'Y')              AS TransferValue,
                              " + confirmExpr + @"                        AS ConfirmationCount,
                              (SELECT MAX(l.M_RequisitionLine_ID)
                                 FROM M_MovementLine l
                                WHERE l.M_Movement_ID = m.M_Movement_ID
                                  AND l.IsActive      = 'Y')              AS SampleRequisitionLineID,
                              (SELECT MAX(" + woExpr + @")
                                 FROM M_MovementLine l
                                WHERE l.M_Movement_ID = m.M_Movement_ID
                                  AND l.IsActive      = 'Y')              AS SampleWorkOrderID
                            FROM M_Movement m
                            LEFT OUTER JOIN M_Warehouse from_wh ON (from_wh.M_Warehouse_ID = " + srcWhExpr + @")
                            LEFT OUTER JOIN M_Warehouse to_wh   ON (to_wh.M_Warehouse_ID   = m.M_Warehouse_ID)
                            LEFT OUTER JOIN AD_User creator     ON (creator.AD_User_ID     = m.CreatedBy)
                            WHERE m.M_Movement_ID = @M_Movement_ID
                              AND m.IsActive      = 'Y'";

            // MRole access only on the primary physical table the user is fetching.
            sql = MRole.GetDefault(ctx).AddAccessSQL(
                sql, "m", MRole.SQL_FULLYQUALIFIED, MRole.SQL_RO);

            SqlParameter[] param = new SqlParameter[]
            {
                new SqlParameter("@M_Movement_ID", M_Movement_ID)
            };

            DataSet ds = DB.ExecuteDataset(sql, param, null);
            if (ds == null || ds.Tables.Count == 0 || ds.Tables[0].Rows.Count == 0)
                return result;

            DataRow r = ds.Tables[0].Rows[0];

            // ----- Header / identity -----
            result.M_Movement_ID    = Util.GetValueOfInt(r["M_Movement_ID"]);
            result.DocumentNo       = Util.GetValueOfString(r["DocumentNo"]);
            result.StatusCode       = Util.GetValueOfString(r["DocStatus"]);
            result.Processed        = Util.GetValueOfString(r["Processed"]) == "Y";
            result.Posted           = Util.GetValueOfString(r["Posted"]) == "Y";
            result.MovementDate     = Util.GetValueOfDateTime(r["MovementDate"]);
            result.Description      = Util.GetValueOfString(r["Description"]);
            result.FromWarehouseName = Util.GetValueOfString(r["FromWarehouseName"]);
            result.ToWarehouseName   = Util.GetValueOfString(r["ToWarehouseName"]);
            result.RequestedBy      = Util.GetValueOfString(r["RequestedBy"]);
            result.TransferTypeCode = Util.GetValueOfString(r["TransferTypeCode"]);

            // ----- KPI aggregates -----
            result.LineCount         = Util.GetValueOfInt(r["LineCount"]);
            result.TransferQty       = Util.GetValueOfDecimal(r["TransferQty"]);
            result.TransferValue     = Util.GetValueOfDecimal(r["TransferValue"]);
            result.ConfirmationCount = Util.GetValueOfInt(r["ConfirmationCount"]);

            // ----- Origin: derived from the linked source ids on the lines -----
            result.HasRequisition = Util.GetValueOfInt(r["SampleRequisitionLineID"]) > 0;
            result.HasWorkOrder   = Util.GetValueOfInt(r["SampleWorkOrderID"]) > 0;

            // Transfer value is expressed in the accounting currency; the panel
            // renders INR (₹) with standard 2-dp precision.
            result.StdPrecision = 2;

            // ----- Transfer lines -----
            result.Lines = LoadLines(M_Movement_ID, rateExpr, woExpr);

            return result;
        }

        /// <summary>
        /// Builds the unit-rate SQL expression from whichever optional cost
        /// columns exist on M_MovementLine, ending at 0.
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
        /// Loads M_MovementLine rows for the transfer with product, from/to
        /// locator and UOM metadata, movement quantity, a unit rate / line value
        /// and the linked source ids used to classify per-line origin. Child of an
        /// already authorized movement, so no separate MRole filter is applied.
        /// </summary>
        /// <param name="M_Movement_ID">Owning stock movement id.</param>
        /// <param name="rateExpr">Unit-rate SQL expression (schema-aware).</param>
        /// <param name="woExpr">Work-order-id SQL expression (schema-aware).</param>
        /// <returns>Ordered list of line rows (may be empty).</returns>
        private List<MaterialTransferLineData> LoadLines(
            int M_Movement_ID, string rateExpr, string woExpr)
        {
            List<MaterialTransferLineData> lines = new List<MaterialTransferLineData>();

            string sql = @"SELECT
                              l.M_MovementLine_ID,
                              l.Line,
                              l.Description     AS LineDescription,
                              l.M_Product_ID,
                              p.Value           AS ProductCode,
                              p.Name            AS ProductName,
                              from_loc.Value    AS FromLocatorCode,
                              COALESCE(from_loc.LocatorCombination, from_loc.Bin, from_loc.Value) AS FromLocatorName,
                              to_loc.Value      AS ToLocatorCode,
                              COALESCE(to_loc.LocatorCombination, to_loc.Bin, to_loc.Value)       AS ToLocatorName,
                              u.Name            AS UOMName,
                              NVL(u.StdPrecision, 0) AS UOMPrecision,
                              NVL(l.MovementQty, 0) AS MovementQty,
                              " + rateExpr + @"                        AS UnitRate,
                              NVL(l.MovementQty, 0) * " + rateExpr + @" AS LineValue,
                              l.M_RequisitionLine_ID AS RequisitionLineID,
                              " + woExpr + @"                          AS WorkOrderID
                           FROM M_MovementLine l
                           LEFT OUTER JOIN M_Product p     ON (p.M_Product_ID    = l.M_Product_ID)
                           LEFT OUTER JOIN C_UOM     u     ON (u.C_UOM_ID         = l.C_UOM_ID)
                           LEFT OUTER JOIN M_Locator from_loc ON (from_loc.M_Locator_ID = l.M_Locator_ID)
                           LEFT OUTER JOIN M_Locator to_loc   ON (to_loc.M_Locator_ID   = l.M_LocatorTo_ID)
                           WHERE l.M_Movement_ID = @M_Movement_ID
                             AND l.IsActive      = 'Y'
                           ORDER BY l.Line";

            SqlParameter[] param = new SqlParameter[]
            {
                new SqlParameter("@M_Movement_ID", M_Movement_ID)
            };

            DataSet ds = DB.ExecuteDataset(sql, param, null);
            if (ds == null || ds.Tables.Count == 0)
                return lines;

            foreach (DataRow r in ds.Tables[0].Rows)
            {
                MaterialTransferLineData ln = new MaterialTransferLineData();
                ln.M_MovementLine_ID = Util.GetValueOfInt(r["M_MovementLine_ID"]);
                ln.Line              = Util.GetValueOfInt(r["Line"]);
                ln.Description       = Util.GetValueOfString(r["LineDescription"]);
                ln.M_Product_ID      = Util.GetValueOfInt(r["M_Product_ID"]);
                ln.ProductCode       = Util.GetValueOfString(r["ProductCode"]);
                ln.ProductName       = Util.GetValueOfString(r["ProductName"]);
                ln.FromLocatorCode   = Util.GetValueOfString(r["FromLocatorCode"]);
                ln.FromLocatorName   = Util.GetValueOfString(r["FromLocatorName"]);
                ln.ToLocatorCode     = Util.GetValueOfString(r["ToLocatorCode"]);
                ln.ToLocatorName     = Util.GetValueOfString(r["ToLocatorName"]);
                ln.UOMName           = Util.GetValueOfString(r["UOMName"]);
                ln.UOMPrecision      = Util.GetValueOfInt(r["UOMPrecision"]);
                ln.MovementQty       = Util.GetValueOfDecimal(r["MovementQty"]);
                ln.UnitRate          = Util.GetValueOfDecimal(r["UnitRate"]);
                ln.LineValue         = Util.GetValueOfDecimal(r["LineValue"]);
                ln.RequisitionLineID = Util.GetValueOfInt(r["RequisitionLineID"]);
                ln.WorkOrderID       = Util.GetValueOfInt(r["WorkOrderID"]);

                lines.Add(ln);
            }
            return lines;
        }

        /// <summary>
        /// Returns true when the given column exists on the given table, using
        /// the AD_Column dictionary. A DB issue degrades to "absent" (false) so a
        /// lookup failure never breaks the overview query. Also used to probe for
        /// the presence of an optional table (via one of its columns).
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

        public class MaterialTransferLineData
        {
            public int      M_MovementLine_ID { get; set; }
            public int      Line              { get; set; }
            public string   Description       { get; set; }
            public int      M_Product_ID      { get; set; }
            public string   ProductCode       { get; set; }   // SKU
            public string   ProductName       { get; set; }
            public string   FromLocatorCode   { get; set; }
            public string   FromLocatorName   { get; set; }
            public string   ToLocatorCode     { get; set; }
            public string   ToLocatorName     { get; set; }
            public string   UOMName           { get; set; }
            public int      UOMPrecision      { get; set; }
            public decimal  MovementQty       { get; set; }
            public decimal  UnitRate          { get; set; }
            public decimal  LineValue         { get; set; }
            public int      RequisitionLineID { get; set; }
            public int      WorkOrderID       { get; set; }
        }

        public class MaterialTransferOverviewData
        {
            // Header / identity
            public int       M_Movement_ID     { get; set; }
            public string    DocumentNo        { get; set; }
            public string    StatusCode        { get; set; }   // DocStatus code
            public bool      Processed         { get; set; }
            public bool      Posted            { get; set; }
            public DateTime? MovementDate      { get; set; }
            public string    Description       { get; set; }
            public string    FromWarehouseName { get; set; }
            public string    ToWarehouseName   { get; set; }
            public string    RequestedBy       { get; set; }
            public string    TransferTypeCode  { get; set; }   // INTER | INTRA

            // Origin (derived from the linked source ids on the lines)
            public bool      HasRequisition { get; set; }
            public bool      HasWorkOrder   { get; set; }

            // Currency
            public int       StdPrecision   { get; set; }

            // KPI aggregates
            public int       LineCount         { get; set; }
            public decimal   TransferQty       { get; set; }
            public decimal   TransferValue     { get; set; }
            public int       ConfirmationCount { get; set; }

            // Collections
            public List<MaterialTransferLineData> Lines { get; set; }
        }
    }
}
