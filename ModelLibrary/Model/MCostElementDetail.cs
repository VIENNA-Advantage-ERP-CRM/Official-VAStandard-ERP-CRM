/********************************************************
 * Module Name    : 
 * Purpose        : Model for Cost Element Detail.
 * Class Used     : X_M_CostElementDetail
 * Chronological    Development
 * Amit Bansal      03-May-2016
**********************************************************/

using System;
using System.Collections.Generic;
using System.Text;
using System.Data;
using System.Data.SqlClient;
using VAdvantage.Classes;
using VAdvantage.Utility;
using VAdvantage.DataBase;
using VAdvantage.Common;
using VAdvantage.Logging;
using ModelLibrary.Classes;

namespace VAdvantage.Model
{
    public class MCostElementDetail : X_M_CostElementDetail
    {
        private static VLogger _log = VLogger.GetVLogger(typeof(MCostElementDetail).FullName);

        public MCostElementDetail(Ctx ctx, int M_CostElementDetail_ID, Trx trxName)
            : base(ctx, M_CostElementDetail_ID, trxName)
        {

        }

        public MCostElementDetail(Ctx ctx, DataRow dr, Trx trxName)
            : base(ctx, dr, trxName)
        {

        }

        protected override bool BeforeSave(bool newRecord)
        {
            return base.BeforeSave(newRecord);
        }

        protected override bool AfterSave(bool newRecord, bool success)
        {
            return base.AfterSave(newRecord, success);
        }

        /// <summary>
        /// This function is used to Create cost Element Detail
        /// </summary>
        /// <param name="ctx">context</param>
        /// <param name="AD_Client_ID">Client</param>
        /// <param name="AD_Org_ID">organization</param>
        /// <param name="Product">Product</param>
        /// <param name="M_ASI_ID">Attribute Set Instance</param>
        /// <param name="mas">Accounting Schema</param>
        /// <param name="M_costElement_ID">Cost Element</param>
        /// <param name="windowName">WindowName</param>
        /// <param name="cd">Cost Detail</param>
        /// <param name="amt">Amount</param>
        /// <param name="qty">Quantity</param>
        /// <returns>true, when success</returns>
        public static bool CreateCostElementDetail(Ctx ctx, int AD_Client_ID, int AD_Org_ID, MProduct Product, int M_ASI_ID,
                                                     MAcctSchema mas, int M_costElement_ID, string windowName, MCostDetail cd, decimal amt, decimal qty)
        {
            return CreateCostElementDetail(ctx, AD_Client_ID, AD_Org_ID, Product, M_ASI_ID,
                                                      mas, M_costElement_ID, windowName, cd, amt, qty, null);
        }

        public static bool CreateCostElementDetail(Ctx ctx, int AD_Client_ID, int AD_Org_ID, MProduct Product, int M_ASI_ID,
               MAcctSchema mas, int M_costElement_ID, string windowName, MCostDetail cd, decimal amt, decimal qty, CostingCheck costingCheck)
        {
            try
            {
                // Org / Warehouse
                int orgID = costingCheck != null ? costingCheck.AD_Org_ID : AD_Org_ID;
                int warehouseID = costingCheck != null ? costingCheck.M_Warehouse_ID : cd.GetM_Warehouse_ID();
                if (windowName.Equals("Inventory Move"))
                {
                    orgID = AD_Org_ID;
                    warehouseID = 0;
                    if ((!(bool)costingCheck.isReversal && qty > 0) || (bool)costingCheck.isReversal && qty < 0)
                    {
                        // when not reversed record and inc qty OR when reversed record and dec qty
                        orgID = costingCheck.AD_OrgTo_ID;
                        warehouseID = costingCheck.M_WarehouseTo_ID;
                    }
                    else if ((!(bool)costingCheck.isReversal && qty < 0) || (bool)costingCheck.isReversal && qty > 0)
                    {
                        // when not reversed record and dec qty OR when reversed record and inc qty
                        orgID = costingCheck.AD_Org_ID;
                        warehouseID = costingCheck.M_Warehouse_ID;
                    }
                }

                // Direct insert instead of MCostElementDetail.Save() - performance
                CostingInsertBuilder ced = new CostingInsertBuilder(ctx, Table_Name, AD_Client_ID, orgID, cd.Get_Trx());
                ced.AddID("M_Warehouse_ID", warehouseID);
                ced.AddInt("C_AcctSchema_ID", mas.GetC_AcctSchema_ID());
                ced.AddInt("M_CostElement_ID", M_costElement_ID);
                ced.AddInt("M_Product_ID", Product.GetM_Product_ID());
                ced.AddID("M_AttributeSetInstance_ID", costingCheck != null ? costingCheck.M_ASI_ID : M_ASI_ID);
                ced.AddDecimal("Qty", qty);
                ced.AddDecimal("Amt", amt);
                if (costingCheck != null && ced.HasColumn("MovementDate"))
                {
                    ced.AddDate("MovementDate", costingCheck.movementDate);
                }
                //Refrences
                ced.AddID("C_OrderLine_ID", cd.GetC_OrderLine_ID());
                ced.AddID("M_InOutLine_ID", cd.GetM_InOutLine_ID());
                if (windowName == "Material Receipt" || windowName == "Customer Return" || windowName == "Shipment" || windowName == "Return To Vendor")
                {
                    // not to bind Invoiceline refernece on cost element detail
                }
                else
                {
                    ced.AddID("C_InvoiceLine_ID", cd.GetC_InvoiceLine_ID());
                }
                ced.AddValueIfExists("VAFAM_AssetDisposal_ID", cd.Get_Value("VAFAM_AssetDisposal_ID"));
                ced.AddID("M_InventoryLine_ID", cd.GetM_InventoryLine_ID());
                ced.AddID("M_MovementLine_ID", cd.GetM_MovementLine_ID());
                ced.AddID("C_ProjectIssue_ID", cd.GetC_ProjectIssue_ID());
                ced.AddBool("IsSOTrx", cd.IsSOTrx());
                ced.AddID("A_Asset_ID", cd.GetA_Asset_ID());
                ced.AddID("M_ProductionLine_ID", cd.GetM_ProductionLine_ID());
                ced.AddID("M_WorkOrderResourceTxnLine_ID", cd.GetM_WorkOrderResourceTxnLine_ID());
                ced.AddID("M_WorkOrderTransactionLine_ID", cd.GetM_WorkOrderTransactionLine_ID());
                if (windowName.Equals("In") || windowName.Equals("Out"))
                {
                    ced.AddValueIfExists(costingCheck.po.GetTableName() + "_ID", costingCheck.po.Get_ID());
                }
                if (Env.IsModuleInstalled("VAMFG_"))
                {
                    ced.AddValueIfExists("VAMFG_M_WrkOdrRscTxnLine_ID", cd.GetVAMFG_M_WrkOdrRscTxnLine_ID());
                    ced.AddValueIfExists("VAMFG_M_WrkOdrTrnsctionLine_ID", cd.GetVAMFG_M_WrkOdrTrnsctionLine_ID());
                }
                ced.AddValueIfExists("C_ProvisionalInvoiceLine_ID", cd.Get_Value("C_ProvisionalInvoiceLine_ID"));
                string error;
                if (!ced.Execute(cd.Get_Trx(), out error))
                {
                    _log.Info("Error Occured during costing " + error);
                    return false;
                }
            }
            catch (Exception ex)
            {
                _log.Info("Error Occured during costing " + ex.ToString());
                return false;
            }
            return true;
        }
    }
}
