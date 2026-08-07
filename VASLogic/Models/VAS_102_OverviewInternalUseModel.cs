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
///   VAI163   2026-07-29  - Requested quantity now reads from the linked
///                          requisition line (M_RequisitionLine.Qty) whenever the
///                          issue line was created from a requisition, falling
///                          back to M_InventoryLine.QtyEntered for a manual issue.
///                          The header RequestedQty / NotFullCount aggregates use
///                          the same expression.
///                        - Added NotFullQty (the requested-minus-issued shortfall
///                          across the issue) for the panel's quantity cards.
///                        - Added the reference documents the panel's References
///                          section needs: the linked requisitions (number, doc /
///                          required dates, description, preparer) and, when the
///                          manufacturing module is installed, the work-order
///                          document no.
///   VAI163   2026-07-29  - Per-line unit rate now resolves from M_CostDetail:
///                          the cost written for the issue line itself, else the
///                          product's latest cost at the issue's warehouse, else
///                          the previous CurrentCostPrice / VA024_UnitPrice
///                          columns. TotalValue is recomputed from the lines so
///                          the KPI, footer and rows always agree.
///                        - Added CreatedDate / UpdatedDate, CompletedDate (the
///                          workflow DocComplete stamp, falling back to Updated)
///                          and PostedDate (earliest Fact_Acct row) for the
///                          panel's timeline.
///                        - Added Activity: the issue's audit trail (created,
///                          updated, completed, posted, chat notes) merged
///                          newest-first. Each source is separately guarded so one
///                          DB problem degrades to a partial trail.
///   VAI163   2026-07-29  - Work order origin now reads the VA075 service module:
///                          M_InventoryLine.VA075_WorkOrder_ID (falling back to the
///                          work order behind VA075_WorkOrderComponent_ID for rows
///                          saved before that column was stamped). The panel gets
///                          the work order's document no and reference, and the
///                          origin reads WORKORDER instead of MANUAL. All of it is
///                          AD_Column-guarded, so a schema without VA075 behaves
///                          exactly as before.
///                        - Added the line's Attribute Set Instance description.
///   VAI163   2026-08-03  - Activity now includes the e-mails sent against the
///                          issue (MailAttachment1 by AD_Table_ID = M_Inventory +
///                          Record_ID): recipient (MailAddress), subject (Title),
///                          body (TextMsg), when (Created) and who sent it
///                          (CreatedBy). The body travels with the row so the
///                          panel can reveal it on click without a second round
///                          trip, and an HTML mail is flattened to plain text
///                          (MailBodyToText) so the panel never has to render
///                          markup. The feed cap rose to 50 so a well-mailed
///                          issue does not push its own milestones off the trail.
///   VAI163   2026-08-03  - The e-mail lookup no longer filters on
///                          MailAttachment1.AttachmentType. That column's value
///                          varies between installations, and requiring 'M' hid
///                          mails that were really there. A row now counts as an
///                          e-mail when it carries a recipient address, which is
///                          what makes it one; the table id is matched with
///                          IN + UPPER so a differently-cased or duplicated
///                          dictionary entry still resolves.
/// </summary>

