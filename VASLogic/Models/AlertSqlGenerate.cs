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
        public List<Tabs> GetTable(Ctx ctx, int tabID)
        {
            List<Tabs> Tab = new List<Tabs>();
            string sql = @"SELECT DISTINCT tl.Name,tb.Ad_Table_ID,tb.AD_Tab_ID
                     FROM AD_Tab tb INNER JOIN Ad_Table tl ON (tl.Ad_Table_ID=tb.Ad_Table_ID)
                     WHERE tl.IsActive='Y' AND tb.IsActive='Y' AND tb.AD_Tab_ID=" + tabID;
            sql = MRole.GetDefault(ctx).AddAccessSQL(sql, "AD_Tab", MRole.SQL_FULLYQUALIFIED, MRole.SQL_RO);
            DataSet ds = DB.ExecuteDataset(sql);
            if (ds != null && ds.Tables[0].Rows.Count > 0)
            {
                for (int i = 0; i < ds.Tables[0].Rows.Count; i++)
                {
                    Tabs obj = new Tabs();
                    
                    obj.TableID = Util.GetValueOfInt(ds.Tables[0].Rows[i]["AD_Table_ID"]);
                    obj.TabID = Util.GetValueOfInt(ds.Tables[0].Rows[i]["AD_Tab_ID"]);
                    obj.TableName = Util.GetValueOfString(ds.Tables[0].Rows[i]["Name"]);
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
        public List<Columnsdetail> GetColumns(Ctx ctx, int tableID,int tabID)
        {
            string sql = @"SELECT DISTINCT t.TableName AS TableName,c.Name AS ColumnName ,f.AD_Field_ID AS FieldID,
                    c.AD_COLUMN_ID AS ColumnID,b.AD_Window_ID AS WindowID,
                    c.AD_Reference_ID AS DataType,f.Name AS FieldName,c.AD_REFERENCE_VALUE_ID AS ReferenceValueID,
                    c.ColumnName AS DBColumn
                    FROM AD_Table t 
                    LEFT OUTER JOIN AD_Column c ON (t.AD_Table_ID=c.AD_Table_ID)
                    LEFT OUTER JOIN AD_Tab b ON (b.AD_Table_ID=t.AD_Table_ID)
                    LEFT OUTER JOIN Ad_Field f ON (b.AD_TAB_ID=f.AD_TAB_ID) AND (c.AD_COLUMN_ID = f.AD_COLUMN_ID)
                    WHERE t.AD_Table_ID=" + tableID + @"
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
        /// <returns>ListofRecords</returns>
        public List<Dictionary<string, string>> GetResult(Ctx ctx, string query,int pageNo,int pageSize)
        {           
            List<Dictionary<string, string>> results = new List<Dictionary<string, string>>();
            string pattern = @"FROM\s+([\w.]+)";
            Match match = Regex.Match(query, pattern, RegexOptions.IgnoreCase);
            if (match.Success)
            {
                string tableName = match.Groups[1].Value;
                query = MRole.GetDefault(ctx).AddAccessSQL(query, tableName, MRole.SQL_FULLYQUALIFIED, MRole.SQL_RO);
            }
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
                else {
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
        public string SaveQuery(Ctx ctx,string query,int tableID, int alertID, int alertRuleID)
        {
            if(query!=null&&query.Length>0){
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
        public string UpdateQuery(Ctx ctx,string query, int tableID, int alertID, int alertRuleID)
        {
            if (query != null && query.Length > 0 && alertID > 0)
            {
                if (alertRuleID <= 0)
                {
                    string msg=SaveQuery(ctx, query, tableID, alertID, 0);
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
                    sql += " "+otherClause;
                }
            }           
            return sql;
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
        public int TableID { get; set; }
        public int TabID { get; set; }
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
    }

    public class ASearch
    {
        public int Value { get; set; }
        public string ColumnName { get; set; }
        public int DataType { get; set; }
    }
}