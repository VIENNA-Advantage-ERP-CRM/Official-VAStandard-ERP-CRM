/********************************************************
 * Class Name     : ProfitLossReactivation
 * Purpose        : This Process is used to Re-Open the PL Record, when found any entry missed after PL Close.
 *                  This Process will Delete Accounting Fact Detail record also, when found.
 * Class Used     : SvrProcess
 * Chronological    Development
 * Amit Bansal      31-Aug-2021
  ******************************************************/

using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using VAdvantage.ProcessEngine;
using VAdvantage.Utility;
using VAdvantage.DataBase;
using System.Data;
using VAdvantage.Model;
using VAdvantage.Logging;

namespace VAdvantage.Process
{
    public class ProfitLossReactivation : SvrProcess
    {
        private int no = 0;
        string docStatus = null;
        /// <summary>
        /// Implement PrepareIt
        /// </summary>
        protected override void Prepare()
        {
            ;
        }

        /// <summary>
        /// Implement DoIt
        /// </summary>
        /// <returns>Message</returns>
        protected override string DoIt()
        {
            string msg = CheckDocStatus(GetRecord_ID());
            if (!string.IsNullOrEmpty(msg))
            {
                Get_Trx().Rollback();
                return msg;
            }
            if (DeletePostingAffects(GetRecord_ID()) < 0)
            {
                Get_Trx().Rollback();
                return Msg.GetMsg(GetCtx(), "PLNotReActivated");
            }

            if (ReActivatePL(GetRecord_ID()) < 0)
            {
                Get_Trx().Rollback();
                return Msg.GetMsg(GetCtx(), "PLNotReActivated");
            }

            return Msg.GetMsg(GetCtx(), "PLReActivationSuccess");
        }

        /// <summary>
        /// Mark Processed as false for unfreeze
        /// </summary>
        /// <param name="C_ProfitLoss_ID">Profit Loss ID</param>
        /// <returns>count</returns>
        public int ReActivatePL(int C_ProfitLoss_ID)
        {
            no = DB.ExecuteQuery(@"UPDATE C_ProfitLoss SET Processed = 'N' , DocAction = 'CO' , DocStatus = 'IP' , Posted = 'N' 
                WHERE C_ProfitLoss_ID = " + C_ProfitLoss_ID, null, Get_Trx());
            log.Info("C_ProfitLoss - Unprocessed record Count : " + no);

            return no;
        }
        /// <summary>
        /// This function used to check the document status 
        /// if the document status is close,void,reverse then return message
        /// </summary>
        /// <param name="C_ProfitLoss_ID">Profit Loss ID</param>
        /// <returns>msg</returns>
        public string CheckDocStatus(int C_ProfitLoss_ID)
        {
            MProfitLoss pl = new MProfitLoss(GetCtx(), C_ProfitLoss_ID, Get_Trx());
            docStatus = pl.GetDocStatus();//Get document status
            if (docStatus == "CL")//Check the docStatus equal to close
            {
                return Msg.GetMsg(GetCtx(), "DocStatusClose");
            }
            else if (docStatus == "VO")//Check the docStatus equal to Void
            {
                return Msg.GetMsg(GetCtx(), "DocStatusVoid");
            }
            else if (docStatus == "RE")//Check the docStatus equal to Reverse
            {
                return Msg.GetMsg(GetCtx(), "DocStatusReverse");
            }
            return "";
        }

        /// <summary>
        /// Delete Accounting Fact Detail record for P&L Clossing 
        /// </summary>
        /// <param name="C_ProfitLoss_ID">Profit Loss ID</param>
        /// <returns>count</returns>
        public int DeletePostingAffects(int C_ProfitLoss_ID)
        {
            no = DB.ExecuteQuery("DELETE FROM Fact_Acct WHERE Record_ID = " + C_ProfitLoss_ID +
                " AND AD_Table_ID = " + X_C_ProfitLoss.Table_ID, null, Get_Trx());
            log.Info("Delete Accounting Fact Detail record for P&L Clossing : " + no);
            return no;
        }
    }
}
