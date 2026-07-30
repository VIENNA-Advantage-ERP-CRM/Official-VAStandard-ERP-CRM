/// <summary>
/// Module Name : VASLogic
/// Purpose     : Purchase Requisition overview tab panel data (read side).
///               Returns header identity, requester / preparer, origin +
///               warehouse route, budget-aware stat data, a 6-stage progress
///               model, line items with source-stock availability, a typed
///               activity feed and notes for a selected requisition
///               (M_Requisition) — consumed by the
///               VAS.VAS_098_PurchaseRequisition tab panel.
/// Chronological development:
///   VAI163   2026-07-01  Created. Core header / line queries use only standard
///                        M_Requisition(Line) columns; the custom columns from
///                        the verified mapping (DTD001_MWarehouseSource_ID,
///                        IsBudgetBreach / VAS_BudgetBreach, reserved / ordered /
///                        available-budget line columns) are loaded in guarded
///                        enrichment passes so a missing column degrades the
///                        affected section rather than breaking the panel.
///   VAI163   2026-07-02  Currency now sourced from the requisition's price list
///                        (M_PriceList.C_Currency_ID) instead of M_Requisition.
///   VAI163   2026-07-28  Source warehouse (DTD001_MWarehouseSource_ID) is read in
///                        its own dictionary-guarded query. It previously shared a
///                        SELECT with the budget-breach columns, so a schema
///                        missing either of those failed the whole statement and
///                        the warehouse came back empty — the panel then showed
///                        "External Procurement" and the wrong procurement type.
///   VAI163   2026-07-28  Lines now carry the Attribute Set Instance description
///                        (guarded on M_RequisitionLine.M_AttributeSetInstance_ID,
///                        joined only for a real instance id). Dropped the derived
///                        Contingency total — the panel no longer shows it.
///   VAI163   2026-07-28  - Source stock is now real on-hand stock: M_Storage
///                          .QtyOnHand summed over the locators of the source
///                          warehouse (DTD001_MWarehouseSource_ID), matched on the
///                          line's product and its attribute set instance when it
///                          has one. It used to be M_RequisitionLine reserved qty.
///                        - LoadLineExtras guards each optional column separately.
///                          QtyOrdered is not a standard M_RequisitionLine column,
///                          so the single combined SELECT was failing outright and
///                          silently zeroing reserved / ordered / budget for every
///                          line — which also kept the In Fulfilment stage dark.
///                        - Added the progress milestones (completed / converted /
///                          in-fulfilment / closed), derived from the requisition's
///                          workflow and from the purchase orders reached through
///                          M_RequisitionLine.C_OrderLine_ID.
///   VAI163   2026-07-28  - Activity now carries the downstream lifecycle: the
///                          purchase orders raised from the requisition and the
///                          goods receipts booked against them (created and
///                          completed), each with its document no and timestamp.
///                        - AvailableBudget sums DISTINCT line amounts. Every line
///                          on the same budget control carries that control's
///                          available budget, so adding them multiplied one budget
///                          by the line count.
/// </summary>

using System;
using System.Collections.Generic;
using System.Data;
using System.Data.SqlClient;
using VAdvantage.DataBase;
using VAdvantage.Logging;
using VAdvantage.Model;
using VAdvantage.Utility;

namespace VASLogic.Models
{
    public class VAS_098_PurchaseRequisitionModel
    {
        private static readonly VLogger _log =
            VLogger.GetVLogger(typeof(VAS_098_PurchaseRequisitionModel).FullName);

        /// <summary>
        /// Returns the full overview payload for the selected requisition.
        /// MRole access filtering is applied only on the main physical table
        /// (M_Requisition alias "r"); child line / activity queries inherit the
        /// parent's authorization.
        /// </summary>
        /// <param name="ctx">User context (client/org/role).</param>
        /// <param name="M_Requisition_ID">Selected requisition id.</param>
        /// <returns>Populated <see cref="RequisitionOverviewData"/>; an empty
        /// instance when the id is invalid or no accessible row is found.</returns>
        public RequisitionOverviewData GetRequisitionOverview(Ctx ctx, int M_Requisition_ID)
        {
            RequisitionOverviewData result = new RequisitionOverviewData();
            if (M_Requisition_ID <= 0) return result;

            if (!LoadHeader(ctx, M_Requisition_ID, result))
                return result;                       // invalid / no access -> empty

            LoadHeaderExtras(M_Requisition_ID, result);   // guarded custom columns

            result.Lines = LoadLines(M_Requisition_ID);
            LoadLineExtras(M_Requisition_ID, result.Lines); // guarded custom columns
            // Real on-hand stock at the source warehouse (needs the id resolved by
            // LoadHeaderExtras above).
            LoadSourceStock(M_Requisition_ID, result.SourceWarehouseId, result.Lines);

            RollUpTotals(result);

            LoadMilestones(M_Requisition_ID, result);

            result.Activity = LoadActivity(M_Requisition_ID, result);

            return result;
        }

        // ----------------------------------------------------------------- //
        //  Header                                                            //
        // ----------------------------------------------------------------- //

