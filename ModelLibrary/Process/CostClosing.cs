using ModelLibrary.Classes;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using VAdvantage.Logging;
using VAdvantage.ProcessEngine;
using VAdvantage.Utility;

namespace ModelLibrary.Process
{
    public class CostClosing : SvrProcess
    {
        private string message = string.Empty;
        private string productID = string.Empty;
        private string productCategoryID = string.Empty;

        protected override void Prepare()
        {
            ProcessInfoParameter[] para = GetParameter();
            for (int i = 0; i < para.Length; i++)
            {
                String name = para[i].GetParameterName();
                if (para[i].GetParameter() == null)
                {
                    ;
                }
                else if (name.Equals("M_Product_Category_ID"))
                {
                    productCategoryID = Util.GetValueOfString(para[i].GetParameter());
                }
                if (name.Equals("M_Product_ID"))
                {
                    productID = Util.GetValueOfString(para[i].GetParameter());
                }
                else
                {
                    log.Log(Level.SEVERE, "Unknown Parameter: " + name);
                }
            }
        }

        protected override string DoIt()
        {
            CostingCheck costingCheck = new CostingCheck(GetCtx());
            message = costingCheck.InsertCostClosing(productCategoryID, productID, Get_Trx());
            if (!string.IsNullOrEmpty(message))
            {
                return message;
            }
            return Msg.GetMsg(GetCtx(), "VAS_CostClosingInsertedSuccess");
        }
    }
}
