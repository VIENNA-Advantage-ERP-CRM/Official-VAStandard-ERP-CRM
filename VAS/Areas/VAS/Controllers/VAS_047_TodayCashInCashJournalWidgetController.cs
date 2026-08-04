using System;
using System.Data;
using System.Data.SqlClient;
using System.Globalization;
using System.Web.Mvc;
using VAdvantage.DataBase;
using VAdvantage.Logging;
using VAdvantage.Model;
using VAdvantage.Utility;

namespace VAS.Controllers
{
    /// <summary>
    /// Module Name : Cash Journal Dashboard Widget
    /// Purpose     : Loads today's cash-in amount from Cash Journal.
    /// Chronological development:
    ///   VAS   Created 2026-06-06
    /// </summary>
    public class VAS_047_TodayCashInCashJournalWidgetController : Controller
    {
        /// <summary>
        /// Gets today's cash-in amount from completed or closed cash journals.
        /// </summary>
        /// <returns>JSON result containing widget KPI data.</returns>
        public JsonResult GetTodayCashInData()
        {
            Ctx ctx = Session["ctx"] as Ctx;

            if (ctx == null)
            {
                return Json(
                    new
                    {
                        success = false,
                        error = "VAS_047_SessionExpired",
                        hasData = false
                    },
                    JsonRequestBehavior.AllowGet
                );
            }

            IDataReader dr = null;

            try
            {
                SqlQueryData queryData =
                    BuildTodayCashInSql(ctx);

                dr = DB.ExecuteReader(
                    queryData.Sql,
                    queryData.Parameters,
                    null
                );

                decimal mainMetric = 0;
                decimal avgDailyAmount = 0;
                int recordCount = 0;
                int currencyId = 0;
                string currencyISO = "";
                string currencySymbol = "";
                int stdPrecision = 2;
                DateTime? dateFrom = null;
                DateTime? dateTo = null;

                if (
                    dr != null &&
                    dr.Read()
                )
                {
                    stdPrecision =
                        Util.GetValueOfInt(
                            dr["StdPrecision"]
                        );

                    mainMetric =
                        GetRoundedDecimal(
                            dr,
                            "MainMetric",
                            stdPrecision
                        );

                    avgDailyAmount =
                        GetRoundedDecimal(
                            dr,
                            "AvgDailyAmount",
                            stdPrecision
                        );

                    recordCount =
                        Util.GetValueOfInt(
                            dr["RecordCount"]
                        );

                    currencyId =
                        Util.GetValueOfInt(
                            dr["C_Currency_ID"]
                        );

                    currencyISO =
                        Util.GetValueOfString(
                            dr["CurrencyISO"]
                        );

                    currencySymbol =
                        Util.GetValueOfString(
                            dr["CurrencySymbol"]
                        );

                    if (
                        dr["DateFrom"] !=
                        DBNull.Value
                    )
                    {
                        dateFrom =
                            Util.GetValueOfDateTime(
                                dr["DateFrom"]
                            );
                    }

                    if (
                        dr["DateTo"] !=
                        DBNull.Value
                    )
                    {
                        dateTo =
                            Util.GetValueOfDateTime(
                                dr["DateTo"]
                            );
                    }
                }

                mainMetric =
                    Math.Round(
                        mainMetric,
                        stdPrecision
                    );

                avgDailyAmount =
                    Math.Round(
                        avgDailyAmount,
                        stdPrecision
                    );

                int deltaPercent = 0;

                if (avgDailyAmount > 0)
                {
                    deltaPercent =
                        Convert.ToInt32(
                            Math.Round(
                                (
                                    (
                                        mainMetric -
                                        avgDailyAmount
                                    )
                                    /
                                    avgDailyAmount
                                )
                                *
                                100,
                                0,
                                MidpointRounding.AwayFromZero
                            )
                        );
                }

                return Json(
                    new
                    {
                        success = true,
                        error = "",

                        title = GetMsg(
                            ctx,
                            "VAS_047_CashInTitle",
                            "Cash in"
                        ),

                        mainMetric =
                            mainMetric,

                        mainMetricText =
                            mainMetric.ToString(
                                "F" + stdPrecision
                            ),

                        avgDailyAmount =
                            avgDailyAmount,

                        deltaPercent =
                            deltaPercent,

                        description = GetMsg(
                            ctx,
                            "VAS_047_Description",
                            "Total cash received in cash journal today"
                        ),

                        badgeText = GetMsg(
                            ctx,
                            "VAS_047_PeriodToday",
                            "Today"
                        ),

                        noDataText = GetMsg(
                            ctx,
                            "VAS_047_NoData",
                            "No data available"
                        ),

                        dateFrom =
                            dateFrom.HasValue
                                ? FormatDate(
                                    dateFrom.Value
                                )
                                : "",

                        dateTo =
                            dateTo.HasValue
                                ? FormatDate(
                                    dateTo.Value
                                )
                                : "",

                        cCurrencyId =
                            currencyId,

                        currencyISO =
                            currencyISO,

                        currencyISOCode =
                            currencyISO,

                        currencySymbol =
                            currencySymbol,

                        stdPrecision =
                            stdPrecision,

                        recordCount =
                            recordCount,

                        hasData =
                            recordCount > 0
                    },
                    JsonRequestBehavior.AllowGet
                );
            }
            catch
            {
                return Json(
                    new
                    {
                        success = false,

                        error = GetMsg(
                            ctx,
                            "VAS_047_LoadError",
                            "Could not load data"
                        ),

                        hasData = false
                    },
                    JsonRequestBehavior.AllowGet
                );
            }
            finally
            {
                if (dr != null)
                {
                    dr.Close();
                    dr.Dispose();
                }
            }
        }

