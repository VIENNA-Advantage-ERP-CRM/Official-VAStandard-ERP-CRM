﻿using CoreLibrary.DataBase;
using System;
using System.Collections.Generic;
using System.Data;
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
        /// Getting Currency ID
        /// </summary>
        /// <param name="ID"></param>
        /// <returns></returns>
        public int GetCurrenyID(int M_Pricelist_ID)
        {
           string sql = "select C_Currency_id from m_pricelist where m_pricelist_ID = " + M_Pricelist_ID + "";
           return Util.GetValueOfInt( DB.ExecuteScalar(sql, null, null));
        }
    }
    
}