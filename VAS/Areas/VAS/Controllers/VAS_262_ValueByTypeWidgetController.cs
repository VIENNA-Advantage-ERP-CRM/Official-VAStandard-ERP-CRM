/******************************************************
 * Module Name    : VAS
 * Purpose        : Service Contracts module Value by contract type breakdown widget endpoints
 * chronological  : Development
 * Created Date   : 2026-09-08
 * Created by     : VAI052
 ******************************************************/

using Newtonsoft.Json;
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Data;
using System.Data.SqlClient;
using System.Globalization;
using System.Web.Mvc;
using VAdvantage.DataBase;
using VAdvantage.Logging;
using VAdvantage.Model;
using VAdvantage.Utility;
using VIS.Filters;

namespace VAS.Controllers
{
    /// <summary>
    /// Module Name : VAS_262_ValueByTypeWidget
    /// Purpose     : Data endpoints for the c3 x r2 "Value by contract type"
    ///               breakdown widget on the Service Contracts dashboard: (1) one
    ///               ranked-bar row per C_Contract.ContractType - decoded label,
    ///               live contract count and summed live portfolio value in the
    ///               tenant's base currency, value-descending (never a zero-count
    ///               type; a type with no live contracts simply does not return a
    ///               row), (2) a paged (@7) drill list for whichever bar the user
    ///               clicks, filtered to that ContractType and value-descending,
    ///               and (3) every matching Contract_ID for a type (capped) so
    ///               "Open in browser" can filter the Service Contract window
    ///               down to exactly that type's population via a TabWhereClause
    ///               IN-list. Live = Processed='Y' AND IsCancel='N' AND
    ///               EndDate&gt;=CURRENT_DATE - not DocStatus=MContract.
    ///               DOCSTATUS_Completed (the Service Contract window itself does
    ///               not rely on that column, verified against real data, the
    ///               same fix already applied to VAS_243/244/245/246/258/259/
    ///               260/261 - applied here proactively on first build rather
    ///               than waiting for a follow-up correction). ContractType is
    ///               decoded through the tenant's own AD_Ref_List/AD_Ref_List_Trl
    ///               dictionary (never a hard-coded 101/102/103/104 -&gt; label
    ///               map), keyed by C_Contract.ContractType's own AD_Column_ID;
    ///               the drill list's RenewalType is decoded the same way. MRole
    ///               is applied to the single physical table alias "co" only. A
    ///               drill row's detail reuses the already-built
    ///               VAS_241_ContractSearchWidget/GetContract, RunRenew and
    ///               RunGenerateInvoice endpoints rather than duplicating that
    ///               logic - the same shared-endpoint reuse VAS_120's Log-
    ///               activity quick action already established against VAS_126,
    ///               and VAS_244/245/246/258/259/260/261 already reused too. The
    ///               breakdown itself is read-only; no writes.
    /// Chronological development:
    ///   VAI052      2026-09-08 Created
    /// </summary>
    public class VAS_262_ValueByTypeWidgetController : Controller
    {
        private static readonly VLogger Log = VLogger.GetVLogger(typeof(VAS_262_ValueByTypeWidgetController).FullName);

        private const int DrillPageSize = 7;

        // Upper bound for the "Open in browser" TabWhereClause IN-list (GetContractIdsData) -
        // one ContractType's live population is naturally small; this just bounds a pathological case.
        private const int MaxZoomIds = 1000;

        /// <summary>
        /// Ranked breakdown: one row per ContractType present in the live set,
        /// decoded label, count and base-currency value, value-descending.
        /// </summary>
        /// <returns>JSON { Types:[{ TypeCode, TypeLabel, ContractCount, TypeValueBase }], TotalValueBase, CurrencyIso, CurrencySymbol, CurrencyPrecision } or { Error }.</returns>
        [AjaxAuthorizeAttribute]
        [AjaxSessionFilterAttribute]
        public JsonResult GetValueByTypeBreakdown()
        {
            if (Session["ctx"] == null) { return Json("", JsonRequestBehavior.AllowGet); }

            Ctx ctx = Session["ctx"] as Ctx;

            try
            {
                string json = JsonConvert.SerializeObject(GetBreakdownData(ctx));
                return Json(json, JsonRequestBehavior.AllowGet);
            }
            catch (Exception ex)
            {
                Log.Log(Level.SEVERE, "VAS_262_ValueByTypeWidget.GetValueByTypeBreakdown", ex);
                return ErrorResult(ctx);
            }
        }

