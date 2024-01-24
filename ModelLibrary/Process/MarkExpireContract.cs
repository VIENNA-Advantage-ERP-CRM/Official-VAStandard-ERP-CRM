/********************************************************
    * Project Name   : Vienna Standard
    * Class Name     : MarkExpireContract
    * Purpose        : Mark expire checkbox true if contract 
    *                  is expired
    * Class Used     : ProcessEngine.SvrProcess
    * Chronological  : Development
    * Manjot         : 07/FEB/2023
******************************************************/
using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using VAdvantage.Classes;
using VAdvantage.Common;
using VAdvantage.Process;
using VAdvantage.Model;
using VAdvantage.DataBase;
using VAdvantage.SqlExec;
using VAdvantage.Utility;
using System.Windows.Forms;
using System.Data;
using System.Data.SqlClient;
using VAdvantage.ProcessEngine;
using VAdvantage.Logging;

namespace VAdvantage.Process
{
    public class MarkExpireContract : SvrProcess
    {
        static VLogger log = VLogger.GetVLogger("MarkExpireContract");
        protected override string DoIt()
        {
            StringBuilder sql = new StringBuilder();
            // VIS0060: Work done to set Status as Expired when Contract got expired 
            if (DatabaseType.IsOracle)
            {
                sql.Append(@"UPDATE VAS_ContractMaster SET IsExpiredContracts = 'Y', VAS_Status ='EXP' , Processed='Y' 
                       WHERE TO_DATE(EndDate, 'DD-MM-YYYY') < TO_DATE(SYSDATE, 'DD-MM-YYYY') AND VAS_Terminate='N' 
                       AND IsExpiredContracts = 'N'"); //VAI050-Terminate Contract Should not be expired
            }
            else
            {
                sql.Append(@"UPDATE VAS_ContractMaster SET IsExpiredContracts = 'Y', VAS_Status ='EXP' , Processed='Y' 
                       WHERE EndDate < CURRENT_DATE AND VAS_Terminate='N' AND IsExpiredContracts = 'N'"); //VAI050-Terminate Contract Should not be expired
            }
            if (Util.GetValueOfInt(DB.ExecuteQuery
                (sql.ToString(), null, Get_Trx())) < 0)
            {
                log.SaveInfo("", Msg.GetMsg(GetCtx(), "VAS_ExpiredNotUpdated"));
                Get_TrxName().Rollback();
                return Msg.GetMsg(GetCtx(), "VAS_ExpiredNotUpdated");
            }
            return Msg.GetMsg(GetCtx(), "VAS_ExpiredContUpdated");
        }

        protected override void Prepare()
        {

        }
    }
}
