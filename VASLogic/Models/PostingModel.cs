/********************************************************
 * Module Name    : VASLogic
 * Purpose        : Model class for Posting viewer
 * Class Used     : 
 * Chronological Development
 * VIS323  :  25 Nov 2022
 ******************************************************/

using ClosedXML.Excel;
using System;
using System.Collections.Generic;
using System.Data;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using System.Web;
using VAdvantage.Acct;
using VAdvantage.DataBase;
using VAdvantage.Logging;
using VAdvantage.Model;
using VAdvantage.Utility;

namespace VIS.Models
{
    public class PostingModel
    {
        private Ctx _ctx;

        public PostingModel(Ctx ctx)
        {
            _ctx = ctx;
        }
        #region DownloadExcel
        /// <summary>
        ///create excel report file
        /// </summary>
        /// <param name="Ad_Table_Id">Ad_Table_Id</param>
        /// <param name="Record_Id">Record_Id</param>
        /// <param name="reportName">OUT Parameter, excel report name</param>
        /// <returns>memory stream</returns>
        public MemoryStream DownloadExcel(int Ad_Table_Id, int Record_Id, out string reportName)
        {
            StringBuilder sql = new StringBuilder();
            MemoryStream stream = null;
            reportName = "";
            sql.Append("SELECT DISTINCT AC.Name,FA.C_AcctSchema_ID FROM Fact_Acct FA INNER JOIN C_AcctSchema AC ON " +
                    "FA.C_AcctSchema_ID=AC.C_AcctSchema_ID WHERE FA.Ad_Table_ID=" + Ad_Table_Id + " AND FA.Record_ID=" + Record_Id);
            DataSet dsFact = DB.ExecuteDataset(sql.ToString());
            if (dsFact != null && dsFact.Tables[0].Rows.Count > 0)
            {
                stream = CreateExcel(_ctx, dsFact, Ad_Table_Id, Record_Id);
                reportName = Util.GetValueOfString(dsFact.Tables[0].Rows[0]["Name"]) + DateTime.Now.ToString("MMddyyHHmm") + "_" + DateTime.Now.Ticks + ".xlsx";
            }
            return stream;
        }

        /// <summary>
        /// Create excel file
        /// </summary>
        /// <param name="ctx">Context</param>
        /// <param name="ds">DataSet</param>
        /// <param name="Ad_Table_Id">Ad_Table_Id</param>
        /// <param name="Record_Id">Record_Id</param>
        /// <returns>memory stream</returns>
        public MemoryStream CreateExcel(Ctx ctx, DataSet dsFact, int Ad_Table_Id, int Record_Id)
        {
            StringBuilder sql = new StringBuilder();
            List<Dictionary<string, string>> elementList = null;
            #region CeateTable

            //Get Data For Excel
            using (XLWorkbook wb = new XLWorkbook())
            {
                for (int j = 1; j <= dsFact.Tables[0].Rows.Count; j++)
                {
                    sql.Clear();
                    //special check for get Element List once based on AccountSchemaId
                    elementList = GetElementType(Util.GetValueOfInt(dsFact.Tables[0].Rows[j - 1]["C_ACCTSCHEMA_ID"]));
                    sql.Append(GetDataName(elementList, Ad_Table_Id, Record_Id, Util.GetValueOfInt(dsFact.Tables[0].Rows[j - 1]["C_ACCTSCHEMA_ID"])));
                    DataSet ds = DB.ExecuteDataset(sql.ToString());
                    //Setting Table Name  
                    if (ds != null && ds.Tables[0].Rows.Count > 0)
                    {
                        // Set AccountSchema Name as Sheet Name 
                        Regex pattern = new Regex("[/*?:\r]|[-]");//Added Special Characters which will be Replaced from string if exists
                        //Added special case for worksheet name should be less than 31 character
                        var ws = wb.Worksheets.Add(pattern.Replace(Util.GetValueOfString(dsFact.Tables[0].Rows[j - 1]["Name"]).Length > 31 ? Util.GetValueOfString(dsFact.Tables[0].Rows[j - 1]["Name"]).Substring(0, 31) : Util.GetValueOfString(dsFact.Tables[0].Rows[j - 1]["Name"]), "-"));
                        //Set Excel file header
                        ws.Cell("A1").Value = Util.GetValueOfString(dsFact.Tables[0].Rows[j - 1]["Name"]);
                        var range = ws.Range("A1:L1");
                        range.Merge().Style.Font.SetBold().Font.FontSize = 12;
                        ws.Cell("A1").Style.Alignment.SetHorizontal(XLAlignmentHorizontalValues.Center);

                        if (ds.Tables[0].Rows.Count > 0)
                        {
                            #region RowHeader
                            ws.Cell("A2").Value = Msg.Translate(ctx, "AD_Org_ID");
                            ws.Cell("A2").Style.Font.Bold = true;
                            ws.Cell("B2").Value = Msg.Translate(ctx, "Account_ID");
                            ws.Cell("B2").Style.Font.Bold = true;
                            ws.Cell("C2").Value = Msg.Translate(ctx, "AMTACCTDR");
                            ws.Cell("C2").Style.Font.Bold = true;
                            ws.Cell("D2").Value = Msg.Translate(ctx, "AMTACCTCR");
                            ws.Cell("D2").Style.Font.Bold = true;
                            for (int i = 0; i < elementList.Count; i++)
                            {
                                ws.Cell(2, i + 5).Value = Msg.Translate(ctx, elementList[i]["ColumnName"]);
                                ws.Cell(2, i + 5).Style.Font.Bold = true;
                            }
                            #endregion

                            // Adding DataRows.
                            for (int i = 0; i < ds.Tables[0].Rows.Count; i++)
                            {
                                ws.Cell(i + 3, 1).Value = ds.Tables[0].Rows[i]["AD_Org_ID"];
                                ws.Cell(i + 3, 2).Value = ds.Tables[0].Rows[i]["ACCOUNT_ID"];
                                ws.Cell(i + 3, 3).Value = ds.Tables[0].Rows[i]["ACCCREDIT"];
                                ws.Cell(i + 3, 4).Value = ds.Tables[0].Rows[i]["ACCDEBIT"];
                                for (int k = 0; k < elementList.Count; k++)
                                {
                                    if (elementList[k]["ElementType"].StartsWith("X"))
                                    {
                                        ws.Cell(i + 3, k + 5).Value = ds.Tables[0].Rows[i][elementList[k]["ElementType"]];
                                    }
                                    else
                                    {
                                        ws.Cell(i + 3, k + 5).Value = ds.Tables[0].Rows[i][elementList[k]["ColumnName"]];
                                    }
                                }
                            }
                        }
                        //Column width adjustment as per data
                        ws.Columns().AdjustToContents();
                    }
                }
                using (MemoryStream stream = new MemoryStream())
                {
                    wb.SaveAs(stream);
                    return stream;
                }
            }
            #endregion
        }