        /// <summary>
        /// Paged drill rows for one clicked ContractType, value-descending
        /// (value-by-type.queries.md D-3).
        /// </summary>
        /// <param name="typeCode">The clicked bar's C_Contract.ContractType code.</param>
        /// <param name="offset">Zero-based paging offset.</param>
        /// <param name="limit">Page size (capped at <see cref="MaxZoomIds"/>).</param>
        /// <returns>JSON { Rows:[...], Total } or { Error }.</returns>
        [AjaxAuthorizeAttribute]
        [AjaxSessionFilterAttribute]
        public JsonResult GetValueByTypeDrill(string typeCode, int offset = 0, int limit = DrillPageSize)
        {
            if (Session["ctx"] == null) { return Json("", JsonRequestBehavior.AllowGet); }

            Ctx ctx = Session["ctx"] as Ctx;

            if (string.IsNullOrEmpty(typeCode)) { return ErrorResult(ctx); }
            if (offset < 0) { offset = 0; }
            if (limit <= 0 || limit > MaxZoomIds) { limit = DrillPageSize; }

            try
            {
                string json = JsonConvert.SerializeObject(GetDrillData(ctx, typeCode, offset, limit));
                return Json(json, JsonRequestBehavior.AllowGet);
            }
            catch (Exception ex)
            {
                Log.Log(Level.SEVERE, "VAS_262_ValueByTypeWidget.GetValueByTypeDrill", ex);
                return ErrorResult(ctx);
            }
        }

        /// <summary>
        /// Every matching C_Contract_ID for one ContractType (not just one page),
        /// capped at <see cref="MaxZoomIds"/> - so "Open in browser" can filter
        /// the Service Contract window down to exactly that type's population via
        /// a TabWhereClause IN-list.
        /// </summary>
        /// <param name="typeCode">The clicked bar's C_Contract.ContractType code.</param>
        /// <returns>JSON { Ids:[...] } or { Error }.</returns>
        [AjaxAuthorizeAttribute]
        [AjaxSessionFilterAttribute]
        public JsonResult GetValueByTypeIds(string typeCode)
        {
            if (Session["ctx"] == null) { return Json("", JsonRequestBehavior.AllowGet); }

            Ctx ctx = Session["ctx"] as Ctx;

            if (string.IsNullOrEmpty(typeCode)) { return ErrorResult(ctx); }

            try
            {
                string json = JsonConvert.SerializeObject(new { Ids = GetContractIdsData(ctx, typeCode) });
                return Json(json, JsonRequestBehavior.AllowGet);
            }
            catch (Exception ex)
            {
                Log.Log(Level.SEVERE, "VAS_262_ValueByTypeWidget.GetValueByTypeIds", ex);
                return ErrorResult(ctx);
            }
        }

        /// <summary>
        /// The shared live predicate, WHERE-only - every caller applies MRole to
        /// alias "co" itself right after calling this. "Live" is Processed='Y'
        /// (not DocStatus=MContract.DOCSTATUS_Completed - the Service Contract
        /// window itself does not rely on that column, verified against real
        /// data, the same fix already applied to VAS_243/244/245/246/258/259/
        /// 260/261) AND IsCancel = 'N' AND EndDate &gt;= CURRENT_DATE.
        /// </summary>
        /// <returns>WHERE-clause text (binds @AD_Client_ID).</returns>
        private static string LiveWhereSql()
        {
            return @"
                   co.AD_Client_ID = @AD_Client_ID
               AND co.IsActive = 'Y'
               AND co.Processed = 'Y'
               AND co.IsCancel = 'N'
               AND co.EndDate >= CURRENT_DATE";
        }

