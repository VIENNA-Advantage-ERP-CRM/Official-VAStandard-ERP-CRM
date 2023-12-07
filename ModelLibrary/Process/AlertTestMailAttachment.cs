/********************************************************
 * Project Name   : ViennaAdvantage
 * Class Name     : AlertTestMailAttachment
 * Purpose        : Create excel of alert sql result and send to login user. 
 * Class Used     : SvrProcess
 * Chronological  : Development
 * VAI055         : 05-Dec-2023
  ******************************************************/
using System;
using ClosedXML.Excel;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using VAdvantage.Utility;
using VAdvantage.DataBase;
using VAdvantage.Logging;
using System.Data;
using VAdvantage.Classes;
using System.Runtime.InteropServices;
using System.Net;
using VAdvantage.ProcessEngine;
using VAdvantage.Model;
using System.IO;
using System.Runtime.CompilerServices;

namespace ViennaAdvantage.Process
{ 
    public class AlertTestMailAttachment : SvrProcess
    {
        string userName =null;
        string userEmail = null;   
        int AD_User_ID = 0;  
        private StringBuilder m_summary = new StringBuilder();
        /**	Last Error Msg				*/
        private StringBuilder m_errors = new StringBuilder();
        /** Client info					*/
        private MClient m_client = null;

        /// <summary>
        /// Getting alerts to find sql result
        /// </summary>
        /// <returns>Message</returns>
        protected override string DoIt()
        {
            string sql = "";
            string msg = "";
            m_summary = new StringBuilder();
            m_errors = new StringBuilder();
            AD_User_ID = GetCtx().GetAD_User_ID();
            MAlert alert = new MAlert(GetCtx(), GetRecord_ID(), Get_Trx());
            StringBuilder message = new StringBuilder(alert.GetAlertMessage()).Append(Env.NL);
            List<FileInfo> attachments = new List<FileInfo>();
            MAlertRule[] rules = alert.GetRules(false);
            if (rules.Length > 0)
            {
                for (int i = 0; i < rules.Length; i++)
                {
                    message.Append(Env.NL);
                    MAlertRule rule = rules[i];
                    sql = rule.GetPreProcessing();
                    sql = rule.GetSql();
                    if (sql != null && sql.Length > 0)
                    {
                        if (!ValidateSql(sql))
                        {
                            rule.SetErrorMsg("Pre= Potential dangerous query");
                            m_errors.Append("Pre= Potential dangerous query");
                            rule.SetIsValid(false);
                            rule.Save();
                            return "Potential dangerous query";
                        }

                        int no = DB.ExecuteQuery(sql);
                        if (no == -1)
                        {
                            msg = " No sql result found";
                        }
                    }
                    GetResult(alert, rule, sql, attachments, message);
                }
                return Msg.GetMsg(GetCtx(), "MailSent") + msg;
            }
            return Msg.GetMsg(GetCtx(), "VAS_AlertRuleRequired");
        }

        /// <summary>
        /// Create excel file
        /// </summary>
        /// <param name="data">sql Result</param>
        /// <returns>file</returns>
        private FileInfo CreateExcelFile(DataTable data)
        {
            try
            {
                Random rndm = new Random();
                string path = "Alert_" + DateTime.Now.Ticks + "_" + rndm.Next(0, 9999);
                string filePath = GlobalVariable.PhysicalPath + "TempDownload\\";
                if (!Directory.Exists(filePath))
                    Directory.CreateDirectory(filePath);
                log.Log(Level.INFO, "AlertProcessor=> Create Directory in CreateExcelFile");
                string fileName = filePath + path + ".xlsx";
                using (XLWorkbook wb = new XLWorkbook())
                {
                    IXLWorksheet worksheet = wb.Worksheets.Add("AlertSheet");
                    for (int i = 0; i < data.Columns.Count; i++)
                    {
                        worksheet.Cell(1, i + 1).Value = data.Columns[i].ColumnName;
                    }
                    for (int r = 0; r < data.Rows.Count; r++)
                    {
                        for (int c = 0; c < data.Columns.Count; c++)
                        {
                            object cellValue = data.Rows[r][c] ?? ""; 
                            worksheet.Cell(r + 2, c + 1).Value = cellValue.ToString(); 
                        }
                    }
                    wb.SaveAs(fileName);
                }
                log.Log(Level.INFO, "AlertProcessor=> Create Exporter.export in CreateExcelFile");
                FileInfo fInfo = new FileInfo(fileName);
                log.Log(Level.INFO, "AlertProcessor=> Create new File Info in CreateExcelFile");
                return fInfo;
            }
            catch (Exception e)
            {
                log.Log(Level.SEVERE, "AlertProcessor=>Error creating File in TempDownload in Excel on CreateExcelFile", e);
                return null;
            }
        }