        #endregion

        /// <summary>
        /// Get Account Element defined on Accounting Schema
        /// </summary>
        /// <param name="C_AcctSchema_ID">Accounting Schema ID</param>
        /// <returns>List of Eccount Elements inculding type and columnName</returns>
        /// <writer>Amit</writer>
        public static List<Dictionary<string, string>> GetElementType(int C_AcctSchema_ID)
        {
            List<Dictionary<string, string>> obj = null;
            string _sql = @" SELECT a.ElementType,  
                             case  when a.ElementType = 'BP' then 'C_BPartner_ID' 
                                   when a.ElementType = 'AY' then 'C_Activity_ID'
                                   when a.ElementType = 'MC' then 'C_Campaign_ID'
                                   when a.ElementType = 'OT' then 'AD_OrgTrx_ID'
                                   when a.ElementType = 'PJ' then 'C_Project_ID'
                                   when a.ElementType = 'PR' then 'M_Product_ID'
                                   when a.ElementType = 'SR' then 'C_SalesRegion_ID'
                                   when a.ElementType = 'U1' then 'User1_ID'
                                   when a.ElementType = 'U2' then 'User2_ID'
                                else cast(col.ColumnName as varchar(100)) end as ColumnName FROM C_AcctSchema_Element a"
                        + " LEFT JOIN AD_Column col ON a.AD_Column_ID = col.AD_Column_ID WHERE a.C_AcctSchema_ID = "
                        + C_AcctSchema_ID + " AND a.IsActive='Y' AND a.ElementType NOT IN ('OO' , 'AC','LF','LT') ORDER BY a.SeqNo";
            DataSet _ds = DB.ExecuteDataset(_sql);
            if (_ds != null && _ds.Tables[0].Rows.Count > 0)
            {
                obj = new List<Dictionary<string, string>>();
                for (int i = 0; i < _ds.Tables[0].Rows.Count; i++)
                {
                    Dictionary<string, string> kp = new Dictionary<string, string>();
                    kp["ElementType"] = Util.GetValueOfString(_ds.Tables[0].Rows[i]["ElementType"]);
                    kp["ColumnName"] = Util.GetValueOfString(_ds.Tables[0].Rows[i]["ColumnName"]);
                    obj.Add(kp);
                }
            }
            return obj;
        }