        /// <summary>
        /// Loads the requisition header using only standard M_Requisition columns
        /// (so identity always renders). Returns false when no accessible row is
        /// found.
        /// </summary>
        private bool LoadHeader(Ctx ctx, int M_Requisition_ID, RequisitionOverviewData d)
        {
            // Posted is a standard M_Requisition column, but this query is the one
            // that must never fail, so it is included only when the dictionary
            // confirms it.
            bool hasPosted = ColumnExists("M_Requisition", "Posted");
            string postedExpr = hasPosted ? "r.Posted" : "'N'";

            string sql = @"SELECT
                              r.M_Requisition_ID,
                              r.DocumentNo,
                              r.DocStatus,
                              r.PriorityRule,
                              r.DateDoc,
                              r.DateRequired,
                              r.Description,
                              r.TotalLines,
                              r.Processed,
                              " + postedExpr + @" AS Posted,
                              r.Created,
                              r.Updated,
                              requester.Name  AS RequesterName,
                              preparer.Name   AS PreparerName,
                              cu.Name         AS CreatedByName,
                              uu.Name         AS UpdatedByName,
                              reqwh.Name      AS RequestWarehouseName,
                              pl.Name         AS PriceListName,
                              cur.CurSymbol   AS CurSymbol,
                              cur.ISO_Code    AS ISO_Code,
                              cur.StdPrecision AS StdPrecision,
                              (SELECT COUNT(*)
                                 FROM M_RequisitionLine rl
                                WHERE rl.M_Requisition_ID = r.M_Requisition_ID
                                  AND rl.IsActive = 'Y'
                                  AND rl.C_OrderLine_ID IS NOT NULL)  AS ConvertedLineCount,
                              TRUNC(CURRENT_DATE) AS SystemDate
                           FROM M_Requisition r
                           LEFT OUTER JOIN C_BPartner requester ON (requester.C_BPartner_ID = r.C_BPartner_ID)
                           LEFT OUTER JOIN AD_User preparer     ON (preparer.AD_User_ID     = r.AD_User_ID)
                           LEFT OUTER JOIN AD_User cu           ON (cu.AD_User_ID           = r.CreatedBy)
                           LEFT OUTER JOIN AD_User uu           ON (uu.AD_User_ID           = r.UpdatedBy)
                           LEFT OUTER JOIN M_Warehouse reqwh    ON (reqwh.M_Warehouse_ID    = r.M_Warehouse_ID)
                           LEFT OUTER JOIN M_PriceList pl       ON (pl.M_PriceList_ID       = r.M_PriceList_ID)
                           -- VAI163 2026-07-02  Currency is taken from the requisition's
                           -- price list (M_PriceList.C_Currency_ID), not M_Requisition.
                           LEFT OUTER JOIN C_Currency cur       ON (cur.C_Currency_ID       = pl.C_Currency_ID)
                           WHERE r.M_Requisition_ID = @M_Requisition_ID
                             AND r.IsActive = 'Y'";

            sql = MRole.GetDefault(ctx).AddAccessSQL(
                sql, "r", MRole.SQL_FULLYQUALIFIED, MRole.SQL_RO);

            DataSet ds = DB.ExecuteDataset(sql, ReqParam(M_Requisition_ID), null);
            if (ds == null || ds.Tables.Count == 0 || ds.Tables[0].Rows.Count == 0)
                return false;

            DataRow r = ds.Tables[0].Rows[0];
            d.M_Requisition_ID    = Util.GetValueOfInt(r["M_Requisition_ID"]);
            d.DocumentNo          = Util.GetValueOfString(r["DocumentNo"]);
            d.StatusCode          = Util.GetValueOfString(r["DocStatus"]);
            d.PriorityCode        = Util.GetValueOfString(r["PriorityRule"]);
            d.DateDoc             = Util.GetValueOfDateTime(r["DateDoc"]);
            d.DateRequired        = Util.GetValueOfDateTime(r["DateRequired"]);
            d.Description         = Util.GetValueOfString(r["Description"]);
            d.EstimatedValue      = Util.GetValueOfDecimal(r["TotalLines"]);
            d.Processed           = Util.GetValueOfString(r["Processed"]) == "Y";
            // Posted is a status list on some schemas ('Y' posted, 'N' not, 'E'
            // error ...); anything other than 'Y' is "not posted" for the badge,
            // and the raw code travels with it so the panel can flag an error.
            d.PostedCode          = Util.GetValueOfString(r["Posted"]);
            d.Posted              = d.PostedCode == "Y";
            d.Created             = Util.GetValueOfDateTime(r["Created"]);
            d.Updated             = Util.GetValueOfDateTime(r["Updated"]);
            d.RequesterName       = Util.GetValueOfString(r["RequesterName"]);
            d.PreparerName        = Util.GetValueOfString(r["PreparerName"]);
            d.CreatedByName       = Util.GetValueOfString(r["CreatedByName"]);
            d.UpdatedByName       = Util.GetValueOfString(r["UpdatedByName"]);
            d.RequestWarehouseName = Util.GetValueOfString(r["RequestWarehouseName"]);
            d.PriceListName       = Util.GetValueOfString(r["PriceListName"]);
            d.CurSymbol           = Util.GetValueOfString(r["CurSymbol"]);
            d.ISO_Code            = Util.GetValueOfString(r["ISO_Code"]);
            d.StdPrecision        = Util.GetValueOfInt(r["StdPrecision"]);
            d.ConvertedLineCount  = Util.GetValueOfInt(r["ConvertedLineCount"]);
            d.SystemDate          = Util.GetValueOfDateTime(r["SystemDate"]);
            return true;
        }

        /// <summary>
        /// Loads the header's custom columns (source warehouse + budget-breach
        /// flags).
        ///
        /// These used to share one SELECT, which meant a schema missing ANY of the
        /// three lost all three: the statement failed, the catch swallowed it, and
        /// the source warehouse silently came back empty — so the panel fell
        /// through to its "External Procurement" wording and mislabelled the
        /// procurement type. Each column is now checked against the dictionary and
        /// read independently, so one absent column only drops its own value.
        /// </summary>
        private void LoadHeaderExtras(int M_Requisition_ID, RequisitionOverviewData d)
        {
            LoadSourceWarehouse(M_Requisition_ID, d);
            LoadBudgetBreach(M_Requisition_ID, d);
        }

        /// <summary>
        /// Reads the requisition's source warehouse from
        /// M_Requisition.DTD001_MWarehouseSource_ID. Skipped when that custom
        /// column is not present in this schema.
        /// </summary>
        private void LoadSourceWarehouse(int M_Requisition_ID, RequisitionOverviewData d)
        {
            if (!ColumnExists("M_Requisition", "DTD001_MWarehouseSource_ID")) return;

            try
            {
                string sql = @"SELECT r.DTD001_MWarehouseSource_ID AS SourceWarehouseId,
                                      srcwh.Name AS SourceWarehouseName
                                 FROM M_Requisition r
                                 LEFT OUTER JOIN M_Warehouse srcwh
                                        ON (srcwh.M_Warehouse_ID = r.DTD001_MWarehouseSource_ID)
                                WHERE r.M_Requisition_ID = @M_Requisition_ID";
                DataSet ds = DB.ExecuteDataset(sql, ReqParam(M_Requisition_ID), null);
                if (ds == null || ds.Tables.Count == 0 || ds.Tables[0].Rows.Count == 0) return;

                DataRow row = ds.Tables[0].Rows[0];
                d.SourceWarehouseId   = Util.GetValueOfInt(row["SourceWarehouseId"]);
                d.SourceWarehouseName = Util.GetValueOfString(row["SourceWarehouseName"]);
            }
            catch (Exception ex)
            {
                _log.Severe("LoadSourceWarehouse (M_Requisition_ID=" + M_Requisition_ID + "): " + ex.Message);
            }
        }

