/********************************************************
    * Project Name   : Vienna Standard
    * Class Name     : MVASContractLine
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

namespace VAdvantage.Model
{
    //VIS0336:add class as public
    public class MVASContractLine : X_VAS_ContractLine
    {
        public MVASContractLine(Ctx ctx, int VAS_ContractLine_ID, Trx Trx_Name)
           : base(ctx, VAS_ContractLine_ID, Trx_Name)
        {
        }
        public MVASContractLine(Ctx ctx, DataRow dr, Trx trx)
            : base(ctx, dr, trx)
        {
        }
        /// <summary>
        ///	Before Save
        /// </summary>
        /// <param name="newRecord">new</param>
        /// <returns>true if can be saved</returns>
        protected override Boolean BeforeSave(Boolean newRecord)
        {
            //VAI050-To check Contract exists in SO,PO,BPO,BSO,AR Invoice,AP Invoice
            if (!newRecord && (Is_ValueChanged("M_Product_ID") || Is_ValueChanged("M_AttributeSetInstance_ID")
                || Is_ValueChanged("C_Charge_ID") || Is_ValueChanged("Amount")))
            {
                decimal amount = checkTransaction();
                if (amount > 0)
                {
                    if (Is_ValueChanged("M_Product_ID") || Is_ValueChanged("M_AttributeSetInstance_ID")
                     || Is_ValueChanged("C_Charge_ID"))
                    {
                        log.SaveError("", Msg.GetMsg(GetCtx(), "VAS_CheckOrder"));
                        return false;
                    }
                    else
                    {
                        if (Util.GetValueOfInt(GetAmount()) < amount) //Amount on line should be greater than utilize amount 
                        {
                            log.SaveError("", Msg.GetMsg(GetCtx(), "VAS_UtilizeAmount"));
                            return false;
                        }
                    }
                }
            }
            //	Product/Charge Must be selected
            if (newRecord || Is_ValueChanged("M_Product_ID") || Is_ValueChanged("C_Charge_ID"))
            {
                if (GetM_Product_ID() == 0 && GetC_Charge_ID() == 0)
                {
                    log.SaveError("", Msg.GetMsg(GetCtx(), "VAS_MsutSelectProdChrg"));
                    return false;
                }
            }
            return true;
        }

        /// <summary>
        ///After Save. Insert - create Contract Line
        /// </summary>
        /// <param name="newRecord">insert</param>
        /// <param name="success">success</param>
        /// <returns>true if inserted</returns>
        protected override bool AfterSave(bool newRecord, bool success)
        {
            if (!success)
            {
                return success;
            }
            //Update sum of all lines on Contract Master Header.
            int count = DB.ExecuteQuery(@" UPDATE VAS_ContractMaster SET VAS_ContractAmount = 
                            (SELECT SUM(AMOUNT) FROM VAS_ContractLine WHERE 
                            VAS_ContractMaster_ID = " + GetVAS_ContractMaster_ID() + "" +
                            ") WHERE VAS_ContractMaster_ID = " + GetVAS_ContractMaster_ID() + "",
                            null, Get_Trx());
            if (count < 0)
                success = false;

            return success;
        }
        /// <summary>
        /// VAI050-Restrict to delete if any transactions have occurred
        /// </summary>
        /// <returns>true if record deleted</returns>
        protected override bool BeforeDelete()
        {
            decimal amount = checkTransaction();
            if (amount > 0)
            {
                log.SaveError("", Msg.GetMsg(GetCtx(), "VAS_CheckOrder"));
                return false;
            }
            return true;
        }
        /// <summary>
        /// if any transactions have occurred through Blanket Purchase Order (BPO),
        /// Purchase Order (PO), Accounts Payable (AP), Blanket Sales Order (BSO), Sales Order
        /// (SO), or Accounts Receivable (AR).To check Line used on SO,P
        /// </summary>
        /// <returns>return line amount used</returns>
        public decimal checkTransaction()
        {
            string sql = "SELECT (a.t1+b.t2) AS LineAmount  FROM (SELECT NVL(SUM(ol.LineNetAmt),0) AS t1 FROM C_Order " +
                         " o INNER JOIN C_OrderLine oL  ON o.C_Order_ID = ol.C_Order_ID WHERE o.DocAction NOT IN ('VO','RC')  " +
                        "AND ol.VAS_ContractLine_ID = "+GetVAS_ContractLine_ID()+" ) a, (SELECT  NVL(SUM(il.LineNetAmt ),0) AS t2  FROM C_Invoice i INNER JOIN " +
                        "C_InvoiceLine il ON i.C_Invoice_ID = il.C_Invoice_ID WHERE i.DocAction NOT IN ('VO','RC') AND " +
                        "il.VAS_ContractLine_ID ="+GetVAS_ContractLine_ID()+") b";
            decimal LineAmount = Util.GetValueOfInt(DB.ExecuteScalar(sql, null, Get_Trx()));
            return LineAmount;
        }
    }
}
