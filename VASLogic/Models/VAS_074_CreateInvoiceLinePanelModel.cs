/******************************************************
 * Module Name    : VASLogic
 * Purpose        : Backing model for the VAS_074_CreateInvoiceLinePanel tab
 *                  panel. Provides parent-invoice context and existing lines,
 *                  paged Product / Charge catalog search (50 rows / scroll),
 *                  the server-side line callout (price / tax / amount), product
 *                  attribute (M_AttributeSetInstance) read + create, barcode
 *                  scan lookup and the C_InvoiceLine insert / update / delete
 *                  write actions (always through the MInvoiceLine business class).
 * chronological  : Development
 *   VAI_XXX        Created  25 June 2026
 ******************************************************/
// NOTE: Replace VAI_XXX above (and in every method header below) with your
// own Employee Code before committing - never use a developer name.

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

namespace VASLogic.Models
{
    /// <summary>
    /// Module Name : VASLogic
    /// Purpose     : Data + write model behind the Create Invoice Line tab panel.
    ///               Every SELECT is filtered through MRole.AddAccessSQL on the
    ///               main physical table alias only and uses bind parameters so
    ///               the same code runs on PostgreSQL and Oracle. All inserts go
    ///               through MInvoiceLine (never a hand-written INSERT) so the
    ///               standard invoice-line callouts (price / tax / amount) run.
    /// Chronological development:
    ///   VAI_XXX        Created  25 June 2026
    /// </summary>
    public class VAS_074_CreateInvoiceLinePanelModel
    {
        private static VLogger log = VLogger.GetVLogger(typeof(VAS_074_CreateInvoiceLinePanelModel).FullName);

        /// <summary>First page size for the Product / Charge catalog search.</summary>
        private const int CATALOG_PAGE_SIZE = 50;

        /// <summary>Saved invoice lines loaded per page (server-side paging).</summary>
        private const int LINE_PAGE_SIZE = 20;

        #region Panel (read) data

        /// <summary>
        /// Builds the panel header context (everything the client-side callouts
        /// need from the parent invoice) plus the already-saved invoice lines.
        /// Returns an empty object (C_Invoice_ID = 0) when the role has no access
        /// to the record.
        /// </summary>
        /// <param name="ctx">session context</param>
        /// <param name="C_Invoice_ID">parent invoice</param>
        /// <returns>panel view model</returns>
        public CreateInvoiceLinePanelData GetPanelData(Ctx ctx, int C_Invoice_ID, int AD_Window_ID, int page = 0)
        {
            CreateInvoiceLinePanelData data = new CreateInvoiceLinePanelData();
            if (C_Invoice_ID <= 0) return data;

            LoadParentContext(ctx, C_Invoice_ID, data);
            if (data.C_Invoice_ID <= 0) return data;   // no access / not found

            data.AD_Window_ID = AD_Window_ID;
            // Resolve EVERY C_InvoiceLine tab in the window ONCE - the union of their
            // fields drives BOTH the line data projection (distinct AD_Field columns)
            // and the column meta. A window may expose the table on several tabs.
            List<int> tabIds = ResolveInvoiceLineTabs(AD_Window_ID);
            data.AD_Tab_IDs = tabIds;
            data.AD_Tab_ID = tabIds.Count > 0 ? tabIds[0] : 0;   // first tab (info)
            // Server-side paging: return only LINE_PAGE_SIZE saved lines for the requested
            // page (0-based) plus the total count so the client can render a pager.
            if (page < 0) page = 0;
            int total;
            data.Lines = LoadLines(ctx, C_Invoice_ID, tabIds, page, out total);
            data.LinesTotal = total;
            data.LinePage = page;
            data.LinePageSize = LINE_PAGE_SIZE;
            decimal oNet, oTax, oTcs;
            ComputeOtherPageTotals(ctx, C_Invoice_ID, data.Lines, data.IsTaxIncluded, out oNet, out oTax, out oTcs);
            data.OtherPagesSubtotal = oNet; data.OtherPagesTax = oTax; data.OtherPagesTcs = oTcs;
            LoadCatalogs(ctx, data);                   // UOM + tax dropdown lists
            LoadColumns(ctx, data, tabIds);            // tab-field meta (callout + validation), cached client-side
            LoadLoginContext(ctx, data);               // $Element_*/#Global tokens used by display/read-only logic
            return data;
        }

        /// <summary>
        /// Collects the login / session context values for every @$Token@ / @#Token@
        /// referenced by any column's DisplayLogic or ReadOnlyLogic (e.g. the accounting
        /// element flags @$Element_OT@, @$Element_PJ@ that gate AD_OrgTrx_ID / C_Project_ID
        /// ...). These live in the session context, not the record, so the client resolves
        /// such tokens from this map. Tokens with no value are omitted -> the client treats
        /// them as "show".
        /// </summary>
        /// <param name="ctx">session context</param>
        /// <param name="data">view model whose Columns carry the logic strings</param>
        private void LoadLoginContext(Ctx ctx, CreateInvoiceLinePanelData data)
        {
            Regex rx = new Regex(@"@([#$][A-Za-z0-9_]+)@");
            HashSet<string> tokens = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (ColumnMeta m in data.Columns)
            {
                if (!string.IsNullOrEmpty(m.DisplayLogic))
                    foreach (Match mt in rx.Matches(m.DisplayLogic)) tokens.Add(mt.Groups[1].Value);
                if (!string.IsNullOrEmpty(m.ReadOnlyLogic))
                    foreach (Match mt in rx.Matches(m.ReadOnlyLogic)) tokens.Add(mt.Groups[1].Value);
            }
            // Accounting-element flags ($Element_OT/PJ/MC/AY/...) gate the accounting
            // dimension fields. They are NOT reliably present in an AJAX session ctx (nor in
            // the client VIS.Env), so a DisplayLogic like @$Element_MC@='Y' resolved to empty
            // -> the client defaulted to SHOW and the logic looked ignored. Resolve them
            // AUTHORITATIVELY from the primary accounting schema's active elements (what login
            // does): $Element_<TYPE> = 'Y' when that ElementType is an active C_AcctSchema_Element.
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

        /// <summary>
        /// Returns the set of accounting ElementTypes (OT, PJ, MC, AY, SR, U1, U2, ...) that
        /// are ACTIVE on the client's primary accounting schema (AD_ClientInfo.C_AcctSchema1_ID).
        /// Used to resolve the @$Element_XX@ display/read-only-logic flags server-side.
        /// </summary>
        /// <param name="ctx">session context (for the client id)</param>
        /// <returns>active element-type codes (case-insensitive)</returns>
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

        /// <summary>
        /// Loads the C_InvoiceLine column metadata ONCE per panel load: each
        /// column's callout reference (AD_Column.Callout), mandatory flag, reference
        /// type, updateable flag, field length and any linked validation rule
        /// (AD_Val_Rule). When an AD_Window_ID is supplied the column set is the
        /// fields actually configured on that window's C_InvoiceLine tab - resolve
        /// the AD_Tab by AD_Table_ID, then read the AD_Field -> AD_Column rows (with
        /// display / read-only / sequence from the field). Falls back to every
        /// active AD_Column when no window / tab is available. Dictionary metadata,
        /// cached client-side so nothing is re-read per change.
        /// </summary>
        /// <param name="ctx">session context</param>
        /// <param name="data">view model being populated</param>
        /// <param name="AD_Tab_IDs">resolved C_InvoiceLine tabs whose fields drive the set</param>
        private void LoadColumns(Ctx ctx, CreateInvoiceLinePanelData data, List<int> AD_Tab_IDs)
        {
            // Window-driven field set: union of the fields across every C_InvoiceLine tab.
            if (AD_Tab_IDs != null && AD_Tab_IDs.Count > 0)
                LoadColumnsFromTabs(ctx, data, AD_Tab_IDs);
            // Always merge in the remaining active C_InvoiceLine columns so the curated
            // "Additional Info" modal fields carry metadata (reference, val rule, read-only,
            // list values) even when they are not configured on the window tab.
            MergeAllColumns(ctx, data);
            LoadListValues(data);
        }

        /// <summary>
        /// Fills the inline AD_Ref_List values for every List (AD_Reference 17) column
        /// in the loaded meta, so the client renders the dropdown without a round trip.
        /// </summary>
        /// <param name="data">view model whose Columns are populated</param>
        private void LoadListValues(CreateInvoiceLinePanelData data)
        {
            foreach (ColumnMeta m in data.Columns)
            {
                if (m.AD_Reference_ID != 17 || m.AD_Reference_Value_ID <= 0) continue;
                DataSet ds = DB.ExecuteDataset(
                    @"SELECT Value, Name FROM AD_Ref_List
                      WHERE AD_Reference_ID = @ref AND IsActive = 'Y'
                      ORDER BY COALESCE(Name, Value)",
                    new SqlParameter[] { new SqlParameter("@ref", m.AD_Reference_Value_ID) }, null);
                if (ds == null || ds.Tables.Count == 0) continue;
                foreach (DataRow r in ds.Tables[0].Rows)
                    m.RefListValues.Add(new RefListItem
                    {
                        Value = Util.GetValueOfString(r["Value"]),
                        Name = Util.GetValueOfString(r["Name"])
                    });
            }
        }

        private Dictionary<int, List<int>> _ilTabsByWindow;

        /// <summary>
        /// Finds EVERY active AD_Tab bound to C_InvoiceLine inside the given window
        /// (a window may surface the line table on more than one tab), ordered by
        /// sequence. Returns an empty list when none. Per-instance cached per window.
        /// </summary>
        /// <param name="AD_Window_ID">target window</param>
        /// <returns>all C_InvoiceLine AD_Tab_IDs (may be empty)</returns>
        private List<int> ResolveInvoiceLineTabs(int AD_Window_ID)
        {
            if (_ilTabsByWindow == null) _ilTabsByWindow = new Dictionary<int, List<int>>();
            List<int> cached;
            if (_ilTabsByWindow.TryGetValue(AD_Window_ID, out cached)) return cached;

            List<int> tabs = new List<int>();
            if (AD_Window_ID > 0)
            {
                int tableId = X_C_InvoiceLine.Table_ID;
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
            _ilTabsByWindow[AD_Window_ID] = tabs;
            return tabs;
        }

        /// <summary>
        /// SELECT expression for ReadOnlyLogic giving AD_Field priority over AD_Column
        /// (per request). AD_Field.ReadOnlyLogic is guarded by ColumnExists (not present in
        /// every schema). When present: the resolved tab field's value first (only when the
        /// query aliases AD_Field f, i.e. <paramref name="hasTabField"/>), then a
        /// representative non-empty value from ANY active C_InvoiceLine field for the column
        /// (so logic set on a sibling field / another tab is still honoured - mirrors how
        /// DisplayLogic is resolved), then AD_Column.ReadOnlyLogic.
        /// </summary>
        /// <param name="hasTabField">true when the outer query aliases AD_Field as f</param>
        /// <returns>a COALESCE(...) SQL expression aliased by the caller as ReadOnlyLogic</returns>
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
                                      AND tt2.TableName = 'C_InvoiceLine'
                                      AND f2.ReadOnlyLogic IS NOT NULL
                                      AND f2.ReadOnlyLogic <> ''), N''), ");
            sb.Append("c.ReadOnlyLogic, N'')");
            return sb.ToString();
        }

