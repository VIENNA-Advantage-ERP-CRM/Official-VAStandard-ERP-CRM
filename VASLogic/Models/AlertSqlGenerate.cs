/*******************************************************
       * Module Name    : VAS
       * Purpose        : Create SQL Generator For TabAlertRule.
       * chronological development.
       * WindowName     :Alert
       * Created Date   : 21 Nov 2023
       * Created by     : VAI055
      ******************************************************/
using System;
using System.Collections.Generic;
using System.Data;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using System.Web;
using VAdvantage.Classes;
using VAdvantage.DataBase;
using VAdvantage.Logging;
using VAdvantage.Model;
using VAdvantage.Utility;
namespace VIS.Models
{
    public class AlertSqlGenerate
    {

        /// <summary>
        ///  Get Table information From Tab
        /// </summary>
        /// <param name="ctx">Contex</param>
        /// <param name="tabID">AD_Tab_ID</param>
        /// <returns>TableName/AD_Table_ID</returns>
        public List<Tabs> GetTable(Ctx ctx, int tabID, int windowNo)
        {
            List<Tabs> Tab = new List<Tabs>();
            string sql = @"SELECT DISTINCT tl.Name, tb.Ad_Table_ID, tb.AD_Tab_ID, tb.WhereClause, tl.TableName 
                     FROM AD_Tab tb INNER JOIN Ad_Table tl ON (tl.Ad_Table_ID=tb.Ad_Table_ID)
                     WHERE tl.IsActive='Y' AND tb.IsActive='Y' AND tb.AD_Tab_ID=" + tabID;
            sql = MRole.GetDefault(ctx).AddAccessSQL(sql, "AD_Tab", MRole.SQL_FULLYQUALIFIED, MRole.SQL_RO);
            DataSet ds = DB.ExecuteDataset(sql);
            if (ds != null && ds.Tables[0].Rows.Count > 0)
            {
                for (int i = 0; i < ds.Tables[0].Rows.Count; i++)
                {
                    Tabs obj = new Tabs();
                    string where = Util.GetValueOfString(ds.Tables[0].Rows[i]["WhereClause"]);
                    obj.TableID = Util.GetValueOfInt(ds.Tables[0].Rows[i]["AD_Table_ID"]);
                    obj.TabID = Util.GetValueOfInt(ds.Tables[0].Rows[i]["AD_Tab_ID"]);
                    obj.TableName = Util.GetValueOfString(ds.Tables[0].Rows[i]["Name"]);
                    if (where.IndexOf("@") != -1)
                    {
                        where = Env.ParseContext(ctx, windowNo, where, false);
                    }
                    obj.WhereClause = where;
                    string query = @"SELECT * FROM " + Util.GetValueOfString(ds.Tables[0].Rows[i]["TableName"]);
                    if (!string.IsNullOrEmpty(where))
                    {
                        query += " WHERE " + where;
                    }
                    query = MRole.GetDefault(ctx).AddAccessSQL(query, Util.GetValueOfString(ds.Tables[0].Rows[i]["TableName"]), MRole.SQL_FULLYQUALIFIED, MRole.SQL_RO);
                    DataSet dr = DB.ExecuteDataset(query);
                    obj.dr = dr;
                    Tab.Add(obj);
                }
            }
            return Tab;
        }

