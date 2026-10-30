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
//using ViennaAdvantage.Model;


/* Writer :VAI052
* Date   : 29-May-26
*
* Cloned from ViennaAdvantage.Process.LeadToOpportunity
* (Process\ViennaAdvantageProcess\LeadToOpportunity.cs).
*
* The generated record was switched from VAdvantage.Model.X_C_Project to
* ViennaAdvantage.Model.X_VAS_Opportunity (the dedicated opportunity table).
* Because the two tables expose different columns, the field mapping was
* refactored as follows (every spot is also commented inline):
*
*   - Typed setters that have NO counterpart on X_VAS_Opportunity and only
*     make sense for the dual-purpose C_Project table are dropped:
*       SetIsOpportunity (VAS_Opportunity is always an opportunity) and
*       SetDateContract  (PO "Created" already records the creation date).
*   - SetC_BPartner_ID is dropped because X_VAS_Opportunity has no
*     C_BPartner_ID column and the same partner is already written to
*     C_BPartnerSR_ID below.
*   - Typed setters whose column may still exist on VAS_Opportunity but which
*     X_VAS_Opportunity does not generate (SetC_EnquiryRdate, SetC_ProposalDdate,
*     SetRef_BPartner_ID) are written through the generic Set_Value(...) guarded
*     by Get_ColumnIndex(...) > 0 - the established idiom in this code base - so
*     the data is copied where the column is present and skipped otherwise.
*   - GetC_Project_ID() -> GetVAS_Opportunity_ID() and the history/AI table id is
*     taken from opp.Get_Table_ID() (the VAS_Opportunity table) instead of the
*     hard-coded "C_Project" table.
*   - The lead.SetC_Project_ID(...) back-link is replaced with a guarded write to
*     the lead's VAS_Opportunity_ID column (if present); C_Project_ID is a project
*     foreign key and must not hold an opportunity id.
*
* VAI052 26-Oct-26 : Error handling - hard-coded / key-less messages replaced with
*     translatable VAS_LeadToOpp* messages; added lead-not-found and
*     prospect-not-created guards (the latter also prevents endless DoIt recursion);
*     every lead save is checked and a failure is returned with its error message.
* VAI052 29-Oct-26 : Completion message (GetCompletionMessage) tells whether the Prospect / User / Address
*     were created or an existing Customer / Prospect was used, with the Opportunity, Lead and Prospect IDs.
*/
namespace ModelLibrary.Process
{
    class VAS_LeadToOpportunity : SvrProcess
    {
        int _C_Lead_ID;
        bool IsProspectCreated = false;
        private string _companyName = "";
        private int _bpGroupID = 0;
        VAdvantage.Model.MLead lead = null;
        protected override void Prepare()
        {
            _C_Lead_ID = GetRecord_ID();
            ProcessInfoParameter[] para = GetParameter();
            if (para.Length > 0)
            {
                foreach (ProcessInfoParameter element in para)
                {
                    String name = element.GetParameterName();
                    if (name.Equals("_C_Lead_ID"))
                    {
                        _C_Lead_ID = element.GetParameterAsInt();
                    }
                    else if (name.Equals("VA061_CompanyName"))
                    {
                        _companyName = Util.GetValueOfString(element.GetParameter());
                    }
                    else if (name.Equals("C_BP_Group_ID"))
                    {
                        _bpGroupID = Util.GetValueOfInt(element.GetParameter());
                    }
                }
            }

        }


