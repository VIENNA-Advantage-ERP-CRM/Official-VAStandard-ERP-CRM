using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using VAdvantage.Utility;
using System.Data;
using VAdvantage.Logging;
using VAdvantage.DataBase;
using VAdvantage.ProcessEngine;
using VAdvantage.Model;
//using ViennaAdvantage.Model;

namespace ViennaAdvantage.Process
{
    class SubscribeInterestArea : SvrProcess
    {

        int _C_Lead_ID;
        int R_InterestArea_ID;
        protected override void Prepare()
        {
            _C_Lead_ID = GetRecord_ID();
            ProcessInfoParameter[] para = GetParameter();
            for (int i = 0; i < para.Length; i++)
            {
                String name = para[i].GetParameterName();
                if (para[i].GetParameter() == null)
                {
                    ;
                }
                else if (name.Equals("R_InterestArea_ID"))
                {
                    R_InterestArea_ID = Util.GetValueOfInt(Util.GetValueOfDecimal(para[i].GetParameter()));
                }
                else
                {

                    log.Log(Level.SEVERE, "Unknown Parameter: " + name);
                }
            }

        }
        protected override String DoIt()
        {

            VAdvantage.Model.X_C_Lead lead = new VAdvantage.Model.X_C_Lead(GetCtx(), _C_Lead_ID, Get_Trx());
            if (!(R_InterestArea_ID > 0))
            {
                return Msg.GetMsg(GetCtx(), "NoInterestAreaIsSelected");

            }
            //VAI050-Changes to add suspect to ineterest area
            string sql = @"SELECT TableName, AD_Table_ID  FROM AD_Table 
                          WHERE TableName IN ('C_Lead', 'VA061_Suspect')";

            DataSet ds = DB.ExecuteDataset(sql);
            int leadTable_ID = 0, SuspectID = 0;
            if (ds != null && ds.Tables.Count > 0 && ds.Tables[0].Rows.Count > 0)
            {
                foreach (System.Data.DataRow row in ds.Tables[0].Rows)
                {
                    string tableName = row["TableName"].ToString();
                    int id = Util.GetValueOfInt(row["AD_Table_ID"]);

                    if (tableName == "C_Lead")
                        leadTable_ID = id;
                    else if (tableName == "VA061_Suspect")
                        SuspectID = id;
                }
            }
            if (leadTable_ID == GetTable_ID())
            {
                if (lead.GetC_BPartner_ID() != 0)
                {

                    VAdvantage.Model.X_R_ContactInterest customer = new VAdvantage.Model.X_R_ContactInterest(GetCtx(), 0, Get_Trx());
                    customer.SetR_InterestArea_ID(R_InterestArea_ID);
                    customer.SetC_BPartner_ID(lead.GetC_BPartner_ID());
                    String query = "Select ad_user_id from ad_user where c_bpartner_id= " + lead.GetC_BPartner_ID();
                    int UserId = Util.GetValueOfInt(DB.ExecuteScalar(query));
                    customer.SetAD_User_ID(UserId);
                    query = "Select C_BPartner_Location_id from C_BPartner_Location where c_bpartner_id= " + lead.GetC_BPartner_ID();

                    int Id = Util.GetValueOfInt(DB.ExecuteScalar(query));
                    VAdvantage.Model.X_C_BPartner_Location loc = new VAdvantage.Model.X_C_BPartner_Location(GetCtx(), Id, Get_Trx());
                    customer.SetC_BPartner_Location_ID(Id);
                    customer.SetPhone(loc.GetPhone());
                    customer.SetFax(loc.GetFax());

                    VAdvantage.Model.X_AD_User us = new VAdvantage.Model.X_AD_User(GetCtx(), UserId, Get_Trx());
                    customer.SetC_Job_ID(us.GetC_Job_ID());
                    customer.SetSubscribeDate(DateTime.Today);
                    query = "Select Email from ad_user where ad_user_id= " + UserId;
                    String mail = Util.GetValueOfString(DB.ExecuteScalar(query));
                    customer.SetEMail(mail);
                    if (!customer.Save())
                    {
                        return GetRetrievedError(customer, "NotSubscribed");
                        //return Msg.GetMsg(GetCtx(), "NotSubscribed");

                    }
                    lead.SetR_InterestArea_ID(customer.GetR_InterestArea_ID());
                    if (lead.Save())
                    {
                    }
                    return Msg.GetMsg(GetCtx(), "SubscribedDone");



                }
                else if (lead.GetRef_BPartner_ID() != 0)
                {
                    VAdvantage.Model.X_R_ContactInterest Prospect = new VAdvantage.Model.X_R_ContactInterest(GetCtx(), 0, Get_Trx());
                    Prospect.SetR_InterestArea_ID(R_InterestArea_ID);
                    Prospect.SetC_BPartner_ID(lead.GetRef_BPartner_ID());
                    String query = "Select ad_user_id from ad_user where c_bpartner_id= " + lead.GetRef_BPartner_ID();
                    int UserId = Util.GetValueOfInt(DB.ExecuteScalar(query));
                    Prospect.SetAD_User_ID(UserId);
                    query = "Select C_BPartner_Location_id from C_BPartner_Location where c_bpartner_id= " + lead.GetRef_BPartner_ID();

                    int Id = Util.GetValueOfInt(DB.ExecuteScalar(query));
                    VAdvantage.Model.X_C_BPartner_Location loc = new VAdvantage.Model.X_C_BPartner_Location(GetCtx(), Id, Get_Trx());
                    Prospect.SetC_BPartner_Location_ID(Id);
                    Prospect.SetPhone(loc.GetPhone());
                    Prospect.SetFax(loc.GetFax());

                    VAdvantage.Model.X_AD_User us = new VAdvantage.Model.X_AD_User(GetCtx(), UserId, Get_Trx());
                    Prospect.SetC_Job_ID(us.GetC_Job_ID());
                    Prospect.SetSubscribeDate(DateTime.Today);
                    query = "Select Email from ad_user where ad_user_id= " + UserId;
                    String mail = Util.GetValueOfString(DB.ExecuteScalar(query));
                    Prospect.SetEMail(mail);
                    if (!Prospect.Save())
                    {
                        return GetRetrievedError(Prospect, "NotSubscribed");
                        //return Msg.GetMsg(GetCtx(), "NotSubscribed");

                    }
                    lead.SetR_InterestArea_ID(Prospect.GetR_InterestArea_ID());
                    if (lead.Save())
                    {
                    }
                    return Msg.GetMsg(GetCtx(), "SubscribedDone");
                }
                else
                {

                    VAdvantage.Model.X_vss_lead_interestarea Ilead = new VAdvantage.Model.X_vss_lead_interestarea(GetCtx(), 0, Get_Trx());
                    Ilead.SetC_Lead_ID(lead.GetC_Lead_ID());
                    Ilead.SetR_InterestArea_ID(R_InterestArea_ID);
                    Ilead.SetAD_User_ID(lead.GetAD_User_ID());
                    Ilead.SetBPName(lead.GetBPName());
                    Ilead.SetEMail(lead.GetEMail());
                    Ilead.SetPhone(lead.GetPhone());
                    Ilead.SetFax(lead.GetFax());
                    Ilead.SetAddress1(lead.GetAddress1());
                    Ilead.SetAddress2(lead.GetAddress2());
                    Ilead.SetC_City_ID(lead.GetC_City_ID());
                    Ilead.SetCity(lead.GetCity());
                    Ilead.SetC_Region_ID(lead.GetC_Region_ID());
                    Ilead.SetRegionName(lead.GetRegionName());
                    Ilead.SetC_Country_ID(lead.GetC_Country_ID());
                    Ilead.SetPostal(lead.GetPostal());
                    Ilead.SetSubscribeDate(DateTime.Today);
                    if (!Ilead.Save())
                    {
                        return GetRetrievedError(Ilead, "NotSubscribed");
                        //return Msg.GetMsg(GetCtx(), "NotSubscribed");

                    }
                    lead.SetR_InterestArea_ID(Ilead.GetR_InterestArea_ID());
                    if (lead.Save())
                    {
                    }
                    return Msg.GetMsg(GetCtx(), "SubscribedDone");


                }
            }
            //VAI050-Add suspect to ineterest area
            if (SuspectID == GetTable_ID() && Env.IsModuleInstalled("VA061_"))
            {
                string query = @"SELECT  AD_Client_ID,AD_Org_ID,VA061_Suspect_ID, Value, Name, VA061_Company, EMail, City, 
                                 C_Country_ID, VA061_LastEngagementDate  FROM VA061_Suspect 
                                  WHERE VA061_Suspect_ID = " + GetRecord_ID();
                DataSet dsSuspect = DB.ExecuteDataset(query);
                if (dsSuspect != null && dsSuspect.Tables.Count > 0 && dsSuspect.Tables[0].Rows.Count > 0)
                {

                    MTable tbl = new MTable(GetCtx(), MTable.Get_Table_ID("VA061_SuspectInterestArea"), null);
                    PO obj = tbl.GetPO(GetCtx(), GetRecord_ID(), null);
                    obj.SetAD_Client_ID(Util.GetValueOfInt(dsSuspect.Tables[0].Rows[0]["AD_Client_ID"]));
                    obj.SetAD_Org_ID(Util.GetValueOfInt(dsSuspect.Tables[0].Rows[0]["AD_Org_ID"]));
                    obj.Set_Value("R_InterestArea_ID", R_InterestArea_ID);
                    obj.Set_Value("Value", dsSuspect.Tables[0].Rows[0]["Value"]);
                    obj.Set_Value("VA061_Suspect_ID", dsSuspect.Tables[0].Rows[0]["VA061_Suspect_ID"]);
                    obj.Set_Value("Name", dsSuspect.Tables[0].Rows[0]["Name"]);
                    obj.Set_Value("VA061_Company", dsSuspect.Tables[0].Rows[0]["VA061_Company"]);
                    obj.Set_Value("EMail", dsSuspect.Tables[0].Rows[0]["EMail"]);
                    obj.Set_Value("City", dsSuspect.Tables[0].Rows[0]["City"]);
                    obj.Set_Value("C_Country_ID", dsSuspect.Tables[0].Rows[0]["C_Country_ID"]);
                    obj.Set_Value("VA061_LastEngagementDate", dsSuspect.Tables[0].Rows[0]["VA061_LastEngagementDate"]);
                    if (!obj.Save())
                    {
                        return GetRetrievedError(obj, "NotSubscribed");

                    }
                }

                return Msg.GetMsg(GetCtx(), "SubscribedDone");

            }
            return Msg.GetMsg(GetCtx(), "NotSubscribed");

        }




    }
}
