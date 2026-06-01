namespace ModelLibrary.Model{
/** Generated Model - DO NOT CHANGE */
using System;using System.Text;using VAdvantage.DataBase;using VAdvantage.Common;using VAdvantage.Classes;using VAdvantage.Process;using VAdvantage.Model;using VAdvantage.Utility;using System.Data;/** Generated Model for VAS_Opportunity
 *  @author Raghu (Updated) 
 *  @version Vienna Framework 1.1.1 - $Id$ */
public class X_VAS_Opportunity : PO{public X_VAS_Opportunity (Context ctx, int VAS_Opportunity_ID, Trx trxName) : base (ctx, VAS_Opportunity_ID, trxName){/** if (VAS_Opportunity_ID == 0){SetVAS_Opportunity_ID (0);} */
}public X_VAS_Opportunity (Ctx ctx, int VAS_Opportunity_ID, Trx trxName) : base (ctx, VAS_Opportunity_ID, trxName){/** if (VAS_Opportunity_ID == 0){SetVAS_Opportunity_ID (0);} */
}/** Load Constructor 
@param ctx context
@param rs result set 
@param trxName transaction
*/
public X_VAS_Opportunity (Context ctx, DataRow rs, Trx trxName) : base(ctx, rs, trxName){}/** Load Constructor 
@param ctx context
@param rs result set 
@param trxName transaction
*/
public X_VAS_Opportunity (Ctx ctx, DataRow rs, Trx trxName) : base(ctx, rs, trxName){}/** Load Constructor 
@param ctx context
@param rs result set 
@param trxName transaction
*/
public X_VAS_Opportunity (Ctx ctx, IDataReader dr, Trx trxName) : base(ctx, dr, trxName){}/** Static Constructor 
 Set Table ID By Table Name
 added by ->Harwinder */
static X_VAS_Opportunity(){ Table_ID = Get_Table_ID(Table_Name); model = new KeyNamePair(Table_ID,Table_Name);}/** Serial Version No */
static long serialVersionUID = 28062182994489L;/** Last Updated Timestamp 5/29/2026 5:57:57 PM */
public static long updatedMS = 1780057677700L;/** AD_Table_ID=1000722 */
public static int Table_ID; // =1000722;
/** TableName=VAS_Opportunity */
public static String Table_Name="VAS_Opportunity";
protected static KeyNamePair model;protected Decimal accessLevel = new Decimal(3);/** AccessLevel
@return 3 - Client - Org 
*/
protected override int Get_AccessLevel(){return Convert.ToInt32(accessLevel.ToString());}/** Load Meta Data
@param ctx context
@return PO Info
*/
protected override POInfo InitPO (Context ctx){POInfo poi = POInfo.GetPOInfo (ctx, Table_ID);return poi;}/** Load Meta Data
@param ctx context
@return PO Info
*/
protected override POInfo InitPO (Ctx ctx){POInfo poi = POInfo.GetPOInfo (ctx, Table_ID);return poi;}/** Info
@return info
*/
public override String ToString(){StringBuilder sb = new StringBuilder ("X_VAS_Opportunity[").Append(Get_ID()).Append("]");return sb.ToString();}/** Set User/Contact.
@param AD_User_ID User within the system - Internal or Customer/Prospect Contact. */
public void SetAD_User_ID (int AD_User_ID){if (AD_User_ID <= 0) Set_Value ("AD_User_ID", null);else
Set_Value ("AD_User_ID", AD_User_ID);}/** Get User/Contact.
@return User within the system - Internal or Customer/Prospect Contact. */
public int GetAD_User_ID() {Object ii = Get_Value("AD_User_ID");if (ii == null) return 0;return Convert.ToInt32(ii);}
/** C_BPartnerSR_ID AD_Reference_ID=138 */
public static int C_BPARTNERSR_ID_AD_Reference_ID=138;/** Set BPartner (Agent).
@param C_BPartnerSR_ID Customer Agent */
public void SetC_BPartnerSR_ID (int C_BPartnerSR_ID){if (C_BPartnerSR_ID <= 0) Set_Value ("C_BPartnerSR_ID", null);else
Set_Value ("C_BPartnerSR_ID", C_BPartnerSR_ID);}/** Get BPartner (Agent).
@return Customer Agent */
public int GetC_BPartnerSR_ID() {Object ii = Get_Value("C_BPartnerSR_ID");if (ii == null) return 0;return Convert.ToInt32(ii);}/** Set Business Partner.
@param C_BPartner_ID Identifies a Customer/Prospect */
public void SetC_BPartner_ID (int C_BPartner_ID){if (C_BPartner_ID <= 0) Set_Value ("C_BPartner_ID", null);else
Set_Value ("C_BPartner_ID", C_BPartner_ID);}/** Get Business Partner.
@return Identifies a Customer/Prospect */
public int GetC_BPartner_ID() {Object ii = Get_Value("C_BPartner_ID");if (ii == null) return 0;return Convert.ToInt32(ii);}/** Set Location.
@param C_BPartner_Location_ID Identifies the address for this Account/Prospect. */
public void SetC_BPartner_Location_ID (int C_BPartner_Location_ID){if (C_BPartner_Location_ID <= 0) Set_Value ("C_BPartner_Location_ID", null);else
Set_Value ("C_BPartner_Location_ID", C_BPartner_Location_ID);}/** Get Location.
@return Identifies the address for this Account/Prospect. */
public int GetC_BPartner_Location_ID() {Object ii = Get_Value("C_BPartner_Location_ID");if (ii == null) return 0;return Convert.ToInt32(ii);}/** Set Campaign.
@param C_Campaign_ID Marketing Campaign */
public void SetC_Campaign_ID (int C_Campaign_ID){if (C_Campaign_ID <= 0) Set_Value ("C_Campaign_ID", null);else
Set_Value ("C_Campaign_ID", C_Campaign_ID);}/** Get Campaign.
@return Marketing Campaign */
public int GetC_Campaign_ID() {Object ii = Get_Value("C_Campaign_ID");if (ii == null) return 0;return Convert.ToInt32(ii);}/** Set Currency.
@param C_Currency_ID The Currency for this record */
public void SetC_Currency_ID (int C_Currency_ID){if (C_Currency_ID <= 0) Set_Value ("C_Currency_ID", null);else
Set_Value ("C_Currency_ID", C_Currency_ID);}/** Get Currency.
@return The Currency for this record */
public int GetC_Currency_ID() {Object ii = Get_Value("C_Currency_ID");if (ii == null) return 0;return Convert.ToInt32(ii);}/** Set Enquiry Received Date.
@param C_EnquiryRdate Enquiry Received Date */
public void SetC_EnquiryRdate (DateTime? C_EnquiryRdate){Set_Value ("C_EnquiryRdate", (DateTime?)C_EnquiryRdate);}/** Get Enquiry Received Date.
@return Enquiry Received Date */
public DateTime? GetC_EnquiryRdate() {return (DateTime?)Get_Value("C_EnquiryRdate");}/** Set Lead.
@param C_Lead_ID Business Lead */
public void SetC_Lead_ID (int C_Lead_ID){if (C_Lead_ID <= 0) Set_Value ("C_Lead_ID", null);else
Set_Value ("C_Lead_ID", C_Lead_ID);}/** Get Lead.
@return Business Lead */
public int GetC_Lead_ID() {Object ii = Get_Value("C_Lead_ID");if (ii == null) return 0;return Convert.ToInt32(ii);}/** Set Comments.
@param Comments Comments or additional information */
public void SetComments (String Comments){if (Comments != null && Comments.Length > 2000){log.Warning("Length > 2000 - truncated");Comments = Comments.Substring(0,2000);}Set_Value ("Comments", Comments);}/** Get Comments.
@return Comments or additional information */
public String GetComments() {return (String)Get_Value("Comments");}/** Set Description.
@param Description Optional short description of the record */
public void SetDescription (String Description){if (Description != null && Description.Length > 255){log.Warning("Length > 255 - truncated");Description = Description.Substring(0,255);}Set_Value ("Description", Description);}/** Get Description.
@return Optional short description of the record */
public String GetDescription() {return (String)Get_Value("Description");}/** Set Export.
@param Export_ID Export */
public void SetExport_ID (String Export_ID){if (Export_ID != null && Export_ID.Length > 50){log.Warning("Length > 50 - truncated");Export_ID = Export_ID.Substring(0,50);}Set_Value ("Export_ID", Export_ID);}/** Get Export.
@return Export */
public String GetExport_ID() {return (String)Get_Value("Export_ID");}/** Set Is Opportunity.
@param IsOpportunity Is Opportunity */
public void SetIsOpportunity (Boolean IsOpportunity){Set_Value ("IsOpportunity", IsOpportunity);}/** Get Is Opportunity.
@return Is Opportunity */
public Boolean IsOpportunity() {Object oo = Get_Value("IsOpportunity");if (oo != null) { if (oo.GetType() == typeof(bool)) return Convert.ToBoolean(oo); return "Y".Equals(oo);}return false;}/** Set Label.
@param Label Label */
public void SetLabel (Object Label){Set_Value ("Label", Label);}/** Get Label.
@return Label */
public Object GetLabel() {return Get_Value("Label");}/** Set Price List Version.
@param M_PriceList_Version_ID Identifies a unique instance of a Price List */
public void SetM_PriceList_Version_ID (int M_PriceList_Version_ID){if (M_PriceList_Version_ID <= 0) Set_Value ("M_PriceList_Version_ID", null);else
Set_Value ("M_PriceList_Version_ID", M_PriceList_Version_ID);}/** Get Price List Version.
@return Identifies a unique instance of a Price List */
public int GetM_PriceList_Version_ID() {Object ii = Get_Value("M_PriceList_Version_ID");if (ii == null) return 0;return Convert.ToInt32(ii);}/** Set Name.
@param Name Alphanumeric identifier of the entity */
public void SetName (String Name){if (Name != null && Name.Length > 255){log.Warning("Length > 255 - truncated");Name = Name.Substring(0,255);}Set_Value ("Name", Name);}/** Get Name.
@return Alphanumeric identifier of the entity */
public String GetName() {return (String)Get_Value("Name");}/** Set Total Amount.
@param PlannedAmt Total Amount */
public void SetPlannedAmt (Decimal? PlannedAmt){Set_Value ("PlannedAmt", (Decimal?)PlannedAmt);}/** Get Total Amount.
@return Total Amount */
public Decimal GetPlannedAmt() {Object bd =Get_Value("PlannedAmt");if (bd == null) return Env.ZERO;return  Convert.ToDecimal(bd);}/** Set Probability.
@param Probability Probability in Percent */
public void SetProbability (int Probability){Set_Value ("Probability", Probability);}/** Get Probability.
@return Probability in Percent */
public int GetProbability() {Object ii = Get_Value("Probability");if (ii == null) return 0;return Convert.ToInt32(ii);}
/** Ref_BPartner_ID AD_Reference_ID=138 */
public static int REF_BPARTNER_ID_AD_Reference_ID=138;/** Set Prospects.
@param Ref_BPartner_ID Identifies a Prospect */
public void SetRef_BPartner_ID (int Ref_BPartner_ID){if (Ref_BPartner_ID <= 0) Set_Value ("Ref_BPartner_ID", null);else
Set_Value ("Ref_BPartner_ID", Ref_BPartner_ID);}/** Get Prospects.
@return Identifies a Prospect */
public int GetRef_BPartner_ID() {Object ii = Get_Value("Ref_BPartner_ID");if (ii == null) return 0;return Convert.ToInt32(ii);}
/** Ref_Order_ID AD_Reference_ID=290 */
public static int REF_ORDER_ID_AD_Reference_ID=290;/** Set Quotation.
@param Ref_Order_ID Sales Quotation */
public void SetRef_Order_ID (int Ref_Order_ID){if (Ref_Order_ID <= 0) Set_Value ("Ref_Order_ID", null);else
Set_Value ("Ref_Order_ID", Ref_Order_ID);}/** Get Quotation.
@return Sales Quotation */
public int GetRef_Order_ID() {Object ii = Get_Value("Ref_Order_ID");if (ii == null) return 0;return Convert.ToInt32(ii);}
/** SalesRep_ID AD_Reference_ID=190 */
public static int SALESREP_ID_AD_Reference_ID=190;/** Set Sales Rep.
@param SalesRep_ID Company Agent like Sales Representative, Customer Service Representative, ... */
public void SetSalesRep_ID (int SalesRep_ID){if (SalesRep_ID <= 0) Set_Value ("SalesRep_ID", null);else
Set_Value ("SalesRep_ID", SalesRep_ID);}/** Get Sales Rep.
@return Company Agent like Sales Representative, Customer Service Representative, ... */
public int GetSalesRep_ID() {Object ii = Get_Value("SalesRep_ID");if (ii == null) return 0;return Convert.ToInt32(ii);}/** Set Archive.
@param VAS_Archive Archive */
public void SetVAS_Archive (String VAS_Archive){if (VAS_Archive != null && VAS_Archive.Length > 1){log.Warning("Length > 1 - truncated");VAS_Archive = VAS_Archive.Substring(0,1);}Set_Value ("VAS_Archive", VAS_Archive);}/** Get Archive.
@return Archive */
public String GetVAS_Archive() {return (String)Get_Value("VAS_Archive");}/** Set Competitor.
@param VAS_Competitor Competitor */
public void SetVAS_Competitor (String VAS_Competitor){if (VAS_Competitor != null && VAS_Competitor.Length > 1){log.Warning("Length > 1 - truncated");VAS_Competitor = VAS_Competitor.Substring(0,1);}Set_Value ("VAS_Competitor", VAS_Competitor);}/** Get Competitor.
@return Competitor */
public String GetVAS_Competitor() {return (String)Get_Value("VAS_Competitor");}
/** VAS_DecisionCriteria AD_Reference_ID=1000359 */
public static int VAS_DECISIONCRITERIA_AD_Reference_ID=1000359;/** Customer budget not finalized = 10 */
public static String VAS_DECISIONCRITERIA_CustomerBudgetNotFinalized = "10";/** Customization complexity high = 11 */
public static String VAS_DECISIONCRITERIA_CustomizationComplexityHigh = "11";/** Aggressive go-live timeline = 12 */
public static String VAS_DECISIONCRITERIA_AggressiveGo_LiveTimeline = "12";/** Customer team unavailable = 13 */
public static String VAS_DECISIONCRITERIA_CustomerTeamUnavailable = "13";/** Competitor already in advanced stage = 14 */
public static String VAS_DECISIONCRITERIA_CompetitorAlreadyInAdvancedStage = "14";/** Final approver not identified = 15 */
public static String VAS_DECISIONCRITERIA_FinalApproverNotIdentified = "15";/** Is test a valid value.
@param test testvalue
@returns true if valid **/
public bool IsVAS_DecisionCriteriaValid (String test){return test == null || test.Equals("10") || test.Equals("11") || test.Equals("12") || test.Equals("13") || test.Equals("14") || test.Equals("15");}/** Set Decision Criteria.
@param VAS_DecisionCriteria Decision Criteria */
public void SetVAS_DecisionCriteria (String VAS_DecisionCriteria){if (!IsVAS_DecisionCriteriaValid(VAS_DecisionCriteria))
throw new ArgumentException ("VAS_DecisionCriteria Invalid value - " + VAS_DecisionCriteria + " - Reference_ID=1000359 - 10 - 11 - 12 - 13 - 14 - 15");if (VAS_DecisionCriteria != null && VAS_DecisionCriteria.Length > 2){log.Warning("Length > 2 - truncated");VAS_DecisionCriteria = VAS_DecisionCriteria.Substring(0,2);}Set_Value ("VAS_DecisionCriteria", VAS_DecisionCriteria);}/** Get Decision Criteria.
@return Decision Criteria */
public String GetVAS_DecisionCriteria() {return (String)Get_Value("VAS_DecisionCriteria");}/** Set Decision Date.
@param VAS_DecisionDate Decision Date */
public void SetVAS_DecisionDate (DateTime? VAS_DecisionDate){Set_Value ("VAS_DecisionDate", (DateTime?)VAS_DecisionDate);}/** Get Decision Date.
@return Decision Date */
public DateTime? GetVAS_DecisionDate() {return (DateTime?)Get_Value("VAS_DecisionDate");}/** Set Estimated Budget Amount.
@param VAS_EstimatedBudgetAmt Estimated Budget Amount */
public void SetVAS_EstimatedBudgetAmt (Decimal? VAS_EstimatedBudgetAmt){Set_Value ("VAS_EstimatedBudgetAmt", (Decimal?)VAS_EstimatedBudgetAmt);}/** Get Estimated Budget Amount.
@return Estimated Budget Amount */
public Decimal GetVAS_EstimatedBudgetAmt() {Object bd =Get_Value("VAS_EstimatedBudgetAmt");if (bd == null) return Env.ZERO;return  Convert.ToDecimal(bd);}
/** VAS_LastProposalStatus AD_Reference_ID=1000361 */
public static int VAS_LASTPROPOSALSTATUS_AD_Reference_ID=1000361;/** Draft = 10 */
public static String VAS_LASTPROPOSALSTATUS_Draft = "10";/** Sent = 11 */
public static String VAS_LASTPROPOSALSTATUS_Sent = "11";/** Under Review = 12 */
public static String VAS_LASTPROPOSALSTATUS_UnderReview = "12";/** Revision Requested = 13 */
public static String VAS_LASTPROPOSALSTATUS_RevisionRequested = "13";/** Negotiation = 14 */
public static String VAS_LASTPROPOSALSTATUS_Negotiation = "14";/** Accepted = 15 */
public static String VAS_LASTPROPOSALSTATUS_Accepted = "15";/** Rejected = 16 */
public static String VAS_LASTPROPOSALSTATUS_Rejected = "16";/** Expired = 17 */
public static String VAS_LASTPROPOSALSTATUS_Expired = "17";/** Cancelled = 18 */
public static String VAS_LASTPROPOSALSTATUS_Cancelled = "18";/** Converted to Order = 19 */
public static String VAS_LASTPROPOSALSTATUS_ConvertedToOrder = "19";/** Is test a valid value.
@param test testvalue
@returns true if valid **/
public bool IsVAS_LastProposalStatusValid (String test){return test == null || test.Equals("10") || test.Equals("11") || test.Equals("12") || test.Equals("13") || test.Equals("14") || test.Equals("15") || test.Equals("16") || test.Equals("17") || test.Equals("18") || test.Equals("19");}/** Set Last Proposal Status.
@param VAS_LastProposalStatus Last Proposal Status */
public void SetVAS_LastProposalStatus (String VAS_LastProposalStatus){if (!IsVAS_LastProposalStatusValid(VAS_LastProposalStatus))
throw new ArgumentException ("VAS_LastProposalStatus Invalid value - " + VAS_LastProposalStatus + " - Reference_ID=1000361 - 10 - 11 - 12 - 13 - 14 - 15 - 16 - 17 - 18 - 19");if (VAS_LastProposalStatus != null && VAS_LastProposalStatus.Length > 2){log.Warning("Length > 2 - truncated");VAS_LastProposalStatus = VAS_LastProposalStatus.Substring(0,2);}Set_Value ("VAS_LastProposalStatus", VAS_LastProposalStatus);}/** Get Last Proposal Status.
@return Last Proposal Status */
public String GetVAS_LastProposalStatus() {return (String)Get_Value("VAS_LastProposalStatus");}/** Set VAS_LeadNextStep_ID.
@param VAS_LeadNextStep_ID VAS_LeadNextStep_ID */
public void SetVAS_LeadNextStep_ID (int VAS_LeadNextStep_ID){if (VAS_LeadNextStep_ID <= 0) Set_Value ("VAS_LeadNextStep_ID", null);else
Set_Value ("VAS_LeadNextStep_ID", VAS_LeadNextStep_ID);}/** Get VAS_LeadNextStep_ID.
@return VAS_LeadNextStep_ID */
public int GetVAS_LeadNextStep_ID() {Object ii = Get_Value("VAS_LeadNextStep_ID");if (ii == null) return 0;return Convert.ToInt32(ii);}/** Set Lines .
@param VAS_Lines Lines  */
public void SetVAS_Lines (String VAS_Lines){if (VAS_Lines != null && VAS_Lines.Length > 1){log.Warning("Length > 1 - truncated");VAS_Lines = VAS_Lines.Substring(0,1);}Set_Value ("VAS_Lines", VAS_Lines);}/** Get Lines .
@return Lines  */
public String GetVAS_Lines() {return (String)Get_Value("VAS_Lines");}/** Set Next Step Date.
@param VAS_NextStepDate Next Step Date */
public void SetVAS_NextStepDate (DateTime? VAS_NextStepDate){Set_Value ("VAS_NextStepDate", (DateTime?)VAS_NextStepDate);}/** Get Next Step Date.
@return Next Step Date */
public DateTime? GetVAS_NextStepDate() {return (DateTime?)Get_Value("VAS_NextStepDate");}
/** VAS_OppBudget AD_Reference_ID=1000356 */
public static int VAS_OPPBUDGET_AD_Reference_ID=1000356;/** Confirmed = CF */
public static String VAS_OPPBUDGET_Confirmed = "CF";/** Constrained = CN */
public static String VAS_OPPBUDGET_Constrained = "CN";/** No Budget = NB */
public static String VAS_OPPBUDGET_NoBudget = "NB";/** Not Discussed = ND */
public static String VAS_OPPBUDGET_NotDiscussed = "ND";/** Planned = PL */
public static String VAS_OPPBUDGET_Planned = "PL";/** Is test a valid value.
@param test testvalue
@returns true if valid **/
public bool IsVAS_OppBudgetValid (String test){return test == null || test.Equals("CF") || test.Equals("CN") || test.Equals("NB") || test.Equals("ND") || test.Equals("PL");}/** Set Opportunity Budget.
@param VAS_OppBudget Opportunity Budget */
public void SetVAS_OppBudget (String VAS_OppBudget){if (!IsVAS_OppBudgetValid(VAS_OppBudget))
throw new ArgumentException ("VAS_OppBudget Invalid value - " + VAS_OppBudget + " - Reference_ID=1000356 - CF - CN - NB - ND - PL");if (VAS_OppBudget != null && VAS_OppBudget.Length > 2){log.Warning("Length > 2 - truncated");VAS_OppBudget = VAS_OppBudget.Substring(0,2);}Set_Value ("VAS_OppBudget", VAS_OppBudget);}/** Get Opportunity Budget.
@return Opportunity Budget */
public String GetVAS_OppBudget() {return (String)Get_Value("VAS_OppBudget");}
/** VAS_OppStage AD_Reference_ID=1000355 */
public static int VAS_OPPSTAGE_AD_Reference_ID=1000355;/** Prospecting = 10 */
public static String VAS_OPPSTAGE_Prospecting = "10";/** Discovery/Design = 11 */
public static String VAS_OPPSTAGE_DiscoveryDesign = "11";/** Product Evaluation = 12 */
public static String VAS_OPPSTAGE_ProductEvaluation = "12";/** Proposal = 13 */
public static String VAS_OPPSTAGE_Proposal = "13";/** Negotiation = 15 */
public static String VAS_OPPSTAGE_Negotiation = "15";/** Won = 16 */
public static String VAS_OPPSTAGE_Won = "16";/** Lost/Archived = 17 */
public static String VAS_OPPSTAGE_LostArchived = "17";/** Is test a valid value.
@param test testvalue
@returns true if valid **/
public bool IsVAS_OppStageValid (String test){return test == null || test.Equals("10") || test.Equals("11") || test.Equals("12") || test.Equals("13") || test.Equals("15") || test.Equals("16") || test.Equals("17");}/** Set Opportunity Stage.
@param VAS_OppStage Opportunity Stage */
public void SetVAS_OppStage (String VAS_OppStage){if (!IsVAS_OppStageValid(VAS_OppStage))
throw new ArgumentException ("VAS_OppStage Invalid value - " + VAS_OppStage + " - Reference_ID=1000355 - 10 - 11 - 12 - 13 - 15 - 16 - 17");if (VAS_OppStage != null && VAS_OppStage.Length > 2){log.Warning("Length > 2 - truncated");VAS_OppStage = VAS_OppStage.Substring(0,2);}Set_Value ("VAS_OppStage", VAS_OppStage);}/** Get Opportunity Stage.
@return Opportunity Stage */
public String GetVAS_OppStage() {return (String)Get_Value("VAS_OppStage");}
/** VAS_OppType AD_Reference_ID=1000357 */
public static int VAS_OPPTYPE_AD_Reference_ID=1000357;/** New Business = 10 */
public static String VAS_OPPTYPE_NewBusiness = "10";/** Renewal = 11 */
public static String VAS_OPPTYPE_Renewal = "11";/** Upsell = 12 */
public static String VAS_OPPTYPE_Upsell = "12";/** Cross-sell = 13 */
public static String VAS_OPPTYPE_Cross_Sell = "13";/** Service Contract = 14 */
public static String VAS_OPPTYPE_ServiceContract = "14";/** Is test a valid value.
@param test testvalue
@returns true if valid **/
public bool IsVAS_OppTypeValid (String test){return test == null || test.Equals("10") || test.Equals("11") || test.Equals("12") || test.Equals("13") || test.Equals("14");}/** Set Opportunity Type.
@param VAS_OppType Opportunity Type */
public void SetVAS_OppType (String VAS_OppType){if (!IsVAS_OppTypeValid(VAS_OppType))
throw new ArgumentException ("VAS_OppType Invalid value - " + VAS_OppType + " - Reference_ID=1000357 - 10 - 11 - 12 - 13 - 14");if (VAS_OppType != null && VAS_OppType.Length > 2){log.Warning("Length > 2 - truncated");VAS_OppType = VAS_OppType.Substring(0,2);}Set_Value ("VAS_OppType", VAS_OppType);}/** Get Opportunity Type.
@return Opportunity Type */
public String GetVAS_OppType() {return (String)Get_Value("VAS_OppType");}/** Set VAS_Opportunity_ID.
@param VAS_Opportunity_ID VAS_Opportunity_ID */
public void SetVAS_Opportunity_ID (int VAS_Opportunity_ID){if (VAS_Opportunity_ID < 1) throw new ArgumentException ("VAS_Opportunity_ID is mandatory.");Set_ValueNoCheck ("VAS_Opportunity_ID", VAS_Opportunity_ID);}/** Get VAS_Opportunity_ID.
@return VAS_Opportunity_ID */
public int GetVAS_Opportunity_ID() {Object ii = Get_Value("VAS_Opportunity_ID");if (ii == null) return 0;return Convert.ToInt32(ii);}
/** VAS_Reseller_ID AD_Reference_ID=1000360 */
public static int VAS_RESELLER_ID_AD_Reference_ID=1000360;/** Set Reseller.
@param VAS_Reseller_ID Reseller */
public void SetVAS_Reseller_ID (int VAS_Reseller_ID){if (VAS_Reseller_ID <= 0) Set_Value ("VAS_Reseller_ID", null);else
Set_Value ("VAS_Reseller_ID", VAS_Reseller_ID);}/** Get Reseller.
@return Reseller */
public int GetVAS_Reseller_ID() {Object ii = Get_Value("VAS_Reseller_ID");if (ii == null) return 0;return Convert.ToInt32(ii);}
/** VAS_RiskLevel AD_Reference_ID=1000358 */
public static int VAS_RISKLEVEL_AD_Reference_ID=1000358;/** Financial Risk = 10 */
public static String VAS_RISKLEVEL_FinancialRisk = "10";/** Technical Risk = 11 */
public static String VAS_RISKLEVEL_TechnicalRisk = "11";/** Timeline Risk = 12 */
public static String VAS_RISKLEVEL_TimelineRisk = "12";/** Resource Risk = 13 */
public static String VAS_RISKLEVEL_ResourceRisk = "13";/** Competition Risk = 14 */
public static String VAS_RISKLEVEL_CompetitionRisk = "14";/** Approval Risk = 15 */
public static String VAS_RISKLEVEL_ApprovalRisk = "15";/** Is test a valid value.
@param test testvalue
@returns true if valid **/
public bool IsVAS_RiskLevelValid (String test){return test == null || test.Equals("10") || test.Equals("11") || test.Equals("12") || test.Equals("13") || test.Equals("14") || test.Equals("15");}/** Set Risk Level.
@param VAS_RiskLevel Risk Level */
public void SetVAS_RiskLevel (String VAS_RiskLevel){if (!IsVAS_RiskLevelValid(VAS_RiskLevel))
throw new ArgumentException ("VAS_RiskLevel Invalid value - " + VAS_RiskLevel + " - Reference_ID=1000358 - 10 - 11 - 12 - 13 - 14 - 15");if (VAS_RiskLevel != null && VAS_RiskLevel.Length > 2){log.Warning("Length > 2 - truncated");VAS_RiskLevel = VAS_RiskLevel.Substring(0,2);}Set_Value ("VAS_RiskLevel", VAS_RiskLevel);}/** Get Risk Level.
@return Risk Level */
public String GetVAS_RiskLevel() {return (String)Get_Value("VAS_RiskLevel");}/** Set Search Key.
@param Value Search key for the record in the format required - must be unique */
public void SetValue (String Value){if (Value != null && Value.Length > 30){log.Warning("Length > 30 - truncated");Value = Value.Substring(0,30);}Set_Value ("Value", Value);}/** Get Search Key.
@return Search key for the record in the format required - must be unique */
public String GetValue() {return (String)Get_Value("Value");}}
}