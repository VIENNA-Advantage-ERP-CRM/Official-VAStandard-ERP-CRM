using Newtonsoft.Json;
using System;
using System.Collections.Generic;
using System.Data;
using System.Data.SqlClient;
using System.Globalization;
using System.Linq;
using System.Web.Mvc;
using VAdvantage.DataBase;
using VAdvantage.Model;
using VAdvantage.Utility;
using VIS.Filters;

namespace VAS.Controllers
{
    /// <summary>
    /// Module Name : VAS_128_IncompleteRecordsWidget (Product Master dashboard)
    /// Purpose     : Data endpoints for the 5x3 "Incomplete Records" widget - a
    ///               data-hygiene worklist of active, non-summary products that
    ///               are missing one or more master-data fields the CURRENT USER
    ///               chose to track. Each user's tracked-field set is stored as
    ///               one AD_Preference row per field (Attribute 'PM.IR.*', scoped
    ///               by client+user, Org 0, Window 0); a missing row means the
    ///               field is tracked (selected = Y), so new fields are on by
    ///               default. The load endpoint returns the tracked set and the
    ///               incomplete products with their ordered missing-field keys;
    ///               the save endpoint persists one field's Y/N immediately.
    ///               Verified is only a miss for BOM products. Preferred Vendor
    ///               means an active M_Product_PO with IsCurrentVendor='Y' to an
    ///               active vendor. Category is mandatory and never tracked.
    ///               MRole is applied to the primary fetched table (M_Product) on
    ///               the product read; all input is parameterized; the SQL uses
    ///               only COALESCE / CASE / EXISTS / NULLIF / TRIM (no NVL,
    ///               DECODE, LISTAGG, FETCH/LIMIT/OFFSET, DB date formatting or
    ///               DB-specific upsert), so it runs unchanged on Oracle and
    ///               PostgreSQL. The VA010 Quality field is schema-guarded: on a
    ///               database without M_Product.VA010_QualityPlan_ID it is simply
    ///               dropped from the catalog and never flagged.
    /// Widget size : 5 columns x 3 rows.
    /// Widget number 128.
    /// Chronological development:
    ///   &lt;EmpCode&gt;   2026-07-20 Created
    /// </summary>
    public class VAS_128_IncompleteRecordsWidgetController : Controller
    {
        // Preference attribute prefix (one AD_Preference row per tracked field).
        private const string PREF_PREFIX = "PM.IR.";

        /// <summary>
        /// DB-appropriate character literal. On this schema the text columns are
        /// national-character (NVARCHAR), so a plain '' / 'text' literal combined
        /// with them inside COALESCE / NULLIF raises ORA-12704 on Oracle; the
        /// N'..' prefix fixes it. PostgreSQL has no N'..' syntax, so it stays a
        /// plain quoted literal there.
        /// </summary>
        private static string NLiteral(string text)
        {
            return DB.IsPostgreSQL() ? "'" + text + "'" : "N'" + text + "'";
        }

        /// <summary>
        /// The fixed 15-field catalog, in chip/settings order. Each entry pairs
        /// the frontend field key with its AD_Preference attribute suffix. The
        /// order here IS the required display order.
        /// </summary>
        private static readonly Tuple<string, string>[] Catalog = new[]
        {
            Tuple.Create("hsn", "HSN"),
            Tuple.Create("barcode", "BARCODE"),
            Tuple.Create("sku", "SKU"),
            Tuple.Create("image", "IMAGE"),
            Tuple.Create("description", "DESCRIPTION"),
            Tuple.Create("brand", "BRAND"),
            Tuple.Create("salesRep", "SALESREP"),
            Tuple.Create("revenueRecognition", "REVREC"),
            Tuple.Create("mailTemplate", "MAIL"),
            Tuple.Create("qualityCriteria", "QUALITY"),
            Tuple.Create("verified", "VERIFIED"),
            Tuple.Create("preferredVendor", "PREFVENDOR"),
            Tuple.Create("guaranteeDetails", "GUARANTEE"),
            Tuple.Create("attributeGroup", "ATTRGROUP"),
            Tuple.Create("customTariff", "TARIFF")
        };