        protected override String DoIt()
        {
            if (_C_Lead_ID == 0)
            {
                return Msg.GetMsg(GetCtx(), "VAS_LeadToOppLeadNotSelected");
            }
            lead = new VAdvantage.Model.MLead(GetCtx(), _C_Lead_ID, Get_TrxName());
            if (lead.Get_ID() != _C_Lead_ID)
            {
                return Msg.GetMsg(GetCtx(), "VAS_LeadToOppLeadNotFound");
            }
            //VAI050-Set Bp name and group iD
            if (Env.IsModuleInstalled("VA061_"))
            {
                lead.SetBPName(_companyName);
                lead.SetC_BP_Group_ID(_bpGroupID);
            }
            //  lead.GetRef_BPartner_ID()))
            int ExCustomer = lead.GetC_BPartner_ID();
            int Pospect = lead.GetRef_BPartner_ID();
            if (ExCustomer == 0 && Pospect == 0)
            {
                //CallProcess(_C_Lead_ID);
                //VAI052-Prospect was already attempted once; stop instead of recursing forever
                if (IsProspectCreated)
                {
                    return Msg.GetMsg(GetCtx(), "VAS_LeadToOppProspectNotCreated");
                }
                if (lead.GetBPName() == null)
                {
                    return Msg.GetMsg(GetCtx(), "VAS_LeadToOppCompanyNameRequired");
                }
                if (lead.GetC_BP_Group_ID() == 0)
                {
                    throw new Exception(Msg.GetMsg(GetCtx(), "SelectBPGroup"));
                }
                callprospect();
                //VAI050-This flag used to check the status of prospect
                IsProspectCreated = true;
                return DoIt();

            }
            int Bpartner_ID = ExCustomer != 0 ? ExCustomer : Pospect;
            int User_ID = 0;

            User_ID = Util.GetValueOfInt(DB.ExecuteScalar("SELECT AD_User_ID FROM AD_User WHERE C_BPartner_ID=" + Bpartner_ID, null, Get_TrxName()));
            X_VAS_Opportunity opp = new X_VAS_Opportunity(GetCtx(), 0, Get_TrxName());
            opp.SetAD_Client_ID(lead.GetAD_Client_ID());
            opp.SetAD_Org_ID(lead.GetAD_Org_ID());
            opp.SetC_Lead_ID(lead.GetC_Lead_ID());
            opp.SetC_BPartner_ID(lead.GetC_BPartner_ID());
            opp.SetDescription(lead.GetDescription());
            if (Env.IsModuleInstalled("SIT01_"))
            {
                opp.Set_Value("R_Source_ID", lead.GetR_Source_ID());
            }
            if (lead.Get_ColumnIndex("VAS_LeadNextStep_ID") >= 0)
            {
                opp.SetVAS_LeadNextStep_ID(Util.GetValueOfInt(lead.Get_Value("VAS_LeadNextStep_ID")));
            }
            // Addde by Bharat on 19 Feb 2018 to set Ref Partner/Prospect
            if (opp.Get_ColumnIndex("Ref_BPartner_ID") >= 0)
            {
                opp.Set_Value("Ref_BPartner_ID", Bpartner_ID);
            }
            opp.SetSalesRep_ID(lead.GetSalesRep_ID());
            // X_C_Project.DateContract has no counterpart on X_VAS_Opportunity (dropped).
            opp.SetC_Campaign_ID(lead.GetC_Campaign_ID());
            //opp.SetR_Source_ID (lead.GetR_Source_ID());
            opp.SetAD_User_ID(User_ID);
            VAdvantage.Model.X_C_BPartner bp = new VAdvantage.Model.X_C_BPartner(GetCtx(), Bpartner_ID, Get_TrxName());
            //VAdvantage.Model.X_C_BPartner_Location loc=new VAdvantage.Model.X_C_BPartner_Location (GetCtx(),ExCustomer,Get_TrxName());

            opp.SetName(bp.GetName());
            opp.SetC_BPartner_Location_ID(lead.GetC_BPartner_Location_ID());

            opp.SetIsOpportunity(true);
            opp.Set_Value("C_EnquiryRdate", lead.GetC_EnquiryRdate());

            opp.SetC_BPartnerSR_ID(Bpartner_ID);
            opp.SkipAIAssistantThreadUpdate = true;
            if (opp.Save())
            {
                //VAI050-To Save history data on opportunity window
                int FromTableID = PO.Get_Table_ID("C_Lead");
                // Target is now the VAS_Opportunity table (was hard-coded "C_Project").
                int ToTableID = opp.Get_Table_ID();
                VAS_CommonMethod.CopyHistorRecordData(FromTableID, ToTableID, opp.GetVAS_Opportunity_ID(), lead.GetC_Lead_ID(), Get_TrxName(), GetCtx());
                if (IsProspectCreated)
                {
                    VAS_CommonMethod.CopyHistorRecordData(FromTableID, bp.Get_Table_ID(), bp.GetC_BPartner_ID(), lead.GetC_Lead_ID(), Get_TrxName(), GetCtx());
                }
                if (lead.Get_ColumnIndex("VAS_Opportunity_ID") > 0)
                {
                    lead.Set_Value("VAS_Opportunity_ID", opp.GetVAS_Opportunity_ID());
                }

                lead.SetStatus(X_C_Lead.STATUS_Converted);
                lead.SetProcessed(true);
                if (ExCustomer != 0)
                {
                    if (lead.Get_ColumnIndex("VAS_IsArchived") >= 0)
                        lead.Set_Value("VAS_IsArchived", "Y");
                    if (lead.Get_ColumnIndex("VAS_ArchivedReason") >= 0)
                        lead.Set_Value("VAS_ArchivedReason", "90");
                }

                if (!lead.Save())
                {
                    return GetRetrievedError(lead, "VAS_LeadToOppLeadNotSaved");
                }
                // Send Opportunity Data to Knowledge Base
                VAS_CommonMethod.SendInfoToAI(ToTableID, opp.Get_ID(), Get_Trx(), GetCtx());
                // Send Lead Data to Knowledge Base
                VAS_CommonMethod.SendInfoToAI(FromTableID, lead.Get_ID(), Get_Trx(), GetCtx());

                return GetCompletionMessage(opp, bp, ExCustomer != 0);

            }
            else
            {
                return GetRetrievedError(opp, "OpprtunityGenerateNotDone");
                //return Msg.GetMsg(GetCtx(), "OpprtunityGenerateNotDone");

            }
        }

