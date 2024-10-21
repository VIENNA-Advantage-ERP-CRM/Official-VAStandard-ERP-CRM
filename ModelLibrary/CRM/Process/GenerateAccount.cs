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
            int value = 0;
            //VAdvantage.Model.MBPartner bp = new VAdvantage.Model.MBPartner(GetCtx(), C_Bpartner_ID, Get_TrxName());
            //BPartner.SetC_Greeting_ID(
            string sqlbp = "UPDATE C_BPartner set VAS_IsConverted='Y', IsCustomer='Y', IsProspect='N' WHERE C_BPartner_ID=" + C_Bpartner_ID + "";
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
