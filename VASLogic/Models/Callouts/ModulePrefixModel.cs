using System;
using System.Collections.Generic;
using System.Data.SqlClient;
using System.Linq;
using System.Web;
using VAdvantage.DataBase;
using VAdvantage.Model;
using VAdvantage.Utility;

namespace VIS.Models
{
    public class ModulePrefixModel
    {
        /// <summary>
        /// Get Module Existance
        /// </summary>        
        /// <param name="fields"></param>
        /// <returns></returns>
        public Dictionary<String, Boolean> GetModulePrefix(Ctx ctx, string fields)
        {
            string[] paramValue = fields.Split(',');
            Dictionary<String, Boolean> _ModulePrifix = new Dictionary<String, Boolean>();
            SqlParameter[] param = new SqlParameter[1];
            for (int i = 0; i < paramValue.Length; i++)
            {
                param[0] = new SqlParameter("@param1", paramValue[i]);
                if (Util.GetValueOfInt(DB.ExecuteScalar(@"SELECT COUNT(AD_ModuleInfo_ID) FROM AD_ModuleInfo 
                    WHERE Prefix=@param1 AND IsActive = 'Y'", param, null)) > 0)
                {
                    _ModulePrifix[paramValue[i]] = true;
                }
                else
                {
                    _ModulePrifix[paramValue[i]] = false;
                }
            }
            return _ModulePrifix;
        }
    }
}