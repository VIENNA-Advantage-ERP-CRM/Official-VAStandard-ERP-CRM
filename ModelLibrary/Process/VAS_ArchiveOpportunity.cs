using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using VAdvantage.Utility;
using System.Data;
using VAdvantage.Logging;
using VAdvantage.DataBase;
using VAdvantage.ProcessEngine;
using VAdvantage.Model;
using ModelLibrary.Classes;
using ModelLibrary.Model;

/* Writer :VAI052
* Date   : 22-Jun-26
*
* Archives a VAS_Opportunity record. The process is launched on an opportunity
* record and accepts a single parameter, VAS_ArchivedReason. On execution it
* writes the supplied reason to the opportunity's VAS_ArchivedReason column and
* flags VAS_IsArchived = 'Y'.
*
* Structure follows the sibling VAS_LeadToOpportunity / VAS_ProspectToOpportunity
* processes (parameter read in Prepare(), work performed in DoIt()).
*/
namespace ModelLibrary.Process
{
    public class VAS_ArchiveOpportunity : SvrProcess
    {
        int _VAS_Opportunity_ID;
        private string _archivedReason = "";

        protected override void Prepare()
        {
            _VAS_Opportunity_ID = GetRecord_ID();
            ProcessInfoParameter[] para = GetParameter();
            if (para.Length > 0)
            {
                foreach (ProcessInfoParameter element in para)
                {
                    String name = element.GetParameterName();
                    if (name.Equals("VAS_ArchivedReason"))
                    {
                        _archivedReason = Util.GetValueOfString(element.GetParameter());
                    }
                }
            }
        }

        /// <summary>
        /// Set the archived reason and mark the opportunity as archived.
        /// </summary>
        /// <returns>message</returns>
        protected override String DoIt()
        {
            if (_VAS_Opportunity_ID == 0)
            {
                throw new Exception("@VAS_Opportunity_ID@ ID=0");
            }

            X_VAS_Opportunity opp = new X_VAS_Opportunity(GetCtx(), _VAS_Opportunity_ID, Get_TrxName());
            if (opp.Get_ID() != _VAS_Opportunity_ID)
            {
                throw new Exception("@NotFound@: @VAS_Opportunity_ID@ ID=" + _VAS_Opportunity_ID);
            }

            if (opp.Get_ColumnIndex("VAS_ArchivedReason") >= 0)
            {
                opp.Set_Value("VAS_ArchivedReason", _archivedReason);
            }
            if (opp.Get_ColumnIndex("VAS_IsArchived") >= 0)
            {
                opp.Set_Value("VAS_IsArchived", "Y");
            }

            opp.SkipAIAssistantThreadUpdate = true;
            if (opp.Save())
            {
                return Msg.GetMsg(GetCtx(), "Saved");
            }
            else
            {
                return GetRetrievedError(opp, "SaveError");
            }
        }
    }
}
