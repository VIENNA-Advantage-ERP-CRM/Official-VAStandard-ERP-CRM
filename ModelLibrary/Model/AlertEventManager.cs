/********************************************************
 * Project Name   : Model Library
 * Class Name     : AlertEventManager
 * Purpose        : 
 * Chronological    Development
 * Ruby           : 17 Sep 2025
  ******************************************************/
using System;
using System.Collections.Generic;
using VAdvantage.DataBase;
using VAdvantage.Model;
using System.Data;
using VAdvantage.Logging;
using System.IO;
using System.Linq;
using VAdvantage.Utility;

namespace VAdvantage.Alert
{
    public class AlertEventManager : AlertEventMgr
    {
        #region Private variable
        //	Document Workflow Manager		
        private static AlertEventManager _mgr = null;
        private int tableID = 0;
        //	Logger			
        private static VLogger log = VLogger.GetVLogger(typeof(AlertEventManager).FullName);
        #endregion

        /// <summary>
        ///Get Document Workflow Manager
        /// </summary>
        /// <returns>mgr</returns>
        public static AlertEventManager Get()
        {
            if (_mgr == null)
                _mgr = new AlertEventManager();
            return _mgr;
        }

        //	Set PO Workflow Manager
        //static 
        //{
        //    PO.SetDocWorkflowMgr(Get());
        //}
        static AlertEventManager()
        {
            PO.SetAlertEventMgr(Get());
        }


        /// <summary>
        /// Doc Workflow Manager
        /// </summary>
        private AlertEventManager() : base()
        {
            if (_mgr == null)
                _mgr = this;
        }

        /// <summary>
        /// Alert Event Process 
        /// </summary>
        /// <param name="document">document</param>
        /// <param name="POInfo">POInfo</param>
        /// <param name="eventType">eventType</param>
        /// <returns></returns>
        public bool Process(PO document, POInfo pinfo, string eventType)
        {
            bool started = false;
            tableID = pinfo.getAD_Table_ID();
            if (tableID == 0) {
                return false;
            }
            Ctx ctx = document.GetCtx();
            MAlert[] alerts = MAlert.GetAlertValue(ctx, document.GetAD_Client_ID(), tableID);
            if (alerts == null || alerts.Length == 0)
                return false;
            for (int i = 0; i < alerts.Length; i++)
            {
                MAlert alert = alerts[i];
                started = AlertRuleActivity(alert,document, pinfo,  eventType);
            }
            return started;
        }
        /// <summary>
        /// Getting alert rule
        /// </summary>
        /// <param name="alert">alert</param>
        /// <param name="document">PO</param>
        /// <param name="POInfo">PO Info</param>
        /// <param name="eventtype">eventtype</param>
        /// <returns></returns>

        public bool AlertRuleActivity(MAlert alert,PO document, POInfo pinfo, string eventType)
        {
            try
            {
                MAlertRule[] rules = alert.GetRule(false);
                List<RuleDetail> ruleDetails = new List<RuleDetail>();
                for (int i = 0; i < rules.Length; i++)
                {
                    MAlertRule rule = rules[i];
                    if (!rule.IsValid())
                        continue;

                    int ruleTableID = Util.GetValueOfInt(rule.GetAD_Table_ID());
                    if (tableID != ruleTableID)
                        continue;

                    string ruleColumnIDs = Util.GetValueOfString(rule.Get_Value("AD_Column_ID"));
                    string[] parts = ruleColumnIDs.Split(',');
                    List<int> colIdList = new List<int>();
                    for (int j = 0; j < parts.Length; j++)
                    {
                        int val = Util.GetValueOfInt(parts[j].Trim());
                        if (val > 0)
                            colIdList.Add(val);
                    }

                    RuleDetail detail = new RuleDetail();
                    detail.TableId = ruleTableID;
                    detail.ColumnIds = colIdList;
                    detail.IsInsert = Util.GetValueOfBool(rule.Get_Value("IsInsert"));
                    detail.IsUpdate = Util.GetValueOfBool(rule.Get_Value("IsUpdate"));
                    detail.IsDeleted = Util.GetValueOfBool(rule.Get_Value("IsDeleted"));
                    ruleDetails.Add(detail);
                }

                if (ruleDetails.Count == 0)
                {
                    log.Severe("Rule Not Found");
                    return false;
                }

                MAlertRecipient[] recipients = alert.GetRecipients(false);
                for (int i = 0; i < recipients.Length; i++)
                {
                    for (int j = 0; j < ruleDetails.Count; j++)
                    {
                        EventAlertProcessing(recipients[i], ruleDetails[j], document,pinfo,  eventType);
                    }
                }
            }
            catch (Exception e)
            {
                log.Log(Level.SEVERE, "", e);
                return false;
            }
            return true;
        }

