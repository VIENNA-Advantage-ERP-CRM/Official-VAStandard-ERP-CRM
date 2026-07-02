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

            RollUpTotals(result);

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
        /// flags) under a guard so a missing column just drops those bits.
        /// </summary>
        private void LoadHeaderExtras(int M_Requisition_ID, RequisitionOverviewData d)
        {
            try
            {
                string sql = @"SELECT srcwh.Name       AS SourceWarehouseName,
                                      COALESCE(r.IsBudgetBreach, 'N') AS IsBudgetBreach,
                                      r.VAS_BudgetBreach AS BudgetBreachNote
                                 FROM M_Requisition r
                                 LEFT OUTER JOIN M_Warehouse srcwh
                                        ON (srcwh.M_Warehouse_ID = r.DTD001_MWarehouseSource_ID)
                                WHERE r.M_Requisition_ID = @M_Requisition_ID";
                DataSet ds = DB.ExecuteDataset(sql, ReqParam(M_Requisition_ID), null);
                if (ds == null || ds.Tables.Count == 0 || ds.Tables[0].Rows.Count == 0) return;

                DataRow r = ds.Tables[0].Rows[0];
                d.SourceWarehouseName = Util.GetValueOfString(r["SourceWarehouseName"]);
                d.IsBudgetBreach      = Util.GetValueOfString(r["IsBudgetBreach"]) == "Y";
                d.BudgetBreachNote    = Util.GetValueOfString(r["BudgetBreachNote"]);
            }
            catch (Exception ex)
            {
                _log.Severe("LoadHeaderExtras (M_Requisition_ID=" + M_Requisition_ID + "): " + ex.Message);
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
                              uom.StdPrecision AS UOMPrecision
                           FROM M_RequisitionLine rl
                           LEFT OUTER JOIN M_Product p            ON (p.M_Product_ID = rl.M_Product_ID)
                           LEFT OUTER JOIN M_Product_Category pcat ON (pcat.M_Product_Category_ID = p.M_Product_Category_ID)
                           LEFT OUTER JOIN C_Charge ch            ON (ch.C_Charge_ID = rl.C_Charge_ID)
                           LEFT OUTER JOIN C_UOM uom              ON (uom.C_UOM_ID = rl.C_UOM_ID)
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
                string sql = @"SELECT rl.M_RequisitionLine_ID,
                                      COALESCE(rl.DTD001_ReservedQty, rl.QtyReserved, 0) AS ReservedQty,
                                      COALESCE(rl.QtyOrdered, 0)          AS OrderedQty,
                                      COALESCE(rl.VAS_AvailableBudget, 0) AS AvailableBudget,
                                      COALESCE(rl.IsBudgetBreach, 'N')    AS IsBudgetBreach
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
                    ln.HasSourceData   = true;

                    // Source state: full when reserved covers the request.
                    if (ln.RequestedQty > 0 && ln.ReservedQty >= ln.RequestedQty)
                        ln.SourceState = "full";
                    else
                        ln.SourceState = "short";
                }
            }
            catch (Exception ex)
            {
                _log.Severe("LoadLineExtras (M_Requisition_ID=" + M_Requisition_ID + "): " + ex.Message);
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
            int fullyInStock = 0;
            bool anyLineHasSource = false;
            bool anyLineBreach = false;

            foreach (RequisitionLineData ln in lines)
            {
                subtotal       += ln.LineAmount;
                requestedUnits += ln.RequestedQty;
                reserved       += ln.ReservedQty;
                ordered        += ln.OrderedQty;
                availableBudget += ln.AvailableBudget;
                if (ln.HasSourceData)
                {
                    anyLineHasSource = true;
                    if (ln.RequestedQty > 0 && ln.ReservedQty >= ln.RequestedQty) fullyInStock++;
                }
                if (ln.IsBudgetBreach) anyLineBreach = true;
            }

            d.LineCount        = lines.Count;
            d.RequestedUnits   = requestedUnits;
            d.EstimatedSubtotal = subtotal;
            if (d.EstimatedValue == 0) d.EstimatedValue = subtotal;         // fall back to line sum
            d.Contingency      = d.EstimatedValue - subtotal;
            if (d.Contingency < 0) d.Contingency = 0;

            d.HasSourceData    = anyLineHasSource;
            d.FullyInStockLines = fullyInStock;
            d.ReservedUnits    = reserved;
            d.OrderedUnits     = ordered;
            d.AvailableBudget  = availableBudget;

            // Budget breach = header flag OR any line flag.
            d.IsBudgetBreach   = d.IsBudgetBreach || anyLineBreach;
            if (d.IsBudgetBreach && availableBudget > 0 && d.EstimatedValue > availableBudget)
                d.BudgetOverage = d.EstimatedValue - availableBudget;

            d.IsConverted      = d.ConvertedLineCount > 0;
            d.HasOrdered       = ordered > 0;
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

            // Chat comments.
            LoadCommentActivity(M_Requisition_ID, activity);

            activity.Sort((a, b) =>
                b.Created.GetValueOrDefault(DateTime.MinValue)
                 .CompareTo(a.Created.GetValueOrDefault(DateTime.MinValue)));
            return activity;
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
            public string   ProductValue  { get; set; }   // SKU
            public string   CategoryName  { get; set; }
            public string   ChargeName    { get; set; }
            public string   UOMName       { get; set; }
            public int      UOMPrecision  { get; set; }

            // From the guarded enrichment pass (custom columns).
            public bool     HasSourceData { get; set; }   // reserved/ordered columns were available
            public decimal  ReservedQty   { get; set; }
            public decimal  OrderedQty    { get; set; }
            public decimal  AvailableBudget { get; set; }
            public bool     IsBudgetBreach { get; set; }
            public string   SourceState   { get; set; }   // full | short (when HasSourceData)
        }

        public class ActivityData
        {
            public string    Type     { get; set; }   // create | status | comment
            public string    Text     { get; set; }   // comment body, or actor for milestones
            public string    UserName { get; set; }
            public DateTime? Created  { get; set; }
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
            public DateTime? Created       { get; set; }
            public DateTime? Updated       { get; set; }
            public DateTime? SystemDate    { get; set; }

            // People / route
            public string    RequesterName { get; set; }
            public string    PreparerName  { get; set; }
            public string    CreatedByName { get; set; }
            public string    UpdatedByName { get; set; }
            public string    RequestWarehouseName { get; set; }
            public string    SourceWarehouseName  { get; set; }   // custom (guarded)
            public string    PriceListName { get; set; }

            // Currency
            public string    CurSymbol     { get; set; }
            public string    ISO_Code      { get; set; }
            public int       StdPrecision  { get; set; }

            // Stats / totals
            public decimal   EstimatedValue    { get; set; }   // TotalLines (or Σ lines)
            public decimal   EstimatedSubtotal { get; set; }   // Σ line amounts
            public decimal   Contingency       { get; set; }   // estimated - subtotal
            public int       LineCount         { get; set; }
            public decimal   RequestedUnits    { get; set; }
            public decimal   ReservedUnits     { get; set; }
            public decimal   OrderedUnits      { get; set; }
            public decimal   AvailableBudget   { get; set; }   // custom (guarded)
            public bool      IsBudgetBreach    { get; set; }
            public decimal   BudgetOverage     { get; set; }   // estimated - available (when breach)
            public string    BudgetBreachNote  { get; set; }   // custom text (guarded)
            public bool      HasSourceData     { get; set; }
            public int       FullyInStockLines { get; set; }

            // Lifecycle
            public int       ConvertedLineCount { get; set; }
            public bool      IsConverted   { get; set; }
            public bool      HasOrdered    { get; set; }

            // Collections
            public List<RequisitionLineData> Lines    { get; set; }
            public List<ActivityData>        Activity { get; set; }
        }
    }
}
