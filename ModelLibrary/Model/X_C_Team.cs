namespace ViennaAdvantage.Model{
/** Generated Model - DO NOT CHANGE */
using System;using System.Text;using VAdvantage.DataBase;using VAdvantage.Common;using VAdvantage.Classes;using VAdvantage.Process;using VAdvantage.Model;using VAdvantage.Utility;using System.Data;/** Generated Model for C_Team
 *  @author Raghu (Updated) 
 *  @version Vienna Framework 1.1.1 - $Id$ */
public class X_C_Team : PO{public X_C_Team (Context ctx, int C_Team_ID, Trx trxName) : base (ctx, C_Team_ID, trxName){/** if (C_Team_ID == 0){SetC_Team_ID (0);} */
}public X_C_Team (Ctx ctx, int C_Team_ID, Trx trxName) : base (ctx, C_Team_ID, trxName){/** if (C_Team_ID == 0){SetC_Team_ID (0);} */
}/** Load Constructor 
@param ctx context
@param rs result set 
@param trxName transaction
*/
public X_C_Team (Context ctx, DataRow rs, Trx trxName) : base(ctx, rs, trxName){}/** Load Constructor 
@param ctx context
@param rs result set 
@param trxName transaction
*/
public X_C_Team (Ctx ctx, DataRow rs, Trx trxName) : base(ctx, rs, trxName){}/** Load Constructor 
@param ctx context
@param rs result set 
@param trxName transaction
*/
public X_C_Team (Ctx ctx, IDataReader dr, Trx trxName) : base(ctx, dr, trxName){}/** Static Constructor 
 Set Table ID By Table Name
 added by ->Harwinder */
static X_C_Team(){ Table_ID = Get_Table_ID(Table_Name); model = new KeyNamePair(Table_ID,Table_Name);}/** Serial Version No */
static long serialVersionUID = 28051184265311L;/** Last Updated Timestamp 1/22/2026 10:45:48 AM */
public static long updatedMS = 1769058948522L;/** AD_Table_ID=1000426 */
public static int Table_ID; // =1000426;
/** TableName=C_Team */
public static String Table_Name="C_Team";
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
public override String ToString(){StringBuilder sb = new StringBuilder ("X_C_Team[").Append(Get_ID()).Append("]");return sb.ToString();}/** Set Team.
@param C_Team_ID Team */
public void SetC_Team_ID (int C_Team_ID){if (C_Team_ID < 1) throw new ArgumentException ("C_Team_ID is mandatory.");Set_ValueNoCheck ("C_Team_ID", C_Team_ID);}/** Get Team.
@return Team */
public int GetC_Team_ID() {Object ii = Get_Value("C_Team_ID");if (ii == null) return 0;return Convert.ToInt32(ii);}/** Set Export.
@param Export_ID Export */
public void SetExport_ID (String Export_ID){if (Export_ID != null && Export_ID.Length > 50){log.Warning("Length > 50 - truncated");Export_ID = Export_ID.Substring(0,50);}Set_Value ("Export_ID", Export_ID);}/** Get Export.
@return Export */
public String GetExport_ID() {return (String)Get_Value("Export_ID");}/** Set Name.
@param Name Alphanumeric identifier of the entity */
public void SetName (String Name){if (Name != null && Name.Length > 50){log.Warning("Length > 50 - truncated");Name = Name.Substring(0,50);}Set_Value ("Name", Name);}/** Get Name.
@return Alphanumeric identifier of the entity */
public String GetName() {return (String)Get_Value("Name");}
/** Supervisor_ID AD_Reference_ID=110 */
public static int SUPERVISOR_ID_AD_Reference_ID=110;/** Set Supervisor.
@param Supervisor_ID Supervisor for this user/organization - used for escalation and approval */
public void SetSupervisor_ID (int Supervisor_ID){if (Supervisor_ID <= 0) Set_Value ("Supervisor_ID", null);else
Set_Value ("Supervisor_ID", Supervisor_ID);}/** Get Supervisor.
@return Supervisor for this user/organization - used for escalation and approval */
public int GetSupervisor_ID() {Object ii = Get_Value("Supervisor_ID");if (ii == null) return 0;return Convert.ToInt32(ii);}/** Set Approver Job Title.
@param VA137_AprvrJobTitle Approver Job Title */
public void SetVA137_AprvrJobTitle (String VA137_AprvrJobTitle){if (VA137_AprvrJobTitle != null && VA137_AprvrJobTitle.Length > 255){log.Warning("Length > 255 - truncated");VA137_AprvrJobTitle = VA137_AprvrJobTitle.Substring(0,255);}Set_Value ("VA137_AprvrJobTitle", VA137_AprvrJobTitle);}/** Get Approver Job Title.
@return Approver Job Title */
public String GetVA137_AprvrJobTitle() {return (String)Get_Value("VA137_AprvrJobTitle");}/** Set Arabic Description.
@param VA137_ArabicDescription Arabic Description */
public void SetVA137_ArabicDescription (String VA137_ArabicDescription){if (VA137_ArabicDescription != null && VA137_ArabicDescription.Length > 2000){log.Warning("Length > 2000 - truncated");VA137_ArabicDescription = VA137_ArabicDescription.Substring(0,2000);}Set_Value ("VA137_ArabicDescription", VA137_ArabicDescription);}/** Get Arabic Description.
@return Arabic Description */
public String GetVA137_ArabicDescription() {return (String)Get_Value("VA137_ArabicDescription");}/** Set VA137_Correspondence_ID.
@param VA137_Correspondence_ID VA137_Correspondence_ID */
public void SetVA137_Correspondence_ID (int VA137_Correspondence_ID){if (VA137_Correspondence_ID <= 0) Set_Value ("VA137_Correspondence_ID", null);else
Set_Value ("VA137_Correspondence_ID", VA137_Correspondence_ID);}/** Get VA137_Correspondence_ID.
@return VA137_Correspondence_ID */
public int GetVA137_Correspondence_ID() {Object ii = Get_Value("VA137_Correspondence_ID");if (ii == null) return 0;return Convert.ToInt32(ii);}/** Set Team Email.
@param VA137_Email Team Email */
public void SetVA137_Email (String VA137_Email){if (VA137_Email != null && VA137_Email.Length > 255){log.Warning("Length > 255 - truncated");VA137_Email = VA137_Email.Substring(0,255);}Set_Value ("VA137_Email", VA137_Email);}/** Get Team Email.
@return Team Email */
public String GetVA137_Email() {return (String)Get_Value("VA137_Email");}/** Set English Description.
@param VA137_EnglishDescription English Description */
public void SetVA137_EnglishDescription (String VA137_EnglishDescription){if (VA137_EnglishDescription != null && VA137_EnglishDescription.Length > 2000){log.Warning("Length > 2000 - truncated");VA137_EnglishDescription = VA137_EnglishDescription.Substring(0,2000);}Set_Value ("VA137_EnglishDescription", VA137_EnglishDescription);}/** Get English Description.
@return English Description */
public String GetVA137_EnglishDescription() {return (String)Get_Value("VA137_EnglishDescription");}/** Set Allow Parent To Access.
@param VA137_IsParentAllowed Allow Parent To Access */
public void SetVA137_IsParentAllowed (Boolean VA137_IsParentAllowed){Set_Value ("VA137_IsParentAllowed", VA137_IsParentAllowed);}/** Get Allow Parent To Access.
@return Allow Parent To Access */
public Boolean IsVA137_IsParentAllowed() {Object oo = Get_Value("VA137_IsParentAllowed");if (oo != null) { if (oo.GetType() == typeof(bool)) return Convert.ToBoolean(oo); return "Y".Equals(oo);}return false;}/** Set Registration Team.
@param VA137_IsRegTeam Registration Team */
public void SetVA137_IsRegTeam (Boolean VA137_IsRegTeam){Set_Value ("VA137_IsRegTeam", VA137_IsRegTeam);}/** Get Registration Team.
@return Registration Team */
public Boolean IsVA137_IsRegTeam() {Object oo = Get_Value("VA137_IsRegTeam");if (oo != null) { if (oo.GetType() == typeof(bool)) return Convert.ToBoolean(oo); return "Y".Equals(oo);}return false;}/** Set Label.
@param VA137_Label Label */
public void SetVA137_Label (Object VA137_Label){Set_Value ("VA137_Label", VA137_Label);}/** Get Label.
@return Label */
public Object GetVA137_Label() {return Get_Value("VA137_Label");}/** Set Notification Settings.
@param VA137_NotSettingTab Notification Settings */
public void SetVA137_NotSettingTab (String VA137_NotSettingTab){if (VA137_NotSettingTab != null && VA137_NotSettingTab.Length > 1){log.Warning("Length > 1 - truncated");VA137_NotSettingTab = VA137_NotSettingTab.Substring(0,1);}Set_Value ("VA137_NotSettingTab", VA137_NotSettingTab);}/** Get Notification Settings.
@return Notification Settings */
public String GetVA137_NotSettingTab() {return (String)Get_Value("VA137_NotSettingTab");}/** Set VA137_PickListLine_ID.
@param VA137_PickListLine_ID VA137_PickListLine_ID */
public void SetVA137_PickListLine_ID (int VA137_PickListLine_ID){if (VA137_PickListLine_ID <= 0) Set_Value ("VA137_PickListLine_ID", null);else
Set_Value ("VA137_PickListLine_ID", VA137_PickListLine_ID);}/** Get VA137_PickListLine_ID.
@return VA137_PickListLine_ID */
public int GetVA137_PickListLine_ID() {Object ii = Get_Value("VA137_PickListLine_ID");if (ii == null) return 0;return Convert.ToInt32(ii);}/** Set Recipient Teams.
@param VA137_RecTeamTab Recipient Teams */
public void SetVA137_RecTeamTab (String VA137_RecTeamTab){if (VA137_RecTeamTab != null && VA137_RecTeamTab.Length > 1){log.Warning("Length > 1 - truncated");VA137_RecTeamTab = VA137_RecTeamTab.Substring(0,1);}Set_Value ("VA137_RecTeamTab", VA137_RecTeamTab);}/** Get Recipient Teams.
@return Recipient Teams */
public String GetVA137_RecTeamTab() {return (String)Get_Value("VA137_RecTeamTab");}
/** VA137_RefPickListLine_ID AD_Reference_ID=1000650 */
public static int VA137_REFPICKLISTLINE_ID_AD_Reference_ID=1000650;/** Set Escalation Performer.
@param VA137_RefPickListLine_ID Escalation Performer */
public void SetVA137_RefPickListLine_ID (int VA137_RefPickListLine_ID){if (VA137_RefPickListLine_ID <= 0) Set_Value ("VA137_RefPickListLine_ID", null);else
Set_Value ("VA137_RefPickListLine_ID", VA137_RefPickListLine_ID);}/** Get Escalation Performer.
@return Escalation Performer */
public int GetVA137_RefPickListLine_ID() {Object ii = Get_Value("VA137_RefPickListLine_ID");if (ii == null) return 0;return Convert.ToInt32(ii);}/** Set Remarks.
@param VA137_Remarks Remarks */
public void SetVA137_Remarks (String VA137_Remarks){if (VA137_Remarks != null && VA137_Remarks.Length > 2000){log.Warning("Length > 2000 - truncated");VA137_Remarks = VA137_Remarks.Substring(0,2000);}Set_Value ("VA137_Remarks", VA137_Remarks);}/** Get Remarks.
@return Remarks */
public String GetVA137_Remarks() {return (String)Get_Value("VA137_Remarks");}
/** VA137_ReqReview AD_Reference_ID=1000648 */
public static int VA137_REQREVIEW_AD_Reference_ID=1000648;/** Flexible = F */
public static String VA137_REQREVIEW_Flexible = "F";/** Strict = S */
public static String VA137_REQREVIEW_Strict = "S";/** Is test a valid value.
@param test testvalue
@returns true if valid **/
public bool IsVA137_ReqReviewValid (String test){return test == null || test.Equals("F") || test.Equals("S");}/** Set Required Review.
@param VA137_ReqReview Required Review */
public void SetVA137_ReqReview (String VA137_ReqReview){if (!IsVA137_ReqReviewValid(VA137_ReqReview))
throw new ArgumentException ("VA137_ReqReview Invalid value - " + VA137_ReqReview + " - Reference_ID=1000648 - F - S");if (VA137_ReqReview != null && VA137_ReqReview.Length > 1){log.Warning("Length > 1 - truncated");VA137_ReqReview = VA137_ReqReview.Substring(0,1);}Set_Value ("VA137_ReqReview", VA137_ReqReview);}/** Get Required Review.
@return Required Review */
public String GetVA137_ReqReview() {return (String)Get_Value("VA137_ReqReview");}
/** VA137_SourceType AD_Reference_ID=1000647 */
public static int VA137_SOURCETYPE_AD_Reference_ID=1000647;/** Within Organization = 01 */
public static String VA137_SOURCETYPE_WithinOrganization = "01";/** Outside Organization = 02 */
public static String VA137_SOURCETYPE_OutsideOrganization = "02";/** Is test a valid value.
@param test testvalue
@returns true if valid **/
public bool IsVA137_SourceTypeValid (String test){return test == null || test.Equals("01") || test.Equals("02");}/** Set Team Source Type.
@param VA137_SourceType Team Source Type */
public void SetVA137_SourceType (String VA137_SourceType){if (!IsVA137_SourceTypeValid(VA137_SourceType))
throw new ArgumentException ("VA137_SourceType Invalid value - " + VA137_SourceType + " - Reference_ID=1000647 - 01 - 02");if (VA137_SourceType != null && VA137_SourceType.Length > 2){log.Warning("Length > 2 - truncated");VA137_SourceType = VA137_SourceType.Substring(0,2);}Set_Value ("VA137_SourceType", VA137_SourceType);}/** Get Team Source Type.
@return Team Source Type */
public String GetVA137_SourceType() {return (String)Get_Value("VA137_SourceType");}/** Set Team Member.
@param VA137_TeamMemberTab Team Member */
public void SetVA137_TeamMemberTab (String VA137_TeamMemberTab){if (VA137_TeamMemberTab != null && VA137_TeamMemberTab.Length > 1){log.Warning("Length > 1 - truncated");VA137_TeamMemberTab = VA137_TeamMemberTab.Substring(0,1);}Set_Value ("VA137_TeamMemberTab", VA137_TeamMemberTab);}/** Get Team Member.
@return Team Member */
public String GetVA137_TeamMemberTab() {return (String)Get_Value("VA137_TeamMemberTab");}
/** VA137_TeamType AD_Reference_ID=1000649 */
public static int VA137_TEAMTYPE_AD_Reference_ID=1000649;/** Kuwait Municipality = 01 */
public static String VA137_TEAMTYPE_KuwaitMunicipality = "01";/** Municipal Council = 02 */
public static String VA137_TEAMTYPE_MunicipalCouncil = "02";/** Is test a valid value.
@param test testvalue
@returns true if valid **/
public bool IsVA137_TeamTypeValid (String test){return test == null || test.Equals("01") || test.Equals("02");}/** Set Team Type.
@param VA137_TeamType Team Type */
public void SetVA137_TeamType (String VA137_TeamType){if (!IsVA137_TeamTypeValid(VA137_TeamType))
throw new ArgumentException ("VA137_TeamType Invalid value - " + VA137_TeamType + " - Reference_ID=1000649 - 01 - 02");if (VA137_TeamType != null && VA137_TeamType.Length > 2){log.Warning("Length > 2 - truncated");VA137_TeamType = VA137_TeamType.Substring(0,2);}Set_Value ("VA137_TeamType", VA137_TeamType);}/** Get Team Type.
@return Team Type */
public String GetVA137_TeamType() {return (String)Get_Value("VA137_TeamType");}/** Set Team Validity Till.
@param VA137_TeamValidity Team Validity Till */
public void SetVA137_TeamValidity (DateTime? VA137_TeamValidity){Set_Value ("VA137_TeamValidity", (DateTime?)VA137_TeamValidity);}/** Get Team Validity Till.
@return Team Validity Till */
public DateTime? GetVA137_TeamValidity() {return (DateTime?)Get_Value("VA137_TeamValidity");}
/** VA137_Team_ID AD_Reference_ID=1000642 */
public static int VA137_TEAM_ID_AD_Reference_ID=1000642;/** Set Team.
@param VA137_Team_ID Team */
public void SetVA137_Team_ID (int VA137_Team_ID){if (VA137_Team_ID <= 0) Set_Value ("VA137_Team_ID", null);else
Set_Value ("VA137_Team_ID", VA137_Team_ID);}/** Get Team.
@return Team */
public int GetVA137_Team_ID() {Object ii = Get_Value("VA137_Team_ID");if (ii == null) return 0;return Convert.ToInt32(ii);}/** Set In Current Next.
@param VADMS_InCurrentNext In Current Next */
public void SetVADMS_InCurrentNext (int VADMS_InCurrentNext){Set_Value ("VADMS_InCurrentNext", VADMS_InCurrentNext);}/** Get In Current Next.
@return In Current Next */
public int GetVADMS_InCurrentNext() {Object ii = Get_Value("VADMS_InCurrentNext");if (ii == null) return 0;return Convert.ToInt32(ii);}/** Set In Increment.
@param VADMS_InIncrement In Increment */
public void SetVADMS_InIncrement (int VADMS_InIncrement){Set_Value ("VADMS_InIncrement", VADMS_InIncrement);}/** Get In Increment.
@return In Increment */
public int GetVADMS_InIncrement() {Object ii = Get_Value("VADMS_InIncrement");if (ii == null) return 0;return Convert.ToInt32(ii);}/** Set In Perfix.
@param VADMS_InPerfix In Perfix */
public void SetVADMS_InPerfix (String VADMS_InPerfix){if (VADMS_InPerfix != null && VADMS_InPerfix.Length > 10){log.Warning("Length > 10 - truncated");VADMS_InPerfix = VADMS_InPerfix.Substring(0,10);}Set_Value ("VADMS_InPerfix", VADMS_InPerfix);}/** Get In Perfix.
@return In Perfix */
public String GetVADMS_InPerfix() {return (String)Get_Value("VADMS_InPerfix");}/** Set In Start No.
@param VADMS_InStartNo In Start No */
public void SetVADMS_InStartNo (int VADMS_InStartNo){Set_Value ("VADMS_InStartNo", VADMS_InStartNo);}/** Get In Start No.
@return In Start No */
public int GetVADMS_InStartNo() {Object ii = Get_Value("VADMS_InStartNo");if (ii == null) return 0;return Convert.ToInt32(ii);}/** Set In Suffix.
@param VADMS_InSuffix In Suffix */
public void SetVADMS_InSuffix (String VADMS_InSuffix){if (VADMS_InSuffix != null && VADMS_InSuffix.Length > 10){log.Warning("Length > 10 - truncated");VADMS_InSuffix = VADMS_InSuffix.Substring(0,10);}Set_Value ("VADMS_InSuffix", VADMS_InSuffix);}/** Get In Suffix.
@return In Suffix */
public String GetVADMS_InSuffix() {return (String)Get_Value("VADMS_InSuffix");}/** Set Info Label1.
@param VADMS_InfoLabel1 Info Label1 */
public void SetVADMS_InfoLabel1 (Object VADMS_InfoLabel1){Set_Value ("VADMS_InfoLabel1", VADMS_InfoLabel1);}/** Get Info Label1.
@return Info Label1 */
public Object GetVADMS_InfoLabel1() {return Get_Value("VADMS_InfoLabel1");}/** Set Info Label2.
@param VADMS_InfoLabel2 Info Label2 */
public void SetVADMS_InfoLabel2 (Object VADMS_InfoLabel2){Set_Value ("VADMS_InfoLabel2", VADMS_InfoLabel2);}/** Get Info Label2.
@return Info Label2 */
public Object GetVADMS_InfoLabel2() {return Get_Value("VADMS_InfoLabel2");}/** Set Out Current Next.
@param VADMS_OutCurrentNext Out Current Next */
public void SetVADMS_OutCurrentNext (int VADMS_OutCurrentNext){Set_Value ("VADMS_OutCurrentNext", VADMS_OutCurrentNext);}/** Get Out Current Next.
@return Out Current Next */
public int GetVADMS_OutCurrentNext() {Object ii = Get_Value("VADMS_OutCurrentNext");if (ii == null) return 0;return Convert.ToInt32(ii);}/** Set Out Increment.
@param VADMS_OutIncrement Out Increment */
public void SetVADMS_OutIncrement (int VADMS_OutIncrement){Set_Value ("VADMS_OutIncrement", VADMS_OutIncrement);}/** Get Out Increment.
@return Out Increment */
public int GetVADMS_OutIncrement() {Object ii = Get_Value("VADMS_OutIncrement");if (ii == null) return 0;return Convert.ToInt32(ii);}/** Set Out Perfix.
@param VADMS_OutPerfix Out Perfix */
public void SetVADMS_OutPerfix (String VADMS_OutPerfix){if (VADMS_OutPerfix != null && VADMS_OutPerfix.Length > 10){log.Warning("Length > 10 - truncated");VADMS_OutPerfix = VADMS_OutPerfix.Substring(0,10);}Set_Value ("VADMS_OutPerfix", VADMS_OutPerfix);}/** Get Out Perfix.
@return Out Perfix */
public String GetVADMS_OutPerfix() {return (String)Get_Value("VADMS_OutPerfix");}/** Set Out Start No.
@param VADMS_OutStartNo Out Start No */
public void SetVADMS_OutStartNo (int VADMS_OutStartNo){Set_Value ("VADMS_OutStartNo", VADMS_OutStartNo);}/** Get Out Start No.
@return Out Start No */
public int GetVADMS_OutStartNo() {Object ii = Get_Value("VADMS_OutStartNo");if (ii == null) return 0;return Convert.ToInt32(ii);}/** Set Out Suffix.
@param VADMS_OutSuffix Out Suffix */
public void SetVADMS_OutSuffix (String VADMS_OutSuffix){if (VADMS_OutSuffix != null && VADMS_OutSuffix.Length > 10){log.Warning("Length > 10 - truncated");VADMS_OutSuffix = VADMS_OutSuffix.Substring(0,10);}Set_Value ("VADMS_OutSuffix", VADMS_OutSuffix);}/** Get Out Suffix.
@return Out Suffix */
public String GetVADMS_OutSuffix() {return (String)Get_Value("VADMS_OutSuffix");}/** Set Label.
@param VAS_Label Label */
public void SetVAS_Label (Object VAS_Label){Set_Value ("VAS_Label", VAS_Label);}/** Get Label.
@return Label */
public Object GetVAS_Label() {return Get_Value("VAS_Label");}/** Set Team Member.
@param VAS_TeamMemberBtn Team Member */
public void SetVAS_TeamMemberBtn (String VAS_TeamMemberBtn){if (VAS_TeamMemberBtn != null && VAS_TeamMemberBtn.Length > 1){log.Warning("Length > 1 - truncated");VAS_TeamMemberBtn = VAS_TeamMemberBtn.Substring(0,1);}Set_Value ("VAS_TeamMemberBtn", VAS_TeamMemberBtn);}/** Get Team Member.
@return Team Member */
public String GetVAS_TeamMemberBtn() {return (String)Get_Value("VAS_TeamMemberBtn");}/** Set Search Key.
@param Value Search key for the record in the format required - must be unique */
public void SetValue (String Value){if (Value != null && Value.Length > 50){log.Warning("Length > 50 - truncated");Value = Value.Substring(0,50);}Set_Value ("Value", Value);}/** Get Search Key.
@return Search key for the record in the format required - must be unique */
public String GetValue() {return (String)Get_Value("Value");}}
}