using CoreLibrary.DataBase;
using System;
using System.Collections.Generic;
using System.Data;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using VAdvantage.Utility;

namespace VASLogic.Models.Callouts
{/// <summary>
/// Declare Class
/// </summary>
    public class MProfitTaxModel
    {
        /// <summary>
        /// Get The value of Year,Profit And Loss 
        /// </summary>
        /// <param name="ctx"></param>
        /// <param name="fields"></param>
        /// <returns></returns>
        public Dictionary<String, object> GetBeforeTax(Ctx ctx, string fields)
        {
            string[] paramValue = fields.Split(',');
            Dictionary<String, object> retDic = new Dictionary<string, object>();
            string sql = "SELECT ProfitBeforeTax,C_Year_ID,C_ProfitAndLoss_ID FROM C_ProfitLoss WHERE C_ProfitLoss_ID=" + paramValue[0] + "";
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
