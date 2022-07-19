using CoreLibrary.DataBase;
using System;
using System.Collections.Generic;
using System.Data;
using System.Linq;
using System.Web;
using VAdvantage.Model;
using VAdvantage.Utility;

namespace VIS.Models
{
    public class MInvoiceBatchLineModel
    {
        /// <summary>
        /// GetInvoiceBatchLine
        /// </summary>
        /// <param name="ctx"></param>
        /// <param name="fields"></param>
        /// <returns></returns>
        public Dictionary<String, String> GetInvoiceBatchLine(Ctx ctx, string fields)
        {
            string[] paramValue = fields.Split(',');
            int C_InvoiceBatchLine_ID;
            //decimal Qty;
            //bool isSOTrx;
            Dictionary<String, String> retDic = new Dictionary<string, string>();
            //Assign parameter value
            C_InvoiceBatchLine_ID = Util.GetValueOfInt(paramValue[0].ToString());
            //End Assign parameter value

            MInvoiceBatchLine last = new MInvoiceBatchLine(ctx, C_InvoiceBatchLine_ID, null);
            //	Need to Increase when different DocType or BP
            retDic["C_DocType_ID"] = last.GetC_DocType_ID().ToString();
            retDic["C_BPartner_ID"] = last.GetC_BPartner_ID().ToString();
            retDic["DocumentNo"] = last.GetDocumentNo();
            //	New Number
            
            return retDic;

        }
        /// <summary>
        /// Getting Charge Amt
        /// </summary>
        /// <param name="C_Charge_ID"></param>
        /// <returns>Amt</returns>
        public decimal GetCharges(int C_Charge_ID)
        {
            
            string sql = "SELECT ChargeAmt FROM C_Charge WHERE C_Charge_ID="+ C_Charge_ID + "";
            return Util.GetValueOfDecimal(DB.ExecuteScalar(sql, null, null));
             
            
        }
        /// <summary>
        /// Invoice Batch Id
        /// </summary>
        /// <param name="C_InvoiceBatch_ID"></param>
        /// <returns>ID</returns>
        public int GetMaxLines(int C_InvoiceBatch_ID)
        {
            string sql = "SELECT COALESCE(MAX(C_InvoiceBatchLine_ID),0) FROM C_InvoiceBatchLine WHERE C_InvoiceBatch_ID=" + C_InvoiceBatch_ID;
            return Util.GetValueOfInt(DB.ExecuteScalar(sql, null, null));

        }
    }
}