        /// <summary>
        /// Gets one page of today's positive cash journal lines for drill-down.
        /// </summary>
        public JsonResult GetTodayCashInRows(
            int pageNo = 1,
            int pageSize = 6
        )
        {
            Ctx ctx = Session["ctx"] as Ctx;

            if (ctx == null)
            {
                return Json(
                    new
                    {
                        success = false,
                        error = "VAS_047_SessionExpired",
                        hasData = false
                    },
                    JsonRequestBehavior.AllowGet
                );
            }

            pageNo = Math.Max(pageNo, 1);
            pageSize = Math.Max(1, Math.Min(pageSize, 50));

            IDataReader reader = null;

            try
            {
                SqlQueryData queryData =
                    BuildTodayCashInRowsSql(
                        ctx,
                        pageNo,
                        pageSize
                    );

                reader = DB.ExecuteReader(
                    queryData.Sql,
                    queryData.Parameters,
                    null
                );

                var rows =
                    new System.Collections.Generic.List<object>();

                int totalRecords = 0;
                int stdPrecision = 2;
                decimal totalAmount = 0;

                while (
                    reader != null &&
                    reader.Read()
                )
                {
                    totalRecords =
                        Util.GetValueOfInt(
                            reader["TotalRecords"]
                        );

                    stdPrecision =
                        Util.GetValueOfInt(
                            reader["StdPrecision"]
                        );

                    if (stdPrecision < 0)
                    {
                        stdPrecision = 2;
                    }

                    totalAmount =
                        GetRoundedDecimal(
                            reader,
                            "TotalAmount",
                            stdPrecision
                        );

                    int cashLineId =
                        Util.GetValueOfInt(
                            reader["C_CashLine_ID"]
                        );

                    if (cashLineId <= 0)
                    {
                        continue;
                    }

                    string cashTypeValue =
                        Util.GetValueOfString(
                            reader["CashTypeValue"]
                        );

                    string cashTypeName =
                        FirstNotEmpty(
                            Util.GetValueOfString(
                                reader["CashTypeTranslatedName"]
                            ),
                            Util.GetValueOfString(
                                reader["CashTypeBaseName"]
                            ),
                            cashTypeValue
                        );

                    DateTime? statementDate = null;

                    if (
                        reader["StatementDate"] !=
                        DBNull.Value
                    )
                    {
                        statementDate =
                            Util.GetValueOfDateTime(
                                reader["StatementDate"]
                            );
                    }

                    rows.Add(
                        new
                        {
                            cCashLineId = cashLineId,
                            cCashId =
                                Util.GetValueOfInt(
                                    reader["C_Cash_ID"]
                                ),
                            documentNo =
                                Util.GetValueOfString(
                                    reader["DocumentNo"]
                                ),
                            statementDate =
                                statementDate.HasValue
                                    ? FormatDate(
                                        statementDate.Value
                                    )
                                    : string.Empty,
                            description =
                                Util.GetValueOfString(
                                    reader["Description"]
                                ),
                            cashTypeValue = cashTypeValue,
                            cashTypeName = cashTypeName,
                            chargeName =
                                Util.GetValueOfString(
                                    reader["ChargeName"]
                                ),
                            cashBookName =
                                Util.GetValueOfString(
                                    reader["CashBookName"]
                                ),
                            amount =
                                GetRoundedDecimal(
                                    reader,
                                    "ConvertedAmount",
                                    stdPrecision
                                ),
                            stdPrecision = stdPrecision
                        }
                    );
                }

                int totalPages =
                    Convert.ToInt32(
                        Math.Ceiling(
                            (decimal)totalRecords /
                            pageSize
                        )
                    );

                return Json(
                    new
                    {
                        success = true,
                        error = string.Empty,
                        title = GetMsg(
                            ctx,
                            "VAS_047_DialogTitle",
                            "Today's Cash Receipts"
                        ),
                        subtitle = GetMsg(
                            ctx,
                            "VAS_047_DialogSubtitle",
                            "Completed cash journal receipts recorded today"
                        ),
                        rows = rows,
                        pageNo = pageNo,
                        pageSize = pageSize,
                        totalRecords = totalRecords,
                        totalPages = totalPages,
                        totalAmount = totalAmount,
                        stdPrecision = stdPrecision,
                        hasData = totalRecords > 0
                    },
                    JsonRequestBehavior.AllowGet
                );
            }
            catch (Exception ex)
            {
                VLogger.Get().SaveError(
                    "GetTodayCashInRows",
                    ex
                );

                return Json(
                    new
                    {
                        success = false,
                        error = GetMsg(
                            ctx,
                            "VAS_047_LoadError",
                            "Could not load data"
                        ),
                        hasData = false
                    },
                    JsonRequestBehavior.AllowGet
                );
            }
            finally
            {
                if (reader != null)
                {
                    reader.Close();
                    reader.Dispose();
                }
            }
        }

