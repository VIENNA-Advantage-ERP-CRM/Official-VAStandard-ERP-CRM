/********************************************************
 * Project Name   : VAdvantage
 * Module Name    : ModelLibrary
 * Class Name     : CostingCheck
 * Purpose        : costing checks
 * Class Used     : none
 * Chronological  : Development
 * Amit           : 12-Jan-2022
  ******************************************************/

using System;
using System.Collections.Generic;
using System.Data;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using VAdvantage.DataBase;
using VAdvantage.Logging;
using VAdvantage.Model;
using VAdvantage.Utility;

namespace ModelLibrary.Classes
{
    public class CostingCheck
    {
        protected internal VLogger log = null;
        private Ctx _ctx = null;
        public int AD_Client_ID = 0, AD_Org_ID = 0, AD_OrgTo_ID = 0, M_ASI_ID = 0, M_Warehouse_ID = 0, M_WarehouseTo_ID = 0;
        public MProduct product = null;
        public MInventory inventory = null;
        public MInventoryLine inventoryLine = null;
        public MInOut inout = null;
        public MInOutLine inoutline = null;
        public MMovement movement = null;
        public MMovementLine movementline = null;
        public MInvoice invoice = null;
        public MInvoiceLine invoiceline = null;
        public MProvisionalInvoice provisionalInvoice = null;
        public MOrder order = null;
        public MOrderLine orderline = null;
        public PO po = null;
        public Decimal Price = 0, Qty = 0;
        public String costingMethod = String.Empty;
        public string materialCostingMethod = string.Empty;
        public int materialCostingElement = 0;
        public bool IsQunatityValidated = true;
        public int costingElement = 0, M_CostType_ID = 0;
        public int definedCostingElement = 0; /* Costing Element ID against selected Costing Method on Product Category or Accounting Schema*/
        public int Lifo_ID = 0, Fifo_ID = 0;
        public String costinglevel = String.Empty;
        public String MMPolicy = String.Empty;
        public bool? isReversal;
        public DataSet dsAccountingSchema = null;
        public String isMatchFromForm = "N";
        public DateTime? movementDate = null;
        private StringBuilder query = new StringBuilder();
        public int M_Transaction_ID = 0, M_TransactionTo_ID = 0;
        public string errorMessage = String.Empty;
        public decimal? onHandQty = null;
        public bool IsCostCalculationfromProcess = false;
        public decimal AdjustAmountAfterDiscount = 0;
        public decimal? currentQtyonQueue = null;
        public bool? IsPOCostingethodBindedonProduct = null;
        public DataSet dsCostElement = null;
        public bool IsCostImmediate = false;
        public int precision = 2;

        public decimal UnAllocatedLandedCost = 0;
        public decimal RemaningQtyonFreight = 0;

        public decimal ExpectedLandedCost = 0;
        public decimal OrderLineAmtinBaseCurrency = 0;
        public decimal DifferenceAmtPOandInvInBaseCurrency = 0;
        public bool VAS_IsDOCost = false;

        public bool isInvoiceLinkedwithGRN = false;

        /* required during Match PO -Receipt-invoice Form*/
        public string handlingWindowName = "";
        public MCostDetail costDetail = null;

        /// <summary>
        /// Constructor
        /// </summary>
        /// <param name="ctx">ctx</param>
        public CostingCheck(Ctx ctx)
        {
            _ctx = ctx;
            log = VLogger.GetVLogger(this.GetType().FullName);
        }

        /// <summary>
        /// Setter Property of Ctx
        /// </summary>
        /// <param name="ctx">ctx</param>
        public void SetCtx(Ctx ctx)
        {
            _ctx = ctx;
            return;
        }

        /// <summary>
        /// Setter Property of Product
        /// </summary>
        /// <param name="_product">Product</param>
        public void SetProduct(MProduct _product)
        {
            product = _product;
            return;
        }

        /// <summary>
        /// Setter Property of Inventory
        /// </summary>
        /// <param name="_inventory">inventory</param>
        public void SetInventory(MInventory _inventory)
        {
            inventory = _inventory;
            return;
        }