        /// <summary>
        /// Reads the budget-breach flag and note, each only when its column exists.
        /// </summary>
        private void LoadBudgetBreach(int M_Requisition_ID, RequisitionOverviewData d)
        {
            bool hasFlag = ColumnExists("M_Requisition", "IsBudgetBreach");
            bool hasNote = ColumnExists("M_Requisition", "VAS_BudgetBreach");
            if (!hasFlag && !hasNote) return;

            try
            {
                List<string> cols = new List<string>();
                cols.Add(hasFlag ? "COALESCE(r.IsBudgetBreach, 'N') AS IsBudgetBreach"
                                 : "'N' AS IsBudgetBreach");
                cols.Add(hasNote ? "r.VAS_BudgetBreach AS BudgetBreachNote"
                                 : "CAST(NULL AS VARCHAR(255)) AS BudgetBreachNote");

                string sql = "SELECT " + string.Join(", ", cols.ToArray()) +
                             @" FROM M_Requisition r
                                WHERE r.M_Requisition_ID = @M_Requisition_ID";
                DataSet ds = DB.ExecuteDataset(sql, ReqParam(M_Requisition_ID), null);
                if (ds == null || ds.Tables.Count == 0 || ds.Tables[0].Rows.Count == 0) return;

                DataRow r = ds.Tables[0].Rows[0];
                d.IsBudgetBreach   = Util.GetValueOfString(r["IsBudgetBreach"]) == "Y";
                d.BudgetBreachNote = Util.GetValueOfString(r["BudgetBreachNote"]);
            }
            catch (Exception ex)
            {
                _log.Severe("LoadBudgetBreach (M_Requisition_ID=" + M_Requisition_ID + "): " + ex.Message);
            }
        }

        /// <summary>
        /// Returns true when the given column exists on the given table, using the
        /// AD_Column dictionary. A DB issue degrades to "absent" (false), which just
        /// drops the optional value that depends on it.
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
        //  Lines                                                             //
        // ----------------------------------------------------------------- //

        /// <summary>
        /// Loads requisition lines using standard columns only (product +
        /// category + UOM + requested qty + price + line amount). Child of an
        /// already-authorized requisition, so no separate MRole filter.
        /// </summary>
        private List<RequisitionLineData> LoadLines(int M_Requisition_ID)
        {
            List<RequisitionLineData> lines = new List<RequisitionLineData>();

            // The attribute set instance (lot / serial / size ...) is only read
            // when the line actually carries the column, and only joined for a real
            // instance id — 0 is "no attributes", not a row to look up.
            bool hasAsi = ColumnExists("M_RequisitionLine", "M_AttributeSetInstance_ID");
            string asiExpr = hasAsi
                ? "asi.Description"
                : "CAST(NULL AS VARCHAR(255))";
            string asiJoin = hasAsi
                ? @"LEFT OUTER JOIN M_AttributeSetInstance asi
                           ON (asi.M_AttributeSetInstance_ID = rl.M_AttributeSetInstance_ID
                               AND rl.M_AttributeSetInstance_ID > 0)"
                : "";

            string sql = @"SELECT
                              rl.M_RequisitionLine_ID,
                              rl.Line,
                              rl.Qty,
                              rl.PriceActual,
                              rl.LineNetAmt,
                              rl.Description AS LineDescription,
                              rl.M_Product_ID,
                              rl.C_Charge_ID,
                              rl.C_OrderLine_ID,
                              p.Name    AS ProductName,
                              p.Value   AS ProductValue,
                              pcat.Name AS CategoryName,
                              ch.Name   AS ChargeName,
                              uom.Name  AS UOMName,
                              uom.StdPrecision AS UOMPrecision,
                              " + asiExpr + @" AS AttributeSetInstance
                           FROM M_RequisitionLine rl
                           LEFT OUTER JOIN M_Product p            ON (p.M_Product_ID = rl.M_Product_ID)
                           LEFT OUTER JOIN M_Product_Category pcat ON (pcat.M_Product_Category_ID = p.M_Product_Category_ID)
                           LEFT OUTER JOIN C_Charge ch            ON (ch.C_Charge_ID = rl.C_Charge_ID)
                           LEFT OUTER JOIN C_UOM uom              ON (uom.C_UOM_ID = rl.C_UOM_ID)
                           " + asiJoin + @"
                           WHERE rl.M_Requisition_ID = @M_Requisition_ID
                             AND rl.IsActive = 'Y'
                           ORDER BY rl.Line";

            DataSet ds = DB.ExecuteDataset(sql, ReqParam(M_Requisition_ID), null);
            if (ds == null || ds.Tables.Count == 0) return lines;

            foreach (DataRow r in ds.Tables[0].Rows)
            {
                RequisitionLineData ln = new RequisitionLineData();
                ln.M_RequisitionLine_ID = Util.GetValueOfInt(r["M_RequisitionLine_ID"]);
                ln.Line          = Util.GetValueOfInt(r["Line"]);
                ln.RequestedQty  = Util.GetValueOfDecimal(r["Qty"]);
                ln.UnitPrice     = Util.GetValueOfDecimal(r["PriceActual"]);
                ln.LineAmount    = Util.GetValueOfDecimal(r["LineNetAmt"]);
                ln.Description   = Util.GetValueOfString(r["LineDescription"]);
                ln.M_Product_ID  = Util.GetValueOfInt(r["M_Product_ID"]);
                ln.C_Charge_ID   = Util.GetValueOfInt(r["C_Charge_ID"]);
                ln.IsConverted   = Util.GetValueOfInt(r["C_OrderLine_ID"]) > 0;
                ln.ProductName   = Util.GetValueOfString(r["ProductName"]);
                ln.ProductValue  = Util.GetValueOfString(r["ProductValue"]);
                ln.CategoryName  = Util.GetValueOfString(r["CategoryName"]);
                ln.ChargeName    = Util.GetValueOfString(r["ChargeName"]);
                ln.UOMName       = Util.GetValueOfString(r["UOMName"]);
                ln.UOMPrecision  = Util.GetValueOfInt(r["UOMPrecision"]);
                ln.AttributeSetInstance = Util.GetValueOfString(r["AttributeSetInstance"]);

                if (string.IsNullOrEmpty(ln.ProductName) && !string.IsNullOrEmpty(ln.ChargeName))
                    ln.ProductName = ln.ChargeName;

                // Line net amount falls back to qty * price when not stored.
                if (ln.LineAmount == 0 && ln.UnitPrice != 0)
                    ln.LineAmount = ln.RequestedQty * ln.UnitPrice;

                lines.Add(ln);
            }
            return lines;
        }