        /// <summary>
        /// Resolves the ranked ContractType breakdown - the base SQL is WHERE-only
        /// until MRole is applied to alias "co", then GROUP BY / ORDER BY are
        /// appended (value-by-type.queries.md D-1). Widget total is summed in C#
        /// from the returned rows, never a separate query.
        /// </summary>
        /// <param name="ctx">Session context.</param>
        private ValueByTypeBreakdown GetBreakdownData(Ctx ctx)
        {
            ValueByTypeBreakdown result = new ValueByTypeBreakdown { Types = new List<ValueByTypeRow>() };
            if (ctx == null) { return result; }

            int baseCurrencyId; string baseIso, baseSymbol; int basePrecision;
            ResolveBaseCurrency(ctx, out baseCurrencyId, out baseIso, out baseSymbol, out basePrecision);
            result.CurrencyIso = baseIso;
            result.CurrencySymbol = baseSymbol;
            result.CurrencyPrecision = basePrecision;

            int contractTypeColumnId = GetColumnId("C_Contract", "ContractType");
            string language = GetLanguage(ctx);

            string baseSql = @"
                SELECT co.ContractType AS Type_Code,
                       COALESCE(rlt.Name, rl.Name, rl.Value) AS Type_Label,
                       COUNT(co.C_Contract_ID) AS Contract_Count,
                       SUM( CurrencyConvert(co.GrandTotal, co.C_Currency_ID, @BaseCurrency_ID,
                              CURRENT_DATE, co.C_ConversionType_ID, co.AD_Client_ID, co.AD_Org_ID) ) AS Type_Value_Base
                  FROM C_Contract co
                  INNER JOIN AD_Column col ON ( col.AD_Column_ID = @AD_Column_ID_ContractType )
                  LEFT OUTER JOIN AD_Ref_List rl ON ( rl.AD_Reference_ID = col.AD_Reference_Value_ID
                                                  AND rl.Value = co.ContractType )
                  LEFT OUTER JOIN AD_Ref_List_Trl rlt ON ( rlt.AD_Ref_List_ID = rl.AD_Ref_List_ID
                                                       AND rlt.AD_Language = @AD_Language )
                 WHERE " + LiveWhereSql();

            baseSql = MRole.GetDefault(ctx).AddAccessSQL(baseSql, "co", MRole.SQL_FULLYQUALIFIED, MRole.SQL_RO);

            string sql = baseSql + @"
                GROUP BY co.ContractType, COALESCE(rlt.Name, rl.Name, rl.Value)
                ORDER BY Type_Value_Base DESC";

            IDataReader dr = null;
            try
            {
                dr = DB.ExecuteReader(sql, new[]
                {
                    new SqlParameter("@AD_Client_ID", ctx.GetAD_Client_ID()),
                    new SqlParameter("@BaseCurrency_ID", baseCurrencyId),
                    new SqlParameter("@AD_Column_ID_ContractType", contractTypeColumnId),
                    new SqlParameter("@AD_Language", SqlDbType.NVarChar) { Value = language }
                });

                while (dr != null && dr.Read())
                {
                    decimal typeValueBase = dr["Type_Value_Base"] == DBNull.Value ? 0m : Util.GetValueOfDecimal(dr["Type_Value_Base"]);
                    result.Types.Add(new ValueByTypeRow
                    {
                        TypeCode = Util.GetValueOfString(dr["Type_Code"]),
                        TypeLabel = Util.GetValueOfString(dr["Type_Label"]),
                        ContractCount = Util.GetValueOfInt(dr["Contract_Count"]),
                        TypeValueBase = typeValueBase
                    });
                    result.TotalValueBase += typeValueBase;
                }
            }
            finally
            {
                CloseReader(dr);
            }

            return result;
        }

