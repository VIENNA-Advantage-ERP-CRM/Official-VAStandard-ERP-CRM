﻿using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using VAdvantage.Utility;
using System.Data;
using VAdvantage.Logging;
using VAdvantage.DataBase;
using VAdvantage.ProcessEngine;
using VAdvantage.Model;

namespace VAdvantage.Process
{
    class SubscribeCustomerInterestArea : SvrProcess
    {

        int _C_BPartner_ID;
        int R_InterestArea_ID;
        protected override void Prepare()
        {
            _C_BPartner_ID = GetRecord_ID();
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
            if (!(R_InterestArea_ID > 0))
            {
                return Msg.GetMsg(GetCtx(), "NoInterestAreaIsSelected");
            }
            else
            {
                String query = "SELECT AD_User_ID FROM AD_User WHERE C_BPartner_ID= " + _C_BPartner_ID;
                int UserId = Util.GetValueOfInt(DB.ExecuteScalar(query));
                //VAI050-Contact details are mandatory
                if (UserId == 0)
                {
                    return Msg.GetMsg(GetCtx(), "VAS_ContactDetailsNotFound");
                }
                X_R_ContactInterest customer = new X_R_ContactInterest(GetCtx(), 0, Get_TrxName());
                customer.SetR_InterestArea_ID(R_InterestArea_ID);
                customer.SetC_BPartner_ID(_C_BPartner_ID);
                customer.SetAD_User_ID(UserId);
                query = "SELECT C_BPartner_Location_ID FROM C_BPartner_Location WHERE C_BPartner_ID= " + _C_BPartner_ID;

                int Id = Util.GetValueOfInt(DB.ExecuteScalar(query));
                VAdvantage.Model.X_C_BPartner_Location loc = new VAdvantage.Model.X_C_BPartner_Location(GetCtx(), Id, Get_TrxName());
                customer.SetC_BPartner_Location_ID(Id);
                customer.SetPhone(loc.GetPhone());
                customer.SetFax(loc.GetFax());

                VAdvantage.Model.X_AD_User us = new VAdvantage.Model.X_AD_User(GetCtx(), UserId, Get_TrxName());
                customer.SetC_Job_ID(us.GetC_Job_ID());
                customer.SetSubscribeDate(DateTime.Today);
                query = "SELECT Email FROM AD_User WHERE AD_User_ID= " + UserId;
                String mail = Util.GetValueOfString(DB.ExecuteScalar(query));
                customer.SetEMail(mail);
                if (!customer.Save())
                {
                    return Msg.GetMsg(GetCtx(), "VAS_NotSubscribed");

                }
                return Msg.GetMsg(GetCtx(), "SubscribedDone");
            }
        }
    }
}