        /// <summary>
        /// Loads the current user's tracked-field preferences and the products
        /// that are missing at least one tracked field.
        /// </summary>
        /// <returns>JSON { trackedFields[], availableFields[], items[] }.</returns>
        [AjaxAuthorizeAttribute]
        [AjaxSessionFilterAttribute]
        public JsonResult GetIncompleteRecords()
        {
            if (Session["ctx"] == null)
            {
                return Fail(Msg.GetMsg(Env.GetCtx(), "SessionExpired") ?? "Session Expired");
            }

            Ctx ctx = Session["ctx"] as Ctx;

            try
            {
                // Quality is schema-guarded: on a DB without the VA010 column the
                // field is not available at all (never tracked, never flagged).
                bool qualityAvailable = HasColumn("M_Product", "VA010_QualityPlan_ID");

                List<string> availableFields = Catalog
                    .Where(c => qualityAvailable || c.Item1 != "qualityCriteria")
                    .Select(c => c.Item1)
                    .ToList();

                // Preference map: field key -> 'Y'/'N' (missing row => 'Y').
                Dictionary<string, string> prefByKey = ReadPreferences(ctx);
                HashSet<string> tracked = new HashSet<string>();
                foreach (Tuple<string, string> field in Catalog)
                {
                    if (!availableFields.Contains(field.Item1)) { continue; }
                    string value;
                    bool selected = !prefByKey.TryGetValue(field.Item1, out value) || !"N".Equals(value, StringComparison.OrdinalIgnoreCase);
                    if (selected) { tracked.Add(field.Item1); }
                }

                List<object> items = LoadIncompleteProducts(ctx, tracked, availableFields, qualityAvailable);

                return Ok(new
                {
                    trackedFields = Catalog.Where(c => tracked.Contains(c.Item1)).Select(c => c.Item1).ToList(),
                    availableFields = availableFields,
                    items = items
                });
            }
            catch (Exception ex)
            {
                return Fail(ex.Message);
            }
        }

        /// <summary>
        /// Persists one tracked-field preference for the current user, updating
        /// the existing AD_Preference row or inserting a new one.
        /// </summary>
        /// <param name="fieldKey">Catalog field key (e.g. "hsn").</param>
        /// <param name="value">'Y' to track, 'N' to stop tracking.</param>
        /// <returns>JSON { success, fieldKey, value }.</returns>
        [HttpPost]
        [AjaxAuthorizeAttribute]
        [AjaxSessionFilterAttribute]
        public JsonResult SaveFieldPreference(string fieldKey = "", string value = "Y")
        {
            if (Session["ctx"] == null)
            {
                return Fail(Msg.GetMsg(Env.GetCtx(), "SessionExpired") ?? "Session Expired");
            }

            Ctx ctx = Session["ctx"] as Ctx;

            string attributeSuffix = Catalog
                .Where(c => c.Item1 == fieldKey)
                .Select(c => c.Item2)
                .FirstOrDefault();
            if (attributeSuffix == null)
            {
                return Fail("Unknown field.");
            }

            string prefValue = "N".Equals(value, StringComparison.OrdinalIgnoreCase) ? "N" : "Y";
            string attribute = PREF_PREFIX + attributeSuffix;

            try
            {
                int preferenceId = FindPreferenceId(ctx, attribute);

                // Use the AD_Preference model (the project's ID-generator /
                // repository convention) rather than a raw INSERT, so this works
                // the same on Oracle and PostgreSQL - no MERGE / ON CONFLICT.
                // Existing row loaded by id already carries the attribute; a new
                // row gets it from the constructor (the proven VAS_080 pattern).
                MPreference preference = preferenceId > 0
                    ? new MPreference(ctx, preferenceId, null)
                    : new MPreference(ctx, attribute, prefValue, null);
                preference.SetValue(prefValue);
                preference.SetAD_User_ID(ctx.GetAD_User_ID());
                // Org 0 / Window 0: the user's tracked set follows them across
                // organizations and windows (matches the load-query filter).
                preference.SetAD_Org_ID(0);
                if (preference.Get_ColumnIndex("AD_Window_ID") >= 0)
                {
                    preference.Set_Value("AD_Window_ID", 0);
                }

                if (!preference.Save())
                {
                    return Fail("Preference could not be saved.");
                }

                return Ok(new { success = true, fieldKey = fieldKey, value = prefValue });
            }
            catch (Exception ex)
            {
                return Fail(ex.Message);
            }
        }

