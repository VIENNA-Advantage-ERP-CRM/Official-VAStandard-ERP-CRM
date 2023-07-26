/********************************************************
 * Project Name   : VAdvantage
 * Class Name     : CreateRecurringFromGLJournalBatch
 * Purpose        : Create Recurring process
 * Class Used     : ProcessEngine.SvrProcess
 * Chronological    Development
 * Task ID        : 2260
 * Date           : 21-July-2023
  ******************************************************/
using System;
using VAdvantage.Model;
using VAdvantage.DataBase;
using VAdvantage.Logging;
using VAdvantage.Utility;

using VAdvantage.ProcessEngine;
using System.Data;
using System.Collections.Generic;

namespace VAS.Process
{
    public class CreateRecurringFromGLJournalBatch : SvrProcess
    {
        /// <summary>
        /// Prepare - e.g., get Parameters.
        /// </summary>
        /// 

        // Current record No
        private int recordId = 0;
        //	Date Of Next run		
        private DateTime? _DateNextRun = null;
        //Frequency Type			
        private string _FrequencyType = null;
        // Maximum Run 
        private int _RunsMax = 0;
        // Frequency 
        private int _Frequency = 0;

        // These variables are use for Dictionary type to get and set value in Recurring window
        private static readonly string RECURRING_NAME = "RECURRING_NAME";
        private static readonly string ORGID = "ORGID";
        private static readonly string CLIENTID = "CLIENTID";

        /// <summary>
        /// Get window Record ID
        /// Get listed parameters from UI
        /// 1. RunsMax : Maximum run as Integer Type
        /// 2. Frequency : Number of frequency as Integer type
        /// 3. DateNextRun : Next running date
        /// 4. FrequencyType : Frequency Type like Month, Date, weekly or Quarterly 
        /// </summary>
        protected override void Prepare()
        {
            recordId = GetRecord_ID();

            ProcessInfoParameter[] para = GetParameter();
            for (int i = 0; i < para.Length; i++)
            {
                String name = para[i].GetParameterName();
                if (para[i].GetParameter() == null)
                {
                    ;
                }
                else if (name.Equals("RunsMax"))
                {
                    _RunsMax = Util.GetValueOfInt(para[i].GetParameter());//.intValue();
                }
                else if (name.Equals("Frequency"))
                {
                    _Frequency = Util.GetValueOfInt(para[i].GetParameter());//.intValue();
                }
                else if (name.Equals("DateNextRun"))
                {
                    _DateNextRun = (DateTime?)para[i].GetParameter();
                }
                else if (name.Equals("FrequencyType"))
                {
                    _FrequencyType = para[i].GetParameter().ToString();
                }
                else
                {
                    log.Log(Level.SEVERE, "prepare - Unknown Parameter: " + name);
                }
            }
        }	//	prepare

        /// <summary>
        /// This method use to create Recurring Window record from GL Journal Batch window.
        /// Date           : 20230721
        /// Task ID        : 2260
        /// </summary>
        /// <returns>Name Number of Recurring Window</returns>
        protected override String DoIt()
        {
           string existingRecurring= GetExistingRecurringRecord(recordId);
            if(existingRecurring!=null && existingRecurring.Length>0)
                return Msg.GetMsg(GetCtx(), "Recurring_AlreadyCreated") + existingRecurring;

            // string recurringName = GetDynamicRecurringName(recordId);

            Dictionary<string,object> keyValuePairs = GetDynamicRecurringName(recordId);

            MRecurring mRecurring = new MRecurring(GetCtx(), 0, Get_TrxName());

            mRecurring.SetName(Util.GetValueOfString(keyValuePairs[RECURRING_NAME]));
            mRecurring.SetAD_Client_ID(Util.GetValueOfInt(keyValuePairs[CLIENTID]));
            mRecurring.SetAD_Org_ID(Util.GetValueOfInt(keyValuePairs[ORGID]));

            mRecurring.SetRunsMax(_RunsMax);
            mRecurring.SetRecurringType(MRecurring.RECURRINGTYPE_GLJournalBatch);
            mRecurring.SetDateNextRun(_DateNextRun);
            mRecurring.SetFrequency(_Frequency);
            mRecurring.SetFrequencyType(_FrequencyType);
            mRecurring.SetGL_JournalBatch_ID(recordId);

            if (!mRecurring.Save())
            {
                 string val = string.Empty;
                 ValueNamePair    pp = VLogger.RetrieveError();
                if (pp != null)
                {
                    val = pp.GetName();
                    if (String.IsNullOrEmpty(val))
                    {
                        val = pp.GetValue();
                    }
                }
                if (string.IsNullOrEmpty(val))
                {
                    val = Msg.GetMsg(GetCtx(), "Recurring_NotCreated");
                }
                log.Log(Level.SEVERE, val);
                return val;
            }
            return Msg.GetMsg(GetCtx(), "Recurring_Created") + mRecurring.GetName();

        }   //	doIt

        /// <summary>
        /// This Method check... Is already created recurring for particular transaction window? if it is then return Name of recurring record. 
        /// </summary>
        /// <param name="recordId">GL Journal Batch Record ID</param>
        /// <returns>Name of existing Recurring Window</returns>
        private string GetExistingRecurringRecord(int recordId)
        {
            string recurringName = Util.GetValueOfString(DB.ExecuteScalar("SELECT Name FROM C_Recurring WHERE GL_JournalBatch_ID=" + recordId));
            return recurringName;
        }

        /// <summary>
        /// Creating the combination of Name of Document Type and DocumentNo of GL Journal Batch window. 
        /// </summary>
        /// <param name="recordId">>GL Journal Batch Record ID</param>
        /// <returns>Combination of DocType Name and Document No, Org Id and Client ID in Dictionary Type</returns>
        private Dictionary<string, object> GetDynamicRecurringName(int recordId)
        {
            
            string sql = "SELECT dt.Name ||'/'|| glj.DocumentNo AS RecurringName, glj.Ad_Org_Id AS OrgId , glj.Ad_Client_Id AS ClientID " +
                        " FROM GL_JournalBatch glj " +
                        " INNER JOIN C_DocType dt on (glj.C_DocType_ID = dt.C_DocType_ID) WHERE glj.GL_JournalBatch_ID =" + recordId;
          
            Dictionary<string, object> keyValuePairs = new Dictionary<string, object>();
            DataSet ds = DB.ExecuteDataset(sql);

            if (ds != null && ds.Tables[0].Rows.Count > 0)
            {
                keyValuePairs.Add(RECURRING_NAME, Util.GetValueOfString(ds.Tables[0].Rows[0]["RecurringName"]));
                keyValuePairs.Add(ORGID, Util.GetValueOfInt(ds.Tables[0].Rows[0]["OrgId"]));
                keyValuePairs.Add(CLIENTID, Util.GetValueOfInt(ds.Tables[0].Rows[0]["ClientID"]));
                ds.Clear();
            }
            return keyValuePairs;
        }
    }	//	Recurring
}
