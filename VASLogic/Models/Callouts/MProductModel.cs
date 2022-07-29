using System;
using System.Collections.Generic;
using System.Data;
using System.Linq;
using System.Web;
using VAdvantage.DataBase;
using VAdvantage.Model;
using VAdvantage.Utility;

namespace VIS.Models
{
    public class MProductModel
    {
        public Dictionary<string, string> GetProduct(Ctx ctx,string fields)
        {
            string[] paramValue = fields.Split(',');

            //Assign parameter value
            int M_Product_ID = Util.GetValueOfInt(paramValue[0].ToString());
            int M_Warehouse_ID = 0;
            if (paramValue.Length > 1)
            {
                M_Warehouse_ID = Util.GetValueOfInt(paramValue[1].ToString());
            }
            //End Assign parameter value

            MProduct product = MProduct.Get(ctx, M_Product_ID);
            Dictionary<string, string> result = new Dictionary<string, string>();
            result["C_UOM_ID"] = product.GetC_UOM_ID().ToString();
            result["IsStocked"] = product.IsStocked() ? "Y" : "N";
            if (M_Product_ID > 0)
            {
                if(M_Warehouse_ID>0)
                result["M_Locator_ID"] = MProductLocator.GetFirstM_Locator_ID(product, M_Warehouse_ID).ToString();
            }
            result["DocumentNote"] = product.GetDocumentNote();
            //if (product.GetM_Product_Category_ID() > 0)
            //{
            //    result["M_Product_Category_ID"] = product.GetM_Product_Category_ID().ToString();
            //}
            //else
            //{
            //    result["M_Product_Category_ID"] = "0";
            //}
            //result["M_Product_ID"] = product.GetM_Product_ID().ToString();
            return result;
        }
        public string GetProductType(Ctx ctx, string fields)
        {
            string[] paramValue = fields.Split(',');

            //Assign parameter value
            int M_Product_ID = Util.GetValueOfInt(paramValue[0].ToString());
          
             MProduct prod = new MProduct(ctx, M_Product_ID, null);
             return prod.GetProductType(); ;
            
            
        }  
        /// <summary>
        /// Get UPM Precision
        /// </summary>
        /// <param name="ctx"></param>
        /// <param name="fields"></param>
        /// <returns></returns>
        public int GetUOMPrecision(Ctx ctx,string fields)
        {
            string[] paramValue = fields.Split(',');
            int M_Product_ID;
            M_Product_ID = Util.GetValueOfInt(paramValue[0].ToString());
            return MProduct.Get(ctx, M_Product_ID).GetUOMPrecision();
            
        }
        /// <summary>
        /// Get C
        /// </summary>
        /// <param name="fields"></param>
        /// <returns></returns>
        public int GetC_UOM_ID(Ctx ctx,string fields)
        {
            string[] paramValue = fields.Split(',');
            int M_Product_ID;
            M_Product_ID = Util.GetValueOfInt(paramValue[0].ToString());
            return MProduct.Get(ctx, M_Product_ID).GetC_UOM_ID();
           
        }

        //Added By amit
        public int GetTaxCategory(Ctx ctx, string fields)
        {
            string[] paramValue = fields.Split(',');
            int M_Product_ID;
            M_Product_ID = Util.GetValueOfInt(paramValue[0].ToString());
            return MProduct.Get(ctx, M_Product_ID).GetC_TaxCategory_ID();
        }
        //End

        /// <summary>
        /// Get C_RevenueRecognition_ID
        /// </summary>
        /// <param name="ctx">Context</param>
        /// <param name="fields">M_Product_ID</param>
        /// <returns>C_RevenueRecognition_ID</returns>
        public int GetRevenuRecognition(Ctx ctx, string fields)
        {
            string sql = "SELECT C_RevenueRecognition_ID FROM M_Product WHERE IsActive = 'Y' AND M_Product_ID = " + Util.GetValueOfInt(fields);
            return Util.GetValueOfInt(DB.ExecuteScalar(sql, null, null));
        }


        /// <summary>
        /// Get AttributeSet
        /// </summary>
        /// <param name="ctx">Context</param>
        /// <param name="fields">C_Product_ID</param>
        /// <returns>AttributeSet_ID</returns>
        public int GetAttributeSet(Ctx ctx, string fields)
        {
            string sql = "SELECT M_AttributeSet_ID FROM M_Product WHERE M_Product_ID = " + Util.GetValueOfInt(fields);
            return Util.GetValueOfInt(DB.ExecuteScalar(sql, null, null));
        }

        /// <summary>
        /// Get Product Attribute
        /// </summary>
        /// <param name="ctx">Context</param>
        /// <param name="fields">C_Product_ID</param>
        /// <returns>Attribute_ID</returns>
        public int GetProductAttribute(Ctx ctx, string fields)
        {
            string[] paramValue = fields.Split(',');
            string sql = "SELECT M_AttributeSetInstance_ID FROM M_ProductAttributes WHERE UPC = '" + paramValue[0] + "' AND M_Product_ID = " + Util.GetValueOfInt(paramValue[1]);
            return Util.GetValueOfInt(DB.ExecuteScalar(sql, null, null));
        }

        /// <summary>
        /// Get resource assignment details
        /// </summary>
        /// <param name="S_ResourceAssignment_ID">ResourceAssignment_ID</param>
        /// /// <param name="ctx">ctx</param>
        /// <returns>Result</returns>
        public Dictionary<String, Object> GetResourceAssignmntDet(Ctx ctx, int S_ResourceAssignment_ID)
        {
            Dictionary<string, object> retDic = null;
            string sql = "SELECT p.M_Product_ID, ra.Name, ra.Description, ra.Qty "
            + "FROM S_ResourceAssignment ra"
            + " INNER JOIN M_Product p ON (p.S_Resource_ID=ra.S_Resource_ID) "
            + "WHERE ra.S_ResourceAssignment_ID=" + S_ResourceAssignment_ID;		//	1
            DataSet ds = DB.ExecuteDataset(sql, null, null);
            if (ds != null && ds.Tables[0].Rows.Count > 0)
            {
                retDic = new Dictionary<string, object>();
                retDic["M_Product_ID"] = Util.GetValueOfInt(ds.Tables[0].Rows[0]["M_Product_ID"]);
                retDic["Name"] = Util.GetValueOfString(ds.Tables[0].Rows[0]["Name"]);
                retDic["Description"] = Util.GetValueOfString(ds.Tables[0].Rows[0]["Description"]);
                retDic["Qty"] = Util.GetValueOfDecimal(ds.Tables[0].Rows[0]["Qty"]);
            }
            return retDic;
        }

        /// Get Counts Of Transaction
        /// </summary>
        /// <param name="ctx">Context</param>
        /// <param name="M_Product_ID">Product ID</param>
        /// <returns>Transction COUNT</returns>
        public int GetTransactionCount(Ctx ctx, int M_Product_ID)
        {
            string sql = "SELECT COUNT(M_Transaction_ID) FROM M_Transaction WHERE M_Product_ID = " + M_Product_ID;
            return Util.GetValueOfInt(DB.ExecuteScalar(sql, null, null));
        }
    }
}