﻿using System;
using System.Collections.Generic;
using System.Data;
using System.Linq;
using System.Text;
using System.Web;
using VAdvantage.Model;
using VAdvantage.Utility;
using VIS.DBase;

namespace VIS.Models
{
    public class MProjectModel
    {
        /// <summary>
        /// GetProjectDetail
        /// </summary>
        /// <param name="ctx"></param>
        /// <param name="fields"></param>
        /// <returns></returns>
        public Dictionary<string, object> GetProjectDetail(Ctx ctx, string fields)
        {
            string[] paramValue = fields.Split(',');
            int TaskID = 0, phaseID = 0, projID = 0, ProductID = 0, Attribute_ID = 0, UOM_ID = 0;
            TaskID = Util.GetValueOfInt(paramValue[0].ToString());
            phaseID = Util.GetValueOfInt(paramValue[1].ToString());
            projID = Util.GetValueOfInt(paramValue[2].ToString());
            ProductID = Util.GetValueOfInt(paramValue[3].ToString());
            Attribute_ID = Util.GetValueOfInt(paramValue[4].ToString());
            UOM_ID = Util.GetValueOfInt(paramValue[5].ToString());
            Dictionary<string, object> result = null;
            string Sql = "SELECT C_Project_ID FROM C_ProjectPhase WHERE C_ProjectPhase_ID IN (SELECT C_ProjectPhase_ID FROM" +
                    " C_ProjectTask WHERE C_ProjectTask_ID = " + TaskID + ")";
            int id = Util.GetValueOfInt(DB.ExecuteScalar(Sql, null, null));
            if (id > 0)
            {
                projID = id;
            }
            else
            {
                Sql = "SELECT C_Project_ID FROM C_ProjectPhase WHERE C_ProjectPhase_ID = " + phaseID;
                id = Util.GetValueOfInt(DB.ExecuteScalar(Sql, null, null));
                if (id > 0)
                {
                    projID = id;
                }
            }

            //Issue ID = SI_0468 Reported by Ankita Work Done by Manjot
            // To get the actual value from the right field

            Sql = "SELECT PriceList, PriceStd, PriceLimit,C_UOM_ID FROM M_ProductPrice WHERE M_PriceList_Version_ID = (SELECT c.M_PriceList_Version_ID FROM C_Project c WHERE c.C_Project_ID = "
                + projID + ")  AND M_Product_ID=" + ProductID + " AND NVL(M_AttributeSetInstance_ID,0)=" + Attribute_ID;

          
            //VAI050-Give priority to Sales UOM  On Product

            string query = "SELECT C_UOM_ID,VAS_SalesUOM_ID FROM M_Product WHERE M_Product_ID= " + ProductID;
            DataSet ds1 = DB.ExecuteDataset(query, null, null);

            if (ds1 != null && ds1.Tables.Count > 0 && ds1.Tables[0].Rows.Count > 0)
            {
                if (Util.GetValueOfInt(ds1.Tables[0].Rows[0]["VAS_SalesUOM_ID"]) > 0)
                {
                    Sql += " AND C_UOM_ID =" + Util.GetValueOfInt(ds1.Tables[0].Rows[0]["VAS_SalesUOM_ID"]);
                }
                else
                {
                    if (UOM_ID > 0)
                    {
                        Sql += " AND C_UOM_ID=" + UOM_ID;
                    }
                    else
                    {
                        Sql += " AND C_UOM_ID =" + Util.GetValueOfInt(ds1.Tables[0].Rows[0]["C_UOM_ID"]);
                    }

                }
            }
            else
            {
                if (UOM_ID > 0)
                {
                    Sql += " AND C_UOM_ID=" + UOM_ID;
                }                                                                                                            
            }

            DataSet ds = DB.ExecuteDataset(Sql, null, null);
            if (ds != null && ds.Tables[0].Rows.Count > 0)
            {
                result = new Dictionary<string, object>();
                result["PriceList"] = Util.GetValueOfDecimal(ds.Tables[0].Rows[0]["PriceList"]);
                result["PriceStd"] = Util.GetValueOfDecimal(ds.Tables[0].Rows[0]["PriceStd"]);
                result["PriceLimit"] = Util.GetValueOfDecimal(ds.Tables[0].Rows[0]["PriceLimit"]);
                result["C_UOM_ID"] = Util.GetValueOfInt(ds.Tables[0].Rows[0]["C_UOM_ID"]);
            }

            return result;
        }

