﻿using System;
using System.Collections.Generic;
using System.Linq;
using System.Web;
using VAdvantage.DataBase;
using VAdvantage.Model;
using VAdvantage.Utility;

namespace VIS.Models
{
    public class MRequestTypeModel
    {
        /// <summary>
        /// GetDefault R_Status_ID
        /// </summary>
        /// <param name="ctx"></param>
        /// <param name="fields"></param>
        /// <returns></returns>
        public int GetDefaultR_Status_ID(Ctx ctx,string fields)
        {            
            string[] paramValue = fields.Split(',');
            int R_RequestType_ID;

            //Assign parameter value
            R_RequestType_ID = Util.GetValueOfInt(paramValue[0].ToString());
            //End Assign parameter value

            MRequestType rt = MRequestType.Get(ctx, R_RequestType_ID);
            int R_Status_ID = rt.GetDefaultR_Status_ID();
            return R_Status_ID;
        }

        /// <summary>
        /// Get Mail text on Request
        /// </summary>
        /// <param name="MailText_ID">MailText_ID</param>
        /// <returns>Result</returns>
        public string GetMailText(int MailText_ID)
        {
            return Util.GetValueOfString(DB.ExecuteScalar("SELECT MailText FROM R_MailText WHERE R_MailText_ID=" + MailText_ID, null, null));
        }

        /// <summary>
        /// Get Response text on Request
        /// </summary>
        /// <param name="Response_ID">Response ID</param>
        /// <returns>Result</returns>
        public string GetResponseText(int Response_ID)
        {
            return Util.GetValueOfString(DB.ExecuteScalar("SELECT ResponseText FROM R_StandardResponse WHERE R_StandardResponse_ID=" + Response_ID, null, null));
        }

        /// <summary>
        /// Get Resolution text on Request
        /// </summary>
        /// <param name="Resolution_ID"></param>
        /// <returns>Result</returns>
        public string GetResolutionText(int Resolution_ID) //VIS_0336 this method is for fetching the comments(help) of selected resolution in request window.
        {
            return Util.GetValueOfString(DB.ExecuteScalar("SELECT HELP FROM R_Resolution WHERE R_Resolution_ID=" + Resolution_ID, null, null));
        }

    }
}