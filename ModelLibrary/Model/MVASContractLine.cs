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
                StringBuilder sql = new StringBuilder();
                sql.Append("SELECT  VAS_ContractMaster_ID,(SELECT VAS_ContractUtilizedAmount FROM VAS_contractmaster WHERE " +
                            "VAS_Contractmaster_Id = " + GetVAS_ContractMaster_ID() + ") UtilizeAmount  FROM C_Order WHERE VAS_ContractMaster_ID = " + GetVAS_ContractMaster_ID() + " UNION " +
                        " SELECT VAS_ContractMaster_ID,(SELECT VAS_ContractUtilizedAmount FROM VAS_contractmaster WHERE " +
                            "VAS_Contractmaster_Id = " + GetVAS_ContractMaster_ID() + ") UtilizeAmount    FROM C_Invoice WHERE VAS_ContractMaster_ID = " + GetVAS_ContractMaster_ID());
                DataSet ds = DB.ExecuteDataset(sql.ToString(), null, Get_Trx());
                if (ds != null && ds.Tables.Count > 0 && ds.Tables[0].Rows.Count > 0)
                {
                    if (Is_ValueChanged("M_Product_ID") || Is_ValueChanged("M_AttributeSetInstance_ID")
                     || Is_ValueChanged("C_Charge_ID"))
                    {
                        log.SaveError("", Msg.GetMsg(GetCtx(), "VAS_CheckOrder"));
                        return false;
                    }                  
                     else 
                    {
                        int UtilizeAmount = Util.GetValueOfInt(ds.Tables[0].Rows[0]["UtilizeAmount"]);
                        sql.Clear();
                        sql.Append("SELECT NVL(SUM(Amount),0) TotalAmount FROM  VAS_ContractLine WHERE VAS_ContractMaster_Id=" 
                            + GetVAS_ContractMaster_ID() + " AND VAS_ContractLine_Id NOT IN(" + GetVAS_ContractLine_ID() + ")");
                        int TotalAmount = Util.GetValueOfInt(DB.ExecuteScalar(sql.ToString(), null, Get_Trx()));
                        TotalAmount = TotalAmount + Util.GetValueOfInt(GetAmount()); //All Contract Lines Amount Total
                        if (UtilizeAmount > TotalAmount) // Total Amount should be greater than utilize amount
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
    }
}