        /// <summary>
        /// (VAI094):Fill Product Type on selection of Product
        /// </summary>
        /// <param name="fields">fields</param>
        /// <returns>product type</returns>
        public string GetProductType(Ctx ctx, string fields)
        {
            string Sql = "SELECT ProductType FROM M_Product WHERE M_Product_ID = " + Util.GetValueOfInt(fields);
            string type = Util.GetValueOfString(DB.ExecuteScalar(Sql, null, null));
            return type;
        }

        // Added by Bharat on 23 May 2017
        public Decimal GetProjectPriceLimit(Ctx ctx, string fields)
        {
            string[] paramValue = fields.Split(',');
            int TaskID = 0, projID = 0, ProductID = 0;
            TaskID = Util.GetValueOfInt(paramValue[0].ToString());
            projID = Util.GetValueOfInt(paramValue[1].ToString());
            ProductID = Util.GetValueOfInt(paramValue[2].ToString());
            string Sql = "SELECT C_Project_ID FROM C_ProjectPhase WHERE C_ProjectPhase_ID IN (SELECT C_ProjectPhase_ID FROM" +
                    " C_ProjectTask WHERE C_ProjectTask_ID = " + TaskID + ")";
            int id = Util.GetValueOfInt(DB.ExecuteScalar(Sql, null, null));
            if (id > 0)
            {
                projID = id;
            }

            Sql = "SELECT PriceLimit FROM M_ProductPrice WHERE M_PriceList_Version_ID = (SELECT c.M_PriceList_Version_ID FROM  C_Project c WHERE c.C_Project_ID = "
                + projID + ")  AND M_Product_ID=" + ProductID;
            Decimal PriceLimit = Util.GetValueOfDecimal(DB.ExecuteScalar(Sql, null, null));
            return PriceLimit;
        }
        /// <summary>
        /// VIS0336:for setting the projectline details on req line tabs
        /// </summary>
        /// <param name="ctx"></param>
        /// <param name="fields"></param>
        /// <returns>Project line detail</returns>
        public Dictionary<string, object> GetReqLinesProjectDetail(Ctx ctx, string fields)
        {
            Dictionary<string, object> retDic = null;
            StringBuilder str = new StringBuilder();
            str.Append("SELECT M_Product_ID,M_AttributeSetInstance_ID,C_Charge_ID,C_UOM_ID,PlannedPrice,PlannedQty from C_ProjectLine WHERE C_ProjectLine_ID=" + Util.GetValueOfInt(fields));
            DataSet ds = DB.ExecuteDataset(str.ToString(), null, null);
            if (ds != null && ds.Tables.Count > 0 && ds.Tables[0].Rows.Count > 0)
            {
                for (int i = 0; i < ds.Tables[0].Rows.Count; i++)
                {
                    retDic = new Dictionary<string, object>();
                    retDic["ProductID"] = Util.GetValueOfInt(ds.Tables[0].Rows[0]["M_Product_ID"]);
                    retDic["AtrrInstance"] = Util.GetValueOfInt(ds.Tables[0].Rows[0]["M_AttributeSetInstance_ID"]);
                    retDic["ChargeID"] = Util.GetValueOfInt(ds.Tables[0].Rows[0]["C_Charge_ID"]);
                    retDic["UOM"] = Util.GetValueOfDecimal(ds.Tables[0].Rows[0]["C_UOM_ID"]);
                    retDic["QtyEntered"] = Util.GetValueOfDecimal(ds.Tables[0].Rows[0]["PlannedQty"]);
                    retDic["PriceActual"] = Util.GetValueOfDecimal(ds.Tables[0].Rows[0]["PlannedPrice"]);

                }
            }
            return retDic;
        }

    }
}