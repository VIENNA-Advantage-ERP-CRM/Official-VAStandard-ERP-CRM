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
            return true;
        }
    }
}
