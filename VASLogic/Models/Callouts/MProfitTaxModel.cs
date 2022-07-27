/********************************************************
 * Project Name   :    VIS
 * Class Name     :    MProfitTax
 * Purpose        :    Used for Income Tax callout
 * Chronological       Development
 * VIS_0045            20/07/2022
  ******************************************************/

using CoreLibrary.DataBase;
using System;
using System.Collections.Generic;
using System.Data;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using VAdvantage.Utility;

namespace VIS.Models
{
    public class MProfitTaxModel
    {
        /// <summary>
        /// Get value of C_Year_ID, C_ProfitAndLoss_ID, ProfitBeforeTax
        /// </summary>
        /// <param name="ctx">Context</param>
        /// <param name="fields">C_ProfitLoss_ID</param>
        /// <returns>ProfitLossDetails</returns>
        public Dictionary<String, object> GetProfitLossDetails(Ctx ctx, string fields)
        {
            string[] paramValue = fields.Split(',');
            Dictionary<String, object> retDic = new Dictionary<string, object>();
            string sql = "SELECT ProfitBeforeTax,C_Year_ID,C_ProfitAndLoss_ID FROM C_ProfitLoss WHERE C_ProfitLoss_ID=" + Util.GetValueOfInt(fields);
            DataSet ds = DB.ExecuteDataset(sql, null, null);
            if (ds != null && ds.Tables[0].Rows.Count > 0)
            {
                retDic["ProfitBeforeTax"] = Util.GetValueOfDecimal(ds.Tables[0].Rows[0]["ProfitBeforeTax"]);
                retDic["C_Year_ID"] = Util.GetValueOfInt(ds.Tables[0].Rows[0]["C_Year_ID"]);
                retDic["C_ProfitAndLoss_ID"] = Util.GetValueOfInt(ds.Tables[0].Rows[0]["C_ProfitAndLoss_ID"]);
            }
            return retDic;
        }
    }
}
