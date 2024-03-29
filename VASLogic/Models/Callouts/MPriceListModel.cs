﻿using System;
using System.Collections.Generic;
using System.Data;
using System.Linq;
using System.Web;

using VAdvantage.Model;
using VAdvantage.Utility;
using VIS.DBase;

namespace VIS.Models
{
    public class MPriceListModel
    {
        /// <summary>
        /// GetPriceList
        /// </summary>
        /// <param name="ctx"></param>
        /// <param name="fields"></param>
        /// <returns></returns>
        public Dictionary<String, String> GetPriceList(Ctx ctx, string fields)
        {
            if (fields != null)
            {
                string[] paramValue = fields.ToString().Split(',');
                int M_PriceList_ID;

                //Assign parameter value
                M_PriceList_ID = Util.GetValueOfInt(paramValue[0].ToString());
                //End Assign parameter value

                MPriceList prcLst =  MPriceList.Get(ctx, M_PriceList_ID, null);
                Dictionary<String, String> retDic = new Dictionary<string, string>();
                // Reset Orig Shipment
                MCurrency crncy = MCurrency.Get(ctx, prcLst.GetC_Currency_ID());
                retDic["PriceListPrecision"] = prcLst.GetPricePrecision().ToString();
                //JID_1744  Precision should be as per currency percision 
                retDic["StdPrecision"] = crncy.GetStdPrecision().ToString();
                retDic["EnforcePriceLimit"] = prcLst.IsEnforcePriceLimit() ? "Y" : "N";
                retDic["IsTaxIncluded"] = prcLst.IsTaxIncluded() ? "Y" : "N";
                retDic["C_Currency_ID"] = prcLst.GetC_Currency_ID().ToString();
                return retDic;
            }
            else
            {
                return null;
            }
        }

        /// <summary>
        /// Get Price List Data
        /// </summary>
        /// <param name="ctx">Context</param>
        /// <param name="fields">Parameters</param>
        /// <returns>List of Data</returns>
        public Dictionary<String, Object> GetPriceListData(Ctx ctx, string fields)
        {
            int M_PriceList_ID;
            M_PriceList_ID = Util.GetValueOfInt(fields);
            Dictionary<String, Object> retDic = null;
            string sql = "SELECT pl.IsTaxIncluded,pl.EnforcePriceLimit,pl.C_Currency_ID,c.StdPrecision,"
            + "plv.M_PriceList_Version_ID,plv.ValidFrom "
            + "FROM M_PriceList pl,C_Currency c,M_PriceList_Version plv "
            + "WHERE pl.C_Currency_ID=c.C_Currency_ID"
            + " AND pl.M_PriceList_ID=plv.M_PriceList_ID"
            + " AND pl.M_PriceList_ID=" + M_PriceList_ID                        //	1
            + " ORDER BY plv.ValidFrom DESC";
            DataSet ds = DB.ExecuteDataset(sql, null, null);
            if (ds != null && ds.Tables[0].Rows.Count > 0)
            {
                retDic = new Dictionary<String, Object>();
                retDic["IsTaxIncluded"] = Util.GetValueOfString(ds.Tables[0].Rows[0]["IsTaxIncluded"]);
                retDic["EnforcePriceLimit"] = Util.GetValueOfString(ds.Tables[0].Rows[0]["EnforcePriceLimit"]);
                retDic["StdPrecision"] = Util.GetValueOfInt(ds.Tables[0].Rows[0]["StdPrecision"]);
                retDic["C_Currency_ID"] = Util.GetValueOfInt(ds.Tables[0].Rows[0]["C_Currency_ID"]);
                retDic["M_PriceList_Version_ID"] = Util.GetValueOfInt(ds.Tables[0].Rows[0]["M_PriceList_Version_ID"]);
                retDic["ValidFrom"] = Util.GetValueOfDateTime(ds.Tables[0].Rows[0]["ValidFrom"]);
            }
            return retDic;
        }

        /// <summary>
        /// Get Price List Data When select or change the PriceList on 
        /// Provisional Invoice window
        /// </summary>
        /// <param name="ctx">Context</param>
        /// <param name="fields">Parameters</param>
        /// <returns>List of Data</returns>
        public Dictionary<String, Object> GetPriceListDataForProvisionalInvoice(Ctx ctx, string fields)
        {
            int M_PriceList_ID = Util.GetValueOfInt(fields);
            Dictionary<String, Object> retDic = null;

            string sql = "SELECT pl.IsTaxIncluded,pl.EnforcePriceLimit,pl.C_Currency_ID,"
            + "plv.M_PriceList_Version_ID "
            + "FROM M_PriceList pl,C_Currency c,M_PriceList_Version plv "
            + "WHERE pl.C_Currency_ID=c.C_Currency_ID"
            + " AND pl.M_PriceList_ID=plv.M_PriceList_ID"
            + " AND pl.M_PriceList_ID=" + M_PriceList_ID
            + " ORDER BY plv.ValidFrom DESC";
            DataSet ds = DB.ExecuteDataset(sql, null, null);
            if (ds != null && ds.Tables[0].Rows.Count > 0)
            {
                retDic = new Dictionary<String, Object>();
                retDic["IsTaxIncluded"] = Util.GetValueOfString(ds.Tables[0].Rows[0]["IsTaxIncluded"]);
                retDic["EnforcePriceLimit"] = Util.GetValueOfString(ds.Tables[0].Rows[0]["EnforcePriceLimit"]);
                retDic["C_Currency_ID"] = Util.GetValueOfInt(ds.Tables[0].Rows[0]["C_Currency_ID"]);
                retDic["M_PriceList_Version_ID"] = Util.GetValueOfInt(ds.Tables[0].Rows[0]["M_PriceList_Version_ID"]);
            }
            return retDic;
        }

        /// <summary>
        /// Get Tax Included
        /// </summary>
        /// <param name="ctx">Ctx</param>
        /// <param name="fields">PriceList_ID</param>
        /// <returns>IsTaxIncluded</returns>
        public string GetTaxIncluded(Ctx ctx, string fields)
        {
            string sql = "SELECT IsTaxIncluded FROM M_PriceList WHERE M_PriceList_ID=" + Util.GetValueOfInt(fields);
            string IsTaxIncluded = Util.GetValueOfString(DB.ExecuteScalar(sql, null, null));
            return IsTaxIncluded;
        }
        /// <summary>
        ///This method is used to Get Price list
        /// </summary>
        /// <param name="fields">M_PriceList_Version_ID</param>
        /// <returns>M_PriceList id</returns>
        public Dictionary<string, object> GetM_PriceList(Ctx ctx, string fields)
        {
            Dictionary<string, object> obj = null;
            string sql = @"SELECT  PL.M_PriceList_ID, PL.C_Currency_id FROM m_pricelist PL
                          INNER JOIN  M_PriceList_Version PLV ON PL.M_PriceList_ID = PLV.M_PriceList_ID
                          WHERE PLV.M_PriceList_Version_ID =" + Util.GetValueOfInt(fields);
            DataSet ds = DB.ExecuteDataset(sql, null, null);
            if (ds != null && ds.Tables[0].Rows.Count > 0)
            {
                obj = new Dictionary<string, object>();
                obj["M_PriceList_ID"] = ds.Tables[0].Rows[0]["M_PriceList_ID"];
                obj["C_Currency_id"] = ds.Tables[0].Rows[0]["C_Currency_id"];
            }
            return obj;
        }
       
    }
}