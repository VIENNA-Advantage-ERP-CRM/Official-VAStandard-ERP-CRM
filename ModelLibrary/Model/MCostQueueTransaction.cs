using ModelLibrary.Classes;
using System;
using System.Collections.Generic;
using System.Data;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using VAdvantage.DataBase;
using VAdvantage.Logging;
using VAdvantage.Utility;

namespace VAdvantage.Model
{
    public class MCostQueueTransaction : X_M_CostQueueTransaction
    {
        private static VLogger _log = VLogger.GetVLogger(typeof(MCostQueueTransaction).FullName);

        public MCostQueueTransaction(Ctx ctx, int M_CostQueueTransaction_ID, Trx trxName)
            : base(ctx, M_CostQueueTransaction_ID, trxName)
        {

        }

        public MCostQueueTransaction(Ctx ctx, DataRow dr, Trx trxName)
            : base(ctx, dr, trxName)
        {

        }

        /// <summary>
        /// This function will create child record of Cost Queue, which will contain transaction affects 
        /// </summary>
        /// <param name="ctx">context</param>
        /// <param name="AD_Client_ID">Client ID</param>
        /// <param name="AD_Org_ID">Organization ID</param>
        /// <param name="M_CostQueue_ID">Cost Queue ID</param>
        /// <param name="cd">Cost Detail Reference</param>
        /// <param name="qty">qty</param>
        /// <returns>true, when success</returns>
        public static bool CreateCostQueueTransaction(Ctx ctx, int AD_Client_ID, int AD_Org_ID,
            int M_CostQueue_ID, MCostDetail cd, decimal qty)
        {
            return CreateCostQueueTransaction(ctx, AD_Client_ID, AD_Org_ID, M_CostQueue_ID, cd, qty, null);
        }

