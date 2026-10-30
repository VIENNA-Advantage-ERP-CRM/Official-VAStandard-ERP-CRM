using Limilabs.Client.IMAP;
using Limilabs.Mail;
using Newtonsoft.Json.Linq;
using System;
using System.Collections;
using System.Collections.Generic;
using System.Data;
using System.Linq;
using System.Reflection;
using System.Security.Authentication;
using System.Text;
using System.Text.RegularExpressions;
using VAdvantage.DataBase;
using VAdvantage.Logging;
using VAdvantage.Model;
using VAdvantage.ProcessEngine;
using VAdvantage.Utility;
using VAModelAD.AIHelper;
using static VAModelAD.AIHelper.AIHelperDataContracts;
using static VAModelAD.AIHelper.AIPayload;

namespace VAdvantage.Process
{
    public class AttachMailToBP : ProcessEngine.SvrProcess
    {
        int AD_User_ID = 0;
        int AD_Client_ID = 0;
        int AD_Org_ID = 0;
        int apiAuthCred_ID = 0;
        private Imap imapMail;
        //string sender = "contacts";
        string sender = string.Empty, userAccount = string.Empty;
        string folderName = "Inbox";
        string isExcludeEmployee = string.Empty, excludedEmails = string.Empty;
        string isAutoAttach = string.Empty;
        string accessToken = string.Empty, provider = string.Empty, userEmail = string.Empty, userDomain = string.Empty;
        // VA101_Protocol of the current mail configuration; isVA101Fetch is true when its mails are fetched through VA101
        string protocol = string.Empty;
        bool isVA101Fetch = false;
        DataSet dsUser = null;
        private Assembly assembly;
        private Type emailServiceType;
        DateTime? lastRun;

        private StringBuilder retVal = new StringBuilder();

        protected override void Prepare()
        {
            //ProcessInfoParameter[] para = GetParameter();
            //for (int i = 0; i < para.Length; i++)
            //{
            //    String name = para[i].GetParameterName();
            //    if (para[i].GetParameter() == null)
            //    {
            //        ;
            //    }
            //    else if (name.Equals("AD_User_ID"))
            //    {
            //        AD_User_ID = para[i].GetParameterAsInt();
            //    }
            //    else
            //    {
            //        log.Log(Level.SEVERE, "Unknown Parameter: " + name);
            //    }
            //}
        }

