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
using System.Text;
using VAdvantage.Classes;

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

        private static readonly Lazy<AlertEventManager> _lazy =
        new Lazy<AlertEventManager>();

        public static AlertEventManager Get()
        {
            return _lazy.Value;
        }

        static AlertEventManager()
        {
            PO.SetAlertEventMgr(Get());
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
            if (ctx != null)
            {
                MAlert[] alerts = MAlert.GetAlertValue(ctx, document.GetAD_Client_ID(), tableID);
                if (alerts == null || alerts.Length == 0)
                    return false;
                for (int i = 0; i < alerts.Length; i++)
                {
                    MAlert alert = alerts[i];
                    started = AlertRuleActivity(alert, document, pinfo, eventType);
                }
            }
            else
            {
                log.Info("context is null");
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
                if (recipients.Length > 0)
                {
                    for (int i = 0; i < recipients.Length; i++)
                    {
                        for (int j = 0; j < ruleDetails.Count; j++)
                        {
                            EventAlertProcessing(recipients[i], ruleDetails[j], document, pinfo, eventType);
                        }
                    }
                }
                else {
                    log.Severe("Recipient Not Found");
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
        /// Getting FieldNames By ColumnName
        /// </summary>
        /// <param name="tabID">tabID</param>
        /// <returns></returns>
        /// <summary>
        /// Returns a mapping of ColumnName → FieldName for the given AD_Tab_ID
        /// </summary>
        public Dictionary<string, string> GetFieldNamesByColumnName(int tabID)
        {
            Dictionary<string, string> map = new Dictionary<string, string>();

            string sql = @"
        SELECT c.ColumnName, f.Name AS FieldName
        FROM AD_Field f
        INNER JOIN AD_Column c ON c.AD_Column_ID = f.AD_Column_ID
        WHERE f.AD_Tab_ID = " + tabID + @" 
          AND f.IsActive = 'Y' ORDER BY f.SeqNo";

            DataSet ds = DB.ExecuteDataset(sql);

            if (ds != null && ds.Tables.Count > 0)
            {
                foreach (DataRow row in ds.Tables[0].Rows)
                {
                    string colName = Util.GetValueOfString(row["ColumnName"]);
                    string fieldName = Util.GetValueOfString(row["FieldName"]);

                    if (!map.ContainsKey(colName))
                        map.Add(colName, fieldName);
                }
            }

            return map;
        }

        /// <summary>
        /// Email Alert Process
        /// </summary>
        /// <param name="recipient">Alert Recipient</param>
        /// <param name="rule">Alert Rule</param>
        /// <param name="document">document</param>
        /// <param name="pinfo">PO info</param>
        /// <param name="eventType">Event type</param>
        /// <returns>true/false</returns>
        public bool EventAlertProcessing(MAlertRecipient recipient, RuleDetail rule, PO document, POInfo pinfo, string eventType)
        {
            string windowName = "";
            string tabName = "";
            string subject = "";
            List<List<object>> data = new List<List<object>>();
            FileInfo attachment = null;

            eventType = eventType.ToUpper();
            int tableID = pinfo.GetAD_Table_ID();

            // -----------------------------------------
            // 1. WINDOW & TAB NAME FETCH
            // -----------------------------------------
            if (document.GetAD_Window_ID() > 0 && document.GetWindowTabID() > 0)
            {
                windowName = Util.GetValueOfString(DB.ExecuteScalar(
                    "SELECT DisplayName FROM AD_Window WHERE AD_Window_ID=" + document.GetAD_Window_ID()));
                tabName = Util.GetValueOfString(DB.ExecuteScalar(
                    "SELECT Name FROM AD_Tab WHERE AD_Tab_ID=" + document.GetWindowTabID()));
            }
            else {
                log.Severe("Window and tab ID not found");
                return false;
            }
            
            // -----------------------------------------------------
            // 2. FETCH FIELDNAME MAP (ColumnName → FieldName)
            // -----------------------------------------------------
            Dictionary<string, string> fieldMap = GetFieldNamesByColumnName(document.GetWindowTabID());

            // Create a sorted map: FieldName → ColumnName
            SortedList<string, string> sortedByFieldName = new SortedList<string, string>();
            foreach (KeyValuePair<string, string> kv in fieldMap)
            {
                sortedByFieldName[kv.Value] = kv.Key;
            }


            // -----------------------------------------------------
            // 3. INSERT CASE
            // -----------------------------------------------------

            if (eventType.Equals("INSERT") && rule.IsInsert)
            {
                subject = Msg.Translate(document.GetCtx(), "VAS_RecordCreateNotification") + " - " + windowName;
                foreach (KeyValuePair<string, string> kv in sortedByFieldName)
                {
                    string fieldName = kv.Key;
                    string colName = kv.Value;

                    List<object> row = new List<object>();
                    row.Add(fieldName);
                    row.Add(document.Get_ValueOld(colName));
                    data.Add(row);
                }
            }


            // -----------------------------------------------------
            // 4. UPDATE CASE
            // -----------------------------------------------------
            else if (eventType.Equals("UPDATE") && rule.IsUpdate)
            {
                List<string> updatedColumn = new List<string>();

                for (int i = 0; i < pinfo.GetColumnCount(); i++)
                {
                    bool isChanged = document.Is_ValueChanged(i);

                    if (isChanged && !pinfo.IsVirtualColumn(i))
                    {
                        int colID = pinfo.GetColumn(i).AD_Column_ID;
                        if (rule.ColumnIds.Contains(colID))
                        {
                            updatedColumn.Add(pinfo.GetColumnName(i));
                        }
                    }
                }

                if (updatedColumn.Count > 0)
                {
                    subject = Msg.Translate(document.GetCtx(), "VAS_RecordUpdateNotification") + " - " + windowName;
                    foreach (KeyValuePair<string, string> kv in sortedByFieldName)
                    {
                        string fieldName = kv.Key;
                        string colName = kv.Value;

                        if (updatedColumn.Contains(colName))
                        {
                            List<object> row = new List<object>();
                            int index = document.Get_ColumnIndex(colName);

                            row.Add(fieldName);
                            row.Add(document.Get_ValueOld(index));
                            row.Add(document.Get_Value(index));

                            data.Add(row);
                        }
                    }
                }
            }

            // -----------------------------------------------------
            // 5. DELETE CASE
            // -----------------------------------------------------
            else if (eventType.Equals("DELETE") && rule.IsDeleted)
            {
                subject = Msg.Translate(document.GetCtx(), "VAS_RecordDeletedNotification") + " - " + windowName;
                foreach (KeyValuePair<string, string> kv in sortedByFieldName)
                {
                    string fieldName = kv.Key;
                    string colName = kv.Value;
                    List<object> row = new List<object>();
                    int index = document.Get_ColumnIndex(colName);

                    row.Add(fieldName);
                    row.Add(document.Get_ValueOld(index));

                    data.Add(row);
                }
            }

            // -----------------------------------------------------
            // 6. IF NO SUBJECT → NO PROCESS
            // -----------------------------------------------------
            if (string.IsNullOrEmpty(subject))
                return false;

            // -----------------------------------------------------
            // 7. COLLECT USERS (same as your code)
            // -----------------------------------------------------
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
            if (AD_User_ID_Rec >= 0)
                users.Add(AD_User_ID_Rec);

            int AD_Role_ID_Rec = recipient.GetAD_Role_ID();
            if (AD_Role_ID_Rec >= 0)
            {
                MUserRoles[] urs = MUserRoles.GetOfRole(document.GetCtx(), AD_Role_ID_Rec);
                for (int j = 0; j < urs.Length; j++)
                {
                    MUserRoles ur = urs[j];
                    if (ur.IsActive() && !users.Contains(ur.GetAD_User_ID()))
                        users.Add(ur.GetAD_User_ID());
                }
            }

            // -----------------------------------------------------
            // 8. SEND EMAIL
            // -----------------------------------------------------
            List<FileInfo> files = new List<FileInfo>();
            if (attachment != null)
                files.Add(attachment);

            string htmlBody = GenerateHtmlEmail(windowName, tabName, document, eventType, data);

            int count = SendInfoHTML(document.GetCtx(), users, subject, htmlBody);

            if (count > 0)
                log.Info("Mail Successfully sent to " + count + " user");
            else
                log.Info("Mail not sent");

            return true;
        }

        /// <summary>
        /// Send Mail
        /// </summary>
        /// <param name="ctx">context</param>
        /// <param name="recipientUsers">recipientUsers</param>
        /// <param name="subject">subject</param>
        /// <param name="htmlBody">htmlBody</param>
        /// <returns>count</returns>
        public int SendInfoHTML(Ctx ctx, List<int> recipientUsers, string subject, string htmlBody)
        {
            int countMail = 0;

            // Step 1: Build a map of unique emails → user_id
            Dictionary<string, int> uniqueEmailUsers = new Dictionary<string, int>();

            foreach (int user_id in recipientUsers)
            {
                MUser user = MUser.Get(ctx, user_id);

                string emailAddr = user.GetEMail();

                // Ignore blank or null email
                if (string.IsNullOrEmpty(emailAddr))
                    continue;

                // Add only if email not already present
                if (!uniqueEmailUsers.ContainsKey(emailAddr))
                {
                    uniqueEmailUsers[emailAddr] = user_id;
                }
            }

            if (uniqueEmailUsers.Count == 0)
            {
                log.Info("No valid email recipients found");
                return 0;
            }

            // Step 2: Send only once per unique email address
            foreach (var item in uniqueEmailUsers)
            {
                int user_id = item.Value;
                MUser user = MUser.Get(ctx, user_id);
                MClient m_client = MClient.Get(ctx, ctx.GetAD_Client_ID());

                if (user.IsNotificationEMail() ||
                    user.GetNotificationType() == X_AD_User.NOTIFICATIONTYPE_EMailPlusNotice)
                {
                    EMail email = m_client.CreateEMail(null, user, subject, htmlBody, true);
                    if (email != null)
                    {
                        email.SetCtx(ctx);
                        string msg = email.Send();
                        log.Info("Email Message => " + msg);

                        if (msg == EMail.SENT_OK)
                            countMail++;
                    }
                }
            }
            return countMail;
        }

        /// <summary>
        /// Generate HTML Email text
        /// </summary>
        /// <param name="windowName">Window Name</param>
        /// <param name="tabName">Tab Name</param>
        /// <param name="document">document</param>
        /// <param name="eventType">Event Type</param>
        /// <param name="data">Data</param>
        /// <returns>Email HTML body</returns>
        public string GenerateHtmlEmail(string windowName, string tabName, PO document, string eventType, List<List<object>> data)
        {
            string recordType = eventType.Equals("INSERT") ? Msg.Translate(document.GetCtx(), "Added") :
                                eventType.Equals("UPDATE") ? Msg.Translate(document.GetCtx(), "Updated") : Msg.Translate(document.GetCtx(), "Deleted");

            string recordAction = eventType.Equals("INSERT") ? Msg.Translate(document.GetCtx(), "VAS_NewRecordAdded") :
                                  eventType.Equals("UPDATE") ? Msg.Translate(document.GetCtx(), "VAS_RecordUpdated") : Msg.Translate(document.GetCtx(), "RecordDeleted");

            string performerName = document.GetCtx().GetAD_User_Name();

            SimpleDateFormat df = DisplayType.GetDateFormat(DisplayType.DateTime);
            string recordTime = Msg.Translate(document.GetCtx(), "Date") + " : " + DateTime.Now.ToString("yyyy-MM-dd hh:mm tt");


            StringBuilder detailRows = new StringBuilder();

            // Build table rows dynamically
            if (data != null && data.Count > 0)
            {
                for (int i = 0; i < data.Count; i++)
                {
                    var row = data[i];
                    string field = row.Count > 0 ? Util.GetValueOfString(row[0]) : "";

                    if (eventType.Equals("UPDATE") && row.Count > 2)
                    {
                        string oldVal = Util.GetValueOfString(row[1]);
                        string newVal = Util.GetValueOfString(row[2]);
                        detailRows.Append($@"
                <tr>
                    <td style='font-size: 14px; color: #475569; padding: 8px; border-bottom: 1px solid #e2e8f0;'>{field}</td>
                    <td style='font-size: 14px; color: #b91c1c; padding: 8px; border-bottom: 1px solid #e2e8f0; text-align:right;'>{oldVal}</td>
                    <td style='font-size: 14px; color: #16a34a; padding: 8px; border-bottom: 1px solid #e2e8f0; text-align:right;'>{newVal}</td>
                </tr>");
                    }
                    else
                    {
                        string value = row.Count > 1 ? Util.GetValueOfString(row[1]) : "";
                        if (!string.IsNullOrEmpty(value))
                        {
                            detailRows.Append($@"
                <tr>
                    <td style='font-size: 14px; color: #475569; padding: 8px; border-bottom: 1px solid #e2e8f0;'>{field}</td>
                    <td style='font-size: 14px; color: #0f172a; padding: 8px; border-bottom: 1px solid #e2e8f0; text-align:right;'>{value}</td>
                </tr>");
                        }
                    }
                }
            }
            else
            {
                detailRows.Append("<tr><td colspan='3' style='text-align:center; padding:8px; color:#94a3b8;'>No details available</td></tr>");
            }

            // Header columns
            string headerColumns = eventType.Equals("UPDATE")
                ? "<th style='text-align:left; padding:8px; border-bottom:2px solid #e2e8f0;'>" + Msg.Translate(document.GetCtx(), "Field") + "</th><th style='text-align:right; padding:8px; border-bottom:2px solid #e2e8f0;'>" + Msg.Translate(document.GetCtx(), "OldValue") + "</th><th style='text-align:right; padding:8px; border-bottom:2px solid #e2e8f0;'>" + Msg.Translate(document.GetCtx(), "NewValue") + "</th>"
                : "<th style='text-align:left; padding:8px; border-bottom:2px solid #e2e8f0;'>" + Msg.Translate(document.GetCtx(), "Field") + "</th><th style='text-align:right; padding:8px; border-bottom:2px solid #e2e8f0;'>" + Msg.Translate(document.GetCtx(), "VAS_Value") + "</th>";

            // Record Window Details section (common)
            string recordDetailsSection = $@"
    <div style='background-color: #f8fafc; border: 1px solid #e2e8f0; border-radius: 8px; margin-bottom: 24px;'>
        <div style='background-color: #f1f5f9; padding: 12px; border-bottom: 1px solid #e2e8f0; font-weight: bold; color: #0f172a;'>{Msg.Translate(document.GetCtx(), "VAS_RecordDetails")}</div>
        <div style='padding: 16px; font-size: 14px; color: #334155;'>
            <p style='margin: 4px 0;'><strong>Window Name:</strong> {windowName}</p>
            <p style='margin: 4px 0;'><strong>Tab Name:</strong> {tabName}</p>
            <p style='margin: 4px 0;'><strong>Record ID:</strong> {document.Get_ID()}</p>
        </div>
    </div>";

            // Changes or Record section
            string recordOrChangeSection = eventType.Equals("UPDATE")
                ? $@"
    <div style='background-color: #f0fdf4; border: 1px solid #bbf7d0; border-radius: 8px; margin-bottom: 24px;'>
        <div style='background-color: #dcfce7; padding: 12px; border-bottom: 1px solid #bbf7d0; font-weight: bold; color: #166534;'>{Msg.Translate(document.GetCtx(), "VAS_ChangesMade")}</div>
        <div style='padding: 16px;'>
            <table cellpadding='8' cellspacing='0' border='0' style='width: 100%; background-color: #fff; border: 1px solid #e2e8f0; border-radius: 8px; border-collapse: collapse;'>
                <thead style='background-color: #f8fafc;'>{headerColumns}</thead>
                <tbody>{detailRows}</tbody>
            </table>
        </div>
    </div>"
                : $@"
    <div style='margin-bottom: 24px;'>
        <h3 style='color: #0f172a; font-size: 16px; font-weight: bold; margin: 0 0 12px;'>{(eventType.Equals("INSERT") ? Msg.Translate(document.GetCtx(), "VAS_AddedFieldValues") : Msg.Translate(document.GetCtx(), "VAS_DeletedFieldValues"))}</h3>
        <table cellpadding='8' cellspacing='0' border='0' style='width: 100%; background-color: #fff; border: 1px solid #e2e8f0; border-radius: 8px; border-collapse: collapse;'>
            <thead style='background-color: #f8fafc;'>{headerColumns}</thead>
            <tbody>{detailRows}</tbody>
        </table>
    </div>";

            // ✅ Dynamic header icon and color setup
            string iconSymbol, iconBgColor, iconColor,borderColor;
            if (eventType.Equals("INSERT"))
            {
                iconSymbol = "✓";
                iconBgColor = "rgb(220, 252, 231)";
                iconColor = "rgb(22, 163, 74)";
                borderColor = "rgb(187, 247, 208)";
            }
            else if (eventType.Equals("UPDATE"))
            {
                iconSymbol = "✎";
                iconBgColor = "rgb(219, 234, 254)";
                iconColor = "rgb(37, 99, 235)";
                borderColor = "rgb(191, 219, 254)";
            }
            else
            {
                iconSymbol = "✕";
                iconBgColor = "rgb(254, 226, 226)";
                iconColor = "rgb(220, 38, 38)";
                borderColor = "rgb(254, 202, 202)";
            }
            string performerImageUrl = GetUserImageUrl(document.GetCtx(), document.GetCtx().GetAD_User_ID());
            string userEmail= GetUserEmail(document.GetCtx(), document.GetCtx().GetAD_User_ID());
            string firstLetter = "U";
            if (string.IsNullOrEmpty(performerName))
            {
                firstLetter = performerName.Substring(0, 1).ToUpper();
            }

            string performerAvatarHtml = performerImageUrl != null
                ? $"<img src='{performerImageUrl}' alt='User' style='width: 32px; height: 32px; border-radius: 50%; display: block;'>"
                : $@"<div style='
            width:32px;
            height:32px;
            border-radius:50%;
            background:#6366f1;
            color:white;
            display:inline-flex;
            align-items:center;
            justify-content:center;
            font-size:14px;
            font-weight:bold;
        '>{firstLetter}</div>";

            // Full HTML email
            string html = $@"
    <div style='font-family: Arial, sans-serif; line-height: 1.5; border: none;'>
      <table cellpadding='0' cellspacing='0' border='0' style='width: 100%; max-width: 600px; margin: 0 auto; background-color: #ffffff;'>
        <tr>
          <td style='background-color: #1e293b; padding: 24px;'>
            <table cellpadding='0' cellspacing='0' border='0' style='width: 100%;'>
              <tr>
                <td style='width: 40px; vertical-align: top;'>
                  <div style='width: 40px; height: 40px; background-color: {iconBgColor}; border-radius: 8px; text-align: center; line-height: 40px; font-size: 20px; color: {iconColor};'>{iconSymbol}</div>
                </td>
                <td style='padding-left: 12px; vertical-align: middle;'>
                  <h1 style='margin: 0; color: #fff; font-size: 20px; font-weight: bold;'>{recordAction}</h1>
                  <p style='margin: 0; color: #cbd5e1; font-size: 14px;'>{Msg.Translate(document.GetCtx(), "VAS_SystemNotification")}</p>
                </td>
              </tr>
            </table>
          </td>
        </tr>

        <tr>
          <td style='padding: 24px;'>

            <h2 style='color: #0f172a; font-size: 18px; font-weight: bold; margin: 0 0 16px;'>{Msg.Translate(document.GetCtx(), "Window")}: {windowName}</h2>

            <table cellpadding='0' cellspacing='0' border='0' style='width: 100%; background-color: {iconBgColor}; border: 1px solid {borderColor}; border-radius: 8px; margin-bottom: 24px;'>
              <tr>
                <td style='padding: 16px;'>
                  <table cellpadding='0' cellspacing='0' border='0' style='width: 100%;'>
                    <tr>
                      <td style='width: 32px; vertical-align: top;'>
                        {performerAvatarHtml}
                      </td>
                      <td style='padding-left: 12px; vertical-align: middle;'>
                        <div style='font-size: 14px; color: #475569;'>{Msg.Translate(document.GetCtx(), "VAS_Performeby")}</div>
                        <div style='font-size: 14px; color: #0f172a; font-weight: bold; margin: 2px 0;'>{performerName}</div>
                        <div style='font-size: 12px; color: rgb(100, 116, 139); '>{userEmail}</div>
                         </td>
                    </tr>
                  </table>
                  <div style='height: 1px; background-color: #e2e8f0; margin: 12px 0;'></div>
                  <div style='font-size: 14px;'>
                    <span style='color: #0f172a;'>{recordTime}</span>
                  </div>
                </td>
              </tr>
            </table>

            {recordDetailsSection}
            {recordOrChangeSection}
          </td>
        </tr>

        <tr>
          <td style='background-color: #f8fafc; border-top: 1px solid #e2e8f0; padding: 24px; text-align: center;'>
            <p style='font-size: 12px; color: #64748b; margin: 0; line-height: 1.5;'>{Msg.Translate(document.GetCtx(), "VAS_AutomatedNotification")}<br>{Msg.Translate(document.GetCtx(), "VAS_NotReply")}<br>{Msg.Translate(document.GetCtx(), "VAS_NullFieldNotDisplay")}</p>
          </td>
        </tr>
      </table>
    </div>";

            return html;
        }

        /// <summary>
        /// Get User Image URL
        /// </summary>
        /// <param name="ctx">Context</param>
        /// <param name="userId">AD_User_ID</param>
        /// <returns>URL</returns>
        private string GetUserImageUrl(Ctx ctx, int userId)
        {
            MUser user = MUser.Get(ctx, userId);

            // If user has image
            if (user != null && user.GetAD_Image_ID() > 0)
            {
                MImage img = MImage.Get(ctx, user.GetAD_Image_ID());
                if (img != null && !string.IsNullOrEmpty(img.GetImageURL()))
                {
                    return ctx.GetApplicationUrl() + img.GetImageURL();
                }
                else if (img.GetBinaryData() != null)
                {
                    return "data:image/*;base64, " + Convert.ToBase64String((byte[])img.GetBinaryData());
                }
            }
            return null;

            // Fallback SVG if no image URL found
            /*string firstLetter = user != null
                ? user.GetName().Substring(0, 1).ToUpper()
                : "U";

            string svg = $@"
<svg width='64' height='64' xmlns='http://www.w3.org/2000/svg'>
    <circle cx='32' cy='32' r='32' fill='#6366f1'/>
    <text x='50%' y='50%' font-size='28' fill='white' dy='.3em'
          text-anchor='middle' font-family='Arial'>
        {firstLetter}
    </text>
</svg>";

            string base64Svg = Convert.ToBase64String(System.Text.Encoding.UTF8.GetBytes(svg));
            return $"data:image/svg+xml;base64,{base64Svg}";*/
        }


        /// <summary>
        /// Get User Email
        /// </summary>
        /// <param name="ctx">Context</param>
        /// <param name="userId">AD_User_ID</param>
        /// <returns>Email</returns>
        private string GetUserEmail(Ctx ctx, int userId)
        {
            MUser user = MUser.Get(ctx, userId);
            if (user != null)
            {
                return user.GetEMail();
            }
            return "";
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
