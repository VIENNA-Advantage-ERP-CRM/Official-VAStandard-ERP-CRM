/******************************************************
 * Module Name    : VASLogic
 * Purpose        : Backing model for the VAS_240_RequisitionBottomPanel tab
 *                  panel — the requisition counterpart of
 *                  VAS_107_CreateOrderBottomPanel. Provides parent-requisition
 *                  context and existing lines, paged Product / Charge catalog
 *                  search (50 rows / scroll), the server-side line callout
 *                  (price / amount), product attribute
 *                  (M_AttributeSetInstance) read + create, barcode scan lookup
 *                  and the M_RequisitionLine insert / update / delete write
 *                  actions (always through the MRequisitionLine business class).
 *
 *                  A requisition line carries NO tax: M_RequisitionLine has no
 *                  C_Tax_ID / TaxAmt / LineTotalAmt and there is no
 *                  M_RequisitionTax table, so the panel states one amount per
 *                  line (Qty x PriceActual = LineNetAmt) and one document
 *                  total. Everything tax-shaped in VAS_107 is therefore absent
 *                  here rather than stubbed.
 * Chronological  : Development
 *   VAI163         Created  03-Sep-2026
 ******************************************************/

using System;
using System.Collections.Generic;
using System.Data;
using System.Data.SqlClient;
using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;
using VAdvantage.DataBase;
using VAdvantage.Logging;
using VAdvantage.Model;
using VAdvantage.Utility;
using VIS.Models;

namespace VASLogic.Models
{
    /// <summary>
    /// Module Name : VASLogic
    /// Purpose     : Data + write model behind the Requisition Bottom Panel.
    ///               Every SELECT is filtered through MRole.AddAccessSQL on the
    ///               main physical table alias only and uses bind parameters so
    ///               the same code runs on PostgreSQL and Oracle. All inserts go
    ///               through MRequisitionLine (never a hand-written INSERT) so the
    ///               standard requisition-line logic runs — pricing from the
    ///               header price list, the QtyEntered -> Qty UOM conversion in
    ///               MRequisitionLine.BeforeSave, and the M_Requisition.TotalLines
    ///               roll-up in its AfterSave.
    /// Chronological development:
    ///   VAI163         Created  03-Sep-2026
    /// </summary>
    public class VAS_240_RequisitionBottomPanelModel
    {
        private static VLogger log = VLogger.GetVLogger(typeof(VAS_240_RequisitionBottomPanelModel).FullName);

        /// <summary>First page size for the Product / Charge catalog search.</summary>
        private const int CATALOG_PAGE_SIZE = 50;

        /// <summary>Saved requisition lines loaded per page (server-side paging).</summary>
        private const int LINE_PAGE_SIZE = 20;

        #region Panel (read) data

        /// <summary>
        /// Builds the panel header context (everything the client-side callouts
        /// need from the parent requisition) plus the already-saved lines.
        /// Returns an empty object (M_Requisition_ID = 0) when the role has no access.
        /// </summary>
        /// <param name="ctx">session context</param>
        /// <param name="M_Requisition_ID">parent requisition</param>
        /// <param name="AD_Window_ID">source window</param>
        /// <param name="page">0-based page of saved lines</param>
        /// <returns>panel view model</returns>
        public RequisitionPanelData GetPanelData(Ctx ctx, int M_Requisition_ID, int AD_Window_ID, int page = 0)
        {
            RequisitionPanelData data = new RequisitionPanelData();
            if (M_Requisition_ID <= 0) return data;

            LoadParentContext(ctx, M_Requisition_ID, data);
            if (data.M_Requisition_ID <= 0) return data;   // no access / not found

            data.AD_Window_ID = AD_Window_ID;
            List<int> tabIds = ResolveRequisitionLineTabs(AD_Window_ID);
            data.AD_Tab_IDs = tabIds;
            data.AD_Tab_ID = tabIds.Count > 0 ? tabIds[0] : 0;

            if (page < 0) page = 0;
            int total;
            data.Lines = LoadLines(ctx, M_Requisition_ID, tabIds, page, out total);
            data.LinesTotal = total;
            data.LinePage = page;
            data.LinePageSize = LINE_PAGE_SIZE;
            data.OtherPagesSubtotal = ComputeOtherPageTotal(ctx, M_Requisition_ID, data.Lines);
            LoadCatalogs(ctx, data);
            LoadColumns(ctx, data, tabIds);
            LoadLoginContext(ctx, data);
            return data;
        }

        /// <summary>
        /// Collects the login / session context values for every @$Token@ / @#Token@
        /// referenced by any column's DisplayLogic or ReadOnlyLogic.
        /// </summary>
        private void LoadLoginContext(Ctx ctx, RequisitionPanelData data)
        {
            Regex rx = new Regex(@"@([#$][A-Za-z0-9_]+)@");
            HashSet<string> tokens = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (RequisitionColumnMeta m in data.Columns)
            {
                if (!string.IsNullOrEmpty(m.DisplayLogic))
                    foreach (Match mt in rx.Matches(m.DisplayLogic)) tokens.Add(mt.Groups[1].Value);
                if (!string.IsNullOrEmpty(m.ReadOnlyLogic))
                    foreach (Match mt in rx.Matches(m.ReadOnlyLogic)) tokens.Add(mt.Groups[1].Value);
            }
            HashSet<string> activeElementTypes = null;
            foreach (string tok in tokens)
            {
                string val;
                if (tok.StartsWith("$Element_", StringComparison.OrdinalIgnoreCase))
                {
                    if (activeElementTypes == null) activeElementTypes = LoadActiveAcctElementTypes(ctx);
                    string type = tok.Substring("$Element_".Length);
                    val = activeElementTypes.Contains(type) ? "Y" : "N";
                }
                else
                {
                    val = ctx.GetContext(tok);
                    if (string.IsNullOrEmpty(val)) val = ctx.GetContext(tok.TrimStart('#', '$'));
                }
                if (!string.IsNullOrEmpty(val)) data.LoginContext[tok] = val;
            }
        }

        /// <summary>Returns the set of accounting ElementTypes active on the client's primary accounting schema.</summary>
        private HashSet<string> LoadActiveAcctElementTypes(Ctx ctx)
        {
            HashSet<string> types = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            string sql = @"SELECT DISTINCT ase.ElementType
                           FROM C_AcctSchema_Element ase
                           WHERE ase.IsActive = 'Y'
                             AND ase.C_AcctSchema_ID = (SELECT ci.C_AcctSchema1_ID
                                                        FROM AD_ClientInfo ci
                                                        WHERE ci.AD_Client_ID = @client)";
            DataSet ds = DB.ExecuteDataset(sql, new SqlParameter[] { new SqlParameter("@client", ctx.GetAD_Client_ID()) }, null);
            if (ds != null && ds.Tables.Count > 0)
                foreach (DataRow r in ds.Tables[0].Rows)
                {
                    string t = Util.GetValueOfString(r["ElementType"]);
                    if (!string.IsNullOrEmpty(t)) types.Add(t);
                }
            return types;
        }

        /// <summary>Loads M_RequisitionLine column metadata once per panel load.</summary>
        private void LoadColumns(Ctx ctx, RequisitionPanelData data, List<int> AD_Tab_IDs)
        {
            if (AD_Tab_IDs != null && AD_Tab_IDs.Count > 0)
                LoadColumnsFromTabs(ctx, data, AD_Tab_IDs);
            MergeAllColumns(ctx, data);
            InjectMissingVasColumns(data);
            LoadListValues(data);
        }

        /// <summary>
        /// VAS FK columns that are shown in the Additional Info modal but may be absent
        /// from the Application Dictionary (AD_Column IsActive = 'N' or entry not yet
        /// created). The array lists: column name, default AD_Reference_ID (18 = TableDir),
        /// and the field label shown in the modal header.
        /// </summary>
        private static readonly (string Col, int RefId, string Label)[] _vasInjectColumns =
        {
            ("VAS_Opportunity_ID", 18, "Opportunity")
        };

        /// <summary>
        /// Injects VAS-specific FK columns into the panel metadata when they are not already
        /// present after <see cref="LoadColumnsFromTabs"/> and <see cref="MergeAllColumns"/>.
        /// This handles the common case where the column exists physically on M_RequisitionLine but
        /// the AD_Column entry is absent or inactive (IsActive = 'N'). The lookup definition
        /// for each injected column is resolved at query time by <see cref="ResolveRefLookup"/>
        /// from the column-name TableDir convention (VAS_Opportunity_ID → VAS_Opportunity).
        /// </summary>
        /// <param name="data">Panel data being built; Columns list is mutated in-place.</param>
        private void InjectMissingVasColumns(RequisitionPanelData data)
        {
            HashSet<string> have = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (RequisitionColumnMeta m in data.Columns) have.Add(m.ColumnName);

            foreach (var cand in _vasInjectColumns)
            {
                if (have.Contains(cand.Col)) continue;
                // Query AD_Column without IsActive filter — the column may be registered but
                // deactivated. We still want to surface it in the panel.
                int adColId = 0, refId = cand.RefId;
                DataSet ds = DB.ExecuteDataset(
                    @"SELECT c.AD_Column_ID, c.AD_Reference_ID
                      FROM AD_Column c INNER JOIN AD_Table t ON (c.AD_Table_ID = t.AD_Table_ID)
                      WHERE t.TableName = 'M_RequisitionLine' AND c.ColumnName = @col",
                    new SqlParameter[] { new SqlParameter("@col", cand.Col) }, null);
                if (ds == null || ds.Tables.Count == 0 || ds.Tables[0].Rows.Count == 0)
                    continue; // column absent from AD_Column entirely — physical column may not exist
                adColId = Util.GetValueOfInt(ds.Tables[0].Rows[0]["AD_Column_ID"]);
                int dbRefId = Util.GetValueOfInt(ds.Tables[0].Rows[0]["AD_Reference_ID"]);
                if (dbRefId > 0) refId = dbRefId;
                data.Columns.Add(new RequisitionColumnMeta
                {
                    ColumnName            = cand.Col,
                    AD_Column_ID          = adColId,
                    AD_Reference_ID       = refId,
                    AD_Reference_Value_ID = 0,
                    Name                  = cand.Label,
                    IsDisplayed           = true,
                    IsUpdateable          = true,
                    IsMandatory           = false,
                    FieldLength           = 0,
                    ReadOnlyLogic         = "",
                    DisplayLogic          = "",
                    AD_Val_Rule_ID        = 0,
                    ValRuleType           = "",
                    ValRuleCode           = "",
                    Callout               = "",
                    IsTabField            = false,
                    IsReadOnly            = false,
                    SeqNo                 = 0
                });
            }
        }

        /// <summary>Fills inline AD_Ref_List values for List (ref 17) columns.</summary>
        private void LoadListValues(RequisitionPanelData data)
        {
            foreach (RequisitionColumnMeta m in data.Columns)
            {
                if (m.AD_Reference_ID != 17 || m.AD_Reference_Value_ID <= 0) continue;
                DataSet ds = DB.ExecuteDataset(
                    @"SELECT Value, Name FROM AD_Ref_List
                      WHERE AD_Reference_ID = @ref AND IsActive = 'Y'
                      ORDER BY COALESCE(Name, Value)",
                    new SqlParameter[] { new SqlParameter("@ref", m.AD_Reference_Value_ID) }, null);
                if (ds == null || ds.Tables.Count == 0) continue;
                foreach (DataRow r in ds.Tables[0].Rows)
                    m.RefListValues.Add(new RequisitionRefListItem
                    {
                        Value = Util.GetValueOfString(r["Value"]),
                        Name = Util.GetValueOfString(r["Name"])
                    });
            }
        }

        private Dictionary<int, List<int>> _rlTabsByWindow;

        /// <summary>Finds every active AD_Tab bound to M_RequisitionLine inside the given window.</summary>
        private List<int> ResolveRequisitionLineTabs(int AD_Window_ID)
        {
            if (_rlTabsByWindow == null) _rlTabsByWindow = new Dictionary<int, List<int>>();
            List<int> cached;
            if (_rlTabsByWindow.TryGetValue(AD_Window_ID, out cached)) return cached;

            List<int> tabs = new List<int>();
            if (AD_Window_ID > 0)
            {
                int tableId = X_M_RequisitionLine.Table_ID;
                if (tableId > 0)
                {
                    DataSet ds = DB.ExecuteDataset(
                        @"SELECT AD_Tab_ID FROM AD_Tab
                          WHERE AD_Window_ID = @win AND AD_Table_ID = @tbl AND IsActive = 'Y'
                          ORDER BY SeqNo",
                        new SqlParameter[] {
                            new SqlParameter("@win", AD_Window_ID),
                            new SqlParameter("@tbl", tableId) }, null);
                    if (ds != null && ds.Tables.Count > 0)
                        foreach (DataRow r in ds.Tables[0].Rows)
                        {
                            int id = Util.GetValueOfInt(r["AD_Tab_ID"]);
                            if (id > 0) tabs.Add(id);
                        }
                }
            }
            _rlTabsByWindow[AD_Window_ID] = tabs;
            return tabs;
        }

        /// <summary>
        /// SELECT expression for ReadOnlyLogic giving AD_Field priority over AD_Column.
        ///
        /// The non-blank test is LENGTH(TRIM(x)) > 0, NOT "x &lt;&gt; ''". On Oracle the
        /// empty string IS NULL, so "x &lt;&gt; ''" evaluates to UNKNOWN for every row and
        /// the sub-select silently matched NOTHING — the AD_Field-level override was
        /// therefore never seen on Oracle and every column fell through to
        /// AD_Column.ReadOnlyLogic. The same trap sat on the DisplayLogic sub-select in
        /// MergeAllColumns, where it meant the panel received NO display logic at all for
        /// any column that is not a field on one of the window's own order-line tabs.
        /// LENGTH(TRIM(x)) > 0 is correct on both engines: Oracle cannot store '' so a
        /// non-null value always passes, and on PostgreSQL a genuinely blank logic string
        /// is excluded as intended.
        /// </summary>
        private string ReadOnlyLogicSelectExpr(bool hasTabField)
        {
            bool fieldCol = ColumnExists("AD_Field", "ReadOnlyLogic");
            StringBuilder sb = new StringBuilder("COALESCE(");
            if (hasTabField && fieldCol)
                sb.Append("NULLIF(f.ReadOnlyLogic, N''), ");
            if (fieldCol)
                sb.Append(@"NULLIF((SELECT MAX(f2.ReadOnlyLogic)
                                FROM AD_Field f2
                                INNER JOIN AD_Tab t2 ON (f2.AD_Tab_ID = t2.AD_Tab_ID)
                                INNER JOIN AD_Table tt2 ON (t2.AD_Table_ID = tt2.AD_Table_ID)
                                WHERE f2.AD_Column_ID = c.AD_Column_ID
                                  AND f2.IsActive = 'Y'
                                  AND tt2.TableName = 'M_RequisitionLine'
                                  AND f2.ReadOnlyLogic IS NOT NULL
                                  AND LENGTH(TRIM(f2.ReadOnlyLogic)) > 0), N''), ");
            sb.Append("c.ReadOnlyLogic, N'')");
            return sb.ToString();
        }