        private SqlQueryData BuildTodayCashInSql(
            Ctx ctx
        )
        {
            string todayDateSql =
                GetTodayDateSql();

            string cashDateSql =
                GetDateOnlySql(
                    "CashData.StatementDate"
                );

            string queryParametersFrom =
                DB.IsOracle()
                    ? " FROM DUAL"
                    : string.Empty;

            string numericType =
                DB.IsOracle()
                    ? "NUMBER"
                    : "NUMERIC";

            string textType =
                DB.IsOracle()
                    ? "VARCHAR2(255)"
                    : "VARCHAR(255)";

            string queryParametersSql = @"
QueryParameters AS
(
    SELECT
        @AD_Client_ID AS AD_Client_ID"
        + queryParametersFrom + @"
)";

            string dateRangeSql = @"
DateRange AS
(
    SELECT
        " + todayDateSql + @" AS TodayDate,

        CAST
        (
            " + todayDateSql + @"
            AS TIMESTAMP
        ) AS TodayStart,

        CAST
        (
            " + todayDateSql + @" + 1
            AS TIMESTAMP
        ) AS TodayEnd,

        CAST
        (
            " + todayDateSql + @" - 7
            AS TIMESTAMP
        ) AS AverageStart,

        CAST
        (
            " + todayDateSql + @"
            AS TIMESTAMP
        ) AS AverageEnd

    FROM QueryParameters QueryParameters
)";

            string schemaCurrencySql = @"
SchemaCurrency AS
(
    SELECT
        ClientInfo.AD_Client_ID,

        AcctSchema.C_Currency_ID
            AS C_Currency_ID,

        Currency.StdPrecision,

        TRIM
        (
            CAST
            (
                Currency.ISO_Code
                AS " + textType + @"
            )
        ) AS ISO_Code,

        CASE
            WHEN Currency.CurSymbol IS NOT NULL
            THEN
                TRIM
                (
                    CAST
                    (
                        Currency.CurSymbol
                        AS " + textType + @"
                    )
                )
            ELSE
                TRIM
                (
                    CAST
                    (
                        Currency.ISO_Code
                        AS " + textType + @"
                    )
                )
        END AS Cur_Symbol
    FROM AD_ClientInfo ClientInfo
    INNER JOIN C_AcctSchema AcctSchema ON
    (
        ClientInfo.C_AcctSchema1_ID =
        AcctSchema.C_AcctSchema_ID
    )
    INNER JOIN C_Currency Currency ON
    (
        AcctSchema.C_Currency_ID =
        Currency.C_Currency_ID
    )
    WHERE ClientInfo.IsActive = 'Y'
    AND AcctSchema.IsActive = 'Y'
    AND Currency.IsActive = 'Y'
    AND ClientInfo.AD_Client_ID =
    (
        SELECT
            QueryParameters.AD_Client_ID
        FROM QueryParameters QueryParameters
    )
)";

            string cashAccessSql = @"
SELECT
    CashJournal.C_Cash_ID,
    CashJournal.AD_Client_ID,
    CashJournal.AD_Org_ID,
    CashJournal.C_Currency_ID,
    CashJournal.StatementDate
FROM C_Cash CashJournal
WHERE CashJournal.DocStatus IN ('CO','CL')";

            /*
             * Apply MRole only to the physical C_Cash table query.
             */
            cashAccessSql =
                MRole.GetDefault(ctx)
                    .AddAccessSQL(
                        cashAccessSql,
                        "CashJournal",
                        MRole.SQL_FULLYQUALIFIED,
                        MRole.SQL_RO
                    );

            string cashAccessCteSql = @"
CashAccess AS
(
" + cashAccessSql + @"
)";

            string cashFilteredSql = @"
CashFiltered AS
(
    SELECT
        CashJournal.C_Cash_ID,
        CashJournal.AD_Client_ID,
        CashJournal.AD_Org_ID,
        CashJournal.C_Currency_ID,
        CashJournal.StatementDate,
        CashLine.C_CashLine_ID,
        CashLine.Amount
    FROM CashAccess CashJournal
    INNER JOIN C_CashLine CashLine ON (CashJournal.C_Cash_ID = CashLine.C_Cash_ID)
    WHERE CashLine.IsActive = 'Y'
    AND CashLine.Amount > 0 )";

            string convertedAmountSql = @"
CASE
    WHEN CashData.C_Currency_ID =
        SchemaCurrency.C_Currency_ID
    THEN
        COALESCE
        (
            CashData.Amount,
            0
        )
    ELSE
        CurrencyConvert
        (
            COALESCE
            (
                CashData.Amount,
                0
            ),
            CashData.C_Currency_ID,
            SchemaCurrency.C_Currency_ID,
            CashData.StatementDate,
            0,
            CashData.AD_Client_ID,
            CashData.AD_Org_ID
        )
END";

            string todayDataSql = @"
TodayData AS
(
    SELECT
        ROUND
        (
            CAST
            (
                COALESCE
                (
                    SUM
                    (
                        " + convertedAmountSql + @"
                    ),
                    0
                )
                AS " + numericType + @"
            ),

            CAST
            (
                COALESCE
                (
                    MAX
                    (
                        SchemaCurrency.StdPrecision
                    ),
                    2
                )
                AS INTEGER
            )
        ) AS MainMetric,

        MAX
        (
            SchemaCurrency.C_Currency_ID
        ) AS C_Currency_ID,

        MAX
        (
            SchemaCurrency.ISO_Code
        ) AS CurrencyISO,

        MAX
        (
            SchemaCurrency.Cur_Symbol
        ) AS CurrencySymbol,

        COALESCE
        (
            MAX
            (
                SchemaCurrency.StdPrecision
            ),
            2
        ) AS StdPrecision,

        COUNT
        (
            CashData.C_CashLine_ID
        ) AS RecordCount

    FROM SchemaCurrency SchemaCurrency

    INNER JOIN DateRange DateRange ON
    (
        1 = 1
    )

    LEFT OUTER JOIN CashFiltered CashData ON
    (
        CashData.AD_Client_ID =
        SchemaCurrency.AD_Client_ID

        AND CAST
        (
            CashData.StatementDate
            AS TIMESTAMP
        ) >=
        DateRange.TodayStart

        AND CAST
        (
            CashData.StatementDate
            AS TIMESTAMP
        ) <
        DateRange.TodayEnd
    )
)";

            string dailyTotalsSql = @"
DailyTotals AS
(
    SELECT
        " + cashDateSql + @" AS CashDate,

        CAST
        (
            COALESCE
            (
                SUM
                (
                    " + convertedAmountSql + @"
                ),
                0
            )
            AS " + numericType + @"
        ) AS DailyAmount

    FROM CashFiltered CashData

    INNER JOIN SchemaCurrency SchemaCurrency ON
    (
        SchemaCurrency.AD_Client_ID =
        CashData.AD_Client_ID
    )

    INNER JOIN DateRange DateRange ON
    (
        CAST
        (
            CashData.StatementDate
            AS TIMESTAMP
        ) >=
        DateRange.AverageStart

        AND CAST
        (
            CashData.StatementDate
            AS TIMESTAMP
        ) <
        DateRange.AverageEnd
    )

    GROUP BY
        " + cashDateSql + @"
)";

            string averageDataSql = @"
AverageData AS
(
    SELECT
        CAST
        (
            COALESCE
            (
                AVG
                (
                    DailyTotals.DailyAmount
                ),
                0
            )
            AS " + numericType + @"
        ) AS AvgDailyAmount

    FROM DailyTotals DailyTotals
)";

            string sql = @"
WITH
" + queryParametersSql + @",
" + dateRangeSql + @",
" + schemaCurrencySql + @",
" + cashAccessCteSql + @",
" + cashFilteredSql + @",
" + todayDataSql + @",
" + dailyTotalsSql + @",
" + averageDataSql + @"
SELECT
    TodayData.MainMetric,
    TodayData.C_Currency_ID,
    TodayData.CurrencyISO,
    TodayData.CurrencySymbol,
    TodayData.StdPrecision,
    TodayData.RecordCount,

    ROUND
    (
        CAST
        (
            AverageData.AvgDailyAmount
            AS " + numericType + @"
        ),

        CAST
        (
            TodayData.StdPrecision
            AS INTEGER
        )
    ) AS AvgDailyAmount,

    DateRange.TodayDate AS DateFrom,
    DateRange.TodayDate AS DateTo

FROM TodayData TodayData

INNER JOIN AverageData AverageData ON
(
    1 = 1
)

INNER JOIN DateRange DateRange ON
(
    1 = 1
)";

            SqlParameter[] parameters =
                new SqlParameter[]
                {
                    new SqlParameter(
                        "@AD_Client_ID",
                        ctx.GetAD_Client_ID()
                    )
                };

            return new SqlQueryData
            {
                Sql = sql,
                Parameters = parameters
            };
        }


