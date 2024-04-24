/*******************************************************
    * Module Name    : Standard module
    * Purpose        : Term Model
    * Chronological  : Development
    * VAI094         : 10/4/2024
******************************************************/
using System;
using System.Collections.Generic;
using System.Data;
using System.Linq;
using System.Web;
using VAdvantage.DataBase;
using VAdvantage.Model;
using VAdvantage.Utility;

namespace VAS.Models
{
    public class MTermModel
    {
        /// <summary>
        /// to get term description data for term description field in 
        /// term assignment tab in terms  window  from term details
        /// field in term master window
        /// </summary>
        /// <param name="fields"></param>
        /// <returns>term description</returns>
        public string GetTermDescription(string fields)
        {
            return Util.GetValueOfString(DB.ExecuteScalar("SELECT VAS_TermDetails FROM VAS_TermsMaster WHERE VAS_TermsMaster_ID="
                + Util.GetValueOfInt(fields), null, null));
        }
    }
}