        /// <summary>Current user's PM.IR.* preferences, newest active row per key.</summary>
        private Dictionary<string, string> ReadPreferences(Ctx ctx)
        {
            Dictionary<string, string> map = new Dictionary<string, string>();

            string sql = @"
                SELECT p.Attribute AS PreferenceKey,
                       p.Value AS PreferenceValue,
                       p.Updated AS UpdatedAt
                FROM AD_Preference p
                WHERE p.AD_Client_ID=@AD_Client_ID
                  AND p.AD_User_ID=@AD_User_ID
                  AND p.AD_Org_ID=0
                  AND COALESCE(p.AD_Window_ID, 0)=0
                  AND p.IsActive='Y'
                  AND p.Attribute LIKE 'PM.IR.%'
                ORDER BY p.Attribute, p.Updated DESC";

            // Reverse the suffix->key lookup once.
            Dictionary<string, string> keyBySuffix = new Dictionary<string, string>();
            foreach (Tuple<string, string> field in Catalog) { keyBySuffix[field.Item2] = field.Item1; }

            IDataReader dr = null;
            try
            {
                dr = DB.ExecuteReader(sql, new SqlParameter[]
                {
                    new SqlParameter("@AD_Client_ID", ctx.GetAD_Client_ID()),
                    new SqlParameter("@AD_User_ID", ctx.GetAD_User_ID())
                });

                while (dr != null && dr.Read())
                {
                    string attribute = Util.GetValueOfString(dr["PreferenceKey"]);
                    if (string.IsNullOrEmpty(attribute) || !attribute.StartsWith(PREF_PREFIX)) { continue; }
                    string suffix = attribute.Substring(PREF_PREFIX.Length);
                    string key;
                    if (!keyBySuffix.TryGetValue(suffix, out key)) { continue; }
                    // ORDER BY newest-first per attribute: keep the first seen.
                    if (!map.ContainsKey(key))
                    {
                        map[key] = Util.GetValueOfString(dr["PreferenceValue"]);
                    }
                }
            }
            finally
            {
                if (dr != null) { dr.Close(); dr.Dispose(); }
            }

            return map;
        }

        /// <summary>AD_Preference_ID of the current user's row for one attribute, or 0.</summary>
        private int FindPreferenceId(Ctx ctx, string attribute)
        {
            string sql = @"
                SELECT p.AD_Preference_ID AS PreferenceId,
                       p.Updated AS UpdatedAt
                FROM AD_Preference p
                WHERE p.AD_Client_ID=@AD_Client_ID
                  AND p.AD_User_ID=@AD_User_ID
                  AND p.AD_Org_ID=0
                  AND COALESCE(p.AD_Window_ID, 0)=0
                  AND p.IsActive='Y'
                  AND p.Attribute=@Attribute
                ORDER BY p.Updated DESC";

            int preferenceId = 0;
            IDataReader dr = null;
            try
            {
                dr = DB.ExecuteReader(sql, new SqlParameter[]
                {
                    new SqlParameter("@AD_Client_ID", ctx.GetAD_Client_ID()),
                    new SqlParameter("@AD_User_ID", ctx.GetAD_User_ID()),
                    new SqlParameter("@Attribute", attribute)
                });
                if (dr != null && dr.Read())
                {
                    // First row = newest; take it and stop.
                    preferenceId = Util.GetValueOfInt(dr["PreferenceId"]);
                }
            }
            finally
            {
                if (dr != null) { dr.Close(); dr.Dispose(); }
            }

            return preferenceId;
        }

