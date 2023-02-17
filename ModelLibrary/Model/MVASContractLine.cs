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
    class MVASContractLine : X_VAS_ContractLine
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