        /// <summary>
        /// Get All Columns From Table
        /// </summary>
        /// <param name="ctx">Contex</param>
        /// <param name="tableID">AD_Table_ID</param>
        /// <returns>ColumnInfornationList</returns>
        public List<Columnsdetail> GetColumns(Ctx ctx, int tableID, int tabID)
        {
            string sql = @"SELECT DISTINCT t.TableName AS TableName,c.Name AS ColumnName ,f.AD_Field_ID AS FieldID,
                    c.AD_COLUMN_ID AS ColumnID,b.AD_Window_ID AS WindowID,
                    c.AD_Reference_ID AS DataType,f.Name AS FieldName,c.AD_REFERENCE_VALUE_ID AS ReferenceValueID,
                    c.ColumnName AS DBColumn, c.IsKey AS IsKey, c.IsParent As IsParent 
                    FROM AD_Table t 
                    LEFT OUTER JOIN AD_Column c ON (t.AD_Table_ID=c.AD_Table_ID)
                    LEFT OUTER JOIN AD_Tab b ON (b.AD_Table_ID=t.AD_Table_ID)
                    LEFT OUTER JOIN Ad_Field f ON (b.AD_TAB_ID=f.AD_TAB_ID) AND (c.AD_COLUMN_ID = f.AD_COLUMN_ID)
                    WHERE t.AD_Table_ID=" + tableID + @" AND c.AD_Reference_ID <> 28 
                    AND b.AD_TAB_ID =" + tabID + @" 
                    AND t.IsActive='Y' AND c.IsActive='Y' AND b.IsActive='Y' AND c.ColumnSQL IS NULL ORDER BY FieldName";
            sql = MRole.GetDefault(ctx).AddAccessSQL(sql, "AD_Table", MRole.SQL_FULLYQUALIFIED, MRole.SQL_RO);
            DataSet ds = DB.ExecuteDataset(sql);
            List<Columnsdetail> column = new List<Columnsdetail>();
            if (ds != null && ds.Tables[0].Rows.Count > 0)
            {
                for (int i = 0; i < ds.Tables[0].Rows.Count; i++)
                {
                    Columnsdetail obj = new Columnsdetail();
                    obj.FieldName = Util.GetValueOfString(ds.Tables[0].Rows[i]["FieldName"]);
                    obj.DBColumn = Util.GetValueOfString(ds.Tables[0].Rows[i]["DBColumn"]);
                    obj.ColumnName = Util.GetValueOfString(ds.Tables[0].Rows[i]["ColumnName"]);
                    obj.TableName = Util.GetValueOfString(ds.Tables[0].Rows[i]["TableName"]);
                    obj.DataType = Util.GetValueOfInt(ds.Tables[0].Rows[i]["DataType"]);
                    obj.ReferenceValueID = Util.GetValueOfInt(ds.Tables[0].Rows[i]["ReferenceValueID"]);
                    obj.ColumnID = Util.GetValueOfInt(ds.Tables[0].Rows[i]["ColumnID"]);
                    obj.FieldID = Util.GetValueOfInt(ds.Tables[0].Rows[i]["FieldID"]);
                    obj.WindowID = Util.GetValueOfInt(ds.Tables[0].Rows[i]["WindowID"]);
                    obj.IsKey = Util.GetValueOfString(ds.Tables[0].Rows[i]["IsKey"]);
                    obj.IsParent = Util.GetValueOfString(ds.Tables[0].Rows[i]["IsParent"]);
                    column.Add(obj);
                }
            }
            return column;
        }

        /// <summary>
        /// Check DROP Keyword,Truncate, Update And Delete
        /// </summary>
        /// <param name="sql">SQL Query</param>
        /// <returns>true/false</returns>     
        private bool ValidateSql(string sql)
        {
            if (string.IsNullOrEmpty(sql))
            {
                return false;
            }
            string upperCaseSql = sql.ToUpper();
            string[] words = upperCaseSql.Split(new char[] { ' ', '\t', '\n', '\r', '(', ')', ',' }, StringSplitOptions.RemoveEmptyEntries);
            bool isValidate = false;
            string[] isValidatewords = { "UPDATE", "DELETE", "DROP", "TRUNCATE" };
            foreach (string word in words)
            {
                if (isValidatewords.Contains(word))
                {
                    if (upperCaseSql.IndexOf($" {word} ") > -1)
                    {
                        isValidate = true;
                        break;
                    }
                }
            }
            return !isValidate;
        }

        /// <summary>
        /// Get Result Of Query 
        /// </summary>
        /// <param name="query">Query</param>
        /// <param name="pageNo">Page No</param>
        /// <param name="pageSize">page Size</param>
        /// <param name="tableName">Table Name</param>
        /// <returns>ListofRecords</returns>
        public List<Dictionary<string, string>> GetResult(Ctx ctx, string query, int pageNo, int pageSize, string tableName)
        {
            List<Dictionary<string, string>> results = new List<Dictionary<string, string>>();        
            query = MRole.GetDefault(ctx).AddAccessSQL(query, tableName, MRole.SQL_FULLYQUALIFIED, MRole.SQL_RO);
            query += " FETCH FIRST 100 ROWS ONLY";
            if (ValidateSql(query))
            {
                DataSet ds = new DataSet();
                if (pageNo == 0)
                {
                    ds = DB.ExecuteDataset(query);
                }
                else
                {
                    ds = VIS.DBase.DB.ExecuteDatasetPaging(query, pageNo, pageSize);
                }

                if (ds != null && ds.Tables.Count > 0)
                {
                    DataTable table = ds.Tables[0];
                    int rowCount = table.Rows.Count;
                    int colCount = table.Columns.Count;
                    for (int rowIndex = 0; rowIndex < rowCount; rowIndex++)
                    {
                        Dictionary<string, string> rowResult = new Dictionary<string, string>();
                        for (int colIndex = 0; colIndex < colCount; colIndex++)
                        {
                            rowResult["recid"] = Util.GetValueOfString(colIndex + 1);
                            string columnName = table.Columns[colIndex].ColumnName;
                            string columnValue = Util.GetValueOfString(table.Rows[rowIndex][columnName]);
                            rowResult[columnName] = columnValue;

                        }
                        results.Add(rowResult);
                    }
                    return results;
                }
                else
                {
                    return null;
                }
            }
            return null;
        }

