/// <summary>
/// Module Name : VASLogic
/// Purpose     : Ship / GRN Confirmation Overview tab panel data (read side).
///               Returns header identity, the linked source shipment/receipt
///               (M_InOut) with party and warehouse, KPI aggregates (line count /
///               target / confirmed / difference / scrapped quantities and the
///               QC-pass count), the derived quality-applicable flag and the
///               confirmation lines for a selected in/out confirmation
///               (M_InOutConfirm). The screen runs in two modes — quality-
///               applicable (incoming QC) and plain confirmation — driven by
///               whether any source line carries a quality plan.
/// Chronological development:
///   VAI163   2026-07-07  Created. Optional module columns
///                        (VA010_QualCheckMArk, VA010_QualityPlan_ID) are guarded
///                        through AD_Column so the panel works whether or not the
///                        quality (VA010) module is installed.
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
    public class VAS_104_OverviewShipGRNConfirmationModel
    {
        private static readonly VLogger _log =
            VLogger.GetVLogger(typeof(VAS_104_OverviewShipGRNConfirmationModel).FullName);

        /// <summary>
        /// Returns the full overview payload for the selected confirmation.
        /// MRole access filtering is applied ONLY on the main physical table
        /// (M_InOutConfirm alias "c"); the child line query inherits the parent's
        /// authorization and is not separately role-filtered.
        /// </summary>
        /// <param name="ctx">User context (client/org/role).</param>
        /// <param name="M_InOutConfirm_ID">Selected confirmation id.</param>
        /// <returns>Populated <see cref="ShipGRNConfirmationOverviewData"/>; an
        /// empty instance when the id is invalid or no accessible row is found.</returns>
        public ShipGRNConfirmationOverviewData GetShipGRNConfirmationOverview(Ctx ctx, int M_InOutConfirm_ID)
        {
            ShipGRNConfirmationOverviewData result = new ShipGRNConfirmationOverviewData();
            if (M_InOutConfirm_ID <= 0) return result;

            // Optional quality-module columns — resolved once so the SQL below
            // only references columns that actually exist in this schema.
            bool hasQcMark   = ColumnExists("M_InOutLineConfirm", "VA010_QualCheckMArk");
            bool hasQualPlan = ColumnExists("M_InOutLine", "VA010_QualityPlan_ID");

            // COALESCE(lc.VA010_QualCheckMArk, 'N') — 'Y' when the line passed QC.
            string qcMarkExpr = hasQcMark ? "COALESCE(lc.VA010_QualCheckMArk, 'N')" : "'N'";
            // 'Y' when the source line carries a quality plan.
            string qualFlagExpr = hasQualPlan
                ? "CASE WHEN il.VA010_QualityPlan_ID IS NOT NULL THEN 'Y' ELSE 'N' END"
                : "'N'";
            string qualCountExpr = hasQualPlan
                ? "NVL(SUM(CASE WHEN il.VA010_QualityPlan_ID IS NOT NULL THEN 1 ELSE 0 END), 0)"
                : "0";

            string sql = @"SELECT
                              c.M_InOutConfirm_ID,
                              c.DocumentNo,
                              c.DocStatus,
                              c.Processed,
                              c.ConfirmType,
                              c.Description,
                              M_InOut.M_InOut_ID    AS SourceInOutID,
                              M_InOut.DocumentNo    AS SourceDocumentNo,
                              M_InOut.IsSOTrx       AS SourceIsSales,
                              M_InOut.MovementDate  AS MovementDate,
                              C_BPartner.Name          AS PartyName,
                              wh.Name          AS WarehouseName,
                              (SELECT COUNT(*)
                                 FROM M_InOutLineConfirm lc
                                WHERE lc.M_InOutConfirm_ID = c.M_InOutConfirm_ID
                                  AND lc.IsActive          = 'Y')              AS LineCount,
                              (SELECT NVL(SUM(NVL(lc.TargetQty, 0)), 0)
                                 FROM M_InOutLineConfirm lc
                                WHERE lc.M_InOutConfirm_ID = c.M_InOutConfirm_ID
                                  AND lc.IsActive          = 'Y')              AS TargetQty,
                              (SELECT NVL(SUM(NVL(lc.ConfirmedQty, 0)), 0)
                                 FROM M_InOutLineConfirm lc
                                WHERE lc.M_InOutConfirm_ID = c.M_InOutConfirm_ID
                                  AND lc.IsActive          = 'Y')              AS ConfirmedQty,
                              (SELECT NVL(SUM(NVL(lc.DifferenceQty, 0)), 0)
                                 FROM M_InOutLineConfirm lc
                                WHERE lc.M_InOutConfirm_ID = c.M_InOutConfirm_ID
                                  AND lc.IsActive          = 'Y')              AS DifferenceQty,
                              (SELECT NVL(SUM(NVL(lc.ScrappedQty, 0)), 0)
                                 FROM M_InOutLineConfirm lc
                                WHERE lc.M_InOutConfirm_ID = c.M_InOutConfirm_ID
                                  AND lc.IsActive          = 'Y')              AS ScrappedQty,
                              (SELECT NVL(SUM(CASE WHEN " + qcMarkExpr + @" = 'Y' THEN 1 ELSE 0 END), 0)
                                 FROM M_InOutLineConfirm lc
                                WHERE lc.M_InOutConfirm_ID = c.M_InOutConfirm_ID
                                  AND lc.IsActive          = 'Y')              AS QcPassCount,
                              (SELECT " + qualCountExpr + @"
                                 FROM M_InOutLineConfirm lc
                                 LEFT OUTER JOIN M_InOutLine il ON (il.M_InOutLine_ID = lc.M_InOutLine_ID)
                                WHERE lc.M_InOutConfirm_ID = c.M_InOutConfirm_ID
                                  AND lc.IsActive          = 'Y')              AS QualityLineCount
                            FROM M_InOutConfirm c
                            LEFT OUTER JOIN M_InOut     ON (M_InOut.M_InOut_ID     = c.M_InOut_ID)
                            LEFT OUTER JOIN C_BPartner ON (C_BPartner.C_BPartner_ID  = M_InOut.C_BPartner_ID)
                            LEFT OUTER JOIN M_Warehouse wh ON (wh.M_Warehouse_ID = M_InOut.M_Warehouse_ID)
                            WHERE c.M_InOutConfirm_ID = @M_InOutConfirm_ID
                              AND c.IsActive          = 'Y'";

            // MRole access only on the primary physical table the user is fetching.
            sql = MRole.GetDefault(ctx).AddAccessSQL(
                sql, "c", MRole.SQL_FULLYQUALIFIED, MRole.SQL_RO);

            SqlParameter[] param = new SqlParameter[]
            {
                new SqlParameter("@M_InOutConfirm_ID", M_InOutConfirm_ID)
            };

            DataSet ds = DB.ExecuteDataset(sql, param, null);
            if (ds == null || ds.Tables.Count == 0 || ds.Tables[0].Rows.Count == 0)
                return result;

            DataRow r = ds.Tables[0].Rows[0];

            // ----- Header / identity -----
            result.M_InOutConfirm_ID = Util.GetValueOfInt(r["M_InOutConfirm_ID"]);
            result.DocumentNo        = Util.GetValueOfString(r["DocumentNo"]);
            result.StatusCode        = Util.GetValueOfString(r["DocStatus"]);
            result.Processed         = Util.GetValueOfString(r["Processed"]) == "Y";
            result.ConfirmTypeCode   = Util.GetValueOfString(r["ConfirmType"]);
            result.Description       = Util.GetValueOfString(r["Description"]);
            result.SourceInOutID     = Util.GetValueOfInt(r["SourceInOutID"]);
            result.SourceDocumentNo  = Util.GetValueOfString(r["SourceDocumentNo"]);
            // Sales trx => outbound Shipment; else inbound Goods Receipt.
            result.SourceTypeCode    = Util.GetValueOfString(r["SourceIsSales"]) == "Y" ? "SHP" : "GRN";
            result.MovementDate      = Util.GetValueOfDateTime(r["MovementDate"]);
            result.PartyName         = Util.GetValueOfString(r["PartyName"]);
            result.WarehouseName     = Util.GetValueOfString(r["WarehouseName"]);

            // ----- KPI aggregates -----
            result.LineCount     = Util.GetValueOfInt(r["LineCount"]);
            result.TargetQty     = Util.GetValueOfDecimal(r["TargetQty"]);
            result.ConfirmedQty  = Util.GetValueOfDecimal(r["ConfirmedQty"]);
            result.DifferenceQty = Util.GetValueOfDecimal(r["DifferenceQty"]);
            result.ScrappedQty   = Util.GetValueOfDecimal(r["ScrappedQty"]);
            result.QcPassCount   = Util.GetValueOfInt(r["QcPassCount"]);

            // ----- Quality mode -----
            result.QualityLineCount   = Util.GetValueOfInt(r["QualityLineCount"]);
            result.QualityApplicable  = result.QualityLineCount > 0;

            // Confirmed quantities are dimensionless counts; the panel renders
            // quantities with UOM precision and needs no currency precision.
            result.StdPrecision = 0;

            // ----- Confirmation lines -----
            result.Lines = LoadLines(M_InOutConfirm_ID, qcMarkExpr, qualFlagExpr);

            return result;
        }

        /// <summary>
        /// Loads M_InOutLineConfirm rows for the confirmation with product,
        /// locator and UOM metadata, target / confirmed / difference / scrapped
        /// quantities, the QC pass mark and the per-line quality-applicable flag.
        /// Child of an already authorized confirmation, so no separate MRole
        /// filter is applied here.
        /// </summary>
        /// <param name="M_InOutConfirm_ID">Owning confirmation id.</param>
        /// <param name="qcMarkExpr">QC-mark SQL expression (schema-aware).</param>
        /// <param name="qualFlagExpr">Quality-applicable SQL expression (schema-aware).</param>
        /// <returns>Ordered list of line rows (may be empty).</returns>
        private List<ShipGRNConfirmationLineData> LoadLines(
            int M_InOutConfirm_ID, string qcMarkExpr, string qualFlagExpr)
        {
            List<ShipGRNConfirmationLineData> lines = new List<ShipGRNConfirmationLineData>();

            string sql = @"SELECT
                              lc.M_InOutLineConfirm_ID,
                              il.Line           AS SourceLineNo,
                              lc.Description     AS LineDescription,
                              p.Value           AS ProductCode,
                              p.Name            AS ProductName,
                              loc.Value         AS LocatorCode,
                              COALESCE(loc.LocatorCombination, loc.Bin, loc.Value) AS LocatorName,
                              u.Name            AS UOMName,
                              NVL(u.StdPrecision, 0) AS UOMPrecision,
                              NVL(lc.TargetQty, 0)     AS TargetQty,
                              NVL(lc.ConfirmedQty, 0)  AS ConfirmedQty,
                              NVL(lc.DifferenceQty, 0) AS DifferenceQty,
                              NVL(lc.ScrappedQty, 0)   AS ScrappedQty,
                              " + qcMarkExpr + @"      AS QcMark,
                              " + qualFlagExpr + @"    AS QualityApplicable
                           FROM M_InOutLineConfirm lc
                           LEFT OUTER JOIN M_InOutLine il ON (il.M_InOutLine_ID = lc.M_InOutLine_ID)
                           LEFT OUTER JOIN M_Product  p   ON (p.M_Product_ID    = il.M_Product_ID)
                           LEFT OUTER JOIN C_UOM      u   ON (u.C_UOM_ID         = lc.C_UOM_ID)
                           LEFT OUTER JOIN M_Locator  loc ON (loc.M_Locator_ID   = lc.M_Locator_ID)
                           WHERE lc.M_InOutConfirm_ID = @M_InOutConfirm_ID
                             AND lc.IsActive          = 'Y'
                           ORDER BY il.Line";

            SqlParameter[] param = new SqlParameter[]
            {
                new SqlParameter("@M_InOutConfirm_ID", M_InOutConfirm_ID)
            };

            DataSet ds = DB.ExecuteDataset(sql, param, null);
            if (ds == null || ds.Tables.Count == 0)
                return lines;

            foreach (DataRow r in ds.Tables[0].Rows)
            {
                ShipGRNConfirmationLineData ln = new ShipGRNConfirmationLineData();
                ln.M_InOutLineConfirm_ID = Util.GetValueOfInt(r["M_InOutLineConfirm_ID"]);
                ln.Line              = Util.GetValueOfInt(r["SourceLineNo"]);
                ln.Description       = Util.GetValueOfString(r["LineDescription"]);
                ln.ProductCode       = Util.GetValueOfString(r["ProductCode"]);
                ln.ProductName       = Util.GetValueOfString(r["ProductName"]);
                ln.LocatorCode       = Util.GetValueOfString(r["LocatorCode"]);
                ln.LocatorName       = Util.GetValueOfString(r["LocatorName"]);
                ln.UOMName           = Util.GetValueOfString(r["UOMName"]);
                ln.UOMPrecision      = Util.GetValueOfInt(r["UOMPrecision"]);
                ln.TargetQty         = Util.GetValueOfDecimal(r["TargetQty"]);
                ln.ConfirmedQty      = Util.GetValueOfDecimal(r["ConfirmedQty"]);
                ln.DifferenceQty     = Util.GetValueOfDecimal(r["DifferenceQty"]);
                ln.ScrappedQty       = Util.GetValueOfDecimal(r["ScrappedQty"]);
                ln.QcMark            = Util.GetValueOfString(r["QcMark"]);
                ln.QualityApplicable = Util.GetValueOfString(r["QualityApplicable"]) == "Y";

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

        public class ShipGRNConfirmationLineData
        {
            public int      M_InOutLineConfirm_ID { get; set; }
            public int      Line              { get; set; }   // source line no
            public string   Description       { get; set; }
            public string   ProductCode       { get; set; }   // SKU
            public string   ProductName       { get; set; }
            public string   LocatorCode       { get; set; }
            public string   LocatorName       { get; set; }
            public string   UOMName           { get; set; }
            public int      UOMPrecision      { get; set; }
            public decimal  TargetQty         { get; set; }
            public decimal  ConfirmedQty      { get; set; }
            public decimal  DifferenceQty     { get; set; }
            public decimal  ScrappedQty       { get; set; }
            public string   QcMark            { get; set; }   // 'Y' = passed
            public bool     QualityApplicable { get; set; }
        }

        public class ShipGRNConfirmationOverviewData
        {
            // Header / identity
            public int       M_InOutConfirm_ID { get; set; }
            public string    DocumentNo        { get; set; }
            public string    StatusCode        { get; set; }   // DocStatus code
            public bool      Processed         { get; set; }
            public string    ConfirmTypeCode   { get; set; }   // ConfirmType code
            public string    Description       { get; set; }
            public int       SourceInOutID     { get; set; }
            public string    SourceDocumentNo  { get; set; }
            public string    SourceTypeCode    { get; set; }   // GRN | SHP
            public DateTime? MovementDate      { get; set; }
            public string    PartyName         { get; set; }
            public string    WarehouseName     { get; set; }

            // Quality mode
            public int       QualityLineCount  { get; set; }
            public bool      QualityApplicable { get; set; }

            // Precision
            public int       StdPrecision      { get; set; }

            // KPI aggregates
            public int       LineCount     { get; set; }
            public decimal   TargetQty     { get; set; }
            public decimal   ConfirmedQty  { get; set; }
            public decimal   DifferenceQty { get; set; }
            public decimal   ScrappedQty   { get; set; }
            public int       QcPassCount   { get; set; }

            // Collections
            public List<ShipGRNConfirmationLineData> Lines { get; set; }
        }
    }
}
