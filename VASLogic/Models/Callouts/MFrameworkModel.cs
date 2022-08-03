using System;
using System.Collections.Generic;
using System.Data;
using System.Linq;
using System.Web;
using VAdvantage.Model;
using VAdvantage.Utility;
using VIS.DBase;

namespace VIS.Models
{
    public class MFrameworkModel
    {
        /// <summary>
        /// This method used to Update Group By Check
        /// </summary>
        /// <param name="ctx">context</param>
        /// <param name="fields"></param>
        /// <returns></returns>
        public int UpdateGroupByChecked(Ctx ctx, string fields)
        {
            string[] paramValue = fields.Split(',');
            string sql = @"UPDATE RC_ViewColumn SET IsGroupBy = 'N' WHERE RC_View_ID = " + paramValue[0] + " AND RC_ViewColumn_ID NOT IN(" + paramValue[1] + ")";
            return DB.ExecuteQuery(sql, null, null);
        }
        /// <summary>
        ///This method used to Get Workflow Type
        /// <param name="ctx">context</param>
        /// </summary>
        /// <returns>Ad_Table id</returns>
        public int GetWorkflowType(Ctx ctx)
        {

            string sql = @"SELECT AD_Table_ID FROM AD_Table WHERE IsActive='Y' AND TableName= 'VADMS_MetaData'";
            return Util.GetValueOfInt(DB.ExecuteScalar(sql, null, null));
        }
        /// <summary>
        ///This method used to Get Workflow Type
        /// <param name="ctx">context</param>
        /// <param name="fields"></param>
        /// </summary>
        /// <returns>Ad_Table id</returns>
        public string GetIsGenericAttribute(Ctx ctx, string fields)
        {

            string sql = @"SELECT ColumnName FROM AD_Column WHERE AD_Column_ID=" + Util.GetValueOfInt(fields);
            return Util.GetValueOfString(DB.ExecuteScalar(sql, null, null));
        }
    }
}
