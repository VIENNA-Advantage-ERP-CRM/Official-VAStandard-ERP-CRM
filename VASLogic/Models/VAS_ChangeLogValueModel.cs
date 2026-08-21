/// <summary>
/// Module Name : VASLogic
/// Purpose     : Turns a value AD_ChangeLog STORED into the value the field
///               SHOWS, for the overview panels' Activity feeds.
///
///               The change log records raw column values. For the fields a
///               reader most wants to trace that is a foreign key, so a
///               field-level activity row read "Business Partner was 1000042 →
///               now 1000117" — which names the edit and nothing else. A date
///               field logs a full timestamp, so an edited Date Promised read
///               "20-08-2026 00:00:00", a midnight nobody chose against a field
///               that has no time part at all.
///
///               One resolver, shared by every panel that reports field-level
///               edits (VAS_092, VAS_098 - VAS_104, VAS_106, VAS_190), rather
///               than ten copies of the same dictionary walk.
///
/// Chronological development:
///   VAI163   2026-08-20  Created, lifted out of
///                        VAS_092_OverviewPurchaseOrderModel where it was first
///                        written, so the other nine panels resolve their
///                        change-log values the same way instead of each
///                        printing ids.
/// </summary>

using System;
using System.Collections.Generic;
using System.Data;
using System.Data.SqlClient;
using System.Globalization;
using System.Text.RegularExpressions;
using VAdvantage.DataBase;
using VAdvantage.Logging;
using VAdvantage.Utility;

namespace VASLogic.Models
{
    /// <summary>
    /// Resolves AD_ChangeLog values for display. Create one per request and keep
    /// it for the life of that request: it caches what it resolves, so a field
    /// edited ten times over a document's life costs one read per distinct value.
    /// </summary>
    public class VAS_ChangeLogValueModel
    {
        private static readonly VLogger _log =
            VLogger.GetVLogger(typeof(VAS_ChangeLogValueModel).FullName);

        // ----------------------------------------------------------------- //
        //  Display types (AD_Reference_ID)                                   //
        // ----------------------------------------------------------------- //
        // Named rather than left as literals at the call site: the numbers are
        // the platform's, and a bare "refType == 19" says nothing about why.
        private const int REF_DATE       = 15;
        private const int REF_DATETIME   = 16;
        private const int REF_LIST       = 17;
        private const int REF_TABLE      = 18;
        private const int REF_TABLEDIR   = 19;
        private const int REF_LOCATION   = 21;
        private const int REF_ACCOUNT    = 25;
        private const int REF_BUTTON     = 28;
        private const int REF_SEARCH     = 30;
        private const int REF_LOCATOR    = 31;
        private const int REF_PATTRIBUTE = 35;

        /// <summary>
        /// Turns what the change log stored into what the field shows.
        ///
        ///   * a reference to another record -> that record's identifier;
        ///   * a list value                  -> the list entry's name;
        ///   * a date or date-time           -> the DATE alone, as yyyy-MM-dd —
        ///     a form with no locale in it, which the panel renders in the
        ///     reader's own.
        ///
        /// Anything it cannot resolve is returned exactly as logged: an
        /// unrecognised reference, a record since deleted, or a value that is not
        /// an id at all still reports what the log holds, which is strictly
        /// better than a blank.
        /// </summary>
        /// <param name="raw">The logged value, already stripped of the platform's
        /// literal "null" by the caller.</param>
        /// <param name="columnName">The column that changed (AD_Column.ColumnName).</param>
        /// <param name="refType">AD_Column.AD_Reference_ID — the display type.</param>
        /// <param name="refValueId">AD_Column.AD_Reference_Value_ID — the list or
        /// table reference behind it, when it has one.</param>
        public string Display(string raw, string columnName, int refType, int refValueId)
        {
            if (string.IsNullOrEmpty(raw)) return "";
            if (columnName == null) columnName = "";

            try
            {
                // Time (24) keeps its clock — that is the whole value.
                if (refType == REF_DATE || refType == REF_DATETIME) return DateOnly(raw);

                // List and Button-with-a-reference store the CODE.
                if ((refType == REF_LIST || refType == REF_BUTTON) && refValueId > 0)
                    return ResolveListValue(raw, refValueId) ?? raw;

                // Everything that stores a record id. Recognised by the reference
                // type where the dictionary types it properly, and otherwise by the
                // column NAME — a custom module column typed as a plain Search or
                // ID still ends in _ID and still logs a key nobody can read.
                bool looksLikeId =
                    refType == REF_TABLE    || refType == REF_TABLEDIR ||
                    refType == REF_LOCATION || refType == REF_ACCOUNT  ||
                    refType == REF_SEARCH   || refType == REF_LOCATOR  ||
                    refType == REF_PATTRIBUTE ||
                    columnName.EndsWith("_ID", StringComparison.OrdinalIgnoreCase);
                if (looksLikeId)
                    return ResolveReferenceName(raw, columnName, refType, refValueId) ?? raw;
            }
            catch (Exception ex)
            {
                _log.Severe("Display (" + columnName + "=" + raw + "): " + ex.Message);
            }
            return raw;
        }

