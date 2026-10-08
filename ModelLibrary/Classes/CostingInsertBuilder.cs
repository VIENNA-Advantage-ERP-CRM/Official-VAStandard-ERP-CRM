/********************************************************
 * Project Name   : VAdvantage
 * Module Name    : ModelLibrary
 * Class Name     : CostingInsertBuilder
 * Purpose        : Build a direct INSERT for costing tables (M_CostQueue, M_CostQueueTransaction,
 *                  M_CostElementDetail, T_Temp_CostDetail) instead of instantiating and saving the
 *                  PO, which is the slow part of the costing engine (defaults load, column lookups,
 *                  re-read after save) when it runs for every line / cost element.
 * Class Used     : none
 * Chronological  : Development
 * Amit           : 06-Oct-2026
  ******************************************************/

using System;
using System.Globalization;
using System.Text;
using VAdvantage.DataBase;
using VAdvantage.Logging;
using VAdvantage.Model;
using VAdvantage.Utility;

namespace ModelLibrary.Classes
{
    public class CostingInsertBuilder
    {
        private readonly string _tableName;
        private readonly POInfo _poInfo;
        private readonly StringBuilder _columns = new StringBuilder();
        private readonly StringBuilder _values = new StringBuilder();

        /// <summary>
        /// Record ID generated for the new row
        /// </summary>
        public int ID { get; private set; }

        /// <summary>
        /// Start an insert with the key and the standard columns PO.Save() would write
        /// </summary>
        /// <param name="ctx">context</param>
        /// <param name="tableName">table name, key column is TableName_ID</param>
        /// <param name="AD_Client_ID">Client</param>
        /// <param name="AD_Org_ID">Organization</param>
        /// <param name="trx">transaction used to get the next ID (same as PO.Save)</param>
        public CostingInsertBuilder(Ctx ctx, string tableName, int AD_Client_ID, int AD_Org_ID, Trx trx)
        {
            _tableName = tableName;
            _poInfo = POInfo.GetPOInfo(ctx, PO.Get_Table_ID(tableName));
            ID = DB.GetNextID(AD_Client_ID, tableName, trx);

            DateTime now = DateTime.Now;
            AddRaw(tableName + "_ID", ID.ToString());
            AddRaw("AD_Client_ID", AD_Client_ID.ToString());
            AddRaw("AD_Org_ID", AD_Org_ID.ToString());
            AddRaw("IsActive", "'Y'");
            AddDate("Created", now);
            AddRaw("CreatedBy", ctx.GetAD_User_ID().ToString());
            AddDate("Updated", now);
            AddRaw("UpdatedBy", ctx.GetAD_User_ID().ToString());
        }

        /// <summary>
        /// Is the column available on the table (same check as PO.Get_ColumnIndex > -1)
        /// </summary>
        public bool HasColumn(string columnName)
        {
            return _poInfo != null && _poInfo.GetColumnIndex(columnName) > -1;
        }

        /// <summary>
        /// Reference ID - 0 or less is left out (the generated setters set it to null and PO.Save
        /// skips null values, so the DB default applies)
        /// </summary>
        public CostingInsertBuilder AddID(string columnName, int value)
        {
            return value > 0 ? AddRaw(columnName, value.ToString()) : this;
        }

        /// <summary>
        /// Mandatory integer, 0 is a valid value (e.g. M_AttributeSetInstance_ID on M_CostQueue)
        /// </summary>
        public CostingInsertBuilder AddInt(string columnName, int value)
        {
            return AddRaw(columnName, value.ToString());
        }

        public CostingInsertBuilder AddDecimal(string columnName, decimal value)
        {
            return AddRaw(columnName, value.ToString(CultureInfo.InvariantCulture));
        }

        public CostingInsertBuilder AddBool(string columnName, bool value)
        {
            return AddRaw(columnName, value ? "'Y'" : "'N'");
        }

        public CostingInsertBuilder AddDate(string columnName, DateTime? value)
        {
            return value == null ? this : AddRaw(columnName, GlobalVariable.TO_DATE(value, false));
        }

        /// <summary>
        /// Value read through Get_Value from another PO, added only when the column exists on this table
        /// </summary>
        public CostingInsertBuilder AddValueIfExists(string columnName, object value)
        {
            if (value == null || value == DBNull.Value || !HasColumn(columnName))
            {
                return this;
            }
            if (value is int || value is long || value is short)
            {
                // all optional columns passed here are references - 0 means no reference
                return AddID(columnName, Convert.ToInt32(value));
            }
            if (value is decimal || value is double || value is float)
            {
                return AddDecimal(columnName, Convert.ToDecimal(value));
            }
            if (value is bool)
            {
                return AddBool(columnName, (bool)value);
            }
            if (value is DateTime)
            {
                return AddDate(columnName, (DateTime)value);
            }
            return AddRaw(columnName, "'" + value.ToString().Replace("'", "''") + "'");
        }

        private CostingInsertBuilder AddRaw(string columnName, string sqlValue)
        {
            if (_columns.Length > 0)
            {
                _columns.Append(", ");
                _values.Append(", ");
            }
            _columns.Append(columnName);
            _values.Append(sqlValue);
            return this;
        }

        public string GetSQL()
        {
            return "INSERT INTO " + _tableName + " (" + _columns + ") VALUES (" + _values + ")";
        }

        /// <summary>
        /// Execute the insert
        /// </summary>
        /// <param name="trx">transaction</param>
        /// <param name="error">DB error when the insert failed</param>
        /// <returns>true, when the row is inserted</returns>
        public bool Execute(Trx trx, out string error)
        {
            error = null;
            if (ID <= 0)
            {
                error = "No NextID for " + _tableName;
                return false;
            }
            if (DB.ExecuteQuery(GetSQL(), null, trx) > 0)
            {
                return true;
            }
            ValueNamePair pp = VLogger.RetrieveError();
            error = pp != null ? (!string.IsNullOrEmpty(pp.GetName()) ? pp.GetName() : pp.GetValue()) : "";
            return false;
        }
    }
}
