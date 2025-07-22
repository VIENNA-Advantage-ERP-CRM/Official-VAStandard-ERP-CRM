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
                    query += " FETCH FIRST 100 ROWS ONLY";
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
        public GetRecords GetResult(Ctx ctx, string query, int pageNo, int pageSize, string tableName, int recordCount)
        {
            GetRecords results = new GetRecords
            {
                RecordList = new List<Dictionary<string, string>>() // Initialize the list
            };
            query = MRole.GetDefault(ctx).AddAccessSQL(query, tableName, MRole.SQL_FULLYQUALIFIED, MRole.SQL_RO);
            // query += " FETCH FIRST 100 ROWS ONLY";

            VLogger.Get().Severe("Alert=" + query);

            if (ValidateSql(query))
            {
                DataSet ds = new DataSet();

                //if (pageNo == 1)
                //{
                //    ds = DB.ExecuteDataset(query);
                //}
                //else
                //{
                //    ds = DBase.DB.ExecuteDatasetPaging(query, pageNo, pageSize);
                //}

                string sql = "SELECT COUNT(*) FROM ( " + query + " )";

                if (VAdvantage.DataBase.DatabaseType.IsPostgre)

                {
                    sql += " as SQLQuery ";

                }


                int totalRec = Util.GetValueOfInt(DB.ExecuteScalar(sql, null, null));

                ds = DBase.DB.ExecuteDatasetPaging(query, pageNo, pageSize);

                if (ds != null && ds.Tables.Count > 0 && ds.Tables[0].Rows.Count > 0)
                {
                    DataTable table = ds.Tables[0];
                    int rowCount = table.Rows.Count;
                    int colCount = table.Columns.Count;

                    //if (recordCount == 0 && pageNo == 1 && rowCount > 100)
                    //{
                    //    results.TotalRecord = rowCount;
                    //    rowCount = 100;
                    //}
                    //else
                    //{
                    //    results.TotalRecord = recordCount;
                    //}

                    results.TotalRecord = totalRec;

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
                        results.RecordList.Add(rowResult); // Add rowResult to the list
                    }

                    // results.TotalRecord = totatRecCount; // Set Totalrecord
                    return results;
                }
                else
                {
                    return results;
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
        /// <param name="isEmail">isEmail</param>
        /// <param name="emailColumn">emailColumn</param>
        /// <returns>saved/notsaved</returns>
        public string SaveQuery(Ctx ctx, string query, int tableID, int alertID, int alertRuleID, bool isEmail, string emailColumn)
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
                    obj.Set_Value("IsEmail", Util.GetValueOfBool(isEmail));
                    obj.Set_Value("EMail", Util.GetValueOfString(emailColumn));
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
        /// <param name="isEmail">isEmail</param>
        /// <param name="emailColumn">emailColumn</param>
        /// <returns>Updated/NotUpdated</returns>
        public string UpdateQuery(Ctx ctx, string query, int tableID, int alertID, int alertRuleID, bool isEmail, string emailColumn)
        {
            if (query != null && query.Length > 0 && alertID > 0)
            {
                if (alertRuleID <= 0)
                {
                    string msg = SaveQuery(ctx, query, tableID, alertID, 0, isEmail, emailColumn);
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
                    obj.Set_Value("IsEmail", Util.GetValueOfBool(isEmail));
                    obj.Set_Value("EMail", Util.GetValueOfString(emailColumn));
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
        public AlertRuleDetail GetAlertData(Ctx ctx, int alertRuleID)
        {
            AlertRuleDetail details = new AlertRuleDetail();
            string sql = "";
            MAlertRule obj = new MAlertRule(ctx, alertRuleID, null);
            int AD_Alert_ID = obj.GetAD_Alert_ID();
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
            MAlert alert = new MAlert(ctx, AD_Alert_ID, null);
            if (Util.GetValueOfBool(alert.Get_Value("IsSchedule")))
            {
                details.IsSchedule = Util.GetValueOfBool(alert.Get_Value("IsSchedule"));
                details.IsEmail = Util.GetValueOfBool(obj.Get_Value("IsEmail"));
                details.EmailColumnName = Util.GetValueOfString(obj.Get_Value("EMail"));
            }
            details.query = sql;
            try
            {
                string decrypted = SecureEngine.Decrypt("ke9LjRIaP4Vsb9y66n1o18pQZUftEs692vGMirrMoME=:YN7RSxHiX54FDF7ucFYtFg==");
                Console.WriteLine("Decrypted: " + decrypted);
            }
            catch (Exception ex)
            {
                Console.WriteLine("Error while decrypting: " + ex.Message);
            }
            return details;
        }
        /// <summary>
        /// Getting idetifier value 
        /// </summary>
        /// <param name="ctx">Context</param>
        /// <param name="columnName">Column name</param>
        /// <param name="tableName">Table Name</param>
        /// <param name="displayType">AD_Refrence_ID</param>
        /// <param name="whereClause">SQL Where</param>
        /// <param name="isNameExist">Is nameExist</param>
        /// <param name="columnID">AD_Column_ID</param>
        /// <param name="refrenceValueID">AD_RefrenceValue_ID</param>
        /// <param name="windowNo">windowNo</param>
        /// <returns>idetifier and ID</returns>
        public List<IDDetails> GetIdsName(Ctx ctx, string columnName, string tableName, int displayType, string whereClause, bool isNameExist,
            int columnID, int refrenceValueID, int windowNo)
        {
            List<IDDetails> data = new List<IDDetails>();
            string getTable = "";
            string sql = "";
            bool isDisplayed = false;
            MLookup res = VLookUpFactory.Get(ctx, windowNo, columnID, displayType, columnName, refrenceValueID, false, "");

            if (res == null)
                return null;
            VLookUpInfo lInfo = res._vInfo;
            string pColumnName = res.GetColumnName();
            string keyCol = lInfo.keyColumn;
            if (pColumnName.IndexOf(".") > -1)
            {
                pColumnName = pColumnName.Substring(pColumnName.IndexOf(".") + 1);
            }
            string displayCol = lInfo.displayColSubQ;
            string newTable = lInfo.tableName;


            if (tableName.ToUpper() != newTable.ToUpper() && newTable != "")
            {
                sql = "SELECT ";
                if (!string.IsNullOrEmpty(displayCol))
                {
                    sql += displayCol + ", " + keyCol;
                    isDisplayed = true;
                }
                else
                {
                    sql += "*";
                }
                sql += @" FROM " + newTable + " WHERE " + newTable + "." + pColumnName + " IS NOT NULL ";
                getTable = newTable;
            }
            else
            {
                sql = @"SELECT " + columnName + " FROM " + tableName + " WHERE " + tableName + "." + columnName + " IS NOT NULL ";
                getTable = tableName;
                isDisplayed = false;
            }

            if (!string.IsNullOrEmpty(whereClause))
            {
                if (isNameExist)
                    sql += " AND " + getTable + ".Name LIKE '%" + whereClause + "%'";
                else
                    sql += " AND " + getTable + "." + columnName + " LIKE '%" + whereClause + "%'";
            }

            sql = MRole.GetDefault(ctx).AddAccessSQL(sql, getTable, MRole.SQL_FULLYQUALIFIED, MRole.SQL_RO);
            //  sql += " FETCH FIRST 100 ROWS ONLY";
            DataSet ds = DB.ExecuteDataset(sql);
            if (ds != null && ds.Tables[0].Rows.Count > 0)
            {
                DataTable table = ds.Tables[0];
                for (int i = 0; i < table.Rows.Count; i++)
                {
                    IDDetails obj = new IDDetails();
                    if (isDisplayed)
                    {
                        obj.Name = Util.GetValueOfString(table.Rows[i][0]);
                        obj.Value = Util.GetValueOfString(table.Rows[i][1]);
                        obj.tableName = getTable;
                        obj.isNameExist = true;
                    }
                    else
                    {
                        obj.Name = Util.GetValueOfString(table.Rows[i][columnName]);
                        obj.isNameExist = isNameExist;
                        obj.Value = Util.GetValueOfString(table.Rows[i][columnName]);
                        obj.tableName = getTable;
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
                if (lookup.tableName.Equals("AD_Ref_List"))
                {
                    subquery = " (SELECT " + displayCol + " FROM " + lookup.tableName + " WHERE " + lookup.keyColumn + " = " + tableName + "." + columnName + " AND AD_Reference_ID = " + refrenceID + ") AS " + columnName + "_TXT ";
                }
                else
                {
                    subquery = " (SELECT " + displayCol + " FROM " + lookup.tableName + " WHERE " + lookup.keyColumn + " = " + tableName + "." + columnName + ") AS " + columnName + "_TXT ";
                }
                subquery = subquery.ToLower();
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

    public class AlertRuleDetail
    {
        public string EmailColumnName { get; set; }
        public bool IsSchedule { get; set; }
        public bool IsEmail { get; set; }
        public string query { get; set; }
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
        public string Value { get; set; }
        public string Name { get; set; }
        public string tableName { get; set; }
        public bool isNameExist { get; set; }
    }
    public class GetRecords
    {
        public List<Dictionary<string, string>> RecordList { get; set; }
        public int TotalRecord { get; set; }
    }
}