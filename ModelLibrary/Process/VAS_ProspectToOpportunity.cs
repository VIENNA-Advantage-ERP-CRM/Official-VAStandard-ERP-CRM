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
using ViennaAdvantage.Model;

/* Writer :VAI052
* Date   : 01-Jun-26
*
* Cloned from ViennaAdvantage.Process.ProspectToOpportunity
* (Process\ViennaAdvantageProcess\ProspectToOpportunity.cs).
*
* The generated record was switched from VAdvantage.Model.X_C_Project to
* ModelLibrary.Model.X_VAS_Opportunity (the dedicated opportunity table).
* Because the two tables expose different columns, the field mapping was
* adapted as follows (every spot is also commented inline):
*
*   - Typed setters that have NO counterpart on X_VAS_Opportunity and only
*     make sense for the dual-purpose C_Project table are dropped:
*       SetDateContract (PO "Created" already records the creation date).
*   - GetC_Project_ID() -> GetVAS_Opportunity_ID() and the history/AI table id is
*     taken from opp.Get_Table_ID() (the VAS_Opportunity table).
*   - The lead.SetC_Project_ID(...) back-link is replaced with a guarded write to
*     the lead's VAS_Opportunity_ID column (if present); C_Project_ID is a project
*     foreign key and must not hold an opportunity id.
*
* VAI052 26-Oct-26 : Error handling - added prospect-not-found guard, the failed
*     opportunity save now reports the underlying save error (GetRetrievedError), and
*     every lead/prospect save is checked and a failure is returned with its VAS_ProspectToOpp* message.
* VAI052 29-Oct-26 : One Opportunity from the Prospect's first Location (lowest ID) and first User (lowest ID);
*     the newest active Lead of exactly that User and Location (Created DESC, C_Lead_ID DESC) is linked to it.
*     Completion message tells whether a Lead was linked; no Location is reported.
* VAI052 30-Oct-26 : Prospect without a User: the Opportunity is still created (no contact, no Lead link)
*     and the message says the contact is not linked.
*/
namespace ModelLibrary.Process
{
    public class VAS_ProspectToOpportunity : SvrProcess
    {
        int _C_Lead_ID;
        protected override void Prepare()
        {

        }

