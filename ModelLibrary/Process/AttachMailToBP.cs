using Limilabs.Client.IMAP;
using Limilabs.Mail;
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
        string sender = string.Empty;
        string folderName = "Inbox";
        string isExcludeEmployee = string.Empty, excludedEmails = string.Empty;
        string accessToken = string.Empty, provider = string.Empty, userEmail = string.Empty, userDomain = string.Empty;
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
                                  umail.AD_Org_ID,umail.ISAUTOATTACH,umail.TABLEATTACH,umail.IsExcludeEmployee,
                                  umail.DateLastRun, umail.AD_UserMailConfigration_ID, umail.VA101_Protocol, umail.VAS_ExcludedEmailList,
                                  ap.VA101_Provider, ac.VA101_APIAuthCredential_ID, ac.VA101_AccessToken, ac.VA101_Email
                                FROM ad_usermailconfigration umail LEFT JOIN VA101_APIAuthCredential ac
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
                                  umail.DateLastRun, AD_UserMailConfigration_ID
                                FROM ad_usermailconfigration umail
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
                sender = Convert.ToString(ds.Tables[0].Rows[i]["TABLEATTACH"]).Trim();
                isExcludeEmployee = Convert.ToString(ds.Tables[0].Rows[i]["IsExcludeEmployee"]).Trim();
                lastRun = null;

                if (ds.Tables[0].Rows[i]["DateLastRun"] != null)
                {
                    lastRun = Util.GetValueOfDateTime(ds.Tables[0].Rows[i]["DateLastRun"]);
                }

                if (Convert.ToString(ds.Tables[0].Rows[i]["ISAUTOATTACH"]).Trim() == "N" && (sender == string.Empty || sender == null))
                {
                    retVal.Append("Mail Configration EMail Address <=> " + Convert.ToString(ds.Tables[0].Rows[i]["imapusername"]) + Environment.NewLine);
                    retVal.Append(Utility.Msg.GetMsg(GetCtx(), "IsAutoAttachORTableAttach") + Environment.NewLine);
                    continue;
                }

                if (Env.IsModuleInstalled("VA101_") && Util.GetValueOfString(ds.Tables[0].Rows[i]["VA101_Protocol"]).Equals("OA"))
                {
                    provider = Util.GetValueOfString(ds.Tables[0].Rows[i]["VA101_Provider"]);
                    apiAuthCred_ID = Util.GetValueOfInt(ds.Tables[0].Rows[i]["VA101_APIAuthCredential_ID"]);

                    if (apiAuthCred_ID == 0)
                    {
                        log.Log(Level.SEVERE, Msg.GetMsg(GetCtx(), "UserAccountNotFound") + " " + ds.Tables[0].Rows[i]["AD_User_ID"].ToString());
                        continue;
                    }

                    accessToken = Util.GetValueOfString(ds.Tables[0].Rows[0]["VA101_AccessToken"]);

                    if (string.IsNullOrEmpty(accessToken))
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

                if (AD_User_ID > 0)
                {
                    if (Env.IsModuleInstalled("VA101_") && Util.GetValueOfString(ds.Tables[0].Rows[i]["VA101_Protocol"]).Equals("OA"))
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
                int _tableID = -1;
                int existRec = -1;
                StringBuilder attachmentID = new StringBuilder();
                string userOrBp = string.Empty, value = string.Empty, name = string.Empty;
                int record_ID = 0;
                var emailDomains = new List<string>
                {
                    "gmail.com", "googlemail.com", "outlook.com", "hotmail.com", "live.com", "msn.com", "yahoo.com", "ymail.com", "rocketmail.com",
                    "icloud.com", "me.com", "mac.com", "protonmail.com", "pm.me", "zoho.com", "aol.com", "gmx.com", "gmx.net", "mail.com", "yandex.com",
                    "yandex.ru", "tutanota.com", "tutanota.de", "comcast.net", "verizon.net", "cox.net", "sbcglobal.net", "bellsouth.net", "btinternet.com",
                    "orange.fr", "mailinator.com", "10minutemail.com", "guerrillamail.com", "tempmail.net", "trashmail.com"
                };
                foreach (dynamic mail in result)
                {
                    try
                    {
                        string tableName = "AD_User";
                        string from = Util.GetValueOfString(mail.From).ToLower();
                        string subJect = mail.Subject;
                        string to = mail.To;
                        string mailDomain = from.Contains("@") ? from.Split('@').Last().Trim().ToLower() : string.Empty;
                        string attachType = "I";
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

                            mAttachment.SetAD_Client_ID(GetCtx().GetAD_Client_ID());
                            mAttachment.SetAD_Org_ID(GetCtx().GetAD_Org_ID());
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
                        }

                        string pattern = @"\{AttachTo:\s*(?<email>[^}]+)\}";
                        Match match = Regex.Match(subJect, pattern, RegexOptions.IgnoreCase);
                        if (match.Success)
                        {
                            from = match.Groups["email"].Value.Trim().ToLower();
                            pattern = @"(?i)\{AttachTo:\s*[^}]+\}";
                            subJect = Regex.Replace(subJect, pattern, "").Trim();
                        }                       
                        else if (Util.GetValueOfString(mail.Cc).ToLower().Contains(userEmail))
                        {
                            bool internalmail = true;
                            string[] tomails = to.Split(';').ToArray();
                            if (userDomain.Equals(mailDomain))
                            {
                                foreach (var tomail in tomails)
                                {
                                    if (userDomain.Equals(tomail.Contains("@") ? tomail.Split('@').Last().Trim().ToLower() : string.Empty))
                                    {
                                        continue;
                                    }
                                    else
                                    {
                                        from = tomail;
                                        attachType = "M";
                                        internalmail = false;
                                    }
                                }
                            }
                            else
                            {
                                internalmail = false;
                            }

                            if (internalmail)
                            {
                                continue;
                            }
                        }
                        else if (userDomain.Equals(mailDomain))
                        {
                            continue;
                        }

                        if (excludedEmails.ToLower().Contains(from))
                        {
                            continue;
                        }

                        StringBuilder sql = new StringBuilder();
                        sql.Append("SELECT " + tableName + "_ID, C_BPartner_ID, Name, Value FROM " + tableName +
                            " WHERE LOWER(Email) LIKE " + "'%" + from + "%' AND AD_Client_ID=" + AD_Client_ID);

                        IDataReader idr = DB.ExecuteReader(sql.ToString());
                        DataTable dt = new DataTable();
                        dt.Load(idr);
                        idr.Close();
                        if (dt.Rows.Count <= 0)
                        {
                            tableName = "C_BPartner";
                            sql.Clear();
                            sql.Append("SELECT " + tableName + "_ID, C_BPartner_ID, Name, Value FROM " + tableName +
                            " WHERE LOWER(Email) LIKE " + "'%" + from + "%' AND AD_Client_ID=" + AD_Client_ID);

                            idr = DB.ExecuteReader(sql.ToString());
                            dt = new DataTable();
                            dt.Load(idr);
                            idr.Close();
                        }

                        if (dt.Rows.Count <= 0)
                        {
                            tableName = "C_Lead";
                            sql.Clear();
                            sql.Append(@"SELECT c.C_Lead_ID, c.Name, c.DocumentNo AS Value, 
                                c.Email FROM C_Lead c WHERE c.IsActive='Y' AND c.IsArchive='N'
                                AND (LOWER(c.Email) LIKE " + "'%" + from + "%'");
                            if (!emailDomains.Contains(mailDomain))
                            {
                                sql.Append(" OR LOWER(c.Email) LIKE " + "'%" + mailDomain + "%'");
                            }
                            sql.Append(") AND c.AD_Client_ID=" + AD_Client_ID + " ORDER BY c.Updated DESC");

                            idr = DB.ExecuteReader(sql.ToString());
                            dt = new DataTable();
                            dt.Load(idr);
                            idr.Close();
                        }

                        if (dt.Rows.Count <= 0 && tableName == "C_Lead")
                        {
                            string[] tomails = to.ToLower().Split(';').ToArray();
                            foreach (var tomail in tomails)
                            {
                                if (userDomain.Equals(tomail.Contains("@") ? tomail.Split('@').Last().Trim().ToLower() : string.Empty))
                                {
                                    continue;
                                }
                                else
                                {
                                    mailDomain = tomail.Contains("@") ? tomail.Split('@').Last().Trim().ToLower() : string.Empty;
                                    sql.Clear();
                                    sql.Append(@"SELECT c.C_Lead_ID, c.Name, c.DocumentNo AS Value, 
                                        c.Email FROM C_Lead c WHERE c.IsActive='Y' AND c.IsArchive='N' 
                                        AND (LOWER(c.Email) LIKE " + "'%" + tomail.Trim() + "%'");
                                    if (!emailDomains.Contains(mailDomain))
                                    {
                                        sql.Append(" OR LOWER(c.Email) LIKE " + "'%" + mailDomain + "%'");
                                    }
                                    sql.Append(") AND c.AD_Client_ID=" + AD_Client_ID);

                                    idr = DB.ExecuteReader(sql.ToString());
                                    dt = new DataTable();
                                    dt.Load(idr);
                                    idr.Close();
                                    if (dt.Rows.Count <= 0)
                                    {
                                        existRec = GetAttachedRecord(0, 0, mail.MessageID, folderName);

                                        if (existRec > 0)// Is mail already attached
                                        {
                                            retVal.Append("MailAlreadyAttachedWithParticularRecord");
                                            continue;
                                        }
                                        AttachMail(mail, 0, 0, attachType, "", "", "");
                                    }
                                    else
                                    {
                                        AttachToLead(mail, dt, attachType, tomail.Trim());
                                    }
                                    continue;
                                }
                            }
                        }

                        else if (dt.Rows.Count <= 0)
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
                        else
                        {
                            if (tableName == "C_Lead")
                            {
                                AttachToLead(mail, dt, attachType, from);
                                continue;
                            }
                            if (dt.Rows.Count > 1)
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
                            else
                            {
                                string sqlQuery = "SELECT IsEmployee FROM C_BPartner WHERE C_BPartner_ID=" + Util.GetValueOfInt(dt.Rows[0]["C_BPartner_ID"]);
                                DataSet ds = DB.ExecuteDataset(sqlQuery);

                                if (ds != null && ds.Tables.Count > 0 && ds.Tables[0].Rows.Count > 0)
                                {
                                    if (isExcludeEmployee == "Y" && Convert.ToString(ds.Tables[0].Rows[0]["IsEmployee"]).Trim() == "Y")
                                    {
                                        continue;
                                    }
                                }
                                if (tableName == "AD_User")
                                {
                                    _tableID = PO.Get_Table_ID("AD_User");
                                    existRec = GetAttachedRecord(0, 0, mail.MessageID, folderName);
                                    userOrBp = Msg.GetMsg(GetCtx(), "User");
                                }
                                if (tableName == "C_BPartner")
                                {
                                    _tableID = PO.Get_Table_ID("C_BPartner");
                                    existRec = GetAttachedRecord(0, 0, mail.MessageID, folderName);
                                    userOrBp = Msg.GetMsg(GetCtx(), "BusinessPartner");
                                }
                                if (existRec > 0)// Is mail already attached
                                {
                                    retVal.Append("MailAlreadyAttachedWithParticularRecord");
                                    continue;
                                }

                                if (tableName == "AD_User")
                                {
                                    record_ID = Util.GetValueOfInt(dt.Rows[0][0]);
                                }
                                if (tableName == "C_BPartner")
                                {
                                    record_ID = Util.GetValueOfInt(dt.Rows[0][1]);
                                }
                                name = Util.GetValueOfString(dt.Rows[0]["Name"]);
                                value = Util.GetValueOfString(dt.Rows[0]["Value"]);
                                AttachMail(mail, _tableID, record_ID, attachType, userOrBp, name, value);
                            }
                        }
                    }
                    catch (Exception ex)
                    {
                    }
                }
            }
        }

        private void GetEmailsOld(int AD_User_ID, int AD_Client_ID, int AD_Org_ID)
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
                string tableName = "AD_User";
                int _tableID = -1;
                int existRec = -1;
                StringBuilder attachmentID = new StringBuilder();
                string userOrBp = string.Empty, value = string.Empty, name = string.Empty;
                int record_ID = 0;
                var emailDomains = new List<string>
                {
                    "gmail.com", "googlemail.com", "outlook.com", "hotmail.com", "live.com", "msn.com", "yahoo.com", "ymail.com", "rocketmail.com",
                    "icloud.com", "me.com", "mac.com", "protonmail.com", "pm.me", "zoho.com", "aol.com", "gmx.com", "gmx.net", "mail.com", "yandex.com",
                    "yandex.ru", "tutanota.com", "tutanota.de", "comcast.net", "verizon.net", "cox.net", "sbcglobal.net", "bellsouth.net", "btinternet.com",
                    "orange.fr", "mailinator.com", "10minutemail.com", "guerrillamail.com", "tempmail.net", "trashmail.com"
                };
                foreach (dynamic mail in result)
                {
                    try
                    {
                        string from = Util.GetValueOfString(mail.From).ToLower();
                        string subJect = mail.Subject;
                        string to = mail.To;
                        string mailDomain = from.Contains("@") ? from.Split('@').Last().Trim().ToLower() : string.Empty;
                        string attachType = "I";
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

                            mAttachment.SetAD_Client_ID(GetCtx().GetAD_Client_ID());
                            mAttachment.SetAD_Org_ID(GetCtx().GetAD_Org_ID());
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
                        }

                        string pattern = @"\{AttachTo:\s*(?<email>[^}]+)\}";
                        Match match = Regex.Match(subJect, pattern, RegexOptions.IgnoreCase);
                        if (match.Success)
                        {
                            from = match.Groups["email"].Value.Trim().ToLower();
                            pattern = @"(?i)\{AttachTo:\s*[^}]+\}";
                            subJect = Regex.Replace(subJect, pattern, "").Trim();
                        }
                        //if (!String.IsNullOrEmpty(subJect) && subJect.ToLower().IndexOf("{attachto") > -1 && subJect.IndexOf("}") > -1)
                        //{
                        //    from = subJect.Substring(subJect.ToLower().IndexOf("{attachto:") + 1, subJect.IndexOf("}") - (subJect.ToLower().IndexOf("{attachto:") + 1)).ToLower();                           
                        //}
                        else if (Util.GetValueOfString(mail.Cc).ToLower().Contains(userEmail))
                        {
                            bool internalmail = true;
                            string[] tomails = to.Split(';').ToArray();
                            if (userDomain.Equals(mailDomain))
                            {
                                foreach (var tomail in tomails)
                                {
                                    if (userDomain.Equals(tomail.Contains("@") ? tomail.Split('@').Last().Trim().ToLower() : string.Empty))
                                    {
                                        continue;
                                    }
                                    else
                                    {
                                        from = tomail;
                                        attachType = "M";
                                        internalmail = false;
                                    }
                                }
                            }
                            else
                            {
                                internalmail = false;
                            }

                            if (internalmail)
                            {
                                continue;
                            }
                        }
                        else if (userDomain.Equals(mailDomain))
                        {
                            continue;
                        }

                        if (excludedEmails.ToLower().Contains(from))
                        {
                            continue;
                        }

                        StringBuilder sql = new StringBuilder();
                        //If sender is lead it checks this query
                        if (sender == "C_Lead")
                        {
                            sql.Append(@"SELECT c.C_Lead_ID, o.C_Bpartner_ID, c.Name, c.DocumentNo AS Value, 
                                c.Email, c.C_Project_ID, o.Ref_Order_ID, o.C_Order_ID
                                FROM C_Lead c LEFT JOIN C_Project o ON (c.C_Project_ID=o.C_Project_ID)
                                WHERE c.IsActive='Y' AND (LOWER(c.Email) LIKE " + "'%" + from + "%'");
                            if (!emailDomains.Contains(mailDomain))
                            {
                                sql.Append(" OR LOWER(c.Email) LIKE " + "'%" + mailDomain + "%'");
                            }
                            sql.Append(") AND c.AD_Client_ID=" + AD_Client_ID + " ORDER BY c.IsArchive ASC, c.Updated DESC");
                        }
                        else
                        {
                            sql.Append("SELECT " + tableName + "_ID, C_BPartner_ID, Name, Value FROM " + tableName +
                                " WHERE LOWER(Email) LIKE " + "'%" + from + "%' AND AD_Client_ID=" + AD_Client_ID);
                        }

                        IDataReader idr = DB.ExecuteReader(sql.ToString());
                        DataTable dt = new DataTable();
                        dt.Load(idr);
                        idr.Close();

                        if (dt.Rows.Count <= 0 && sender == "C_Lead")
                        {
                            string[] tomails = to.ToLower().Split(';').ToArray();
                            foreach (var tomail in tomails)
                            {
                                if (userDomain.Equals(tomail.Contains("@") ? tomail.Split('@').Last().Trim().ToLower() : string.Empty))
                                {
                                    continue;
                                }
                                else
                                {
                                    mailDomain = tomail.Contains("@") ? tomail.Split('@').Last().Trim().ToLower() : string.Empty;
                                    sql.Clear();
                                    sql.Append(@"SELECT c.C_Lead_ID, o.C_Bpartner_ID, c.Name, c.DocumentNo AS Value, 
                                        c.Email, c.C_Project_ID, o.Ref_Order_ID, o.C_Order_ID
                                        FROM C_Lead c LEFT JOIN C_Project o ON (c.C_Project_ID=o.C_Project_ID)
                                        WHERE c.IsActive='Y' AND (LOWER(c.Email) LIKE " + "'%" + tomail.Trim() + "%'");
                                    if (!emailDomains.Contains(mailDomain))
                                    {
                                        sql.Append(" OR LOWER(c.Email) LIKE " + "'%" + mailDomain + "%'");
                                    }
                                    sql.Append(") AND c.AD_Client_ID=" + AD_Client_ID);

                                    idr = DB.ExecuteReader(sql.ToString());
                                    dt = new DataTable();
                                    dt.Load(idr);
                                    idr.Close();
                                    if (dt.Rows.Count <= 0)
                                    {
                                        existRec = GetAttachedRecord(0, 0, mail.MessageID, folderName);

                                        if (existRec > 0)// Is mail already attached
                                        {
                                            retVal.Append("MailAlreadyAttachedWithParticularRecord");
                                            return;
                                        }
                                        AttachMail(mail, 0, 0, attachType, "", "", "");
                                    }
                                    else
                                    {
                                        AttachToLead(mail, dt, attachType, tomail.Trim());
                                    }
                                    continue;
                                }
                            }
                        }

                        else if (dt.Rows.Count <= 0)
                        {
                            if (sender == "C_Lead")
                            {
                                existRec = GetAttachedRecord(0, 0, mail.MessageID, folderName);

                                if (existRec > 0)// Is mail already attached
                                {
                                    retVal.Append("MailAlreadyAttachedWithParticularRecord");
                                    return;
                                }
                                AttachMail(mail, 0, 0, attachType, "", "", "");
                            }
                            else
                            {
                                retVal.Append("Email not found in database");
                            }
                            continue;
                        }
                        else
                        {
                            if (sender == "C_Lead")
                            {
                                AttachToLead(mail, dt, attachType, from);
                                continue;
                                //_tableID = PO.Get_Table_ID("C_Lead");
                                //DataRow[] dr = dt.Select($"Email = '{from.Replace("'", "''")}'");
                                //if (dr.Length == 1)
                                //{
                                //    existRec = GetAttachedRecord(_tableID, Util.GetValueOfInt(dr[0]["C_Lead_ID"]), mail.MessageID, folderName);
                                //    userOrBp = Msg.GetMsg(GetCtx(), "Lead");
                                //    if (existRec > 0)// Is mail already attached
                                //    {
                                //        retVal.Append("MailAlreadyAttachedWithParticularRecord");
                                //        continue;
                                //    }
                                //    AttachMail(mail, _tableID, Util.GetValueOfInt(dr[0]["C_Lead_ID"]), attachType, userOrBp, 
                                //        Util.GetValueOfString(dr[0]["Name"]), Util.GetValueOfString(dr[0]["Value"]));
                                //}
                                //else
                                //{
                                //    AttachMail(mail, 0, 0, attachType, "", "", "");
                                //}
                                //continue;
                            }

                            for (int j = 0; j < dt.Rows.Count; j++)
                            {
                                // Its go inside for user or busineespartner
                                if (sender != "C_Lead")
                                {
                                    string sqlQuery = "SELECT IsEmployee FROM C_BPartner WHERE C_BPartner_ID=" + Util.GetValueOfInt(dt.Rows[j]["C_BPartner_ID"]);
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
                                    existRec = GetAttachedRecord(_tableID, Util.GetValueOfInt(dt.Rows[j]["AD_User_ID"]), mail.MessageID, folderName);
                                    userOrBp = Msg.GetMsg(GetCtx(), "User");
                                }
                                //if (sender == "businessPartner")
                                if (sender == "C_BPartner")
                                {
                                    _tableID = PO.Get_Table_ID("C_BPartner");
                                    existRec = GetAttachedRecord(_tableID, Util.GetValueOfInt(dt.Rows[j]["C_BPartner_ID"]), mail.MessageID, folderName);
                                    userOrBp = Msg.GetMsg(GetCtx(), "BusinessPartner");
                                }
                                //if sender is lead
                                if (sender == "C_Lead")
                                {
                                    _tableID = PO.Get_Table_ID("C_Lead");
                                    existRec = GetAttachedRecord(_tableID, Util.GetValueOfInt(dt.Rows[j]["C_Lead_ID"]), mail.MessageID, folderName);
                                    userOrBp = Msg.GetMsg(GetCtx(), "Lead");
                                }
                                if (existRec > 0)// Is mail already attached
                                {
                                    retVal.Append("MailAlreadyAttachedWithParticularRecord");
                                    continue;
                                }

                                if (sender == "AD_User")
                                {
                                    record_ID = Util.GetValueOfInt(dt.Rows[j][0]);
                                }
                                if (sender == "C_BPartner")
                                {
                                    record_ID = Util.GetValueOfInt(dt.Rows[j][1]);
                                }
                                if (sender == "C_Lead")
                                {
                                    record_ID = Util.GetValueOfInt(dt.Rows[j][0]);
                                }
                                name = Util.GetValueOfString(dt.Rows[j]["Name"]);
                                value = Util.GetValueOfString(dt.Rows[j]["Value"]);
                                AttachMail(mail, _tableID, record_ID, attachType, userOrBp, name, value);
                            }
                        }
                    }
                    catch (Exception ex)
                    {
                    }
                }
            }
        }

        private void AttachToLead(dynamic mail, DataTable dt, string attachType, string from)
        {
            int _tableID, recordID, existRec;
            string userOrBp;
            DataRow[] dr = dt.Select($"Email = '{from.Replace("'", "''")}'");
            if (dr.Length == 1)
            {
                _tableID = PO.Get_Table_ID("C_Lead");
                recordID = Util.GetValueOfInt(dr[0]["C_Lead_ID"]);
                userOrBp = Msg.GetMsg(GetCtx(), "Lead");
                existRec = GetAttachedRecord(_tableID, recordID, mail.MessageID, folderName);
                if (existRec > 0)// Is mail already attached
                {
                    retVal.Append("MailAlreadyAttachedWithParticularRecord");
                    return;
                }
                AttachMail(mail, _tableID, recordID, attachType, userOrBp,
                    Util.GetValueOfString(dr[0]["Name"]), Util.GetValueOfString(dr[0]["Value"]));
            }
            else
            {
                existRec = GetAttachedRecord(0, 0, mail.MessageID, folderName);
                if (existRec > 0)// Is mail already attached
                {
                    retVal.Append("MailAlreadyAttachedWithParticularRecord");
                    return;
                }
                AttachMail(mail, 0, 0, attachType, "", "", "");
            }
        }

        private void AttachMail(dynamic mail, int _tableID, int record_ID, string attachType, string userOrBp, string name, string value)
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

            mAttachment.SetAD_Client_ID(GetCtx().GetAD_Client_ID());
            mAttachment.SetAD_Org_ID(GetCtx().GetAD_Org_ID());
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
            if (!mAttachment.Save())//save into database
            {
                retVal.Append("SaveError");
            }
            else if (record_ID > 0)
            {
                SendMailOrNotification(dsUser, GetCtx(), Msg.GetMsg(GetCtx(), "Emailrecievedwithsubject") + " = " + mail.Subject + Msg.GetMsg(GetCtx(), "ANDAttachto") + userOrBp + " = " + name, _tableID, record_ID, value);
            }
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