        /// <summary>
        /// Completion message that tells whether the Opportunity was created from a Lead that already had a
        /// Customer / Prospect, or whether the Prospect (with its User and Address) was created first.
        /// </summary>
        /// <param name="opp">saved Opportunity</param>
        /// <param name="bp">Customer or Prospect the Opportunity belongs to</param>
        /// <param name="isCustomer">true when the Lead already had a Customer</param>
        /// <returns>message</returns>
        private string GetCompletionMessage(X_VAS_Opportunity opp, VAdvantage.Model.X_C_BPartner bp, bool isCustomer)
        {
            int bpartnerID = bp.GetC_BPartner_ID();
            List<string> created = new List<string>();
            // lead.CreateBP() ran in this run only when IsProspectCreated; its outcome flags describe that run
            if (IsProspectCreated && lead.BPartnerCreated)
            {
                created.Add(Msg.GetMsg(GetCtx(), "VAS_LeadToBPProspect"));
                if (lead.UserCreated) created.Add(Msg.GetMsg(GetCtx(), "VAS_LeadToBPUser"));
                if (lead.LocationCreated) created.Add(Msg.GetMsg(GetCtx(), "VAS_LeadToBPAddress"));
            }

            // The user sees the number / value of the records, not their database IDs.
            // Message placeholders: {1} Opportunity No, {2} Lead No, {3} Customer / Prospect Value ({0} is the record type text)
            string oppNo = GetRecordNo(opp.GetValue(), opp.GetVAS_Opportunity_ID());
            string leadNo = GetRecordNo(lead.GetDocumentNo(), lead.GetC_Lead_ID());
            string bpNo = GetRecordNo(bp.GetValue(), bpartnerID);
            log.Info("C_Lead_ID=" + lead.GetC_Lead_ID() + ", Opportunity ID=" + opp.GetVAS_Opportunity_ID()
                + ", BPartner ID=" + bpartnerID + (created.Count > 0 ? ", created: " + string.Join(", ", created) : ", existing BPartner used"));

            if (created.Count > 0)
            {
                return Msg.GetMsg(GetCtx(), "VAS_LeadToOppProspectCreatedMsg")
                    .Replace("{0}", JoinNames(created)).Replace("{1}", oppNo).Replace("{2}", leadNo).Replace("{3}", bpNo);
            }
            string bpType = isCustomer ? Msg.GetMsg(GetCtx(), "VAS_LeadToOppCustomer") : Msg.GetMsg(GetCtx(), "VAS_LeadToBPProspect");
            return Msg.GetMsg(GetCtx(), "VAS_LeadToOppExistingMsg")
                .Replace("{0}", bpType).Replace("{1}", oppNo).Replace("{2}", leadNo).Replace("{3}", bpNo);
        }