        /// <summary>
        /// Normalises a logged value. The platform writes the literal "null" into
        /// AD_ChangeLog for a cleared field, which would otherwise be shown to the
        /// reader as though it were the text "null".
        /// </summary>
        public static string Normalise(string value)
        {
            if (string.IsNullOrEmpty(value)) return "";
            string v = value.Trim();
            return string.Equals(v, "null", StringComparison.OrdinalIgnoreCase) ? "" : v;
        }

        /// <summary>
        /// The DATE part of a logged timestamp, as yyyy-MM-dd.
        ///
        /// The stored text varies with what wrote it (invariant, the server's
        /// culture, or the database's own rendering), so it is parsed permissively
        /// and, failing that, cut at the first space — which is where every one of
        /// those formats puts the time. A value that is neither is returned
        /// untouched.
        /// </summary>
        public static string DateOnly(string raw)
        {
            if (string.IsNullOrEmpty(raw)) return "";

            DateTime parsed;
            if (DateTime.TryParse(raw, CultureInfo.InvariantCulture,
                                  DateTimeStyles.None, out parsed) ||
                DateTime.TryParse(raw, out parsed))
            {
                return parsed.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
            }

            int space = raw.IndexOf(' ');
            return space > 0 ? raw.Substring(0, space) : raw;
        }

        /// <summary>The list entry's name for a logged code (AD_Ref_List).</summary>
        private string ResolveListValue(string raw, int refValueId)
        {
            string key = "L:" + refValueId + ":" + raw;
            string cached;
            if (_valueCache.TryGetValue(key, out cached)) return cached;

            string name = null;
            try
            {
                string sql = @"SELECT rl.Name
                                 FROM AD_Ref_List rl
                                WHERE rl.AD_Reference_ID = @AD_Reference_ID
                                  AND rl.Value           = @Value
                                  AND COALESCE(rl.IsActive, 'Y') = 'Y'";
                SqlParameter[] param = new SqlParameter[]
                {
                    new SqlParameter("@AD_Reference_ID", refValueId),
                    new SqlParameter("@Value", raw)
                };
                DataSet ds = DB.ExecuteDataset(sql, param, null);
                if (ds != null && ds.Tables.Count > 0 && ds.Tables[0].Rows.Count > 0)
                {
                    string n = Util.GetValueOfString(ds.Tables[0].Rows[0]["Name"]);
                    if (!string.IsNullOrEmpty(n)) name = n;
                }
            }
            catch (Exception ex)
            {
                _log.Severe("ResolveListValue (ref=" + refValueId + ", value=" + raw + "): " + ex.Message);
            }

            _valueCache[key] = name;
            return name;
        }

        /// <summary>
        /// The referenced record's identifier for a logged id — "Acme Ltd" where
        /// the log holds 1000042.
        ///
        /// Returns null when the value is not an id, the table cannot be worked
        /// out, or the record is gone; the caller then keeps the raw value.
        /// </summary>
        private string ResolveReferenceName(string raw, string columnName, int refType, int refValueId)
        {
            int id;
            if (!int.TryParse(raw, out id) || id <= 0) return null;

            RefTarget target = ReferenceTargetFor(columnName, refType, refValueId);
            if (target == null) return null;

            string key = "R:" + target.Table + ":" + id;
            string cached;
            if (_valueCache.TryGetValue(key, out cached)) return cached;

            string name = null;
            try
            {
                // Table and column names come from the dictionary and are checked
                // against SAFE_NAME before they are written into a statement, so
                // nothing here can carry anything but an identifier. The id is an
                // int by the parse above.
                string sql = "SELECT " + target.Display + " AS DisplayValue" +
                             "  FROM " + target.Table +
                             " WHERE " + target.Key + " = " + id;
                DataSet ds = DB.ExecuteDataset(sql, null, null);
                if (ds != null && ds.Tables.Count > 0 && ds.Tables[0].Rows.Count > 0)
                {
                    string n = Util.GetValueOfString(ds.Tables[0].Rows[0]["DisplayValue"]);
                    if (!string.IsNullOrEmpty(n)) name = n.Trim();
                }
            }
            catch (Exception ex)
            {
                _log.Severe("ResolveReferenceName (" + target.Table + " id=" + id + "): " + ex.Message);
            }

            _valueCache[key] = name;
            return name;
        }

        /// <summary>Where a reference column's value points, and what to show for
        /// it: the table, its key column and the column carrying its identifier.</summary>
        private class RefTarget
        {
            public string Table   { get; set; }
            public string Key     { get; set; }
            public string Display { get; set; }
        }

        /// <summary>Resolved reference targets, keyed by column — one dictionary
        /// read per FIELD rather than per log row.</summary>
        private readonly Dictionary<string, RefTarget> _targetCache =
            new Dictionary<string, RefTarget>(StringComparer.OrdinalIgnoreCase);

        /// <summary>Resolved display values, keyed by table + id (or list + code).</summary>
        private readonly Dictionary<string, string> _valueCache =
            new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

