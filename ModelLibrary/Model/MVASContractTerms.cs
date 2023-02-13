/********************************************************
    * Project Name   : Vienna Standard
    * Class Name     : MVASContractTerms
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
    class MVASContractTerms : X_VAS_ContractTerms
    {
        public MVASContractTerms(Ctx ctx, int VAS_ContractTerms_ID, Trx Trx_Name)
           : base(ctx, VAS_ContractTerms_ID, Trx_Name)
        {
        }
        public MVASContractTerms(Ctx ctx, DataRow dr, Trx trx)
            : base(ctx, dr, trx)
        {
        }
    }
}
