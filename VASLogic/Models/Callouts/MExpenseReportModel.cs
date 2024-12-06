using System;
using System.Collections.Generic;
using System.Data;
using System.Linq;
using System.Text;
using System.Web;
using VAdvantage.Logging;
using VAdvantage.Model;
using VAdvantage.Utility;
using VIS.DBase;


namespace VIS.Models
{
    public class MExpenseReportModel
    {
        /// <summary>
        /// Get Price of product
        /// </summary>
        /// <param name="ctx">Context</param>
        /// <param name="param">List of parameters</param>
        /// <returns>Price</returns>
        public Dictionary<String, Object> GetPrices(Ctx ctx, string param)
        {
            string[] paramValue = param.Split(',');

            Dictionary<String, Object> retDic = new Dictionary<String, Object>();

            //Assign parameter value
            int _m_Product_Id = Util.GetValueOfInt(paramValue[0].ToString());
            int _s_TimeExpense_ID = Util.GetValueOfInt(paramValue[1].ToString());
            int _c_Uom_Id = Util.GetValueOfInt(paramValue[2].ToString());
            StringBuilder sql = new StringBuilder();
            decimal PriceStd = 0;
            int _m_PriceList_ID = 0;
            int _priceListVersion_Id = 0;
            _m_PriceList_ID = Util.GetValueOfInt(DB.ExecuteScalar("SELECT M_PriceList_ID FROM S_TimeExpense  WHERE S_TimeExpense_ID =" + _s_TimeExpense_ID, null, null));
            _priceListVersion_Id = Util.GetValueOfInt(DB.ExecuteScalar("SELECT M_PriceList_Version_ID,name FROM M_PriceList_Version WHERE IsActive='Y' AND M_PriceList_ID=" + _m_PriceList_ID + " ORDER BY ValidFrom DESC", null, null));
            sql.Append("SELECT PriceStd , PriceList, PriceLimit FROM M_ProductPrice WHERE Isactive='Y' AND M_Product_ID = " + _m_Product_Id
                        + " AND M_PriceList_Version_ID = " + _priceListVersion_Id
                         + " AND  ( M_AttributeSetInstance_ID = 0 OR M_AttributeSetInstance_ID IS NULL ) "
                        + "  AND C_UOM_ID=" + _c_Uom_Id);
            PriceStd = Util.GetValueOfInt(DB.ExecuteScalar(sql.ToString(), null, null));
            sql.Clear();
            retDic["PriceStd"] = PriceStd;
            return retDic;
        }