        /// <summary>
        /// Enriches the loaded lines with the custom reserved / ordered /
        /// available-budget / breach columns (source-stock + budget), keyed by
        /// line id. Guarded so a missing column simply leaves those unset (the
        /// UI then shows N/A source stock and no per-line breach).
        /// </summary>
        private void LoadLineExtras(int M_Requisition_ID, List<RequisitionLineData> lines)
        {
            if (lines == null || lines.Count == 0) return;
            try
            {
                // Every one of these is optional. They used to sit in a single
                // SELECT, so a schema without (say) M_RequisitionLine.QtyOrdered —
                // which is not a standard column — failed the whole statement and
                // silently zeroed the reserved / ordered / budget figures for every
                // line. Each is now included only when it actually exists.
                string reservedExpr = FirstExistingExpr("M_RequisitionLine",
                    new string[] { "DTD001_ReservedQty", "QtyReserved" }, "rl");
                string orderedExpr = FirstExistingExpr("M_RequisitionLine",
                    new string[] { "QtyOrdered", "DTD001_OrderedQty" }, "rl");
                string budgetExpr = FirstExistingExpr("M_RequisitionLine",
                    new string[] { "VAS_AvailableBudget" }, "rl");
                string breachExpr = ColumnExists("M_RequisitionLine", "IsBudgetBreach")
                    ? "COALESCE(rl.IsBudgetBreach, 'N')" : "'N'";

                string sql = @"SELECT rl.M_RequisitionLine_ID,
                                      " + reservedExpr + @" AS ReservedQty,
                                      " + orderedExpr  + @" AS OrderedQty,
                                      " + budgetExpr   + @" AS AvailableBudget,
                                      " + breachExpr   + @" AS IsBudgetBreach
                                 FROM M_RequisitionLine rl
                                WHERE rl.M_Requisition_ID = @M_Requisition_ID
                                  AND rl.IsActive = 'Y'";
                DataSet ds = DB.ExecuteDataset(sql, ReqParam(M_Requisition_ID), null);
                if (ds == null || ds.Tables.Count == 0) return;

                Dictionary<int, DataRow> byId = new Dictionary<int, DataRow>();
                foreach (DataRow r in ds.Tables[0].Rows)
                    byId[Util.GetValueOfInt(r["M_RequisitionLine_ID"])] = r;

                foreach (RequisitionLineData ln in lines)
                {
                    DataRow r;
                    if (!byId.TryGetValue(ln.M_RequisitionLine_ID, out r)) continue;
                    ln.ReservedQty     = Util.GetValueOfDecimal(r["ReservedQty"]);
                    ln.OrderedQty      = Util.GetValueOfDecimal(r["OrderedQty"]);
                    ln.AvailableBudget = Util.GetValueOfDecimal(r["AvailableBudget"]);
                    ln.IsBudgetBreach  = Util.GetValueOfString(r["IsBudgetBreach"]) == "Y";
                }
            }
            catch (Exception ex)
            {
                _log.Severe("LoadLineExtras (M_Requisition_ID=" + M_Requisition_ID + "): " + ex.Message);
            }
        }

        /// <summary>
        /// Returns "COALESCE(alias.Col, 0)" for the first candidate column that
        /// exists on the table, or the literal "0" when none of them do.
        /// </summary>
        private string FirstExistingExpr(string tableName, string[] candidates, string alias)
        {
            for (int i = 0; i < candidates.Length; i++)
            {
                if (ColumnExists(tableName, candidates[i]))
                    return "COALESCE(" + alias + "." + candidates[i] + ", 0)";
            }
            return "0";
        }

