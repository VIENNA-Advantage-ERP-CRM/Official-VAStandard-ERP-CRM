/******************************************************
 * Module Name    : VASLogic
 * Purpose        : Backing model for the VAS_247_MaterialTransferBottomPanel tab
 *                  panel — the material-transfer counterpart of
 *                  VAS_240_RequisitionBottomPanel. Provides parent-movement
 *                  context and existing lines, the dictionary column metadata
 *                  that drives the Additional Info modal, paged product catalog
 *                  search (50 rows / scroll), per-row AD_Val_Rule lookups, the
 *                  server-side line callout (unit + converted quantity), product
 *                  attribute (M_AttributeSetInstance) read + create, barcode scan
 *                  lookup and the M_MovementLine insert / update / delete write
 *                  actions (always through the MMovementLine business class).
 *
 *                  A movement line carries NO price, tax, discount or currency:
 *                  M_MovementLine has no PriceActual / C_Tax_ID / C_Charge_ID and
 *                  there is no movement-tax table, so the panel states quantities
 *                  only — a line count and a summed MovementQty. Everything
 *                  money-shaped in VAS_240 is therefore absent here rather than
 *                  stubbed. It is the only one of the three quantity panels with
 *                  TWO locators (from / to) and TWO attribute-set instances.
 *
 *                  Structured as a standalone model — its own column metadata,
 *                  val-rule, lookup, attribute and write machinery, and its own
 *                  data contracts at the foot of the file — mirroring VAS_240
 *                  rather than sharing a base class, so a change made for one
 *                  document can never silently alter another.
 * Chronological  : Development
 *   VAI154         Created  07-Sep-2026
 *   VAI154         Rebuilt on the VAS_240 pattern  09-Sep-2026
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
    /// Purpose     : Data + write model behind the Material Transfer Bottom Panel
    ///               (M_Movement / M_MovementLine). Every SELECT is filtered
    ///               through MRole.AddAccessSQL on the main physical table alias
    ///               only and uses bind parameters so the same code runs on
    ///               PostgreSQL and Oracle. All inserts go through MMovementLine
    ///               (never a hand-written INSERT) so the standard movement-line
    ///               logic runs — locator validation and the QtyEntered ->
    ///               MovementQty UOM conversion in MMovementLine.BeforeSave.
    /// Chronological development:
    ///   VAI154         Created  07-Sep-2026
    ///   VAI154         Rebuilt on the VAS_240 pattern  09-Sep-2026
    /// </summary>
    public class VAS_247_MaterialTransferBottomPanelModel
    {
        private static VLogger log = VLogger.GetVLogger(typeof(VAS_247_MaterialTransferBottomPanelModel).FullName);

        /// <summary>Page size for the product catalog search.</summary>
        private const int CATALOG_PAGE_SIZE = 50;

        /// <summary>Saved movement lines loaded per page (server-side paging).</summary>
        private const int LINE_PAGE_SIZE = 10;

        /// <summary>Physical line table this panel edits.</summary>
        private const string LINE_TABLE = "M_MovementLine";

        /// <summary>Physical parent table.</summary>
        private const string PARENT_TABLE = "M_Movement";

        /// <summary>Parent key column.</summary>
        private const string PARENT_KEY = "M_Movement_ID";

        /// <summary>AD_Message key prefix for this panel.</summary>
        private const string MSG = "VAS_247_";

        #region Panel (read) data

        /// <summary>
        /// Builds the panel header context (everything the client-side logic needs
        /// from the parent movement) plus one page of already-saved lines.
        /// Returns an empty object (ParentId = 0) when the role has no access.
        /// </summary>
        /// <param name="ctx">session context</param>
        /// <param name="M_Movement_ID">parent movement</param>
        /// <param name="AD_Window_ID">source window (supplies the line tabs)</param>
        /// <param name="page">0-based page of saved lines</param>
        /// <returns>panel view model</returns>
        public MovementPanelData GetPanelData(Ctx ctx, int M_Movement_ID, int AD_Window_ID, int page = 0)
        {
            MovementPanelData data = new MovementPanelData();
            data.LinePageSize = LINE_PAGE_SIZE;
            if (M_Movement_ID <= 0) return data;

            LoadParentContext(ctx, M_Movement_ID, data);
            if (data.M_Movement_ID <= 0) return data;   // no access / not found

            data.AD_Window_ID = AD_Window_ID;
            List<int> tabIds = ResolveMovementLineTabs(AD_Window_ID);
            data.AD_Tab_IDs = tabIds;
            data.AD_Tab_ID = tabIds.Count > 0 ? tabIds[0] : 0;

            if (page < 0) page = 0;
            int total;
            data.Lines = LoadLines(ctx, M_Movement_ID, page, out total);
            data.LinesTotal = total;
            data.LinePage = page;
            data.TotalQty = SumQty(ctx, M_Movement_ID);
            LoadCatalogs(ctx, data);
            LoadColumns(ctx, data, tabIds);
            LoadLoginContext(ctx, data);
            return data;
        }

        /// <summary>
        /// Collects the login / session context values for every @$Token@ / @#Token@
        /// referenced by any column's DisplayLogic or ReadOnlyLogic, so the client
        /// evaluator resolves them without a second round trip.
        /// </summary>
        /// <param name="ctx">session context</param>
        /// <param name="data">panel data whose Columns have already been loaded</param>
        private void LoadLoginContext(Ctx ctx, MovementPanelData data)
        {
            Regex rx = new Regex(@"@([#$][A-Za-z0-9_]+)@");
            HashSet<string> tokens = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (MovementColumnMeta m in data.Columns)
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

        /// <summary>Accounting ElementTypes active on the client's primary accounting schema.</summary>
        /// <param name="ctx">session context</param>
        /// <returns>set of active ElementType codes</returns>
        private HashSet<string> LoadActiveAcctElementTypes(Ctx ctx)
        {
            HashSet<string> types = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            string sql = @"SELECT DISTINCT ase.ElementType
                           FROM C_AcctSchema_Element ase
                           WHERE ase.IsActive = 'Y'
                             AND ase.C_AcctSchema_ID = (SELECT ci.C_AcctSchema1_ID
                                                        FROM AD_ClientInfo ci
                                                        WHERE ci.AD_Client_ID = @client)";
            DataSet ds = DB.ExecuteDataset(sql,
                new SqlParameter[] { new SqlParameter("@client", ctx.GetAD_Client_ID()) }, null);
            if (ds != null && ds.Tables.Count > 0)
                foreach (DataRow r in ds.Tables[0].Rows)
                {
                    string t = Util.GetValueOfString(r["ElementType"]);
                    if (!string.IsNullOrEmpty(t)) types.Add(t);
                }
            return types;
        }

        /// <summary>Loads M_MovementLine column metadata once per panel load.</summary>
        /// <param name="ctx">session context</param>
        /// <param name="data">panel data to populate</param>
        /// <param name="AD_Tab_IDs">movement-line tabs of the source window</param>
        private void LoadColumns(Ctx ctx, MovementPanelData data, List<int> AD_Tab_IDs)
        {
            if (AD_Tab_IDs != null && AD_Tab_IDs.Count > 0)
                LoadColumnsFromTabs(ctx, data, AD_Tab_IDs);
            MergeAllColumns(ctx, data);
            LoadListValues(data);
        }

        /// <summary>Fills inline AD_Ref_List values for List (reference 17) columns.</summary>
        /// <param name="data">panel data whose Columns are already loaded</param>
        private void LoadListValues(MovementPanelData data)
        {
            foreach (MovementColumnMeta m in data.Columns)
            {
                if (m.AD_Reference_ID != 17 || m.AD_Reference_Value_ID <= 0) continue;
                DataSet ds = DB.ExecuteDataset(
                    @"SELECT Value, Name FROM AD_Ref_List
                      WHERE AD_Reference_ID = @ref AND IsActive = 'Y'
                      ORDER BY COALESCE(Name, Value)",
                    new SqlParameter[] { new SqlParameter("@ref", m.AD_Reference_Value_ID) }, null);
                if (ds == null || ds.Tables.Count == 0) continue;
                foreach (DataRow r in ds.Tables[0].Rows)
                    m.RefListValues.Add(new MovementRefListItem
                    {
                        Value = Util.GetValueOfString(r["Value"]),
                        Name = Util.GetValueOfString(r["Name"])
                    });
            }
        }

        private Dictionary<int, List<int>> _mlTabsByWindow;

        /// <summary>Finds every active AD_Tab bound to M_MovementLine inside the given window.</summary>
        /// <param name="AD_Window_ID">source window</param>
        /// <returns>tab ids, in window sequence</returns>
        private List<int> ResolveMovementLineTabs(int AD_Window_ID)
        {
            if (_mlTabsByWindow == null) _mlTabsByWindow = new Dictionary<int, List<int>>();
            List<int> cached;
            if (_mlTabsByWindow.TryGetValue(AD_Window_ID, out cached)) return cached;

            List<int> tabs = new List<int>();
            if (AD_Window_ID > 0)
            {
                DataSet ds = DB.ExecuteDataset(
                    @"SELECT tb.AD_Tab_ID
                      FROM AD_Tab tb
                      INNER JOIN AD_Table t ON (tb.AD_Table_ID = t.AD_Table_ID)
                      WHERE tb.AD_Window_ID = @win AND t.TableName = @tbl AND tb.IsActive = 'Y'
                      ORDER BY tb.SeqNo",
                    new SqlParameter[] {
                        new SqlParameter("@win", AD_Window_ID),
                        new SqlParameter("@tbl", LINE_TABLE) }, null);
                if (ds != null && ds.Tables.Count > 0)
                    foreach (DataRow r in ds.Tables[0].Rows)
                    {
                        int id = Util.GetValueOfInt(r["AD_Tab_ID"]);
                        if (id > 0) tabs.Add(id);
                    }
            }
            _mlTabsByWindow[AD_Window_ID] = tabs;
            return tabs;
        }

        /// <summary>
        /// SELECT expression for ReadOnlyLogic giving AD_Field priority over AD_Column.
        ///
        /// The non-blank test is LENGTH(TRIM(x)) &gt; 0, NOT "x &lt;&gt; ''". On Oracle the
        /// empty string IS NULL, so "x &lt;&gt; ''" evaluates to UNKNOWN for every row and the
        /// sub-select silently matches nothing — the AD_Field-level override would never be
        /// seen there and every column would fall through to AD_Column.ReadOnlyLogic.
        /// </summary>
        /// <param name="hasTabField">whether the outer query already joins AD_Field as f</param>
        /// <returns>SQL expression</returns>
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
                                  AND tt2.TableName = '" + LINE_TABLE + @"'
                                  AND f2.ReadOnlyLogic IS NOT NULL
                                  AND LENGTH(TRIM(f2.ReadOnlyLogic)) > 0), N''), ");
            sb.Append("c.ReadOnlyLogic, N'')");
            return sb.ToString();
        }

        /// <summary>
        /// Base-system column-name prefixes (including the trailing underscore) that are
        /// always present and never require a module-installation check. Every other prefix
        /// belongs to an optional module and must pass <see cref="IsColumnModuleInstalled"/>.
        /// </summary>
        private static readonly HashSet<string> _systemPrefixes = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            // Core Application Dictionary / Compiere / ADempiere columns
            "AD_", "C_", "M_", "A_", "G_", "K_", "R_", "I_", "B_", "T_", "S_", "W_", "U_",
            // VAS / VIS platform-core columns
            "VAS_", "VIS_", "VA_", "VB_",
            // Prefixes that name a ROLE rather than a module. The prefix rule takes
            // everything up to the first underscore, so Ref_MovementLine_ID would read as
            // an optional module, fail Env.IsModuleInstalled and vanish from the payload.
            "Ref_", "Link_"
        };

        /// <summary>
        /// Returns true when the column belongs to the base system or when the optional
        /// module that owns it is confirmed installed. The module prefix is the leading
        /// segment up to (and including) the first underscore, e.g. "VA024_".
        /// </summary>
        /// <param name="columnName">AD_Column.ColumnName to test</param>
        /// <returns>whether the column should be included in the panel metadata</returns>
        private static bool IsColumnModuleInstalled(string columnName)
        {
            if (string.IsNullOrEmpty(columnName)) return true;
            int idx = columnName.IndexOf('_');
            if (idx <= 0) return true;
            string prefix = columnName.Substring(0, idx + 1);
            if (_systemPrefixes.Contains(prefix)) return true;
            return Env.IsModuleInstalled(prefix);
        }

        /// <summary>Reads AD_Field -&gt; AD_Column metadata across all the window's movement-line tabs.</summary>
        /// <param name="ctx">session context</param>
        /// <param name="data">panel data to populate</param>
        /// <param name="AD_Tab_IDs">movement-line tabs</param>
        /// <returns>whether any column was found</returns>
        private bool LoadColumnsFromTabs(Ctx ctx, MovementPanelData data, List<int> AD_Tab_IDs)
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
                                  COALESCE(vr.Type, '')           AS ValRuleType,
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
                if (!IsColumnModuleInstalled(name)) continue;
                data.Columns.Add(MapColumnMeta(r, true));
            }
            return true;
        }

        /// <summary>Merges every active M_MovementLine column not already loaded from a tab.</summary>
        /// <param name="ctx">session context</param>
        /// <param name="data">panel data to populate</param>
        private void MergeAllColumns(Ctx ctx, MovementPanelData data)
        {
            HashSet<string> have = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (MovementColumnMeta cm in data.Columns) have.Add(cm.ColumnName);

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
                                  COALESCE(vr.Type, '')           AS ValRuleType,
                                  COALESCE(vr.Code, N'')          AS ValRuleCode,
                                  COALESCE((SELECT MAX(f2.DisplayLogic)
                                                FROM AD_Field f2
                                                INNER JOIN AD_Tab t2 ON (f2.AD_Tab_ID = t2.AD_Tab_ID)
                                                INNER JOIN AD_Table tt2 ON (t2.AD_Table_ID = tt2.AD_Table_ID)
                                                WHERE f2.AD_Column_ID = c.AD_Column_ID
                                                  AND f2.IsActive = 'Y'
                                                  AND tt2.TableName = '" + LINE_TABLE + @"'
                                                  AND f2.DisplayLogic IS NOT NULL
                                                  AND LENGTH(TRIM(f2.DisplayLogic)) > 0), N'') AS DisplayLogic
                           FROM AD_Column c
                           INNER JOIN AD_Table t ON (c.AD_Table_ID = t.AD_Table_ID)
                           LEFT JOIN AD_Val_Rule vr ON (c.AD_Val_Rule_ID = vr.AD_Val_Rule_ID
                                AND vr.IsActive = 'Y')
                           WHERE t.TableName = '" + LINE_TABLE + @"'
                             AND c.IsActive = 'Y'";

            DataSet ds = DB.ExecuteDataset(sql);
            if (ds == null || ds.Tables.Count == 0) return;
            foreach (DataRow r in ds.Tables[0].Rows)
            {
                string colName = Util.GetValueOfString(r["ColumnName"]);
                if (have.Contains(colName)) continue;
                if (!IsColumnModuleInstalled(colName)) continue;
                data.Columns.Add(MapColumnMeta(r, false));
            }
        }

        /// <summary>Maps one column-meta row.</summary>
        /// <param name="r">source row</param>
        /// <param name="fromField">true when the row came from AD_Field (tab) metadata</param>
        /// <returns>column metadata</returns>
        private MovementColumnMeta MapColumnMeta(DataRow r, bool fromField)
        {
            MovementColumnMeta m = new MovementColumnMeta
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
        /// <param name="adImageId">AD_Image_ID</param>
        /// <param name="imageExtension">stored file extension</param>
        /// <returns>relative URL, or "" when no thumbnail exists</returns>
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
        /// Loads the option lists the grid cells offer. There is no tax or price-list
        /// catalog: a movement line carries neither.
        /// </summary>
        /// <param name="ctx">session context</param>
        /// <param name="data">panel data to populate</param>
        private void LoadCatalogs(Ctx ctx, MovementPanelData data)
        {
            data.UomList = LoadUomList(ctx, data.M_Movement_ID, null);
            // A movement crosses warehouses, so neither locator list is filtered to the
            // header warehouse — the destination bin usually belongs to a different one,
            // and the warehouse name in each label keeps the list readable. The two lists
            // are built separately because the source and destination columns carry their
            // own dictionary val rules.
            data.LocatorList = LoadLocatorList(ctx, data.M_Movement_ID, 0, null, "M_Locator_ID");
            data.LocatorToList = LoadLocatorList(ctx, data.M_Movement_ID, 0, null, "M_LocatorTo_ID");
        }

        /// <summary>Builds the UOM dropdown list, enforcing the C_UOM_ID column's AD_Val_Rule.</summary>
        /// <param name="ctx">session context</param>
        /// <param name="M_Movement_ID">parent movement (val-rule context)</param>
        /// <param name="rowVars">current line values for @token@ substitution, or null</param>
        /// <returns>UOM options</returns>
        private List<MovementUomItem> LoadUomList(Ctx ctx, int M_Movement_ID, Dictionary<string, string> rowVars)
        {
            List<MovementUomItem> list = new List<MovementUomItem>();
            string sql = @"SELECT u.C_UOM_ID, u.Name AS UOMName,
                                  COALESCE(u.UOMSymbol, u.Name) AS UOMSymbol
                           FROM C_UOM u
                           WHERE u.IsActive = 'Y'
                             AND u.AD_Client_ID IN (0, " + ctx.GetAD_Client_ID() + ")";
            string pred = GetValRulePredicate(ctx, "C_UOM_ID", "C_UOM", "u", M_Movement_ID, rowVars);
            if (pred.Length > 0) sql += " AND (" + pred + ")";
            sql = MRole.GetDefault(ctx).AddAccessSQL(sql, "u", MRole.SQL_FULLYQUALIFIED, MRole.SQL_RO);
            sql += " ORDER BY u.Name";

            DataSet ds = DB.ExecuteDataset(sql);
            if (ds != null && ds.Tables.Count > 0)
                foreach (DataRow r in ds.Tables[0].Rows)
                    list.Add(new MovementUomItem
                    {
                        C_UOM_ID = Util.GetValueOfInt(r["C_UOM_ID"]),
                        Name = Util.GetValueOfString(r["UOMName"]),
                        Symbol = Util.GetValueOfString(r["UOMSymbol"])
                    });
            return list;
        }

        /// <summary>
        /// Builds the locator dropdown list, labelled "WAREHOUSE - VALUE" and filtered by
        /// the M_Locator_ID column's AD_Val_Rule where one is configured.
        /// </summary>
        /// <param name="ctx">session context</param>
        /// <param name="M_Movement_ID">parent movement (val-rule context)</param>
        /// <param name="M_Warehouse_ID">restrict to this warehouse, or 0 for all</param>
        /// <param name="rowVars">current line values for @token@ substitution, or null</param>
        /// <param name="columnName">
        /// which locator column the list is for — M_Locator_ID (source) or M_LocatorTo_ID
        /// (destination). The two carry DIFFERENT dictionary val rules, so a single shared
        /// list would silently filter the destination bin by the source's rule.
        /// </param>
        /// <returns>locator options</returns>
        private List<MovementLocatorItem> LoadLocatorList(Ctx ctx, int M_Movement_ID, int M_Warehouse_ID,
            Dictionary<string, string> rowVars, string columnName)
        {
            List<MovementLocatorItem> list = new List<MovementLocatorItem>();
            string sql = @"SELECT l.M_Locator_ID, l.M_Warehouse_ID, l.Value,
                                  COALESCE(w.Name, N'') AS WarehouseName
                           FROM M_Locator l
                           INNER JOIN M_Warehouse w ON (w.M_Warehouse_ID = l.M_Warehouse_ID)
                           WHERE l.IsActive = 'Y'
                             AND l.AD_Client_ID = " + ctx.GetAD_Client_ID();
            if (M_Warehouse_ID > 0) sql += " AND l.M_Warehouse_ID = @wh";
            string pred = GetValRulePredicate(ctx, columnName, "M_Locator", "l", M_Movement_ID, rowVars);
            if (pred.Length > 0) sql += " AND (" + pred + ")";
            sql = MRole.GetDefault(ctx).AddAccessSQL(sql, "l", MRole.SQL_FULLYQUALIFIED, MRole.SQL_RO);
            sql += " ORDER BY w.Name, l.Value";

            SqlParameter[] ps = M_Warehouse_ID > 0
                ? new SqlParameter[] { new SqlParameter("@wh", M_Warehouse_ID) }
                : null;

            DataSet ds = DB.ExecuteDataset(sql, ps, null);
            if (ds == null || ds.Tables.Count == 0) return list;
            foreach (DataRow r in ds.Tables[0].Rows)
            {
                string wh = Util.GetValueOfString(r["WarehouseName"]);
                string val = Util.GetValueOfString(r["Value"]);
                list.Add(new MovementLocatorItem
                {
                    M_Locator_ID = Util.GetValueOfInt(r["M_Locator_ID"]),
                    M_Warehouse_ID = Util.GetValueOfInt(r["M_Warehouse_ID"]),
                    Value = val,
                    Name = wh.Length > 0 ? wh + " - " + val : val
                });
            }
            return list;
        }

        /// <summary>
        /// Re-fetches the per-row filtered UOM and locator lists for one movement line,
        /// honouring each column's AD_Val_Rule against the line's current values.
        /// </summary>
        /// <param name="ctx">session context</param>
        /// <param name="req">line context</param>
        /// <returns>filtered option lists</returns>
        public MovementLookupData GetLookupData(Ctx ctx, MovementLookupRequest req)
        {
            MovementLookupData data = new MovementLookupData();
            if (req == null || req.M_Movement_ID <= 0) return data;

            MovementPanelData parent = new MovementPanelData();
            LoadParentContext(ctx, req.M_Movement_ID, parent);
            if (parent.M_Movement_ID <= 0) return data;

            data.M_Movement_ID = req.M_Movement_ID;
            Dictionary<string, string> rowVars = BuildRowVars(req.RowValues);
            data.UomList = LoadUomList(ctx, req.M_Movement_ID, rowVars);
            data.LocatorList = LoadLocatorList(ctx, req.M_Movement_ID, 0, rowVars, "M_Locator_ID");
            data.LocatorToList = LoadLocatorList(ctx, req.M_Movement_ID, 0, rowVars, "M_LocatorTo_ID");
            return data;
        }

        #endregion

        #region Generic FK lookup for the Additional Info modal

        /// <summary>
        /// Generic FK lookup for a dynamic M_MovementLine field (Table / TableDir / Search):
        /// id + label rows filtered by keyword, the column's AD_Val_Rule in the line's
        /// context and the role's access. Id &gt; 0 resolves a single value's label.
        /// </summary>
        /// <param name="ctx">session context</param>
        /// <param name="req">lookup request</param>
        /// <returns>matching reference rows</returns>
        public List<MovementRefItem> GetRefLookup(Ctx ctx, MovementRefLookupRequest req)
        {
            List<MovementRefItem> items = new List<MovementRefItem>();
            if (req == null || req.M_Movement_ID <= 0 || string.IsNullOrEmpty(req.ColumnName)) return items;

            MovementPanelData parent = new MovementPanelData();
            LoadParentContext(ctx, req.M_Movement_ID, parent);
            if (parent.M_Movement_ID <= 0) return items;

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
                string pred = GetValRulePredicate(ctx, req.ColumnName, def.TableName, alias, req.M_Movement_ID, rowVars);
                if (pred.Length > 0) sql.Append(" AND (").Append(pred).Append(")");
            }

            string secured = MRole.GetDefault(ctx).AddAccessSQL(
                sql.ToString(), alias, MRole.SQL_FULLYQUALIFIED, MRole.SQL_RO);
            if (req.Id <= 0) secured += " ORDER BY " + dispExpr + PagingSuffix(pageSize, offset);

            DataSet ds = DB.ExecuteDataset(secured, ps.ToArray(), null);
            if (ds == null || ds.Tables.Count == 0) return items;
            foreach (DataRow r in ds.Tables[0].Rows)
                items.Add(new MovementRefItem { Id = Util.GetValueOfInt(r["Id"]), Name = Util.GetValueOfString(r["Name"]) });
            return items;
        }

        /// <summary>Non-standard TableDir FK columns whose lookup table/key is not the column name minus "_ID".</summary>
        private static readonly Dictionary<string, string[]> TableDirOverrides =
            new Dictionary<string, string[]>(StringComparer.OrdinalIgnoreCase)
            {
                { "AD_OrgTrx_ID", new string[] { "AD_Org", "AD_Org_ID" } },
                // Both locator columns point at M_Locator; only the "To" side needs telling.
                { "M_LocatorTo_ID", new string[] { "M_Locator", "M_Locator_ID" } },
                // The destination attribute-set instance shares M_AttributeSetInstance.
                { "M_AttributeSetInstanceTo_ID", new string[] { "M_AttributeSetInstance", "M_AttributeSetInstance_ID" } },
                // A movement line raised from an order / work order names its source line.
                { "Ref_MovementLine_ID", new string[] { "M_MovementLine", "M_MovementLine_ID" } }
            };

        /// <summary>
        /// Display expression for a lookup pointing at a DOCUMENT LINE table, which has no
        /// identifier column of its own: name the line by its document instead, as
        /// "&lt;DocumentNo&gt; - &lt;line no&gt;".
        /// </summary>
        /// <param name="headerTable">parent document table</param>
        /// <param name="headerKey">parent key column on the line</param>
        /// <returns>SQL display expression with an {a} alias placeholder</returns>
        private string DocLineDisplayExpr(string headerTable, string headerKey)
        {
            string docNo = "(SELECT h.DocumentNo FROM " + headerTable + " h WHERE h."
                + headerKey + " = {a}." + headerKey + ")";
            if (DB.IsPostgreSQL() || DB.IsOracle())
                return docNo + " || ' - ' || {a}.Line";
            return "CONCAT(" + docNo + ", ' - ', {a}.Line)";
        }

        /// <summary>Display expression for the line table a lookup points at, or null for any other table.</summary>
        /// <param name="table">lookup table name</param>
        /// <returns>display expression, or null</returns>
        private string LineTableDisplayExpr(string table)
        {
            if ("M_MovementLine".Equals(table, StringComparison.OrdinalIgnoreCase))
                return DocLineDisplayExpr("M_Movement", "M_Movement_ID");
            if ("C_OrderLine".Equals(table, StringComparison.OrdinalIgnoreCase))
                return DocLineDisplayExpr("C_Order", "C_Order_ID");
            if ("M_InOutLine".Equals(table, StringComparison.OrdinalIgnoreCase))
                return DocLineDisplayExpr("M_InOut", "M_InOut_ID");
            return null;
        }

        private Dictionary<string, RefLookupDef> _refDefByColumn;

        /// <summary>Resolves a M_MovementLine FK column to its lookup table, key and display expression.</summary>
        /// <param name="columnName">FK column</param>
        /// <returns>lookup definition, or null when the column is not a resolvable FK</returns>
        private RefLookupDef ResolveRefLookup(string columnName)
        {
            if (_refDefByColumn == null) _refDefByColumn = new Dictionary<string, RefLookupDef>(StringComparer.OrdinalIgnoreCase);
            RefLookupDef cached;
            if (_refDefByColumn.TryGetValue(columnName, out cached)) return cached;

            RefLookupDef def = null;
            DataSet rs = DB.ExecuteDataset(
                @"SELECT c.AD_Reference_ID, COALESCE(c.AD_Reference_Value_ID, 0) AS AD_Reference_Value_ID
                  FROM AD_Column c INNER JOIN AD_Table t ON (c.AD_Table_ID = t.AD_Table_ID)
                  WHERE t.TableName = '" + LINE_TABLE + @"' AND c.ColumnName = @c AND c.IsActive = 'Y'",
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

            // Fallback: derive the lookup table from the column-name TableDir convention
            // for a column that is absent or inactive in AD_Column.
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
        /// <param name="table">lookup table</param>
        /// <returns>SQL expression with an {a} alias placeholder</returns>
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
        /// <param name="table">table name</param>
        /// <param name="column">column name</param>
        /// <returns>whether the column is registered and active</returns>
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

        #endregion

        #region Parent context + lines

        /// <summary>
        /// SELECT-list item for one optional M_Movement column that a movement-line
        /// field's DisplayLogic reads as a token. The column where the dictionary has
        /// it, NULL where it does not, always under its own name — NULL rather than a
        /// literal default on purpose, so the logic can tell "not set" from a real value.
        /// </summary>
        /// <param name="column">M_Movement column named by a DisplayLogic token</param>
        /// <returns>SELECT-list fragment ending in a comma</returns>
        private string LogicTokenExpr(string column)
        {
            return ColumnExists(PARENT_TABLE, column)
                ? "o." + column + " AS " + column + ","
                : "NULL AS " + column + ",";
        }

        /// <summary>
        /// M_Movement columns that M_MovementLine field DisplayLogic / ReadOnlyLogic refer
        /// to by token, and that the panel therefore has to carry on the header for the
        /// logic to evaluate at all. Each is guarded, so a schema without one simply
        /// reports it as absent.
        /// </summary>
        private static readonly string[] _logicTokenColumns =
        {
            "DocumentNo", "MovementDate", "M_Warehouse_ID", "C_DocType_ID",
            "C_BPartner_ID", "AD_User_ID", "C_Project_ID", "C_Activity_ID", "C_Campaign_ID"
        };

        /// <summary>Loads the parent movement header values used as line context.</summary>
        /// <param name="ctx">session context</param>
        /// <param name="M_Movement_ID">parent movement</param>
        /// <param name="data">object to populate</param>
        private void LoadParentContext(Ctx ctx, int M_Movement_ID, MovementPanelData data)
        {
            StringBuilder logicCols = new StringBuilder();
            foreach (string c in _logicTokenColumns) logicCols.Append(LogicTokenExpr(c)).Append(' ');

            string sql = @"SELECT
                              " + logicCols + @"
                              o.M_Movement_ID,
                              o.AD_Client_ID,
                              o.AD_Org_ID,
                              o.DocStatus,
                              COALESCE(o.Processed, 'N') AS Processed,
                              COALESCE(dt.Name, N'') AS DocTypeName
                           FROM M_Movement o
                           LEFT JOIN C_DocType dt ON (dt.C_DocType_ID = o.C_DocType_ID)
                           WHERE o.M_Movement_ID = @M_Movement_ID
                             AND o.IsActive = 'Y'";
            sql = MRole.GetDefault(ctx).AddAccessSQL(sql, "o", MRole.SQL_FULLYQUALIFIED, MRole.SQL_RO);

            DataSet ds = DB.ExecuteDataset(sql,
                new SqlParameter[] { new SqlParameter("@M_Movement_ID", M_Movement_ID) }, null);
            if (ds == null || ds.Tables.Count == 0 || ds.Tables[0].Rows.Count == 0) return;

            DataRow r = ds.Tables[0].Rows[0];
            data.M_Movement_ID = Util.GetValueOfInt(r["M_Movement_ID"]);
            data.AD_Client_ID = Util.GetValueOfInt(r["AD_Client_ID"]);
            data.AD_Org_ID = Util.GetValueOfInt(r["AD_Org_ID"]);
            data.DocTypeName = Util.GetValueOfString(r["DocTypeName"]);
            // Header values named by movement-line DisplayLogic tokens. A DBNull stays
            // absent from the bag rather than becoming "" or 0, so the client can tell
            // "not set" from a real value and "@token@=null" evaluates as intended.
            foreach (string c in _logicTokenColumns)
                if (r[c] != DBNull.Value) data.LogicContext[c] = Util.GetValueOfString(r[c]);
            data.DocumentNo = LogicValue(data, "DocumentNo");
            data.M_Warehouse_ID = Util.GetValueOfInt(LogicValue(data, "M_Warehouse_ID"));
            data.C_BPartner_ID = Util.GetValueOfInt(LogicValue(data, "C_BPartner_ID"));
            data.MovementDate = ParseDate(LogicValue(data, "MovementDate"));
            data.DocStatus = Util.GetValueOfString(r["DocStatus"]);
            data.Processed = Util.GetValueOfString(r["Processed"]) == "Y";
            data.IsEditable = !data.Processed
                && data.DocStatus != "CO" && data.DocStatus != "CL"
                && data.DocStatus != "VO" && data.DocStatus != "RE";
        }

        /// <summary>Reads one header token out of the LogicContext bag ("" when absent).</summary>
        /// <param name="data">panel data</param>
        /// <param name="column">column name</param>
        /// <returns>stored value, or ""</returns>
        private static string LogicValue(MovementPanelData data, string column)
        {
            string v;
            return (data.LogicContext != null && data.LogicContext.TryGetValue(column, out v)) ? v : "";
        }

        /// <summary>Parses a header date token; null when absent or unparseable.</summary>
        /// <param name="raw">stored token value</param>
        /// <returns>parsed date, or null</returns>
        private static DateTime? ParseDate(string raw)
        {
            if (string.IsNullOrEmpty(raw)) return null;
            DateTime dt;
            if (DateTime.TryParse(raw, CultureInfo.InvariantCulture, DateTimeStyles.None, out dt)) return dt;
            if (DateTime.TryParse(raw, out dt)) return dt;
            return null;
        }

        /// <summary>Loads one page of saved movement lines, with the full column bag and display labels.</summary>
        /// <param name="ctx">session context</param>
        /// <param name="M_Movement_ID">parent movement</param>
        /// <param name="page">0-based page</param>
        /// <param name="total">receives the total line count across all pages</param>
        /// <returns>the page's rows</returns>
        private List<MovementLineRow> LoadLines(Ctx ctx, int M_Movement_ID, int page, out int total)
        {
            List<MovementLineRow> rows = new List<MovementLineRow>();

            string countSql = "SELECT COUNT(*) FROM M_MovementLine ml"
                + " WHERE ml.M_Movement_ID = @M_Movement_ID AND ml.IsActive = 'Y'";
            countSql = MRole.GetDefault(ctx).AddAccessSQL(countSql, "ml", MRole.SQL_FULLYQUALIFIED, MRole.SQL_RO);
            total = Util.GetValueOfInt(DB.ExecuteScalar(countSql,
                new SqlParameter[] { new SqlParameter("@M_Movement_ID", M_Movement_ID) }, null));

            StringBuilder cols = new StringBuilder();
            foreach (string cn in GetMovementLineColumns())
                cols.Append("ml.").Append(cn).Append(", ");
            if (cols.Length == 0)
                cols.Append("ml.M_MovementLine_ID, ml.M_Movement_ID, ml.Line, ml.M_Product_ID, ml.M_Locator_ID, ml.M_LocatorTo_ID, ml.C_UOM_ID, ml.QtyEntered, ml.MovementQty, ml.M_AttributeSetInstance_ID, ml.M_AttributeSetInstanceTo_ID, ml.Description, ");

            string sql = "SELECT " + cols.ToString() +
                @"COALESCE(p.Value, N'') AS VASMTLDISP_ProductValue,
                  COALESCE(p.Name, N'') AS VASMTLDISP_ProductName,
                  COALESCE(uom.Name, N'') AS VASMTLDISP_UOMName,
                  COALESCE(lf.Value, N'') AS VASMTLDISP_LocatorName,
                  COALESCE(lt.Value, N'') AS VASMTLDISP_LocatorToName,
                  COALESCE(asi.Description, N'') AS VASMTLDISP_AttrName,
                  COALESCE(p.M_AttributeSet_ID, 0) AS VASMTLDISP_HasAttrSet,
                  COALESCE(p.ProductType, '') AS VASMTLDISP_ProductType
               FROM M_MovementLine ml
               LEFT JOIN M_Product p ON (ml.M_Product_ID = p.M_Product_ID)
               LEFT JOIN C_UOM uom ON (ml.C_UOM_ID = uom.C_UOM_ID)
               LEFT JOIN M_Locator lf ON (ml.M_Locator_ID = lf.M_Locator_ID)
               LEFT JOIN M_Locator lt ON (ml.M_LocatorTo_ID = lt.M_Locator_ID)
               LEFT JOIN M_AttributeSetInstance asi ON (ml.M_AttributeSetInstance_ID = asi.M_AttributeSetInstance_ID)
               WHERE ml.M_Movement_ID = @M_Movement_ID
                 AND ml.IsActive = 'Y'";

            sql = MRole.GetDefault(ctx).AddAccessSQL(sql, "ml", MRole.SQL_FULLYQUALIFIED, MRole.SQL_RO);
            if (page < 0) page = 0;
            sql += " ORDER BY ml.Line, ml.M_MovementLine_ID" + PagingSuffix(LINE_PAGE_SIZE, page * LINE_PAGE_SIZE);

            DataSet ds = DB.ExecuteDataset(sql,
                new SqlParameter[] { new SqlParameter("@M_Movement_ID", M_Movement_ID) }, null);
            if (ds == null || ds.Tables.Count == 0) return rows;

            DataTable dt = ds.Tables[0];
            foreach (DataRow r in dt.Rows)
            {
                MovementLineRow row = new MovementLineRow();
                foreach (DataColumn dc in dt.Columns)
                {
                    // OrdinalIgnoreCase: PostgreSQL lowercases aliases, Oracle uppercases
                    // them — both must be excluded from the generic Values bag so only real
                    // M_MovementLine columns are sent back on save.
                    if (dc.ColumnName.StartsWith("VASMTLDISP_", StringComparison.OrdinalIgnoreCase)) continue;
                    row.Values[dc.ColumnName] = (r[dc] == DBNull.Value) ? null : r[dc];
                }
                row.M_MovementLine_ID = Util.GetValueOfInt(r["M_MovementLine_ID"]);
                row.Line = Util.GetValueOfInt(r["Line"]);
                row.M_Product_ID = Util.GetValueOfInt(r["M_Product_ID"]);
                row.ProductValue = Util.GetValueOfString(r["VASMTLDISP_ProductValue"]);
                row.ProductName = Util.GetValueOfString(r["VASMTLDISP_ProductName"]);
                row.Description = Util.GetValueOfString(r["Description"]);
                row.MovementQty = Util.GetValueOfDecimal(r["MovementQty"]);
                // QtyEntered is the quantity in the line's SELECTED unit. A line raised by a
                // process (rather than by the movement window) can carry MovementQty alone,
                // in which case the base figure IS the selected-unit figure.
                row.QtyEntered = dt.Columns.Contains("QtyEntered") ? Util.GetValueOfDecimal(r["QtyEntered"]) : row.MovementQty;
                if (row.QtyEntered == 0) row.QtyEntered = row.MovementQty;
                row.C_UOM_ID = Util.GetValueOfInt(r["C_UOM_ID"]);
                row.UOMName = Util.GetValueOfString(r["VASMTLDISP_UOMName"]);
                row.M_Locator_ID = Util.GetValueOfInt(r["M_Locator_ID"]);
                row.LocatorName = Util.GetValueOfString(r["VASMTLDISP_LocatorName"]);
                row.M_LocatorTo_ID = Util.GetValueOfInt(r["M_LocatorTo_ID"]);
                row.LocatorToName = Util.GetValueOfString(r["VASMTLDISP_LocatorToName"]);
                row.M_AttributeSetInstance_ID = Util.GetValueOfInt(r["M_AttributeSetInstance_ID"]);
                row.M_AttributeSetInstanceTo_ID = dt.Columns.Contains("M_AttributeSetInstanceTo_ID")
                    ? Util.GetValueOfInt(r["M_AttributeSetInstanceTo_ID"]) : 0;
                row.AttrName = Util.GetValueOfString(r["VASMTLDISP_AttrName"]);
                int hasAttrSetRaw = Util.GetValueOfInt(r["VASMTLDISP_HasAttrSet"]);
                row.HasAttributeSet = hasAttrSetRaw > 0;
                // Under a canonical mixed-case key so the client can read it case-insensitively
                // on both engines. Authoritative, unlike the display flag, which is OR'd with an
                // existing instance description.
                row.Values["VASMTLDISP_HasAttrSet"] = hasAttrSetRaw;
                row.ProductType = Util.GetValueOfString(r["VASMTLDISP_ProductType"]);
                rows.Add(row);
            }
            return rows;
        }

        /// <summary>
        /// Summed MovementQty across every saved line — the quantity figure the totals
        /// strip states, read from the lines so it is right the moment one is saved.
        /// </summary>
        /// <param name="ctx">session context</param>
        /// <param name="M_Movement_ID">parent movement</param>
        /// <returns>total moved quantity</returns>
        private decimal SumQty(Ctx ctx, int M_Movement_ID)
        {
            string sql = "SELECT COALESCE(SUM(ml.MovementQty), 0) AS Qty FROM M_MovementLine ml"
                + " WHERE ml.M_Movement_ID = @M_Movement_ID AND ml.IsActive = 'Y'";
            sql = MRole.GetDefault(ctx).AddAccessSQL(sql, "ml", MRole.SQL_FULLYQUALIFIED, MRole.SQL_RO);
            DataSet ds = DB.ExecuteDataset(sql,
                new SqlParameter[] { new SqlParameter("@M_Movement_ID", M_Movement_ID) }, null);
            if (ds != null && ds.Tables.Count > 0 && ds.Tables[0].Rows.Count > 0)
                return Util.GetValueOfDecimal(ds.Tables[0].Rows[0]["Qty"]);
            return 0;
        }

        private List<string> _mlColumns;

        /// <summary>Returns (and caches) the active M_MovementLine column names from the dictionary.</summary>
        /// <returns>column names</returns>
        private List<string> GetMovementLineColumns()
        {
            if (_mlColumns != null) return _mlColumns;
            _mlColumns = new List<string>();
            DataSet ds = DB.ExecuteDataset(
                @"SELECT c.ColumnName
                  FROM AD_Column c
                  INNER JOIN AD_Table t ON (c.AD_Table_ID = t.AD_Table_ID)
                  WHERE t.TableName = '" + LINE_TABLE + @"' AND c.IsActive = 'Y' AND c.ColumnSQL IS NULL ");
            if (ds != null && ds.Tables.Count > 0)
                foreach (DataRow r in ds.Tables[0].Rows)
                    _mlColumns.Add(Util.GetValueOfString(r["ColumnName"]));
            return _mlColumns;
        }

        #endregion

        #region Product catalog search

        /// <summary>
        /// Paged product catalog search for the line picker.
        ///
        /// Products only: M_MovementLine has no C_Charge_ID — a movement moves goods and
        /// never books a charge — so offering charges would offer something the save
        /// could not persist.
        /// </summary>
        /// <param name="ctx">session context</param>
        /// <param name="M_Movement_ID">parent movement (guards the call, and client scope)</param>
        /// <param name="query">typed keyword</param>
        /// <param name="pageSize">rows per page</param>
        /// <param name="offset">rows already loaded</param>
        /// <param name="rowValues">compact line context for the M_Product_ID AD_Val_Rule</param>
        /// <returns>matching catalog rows</returns>
        public List<MovementCatalogItem> SearchProductsCharges(Ctx ctx, int M_Movement_ID,
            string query, int pageSize, int offset, Dictionary<string, object> rowValues = null)
        {
            List<MovementCatalogItem> items = new List<MovementCatalogItem>();
            if (M_Movement_ID <= 0) return items;

            if (pageSize <= 0 || pageSize > CATALOG_PAGE_SIZE) pageSize = CATALOG_PAGE_SIZE;
            if (offset < 0) offset = 0;
            if (offset > 1000000) offset = 1000000;

            Dictionary<string, string> rowVars = BuildRowVars(rowValues);
            string like = "%" + (query ?? "").Trim().ToLower() + "%";

            // NOTE: every bind name occurs EXACTLY ONCE across the whole statement.
            // Oracle binds positionally, so a name reused in two places is bound twice
            // and the parameter array no longer lines up.
            string prodSql = @"SELECT p.M_Product_ID AS RecordId, 'P' AS Kind,
                                      p.Value AS SearchKey, p.Name AS DisplayName,
                                      COALESCE(p.Description, N'') AS Description,
                                      p.M_AttributeSet_ID AS AttributeSetId,
                                      COALESCE(p.ProductType, '') AS ProductType,
                                      COALESCE(p.C_UOM_ID, 0) AS UomId
                               FROM M_Product p
                               WHERE p.IsActive = 'Y'
                                 AND p.IsSummary = 'N'
                                 AND p.AD_Client_ID = " + ctx.GetAD_Client_ID() + @"
                                 AND (LOWER(p.Value) LIKE @kwPV
                                   OR LOWER(p.Name) LIKE @kwPN
                                   OR LOWER(COALESCE(p.UPC, N'')) LIKE @kwPU)";
            string prodPred = GetValRulePredicate(ctx, "M_Product_ID", "M_Product", "p", M_Movement_ID, rowVars);
            if (prodPred.Length > 0) prodSql += " AND (" + prodPred + ")";
            prodSql = MRole.GetDefault(ctx).AddAccessSQL(prodSql, "p", MRole.SQL_FULLYQUALIFIED, MRole.SQL_RO);

            string combined = "SELECT x.RecordId, x.Kind, x.SearchKey, x.DisplayName, x.Description,"
                + " x.AttributeSetId, x.ProductType, x.UomId"
                + " FROM (" + prodSql + ") x"
                + " ORDER BY x.DisplayName" + PagingSuffix(pageSize, offset);

            DataSet ds = DB.ExecuteDataset(combined, new SqlParameter[] {
                new SqlParameter("@kwPV", like),
                new SqlParameter("@kwPN", like),
                new SqlParameter("@kwPU", like)
            }, null);
            if (ds == null || ds.Tables.Count == 0)
            {
                log.Severe("VAS_247 SearchProductsCharges SQL failed. Term: " + like);
                return items;
            }

            Dictionary<int, string> uomNames = LoadUomNames(ctx, M_Movement_ID);
            foreach (DataRow r in ds.Tables[0].Rows)
            {
                MovementCatalogItem it = new MovementCatalogItem();
                it.RecordId = Util.GetValueOfInt(r["RecordId"]);
                it.Kind = Util.GetValueOfString(r["Kind"]);
                it.SearchKey = Util.GetValueOfString(r["SearchKey"]);
                it.DisplayName = Util.GetValueOfString(r["DisplayName"]);
                it.Description = Util.GetValueOfString(r["Description"]);
                it.HasAttributeSet = Util.GetValueOfInt(r["AttributeSetId"]) > 0;
                it.ProductType = Util.GetValueOfString(r["ProductType"]);
                it.C_UOM_ID = Util.GetValueOfInt(r["UomId"]);
                if (it.C_UOM_ID > 0 && uomNames.ContainsKey(it.C_UOM_ID)) it.UomName = uomNames[it.C_UOM_ID];
                items.Add(it);
            }
            return items;
        }

        /// <summary>C_UOM_ID -&gt; display symbol, so a catalog row can label its own unit.</summary>
        /// <param name="ctx">session context</param>
        /// <param name="M_Movement_ID">parent movement (val-rule context)</param>
        /// <returns>unit id to symbol map</returns>
        private Dictionary<int, string> LoadUomNames(Ctx ctx, int M_Movement_ID)
        {
            Dictionary<int, string> map = new Dictionary<int, string>();
            foreach (MovementUomItem u in LoadUomList(ctx, M_Movement_ID, null)) map[u.C_UOM_ID] = u.Symbol;
            return map;
        }

        /// <summary>Looks up a single product by a scanned barcode (UPC or search key).</summary>
        /// <param name="ctx">session context</param>
        /// <param name="M_Movement_ID">parent movement (client scope)</param>
        /// <param name="code">scanned code</param>
        /// <returns>the matched catalog row, or an empty item</returns>
        public MovementCatalogItem ScanLookup(Ctx ctx, int M_Movement_ID, string code)
        {
            MovementCatalogItem none = new MovementCatalogItem();
            if (M_Movement_ID <= 0 || string.IsNullOrEmpty(code)) return none;
            string key = code.Trim();

            string prodSql = @"SELECT p.M_Product_ID AS RecordId, 'P' AS Kind, p.Value AS SearchKey,
                                      p.Name AS DisplayName, COALESCE(p.Description, N'') AS Description,
                                      p.M_AttributeSet_ID AS AttributeSetId,
                                      COALESCE(p.ProductType, '') AS ProductType,
                                      COALESCE(p.C_UOM_ID, 0) AS UomId
                               FROM M_Product p
                               WHERE p.IsActive = 'Y'
                                 AND p.AD_Client_ID = " + ctx.GetAD_Client_ID() + @"
                                 AND (UPPER(p.UPC) = UPPER(@code) OR UPPER(p.Value) = UPPER(@code))";
            string scanPred = GetValRulePredicate(ctx, "M_Product_ID", "M_Product", "p", M_Movement_ID, null);
            if (scanPred.Length > 0) prodSql += " AND (" + scanPred + ")";
            prodSql = MRole.GetDefault(ctx).AddAccessSQL(prodSql, "p", MRole.SQL_FULLYQUALIFIED, MRole.SQL_RO);

            DataSet ds = DB.ExecuteDataset(prodSql, new SqlParameter[] { new SqlParameter("@code", key) }, null);
            if (ds == null || ds.Tables.Count == 0 || ds.Tables[0].Rows.Count == 0) return none;

            DataRow r = ds.Tables[0].Rows[0];
            MovementCatalogItem it = new MovementCatalogItem();
            it.RecordId = Util.GetValueOfInt(r["RecordId"]);
            it.Kind = "P";
            it.SearchKey = Util.GetValueOfString(r["SearchKey"]);
            it.DisplayName = Util.GetValueOfString(r["DisplayName"]);
            it.Description = Util.GetValueOfString(r["Description"]);
            it.HasAttributeSet = Util.GetValueOfInt(r["AttributeSetId"]) > 0;
            it.ProductType = Util.GetValueOfString(r["ProductType"]);
            it.C_UOM_ID = Util.GetValueOfInt(r["UomId"]);
            return it;
        }

        #endregion

        #region AD_Val_Rule enforcement

        private Dictionary<string, string> _valRuleByColumn;
        private Dictionary<string, string> _movVars;

        /// <summary>Returns the SQL validation code linked to a M_MovementLine lookup column.</summary>
        /// <param name="columnName">lookup column</param>
        /// <returns>val-rule code, or "" when none is configured</returns>
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
                  WHERE t.TableName = '" + LINE_TABLE + @"'
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
        /// Resolves a lookup column's AD_Val_Rule into a SQL predicate, substituting
        /// @tokens@ from the line values first, then the movement header, then the session
        /// context. A rule with an unresolved token is dropped rather than applied wrong.
        /// </summary>
        /// <param name="ctx">session context</param>
        /// <param name="columnName">lookup column on M_MovementLine</param>
        /// <param name="tableName">lookup table the rule is written against</param>
        /// <param name="alias">alias the outer query gives that table</param>
        /// <param name="M_Movement_ID">parent movement</param>
        /// <param name="rowVars">current line values, or null</param>
        /// <returns>predicate, or "" when there is no usable rule</returns>
        private string GetValRulePredicate(Ctx ctx, string columnName, string tableName, string alias,
            int M_Movement_ID, Dictionary<string, string> rowVars)
        {
            string code = GetColumnValRule(columnName);
            if (string.IsNullOrEmpty(code)) return "";

            string frag = Regex.Replace(code, @"\b" + Regex.Escape(tableName) + @"\.", alias + ".", RegexOptions.IgnoreCase);

            Dictionary<string, string> oVars = GetMovementVars(ctx, M_Movement_ID);
            // Set when an FK token resolves to id 0 — see the guard below.
            bool zeroId = false;
            frag = Regex.Replace(frag, @"@(#?[A-Za-z0-9_]+)@", delegate (Match m)
            {
                string token = m.Groups[1].Value;
                string k = token.TrimStart('#');
                string val = null;
                if (rowVars == null || !rowVars.TryGetValue(k, out val))
                {
                    if (!oVars.TryGetValue(k, out val)) val = GetCtxLiteral(ctx, token);
                }
                if (val == null) return m.Value;
                if (k.EndsWith("_ID", StringComparison.OrdinalIgnoreCase) && val == "0") zeroId = true;
                return val;
            });

            if (frag.IndexOf('@') >= 0)
            {
                log.Warning("VAS_247 val rule skipped for " + columnName + " (unresolved context): " + code);
                return "";
            }
            // An FK token that resolved to 0 means the document simply does not carry that
            // reference — most often @M_Warehouse_ID@ on a movement, which crosses
            // warehouses and therefore usually has no header warehouse at all. Substituting
            // it leaves a predicate like "M_Warehouse_ID = 0", which no row can satisfy, so
            // the lookup would come back EMPTY rather than merely narrowed. Dropping the
            // rule and offering the role-secured, client-scoped list is the honest answer:
            // it is what the user can legitimately choose from, and it is what this panel
            // showed before the rule was applied at all.
            if (zeroId)
            {
                log.Fine("VAS_247 val rule skipped for " + columnName
                    + " (an id token resolved to 0, which would match no row): " + code);
                return "";
            }
            return frag;
        }

        /// <summary>Reads a session-context token as a SQL literal, or null when unset.</summary>
        /// <param name="ctx">session context</param>
        /// <param name="token">context key, with or without a leading '#'</param>
        /// <returns>SQL literal, or null</returns>
        private string GetCtxLiteral(Ctx ctx, string token)
        {
            string v = ctx.GetContext(token);
            if (string.IsNullOrEmpty(v) && !token.StartsWith("#")) v = ctx.GetContext("#" + token);
            if (string.IsNullOrEmpty(v)) return null;
            return ToSqlLiteral(v);
        }

        /// <summary>Converts a compact line-value bag into SQL literals for val-rule substitution.</summary>
        /// <param name="rowValues">line values as sent by the client</param>
        /// <returns>column name to SQL literal map</returns>
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

        /// <summary>Renders a deserialized JSON value as a SQL literal.</summary>
        /// <param name="v">value</param>
        /// <returns>SQL literal</returns>
        private string ObjToSqlLiteral(object v)
        {
            if (v is bool) return ((bool)v) ? "'Y'" : "'N'";
            if (v is long || v is int || v is short || v is double || v is float || v is decimal)
                return Convert.ToString(v, CultureInfo.InvariantCulture);
            return ToSqlLiteral(Convert.ToString(v, CultureInfo.InvariantCulture));
        }

        /// <summary>Quotes a string as a SQL literal, leaving a numeric value bare.</summary>
        /// <param name="v">raw value</param>
        /// <returns>SQL literal</returns>
        private string ToSqlLiteral(string v)
        {
            decimal d;
            if (decimal.TryParse(v, NumberStyles.Any, CultureInfo.InvariantCulture, out d)) return v;
            return "'" + v.Replace("'", "''") + "'";
        }

        /// <summary>
        /// Parent-movement context values (as SQL literals) for val-rule substitution.
        /// IsSOTrx is stated as 'N': a stock movement is not a sales transaction, and the
        /// standard product val rules gate on that token.
        /// </summary>
        /// <param name="ctx">session context</param>
        /// <param name="M_Movement_ID">parent movement</param>
        /// <returns>token to SQL literal map</returns>
        private Dictionary<string, string> GetMovementVars(Ctx ctx, int M_Movement_ID)
        {
            if (_movVars != null) return _movVars;
            _movVars = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            // Both are read under a guard: a val rule naming one must not take down the
            // whole lookup on a schema that lacks it.
            string bpCol = ColumnExists(PARENT_TABLE, "C_BPartner_ID") ? "m.C_BPartner_ID" : "0";
            string whCol = ColumnExists(PARENT_TABLE, "M_Warehouse_ID") ? "m.M_Warehouse_ID" : "0";
            DataSet ds = DB.ExecuteDataset(
                @"SELECT m.AD_Client_ID, m.AD_Org_ID,
                         " + bpCol + @" AS C_BPartner_ID,
                         " + whCol + @" AS M_Warehouse_ID
                  FROM M_Movement m
                  WHERE m.M_Movement_ID = @id",
                new SqlParameter[] { new SqlParameter("@id", M_Movement_ID) }, null);
            if (ds != null && ds.Tables.Count > 0 && ds.Tables[0].Rows.Count > 0)
            {
                DataRow r = ds.Tables[0].Rows[0];
                _movVars["AD_Client_ID"] = Util.GetValueOfInt(r["AD_Client_ID"]).ToString();
                _movVars["AD_Org_ID"] = Util.GetValueOfInt(r["AD_Org_ID"]).ToString();
                _movVars["C_BPartner_ID"] = Util.GetValueOfInt(r["C_BPartner_ID"]).ToString();
                _movVars["M_Warehouse_ID"] = Util.GetValueOfInt(r["M_Warehouse_ID"]).ToString();
                _movVars["M_Movement_ID"] = M_Movement_ID.ToString();
                _movVars["IsSOTrx"] = "'N'";
            }
            return _movVars;
        }

        #endregion

        #region Line callout (server-side unit + quantity conversion)

        /// <summary>Database-specific OFFSET/FETCH vs LIMIT/OFFSET paging suffix.</summary>
        /// <param name="pageSize">rows to return</param>
        /// <param name="offset">rows to skip</param>
        /// <returns>SQL suffix</returns>
        private string PagingSuffix(int pageSize, int offset)
        {
            if (pageSize <= 0) pageSize = CATALOG_PAGE_SIZE;
            if (offset < 0) offset = 0;
            if (DB.IsOracle())
                return " OFFSET " + offset + " ROWS FETCH NEXT " + pageSize + " ROWS ONLY";
            return " LIMIT " + pageSize + " OFFSET " + offset;
        }

        /// <summary>
        /// Reads the AD_Column.Callout for the changed column and returns the values the
        /// framework would derive, as a patch the client applies to the line.
        ///
        /// For a movement there is nothing to price: what a product / unit / quantity
        /// change actually determines is the line's unit and the base-unit MovementQty
        /// that MMovementLine.BeforeSave will store. No row is written.
        /// </summary>
        /// <param name="ctx">session context</param>
        /// <param name="req">current line state</param>
        /// <returns>changed columns + display labels</returns>
        public MovementCalloutResult RunColumnCallout(Ctx ctx, MovementLineCalcRequest req)
        {
            MovementCalloutResult res = new MovementCalloutResult();
            if (req == null || req.M_Movement_ID <= 0) return res;

            string column = MapTriggerToColumn(req.TriggerColumn);
            res.Column = column;
            res.Callout = ReadColumnCallout(ctx, LINE_TABLE, column);

            if (req.M_Product_ID <= 0) return res;

            int uom = req.C_UOM_ID;
            if (uom <= 0) uom = GetProductUomId(ctx, req.M_Product_ID);

            decimal entered = req.QtyEntered > 0 ? req.QtyEntered : (req.MovementQty > 0 ? req.MovementQty : 1);
            decimal baseQty = ConvertToBaseQty(ctx, req.M_Product_ID, uom, entered);

            res.Values["C_UOM_ID"] = uom;
            res.Values["QtyEntered"] = entered;
            res.Values["MovementQty"] = baseQty;
            res.Display["uomName"] = GetUomLabel(ctx, uom);
            return res;
        }

        /// <summary>
        /// Converts a quantity keyed in the line's SELECTED unit into the product's BASE
        /// unit — the pair MMovementLine.BeforeSave maintains. A product with no
        /// conversion defined for that unit returns null from the framework, in which case
        /// the entered figure stands rather than the line collapsing to zero.
        /// </summary>
        /// <param name="ctx">session context</param>
        /// <param name="M_Product_ID">product being moved</param>
        /// <param name="C_UOM_ID">the line's selected unit</param>
        /// <param name="entered">quantity as keyed</param>
        /// <returns>quantity in the product's base unit</returns>
        private decimal ConvertToBaseQty(Ctx ctx, int M_Product_ID, int C_UOM_ID, decimal entered)
        {
            if (M_Product_ID <= 0 || C_UOM_ID <= 0) return entered;
            int productUom = GetProductUomId(ctx, M_Product_ID);
            if (productUom <= 0 || productUom == C_UOM_ID) return entered;
            decimal? conv = MUOMConversion.ConvertProductFrom(ctx, M_Product_ID, C_UOM_ID, entered);
            if (conv != null && conv.Value != 0) return conv.Value;
            return entered;
        }

        /// <summary>Reads AD_Column.Callout for one column of a table.</summary>
        /// <param name="ctx">session context</param>
        /// <param name="tableName">table</param>
        /// <param name="columnName">column</param>
        /// <returns>callout string, or ""</returns>
        private string ReadColumnCallout(Ctx ctx, string tableName, string columnName)
        {
            if (string.IsNullOrEmpty(columnName)) return "";
            object o = DB.ExecuteScalar(
                @"SELECT c.Callout
                  FROM AD_Column c
                  INNER JOIN AD_Table t ON (c.AD_Table_ID = t.AD_Table_ID)
                  WHERE t.TableName = @t
                    AND c.ColumnName = @c
                    AND c.IsActive = 'Y'",
                new SqlParameter[] { new SqlParameter("@t", tableName), new SqlParameter("@c", columnName) }, null);
            return Util.GetValueOfString(o);
        }

        /// <summary>Normalises a client trigger name to a dictionary column name.</summary>
        /// <param name="trigger">trigger sent by the panel</param>
        /// <returns>column name</returns>
        private string MapTriggerToColumn(string trigger)
        {
            switch (trigger)
            {
                case "product": return "M_Product_ID";
                case "quantity": return "QtyEntered";
                case "uom": return "C_UOM_ID";
                default: return trigger ?? "";
            }
        }

        /// <summary>Reads a unit's display name.</summary>
        /// <param name="ctx">session context</param>
        /// <param name="C_UOM_ID">unit</param>
        /// <returns>name, or ""</returns>
        private string GetUomLabel(Ctx ctx, int C_UOM_ID)
        {
            if (C_UOM_ID <= 0) return "";
            object o = DB.ExecuteScalar(
                "SELECT COALESCE(UOMSymbol, Name) FROM C_UOM WHERE C_UOM_ID = @id",
                new SqlParameter[] { new SqlParameter("@id", C_UOM_ID) }, null);
            return Util.GetValueOfString(o);
        }

        private Dictionary<int, int> _productUom;

        /// <summary>
        /// The product's own stocking unit (M_Product.C_UOM_ID) — the BASE unit every
        /// quantity on the line is converted to. Cached per instance.
        /// </summary>
        /// <param name="ctx">session context</param>
        /// <param name="productId">M_Product_ID</param>
        /// <returns>C_UOM_ID, or 0 when the product does not exist</returns>
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

        /// <summary>
        /// Returns the product's attribute-set definition (attributes + allowed values)
        /// for the attribute picker dialog.
        /// </summary>
        /// <param name="ctx">session context</param>
        /// <param name="M_Product_ID">product whose attribute set is read</param>
        /// <returns>attribute-set definition; empty object when no set is configured</returns>
        public MovementAttributeSetInfo GetProductAttributes(Ctx ctx, int M_Product_ID)
        {
            MovementAttributeSetInfo info = new MovementAttributeSetInfo();
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

            Dictionary<int, MovementAttributeDef> map = new Dictionary<int, MovementAttributeDef>();
            foreach (DataRow r in ds.Tables[0].Rows)
            {
                int attrId = Util.GetValueOfInt(r["M_Attribute_ID"]);
                MovementAttributeDef def;
                if (!map.TryGetValue(attrId, out def))
                {
                    def = new MovementAttributeDef();
                    def.M_Attribute_ID = attrId;
                    def.Name = Util.GetValueOfString(r["AttributeName"]);
                    def.ValueType = Util.GetValueOfString(r["AttributeValueType"]);
                    def.IsInstanceAttribute = Util.GetValueOfString(r["IsInstanceAttribute"]) == "Y";
                    def.IsMandatory = Util.GetValueOfString(r["IsMandatory"]) == "Y";
                    def.Values = new List<MovementAttributeValueDef>();
                    map[attrId] = def;
                    info.Attributes.Add(def);
                }
                int valId = Util.GetValueOfInt(r["M_AttributeValue_ID"]);
                if (valId > 0)
                {
                    MovementAttributeValueDef v = new MovementAttributeValueDef();
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
        /// used to pre-populate the edit form when the user opens an already-assigned one.
        /// </summary>
        /// <param name="ctx">session context</param>
        /// <param name="M_AttributeSetInstance_ID">instance whose values are read</param>
        /// <returns>typed attribute values; empty when the instance does not exist</returns>
        public List<MovementAttributeInstanceValue> GetInstanceValues(Ctx ctx, int M_AttributeSetInstance_ID)
        {
            List<MovementAttributeInstanceValue> list = new List<MovementAttributeInstanceValue>();
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
                MovementAttributeInstanceValue v = new MovementAttributeInstanceValue();
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
        /// Creates or updates an M_AttributeSetInstance from the picker selection by
        /// delegating to the framework's PAttributesModel.SaveAttribute, so dedup,
        /// mandatory validation and AttrCode / UPC behaviour stay identical to the
        /// standard attribute control.
        /// </summary>
        /// <param name="ctx">session context</param>
        /// <param name="req">attribute instance create / update request</param>
        /// <returns>new instance id + description, or Error text on failure</returns>
        public MovementAttributeSaveResult SaveAttribute(Ctx ctx, MovementAttributeSaveRequest req)
        {
            MovementAttributeSaveResult res = new MovementAttributeSaveResult();
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
        /// Maps the picker selection onto the positional List&lt;KeyNamePair&gt; expected by
        /// PAttributesModel.SaveAttribute: one entry per instance attribute, in
        /// M_AttributeSet order, typed by value type. A missing selection yields an empty
        /// placeholder so positional indexing stays aligned and the framework's mandatory
        /// check still fires correctly.
        /// </summary>
        /// <param name="aset">attribute set definition for the product</param>
        /// <param name="selections">values entered by the user in the picker</param>
        /// <returns>positionally-aligned value list</returns>
        private List<KeyNamePair> BuildAttributeValueList(MAttributeSet aset, List<MovementAttributeValueSelection> selections)
        {
            List<KeyNamePair> values = new List<KeyNamePair>();
            if (aset == null) return values;

            Dictionary<int, MovementAttributeValueSelection> byAttr = new Dictionary<int, MovementAttributeValueSelection>();
            if (selections != null)
            {
                foreach (MovementAttributeValueSelection sel in selections)
                {
                    if (sel != null && sel.M_Attribute_ID > 0)
                        byAttr[sel.M_Attribute_ID] = sel;
                }
            }

            MAttribute[] attributes = aset.GetMAttributes(true);
            foreach (MAttribute attr in attributes)
            {
                MovementAttributeValueSelection sel;
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

        #region Write actions (insert / update / delete M_MovementLine)

        /// <summary>
        /// Columns SaveLines writes itself through the business setters. ApplyExtraColumns
        /// must never touch them — in particular QtyEntered, whose stale client value would
        /// otherwise be re-applied AFTER the fresh one and make BeforeSave recompute
        /// MovementQty from the old quantity.
        /// </summary>
        private static readonly HashSet<string> CORE_OR_SYSTEM_COLUMNS = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            "M_MovementLine_ID", "M_Movement_ID", "AD_Client_ID", "AD_Org_ID",
            "Created", "CreatedBy", "Updated", "UpdatedBy", "IsActive",
            "M_Product_ID", "M_AttributeSetInstance_ID", "M_AttributeSetInstanceTo_ID",
            "M_Locator_ID", "M_LocatorTo_ID",
            "QtyEntered", "MovementQty", "C_UOM_ID",
            "Line", "Description"
        };

        private HashSet<string> _updateableColumns;
        private HashSet<string> _yesNoColumns;
        private HashSet<string> _referenceColumns;

        /// <summary>Persists every non-core, updateable M_MovementLine column through PO.Set_Value.</summary>
        /// <param name="line">line being saved</param>
        /// <param name="values">full client column bag</param>
        /// <param name="touched">columns the user actually changed in the Additional Info modal</param>
        private void ApplyExtraColumns(MMovementLine line, Dictionary<string, object> values, HashSet<string> touched)
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
                    if (GetReferenceColumns().Contains(col))
                    {
                        decimal number;
                        bool blank = val == null
                            || (decimal.TryParse(Convert.ToString(val, CultureInfo.InvariantCulture),
                                    NumberStyles.Any, CultureInfo.InvariantCulture, out number) && number == 0);
                        if (blank)
                        {
                            // An intentional clear is persisted as NULL; an untouched blank FK
                            // is left alone so a default set elsewhere is not wiped.
                            if (isTouched) line.Set_Value(col, null);
                            continue;
                        }
                    }
                    line.Set_Value(col, val);
                }
                catch (Exception ex) { log.Warning("VAS_247 SaveLines: skip column " + col + " - " + ex.Message); }
            }
        }

        /// <summary>Coerces a deserialized JSON scalar into the CLR type the column expects.</summary>
        /// <param name="v">value from the client bag</param>
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
            string s = v as string;
            if (s != null && Regex.IsMatch(s, @"^\d{4}-\d{2}-\d{2}(T\d{2}:\d{2}(:\d{2})?)?$"))
            {
                DateTime dt;
                if (DateTime.TryParse(s, CultureInfo.InvariantCulture, DateTimeStyles.None, out dt))
                    return dt;
            }
            return v;
        }

        /// <summary>Loads the updateable / YesNo / reference column sets once per instance.</summary>
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
                  WHERE t.TableName = '" + LINE_TABLE + @"'
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

        /// <summary>Reads a client YesNo value as a bool.</summary>
        /// <param name="v">value</param>
        /// <returns>whether it means yes</returns>
        private static bool CoerceYesNo(object v)
        {
            if (v is bool) return (bool)v;
            string s = Util.GetValueOfString(v).Trim();
            return s.Equals("Y", StringComparison.OrdinalIgnoreCase)
                || s.Equals("true", StringComparison.OrdinalIgnoreCase)
                || s == "1";
        }

        /// <summary>
        /// Inserts or updates the supplied movement lines through MMovementLine. All lines
        /// share a single transaction, so a failure rolls the whole batch back and the grid
        /// never ends up half-saved.
        /// </summary>
        /// <param name="ctx">session context</param>
        /// <param name="M_Movement_ID">parent movement</param>
        /// <param name="AD_Window_ID">source window</param>
        /// <param name="rows">lines to write</param>
        /// <param name="page">page to return after the write</param>
        /// <returns>refreshed page, or the failure</returns>
        public MovementSaveResult SaveLines(Ctx ctx, int M_Movement_ID, int AD_Window_ID, List<MovementLineInput> rows, int page = 0)
        {
            MovementSaveResult res = new MovementSaveResult();
            res.LinePageSize = LINE_PAGE_SIZE;
            if (M_Movement_ID <= 0 || rows == null || rows.Count == 0)
            {
                res.ErrorKey = MSG + "NothingToSave";
                return res;
            }

            MovementPanelData head = new MovementPanelData();
            LoadParentContext(ctx, M_Movement_ID, head);
            if (head.M_Movement_ID <= 0) { res.ErrorKey = MSG + "NoAccess"; return res; }
            if (!head.IsEditable) { res.ErrorKey = MSG + "NotEditable"; return res; }

            Trx trx = Trx.GetTrx(Trx.CreateTrxName("VAS247Save_" + M_Movement_ID));
            try
            {
                MMovement movement = new MMovement(ctx, M_Movement_ID, trx);
                foreach (MovementLineInput input in rows)
                {
                    // A movement line without a product is meaningless — there is no charge
                    // alternative on this table — so skip rather than fail the batch.
                    if (input.M_Product_ID <= 0) continue;

                    MMovementLine line = input.M_MovementLine_ID > 0
                        ? new MMovementLine(ctx, input.M_MovementLine_ID, trx)
                        : new MMovementLine(movement);

                    // SetM_Product_ID resets M_AttributeSetInstance_ID, so the instance is
                    // applied AFTER the product, never before.
                    line.SetM_Product_ID(input.M_Product_ID);
                    line.SetM_AttributeSetInstance_ID(input.M_AttributeSetInstance_ID);
                    if (input.M_AttributeSetInstanceTo_ID > 0)
                        line.SetM_AttributeSetInstanceTo_ID(input.M_AttributeSetInstanceTo_ID);

                    if (input.M_Locator_ID > 0) line.SetM_Locator_ID(input.M_Locator_ID);
                    if (input.M_LocatorTo_ID > 0) line.SetM_LocatorTo_ID(input.M_LocatorTo_ID);

                    // C_UOM_ID has no generated accessor on MMovementLine, so it is set
                    // through the generic column bag — and BEFORE the quantity, because
                    // MMovementLine.BeforeSave reads it to derive MovementQty from
                    // QtyEntered via MUOMConversion.
                    int uom = input.C_UOM_ID > 0 ? input.C_UOM_ID : GetProductUomId(ctx, input.M_Product_ID);
                    if (uom > 0 && line.Get_ColumnIndex("C_UOM_ID") >= 0) line.Set_Value("C_UOM_ID", uom);

                    decimal entered = input.QtyEntered > 0 ? input.QtyEntered : (input.MovementQty > 0 ? input.MovementQty : 1);
                    line.SetQtyEntered(entered);

                    if (input.Line > 0) line.SetLine(input.Line);
                    line.SetDescription(input.Description ?? "");

                    HashSet<string> touchedCols = (input.TouchedCols != null && input.TouchedCols.Count > 0)
                        ? new HashSet<string>(input.TouchedCols, StringComparer.OrdinalIgnoreCase)
                        : null;
                    ApplyExtraColumns(line, input.Values, touchedCols);

                    if (!line.Save())
                    {
                        string err = FrameworkError(ctx);
                        log.Warning("VAS_247 SaveLines: line save failed (Line " + input.Line + ") - " + err);
                        res.LineErrors.Add(new MovementLineSaveError
                        {
                            RowKey = input.RowKey,
                            M_MovementLine_ID = input.M_MovementLine_ID,
                            Line = input.Line,
                            Message = err
                        });
                    }
                }

                if (res.LineErrors.Count > 0)
                {
                    trx.Rollback();
                    res.ErrorKey = MSG + "SaveFailed";
                    res.ErrorDetail = res.LineErrors[0].Message;
                    return res;
                }
                trx.Commit();
            }
            catch (Exception ex)
            {
                trx.Rollback();
                log.Log(Level.SEVERE, "VAS_247 SaveLines failed", ex);
                res.ErrorKey = MSG + "SaveFailed";
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
            res.Lines = LoadLines(ctx, M_Movement_ID, page, out total);
            res.LinesTotal = total;
            res.LinePage = page;
            res.TotalQty = SumQty(ctx, M_Movement_ID);
            return res;
        }

        /// <summary>Deletes the supplied saved movement lines through MMovementLine.</summary>
        /// <param name="ctx">session context</param>
        /// <param name="M_Movement_ID">parent movement</param>
        /// <param name="AD_Window_ID">source window</param>
        /// <param name="lineIds">lines to remove</param>
        /// <param name="page">page to return after the delete</param>
        /// <returns>refreshed page, or the failure</returns>
        public MovementSaveResult DeleteLines(Ctx ctx, int M_Movement_ID, int AD_Window_ID, List<int> lineIds, int page = 0)
        {
            MovementSaveResult res = new MovementSaveResult();
            res.LinePageSize = LINE_PAGE_SIZE;
            if (M_Movement_ID <= 0 || lineIds == null || lineIds.Count == 0)
            {
                res.ErrorKey = MSG + "NothingToSave";
                return res;
            }

            MovementPanelData head = new MovementPanelData();
            LoadParentContext(ctx, M_Movement_ID, head);
            if (head.M_Movement_ID <= 0) { res.ErrorKey = MSG + "NoAccess"; return res; }
            if (!head.IsEditable) { res.ErrorKey = MSG + "NotEditable"; return res; }

            Trx trx = Trx.GetTrx(Trx.CreateTrxName("VAS247Delete_" + M_Movement_ID));
            try
            {
                foreach (int id in lineIds)
                {
                    if (id <= 0) continue;
                    MMovementLine line = new MMovementLine(ctx, id, trx);
                    // Guard against a stale / forged id: the line must exist AND belong to
                    // the movement the panel is bound to.
                    if (line.Get_ID() != id) continue;
                    if (line.GetM_Movement_ID() != M_Movement_ID) continue;
                    if (!line.Delete(true, trx))
                    {
                        trx.Rollback();
                        string err = FrameworkError(ctx);
                        if (string.IsNullOrEmpty(err)) err = Msg.GetMsg(ctx, MSG + "DeleteFailed");
                        log.Warning("VAS_247 DeleteLines: delete failed for line " + id);
                        res.ErrorKey = err;
                        return res;
                    }
                }
                trx.Commit();
            }
            catch (Exception ex)
            {
                trx.Rollback();
                log.Log(Level.SEVERE, "VAS_247 DeleteLines failed", ex);
                res.ErrorKey = MSG + "DeleteFailed";
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
            res.Lines = LoadLines(ctx, M_Movement_ID, page, out total);
            // Deleting the last row of the last page leaves the pager past the end — step
            // back one page so the grid is never blank with rows still behind it.
            int pageCount = Math.Max(1, (int)Math.Ceiling(total / (double)LINE_PAGE_SIZE));
            if (page > pageCount - 1)
            {
                page = pageCount - 1;
                res.Lines = LoadLines(ctx, M_Movement_ID, page, out total);
            }
            res.LinesTotal = total;
            res.LinePage = page;
            res.TotalQty = SumQty(ctx, M_Movement_ID);
            return res;
        }

        /// <summary>
        /// Pops the framework's last error off the logger and translates it. The business
        /// classes report a failed Save()/Delete() this way rather than by throwing, so
        /// this is the only place the real reason can be read.
        /// </summary>
        /// <param name="ctx">session context</param>
        /// <returns>translated message, or "" when none was recorded</returns>
        private static string FrameworkError(Ctx ctx)
        {
            ValueNamePair pp = VLogger.RetrieveError();
            if (pp == null) return string.Empty;
            string val = pp.GetName();
            if (string.IsNullOrEmpty(val)) val = Msg.GetMsg(ctx, pp.GetValue());
            return val ?? string.Empty;
        }

        #endregion
    }

    #region Data contracts — VAS_247 specific

    /// <summary>Parent movement context + saved lines returned to the panel on load.</summary>
    public class MovementPanelData
    {
        public int M_Movement_ID { get; set; }
        public int AD_Client_ID { get; set; }
        public int AD_Org_ID { get; set; }
        /// <summary>M_Movement.DocumentNo — shown in the panel subtitle.</summary>
        public string DocumentNo { get; set; }
        /// <summary>C_DocType name, when the movement carries one.</summary>
        public string DocTypeName { get; set; }
        /// <summary>M_Movement.C_BPartner_ID where the schema has it (0 otherwise).</summary>
        public int C_BPartner_ID { get; set; }
        /// <summary>Header warehouse where the schema has one; a movement often has none.</summary>
        public int M_Warehouse_ID { get; set; }
        public DateTime? MovementDate { get; set; }
        /// <summary>
        /// Header values that M_MovementLine field DisplayLogic / ReadOnlyLogic name as
        /// tokens. A column that is NULL on the movement is ABSENT from this bag, which
        /// the client resolves to "" — so "@token@=null" matches and "@token@&gt;0" does not.
        /// </summary>
        public Dictionary<string, string> LogicContext { get; set; }
        public string DocStatus { get; set; }
        public bool Processed { get; set; }
        public bool IsEditable { get; set; }
        public int LinesTotal { get; set; }
        public int LinePage { get; set; }
        public int LinePageSize { get; set; }
        /// <summary>Summed MovementQty across ALL pages, not just the loaded one.</summary>
        public decimal TotalQty { get; set; }
        public int AD_Window_ID { get; set; }
        public int AD_Tab_ID { get; set; }
        public List<int> AD_Tab_IDs { get; set; }
        public List<MovementLineRow> Lines { get; set; }
        public List<MovementUomItem> UomList { get; set; }
        /// <summary>Options for the SOURCE locator (M_Locator_ID).</summary>
        public List<MovementLocatorItem> LocatorList { get; set; }
        /// <summary>
        /// Options for the DESTINATION locator (M_LocatorTo_ID). A separate list because
        /// that column carries its own AD_Val_Rule.
        /// </summary>
        public List<MovementLocatorItem> LocatorToList { get; set; }
        public List<MovementColumnMeta> Columns { get; set; }
        public Dictionary<string, string> LoginContext { get; set; }

        public MovementPanelData()
        {
            Lines = new List<MovementLineRow>();
            UomList = new List<MovementUomItem>();
            LocatorList = new List<MovementLocatorItem>();
            LocatorToList = new List<MovementLocatorItem>();
            Columns = new List<MovementColumnMeta>();
            AD_Tab_IDs = new List<int>();
            LoginContext = new Dictionary<string, string>();
            LogicContext = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            DocumentNo = "";
            DocTypeName = "";
            DocStatus = "";
        }
    }

    /// <summary>A saved movement line as the panel grid renders it.</summary>
    public class MovementLineRow
    {
        public int M_MovementLine_ID { get; set; }
        public int Line { get; set; }
        public int M_Product_ID { get; set; }
        public string ProductValue { get; set; }
        public string ProductName { get; set; }
        public string Description { get; set; }
        /// <summary>Quantity in the line's SELECTED unit — what the user typed.</summary>
        public decimal QtyEntered { get; set; }
        /// <summary>Quantity in the product's BASE unit — what the document moves.</summary>
        public decimal MovementQty { get; set; }
        public int C_UOM_ID { get; set; }
        public string UOMName { get; set; }
        /// <summary>Source locator — the bin the stock leaves.</summary>
        public int M_Locator_ID { get; set; }
        public string LocatorName { get; set; }
        /// <summary>Destination locator — the bin the stock arrives in.</summary>
        public int M_LocatorTo_ID { get; set; }
        public string LocatorToName { get; set; }
        public int M_AttributeSetInstance_ID { get; set; }
        public int M_AttributeSetInstanceTo_ID { get; set; }
        public string AttrName { get; set; }
        public bool HasAttributeSet { get; set; }
        public string ProductType { get; set; }
        /// <summary>Every M_MovementLine column, so the modal and the save carry them all.</summary>
        public Dictionary<string, object> Values { get; set; }

        public MovementLineRow()
        {
            Values = new Dictionary<string, object>();
            ProductValue = "";
            ProductName = "";
            Description = "";
            UOMName = "";
            LocatorName = "";
            LocatorToName = "";
            AttrName = "";
            ProductType = "";
        }
    }

    /// <summary>Request for the per-row UOM / locator lookup re-filter.</summary>
    public class MovementLookupRequest
    {
        public int M_Movement_ID { get; set; }
        public string ColumnName { get; set; }
        public Dictionary<string, object> RowValues { get; set; }
        public MovementLookupRequest() { RowValues = new Dictionary<string, object>(); }
    }

    /// <summary>Per-row filtered option lists for one movement line.</summary>
    public class MovementLookupData
    {
        public int M_Movement_ID { get; set; }
        public List<MovementUomItem> UomList { get; set; }
        /// <summary>Options for the SOURCE locator (M_Locator_ID).</summary>
        public List<MovementLocatorItem> LocatorList { get; set; }
        /// <summary>Options for the DESTINATION locator (M_LocatorTo_ID).</summary>
        public List<MovementLocatorItem> LocatorToList { get; set; }
        public MovementLookupData()
        {
            UomList = new List<MovementUomItem>();
            LocatorList = new List<MovementLocatorItem>();
            LocatorToList = new List<MovementLocatorItem>();
        }
    }

    /// <summary>Request for the generic FK lookup of a dynamic M_MovementLine field.</summary>
    public class MovementRefLookupRequest
    {
        public int M_Movement_ID { get; set; }
        public string ColumnName { get; set; }
        public string Query { get; set; }
        public int Id { get; set; }
        public int PageSize { get; set; }
        public int Offset { get; set; }
        public Dictionary<string, object> RowValues { get; set; }
        public MovementRefLookupRequest() { RowValues = new Dictionary<string, object>(); }
    }

    /// <summary>One id + label row of a generic FK lookup.</summary>
    public class MovementRefItem
    {
        public int Id { get; set; }
        public string Name { get; set; }
        public MovementRefItem() { Name = ""; }
    }

    /// <summary>Inbound callout request from the movement panel.</summary>
    public class MovementLineCalcRequest
    {
        public int M_Movement_ID { get; set; }
        public string TriggerColumn { get; set; }
        public int M_Product_ID { get; set; }
        public int M_AttributeSetInstance_ID { get; set; }
        /// <summary>Quantity as keyed, in the line's SELECTED unit (C_UOM_ID).</summary>
        public decimal QtyEntered { get; set; }
        /// <summary>Quantity in the product's BASE unit; fallback when QtyEntered is absent.</summary>
        public decimal MovementQty { get; set; }
        public int C_UOM_ID { get; set; }
        public int M_Locator_ID { get; set; }
        public int M_LocatorTo_ID { get; set; }
    }

    /// <summary>Columns the server callout changed, patched back into the client line.</summary>
    public class MovementCalloutResult
    {
        /// <summary>Dictionary column the callout ran for.</summary>
        public string Column { get; set; }
        /// <summary>AD_Column.Callout as configured, for the client's own resolution attempt.</summary>
        public string Callout { get; set; }
        public Dictionary<string, object> Values { get; set; }
        public Dictionary<string, string> Display { get; set; }

        public MovementCalloutResult()
        {
            Column = "";
            Callout = "";
            Values = new Dictionary<string, object>();
            Display = new Dictionary<string, string>();
        }
    }

    /// <summary>One inbound movement line to insert / update.</summary>
    public class MovementLineInput
    {
        /// <summary>Client row identity, echoed back on a per-line error so the grid can mark the row.</summary>
        public string RowKey { get; set; }
        public int M_MovementLine_ID { get; set; }
        public int Line { get; set; }
        public int M_Product_ID { get; set; }
        public int M_AttributeSetInstance_ID { get; set; }
        public int M_AttributeSetInstanceTo_ID { get; set; }
        public int M_Locator_ID { get; set; }
        public int M_LocatorTo_ID { get; set; }
        public int C_UOM_ID { get; set; }
        public decimal QtyEntered { get; set; }
        public decimal MovementQty { get; set; }
        public string Description { get; set; }
        /// <summary>Full client column bag, so every modal-set column is persisted.</summary>
        public Dictionary<string, object> Values { get; set; }
        /// <summary>
        /// Columns the user intentionally changed in the Additional Info modal, so a
        /// deliberate null can be told apart from a column that was never touched.
        /// </summary>
        public List<string> TouchedCols { get; set; }

        public MovementLineInput()
        {
            RowKey = "";
            Description = "";
            Values = new Dictionary<string, object>();
            TouchedCols = new List<string>();
        }
    }

    /// <summary>POST body for the SaveLines action.</summary>
    public class MovementSaveLinesRequest
    {
        public int M_Movement_ID { get; set; }
        public int AD_Window_ID { get; set; }
        public int Page { get; set; }
        public List<MovementLineInput> Lines { get; set; }
        public MovementSaveLinesRequest() { Lines = new List<MovementLineInput>(); }
    }

    /// <summary>POST body for the DeleteLines action.</summary>
    public class MovementDeleteLinesRequest
    {
        public int M_Movement_ID { get; set; }
        public int AD_Window_ID { get; set; }
        public int Page { get; set; }
        public List<int> LineIds { get; set; }
        public MovementDeleteLinesRequest() { LineIds = new List<int>(); }
    }

    /// <summary>One line that would not save, reported back against its client row.</summary>
    public class MovementLineSaveError
    {
        public string RowKey { get; set; }
        public int M_MovementLine_ID { get; set; }
        public int Line { get; set; }
        public string Message { get; set; }
        public MovementLineSaveError() { RowKey = ""; Message = ""; }
    }

    /// <summary>
    /// Result of a save / delete. On success it carries the refreshed page so the grid
    /// repaints from the database rather than from what it hoped it wrote.
    /// </summary>
    public class MovementSaveResult
    {
        public bool Success { get; set; }
        /// <summary>AD_Message key, or an already-translated message where the framework produced one.</summary>
        public string ErrorKey { get; set; }
        /// <summary>Untranslated detail (exception text) for the log / tooltip.</summary>
        public string ErrorDetail { get; set; }
        public List<MovementLineSaveError> LineErrors { get; set; }
        public List<MovementLineRow> Lines { get; set; }
        public int LinesTotal { get; set; }
        public int LinePage { get; set; }
        public int LinePageSize { get; set; }
        public decimal TotalQty { get; set; }

        public MovementSaveResult()
        {
            ErrorKey = "";
            ErrorDetail = "";
            LineErrors = new List<MovementLineSaveError>();
            Lines = new List<MovementLineRow>();
        }
    }

    /// <summary>A UOM option for the quantity cell.</summary>
    public class MovementUomItem
    {
        public int C_UOM_ID { get; set; }
        public string Name { get; set; }
        /// <summary>UOMSymbol where one is defined, else the name — what the row prints.</summary>
        public string Symbol { get; set; }
        public MovementUomItem() { Name = ""; Symbol = ""; }
    }

    /// <summary>A locator option, labelled with its warehouse so cross-warehouse lists stay readable.</summary>
    public class MovementLocatorItem
    {
        public int M_Locator_ID { get; set; }
        public int M_Warehouse_ID { get; set; }
        /// <summary>Locator value (the aisle/bin key).</summary>
        public string Value { get; set; }
        /// <summary>"WAREHOUSE - VALUE", ready to render.</summary>
        public string Name { get; set; }
        public MovementLocatorItem() { Value = ""; Name = ""; }
    }

    /// <summary>One product row in the catalog autocomplete.</summary>
    public class MovementCatalogItem
    {
        /// <summary>M_Product_ID. Kind is always "P" — a movement line has no charge column.</summary>
        public int RecordId { get; set; }
        public string Kind { get; set; }
        public string SearchKey { get; set; }
        public string DisplayName { get; set; }
        public string Description { get; set; }
        public bool HasAttributeSet { get; set; }
        /// <summary>M_Product.ProductType — 'I' item, 'S' service, 'E' expense.</summary>
        public string ProductType { get; set; }
        /// <summary>Product's own UOM, so a picked product can default its quantity unit.</summary>
        public int C_UOM_ID { get; set; }
        public string UomName { get; set; }

        public MovementCatalogItem()
        {
            Kind = "";
            SearchKey = "";
            DisplayName = "";
            Description = "";
            ProductType = "";
            UomName = "";
        }
    }

    /// <summary>One AD_Ref_List value offered by a List (reference 17) column.</summary>
    public class MovementRefListItem
    {
        public string Value { get; set; }
        public string Name { get; set; }
        public MovementRefListItem() { Value = ""; Name = ""; }
    }

    /// <summary>Dictionary metadata for one M_MovementLine column.</summary>
    public class MovementColumnMeta
    {
        public string ColumnName { get; set; }
        public int AD_Column_ID { get; set; }
        public string Callout { get; set; }
        public bool IsMandatory { get; set; }
        public int AD_Reference_ID { get; set; }
        public int AD_Reference_Value_ID { get; set; }
        public string Name { get; set; }
        public bool IsUpdateable { get; set; }
        public int FieldLength { get; set; }
        public string ReadOnlyLogic { get; set; }
        public string DisplayLogic { get; set; }
        public int AD_Val_Rule_ID { get; set; }
        public string ValRuleType { get; set; }
        public string ValRuleCode { get; set; }
        public bool IsDisplayed { get; set; }
        /// <summary>True when the column is a field on one of the window's own line tabs.</summary>
        public bool IsTabField { get; set; }
        public bool IsReadOnly { get; set; }
        public int SeqNo { get; set; }
        public int AD_Image_ID { get; set; }
        /// <summary>AD_Image.FontName — an icon-font class, preferred over the bitmap.</summary>
        public string IconFont { get; set; }
        public string ImageUrl { get; set; }
        public List<MovementRefListItem> RefListValues { get; set; }

        public MovementColumnMeta()
        {
            ColumnName = "";
            Callout = "";
            Name = "";
            ReadOnlyLogic = "";
            DisplayLogic = "";
            ValRuleType = "";
            ValRuleCode = "";
            IconFont = "";
            ImageUrl = "";
            RefListValues = new List<MovementRefListItem>();
        }
    }

    /// <summary>Attribute-set definition behind the attribute picker.</summary>
    public class MovementAttributeSetInfo
    {
        public int M_AttributeSet_ID { get; set; }
        public int M_Product_ID { get; set; }
        public string ProductName { get; set; }
        public bool IsLot { get; set; }
        public bool IsSerNo { get; set; }
        public bool IsGuaranteeDate { get; set; }
        public bool IsMandatory { get; set; }
        /// <summary>Whether the role may create a new instance.</summary>
        public bool IsCanCreate { get; set; }
        /// <summary>Whether the role may edit an existing instance.</summary>
        public bool IsCanEdit { get; set; }
        public string GuaranteeDateDefault { get; set; }
        public List<MovementAttributeDef> Attributes { get; set; }

        public MovementAttributeSetInfo()
        {
            ProductName = "";
            GuaranteeDateDefault = "";
            Attributes = new List<MovementAttributeDef>();
        }
    }

    /// <summary>One attribute of a product's attribute set.</summary>
    public class MovementAttributeDef
    {
        public int M_Attribute_ID { get; set; }
        public string Name { get; set; }
        /// <summary>'L' list, 'N' number, else string.</summary>
        public string ValueType { get; set; }
        public bool IsInstanceAttribute { get; set; }
        public bool IsMandatory { get; set; }
        public List<MovementAttributeValueDef> Values { get; set; }

        public MovementAttributeDef()
        {
            Name = "";
            ValueType = "";
            Values = new List<MovementAttributeValueDef>();
        }
    }

    /// <summary>One allowed value of a list attribute.</summary>
    public class MovementAttributeValueDef
    {
        public int M_AttributeValue_ID { get; set; }
        public string Code { get; set; }
        public string Name { get; set; }
        public MovementAttributeValueDef() { Code = ""; Name = ""; }
    }

    /// <summary>One typed value stored on an existing attribute-set instance.</summary>
    public class MovementAttributeInstanceValue
    {
        public int M_Attribute_ID { get; set; }
        public string ValueType { get; set; }
        public int M_AttributeValue_ID { get; set; }
        public decimal? NumberValue { get; set; }
        public string StringValue { get; set; }
        public MovementAttributeInstanceValue() { ValueType = ""; StringValue = ""; }
    }

    /// <summary>Attribute-instance create / update request from the movement panel.</summary>
    public class MovementAttributeSaveRequest
    {
        public int M_Product_ID { get; set; }
        public int M_AttributeSetInstance_ID { get; set; }
        public string Lot { get; set; }
        public string SerNo { get; set; }
        public string GuaranteeDate { get; set; }
        public List<MovementAttributeValueSelection> Values { get; set; }
    }

    /// <summary>One entered attribute value in a movement-panel attribute save request.</summary>
    public class MovementAttributeValueSelection
    {
        public int M_Attribute_ID { get; set; }
        public string ValueType { get; set; }
        public int M_AttributeValue_ID { get; set; }
        public decimal? NumberValue { get; set; }
        public string StringValue { get; set; }
        public string DisplayValue { get; set; }
    }

    /// <summary>Result of creating or updating an attribute-set instance.</summary>
    public class MovementAttributeSaveResult
    {
        public int M_AttributeSetInstance_ID { get; set; }
        public string Description { get; set; }
        public string Error { get; set; }
        public MovementAttributeSaveResult() { Description = ""; Error = ""; }
    }

    #endregion
}
