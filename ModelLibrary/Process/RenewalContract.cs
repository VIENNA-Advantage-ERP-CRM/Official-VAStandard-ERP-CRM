/********************************************************
    * Project Name   : Vienna Standard
    * Class Name     : RenewalContract
    * Purpose        : Create New Contract with reference 
    *                  of old contract
    * Class Used     : ProcessEngine.SvrProcess
    * Chronological  : Development
    * Manjot         : 30/JAN/2023
******************************************************/
using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using VAdvantage.Classes;
using VAdvantage.Common;
using VAdvantage.Process;
using VAdvantage.Model;
using VAdvantage.DataBase;
using VAdvantage.SqlExec;
using VAdvantage.Utility;
using System.Windows.Forms;
using System.Data;
using System.Data.SqlClient;
using VAdvantage.ProcessEngine;
using VAdvantage.Logging;

namespace VAdvantage.Process
{

    public class RenewalContract : SvrProcess
    {
        # region    private varriables 
        private ValueNamePair pp = null;
        private string rMsg = "";
        static VLogger log = VLogger.GetVLogger("RenewalContract");
        DateTime? StartDate;
        DateTime? EndDate;
        #endregion

        protected override void Prepare()
        {
           
        }
        protected override string DoIt()
        {

            int C_OldContract_ID = GetRecord_ID();
            MVASContractMaster _oldCont = new MVASContractMaster(GetCtx(),
                                C_OldContract_ID, Get_Trx());
            MVASContractMaster _newCont = new MVASContractMaster(GetCtx(),
                                        0, Get_Trx());
           
            if (_oldCont.GetVAS_RenewalDate() == null)
            {
                return Msg.GetMsg(GetCtx(), "VAS_CheckRenewalDate");      //VAI050--Check renewal date not null
            }

            //VIS0336:Set start date as Renewal date and End date= renewal date+ duration month + year, for Renewal document.
            StartDate = _oldCont.GetVAS_RenewalDate();
            EndDate = _oldCont.GetVAS_RenewalDate().Value.AddYears(Util.GetValueOfInt(_oldCont.GetVAS_ContractDuration())).
                       AddMonths(Util.GetValueOfInt(_oldCont.GetVAS_ContractMonths()));

            _oldCont.CopyTo(_newCont);
            _newCont.SetAD_Client_ID(GetAD_Client_ID());
            _newCont.SetAD_Org_ID(GetAD_Org_ID());
            _newCont.SetRef_Contract_ID(C_OldContract_ID);
            _newCont.SetBill_Location_ID(_oldCont.GetBill_Location_ID());
            _newCont.SetDateDoc(_oldCont.GetVAS_RenewalDate());
            _newCont.SetStartDate(StartDate);
            _newCont.SetEndDate(EndDate);
            _newCont.SetVAS_RenewalDate(EndDate.Value.AddDays(1));
            _newCont.SetDocumentNo(string.Empty);
            _newCont.SetIsExpiredContracts(false);
            _newCont.Set_Value("Vas_Contractreferral", "Renew"); //VAI050-Set value in Contract Referal field
            _newCont.SetVAS_IsApproved(false); //VAI050-Set value false 
            _newCont.SetVAS_Status("DFT"); //VAI050-Set Drafted in Status field
            _newCont.Set_Value("Processed", false); //VAI050-Set false  for Processed
            _newCont.SetVAS_ContractDuration(Math.Round((decimal.Subtract(EndDate.Value.Year, StartDate.Value.Year) * 12 + decimal.Subtract(EndDate.Value.Month, StartDate.Value.Month)) / 12, 1));
            var monthDiff = (EndDate - StartDate).Value.Days;
            _newCont.SetVAS_ContractMonths(Math.Round((decimal)monthDiff / 30, 1));
            if (!_newCont.Save())
            {
                pp = VLogger.RetrieveError();
                if (pp != null)
                {
                    rMsg = pp.GetName();
                    if (rMsg == "")
                    {
                        rMsg = pp.GetValue();
                    }
                }
                if (rMsg == "")
                {
                    rMsg = Msg.GetMsg(GetCtx(), "VAS_ContNotCopied");
                }
                Get_TrxName().Rollback();
                return rMsg;
            }

            int[] _oldlinesIds = MVASContractLine.GetAllIDs(
                                          X_VAS_ContractLine.Table_Name, " VAS_ContractMaster_ID = " +
                                          GetRecord_ID(), Get_Trx());
            if (_oldlinesIds != null && _oldlinesIds.Length > 0)
            {
                MVASContractLine _newLine = null;
                MVASContractLine _oldLine = null;
                for (int i = 0; i < _oldlinesIds.Length; i++)
                {
                    _oldLine = new MVASContractLine(GetCtx(),
                                        _oldlinesIds[i], Get_Trx());
                    _newLine = new MVASContractLine(GetCtx(),
                                       0, Get_Trx());
                    _oldLine.CopyTo(_newLine);
                    _newLine.SetVAS_ContractMaster_ID(_newCont.GetVAS_ContractMaster_ID());
                    _newLine.SetAD_Client_ID(GetAD_Client_ID());
                    _newLine.SetAD_Org_ID(GetAD_Org_ID());
                    if (!_newLine.Save())
                    {
                        pp = VLogger.RetrieveError();
                        if (pp != null)
                        {
                            rMsg = pp.GetName();
                            if (rMsg == "")
                            {
                                rMsg = pp.GetValue();
                            }
                        }
                        if (rMsg == "")
                        {
                            rMsg = Msg.GetMsg(GetCtx(), "VAS_LineNotCopied");
                        }
                        Get_TrxName().Rollback();
                        return rMsg;
                    }
                }
            }

            _oldlinesIds = MVASContractOwner.GetAllIDs(
                                          X_VAS_ContractOwner.Table_Name, " VAS_ContractMaster_ID = " +
                                          GetRecord_ID(), Get_Trx());
            if (_oldlinesIds != null && _oldlinesIds.Length > 0)
            {
                MVASContractOwner _newCLine = null;
                MVASContractOwner _oldCLine = null;
                for (int i = 0; i < _oldlinesIds.Length; i++)
                {
                    _oldCLine = new MVASContractOwner(GetCtx(),
                                        _oldlinesIds[i], Get_Trx());
                    _newCLine = new MVASContractOwner(GetCtx(),
                                       0, Get_Trx());
                    _oldCLine.CopyTo(_newCLine);
                    _newCLine.SetVAS_ContractMaster_ID(_newCont.GetVAS_ContractMaster_ID());
                    _newCLine.SetAD_Client_ID(GetAD_Client_ID());
                    _newCLine.SetAD_Org_ID(GetAD_Org_ID());
                    if (!_newCLine.Save())
                    {
                        pp = VLogger.RetrieveError();
                        if (pp != null)
                        {
                            rMsg = pp.GetName();
                            if (rMsg == "")
                            {
                                rMsg = pp.GetValue();
                            }
                        }
                        if (rMsg == "")
                        {
                            rMsg = Msg.GetMsg(GetCtx(), "VAS_LineNotCopied");
                        }
                        Get_TrxName().Rollback();
                        return rMsg;
                    }
                }
            }

            _oldlinesIds = MVASContractTerms.GetAllIDs(
                                          X_VAS_ContractTerms.Table_Name, " VAS_ContractMaster_ID = " +
                                          GetRecord_ID(), Get_Trx());
            if (_oldlinesIds != null && _oldlinesIds.Length > 0)
            {
                MVASContractTerms _newTLine = null;
                MVASContractTerms _oldTLine = null;
                for (int i = 0; i < _oldlinesIds.Length; i++)
                {
                    _oldTLine = new MVASContractTerms(GetCtx(),
                                        _oldlinesIds[i], Get_Trx());
                    _newTLine = new MVASContractTerms(GetCtx(),
                                       0, Get_Trx());
                    _oldTLine.CopyTo(_newTLine);
                    _newTLine.SetVAS_ContractMaster_ID(_newCont.GetVAS_ContractMaster_ID());
                    _newTLine.SetAD_Client_ID(GetAD_Client_ID());
                    _newTLine.SetAD_Org_ID(GetAD_Org_ID());
                    if (!_newTLine.Save())
                    {
                        pp = VLogger.RetrieveError();
                        if (pp != null)
                        {
                            rMsg = pp.GetName();
                            if (rMsg == "")
                            {
                                rMsg = pp.GetValue();
                            }
                        }
                        if (rMsg == "")
                        {
                            rMsg = Msg.GetMsg(GetCtx(), "VAS_LineNotCopied");
                        }
                        Get_TrxName().Rollback();
                        return rMsg;
                    }
                }
            }
            if (string.IsNullOrEmpty(rMsg))
            {
                //VAI050-To set VAS_IsRenewable true in existing contract when contract renewed
                DB.ExecuteQuery("UPDATE VAS_ContractMaster SET VAS_Isrenewable='Y', Processed='Y' WHERE VAS_ContractMaster_ID=" + GetRecord_ID(), null, Get_Trx());
                rMsg = Msg.GetMsg(GetCtx(), "VAS_ContractRenewed") + _newCont.GetDocumentNo();
            }

            return rMsg;
        }


    }
}