        /// <summary>
        /// Fills each line's on-hand quantity at the requisition's SOURCE warehouse
        /// (M_Requisition.DTD001_MWarehouseSource_ID).
        ///
        /// Stock is summed from M_Storage.QtyOnHand over every locator belonging to
        /// that warehouse, matched on the line's product and — when the line names
        /// one — its attribute set instance. A line with no attribute set instance
        /// sums across all instances of the product. A product with no stock row
        /// yields 0 rather than "unknown".
        ///
        /// Skipped entirely when the requisition has no source warehouse: that is
        /// external procurement, where source stock is genuinely not applicable and
        /// the panel should keep saying so instead of showing a misleading 0.
        /// </summary>
        /// <param name="M_Requisition_ID">Owning requisition id.</param>
        /// <param name="sourceWarehouseId">Source warehouse; 0 when not set.</param>
        /// <param name="lines">Lines to enrich, keyed by line id.</param>
        private void LoadSourceStock(int M_Requisition_ID, int sourceWarehouseId,
                                     List<RequisitionLineData> lines)
        {
            if (lines == null || lines.Count == 0 || sourceWarehouseId <= 0) return;

            try
            {
                // Match the instance only when the line actually carries one.
                string asiCond = ColumnExists("M_RequisitionLine", "M_AttributeSetInstance_ID")
                    ? @"(rl.M_AttributeSetInstance_ID IS NULL
                         OR rl.M_AttributeSetInstance_ID = 0
                         OR s.M_AttributeSetInstance_ID = rl.M_AttributeSetInstance_ID)"
                    : "1 = 1";

                string sql = @"SELECT rl.M_RequisitionLine_ID,
                                      NVL(SUM(s.QtyOnHand), 0) AS SourceQtyOnHand
                                 FROM M_RequisitionLine rl
                                 LEFT OUTER JOIN M_Storage s
                                        ON (s.M_Product_ID = rl.M_Product_ID
                                            AND s.M_Locator_ID IN (SELECT loc.M_Locator_ID
                                                                     FROM M_Locator loc
                                                                    WHERE loc.M_Warehouse_ID = @M_Warehouse_ID
                                                                      AND loc.IsActive = 'Y')
                                            AND " + asiCond + @")
                                WHERE rl.M_Requisition_ID = @M_Requisition_ID
                                  AND rl.IsActive = 'Y'
                                GROUP BY rl.M_RequisitionLine_ID";

                SqlParameter[] param = new SqlParameter[]
                {
                    new SqlParameter("@M_Requisition_ID", M_Requisition_ID),
                    new SqlParameter("@M_Warehouse_ID", sourceWarehouseId)
                };

                DataSet ds = DB.ExecuteDataset(sql, param, null);
                if (ds == null || ds.Tables.Count == 0) return;

                Dictionary<int, decimal> byId = new Dictionary<int, decimal>();
                foreach (DataRow r in ds.Tables[0].Rows)
                {
                    byId[Util.GetValueOfInt(r["M_RequisitionLine_ID"])] =
                        Util.GetValueOfDecimal(r["SourceQtyOnHand"]);
                }

                foreach (RequisitionLineData ln in lines)
                {
                    decimal onHand;
                    ln.SourceQtyOnHand = byId.TryGetValue(ln.M_RequisitionLine_ID, out onHand) ? onHand : 0;
                    // A source warehouse is configured, so the figure is meaningful
                    // even when it is zero.
                    ln.HasSourceData = true;
                    ln.SourceState = (ln.RequestedQty > 0 && ln.SourceQtyOnHand >= ln.RequestedQty)
                        ? "full" : "short";
                }
            }
            catch (Exception ex)
            {
                _log.Severe("LoadSourceStock (M_Requisition_ID=" + M_Requisition_ID + "): " + ex.Message);
            }
        }

        /// <summary>
        /// Rolls the loaded lines up into the header-level stat figures
        /// (line/unit counts, estimated subtotal / contingency, source
        /// availability, reserved-ordered progress, any-line breach).
        /// </summary>
        private void RollUpTotals(RequisitionOverviewData d)
        {
            List<RequisitionLineData> lines = d.Lines ?? new List<RequisitionLineData>();

            decimal subtotal = 0, requestedUnits = 0, reserved = 0, ordered = 0, availableBudget = 0;
            decimal sourceOnHand = 0;
            List<decimal> budgetAmounts = new List<decimal>();
            int fullyInStock = 0;
            bool anyLineHasSource = false;
            bool anyLineBreach = false;

            foreach (RequisitionLineData ln in lines)
            {
                subtotal       += ln.LineAmount;
                requestedUnits += ln.RequestedQty;
                reserved       += ln.ReservedQty;
                ordered        += ln.OrderedQty;
                // VAS_AvailableBudget is the budget available to THAT line's budget
                // control, so every line drawing on the same control carries the
                // same figure. Adding them up would multiply one budget by the line
                // count, so only distinct amounts contribute.
                if (ln.AvailableBudget != 0 && !budgetAmounts.Contains(ln.AvailableBudget))
                {
                    budgetAmounts.Add(ln.AvailableBudget);
                    availableBudget += ln.AvailableBudget;
                }
                sourceOnHand   += ln.SourceQtyOnHand;
                if (ln.HasSourceData)
                {
                    anyLineHasSource = true;
                    if (ln.RequestedQty > 0 && ln.SourceQtyOnHand >= ln.RequestedQty) fullyInStock++;
                }
                if (ln.IsBudgetBreach) anyLineBreach = true;
            }

            d.LineCount        = lines.Count;
            d.RequestedUnits   = requestedUnits;
            d.EstimatedSubtotal = subtotal;

            // The estimated total is the sum of the lines actually shown, not the
            // header's TotalLines. MRequisitionLine.UpdateHeader maintains
            // TotalLines as SUM(LineNetAmt) over EVERY line with no IsActive
            // filter, so a line that was deactivated — which is what happens when a
            // line is re-entered under a different unit of measure — stays in the
            // header figure forever and the total reads high. Deriving it here
            // keeps the KPI card and the footer equal to the rows on screen.
            // TotalLines survives only as the fallback for a requisition with no
            // active lines at all.
            if (lines.Count > 0) d.EstimatedValue = subtotal;
            else if (d.EstimatedValue == 0) d.EstimatedValue = subtotal;

            d.HasSourceData    = anyLineHasSource;
            d.FullyInStockLines = fullyInStock;
            d.SourceStockOnHand = sourceOnHand;
            d.ReservedUnits    = reserved;
            d.OrderedUnits     = ordered;
            d.AvailableBudget  = availableBudget;

            // Budget breach = header flag OR any line flag.
            d.IsBudgetBreach   = d.IsBudgetBreach || anyLineBreach;
            if (d.IsBudgetBreach && availableBudget > 0 && d.EstimatedValue > availableBudget)
                d.BudgetOverage = d.EstimatedValue - availableBudget;

            d.IsConverted      = d.ConvertedLineCount > 0;
            // HasOrdered is set from the linked purchase orders in LoadMilestones —
            // a per-line QtyOrdered column is not standard and cannot be relied on.
        }

        // ----------------------------------------------------------------- //
        //  Progress milestones                                               //
        // ----------------------------------------------------------------- //