        /// <summary>
        /// Saving SqlGenerator query in AlertRule Window
        /// </summary>
        /// <param name="ctx">Contex</param>
        /// <param name="query">Query</param>
        /// <param name="tableID">AD_Table_ID</param>
        /// <param name="alertID">AD_Alert_ID</param>
        /// <param name="alertRuleID">AD_AlertRule_ID</param>
        /// <returns>saved/notsaved</returns>
        public string SaveQuery(Ctx ctx, string query, int tableID, int alertID, int alertRuleID)
        {
            if (query != null && query.Length > 0)
            {
                int indexOfFrom = query.IndexOf("FROM");
                int indexOfWhere = query.IndexOf("WHERE");
                int indexOfOrder = query.IndexOf("ORDER BY");
                if (indexOfFrom != -1)
                {
                    string selectClause = query.Substring(7, indexOfFrom - 7).Trim();
                    string fromClause = "";
                    string otherClause = string.Empty;
                    string whereClause = string.Empty;
                    if (indexOfWhere != -1)
                    {
                        fromClause = query.Substring(indexOfFrom + 4, indexOfWhere - (indexOfFrom + 4)).Trim();
                        if (indexOfOrder != -1)
                        {
                            whereClause = query.Substring(indexOfWhere + 5, indexOfOrder - (indexOfWhere + 5)).Trim();
                            otherClause = query.Substring(indexOfOrder).Trim();
                        }
                        else
                        {
                            whereClause = query.Substring(indexOfWhere + 5).Trim();
                        }
                    }
                    else
                    {
                        fromClause = (indexOfOrder != -1) ? query.Substring(indexOfFrom + 4, indexOfOrder - (indexOfFrom + 4)).Trim() : query.Substring(indexOfFrom + 4).Trim();
                    }

                    MAlertRule obj = new MAlertRule(ctx, 0, null);
                    obj.SetAD_Client_ID(ctx.GetAD_Client_ID());
                    obj.SetAD_Org_ID(ctx.GetAD_Org_ID());
                    obj.SetAD_Alert_ID(Util.GetValueOfInt(alertID));
                    obj.SetSelectClause(Util.GetValueOfString(selectClause));
                    obj.SetFromClause(Util.GetValueOfString(fromClause));
                    obj.SetWhereClause(Util.GetValueOfString(whereClause));
                    obj.SetOtherClause(Util.GetValueOfString(" " + otherClause));
                    obj.SetName(Util.GetValueOfString("AlertRule"));
                    obj.SetAD_Table_ID(Util.GetValueOfInt(tableID));
                    obj.SetIsActive(true);
                    obj.SetIsValid(true);
                    if (obj.Save())
                    {
                        return Msg.GetMsg(ctx, "SavedSuccessfully");
                    }
                    else
                    {
                        ValueNamePair vnp = VLogger.RetrieveError();
                        string info = vnp.GetName();
                        return Msg.GetMsg(ctx, "NotSaved");
                    }
                }
                return Msg.GetMsg(ctx, "VAS_SQLProperformat");
            }
            return "";
        }

