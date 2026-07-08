/// <summary>
/// Module Name : VASLogic
/// Purpose     : Inventory Count Overview tab panel data (read side). Returns
///               header identity, warehouse, KPI aggregates (counted value /
///               net variance qty / variance-line count / total lines), the
///               matched / short / excess roll-ups and the count lines for a
///               selected physical inventory count (M_Inventory where
///               IsInternalUse is not 'Y'). Book-vs-counted variance is derived
///               from the line quantities.
/// Chronological development:
///   VAI163   2026-07-06  Created. Optional module columns (CurrentCostPrice,
///                        VA024_UnitPrice) are guarded through AD_Column so the
///                        panel works whether or not those modules are installed.
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
    public class VAS_101_OverviewInventoryCountModel
    {
        private static readonly VLogger _log =
            VLogger.GetVLogger(typeof(VAS_101_OverviewInventoryCountModel).FullName);

        /// <summary>
        /// Returns the full overview payload for the selected inventory count.
        /// MRole access filtering is applied ONLY on the main physical table
        /// (M_Inventory alias "inv"); the child line query inherits the parent's
        /// authorization and is not separately role-filtered.
        /// </summary>
        /// <param name="ctx">User context (client/org/role).</param>
        /// <param name="M_Inventory_ID">Selected inventory count id.</param>
        /// <returns>Populated <see cref="InventoryCountOverviewData"/>; an empty
        /// instance when the id is invalid or no accessible row is found.</returns>
        public InventoryCountOverviewData GetInventoryCountOverview(Ctx ctx, int M_Inventory_ID)
        {
            InventoryCountOverviewData result = new InventoryCountOverviewData();
            if (M_Inventory_ID <= 0) return result;

            // Optional module columns — resolved once so the SQL below only
            // references columns that actually exist in this schema.
            bool hasCurrentCost = ColumnExists("M_InventoryLine", "CurrentCostPrice");
            bool hasUnitPrice   = ColumnExists("M_InventoryLine", "VA024_UnitPrice");

            // COALESCE([l.CurrentCostPrice], [l.VA024_UnitPrice], 0)
            string rateExpr = BuildRateExpr(hasCurrentCost, hasUnitPrice);
            // COALESCE(l.DifferenceQty, QtyCount - QtyBook) — variance per line.
            const string varExpr =
                "COALESCE(l.DifferenceQty, COALESCE(l.QtyCount, 0) - COALESCE(l.QtyBook, 0))";

            string sql = @"SELECT
                              inv.M_Inventory_ID,
                              inv.DocumentNo,
                              inv.DocStatus,
                              inv.Processed,
                              inv.Posted,
                              inv.MovementDate,
                              inv.Description,
                              wh.Name          AS WarehouseName,
                              creator.Name     AS CountedBy,
                              (SELECT COUNT(*)
                                 FROM M_InventoryLine l
                                WHERE l.M_Inventory_ID = inv.M_Inventory_ID
                                  AND l.IsActive       = 'Y')                   AS LineCount,
                              (SELECT NVL(SUM(NVL(l.QtyCount, 0) * " + rateExpr + @"), 0)
                                 FROM M_InventoryLine l
                                WHERE l.M_Inventory_ID = inv.M_Inventory_ID
                                  AND l.IsActive       = 'Y')                   AS TotalValue,
                              (SELECT NVL(SUM(" + varExpr + @"), 0)
                                 FROM M_InventoryLine l
                                WHERE l.M_Inventory_ID = inv.M_Inventory_ID
                                  AND l.IsActive       = 'Y')                   AS NetVarianceQty,
                              (SELECT NVL(SUM(CASE WHEN " + varExpr + @" = 0 THEN 1 ELSE 0 END), 0)
                                 FROM M_InventoryLine l
                                WHERE l.M_Inventory_ID = inv.M_Inventory_ID
                                  AND l.IsActive       = 'Y')                   AS MatchedCount,
                              (SELECT NVL(SUM(CASE WHEN " + varExpr + @" < 0 THEN 1 ELSE 0 END), 0)
                                 FROM M_InventoryLine l
                                WHERE l.M_Inventory_ID = inv.M_Inventory_ID
                                  AND l.IsActive       = 'Y')                   AS ShortCount,
                              (SELECT NVL(SUM(CASE WHEN " + varExpr + @" > 0 THEN 1 ELSE 0 END), 0)
                                 FROM M_InventoryLine l
                                WHERE l.M_Inventory_ID = inv.M_Inventory_ID
                                  AND l.IsActive       = 'Y')                   AS ExcessCount
                            FROM M_Inventory inv
                            LEFT OUTER JOIN M_Warehouse wh   ON (inv.M_Warehouse_ID = wh.M_Warehouse_ID)
                            LEFT OUTER JOIN AD_User creator  ON (inv.CreatedBy      = creator.AD_User_ID)
                            WHERE inv.M_Inventory_ID = @M_Inventory_ID
                              AND inv.IsActive       = 'Y'
                              AND COALESCE(inv.IsInternalUse, 'N') <> 'Y'";

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
            result.CountDate      = Util.GetValueOfDateTime(r["MovementDate"]);
            result.Description    = Util.GetValueOfString(r["Description"]);
            result.WarehouseName  = Util.GetValueOfString(r["WarehouseName"]);
            result.CountedBy      = Util.GetValueOfString(r["CountedBy"]);

            // ----- KPI aggregates -----
            result.LineCount      = Util.GetValueOfInt(r["LineCount"]);
            result.TotalValue     = Util.GetValueOfDecimal(r["TotalValue"]);
            result.NetVarianceQty = Util.GetValueOfDecimal(r["NetVarianceQty"]);
            result.MatchedCount   = Util.GetValueOfInt(r["MatchedCount"]);
            result.ShortCount     = Util.GetValueOfInt(r["ShortCount"]);
            result.ExcessCount    = Util.GetValueOfInt(r["ExcessCount"]);
            result.VarianceLineCount = result.ShortCount + result.ExcessCount;

            // Physical count value is expressed in the accounting currency; the
            // panel renders INR (₹) with standard 2-dp precision.
            result.StdPrecision = 2;

            // ----- Count lines -----
            result.Lines = LoadLines(M_Inventory_ID, rateExpr, varExpr);

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
        /// Loads M_InventoryLine rows for the count with product, locator and UOM
        /// metadata, system (book) / counted quantities, the derived variance and
        /// a unit rate / line value. Child of an already authorized count, so no
        /// separate MRole filter is applied here.
        /// </summary>
        /// <param name="M_Inventory_ID">Owning inventory count id.</param>
        /// <param name="rateExpr">Unit-rate SQL expression (schema-aware).</param>
        /// <param name="varExpr">Variance SQL expression.</param>
        /// <returns>Ordered list of line rows (may be empty).</returns>
        private List<InventoryCountLineData> LoadLines(
            int M_Inventory_ID, string rateExpr, string varExpr)
        {
            List<InventoryCountLineData> lines = new List<InventoryCountLineData>();

            string sql = @"SELECT
                              l.M_InventoryLine_ID,
                              l.Line,
                              l.Description     AS LineDescription,
                              l.M_Product_ID,
                              NVL(l.QtyBook, 0)  AS SystemQty,
                              NVL(l.QtyCount, 0) AS CountedQty,
                              " + varExpr + @"                       AS VarianceQty,
                              p.Value           AS ProductCode,
                              p.Name            AS ProductName,
                              loc.Value         AS LocatorCode,
                              COALESCE(loc.LocatorCombination, loc.Bin, loc.Value) AS LocatorName,
                              u.Name            AS UOMName,
                              NVL(u.StdPrecision, 0) AS UOMPrecision,
                              " + rateExpr + @"                       AS UnitRate,
                              NVL(l.QtyCount, 0) * " + rateExpr + @"   AS LineValue
                           FROM M_InventoryLine l
                           LEFT OUTER JOIN M_Product p   ON (p.M_Product_ID   = l.M_Product_ID)
                           LEFT OUTER JOIN C_UOM     u   ON (u.C_UOM_ID        = l.C_UOM_ID)
                           LEFT OUTER JOIN M_Locator loc ON (loc.M_Locator_ID  = l.M_Locator_ID)
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
                InventoryCountLineData ln = new InventoryCountLineData();
                ln.M_InventoryLine_ID = Util.GetValueOfInt(r["M_InventoryLine_ID"]);
                ln.Line               = Util.GetValueOfInt(r["Line"]);
                ln.Description        = Util.GetValueOfString(r["LineDescription"]);
                ln.M_Product_ID       = Util.GetValueOfInt(r["M_Product_ID"]);
                ln.SystemQty          = Util.GetValueOfDecimal(r["SystemQty"]);
                ln.CountedQty         = Util.GetValueOfDecimal(r["CountedQty"]);
                ln.VarianceQty        = Util.GetValueOfDecimal(r["VarianceQty"]);
                ln.ProductCode        = Util.GetValueOfString(r["ProductCode"]);
                ln.ProductName        = Util.GetValueOfString(r["ProductName"]);
                ln.LocatorCode        = Util.GetValueOfString(r["LocatorCode"]);
                ln.LocatorName        = Util.GetValueOfString(r["LocatorName"]);
                ln.UOMName            = Util.GetValueOfString(r["UOMName"]);
                ln.UOMPrecision       = Util.GetValueOfInt(r["UOMPrecision"]);
                ln.UnitRate           = Util.GetValueOfDecimal(r["UnitRate"]);
                ln.LineValue          = Util.GetValueOfDecimal(r["LineValue"]);

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

        public class InventoryCountLineData
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
            public decimal  SystemQty          { get; set; }   // QtyBook
            public decimal  CountedQty         { get; set; }   // QtyCount
            public decimal  VarianceQty        { get; set; }   // counted - system
            public decimal  UnitRate           { get; set; }
            public decimal  LineValue          { get; set; }
        }

        public class InventoryCountOverviewData
        {
            // Header / identity
            public int       M_Inventory_ID { get; set; }
            public string    DocumentNo     { get; set; }
            public string    StatusCode     { get; set; }   // DocStatus code
            public bool      Processed      { get; set; }
            public bool      Posted         { get; set; }
            public DateTime? CountDate      { get; set; }   // MovementDate
            public string    Description    { get; set; }
            public string    WarehouseName  { get; set; }
            public string    CountedBy      { get; set; }

            // Currency
            public int       StdPrecision   { get; set; }

            // KPI aggregates
            public int       LineCount        { get; set; }
            public decimal   TotalValue       { get; set; }
            public decimal   NetVarianceQty   { get; set; }
            public int       MatchedCount     { get; set; }
            public int       ShortCount       { get; set; }
            public int       ExcessCount      { get; set; }
            public int       VarianceLineCount { get; set; }

            // Collections
            public List<InventoryCountLineData> Lines { get; set; }
        }
    }
}