        /// <summary>
        /// Paged drill rows for one ContractType, value-descending (value-by-
        /// type.queries.md D-3) - the base SQL is WHERE-only until MRole is
        /// applied to alias "co", then ORDER BY / OFFSET..FETCH are appended.
        /// </summary>
        /// <param name="ctx">Session context.</param>
        /// <param name="typeCode">The clicked bar's ContractType code.</param>
        /// <param name="offset">Zero-based paging offset.</param>
        /// <param name="limit">Page size.</param>
        private ValueByTypeDrillResult GetDrillData(Ctx ctx, string typeCode, int offset, int limit)
        {
            ValueByTypeDrillResult result = new ValueByTypeDrillResult { Rows = new List<ValueByTypeDrillRow>() };
            if (ctx == null) { return result; }

            string language = GetLanguage(ctx);
            Dictionary<string, string> renewalTypeMap = GetDecodeMap("C_Contract", "RenewalType", language);

            int baseCurrencyId; string baseIso, baseSymbol; int basePrecision;
            ResolveBaseCurrency(ctx, out baseCurrencyId, out baseIso, out baseSymbol, out basePrecision);

            string baseSql = @"
                SELECT co.C_Contract_ID      AS Contract_Id,
                       co.DocumentNo         AS Document_No,
                       COALESCE(bp.Name, N'') AS Customer_Name,
                       COALESCE(pr.Name, N'') AS Product_Name,
                       co.RenewalType        AS Renewal_Type_Code,
                       co.EndDate            AS End_Date,
                       CAST(DAYSBETWEEN(co.EndDate, CURRENT_DATE) AS INTEGER) AS Days_To_End,
                       CurrencyConvert(co.GrandTotal, co.C_Currency_ID, @BaseCurrency_ID,
                              CURRENT_DATE, co.C_ConversionType_ID, co.AD_Client_ID, co.AD_Org_ID) AS Value_Base
                  FROM C_Contract co
                  LEFT OUTER JOIN C_BPartner bp ON ( bp.C_BPartner_ID = co.C_BPartner_ID )
                  LEFT OUTER JOIN M_Product  pr ON ( pr.M_Product_ID  = co.M_Product_ID )
                 WHERE " + LiveWhereSql() + @"
                   AND co.ContractType = @ContractType";

            baseSql = MRole.GetDefault(ctx).AddAccessSQL(baseSql, "co", MRole.SQL_FULLYQUALIFIED, MRole.SQL_RO);

            string listSql = baseSql + @"
                ORDER BY Value_Base DESC
                OFFSET @Off ROWS FETCH NEXT @Lim ROWS ONLY";

            string countSql = "SELECT COUNT(1) AS Total_Count FROM ( " + baseSql + " ) cnt_wrap";

            IDataReader dr = null;
            try
            {
                List<SqlParameter> parameters = new List<SqlParameter>
                {
                    new SqlParameter("@AD_Client_ID", ctx.GetAD_Client_ID()),
                    new SqlParameter("@BaseCurrency_ID", baseCurrencyId),
                    new SqlParameter("@ContractType", SqlDbType.NVarChar) { Value = typeCode },
                    new SqlParameter("@Off", offset),
                    new SqlParameter("@Lim", limit)
                };

                dr = DB.ExecuteReader(listSql, parameters.ToArray());
                while (dr != null && dr.Read())
                {
                    string renewalTypeCode = Util.GetValueOfString(dr["Renewal_Type_Code"]);
                    DateTime? endDate = dr["End_Date"] == DBNull.Value ? (DateTime?)null : Util.GetValueOfDateTime(dr["End_Date"]);

                    result.Rows.Add(new ValueByTypeDrillRow
                    {
                        ContractId = Util.GetValueOfInt(dr["Contract_Id"]),
                        DocumentNo = Util.GetValueOfString(dr["Document_No"]),
                        CustomerName = Util.GetValueOfString(dr["Customer_Name"]),
                        ProductName = Util.GetValueOfString(dr["Product_Name"]),
                        GrandTotal = dr["Value_Base"] == DBNull.Value ? 0m : Util.GetValueOfDecimal(dr["Value_Base"]),
                        CurrencyIso = baseIso,
                        CurrencySymbol = baseSymbol,
                        CurrencyPrecision = basePrecision,
                        RenewalTypeCode = renewalTypeCode,
                        RenewalType = DecodeLabel(renewalTypeMap, renewalTypeCode),
                        EndDate = endDate.HasValue ? endDate.Value.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture) : "",
                        DaysToEnd = dr["Days_To_End"] == DBNull.Value ? 0 : Util.GetValueOfInt(dr["Days_To_End"])
                    });
                }
            }
            finally
            {
                CloseReader(dr);
            }

