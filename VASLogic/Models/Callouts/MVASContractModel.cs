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
            DataSet ds = DB.ExecuteDataset(@" SELECT BILL_LOCATION_ID, 
                        BILL_USER_ID, C_BPARTNER_ID, VAS_TERMINATIONREASON,
                        C_CURRENCY_ID , C_INCOTERM_ID, C_PAYMENTTERM_ID,
                        C_PROJECT_ID, CANCELBEFOREDAYS, CONTRACTTYPE,
                        CYCLES, DATEDOC, ENDDATE, M_PRICELIST_ID,
                        RENEWALTYPE, STARTDATE, VAS_CONTRACTAMOUNT,
                        VAS_CONTRACTCATEGORY_ID, VAS_CONTRACTDURATION,
                        VAS_CONTRACTSUMMARY,  VAS_CONTRACTUTILIZEDAMOUNT,
                        VAS_JURISDICTION, VAS_OVERLIMIT, VAS_RENEWCONTRACT,
                        VAS_RENEWALDATE, VAS_RENEWALTERM, VAS_TERMINATE,
                        VAS_TERMINATIONDATE, VA009_PaymentMethod_ID
                        FROM VAS_ContractMaster WHERE VAS_ContractMaster_ID = " + VAS_Contract_ID , null, null);
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
                }
            }
                
            return retDic;

        }
    }
}
