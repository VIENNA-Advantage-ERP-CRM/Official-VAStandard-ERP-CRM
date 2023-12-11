/********************************************************
    * Project Name   : Vienna Standard
    * Class Name     : PublishContract
    * Purpose        : To Freeze all tabs
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
    public class PublishContract : SvrProcess
    {
        protected override void Prepare()
        {

        }
        /// <summary>
        /// To freeze all tabs excluding Contract Terms tab
        /// </summary>
        /// <returns> return message</returns>
        protected override string DoIt()
        {
            StringBuilder query = new StringBuilder();
            if (GetRecord_ID() > 0)
            {
                int recordId = GetRecord_ID();
                query.Append(@"SELECT (SELECT COUNT(VAS_ContractLine_ID) FROM VAS_ContractLine WHERE VAS_ContractMaster_ID=" + recordId +
                                    ") AS VAS_ContractLine_ID, (SELECT COUNT(VAS_ContractOwner_ID) FROM VAS_ContractOwner WHERE " +
                                    "VAS_ContractMaster_ID =" + recordId + ") AS VAS_ContractOwner_ID  FROM VAS_ContractMaster " +
                                    "WHERE VAS_ContractMaster_ID =" + recordId);
                DataSet ds = DB.ExecuteDataset(query.ToString(), null, Get_Trx());
                if (ds != null && ds.Tables.Count > 0 && ds.Tables[0].Rows.Count > 0)
                {
                    int count = 0;
                    if (Util.GetValueOfInt(ds.Tables[0].Rows[0]["VAS_ContractLine_ID"]) > 0 && Util.GetValueOfInt(ds.Tables[0].Rows[0]["VAS_ContractOwner_ID"]) > 0)
                    {
                        query.Clear();
                        query.Append("UPDATE VAS_ContractLine SET Processed='Y' WHERE VAS_ContractMaster_ID =" + recordId);
                        count = DB.ExecuteQuery(query.ToString(), null, Get_Trx());
                        query.Clear();
                        query.Append("UPDATE VAS_ContractOwner SET Processed='Y' WHERE VAS_ContractMaster_ID =" + recordId);
                        count = DB.ExecuteQuery(query.ToString(), null, Get_Trx());
                        query.Clear();
                        query.Append("UPDATE VAS_ContractMaster SET Processed='Y' WHERE VAS_ContractMaster_ID =" + recordId);
                        count = DB.ExecuteQuery(query.ToString(), null, Get_Trx());
                        return Msg.GetMsg(GetCtx(), "VAS_PublishContract"); //Process executed
                    }                   
                    else if (Util.GetValueOfInt(ds.Tables[0].Rows[0]["VAS_ContractOwner_ID"]) == 0)
                    {
                        return Msg.GetMsg(GetCtx(), "VAS_ContractOwnerRecord"); // record not found on ContractOwner
                    }
                    else if (Util.GetValueOfInt(ds.Tables[0].Rows[0]["VAS_ContractLine_ID"]) == 0)
                    {
                        return Msg.GetMsg(GetCtx(), "VAS_ContractLineRecord"); //record not found on  Contract Line
                    }
                }
                else
                {
                    return Msg.GetMsg(GetCtx(), "VAS_PublishContractRecord"); //  record not found on both tabs
                }
            }
            return Msg.GetMsg(GetCtx(), "VAS_PublishContract"); //Process executed

        }
    }
}