        /// <summary>
        /// Getting query result
        /// </summary>
        /// <param name="sql">Sql Query</param>
        /// <param name="trxName">Transaction Name</param>
        /// <returns>Query data</returns>
        private DataTable GetData(String sql, Trx trxName)
        {
            DataTable data = new DataTable();
            IDataReader rs = null;
            Exception error = null;
            try
            {
                if (!ValidateSql(sql))
                {
                    throw new Exception("Potential dangerous SQL query");
                }

                rs = DB.ExecuteReader(sql, null, trxName);
                bool isFirstRow = true;
                while (rs.Read())
                {
                    if (isFirstRow)
                    {
                        for (int col = 0; col < rs.FieldCount; col++)
                        {
                            string columnName = rs.GetName(col);
                            data.Columns.Add(columnName);
                        }
                        isFirstRow = false;
                    }
                    DataRow row = data.NewRow();
                    for (int col = 0; col < rs.FieldCount; col++)
                    {
                        row[col] = rs[col];
                    }
                    data.Rows.Add(row);
                }
            }
            catch (Exception e)
            {
                log.Log(Level.SEVERE, sql, e);
                error = e;
            }
            finally
            {
                if (rs != null)
                {
                    rs.Close();
                }
                rs = null;
            }

            //	Error occurred
            if (error != null)
                throw new Exception("(" + sql + ") " + Env.NL
                    + error.Message);
            return data;
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
            sql = sql.ToUpper();

            if (sql.IndexOf("UPDATE") > -1 || sql.IndexOf("DELETE") > -1 || sql.IndexOf("DROP") > -1
                || sql.IndexOf("TRUNCATE") > -1)
            {
                return false;
            }
            return true;
        }

        /// <summary>
        /// Geting result for alerts to create excel.
        /// </summary>
        /// <param name="alert">Object</param>
        /// <param name="rule">Object</param>
        /// <param name="sql">Sql Query</param>
        /// <param name="attachments">Sql result file</param>
        /// <param name="message">Final message for mail</param>
        /// <returns>true/false</returns>
        private bool GetResult(MAlert alert, MAlertRule rule, String sql, List<FileInfo> attachments, StringBuilder message)
        {
            bool valid = true;
            StringBuilder finalMsg = new StringBuilder(message.ToString());        
            SimpleDateFormat df = DisplayType.GetDateFormat(DisplayType.DateTime);
            finalMsg.Append("\n\n" + Msg.Translate(GetCtx(), "Date") + (" : ") + (df.Format(DateTime.Now)));
            try
            {
                attachments.Clear();
                String text = "";
                text = GetExcelReport(rule, sql, null, attachments);
                if (string.IsNullOrEmpty(text))
                {
                    text = ListSqlSelect(sql);
                }
                if (text != null && text.Length > 0)
                {
                    finalMsg.Append("\n\n" + text);
                    int index = text.IndexOf(":");
                    if (index > 0 && index < 5)
                       m_summary.Append(text.Substring(0, index));
                }
            }
            catch (Exception e)
            {
                rule.SetErrorMsg("Select=" + e.Message);
                m_errors.Append("Select=" + e.Message);
                rule.SetIsValid(false);
                rule.Save();
                valid = false;
            }
            SendInfo(AD_User_ID, alert.GetAlertSubject(), finalMsg.ToString(), attachments);
            return valid;
        }

