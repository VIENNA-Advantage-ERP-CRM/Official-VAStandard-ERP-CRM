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
                    if (j == 1)
                        elementList = GetElementType(Util.GetValueOfInt(dsFact.Tables[0].Rows[j-1]["C_ACCTSCHEMA_ID"]));
                    sql.Append(GetDataName(elementList, Ad_Table_Id, Record_Id, Util.GetValueOfInt(dsFact.Tables[0].Rows[j-1]["C_ACCTSCHEMA_ID"])));
                    DataSet ds = DB.ExecuteDataset(sql.ToString());
                    DataTable dt = new DataTable();
                    //Setting Table Name  
                    dt.TableName = Util.GetValueOfString(dsFact.Tables[0].Rows[j-1]["Name"]);
                    if (ds != null && ds.Tables[0].Rows.Count > 0)
                    {
                        //Add Columns  
                        dt.Columns.Add("ad_org_id", typeof(string));
                        dt.Columns.Add("account_id", typeof(string));
                        dt.Columns.Add("AMTACCTDR", typeof(string));
                        dt.Columns.Add("AMTACCTCR", typeof(string));
                        dt.Columns.Add("m_product_id", typeof(string));
                        dt.Columns.Add("C_BPARTNER_ID", typeof(string));
                        dt.Columns.Add("userelement8_id", typeof(string));
                        dt.Columns.Add("userelement1_id", typeof(string));
                        dt.Columns.Add("userelement4_id", typeof(string));
                        dt.Columns.Add("userelement2_id", typeof(string));
                        dt.Columns.Add("c_activity_id", typeof(string));
                        dt.Columns.Add("ad_orgtrx_id", typeof(string));
                        dt.Columns.Add("c_project_id", typeof(string));
                        dt.Columns.Add("c_campaign_id", typeof(string));
                        dt.Columns.Add("userelement9_id", typeof(string));
                        dt.Columns.Add("userelement3_id", typeof(string));
                        dt.Columns.Add("userelement5_id", typeof(string));
                        dt.Columns.Add("userelement6_id", typeof(string));
                        dt.Columns.Add("userelement7_id", typeof(string));

                        //add data
                        for (int i = 0; i < ds.Tables[0].Rows.Count; i++)
                        {
                            dt.Rows.Add(new Object[]{
                             ds.Tables[0].Rows[i]["AD_ORG_ID"],
                             ds.Tables[0].Rows[i]["account_id"],
                             ds.Tables[0].Rows[i]["ACCDEBIT"],
                             ds.Tables[0].Rows[i]["ACCCREDIT"],
                             ds.Tables[0].Rows[i]["m_product_id"],
                             ds.Tables[0].Rows[i]["C_BPARTNER_ID"],
                             ds.Tables[0].Rows[i]["userelement8_id"],
                             ds.Tables[0].Rows[i]["userelement1_id"],
                             ds.Tables[0].Rows[i]["userelement4_id"],
                             ds.Tables[0].Rows[i]["userelement2_id"],
                             ds.Tables[0].Rows[i]["c_activity_id"],
                             ds.Tables[0].Rows[i]["ad_orgtrx_id"],
                             ds.Tables[0].Rows[i]["c_project_id"],
                             ds.Tables[0].Rows[i]["c_campaign_id"],
                             ds.Tables[0].Rows[i]["userelement9_id"],
                             ds.Tables[0].Rows[i]["userelement3_id"],
                             ds.Tables[0].Rows[i]["userelement5_id"],
                             ds.Tables[0].Rows[i]["userelement6_id"],
                             ds.Tables[0].Rows[i]["userelement7_id"]
                             });
                        }
                        dt.AcceptChanges();
                        #endregion

                        #region CreateExcel
                        // Set AccountSchema Name as Sheet Name 
                        Regex pattern = new Regex("[/*?:\r]|[-]");//Added Special Characters which will be Replaced from string if exists
                        var ws = wb.Worksheets.Add(pattern.Replace(Util.GetValueOfString(dsFact.Tables[0].Rows[j - 1]["Name"]),"-"));
                        //Set Excel file header
                        ws.Cell("A1").Value = Util.GetValueOfString(dsFact.Tables[0].Rows[j-1]["Name"]);
                        var range = ws.Range("A1:L1");
                        range.Merge().Style.Font.SetBold().Font.FontSize = 12;
                        ws.Cell("A1").Style.Alignment.SetHorizontal(XLAlignmentHorizontalValues.Center);

                        if (dt.Rows.Count > 0)
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
                            ws.Cell("E2").Value = Msg.Translate(ctx, "M_Product_ID");
                            ws.Cell("E2").Style.Font.Bold = true;
                            ws.Cell("F2").Value = Msg.Translate(ctx, "C_BPARTNER_ID");
                            ws.Cell("F2").Style.Font.Bold = true;
                            ws.Cell("G2").Value = Msg.GetMsg(ctx, "UserElement8_ID");
                            ws.Cell("G2").Style.Font.Bold = true;
                            ws.Cell("H2").Value = Msg.GetMsg(ctx, "UserElement1_ID");
                            ws.Cell("H2").Style.Font.Bold = true;

                            // Sender Detail
                            ws.Cell("I2").Value = Msg.GetMsg(ctx, "UserElement4_ID");
                            ws.Cell("I2").Style.Font.Bold = true;
                            ws.Cell("J2").Value = Msg.GetMsg(ctx, "UserElement2_ID");
                            ws.Cell("J2").Style.Font.Bold = true;
                            ws.Cell("K2").Value = Msg.Translate(ctx, "C_Activity_ID");
                            ws.Cell("K2").Style.Font.Bold = true;
                            ws.Cell("L2").Value = Msg.Translate(ctx, "AD_Orgtrx_ID");
                            ws.Cell("L2").Style.Font.Bold = true;
                            ws.Cell("M2").Value = Msg.Translate(ctx, "C_Project_ID");
                            ws.Cell("M2").Style.Font.Bold = true;

                            // Receiver Detail
                            ws.Cell("N2").Value = Msg.Translate(ctx, "C_Campaign_ID");
                            ws.Cell("N2").Style.Font.Bold = true;
                            ws.Cell("O2").Value = Msg.GetMsg(ctx, "UserElement9_ID");
                            ws.Cell("O2").Style.Font.Bold = true;
                            ws.Cell("P2").Value = Msg.GetMsg(ctx, "UserElement3_ID");
                            ws.Cell("P2").Style.Font.Bold = true;
                            ws.Cell("Q2").Value = Msg.GetMsg(ctx, "UserElement5_ID");
                            ws.Cell("Q2").Style.Font.Bold = true;
                            ws.Cell("R2").Value = Msg.GetMsg(ctx, "UserElement6_ID");
                            ws.Cell("R2").Style.Font.Bold = true;
                            ws.Cell("S2").Value = Msg.GetMsg(ctx, "UserElement7_ID");
                            ws.Cell("S2").Style.Font.Bold = true;
                            #endregion

                            // Adding DataRows.
                            for (int i = 0; i < dt.Rows.Count; i++)
                            {
                                // Parameter Detail
                                ws.Cell("A" + (i + 3)).Value = dt.Rows[i]["AD_Org_ID"];
                                ws.Cell("B" + (i + 3)).Value = dt.Rows[i]["account_id"];
                                ws.Cell("C" + (i + 3)).Value = dt.Rows[i]["AMTACCTDR"];
                                ws.Cell("D" + (i + 3)).Value = dt.Rows[i]["AMTACCTCR"];
                                ws.Cell("E" + (i + 3)).Value = dt.Rows[i]["m_product_id"];
                                ws.Cell("F" + (i + 3)).Value = dt.Rows[i]["C_BPARTNER_ID"];
                                ws.Cell("G" + (i + 3)).Value = dt.Rows[i]["userelement8_id"];
                                ws.Cell("H" + (i + 3)).Value = dt.Rows[i]["userelement1_id"];
                                ws.Cell("I" + (i + 3)).Value = dt.Rows[i]["userelement4_id"];
                                ws.Cell("J" + (i + 3)).Value = dt.Rows[i]["userelement2_id"];
                                ws.Cell("K" + (i + 3)).Value = dt.Rows[i]["c_activity_id"];
                                ws.Cell("L" + (i + 3)).Value = dt.Rows[i]["ad_orgtrx_id"];
                                ws.Cell("M" + (i + 3)).Value = dt.Rows[i]["c_project_id"];
                                ws.Cell("N" + (i + 3)).Value = dt.Rows[i]["c_campaign_id"];
                                ws.Cell("O" + (i + 3)).Value = dt.Rows[i]["userelement9_id"];
                                ws.Cell("P" + (i + 3)).Value = dt.Rows[i]["userelement3_id"];
                                ws.Cell("Q" + (i + 3)).Value = dt.Rows[i]["userelement5_id"];
                                ws.Cell("R" + (i + 3)).Value = dt.Rows[i]["userelement6_id"];
                                ws.Cell("S" + (i + 3)).Value = dt.Rows[i]["userelement7_id"];
                            }
                        }
                        //Column width adjustment as per data
                        ws.Column(1).AdjustToContents();
                        ws.Column(2).AdjustToContents();
                        ws.Column(3).AdjustToContents();
                        ws.Column(4).AdjustToContents();
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
                    sql.Append(" , GetGLDimensionValue(NVL(fl.UserElement1_ID,0),'" + columnName.Substring(0, columnName.Length - 3) + "') AS UserElement1_ID ");
                }
                else if (Util.GetValueOfString(_elementlist["ElementType"]).Equals("X2"))
                {
                    sql.Append(" ,  GetGLDimensionValue(NVL(fl.UserElement2_ID,0),'" + columnName.Substring(0, columnName.Length - 3) + "') AS UserElement2_ID ");
                }
                else if (Util.GetValueOfString(_elementlist["ElementType"]).Equals("X3"))
                {
                    sql.Append(" ,GetGLDimensionValue(NVL(fl.UserElement3_ID,0),'" + columnName.Substring(0, columnName.Length - 3) + "') AS UserElement3_ID ");
                }
                else if (Util.GetValueOfString(_elementlist["ElementType"]).Equals("X4"))
                {
                    sql.Append(" ,GetGLDimensionValue(NVL(fl.UserElement4_ID,0),'" + columnName.Substring(0, columnName.Length - 3) + "') AS UserElement4_ID ");
                }
                else if (Util.GetValueOfString(_elementlist["ElementType"]).Equals("X5"))
                {
                    sql.Append(" ,GetGLDimensionValue(NVL(fl.UserElement5_ID,0),'" + columnName.Substring(0, columnName.Length - 3) + "') AS UserElement5_ID ");
                }
                else if (Util.GetValueOfString(_elementlist["ElementType"]).Equals("X6"))
                {
                    sql.Append(" , GetGLDimensionValue(NVL(fl.UserElement6_ID,0),'" + columnName.Substring(0, columnName.Length - 3) + "') AS UserElement6_ID ");
                }
                else if (Util.GetValueOfString(_elementlist["ElementType"]).Equals("X7"))
                {
                    sql.Append(" ,  GetGLDimensionValue(NVL(fl.UserElement7_ID,0),'" + columnName.Substring(0, columnName.Length - 3) + "') AS UserElement7_ID ");
                }
                else if (Util.GetValueOfString(_elementlist["ElementType"]).Equals("X8"))
                {
                    sql.Append(" , GetGLDimensionValue(NVL(fl.UserElement8_ID,0),'" + columnName.Substring(0, columnName.Length - 3) + "') AS UserElement8_ID ");
                }
                else if (Util.GetValueOfString(_elementlist["ElementType"]).Equals("X9"))
                {
                    sql.Append(" , GetGLDimensionValue(NVL(fl.UserElement9_ID,0),'" + columnName.Substring(0, columnName.Length - 3) + "') AS UserElement9_ID ");
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
