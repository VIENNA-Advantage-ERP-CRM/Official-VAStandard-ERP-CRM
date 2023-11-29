/********************************************************
    * Project Name   : Vienna Standard
    * Class Name     : ReActiveContract
    * Purpose        : To Unfreeze all tabs
    * Class Used     : ProcessEngine.SvrProcess
    * Chronological  : Development
    * Sukhvinder     : 24/NOV/2023
******************************************************/

using CoreLibrary.DataBase;
using System;
using System.Collections.Generic;
using System.Data;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using VAdvantage.ProcessEngine;
using VAdvantage.Utility;


namespace VAdvantage.Process
{
    public class ReActiveContract : SvrProcess
    {
        protected override void Prepare()
        {

        }
        /// <summary>
        /// To unfreeze all Tabs excluding contract Terms tab
        /// </summary>
        /// <returns>return message</returns>
        protected override string DoIt()
        {
            StringBuilder query = new StringBuilder();
            if (GetRecord_ID() > 0)
            {
                int recordId = GetRecord_ID();
                int count = 0;
                query.Append("UPDATE VAS_ContractMaster SET Processed='N',VAS_IsApproved='N' WHERE VAS_ContractMaster_ID =" + recordId);
                count = DB.ExecuteQuery(query.ToString(), null, Get_Trx());
                query.Clear();
                query.Append("UPDATE VAS_ContractLine SET Processed='N' WHERE VAS_ContractMaster_ID =" + recordId);
                count = DB.ExecuteQuery(query.ToString(), null, Get_Trx());
                query.Clear();
                query.Append("UPDATE VAS_ContractOwner SET Processed='N' WHERE VAS_ContractMaster_ID =" + recordId);
                count = DB.ExecuteQuery(query.ToString(), null, Get_Trx());
            }
            return Msg.GetMsg(GetCtx(), "VAS_ReactiveContract");
        }
    }
}
