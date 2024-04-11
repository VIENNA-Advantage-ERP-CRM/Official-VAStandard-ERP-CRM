/********************************************************
 * Project Name   : VAdvantage
 * Class Name     : RfQCreate
 * Purpose        : Create RfQ Response from RfQ Topic
 * Class Used     : ProcessEngine.SvrProcess
 * Chronological    Development
 * Raghunandan     11-Aug.-2009
  ******************************************************/
using System;
using System.Data;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;
using VAdvantage.DataBase;
using VAdvantage.Logging;
using VAdvantage.Model;
using VAdvantage.ProcessEngine;
using VAdvantage.Utility;

namespace VAdvantage.Process
{
    public class RfQCreate : ProcessEngine.SvrProcess
    {
        //Send RfQ				
        private bool _IsSendRfQ = false;
        //	RfQ						
        private int _C_RfQ_ID = 0;
        int OrgID = 0;
        int TotalRecepients = 0;
        MRfQ rfq = null;

        /// <summary>
        /// Prepare - e.g., get Parameters.
        /// </summary>
        protected override void Prepare()
        {
            ProcessInfoParameter[] para = GetParameter();
            for (int i = 0; i < para.Length; i++)
            {
                String name = para[i].GetParameterName();
                if (para[i].GetParameter() == null)
                {
                    ;
                }
                else if (name.Equals("IsSendRfQ"))
                {
                    _IsSendRfQ = "Y".Equals(para[i].GetParameter());
                }
                else
                {
                    log.Log(Level.SEVERE, "Unknown Parameter: " + name);
                }
            }
            _C_RfQ_ID = GetRecord_ID();
        }

