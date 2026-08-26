/******************************************************
 * Module Name    : VASLogic
 * Purpose        : Backing model for the VAS_218_CreateOppLines tab panel.
 *                  Provides parent-opportunity context and existing lines,
 *                  paged Product / Charge catalog search (50 rows / scroll),
 *                  the server-side line callout (UOM default), product attribute
 *                  (M_AttributeSetInstance) read + create, barcode scan lookup
 *                  and the VAS_OppLines insert / update / delete write actions
 *                  (always through MTable.GetPO() — no generated M-class required).
 * Chronological  : Development
 *   VAI154         Created  21-Aug-2026
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
    /// Purpose     : Data + write model behind the Create Opportunity Lines panel.
    ///               Every SELECT is filtered through MRole.AddAccessSQL on the
    ///               main physical table alias only and uses bind parameters so
    ///               the same code runs on PostgreSQL and Oracle. All inserts go
    ///               through MTable.GetPO() (never a hand-written INSERT) so the
    ///               standard framework before/after-save hooks run.
    /// Chronological development:
    ///   VAI154         Created  21-Aug-2026
    /// </summary>
    public class VAS_218_CreateOppLinesModel
    {
        private static VLogger log = VLogger.GetVLogger(typeof(VAS_218_CreateOppLinesModel).FullName);

        /// <summary>First page size for the Product / Charge catalog search.</summary>
        private const int CATALOG_PAGE_SIZE = 50;

        /// <summary>Saved opportunity lines loaded per page (server-side paging).</summary>
        private const int LINE_PAGE_SIZE = 20;

        #region Panel (read) data

        /// <summary>
        /// Builds the panel header context (everything the client-side callouts need from the
        /// parent opportunity) plus the already-saved opportunity lines. Returns an empty object
        /// (VAS_Opportunity_ID = 0) when the role has no access.
        /// </summary>
        /// <param name="ctx">session context</param>
        /// <param name="VAS_Opportunity_ID">parent opportunity</param>
        /// <param name="AD_Window_ID">source window</param>
        /// <param name="page">0-based page of saved lines</param>
        /// <returns>panel view model</returns>
        public OppPanelData GetPanelData(Ctx ctx, int VAS_Opportunity_ID, int AD_Window_ID, int page = 0)
        {
            OppPanelData data = new OppPanelData();
            if (VAS_Opportunity_ID <= 0) return data;

            LoadParentContext(ctx, VAS_Opportunity_ID, data);
            if (data.VAS_Opportunity_ID <= 0) return data;

            data.AD_Window_ID = AD_Window_ID;
            List<int> tabIds = ResolveOppLineTabs(AD_Window_ID);
            data.AD_Tab_IDs = tabIds;
            data.AD_Tab_ID = tabIds.Count > 0 ? tabIds[0] : 0;

            if (page < 0) page = 0;
            int total;
            data.Lines = LoadLines(ctx, VAS_Opportunity_ID, tabIds, page, out total);
            data.LinesTotal = total;
            data.LinePage = page;
            data.LinePageSize = LINE_PAGE_SIZE;
            decimal otherAmt;
            ComputeOtherPageTotals(ctx, VAS_Opportunity_ID, data.Lines, out otherAmt);
            data.OtherPagesPlannedAmt = otherAmt;
            LoadCatalogs(ctx, data);
            LoadColumns(ctx, data, tabIds);
            LoadLoginContext(ctx, data);
            return data;
        }

        /// <summary>
        /// Collects the login / session context values for every @$Token@ / @#Token@
        /// referenced by any column's DisplayLogic or ReadOnlyLogic.
        /// </summary>
        private void LoadLoginContext(Ctx ctx, OppPanelData data)
        {
            Regex rx = new Regex(@"@([#$][A-Za-z0-9_]+)@");
            HashSet<string> tokens = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (OppColumnMeta m in data.Columns)
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

        /// <summary>Loads VAS_OppLines column metadata once per panel load.</summary>
        private void LoadColumns(Ctx ctx, OppPanelData data, List<int> AD_Tab_IDs)
        {
            if (AD_Tab_IDs != null && AD_Tab_IDs.Count > 0)
                LoadColumnsFromTabs(ctx, data, AD_Tab_IDs);
            MergeAllColumns(ctx, data);
            LoadListValues(data);
        }

        /// <summary>Fills inline AD_Ref_List values for List (ref 17) columns.</summary>
        private void LoadListValues(OppPanelData data)
        {
            foreach (OppColumnMeta m in data.Columns)
            {
                if (m.AD_Reference_ID != 17 || m.AD_Reference_Value_ID <= 0) continue;
                DataSet ds = DB.ExecuteDataset(
                    @"SELECT Value, Name FROM AD_Ref_List
                      WHERE AD_Reference_ID = @ref AND IsActive = 'Y'
                      ORDER BY COALESCE(Name, Value)",
                    new SqlParameter[] { new SqlParameter("@ref", m.AD_Reference_Value_ID) }, null);
                if (ds == null || ds.Tables.Count == 0) continue;
                foreach (DataRow r in ds.Tables[0].Rows)
                    m.RefListValues.Add(new OppRefListItem
                    {
                        Value = Util.GetValueOfString(r["Value"]),
                        Name = Util.GetValueOfString(r["Name"])
                    });
            }
        }

        private Dictionary<int, List<int>> _olTabsByWindow;

        /// <summary>Finds every active AD_Tab bound to VAS_OppLines inside the given window.</summary>
        private List<int> ResolveOppLineTabs(int AD_Window_ID)
        {
            if (_olTabsByWindow == null) _olTabsByWindow = new Dictionary<int, List<int>>();
            List<int> cached;
            if (_olTabsByWindow.TryGetValue(AD_Window_ID, out cached)) return cached;

            List<int> tabs = new List<int>();
            if (AD_Window_ID > 0)
            {
                int tableId = Util.GetValueOfInt(DB.ExecuteScalar(
                    "SELECT AD_Table_ID FROM AD_Table WHERE TableName = 'VAS_OppLines' AND IsActive = 'Y'", null, null));
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

        /// <summary>SELECT expression for ReadOnlyLogic giving AD_Field priority over AD_Column.</summary>
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
                                  AND tt2.TableName = 'VAS_OppLines'
                                  AND f2.ReadOnlyLogic IS NOT NULL
                                  AND f2.ReadOnlyLogic <> ''), N''), ");
            sb.Append("c.ReadOnlyLogic, N'')");
            return sb.ToString();
        }

        /// <summary>
        /// Base-system column-name prefixes that are always present and never require a
        /// module-installation check.
        /// </summary>
        private static readonly HashSet<string> _systemPrefixes = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            "AD_", "C_", "M_", "A_", "G_", "K_", "R_", "I_", "B_", "T_", "S_", "W_", "U_",
            "VAS_", "VIS_", "VA_", "VB_"
        };

        /// <summary>
        /// Returns true when columnName belongs to the base system or when the optional module
        /// that owns the column is confirmed installed.
        /// </summary>
        private static bool IsColumnModuleInstalled(string columnName)
        {
            if (string.IsNullOrEmpty(columnName)) return true;
            int idx = columnName.IndexOf('_');
            if (idx <= 0) return true;
            string prefix = columnName.Substring(0, idx + 1);
            if (_systemPrefixes.Contains(prefix)) return true;
            return Env.IsModuleInstalled(prefix);
        }

        /// <summary>Reads AD_Field -> AD_Column metadata across all the window's opp-line tabs.</summary>
        private bool LoadColumnsFromTabs(Ctx ctx, OppPanelData data, List<int> AD_Tab_IDs)
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
                if (!IsColumnModuleInstalled(name)) continue;
                data.Columns.Add(MapOppColumnMeta(r, true));
            }
            return true;
        }

        /// <summary>Merges every active VAS_OppLines column not already loaded from the tab.</summary>
        private void MergeAllColumns(Ctx ctx, OppPanelData data)
        {
            HashSet<string> have = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (OppColumnMeta cm in data.Columns) have.Add(cm.ColumnName);

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
                                                  AND tt2.TableName = 'VAS_OppLines'
                                                  AND f2.DisplayLogic IS NOT NULL
                                              AND f2.DisplayLogic <> ''), N'') AS DisplayLogic
                           FROM AD_Column c
                           INNER JOIN AD_Table t ON (c.AD_Table_ID = t.AD_Table_ID)
                           LEFT JOIN AD_Val_Rule vr ON (c.AD_Val_Rule_ID = vr.AD_Val_Rule_ID
                                AND vr.IsActive = 'Y')
                           WHERE t.TableName = 'VAS_OppLines'
                             AND c.IsActive = 'Y'";

            DataSet ds = DB.ExecuteDataset(sql);
            if (ds == null || ds.Tables.Count == 0) return;
            foreach (DataRow r in ds.Tables[0].Rows)
            {
                string colName = Util.GetValueOfString(r["ColumnName"]);
                if (have.Contains(colName)) continue;
                if (!IsColumnModuleInstalled(colName)) continue;
                data.Columns.Add(MapOppColumnMeta(r, false));
            }
        }

        /// <summary>Maps a column-meta DataRow to OppColumnMeta.</summary>
        private OppColumnMeta MapOppColumnMeta(DataRow r, bool fromField)
        {
            OppColumnMeta m = new OppColumnMeta
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

        /// <summary>Loads UOM dropdown catalog using the opportunity header context.</summary>
        private void LoadCatalogs(Ctx ctx, OppPanelData data)
        {
            data.UomList = LoadUomList(ctx, data.VAS_Opportunity_ID, null);
        }

        /// <summary>Builds the UOM dropdown list, enforcing the C_UOM_ID column's AD_Val_Rule.</summary>
        private List<OppUomItem> LoadUomList(Ctx ctx, int VAS_Opportunity_ID, Dictionary<string, string> rowVars)
        {
            List<OppUomItem> list = new List<OppUomItem>();
            string uomSql = @"SELECT u.C_UOM_ID, u.Name AS UOMName
                              FROM C_UOM u
                              WHERE u.IsActive = 'Y'";
            // Apply MRole on the base query BEFORE appending the val-rule predicate.
            // AccessSqlParser inside AddAccessSQL cannot handle multiple FROM clauses,
            // which the val-rule subquery introduces (UNION ALL with separate FROM each).
            uomSql = MRole.GetDefault(ctx).AddAccessSQL(uomSql, "u", MRole.SQL_FULLYQUALIFIED, MRole.SQL_RO);
            string uomPred = GetValRulePredicate(ctx, "C_UOM_ID", "C_UOM", "u", VAS_Opportunity_ID, rowVars);
            if (uomPred.Length > 0) uomSql += " AND (" + uomPred + ")";
            uomSql += " ORDER BY u.Name";
            DataSet uds = DB.ExecuteDataset(uomSql);
            if (uds != null && uds.Tables.Count > 0)
                foreach (DataRow r in uds.Tables[0].Rows)
                    list.Add(new OppUomItem
                    {
                        C_UOM_ID = Util.GetValueOfInt(r["C_UOM_ID"]),
                        Name = Util.GetValueOfString(r["UOMName"])
                    });
            return list;
        }

        /// <summary>
        /// Re-fetches the per-row filtered UOM list for one opportunity line, honouring
        /// the column's AD_Val_Rule against the line's current values.
        /// </summary>
        public OppLookupData GetLookupData(Ctx ctx, OppLookupRequest req)
        {
            OppLookupData data = new OppLookupData();
            if (req == null || req.VAS_Opportunity_ID <= 0) return data;

            OppPanelData parent = new OppPanelData();
            LoadParentContext(ctx, req.VAS_Opportunity_ID, parent);
            if (parent.VAS_Opportunity_ID <= 0) return data;

            data.VAS_Opportunity_ID = req.VAS_Opportunity_ID;
            Dictionary<string, string> rowVars = BuildRowVars(req.RowValues);
            data.UomList = LoadUomList(ctx, req.VAS_Opportunity_ID, rowVars);
            return data;
        }

        /// <summary>
        /// Returns the UOM list valid for the given product, resolving the C_UOM_ID
        /// column's AD_Val_Rule with M_Product_ID as the only row-level context.
        /// When M_Product_ID is 0 (no product selected) the val rule returns all
        /// default UOMs; when M_Product_ID is set it returns only that product's UOMs.
        /// Called by the per-row UOM dropdown via the GetUomList controller action.
        /// </summary>
        /// <param name="ctx">session context</param>
        /// <param name="M_Product_ID">selected product (0 = no product)</param>
        /// <returns>filtered list of UOM options</returns>
        public List<OppUomItem> GetUomListForProduct(Ctx ctx, int M_Product_ID)
        {
            Dictionary<string, string> rowVars = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            rowVars["M_Product_ID"] = M_Product_ID.ToString();
            // VAS_Opportunity_ID = 0 here because opportunity-level vars (C_BPartner_ID etc.)
            // are not needed — the val rule only references @M_Product_ID@.
            return LoadUomList(ctx, 0, rowVars);
        }

        /// <summary>
        /// Generic FK lookup for a dynamic VAS_OppLines field (Table / TableDir / Search).
        /// </summary>
        public List<OppRefItem> GetRefLookup(Ctx ctx, OppRefLookupRequest req)
        {
            List<OppRefItem> items = new List<OppRefItem>();
            if (req == null || req.VAS_Opportunity_ID <= 0 || string.IsNullOrEmpty(req.ColumnName)) return items;

            OppPanelData parent = new OppPanelData();
            LoadParentContext(ctx, req.VAS_Opportunity_ID, parent);
            if (parent.VAS_Opportunity_ID <= 0) return items;

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
                string pred = GetValRulePredicate(ctx, req.ColumnName, def.TableName, alias, req.VAS_Opportunity_ID, rowVars);
                if (pred.Length > 0) sql.Append(" AND (").Append(pred).Append(")");
            }

            string secured = MRole.GetDefault(ctx).AddAccessSQL(
                sql.ToString(), alias, MRole.SQL_FULLYQUALIFIED, MRole.SQL_RO);
            if (req.Id <= 0) secured += " ORDER BY " + dispExpr + PagingSuffix(pageSize, offset);

            DataSet ds = DB.ExecuteDataset(secured, ps.ToArray(), null);
            if (ds == null || ds.Tables.Count == 0) return items;
            foreach (DataRow r in ds.Tables[0].Rows)
                items.Add(new OppRefItem { Id = Util.GetValueOfInt(r["Id"]), Name = Util.GetValueOfString(r["Name"]) });
            return items;
        }

        /// <summary>Non-standard TableDir FK columns whose lookup table/key is not the column name minus "_ID".</summary>
        private static readonly Dictionary<string, string[]> TableDirOverrides =
            new Dictionary<string, string[]>(StringComparer.OrdinalIgnoreCase)
            {
                { "AD_OrgTrx_ID", new string[] { "AD_Org", "AD_Org_ID" } }
            };

        private Dictionary<string, RefLookupDef> _refDefByColumn;

        /// <summary>Resolves a VAS_OppLines FK column to its lookup table, key and display expression.</summary>
        private RefLookupDef ResolveRefLookup(string columnName)
        {
            if (_refDefByColumn == null) _refDefByColumn = new Dictionary<string, RefLookupDef>(StringComparer.OrdinalIgnoreCase);
            RefLookupDef cached;
            if (_refDefByColumn.TryGetValue(columnName, out cached)) return cached;

            RefLookupDef def = null;
            DataSet rs = DB.ExecuteDataset(
                @"SELECT c.AD_Reference_ID, COALESCE(c.AD_Reference_Value_ID, 0) AS AD_Reference_Value_ID
                  FROM AD_Column c INNER JOIN AD_Table t ON (c.AD_Table_ID = t.AD_Table_ID)
                  WHERE t.TableName = 'VAS_OppLines' AND c.ColumnName = @c AND c.IsActive = 'Y'",
                new SqlParameter[] { new SqlParameter("@c", columnName) }, null);
            if (rs != null && rs.Tables.Count > 0 && rs.Tables[0].Rows.Count > 0)
            {
                int refId = Util.GetValueOfInt(rs.Tables[0].Rows[0]["AD_Reference_ID"]);
                int refValId = Util.GetValueOfInt(rs.Tables[0].Rows[0]["AD_Reference_Value_ID"]);

                string table = null, key = null, display = null;
                if (refId == 19 || (refId == 18 && refValId <= 0))
                {
                    string[] ov;
                    if (TableDirOverrides.TryGetValue(columnName, out ov)) { table = ov[0]; key = ov[1]; }
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

        /// <summary>Whether a table has a given column (per-instance cached).</summary>
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

        /// <summary>Loads the parent opportunity header values used as callout context.</summary>
        private void LoadParentContext(Ctx ctx, int VAS_Opportunity_ID, OppPanelData data)
        {
            try
            {
                // LEFT OUTER JOIN so opportunities without a currency still load their lines.
                // VAS_Opportunity has no Processed column — panel is always editable.
                string sql = @"SELECT
                                  o.VAS_Opportunity_ID,
                                  o.AD_Client_ID,
                                  o.AD_Org_ID,
                                  o.C_BPartner_ID,
                                  o.C_BPartner_Location_ID,
                                  o.M_PriceList_Version_ID,
                                  o.C_Currency_ID,
                                  o.C_EnquiryRdate,
                                  COALESCE(cur.StdPrecision, 2) AS StdPrecision,
                                  COALESCE(cur.CurSymbol, N'') AS CurrencySymbol,
                                  COALESCE(cur.ISO_Code, N'') AS CurrencyISOCode
                               FROM VAS_Opportunity o
                               LEFT OUTER JOIN C_Currency cur ON (o.C_Currency_ID = cur.C_Currency_ID)
                               WHERE o.VAS_Opportunity_ID = @VAS_Opportunity_ID
                                 AND o.IsActive = 'Y'";

                sql = MRole.GetDefault(ctx).AddAccessSQL(sql, "o", MRole.SQL_FULLYQUALIFIED, MRole.SQL_RO);

                DataSet ds = DB.ExecuteDataset(sql,
                    new SqlParameter[] { new SqlParameter("@VAS_Opportunity_ID", VAS_Opportunity_ID) }, null);
                if (ds == null || ds.Tables.Count == 0 || ds.Tables[0].Rows.Count == 0) return;

                DataRow r = ds.Tables[0].Rows[0];
                data.VAS_Opportunity_ID = Util.GetValueOfInt(r["VAS_Opportunity_ID"]);
                data.AD_Client_ID = Util.GetValueOfInt(r["AD_Client_ID"]);
                data.AD_Org_ID = Util.GetValueOfInt(r["AD_Org_ID"]);
                data.C_BPartner_ID = Util.GetValueOfInt(r["C_BPartner_ID"]);
                data.C_BPartner_Location_ID = Util.GetValueOfInt(r["C_BPartner_Location_ID"]);
                data.M_PriceList_Version_ID = Util.GetValueOfInt(r["M_PriceList_Version_ID"]);
                data.C_Currency_ID = Util.GetValueOfInt(r["C_Currency_ID"]);
                data.C_EnquiryRdate = Util.GetValueOfDateTime(r["C_EnquiryRdate"]);
                data.Processed = false;
                data.StdPrecision = Util.GetValueOfInt(r["StdPrecision"]);
                data.CurSymbol = Util.GetValueOfString(r["CurrencySymbol"]);
                data.CurISO = Util.GetValueOfString(r["CurrencyISOCode"]);
                data.IsEditable = !data.Processed;
            }
            catch (Exception ex)
            {
                log.Severe("VAS_218 LoadParentContext failed for VAS_Opportunity_ID=" + VAS_Opportunity_ID + ": " + ex.Message);
            }
        }

        /// <summary>Loads the opportunity lines saved against the parent opportunity.</summary>
        private List<OppLineRow> LoadLines(Ctx ctx, int VAS_Opportunity_ID, List<int> AD_Tab_IDs, int page, out int total)
        {
            List<OppLineRow> rows = new List<OppLineRow>();

            string countSql = "SELECT COUNT(*) FROM VAS_OppLines ol WHERE ol.VAS_Opportunity_ID = @VAS_Opportunity_ID AND ol.IsActive = 'Y'";
            countSql = MRole.GetDefault(ctx).AddAccessSQL(countSql, "ol", MRole.SQL_FULLYQUALIFIED, MRole.SQL_RO);
            total = Util.GetValueOfInt(DB.ExecuteScalar(countSql,
                new SqlParameter[] { new SqlParameter("@VAS_Opportunity_ID", VAS_Opportunity_ID) }, null));

            StringBuilder cols = new StringBuilder();
            foreach (string cn in GetLineProjectionColumns(AD_Tab_IDs))
                cols.Append("ol.").Append(cn).Append(", ");
            if (cols.Length == 0)
                cols.Append("ol.VAS_OppLines_ID, ol.VAS_LineNo, ol.M_Product_ID, ol.C_Charge_ID, ol.PlannedQty, ol.C_UOM_ID, ol.PlannedPrice, ol.PlannedAmt, ol.M_AttributeSetInstance_ID, ol.Description, ");

            string sql = "SELECT " + cols.ToString() +
                @"COALESCE(p.Name, N'') AS VASOLDISP_ProductName,
                  COALESCE(ch.Name, N'') AS VASOLDISP_ChargeName,
                  COALESCE(uom.Name, N'') AS VASOLDISP_UOMName,
                  COALESCE(asi.Description, N'') AS VASOLDISP_AttrName,
                  COALESCE(p.M_AttributeSet_ID, 0) AS VASOLDISP_HasAttrSet,
                  COALESCE(p.ProductType, '') AS VASOLDISP_ProductType
               FROM VAS_OppLines ol
               LEFT OUTER JOIN M_Product p ON (ol.M_Product_ID = p.M_Product_ID)
               LEFT OUTER JOIN C_Charge ch ON (ol.C_Charge_ID = ch.C_Charge_ID)
               LEFT OUTER JOIN C_UOM uom ON (ol.C_UOM_ID = uom.C_UOM_ID)
               LEFT OUTER JOIN M_AttributeSetInstance asi ON (ol.M_AttributeSetInstance_ID = asi.M_AttributeSetInstance_ID)
               WHERE ol.VAS_Opportunity_ID = @VAS_Opportunity_ID
                 AND ol.IsActive = 'Y'";

            sql = MRole.GetDefault(ctx).AddAccessSQL(sql, "ol", MRole.SQL_FULLYQUALIFIED, MRole.SQL_RO);
            if (page < 0) page = 0;
            sql += " ORDER BY ol.VAS_LineNo" + PagingSuffix(LINE_PAGE_SIZE, page * LINE_PAGE_SIZE);

            DataSet ds;
            try
            {
                ds = DB.ExecuteDataset(sql,
                    new SqlParameter[] { new SqlParameter("@VAS_Opportunity_ID", VAS_Opportunity_ID) }, null);
            }
            catch (Exception ex)
            {
                // Dynamic column projection may include a metadata column not yet in the physical table.
                // Fall back to the minimal hardcoded projection and clear the metadata cache so the
                // next call rebuilds it from AD_Column.
                log.Severe("VAS_218 LoadLines dynamic projection failed (retrying with minimal columns): " + ex.Message);
                _olColumns = null;
                string fallbackCols = "ol.VAS_OppLines_ID, ol.VAS_LineNo, ol.M_Product_ID, ol.C_Charge_ID, ol.PlannedQty, ol.C_UOM_ID, ol.PlannedPrice, ol.PlannedAmt, ol.M_AttributeSetInstance_ID, ol.Description, ";
                string fallbackSql = "SELECT " + fallbackCols +
                    @"COALESCE(p.Name, N'') AS VASOLDISP_ProductName,
                      COALESCE(ch.Name, N'') AS VASOLDISP_ChargeName,
                      COALESCE(uom.Name, N'') AS VASOLDISP_UOMName,
                      COALESCE(asi.Description, N'') AS VASOLDISP_AttrName,
                      COALESCE(p.M_AttributeSet_ID, 0) AS VASOLDISP_HasAttrSet,
                      COALESCE(p.ProductType, '') AS VASOLDISP_ProductType
                   FROM VAS_OppLines ol
                   LEFT OUTER JOIN M_Product p ON (ol.M_Product_ID = p.M_Product_ID)
                   LEFT OUTER JOIN C_Charge ch ON (ol.C_Charge_ID = ch.C_Charge_ID)
                   LEFT OUTER JOIN C_UOM uom ON (ol.C_UOM_ID = uom.C_UOM_ID)
                   LEFT OUTER JOIN M_AttributeSetInstance asi ON (ol.M_AttributeSetInstance_ID = asi.M_AttributeSetInstance_ID)
                   WHERE ol.VAS_Opportunity_ID = @VAS_Opportunity_ID
                     AND ol.IsActive = 'Y'";
                fallbackSql = MRole.GetDefault(ctx).AddAccessSQL(fallbackSql, "ol", MRole.SQL_FULLYQUALIFIED, MRole.SQL_RO);
                fallbackSql += " ORDER BY ol.VAS_LineNo" + PagingSuffix(LINE_PAGE_SIZE, page * LINE_PAGE_SIZE);
                ds = DB.ExecuteDataset(fallbackSql,
                    new SqlParameter[] { new SqlParameter("@VAS_Opportunity_ID", VAS_Opportunity_ID) }, null);
            }

            if (ds == null || ds.Tables.Count == 0) return rows;

            DataTable dt = ds.Tables[0];
            foreach (DataRow r in dt.Rows)
            {
                OppLineRow row = new OppLineRow();
                foreach (DataColumn dc in dt.Columns)
                {
                    if (dc.ColumnName.StartsWith("VASOLDISP_")) continue;
                    row.Values[dc.ColumnName] = (r[dc] == DBNull.Value) ? null : r[dc];
                }
                row.VAS_OppLines_ID = Util.GetValueOfInt(r["VAS_OppLines_ID"]);
                row.Line = Util.GetValueOfInt(r["VAS_LineNo"]);
                row.M_Product_ID = Util.GetValueOfInt(r["M_Product_ID"]);
                row.ProductName = Util.GetValueOfString(r["VASOLDISP_ProductName"]);
                row.C_Charge_ID = Util.GetValueOfInt(r["C_Charge_ID"]);
                row.ChargeName = Util.GetValueOfString(r["VASOLDISP_ChargeName"]);
                row.Description = Util.GetValueOfString(r["Description"]);
                row.PlannedQty = Util.GetValueOfDecimal(r["PlannedQty"]);
                row.C_UOM_ID = Util.GetValueOfInt(r["C_UOM_ID"]);
                row.UOMName = Util.GetValueOfString(r["VASOLDISP_UOMName"]);
                row.PlannedPrice = Util.GetValueOfDecimal(r["PlannedPrice"]);
                row.PlannedAmt = Util.GetValueOfDecimal(r["PlannedAmt"]);
                row.M_AttributeSetInstance_ID = Util.GetValueOfInt(r["M_AttributeSetInstance_ID"]);
                row.AttrName = Util.GetValueOfString(r["VASOLDISP_AttrName"]);
                row.HasAttributeSet = Util.GetValueOfInt(r["VASOLDISP_HasAttrSet"]) > 0;
                row.ProductType = Util.GetValueOfString(r["VASOLDISP_ProductType"]);
                rows.Add(row);
            }
            return rows;
        }

        /// <summary>
        /// Sums PlannedAmt across the whole opportunity for all saved lines.
        /// </summary>
        private void LoadGrandTotals(Ctx ctx, int VAS_Opportunity_ID, out decimal plannedAmt)
        {
            plannedAmt = 0;
            string sql = "SELECT COALESCE(SUM(ol.PlannedAmt), 0) AS PlannedAmt"
                + " FROM VAS_OppLines ol WHERE ol.VAS_Opportunity_ID = @VAS_Opportunity_ID AND ol.IsActive = 'Y'";
            sql = MRole.GetDefault(ctx).AddAccessSQL(sql, "ol", MRole.SQL_FULLYQUALIFIED, MRole.SQL_RO);
            DataSet ds = DB.ExecuteDataset(sql,
                new SqlParameter[] { new SqlParameter("@VAS_Opportunity_ID", VAS_Opportunity_ID) }, null);
            if (ds != null && ds.Tables.Count > 0 && ds.Tables[0].Rows.Count > 0)
                plannedAmt = Util.GetValueOfDecimal(ds.Tables[0].Rows[0]["PlannedAmt"]);
        }

        /// <summary>Computes the PlannedAmt sum of every saved line NOT on the current page.</summary>
        private void ComputeOtherPageTotals(Ctx ctx, int VAS_Opportunity_ID, List<OppLineRow> pageRows,
            out decimal otherAmt)
        {
            decimal gAmt;
            LoadGrandTotals(ctx, VAS_Opportunity_ID, out gAmt);
            decimal pAmt = 0;
            if (pageRows != null)
                foreach (OppLineRow row in pageRows)
                    pAmt += row.PlannedAmt;
            otherAmt = gAmt - pAmt;
        }

        private List<string> _olColumns;

        /// <summary>Returns (and caches) the active VAS_OppLines column names from the dictionary.</summary>
        private List<string> GetOppLineColumns()
        {
            if (_olColumns != null) return _olColumns;
            _olColumns = new List<string>();
            DataSet ds = DB.ExecuteDataset(
                @"SELECT c.ColumnName
                  FROM AD_Column c
                  INNER JOIN AD_Table t ON (c.AD_Table_ID = t.AD_Table_ID)
                  WHERE t.TableName = 'VAS_OppLines' AND c.IsActive = 'Y' AND c.ColumnSQL IS NULL");
            if (ds != null && ds.Tables.Count > 0)
                foreach (DataRow r in ds.Tables[0].Rows)
                    _olColumns.Add(Util.GetValueOfString(r["ColumnName"]));
            return _olColumns;
        }

        /// <summary>Builds the VAS_OppLines column projection for LoadLines.</summary>
        private List<string> GetLineProjectionColumns(List<int> AD_Tab_IDs)
        {
            return GetOppLineColumns();
        }

        #endregion

        #region Product / Charge catalog search

        /// <summary>Paged Product / Charge catalog search for the line picker.</summary>
        public List<OppCatalogItem> SearchProductsCharges(Ctx ctx, int VAS_Opportunity_ID,
            string query, int pageSize, int offset, Dictionary<string, object> rowValues = null)
        {
            List<OppCatalogItem> items = new List<OppCatalogItem>();
            if (VAS_Opportunity_ID <= 0) return items;

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
            string prodPred = GetValRulePredicate(ctx, "M_Product_ID", "M_Product", "p", VAS_Opportunity_ID, rowVars);
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
            string chargePred = GetValRulePredicate(ctx, "C_Charge_ID", "C_Charge", "ch", VAS_Opportunity_ID, rowVars);
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
                log.Severe("VAS_218 SearchProductsCharges SQL failed. Term: " + like);
                return items;
            }

            foreach (DataRow r in ds.Tables[0].Rows)
            {
                OppCatalogItem it = new OppCatalogItem();
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
        public OppCatalogItem ScanLookup(Ctx ctx, int VAS_Opportunity_ID, string code)
        {
            OppCatalogItem none = new OppCatalogItem();
            if (VAS_Opportunity_ID <= 0 || string.IsNullOrEmpty(code)) return none;
            string key = code.Trim();

            string prodSql = @"SELECT p.M_Product_ID AS RecordId, 'P' AS Kind, p.Value AS SearchKey,
                                      p.Name AS DisplayName, COALESCE(p.Description, N'') AS Description,
                                      p.M_AttributeSet_ID AS AttributeSetId,
                                      COALESCE(p.ProductType, '') AS ProductType
                               FROM M_Product p
                               WHERE p.IsActive = 'Y'
                                 AND p.AD_Client_ID = " + ctx.GetAD_Client_ID() + @"
                                 AND (UPPER(p.UPC) = UPPER(@code) OR UPPER(p.Value) = UPPER(@code))";
            string scanProdPred = GetValRulePredicate(ctx, "M_Product_ID", "M_Product", "p", VAS_Opportunity_ID);
            if (scanProdPred.Length > 0) prodSql += " AND (" + scanProdPred + ")";
            prodSql = MRole.GetDefault(ctx).AddAccessSQL(prodSql, "p", MRole.SQL_FULLYQUALIFIED, MRole.SQL_RO);

            DataSet ds = DB.ExecuteDataset(prodSql, new SqlParameter[] { new SqlParameter("@code", key) }, null);
            if (ds != null && ds.Tables.Count > 0 && ds.Tables[0].Rows.Count > 0)
            {
                DataRow r = ds.Tables[0].Rows[0];
                OppCatalogItem it = new OppCatalogItem();
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
            string scanChargePred = GetValRulePredicate(ctx, "C_Charge_ID", "C_Charge", "ch", VAS_Opportunity_ID);
            if (scanChargePred.Length > 0) chargeSql += " AND (" + scanChargePred + ")";
            chargeSql = MRole.GetDefault(ctx).AddAccessSQL(chargeSql, "ch", MRole.SQL_FULLYQUALIFIED, MRole.SQL_RO);
            ds = DB.ExecuteDataset(chargeSql, new SqlParameter[] { new SqlParameter("@code", key) }, null);
            if (ds != null && ds.Tables.Count > 0 && ds.Tables[0].Rows.Count > 0)
            {
                DataRow r = ds.Tables[0].Rows[0];
                OppCatalogItem it = new OppCatalogItem();
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

        #endregion

        #region AD_Val_Rule enforcement

        private Dictionary<string, string> _valRuleByColumn;
        private Dictionary<string, string> _oppVars;

        /// <summary>Returns the SQL validation code linked to a VAS_OppLines lookup column.</summary>
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
                  WHERE t.TableName = 'VAS_OppLines'
                    AND c.ColumnName = @c
                    AND c.IsActive = 'Y'
                    AND vr.IsActive = 'Y'
                    AND vr.Type = 'S'",
                new SqlParameter[] { new SqlParameter("@c", columnName) }, null);
            string code = Util.GetValueOfString(o);
            _valRuleByColumn[columnName] = code;
            return code;
        }

        private string GetValRulePredicate(Ctx ctx, string columnName, string tableName, string alias, int VAS_Opportunity_ID)
        {
            return GetValRulePredicate(ctx, columnName, tableName, alias, VAS_Opportunity_ID, null);
        }

        private string GetValRulePredicate(Ctx ctx, string columnName, string tableName, string alias,
            int VAS_Opportunity_ID, Dictionary<string, string> rowVars)
        {
            string code = GetColumnValRule(columnName);
            if (string.IsNullOrEmpty(code)) return "";

            string frag = Regex.Replace(code, @"\b" + Regex.Escape(tableName) + @"\.", alias + ".", RegexOptions.IgnoreCase);

            Dictionary<string, string> oVars = GetOppVars(ctx, VAS_Opportunity_ID);
            frag = Regex.Replace(frag, @"@(#?[A-Za-z0-9_]+)@", delegate (Match m)
            {
                string token = m.Groups[1].Value;
                string k = token.TrimStart('#');
                string val;
                if (rowVars != null && rowVars.TryGetValue(k, out val)) return val;
                if (oVars.TryGetValue(k, out val)) return val;
                string ctxVal = GetCtxLiteral(ctx, token);
                if (ctxVal != null) return ctxVal;
                // For integer FK tokens (ending in _ID) with no context value, substitute 0
                // so the val rule executes with a safe default (e.g. @M_Product_ID@ → 0
                // returns only default UOMs, which is correct when no product is selected yet).
                if (k.EndsWith("_ID", StringComparison.OrdinalIgnoreCase)) return "0";
                return m.Value;
            });

            if (frag.IndexOf('@') >= 0)
            {
                log.Warning("VAS_218 val rule skipped for " + columnName + " (unresolved context): " + code);
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

        /// <summary>Parent-opportunity context values (as SQL literals) for val-rule substitution.</summary>
        private Dictionary<string, string> GetOppVars(Ctx ctx, int VAS_Opportunity_ID)
        {
            if (_oppVars != null) return _oppVars;
            _oppVars = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            DataSet ds = DB.ExecuteDataset(
                @"SELECT AD_Client_ID, AD_Org_ID, C_BPartner_ID, C_BPartner_Location_ID,
                         M_PriceList_Version_ID, C_Currency_ID
                  FROM VAS_Opportunity WHERE VAS_Opportunity_ID = @id",
                new SqlParameter[] { new SqlParameter("@id", VAS_Opportunity_ID) }, null);
            if (ds != null && ds.Tables.Count > 0 && ds.Tables[0].Rows.Count > 0)
            {
                DataRow r = ds.Tables[0].Rows[0];
                _oppVars["AD_Client_ID"] = Util.GetValueOfInt(r["AD_Client_ID"]).ToString();
                _oppVars["AD_Org_ID"] = Util.GetValueOfInt(r["AD_Org_ID"]).ToString();
                _oppVars["C_BPartner_ID"] = Util.GetValueOfInt(r["C_BPartner_ID"]).ToString();
                _oppVars["C_BPartner_Location_ID"] = Util.GetValueOfInt(r["C_BPartner_Location_ID"]).ToString();
                _oppVars["M_PriceList_Version_ID"] = Util.GetValueOfInt(r["M_PriceList_Version_ID"]).ToString();
                _oppVars["C_Currency_ID"] = Util.GetValueOfInt(r["C_Currency_ID"]).ToString();
                _oppVars["VAS_Opportunity_ID"] = VAS_Opportunity_ID.ToString();
            }
            return _oppVars;
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

        #region Line callout (server-side UOM default resolution)

        /// <summary>
        /// Reads the changed column's AD_Column.Callout and returns the default UOM +
        /// display name for the selected product or charge. No pricing or tax recalculation
        /// is performed — VAS_OppLines carries PlannedQty / PlannedPrice without framework
        /// pricing dependencies.
        /// </summary>
        public OppCalloutResult RunColumnCallout(Ctx ctx, OppLineCalcRequest req)
        {
            OppCalloutResult res = new OppCalloutResult();
            if (req == null || req.VAS_Opportunity_ID <= 0) return res;

            string column = MapTriggerToColumn(req.TriggerColumn);
            res.Column = column;
            res.Callout = ReadColumnCallout(ctx, "VAS_OppLines", column);

            int uomId = req.C_UOM_ID;
            // On product change, attempt to use the product's Sales UOM first, then the primary UOM.
            if (req.M_Product_ID > 0)
            {
                if (uomId <= 0)
                {
                    uomId = GetProductSalesUomId(ctx, req.M_Product_ID);
                    if (uomId <= 0)
                    {
                        object o = DB.ExecuteScalar(
                            "SELECT p.C_UOM_ID FROM M_Product p WHERE p.M_Product_ID = @pid AND p.IsActive = 'Y'",
                            new SqlParameter[] { new SqlParameter("@pid", req.M_Product_ID) }, null);
                        uomId = Util.GetValueOfInt(o);
                    }
                }
                res.Values["M_Product_ID"] = req.M_Product_ID;
                res.Values["C_Charge_ID"] = 0;
            }
            else if (req.C_Charge_ID > 0)
            {
                if (uomId <= 0)
                    uomId = GetDefaultUomId(ctx);
                res.Values["M_Product_ID"] = 0;
                res.Values["C_Charge_ID"] = req.C_Charge_ID;
                res.Values["M_AttributeSetInstance_ID"] = 0;
            }

            if (uomId > 0)
                res.Values["C_UOM_ID"] = uomId;

            res.Display["uomName"] = GetUomLabel(ctx, uomId);
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
                case "PlannedQty":
                case "PlannedPrice":
                case "C_UOM_ID":
                case "M_AttributeSetInstance_ID":
                    return trigger;
                default: return trigger ?? "";
            }
        }

        private string GetUomLabel(Ctx ctx, int C_UOM_ID)
        {
            if (C_UOM_ID <= 0) return "";
            object o = DB.ExecuteScalar(
                "SELECT Name FROM C_UOM WHERE C_UOM_ID=@id",
                new SqlParameter[] { new SqlParameter("@id", C_UOM_ID) }, null);
            return Util.GetValueOfString(o);
        }

        private int GetDefaultUomId(Ctx ctx)
        {
            return MUOM.GetDefault_UOM_ID(ctx);
        }

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

        /// <summary>
        /// Returns the product's attribute-set definition (attributes + allowed values)
        /// for the attribute picker dialog.
        /// </summary>
        /// <summary>
        /// Returns true when the product has a non-zero M_AttributeSet_ID, i.e. the
        /// attribute picker should be enabled for this product on the opportunity line.
        /// Called by the HasAttributeSet controller action.
        /// </summary>
        /// <param name="ctx">session context</param>
        /// <param name="M_Product_ID">product to check</param>
        /// <returns>true when an attribute set is configured</returns>
        public bool ProductHasAttributeSet(Ctx ctx, int M_Product_ID)
        {
            if (M_Product_ID <= 0) return false;
            MProduct product = MProduct.Get(ctx, M_Product_ID);
            return product != null && product.GetM_AttributeSet_ID() > 0;
        }

        /// <param name="ctx">session context</param>
        /// <param name="M_Product_ID">product whose attribute set is read</param>
        /// <returns>attribute-set definition; empty object when no set is configured</returns>
        public OppAttributeSetInfo GetProductAttributes(Ctx ctx, int M_Product_ID)
        {
            OppAttributeSetInfo info = new OppAttributeSetInfo();
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

            Dictionary<int, OppAttributeDef> map = new Dictionary<int, OppAttributeDef>();
            foreach (DataRow r in ds.Tables[0].Rows)
            {
                int attrId = Util.GetValueOfInt(r["M_Attribute_ID"]);
                OppAttributeDef def;
                if (!map.TryGetValue(attrId, out def))
                {
                    def = new OppAttributeDef();
                    def.M_Attribute_ID = attrId;
                    def.Name = Util.GetValueOfString(r["AttributeName"]);
                    def.ValueType = Util.GetValueOfString(r["AttributeValueType"]);
                    def.IsInstanceAttribute = Util.GetValueOfString(r["IsInstanceAttribute"]) == "Y";
                    def.IsMandatory = Util.GetValueOfString(r["IsMandatory"]) == "Y";
                    def.Values = new List<OppAttributeValueDef>();
                    map[attrId] = def;
                    info.Attributes.Add(def);
                }
                int valId = Util.GetValueOfInt(r["M_AttributeValue_ID"]);
                if (valId > 0)
                {
                    OppAttributeValueDef v = new OppAttributeValueDef();
                    v.M_AttributeValue_ID = valId;
                    v.Code = Util.GetValueOfString(r["ValueCode"]);
                    v.Name = Util.GetValueOfString(r["ValueName"]);
                    def.Values.Add(v);
                }
            }
            return info;
        }

        /// <summary>
        /// Returns the per-attribute values stored on an existing attribute-set instance.
        /// </summary>
        /// <param name="ctx">session context</param>
        /// <param name="M_AttributeSetInstance_ID">instance whose values are read</param>
        /// <returns>list of typed attribute values</returns>
        public List<OppAttributeInstanceValue> GetInstanceValues(Ctx ctx, int M_AttributeSetInstance_ID)
        {
            List<OppAttributeInstanceValue> list = new List<OppAttributeInstanceValue>();
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
                OppAttributeInstanceValue v = new OppAttributeInstanceValue();
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
        /// Creates or updates an M_AttributeSetInstance from the picker selection by delegating
        /// to the framework's PAttributesModel.SaveAttribute.
        /// </summary>
        /// <param name="ctx">session context</param>
        /// <param name="req">attribute instance create / update request</param>
        /// <returns>new instance id + description, or Error text on failure</returns>
        public OppAttributeSaveResult SaveAttribute(Ctx ctx, OppAttributeSaveRequest req)
        {
            OppAttributeSaveResult res = new OppAttributeSaveResult();
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
        /// Maps the picker selection onto the positional List expected by
        /// PAttributesModel.SaveAttribute.
        /// </summary>
        private List<KeyNamePair> BuildAttributeValueList(MAttributeSet aset, List<OppAttributeValueSelection> selections)
        {
            List<KeyNamePair> values = new List<KeyNamePair>();
            if (aset == null) return values;

            Dictionary<int, OppAttributeValueSelection> byAttr = new Dictionary<int, OppAttributeValueSelection>();
            if (selections != null)
                foreach (OppAttributeValueSelection sel in selections)
                    if (sel != null && sel.M_Attribute_ID > 0)
                        byAttr[sel.M_Attribute_ID] = sel;

            MAttribute[] attributes = aset.GetMAttributes(true);
            foreach (MAttribute attr in attributes)
            {
                OppAttributeValueSelection sel;
                byAttr.TryGetValue(attr.Get_ID(), out sel);

                if (MAttribute.ATTRIBUTEVALUETYPE_List.Equals(attr.GetAttributeValueType()))
                {
                    int valId = sel != null ? sel.M_AttributeValue_ID : 0;
                    string label = sel != null ? sel.DisplayValue : "";
                    values.Add(new KeyNamePair(valId, label));
                }
                else if (MAttribute.ATTRIBUTEVALUETYPE_Number.Equals(attr.GetAttributeValueType()))
                {
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

        #region Write actions (insert / update / delete VAS_OppLines)

        /// <summary>Core columns managed by SaveLines directly; excluded from ApplyExtraColumns.</summary>
        private static readonly HashSet<string> CORE_OR_SYSTEM_COLUMNS = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            "VAS_OppLines_ID", "VAS_Opportunity_ID", "AD_Client_ID", "AD_Org_ID",
            "Created", "CreatedBy", "Updated", "UpdatedBy", "IsActive",
            "M_Product_ID", "C_Charge_ID", "M_AttributeSetInstance_ID",
            "PlannedQty", "C_UOM_ID", "PlannedPrice", "PlannedAmt",
            "VAS_LineNo", "Description"
        };

        private HashSet<string> _updateableColumns;
        private HashSet<string> _yesNoColumns;
        private HashSet<string> _referenceColumns;

        /// <summary>Persists every non-core, updateable VAS_OppLines column through PO.Set_Value.</summary>
        private void ApplyExtraColumns(PO line, Dictionary<string, object> values, HashSet<string> touched)
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
                catch (Exception ex) { log.Warning("VAS_218 SaveLines: skip column " + col + " - " + ex.Message); }
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
                  WHERE t.TableName = 'VAS_OppLines'
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

        /// <summary>
        /// Inserts or updates the supplied opportunity lines through MTable.GetPO().
        /// All lines share a single transaction so a failure rolls the whole batch back.
        /// PlannedAmt is computed as PlannedQty * PlannedPrice server-side.
        /// </summary>
        public OppSaveResult SaveLines(Ctx ctx, int VAS_Opportunity_ID, int AD_Window_ID, List<OppLineInput> rows, int page = 0)
        {
            OppSaveResult res = new OppSaveResult();
            if (VAS_Opportunity_ID <= 0 || rows == null || rows.Count == 0)
            {
                res.ErrorKey = "VAS_218_NothingToSave";
                return res;
            }

            OppPanelData ctxData = new OppPanelData();
            LoadParentContext(ctx, VAS_Opportunity_ID, ctxData);
            if (ctxData.VAS_Opportunity_ID <= 0)
            {
                res.ErrorKey = "VAS_218_NoAccess";
                return res;
            }
            if (!ctxData.IsEditable)
            {
                res.ErrorKey = "VAS_218_OppNotEditable";
                return res;
            }

            Trx trx = Trx.GetTrx(Trx.CreateTrxName("VAS218Save_" + VAS_Opportunity_ID));
            try
            {
                foreach (OppLineInput input in rows)
                {
                    if (input.M_Product_ID <= 0 && input.C_Charge_ID <= 0)
                        continue;

                    PO line = MTable.GetPO(ctx, "VAS_OppLines", input.VAS_OppLines_ID, trx);

                    if (input.VAS_OppLines_ID <= 0)
                        line.Set_Value("VAS_Opportunity_ID", VAS_Opportunity_ID);

                    if (input.M_Product_ID > 0)
                    {
                        line.Set_Value("M_Product_ID", input.M_Product_ID);
                        line.Set_Value("C_Charge_ID", null);
                        if (input.M_AttributeSetInstance_ID > 0)
                            line.Set_Value("M_AttributeSetInstance_ID", input.M_AttributeSetInstance_ID);
                    }
                    else
                    {
                        line.Set_Value("C_Charge_ID", input.C_Charge_ID);
                        line.Set_Value("M_Product_ID", null);
                    }

                    decimal qty = input.PlannedQty > 0 ? input.PlannedQty : 1;
                    line.Set_Value("PlannedQty", qty);

                    if (input.C_UOM_ID > 0)
                        line.Set_Value("C_UOM_ID", input.C_UOM_ID);

                    // Charge lines: ensure UOM is never 0.
                    if (input.C_Charge_ID > 0 && Util.GetValueOfInt(line.Get_Value("C_UOM_ID")) <= 0)
                        line.Set_Value("C_UOM_ID", GetDefaultUomId(ctx));

                    line.Set_Value("PlannedPrice", input.PlannedPrice);
                    // PlannedAmt = PlannedQty * PlannedPrice (no tax).
                    line.Set_Value("PlannedAmt", decimal.Multiply(qty, input.PlannedPrice));

                    if (input.Line > 0)
                        line.Set_Value("Line", input.Line);

                    line.Set_Value("Description", input.Description ?? "");

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
                        log.Warning("VAS_218 SaveLines: line save failed (Line " + input.Line + ") - " + err);
                        res.LineErrors.Add(new OppLineSaveError
                        {
                            RowKey = input.RowKey,
                            VAS_OppLines_ID = input.VAS_OppLines_ID,
                            Line = input.Line,
                            Message = err
                        });
                    }
                }

                if (res.LineErrors.Count > 0)
                {
                    trx.Rollback();
                    res.ErrorKey = "VAS_218_SaveFailed";
                    res.ErrorDetail = res.LineErrors[0].Message;
                    return res;
                }

                trx.Commit();
            }
            catch (Exception ex)
            {
                trx.Rollback();
                log.Log(Level.SEVERE, "VAS_218 SaveLines failed", ex);
                res.ErrorKey = "VAS_218_SaveFailed";
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
            res.Lines = LoadLines(ctx, VAS_Opportunity_ID, ResolveOppLineTabs(AD_Window_ID), page, out total);
            res.LinesTotal = total;
            res.LinePage = page;
            res.LinePageSize = LINE_PAGE_SIZE;
            decimal soAmt;
            ComputeOtherPageTotals(ctx, VAS_Opportunity_ID, res.Lines, out soAmt);
            res.OtherPagesPlannedAmt = soAmt;
            return res;
        }

        /// <summary>Soft-deletes the supplied saved opportunity lines through MTable.GetPO().</summary>
        public OppSaveResult DeleteLines(Ctx ctx, int VAS_Opportunity_ID, int AD_Window_ID, List<int> lineIds, int page = 0)
        {
            OppSaveResult res = new OppSaveResult();
            if (VAS_Opportunity_ID <= 0 || lineIds == null || lineIds.Count == 0)
            {
                res.ErrorKey = "VAS_218_NothingToSave";
                return res;
            }

            OppPanelData ctxData = new OppPanelData();
            LoadParentContext(ctx, VAS_Opportunity_ID, ctxData);
            if (ctxData.VAS_Opportunity_ID <= 0) { res.ErrorKey = "VAS_218_NoAccess"; return res; }
            if (!ctxData.IsEditable) { res.ErrorKey = "VAS_218_OppNotEditable"; return res; }

            Trx trx = Trx.GetTrx(Trx.CreateTrxName("VAS218Delete_" + VAS_Opportunity_ID));
            try
            {
                foreach (int id in lineIds)
                {
                    if (id <= 0) continue;
                    PO line = MTable.GetPO(ctx, "VAS_OppLines", id, trx);
                    if (line.Get_ID() != id) continue;
                    if (Util.GetValueOfInt(line.Get_Value("VAS_Opportunity_ID")) != VAS_Opportunity_ID) continue;
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
                                err = Msg.GetMsg(ctx, "VAS_218_DeleteFailed");
                            else
                                err = val;
                        }
                        log.Warning("VAS_218 DeleteLines: delete failed for line " + id);
                        res.ErrorKey = err;
                        return res;
                    }
                }
                trx.Commit();
            }
            catch (Exception ex)
            {
                trx.Rollback();
                log.Log(Level.SEVERE, "VAS_218 DeleteLines failed", ex);
                res.ErrorKey = "VAS_218_DeleteFailed";
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
            res.Lines = LoadLines(ctx, VAS_Opportunity_ID, ResolveOppLineTabs(AD_Window_ID), page, out total);
            int pageCount = System.Math.Max(1, (int)System.Math.Ceiling(total / (double)LINE_PAGE_SIZE));
            if (page > pageCount - 1)
            {
                page = pageCount - 1;
                res.Lines = LoadLines(ctx, VAS_Opportunity_ID, ResolveOppLineTabs(AD_Window_ID), page, out total);
            }
            res.LinesTotal = total;
            res.LinePage = page;
            res.LinePageSize = LINE_PAGE_SIZE;
            decimal doAmt;
            ComputeOtherPageTotals(ctx, VAS_Opportunity_ID, res.Lines, out doAmt);
            res.OtherPagesPlannedAmt = doAmt;
            return res;
        }

        #endregion
    }

    #region Data contracts — VAS_218 specific

    /// <summary>Parent opportunity context + saved lines returned to the panel on load.</summary>
    public class OppPanelData
    {
        public int VAS_Opportunity_ID { get; set; }
        public int AD_Client_ID { get; set; }
        public int AD_Org_ID { get; set; }
        public int C_BPartner_ID { get; set; }
        public int C_BPartner_Location_ID { get; set; }
        public int M_PriceList_Version_ID { get; set; }
        public int C_Currency_ID { get; set; }
        public DateTime? C_EnquiryRdate { get; set; }
        public bool Processed { get; set; }
        public bool IsEditable { get; set; }
        public int LinesTotal { get; set; }
        public int LinePage { get; set; }
        public int LinePageSize { get; set; }
        public decimal OtherPagesPlannedAmt { get; set; }
        public int StdPrecision { get; set; }
        public string CurSymbol { get; set; }
        public string CurISO { get; set; }
        public int AD_Window_ID { get; set; }
        public int AD_Tab_ID { get; set; }
        public List<int> AD_Tab_IDs { get; set; }
        public List<OppLineRow> Lines { get; set; }
        public List<OppUomItem> UomList { get; set; }
        public List<OppColumnMeta> Columns { get; set; }
        public Dictionary<string, string> LoginContext { get; set; }

        public OppPanelData()
        {
            Lines = new List<OppLineRow>();
            UomList = new List<OppUomItem>();
            Columns = new List<OppColumnMeta>();
            AD_Tab_IDs = new List<int>();
            LoginContext = new Dictionary<string, string>();
            StdPrecision = 2;
        }
    }

    /// <summary>Request for the per-row UOM lookup re-filter.</summary>
    public class OppLookupRequest
    {
        public int VAS_Opportunity_ID { get; set; }
        public string ColumnName { get; set; }
        public Dictionary<string, object> RowValues { get; set; }
        public OppLookupRequest() { RowValues = new Dictionary<string, object>(); }
    }

    /// <summary>Per-row filtered UOM list for one opportunity line.</summary>
    public class OppLookupData
    {
        public int VAS_Opportunity_ID { get; set; }
        public List<OppUomItem> UomList { get; set; }
        public OppLookupData() { UomList = new List<OppUomItem>(); }
    }

    /// <summary>Request for the generic FK lookup of a dynamic VAS_OppLines field.</summary>
    public class OppRefLookupRequest
    {
        public int VAS_Opportunity_ID { get; set; }
        public string ColumnName { get; set; }
        public string Query { get; set; }
        public int Id { get; set; }
        public int PageSize { get; set; }
        public int Offset { get; set; }
        public Dictionary<string, object> RowValues { get; set; }
        public OppRefLookupRequest() { RowValues = new Dictionary<string, object>(); }
    }

    /// <summary>Inbound callout request from the opportunity lines panel.</summary>
    public class OppLineCalcRequest
    {
        public int VAS_Opportunity_ID { get; set; }
        public string TriggerColumn { get; set; }
        public int M_Product_ID { get; set; }
        public int C_Charge_ID { get; set; }
        public int M_AttributeSetInstance_ID { get; set; }
        public decimal PlannedQty { get; set; }
        public int C_UOM_ID { get; set; }
        public decimal PlannedPrice { get; set; }
        public bool PriceOverride { get; set; }
    }

    /// <summary>A saved opportunity line shown in the panel grid.</summary>
    public class OppLineRow
    {
        public int VAS_OppLines_ID { get; set; }
        public int Line { get; set; }
        public int M_Product_ID { get; set; }
        public string ProductName { get; set; }
        public int C_Charge_ID { get; set; }
        public string ChargeName { get; set; }
        public string Description { get; set; }
        public decimal PlannedQty { get; set; }
        public int C_UOM_ID { get; set; }
        public string UOMName { get; set; }
        public decimal PlannedPrice { get; set; }
        public decimal PlannedAmt { get; set; }
        public int M_AttributeSetInstance_ID { get; set; }
        public string AttrName { get; set; }
        public bool HasAttributeSet { get; set; }
        public string ProductType { get; set; }
        public Dictionary<string, object> Values { get; set; }
        public OppLineRow() { Values = new Dictionary<string, object>(); }
    }

    /// <summary>Result of running a VAS_OppLines column's callout server-side.</summary>
    public class OppCalloutResult
    {
        public string Column { get; set; }
        public string Callout { get; set; }
        public Dictionary<string, object> Values { get; set; }
        public Dictionary<string, string> Display { get; set; }
        public OppCalloutResult()
        {
            Values = new Dictionary<string, object>();
            Display = new Dictionary<string, string>();
        }
    }

    /// <summary>
    /// Metadata for one VAS_OppLines column: reference type, callout, val-rule, display/
    /// read-only logic and optional inline list values.
    /// </summary>
    public class OppColumnMeta
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
        public bool IsTabField { get; set; }
        public bool IsReadOnly { get; set; }
        public int SeqNo { get; set; }
        public string DisplayLogic { get; set; }
        public int AD_Reference_Value_ID { get; set; }
        public string Name { get; set; }
        public int AD_Image_ID { get; set; }
        public string IconFont { get; set; }
        public string ImageUrl { get; set; }
        public List<OppRefListItem> RefListValues { get; set; }
        public OppColumnMeta() { RefListValues = new List<OppRefListItem>(); }
    }

    /// <summary>One value of a List (AD_Reference 17) field.</summary>
    public class OppRefListItem
    {
        public string Value { get; set; }
        public string Name { get; set; }
    }

    /// <summary>One row of a generic FK lookup (id + display label).</summary>
    public class OppRefItem
    {
        public int Id { get; set; }
        public string Name { get; set; }
    }

    /// <summary>A unit of measure for the UOM dropdown in the opportunity panel grid.</summary>
    public class OppUomItem
    {
        public int C_UOM_ID { get; set; }
        public string Name { get; set; }
    }

    /// <summary>One Product or Charge row in the catalog autocomplete for the opportunity panel.</summary>
    public class OppCatalogItem
    {
        public int RecordId { get; set; }
        /// <summary>"P" = product, "C" = charge.</summary>
        public string Kind { get; set; }
        public string SearchKey { get; set; }
        public string DisplayName { get; set; }
        public string Description { get; set; }
        public bool HasAttributeSet { get; set; }
        public string ProductType { get; set; }
    }

    /// <summary>Attribute-set definition returned by GetProductAttributes.</summary>
    public class OppAttributeSetInfo
    {
        public int M_AttributeSet_ID { get; set; }
        public int M_Product_ID { get; set; }
        public string ProductName { get; set; }
        public bool IsLot { get; set; }
        public bool IsSerNo { get; set; }
        public bool IsGuaranteeDate { get; set; }
        public string GuaranteeDateDefault { get; set; }
        public bool IsMandatory { get; set; }
        public bool IsCanCreate { get; set; }
        public bool IsCanEdit { get; set; }
        public List<OppAttributeDef> Attributes { get; set; }
        public OppAttributeSetInfo() { Attributes = new List<OppAttributeDef>(); }
    }

    /// <summary>One attribute within an opportunity-panel attribute set.</summary>
    public class OppAttributeDef
    {
        public int M_Attribute_ID { get; set; }
        public string Name { get; set; }
        public string ValueType { get; set; }
        public bool IsInstanceAttribute { get; set; }
        public bool IsMandatory { get; set; }
        public List<OppAttributeValueDef> Values { get; set; }
    }

    /// <summary>A selectable list value for an opportunity-panel attribute.</summary>
    public class OppAttributeValueDef
    {
        public int M_AttributeValue_ID { get; set; }
        public string Code { get; set; }
        public string Name { get; set; }
    }

    /// <summary>One stored attribute value on an existing instance.</summary>
    public class OppAttributeInstanceValue
    {
        public int M_Attribute_ID { get; set; }
        public string ValueType { get; set; }
        public int M_AttributeValue_ID { get; set; }
        public decimal? NumberValue { get; set; }
        public string StringValue { get; set; }
    }

    /// <summary>Result of creating or updating an attribute-set instance via SaveAttribute.</summary>
    public class OppAttributeSaveResult
    {
        public int M_AttributeSetInstance_ID { get; set; }
        public string Description { get; set; }
        public string Error { get; set; }
    }

    /// <summary>Attribute-instance create/update request from the opportunity panel.</summary>
    public class OppAttributeSaveRequest
    {
        public int M_Product_ID { get; set; }
        public int M_AttributeSetInstance_ID { get; set; }
        public string Lot { get; set; }
        public string SerNo { get; set; }
        public string GuaranteeDate { get; set; }
        public List<OppAttributeValueSelection> Values { get; set; }
    }

    /// <summary>One entered attribute value in an opportunity-panel attribute save request.</summary>
    public class OppAttributeValueSelection
    {
        public int M_Attribute_ID { get; set; }
        public string ValueType { get; set; }
        public int M_AttributeValue_ID { get; set; }
        public decimal? NumberValue { get; set; }
        public string StringValue { get; set; }
        public string DisplayValue { get; set; }
    }

    /// <summary>One inbound opportunity line to insert / update.</summary>
    public class OppLineInput
    {
        public int VAS_OppLines_ID { get; set; }
        public string RowKey { get; set; }
        public int Line { get; set; }
        public int M_Product_ID { get; set; }
        public int C_Charge_ID { get; set; }
        public int M_AttributeSetInstance_ID { get; set; }
        public decimal PlannedQty { get; set; }
        public int C_UOM_ID { get; set; }
        public decimal PlannedPrice { get; set; }
        public string Description { get; set; }
        public Dictionary<string, object> Values { get; set; }
        public List<string> TouchedCols { get; set; }
        public OppLineInput() { Values = new Dictionary<string, object>(); TouchedCols = new List<string>(); }
    }

    /// <summary>POST body for the batch opportunity-line save.</summary>
    public class OppSaveLinesRequest
    {
        public int VAS_Opportunity_ID { get; set; }
        public int AD_Window_ID { get; set; }
        public int Page { get; set; }
        public List<OppLineInput> Lines { get; set; }
        public OppSaveLinesRequest() { Lines = new List<OppLineInput>(); }
    }

    /// <summary>POST body for the batch opportunity-line delete.</summary>
    public class OppDeleteLinesRequest
    {
        public int VAS_Opportunity_ID { get; set; }
        public int AD_Window_ID { get; set; }
        public int Page { get; set; }
        public List<int> LineIds { get; set; }
        public OppDeleteLinesRequest() { LineIds = new List<int>(); }
    }

    /// <summary>A save failure for one specific opportunity line.</summary>
    public class OppLineSaveError
    {
        public string RowKey { get; set; }
        public int VAS_OppLines_ID { get; set; }
        public int Line { get; set; }
        public string Message { get; set; }
    }

    /// <summary>Result of a save / delete batch for opportunity lines.</summary>
    public class OppSaveResult
    {
        public bool Success { get; set; }
        public string ErrorKey { get; set; }
        public string ErrorDetail { get; set; }
        public List<OppLineSaveError> LineErrors { get; set; }
        public List<OppLineRow> Lines { get; set; }
        public int LinesTotal { get; set; }
        public int LinePage { get; set; }
        public int LinePageSize { get; set; }
        public decimal OtherPagesPlannedAmt { get; set; }
        public OppSaveResult() { Lines = new List<OppLineRow>(); LineErrors = new List<OppLineSaveError>(); }
    }

    #endregion
}
