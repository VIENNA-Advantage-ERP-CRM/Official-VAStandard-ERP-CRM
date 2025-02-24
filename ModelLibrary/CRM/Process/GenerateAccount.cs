using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using VAdvantage.Classes;
using ViennaAdvantage.Process;
using System.Windows.Forms;
//using ViennaAdvantage.Model;
using VAdvantage.DataBase;
using VAdvantage.SqlExec;
using VAdvantage.Utility;
using System.Data;
using VAdvantage.Logging;
using VAdvantage.ProcessEngine;


namespace VAdvantage.Process
{
    public class GenerateAccount:SvrProcess
    {
        private int C_Bpartner_ID = 0;

        protected override void Prepare()
        {
            C_Bpartner_ID = GetRecord_ID();
        } //prepare

        protected override String DoIt()
        {
            int value = 0, bpAgent_ID = 0;
            // VAdvantage.Model.MBPartner bp = new VAdvantage.Model.MBPartner(GetCtx(), C_Bpartner_ID, Get_TrxName());
            //BPartner.SetC_Greeting_ID(
            if (Env.IsModuleInstalled("VA114_"))
            {
                bpAgent_ID = Util.GetValueOfInt(DB.ExecuteScalar(@"SELECT C_BPartner_ID FROM AD_User WHERE AD_User_ID = (SELECT SalesRep_ID FROM 
                    C_BPartner WHERE C_BPartner_ID=" + C_Bpartner_ID + ")"));
            }

            string sqlbp = "UPDATE C_BPartner SET VAS_IsConverted='Y', IsCustomer='Y', IsProspect='N'" +
                    // VIS0060: Changes done to set BPartner Agent on Customer in Case Partner Management module is installed.
                    (Env.IsModuleInstalled("VA114_") && bpAgent_ID > 0 ? ", VA114_BPAgent_ID = " + bpAgent_ID : "") +
                    " WHERE C_BPartner_ID=" + C_Bpartner_ID;
            value = DB.ExecuteQuery(sqlbp, null, Get_TrxName());
            if (value == -1)
            {

            }
            //bp.SetIsCustomer(true);
            //bp.SetIsProspect(false);
          
            return Msg.GetMsg(GetCtx(), "AccountGenerated");
        }
    }
}