        /// <summary>
        /// Getting data
        /// </summary>
        /// <param name="ctx">context</param>
        /// <param name="recipient">recipient</param>
        /// <param name="rule">rule</param>
        /// <param name="document">PO</param>
        /// <returns></returns>
        public bool EventAlertProcessing(MAlertRecipient recipient, RuleDetail rule, PO document, POInfo pinfo, string eventType)
        {
            string windowName = "";
            string tabName = "";
            string subject = "";
            string msg = "";
            List<List<object>> data = new List<List<object>>();
            List<object> header = new List<object>();
            FileInfo attachment = null;
            eventType = eventType.ToUpper();
            if (eventType.Equals("DELETE"))
            {
                string query = @"SELECT AD_Tab.Name AS TabName, AD_Window.Name AS WindowName FROM AD_Tab INNER JOIN AD_Window ON AD_Window.AD_WINDOW_ID = AD_Tab.AD_Window_ID  ";
                if (document.GetTableName().Equals("GL_Journal"))
                {
                    query += " WHERE AD_Window.Name = " +
                        "CASE WHEN(SELECT NVL(GL_JournalBatch_ID, 0) FROM GL_Journal WHERE GL_Journal_ID = " + document.Get_ID() + ") = 0 " +
                        "THEN 'GL Journal Line' ELSE 'GL Journal' END AND AD_Tab.AD_Table_ID = " + tableID;
                }
                else
                {
                    query += " WHERE AD_Tab.AD_Table_ID = " + tableID;
                }
                DataSet ds = DB.ExecuteDataset(query);
                if (ds != null && ds.Tables.Count > 0 && ds.Tables[0].Rows.Count > 0)
                {
                    windowName = Util.GetValueOfString(ds.Tables[0].Rows[0]["WindowName"]);
                    tabName = Util.GetValueOfString(ds.Tables[0].Rows[0]["TabName"]);
                }
            }
            else
            {
                windowName = Util.GetValueOfString(DB.ExecuteScalar(
                    "SELECT Name FROM AD_Window WHERE AD_Window_ID=" + document.GetAD_Window_ID()));
                tabName = Util.GetValueOfString(DB.ExecuteScalar(
                    "SELECT Name FROM AD_Tab WHERE AD_Tab_ID=" + document.GetWindowTabID()));
            }

            if (eventType.Equals("INSERT") && rule.IsInsert)
            {
                subject = "New Record Created Notification - " + windowName;
                msg = "Hello Team,\n\nA new record has been created\n\n"
                    + "Window: " + windowName + "\nTab: " + tabName
                    + "\nRecord ID: " + document.Get_ID();

                header.Add("Field Name");
                header.Add("Value");
                data.Add(header);

                for (int i = 0; i < document.Get_ColumnCount(); i++)
                {
                    List<object> row = new List<object>();
                    row.Add(document.Get_ColumnName(i));
                    row.Add(document.Get_ValueOld(i));
                    data.Add(row);
                }

                attachment = CreateCSVFile(data);
            }
            else if (eventType.Equals("UPDATE") && rule.IsUpdate)
            {
                List<string> updatedColumn = new List<string>();
                for (int i = 0; i < pinfo.GetColumnCount(); i++)
                {
                    bool ischanges = document.Is_ValueChanged(i);
                    if (ischanges && !pinfo.IsVirtualColumn(i))
                    {
                        int ColumnId = pinfo.GetColumn(i).AD_Column_ID;
                        if (rule.ColumnIds.Contains(ColumnId))
                        {
                            updatedColumn.Add(pinfo.GetColumnName(i));
                        }
                    }

                }

                if (updatedColumn.Count > 0)
                {
                    string columnName = string.Join(", ", updatedColumn);
                    subject = "Record Update Notification - " + windowName;
                    msg = "Hello Team,\n\nA record has been updated: " + columnName + "\n\n"
                        + "Window: " + windowName + "\nTab: " + tabName
                        + "\nRecord ID: " + document.Get_ID();

                    header.Add("Field Name");
                    header.Add("Old Value");
                    header.Add("New Value");
                    data.Add(header);

                    for (int i = 0; i < document.Get_ColumnCount(); i++)
                    {
                        string colName = document.Get_ColumnName(i);
                        if (updatedColumn.Contains(colName))
                        {
                            List<object> row = new List<object>();
                            row.Add(colName);
                            row.Add(document.Get_ValueOld(i));
                            row.Add(document.Get_Value(i));
                            data.Add(row);
                        }
                    }

                    attachment = CreateCSVFile(data);
                }
            }
            else if (eventType.Equals("DELETE") && rule.IsDeleted)
            {
                subject = "Record Deleted Notification - " + windowName;
                msg = "Hello Team,\n\nA record has been deleted\n\n"
                    + "Window: " + windowName + "\nTab: " + tabName
                    + "\nRecord ID: " + document.Get_IDOld();

                header.Add("Field Name");
                header.Add("Value");
                data.Add(header);

                for (int i = 0; i < document.Get_ColumnCount(); i++)
                {
                    List<object> row = new List<object>();
                    row.Add(document.Get_ColumnName(i));
                    row.Add(document.Get_ValueOld(i));
                    data.Add(row);
                }

                attachment = CreateCSVFile(data);
            }

            if (string.IsNullOrEmpty(subject))
                return false;

            List<int> users = new List<int>();
            int notificationTo = Util.GetValueOfInt(recipient.Get_Value("AD_Column_ID"));
            if (notificationTo > 0)
                users.Add(notificationTo);

            string sql = Util.GetValueOfString(recipient.Get_Value("VAS_NotificationSQL"));
            if (ValidateSql(sql))
            {
                DataSet ds = DB.ExecuteDataset(sql);
                if (ds != null && ds.Tables.Count > 0)
                {
                    for (int i = 0; i < ds.Tables[0].Rows.Count; i++)
                    {
                        users.Add(Convert.ToInt32(ds.Tables[0].Rows[i][0]));
                    }
                }
            }
            int AD_User_ID_Rec = recipient.GetAD_User_ID();
            if (AD_User_ID_Rec >= 0)		//	System == 0
            {
                users.Add(AD_User_ID_Rec);
            }
            int AD_Role_ID_Rec = recipient.GetAD_Role_ID();
            if (AD_Role_ID_Rec >= 0)		//	SystemAdministrator == 0
            {
                MUserRoles[] urs = MUserRoles.GetOfRole(document.GetCtx(), AD_Role_ID_Rec);
                for (int j = 0; j < urs.Length; j++)
                {
                    MUserRoles ur = urs[j];
                    if (!ur.IsActive())
                        continue;
                    if (!users.Contains(ur.GetAD_User_ID()))
                    {
                        users.Add(ur.GetAD_User_ID());
                    }
                }
            }

            List<FileInfo> files = new List<FileInfo>();
            if (attachment != null)
                files.Add(attachment);

            int count = SendInfo(document.GetCtx(), users, subject, msg, files);
            if (count > 0)
            {
                log.Info("Mail Sucessfully send to " + count + " user");
            }
            else
            {
                log.Info("Mail not send ");
            }        
            return true;
        }

