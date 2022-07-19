using CoreLibrary.DataBase;
using System;
using System.Collections.Generic;
using System.Data;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using VAdvantage.Utility;

namespace VASLogic.Models
{
    public class MPaySelectionModel
    { 

        /// <summary>
        /// GetPaySelection
        /// </summary>
        /// <param name="ctx"></param>
        /// <param name="fields"></param>
        /// <returns></returns>
        public Dictionary<String, object> GetInvoice(Ctx ctx, string fields)
        {
            string[] paramValue = fields.Split(',');
            //Creating Object
            Dictionary<String, object> retDic = new Dictionary<string, object>();
            var sql = "SELECT currencyConvert(invoiceOpen(i.C_Invoice_ID, 0), i.C_Currency_ID,"
            + "ba.C_Currency_ID, i.DateInvoiced, i.C_ConversionType_ID, i.AD_Client_ID, i.AD_Org_ID) as OpenAmt,"
            + " paymentTermDiscount(i.GrandTotal,i.C_Currency_ID,i.C_PaymentTerm_ID,i.DateInvoiced,'" + paramValue[2] + "') As DiscountAmt, i.IsSOTrx "
            + "FROM C_Invoice_v i, C_BankAccount ba "
            + "WHERE i.C_Invoice_ID="+paramValue[0]+"AND ba.C_BankAccount_ID="+paramValue[1]+"";
            DataSet ds = DB.ExecuteDataset(sql, null, null);
            if (ds != null && ds.Tables[0].Rows.Count > 0)
            {
               //retDic["InvoiceId"] = Util.GetValueOfInt(ds.Tables[0].Rows[0]["C_Invoice_ID"]);
                retDic["OpenAmt"] = Util.GetValueOfDecimal(ds.Tables[0].Rows[0]["OpenAmt"]);
                retDic["DiscountAmt"] = Util.GetValueOfDecimal(ds.Tables[0].Rows[0]["DiscountAmt"]);
                retDic["IsSOTrx"] = Util.GetValueOfString(ds.Tables[0].Rows[0]["IsSOTrx"]);

            }
            return retDic;
        }
    }

}