        /// <summary>
        /// Perform Process.
        /// </summary>
        /// <returns>Message (translated text)</returns>
        protected override String DoIt()
        {
            rfq = new MRfQ(GetCtx(), _C_RfQ_ID, Get_TrxName());
            log.Info("doIt - " + rfq + ", Send=" + _IsSendRfQ);
            int counter = 0;
            int sent = 0;
            int notSent = 0;
            String retValue = string.Empty;

            ////ErrorLog.FillErrorLog("", "", "doIt - " + rfq + ", Send=" + _IsSendRfQ, VAdvantage.Framework.Message.MessageType.INFORMATION);

            String error = rfq.CheckQuoteTotalAmtOnly();
            if (error != null && error.Length > 0)
            {
                throw new Exception(error);
            }
            if (!Env.IsModuleInstalled("VA068_"))
            {
                // VIS0060: Get Existing Rfq Response
                int rfqResponse_ID = Util.GetValueOfInt(DB.ExecuteScalar("SELECT VAS_Response_ID FROM VAS_Response WHERE C_RfQ_ID=" + _C_RfQ_ID, null, Get_Trx()));

                if (rfqResponse_ID == 0)
                {
                    MTable tbl = new MTable(GetCtx(), MTable.Get_Table_ID("VAS_Response"), Get_Trx());
                    PO rfqResponse = tbl.GetPO(GetCtx(), 0, Get_Trx());
                    rfqResponse.SetClientOrg(rfq);
                    rfqResponse.Set_ValueNoCheck("C_RfQ_ID", rfq.GetC_RfQ_ID());
                    rfqResponse.Set_Value("Name", rfq.GetName());
                    if (!rfqResponse.Save())
                    {
                        ValueNamePair pp = VLogger.RetrieveError();
                        if (pp != null && !string.IsNullOrEmpty(pp.GetName()))
                            return "Could not create Rfq Response. " + pp.GetName();
                        else
                            return "Could not create Rfq Response";
                    }
                    else
                    {
                        rfqResponse_ID = rfqResponse.Get_ID();
                    }
                }

                //	Get all existing responses
                MRfQResponse[] responses = rfq.GetResponses(false, false);

                //	Topic
                MRfQTopic topic = new MRfQTopic(GetCtx(), rfq.GetC_RfQ_Topic_ID(), Get_TrxName());
                MRfQTopicSubscriber[] subscribers = topic.GetSubscribers();
                for (int i = 0; i < subscribers.Length; i++)
                {
                    MRfQTopicSubscriber subscriber = subscribers[i];
                    bool skip = false;
                    //	existing response
                    for (int r = 0; r < responses.Length; r++)
                    {
                        if (Env.IsModuleInstalled("VA068_") && subscriber.Get_ValueAsInt("VA068_VendorRegistration_ID") > 0
                            && subscriber.Get_ValueAsInt("VA068_VendorRegistration_ID") == Util.GetValueOfInt(responses[r].Get_Value("VA068_VendorRegistration_ID"))
                                && subscriber.Get_ValueAsInt("VA068_RegisteredLocation_ID") == Util.GetValueOfInt(responses[r].Get_Value("VA068_RegisteredLocation_ID")))
                        {
                            skip = true;
                            break;
                        }

                        if (subscriber.GetC_BPartner_ID() > 0 && subscriber.GetC_BPartner_ID() == responses[r].GetC_BPartner_ID()
                            && subscriber.GetC_BPartner_Location_ID() == responses[r].GetC_BPartner_Location_ID())
                        {
                            skip = true;
                            break;
                        }
                    }
                    if (skip)
                    {
                        continue;
                    }

                    //	Create Response
                    MRfQResponse response = new MRfQResponse(rfq, subscriber, rfqResponse_ID);
                    if (response.Get_ID() == 0) //	no lines
                    {
                        continue;
                    }

                    counter++;
                    if (_IsSendRfQ)//send mail check
                    {
                        if (Env.IsModuleInstalled("VA068_") && subscriber.Get_ValueAsInt("VA068_VendorRegistration_ID") > 0
                            && response.SendRfqToVendors())
                        {
                            sent++;
                        }
                        else if (subscriber.GetC_BPartner_ID() > 0 && response.SendRfQ())
                        {
                            sent++;
                        }
                        else
                        {
                            notSent++;
                        }
                    }
                }   //	for all subscribers

                retValue += "@Created@ " + counter;
            }
            else
            {
                //VIS430:Set Published Checkbox true when click on Publish and Invite Button on RFQ tab of RFQ Window.

                int no = DB.ExecuteQuery(@"UPDATE C_RfQ SET VA068_IsPublished='Y' WHERE C_RfQ_ID =" + GetRecord_ID(), null, Get_Trx());
                
                //VIS0336:for sendinf the mails to the subscribers
                //	Topic 
                MRfQTopic topic = new MRfQTopic(GetCtx(), rfq.GetC_RfQ_Topic_ID(), Get_TrxName());
                MRfQTopicSubscriber[] subscribers = topic.GetSubscribers();
                for (int i = 0; i < subscribers.Length; i++)
                {
                    MRfQTopicSubscriber subscriber = subscribers[i];

                    if (_IsSendRfQ)//send mail check
                    {
                        if (subscriber.Get_ValueAsInt("VA068_VendorRegistration_ID") > 0
                            && SendRfqToVendors(Util.GetValueOfInt(subscriber.Get_Value("VA068_RegisteredUser_ID"))))
                        {
                            sent++;
                        }
                        else if (subscriber.GetC_BPartner_ID() > 0 &&
                           SendRfQ(subscriber.GetAD_User_ID()))

                        {
                            sent++;
                        }

                        else
                        {
                            notSent++;
                        }
                    }
                }   //	for all subscribers

                ///VIS0336:- changes done for sending the mail (for new invitation)notifications to the register vendors
                StringBuilder sql = new StringBuilder();
                OrgID = rfq.GetAD_Org_ID();

                sql.Clear();
                sql.Append("SELECT r.VA068_Email,r.VA068_VendorRegistration_ID,(SELECT AD_Table_ID FROM AD_Table WHERE " +
                    " TableName = 'VA068_VendorRegistration') AS TableId ,u.VA068_FirstName FROM VA068_VendorRecomend  r " +
                    " LEFT JOIN VA068_RegisteredUser u on u.VA068_VendorRegistration_ID=r.VA068_VendorRegistration_ID " +
                    "  WHERE r.C_RfQLine_ID IN (SELECT C_RfQLine_ID  FROM C_RfQLine WHERE C_RfQ_ID=" + rfq.GetC_RfQ_ID() + ") AND " +
                    "  r.VA068_VendorRegistration_ID > 0");
                DataSet ds = DB.ExecuteDataset(sql.ToString(), null, null);
                if (ds != null && ds.Tables.Count > 0 && ds.Tables[0].Rows.Count > 0)
                {
                    TotalRecepients = ds.Tables[0].Rows.Count;
                    Thread thread = new Thread(new ThreadStart(() => SendMail(ds)));
                    thread.Start();
                }

                retValue = " @InviteSent@=" + TotalRecepients;
            }


            if (_IsSendRfQ)
            {
                retValue += " - @IsSendRfQ@=" + sent + " - @Error@=" + notSent;
            }
            return retValue;
        }


