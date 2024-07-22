using System;
using System.Collections.Generic;
using System.Data;
using System.Data.SqlClient;
using System.Linq;
using System.Web;
using VAdvantage.DataBase;
using VAdvantage.Logging;
using VAdvantage.Model;
using VAdvantage.Utility;

namespace VIS.Models
{
    public class MProductModel
    {
        public Dictionary<string, string> GetProduct(Ctx ctx, string fields)
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
                if (M_Warehouse_ID > 0)
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
        public int GetUOMPrecision(Ctx ctx, string fields)
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
        public int GetC_UOM_ID(Ctx ctx, string fields)
        {
            string[] paramValue = fields.Split(',');
            int M_Product_ID;
            M_Product_ID = Util.GetValueOfInt(paramValue[0].ToString());
            //VAI050-Return Purchase UOM if Product have
            if (Util.GetValueOfInt(MProduct.Get(ctx, M_Product_ID).Get_Value("VAS_PurchaseUOM_ID")) > 0)
                return Util.GetValueOfInt(MProduct.Get(ctx, M_Product_ID).Get_Value("VAS_PurchaseUOM_ID"));
            else
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
        /// <summary>
        /// GetPOUOM
        /// </summary>
        /// <param name="fields">fields</param>
        /// <returns>Get UOM_ID from M_Product_PO</returns>
        public int GetPOUOM(string fields)
        {
            string[] paramValue = fields.Split(',');
            string sql = "SELECT C_UOM_ID FROM M_Product_PO WHERE IsActive = 'Y' AND  C_BPartner_ID = " + paramValue[0] + " AND M_Product_ID = " + paramValue[1];
            //VAI050-Get Purchase UOM form Product if Purchasing not found
            if (Util.GetValueOfInt(DB.ExecuteScalar(sql, null, null)) > 0)
                return Util.GetValueOfInt(DB.ExecuteScalar(sql, null, null));
            else
                sql = "SELECT VAS_PurchaseUOM_ID FROM M_Product WHERE M_Product_ID=" + paramValue[1];
            return Util.GetValueOfInt(DB.ExecuteScalar(sql, null, null));
        }
        /// <summary>
        /// GetUOMID
        /// </summary>
        /// <param name="M_Product_ID">M_Product_ID</param>
        /// <returns>Get UOM_ID from M_Product </returns>
        public int GetUOMID(string fields)
        {
            string[] paramValue = fields.Split(',');
            string sql = "SELECT C_UOM_ID,VAS_SalesUOM_ID FROM M_Product WHERE IsActive = 'Y' AND M_Product_ID = " + paramValue[0];
            DataSet ds = DB.ExecuteDataset(sql, null, null);
            if (ds != null && ds.Tables.Count > 0 && ds.Tables[0].Rows.Count > 0)
            {
                if (Util.GetValueOfInt(ds.Tables[0].Rows[0]["VAS_SalesUOM_ID"]) > 0)
                {
                    return Util.GetValueOfInt(ds.Tables[0].Rows[0]["VAS_SalesUOM_ID"]);
                }
                return Util.GetValueOfInt(ds.Tables[0].Rows[0]["C_UOM_ID"]);
            }
            return 0;
        }
        /// <summary>
        /// GetManufacturer
        /// </summary>
        /// <param name="fields">fields</param>
        /// <returns>Count</returns>
        public int GetManufacturer(string fields)
        {
            string sql = "SELECT Count(M_Manufacturer_ID) FROM M_Manufacturer WHERE IsActive = 'Y' AND UPC = '" + fields + "'";
            return Util.GetValueOfInt(DB.ExecuteScalar(sql, null, null));
        }

        /// <summary>
        /// VAI050-Save the UOM Conversion
        /// </summary>
        /// <param name="ctx"></param>
        /// <param name="C_UOM_ID"></param>
        /// <param name="multiplyRateItems"></param>
        /// <param name="Product_ID"></param>
        /// <param name="VAS_PurchaseUOM_ID"></param>
        /// <param name="VAS_SalesUOM_ID"></param>
        /// <param name="VAS_ConsumableUOM_ID"></param>
        /// <returns></returns>
        public Dictionary<string, object> SaveUOMConversion(Ctx ctx, int C_UOM_ID, List<MultiplyRateItem> multiplyRateItems, int Product_ID, int VAS_PurchaseUOM_ID, int VAS_SalesUOM_ID, int VAS_ConsumableUOM_ID)
        {
            Dictionary<string, object> retDic = new Dictionary<string, object>
                    { { "Status", 1 } };
            Trx trx = null;
            try
            {
                trx = Trx.Get("VAS_ProductUOMSetup" + DateTime.Now.Ticks);

                foreach (var item in multiplyRateItems)
                {
                    MUOMConversion obj = new MUOMConversion(ctx, 0, trx);
                    obj.SetAD_Client_ID(ctx.GetAD_Client_ID());
                    obj.SetAD_Org_ID(ctx.GetAD_Org_ID());
                    obj.SetC_UOM_ID(C_UOM_ID);
                    obj.SetC_UOM_To_ID(item.C_UOM_To_ID);
                    obj.SetMultiplyRate(item.DivideRate); // Dividing rate is saved in MultiplyRate column
                    obj.SetDivideRate(item.MultiplyRate); // Multiplying rate is saved in DivideRate column
                    obj.SetM_Product_ID(Product_ID);

                    if (!obj.Save())
                    {
                        trx.Rollback();
                        retDic["Status"] = 0;
                        retDic["message"] = Msg.GetMsg(ctx, "VAS_UOMConvNotSaved") + " - " + VLogger.RetrieveError()?.GetName();
                        return retDic;
                    }
                }

                // Update M_Product with new UOM IDs
                if (multiplyRateItems.Count > 0)
                {
                    // Define the parameters
                    SqlParameter[] param = new SqlParameter[]
                    {
                      new SqlParameter("@PurchaseUOMID", VAS_PurchaseUOM_ID),
                      new SqlParameter("@ConsumableUOMID", VAS_ConsumableUOM_ID),
                      new SqlParameter("@SalesUOMID", VAS_SalesUOM_ID),
                      new SqlParameter("@ProductID", Product_ID)
                    };

                    //use parameterized query
                    string query = @"UPDATE M_Product SET VAS_PurchaseUOM_ID = @PurchaseUOMID, VAS_ConsumableUOM_ID = @ConsumableUOMID, 
                                     VAS_SalesUOM_ID = @SalesUOMID WHERE M_Product_ID = @ProductID";
                    if (DB.ExecuteQuery(query, param, trx) <= 0)
                    {
                        trx.Rollback();
                        retDic["Status"] = 0;
                        retDic["message"] = Msg.GetMsg(ctx, "VAS_ProductNotUpdated");
                        return retDic;
                    }

                    trx.Commit();
                }

            }
            catch (Exception ex)
            {
                trx?.Rollback();
                retDic["Status"] = 0;
                retDic["message"] = Msg.GetMsg(ctx, "VAS_UOMConvNotSaved") + " - " + ex.Message;
                return retDic;
            }
            finally
            {
                trx?.Close();
            }

            retDic["message"] = Msg.GetMsg(ctx, "VAS_UOMConvSaved");
            return retDic;
        }

        /// <summary>
        /// VAI050-Get Product's UOM 
        /// </summary>
        /// <param name="fields"></param>
        /// <returns></returns>
        public Dictionary<string, int> GetProductUOMs(string fields)
        {
            Dictionary<string, int> retDic = null;
            string[] paramValue = fields.Split(',');
            int M_Product_ID = Util.GetValueOfInt(paramValue[1].ToString());
            int C_BPartner_ID = Util.GetValueOfInt(paramValue[0].ToString());
            int PurchaseUOM = 0;

            if (C_BPartner_ID > 0)
            {
                string query = @"SELECT C_UOM_ID FROM M_Product_PO WHERE IsActive = 'Y' AND  C_BPartner_ID = " + C_BPartner_ID + " " +
                                 " AND M_Product_ID = " + M_Product_ID;
                PurchaseUOM = Util.GetValueOfInt(DB.ExecuteScalar(query, null, null));
            }     
            string sql = "SELECT C_UOM_ID,VAS_SalesUOM_ID,VAS_PurchaseUOM_ID,VAS_ConsumableUOM_ID FROM M_Product WHERE M_Product_ID = " + M_Product_ID;
            DataSet ds = DB.ExecuteDataset(sql, null, null);
            if (ds != null && ds.Tables[0].Rows.Count > 0)
            {
                retDic = new Dictionary<string, int>();
                retDic["C_UOM_ID"] = Util.GetValueOfInt(ds.Tables[0].Rows[0]["C_UOM_ID"]);
                retDic["VAS_SalesUOM_ID"] = Util.GetValueOfInt(ds.Tables[0].Rows[0]["VAS_SalesUOM_ID"]);
                retDic["VAS_PurchaseUOM_ID"] = Util.GetValueOfInt(ds.Tables[0].Rows[0]["VAS_PurchaseUOM_ID"]);
                retDic["VAS_ConsumableUOM_ID"] = Util.GetValueOfInt(ds.Tables[0].Rows[0]["VAS_ConsumableUOM_ID"]);
                retDic["PurchaseUOM"] = PurchaseUOM;

            }
            return retDic;
        }
        public class MultiplyRateItem
        {
            public int C_UOM_To_ID { get; set; }
            public decimal MultiplyRate { get; set; }
            public decimal DivideRate { get; set; }
        }






    }
}