        /// <summary>
        /// Fills the requisition-progress dates.
        ///
        ///   CompletedDate  — when the requisition itself was completed: the Created
        ///                    stamp of its workflow DocComplete activity, falling
        ///                    back to Updated for a CO/CL document completed outside
        ///                    the workflow engine.
        ///   ConvertedDate  — when the first purchase order referencing this
        ///                    requisition was created.
        ///   FulfilmentDate — when the first of those orders reached Completed.
        ///   ClosedDate     — the requisition's own close, or the last of its orders
        ///                    reaching Closed.
        ///
        /// Purchase orders are reached through M_RequisitionLine.C_OrderLine_ID,
        /// which is what the conversion process writes back.
        /// </summary>
        private void LoadMilestones(int M_Requisition_ID, RequisitionOverviewData d)
        {
            d.CompletedDate = GetRequisitionCompletedDate(M_Requisition_ID);
            if (!d.CompletedDate.HasValue &&
                (d.StatusCode == "CO" || d.StatusCode == "CL"))
            {
                d.CompletedDate = d.Updated;
            }

            LoadLinkedOrderMilestones(M_Requisition_ID, d);

            // The requisition's own close always wins as the closing moment.
            if (d.StatusCode == "CL")
            {
                d.IsClosed   = true;
                d.ClosedDate = d.Updated;
            }
        }

        /// <summary>
        /// The moment the requisition's workflow DocComplete activity ran, or null
        /// when it has none. Standalone query — never a subselect of the MRole-
        /// rewritten header SELECT.
        /// </summary>
        private DateTime? GetRequisitionCompletedDate(int M_Requisition_ID)
        {
            try
            {
                string sql = @"SELECT MAX(wfa.Created) AS CompletedDate
                                 FROM AD_WF_Process wfp
                                INNER JOIN AD_WF_Activity wfa
                                        ON (wfa.AD_WF_Process_ID = wfp.AD_WF_Process_ID)
                                INNER JOIN AD_WF_Node wfn
                                        ON (wfn.AD_WF_Node_ID = wfa.AD_WF_Node_ID)
                                INNER JOIN AD_Table adt
                                        ON (adt.AD_Table_ID = wfp.AD_Table_ID)
                                WHERE wfp.Record_ID = @M_Requisition_ID
                                  AND adt.TableName = 'M_Requisition'
                                  AND wfp.IsActive  = 'Y'
                                  AND wfa.IsActive  = 'Y'
                                  AND wfn.IsActive  = 'Y'
                                  AND wfa.WFState   = 'CC'
                                  AND UPPER(TRIM(wfn.Value)) IN ('DOCCOMPLETE', 'COMPLETE', '(DOCCOMPLETE)')";
                DataSet ds = DB.ExecuteDataset(sql, ReqParam(M_Requisition_ID), null);
                if (ds == null || ds.Tables.Count == 0 || ds.Tables[0].Rows.Count == 0)
                    return null;
                return Util.GetValueOfDateTime(ds.Tables[0].Rows[0]["CompletedDate"]);
            }
            catch (Exception ex)
            {
                _log.Severe("GetRequisitionCompletedDate (M_Requisition_ID=" + M_Requisition_ID + "): " + ex.Message);
                return null;
            }
        }

        /// <summary>
        /// Reads every purchase order raised from this requisition and derives the
        /// converted / in-fulfilment / closed milestones from them.
        /// </summary>
        private void LoadLinkedOrderMilestones(int M_Requisition_ID, RequisitionOverviewData d)
        {
            try
            {
                string sql = @"SELECT o.C_Order_ID,
                                      o.DocumentNo,
                                      o.DocStatus,
                                      o.Created,
                                      o.Updated
                                 FROM C_Order o
                                WHERE o.IsActive = 'Y'
                                  AND EXISTS (SELECT 1
                                                FROM C_OrderLine ol
                                               INNER JOIN M_RequisitionLine rl
                                                       ON (rl.C_OrderLine_ID = ol.C_OrderLine_ID)
                                               WHERE ol.C_Order_ID       = o.C_Order_ID
                                                 AND rl.M_Requisition_ID = @M_Requisition_ID
                                                 AND rl.IsActive         = 'Y')
                                ORDER BY o.Created";
                DataSet ds = DB.ExecuteDataset(sql, ReqParam(M_Requisition_ID), null);
                if (ds == null || ds.Tables.Count == 0 || ds.Tables[0].Rows.Count == 0) return;

                int orderCount = 0, closedCount = 0;
                foreach (DataRow r in ds.Tables[0].Rows)
                {
                    orderCount++;
                    string status     = Util.GetValueOfString(r["DocStatus"]);
                    DateTime? created = Util.GetValueOfDateTime(r["Created"]);
                    DateTime? updated = Util.GetValueOfDateTime(r["Updated"]);

                    // Converted: the earliest linked order's creation.
                    if (!d.ConvertedDate.HasValue ||
                        (created.HasValue && created.Value < d.ConvertedDate.Value))
                        d.ConvertedDate = created;

                    if (d.OrderDocumentNo == null) d.OrderDocumentNo = Util.GetValueOfString(r["DocumentNo"]);

                    if (status == "CO" || status == "CL")
                    {
                        // In fulfilment: the earliest linked order to be completed.
                        d.HasOrdered = true;
                        if (!d.FulfilmentDate.HasValue ||
                            (updated.HasValue && updated.Value < d.FulfilmentDate.Value))
                            d.FulfilmentDate = updated;
                    }

                    if (status == "CL")
                    {
                        closedCount++;
                        // Closed: the last linked order to close.
                        if (!d.ClosedDate.HasValue ||
                            (updated.HasValue && updated.Value > d.ClosedDate.Value))
                            d.ClosedDate = updated;
                    }
                }

                d.OrderCount = orderCount;
                // Every order raised from the requisition is closed -> the
                // requisition's procurement cycle is finished.
                if (orderCount > 0 && closedCount == orderCount) d.IsClosed = true;
                else if (!d.IsClosed) d.ClosedDate = null;
            }
            catch (Exception ex)
            {
                _log.Severe("LoadLinkedOrderMilestones (M_Requisition_ID=" + M_Requisition_ID + "): " + ex.Message);
            }
        }

        // ----------------------------------------------------------------- //
        //  Activity                                                          //
        // ----------------------------------------------------------------- //

