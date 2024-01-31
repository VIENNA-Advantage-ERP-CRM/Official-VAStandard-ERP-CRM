/********************************************************
 * Project Name   : VAdvantage
 * Class Name     : RfQResponseCComplete
 * Purpose        : Check if Response is Complete
 * Class Used     : ProcessEngine.SvrProcess
 * Chronological    Development
 * Raghunandan     11-Aug.-2009
  ******************************************************/
using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using VAdvantage.Classes;
using VAdvantage.Common;
using VAdvantage.Process;
using System.Windows.Forms;
using VAdvantage.Model;
using VAdvantage.DataBase;
using VAdvantage.SqlExec;
using VAdvantage.Utility;
using System.Data;
using System.Data.SqlClient;
using VAdvantage.Logging;

using VAdvantage.ProcessEngine;
namespace VAdvantage.Process
{
    public class RfQResponseCComplete : ProcessEngine.SvrProcess
    {
        //RfQ Response				
        private int _C_RfQResponse_ID = 0;

        /// <summary>
        /// Prepare - e.g., get Parameters.
        /// </summary>
        protected override void Prepare()
        {
            ProcessInfoParameter[] para = GetParameter();
            for (int i = 0; i < para.Length; i++)
            {
                String name = para[i].GetParameterName();
                if (para[i].GetParameter() == null)
                {
                    ;
                }
                else
                {
                    log.Log(Level.SEVERE, "Unknown Parameter: " + name);
                }
            }
            _C_RfQResponse_ID = GetRecord_ID();
        }

        /// <summary>
        /// Perform Process.
        /// </summary>
        /// <returns>message</returns>
        protected override String DoIt()
        {
            MRfQResponse response = new MRfQResponse(GetCtx(), _C_RfQResponse_ID, Get_TrxName());
            log.Info("doIt - " + response);
            //
            String error = response.CheckComplete();
            if (error != null && error.Length > 0)
            {
                throw new Exception(error);
            }
            //
            response.Save();
            //Deekshant changes in return Completed Sucessfully
            //VAI050--Changes to freeze Vendor tabs.
            return SetProccessedCheck();
        }

        //VAI050--On Vendor Response tab When user click on Check Complete button than 
        // RfQ Response Vendor tab,Response Line tab and Response Quantity tab should be freezed.
        public string SetProccessedCheck()
        {
            StringBuilder query = new StringBuilder();
            query.Append("UPDATE C_RfQResponse SET Processed='Y' WHERE  C_RfQResponse_ID="+ GetRecord_ID());
            if (DB.ExecuteQuery(query.ToString(), null, Get_Trx()) < 0)
            {
                Get_Trx().Rollback();
                return Msg.GetMsg(GetCtx(), "VAS_ResponseVendorNot");
            }
            query.Clear();
            query.Append("UPDATE C_RfQResponseLine SET Processed='Y' WHERE  C_RfQResponse_ID="+ GetRecord_ID());
            if (DB.ExecuteQuery(query.ToString(), null, Get_Trx()) < 0)
            {
                Get_Trx().Rollback();
                return Msg.GetMsg(GetCtx(), "VAS_ResponseLineNot");
            }
            query.Clear();
            query.Append("SELECT C_RfQResponseLine_ID FROM C_RfQResponseLine WHERE  C_RfQResponse_ID="+ GetRecord_ID());
            DataSet ds = DB.ExecuteDataset(query.ToString(), null, Get_Trx());
            if(ds!=null && ds.Tables.Count>0 && ds.Tables[0].Rows.Count > 0)
            {
               for(int i = 0; i < ds.Tables[0].Rows.Count; i++)
                {
                    query.Clear();
                    query.Append("UPDATE C_RfQResponseLineQty SET Processed='Y' WHERE  C_RfQResponseLine_ID=" + ds.Tables[0].Rows[i]["C_RfQResponseLine_ID"]);
                    if (DB.ExecuteQuery(query.ToString(), null, Get_Trx()) < 0)
                    {
                        Get_Trx().Rollback();
                        return Msg.GetMsg(GetCtx(), "VAS_ResponseQuantityNot");
                    }
                }
            }
            return Msg.GetMsg(GetCtx(), "Success", "");
        }
    }
}
