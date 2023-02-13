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
        private string errorMsg = "";
        static VLogger log = VLogger.GetVLogger("RenewalContract");
        DateTime? StartDate;
        DateTime? EndDate;
        #endregion
        protected override string DoIt()
        {
            int C_OldContract_ID = GetRecord_ID();
            MVASContractMaster _oldCont = new MVASContractMaster(GetCtx(),
                                C_OldContract_ID, Get_Trx());
            MVASContractMaster _newCont = new MVASContractMaster(GetCtx(),
                                        0, Get_Trx());
            _oldCont.CopyTo(_newCont);
            _newCont.SetAD_Client_ID(GetAD_Client_ID());
            _newCont.SetAD_Org_ID(GetAD_Org_ID());
            _newCont.SetRef_Contract_ID(C_OldContract_ID);
            _newCont.SetBill_Location_ID(_oldCont.GetBill_Location_ID());
            _newCont.SetStartDate(StartDate);
            _newCont.SetEndDate(EndDate);
            _newCont.SetVAS_ContractDuration(Util.GetValueOfString(EndDate.Value.Year - StartDate.Value.Year));
            _newCont.SetVAS_ContractMonths(Util.GetValueOfString(
                decimal.Negate(EndDate.Value.Month - StartDate.Value.Month)));
            if (!_newCont.Save())
            {
                pp = VLogger.RetrieveError();
                if (pp != null)
                {
                    errorMsg = pp.GetName();
                    if (errorMsg == "")
                    {
                        errorMsg = pp.GetValue();
                    }
                }
                if (errorMsg == "")
                {
                    errorMsg = Msg.GetMsg(GetCtx(), "VAS_ContNotCopied");
                }
                Get_TrxName().Rollback();
                return errorMsg;
            }

            int[] _oldlinesIds = MVASContractLine.GetAllIDs(
                                          X_VAS_ContractLine.Table_Name, " VAS_ContractMaster_ID = " +
                                          GetRecord_ID(), Get_Trx());
            if (_oldlinesIds.Length > 0)
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
                            errorMsg = pp.GetName();
                            if (errorMsg == "")
                            {
                                errorMsg = pp.GetValue();
                            }
                        }
                        if (errorMsg == "")
                        {
                            errorMsg = Msg.GetMsg(GetCtx(), "VAS_LineNotCopied");
                        }
                        Get_TrxName().Rollback();
                        return errorMsg;
                    }
                }
            }

            _oldlinesIds = MVASContractOwner.GetAllIDs(
                                          X_VAS_ContractOwner.Table_Name, " VAS_ContractMaster_ID = " +
                                          GetRecord_ID(), Get_Trx());
            if (_oldlinesIds.Length > 0)
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
                            errorMsg = pp.GetName();
                            if (errorMsg == "")
                            {
                                errorMsg = pp.GetValue();
                            }
                        }
                        if (errorMsg == "")
                        {
                            errorMsg = Msg.GetMsg(GetCtx(), "VAS_LineNotCopied");
                        }
                        Get_TrxName().Rollback();
                        return errorMsg;
                    }
                }
            }

            _oldlinesIds = MVASContractTerms.GetAllIDs(
                                          X_VAS_ContractTerms.Table_Name, " VAS_ContractMaster_ID = " +
                                          GetRecord_ID(), Get_Trx());
            if (_oldlinesIds.Length > 0)
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
                            errorMsg = pp.GetName();
                            if (errorMsg == "")
                            {
                                errorMsg = pp.GetValue();
                            }
                        }
                        if (errorMsg == "")
                        {
                            errorMsg = Msg.GetMsg(GetCtx(), "VAS_LineNotCopied");
                        }
                        Get_TrxName().Rollback();
                        return errorMsg;
                    }
                }
            }

            return errorMsg;
        }

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
                else if (name.Equals("StartDate"))
                {
                    StartDate = Util.GetValueOfDateTime(para[i].GetParameter()).HasValue ?
                               Util.GetValueOfDateTime(para[i].GetParameter()) : null;
                }
                else if (name.Equals("EndDate"))
                {
                    EndDate = Util.GetValueOfDateTime(para[i].GetParameter()).HasValue ?
                             Util.GetValueOfDateTime(para[i].GetParameter()) : null;
                }
                else
                {
                    log.Log(Level.SEVERE, "Unknown Parameter: " + name);
                }
            }
        }
    }
}