        private SqlQueryData BuildTodayCashInRowsSql(
            Ctx ctx,
            int pageNo,
            int pageSize
        )
        {
            string queryParametersFrom =
                DB.IsOracle()
                    ? " FROM DUAL"
                    : string.Empty;

            string numericType =
                DB.IsOracle()
                    ? "NUMBER"
                    : "NUMERIC";

            string textType =
                DB.IsOracle()
                    ? "VARCHAR2(4000)"
                    : "VARCHAR(4000)";

            string todayDateSql =
                GetTodayDateSql();

            int startRow =
                ((pageNo - 1) * pageSize) + 1;

            int endRow =
                pageNo * pageSize;

            string queryParametersSql = @"
QueryParameters AS
(
    SELECT
        @AD_Client_ID AS AD_Client_ID,
        @AD_Language AS AD_Language,
        @StartRow AS StartRow,
        @EndRow AS EndRow"
                + queryParametersFrom + @"
)";

            string dateRangeSql = @"
DateRange AS
(
    SELECT
        CAST
        (
            " + todayDateSql + @"
            AS TIMESTAMP
        ) AS TodayStart,

        CAST
        (
            " + todayDateSql + @" + 1
            AS TIMESTAMP
        ) AS TodayEnd

    FROM QueryParameters QueryParameters
)";

            string schemaCurrencySql = @"
SchemaCurrency AS
(
    SELECT
        ClientInfo.AD_Client_ID,
        AcctSchema.C_Currency_ID,
        Currency.StdPrecision
    FROM AD_ClientInfo ClientInfo
    INNER JOIN C_AcctSchema AcctSchema ON
    (
        AcctSchema.C_AcctSchema_ID =
        ClientInfo.C_AcctSchema1_ID
    )
    INNER JOIN C_Currency Currency ON
    (
        Currency.C_Currency_ID =
        AcctSchema.C_Currency_ID
    )
    WHERE ClientInfo.IsActive = 'Y'
    AND AcctSchema.IsActive = 'Y'
    AND Currency.IsActive = 'Y'
    AND ClientInfo.AD_Client_ID =
    (
        SELECT
            QueryParameters.AD_Client_ID
        FROM QueryParameters QueryParameters
    )
)";

            string cashAccessSql = @"
SELECT
    CashJournal.C_Cash_ID,
    CashJournal.AD_Client_ID,
    CashJournal.AD_Org_ID,
    CashJournal.C_Currency_ID,
    CashJournal.C_CashBook_ID,
    CashJournal.DocumentNo,
    CashJournal.StatementDate
FROM C_Cash CashJournal
WHERE CashJournal.DocStatus IN ('CO','CL')";

            cashAccessSql =
                MRole.GetDefault(ctx)
                    .AddAccessSQL(
                        cashAccessSql,
                        "CashJournal",
                        MRole.SQL_FULLYQUALIFIED,
                        MRole.SQL_RO
                    );

            string cashAccessCteSql = @"
CashAccess AS
(
" + cashAccessSql + @"
)";

            string cashTypeReferenceSql = @"
CashTypeReference AS
(
    SELECT DISTINCT
        TRIM
        (
            CAST
            (
                RefList.Value
                AS " + textType + @"
            )
        ) AS ReferenceValue,
        TRIM
        (
            CAST
            (
                RefList.Name
                AS " + textType + @"
            )
        ) AS BaseName,
        TRIM
        (
            CAST
            (
                RefListTrl.Name
                AS " + textType + @"
            )
        ) AS TranslatedName
    FROM AD_Table TableInfo
    INNER JOIN AD_Column ColumnInfo ON
    (
        ColumnInfo.AD_Table_ID =
        TableInfo.AD_Table_ID
    )
    INNER JOIN AD_Reference ReferenceInfo ON
    (
        ReferenceInfo.AD_Reference_ID =
        ColumnInfo.AD_Reference_Value_ID
    )

    INNER JOIN AD_Ref_List RefList ON
    (
        RefList.AD_Reference_ID =
        ReferenceInfo.AD_Reference_ID
    )
    LEFT OUTER JOIN AD_Ref_List_Trl RefListTrl ON
    (
        RefListTrl.AD_Ref_List_ID =
        RefList.AD_Ref_List_ID

        AND RefListTrl.AD_Language =
        (
            SELECT
                QueryParameters.AD_Language
            FROM QueryParameters QueryParameters
        )
    )
    WHERE TableInfo.TableName = 'C_CashLine'
    AND ColumnInfo.ColumnName = 'CashType'
    AND TableInfo.IsActive = 'Y'
    AND ColumnInfo.IsActive = 'Y'
    AND ReferenceInfo.IsActive = 'Y'
    AND RefList.IsActive = 'Y'
)";

            string cashFilteredSql = @"
CashFiltered AS
(
    SELECT
        CashJournal.C_Cash_ID,
        CashJournal.AD_Client_ID,
        CashJournal.AD_Org_ID,
        CashJournal.C_Currency_ID,
        CashJournal.C_CashBook_ID,
        CashJournal.DocumentNo,
        CashJournal.StatementDate,
        CashLine.C_CashLine_ID,
        CashLine.Description,
        CashLine.Amount,
        CashLine.C_Charge_ID,
        TRIM
        (
            CAST
            (
                CashLine.CashType
                AS " + textType + @"
            )
        ) AS CashTypeValue
    FROM CashAccess CashJournal
    INNER JOIN C_CashLine CashLine ON
    (
        CashLine.C_Cash_ID =
        CashJournal.C_Cash_ID
    )
    WHERE CashLine.IsActive = 'Y'
    AND CashLine.Amount > 0
)";

            string convertedAmountSql = @"
CASE
    WHEN CashData.C_Currency_ID =
        SchemaCurrency.C_Currency_ID
    THEN
        COALESCE
        (
            CashData.Amount,
            0
        )
    ELSE
        CurrencyConvert
        (
            COALESCE
            (
                CashData.Amount,
                0
            ),
            CashData.C_Currency_ID,
            SchemaCurrency.C_Currency_ID,
            CashData.StatementDate,
            0,
            CashData.AD_Client_ID,
            CashData.AD_Org_ID
        )
END";

            string todayRowsSql = @"
TodayRows AS
(
    SELECT
        CashData.C_CashLine_ID,
        CashData.C_Cash_ID,
        CashData.DocumentNo,
        CashData.StatementDate,
        CashData.Description,
        CashData.CashTypeValue,
        CashTypeReference.BaseName
            AS CashTypeBaseName,
        CashTypeReference.TranslatedName
            AS CashTypeTranslatedName,
        Charge.Name
            AS ChargeName,
        CashBook.Name
            AS CashBookName,
        ROUND
        (
            CAST
            (
                " + convertedAmountSql + @"
                AS " + numericType + @"
            ),
            CAST
            (
                COALESCE
                (
                    SchemaCurrency.StdPrecision,
                    2
                )
                AS INTEGER
            )
        ) AS ConvertedAmount,
        COALESCE
        (
            SchemaCurrency.StdPrecision,
            2
        ) AS StdPrecision
    FROM CashFiltered CashData
    INNER JOIN DateRange DateRange ON
    (
        CAST
        (
            CashData.StatementDate
            AS TIMESTAMP
        ) >=
        DateRange.TodayStart

        AND CAST
        (
            CashData.StatementDate
            AS TIMESTAMP
        ) <
        DateRange.TodayEnd
    )
    INNER JOIN SchemaCurrency SchemaCurrency ON
    (
        SchemaCurrency.AD_Client_ID =
        CashData.AD_Client_ID
    )
    LEFT OUTER JOIN C_CashBook CashBook ON
    (
        CashBook.C_CashBook_ID =
        CashData.C_CashBook_ID
    )
    LEFT OUTER JOIN C_Charge Charge ON
    (
        Charge.C_Charge_ID =
        CashData.C_Charge_ID
    )
    LEFT OUTER JOIN CashTypeReference CashTypeReference ON
    (
        CashTypeReference.ReferenceValue =
        CashData.CashTypeValue
    )
)";

            string numberedRowsSql = @"
NumberedRows AS
(
    SELECT
        TodayRows.C_CashLine_ID,
        TodayRows.C_Cash_ID,
        TodayRows.DocumentNo,
        TodayRows.StatementDate,
        TodayRows.Description,
        TodayRows.CashTypeValue,
        TodayRows.CashTypeBaseName,
        TodayRows.CashTypeTranslatedName,
        TodayRows.ChargeName,
        TodayRows.CashBookName,
        TodayRows.ConvertedAmount,
        TodayRows.StdPrecision,
        COUNT
        (
            1
        ) OVER () AS TotalRecords,
        ROUND
        (
            CAST
            (
                SUM
                (
                    TodayRows.ConvertedAmount
                ) OVER ()
                AS " + numericType + @"
            ),
            CAST
            (
                COALESCE
                (
                    TodayRows.StdPrecision,
                    2
                )
                AS INTEGER
            )
        ) AS TotalAmount,
        ROW_NUMBER() OVER
        (
            ORDER BY
                TodayRows.StatementDate DESC,
                TodayRows.C_CashLine_ID DESC
        ) AS RowNo
    FROM TodayRows TodayRows
)";

            string sql = @"
WITH
" + queryParametersSql + @",
" + dateRangeSql + @",
" + schemaCurrencySql + @",
" + cashAccessCteSql + @",
" + cashTypeReferenceSql + @",
" + cashFilteredSql + @",
" + todayRowsSql + @",
" + numberedRowsSql + @"
SELECT
    NumberedRows.C_CashLine_ID,
    NumberedRows.C_Cash_ID,
    NumberedRows.DocumentNo,
    NumberedRows.StatementDate,
    NumberedRows.Description,
    NumberedRows.CashTypeValue,
    NumberedRows.CashTypeBaseName,
    NumberedRows.CashTypeTranslatedName,
    NumberedRows.ChargeName,
    NumberedRows.CashBookName,
    NumberedRows.ConvertedAmount,
    NumberedRows.TotalRecords,
    NumberedRows.TotalAmount,
    NumberedRows.StdPrecision
FROM NumberedRows NumberedRows
WHERE NumberedRows.RowNo >=
(
    SELECT
        QueryParameters.StartRow
    FROM QueryParameters QueryParameters
)
AND NumberedRows.RowNo <=
(
    SELECT
        QueryParameters.EndRow

    FROM QueryParameters QueryParameters
)
ORDER BY
    NumberedRows.RowNo";

            SqlParameter[] parameters =
                new SqlParameter[]
                {
            new SqlParameter(
                "@AD_Client_ID",
                ctx.GetAD_Client_ID()
            ),

            new SqlParameter(
                "@AD_Language",
                ctx.GetAD_Language()
            ),

            new SqlParameter(
                "@StartRow",
                startRow
            ),

            new SqlParameter(
                "@EndRow",
                endRow
            )
                };

            return new SqlQueryData
            {
                Sql =
                    sql,

                Parameters =
                    parameters
            };
        }


