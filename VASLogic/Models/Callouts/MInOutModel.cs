﻿using System;
using System.Collections.Generic;
using System.Data;
using System.Linq;
using System.Web;
using VAdvantage.DataBase;
using VAdvantage.Model;
using VAdvantage.Utility;


namespace VIS.Models
{
    public class MInOutModel
    {
        /// <summary>
        /// GetInOut
        /// </summary>
        /// <param name="ctx"></param>
        /// <param name="param"></param>
        /// <returns></returns>
        public Dictionary<String, String> GetInOut(Ctx ctx, string param)
        {
            string[] paramValue = param.Split(',');
            int Orig_InOut_ID;

            //Assign parameter value
            Orig_InOut_ID = Util.GetValueOfInt(paramValue[0].ToString());
            MInOut io = new MInOut(ctx, Orig_InOut_ID, null);
            //End Assign parameter

            Dictionary<String, String> retDic = new Dictionary<string, string>();
            //retDic["MovementDate"] = io.GetMovementDate().ToString();
            retDic["C_Project_ID"] = io.GetC_Project_ID().ToString();
            retDic["C_Campaign_ID"] = io.GetC_Campaign_ID().ToString();
            retDic["C_Activity_ID"] = io.GetC_Activity_ID().ToString();
            retDic["AD_OrgTrx_ID"] = io.GetAD_OrgTrx_ID().ToString();
            retDic["User1_ID"] = io.GetUser1_ID().ToString();
            retDic["User2_ID"] = io.GetUser2_ID().ToString();
            retDic["IsDropShip"] = io.IsDropShip() ? "Y" : "N";
            retDic["M_Warehouse_ID"] = io.GetM_Warehouse_ID().ToString();
            return retDic;
        }

        // Added by Bharat on 19 May 2017
        public Dictionary<String, Object> GetWarehouse(Ctx ctx, string param)
        {
            int M_Warehouse_ID = Util.GetValueOfInt(param);
            Dictionary<string, object> retDic = null;
            string sql = "SELECT w.AD_Org_ID, l.M_Locator_ID"
            + " FROM M_Warehouse w"
            + " LEFT OUTER JOIN M_Locator l ON (l.M_Warehouse_ID=w.M_Warehouse_ID AND l.IsDefault='Y') "
            + "WHERE w.M_Warehouse_ID=" + M_Warehouse_ID;		//	1
            DataSet ds = DB.ExecuteDataset(sql, null, null);
            if (ds != null && ds.Tables[0].Rows.Count > 0)
            {
                retDic = new Dictionary<string, object>();
                retDic["AD_Org_ID"] = Util.GetValueOfInt(ds.Tables[0].Rows[0]["AD_Org_ID"]);
                retDic["M_Locator_ID"] = Util.GetValueOfInt(ds.Tables[0].Rows[0]["M_Locator_ID"]);
            }
            return retDic;
        }

        // Change by mohit to remove client side queries- 19 May 2017
        public Dictionary<string, object> GetDocumentTypeData(string fields)
        {
            Dictionary<string, object> retValue = null;
            DataSet _ds = null;
            string sql = "SELECT d.docBaseType, d.IsDocNoControlled, s.CurrentNext, d.IsReturnTrx "
           + " FROM C_DocType d, AD_Sequence s "
           + " WHERE C_DocType_ID=" + Util.GetValueOfInt(fields)
           + " AND d.DocNoSequence_ID=s.AD_Sequence_ID(+)";
            try
            {
                _ds = DB.ExecuteDataset(sql, null, null);
                if (_ds != null && _ds.Tables[0].Rows.Count > 0)
                {
                    retValue = new Dictionary<string, object>();
                    retValue["docBaseType"] = _ds.Tables[0].Rows[0]["docBaseType"].ToString();
                    retValue["IsDocNoControlled"] = _ds.Tables[0].Rows[0]["IsDocNoControlled"].ToString();
                    retValue["CurrentNext"] = _ds.Tables[0].Rows[0]["CurrentNext"].ToString();
                    retValue["IsReturnTrx"] = _ds.Tables[0].Rows[0]["IsReturnTrx"].ToString();
                }
            }
            catch (Exception e)
            {
                if (_ds != null)
                {
                    _ds.Dispose();
                    _ds = null;
                }
            }
            return retValue;
        }

