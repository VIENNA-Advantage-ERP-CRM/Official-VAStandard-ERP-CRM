using System;
using System.Collections.Generic;
using System.Data;
using System.Data.SqlClient;
using System.Dynamic;
using System.Linq;
using System.Text;
using System.Web;
using VAdvantage.Classes;
using VAdvantage.DataBase;
using VAdvantage.Logging;
using VAdvantage.Model;
using VAdvantage.Utility;
using ViennaAdvantage.Process;
using static ViennaAdvantage.Process.InOutGenerate;

namespace VIS.Models
{
    public class MProductModel
    {
        string sqlWhereForLookup = "";
        MInOut _shipment = null;
        int Shipment_ID = 0;

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
            sb.Append(@"WITH current_yearReturn AS ( " + MRole.GetDefault(ctx).AddAccessSQL(@" SELECT
                                 SUM(NVL(currencyconvert(
                                  il.LineNetAmt, l.c_currency_id, " + C_Currency_ID + @", l.dateacct, l.c_conversiontype_id, l.AD_Client_id, l.ad_org_id
                                  ), 0)) AS ReturnAmount,
                                   il.m_product_id,
                                   p.name
                                   FROM C_InvoiceLine il
                                   INNER JOIN C_Invoice l ON il.C_Invoice_ID = l.C_Invoice_ID
                                   INNER JOIN m_product p ON il.m_product_id = p.m_product_id
                                   INNER JOIN M_InOutLine dl ON dl.C_OrderLine_ID = il.C_OrderLine_ID
                                   INNER JOIN M_InOut d ON d.M_InOut_ID = dl.M_InOut_ID AND d.ISSoTrx = 'Y' AND d.IsReturnTrx = 'Y'", "il", MRole.SQL_FULLYQUALIFIED, MRole.SQL_RO) + @"
                                  AND l.IsSOTrx='Y' AND l.IsReturnTrx='Y' AND
                                 l.docstatus IN('CO', 'CL') 
                                 AND l.DateInvoiced >= ").Append(startdate).Append(@"
                                 AND l.DateInvoiced <= ").Append(enddate).Append(@"
                                 GROUP BY il.M_Product_ID, p.Name
                                 HAVING SUM(il.LineNetAmt)  > 0)
                                 ,");
            sb.Append(" current_year AS (" + MRole.GetDefault(ctx).AddAccessSQL(@"SELECT SUM(NVL(currencyConvert(ol.LineNetAmt, 
                    o.C_Currency_ID, " + C_Currency_ID + @", o.DateAcct, o.C_ConversionType_ID, o.AD_Client_ID, o.AD_Org_ID), 0)) AS LineTotalAmt, 
                    SUM(ol.QtyInvoiced) AS CurrentQty, ol.M_Product_ID, p.Name, NVL(u.UOMSymbol, u.X12DE355) AS UOM, img.ImageUrl 
                    FROM C_InvoiceLine ol INNER JOIN C_Invoice o ON (ol.C_Invoice_ID = o.C_Invoice_ID)
                    INNER JOIN M_Product p ON (ol.M_Product_ID = p.M_Product_ID)
                    INNER JOIN C_UOM u ON (p.C_UOM_ID = u.C_UOM_ID)
                    LEFT JOIN AD_Image img ON (p.AD_Image_ID = img.AD_Image_ID)
                    WHERE o.IsSOTrx='Y' AND o.IsReturnTrx='N' AND o.DocStatus IN ('CO', 'CL') AND o.AD_Client_ID = " + ctx.GetAD_Client_ID()
                    + @" AND o.DateInvoiced >= " + startdate + " AND o.DateInvoiced < " + enddate, "ol",
                    MRole.SQL_FULLYQUALIFIED, MRole.SQL_RO) + @" GROUP BY ol.M_Product_ID, p.Name, NVL(u.UOMSymbol, u.X12DE355), img.ImageUrl 
             HAVING NVL(SUM(NVL(ol.LineNetAmt,0)),0)>0
                    ORDER BY LineTotalAmt)");

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
            sb.Append(@", last_yearReturn AS ( " + MRole.GetDefault(ctx).AddAccessSQL(@" SELECT
                                 SUM(NVL(currencyconvert(
                                  il.LineNetAmt, l.c_currency_id, " + C_Currency_ID + @", l.dateacct, l.c_conversiontype_id, l.AD_Client_id, l.ad_org_id
                                  ), 0)) AS LastYearReturnAmount,
                                   il.m_product_id,
                                   p.name
                                   FROM C_InvoiceLine il
                                   INNER JOIN C_Invoice l ON il.C_Invoice_ID = l.C_Invoice_ID
                                   INNER JOIN m_product p ON il.m_product_id = p.m_product_id
                                   INNER JOIN M_InOutLine dl ON dl.C_OrderLine_ID = il.C_OrderLine_ID
                                   INNER JOIN M_InOut d ON d.M_InOut_ID = dl.M_InOut_ID AND d.ISSoTrx = 'Y' AND d.IsReturnTrx = 'Y'", "il", MRole.SQL_FULLYQUALIFIED, MRole.SQL_RO) + @"
                                  AND l.IsSOTrx='Y' AND l.IsReturnTrx='Y' AND
                                 l.docstatus IN('CO', 'CL') 
                                 AND l.DateInvoiced >= ").Append(startdate).Append(@"
                                 AND l.DateInvoiced <= ").Append(enddate).Append(@"
                                 GROUP BY il.M_Product_ID, p.Name
                                 HAVING SUM(il.LineNetAmt)  > 0)
                                 ");
            sb.Append(", previous_year AS(" + MRole.GetDefault(ctx).AddAccessSQL(@"SELECT ol.M_Product_ID, SUM(NVL(currencyConvert(ol.LineNetAmt, 
                    o.C_Currency_ID, " + C_Currency_ID + @", o.DateAcct, o.C_ConversionType_ID, o.AD_Client_ID, o.AD_Org_ID), 0)) AS LineTotalAmt, 
                    SUM(ol.QtyInvoiced) AS PreviousQty FROM C_InvoiceLine ol INNER JOIN C_Invoice o ON (ol.C_Invoice_ID = o.C_Invoice_ID)
                    WHERE o.IsSOTrx='Y' AND o.IsReturnTrx='N' AND o.DocStatus IN('CO', 'CL') AND o.AD_Client_ID = " + ctx.GetAD_Client_ID()
                    + @" AND o.DateInvoiced >= " + startdate + " AND o.DateInvoiced < " + enddate, "ol",
                    MRole.SQL_FULLYQUALIFIED, MRole.SQL_RO) + @" GROUP BY ol.M_Product_ID)
                    SELECT cy.M_Product_ID, cy.Name, cy.ImageUrl, NVL(py.LineTotalAmt, 0)-NVL(lyr.LastYearReturnAmount,0) AS PreviousTotal, NVL(cy.LineTotalAmt,0)-NVL(cyr.ReturnAmount,0) AS CurrentTotal,
                    cy.UOM, cy.CurrentQty, py.PreviousQty
                    FROM current_year cy LEFT JOIN previous_year py ON (cy.M_Product_ID = py.M_Product_ID)
                      LEFT JOIN current_yearReturn cyr ON cyr.M_Product_ID = cy.M_Product_ID
                    LEFT JOIN last_yearReturn lyr ON lyr.M_Product_ID = cy.M_Product_ID
                    ORDER BY CurrentTotal DESC
                     FETCH FIRST 10 ROWS ONLY   ");
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
            sb.Append(@"WITH current_yearReturn AS ( " + MRole.GetDefault(ctx).AddAccessSQL(@" SELECT
                                 SUM(NVL(currencyconvert(
                                  il.LineNetAmt, l.c_currency_id, " + C_Currency_ID + @", l.dateacct, l.c_conversiontype_id, l.AD_Client_id, l.ad_org_id
                                  ), 0)) AS ReturnAmount,
                                   il.m_product_id,
                                   p.name
                                   FROM C_InvoiceLine il
                                   INNER JOIN C_Invoice l ON il.C_Invoice_ID = l.C_Invoice_ID
                                   INNER JOIN m_product p ON il.m_product_id = p.m_product_id
                                   INNER JOIN M_InOutLine dl ON dl.C_OrderLine_ID = il.C_OrderLine_ID
                                   INNER JOIN M_InOut d ON d.M_InOut_ID = dl.M_InOut_ID AND d.ISSoTrx = 'Y' AND d.IsReturnTrx = 'Y'", "il", MRole.SQL_FULLYQUALIFIED, MRole.SQL_RO) + @"
                                  AND l.IsSOTrx='Y' AND l.IsReturnTrx='Y' AND
                                 l.docstatus IN('CO', 'CL') 
                                 AND l.DateInvoiced >= ").Append(startdate).Append(@"
                                 AND l.DateInvoiced <= ").Append(enddate).Append(@"
                                 GROUP BY il.M_Product_ID, p.Name
                                 HAVING SUM(il.LineNetAmt)  > 0)
                                 ,");
            sb.Append(" current_year AS (" + MRole.GetDefault(ctx).AddAccessSQL(@"SELECT SUM(NVL(currencyConvert(ol.LineNetAmt, 
                    o.C_Currency_ID, " + C_Currency_ID + @", o.DateAcct, o.C_ConversionType_ID, o.AD_Client_ID, o.AD_Org_ID), 0)) AS LineTotalAmt,
                    SUM(ol.QtyInvoiced) AS CurrentQty, ol.M_Product_ID, p.Name, NVL(u.UOMSymbol, u.X12DE355) AS UOM, img.ImageUrl 
                    FROM C_InvoiceLine ol INNER JOIN C_Invoice o ON (ol.C_Invoice_ID = o.C_Invoice_ID)
                    INNER JOIN M_Product p ON (ol.M_Product_ID = p.M_Product_ID) 
                    INNER JOIN C_UOM u ON (p.C_UOM_ID = u.C_UOM_ID)
                    LEFT JOIN AD_Image img ON (p.AD_Image_ID = img.AD_Image_ID)
                    WHERE o.IsSOTrx='Y' AND o.IsReturnTrx='N' AND o.DocStatus IN ('CO', 'CL') AND o.AD_Client_ID = " + ctx.GetAD_Client_ID()
                    + @" AND o.DateInvoiced >= " + startdate + " AND o.DateInvoiced < " + enddate, "ol",
                    MRole.SQL_FULLYQUALIFIED, MRole.SQL_RO) + @" GROUP BY ol.M_Product_ID, p.Name, NVL(u.UOMSymbol, u.X12DE355), img.ImageUrl 
                    HAVING NVL(SUM(NVL(ol.LineNetAmt,0)),0)>0 )");

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
            sb.Append(@", last_yearReturn AS ( " + MRole.GetDefault(ctx).AddAccessSQL(@" SELECT
                                 SUM(NVL(currencyconvert(
                                  il.LineNetAmt, l.c_currency_id, " + C_Currency_ID + @", l.dateacct, l.c_conversiontype_id, l.AD_Client_id, l.ad_org_id
                                  ), 0)) AS LastYearReturnAmount,
                                   il.m_product_id,
                                   p.name
                                   FROM C_InvoiceLine il
                                   INNER JOIN C_Invoice l ON il.C_Invoice_ID = l.C_Invoice_ID
                                   INNER JOIN m_product p ON il.m_product_id = p.m_product_id
                                   INNER JOIN M_InOutLine dl ON dl.C_OrderLine_ID = il.C_OrderLine_ID
                                   INNER JOIN M_InOut d ON d.M_InOut_ID = dl.M_InOut_ID AND d.ISSoTrx = 'Y' AND d.IsReturnTrx = 'Y'", "il", MRole.SQL_FULLYQUALIFIED, MRole.SQL_RO) + @"
                                  AND l.IsSOTrx='Y' AND l.IsReturnTrx='Y' AND
                                 l.docstatus IN('CO', 'CL') 
                                 AND l.DateInvoiced >= ").Append(startdate).Append(@"
                                 AND l.DateInvoiced <= ").Append(enddate).Append(@"
                                 GROUP BY il.M_Product_ID, p.Name
                                 HAVING SUM(il.LineNetAmt)  > 0)
                                 ");
            sb.Append(", previous_year AS(" + MRole.GetDefault(ctx).AddAccessSQL(@"SELECT ol.M_Product_ID, SUM(NVL(currencyConvert(ol.LineNetAmt, 
                    o.C_Currency_ID, " + C_Currency_ID + @", o.DateAcct, o.C_ConversionType_ID, o.AD_Client_ID, o.AD_Org_ID), 0)) AS LineTotalAmt,
                    SUM(ol.QtyInvoiced) AS PreviousQty FROM C_InvoiceLine ol INNER JOIN C_Invoice o ON (ol.C_Invoice_ID = o.C_Invoice_ID)
                    WHERE o.IsSOTrx='Y' AND o.IsReturnTrx='N' AND o.DocStatus IN('CO', 'CL') AND o.AD_Client_ID = " + ctx.GetAD_Client_ID()
                    + @" AND o.DateInvoiced >= " + startdate + " AND o.DateInvoiced < " + enddate, "ol",
                    MRole.SQL_FULLYQUALIFIED, MRole.SQL_RO) + @" GROUP BY ol.M_Product_ID)
                    SELECT cy.M_Product_ID, cy.Name, cy.ImageUrl, NVL(py.LineTotalAmt, 0)-NVL(lyr.LastYearReturnAmount,0) AS PreviousTotal, NVL(cy.LineTotalAmt,0)-NVL(cyr.ReturnAmount,0) AS CurrentTotal,
                    cy.UOM, cy.CurrentQty, py.PreviousQty
                    FROM current_year cy LEFT JOIN previous_year py ON (cy.M_Product_ID = py.M_Product_ID)
                    LEFT JOIN current_yearReturn cyr ON cyr.M_Product_ID = cy.M_Product_ID
                    LEFT JOIN last_yearReturn lyr ON lyr.M_Product_ID = cy.M_Product_ID
                     ORDER BY CurrentTotal ASC
                     FETCH FIRST 10 ROWS ONLY");
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
                                     AND AD_OrgTrx_ID IS NOT NULL AND  IsSOTrx='Y' AND IsReturnTrx='N' 
                                     AND DateInvoiced >= " + startdate + @"
                                     AND DateInvoiced <=" + enddate + ")";
            }
            else
            {
                startdate = "TRUNC(SYSDATE, 'YEAR')";
                enddate = "TRUNC(ADD_MONTHS(SYSDATE, 12), 'YEAR')";
                sqlWhereForLookup = @" AD_Org.IsActive = 'Y' AND (AD_Org.IsProfitCenter = 'Y') AND AD_Org.LegalEntityOrg = " + ctx.GetAD_Org_ID() + " AND AD_Org_ID IN ( " +
                                     MRole.GetDefault(ctx).AddAccessSQL("SELECT AD_OrgTrx_ID FROM C_Invoice", "C_Invoice", MRole.SQL_FULLYQUALIFIED, MRole.SQL_RO) + @"
                                       AND AD_OrgTrx_ID IS NOT NULL AND  IsSOTrx='Y' AND IsReturnTrx='N'
                                     AND DateInvoiced >= " + startdate + @"
                                     AND DateInvoiced <=" + enddate + ")";

            }
            queryBuilder.Append(@"WITH current_yearReturn AS ( " + MRole.GetDefault(ctx).AddAccessSQL(@" SELECT
                                 SUM(NVL(currencyconvert(
                                  il.LineNetAmt, l.c_currency_id, " + C_Currency_ID + @", l.dateacct, l.c_conversiontype_id, l.AD_Client_id, l.ad_org_id
                                  ), 0)) AS ReturnAmount,
                                   il.m_product_id,
                                   p.name
                                   FROM C_InvoiceLine il
                                   INNER JOIN C_Invoice l ON il.C_Invoice_ID = l.C_Invoice_ID
                                   INNER JOIN m_product p ON il.m_product_id = p.m_product_id
                                   INNER JOIN M_InOutLine dl ON dl.C_OrderLine_ID = il.C_OrderLine_ID
                                   INNER JOIN M_InOut d ON d.M_InOut_ID = dl.M_InOut_ID AND d.ISSoTrx = 'Y' AND d.IsReturnTrx = 'Y'", "il", MRole.SQL_FULLYQUALIFIED, MRole.SQL_RO) + @"
                                  AND l.IsSOTrx='Y' AND l.IsReturnTrx='Y' AND
                                 l.docstatus IN('CO', 'CL') " + WhereCondition + @"
                                 AND l.DateInvoiced >= ").Append(startdate).Append(@"
                                 AND l.DateInvoiced <= ").Append(enddate).Append(@"
                                 GROUP BY il.M_Product_ID, p.Name
                                 HAVING SUM(il.LineNetAmt)  > 0)
                                 ,");
            queryBuilder.Append(@"current_year AS ( " + MRole.GetDefault(ctx).AddAccessSQL(@"SELECT
                                  SUM(nvl(
                                 currencyconvert(
                                 il.LineNetAmt, l.c_currency_id, " + C_Currency_ID + @", l.dateacct, l.c_conversiontype_id, l.AD_Client_id, l.ad_org_id
                                 ), 0)) AS linetotalamt,
                                il.m_product_id,
                                 p.name
                                  FROM  C_InvoiceLine il
                                 INNER JOIN C_Invoice l ON(il.C_Invoice_ID = l.C_Invoice_ID)
                                 INNER JOIN m_product p ON(il.m_product_id = p.m_product_id)
                                 INNER JOIN M_InOutLine dl ON(dl.C_OrderLine_ID=il.C_OrderLine_ID)
                                 INNER JOIN M_InOut d ON(d.M_InOut_ID=dl.M_InOut_ID AND d.ISSoTrx='Y' AND d.IsReturnTrx='N')
                                 LEFT JOIN ad_image img ON(p.ad_image_id = img.ad_image_id)", "il", MRole.SQL_FULLYQUALIFIED, MRole.SQL_RO) + @"
                                  AND l.IsSOTrx='Y' AND l.IsReturnTrx='N' AND
                                 l.docstatus IN('CO', 'CL') " + WhereCondition + @"
                                 AND l.DateInvoiced >= ").Append(startdate).Append(@"
                                 AND l.DateInvoiced <= ").Append(enddate).Append(@"
                                 GROUP BY il.M_Product_ID, p.Name
                                 HAVING SUM(il.LineNetAmt) > 0
                                ),");
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
            queryBuilder.Append(@"previous_yearReturn AS (" + MRole.GetDefault(ctx).AddAccessSQL(@"SELECT
                                  SUM(NVL(currencyconvert(
                                  il.LineNetAmt, l.c_currency_id, " + C_Currency_ID + @", l.dateacct, l.c_conversiontype_id, l.AD_Client_id, l.ad_org_id
                                  ), 0)) AS LastYearReturnAmount,
                                  il.m_product_id,
                                   p.name
                                   FROM C_InvoiceLine il
                                   INNER JOIN C_Invoice l ON il.C_Invoice_ID = l.C_Invoice_ID
                                    INNER JOIN m_product p ON il.m_product_id = p.m_product_id
                                   INNER JOIN M_InOutLine dl ON dl.C_OrderLine_ID = il.C_OrderLine_ID
                                   INNER JOIN M_InOut d ON d.M_InOut_ID = dl.M_InOut_ID AND d.ISSoTrx = 'Y' AND d.IsReturnTrx = 'Y'", "il", MRole.SQL_FULLYQUALIFIED, MRole.SQL_RO) + @"   
                                   AND l.IsSOTrx='Y' AND l.IsReturnTrx='Y' AND
                                 l.docstatus IN ('CO', 'CL') " + WhereCondition + @"
                                 AND l.DateInvoiced >= ").Append(startdate).Append(@"
                                 AND l.DateInvoiced <= ").Append(enddate).Append(@"
                                 GROUP BY il.m_product_id,p.Name ),");
            queryBuilder.Append(@"previous_year AS (" + MRole.GetDefault(ctx).AddAccessSQL(@"SELECT 
                                 il.M_Product_ID,
                                 SUM(NVL(currencyConvert(il.LineNetAmt,
                                 l.C_Currency_ID, " + C_Currency_ID + @", l.DateAcct, l.C_ConversionType_ID, l.AD_Client_ID, l.AD_Org_ID), 0)) AS LineTotalAmt
                                 FROM  C_InvoiceLine il
                                 INNER JOIN C_Invoice l ON(il.C_Invoice_ID = l.C_Invoice_ID)", "il", MRole.SQL_FULLYQUALIFIED, MRole.SQL_RO) + @"   
                                   AND l.IsSOTrx='Y' AND l.IsReturnTrx='N' AND
                                 l.docstatus IN ('CO', 'CL') " + WhereCondition + @"
                                 AND l.DateInvoiced >= ").Append(startdate).Append(@"
                                 AND l.DateInvoiced <= ").Append(enddate).Append(@"
                                 GROUP BY il.m_product_id )");
            queryBuilder.Append(@"SELECT  cy.M_Product_ID,  cy.Name,
                                  NVL(py.LineTotalAmt, 0)-NVL(lyr.LastYearReturnAmount,0) AS PreviousTotal, 
                                 cy.LineTotalAmt-NVL(cyr.ReturnAmount,0) AS CurrentTotal
                                  FROM current_year cy 
                                  LEFT JOIN previous_year py ON cy.M_Product_ID = py.M_Product_ID
                                  LEFT JOIN current_yearReturn cyr ON cyr.M_Product_ID = cy.M_Product_ID
                                 LEFT JOIN previous_yearReturn lyr ON lyr.M_Product_ID = cy.M_Product_ID
                                  ORDER BY CurrentTotal " + Type + @"
                                  FETCH FIRST 10 ROWS ONLY");
            DataSet ds = DB.ExecuteDataset(queryBuilder.ToString(), null, null);
            if (ds != null && ds.Tables.Count > 0 && ds.Tables[0].Rows.Count > 0)
            {
                List<Dictionary<string, object>> ProductList = new List<Dictionary<string, object>>();
                for (int i = 0; i < ds.Tables[0].Rows.Count; i++)
                {
                    Dictionary<string, object> retDic = new Dictionary<string, object>();
                    retDic["Product_ID"] = Util.GetValueOfInt(ds.Tables[0].Rows[i]["M_Product_ID"]);
                    retDic["ProductName"] = Util.GetValueOfString(ds.Tables[0].Rows[i]["Name"]);
                    retDic["CurrentTotal"] = Util.GetValueOfDecimal(ds.Tables[0].Rows[i]["CurrentTotal"]);
                    retDic["PreviousTotal"] = Util.GetValueOfDecimal(ds.Tables[0].Rows[i]["PreviousTotal"]);
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
                                 AND i.IsSOTrx='Y' AND i.IsReturnTrx='N' AND
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
                                 AND i.IsSOTrx='Y' AND i.IsReturnTrx='N' AND
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
                               AND i.IsSOTrx='Y' AND i.IsReturnTrx='N' AND
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
                                AND i.IsSOTrx='Y' AND i.IsReturnTrx='N'  AND   i.DocStatus IN ('CO', 'CL') {0}  
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

        /// <summary>
        /// VAI050-Get the list of expected sales order
        /// </summary>
        /// <param name="ctx"></param>
        /// <param name="pageNo"></param>
        /// <param name="pageSize"></param>
        /// <returns></returns>
        public DeliveryResult GetExpectedDelivery(Ctx ctx, int pageNo, int pageSize, string Type)
        {
            bool isAllownonItem = Util.GetValueOfString(ctx.GetContext("$AllowNonItem")).Equals("Y");
            string WhereCondition = "";
            if (Type == "PD") //Pending Delivery Order
            {
                WhereCondition = @" AND o.DatePromised < TRUNC(CURRENT_DATE) AND o.IsSoTrx = 'Y' AND o.IsReturnTrx = 'N' AND o.IsSalesQuotation = 'N' AND o.IsBlanketTrx = 'N' ";
            }
            else if (Type == "EG") //Expected GRN
            {
                WhereCondition = " AND o.DatePromised >=TRUNC(CURRENT_DATE) AND o.IsSoTrx = 'N' AND o.IsReturnTrx = 'N' AND o.IsSalesQuotation = 'N' AND o.IsBlanketTrx = 'N' ";
            }
            else if (Type == "PG") //Pending GRN
            {
                WhereCondition = " AND  o.DatePromised <TRUNC(CURRENT_DATE) AND o.IsSoTrx = 'N' AND o.IsReturnTrx = 'N' AND o.IsSalesQuotation = 'N' AND o.IsBlanketTrx = 'N' ";
            }
            else if (Type == "CR") //Customer RMA
            {
                WhereCondition = " AND o.IsSoTrx = 'Y' AND o.IsReturnTrx = 'Y' ";
            }
            else if (Type == "VR") //Vendor RMA
            {
                WhereCondition = " AND o.IsSoTrx = 'N' AND o.IsReturnTrx = 'Y' ";
            }
            else if (Type == "ED") //Expected Delivery Order
            {
                WhereCondition = @" AND o.DatePromised >= TRUNC(CURRENT_DATE) AND o.IsSOTrx = 'Y' AND o.IsReturnTrx = 'N' AND o.IsSalesQuotation = 'N' AND o.IsBlanketTrx = 'N' ";
            }
            else if (Type == "OQ") //Open Sales Quotations
            {
                WhereCondition = @" AND o.IsSOTrx = 'Y' AND o.IsReturnTrx = 'N' AND o.IsSalesQuotation = 'Y' AND NVL(ol.Ref_OrderLine_ID, 0) = 0";
            }
            DeliveryResult result = new DeliveryResult
            {
                Orders = new List<ParentOrder>()
            };
            StringBuilder sb = new StringBuilder();
            sb.Append(MRole.GetDefault(ctx).AddAccessSQL(@"SELECT o.C_Order_ID, o.DocumentNo, o.DateOrdered,
                    COUNT(ol.C_OrderLine_ID) AS LineCount,
                    NVL(currencyConvert(o.GrandTotal,o.C_Currency_ID, " + ctx.GetContextAsInt("$C_Currency_ID") +
                    @", o.DateAcct, o.C_ConversionType_ID, o.AD_Client_ID, o.AD_Org_ID), 0) AS GrandTotal,
                    w.Name AS ProductLocation,l.Name AS Deliverylocation,cb.Name AS CustomerName
                    FROM C_Order o INNER JOIN C_OrderLine ol ON o.C_Order_ID = ol.C_Order_ID" +
                    (!isAllownonItem ? " INNER JOIN M_Product p ON ol.M_Product_ID = p.M_Product_ID AND p.ProductType = 'I'" : "") +
                    @" INNER JOIN M_WareHouse w ON( w.M_WareHouse_ID=o.M_WareHouse_ID)
                    INNER JOIN C_BPartner cb ON(cb.C_BPartner_ID=o.C_BPartner_ID)
                    INNER JOIN C_BPartner_Location l  ON (l.C_BPartner_Location_ID=o.C_BPartner_Location_ID)", "o", MRole.SQL_FULLYQUALIFIED, MRole.SQL_RO) + @"
                    AND o.DocStatus IN('CO')  " + WhereCondition + @"
                    AND (ol.QtyOrdered - ol.QtyDelivered - (SELECT NVL(SUM(il.MovementQty), 0) FROM M_Inout i INNER JOIN M_InoutLine il ON i.M_Inout_ID = il.M_Inout_ID
                    WHERE il.C_OrderLine_ID = ol.C_OrderLine_ID AND il.IsActive = 'Y' AND i.DocStatus NOT IN ('RE', 'VO', 'CL', 'CO')) > 0)
                    GROUP BY o.C_Order_ID, o.DocumentNo, o.DateOrdered, w.Name, l.Name, cb.Name, o.DatePromised, o.GrandTotal, o.C_Currency_ID, 
                    o.DateAcct, o.C_ConversionType_ID, o.AD_Client_ID, o.AD_Org_ID
                    ORDER BY o.DatePromised DESC");

            DataSet ds = DB.ExecuteDataset(sb.ToString(), null, null, pageSize, pageNo);
            if (ds != null && ds.Tables.Count > 0 && ds.Tables[0].Rows.Count > 0)
            {
                if (pageNo == 1)
                {
                    result.RecordCount = Util.GetValueOfInt(DB.ExecuteScalar("SELECT COUNT(C_Order_ID) FROM (" + sb.ToString() + ") t ", null, null));
                    if (Type == "EG" || Type == "PG")
                    {
                        result.AD_Window_ID = Util.GetValueOfInt(DB.ExecuteScalar("SELECT AD_Window_ID FROM AD_Window WHERE Name='VAS_MaterialReceipt'", null, null));
                    }
                    else if (Type == "CR")
                    {
                        result.AD_Window_ID = Util.GetValueOfInt(DB.ExecuteScalar("SELECT AD_Window_ID FROM AD_Window WHERE Name='VAS_CustomerReturn'", null, null));
                    }
                    else if (Type == "VR")
                    {
                        result.AD_Window_ID = Util.GetValueOfInt(DB.ExecuteScalar("SELECT AD_Window_ID FROM AD_Window WHERE Name='VAS_VendorReturn'", null, null));
                    }
                    else if (Type == "ED" || Type == "PD")
                    {
                        result.AD_Window_ID = Util.GetValueOfInt(DB.ExecuteScalar("SELECT AD_Window_ID FROM AD_Window WHERE Name='VAS_DeliveryOrder'", null, null));
                    }
                    else if (Type == "OQ")
                    {
                        result.AD_Window_ID = Util.GetValueOfInt(DB.ExecuteScalar("SELECT AD_Window_ID FROM AD_Window WHERE Name='VAS_SalesOrder'", null, null));
                    }
                }
                // Get the list of order IDs from the retrieved parent records
                // Extract order IDs
                List<int> orderIds = ds.Tables[0].AsEnumerable()
                    .Select(row => Util.GetValueOfInt(row["C_Order_ID"]))
                    .ToList();

                if (orderIds.Count > 0)
                {
                    sb.Clear();
                    sb.Append(@"SELECT * FROM (SELECT ol.C_Order_ID,ol.C_OrderLine_ID, ol.M_Product_ID, 0 AS C_Charge_ID, ol.M_AttributeSetInstance_ID,
                    (ol.QtyOrdered-ol.QtyDelivered-(SELECT NVL(SUM(MovementQty),0) FROM M_Inout i INNER JOIN M_InoutLine il ON (i.M_Inout_ID = il.M_Inout_ID)
                    WHERE il.C_OrderLine_ID =ol.C_OrderLine_ID AND il.IsActive = 'Y' AND i.DocStatus NOT IN ('RE', 'VO', 'CL', 'CO'))) AS QtyOrdered, 
                    ol.C_UOM_ID, atr.Description AS AttributeName, u.Name AS Uom, p.Name As ProductName, p.ProductType, (SELECT NVL(SUM(s.QtyOnHand),0) FROM M_Storage s 
                    INNER JOIN M_Locator loc ON(loc.M_Locator_ID=s.M_Locator_ID AND loc.M_WareHouse_ID=o.M_WareHouse_ID)
                    WHERE NVL(s.M_Product_ID,0)=NVL(ol.M_Product_ID,0) AND NVL(s.M_AttributeSetInstance_ID,0)=NVL(ol.M_AttributeSetInstance_ID,0)) AS OnHandQty,
                    NVL((ol.QtyOrdered / NULLIF(ol.QtyEntered, 0)),0) AS ConversionRate
                    FROM C_OrderLine ol INNER JOIN C_Order o ON ol.C_Order_ID = o.C_Order_ID
                    INNER JOIN M_Product p ON (ol.M_Product_ID=p.M_Product_ID)
                    LEFT JOIN C_UOM u ON (ol.C_UOM_ID=u.C_UOM_ID)                    
                    LEFT JOIN  M_AttributeSetInstance atr ON(ol.M_AttributeSetInstance_ID=atr.M_AttributeSetInstance_ID)
                    WHERE (ol.QtyOrdered - ol.QtyDelivered - (SELECT NVL(SUM(il.MovementQty), 0) FROM M_Inout i INNER JOIN M_InoutLine il ON (i.M_Inout_ID = il.M_Inout_ID)
                    WHERE il.C_OrderLine_ID = ol.C_OrderLine_ID AND il.IsActive = 'Y' AND i.DocStatus NOT IN ('RE', 'VO', 'CL', 'CO')) > 0) AND
                    ol.C_Order_ID IN (" + string.Join(",", orderIds) + @")" + (!isAllownonItem ? " AND p.ProductType='I'" : ""));

                    if (isAllownonItem)
                    {
                        sb.Append(@" UNION SELECT ol.C_Order_ID,ol.C_OrderLine_ID, 0 AS M_Product_ID, ol.C_Charge_ID, ol.M_AttributeSetInstance_ID,
                        (ol.QtyOrdered-ol.QtyDelivered-(SELECT NVL(SUM(MovementQty),0) FROM M_Inout i INNER JOIN M_InoutLine il ON (i.M_Inout_ID = il.M_Inout_ID)
                        WHERE il.C_OrderLine_ID =ol.C_OrderLine_ID AND il.IsActive = 'Y' AND i.DocStatus NOT IN ('RE', 'VO', 'CL', 'CO'))) AS QtyOrdered, 
                        ol.C_UOM_ID, atr.Description AS AttributeName, u.Name AS Uom, c.Name As ProductName, 'C' AS ProductType, 0 AS OnHandQty,
                        NVL((ol.QtyOrdered / NULLIF(ol.QtyEntered, 0)),0) AS ConversionRate
                        FROM C_OrderLine ol INNER JOIN C_Order o ON ol.C_Order_ID = o.C_Order_ID
                        INNER JOIN C_Charge c ON (ol.C_Charge_ID=c.C_Charge_ID)
                        LEFT JOIN C_UOM u ON (ol.C_UOM_ID=u.C_UOM_ID)
                        LEFT JOIN  M_AttributeSetInstance atr ON(ol.M_AttributeSetInstance_ID=atr.M_AttributeSetInstance_ID)
                        WHERE (ol.QtyOrdered - ol.QtyDelivered - (SELECT NVL(SUM(il.MovementQty), 0) FROM M_Inout i INNER JOIN M_InoutLine il ON (i.M_Inout_ID = il.M_Inout_ID)
                        WHERE il.C_OrderLine_ID = ol.C_OrderLine_ID AND il.IsActive = 'Y' AND i.DocStatus NOT IN ('RE', 'VO', 'CL', 'CO')) > 0) AND
                        ol.C_Order_ID IN (" + string.Join(",", orderIds) + @")");
                    }
                    sb.Append(") t ORDER BY C_Order_ID");

                    DataSet childDs = DB.ExecuteDataset(sb.ToString(), null, null);
                    if (childDs != null && childDs.Tables.Count > 0 && childDs.Tables[0].Rows.Count > 0)
                    {
                        /// Map order lines to parent orders
                        Dictionary<int, List<OrderLine>> orderLinesMap = childDs.Tables[0].AsEnumerable()
                            .GroupBy(row => Util.GetValueOfInt(row["C_Order_ID"]))
                            .ToDictionary(
                                group => group.Key,
                                group => group.Select(row => new OrderLine
                                {
                                    QtyEntered = Util.GetValueOfDecimal(row["ConversionRate"]) == 0 ? 0
                                     : Util.GetValueOfDecimal(row["QtyOrdered"]) / Util.GetValueOfDecimal(row["ConversionRate"]), // Remaining qty in line uom
                                    M_Product_ID = Util.GetValueOfInt(row["M_Product_ID"]),
                                    C_OrderLine_ID = Util.GetValueOfInt(row["C_OrderLine_ID"]),
                                    C_Order_ID = Util.GetValueOfInt(row["C_Order_ID"]),
                                    M_AttributeSetInstance_ID = Util.GetValueOfInt(row["M_AttributeSetInstance_ID"]),
                                    QtyOrdered = Util.GetValueOfDecimal(row["QtyOrdered"]),
                                    C_UOM_ID = Util.GetValueOfInt(row["C_UOM_ID"]),
                                    AttributeName = Util.GetValueOfString(row["AttributeName"]),
                                    UOM = Util.GetValueOfString(row["Uom"]),
                                    ProductName = Util.GetValueOfString(row["ProductName"]),
                                    ProductType = Util.GetValueOfString(row["ProductType"]),
                                    OnHandQty = Util.GetValueOfDecimal(row["OnHandQty"])
                                }).ToList()
                            );

                        string Symbol = "$";
                        int StdPrecision = 2;
                        sb.Clear();
                        sb.Append(@"SELECT CASE WHEN Cursymbol IS NOT NULL THEN Cursymbol ELSE ISO_Code END AS Symbol, StdPrecision 
                                         FROM C_Currency WHERE C_Currency_ID=" + ctx.GetContextAsInt("$C_Currency_ID"));
                        DataSet dsCurrency = DB.ExecuteDataset(sb.ToString(), null, null);
                        if (dsCurrency != null && dsCurrency.Tables.Count > 0 && dsCurrency.Tables[0].Rows.Count > 0)
                        {
                            Symbol = Util.GetValueOfString(dsCurrency.Tables[0].Rows[0]["Symbol"]);
                            StdPrecision = Util.GetValueOfInt(dsCurrency.Tables[0].Rows[0]["StdPrecision"]);
                        }
                        // Step 4: Associate child records with their parent orders
                        foreach (DataRow parentRow in ds.Tables[0].Rows)
                        {
                            int orderId = Util.GetValueOfInt(parentRow["C_Order_ID"]);
                            ParentOrder parentOrder = new ParentOrder
                            {
                                DocumentNo = Util.GetValueOfString(parentRow["DocumentNo"]),
                                C_Order_ID = Util.GetValueOfInt(parentRow["C_Order_ID"]),
                                DateOrdered = Util.GetValueOfDateTime(parentRow["DateOrdered"]),
                                GrandTotal = Util.GetValueOfDecimal(parentRow["GrandTotal"]),
                                LineCount = Util.GetValueOfDecimal(parentRow["LineCount"]),
                                DeliveryLocation = Util.GetValueOfString(parentRow["Deliverylocation"]),
                                ProductLocation = Util.GetValueOfString(parentRow["ProductLocation"]),
                                CustomerName = Util.GetValueOfString(parentRow["CustomerName"]),
                                Symbol = Symbol,
                                StdPrecision = StdPrecision,
                                OrderLines = orderLinesMap.ContainsKey(orderId) ? orderLinesMap[orderId] : new List<OrderLine>()
                            };
                            result.Orders.Add(parentOrder);
                        }
                    }
                }
            }
            else
            {
                return null;
            }
            return result;
        }

        /// <summary>
        /// VAI050-Get the list of expected Requisition
        /// </summary>
        /// <param name="ctx"></param>
        /// <param name="pageNo"></param>
        /// <param name="pageSize"></param>
        /// <returns></returns>
        public dynamic GetExpectedTransfer(Ctx ctx, int pageNo, int pageSize, string Type)
        {
            string WhereCondition = "";
            if (Type.Contains("P")) //Pending Material Transfer
            {
                WhereCondition = " AND r.DateRequired < TRUNC(CURRENT_DATE) ";
            }
            else //Expected Material Transfer
            {
                WhereCondition = " AND r.DateRequired >= TRUNC(CURRENT_DATE) ";
            }
            dynamic result = new ExpandoObject();
            result.Requisitions = new List<dynamic>();
            StringBuilder sb = new StringBuilder();
            sb.Append(@"" + MRole.GetDefault(ctx).AddAccessSQL(@"SELECT r.M_Requisition_ID, r.DocumentNo, r.DateDoc,
                    COUNT(rl.M_RequisitionLine_ID) AS LineCount, r.TotalLines AS GrandTotal, sw.Name AS Source, w.Name AS Destination, 
                    cb.Name AS Employee, CASE WHEN c.Cursymbol IS NOT NULL THEN c.Cursymbol ELSE c.ISO_Code END AS Symbol, c.StdPrecision
                    FROM M_Requisition r INNER JOIN M_RequisitionLine rl ON (r.M_Requisition_ID = rl.M_Requisition_ID)
                    INNER JOIN M_WareHouse sw ON (r.DTD001_MWarehouseSource_ID = sw.M_WareHouse_ID)
                    INNER JOIN M_WareHouse w ON (r.M_WareHouse_ID = w.M_WareHouse_ID)
                    INNER JOIN M_PriceList p ON (r.M_PriceList_ID = p.M_PriceList_ID)
                    INNER JOIN C_Currency c ON (p.C_Currency_ID = c.C_Currency_ID)
                    LEFT JOIN C_BPartner cb ON (cb.C_BPartner_ID = r.C_BPartner_ID)", "r", MRole.SQL_FULLYQUALIFIED, MRole.SQL_RO) + @"
                    AND r.DocStatus IN ('CO')" + WhereCondition + @"AND (rl.Qty - rl.DTD001_ReservedQty - rl.DTD001_DeliveredQty) > 0
                    GROUP BY r.M_Requisition_ID, r.DocumentNo, r.DateDoc, r.TotalLines, sw.Name, w.Name, cb.Name, CASE WHEN c.Cursymbol IS NOT NULL 
                    THEN c.Cursymbol ELSE c.ISO_Code END, c.StdPrecision ORDER BY r.DateDoc DESC");

            DataSet ds = DB.ExecuteDataset(sb.ToString(), null, null, pageSize, pageNo);
            if (ds != null && ds.Tables.Count > 0 && ds.Tables[0].Rows.Count > 0)
            {
                if (pageNo == 1)
                {
                    result.RecordCount = Util.GetValueOfInt(DB.ExecuteScalar("SELECT COUNT(M_Requisition_ID) FROM (" + sb.ToString() + ") t ", null, null));
                }

                // Get the list of Requisitions IDs from the retrieved parent records
                // Extract Requisition IDs
                List<int> reqIDs = ds.Tables[0].AsEnumerable().Select(row => Util.GetValueOfInt(row["M_Requisition_ID"])).ToList();
                if (reqIDs.Count > 0)
                {
                    StringBuilder childSb = new StringBuilder(@"
                    SELECT rl.M_Requisition_ID,rl.M_RequisitionLine_ID, rl.M_Product_ID, rl.M_AttributeSetInstance_ID,
                    (rl.Qty - rl.DTD001_ReservedQty - rl.DTD001_DeliveredQty) AS QtyOrdered, rl.C_UOM_ID,
                    atr.Description AS AttributeName, u.Name AS Uom, p.Name As ProductName, (SELECT NVL(SUM(s.QtyOnHand),0) FROM M_Storage s 
                    INNER JOIN M_Locator loc ON (loc.M_Locator_ID=s.M_Locator_ID AND loc.M_WareHouse_ID=r.DTD001_MWarehouseSource_ID)
                    WHERE NVL(s.M_Product_ID,0)=NVL(rl.M_Product_ID,0) AND NVL(s.M_AttributeSetInstance_ID,0)=NVL(rl.M_AttributeSetInstance_ID,0)) AS OnHandQty ,
                    NVL((rl.Qty / NULLIF(rl.QtyEntered, 0)),0) AS ConversionRate
                    FROM M_RequisitionLine rl INNER JOIN  M_Requisition r ON (rl.M_Requisition_ID=r.M_Requisition_ID)
                    INNER JOIN M_Product p ON (rl.M_Product_ID=p.M_Product_ID) LEFT JOIN C_UOM u ON (rl.C_UOM_ID=u.C_UOM_ID) 
                    LEFT JOIN  M_AttributeSetInstance atr ON (rl.M_AttributeSetInstance_ID=atr.M_AttributeSetInstance_ID)
                    WHERE (rl.Qty - rl.DTD001_ReservedQty - rl.DTD001_DeliveredQty) > 0 AND
                    rl.M_Requisition_ID IN (" + string.Join(",", reqIDs) + @") ORDER BY rl.M_Requisition_ID");

                    DataSet childDs = DB.ExecuteDataset(childSb.ToString(), null, null);
                    if (childDs != null && childDs.Tables.Count > 0 && childDs.Tables[0].Rows.Count > 0)
                    {
                        /// Map Requisition lines to parent Requisitions
                        dynamic reqLine;
                        Dictionary<int, List<dynamic>> ReqLinesMap = childDs.Tables[0].AsEnumerable()
                            .GroupBy(row => Util.GetValueOfInt(row["M_Requisition_ID"]))
                            .ToDictionary(
                                group => group.Key,
                                group => group.Select(row =>
                                {
                                    reqLine = new ExpandoObject();
                                    reqLine.QtyEntered = Util.GetValueOfDecimal(row["ConversionRate"]) == 0 ? 0
                                     : Util.GetValueOfDecimal(row["QtyOrdered"]) / Util.GetValueOfDecimal(row["ConversionRate"]); // Remaining qty in line uom
                                    reqLine.M_Product_ID = Util.GetValueOfInt(row["M_Product_ID"]);
                                    reqLine.M_RequisitionLine_ID = Util.GetValueOfInt(row["M_RequisitionLine_ID"]);
                                    reqLine.M_Requisition_ID = Util.GetValueOfInt(row["M_Requisition_ID"]);
                                    reqLine.M_AttributeSetInstance_ID = Util.GetValueOfInt(row["M_AttributeSetInstance_ID"]);
                                    reqLine.QtyOrdered = Util.GetValueOfDecimal(row["QtyOrdered"]);
                                    reqLine.C_UOM_ID = Util.GetValueOfInt(row["C_UOM_ID"]);
                                    reqLine.AttributeName = Util.GetValueOfString(row["AttributeName"]);
                                    reqLine.UOM = Util.GetValueOfString(row["Uom"]);
                                    reqLine.ProductName = Util.GetValueOfString(row["ProductName"]);
                                    reqLine.OnHandQty = Util.GetValueOfDecimal(row["OnHandQty"]);
                                    return reqLine;
                                }).ToList()
                            );

                        // Step 4: Associate child records with their parent requisitions
                        dynamic requisition;
                        int reqId;
                        foreach (DataRow parentRow in ds.Tables[0].Rows)
                        {
                            reqId = Util.GetValueOfInt(parentRow["M_Requisition_ID"]);
                            requisition = new ExpandoObject();
                            requisition.DocumentNo = Util.GetValueOfString(parentRow["DocumentNo"]);
                            requisition.M_Requisition_ID = Util.GetValueOfInt(parentRow["M_Requisition_ID"]);
                            requisition.DateDoc = Util.GetValueOfDateTime(parentRow["DateDoc"]);
                            requisition.GrandTotal = Util.GetValueOfDecimal(parentRow["GrandTotal"]);
                            requisition.LineCount = Util.GetValueOfDecimal(parentRow["LineCount"]);
                            requisition.Source = Util.GetValueOfString(parentRow["Source"]);
                            requisition.Destination = Util.GetValueOfString(parentRow["Destination"]);
                            requisition.Employee = Util.GetValueOfString(parentRow["Employee"]);
                            requisition.Symbol = Util.GetValueOfString(parentRow["Symbol"]);
                            requisition.StdPrecision = Util.GetValueOfInt(parentRow["StdPrecision"]);
                            requisition.ReqLines = ReqLinesMap.ContainsKey(reqId) ? ReqLinesMap[reqId] : new List<dynamic>();
                            result.Requisitions.Add(requisition);
                        }
                    }
                }
            }
            else
            {
                return null;
            }

            return result;
        }

        /// <summary>
        /// VAI050-This method used to create delivery order and Vendor Return
        /// </summary>
        /// <param name="ctx"></param>
        /// <param name="Order_ID"></param>
        /// <param name="OrderLinesIDs"></param>
        /// <returns></returns>
        public Dictionary<string, object> CreateShipment(Ctx ctx, int Order_ID, string OrderLinesIDs)
        {
            Dictionary<string, object> obj = new Dictionary<string, object>();
            MClient client = MClient.Get(ctx);
            StringBuilder _msg = new StringBuilder();
            DateTime? _movementDate = CommonFunctions.CovertMilliToDate(ctx.GetContextAsTime("#Date"));


            // check container functionality applicable into system or not
            bool isContainerApplicable = MTransaction.ProductContainerApplicable(ctx);
            VAdvantage.DataBase.Trx trx = VAdvantage.DataBase.Trx.Get("VAS_GenerateShipment" + DateTime.Now.Ticks);
            //InOutGenerate obj = new InOutGenerate();

            MOrder order = new MOrder(ctx, Order_ID, trx);

            // Credit Limit check 
            MBPartner bp = MBPartner.Get(ctx, order.GetC_BPartner_ID());
            if (bp.GetCreditStatusSettingOn() == "CH")
            {
                decimal creditLimit = bp.GetSO_CreditLimit();
                string creditVal = bp.GetCreditValidation();
                if (creditLimit != 0)
                {
                    decimal creditAvlb = creditLimit - bp.GetSO_CreditUsed();
                    if (creditAvlb <= 0)
                    {
                        if (creditVal == "B" || creditVal == "D" || creditVal == "E" || creditVal == "F")
                        {
                            //AddLog(Msg.GetMsg(ctx, "StopShipment") + bp.GetName());
                        }
                        else if (creditVal == "H" || creditVal == "J" || creditVal == "K" || creditVal == "L")
                        {
                            if (_msg != null)
                            {
                                _msg.Clear();
                            }
                            _msg.Append(Msg.GetMsg(ctx, "WarningShipment") + bp.GetName());
                            //AddLog(Msg.GetMsg(GetCtx(), "WarningShipment") + bp.GetName());
                        }
                    }
                }
            }
            // JID_0161 // change here now will check credit settings on field only on Business Partner Header // Lokesh Chauhan 15 July 2019
            else if (bp.GetCreditStatusSettingOn() == X_C_BPartner.CREDITSTATUSSETTINGON_CustomerLocation)
            {
                MBPartnerLocation bpl = new MBPartnerLocation(ctx, order.GetC_BPartner_Location_ID(), trx);
                //MBPartner bpartner = MBPartner.Get(GetCtx(), order.GetC_BPartner_ID());
                //if (bpl.GetCreditStatusSettingOn() == "CL")
                //{
                decimal creditLimit = bpl.GetSO_CreditLimit();
                string creditVal = bpl.GetCreditValidation();
                if (creditLimit != 0)
                {
                    decimal creditAvlb = creditLimit - bpl.GetSO_CreditUsed();
                    if (creditAvlb <= 0)
                    {
                        if (creditVal == "B" || creditVal == "D" || creditVal == "E" || creditVal == "F")
                        {
                            //AddLog(Msg.GetMsg(GetCtx(), "StopShipment") + bp.GetName() + " " + bpl.GetName());
                        }
                        else if (creditVal == "H" || creditVal == "J" || creditVal == "K" || creditVal == "L")
                        {
                            if (_msg != null)
                            {
                                _msg.Clear();
                            }
                            _msg.Append(Msg.GetMsg(ctx, "WarningShipment") + bp.GetName() + " " + bpl.GetName());
                            //AddLog(Msg.GetMsg(GetCtx(), "WarningShipment") + bp.GetName() + " " + bpl.GetName());
                        }
                    }
                }
                //}
            }
            // Credit Limit End

            DateTime? minGuaranteeDate = _movementDate;

            //	Deadlock Prevention - Order by M_Product_ID
            MOrderLine[] lines = order.GetLines("AND C_OrderLine_ID IN (" + OrderLinesIDs + ")", "ORDER BY C_BPartner_Location_ID, M_Product_ID");
            for (int i = 0; i < lines.Length; i++)
            {
                MOrderLine line = lines[i];
                // if order line is not drop ship type
                if (!line.IsDropShip())
                {

                    Decimal onHand = Env.ZERO;
                    Decimal toDeliver = Decimal.Subtract(line.GetQtyOrdered(),
                        line.GetQtyDelivered());
                    Decimal QtyNotDelivered = Util.GetValueOfDecimal(DB.ExecuteScalar(@"SELECT SUM(MovementQty) FROM M_Inout i INNER JOIN M_InoutLine il ON i.M_Inout_ID = il.M_Inout_ID
                            WHERE il.C_OrderLine_ID = " + line.GetC_OrderLine_ID() + @" AND il.Isactive = 'Y' AND i.docstatus NOT IN ('RE' , 'VO' , 'CL' , 'CO')", null, trx));
                    toDeliver -= QtyNotDelivered;
                    MProduct product = line.GetProduct();
                    //	Nothing to Deliver
                    if (product != null && Env.Signum(toDeliver) == 0)
                    {
                        continue;
                    }

                    //	Stored Product
                    MProductCategory pc = MProductCategory.Get(order.GetCtx(), product.GetM_Product_Category_ID());
                    String MMPolicy = pc.GetMMPolicy();
                    if (MMPolicy == null || MMPolicy.Length == 0)
                    {
                        MMPolicy = client.GetMMPolicy();
                    }
                    //
                    dynamic[] storages = null;
                    if (!isContainerApplicable)
                    {
                        storages = GetStorages(line.GetM_Warehouse_ID(),
                           line.GetM_Product_ID(), line.GetM_AttributeSetInstance_ID(),
                           product.GetM_AttributeSet_ID(),
                           line.GetM_AttributeSetInstance_ID() == 0,
                           (DateTime?)minGuaranteeDate,
                           MClient.MMPOLICY_FiFo.Equals(MMPolicy), ctx, trx);
                    }
                    else
                    {
                        storages = GetContainerStorages(line.GetM_Warehouse_ID(),
                          line.GetM_Product_ID(), line.GetM_AttributeSetInstance_ID(),
                          product.GetM_AttributeSet_ID(),
                          line.GetM_AttributeSetInstance_ID() == 0,
                          (DateTime?)minGuaranteeDate,
                          MClient.MMPOLICY_FiFo.Equals(MMPolicy), false, false, ctx, trx);
                    }

                    for (int j = 0; j < storages.Length; j++)
                    {
                        dynamic storage = storages[j];
                        onHand = Decimal.Add(onHand, isContainerApplicable ? storage.GetQty() : storage.GetQtyOnHand());
                    }
                    bool fullLine = onHand.CompareTo(toDeliver) >= 0
                        || Env.Signum(toDeliver) < 0;
                    ////	Complete Line
                    if (fullLine && MOrder.DELIVERYRULE_CompleteLine.Equals(order.GetDeliveryRule()))
                    {
                        ////log.Fine("CompleteLine - OnHand=" + onHand
                        ////    + " (Unconfirmed=" + unconfirmedShippedQty
                        ////    + ", ToDeliver=" + toDeliver + " - " + line);
                        //	
                        obj = CreateLine(order, line, toDeliver, storages, false, MClient.MMPOLICY_FiFo.Equals(MMPolicy), ctx, trx);
                        if (Util.GetValueOfInt(obj["Shipment_ID"]) == 0)
                        {
                            trx.Rollback();
                            return obj;
                        }

                    }
                    //	Availability
                    else if (MOrder.DELIVERYRULE_Availability.Equals(order.GetDeliveryRule())
                        && (Env.Signum(onHand) > 0
                            || Env.Signum(toDeliver) < 0))
                    {
                        Decimal deliver = toDeliver;
                        if (deliver.CompareTo(onHand) > 0)
                            deliver = onHand;
                        ////log.Fine("Available - OnHand=" + onHand
                        ////    + " (Unconfirmed=" + unconfirmedShippedQty
                        ////    + "), ToDeliver=" + toDeliver
                        ////    + ", Delivering=" + deliver + " - " + line);
                        //	
                        obj = CreateLine(order, line, deliver, storages, false, MClient.MMPOLICY_FiFo.Equals(MMPolicy), ctx, trx);
                        if (Util.GetValueOfInt(obj["Shipment_ID"]) == 0)
                        {
                            trx.Rollback();
                            return obj;
                        }
                    }
                    //	Force
                    else if (MOrder.DELIVERYRULE_Force.Equals(order.GetDeliveryRule()))
                    {
                        Decimal deliver = toDeliver;

                        obj = CreateLine(order, line, deliver, storages, true, MClient.MMPOLICY_FiFo.Equals(MMPolicy), ctx, trx);
                        if (Util.GetValueOfInt(obj["Shipment_ID"]) == 0)
                        {
                            trx.Rollback();
                            return obj;
                        }
                    }
                    else if (MOrder.DELIVERYRULE_Manual.Equals(order.GetDeliveryRule()))
                    {

                        obj["Shipment_ID"] = 0;
                        obj["message"] = Msg.GetMsg(ctx, "VAS_DeliverRuleManual");
                        return obj;
                    }
                }
            }//	for all order lines
            if (Util.GetValueOfInt(obj["Shipment_ID"]) > 0)
            {
                trx.Commit();
            }


            return obj;
        }

        /// <summary>
        /// Create line
        /// </summary>
        /// <param name="order"></param>
        /// <param name="orderLine"></param>
        /// <param name="qty"></param>
        /// <param name="storages"></param>
        /// <param name="force"></param>
        /// <param name="FiFo"></param>
        /// <param name="ctx"></param>
        /// <param name="trx"></param>
        /// <returns></returns>
        public Dictionary<string, object> CreateLine(MOrder order, MOrderLine orderLine, Decimal qty,
            dynamic[] storages, bool force, bool FiFo, Ctx ctx, Trx trx)
        {
            StringBuilder _msg = new StringBuilder();
            ValueNamePair pp = null;
            bool isContainerApplicable = MTransaction.ProductContainerApplicable(ctx);
            int _line = 0;
            Dictionary<string, object> ret = new Dictionary<string, object>();

            //	Create New Shipment
            if (_shipment == null)
            {
                _shipment = new MInOut(order, 0, DateTime.Now);
                _shipment.SetM_Warehouse_ID(orderLine.GetM_Warehouse_ID()); //	sets Org too
                if (order.GetC_BPartner_ID() != orderLine.GetC_BPartner_ID())
                {
                    _shipment.SetC_BPartner_ID(orderLine.GetC_BPartner_ID());
                }
                if (order.GetC_BPartner_Location_ID() != orderLine.GetC_BPartner_Location_ID())
                {
                    _shipment.SetC_BPartner_Location_ID(orderLine.GetC_BPartner_Location_ID());
                }

                if (_shipment.Get_ColumnIndex("C_IncoTerm_ID") > 0)
                {
                    _shipment.SetC_IncoTerm_ID(order.GetC_IncoTerm_ID());
                }
                if (!_shipment.Save())
                {
                    pp = VLogger.RetrieveError();
                    string error = pp != null ? pp.GetName() : "";
                    if (string.IsNullOrEmpty(error))
                    {
                        error = pp != null ? Msg.GetMsg(ctx, pp.GetValue()) : "";
                    }

                    ret["Shipment_ID"] = 0;
                    ret["message"] = !string.IsNullOrEmpty(error) ? error : Msg.GetMsg(ctx, "VAS_DeliveryOrderNotSaved");
                    return ret;
                }
            }

            //	Non Inventory Lines
            if (storages == null || storages.Count() == 0)
            {
                #region Non Inventory Lines
                MInOutLine line = new MInOutLine(_shipment);
                line.SetOrderLine(orderLine, 0, Env.ZERO);
                line.SetQty(qty);	//	Correct UOM
                if (orderLine.GetQtyEntered().CompareTo(orderLine.GetQtyOrdered()) != 0)
                {
                    line.SetQtyEntered(Decimal.Round(Decimal.Divide(Decimal.Multiply(qty,
                        orderLine.GetQtyEntered()), orderLine.GetQtyOrdered()),
                        12, MidpointRounding.AwayFromZero));
                }
                line.SetLine(_line + orderLine.GetLine());

                if (Env.IsModuleInstalled("DTD001_"))
                {
                    line.SetDTD001_IsAttributeNo(true);
                }

                //190 - Set Print Description
                if (line.Get_ColumnIndex("PrintDescription") >= 0)
                    line.Set_Value("PrintDescription", orderLine.Get_Value("PrintDescription"));

                if (!line.Save())
                {
                    pp = VLogger.RetrieveError();
                    string error = pp != null ? pp.GetName() : "";
                    if (string.IsNullOrEmpty(error))
                    {
                        error = pp != null ? Msg.GetMsg(ctx, pp.GetValue()) : "";
                    }

                    ret["Shipment_ID"] = 0;
                    ret["message"] = !string.IsNullOrEmpty(error) ? error : Msg.GetMsg(ctx, "VAS_DeliveryOrderNotSaved");
                    return ret;

                }
                //log.Fine(line.ToString());
                ret["Shipment_ID"] = _shipment.GetM_InOut_ID();
                ret["message"] = _msg;
                return ret;
                #endregion
            }

            //	Product
            MProduct product = orderLine.GetProduct();
            bool linePerASI = false;

            //	Inventory Lines
            if (isContainerApplicable)
            {
                #region Container applicable
                List<MInOutLine> list = new List<MInOutLine>();

                // qty to be delivered
                Decimal toDeliver = qty;

                for (int i = 0; i < storages.Length; i++)
                {
                    if (Env.Signum(toDeliver) == 0)	//	zero deliver
                        continue;

                    dynamic storage = storages[i];

                    #region when data found on container storage
                    Decimal deliver = toDeliver;
                    Decimal containerQty = Util.GetValueOfDecimal(storage.GetQty());
                    int M_ProductContainer_ID = Util.GetValueOfInt(storage.GetM_ProductContainer_ID());
                    // when container qty is less than ZERO, then not to consider that line
                    if (containerQty < 0) continue;

                    //	when deliver qty > container qty, and if system traverse last record of storage loop then make deliver = containerQty
                    if (deliver.CompareTo(containerQty) > 0)
                    {
                        if (!force	//	Adjust to OnHand Qty  
                           || (i + 1 != storages.Length))	//	if force don't adjust last location
                            deliver = containerQty;
                    }

                    if (Env.Signum(deliver) == 0)	//	zero deliver
                    {
                        continue;
                    }

                    int M_Locator_ID = storage.GetM_Locator_ID();

                    //
                    MInOutLine line = null;
                    if (!linePerASI)	//	find line with Locator, AttributeSetInsatnce and ProductContainer
                    {
                        for (int n = 0; n < list.Count; n++)
                        {
                            MInOutLine test = (MInOutLine)list[n];
                            if (test.GetM_Locator_ID() == M_Locator_ID
                                && test.GetM_ProductContainer_ID() == M_ProductContainer_ID
                                && test.GetM_AttributeSetInstance_ID() == storage.GetM_AttributeSetInstance_ID())
                            {
                                line = test;
                                break;
                            }
                        }
                    }
                    if (line == null)	//	new line
                    {
                        line = new MInOutLine(_shipment);
                        line.SetOrderLine(orderLine, M_Locator_ID, order.IsSOTrx() ? deliver : Env.ZERO);
                        line.SetQty(deliver);
                        line.SetM_ProductContainer_ID(M_ProductContainer_ID);
                        list.Add(line);
                    }
                    else
                    {
                        //	existing line
                        line.SetQty(Decimal.Add(line.GetMovementQty(), deliver));
                    }
                    if (orderLine.GetQtyEntered().CompareTo(orderLine.GetQtyOrdered()) != 0)
                    {
                        line.SetQtyEntered(Decimal.Round(Decimal.Divide(Decimal.Multiply(line.GetMovementQty(), orderLine.GetQtyEntered()),
                            orderLine.GetQtyOrdered()), 12, MidpointRounding.AwayFromZero));
                    }

                    line.SetLine(_line + orderLine.GetLine());
                    line.SetM_AttributeSetInstance_ID(storage.GetM_AttributeSetInstance_ID());

                    if (Env.IsModuleInstalled("DTD001_"))
                    {
                        line.SetDTD001_IsAttributeNo(true);
                    }

                    //190 - Set Print Description
                    if (line.Get_ColumnIndex("PrintDescription") >= 0)
                        line.Set_Value("PrintDescription", orderLine.Get_Value("PrintDescription"));

                    if (!line.Save())
                    {
                        //log.Fine("Failed: Could not create Shipment Line against Order No : " + order.GetDocumentNo() + " for orderline id : " + orderLine.GetC_OrderLine_ID());
                        pp = VLogger.RetrieveError();
                        if (pp != null)
                        {
                            _msg.Append(!string.IsNullOrEmpty(pp.GetName()) ? pp.GetName() : pp.GetValue());
                        }
                        continue;
                    }
                    //log.Fine("ToDeliver=" + qty + "/" + deliver + " - " + line);
                    toDeliver = Decimal.Subtract(toDeliver, deliver);
                    //	Temp adjustment
                    //storage.SetQtyOnHand(Decimal.Subtract(storage.GetQtyOnHand(), deliver));
                    storage.SetQty(Decimal.Subtract(storage.GetQty(), deliver));
                    //
                    if (Env.Signum(toDeliver) == 0)
                    {
                        break;
                    }
                    #endregion
                }
                if (Env.Signum(toDeliver) != 0)
                {
                    ret["Shipment_ID"] = 0;
                    ret["message"] = Msg.GetMsg(ctx, "VAS_StockNotAvailable");
                    return ret;
                }

                #endregion
            }
            else
            {
                #region normal Flow
                List<MInOutLine> list = new List<MInOutLine>();
                Decimal toDeliver = qty;
                for (int i = 0; i < storages.Length; i++)
                {
                    MStorage storage = storages[i];
                    Decimal deliver = toDeliver;
                    //	Not enough On Hand
                    if (deliver.CompareTo(storage.GetQtyOnHand()) > 0
                        && Env.Signum(storage.GetQtyOnHand()) >= 0)		//	positive storage
                    {
                        if (!force	//	Adjust to OnHand Qty  
                            || (i + 1 != storages.Length))	//	if force don't adjust last location
                            deliver = storage.GetQtyOnHand();
                    }
                    if (Env.Signum(deliver) == 0)	//	zero deliver
                    {
                        continue;
                    }

                    int M_Locator_ID = storage.GetM_Locator_ID();
                    //
                    MInOutLine line = null;
                    if (!linePerASI)	//	find line with Locator
                    {
                        for (int n = 0; n < list.Count; n++)
                        {
                            MInOutLine test = (MInOutLine)list[n];
                            if (test.GetM_Locator_ID() == M_Locator_ID)
                            {
                                line = test;
                                break;
                            }
                        }
                    }
                    if (line == null)	//	new line
                    {
                        line = new MInOutLine(_shipment);
                        line.SetOrderLine(orderLine, M_Locator_ID, order.IsSOTrx() ? deliver : Env.ZERO);
                        line.SetQty(deliver);
                        list.Add(line);
                    }
                    else
                    {
                        //	existing line
                        line.SetQty(Decimal.Add(line.GetMovementQty(), deliver));
                    }
                    if (orderLine.GetQtyEntered().CompareTo(orderLine.GetQtyOrdered()) != 0)
                    {
                        line.SetQtyEntered(Decimal.Round(Decimal.Divide(Decimal.Multiply(line.GetMovementQty(), orderLine.GetQtyEntered()),
                            orderLine.GetQtyOrdered()), 12, MidpointRounding.AwayFromZero));
                    }

                    line.SetLine(_line + orderLine.GetLine());
                    //if (linePerASI)
                    //{
                    line.SetM_AttributeSetInstance_ID(storage.GetM_AttributeSetInstance_ID());
                    //}

                    if (Env.IsModuleInstalled("DTD001_"))
                    {
                        line.SetDTD001_IsAttributeNo(true);
                    }
                    //190 - Set Print Description
                    if (line.Get_ColumnIndex("PrintDescription") >= 0)
                        line.Set_Value("PrintDescription", orderLine.Get_Value("PrintDescription"));

                    if (!line.Save())
                    {

                        //throw new Exception("Could not create Shipment Line");
                        //log.Fine("Failed: Could not create Shipment Line against Order No : " + order.GetDocumentNo() + " for orderline id : " + orderLine.GetC_OrderLine_ID());

                        // JID_0405: In the error message it should show the Name of the Product for which qty is not available.
                        pp = VLogger.RetrieveError();
                        if (pp != null)
                        {
                            _msg.Append(!string.IsNullOrEmpty(pp.GetName()) ? pp.GetName() : pp.GetValue());
                        }
                    }
                    //log.Fine("ToDeliver=" + qty + "/" + deliver + " - " + line);
                    toDeliver = Decimal.Subtract(toDeliver, deliver);
                    //	Temp adjustment
                    storage.SetQtyOnHand(Decimal.Subtract(storage.GetQtyOnHand(), deliver));
                    //
                    if (Env.Signum(toDeliver) == 0)
                    {
                        break;
                    }
                }
                if (Env.Signum(toDeliver) != 0)
                {
                    ret["Shipment_ID"] = 0;
                    ret["message"] = Msg.GetMsg(ctx, "VAS_StockNotAvailable");
                    return ret;
                }
                ret["Shipment_ID"] = _shipment.GetM_InOut_ID();
                ret["message"] = _msg;
                return ret;
                #endregion
            }
            ret["Shipment_ID"] = _shipment.GetM_InOut_ID();
            ret["message"] = _msg;
            return ret;
        }

        /// <summary>
        /// Get Storges
        /// </summary>
        /// <param name="M_Warehouse_ID"></param>
        /// <param name="M_Product_ID"></param>
        /// <param name="M_AttributeSetInstance_ID"></param>
        /// <param name="M_AttributeSet_ID"></param>
        /// <param name="allAttributeInstances"></param>
        /// <param name="minGuaranteeDate"></param>
        /// <param name="FiFo"></param>
        /// <param name="ctx"></param>
        /// <param name="trx"></param>
        /// <returns></returns>
        public MStorage[] GetStorages(int M_Warehouse_ID,
            int M_Product_ID, int M_AttributeSetInstance_ID, int M_AttributeSet_ID,
            bool allAttributeInstances, DateTime? minGuaranteeDate,
            bool FiFo, Ctx ctx, Trx trx)
        {

            SParameter _lastPP = new SParameter(M_Warehouse_ID,
                M_Product_ID, M_AttributeSetInstance_ID, M_AttributeSet_ID,
                allAttributeInstances, minGuaranteeDate, FiFo);
            Dictionary<SParameter, MStorage[]> _map = new Dictionary<SParameter, MStorage[]>();
            MStorage[] _lastStorages = null;


            //m_lastStorages = m_map.get(m_lastPP);
            if (_map.ContainsKey(_lastPP))
            {
                _lastStorages = _map[_lastPP];
            }
            else
            {
                _lastStorages = null;
            }

            if (_lastStorages == null)
            {
                _lastStorages = MStorage.GetWarehouse(ctx,
                    M_Warehouse_ID, M_Product_ID, M_AttributeSetInstance_ID,
                    M_AttributeSet_ID, allAttributeInstances, minGuaranteeDate,
                    FiFo, trx);

                try
                {
                    _map.Add(_lastPP, _lastStorages);
                }
                catch (Exception exp)
                {
                    //// MessageBox.Show(exp.ToString());
                    //log.Severe(exp.ToString());
                }
            }

            return _lastStorages;
        }


        /// <summary>
        /// Get Storage container
        /// </summary>
        /// <param name="M_Warehouse_ID"></param>
        /// <param name="M_Product_ID"></param>
        /// <param name="M_AttributeSetInstance_ID"></param>
        /// <param name="M_AttributeSet_ID"></param>
        /// <param name="allAttributeInstances"></param>
        /// <param name="minGuaranteeDate"></param>
        /// <param name="FiFo"></param>
        /// <param name="greater"></param>
        /// <param name="isContainerConsider"></param>
        /// <param name="ctx"></param>
        /// <param name="trx"></param>
        /// <returns></returns>
        public X_M_ContainerStorage[] GetContainerStorages(int M_Warehouse_ID, int M_Product_ID, int M_AttributeSetInstance_ID, int M_AttributeSet_ID,
                                                              bool allAttributeInstances, DateTime? minGuaranteeDate, bool FiFo, bool greater, bool isContainerConsider, Ctx ctx, Trx trx)
        {

            SParameter _lastPP = new SParameter(M_Warehouse_ID,
              M_Product_ID, M_AttributeSetInstance_ID, M_AttributeSet_ID,
              allAttributeInstances, minGuaranteeDate, FiFo);
            Dictionary<SParameter, X_M_ContainerStorage[]> _mapContainer = new Dictionary<SParameter, X_M_ContainerStorage[]>();

            MStorage[] _lastStorages = null;

            X_M_ContainerStorage[] _lastContainerStorages = null;
            //m_lastStorages = m_map.get(m_lastPP);
            if (_mapContainer.ContainsKey(_lastPP))
            {
                _lastContainerStorages = _mapContainer[_lastPP];
            }
            else
            {
                _lastContainerStorages = null;
            }

            if (_lastContainerStorages == null)
            {
                _lastContainerStorages = MProductContainer.GetContainerStorage(ctx, M_Warehouse_ID, 0, 0,
                              M_Product_ID, M_AttributeSetInstance_ID,
                              M_AttributeSet_ID,
                              allAttributeInstances,
                              (DateTime?)minGuaranteeDate,
                              FiFo, false, trx, false);

                try
                {
                    _mapContainer.Add(_lastPP, _lastContainerStorages);
                }
                catch (Exception exp)
                {
                    //// MessageBox.Show(exp.ToString());
                    //log.Severe(exp.ToString());
                }
            }

            return _lastContainerStorages;
        }


        /// <summary>
        /// VAI050-This method is used to generate GRN and also generate the Customer Return
        /// </summary>
        /// <param name="Order_ID"></param>
        /// <param name="Order_LineIDs"></param>
        /// <param name="ctx"></param>
        /// <returns></returns>
        public Dictionary<string, object> CreateGRN(int Order_ID, string Order_LineIDs, string Type, Ctx ctx)
        {
            VAdvantage.DataBase.Trx trx = null;
            Dictionary<string, object> ret = null;
            try
            {
                ret = new Dictionary<string, object>();
                MInOut obj = null;
                int M_Locator_ID = 0;
                string FetchSingleRecord = "";
                trx = VAdvantage.DataBase.Trx.Get("VAS_GenerateGRN" + DateTime.Now.Ticks);
                StringBuilder query = new StringBuilder();
                query.Append(@"SELECT  o.DateOrdered,o.AD_Org_ID, o.C_BPartner_ID, o.C_BPartner_Location_ID, o.M_Warehouse_ID,
                            o.AD_User_ID, o.SalesRep_ID, ol.C_OrderLine_ID, ol.M_AttributeSetInstance_ID,
                            ol.M_Product_ID, ol.C_Charge_ID, ol.C_UOM_ID, (ol.QtyOrdered/ol.QtyEntered) AS ConversionRate,
                            (ol.QtyOrdered-ol.QtyDelivered-(SELECT NVL(SUM(MovementQty),0) FROM M_Inout i 
                            INNER JOIN M_InoutLine il ON (i.M_Inout_ID=il.M_Inout_ID)
                            WHERE il.C_OrderLine_ID=ol.C_OrderLine_ID AND il.IsActive = 'Y' 
                            AND i.DocStatus NOT IN ('RE', 'VO', 'CL', 'CO'))) AS QtyRemianing
                            FROM C_Order o INNER JOIN C_OrderLine ol ON (o.C_Order_ID = ol.C_Order_ID)
                            WHERE ol.C_OrderLine_ID IN (" + Order_LineIDs + ")");
                DataSet ds = DB.ExecuteDataset(query.ToString(), null, null);
                if (ds != null && ds.Tables.Count > 0 && ds.Tables[0].Rows.Count > 0)
                {
                    query.Clear();
                    query.Append("SELECT M_Locator_ID FROM M_Locator WHERE M_Warehouse_ID=" + Util.GetValueOfInt(ds.Tables[0].Rows[0]["M_Warehouse_ID"]) + " ORDER BY IsDefault  DESC");
                    M_Locator_ID = Util.GetValueOfInt(DB.ExecuteScalar(query.ToString(), null, null));

                    obj = new MInOut(ctx, 0, trx);
                    obj.SetAD_Client_ID(ctx.GetAD_Client_ID());
                    obj.SetAD_Org_ID(Util.GetValueOfInt(ds.Tables[0].Rows[0]["AD_Org_ID"]));
                    obj.SetC_BPartner_ID(Util.GetValueOfInt(ds.Tables[0].Rows[0]["C_BPartner_ID"]));
                    obj.SetC_BPartner_Location_ID(Util.GetValueOfInt(ds.Tables[0].Rows[0]["C_BPartner_Location_ID"]));
                    obj.SetAD_User_ID(Util.GetValueOfInt(ds.Tables[0].Rows[0]["AD_User_ID"]));
                    obj.SetM_Warehouse_ID(Util.GetValueOfInt(ds.Tables[0].Rows[0]["M_Warehouse_ID"]));
                    obj.SetC_Order_ID(Order_ID);
                    obj.SetSalesRep_ID(Util.GetValueOfInt(ds.Tables[0].Rows[0]["SalesRep_ID"]));
                    if (Type == "CR")
                    {
                        obj.SetDateOrdered(Util.GetValueOfDateTime(ds.Tables[0].Rows[0]["DateOrdered"]));
                        obj.SetIsReturnTrx(true);
                        obj.SetIsSOTrx(true);
                        obj.SetMovementType("C+");
                        obj.SetC_DocType_ID(SetDocType(obj.GetAD_Org_ID(), ctx.GetAD_Client_ID(), "Y", "Y", "MMS"));
                    }
                    else
                    {
                        obj.SetMovementType("V+");
                        obj.SetC_DocType_ID(SetDocType(obj.GetAD_Org_ID(), ctx.GetAD_Client_ID(), "N", "N", "MMR"));
                        obj.Set_Value("M_Locator_ID", M_Locator_ID);
                    }
                    obj.SetMovementDate(DateTime.Now);
                    obj.SetDateAcct(DateTime.Now);
                    if (!obj.Save())
                    {
                        ValueNamePair pp = VLogger.RetrieveError();
                        string error = pp != null ? pp.GetName() : "";
                        if (string.IsNullOrEmpty(error))
                        {
                            error = pp != null ? Msg.GetMsg(ctx, pp.GetValue()) : "";
                        }

                        ret["Shipment_ID"] = 0;
                        ret["message"] = !string.IsNullOrEmpty(error) ? error : Msg.GetMsg(ctx, "VAS_GRNNotSaved");
                        return ret;
                    }

                    MInOutLine objLine = null;
                    int LineNo = 10;
                    decimal QtyEnetered = 0;

                    for (int i = 0; i < ds.Tables[0].Rows.Count; i++)
                    {
                        QtyEnetered = Util.GetValueOfDecimal(ds.Tables[0].Rows[i]["QtyRemianing"]) / Util.GetValueOfInt(ds.Tables[0].Rows[i]["ConversionRate"]);
                        objLine = new MInOutLine(ctx, 0, trx);
                        objLine.SetAD_Client_ID(ctx.GetAD_Client_ID());
                        objLine.SetAD_Org_ID(Util.GetValueOfInt(ds.Tables[0].Rows[0]["AD_Org_ID"]));
                        objLine.SetC_OrderLine_ID(Util.GetValueOfInt(ds.Tables[0].Rows[i]["C_OrderLine_ID"]));
                        objLine.SetC_UOM_ID(Util.GetValueOfInt(ds.Tables[0].Rows[i]["C_UOM_ID"]));
                        objLine.SetM_InOut_ID(obj.GetM_InOut_ID());
                        objLine.SetLine(LineNo);
                        objLine.SetM_AttributeSetInstance_ID(Util.GetValueOfInt(ds.Tables[0].Rows[i]["M_AttributeSetInstance_ID"]));
                        if (Util.GetValueOfInt(ds.Tables[0].Rows[i]["M_Product_ID"]) > 0)
                        {
                            objLine.SetM_Product_ID(Util.GetValueOfInt(ds.Tables[0].Rows[i]["M_Product_ID"]));
                        }
                        else
                        {
                            objLine.SetC_Charge_ID(Util.GetValueOfInt(ds.Tables[0].Rows[i]["C_Charge_ID"]));
                        }
                        objLine.SetQtyEntered(QtyEnetered);
                        //objLine.SetMovementQty(Util.GetValueOfDecimal(ds.Tables[0].Rows[i]["QtyRemianing"]));
                        objLine.SetMovementQty(QtyEnetered);
                        objLine.SetM_Locator_ID(M_Locator_ID);
                        if (!objLine.Save())
                        {
                            ValueNamePair pp = VLogger.RetrieveError();
                            string error = pp != null ? pp.GetName() : "";
                            if (string.IsNullOrEmpty(error))
                            {
                                error = pp != null ? Msg.GetMsg(ctx, pp.GetValue()) : "";
                            }

                            ret["Shipment_ID"] = 0;
                            ret["message"] = !string.IsNullOrEmpty(error) ? error : Msg.GetMsg(ctx, "VAS_GRNNotSaved");
                            return ret;
                        }

                        LineNo = LineNo + 10;

                    }
                    ret["Shipment_ID"] = obj.GetM_InOut_ID();
                    ret["message"] = Msg.GetMsg(ctx, "VAS_GRNSaved");
                    trx.Commit();
                    return ret;
                }
                return null;
            }
            catch (Exception ex)
            {
                if (trx != null)
                {
                    trx.Rollback();
                }
                ret["Shipment_ID"] = 0;
                ret["message"] = Msg.GetMsg(ctx, "VAS_DeliveryOrderNotSaved");
                return ret;
            }
            finally
            {

                if (trx != null)
                    trx.Close();
            }
        }

        /// <summary>
        /// This method is used to generate Material Transfer
        /// </summary>
        /// <param name="M_Requisition_ID"></param>
        /// <param name="M_RequisitionLines_IDs"></param>
        /// <param name="ctx"></param>
        /// <returns></returns>
        public Dictionary<string, object> CreateMaterialTransfer(int M_Requisition_ID, string M_RequisitionLines_IDs, Ctx ctx)
        {
            VAdvantage.DataBase.Trx trx = null;
            Dictionary<string, object> ret = null;
            try
            {
                ret = new Dictionary<string, object>();
                MMovement Mov = null;
                int DocType_ID;
                int LocatorFrom, LocatorTo;
                trx = VAdvantage.DataBase.Trx.Get("VAS_GenMaterialTransfer" + DateTime.Now.Ticks);
                StringBuilder query = new StringBuilder();
                query.Append(@"SELECT r.DateRequired, r.AD_Org_ID, r.DTD001_MWarehouseSource_ID, r.M_Warehouse_ID, r.C_IncoTerm_ID, r.C_BPartner_ID,
                           rl.M_RequisitionLine_ID, rl.M_Product_ID, rl.M_AttributeSetInstance_ID, rl.C_UOM_ID, (rl.Qty/rl.QtyEntered) AS ConversionRate,
                           r.AD_OrgTrx_ID, (rl.Qty - rl.DTD001_ReservedQty - rl.DTD001_DeliveredQty) AS QtyRemianing
                           FROM M_Requisition r INNER JOIN M_RequisitionLine rl ON(r.M_Requisition_ID = rl.M_Requisition_ID)
                           WHERE rl.M_RequisitionLine_ID IN (" + M_RequisitionLines_IDs + ")");
                DataSet ds = DB.ExecuteDataset(query.ToString(), null, null);

                if (ds != null && ds.Tables.Count > 0 && ds.Tables[0].Rows.Count > 0)
                {
                    query.Clear();
                    query.Append(@"SELECT C_DocType_ID FROM C_DocType WHERE IsActive = 'Y' AND DocBaseType='MMM' AND AD_Client_ID=" + ctx.GetAD_Client_ID()
                                     + " AND AD_Org_ID IN (0, " + Util.GetValueOfInt(ds.Tables[0].Rows[0]["AD_Org_ID"]) + ") ORDER BY AD_Org_ID DESC, IsInTransit ASC");
                    DocType_ID = Util.GetValueOfInt(DB.ExecuteScalar(query.ToString(), null, null));

                    Mov = new MMovement(ctx, 0, trx);
                    Mov.SetAD_Org_ID(Util.GetValueOfInt(ds.Tables[0].Rows[0]["AD_Org_ID"]));
                    //Mov.SetMovementDate(DateTime.Now.Date);
                    Mov.SetC_DocType_ID(DocType_ID);
                    Mov.SetM_Warehouse_ID(Util.GetValueOfInt(ds.Tables[0].Rows[0]["M_Warehouse_ID"]));
                    Mov.SetDTD001_MWarehouseSource_ID(Util.GetValueOfInt(ds.Tables[0].Rows[0]["DTD001_MWarehouseSource_ID"]));
                    Mov.SetMovementDate(Util.GetValueOfDateTime(ds.Tables[0].Rows[0]["DateRequired"]));
                    Mov.Set_Value("C_IncoTerm_ID", Util.GetValueOfInt(ds.Tables[0].Rows[0]["C_IncoTerm_ID"]));
                    if (!Mov.Save())
                    {
                        ValueNamePair pp = VLogger.RetrieveError();
                        string error = pp != null ? pp.GetName() : "";
                        if (string.IsNullOrEmpty(error))
                        {
                            error = pp != null ? Msg.GetMsg(ctx, pp.GetValue()) : "";
                        }

                        ret["Movement_ID"] = 0;
                        ret["message"] = !string.IsNullOrEmpty(error) ? error : Msg.GetMsg(ctx, "VAS_TransferNotSaved");
                        return ret;
                    }

                    query.Clear();
                    query.Append("SELECT M_Locator_ID FROM M_Locator WHERE M_Warehouse_ID=" + Util.GetValueOfInt(ds.Tables[0].Rows[0]["DTD001_MWarehouseSource_ID"]) + " ORDER BY IsDefault DESC");
                    LocatorFrom = Util.GetValueOfInt(DB.ExecuteScalar(query.ToString(), null, null));

                    query.Clear();
                    query.Append("SELECT M_Locator_ID FROM M_Locator WHERE M_Warehouse_ID=" + Util.GetValueOfInt(ds.Tables[0].Rows[0]["M_Warehouse_ID"]) + " ORDER BY IsDefault DESC");
                    LocatorTo = Util.GetValueOfInt(DB.ExecuteScalar(query.ToString(), null, null));

                    MMovementLine MovL = null;
                    int LineNo = 10;
                    decimal QtyEnetered = 0;

                    for (int i = 0; i < ds.Tables[0].Rows.Count; i++)
                    {
                        QtyEnetered = Util.GetValueOfDecimal(ds.Tables[0].Rows[i]["QtyRemianing"]) / Util.GetValueOfInt(ds.Tables[0].Rows[i]["ConversionRate"]);
                        MovL = new MMovementLine(ctx, 0, trx);
                        MovL.SetAD_Client_ID(Mov.GetAD_Client_ID());
                        MovL.SetAD_Org_ID(Mov.GetAD_Org_ID());
                        MovL.SetM_RequisitionLine_ID(Util.GetValueOfInt(ds.Tables[0].Rows[i]["M_RequisitionLine_ID"]));
                        MovL.SetMovementQty(QtyEnetered);
                        MovL.SetQtyEntered(QtyEnetered);
                        MovL.SetM_Movement_ID(Mov.GetM_Movement_ID());
                        MovL.SetM_Locator_ID(LocatorFrom);
                        MovL.SetM_LocatorTo_ID(LocatorTo);
                        MovL.SetLine(LineNo);
                        MovL.SetM_Product_ID(Util.GetValueOfInt(ds.Tables[0].Rows[i]["M_Product_ID"]));
                        MovL.SetM_AttributeSetInstance_ID(Util.GetValueOfInt(ds.Tables[0].Rows[i]["M_AttributeSetInstance_ID"]));
                        MovL.SetC_BPartner_ID(Util.GetValueOfInt(ds.Tables[0].Rows[i]["C_BPartner_ID"]));
                        //MovL.SetA_Asset_ID(asset_id);
                        MovL.Set_Value("C_UOM_ID", Util.GetValueOfInt(ds.Tables[0].Rows[i]["C_UOM_ID"]));

                        if (Util.GetValueOfInt(ds.Tables[0].Rows[i]["AD_OrgTrx_ID"]) > 0)
                        {
                            MovL.Set_Value("AD_OrgTrx_ID", Util.GetValueOfInt(ds.Tables[0].Rows[i]["AD_OrgTrx_ID"]));
                        }
                        if (!MovL.Save())
                        {
                            ValueNamePair pp = VLogger.RetrieveError();
                            string error = pp != null ? pp.GetName() : "";
                            if (string.IsNullOrEmpty(error))
                            {
                                error = pp != null ? Msg.GetMsg(ctx, pp.GetValue()) : "";
                            }

                            ret["Movement_ID"] = 0;
                            ret["message"] = !string.IsNullOrEmpty(error) ? error : Msg.GetMsg(ctx, "VAS_TransferNotSaved");
                            return ret;
                        }
                        LineNo += 10;
                    }
                    ret["Movement_ID"] = Mov.GetM_Movement_ID();
                    ret["message"] = Msg.GetMsg(ctx, "VAS_TransferSaved");
                    trx.Commit();
                    return ret;
                }
                return null;
            }
            catch (Exception ex)
            {
                if (trx != null)
                {
                    trx.Rollback();
                }
                ret["Movement_ID"] = 0;
                ret["message"] = Msg.GetMsg(ctx, "VAS_TransferNotSaved");
                return ret;
            }
            finally
            {
                if (trx != null)
                    trx.Close();
            }
        }

        /// <summary>
        /// VAI050-This method is used to generate GRN and also generate the Customer Return
        /// </summary>
        /// <param name="Order_ID"></param>
        /// <param name="Order_LineIDs"></param>
        /// <param name="ctx"></param>
        /// <returns></returns>
        public Dictionary<string, object> CreateSalesOrder(int Order_ID, string Order_LineIDs, Ctx ctx)
        {
            Trx trx = null;
            Dictionary<string, object> ret = null;
            try
            {
                ret = new Dictionary<string, object>();
                trx = Trx.Get("VAS_GenerateGRN" + DateTime.Now.Ticks);

                MOrder from = new MOrder(ctx, Order_ID, trx);
                MOrder to = new MOrder(from.GetCtx(), 0, trx);
                to.Set_TrxName(trx);
                PO.CopyValues(from, to, from.GetAD_Client_ID(), from.GetAD_Org_ID());
                to.Set_ValueNoCheck("C_Order_ID", Env.ZERO);
                to.Set_ValueNoCheck("DocumentNo", null);
                to.SetDocStatus(X_C_Order.DOCSTATUS_Drafted);
                to.SetDocAction(X_C_Order.DOCACTION_Complete);
                to.SetC_DocType_ID(0);
                to.SetC_DocTypeTarget_ID();
                to.SetIsSelected(false);
                to.SetDateOrdered(DateTime.Now);
                to.SetDateAcct(DateTime.Now);
                to.SetDatePromised(DateTime.Now);
                to.SetDatePrinted(null);
                to.SetIsPrinted(false);
                to.SetIsApproved(false);
                to.SetIsCreditApproved(false);
                to.SetC_Payment_ID(0);
                to.SetC_CashLine_ID(0);
                //	Amounts are updated  when adding lines
                to.SetGrandTotal(Env.ZERO);
                to.SetTotalLines(Env.ZERO);
                to.SetIsDelivered(false);
                to.SetIsInvoiced(false);
                to.SetIsSelfService(false);
                to.SetIsTransferred(false);
                to.SetPosted(false);
                to.SetProcessed(false);
                to.SetPOReference(Util.GetValueOfString(from.GetDocumentNo()));
                if (to.Get_ColumnIndex("C_Order_Quotation") > 0)
                    to.SetC_Order_Quotation(Order_ID);

                if (to.Get_ColumnIndex("VAS_ContractMaster_ID") >= 0 && Util.GetValueOfInt(from.Get_Value("VAS_ContractMaster_ID")) > 0)
                {
                    to.Set_Value("VAS_ContractMaster_ID", from.Get_Value("VAS_ContractMaster_ID"));
                }

                if (to.Get_ColumnIndex("C_IncoTerm_ID") > 0)
                {
                    to.SetC_IncoTerm_ID(from.GetC_IncoTerm_ID());
                }

                if (to.Get_ColumnIndex("ConditionalFlag") > -1)
                {
                    to.SetConditionalFlag(MOrder.CONDITIONALFLAG_PrepareIt);
                }

                if (!to.Save(trx))
                {
                    trx.Rollback();
                    ValueNamePair pp = VLogger.RetrieveError();
                    string error = pp != null ? pp.GetName() : "";
                    if (string.IsNullOrEmpty(error))
                    {
                        error = pp != null ? Msg.GetMsg(ctx, pp.GetValue()) : "";
                    }
                    ret["SalesOrder_ID"] = 0;
                    ret["message"] = !string.IsNullOrEmpty(error) ? error : Msg.GetMsg(ctx, "VAS_OrderNotSaved");
                    return ret;
                }

                DB.ExecuteQuery("UPDATE C_Order SET Ref_Order_ID = " + to.GetC_Order_ID() + " WHERE C_Order_ID = " + from.GetC_Order_ID(), null, trx);

                if (Env.IsModuleInstalled("VA075_") && to.Get_ColumnIndex("VA075_FieldServiceReq_ID") > 0 && from.Get_ValueAsInt("VA075_FieldServiceReq_ID") > 0)
                {
                    DB.ExecuteQuery("UPDATE VA075_FieldServiceReq SET C_Order_ID=" + to.GetC_Order_ID() + " WHERE VA075_FieldServiceReq_ID="
                        + from.Get_ValueAsInt("VA075_FieldServiceReq_ID"), null, trx);
                }

                StringBuilder query = new StringBuilder();
                query.Append(@"SELECT ol.AD_Org_ID, ol.C_OrderLine_ID, ol.M_AttributeSetInstance_ID, ol.M_Product_ID, ol.C_UOM_ID, 
                            ol.QtyEntered, ol.QtyOrdered, ol.PriceEntered, ol.PriceActual, ol.PriceList, ol.C_Tax_ID
                            FROM C_Order o INNER JOIN C_OrderLine ol ON (o.C_Order_ID = ol.C_Order_ID)
                            WHERE ol.C_OrderLine_ID IN (" + Order_LineIDs + ")");
                DataSet ds = DB.ExecuteDataset(query.ToString(), null, null);
                if (ds != null && ds.Tables.Count > 0 && ds.Tables[0].Rows.Count > 0)
                {
                    MOrderLine objLine = null;
                    int LineNo = 10;

                    for (int i = 0; i < ds.Tables[0].Rows.Count; i++)
                    {
                        objLine = new MOrderLine(to);
                        objLine.SetAD_Client_ID(ctx.GetAD_Client_ID());
                        objLine.SetAD_Org_ID(Util.GetValueOfInt(ds.Tables[0].Rows[0]["AD_Org_ID"]));
                        objLine.SetC_Order_ID(to.GetC_Order_ID());
                        objLine.SetLine(LineNo);
                        objLine.SetM_AttributeSetInstance_ID(Util.GetValueOfInt(ds.Tables[0].Rows[i]["M_AttributeSetInstance_ID"]));
                        objLine.SetM_Product_ID(Util.GetValueOfInt(ds.Tables[0].Rows[i]["M_Product_ID"]));
                        objLine.SetC_UOM_ID(Util.GetValueOfInt(ds.Tables[0].Rows[i]["C_UOM_ID"]));
                        objLine.SetC_Tax_ID(Util.GetValueOfInt(ds.Tables[0].Rows[i]["C_Tax_ID"]));
                        objLine.SetQtyEntered(Util.GetValueOfDecimal(ds.Tables[0].Rows[i]["QtyEntered"]));
                        objLine.SetQtyOrdered(Util.GetValueOfDecimal(ds.Tables[0].Rows[i]["QtyOrdered"]));
                        objLine.SetPriceEntered(Util.GetValueOfDecimal(ds.Tables[0].Rows[i]["PriceEntered"]));
                        objLine.SetPriceActual(Util.GetValueOfDecimal(ds.Tables[0].Rows[i]["PriceActual"]));
                        objLine.SetPriceList(Util.GetValueOfDecimal(ds.Tables[0].Rows[i]["PriceList"]));
                        if (objLine.Get_ColumnIndex("C_Quotation_Line_ID") >= 0)
                            objLine.Set_Value("C_Quotation_Line_ID", Util.GetValueOfInt(ds.Tables[0].Rows[i]["C_OrderLine_ID"]));
                        if (!objLine.Save())
                        {
                            ValueNamePair pp = VLogger.RetrieveError();
                            string error = pp != null ? pp.GetName() : "";
                            if (string.IsNullOrEmpty(error))
                            {
                                error = pp != null ? Msg.GetMsg(ctx, pp.GetValue()) : "";
                            }

                            ret["SalesOrder_ID"] = 0;
                            ret["message"] = !string.IsNullOrEmpty(error) ? error : Msg.GetMsg(ctx, "VAS_OrderNotSaved");
                            return ret;
                        }

                        LineNo += 10;
                        DB.ExecuteQuery("UPDATE C_OrderLine SET Ref_OrderLine_ID = " + objLine.GetC_OrderLine_ID() + " WHERE C_OrderLine_ID = "
                            + Util.GetValueOfInt(ds.Tables[0].Rows[i]["C_OrderLine_ID"]), null, trx);
                    }

                    ret["SalesOrder_ID"] = to.GetC_Order_ID();
                    ret["message"] = Msg.GetMsg(ctx, "VAS_OrderSaved");
                    trx.Commit();
                    return ret;
                }
                return null;
            }
            catch (Exception ex)
            {
                if (trx != null)
                {
                    trx.Rollback();
                }
                ret["SalesOrder_ID"] = 0;
                ret["message"] = ex.Message;
                return ret;
            }
            finally
            {
                if (trx != null)
                    trx.Close();
            }
        }

        /// <summary>
        /// VAI050-This method use to get doctype
        /// </summary>
        /// <param name="AD_Org_ID"></param>
        /// <param name="AD_Client_ID"></param>
        /// <param name="IsSOTrx"></param>
        /// <param name="IsReturnTrx"></param>
        /// <param name="DocBaseType"></param>
        /// <returns></returns>
        public int SetDocType(int AD_Org_ID, int AD_Client_ID, string IsSOTrx, string IsReturnTrx, string DocBaseType)
        {
            string query = @"SELECT C_DocType_ID FROM C_DocType WHERE DocBaseType='" + DocBaseType + "' AND AD_Client_ID=" + AD_Client_ID + @"
                   AND IsActive = 'Y' AND AD_Org_ID IN(0, " + AD_Org_ID + ") AND IsSOTrx ='" + IsSOTrx + "' AND IsReturnTrx = '" + IsReturnTrx + "' ORDER BY AD_Org_ID DESC";
            return Util.GetValueOfInt(DB.ExecuteScalar(query));
        }

        /// <summary>
        /// Get the list of Customer with Credit Limit Utilization
        /// </summary>
        /// <param name="ctx"></param>
        /// <param name="pageNo"></param>
        /// <param name="pageSize"></param>
        /// <returns></returns>
        public dynamic GetCustomerCredit(Ctx ctx, int pageNo, int pageSize)
        {
            dynamic result = new ExpandoObject();
            result.Customers = new List<dynamic>();
            StringBuilder sb = new StringBuilder();
            sb.Append(@"" + MRole.GetDefault(ctx).AddAccessSQL(@"SELECT bp.C_BPartner_ID, bp.Name, img.ImageUrl, bp.CreditStatusSettingOn, 
                    ref.Name AS CreditStatus, cl.C_BPartner_Location_ID,
                    CASE WHEN (bp.CreditStatusSettingOn = 'CH') THEN bp.SO_CreditLimit ELSE cl.SO_CreditLimit END AS SO_CreditLimit,
                    CASE WHEN (bp.CreditStatusSettingOn = 'CH') THEN bp.SO_CreditUsed ELSE cl.SO_CreditUsed END AS SO_CreditUsed,
                    CASE WHEN (bp.CreditStatusSettingOn = 'CH') THEN bp.CreditValidation ELSE cl.CreditValidation END AS CreditValidation,
                    CASE WHEN (bp.CreditStatusSettingOn = 'CH') THEN bp.SOCreditStatus ELSE cl.SOCreditStatus END AS SOCreditStatus
                    FROM C_BPartner bp INNER JOIN C_BPartner_Location cl ON (bp.C_BPartner_ID = cl.C_BPartner_ID)
                    INNER JOIN AD_Ref_List ref ON (ref.Value = CASE WHEN (bp.CreditStatusSettingOn = 'CH') THEN bp.SOCreditStatus ELSE cl.SOCreditStatus END 
                    AND ref.AD_Reference_ID = 289)
                    LEFT JOIN AD_Image img ON (bp.Pic = img.AD_Image_ID)
                    WHERE CASE WHEN (bp.CreditStatusSettingOn = 'CH') THEN bp.SO_CreditLimit ELSE cl.SO_CreditLimit END > 0",
                    "bp", MRole.SQL_FULLYQUALIFIED, MRole.SQL_RO));

            DataSet ds = DB.ExecuteDataset(sb.ToString(), null, null, pageSize, pageNo);
            if (ds != null && ds.Tables.Count > 0 && ds.Tables[0].Rows.Count > 0)
            {
                if (pageNo == 1)
                {
                    result.RecordCount = Util.GetValueOfInt(DB.ExecuteScalar("SELECT COUNT(C_BPartner_ID) FROM (" + sb.ToString() + ") t ", null, null));
                }
                dynamic obj;
                foreach (DataRow parentRow in ds.Tables[0].Rows)
                {
                    obj = new ExpandoObject();
                    obj.Name = Util.GetValueOfString(parentRow["Name"]);
                    obj.C_BPartner_ID = Util.GetValueOfInt(parentRow["C_BPartner_ID"]);
                    obj.Location_ID = Util.GetValueOfInt(parentRow["C_BPartner_Location_ID"]);
                    obj.SO_CreditLimit = Util.GetValueOfDecimal(parentRow["SO_CreditLimit"]);
                    obj.SO_CreditUsed = Util.GetValueOfDecimal(parentRow["SO_CreditUsed"]);
                    obj.CreditUtil = Math.Round(decimal.Multiply(decimal.Divide(obj.SO_CreditUsed, obj.SO_CreditLimit), 100), 2, MidpointRounding.AwayFromZero);
                    obj.SOCreditStatus = Util.GetValueOfString(parentRow["SOCreditStatus"]);
                    obj.CreditSetting = Util.GetValueOfString(parentRow["CreditStatusSettingOn"]);
                    obj.CreditValidation = Util.GetValueOfString(parentRow["CreditValidation"]);
                    obj.CreditStatus = Util.GetValueOfString(parentRow["CreditStatus"]);
                    obj.ImageUrl = Util.GetValueOfString(parentRow["ImageUrl"]);
                    result.Customers.Add(obj);
                }
            }
            else
            {
                return null;
            }

            return result;
        }

        /// <summary>
        /// Update Customer Credit Limit
        /// </summary>
        /// <param name="ctx"></param>
        /// <param name="BP_ID"></param>
        /// <param name="Loc_ID"></param>
        /// <param name="CreditSetting"></param>
        /// <param name="CreditValidation"></param>
        /// <returns>1 if success</returns>
        public int UpdateCreditValidation(Ctx ctx, int BP_ID, int Loc_ID, string CreditSetting, string CreditValidation)
        {
            string tableName = CreditSetting == "CH" ? "C_BPartner" : "C_BPartner_Location";
            return DB.ExecuteQuery(@"UPDATE " + tableName + " SET CreditValidation = '" + CreditValidation + "' WHERE " + tableName + "_ID = " +
                (CreditSetting == "CH" ? BP_ID : Loc_ID));
        }


        public class MultiplyRateItem
        {
            public int C_UOM_To_ID { get; set; }
            public decimal MultiplyRate { get; set; }
            public decimal DivideRate { get; set; }
        }

        public class OrderLine
        {
            public int M_Product_ID { get; set; }
            public int C_Order_ID { get; set; }
            public int C_OrderLine_ID { get; set; }
            public int M_AttributeSetInstance_ID { get; set; }
            public decimal QtyOrdered { get; set; } //Remaing Qty in base uom
            public decimal QtyEntered { get; set; } //Remaing qty in line uom
            public decimal OnHandQty { get; set; }
            public int C_UOM_ID { get; set; }
            public string AttributeName { get; set; }
            public string UOM { get; set; }
            public string ProductName { get; set; }
            public string ProductType { get; set; }
        }

        public class ParentOrder
        {
            public string DocumentNo { get; set; }
            public DateTime? DateOrdered { get; set; }
            public decimal GrandTotal { get; set; }
            public decimal LineCount { get; set; }
            public string Symbol { get; set; }
            public int StdPrecision { get; set; }
            public int C_Order_ID { get; set; }
            public string DeliveryLocation { get; set; }
            public string ProductLocation { get; set; }
            public string CustomerName { get; set; }
            public List<OrderLine> OrderLines { get; set; }
        }

        public class DeliveryResult
        {
            public List<ParentOrder> Orders { get; set; }
            public int RecordCount { get; set; }
            public int AD_Window_ID { get; set; }
        }
    }
}