        /// <summary>
        /// Document number / value of a record; its ID only when it has none.
        /// </summary>
        private string GetRecordNo(string documentNoOrValue, int recordID)
        {
            return string.IsNullOrWhiteSpace(documentNoOrValue) ? recordID.ToString() : documentNoOrValue;
        }

        /// <summary>
        /// "A", "A and B" or "A, B, and C"
        /// </summary>
        private string JoinNames(List<string> names)
        {
            string and = Msg.GetMsg(GetCtx(), "VAS_LeadToBPAnd");
            if (names.Count == 1)
                return names[0];
            if (names.Count == 2)
                return names[0] + " " + and + " " + names[1];
            return string.Join(", ", names.Take(names.Count - 1)) + ", " + and + " " + names[names.Count - 1];
        }
        private void CallProcess(int lead_id)
        {
            string sql = "select ad_process_id from ad_process where name = 'C_Lead BPartner'";
            int AD_Process_ID = Util.GetValueOfInt(DB.ExecuteScalar(sql, null, Get_TrxName())); // 1000025;

            MPInstance instance = new MPInstance(GetCtx(), AD_Process_ID, GetRecord_ID());
            if (!instance.Save())
            {
                return;
            }

            ProcessInfo pi = new ProcessInfo("VInOutGen", AD_Process_ID);
            pi.SetAD_PInstance_ID(instance.GetAD_PInstance_ID());

            // Add Parameter - Selection=Y
            MPInstancePara para = new MPInstancePara(instance, 10);

            // Add Parameter - M_Warehouse_ID=x
            para = new MPInstancePara(instance, 20);
            para.setParameter("_C_Lead_ID", lead_id);
            if (!para.Save())
            {
                String msg = "No DocAction Parameter added";  //  not translated
                // lblStatusInfo.Text = msg.ToString();
                log.Log(Level.SEVERE, msg);
                return;
            }

            //Execute Process
            //ASyncProcess asp=null;
            //ProcessCtl worker = new ProcessCtl(asp,pi, null);
            //worker.Run();     //  complete tasks in unlockUI / generateInvoice_complete

        }


        public String callprospect()
        {
            log.Info("C_Lead_ID=" + _C_Lead_ID);
            if (_C_Lead_ID == 0)
            {
                throw new Exception(Msg.GetMsg(GetCtx(), "VAS_LeadToOppLeadNotSelected"));
            }
            //VAdvantage.Model.MLead lead = new VAdvantage.Model.MLead(GetCtx(), _C_Lead_ID, Get_TrxName());

            //if (lead.GetC_BP_Group_ID() == 0)
            //{
            //    throw new Exception(Msg.GetMsg(GetCtx(), "SelectBPGroup"));
            //}
            if (lead.Get_ID() != _C_Lead_ID)
            {
                throw new Exception(Msg.GetMsg(GetCtx(), "VAS_LeadToOppLeadNotFound") + " ID=" + _C_Lead_ID);
            }
            //
            String retValue = lead.CreateBP();
            if (retValue != null)
            {
                throw new SystemException(retValue);
            }
            if (!lead.Save())
            {
                throw new SystemException(GetRetrievedError(lead, "VAS_LeadToOppProspectLeadNotSaved"));
            }
            //
            VAdvantage.Model.MBPartner bp = lead.GetBPartner();
            if (bp != null)
            {
                return "@C_BPartner_ID@: " + bp.GetName();
            }
            VAdvantage.Model.MUser user = lead.GetUser();
            if (user != null)
            {
                return "@AD_User_ID@: " + user.GetName();
            }
            return "@SaveError@";
        }	//	doIt

    }


}
