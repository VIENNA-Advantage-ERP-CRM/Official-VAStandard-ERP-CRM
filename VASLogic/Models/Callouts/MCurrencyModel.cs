using CoreLibrary.DataBase;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Web;
using VAdvantage.Model;
using VAdvantage.Utility;

namespace VIS.Models
{
    public class MCurrencyModel
    {
        /// <summary>
        /// Get Currency Detail
        /// </summary>
        /// <param name="ctx"></param>
        /// <param name="fields"></param>
        /// <returns></returns>
        public Dictionary<string, string> GetCurrency(Ctx ctx, string fields)
        {         
            string[] paramValue = fields.Split(',');                
            //Assign parameter value
            int C_Currency_ID = Util.GetValueOfInt(paramValue[0].ToString());
            //End Assign parameter
            MCurrency currency = MCurrency.Get(ctx, C_Currency_ID);
            Dictionary<string, string> result = new Dictionary<string, string>();
            result["StdPrecision"] = currency.GetStdPrecision().ToString();
            return result;
        }
        /// <summary>
        /// 04/10/2023 Bud ID:2488:- This method returns Precision of currency present on bank
        /// </summary>
        /// <author>VIS_427</author>
        /// <param name="ctx">Context</param>
        /// <param name="C_BankAccount_ID"> Contain value of C_BankAccount_ID</param>
        /// <returns>Precision of currency</returns>

        public Dictionary<string, string> GetBankCurrencyPrecision(Ctx ctx, string fields)
        {
            int stdPrecision = Util.GetValueOfInt(DB.ExecuteScalar(@"SELECT StdPrecision FROM C_Currency WHERE C_Currency_ID=
                (SELECT C_Currency_ID FROM C_BankAccount WHERE C_BankAccount_ID = " + Util.GetValueOfInt(fields) + ")"));
            Dictionary<string, string> result = new Dictionary<string, string>();
            result["StdPrecision"] = stdPrecision.ToString();
            return result;
        }
    }
}