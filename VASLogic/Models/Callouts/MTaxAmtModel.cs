using CoreLibrary.DataBase;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using VAdvantage.Utility;

namespace VASLogic.Models.Callouts
{
    /// <summary>
    /// Declare class
    /// </summary>
   public class MTaxAmtModel
    {
        /// <summary>
        /// Getting Rate From Tax Table
        /// </summary>
        /// <param name="Id"></param>
        /// <returns></returns>
        public Decimal? GetRate(int Id)
        {
            Decimal? rate;
            string sql = "select Rate from C_Tax where C_Tax_ID = " + Id+"";
            rate = Util.GetValueOfDecimal(DB.ExecuteScalar(sql, null, null));
            return rate;

        }
        /// <summary>
        /// Getting Expense Amount
        /// </summary>
        /// <param name="Id"></param>
        /// <returns></returns>
        public Decimal? GetExpAmt(int Id)
        {
            Decimal? ExpAmt;
            var sql= "select Rate from C_Tax where C_Tax_ID = " +Id+"";
            ExpAmt = Util.GetValueOfDecimal(DB.ExecuteScalar(sql, null, null));
            return ExpAmt;
        }

    }
}
