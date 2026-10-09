/******************************************************
 * Module Name    : VASLogic
 * Purpose        : Active Setups dashboard widget data
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
    /// Module Name : VAS_219_ActiveSetups
    /// Purpose     : Backs the VAS_219_ActiveSetupsWidget dashboard widget
    ///               (Recurring module, 2x1 KPI). Answers one question: how many
    ///               recurring setups of the session tenant are active AND still
    ///               have runs left to generate.
    ///               "Still has runs left" is C_Recurring.RunsRemaining > 0.
    ///               RunsRemaining is nullable on legacy rows, so it is coalesced
    ///               to 0 rather than compared directly - a NULL comparison would
    ///               silently drop those setups out of the count on both backends.
    ///               MRole row-level security is applied to the single main
    ///               physical table the widget fetches from (C_Recurring, alias r).
    ///               There is no CTE and no join here, so there is no secondary
    ///               alias to exclude.
    ///               The tenant always comes from the authenticated context; the
    ///               client never supplies AD_Client_ID. Compatible with
    ///               PostgreSQL and Oracle (COALESCE / COUNT only).
    /// Chronological development:
    ///   VAI154      2026-08-31 Created
    /// </summary>
    public class VAS_219_ActiveSetupsModel
    {
        private static readonly VLogger Log = VLogger.GetVLogger(typeof(VAS_219_ActiveSetupsModel).FullName);

        /// <summary>
        /// Counts the recurring setups that are active and still have remaining runs
        /// for the session tenant.
        /// </summary>
        /// <param name="ctx">Session context (client / org / role).</param>
        /// <returns>Populated <see cref="ActiveSetupsInfo"/> (never null). Loaded is
        /// false only when the context is missing; a tenant with no matching setup
        /// returns Loaded=true and ActiveSetups=0 - zero is a real answer, not an
        /// error state.</returns>
        public ActiveSetupsInfo GetActiveSetups(Ctx ctx)
        {
            ActiveSetupsInfo result = new ActiveSetupsInfo();
            result.Loaded = false;
            result.ActiveSetups = 0;

            if (ctx == null) { return result; }

            /* No org predicate is written here: MRole.AddAccessSQL appends the
               organisation access clause for the main table itself, so restating it
               would duplicate the filter and risk disagreeing with the role's own
               rule. */
            string sql = @"
                SELECT COUNT(1) AS Active_Setups
                FROM C_Recurring r
                WHERE r.IsActive='Y'
                  AND r.AD_Client_ID IN (@AD_Client_ID)
                  AND COALESCE(r.RunsRemaining,0)>0";

            /* MRole only on the main physical table (C_Recurring / alias r). It
               supplies the organisation access clause, and the explicit tenant filter
               above is a second, independent guard rather than the only one. Applied
               last so the WHERE clause is complete before the parser sees it. */
            sql = MRole.GetDefault(ctx).AddAccessSQL(sql, "r", MRole.SQL_FULLYQUALIFIED, MRole.SQL_RO);

            /* The provider binds positionally, so the parameters are added in the
               order their placeholders appear in the statement text. */
            List<SqlParameter> parameters = new List<SqlParameter>();
            parameters.Add(new SqlParameter("@AD_Client_ID", ctx.GetAD_Client_ID()));

            try
            {
                DataSet ds = DB.ExecuteDataset(sql, parameters.ToArray(), null);
                if (ds != null && ds.Tables.Count > 0 && ds.Tables[0].Rows.Count > 0)
                {
                    result.ActiveSetups = Util.GetValueOfInt(ds.Tables[0].Rows[0]["Active_Setups"]);
                }

                result.Loaded = true;
            }
            catch (Exception ex)
            {
                /* Caught here so the caller can tell "query failed" apart from
                   "tenant genuinely has no active setup" - the widget renders those
                   two cases differently. */
                Log.Log(Level.SEVERE, "VAS_219_ActiveSetups.GetActiveSetups AD_Client_ID=" + ctx.GetAD_Client_ID(), ex);
                result.Loaded = false;
                result.ActiveSetups = 0;
            }

            return result;
        }

        /// <summary>
        /// Result envelope for the widget.
        /// </summary>
        public class ActiveSetupsInfo
        {
            /// <summary>False only when the count could not be read (no context or
            /// query failure). A tenant with no matching setup is Loaded=true,
            /// ActiveSetups=0.</summary>
            public bool Loaded { get; set; }

            /// <summary>Active recurring setups with RunsRemaining &gt; 0.</summary>
            public int ActiveSetups { get; set; }
        }
    }
}
