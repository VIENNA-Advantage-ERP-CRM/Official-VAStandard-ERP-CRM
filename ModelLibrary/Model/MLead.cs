/********************************************************
 * Project Name   : VAdvantage
 * Class Name     : MLead
 * Purpose        : for workflow
 * Chronological    Development
 * Raghunandan     05-Jun-2009
 * VAI052           23-Oct-2026  CreateBPContact: unique AD_User.Value for the new contact
 * VAI052           26-Oct-2026  CreateBP: clear error messages (VAS_LeadCreateBP*), real save error reported
 * VAI052           29-Oct-2026  CreateBP(reuseExisting): reuse existing Prospect (same company name), its User and Address instead of creating duplicates
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
using VAdvantage.Logging;
using VAdvantage.Model;
using VAdvantage.DataBase;
using VAdvantage.SqlExec;
using VAdvantage.Utility;
using System.Data;


namespace VAdvantage.Model
{
    public class MLead : X_C_Lead
    {
        #region private variables
        /** BPartner			*/
        private MBPartner _bp = null;
        /** User				*/
        private MUser _user = null;
        /** Request				*/
        private MRequest _request = null;
        /** Project				*/
        private MProject _project = null;
        /** Request Status		*/
        private MStatus _Status = null;
        /** CreateBP(true): reuse existing Prospect / User / Address */
        private bool _reuseExisting = false;
        #endregion

        #region CreateBP outcome (filled by CreateBP, read by the calling process to build its message)
        /** Existing Prospect found (same company name) and mapped to the Lead */
        public bool BPartnerReused { get; private set; }
        /** New Prospect created */
        public bool BPartnerCreated { get; private set; }
        /** Existing User mapped to the Lead (found against the Prospect, or already linked to the Lead) */
        public bool UserReused { get; private set; }
        /** New User created */
        public bool UserCreated { get; private set; }
        /** Existing Address mapped to the Lead (found against the Prospect, or already linked to the Lead) */
        public bool LocationReused { get; private set; }
        /** New Address created */
        public bool LocationCreated { get; private set; }
        #endregion

        /**
        * 	Standard Constructor
        *	@param ctx context
        *	@param C_Lead_ID id
        *	@param trxName trx
        */
        public MLead(Ctx ctx, int C_Lead_ID, Trx trxName) :
            base(ctx, C_Lead_ID, trxName)
        {
            if (C_Lead_ID == 0)
            {
                SetProcessed(false);
            }
        }

        /**
         * 	Load Constructor
         *	@param ctx context
         *	@param dr result Set
         *	@param trxName trx
         */
        public MLead(Ctx ctx, DataRow dr, Trx trxName) :
            base(ctx, dr, trxName)
        {

        }

        /**
         * 	Lead map constructor
         *	@param ctx context
         *	@param map map
         *	@param trxName trx
         */
        public MLead(Ctx ctx, Dictionary<String, String> map, Trx trxName)
            : this(ctx, 0, trxName)
        {

            Load(map);
            //	Overwrite
            //	Set_ValueNoCheck ("C_Lead_ID", null);
            SetIsActive(true);
            SetProcessed(false);
        }

        /**
         * 	Set AD_User_ID from email
         */
        public void SetAD_User_ID()
        {
            if (GetAD_User_ID() != 0)
                return;
            String email = GetEMail();
            if (email != null && email.Length > 0)
            {
                _user = MUser.Get(GetCtx(), email, Get_TrxName());
                if (_user != null)
                {
                    base.SetAD_User_ID(_user.GetAD_User_ID());
                    if (GetC_BPartner_ID() == 0)
                        SetC_BPartner_ID(_user.GetC_BPartner_ID());
                    else if (_user.GetC_BPartner_ID() != GetC_BPartner_ID())
                    {
                        log.Warning("@C_BPartner_ID@ (ID=" + GetC_BPartner_ID()
                            + ") <> @AD_User_ID@ @C_BPartner_ID@ (ID=" + _user.GetC_BPartner_ID() + ")");
                    }
                }
            }
        }

        /**
         * 	Set AD_User_ID
         *	@param AD_User_ID user
         */
        public new void SetAD_User_ID(int AD_User_ID)
        {
            base.SetAD_User_ID(AD_User_ID);
            GetUser();
        }

        /**
         * 	Get User
         *	@return user
         */
        public MUser GetUser()
        {
            if (GetAD_User_ID() == 0)
                _user = null;
            else if (_user == null
                || _user.GetAD_User_ID() != GetAD_User_ID())
                _user = new MUser(GetCtx(), GetAD_User_ID(), Get_TrxName());
            return _user;
        }

        /**
         * 	Set C_BPartner_ID
         *	@param C_BPartner_ID bp
         */
        public new void SetC_BPartner_ID(int C_BPartner_ID)
        {
            base.SetC_BPartner_ID(C_BPartner_ID);
            GetBPartner();
        }

        /**
         * 	Get BPartner
         *	@return bp or null
         */
        public MBPartner GetBPartner()
        {
            if (GetC_BPartner_ID() == 0)
                _bp = null;
            else if (_bp == null
                || _bp.GetC_BPartner_ID() != GetC_BPartner_ID())
                _bp = new MBPartner(GetCtx(), GetC_BPartner_ID(), Get_TrxName());
            return _bp;
        }

        /**
         * 	Set R_Request_ID
         *	@param R_Request_ID
         */
        public new void SetR_Request_ID(int R_Request_ID)
        {
            base.SetR_Request_ID(R_Request_ID);
            GetRequest();
        }

        /**
         * 	Get Request
         *	@return request
         */
        public MRequest GetRequest()
        {
            if (GetR_Request_ID() == 0)
                _request = null;
            else if (_request == null
                || _request.GetR_Request_ID() != GetR_Request_ID())
                _request = new MRequest(GetCtx(), GetR_Request_ID(), Get_TrxName());
            return _request;
        }

        /**
         * 	Set R_Status_ID
         *	@see Model.X_C_Lead#SetR_Status_ID(int)
         *	@param R_Status_ID
         */
        public new void SetR_Status_ID(int R_Status_ID)
        {
            if (IsR_Status_IDValid(R_Status_ID))
                base.SetR_Status_ID(R_Status_ID);
            else
                base.SetR_Status_ID(0);
            GetStatus();
        }

        /**
         * 	Is R_Status_ID Valid
         *	@param R_Status_ID id
         *	@return true if valid
         */
        public bool IsR_Status_IDValid(int R_Status_ID)
        {
            if (R_Status_ID == 0)
                return true;

            _Status = MStatus.Get(GetCtx(), R_Status_ID);
            int R_StatusCategory_ID = _Status.GetR_StatusCategory_ID();
            //
            int R_RequestType_ID = GetR_RequestType_ID();
            if (R_RequestType_ID == 0)
            {
                log.Warning("No Client Request Type");
                return false;
            }
            MRequestType rt = MRequestType.Get(GetCtx(), R_RequestType_ID);
            if (rt.GetR_StatusCategory_ID() != R_StatusCategory_ID)
            {
                log.Warning("Status Category different - Status("
                    + R_StatusCategory_ID + ") <> RequestType("
                    + rt.GetR_StatusCategory_ID() + ")");
                return false;
            }
            return true;
        }

        /**
         * 	Get R_RequestType_ID
         *	@return Request Type
         */
        private int GetR_RequestType_ID()
        {
            MClientInfo ci = MClientInfo.Get(GetCtx(), GetAD_Client_ID());
            int R_RequestType_ID = ci.GetR_RequestType_ID();
            if (R_RequestType_ID != 0)
                return R_RequestType_ID;
            log.Warning("Set Request Type in Window Client Info");

            //	Default
            MRequestType rt = MRequestType.GetDefault(GetCtx());
            if (rt != null)
            {
                R_RequestType_ID = rt.GetR_RequestType_ID();
                ci.SetR_RequestType_ID(R_RequestType_ID);
                ci.Save();
                return R_RequestType_ID;
            }
            //
            return 0;
        }

        /**
         * 	Get Status
         *	@return status or null
         */
        public new MStatus GetStatus()
        {
            if (GetR_Status_ID() == 0)
                _Status = null;
            else if (_Status == null
                || _Status.GetR_Status_ID() != GetR_Status_ID())
                _Status = MStatus.Get(GetCtx(), GetR_Status_ID());
            return _Status;
        }

        /**
         * 	Set C_Project_ID
         *	@param C_Project_ID project
         */
        public new void SetC_Project_ID(int C_Project_ID)
        {
            base.SetC_Project_ID(C_Project_ID);
            GetProject();
        }

        /**
         * 	Get Project
         *	@return project or null
         */
        public MProject GetProject()
        {
            if (GetC_Project_ID() == 0)
                _project = null;
            else if (_project == null
                || _project.GetC_Project_ID() != GetC_Project_ID())
                _project = new MProject(GetCtx(), GetC_Project_ID(), Get_TrxName());
            return _project;
        }

        /**
         * 	Get Name
         *	@return not null value
         */
        public new String GetName()
        {
            String name = base.GetName();			//	Subject
            if (name == null)
            {
                name = GetBPName();					//	BPartner
                if (name == null)
                {
                    name = GetContactName();		//	Contact
                    if (name == null)
                    {
                        name = GetDocumentNo();		//	DocumentNo
                        if (name == null)
                            name = "Lead";
                    }
                }
            }
            return name;
        }

        /**
         * 	String Representation
         *	@return info
         */
        public override String ToString()
        {
            StringBuilder sb = new StringBuilder("MLead[");
            sb.Append(Get_ID())
                .Append("-").Append(GetName()).Append("]");
            return sb.ToString();
        }

        /**
         * 	Create BP, Contact, Location from Lead
         *	@return error message
         */
        public String CreateBP()
        {
            return CreateBP(false);
        }

        /**
         * 	Create BP, Contact, Location from Lead
         *	@param reuseExisting if true, an existing Prospect with the same company name (and its
         *	User and Address matching the Lead) is mapped to the Lead instead of creating a new one.
         *	What was created / reused is available afterwards in the outcome properties.
         *	@return error message
         */
        public String CreateBP(bool reuseExisting)
        {
            BPartnerReused = BPartnerCreated = false;
            UserReused = UserCreated = false;
            LocationReused = LocationCreated = false;
            _reuseExisting = reuseExisting;

            if (GetC_BPartner_ID() != 0 && GetAD_User_ID() != 0 && GetC_BPartner_Location_ID() == 0)
                return "@AlreadyExists@: @C_BPartner_ID@ (ID=" + GetC_BPartner_ID() + ")";

            //	BPartner
            if (GetC_BPartner_ID() == 0
                && (GetBPName() != null && GetBPName().Length > 0))
            {
                //	Existing User
                _user = GetUser();
                if (_user != null)
                {
                    if (_user.GetC_BPartner_ID() != 0)
                    {
                        SetRef_BPartner_ID(_user.GetC_BPartner_ID());
                        log.Info("Set to BPartner of User - " + _user);
                        if (reuseExisting)
                        {
                            //VAI052-the location below needs the Business Partner of the User
                            _bp = new MBPartner(GetCtx(), _user.GetC_BPartner_ID(), Get_TrxName());
                            BPartnerReused = true;
                            UserReused = true;
                        }
                        return CreateBPLocation();
                    }
                }

                //VAI052-Existing Prospect with the same company name: map it instead of creating a new one
                if (reuseExisting)
                {
                    int existingBPartnerID = GetExistingProspectID(GetBPName());
                    if (existingBPartnerID > 0)
                    {
                        _bp = new MBPartner(GetCtx(), existingBPartnerID, Get_TrxName());
                        BPartnerReused = true;
                        log.Info("Existing Prospect found for company name '" + GetBPName() + "' - " + _bp);
                    }
                }

                if (!BPartnerReused)
                {
                    //
                    _bp = new MBPartner(GetCtx(), Get_TrxName());	//	Template
                    _bp.SetAD_Org_ID(GetAD_Org_ID());
                    //_bp.SetValue(GetBPName());
                    _bp.SetName(GetBPName());
                    _bp.SetIsCustomer(false);
                    _bp.SetIsProspect(true);
                    _bp.SetSOCreditStatus("O");
                    // Check Removed as per Surya Sir's Decision BY Lokesh

                    //_bp.SetC_Campaign_ID(GetC_Campaign_ID());
                    //
                    if (GetC_BP_Group_ID() == 0)
                    {
                        // ShowMessage.Info("First Select Customer Group Then process again", true, null, null);
                        return null;
                    }
                    else
                    {
                        _bp.SetC_BP_Group_ID(GetC_BP_Group_ID());
                    }
                    MBPGroup gp = new MBPGroup(GetCtx(), GetC_BP_Group_ID(), Get_TrxName());
                    _bp.SetM_PriceList_ID(gp.GetM_PriceList_ID());
                    if (GetC_BP_Size_ID() != 0)
                        _bp.SetC_BP_Size_ID(GetC_BP_Size_ID());
                    if (GetURL() != null)
                        _bp.SetURL(GetURL());
                    if (GetC_BP_Status_ID() != 0)
                        _bp.SetC_BP_Status_ID(GetC_BP_Status_ID());
                    if (GetC_IndustryCode_ID() != 0)
                        _bp.SetC_IndustryCode_ID(GetC_IndustryCode_ID());
                    if (GetNAICS() != null)
                        _bp.SetNAICS(GetNAICS());
                    if (GetDUNS() != null)
                        _bp.SetDUNS(GetDUNS());
                    if (GetNumberEmployees() != 0)
                        _bp.SetNumberEmployees(GetNumberEmployees());
                    if (GetSalesVolume() != 0)
                        _bp.SetSalesVolume(GetSalesVolume());
                    if (GetSalesRep_ID() != 0)
                        _bp.SetSalesRep_ID(GetSalesRep_ID());
                    if (GetC_Campaign_ID() != 0)
                        _bp.SetC_Campaign_ID(GetC_Campaign_ID());

                    _bp.Set_Value("C_SalesRegion_ID", GetC_SalesRegion_ID());
                    _bp.SetC_Country_ID(GetC_Country_ID());
                    _bp.SetDescription(GetDescription());
                    _bp.Set_Value("C_Lead_ID", GetC_Lead_ID());
                    _bp.SetEMail(GetEMail());
                    _bp.SetMobile(GetMobile());
                    _bp.Set_Value("Phone", Get_Value("Phone"));
                    _bp.Set_Value("R_Source_ID", GetR_Source_ID());
                    _bp.Set_Value("C_BPartnerSR_ID", GetC_BPartnerSR_ID());
                    _bp.Set_Value("LeadRating", GetLeadRating());



                    // VIS0060: Set Next Step, Next Step By and Follow update
                    if (Env.IsModuleInstalled("VA061_"))
                    {
                        //_bp.Set_Value("VA061_NextStep", Get_Value("VA061_NextStep"));
                        if (Get_Value("C_Followupdate") != null)
                        {
                            _bp.Set_Value("C_Followupdate", Util.GetValueOfDateTime(Get_Value("C_Followupdate")));
                        }
                        _bp.Set_Value("VA061_NextStepBy", Get_Value("VA061_NextStepBy"));
                        _bp.Set_Value("LeadRating", GetLeadRating());
                        _bp.Set_Value("C_LeadQualification_ID", GetC_LeadQualification_ID());
                        _bp.Set_Value("R_Status_ID", GetR_Status_ID());
                        _bp.Set_Value("Created", GetCreated());
                        _bp.Set_Value("VA061_ThreadID", Get_Value("VA061_ThreadID"));
                        _bp.Set_Value("VA061_SheetURL", Get_Value("VA061_SheetURL"));
                        _bp.Set_Value("VA061_SheetName", Get_Value("VA061_SheetName"));
                        _bp.Set_Value("VA061_SheetID", Get_Value("VA061_SheetID"));
                        _bp.Set_Value("VA061_SheetPDFURL", Get_Value("VA061_SheetPDFURL"));
                        _bp.Set_Value("VA061_ProcessStage", Get_Value("VA061_ProcessStage"));
                    }
                    if (!_bp.Save())
                    {
                        return GetSaveErrorMessage("VAS_LeadCreateBPFailed");
                    }
                    BPartnerCreated = true;
                }

                //	Update User
                if (_user != null && _user.GetC_BPartner_ID() == 0)
                {
                    _user.SetC_BPartner_ID(_bp.GetC_BPartner_ID());
                    if (!_user.Save())
                    {
                        return GetSaveErrorMessage("VAS_LeadCreateBPUserLinkFailed");
                    }
                }
                //	Save BP
                SetRef_BPartner_ID(_bp.GetC_BPartner_ID());
                SetC_BPartnerSR_ID(_bp.GetC_BPartner_ID());
            }

            string error = CreateBPContact();
            if (error != null && error.Length > 0)
                return error;

            //VAI052-Without a Business Partner the location and the update below cannot work
            //(previously ended in a NullReferenceException), so report it clearly instead
            if (_bp == null)
            {
                return Msg.GetMsg(GetCtx(), "VAS_LeadCreateBPNoPartner");
            }
            error = CreateBPLocation();
            if (error != null && error.Length > 0)
                return error;

            try
            {
                int id = _bp.GetC_BPartner_ID();
                //VAI052 22-oct-26 removed part , Value='" + _bp.GetValue() + GetBPName() + "';
                //VAI052 29-Oct-26 No need to set description blank.
                //string qry = "UPDATE C_BPartner SET Description='' WHERE C_BPartner_ID=" + id;
                //int check = DB.ExecuteQuery(qry, null, Get_TrxName());


                if (GetR_InterestArea_ID() != 0)
                {
                    StringBuilder sql = new StringBuilder("SELECT R_InterestArea_ID FROM vss_lead_interestarea WHERE C_Lead_ID=" + GetC_Lead_ID());
                    DataSet ds = DB.ExecuteDataset(sql.ToString(), null, Get_TrxName());
                    if (ds != null && ds.Tables.Count > 0 && ds.Tables[0].Rows.Count > 0)
                    {
                        int UserId = 0, jobID = 0, locID = 0;
                        string mail = "", phone = "", fax = "";

                        sql.Clear();
                        sql.Append("SELECT AD_User_ID, C_Job_ID, Email FROM AD_User WHERE C_BPartner_ID = " + GetRef_BPartner_ID());
                        DataSet ds1 = DB.ExecuteDataset(sql.ToString(), null, Get_TrxName());
                        if (ds1 != null && ds1.Tables.Count > 0 && ds1.Tables[0].Rows.Count > 0)
                        {
                            UserId = Util.GetValueOfInt(ds1.Tables[0].Rows[0]["AD_User_ID"]);
                            jobID = Util.GetValueOfInt(ds1.Tables[0].Rows[0]["C_Job_ID"]);
                            mail = Util.GetValueOfString(ds1.Tables[0].Rows[0]["Email"]);
                        }

                        sql.Clear();
                        sql.Append("SELECT C_BPartner_Location_ID, Phone, Fax FROM C_BPartner_Location WHERE C_BPartner_ID = " + GetRef_BPartner_ID());
                        ds1 = DB.ExecuteDataset(sql.ToString(), null, Get_TrxName());
                        if (ds1 != null && ds1.Tables.Count > 0 && ds1.Tables[0].Rows.Count > 0)
                        {
                            locID = Util.GetValueOfInt(ds1.Tables[0].Rows[0]["C_BPartner_Location_ID"]);
                            phone = Util.GetValueOfString(ds1.Tables[0].Rows[0]["Phone"]);
                            fax = Util.GetValueOfString(ds1.Tables[0].Rows[0]["Fax"]);
                        }

                        foreach (DataRow dr in ds.Tables[0].Rows)
                        {
                            //VAI052-existing Prospect: do not add the same Interest Area again for the same contact
                            if (BPartnerReused && Util.GetValueOfInt(DB.ExecuteScalar(
                                "SELECT COUNT(*) FROM R_ContactInterest WHERE R_InterestArea_ID=" + Util.GetValueOfInt(dr[0])
                                + " AND C_BPartner_ID=" + GetRef_BPartner_ID() + " AND AD_User_ID=" + UserId, null, Get_TrxName())) > 0)
                            {
                                continue;
                            }
                            X_R_ContactInterest Prospect = new X_R_ContactInterest(GetCtx(), 0, Get_TrxName());
                            Prospect.SetR_InterestArea_ID(Util.GetValueOfInt(dr[0]));
                            Prospect.SetC_BPartner_ID(GetRef_BPartner_ID());
                            Prospect.SetC_BPartner_Location_ID(locID);
                            Prospect.SetPhone(phone);
                            Prospect.SetFax(fax);
                            Prospect.SetAD_User_ID(UserId);
                            Prospect.SetC_Job_ID(jobID);
                            Prospect.SetSubscribeDate(DateTime.Today);
                            Prospect.SetEMail(mail);
                            if (!Prospect.Save())
                            {
                                return GetSaveErrorMessage("VAS_LeadCreateBPInterestFailed");
                            }
                        }
                    }
                    //dr.Close();
                }
            }
            catch (Exception ex)
            {
                log.Log(Level.SEVERE, "MLead" + ex.Message, ex);
                return Msg.GetMsg(GetCtx(), "VAS_LeadCreateBPUpdateFailed");
                //if (dr != null)
                //{
                //    dr.Close();
                //    dr = null;
                //}
            }

            return null;
        }

        /**
         * 	Create BP Contact from Lead
         *	@return error
         */
        private String CreateBPContact()
        {
            //	Contact exists
            if (GetAD_User_ID() != 0)
            {
                //VAI052-the User already linked to the Lead is the one that is mapped
                if (_reuseExisting)
                    UserReused = true;
                return null;
            }

            //	Something to save
            if ((GetContactName() != null && GetContactName().Length > 0))
            {
                ;
            }
            else
            {
                log.Fine("No BP Contact Info to save");
                return null;
            }

            if (_user == null)
            {
                if (_bp == null)
                    _user = new MUser(GetCtx(), 0, Get_TrxName());
                else
                    _user = new MUser(_bp);
            }
            //VAI052-existing Prospect: use the User of that Prospect with the same name instead of creating a duplicate
            if (BPartnerReused && _bp != null)
            {
                int existingUserID = GetExistingUserID(_bp.GetC_BPartner_ID());
                if (existingUserID > 0)
                {
                    SetAD_User_ID(existingUserID);
                    UserReused = true;
                    log.Info("Existing User " + existingUserID + " of Prospect " + _bp.GetC_BPartner_ID() + " mapped to the Lead");
                    return null;
                }
            }

            // If upgrade 4 module is not updated with new columns, then set contactname on user , else set firstname and lastname
            if (Get_ColumnIndex("FirstName") < 0)
            {
                _user.SetName(GetContactName());
            }
            else
            {
                _user.SetName(GetFirstName());
                _user.Set_ValueNoCheck("LastName", GetLastName());
            }
            //
            if (GetC_Job_ID() != 0)
                _user.SetC_Job_ID(GetC_Job_ID());
            if (GetEMail() != null)
                _user.SetEMail(GetEMail());
            if (GetC_Greeting_ID() != 0)
                _user.SetC_Greeting_ID(GetC_Greeting_ID());
            if (GetPhone() != null)
                _user.SetPhone(GetPhone());
            if (GetPhone2() != null)
                _user.SetPhone2(GetPhone2());
            if (GetFax() != null)
                _user.SetFax(GetFax());
            if (GetTitle() != null)
                _user.SetTitle(GetTitle());
            //VAI050-Set LinkedInProfile and mobile on prospect     
            if (GetMobile() != null)
                _user.SetMobile(GetMobile());

            _user.Set_Value("LinkedInProfile", Get_Value("LinkedInProfile"));

            // VAI052: a new contact must get an AD_User.Value (User ID) that no other user has,
            // otherwise the save fails with "User ID already exist". Use the contact name and,
            // when it is taken, add the next free number (name1, name2, ...).
            if (_user.Get_ID() == 0 && !string.IsNullOrWhiteSpace(GetContactName()))
            {
                _user.SetValue(GetUniqueUserValue(GetContactName()));
            }


            if (!_user.Save())
            {
                return GetSaveErrorMessage("VAS_LeadCreateBPContactFailed");
            }
            SetAD_User_ID(_user.GetAD_User_ID());
            UserCreated = true;
            return null;
        }

        /// <summary>
        /// Returns an AD_User.Value that is not used yet: the given value, or the value followed by
        /// 1, 2, 3 ... when it is already taken. All blanks are removed from the given value first
        /// ("John Smith" becomes "JohnSmith"). Checked across all clients and ignoring case, so
        /// the result is safe whichever way the framework scopes the User ID rule. The check runs
        /// inside the current transaction, so users created earlier in the same run are seen.
        /// </summary>
        /// <param name="baseValue">wanted value, for example the contact name</param>
        /// <returns>unused value</returns>
        private string GetUniqueUserValue(string baseValue)
        {
            const int maxLength = 40;   // AD_User.Value length - keeps name + number inside the column
            // VAI052: the User ID (Value) must not contain blanks - remove every whitespace character, also between words
            string root = new string(baseValue.Where(c => !char.IsWhiteSpace(c)).ToArray());
            if (root.Length > maxLength)
                root = root.Substring(0, maxLength);

            string sql = "SELECT COUNT(*) FROM AD_User WHERE UPPER(Value) = UPPER(@userValue)";
            string candidate = root;
            int seq = 0;
            while (seq < 10000
                && Util.GetValueOfInt(DB.ExecuteScalar(sql,
                    new System.Data.SqlClient.SqlParameter[] { new System.Data.SqlClient.SqlParameter("@userValue", candidate) },
                    Get_Trx())) > 0)
            {
                seq++;
                string suffix = seq.ToString();
                candidate = root.Substring(0, Math.Min(root.Length, maxLength - suffix.Length)) + suffix;
            }
            return candidate;
        }
        /**
         * 	Create BP Location from Lead
         *	@return error message
         */
        private String CreateBPLocation()
        {
            if (GetC_BPartner_Location_ID() != 0)
            {
                //VAI052-the Address already linked to the Lead is the one that is mapped
                if (_reuseExisting)
                    LocationReused = true;
                return null;
            }
            if (GetC_Country_ID() == 0)	//	mandatory
                return null;

            //	Something to save
            if ((GetAddress1() != null && GetAddress1().Length > 0)
                || (GetPostal() != null && GetPostal().Length > 0)
                || (GetCity() != null && GetCity().Length > 0)
                || (GetRegionName() != null && GetRegionName().Length > 0)
            )
            {
                ;
            }
            else
            {
                log.Fine("No BP Location Info to save");
                return null;
            }

            //	Address
            MLocation location = new MLocation(GetCtx(), GetC_Country_ID(),
                GetC_Region_ID(), GetCity(), Get_TrxName());
            location.SetAddress1(GetAddress1());
            location.SetAddress2(GetAddress2());
            location.SetPostal(GetPostal());
            location.SetPostal_Add(GetPostal_Add());
            location.SetRegionName(GetRegionName());

            //VAI052-existing Prospect: use its Address that matches the Lead's address instead of creating a duplicate
            if (BPartnerReused && _bp != null)
            {
                int existingLocationID = GetExistingBPLocationID(location);
                if (existingLocationID > 0)
                {
                    SetC_BPartner_Location_ID(existingLocationID);
                    LocationReused = true;
                    log.Info("Existing Address " + existingLocationID + " of Prospect " + _bp.GetC_BPartner_ID() + " mapped to the Lead");
                    return null;
                }
            }

            if (location.Save())
            {
                MBPartnerLocation bpl = new MBPartnerLocation(_bp);
                bpl.SetC_Location_ID(location.GetC_Location_ID());
                bpl.SetPhone(GetPhone());
                bpl.SetPhone2(GetPhone2());
                bpl.SetFax(GetFax());
                bpl.SetC_SalesRegion_ID(GetC_SalesRegion_ID());
                if (!bpl.Save())
                {
                    return GetSaveErrorMessage("VAS_LeadCreateBPPartnerLocationFailed");
                }
                SetC_BPartner_Location_ID(bpl.GetC_BPartner_Location_ID());
                LocationCreated = true;
            }
            else
            {
                return GetSaveErrorMessage("VAS_LeadCreateBPAddressFailed");
            }
            return null;
        }

        /// <summary>
        /// Active Prospect that has the given company name (ignoring case and surrounding blanks),
        /// in the Lead's client. When several have the same name the oldest one is used.
        /// </summary>
        /// <param name="companyName">company name of the Lead</param>
        /// <returns>C_BPartner_ID or 0 when there is none</returns>
        private int GetExistingProspectID(string companyName)
        {
            if (string.IsNullOrWhiteSpace(companyName))
                return 0;

            string sql = "SELECT C_BPartner_ID FROM C_BPartner WHERE AD_Client_ID = " + GetAD_Client_ID()
                + " AND IsActive = 'Y' AND IsProspect = 'Y' AND UPPER(TRIM(Name)) = UPPER(@bpName)"
                + " ORDER BY C_BPartner_ID";
            return Util.GetValueOfInt(DB.ExecuteScalar(sql,
                new System.Data.SqlClient.SqlParameter[] { new System.Data.SqlClient.SqlParameter("@bpName", companyName.Trim()) },
                Get_Trx()));
        }

        /// <summary>
        /// Active User of the given Business Partner with the same name as the contact of the Lead.
        /// Same rule as the Business Partner import (name within the same Business Partner); when the
        /// Lead keeps first and last name separately both must match, as that is how the User is saved.
        /// </summary>
        /// <param name="C_BPartner_ID">Business Partner whose Users are searched</param>
        /// <returns>AD_User_ID or 0 when there is none</returns>
        private int GetExistingUserID(int C_BPartner_ID)
        {
            if (C_BPartner_ID <= 0)
                return 0;

            bool separateNames = Get_ColumnIndex("FirstName") >= 0;
            string name = separateNames ? GetFirstName() : GetContactName();
            if (string.IsNullOrWhiteSpace(name))
                return 0;

            List<System.Data.SqlClient.SqlParameter> param = new List<System.Data.SqlClient.SqlParameter>();
            param.Add(new System.Data.SqlClient.SqlParameter("@userName", name.Trim()));
            StringBuilder sql = new StringBuilder("SELECT AD_User_ID FROM AD_User WHERE C_BPartner_ID = " + C_BPartner_ID
                + " AND IsActive = 'Y' AND UPPER(TRIM(Name)) = UPPER(@userName)");
            if (separateNames)
            {
                string lastName = GetLastName();
                if (string.IsNullOrWhiteSpace(lastName))
                {
                    //VAI052-no COALESCE/NVL with '' here: LastName is a national-character column and Oracle
                    //rejects mixing it with a plain literal (ORA-12704). Oracle stores '' as NULL.
                    sql.Append(" AND (LastName IS NULL OR LENGTH(TRIM(LastName)) = 0)");
                }
                else
                {
                    param.Add(new System.Data.SqlClient.SqlParameter("@lastName", lastName.Trim()));
                    sql.Append(" AND UPPER(TRIM(LastName)) = UPPER(@lastName)");
                }
            }
            sql.Append(" ORDER BY AD_User_ID");
            return Util.GetValueOfInt(DB.ExecuteScalar(sql.ToString(), param.ToArray(), Get_Trx()));
        }

        /// <summary>
        /// Active Address of the current Business Partner that equals the (not yet saved) address built
        /// from the Lead. Uses the existing address comparison, MLocation.Equals(country, region, postal,
        /// postal add, city, address 1, address 2); the address is read fresh, not from the cache.
        /// </summary>
        /// <param name="location">address built from the Lead (not saved)</param>
        /// <returns>C_BPartner_Location_ID or 0 when there is none</returns>
        private int GetExistingBPLocationID(MLocation location)
        {
            foreach (MBPartnerLocation bpl in _bp.GetLocations(true))
            {
                if (!bpl.IsActive() || bpl.GetC_Location_ID() == 0)
                    continue;
                MLocation existing = new MLocation(GetCtx(), bpl.GetC_Location_ID(), Get_TrxName());
                if (existing.Get_ID() != 0 && existing.Equals(location.GetC_Country_ID(), location.GetC_Region_ID(),
                    location.GetPostal(), location.GetPostal_Add(), location.GetCity(),
                    location.GetAddress1(), location.GetAddress2()))
                {
                    return bpl.GetC_BPartner_Location_ID();
                }
            }
            return 0;
        }

        /// <summary>
        /// Translated message for a failed save, followed by the reason the framework recorded
        /// (when there is one), so the user sees why and not only that the save failed.
        /// </summary>
        /// <param name="msgKey">message key</param>
        /// <returns>message</returns>
        private string GetSaveErrorMessage(string msgKey)
        {
            string msg = Msg.GetMsg(GetCtx(), msgKey);
            ValueNamePair vp = VLogger.RetrieveError();
            if (vp != null)
            {
                string detail = vp.GetName();
                if (string.IsNullOrEmpty(detail))
                {
                    detail = vp.GetValue();
                }
                if (!string.IsNullOrEmpty(detail))
                {
                    msg += ": " + detail;
                }
            }
            return msg;
        }

        /**
         * 	Create Project from Lead
         *	@return error message
         */
        public String CreateProject(int C_ProjectType_ID)
        {
            if (GetC_Project_ID() != 0)
                return "@AlreadyExists@: @C_Project_ID@ (ID=" + GetC_Project_ID() + ")";
            if (GetC_BPartner_ID() == 0)
            {
                String retValue = CreateBP();
                if (retValue != null)
                    return retValue;
            }
            _project = new MProject(GetCtx(), 0, Get_TrxName());
            _project.SetAD_Org_ID(GetAD_Org_ID());
            _project.SetProjectLineLevel(MProject.PROJECTLINELEVEL_Project);
            _project.SetName(GetName());
            _project.SetDescription(GetDescription());
            _project.SetNote(GetHelp());
            //
            //VIS0336:chnages done for lead window

            if (Env.IsModuleInstalled("VA047_"))
            {

                if ((GetC_BPartner_ID() == 0 && GetRef_BPartner_ID() != 0) || (GetC_BPartner_ID() == 0 && GetRef_BPartner_ID() != 0 && GetC_Project_ID() != 0))
                {
                    _project.SetC_BPartner_ID(GetC_BPartner_ID());
                    _project.SetC_BPartnerSR_ID(GetC_BPartnerSR_ID());
                    _project.SetRef_BPartner_ID(GetRef_BPartner_ID());

                }

                else if (GetC_BPartner_ID() > 0 && GetRef_BPartner_ID() == 0 && GetC_Project_ID() == 0)
                {
                    _project.SetC_BPartner_ID(GetC_BPartner_ID());
                    _project.SetC_BPartnerSR_ID(GetC_BPartner_ID());
                    _project.SetRef_BPartner_ID(GetC_BPartner_ID());

                }

                else if (GetC_BPartner_ID() > 0 && GetRef_BPartner_ID() > 0)
                {
                    _project.SetC_BPartner_ID(GetC_BPartner_ID());
                    _project.SetC_BPartnerSR_ID(GetRef_BPartner_ID());
                    _project.SetRef_BPartner_ID(GetRef_BPartner_ID());

                }
                else if (GetC_BPartner_ID() == 0 && GetRef_BPartner_ID() == 0 && GetC_Project_ID() == 0)
                {
                    _project.SetC_BPartner_ID(GetC_BPartner_ID());
                    _project.SetC_BPartnerSR_ID(GetRef_BPartner_ID());
                    _project.SetRef_BPartner_ID(GetRef_BPartner_ID());

                }

            }
            else
            {
                _project.SetC_BPartner_ID(GetC_BPartner_ID());

            }

            _project.SetC_BPartner_Location_ID(GetC_BPartner_Location_ID());
            _project.SetAD_User_ID(GetAD_User_ID());
            _project.SetC_Campaign_ID(GetC_Campaign_ID());
            _project.SetC_ProjectType_ID(C_ProjectType_ID);
            _project.SetSalesRep_ID(GetSalesRep_ID());
            _project.SetC_SalesRegion_ID(GetC_SalesRegion_ID());
            _project.SetC_EnquiryRdate(GetC_EnquiryRdate());
            _project.Set_Value("Status", Get_Value("Status"));
            _project.Set_Value("LeadRating", Get_Value("LeadRating"));

            if (!_project.Save())
                return "@SaveError@";
            //
            if (GetRequest() != null)
            {
                _request.SetC_Project_ID(_project.GetC_Project_ID());
                _request.Save();
            }
            //
            SetC_Project_ID(_project.GetC_Project_ID());
            if (Env.IsModuleInstalled("VA047_"))
            {
                Set_Value("IsArchive", true);
            }
            return null;
        }

        /**
         * 	Create Request from Lead
         * 	@param R_RequestType_ID request type
         *	@return error message
         */
        public String CreateRequest()
        {
            int R_RequestType_ID = GetR_RequestType_ID();
            if (R_RequestType_ID == 0)
                return "@NotFound@: @R_RequestType_ID@ (@AD_Client_ID@)";
            return CreateRequest(R_RequestType_ID);
        }

        /**
         * 	Create Request from Lead
         * 	@param R_RequestType_ID request type
         *	@return error message
         */
        private String CreateRequest(int R_RequestType_ID)
        {
            if (GetR_Request_ID() != 0)
                return "@AlreadyExists@: @R_Request_ID@ (ID=" + GetR_Request_ID() + ")";
            if (GetC_BPartner_ID() == 0)
            {
                String retValue = CreateBP();
                if (retValue != null)
                    return retValue;
            }
            _request = new MRequest(GetCtx(), 0, Get_TrxName());
            _request.SetAD_Org_ID(GetAD_Org_ID());
            String summary = GetName();
            if (summary == null)
                summary = GetHelp();
            if (summary == null)
                summary = GetSummary();
            if (summary == null)
                summary = GetDescription();
            _request.SetSummary(summary);
            //
            _request.SetR_RequestType_ID(R_RequestType_ID);
            if (IsR_Status_IDValid(GetR_Status_ID()))
                _request.SetR_Status_ID(GetR_Status_ID());
            else
                _request.SetR_Status_ID();
            //
            _request.SetC_Lead_ID(GetC_Lead_ID());
            //
            _request.SetC_BPartner_ID(GetC_BPartner_ID());
            _request.SetAD_User_ID(GetAD_User_ID());
            _request.SetC_Project_ID(GetC_Project_ID());
            _request.SetC_Campaign_ID(GetC_Campaign_ID());
            _request.SetR_Source_ID(GetR_Source_ID());
            _request.SetC_BPartnerSR_ID(GetC_BPartnerSR_ID());
            _request.SetC_SalesRegion_ID(GetC_SalesRegion_ID());

            _request.SetSalesRep_ID(GetSalesRep_ID());
            if (!_request.Save())
                return "@SaveError@";
            //
            SetR_Request_ID(_request.GetR_Request_ID());
            return null;
        }

        /// <summary>
        /// Parameters for a duplicate-lead lookup, scoped to this lead's tenant
        /// </summary>
        private System.Data.SqlClient.SqlParameter[] GetDuplicateParams(string name, string value)
        {
            return new System.Data.SqlClient.SqlParameter[]
            {
                new System.Data.SqlClient.SqlParameter("@clientId", GetAD_Client_ID()),
                new System.Data.SqlClient.SqlParameter(name, value),
                new System.Data.SqlClient.SqlParameter("@leadId", GetC_Lead_ID())
            };
        }

        /**
         * 	Before Save
         *	@param newRecord new
         *	@return true
         */
        protected override bool BeforeSave(bool newRecord)
        {
            if (!newRecord && Is_ValueChanged("EMail"))
            {
                SetAD_User_ID(0);
                SetC_BPartner_ID(0);
            }
            //	EMail Address specified
            if (GetEMail() != null && GetAD_User_ID() == 0)
                SetAD_User_ID();

            if (newRecord || Is_ValueChanged("R_Status_ID"))
            {
                if (!IsR_Status_IDValid(GetR_Status_ID()))
                    SetR_Status_ID(0);
                else if (_Status != null)
                    SetProcessed(_Status.IsClosed());
            }

            // Added work from SOTC module.
            if (Env.IsModuleInstalled("VA047_"))
            {
                // VIS0060: When Leads submited from partner portal, Set Lead source as Partner and Lead Name as company name.
                if (newRecord && Util.GetValueOfInt(Get_Value("VA047_Partner_Sales_Rep")) > 0)
                {
                    SetName(GetBPName());
                    SetR_Source_ID(Util.GetValueOfInt(DB.ExecuteScalar("SELECT R_Source_ID FROM R_Source WHERE Export_ID='VIS_1000008'")));
                }

                int count = 0;
                StringBuilder sql = new StringBuilder();
                bool email = false;
                bool mobile = false;
                bool phone = false;
                if (!string.IsNullOrEmpty(GetEMail()))
                {
                    sql.Append("SELECT COUNT(C_Lead_ID) FROM C_LEAD WHERE IsActive = 'Y' AND AD_Client_ID = @clientId AND LOWER(Email) = @email AND C_Lead_ID != @leadId AND IsArchive != 'Y'");
                    count = Util.GetValueOfInt(DB.ExecuteScalar(sql.ToString(), GetDuplicateParams("@email", GetEMail().ToLower()), Get_Trx()));
                    if (count > 0)
                    {
                        email = true;
                    }
                    sql.Clear();
                }
                if (!string.IsNullOrEmpty(GetMobile()))
                {
                    sql.Append("SELECT COUNT(C_Lead_ID) FROM C_LEAD WHERE IsActive = 'Y' AND AD_Client_ID = @clientId AND Mobile = @mobile AND C_Lead_ID != @leadId AND IsArchive != 'Y'");
                    count = Util.GetValueOfInt(DB.ExecuteScalar(sql.ToString(), GetDuplicateParams("@mobile", GetMobile()), Get_Trx()));
                    if (count > 0)
                    {
                        mobile = true;
                    }

                    sql.Clear();
                }
                if (!string.IsNullOrEmpty(GetPhone()))
                {
                    sql.Append("SELECT COUNT(C_Lead_ID) FROM C_LEAD WHERE IsActive = 'Y' AND AD_Client_ID = @clientId AND Phone = @phone AND C_Lead_ID != @leadId AND IsArchive != 'Y'");
                    count = Util.GetValueOfInt(DB.ExecuteScalar(sql.ToString(), GetDuplicateParams("@phone", GetPhone()), Get_Trx()));
                    if (count > 0)
                    {
                        phone = true;
                    }
                }
                if (email && mobile && phone)
                {
                    log.SaveInfo("VA047_AllExists", "");
                }
                else if (email && mobile)
                {
                    log.SaveInfo("VA047_EmailMobileExists", "");
                }
                else if (mobile && phone)
                {
                    log.SaveInfo("VA047_MobilePhoneExists", "");
                }
                else if (email && phone)
                {
                    log.SaveInfo("VA047_EmailPhoneExists", "");
                }
                else if (email)
                {
                    log.SaveInfo("VA047_EmailExists", "");

                }
                else if (mobile)
                {
                    log.SaveInfo("VA047_MobileExists", "");
                }
                else if (phone)
                {
                    log.SaveInfo("VA047_PhoneExists", "");
                }
            }
            //VAI052 30-Oct VAS_LeadNextStep_ID validation is not required as this column is not mandetory on window as discussed with Siddharth and Atul sir.
            //if (Get_ColumnIndex("VAS_LeadNextStep_ID") >= 0 && Util.GetValueOfInt(Get_Value("VAS_LeadNextStep_ID")) <= 0)
            //{
            //    log.SaveWarning("VA061_NextStepMustHaveValue", "");
            //    return false;
            //}
            // If NextStepBy value not provided or neither NextStepBy nor C_Followupdate has changed, no task will be created.
            if (Env.IsModuleInstalled("VA061_"))
            {
                int wf_ID = Util.GetValueOfInt(DB.ExecuteScalar("SELECT AD_Workflow_ID FROM AD_Workflow WHERE WorkflowType='V' AND IsActive='Y' AND AD_Table_ID="
                    + Get_Table_ID(), null, Get_Trx()));

                if (wf_ID > 0 && Get_Value("VA061_NextStepBy") != null && (Is_ValueChanged("VA061_NextStepBy") || Is_ValueChanged("C_Followupdate")))
                {
                    // Ensure that VA061_NextStep has value
                    //if (string.IsNullOrEmpty(Util.GetValueOfString(Get_Value("VA061_NextStep"))))
                    //{
                    //    log.SaveWarning("VA061_NextStepMustHaveValue", "");
                    //    return false;
                    //}

                    // Ensure that FollowUp On has value
                    if (string.IsNullOrEmpty(Util.GetValueOfString(GetC_Followupdate())))
                    {
                        log.SaveWarning("VA061_FollowupdateMustHaveValue", "");
                        return false;
                    }

                    // Set Value in this field to true, for task generation on workflow process
                    Set_Value("VA061_IsCreateTask", true);
                }
                else
                {
                    Set_Value("VA061_IsCreateTask", false);
                }
            }
            return true;
        }

        /**
         * 	After Save
         *	@param newRecord new
         *	@param success success
         *	@returnsuccess
         */
        protected override bool AfterSave(bool newRecord, bool success)
        {
            if (!success)
                return success;

            //	Create Contact Interest
            if (GetAD_User_ID() != 0 && GetR_InterestArea_ID() != 0
                && (Is_ValueChanged("AD_User_ID") || Is_ValueChanged("R_InterestArea_ID")))
            {
                MContactInterest ci = MContactInterest.Get(GetCtx(),
                    GetR_InterestArea_ID(), GetAD_User_ID(),
                    true, Get_TrxName());
                ci.Save();		//	don't subscribe or re-activate
            }
            return true;
        }



    }
}