        /// <summary>
        /// Create CSV File
        /// </summary>
        /// <param name="data">data</param>
        /// <returns></returns>
        public FileInfo CreateCSVFile(List<List<object>> data)
        {
            Random rndm = new Random();
            string path = "Alert_" + DateTime.Now.Ticks + "_" + rndm.Next(0, 9999);
            string filePath = GlobalVariable.PhysicalPath + "TempDownload";
            if (!Directory.Exists(filePath))
                Directory.CreateDirectory(filePath);
            string fileName = filePath + "\\" + path + ".csv";
            using (StreamWriter writer = new StreamWriter(fileName))
            {
                for (int i = 0; i < data.Count; i++)
                {
                    List<object> row = data[i];
                    List<string> fields = new List<string>();
                    for (int j = 0; j < row.Count; j++)
                    {
                        string field = row[j] != null ? row[j].ToString() : "";
                        if (field.Contains(",") || field.Contains("\"") || field.Contains("\n"))
                        {
                            field = "\"" + field.Replace("\"", "\"\"") + "\"";
                        }
                        fields.Add(field);
                    }
                    writer.WriteLine(string.Join(",", fields));
                }
            }
            return new FileInfo(fileName);
        }

        /// <summary>
        /// Verify Sql, if Contains DROP ,Truncate in sql 
        /// </summary>
        /// <param name="sql">sql query</param>
        /// <returns>true if verified</returns>
        private bool ValidateSql(string sql)
        {
            if (string.IsNullOrEmpty(sql))
            {
                return false;
            }
            sql = sql.ToUpper();
            List<string> arr = sql.Split(' ').ToList();
            foreach (var str in arr)
            {
                if (System.Text.RegularExpressions.Regex.IsMatch(str, @"\b(UPDATE|DELETE|DROP|TRUNCATE)\b"))
                {
                    return false;
                }
            }
            return true;
        }

