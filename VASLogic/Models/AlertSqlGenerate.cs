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
        public List<Windows> GetWindows(Ctx ctx)
        {
            List<Windows> window = new List<Windows>();
            string sql = @"SELECT T.NAME AS TabName,W.DisplayName As WindowName,T.AD_Table_ID 
                     FROM AD_Tab T INNER JOIN AD_Window W ON T.AD_Window_ID=W.AD_Window_ID 
                     WHERE T.IsACtive='Y' AND W.IsACtive='Y'
                     ORDER BY WindowName,TabName";
            sql = MRole.GetDefault(ctx).AddAccessSQL(sql, "AD_Tab", MRole.SQL_FULLYQUALIFIED, MRole.SQL_RO);
            DataSet ds = DB.ExecuteDataset(sql);
            if (ds != null && ds.Tables[0].Rows.Count > 0)
            {
                for (int i = 0; i < ds.Tables[0].Rows.Count; i++)
                {
                    Windows obj = new Windows();
                    obj.WindowName = Util.GetValueOfString(ds.Tables[0].Rows[i]["WindowName"]);
                    obj.TableID = Util.GetValueOfInt(ds.Tables[0].Rows[i]["AD_Table_ID"]);
                    obj.TabName = Util.GetValueOfString(ds.Tables[0].Rows[i]["TabName"]);
                    window.Add(obj);
                }
            }
            return window;
        }

        public List<Tabs> GetTable(Ctx ctx, int tabID)
        {
            List<Tabs> Tab = new List<Tabs>();
            string sql = @"SELECT DISTINCT TL.Name,TB.Ad_Table_ID
                     FROM AD_Tab TB INNER JOIN Ad_Table TL ON TL.Ad_Table_ID=TB.Ad_Table_ID
                     WHERE TL.IsACtive='Y' AND TB.AD_Tab_ID=" + tabID;
            sql = MRole.GetDefault(ctx).AddAccessSQL(sql, "AD_Tab", MRole.SQL_FULLYQUALIFIED, MRole.SQL_RO);
            DataSet ds = DB.ExecuteDataset(sql);
            if (ds != null && ds.Tables[0].Rows.Count > 0)
            {
                for (int i = 0; i < ds.Tables[0].Rows.Count; i++)
                {
                    Tabs obj = new Tabs();
                    
                    obj.TableID = Util.GetValueOfInt(ds.Tables[0].Rows[i]["AD_Table_ID"]);
                    obj.TableName = Util.GetValueOfString(ds.Tables[0].Rows[i]["Name"]);
                    Tab.Add(obj);
                }
            }
            return Tab;
        }

        public List<Columnsdetail> GetColumns(Ctx ctx, int tableID)
        {
            string sql = @"SELECT DISTINCT T.TableName AS TableName,C.NAME AS ColumnName,C.AD_REFERENCE_ID AS DataType,F.Name AS FieldName, C.ColumnName AS DBColumn 
                    FROM AD_Table T 
                    INNER JOIN AD_Column C ON (T.AD_Table_ID=C.AD_Table_ID)
                    LEFT JOIN Ad_Field F ON (F.AD_Column_ID=C.AD_Column_ID) WHERE T.AD_Table_ID=" + tableID + @"
                    AND T.IsActive='Y' AND C.IsActive='Y' ORDER BY FieldName";
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
                    column.Add(obj);
                }
            }
            return column;
        }

        private bool ValidateSql(string sql)
        {
            if (string.IsNullOrEmpty(sql))
            {
                return false;
            }
            sql = sql.ToUpper();

            if (sql.IndexOf("UPDATE") > -1 || sql.IndexOf("DELETE") > -1 || sql.IndexOf("DROP") > -1
                || sql.IndexOf("TRUNCATE") > -1)
            {
                return false;
            }
            return true;

            //Check DROP Keyword
            //Check Truncate And Delete 
            //Check Update
        }
        public List<Dictionary<string, string>> GetResult(Ctx ctx, string query,int pageNo,int pageSize)
        {
            List<Dictionary<string, string>> results = new List<Dictionary<string, string>>();
            string pattern = @"FROM\s+([\w.]+)";
            Match match = Regex.Match(query, pattern, RegexOptions.IgnoreCase);
            if (match.Success)
            {
                string tableName = match.Groups[1].Value;
               // query = MRole.GetDefault(ctx).AddAccessSQL(query, tableName, MRole.SQL_FULLYQUALIFIED, MRole.SQL_RO);
            }          
            query += " Fetch First 100 Rows Only";
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
            return results;

        }
        public string SaveQuery(Ctx ctx,string query, string tableName,int TableID, int alertID, int alertRuleID)
        {
            if(query!=null&&query.Length>0){
             //  query= query.ToUpper();
                int indexOfFrom = query.IndexOf("FROM");
                int indexOfWhere = query.IndexOf("WHERE");
                int indexOfOrder = query.IndexOf("ORDER BY");
                string selectClause = query.Substring(7, indexOfFrom - 7).Trim();
                string fromClause = "";
                string otherClause = string.Empty;
                string whereClause = string.Empty;
                if (indexOfWhere != -1)
                {
                    fromClause = query.Substring(indexOfFrom + 4, indexOfWhere - (indexOfFrom + 4)).Trim();
                    if (indexOfOrder != -1)
                    {
                        whereClause = query.Substring(indexOfWhere+5, indexOfOrder - (indexOfWhere+5)).Trim();
                        otherClause = query.Substring(indexOfOrder).Trim();
                    }
                    else
                    {
                        whereClause = query.Substring(indexOfWhere+5).Trim();
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
                obj.SetOtherClause(Util.GetValueOfString(" "+otherClause));
                if (tableName != null&& tableName!="")
                {
                    obj.SetName(Util.GetValueOfString(tableName));
                }
                else {
                    obj.SetName(Util.GetValueOfString("AlertRule"));
                }
                obj.SetAD_Table_ID(Util.GetValueOfInt(TableID));
                obj.SetIsActive(true);
                obj.SetIsValid(true);
                if (obj.Save())
                {
                    return "Saved Successfully";
                }
                else
                {
                    ValueNamePair vnp = VLogger.RetrieveError();
                    string info = vnp.GetName();
                    return "not save";
                }
            }
            return "";
        }
        public string UpdateQuery(Ctx ctx,string query, int TableID, int alertID, int alertRuleID)
        {
            if (query != null && query.Length > 0 && alertID>0)
            {
                if (alertRuleID == 0) {
                    SaveQuery(ctx,query, "AlertRule",TableID, alertID, alertRuleID);
                    return "saved";
                }
                int indexOfFrom = query.IndexOf("FROM");
                int indexOfWhere = query.IndexOf("WHERE");
                int indexOfOrder = query.IndexOf("ORDER BY");
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
                obj.SetWhereClause(Util.GetValueOfString(whereClause+" "));
                obj.SetOtherClause(Util.GetValueOfString(" "+otherClause));
                obj.SetAD_Table_ID(Util.GetValueOfInt(TableID));
                obj.SetIsActive(true);
                if (obj.Save())
                {
                    return "Updated Successfully";
                }
                else
                {
                    ValueNamePair vnp = VLogger.RetrieveError();
                    string info = vnp.GetName();
                    return "not Updated";
                }
            }
            return "";
        }
        public string GetAlertData(Ctx ctx, int alertID, int alertRuleID)
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
                    sql += otherClause;
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
    }
    public class Tabs
    {
        public string TableName { get; set; }
        public int TableID { get; set; }
    }
    public class Columnsdetail
    {
        public string FieldName { get; set; }
        public string ColumnName { get; set; }
        public string TableName { get; set; }
        public string DBColumn { get; set; }
        public int DataType { get; set; }
    }
    public class ASearch
    {
        public int Value { get; set; }
        public string ColumnName { get; set; }
        public int DataType { get; set; }
    }
}