        /// <summary>
        /// Sending mail to login User
        /// </summary>
        /// <param name="AD_User_ID">AD_User_ID</param>
        /// <param name="subject">Mail Subject</param>
        /// <param name="message">Mail Content</param>
        /// <param name="attachments">Excel file</param>
        /// <returns>void</returns>
        private void SendInfo(int AD_User_ID, string subject, string message, List<FileInfo> attachments)
        {           
            m_client = MClient.Get(GetCtx(), GetCtx().GetAD_Client_ID());
            MUser user = MUser.Get(GetCtx(), AD_User_ID);
            userName = user.GetName();
            userEmail = user.GetEMail();
            if (userName!=null && userEmail!=null)
            {
                EMail email = m_client.CreateEMail(userEmail, userName, subject, message, false);
                if (email != null)
                { 
                    foreach (FileInfo f in attachments)
                    {
                        email.AddAttachment(f);
                    }
                    email.Send();
                }
            }
        }

        /// <summary>
        /// Get Excel File to send mail
        /// </summary>
        /// <param name="rule">MAlertRule object </param>
        /// <param name="sql">Sql Query</param>
        /// <param name="trxName">Transaction Name</param>
        /// <param name="attachments">Excel file</param>
        /// <returns></returns>
        private String GetExcelReport(MAlertRule rule, String sql, Trx trxName, List<FileInfo> attachments)
        {
            DataTable data = GetData(sql, trxName);
            if (data == null)
            {
                log.Log(Level.SEVERE, "AlertProcessor=>Error executing sql on GetExcelReport");
                return null;
            }
            try
            {
                log.Log(Level.INFO, "AlertProcessor=> File to CreateExcelFile");
                FileInfo fInfo = CreateExcelFile(data);
                log.Log(Level.INFO, "AlertProcessor=>File to Attachments");
                attachments.Add(fInfo);
                String msg = rule.GetName() + " (@SeeAttachment@ " + fInfo.Name + ")" + Env.NL;
                log.Log(Level.INFO, "AlertProcessor=> " + msg);
                return Msg.ParseTranslation(GetCtx(), msg);
            }
            catch
            {
                log.Log(Level.SEVERE, "AlertProcessor=>Error writing data in Excel on GetExcelReport");
                return null;

            }
        }

        /// <summary>
        /// Getting list of Sql result
        /// </summary>
        /// <param name="sql">Sql query</param>
        /// <returns>Message</returns>
        private String ListSqlSelect(String sql)
        {
            StringBuilder result = new StringBuilder();
            Exception error = null;
            int count = 0;
            try
            {
                IDataReader dr = DB.ExecuteReader(sql);
                while (dr.Read())
                {
                    result.Append("------------------").Append(Env.NL);
                    for (int col = 0; col <= dr.FieldCount - 1; col++)
                    {
                        result.Append(dr.GetName(col)).Append(" = ");
                        result.Append(dr[col].ToString());
                        result.Append(Env.NL);
                    }
                    count++;
                }
                dr.Close();
                if (result.Length == 0)
                    log.Fine("No rows selected");
            }
            catch (Exception e)
            {
                if (DB.IsOracle() || sql.IndexOf(" DBA_Free_Space") == -1)
                {
                    log.Log(Level.SEVERE, sql, e);
                    error = e;
                }
                else
                {
                    log.Log(Level.WARNING, sql, e);
                }
            }
            if (error != null)
                throw new Exception("(" + sql + ") " + Env.NL
                    + error.Message);

            if (count > 0)
                result.Insert(0, "#" + count + ": ");
            return result.ToString();
        }

        protected override void Prepare()
        {

        }
    }
}