        /// <summary>
        /// Base-system column-name prefixes (including the trailing underscore) that are
        /// always present and never require a module-installation check. Every other prefix
        /// (e.g. VA106_, VAFAM_, ED011_) belongs to an optional module and must pass
        /// <see cref="IsColumnModuleInstalled"/> before the column is included in the panel.
        /// </summary>
        private static readonly HashSet<string> _systemPrefixes = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            // Core Application Dictionary / Compiere / ADempiere columns
            "AD_", "C_", "M_", "A_", "G_", "K_", "R_", "I_", "B_", "T_", "S_", "W_", "U_",
            // VAS / VIS platform-core columns
            "VAS_", "VIS_", "VA_", "VB_",
            // Core columns whose prefix is a ROLE, not a module. The prefix rule takes
            // everything up to the first underscore, so M_RequisitionLine.Ref_OrderLine_ID
            // ("Original PO Line") yielded "Ref_" — read as an optional module, failed
            // Env.IsModuleInstalled, and the column was stripped from the payload before
            // the panel ever saw it. Link_ is the same shape (Link_OrderLine_ID).
            "Ref_", "Link_"
        };

        /// <summary>
        /// Returns true when <paramref name="columnName"/> belongs to the base system or when
        /// the optional module that owns the column is confirmed installed.
        /// The module prefix is the leading alphabetic + digit segment up to (and including)
        /// the first underscore, e.g. "VA106_" from "VA106_TaxCollectedAtSource_ID".
        /// Columns whose prefix is in <see cref="_systemPrefixes"/> (AD_, C_, VAS_, VIS_,
        /// etc.) always return true without a module check.
        /// </summary>
        /// <param name="columnName">AD_Column.ColumnName to test.</param>
        /// <returns>true when the column should be included in the panel metadata.</returns>
        private static bool IsColumnModuleInstalled(string columnName)
        {
            if (string.IsNullOrEmpty(columnName)) return true;
            int idx = columnName.IndexOf('_');
            if (idx <= 0) return true;
            string prefix = columnName.Substring(0, idx + 1);   // e.g. "VA106_"
            if (_systemPrefixes.Contains(prefix)) return true;
            return Env.IsModuleInstalled(prefix);
        }

        /// <summary>Reads AD_Field -> AD_Column metadata across all the window's order-line tabs.</summary>
        private bool LoadColumnsFromTabs(Ctx ctx, RequisitionPanelData data, List<int> AD_Tab_IDs)
        {
            string inList = string.Join(",", AD_Tab_IDs.ToArray());
            string roLogicExpr = ReadOnlyLogicSelectExpr(true);
            string sql = @"SELECT c.ColumnName,
                                  c.AD_Column_ID,
                                  COALESCE(c.Callout, N'')        AS Callout,
                                  COALESCE(c.IsMandatory, 'N')    AS IsMandatory,
                                  c.AD_Reference_ID,
                                  COALESCE(c.AD_Reference_Value_ID, 0) AS AD_Reference_Value_ID,
                                  COALESCE(f.Name, c.Name, c.ColumnName) AS FieldName,
                                  COALESCE(c.IsUpdateable, 'Y')   AS IsUpdateable,
                                  COALESCE(c.FieldLength, 0)      AS FieldLength,
                                  " + roLogicExpr + @"  AS ReadOnlyLogic,
                                  COALESCE(c.AD_Val_Rule_ID, 0)   AS AD_Val_Rule_ID,
                                  COALESCE(vr.Type, '')          AS ValRuleType,
                                  COALESCE(vr.Code, N'')          AS ValRuleCode,
                                  COALESCE(f.IsDisplayed, 'Y')    AS IsDisplayed,
                                  COALESCE(f.IsReadOnly, 'N')     AS IsReadOnly,
                                  COALESCE(f.DisplayLogic, N'')   AS DisplayLogic,
                                  COALESCE(f.SeqNo, 0)            AS SeqNo,
                                  COALESCE(f.AD_Image_ID, 0)      AS AD_Image_ID,
                                  COALESCE(img.FontName, N'')     AS FontName,
                                  COALESCE(img.ImageExtension, N'') AS ImageExtension,
                                  t.SeqNo                         AS TabSeqNo
                           FROM AD_Field f
                           INNER JOIN AD_Tab t ON (f.AD_Tab_ID = t.AD_Tab_ID)
                           INNER JOIN AD_Column c ON (f.AD_Column_ID = c.AD_Column_ID)
                           LEFT JOIN AD_Val_Rule vr ON (c.AD_Val_Rule_ID = vr.AD_Val_Rule_ID
                                AND vr.IsActive = 'Y')
                           LEFT JOIN AD_Image img ON (img.AD_Image_ID = f.AD_Image_ID
                                AND img.IsActive = 'Y')
                           WHERE f.AD_Tab_ID IN (" + inList + @")
                             AND f.IsActive = 'Y'
                             AND c.IsActive = 'Y'
                           ORDER BY t.SeqNo, f.SeqNo";

            DataSet ds = DB.ExecuteDataset(sql);
            if (ds == null || ds.Tables.Count == 0 || ds.Tables[0].Rows.Count == 0) return false;

            HashSet<string> seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (DataRow r in ds.Tables[0].Rows)
            {
                string name = Util.GetValueOfString(r["ColumnName"]);
                if (!seen.Add(name)) continue;
                // Skip columns from optional modules that are not installed on this deployment.
                if (!IsColumnModuleInstalled(name)) continue;
                data.Columns.Add(MapRequisitionColumnMeta(r, true));
            }
            return true;
        }

        /// <summary>Merges every active M_RequisitionLine column not already loaded from the tab.</summary>
        private void MergeAllColumns(Ctx ctx, RequisitionPanelData data)
        {
            HashSet<string> have = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (RequisitionColumnMeta cm in data.Columns) have.Add(cm.ColumnName);

            string roLogicExpr = ReadOnlyLogicSelectExpr(false);
            string sql = @"SELECT c.ColumnName,
                                  c.AD_Column_ID,
                                  COALESCE(c.Callout, N'')        AS Callout,
                                  COALESCE(c.IsMandatory, 'N')    AS IsMandatory,
                                  c.AD_Reference_ID,
                                  COALESCE(c.AD_Reference_Value_ID, 0) AS AD_Reference_Value_ID,
                                  COALESCE(c.Name, c.ColumnName)  AS FieldName,
                                  COALESCE(c.IsUpdateable, 'Y')   AS IsUpdateable,
                                  COALESCE(c.FieldLength, 0)      AS FieldLength,
                                  " + roLogicExpr + @"  AS ReadOnlyLogic,
                                  COALESCE(c.AD_Val_Rule_ID, 0)   AS AD_Val_Rule_ID,
                                  COALESCE(vr.Type, '')          AS ValRuleType,
                                  COALESCE(vr.Code, N'')          AS ValRuleCode,
                                  COALESCE((SELECT MAX(f2.DisplayLogic)
                                                FROM AD_Field f2
                                                INNER JOIN AD_Tab t2 ON (f2.AD_Tab_ID = t2.AD_Tab_ID)
                                                INNER JOIN AD_Table tt2 ON (t2.AD_Table_ID = tt2.AD_Table_ID)
                                                WHERE f2.AD_Column_ID = c.AD_Column_ID
                                                  AND f2.IsActive = 'Y'
                                                  AND tt2.TableName = 'M_RequisitionLine'
                                                  AND f2.DisplayLogic IS NOT NULL
                                                  AND LENGTH(TRIM(f2.DisplayLogic)) > 0), N'') AS DisplayLogic
                           FROM AD_Column c
                           INNER JOIN AD_Table t ON (c.AD_Table_ID = t.AD_Table_ID)
                           LEFT JOIN AD_Val_Rule vr ON (c.AD_Val_Rule_ID = vr.AD_Val_Rule_ID
                                AND vr.IsActive = 'Y')
                           WHERE t.TableName = 'M_RequisitionLine'
                             AND c.IsActive = 'Y'";

            DataSet ds = DB.ExecuteDataset(sql);
            if (ds == null || ds.Tables.Count == 0) return;
            foreach (DataRow r in ds.Tables[0].Rows)
            {
                string colName = Util.GetValueOfString(r["ColumnName"]);
                if (have.Contains(colName)) continue;
                // Skip columns from optional modules that are not installed on this deployment.
                if (!IsColumnModuleInstalled(colName)) continue;
                data.Columns.Add(MapRequisitionColumnMeta(r, false));
            }
        }

        /// <summary>Maps a column-meta row.</summary>
        private RequisitionColumnMeta MapRequisitionColumnMeta(DataRow r, bool fromField)
        {
            RequisitionColumnMeta m = new RequisitionColumnMeta
            {
                ColumnName = Util.GetValueOfString(r["ColumnName"]),
                AD_Column_ID = Util.GetValueOfInt(r["AD_Column_ID"]),
                Callout = Util.GetValueOfString(r["Callout"]),
                IsMandatory = Util.GetValueOfString(r["IsMandatory"]) == "Y",
                AD_Reference_ID = Util.GetValueOfInt(r["AD_Reference_ID"]),
                IsUpdateable = Util.GetValueOfString(r["IsUpdateable"]) == "Y",
                FieldLength = Util.GetValueOfInt(r["FieldLength"]),
                ReadOnlyLogic = Util.GetValueOfString(r["ReadOnlyLogic"]),
                AD_Val_Rule_ID = Util.GetValueOfInt(r["AD_Val_Rule_ID"]),
                ValRuleType = Util.GetValueOfString(r["ValRuleType"]),
                ValRuleCode = Util.GetValueOfString(r["ValRuleCode"]),
                AD_Reference_Value_ID = Util.GetValueOfInt(r["AD_Reference_Value_ID"]),
                Name = Util.GetValueOfString(r["FieldName"]),
                IsDisplayed = true,
                IsTabField = fromField
            };
            if (r.Table.Columns.Contains("DisplayLogic"))
                m.DisplayLogic = Util.GetValueOfString(r["DisplayLogic"]);
            if (fromField)
            {
                m.IsDisplayed = Util.GetValueOfString(r["IsDisplayed"]) == "Y";
                m.IsReadOnly = Util.GetValueOfString(r["IsReadOnly"]) == "Y";
                m.SeqNo = Util.GetValueOfInt(r["SeqNo"]);
                m.AD_Image_ID = Util.GetValueOfInt(r["AD_Image_ID"]);
                m.IconFont = Util.GetValueOfString(r["FontName"]);
                if (string.IsNullOrEmpty(m.IconFont))
                    m.ImageUrl = FieldImageUrl(m.AD_Image_ID, Util.GetValueOfString(r["ImageExtension"]));
            }
            return m;
        }

        /// <summary>Resolves an AD_Field image to a thumbnail URL.</summary>
        private string FieldImageUrl(int adImageId, string imageExtension)
        {
            if (adImageId <= 0 || string.IsNullOrEmpty(imageExtension)) return "";
            try
            {
                string file = GlobalVariable.ImagePath + "\\Thumb46x46\\" + adImageId + imageExtension;
                if (System.IO.File.Exists(file))
                    return "Images/Thumb46x46/" + adImageId + imageExtension;
            }
            catch { }
            return "";
        }

        /// <summary>
        /// Loads the UOM dropdown catalog using the requisition header context.
        /// There is no tax catalog: a requisition line carries no C_Tax_ID.
        /// </summary>
        private void LoadCatalogs(Ctx ctx, RequisitionPanelData data)
        {
            data.UomList = LoadUomList(ctx, data.M_Requisition_ID, null);
        }

        /// <summary>Builds the UOM dropdown list, enforcing the C_UOM_ID column's AD_Val_Rule.</summary>
        private List<RequisitionUomItem> LoadUomList(Ctx ctx, int M_Requisition_ID, Dictionary<string, string> rowVars)
        {
            List<RequisitionUomItem> list = new List<RequisitionUomItem>();
            string uomSql = @"SELECT u.C_UOM_ID, u.Name AS UOMName
                              FROM C_UOM u
                              WHERE u.IsActive = 'Y'";
            string uomPred = GetValRulePredicate(ctx, "C_UOM_ID", "C_UOM", "u", M_Requisition_ID, rowVars);
            if (uomPred.Length > 0) uomSql += " AND (" + uomPred + ")";
            uomSql += " ORDER BY u.Name";
            uomSql = MRole.GetDefault(ctx).AddAccessSQL(uomSql, "u", MRole.SQL_FULLYQUALIFIED, MRole.SQL_RO);
            DataSet uds = DB.ExecuteDataset(uomSql);
            if (uds != null && uds.Tables.Count > 0)
                foreach (DataRow r in uds.Tables[0].Rows)
                    list.Add(new RequisitionUomItem
                    {
                        C_UOM_ID = Util.GetValueOfInt(r["C_UOM_ID"]),
                        Name = Util.GetValueOfString(r["UOMName"])
                    });
            return list;
        }

        /// <summary>
        /// Re-fetches the per-row filtered UOM list for one requisition line,
        /// honouring the column's AD_Val_Rule against the line's current values.
        /// </summary>
        public RequisitionLookupData GetLookupData(Ctx ctx, RequisitionLookupRequest req)
        {
            RequisitionLookupData data = new RequisitionLookupData();
            if (req == null || req.M_Requisition_ID <= 0) return data;

            RequisitionPanelData parent = new RequisitionPanelData();
            LoadParentContext(ctx, req.M_Requisition_ID, parent);
            if (parent.M_Requisition_ID <= 0) return data;

            data.M_Requisition_ID = req.M_Requisition_ID;
            Dictionary<string, string> rowVars = BuildRowVars(req.RowValues);
            data.UomList = LoadUomList(ctx, req.M_Requisition_ID, rowVars);
            return data;
        }

        /// <summary>
        /// Generic FK lookup for a dynamic M_RequisitionLine field (Table / TableDir / Search).
        /// </summary>
        public List<RequisitionRefItem> GetRefLookup(Ctx ctx, RequisitionRefLookupRequest req)
        {
            List<RequisitionRefItem> items = new List<RequisitionRefItem>();
            if (req == null || req.M_Requisition_ID <= 0 || string.IsNullOrEmpty(req.ColumnName)) return items;

            RequisitionPanelData parent = new RequisitionPanelData();
            LoadParentContext(ctx, req.M_Requisition_ID, parent);
            if (parent.M_Requisition_ID <= 0) return items;

            RefLookupDef def = ResolveRefLookup(req.ColumnName);
            if (def == null) return items;

            int pageSize = (req.PageSize <= 0 || req.PageSize > CATALOG_PAGE_SIZE) ? CATALOG_PAGE_SIZE : req.PageSize;
            int offset = req.Offset < 0 ? 0 : (req.Offset > 1000000 ? 1000000 : req.Offset);

            string alias = "lk";
            string dispExpr = def.DisplayExpr.Replace("{a}", alias);

            StringBuilder sql = new StringBuilder();
            sql.Append("SELECT ").Append(alias).Append(".").Append(def.KeyColumn).Append(" AS Id, ")
               .Append(dispExpr).Append(" AS Name FROM ").Append(def.TableName).Append(" ").Append(alias)
               .Append(" WHERE 1 = 1");
            if (def.HasIsActive) sql.Append(" AND ").Append(alias).Append(".IsActive = 'Y'");
            if (def.HasClientId) sql.Append(" AND ").Append(alias).Append(".AD_Client_ID IN (0, ").Append(ctx.GetAD_Client_ID()).Append(")");

            List<SqlParameter> ps = new List<SqlParameter>();
            if (req.Id > 0)
            {
                sql.Append(" AND ").Append(alias).Append(".").Append(def.KeyColumn).Append(" = @id");
                ps.Add(new SqlParameter("@id", req.Id));
            }
            else
            {
                string term = (req.Query ?? "").Trim();
                if (term.Length > 0)
                {
                    sql.Append(" AND LOWER(").Append(dispExpr).Append(") LIKE @kw");
                    ps.Add(new SqlParameter("@kw", "%" + term.ToLower() + "%"));
                }
                Dictionary<string, string> rowVars = BuildRowVars(req.RowValues);
                string pred = GetValRulePredicate(ctx, req.ColumnName, def.TableName, alias, req.M_Requisition_ID, rowVars);
                if (pred.Length > 0) sql.Append(" AND (").Append(pred).Append(")");
            }

            string secured = MRole.GetDefault(ctx).AddAccessSQL(
                sql.ToString(), alias, MRole.SQL_FULLYQUALIFIED, MRole.SQL_RO);
            if (req.Id <= 0) secured += " ORDER BY " + dispExpr + PagingSuffix(pageSize, offset);

            DataSet ds = DB.ExecuteDataset(secured, ps.ToArray(), null);
            if (ds == null || ds.Tables.Count == 0) return items;
            foreach (DataRow r in ds.Tables[0].Rows)
                items.Add(new RequisitionRefItem { Id = Util.GetValueOfInt(r["Id"]), Name = Util.GetValueOfString(r["Name"]) });
            return items;
        }

        /// <summary>Non-standard TableDir FK columns whose lookup table/key is not the column name minus "_ID".</summary>
        private static readonly Dictionary<string, string[]> TableDirOverrides =
            new Dictionary<string, string[]>(StringComparer.OrdinalIgnoreCase)
            {
                { "AD_OrgTrx_ID", new string[] { "AD_Org", "AD_Org_ID" } },
                // The requisition line's link to the purchase-order line it was
                // converted into. The TableDir convention already derives
                // C_OrderLine / C_OrderLine_ID from the name, but it is listed
                // explicitly because the display expression below depends on it.
                { "C_OrderLine_ID", new string[] { "C_OrderLine", "C_OrderLine_ID" } },
                // Requisition-line self reference (a module's "source line" column):
                // the convention would derive a table that does not exist.
                { "Ref_M_RequisitionLine_ID", new string[] { "M_RequisitionLine", "M_RequisitionLine_ID" } }
            };

        /// <summary>
        /// Display expression for a lookup that points at a DOCUMENT LINE table
        /// (M_RequisitionLine for a module's source-line column, C_OrderLine for
        /// the purchase-order line a requisition line was converted into). Neither
        /// table has an identifier column, so the generic builder would fall back
        /// to showing the raw id — name the line by its document instead:
        /// "&lt;DocumentNo&gt; - &lt;line no&gt;".
        /// </summary>
        /// <param name="headerTable">parent document table (M_Requisition / C_Order)</param>
        /// <param name="headerKey">parent key column on the line</param>
        private string DocLineDisplayExpr(string headerTable, string headerKey)
        {
            string docNo = "(SELECT h.DocumentNo FROM " + headerTable + " h WHERE h."
                + headerKey + " = {a}." + headerKey + ")";
            if (DB.IsPostgreSQL() || DB.IsOracle())
                return docNo + " || ' - ' || {a}.Line";
            return "CONCAT(" + docNo + ", ' - ', {a}.Line)";
        }

        /// <summary>Display expression for the line table a lookup points at, or null for any other table.</summary>
        private string LineTableDisplayExpr(string table)
        {
            if ("M_RequisitionLine".Equals(table, StringComparison.OrdinalIgnoreCase))
                return DocLineDisplayExpr("M_Requisition", "M_Requisition_ID");
            if ("C_OrderLine".Equals(table, StringComparison.OrdinalIgnoreCase))
                return DocLineDisplayExpr("C_Order", "C_Order_ID");
            return null;
        }

        private Dictionary<string, RefLookupDef> _refDefByColumn;

        /// <summary>Resolves a M_RequisitionLine FK column to its lookup table, key and display expression.</summary>
        private RefLookupDef ResolveRefLookup(string columnName)
        {
            if (_refDefByColumn == null) _refDefByColumn = new Dictionary<string, RefLookupDef>(StringComparer.OrdinalIgnoreCase);
            RefLookupDef cached;
            if (_refDefByColumn.TryGetValue(columnName, out cached)) return cached;

            RefLookupDef def = null;
            DataSet rs = DB.ExecuteDataset(
                @"SELECT c.AD_Reference_ID, COALESCE(c.AD_Reference_Value_ID, 0) AS AD_Reference_Value_ID
                  FROM AD_Column c INNER JOIN AD_Table t ON (c.AD_Table_ID = t.AD_Table_ID)
                  WHERE t.TableName = 'M_RequisitionLine' AND c.ColumnName = @c AND c.IsActive = 'Y'",
                new SqlParameter[] { new SqlParameter("@c", columnName) }, null);
            if (rs != null && rs.Tables.Count > 0 && rs.Tables[0].Rows.Count > 0)
            {
                int refId = Util.GetValueOfInt(rs.Tables[0].Rows[0]["AD_Reference_ID"]);
                int refValId = Util.GetValueOfInt(rs.Tables[0].Rows[0]["AD_Reference_Value_ID"]);

                string table = null, key = null, display = null;
                if (refId == 19 || (refId == 18 && refValId <= 0))
                {
                    string[] ov;
                    if (TableDirOverrides.TryGetValue(columnName, out ov))
                    {
                        table = ov[0]; key = ov[1];
                    }
                    else if (columnName.EndsWith("_ID", StringComparison.OrdinalIgnoreCase))
                    {
                        table = columnName.Substring(0, columnName.Length - 3);
                        key = columnName;
                    }
                    if (table != null) display = BuildIdentifierExpr(table);
                }
                else if ((refId == 18 || refId == 30) && refValId > 0)
                {
                    DataSet ts = DB.ExecuteDataset(
                        @"SELECT t.TableName, ck.ColumnName AS KeyColumn, cd.ColumnName AS DisplayColumn
                          FROM AD_Ref_Table rt
                          INNER JOIN AD_Table t ON (rt.AD_Table_ID = t.AD_Table_ID)
                          INNER JOIN AD_Column ck ON (rt.AD_Key = ck.AD_Column_ID)
                          LEFT JOIN AD_Column cd ON (rt.AD_Display = cd.AD_Column_ID)
                          WHERE rt.AD_Reference_ID = @rv",
                        new SqlParameter[] { new SqlParameter("@rv", refValId) }, null);
                    if (ts != null && ts.Tables.Count > 0 && ts.Tables[0].Rows.Count > 0)
                    {
                        DataRow tr = ts.Tables[0].Rows[0];
                        table = Util.GetValueOfString(tr["TableName"]);
                        key = Util.GetValueOfString(tr["KeyColumn"]);
                        string disp = Util.GetValueOfString(tr["DisplayColumn"]);
                        display = !string.IsNullOrEmpty(disp) ? "{a}." + disp : BuildIdentifierExpr(table);
                    }
                }

                if (table != null && key != null && display != null)
                {
                    string lineExpr = LineTableDisplayExpr(table);
                    if (lineExpr != null) display = lineExpr;
                    def = new RefLookupDef { TableName = table, KeyColumn = key, DisplayExpr = display, HasIsActive = true, HasClientId = true };
                }
            }

            // Fallback for columns injected by InjectMissingVasColumns that are absent or
            // inactive in AD_Column: derive the lookup table from the column-name TableDir
            // convention (e.g. VAS_Opportunity_ID → VAS_Opportunity).
            if (def == null && columnName.EndsWith("_ID", StringComparison.OrdinalIgnoreCase))
            {
                string[] ov;
                string tbl, kc;
                if (TableDirOverrides.TryGetValue(columnName, out ov)) { tbl = ov[0]; kc = ov[1]; }
                else { tbl = columnName.Substring(0, columnName.Length - 3); kc = columnName; }
                string dispExpr = LineTableDisplayExpr(tbl) ?? BuildIdentifierExpr(tbl);
                def = new RefLookupDef { TableName = tbl, KeyColumn = kc, DisplayExpr = dispExpr, HasIsActive = true, HasClientId = true };
            }

            _refDefByColumn[columnName] = def;
            return def;
        }

        /// <summary>Builds the display expression for a table from its identifier columns.</summary>
        private string BuildIdentifierExpr(string table)
        {
            DataSet ds = DB.ExecuteDataset(
                @"SELECT c.ColumnName FROM AD_Column c INNER JOIN AD_Table t ON (c.AD_Table_ID = t.AD_Table_ID)
                  WHERE t.TableName = @t AND c.IsIdentifier = 'Y' AND c.IsActive = 'Y'
                  ORDER BY c.SeqNo",
                new SqlParameter[] { new SqlParameter("@t", table) }, null);
            List<string> cols = new List<string>();
            if (ds != null && ds.Tables.Count > 0)
                foreach (DataRow r in ds.Tables[0].Rows) cols.Add(Util.GetValueOfString(r["ColumnName"]));

            if (cols.Count == 0)
            {
                if (ColumnExists(table, "Name")) return "{a}.Name";
                if (ColumnExists(table, "Value")) return "{a}.Value";
                return "{a}." + table + "_ID";
            }
            if (cols.Count == 1) return "{a}." + cols[0];

            if (DB.IsPostgreSQL() || DB.IsOracle())
            {
                StringBuilder sb = new StringBuilder();
                for (int i = 0; i < cols.Count; i++)
                {
                    if (i > 0) sb.Append(" || ' - ' || ");
                    sb.Append("{a}.").Append(cols[i]);
                }
                return sb.ToString();
            }
            StringBuilder cc = new StringBuilder("CONCAT(");
            for (int i = 0; i < cols.Count; i++)
            {
                if (i > 0) cc.Append(", ' - ', ");
                cc.Append("{a}.").Append(cols[i]);
            }
            cc.Append(")");
            return cc.ToString();
        }

        private Dictionary<string, bool> _colExists;

        /// <summary>Whether a table has a given column (dictionary check, per-instance cached).</summary>
        private bool ColumnExists(string table, string column)
        {
            string key = table + "." + column;
            if (_colExists == null) _colExists = new Dictionary<string, bool>(StringComparer.OrdinalIgnoreCase);
            bool cached;
            if (_colExists.TryGetValue(key, out cached)) return cached;
            object o = DB.ExecuteScalar(
                @"SELECT COUNT(*) FROM AD_Column c INNER JOIN AD_Table t ON (c.AD_Table_ID = t.AD_Table_ID)
                  WHERE t.TableName = @t AND c.ColumnName = @c AND c.IsActive = 'Y'",
                new SqlParameter[] { new SqlParameter("@t", table), new SqlParameter("@c", column) }, null);
            bool exists = Util.GetValueOfInt(o) > 0;
            _colExists[key] = exists;
            return exists;
        }

        /// <summary>Resolved FK lookup definition for a dynamic field.</summary>
        private class RefLookupDef
        {
            public string TableName;
            public string KeyColumn;
            public string DisplayExpr;
            public bool HasIsActive;
            public bool HasClientId;
        }

        /// <summary>
        /// SELECT-list item for one optional M_Requisition column that a
        /// requisition-line field's DisplayLogic reads as a token
        /// (@M_Warehouse_ID@, @PriorityRule@, ...). The column where the
        /// dictionary has it, NULL where it does not, always under its own name.
        /// NULL rather than a literal default on purpose: the logic distinguishes
        /// "not set" from any real value, and the client resolves a null token to
        /// "" so "@x@=null" matches and "@x@&gt;0" does not.
        /// </summary>
        /// <param name="column">M_Requisition column named by a DisplayLogic token.</param>
        private string LogicTokenExpr(string column)
        {
            return ColumnExists("M_Requisition", column)
                ? "o." + column + " AS " + column + ","
                : "NULL AS " + column + ",";
        }

        /// <summary>
        /// M_Requisition columns that M_RequisitionLine field DisplayLogic /
        /// ReadOnlyLogic refer to by token, and that the panel therefore has to
        /// carry on the header for the logic to evaluate at all. Without these the
        /// tokens resolve to "" and every field whose logic names one is judged
        /// invisible, whatever the panel's own gating says.
        ///
        /// M_Requisition.DateRequired and M_Warehouse_ID are the ones the standard
        /// dictionary names; the rest are carried because module columns on the
        /// line commonly gate on them. Each is guarded, so a schema without one
        /// simply reports it as absent.
        /// </summary>
        private static readonly string[] _logicTokenColumns =
        {
            "DocumentNo", "PriorityRule", "M_Warehouse_ID", "DateRequired",
            "AD_User_ID", "C_BPartner_ID", "DTD001_MWarehouseSource_ID"
        };

        /// <summary>Loads the parent requisition header values used as callout context.</summary>
        private void LoadParentContext(Ctx ctx, int M_Requisition_ID, RequisitionPanelData data)
        {
            // Header columns the requisition line's own DisplayLogic reads by token.
            StringBuilder logicCols = new StringBuilder();
            foreach (string c in _logicTokenColumns) logicCols.Append(LogicTokenExpr(c)).Append(' ');
            // A requisition has no currency of its own: the amounts are stated in the
            // currency of its PRICE LIST (M_PriceList.C_Currency_ID) — the same reading
            // VAS_098 takes — so precision and symbol come from there. Both joins are
            // INNER: a requisition without a price list cannot price a line, and the
            // panel would have nothing to state the amounts in.
            string sql = @"SELECT
                              " + logicCols + @"
                              o.M_Requisition_ID,
                              o.AD_Client_ID,
                              o.AD_Org_ID,
                              o.M_PriceList_ID,
                              pl.C_Currency_ID,
                              o.DateDoc,
                              o.DocStatus,
                              COALESCE(o.Processed, 'N') AS Processed,
                              cur.StdPrecision  AS StdPrecision,
                              cur.CurSymbol     AS CurrencySymbol,
                              cur.ISO_Code      AS CurrencyISOCode
                           FROM M_Requisition o
                           INNER JOIN M_PriceList pl ON (o.M_PriceList_ID = pl.M_PriceList_ID)
                           INNER JOIN C_Currency cur ON (pl.C_Currency_ID = cur.C_Currency_ID)
                           WHERE o.M_Requisition_ID = @M_Requisition_ID
                             AND o.IsActive = 'Y'";

            sql = MRole.GetDefault(ctx).AddAccessSQL(sql, "o", MRole.SQL_FULLYQUALIFIED, MRole.SQL_RO);

            DataSet ds = DB.ExecuteDataset(sql,
                new SqlParameter[] { new SqlParameter("@M_Requisition_ID", M_Requisition_ID) }, null);
            if (ds == null || ds.Tables.Count == 0 || ds.Tables[0].Rows.Count == 0) return;

            DataRow r = ds.Tables[0].Rows[0];
            data.M_Requisition_ID = Util.GetValueOfInt(r["M_Requisition_ID"]);
            data.AD_Client_ID = Util.GetValueOfInt(r["AD_Client_ID"]);
            data.AD_Org_ID = Util.GetValueOfInt(r["AD_Org_ID"]);
            data.M_PriceList_ID = Util.GetValueOfInt(r["M_PriceList_ID"]);
            data.C_Currency_ID = Util.GetValueOfInt(r["C_Currency_ID"]);
            data.DateDoc = Util.GetValueOfDateTime(r["DateDoc"]);
            // Header values named by requisition-line DisplayLogic tokens. A DBNull stays
            // absent from the bag rather than becoming "" or 0, so the client can tell
            // "not set" from a real value and "@token@=null" evaluates the way the
            // dictionary means.
            foreach (string c in _logicTokenColumns)
                if (r[c] != DBNull.Value) data.LogicContext[c] = Util.GetValueOfString(r[c]);
            data.DocumentNo = LogicValue(data, "DocumentNo");
            data.M_Warehouse_ID = Util.GetValueOfInt(LogicValue(data, "M_Warehouse_ID"));
            data.C_BPartner_ID = Util.GetValueOfInt(LogicValue(data, "C_BPartner_ID"));
            data.DocStatus = Util.GetValueOfString(r["DocStatus"]);
            data.Processed = Util.GetValueOfString(r["Processed"]) == "Y";
            data.StdPrecision = Util.GetValueOfInt(r["StdPrecision"]);
            data.CurSymbol = Util.GetValueOfString(r["CurrencySymbol"]);
            data.CurISO = Util.GetValueOfString(r["CurrencyISOCode"]);
            data.IsEditable = !data.Processed
                && data.DocStatus != "CO" && data.DocStatus != "CL"
                && data.DocStatus != "VO" && data.DocStatus != "RE";
        }

        /// <summary>Reads one header token out of the LogicContext bag ("" when absent).</summary>
        private static string LogicValue(RequisitionPanelData data, string column)
        {
            string v;
            return (data.LogicContext != null && data.LogicContext.TryGetValue(column, out v)) ? v : "";
        }

        /// <summary>Loads the requisition lines saved against the parent requisition.</summary>
        private List<RequisitionLineRow> LoadLines(Ctx ctx, int M_Requisition_ID, List<int> AD_Tab_IDs, int page, out int total)
        {
            List<RequisitionLineRow> rows = new List<RequisitionLineRow>();

            string countSql = "SELECT COUNT(*) FROM M_RequisitionLine rl WHERE rl.M_Requisition_ID = @M_Requisition_ID AND rl.IsActive = 'Y'";
            countSql = MRole.GetDefault(ctx).AddAccessSQL(countSql, "rl", MRole.SQL_FULLYQUALIFIED, MRole.SQL_RO);
            total = Util.GetValueOfInt(DB.ExecuteScalar(countSql,
                new SqlParameter[] { new SqlParameter("@M_Requisition_ID", M_Requisition_ID) }, null));

            StringBuilder cols = new StringBuilder();
            foreach (string cn in GetLineProjectionColumns(AD_Tab_IDs))
                cols.Append("rl.").Append(cn).Append(", ");
            if (cols.Length == 0)
                cols.Append("rl.M_RequisitionLine_ID, rl.Line, rl.M_Product_ID, rl.C_Charge_ID, rl.Qty, rl.C_UOM_ID, rl.PriceActual, rl.LineNetAmt, rl.M_AttributeSetInstance_ID, rl.Description, ");

            string sql = "SELECT " + cols.ToString() +
                @"COALESCE(p.Name, N'') AS VASOLDISP_ProductName,
                  COALESCE(ch.Name, N'') AS VASOLDISP_ChargeName,
                  COALESCE(uom.Name, N'') AS VASOLDISP_UOMName,
                  COALESCE(asi.Description, N'') AS VASOLDISP_AttrName,
                  COALESCE(p.M_AttributeSet_ID, 0) AS VASOLDISP_HasAttrSet,
                  COALESCE(p.ProductType, '') AS VASOLDISP_ProductType
               FROM M_RequisitionLine rl
               LEFT JOIN M_Product p ON (rl.M_Product_ID = p.M_Product_ID)
               LEFT JOIN C_Charge ch ON (rl.C_Charge_ID = ch.C_Charge_ID)
               LEFT JOIN C_UOM uom ON (rl.C_UOM_ID = uom.C_UOM_ID)
               LEFT JOIN M_AttributeSetInstance asi ON (rl.M_AttributeSetInstance_ID = asi.M_AttributeSetInstance_ID)
               WHERE rl.M_Requisition_ID = @M_Requisition_ID
                 AND rl.IsActive = 'Y'";

            sql = MRole.GetDefault(ctx).AddAccessSQL(sql, "rl", MRole.SQL_FULLYQUALIFIED, MRole.SQL_RO);
            if (page < 0) page = 0;
            sql += " ORDER BY rl.Line" + PagingSuffix(LINE_PAGE_SIZE, page * LINE_PAGE_SIZE);

            DataSet ds = DB.ExecuteDataset(sql,
                new SqlParameter[] { new SqlParameter("@M_Requisition_ID", M_Requisition_ID) }, null);
            if (ds == null || ds.Tables.Count == 0) return rows;

            DataTable dt = ds.Tables[0];
            foreach (DataRow r in dt.Rows)
            {
                RequisitionLineRow row = new RequisitionLineRow();
                foreach (DataColumn dc in dt.Columns)
                {
                    // OrdinalIgnoreCase: PostgreSQL lowercases aliases (vasoldisp_*),
                    // Oracle uppercases them (VASOLDISP_*) — both must be excluded from
                    // the generic Values bag so only real M_RequisitionLine columns are sent.
                    if (dc.ColumnName.StartsWith("VASOLDISP_", StringComparison.OrdinalIgnoreCase)) continue;
                    row.Values[dc.ColumnName] = (r[dc] == DBNull.Value) ? null : r[dc];
                }
                row.M_RequisitionLine_ID = Util.GetValueOfInt(r["M_RequisitionLine_ID"]);
                row.Line = Util.GetValueOfInt(r["Line"]);
                row.M_Product_ID = Util.GetValueOfInt(r["M_Product_ID"]);
                row.ProductName = Util.GetValueOfString(r["VASOLDISP_ProductName"]);
                row.C_Charge_ID = Util.GetValueOfInt(r["C_Charge_ID"]);
                row.ChargeName = Util.GetValueOfString(r["VASOLDISP_ChargeName"]);
                row.Description = Util.GetValueOfString(r["Description"]);
                row.Qty = Util.GetValueOfDecimal(r["Qty"]);
                // QtyEntered — the quantity in the line's SELECTED unit — is an optional
                // column that only the requisition window maintains (a line raised by
                // replenishment or by a work order carries Qty alone). Where it is absent,
                // or zero on such a line, the selected-unit figure IS the base figure.
                row.QtyEntered = dt.Columns.Contains("QtyEntered") ? Util.GetValueOfDecimal(r["QtyEntered"]) : row.Qty;
                if (row.QtyEntered == 0) row.QtyEntered = row.Qty;
                row.C_UOM_ID = Util.GetValueOfInt(r["C_UOM_ID"]);
                row.UOMName = Util.GetValueOfString(r["VASOLDISP_UOMName"]);
                row.PriceActual = Util.GetValueOfDecimal(r["PriceActual"]);
                row.LineNetAmt = Util.GetValueOfDecimal(r["LineNetAmt"]);
                row.M_AttributeSetInstance_ID = Util.GetValueOfInt(r["M_AttributeSetInstance_ID"]);
                row.AttrName = Util.GetValueOfString(r["VASOLDISP_AttrName"]);
                int hasAttrSetRaw = Util.GetValueOfInt(r["VASOLDISP_HasAttrSet"]);
                row.HasAttributeSet = hasAttrSetRaw > 0;
                // Store under a canonical mixed-case key so the JS productHasAttributeSet()
                // can read it via lineVal() on both PostgreSQL and Oracle without falling back
                // to the display flag (which conflates AttrName with the actual attribute-set
                // presence and would wrongly enable the attribute link when the product's
                // attribute set was removed after the line was saved).
                row.Values["VASOLDISP_HasAttrSet"] = hasAttrSetRaw;
                row.ProductType = Util.GetValueOfString(r["VASOLDISP_ProductType"]);
                rows.Add(row);
            }
            return rows;
        }

        /// <summary>
        /// Sums LineNetAmt across every saved line of the requisition. This is the
        /// document total the requisition itself keeps in M_Requisition.TotalLines
        /// (MRequisitionLine.AfterSave rolls it up), read from the lines rather than
        /// from the header so it is right the moment a line is saved, whatever the
        /// header roll-up has done. A requisition carries no tax, so this single
        /// figure IS the document total.
        /// </summary>
        /// <param name="ctx">session context</param>
        /// <param name="M_Requisition_ID">parent requisition</param>
        /// <returns>sum of LineNetAmt over all active lines</returns>
        private decimal LoadGrandTotal(Ctx ctx, int M_Requisition_ID)
        {
            string sql = "SELECT COALESCE(SUM(rl.LineNetAmt), 0) AS Net"
                + " FROM M_RequisitionLine rl WHERE rl.M_Requisition_ID = @M_Requisition_ID AND rl.IsActive = 'Y'";
            sql = MRole.GetDefault(ctx).AddAccessSQL(sql, "rl", MRole.SQL_FULLYQUALIFIED, MRole.SQL_RO);
            DataSet ds = DB.ExecuteDataset(sql,
                new SqlParameter[] { new SqlParameter("@M_Requisition_ID", M_Requisition_ID) }, null);
            if (ds != null && ds.Tables.Count > 0 && ds.Tables[0].Rows.Count > 0)
                return Util.GetValueOfDecimal(ds.Tables[0].Rows[0]["Net"]);
            return 0;
        }

        /// <summary>
        /// Total of every saved line NOT on the current page, so the client can add
        /// the page it holds and state a document total without loading every line.
        /// </summary>
        /// <param name="ctx">session context</param>
        /// <param name="M_Requisition_ID">parent requisition</param>
        /// <param name="pageRows">lines loaded for the current page</param>
        /// <returns>sum of LineNetAmt for the lines NOT on the current page</returns>
        private decimal ComputeOtherPageTotal(Ctx ctx, int M_Requisition_ID, List<RequisitionLineRow> pageRows)
        {
            decimal grand = LoadGrandTotal(ctx, M_Requisition_ID);
            decimal page = 0;
            if (pageRows != null)
                foreach (RequisitionLineRow row in pageRows) page += row.LineNetAmt;
            return grand - page;
        }

        private List<string> _rlColumns;

        /// <summary>Returns (and caches) the active M_RequisitionLine column names from the dictionary.</summary>
        private List<string> GetRequisitionLineColumns()
        {
            if (_rlColumns != null) return _rlColumns;
            _rlColumns = new List<string>();
            DataSet ds = DB.ExecuteDataset(
                @"SELECT c.ColumnName
                  FROM AD_Column c
                  INNER JOIN AD_Table t ON (c.AD_Table_ID = t.AD_Table_ID)
                  WHERE t.TableName = 'M_RequisitionLine' AND c.IsActive = 'Y' AND c.ColumnSQL IS NULL ");
            if (ds != null && ds.Tables.Count > 0)
                foreach (DataRow r in ds.Tables[0].Rows)
                    _rlColumns.Add(Util.GetValueOfString(r["ColumnName"]));
            return _rlColumns;
        }

        private static readonly string[] ESSENTIAL_LINE_COLUMNS = new string[]
        {
            "M_RequisitionLine_ID", "M_Requisition_ID", "Line", "M_Product_ID", "C_Charge_ID",
            "QtyEntered", "Qty", "C_UOM_ID", "PriceActual", "LineNetAmt",
            "M_AttributeSetInstance_ID", "Description"
        };

        /// <summary>Builds the M_RequisitionLine column projection for LoadLines.</summary>
        private List<string> GetLineProjectionColumns(List<int> AD_Tab_IDs)
        {
            return GetRequisitionLineColumns();
        }

        private Dictionary<int, List<string>> _tabLineColumns;

        /// <summary>Returns (and caches) the M_RequisitionLine column names for a given tab.</summary>
        private List<string> GetTabLineColumns(int AD_Tab_ID)
        {
            if (_tabLineColumns == null) _tabLineColumns = new Dictionary<int, List<string>>();
            List<string> cached;
            if (_tabLineColumns.TryGetValue(AD_Tab_ID, out cached)) return cached;

            List<string> names = new List<string>();
            DataSet ds = DB.ExecuteDataset(
                @"SELECT c.ColumnName
                  FROM AD_Field f
                  INNER JOIN AD_Column c ON (f.AD_Column_ID = c.AD_Column_ID)
                  WHERE f.AD_Tab_ID = @tabId
                    AND f.IsActive = 'Y'
                    AND c.IsActive = 'Y' AND c.ColumnSQL IS NULL
                  ORDER BY f.SeqNo",
                new SqlParameter[] { new SqlParameter("@tabId", AD_Tab_ID) }, null);
            if (ds != null && ds.Tables.Count > 0)
                foreach (DataRow r in ds.Tables[0].Rows)
                    names.Add(Util.GetValueOfString(r["ColumnName"]));
            _tabLineColumns[AD_Tab_ID] = names;
            return names;
        }

        #endregion

        #region Product / Charge catalog search

        /// <summary>
        /// Paged Product / Charge catalog search for the line picker.
        /// </summary>
        public List<RequisitionCatalogItem> SearchProductsCharges(Ctx ctx, int M_Requisition_ID,
            string query, int pageSize, int offset, Dictionary<string, object> rowValues = null)
        {
            List<RequisitionCatalogItem> items = new List<RequisitionCatalogItem>();
            if (M_Requisition_ID <= 0) return items;

            if (pageSize <= 0 || pageSize > CATALOG_PAGE_SIZE) pageSize = CATALOG_PAGE_SIZE;
            if (offset < 0) offset = 0;
            if (offset > 1000000) offset = 1000000;

            Dictionary<string, string> rowVars = BuildRowVars(rowValues);
            string term = (query ?? "").Trim();
            string like = "%" + term.ToLower() + "%";

            string prodSql = @"SELECT p.M_Product_ID AS RecordId, 'P' AS Kind,
                                      p.Value AS SearchKey, p.Name AS DisplayName,
                                      COALESCE(p.Description, N'') AS Description,
                                      p.M_AttributeSet_ID AS AttributeSetId,
                                      COALESCE(p.ProductType, '') AS ProductType
                               FROM M_Product p
                               WHERE p.IsActive = 'Y'
                                 AND p.IsSummary = 'N'
                                 AND p.AD_Client_ID = " + ctx.GetAD_Client_ID() + @"
                                 AND (LOWER(p.Value) LIKE @kwPV
                                   OR LOWER(p.Name) LIKE @kwPN
                                   OR LOWER(COALESCE(p.UPC, N'')) LIKE @kwPU)";
            string prodPred = GetValRulePredicate(ctx, "M_Product_ID", "M_Product", "p", M_Requisition_ID, rowVars);
            if (prodPred.Length > 0) prodSql += " AND (" + prodPred + ")";
            prodSql = MRole.GetDefault(ctx).AddAccessSQL(prodSql, "p", MRole.SQL_FULLYQUALIFIED, MRole.SQL_RO);

            string chargeSql = @"SELECT ch.C_Charge_ID AS RecordId, 'C' AS Kind,
                                        COALESCE(ch.Name, N'') AS SearchKey, ch.Name AS DisplayName,
                                        COALESCE(ch.Description, N'') AS Description,
                                        0 AS AttributeSetId,
                                        '' AS ProductType
                                 FROM C_Charge ch
                                 WHERE ch.IsActive = 'Y'
                                   AND ch.AD_Client_ID = " + ctx.GetAD_Client_ID() + @"
                                   AND (LOWER(ch.Name) LIKE @kwCN
                                     OR LOWER(COALESCE(ch.Description, N'')) LIKE @kwCD)";
            string chargePred = GetValRulePredicate(ctx, "C_Charge_ID", "C_Charge", "ch", M_Requisition_ID, rowVars);
            if (chargePred.Length > 0) chargeSql += " AND (" + chargePred + ")";
            chargeSql = MRole.GetDefault(ctx).AddAccessSQL(chargeSql, "ch", MRole.SQL_FULLYQUALIFIED, MRole.SQL_RO);

            string combined = "SELECT x.RecordId, x.Kind, x.SearchKey, x.DisplayName, x.Description, x.AttributeSetId, x.ProductType"
                + " FROM ((" + prodSql + ") UNION ALL (" + chargeSql + ")) x"
                + " ORDER BY x.Kind, x.DisplayName" + PagingSuffix(pageSize, offset);

            DataSet ds = DB.ExecuteDataset(combined, new SqlParameter[] {
                new SqlParameter("@kwPV", like),
                new SqlParameter("@kwPN", like),
                new SqlParameter("@kwPU", like),
                new SqlParameter("@kwCN", like),
                new SqlParameter("@kwCD", like)
            }, null);
            if (ds == null || ds.Tables.Count == 0)
            {
                log.Severe("VAS_240 SearchProductsCharges SQL failed. Term: " + like);
                return items;
            }

            foreach (DataRow r in ds.Tables[0].Rows)
            {
                RequisitionCatalogItem it = new RequisitionCatalogItem();
                it.RecordId = Util.GetValueOfInt(r["RecordId"]);
                it.Kind = Util.GetValueOfString(r["Kind"]);
                it.SearchKey = Util.GetValueOfString(r["SearchKey"]);
                it.DisplayName = Util.GetValueOfString(r["DisplayName"]);
                it.Description = Util.GetValueOfString(r["Description"]);
                it.HasAttributeSet = Util.GetValueOfInt(r["AttributeSetId"]) > 0;
                it.ProductType = Util.GetValueOfString(r["ProductType"]);
                items.Add(it);
            }
            return items;
        }

        /// <summary>Looks up a single product / charge by a scanned barcode.</summary>
        public RequisitionCatalogItem ScanLookup(Ctx ctx, int M_Requisition_ID, string code)
        {
            RequisitionCatalogItem none = new RequisitionCatalogItem();
            if (M_Requisition_ID <= 0 || string.IsNullOrEmpty(code)) return none;
            string key = code.Trim();

            string prodSql = @"SELECT p.M_Product_ID AS RecordId, 'P' AS Kind, p.Value AS SearchKey,
                                      p.Name AS DisplayName, COALESCE(p.Description, N'') AS Description,
                                      p.M_AttributeSet_ID AS AttributeSetId,
                                      COALESCE(p.ProductType, '') AS ProductType
                               FROM M_Product p
                               WHERE p.IsActive = 'Y'
                                 AND p.AD_Client_ID = " + ctx.GetAD_Client_ID() + @"
                                 AND (UPPER(p.UPC) = UPPER(@code) OR UPPER(p.Value) = UPPER(@code))";
            string scanProdPred = GetValRulePredicate(ctx, "M_Product_ID", "M_Product", "p", M_Requisition_ID);
            if (scanProdPred.Length > 0) prodSql += " AND (" + scanProdPred + ")";
            prodSql = MRole.GetDefault(ctx).AddAccessSQL(prodSql, "p", MRole.SQL_FULLYQUALIFIED, MRole.SQL_RO);

            DataSet ds = DB.ExecuteDataset(prodSql, new SqlParameter[] { new SqlParameter("@code", key) }, null);
            if (ds != null && ds.Tables.Count > 0 && ds.Tables[0].Rows.Count > 0)
            {
                DataRow r = ds.Tables[0].Rows[0];
                RequisitionCatalogItem it = new RequisitionCatalogItem();
                it.RecordId = Util.GetValueOfInt(r["RecordId"]);
                it.Kind = "P";
                it.SearchKey = Util.GetValueOfString(r["SearchKey"]);
                it.DisplayName = Util.GetValueOfString(r["DisplayName"]);
                it.Description = Util.GetValueOfString(r["Description"]);
                it.HasAttributeSet = Util.GetValueOfInt(r["AttributeSetId"]) > 0;
                it.ProductType = Util.GetValueOfString(r["ProductType"]);
                return it;
            }

            string chargeSql = @"SELECT ch.C_Charge_ID AS RecordId, 'C' AS Kind, ch.Name AS SearchKey,
                                        ch.Name AS DisplayName, COALESCE(ch.Description, N'') AS Description
                                 FROM C_Charge ch
                                 WHERE ch.IsActive = 'Y'
                                   AND ch.AD_Client_ID = " + ctx.GetAD_Client_ID() + @"
                                   AND UPPER(ch.Name) = UPPER(@code)";
            string scanChargePred = GetValRulePredicate(ctx, "C_Charge_ID", "C_Charge", "ch", M_Requisition_ID);
            if (scanChargePred.Length > 0) chargeSql += " AND (" + scanChargePred + ")";
            chargeSql = MRole.GetDefault(ctx).AddAccessSQL(chargeSql, "ch", MRole.SQL_FULLYQUALIFIED, MRole.SQL_RO);
            ds = DB.ExecuteDataset(chargeSql, new SqlParameter[] { new SqlParameter("@code", key) }, null);
            if (ds != null && ds.Tables.Count > 0 && ds.Tables[0].Rows.Count > 0)
            {
                DataRow r = ds.Tables[0].Rows[0];
                RequisitionCatalogItem it = new RequisitionCatalogItem();
                it.RecordId = Util.GetValueOfInt(r["RecordId"]);
                it.Kind = "C";
                it.SearchKey = Util.GetValueOfString(r["SearchKey"]);
                it.DisplayName = Util.GetValueOfString(r["DisplayName"]);
                it.Description = Util.GetValueOfString(r["Description"]);
                it.HasAttributeSet = false;
                return it;
            }
            return none;
        }

        #region AD_Val_Rule enforcement

        private Dictionary<string, string> _valRuleByColumn;
        private Dictionary<string, string> _reqVars;

        /// <summary>Returns the SQL validation code linked to a M_RequisitionLine lookup column.</summary>
        private string GetColumnValRule(string columnName)
        {
            if (_valRuleByColumn == null) _valRuleByColumn = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            string cached;
            if (_valRuleByColumn.TryGetValue(columnName, out cached)) return cached;
            object o = DB.ExecuteScalar(
                @"SELECT vr.Code
                  FROM AD_Column c
                  INNER JOIN AD_Table t ON (c.AD_Table_ID = t.AD_Table_ID)
                  INNER JOIN AD_Val_Rule vr ON (c.AD_Val_Rule_ID = vr.AD_Val_Rule_ID)
                  WHERE t.TableName = 'M_RequisitionLine'
                    AND c.ColumnName = @c
                    AND c.IsActive = 'Y'
                    AND vr.IsActive = 'Y'
                    AND vr.Type = 'S'",
                new SqlParameter[] { new SqlParameter("@c", columnName) }, null);
            string code = Util.GetValueOfString(o);
            _valRuleByColumn[columnName] = code;
            return code;
        }

        private string GetValRulePredicate(Ctx ctx, string columnName, string tableName, string alias, int M_Requisition_ID)
        {
            return GetValRulePredicate(ctx, columnName, tableName, alias, M_Requisition_ID, null);
        }

        private string GetValRulePredicate(Ctx ctx, string columnName, string tableName, string alias,
            int M_Requisition_ID, Dictionary<string, string> rowVars)
        {
            string code = GetColumnValRule(columnName);
            if (string.IsNullOrEmpty(code)) return "";

            string frag = Regex.Replace(code, @"\b" + Regex.Escape(tableName) + @"\.", alias + ".", RegexOptions.IgnoreCase);

            Dictionary<string, string> oVars = GetRequisitionVars(ctx, M_Requisition_ID);
            frag = Regex.Replace(frag, @"@(#?[A-Za-z0-9_]+)@", delegate (Match m)
            {
                string token = m.Groups[1].Value;
                string k = token.TrimStart('#');
                string val;
                if (rowVars != null && rowVars.TryGetValue(k, out val)) return val;
                if (oVars.TryGetValue(k, out val)) return val;
                string ctxVal = GetCtxLiteral(ctx, token);
                return ctxVal ?? m.Value;
            });

            if (frag.IndexOf('@') >= 0)
            {
                log.Warning("VAS_240 val rule skipped for " + columnName + " (unresolved context): " + code);
                return "";
            }
            return frag;
        }

        private string GetCtxLiteral(Ctx ctx, string token)
        {
            string v = ctx.GetContext(token);
            if (string.IsNullOrEmpty(v) && !token.StartsWith("#")) v = ctx.GetContext("#" + token);
            if (string.IsNullOrEmpty(v)) return null;
            return ToSqlLiteral(v);
        }

        private Dictionary<string, string> BuildRowVars(Dictionary<string, object> rowValues)
        {
            Dictionary<string, string> vars = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            if (rowValues == null) return vars;
            foreach (KeyValuePair<string, object> kv in rowValues)
            {
                if (kv.Value == null) continue;
                vars[kv.Key] = ObjToSqlLiteral(kv.Value);
            }
            return vars;
        }

        private string ObjToSqlLiteral(object v)
        {
            if (v is bool) return ((bool)v) ? "'Y'" : "'N'";
            if (v is long || v is int || v is short || v is double || v is float || v is decimal)
                return Convert.ToString(v, CultureInfo.InvariantCulture);
            return ToSqlLiteral(Convert.ToString(v, CultureInfo.InvariantCulture));
        }

        private string ToSqlLiteral(string v)
        {
            decimal d;
            if (decimal.TryParse(v, NumberStyles.Any, CultureInfo.InvariantCulture, out d)) return v;
            return "'" + v.Replace("'", "''") + "'";
        }

        /// <summary>
        /// Parent-requisition context values (as SQL literals) for val-rule substitution.
        /// IsSOTrx is stated as 'N': a requisition is always a purchase-side document,
        /// and the standard product / charge val rules gate on that token.
        /// </summary>
        private Dictionary<string, string> GetRequisitionVars(Ctx ctx, int M_Requisition_ID)
        {
            if (_reqVars != null) return _reqVars;
            _reqVars = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            // C_BPartner_ID (the requester) and M_Warehouse_ID are read under a guard:
            // both are standard, but a val rule naming one must not take down the whole
            // lookup on a schema that lacks it.
            string bpCol = ColumnExists("M_Requisition", "C_BPartner_ID") ? "r.C_BPartner_ID" : "0";
            string whCol = ColumnExists("M_Requisition", "M_Warehouse_ID") ? "r.M_Warehouse_ID" : "0";
            DataSet ds = DB.ExecuteDataset(
                @"SELECT r.AD_Client_ID, r.AD_Org_ID, r.M_PriceList_ID,
                         pl.C_Currency_ID,
                         " + bpCol + @" AS C_BPartner_ID,
                         " + whCol + @" AS M_Warehouse_ID
                  FROM M_Requisition r
                  LEFT JOIN M_PriceList pl ON (pl.M_PriceList_ID = r.M_PriceList_ID)
                  WHERE r.M_Requisition_ID = @id",
                new SqlParameter[] { new SqlParameter("@id", M_Requisition_ID) }, null);
            if (ds != null && ds.Tables.Count > 0 && ds.Tables[0].Rows.Count > 0)
            {
                DataRow r = ds.Tables[0].Rows[0];
                _reqVars["AD_Client_ID"] = Util.GetValueOfInt(r["AD_Client_ID"]).ToString();
                _reqVars["AD_Org_ID"] = Util.GetValueOfInt(r["AD_Org_ID"]).ToString();
                _reqVars["C_BPartner_ID"] = Util.GetValueOfInt(r["C_BPartner_ID"]).ToString();
                _reqVars["M_PriceList_ID"] = Util.GetValueOfInt(r["M_PriceList_ID"]).ToString();
                _reqVars["C_Currency_ID"] = Util.GetValueOfInt(r["C_Currency_ID"]).ToString();
                _reqVars["M_Warehouse_ID"] = Util.GetValueOfInt(r["M_Warehouse_ID"]).ToString();
                _reqVars["M_Requisition_ID"] = M_Requisition_ID.ToString();
                _reqVars["IsSOTrx"] = "'N'";
            }
            return _reqVars;
        }

        #endregion

        /// <summary>Database-specific OFFSET/FETCH vs LIMIT/OFFSET paging suffix.</summary>
        private string PagingSuffix(int pageSize, int offset)
        {
            if (pageSize <= 0) pageSize = CATALOG_PAGE_SIZE;
            if (offset < 0) offset = 0;
            if (DB.IsOracle())
                return " OFFSET " + offset + " ROWS FETCH NEXT " + pageSize + " ROWS ONLY";
            return " LIMIT " + pageSize + " OFFSET " + offset;
        }

        #endregion

        #region Line callout (server-side price / amount)

        /// <summary>
        /// Server-side replacement for the M_RequisitionLine callout chain. Builds a
        /// transient MRequisitionLine, applies product / charge / qty / price and
        /// returns the framework-computed values. No row is written.
        /// </summary>
        public RequisitionLineCalcResult CalcLine(Ctx ctx, RequisitionLineCalcRequest req)
        {
            RequisitionLineCalcResult res = new RequisitionLineCalcResult();
            if (req == null || req.M_Requisition_ID <= 0) return res;

            MRequisition req_ = null;
            MRequisitionLine line = BuildCalcLine(ctx, req, out req_);
            if (line == null || (req.M_Product_ID <= 0 && req.C_Charge_ID <= 0)) return res;

            res.C_UOM_ID = LineUomId(line);
            res.PriceActual = line.GetPriceActual();
            res.LineNetAmt = line.GetLineNetAmt();
            res.Qty = line.GetQty();
            res.UOMName = GetUomLabel(ctx, res.C_UOM_ID);
            return res;
        }

        /// <summary>
        /// Builds a transient MRequisitionLine and applies the product / charge / qty /
        /// price the same way the standard requisition-line callouts do. Shared by
        /// CalcLine and RunColumnCallout.
        ///
        /// The quantity is handled the way MRequisitionLine.BeforeSave does: the entered
        /// figure belongs to the line's SELECTED unit (C_UOM_ID) and Qty is that figure
        /// converted to the product's BASE unit. The conversion is applied here as well
        /// so the amount the panel shows before saving is the amount the row will hold.
        /// </summary>
        private MRequisitionLine BuildCalcLine(Ctx ctx, RequisitionLineCalcRequest req, out MRequisition parent)
        {
            parent = new MRequisition(ctx, req.M_Requisition_ID, null);
            if (parent.Get_ID() <= 0) return null;

            MRequisitionLine line = new MRequisitionLine(parent);
            if (req.M_Product_ID > 0)
            {
                line.SetM_Product_ID(req.M_Product_ID);
                if (req.M_AttributeSetInstance_ID > 0)
                    line.SetM_AttributeSetInstance_ID(req.M_AttributeSetInstance_ID);
                // The product's own stocking unit, unless the client sent one (below).
                if (req.C_UOM_ID <= 0)
                {
                    int uom = GetProductUomId(ctx, req.M_Product_ID);
                    if (uom > 0) SetLineUom(line, uom);
                }
            }
            else if (req.C_Charge_ID > 0)
            {
                line.SetC_Charge_ID(req.C_Charge_ID);
            }
            else
            {
                return line;
            }

            if (req.C_UOM_ID > 0)
                SetLineUom(line, req.C_UOM_ID);

            // A charge line has no product to take a unit from; use the system default so
            // C_UOM_ID is never 0 (MRequisitionLine does not set one for a charge).
            if (req.C_Charge_ID > 0 && LineUomId(line) <= 0)
                SetLineUom(line, GetDefaultUomId(ctx));

            decimal entered = req.QtyEntered > 0 ? req.QtyEntered : (req.Qty > 0 ? req.Qty : 1);
            SetLineQty(ctx, line, entered);

            if (req.PriceOverride || req.M_Product_ID <= 0)
                line.SetPriceActual(req.PriceActual);
            else
                SetLinePriceWithAttribute(line, parent, req.M_AttributeSetInstance_ID);

            ApplyDiscount(line, req.Discount);

            // MRequisitionLine.BeforeSave only recomputes LineNetAmt when it is still
            // zero, so the panel states it here from the values it just applied.
            line.SetLineNetAmt();
            return line;
        }

        /// <summary>
        /// Writes the line's selected unit. C_UOM_ID is an optional column on
        /// M_RequisitionLine (only the requisition window maintains it), so it is set
        /// through Set_Value under a column guard rather than through a typed setter
        /// that a schema without the column would not carry.
        /// </summary>
        private void SetLineUom(MRequisitionLine line, int C_UOM_ID)
        {
            if (C_UOM_ID <= 0) return;
            if (line.Get_ColumnIndex("C_UOM_ID") < 0) return;
            line.Set_Value("C_UOM_ID", C_UOM_ID);
        }

        /// <summary>
        /// The line's selected unit, read the same way MRequisitionLine reads it —
        /// through Get_Value, because C_UOM_ID is not part of every generated
        /// X_M_RequisitionLine. Returns 0 when the schema has no such column.
        /// </summary>
        private static int LineUomId(MRequisitionLine line)
        {
            if (line == null || line.Get_ColumnIndex("C_UOM_ID") < 0) return 0;
            return Util.GetValueOfInt(line.Get_Value("C_UOM_ID"));
        }

        /// <summary>
        /// Applies a quantity keyed in the line's SELECTED unit: QtyEntered holds it as
        /// keyed (where the schema has that column) and Qty holds the same quantity in
        /// the product's BASE unit — the identical pair MRequisitionLine.BeforeSave
        /// maintains. Where there is no conversion to make (a charge line, a product
        /// held in the selected unit, a schema without QtyEntered) the two are equal.
        /// </summary>
        private void SetLineQty(Ctx ctx, MRequisitionLine line, decimal entered)
        {
            decimal baseQty = entered;
            int uom = LineUomId(line);
            if (line.GetM_Product_ID() > 0 && uom > 0)
            {
                int productUom = GetProductUomId(ctx, line.GetM_Product_ID());
                if (productUom > 0 && productUom != uom)
                {
                    decimal? conv = MUOMConversion.ConvertProductFrom(ctx, line.GetM_Product_ID(), uom, entered);
                    // A product with no conversion defined for the selected unit returns
                    // null; the entered figure then stands as the base figure rather than
                    // the line silently collapsing to zero.
                    if (conv != null && conv.Value != 0) baseQty = conv.Value;
                }
            }
            if (line.Get_ColumnIndex("QtyEntered") >= 0)
                line.Set_Value("QtyEntered", entered);
            line.SetQty(baseQty);
        }

        /// <summary>
        /// Prices a product line from the requisition's price list using the actual
        /// attribute-set instance. PriceActual is the price per SELECTED unit — the
        /// figure the requisition window shows in Unit Price — so the quantity handed
        /// to the pricing engine is the entered one and no base-unit rescaling follows.
        /// </summary>
        private void SetLinePriceWithAttribute(MRequisitionLine line, MRequisition parent, int asi)
        {
            if (line.GetM_Product_ID() == 0) return;
            // A requisition is always bought, never sold: IsSOTrx = false.
            MProductPricing pp = new MProductPricing(line.GetAD_Client_ID(), line.GetAD_Org_ID(),
                line.GetM_Product_ID(), Util.GetValueOfInt(parent.Get_Value("C_BPartner_ID")), line.GetQty(), false);
            pp.SetM_PriceList_ID(parent.GetM_PriceList_ID());
            pp.SetPriceDate(parent.GetDateDoc());
            pp.SetM_AttributeSetInstance_ID(asi);
            // Mirrors MRequisitionLine.SetPrice: the line is priced off its own unit only
            // where ED011 (multi-UOM pricing) is installed.
            if (Env.IsModuleInstalled("ED011_"))
                pp.SetC_UOM_ID(LineUomId(line));
            line.SetPriceActual(pp.GetPriceStd());
            if (LineUomId(line) == 0)
                SetLineUom(line, pp.GetC_UOM_ID());
        }

        /// <summary>
        /// Reads the AD_Column.Callout for the changed column and executes the equivalent
        /// server-side callout logic, returning the changed columns as a patch object.
        /// </summary>
        public RequisitionCalloutResult RunColumnCallout(Ctx ctx, RequisitionLineCalcRequest req)
        {
            RequisitionCalloutResult res = new RequisitionCalloutResult();
            if (req == null || req.M_Requisition_ID <= 0) return res;

            string column = MapTriggerToColumn(req.TriggerColumn);
            res.Column = column;
            res.Callout = ReadColumnCallout(ctx, "M_RequisitionLine", column);

            MRequisition parent;
            MRequisitionLine line = BuildCalcLine(ctx, req, out parent);
            if (line == null || (req.M_Product_ID <= 0 && req.C_Charge_ID <= 0)) return res;

            res.Values["C_UOM_ID"] = LineUomId(line);
            res.Values["PriceActual"] = line.GetPriceActual();
            res.Values["Qty"] = line.GetQty();
            res.Values["LineNetAmt"] = line.GetLineNetAmt();
            if (req.M_Product_ID > 0) res.Values["C_Charge_ID"] = 0;
            else if (req.C_Charge_ID > 0) { res.Values["M_Product_ID"] = 0; res.Values["M_AttributeSetInstance_ID"] = 0; }

            res.Display["uomName"] = GetUomLabel(ctx, LineUomId(line));
            return res;
        }

        private string ReadColumnCallout(Ctx ctx, string tableName, string columnName)
        {
            if (string.IsNullOrEmpty(columnName)) return "";
            string sql = @"SELECT c.Callout
                           FROM AD_Column c
                           INNER JOIN AD_Table t ON (c.AD_Table_ID = t.AD_Table_ID)
                           WHERE t.TableName = @t
                             AND c.ColumnName = @c
                             AND c.IsActive = 'Y'";
            object o = DB.ExecuteScalar(sql,
                new SqlParameter[] { new SqlParameter("@t", tableName), new SqlParameter("@c", columnName) }, null);
            return Util.GetValueOfString(o);
        }

        private string MapTriggerToColumn(string trigger)
        {
            switch (trigger)
            {
                case "product": return "M_Product_ID";
                case "charge": return "C_Charge_ID";
                case "M_Product_ID":
                case "C_Charge_ID":
                case "Qty":
                case "QtyEntered":
                case "PriceActual":
                case "C_UOM_ID":
                case "M_AttributeSetInstance_ID":
                    return trigger;
                default: return trigger ?? "";
            }
        }

        private void ApplyDiscount(MRequisitionLine line, decimal discountPct)
        {
            if (discountPct <= 0) return;
            if (discountPct > 100) discountPct = 100;
            decimal factor = decimal.Subtract(1m, decimal.Divide(discountPct, 100m));
            line.SetPriceActual(decimal.Multiply(line.GetPriceActual(), factor));
        }

        private static readonly HashSet<string> CORE_OR_SYSTEM_COLUMNS = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            "M_RequisitionLine_ID", "M_Requisition_ID", "AD_Client_ID", "AD_Org_ID",
            "Created", "CreatedBy", "Updated", "UpdatedBy", "IsActive",
            "M_Product_ID", "C_Charge_ID", "M_AttributeSetInstance_ID",
            // QtyEntered is blocked alongside Qty: SaveLines writes the pair itself, and
            // ApplyExtraColumns must not overwrite QtyEntered with the stale client value
            // (which carries the old DB qty), otherwise MRequisitionLine.BeforeSave uses
            // the stale QtyEntered to recalculate Qty — reverting the user's qty change.
            "Qty", "QtyEntered", "C_UOM_ID", "PriceActual", "LineNetAmt",
            "Line", "Description"
        };

        private HashSet<string> _updateableColumns;
        private HashSet<string> _yesNoColumns;
        private HashSet<string> _referenceColumns;

        /// <summary>Persists every non-core, updateable M_RequisitionLine column through PO.Set_Value.</summary>
        private void ApplyExtraColumns(MRequisitionLine line, Dictionary<string, object> values, HashSet<string> touched)
        {
            if (values == null || values.Count == 0) return;
            HashSet<string> updateable = GetUpdateableColumns();
            foreach (KeyValuePair<string, object> kv in values)
            {
                string col = kv.Key;
                bool isTouched = touched != null && touched.Contains(col);
                if (kv.Value == null && !isTouched) continue;
                if (CORE_OR_SYSTEM_COLUMNS.Contains(col)) continue;
                if (updateable.Count > 0 && !updateable.Contains(col)) continue;
                try
                {
                    object val = GetYesNoColumns().Contains(col)
                        ? (object)CoerceYesNo(kv.Value)
                        : CoerceJsonValue(kv.Value);
                    if (GetReferenceColumns().Contains(col) &&
                        (val == null || (decimal.TryParse(val.ToString(), out decimal number) && number == 0)))
                    {
                        if (isTouched) line.Set_Value(col, null);
                        continue;
                    }
                    line.Set_Value(col, val);
                }
                catch (Exception ex) { log.Warning("VAS_240 SaveLines: skip column " + col + " - " + ex.Message); }
            }
        }

        private object CoerceJsonValue(object v)
        {
            if (v is long)
            {
                long l = (long)v;
                if (l >= int.MinValue && l <= int.MaxValue) return (int)l;
                return l;
            }
            if (v is double) return Convert.ToDecimal((double)v);
            string s = v as string;
            if (s != null && Regex.IsMatch(s, @"^\d{4}-\d{2}-\d{2}(T\d{2}:\d{2}(:\d{2})?)?$"))
            {
                DateTime dt;
                if (DateTime.TryParse(s, CultureInfo.InvariantCulture, DateTimeStyles.None, out dt))
                    return dt;
            }
            return v;
        }

        private void EnsureColumnSets()
        {
            if (_updateableColumns != null) return;
            _updateableColumns = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            _yesNoColumns = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            _referenceColumns = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            DataSet ds = DB.ExecuteDataset(
                @"SELECT c.ColumnName, c.AD_Reference_ID
                  FROM AD_Column c
                  INNER JOIN AD_Table t ON (c.AD_Table_ID = t.AD_Table_ID)
                  WHERE t.TableName = 'M_RequisitionLine'
                    AND c.IsActive = 'Y'
                    AND COALESCE(c.IsUpdateable, 'Y') = 'Y'");
            if (ds != null && ds.Tables.Count > 0)
                foreach (DataRow r in ds.Tables[0].Rows)
                {
                    string name = Util.GetValueOfString(r["ColumnName"]);
                    _updateableColumns.Add(name);
                    int refId = Util.GetValueOfInt(r["AD_Reference_ID"]);
                    if (refId == 20) _yesNoColumns.Add(name);
                    if (refId == 18 || refId == 19 || refId == 30) _referenceColumns.Add(name);
                }
        }

        private HashSet<string> GetUpdateableColumns() { EnsureColumnSets(); return _updateableColumns; }
        private HashSet<string> GetYesNoColumns() { EnsureColumnSets(); return _yesNoColumns; }
        private HashSet<string> GetReferenceColumns() { EnsureColumnSets(); return _referenceColumns; }

        private static bool CoerceYesNo(object v)
        {
            if (v is bool) return (bool)v;
            string s = Util.GetValueOfString(v).Trim();
            return s.Equals("Y", StringComparison.OrdinalIgnoreCase)
                || s.Equals("true", StringComparison.OrdinalIgnoreCase)
                || s == "1";
        }

        private string GetUomLabel(Ctx ctx, int C_UOM_ID)
        {
            if (C_UOM_ID <= 0) return "";
            object o = DB.ExecuteScalar(
                "SELECT Name FROM C_UOM WHERE C_UOM_ID=@id",
                new SqlParameter[] { new SqlParameter("@id", C_UOM_ID) }, null);
            return Util.GetValueOfString(o);
        }

        /// <summary>
        /// Returns the system-default UOM ID ("Each") by delegating to the framework's
        /// MUOM.GetDefault_UOM_ID — the same call that MInvoiceLine.SetC_Charge_ID makes
        /// internally. A requisition line gets no UOM for a charge, so we mirror it here.
        /// </summary>
        /// <param name="ctx">session context</param>
        /// <returns>default C_UOM_ID from the framework, or 0 if not found</returns>
        private int GetDefaultUomId(Ctx ctx)
        {
            return MUOM.GetDefault_UOM_ID(ctx);
        }

        private Dictionary<int, int> _productUom;

        /// <summary>
        /// The product's own stocking unit (M_Product.C_UOM_ID) — the BASE unit every
        /// quantity on the line is converted to. Cached per instance.
        /// </summary>
        /// <param name="ctx">session context</param>
        /// <param name="productId">M_Product_ID to look up</param>
        /// <returns>C_UOM_ID from M_Product, or 0 when the product does not exist</returns>
        private int GetProductUomId(Ctx ctx, int productId)
        {
            if (productId <= 0) return 0;
            if (_productUom == null) _productUom = new Dictionary<int, int>();
            int cached;
            if (_productUom.TryGetValue(productId, out cached)) return cached;
            object val = DB.ExecuteScalar(
                "SELECT p.C_UOM_ID FROM M_Product p WHERE p.M_Product_ID = @M_Product_ID AND p.IsActive = 'Y'",
                new SqlParameter[] { new SqlParameter("@M_Product_ID", productId) }, null);
            int uom = Util.GetValueOfInt(val);
            _productUom[productId] = uom;
            return uom;
        }

        #endregion

        #region Product attributes (M_AttributeSetInstance)

        /// <summary>Reads the product's attribute set definition for the attribute picker.</summary>
        /// <summary>
        /// Returns the product's attribute-set definition (attributes + allowed values)
        /// for the attribute picker dialog.
        /// </summary>
        /// <param name="ctx">session context</param>
        /// <param name="M_Product_ID">product whose attribute set is read</param>
        /// <returns>attribute-set definition; empty object when no set is configured</returns>
        public RequisitionAttributeSetInfo GetProductAttributes(Ctx ctx, int M_Product_ID)
        {
            RequisitionAttributeSetInfo info = new RequisitionAttributeSetInfo();
            if (M_Product_ID <= 0) return info;

            MProduct product = MProduct.Get(ctx, M_Product_ID);
            if (product == null) return info;
            int M_AttributeSet_ID = product.GetM_AttributeSet_ID();
            if (M_AttributeSet_ID <= 0) return info;

            info.M_AttributeSet_ID = M_AttributeSet_ID;
            info.M_Product_ID = M_Product_ID;
            info.ProductName = product.GetName();

            MRole role = MRole.GetDefault(ctx);
            if (role != null)
            {
                info.IsCanCreate = role.IsCanCreateAttribute();
                info.IsCanEdit = role.IsCanEditAttribute();
            }

            MAttributeSet mas = MAttributeSet.Get(ctx, M_AttributeSet_ID);
            if (mas != null)
            {
                info.IsLot = mas.IsLot();
                info.IsSerNo = mas.IsSerNo();
                info.IsGuaranteeDate = mas.IsGuaranteeDate();
                info.IsMandatory = mas.IsMandatory();
                if (info.IsGuaranteeDate)
                {
                    int gdays = product.GetGuaranteeDays();
                    if (gdays <= 0) gdays = mas.GetGuaranteeDays();
                    DateTime gdt = gdays > 0 ? DateTime.Now.AddDays(gdays) : DateTime.Now;
                    info.GuaranteeDateDefault = gdt.ToString("yyyy-MM-dd");
                }
            }

            string sql = @"SELECT a.M_Attribute_ID, a.Name AS AttributeName, a.AttributeValueType,
                                  COALESCE(a.IsInstanceAttribute, 'N') AS IsInstanceAttribute,
                                  COALESCE(a.IsMandatory, 'N') AS IsMandatory,
                                  av.M_AttributeValue_ID, COALESCE(av.Name, N'') AS ValueName,
                                  COALESCE(av.Value, N'') AS ValueCode
                           FROM M_AttributeUse au
                           INNER JOIN M_Attribute a ON (au.M_Attribute_ID = a.M_Attribute_ID)
                           LEFT OUTER JOIN M_AttributeValue av ON (a.M_Attribute_ID = av.M_Attribute_ID
                                AND av.IsActive = 'Y')
                           WHERE au.M_AttributeSet_ID = @setId
                             AND au.IsActive = 'Y'
                             AND a.IsActive = 'Y'
                           ORDER BY au.SeqNo, a.Name, av.Name";

            DataSet ds = DB.ExecuteDataset(sql,
                new SqlParameter[] { new SqlParameter("@setId", M_AttributeSet_ID) }, null);
            if (ds == null || ds.Tables.Count == 0) return info;

            Dictionary<int, RequisitionAttributeDef> map = new Dictionary<int, RequisitionAttributeDef>();
            foreach (DataRow r in ds.Tables[0].Rows)
            {
                int attrId = Util.GetValueOfInt(r["M_Attribute_ID"]);
                RequisitionAttributeDef def;
                if (!map.TryGetValue(attrId, out def))
                {
                    def = new RequisitionAttributeDef();
                    def.M_Attribute_ID = attrId;
                    def.Name = Util.GetValueOfString(r["AttributeName"]);
                    def.ValueType = Util.GetValueOfString(r["AttributeValueType"]);
                    def.IsInstanceAttribute = Util.GetValueOfString(r["IsInstanceAttribute"]) == "Y";
                    def.IsMandatory = Util.GetValueOfString(r["IsMandatory"]) == "Y";
                    def.Values = new List<RequisitionAttributeValueDef>();
                    map[attrId] = def;
                    info.Attributes.Add(def);
                }
                int valId = Util.GetValueOfInt(r["M_AttributeValue_ID"]);
                if (valId > 0)
                {
                    RequisitionAttributeValueDef v = new RequisitionAttributeValueDef();
                    v.M_AttributeValue_ID = valId;
                    v.Code = Util.GetValueOfString(r["ValueCode"]);
                    v.Name = Util.GetValueOfString(r["ValueName"]);
                    def.Values.Add(v);
                }
            }
            return info;
        }

        /// <summary>
        /// Returns the per-attribute values stored on an existing attribute-set instance,
        /// used to pre-populate the edit form when the user opens an already-assigned ASI.
        /// </summary>
        /// <param name="ctx">session context</param>
        /// <param name="M_AttributeSetInstance_ID">instance whose values are read</param>
        /// <returns>list of typed attribute values; empty list when the instance does not exist</returns>
        public List<RequisitionAttributeInstanceValue> GetInstanceValues(Ctx ctx, int M_AttributeSetInstance_ID)
        {
            List<RequisitionAttributeInstanceValue> list = new List<RequisitionAttributeInstanceValue>();
            if (M_AttributeSetInstance_ID <= 0) return list;
            string sql = @"SELECT ai.M_Attribute_ID, a.AttributeValueType,
                                  ai.M_AttributeValue_ID, COALESCE(ai.Value, N'') AS StringValue, ai.ValueNumber
                           FROM M_AttributeInstance ai
                           INNER JOIN M_Attribute a ON (ai.M_Attribute_ID = a.M_Attribute_ID)
                           WHERE ai.M_AttributeSetInstance_ID = @asi";
            DataSet ds = DB.ExecuteDataset(sql,
                new SqlParameter[] { new SqlParameter("@asi", M_AttributeSetInstance_ID) }, null);
            if (ds == null || ds.Tables.Count == 0) return list;
            foreach (DataRow r in ds.Tables[0].Rows)
            {
                RequisitionAttributeInstanceValue v = new RequisitionAttributeInstanceValue();
                v.M_Attribute_ID = Util.GetValueOfInt(r["M_Attribute_ID"]);
                v.ValueType = Util.GetValueOfString(r["AttributeValueType"]);
                v.M_AttributeValue_ID = Util.GetValueOfInt(r["M_AttributeValue_ID"]);
                v.StringValue = Util.GetValueOfString(r["StringValue"]);
                if (r["ValueNumber"] != null && r["ValueNumber"] != DBNull.Value)
                    v.NumberValue = Util.GetValueOfDecimal(r["ValueNumber"]);
                list.Add(v);
            }
            return list;
        }

        /// <summary>
        /// Creates or updates an M_AttributeSetInstance from the picker selection
        /// by delegating to the framework's PAttributesModel.SaveAttribute so that
        /// dedup, mandatory-validation and AttrCode / UPC behaviour stay identical
        /// to the standard ASI control.
        /// </summary>
        /// <param name="ctx">session context</param>
        /// <param name="req">attribute instance create / update request</param>
        /// <returns>new instance id + description, or Error text on failure</returns>
        public RequisitionAttributeSaveResult SaveAttribute(Ctx ctx, RequisitionAttributeSaveRequest req)
        {
            RequisitionAttributeSaveResult res = new RequisitionAttributeSaveResult();
            if (req == null) return res;

            MProduct product = req.M_Product_ID > 0 ? MProduct.Get(ctx, req.M_Product_ID) : null;
            if (product == null || product.GetM_AttributeSet_ID() <= 0) return res;

            MAttributeSet aset = MAttributeSet.Get(ctx, product.GetM_AttributeSet_ID());
            List<KeyNamePair> values = BuildAttributeValueList(aset, req.Values);

            bool isEdited = req.M_AttributeSetInstance_ID > 0;
            AttributeInstance fres = new PAttributesModel().SaveAttribute(
                0, req.Lot, req.SerNo, req.GuaranteeDate, "",
                false, req.M_AttributeSetInstance_ID, req.M_Product_ID, 0,
                "", isEdited, values, ctx);

            if (fres != null)
            {
                if (string.IsNullOrEmpty(fres.Error))
                {
                    res.M_AttributeSetInstance_ID = fres.M_AttributeSetInstance_ID;
                    res.Description = fres.M_AttributeSetInstanceName;
                }
                else
                {
                    res.Error = fres.Error;
                }
            }
            return res;
        }

        /// <summary>
        /// Maps the picker selection onto the positional List&lt;KeyNamePair&gt; expected
        /// by PAttributesModel.SaveAttribute: one entry per instance attribute, in
        /// M_AttributeSet order (aset.GetMAttributes(true)), typed by value type.
        /// A missing selection yields an empty placeholder so positional indexing
        /// stays aligned and the framework's mandatory check still fires correctly.
        /// </summary>
        /// <param name="aset">attribute set definition for the product</param>
        /// <param name="selections">values entered by the user in the picker</param>
        /// <returns>positionally-aligned value list for PAttributesModel.SaveAttribute</returns>
        private List<KeyNamePair> BuildAttributeValueList(MAttributeSet aset, List<RequisitionAttributeValueSelection> selections)
        {
            List<KeyNamePair> values = new List<KeyNamePair>();
            if (aset == null) return values;

            Dictionary<int, RequisitionAttributeValueSelection> byAttr = new Dictionary<int, RequisitionAttributeValueSelection>();
            if (selections != null)
            {
                foreach (RequisitionAttributeValueSelection sel in selections)
                {
                    if (sel != null && sel.M_Attribute_ID > 0)
                        byAttr[sel.M_Attribute_ID] = sel;
                }
            }

            MAttribute[] attributes = aset.GetMAttributes(true);
            foreach (MAttribute attr in attributes)
            {
                RequisitionAttributeValueSelection sel;
                byAttr.TryGetValue(attr.Get_ID(), out sel);

                if (MAttribute.ATTRIBUTEVALUETYPE_List.Equals(attr.GetAttributeValueType()))
                {
                    int valId = sel != null ? sel.M_AttributeValue_ID : 0;
                    string label = sel != null ? sel.DisplayValue : "";
                    values.Add(new KeyNamePair(valId, label));
                }
                else if (MAttribute.ATTRIBUTEVALUETYPE_Number.Equals(attr.GetAttributeValueType()))
                {
                    // "0" avoids Convert.ToDecimal on an empty string inside the framework.
                    string num = sel != null && sel.NumberValue.HasValue
                        ? sel.NumberValue.Value.ToString(CultureInfo.InvariantCulture)
                        : "0";
                    values.Add(new KeyNamePair(0, num));
                }
                else
                {
                    values.Add(new KeyNamePair(0, sel != null ? (sel.StringValue ?? "") : ""));
                }
            }
            return values;
        }

        #endregion

        #region Write actions (insert / update / delete M_RequisitionLine)

        /// <summary>
        /// Inserts or updates the supplied requisition lines through MRequisitionLine.
        /// All lines share a single transaction so a failure rolls the whole batch back.
        /// </summary>
        public RequisitionSaveResult SaveLines(Ctx ctx, int M_Requisition_ID, int AD_Window_ID, List<RequisitionLineInput> rows, int page = 0)
        {
            RequisitionSaveResult res = new RequisitionSaveResult();
            if (M_Requisition_ID <= 0 || rows == null || rows.Count == 0)
            {
                res.ErrorKey = "VAS_240_NothingToSave";
                return res;
            }

            RequisitionPanelData ctxData = new RequisitionPanelData();
            LoadParentContext(ctx, M_Requisition_ID, ctxData);
            if (ctxData.M_Requisition_ID <= 0)
            {
                res.ErrorKey = "VAS_240_NoAccess";
                return res;
            }
            if (!ctxData.IsEditable)
            {
                res.ErrorKey = "VAS_240_NotEditable";
                return res;
            }

            Trx trx = Trx.GetTrx(Trx.CreateTrxName("VAS240Save_" + M_Requisition_ID));
            try
            {
                MRequisition parent = new MRequisition(ctx, M_Requisition_ID, trx);
                foreach (RequisitionLineInput input in rows)
                {
                    if (input.M_Product_ID <= 0 && input.C_Charge_ID <= 0)
                        continue;

                    MRequisitionLine line = input.M_RequisitionLine_ID > 0
                        ? new MRequisitionLine(ctx, input.M_RequisitionLine_ID, trx)
                        : new MRequisitionLine(parent);

                    if (input.M_RequisitionLine_ID <= 0)
                        line.SetM_Requisition_ID(M_Requisition_ID);

                    if (input.M_Product_ID > 0)
                    {
                        line.SetM_Product_ID(input.M_Product_ID);
                        line.SetC_Charge_ID(0);
                        // BeforeSave blanks the instance on a charge line; set it only for a product.
                        line.SetM_AttributeSetInstance_ID(input.M_AttributeSetInstance_ID);
                    }
                    else
                    {
                        line.SetC_Charge_ID(input.C_Charge_ID);
                        line.SetM_Product_ID(0);
                    }

                    // The unit first, then the quantity: SetLineQty converts the entered
                    // figure into the product's base unit against the unit now on the line.
                    if (input.C_UOM_ID > 0)
                        SetLineUom(line, input.C_UOM_ID);
                    else if (input.M_Product_ID > 0 && LineUomId(line) <= 0)
                        SetLineUom(line, GetProductUomId(ctx, input.M_Product_ID));

                    // A charge line has no product to take a unit from; the framework does
                    // not set one either, and a line without a unit fails to save.
                    if (input.C_Charge_ID > 0 && LineUomId(line) <= 0)
                        SetLineUom(line, GetDefaultUomId(ctx));

                    decimal entered = input.QtyEntered > 0 ? input.QtyEntered : (input.Qty > 0 ? input.Qty : 1);
                    SetLineQty(ctx, line, entered);

                    // The panel prices every line via CalcLine, so persist the client-sent price
                    // exactly, then state the amount from the pair just written — BeforeSave only
                    // recomputes LineNetAmt while it is still zero, so an edited price or quantity
                    // would otherwise keep the amount the row was first saved with.
                    line.SetPriceActual(input.PriceActual);
                    line.SetLineNetAmt();

                    if (input.Line > 0)
                        line.SetLine(input.Line);

                    line.SetDescription(input.Description ?? "");

                    HashSet<string> touchedCols = (input.TouchedCols != null && input.TouchedCols.Count > 0)
                        ? new HashSet<string>(input.TouchedCols, StringComparer.OrdinalIgnoreCase)
                        : null;
                    ApplyExtraColumns(line, input.Values, touchedCols);

                    if (!line.Save())
                    {
                        string err = string.Empty;
                        ValueNamePair pp = VLogger.RetrieveError();
                        if (pp != null)
                        {
                            string val = pp.GetName();
                            if (String.IsNullOrEmpty(val))
                                val = Msg.GetMsg(ctx, pp.GetValue());
                            err = val;
                        }
                        log.Warning("VAS_240 SaveLines: line save failed (Line " + input.Line + ") - " + err);
                        res.LineErrors.Add(new RequisitionLineSaveError
                        {
                            RowKey = input.RowKey,
                            M_RequisitionLine_ID = input.M_RequisitionLine_ID,
                            Line = input.Line,
                            Message = err
                        });
                    }
                }

                if (res.LineErrors.Count > 0)
                {
                    trx.Rollback();
                    res.ErrorKey = "VAS_240_SaveFailed";
                    res.ErrorDetail = res.LineErrors[0].Message;
                    return res;
                }

                trx.Commit();
            }
            catch (Exception ex)
            {
                trx.Rollback();
                log.Log(Level.SEVERE, "VAS_240 SaveLines failed", ex);
                res.ErrorKey = "VAS_240_SaveFailed";
                res.ErrorDetail = ex.Message;
                return res;
            }
            finally
            {
                trx.Close();
                trx = null;
            }

            res.Success = true;
            if (page < 0) page = 0;
            int total;
            res.Lines = LoadLines(ctx, M_Requisition_ID, ResolveRequisitionLineTabs(AD_Window_ID), page, out total);
            res.LinesTotal = total;
            res.LinePage = page;
            res.LinePageSize = LINE_PAGE_SIZE;
            res.OtherPagesSubtotal = ComputeOtherPageTotal(ctx, M_Requisition_ID, res.Lines);
            return res;
        }

        /// <summary>Soft-deletes the supplied saved requisition lines through MRequisitionLine.</summary>
        public RequisitionSaveResult DeleteLines(Ctx ctx, int M_Requisition_ID, int AD_Window_ID, List<int> lineIds, int page = 0)
        {
            RequisitionSaveResult res = new RequisitionSaveResult();
            if (M_Requisition_ID <= 0 || lineIds == null || lineIds.Count == 0)
            {
                res.ErrorKey = "VAS_240_NothingToSave";
                return res;
            }

            RequisitionPanelData ctxData = new RequisitionPanelData();
            LoadParentContext(ctx, M_Requisition_ID, ctxData);
            if (ctxData.M_Requisition_ID <= 0) { res.ErrorKey = "VAS_240_NoAccess"; return res; }
            if (!ctxData.IsEditable) { res.ErrorKey = "VAS_240_NotEditable"; return res; }

            Trx trx = Trx.GetTrx(Trx.CreateTrxName("VAS240Delete_" + M_Requisition_ID));
            try
            {
                foreach (int id in lineIds)
                {
                    if (id <= 0) continue;
                    MRequisitionLine line = new MRequisitionLine(ctx, id, trx);
                    if (line.Get_ID() != id) continue;
                    if (line.GetM_Requisition_ID() != M_Requisition_ID) continue;
                    if (!line.Delete(true, trx))
                    {
                        trx.Rollback();
                        string err = string.Empty;
                        ValueNamePair pp = VLogger.RetrieveError();
                        if (pp != null)
                        {
                            string val = pp.GetName();
                            if (String.IsNullOrEmpty(val))
                                val = Msg.GetMsg(ctx, pp.GetValue());
                            if (String.IsNullOrEmpty(val))
                                err = Msg.GetMsg(ctx, "VAS_240_DeleteFailed");
                            else
                                err = val;
                        }
                        log.Warning("VAS_240 DeleteLines: delete failed for line " + id);
                        res.ErrorKey = err;
                        return res;
                    }
                }
                trx.Commit();
            }
            catch (Exception ex)
            {
                trx.Rollback();
                log.Log(Level.SEVERE, "VAS_240 DeleteLines failed", ex);
                res.ErrorKey = "VAS_240_DeleteFailed";
                res.ErrorDetail = ex.Message;
                return res;
            }
            finally
            {
                trx.Close();
                trx = null;
            }

            res.Success = true;
            if (page < 0) page = 0;
            int total;
            res.Lines = LoadLines(ctx, M_Requisition_ID, ResolveRequisitionLineTabs(AD_Window_ID), page, out total);
            int pageCount = System.Math.Max(1, (int)System.Math.Ceiling(total / (double)LINE_PAGE_SIZE));
            if (page > pageCount - 1)
            {
                page = pageCount - 1;
                res.Lines = LoadLines(ctx, M_Requisition_ID, ResolveRequisitionLineTabs(AD_Window_ID), page, out total);
            }
            res.LinesTotal = total;
            res.LinePage = page;
            res.LinePageSize = LINE_PAGE_SIZE;
            res.OtherPagesSubtotal = ComputeOtherPageTotal(ctx, M_Requisition_ID, res.Lines);
            return res;
        }

        #endregion
    }

    #region Data contracts — VAS_240 specific

    /// <summary>Parent requisition context + saved lines returned to the panel on load.</summary>
    public class RequisitionPanelData
    {
        public int M_Requisition_ID { get; set; }
        public int AD_Client_ID { get; set; }
        public int AD_Org_ID { get; set; }
        /// <summary>M_Requisition.DocumentNo — shown in the panel heading.</summary>
        public string DocumentNo { get; set; }
        /// <summary>M_Requisition.C_BPartner_ID — the requester (not a vendor).</summary>
        public int C_BPartner_ID { get; set; }
        /// <summary>M_Requisition.M_Warehouse_ID — the warehouse the goods are requested for.</summary>
        public int M_Warehouse_ID { get; set; }
        public int M_PriceList_ID { get; set; }
        /// <summary>Currency of the requisition's PRICE LIST — a requisition has none of its own.</summary>
        public int C_Currency_ID { get; set; }
        public DateTime? DateDoc { get; set; }
        /// <summary>
        /// Header values that M_RequisitionLine field DisplayLogic / ReadOnlyLogic name as
        /// tokens (@M_Warehouse_ID@, @DateRequired@, ...). A column that is NULL on the
        /// requisition is simply ABSENT from this bag, which the client resolves to "" —
        /// so "@token@=null" matches and "@token@&gt;0" does not.
        /// </summary>
        public Dictionary<string, string> LogicContext { get; set; }
        public string DocStatus { get; set; }
        public bool Processed { get; set; }
        public bool IsEditable { get; set; }
        public int LinesTotal { get; set; }
        public int LinePage { get; set; }
        public int LinePageSize { get; set; }
        /// <summary>Sum of LineNetAmt for the saved lines NOT on the loaded page.</summary>
        public decimal OtherPagesSubtotal { get; set; }
        public int StdPrecision { get; set; }
        public string CurSymbol { get; set; }
        public string CurISO { get; set; }
        public int AD_Window_ID { get; set; }
        public int AD_Tab_ID { get; set; }
        public List<int> AD_Tab_IDs { get; set; }
        public List<RequisitionLineRow> Lines { get; set; }
        public List<RequisitionUomItem> UomList { get; set; }
        public List<RequisitionColumnMeta> Columns { get; set; }
        public Dictionary<string, string> LoginContext { get; set; }

        public RequisitionPanelData()
        {
            Lines = new List<RequisitionLineRow>();
            UomList = new List<RequisitionUomItem>();
            Columns = new List<RequisitionColumnMeta>();
            AD_Tab_IDs = new List<int>();
            LoginContext = new Dictionary<string, string>();
            LogicContext = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            StdPrecision = 2;
        }
    }

    /// <summary>Request for the per-row UOM lookup re-filter.</summary>
    public class RequisitionLookupRequest
    {
        public int M_Requisition_ID { get; set; }
        public string ColumnName { get; set; }
        public Dictionary<string, object> RowValues { get; set; }
        public RequisitionLookupRequest() { RowValues = new Dictionary<string, object>(); }
    }

    /// <summary>Per-row filtered UOM list for one requisition line.</summary>
    public class RequisitionLookupData
    {
        public int M_Requisition_ID { get; set; }
        public List<RequisitionUomItem> UomList { get; set; }
        public RequisitionLookupData() { UomList = new List<RequisitionUomItem>(); }
    }

    /// <summary>Request for the generic FK lookup of a dynamic M_RequisitionLine field.</summary>
    public class RequisitionRefLookupRequest
    {
        public int M_Requisition_ID { get; set; }
        public string ColumnName { get; set; }
        public string Query { get; set; }
        public int Id { get; set; }
        public int PageSize { get; set; }
        public int Offset { get; set; }
        public Dictionary<string, object> RowValues { get; set; }
        public RequisitionRefLookupRequest() { RowValues = new Dictionary<string, object>(); }
    }

    /// <summary>Inbound callout / calc request from the requisition panel.</summary>
    public class RequisitionLineCalcRequest
    {
        public int M_Requisition_ID { get; set; }
        public string TriggerColumn { get; set; }
        public int M_Product_ID { get; set; }
        public int C_Charge_ID { get; set; }
        public int M_AttributeSetInstance_ID { get; set; }
        /// <summary>Quantity as keyed, in the line's SELECTED unit (C_UOM_ID).</summary>
        public decimal QtyEntered { get; set; }
        /// <summary>Quantity in the product's BASE unit; fallback when QtyEntered is absent.</summary>
        public decimal Qty { get; set; }
        public int C_UOM_ID { get; set; }
        public decimal PriceActual { get; set; }
        /// <summary>True when the user typed the price: keep it instead of re-pricing.</summary>
        public bool PriceOverride { get; set; }
        public decimal Discount { get; set; }
    }

    /// <summary>Recomputed values returned by the requisition-line callout.</summary>
    public class RequisitionLineCalcResult
    {
        public int C_UOM_ID { get; set; }
        public string UOMName { get; set; }
        public decimal Qty { get; set; }
        public decimal PriceActual { get; set; }
        public decimal LineNetAmt { get; set; }
    }

    /// <summary>A saved requisition line shown in the panel grid.</summary>
    public class RequisitionLineRow
    {
        public int M_RequisitionLine_ID { get; set; }
        public int Line { get; set; }
        public int M_Product_ID { get; set; }
        public string ProductName { get; set; }
        public int C_Charge_ID { get; set; }
        public string ChargeName { get; set; }
        public string Description { get; set; }
        public decimal QtyEntered { get; set; }
        public decimal Qty { get; set; }
        public int C_UOM_ID { get; set; }
        public string UOMName { get; set; }
        public decimal PriceActual { get; set; }
        public decimal LineNetAmt { get; set; }
        public int M_AttributeSetInstance_ID { get; set; }
        public string AttrName { get; set; }
        public bool HasAttributeSet { get; set; }
        public string ProductType { get; set; }
        public Dictionary<string, object> Values { get; set; }
        public RequisitionLineRow() { Values = new Dictionary<string, object>(); }
    }

    /// <summary>Attribute-instance create/update request from the requisition panel.</summary>
    public class RequisitionAttributeSaveRequest
    {
        public int M_Product_ID { get; set; }
        public int M_AttributeSetInstance_ID { get; set; }
        public string Lot { get; set; }
        public string SerNo { get; set; }
        public string GuaranteeDate { get; set; }
        public List<RequisitionAttributeValueSelection> Values { get; set; }
    }

    /// <summary>One entered attribute value in an requisition-panel attribute save request.</summary>
    public class RequisitionAttributeValueSelection
    {
        public int M_Attribute_ID { get; set; }
        public string ValueType { get; set; }
        public int M_AttributeValue_ID { get; set; }
        public decimal? NumberValue { get; set; }
        public string StringValue { get; set; }
        public string DisplayValue { get; set; }
    }

    /// <summary>One inbound requisition line to insert / update.</summary>
    public class RequisitionLineInput
    {
        public int M_RequisitionLine_ID { get; set; }
        public string RowKey { get; set; }
        public int Line { get; set; }
        public int M_Product_ID { get; set; }
        public int C_Charge_ID { get; set; }
        public int M_AttributeSetInstance_ID { get; set; }
        public decimal QtyEntered { get; set; }
        public decimal Qty { get; set; }
        public int C_UOM_ID { get; set; }
        public decimal PriceActual { get; set; }
        public decimal Discount { get; set; }
        public string Description { get; set; }
        public Dictionary<string, object> Values { get; set; }
        public List<string> TouchedCols { get; set; }
        public RequisitionLineInput() { Values = new Dictionary<string, object>(); TouchedCols = new List<string>(); }
    }

    /// <summary>POST body for the batch requisition-line save.</summary>
    public class RequisitionSaveLinesRequest
    {
        public int M_Requisition_ID { get; set; }
        public int AD_Window_ID { get; set; }
        public int Page { get; set; }
        public List<RequisitionLineInput> Lines { get; set; }
        public RequisitionSaveLinesRequest() { Lines = new List<RequisitionLineInput>(); }
    }

    /// <summary>POST body for the batch requisition-line delete.</summary>
    public class RequisitionDeleteLinesRequest
    {
        public int M_Requisition_ID { get; set; }
        public int AD_Window_ID { get; set; }
        public int Page { get; set; }
        public List<int> LineIds { get; set; }
        public RequisitionDeleteLinesRequest() { LineIds = new List<int>(); }
    }

    /// <summary>A save failure for one specific requisition line.</summary>
    public class RequisitionLineSaveError
    {
        public string RowKey { get; set; }
        public int M_RequisitionLine_ID { get; set; }
        public int Line { get; set; }
        public string Message { get; set; }
    }

    /// <summary>Result of a save / delete batch for requisition lines.</summary>
    public class RequisitionSaveResult
    {
        public bool Success { get; set; }
        public string ErrorKey { get; set; }
        public string ErrorDetail { get; set; }
        public List<RequisitionLineSaveError> LineErrors { get; set; }
        public List<RequisitionLineRow> Lines { get; set; }
        public int LinesTotal { get; set; }
        public int LinePage { get; set; }
        public int LinePageSize { get; set; }
        public decimal OtherPagesSubtotal { get; set; }
        public RequisitionSaveResult() { Lines = new List<RequisitionLineRow>(); LineErrors = new List<RequisitionLineSaveError>(); }
    }

    /// <summary>
    /// Attribute-set definition returned by GetProductAttributes.
    /// Carries the set-level flags (Lot / SerNo / GuaranteeDate / Mandatory)
    /// plus the ordered list of attributes and their selectable values.
    /// </summary>
    public class RequisitionAttributeSetInfo
    {
        public int M_AttributeSet_ID { get; set; }
        public int M_Product_ID { get; set; }
        public string ProductName { get; set; }
        public bool IsLot { get; set; }
        public bool IsSerNo { get; set; }
        public bool IsGuaranteeDate { get; set; }
        /// <summary>ISO yyyy-MM-dd default for a NEW instance (today + GuaranteeDays, else today).</summary>
        public string GuaranteeDateDefault { get; set; }
        public bool IsMandatory { get; set; }
        public bool IsCanCreate { get; set; }
        public bool IsCanEdit { get; set; }
        public List<RequisitionAttributeDef> Attributes { get; set; }
        public RequisitionAttributeSetInfo() { Attributes = new List<RequisitionAttributeDef>(); }
    }

    /// <summary>One attribute within an requisition-panel attribute set (with its list values).</summary>
    public class RequisitionAttributeDef
    {
        public int M_Attribute_ID { get; set; }
        public string Name { get; set; }
        /// <summary>'L' list, 'N' number, 'S' string.</summary>
        public string ValueType { get; set; }
        public bool IsInstanceAttribute { get; set; }
        public bool IsMandatory { get; set; }
        public List<RequisitionAttributeValueDef> Values { get; set; }
    }

    /// <summary>A selectable list value for an requisition-panel attribute.</summary>
    public class RequisitionAttributeValueDef
    {
        public int M_AttributeValue_ID { get; set; }
        public string Code { get; set; }
        public string Name { get; set; }
    }

    /// <summary>
    /// One stored attribute value on an existing instance, returned by GetInstanceValues
    /// to pre-populate the edit form when the user opens an already-assigned ASI.
    /// </summary>
    public class RequisitionAttributeInstanceValue
    {
        public int M_Attribute_ID { get; set; }
        /// <summary>'L' list, 'N' number, 'S' string.</summary>
        public string ValueType { get; set; }
        public int M_AttributeValue_ID { get; set; }
        public decimal? NumberValue { get; set; }
        public string StringValue { get; set; }
    }

    /// <summary>Result of creating or updating an attribute-set instance via SaveAttribute.</summary>
    public class RequisitionAttributeSaveResult
    {
        public int M_AttributeSetInstance_ID { get; set; }
        public string Description { get; set; }
        /// <summary>Framework mandatory / save error message; empty string on success.</summary>
        public string Error { get; set; }
    }

    /// <summary>
    /// Metadata for one M_RequisitionLine column: reference type, callout, val-rule, display/
    /// read-only logic and optional inline list values. Used by the panel to drive
    /// FK lookups, callout chains and field visibility without extra round-trips.
    /// </summary>
    public class RequisitionColumnMeta
    {
        public string ColumnName { get; set; }
        public int AD_Column_ID { get; set; }
        public string Callout { get; set; }
        public bool IsMandatory { get; set; }
        public int AD_Reference_ID { get; set; }
        public bool IsUpdateable { get; set; }
        public int FieldLength { get; set; }
        public string ReadOnlyLogic { get; set; }
        public int AD_Val_Rule_ID { get; set; }
        public string ValRuleType { get; set; }
        public string ValRuleCode { get; set; }
        public bool IsDisplayed { get; set; }
        /// <summary>True when the column has an AD_Field on the window tab (not a merged-only table column).</summary>
        public bool IsTabField { get; set; }
        public bool IsReadOnly { get; set; }
        public int SeqNo { get; set; }
        public string DisplayLogic { get; set; }
        public int AD_Reference_Value_ID { get; set; }
        public string Name { get; set; }
        public int AD_Image_ID { get; set; }
        /// <summary>Icon-font class name (takes priority over ImageUrl when non-empty).</summary>
        public string IconFont { get; set; }
        public string ImageUrl { get; set; }
        public List<RequisitionRefListItem> RefListValues { get; set; }
        public RequisitionColumnMeta() { RefListValues = new List<RequisitionRefListItem>(); }
    }

    /// <summary>One value of a List (AD_Reference 17) field, sourced from AD_Ref_List.</summary>
    public class RequisitionRefListItem
    {
        public string Value { get; set; }
        public string Name { get; set; }
    }

    /// <summary>One row of a generic FK lookup (id + display label), returned by GetRefLookup.</summary>
    public class RequisitionRefItem
    {
        public int Id { get; set; }
        public string Name { get; set; }
    }

    /// <summary>A unit of measure for the UOM dropdown in the panel grid.</summary>
    public class RequisitionUomItem
    {
        public int C_UOM_ID { get; set; }
        public string Name { get; set; }
    }

    /// <summary>One Product or Charge row in the catalog autocomplete for the panel.</summary>
    public class RequisitionCatalogItem
    {
        public int RecordId { get; set; }
        /// <summary>"P" = product, "C" = charge.</summary>
        public string Kind { get; set; }
        public string SearchKey { get; set; }
        public string DisplayName { get; set; }
        public string Description { get; set; }
        public bool HasAttributeSet { get; set; }
        /// <summary>M_Product.ProductType; empty for a charge.</summary>
        public string ProductType { get; set; }
    }

    /// <summary>
    /// Result of running a M_RequisitionLine column's AD_Column.Callout server-side:
    /// the changed column values the client patches back into the line, the display
    /// labels and the callout reference that was read (for traceability).
    /// </summary>
    public class RequisitionCalloutResult
    {
        public string Column { get; set; }
        public string Callout { get; set; }
        public Dictionary<string, object> Values { get; set; }
        public Dictionary<string, string> Display { get; set; }
        public RequisitionCalloutResult()
        {
            Values = new Dictionary<string, object>();
            Display = new Dictionary<string, string>();
        }
    }

    #endregion
}