        /// <summary>
        /// This function will create child record of Cost Queue, which will contain transaction affects 
        /// </summary>
        /// <param name="ctx">context</param>
        /// <param name="AD_Client_ID">Client ID</param>
        /// <param name="AD_Org_ID">Organization ID</param>
        /// <param name="M_CostQueue_ID">Cost Queue ID</param>
        /// <param name="cd">Cost Detail Reference</param>
        /// <param name="qty">qty</param>
        /// <param name="CostingCheck">Costing Check</param>
        /// <returns>true, when success</returns>
        public static bool CreateCostQueueTransaction(Ctx ctx, int AD_Client_ID, int AD_Org_ID,
            int M_CostQueue_ID, MCostDetail cd, decimal qty, CostingCheck costingCheck)
        {
            try
            {
                // Direct insert instead of MCostQueueTransaction.Save() - performance
                CostingInsertBuilder ced = new CostingInsertBuilder(ctx, Table_Name, AD_Client_ID, AD_Org_ID, cd.Get_Trx());
                ced.AddInt("M_CostQueue_ID", M_CostQueue_ID);
                ced.AddID("M_Product_ID", cd.GetM_Product_ID());
                ced.AddID("M_AttributeSetInstance_ID", cd.GetM_AttributeSetInstance_ID());
                ced.AddID("M_Warehouse_ID", cd.GetM_Warehouse_ID());

                // date and qty
                ced.AddDecimal("MovementQty", qty);
                ced.AddDate("MovementDate", (costingCheck != null && costingCheck.movementDate != null) ? costingCheck.movementDate : DateTime.Now);

                //Refrences
                int M_InOutLine_ID = cd.GetM_InOutLine_ID();
                ced.AddID("M_InOutLine_ID", M_InOutLine_ID);
                if (M_InOutLine_ID > 0)
                {
                    if (costingCheck != null && costingCheck.inout != null)
                    {
                        ced.AddBool("IsSOTrx", costingCheck.inout.IsSOTrx());
                        ced.AddBool("IsReturnTrx", costingCheck.inout.IsReturnTrx());
                    }
                    else
                    {
                        DataSet ds = DB.ExecuteDataset(@"SELECT M_InOut.IsSOTrx, M_InOut.IsReturnTrx FROM M_InOutLine
                                    INNER JOIN M_InOut ON M_InOutLine.M_InOut_ID = M_InOut.M_InOut_ID
                                    WHERE M_InOutLine.M_InOutLine_ID = " + M_InOutLine_ID, null, cd.Get_Trx());
                        if (ds != null && ds.Tables.Count > 0 && ds.Tables[0].Rows.Count > 0)
                        {
                            ced.AddBool("IsSOTrx", Util.GetValueOfString(ds.Tables[0].Rows[0]["IsSOTrx"]).Equals("N") ? false : true);
                            ced.AddBool("IsReturnTrx", Util.GetValueOfString(ds.Tables[0].Rows[0]["IsReturnTrx"]).Equals("N") ? false : true);
                        }
                    }
                }
                if (cd.GetC_InvoiceLine_ID() > 0)
                {
                    ced.AddValueIfExists("C_InvoiceLine_ID", cd.GetC_InvoiceLine_ID());
                }
                int M_InventoryLine_ID = cd.GetM_InventoryLine_ID();
                ced.AddID("M_InventoryLine_ID", M_InventoryLine_ID);
                if (M_InventoryLine_ID > 0)
                {
                    if (costingCheck != null && costingCheck.inventory != null)
                    {
                        ced.AddBool("IsInternalUse", costingCheck.inventory.IsInternalUse());
                    }
                    else
                    {
                        bool isInternalUse = Util.GetValueOfString(DB.ExecuteScalar(@"SELECT M_Inventory.IsInternalUse  FROM M_InventoryLine
                                    INNER JOIN M_Inventory ON M_InventoryLine.M_Inventory_ID = M_Inventory.M_Inventory_ID
                                    WHERE M_InventoryLine.M_InventoryLine_ID = " + M_InventoryLine_ID, null, cd.Get_Trx())).Equals("N") ? false : true;
                        ced.AddBool("IsInternalUse", isInternalUse);
                    }
                }
                ced.AddID("M_MovementLine_ID", cd.GetM_MovementLine_ID());
                ced.AddID("C_ProjectIssue_ID", cd.GetC_ProjectIssue_ID());
                ced.AddID("M_ProductionLine_ID", cd.GetM_ProductionLine_ID());
                if (Env.IsModuleInstalled("VAFAM_"))
                {
                    ced.AddValueIfExists("VAFAM_AssetDisposal_ID", cd.Get_Value("VAFAM_AssetDisposal_ID"));
                }
                if (Env.IsModuleInstalled("VAMFG_"))
                {
                    ced.AddValueIfExists("VAMFG_M_WrkOdrRscTxnLine_ID", cd.GetVAMFG_M_WrkOdrRscTxnLine_ID());
                    ced.AddValueIfExists("VAMFG_M_WrkOdrTrnsctionLine_ID", cd.GetVAMFG_M_WrkOdrTrnsctionLine_ID());
                }
                if (Env.IsModuleInstalled("VA143_") && costingCheck != null && "VA143_JobWorkInOutLine".Equals(costingCheck.TableName))
                {
                    ced.AddValueIfExists(costingCheck.po.GetTableName() + "_ID", costingCheck.po.Get_ID());
                }
                string error;
                if (!ced.Execute(cd.Get_Trx(), out error))
                {
                    _log.Info("Costing Engine : Error Occured during saving a record on Cost Queue Transaction -> " + error);
                    costingCheck.errorMessage += "Cost Queue Transaction not created, " + error;
                    return false;
                }
            }
            catch (Exception ex)
            {
                _log.Info("Costing Engine : Exception Occured during saving a record on Cost Queue Transaction " + ex.Message);
                costingCheck.errorMessage += "Exception at Cost Queue Transaction, " + ex.Message;
                return false;
            }
            return true;
        }
    }
}
