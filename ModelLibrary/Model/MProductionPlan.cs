using System;
using System.Collections.Generic;
using System.Data;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using VAdvantage.DataBase;
using VAdvantage.Model;
using VAdvantage.Utility;

namespace VAdvantage.Model
{
    public class MProductionPlan : X_M_ProductionPlan
    {
        /// <summary>
        /// 	Std Constructor
        /// </summary>
        /// <param name="ctx"></param>
        /// <param name="M_ProductionLine_ID"></param>
        /// <param name="trxName"></param>
        public MProductionPlan(Ctx ctx, int M_ProductionPlan_ID, Trx trxName)
            : base(ctx, M_ProductionPlan_ID, trxName)
        {

        }

        /// <summary>
        /// Load Constructor
        /// </summary>
        /// <param name="ctx"></param>
        /// <param name="dr"></param>
        /// <param name="trxName"></param>
        public MProductionPlan(Ctx ctx, DataRow dr, Trx trxName)
            : base(ctx, dr, trxName)
        {

        }

        /// <summary>
        /// Implement Before save logic
        /// </summary>
        /// <param name="newRecord">Is New Record or not</param>
        /// <returns>true, when success</returns>
        protected override bool BeforeSave(bool newRecord)
        {
            if (Get_ColumnIndex("VAS_IsReverseAssembly") >= 0)
            {
                // check production plan record is reversal record or not
                string sql = @"SELECT COUNT(M_Production_ID) FROM M_Production WHERE Name like '{->%' AND DocumentNo LIKE '{->%' AND 
                           M_Production_ID = " + GetM_Production_ID();
                int count = Util.GetValueOfInt(DB.ExecuteScalar(sql, null, Get_Trx()));

                // when record is not reversal and Production Qty > 0
                if (count == 0)
                {
                    // On production plan, on selected BOM - is Reverse Assembly True or not. 
                    // When True, then make the production qty as Negative
                    sql = $@"SELECT VAS_IsReverseAssembly FROM M_BOM WHERE M_BOM_ID = {Get_ValueAsInt("M_BOM_ID")}";
                    if (Util.GetValueOfString(DB.ExecuteScalar(sql, null, Get_Trx())).Equals("Y"))
                    {
                        if (GetProductionQty() > 0)
                        {
                            SetProductionQty(decimal.Negate(GetProductionQty()));
                        }
                        Set_Value("VAS_IsReverseAssembly", true);
                    }
                    else
                    {
                        Set_Value("VAS_IsReverseAssembly", false);
                    }
                }
            }
            return true;
        }

        /// <summary>
        /// Implement After Save Logic
        /// </summary>
        /// <param name="newRecord">is New Record</param>
        /// <param name="success">Is Success</param>
        /// <returns>true, when success</returns>
        protected override bool AfterSave(bool newRecord, bool success)
        {
            if (!success)
            {
                return success;
            }

            return true;
        }

        /// <summary>
        /// After Delete
        /// </summary>
        /// <param name="success">success</param>
        /// <returns>deleted</returns>
        /// 
        protected override bool AfterDelete(bool success)
        {
            if (!success)
                return success;

            // VIS0060: Update IsCreated as False on Production header when all plans are deleted.
            if (Util.GetValueOfInt(DB.ExecuteScalar("SELECT COUNT(M_ProductionPlan_ID) FROM M_ProductionPlan WHERE M_Production_ID = "
                + GetM_Production_ID(), null, Get_Trx())) == 0)
            {
                DB.ExecuteQuery("UPDATE M_Production SET IsCreated = 'N' WHERE M_Production_ID = " + GetM_Production_ID(), null, Get_Trx());
            }
            return true;
        }
    }
}
