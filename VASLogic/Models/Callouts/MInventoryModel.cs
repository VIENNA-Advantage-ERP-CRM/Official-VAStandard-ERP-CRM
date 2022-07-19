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
            string tsDate = Util.GetValueOfString(paramValue[1].ToString());
            int M_Product_ID = Util.GetValueOfInt(paramValue[2].ToString());
            int M_Locator_ID = Util.GetValueOfInt(paramValue[3].ToString());
            int M_AttributeSetInstance_ID = Util.GetValueOfInt(paramValue[4].ToString());
            int M_ProductContainer_ID = Util.GetValueOfInt(paramValue[5].ToString());
            int AD_Org_ID= Util.GetValueOfInt(paramValue[6].ToString());
            string query = string.Empty;
            string sql = string.Empty;
            decimal currentqty = 0;
            int result = 0;

            if (isContainerApplicable)
            {
                query = "SELECT COUNT(*) FROM M_Transaction WHERE movementdate = TO_DATE('" + tsDate + "', 'MM - DD - YYYY')" +
                    " AND  M_Product_ID = " + M_Product_ID + " AND M_Locator_ID = " + M_Locator_ID + " AND M_AttributeSetInstance_ID = " + M_AttributeSetInstance_ID + " AND NVL(M_ProductContainer_ID, 0) = " + M_ProductContainer_ID;
            }
            else
            {
                query = "SELECT COUNT(*) FROM M_Transaction WHERE movementdate = TO_DATE('" + tsDate + "', 'MM - DD - YYYY')" +
                    " AND  M_Product_ID = " + M_Product_ID + " AND M_Locator_ID = " + M_Locator_ID + " AND M_AttributeSetInstance_ID = " + M_AttributeSetInstance_ID;
            }
            result = Util.GetValueOfInt(DB.ExecuteScalar(query,null,null));
            if (result > 0)
            {
                if (isContainerApplicable)
                {
                    sql = "SELECT  NVL(ContainerCurrentQty, 0) AS currentqty FROM M_Transaction WHERE M_Transaction_ID = (SELECT MAX(M_Transaction_ID)   FROM M_Transaction  WHERE movementdate = " +
                        "(SELECT MAX(movementdate) FROM M_Transaction WHERE movementdate <= TO_DATE('" + tsDate + "', 'MM - DD - YYYY')  AND  M_Product_ID = " + M_Product_ID + " AND M_Locator_ID = " + M_Locator_ID +
                        " AND M_AttributeSetInstance_ID = " + M_AttributeSetInstance_ID + " AND NVL(M_ProductContainer_ID, 0) = " + M_ProductContainer_ID +
                        ") AND  M_Product_ID = " + M_Product_ID + " AND M_Locator_ID = " + M_Locator_ID +
                        " AND M_AttributeSetInstance_ID = " + M_AttributeSetInstance_ID + " AND NVL(M_ProductContainer_ID, 0) = " + M_ProductContainer_ID +
                        ")  AND  M_Product_ID = " + M_Product_ID +
                        " AND M_Locator_ID = " + M_Locator_ID + " AND M_AttributeSetInstance_ID = " + M_AttributeSetInstance_ID + " AND NVL(M_ProductContainer_ID, 0) = " + M_ProductContainer_ID;
                }
                else
                {
                    sql = "SELECT currentqty FROM M_Transaction WHERE M_Transaction_ID = (SELECT MAX(M_Transaction_ID)   FROM M_Transaction  WHERE movementdate = " +
                        "(SELECT MAX(movementdate) FROM M_Transaction WHERE movementdate <= TO_DATE('" + tsDate + "', 'MM - DD - YYYY')  AND  M_Product_ID = " + M_Product_ID + " AND M_Locator_ID = " + M_Locator_ID +
                        " AND M_AttributeSetInstance_ID = " + M_AttributeSetInstance_ID + ") AND  M_Product_ID = " + M_Product_ID + " AND M_Locator_ID = " + M_Locator_ID +
                        " AND M_AttributeSetInstance_ID = " + M_AttributeSetInstance_ID + ") AND AD_Org_ID = " + AD_Org_ID + " AND  M_Product_ID = " + M_Product_ID +
                        " AND M_Locator_ID = " + M_Locator_ID + " AND M_AttributeSetInstance_ID = " + M_AttributeSetInstance_ID;
                }
                currentqty = Util.GetValueOfDecimal(DB.ExecuteScalar(sql,null,null));
            }
            else
            {
                if (isContainerApplicable)
                {
                    query = "SELECT COUNT(*) FROM M_Transaction WHERE movementdate < TO_DATE('" + tsDate + "', 'MM - DD - YYYY') AND  M_Product_ID = " + M_Product_ID +
                        " AND M_Locator_ID = " + M_Locator_ID + " AND M_AttributeSetInstance_ID = " + M_AttributeSetInstance_ID + " AND NVL(M_ProductContainer_ID, 0) = " + M_ProductContainer_ID;
                }
                else
                {
                    query = "SELECT COUNT(*) FROM M_Transaction WHERE movementdate < TO_DATE('" + tsDate + "', 'MM - DD - YYYY') AND  M_Product_ID = " + M_Product_ID +
                        " AND M_Locator_ID = " + M_Locator_ID + " AND M_AttributeSetInstance_ID = " + M_AttributeSetInstance_ID;
                }
                result = Util.GetValueOfInt(DB.ExecuteScalar(query, null, null));
                if (result > 0)
                {
                    if (isContainerApplicable)
                    {
                        sql = "SELECT NVL(ContainerCurrentQty, 0) AS currentqty FROM M_Transaction WHERE M_Transaction_ID = (SELECT MAX(M_Transaction_ID)   FROM M_Transaction  WHERE movementdate = " +
                            " (SELECT MAX(movementdate) FROM M_Transaction WHERE movementdate < TO_DATE('" + tsDate + "', 'MM - DD - YYYY') AND  M_Product_ID = " + M_Product_ID + " AND M_Locator_ID = " + M_Locator_ID +
                            " AND M_AttributeSetInstance_ID = " + M_AttributeSetInstance_ID + " AND NVL(M_ProductContainer_ID, 0) = " + M_ProductContainer_ID +
                            ") AND  M_Product_ID = " + M_Product_ID + " AND M_Locator_ID = " + M_Locator_ID +
                            " AND M_AttributeSetInstance_ID = " + M_AttributeSetInstance_ID + " AND NVL(M_ProductContainer_ID, 0) = " + M_ProductContainer_ID +
                            ")  AND  M_Product_ID = " + M_Product_ID +
                            " AND M_Locator_ID = " + M_Locator_ID + " AND M_AttributeSetInstance_ID = " + M_AttributeSetInstance_ID + " AND NVL(M_ProductContainer_ID, 0) = " + M_ProductContainer_ID;
                    }
                    else
                    {
                        sql = "SELECT currentqty FROM M_Transaction WHERE M_Transaction_ID = (SELECT MAX(M_Transaction_ID)   FROM M_Transaction  WHERE movementdate = " +
                            " (SELECT MAX(movementdate) FROM M_Transaction WHERE movementdate < TO_DATE('" + tsDate + "', 'MM - DD - YYYY')AND  M_Product_ID = " + M_Product_ID + " AND M_Locator_ID = " + M_Locator_ID +
                            " AND M_AttributeSetInstance_ID = " + M_AttributeSetInstance_ID + ") AND  M_Product_ID = " + M_Product_ID + " AND M_Locator_ID = " + M_Locator_ID +
                            " AND M_AttributeSetInstance_ID = " + M_AttributeSetInstance_ID + ") AND AD_Org_ID = " + AD_Org_ID + " AND  M_Product_ID = " + M_Product_ID +
                            " AND M_Locator_ID = " + M_Locator_ID + " AND M_AttributeSetInstance_ID = " + M_AttributeSetInstance_ID;
                    }
                    currentqty = Util.GetValueOfDecimal(DB.ExecuteScalar(sql, null, null));
                }
            }
            return currentqty;
        }
    }
}