            IDataReader countReader = null;
            try
            {
                List<SqlParameter> countParameters = new List<SqlParameter>
                {
                    new SqlParameter("@AD_Client_ID", ctx.GetAD_Client_ID()),
                    new SqlParameter("@BaseCurrency_ID", baseCurrencyId),
                    new SqlParameter("@ContractType", SqlDbType.NVarChar) { Value = typeCode }
                };

                countReader = DB.ExecuteReader(countSql, countParameters.ToArray());
                if (countReader != null && countReader.Read())
                {
                    result.Total = Util.GetValueOfInt(countReader["Total_Count"]);
                }
            }
            finally
            {
                CloseReader(countReader);
            }

            return result;
        }

        /// <summary>
        /// Every C_Contract_ID matching one ContractType, value-descending, capped
        /// at <see cref="MaxZoomIds"/>. Backs "Open in browser"'s TabWhereClause
        /// IN-list - see <see cref="GetValueByTypeIds"/>.
        /// </summary>
        /// <param name="ctx">Session context.</param>
        /// <param name="typeCode">The clicked bar's ContractType code.</param>
        private List<int> GetContractIdsData(Ctx ctx, string typeCode)
        {
            List<int> ids = new List<int>();
            if (ctx == null) { return ids; }

            int baseCurrencyId; string baseIso, baseSymbol; int basePrecision;
            ResolveBaseCurrency(ctx, out baseCurrencyId, out baseIso, out baseSymbol, out basePrecision);

            string sql = @"
                SELECT co.C_Contract_ID AS Contract_Id
                  FROM C_Contract co
                 WHERE " + LiveWhereSql() + @"
                   AND co.ContractType = @ContractType";

            sql = MRole.GetDefault(ctx).AddAccessSQL(sql, "co", MRole.SQL_FULLYQUALIFIED, MRole.SQL_RO);

            sql = sql + @"
                 ORDER BY CurrencyConvert(co.GrandTotal, co.C_Currency_ID, @BaseCurrency_ID,
                              CURRENT_DATE, co.C_ConversionType_ID, co.AD_Client_ID, co.AD_Org_ID) DESC
                 OFFSET 0 ROWS FETCH NEXT @MaxIds ROWS ONLY";

            IDataReader dr = null;
            try
            {
                dr = DB.ExecuteReader(sql, new[]
                {
                    new SqlParameter("@AD_Client_ID", ctx.GetAD_Client_ID()),
                    new SqlParameter("@ContractType", SqlDbType.NVarChar) { Value = typeCode },
                    new SqlParameter("@BaseCurrency_ID", baseCurrencyId),
                    new SqlParameter("@MaxIds", MaxZoomIds)
                });
                while (dr != null && dr.Read())
                {
                    ids.Add(Util.GetValueOfInt(dr["Contract_Id"]));
                }
            }
            finally
            {
                CloseReader(dr);
            }

            return ids;
        }

        /// <summary>
        /// The tenant's accounting-schema (base) currency - AD_ClientInfo -&gt;
        /// C_AcctSchema1 -&gt; C_Currency, the same resolution the sibling Service
        /// Contracts KPIs (VAS_243/244/245/246/258/259/260/261) already use.
        /// </summary>
        /// <returns>Parameterised SELECT text (binds @AD_Client_ID).</returns>
        private static string SchemaCurrencySql()
        {
            return @"
                SELECT cs.C_Currency_ID AS Acct_Currency_ID,
                       cur.StdPrecision AS Std_Precision,
                       cur.ISO_Code     AS ISO_Code,
                       CASE WHEN cur.CurSymbol IS NOT NULL THEN cur.CurSymbol ELSE cur.ISO_Code END AS Cur_Symbol
                  FROM AD_ClientInfo ci
                  INNER JOIN C_AcctSchema cs ON ( cs.C_AcctSchema_ID = ci.C_AcctSchema1_ID AND cs.IsActive = 'Y' )
                  INNER JOIN C_Currency cur  ON ( cur.C_Currency_ID  = cs.C_Currency_ID    AND cur.IsActive = 'Y' )
                 WHERE ci.IsActive = 'Y'
                   AND ci.AD_Client_ID = @AD_Client_ID";
        }