        /// <summary>
        /// Send Mail
        /// </summary>
        /// <param name="ctx">context</param>
        /// <param name="recipientUsers">recipientUsers</param>
        /// <param name="subject">subject</param>
        /// <param name="message">message</param>
        /// <param name="attachments">attachments</param>
        /// <returns></returns>
        public int SendInfo(Ctx ctx, List<int> recipientUsers, string subject, string message, List<FileInfo> attachments)
        {
            int countMail = 0;
            if (recipientUsers.Count == 0)
            {
                log.Info("No Recipient Found");
                return 0;
            }
            foreach (int user_id in recipientUsers)
            {
                MClient m_client = MClient.Get(ctx, ctx.GetAD_Client_ID());
                MUser user = MUser.Get(ctx, user_id);
                if (user.IsNotificationEMail() || user.GetNotificationType() == X_AD_User.NOTIFICATIONTYPE_EMailPlusNotice)
                {
                    {
                        EMail email = m_client.CreateEMail(null, user, subject, message, false);
                        if (email != null)
                        {
                            email.SetCtx(ctx);

                            log.Info(email.ToString());
                            foreach (FileInfo f in attachments)
                            {
                                email.AddAttachment(f);
                            }
                            string msg = email.Send();
                            log.Info("EMail Msg =>" + msg);
                            if (msg == EMail.SENT_OK)
                            {
                                try
                                {
                                    if (attachments != null && attachments.Count > 0)
                                    {
                                        foreach (FileInfo file in attachments)
                                        {
                                            if (file.Exists)
                                            {
                                                file.Delete();
                                                log.Info("Deleted file: " + file.FullName);
                                            }
                                        }
                                    }

                                    countMail++;
                                }
                                catch (Exception ex)
                                {
                                    log.Log(Level.SEVERE, "Failed to delete file: " + ex.Message);
                                }
                            }
                        }                       
                    }
                }
            }
            return countMail;
        }

    }
    public class RuleDetail
    {
        public int TableId { get; set; }
        public List<int> ColumnIds { get; set; } = new List<int>();
        public bool IsInsert { get; set; }
        public bool IsUpdate { get; set; }
        public bool IsDeleted { get; set; }
    }
}
