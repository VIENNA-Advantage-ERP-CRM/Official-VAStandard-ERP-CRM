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
    ///               Returns the top N customers ranked by total AR receipt
    ///               collections (C_Payment.PaymentAmount) over a rolling window of the
    ///               last 30 days, converted to the accounting-schema (base)
    ///               currency. MRole row-level security is applied only on the
    ///               main physical table (C_Payment, alias Payment) inside the
    ///               CustomerCollections CTE body — never on the SchemaCurrency
    ///               CTE alias nor on the outer combined query. Compatible with
    ///               PostgreSQL and Oracle.
    /// Chronological development:
    ///   VAI154      2026-06-02 Created
    /// </summary>
    public class VAS_013_TopCustomerReceiptsModel
    {
        /// <summary>
        /// Returns the top N customers by collection amount for the session
        /// client over the last 30 days (transaction date), highest first. All
        /// amounts are converted to the base/accounting currency so a single
        /// symbol applies to the whole list.
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
                dateCondition = "Payment.DateTrx >= CURRENT_DATE - 30 AND Payment.DateTrx < CURRENT_DATE + 1";
            }
            else
            {
                dateCondition = "TRUNC(Payment.DateTrx) >= TRUNC(SYSDATE) - 30 AND TRUNC(Payment.DateTrx) <= TRUNC(SYSDATE)";
            }

            /* SchemaCurrency CTE resolves the accounting-schema (base) currency
               for the client; it reads only system/reference tables, so no MRole
               predicate is applied to it. */
            string schemaCurrencySql = @"
                SELECT ClientInfo.AD_Client_ID,
                       AcctSchema.C_Currency_ID AS Acct_Currency_ID,
                       Currency.StdPrecision AS Std_Precision,
                       CASE WHEN Currency.CurSymbol IS NOT NULL THEN Currency.CurSymbol ELSE Currency.ISO_Code END AS Currency_Symbol,
                       Currency.ISO_Code AS Currency_ISO
                FROM AD_ClientInfo ClientInfo
                INNER JOIN C_AcctSchema AcctSchema ON (AcctSchema.C_AcctSchema_ID=ClientInfo.C_AcctSchema1_ID)
                INNER JOIN C_Currency Currency ON (Currency.C_Currency_ID=AcctSchema.C_Currency_ID)";

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

            /* Top-N ranked output. OFFSET / FETCH NEXT is portable across
               Oracle 12c+ and PostgreSQL and lets the row cap be parameterized. */
            string sql = @"
                WITH SchemaCurrency AS (
                    " + schemaCurrencySql + @"
                ),
                CustomerCollections AS (
                    " + customerCollectionsSql + @"
                )
                SELECT CustomerCollections.Customer_Name,
                       CustomerCollections.Currency_Symbol,
                       CustomerCollections.Currency_ISO,
                       CustomerCollections.Std_Precision,
                       ROUND(
                           COALESCE(CustomerCollections.Collection_Amount, 0),
                           CustomerCollections.Std_Precision
                       ) AS Collection_Amount
                FROM CustomerCollections
                ORDER BY Collection_Amount DESC
                OFFSET 0 ROWS FETCH NEXT @TopN ROWS ONLY";

            SqlParameter[] parameters = new SqlParameter[]
            {
                new SqlParameter("@TopN", topN)
            };

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

                    /* All rows share the base currency — capture the symbol /
                       ISO / precision once from the first row read. */
                    if (result.Rows.Count == 0)
                    {
                        result.CurrencySymbol = Util.GetValueOfString(dr["Currency_Symbol"]);
                        result.CurrencyIso = Util.GetValueOfString(dr["Currency_ISO"]);
                        result.StdPrecision = stdPrecision;
                    }

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
