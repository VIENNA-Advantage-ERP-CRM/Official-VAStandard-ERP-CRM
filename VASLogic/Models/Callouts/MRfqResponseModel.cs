/********************************************************
 * Project Name   : VAdvantage
 * Class Name     : MRfQResponseModel.cs
 * Purpose        :for fetch quantity.
 * Chronological    Development
 * Priyanka Sharma     15-Dec-2023
  ******************************************************/

using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using VAdvantage.DataBase;
using VAdvantage.Utility;

namespace VIS.Models
{
    public class MRfqResponseModel
    {
        /// <summary>
        ///  VAI051: changes done for fetching the qty from C_RfQLineQty
        /// </summary>
        /// <param name="ctx">Context</param>
        /// <param name="fields></param>
        /// <returns></returns>
        public int GetPriceDetail(Ctx ctx, string fields)
        {
            string query = "SELECT Qty FROM C_RfQLineQty WHERE C_RfQLineQty_ID= " + fields;
            int quantity = Util.GetValueOfInt(DB.ExecuteScalar(query));
            return quantity;

        }
    }

}