        protected override string DoIt()
        {
            string sql = "";
            if (Env.IsModuleInstalled("VA101_"))
            {
                sql = @"SELECT umail.imaphost,
                                  umail.imapisssl,
                                  umail.imappassword,
                                  umail.imapport,
                                  umail.imapusername,
                                  umail.AD_User_ID,
                                  umail.AD_CLient_ID,
                                  umail.AD_Org_ID,umail.ISAUTOATTACH,umail.TABLEATTACH,umail.IsExcludeEmployee,umail.EMail,
                                  umail.DateLastRun, umail.AD_UserMailConfigration_ID, umail.VA101_Protocol, umail.VAS_ExcludedEmailList,
                                  ap.VA101_Provider, ac.VA101_APIAuthCredential_ID, ac.VA101_AccessToken, ac.VA101_Email"
                                + (Env.IsModuleInstalled("VAI01_") ? ", VAI01_IsCustomizeAttach, VAI01_AIOrchestration_ID" : "") +
                                @" FROM ad_usermailconfigration umail LEFT JOIN VA101_APIAuthCredential ac
                                ON (umail.VA101_APIAuthCredential_ID=ac.VA101_APIAuthCredential_ID AND ac.VA101_IsAuthorized='Y')
                                LEFT JOIN VA101_AuthProvider ap ON (umail.VA101_AuthProvider_ID=ap.VA101_AuthProvider_ID)
                                WHERE umail.IsActive ='Y' AND ac.IsActive ='Y' AND umail.VA101_IsAllowAccessEmail='Y'";
            }
            else
            {
                sql = @"SELECT umail.imaphost,
                                  umail.imapisssl,
                                  umail.imappassword,
                                  umail.imapport,
                                  umail.imapusername,
                                  umail.AD_User_ID,
                                  umail.AD_CLient_ID,
                                  umail.AD_Org_ID,umail.ISAUTOATTACH,umail.TABLEATTACH,umail.IsExcludeEmployee,
                                  umail.EMail,umail.DateLastRun, AD_UserMailConfigration_ID"
                                + (Env.IsModuleInstalled("VAI01_") ? ", VAI01_IsCustomizeAttach, VAI01_AIOrchestration_ID" : "") +
                                @" FROM ad_usermailconfigration umail
                                WHERE umail.IsActive ='Y' ";
            }

            DataSet ds = DB.ExecuteDataset(sql);
            if (ds == null || ds.Tables[0].Rows.Count == 0)
            {
                log.Log(Level.SEVERE, "No Config found");
                return "No Config found";
            }

            UserInformation user = null;
            for (int i = 0; i < ds.Tables[0].Rows.Count; i++)
            {
                //sender = Convert.ToString(ds.Tables[0].Rows[i]["TABLEATTACH"]).Trim();
                isExcludeEmployee = Convert.ToString(ds.Tables[0].Rows[i]["IsExcludeEmployee"]).Trim();
                isAutoAttach = Convert.ToString(ds.Tables[0].Rows[i]["ISAUTOATTACH"]).Trim();
                lastRun = null;

                if (ds.Tables[0].Rows[i]["DateLastRun"] != null)
                {
                    lastRun = Util.GetValueOfDateTime(ds.Tables[0].Rows[i]["DateLastRun"]);
                }

                if (isAutoAttach == "N" && Env.IsModuleInstalled("VAI01_") && Convert.ToString(ds.Tables[0].Rows[i]["VAI01_IsCustomizeAttach"]).Trim() == "N")
                {
                    retVal.Append("Mail Configration EMail Address <=> " + Convert.ToString(ds.Tables[0].Rows[i]["imapusername"]) + Environment.NewLine);
                    retVal.Append(Utility.Msg.GetMsg(GetCtx(), "IsAutoAttachORCustAttach") + Environment.NewLine);
                    continue;
                }

                // OA = OAuth (Google, Microsoft), EX = Exchange Web Services: both fetch through VA101
                protocol = Env.IsModuleInstalled("VA101_") ? Util.GetValueOfString(ds.Tables[0].Rows[i]["VA101_Protocol"]) : string.Empty;
                isVA101Fetch = protocol.Equals("OA") || protocol.Equals("EX");

                if (isVA101Fetch)
                {
                    provider = Util.GetValueOfString(ds.Tables[0].Rows[i]["VA101_Provider"]);
                    apiAuthCred_ID = Util.GetValueOfInt(ds.Tables[0].Rows[i]["VA101_APIAuthCredential_ID"]);

                    if (apiAuthCred_ID == 0)
                    {
                        log.Log(Level.SEVERE, Msg.GetMsg(GetCtx(), "UserAccountNotFound") + " " + ds.Tables[0].Rows[i]["AD_User_ID"].ToString());
                        continue;
                    }

                    accessToken = Util.GetValueOfString(ds.Tables[0].Rows[i]["VA101_AccessToken"]);

                    // Exchange signs in with the stored user name and password and has no access token
                    if (protocol.Equals("OA") && string.IsNullOrEmpty(accessToken))
                    {
                        log.Log(Level.SEVERE, Msg.GetMsg(GetCtx(), "TokenNotGenerated") + " " + ds.Tables[0].Rows[i]["AD_User_ID"].ToString());
                        continue;
                    }
                    excludedEmails = Util.GetValueOfString(ds.Tables[0].Rows[i]["VAS_ExcludedEmailList"]);
                    userEmail = Util.GetValueOfString(ds.Tables[0].Rows[i]["VA101_Email"]).ToLower();
                    userDomain = userEmail.Contains("@") ? userEmail.Split('@').Last().Trim().ToLower() : string.Empty;
                }
                else
                {
                    user = new UserInformation();
                    if (ds.Tables[0].Rows[i]["imapusername"] != DBNull.Value && ds.Tables[0].Rows[i]["imapusername"] != null)
                    {
                        user.Username = Convert.ToString(ds.Tables[0].Rows[i]["imapusername"]);
                    }
                    else
                    {
                        log.Log(Level.SEVERE, "UserName not found for AD_User_ID=" + ds.Tables[0].Rows[i]["AD_User_ID"].ToString());
                        continue;
                    }

                    if (ds.Tables[0].Rows[i]["imappassword"] != DBNull.Value && ds.Tables[0].Rows[i]["imappassword"] != null)
                    {
                        user.Password = SecureEngine.IsEncrypted(Convert.ToString(ds.Tables[0].Rows[i]["imappassword"])) ?
                            SecureEngine.Decrypt(Convert.ToString(ds.Tables[0].Rows[i]["imappassword"])) : Convert.ToString(ds.Tables[0].Rows[i]["imappassword"]);
                    }
                    else
                    {
                        log.Log(Level.SEVERE, "password not found for AD_User_ID=" + ds.Tables[0].Rows[i]["AD_User_ID"].ToString());
                        continue;
                    }

                    if (ds.Tables[0].Rows[i]["imapisssl"] != DBNull.Value && ds.Tables[0].Rows[i]["imapisssl"] != null)
                    {
                        user.UseSSL = Convert.ToString(ds.Tables[0].Rows[i]["imapisssl"]) == "Y" ? true : false;
                    }
                    else
                    {
                        log.Log(Level.SEVERE, "SSL not found for AD_User_ID=" + ds.Tables[0].Rows[i]["AD_User_ID"].ToString());
                        continue;
                    }

                    if (ds.Tables[0].Rows[i]["imapport"] != DBNull.Value && ds.Tables[0].Rows[i]["imapport"] != null)
                    {
                        user.HostPort = Util.GetValueOfInt(ds.Tables[0].Rows[i]["imapport"]);
                    }
                    else
                    {
                        log.Log(Level.SEVERE, "imapport not found for AD_User_ID=" + ds.Tables[0].Rows[i]["AD_User_ID"].ToString());
                        continue;
                    }

                    if (ds.Tables[0].Rows[i]["imaphost"] != DBNull.Value && ds.Tables[0].Rows[i]["imaphost"] != null)
                    {
                        user.Host = Convert.ToString(ds.Tables[0].Rows[i]["imaphost"]);
                    }
                    else
                    {
                        log.Log(Level.SEVERE, "imaphost not found for AD_User_ID=" + ds.Tables[0].Rows[i]["AD_User_ID"].ToString());
                        continue;
                    }
                }

                if (ds.Tables[0].Rows[i]["AD_User_ID"] != DBNull.Value && ds.Tables[0].Rows[i]["AD_User_ID"] != null)
                {
                    AD_User_ID = Util.GetValueOfInt(ds.Tables[0].Rows[i]["AD_User_ID"]);
                }

                //AD_Client_ID
                if (ds.Tables[0].Rows[i]["AD_Client_ID"] != DBNull.Value && ds.Tables[0].Rows[i]["AD_Client_ID"] != null)
                {
                    AD_Client_ID = Util.GetValueOfInt(ds.Tables[0].Rows[i]["AD_Client_ID"]);
                }

                if (ds.Tables[0].Rows[i]["AD_Org_ID"] != DBNull.Value && ds.Tables[0].Rows[i]["AD_Org_ID"] != null)
                {
                    AD_Org_ID = Util.GetValueOfInt(ds.Tables[0].Rows[i]["AD_Org_ID"]);
                }

                if (ds.Tables[0].Rows[i]["EMail"] != DBNull.Value && ds.Tables[0].Rows[i]["EMail"] != null)
                {
                    userAccount = Util.GetValueOfString(ds.Tables[0].Rows[i]["EMail"]);
                }

                if (AD_User_ID > 0)
                {
                    if (isVA101Fetch)
                    {
                        assembly = Assembly.Load("VA101Svc");
                        // Load type VA101.Common.EmailServices
                        emailServiceType = assembly.GetType("VA101.Common.EmailServices");
                        if (emailServiceType == null)
                        {
                            return "Type 'VA101.Common.EmailServices' not found.";
                        }
                        try
                        {
                            GetEmails(AD_User_ID, AD_Client_ID, AD_Org_ID);
                        }
                        catch (Exception ex)
                        {
                            log.Severe(ex.Message);
                        }
                    }
                    else
                    {
                        GetMails(user, AD_User_ID, AD_Client_ID, AD_Org_ID);
                    }
                }

                DB.ExecuteQuery("UPDATE AD_UserMailConfigration SET DateLastRun = " + GlobalVariable.TO_DATE(DateTime.Now.AddDays(-1), true)
                    + " WHERE AD_UserMailConfigration_ID = " + Util.GetValueOfInt(ds.Tables[0].Rows[i]["AD_UserMailConfigration_ID"]));

                if (Env.IsModuleInstalled("VAI01_") && Util.GetValueOfInt(ds.Tables[0].Rows[i]["VAI01_AIOrchestration_ID"]) > 0)
                {
                    string orchestartionkey = Util.GetValueOfString(DB.ExecuteScalar(@"SELECT Value FROM VAI01_AIOrchestration 
                    WHERE VAI01_AIOrchestration_ID = " + Util.GetValueOfInt(ds.Tables[0].Rows[i]["VAI01_AIOrchestration_ID"])));

                    ExecuteMailSuggestion(AD_Client_ID, orchestartionkey, 1000003, 0, AD_User_ID, GetCtx());
                }
            }
            return retVal.ToString();
        }

        private void GetEmails(int AD_User_ID, int AD_Client_ID, int AD_Org_ID)
        {
            dsUser = DB.ExecuteDataset("SELECT IsEmail, NotificationType FROM AD_User WHERE AD_User_ID=" + AD_User_ID);

            // Create an instance of EmailServices
            object emailServiceInstance = Activator.CreateInstance(emailServiceType);

            // Get the method with matching parameter types
            MethodInfo getEmailMethod = emailServiceType.GetMethod("GetEmails", new Type[] {
                                typeof(Ctx), typeof(int), typeof(DateTime?)});

            if (getEmailMethod == null)
            {
                log.Severe("GetEmails method not found.");
            }

            // Prepare arguments
            object[] parameters = new object[]
            {
                 GetCtx(),
                 apiAuthCred_ID,
                 lastRun
            };

            var rawResult = getEmailMethod.Invoke(emailServiceInstance, parameters);

            // Safely cast to IEnumerable to iterate
            IEnumerable enumerable = rawResult as IEnumerable;
            if (enumerable == null)
                throw new InvalidCastException("Returned result is not an IEnumerable.");

            List<object> result = enumerable.Cast<object>().ToList();
            if (result != null && result.Count > 0)
            {
                bool hasFolderName = result[0].GetType().GetProperty("Folder") != null;
                List<object> inboxMails = hasFolderName
                    ? result.Where(m => Util.GetValueOfString(((dynamic)m).Folder).IndexOf("sent", StringComparison.OrdinalIgnoreCase) < 0).ToList()
                    : result;
                List<object> sentMails = hasFolderName
                    ? result.Where(m => Util.GetValueOfString(((dynamic)m).Folder).IndexOf("sent", StringComparison.OrdinalIgnoreCase) >= 0).ToList()
                    : new List<object>();

                int existRec = -1;
                foreach (dynamic mail in inboxMails)
                {
                    try
                    {
                        string from = Util.GetValueOfString(mail.From).ToLower();
                        string subJect = mail.Subject;
                        string to = mail.To;
                        string mailDomain = GetAddresses(from).Select(a => a.Split('@').Last()).FirstOrDefault() ?? string.Empty;
                        string attachType = "I";

                        if (isAutoAttach == "N")
                        {
                            existRec = GetAttachedRecord(0, 0, mail.MessageID, folderName);
                            if (existRec > 0)// Is mail already attached
                            {
                                retVal.Append("MailAlreadyAttachedWithParticularRecord");
                                continue;
                            }
                            AttachMail(mail, 0, 0, attachType, "", "", "");
                            continue;
                        }

                        if (!String.IsNullOrEmpty(subJect) && subJect.IndexOf("(●") > -1)
                        {
                            string documentNO = subJect.Substring(subJect.IndexOf(":") + 1, subJect.IndexOf("(●") - (subJect.IndexOf(":") + 1));

                            subJect = subJect.Substring(subJect.IndexOf("(●") + 2);
                            subJect = subJect.Substring(0, subJect.LastIndexOf("●)"));
                            string TableID = subJect.Split('-')[0];// subJect.Substring(subJect.IndexOf("(") + 1, subJect.LastIndexOf("_") - subJect.IndexOf("(") - 1);
                            string recordID = subJect.Split('-')[1];// subJect.Substring(subJect.IndexOf("_") + 1, subJect.LastIndexOf(")") - subJect.IndexOf("_") - 1);


                            existRec = GetAttachedRecord(Util.GetValueOfInt(TableID), Util.GetValueOfInt(recordID), mail.MessageID, folderName);

                            if (existRec > 0)// Is mail already attached
                            {
                                retVal.Append("MailAlreadyAttachedWithParticularRecord");
                                continue;
                            }

                            MMailAttachment1 mAttachment = new MMailAttachment1(GetCtx(), 0, null);

                            string textmsg = mail.Body;
                            bool isAttachment = false;

                            for (int i = 0; i < mail.Attachments.Count; i++)
                            {
                                isAttachment = true;
                                mAttachment.AddEntry(mail.Attachments[i].FileName, mail.Attachments[i].AttachData);
                            }

                            string mailAddress = "";
                            for (int i = 0; i < mail.To.Count; i++)
                            {
                                mailAddress += mail.To[i].Address + ";";
                            }
                            string mailFrom = mail.From;

                            mAttachment.SetAD_Client_ID(AD_Client_ID);
                            mAttachment.SetAD_Org_ID(GetAD_Org_ID());
                            mAttachment.SetAD_Table_ID(Util.GetValueOfInt(TableID));
                            mAttachment.SetAttachmentType("I");
                            mAttachment.SetDateMailReceived(mail.EmailDate);
                            mAttachment.SetFolderName(folderName);
                            mAttachment.SetIsActive(true);
                            mAttachment.SetIsAttachment(isAttachment);
                            mAttachment.SetMailAddress(mailAddress);
                            mAttachment.SetMailAddressBcc(mail.Bcc);
                            mAttachment.SetMailAddressCc(mail.Cc);
                            mAttachment.SetMailAddressFrom(mailFrom);
                            mAttachment.SetRecord_ID(Util.GetValueOfInt(recordID));

                            mAttachment.SetMailUID(mail.MessageID);
                            mAttachment.SetMailUserName(mailAddress);
                            mAttachment.SetTextMsg(textmsg);
                            mAttachment.SetTitle(mail.Subject);
                            if (!mAttachment.Save())//save into database
                            {
                                retVal.Append("SaveError");
                            }
                            else
                            {
                                SendMailOrNotification(dsUser, GetCtx(), Msg.GetMsg(GetCtx(), "Emailrecievedwithsubject") + " = " + mail.Subject + " " + Msg.GetMsg(GetCtx(), "ANDAttachto") + " " + Msg.GetMsg(GetCtx(), "RequestID") + " = " + recordID, Util.GetValueOfInt(TableID), Util.GetValueOfInt(recordID), Convert.ToString(documentNO));
                            }
                            continue;
                        }

                        // Addresses to link: an {AttachTo: address} in the subject, otherwise every outside address of the
                        // mail (sender, To and Cc), so a mail to several business partners is linked to each of them.
                        List<string> addresses;
                        string pattern = @"\{AttachTo:\s*(?<email>[^}]+)\}";
                        Match match = Regex.Match(Util.GetValueOfString(subJect), pattern, RegexOptions.IgnoreCase);
                        if (match.Success)
                        {
                            addresses = GetAddresses(match.Groups["email"].Value);
                        }
                        else
                        {
                            string cc = Util.GetValueOfString(mail.Cc);
                            addresses = GetOutsideAddresses(GetAddresses(from + ";" + to + ";" + cc));
                            if (addresses.Count == 0)
                            {
                                // internal mail
                                continue;
                            }
                            if (userDomain.Length > 0 && userDomain.Equals(mailDomain))
                            {
                                // sent by a colleague to outside recipients
                                attachType = "M";
                            }
                        }

                        addresses = addresses.Where(a => !excludedEmails.ToLower().Contains(a)).ToList();
                        if (addresses.Count == 0)
                        {
                            continue;
                        }

                        LinkMail(mail, FindTargets(addresses), attachType);
                    }
                    catch (Exception ex)
                    {
                    }
                }

                // Sent mails (AttachmentType "M", folder Sent): linked to the business partners of the outside
                // recipients in To and Cc; stored unlinked when none is found
                foreach (dynamic mail in sentMails)
                {
                    try
                    {
                        string recipients = Util.GetValueOfString(mail.To) + ";" + Util.GetValueOfString(mail.Cc);
                        List<string> addresses = GetOutsideAddresses(GetAddresses(recipients))
                            .Where(a => !excludedEmails.ToLower().Contains(a)).ToList();
                        folderName = "Sent";
                        LinkMail(mail, FindTargets(addresses), "M");
                    }
                    catch (Exception ex)
                    {
                        log.Severe(ex.Message);
                    }
                    folderName = "Inbox";
                }
            }
        }

        public bool ExecuteMailSuggestion(
            int clientID,
            string orchestrationID,
            int tableID,
            int recordID,
            int userID,
            Ctx ctx)
        {
            MailSuggestionDataIn mailDataIn = new MailSuggestionDataIn();
            AIApiService.InitAIEndPoint(ctx.GetContext("#AppFullUrl"));
            RequestPayload.Get().SetDefaultParameters(mailDataIn);
            mailDataIn.orchestrationID = orchestrationID;
            mailDataIn.table_id = tableID;
            mailDataIn.record_id = recordID;
            mailDataIn.sessionID = ctx.GetAD_Session_ID();
            mailDataIn.sessionGUID = ctx.GetContext("#AD_Session_GUID");
            mailDataIn.userID = userID;

            // vis0008 Handled case for the API being called from VServer
            if (Util.GetValueOfInt(DB.ExecuteScalar("SELECT AD_Role_ID FROM AD_Session WHERE AD_Session_ID = " + mailDataIn.sessionID)) == 0)
            {
                int AD_Role_ID = Util.GetValueOfInt(DB.ExecuteScalar("SELECT AD_Role_ID FROM AD_Role WHERE AD_Client_ID = " + clientID + " AND IsAdministrator = 'Y' ORDER BY Created"));
                Ctx _newCtx = new Ctx();
                _newCtx.SetAD_Client_ID(clientID);
                // Fixed for Organization *
                _newCtx.SetAD_Org_ID(0);
                _newCtx.SetAD_Role_ID(AD_Role_ID);
                // Fixed user for SuperUser
                _newCtx.SetAD_User_ID(100);
                MSession s = MSession.Get(_newCtx, true, mailDataIn.endPoints);
                //s.SetDescription("By Web Service");
                if (s.Save())
                {
                    mailDataIn.sessionID = s.GetAD_Session_ID();
                }
                else
                {
                    ValueNamePair vnp = VLogger.RetrieveError();
                    string error = "";
                    if (vnp != null)
                    {
                        error = vnp.GetName();
                        if (error == "" && vnp.GetValue() != null)
                            error = vnp.GetValue();
                    }
                    log.SaveError("AI Chat Session Creation Error : ", "Session Not Created : " + mailDataIn.sessionID + ", Error Description : " + error);
                    return false;
                }
            }

            using (AIApiService service = new AIApiService(mailDataIn.token))
            {
                var outp = service.ExecuteRequest(mailDataIn, "callOrchestrationForAISuggestion");
                if (!outp.isError)
                {
                    try
                    {
                        JObject jObj = JObject.Parse(outp.result);
                        if (jObj.ContainsKey("is_error") && jObj.ContainsKey("success"))
                        {
                            return Util.GetValueOfString(jObj["is_error"]).ToLower() == "false" && Util.GetValueOfString(jObj["success"]).ToLower() == "true";
                        }
                    }
                    catch (Newtonsoft.Json.JsonException ex)
                    {
                        log.SaveError("AI Chat API Error : ", "Exception : " + ex.Message);
                    }
                }
                else
                {
                    log.SaveError("AI Chat API Error : ", "Error while creating thread");
                }
            }
            return false;
        }

        /// <summary>
        /// A record a fetched mail is linked to.
        /// </summary>
        private class MailTarget
        {
            public int TableID;
            public int RecordID;
            public string Kind;
            public string Name;
            public string Value;
        }

        /// <summary>
        /// Mail addresses in a From, To or Cc value such as "a@x.de; Name &lt;b@y.de&gt;", lower case, each once.
        /// </summary>
        private static List<string> GetAddresses(string value)
        {
            List<string> list = new List<string>();
            if (string.IsNullOrEmpty(value))
            {
                return list;
            }
            foreach (Match m in Regex.Matches(value, @"[^\s<>;,""']+@[^\s<>;,""']+"))
            {
                string address = m.Value.Trim().ToLower();
                if (!list.Contains(address))
                {
                    list.Add(address);
                }
            }
            return list;
        }

        /// <summary>
        /// The addresses outside the user's own domain, without the user's own address.
        /// </summary>
        private List<string> GetOutsideAddresses(List<string> addresses)
        {
            return addresses.Where(a => !a.Equals(userEmail)
                && (userDomain.Length == 0 || !userDomain.Equals(a.Split('@').Last()))).ToList();
        }

        /// <summary>
        /// SQL condition: the e-mail column holds exactly this address, alone or in a list separated by ; or ,
        /// </summary>
        private static string EmailMatch(string column, string address)
        {
            string a = address.Replace("'", "''").Replace("!", "!!").Replace("%", "!%").Replace("_", "!_");
            return "(';' || REPLACE(REPLACE(LOWER(" + column + "), ' ', ''), ',', ';') || ';') LIKE '%;" + a + ";%' ESCAPE '!'";
        }

        /// <summary>
        /// The records a mail with these addresses is linked to. Per address: the business partners of the
        /// contacts with that address (a contact without business partner adds nothing); when no contact has
        /// it, the business partners with that address; when none has it, the one lead with that address.
        /// An address held by several contacts or business partners links to all of them.
        /// </summary>
        private List<MailTarget> FindTargets(List<string> addresses)
        {
            List<MailTarget> targets = new List<MailTarget>();
            foreach (string address in addresses)
            {
                string userWhere = " WHERE u.IsActive='Y' AND u.AD_Client_ID=" + AD_Client_ID + " AND " + EmailMatch("u.Email", address);
                if (Util.GetValueOfInt(DB.ExecuteScalar("SELECT COUNT(*) FROM AD_User u" + userWhere)) > 0)
                {
                    AddPartners(targets, "SELECT bp.C_BPartner_ID, bp.Name, bp.Value, bp.IsEmployee FROM AD_User u"
                        + " INNER JOIN C_BPartner bp ON (bp.C_BPartner_ID = u.C_BPartner_ID)" + userWhere);
                    continue;
                }

                if (AddPartners(targets, "SELECT bp.C_BPartner_ID, bp.Name, bp.Value, bp.IsEmployee FROM C_BPartner bp"
                    + " WHERE bp.IsActive='Y' AND bp.AD_Client_ID=" + AD_Client_ID + " AND " + EmailMatch("bp.Email", address)))
                {
                    continue;
                }

                DataSet dsLead = DB.ExecuteDataset("SELECT c.C_Lead_ID, c.Name, c.DocumentNo FROM C_Lead c"
                    + " WHERE c.IsActive='Y' AND c.IsArchive='N' AND c.AD_Client_ID=" + AD_Client_ID + " AND " + EmailMatch("c.Email", address));
                if (dsLead != null && dsLead.Tables.Count > 0 && dsLead.Tables[0].Rows.Count == 1)
                {
                    DataRow dr = dsLead.Tables[0].Rows[0];
                    AddTarget(targets, PO.Get_Table_ID("C_Lead"), Util.GetValueOfInt(dr["C_Lead_ID"]), Msg.GetMsg(GetCtx(), "Lead"),
                        Util.GetValueOfString(dr["Name"]), Util.GetValueOfString(dr["DocumentNo"]));
                }
            }
            return targets;
        }

        /// <summary>
        /// Adds the business partners the query returns, except employees when the configuration excludes them.
        /// </summary>
        /// <returns>true when the query found any business partner</returns>
        private bool AddPartners(List<MailTarget> targets, string sql)
        {
            DataSet dsBP = DB.ExecuteDataset(sql);
            if (dsBP == null || dsBP.Tables.Count == 0 || dsBP.Tables[0].Rows.Count == 0)
            {
                return false;
            }
            int tableID = PO.Get_Table_ID("C_BPartner");
            foreach (DataRow dr in dsBP.Tables[0].Rows)
            {
                if (isExcludeEmployee == "Y" && Util.GetValueOfString(dr["IsEmployee"]).Trim() == "Y")
                {
                    continue;
                }
                AddTarget(targets, tableID, Util.GetValueOfInt(dr["C_BPartner_ID"]), Msg.GetMsg(GetCtx(), "BusinessPartner"),
                    Util.GetValueOfString(dr["Name"]), Util.GetValueOfString(dr["Value"]));
            }
            return true;
        }

        private static void AddTarget(List<MailTarget> targets, int tableID, int recordID, string kind, string name, string value)
        {
            if (recordID > 0 && !targets.Any(t => t.TableID == tableID && t.RecordID == recordID))
            {
                targets.Add(new MailTarget { TableID = tableID, RecordID = recordID, Kind = kind, Name = name, Value = value });
            }
        }

        /// <summary>
        /// Stores the mail once (MailAttachment1, current folder) and links it to every target: the first on the
        /// mail itself, the others through MailAttachmentRelatedTo. A mail stored before (another run, or the same
        /// mail in another user's mailbox) only gets the links it does not have yet; without targets it stays unlinked.
        /// </summary>
        private void LinkMail(dynamic mail, List<MailTarget> targets, string attachType)
        {
            string mailUID = Util.GetValueOfString((object)mail.MessageID);
            mailUID = mailUID.Replace("'", "''");
            string subject = Util.GetValueOfString((object)mail.Subject);
            int mailID = Util.GetValueOfInt(DB.ExecuteScalar("SELECT MAX(MailAttachment1_ID) FROM MailAttachment1 WHERE MailUID='"
                + mailUID + "' AND FolderName='" + folderName + "'"));
            bool linked = false;
            if (mailID == 0)
            {
                MailTarget first = targets.FirstOrDefault();
                mailID = first == null ? AttachMail(mail, 0, 0, attachType, "", "", "")
                    : AttachMail(mail, first.TableID, first.RecordID, attachType, first.Kind, first.Name, first.Value);
                if (mailID == 0)
                {
                    return;
                }
                linked = true;
            }

            foreach (MailTarget t in targets)
            {
                int existing = Util.GetValueOfInt(DB.ExecuteScalar("SELECT COUNT(*) FROM MailAttachment1 WHERE MailAttachment1_ID=" + mailID
                    + " AND AD_Table_ID=" + t.TableID + " AND Record_ID=" + t.RecordID))
                    + Util.GetValueOfInt(DB.ExecuteScalar("SELECT COUNT(*) FROM MailAttachmentRelatedTo WHERE MailAttachment1_ID=" + mailID
                    + " AND AD_Table_ID=" + t.TableID + " AND Record_ID=" + t.RecordID));
                if (existing > 0)
                {
                    continue;
                }

                // a mail stored unlinked gets the link on itself, any other one through MailAttachmentRelatedTo
                bool saved = DB.ExecuteQuery("UPDATE MailAttachment1 SET AD_Table_ID=" + t.TableID + ", Record_ID=" + t.RecordID
                    + " WHERE MailAttachment1_ID=" + mailID + " AND (AD_Table_ID IS NULL OR AD_Table_ID=0)") > 0;
                if (!saved)
                {
                    PO related = MTable.Get(GetCtx(), "MailAttachmentRelatedTo").GetPO(GetCtx(), 0, null);
                    related.SetAD_Client_ID(AD_Client_ID);
                    related.SetAD_Org_ID(GetAD_Org_ID());
                    related.Set_Value("MailAttachment1_ID", mailID);
                    related.Set_Value("AD_Table_ID", t.TableID);
                    related.Set_Value("Record_ID", t.RecordID);
                    saved = related.Save();
                }
                if (!saved)
                {
                    retVal.Append("SaveError");
                    continue;
                }
                linked = true;
                SendMailOrNotification(dsUser, GetCtx(), Msg.GetMsg(GetCtx(), "Emailrecievedwithsubject") + " = " + subject
                    + Msg.GetMsg(GetCtx(), "ANDAttachto") + t.Kind + " = " + t.Name, t.TableID, t.RecordID, t.Value);
            }

            if (!linked)
            {
                retVal.Append("MailAlreadyAttachedWithParticularRecord");
            }
        }

        /// <returns>MailAttachment1_ID of the stored mail, 0 when it could not be saved</returns>
        private int AttachMail(dynamic mail, int _tableID, int record_ID, string attachType, string userOrBp, string name, string value)
        {
            MMailAttachment1 mAttachment = new MMailAttachment1(GetCtx(), 0, null);
            string textmsg = mail.Body;
            bool isAttachment = false;
            if (mail.Attachments != null && mail.Attachments.Count > 0)
            {
                for (int i = 0; i < mail.Attachments.Count; i++)
                {
                    isAttachment = true;
                    mAttachment.AddEntry(mail.Attachments[i].FileName, mail.Attachments[i].AttachData);
                }
            }

            mAttachment.SetAD_Client_ID(AD_Client_ID);
            mAttachment.SetAD_Org_ID(GetAD_Org_ID());
            if (_tableID > 0)
            {
                mAttachment.SetAD_Table_ID(_tableID);
            }
            mAttachment.SetAttachmentType(attachType);
            mAttachment.SetDateMailReceived(Util.GetValueOfDateTime(mail.EmailDate));
            mAttachment.SetFolderName(folderName);
            mAttachment.SetIsActive(true);
            mAttachment.SetIsAttachment(isAttachment);
            mAttachment.SetMailAddress(mail.To);
            mAttachment.SetMailAddressBcc(mail.Bcc);
            mAttachment.SetMailAddressCc(mail.Cc);
            mAttachment.SetMailAddressFrom(mail.From);
            if (record_ID > 0)
            {
                mAttachment.SetRecord_ID(record_ID);
            }
            mAttachment.SetMailUID(mail.MessageID);
            mAttachment.SetMailUserName(mail.To);
            mAttachment.SetTextMsg(textmsg);
            mAttachment.SetTitle(mail.Subject);
            mAttachment.Set_Value("MailAccount", userAccount);
            mAttachment.Set_Value("UserAccount_ID", AD_User_ID);
            if (!mAttachment.Save())//save into database
            {
                retVal.Append("SaveError");
                return 0;
            }
            if (record_ID > 0)
            {
                SendMailOrNotification(dsUser, GetCtx(), Msg.GetMsg(GetCtx(), "Emailrecievedwithsubject") + " = " + mail.Subject + Msg.GetMsg(GetCtx(), "ANDAttachto") + userOrBp + " = " + name, _tableID, record_ID, value);
            }
            return mAttachment.Get_ID();
        }

        private void GetMails(UserInformation user, int AD_User_ID, int AD_Client_ID, int AD_Org_ID)
        {
            DataSet dsUser = DB.ExecuteDataset("select isemail,notificationtype from ad_user where ad_user_id=" + AD_User_ID);
            string login = Login(user);
            if (login.Equals(""))
            {
                imapMail.SelectInbox();
                List<long> uidList;
                // if Process is running first time then search all mails else search mails after last run date.
                if (lastRun != null)
                {
                    uidList = imapMail.Search(Expression.SentSince(lastRun.Value));
                }
                else
                {
                    uidList = imapMail.SearchFlag(Flag.All);
                }

                uidList.Reverse();

                //DocumentService ser = new DocumentService();
                byte[] bytes = null;
                string tableName = "AD_User";
                int _tableID = -1;
                int existRec = -1;
                StringBuilder attachmentID = new StringBuilder();
                string userOrBp = string.Empty;
                int record_ID = 0;
                foreach (long uid in uidList)
                {
                    try
                    {
                        Envelope structure = imapMail.GetEnvelopeByUID(uid);
                        string from = structure.From[0].Address;

                        string subJect = structure.Subject;
                        if (!String.IsNullOrEmpty(subJect) && subJect.IndexOf("(●") > -1)
                        {
                            string documentNO = subJect.Substring(subJect.IndexOf(":") + 1, subJect.IndexOf("(●") - (subJect.IndexOf(":") + 1));

                            subJect = subJect.Substring(subJect.IndexOf("(●") + 2);
                            subJect = subJect.Substring(0, subJect.LastIndexOf("●)"));
                            string TableID = subJect.Split('-')[0];// subJect.Substring(subJect.IndexOf("(") + 1, subJect.LastIndexOf("_") - subJect.IndexOf("(") - 1);
                            string recordID = subJect.Split('-')[1];// subJect.Substring(subJect.IndexOf("_") + 1, subJect.LastIndexOf(")") - subJect.IndexOf("_") - 1);


                            existRec = GetAttachedRecord(Util.GetValueOfInt(TableID), Util.GetValueOfInt(recordID), Util.GetValueOfInt(uid), folderName);

                            if (existRec > 0)// Is mail already attached
                            {
                                retVal.Append("MailAlreadyAttachedWithParticularRecord");
                                continue;
                            }

                            MMailAttachment1 mAttachment = new MMailAttachment1(GetCtx(), 0, null);

                            IMail message;
                            String eml = imapMail.GetMessageByUID(uid);
                            message = new MailBuilder().CreateFromEml(eml);
                            string textmsg = message.Html;
                            bool isAttachment = false;

                            for (int i = 0; i < message.Attachments.Count; i++)
                            {
                                isAttachment = true;
                                //mAttachment.SetBinaryData(message.Attachments[i].Data);
                                mAttachment.AddEntry(message.Attachments[i].FileName, message.Attachments[i].Data);
                            }


                            string cc = "";// mailBody.Cc;
                            for (int i = 0; i < message.Cc.Count; i++)
                            {
                                cc += ((Limilabs.Mail.Headers.MailBox)message.Cc[i]).Address + ";";
                            }
                            string bcc = "";// mailBody.Bcc;
                            for (int i = 0; i < message.Bcc.Count; i++)
                            {
                                bcc += ((Limilabs.Mail.Headers.MailBox)message.Bcc[i]).Address + ";";
                            }
                            string title = message.Subject;




                            string mailAddress = "";
                            for (int i = 0; i < message.To.Count; i++)
                            {
                                mailAddress += ((Limilabs.Mail.Headers.MailBox)message.To[i]).Address + ";";
                            }
                            string mailFrom = "";
                            for (int i = 0; i < message.From.Count; i++)
                            {
                                mailFrom += ((Limilabs.Mail.Headers.MailBox)message.From[i]).Address + ";";

                            }

                            mAttachment.SetAD_Client_ID(GetCtx().GetAD_Client_ID());
                            mAttachment.SetAD_Org_ID(GetCtx().GetAD_Org_ID());
                            mAttachment.SetAD_Table_ID(Util.GetValueOfInt(TableID));
                            mAttachment.SetAttachmentType("I");
                            mAttachment.SetDateMailReceived(message.Date);
                            mAttachment.SetFolderName(folderName);
                            mAttachment.SetIsActive(true);
                            mAttachment.SetIsAttachment(isAttachment);
                            mAttachment.SetMailAddress(mailAddress);
                            mAttachment.SetMailAddressBcc(bcc);
                            mAttachment.SetMailAddressCc(cc);
                            mAttachment.SetMailAddressFrom(mailFrom);
                            mAttachment.SetRecord_ID(Util.GetValueOfInt(recordID));
                            //if (sender == "AD_User")
                            //{
                            //    mAttachment.SetRecord_ID(Convert.ToInt32(dt.Rows[j][0]));
                            //    record_ID = Convert.ToInt32(dt.Rows[j][0]);
                            //}
                            //if (sender == "C_BPartner")
                            //{
                            //    mAttachment.SetRecord_ID(Convert.ToInt32(dt.Rows[j][1]));
                            //    record_ID = Convert.ToInt32(dt.Rows[j][1]);
                            //}

                            mAttachment.SetMailUID(Util.GetValueOfString(uid));
                            mAttachment.SetMailUserName(mailAddress);
                            mAttachment.SetTextMsg(textmsg);
                            mAttachment.SetTitle(message.Subject);
                            if (!mAttachment.Save())//save into database
                            {
                                retVal.Append("SaveError");
                            }
                            else
                            {
                                SendMailOrNotification(dsUser, GetCtx(), Msg.GetMsg(GetCtx(), "Emailrecievedwithsubject") + " = " + message.Subject + " " + Msg.GetMsg(GetCtx(), "ANDAttachto") + " " + Msg.GetMsg(GetCtx(), "RequestID") + " = " + recordID, Util.GetValueOfInt(TableID), Util.GetValueOfInt(recordID), Convert.ToString(documentNO));
                            }

                        }
                        //  else
                        //  {
                        try
                        {
                            string sql;
                            //changes done by Emp id:187
                            //If sender is lead it checks this query
                            if (sender == "C_Lead")
                            {
                                sql = "SELECT C_Lead_ID, C_Bpartner_ID,Name,DocumentNo AS value FROM C_Lead WHERE LOWER(Email) LIKE " + "'%" + from.Trim().ToLower() + "%'";
                            }
                            else
                            {
                                sql = "SELECT " + tableName + "_ID " + " , C_BPartner_ID,Name,value " + "FROM " + tableName + " WHERE LOWER(Email) LIKE " + "'%" + from.Trim().ToLower() + "%'";
                            }

                            sql += " AND AD_Client_ID=" + AD_Client_ID;
                            //sql += " AND AD_Client_ID=" + GetCtx().GetAD_Client_ID();
                            //string finalSql = MRole.GetDefault(GetCtx(), false).AddAccessSQL(sql, tableName.ToString(), MRole.SQL_NOTQUALIFIED, MRole.SQL_RO);
                            IDataReader idr = DB.ExecuteReader(sql);//+ " order by ad_texttemplate_id");                    
                            DataTable dt = new DataTable();
                            dt.Load(idr);
                            idr.Close();



                            if (dt.Rows.Count <= 0)
                            {
                                retVal.Append("Either proper access is not there or Email not found in database");
                                continue;
                            }



                            //  if (sender == "contacts")


                            if (dt.Rows.Count > 0)
                            {
                                for (int j = 0; j < dt.Rows.Count; j++)
                                {
                                    // Its go inside for user or busineespartner
                                    if (sender != "C_Lead")
                                    {
                                        string sqlQuery = "SELECT IsEmployee FROM C_BPartner WHERE C_BPartner_ID=" + Util.GetValueOfInt(dt.Rows[j]["C_BPartner_ID"]);
                                        sql += " AND AD_Client_ID=" + AD_Client_ID;
                                        //sqlQuery += " AND AD_Client_ID=" + GetCtx().GetAD_Client_ID();
                                        //string finalQuery = MRole.GetDefault(GetCtx(), false).AddAccessSQL(sqlQuery, "C_BPartner", MRole.SQL_NOTQUALIFIED, MRole.SQL_RO);
                                        DataSet ds = DB.ExecuteDataset(sqlQuery);

                                        if (ds != null && ds.Tables.Count > 0 && ds.Tables[0].Rows.Count > 0)
                                        {
                                            if (isExcludeEmployee == "Y" && Convert.ToString(ds.Tables[0].Rows[0]["IsEmployee"]).Trim() == "Y")
                                            {
                                                continue;
                                            }
                                        }
                                    }
                                    if (sender == "AD_User")
                                    {
                                        _tableID = PO.Get_Table_ID("AD_User");
                                        existRec = GetAttachedRecord(_tableID, Util.GetValueOfInt(dt.Rows[j]["AD_User_ID"]), Util.GetValueOfInt(uid), folderName);
                                        userOrBp = Msg.GetMsg(GetCtx(), "User");
                                    }
                                    //if (sender == "businessPartner")
                                    if (sender == "C_BPartner")
                                    {
                                        _tableID = PO.Get_Table_ID("C_BPartner");
                                        existRec = GetAttachedRecord(_tableID, Util.GetValueOfInt(dt.Rows[j]["C_BPartner_ID"]), Util.GetValueOfInt(uid), folderName);
                                        userOrBp = Msg.GetMsg(GetCtx(), "BusinessPartner");
                                    }
                                    //if sender is lead
                                    if (sender == "C_Lead")
                                    {
                                        _tableID = PO.Get_Table_ID("C_Lead");
                                        existRec = GetAttachedRecord(_tableID, Util.GetValueOfInt(dt.Rows[j]["C_Lead_ID"]), Util.GetValueOfInt(uid), folderName);
                                        userOrBp = Msg.GetMsg(GetCtx(), "Lead");
                                    }

                                    if (existRec > 0)// Is mail already attached
                                    {
                                        retVal.Append("MailAlreadyAttachedWithParticularRecord");
                                        continue;
                                    }
                                    MMailAttachment1 mAttachment = new MMailAttachment1(GetCtx(), 0, null);
                                    IMail message;
                                    String eml = imapMail.GetMessageByUID(uid);
                                    message = new MailBuilder().CreateFromEml(eml);

                                    string textmsg = message.Html;
                                    bool isAttachment = false;

                                    for (int i = 0; i < message.Attachments.Count; i++)
                                    {
                                        isAttachment = true;
                                        //mAttachment.SetBinaryData(message.Attachments[i].Data);
                                        mAttachment.AddEntry(message.Attachments[i].FileName, message.Attachments[i].Data);
                                    }

                                    string cc = "";// mailBody.Cc;
                                    for (int i = 0; i < message.Cc.Count; i++)
                                    {
                                        cc += ((Limilabs.Mail.Headers.MailBox)message.Cc[i]).Address + ";";
                                    }
                                    string bcc = "";// mailBody.Bcc;
                                    for (int i = 0; i < message.Bcc.Count; i++)
                                    {
                                        bcc += ((Limilabs.Mail.Headers.MailBox)message.Bcc[i]).Address + ";";
                                    }
                                    string title = message.Subject;

                                    string mailAddress = "";
                                    for (int i = 0; i < message.To.Count; i++)
                                    {
                                        mailAddress += ((Limilabs.Mail.Headers.MailBox)message.To[i]).Address + ";";
                                    }
                                    string mailFrom = "";
                                    string subject = "";
                                    for (int i = 0; i < message.From.Count; i++)
                                    {
                                        mailFrom += ((Limilabs.Mail.Headers.MailBox)message.From[i]).Address + ";";

                                    }
                                    string date = ((DateTime)message.Date).ToShortDateString();


                                    mAttachment.SetAD_Client_ID(GetCtx().GetAD_Client_ID());
                                    mAttachment.SetAD_Org_ID(GetCtx().GetAD_Org_ID());
                                    mAttachment.SetAD_Table_ID(_tableID);
                                    mAttachment.SetAttachmentType("I");
                                    mAttachment.SetDateMailReceived(message.Date);
                                    mAttachment.SetFolderName(folderName);
                                    mAttachment.SetIsActive(true);
                                    mAttachment.SetIsAttachment(isAttachment);
                                    mAttachment.SetMailAddress(mailAddress);
                                    mAttachment.SetMailAddressBcc(bcc);
                                    mAttachment.SetMailAddressCc(cc);
                                    mAttachment.SetMailAddressFrom(mailFrom);

                                    if (sender == "AD_User")
                                    {
                                        mAttachment.SetRecord_ID(Util.GetValueOfInt(dt.Rows[j]["AD_User_ID"]));
                                        record_ID = Util.GetValueOfInt(dt.Rows[j][0]);
                                    }
                                    if (sender == "C_BPartner")
                                    {
                                        mAttachment.SetRecord_ID(Util.GetValueOfInt(dt.Rows[j]["C_BPartner_ID"]));
                                        record_ID = Util.GetValueOfInt(dt.Rows[j][1]);
                                    }
                                    if (sender == "C_Lead")
                                    {
                                        mAttachment.SetRecord_ID(Util.GetValueOfInt(dt.Rows[j]["C_Lead_ID"]));
                                        record_ID = Util.GetValueOfInt(dt.Rows[j][0]);
                                    }

                                    mAttachment.SetMailUID(Util.GetValueOfString(uid));
                                    mAttachment.SetMailUserName(mailAddress);
                                    mAttachment.SetTextMsg(textmsg);
                                    mAttachment.SetTitle(message.Subject);
                                    if (!mAttachment.Save())//save into database
                                    {
                                        retVal.Append("SaveError");
                                    }
                                    else
                                    {
                                        SendMailOrNotification(dsUser, GetCtx(), Msg.GetMsg(GetCtx(), "Emailrecievedwithsubject") + " = " + message.Subject + Msg.GetMsg(GetCtx(), "ANDAttachto") + userOrBp + " = " + Util.GetValueOfString(dt.Rows[j]["Name"]), _tableID, record_ID, Util.GetValueOfString(dt.Rows[j]["Value"]));
                                    }
                                }
                            }

                            else if (dt.Rows.Count == 0)
                            {
                                retVal.Append("NoRecordFound");
                            }
                            //else
                            //{
                            //    retVal.Append("MultipleRecordFound");
                            //}
                        }
                        catch (Exception ex)
                        { }
                        //}

                    }
                    catch (Exception ex)
                    {
                        Logout();
                    }

                }

                Logout();

            }
            else
            {
                log.Log(Level.SEVERE, login);

            }
        }

        private int GetAttachedRecord(int tableID, int RecordID, string MailUID, string folderName)//, string MailUserFrom)
        {
            String sql = "SELECT MAILATTACHMENT1_ID FROM MAILATTACHMENT1 WHERE" + (tableID > 0 ? " AD_TABLE_ID=" + tableID + " AND"
                        : "") + (RecordID > 0 ? " RECORD_ID=" + RecordID + " AND" : "") + " MAILUID='" + MailUID
                        + "' AND FolderName='" + folderName + "'";

            System.Data.DataSet ds = DB.ExecuteDataset(sql);
            return ds.Tables[0].Rows.Count;
        }

        private int GetAttachedRecord(int tableID, int RecordID, int MailUID, string folderName)//, string MailUserFrom)
        {
            String sql = "SELECT MAILATTACHMENT1_ID FROM MAILATTACHMENT1 where AD_TABLE_ID=" + tableID
                        + " AND RECORD_ID=" + RecordID
                        + " AND MAILUID='" + MailUID.ToString()
                        + "' AND FolderName='" + folderName + "'";

            System.Data.DataSet ds = DB.ExecuteDataset(sql);
            return ds.Tables[0].Rows.Count;
        }

        private void SendMailOrNotification(DataSet dsUser, Ctx ctx, string message, int tableID, int recordID, string searchKey)
        {
            StringBuilder str = new StringBuilder();
            SendEmailOrNotification(ctx, AD_User_ID, false, true, false, tableID, str, message, recordID, searchKey);

            //bool isEmail = false;
            //bool isNotice = false;
            //if (dsUser != null && dsUser.Tables.Count > 0 && dsUser.Tables[0].Rows.Count > 0)
            //{
            //    if (Convert.ToString(dsUser.Tables[0].Rows[0]["ISEMAIL"]) == "Y")
            //    {
            //        isEmail = true;

            //        if (Convert.ToString(dsUser.Tables[0].Rows[0]["NOTIFICATIONTYPE"]) == "E")
            //        {
            //            isEmail = true;
            //        }
            //        else if (Convert.ToString(dsUser.Tables[0].Rows[0]["NOTIFICATIONTYPE"]) == "N")
            //        {
            //            isNotice = true;
            //        }
            //        else if (Convert.ToString(dsUser.Tables[0].Rows[0]["NOTIFICATIONTYPE"]) == "B")
            //        {
            //            isNotice = true;
            //        }
            //        if (isEmail && isNotice)
            //        {
            //            SendEmailOrNotification(ctx, AD_User_ID, false, false, true, tableID, str, message, recordID, searchKey);
            //        }
            //        else if (isEmail)
            //        {
            //            SendEmailOrNotification(ctx, AD_User_ID, true, false, false, tableID, str, message, recordID, searchKey);
            //        }
            //    }
            //    else
            //    {
            //        if (Convert.ToString(dsUser.Tables[0].Rows[0]["NOTIFICATIONTYPE"]) == "E")
            //        {
            //            SendEmailOrNotification(ctx, AD_User_ID, true, false, false, tableID, str, message, recordID, searchKey);
            //        }
            //        else if (Convert.ToString(dsUser.Tables[0].Rows[0]["NOTIFICATIONTYPE"]) == "N")
            //        {
            //            SendEmailOrNotification(ctx, AD_User_ID, false, true, false, tableID, str, message, recordID, searchKey);
            //        }
            //        else if (Convert.ToString(dsUser.Tables[0].Rows[0]["NOTIFICATIONTYPE"]) == "B")
            //        {
            //            SendEmailOrNotification(ctx, AD_User_ID, false, false, true, tableID, str, message, recordID, searchKey);
            //        }
            //    }
            //}
        }

        public void SendEmailOrNotification(Ctx ctx, int userID, bool isEmail, bool isNotification, bool isBoth, int tableid, StringBuilder strBuilder, string message, int recordID, string searchKey)
        {

            //******************Temporary Commented******************
            // VAdvantage.Classes.Context ctx = new VAdvantage.Classes.Context(ctxmap);

            string emailID = Convert.ToString(DB.ExecuteScalar("SELECT EMAIL FROM AD_USER WHERE AD_USER_ID=" + userID));
            message += " (" + searchKey + ")";

            try
            {

                if (isEmail)
                {
                    //  log.Log(Level.SEVERE, "SendOnlyEmailToSubscribeUser");
                    //  VAdvantage.Logging.VLogger.Get().Info("SendOnlyEmailToSubscribeUser");
                    // VAdvantage.Utility.EMail objEmail = new VAdvantage.Utility.EMail(ctx, string.Empty, string.Empty, string.Empty, string.Empty, Msg.GetMsg(ctx, "VADMS_FolderSubscriptionNotification"), message,false,true);
                    VAdvantage.Utility.EMail objEmail = new EMail(ctx, string.Empty, string.Empty, string.Empty, string.Empty, "AttachEmailNotification", message, false, true);
                    if (emailID.IndexOf(";") > -1)
                    {
                        string[] eIDS = emailID.Split(';');

                        for (int k = 0; k < eIDS.Length; k++)
                        {
                            objEmail.AddTo(eIDS[k], "");
                        }
                    }
                    else
                    {
                        objEmail.AddTo(emailID, "");
                    }
                    objEmail.SetMessageText(message);
                    objEmail.SetSubject("AttachEmailNotification");
                    string resu = objEmail.Send();
                }
                else if (isNotification)
                {
                    //  log.Log(Level.SEVERE, "SendOnlyNoticeToSubscribeUser");

                    MNote note = new MNote(ctx, 0, null);
                    note.SetAD_User_ID(userID);
                    // changes done by Bharat on 22 May 2018 to set Organization to * on Notification as discussed with Mukesh Sir.
                    //note.SetClientOrg(ctx.GetAD_Client_ID(), ctx.GetAD_Org_ID());
                    note.SetClientOrg(ctx.GetAD_Client_ID(), 0);
                    note.SetTextMsg(message);
                    note.SetDescription(Msg.GetMsg(ctx, "AttachEmailNotification"));
                    note.SetRecord(tableid, recordID);  // point to this
                    note.SetAD_Message_ID(859);//Workflow
                    if (!note.Save())
                    {
                        // CreateMessage(strBuilder, Convert.ToString(recordID));
                    }
                }
                else if (isBoth)
                {
                    // log.Log(Level.SEVERE, "SendOnlyEmailAndNoticeToSubscribeUser");

                    VAdvantage.Utility.EMail objEmail = new VAdvantage.Utility.EMail(ctx, string.Empty, string.Empty, string.Empty, string.Empty, Msg.GetMsg(ctx, "AttachEmailNotification"), message, false, true);
                    // VAdvantage.Utility.EMail objEmail = new EMail(ctx, string.Empty, string.Empty, emailID, string.Empty, Msg.GetMsg(ctx, "VADMS_FolderSubscriptionNotification"), message, false, true);
                    if (emailID.IndexOf(";") > -1)
                    {
                        string[] eIDS = emailID.Split(';');

                        for (int k = 0; k < eIDS.Length; k++)
                        {
                            objEmail.AddTo(eIDS[k], "");
                        }
                    }
                    else
                    {
                        objEmail.AddTo(emailID, "");
                    }
                    objEmail.SetMessageText(message);
                    objEmail.SetSubject(Msg.GetMsg(ctx, "AttachEmailNotification"));
                    objEmail.Send();
                    MNote note = new MNote(ctx, 0, null);
                    note.SetAD_User_ID(userID);
                    // changes done by Bharat on 22 May 2018 to set Organization to * on Notification as discussed with Mukesh Sir.
                    //note.SetClientOrg(ctx.GetAD_Client_ID(), ctx.GetAD_Org_ID());
                    note.SetClientOrg(ctx.GetAD_Client_ID(), 0);
                    note.SetTextMsg(message);
                    note.SetDescription(Msg.GetMsg(ctx, "AttachEmailNotification"));
                    note.SetRecord(tableid, recordID);  // point to this
                    note.SetAD_Message_ID(859);//Workflow
                    if (!note.Save())
                    {
                        // CreateMessage(strBuilder, Convert.ToString(recordID));
                    }
                }
            }
            catch (Exception ex)
            {
                VAdvantage.Logging.VLogger.Get().Info(ex.Message);
                log.Severe("SendEmailOrNotification Error : " + ex.Message);
            }

            //***************************************
        }
        /// <summary>
        /// Login
        /// </summary>
        /// <param name="userInfo"></param>
        /// <returns></returns>
        public string Login(UserInformation userInfo)
        {
            if (userInfo != null)
            {
                try
                {
                    this.imapMail = CreateImapConnection(userInfo);
                    return "";
                }
                catch (Exception ex)
                {
                    return ex.Message;
                }
            }
            else
                return "";
        }

        /// <summary>
        /// Create Imap Connection
        /// </summary>
        /// <param name="userInfo"></param>
        /// <returns></returns>
        private static Imap CreateImapConnection(UserInformation userInfo)
        {
            // connect
            Imap imapC = new Imap();
            imapC.SendTimeout = new TimeSpan(10, 0, 0);
            imapC.ReceiveTimeout = new TimeSpan(10, 0, 0);


            if (userInfo != null)
            {
                if (userInfo.UseSSL)
                {
                    imapC.SSLConfiguration.EnabledSslProtocols = SslProtocols.Default;
                    // Ignore certificate errors
                    imapC.ServerCertificateValidate += (sender, e) => { e.IsValid = true; };

                    imapC.ConnectSSL(userInfo.Host, userInfo.HostPort);
                }
                else
                {
                    imapC.Connect(userInfo.Host, userInfo.HostPort);
                }

                // Login
                imapC.Login(userInfo.Username, userInfo.Password);
            }
            return imapC;
        }

        /// <summary>
        /// Logout
        /// </summary>
        public void Logout()
        {
            this.CloseImapConnection();
        }

        /// <summary>
        /// closing Impa connection
        /// </summary>
        private void CloseImapConnection()
        {
            if (this.imapMail != null)
            {
                try
                {
                    if (this.imapMail.Connected)
                    {
                        this.imapMail.Close();
                    }

                    this.imapMail.Dispose();
                }
                catch
                {
                    if (this.imapMail.Connected)
                    {
                        this.imapMail.Close();
                    }

                    this.imapMail.Dispose();
                }
            }
        }

        //private string[] ToText(Mail_t_AddressList mail_t_AddressList)
        //{
        //    if (mail_t_AddressList == null)
        //        return new string[0];
        //    else
        //        return mail_t_AddressList.Mailboxes.Select(mb => mb.Address.ToString()).ToArray();
        //}

        //private string[] ToText(Mail_t_MailboxList mail_t_MailboxList)
        //{
        //    if (mail_t_MailboxList == null)
        //        return new string[0];
        //    else
        //        return mail_t_MailboxList.ToArray().Select(mb => mb.Address.ToString()).ToArray();
        //}




    }
    public class MailSuggestionDataIn : AIHelperData
    {
        public int table_id { get; set; }
        public int record_id { get; set; }
        public string orchestrationID { get; set; }
    }

    public class UserInformation
    {
        public string Username { get; set; }
        public string Password { get; set; }
        public string Host { get; set; }
        public string Email { get; set; }
        public int HostPort { get; set; }
        public bool UseSSL { get; set; }

        public string SmtpUsername { get; set; }
        public string SmtpPassword { get; set; }
        public string SmtpHost { get; set; }
        public bool IsSmtpAuth { get; set; }
        public bool IsSmtpUseSsl { get; set; }
        public int SmtpHostPort { get; set; }
    }


}