        /// <summary>
        /// Get Standard Price
        /// </summary>
        /// <param name="ctx">Context</param>
        /// <param name="param">List of parameters</param>
        /// <returns>price</returns>
        public Dictionary<String, Object> GetStandardPrice(Ctx ctx, string param)
        {
            string[] paramValue = param.Split(',');

            Dictionary<String, Object> retDic = new Dictionary<String, Object>();

            //Assign parameter value
            int _m_Product_Id = Util.GetValueOfInt(paramValue[0].ToString());
            int _s_TimeExpense_ID = Util.GetValueOfInt(paramValue[1].ToString());
            int _c_Uom_Id = Util.GetValueOfInt(paramValue[2].ToString());
            DateTime? dateExpense = Util.GetValueOfDateTime(paramValue[3].ToString());
            StringBuilder sql = new StringBuilder();
            int _m_PriceList_ID = 0;
            DateTime? validFrom;
            decimal priceActual = 0;
            int currency = 0;
            bool noPrice = true;
            _m_PriceList_ID = Util.GetValueOfInt(DB.ExecuteScalar("SELECT M_PriceList_ID FROM S_TimeExpense  WHERE S_TimeExpense_ID =" + _s_TimeExpense_ID, null, null));
            sql.Append("SELECT pp.PriceStd, pp.PriceList,pp.PriceLimit ,"
                + "pp.C_UOM_ID,pv.ValidFrom,pl.C_Currency_ID "
                + "FROM M_Product p, M_ProductPrice pp, M_PriceList pl, M_PriceList_Version pv "
                + "WHERE p.M_Product_ID=pp.M_Product_ID"
                + " AND pp.M_PriceList_Version_ID=pv.M_PriceList_Version_ID"
                + " AND pv.M_PriceList_ID=pl.M_PriceList_ID"
                + " AND pv.IsActive='Y'"
                + " AND p.M_Product_ID=" + _m_Product_Id
                + " AND pl.M_PriceList_ID=" + _m_PriceList_ID
                + " AND pp.C_UOM_ID=" + _c_Uom_Id
                + " ORDER BY pv.ValidFrom DESC");

            DataSet ds = DB.ExecuteDataset(sql.ToString());
            if (ds != null && ds.Tables.Count > 0 && noPrice)
            {
                if (ds.Tables[0].Rows.Count > 0)
                {
                    validFrom = Util.GetValueOfDateTime(ds.Tables[0].Rows[0]["ValidFrom"]);
                    if (validFrom == null || !(dateExpense < validFrom))
                    {
                        noPrice = false;
                        priceActual = Util.GetValueOfDecimal(ds.Tables[0].Rows[0]["PriceStd"]);

                        if (priceActual == 0)
                        {
                            priceActual = Util.GetValueOfDecimal(ds.Tables[0].Rows[0]["PriceList"]);//.GetDecimal("PriceList");
                        }
                        if (priceActual == 0)
                        {
                            priceActual = Util.GetValueOfDecimal(ds.Tables[0].Rows[0]["PriceLimit"]);//.GetDecimal("PriceLimit");
                        }
                        //	Currency
                        currency = Util.GetValueOfInt(ds.Tables[0].Rows[0]["C_Currency_ID"]);

                    }
                }

            }
            if (noPrice)
            {
                sql.Clear();
                //	Find if via Base Pricelist
                sql.Append("SELECT  pp.PriceStd, pp.PriceList,pp.PriceLimit ,"
                    + "pp.C_UOM_ID,pv.ValidFrom,pl.C_Currency_ID "
                    + "FROM M_Product p, M_ProductPrice pp, M_PriceList pl, M_PriceList bpl, M_PriceList_Version pv "
                    + "WHERE p.M_Product_ID=pp.M_Product_ID"
                    + " AND pp.M_PriceList_Version_ID=pv.M_PriceList_Version_ID"
                    + " AND pv.M_PriceList_ID=bpl.M_PriceList_ID"
                    + " AND pv.IsActive='Y'"
                    + " AND bpl.M_PriceList_ID=pl.BasePriceList_ID"	//	Base
                    + " AND p.M_Product_ID=" + _m_Product_Id
                    + " AND pl.M_PriceList_ID=" + _m_PriceList_ID
                    + " ORDER BY pv.ValidFrom DESC");
                DataSet ds1 = DB.ExecuteDataset(sql.ToString());
                if (ds1 != null && ds1.Tables.Count > 0 && noPrice)
                {
                    if (ds1.Tables[0].Rows.Count > 0)
                    {
                        validFrom = Util.GetValueOfDateTime(ds1.Tables[0].Rows[0]["ValidFrom"]);
                        if (validFrom == null || !(dateExpense < validFrom))
                        {
                            noPrice = false;
                            priceActual = Util.GetValueOfDecimal(ds1.Tables[0].Rows[0]["PriceStd"]);

                            if (priceActual == 0)
                            {
                                priceActual = Util.GetValueOfDecimal(ds1.Tables[0].Rows[0]["PriceList"]);//.GetDecimal("PriceList");
                            }
                            if (priceActual == 0)
                            {
                                priceActual = Util.GetValueOfDecimal(ds1.Tables[0].Rows[0]["PriceLimit"]);//.GetDecimal("PriceLimit");
                            }
                            //	Currency
                            currency = Util.GetValueOfInt(ds1.Tables[0].Rows[0]["C_Currency_ID"]);

                        }
                    }

                }
            }
            sql.Clear();
            retDic["PriceActual"] = priceActual;
            retDic["C_Currency_ID"] = currency;
            return retDic;
        }

        /// <summary>
        /// Get Charge Amount
        /// </summary>
        /// <param name="ctx">Parameters</param>
        /// <param name="fields"></param>
        /// <returns>Charge Amount</returns>
        public int GetChargeAmount(Ctx ctx, string fields)
        {
            int chargeAmt = Util.GetValueOfInt(DB.ExecuteScalar("SELECT ChargeAmt FROM C_Charge WHERE C_Charge_ID = " + Util.GetValueOfInt(fields), null, null));
            return chargeAmt;
        }
        /// <summary>
        /// GetProfiletype
        /// </summary>
        /// <param name="fields">fields</param>
        /// <returns>ProfileType</returns>
        public string GetProfiletype(string fields)
        {
            return Util.GetValueOfString(DB.ExecuteScalar("SELECT ProfileType from S_Resource WHERE AD_User_ID =  " + Util.GetValueOfInt(fields), null, null));

        }