        /// <summary>
        /// Doit
        /// </summary>
        /// <returns>message</returns>
        protected override string DoIt()
        {
            X_C_BPartner partner = new X_C_BPartner(GetCtx(), GetRecord_ID(), null);
            if (GetRecord_ID() == 0 || partner.Get_ID() != GetRecord_ID())
            {
                return Msg.GetMsg(GetCtx(), "VAS_ProspectToOppProspectNotFound");
            }

            // VAI052: one Location and one User of the Prospect drive the Opportunity and the Lead search.
            // First = oldest active record (lowest ID); the Prospect tables define no other order.
            int locationID = Util.GetValueOfInt(DB.ExecuteScalar("SELECT C_BPartner_Location_ID FROM C_BPartner_Location WHERE C_BPartner_ID="
                + partner.GetC_BPartner_ID() + " AND IsActive = 'Y' ORDER BY C_BPartner_Location_ID", null, Get_TrxName()));
            if (locationID == 0)
            {
                return Msg.GetMsg(GetCtx(), "VAS_ProspectToOppNoLocation");
            }
            int userID = Util.GetValueOfInt(DB.ExecuteScalar("SELECT AD_User_ID FROM AD_User WHERE C_BPartner_ID="
                + partner.GetC_BPartner_ID() + " AND IsActive = 'Y' ORDER BY AD_User_ID", null, Get_TrxName()));
            // VAI052: a Prospect without a User does not stop the process - the Opportunity is created
            // without a contact and without a Lead (the Lead is searched by User and Location), and the
            // message says so.

            // Latest Lead of exactly this User and Location (newest Created; Lead ID breaks a tie)
            X_C_Lead lead = null;
            if (userID > 0)
            {
                _C_Lead_ID = Util.GetValueOfInt(DB.ExecuteScalar("SELECT C_Lead_ID FROM C_Lead WHERE AD_User_ID=" + userID
                    + " AND C_BPartner_Location_ID=" + locationID + " AND IsActive = 'Y' ORDER BY Created DESC, C_Lead_ID DESC", null, Get_TrxName()));
                if (_C_Lead_ID > 0)
                {
                    lead = new X_C_Lead(GetCtx(), _C_Lead_ID, Get_TrxName());
                }
            }

            X_VAS_Opportunity opp = new X_VAS_Opportunity(GetCtx(), 0, Get_TrxName());
            opp.SetAD_Client_ID(partner.GetAD_Client_ID());
            opp.SetAD_Org_ID(partner.GetAD_Org_ID());
            opp.SetName(partner.GetName());
            if (userID > 0)
            {
                opp.SetAD_User_ID(userID);
            }
            opp.SetC_BPartner_Location_ID(locationID);
            opp.SetRef_BPartner_ID(partner.GetC_BPartner_ID());
            opp.SetDescription(partner.GetDescription());
            opp.SetSalesRep_ID(partner.GetSalesRep_ID());
            opp.SetC_Campaign_ID(partner.GetC_Campaign_ID());
            //VIA051:- Implement IsCustomer check to verify opportunity is generated by customer or Prospect
            if (partner.IsCustomer())
            {
                opp.SetC_BPartner_ID(partner.GetC_BPartner_ID());
            }
            else
            {
                opp.SetC_BPartnerSR_ID(partner.GetC_BPartner_ID());
            }
            if (Env.IsModuleInstalled("SIT01_") && partner.Get_ColumnIndex("R_Source_ID")>=0)
            {
                opp.Set_Value("R_Source_ID", Util.GetValueOfInt(partner.Get_Value("R_Source_ID")));
            }
            if (lead != null)
            {
                opp.SetC_Lead_ID(lead.GetC_Lead_ID());
                opp.SetC_EnquiryRdate(lead.GetC_EnquiryRdate());
            }
            opp.SetIsOpportunity(true);
            opp.SkipAIAssistantThreadUpdate = true;
            if (!opp.Save())
            {
                return GetRetrievedError(opp, "OpprtunityGenerateNotDone");
            }

            //Save history chat data form prospect window to opportunity window
            int FromTableID = partner.Get_Table_ID();
            int ToTableID = opp.Get_Table_ID();
            VAS_CommonMethod.CopyHistorRecordData(FromTableID, ToTableID, opp.GetVAS_Opportunity_ID(), GetRecord_ID(), Get_TrxName(), GetCtx());

            if (lead != null)
            {
                // C_Project_ID is a project FK; link the lead to the new opportunity
                // through its VAS_Opportunity_ID column instead (when available).
                if (lead.Get_ColumnIndex("VAS_Opportunity_ID") >= 0)
                {
                    lead.Set_Value("VAS_Opportunity_ID", opp.GetVAS_Opportunity_ID());
                }
                lead.SetProcessed(true);
                if (!lead.Save())
                {
                    return GetRetrievedError(lead, "VAS_ProspectToOppLeadNotSaved");
                }
                // Send Lead Data to Knowledge Base
                VAS_CommonMethod.SendInfoToAI(lead.Get_Table_ID(), lead.Get_ID(), Get_Trx(), GetCtx());
            }
            partner.SetCreateProject("Y");
            // Send opp Data to Knowledge Base
            VAS_CommonMethod.SendInfoToAI(opp.Get_Table_ID(), opp.Get_ID(), Get_Trx(), GetCtx());
            if (!partner.Save())
            {
                return GetRetrievedError(partner, "VAS_ProspectToOppProspectNotSaved");
            }

            // The user sees the number / value of the records, not their database IDs.
            // Message placeholders: {0} Opportunity No, {1} Prospect Value, {2} Lead No
            string oppNo = GetRecordNo(opp.GetValue(), opp.GetVAS_Opportunity_ID());
            string prospectNo = GetRecordNo(partner.GetValue(), partner.GetC_BPartner_ID());
            if (userID == 0)
            {
                return Msg.GetMsg(GetCtx(), "VAS_ProspectToOppDoneNoUser")
                    .Replace("{0}", oppNo).Replace("{1}", prospectNo);
            }
            if (lead != null)
            {
                return Msg.GetMsg(GetCtx(), "VAS_ProspectToOppDoneLinked")
                    .Replace("{0}", oppNo).Replace("{1}", prospectNo)
                    .Replace("{2}", GetRecordNo(lead.GetDocumentNo(), lead.GetC_Lead_ID()));
            }
            return Msg.GetMsg(GetCtx(), "VAS_ProspectToOppDoneNoLead")
                .Replace("{0}", oppNo).Replace("{1}", prospectNo);
        }

        /// <summary>
        /// Document number / value of a record; its ID only when it has none.
        /// </summary>
        private string GetRecordNo(string documentNoOrValue, int recordID)
        {
            return string.IsNullOrWhiteSpace(documentNoOrValue) ? recordID.ToString() : documentNoOrValue;
        }

    }
}