        // Get Locator from warehouse

        public int GetWarehouseLocator(string fields)
        {
            return Util.GetValueOfInt(DB.ExecuteScalar("SELECT MIN(M_Locator_ID) FROM M_Locator WHERE IsActive = 'Y' AND M_Warehouse_ID = " + Util.GetValueOfInt(fields), null, null));
        }
        //Get UOM Conversion
        public Dictionary<string, object> GetUOMConversion(Ctx ctx, string fields)
        {
            Dictionary<string, object> retValue = null;
            string[] paramString = fields.Split(',');
            int M_Product_ID = Util.GetValueOfInt(paramString[1]);
            int QtyEntered = Util.GetValueOfInt(paramString[2]);
            int C_BPartner_ID = Util.GetValueOfInt(paramString[3]);
            //MInOut inout = new MInOut(ctx, Util.GetValueOfInt(paramString[0]), null);
            if (C_BPartner_ID == 0)
            {
                C_BPartner_ID = Util.GetValueOfInt(DB.ExecuteScalar("SELECT C_BPartner_ID FROM M_InOut WHERE M_InOut_ID =" + Util.GetValueOfInt(paramString[0]), null, null));
            }
            int C_UOM_ID = Util.GetValueOfInt(DB.ExecuteScalar("SELECT C_UOM_ID FROM M_Product WHERE M_Product_ID =" + M_Product_ID, null, null));

            try
            {
                int uom = 0;
                retValue = new Dictionary<string, object>();
                uom = Util.GetValueOfInt(DB.ExecuteScalar("SELECT vdr.C_UOM_ID FROM M_Product p LEFT JOIN M_Product_Po vdr ON p.M_Product_ID= vdr.M_Product_ID " +
                                                             "WHERE p.M_Product_ID=" + M_Product_ID + " AND vdr.C_BPartner_ID = " + C_BPartner_ID, null, null));
                //VAI050-If Product have Purchasing unit than give priority Purchasing unit  from Purchasing tab
                //If not found than give priority to PU uni which is define on Product else set Base UOM of product
                if (uom == 0) 
                {
                    uom = Util.GetValueOfInt(DB.ExecuteScalar("SELECT VAS_PurchaseUOM_ID FROM M_Product WHERE M_Product_ID =" + M_Product_ID, null, null));
                }
                if (C_UOM_ID != 0)
                {
                    if (C_UOM_ID != uom && uom != 0)
                    {
                        retValue["qtyentered"] = MUOMConversion.ConvertProductFrom(ctx, M_Product_ID, uom, QtyEntered);
                        if (retValue["qtyentered"] == null)
                        {
                            retValue["qtyentered"] = QtyEntered;
                        }
                    }
                }
                retValue["C_UOM_ID"] = C_UOM_ID;
                retValue["uom"] = uom;
            }
            catch (Exception e)
            {

            }
            return retValue;
        }
        /// <summary>
        /// Get Product Details 
        /// </summary>
        /// <param name="ctx">Parameters</param>
        /// <param name="fields"></param>
        /// <returns>Product id, Quantity, Attribute Id,totalConfirmedAndScrapped</returns>
        public Dictionary<string, object> GetProductDetails(Ctx ctx, string fields)
        {
            string[] paramValue = fields.Split(',');
            Dictionary<string, object> obj = null;
            string sql1 = "SELECT SUM(ConfirmedQty +scrappedqty) FROM M_PackageLine WHERE M_Package_ID = " + paramValue[1] + " and m_inoutline_id = " + paramValue[2];
            obj = new Dictionary<string, object>();
            obj["totalConfirmedAndScrapped"] = Util.GetValueOfDecimal(DB.ExecuteScalar(sql1, null, null));
            string sql = @"SELECT M_Product_ID , movementqty , M_AttributeSetInstance_ID FROM M_InOutLine WHERE M_InOutLine_ID=" + paramValue[0];
            DataSet ds = DB.ExecuteDataset(sql, null, null);
            if (ds != null && ds.Tables[0].Rows.Count > 0)
            {

                obj["M_Product_ID"] = Util.GetValueOfString(ds.Tables[0].Rows[0]["M_Product_ID"]);
                obj["Movementqty"] = Util.GetValueOfInt(ds.Tables[0].Rows[0]["movementqty"]);
                obj["M_AttributeSetInstance_ID"] = Util.GetValueOfInt(ds.Tables[0].Rows[0]["M_AttributeSetInstance_ID"]);
            }
            return obj;
        }