        /// <summary>Resolves the base currency id/iso/symbol/precision for this tenant, or a zeroed default when the schema currency cannot be resolved.</summary>
        /// <param name="ctx">Session context.</param>
        /// <param name="baseCurrencyId">Resolved C_Currency_ID of the tenant's accounting schema.</param>
        /// <param name="iso">Resolved ISO code.</param>
        /// <param name="symbol">Resolved display symbol (CurSymbol, falling back to ISO code).</param>
        /// <param name="precision">Resolved standard display precision.</param>
        private void ResolveBaseCurrency(Ctx ctx, out int baseCurrencyId, out string iso, out string symbol, out int precision)
        {
            baseCurrencyId = 0;
            iso = "";
            symbol = "";
            precision = 2;

            IDataReader dr = null;
            try
            {
                dr = DB.ExecuteReader(SchemaCurrencySql(), new[] { new SqlParameter("@AD_Client_ID", ctx.GetAD_Client_ID()) });
                if (dr != null && dr.Read())
                {
                    baseCurrencyId = Util.GetValueOfInt(dr["Acct_Currency_ID"]);
                    iso = Util.GetValueOfString(dr["ISO_Code"]);
                    symbol = Util.GetValueOfString(dr["Cur_Symbol"]);
                    precision = dr["Std_Precision"] == DBNull.Value ? 2 : Util.GetValueOfInt(dr["Std_Precision"]);
                }
            }
            finally
            {
                CloseReader(dr);
            }
        }

        /// <summary>The tenant's own label for a stored code, or the code itself when the dictionary carries no entry (never invented English).</summary>
        /// <param name="map">Code -&gt; label dictionary from <see cref="GetDecodeMap"/>.</param>
        /// <param name="code">Stored List code.</param>
        private static string DecodeLabel(Dictionary<string, string> map, string code)
        {
            if (string.IsNullOrEmpty(code)) { return ""; }
            string label;
            return map != null && map.TryGetValue(code, out label) ? label : code;
        }

        private static readonly ConcurrentDictionary<string, Dictionary<string, string>> DecodeMapCache =
            new ConcurrentDictionary<string, Dictionary<string, string>>(StringComparer.OrdinalIgnoreCase);

        /// <summary>AD_Ref_List(_Trl) Code -&gt; Label map for one column, cached per column+language (value-by-type.queries.md D-2). Never a hard-coded code -&gt; label map.</summary>
        /// <param name="tableName">Owning AD_Table.TableName.</param>
        /// <param name="columnName">Owning AD_Column.ColumnName.</param>
        /// <param name="language">AD_Language for the translated label.</param>
        private Dictionary<string, string> GetDecodeMap(string tableName, string columnName, string language)
        {
            string cacheKey = tableName + "." + columnName + "|" + language;

            Dictionary<string, string> cached;
            if (DecodeMapCache.TryGetValue(cacheKey, out cached)) { return cached; }

            Dictionary<string, string> map = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            int columnId = GetColumnId(tableName, columnName);

            if (columnId > 0)
            {
                string sql = @"
                    SELECT rl.Value AS Code, COALESCE(rlt.Name, rl.Name, rl.Value) AS Label
                      FROM AD_Ref_List rl
                      INNER JOIN AD_Column col ON ( col.AD_Reference_Value_ID = rl.AD_Reference_ID )
                      LEFT OUTER JOIN AD_Ref_List_Trl rlt ON ( rlt.AD_Ref_List_ID = rl.AD_Ref_List_ID AND rlt.AD_Language = @AD_Language )
                     WHERE col.AD_Column_ID = @AD_Column_ID
                     ORDER BY rl.Value";

                IDataReader dr = null;
                try
                {
                    dr = DB.ExecuteReader(sql, new[]
                    {
                        new SqlParameter("@AD_Column_ID", columnId),
                        new SqlParameter("@AD_Language", SqlDbType.NVarChar) { Value = language }
                    });
                    while (dr != null && dr.Read())
                    {
                        string code = Util.GetValueOfString(dr["Code"]);
                        if (!string.IsNullOrEmpty(code))
                        {
                            map[code] = Util.GetValueOfString(dr["Label"]);
                        }
                    }
                }
                finally
                {
                    CloseReader(dr);
                }
            }

            DecodeMapCache[cacheKey] = map;
            return map;
        }