        /// <summary>
        /// Update record of AlertRule by TabSqlGenerator 
        /// </summary>
        /// <param name="ctx">Contex</param>
        /// <param name="query">Query</param>
        /// <param name="tableID">AD_Table_ID</param>
        /// <param name="alertID">AD_Alert_ID</param>
        /// <param name="alertRuleID">AD_AlertRule_ID</param>
        /// <returns>Updated/NotUpdated</returns>
        public string UpdateQuery(Ctx ctx, string query, int tableID, int alertID, int alertRuleID)
        {
            if (query != null && query.Length > 0 && alertID > 0)
            {
                if (alertRuleID <= 0)
                {
                    string msg = SaveQuery(ctx, query, tableID, alertID, 0);
                    return msg;
                }
                int indexOfFrom = query.IndexOf("FROM");
                int indexOfWhere = query.IndexOf("WHERE");
                int indexOfOrder = query.IndexOf("ORDER BY");
                if (indexOfFrom != -1)
                {
                    string selectClause = query.Substring(7, indexOfFrom - 7).Trim();
                    string fromClause = string.Empty;
                    string otherClause = string.Empty;
                    string whereClause = string.Empty;
                    if (indexOfWhere != -1)
                    {
                        fromClause = query.Substring(indexOfFrom + 4, indexOfWhere - (indexOfFrom + 4)).Trim();
                        if (indexOfOrder != -1)
                        {
                            whereClause = query.Substring(indexOfWhere + 5, indexOfOrder - (indexOfWhere + 5)).Trim();
                            otherClause = query.Substring(indexOfOrder).Trim();
                        }
                        else
                        {
                            whereClause = query.Substring(indexOfWhere + 5).Trim();
                        }
                    }
                    else
                    {
                        fromClause = (indexOfOrder != -1) ? query.Substring(indexOfFrom + 4, indexOfOrder - (indexOfFrom + 4)).Trim() : query.Substring(indexOfFrom + 4).Trim();
                    }
                    MAlertRule obj = new MAlertRule(ctx, alertRuleID, null);
                    obj.SetSelectClause(Util.GetValueOfString(selectClause));
                    obj.SetFromClause(Util.GetValueOfString(fromClause));
                    obj.SetWhereClause(Util.GetValueOfString(whereClause + " "));
                    obj.SetOtherClause(Util.GetValueOfString(" " + otherClause));
                    obj.SetAD_Table_ID(Util.GetValueOfInt(tableID));
                    obj.SetIsActive(true);
                    if (obj.Save())
                    {
                        return Msg.GetMsg(ctx, "VAS_Updated");
                    }
                    else
                    {
                        ValueNamePair vnp = VLogger.RetrieveError();
                        string info = vnp.GetName();
                        return Msg.GetMsg(ctx, "VAS_NotUpdated");
                    }
                }
                return Msg.GetMsg(ctx, "VAS_SQLProperformat");
            }
            return "";
        }

        /// <summary>
        /// Get AlertRule RecordInfo for TabSqlGenerator
        /// </summary>
        /// <param name="ctx">Contex</param>
        /// <param name="alertID">AD_Alert_ID</param>
        /// <param name="alertRuleID">AD_AlertRule_ID</param>
        /// <returns>RecordInfo</returns>
        public string GetAlertData(Ctx ctx, int alertRuleID)
        {
            string sql = "";
            MAlertRule obj = new MAlertRule(ctx, alertRuleID, null);
            string selectClause = obj.GetSelectClause();
            string fromClause = obj.GetFromClause();
            string whereClause = obj.GetWhereClause();
            string otherClause = obj.GetOtherClause();
            if (selectClause != null && selectClause.Length > 0 && fromClause != null && fromClause.Length > 0)
            {
                sql = "SELECT " + selectClause + " FROM " + fromClause;
                if (whereClause != null && whereClause.Length > 0)
                {
                    sql += " WHERE " + whereClause;
                }
                if (otherClause != null && otherClause.Length > 0)
                {
                    sql += " " + otherClause;
                }
            }
            return sql;
        }
        public List<IDDetails> GetIdsName(Ctx ctx, string columnName, string tableName, int displayType)
        {
            List<IDDetails> data = new List<IDDetails>();
            string newTable = "";
            string getTable = "";
            string sql = "";

            if (columnName.EndsWith("_ID"))
            {
                newTable = columnName.Substring(0, columnName.Length - 3);
            }

            if (tableName.ToUpper() != newTable.ToUpper() && newTable != "")
            {
                sql = @"SELECT * FROM " + newTable + " FULL JOIN " + tableName +
                    " ON " + newTable + "." + columnName + " = " + tableName + "." + columnName;
                getTable = newTable;
            }
            else
            {
                sql = @"SELECT * FROM " + tableName;
                getTable = tableName;
            }
            sql = MRole.GetDefault(ctx).AddAccessSQL(sql, tableName, MRole.SQL_FULLYQUALIFIED, MRole.SQL_RO);
            DataSet ds = DB.ExecuteDataset(sql);
            if (ds != null && ds.Tables[0].Rows.Count > 0)
            {
                DataTable table = ds.Tables[0];
                bool hasNameColumn = false;
                if (ds.Tables.Contains(getTable) && ds.Tables[getTable].Columns.Contains("Name"))
                {
                    hasNameColumn = true;
                    table = ds.Tables[getTable];
                }
                else if (ds.Tables[0].Columns.Contains("Name"))
                {
                    hasNameColumn = true;
                }
                for (int i = 0; i < table.Rows.Count; i++)
                {
                    IDDetails obj = new IDDetails();
                    if (hasNameColumn)
                    {
                        obj.Name = Util.GetValueOfString(table.Rows[i]["Name"]);
                        obj.Value = Util.GetValueOfInt(table.Rows[i][columnName]);
                    }
                    else
                    {
                        obj.Name = Util.GetValueOfString(table.Rows[i][columnName]);
                        obj.Value = Util.GetValueOfInt(table.Rows[i][columnName]);
                    }
                    data.Add(obj);
                }
            }
            return data;
        }