        /// <summary>
        /// VAI050-Get Fleet Detail of Shipper
        /// </summary>
        /// <param name="ctx"></param>
        /// <param name="param"></param>
        /// <returns>return fleet detail</returns>
        public Dictionary<string, object> GetFleetDetail(Ctx ctx, string param)
        {

            Dictionary<string, object> retDic = null;
            string sql = @"SELECT VAS_VehicleRegistrationNo,VAS_GrossWeight,VAS_TareWeight FROM VAS_FleetDetail" +
                        " WHERE VAS_FleetDetail_ID=" + Util.GetValueOfInt(param);
            DataSet ds = DB.ExecuteDataset(sql, null, null);
            if (ds != null && ds.Tables.Count > 0 && ds.Tables[0].Rows.Count > 0)
            {
                retDic = new Dictionary<string, object>();
                retDic["VAS_VehicleRegistrationNo"] = Util.GetValueOfString(ds.Tables[0].Rows[0]["VAS_VehicleRegistrationNo"]);
                retDic["VAS_GrossWeight"] = Util.GetValueOfDecimal(ds.Tables[0].Rows[0]["VAS_GrossWeight"]);
                retDic["VAS_TareWeight"] = Util.GetValueOfDecimal(ds.Tables[0].Rows[0]["VAS_TareWeight"]);
            }
            return retDic;
        }

        /// <summary>
        /// VAI050-Get Shipper Detail of Freight Carrier
        /// </summary>
        /// <param name="ctx"></param>
        /// <param name="param"></param>
        /// <returns>returns freight carrier details</returns>
        public Dictionary<string, object> GetShipperDetail(Ctx ctx, string param)
        {

            Dictionary<string, object> retDic = null;
            string sql = @"SELECT s.C_BPartner_ID,cb.TaxID FROM M_Shipper s
                           INNER JOIN C_BPartner cb ON cb.C_BPartner_ID=s.C_BPartner_ID
                           WHERE s.M_Shipper_ID=" + Util.GetValueOfInt(param);
            DataSet ds = DB.ExecuteDataset(sql, null, null);
            if (ds != null && ds.Tables.Count > 0 && ds.Tables[0].Rows.Count > 0)
            {
                retDic = new Dictionary<string, object>();
                retDic["C_BPartner_ID"] = Util.GetValueOfInt(ds.Tables[0].Rows[0]["C_BPartner_ID"]);
                retDic["TaxID"] = Util.GetValueOfString(ds.Tables[0].Rows[0]["TaxID"]);
            }
            return retDic;
        }

        /// <summary>
        /// This function is use to get the value of Transportation Mode
        /// on selection of Freight category
        /// </summary>
        /// <param name="fields">M_FreightCategory_ID</param>
        /// <returns>returns Search key of freight category</returns>
        /// <author>VIS_427</author>
        public Dictionary<string, string> GetFreightCategory(Ctx ctx, string M_FreightCategory_ID)
        {

            Dictionary<string, string> retDic = null;
            string sql = @"SELECT VAS_Category FROM M_FreightCategory WHERE M_FreightCategory_ID=" + Util.GetValueOfInt(M_FreightCategory_ID);
            DataSet ds = DB.ExecuteDataset(sql, null, null);
            if (ds != null && ds.Tables.Count > 0 && ds.Tables[0].Rows.Count > 0)
            {
                retDic = new Dictionary<string, string>();
                retDic["VAS_Category"] = Util.GetValueOfString(ds.Tables[0].Rows[0]["VAS_Category"]);
            }
            return retDic;
        }
    }
}