        private static readonly ConcurrentDictionary<string, int> ColumnIdCache = new ConcurrentDictionary<string, int>(StringComparer.OrdinalIgnoreCase);

        /// <summary>Resolves and caches an AD_Column_ID by table/column name.</summary>
        /// <param name="tableName">AD_Table.TableName.</param>
        /// <param name="columnName">AD_Column.ColumnName.</param>
        private static int GetColumnId(string tableName, string columnName)
        {
            string cacheKey = tableName + "." + columnName;

            int cached;
            if (ColumnIdCache.TryGetValue(cacheKey, out cached)) { return cached; }

            int columnId = Util.GetValueOfInt(DB.ExecuteScalar(
                "SELECT AD_Column_ID FROM AD_Column WHERE ColumnName = @Col AND AD_Table_ID = (SELECT AD_Table_ID FROM AD_Table WHERE TableName = @Tbl)",
                new[]
                {
                    new SqlParameter("@Col", SqlDbType.NVarChar) { Value = columnName },
                    new SqlParameter("@Tbl", SqlDbType.NVarChar) { Value = tableName }
                },
                null));

            ColumnIdCache[cacheKey] = columnId;
            return columnId;
        }

        /// <summary>Resolves the session's AD_Language, defaulting to en_US when unset.</summary>
        /// <param name="ctx">Session context.</param>
        private string GetLanguage(Ctx ctx)
        {
            string language = ctx == null ? string.Empty : ctx.GetAD_Language();
            return string.IsNullOrWhiteSpace(language) ? "en_US" : language;
        }

        /// <summary>Closes and disposes a DataReader if open - every reader opened by this controller passes through here so no DB connection leaks.</summary>
        /// <param name="reader">Reader to close, or null.</param>
        private void CloseReader(IDataReader reader)
        {
            if (reader == null) { return; }
            reader.Close();
            reader.Dispose();
        }

        /// <summary>Builds the tenant-localised generic error JSON payload for a failed endpoint call.</summary>
        /// <param name="ctx">Session context (for message localisation).</param>
        private JsonResult ErrorResult(Ctx ctx)
        {
            string message = Msg.GetMsg(ctx, "Error") ?? "Error";
            string json = JsonConvert.SerializeObject(new { Error = message });
            return Json(json, JsonRequestBehavior.AllowGet);
        }

        private class ValueByTypeBreakdown
        {
            public List<ValueByTypeRow> Types { get; set; }
            public decimal TotalValueBase { get; set; }
            public string CurrencyIso { get; set; }
            public string CurrencySymbol { get; set; }
            public int CurrencyPrecision { get; set; } = 2;
        }

        private class ValueByTypeRow
        {
            public string TypeCode { get; set; }
            public string TypeLabel { get; set; }
            public int ContractCount { get; set; }
            public decimal TypeValueBase { get; set; }
        }

        private class ValueByTypeDrillResult
        {
            public List<ValueByTypeDrillRow> Rows { get; set; }
            public int Total { get; set; }
        }

        private class ValueByTypeDrillRow
        {
            public int ContractId { get; set; }
            public string DocumentNo { get; set; }
            public string CustomerName { get; set; }
            public string ProductName { get; set; }
            public decimal GrandTotal { get; set; }
            public string CurrencyIso { get; set; }
            public string CurrencySymbol { get; set; }
            public int CurrencyPrecision { get; set; }
            public string RenewalTypeCode { get; set; }
            public string RenewalType { get; set; }
            public string EndDate { get; set; }
            public int DaysToEnd { get; set; }
        }
    }
}