        private string FirstNotEmpty(
            params string[] values
        )
        {
            if (values == null)
            {
                return string.Empty;
            }

            for (int index = 0; index < values.Length; index++)
            {
                if (
                    !string.IsNullOrWhiteSpace(
                        values[index]
                    )
                )
                {
                    return values[index];
                }
            }

            return string.Empty;
        }

        private decimal GetRoundedDecimal(
            IDataReader reader,
            string columnName,
            int precision
        )
        {
            object value =
                reader[columnName];

            if (
                value == null ||
                value == DBNull.Value
            )
            {
                return 0;
            }

            precision =
                Math.Max(
                    0,
                    Math.Min(
                        precision,
                        28
                    )
                );

            decimal decimalValue;

            string invariantText =
                Convert.ToString(
                    value,
                    CultureInfo.InvariantCulture
                );

            if (
                decimal.TryParse(
                    invariantText,
                    NumberStyles.Any,
                    CultureInfo.InvariantCulture,
                    out decimalValue
                )
                ||
                decimal.TryParse(
                    value.ToString(),
                    NumberStyles.Any,
                    CultureInfo.CurrentCulture,
                    out decimalValue
                )
            )
            {
                return Math.Round(
                    decimalValue,
                    precision,
                    MidpointRounding.AwayFromZero
                );
            }

            double doubleValue;

            if (
                double.TryParse(
                    invariantText,
                    NumberStyles.Any,
                    CultureInfo.InvariantCulture,
                    out doubleValue
                )
                &&
                !double.IsNaN(
                    doubleValue
                )
                &&
                !double.IsInfinity(
                    doubleValue
                )
            )
            {
                double roundedDouble =
                    Math.Round(
                        doubleValue,
                        precision,
                        MidpointRounding.AwayFromZero
                    );

                if (
                    roundedDouble >
                    Convert.ToDouble(
                        decimal.MaxValue
                    )
                    ||
                    roundedDouble <
                    Convert.ToDouble(
                        decimal.MinValue
                    )
                )
                {
                    return 0;
                }

                return Convert.ToDecimal(
                    roundedDouble
                );
            }

            return 0;
        }