        /// <summary>
        /// Method to get subquery from loopup  to show names instead of Ids
        /// </summary>
        /// <param name="windowNo">Window number</param>
        /// <param name="columnDatatype">DataType</param>
        /// <param name="columnID">ad_Column_ID</param>
        /// <param name="columnName">Column Name</param>
        /// <param name="refrenceID">AD_Refrence_ID</param>
        /// <param name="isParent">Is parent link column</param>
        /// <param name="tableName">Table Name</param>
        /// <returns>subquery</returns>
        public string GetLookup(Ctx ctx, int windowNo, int columnDatatype, int columnID, string columnName, int refrenceID, bool isParent, string tableName)
        {
            string subquery = "";
            VLookUpInfo lookup = VLookUpFactory.GetLookUpInfo(ctx, windowNo, columnDatatype, columnID, Env.GetLanguage(ctx),
                   columnName, refrenceID,
                   isParent, "");
            string displayCol = lookup.displayColSubQ;
            //Remove query which will fetch image.. Only display test in Filter option.
            if (displayCol.IndexOf("||'^^'|| NVL((SELECT NVL(ImageURL,'')") > 0
                && displayCol.IndexOf("thing.png^^') ||' '||") > 0)
            {
                var displayCol1 = displayCol.Substring(0, displayCol.IndexOf("||'^^'|| NVL((SELECT NVL(Imag"));
                displayCol = displayCol.Substring(displayCol.IndexOf("othing.png^^') ||' '||") + 22);
                displayCol = displayCol1 + "||'_'||" + displayCol;
            }
            if (displayCol.IndexOf("||'^^'|| NVL((SELECT NVL(ImageURL,'')") > 0)
            {
                int startIndex = displayCol.IndexOf("||'^^'|| NVL((SELECT NVL(Imag");
                int endIndex = displayCol.IndexOf("Images/nothing.png^^')") + "Images/nothing.png^^')".Length;
                int length = endIndex - startIndex;
                displayCol = displayCol.Remove(startIndex, length);
            }
            else if (displayCol.IndexOf("nothing.png") > -1)
            {
                displayCol = displayCol.Replace(displayCol.Substring(displayCol.IndexOf("NVL((SELECT NVL(ImageURL,'')"), displayCol.IndexOf("thing.png^^') ||' '||") + 21), "");
            }
            if (lookup.queryDirect.Length > 0 && !string.IsNullOrEmpty(displayCol))
            {
                subquery = " (SELECT " + displayCol + lookup.queryDirect.Substring(lookup.queryDirect.LastIndexOf(" FROM " + lookup.tableName + " "), lookup.queryDirect.Length - (lookup.queryDirect.LastIndexOf(" FROM " + lookup.tableName + " "))) + ") AS " + columnName + "_TXT "; ;
                subquery = subquery.Replace("@key", tableName + "." + columnName).ToLower();
            }
            return subquery;
        }
    }

    public class Windows
    {
        public string TabName { get; set; }
        public string WindowName { get; set; }
        public int TableID { get; set; }
        public int WindowID { get; set; }
    }

    public class Tabs
    {
        public string TableName { get; set; }
        public string WhereClause { get; set; }
        public int TableID { get; set; }
        public int TabID { get; set; }
        public DataSet dr { get; set; }
    }

    public class Columnsdetail
    {
        public string FieldName { get; set; }
        public string ColumnName { get; set; }
        public string TableName { get; set; }
        public string DBColumn { get; set; }
        public int DataType { get; set; }
        public int ReferenceValueID { get; set; }
        public int ColumnID { get; set; }
        public int FieldID { get; set; }
        public int WindowID { get; set; }
        public string IsKey { get; set; }
        public string IsParent { get; set; }
    }

    public class ASearch
    {
        public int Value { get; set; }
        public string ColumnName { get; set; }
        public int DataType { get; set; }
    }
    public class IDDetails
    {
        public int Value { get; set; }
        public string Name { get; set; }
    }
}