        /// <summary>
        /// Reads the AD_Field -> AD_Column metadata across ALL the window's invoice-line
        /// tabs and adds each DISTINCT column once (first occurrence wins, tabs taken in
        /// sequence then field sequence). Returns false (so the caller can fall back)
        /// when none of the tabs have any fields.
        /// </summary>
        /// <param name="ctx">session context</param>
        /// <param name="data">view model being populated</param>
        /// <param name="AD_Tab_IDs">invoice-line tabs</param>
        /// <returns>true when any fields were loaded</returns>
        private bool LoadColumnsFromTabs(Ctx ctx, CreateInvoiceLinePanelData data, List<int> AD_Tab_IDs)
        {
            // Tab ids come from the dictionary (never user input) - safe to inline.
            string inList = string.Join(",", AD_Tab_IDs.ToArray());
            // ReadOnlyLogic: AD_Field (tab field, then any C_InvoiceLine field) over AD_Column.
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
                                  COALESCE(vr.Type, N'')          AS ValRuleType,
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
                if (!seen.Add(name)) continue;   // same column on another tab - keep the first
                data.Columns.Add(MapColumnMeta(r, true));
            }
            return true;
        }

        /// <summary>
        /// Merges every active C_InvoiceLine column not already loaded from the tab into
        /// the meta set (IsDisplayed defaults true; these are used only by the curated
        /// modal, which selects columns by name). Also the standalone fallback when no
        /// tab resolves.
        /// </summary>
        /// <param name="ctx">session context</param>
        /// <param name="data">view model being populated</param>
        private void MergeAllColumns(Ctx ctx, CreateInvoiceLinePanelData data)
        {
            HashSet<string> have = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (ColumnMeta cm in data.Columns) have.Add(cm.ColumnName);

            // ReadOnlyLogic: a representative AD_Field value (any active C_InvoiceLine field)
            // over AD_Column - so a column merged in (not on the resolved tab, e.g. C_UOM_ID
            // when it isn't a tab field) still gets its field-level read-only logic.
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
                                  COALESCE(vr.Type, N'')          AS ValRuleType,
                                  COALESCE(vr.Code, N'')          AS ValRuleCode,
                                  -- DisplayLogic lives on AD_Field, not AD_Column: pick a
                                  -- representative non-empty expression from any active
                                  -- C_InvoiceLine field for this column, so a curated modal
                                  -- field that isn't on the resolved window tab still has its
                                  -- show/hide logic applied (uniform per column in practice).
                                  COALESCE((SELECT MAX(f2.DisplayLogic)
                                            FROM AD_Field f2
                                            INNER JOIN AD_Tab t2 ON (f2.AD_Tab_ID = t2.AD_Tab_ID)
                                            INNER JOIN AD_Table tt2 ON (t2.AD_Table_ID = tt2.AD_Table_ID)
                                            WHERE f2.AD_Column_ID = c.AD_Column_ID
                                              AND f2.IsActive = 'Y'
                                              AND tt2.TableName = 'C_InvoiceLine'
                                              AND f2.DisplayLogic IS NOT NULL
                                              AND f2.DisplayLogic <> ''), N'') AS DisplayLogic
                           FROM AD_Column c
                           INNER JOIN AD_Table t ON (c.AD_Table_ID = t.AD_Table_ID)
                           LEFT JOIN AD_Val_Rule vr ON (c.AD_Val_Rule_ID = vr.AD_Val_Rule_ID
                                AND vr.IsActive = 'Y')
                           WHERE t.TableName = 'C_InvoiceLine'
                             AND c.IsActive = 'Y'";

            DataSet ds = DB.ExecuteDataset(sql);
            if (ds == null || ds.Tables.Count == 0) return;
            foreach (DataRow r in ds.Tables[0].Rows)
            {
                if (have.Contains(Util.GetValueOfString(r["ColumnName"]))) continue;
                data.Columns.Add(MapColumnMeta(r, false));
            }
        }

        /// <summary>Maps a column-meta row (field-aware when <paramref name="fromField"/>).</summary>
        /// <param name="r">data row</param>
        /// <param name="fromField">true when read from AD_Field (has display / read-only / seq)</param>
        /// <returns>column meta</returns>
        private ColumnMeta MapColumnMeta(DataRow r, bool fromField)
        {
            ColumnMeta m = new ColumnMeta
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
                IsDisplayed = true
            };
            // DisplayLogic is selected by BOTH the tab-field query (AD_Field.DisplayLogic) and
            // the all-columns merge (representative C_InvoiceLine field via subquery), so read
            // it whenever the row carries the column - the curated modal applies it either way.
            if (r.Table.Columns.Contains("DisplayLogic"))
                m.DisplayLogic = Util.GetValueOfString(r["DisplayLogic"]);
            if (fromField)
            {
                m.IsDisplayed = Util.GetValueOfString(r["IsDisplayed"]) == "Y";
                m.IsReadOnly = Util.GetValueOfString(r["IsReadOnly"]) == "Y";
                m.SeqNo = Util.GetValueOfInt(r["SeqNo"]);
                // AD_Field image (window-driven). Only the field query selects these
                // columns; the all-columns merge (fromField == false) does not.
                // FontName (icon font) takes FIRST priority - when set, the bitmap thumbnail
                // is not resolved (the client renders the font glyph instead).
                m.AD_Image_ID = Util.GetValueOfInt(r["AD_Image_ID"]);
                m.IconFont = Util.GetValueOfString(r["FontName"]);
                if (string.IsNullOrEmpty(m.IconFont))
                    m.ImageUrl = FieldImageUrl(m.AD_Image_ID, Util.GetValueOfString(r["ImageExtension"]));
            }
            return m;
        }

        /// <summary>
        /// Resolves an AD_Field's AD_Image_ID to a servable thumbnail URL, using the same
        /// convention as VASAttachUserToBP / VAS_TimeSheetInvoice
        /// (Images/Thumb46x46/&lt;id&gt;&lt;ext&gt;). Returns "" when the id is unset or the
        /// thumbnail file is missing, so the client can simply skip rendering an image.
        /// </summary>
        /// <param name="adImageId">AD_Field.AD_Image_ID</param>
        /// <param name="imageExtension">AD_Image.ImageExtension (e.g. ".png")</param>
        /// <returns>relative image URL or ""</returns>
        private string FieldImageUrl(int adImageId, string imageExtension)
        {
            if (adImageId <= 0 || string.IsNullOrEmpty(imageExtension)) return "";
            try
            {
                string file = GlobalVariable.ImagePath + "\\Thumb46x46\\" + adImageId + imageExtension;
                if (System.IO.File.Exists(file))
                    return "Images/Thumb46x46/" + adImageId + imageExtension;
            }
            catch { /* path/IO issue -> no image */ }
            return "";
        }

        /// <summary>
        /// Loads the editable-cell dropdown catalogs: active units of measure and
        /// active taxes (with rate, so the client can preview line amounts without
        /// a round trip). MRole is applied to C_Tax (a client-scoped physical
        /// table); C_UOM is system-shared so only IsActive is filtered.
        /// </summary>
        /// <param name="ctx">session context</param>
        /// <param name="data">view model being populated</param>
        private void LoadCatalogs(Ctx ctx, CreateInvoiceLinePanelData data)
        {
            // Panel-load lists use invoice-header context only (no row yet).
            data.UomList = LoadUomList(ctx, data.C_Invoice_ID, null);
            data.TaxList = LoadTaxList(ctx, data.C_Invoice_ID, null);
        }

        /// <summary>
        /// Builds the unit-of-measure dropdown list, enforcing the C_UOM_ID column's
        /// AD_Val_Rule. <paramref name="rowVars"/> carries the current line's column
        /// values so a context-sensitive rule (e.g. UOM valid for @M_Product_ID@)
        /// re-filters per row; pass null for the panel-load (header-only) list.
        /// C_UOM is system-shared, so only IsActive + the val rule apply (no MRole).
        /// </summary>
        /// <param name="ctx">session context</param>
        /// <param name="C_Invoice_ID">parent invoice (header context source)</param>
        /// <param name="rowVars">current line column values as SQL literals (may be null)</param>
        /// <returns>filtered units of measure</returns>
        private List<UomItem> LoadUomList(Ctx ctx, int C_Invoice_ID, Dictionary<string, string> rowVars)
        {
            List<UomItem> list = new List<UomItem>();
            string uomSql = @"SELECT u.C_UOM_ID, COALESCE(u.UOMSymbol, u.Name) AS UOMName
                              FROM C_UOM u
                              WHERE u.IsActive = 'Y'";
            string uomPred = GetValRulePredicate(ctx, "C_UOM_ID", "C_UOM", "u", C_Invoice_ID, rowVars);
            if (uomPred.Length > 0) uomSql += " AND (" + uomPred + ")";
            uomSql += " ORDER BY COALESCE(u.UOMSymbol, u.Name)";
            uomSql = MRole.GetDefault(ctx).AddAccessSQL(
                uomSql, "u", MRole.SQL_FULLYQUALIFIED, MRole.SQL_RO);
            DataSet uds = DB.ExecuteDataset(uomSql);
            if (uds != null && uds.Tables.Count > 0)
            {
                foreach (DataRow r in uds.Tables[0].Rows)
                {
                    list.Add(new UomItem
                    {
                        C_UOM_ID = Util.GetValueOfInt(r["C_UOM_ID"]),
                        Name = Util.GetValueOfString(r["UOMName"])
                    });
                }
            }
            return list;
        }

        /// <summary>
        /// Builds the tax dropdown list (rate included for the client amount preview),
        /// enforcing the C_Tax_ID column's AD_Val_Rule. <paramref name="rowVars"/>
        /// carries the current line's column values for a per-row context rule;
        /// pass null for the panel-load (header-only) list. MRole is applied to the
        /// physical C_Tax table.
        /// </summary>
        /// <param name="ctx">session context</param>
        /// <param name="C_Invoice_ID">parent invoice (header context source)</param>
        /// <param name="rowVars">current line column values as SQL literals (may be null)</param>
        /// <returns>filtered taxes</returns>
        private List<TaxItem> LoadTaxList(Ctx ctx, int C_Invoice_ID, Dictionary<string, string> rowVars)
        {
            List<TaxItem> list = new List<TaxItem>();
            string taxSql = @"SELECT t.C_Tax_ID, t.Name, t.Rate
                              FROM C_Tax t
                              WHERE t.IsActive = 'Y'
                                AND COALESCE(t.IsSummary, 'N') = 'N'
                                AND t.AD_Client_ID IN (0, " + ctx.GetAD_Client_ID() + ")";
            string taxPred = GetValRulePredicate(ctx, "C_Tax_ID", "C_Tax", "t", C_Invoice_ID, rowVars);
            if (taxPred.Length > 0) taxSql += " AND (" + taxPred + ")";
            taxSql += " ORDER BY t.Rate, t.Name";
            taxSql = MRole.GetDefault(ctx).AddAccessSQL(
                taxSql, "t", MRole.SQL_FULLYQUALIFIED, MRole.SQL_RO);
            DataSet tds = DB.ExecuteDataset(taxSql);
            if (tds != null && tds.Tables.Count > 0)
            {
                foreach (DataRow r in tds.Tables[0].Rows)
                {
                    list.Add(new TaxItem
                    {
                        C_Tax_ID = Util.GetValueOfInt(r["C_Tax_ID"]),
                        Name = Util.GetValueOfString(r["Name"]),
                        Rate = Util.GetValueOfDecimal(r["Rate"])
                    });
                }
            }
            return list;
        }

        /// <summary>
        /// Re-fetches the context-sensitive lookup lists (UOM + tax) for ONE invoice
        /// line, honouring each column's AD_Val_Rule against the line's CURRENT values
        /// (selected product / charge, etc.) plus the invoice-header and session
        /// context. The panel calls this when a row's UOM / tax control is opened (or
        /// the product / charge changes) so each line's dropdown only offers values
        /// valid in that line's context. Returns empty lists when the role has no
        /// access to the parent invoice.
        /// </summary>
        /// <param name="ctx">session context</param>
        /// <param name="req">lookup request (parent invoice + the line's column values)</param>
        /// <returns>per-row filtered UOM + tax lists</returns>
        public LookupData GetLookupData(Ctx ctx, LookupRequest req)
        {
            LookupData data = new LookupData();
            if (req == null || req.C_Invoice_ID <= 0) return data;

            // Verify the role can see the parent invoice before exposing any list.
            CreateInvoiceLinePanelData parent = new CreateInvoiceLinePanelData();
            LoadParentContext(ctx, req.C_Invoice_ID, parent);
            if (parent.C_Invoice_ID <= 0) return data;

            data.C_Invoice_ID = req.C_Invoice_ID;
            Dictionary<string, string> rowVars = BuildRowVars(req.RowValues);

            // One round trip warms both controls; the client caches them per line.
            data.UomList = LoadUomList(ctx, req.C_Invoice_ID, rowVars);
            data.TaxList = LoadTaxList(ctx, req.C_Invoice_ID, rowVars);
            return data;
        }

        /// <summary>
        /// Generic foreign-key lookup for a DYNAMIC C_InvoiceLine field rendered in the
        /// "more" popover (reference Table 18 / TableDir 19 / Search 30). Resolves the
        /// column's lookup table, key and display (identifier) columns, then returns the
        /// matching id + label rows - filtered by the typed keyword, the column's
        /// AD_Val_Rule (in the line's context) and the role's data access, paged like the
        /// catalog. When <see cref="RefLookupRequest.Id"/> &gt; 0 it resolves that single
        /// row's label (used to show an existing value's caption).
        /// </summary>
        /// <param name="ctx">session context</param>
        /// <param name="req">lookup request (column + keyword / id + line context)</param>
        /// <returns>matching reference rows (id + display)</returns>
        public List<RefItem> GetRefLookup(Ctx ctx, RefLookupRequest req)
        {
            List<RefItem> items = new List<RefItem>();
            if (req == null || req.C_Invoice_ID <= 0 || string.IsNullOrEmpty(req.ColumnName)) return items;

            CreateInvoiceLinePanelData parent = new CreateInvoiceLinePanelData();
            LoadParentContext(ctx, req.C_Invoice_ID, parent);
            if (parent.C_Invoice_ID <= 0) return items;

            RefLookupDef def = ResolveRefLookup(req.ColumnName);
            if (def == null) return items;   // not an FK reference we can resolve

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
                // Single-row label resolution for an existing value.
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
                // Column AD_Val_Rule (resolved against the line + invoice + session context).
                Dictionary<string, string> rowVars = BuildRowVars(req.RowValues);
                string pred = GetValRulePredicate(ctx, req.ColumnName, def.TableName, alias, req.C_Invoice_ID, rowVars);
                if (pred.Length > 0) sql.Append(" AND (").Append(pred).Append(")");
            }

            string secured = MRole.GetDefault(ctx).AddAccessSQL(
                sql.ToString(), alias, MRole.SQL_FULLYQUALIFIED, MRole.SQL_RO);
            if (req.Id <= 0) secured += " ORDER BY " + dispExpr + PagingSuffix(pageSize, offset);

            DataSet ds = DB.ExecuteDataset(secured, ps.ToArray(), null);
            if (ds == null || ds.Tables.Count == 0) return items;
            foreach (DataRow r in ds.Tables[0].Rows)
                items.Add(new RefItem { Id = Util.GetValueOfInt(r["Id"]), Name = Util.GetValueOfString(r["Name"]) });
            return items;
        }

        /// <summary>
        /// Non-standard TableDir FK columns whose lookup table / key is NOT the column
        /// name minus "_ID" (columnName -> { table, keyColumn }). Add entries here when a
        /// curated FK field doesn't follow the convention.
        /// </summary>
        private static readonly Dictionary<string, string[]> TableDirOverrides =
            new Dictionary<string, string[]>(StringComparer.OrdinalIgnoreCase)
            {
                { "AD_OrgTrx_ID", new string[] { "AD_Org", "AD_Org_ID" } }
            };

        private Dictionary<string, RefLookupDef> _refDefByColumn;

        /// <summary>
        /// Resolves a C_InvoiceLine FK column to its lookup table, key column and
        /// display (identifier) expression. TableDir (19) maps the table from the column
        /// name (strip "_ID"); Table (18) / Search (30) read AD_Ref_Table for the table,
        /// key and display columns. Returns null when the column is not such a reference.
        /// Per-instance cached.
        /// </summary>
        /// <param name="columnName">C_InvoiceLine column</param>
        /// <returns>lookup definition or null</returns>
        private RefLookupDef ResolveRefLookup(string columnName)
        {
            if (_refDefByColumn == null) _refDefByColumn = new Dictionary<string, RefLookupDef>(StringComparer.OrdinalIgnoreCase);
            RefLookupDef cached;
            if (_refDefByColumn.TryGetValue(columnName, out cached)) return cached;

            RefLookupDef def = null;
            DataSet rs = DB.ExecuteDataset(
                @"SELECT c.AD_Reference_ID, COALESCE(c.AD_Reference_Value_ID, 0) AS AD_Reference_Value_ID
                  FROM AD_Column c INNER JOIN AD_Table t ON (c.AD_Table_ID = t.AD_Table_ID)
                  WHERE t.TableName = 'C_InvoiceLine' AND c.ColumnName = @c AND c.IsActive = 'Y'",
                new SqlParameter[] { new SqlParameter("@c", columnName) }, null);
            if (rs != null && rs.Tables.Count > 0 && rs.Tables[0].Rows.Count > 0)
            {
                int refId = Util.GetValueOfInt(rs.Tables[0].Rows[0]["AD_Reference_ID"]);
                int refValId = Util.GetValueOfInt(rs.Tables[0].Rows[0]["AD_Reference_Value_ID"]);

                string table = null, key = null, display = null;
                if (refId == 19 || (refId == 18 && refValId <= 0))
                {
                    // TableDir: table is the column name without the "_ID" suffix, except
                    // for a few non-standard FKs that point at a different table / key.
                    string[] ov;
                    if (TableDirOverrides.TryGetValue(columnName, out ov))
                    {
                        table = ov[0];
                        key = ov[1];
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
                    def = new RefLookupDef
                    {
                        TableName = table,
                        KeyColumn = key,
                        DisplayExpr = display,
                        HasIsActive = true, /*ColumnExists(table, "IsActive")*/
                        HasClientId = true /*ColumnExists(table, "AD_Client_ID")*/
                    };
                }
            }
            _refDefByColumn[columnName] = def;
            return def;
        }

        /// <summary>
        /// Builds the display expression for a table from its identifier columns
        /// (AD_Column.IsIdentifier in sequence). Falls back to Name, then Value, then the
        /// key. Multiple identifiers are concatenated with the DB-appropriate operator.
        /// {a} is the alias placeholder substituted by the caller.
        /// </summary>
        /// <param name="table">lookup table name</param>
        /// <returns>display expression with the {a} alias placeholder</returns>
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

            // Concatenate identifiers. PostgreSQL / Oracle use ||; the else path (MySQL)
            // uses CONCAT.
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
        /// <param name="table">table name</param>
        /// <param name="column">column name</param>
        /// <returns>true when the column exists</returns>
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
            public string DisplayExpr;   // contains the {a} alias placeholder
            public bool HasIsActive;
            public bool HasClientId;
        }

        /// <summary>
        /// Loads the parent invoice header values used as callout context
        /// (price list, partner, currency, dates, tax-inclusive + doc-status
        /// flags) and the base-currency precision / symbol.
        /// </summary>
        /// <param name="ctx">session context</param>
        /// <param name="C_Invoice_ID">parent invoice</param>
        /// <param name="data">view model being populated</param>
        private void LoadParentContext(Ctx ctx, int C_Invoice_ID, CreateInvoiceLinePanelData data)
        {
            string sql = @"SELECT
                              i.C_Invoice_ID,
                              i.AD_Client_ID,
                              i.AD_Org_ID,
                              i.C_BPartner_ID,
                              i.C_BPartner_Location_ID,
                              i.M_PriceList_ID,
                              i.C_Currency_ID,
                              i.DateInvoiced,
                              i.DateAcct,
                              i.IsSOTrx,
                              COALESCE(i.IsTaxIncluded, 'N') AS IsTaxIncluded,
                              i.DocStatus,
                              COALESCE(i.Processed, 'N') AS Processed,
                              cur.StdPrecision     AS StdPrecision,
                              cur.CurSymbol        AS CurrencySymbol,
                              cur.ISO_Code         AS CurrencyISOCode
                           FROM C_Invoice i
                           INNER JOIN C_Currency cur ON (i.C_Currency_ID = cur.C_Currency_ID)
                           WHERE i.C_Invoice_ID = @C_Invoice_ID
                             AND i.IsActive = 'Y'";

            sql = MRole.GetDefault(ctx).AddAccessSQL(
                sql, "i", MRole.SQL_FULLYQUALIFIED, MRole.SQL_RO);

            DataSet ds = DB.ExecuteDataset(sql,
                new SqlParameter[] { new SqlParameter("@C_Invoice_ID", C_Invoice_ID) }, null);
            if (ds == null || ds.Tables.Count == 0 || ds.Tables[0].Rows.Count == 0)
                return;

            DataRow r = ds.Tables[0].Rows[0];
            data.C_Invoice_ID = Util.GetValueOfInt(r["C_Invoice_ID"]);
            data.AD_Client_ID = Util.GetValueOfInt(r["AD_Client_ID"]);
            data.AD_Org_ID = Util.GetValueOfInt(r["AD_Org_ID"]);
            data.C_BPartner_ID = Util.GetValueOfInt(r["C_BPartner_ID"]);
            data.C_BPartner_Location_ID = Util.GetValueOfInt(r["C_BPartner_Location_ID"]);
            data.M_PriceList_ID = Util.GetValueOfInt(r["M_PriceList_ID"]);
            data.C_Currency_ID = Util.GetValueOfInt(r["C_Currency_ID"]);
            data.DateInvoiced = Util.GetValueOfDateTime(r["DateInvoiced"]);
            data.DateAcct = Util.GetValueOfDateTime(r["DateAcct"]);
            data.IsSOTrx = Util.GetValueOfString(r["IsSOTrx"]) == "Y";
            data.IsTaxIncluded = Util.GetValueOfString(r["IsTaxIncluded"]) == "Y";
            data.DocStatus = Util.GetValueOfString(r["DocStatus"]);
            data.Processed = Util.GetValueOfString(r["Processed"]) == "Y";
            data.StdPrecision = Util.GetValueOfInt(r["StdPrecision"]);
            data.CurSymbol = Util.GetValueOfString(r["CurrencySymbol"]);
            data.CurISO = Util.GetValueOfString(r["CurrencyISOCode"]);
            // A completed / closed / processed invoice cannot take new lines.
            data.IsEditable = !data.Processed
                && data.DocStatus != "CO" && data.DocStatus != "CL"
                && data.DocStatus != "VO" && data.DocStatus != "RE";
        }

        /// <summary>
        /// Loads the invoice lines already saved against the parent invoice with
        /// their product / charge / UOM / tax display labels.
        /// </summary>
        /// <param name="ctx">session context</param>
        /// <param name="C_Invoice_ID">parent invoice</param>
        /// <param name="AD_Tab_IDs">resolved C_InvoiceLine tabs whose distinct AD_Field
        ///   columns are projected (empty -> fall back to every active column)</param>
        /// <returns>saved invoice lines</returns>
        private List<InvoiceLineRow> LoadLines(Ctx ctx, int C_Invoice_ID, List<int> AD_Tab_IDs, int page, out int total)
        {
            List<InvoiceLineRow> rows = new List<InvoiceLineRow>();

            // Total saved-line count (same filter + MRole as the paged query) so the client
            // can render "X-Y of N" and prev/next.
            string countSql = "SELECT COUNT(*) FROM C_InvoiceLine il WHERE il.C_Invoice_ID = @C_Invoice_ID AND il.IsActive = 'Y'";
            countSql = MRole.GetDefault(ctx).AddAccessSQL(countSql, "il", MRole.SQL_FULLYQUALIFIED, MRole.SQL_RO);
            total = Util.GetValueOfInt(DB.ExecuteScalar(countSql,
                new SqlParameter[] { new SqlParameter("@C_Invoice_ID", C_Invoice_ID) }, null));

            // Project the C_InvoiceLine columns configured across the window's tabs
            // (AD_Field -> AD_Column, distinct). The essential grid columns are always
            // added so the typed-field reads below never miss one, and the set is
            // intersected with the live dictionary columns so only real, active columns
            // reach the SELECT (column names come from the dictionary, never user input,
            // so there is no injection surface). Falls back to every active column when
            // no tab is resolved.
            StringBuilder cols = new StringBuilder();
            foreach (string cn in GetLineProjectionColumns(AD_Tab_IDs))
                cols.Append("il.").Append(cn).Append(", ");
            if (cols.Length == 0)
                cols.Append("il.C_InvoiceLine_ID, il.Line, il.M_Product_ID, il.C_Charge_ID, il.QtyEntered, il.C_UOM_ID, il.PriceEntered, il.C_Tax_ID, il.TaxAmt, il.LineNetAmt, il.LineTotalAmt, il.M_AttributeSetInstance_ID, il.Description, ");

            string sql = "SELECT " + cols.ToString() +
                @"COALESCE(p.Name, N'')        AS VASCILDISP_ProductName,
                  COALESCE(ch.Name, N'')       AS VASCILDISP_ChargeName,
                  COALESCE(uom.UOMSymbol, uom.Name, N'') AS VASCILDISP_UOMName,
                  COALESCE(t.Name, N'')        AS VASCILDISP_TaxName,
                  COALESCE(asi.Description, N'') AS VASCILDISP_AttrName,
                  COALESCE(p.M_AttributeSet_ID, 0) AS VASCILDISP_HasAttrSet,
                  COALESCE(p.ProductType, N'') AS VASCILDISP_ProductType
               FROM C_InvoiceLine il
               LEFT JOIN M_Product p ON (il.M_Product_ID = p.M_Product_ID)
               LEFT JOIN C_Charge ch ON (il.C_Charge_ID = ch.C_Charge_ID)
               LEFT JOIN C_UOM uom ON (il.C_UOM_ID = uom.C_UOM_ID)
               LEFT JOIN C_Tax t ON (il.C_Tax_ID = t.C_Tax_ID)
               LEFT JOIN M_AttributeSetInstance asi ON (il.M_AttributeSetInstance_ID = asi.M_AttributeSetInstance_ID)
               WHERE il.C_Invoice_ID = @C_Invoice_ID
                 AND il.IsActive = 'Y'";

            sql = MRole.GetDefault(ctx).AddAccessSQL(
                sql, "il", MRole.SQL_FULLYQUALIFIED, MRole.SQL_RO);
            if (page < 0) page = 0;
            sql += " ORDER BY il.Line" + PagingSuffix(LINE_PAGE_SIZE, page * LINE_PAGE_SIZE);

            DataSet ds = DB.ExecuteDataset(sql,
                new SqlParameter[] { new SqlParameter("@C_Invoice_ID", C_Invoice_ID) }, null);
            if (ds == null || ds.Tables.Count == 0) return rows;

            DataTable dt = ds.Tables[0];
            foreach (DataRow r in dt.Rows)
            {
                InvoiceLineRow row = new InvoiceLineRow();
                // Full column bag (every C_InvoiceLine column) - skip only the
                // panel's own display aliases (VASCILDISP_*).
                foreach (DataColumn dc in dt.Columns)
                {
                    if (dc.ColumnName.StartsWith("VASCILDISP_")) continue;
                    row.Values[dc.ColumnName] = (r[dc] == DBNull.Value) ? null : r[dc];
                }
                // Typed fields used by the grid renderer.
                row.C_InvoiceLine_ID = Util.GetValueOfInt(r["C_InvoiceLine_ID"]);
                row.Line = Util.GetValueOfInt(r["Line"]);
                row.M_Product_ID = Util.GetValueOfInt(r["M_Product_ID"]);
                row.ProductName = Util.GetValueOfString(r["VASCILDISP_ProductName"]);
                row.C_Charge_ID = Util.GetValueOfInt(r["C_Charge_ID"]);
                row.ChargeName = Util.GetValueOfString(r["VASCILDISP_ChargeName"]);
                row.Description = Util.GetValueOfString(r["Description"]);
                row.QtyEntered = Util.GetValueOfDecimal(r["QtyEntered"]);
                row.C_UOM_ID = Util.GetValueOfInt(r["C_UOM_ID"]);
                row.UOMName = Util.GetValueOfString(r["VASCILDISP_UOMName"]);
                row.PriceEntered = Util.GetValueOfDecimal(r["PriceEntered"]);
                row.C_Tax_ID = Util.GetValueOfInt(r["C_Tax_ID"]);
                row.TaxName = Util.GetValueOfString(r["VASCILDISP_TaxName"]);
                row.TaxAmt = Util.GetValueOfDecimal(r["TaxAmt"]);
                row.LineNetAmt = Util.GetValueOfDecimal(r["LineNetAmt"]);
                row.LineTotalAmt = Util.GetValueOfDecimal(r["LineTotalAmt"]);
                row.M_AttributeSetInstance_ID = Util.GetValueOfInt(r["M_AttributeSetInstance_ID"]);
                row.AttrName = Util.GetValueOfString(r["VASCILDISP_AttrName"]);
                row.HasAttributeSet = Util.GetValueOfInt(r["VASCILDISP_HasAttrSet"]) > 0;
                row.ProductType = Util.GetValueOfString(r["VASCILDISP_ProductType"]);
                rows.Add(row);
            }
            return rows;
        }

        /// <summary>
        /// Sums the saved-line amounts across the WHOLE invoice (every active line,
        /// same MRole filter as the pager count) so the totals row can show the invoice
        /// grand total, not just the current page. TCS (VA106) is summed only when the
        /// column is installed.
        /// </summary>
        private void LoadGrandTotals(Ctx ctx, int C_Invoice_ID, out decimal net, out decimal tax, out decimal tcs)
        {
            net = 0; tax = 0; tcs = 0;
            string tcsExpr = ColumnExists("C_InvoiceLine", "VA106_TCSAmount")
                ? "COALESCE(SUM(il.VA106_TCSAmount), 0)" : "0";
            // Tax total includes the surcharge (totalTax = TaxAmt + SurchargeAmt) when the
            // SurchargeAmt column is installed, matching the client per-line lineTaxTotal.
            string surExpr = " + COALESCE(SUM(il.SurchargeAmt), 0)";
            // Subtotal = SUM(TaxBaseAmt) - the actual stored tax base (net) the framework writes on
            // save. This is correct whether the price is tax-inclusive or exclusive, so no further
            // inclusive adjustment is needed (see ComputeOtherPageTotals).
            string sql = "SELECT COALESCE(SUM(il.TaxBaseAmt), 0) AS Net, COALESCE(SUM(il.TaxAmt), 0)" + surExpr + " AS Tax, "
                + tcsExpr + " AS Tcs"
                + " FROM C_InvoiceLine il WHERE il.C_Invoice_ID = @C_Invoice_ID AND il.IsActive = 'Y'";
            sql = MRole.GetDefault(ctx).AddAccessSQL(sql, "il", MRole.SQL_FULLYQUALIFIED, MRole.SQL_RO);
            DataSet ds = DB.ExecuteDataset(sql,
                new SqlParameter[] { new SqlParameter("@C_Invoice_ID", C_Invoice_ID) }, null);
            if (ds != null && ds.Tables.Count > 0 && ds.Tables[0].Rows.Count > 0)
            {
                DataRow r = ds.Tables[0].Rows[0];
                net = Util.GetValueOfDecimal(r["Net"]);
                tax = Util.GetValueOfDecimal(r["Tax"]);
                tcs = Util.GetValueOfDecimal(r["Tcs"]);
            }
        }

        /// <summary>
        /// Computes the totals of every saved line NOT on the current page (invoice
        /// grand total minus the loaded page rows). The client adds its live per-line
        /// sum of the current page to these so the totals row reflects the WHOLE invoice
        /// yet still updates instantly while a page is being edited.
        /// </summary>
        private void ComputeOtherPageTotals(Ctx ctx, int C_Invoice_ID, List<InvoiceLineRow> pageRows,
            bool taxIncluded, out decimal otherNet, out decimal otherTax, out decimal otherTcs)
        {
            decimal gNet, gTax, gTcs;
            LoadGrandTotals(ctx, C_Invoice_ID, out gNet, out gTax, out gTcs);
            decimal pNet = 0, pTax = 0, pTcs = 0;
            if (pageRows != null)
                foreach (InvoiceLineRow row in pageRows)
                {
                    pNet += RowDecimal(row, "TaxBaseAmt");                  // Subtotal base = stored TaxBaseAmt
                    pTax += row.TaxAmt + RowDecimal(row, "SurchargeAmt");   // totalTax = TaxAmt + SurchargeAmt
                    pTcs += RowTcs(row);
                }
            otherNet = gNet - pNet;
            otherTax = gTax - pTax;
            otherTcs = gTcs - pTcs;
            // Subtotal now comes from the stored TaxBaseAmt (already the net base whether the price
            // is tax-inclusive or exclusive), so no LineNetAmt-minus-TaxAmt inclusive adjustment is
            // needed here. The taxIncluded flag is retained on the signature for the call sites.
        }

        /// <summary>Per-line TCS amount (VA106_TCSAmount) read case-insensitively; 0 when
        /// the column isn't installed or set.</summary>
        private static decimal RowTcs(InvoiceLineRow row)
        {
            return RowDecimal(row, "VA106_TCSAmount");
        }

        /// <summary>Reads a decimal column from a line's value bag case-insensitively (the
        /// keys are DB-cased: PG lowercases, Oracle uppercases); 0 when absent / not set.</summary>
        private static decimal RowDecimal(InvoiceLineRow row, string columnName)
        {
            if (row == null || row.Values == null) return 0;
            foreach (KeyValuePair<string, object> kv in row.Values)
                if (string.Equals(kv.Key, columnName, StringComparison.OrdinalIgnoreCase))
                    return Util.GetValueOfDecimal(kv.Value);
            return 0;
        }

        private List<string> _ilColumns;

        /// <summary>
        /// Returns (and per-instance caches) the active C_InvoiceLine column names
        /// from the dictionary - used to project the full line VO and to gate the
        /// generic column persistence in SaveLines.
        /// </summary>
        /// <returns>active column names</returns>
        private List<string> GetInvoiceLineColumns()
        {
            if (_ilColumns != null) return _ilColumns;
            _ilColumns = new List<string>();
            DataSet ds = DB.ExecuteDataset(
                @"SELECT c.ColumnName
                  FROM AD_Column c
                  INNER JOIN AD_Table t ON (c.AD_Table_ID = t.AD_Table_ID)
                  WHERE t.TableName = 'C_InvoiceLine' AND c.IsActive = 'Y' AND c.ColumnSQL IS NULL ");
            if (ds != null && ds.Tables.Count > 0)
                foreach (DataRow r in ds.Tables[0].Rows)
                    _ilColumns.Add(Util.GetValueOfString(r["ColumnName"]));
            return _ilColumns;
        }

        /// <summary>Columns the grid renderer always needs (typed fields + join keys),
        /// projected even when the tab's field set omits them so the reads in LoadLines
        /// never miss a column.</summary>
        private static readonly string[] ESSENTIAL_LINE_COLUMNS = new string[]
        {
            "C_InvoiceLine_ID", "C_Invoice_ID", "Line", "M_Product_ID", "C_Charge_ID",
            "QtyEntered", "C_UOM_ID", "PriceEntered", "C_Tax_ID", "TaxAmt",
            "LineNetAmt", "LineTotalAmt", "M_AttributeSetInstance_ID", "Description"
        };

        /// <summary>
        /// Builds the C_InvoiceLine column projection for LoadLines: the DISTINCT
        /// columns configured across the window's tabs (AD_Field -> AD_Column) unioned
        /// with the essential grid columns, intersected with the live active dictionary
        /// columns so only real columns are selected. Falls back to every active column
        /// when no tab is resolved (or none of the tabs have fields).
        /// </summary>
        /// <param name="AD_Tab_IDs">resolved C_InvoiceLine tabs (empty = none)</param>
        /// <returns>distinct column names to project</returns>
        private List<string> GetLineProjectionColumns(List<int> AD_Tab_IDs)
        {
            // Project EVERY active C_InvoiceLine column so the curated "Additional Info"
            // fields (which may not be on the window tab) populate from a saved line.
            return GetInvoiceLineColumns();
        }

        private Dictionary<int, List<string>> _tabLineColumns;

        /// <summary>
        /// Returns (and per-instance caches) the C_InvoiceLine column names mapped to
        /// the given tab's active fields (AD_Field -> AD_Column), in field sequence.
        /// </summary>
        /// <param name="AD_Tab_ID">invoice-line tab</param>
        /// <returns>field column names</returns>
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

        #region Product / Charge catalog search (keyword + scroll paging)

        /// <summary>
        /// Paged Product / Charge catalog search for the line picker. Filters by
        /// the typed keyword (Value / Name / UPC for products, Value / Name for
        /// charges), returns CATALOG_PAGE_SIZE rows from <paramref name="offset"/>
        /// and lets the client request the next page on scroll. MRole is applied
        /// to each physical base table (M_Product / C_Charge) inside its own
        /// branch - never to the combined / outer query.
        /// </summary>
        /// <param name="ctx">session context</param>
        /// <param name="C_Invoice_ID">parent invoice (for client / SOTrx scope)</param>
        /// <param name="query">keyword typed by the user (may be empty)</param>
        /// <param name="pageSize">rows per page (defaults / caps to 50)</param>
        /// <param name="offset">rows already loaded (scroll position)</param>
        /// <returns>matching product + charge rows</returns>
        public List<CatalogItem> SearchProductsCharges(Ctx ctx, int C_Invoice_ID,
            string query, int pageSize, int offset, Dictionary<string, object> rowValues = null)
        {
            List<CatalogItem> items = new List<CatalogItem>();
            if (C_Invoice_ID <= 0) return items;

            // Validate / sanitise paging inputs (never trust the client).
            if (pageSize <= 0 || pageSize > CATALOG_PAGE_SIZE) pageSize = CATALOG_PAGE_SIZE;
            if (offset < 0) offset = 0;
            if (offset > 1000000) offset = 1000000;

            // Current line context so a per-row M_Product / C_Charge val rule applies.
            Dictionary<string, string> rowVars = BuildRowVars(rowValues);

            string term = (query ?? "").Trim();
            string like = "%" + term.ToLower() + "%";

            // --- Product branch (MRole on M_Product p) ---
            string prodSql = @"SELECT p.M_Product_ID AS RecordId, 'P' AS Kind,
                                      p.Value AS SearchKey, p.Name AS DisplayName,
                                      COALESCE(p.Description, N'') AS Description,
                                      p.M_AttributeSet_ID AS AttributeSetId,
                                      COALESCE(p.ProductType, N'') AS ProductType
                               FROM M_Product p
                               WHERE p.IsActive = 'Y'
                                 AND p.IsSummary = 'N'
                                 AND p.AD_Client_ID = " + ctx.GetAD_Client_ID() + @"
                                 AND (LOWER(p.Value) LIKE @kwP
                                   OR LOWER(p.Name) LIKE @kwP
                                   OR LOWER(COALESCE(p.UPC, N'')) LIKE @kwP)";
            // Enforce the M_Product_ID column's AD_Val_Rule (if any) in line context.
            string prodPred = GetValRulePredicate(ctx, "M_Product_ID", "M_Product", "p", C_Invoice_ID, rowVars);
            if (prodPred.Length > 0) prodSql += " AND (" + prodPred + ")";
            prodSql = MRole.GetDefault(ctx).AddAccessSQL(
                prodSql, "p", MRole.SQL_FULLYQUALIFIED, MRole.SQL_RO);

            // --- Charge branch (MRole on C_Charge ch) ---
            string chargeSql = @"SELECT ch.C_Charge_ID AS RecordId, 'C' AS Kind,
                                        COALESCE(ch.Name, N'') AS SearchKey, ch.Name AS DisplayName,
                                        COALESCE(ch.Description, N'') AS Description,
                                        0 AS AttributeSetId,
                                        N'' AS ProductType
                                 FROM C_Charge ch
                                 WHERE ch.IsActive = 'Y'
                                   AND ch.AD_Client_ID = " + ctx.GetAD_Client_ID() + @"
                                   AND (LOWER(ch.Name) LIKE @kwC
                                     OR LOWER(COALESCE(ch.Description, N'')) LIKE @kwC)";
            // Enforce the C_Charge_ID column's AD_Val_Rule (if any) in line context.
            string chargePred = GetValRulePredicate(ctx, "C_Charge_ID", "C_Charge", "ch", C_Invoice_ID, rowVars);
            if (chargePred.Length > 0) chargeSql += " AND (" + chargePred + ")";
            chargeSql = MRole.GetDefault(ctx).AddAccessSQL(
                chargeSql, "ch", MRole.SQL_FULLYQUALIFIED, MRole.SQL_RO);

            // Combine both already-secured branches; paging + ordering on the outer
            // derived table only (no MRole on the combined set - it has no single
            // physical table to resolve through AD_Table metadata).
            string combined = "SELECT x.RecordId, x.Kind, x.SearchKey, x.DisplayName, x.Description, x.AttributeSetId, x.ProductType"
                + " FROM ((" + prodSql + ") UNION ALL (" + chargeSql + ")) x"
                + " ORDER BY x.Kind, x.DisplayName" + PagingSuffix(pageSize, offset);

            DataSet ds = DB.ExecuteDataset(combined, new SqlParameter[] {
                new SqlParameter("@kwP", like),
                new SqlParameter("@kwC", like)
            }, null);
            if (ds == null || ds.Tables.Count == 0) return items;

            foreach (DataRow r in ds.Tables[0].Rows)
            {
                CatalogItem it = new CatalogItem();
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

        /// <summary>
        /// Looks up a single product / charge by a scanned barcode (UPC, SKU /
        /// Value or charge name). Used by the Quick-add-by-barcode dialog.
        /// </summary>
        /// <param name="ctx">session context</param>
        /// <param name="C_Invoice_ID">parent invoice (client scope)</param>
        /// <param name="code">scanned code</param>
        /// <returns>matched catalog row, or an empty row (RecordId = 0) when not found</returns>
        public CatalogItem ScanLookup(Ctx ctx, int C_Invoice_ID, string code)
        {
            CatalogItem none = new CatalogItem();
            if (C_Invoice_ID <= 0 || string.IsNullOrEmpty(code)) return none;
            string key = code.Trim();

            string prodSql = @"SELECT p.M_Product_ID AS RecordId, 'P' AS Kind, p.Value AS SearchKey,
                                      p.Name AS DisplayName, COALESCE(p.Description, N'') AS Description,
                                      p.M_AttributeSet_ID AS AttributeSetId,
                                      COALESCE(p.ProductType, N'') AS ProductType
                               FROM M_Product p
                               WHERE p.IsActive = 'Y'
                                 AND p.AD_Client_ID = " + ctx.GetAD_Client_ID() + @"
                                 AND (UPPER(p.UPC) = UPPER(@code) OR UPPER(p.Value) = UPPER(@code))";
            string scanProdPred = GetValRulePredicate(ctx, "M_Product_ID", "M_Product", "p", C_Invoice_ID);
            if (scanProdPred.Length > 0) prodSql += " AND (" + scanProdPred + ")";
            prodSql = MRole.GetDefault(ctx).AddAccessSQL(prodSql, "p", MRole.SQL_FULLYQUALIFIED, MRole.SQL_RO);

            DataSet ds = DB.ExecuteDataset(prodSql,
                new SqlParameter[] { new SqlParameter("@code", key) }, null);
            if (ds != null && ds.Tables.Count > 0 && ds.Tables[0].Rows.Count > 0)
            {
                DataRow r = ds.Tables[0].Rows[0];
                CatalogItem it = new CatalogItem();
                it.RecordId = Util.GetValueOfInt(r["RecordId"]);
                it.Kind = "P";
                it.SearchKey = Util.GetValueOfString(r["SearchKey"]);
                it.DisplayName = Util.GetValueOfString(r["DisplayName"]);
                it.Description = Util.GetValueOfString(r["Description"]);
                it.HasAttributeSet = Util.GetValueOfInt(r["AttributeSetId"]) > 0;
                it.ProductType = Util.GetValueOfString(r["ProductType"]);
                return it;
            }

            // Fall back to a charge name match.
            string chargeSql = @"SELECT ch.C_Charge_ID AS RecordId, 'C' AS Kind, ch.Name AS SearchKey,
                                        ch.Name AS DisplayName, COALESCE(ch.Description, N'') AS Description
                                 FROM C_Charge ch
                                 WHERE ch.IsActive = 'Y'
                                   AND ch.AD_Client_ID = " + ctx.GetAD_Client_ID() + @"
                                   AND UPPER(ch.Name) = UPPER(@code)";
            string scanChargePred = GetValRulePredicate(ctx, "C_Charge_ID", "C_Charge", "ch", C_Invoice_ID);
            if (scanChargePred.Length > 0) chargeSql += " AND (" + scanChargePred + ")";
            chargeSql = MRole.GetDefault(ctx).AddAccessSQL(chargeSql, "ch", MRole.SQL_FULLYQUALIFIED, MRole.SQL_RO);
            ds = DB.ExecuteDataset(chargeSql,
                new SqlParameter[] { new SqlParameter("@code", key) }, null);
            if (ds != null && ds.Tables.Count > 0 && ds.Tables[0].Rows.Count > 0)
            {
                DataRow r = ds.Tables[0].Rows[0];
                CatalogItem it = new CatalogItem();
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

        #region AD_Val_Rule enforcement (dynamic validation on lookups)

        private Dictionary<string, string> _valRuleByColumn;
        private Dictionary<string, string> _invVars;

        /// <summary>
        /// Returns the SQL validation code (AD_Val_Rule.Code, Type = 'S') linked to a
        /// C_InvoiceLine lookup column, or empty when none. Per-instance cached.
        /// </summary>
        /// <param name="columnName">lookup column (e.g. M_Product_ID)</param>
        /// <returns>val rule SQL code or empty</returns>
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
                  WHERE t.TableName = 'C_InvoiceLine'
                    AND c.ColumnName = @c
                    AND c.IsActive = 'Y'
                    AND vr.IsActive = 'Y'
                    AND vr.Type = 'S'",
                new SqlParameter[] { new SqlParameter("@c", columnName) }, null);
            string code = Util.GetValueOfString(o);
            _valRuleByColumn[columnName] = code;
            return code;
        }

        /// <summary>
        /// Builds a SQL predicate from a column's AD_Val_Rule for the given lookup
        /// query: remaps the dictionary table name to the query alias and substitutes
        /// @Context@ tokens from the parent invoice. Returns empty (rule skipped, with
        /// a log warning) when any @token@ cannot be resolved - so the lookup never
        /// breaks on context the panel does not carry.
        /// </summary>
        /// <param name="ctx">session context</param>
        /// <param name="columnName">lookup column whose rule is applied</param>
        /// <param name="tableName">physical lookup table (e.g. M_Product)</param>
        /// <param name="alias">alias used for the table in the query (e.g. p)</param>
        /// <param name="C_Invoice_ID">parent invoice (context source)</param>
        /// <returns>predicate (without enclosing brackets) or empty</returns>
        private string GetValRulePredicate(Ctx ctx, string columnName, string tableName, string alias, int C_Invoice_ID)
        {
            return GetValRulePredicate(ctx, columnName, tableName, alias, C_Invoice_ID, null);
        }

        /// <summary>
        /// Builds a SQL predicate from a column's AD_Val_Rule for the given lookup
        /// query: remaps the dictionary table name to the query alias and substitutes
        /// @[#]Context@ tokens, resolving each (in order) from the current line's
        /// values (<paramref name="rowVars"/>), the parent invoice header, then the
        /// session / global context (login client / org / user / role / date, ...).
        /// Returns empty (rule skipped, with a log warning) when any @token@ cannot be
        /// resolved - so the lookup never breaks on context the panel does not carry.
        /// </summary>
        /// <param name="ctx">session context</param>
        /// <param name="columnName">lookup column whose rule is applied</param>
        /// <param name="tableName">physical lookup table (e.g. M_Product)</param>
        /// <param name="alias">alias used for the table in the query (e.g. p)</param>
        /// <param name="C_Invoice_ID">parent invoice (header context source)</param>
        /// <param name="rowVars">current line column values as SQL literals (may be null)</param>
        /// <returns>predicate (without enclosing brackets) or empty</returns>
        private string GetValRulePredicate(Ctx ctx, string columnName, string tableName, string alias,
            int C_Invoice_ID, Dictionary<string, string> rowVars)
        {
            string code = GetColumnValRule(columnName);
            if (string.IsNullOrEmpty(code)) return "";

            // Remap "TableName." references to the query alias.
            string frag = Regex.Replace(code, @"\b" + Regex.Escape(tableName) + @"\.", alias + ".", RegexOptions.IgnoreCase);

            // Substitute @[#]Var@ tokens: current row -> invoice header -> session/global.
            Dictionary<string, string> invVars = GetInvoiceVars(ctx, C_Invoice_ID);
            frag = Regex.Replace(frag, @"@(#?[A-Za-z0-9_]+)@", delegate (Match m)
            {
                string token = m.Groups[1].Value;       // may keep a leading '#'
                string key = token.TrimStart('#');
                string val;
                if (rowVars != null && rowVars.TryGetValue(key, out val)) return val;
                if (invVars.TryGetValue(key, out val)) return val;
                string ctxVal = GetCtxLiteral(ctx, token);
                return ctxVal ?? m.Value;                // leave unresolved
            });

            if (frag.IndexOf('@') >= 0)
            {
                log.Warning("VAS_074 val rule skipped for " + columnName + " (unresolved context): " + code);
                return "";
            }
            return frag;
        }

        /// <summary>
        /// Resolves a single @[#]Token@ from the session / global context and returns
        /// it as a SQL literal (numeric values left bare, text single-quoted), or null
        /// when the context holds no value. Tries the token as supplied (e.g. a global
        /// "#AD_User_ID") and, for a window token, also the "#"-prefixed global form.
        /// </summary>
        /// <param name="ctx">session context</param>
        /// <param name="token">context token (with or without a leading '#')</param>
        /// <returns>SQL literal or null</returns>
        private string GetCtxLiteral(Ctx ctx, string token)
        {
            string v = ctx.GetContext(token);
            if (string.IsNullOrEmpty(v) && !token.StartsWith("#")) v = ctx.GetContext("#" + token);
            if (string.IsNullOrEmpty(v)) return null;
            return ToSqlLiteral(v);
        }

        /// <summary>
        /// Builds the per-row context variable map (column name -> SQL literal) from
        /// the line's current values supplied by the client. Null values are skipped
        /// so an unset column falls through to the invoice / session context instead
        /// of substituting an empty literal.
        /// </summary>
        /// <param name="rowValues">the line's column bag (may be null)</param>
        /// <returns>column -> SQL literal map (case-insensitive)</returns>
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

        /// <summary>Formats a deserialized scalar as a SQL literal (numeric bare, text quoted, bool 'Y'/'N').</summary>
        /// <param name="v">value from the client VO</param>
        /// <returns>SQL literal</returns>
        private string ObjToSqlLiteral(object v)
        {
            if (v is bool) return ((bool)v) ? "'Y'" : "'N'";
            if (v is long || v is int || v is short || v is double || v is float || v is decimal)
                return Convert.ToString(v, CultureInfo.InvariantCulture);
            return ToSqlLiteral(Convert.ToString(v, CultureInfo.InvariantCulture));
        }

        /// <summary>Numeric strings stay bare; everything else is single-quoted (quotes escaped).</summary>
        /// <param name="v">raw string value</param>
        /// <returns>SQL literal</returns>
        private string ToSqlLiteral(string v)
        {
            decimal d;
            if (decimal.TryParse(v, NumberStyles.Any, CultureInfo.InvariantCulture, out d)) return v;
            return "'" + v.Replace("'", "''") + "'";
        }

        /// <summary>Parent-invoice context values (as SQL literals) for val-rule substitution.</summary>
        /// <param name="ctx">session context</param>
        /// <param name="C_Invoice_ID">parent invoice</param>
        /// <returns>token -> SQL literal map</returns>
        private Dictionary<string, string> GetInvoiceVars(Ctx ctx, int C_Invoice_ID)
        {
            if (_invVars != null) return _invVars;
            _invVars = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            DataSet ds = DB.ExecuteDataset(
                @"SELECT AD_Client_ID, AD_Org_ID, C_BPartner_ID, C_BPartner_Location_ID,
                         M_PriceList_ID, C_Currency_ID, COALESCE(IsSOTrx, 'N') AS IsSOTrx
                  FROM C_Invoice WHERE C_Invoice_ID = @id",
                new SqlParameter[] { new SqlParameter("@id", C_Invoice_ID) }, null);
            if (ds != null && ds.Tables.Count > 0 && ds.Tables[0].Rows.Count > 0)
            {
                DataRow r = ds.Tables[0].Rows[0];
                _invVars["AD_Client_ID"] = Util.GetValueOfInt(r["AD_Client_ID"]).ToString();
                _invVars["AD_Org_ID"] = Util.GetValueOfInt(r["AD_Org_ID"]).ToString();
                _invVars["C_BPartner_ID"] = Util.GetValueOfInt(r["C_BPartner_ID"]).ToString();
                _invVars["C_BPartner_Location_ID"] = Util.GetValueOfInt(r["C_BPartner_Location_ID"]).ToString();
                _invVars["M_PriceList_ID"] = Util.GetValueOfInt(r["M_PriceList_ID"]).ToString();
                _invVars["C_Currency_ID"] = Util.GetValueOfInt(r["C_Currency_ID"]).ToString();
                _invVars["C_Invoice_ID"] = C_Invoice_ID.ToString();
                _invVars["IsSOTrx"] = "'" + (Util.GetValueOfString(r["IsSOTrx"]) == "Y" ? "Y" : "N") + "'";
            }
            return _invVars;
        }

        #endregion

        /// <summary>
        /// Database-specific OFFSET / FETCH suffix (Oracle) vs LIMIT / OFFSET
        /// (PostgreSQL / MySQL) for catalog paging.
        /// </summary>
        /// <param name="pageSize">rows to fetch</param>
        /// <param name="offset">rows to skip</param>
        /// <returns>paging clause</returns>
        private string PagingSuffix(int pageSize, int offset)
        {
            if (pageSize <= 0) pageSize = CATALOG_PAGE_SIZE;
            if (offset < 0) offset = 0;
            if (DB.IsOracle())
                return " OFFSET " + offset + " ROWS FETCH NEXT " + pageSize + " ROWS ONLY";
            return " LIMIT " + pageSize + " OFFSET " + offset;
        }

        #endregion

        #region Line callout (server-side price / tax / amount)

        /// <summary>
        /// Server-side replacement for the C_InvoiceLine callout chain. Builds a
        /// transient (unsaved) MInvoiceLine bound to the parent invoice, applies
        /// the product / charge, quantity, price and tax exactly the way the real
        /// document does, then reads the framework-computed UOM, price, tax and
        /// amounts back out. No row is written. This keeps the panel's numbers
        /// identical to a line saved through the standard window.
        /// </summary>
        /// <param name="ctx">session context</param>
        /// <param name="req">callout request (parent + trigger column + current values)</param>
        /// <returns>recomputed values + display labels</returns>
        public LineCalcResult CalcLine(Ctx ctx, LineCalcRequest req)
        {
            LineCalcResult res = new LineCalcResult();
            if (req == null || req.C_Invoice_ID <= 0) return res;

            MInvoice inv;
            MInvoiceLine line = BuildCalcLine(ctx, req, out inv);
            if (line == null || (req.M_Product_ID <= 0 && req.C_Charge_ID <= 0)) return res;

            res.C_UOM_ID = line.GetC_UOM_ID();
            res.C_Tax_ID = line.GetC_Tax_ID();
            res.PriceEntered = line.GetPriceEntered();
            res.LineNetAmt = line.GetLineNetAmt();
            res.TaxAmt = line.GetTaxAmt();
            res.LineTotalAmt = decimal.Add(res.LineNetAmt, res.TaxAmt);
            res.QtyEntered = req.QtyEntered > 0 ? req.QtyEntered : 1;

            // Display labels for the UOM / tax cells.
            res.UOMName = GetUomLabel(ctx, res.C_UOM_ID);
            res.TaxName = res.C_Tax_ID > 0 ? MTax.Get(ctx, res.C_Tax_ID).GetName() : "";
            return res;
        }

        /// <summary>
        /// Builds a transient (unsaved) MInvoiceLine bound to the parent invoice and
        /// applies the product / charge, quantity, UOM, price, discount and tax the
        /// same way the standard C_InvoiceLine callouts do. Shared by CalcLine and
        /// RunColumnCallout so both paths produce identical numbers. No row is saved.
        /// </summary>
        /// <param name="ctx">session context</param>
        /// <param name="req">callout request</param>
        /// <param name="inv">parent invoice (out)</param>
        /// <returns>the configured line, or null when the invoice is inaccessible</returns>
        private MInvoiceLine BuildCalcLine(Ctx ctx, LineCalcRequest req, out MInvoice inv)
        {
            inv = new MInvoice(ctx, req.C_Invoice_ID, null);
            if (inv.Get_ID() <= 0) return null;

            MInvoiceLine line = new MInvoiceLine(inv);
            if (req.M_Product_ID > 0)
            {
                // Setting the product also defaults the entered UOM from the product.
                line.SetM_Product_ID(req.M_Product_ID, true);
                if (req.M_AttributeSetInstance_ID > 0)
                    line.SetM_AttributeSetInstance_ID(req.M_AttributeSetInstance_ID);
            }
            else if (req.C_Charge_ID > 0)
            {
                line.SetC_Charge_ID(req.C_Charge_ID);
            }
            else
            {
                return line;  // nothing selected yet
            }

            // Quantity (defaults to 1 for a new line).
            decimal qty = req.QtyEntered > 0 ? req.QtyEntered : 1;
            line.SetQty(qty);

            // A user-chosen UOM must be applied BEFORE pricing so the price list
            // lookup resolves for the selected UOM.
            if (req.C_UOM_ID > 0)
                line.SetC_UOM_ID(req.C_UOM_ID);

            // Price: honour a user override, otherwise pull from the price list
            // (products only - a charge keeps its entered amount).
            if (req.PriceOverride)
            {
                line.SetPriceEntered(req.PriceEntered);
                line.SetPriceActual(req.PriceEntered);
            }
            else if (req.M_Product_ID > 0)
            {
                SetLinePriceWithAttribute(line, inv, req.M_AttributeSetInstance_ID);
            }
            else
            {
                line.SetPriceEntered(req.PriceEntered);
                line.SetPriceActual(req.PriceEntered);
            }

            // Line discount percent reduces the effective (actual) price.
            ApplyDiscount(line, req.Discount);

            // Tax: honour a user override, otherwise let the line resolve it from
            // product / charge tax category + billing location + partner.
            if (req.C_Tax_ID > 0)
                line.SetC_Tax_ID(req.C_Tax_ID);
            else
                line.SetTax();

            // Amounts (Qty * Price, then tax on the net).
            line.SetLineNetAmt();
            if (line.GetC_Tax_ID() > 0)
                line.SetTaxAmt();

            return line;
        }

        /// <summary>
        /// Prices a product line from the price list using the ACTUAL attribute-set-instance
        /// so attribute-level M_ProductPrice entries apply (PriceList / PriceStd / PriceLimit).
        /// The framework MInvoiceLine.SetPrice() prices via a PRIVATE M_AttributeSetInstance_ID
        /// field that its public SetM_AttributeSetInstance_ID setter never assigns (only the
        /// ship / order-line copy paths do) - so on a manually entered line the attribute price
        /// (and hence ListPrice) is ignored. This mirrors SetPrice via MProductPricing but
        /// passes the line's real ASI.
        /// </summary>
        /// <param name="line">the line to price (product already set)</param>
        /// <param name="inv">parent invoice (price list / partner / date / SO flag)</param>
        /// <param name="asi">the line's M_AttributeSetInstance_ID (0 = none)</param>
        private void SetLinePriceWithAttribute(MInvoiceLine line, MInvoice inv, int asi)
        {
            if (line.GetM_Product_ID() == 0) return;
            MProductPricing pp = new MProductPricing(line.GetAD_Client_ID(), line.GetAD_Org_ID(),
                line.GetM_Product_ID(), inv.GetC_BPartner_ID(), line.GetQtyInvoiced(), inv.IsSOTrx());
            pp.SetM_PriceList_ID(inv.GetM_PriceList_ID());
            pp.SetPriceDate(inv.GetDateInvoiced());
            pp.SetM_AttributeSetInstance_ID(asi);
            if (Env.IsModuleInstalled("ED011_"))
                pp.SetC_UOM_ID(line.GetC_UOM_ID());
            line.SetPriceActual(pp.GetPriceStd());
            line.SetPriceList(pp.GetPriceList());
            line.SetPriceLimit(pp.GetPriceLimit());
            if (decimal.Compare(line.GetQtyEntered(), line.GetQtyInvoiced()) == 0)
                line.SetPriceEntered(line.GetPriceActual());
            else
                line.SetPriceEntered(decimal.Multiply(line.GetPriceActual(),
                    decimal.Round(decimal.Divide(line.GetQtyInvoiced(), line.GetQtyEntered()), 6)));
            if (line.GetC_UOM_ID() == 0)
                line.SetC_UOM_ID(pp.GetC_UOM_ID());
        }

        /// <summary>
        /// Reads the dictionary callout reference(s) configured on the changed
        /// column (AD_Column.Callout), executes the equivalent server-side callout
        /// logic and returns the columns it changed as a name -> value object
        /// (plus display labels). This is the data-driven "run the column callout
        /// on Product / Charge change" entry point.
        ///
        /// Note: the framework's real Callout classes (CalloutEngine subclasses)
        /// run inside the UI against a live GridTab / GridField, which cannot be
        /// constructed headlessly. So the AD_Column.Callout string is read (and
        /// returned for traceability) while the SAME business logic it triggers is
        /// executed here through MInvoiceLine - giving identical results.
        /// </summary>
        /// <param name="ctx">session context</param>
        /// <param name="req">callout request (parent + trigger column + values)</param>
        /// <returns>changed-column value object + display labels + the callout read</returns>
        public CalloutResult RunColumnCallout(Ctx ctx, LineCalcRequest req)
        {
            CalloutResult res = new CalloutResult();
            if (req == null || req.C_Invoice_ID <= 0) return res;

            // 1) Read the callout code configured on the changed column.
            string column = MapTriggerToColumn(req.TriggerColumn);
            res.Column = column;
            res.Callout = ReadColumnCallout(ctx, "C_InvoiceLine", column);

            // 2) Execute the equivalent callout logic and capture the patch.
            MInvoice inv;
            MInvoiceLine line = BuildCalcLine(ctx, req, out inv);
            if (line == null || (req.M_Product_ID <= 0 && req.C_Charge_ID <= 0)) return res;

            int taxId = line.GetC_Tax_ID();
            decimal net = line.GetLineNetAmt();
            decimal taxAmt = line.GetTaxAmt();

            // Patch object keyed by physical column name (applied back on the client).
            res.Values["C_UOM_ID"] = line.GetC_UOM_ID();
            res.Values["PriceEntered"] = line.GetPriceEntered();
            res.Values["PriceActual"] = line.GetPriceActual();
            res.Values["PriceList"] = line.GetPriceList();
            res.Values["C_Tax_ID"] = taxId;
            res.Values["TaxAmt"] = taxAmt;
            res.Values["LineNetAmt"] = net;
            res.Values["LineTotalAmt"] = decimal.Add(net, taxAmt);
            // Product and charge are mutually exclusive - mirror the framework callout.
            if (req.M_Product_ID > 0) res.Values["C_Charge_ID"] = 0;
            else if (req.C_Charge_ID > 0) { res.Values["M_Product_ID"] = 0; res.Values["M_AttributeSetInstance_ID"] = 0; }

            res.Display["uomName"] = GetUomLabel(ctx, line.GetC_UOM_ID());
            res.Display["taxName"] = taxId > 0 ? MTax.Get(ctx, taxId).GetName() : "";
            return res;
        }

        /// <summary>
        /// Reads AD_Column.Callout for a table + column (dictionary metadata).
        /// </summary>
        /// <param name="ctx">session context</param>
        /// <param name="tableName">physical table name (e.g. C_InvoiceLine)</param>
        /// <param name="columnName">physical column name (e.g. M_Product_ID)</param>
        /// <returns>the configured callout string, or empty</returns>
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

        /// <summary>
        /// Maps a panel trigger token to the physical C_InvoiceLine column whose
        /// AD_Column.Callout should be read.
        /// </summary>
        /// <param name="trigger">trigger column / token</param>
        /// <returns>physical column name</returns>
        private string MapTriggerToColumn(string trigger)
        {
            switch (trigger)
            {
                case "product": return "M_Product_ID";
                case "charge": return "C_Charge_ID";
                case "M_Product_ID":
                case "C_Charge_ID":
                case "QtyEntered":
                case "PriceEntered":
                case "C_Tax_ID":
                case "C_UOM_ID":
                case "M_AttributeSetInstance_ID":
                    return trigger;
                default: return trigger ?? "";
            }
        }

        /// <summary>
        /// Applies a line discount percent by reducing the actual price
        /// (PriceActual = PriceActual * (1 - discount / 100)). The entered price is
        /// preserved so the grid keeps showing the list / typed price.
        /// </summary>
        /// <param name="line">invoice line being priced</param>
        /// <param name="discountPct">discount percent (0 = none)</param>
        private void ApplyDiscount(MInvoiceLine line, decimal discountPct)
        {
            if (discountPct <= 0) return;
            if (discountPct > 100) discountPct = 100;
            decimal factor = decimal.Subtract(1m, decimal.Divide(discountPct, 100m));
            line.SetPriceActual(decimal.Multiply(line.GetPriceActual(), factor));
        }

        /// <summary>Columns whose value is owned by the core business setters (or
        /// is identity / system) and must NOT be overwritten by the generic VO
        /// persistence.</summary>
        private static readonly HashSet<string> CORE_OR_SYSTEM_COLUMNS = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            "C_InvoiceLine_ID", "C_Invoice_ID", "AD_Client_ID", "AD_Org_ID",
            "Created", "CreatedBy", "Updated", "UpdatedBy", "IsActive",
            "M_Product_ID", "C_Charge_ID", "M_AttributeSetInstance_ID",
            "QtyEntered", "C_UOM_ID", "PriceEntered", "PriceActual",
            "C_Tax_ID", "Line", "Description", "LineNetAmt", "TaxAmt", "LineTotalAmt", "Discount"
        };

        private HashSet<string> _updateableColumns;

        /// <summary>
        /// Persists every NON-core, updateable C_InvoiceLine column present in the
        /// client VO through PO.Set_Value, so columns the callout set (PriceList,
        /// PriceLimit, C_Currency_ID, PrintDescription, HSN, TCS, revenue
        /// recognition, ...) are saved. Null values and core / system / read-only
        /// columns are skipped. Numeric JSON tokens are coerced to CLR primitives.
        /// </summary>
        /// <param name="line">invoice line being saved</param>
        /// <param name="values">full column bag from the client</param>
        private void ApplyExtraColumns(MInvoiceLine line, Dictionary<string, object> values)
        {
            if (values == null || values.Count == 0) return;
            HashSet<string> updateable = GetUpdateableColumns();
            foreach (KeyValuePair<string, object> kv in values)
            {
                string col = kv.Key;
                if (kv.Value == null) continue;
                if (CORE_OR_SYSTEM_COLUMNS.Contains(col)) continue;
                if (updateable.Count > 0 && !updateable.Contains(col)) continue;
                try { line.Set_Value(col, CoerceJsonValue(kv.Value)); }
                catch (Exception ex) { log.Warning("VAS_074 SaveLines: skip column " + col + " - " + ex.Message); }
            }
        }

        /// <summary>Coerces a Newtonsoft-deserialized scalar to a CLR primitive PO accepts.</summary>
        /// <param name="v">deserialized value (Int64 / Double / String / Boolean)</param>
        /// <returns>coerced value</returns>
        private object CoerceJsonValue(object v)
        {
            if (v is long)
            {
                long l = (long)v;
                if (l >= int.MinValue && l <= int.MaxValue) return (int)l;
                return l;
            }
            if (v is double) return Convert.ToDecimal((double)v);
            // A dynamic Date / DateTime field arrives as an ISO string (yyyy-MM-dd or
            // yyyy-MM-ddTHH:mm[:ss]) - convert so PO.Set_Value gets a real DateTime.
            // Anchored so a free-text value is never mistaken for a date.
            string s = v as string;
            if (s != null && Regex.IsMatch(s, @"^\d{4}-\d{2}-\d{2}(T\d{2}:\d{2}(:\d{2})?)?$"))
            {
                DateTime dt;
                if (DateTime.TryParse(s, CultureInfo.InvariantCulture, DateTimeStyles.None, out dt))
                    return dt;
            }
            return v;
        }

        /// <summary>Active + updateable C_InvoiceLine column names (per-instance cached).</summary>
        /// <returns>updateable column set</returns>
        private HashSet<string> GetUpdateableColumns()
        {
            if (_updateableColumns != null) return _updateableColumns;
            _updateableColumns = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            DataSet ds = DB.ExecuteDataset(
                @"SELECT c.ColumnName
                  FROM AD_Column c
                  INNER JOIN AD_Table t ON (c.AD_Table_ID = t.AD_Table_ID)
                  WHERE t.TableName = 'C_InvoiceLine'
                    AND c.IsActive = 'Y'
                    AND COALESCE(c.IsUpdateable, 'Y') = 'Y'");
            if (ds != null && ds.Tables.Count > 0)
                foreach (DataRow r in ds.Tables[0].Rows)
                    _updateableColumns.Add(Util.GetValueOfString(r["ColumnName"]));
            return _updateableColumns;
        }

        /// <summary>Returns the UOM symbol (or name) for display, or empty.</summary>
        /// <param name="ctx">session context</param>
        /// <param name="C_UOM_ID">unit of measure</param>
        /// <returns>display label</returns>
        private string GetUomLabel(Ctx ctx, int C_UOM_ID)
        {
            if (C_UOM_ID <= 0) return "";
            object o = DB.ExecuteScalar(
                "SELECT COALESCE(UOMSymbol, Name) FROM C_UOM WHERE C_UOM_ID=@id",
                new SqlParameter[] { new SqlParameter("@id", C_UOM_ID) }, null);
            return Util.GetValueOfString(o);
        }

        #endregion

        #region Product attributes (M_AttributeSetInstance - PAttributes rule)

        /// <summary>
        /// Reads the product's attribute set and its selectable (list) attribute
        /// values so the attribute picker can list them. Follows the standard
        /// PAttributes path: resolve M_AttributeSet from the product, then its
        /// M_Attribute rows and their M_AttributeValue options.
        /// </summary>
        /// <param name="ctx">session context</param>
        /// <param name="M_Product_ID">product whose attribute set is read</param>
        /// <returns>attribute-set definition (empty when the product has none)</returns>
        public AttributeSetInfo GetProductAttributes(Ctx ctx, int M_Product_ID)
        {
            AttributeSetInfo info = new AttributeSetInfo();
            if (M_Product_ID <= 0) return info;

            MProduct product = MProduct.Get(ctx, M_Product_ID);
            if (product == null) return info;
            int M_AttributeSet_ID = product.GetM_AttributeSet_ID();
            if (M_AttributeSet_ID <= 0) return info;   // product has no attribute set

            info.M_AttributeSet_ID = M_AttributeSet_ID;
            info.M_Product_ID = M_Product_ID;
            info.ProductName = product.GetName();

            // Role permissions (mirrors PAttributesModel.LoadInit) - drive whether the
            // picker offers the "New attribute" (create) and per-row "Edit" actions.
            MRole role = MRole.GetDefault(ctx);
            if (role != null)
            {
                info.IsCanCreate = role.IsCanCreateAttribute();
                info.IsCanEdit = role.IsCanEditAttribute();
            }

            // Set-level instance controls (drives Lot / Serial / Guarantee Date
            // fields in the dynamic editor, mirroring the PAttribute form).
            MAttributeSet mas = MAttributeSet.Get(ctx, M_AttributeSet_ID);
            if (mas != null)
            {
                info.IsLot = mas.IsLot();
                info.IsSerNo = mas.IsSerNo();
                info.IsGuaranteeDate = mas.IsGuaranteeDate();
                info.IsMandatory = mas.IsMandatory();
                // Default guarantee date for a NEW instance: today + configured GuaranteeDays.
                // GuaranteeDays is set on the PRODUCT MASTER (M_Product.GuaranteeDays) - read it
                // from the product first, only falling back to the attribute set's value; when
                // neither has days, fall back to today so the field is still autofilled.
                if (info.IsGuaranteeDate)
                {
                    int gdays = product.GetGuaranteeDays();
                    if (gdays <= 0) gdays = mas.GetGuaranteeDays();
                    DateTime gdt = gdays > 0 ? DateTime.Now.AddDays(gdays) : DateTime.Now;
                    info.GuaranteeDateDefault = gdt.ToString("yyyy-MM-dd");
                }
            }

            // Attributes belonging to the set, with their list values (if any).
            string sql = @"SELECT a.M_Attribute_ID, a.Name AS AttributeName, a.AttributeValueType,
                                  COALESCE(a.IsInstanceAttribute, 'N') AS IsInstanceAttribute,
                                  COALESCE(a.IsMandatory, 'N') AS IsMandatory,
                                  av.M_AttributeValue_ID, COALESCE(av.Name, N'') AS ValueName,
                                  COALESCE(av.Value, N'') AS ValueCode
                           FROM M_AttributeUse au
                           INNER JOIN M_Attribute a ON (au.M_Attribute_ID = a.M_Attribute_ID)
                           LEFT JOIN M_AttributeValue av ON (a.M_Attribute_ID = av.M_Attribute_ID
                                AND av.IsActive = 'Y')
                           WHERE au.M_AttributeSet_ID = @setId
                             AND au.IsActive = 'Y'
                             AND a.IsActive = 'Y'
                           ORDER BY au.SeqNo, a.Name, av.Name";

            DataSet ds = DB.ExecuteDataset(sql,
                new SqlParameter[] { new SqlParameter("@setId", M_AttributeSet_ID) }, null);
            if (ds == null || ds.Tables.Count == 0) return info;

            Dictionary<int, AttributeDef> map = new Dictionary<int, AttributeDef>();
            foreach (DataRow r in ds.Tables[0].Rows)
            {
                int attrId = Util.GetValueOfInt(r["M_Attribute_ID"]);
                AttributeDef def;
                if (!map.TryGetValue(attrId, out def))
                {
                    def = new AttributeDef();
                    def.M_Attribute_ID = attrId;
                    def.Name = Util.GetValueOfString(r["AttributeName"]);
                    def.ValueType = Util.GetValueOfString(r["AttributeValueType"]);
                    def.IsInstanceAttribute = Util.GetValueOfString(r["IsInstanceAttribute"]) == "Y";
                    def.IsMandatory = Util.GetValueOfString(r["IsMandatory"]) == "Y";
                    def.Values = new List<AttributeValueDef>();
                    map[attrId] = def;
                    info.Attributes.Add(def);
                }
                int valId = Util.GetValueOfInt(r["M_AttributeValue_ID"]);
                if (valId > 0)
                {
                    AttributeValueDef v = new AttributeValueDef();
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
        /// typed by the attribute's value type - used to autofill the edit form.
        /// </summary>
        /// <param name="ctx">session context</param>
        /// <param name="M_AttributeSetInstance_ID">instance to read</param>
        /// <returns>one entry per M_AttributeInstance row</returns>
        public List<AttributeInstanceValue> GetInstanceValues(Ctx ctx, int M_AttributeSetInstance_ID)
        {
            List<AttributeInstanceValue> list = new List<AttributeInstanceValue>();
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
                AttributeInstanceValue v = new AttributeInstanceValue();
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

        #endregion

        #region Write actions (insert / update / delete C_InvoiceLine)

        /// <summary>
        /// Inserts or updates the supplied invoice lines through the MInvoiceLine
        /// business class (never a hand-written INSERT). All lines for one request
        /// share a single transaction so a failure rolls the whole batch back.
        /// Returns the refreshed line list + totals on success.
        /// </summary>
        /// <param name="ctx">session context</param>
        /// <param name="C_Invoice_ID">parent invoice</param>
        /// <param name="AD_Window_ID">source window (drives the refreshed line column set)</param>
        /// <param name="rows">lines to persist</param>
        /// <returns>save result with refreshed lines / totals or an error message key</returns>
        public SaveLinesResult SaveLines(Ctx ctx, int C_Invoice_ID, int AD_Window_ID, List<InvoiceLineInput> rows, int page = 0)
        {
            SaveLinesResult res = new SaveLinesResult();
            if (C_Invoice_ID <= 0 || rows == null || rows.Count == 0)
            {
                res.ErrorKey = "VAS_074_NothingToSave";
                return res;
            }

            // Guard: do not allow edits on a processed / completed invoice.
            CreateInvoiceLinePanelData ctxData = new CreateInvoiceLinePanelData();
            LoadParentContext(ctx, C_Invoice_ID, ctxData);
            if (ctxData.C_Invoice_ID <= 0)
            {
                res.ErrorKey = "VAS_074_NoAccess";
                return res;
            }
            if (!ctxData.IsEditable)
            {
                res.ErrorKey = "VAS_074_InvoiceNotEditable";
                return res;
            }

            Trx trx = Trx.Get("VAS074_SaveLines_" + C_Invoice_ID, true);
            try
            {
                MInvoice inv = new MInvoice(ctx, C_Invoice_ID, trx);
                foreach (InvoiceLineInput input in rows)
                {
                    if (input.M_Product_ID <= 0 && input.C_Charge_ID <= 0)
                        continue;  // skip empty lines

                    MInvoiceLine line = input.C_InvoiceLine_ID > 0
                        ? new MInvoiceLine(ctx, input.C_InvoiceLine_ID, trx)
                        : new MInvoiceLine(inv);

                    if (input.C_InvoiceLine_ID <= 0)
                        line.SetInvoice(inv);

                    if (input.M_Product_ID > 0)
                    {
                        line.SetM_Product_ID(input.M_Product_ID, true);
                        line.SetC_Charge_ID(0);
                        if (input.M_AttributeSetInstance_ID > 0)
                            line.SetM_AttributeSetInstance_ID(input.M_AttributeSetInstance_ID);
                    }
                    else
                    {
                        line.SetC_Charge_ID(input.C_Charge_ID);
                        line.SetM_Product_ID(0);
                    }

                    decimal qty = input.QtyEntered > 0 ? input.QtyEntered : 1;
                    line.SetQty(qty);

                    // Apply the user-chosen UOM before pricing.
                    if (input.C_UOM_ID > 0)
                        line.SetC_UOM_ID(input.C_UOM_ID);

                    if (input.PriceEntered != 0 || input.M_Product_ID <= 0)
                    {
                        line.SetPriceEntered(input.PriceEntered);
                        line.SetPriceActual(input.PriceEntered);
                    }
                    else
                    {
                        //  SetLinePriceWithAttribute(line, inv, input.M_AttributeSetInstance_ID);
                    }

                    // Line discount percent reduces the actual price.
                    // ApplyDiscount(line, input.Discount);

                    if (input.C_Tax_ID > 0)
                    {
                        line.SetC_Tax_ID(input.C_Tax_ID);
                    }
                    else
                    {
                        line.SetTax();
                    }

                    if (input.Line > 0)
                    {
                        line.SetLine(input.Line);
                    }

                    line.SetDescription(input.Description ?? "");

                    // Generic persistence: every OTHER updateable C_InvoiceLine column
                    // the callout / panel set (e.g. PriceList, PriceLimit, C_Currency_ID,
                    // PrintDescription, HSN, TCS, revenue recognition) is applied through
                    // PO.Set_Value so the full VO is saved - the core financial columns
                    // above keep their business-rule values (denylist below).
                    ApplyExtraColumns(line, input.Values);

                    // QtyInvoiced (base UOM). SetQty() set QtyInvoiced = QtyEntered (raw); the
                    // framework only converts it to the product base UOM in MInvoiceLine.beforeSave
                    // for PURCHASE invoices (!IsSOTrx), so a SALES invoice with a non-base entered
                    // UOM would persist the entered qty as invoiced. Convert here so the base-UOM
                    // QtyInvoiced is correct for both (idempotent for PO - beforeSave re-converts).
                    //if (input.M_Product_ID > 0 && line.GetC_UOM_ID() > 0)
                    //{
                    //    decimal? convQty = MUOMConversion.ConvertProductFrom(
                    //        ctx, input.M_Product_ID, line.GetC_UOM_ID(), line.GetQtyEntered());
                    //    if (convQty != null && convQty.Value != 0)
                    //        line.SetQtyInvoiced(convQty.Value);
                    //}

                    //line.SetLineNetAmt();
                    //if (line.GetC_Tax_ID() > 0)
                    //    line.SetTaxAmt();

                    if (!line.Save())
                    {
                        string err = string.Empty;
                        ValueNamePair pp = VLogger.RetrieveError();
                        if (pp != null)
                        {
                            string val = pp.GetName();
                            if (String.IsNullOrEmpty(val))
                            {
                                val = Msg.GetMsg(ctx, pp.GetValue());
                            }
                            err = val;
                        }
                        log.Warning("VAS_074 SaveLines: line save failed (Line " + input.Line + ") - " + err);
                        // Record the failure against THIS line and keep going, so every
                        // failing record is reported on its own row (the whole batch is rolled
                        // back below). Safe because the panel pre-validates mandatory fields,
                        // so remaining rejections are beforeSave business rules that run no SQL
                        // (they don't poison the transaction).
                        res.LineErrors.Add(new LineSaveError
                        {
                            RowKey = input.RowKey,
                            C_InvoiceLine_ID = input.C_InvoiceLine_ID,
                            Line = input.Line,
                            Message = err
                        });
                    }
                }

                // Any line failed -> roll the whole batch back (all-or-nothing) and return the
                // per-line errors; the client paints each on its record row.
                if (res.LineErrors.Count > 0)
                {
                    trx.Rollback();
                    res.ErrorKey = "VAS_074_SaveFailed";
                    res.ErrorDetail = res.LineErrors[0].Message;
                    return res;
                }

                trx.Commit();
            }
            catch (Exception ex)
            {
                trx.Rollback();
                log.Log(Level.SEVERE, "VAS_074 SaveLines failed", ex);
                res.ErrorKey = "VAS_074_SaveFailed";
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
            res.Lines = LoadLines(ctx, C_Invoice_ID, ResolveInvoiceLineTabs(AD_Window_ID), page, out total);
            res.LinesTotal = total;
            res.LinePage = page;
            res.LinePageSize = LINE_PAGE_SIZE;
            decimal soNet, soTax, soTcs;
            ComputeOtherPageTotals(ctx, C_Invoice_ID, res.Lines, ctxData.IsTaxIncluded, out soNet, out soTax, out soTcs);
            res.OtherPagesSubtotal = soNet; res.OtherPagesTax = soTax; res.OtherPagesTcs = soTcs;
            return res;
        }

        /// <summary>
        /// Soft-deletes (deletes) the supplied saved invoice lines through the
        /// MInvoiceLine business class inside a single transaction.
        /// </summary>
        /// <param name="ctx">session context</param>
        /// <param name="C_Invoice_ID">parent invoice</param>
        /// <param name="AD_Window_ID">source window (drives the refreshed line column set)</param>
        /// <param name="lineIds">C_InvoiceLine ids to remove</param>
        /// <returns>save result with the refreshed line list</returns>
        public SaveLinesResult DeleteLines(Ctx ctx, int C_Invoice_ID, int AD_Window_ID, List<int> lineIds, int page = 0)
        {
            SaveLinesResult res = new SaveLinesResult();
            if (C_Invoice_ID <= 0 || lineIds == null || lineIds.Count == 0)
            {
                res.ErrorKey = "VAS_074_NothingToSave";
                return res;
            }

            CreateInvoiceLinePanelData ctxData = new CreateInvoiceLinePanelData();
            LoadParentContext(ctx, C_Invoice_ID, ctxData);
            if (ctxData.C_Invoice_ID <= 0) { res.ErrorKey = "VAS_074_NoAccess"; return res; }
            if (!ctxData.IsEditable) { res.ErrorKey = "VAS_074_InvoiceNotEditable"; return res; }

            Trx trx = Trx.Get("VAS074_DeleteLines_" + C_Invoice_ID, true);
            try
            {
                foreach (int id in lineIds)
                {
                    if (id <= 0) continue;
                    MInvoiceLine line = new MInvoiceLine(ctx, id, trx);
                    if (line.Get_ID() != id) continue;            // not found / no access
                    if (line.GetC_Invoice_ID() != C_Invoice_ID) continue;  // belongs elsewhere
                    if (!line.Delete(true, trx))
                    {
                        trx.Rollback();
                        string err = string.Empty;
                        ValueNamePair pp = VLogger.RetrieveError();
                        if (pp != null)
                        {
                            string val = pp.GetName();
                            if (String.IsNullOrEmpty(val))
                            {
                                val = Msg.GetMsg(ctx, pp.GetValue());
                            }
                            if (String.IsNullOrEmpty(val))
                            {
                                err = Msg.GetMsg(ctx, "VAS_074_DeleteFailed");
                            }
                            else
                            {
                                err = val;
                            }
                        }
                        log.Warning("VAS_074 DeleteLines: delete failed for line " + id);
                        res.ErrorKey = err;
                        return res;
                    }
                }
                trx.Commit();
            }
            catch (Exception ex)
            {
                trx.Rollback();
                log.Log(Level.SEVERE, "VAS_074 DeleteLines failed", ex);
                res.ErrorKey = "VAS_074_DeleteFailed";
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
            res.Lines = LoadLines(ctx, C_Invoice_ID, ResolveInvoiceLineTabs(AD_Window_ID), page, out total);
            // Deleting the last row(s) on a page can leave the requested page past the end;
            // step back so the client shows a populated page.
            int pageCount = System.Math.Max(1, (int)System.Math.Ceiling(total / (double)LINE_PAGE_SIZE));
            if (page > pageCount - 1)
            {
                page = pageCount - 1;
                res.Lines = LoadLines(ctx, C_Invoice_ID, ResolveInvoiceLineTabs(AD_Window_ID), page, out total);
            }
            res.LinesTotal = total;
            res.LinePage = page;
            res.LinePageSize = LINE_PAGE_SIZE;
            decimal doNet, doTax, doTcs;
            ComputeOtherPageTotals(ctx, C_Invoice_ID, res.Lines, ctxData.IsTaxIncluded, out doNet, out doTax, out doTcs);
            res.OtherPagesSubtotal = doNet; res.OtherPagesTax = doTax; res.OtherPagesTcs = doTcs;
            return res;
        }

        #endregion
    }

    #region Data contracts

    /// <summary>Parent context + saved lines returned to the panel on load.</summary>
    public class CreateInvoiceLinePanelData
    {
        public int C_Invoice_ID { get; set; }
        public int AD_Client_ID { get; set; }
        public int AD_Org_ID { get; set; }
        public int C_BPartner_ID { get; set; }
        public int C_BPartner_Location_ID { get; set; }
        public int M_PriceList_ID { get; set; }
        public int C_Currency_ID { get; set; }
        public DateTime? DateInvoiced { get; set; }
        public DateTime? DateAcct { get; set; }
        public bool IsSOTrx { get; set; }
        public bool IsTaxIncluded { get; set; }
        public string DocStatus { get; set; }
        public bool Processed { get; set; }
        public bool IsEditable { get; set; }
        public int LinesTotal { get; set; }          // total saved lines (for the pager)
        public int LinePage { get; set; }            // 0-based page returned
        public int LinePageSize { get; set; }        // rows per page (LINE_PAGE_SIZE)
        // Saved totals of every line NOT on the current page (invoice grand total minus
        // this page). The client adds its live current-page sum so the totals row shows
        // the WHOLE invoice yet still updates while a page is edited.
        public decimal OtherPagesSubtotal { get; set; }
        public decimal OtherPagesTax { get; set; }
        public decimal OtherPagesTcs { get; set; }
        public int StdPrecision { get; set; }
        public string CurSymbol { get; set; }
        public string CurISO { get; set; }
        public int AD_Window_ID { get; set; }        // source window (column meta)
        public int AD_Tab_ID { get; set; }           // first resolved C_InvoiceLine tab (info)
        public List<int> AD_Tab_IDs { get; set; }    // all C_InvoiceLine tabs in the window
        public List<InvoiceLineRow> Lines { get; set; }
        public List<UomItem> UomList { get; set; }
        public List<TaxItem> TaxList { get; set; }
        public List<ColumnMeta> Columns { get; set; }
        /// <summary>Login/session context ($Element_*/#Global) for display/read-only logic.</summary>
        public Dictionary<string, string> LoginContext { get; set; }

        public CreateInvoiceLinePanelData()
        {
            Lines = new List<InvoiceLineRow>();
            UomList = new List<UomItem>();
            TaxList = new List<TaxItem>();
            Columns = new List<ColumnMeta>();
            AD_Tab_IDs = new List<int>();
            LoginContext = new Dictionary<string, string>();
            StdPrecision = 2;
        }
    }

    /// <summary>
    /// AD_Column dictionary metadata for one C_InvoiceLine column: its callout
    /// reference, validation and reference info. Cached on the client so callout
    /// code + validation are resolved without re-querying AD_Column per change.
    /// </summary>
    public class ColumnMeta
    {
        public string ColumnName { get; set; }
        public int AD_Column_ID { get; set; }         // for MLookupFactory (loads the column's lookup + val rule)
        public string Callout { get; set; }          // AD_Column.Callout (e.g. ViennaAdvantage.Model.CalloutInvoice.Product)
        public bool IsMandatory { get; set; }
        public int AD_Reference_ID { get; set; }
        public bool IsUpdateable { get; set; }
        public int FieldLength { get; set; }
        public string ReadOnlyLogic { get; set; }    // AD_Column.ReadOnlyLogic (dynamic read-only expr)
        public int AD_Val_Rule_ID { get; set; }
        public string ValRuleType { get; set; }      // 'S' SQL, 'J' Java/JS, 'F' filter
        public string ValRuleCode { get; set; }
        public bool IsDisplayed { get; set; }        // AD_Field.IsDisplayed (window-driven)
        public bool IsReadOnly { get; set; }         // AD_Field.IsReadOnly (window-driven)
        public int SeqNo { get; set; }               // AD_Field.SeqNo (window-driven)
        public string DisplayLogic { get; set; }     // AD_Field.DisplayLogic (dynamic show/hide expr)
        public int AD_Reference_Value_ID { get; set; } // List/Table reference value (AD_Ref_List / AD_Ref_Table)
        public string Name { get; set; }             // field caption (AD_Field.Name / AD_Column.Name)
        public int AD_Image_ID { get; set; }         // AD_Field.AD_Image_ID (window-driven field image)
        public string IconFont { get; set; }         // AD_Image.FontName (icon-font class) - takes priority over ImageUrl
        public string ImageUrl { get; set; }         // resolved Thumb46x46 URL ("" when none / file missing / a font is set)
        public List<RefListItem> RefListValues { get; set; }   // inline values for a List (ref 17) field
        public ColumnMeta() { RefListValues = new List<RefListItem>(); }
    }

    /// <summary>One value of a List (AD_Reference 17) field, from AD_Ref_List.</summary>
    public class RefListItem
    {
        public string Value { get; set; }
        public string Name { get; set; }
    }

    /// <summary>Request for the generic FK (Table/TableDir/Search) lookup of a dynamic field.</summary>
    public class RefLookupRequest
    {
        public int C_Invoice_ID { get; set; }
        public string ColumnName { get; set; }
        public string Query { get; set; }
        public int Id { get; set; }                  // > 0 -> resolve a single row's label
        public int PageSize { get; set; }
        public int Offset { get; set; }
        public Dictionary<string, object> RowValues { get; set; }
        public RefLookupRequest() { RowValues = new Dictionary<string, object>(); }
    }

    /// <summary>One row of a generic FK lookup (id + display label).</summary>
    public class RefItem
    {
        public int Id { get; set; }
        public string Name { get; set; }
    }

    /// <summary>A unit of measure for the UOM dropdown.</summary>
    public class UomItem
    {
        public int C_UOM_ID { get; set; }
        public string Name { get; set; }
    }

    /// <summary>A tax (with rate) for the tax dropdown / client-side preview.</summary>
    public class TaxItem
    {
        public int C_Tax_ID { get; set; }
        public string Name { get; set; }
        public decimal Rate { get; set; }
    }

    /// <summary>
    /// Request for the per-row lookup re-filter: the parent invoice plus the line's
    /// current column values, used to resolve a column's AD_Val_Rule in line context.
    /// </summary>
    public class LookupRequest
    {
        public int C_Invoice_ID { get; set; }
        public string ColumnName { get; set; }                  // optional hint (UOM / tax returned together)
        public Dictionary<string, object> RowValues { get; set; }
        public LookupRequest() { RowValues = new Dictionary<string, object>(); }
    }

    /// <summary>Per-row filtered lookup lists (UOM + tax) for one invoice line.</summary>
    public class LookupData
    {
        public int C_Invoice_ID { get; set; }
        public List<UomItem> UomList { get; set; }
        public List<TaxItem> TaxList { get; set; }
        public LookupData()
        {
            UomList = new List<UomItem>();
            TaxList = new List<TaxItem>();
        }
    }

    /// <summary>A saved invoice line shown in the panel grid.</summary>
    public class InvoiceLineRow
    {
        public int C_InvoiceLine_ID { get; set; }
        public int Line { get; set; }
        public int M_Product_ID { get; set; }
        public string ProductName { get; set; }
        public int C_Charge_ID { get; set; }
        public string ChargeName { get; set; }
        public string Description { get; set; }
        public decimal QtyEntered { get; set; }
        public int C_UOM_ID { get; set; }
        public string UOMName { get; set; }
        public decimal PriceEntered { get; set; }
        public int C_Tax_ID { get; set; }
        public string TaxName { get; set; }
        public decimal TaxAmt { get; set; }
        public decimal LineNetAmt { get; set; }
        public decimal LineTotalAmt { get; set; }
        public int M_AttributeSetInstance_ID { get; set; }
        public string AttrName { get; set; }
        /// <summary>True when the line's product has an attribute set (drives the
        /// "Set attribute…" affordance even when no instance is linked yet).</summary>
        public bool HasAttributeSet { get; set; }
        /// <summary>M_Product.ProductType (S=Service, E=Expense, ...) - drives the
        /// conditional "Additional Info" field groups; empty for a charge line.</summary>
        public string ProductType { get; set; }
        /// <summary>Every C_InvoiceLine column for this line (full VO).</summary>
        public Dictionary<string, object> Values { get; set; }
        public InvoiceLineRow() { Values = new Dictionary<string, object>(); }
    }

    /// <summary>A product or charge row from the catalog search.</summary>
    public class CatalogItem
    {
        public int RecordId { get; set; }
        public string Kind { get; set; }          // "P" = product, "C" = charge
        public string SearchKey { get; set; }     // Value / Name
        public string DisplayName { get; set; }
        public string Description { get; set; }
        public bool HasAttributeSet { get; set; }
        public string ProductType { get; set; }    // M_Product.ProductType (empty for a charge)
    }

    /// <summary>Inbound callout request from the panel.</summary>
    public class LineCalcRequest
    {
        public int C_Invoice_ID { get; set; }
        public string TriggerColumn { get; set; }
        public int M_Product_ID { get; set; }
        public int C_Charge_ID { get; set; }
        public int M_AttributeSetInstance_ID { get; set; }
        public decimal QtyEntered { get; set; }
        public int C_UOM_ID { get; set; }
        public decimal PriceEntered { get; set; }
        public bool PriceOverride { get; set; }   // true once the user edits price
        public int C_Tax_ID { get; set; }
        public decimal Discount { get; set; }     // line discount percent
    }

    /// <summary>Recomputed values returned by the callout.</summary>
    public class LineCalcResult
    {
        public int C_UOM_ID { get; set; }
        public string UOMName { get; set; }
        public decimal QtyEntered { get; set; }
        public decimal PriceEntered { get; set; }
        public int C_Tax_ID { get; set; }
        public string TaxName { get; set; }
        public decimal TaxAmt { get; set; }
        public decimal LineNetAmt { get; set; }
        public decimal LineTotalAmt { get; set; }
    }

    /// <summary>
    /// Result of running a column's AD_Column.Callout: the changed columns as a
    /// name -> value object the client patches back into the line, the display
    /// labels, plus the callout reference that was read (for traceability).
    /// </summary>
    public class CalloutResult
    {
        public string Column { get; set; }                       // column whose callout ran
        public string Callout { get; set; }                      // AD_Column.Callout value read
        public Dictionary<string, object> Values { get; set; }   // changed column -> value
        public Dictionary<string, string> Display { get; set; }  // display labels (uomName / taxName)
        public CalloutResult()
        {
            Values = new Dictionary<string, object>();
            Display = new Dictionary<string, string>();
        }
    }

    /// <summary>Attribute set definition for the dynamic attribute editor.</summary>
    public class AttributeSetInfo
    {
        public int M_AttributeSet_ID { get; set; }
        public int M_Product_ID { get; set; }
        public string ProductName { get; set; }
        public bool IsLot { get; set; }            // set captures a lot number
        public bool IsSerNo { get; set; }          // set captures a serial number
        public bool IsGuaranteeDate { get; set; }  // set captures a guarantee date
        public string GuaranteeDateDefault { get; set; }  // ISO yyyy-MM-dd default for a NEW instance (today + GuaranteeDays, else today)
        public bool IsMandatory { get; set; }      // attribute set instance is mandatory
        public bool IsCanCreate { get; set; }      // role may create attribute instances
        public bool IsCanEdit { get; set; }        // role may edit attribute instances
        public List<AttributeDef> Attributes { get; set; }
        public AttributeSetInfo() { Attributes = new List<AttributeDef>(); }
    }

    /// <summary>One attribute within a set (with its list values).</summary>
    public class AttributeDef
    {
        public int M_Attribute_ID { get; set; }
        public string Name { get; set; }
        public string ValueType { get; set; }     // 'L' list, 'N' number, 'S' string
        public bool IsInstanceAttribute { get; set; }
        public bool IsMandatory { get; set; }
        public List<AttributeValueDef> Values { get; set; }
    }

    /// <summary>A selectable list value for an attribute.</summary>
    public class AttributeValueDef
    {
        public int M_AttributeValue_ID { get; set; }
        public string Code { get; set; }
        public string Name { get; set; }
    }

    /// <summary>Attribute-instance create request from the dynamic editor.</summary>
    public class AttributeSaveRequest
    {
        public int M_Product_ID { get; set; }
        public int M_AttributeSetInstance_ID { get; set; }   // > 0 -> UPDATE this instance in place (edit); 0 -> create new
        public string Lot { get; set; }
        public string SerNo { get; set; }
        public string GuaranteeDate { get; set; }   // ISO yyyy-MM-dd
        public List<AttributeValueSelection> Values { get; set; }
    }

    /// <summary>One entered attribute value (typed) in a save request.</summary>
    public class AttributeValueSelection
    {
        public int M_Attribute_ID { get; set; }
        public string ValueType { get; set; }        // 'L' list, 'N' number, 'S' string
        public int M_AttributeValue_ID { get; set; } // list selection
        public decimal? NumberValue { get; set; }    // number attribute
        public string StringValue { get; set; }      // string attribute
        public string DisplayValue { get; set; }     // human label for the instance
    }

    /// <summary>One stored attribute value on an existing instance (for edit autofill).</summary>
    public class AttributeInstanceValue
    {
        public int M_Attribute_ID { get; set; }
        public string ValueType { get; set; }        // 'L' list, 'N' number, 'S' string
        public int M_AttributeValue_ID { get; set; } // list selection
        public decimal? NumberValue { get; set; }    // number attribute
        public string StringValue { get; set; }      // string attribute
    }

    /// <summary>Result of creating an attribute set instance.</summary>
    public class AttributeSaveResult
    {
        public int M_AttributeSetInstance_ID { get; set; }
        public string Description { get; set; }
        public string Error { get; set; }   // framework mandatory / save error (empty on success)
    }

    /// <summary>One inbound line to insert / update.</summary>
    public class InvoiceLineInput
    {
        public int C_InvoiceLine_ID { get; set; }   // 0 / negative -> insert
        public string RowKey { get; set; }          // client rowId, echoed back on a per-line save error
        public int Line { get; set; }
        public int M_Product_ID { get; set; }
        public int C_Charge_ID { get; set; }
        public int M_AttributeSetInstance_ID { get; set; }
        public decimal QtyEntered { get; set; }
        public int C_UOM_ID { get; set; }
        public decimal PriceEntered { get; set; }
        public int C_Tax_ID { get; set; }
        public decimal Discount { get; set; }     // line discount percent
        public string Description { get; set; }
        /// <summary>
        /// Full column bag from the client (every C_InvoiceLine column the callout
        /// / panel touched). Non-core updateable columns are persisted generically.
        /// </summary>
        public Dictionary<string, object> Values { get; set; }
        public InvoiceLineInput() { Values = new Dictionary<string, object>(); }
    }

    /// <summary>POST body for the batch line save.</summary>
    public class SaveLinesRequest
    {
        public int C_Invoice_ID { get; set; }
        public int AD_Window_ID { get; set; }   // source window (refreshed line column set)
        public int Page { get; set; }           // current 0-based line page (refreshed after save)
        public List<InvoiceLineInput> Lines { get; set; }
        public SaveLinesRequest() { Lines = new List<InvoiceLineInput>(); }
    }

    /// <summary>POST body for the batch line delete.</summary>
    public class DeleteLinesRequest
    {
        public int C_Invoice_ID { get; set; }
        public int AD_Window_ID { get; set; }   // source window (refreshed line column set)
        public int Page { get; set; }           // current 0-based line page (refreshed after delete)
        public List<int> LineIds { get; set; }
        public DeleteLinesRequest() { LineIds = new List<int>(); }
    }

    /// <summary>A save failure for one specific line, echoed back so the client can show
    /// the message on that record's row instead of a single global toast.</summary>
    public class LineSaveError
    {
        public string RowKey { get; set; }          // client rowId (primary match key)
        public int C_InvoiceLine_ID { get; set; }   // fallback match for existing lines
        public int Line { get; set; }               // fallback match (line number)
        public string Message { get; set; }
    }

    /// <summary>Result of a save / delete batch.</summary>
    public class SaveLinesResult
    {
        public bool Success { get; set; }
        public string ErrorKey { get; set; }
        public string ErrorDetail { get; set; }
        public List<LineSaveError> LineErrors { get; set; }   // per-line failures (empty on success)
        public List<InvoiceLineRow> Lines { get; set; }
        public int LinesTotal { get; set; }                   // total saved lines (pager)
        public int LinePage { get; set; }                     // 0-based page returned
        public int LinePageSize { get; set; }                 // rows per page
        public decimal OtherPagesSubtotal { get; set; }       // saved totals of lines NOT on this page
        public decimal OtherPagesTax { get; set; }
        public decimal OtherPagesTcs { get; set; }
        public SaveLinesResult() { Lines = new List<InvoiceLineRow>(); LineErrors = new List<LineSaveError>(); }
    }

    #endregion
}