        /// <summary>
        /// Get LIFO and FIFO Costing Method ID
        /// </summary>
        /// <param name="AD_Client_ID">Client ID</param>
        public void GetLifoAndFIFoID(int AD_Client_ID)
        {
            query.Clear();
            query.Append(@"SELECT M_CostElement_ID, CostingMethod FROM M_CostElement WHERE IsActive = 'Y' AND 
                            CostingMethod IN ('" + MCostElement.COSTINGMETHOD_Fifo + "', '" + MCostElement.COSTINGMETHOD_Lifo + @"')
                            AND AD_Client_ID = " + AD_Client_ID);
            DataSet dsCostElement = DB.ExecuteDataset(query.ToString(), null, null);
            if (dsCostElement != null && dsCostElement.Tables.Count > 0 && dsCostElement.Tables[0].Rows.Count > 0)
            {
                for (int i = 0; i < dsCostElement.Tables[0].Rows.Count; i++)
                {
                    if (dsCostElement.Tables[0].Rows[i]["CostingMethod"].ToString().Equals(MCostElement.COSTINGMETHOD_Fifo))
                    {
                        Fifo_ID = Convert.ToInt32(dsCostElement.Tables[0].Rows[i]["M_CostElement_ID"]);
                    }
                    else
                    {
                        Lifo_ID = Convert.ToInt32(dsCostElement.Tables[0].Rows[i]["M_CostElement_ID"]);
                    }
                }
            }
        }

        /// <summary>
        /// Get Accounting Schemas
        /// </summary>
        /// <param name="AD_Client_ID">Client ID</param>
        /// <returns>Accounting Schemas</returns>
        public DataSet GetAccountingSchema(int AD_Client_ID)
        {
            // Get Cost Element 
            GetCostElement(AD_Client_ID);

            // Get Accounting Schema
            query.Clear();
            query.Append(@"Select C_Acctschema_Id From C_Acctschema
                                WHERE Isactive = 'Y' AND C_Acctschema_Id = (SELECT C_Acctschema1_Id FROM Ad_Clientinfo 
                                WHERE Ad_Client_Id = " + AD_Client_ID + @" )
                                Union
                                Select C_Acctschema_Id From C_Acctschema Where Isactive = 'Y' And Ad_Client_Id = " + AD_Client_ID + @"
                                AND C_Acctschema_Id != (SELECT C_Acctschema1_Id FROM Ad_Clientinfo WHERE Ad_Client_Id = " + AD_Client_ID + " )");
            return DB.ExecuteDataset(query.ToString(), null, null);
        }

        /// <summary>
        /// Get Product Cost Element 
        /// </summary>
        /// <param name="AD_Client_ID">Client ID</param>
        public void GetCostElement(int AD_Client_ID)
        {
            query.Clear();
            query.Append($@"SELECT DISTINCT M_CostElement_ID, CostingMethod, AD_Client_ID FROM M_CostElement WHERE IsActive = 'Y' AND AD_Client_ID = {AD_Client_ID}");
            dsCostElement = DB.ExecuteDataset(query.ToString(), null, null);
        }

        /// <summary>
        /// Reset Property
        /// </summary>
        public void ResetProperty()
        {
            AD_Client_ID = 0;
            AD_Org_ID = 0;
            AD_OrgTo_ID = 0;
            M_ASI_ID = 0;
            M_Warehouse_ID = 0;
            M_WarehouseTo_ID = 0;
            product = null;
            inventoryLine = null;
            inoutline = null;
            movementline = null;
            invoiceline = null;
            order = null;
            orderline = null;
            po = null;
            costingMethod = String.Empty;
            costingElement = 0;
            M_CostType_ID = 0;
            definedCostingElement = 0;
            Lifo_ID = 0;
            Fifo_ID = 0;
            costinglevel = String.Empty;
            MMPolicy = String.Empty;
            isMatchFromForm = "N";
            movementDate = null;
            query.Clear();
            M_Transaction_ID = 0; M_TransactionTo_ID = 0;
            errorMessage = String.Empty;
            onHandQty = null;
            IsPOCostingethodBindedonProduct = null;
            IsCostCalculationfromProcess = false;
            currentQtyonQueue = null;
            IsCostImmediate = false;

            /*31-Dec-2024*/
            Price = 0;
            Qty = 0;
            materialCostingMethod = string.Empty;
            materialCostingElement = 0;
            IsQunatityValidated = true;
            UnAllocatedLandedCost = 0;
            RemaningQtyonFreight = 0;

            ExpectedLandedCost = 0;
            OrderLineAmtinBaseCurrency = 0;
            DifferenceAmtPOandInvInBaseCurrency = 0;
            VAS_IsDOCost = false;

            isInvoiceLinkedwithGRN = false;

            handlingWindowName = "";
            costDetail = null;

        }

        /// <summary>
        /// Update Cost Error on transaction line
        /// </summary>
        /// <param name="Tablename">TableName</param>
        /// <param name="Record_id">Record ID</param>
        /// <param name="ErrorMessage">Error Message</param>
        /// <param name="trxname">Trx</param>
        /// <param name="IsCommit">Is Commit record after update or not</param>
        public void UpdateCostError(string Tablename, int Record_id, string ErrorMessage, Trx trxname, bool IsCommit)
        {
            DB.ExecuteQuery($@"UPDATE {Tablename} SET IsCostError = 'Y', CostErrorDetails = { GlobalVariable.TO_STRING(ErrorMessage)}
             WHERE {Tablename}_ID = " + Record_id, null, trxname);
            if (IsCommit)
            {
                trxname.Commit();
            }
        }

        /// <summary>
        /// This function is used to get the linked Costing Method Details on Cost Combination
        /// </summary>
        /// <param name="costElementId">Cost Combination Element ID</param>
        /// <param name="AD_Client_ID">Client ID</param>
        /// <author>VIS_0045</author>
        public void GetMaterialCostingMethodFroCombinaton(int costElementId, int AD_Client_ID)
        {
            query.Clear();
            query.Append($@"SELECT  cel.M_Ref_CostElement, refEle.costingmethod 
                             FROM M_CostElement ce 
                             INNER JOIN m_costelementline cel ON (ce.M_CostElement_ID = cel.M_CostElement_ID) 
                             INNER JOIN M_CostElement refEle ON (CAST(cel.M_Ref_CostElement AS INTEGER) = refEle.M_CostElement_ID AND refEle.costingmethod IS NOT NULL) 
                             WHERE ce.AD_Client_ID = " + AD_Client_ID + @"
                             AND ce.IsActive = 'Y' AND ce.CostElementType = 'C'
                             AND cel.IsActive = 'Y' AND ce.M_CostElement_ID = " + costElementId + @"
                             ORDER BY ce.M_CostElement_ID");
            DataSet dsMaterial = DB.ExecuteDataset(query.ToString(), null, null);
            if (dsMaterial != null && dsMaterial.Tables.Count > 0 && dsMaterial.Tables[0].Rows.Count > 0)
            {
                materialCostingMethod = Util.GetValueOfString(dsMaterial.Tables[0].Rows[0]["costingmethod"]);
                materialCostingElement = Util.GetValueOfInt(dsMaterial.Tables[0].Rows[0]["M_Ref_CostElement"]);
            }
        }

        /// <summary>
        /// This function is used to Insert the Data into M_CostClosing for maintaining the closing details
        /// </summary>
        /// <param name="trx">Transaction</param>
        /// <returns>Error Message (if any)</returns>
        public string InsertCostClosing(string ProductCategoryID, string Product_ID, Trx trx)
        {
            query.Clear();
            query.Append($@"DELETE FROM M_COSTClosing WHERE TRUNC(created) = TRUNC(current_Date)");
            DB.ExecuteQuery(query.ToString(), null, trx);

            query.Clear();
            query.Append($@"INSERT INTO M_CostClosing(
                M_CostClosing_ID, AD_CLIENT_ID, AD_ORG_ID, C_ACCTSCHEMA_ID, CREATED, CREATEDBY, CUMULATEDAMT, CUMULATEDQTY, CURRENTCOSTPRICE, CURRENTQTY,
                DESCRIPTION, FUTURECOSTPRICE, ISACTIVE, M_ATTRIBUTESETINSTANCE_ID, M_COSTELEMENT_ID, M_COSTTYPE_ID, M_PRODUCT_ID, PERCENTCOST, UPDATED,
                UPDATEDBY, BASISTYPE, ISTHISLEVEL, ISUSERDEFINED, LASTCOSTPRICE,A_ASSET_ID, ISASSETCOST, M_WAREHOUSE_ID)
            SELECT
                M_Cost_ID, AD_CLIENT_ID, AD_ORG_ID, C_ACCTSCHEMA_ID, Current_Date, {_ctx.GetAD_User_ID()}, CUMULATEDAMT, CUMULATEDQTY, CURRENTCOSTPRICE, CURRENTQTY, 
                DESCRIPTION, FUTURECOSTPRICE, ISACTIVE, M_ATTRIBUTESETINSTANCE_ID, M_COSTELEMENT_ID, M_COSTTYPE_ID, M_PRODUCT_ID, PERCENTCOST, Current_Date, 
                {_ctx.GetAD_User_ID()}, BASISTYPE, ISTHISLEVEL, ISUSERDEFINED, LASTCOSTPRICE, 
                CASE WHEN NVL(A_ASSET_ID, 0) = 0 THEN NULL ELSE A_ASSET_ID END AS A_ASSET_ID, ISASSETCOST, 
                 CASE WHEN NVL(M_WAREHOUSE_ID, 0) = 0 THEN NULL ELSE M_WAREHOUSE_ID END AS M_WAREHOUSE_ID           
            FROM M_Cost");
            query.Append($@" WHERE AD_Client_ID = {_ctx.GetAD_Client_ID()} ");
            if (!string.IsNullOrEmpty(Product_ID))
            {
                query.Append($@" AND M_Product_ID IN ({Product_ID}) ");
            }
            if (!string.IsNullOrEmpty(ProductCategoryID))
            {
                query.Append($@" AND M_Product_ID IN (SELECT M_Product_ID FROM M_Product WHERE M_Product_Category_ID IN ({ProductCategoryID}))");
            }
            int no = DB.ExecuteQuery(query.ToString(), null, trx);
            if (no <= 0)
            {
                return Msg.GetMsg(_ctx, "VAS_CostClosingNotInserted");
            }
            return "";
        }

    }
}
