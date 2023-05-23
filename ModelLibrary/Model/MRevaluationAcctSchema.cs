using System;
using System.Collections.Generic;
using System.Data;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using VAdvantage.DataBase;
using VAdvantage.Logging;
using VAdvantage.Model;
using VAdvantage.Utility;

namespace ModelLibrary.Model
{
    class MRevaluationAcctSchema : X_M_RevaluationAcctSchema
    {
        /// <summary>
        /// Standard Constructor
        /// </summary>
        /// <param name="ctx">context</param>
        /// <param name="M_RevaluationAcctSchema_ID">id</param>
        /// <param name="trxName">transaction</param>
        public MRevaluationAcctSchema(Ctx ctx, int M_RevaluationAcctSchema_ID, Trx trxName)
           : base(ctx, M_RevaluationAcctSchema_ID, trxName)
        {

        }

        /// <summary>
        /// Load Constructor
        /// </summary>
        /// <param name="ctx">context</param>
        /// <param name="dr">data row</param>
        /// <param name="trxName">transaction</param>
        public MRevaluationAcctSchema(Ctx ctx, DataRow dr, Trx trxName)
            : base(ctx, dr, trxName)
        {
        }

    }
}
