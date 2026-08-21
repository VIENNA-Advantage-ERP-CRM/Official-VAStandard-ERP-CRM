/******************************************************
 * Module Name    : VASLogic
 * Purpose        : Backing model for the VAS_107_CreateOrderBottomPanel tab
 *                  panel. Provides parent-order context and existing lines,
 *                  paged Product / Charge catalog search (50 rows / scroll),
 *                  the server-side line callout (price / tax / amount), product
 *                  attribute (M_AttributeSetInstance) read + create, barcode
 *                  scan lookup and the C_OrderLine insert / update / delete
 *                  write actions (always through the MOrderLine business class).
 * Chronological  : Development
 *   VAI154         Created  09-Jul-2026
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
    /// Purpose     : Data + write model behind the Create Order Bottom Panel.
    ///               Every SELECT is filtered through MRole.AddAccessSQL on the
    ///               main physical table alias only and uses bind parameters so
    ///               the same code runs on PostgreSQL and Oracle. All inserts go
    ///               through MOrderLine (never a hand-written INSERT) so the
    ///               standard order-line callouts (price / tax / amount) run.
    /// Chronological development:
    ///   VAI154         Created  09-Jul-2026
    /// </summary>
    public class VAS_107_CreateOrderBottomPanelModel
    {
        private static VLogger log = VLogger.GetVLogger(typeof(VAS_107_CreateOrderBottomPanelModel).FullName);

        /// <summary>First page size for the Product / Charge catalog search.</summary>
        private const int CATALOG_PAGE_SIZE = 50;

        /// <summary>Saved order lines loaded per page (server-side paging).</summary>
        private const int LINE_PAGE_SIZE = 20;

        #region Panel (read) data

        /// <summary>
        /// Builds the panel header context (everything the client-side callouts
        /// need from the parent order) plus the already-saved order lines.
        /// Returns an empty object (C_Order_ID = 0) when the role has no access.
        /// </summary>
        /// <param name="ctx">session context</param>
        /// <param name="C_Order_ID">parent order</param>
        /// <param name="AD_Window_ID">source window</param>
        /// <param name="page">0-based page of saved lines</param>
        /// <returns>panel view model</returns>
        public CreateOrderPanelData GetPanelData(Ctx ctx, int C_Order_ID, int AD_Window_ID, int page = 0)
        {
            CreateOrderPanelData data = new CreateOrderPanelData();
            if (C_Order_ID <= 0) return data;

            LoadParentContext(ctx, C_Order_ID, data);
            if (data.C_Order_ID <= 0) return data;   // no access / not found

            data.AD_Window_ID = AD_Window_ID;
            List<int> tabIds = ResolveOrderLineTabs(AD_Window_ID);
            data.AD_Tab_IDs = tabIds;
            data.AD_Tab_ID = tabIds.Count > 0 ? tabIds[0] : 0;

            if (page < 0) page = 0;
            int total;
            data.Lines = LoadLines(ctx, C_Order_ID, tabIds, page, out total);
            data.LinesTotal = total;
            data.LinePage = page;
            data.LinePageSize = LINE_PAGE_SIZE;
            decimal oNet, oTax, oTcs;
            ComputeOtherPageTotals(ctx, C_Order_ID, data.Lines, data.IsTaxIncluded, out oNet, out oTax, out oTcs);
            data.OtherPagesSubtotal = oNet; data.OtherPagesTax = oTax; data.OtherPagesTcs = oTcs;
            LoadCatalogs(ctx, data);
            LoadColumns(ctx, data, tabIds);
            LoadLoginContext(ctx, data);
            return data;
        }

        /// <summary>
        /// Collects the login / session context values for every @$Token@ / @#Token@
        /// referenced by any column's DisplayLogic or ReadOnlyLogic.
        /// </summary>
        private void LoadLoginContext(Ctx ctx, CreateOrderPanelData data)
        {
            Regex rx = new Regex(@"@([#$][A-Za-z0-9_]+)@");
            HashSet<string> tokens = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (OrderColumnMeta m in data.Columns)
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

        /// <summary>Loads C_OrderLine column metadata once per panel load.</summary>
        private void LoadColumns(Ctx ctx, CreateOrderPanelData data, List<int> AD_Tab_IDs)
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
        /// This handles the common case where the column exists physically on C_OrderLine but
        /// the AD_Column entry is absent or inactive (IsActive = 'N'). The lookup definition
        /// for each injected column is resolved at query time by <see cref="ResolveRefLookup"/>
        /// from the column-name TableDir convention (VAS_Opportunity_ID → VAS_Opportunity).
        /// </summary>
        /// <param name="data">Panel data being built; Columns list is mutated in-place.</param>
        private void InjectMissingVasColumns(CreateOrderPanelData data)
        {
            HashSet<string> have = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (OrderColumnMeta m in data.Columns) have.Add(m.ColumnName);

            foreach (var cand in _vasInjectColumns)
            {
                if (have.Contains(cand.Col)) continue;
                // Query AD_Column without IsActive filter — the column may be registered but
                // deactivated. We still want to surface it in the panel.
                int adColId = 0, refId = cand.RefId;
                DataSet ds = DB.ExecuteDataset(
                    @"SELECT c.AD_Column_ID, c.AD_Reference_ID
                      FROM AD_Column c INNER JOIN AD_Table t ON (c.AD_Table_ID = t.AD_Table_ID)
                      WHERE t.TableName = 'C_OrderLine' AND c.ColumnName = @col",
                    new SqlParameter[] { new SqlParameter("@col", cand.Col) }, null);
                if (ds == null || ds.Tables.Count == 0 || ds.Tables[0].Rows.Count == 0)
                    continue; // column absent from AD_Column entirely — physical column may not exist
                adColId = Util.GetValueOfInt(ds.Tables[0].Rows[0]["AD_Column_ID"]);
                int dbRefId = Util.GetValueOfInt(ds.Tables[0].Rows[0]["AD_Reference_ID"]);
                if (dbRefId > 0) refId = dbRefId;
                data.Columns.Add(new OrderColumnMeta
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
        private void LoadListValues(CreateOrderPanelData data)
        {
            foreach (OrderColumnMeta m in data.Columns)
            {
                if (m.AD_Reference_ID != 17 || m.AD_Reference_Value_ID <= 0) continue;
                DataSet ds = DB.ExecuteDataset(
                    @"SELECT Value, Name FROM AD_Ref_List
                      WHERE AD_Reference_ID = @ref AND IsActive = 'Y'
                      ORDER BY COALESCE(Name, Value)",
                    new SqlParameter[] { new SqlParameter("@ref", m.AD_Reference_Value_ID) }, null);
                if (ds == null || ds.Tables.Count == 0) continue;
                foreach (DataRow r in ds.Tables[0].Rows)
                    m.RefListValues.Add(new OrderRefListItem
                    {
                        Value = Util.GetValueOfString(r["Value"]),
                        Name = Util.GetValueOfString(r["Name"])
                    });
            }
        }

        private Dictionary<int, List<int>> _olTabsByWindow;

        /// <summary>Finds every active AD_Tab bound to C_OrderLine inside the given window.</summary>
        private List<int> ResolveOrderLineTabs(int AD_Window_ID)
        {
            if (_olTabsByWindow == null) _olTabsByWindow = new Dictionary<int, List<int>>();
            List<int> cached;
            if (_olTabsByWindow.TryGetValue(AD_Window_ID, out cached)) return cached;

            List<int> tabs = new List<int>();
            if (AD_Window_ID > 0)
            {
                int tableId = X_C_OrderLine.Table_ID;
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
            _olTabsByWindow[AD_Window_ID] = tabs;
            return tabs;
        }

        /// <summary>
        /// SELECT expression for ReadOnlyLogic giving AD_Field priority over AD_Column.
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
                                  AND tt2.TableName = 'C_OrderLine'
                                  AND f2.ReadOnlyLogic IS NOT NULL
                                  AND f2.ReadOnlyLogic <> ''), N''), ");
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
            "VAS_", "VIS_", "VA_", "VB_"
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
        private bool LoadColumnsFromTabs(Ctx ctx, CreateOrderPanelData data, List<int> AD_Tab_IDs)
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
                data.Columns.Add(MapOrderColumnMeta(r, true));
            }
            return true;
        }

        /// <summary>Merges every active C_OrderLine column not already loaded from the tab.</summary>
        private void MergeAllColumns(Ctx ctx, CreateOrderPanelData data)
        {
            HashSet<string> have = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (OrderColumnMeta cm in data.Columns) have.Add(cm.ColumnName);

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
                                                  AND tt2.TableName = 'C_OrderLine'
                                                  AND f2.DisplayLogic IS NOT NULL
                                              AND f2.DisplayLogic <> ''), N'') AS DisplayLogic
                           FROM AD_Column c
                           INNER JOIN AD_Table t ON (c.AD_Table_ID = t.AD_Table_ID)
                           LEFT JOIN AD_Val_Rule vr ON (c.AD_Val_Rule_ID = vr.AD_Val_Rule_ID
                                AND vr.IsActive = 'Y')
                           WHERE t.TableName = 'C_OrderLine'
                             AND c.IsActive = 'Y'";

            DataSet ds = DB.ExecuteDataset(sql);
            if (ds == null || ds.Tables.Count == 0) return;
            foreach (DataRow r in ds.Tables[0].Rows)
            {
                string colName = Util.GetValueOfString(r["ColumnName"]);
                if (have.Contains(colName)) continue;
                // Skip columns from optional modules that are not installed on this deployment.
                if (!IsColumnModuleInstalled(colName)) continue;
                data.Columns.Add(MapOrderColumnMeta(r, false));
            }
        }

        /// <summary>Maps a column-meta row.</summary>
        private OrderColumnMeta MapOrderColumnMeta(DataRow r, bool fromField)
        {
            OrderColumnMeta m = new OrderColumnMeta
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

        /// <summary>Loads UOM + tax dropdown catalogs using the order header context.</summary>
        private void LoadCatalogs(Ctx ctx, CreateOrderPanelData data)
        {
            data.UomList = LoadUomList(ctx, data.C_Order_ID, null);
            data.TaxList = LoadTaxList(ctx, data.C_Order_ID, null);
        }

        /// <summary>Builds the UOM dropdown list, enforcing the C_UOM_ID column's AD_Val_Rule.</summary>
        private List<OrderUomItem> LoadUomList(Ctx ctx, int C_Order_ID, Dictionary<string, string> rowVars)
        {
            List<OrderUomItem> list = new List<OrderUomItem>();
            string uomSql = @"SELECT u.C_UOM_ID, u.Name AS UOMName
                              FROM C_UOM u
                              WHERE u.IsActive = 'Y'";
            string uomPred = GetValRulePredicate(ctx, "C_UOM_ID", "C_UOM", "u", C_Order_ID, rowVars);
            if (uomPred.Length > 0) uomSql += " AND (" + uomPred + ")";
            uomSql += " ORDER BY u.Name";
            uomSql = MRole.GetDefault(ctx).AddAccessSQL(uomSql, "u", MRole.SQL_FULLYQUALIFIED, MRole.SQL_RO);
            DataSet uds = DB.ExecuteDataset(uomSql);
            if (uds != null && uds.Tables.Count > 0)
                foreach (DataRow r in uds.Tables[0].Rows)
                    list.Add(new OrderUomItem
                    {
                        C_UOM_ID = Util.GetValueOfInt(r["C_UOM_ID"]),
                        Name = Util.GetValueOfString(r["UOMName"])
                    });
            return list;
        }

        /// <summary>Builds the tax dropdown list (rate included), enforcing the C_Tax_ID column's AD_Val_Rule.</summary>
        private List<OrderTaxItem> LoadTaxList(Ctx ctx, int C_Order_ID, Dictionary<string, string> rowVars)
        {
            List<OrderTaxItem> list = new List<OrderTaxItem>();
            string taxSql = @"SELECT t.C_Tax_ID, t.Name, t.Rate
                              FROM C_Tax t
                              WHERE t.IsActive = 'Y'
                                AND COALESCE(t.IsSummary, 'N') = 'N'
                                AND t.AD_Client_ID IN (0, " + ctx.GetAD_Client_ID() + ")";
            string taxPred = GetValRulePredicate(ctx, "C_Tax_ID", "C_Tax", "t", C_Order_ID, rowVars);
            if (taxPred.Length > 0) taxSql += " AND (" + taxPred + ")";
            taxSql += " ORDER BY t.Rate, t.Name";
            taxSql = MRole.GetDefault(ctx).AddAccessSQL(taxSql, "t", MRole.SQL_FULLYQUALIFIED, MRole.SQL_RO);
            DataSet tds = DB.ExecuteDataset(taxSql);
            if (tds != null && tds.Tables.Count > 0)
                foreach (DataRow r in tds.Tables[0].Rows)
                    list.Add(new OrderTaxItem
                    {
                        C_Tax_ID = Util.GetValueOfInt(r["C_Tax_ID"]),
                        Name = Util.GetValueOfString(r["Name"]),
                        Rate = Util.GetValueOfDecimal(r["Rate"])
                    });
            return list;
        }

        /// <summary>
        /// Re-fetches the per-row filtered UOM + tax lists for one order line,
        /// honouring each column's AD_Val_Rule against the line's current values.
        /// </summary>
        public OrderLookupData GetLookupData(Ctx ctx, OrderLookupRequest req)
        {
            OrderLookupData data = new OrderLookupData();
            if (req == null || req.C_Order_ID <= 0) return data;

            CreateOrderPanelData parent = new CreateOrderPanelData();
            LoadParentContext(ctx, req.C_Order_ID, parent);
            if (parent.C_Order_ID <= 0) return data;

            data.C_Order_ID = req.C_Order_ID;
            Dictionary<string, string> rowVars = BuildRowVars(req.RowValues);
            data.UomList = LoadUomList(ctx, req.C_Order_ID, rowVars);
            data.TaxList = LoadTaxList(ctx, req.C_Order_ID, rowVars);
            return data;
        }

        /// <summary>
        /// Generic FK lookup for a dynamic C_OrderLine field (Table / TableDir / Search).
        /// </summary>
        public List<OrderRefItem> GetRefLookup(Ctx ctx, OrderRefLookupRequest req)
        {
            List<OrderRefItem> items = new List<OrderRefItem>();
            if (req == null || req.C_Order_ID <= 0 || string.IsNullOrEmpty(req.ColumnName)) return items;

            CreateOrderPanelData parent = new CreateOrderPanelData();
            LoadParentContext(ctx, req.C_Order_ID, parent);
            if (parent.C_Order_ID <= 0) return items;

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
                string pred = GetValRulePredicate(ctx, req.ColumnName, def.TableName, alias, req.C_Order_ID, rowVars);
                if (pred.Length > 0) sql.Append(" AND (").Append(pred).Append(")");
            }

            string secured = MRole.GetDefault(ctx).AddAccessSQL(
                sql.ToString(), alias, MRole.SQL_FULLYQUALIFIED, MRole.SQL_RO);
            if (req.Id <= 0) secured += " ORDER BY " + dispExpr + PagingSuffix(pageSize, offset);

            DataSet ds = DB.ExecuteDataset(secured, ps.ToArray(), null);
            if (ds == null || ds.Tables.Count == 0) return items;
            foreach (DataRow r in ds.Tables[0].Rows)
                items.Add(new OrderRefItem { Id = Util.GetValueOfInt(r["Id"]), Name = Util.GetValueOfString(r["Name"]) });
            return items;
        }

        /// <summary>Non-standard TableDir FK columns whose lookup table/key is not the column name minus "_ID".</summary>
        private static readonly Dictionary<string, string[]> TableDirOverrides =
            new Dictionary<string, string[]>(StringComparer.OrdinalIgnoreCase)
            {
                { "AD_OrgTrx_ID", new string[] { "AD_Org", "AD_Org_ID" } }
            };

        private Dictionary<string, RefLookupDef> _refDefByColumn;

        /// <summary>Resolves a C_OrderLine FK column to its lookup table, key and display expression.</summary>
        private RefLookupDef ResolveRefLookup(string columnName)
        {
            if (_refDefByColumn == null) _refDefByColumn = new Dictionary<string, RefLookupDef>(StringComparer.OrdinalIgnoreCase);
            RefLookupDef cached;
            if (_refDefByColumn.TryGetValue(columnName, out cached)) return cached;

            RefLookupDef def = null;
            DataSet rs = DB.ExecuteDataset(
                @"SELECT c.AD_Reference_ID, COALESCE(c.AD_Reference_Value_ID, 0) AS AD_Reference_Value_ID
                  FROM AD_Column c INNER JOIN AD_Table t ON (c.AD_Table_ID = t.AD_Table_ID)
                  WHERE t.TableName = 'C_OrderLine' AND c.ColumnName = @c AND c.IsActive = 'Y'",
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
                    def = new RefLookupDef { TableName = table, KeyColumn = key, DisplayExpr = display, HasIsActive = true, HasClientId = true };
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
                def = new RefLookupDef { TableName = tbl, KeyColumn = kc, DisplayExpr = BuildIdentifierExpr(tbl), HasIsActive = true, HasClientId = true };
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

        /// <summary>Loads the parent order header values used as callout context.</summary>
        private void LoadParentContext(Ctx ctx, int C_Order_ID, CreateOrderPanelData data)
        {
            string sql = @"SELECT
                              o.C_Order_ID,
                              o.AD_Client_ID,
                              o.AD_Org_ID,
                              o.C_BPartner_ID,
                              o.C_BPartner_Location_ID,
                              o.M_PriceList_ID,
                              o.C_Currency_ID,
                              o.DateOrdered,
                              o.DateAcct,
                              o.IsSOTrx,
                              COALESCE(pl.IsTaxIncluded, 'N') AS IsTaxIncluded,
                              o.DocStatus,
                              COALESCE(o.Processed, 'N') AS Processed,
                              cur.StdPrecision  AS StdPrecision,
                              cur.CurSymbol     AS CurrencySymbol,
                              cur.ISO_Code      AS CurrencyISOCode
                           FROM C_Order o
                           INNER JOIN C_Currency cur ON (o.C_Currency_ID = cur.C_Currency_ID)
                           INNER JOIN M_PriceList pl ON (o.M_PriceList_ID = pl.M_PriceList_ID)
                           WHERE o.C_Order_ID = @C_Order_ID
                             AND o.IsActive = 'Y'";

            sql = MRole.GetDefault(ctx).AddAccessSQL(sql, "o", MRole.SQL_FULLYQUALIFIED, MRole.SQL_RO);

            DataSet ds = DB.ExecuteDataset(sql,
                new SqlParameter[] { new SqlParameter("@C_Order_ID", C_Order_ID) }, null);
            if (ds == null || ds.Tables.Count == 0 || ds.Tables[0].Rows.Count == 0) return;

            DataRow r = ds.Tables[0].Rows[0];
            data.C_Order_ID = Util.GetValueOfInt(r["C_Order_ID"]);
            data.AD_Client_ID = Util.GetValueOfInt(r["AD_Client_ID"]);
            data.AD_Org_ID = Util.GetValueOfInt(r["AD_Org_ID"]);
            data.C_BPartner_ID = Util.GetValueOfInt(r["C_BPartner_ID"]);
            data.C_BPartner_Location_ID = Util.GetValueOfInt(r["C_BPartner_Location_ID"]);
            data.M_PriceList_ID = Util.GetValueOfInt(r["M_PriceList_ID"]);
            data.C_Currency_ID = Util.GetValueOfInt(r["C_Currency_ID"]);
            data.DateOrdered = Util.GetValueOfDateTime(r["DateOrdered"]);
            data.DateAcct = Util.GetValueOfDateTime(r["DateAcct"]);
            data.IsSOTrx = Util.GetValueOfString(r["IsSOTrx"]) == "Y";
            data.IsTaxIncluded = Util.GetValueOfString(r["IsTaxIncluded"]) == "Y";
            data.DocStatus = Util.GetValueOfString(r["DocStatus"]);
            data.Processed = Util.GetValueOfString(r["Processed"]) == "Y";
            data.StdPrecision = Util.GetValueOfInt(r["StdPrecision"]);
            data.CurSymbol = Util.GetValueOfString(r["CurrencySymbol"]);
            data.CurISO = Util.GetValueOfString(r["CurrencyISOCode"]);
            data.IsEditable = !data.Processed
                && data.DocStatus != "CO" && data.DocStatus != "CL"
                && data.DocStatus != "VO" && data.DocStatus != "RE";
        }

        /// <summary>Loads the order lines saved against the parent order.</summary>
        private List<OrderLineRow> LoadLines(Ctx ctx, int C_Order_ID, List<int> AD_Tab_IDs, int page, out int total)
        {
            List<OrderLineRow> rows = new List<OrderLineRow>();

            string countSql = "SELECT COUNT(*) FROM C_OrderLine ol WHERE ol.C_Order_ID = @C_Order_ID AND ol.IsActive = 'Y'";
            countSql = MRole.GetDefault(ctx).AddAccessSQL(countSql, "ol", MRole.SQL_FULLYQUALIFIED, MRole.SQL_RO);
            total = Util.GetValueOfInt(DB.ExecuteScalar(countSql,
                new SqlParameter[] { new SqlParameter("@C_Order_ID", C_Order_ID) }, null));

            StringBuilder cols = new StringBuilder();
            foreach (string cn in GetLineProjectionColumns(AD_Tab_IDs))
                cols.Append("ol.").Append(cn).Append(", ");
            if (cols.Length == 0)
                cols.Append("ol.C_OrderLine_ID, ol.Line, ol.M_Product_ID, ol.C_Charge_ID, ol.QtyEntered, ol.QtyOrdered, ol.C_UOM_ID, ol.PriceEntered, ol.C_Tax_ID, ol.TaxAmt, ol.TaxableAmt, ol.LineNetAmt, ol.LineTotalAmt, ol.M_AttributeSetInstance_ID, ol.Description, ");

            string sql = "SELECT " + cols.ToString() +
                @"COALESCE(p.Name, N'') AS VASOLDISP_ProductName,
                  COALESCE(ch.Name, N'') AS VASOLDISP_ChargeName,
                  COALESCE(uom.Name, N'') AS VASOLDISP_UOMName,
                  COALESCE(t.Name, N'') AS VASOLDISP_TaxName,
                  COALESCE(asi.Description, N'') AS VASOLDISP_AttrName,
                  COALESCE(p.M_AttributeSet_ID, 0) AS VASOLDISP_HasAttrSet,
                  COALESCE(p.ProductType, '') AS VASOLDISP_ProductType
               FROM C_OrderLine ol
               LEFT JOIN M_Product p ON (ol.M_Product_ID = p.M_Product_ID)
               LEFT JOIN C_Charge ch ON (ol.C_Charge_ID = ch.C_Charge_ID)
               LEFT JOIN C_UOM uom ON (ol.C_UOM_ID = uom.C_UOM_ID)
               LEFT JOIN C_Tax t ON (ol.C_Tax_ID = t.C_Tax_ID)
               LEFT JOIN M_AttributeSetInstance asi ON (ol.M_AttributeSetInstance_ID = asi.M_AttributeSetInstance_ID)
               WHERE ol.C_Order_ID = @C_Order_ID
                 AND ol.IsActive = 'Y'";

            sql = MRole.GetDefault(ctx).AddAccessSQL(sql, "ol", MRole.SQL_FULLYQUALIFIED, MRole.SQL_RO);
            if (page < 0) page = 0;
            sql += " ORDER BY ol.Line" + PagingSuffix(LINE_PAGE_SIZE, page * LINE_PAGE_SIZE);

            DataSet ds = DB.ExecuteDataset(sql,
                new SqlParameter[] { new SqlParameter("@C_Order_ID", C_Order_ID) }, null);
            if (ds == null || ds.Tables.Count == 0) return rows;

            DataTable dt = ds.Tables[0];
            foreach (DataRow r in dt.Rows)
            {
                OrderLineRow row = new OrderLineRow();
                foreach (DataColumn dc in dt.Columns)
                {
                    if (dc.ColumnName.StartsWith("VASOLDISP_")) continue;
                    row.Values[dc.ColumnName] = (r[dc] == DBNull.Value) ? null : r[dc];
                }
                row.C_OrderLine_ID = Util.GetValueOfInt(r["C_OrderLine_ID"]);
                row.Line = Util.GetValueOfInt(r["Line"]);
                row.M_Product_ID = Util.GetValueOfInt(r["M_Product_ID"]);
                row.ProductName = Util.GetValueOfString(r["VASOLDISP_ProductName"]);
                row.C_Charge_ID = Util.GetValueOfInt(r["C_Charge_ID"]);
                row.ChargeName = Util.GetValueOfString(r["VASOLDISP_ChargeName"]);
                row.Description = Util.GetValueOfString(r["Description"]);
                row.QtyOrdered = Util.GetValueOfDecimal(r["QtyOrdered"]);
                row.QtyEntered = dt.Columns.Contains("QtyEntered") ? Util.GetValueOfDecimal(r["QtyEntered"]) : row.QtyOrdered;
                row.C_UOM_ID = Util.GetValueOfInt(r["C_UOM_ID"]);
                row.UOMName = Util.GetValueOfString(r["VASOLDISP_UOMName"]);
                row.PriceEntered = Util.GetValueOfDecimal(r["PriceEntered"]);
                row.C_Tax_ID = Util.GetValueOfInt(r["C_Tax_ID"]);
                row.TaxName = Util.GetValueOfString(r["VASOLDISP_TaxName"]);
                row.TaxAmt = Util.GetValueOfDecimal(r["TaxAmt"]);
                row.TaxableAmt = Util.GetValueOfDecimal(r["TaxableAmt"]);
                row.LineNetAmt = Util.GetValueOfDecimal(r["LineNetAmt"]);
                row.LineTotalAmt = Util.GetValueOfDecimal(r["LineTotalAmt"]);
                row.M_AttributeSetInstance_ID = Util.GetValueOfInt(r["M_AttributeSetInstance_ID"]);
                row.AttrName = Util.GetValueOfString(r["VASOLDISP_AttrName"]);
                row.HasAttributeSet = Util.GetValueOfInt(r["VASOLDISP_HasAttrSet"]) > 0;
                row.ProductType = Util.GetValueOfString(r["VASOLDISP_ProductType"]);
                rows.Add(row);
            }
            return rows;
        }

        /// <summary>
        /// Sums saved-line amounts across the whole order.
        /// Sub Total (Net) is derived from LineNetAmt rather than TaxBaseAmt: for
        /// tax-inclusive orders the extracted tax (TaxAmt + SurchargeAmt) is subtracted
        /// to give the true taxable base; for tax-exclusive orders LineNetAmt IS the
        /// taxable base. This avoids relying on the stored TaxBaseAmt column, which
        /// may not be registered in AD_Column and can hold stale / incorrect values.
        /// </summary>
        /// <param name="ctx">session context</param>
        /// <param name="C_Order_ID">parent order</param>
        /// <param name="taxIncluded">true when the price list has IsTaxIncluded = Y</param>
        /// <param name="net">output: order sub-total (taxable base)</param>
        /// <param name="tax">output: order total tax (TaxAmt + SurchargeAmt)</param>
        /// <param name="tcs">output: order total TCS (VA106)</param>
        private void LoadGrandTotals(Ctx ctx, int C_Order_ID, bool taxIncluded, out decimal net, out decimal tax, out decimal tcs)
        {
            net = 0; tax = 0; tcs = 0;
            string tcsExpr = ColumnExists("C_OrderLine", "VA106_TCSAmount")
                ? "COALESCE(SUM(ol.VA106_TCSAmount), 0)" : "0";
            string surExpr = " + COALESCE(SUM(ol.SurchargeAmt), 0)";
            // For tax-inclusive orders: taxable base = LineNetAmt - TaxAmt - SurchargeAmt.
            // For tax-exclusive orders: taxable base = LineNetAmt (tax is additive, not extracted).
            string netExpr = taxIncluded
                ? "COALESCE(SUM(ol.LineNetAmt - ol.TaxAmt - COALESCE(ol.SurchargeAmt, 0)), 0)"
                : "COALESCE(SUM(ol.LineNetAmt), 0)";
            string sql = "SELECT " + netExpr + " AS Net, COALESCE(SUM(ol.TaxAmt), 0)" + surExpr + " AS Tax, "
                + tcsExpr + " AS Tcs"
                + " FROM C_OrderLine ol WHERE ol.C_Order_ID = @C_Order_ID AND ol.IsActive = 'Y'";
            sql = MRole.GetDefault(ctx).AddAccessSQL(sql, "ol", MRole.SQL_FULLYQUALIFIED, MRole.SQL_RO);
            DataSet ds = DB.ExecuteDataset(sql,
                new SqlParameter[] { new SqlParameter("@C_Order_ID", C_Order_ID) }, null);
            if (ds != null && ds.Tables.Count > 0 && ds.Tables[0].Rows.Count > 0)
            {
                DataRow r = ds.Tables[0].Rows[0];
                net = Util.GetValueOfDecimal(r["Net"]);
                tax = Util.GetValueOfDecimal(r["Tax"]);
                tcs = Util.GetValueOfDecimal(r["Tcs"]);
            }
        }

        /// <summary>Computes totals of every saved line NOT on the current page.</summary>
        /// <param name="ctx">session context</param>
        /// <param name="C_Order_ID">parent order</param>
        /// <param name="pageRows">lines loaded for the current page</param>
        /// <param name="taxIncluded">true when the price list has IsTaxIncluded = Y</param>
        /// <param name="otherNet">output: sub-total for lines NOT on current page</param>
        /// <param name="otherTax">output: tax for lines NOT on current page</param>
        /// <param name="otherTcs">output: TCS for lines NOT on current page</param>
        private void ComputeOtherPageTotals(Ctx ctx, int C_Order_ID, List<OrderLineRow> pageRows,
            bool taxIncluded, out decimal otherNet, out decimal otherTax, out decimal otherTcs)
        {
            decimal gNet, gTax, gTcs;
            LoadGrandTotals(ctx, C_Order_ID, taxIncluded, out gNet, out gTax, out gTcs);
            decimal pNet = 0, pTax = 0, pTcs = 0;
            if (pageRows != null)
                foreach (OrderLineRow row in pageRows)
                {
                    // Mirror the LoadGrandTotals formula so grand - page = other-pages.
                    // Tax-inclusive: taxable base = LineNetAmt - TaxAmt - SurchargeAmt.
                    // Tax-exclusive: taxable base = LineNetAmt.
                    decimal surcharge = RowOrderDecimal(row, "SurchargeAmt");
                    pNet += taxIncluded ? row.LineNetAmt - row.TaxAmt - surcharge : row.LineNetAmt;
                    pTax += row.TaxAmt + surcharge;
                    pTcs += RowOrderTcs(row);
                }
            otherNet = gNet - pNet;
            otherTax = gTax - pTax;
            otherTcs = gTcs - pTcs;
        }

        /// <summary>
        /// Returns the aggregated per-tax breakdown for the whole order from C_OrderTax.
        /// C_OrderTax is updated by the framework whenever a C_OrderLine is saved, so
        /// this reflects every saved line across all pages — not just the loaded page.
        /// The client uses this to display correct individual tax rows in the totals footer.
        /// </summary>
        /// <param name="ctx">session context</param>
        /// <param name="C_Order_ID">parent sales order</param>
        /// <returns>one item per tax plus header totals; empty list when no rows exist yet</returns>
        public List<OrderTaxBreakdownItem> LoadOrderTaxBreakdown(Ctx ctx, int C_Order_ID)
        {
            List<OrderTaxBreakdownItem> result = new List<OrderTaxBreakdownItem>();
            if (C_Order_ID <= 0) return result;

            // C_Order is the leading (FROM) table so AddAccessSQL correctly identifies
            // alias "co" as the main table and injects the security predicates.
            // Previously C_OrderTax was first in FROM but alias "co" was passed to
            // AddAccessSQL — the AccessSqlParser mismatch logged a SEVERE warning and
            // skipped all security predicates.
            //
            // VA106_TCSTotalAmount is intentionally excluded from this query.
            // Env.IsModuleInstalled checks the module registry which can be populated
            // before the column migration runs, so the column may be absent on C_Order
            // even when the check returns true. TCS is read from C_OrderLine.VA106_TCSAmount
            // in the second query below where it is reliably present when VA106 is installed.
            // Sub Total must be the taxable base (net of tax), not the gross LineNetAmt stored in
            // C_Order.TotalLines. For a tax-inclusive price list, TotalLines = sum of LineNetAmt
            // which embeds the tax, so it is LARGER than the taxable base. Using SUM(TaxableAmt)
            // from C_OrderLine mirrors VAS_074's SUM(TaxBaseAmt) pattern and is correct for both
            // tax-inclusive and tax-exclusive price lists.
            string sql = @"SELECT t.Name AS TaxName, ot.TaxAmt, ot.TaxBaseAmt,
                                  (SELECT COALESCE(SUM(ol2.TaxableAmt), 0) FROM C_OrderLine ol2
                                   WHERE ol2.C_Order_ID = co.C_Order_ID AND ol2.IsActive = 'Y') AS TotalLines,
                                  co.GrandTotal, cy.CurSymbol, cy.StdPrecision
                           FROM C_Order co
                           INNER JOIN C_OrderTax ot ON (ot.C_Order_ID = co.C_Order_ID)
                           INNER JOIN C_Tax t ON (t.C_Tax_ID = ot.C_Tax_ID)
                           INNER JOIN C_Currency cy ON (cy.C_Currency_ID = co.C_Currency_ID)
                           WHERE co.C_Order_ID = @C_Order_ID
                           AND ot.IsActive = 'Y'
                           ORDER BY t.Name";
            sql = MRole.GetDefault(ctx).AddAccessSQL(sql, "co", MRole.SQL_FULLYQUALIFIED, MRole.SQL_RO);
            DataSet ds = DB.ExecuteDataset(sql,
                new SqlParameter[] { new SqlParameter("@C_Order_ID", C_Order_ID) }, null);
            if (ds == null || ds.Tables.Count == 0) return result;
            foreach (DataRow r in ds.Tables[0].Rows)
            {
                result.Add(new OrderTaxBreakdownItem
                {
                    TaxName      = Util.GetValueOfString(r["TaxName"]),
                    TaxAmt       = Util.GetValueOfDecimal(r["TaxAmt"]),
                    TaxBaseAmt   = Util.GetValueOfDecimal(r["TaxBaseAmt"]),
                    TotalLines   = Util.GetValueOfDecimal(r["TotalLines"]),
                    GrandTotal   = Util.GetValueOfDecimal(r["GrandTotal"]),
                    CurSymbol    = Util.GetValueOfString(r["CurSymbol"]),
                    StdPrecision = Util.GetValueOfInt(r["StdPrecision"])
                });
            }

            // SurchargeAmt and VA106_TCSAmount live on C_OrderLine, not C_OrderTax.
            // Fetch both in one round-trip and store on result[0] so the client can
            // render them as separate rows in the totals footer.
            if (result.Count > 0)
            {
                bool hasTcs = Env.IsModuleInstalled("VA106_");
                string tcsCol = hasTcs ? ", COALESCE(SUM(ol.VA106_TCSAmount), 0) AS TotalTCS" : "";
                string surgSql = @"SELECT COALESCE(SUM(ol.SurchargeAmt), 0) AS TotalSurcharge"
                                 + tcsCol +
                                 @" FROM C_OrderLine ol
                                    WHERE ol.C_Order_ID = @C_Order_ID AND ol.IsActive = 'Y'";
                surgSql = MRole.GetDefault(ctx).AddAccessSQL(surgSql, "ol", MRole.SQL_FULLYQUALIFIED, MRole.SQL_RO);
                DataSet ds2 = DB.ExecuteDataset(surgSql,
                    new SqlParameter[] { new SqlParameter("@C_Order_ID", C_Order_ID) }, null);
                if (ds2 != null && ds2.Tables.Count > 0 && ds2.Tables[0].Rows.Count > 0)
                {
                    DataRow r2 = ds2.Tables[0].Rows[0];
                    result[0].TotalSurcharge = Util.GetValueOfDecimal(r2["TotalSurcharge"]);
                    if (hasTcs)
                        result[0].TCSAmount = Util.GetValueOfDecimal(r2["TotalTCS"]);
                }
            }

            return result;
        }

        private static decimal RowOrderTcs(OrderLineRow row)
        {
            return RowOrderDecimal(row, "VA106_TCSAmount");
        }

        private static decimal RowOrderDecimal(OrderLineRow row, string columnName)
        {
            if (row == null || row.Values == null) return 0;
            foreach (KeyValuePair<string, object> kv in row.Values)
                if (string.Equals(kv.Key, columnName, StringComparison.OrdinalIgnoreCase))
                    return Util.GetValueOfDecimal(kv.Value);
            return 0;
        }

        private List<string> _olColumns;

        /// <summary>Returns (and caches) the active C_OrderLine column names from the dictionary.</summary>
        private List<string> GetOrderLineColumns()
        {
            if (_olColumns != null) return _olColumns;
            _olColumns = new List<string>();
            DataSet ds = DB.ExecuteDataset(
                @"SELECT c.ColumnName
                  FROM AD_Column c
                  INNER JOIN AD_Table t ON (c.AD_Table_ID = t.AD_Table_ID)
                  WHERE t.TableName = 'C_OrderLine' AND c.IsActive = 'Y' AND c.ColumnSQL IS NULL ");
            if (ds != null && ds.Tables.Count > 0)
                foreach (DataRow r in ds.Tables[0].Rows)
                    _olColumns.Add(Util.GetValueOfString(r["ColumnName"]));
            return _olColumns;
        }

        private static readonly string[] ESSENTIAL_LINE_COLUMNS = new string[]
        {
            "C_OrderLine_ID", "C_Order_ID", "Line", "M_Product_ID", "C_Charge_ID",
            "QtyEntered", "QtyOrdered", "C_UOM_ID", "PriceEntered", "C_Tax_ID", "TaxAmt",
            "TaxableAmt", "LineNetAmt", "LineTotalAmt", "M_AttributeSetInstance_ID", "Description"
        };

        /// <summary>Builds the C_OrderLine column projection for LoadLines.</summary>
        private List<string> GetLineProjectionColumns(List<int> AD_Tab_IDs)
        {
            return GetOrderLineColumns();
        }

        private Dictionary<int, List<string>> _tabLineColumns;

        /// <summary>Returns (and caches) the C_OrderLine column names for a given tab.</summary>
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
        public List<OrderCatalogItem> SearchProductsCharges(Ctx ctx, int C_Order_ID,
            string query, int pageSize, int offset, Dictionary<string, object> rowValues = null)
        {
            List<OrderCatalogItem> items = new List<OrderCatalogItem>();
            if (C_Order_ID <= 0) return items;

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
            string prodPred = GetValRulePredicate(ctx, "M_Product_ID", "M_Product", "p", C_Order_ID, rowVars);
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
            string chargePred = GetValRulePredicate(ctx, "C_Charge_ID", "C_Charge", "ch", C_Order_ID, rowVars);
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
                log.Severe("VAS_107 SearchProductsCharges SQL failed. Term: " + like);
                return items;
            }

            foreach (DataRow r in ds.Tables[0].Rows)
            {
                OrderCatalogItem it = new OrderCatalogItem();
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
        public OrderCatalogItem ScanLookup(Ctx ctx, int C_Order_ID, string code)
        {
            OrderCatalogItem none = new OrderCatalogItem();
            if (C_Order_ID <= 0 || string.IsNullOrEmpty(code)) return none;
            string key = code.Trim();

            string prodSql = @"SELECT p.M_Product_ID AS RecordId, 'P' AS Kind, p.Value AS SearchKey,
                                      p.Name AS DisplayName, COALESCE(p.Description, N'') AS Description,
                                      p.M_AttributeSet_ID AS AttributeSetId,
                                      COALESCE(p.ProductType, '') AS ProductType
                               FROM M_Product p
                               WHERE p.IsActive = 'Y'
                                 AND p.AD_Client_ID = " + ctx.GetAD_Client_ID() + @"
                                 AND (UPPER(p.UPC) = UPPER(@code) OR UPPER(p.Value) = UPPER(@code))";
            string scanProdPred = GetValRulePredicate(ctx, "M_Product_ID", "M_Product", "p", C_Order_ID);
            if (scanProdPred.Length > 0) prodSql += " AND (" + scanProdPred + ")";
            prodSql = MRole.GetDefault(ctx).AddAccessSQL(prodSql, "p", MRole.SQL_FULLYQUALIFIED, MRole.SQL_RO);

            DataSet ds = DB.ExecuteDataset(prodSql, new SqlParameter[] { new SqlParameter("@code", key) }, null);
            if (ds != null && ds.Tables.Count > 0 && ds.Tables[0].Rows.Count > 0)
            {
                DataRow r = ds.Tables[0].Rows[0];
                OrderCatalogItem it = new OrderCatalogItem();
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
            string scanChargePred = GetValRulePredicate(ctx, "C_Charge_ID", "C_Charge", "ch", C_Order_ID);
            if (scanChargePred.Length > 0) chargeSql += " AND (" + scanChargePred + ")";
            chargeSql = MRole.GetDefault(ctx).AddAccessSQL(chargeSql, "ch", MRole.SQL_FULLYQUALIFIED, MRole.SQL_RO);
            ds = DB.ExecuteDataset(chargeSql, new SqlParameter[] { new SqlParameter("@code", key) }, null);
            if (ds != null && ds.Tables.Count > 0 && ds.Tables[0].Rows.Count > 0)
            {
                DataRow r = ds.Tables[0].Rows[0];
                OrderCatalogItem it = new OrderCatalogItem();
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
        private Dictionary<string, string> _orderVars;

        /// <summary>Returns the SQL validation code linked to a C_OrderLine lookup column.</summary>
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
                  WHERE t.TableName = 'C_OrderLine'
                    AND c.ColumnName = @c
                    AND c.IsActive = 'Y'
                    AND vr.IsActive = 'Y'
                    AND vr.Type = 'S'",
                new SqlParameter[] { new SqlParameter("@c", columnName) }, null);
            string code = Util.GetValueOfString(o);
            _valRuleByColumn[columnName] = code;
            return code;
        }

        private string GetValRulePredicate(Ctx ctx, string columnName, string tableName, string alias, int C_Order_ID)
        {
            return GetValRulePredicate(ctx, columnName, tableName, alias, C_Order_ID, null);
        }

        private string GetValRulePredicate(Ctx ctx, string columnName, string tableName, string alias,
            int C_Order_ID, Dictionary<string, string> rowVars)
        {
            string code = GetColumnValRule(columnName);
            if (string.IsNullOrEmpty(code)) return "";

            string frag = Regex.Replace(code, @"\b" + Regex.Escape(tableName) + @"\.", alias + ".", RegexOptions.IgnoreCase);

            Dictionary<string, string> oVars = GetOrderVars(ctx, C_Order_ID);
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
                log.Warning("VAS_107 val rule skipped for " + columnName + " (unresolved context): " + code);
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

        /// <summary>Parent-order context values (as SQL literals) for val-rule substitution.</summary>
        private Dictionary<string, string> GetOrderVars(Ctx ctx, int C_Order_ID)
        {
            if (_orderVars != null) return _orderVars;
            _orderVars = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            DataSet ds = DB.ExecuteDataset(
                @"SELECT AD_Client_ID, AD_Org_ID, C_BPartner_ID, C_BPartner_Location_ID,
                         M_PriceList_ID, C_Currency_ID, COALESCE(IsSOTrx, 'N') AS IsSOTrx
                  FROM C_Order WHERE C_Order_ID = @id",
                new SqlParameter[] { new SqlParameter("@id", C_Order_ID) }, null);
            if (ds != null && ds.Tables.Count > 0 && ds.Tables[0].Rows.Count > 0)
            {
                DataRow r = ds.Tables[0].Rows[0];
                _orderVars["AD_Client_ID"] = Util.GetValueOfInt(r["AD_Client_ID"]).ToString();
                _orderVars["AD_Org_ID"] = Util.GetValueOfInt(r["AD_Org_ID"]).ToString();
                _orderVars["C_BPartner_ID"] = Util.GetValueOfInt(r["C_BPartner_ID"]).ToString();
                _orderVars["C_BPartner_Location_ID"] = Util.GetValueOfInt(r["C_BPartner_Location_ID"]).ToString();
                _orderVars["M_PriceList_ID"] = Util.GetValueOfInt(r["M_PriceList_ID"]).ToString();
                _orderVars["C_Currency_ID"] = Util.GetValueOfInt(r["C_Currency_ID"]).ToString();
                _orderVars["C_Order_ID"] = C_Order_ID.ToString();
                _orderVars["IsSOTrx"] = "'" + (Util.GetValueOfString(r["IsSOTrx"]) == "Y" ? "Y" : "N") + "'";
            }
            return _orderVars;
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

        #region Line callout (server-side price / tax / amount)

        /// <summary>
        /// Server-side replacement for the C_OrderLine callout chain. Builds a
        /// transient MOrderLine, applies product / charge / qty / price / tax and
        /// returns the framework-computed values. No row is written.
        /// </summary>
        public OrderLineCalcResult CalcLine(Ctx ctx, OrderLineCalcRequest req)
        {
            OrderLineCalcResult res = new OrderLineCalcResult();
            if (req == null || req.C_Order_ID <= 0) return res;

            MOrder order;
            MOrderLine line = BuildCalcLine(ctx, req, out order);
            if (line == null || (req.M_Product_ID <= 0 && req.C_Charge_ID <= 0)) return res;

            res.C_UOM_ID = line.GetC_UOM_ID();
            res.C_Tax_ID = line.GetC_Tax_ID();
            res.PriceEntered = line.GetPriceEntered();
            res.LineNetAmt = line.GetLineNetAmt();
            res.TaxAmt = line.GetTaxAmt();
            res.LineTotalAmt = decimal.Add(res.LineNetAmt, res.TaxAmt);
            res.QtyOrdered = req.QtyOrdered > 0 ? req.QtyOrdered : 1;
            res.UOMName = GetUomLabel(ctx, res.C_UOM_ID);
            res.TaxName = res.C_Tax_ID > 0 ? MTax.Get(ctx, res.C_Tax_ID).GetName() : "";
            return res;
        }

        /// <summary>
        /// Builds a transient MOrderLine and applies the product / charge / qty / price / tax
        /// the same way the standard C_OrderLine callouts do. Shared by CalcLine and RunColumnCallout.
        /// </summary>
        private MOrderLine BuildCalcLine(Ctx ctx, OrderLineCalcRequest req, out MOrder order)
        {
            order = new MOrder(ctx, req.C_Order_ID, null);
            if (order.Get_ID() <= 0) return null;

            MOrderLine line = new MOrderLine(order);
            if (req.M_Product_ID > 0)
            {
                line.SetM_Product_ID(req.M_Product_ID, true);
                if (req.M_AttributeSetInstance_ID > 0)
                    line.SetM_AttributeSetInstance_ID(req.M_AttributeSetInstance_ID);

                // On a sales order, when the client has not yet supplied a UOM (fresh product
                // selection), prefer the product's Sales UOM (VAS_SalesUOM_Id) over the primary
                // unit set by SetM_Product_ID. A UOM already on the line (req.C_UOM_ID > 0)
                // means the user changed it deliberately — that is preserved below.
                if (req.C_UOM_ID <= 0 && order.IsSOTrx())
                {
                    int salesUomId = GetProductSalesUomId(ctx, req.M_Product_ID);
                    if (salesUomId > 0)
                        line.SetC_UOM_ID(salesUomId);
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

            // Prefer QtyEntered (the user-visible quantity in the entered UOM). Fall back to
            // QtyOrdered when QtyEntered was not supplied by the client (older callers).
            decimal qty = req.QtyEntered > 0 ? req.QtyEntered : (req.QtyOrdered > 0 ? req.QtyOrdered : 1);
            line.SetQty(qty);

            if (req.C_UOM_ID > 0)
                line.SetC_UOM_ID(req.C_UOM_ID);

            // MOrderLine.SetC_Charge_ID does not auto-set a UOM (unlike MInvoiceLine).
            // Apply the system default so C_UOM_ID is never 0 on a charge line.
            if (req.C_Charge_ID > 0 && line.GetC_UOM_ID() <= 0)
                line.SetC_UOM_ID(GetDefaultUomId(ctx));

            if (req.PriceOverride)
            {
                line.SetPriceEntered(req.PriceEntered);
                line.SetPriceActual(req.PriceEntered);
            }
            else if (req.M_Product_ID > 0)
            {
                SetLinePriceWithAttribute(line, order, req.M_AttributeSetInstance_ID);
            }
            else
            {
                line.SetPriceEntered(req.PriceEntered);
                line.SetPriceActual(req.PriceEntered);
            }

            ApplyDiscount(line, req.Discount);

            if (req.C_Tax_ID > 0)
                line.SetC_Tax_ID(req.C_Tax_ID);
            else
                line.SetTax();

            line.SetLineNetAmt();
            if (line.GetC_Tax_ID() > 0)
                line.SetTaxAmt();

            return line;
        }

        /// <summary>
        /// Prices a product line from the price list using the actual attribute-set-instance.
        /// </summary>
        private void SetLinePriceWithAttribute(MOrderLine line, MOrder order, int asi)
        {
            if (line.GetM_Product_ID() == 0) return;
            MProductPricing pp = new MProductPricing(line.GetAD_Client_ID(), line.GetAD_Org_ID(),
                line.GetM_Product_ID(), order.GetC_BPartner_ID(), line.GetQtyOrdered(), order.IsSOTrx());
            pp.SetM_PriceList_ID(order.GetM_PriceList_ID());
            pp.SetPriceDate(order.GetDateOrdered());
            pp.SetM_AttributeSetInstance_ID(asi);
            if (Env.IsModuleInstalled("ED011_"))
                pp.SetC_UOM_ID(line.GetC_UOM_ID());
            line.SetPriceActual(pp.GetPriceStd());
            line.SetPriceList(pp.GetPriceList());
            line.SetPriceLimit(pp.GetPriceLimit());
            if (decimal.Compare(line.GetQtyEntered(), line.GetQtyOrdered()) == 0)
                line.SetPriceEntered(line.GetPriceActual());
            else
                line.SetPriceEntered(decimal.Multiply(line.GetPriceActual(),
                    decimal.Round(decimal.Divide(line.GetQtyOrdered(), line.GetQtyEntered()), 6)));
            if (line.GetC_UOM_ID() == 0)
                line.SetC_UOM_ID(pp.GetC_UOM_ID());
        }

        /// <summary>
        /// Reads the AD_Column.Callout for the changed column and executes the equivalent
        /// server-side callout logic, returning the changed columns as a patch object.
        /// </summary>
        public OrderCalloutResult RunColumnCallout(Ctx ctx, OrderLineCalcRequest req)
        {
            OrderCalloutResult res = new OrderCalloutResult();
            if (req == null || req.C_Order_ID <= 0) return res;

            string column = MapTriggerToColumn(req.TriggerColumn);
            res.Column = column;
            res.Callout = ReadColumnCallout(ctx, "C_OrderLine", column);

            MOrder order;
            MOrderLine line = BuildCalcLine(ctx, req, out order);
            if (line == null || (req.M_Product_ID <= 0 && req.C_Charge_ID <= 0)) return res;

            int taxId = line.GetC_Tax_ID();
            decimal net = line.GetLineNetAmt();
            decimal taxAmt = line.GetTaxAmt();

            res.Values["C_UOM_ID"] = line.GetC_UOM_ID();
            res.Values["PriceEntered"] = line.GetPriceEntered();
            res.Values["PriceActual"] = line.GetPriceActual();
            res.Values["PriceList"] = line.GetPriceList();
            res.Values["C_Tax_ID"] = taxId;
            res.Values["TaxAmt"] = taxAmt;
            res.Values["LineNetAmt"] = net;
            res.Values["LineTotalAmt"] = decimal.Add(net, taxAmt);
            if (req.M_Product_ID > 0) res.Values["C_Charge_ID"] = 0;
            else if (req.C_Charge_ID > 0) { res.Values["M_Product_ID"] = 0; res.Values["M_AttributeSetInstance_ID"] = 0; }

            res.Display["uomName"] = GetUomLabel(ctx, line.GetC_UOM_ID());
            res.Display["taxName"] = taxId > 0 ? MTax.Get(ctx, taxId).GetName() : "";
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
                case "QtyOrdered":
                case "PriceEntered":
                case "C_Tax_ID":
                case "C_UOM_ID":
                case "M_AttributeSetInstance_ID":
                    return trigger;
                default: return trigger ?? "";
            }
        }

        private void ApplyDiscount(MOrderLine line, decimal discountPct)
        {
            if (discountPct <= 0) return;
            if (discountPct > 100) discountPct = 100;
            decimal factor = decimal.Subtract(1m, decimal.Divide(discountPct, 100m));
            line.SetPriceActual(decimal.Multiply(line.GetPriceActual(), factor));
        }

        private static readonly HashSet<string> CORE_OR_SYSTEM_COLUMNS = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            "C_OrderLine_ID", "C_Order_ID", "AD_Client_ID", "AD_Org_ID",
            "Created", "CreatedBy", "Updated", "UpdatedBy", "IsActive",
            "M_Product_ID", "C_Charge_ID", "M_AttributeSetInstance_ID",
            // QtyEntered must be blocked alongside QtyOrdered: SetQty() sets both, and
            // ApplyExtraColumns must not overwrite QtyEntered with the stale client value
            // (which carries the old DB qty), otherwise the framework's beforeSave uses the
            // stale QtyEntered to recalculate QtyOrdered — reverting the user's qty change.
            "QtyOrdered", "QtyEntered", "C_UOM_ID", "PriceEntered", "PriceActual",
            "C_Tax_ID", "Line", "Description"
        };

        private HashSet<string> _updateableColumns;
        private HashSet<string> _yesNoColumns;
        private HashSet<string> _referenceColumns;

        /// <summary>Persists every non-core, updateable C_OrderLine column through PO.Set_Value.</summary>
        private void ApplyExtraColumns(MOrderLine line, Dictionary<string, object> values, HashSet<string> touched)
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
                catch (Exception ex) { log.Warning("VAS_107 SaveLines: skip column " + col + " - " + ex.Message); }
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
                  WHERE t.TableName = 'C_OrderLine'
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
        /// internally, which is why VAS_074 charge lines always have a UOM. MOrderLine does
        /// not make this call, so we mirror it here.
        /// </summary>
        /// <param name="ctx">session context</param>
        /// <returns>default C_UOM_ID from the framework, or 0 if not found</returns>
        private int GetDefaultUomId(Ctx ctx)
        {
            return MUOM.GetDefault_UOM_ID(ctx);
        }

        /// <summary>
        /// Returns the Sales UOM (VAS_SalesUOM_Id) configured on the product master for sales orders.
        /// Returns 0 when no Sales UOM is defined or the product does not exist.
        /// </summary>
        /// <param name="ctx">session context</param>
        /// <param name="productId">M_Product_ID to look up</param>
        /// <returns>VAS_SalesUOM_Id from M_Product, or 0 if not set</returns>
        private int GetProductSalesUomId(Ctx ctx, int productId)
        {
            if (productId <= 0) return 0;
            string sql = "SELECT p.VAS_SalesUOM_Id FROM M_Product p WHERE p.M_Product_ID = @M_Product_ID AND p.IsActive = 'Y'";
            sql = MRole.GetDefault(ctx).AddAccessSQL(sql, "p", MRole.SQL_FULLYQUALIFIED, MRole.SQL_RO);
            object val = DB.ExecuteScalar(sql, new SqlParameter[] { new SqlParameter("@M_Product_ID", productId) }, null);
            return Util.GetValueOfInt(val);
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
        public OrderAttributeSetInfo GetProductAttributes(Ctx ctx, int M_Product_ID)
        {
            OrderAttributeSetInfo info = new OrderAttributeSetInfo();
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

            Dictionary<int, OrderAttributeDef> map = new Dictionary<int, OrderAttributeDef>();
            foreach (DataRow r in ds.Tables[0].Rows)
            {
                int attrId = Util.GetValueOfInt(r["M_Attribute_ID"]);
                OrderAttributeDef def;
                if (!map.TryGetValue(attrId, out def))
                {
                    def = new OrderAttributeDef();
                    def.M_Attribute_ID = attrId;
                    def.Name = Util.GetValueOfString(r["AttributeName"]);
                    def.ValueType = Util.GetValueOfString(r["AttributeValueType"]);
                    def.IsInstanceAttribute = Util.GetValueOfString(r["IsInstanceAttribute"]) == "Y";
                    def.IsMandatory = Util.GetValueOfString(r["IsMandatory"]) == "Y";
                    def.Values = new List<OrderAttributeValueDef>();
                    map[attrId] = def;
                    info.Attributes.Add(def);
                }
                int valId = Util.GetValueOfInt(r["M_AttributeValue_ID"]);
                if (valId > 0)
                {
                    OrderAttributeValueDef v = new OrderAttributeValueDef();
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
        public List<OrderAttributeInstanceValue> GetInstanceValues(Ctx ctx, int M_AttributeSetInstance_ID)
        {
            List<OrderAttributeInstanceValue> list = new List<OrderAttributeInstanceValue>();
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
                OrderAttributeInstanceValue v = new OrderAttributeInstanceValue();
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
        public OrderAttributeSaveResult SaveAttribute(Ctx ctx, OrderAttributeSaveRequest req)
        {
            OrderAttributeSaveResult res = new OrderAttributeSaveResult();
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
        private List<KeyNamePair> BuildAttributeValueList(MAttributeSet aset, List<OrderAttributeValueSelection> selections)
        {
            List<KeyNamePair> values = new List<KeyNamePair>();
            if (aset == null) return values;

            Dictionary<int, OrderAttributeValueSelection> byAttr = new Dictionary<int, OrderAttributeValueSelection>();
            if (selections != null)
            {
                foreach (OrderAttributeValueSelection sel in selections)
                {
                    if (sel != null && sel.M_Attribute_ID > 0)
                        byAttr[sel.M_Attribute_ID] = sel;
                }
            }

            MAttribute[] attributes = aset.GetMAttributes(true);
            foreach (MAttribute attr in attributes)
            {
                OrderAttributeValueSelection sel;
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

        #region Write actions (insert / update / delete C_OrderLine)

        /// <summary>
        /// Inserts or updates the supplied order lines through MOrderLine.
        /// All lines share a single transaction so a failure rolls the whole batch back.
        /// </summary>
        public OrderSaveResult SaveLines(Ctx ctx, int C_Order_ID, int AD_Window_ID, List<OrderLineInput> rows, int page = 0)
        {
            OrderSaveResult res = new OrderSaveResult();
            if (C_Order_ID <= 0 || rows == null || rows.Count == 0)
            {
                res.ErrorKey = "VAS_107_NothingToSave";
                return res;
            }

            CreateOrderPanelData ctxData = new CreateOrderPanelData();
            LoadParentContext(ctx, C_Order_ID, ctxData);
            if (ctxData.C_Order_ID <= 0)
            {
                res.ErrorKey = "VAS_107_NoAccess";
                return res;
            }
            if (!ctxData.IsEditable)
            {
                res.ErrorKey = "VAS_107_OrderNotEditable";
                return res;
            }

            Trx trx = Trx.GetTrx(Trx.CreateTrxName("VAS107Save_" + C_Order_ID));
            try
            {
                MOrder order = new MOrder(ctx, C_Order_ID, trx);
                foreach (OrderLineInput input in rows)
                {
                    if (input.M_Product_ID <= 0 && input.C_Charge_ID <= 0)
                        continue;

                    MOrderLine line = input.C_OrderLine_ID > 0
                        ? new MOrderLine(ctx, input.C_OrderLine_ID, trx)
                        : new MOrderLine(order);

                    if (input.C_OrderLine_ID <= 0)
                        line.SetOrder(order);

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

                    // Prefer QtyEntered (the user-visible quantity in the entered UOM). Fall back to
                    // QtyOrdered when QtyEntered was not supplied by the client (older callers).
                    decimal qty = input.QtyEntered > 0 ? input.QtyEntered : (input.QtyOrdered > 0 ? input.QtyOrdered : 1);
                    line.SetQty(qty);

                    if (input.C_UOM_ID > 0)
                        line.SetC_UOM_ID(input.C_UOM_ID);

                    // Charge lines: MOrderLine.SetC_Charge_ID does not auto-set a UOM.
                    // Guard against a missing UOM which would cause MOrderLine.Save to fail.
                    if (input.C_Charge_ID > 0 && line.GetC_UOM_ID() <= 0)
                        line.SetC_UOM_ID(GetDefaultUomId(ctx));

                    // The panel prices every line via CalcLine, so persist the client-sent price exactly.
                    line.SetPriceEntered(input.PriceEntered);
                    line.SetPriceActual(input.PriceEntered);

                    if (input.C_Tax_ID > 0)
                        line.SetC_Tax_ID(input.C_Tax_ID);
                    else
                        line.SetTax();

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
                        log.Warning("VAS_107 SaveLines: line save failed (Line " + input.Line + ") - " + err);
                        res.LineErrors.Add(new OrderLineSaveError
                        {
                            RowKey = input.RowKey,
                            C_OrderLine_ID = input.C_OrderLine_ID,
                            Line = input.Line,
                            Message = err
                        });
                    }
                }

                if (res.LineErrors.Count > 0)
                {
                    trx.Rollback();
                    res.ErrorKey = "VAS_107_SaveFailed";
                    res.ErrorDetail = res.LineErrors[0].Message;
                    return res;
                }

                trx.Commit();
            }
            catch (Exception ex)
            {
                trx.Rollback();
                log.Log(Level.SEVERE, "VAS_107 SaveLines failed", ex);
                res.ErrorKey = "VAS_107_SaveFailed";
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
            res.Lines = LoadLines(ctx, C_Order_ID, ResolveOrderLineTabs(AD_Window_ID), page, out total);
            res.LinesTotal = total;
            res.LinePage = page;
            res.LinePageSize = LINE_PAGE_SIZE;
            decimal soNet, soTax, soTcs;
            ComputeOtherPageTotals(ctx, C_Order_ID, res.Lines, ctxData.IsTaxIncluded, out soNet, out soTax, out soTcs);
            res.OtherPagesSubtotal = soNet; res.OtherPagesTax = soTax; res.OtherPagesTcs = soTcs;
            return res;
        }

        /// <summary>Soft-deletes the supplied saved order lines through MOrderLine.</summary>
        public OrderSaveResult DeleteLines(Ctx ctx, int C_Order_ID, int AD_Window_ID, List<int> lineIds, int page = 0)
        {
            OrderSaveResult res = new OrderSaveResult();
            if (C_Order_ID <= 0 || lineIds == null || lineIds.Count == 0)
            {
                res.ErrorKey = "VAS_107_NothingToSave";
                return res;
            }

            CreateOrderPanelData ctxData = new CreateOrderPanelData();
            LoadParentContext(ctx, C_Order_ID, ctxData);
            if (ctxData.C_Order_ID <= 0) { res.ErrorKey = "VAS_107_NoAccess"; return res; }
            if (!ctxData.IsEditable) { res.ErrorKey = "VAS_107_OrderNotEditable"; return res; }

            Trx trx = Trx.GetTrx(Trx.CreateTrxName("VAS107Delete_" + C_Order_ID));
            try
            {
                foreach (int id in lineIds)
                {
                    if (id <= 0) continue;
                    MOrderLine line = new MOrderLine(ctx, id, trx);
                    if (line.Get_ID() != id) continue;
                    if (line.GetC_Order_ID() != C_Order_ID) continue;
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
                                err = Msg.GetMsg(ctx, "VAS_107_DeleteFailed");
                            else
                                err = val;
                        }
                        log.Warning("VAS_107 DeleteLines: delete failed for line " + id);
                        res.ErrorKey = err;
                        return res;
                    }
                }
                trx.Commit();
            }
            catch (Exception ex)
            {
                trx.Rollback();
                log.Log(Level.SEVERE, "VAS_107 DeleteLines failed", ex);
                res.ErrorKey = "VAS_107_DeleteFailed";
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
            res.Lines = LoadLines(ctx, C_Order_ID, ResolveOrderLineTabs(AD_Window_ID), page, out total);
            int pageCount = System.Math.Max(1, (int)System.Math.Ceiling(total / (double)LINE_PAGE_SIZE));
            if (page > pageCount - 1)
            {
                page = pageCount - 1;
                res.Lines = LoadLines(ctx, C_Order_ID, ResolveOrderLineTabs(AD_Window_ID), page, out total);
            }
            res.LinesTotal = total;
            res.LinePage = page;
            res.LinePageSize = LINE_PAGE_SIZE;
            decimal doNet, doTax, doTcs;
            ComputeOtherPageTotals(ctx, C_Order_ID, res.Lines, ctxData.IsTaxIncluded, out doNet, out doTax, out doTcs);
            res.OtherPagesSubtotal = doNet; res.OtherPagesTax = doTax; res.OtherPagesTcs = doTcs;
            return res;
        }

        #endregion
    }

    #region Data contracts — VAS_107 specific

    /// <summary>Parent order context + saved lines returned to the panel on load.</summary>
    public class CreateOrderPanelData
    {
        public int C_Order_ID { get; set; }
        public int AD_Client_ID { get; set; }
        public int AD_Org_ID { get; set; }
        public int C_BPartner_ID { get; set; }
        public int C_BPartner_Location_ID { get; set; }
        public int M_PriceList_ID { get; set; }
        public int C_Currency_ID { get; set; }
        public DateTime? DateOrdered { get; set; }
        public DateTime? DateAcct { get; set; }
        public bool IsSOTrx { get; set; }
        public bool IsTaxIncluded { get; set; }
        public string DocStatus { get; set; }
        public bool Processed { get; set; }
        public bool IsEditable { get; set; }
        public int LinesTotal { get; set; }
        public int LinePage { get; set; }
        public int LinePageSize { get; set; }
        public decimal OtherPagesSubtotal { get; set; }
        public decimal OtherPagesTax { get; set; }
        public decimal OtherPagesTcs { get; set; }
        public int StdPrecision { get; set; }
        public string CurSymbol { get; set; }
        public string CurISO { get; set; }
        public int AD_Window_ID { get; set; }
        public int AD_Tab_ID { get; set; }
        public List<int> AD_Tab_IDs { get; set; }
        public List<OrderLineRow> Lines { get; set; }
        public List<OrderUomItem> UomList { get; set; }
        public List<OrderTaxItem> TaxList { get; set; }
        public List<OrderColumnMeta> Columns { get; set; }
        public Dictionary<string, string> LoginContext { get; set; }

        public CreateOrderPanelData()
        {
            Lines = new List<OrderLineRow>();
            UomList = new List<OrderUomItem>();
            TaxList = new List<OrderTaxItem>();
            Columns = new List<OrderColumnMeta>();
            AD_Tab_IDs = new List<int>();
            LoginContext = new Dictionary<string, string>();
            StdPrecision = 2;
        }
    }

    /// <summary>Request for the per-row UOM/tax lookup re-filter.</summary>
    public class OrderLookupRequest
    {
        public int C_Order_ID { get; set; }
        public string ColumnName { get; set; }
        public Dictionary<string, object> RowValues { get; set; }
        public OrderLookupRequest() { RowValues = new Dictionary<string, object>(); }
    }

    /// <summary>Per-row filtered lookup lists (UOM + tax) for one order line.</summary>
    public class OrderLookupData
    {
        public int C_Order_ID { get; set; }
        public List<OrderUomItem> UomList { get; set; }
        public List<OrderTaxItem> TaxList { get; set; }
        public OrderLookupData() { UomList = new List<OrderUomItem>(); TaxList = new List<OrderTaxItem>(); }
    }

    /// <summary>Request for the generic FK lookup of a dynamic C_OrderLine field.</summary>
    public class OrderRefLookupRequest
    {
        public int C_Order_ID { get; set; }
        public string ColumnName { get; set; }
        public string Query { get; set; }
        public int Id { get; set; }
        public int PageSize { get; set; }
        public int Offset { get; set; }
        public Dictionary<string, object> RowValues { get; set; }
        public OrderRefLookupRequest() { RowValues = new Dictionary<string, object>(); }
    }

    /// <summary>Inbound callout / calc request from the order panel.</summary>
    public class OrderLineCalcRequest
    {
        public int C_Order_ID { get; set; }
        public string TriggerColumn { get; set; }
        public int M_Product_ID { get; set; }
        public int C_Charge_ID { get; set; }
        public int M_AttributeSetInstance_ID { get; set; }
        public decimal QtyEntered { get; set; }
        public decimal QtyOrdered { get; set; }
        public int C_UOM_ID { get; set; }
        public decimal PriceEntered { get; set; }
        public bool PriceOverride { get; set; }
        public int C_Tax_ID { get; set; }
        public decimal Discount { get; set; }
    }

    /// <summary>Recomputed values returned by the order-line callout.</summary>
    public class OrderLineCalcResult
    {
        public int C_UOM_ID { get; set; }
        public string UOMName { get; set; }
        public decimal QtyOrdered { get; set; }
        public decimal PriceEntered { get; set; }
        public int C_Tax_ID { get; set; }
        public string TaxName { get; set; }
        public decimal TaxAmt { get; set; }
        public decimal LineNetAmt { get; set; }
        public decimal LineTotalAmt { get; set; }
    }

    /// <summary>A saved order line shown in the panel grid.</summary>
    public class OrderLineRow
    {
        public int C_OrderLine_ID { get; set; }
        public int Line { get; set; }
        public int M_Product_ID { get; set; }
        public string ProductName { get; set; }
        public int C_Charge_ID { get; set; }
        public string ChargeName { get; set; }
        public string Description { get; set; }
        public decimal QtyEntered { get; set; }
        public decimal QtyOrdered { get; set; }
        public int C_UOM_ID { get; set; }
        public string UOMName { get; set; }
        public decimal PriceEntered { get; set; }
        public int C_Tax_ID { get; set; }
        public string TaxName { get; set; }
        public decimal TaxAmt { get; set; }
        public decimal TaxableAmt { get; set; }
        public decimal LineNetAmt { get; set; }
        public decimal LineTotalAmt { get; set; }
        public int M_AttributeSetInstance_ID { get; set; }
        public string AttrName { get; set; }
        public bool HasAttributeSet { get; set; }
        public string ProductType { get; set; }
        public Dictionary<string, object> Values { get; set; }
        public OrderLineRow() { Values = new Dictionary<string, object>(); }
    }

    /// <summary>Attribute-instance create/update request from the order panel.</summary>
    public class OrderAttributeSaveRequest
    {
        public int M_Product_ID { get; set; }
        public int M_AttributeSetInstance_ID { get; set; }
        public string Lot { get; set; }
        public string SerNo { get; set; }
        public string GuaranteeDate { get; set; }
        public List<OrderAttributeValueSelection> Values { get; set; }
    }

    /// <summary>One entered attribute value in an order-panel attribute save request.</summary>
    public class OrderAttributeValueSelection
    {
        public int M_Attribute_ID { get; set; }
        public string ValueType { get; set; }
        public int M_AttributeValue_ID { get; set; }
        public decimal? NumberValue { get; set; }
        public string StringValue { get; set; }
        public string DisplayValue { get; set; }
    }

    /// <summary>One inbound order line to insert / update.</summary>
    public class OrderLineInput
    {
        public int C_OrderLine_ID { get; set; }
        public string RowKey { get; set; }
        public int Line { get; set; }
        public int M_Product_ID { get; set; }
        public int C_Charge_ID { get; set; }
        public int M_AttributeSetInstance_ID { get; set; }
        public decimal QtyEntered { get; set; }
        public decimal QtyOrdered { get; set; }
        public int C_UOM_ID { get; set; }
        public decimal PriceEntered { get; set; }
        public int C_Tax_ID { get; set; }
        public decimal Discount { get; set; }
        public string Description { get; set; }
        public Dictionary<string, object> Values { get; set; }
        public List<string> TouchedCols { get; set; }
        public OrderLineInput() { Values = new Dictionary<string, object>(); TouchedCols = new List<string>(); }
    }

    /// <summary>POST body for the batch order-line save.</summary>
    public class OrderSaveLinesRequest
    {
        public int C_Order_ID { get; set; }
        public int AD_Window_ID { get; set; }
        public int Page { get; set; }
        public List<OrderLineInput> Lines { get; set; }
        public OrderSaveLinesRequest() { Lines = new List<OrderLineInput>(); }
    }

    /// <summary>POST body for the batch order-line delete.</summary>
    public class OrderDeleteLinesRequest
    {
        public int C_Order_ID { get; set; }
        public int AD_Window_ID { get; set; }
        public int Page { get; set; }
        public List<int> LineIds { get; set; }
        public OrderDeleteLinesRequest() { LineIds = new List<int>(); }
    }

    /// <summary>A save failure for one specific order line.</summary>
    public class OrderLineSaveError
    {
        public string RowKey { get; set; }
        public int C_OrderLine_ID { get; set; }
        public int Line { get; set; }
        public string Message { get; set; }
    }

    /// <summary>Result of a save / delete batch for order lines.</summary>
    public class OrderSaveResult
    {
        public bool Success { get; set; }
        public string ErrorKey { get; set; }
        public string ErrorDetail { get; set; }
        public List<OrderLineSaveError> LineErrors { get; set; }
        public List<OrderLineRow> Lines { get; set; }
        public int LinesTotal { get; set; }
        public int LinePage { get; set; }
        public int LinePageSize { get; set; }
        public decimal OtherPagesSubtotal { get; set; }
        public decimal OtherPagesTax { get; set; }
        public decimal OtherPagesTcs { get; set; }
        public OrderSaveResult() { Lines = new List<OrderLineRow>(); LineErrors = new List<OrderLineSaveError>(); }
    }

    /// <summary>
    /// Attribute-set definition returned by GetProductAttributes.
    /// Carries the set-level flags (Lot / SerNo / GuaranteeDate / Mandatory)
    /// plus the ordered list of attributes and their selectable values.
    /// </summary>
    public class OrderAttributeSetInfo
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
        public List<OrderAttributeDef> Attributes { get; set; }
        public OrderAttributeSetInfo() { Attributes = new List<OrderAttributeDef>(); }
    }

    /// <summary>One attribute within an order-panel attribute set (with its list values).</summary>
    public class OrderAttributeDef
    {
        public int M_Attribute_ID { get; set; }
        public string Name { get; set; }
        /// <summary>'L' list, 'N' number, 'S' string.</summary>
        public string ValueType { get; set; }
        public bool IsInstanceAttribute { get; set; }
        public bool IsMandatory { get; set; }
        public List<OrderAttributeValueDef> Values { get; set; }
    }

    /// <summary>A selectable list value for an order-panel attribute.</summary>
    public class OrderAttributeValueDef
    {
        public int M_AttributeValue_ID { get; set; }
        public string Code { get; set; }
        public string Name { get; set; }
    }

    /// <summary>
    /// One stored attribute value on an existing instance, returned by GetInstanceValues
    /// to pre-populate the edit form when the user opens an already-assigned ASI.
    /// </summary>
    public class OrderAttributeInstanceValue
    {
        public int M_Attribute_ID { get; set; }
        /// <summary>'L' list, 'N' number, 'S' string.</summary>
        public string ValueType { get; set; }
        public int M_AttributeValue_ID { get; set; }
        public decimal? NumberValue { get; set; }
        public string StringValue { get; set; }
    }

    /// <summary>Result of creating or updating an attribute-set instance via SaveAttribute.</summary>
    public class OrderAttributeSaveResult
    {
        public int M_AttributeSetInstance_ID { get; set; }
        public string Description { get; set; }
        /// <summary>Framework mandatory / save error message; empty string on success.</summary>
        public string Error { get; set; }
    }

    /// <summary>
    /// Metadata for one C_OrderLine column: reference type, callout, val-rule, display/
    /// read-only logic and optional inline list values. Used by the panel to drive
    /// FK lookups, callout chains and field visibility without extra round-trips.
    /// </summary>
    public class OrderColumnMeta
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
        public List<OrderRefListItem> RefListValues { get; set; }
        public OrderColumnMeta() { RefListValues = new List<OrderRefListItem>(); }
    }

    /// <summary>One value of a List (AD_Reference 17) field, sourced from AD_Ref_List.</summary>
    public class OrderRefListItem
    {
        public string Value { get; set; }
        public string Name { get; set; }
    }

    /// <summary>One row of a generic FK lookup (id + display label), returned by GetRefLookup.</summary>
    public class OrderRefItem
    {
        public int Id { get; set; }
        public string Name { get; set; }
    }

    /// <summary>A unit of measure for the UOM dropdown in the order panel grid.</summary>
    public class OrderUomItem
    {
        public int C_UOM_ID { get; set; }
        public string Name { get; set; }
    }

    /// <summary>A tax entry (with rate) for the tax dropdown in the order panel grid.</summary>
    public class OrderTaxItem
    {
        public int C_Tax_ID { get; set; }
        public string Name { get; set; }
        public decimal Rate { get; set; }
    }

    /// <summary>One Product or Charge row in the catalog autocomplete for the order panel.</summary>
    public class OrderCatalogItem
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
    /// Result of running a C_OrderLine column's AD_Column.Callout server-side:
    /// the changed column values the client patches back into the line, the display
    /// labels and the callout reference that was read (for traceability).
    /// </summary>
    public class OrderCalloutResult
    {
        public string Column { get; set; }
        public string Callout { get; set; }
        public Dictionary<string, object> Values { get; set; }
        public Dictionary<string, string> Display { get; set; }
        public OrderCalloutResult()
        {
            Values = new Dictionary<string, object>();
            Display = new Dictionary<string, string>();
        }
    }

    #endregion

    /// <summary>
    /// One row returned by LoadOrderTaxBreakdown — a per-tax summary from C_OrderTax
    /// covering ALL order lines (not just the current page). Used by the JS totals footer.
    /// </summary>
    public class OrderTaxBreakdownItem
    {
        public string TaxName { get; set; }
        public decimal TaxAmt { get; set; }
        public decimal TaxBaseAmt { get; set; }
        public decimal TotalLines { get; set; }
        public decimal GrandTotal { get; set; }
        public string CurSymbol { get; set; }
        public int StdPrecision { get; set; }
        public decimal TCSAmount { get; set; }
        /// <summary>
        /// Total surcharge across all order lines (SUM of C_OrderLine.SurchargeAmt).
        /// Only populated on the first item (index 0). The client renders it as a
        /// separate "Surcharge" row so it is not bundled with the base tax amounts.
        /// </summary>
        public decimal TotalSurcharge { get; set; }
    }
}
