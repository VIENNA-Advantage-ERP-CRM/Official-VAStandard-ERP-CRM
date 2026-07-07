/******************************************************
 * Module Name    : VASLogic
 * Purpose        : Aging Receivables widget data
 * chronological  : Development
 * Created Date   : 28 April 2026
 * Created by     : VAI154
 ******************************************************/

using System;
using System.Collections.Generic;
using System.Data;
using VAdvantage.DataBase;
using VAdvantage.Model;
using VAdvantage.Utility;

namespace VASLogic.Models
{
    public class VAS_AgingReceivablesWidgetModel
    {
        /// <summary>
        /// Returns aging-receivables totals bucketed by overdue range.
        /// Buckets: NotDue, 1-30, 31-60, 61-90, 90+ (combines 91-120 and >120).
        /// </summary>
        public AgingReceivablesData GetAgingReceivables(Ctx ctx)
        {
            AgingReceivablesData result = new AgingReceivablesData();

            string innerSql = @"SELECT i.AD_Client_ID,
                                       i.AD_Org_ID,
                                       ips.DueDate,
                                       ips.DueAmt,
                                       i.C_Currency_ID,
                                       i.DateAcct,
                                       i.C_ConversionType_ID,
                                       i.IsSoTrx,
                                       i.IsReturnTrx
                                FROM C_InvoicePaySchedule ips
                                INNER JOIN C_Invoice i ON (ips.C_Invoice_ID = i.C_Invoice_ID)
                                WHERE ips.VA009_IsPaid = 'N'
                                  AND i.DocStatus IN ('CO','CL')
                                  AND i.IsSoTrx = 'Y'";

            innerSql = MRole.GetDefault(ctx).AddAccessSQL(
                innerSql, "i", MRole.SQL_FULLYQUALIFIED, MRole.SQL_RW);

            string sql = @"WITH SchemaCurrency AS (
                              SELECT ci.AD_Client_ID,
                                     cs.C_Currency_ID  AS Acct_Currency_ID,
                                     cur.StdPrecision,
                                     cur.CurSymbol,
                                     cur.ISO_Code
                              FROM AD_ClientInfo ci
                              INNER JOIN C_AcctSchema cs ON (cs.C_AcctSchema_ID = ci.C_AcctSchema1_ID)
                              INNER JOIN C_Currency cur ON (cur.C_Currency_ID = cs.C_Currency_ID)
                           ),
                           Bucketed AS (
                              SELECT b.AD_Client_ID,
                                     CASE
                                        WHEN TRUNC(b.DueDate) >= TRUNC(CURRENT_DATE) THEN 'Not_Due'
                                        WHEN (TRUNC(CURRENT_DATE) - TRUNC(b.DueDate)) BETWEEN 1 AND 30   THEN 'Days_1_30'
                                        WHEN (TRUNC(CURRENT_DATE) - TRUNC(b.DueDate)) BETWEEN 31 AND 60  THEN 'Days_31_60'
                                        WHEN (TRUNC(CURRENT_DATE) - TRUNC(b.DueDate)) BETWEEN 61 AND 90  THEN 'Days_61_90'
                                        WHEN (TRUNC(CURRENT_DATE) - TRUNC(b.DueDate)) BETWEEN 91 AND 120 THEN 'Days_91_120'
                                        WHEN (TRUNC(CURRENT_DATE) - TRUNC(b.DueDate)) > 120              THEN 'Days_Over_120'
                                     END AS bucket,
                                     CASE
                                        WHEN b.IsSoTrx = 'Y' AND b.IsReturnTrx = 'N' THEN
                                             CurrencyConvert(b.DueAmt, b.C_Currency_ID, sc.Acct_Currency_ID,
                                                             b.DateAcct, b.C_ConversionType_ID,
                                                             b.AD_Client_ID, b.AD_Org_ID)
                                        WHEN b.IsSoTrx = 'Y' AND b.IsReturnTrx = 'Y' THEN
                                            -CurrencyConvert(b.DueAmt, b.C_Currency_ID, sc.Acct_Currency_ID,
                                                             b.DateAcct, b.C_ConversionType_ID,
                                                             b.AD_Client_ID, b.AD_Org_ID)
                                        ELSE 0
                                     END AS Amt
                              FROM (" + innerSql + @") b
                              INNER JOIN SchemaCurrency sc ON (sc.AD_Client_ID = b.AD_Client_ID)
                           )
                           SELECT
                              ROUND(NVL(SUM(CASE WHEN bucket = 'Not_Due'       THEN Amt END), 0), MAX(sc.StdPrecision)) AS Not_Due_Amount,
                              ROUND(NVL(SUM(CASE WHEN bucket = 'Days_1_30'     THEN Amt END), 0), MAX(sc.StdPrecision)) AS Days_1_30_Amount,
                              ROUND(NVL(SUM(CASE WHEN bucket = 'Days_31_60'    THEN Amt END), 0), MAX(sc.StdPrecision)) AS Days_31_60_Amount,
                              ROUND(NVL(SUM(CASE WHEN bucket = 'Days_61_90'    THEN Amt END), 0), MAX(sc.StdPrecision)) AS Days_61_90_Amount,
                              ROUND(NVL(SUM(CASE WHEN bucket = 'Days_91_120'   THEN Amt END), 0), MAX(sc.StdPrecision)) AS Days_91_120_Amount,
                              ROUND(NVL(SUM(CASE WHEN bucket = 'Days_Over_120' THEN Amt END), 0), MAX(sc.StdPrecision)) AS Days_Over_120_Amount,
                              MAX(sc.StdPrecision) AS StdPrecision,
                              MAX(sc.CurSymbol)    AS CurSymbol,
                              MAX(sc.ISO_Code)     AS ISO_Code
                           FROM Bucketed b
                           INNER JOIN SchemaCurrency sc ON (sc.AD_Client_ID = b.AD_Client_ID)";

            DataSet ds = DB.ExecuteDataset(sql, null, null);
            if (ds != null && ds.Tables.Count > 0 && ds.Tables[0].Rows.Count > 0)
            {
                DataRow r = ds.Tables[0].Rows[0];
                result.NotDueAmount    = Util.GetValueOfDecimal(r["Not_Due_Amount"]);
                result.Days_1_30       = Util.GetValueOfDecimal(r["Days_1_30_Amount"]);
                result.Days_31_60      = Util.GetValueOfDecimal(r["Days_31_60_Amount"]);
                result.Days_61_90      = Util.GetValueOfDecimal(r["Days_61_90_Amount"]);
                result.Days_91_120     = Util.GetValueOfDecimal(r["Days_91_120_Amount"]);
                result.Days_Over_120   = Util.GetValueOfDecimal(r["Days_Over_120_Amount"]);
                result.StdPrecision    = Util.GetValueOfInt(r["StdPrecision"]);
                result.CurSymbol       = Util.GetValueOfString(r["CurSymbol"]);
                result.ISO_Code        = Util.GetValueOfString(r["ISO_Code"]);
            }

            return result;
        }

        public class AgingReceivablesData
        {
            public decimal NotDueAmount  { get; set; }
            public decimal Days_1_30     { get; set; }
            public decimal Days_31_60    { get; set; }
            public decimal Days_61_90    { get; set; }
            public decimal Days_91_120   { get; set; }
            public decimal Days_Over_120 { get; set; }
            public int     StdPrecision  { get; set; }
            public string  CurSymbol     { get; set; }
            public string  ISO_Code      { get; set; }
        }
    }
}
