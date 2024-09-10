using System;
using System.Collections.Generic;
using System.Data;
using System.Data.SqlClient;
using System.Dynamic;
using System.Linq;
using System.Text;
using System.Web;
using VAdvantage.DataBase;
using VAdvantage.Logging;
using VAdvantage.Model;
using VAdvantage.Utility;

namespace VIS.Models
{
    public class MProductModel
    {
        string sqlWhereForLookup = "";
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
            string sql = "SELECT C_UOM_ID FROM M_Product WHERE IsActive = 'Y' AND M_Product_ID = " + paramValue[0];
            return Util.GetValueOfInt(DB.ExecuteScalar(sql, null, null));
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
            Trx trx = Trx.Get("VAS_ProductUOMSetup" + DateTime.Now.Ticks);
            try
            {
                foreach (var item in multiplyRateItems)
                {
                    if (C_UOM_ID != item.C_UOM_To_ID)
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
                            retDic["message"] = Msg.GetMsg(ctx, "VAS_UOMConvNotSaved");
                            return retDic;
                        }
                    }
                }
                // Update product with new UOM IDs
                bool updateResult = UpdateProductUOMs(ctx, trx, VAS_PurchaseUOM_ID, VAS_ConsumableUOM_ID, VAS_SalesUOM_ID, Product_ID);
                if (!updateResult)
                {
                    trx.Rollback();
                    retDic["Status"] = 0;
                    retDic["message"] = Msg.GetMsg(ctx, "VAS_ProductNotUpdated");
                    return retDic;
                }
                trx.Commit();
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
        /// VAI050-This method used to update UOM value on Product tab
        /// </summary>
        /// <param name="ctx"></param>
        /// <param name="trx"></param>
        /// <param name="purchaseUOMID"></param>
        /// <param name="consumableUOMID"></param>
        /// <param name="salesUOMID"></param>
        /// <param name="productID"></param>
        /// <returns></returns>
        private bool UpdateProductUOMs(Ctx ctx, Trx trx, int purchaseUOMID, int consumableUOMID, int salesUOMID, int productID)
        {
            string sql = @"UPDATE M_Product SET VAS_PurchaseUOM_ID = @PurchaseUOMID,
                                    VAS_ConsumableUOM_ID = @ConsumableUOMID,
                                    VAS_SalesUOM_ID = @SalesUOMID
                                 WHERE M_Product_ID = @ProductID";

            SqlParameter[] parameters = new SqlParameter[]
            {
                  new SqlParameter("@PurchaseUOMID", purchaseUOMID),
                  new SqlParameter("@ConsumableUOMID", consumableUOMID),
                  new SqlParameter("@SalesUOMID", salesUOMID),
                  new SqlParameter("@ProductID", productID)
            };

            int rowsAffected = DB.ExecuteQuery(sql, parameters, trx);
            return rowsAffected > 0;
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

        /// <summary>
        /// Get Top 10 Highest selling product data.
        /// </summary>
        /// <param name="ctx">Context</param>
        /// <returns>List of Product data</returns>
        public List<dynamic> GetTopProductData(Ctx ctx)
        {
            List<dynamic> retDic = null;
            dynamic obj;
            StringBuilder sb = new StringBuilder();
            string startdate, enddate;
            var C_Currency_ID = ctx.GetContextAsInt("$C_Currency_ID");

            if (DB.IsPostgreSQL())
            {
                startdate = "date_trunc('YEAR', CURRENT_DATE)";
                enddate = "date_trunc('YEAR', CURRENT_DATE) + INTERVAL '1' YEAR";
            }
            else
            {
                startdate = "TRUNC(SYSDATE, 'YEAR')";
                enddate = "TRUNC(ADD_MONTHS(SYSDATE, 12), 'YEAR')";
            }
            sb.Append("WITH current_year AS (" + MRole.GetDefault(ctx).AddAccessSQL(@"SELECT SUM(NVL(currencyConvert(ol.LineTotalAmt, 
                    o.C_Currency_ID, " + C_Currency_ID + @", o.DateAcct, o.C_ConversionType_ID, o.AD_Client_ID, o.AD_Org_ID), 0)) AS LineTotalAmt, 
                    SUM(ol.QtyInvoiced) AS CurrentQty, ol.M_Product_ID, p.Name, NVL(u.UOMSymbol, u.X12DE355) AS UOM, img.ImageUrl 
                    FROM C_InvoiceLine ol INNER JOIN C_Invoice o ON (ol.C_Invoice_ID = o.C_Invoice_ID)
                    INNER JOIN M_Product p ON (ol.M_Product_ID = p.M_Product_ID)
                    INNER JOIN C_UOM u ON (p.C_UOM_ID = u.C_UOM_ID)
                    LEFT JOIN AD_Image img ON (p.AD_Image_ID = img.AD_Image_ID)
                    WHERE o.DocStatus IN ('CO', 'CL') AND o.AD_Client_ID = " + ctx.GetAD_Client_ID()
                    + @" AND o.DateInvoiced >= " + startdate + " AND o.DateInvoiced < " + enddate, "ol",
                    MRole.SQL_FULLYQUALIFIED, MRole.SQL_RO) + @"GROUP BY ol.M_Product_ID, p.Name, NVL(u.UOMSymbol, u.X12DE355), img.ImageUrl 
                    ORDER BY LineTotalAmt DESC FETCH FIRST 10 ROWS ONLY)");

            if (DB.IsPostgreSQL())
            {
                startdate = "date_trunc('YEAR', CURRENT_DATE) - INTERVAL '1' YEAR";
                enddate = "date_trunc('YEAR', CURRENT_DATE)";
            }
            else
            {
                startdate = "TRUNC(SYSDATE, 'YEAR') - INTERVAL '1' YEAR";
                enddate = "TRUNC(SYSDATE, 'YEAR')";
            }
            sb.Append(", previous_year AS(" + MRole.GetDefault(ctx).AddAccessSQL(@"SELECT ol.M_Product_ID, SUM(NVL(currencyConvert(ol.LineTotalAmt, 
                    o.C_Currency_ID, " + C_Currency_ID + @", o.DateAcct, o.C_ConversionType_ID, o.AD_Client_ID, o.AD_Org_ID), 0)) AS LineTotalAmt, 
                    SUM(ol.QtyInvoiced) AS PreviousQty FROM C_InvoiceLine ol INNER JOIN C_Invoice o ON (ol.C_Invoice_ID = o.C_Invoice_ID)
                    WHERE o.DocStatus IN('CO', 'CL') AND o.AD_Client_ID = " + ctx.GetAD_Client_ID()
                    + @" AND o.DateInvoiced >= " + startdate + " AND o.DateInvoiced < " + enddate, "ol",
                    MRole.SQL_FULLYQUALIFIED, MRole.SQL_RO) + @" GROUP BY ol.M_Product_ID)
                    SELECT cy.M_Product_ID, cy.Name, cy.ImageUrl, NVL(py.LineTotalAmt, 0) AS PreviousTotal, cy.LineTotalAmt AS CurrentTotal,
                    cy.UOM, cy.CurrentQty, py.PreviousQty
                    FROM current_year cy LEFT JOIN previous_year py ON (cy.M_Product_ID = py.M_Product_ID)");
            DataSet ds = DB.ExecuteDataset(sb.ToString(), null, null);
            if (ds != null && ds.Tables.Count > 0 && ds.Tables[0].Rows.Count > 0)
            {
                retDic = new List<dynamic>();
                string Symbol = "$";
                int StdPrecision = 2;
                sb.Clear();
                sb.Append(@"SELECT CASE WHEN Cursymbol IS NOT NULL THEN Cursymbol ELSE ISO_Code END AS Symbol, StdPrecision 
                FROM C_Currency WHERE C_Currency_ID=" + C_Currency_ID);
                DataSet dsCurrency = DB.ExecuteDataset(sb.ToString(), null, null);
                if (dsCurrency != null && dsCurrency.Tables.Count > 0 && dsCurrency.Tables[0].Rows.Count > 0)
                {
                    Symbol = Util.GetValueOfString(dsCurrency.Tables[0].Rows[0]["Symbol"]);
                    StdPrecision = Util.GetValueOfInt(dsCurrency.Tables[0].Rows[0]["StdPrecision"]);
                }

                for (int i = 0; i < ds.Tables[0].Rows.Count; i++)
                {
                    obj = new ExpandoObject();
                    obj.M_Product_ID = Util.GetValueOfInt(ds.Tables[0].Rows[i]["M_Product_ID"]);
                    obj.Name = Util.GetValueOfString(ds.Tables[0].Rows[i]["Name"]);
                    obj.UOM = Util.GetValueOfString(ds.Tables[0].Rows[i]["UOM"]);
                    obj.ImageUrl = Util.GetValueOfString(ds.Tables[0].Rows[i]["ImageUrl"]);
                    obj.CurrentTotal = Util.GetValueOfDecimal(ds.Tables[0].Rows[i]["CurrentTotal"]);
                    obj.PreviousTotal = Util.GetValueOfDecimal(ds.Tables[0].Rows[i]["PreviousTotal"]);
                    obj.CurrentQty = Util.GetValueOfDecimal(ds.Tables[0].Rows[i]["CurrentQty"]);
                    obj.PreviousQty = Util.GetValueOfDecimal(ds.Tables[0].Rows[i]["PreviousQty"]);
                    obj.Symbol = Symbol;
                    obj.StdPrecision = StdPrecision;
                    retDic.Add(obj);
                }
            }
            return retDic;
        }

        /// <summary>
        /// Get 10 Lowest selling product data.
        /// </summary>
        /// <param name="ctx">Context</param>
        /// <returns>List of Product data</returns>
        public List<dynamic> GetLowestProductData(Ctx ctx)
        {
            List<dynamic> retDic = null;
            dynamic obj;
            StringBuilder sb = new StringBuilder();
            string startdate, enddate;
            var C_Currency_ID = ctx.GetContextAsInt("$C_Currency_ID");

            if (DB.IsPostgreSQL())
            {
                startdate = "date_trunc('YEAR', CURRENT_DATE)";
                enddate = "date_trunc('YEAR', CURRENT_DATE) + INTERVAL '1' YEAR";
            }
            else
            {
                startdate = "TRUNC(SYSDATE, 'YEAR')";
                enddate = "TRUNC(ADD_MONTHS(SYSDATE, 12), 'YEAR')";
            }
            sb.Append("WITH current_year AS (" + MRole.GetDefault(ctx).AddAccessSQL(@"SELECT SUM(NVL(currencyConvert(ol.LineTotalAmt, 
                    o.C_Currency_ID, " + C_Currency_ID + @", o.DateAcct, o.C_ConversionType_ID, o.AD_Client_ID, o.AD_Org_ID), 0)) AS LineTotalAmt,
                    SUM(ol.QtyInvoiced) AS CurrentQty, ol.M_Product_ID, p.Name, NVL(u.UOMSymbol, u.X12DE355) AS UOM, img.ImageUrl 
                    FROM C_InvoiceLine ol INNER JOIN C_Invoice o ON (ol.C_Invoice_ID = o.C_Invoice_ID)
                    INNER JOIN M_Product p ON (ol.M_Product_ID = p.M_Product_ID) 
                    INNER JOIN C_UOM u ON (p.C_UOM_ID = u.C_UOM_ID)
                    LEFT JOIN AD_Image img ON (p.AD_Image_ID = img.AD_Image_ID)
                    WHERE o.DocStatus IN ('CO', 'CL') AND o.AD_Client_ID = " + ctx.GetAD_Client_ID()
                    + @" AND o.DateInvoiced >= " + startdate + " AND o.DateInvoiced < " + enddate, "ol",
                    MRole.SQL_FULLYQUALIFIED, MRole.SQL_RO) + @"GROUP BY ol.M_Product_ID, p.Name, NVL(u.UOMSymbol, u.X12DE355), img.ImageUrl 
                    HAVING SUM(ol.LineTotalAmt) > 0 ORDER BY LineTotalAmt ASC FETCH FIRST 10 ROWS ONLY)");

            if (DB.IsPostgreSQL())
            {
                startdate = "date_trunc('YEAR', CURRENT_DATE) - INTERVAL '1' YEAR";
                enddate = "date_trunc('YEAR', CURRENT_DATE)";
            }
            else
            {
                startdate = "TRUNC(SYSDATE, 'YEAR') - INTERVAL '1' YEAR";
                enddate = "TRUNC(SYSDATE, 'YEAR')";
            }
            sb.Append(", previous_year AS(" + MRole.GetDefault(ctx).AddAccessSQL(@"SELECT ol.M_Product_ID, SUM(NVL(currencyConvert(ol.LineTotalAmt, 
                    o.C_Currency_ID, " + C_Currency_ID + @", o.DateAcct, o.C_ConversionType_ID, o.AD_Client_ID, o.AD_Org_ID), 0)) AS LineTotalAmt,
                    SUM(ol.QtyInvoiced) AS PreviousQty FROM C_InvoiceLine ol INNER JOIN C_Invoice o ON (ol.C_Invoice_ID = o.C_Invoice_ID)
                    WHERE o.DocStatus IN('CO', 'CL') AND o.AD_Client_ID = " + ctx.GetAD_Client_ID()
                    + @" AND o.DateInvoiced >= " + startdate + " AND o.DateInvoiced < " + enddate, "ol",
                    MRole.SQL_FULLYQUALIFIED, MRole.SQL_RO) + @" GROUP BY ol.M_Product_ID)
                    SELECT cy.M_Product_ID, cy.Name, cy.ImageUrl, NVL(py.LineTotalAmt, 0) AS PreviousTotal, cy.LineTotalAmt AS CurrentTotal,
                    cy.UOM, cy.CurrentQty, py.PreviousQty
                    FROM current_year cy LEFT JOIN previous_year py ON (cy.M_Product_ID = py.M_Product_ID)");
            DataSet ds = DB.ExecuteDataset(sb.ToString(), null, null);
            if (ds != null && ds.Tables.Count > 0 && ds.Tables[0].Rows.Count > 0)
            {
                retDic = new List<dynamic>();
                string Symbol = "$";
                int StdPrecision = 2;
                sb.Clear();
                sb.Append(@"SELECT CASE WHEN Cursymbol IS NOT NULL THEN Cursymbol ELSE ISO_Code END AS Symbol, StdPrecision 
                FROM C_Currency WHERE C_Currency_ID=" + C_Currency_ID);
                DataSet dsCurrency = DB.ExecuteDataset(sb.ToString(), null, null);
                if (dsCurrency != null && dsCurrency.Tables.Count > 0 && dsCurrency.Tables[0].Rows.Count > 0)
                {
                    Symbol = Util.GetValueOfString(dsCurrency.Tables[0].Rows[0]["Symbol"]);
                    StdPrecision = Util.GetValueOfInt(dsCurrency.Tables[0].Rows[0]["StdPrecision"]);
                }

                for (int i = 0; i < ds.Tables[0].Rows.Count; i++)
                {
                    obj = new ExpandoObject();
                    obj.M_Product_ID = Util.GetValueOfInt(ds.Tables[0].Rows[i]["M_Product_ID"]);
                    obj.Name = Util.GetValueOfString(ds.Tables[0].Rows[i]["Name"]);
                    obj.UOM = Util.GetValueOfString(ds.Tables[0].Rows[i]["UOM"]);
                    obj.ImageUrl = Util.GetValueOfString(ds.Tables[0].Rows[i]["ImageUrl"]);
                    obj.CurrentTotal = Util.GetValueOfDecimal(ds.Tables[0].Rows[i]["CurrentTotal"]);
                    obj.PreviousTotal = Util.GetValueOfDecimal(ds.Tables[0].Rows[i]["PreviousTotal"]);
                    obj.CurrentQty = Util.GetValueOfDecimal(ds.Tables[0].Rows[i]["CurrentQty"]);
                    obj.PreviousQty = Util.GetValueOfDecimal(ds.Tables[0].Rows[i]["PreviousQty"]);
                    obj.Symbol = Symbol;
                    obj.StdPrecision = StdPrecision;
                    retDic.Add(obj);
                }
            }
            return retDic;
        }
      
        /// <summary>
        ///  VAI050-Get the top 10 Highest selling products and lowest selling products
        /// </summary>
        /// <param name="ctx"></param>
        /// <param name="OrganizationUnit"></param>
        /// <param name="Type"></param>
        /// <returns></returns>
        public List<Dictionary<string, object>> GetHighestSellingProductList(Ctx ctx, int OrganizationUnit, string Type)
        {
            StringBuilder queryBuilder = new StringBuilder();
            string startdate = "";
            int C_Currency_ID = ctx.GetContextAsInt("$C_Currency_ID");
            string enddate = "";
            string WhereCondition = "";

            if (OrganizationUnit > 0)
            {
                WhereCondition = " AND l.AD_OrgTrx_ID =" + OrganizationUnit;
            }
            if (DB.IsPostgreSQL())
            {
                startdate = "date_trunc('YEAR', CURRENT_DATE)";
                enddate = "date_trunc('YEAR', CURRENT_DATE) + INTERVAL '1' YEAR";
                sqlWhereForLookup = @" AD_Org.IsActive = 'Y' AND (AD_Org.IsProfitCenter = 'Y') AND CAST(AD_Org.LegalEntityOrg AS INTEGER) =" + ctx.GetAD_Org_ID() + " AND AD_Org_ID IN ( " +
                                    MRole.GetDefault(ctx).AddAccessSQL("SELECT AD_OrgTrx_ID FROM C_Invoice", "C_Invoice", MRole.SQL_FULLYQUALIFIED, MRole.SQL_RO) + @"
                                     AND AD_OrgTrx_ID IS NOT NULL
                                     AND DateInvoiced >= " + startdate + @"
                                     AND DateInvoiced <=" + enddate + ")";
            }
            else
            {
                startdate = "TRUNC(SYSDATE, 'YEAR')";
                enddate = "TRUNC(ADD_MONTHS(SYSDATE, 12), 'YEAR')";
                sqlWhereForLookup = @" AD_Org.IsActive = 'Y' AND (AD_Org.IsProfitCenter = 'Y') AND AD_Org.LegalEntityOrg = " + ctx.GetAD_Org_ID() + " AND AD_Org_ID IN ( " +
                                     MRole.GetDefault(ctx).AddAccessSQL("SELECT AD_OrgTrx_ID FROM C_Invoice", "C_Invoice", MRole.SQL_FULLYQUALIFIED, MRole.SQL_RO) + @"
                                     AND AD_OrgTrx_ID IS NOT NULL
                                     AND DateInvoiced >= " + startdate + @"
                                     AND DateInvoiced <=" + enddate + ")";

            }
            queryBuilder.Append(@"WITH current_year AS ( " + MRole.GetDefault(ctx).AddAccessSQL(@"SELECT
                                  SUM(nvl(
                                 currencyconvert(
                                 il.linetotalamt, l.c_currency_id, " + C_Currency_ID + @", l.dateacct, l.c_conversiontype_id, l.AD_Client_id, l.ad_org_id
                                 ), 0)) AS linetotalamt,
                                il.m_product_id,
                                 p.name
                                  FROM  C_InvoiceLine il
                                 INNER JOIN C_Invoice l ON(il.C_Invoice_ID = l.C_Invoice_ID)
                                 INNER JOIN m_product p ON(il.m_product_id = p.m_product_id)
                                 LEFT JOIN ad_image img ON(p.ad_image_id = img.ad_image_id)", "il", MRole.SQL_FULLYQUALIFIED, MRole.SQL_RO) + @"
                                  AND
                                 l.docstatus IN('CO', 'CL') " + WhereCondition + @"
                                 AND l.DateInvoiced >= ").Append(startdate).Append(@"
                                 AND l.DateInvoiced <= ").Append(enddate).Append(@"
                                 GROUP BY il.M_Product_ID, p.Name
                                 HAVING SUM(il.linetotalamt) > 0
                                 ORDER BY LineTotalAmt " + Type + @"
                                 FETCH FIRST 10 ROWS ONLY),");
            if (DB.IsPostgreSQL())
            {
                startdate = "date_trunc('YEAR', CURRENT_DATE) - INTERVAL '1' YEAR";
                enddate = "date_trunc('YEAR', CURRENT_DATE)";
            }
            else
            {
                startdate = "TRUNC(SYSDATE, 'YEAR') - INTERVAL '1' YEAR";
                enddate = "TRUNC(SYSDATE, 'YEAR')";
            }
            queryBuilder.Append(@"previous_year AS (" + MRole.GetDefault(ctx).AddAccessSQL(@"SELECT 
                                 il.M_Product_ID,
                                 SUM(NVL(currencyConvert(il.LineTotalAmt,
                                 l.C_Currency_ID, " + C_Currency_ID + @", l.DateAcct, l.C_ConversionType_ID, l.AD_Client_ID, l.AD_Org_ID), 0)) AS LineTotalAmt
                                 FROM  C_InvoiceLine il
                                 INNER JOIN C_Invoice l ON(il.C_Invoice_ID = l.C_Invoice_ID)", "il", MRole.SQL_FULLYQUALIFIED, MRole.SQL_RO) + @"   
                                   AND
                                 l.docstatus IN ('CO', 'CL') " + WhereCondition + @"
                                 AND l.DateInvoiced >= ").Append(startdate).Append(@"
                                 AND l.DateInvoiced <= ").Append(enddate).Append(@"
                                 GROUP BY il.m_product_id )");
            queryBuilder.Append(@"SELECT  cy.M_Product_ID,  cy.Name,
                                  NVL(py.LineTotalAmt, 0) AS PreviousTotal, 
                                  cy.LineTotalAmt AS CurrentTotal
                                  FROM current_year cy 
                                  LEFT JOIN previous_year py ON cy.M_Product_ID = py.M_Product_ID");
            DataSet ds = DB.ExecuteDataset(queryBuilder.ToString(), null, null);
            if (ds != null && ds.Tables.Count > 0 && ds.Tables[0].Rows.Count > 0)
            {
                List<Dictionary<string, object>> ProductList = new List<Dictionary<string, object>>();
                for (int i = 0; i < ds.Tables[0].Rows.Count; i++)
                {
                    Dictionary<string, object> retDic = new Dictionary<string, object>();
                    retDic["Product_ID"] = Util.GetValueOfInt(ds.Tables[0].Rows[i]["M_Product_ID"]);
                    retDic["ProductName"] = Util.GetValueOfString(ds.Tables[0].Rows[i]["Name"]);
                    retDic["CurrentTotal"] = Util.GetValueOfInt(ds.Tables[0].Rows[i]["CurrentTotal"]);
                    retDic["PreviousTotal"] = Util.GetValueOfInt(ds.Tables[0].Rows[i]["PreviousTotal"]);
                    ProductList.Add(retDic);
                }
                return ProductList;
            }
            return null;
        }

        /// <summary>
        /// VAI050-This method is used to get the Top 10 Highest selling
        ///        And Lowest 10  selling Products
        /// </summary>
        /// <param name="ctx"></param>
        /// <param name="OrganizationUnit"></param>
        /// <param name="Type"></param>
        /// <returns></returns>
        public Dictionary<string, object> GetProductSalesAndDetails(Ctx ctx, int OrganizationUnit, string Type)
        {
            // Get the list of top 10 highest selling products
            List<Dictionary<string, object>> productList = GetHighestSellingProductList(ctx, OrganizationUnit, Type);
            string Symbol = "$";
            int StdPrecision = 2;
            if (productList == null || productList.Count == 0)
            {
                return null;
            }
            else
            {
                string query = @"SELECT CASE WHEN Cursymbol IS NOT NULL THEN Cursymbol ELSE ISO_Code END AS Symbol, StdPrecision 
                FROM C_Currency WHERE C_Currency_ID=" + ctx.GetContextAsInt("$C_Currency_ID");
                DataSet dsCurrency = DB.ExecuteDataset(query, null, null);
                if (dsCurrency != null && dsCurrency.Tables.Count > 0 && dsCurrency.Tables[0].Rows.Count > 0)
                {
                    Symbol = Util.GetValueOfString(dsCurrency.Tables[0].Rows[0]["Symbol"]);
                    StdPrecision = Util.GetValueOfInt(dsCurrency.Tables[0].Rows[0]["StdPrecision"]);
                }
            }
            int firstProductID = Util.GetValueOfInt(productList[0]["Product_ID"]);
            // Get the monthly sales data for the first product
            List<Dictionary<string, object>> monthlyData = GetMonthlyDataOfProduct(ctx, firstProductID, OrganizationUnit);
            Dictionary<string, object> result = new Dictionary<string, object>();
            result["products"] = productList;
            result["product_monthly_sales"] = monthlyData;
            result["sqlWhereForLookup"] = sqlWhereForLookup;
            result["Symbol"] = Symbol;
            result["StdPrecision"] = StdPrecision;
            return result;
        }
      
        /// <summary>
        /// VAI050-Get the montly details of product
        /// </summary>
        /// <param name="ctx"></param>
        /// <param name="ProductID"></param>
        /// <param name="OrganizationUnit"></param>
        /// <returns></returns>
        public List<Dictionary<string, object>> GetMonthlyDataOfProduct(Ctx ctx, int ProductID, int OrganizationUnit)
        {
            // Base query for Oracle
            string oracleQuery = @"
                                WITH months AS (
                                SELECT
                                TO_CHAR(TRUNC(SYSDATE, 'YEAR') + INTERVAL '1' MONTH * LEVEL - 1, 'Mon') AS month_name
                                FROM DUAL CONNECT BY LEVEL <= 12),
                                monthly_current_year AS (" + MRole.GetDefault(ctx).AddAccessSQL(@"SELECT il.M_Product_ID,TO_CHAR(i.DateInvoiced, 'Mon') AS month_name,SUM(il.QtyInvoiced) AS TotalQty
                                FROM
                                C_InvoiceLine il
                                INNER JOIN C_Invoice i ON il.C_Invoice_ID = i.C_Invoice_ID", "il", MRole.SQL_FULLYQUALIFIED, MRole.SQL_RO) + @"                               
                                 AND
                                i.DocStatus IN ('CO', 'CL') {0}
                                AND i.DateInvoiced >= TRUNC(SYSDATE, 'YEAR')
                                AND i.DateInvoiced < TRUNC(SYSDATE, 'MONTH') + INTERVAL '1' MONTH
                                AND il.M_Product_ID = {1}
                                GROUP BY
                                il.M_Product_ID,
                                TO_CHAR(i.DateInvoiced, 'Mon')
                                ),
                                monthly_previous_year AS (" + MRole.GetDefault(ctx).AddAccessSQL(@"SELECT il.M_Product_ID,TO_CHAR(i.DateInvoiced, 'Mon') AS month_name, SUM(il.QtyInvoiced) AS TotalQty
                                FROM
                                C_InvoiceLine il
                                INNER JOIN C_Invoice i ON il.C_Invoice_ID = i.C_Invoice_ID", "il", MRole.SQL_FULLYQUALIFIED, MRole.SQL_RO) + @"                              
                                 AND
                                i.DocStatus IN ('CO', 'CL') {0}
                                AND i.DateInvoiced >= TRUNC(SYSDATE, 'YEAR') - INTERVAL '1' YEAR
                                AND i.DateInvoiced < TRUNC(SYSDATE, 'YEAR')
                                AND il.M_Product_ID = {1}
                                GROUP BY
                                il.M_Product_ID,
                               TO_CHAR(i.DateInvoiced, 'Mon')
                                )
                               SELECT  m.month_name,COALESCE(cm.TotalQty, 0) AS cy_month_sales,COALESCE(py.TotalQty, 0) AS ly_month_sales
                               FROM
                               months m
                               LEFT JOIN monthly_current_year cm ON m.month_name = cm.month_name
                               LEFT JOIN monthly_previous_year py ON m.month_name = py.month_name
                               ORDER BY  TO_DATE(m.month_name, 'Mon')";

            // Adjust query for PostgreSQL
            if (DB.IsPostgreSQL())
            {
                oracleQuery = @" WITH months AS (
                               SELECT TO_CHAR(date_trunc('year', CURRENT_DATE) + INTERVAL '1 month' * (s - 1), 'Mon') AS month_name
                               FROM
                               generate_series(1, 12) AS s
                               ),
                               monthly_current_year AS (" + MRole.GetDefault(ctx).AddAccessSQL(@"SELECT
                               il.M_Product_ID,
                               TO_CHAR(i.DateInvoiced, 'Mon') AS month_name,
                               SUM(il.QtyInvoiced) AS TotalQty
                               FROM
                               C_InvoiceLine il
                                INNER JOIN C_Invoice i ON il.C_Invoice_ID = i.C_Invoice_ID", "il", MRole.SQL_FULLYQUALIFIED, MRole.SQL_RO) + @"          
                               AND
                               i.DocStatus IN ('CO', 'CL') {0}  AND i.DateInvoiced >= date_trunc('year', CURRENT_DATE)
                               AND i.DateInvoiced < date_trunc('month', CURRENT_DATE) + INTERVAL '1 month'
                               AND il.M_Product_ID = {1}
                               GROUP BY
                               il.M_Product_ID,
                               TO_CHAR(i.DateInvoiced, 'Mon')
                                ),
                                monthly_previous_year AS (" + MRole.GetDefault(ctx).AddAccessSQL(@"SELECT
                                il.M_Product_ID, TO_CHAR(i.DateInvoiced, 'Mon') AS month_name, SUM(il.QtyInvoiced) AS TotalQty
                                FROM
                                C_InvoiceLine il
                                INNER JOIN C_Invoice i ON il.C_Invoice_ID = i.C_Invoice_ID", "il", MRole.SQL_FULLYQUALIFIED, MRole.SQL_RO) + @"           
                                AND   i.DocStatus IN ('CO', 'CL') {0}
                                AND i.DateInvoiced >= date_trunc('year', CURRENT_DATE) - INTERVAL '1 year'
                                AND i.DateInvoiced < date_trunc('year', CURRENT_DATE)
                                AND il.M_Product_ID = {1}
                                GROUP BY
                                il.M_Product_ID, TO_CHAR(i.DateInvoiced, 'Mon')
                                 )
                                SELECT m.month_name, COALESCE(cm.TotalQty, 0) AS cy_month_sales, COALESCE(py.TotalQty, 0) AS ly_month_sales
                                 FROM
                                 months m
                                 LEFT JOIN monthly_current_year cm ON m.month_name = cm.month_name
                                 LEFT JOIN monthly_previous_year py ON m.month_name = py.month_name
                                 ORDER BY  TO_DATE(m.month_name, 'Mon')";
            }

            // Append OrganizationUnit condition
            string organizationUnitCondition = OrganizationUnit > 0 ? $" AND il.AD_OrgTrx_ID = {OrganizationUnit}" : "";
            string query = string.Format(oracleQuery, organizationUnitCondition, ProductID);
            DataSet ds = DB.ExecuteDataset(query, null, null);
            if (ds != null && ds.Tables.Count > 0 && ds.Tables[0].Rows.Count > 0)
            {
                List<Dictionary<string, object>> result = new List<Dictionary<string, object>>();
                foreach (DataRow row in ds.Tables[0].Rows)
                {
                    var obj = new Dictionary<string, object>
                    {
                        ["month"] = Util.GetValueOfString(row["month_name"]),
                        ["cy_month_sales"] = Util.GetValueOfInt(row["cy_month_sales"]),
                        ["ly_month_sales"] = Util.GetValueOfInt(row["ly_month_sales"])
                    };
                    result.Add(obj);
                }
                return result;
            }

            return null;
        }

        public class MultiplyRateItem
        {
            public int C_UOM_To_ID { get; set; }
            public decimal MultiplyRate { get; set; }
            public decimal DivideRate { get; set; }
        }
    }
}