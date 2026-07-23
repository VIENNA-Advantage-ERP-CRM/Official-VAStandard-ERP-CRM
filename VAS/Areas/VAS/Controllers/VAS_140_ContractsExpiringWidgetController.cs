/******************************************************
 * Module Name    : VAS
 * Purpose        : Customers module Contracts Expiring widget endpoints
 * chronological  : Development
 * Created Date   : 2026-07-22
 * Created by     : VAI052
 ******************************************************/

using Newtonsoft.Json;
using System;
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
    /// Module Name : VAS_140_ContractsExpiringWidget
    /// Purpose     : 3x1 widget - Accounts Receivable ('ASR') contracts from BOTH the
    ///               VAS Contract Master register (VAS_ContractMaster) and the core
    ///               contract register (C_Contract), combined with UNION ALL, whose
    ///               EndDate falls in the next 90 days, split into three mutually-
    ///               exclusive urgency windows (LE_30 / D31_60 / D61_90). Each window
    ///               shows the contract count, the converted contract value at stake and
    ///               a high-value count. High-value = a contract whose converted value is
    ///               >= 1.5x the average converted value across the whole combined
    ///               eligible 0-90 day population (threshold computed once, applied
    ///               everywhere). VAS eligible = IsActive='Y', VAS_Status='ARD',
    ///               VAS_IsApproved='Y', IsExpiredContracts<>'Y', VAS_Terminate<>'Y';
    ///               core eligible = IsActive='Y', Processed='Y'. Each source's contract
    ///               amount (VAS_ContractAmount / GrandTotal) is converted to the tenant
    ///               accounting currency via CurrencyConvert as-of the contract StartDate
    ///               so mixed currencies rank on one comparable base. MRole (tenant + org
    ///               + record access) is applied to each source's own physical table.
    ///
    /// Note        : Rows are NOT de-duplicated across the two contract registers (union
    ///               of both was explicitly requested). The verified VAS expired flag is
    ///               IsExpiredContracts (the spec's VAS_IsExpired does not exist here),
    ///               the VAS owner/contact is Bill_User_ID (no SalesRep_ID on that
    ///               table), and VAS RenewalType uses 'ATC'/'MNL'.
    /// Chronological development:
    ///   VAI052      2026-07-22 Created
    ///   VAI052      2026-07-23 Switched to VAS_ContractMaster, then UNION ALL with C_Contract (ASR)
    /// </summary>
    public class VAS_140_ContractsExpiringWidgetController : Controller
    {
        private static readonly VLogger Log = VLogger.GetVLogger(typeof(VAS_140_ContractsExpiringWidgetController).FullName);

        private const int WidgetPageSize = 7;
        private const int MaxListPageSize = 25;

        /// <summary>Single-row tenant accounting (display) currency.</summary>
        private const string SchemaCurrencySql = @"
            SELECT ci.AD_Client_ID AS AD_Client_ID,
                   cs.C_Currency_ID AS Acct_Currency_ID,
                   cur.StdPrecision AS Std_Precision,
                   cur.ISO_Code AS ISO_Code,
                   CASE WHEN cur.CurSymbol IS NOT NULL THEN cur.CurSymbol ELSE cur.ISO_Code END AS Cur_Symbol
            FROM AD_ClientInfo ci
            INNER JOIN C_AcctSchema cs ON (cs.C_AcctSchema_ID=ci.C_AcctSchema1_ID AND cs.IsActive = 'Y')
            INNER JOIN C_Currency cur ON (cur.C_Currency_ID=cs.C_Currency_ID AND cur.IsActive = 'Y')
            WHERE ci.IsActive = 'Y'
              AND ci.AD_Client_ID = @Client_ID";

        /// <summary>
        /// Mutually-exclusive urgency bucket by the bound boundary dates. The window
        /// boundaries (@Plus31 / @Plus61 / @Plus91) are computed in C# and bound as
        /// parameters - never derived with SQL date arithmetic, which is dialect-
        /// specific and, on PostgreSQL, fails as "timestamp + integer".
        /// </summary>
        private string BucketCase()
        {
            return @"CASE WHEN c.EndDate < @Plus31 THEN 'LE_30'
                          WHEN c.EndDate < @Plus61 THEN 'D31_60'
                          ELSE 'D61_90' END";
        }

        /// <summary>Only Accounts Receivable contracts are shown (bound, not inlined).</summary>
        private const string ContractTypeFilter = "ASR";

        /// <summary>
        /// Boundary-date parameters (today, +31, +61, +91) computed in C# so the SQL
        /// never does date arithmetic. Reused by both endpoints.
        /// </summary>
        private SqlParameter[] DateParams(DateTime today)
        {
            return new SqlParameter[]
            {
                new SqlParameter("@Today", today),
                new SqlParameter("@Plus31", today.AddDays(31)),
                new SqlParameter("@Plus61", today.AddDays(61)),
                new SqlParameter("@Plus91", today.AddDays(91))
            };
        }

        /// <summary>
        /// VAS Contract Master eligible rows: active, approved (VAS_Status='ARD',
        /// VAS_IsApproved='Y'), not expired (IsExpiredContracts&lt;&gt;'Y' - the real
        /// expired flag here, not the spec's VAS_IsExpired) and not terminated
        /// (VAS_Terminate&lt;&gt;'Y'), Accounts Receivable type, with an EndDate in
        /// [today, today+91). Value = VAS_ContractAmount converted to the tenant
        /// accounting currency as-of the contract StartDate (tenant default conversion
        /// type - the VAS table has no C_ConversionType_ID). Owner/contact = Bill_User_ID
        /// (no SalesRep_ID on this table). Alias "c" is the MRole physical table.
        /// </summary>
        private string VasEligibleSql()
        {
            string valueConv = @"CASE WHEN c.C_Currency_ID = sc.Acct_Currency_ID THEN COALESCE(c.VAS_ContractAmount, 0)
                                     ELSE CurrencyConvert(COALESCE(c.VAS_ContractAmount, 0), c.C_Currency_ID, sc.Acct_Currency_ID, c.StartDate,
                                             @ConversionType_ID, c.AD_Client_ID, c.AD_Org_ID) END";
            return @"
                SELECT 'VAS' AS Source_Type,
                       c.VAS_ContractMaster_ID AS Contract_Id,
                       c.DocumentNo AS Contract_No,
                       c.C_BPartner_ID AS Customer_Id,
                       bp.Name AS Customer_Name,
                       c.EndDate AS End_Date,
                       c.StartDate AS Start_Date,
                       c.RenewalType AS Renewal_Type,
                       c.ContractType AS Contract_Type,
                       c.VAS_Status AS Doc_Status,
                       COALESCE(sr.Name, N'') AS Sales_Rep_Name,
                       " + BucketCase() + @" AS Bucket,
                       " + valueConv + @" AS Value_Conv
                FROM VAS_ContractMaster c
                INNER JOIN C_BPartner bp ON (bp.C_BPartner_ID=c.C_BPartner_ID AND bp.AD_Client_ID=c.AD_Client_ID AND bp.IsActive = 'Y')
                INNER JOIN schema_currency sc ON (sc.AD_Client_ID=c.AD_Client_ID)
                LEFT OUTER JOIN AD_User sr ON (sr.AD_User_ID=c.Bill_User_ID AND sr.AD_Client_ID=c.AD_Client_ID AND sr.IsActive = 'Y')
                WHERE c.IsActive = 'Y'
                  AND c.VAS_Status = 'ARD'
                  AND c.ContractType = @ContractType
                  AND COALESCE(c.VAS_IsApproved, 'N') = 'Y'
                  AND COALESCE(c.IsExpiredContracts, 'N') <> 'Y'
                  AND COALESCE(c.VAS_Terminate, 'N') <> 'Y'
                  AND c.C_BPartner_ID IS NOT NULL
                  AND c.EndDate IS NOT NULL
                  AND c.AD_Client_ID = @Client_ID
                  AND c.EndDate >= @Today
                  AND c.EndDate < @Plus91";
        }

        /// <summary>
        /// Core C_Contract eligible rows: active, Processed='Y', Accounts Receivable
        /// type, with an EndDate in [today, today+91). Value = GrandTotal converted to
        /// the tenant accounting currency as-of the contract StartDate (own
        /// C_ConversionType_ID, tenant default as fallback). Owner = SalesRep_ID. Alias
        /// "c" is the MRole physical table.
        /// </summary>
        private string CoreEligibleSql()
        {
            string valueConv = @"CASE WHEN c.C_Currency_ID = sc.Acct_Currency_ID THEN COALESCE(c.GrandTotal, 0)
                                     ELSE CurrencyConvert(COALESCE(c.GrandTotal, 0), c.C_Currency_ID, sc.Acct_Currency_ID, c.StartDate,
                                             CASE WHEN COALESCE(c.C_ConversionType_ID, 0) = 0 THEN @ConversionType_ID ELSE c.C_ConversionType_ID END,
                                             c.AD_Client_ID, c.AD_Org_ID) END";
            return @"
                SELECT 'CORE' AS Source_Type,
                       c.C_Contract_ID AS Contract_Id,
                       c.DocumentNo AS Contract_No,
                       c.C_BPartner_ID AS Customer_Id,
                       bp.Name AS Customer_Name,
                       c.EndDate AS End_Date,
                       c.StartDate AS Start_Date,
                       c.RenewalType AS Renewal_Type,
                       c.ContractType AS Contract_Type,
                       c.DocStatus AS Doc_Status,
                       COALESCE(sr.Name, N'') AS Sales_Rep_Name,
                       " + BucketCase() + @" AS Bucket,
                       " + valueConv + @" AS Value_Conv
                FROM C_Contract c
                INNER JOIN C_BPartner bp ON (bp.C_BPartner_ID=c.C_BPartner_ID AND bp.AD_Client_ID=c.AD_Client_ID AND bp.IsActive = 'Y')
                INNER JOIN schema_currency sc ON (sc.AD_Client_ID=c.AD_Client_ID)
                LEFT OUTER JOIN AD_User sr ON (sr.AD_User_ID=c.SalesRep_ID AND sr.AD_Client_ID=c.AD_Client_ID AND sr.IsActive = 'Y')
                WHERE c.IsActive = 'Y'
                  AND COALESCE(c.Processed, 'N') = 'Y'
                  AND c.ContractType = @ContractType
                  AND c.C_BPartner_ID IS NOT NULL
                  AND c.EndDate IS NOT NULL
                  AND c.AD_Client_ID = @Client_ID
                  AND c.EndDate >= @Today
                  AND c.EndDate < @Plus91";
        }

        /// <summary>
        /// The combined eligible population: VAS Contract Master UNION ALL core
        /// C_Contract, with MRole (tenant + org + record access) applied to each source's
        /// own "c" alias. Both modules are shown together (per requirement); rows are not
        /// de-duplicated across the two registers. The high-value threshold is later
        /// computed once over this combined population.
        /// </summary>
        private string EligibleUnionSql(Ctx ctx)
        {
            string vas = MRole.GetDefault(ctx).AddAccessSQL(VasEligibleSql(), "c", MRole.SQL_FULLYQUALIFIED, MRole.SQL_RO);
            string core = MRole.GetDefault(ctx).AddAccessSQL(CoreEligibleSql(), "c", MRole.SQL_FULLYQUALIFIED, MRole.SQL_RO);
            return vas + @"
                UNION ALL
                " + core;
        }

        /// <summary>
        /// The three urgency windows with distinct contract counts, converted value at
        /// stake and high-value counts, plus the total high-value count across 90 days
        /// and the display currency. All three buckets are always returned.
        /// </summary>
        /// <returns>JSON { buckets:[...], highValueCount90, currency_* } or { error }.</returns>
        [AjaxAuthorizeAttribute]
        [AjaxSessionFilterAttribute]
        public JsonResult GetBuckets()
        {
            if (Session["ctx"] == null)
            {
                return Json(new { error = Msg.GetMsg(Env.GetCtx(), "SessionExpired") ?? "Session Expired" }, JsonRequestBehavior.AllowGet);
            }

            Ctx ctx = Session["ctx"] as Ctx;

            try
            {
                int conversionTypeId = MConversionType.GetDefault(ctx.GetAD_Client_ID());
                DateTime today = DateTime.Today;

                // Display currency (always available, even at zero contracts).
                string currencySymbol = "", isoCode = "";
                int stdPrecision = 2;
                IDataReader cdr = null;
                try
                {
                    cdr = DB.ExecuteReader(SchemaCurrencySql, new SqlParameter[] { new SqlParameter("@Client_ID", ctx.GetAD_Client_ID()) });
                    if (cdr != null && cdr.Read())
                    {
                        currencySymbol = Util.GetValueOfString(cdr["Cur_Symbol"]);
                        isoCode = Util.GetValueOfString(cdr["ISO_Code"]);
                        if (cdr["Std_Precision"] != null && cdr["Std_Precision"] != DBNull.Value)
                        {
                            stdPrecision = Util.GetValueOfInt(cdr["Std_Precision"]);
                        }
                    }
                }
                finally { CloseReader(cdr); }

                // VAS Contract Master UNION ALL core C_Contract (MRole per source).
                string eligibleSql = EligibleUnionSql(ctx);

                string sql = @"
                    WITH schema_currency AS (
                        " + SchemaCurrencySql + @"
                    ),
                    eligible AS (
                        " + eligibleSql + @"
                    ),
                    scored AS (
                        SELECT e.Contract_Id AS Contract_Id,
                               e.Bucket AS Bucket,
                               e.Value_Conv AS Value_Conv,
                               CASE WHEN e.Value_Conv >= 1.5 * AVG(e.Value_Conv) OVER () THEN 1 ELSE 0 END AS Is_HV
                        FROM eligible e
                        WHERE e.Value_Conv IS NOT NULL
                    )
                    SELECT s.Bucket AS Bucket,
                           COUNT(*) AS Contract_Count,
                           COALESCE(SUM(s.Value_Conv), 0) AS Value_At_Stake,
                           COALESCE(SUM(s.Is_HV), 0) AS High_Value_Count
                    FROM scored s
                    GROUP BY s.Bucket";

                // Default zero buckets in fixed order.
                var counts = new Dictionary<string, int> { { "LE_30", 0 }, { "D31_60", 0 }, { "D61_90", 0 } };
                var values = new Dictionary<string, decimal> { { "LE_30", 0 }, { "D31_60", 0 }, { "D61_90", 0 } };
                var hv = new Dictionary<string, int> { { "LE_30", 0 }, { "D31_60", 0 }, { "D61_90", 0 } };

                List<SqlParameter> bucketParams = new List<SqlParameter>
                {
                    new SqlParameter("@Client_ID", ctx.GetAD_Client_ID()),
                    new SqlParameter("@ConversionType_ID", conversionTypeId),
                    new SqlParameter("@ContractType", SqlDbType.VarChar) { Value = ContractTypeFilter }
                };
                bucketParams.AddRange(DateParams(today));

                IDataReader dr = null;
                try
                {
                    dr = DB.ExecuteReader(sql, bucketParams.ToArray());
                    while (dr != null && dr.Read())
                    {
                        string bucket = Util.GetValueOfString(dr["Bucket"]);
                        if (bucket == null || !counts.ContainsKey(bucket)) { continue; }
                        counts[bucket] = Util.GetValueOfInt(dr["Contract_Count"]);
                        values[bucket] = Math.Round(Util.GetValueOfDecimal(dr["Value_At_Stake"]), stdPrecision);
                        hv[bucket] = Util.GetValueOfInt(dr["High_Value_Count"]);
                    }
                }
                finally { CloseReader(dr); }

                string[] order = new string[] { "LE_30", "D31_60", "D61_90" };
                List<object> buckets = new List<object>();
                int highValue90 = 0;
                foreach (string key in order)
                {
                    highValue90 += hv[key];
                    buckets.Add(new
                    {
                        key = key,
                        contractCount = counts[key],
                        valueAtStake = values[key],
                        highValueCount = hv[key]
                    });
                }

                var result = new
                {
                    buckets = buckets,
                    highValueCount90 = highValue90,
                    currency_symbol = currencySymbol,
                    currency_iso = isoCode,
                    std_precision = stdPrecision
                };
                return Json(JsonConvert.SerializeObject(result), JsonRequestBehavior.AllowGet);
            }
            catch (Exception ex)
            {
                Log.Log(Level.SEVERE, "VAS_140_ContractsExpiringWidget.GetBuckets", ex);
                return Json(new { error = ex.Message }, JsonRequestBehavior.AllowGet);
            }
        }

        /// <summary>
        /// Paged contract rows for a drill-down bucket. Sorted nearest expiry first,
        /// then highest value. High-value uses the same population-wide threshold.
        /// </summary>
        /// <param name="bucket">LE_30, D31_60, D61_90, ALL_90 or HIGH_VALUE.</param>
        /// <param name="offset">Zero-based paging offset.</param>
        /// <param name="limit">Page size, up to 25.</param>
        /// <returns>JSON { items:[...], total, offset, limit, currency_* } or { error }.</returns>
        [AjaxAuthorizeAttribute]
        [AjaxSessionFilterAttribute]
        public JsonResult GetContracts(string bucket = "ALL_90", int offset = 0, int limit = WidgetPageSize)
        {
            if (Session["ctx"] == null)
            {
                return Json(new { error = Msg.GetMsg(Env.GetCtx(), "SessionExpired") ?? "Session Expired" }, JsonRequestBehavior.AllowGet);
            }

            Ctx ctx = Session["ctx"] as Ctx;

            if (offset < 0) { offset = 0; }
            if (limit <= 0 || limit > MaxListPageSize) { limit = WidgetPageSize; }

            // Validate the bucket key against the known set (parameter-bound below).
            string bucketKey = (bucket ?? "").Trim().ToUpperInvariant();
            if (bucketKey != "LE_30" && bucketKey != "D31_60" && bucketKey != "D61_90" && bucketKey != "ALL_90" && bucketKey != "HIGH_VALUE")
            {
                bucketKey = "ALL_90";
            }

            try
            {
                int conversionTypeId = MConversionType.GetDefault(ctx.GetAD_Client_ID());
                DateTime today = DateTime.Today;

                string currencySymbol = "", isoCode = "";
                int stdPrecision = 2;
                IDataReader cdr = null;
                try
                {
                    cdr = DB.ExecuteReader(SchemaCurrencySql, new SqlParameter[] { new SqlParameter("@Client_ID", ctx.GetAD_Client_ID()) });
                    if (cdr != null && cdr.Read())
                    {
                        currencySymbol = Util.GetValueOfString(cdr["Cur_Symbol"]);
                        isoCode = Util.GetValueOfString(cdr["ISO_Code"]);
                        if (cdr["Std_Precision"] != null && cdr["Std_Precision"] != DBNull.Value)
                        {
                            stdPrecision = Util.GetValueOfInt(cdr["Std_Precision"]);
                        }
                    }
                }
                finally { CloseReader(cdr); }

                // VAS Contract Master UNION ALL core C_Contract (MRole per source).
                string eligibleSql = EligibleUnionSql(ctx);

                string sql = @"
                    WITH schema_currency AS (
                        " + SchemaCurrencySql + @"
                    ),
                    eligible AS (
                        " + eligibleSql + @"
                    ),
                    scored AS (
                        SELECT e.Source_Type, e.Contract_Id, e.Contract_No, e.Customer_Id, e.Customer_Name,
                               e.End_Date, e.Start_Date, e.Renewal_Type, e.Contract_Type,
                               e.Doc_Status, e.Sales_Rep_Name, e.Bucket, e.Value_Conv,
                               CASE WHEN e.Value_Conv >= 1.5 * AVG(e.Value_Conv) OVER () THEN 1 ELSE 0 END AS Is_HV
                        FROM eligible e
                        WHERE e.Value_Conv IS NOT NULL
                    )
                    SELECT s.Source_Type,
                           s.Contract_Id,
                           s.Contract_No,
                           s.Customer_Id,
                           s.Customer_Name,
                           s.End_Date,
                           s.Start_Date,
                           s.Renewal_Type,
                           s.Contract_Type,
                           s.Doc_Status,
                           s.Sales_Rep_Name,
                           s.Value_Conv,
                           s.Is_HV,
                           COUNT(*) OVER () AS Total_Rows
                    FROM scored s
                    WHERE (@Bucket = 'ALL_90')
                       OR (@Bucket = 'HIGH_VALUE' AND s.Is_HV = 1)
                       OR (s.Bucket = @Bucket)
                    ORDER BY s.End_Date ASC,
                             s.Value_Conv DESC,
                             s.Source_Type ASC,
                             s.Contract_Id ASC
                    OFFSET " + offset + @" ROWS FETCH NEXT " + limit + @" ROWS ONLY";

                List<object> items = new List<object>();
                int total = 0;

                List<SqlParameter> rowParams = new List<SqlParameter>
                {
                    new SqlParameter("@Client_ID", ctx.GetAD_Client_ID()),
                    new SqlParameter("@ConversionType_ID", conversionTypeId),
                    new SqlParameter("@ContractType", SqlDbType.VarChar) { Value = ContractTypeFilter },
                    new SqlParameter("@Bucket", SqlDbType.VarChar) { Value = bucketKey }
                };
                rowParams.AddRange(DateParams(today));

                IDataReader dr = null;
                try
                {
                    dr = DB.ExecuteReader(sql, rowParams.ToArray());
                    while (dr != null && dr.Read())
                    {
                        total = Util.GetValueOfInt(dr["Total_Rows"]);
                        DateTime? endDate = dr["End_Date"] == DBNull.Value ? (DateTime?)null : Util.GetValueOfDateTime(dr["End_Date"]);
                        // Whole calendar days to expiry, computed in C# (no SQL date math).
                        int daysToExpiry = 0;
                        if (endDate.HasValue)
                        {
                            daysToExpiry = (int)(endDate.Value.Date - today.Date).TotalDays;
                            if (daysToExpiry < 0) { daysToExpiry = 0; }
                        }
                        DateTime? startDate = dr["Start_Date"] == DBNull.Value ? (DateTime?)null : Util.GetValueOfDateTime(dr["Start_Date"]);
                        items.Add(new
                        {
                            id = Util.GetValueOfInt(dr["Contract_Id"]),
                            contractNumber = Util.GetValueOfString(dr["Contract_No"]),
                            customerId = Util.GetValueOfInt(dr["Customer_Id"]),
                            customerName = Util.GetValueOfString(dr["Customer_Name"]),
                            endDate = endDate.HasValue ? endDate.Value.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture) : "",
                            startDate = startDate.HasValue ? startDate.Value.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture) : "",
                            daysToExpiry = daysToExpiry,
                            contractValue = Math.Round(Util.GetValueOfDecimal(dr["Value_Conv"]), stdPrecision),
                            isHighValue = Util.GetValueOfInt(dr["Is_HV"]) == 1,
                            renewalTypeCode = Util.GetValueOfString(dr["Renewal_Type"]),
                            contractTypeCode = Util.GetValueOfString(dr["Contract_Type"]),
                            salesRepName = Util.GetValueOfString(dr["Sales_Rep_Name"]),
                            docStatus = Util.GetValueOfString(dr["Doc_Status"]),
                            source = Util.GetValueOfString(dr["Source_Type"])
                        });
                    }
                }
                finally { CloseReader(dr); }

                var result = new
                {
                    items = items,
                    total = total,
                    offset = offset,
                    limit = limit,
                    currency_symbol = currencySymbol,
                    currency_iso = isoCode,
                    std_precision = stdPrecision
                };
                return Json(JsonConvert.SerializeObject(result), JsonRequestBehavior.AllowGet);
            }
            catch (Exception ex)
            {
                Log.Log(Level.SEVERE, "VAS_140_ContractsExpiringWidget.GetContracts", ex);
                return Json(new { error = ex.Message }, JsonRequestBehavior.AllowGet);
            }
        }

        private void CloseReader(IDataReader reader)
        {
            if (reader == null) { return; }
            reader.Close();
            reader.Dispose();
        }
    }
}