        /// <summary>
        /// VAI094:for fetching customer id from database according to requestid or project id  selected in 
        /// request field  or project field
        /// in Report line tab in time and expense report window
        /// </summary>
        /// <param name="fields">string fields </param>
        /// <returns>customer id</returns>
        public Dictionary<string, object> LoadCustomerData(string fields)
        {
            string[] paramValue = fields.Split(',');
            int ID = Util.GetValueOfInt(paramValue[0]);
            var columnName = paramValue[1];
            string str = "";
            Dictionary<string, object> retDic = null;
            if (columnName == "R_Request_ID")
            {
                //VIS0336-implement the code for settignthe location on time expense line tab
                str = " SELECT R.C_BPartner_ID AS Customer, l.C_BPartner_Location_ID AS Location FROM R_Request R " +
                    " INNER JOIN C_BPartner C ON R.C_BPartner_ID = C.C_BPartner_ID " +
                    " LEFT JOIN C_BPartner_Location l ON C.C_BPartner_ID = l.C_BPartner_ID WHERE R.R_Request_ID = " + ID + " AND " +
                    " C.IsCustomer = 'Y' AND l.C_BPartner_Location_ID = ( SELECT l1.C_BPartner_Location_ID FROM C_BPartner_Location l1" +
                    "  WHERE l1.C_BPartner_ID = C.C_BPartner_ID ORDER BY l1.Created ASC LIMIT 1 )";
            }
            else if (columnName == "C_Project_ID")
            {

                str = " SELECT DISTINCT CASE WHEN p.C_BPartner_ID > 0 THEN p.C_BPartner_ID ELSE b.C_BPartner_ID END AS Customer, CASE WHEN l.C_BPartner_Location_ID > 0" +
                    " THEN (SELECT l1.C_BPartner_Location_ID FROM C_BPartner_Location l1 WHERE l1.C_BPartner_ID = p.C_BPartner_ID ORDER BY l1.Created ASC LIMIT 1) " +
                    " ELSE (SELECT lc1.C_BPartner_Location_ID FROM C_BPartner_Location lc1 WHERE lc1.C_BPartner_ID = b.C_BPartner_ID ORDER BY lc1.Created ASC LIMIT 1) END " +
                    " AS Location FROM C_Project p LEFT JOIN C_BPartner b ON b.C_BPartner_ID = p.C_BPartnerSR_ID AND b.IsCustomer = 'Y' LEFT JOIN C_BPartner_Location l" +
                    " ON p.C_BPartner_ID = l.C_BPartner_ID LEFT JOIN C_BPartner_Location lc ON b.C_BPartner_ID = lc.C_BPartner_ID WHERE p.C_Project_ID = " + ID;
            }
            DataSet ds = DB.ExecuteDataset(str.ToString(), null, null);
            if (ds != null && ds.Tables[0].Rows.Count > 0)
            {
                retDic = new Dictionary<string, object>();
                retDic["Customer"] = Util.GetValueOfInt(ds.Tables[0].Rows[0]["Customer"]);
                retDic["Location"] = Util.GetValueOfInt(ds.Tables[0].Rows[0]["Location"]);
            }
            return retDic;
        }


        /// <summary>
        /// VAI094:for fetching M_PRODUCT_ID,C_UOM_ID from database according to Id  selected in 
        /// projectphase or project task or product field in Report line tab in time and expense report window
        /// </summary>
        /// <param name="fields">string fields </param>
        /// <returns>M_PRODUCT_ID,C_UOM_ID</returns>
        public Dictionary<string, object> LoadProductData(string fields)
        {
            Dictionary<string, object> retDic = null;
            string[] paramValue = fields.Split(',');
            int ID = Util.GetValueOfInt(paramValue[0]);
            var columnName = paramValue[1];
            StringBuilder str = new StringBuilder();
            int productId;
            if (columnName != null && ID > 0)
            {
                if (columnName == "C_ProjectPhase_ID")
                    str.Append("SELECT M_Product_ID FROM C_ProjectPhase WHERE C_ProjectPhase_ID= " + Util.GetValueOfInt(ID));
                else if (columnName == "C_ProjectTask_ID")
                    str.Append("SELECT M_Product_ID FROM C_ProjectTask WHERE C_ProjectTask_ID = " + Util.GetValueOfInt(ID));

                productId = Util.GetValueOfInt(DB.ExecuteScalar(str.ToString(), null, null));
                if (productId > 0)
                {
                    retDic = new Dictionary<string, object>();
                    retDic["M_Product_ID"] = productId;
                    str.Clear();
                    str.Append("SELECT C_UOM_ID FROM M_Product WHERE M_Product_ID= " + Util.GetValueOfInt(productId));
                    retDic["C_UOM_ID"] = Util.GetValueOfInt(DB.ExecuteScalar(str.ToString(), null, null));
                }
            }
            return retDic;
        }
    }
}





