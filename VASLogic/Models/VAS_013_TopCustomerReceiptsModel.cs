/******************************************************
 * Module Name    : VASLogic
 * Purpose        : Top Customers by Collections dashboard widget data
 * chronological  : Development
 * Created Date   : 2026-06-02
 * Created by     : VAI154
 ******************************************************/

using System;
using System.Collections.Generic;
using System.Data;
using System.Data.SqlClient;
using VAdvantage.DataBase;
using VAdvantage.Model;
using VAdvantage.Utility;

namespace VASLogic.Models
{
    /// <summary>
    /// Module Name : VAS_013_TopCustomerReceipts
    /// Purpose     : Backs the VAS_013_TopCustomerReceipts dashboard widget.
    ///               Returns the top N customers ranked by total collections over a
    ///               rolling window of the last 30 days, converted to the
    ///               accounting-schema (base) currency. Collections come from BOTH
    ///               AR receipts (C_Payment.PaymentAmount) and cash-journal
    ///               collections (C_CashLine.Amount of Receipt / ReceiptReturn lines,
    ///               attributed to the line's business partner); the two sources are
    ///               UNIONed per partner before ranking. MRole row-level security is
    ///               applied on each main physical table independently (C_Payment in
    ///               the CustomerCollections CTE, C_Cash in the CashCollections CTE)
    ///               — never on the SchemaCurrency CTE alias nor on the outer
    ///               combined query. Compatible with PostgreSQL and Oracle.
    /// Chronological development:
    ///   VAI154      2026-06-02 Created
    ///   VAI154      2026-07-14 Add cash-journal Receipt / ReceiptReturn collections
    ///                          (C_Cash / C_CashLine) to the per-customer total.
    /// </summary>
    public class VAS_013_TopCustomerReceiptsModel
    {
        /// <summary>
        /// Returns the top N customers by collection amount for the session
        /// client over the last 30 days, highest first, combining AR receipts (by
        /// transaction date) and cash-journal Receipt / ReceiptReturn collections
        /// (by statement date). All amounts are converted to the base/accounting
        /// currency so a single symbol applies to the whole list.
        /// </summary>
        /// <param name="ctx">Session context (client / org / role).</param>
        /// <param name="topN">Number of customers to return (default 5).</param>
        /// <returns>Populated <see cref="TopCustomerCollections"/> (Rows may be empty).</returns>
        public TopCustomerCollections GetTopCustomers(Ctx ctx, int topN = 5)
        {
            TopCustomerCollections result = new TopCustomerCollections();
            result.Rows = new List<TopCustomerRow>();

            if (ctx == null) { return result; }
            if (topN <= 0) { topN = 5; }

            int clientId = ctx.GetAD_Client_ID();

            /* Last-30-days window built per dialect. PostgreSQL: in this
               deployment DateTrx behaves as a timestamp, so (date - integer)
               is the only well-defined day arithmetic (TRUNC / DATE-DATE either
               fail or return an INTERVAL). CURRENT_DATE + 1 with the < operator
               includes every receipt dated today regardless of its time part.
               Oracle: TRUNC drops the time component so the range is whole days. */
            string dateCondition;
            if (DB.IsPostgreSQL())
            {
                dateCondition = "Payment.DateAcct >= CURRENT_DATE - 30 AND Payment.DateAcct < CURRENT_DATE + 1";
            }
            else
            {
                dateCondition = "TRUNC(Payment.DateAcct) >= TRUNC(SYSDATE) - 30 AND TRUNC(Payment.DateAcct) <= TRUNC(SYSDATE)";
            }

            /* SchemaCurrency CTE resolves the accounting-schema (base) currency
               for the session client; it reads only system/reference tables, so
               no MRole predicate is applied to it. The AD_Client_ID filter scopes
               it to the current client so a multi-tenant database yields exactly
               one base-currency row, not one per client. */
            string schemaCurrencySql = @"
                SELECT ClientInfo.AD_Client_ID,
                       AcctSchema.C_Currency_ID AS Acct_Currency_ID,
                       Currency.StdPrecision AS Std_Precision,
                       CASE WHEN Currency.CurSymbol IS NOT NULL THEN Currency.CurSymbol ELSE Currency.ISO_Code END AS Currency_Symbol,
                       Currency.ISO_Code AS Currency_ISO
                FROM AD_ClientInfo ClientInfo
                INNER JOIN C_AcctSchema AcctSchema ON (AcctSchema.C_AcctSchema_ID=ClientInfo.C_AcctSchema1_ID)
                INNER JOIN C_Currency Currency ON (Currency.C_Currency_ID=AcctSchema.C_Currency_ID)
                WHERE ClientInfo.AD_Client_ID=" + clientId;

            /* CustomerCollections CTE — the main physical table is C_Payment
               (alias Payment). The SELECT/WHERE is built first so MRole can be
               applied to the Payment alias; GROUP BY is appended AFTER MRole so
               the FROM-clause parser is not confused by a trailing clause. */
            string customerCollectionsSql = @"
                SELECT BPartner.C_BPartner_ID AS C_BPartner_ID,
                       BPartner.Name AS Customer_Name,
                       SchemaCurrency.Currency_Symbol,
                       SchemaCurrency.Currency_ISO,
                       SchemaCurrency.Std_Precision,
                       SUM(
                           CASE
                               WHEN Payment.C_Currency_ID = SchemaCurrency.Acct_Currency_ID
                               THEN COALESCE(Payment.PaymentAmount, 0)
                               ELSE CurrencyConvert(
                                   COALESCE(Payment.PaymentAmount, 0),
                                   Payment.C_Currency_ID,
                                   SchemaCurrency.Acct_Currency_ID,
                                   COALESCE(Payment.DateAcct, Payment.DateTrx),
                                   Payment.C_ConversionType_ID,
                                   Payment.AD_Client_ID,
                                   Payment.AD_Org_ID
                               )
                           END
                       ) AS Collection_Amount
                FROM C_Payment Payment
                INNER JOIN C_BPartner BPartner ON (BPartner.C_BPartner_ID=Payment.C_BPartner_ID)
                INNER JOIN SchemaCurrency SchemaCurrency ON (SchemaCurrency.AD_Client_ID=Payment.AD_Client_ID)
                WHERE Payment.IsReceipt = 'Y'
                  AND Payment.IsActive = 'Y'
                  AND Payment.DocStatus IN ('CO', 'CL')
                  AND Payment.AD_Client_ID = " + clientId + @"
                  AND " + dateCondition;

            /* MRole only on the main physical table (C_Payment / alias Payment). */
            customerCollectionsSql = MRole.GetDefault(ctx).AddAccessSQL(
                customerCollectionsSql,
                "Payment",
                MRole.SQL_FULLYQUALIFIED,
                MRole.SQL_RO
            );

            customerCollectionsSql += @"
                GROUP BY BPartner.C_BPartner_ID,
                         BPartner.Name,
                         SchemaCurrency.Currency_Symbol,
                         SchemaCurrency.Currency_ISO,
                         SchemaCurrency.Std_Precision";

            /* CashCollections CTE — the main physical table is C_Cash (alias
               Cash); its C_CashLine child supplies the collected Amount and the
               business partner. Include completed/closed cash lines whose
               VSS_PAYMENTTYPE is a customer Receipt or ReceiptReturn (the return
               leg signed by its own Amount), over the same 30-day window (by
               statement date). Same source, currency conversion and payment-type
               rule as the Daily Collection Trend / Outstanding-vs-Received widgets
               (VAS_014 / 015). Mirrors the CustomerCollections CTE so the two can
               be UNIONed per partner. */
            string cashDateCondition;
            if (DB.IsPostgreSQL())
            {
                cashDateCondition = "Cash.StatementDate >= CURRENT_DATE - 30 AND Cash.StatementDate < CURRENT_DATE + 1";
            }
            else
            {
                cashDateCondition = "TRUNC(Cash.StatementDate) >= TRUNC(SYSDATE) - 30 AND TRUNC(Cash.StatementDate) <= TRUNC(SYSDATE)";
            }

            /* VSS_PAYMENTTYPE list values rendered as SQL literals via the same
               framework helper used elsewhere (e.g. VAS_014). */
            string cashReceiptTypes =
                GlobalVariable.TO_STRING(MCashLine.VSS_PAYMENTTYPE_Receipt) + ", " +
                GlobalVariable.TO_STRING(MCashLine.VSS_PAYMENTTYPE_ReceiptReturn);

            string cashCollectionsSql = @"
                SELECT BPartner.C_BPartner_ID AS C_BPartner_ID,
                       BPartner.Name AS Customer_Name,
                       SchemaCurrency.Currency_Symbol,
                       SchemaCurrency.Currency_ISO,
                       SchemaCurrency.Std_Precision,
                       SUM(
                           CASE
                               WHEN Cash.C_Currency_ID = SchemaCurrency.Acct_Currency_ID
                               THEN COALESCE(CashLine.Amount, 0)
                               ELSE CurrencyConvert(
                                   COALESCE(CashLine.Amount, 0),
                                   CashLine.C_Currency_ID,
                                   SchemaCurrency.Acct_Currency_ID,
                                   COALESCE(Cash.DateAcct, Cash.StatementDate),
                                   COALESCE(CashLine.C_ConversionType_ID, 0),
                                   CashLine.AD_Client_ID,
                                   CashLine.AD_Org_ID
                               )
                           END
                       ) AS Collection_Amount
                FROM C_Cash Cash
                INNER JOIN C_CashLine CashLine ON (CashLine.C_Cash_ID=Cash.C_Cash_ID)
                INNER JOIN C_BPartner BPartner ON (BPartner.C_BPartner_ID=CashLine.C_BPartner_ID)
                INNER JOIN SchemaCurrency SchemaCurrency ON (SchemaCurrency.AD_Client_ID=Cash.AD_Client_ID)
                WHERE Cash.IsActive = 'Y'
                  AND CashLine.IsActive = 'Y'
                  AND CashLine.VSS_PAYMENTTYPE IN (" + cashReceiptTypes + @")
                  AND Cash.DocStatus IN ('CO', 'CL')
                  AND Cash.AD_Client_ID = " + clientId + @"
                  AND " + cashDateCondition;

            /* MRole only on the main physical table (C_Cash / alias Cash); the
               joined C_CashLine / C_BPartner are child/lookup tables. GROUP BY is
               appended AFTER MRole so the FROM-clause parser is not confused by a
               trailing clause. */
            cashCollectionsSql = MRole.GetDefault(ctx).AddAccessSQL(
                cashCollectionsSql,
                "Cash",
                MRole.SQL_FULLYQUALIFIED,
                MRole.SQL_RO
            );

            cashCollectionsSql += @"
                GROUP BY BPartner.C_BPartner_ID,
                         BPartner.Name,
                         SchemaCurrency.Currency_Symbol,
                         SchemaCurrency.Currency_ISO,
                         SchemaCurrency.Std_Precision";

            /* Top-N ranked output. The two per-partner sources are UNIONed and
               re-aggregated by partner so a customer with both a receipt and a
               cash-book collection ranks on the combined total. OFFSET / FETCH
               NEXT is portable across Oracle 12c+ and PostgreSQL and lets the row
               cap be parameterized. */
            string sql = @"
                WITH SchemaCurrency AS (
                    " + schemaCurrencySql + @"
                ),
                CustomerCollections AS (
                    " + customerCollectionsSql + @"
                ),
                CashCollections AS (
                    " + cashCollectionsSql + @"
                ),
                CombinedCollections AS (
                    SELECT C_BPartner_ID, Customer_Name, Currency_Symbol, Currency_ISO, Std_Precision, Collection_Amount
                    FROM CustomerCollections
                    UNION ALL
                    SELECT C_BPartner_ID, Customer_Name, Currency_Symbol, Currency_ISO, Std_Precision, Collection_Amount
                    FROM CashCollections
                )
                SELECT CombinedCollections.Customer_Name,
                       MAX(CombinedCollections.Currency_Symbol) AS Currency_Symbol,
                       MAX(CombinedCollections.Currency_ISO) AS Currency_ISO,
                       MAX(CombinedCollections.Std_Precision) AS Std_Precision,
                       ROUND(
                           SUM(COALESCE(CombinedCollections.Collection_Amount, 0)),
                           MAX(CombinedCollections.Std_Precision)
                       ) AS Collection_Amount
                FROM CombinedCollections
                GROUP BY CombinedCollections.C_BPartner_ID,
                         CombinedCollections.Customer_Name
                HAVING SUM(COALESCE(CombinedCollections.Collection_Amount, 0)) > 0
                ORDER BY Collection_Amount DESC
                OFFSET 0 ROWS FETCH NEXT @TopN ROWS ONLY";

            SqlParameter[] parameters = new SqlParameter[]
            {
                new SqlParameter("@TopN", topN)
            };

            /* Resolve the base/accounting-currency descriptors up front, straight
               from the SchemaCurrency query, so the symbol / ISO / precision are
               ALWAYS present — even when there are zero collection rows. Previously
               these were captured only from the first data row, so they came back
               empty at zero rows and the widget lost its currency symbol (same
               class of bug fixed in OverdueController by sourcing the symbol from
               the schema-currency query independently of the data). This query
               reads only system/reference tables, so no MRole predicate applies. */
            result.StdPrecision = 2;
            IDataReader curDr = null;
            try
            {
                curDr = DB.ExecuteReader(schemaCurrencySql);
                if (curDr != null && curDr.Read())
                {
                    result.CurrencySymbol = Util.GetValueOfString(curDr["Currency_Symbol"]);
                    result.CurrencyIso = Util.GetValueOfString(curDr["Currency_ISO"]);
                    result.StdPrecision = Util.GetValueOfInt(curDr["Std_Precision"]);
                }
            }
            finally
            {
                if (curDr != null)
                {
                    curDr.Close();
                    curDr.Dispose();
                }
            }

            IDataReader dr = null;
            try
            {
                dr = DB.ExecuteReader(sql, parameters);
                while (dr != null && dr.Read())
                {
                    int stdPrecision = Util.GetValueOfInt(dr["Std_Precision"]);

                    decimal amount = Math.Round(
                        Util.GetValueOfDecimal(dr["Collection_Amount"]),
                        stdPrecision,
                        MidpointRounding.AwayFromZero
                    );

                    /* Currency symbol / ISO / precision are resolved once up front
                       from the SchemaCurrency query (above) so they are present even
                       at zero rows; nothing to capture per row here. */

                    result.Rows.Add(new TopCustomerRow
                    {
                        CustomerName = Util.GetValueOfString(dr["Customer_Name"]),
                        Amount = amount
                    });
                }
            }
            finally
            {
                if (dr != null)
                {
                    dr.Close();
                    dr.Dispose();
                }
            }

            return result;
        }

        /// <summary>
        /// Result envelope: the shared base-currency descriptors plus the ranked
        /// customer rows for the widget.
        /// </summary>
        public class TopCustomerCollections
        {
            public string CurrencySymbol { get; set; }
            public string CurrencyIso { get; set; }
            public int StdPrecision { get; set; }
            public List<TopCustomerRow> Rows { get; set; }
        }

        /// <summary>
        /// One ranked customer row: display name and total collection amount in
        /// base currency.
        /// </summary>
        public class TopCustomerRow
        {
            public string CustomerName { get; set; }
            public decimal Amount { get; set; }
        }
    }
}
