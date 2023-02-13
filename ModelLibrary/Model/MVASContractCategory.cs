/********************************************************
    * Project Name   : Vienna Standard
    * Class Name     : MVASContractCategory
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
    class MVASContractCategory : X_VAS_ContractCategory
    {
        public MVASContractCategory(Ctx ctx, int VAS_ContractCategory_ID, Trx Trx_Name)
           : base(ctx, VAS_ContractCategory_ID, Trx_Name)
        {
        }
        public MVASContractCategory(Ctx ctx, DataRow dr, Trx trx)
            : base(ctx, dr, trx)
        {
        }
    }
}