        private string GetTodayDateSql()
        {
            if (DB.IsOracle())
            {
                return "TRUNC(CURRENT_DATE)";
            }

            return "CURRENT_DATE";
        }

        private string GetDateOnlySql(
            string columnName
        )
        {
            if (DB.IsOracle())
            {
                return
                    "CAST(TRUNC("
                    + columnName
                    + ") AS DATE)";
            }

            return
                "CAST("
                + columnName
                + " AS DATE)";
        }

        /// <summary>
        /// Formats date using fixed SQL-safe format.
        /// </summary>
        private string FormatDate(
            DateTime date
        )
        {
            return date.ToString(
                "yyyy-MM-dd"
            );
        }

        /// <summary>
        /// Gets translated message with fallback text.
        /// </summary>
        private string GetMsg(
            Ctx ctx,
            string key,
            string fallback
        )
        {
            string msg =
                Msg.GetMsg(
                    ctx,
                    key
                );

            if (
                string.IsNullOrEmpty(
                    msg
                )
                ||
                msg == key
                ||
                msg == "[" + key + "]"
            )
            {
                return fallback;
            }

            return msg;
        }

        private class SqlQueryData
        {
            public string Sql
            {
                get;
                set;
            }

            public SqlParameter[] Parameters
            {
                get;
                set;
            }
        }
    }
}
