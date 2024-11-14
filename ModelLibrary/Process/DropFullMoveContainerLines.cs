/********************************************************
    * Project Name   : VAdvantage
    * Class Name     : DropFullMoveContainerLines
    * Purpose        : Is used to drop all movement line when movement is of Full Movement container
                       In Future we will take Parameter as "Parent Container" -- based on this parameter -- we will drop line of selected parent and its child only
    * Class Used     : ProcessEngine.SvrProcess
    * Chronological    Development
    * Amit Bansal     23-Oct-2018
******************************************************/

using System;
using System.Collections.Generic;
using System.Data;
using System.Linq;
using System.Text;
using VAdvantage.DataBase;
using VAdvantage.Logging;
using VAdvantage.Model;
using VAdvantage.ProcessEngine;
using VAdvantage.Utility;

namespace VAdvantage.Process
{
    class DropFullMoveContainerLines : SvrProcess
    {
        private static VLogger _log = VLogger.GetVLogger(typeof(DropFullMoveContainerLines).FullName);

        protected override void Prepare()
        {
            ;
        }

        protected override string DoIt()
        {
            //Is used to drop all movement line when movement is of Full Movement container
            MMovementLine line;
            DataSet ds = DB.ExecuteDataset("SELECT * FROM M_MovementLine WHERE M_Movement_ID=" + GetRecord_ID() + " ORDER BY Line", null, Get_Trx());
            if (ds != null && ds.Tables.Count > 0 && ds.Tables[0].Rows.Count > 0)
            {
                foreach (DataRow dr in ds.Tables[0].Rows)
                {
                    line = new MMovementLine(GetCtx(), dr, Get_TrxName());
                    if (!line.Delete(true))
                    {
                        Get_Trx().Rollback();
                        ValueNamePair vp = VLogger.RetrieveError();
                        if (vp != null && !string.IsNullOrEmpty(vp.GetName()))
                        {
                            return vp.GetName();
                        }
                        return Msg.GetMsg(GetCtx(), "DeleteError");
                    }
                }
            }
            else
            {
                return Msg.GetMsg(GetCtx(), "VIS_NoRecordsFound");
            }
            //int no = DB.ExecuteQuery("DELETE FROM M_MovementLine WHERE M_Movement_ID = " + GetRecord_ID(), null, Get_Trx());
            //_log.Info(no + " records delete from movement line, movement id =  " + GetRecord_ID());
            //if (no >= 0)
            //if (no <= 0)
            //    return Msg.GetMsg(GetCtx(), "VIS_NoRecordsFound"); // No document line found.

            DB.ExecuteQuery("Update M_Movement SET DocStatus = 'DR' WHERE M_Movement_ID = " + GetRecord_ID(), null, Get_Trx());
            return Msg.GetMsg(GetCtx(), "VIS_RecordsDeleted"); // All records on line deleted successfully - 
        }
    }
}