        /// <summary>
        /// VIS0336:using this method for sending the mails to register user.
        /// </summary>
        /// <param name="ds">ds</param>
        private void SendMail(DataSet ds)
        {
            StringBuilder sql = new StringBuilder();
            String MailHeader = null;
            string MailText = null;
            string MailText2 = null;
            string MailText3 = null;
            string Mailaddress = string.Empty;
            string VendorName = "Vendor", OrgName = string.Empty;

            VA068_RegistedUser ret = new VA068_RegistedUser();
            if (ds != null && ds.Tables.Count > 0 && ds.Tables[0].Rows.Count > 0)
            {
                string url = GetCtx().GetApplicationUrl();
                url += "Areas/VA068/WebPages/VendorPreRegistration.aspx";

                sql.Clear();
                sql.Append("SELECT rs.VA068_RegistrationSetting_ID,(SELECT Name FROM AD_Org WHERE AD_Org_ID=rs.AD_Org_ID) AS OrgName," +
                    " rs.AD_Org_ID,rs.R_MailText_ID,mt.MailHeader,mt.MailText,mt.MailText2,mt.MailText3 " +
                    " FROM VA068_RegistrationSetting rs " +
                    " LEFT JOIN R_MailText mt ON rs.R_MailText_ID = mt.R_MailText_ID" +
                    " WHERE rs.IsActive = 'Y' AND rs.AD_Org_ID IN (0 , " + OrgID + ") ORDER BY rs.Ad_Org_ID DESC ");

                DataSet ds1 = DB.ExecuteDataset(sql.ToString(), null, null);
                if (ds1 != null && ds1.Tables.Count > 0 && ds1.Tables[0].Rows.Count > 0)
                {

                    MailHeader = Util.GetValueOfString(ds1.Tables[0].Rows[0]["MailHeader"].ToString());
                    MailText = Util.GetValueOfString(ds1.Tables[0].Rows[0]["MailText"].ToString());
                    MailText2 = Util.GetValueOfString(ds1.Tables[0].Rows[0]["MailText2"].ToString());
                    MailText3 = Util.GetValueOfString(ds1.Tables[0].Rows[0]["MailText3"].ToString());
                    OrgName = Util.GetValueOfString(ds1.Tables[0].Rows[0]["OrgName"]);

                }

                for (int i = 0; i < ds.Tables[0].Rows.Count; i++)
                {
                    if (Util.GetValueOfString(ds.Tables[0].Rows[i]["VA068_FirstName"]) != "")
                    {
                        VendorName = Util.GetValueOfString(ds.Tables[0].Rows[i]["VA068_FirstName"]);
                    }

                    DateTime t = DateTime.Now.ToUniversalTime();
                    string queryString = "?inviteID=" + SecureEngine.Encrypt(OrgID.ToString()) + "&lang=" + GetCtx().GetAD_Language() + "&RecordID=" + SecureEngine.Encrypt(Util.GetValueOfInt(Util.GetValueOfInt(ds.Tables[0].Rows[i]["VA068_VendorRegistration_ID"])).ToString())
                        + "&SchemaID=" + SecureEngine.Encrypt(Util.GetValueOfInt(ds.Tables[0].Rows[i]["TableId"]).ToString()) + "&session=" + SecureEngine.Encrypt(t.ToString());
                    ret.Message = "";
                    ret.Url = url + queryString;

                    EMail objMail = new EMail(GetCtx(), "", "", "", "", "", "", true, false);
                    objMail.SetSubject(MailHeader);
                    objMail.SetMessageHTML(MailText.Replace("@VendorContactName@", VendorName + " " + MailText2)
                                          + MailText3.Replace("@Organisation@", OrgName)
                                    .Replace("@VA068Link@", url + queryString.Replace("&registerID", "&amp;registerID")));


                    objMail.AddTo(Util.GetValueOfString(ds.Tables[0].Rows[i]["VA068_Email"]), "");
                    string res1 = objMail.Send();

                    StringBuilder res = new StringBuilder();
                    if (res1 != "OK")           // if mail not sent....
                    {
                        if (res1 == "AuthenticationFailed.")
                        {
                            res.Append("AuthenticationFailed");
                            log.Fine(res.ToString());
                            res.Append(" " + Msg.GetMsg(GetCtx(), "MailNotSentTo"));
                        }
                        else if (res1 == "ConfigurationIncompleteOrNotFound")
                        {
                            res.Append("ConfigurationIncompleteOrNotFound");
                            log.Fine(res.ToString());

                        }
                        else
                        {
                            res.Append(" " + Msg.GetMsg(GetCtx(), "MailNotSentTo") + ": " + Mailaddress);
                            log.Fine(res.ToString());
                        }
                    }
                    else
                    {
                        if (!res.ToString().Contains("MailSent"))
                        {
                            res.Append("MailSent");
                        }

                    }
                }
            }

        }
        /// <summary>
        /// VIS0336:using this method for sending the mails to subscriber register vendor.
        /// </summary>
        /// <param name="AD_User_ID">AD_User_ID</param>
        /// <param name="ADClientID">ADClientID</param>
        /// <returns>true/false</returns>
        public bool SendRfQ(int AD_User_ID)
        {
            bool mailSent = false;
            try
            {
                string NotificationType = null;
                MUser to = MUser.Get(GetCtx(), AD_User_ID);
                MClient client = MClient.Get(GetCtx());
                MMailText mtext = new MMailText(GetCtx(), rfq.GetR_MailText_ID(), Get_TrxName());

                if (to.Get_ID() == 0 || to.GetEMail() == null || to.GetEMail().Length == 0)
                {
                    log.Log(Level.SEVERE, "No User or no EMail - " + to);
                    return false;
                }

                // Check if mail template is set for RfQ window, if not then get from RfQ Topic window.
                if (mtext.GetR_MailText_ID() == 0)
                {
                    MRfQTopic mRfQTopic = new MRfQTopic(GetCtx(), rfq.GetC_RfQ_Topic_ID(), Get_TrxName());
                    if (mRfQTopic.GetC_RfQ_Topic_ID() > 0)
                    {
                        mtext = new MMailText(GetCtx(), mRfQTopic.GetR_MailText_ID(), Get_TrxName());
                    }
                }

                //Replace the email template constants with tables values.
                StringBuilder message = new StringBuilder();
                mtext.SetPO(rfq, true);
                message.Append(mtext.GetMailText(true).Equals(string.Empty) ? "** No Email Body" : mtext.GetMailText(true));

                String subject = String.IsNullOrEmpty(mtext.GetMailHeader()) ? "** No Subject" : mtext.GetMailHeader(); ;

                EMail email = client.CreateEMail(to.GetEMail(), to.GetName(), subject, message.ToString());
                if (email == null)
                {
                    return false;
                }
                email.AddAttachment(CreatePDF());
                if (EMail.SENT_OK.Equals(email.Send()))
                {
                    mailSent = true;

                }

                if (NotificationType == null)
                    NotificationType = to.GetNotificationType();

                //	Send Note
                if (X_AD_User.NOTIFICATIONTYPE_Notice.Equals(NotificationType)
                    || X_AD_User.NOTIFICATIONTYPE_EMailPlusNotice.Equals(NotificationType))
                {
                    MNote note = new MNote(GetCtx(), "Response", to.GetAD_User_ID(), rfq.GetAD_Client_ID(), 0, Get_TrxName());
                    note.SetRecord(X_C_RfQ.Table_ID, _C_RfQ_ID);
                    note.SetReference(subject);
                    note.SetTextMsg(message.ToString());
                    note.SetAD_Org_ID(0);
                    note.Save();
                }
            }
            catch (Exception ex)
            {
                log.Severe(ex.ToString());
                //MessageBox.Show("error--" + ex.ToString());
            }
            return mailSent;
        }
        /// <summary>
        /// VIS0336:using this method for sending the mails to subscribers BPartner.
        /// </summary>
        /// <param name="RegisterUserID"></param>
        /// <returns></returns>
        public bool SendRfqToVendors(int RegisterUserID)
        {
            string mail = "", name = "", notificationType = "";
            int ad_user_ID = 0;
            bool mailSent = false;
            try
            {
                DataSet ds = DB.ExecuteDataset(@"SELECT ru.VA068_Email, ru.VA068_FirstName, au.AD_User_ID, au.NotificationType
                    FROM VA068_RegisteredUser ru LEFT JOIN AD_User au ON (ru.AD_User_ID = au.AD_User_ID) 
                    WHERE ru.VA068_RegisteredUser_ID = " + RegisterUserID, null, Get_Trx());
                if (ds != null && ds.Tables.Count > 0 && ds.Tables[0].Rows.Count > 0)
                {
                    mail = Util.GetValueOfString(ds.Tables[0].Rows[0]["VA068_Email"]);
                    name = Util.GetValueOfString(ds.Tables[0].Rows[0]["VA068_FirstName"]);
                    ad_user_ID = Util.GetValueOfInt(ds.Tables[0].Rows[0]["AD_User_ID"]);
                    notificationType = Util.GetValueOfString(ds.Tables[0].Rows[0]["VA068_FirstName"]);
                }

                MClient client = MClient.Get(GetCtx());
                MMailText mtext = new MMailText(GetCtx(), rfq.GetR_MailText_ID(), Get_TrxName());

                if (RegisterUserID == 0 || string.IsNullOrEmpty(mail))
                {
                    log.Log(Level.SEVERE, "No User or no EMail - " + GetName());
                    return false;
                }

                // Check if mail template is set for RfQ window, if not then get from RfQ Topic window.
                if (mtext.GetR_MailText_ID() == 0)
                {
                    MRfQTopic mRfQTopic = new MRfQTopic(GetCtx(), rfq.GetC_RfQ_Topic_ID(), Get_TrxName());
                    if (mRfQTopic.GetC_RfQ_Topic_ID() > 0)
                    {
                        mtext = new MMailText(GetCtx(), mRfQTopic.GetR_MailText_ID(), Get_TrxName());
                    }
                }

                //Replace the email template constants with tables values.
                StringBuilder message = new StringBuilder();
                mtext.SetPO(rfq, true);
                message.Append(mtext.GetMailText(true).Equals(string.Empty) ? "** No Email Body" : mtext.GetMailText(true));

                String subject = String.IsNullOrEmpty(mtext.GetMailHeader()) ? "** No Subject" : mtext.GetMailHeader(); ;

                EMail email = client.CreateEMail(mail, name, subject, message.ToString());
                if (email == null)
                {
                    return false;
                }
                email.AddAttachment(CreatePDF());
                if (EMail.SENT_OK.Equals(email.Send()))
                {
                    mailSent = true;
                    //SetDateInvited(DateTime.Now);
                    // Save();
                }

                //	Send Note
                if (ad_user_ID > 0 && (X_AD_User.NOTIFICATIONTYPE_Notice.Equals(notificationType)
                    || X_AD_User.NOTIFICATIONTYPE_EMailPlusNotice.Equals(notificationType)))
                {
                    MNote note = new MNote(GetCtx(), "Response", ad_user_ID, GetAD_Client_ID(), 0, Get_TrxName());
                    note.SetRecord(X_C_RfQ.Table_ID, _C_RfQ_ID);
                    note.SetReference(subject);
                    note.SetTextMsg(message.ToString());
                    note.SetAD_Org_ID(0);
                    note.Save();
                }

            }
            catch (Exception ex)
            {
                log.Severe(ex.ToString());
                //MessageBox.Show("error--" + ex.ToString());
            }
            return mailSent;
        }

        /// <summary>
        /// Create PDF file
        /// </summary>
        /// <returns>File or null</returns>
        public FileInfo CreatePDF(FileInfo file)
        {

            return file;
        }
        /// <summary>
        /// Create PDF file
        /// </summary>
        /// <returns>File or null</returns>
        public FileInfo CreatePDF()
        {
            return CreatePDF(null);
        }

        public class VA068_RegistedUser
        {
            public string Message { get; set; }
            public string Url { get; set; }
        }
    }
}