using System;
using System.Collections.Generic;
using System.Data;
using System.Data.SqlClient;
using System.Net;
using System.Text;
using System.Text.RegularExpressions;
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
            bool hasAsi         = ColumnExists("M_InventoryLine", "M_AttributeSetInstance_ID");

            // COALESCE([l.CurrentCostPrice], [l.VA024_UnitPrice], 0)
            string rateExpr = BuildRateExpr(hasCurrentCost, hasUnitPrice);
            // Manufacturing work-order id (schema-aware) used to classify origin.
            string woExpr = hasWorkOrder ? "l.VAMFG_M_WorkOrder_ID" : "NULL";

            // Requested quantity: what was asked for on the requisition the line
            // came from — not what the issue line itself carries. QtyEntered on an
            // internal-use line mirrors the issued quantity, so a partially issued
            // line would otherwise report "requested == issued". Lines with no
            // requisition (manual issue) keep QtyEntered.
            const string REQ_JOIN =
                "LEFT OUTER JOIN M_RequisitionLine rql ON (rql.M_RequisitionLine_ID = l.M_RequisitionLine_ID)";
            const string REQ_QTY = "NVL(rql.Qty, NVL(l.QtyEntered, 0))";

            string sql = @"SELECT
                              inv.M_Inventory_ID,
                              inv.DocumentNo,
                              inv.DocStatus,
                              inv.Processed,
                              inv.Posted,
                              inv.MovementDate,
                              inv.Description,
                              inv.M_Warehouse_ID,
                              inv.Created      AS CreatedDate,
                              inv.Updated      AS UpdatedDate,
                              wh.Name          AS WarehouseName,
                              creator.Name     AS IssuedBy,
                              (SELECT COUNT(*)
                                 FROM M_InventoryLine l
                                WHERE l.M_Inventory_ID = inv.M_Inventory_ID
                                  AND l.IsActive       = 'Y')                   AS LineCount,
                              (SELECT NVL(SUM(" + REQ_QTY + @"), 0)
                                 FROM M_InventoryLine l " + REQ_JOIN + @"
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
                              (SELECT NVL(SUM(CASE WHEN NVL(l.QtyInternalUse, 0) < " + REQ_QTY + @"
                                                   THEN 1 ELSE 0 END), 0)
                                 FROM M_InventoryLine l " + REQ_JOIN + @"
                                WHERE l.M_Inventory_ID = inv.M_Inventory_ID
                                  AND l.IsActive       = 'Y')                   AS NotFullCount,
                              (SELECT NVL(SUM(CASE WHEN " + REQ_QTY + @" > NVL(l.QtyInternalUse, 0)
                                                   THEN " + REQ_QTY + @" - NVL(l.QtyInternalUse, 0)
                                                   ELSE 0 END), 0)
                                 FROM M_InventoryLine l " + REQ_JOIN + @"
                                WHERE l.M_Inventory_ID = inv.M_Inventory_ID
                                  AND l.IsActive       = 'Y')                   AS NotFullQty,
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
            result.CreatedDate    = Util.GetValueOfDateTime(r["CreatedDate"]);
            result.UpdatedDate    = Util.GetValueOfDateTime(r["UpdatedDate"]);
            int M_Warehouse_ID    = Util.GetValueOfInt(r["M_Warehouse_ID"]);

            // ----- KPI aggregates -----
            result.LineCount    = Util.GetValueOfInt(r["LineCount"]);
            result.RequestedQty = Util.GetValueOfDecimal(r["RequestedQty"]);
            result.IssuedQty    = Util.GetValueOfDecimal(r["IssuedQty"]);
            result.TotalValue   = Util.GetValueOfDecimal(r["TotalValue"]);
            result.NotFullCount = Util.GetValueOfInt(r["NotFullCount"]);
            result.NotFullQty   = Util.GetValueOfDecimal(r["NotFullQty"]);

            // ----- Origin: derived from the linked source ids on the lines -----
            // A VA075 service work order wins, then a manufacturing work order,
            // then a requisition; only an issue linked to nothing is manual.
            LoadVA075WorkOrder(M_Inventory_ID, result);
            result.HasWorkOrder   = !string.IsNullOrEmpty(result.WorkOrderNo)
                                    || Util.GetValueOfInt(r["SampleWorkOrderID"]) > 0;
            result.HasRequisition = Util.GetValueOfInt(r["SampleRequisitionLineID"]) > 0;
            result.OriginCode     = !string.IsNullOrEmpty(result.WorkOrderNo) ? "WORKORDER"
                                  : (Util.GetValueOfInt(r["SampleWorkOrderID"]) > 0 ? "PRODUCTION"
                                  : (result.HasRequisition ? "REQUISITION" : "MANUAL"));

            // Issued value is expressed in the accounting currency; the panel
            // renders INR (₹) with standard 2-dp precision.
            result.StdPrecision = 2;

            // ----- Reference documents (References section) -----
            result.Requisitions = LoadRequisitions(M_Inventory_ID);
            if (result.Requisitions.Count > 0)
            {
                RequisitionRefData first = result.Requisitions[0];
                result.RequisitionNo    = first.DocumentNo;
                result.RequisitionDate  = first.DateDoc;
                result.DateRequired     = first.DateRequired;
                result.RequestedBy      = first.PreparerName;
                result.RequisitionNote  = first.Description;
                // More than one requisition can feed a single issue; the panel
                // shows the first and a "+n" hint from this count.
                result.RequisitionCount = result.Requisitions.Count;
            }
            // The manufacturing work order only fills in when the VA075 service
            // module did not already supply one.
            if (hasWorkOrder && string.IsNullOrEmpty(result.WorkOrderNo))
                result.WorkOrderNo = GetWorkOrderNo(M_Inventory_ID);

            // ----- Timeline stamps -----
            // Created is the record's own stamp (not the movement date) and Issued
            // is the completion moment; both are what the panel's timeline reads.
            result.CompletedDate = GetCompletedDate(M_Inventory_ID);
            result.CompletedBy   = _lastCompletedByName;
            if (!result.CompletedDate.HasValue &&
                (result.StatusCode == "CO" || result.StatusCode == "CL"))
            {
                // Completed outside the workflow engine — the last change is the
                // closest stamp we have.
                result.CompletedDate = result.UpdatedDate;
            }
            result.PostedDate = GetPostedDate(M_Inventory_ID);

            // ----- Issue lines -----
            result.Lines = LoadLines(M_Inventory_ID, rateExpr, woExpr, hasAsi, M_Warehouse_ID);

            // Line values may have been re-rated from M_CostDetail, so the issue
            // total is summed from the lines rather than kept from the header
            // aggregate — the KPI card, the table rows and the footer must agree.
            decimal total = 0;
            for (int i = 0; i < result.Lines.Count; i++) total += result.Lines[i].LineValue;
            result.TotalValue = total;

            // ----- Audit trail -----
            result.Activity = LoadActivity(M_Inventory_ID, result.StatusCode);

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
        /// metadata, requested (the linked requisition line's Qty, else
        /// QtyEntered) / issued (QtyInternalUse) quantities, available stock at the
        /// locator (summed from M_Storage) and a unit rate / line value. Child of
        /// an already authorized issue, so no separate MRole filter is applied
        /// here.
        /// </summary>
        /// <param name="M_Inventory_ID">Owning internal-use issue id.</param>
        /// <param name="rateExpr">Unit-rate SQL expression (schema-aware).</param>
        /// <param name="woExpr">Work-order-id SQL expression (schema-aware).</param>
        /// <param name="hasAsi">True when M_InventoryLine carries an attribute set instance.</param>
        /// <param name="M_Warehouse_ID">The issue's warehouse, for the costing lookup.</param>
        /// <returns>Ordered list of line rows (may be empty).</returns>
        private List<InternalUseLineData> LoadLines(
            int M_Inventory_ID, string rateExpr, string woExpr, bool hasAsi, int M_Warehouse_ID)
        {
            List<InternalUseLineData> lines = new List<InternalUseLineData>();

            // Attribute set instance (lot / serial / attributes) — only referenced
            // when the column is actually there.
            string asiExpr = hasAsi ? "asi.Description" : "CAST(NULL AS VARCHAR(255))";
            string asiJoin = hasAsi
                ? @"LEFT OUTER JOIN M_AttributeSetInstance asi
                           ON (asi.M_AttributeSetInstance_ID = l.M_AttributeSetInstance_ID
                               AND l.M_AttributeSetInstance_ID > 0)"
                : "";

            string sql = @"SELECT
                              l.M_InventoryLine_ID,
                              l.Line,
                              l.Description     AS LineDescription,
                              l.M_Product_ID,
                              NVL(rql.Qty, NVL(l.QtyEntered, 0)) AS RequestedQty,
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
                              rq.DocumentNo     AS RequisitionNo,
                              " + asiExpr + @"  AS AttributeSetInstance,
                              " + woExpr + @"                          AS WorkOrderID
                           FROM M_InventoryLine l
                           LEFT OUTER JOIN M_Product p   ON (p.M_Product_ID   = l.M_Product_ID)
                           LEFT OUTER JOIN C_UOM     u   ON (u.C_UOM_ID        = l.C_UOM_ID)
                           LEFT OUTER JOIN M_Locator loc ON (loc.M_Locator_ID  = l.M_Locator_ID)
                           LEFT OUTER JOIN M_RequisitionLine rql ON (rql.M_RequisitionLine_ID = l.M_RequisitionLine_ID)
                           LEFT OUTER JOIN M_Requisition     rq  ON (rq.M_Requisition_ID      = rql.M_Requisition_ID)
                           " + asiJoin + @"
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

            // Costing: the rate booked for the line itself wins, then the
            // product's latest cost at this warehouse; the line's own cost columns
            // remain the last resort.
            Dictionary<int, decimal> lineCosts = LoadLineCosts(M_Inventory_ID);
            Dictionary<int, decimal> whCosts   = LoadWarehouseCosts(M_Inventory_ID, M_Warehouse_ID);

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
                ln.RequisitionNo      = Util.GetValueOfString(r["RequisitionNo"]);
                ln.AttributeSetInstance = Util.GetValueOfString(r["AttributeSetInstance"]);
                ln.WorkOrderID        = Util.GetValueOfInt(r["WorkOrderID"]);

                decimal costRate;
                if (lineCosts.TryGetValue(ln.M_InventoryLine_ID, out costRate) && costRate != 0)
                {
                    ln.UnitRate   = costRate;
                    ln.CostSource = "LINE";
                }
                else if (whCosts.TryGetValue(ln.M_Product_ID, out costRate) && costRate != 0)
                {
                    ln.UnitRate   = costRate;
                    ln.CostSource = "WAREHOUSE";
                }
                else
                {
                    ln.CostSource = "PRICE";
                }
                ln.LineValue = ln.IssuedQty * ln.UnitRate;

                lines.Add(ln);
            }
            return lines;
        }

        /// <summary>
        /// Loads the distinct requisitions the issue's lines were raised against,
        /// lowest document no first. Empty for a manual issue or one raised from a
        /// production order.
        /// </summary>
        /// <param name="M_Inventory_ID">Owning internal-use issue id.</param>
        /// <returns>Ordered list of linked requisitions (may be empty).</returns>
        private List<RequisitionRefData> LoadRequisitions(int M_Inventory_ID)
        {
            List<RequisitionRefData> refs = new List<RequisitionRefData>();

            string sql = @"SELECT DISTINCT
                              rq.M_Requisition_ID,
                              rq.DocumentNo,
                              rq.DateDoc,
                              rq.DateRequired,
                              rq.Description,
                              preparer.Name AS PreparerName
                           FROM M_InventoryLine l
                           INNER JOIN M_RequisitionLine rql ON (rql.M_RequisitionLine_ID = l.M_RequisitionLine_ID)
                           INNER JOIN M_Requisition     rq  ON (rq.M_Requisition_ID      = rql.M_Requisition_ID)
                           LEFT OUTER JOIN AD_User preparer ON (preparer.AD_User_ID      = rq.AD_User_ID)
                          WHERE l.M_Inventory_ID = @M_Inventory_ID
                            AND l.IsActive       = 'Y'
                          ORDER BY rq.DocumentNo";

            SqlParameter[] param = new SqlParameter[]
            {
                new SqlParameter("@M_Inventory_ID", M_Inventory_ID)
            };

            DataSet ds = DB.ExecuteDataset(sql, param, null);
            if (ds == null || ds.Tables.Count == 0)
                return refs;

            foreach (DataRow r in ds.Tables[0].Rows)
            {
                RequisitionRefData rf = new RequisitionRefData();
                rf.M_Requisition_ID = Util.GetValueOfInt(r["M_Requisition_ID"]);
                rf.DocumentNo       = Util.GetValueOfString(r["DocumentNo"]);
                rf.DateDoc          = Util.GetValueOfDateTime(r["DateDoc"]);
                rf.DateRequired     = Util.GetValueOfDateTime(r["DateRequired"]);
                rf.Description      = Util.GetValueOfString(r["Description"]);
                rf.PreparerName     = Util.GetValueOfString(r["PreparerName"]);
                refs.Add(rf);
            }
            return refs;
        }

        /// <summary>
        /// Returns the document no of the manufacturing work order the issue was
        /// raised against. Only called when M_InventoryLine.VAMFG_M_WorkOrder_ID
        /// exists; a DB issue degrades to an empty string so a missing / renamed
        /// VAMFG table never breaks the overview.
        /// </summary>
        /// <param name="M_Inventory_ID">Owning internal-use issue id.</param>
        /// <returns>Work-order document no, or an empty string.</returns>
        private string GetWorkOrderNo(int M_Inventory_ID)
        {
            try
            {
                string sql = @"SELECT MAX(wo.DocumentNo)
                                 FROM M_InventoryLine l
                                INNER JOIN VAMFG_M_WorkOrder wo
                                   ON (wo.VAMFG_M_WorkOrder_ID = l.VAMFG_M_WorkOrder_ID)
                                WHERE l.M_Inventory_ID = @M_Inventory_ID
                                  AND l.IsActive       = 'Y'";
                SqlParameter[] param = new SqlParameter[]
                {
                    new SqlParameter("@M_Inventory_ID", M_Inventory_ID)
                };
                return Util.GetValueOfString(DB.ExecuteScalar(sql, param, null));
            }
            catch (Exception ex)
            {
                _log.Severe("GetWorkOrderNo (" + M_Inventory_ID + "): " + ex.Message);
                return "";
            }
        }

        /// <summary>
        /// Fills WorkOrderNo / WorkOrderRef / VA075_WorkOrder_ID from the VA075
        /// service work order the issue was raised against.
        ///
        /// The link is M_InventoryLine.VA075_WorkOrder_ID. Rows saved before that
        /// column was stamped only carry VA075_WorkOrderComponent_ID, so the
        /// component's own work order is accepted as a fallback — that is what
        /// keeps existing issues from reading "Manual Issue". Everything is
        /// AD_Column-guarded and try/caught: without the VA075 module this is a
        /// no-op.
        /// </summary>
        /// <param name="M_Inventory_ID">Selected internal-use issue id.</param>
        /// <param name="result">Overview payload being populated.</param>
        private void LoadVA075WorkOrder(int M_Inventory_ID, InternalUseOverviewData result)
        {
            bool hasWoCol   = ColumnExists("M_InventoryLine", "VA075_WorkOrder_ID");
            bool hasCompCol = ColumnExists("M_InventoryLine", "VA075_WorkOrderComponent_ID");
            // No VA075_WorkOrder table (module not installed) — nothing to read.
            if (!ColumnExists("VA075_WorkOrder", "DocumentNo")) return;
            if (!hasWoCol && !hasCompCol) return;

            try
            {
                // "Work order reference" is named differently across VA075
                // revisions; take the first one this schema actually has.
                string refCol = FirstExistingColumn("VA075_WorkOrder", new string[]
                {
                    "VA075_ReferenceNo", "Reference", "POReference", "Description", "Name"
                });
                string refExpr = string.IsNullOrEmpty(refCol)
                    ? "CAST(NULL AS VARCHAR(255))" : "wo." + refCol;

                // The ids reachable from the issue: stamped on the line, and (for
                // older rows) through the spare-part component. The id is inlined
                // rather than bound — the two UNION branches would otherwise need
                // the same bind twice, which is not portable across the drivers
                // this platform runs on. It is an int, so nothing can be injected.
                List<string> sources = new List<string>();
                if (hasWoCol)
                {
                    sources.Add(@"SELECT l.VA075_WorkOrder_ID AS WO_ID
                                    FROM M_InventoryLine l
                                   WHERE l.M_Inventory_ID = " + M_Inventory_ID + @"
                                     AND l.IsActive       = 'Y'
                                     AND NVL(l.VA075_WorkOrder_ID, 0) > 0");
                }
                if (hasCompCol && ColumnExists("VA075_WorkOrderComponent", "VA075_WorkOrder_ID"))
                {
                    sources.Add(@"SELECT c.VA075_WorkOrder_ID AS WO_ID
                                    FROM M_InventoryLine l
                                   INNER JOIN VA075_WorkOrderComponent c
                                           ON (c.VA075_WorkOrderComponent_ID = l.VA075_WorkOrderComponent_ID)
                                   WHERE l.M_Inventory_ID = " + M_Inventory_ID + @"
                                     AND l.IsActive       = 'Y'
                                     AND NVL(l.VA075_WorkOrderComponent_ID, 0) > 0");
                }
                if (sources.Count == 0) return;

                string sql = @"SELECT wo.VA075_WorkOrder_ID,
                                      wo.DocumentNo,
                                      " + refExpr + @" AS WorkOrderRef
                                 FROM VA075_WorkOrder wo
                                WHERE wo.VA075_WorkOrder_ID IN ("
                                 + string.Join(" UNION ", sources.ToArray()) + @")
                                ORDER BY wo.DocumentNo";

                DataSet ds = DB.ExecuteDataset(sql, null, null);
                if (ds == null || ds.Tables.Count == 0 || ds.Tables[0].Rows.Count == 0) return;

                DataRow r = ds.Tables[0].Rows[0];
                result.VA075_WorkOrder_ID = Util.GetValueOfInt(r["VA075_WorkOrder_ID"]);
                result.WorkOrderNo        = Util.GetValueOfString(r["DocumentNo"]);
                result.WorkOrderRef       = Util.GetValueOfString(r["WorkOrderRef"]);
                result.WorkOrderCount     = ds.Tables[0].Rows.Count;
            }
            catch (Exception ex)
            {
                // Non-fatal: the origin simply falls back to requisition / manual.
                _log.Severe("LoadVA075WorkOrder (M_Inventory_ID=" + M_Inventory_ID + "): " + ex.Message);
            }
        }

        /// <summary>
        /// Returns the first of the candidate columns that exists on the table, or
        /// an empty string when the table has none of them.
        /// </summary>
        private string FirstExistingColumn(string tableName, string[] candidates)
        {
            for (int i = 0; i < candidates.Length; i++)
            {
                if (ColumnExists(tableName, candidates[i])) return candidates[i];
            }
            return "";
        }

        // ----------------------------------------------------------------- //
        //  Costing (M_CostDetail)                                            //
        // ----------------------------------------------------------------- //

        /// <summary>
        /// Unit cost actually booked for each line of this issue, keyed by
        /// M_InventoryLine_ID: Amt / Qty of the newest M_CostDetail row written
        /// against the line. Empty for an unprocessed issue (no cost detail yet)
        /// or when the lookup fails.
        /// </summary>
        /// <param name="M_Inventory_ID">Owning internal-use issue id.</param>
        private Dictionary<int, decimal> LoadLineCosts(int M_Inventory_ID)
        {
            Dictionary<int, decimal> costs = new Dictionary<int, decimal>();
            if (!ColumnExists("M_CostDetail", "M_InventoryLine_ID")) return costs;

            try
            {
                // One row per line — the newest cost detail — rather than a SUM,
                // which would double-count across accounting schemas and cost
                // elements.
                string sql = @"SELECT cd.M_InventoryLine_ID, cd.Amt, cd.Qty
                                 FROM M_CostDetail cd
                                WHERE cd.M_CostDetail_ID IN
                                      (SELECT MAX(cd2.M_CostDetail_ID)
                                         FROM M_CostDetail cd2
                                        INNER JOIN M_InventoryLine l2
                                                ON (l2.M_InventoryLine_ID = cd2.M_InventoryLine_ID)
                                        WHERE l2.M_Inventory_ID = @M_Inventory_ID
                                          AND l2.IsActive       = 'Y'
                                          AND cd2.IsActive      = 'Y'
                                          AND NVL(cd2.Qty, 0)  <> 0
                                        GROUP BY cd2.M_InventoryLine_ID)";

                DataSet ds = DB.ExecuteDataset(sql, InventoryParam(M_Inventory_ID), null);
                if (ds == null || ds.Tables.Count == 0) return costs;

                foreach (DataRow r in ds.Tables[0].Rows)
                {
                    decimal qty = Util.GetValueOfDecimal(r["Qty"]);
                    if (qty == 0) continue;
                    costs[Util.GetValueOfInt(r["M_InventoryLine_ID"])] =
                        Math.Abs(Util.GetValueOfDecimal(r["Amt"]) / qty);
                }
            }
            catch (Exception ex)
            {
                // Non-fatal: the line falls back to the warehouse cost, then to
                // its own cost columns.
                _log.Severe("LoadLineCosts (M_Inventory_ID=" + M_Inventory_ID + "): " + ex.Message);
            }
            return costs;
        }

        /// <summary>
        /// Latest unit cost per product AT THE ISSUE'S WAREHOUSE, keyed by
        /// M_Product_ID — Amt / Qty of the newest M_CostDetail row for that
        /// product and warehouse. Used for lines that carry no cost detail of
        /// their own (a draft issue). Empty when the schema keeps no warehouse on
        /// M_CostDetail (costing level below warehouse) or the lookup fails.
        /// </summary>
        /// <param name="M_Inventory_ID">Owning internal-use issue id.</param>
        /// <param name="M_Warehouse_ID">The issue's warehouse.</param>
        private Dictionary<int, decimal> LoadWarehouseCosts(int M_Inventory_ID, int M_Warehouse_ID)
        {
            Dictionary<int, decimal> costs = new Dictionary<int, decimal>();
            if (M_Warehouse_ID <= 0) return costs;
            if (!ColumnExists("M_CostDetail", "M_Warehouse_ID")) return costs;

            try
            {
                string sql = @"SELECT cd.M_Product_ID, cd.Amt, cd.Qty
                                 FROM M_CostDetail cd
                                WHERE cd.M_CostDetail_ID IN
                                      (SELECT MAX(cd2.M_CostDetail_ID)
                                         FROM M_CostDetail cd2
                                        WHERE cd2.IsActive        = 'Y'
                                          AND NVL(cd2.Qty, 0)    <> 0
                                          AND cd2.M_Warehouse_ID  = @M_Warehouse_ID
                                          AND cd2.M_Product_ID IN
                                              (SELECT l.M_Product_ID
                                                 FROM M_InventoryLine l
                                                WHERE l.M_Inventory_ID = @M_Inventory_ID
                                                  AND l.IsActive       = 'Y')
                                        GROUP BY cd2.M_Product_ID)";
                SqlParameter[] param = new SqlParameter[]
                {
                    new SqlParameter("@M_Warehouse_ID", M_Warehouse_ID),
                    new SqlParameter("@M_Inventory_ID", M_Inventory_ID)
                };

                DataSet ds = DB.ExecuteDataset(sql, param, null);
                if (ds == null || ds.Tables.Count == 0) return costs;

                foreach (DataRow r in ds.Tables[0].Rows)
                {
                    decimal qty = Util.GetValueOfDecimal(r["Qty"]);
                    if (qty == 0) continue;
                    costs[Util.GetValueOfInt(r["M_Product_ID"])] =
                        Math.Abs(Util.GetValueOfDecimal(r["Amt"]) / qty);
                }
            }
            catch (Exception ex)
            {
                _log.Severe("LoadWarehouseCosts (M_Inventory_ID=" + M_Inventory_ID + "): " + ex.Message);
            }
            return costs;
        }

        // ----------------------------------------------------------------- //
        //  Timeline stamps + audit trail                                     //
        // ----------------------------------------------------------------- //

        /// <summary>
        /// Set by <see cref="GetCompletedDate"/> alongside its return value, so the
        /// caller gets both the moment and the actor from one query.
        /// </summary>
        private string _lastCompletedByName;

        /// <summary>
        /// Returns the moment the issue was completed — the Created stamp of its
        /// workflow DocComplete activity — or null when it has no such node (an
        /// issue completed outside the workflow engine); the caller then falls back
        /// to the last-updated stamp.
        ///
        /// Deliberately a standalone query rather than a subselect in the main
        /// SELECT, which MRole.AddAccessSQL rewrites with SQL_FULLYQUALIFIED: that
        /// parser walks every FROM/JOIN it can read (including inside subselects)
        /// and appends its filters to the OUTER WHERE, so aliases living only in a
        /// subselect break the whole statement.
        /// </summary>
        /// <param name="M_Inventory_ID">Selected internal-use issue id.</param>
        private DateTime? GetCompletedDate(int M_Inventory_ID)
        {
            _lastCompletedByName = "";
            try
            {
                string sql = @"SELECT wfa.Created, u.Name AS UserName
                                 FROM AD_WF_Process wfp
                                INNER JOIN AD_WF_Activity wfa
                                        ON (wfa.AD_WF_Process_ID = wfp.AD_WF_Process_ID)
                                INNER JOIN AD_WF_Node wfn
                                        ON (wfn.AD_WF_Node_ID = wfa.AD_WF_Node_ID)
                                INNER JOIN AD_Table adt
                                        ON (adt.AD_Table_ID = wfp.AD_Table_ID)
                                 LEFT OUTER JOIN AD_User u ON (wfa.CreatedBy = u.AD_User_ID)
                                WHERE wfp.Record_ID = @M_Inventory_ID
                                  AND adt.TableName = 'M_Inventory'
                                  AND wfp.IsActive  = 'Y'
                                  AND wfa.IsActive  = 'Y'
                                  AND wfn.IsActive  = 'Y'
                                  AND wfa.WFState   = 'CC'
                                  AND UPPER(TRIM(wfn.Value)) IN ('DOCCOMPLETE', 'COMPLETE', '(DOCCOMPLETE)')
                                ORDER BY wfa.Created DESC";
                DataSet ds = DB.ExecuteDataset(sql, InventoryParam(M_Inventory_ID), null);
                if (ds == null || ds.Tables.Count == 0 || ds.Tables[0].Rows.Count == 0)
                    return null;

                DataRow r = ds.Tables[0].Rows[0];
                _lastCompletedByName = Util.GetValueOfString(r["UserName"]);
                return Util.GetValueOfDateTime(r["Created"]);
            }
            catch (Exception ex)
            {
                _log.Severe("GetCompletedDate (M_Inventory_ID=" + M_Inventory_ID + "): " + ex.Message);
                return null;
            }
        }

        /// <summary>
        /// Returns the moment the issue was actually posted — the earliest Created
        /// stamp across the Fact_Acct rows written for it — or null when it has
        /// never been posted. Standalone query for the same MRole reason documented
        /// on <see cref="GetCompletedDate"/>.
        /// </summary>
        /// <param name="M_Inventory_ID">Selected internal-use issue id.</param>
        private DateTime? GetPostedDate(int M_Inventory_ID)
        {
            try
            {
                string sql = @"SELECT MIN(fa.Created) AS PostedDate
                                 FROM Fact_Acct fa
                                INNER JOIN AD_Table adt ON (adt.AD_Table_ID = fa.AD_Table_ID)
                                WHERE fa.Record_ID  = @M_Inventory_ID
                                  AND adt.TableName = 'M_Inventory'";
                DataSet ds = DB.ExecuteDataset(sql, InventoryParam(M_Inventory_ID), null);
                if (ds == null || ds.Tables.Count == 0 || ds.Tables[0].Rows.Count == 0)
                    return null;
                return Util.GetValueOfDateTime(ds.Tables[0].Rows[0]["PostedDate"]);
            }
            catch (Exception ex)
            {
                _log.Severe("GetPostedDate (M_Inventory_ID=" + M_Inventory_ID + "): " + ex.Message);
                return null;
            }
        }

        /// <summary>
        /// Builds the issue's audit trail: who created it, who last changed it and
        /// when, when it was completed and by whom, when it was posted, plus any
        /// chat notes and e-mails logged against it — merged newest-first and
        /// capped.
        ///
        /// Each source runs under its own guard so a DB-level problem with one
        /// degrades to a partial trail (logged) rather than breaking the overview.
        /// </summary>
        /// <param name="M_Inventory_ID">Selected internal-use issue id.</param>
        /// <param name="docStatus">The issue's DocStatus, for the completion entry.</param>
        /// <returns>Activity rows ordered newest-first (may be empty).</returns>
        private List<InternalUseActivityData> LoadActivity(int M_Inventory_ID, string docStatus)
        {
            // An issue mailed out a dozen times would otherwise push its own
            // milestones off the bottom of the trail, so the cap is a runaway
            // guard rather than a headline count.
            const int MAX_ENTRIES = 50;

            List<InternalUseActivityData> activity = new List<InternalUseActivityData>();
            LoadIssueMilestones(M_Inventory_ID, docStatus, activity);
            LoadPostingActivity(M_Inventory_ID, activity);
            LoadNoteActivity(M_Inventory_ID, activity);
            LoadEmailActivity(M_Inventory_ID, activity);

            // Newest first; entries with no timestamp sink to the bottom.
            activity.Sort(delegate (InternalUseActivityData a, InternalUseActivityData b)
            {
                return b.Created.GetValueOrDefault(DateTime.MinValue)
                        .CompareTo(a.Created.GetValueOrDefault(DateTime.MinValue));
            });

            if (activity.Count > MAX_ENTRIES)
                activity = activity.GetRange(0, MAX_ENTRIES);
            return activity;
        }

        /// <summary>
        /// The issue's own milestones: "created" (Created / CreatedBy), "updated"
        /// (Updated / UpdatedBy, only when it differs from the create stamp) and,
        /// for a completed or closed issue, "completed" — the workflow stamp when
        /// there is one, else the last change.
        /// </summary>
        private void LoadIssueMilestones(
            int M_Inventory_ID, string docStatus, List<InternalUseActivityData> list)
        {
            try
            {
                string sql = @"SELECT inv.Created,
                                      inv.Updated,
                                      cu.Name AS CreatedByName,
                                      uu.Name AS UpdatedByName
                                 FROM M_Inventory inv
                                 LEFT OUTER JOIN AD_User cu ON (inv.CreatedBy = cu.AD_User_ID)
                                 LEFT OUTER JOIN AD_User uu ON (inv.UpdatedBy = uu.AD_User_ID)
                                WHERE inv.M_Inventory_ID = @M_Inventory_ID";
                DataSet ds = DB.ExecuteDataset(sql, InventoryParam(M_Inventory_ID), null);
                if (ds == null || ds.Tables.Count == 0 || ds.Tables[0].Rows.Count == 0) return;

                DataRow r = ds.Tables[0].Rows[0];
                DateTime? created = Util.GetValueOfDateTime(r["Created"]);
                DateTime? updated = Util.GetValueOfDateTime(r["Updated"]);
                string updatedBy  = Util.GetValueOfString(r["UpdatedByName"]);

                list.Add(new InternalUseActivityData
                {
                    Type     = "created",
                    UserName = Util.GetValueOfString(r["CreatedByName"]),
                    Created  = created
                });

                // An issue saved once has Updated == Created; that is not an edit.
                if (updated.HasValue &&
                    (!created.HasValue || updated.Value > created.Value.AddSeconds(1)))
                {
                    list.Add(new InternalUseActivityData
                    {
                        Type     = "updated",
                        UserName = updatedBy,
                        Created  = updated
                    });
                }

                if (docStatus == "CO" || docStatus == "CL")
                {
                    DateTime? completedAt = GetCompletedDate(M_Inventory_ID);
                    string completedBy    = _lastCompletedByName;

                    list.Add(new InternalUseActivityData
                    {
                        Type     = "completed",
                        UserName = !string.IsNullOrEmpty(completedBy) ? completedBy : updatedBy,
                        Created  = completedAt.HasValue ? completedAt : updated
                    });
                }
            }
            catch (Exception ex)
            {
                _log.Severe("LoadIssueMilestones (M_Inventory_ID=" + M_Inventory_ID + "): " + ex.Message);
            }
        }

        /// <summary>
        /// Adds a "posted" entry from the earliest Fact_Acct row written for the
        /// issue, carrying the user who ran the posting.
        /// </summary>
        private void LoadPostingActivity(int M_Inventory_ID, List<InternalUseActivityData> list)
        {
            try
            {
                string sql = @"SELECT fa.Created, u.Name AS UserName
                                 FROM Fact_Acct fa
                                INNER JOIN AD_Table adt ON (adt.AD_Table_ID = fa.AD_Table_ID)
                                 LEFT OUTER JOIN AD_User u ON (fa.CreatedBy = u.AD_User_ID)
                                WHERE fa.Record_ID  = @M_Inventory_ID
                                  AND adt.TableName = 'M_Inventory'
                                ORDER BY fa.Created";
                DataSet ds = DB.ExecuteDataset(sql, InventoryParam(M_Inventory_ID), null);
                if (ds == null || ds.Tables.Count == 0 || ds.Tables[0].Rows.Count == 0) return;

                DataRow r = ds.Tables[0].Rows[0];
                list.Add(new InternalUseActivityData
                {
                    Type     = "posted",
                    UserName = Util.GetValueOfString(r["UserName"]),
                    Created  = Util.GetValueOfDateTime(r["Created"])
                });
            }
            catch (Exception ex)
            {
                _log.Severe("LoadPostingActivity (M_Inventory_ID=" + M_Inventory_ID + "): " + ex.Message);
            }
        }

        /// <summary>
        /// Adds free-text chat notes (CM_ChatEntry) logged against this issue.
        /// </summary>
        private void LoadNoteActivity(int M_Inventory_ID, List<InternalUseActivityData> list)
        {
            try
            {
                string sql = @"SELECT ce.CharacterData, ce.Created, u.Name AS UserName
                                 FROM CM_ChatEntry ce
                                INNER JOIN CM_Chat ch     ON (ce.CM_Chat_ID = ch.CM_Chat_ID)
                                 LEFT OUTER JOIN AD_User u ON (ce.AD_User_ID = u.AD_User_ID)
                                WHERE ch.AD_Table_ID =
                                      (SELECT t.AD_Table_ID FROM AD_Table t WHERE t.TableName = 'M_Inventory')
                                  AND ch.Record_ID = @M_Inventory_ID
                                  AND ce.IsActive  = 'Y'";
                DataSet ds = DB.ExecuteDataset(sql, InventoryParam(M_Inventory_ID), null);
                if (ds == null || ds.Tables.Count == 0) return;

                foreach (DataRow r in ds.Tables[0].Rows)
                {
                    list.Add(new InternalUseActivityData
                    {
                        Type     = "note",
                        Text     = Util.GetValueOfString(r["CharacterData"]),
                        UserName = Util.GetValueOfString(r["UserName"]),
                        Created  = Util.GetValueOfDateTime(r["Created"])
                    });
                }
            }
            catch (Exception ex)
            {
                _log.Severe("LoadNoteActivity (M_Inventory_ID=" + M_Inventory_ID + "): " + ex.Message);
            }
        }

        /// <summary>
        /// Adds the e-mails sent against this issue (MailAttachment1, joined by
        /// AD_Table_ID = M_Inventory + Record_ID = the issue id) as "email" rows:
        /// recipient (MailAddress), subject (Title), body (TextMsg), when
        /// (Created) and who sent it (CreatedBy).
        ///
        /// The body travels with the row so the panel can reveal it on click
        /// without a second round trip.
        /// </summary>
        private void LoadEmailActivity(int M_Inventory_ID, List<InternalUseActivityData> list)
        {
            try
            {
                // A row is an e-mail when it has somewhere to go — a recipient on
                // any of the address columns. AttachmentType is deliberately NOT
                // filtered on: its value varies between installations, and a mail
                // that carries an address is a mail whatever the column says.
                // Rows with no recipient at all (a stored letter / inbound
                // document) are the ones this leaves out.
                //
                // The table id is matched with IN + UPPER so a dictionary holding
                // the name in another case, or more than one row for it, resolves
                // instead of failing the statement.
                string sql = @"SELECT ma.MailAddress,
                                      ma.MailAddressCc,
                                      ma.MailAddressBcc,
                                      ma.MailAddressFrom,
                                      ma.Title,
                                      ma.TextMsg,
                                      ma.Created,
                                      ma.IsMailSent,
                                      u.Name AS UserName
                                 FROM MailAttachment1 ma
                                 LEFT OUTER JOIN AD_User u ON (u.AD_User_ID = ma.CreatedBy)
                                WHERE ma.AD_Table_ID IN
                                      (SELECT t.AD_Table_ID FROM AD_Table t
                                        WHERE UPPER(t.TableName) = 'M_INVENTORY')
                                  AND ma.Record_ID          = @M_Inventory_ID
                                  AND NVL(ma.IsActive, 'Y') = 'Y'
                                  AND (NVL(TRIM(ma.MailAddress), '')     <> ''
                                    OR NVL(TRIM(ma.MailAddressCc), '')   <> ''
                                    OR NVL(TRIM(ma.MailAddressBcc), '')  <> '')
                                ORDER BY ma.Created DESC";
                DataSet ds = DB.ExecuteDataset(sql, InventoryParam(M_Inventory_ID), null);
                if (ds == null || ds.Tables.Count == 0) return;

                foreach (DataRow r in ds.Tables[0].Rows)
                {
                    list.Add(new InternalUseActivityData
                    {
                        Type       = "email",
                        // Text is the row's headline everywhere in this feed; for
                        // an e-mail that is its subject.
                        Text       = Util.GetValueOfString(r["Title"]),
                        // Mails sent as HTML store their markup in TextMsg; the
                        // panel shows a body as text, so it is flattened here.
                        Body       = MailBodyToText(Util.GetValueOfString(r["TextMsg"])),
                        MailTo     = Util.GetValueOfString(r["MailAddress"]),
                        MailCc     = Util.GetValueOfString(r["MailAddressCc"]),
                        MailBcc    = Util.GetValueOfString(r["MailAddressBcc"]),
                        MailFrom   = Util.GetValueOfString(r["MailAddressFrom"]),
                        IsMailSent = Util.GetValueOfString(r["IsMailSent"]) == "Y",
                        UserName   = Util.GetValueOfString(r["UserName"]),
                        Created    = Util.GetValueOfDateTime(r["Created"])
                    });
                }
            }
            catch (Exception ex)
            {
                // Non-fatal: a schema without MailAttachment1 just shows no e-mails.
                _log.Severe("LoadEmailActivity (M_Inventory_ID=" + M_Inventory_ID + "): " + ex.Message);
            }
        }

        /// <summary>
        /// Cheap "is this markup" test — a real tag, not a stray '&lt;' in a
        /// plain-text mail ("qty &lt; 10"), so a plain body is left untouched.
        /// </summary>
        private static readonly Regex HTML_BODY = new Regex(
            @"<\s*/?\s*(html|body|head|br|p|div|table|thead|tbody|tr|td|th|span|a|img|b|i|u"
            + @"|strong|em|ul|ol|li|h[1-6]|font|style|script)\b",
            RegexOptions.IgnoreCase | RegexOptions.Compiled);

        /// <summary>
        /// Renders a mail body (MailAttachment1.TextMsg) as readable plain text.
        /// A mail sent as HTML stores its markup here, and the panel shows the
        /// body as text — so without this the reader gets tags instead of a
        /// message. Block-level markup becomes line breaks, table cells become
        /// tabs, everything else is dropped and entities are decoded last, so
        /// the browser still receives text it can safely escape: no markup is
        /// ever handed to the panel. A body with no markup is returned as it
        /// was stored.
        /// </summary>
        private static string MailBodyToText(string body)
        {
            if (string.IsNullOrEmpty(body)) return body;
            if (!HTML_BODY.IsMatch(body)) return body;      // plain-text mail

            try
            {
                string s = body;

                // Head matter, styles and scripts carry no reading content.
                s = Regex.Replace(s, @"<\s*(script|style|head)\b[^>]*>.*?<\s*/\s*\1\s*>", " ",
                                  RegexOptions.IgnoreCase | RegexOptions.Singleline);

                // Block boundaries become line breaks so paragraphs survive.
                s = Regex.Replace(s, @"<\s*br\s*/?\s*>", "\n", RegexOptions.IgnoreCase);
                s = Regex.Replace(s, @"<\s*/\s*(p|div|tr|li|h[1-6]|table|blockquote)\s*>", "\n",
                                  RegexOptions.IgnoreCase);
                // Opening tags too, so a <p> with no closing tag still breaks.
                // 'tr' is deliberately absent — </tr> already ends the row, and
                // breaking on both would leave a blank line between every row.
                s = Regex.Replace(s, @"<\s*(p|div|li|h[1-6])\b[^>]*>", "\n",
                                  RegexOptions.IgnoreCase);
                // Cells read better separated than run together.
                s = Regex.Replace(s, @"<\s*/\s*(td|th)\s*>", "\t", RegexOptions.IgnoreCase);

                // Everything left is presentation.
                s = Regex.Replace(s, @"<[^>]*>", string.Empty);

                // Entities last, so an escaped &lt;b&gt; in the text was never
                // treated as a tag above.
                s = WebUtility.HtmlDecode(s);
                s = s.Replace('\u00A0', ' ');               // nbsp reads as a space

                // Normalise the whitespace the markup left behind.
                s = s.Replace("\r\n", "\n").Replace('\r', '\n');
                s = Regex.Replace(s, @"[^\S\n\t]+", " ");   // runs of spaces -> one
                s = Regex.Replace(s, @"\t{2,}", "\t");
                s = Regex.Replace(s, @"[ \t]*\n[ \t]*", "\n");   // incl. the last cell's tab
                s = Regex.Replace(s, @"\n{3,}", "\n\n");    // at most one blank line

                return s.Trim();
            }
            catch (Exception ex)
            {
                // Never lose the mail over a formatting failure — show it raw.
                _log.Severe("MailBodyToText: " + ex.Message);
                return body;
            }
        }

        /// <summary>The single-id parameter every child query takes.</summary>
        private SqlParameter[] InventoryParam(int M_Inventory_ID)
        {
            return new SqlParameter[]
            {
                new SqlParameter("@M_Inventory_ID", M_Inventory_ID)
            };
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
            public decimal  RequestedQty       { get; set; }   // M_RequisitionLine.Qty, else QtyEntered
            public decimal  IssuedQty          { get; set; }   // QtyInternalUse
            public decimal  AvailableQty       { get; set; }   // M_Storage on-hand
            public decimal  UnitRate           { get; set; }
            public decimal  LineValue          { get; set; }
            // Where UnitRate came from: LINE (this line's cost detail),
            // WAREHOUSE (product cost at the issue's warehouse) or PRICE (the
            // line's own cost / price columns).
            public string   CostSource         { get; set; }
            public int      RequisitionLineID  { get; set; }
            public string   RequisitionNo      { get; set; }   // parent requisition doc no
            public string   AttributeSetInstance { get; set; } // lot / serial / attributes
            public int      WorkOrderID        { get; set; }
        }

        /// <summary>One entry in the issue's audit trail.</summary>
        public class InternalUseActivityData
        {
            /// <summary>created | updated | completed | posted | note | email</summary>
            public string    Type       { get; set; }
            public string    UserName   { get; set; }   // actor / mail sender
            public DateTime? Created    { get; set; }
            public string    DocumentNo { get; set; }
            public string    Text       { get; set; }   // note body / e-mail subject

            // E-mail (MailAttachment1) — the body is revealed on click.
            public string    Body       { get; set; }   // TextMsg (flattened to text)
            public string    MailTo     { get; set; }   // MailAddress
            public string    MailCc     { get; set; }   // MailAddressCc
            public string    MailBcc    { get; set; }   // MailAddressBcc
            public string    MailFrom   { get; set; }   // MailAddressFrom
            public bool      IsMailSent { get; set; }
        }

        /// <summary>A requisition the issue's lines were raised against.</summary>
        public class RequisitionRefData
        {
            public int       M_Requisition_ID { get; set; }
            public string    DocumentNo       { get; set; }
            public DateTime? DateDoc          { get; set; }
            public DateTime? DateRequired     { get; set; }
            public string    Description      { get; set; }
            public string    PreparerName     { get; set; }
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

            // Timeline stamps: when the record was created, completed and posted
            // — none of them the movement date.
            public DateTime? CreatedDate    { get; set; }
            public DateTime? UpdatedDate    { get; set; }
            public DateTime? CompletedDate  { get; set; }
            public string    CompletedBy    { get; set; }
            public DateTime? PostedDate     { get; set; }

            // Origin (derived from the linked source ids on the lines)
            public string    OriginCode     { get; set; }   // PRODUCTION | REQUISITION | MANUAL
            public bool      HasWorkOrder   { get; set; }
            public bool      HasRequisition { get; set; }

            // Reference documents (References section)
            public string    RequisitionNo    { get; set; }  // first linked requisition
            public DateTime? RequisitionDate  { get; set; }
            public DateTime? DateRequired     { get; set; }
            public string    RequestedBy      { get; set; }
            public string    RequisitionNote  { get; set; }
            public int       RequisitionCount { get; set; }
            public string    WorkOrderNo      { get; set; }
            public string    WorkOrderRef     { get; set; }   // the work order's own reference
            public int       VA075_WorkOrder_ID { get; set; }
            public int       WorkOrderCount   { get; set; }

            // Currency
            public int       StdPrecision   { get; set; }

            // KPI aggregates
            public int       LineCount    { get; set; }
            public decimal   RequestedQty { get; set; }
            public decimal   IssuedQty    { get; set; }
            public decimal   TotalValue   { get; set; }
            public int       NotFullCount { get; set; }
            // Requested minus issued across every not-fully-issued line.
            public decimal   NotFullQty   { get; set; }

            // Collections
            public List<InternalUseLineData>     Lines        { get; set; }
            public List<RequisitionRefData>      Requisitions { get; set; }
            public List<InternalUseActivityData> Activity     { get; set; }
        }
    }
}
