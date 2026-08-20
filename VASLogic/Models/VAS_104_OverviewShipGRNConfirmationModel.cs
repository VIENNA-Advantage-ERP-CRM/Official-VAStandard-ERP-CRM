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
///   VAI163   2026-08-12  - Lines carry their Attribute Set Instance
///                          (M_InOutLine.M_AttributeSetInstance_ID), joined only
///                          for a REAL instance (id > 0) so a line with no
///                          attributes cannot pick up the zero-record's "--"
///                          description. Follows VAS_106.
///                        - Added QualityParams (LoadQualityParams): the per-LINE
///                          inspection rows from VA010_ShipConfParameters — the QC
///                          date, the test parameter, the acceptable value, the
///                          actual value and the quantity to verify — keyed to the
///                          confirmation line they belong to. Ported from VAS_099's
///                          receipt-scoped loader; every VA010 table and display
///                          column is resolved through the dictionary first, so a
///                          schema without the quality module simply reports none.
///                          VA010_ActualValue is a VARCHAR holding a
///                          VA010_TestPrmtrList_ID rather than free text, so it is
///                          deliberately NOT joined in SQL (a non-numeric value
///                          would abort the statement on the cast) — the ids are
///                          parsed in managed code and resolved in one follow-up.
///                        - Each line reports QcStatusCode, derived from its own
///                          parameters by comparing the actual value against the
///                          acceptable one (P / F / N), and "" for a line that has
///                          no quality parameters at all. The panel shows a line
///                          status only where quality actually applies.
///                        - Added IsInDispute (M_InOutConfirm.IsInDispute), so the
///                          panel can say a confirmation is disputed.
///                        - Added ConfirmTypeName: the DICTIONARY's own name for
///                          the confirmation type stored on the record, resolved
///                          through the COLUMN's reference list (LoadRefListName,
///                          ported from VAS_106). The panel carried its own map of
///                          the four codes, so a customer-added type showed a bare
///                          code and no translation was ever applied.
///                        - Added Notes (LoadNotes) and Activity (LoadActivity):
///                          the confirmation's own note and its line notes, and a
///                          feed of chat entries (CM_ChatEntry) merged with the
///                          document's create / complete milestones.
///   VAI163   2026-08-13  - Activity gains the confirmation's field-level edit
///                          history (LoadFieldChangeActivity, AD_ChangeLog): one
///                          "Changed" row per changed column carrying its label,
///                          its old value and its new one, for the header AND its
///                          LINES — a confirmation's meaningful edits are its
///                          confirmed / scrapped quantities, and those live on
///                          the lines, so a header-only trail reported almost
///                          nothing. Line rows are scoped with the source line
///                          number and product. The feed previously said only
///                          WHEN the document was last saved (the Completed
///                          milestone's Updated stamp) and never what changed.
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
                              c.IsInDispute,
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
            // The dictionary's own name for that code, so the panel shows exactly
            // what the confirmation screen shows — customer-added types and
            // translations included. "" leaves the panel on its built-in map.
            result.ConfirmTypeName   = LoadRefListName(ctx, "M_InOutConfirm", "ConfirmType",
                                                       result.ConfirmTypeCode);
            result.IsInDispute       = Util.GetValueOfString(r["IsInDispute"]) == "Y";
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

            // ----- Quality parameters, attached to the line they belong to -----
            //        One query for the whole confirmation, then distributed: a
            //        query per line would be one round trip per row.
            AttachQualityParams(M_InOutConfirm_ID, result.Lines);

            // ----- Notes + activity -----
            result.Notes    = LoadNotes(M_InOutConfirm_ID);
            result.Activity = LoadActivity(M_InOutConfirm_ID);

            return result;
        }

        /// <summary>
        /// Resolves a window's AD_Window_ID from its name (AD_Window.Name) for the
        /// panel's source-document link.
        ///
        /// The source is an M_InOut record on either side of the trade — a goods
        /// receipt or a delivery order — and those are two different screens on one
        /// table, which the browser's zoom lookup cannot choose between. Naming the
        /// window is what settles it.
        ///
        /// Restricted to windows this tenant can see (AD_Client_ID 0 or its own),
        /// preferring the tenant's own row over the system one. Whether the ROLE may
        /// open it is the platform's call, made when the window is started. Ported
        /// from VAS_106.
        /// </summary>
        /// <param name="ctx">User context (client).</param>
        /// <param name="windowName">Window name to resolve.</param>
        /// <returns>The window id, or 0 when the name resolves to nothing.</returns>
        public int GetWindowId(Ctx ctx, string windowName)
        {
            if (string.IsNullOrEmpty(windowName)) return 0;
            try
            {
                string sql = @"SELECT w.AD_Window_ID
                                 FROM AD_Window w
                                WHERE w.Name         = @Name
                                  AND w.IsActive     = 'Y'
                                  AND w.AD_Client_ID IN (0, @AD_Client_ID)
                                ORDER BY w.AD_Client_ID DESC";
                SqlParameter[] param = new SqlParameter[]
                {
                    new SqlParameter("@Name", windowName.Trim()),
                    new SqlParameter("@AD_Client_ID", ctx == null ? 0 : ctx.GetAD_Client_ID())
                };
                DataSet ds = DB.ExecuteDataset(sql, param, null);
                if (ds == null || ds.Tables.Count == 0 || ds.Tables[0].Rows.Count == 0)
                    return 0;
                return Util.GetValueOfInt(ds.Tables[0].Rows[0]["AD_Window_ID"]);
            }
            catch (Exception ex)
            {
                _log.Severe("GetWindowId (" + windowName + "): " + ex.Message);
                return 0;
            }
        }

        /// <summary>
        /// The dictionary's display name for one value of a reference-list column
        /// — the label the record screen shows for it.
        ///
        /// The reference is reached through the COLUMN
        /// (AD_Column.AD_Reference_Value_ID), not by hard-coding a reference id, so
        /// a deployment that points the column at its own list is read correctly.
        /// Translated where the user's language has a translation.
        ///
        /// Returns "" when the value, the column or the list entry cannot be
        /// resolved, which leaves the panel on its own built-in labels. Ported
        /// from VAS_106.
        /// </summary>
        /// <param name="ctx">User context, for the language.</param>
        /// <param name="tableName">Table owning the column, e.g. "M_InOutConfirm".</param>
        /// <param name="columnName">Reference-list column, e.g. "ConfirmType".</param>
        /// <param name="value">The stored value, e.g. "SI".</param>
        /// <returns>The list entry's name, or "".</returns>
        private string LoadRefListName(Ctx ctx, string tableName, string columnName, string value)
        {
            if (string.IsNullOrEmpty(value)) return "";
            try
            {
                string lang = (ctx == null) ? "" : ctx.GetAD_Language();

                // Each bind name occurs exactly once: positional binding gives a
                // repeated name a second, unfilled placeholder.
                string sql = @"SELECT COALESCE(rlt.Name, rl.Name) AS Name
                                 FROM AD_Ref_List rl
                                INNER JOIN AD_Column c
                                        ON (c.AD_Reference_Value_ID = rl.AD_Reference_ID)
                                INNER JOIN AD_Table t
                                        ON (t.AD_Table_ID = c.AD_Table_ID)
                                 LEFT OUTER JOIN AD_Ref_List_Trl rlt
                                        ON (rlt.AD_Ref_List_ID = rl.AD_Ref_List_ID
                                            AND rlt.AD_Language = @AD_Language)
                                WHERE UPPER(t.TableName)  = UPPER(@TableName)
                                  AND UPPER(c.ColumnName) = UPPER(@ColumnName)
                                  AND rl.Value            = @Value
                                  AND rl.IsActive         = 'Y'";
                SqlParameter[] param = new SqlParameter[]
                {
                    new SqlParameter("@AD_Language", lang),
                    new SqlParameter("@TableName", tableName),
                    new SqlParameter("@ColumnName", columnName),
                    new SqlParameter("@Value", value)
                };
                DataSet ds = DB.ExecuteDataset(sql, param, null);
                if (ds == null || ds.Tables.Count == 0 || ds.Tables[0].Rows.Count == 0) return "";
                return Util.GetValueOfString(ds.Tables[0].Rows[0]["Name"]);
            }
            catch (Exception ex)
            {
                _log.Severe("LoadRefListName (" + tableName + "." + columnName + "=" + value + "): " + ex.Message);
                return "";
            }
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
                              asi.Description   AS AttributeSetInstance,
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
                           -- Only a REAL instance is joined: id 0 is the
                           -- dictionary's no-attributes row, whose description is a
                           -- bare double dash that would otherwise print against
                           -- every line carrying no attributes at all.
                           LEFT OUTER JOIN M_AttributeSetInstance asi
                                  ON (asi.M_AttributeSetInstance_ID = il.M_AttributeSetInstance_ID
                                      AND il.M_AttributeSetInstance_ID > 0)
                           WHERE lc.M_InOutConfirm_ID = @M_InOutConfirm_ID
                             AND lc.IsActive          = 'Y'
                           ORDER BY il.Line";

            SqlParameter[] param = new SqlParameter[]
            {
                new SqlParameter("@M_InOutConfirm_ID", M_InOutConfirm_ID)
            };

            try
            {
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
                    ln.AttributeSetInstance = Util.GetValueOfString(r["AttributeSetInstance"]);
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
                    ln.QualityParams     = new List<ConfirmationQualityParamData>();

                    lines.Add(ln);
                }
            }
            catch (Exception ex)
            {
                _log.Severe("LoadLines (M_InOutConfirm_ID=" + M_InOutConfirm_ID + "): " + ex.Message);
            }
            return lines;
        }

        // ================================================================= //
        //  Quality parameters (VA010_ShipConfParameters)                    //
        // ================================================================= //

        /// <summary>
        /// Loads the confirmation's quality-inspection rows and hangs each one off
        /// the line it belongs to, then derives that line's QC status from them.
        ///
        /// Read in ONE query for the whole confirmation and distributed here: a
        /// query per line would cost a round trip per row.
        /// </summary>
        /// <param name="M_InOutConfirm_ID">Owning confirmation id.</param>
        /// <param name="lines">Lines to attach the parameters to.</param>
        private void AttachQualityParams(int M_InOutConfirm_ID, List<ShipGRNConfirmationLineData> lines)
        {
            if (lines == null || lines.Count == 0) return;

            List<ConfirmationQualityParamData> all = LoadQualityParams(M_InOutConfirm_ID);

            if (all.Count > 0)
            {
                Dictionary<int, ShipGRNConfirmationLineData> byId =
                    new Dictionary<int, ShipGRNConfirmationLineData>();
                foreach (ShipGRNConfirmationLineData ln in lines)
                {
                    if (!byId.ContainsKey(ln.M_InOutLineConfirm_ID))
                        byId.Add(ln.M_InOutLineConfirm_ID, ln);
                }

                foreach (ConfirmationQualityParamData q in all)
                {
                    ShipGRNConfirmationLineData ln;
                    if (byId.TryGetValue(q.M_InOutLineConfirm_ID, out ln))
                        ln.QualityParams.Add(q);
                }
            }

            // A line's status comes from its OWN parameters. A line with none
            // reports "" — the panel shows a status only where quality applies.
            foreach (ShipGRNConfirmationLineData ln in lines)
                ln.QcStatusCode = DeriveLineQcStatus(ln.QualityParams);
        }

        /// <summary>
        /// A confirmation line's quality verdict, read off its own parameters:
        ///   ""  no quality parameters at all — quality does not apply to the line
        ///   "N" at least one parameter still has no actual value recorded
        ///   "F" every parameter is inspected and at least one missed its
        ///       acceptable value
        ///   "P" every parameter is inspected and every one matched
        /// Pending outranks failed: a line still being inspected has not failed
        /// yet, whatever the parameters read so far.
        /// </summary>
        private string DeriveLineQcStatus(List<ConfirmationQualityParamData> parameters)
        {
            if (parameters == null || parameters.Count == 0) return "";

            bool anyPending = false, anyFailed = false;
            foreach (ConfirmationQualityParamData q in parameters)
            {
                if (q.StatusCode == "N") anyPending = true;
                else if (q.StatusCode == "F") anyFailed = true;
            }
            if (anyPending) return "N";
            return anyFailed ? "F" : "P";
        }

        /// <summary>
        /// The inspection rows recorded against this confirmation's lines
        /// (VA010_ShipConfParameters), carrying the QC date, the test parameter,
        /// the acceptable value, the actual value and the quantity to verify.
        ///
        /// The whole VA010 schema is optional, so the table and its display columns
        /// are resolved through the dictionary first; anything missing yields an
        /// empty list and the panel simply shows no parameters.
        ///
        /// VA010_ActualValue is a VARCHAR reference holding the chosen
        /// VA010_TestPrmtrList_ID rather than free text. It is deliberately NOT
        /// joined in SQL — a non-numeric value would abort the query on a numeric
        /// cast — so the ids are parsed in managed code and resolved to names in
        /// one follow-up lookup. Ported from VAS_099, scoped to the confirmation.
        /// </summary>
        /// <param name="M_InOutConfirm_ID">Owning confirmation id.</param>
        /// <returns>Inspection rows (empty when VA010 is absent).</returns>
        private List<ConfirmationQualityParamData> LoadQualityParams(int M_InOutConfirm_ID)
        {
            List<ConfirmationQualityParamData> rows = new List<ConfirmationQualityParamData>();
            if (!TableExists("VA010_ShipConfParameters")) return rows;

            // Display columns differ between VA010 revisions — probe for the one
            // this schema actually has, exactly as VAS_099 does.
            string paramNameCol = FindDisplayColumn("VA010_TestParameter",
                new string[] { "VA010_TestPrmtrName", "Name", "Description", "Value" });
            string valueNameCol = FindDisplayColumn("VA010_TestPrmtrList",
                new string[] { "VA010_ParameterValue", "Name", "Description", "Value" });

            string paramNameExpr = !string.IsNullOrEmpty(paramNameCol)
                ? "tp." + paramNameCol : "CAST(NULL AS VARCHAR(255))";
            string paramJoin = !string.IsNullOrEmpty(paramNameCol)
                ? @"LEFT OUTER JOIN VA010_TestParameter tp
                            ON (tp.VA010_TestParameter_ID = qp.VA010_TestParameter_ID
                                AND tp.IsActive = 'Y')"
                : "";

            string acceptExpr = !string.IsNullOrEmpty(valueNameCol)
                ? "acc." + valueNameCol : "CAST(NULL AS VARCHAR(255))";
            string acceptJoin = !string.IsNullOrEmpty(valueNameCol)
                ? @"LEFT OUTER JOIN VA010_TestPrmtrList acc
                            ON (acc.VA010_TestPrmtrList_ID = qp.VA010_TestPrmtrList_ID
                                AND acc.IsActive = 'Y')"
                : "";

            // Only sort by the parameter name when there actually is such a column.
            string orderBy = !string.IsNullOrEmpty(paramNameCol)
                ? "ORDER BY lc.M_InOutLineConfirm_ID, " + paramNameExpr
                : "ORDER BY lc.M_InOutLineConfirm_ID";

            try
            {
                string sql = @"SELECT
                                  qp.M_InOutLineConfirm_ID,
                                  " + paramNameExpr + @"  AS ParameterName,
                                  NVL(qp.VA010_QuantityToVerify, 0) AS QuantityToVerify,
                                  qp.VA010_TestPrmtrList_ID AS AcceptableValueId,
                                  " + acceptExpr + @"     AS AcceptableValue,
                                  qp.VA010_ActualValue  AS ActualValueRaw,
                                  qp.VA010_QAQCDate     AS QAQCDate,
                                  qp.Remark             AS Remark
                               FROM VA010_ShipConfParameters qp
                               INNER JOIN M_InOutLineConfirm lc
                                       ON (lc.M_InOutLineConfirm_ID = qp.M_InOutLineConfirm_ID
                                           AND lc.IsActive = 'Y')
                               " + paramJoin + @"
                               " + acceptJoin + @"
                               WHERE lc.M_InOutConfirm_ID = @M_InOutConfirm_ID
                                 AND qp.IsActive          = 'Y'
                               " + orderBy;

                SqlParameter[] param = new SqlParameter[]
                {
                    new SqlParameter("@M_InOutConfirm_ID", M_InOutConfirm_ID)
                };

                DataSet ds = DB.ExecuteDataset(sql, param, null);
                if (ds == null || ds.Tables.Count == 0) return rows;

                foreach (DataRow r in ds.Tables[0].Rows)
                {
                    ConfirmationQualityParamData q = new ConfirmationQualityParamData();
                    q.M_InOutLineConfirm_ID = Util.GetValueOfInt(r["M_InOutLineConfirm_ID"]);
                    q.ParameterName     = Util.GetValueOfString(r["ParameterName"]);
                    q.QuantityToVerify  = Util.GetValueOfDecimal(r["QuantityToVerify"]);
                    q.AcceptableValueId = Util.GetValueOfInt(r["AcceptableValueId"]);
                    q.AcceptableValue   = Util.GetValueOfString(r["AcceptableValue"]);
                    q.QAQCDate          = Util.GetValueOfDateTime(r["QAQCDate"]);
                    q.Remark            = Util.GetValueOfString(r["Remark"]);

                    // Reference into VA010_TestPrmtrList; anything unparseable is
                    // treated as "not yet inspected".
                    int actualId;
                    string raw = Util.GetValueOfString(r["ActualValueRaw"]);
                    q.ActualValueId = int.TryParse(raw, out actualId) && actualId > 0 ? actualId : 0;

                    rows.Add(q);
                }

                ResolveActualValueNames(rows, valueNameCol);
                foreach (ConfirmationQualityParamData q in rows) q.StatusCode = DeriveQcStatus(q);
            }
            catch (Exception ex)
            {
                _log.Severe("LoadQualityParams (M_InOutConfirm_ID=" + M_InOutConfirm_ID + "): " + ex.Message);
                return new List<ConfirmationQualityParamData>();
            }
            return rows;
        }

        /// <summary>
        /// Fills <see cref="ConfirmationQualityParamData.ActualValue"/> for the
        /// rows whose actual value parsed to a VA010_TestPrmtrList id, in one
        /// lookup. Ported from VAS_099.
        /// </summary>
        private void ResolveActualValueNames(List<ConfirmationQualityParamData> rows, string valueNameCol)
        {
            if (rows.Count == 0 || string.IsNullOrEmpty(valueNameCol)) return;

            List<string> ids = new List<string>();
            foreach (ConfirmationQualityParamData q in rows)
            {
                string id = q.ActualValueId.ToString();
                if (q.ActualValueId > 0 && !ids.Contains(id)) ids.Add(id);
            }
            if (ids.Count == 0) return;

            try
            {
                // The id list is built from parsed integers only, so it is safe to
                // inline — no user text reaches the statement.
                string sql = @"SELECT VA010_TestPrmtrList_ID AS ValueId,
                                      " + valueNameCol + @" AS ValueName
                                 FROM VA010_TestPrmtrList
                                WHERE IsActive = 'Y'
                                  AND VA010_TestPrmtrList_ID IN (" + string.Join(",", ids.ToArray()) + ")";

                DataSet ds = DB.ExecuteDataset(sql, null, null);
                if (ds == null || ds.Tables.Count == 0) return;

                Dictionary<int, string> names = new Dictionary<int, string>();
                foreach (DataRow r in ds.Tables[0].Rows)
                {
                    names[Util.GetValueOfInt(r["ValueId"])] = Util.GetValueOfString(r["ValueName"]);
                }

                foreach (ConfirmationQualityParamData q in rows)
                {
                    if (q.ActualValueId > 0 && names.ContainsKey(q.ActualValueId))
                        q.ActualValue = names[q.ActualValueId];
                }
            }
            catch (Exception ex)
            {
                _log.Severe("ResolveActualValueNames: " + ex.Message);
            }
        }

        /// <summary>
        /// One parameter's verdict: P when the inspected value is the configured
        /// acceptable one, F when it is not, N while nothing has been recorded.
        /// Ported from VAS_099.
        /// </summary>
        private string DeriveQcStatus(ConfirmationQualityParamData q)
        {
            if (q.ActualValueId <= 0) return "N";
            return q.ActualValueId == q.AcceptableValueId ? "P" : "F";
        }

        /// <summary>
        /// Returns the first of the candidate columns that exists on the table, or
        /// "" when the table has none of them. Used where an optional module names
        /// the same concept differently across its revisions.
        /// </summary>
        private string FindDisplayColumn(string tableName, string[] candidates)
        {
            for (int i = 0; i < candidates.Length; i++)
            {
                if (ColumnExists(tableName, candidates[i])) return candidates[i];
            }
            return "";
        }

        /// <summary>
        /// Returns true when the given table exists in the AD_Table dictionary. A
        /// DB issue degrades to "absent" (false), which simply hides the optional
        /// section that depends on it.
        /// </summary>
        private bool TableExists(string tableName)
        {
            try
            {
                string sql = @"SELECT COUNT(*) FROM AD_Table
                                WHERE UPPER(TableName) = UPPER(@TableName)";
                SqlParameter[] param = new SqlParameter[]
                {
                    new SqlParameter("@TableName", tableName)
                };
                return Util.GetValueOfInt(DB.ExecuteScalar(sql, param, null)) > 0;
            }
            catch (Exception ex)
            {
                _log.Severe("TableExists (" + tableName + "): " + ex.Message);
                return false;
            }
        }

        // ================================================================= //
        //  Notes + activity                                                 //
        // ================================================================= //

        /// <summary>Single-parameter helper for the confirmation-scoped queries.</summary>
        private SqlParameter[] ConfirmParam(int M_InOutConfirm_ID)
        {
            return new SqlParameter[] { new SqlParameter("@M_InOutConfirm_ID", M_InOutConfirm_ID) };
        }

        /// <summary>
        /// Loads the confirmation's notes: the document's own note
        /// (M_InOutConfirm.Description) plus each line's note (product name + the
        /// line's description). Composed in C# so the SQL stays portable.
        /// </summary>
        private List<NoteData> LoadNotes(int M_InOutConfirm_ID)
        {
            List<NoteData> notes = new List<NoteData>();
            try
            {
                string sql = @"SELECT
                                  c.Description  AS HeaderNote,
                                  il.Line        AS LineNo,
                                  lc.Description AS LineNote,
                                  p.Name         AS ProductName
                                FROM M_InOutConfirm c
                                LEFT OUTER JOIN M_InOutLineConfirm lc
                                       ON (lc.M_InOutConfirm_ID = c.M_InOutConfirm_ID
                                           AND lc.IsActive = 'Y')
                                LEFT OUTER JOIN M_InOutLine il ON (il.M_InOutLine_ID = lc.M_InOutLine_ID)
                                LEFT OUTER JOIN M_Product   p  ON (p.M_Product_ID    = il.M_Product_ID)
                                WHERE c.M_InOutConfirm_ID = @M_InOutConfirm_ID
                                  AND c.IsActive          = 'Y'
                                ORDER BY il.Line";
                DataSet ds = DB.ExecuteDataset(sql, ConfirmParam(M_InOutConfirm_ID), null);
                if (ds == null || ds.Tables.Count == 0) return notes;

                bool headerAdded = false;
                foreach (DataRow r in ds.Tables[0].Rows)
                {
                    // Header note once — it repeats across every joined line.
                    if (!headerAdded)
                    {
                        string headerNote = Util.GetValueOfString(r["HeaderNote"]);
                        if (!string.IsNullOrEmpty(headerNote))
                            notes.Add(new NoteData { NoteType = "header", Text = headerNote.Trim() });
                        headerAdded = true;
                    }

                    string lineNote = Util.GetValueOfString(r["LineNote"]);
                    if (string.IsNullOrEmpty(lineNote)) continue;

                    string prod = Util.GetValueOfString(r["ProductName"]);
                    string text = string.IsNullOrEmpty(prod)
                        ? lineNote.Trim()
                        : prod.Trim() + " — " + lineNote.Trim();
                    notes.Add(new NoteData { NoteType = "line", Text = text });
                }
            }
            catch (Exception ex)
            {
                _log.Severe("LoadNotes (M_InOutConfirm_ID=" + M_InOutConfirm_ID + "): " + ex.Message);
            }
            return notes;
        }

        /// <summary>
        /// Builds the confirmation's activity feed: its chat entries
        /// (CM_ChatEntry) merged with the document's own create / complete
        /// milestones, newest first. Each source is guarded so a DB-level issue
        /// with one degrades to a partial feed rather than breaking the overview.
        /// </summary>
        private List<ActivityData> LoadActivity(int M_InOutConfirm_ID)
        {
            // A runaway guard, not a headline count.
            const int MAX_ENTRIES = 200;
            List<ActivityData> activity = new List<ActivityData>();

            LoadNoteActivity(M_InOutConfirm_ID, activity);
            LoadMilestoneActivity(M_InOutConfirm_ID, activity);
            LoadFieldChangeActivity(M_InOutConfirm_ID, activity);
            // Appointments, tasks, calls, letters AND mails filed against the
            // confirmation. Mails come from here too: this panel has no mail loader
            // of its own, so without them the correspondence trail would carry the
            // letters and silently drop the e-mails beside them.
            LoadSharedSourceActivity(M_InOutConfirm_ID, activity);

            activity.Sort((a, b) =>
                b.EventTime.GetValueOrDefault(DateTime.MinValue)
                 .CompareTo(a.EventTime.GetValueOrDefault(DateTime.MinValue)));

            if (activity.Count > MAX_ENTRIES)
                activity = activity.GetRange(0, MAX_ENTRIES);
            return activity;
        }

        private void LoadNoteActivity(int M_InOutConfirm_ID, List<ActivityData> list)
        {
            try
            {
                // The author resolves from CM_ChatEntry.AD_User_ID falling back to
                // CreatedBy: a note logged by the platform itself leaves AD_User_ID
                // null, and those appeared in the feed with no name against them.
                string sql = @"SELECT ce.CharacterData, ce.Created, u.Name AS UserName
                                 FROM CM_ChatEntry ce
                                INNER JOIN CM_Chat ch ON (ce.CM_Chat_ID = ch.CM_Chat_ID)
                                 LEFT OUTER JOIN AD_User u
                                        ON (u.AD_User_ID = COALESCE(ce.AD_User_ID, ce.CreatedBy))
                                WHERE ch.AD_Table_ID =
                                      (SELECT t.AD_Table_ID FROM AD_Table t
                                        WHERE t.TableName = 'M_InOutConfirm')
                                  AND ch.Record_ID = @M_InOutConfirm_ID
                                  AND ce.IsActive  = 'Y'";
                DataSet ds = DB.ExecuteDataset(sql, ConfirmParam(M_InOutConfirm_ID), null);
                if (ds == null || ds.Tables.Count == 0) return;
                foreach (DataRow r in ds.Tables[0].Rows)
                {
                    list.Add(new ActivityData
                    {
                        EventType = "Note",
                        Title     = Util.GetValueOfString(r["CharacterData"]),
                        ActorName = Util.GetValueOfString(r["UserName"]),
                        EventTime = Util.GetValueOfDateTime(r["Created"])
                    });
                }
            }
            catch (Exception ex)
            {
                _log.Severe("LoadNoteActivity (M_InOutConfirm_ID=" + M_InOutConfirm_ID + "): " + ex.Message);
            }
        }

        private void LoadMilestoneActivity(int M_InOutConfirm_ID, List<ActivityData> list)
        {
            try
            {
                string sql = @"SELECT c.Created, c.Updated, c.DocStatus, c.DocumentNo,
                                      cu.Name AS CreatedByName, uu.Name AS UpdatedByName
                                 FROM M_InOutConfirm c
                                 LEFT OUTER JOIN AD_User cu ON (c.CreatedBy = cu.AD_User_ID)
                                 LEFT OUTER JOIN AD_User uu ON (c.UpdatedBy = uu.AD_User_ID)
                                WHERE c.M_InOutConfirm_ID = @M_InOutConfirm_ID";
                DataSet ds = DB.ExecuteDataset(sql, ConfirmParam(M_InOutConfirm_ID), null);
                if (ds == null || ds.Tables.Count == 0 || ds.Tables[0].Rows.Count == 0) return;

                DataRow r = ds.Tables[0].Rows[0];
                list.Add(new ActivityData
                {
                    EventType = "Created",
                    Title     = Util.GetValueOfString(r["DocumentNo"]),
                    ActorName = Util.GetValueOfString(r["CreatedByName"]),
                    EventTime = Util.GetValueOfDateTime(r["Created"])
                });

                string docStatus = Util.GetValueOfString(r["DocStatus"]);
                if (docStatus == "CO" || docStatus == "CL")
                {
                    list.Add(new ActivityData
                    {
                        EventType = "Completed",
                        Title     = Util.GetValueOfString(r["DocumentNo"]),
                        ActorName = Util.GetValueOfString(r["UpdatedByName"]),
                        EventTime = Util.GetValueOfDateTime(r["Updated"])
                    });
                }
            }
            catch (Exception ex)
            {
                _log.Severe("LoadMilestoneActivity (M_InOutConfirm_ID=" + M_InOutConfirm_ID + "): " + ex.Message);
            }
        }

        /// <summary>
        /// The confirmation's field-level edit history, read from the platform's
        /// change log (AD_ChangeLog): one row per changed COLUMN carrying the
        /// dictionary's label for it, the value it held before and the value it
        /// holds now, when the change was saved and who saved it.
        ///
        /// Both the header (M_InOutConfirm) and its LINES (M_InOutLineConfirm) are
        /// read. A confirmation's meaningful edits are its confirmed / scrapped
        /// quantities, and those live on the lines — a header-only trail would
        /// report almost nothing. A line change is labelled with its source line
        /// number and product so the reader can tell which row moved.
        ///
        /// Rows are NOT collapsed to one per save: the panel is asked for exactly
        /// what changed, so a save touching three columns is three rows. This is
        /// what replaces reading the single M_InOutConfirm.Updated stamp, which
        /// could only ever report the LAST save and never said what it touched.
        ///
        /// Silently degrades when change logging is off for the table — there are
        /// simply no rows, and the feed keeps its milestones.
        /// </summary>
        /// <param name="M_InOutConfirm_ID">Selected confirmation id.</param>
        /// <param name="list">Activity list being populated.</param>
        private void LoadFieldChangeActivity(int M_InOutConfirm_ID, List<ActivityData> list)
        {
            // ----- Header edits -----
            try
            {
                string sql = @"SELECT cl.Created,
                                      cl.OldValue,
                                      cl.NewValue,
                                      COALESCE(col.Name, col.ColumnName) AS FieldName,
                                      col.ColumnName             AS FieldColumn,
                                      col.AD_Reference_ID        AS RefType,
                                      col.AD_Reference_Value_ID  AS RefValueId,
                                      u.Name AS UserName
                                 FROM AD_ChangeLog cl
                                INNER JOIN AD_Table adt ON (adt.AD_Table_ID = cl.AD_Table_ID)
                                 LEFT OUTER JOIN AD_Column col ON (col.AD_Column_ID = cl.AD_Column_ID)
                                 LEFT OUTER JOIN AD_User  u    ON (u.AD_User_ID     = cl.CreatedBy)
                                WHERE cl.Record_ID = @M_InOutConfirm_ID
                                  AND adt.TableName = 'M_InOutConfirm'
                                  AND COALESCE(cl.IsActive, 'Y') = 'Y'
                                ORDER BY cl.Created DESC";
                DataSet ds = DB.ExecuteDataset(sql, ConfirmParam(M_InOutConfirm_ID), null);
                if (ds != null && ds.Tables.Count > 0)
                {
                    foreach (DataRow r in ds.Tables[0].Rows) AddChangeRow(r, "", list);
                }
            }
            catch (Exception ex)
            {
                _log.Severe("LoadFieldChangeActivity/header (M_InOutConfirm_ID="
                            + M_InOutConfirm_ID + "): " + ex.Message);
            }

            // ----- Line edits -----
            //
            // The confirm-line ids are reached through the join rather than a
            // second bind, so the statement carries its bind name exactly once:
            // positional binding gives a repeated name a second, unfilled
            // placeholder.
            try
            {
                string sql = @"SELECT cl.Created,
                                      cl.OldValue,
                                      cl.NewValue,
                                      COALESCE(col.Name, col.ColumnName) AS FieldName,
                                      col.ColumnName             AS FieldColumn,
                                      col.AD_Reference_ID        AS RefType,
                                      col.AD_Reference_Value_ID  AS RefValueId,
                                      u.Name  AS UserName,
                                      il.Line AS LineNo,
                                      p.Name  AS ProductName
                                 FROM AD_ChangeLog cl
                                INNER JOIN AD_Table adt ON (adt.AD_Table_ID = cl.AD_Table_ID)
                                INNER JOIN M_InOutLineConfirm lc
                                        ON (lc.M_InOutLineConfirm_ID = cl.Record_ID)
                                 LEFT OUTER JOIN M_InOutLine il ON (il.M_InOutLine_ID = lc.M_InOutLine_ID)
                                 LEFT OUTER JOIN M_Product   p  ON (p.M_Product_ID    = il.M_Product_ID)
                                 LEFT OUTER JOIN AD_Column   col ON (col.AD_Column_ID = cl.AD_Column_ID)
                                 LEFT OUTER JOIN AD_User     u   ON (u.AD_User_ID     = cl.CreatedBy)
                                WHERE adt.TableName = 'M_InOutLineConfirm'
                                  AND COALESCE(cl.IsActive, 'Y') = 'Y'
                                  AND lc.M_InOutConfirm_ID = @M_InOutConfirm_ID
                                ORDER BY cl.Created DESC";
                DataSet ds = DB.ExecuteDataset(sql, ConfirmParam(M_InOutConfirm_ID), null);
                if (ds != null && ds.Tables.Count > 0)
                {
                    foreach (DataRow r in ds.Tables[0].Rows)
                    {
                        int lineNo = Util.GetValueOfInt(r["LineNo"]);
                        string scope = lineNo > 0 ? "#" + lineNo : "";
                        string prod  = Util.GetValueOfString(r["ProductName"]);
                        if (!string.IsNullOrEmpty(prod))
                            scope = (scope.Length > 0 ? scope + " " : "") + prod.Trim();
                        AddChangeRow(r, scope, list);
                    }
                }
            }
            catch (Exception ex)
            {
                _log.Severe("LoadFieldChangeActivity/lines (M_InOutConfirm_ID="
                            + M_InOutConfirm_ID + "): " + ex.Message);
            }
        }

        /// <summary>
        /// Resolves a change-log value into the text the field shows — a reference
        /// into the referenced record's identifier, a list code into its label, a
        /// timestamp into the date alone. Shared with the other overview panels
        /// (VAS_ChangeLogValueModel). One per request, so its caches last exactly
        /// as long as the feed being built.
        /// </summary>
        private readonly VAS_ChangeLogValueModel _changeValues = new VAS_ChangeLogValueModel();

        /// <summary>Reads the appointment / task / call / letter / mail sources
        /// every overview panel shares (VAS_ActivitySourcesModel).</summary>
        private readonly VAS_ActivitySourcesModel _activitySources = new VAS_ActivitySourcesModel();

        /// <summary>
        /// The correspondence and engagement sources shared with every other
        /// overview panel: appointments and tasks (AppointmentsInfo, split on
        /// IsTask), calls (VA048_CallDetails), and the letters and mails
        /// MailAttachment1 holds, split on AttachmentType. Each is pinned to the
        /// confirmation by AD_Table_ID + Record_ID.
        ///
        /// Mails are read HERE rather than by a loader of this panel's own — it
        /// never had one, so its trail reported no correspondence at all.
        /// </summary>
        private void LoadSharedSourceActivity(int M_InOutConfirm_ID, List<ActivityData> list)
        {
            List<VAS_ActivitySourceRow> rows =
                _activitySources.Load("M_InOutConfirm", M_InOutConfirm_ID, true);
            foreach (VAS_ActivitySourceRow s in rows)
            {
                list.Add(new ActivityData
                {
                    // appointment | task | call | letter | email
                    EventType   = s.Kind,
                    Title       = s.Title,
                    Body        = s.Body,
                    Location    = s.Location,
                    IsClosed    = s.IsClosed,
                    IsCancelled = s.IsCancelled,
                    MailTo      = s.MailTo,
                    MailCc      = s.MailCc,
                    MailBcc     = s.MailBcc,
                    MailFrom    = s.MailFrom,
                    IsMailSent  = s.IsMailSent,
                    ActorName   = s.ActorName,
                    EventTime   = s.EventTime
                });
            }
        }

        /// <summary>
        /// Turns one AD_ChangeLog row into an activity entry. A change whose column
        /// cannot be resolved through the dictionary is skipped: without a field
        /// name the row says only that "something" changed, which is what this
        /// whole loader exists to stop reporting.
        /// </summary>
        private void AddChangeRow(DataRow r, string scope, List<ActivityData> list)
        {
            string field = Util.GetValueOfString(r["FieldName"]);
            if (string.IsNullOrEmpty(field)) return;

            // Reported as the field SHOWS them, not as the log stored them: a
            // reference reads as the referenced record's identifier, a list value
            // as its label, a date as the date alone.
            string column  = Util.GetValueOfString(r["FieldColumn"]);
            int refType    = Util.GetValueOfInt(r["RefType"]);
            int refValueId = Util.GetValueOfInt(r["RefValueId"]);

            list.Add(new ActivityData
            {
                EventType   = "Changed",
                FieldName   = field,
                OldValue    = _changeValues.Display(
                                  ChangeValue(Util.GetValueOfString(r["OldValue"])),
                                  column, refType, refValueId),
                NewValue    = _changeValues.Display(
                                  ChangeValue(Util.GetValueOfString(r["NewValue"])),
                                  column, refType, refValueId),
                ChangeScope = scope,
                ActorName   = Util.GetValueOfString(r["UserName"]),
                EventTime   = Util.GetValueOfDateTime(r["Created"])
            });
        }

        /// <summary>
        /// Tidies one side of a change for display: the platform writes an unset
        /// value as the literal "NULL", which reads as a value of its own rather
        /// than as an empty field.
        /// </summary>
        private static string ChangeValue(string raw)
        {
            if (string.IsNullOrEmpty(raw)) return "";
            string v = raw.Trim();
            return string.Equals(v, "NULL", StringComparison.OrdinalIgnoreCase) ? "" : v;
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
            public int      Line              { get; set; }   // source GRN / shipment line no
            public string   Description       { get; set; }
            public string   ProductCode       { get; set; }   // M_Product.Value
            public string   ProductName       { get; set; }
            /// <summary>The line's attribute set instance (lot / serial / attributes),
            /// "" when the line carries none.</summary>
            public string   AttributeSetInstance { get; set; }
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

            /// <summary>The line's own inspection rows; empty when quality does
            /// not apply to it.</summary>
            public List<ConfirmationQualityParamData> QualityParams { get; set; }
            /// <summary>The verdict derived from those parameters: P / F / N, and
            /// "" for a line with none — the panel shows a status only where
            /// quality actually applies.</summary>
            public string   QcStatusCode      { get; set; }
        }

        /// <summary>
        /// One quality-inspection row recorded against a confirmation line
        /// (VA010_ShipConfParameters).
        /// </summary>
        public class ConfirmationQualityParamData
        {
            public int       M_InOutLineConfirm_ID { get; set; }   // owning line
            public string    ParameterName     { get; set; }   // Colour / Size / Grade ...
            public decimal   QuantityToVerify  { get; set; }
            public int       AcceptableValueId { get; set; }   // configured VA010_TestPrmtrList_ID
            public string    AcceptableValue   { get; set; }
            public int       ActualValueId     { get; set; }   // inspected value; 0 = not inspected
            public string    ActualValue       { get; set; }
            public DateTime? QAQCDate          { get; set; }
            public string    Remark            { get; set; }
            public string    StatusCode        { get; set; }   // P = passed, F = failed, N = pending
        }

        /// <summary>One note: the confirmation's own, or one of its lines'.</summary>
        public class NoteData
        {
            public string NoteType { get; set; }   // header | line
            public string Text     { get; set; }
        }

        /// <summary>One activity entry. EventType drives the tag + icon the client
        /// renders: Note | Created | Completed.</summary>
        public class ActivityData
        {
            /// <summary>Note | Created | Completed | Changed</summary>
            public string    EventType { get; set; }
            public string    Title     { get; set; }
            public string    ActorName { get; set; }
            public DateTime? EventTime { get; set; }

            // Field-level change ("Changed"): which field moved, and from what to
            // what. ChangeScope names the line when it was a line that changed,
            // and is empty for a header edit.
            public string    FieldName   { get; set; }
            public string    OldValue    { get; set; }
            public string    NewValue    { get; set; }
            public string    ChangeScope { get; set; }

            // The shared correspondence / engagement sources
            // (VAS_ActivitySourcesModel). Empty on every other event type.
            /// <summary>Appointment / task: where the meeting is.</summary>
            public string    Location    { get; set; }
            public bool      IsClosed    { get; set; }
            public bool      IsCancelled { get; set; }
            /// <summary>Who it reached: a mail's or letter's recipient, a call's
            /// number.</summary>
            public string    MailTo      { get; set; }
            // The rest of a mail's / letter's envelope, and its body — carried
            // with the row so the panel can reveal the message on click without a
            // second round trip. The body arrives already flattened to text
            // (VAS_ActivitySourcesModel), since a mail sent as HTML stores markup.
            public string    MailCc      { get; set; }
            public string    MailBcc     { get; set; }
            public string    MailFrom    { get; set; }
            public bool      IsMailSent  { get; set; }
            public string    Body        { get; set; }
        }

        public class ShipGRNConfirmationOverviewData
        {
            // Header / identity
            public int       M_InOutConfirm_ID { get; set; }
            public string    DocumentNo        { get; set; }
            public string    StatusCode        { get; set; }   // DocStatus code
            public bool      Processed         { get; set; }
            public string    ConfirmTypeCode   { get; set; }   // ConfirmType code
            /// <summary>The dictionary's own name for that code — what the
            /// confirmation screen shows. "" when it cannot be resolved, which
            /// leaves the panel on its built-in map.</summary>
            public string    ConfirmTypeName   { get; set; }
            /// <summary>M_InOutConfirm.IsInDispute — the confirmation reported a
            /// difference and is under dispute.</summary>
            public bool      IsInDispute       { get; set; }
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
            public List<NoteData>     Notes    { get; set; }
            public List<ActivityData> Activity { get; set; }
        }
    }
}
