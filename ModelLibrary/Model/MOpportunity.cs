using System;
using System.Collections.Generic;
using System.Data;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using VAdvantage.Classes;
using VAdvantage.DataBase;
using VAdvantage.Utility;
using ViennaAdvantage.Model;

namespace ModelLibrary.Model
{
    internal class MOpportunity : X_VAS_Opportunity
    {
        public MOpportunity(Ctx ctx, int VAS_Opportunity_ID, Trx trxName) : base(ctx, VAS_Opportunity_ID, trxName)
        {
        }


        public MOpportunity(Ctx ctx, DataRow rs, Trx trxName) : base(ctx, rs, trxName)
        {
        }

    }
}