        /// <summary>
        /// Products missing at least one tracked field, newest update first, with
        /// the ordered missing-field keys built in code (never aggregated in SQL).
        /// </summary>
        private List<object> LoadIncompleteProducts(Ctx ctx, HashSet<string> tracked, List<string> availableFields, bool qualityAvailable)
        {
            // Quality CASE is present only when the column exists; otherwise the
            // flag is a constant 0, so the field can never be a miss.
            string missQuality = qualityAvailable
                ? "CASE WHEN COALESCE(p.VA010_QualityPlan_ID, 0) = 0 THEN 1 ELSE 0 END"
                : "0";

            // Empty-string / 'Unknown' literals must carry the national-character
            // prefix on Oracle where they combine with NVARCHAR columns (12704).
            string E = NLiteral("");

            string innerSql = @"
                SELECT p.M_Product_ID AS ProductId,
                       p.Name AS ProductName,
                       p.Value AS ProductCode,
                       pc.Name AS CategoryName,
                       p.Updated AS UpdatedAt,
                       COALESCE(u.Name, u.Value, " + NLiteral("Unknown") + @") AS UpdatedByName,
                       CASE WHEN p.IsBOM = 'Y' THEN 1 ELSE 0 END AS HasBom,
                       CASE WHEN NULLIF(TRIM(p.VAS_HSN_SACCode), " + E + @") IS NULL THEN 1 ELSE 0 END AS MissHsn,
                       CASE WHEN NULLIF(TRIM(p.UPC), " + E + @") IS NULL THEN 1 ELSE 0 END AS MissBarcode,
                       CASE WHEN NULLIF(TRIM(p.SKU), " + E + @") IS NULL THEN 1 ELSE 0 END AS MissSku,
                       CASE WHEN COALESCE(p.AD_Image_ID, 0) = 0 THEN 1 ELSE 0 END AS MissImage,
                       CASE WHEN NULLIF(TRIM(p.Description), " + E + @") IS NULL THEN 1 ELSE 0 END AS MissDescription,
                       CASE WHEN COALESCE(p.M_Brand_ID, 0) = 0 THEN 1 ELSE 0 END AS MissBrand,
                       CASE WHEN COALESCE(p.SalesRep_ID, 0) = 0 THEN 1 ELSE 0 END AS MissSalesRep,
                       CASE WHEN COALESCE(p.C_RevenueRecognition_ID, 0) = 0 THEN 1 ELSE 0 END AS MissRevenueRec,
                       CASE WHEN COALESCE(p.R_MailText_ID, 0) = 0 THEN 1 ELSE 0 END AS MissMailTemplate,
                       " + missQuality + @" AS MissQualityCriteria,
                       CASE WHEN p.IsBOM = 'Y' AND COALESCE(p.IsVerified, 'N') <> 'Y' THEN 1 ELSE 0 END AS MissVerified,
                       CASE WHEN EXISTS (
                               SELECT 1
                               FROM M_Product_PO po
                               JOIN C_BPartner v ON (v.C_BPartner_ID = po.C_BPartner_ID AND v.AD_Client_ID = po.AD_Client_ID)
                               WHERE po.M_Product_ID = p.M_Product_ID
                                 AND po.AD_Client_ID = p.AD_Client_ID
                                 AND po.IsActive = 'Y'
                                 AND po.IsCurrentVendor = 'Y'
                                 AND v.IsActive = 'Y'
                                 AND v.IsVendor = 'Y'
                           ) THEN 0 ELSE 1 END AS MissPreferredVendor,
                       CASE WHEN p.GuaranteeDays IS NULL THEN 1 ELSE 0 END AS MissGuarantee,
                       CASE WHEN COALESCE(p.M_AttributeSet_ID, 0) = 0 THEN 1 ELSE 0 END AS MissAttributeGroup,
                       CASE WHEN COALESCE(p.M_CustomTariff_ID, 0) = 0 THEN 1 ELSE 0 END AS MissCustomTariff
                FROM M_Product p
                JOIN M_Product_Category pc ON (pc.M_Product_Category_ID = p.M_Product_Category_ID)
                LEFT JOIN AD_User u ON (u.AD_User_ID = p.UpdatedBy)
                WHERE p.AD_Client_ID = @AD_Client_ID
                  AND p.IsActive = 'Y'
                  AND COALESCE(p.IsSummary, 'N') = 'N'";

            // MRole (org/role data-access) on the primary table; AddAccessSQL
            // appends its predicate to the END, and the EXISTS is in the SELECT
            // list, so appending after the base WHERE stays valid.
            innerSql = MRole.GetDefault(ctx).AddAccessSQL(
                innerSql,
                "p",
                MRole.SQL_FULLYQUALIFIED,
                MRole.SQL_RO
            );

            // Track flags: 'Y' only for tracked+available fields. When a field is
            // untracked its OR branch can never include a product for that field.
            string T(string key) { return tracked.Contains(key) ? "@Track_Y" : "@Track_N"; }

            string sql = @"
                WITH ProdStatus AS (
                    " + innerSql + @"
                )
                SELECT s.ProductId, s.ProductName, s.ProductCode, s.CategoryName,
                       s.UpdatedAt, s.UpdatedByName, s.HasBom,
                       s.MissHsn, s.MissBarcode, s.MissSku, s.MissImage, s.MissDescription,
                       s.MissBrand, s.MissSalesRep, s.MissRevenueRec, s.MissMailTemplate,
                       s.MissQualityCriteria, s.MissVerified, s.MissPreferredVendor,
                       s.MissGuarantee, s.MissAttributeGroup, s.MissCustomTariff
                FROM ProdStatus s
                WHERE (" + T("hsn") + @" = 'Y' AND s.MissHsn = 1)
                   OR (" + T("barcode") + @" = 'Y' AND s.MissBarcode = 1)
                   OR (" + T("sku") + @" = 'Y' AND s.MissSku = 1)
                   OR (" + T("image") + @" = 'Y' AND s.MissImage = 1)
                   OR (" + T("description") + @" = 'Y' AND s.MissDescription = 1)
                   OR (" + T("brand") + @" = 'Y' AND s.MissBrand = 1)
                   OR (" + T("salesRep") + @" = 'Y' AND s.MissSalesRep = 1)
                   OR (" + T("revenueRecognition") + @" = 'Y' AND s.MissRevenueRec = 1)
                   OR (" + T("mailTemplate") + @" = 'Y' AND s.MissMailTemplate = 1)
                   OR (" + T("qualityCriteria") + @" = 'Y' AND s.MissQualityCriteria = 1)
                   OR (" + T("verified") + @" = 'Y' AND s.MissVerified = 1)
                   OR (" + T("preferredVendor") + @" = 'Y' AND s.MissPreferredVendor = 1)
                   OR (" + T("guaranteeDetails") + @" = 'Y' AND s.MissGuarantee = 1)
                   OR (" + T("attributeGroup") + @" = 'Y' AND s.MissAttributeGroup = 1)
                   OR (" + T("customTariff") + @" = 'Y' AND s.MissCustomTariff = 1)
                ORDER BY s.UpdatedAt DESC, s.ProductName, s.ProductCode";

            // Column-flag -> field key, in fixed catalog order (missingKeys order).
            Tuple<string, string>[] flagColumns = new[]
            {
                Tuple.Create("MissHsn", "hsn"),
                Tuple.Create("MissBarcode", "barcode"),
                Tuple.Create("MissSku", "sku"),
                Tuple.Create("MissImage", "image"),
                Tuple.Create("MissDescription", "description"),
                Tuple.Create("MissBrand", "brand"),
                Tuple.Create("MissSalesRep", "salesRep"),
                Tuple.Create("MissRevenueRec", "revenueRecognition"),
                Tuple.Create("MissMailTemplate", "mailTemplate"),
                Tuple.Create("MissQualityCriteria", "qualityCriteria"),
                Tuple.Create("MissVerified", "verified"),
                Tuple.Create("MissPreferredVendor", "preferredVendor"),
                Tuple.Create("MissGuarantee", "guaranteeDetails"),
                Tuple.Create("MissAttributeGroup", "attributeGroup"),
                Tuple.Create("MissCustomTariff", "customTariff")
            };

            List<object> items = new List<object>();
            IDataReader dr = null;
            try
            {
                dr = DB.ExecuteReader(sql, new SqlParameter[]
                {
                    new SqlParameter("@AD_Client_ID", ctx.GetAD_Client_ID()),
                    new SqlParameter("@Track_Y", "Y"),
                    new SqlParameter("@Track_N", "N")
                });

                while (dr != null && dr.Read())
                {
                    bool hasBom = Util.GetValueOfInt(dr["HasBom"]) == 1;
                    List<string> missingKeys = new List<string>();
                    foreach (Tuple<string, string> flag in flagColumns)
                    {
                        string key = flag.Item2;
                        if (!tracked.Contains(key)) { continue; }
                        if (Util.GetValueOfInt(dr[flag.Item1]) != 1) { continue; }
                        // Verified is only a miss for BOM products (defensive -
                        // the SQL already enforces this).
                        if (key == "verified" && !hasBom) { continue; }
                        missingKeys.Add(key);
                    }

                    if (missingKeys.Count == 0) { continue; }

                    DateTime? updatedAt = Util.GetValueOfDateTime(dr["UpdatedAt"]);
                    items.Add(new
                    {
                        productId = Util.GetValueOfInt(dr["ProductId"]),
                        name = Util.GetValueOfString(dr["ProductName"]),
                        code = Util.GetValueOfString(dr["ProductCode"]),
                        category = Util.GetValueOfString(dr["CategoryName"]),
                        updatedAt = updatedAt.HasValue ? updatedAt.Value.ToString("yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture) : "",
                        updatedBy = Util.GetValueOfString(dr["UpdatedByName"]),
                        hasBom = hasBom,
                        missingKeys = missingKeys
                    });
                }
            }
            finally
            {
                if (dr != null) { dr.Close(); dr.Dispose(); }
            }

            return items;
        }

        /// <summary>True when the physical column exists on the active database.</summary>
        private bool HasColumn(string tableName, string columnName)
        {
            string sql;
            if (DB.IsPostgreSQL())
            {
                sql = @"
                    SELECT COUNT(1)
                    FROM information_schema.columns
                    WHERE UPPER(table_name)=UPPER(@TableName)
                      AND UPPER(column_name)=UPPER(@ColumnName)";
            }
            else
            {
                sql = @"
                    SELECT COUNT(1)
                    FROM USER_TAB_COLUMNS
                    WHERE TABLE_NAME=UPPER(@TableName)
                      AND COLUMN_NAME=UPPER(@ColumnName)";
            }

            try
            {
                return Util.GetValueOfInt(DB.ExecuteScalar(sql, new SqlParameter[]
                {
                    new SqlParameter("@TableName", tableName),
                    new SqlParameter("@ColumnName", columnName)
                }, null)) > 0;
            }
            catch
            {
                return false;
            }
        }

        /// <summary>Wraps a success payload as a serialized JSON result.</summary>
        private JsonResult Ok(object result)
        {
            return Json(JsonConvert.SerializeObject(result), JsonRequestBehavior.AllowGet);
        }

        /// <summary>Wraps a failure message as a serialized JSON result.</summary>
        private JsonResult Fail(string message)
        {
            return Json(JsonConvert.SerializeObject(new
            {
                success = false,
                error = message,
                message = message
            }), JsonRequestBehavior.AllowGet);
        }
    }
}
