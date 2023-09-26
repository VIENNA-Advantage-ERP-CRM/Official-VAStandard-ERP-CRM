/********************************************************
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
            
            // VIS0060: Set Contract Status as Terminated when contract is marked as Terminated.
            if (IsVAS_Terminate())
            {
                SetVAS_Status(VAS_STATUS_Terminated);
            }
            return true;
        }
    }
}
