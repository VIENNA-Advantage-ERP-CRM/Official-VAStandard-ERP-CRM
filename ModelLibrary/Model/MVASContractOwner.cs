/********************************************************
    * Project Name   : Vienna Standard
    * Class Name     : MVASContractOwner
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
    class MVASContractOwner : X_VAS_ContractOwner
    {
        public MVASContractOwner(Ctx ctx, int VAS_ContractOwner_ID, Trx Trx_Name)
           : base(ctx, VAS_ContractOwner_ID, Trx_Name)
        {
        }
        public MVASContractOwner(Ctx ctx, DataRow dr, Trx trx)
            : base(ctx, dr, trx)
        {
        }

        /// <summary>
        ///	Before Save
        /// </summary>
        /// <param name="newRecord">new</param>
        /// <returns>true if can be saved</returns>
        protected override Boolean BeforeSave(Boolean newRecord)
        {
            //	Role/User Must be selected
            if (newRecord || Is_ValueChanged("AD_Role_ID") || Is_ValueChanged("AD_User_ID"))
            {
                if (GetAD_Role_ID() == 0 && GetAD_User_ID() == 0)
                {
                    log.SaveError("", "VAS_MsutSelectUserRole");
                    return false;
                }
            }
            return true;
        }

    }
}
