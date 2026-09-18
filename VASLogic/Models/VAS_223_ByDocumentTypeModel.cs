/******************************************************
 * Module Name    : VASLogic
 * Purpose        : By Document Type dashboard widget data
 * chronological  : Development
 * Created Date   : 2026-08-31
 * Created by     : VAI154
 ******************************************************/

using System;
using System.Collections.Generic;
using System.Data;
using System.Data.SqlClient;
using VAdvantage.DataBase;
using VAdvantage.Logging;
using VAdvantage.Model;
using VAdvantage.Utility;

namespace VASLogic.Models
{
    /// <summary>
    /// Module Name : VAS_223_ByDocumentType
    /// Purpose     : Backs the VAS_223_ByDocumentTypeWidget dashboard widget
    ///               (Recurring module, 3x2 list). Answers "how are active recurring
    ///               setups distributed by document type?".
    ///
    ///               One grouped count over C_Recurring. The share each bucket holds
    ///               is derived in code rather than with a windowed
    ///               SUM(COUNT(1)) OVER () in SQL: the window function and its
    ///               rounding are the only reason the build pack needed a separate
    ///               PostgreSQL and Oracle statement, and the arithmetic is trivial
    ///               once the rows are in hand. One statement now runs unchanged on
    ///               both backends.
    ///
    ///               No display text is produced by the query. The stored
    ///               RecurringType code is returned raw and the client resolves the
    ///               label from AD_Message, so the whole Recurring family shares one
    ///               label map.
    ///
    ///               MRole row-level security is applied to the single main physical
    ///               table the widget fetches from (C_Recurring, alias r). There is no
    ///               join and no CTE, so there is no secondary alias to exclude.
    ///               GROUP BY and ORDER BY are appended AFTER AddAccessSQL so the
    ///               FROM-clause parser is not confused by a trailing clause.
    ///
    ///               The tenant always comes from the authenticated context; the
    ///               client never supplies AD_Client_ID.
    /// Chronological development:
    ///   VAI154      2026-08-31 Created
    /// </summary>
    public class VAS_223_ByDocumentTypeModel
    {
        private static readonly VLogger Log = VLogger.GetVLogger(typeof(VAS_223_ByDocumentTypeModel).FullName);

        /* C_Recurring.RecurringType stored codes (list reference), shared with the
           sibling Recurring widgets. Returned raw; the client resolves the labels. */
        public const string RECURRINGTYPE_GLJournal = "B";
        public const string RECURRINGTYPE_GLJournalBatch = "G";
        public const string RECURRINGTYPE_Invoice = "I";
        public const string RECURRINGTYPE_Project = "J";
        public const string RECURRINGTYPE_Order = "O";
        public const string RECURRINGTYPE_Payment = "P";

        /// <summary>
        /// Counts the active recurring setups that still have runs left, grouped by
        /// the type of document they generate, largest bucket first.
        /// </summary>
        /// <param name="ctx">Session context (client / org / role).</param>
        /// <returns>Populated <see cref="ByDocumentTypeInfo"/> (never null). Loaded is
        /// false only when the context is missing or the query failed; a tenant with
        /// no active setup returns Loaded=true with an empty bucket list - zero is a
        /// real answer, not an error state.</returns>
        public ByDocumentTypeInfo GetByDocumentType(Ctx ctx)
        {
            ByDocumentTypeInfo result = new ByDocumentTypeInfo();
            result.Loaded = false;
            result.Buckets = new List<DocumentTypeBucket>();

            if (ctx == null) { return result; }

            /* No org predicate: MRole.AddAccessSQL appends the organisation access
               clause for the main table itself. */
            string sql = @"
                SELECT r.RecurringType AS Recurring_Type,
                       COUNT(1) AS Setup_Count
                FROM C_Recurring r
                WHERE r.IsActive='Y'
                  AND r.AD_Client_ID IN (@AD_Client_ID)
                  AND COALESCE(r.RunsRemaining,0)>0";

            /* MRole only on the main physical table (C_Recurring / alias r). It
               supplies the organisation access clause, and the explicit tenant filter
               above is a second, independent guard rather than the only one. Applied
               before the grouping clauses so the WHERE clause is complete when the
               parser sees it. */
            sql = MRole.GetDefault(ctx).AddAccessSQL(sql, "r", MRole.SQL_FULLYQUALIFIED, MRole.SQL_RO);

            /* Largest bucket first; the stored code breaks a tie so two buckets of
               equal size keep a stable, reproducible order across refreshes. Appended
               after AddAccessSQL by design. */
            sql += @"
                GROUP BY r.RecurringType
                ORDER BY COUNT(1) DESC,r.RecurringType";

            SqlParameter[] parameters = new SqlParameter[]
            {
                new SqlParameter("@AD_Client_ID", ctx.GetAD_Client_ID())
            };

            try
            {
                DataSet ds = DB.ExecuteDataset(sql, parameters, null);
                if (ds != null && ds.Tables.Count > 0)
                {
                    DataTable dt = ds.Tables[0];

                    for (int i = 0; i < dt.Rows.Count; i++)
                    {
                        DocumentTypeBucket bucket = new DocumentTypeBucket();
                        bucket.RecurringType = Util.GetValueOfString(dt.Rows[i]["Recurring_Type"]);
                        bucket.SetupCount = Util.GetValueOfInt(dt.Rows[i]["Setup_Count"]);

                        result.Buckets.Add(bucket);
                        result.TotalSetups += bucket.SetupCount;
                    }

                    /* The share is computed once the total is known, so the buckets
                       always add up to the same denominator the subtitle reports.
                       Rounded to two decimals; the client trims trailing zeros so a
                       clean 50 reads as "50%" rather than "50.00%". */
                    if (result.TotalSetups > 0)
                    {
                        for (int i = 0; i < result.Buckets.Count; i++)
                        {
                            double share = (double)result.Buckets[i].SetupCount * 100d / result.TotalSetups;
                            result.Buckets[i].SetupPercent = Math.Round(share, 2);
                        }
                    }
                }

                result.Loaded = true;
            }
            catch (Exception ex)
            {
                /* Caught here so the caller can tell "query failed" apart from
                   "tenant genuinely has no active setup" - the widget renders those
                   two cases differently. */
                Log.Log(Level.SEVERE, "VAS_223_ByDocumentType.GetByDocumentType AD_Client_ID="
                    + ctx.GetAD_Client_ID(), ex);
                result.Loaded = false;
                result.TotalSetups = 0;
                result.Buckets.Clear();
            }

            return result;
        }

        /// <summary>
        /// Result envelope for the widget: the buckets and the total they share.
        /// </summary>
        public class ByDocumentTypeInfo
        {
            /// <summary>False only when the data could not be read. A tenant with no
            /// active setup is Loaded=true with an empty bucket list.</summary>
            public bool Loaded { get; set; }

            /// <summary>Active setups with runs left, across every bucket - the
            /// denominator behind each bucket's share.</summary>
            public int TotalSetups { get; set; }

            /// <summary>Largest bucket first.</summary>
            public List<DocumentTypeBucket> Buckets { get; set; }
        }

        /// <summary>One document type and the setups that generate it.</summary>
        public class DocumentTypeBucket
        {
            /// <summary>C_Recurring.RecurringType stored code (B/G/I/J/O/P); the
            /// client resolves the label. Empty when a setup carries no type.</summary>
            public string RecurringType { get; set; }

            public int SetupCount { get; set; }

            /// <summary>Share of TotalSetups, rounded to two decimals.</summary>
            public double SetupPercent { get; set; }
        }
    }
}