        /// <summary>
        /// Get Name and Values of Dimesnion
        /// </summary>
        /// <param name="fl">fact Line</param>
        /// <param name="elementList">Account element dimension</param>
        /// <returns>Query to be created based on fact lines and account element</returns>
        /// <writer>Amit</writer>
        public static StringBuilder GetDataName(List<Dictionary<string, string>> elementList, int Ad_Table_Id, int Record_Id, int AccSchemaId)
        {
            StringBuilder sql = new StringBuilder();
            string columnName = "";
            Dictionary<string, string> _elementlist = null;
            sql.Append(@"SELECT (SELECT NVL(AD_Org.Name,'') FROM AD_Org WHERE AD_Org_ID=fl.AD_Org_ID
                         ) AS AD_Org_ID , " +
                        "(SELECT NVL(C_ElementValue.Value,'') || ' - '  || NVL(C_ElementValue.Name, '')  FROM C_ElementValue" +
                        " WHERE C_ElementValue_ID =fl.Account_ID) AS Account_ID,fl.AMTACCTCR AS AccCredit,fl.AMTACCTDR AS ACCDebit");
            for (int k = 0; k < elementList.Count; k++)
            {
                _elementlist = elementList[k];
                columnName = Util.GetValueOfString(_elementlist["ColumnName"]);
                if (Util.GetValueOfString(_elementlist["ElementType"]).Equals("X1"))
                {
                    sql.Append(" , GetGLDimensionValue(NVL(fl.UserElement1_ID,0),'" + columnName.Substring(0, columnName.Length - 3) + "') AS X1 ");
                }
                else if (Util.GetValueOfString(_elementlist["ElementType"]).Equals("X2"))
                {
                    sql.Append(" ,  GetGLDimensionValue(NVL(fl.UserElement2_ID,0),'" + columnName.Substring(0, columnName.Length - 3) + "') AS X2 ");
                }
                else if (Util.GetValueOfString(_elementlist["ElementType"]).Equals("X3"))
                {
                    sql.Append(" ,GetGLDimensionValue(NVL(fl.UserElement3_ID,0),'" + columnName.Substring(0, columnName.Length - 3) + "') AS X3 ");
                }
                else if (Util.GetValueOfString(_elementlist["ElementType"]).Equals("X4"))
                {
                    sql.Append(" ,GetGLDimensionValue(NVL(fl.UserElement4_ID,0),'" + columnName.Substring(0, columnName.Length - 3) + "') AS X4 ");
                }
                else if (Util.GetValueOfString(_elementlist["ElementType"]).Equals("X5"))
                {
                    sql.Append(" ,GetGLDimensionValue(NVL(fl.UserElement5_ID,0),'" + columnName.Substring(0, columnName.Length - 3) + "') AS X5 ");
                }
                else if (Util.GetValueOfString(_elementlist["ElementType"]).Equals("X6"))
                {
                    sql.Append(" , GetGLDimensionValue(NVL(fl.UserElement6_ID,0),'" + columnName.Substring(0, columnName.Length - 3) + "') AS X6 ");
                }
                else if (Util.GetValueOfString(_elementlist["ElementType"]).Equals("X7"))
                {
                    sql.Append(" ,  GetGLDimensionValue(NVL(fl.UserElement7_ID,0),'" + columnName.Substring(0, columnName.Length - 3) + "') AS X7 ");
                }
                else if (Util.GetValueOfString(_elementlist["ElementType"]).Equals("X8"))
                {
                    sql.Append(" , GetGLDimensionValue(NVL(fl.UserElement8_ID,0),'" + columnName.Substring(0, columnName.Length - 3) + "') AS X8 ");
                }
                else if (Util.GetValueOfString(_elementlist["ElementType"]).Equals("X9"))
                {
                    sql.Append(" , GetGLDimensionValue(NVL(fl.UserElement9_ID,0),'" + columnName.Substring(0, columnName.Length - 3) + "') AS X9 ");
                }
                else if (Util.GetValueOfString(_elementlist["ElementType"]).Equals("OT"))
                {
                    sql.Append(" , GetGLDimensionValue(NVL(fl.AD_OrgTrx_ID,0), '" + columnName.Substring(0, columnName.Length - 6) + "') AS AD_OrgTrx_ID ");
                }
                else if (Util.GetValueOfString(_elementlist["ElementType"]).Equals("U1"))
                {
                    sql.Append(" , (SELECT NVL(C_ElementValue.Value,'') || ' - '  || NVL(C_ElementValue.Name, '')  FROM C_ElementValue" +
                       " WHERE C_ElementValue_ID = fl.User1_ID) AS User1_ID ");
                }
                else if (Util.GetValueOfString(_elementlist["ElementType"]).Equals("U2"))
                {
                    sql.Append(" , (SELECT NVL(C_ElementValue.Value,'') || ' - '  || NVL(C_ElementValue.Name, '')  FROM C_ElementValue" +
                       " WHERE C_ElementValue_ID =fl.User2_ID) AS User2_ID ");
                }
                else
                {
                    sql.Append(" , GetGLDimensionValue(NVL(fl." + columnName + ",0),'" + columnName.Substring(0, columnName.Length - 3) + "') AS " + columnName + " ");
                }
            }
            sql.Append(" FROM fact_acct fl WHERE Ad_Table_Id=" + Ad_Table_Id + " AND Record_ID=" + Record_Id + " AND C_ACCTSCHEMA_ID=" + AccSchemaId + " ");
            return sql;
        }
    }
}
