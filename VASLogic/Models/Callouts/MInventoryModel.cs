using System;
using System.Collections.Generic;
using System.Linq;
using System.Web;
using VAdvantage.DataBase;
using VAdvantage.Model;
using VAdvantage.Utility;

namespace VIS.Models
{
    public class MInventoryModel
    {
        /// <summary>
        /// GetMInventory
        /// </summary>
        /// <param name="ctx"></param>
        /// <param name="fields"></param>
        /// <returns></returns>
        public Dictionary<string, string> GetMInventory(Ctx ctx, string fields)
        {
            string[] paramValue = fields.Split(',');
            int M_Inventory_ID;

            //Assign parameter value
            M_Inventory_ID = Util.GetValueOfInt(paramValue[0].ToString());
            MInventory inv = new MInventory(ctx, M_Inventory_ID, null);
            DateTime? MovementDate = inv.GetMovementDate();
            int AD_Org_ID = inv.GetAD_Org_ID();

            Dictionary<string, string> retDic = new Dictionary<string, string>();
            retDic["MovementDate"] = MovementDate.ToString();
            retDic["AD_Org_ID"] = AD_Org_ID.ToString();
            return retDic;
        }

        /// <summary>
        /// Get Current Qty from Product Transaction
        /// </summary>
        /// <param name="ctx">ctx</param>
        /// <param name="fields">fields</param>
        /// <returns>Current qty</returns>
        public decimal GetCurrentQty(Ctx ctx, string fields)
        {
            string[] paramValue = fields.Split(',');
            bool isContainerApplicable = Util.GetValueOfBool(paramValue[0].ToString());
            DateTime? tsDate = Util.GetValueOfDateTime(paramValue[1].ToString());
            int M_Product_ID = Util.GetValueOfInt(paramValue[2].ToString());
            int M_Locator_ID = Util.GetValueOfInt(paramValue[3].ToString());
            int M_AttributeSetInstance_ID = Util.GetValueOfInt(paramValue[4].ToString());
            int M_ProductContainer_ID = Util.GetValueOfInt(paramValue[5].ToString());
            int AD_Org_ID = Util.GetValueOfInt(paramValue[6].ToString());
            string sql = string.Empty;
            decimal currentqty = 0;

            if (isContainerApplicable)
            {
                sql = @"SELECT DISTINCT FIRST_VALUE(t.ContainerCurrentQty) OVER (PARTITION BY t.M_Product_ID, t.M_AttributeSetInstance_ID ORDER BY t.MovementDate DESC, t.M_Transaction_ID DESC) AS ContainerCurrentQty
                           FROM M_Transaction t
                           WHERE t.MovementDate <=" + GlobalVariable.TO_DATE(tsDate, true) + @" 
                           AND t.M_Locator_ID                       = " + M_Locator_ID + @"
                           AND t.M_Product_ID                       = " + M_Product_ID + @"
                           AND NVL(t.M_AttributeSetInstance_ID , 0) = COALESCE(" + M_AttributeSetInstance_ID + @",0)
                           AND NVL(t.M_ProductContainer_ID, 0)              = " + M_ProductContainer_ID;
            }
            else
            {
                sql = @"SELECT DISTINCT First_VALUE(t.CurrentQty) OVER (PARTITION BY t.M_Product_ID, t.M_AttributeSetInstance_ID ORDER BY t.MovementDate DESC, t.M_Transaction_ID DESC) AS CurrentQty FROM M_Transaction t 
                        INNER JOIN M_Locator l ON t.M_Locator_ID = l.M_Locator_ID WHERE t.MovementDate <= " + GlobalVariable.TO_DATE(tsDate, true) +
                        " AND t.AD_Org_ID = " + AD_Org_ID + " AND t.M_Locator_ID = " + M_Locator_ID +
                        " AND t.M_Product_ID = " + M_Product_ID + " AND NVL(t.M_AttributeSetInstance_ID,0) = " + M_AttributeSetInstance_ID;
            }
            currentqty = Util.GetValueOfDecimal(DB.ExecuteScalar(sql, null, null));
            return currentqty;
        }
    }
}