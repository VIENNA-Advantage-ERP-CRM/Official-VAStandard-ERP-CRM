﻿/********************************************************
    * Project Name   : Vienna Standard
    * Class Name     : MVASContractMaster
    * Chronological  : Development
    * Manjot         : 07/FEB/2023
******************************************************/
using System;
using System.Collections.Generic;
using System.Data;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using VAdvantage.DataBase;
using VAdvantage.Utility;

namespace VAdvantage.Model
{
    class MVASContractMaster : X_VAS_ContractMaster
    {
        public MVASContractMaster(Ctx ctx, int VAS_ContractMaster_ID, Trx Trx_Name)
           : base(ctx, VAS_ContractMaster_ID, Trx_Name)
        {
        }
        public MVASContractMaster(Ctx ctx, DataRow dr, Trx trx)
            : base(ctx, dr, trx)
        {
        }

        //VIS 404 check for End date not greater than startdate date on Contract Master window

        protected override bool BeforeSave(bool newRecord)
        {

            //VIS0336:Restrict the user not to change the below fields if contract line exists for this contract.
            //VAI050-Restrict the user not to change the StartDate and End Date if contract line exists for this contract.
            if (!newRecord && (Is_ValueChanged("ContractType") || Is_ValueChanged("C_BPartner_ID") || Is_ValueChanged("C_PaymentTerm_ID") || Is_ValueChanged("M_PriceList_ID") || Is_ValueChanged("VA009_PaymentMethod_ID") || Is_ValueChanged("StartDate") || Is_ValueChanged("EndDate")))//VIS430:When transactionline available for Contract refrence on header show error message
            {

                string sql = "SELECT COUNT(VAS_ContractMaster_ID) FROM VAS_ContractLine WHERE VAS_ContractMaster_ID = " + GetVAS_ContractMaster_ID() + " AND IsActive = 'Y'";
                if (Util.GetValueOfInt(DB.ExecuteScalar(sql, null, Get_Trx())) > 0)
                {
                    log.SaveError("", Msg.GetMsg(GetCtx(), "VAS_DeleteLines"));
                    return false;
                }
            }
            //VAI050-If transaction available for contract, then system should not allow to edit any information on the header of contract
            if (!newRecord && !Is_ValueChanged("VAS_TerminationReason") && !Is_ValueChanged("VAS_TerminationDate") && !Is_ValueChanged("VAS_Terminate"))
            {
                string query = "SELECT a.OrderId , b.InvoiceId  FROM ( SELECT COUNT(VAS_ContractMaster_ID) AS OrderId FROM C_Order " +
                     "WHERE DocAction NOT IN ('VO','RC') AND VAS_ContractMaster_ID=" + GetVAS_ContractMaster_ID() + " AND IsBlanketTrx!='Y' )  a,   ( SELECT COUNT(VAS_ContractMaster_ID) AS InvoiceId " +
                     " FROM C_Invoice WHERE DocAction NOT IN ('VO','RC') AND VAS_ContractMaster_ID= " + GetVAS_ContractMaster_ID() + " )  b";
                DataSet ds = DB.ExecuteDataset(query, null, Get_Trx());
                if (ds != null && ds.Tables.Count > 0 && ds.Tables[0].Rows.Count > 0)
                {
                    if (Util.GetValueOfInt(ds.Tables[0].Rows[0]["OrderId"]) > 0 || Util.GetValueOfInt(ds.Tables[0].Rows[0]["InvoiceId"]) > 0)
                    {
                        log.SaveError("", Msg.GetMsg(GetCtx(), "VAS_CheckOrder"));
                        return false;
                    }
                }

            }

            //VAI050-Should not allow to change any details of termination if contract is already terminated
            if (!newRecord && (Is_ValueChanged("VAS_TerminationReason") || Is_ValueChanged("VAS_TerminationDate") || Is_ValueChanged("VAS_Terminate")))
            {
                if (Util.GetValueOfBool(Get_ValueOld("VAS_Terminate")))
                {
                    log.SaveError("", Msg.GetMsg(GetCtx(), "VAS_TerminateStatus"));
                    return false;
                }
                //VIS0336:Implement check for restricting the user to enter termination date less than contract start date and end date.
                if (GetVAS_TerminationDate() < GetStartDate() || GetVAS_TerminationDate() < GetDateDoc())
                {
                    log.SaveError("", Msg.GetMsg(GetCtx(), "VAS_TerminationMustGreaters"));
                    return false;
                }

            }

            if (Util.GetValueOfDateTime(GetStartDate()) > Util.GetValueOfDateTime(GetEndDate()))
            {
                log.SaveError("", Msg.GetMsg(GetCtx(), "VAS_EndDateMustGreater"));
                return false;
            }

            // VIS0060: Check Vendor for Blacklisting and Suspension for the particuler period if Vendor Mgt module is installed.
            if (Env.IsModuleInstalled("VA068_") && (newRecord || Is_ValueChanged("DateDoc") || Is_ValueChanged("C_BPartner_ID")))
            {
                int blkSpn = Util.GetValueOfInt(DB.ExecuteScalar(@"SELECT COUNT(C_BPartner_ID)
                            FROM VA068_VendorBlacklistingSuspen WHERE C_BPartner_ID = " + GetC_BPartner_ID() +
                        " AND (VA068_FinalIndefiniteBlacklisting = 'Y' OR VA068_FinalEndingDate > "
                        + GlobalVariable.TO_DATE(GetDateDoc(), true) + ")", null, Get_Trx()));

                if (blkSpn > 0)
                {
                    log.SaveError("", Msg.GetMsg(GetCtx(), "VA068_VendorBlkSpn"));
                    return false;
                }
            }

            // VIS430: Validation popup for renewal date, renewal type, no of cycle, notice days and termination date
            // VAI050:Remove Manual Renewal Type check 
            if (GetRenewalType() == "ATC")
            {
                if (GetVAS_RenewalDate() == null)
                {
                    log.SaveError("", Msg.GetMsg(GetCtx(), "VAS_RenewalMustFilled"));
                    return false;
                }
                if (GetCycles() == 0 && GetRenewalType() == "ATC")
                {
                    log.SaveError("", Msg.GetMsg(GetCtx(), "VAS_CyclesMustFilled"));
                    return false;
                }
                else if (GetCancelBeforeDays() == 0 && GetRenewalType() == "ATC")
                {
                    log.SaveError("", Msg.GetMsg(GetCtx(), "VAS_CancelBeforeDaysMustFilled"));
                    return false;

                }
            }
            if (IsVAS_Terminate() && GetVAS_TerminationDate() == null)
            {
                log.SaveError("", Msg.GetMsg(GetCtx(), "VAS_TerminateMustFilled"));
                return false;
            }
            if (IsVAS_Terminate())
            {
                SetVAS_Status(VAS_STATUS_Terminated);
            }
            return true;
        }

    }
}
