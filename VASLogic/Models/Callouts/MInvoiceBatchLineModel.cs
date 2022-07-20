using CoreLibrary.DataBase;
using System;
using System.Collections.Generic;
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
        /// Get Invoice Batch Line Details
        /// </summary>
        /// <param name="ctx">Context</param>
        /// <param name="fields">C_InvoiceBatch_ID</param>
        /// <returns>Batch Line Details</returns>
        public Dictionary<String, String> GetInvoiceBatchLineDetail(Ctx ctx, string fields)
        {
            string[] paramValue = fields.Split(',');
            int C_InvoiceBatchLine_ID = 0;
            int C_InvoiceBatch_ID = 0;
            Dictionary<String, String> retDic = new Dictionary<string, string>();

            //Assign parameter value
            C_InvoiceBatch_ID = Util.GetValueOfInt(paramValue[0].ToString());

            // Get Max Batch Line Detail
            C_InvoiceBatchLine_ID = Util.GetValueOfInt(DB.ExecuteScalar(@"SELECT COALESCE(MAX(C_InvoiceBatchLine_ID),0) FROM C_InvoiceBatchLine
                                WHERE C_InvoiceBatch_ID=" + C_InvoiceBatch_ID, null, null));
            if (C_InvoiceBatchLine_ID == 0)
            {
                return null;
            }

            MInvoiceBatchLine last = new MInvoiceBatchLine(ctx, C_InvoiceBatchLine_ID, null);
            //	Need to Increase when different DocType or BP
            retDic["C_DocType_ID"] = last.GetC_DocType_ID().ToString();
            retDic["C_BPartner_ID"] = last.GetC_BPartner_ID().ToString();
            retDic["DocumentNo"] = last.GetDocumentNo();
            //	New Number            
            return retDic;

        }
    }
}