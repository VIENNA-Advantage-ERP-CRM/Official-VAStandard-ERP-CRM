/********************************************************
 * Project Name   : VAdvantage
 * Class Name     : CreateRecurringFromGLJournal
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

namespace VAS.Process
{
    public class CreateRecurringFromGLJournal : SvrProcess
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
        /// This method use to create Recurring Window record from GL Journal window.
        /// Date           : 20230721
        /// Task ID        : 2260
        /// </summary>
        /// <returns>Name Number of Recurring Window</returns>
        protected override String DoIt()
        {
           string existingRecurring= GetExistingRecurringRecord(recordId);
            if(existingRecurring!=null && existingRecurring.Length>0)
                return Msg.GetMsg(GetCtx(), "Recurring_AlreadyCreated") + existingRecurring;

            string recurringName = GetDynamicRecurringName(recordId);

            MRecurring mRecurring = new MRecurring(GetCtx(), 0, Get_TrxName());
            mRecurring.SetName(recurringName);
            mRecurring.SetRunsMax(_RunsMax);
            mRecurring.SetRecurringType(MRecurring.RECURRINGTYPE_GLJournal);
            mRecurring.SetDateNextRun(_DateNextRun);
            mRecurring.SetFrequency(_Frequency);
            mRecurring.SetFrequencyType(_FrequencyType);
            mRecurring.SetGL_Journal_ID(recordId);

            if (!mRecurring.Save())
            {
                log.Log(Level.SEVERE, Msg.GetMsg(GetCtx(), "Recurring_NotSaved"));
                return Msg.GetMsg(GetCtx(), "Recurring_NotCreated");
            }
            return Msg.GetMsg(GetCtx(), "Recurring_Created") + mRecurring.GetName();

        }   //	doIt

        /// <summary>
        /// This Method check... Is already created recurring for particular transaction window? if it is then return Name of recurring record. 
        /// </summary>
        /// <param name="recordId">GL Journal Record ID</param>
        /// <returns>Name of existing Recurring Window</returns>
        private string GetExistingRecurringRecord(int recordId)
        {
            string recurringName = Util.GetValueOfString(DB.ExecuteScalar("SELECT Name FROM C_Recurring WHERE GL_Journal_ID=" + recordId));

            /*else if (_RecurringType.Equals(MRecurring.RECURRINGTYPE_GLJournal))
            {
                recurringName = Util.GetValueOfString(DB.ExecuteScalar("SELECT Name FROM C_Recurring WHERE GL_Journal_ID=" + recordId));
            }
            else if (_RecurringType.Equals(MRecurring.RECURRINGTYPE_GLJournalBatch))
            {
                recurringName = Util.GetValueOfString(DB.ExecuteScalar("SELECT Name FROM C_Recurring WHERE GL_JournalBatch_ID=" + recordId));
            }*/
            return recurringName;
        }


        /// <summary>
        /// Creating the combination of Name of Document Type and DocumentNo of GL Journal Batch window. 
        /// </summary>
        /// <param name="recordId">>GL Journal Record ID</param>
        /// <returns>Combination of DocType Name and Document No</returns>
        private string GetDynamicRecurringName(int recordId)
        {
            
            string sql = "SELECT dt.Name ||'/'|| glj.DocumentNo " +
                        " FROM GL_Journal glj " +
                        " INNER JOIN C_DocType dt on (glj.C_DocType_ID = dt.C_DocType_ID) WHERE glj.GL_Journal_ID =" + recordId;
            string recurringName = Util.GetValueOfString(DB.ExecuteScalar(sql));

            
            /*else
            if (recurringType.Equals(MRecurring.RECURRINGTYPE_GLJournal))
            {
                MJournal mJournal = new MJournal(GetCtx(), recordId, Get_TrxName());
                MDocType mDocType = new MDocType(GetCtx(), mJournal.GetC_DocType_ID(), Get_TrxName());
                recurringName = mDocType.GetName() + "/" + mJournal.GetDocumentNo();
            }
            else
            if (recurringType.Equals(MRecurring.RECURRINGTYPE_GLJournalBatch))
            {
                MJournalBatch mJournalBatch = new MJournalBatch(GetCtx(), recordId, Get_TrxName());
                MDocType mDocType = new MDocType(GetCtx(), mJournalBatch.GetC_DocType_ID(), Get_TrxName());
                recurringName = mDocType.GetName() + "/" + mJournalBatch.GetDocumentNo();
            }*/
            return recurringName;
        }
    }	//	Recurring
}
