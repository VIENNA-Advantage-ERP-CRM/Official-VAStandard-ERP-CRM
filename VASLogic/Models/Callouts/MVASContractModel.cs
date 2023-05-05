/********************************************************
    * Project Name   : Vienna Standard
    * Class Name     : MVASContractModel
    * Chronological  : Development
    * Manjot         : 07/FEB/2023
******************************************************/
using System;
using System.Collections.Generic;
using System.Data;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using VAdvantage.DataBase;
using VAdvantage.Utility;

namespace VIS.Models
{
    public class MVASContractModel
    {
        /// <summary>
        /// GetContract Detail
        /// </summary>
        /// <param name="ctx"></param>
        /// <param name="fields"></param>
        /// <returns></returns>
        public Dictionary<String, String> GetContractDetails(Ctx ctx, string fields)
        {
            string[] paramValue = fields.Split(',');
            int VAS_Contract_ID;
            Dictionary<String, String> retDic = new Dictionary<string, string>();
            //Assign parameter value
            VAS_Contract_ID = Util.GetValueOfInt(paramValue[0].ToString());
            //End Assign parameter value

            DataSet ds = DB.ExecuteDataset(@" SELECT Bill_Location_ID, 
                        Bill_User_ID, C_BPartner_ID, VAS_TERMINATIONREASON,
                        C_Currency_ID , C_IncoTerm_ID, C_PaymentTerm_ID,
                        C_Project_ID, CANCELBEFOREDAYS, CONTRACTTYPE,
                        CYCLES, DATEDOC, ENDDATE, M_PriceList_ID,
                        RENEWALTYPE, STARTDATE, VAS_CONTRACTAMOUNT,
                        VAS_ContractCategory_ID, VAS_CONTRACTDURATION,
                        VAS_ContractSummary,  VAS_CONTRACTUTILIZEDAMOUNT,
                        VAS_JURISDICTION, VAS_OVERLIMIT, VAS_RENEWCONTRACT,
                        VAS_RENEWALDATE, VAS_RENEWALTERM, VAS_TERMINATE,
                        VAS_TERMINATIONDATE, VA009_PaymentMethod_ID,IsExpiredContracts " + (Env.IsModuleInstalled("VA097_") ? " , VA097_VendorDetails_ID " : "") +
                        " FROM VAS_ContractMaster WHERE VAS_ContractMaster_ID = " + VAS_Contract_ID, null, null);

            if (ds != null && ds.Tables.Count > 0 && ds.Tables[0].Rows.Count > 0)
            {
                for (int i = 0; i < ds.Tables[0].Rows.Count; i++)
                {
                    retDic["Bill_Location_ID"] = ds.Tables[0].Rows[i]["Bill_Location_ID"].ToString();
                    retDic["Bill_User_ID"] = ds.Tables[0].Rows[i]["Bill_User_ID"].ToString();
                    retDic["C_BPartner_ID"] = ds.Tables[0].Rows[i]["C_BPartner_ID"].ToString();
                    retDic["C_Currency_ID"] = ds.Tables[0].Rows[i]["C_Currency_ID"].ToString();
                    retDic["C_IncoTerm_ID"] = ds.Tables[0].Rows[i]["C_IncoTerm_ID"].ToString();
                    retDic["C_PaymentTerm_ID"] = ds.Tables[0].Rows[i]["C_PaymentTerm_ID"].ToString();
                    retDic["C_Project_ID"] = ds.Tables[0].Rows[i]["C_Project_ID"].ToString();
                    retDic["M_PriceList_ID"] = ds.Tables[0].Rows[i]["M_PriceList_ID"].ToString();
                    retDic["VA009_PaymentMethod_ID"] = ds.Tables[0].Rows[i]["VA009_PaymentMethod_ID"].ToString();
                    retDic["VAS_ContractCategory_ID"] = ds.Tables[0].Rows[i]["VAS_ContractCategory_ID"].ToString();
                    retDic["ContractType"] = ds.Tables[0].Rows[i]["ContractType"].ToString();
                    retDic["VAS_Jurisdiction"] = ds.Tables[0].Rows[i]["VAS_Jurisdiction"].ToString();
                    retDic["VAS_ContractSummary"] = ds.Tables[0].Rows[i]["VAS_ContractSummary"].ToString();
                    if (Env.IsModuleInstalled("VA097_"))//VIS0336_changes done for setting the vendor details id on purchase order window
                    {
                        retDic["VA097_VendorDetails_ID"] = ds.Tables[0].Rows[i]["VA097_VendorDetails_ID"].ToString();
                    }              
                    retDic["IsExpiredContracts"] = ds.Tables[0].Rows[i]["IsExpiredContracts"].ToString();
                }
            }


            return retDic;

        }
        /// <summary>
        /// On Business Partner and ContractType according Field should be Updated
        /// </summary>
        /// <param name="ctx"></param>
        /// <param name="fields"></param>
        /// <returns></returns>
        public Dictionary<string, object> GetBPartnerData(Ctx ctx, string fields)
        {
            string[] paramValue = fields.Split(',');
            bool countVA009 = Util.GetValueOfBool(paramValue[0]);
            int C_BPartner_ID = Util.GetValueOfInt(paramValue[1]);
            Dictionary<string, object> retDic = null;
            string sql = "SELECT p.AD_Language, p.C_PaymentTerm_ID, COALESCE(p.M_PriceList_ID, g.M_PriceList_ID) AS M_PriceList_ID,"
                + "p.PaymentRule,p.SO_Description,p.C_IncoTerm_ID,p.C_IncoTermPO_ID, ";
            if (countVA009)
            {
                sql += " p.VA009_PaymentMethod_ID, p.VA009_PO_PaymentMethod_ID,";
            }
            sql += " lship.C_BPartner_Location_ID,c.AD_User_ID,"
                + " COALESCE(p.PO_PriceList_ID,g.PO_PriceList_ID) AS PO_PriceList_ID, p.PaymentRulePO,p.PO_PaymentTerm_ID,"
                + " lbill.C_BPartner_Location_ID AS Bill_Location_ID,lbill.IsShipTo,p.VA068_TaxJurisdiction"
                + " FROM C_BPartner p"
                + " INNER JOIN C_BP_Group g ON (p.C_BP_Group_ID=g.C_BP_Group_ID)"
                + " LEFT OUTER JOIN C_BPartner_Location lbill ON (p.C_BPartner_ID=lbill.C_BPartner_ID AND lbill.IsBillTo='Y' AND lbill.IsActive='Y')"
                + " LEFT OUTER JOIN C_BPartner_Location lship ON (p.C_BPartner_ID=lship.C_BPartner_ID AND lship.IsShipTo='Y' AND lship.IsActive='Y')"
                + " LEFT OUTER JOIN AD_User c ON (p.C_BPartner_ID=c.C_BPartner_ID) "
                + "WHERE p.C_BPartner_ID=" + C_BPartner_ID + " AND p.IsActive='Y'";		//	#1
            DataSet ds = DB.ExecuteDataset(sql, null, null);
            if (ds != null && ds.Tables[0].Rows.Count > 0)
            {
                retDic = new Dictionary<string, object>();
                retDic["C_PaymentTerm_ID"] = Util.GetValueOfInt(ds.Tables[0].Rows[0]["C_PaymentTerm_ID"]);
                retDic["M_PriceList_ID"] = Util.GetValueOfInt(ds.Tables[0].Rows[0]["M_PriceList_ID"]);
                retDic["VA068_TaxJurisdiction"] = Util.GetValueOfString(ds.Tables[0].Rows[0]["VA068_TaxJurisdiction"]);
                if (countVA009)
                {
                    retDic["VA009_PaymentMethod_ID"] = Util.GetValueOfInt(ds.Tables[0].Rows[0]["VA009_PaymentMethod_ID"]);
                    retDic["VA009_PaymentBaseType"] = Util.GetValueOfString(DB.ExecuteScalar("SELECT VA009_PaymentBaseType FROM VA009_PaymentMethod WHERE VA009_PaymentMethod_ID="
                        + Util.GetValueOfInt(ds.Tables[0].Rows[0]["VA009_PaymentMethod_ID"]), null, null));
                    retDic["VA009_PO_PaymentMethod_ID"] = Util.GetValueOfInt(ds.Tables[0].Rows[0]["VA009_PO_PaymentMethod_ID"]);
                }

                retDic["PO_PriceList_ID"] = Util.GetValueOfInt(ds.Tables[0].Rows[0]["PO_PriceList_ID"]);
                retDic["PaymentRulePO"] = Util.GetValueOfString(ds.Tables[0].Rows[0]["PaymentRulePO"]);
                retDic["PO_PaymentTerm_ID"] = Util.GetValueOfInt(ds.Tables[0].Rows[0]["PO_PaymentTerm_ID"]);
                retDic["C_BPartner_Location_ID"] = Util.GetValueOfInt(ds.Tables[0].Rows[0]["C_BPartner_Location_ID"]);
                retDic["AD_User_ID"] = Util.GetValueOfInt(ds.Tables[0].Rows[0]["AD_User_ID"]);
                retDic["Bill_BPartner_ID"] = Util.GetValueOfInt(DB.ExecuteScalar("SELECT C_BPartnerRelation_ID FROM C_BP_Relation WHERE C_BPartner_ID = " + C_BPartner_ID, null, null));
                retDic["Bill_Location_ID"] = Util.GetValueOfInt(ds.Tables[0].Rows[0]["Bill_Location_ID"]);
                retDic["C_IncoTerm_ID"] = Util.GetValueOfInt(ds.Tables[0].Rows[0]["C_IncoTerm_ID"]);
                retDic["C_IncoTermPO_ID"] = Util.GetValueOfInt(ds.Tables[0].Rows[0]["C_IncoTermPO_ID"]);
            }
            return retDic;
        }

        /// <summary>
        /// Get Product UOM for Contract master window's contract line tab
        /// </summary>
        /// <param name="fields"></param>
        /// <returns></returns>
        public int GetProductUOM(string fields)
        {
            string sql = @"SELECT C_UOM_ID FROM M_Product WHERE M_Product_ID=" + Util.GetValueOfInt(fields);
            int UOM=Util.GetValueOfInt(DB.ExecuteScalar(sql, null, null));
            return UOM;
        }
    }
}