        /// <summary>
        /// Builds the activity feed from real events: the create milestone
        /// (M_Requisition.Created), a status milestone when completed / closed
        /// (M_Requisition.Updated), and chat comments (CM_ChatEntry on
        /// M_Requisition), newest-first. Each carries a Type the client maps to
        /// a badge.
        /// </summary>
        private List<ActivityData> LoadActivity(int M_Requisition_ID, RequisitionOverviewData d)
        {
            List<ActivityData> activity = new List<ActivityData>();

            // Milestones from the header we already have.
            activity.Add(new ActivityData
            {
                Type    = "create",
                Text    = d.CreatedByName,
                Created = d.Created
            });
            if (d.StatusCode == "CO" || d.StatusCode == "CL")
            {
                activity.Add(new ActivityData
                {
                    Type    = "status",
                    Text    = d.UpdatedByName,
                    Created = d.Updated
                });
            }

            // Downstream documents — the rest of the requisition's lifecycle.
            LoadDocumentActivity(M_Requisition_ID, activity);

            // Chat comments.
            LoadCommentActivity(M_Requisition_ID, activity);

            activity.Sort((a, b) =>
                b.Created.GetValueOrDefault(DateTime.MinValue)
                 .CompareTo(a.Created.GetValueOrDefault(DateTime.MinValue)));
            return activity;
        }

        /// <summary>
        /// Adds the downstream documents the requisition produced, so the feed
        /// carries the whole lifecycle rather than stopping at the requisition:
        ///
        ///   po          — a purchase order was raised from this requisition.
        ///   grn         — a goods receipt was booked against one of those orders.
        ///   grncomplete — that receipt was completed.
        ///
        /// Purchase orders are reached through M_RequisitionLine.C_OrderLine_ID and
        /// receipts through their own C_OrderLine_ID back to the same order lines,
        /// so only documents genuinely descended from this requisition appear.
        /// </summary>
        private void LoadDocumentActivity(int M_Requisition_ID, List<ActivityData> list)
        {
            // ----- Purchase orders raised from the requisition -----
            try
            {
                string sql = @"SELECT o.DocumentNo,
                                      o.Created,
                                      u.Name AS UserName
                                 FROM C_Order o
                                 LEFT OUTER JOIN AD_User u ON (o.CreatedBy = u.AD_User_ID)
                                WHERE o.IsActive = 'Y'
                                  AND EXISTS (SELECT 1
                                                FROM C_OrderLine ol
                                               INNER JOIN M_RequisitionLine rl
                                                       ON (rl.C_OrderLine_ID = ol.C_OrderLine_ID)
                                               WHERE ol.C_Order_ID       = o.C_Order_ID
                                                 AND rl.M_Requisition_ID = @M_Requisition_ID
                                                 AND rl.IsActive         = 'Y')";
                DataSet ds = DB.ExecuteDataset(sql, ReqParam(M_Requisition_ID), null);
                if (ds != null && ds.Tables.Count > 0)
                {
                    foreach (DataRow r in ds.Tables[0].Rows)
                    {
                        list.Add(new ActivityData
                        {
                            Type       = "po",
                            DocumentNo = Util.GetValueOfString(r["DocumentNo"]),
                            UserName   = Util.GetValueOfString(r["UserName"]),
                            Text       = Util.GetValueOfString(r["UserName"]),
                            Created    = Util.GetValueOfDateTime(r["Created"])
                        });
                    }
                }
            }
            catch (Exception ex)
            {
                _log.Severe("LoadDocumentActivity/PO (M_Requisition_ID=" + M_Requisition_ID + "): " + ex.Message);
            }

            // ----- Goods receipts booked against those orders -----
            try
            {
                string sql = @"SELECT io.DocumentNo,
                                      io.DocStatus,
                                      io.Created,
                                      io.Updated,
                                      u.Name AS UserName
                                 FROM M_InOut io
                                 LEFT OUTER JOIN AD_User u ON (io.CreatedBy = u.AD_User_ID)
                                WHERE io.IsActive = 'Y'
                                  AND io.IsSOTrx  = 'N'
                                  AND EXISTS (SELECT 1
                                                FROM M_InOutLine iol
                                               INNER JOIN M_RequisitionLine rl
                                                       ON (rl.C_OrderLine_ID = iol.C_OrderLine_ID)
                                               WHERE iol.M_InOut_ID      = io.M_InOut_ID
                                                 AND iol.IsActive        = 'Y'
                                                 AND rl.M_Requisition_ID = @M_Requisition_ID
                                                 AND rl.IsActive         = 'Y')";
                DataSet ds = DB.ExecuteDataset(sql, ReqParam(M_Requisition_ID), null);
                if (ds == null || ds.Tables.Count == 0) return;

                foreach (DataRow r in ds.Tables[0].Rows)
                {
                    string docNo   = Util.GetValueOfString(r["DocumentNo"]);
                    string user    = Util.GetValueOfString(r["UserName"]);
                    string status  = Util.GetValueOfString(r["DocStatus"]);

                    list.Add(new ActivityData
                    {
                        Type       = "grn",
                        DocumentNo = docNo,
                        UserName   = user,
                        Text       = user,
                        Created    = Util.GetValueOfDateTime(r["Created"])
                    });

                    // A completed receipt gets its own entry, stamped with the
                    // change that carried it there.
                    if (status == "CO" || status == "CL")
                    {
                        list.Add(new ActivityData
                        {
                            Type       = "grncomplete",
                            DocumentNo = docNo,
                            UserName   = user,
                            Text       = user,
                            Created    = Util.GetValueOfDateTime(r["Updated"])
                        });
                    }
                }
            }
            catch (Exception ex)
            {
                _log.Severe("LoadDocumentActivity/GRN (M_Requisition_ID=" + M_Requisition_ID + "): " + ex.Message);
            }
        }

        /// <summary>Loads CM_ChatEntry comments logged against the requisition.</summary>
        private void LoadCommentActivity(int M_Requisition_ID, List<ActivityData> list)
        {
            try
            {
                string sql = @"SELECT ce.CharacterData,
                                      ce.Created,
                                      u.Name AS UserName
                                 FROM CM_ChatEntry ce
                                 INNER JOIN CM_Chat ch     ON (ce.CM_Chat_ID = ch.CM_Chat_ID)
                                 LEFT OUTER JOIN AD_User u ON (ce.AD_User_ID = u.AD_User_ID)
                                WHERE ch.AD_Table_ID =
                                      (SELECT t.AD_Table_ID FROM AD_Table t WHERE t.TableName = 'M_Requisition')
                                  AND ch.Record_ID = @M_Requisition_ID
                                  AND ce.IsActive  = 'Y'";
                DataSet ds = DB.ExecuteDataset(sql, ReqParam(M_Requisition_ID), null);
                if (ds == null || ds.Tables.Count == 0) return;
                foreach (DataRow r in ds.Tables[0].Rows)
                {
                    list.Add(new ActivityData
                    {
                        Type     = "comment",
                        Text     = Util.GetValueOfString(r["CharacterData"]),
                        UserName = Util.GetValueOfString(r["UserName"]),
                        Created  = Util.GetValueOfDateTime(r["Created"])
                    });
                }
            }
            catch (Exception ex)
            {
                _log.Severe("LoadCommentActivity (M_Requisition_ID=" + M_Requisition_ID + "): " + ex.Message);
            }
        }

