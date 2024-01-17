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
            MRfQ rfq = new MRfQ(GetCtx(), _C_RfQ_ID, Get_TrxName());
            log.Info("doIt - " + rfq + ", Send=" + _IsSendRfQ);
            int counter = 0;
            int sent = 0;
            int notSent = 0;
            int rfqResponse_ID = Util.GetValueOfInt(DB.ExecuteScalar("SELECT VAS_Response_ID FROM VAS_Response WHERE C_RfQ_ID=" + _C_RfQ_ID, null, Get_Trx()));

            ////ErrorLog.FillErrorLog("", "", "doIt - " + rfq + ", Send=" + _IsSendRfQ, VAdvantage.Framework.Message.MessageType.INFORMATION);
            if (!Env.IsModuleInstalled("VA068_"))
            {
                String error = rfq.CheckQuoteTotalAmtOnly();
                if (error != null && error.Length > 0)
                {
                    throw new Exception(error);
                }

                // VIS0060: Get Existing Rfq Response
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
                        if (Env.IsModuleInstalled("VA068_") && subscriber.Get_ValueAsInt("VA068_VendorRegistration_ID") > 0 && response.SendRfqToVendors())
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

            }
            else
            {
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
                        if (Env.IsModuleInstalled("VA068_") && subscriber.Get_ValueAsInt("VA068_VendorRegistration_ID") > 0 && response.SendRfqToVendors())
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

                ///VIS0336:- changes done for sending the mail notifications to the registre vendors
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
                    Thread thread = new Thread(new ThreadStart(() => SendMail(ds)));
                    thread.Start();
                }
            }

            String retValue = "@Created@ " + counter;
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
                            // return Msg.GetMsg(GetCtx(), "error");
                            //return null;
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

        public class VA068_RegistedUser
        {
            public string Message { get; set; }
            public string Url { get; set; }
        }
    }
}