        /// <summary>Only ever an identifier — nothing built from the dictionary is
        /// written into a statement without passing this.</summary>
        private static readonly Regex SAFE_NAME = new Regex(@"^[A-Za-z0-9_]+$", RegexOptions.Compiled);

        /// <summary>
        /// Works out which table a reference column points at, and which of its
        /// columns to read.
        ///
        ///   Table / Search with a reference value — AD_Ref_Table names the table,
        ///   its key and its display column outright.
        ///
        ///   TableDir, and anything else whose column simply ends in _ID — the
        ///   table IS the column name without the suffix, which is the platform's
        ///   own convention, and the key is the column itself.
        ///
        /// The display column falls back to the referenced table's first
        /// IDENTIFIER column (AD_Column.IsIdentifier), which is exactly what the
        /// platform shows for that record everywhere else. A table with no
        /// identifier at all yields null and the caller keeps the raw id.
        /// </summary>
        private RefTarget ReferenceTargetFor(string columnName, int refType, int refValueId)
        {
            string cacheKey = columnName + "|" + refType + "|" + refValueId;
            RefTarget cached;
            if (_targetCache.TryGetValue(cacheKey, out cached)) return cached;

            RefTarget target = null;
            try
            {
                string table = "", key = "", display = "";

                // The dictionary names the target outright for a Table / Search
                // reference.
                if (refValueId > 0 && (refType == REF_TABLE || refType == REF_SEARCH))
                {
                    string sql = @"SELECT t.TableName    AS TableName,
                                          kc.ColumnName  AS KeyCol,
                                          dc.ColumnName  AS DisplayCol
                                     FROM AD_Ref_Table rt
                                    INNER JOIN AD_Table t   ON (t.AD_Table_ID   = rt.AD_Table_ID)
                                     LEFT OUTER JOIN AD_Column kc ON (kc.AD_Column_ID = rt.AD_Key)
                                     LEFT OUTER JOIN AD_Column dc ON (dc.AD_Column_ID = rt.AD_Display)
                                    WHERE rt.AD_Reference_ID = @AD_Reference_ID";
                    DataSet ds = DB.ExecuteDataset(sql,
                        new SqlParameter[] { new SqlParameter("@AD_Reference_ID", refValueId) }, null);
                    if (ds != null && ds.Tables.Count > 0 && ds.Tables[0].Rows.Count > 0)
                    {
                        DataRow r = ds.Tables[0].Rows[0];
                        table   = Util.GetValueOfString(r["TableName"]);
                        key     = Util.GetValueOfString(r["KeyCol"]);
                        display = Util.GetValueOfString(r["DisplayCol"]);
                    }
                }

                // Otherwise the column names its own table: C_BPartner_ID ->
                // C_BPartner. Also the fallback when the reference above named
                // nothing usable.
                if (string.IsNullOrEmpty(table) &&
                    columnName.EndsWith("_ID", StringComparison.OrdinalIgnoreCase))
                {
                    table = columnName.Substring(0, columnName.Length - 3);
                }
                if (string.IsNullOrEmpty(table)) { _targetCache[cacheKey] = null; return null; }
                if (string.IsNullOrEmpty(key)) key = table + "_ID";
                if (string.IsNullOrEmpty(display)) display = FirstIdentifierColumn(table);

                if (SAFE_NAME.IsMatch(table) && SAFE_NAME.IsMatch(key) &&
                    !string.IsNullOrEmpty(display) && SAFE_NAME.IsMatch(display))
                {
                    target = new RefTarget { Table = table, Key = key, Display = display };
                }
            }
            catch (Exception ex)
            {
                _log.Severe("ReferenceTargetFor (" + columnName + "): " + ex.Message);
            }

            _targetCache[cacheKey] = target;
            return target;
        }

        /// <summary>
        /// The first column the dictionary marks as a table's IDENTIFIER — what the
        /// platform shows for one of its records. Null when the table has none, or
        /// is not in the dictionary at all (which is the answer for a column whose
        /// name only LOOKS like a reference).
        /// </summary>
        private string FirstIdentifierColumn(string tableName)
        {
            try
            {
                string sql = @"SELECT c.ColumnName
                                 FROM AD_Column c
                                INNER JOIN AD_Table t ON (t.AD_Table_ID = c.AD_Table_ID)
                                WHERE UPPER(t.TableName)       = UPPER(@TableName)
                                  AND c.IsIdentifier           = 'Y'
                                  AND COALESCE(c.IsActive, 'Y') = 'Y'
                                ORDER BY c.SeqNo, c.AD_Column_ID";
                DataSet ds = DB.ExecuteDataset(sql,
                    new SqlParameter[] { new SqlParameter("@TableName", tableName) }, null);
                if (ds == null || ds.Tables.Count == 0 || ds.Tables[0].Rows.Count == 0) return null;
                return Util.GetValueOfString(ds.Tables[0].Rows[0]["ColumnName"]);
            }
            catch (Exception ex)
            {
                _log.Severe("FirstIdentifierColumn (" + tableName + "): " + ex.Message);
                return null;
            }
        }
    }
}