        /// <summary>Single-parameter helper for the requisition-scoped queries.</summary>
        private SqlParameter[] ReqParam(int M_Requisition_ID)
        {
            return new SqlParameter[] { new SqlParameter("@M_Requisition_ID", M_Requisition_ID) };
        }

        // ----------------------------------------------------------------- //
        //  Data carriers                                                     //
        // ----------------------------------------------------------------- //

        public class RequisitionLineData
        {
            public int      M_RequisitionLine_ID { get; set; }
            public int      Line          { get; set; }
            public decimal  RequestedQty  { get; set; }
            public decimal  UnitPrice     { get; set; }
            public decimal  LineAmount    { get; set; }
            public string   Description   { get; set; }
            public int      M_Product_ID  { get; set; }
            public int      C_Charge_ID   { get; set; }
            public bool     IsConverted   { get; set; }   // line linked to an order line
            public string   ProductName   { get; set; }
            public string   ProductValue  { get; set; }   // product search key
            public string   CategoryName  { get; set; }
            public string   ChargeName    { get; set; }
            public string   UOMName       { get; set; }
            public int      UOMPrecision  { get; set; }
            public string   AttributeSetInstance { get; set; }   // blank when the line carries none

            // From the guarded enrichment pass (custom columns).
            public bool     HasSourceData { get; set; }   // reserved/ordered columns were available
            public decimal  ReservedQty   { get; set; }
            public decimal  OrderedQty    { get; set; }
            public decimal  AvailableBudget { get; set; }
            public bool     IsBudgetBreach { get; set; }
            public string   SourceState   { get; set; }   // full | short (when HasSourceData)
            public decimal  SourceQtyOnHand { get; set; } // M_Storage.QtyOnHand at the source warehouse
        }

        public class ActivityData
        {
            // create | status | comment | po | grn | grncomplete
            public string    Type       { get; set; }
            public string    Text       { get; set; }   // comment body, or actor for milestones
            public string    UserName   { get; set; }
            public string    DocumentNo { get; set; }   // related PO / GRN, when any
            public DateTime? Created    { get; set; }
        }

        public class RequisitionOverviewData
        {
            // Identity / header
            public int       M_Requisition_ID { get; set; }
            public string    DocumentNo    { get; set; }
            public string    StatusCode    { get; set; }   // DocStatus (mapped to label in JS)
            public string    PriorityCode  { get; set; }   // PriorityRule (mapped in JS)
            public DateTime? DateDoc       { get; set; }
            public DateTime? DateRequired  { get; set; }
            public string    Description   { get; set; }   // purpose / notes
            public bool      Processed     { get; set; }
            public bool      Posted        { get; set; }   // M_Requisition.Posted = 'Y'
            public string    PostedCode    { get; set; }   // raw posting status code
            public DateTime? Created       { get; set; }
            public DateTime? Updated       { get; set; }
            public DateTime? SystemDate    { get; set; }

            // People / route
            public string    RequesterName { get; set; }
            public string    PreparerName  { get; set; }
            public string    CreatedByName { get; set; }
            public string    UpdatedByName { get; set; }
            public string    RequestWarehouseName { get; set; }
            public int       SourceWarehouseId    { get; set; }   // DTD001_MWarehouseSource_ID
            public string    SourceWarehouseName  { get; set; }   // custom (guarded)
            public string    PriceListName { get; set; }

            // Currency
            public string    CurSymbol     { get; set; }
            public string    ISO_Code      { get; set; }
            public int       StdPrecision  { get; set; }

            // Stats / totals
            public decimal   EstimatedValue    { get; set; }   // TotalLines (or Σ lines)
            public decimal   EstimatedSubtotal { get; set; }   // Σ line amounts
            public int       LineCount         { get; set; }
            public decimal   RequestedUnits    { get; set; }
            public decimal   ReservedUnits     { get; set; }
            public decimal   OrderedUnits      { get; set; }
            // Budget set for the requisition — M_RequisitionLine.VAS_AvailableBudget,
            // written by the platform's "Calculate Budget" process. Distinct amounts
            // only, so one budget shared across lines is not counted twice.
            public decimal   AvailableBudget   { get; set; }   // custom (guarded)
            public bool      IsBudgetBreach    { get; set; }
            public decimal   BudgetOverage     { get; set; }   // estimated - available (when breach)
            public string    BudgetBreachNote  { get; set; }   // custom text (guarded)
            public bool      HasSourceData     { get; set; }   // a source warehouse is configured
            public int       FullyInStockLines { get; set; }
            public decimal   SourceStockOnHand { get; set; }   // Σ QtyOnHand at the source warehouse

            // Lifecycle
            public int       ConvertedLineCount { get; set; }
            public bool      IsConverted   { get; set; }
            public bool      HasOrdered    { get; set; }   // a linked PO reached Completed
            public bool      IsClosed      { get; set; }   // requisition CL, or every linked PO closed
            public int       OrderCount    { get; set; }   // purchase orders raised from this requisition
            public string    OrderDocumentNo { get; set; } // first of them, for display

            // Progress milestone dates
            public DateTime? CompletedDate  { get; set; }
            public DateTime? ConvertedDate  { get; set; }
            public DateTime? FulfilmentDate { get; set; }
            public DateTime? ClosedDate     { get; set; }

            // Collections
            public List<RequisitionLineData> Lines    { get; set; }
            public List<ActivityData>        Activity { get; set; }
        }
    }
}
