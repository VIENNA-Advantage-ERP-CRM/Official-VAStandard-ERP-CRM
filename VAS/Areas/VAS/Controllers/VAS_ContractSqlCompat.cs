/******************************************************
 * Module Name    : VAS
 * Purpose        : Oracle compatibility for the Service Contracts dashboard queries
 * chronological  : Development
 * Created Date   : 2026-11-26
 * Created by     : VAI052
 ******************************************************/

using System;
using System.Collections.Generic;
using System.Data;
using System.Data.SqlClient;
using System.Text.RegularExpressions;
using VAdvantage.DataBase;

namespace VAS.Controllers
{
    /// <summary>
    /// Module Name : VAS_ContractSqlCompat
    /// Purpose     : Covers the two things the Service Contracts widgets
    ///               (VAS_241 to VAS_267) need on Oracle that PostgreSQL
    ///               tolerates: (1) Oracle binds parameters by position, not
    ///               by name, so the array must follow the order of the
    ///               placeholders in the final SQL; (2) a code column
    ///               (ISO_Code, AD_Ref_List.Value) cannot share a CASE or
    ///               COALESCE with a national-text column (ORA-12704).
    /// Chronological development:
    ///   VAI052      2026-11-26 Created
    /// </summary>
    internal static class VAS_ContractSqlCompat
    {
        private static readonly Regex Placeholder = new Regex(@"@\w+", RegexOptions.Compiled);

        /// <summary>A stored code column in the character type of the name columns beside it.</summary>
        /// <param name="codeExpr">Qualified column holding the stored code.</param>
        /// <returns>The expression, cast on Oracle only.</returns>
        public static string AsNationalText(string codeExpr)
        {
            return DB.IsOracle() ? "CAST(" + codeExpr + " AS NVARCHAR2(100))" : codeExpr;
        }

        /// <summary>DB.ExecuteReader with the parameters ordered to match the SQL.</summary>
        /// <param name="sql">Final SQL text, after MRole and any appended clauses.</param>
        /// <param name="parameters">Parameters in any order.</param>
        /// <returns>Open data reader.</returns>
        public static IDataReader ExecuteReader(string sql, SqlParameter[] parameters)
        {
            return DB.ExecuteReader(sql, InSqlOrder(sql, parameters));
        }

        /// <summary>DB.ExecuteScalar with the parameters ordered to match the SQL.</summary>
        /// <param name="sql">Final SQL text, after MRole and any appended clauses.</param>
        /// <param name="parameters">Parameters in any order.</param>
        /// <param name="trx">Transaction, or null.</param>
        /// <returns>First column of the first row.</returns>
        public static object ExecuteScalar(string sql, SqlParameter[] parameters, Trx trx)
        {
            return DB.ExecuteScalar(sql, InSqlOrder(sql, parameters), trx);
        }

        /// <summary>
        /// The parameters in order of first appearance in the SQL. A parameter the
        /// statement does not mention is left out, since by position it would shift
        /// every value after it. Each placeholder must occur only once per statement.
        /// </summary>
        /// <param name="sql">Final SQL text.</param>
        /// <param name="parameters">Parameters in any order.</param>
        /// <returns>Ordered parameter array.</returns>
        private static SqlParameter[] InSqlOrder(string sql, SqlParameter[] parameters)
        {
            List<SqlParameter> ordered = new List<SqlParameter>();
            HashSet<string> seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            foreach (Match match in Placeholder.Matches(sql))
            {
                if (!seen.Add(match.Value)) { continue; }

                SqlParameter parameter = Array.Find(parameters,
                    p => string.Equals(p.ParameterName, match.Value, StringComparison.OrdinalIgnoreCase));
                if (parameter != null) { ordered.Add(parameter); }
            }

            return ordered.ToArray();
        }
    }
}
