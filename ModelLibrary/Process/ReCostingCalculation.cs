/********************************************************
    * Project Name   : VAdvantage
    * Class Name     : ReCostingCalculation
    * Purpose        : Re Calculate Cost (Product category / Products wise)
    * Class Used     : ProcessEngine.SvrProcess
    * Chronological    Development
    * Amit Bansal     08-June-2018
******************************************************/


using ModelLibrary.Classes;
using System;
using System.Collections.Generic;
using System.Data;
using System.Data.SqlClient;
//using System.Data.OracleClient;

using System.Linq;
using System.Reflection;
using System.Text;
using VAdvantage.DataBase;
using VAdvantage.Logging;
using VAdvantage.Model;
using VAdvantage.ProcessEngine;
using VAdvantage.Utility;

namespace VAdvantage.Process
{
    public class ReCostingCalculation : SvrProcess
    {
        private StringBuilder sql = new StringBuilder();
        private StringBuilder sqlInvoice = new StringBuilder();
        private DataSet dsInvoice = null;
        private static VLogger _log = VLogger.GetVLogger(typeof(ReCostingCalculation).FullName);

        //Parameters
        string productCategoryID = null;
        string productID = null;
        String onlyDeleteCosting = "N";
        DateTime? DateFrom = null;
        int M_AttributeSetInstance_ID = 0;

        // for load assembly
        private static Assembly asm = null;
        private static Type type = null;
        private static MethodInfo methodInfo = null;

        DateTime? currentDate = DateTime.Now;
        DateTime? minDateRecord;

        DataSet dsRecord = null;
        DataSet dsChildRecord = null;

        Decimal quantity = 0;
        decimal currentCostPrice = 0;
        decimal amt = 0;
        // Order Line amt included (taxable amt + tax amt + surcharge amt)
        Decimal ProductOrderLineCost = 0;
        // Order Line Price actual
        Decimal ProductOrderPriceActual = 0;
        // Invoice Line amt included (taxable amt + tax amt + surcharge amt)
        Decimal ProductInvoiceLineCost = 0;

        MClient client = null;

        MInventory inventory = null;
        MInventoryLine inventoryLine = null;

        MMovement movement = null;
        MMovementLine movementLine = null;
        //MWarehouse warehouse = null;
        MLocator locatorTo = null; // is used to get "to warehouse" reference and "to org" reference for getting cost from prodyc costs 
        Decimal toCurrentCostPrice = 0; // is used to maintain cost of "move to" 

        MInOut inout = null;
        MInOutLine inoutLine = null;
        MOrderLine orderLine = null;
        MOrder order = null;

        MInvoice invoice = null;
        MInvoiceLine invoiceLine = null;
        bool isCostAdjustableOnLost = false;
        MLandedCostAllocation landedCostAllocation = null;

        MProvisionalInvoice provisionalInvoice = null;
        MProvisionalInvoiceLine provisionalInvoiceLine = null;

        MProduct product = null;

        MMatchInv matchInvoice = null;
        X_M_MatchInvCostTrack matchInvCostReverse = null;

        int table_WrkOdrTrnsctionLine = 0;
        MTable tbl_WrkOdrTrnsctionLine = null;
        int table_WrkOdrTransaction = 0;
        MTable tbl_WrkOdrTransaction = null;
        PO po_WrkOdrTransaction = null;
        PO po_WrkOdrTrnsctionLine = null;
        String woTrxType = null;
        int table_AssetDisposal = 0;
        MTable tbl_AssetDisposal = null;
        PO po_AssetDisposal = null;

        //Production
        int CountCostNotAvialable = 1;

        int countColumnExist = 0; //check IsCostAdjustmentOnLost exist on product 
        int countGOM01 = 0;  // check Gomel Modeule exist or not
        int count = 0; //check Manufacturing Modeule exist or not

        string conversionNotFoundInvoice = "";
        string conversionNotFoundInOut = "";
        string conversionNotFoundInventory = "";
        string conversionNotFoundMovement = "";
        string conversionNotFoundProductionExecution = "";
        string conversionNotFoundInvoice1 = "";
        string conversionNotFoundInOut1 = "";
        string conversionNotFoundInventory1 = "";
        string conversionNotFoundMovement1 = "";
        string conversionNotFoundProductionExecution1 = "";
        string conversionNotFound = "";
        string conversionNotFoundProvisionalInvoice = "";

        List<ReCalculateRecord> ListReCalculatedRecords = new List<ReCalculateRecord>();
        public enum windowName { Shipment = 0, CustomerReturn, ReturnVendor, MaterialReceipt, MatchInvoice, Inventory, Movement, CreditMemo, AssetDisposal }

        private String costingMethod = string.Empty;

        private CostingCheck costingCheck = null;
        private StringBuilder query = new StringBuilder();
        private StringBuilder queryTo = new StringBuilder();

        private bool IsCostUpdation = false;

        private decimal postingCost = 0;
        private DataSet CostOnOriginalDoc = null;

        private bool VAS_IsInvoiceRecostonGRNDate = false;

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
                    //int[] productCategoryArr = Array.ConvertAll(productCategoryID.Split(','), int.Parse);
                }
                else if (name.Equals("M_Product_ID"))
                {
                    productID = Util.GetValueOfString(para[i].GetParameter());
                }
                else if (name.Equals("M_AttributeSetInstance_ID"))
                {
                    M_AttributeSetInstance_ID = Util.GetValueOfInt(para[i].GetParameter());
                }
                else if (name.Equals("IsDeleteCosting"))
                {
                    onlyDeleteCosting = (string)para[i].GetParameter();
                }
                else if (name.Equals("DateFrom"))
                {
                    DateFrom = (DateTime?)para[i].GetParameter();
                }
                else
                {
                    log.Log(Level.SEVERE, "Unknown Parameter: " + name);
                }
            }
        }

        protected override string DoIt()
        {
            try
            {
                _log.Info("RE Cost Calculation Start on " + DateTime.Now);

                // check Manufacturing Modeule exist or not
                count = Env.IsModuleInstalled("VAMFG_") ? 1 : 0;

                // check VAFAM Modeule exist or not
                int countVAFAM = Env.IsModuleInstalled("VAFAM_") ? 1 : 0;

                // check Gomel Modeule exist or not
                countGOM01 = Env.IsModuleInstalled("GOM01_") ? 1 : 0;

                // Check Cost Closing Data found or not, if not then give message 
                if (DateFrom != null)
                {
                    string Closing_Cost = GetClosingInfo();
                    if (!string.IsNullOrEmpty(Closing_Cost))
                    {
                        return Closing_Cost;
                    }
                }

                // update / delete query
                UpdateAndDeleteCostImpacts(count);

                // when user not selected anything, then not to calculate cost, it will be calculated by Costing Calculation process
                if (String.IsNullOrEmpty(productCategoryID) && String.IsNullOrEmpty(productID) && onlyDeleteCosting.Equals("N"))
                    return Msg.GetMsg(GetCtx(), "VIS_NoImpacts");

                if (onlyDeleteCosting.Equals("Y"))
                    return Msg.GetMsg(GetCtx(), "VIS_DeleteCostingImpacts");

                // Insert Closing Cost 
                if (DateFrom != null)
                {
                    string Closing_Cost = InsertClosingCostonCost();
                    if (!string.IsNullOrEmpty(Closing_Cost))
                    {
                        return Closing_Cost;
                    }
                }

                // check IsCostAdjustmentOnLost exist on product 
                sql.Clear();
                sql.Append(@"SELECT COUNT(*) FROM AD_Column WHERE IsActive = 'Y' AND 
                                       AD_Table_ID =  ( SELECT AD_Table_ID FROM AD_Table WHERE IsActive = 'Y' AND TableName LIKE 'M_Product' ) 
                                       AND ColumnName = 'IsCostAdjustmentOnLost' ");
                countColumnExist = Util.GetValueOfInt(DB.ExecuteScalar(sql.ToString(), null, null));
                sql.Clear();

                // min date record from the transaction window
                minDateRecord = SerachMinDate(count, productID);

                if (count > 0)
                {
                    table_WrkOdrTransaction = Util.GetValueOfInt(DB.ExecuteScalar("SELECT AD_TABLE_ID  FROM AD_TABLE WHERE tablename = 'VAMFG_M_WrkOdrTransaction' AND IsActive = 'Y' "));
                    tbl_WrkOdrTransaction = new MTable(GetCtx(), table_WrkOdrTransaction, null);

                    table_WrkOdrTrnsctionLine = Util.GetValueOfInt(DB.ExecuteScalar("SELECT AD_TABLE_ID  FROM AD_TABLE WHERE tablename = 'VAMFG_M_WrkOdrTrnsctionLine' AND IsActive = 'Y' "));
                    tbl_WrkOdrTrnsctionLine = new MTable(GetCtx(), table_WrkOdrTrnsctionLine, null);
                }
                if (countVAFAM > 0)
                {
                    table_AssetDisposal = Util.GetValueOfInt(DB.ExecuteScalar("SELECT AD_TABLE_ID  FROM AD_TABLE WHERE tablename = 'VAFAM_AssetDisposal' AND IsActive = 'Y' "));
                    tbl_AssetDisposal = new MTable(GetCtx(), table_AssetDisposal, null);
                }

                int diff = (int)(Math.Ceiling((DateTime.Now.Date - minDateRecord.Value.Date).TotalDays));

                client = MClient.Get(GetCtx(), GetCtx().GetAD_Client_ID());
                VAS_IsInvoiceRecostonGRNDate = Util.GetValueOfBool(client.Get_Value("VAS_IsInvoiceRecostonGRNDate"));

                for (int days = 0; days <= diff; days++)
                {
                    if (days != 0)
                    {
                        minDateRecord = minDateRecord.Value.AddDays(1);
                    }

                    // When From Date less than min Date record then Cost to be updated 
                    if (DateFrom != null && minDateRecord != null && DateFrom.Value.Date <= minDateRecord.Value.Date)
                    {
                        IsCostUpdation = true;
                    }
                    else if (DateFrom == null)
                    {
                        // When From date not selected then Cost to be updated 
                        IsCostUpdation = true;
                    }

                    _log.Info("RE Cost Calculation Start for " + minDateRecord);
                    var pc = "(SELECT M_Product_ID FROM M_Product WHERE M_Product_Category_ID IN (" + productCategoryID + " ) )";
                    sql.Clear();
                    sql.Append(@"SELECT * FROM (
                        SELECT ad_client_id , ad_org_id , isactive , to_char(created, 'DD-MON-YY HH24:MI:SS') as created , createdby , to_char(updated, 'DD-MON-YY HH24:MI:SS') as updated , updatedby ,  
                               documentno , m_inout_id AS Record_Id , issotrx ,  isreturntrx , ''  AS IsInternalUse, 'M_InOut' AS TableName,
                               docstatus, movementdate AS DateAcct , iscostcalculated , isreversedcostcalculated 
                         FROM M_InOut WHERE dateacct   = " + GlobalVariable.TO_DATE(minDateRecord, true) + @" AND isactive = 'Y'
                               AND ((docstatus IN ('CO' , 'CL') AND iscostcalculated = 'N' ) OR (docstatus IN ('RE') AND iscostcalculated = 'Y'
                               AND ISREVERSEDCOSTCALCULATED= 'N' AND description LIKE '%{->%'))
                         UNION
                         SELECT ad_client_id , ad_org_id , isactive ,to_char(created, 'DD-MON-YY HH24:MI:SS') as created , createdby ,  to_char(updated, 'DD-MON-YY HH24:MI:SS') as updated , updatedby ,
                                documentno , C_Invoice_id AS Record_Id , issotrx , isreturntrx , '' AS IsInternalUse, 'C_Invoice' AS TableName,
                                docstatus, DateAcct AS DateAcct, iscostcalculated , isreversedcostcalculated 
                         FROM C_Invoice");
                    sql.Append(@" WHERE (dateacct = " + GlobalVariable.TO_DATE(minDateRecord, true));
                    sql.Append(@" ) AND isactive = 'Y'
                              AND ((docstatus IN ('CO' , 'CL') AND iscostcalculated = 'N' ) OR (docstatus  IN ('RE') AND iscostcalculated = 'Y'
                              AND ISREVERSEDCOSTCALCULATED= 'N' AND description LIKE '%{->%'))");
                    if (VAS_IsInvoiceRecostonGRNDate)
                    {
                        sql.Append(@" AND IsSOTrx = 'Y' ");
                    }

                    if (VAS_IsInvoiceRecostonGRNDate)
                    {
                        sql.Append(@" UNION
                         SELECT ad_client_id , ad_org_id , isactive ,to_char(created, 'DD-MON-YY HH24:MI:SS') as created , createdby ,  to_char(updated, 'DD-MON-YY HH24:MI:SS') as updated , updatedby ,
                                documentno , C_Invoice_id AS Record_Id , issotrx , isreturntrx , '' AS IsInternalUse, 'C_Invoice' AS TableName,
                                docstatus, DateAcct AS DateAcct, iscostcalculated, isreversedcostcalculated
                         FROM C_Invoice");
                        sql.Append(@" WHERE  IsSOTrx = 'N' AND (dateacct = " + GlobalVariable.TO_DATE(minDateRecord, true));

                        // Get invoice which are not linked with GRN / DO
                        sql.Append(@" AND ( 
                                C_Invoice_ID IN (SELECT il.C_Invoice_ID FROM C_InvoiceLine il 
                                    INNER JOIN C_Invoice i ON (il.C_Invoice_ID = i.C_Invoice_ID)
                                    WHERE NVL(il.M_InOutLine_ID, 0) = 0 AND i.dateacct = " + GlobalVariable.TO_DATE(minDateRecord, true) +
                                    @" AND il.IsActive = 'Y' AND il.iscostcalculated = 'N' AND il.IsCostImmediate = 'N' 
                                    AND il.M_Product_ID IN ( " + ((!String.IsNullOrEmpty(productCategoryID) && String.IsNullOrEmpty(productID)) ? pc : productID) + @" ) ) ");

                        // Treat as Discount record where on orginal invoice GRN reference not linked
                        sql.Append(@" OR 
                                C_Invoice_ID IN (SELECT il.C_Invoice_ID FROM C_InvoiceLine lc 
                                    INNER JOIN C_InvoiceLine il ON (lc.Ref_InvoiceLineOrg_ID = il.C_InvoiceLine_ID) 
                                    INNER JOIN C_Invoice ii ON (ii.C_Invoice_ID = lc.C_Invoice_ID)                                   
                                    WHERE NVL(il.M_InOutLine_ID, 0) = 0 AND ii.dateacct = " + GlobalVariable.TO_DATE(minDateRecord, true) +
                                    @" AND lc.IsActive = 'Y' AND lc.iscostcalculated = 'N' AND lc.IsCostImmediate = 'N' 
                                    AND lc.M_Product_ID IN ( " + ((!String.IsNullOrEmpty(productCategoryID) && String.IsNullOrEmpty(productID)) ? pc : productID) + @" ) ) ");
                        sql.Append(@" ) AND isactive = 'Y'
                              AND ((docstatus IN ('CO' , 'CL') AND iscostcalculated = 'N' ) OR (docstatus  IN ('RE') AND iscostcalculated = 'Y'
                              AND ISREVERSEDCOSTCALCULATED= 'N' AND description LIKE '%{->%')) )");
                    }

                    sql.Append(@" UNION 
                         SELECT i.ad_client_id ,  i.ad_org_id , i.isactive ,to_char(i.created, 'DD-MON-YY HH24:MI:SS') as  created ,  i.createdby ,  TO_CHAR(mi.updated, 'DD-MON-YY HH24:MI:SS') AS updated ,
                                i.updatedby ,  mi.documentno ,  M_MatchInv_Id AS Record_Id ,  i.issotrx ,  i.isreturntrx ,  ''           AS IsInternalUse,  'M_MatchInv' AS TableName,
                                i.docstatus,i.DateAcct AS DateAcct,  mi.iscostcalculated ,  i.isreversedcostcalculated
                         FROM M_MatchInv mi INNER JOIN c_invoiceline il ON il.c_invoiceline_id = mi.c_invoiceline_id INNER JOIN C_Invoice i ON i.c_invoice_id       = il.c_invoice_id
                              WHERE " + (M_AttributeSetInstance_ID > 0 ? $" mi.M_AttributeSetInstance_ID = {M_AttributeSetInstance_ID} AND " : "") +
                              @"mi.M_Product_ID IN ( " + ((!String.IsNullOrEmpty(productCategoryID) && String.IsNullOrEmpty(productID)) ? pc : productID) + @" )
                              AND (mi.dateacct = " + GlobalVariable.TO_DATE(minDateRecord, true));
                    sql.Append(@" ) AND i.isactive = 'Y' AND i.docstatus IN ('CO' , 'CL') AND mi.iscostcalculated = 'N' AND mi.iscostImmediate = 'N'");
                    if (VAS_IsInvoiceRecostonGRNDate)
                    {
                        sql.Append(@" AND i.IsSOTrx = 'Y' ");
                    }

                    sql.Append(@" UNION 
                         SELECT i.ad_client_id ,  i.ad_org_id , i.isactive ,to_char(mi.created, 'DD-MON-YY HH24:MI:SS') as  created ,  i.createdby ,  TO_CHAR(mi.updated, 'DD-MON-YY HH24:MI:SS') AS updated ,
                                i.updatedby ,  null AS documentno ,  M_MatchInvCostTrack_Id AS Record_Id ,  i.issotrx ,  i.isreturntrx ,  ''           AS IsInternalUse,  'M_MatchInvCostTrack' AS TableName,
                                i.docstatus, i.DateAcct AS DateAcct,  mi.iscostcalculated ,  mi.iscostimmediate AS isreversedcostcalculated
                          FROM M_MatchInvCostTrack mi 
                               INNER JOIN c_invoiceline il ON il.c_invoiceline_id = mi.c_invoiceline_id
                               INNER JOIN C_Invoice i ON i.c_invoice_id = il.c_invoice_id
                          WHERE  mi.M_Product_ID IN ( " + ((!String.IsNullOrEmpty(productCategoryID) && String.IsNullOrEmpty(productID)) ? pc : productID) + @" )
                           AND mi.updated >= " + GlobalVariable.TO_DATE(minDateRecord, true) + @" AND mi.updated < " + GlobalVariable.TO_DATE(minDateRecord.Value.AddDays(1), true) +
                          @"  AND i.isactive        = 'Y' AND (i.docstatus       IN ('RE' , 'VO') )

                        UNION
                         SELECT DISTINCT il.ad_client_id ,il.ad_org_id ,il.isactive ,to_char(LCA.created, 'DD-MON-YY HH24:MI:SS') as created ,   i.createdby , TO_CHAR(i.updated, 'DD-MON-YY HH24:MI:SS') AS updated ,
                              i.updatedby , I.DOCUMENTNO , LCA.C_LANDEDCOSTALLOCATION_ID AS RECORD_ID ,   '' AS ISSOTRX , '' AS ISRETURNTRX , '' AS ISINTERNALUSE,
                              'LandedCost' as TABLENAME,  I.DOCSTATUS, DATEACCT as DATEACCT , LCA.ISCOSTCALCULATED , 'N' as ISREVERSEDCOSTCALCULATED
                        FROM C_LANDEDCOSTALLOCATION LCA INNER JOIN C_INVOICELINE IL ON IL.C_INVOICELINE_ID = LCA.C_INVOICELINE_ID
                        INNER JOIN c_invoice i ON I.C_INVOICE_ID = IL.C_INVOICE_ID
                        WHERE " + (M_AttributeSetInstance_ID > 0 ? $" LCA.M_AttributeSetInstance_ID = {M_AttributeSetInstance_ID} AND " : "") +
                        @"i.dateacct = " + GlobalVariable.TO_DATE(minDateRecord, true) + @" AND il.isactive     = 'Y' AND 
                        LCA.M_PRODUCT_ID IN ( " + ((!String.IsNullOrEmpty(productCategoryID) && String.IsNullOrEmpty(productID)) ? pc : productID) + @" ) 
                        AND lca.iscostcalculated = 'N' AND I.DOCSTATUS IN ('CO' , 'CL', 'RE', 'VO') AND I.ISSOTRX = 'N' AND I.ISRETURNTRX = 'N' ");
                    if (VAS_IsInvoiceRecostonGRNDate)
                    {
                        sql.Append(@" AND NVL(LCA.M_InOutLine_ID  , 0) = 0");
                    }

                    sql.Append(@" UNION
                         SELECT ad_client_id , ad_org_id , isactive ,to_char(created, 'DD-MON-YY HH24:MI:SS') as created , createdby ,  to_char(updated, 'DD-MON-YY HH24:MI:SS') as updated , updatedby ,
                                documentno , m_Inventory_id AS Record_Id , '' AS issotrx , '' AS isreturntrx , IsInternalUse, 'M_Inventory' AS TableName,
                                docstatus, movementdate AS DateAcct ,  iscostcalculated ,  isreversedcostcalculated 
                         FROM m_Inventory WHERE movementdate = " + GlobalVariable.TO_DATE(minDateRecord, true) + @" AND isactive       = 'Y'
                              AND ((docstatus   IN ('CO' , 'CL') AND iscostcalculated = 'N' ) OR (docstatus IN ('RE') AND iscostcalculated = 'Y'
                              AND ISREVERSEDCOSTCALCULATED= 'N' AND (IsReversal = 'Y' OR description LIKE '%{->%'))) 
                         UNION
                         SELECT ad_client_id , ad_org_id , isactive ,to_char(created, 'DD-MON-YY HH24:MI:SS') as created , createdby ,  to_char(updated, 'DD-MON-YY HH24:MI:SS') as updated , updatedby , 
                                documentno ,  M_Movement_id AS Record_Id , '' AS issotrx , ''  AS isreturntrx , ''  AS IsInternalUse,  'M_Movement'  AS TableName,
                                docstatus,  movementdate AS DateAcct ,  iscostcalculated ,  isreversedcostcalculated 
                         FROM M_Movement WHERE movementdate = " + GlobalVariable.TO_DATE(minDateRecord, true) + @" AND isactive       = 'Y'
                               AND ((docstatus   IN ('CO' , 'CL') AND iscostcalculated = 'N' ) OR (docstatus IN ('RE') AND iscostcalculated        = 'Y'
                               AND ISREVERSEDCOSTCALCULATED= 'N' AND (IsReversal = 'Y' OR description LIKE '%{->%')))
                         UNION
                         SELECT ad_client_id , ad_org_id , isactive ,to_char(created, 'DD-MON-YY HH24:MI:SS') as created , createdby ,  to_char(updated, 'DD-MON-YY HH24:MI:SS') as updated , updatedby , 
                                name AS documentno ,  M_Production_ID AS Record_Id , IsReversed AS issotrx , ''  AS isreturntrx , ''  AS IsInternalUse,  'M_Production'  AS TableName,
                                '' AS docstatus, movementdate AS DateAcct ,  iscostcalculated ,  isreversedcostcalculated 
                         FROM M_Production WHERE movementdate = " + GlobalVariable.TO_DATE(minDateRecord, true) + @" AND isactive       = 'Y'
                               AND ((PROCESSED = 'Y' AND iscostcalculated = 'N' AND IsReversed = 'N' ) OR (PROCESSED = 'Y' AND iscostcalculated  = 'Y'
                               AND ISREVERSEDCOSTCALCULATED= 'N' AND IsReversed = 'Y' AND Name LIKE '%{->%'))");
                    sql.Append(@"UNION 
                         SELECT ad_client_id , ad_org_id , isactive , to_char(created, 'DD-MON-YY HH24:MI:SS') as created , createdby ,  to_char(updated, 'DD-MON-YY HH24:MI:SS') as updated , updatedby ,
                                documentno , C_ProvisionalInvoice_id AS Record_Id , issotrx , isreturntrx , '' AS IsInternalUse, 'C_ProvisionalInvoice' AS TableName,
                                docstatus, DateAcct AS DateAcct, iscostcalculated , isreversedcostcalculated 
                         FROM C_ProvisionalInvoice WHERE dateacct = " + GlobalVariable.TO_DATE(minDateRecord, true) + @" AND isactive     = 'Y'
                              AND ((docstatus IN ('CO' , 'CL') AND iscostcalculated = 'N' ) OR (docstatus  IN ('RE') AND iscostcalculated = 'Y'
                              AND ISREVERSEDCOSTCALCULATED= 'N' AND IsReversal ='Y')) ");
                    if (count > 0)
                    {
                        sql.Append(@" UNION
                         SELECT ad_client_id , ad_org_id , isactive ,to_char(created, 'DD-MON-YY HH24:MI:SS') as created , createdby ,  to_char(updated, 'DD-MON-YY HH24:MI:SS') as updated , updatedby , 
                                DOCUMENTNO ,  VAMFG_M_WrkOdrTransaction_id AS Record_Id ,  
                                 CASE WHEN  " + (String.IsNullOrEmpty(productID) ? "1 = 0" : "1 = 1") + " AND (SELECT COUNT(*) FROM m_product WHERE m_product_category_ID IN (" + (String.IsNullOrEmpty(productCategoryID) ? 0.ToString() : productCategoryID) + @") 
                                            AND m_product_id = VAMFG_M_WrkOdrTransaction.m_product_id) > 0 THEN 'Y'
                                      WHEN (SELECT COUNT(*) FROM m_product WHERE m_product_ID IN (" + (String.IsNullOrEmpty(productID) ? 0.ToString() : productID) + @") 
                                            AND m_product_id = VAMFG_M_WrkOdrTransaction.m_product_id) > 0 THEN 'Y' END AS issotrx ,
                                '' AS isreturntrx  , '' AS IsInternalUse,  'VAMFG_M_WrkOdrTransaction'  AS TableName,
                                docstatus, vamfg_dateacct AS DateAcct , iscostcalculated ,  isreversedcostcalculated 
                         FROM VAMFG_M_WrkOdrTransaction WHERE VAMFG_WorkOrderTxnType IN ('CI', 'CR' , 'AR' , 'AI', 'PM') AND vamfg_dateacct = " + GlobalVariable.TO_DATE(minDateRecord, true) + @" 
                              AND isactive  = 'Y' AND ((docstatus IN ('CO' , 'CL') AND iscostcalculated = 'N' ) OR (docstatus IN ('RE') AND iscostcalculated = 'Y'
                              AND ISREVERSEDCOSTCALCULATED  = 'N' AND ( REVERSALDOC_ID != 0 OR VAMFG_description LIKE '%{->%'))) ");
                    }
                    if (countVAFAM > 0)
                    {
                        sql.Append(@" UNION
                        SELECT ad_client_id , ad_org_id , isactive ,to_char(created, 'DD-MON-YY HH24:MI:SS') as created , createdby ,  to_char(updated, 'DD-MON-YY HH24:MI:SS') as updated , updatedby ,
                                documentno , VAFAM_AssetDisposal_ID AS Record_Id , '' AS issotrx , '' AS isreturntrx ,'' AS IsInternalUse ,'VAFAM_AssetDisposal' AS TableName,
                                docstatus , vafam_trxdate AS DateAcct ,  iscostcalculated ,  isreversedcostcalculated 
                         FROM VAFAM_AssetDisposal WHERE vafam_trxdate = " + GlobalVariable.TO_DATE(minDateRecord, true) + @" AND isactive = 'Y'
                              AND M_PRODUCT_ID IN ( " + ((!String.IsNullOrEmpty(productCategoryID) && String.IsNullOrEmpty(productID)) ? pc : productID) + @" ) 
                              AND ((docstatus   IN ('CO' , 'CL') AND iscostcalculated = 'N' ) OR (docstatus IN ('RE' , 'VO') AND iscostcalculated ='Y'
                              AND ISREVERSEDCOSTCALCULATED= 'N' AND ReversalDoc_ID != 0) )");
                    }
                    sql.Append(@" ) t WHERE AD_Client_ID = " + GetCtx().GetAD_Client_ID() + "  order by dateacct , to_date(created, 'DD-MON-YY HH24:MI:SS')");
                    dsRecord = DB.ExecuteDataset(sql.ToString(), null, Get_Trx());

                    // Complete Record
                    if (dsRecord != null && dsRecord.Tables.Count > 0 && dsRecord.Tables[0].Rows.Count > 0)
                    {
                        for (int z = 0; z < dsRecord.Tables[0].Rows.Count; z++)
                        {
                            // for checking - costing calculate on completion or not
                            // IsCostImmediate = true - calculate cost on completion else through process


                            #region Cost Calculation For Material Receipt --
                            try
                            {
                                if (Util.GetValueOfString(dsRecord.Tables[0].Rows[z]["TableName"]) == "M_InOut" &&
                                    Util.GetValueOfString(dsRecord.Tables[0].Rows[z]["issotrx"]) == "N" &&
                                    Util.GetValueOfString(dsRecord.Tables[0].Rows[z]["isreturntrx"]) == "N" &&
                                    (Util.GetValueOfString(dsRecord.Tables[0].Rows[z]["docstatus"]) == "CO" ||
                                     Util.GetValueOfString(dsRecord.Tables[0].Rows[z]["docstatus"]) == "CL"))
                                {

                                    CalculateCostForMaterial(Util.GetValueOfInt(dsRecord.Tables[0].Rows[z]["Record_Id"]));

                                    CalculateCostForPurchaseInvoice(Util.GetValueOfInt(dsRecord.Tables[0].Rows[z]["Record_Id"]));

                                    continue;
                                }
                            }
                            catch { }
                            #endregion

                            #region Cost Calculation for Provisional Invoice
                            if (Util.GetValueOfString(dsRecord.Tables[0].Rows[z]["TableName"]).Equals("C_ProvisionalInvoice"))
                            {
                                provisionalInvoice = new MProvisionalInvoice(GetCtx(), Util.GetValueOfInt(dsRecord.Tables[0].Rows[z]["Record_Id"]), Get_Trx());

                                sql.Clear();
                                sql.Append(@"SELECT * FROM C_ProvisionalInvoiceLine WHERE IsActive = 'Y' "
                                                + (M_AttributeSetInstance_ID > 0 ? $" M_AttributeSetInstance_ID = {M_AttributeSetInstance_ID} AND " : "") + @"
                                                AND " + (provisionalInvoice.IsReversal() ? " iscostcalculated = 'Y' AND IsReversedCostCalculated = 'N' "
                                                : " iscostcalculated = 'N' ") +
                                                " AND C_ProvisionalInvoice_ID = " + provisionalInvoice.GetC_ProvisionalInvoice_ID());
                                if (!String.IsNullOrEmpty(productCategoryID) && String.IsNullOrEmpty(productID))
                                {
                                    sql.Append(" AND M_Product_ID IN (SELECT M_Product_ID FROM M_Product WHERE M_Product_Category_ID IN (" + productCategoryID + " ) ) ");
                                }
                                else
                                {
                                    sql.Append(" AND M_Product_ID IN (" + productID + " )");
                                }
                                sql.Append(" ORDER BY Line ");
                                dsChildRecord = DB.ExecuteDataset(sql.ToString(), null, Get_Trx());
                                if (dsChildRecord != null && dsChildRecord.Tables.Count > 0 && dsChildRecord.Tables[0].Rows.Count > 0)
                                {
                                    for (int j = 0; j < dsChildRecord.Tables[0].Rows.Count; j++)
                                    {
                                        provisionalInvoiceLine = new MProvisionalInvoiceLine(GetCtx(), dsChildRecord.Tables[0].Rows[j], Get_Trx());
                                        if (client.IsCostImmediate() && provisionalInvoiceLine.GetM_Product_ID() > 0)
                                        {
                                            if (!provisionalInvoice.CostingCalculation(client, provisionalInvoiceLine, false))
                                            {
                                                if (!conversionNotFoundProvisionalInvoice.Contains(provisionalInvoice.GetDocumentNo()))
                                                {
                                                    conversionNotFoundProvisionalInvoice += provisionalInvoice.GetDocumentNo() + " , ";
                                                }
                                                _log.Info("Cost not Calculated for Provisional Invoice for this Line ID = "
                                                    + provisionalInvoiceLine.GetC_ProvisionalInvoiceLine_ID() +
                                                   " , " + (!string.IsNullOrEmpty(costingCheck.errorMessage) ? costingCheck.errorMessage : ""));
                                            }
                                            else
                                            {
                                                _log.Fine("Cost Calculation updated for C_ProvisionalLine = " + provisionalInvoiceLine.GetC_ProvisionalInvoiceLine_ID());
                                                Get_Trx().Commit();
                                            }
                                        }
                                    }

                                    // update Provisional Invoice Header
                                    //sql.Clear();
                                    //sql.Append(@"SELECT COUNT(C_ProvisionalInvoiceLine_ID) FROM C_ProvisionalInvoiceLine
                                    //    WHERE " + (provisionalInvoice.IsReversal() ? "IsReversedCostCalculated = 'N'" : "IsCostCalculated = 'N'") + @"
                                    //     AND IsActive = 'Y' AND C_ProvisionalInvoice_ID = " + provisionalInvoice.GetC_ProvisionalInvoice_ID());
                                    //if (Util.GetValueOfInt(DB.ExecuteScalar(sql.ToString(), null, Get_Trx())) <= 0)
                                    //{
                                    //    //Update  IsCostCalculated, IsReversedCostCalculated  as True on Header
                                    //    DB.ExecuteQuery(@"UPDATE C_ProvisionalInvoice SET IsCostCalculated = 'Y' "
                                    //      + (provisionalInvoice.IsReversal() ? ", IsReversedCostCalculated = 'Y'" : "") + @"
                                    //      WHERE C_ProvisionalInvoice_ID = " + provisionalInvoice.GetC_ProvisionalInvoice_ID(), null, Get_Trx());

                                    //    _log.Fine("Cost Calculation updated for C_provisionalInvoice_ID = " + provisionalInvoice.GetC_ProvisionalInvoice_ID());
                                    //    Get_Trx().Commit();
                                    //}
                                }
                                continue;
                            }
                            #endregion

                            #region Cost Calculation for SO / CRMA / VRMA
                            try
                            {
                                if (Util.GetValueOfString(dsRecord.Tables[0].Rows[z]["TableName"]) == "C_Invoice" &&
                                    (Util.GetValueOfString(dsRecord.Tables[0].Rows[z]["docstatus"]) == "CO" ||
                                     Util.GetValueOfString(dsRecord.Tables[0].Rows[z]["docstatus"]) == "CL"))
                                {
                                    CalculateCostForInvoice(Util.GetValueOfInt(dsRecord.Tables[0].Rows[z]["Record_Id"]));
                                    continue;
                                }
                            }
                            catch { }
                            #endregion

                            #region Cost Calculation for Landed cost Allocation
                            if (Util.GetValueOfString(dsRecord.Tables[0].Rows[z]["TableName"]) == "LandedCost")
                            {
                                CalculateLandedCost(Util.GetValueOfInt(dsRecord.Tables[0].Rows[z]["Record_Id"]));
                                continue;
                            }
                            #endregion

                            #region Cost Calculation for  PO Cycle --
                            try
                            {
                                if (Util.GetValueOfString(dsRecord.Tables[0].Rows[z]["TableName"]) == "M_MatchInv" &&
                                    (Util.GetValueOfString(dsRecord.Tables[0].Rows[z]["issotrx"]) == "N" &&
                                     Util.GetValueOfString(dsRecord.Tables[0].Rows[z]["isreturntrx"]) == "N"))
                                {

                                    CalculateCostForMatchInvoiced(Util.GetValueOfInt(dsRecord.Tables[0].Rows[z]["Record_Id"]));

                                    continue;
                                }
                            }
                            catch { }
                            #endregion

                            #region Cost Calculation for Physical Inventory --
                            try
                            {
                                if (Util.GetValueOfString(dsRecord.Tables[0].Rows[z]["TableName"]) == "M_Inventory" &&
                                    (Util.GetValueOfString(dsRecord.Tables[0].Rows[z]["docstatus"]) == "CO" ||
                                     Util.GetValueOfString(dsRecord.Tables[0].Rows[z]["docstatus"]) == "CL") &&
                                    Util.GetValueOfString(dsRecord.Tables[0].Rows[z]["IsInternalUse"]) == "N")
                                {

                                    CalculateCostForInventory(Util.GetValueOfInt(dsRecord.Tables[0].Rows[z]["Record_Id"]));

                                    continue;
                                }
                            }
                            catch { }
                            #endregion

                            #region Cost Calculation for  Internal use inventory --
                            try
                            {
                                if (Util.GetValueOfString(dsRecord.Tables[0].Rows[z]["TableName"]) == "M_Inventory" &&
                                    (Util.GetValueOfString(dsRecord.Tables[0].Rows[z]["docstatus"]) == "CO" ||
                                     Util.GetValueOfString(dsRecord.Tables[0].Rows[z]["docstatus"]) == "CL") &&
                                    Util.GetValueOfString(dsRecord.Tables[0].Rows[z]["IsInternalUse"]) == "Y")
                                {

                                    CalculateCostForInventory(Util.GetValueOfInt(dsRecord.Tables[0].Rows[z]["Record_Id"]));

                                    continue;
                                }
                            }
                            catch { }
                            #endregion

                            #region Cost Calculation for Asset Disposal
                            try
                            {
                                if (Util.GetValueOfString(dsRecord.Tables[0].Rows[z]["TableName"]).Equals("VAFAM_AssetDisposal"))
                                {
                                    po_AssetDisposal = tbl_AssetDisposal.GetPO(GetCtx(), Util.GetValueOfInt(dsRecord.Tables[0].Rows[z]["Record_Id"]), Get_Trx());
                                    CalculateCostForAssetDisposal(Util.GetValueOfInt(dsRecord.Tables[0].Rows[z]["Record_Id"]), Util.GetValueOfInt(Util.GetValueOfInt(po_AssetDisposal.Get_Value("M_Product_ID"))));

                                    continue;
                                }
                            }
                            catch { }
                            #endregion

                            #region Cost Calculation for Inventory Move --
                            try
                            {
                                if (Util.GetValueOfString(dsRecord.Tables[0].Rows[z]["TableName"]) == "M_Movement" &&
                                   (Util.GetValueOfString(dsRecord.Tables[0].Rows[z]["docstatus"]) == "CO" ||
                                    Util.GetValueOfString(dsRecord.Tables[0].Rows[z]["docstatus"]) == "CL"))
                                {

                                    CalculateCostForMovement(Util.GetValueOfInt(dsRecord.Tables[0].Rows[z]["Record_Id"]));

                                    continue;
                                }
                            }
                            catch { }
                            #endregion

                            #region Cost Calculation for Production
                            try
                            {
                                if (Util.GetValueOfString(dsRecord.Tables[0].Rows[z]["TableName"]) == "M_Production")
                                {
                                    sql.Clear();
                                    sql.Append($@"SELECT COUNT(pl.M_ProductionLine_ID) FROM M_ProductionLine pl 
                                                   INNER JOIN M_Product pr ON (pr.M_Product_ID = pl.M_Product_ID)
                                                    WHERE " + (M_AttributeSetInstance_ID > 0 ? $" pl.M_AttributeSetInstance_ID = {M_AttributeSetInstance_ID} AND " : "") +
                                                    $@"pl.M_Production_ID = {Util.GetValueOfInt(dsRecord.Tables[0].Rows[z]["Record_Id"])} ");
                                    if (!String.IsNullOrEmpty(productCategoryID) && String.IsNullOrEmpty(productID))
                                    {
                                        sql.Append(" AND pl.M_Product_ID IN (SELECT M_Product_ID FROM M_Product WHERE M_Product_Category_ID IN (" + productCategoryID + " ) ) ");
                                    }
                                    else
                                    {
                                        sql.Append(" AND pl.M_Product_ID IN (" + productID + " )");
                                    }
                                    if (Util.GetValueOfInt(DB.ExecuteScalar(sql.ToString(), null, Get_Trx())) > 0)
                                    {
                                        DataSet dsCostingMethod = null;
                                        int v_definedcostelement_id = 0;
                                        string v_costinglevel = string.Empty;
                                        decimal v_productapproxcost = 0;
                                        decimal v_productcost = 0;
                                        decimal LineAmt = 0;

                                        // get record from production line based on production id
                                        sql.Clear();
                                        sql.Append(@"SELECT pl.M_ProductionLine_ID, pl.AD_Client_ID, pl.AD_Org_ID, p.MovementDate,  pl.M_Product_ID, 
                                                    t.M_AttributeSetInstance_ID, t.MovementQty, pl.M_Locator_ID, wh.IsDisallowNegativeInv,  pl.M_Warehouse_ID ,
                                                    p.IsCostCalculated, p.IsReversedCostCalculated,  p.IsReversed, t.M_Transaction_ID, p.M_Production_ID, pl.Amt, pl.M_ProductionPlan_id  
                                                FROM M_Production p 
                                                     INNER JOIN M_ProductionPlan pp ON (pp.M_Production_id = pp.M_Production_id)
                                                     INNER JOIN M_ProductionLine pl ON (pl.M_ProductionPlan_id = pp.M_ProductionPlan_id)
                                                     INNER JOIN M_Product prod ON (pl.M_Product_id = prod.M_Product_id)
                                                     INNER JOIN M_Locator loc ON (loc.M_Locator_id = pl.M_Locator_id)
                                                     INNER JOIN M_Warehouse wh ON (loc.M_Warehouse_id = wh.M_Warehouse_id)
                                                     INNER JOIN M_Transaction t ON (t.M_ProductionLine_ID = pl.M_ProductionLine_ID) 
                                                WHERE " + (M_AttributeSetInstance_ID > 0 ? $" pl.M_AttributeSetInstance_ID = {M_AttributeSetInstance_ID} AND " : "") +
                                                      @"p.M_Production_ID = pp.M_Production_ID AND pp.M_ProductionPlan_ID=pl.M_ProductionPlan_ID AND pl.IsCostImmediate = 'N' 
                                                      AND pp.M_Production_ID    =" + Util.GetValueOfInt(dsRecord.Tables[0].Rows[z]["Record_Id"]) + @"
                                                      AND pl.M_Product_ID = prod.M_Product_ID AND prod.ProductType ='I' 
                                                      AND pl.M_Locator_ID = loc.M_Locator_ID AND loc.M_Warehouse_ID = wh.M_Warehouse_ID");
                                        if (!String.IsNullOrEmpty(productCategoryID) && String.IsNullOrEmpty(productID))
                                        {
                                            sql.Append(" AND pl.M_Product_ID IN (SELECT M_Product_ID FROM M_Product WHERE M_Product_Category_ID IN (" + productCategoryID + " ) ) ");
                                        }
                                        else
                                        {
                                            sql.Append(" AND pl.M_Product_ID IN (" + productID + " )");
                                        }
                                        sql.Append(" ORDER BY  pp.Line,  pl.Line");
                                        dsChildRecord = DB.ExecuteDataset(sql.ToString(), null, Get_Trx());
                                        if (dsChildRecord != null && dsChildRecord.Tables.Count > 0 && dsChildRecord.Tables[0].Rows.Count > 0)
                                        {
                                            for (int j = 0; j < dsChildRecord.Tables[0].Rows.Count; j++)
                                            {
                                                #region calculate/update cost of components (Here IsSotrx means IsReversed --> on production header)
                                                if (Util.GetValueOfString(dsRecord.Tables[0].Rows[z]["issotrx"]).Equals("N") && IsCostUpdation)
                                                {
                                                    SqlParameter[] param = new SqlParameter[2];
                                                    param[0] = new SqlParameter("p_record_id", Util.GetValueOfInt(dsRecord.Tables[0].Rows[z]["Record_Id"]));
                                                    param[0].SqlDbType = SqlDbType.Int;
                                                    param[0].Direction = ParameterDirection.Input;

                                                    param[1] = new SqlParameter("p_m_product_id", Util.GetValueOfInt(dsChildRecord.Tables[0].Rows[j]["M_Product_ID"]));
                                                    param[1].SqlDbType = SqlDbType.Int;
                                                    param[1].Direction = ParameterDirection.Input;

                                                    DB.ExecuteProcedure("updateproductionlinewithcostreversal", param, Get_Trx());
                                                }
                                                #endregion

                                                // count -> is there any record having cost not available on production line except finished good
                                                // if not found, then we will calculate cost of finished good else not.
                                                CountCostNotAvialable = 1;
                                                CountCostNotAvialable = Util.GetValueOfInt(DB.ExecuteScalar($@" SELECT
                                                        COUNT(pl.m_productionline_id)
                                                    FROM
                                                        M_Production p
                                                        INNER JOIN M_ProductionPlan pp ON ( p.M_Production_ID = pp.M_Production_ID )
                                                        INNER JOIN M_ProductionLine pl ON ( pl.M_ProductionPlan_ID = pp.M_ProductionPlan_ID )
                                                        INNER JOIN M_Product        pr ON ( pr.M_Product_ID = pl.M_Product_ID )
                                                    WHERE nvl(pl.Amt, 0) = 0
                                                        AND pl.IsActive = 'Y' AND pp.IsActive = 'Y' AND pl.MovementQty < 0 AND pr.IsFocItem = 'N'
                                                        AND p.M_Production_ID = " + Util.GetValueOfInt(dsRecord.Tables[0].Rows[z]["Record_Id"]) +
                                                        " AND pl.M_ProductionPlan_ID = " + Util.GetValueOfInt(dsChildRecord.Tables[0].Rows[j]["M_ProductionPlan_id"]), null, Get_Trx()));

                                                if ((CountCostNotAvialable == 0 && Util.GetValueOfDecimal(dsChildRecord.Tables[0].Rows[j]["MovementQty"]) > 0) ||
                                                    Util.GetValueOfDecimal(dsChildRecord.Tables[0].Rows[j]["MovementQty"]) < 0)
                                                {

                                                    #region Get Costing Method, Level and Approx Cost
                                                    sql.Clear();
                                                    sql.Append($@" SELECT
	                                                            CASE
		                                                            WHEN M_Product_Category.costingmethod is not null
		                                                            AND M_Product_Category.costingmethod = 'C' THEN NVL(M_Product_Category.m_costelement_id,0)
		                                                            WHEN M_Product_Category.costingmethod is not null THEN (
		                                                                SELECT m_costelement_id FROM m_costelement
		                                                                WHERE costingmethod = M_Product_Category.costingmethod
			                                                                AND ad_client_id = {Util.GetValueOfInt(dsChildRecord.Tables[0].Rows[j]["AD_Client_ID"])} )
		                                                                WHEN C_AcctSchema.costingmethod is not null
		                                                                AND C_AcctSchema.costingmethod = 'C' THEN NVL(C_AcctSchema.m_costelement_id,0)
		                                                            ELSE (
		                                                                SELECT m_costelement_id FROM m_costelement
		                                                                WHERE costingmethod = C_AcctSchema.costingmethod 
			                                                                  AND ad_client_id ={Util.GetValueOfInt(dsChildRecord.Tables[0].Rows[j]["AD_Client_ID"])})
	                                                                END AS m_costelement_id,
	                                                            CASE
		                                                            WHEN M_Product_Category.costinglevel is not null THEN M_Product_Category.costinglevel
		                                                            WHEN C_AcctSchema.costinglevel is not null THEN C_AcctSchema.costinglevel
	                                                            END AS costinglevel
                                                            FROM
	                                                            M_Product
                                                            INNER JOIN M_Product_Category on ( M_Product_Category.m_product_category_id = M_Product.m_product_category_id )
                                                            INNER JOIN C_AcctSchema ON ( C_AcctSchema.c_acctschema_id = (
	                                                            SELECT c_acctschema1_id FROM
		                                                            AD_ClientInfo WHERE ad_client_id = {Util.GetValueOfInt(dsChildRecord.Tables[0].Rows[j]["AD_Client_ID"])} ) )
                                                            WHERE
	                                                            M_Product.M_Product_ID = {Util.GetValueOfInt(dsChildRecord.Tables[0].Rows[j]["M_Product_ID"])} ");
                                                    dsCostingMethod = DB.ExecuteDataset(sql.ToString());
                                                    if (dsCostingMethod != null && dsCostingMethod.Tables.Count > 0 && dsCostingMethod.Tables[0].Rows.Count > 0)
                                                    {
                                                        // Assigned Costing Element with Product
                                                        v_definedcostelement_id = Util.GetValueOfInt(dsCostingMethod.Tables[0].Rows[0]["m_costelement_id"]);

                                                        // Assigned Costing Level with Product
                                                        v_costinglevel = Util.GetValueOfString(dsCostingMethod.Tables[0].Rows[0]["costinglevel"]);

                                                        // Get Product Cost Before Transaction
                                                        v_productapproxcost = MCost.GetproductCosts(
                                                            Util.GetValueOfInt(dsChildRecord.Tables[0].Rows[j]["AD_Client_ID"]),
                                                            Util.GetValueOfInt(dsChildRecord.Tables[0].Rows[j]["AD_Org_ID"]),
                                                            Util.GetValueOfInt(dsChildRecord.Tables[0].Rows[j]["M_Product_ID"]),
                                                            Util.GetValueOfInt(dsChildRecord.Tables[0].Rows[j]["M_AttributeSetInstance_ID"]),
                                                            Get_Trx(),
                                                            Util.GetValueOfInt(dsChildRecord.Tables[0].Rows[j]["M_Warehouse_ID"]));
                                                    }
                                                    #endregion

                                                    #region Create & Open connection and Execute Procedure
                                                    try
                                                    {
                                                        SqlParameter[] param = new SqlParameter[9];
                                                        param[0] = new SqlParameter("p_m_product_id", Util.GetValueOfInt(dsChildRecord.Tables[0].Rows[j]["M_Product_ID"]));
                                                        param[0].SqlDbType = SqlDbType.Int;
                                                        param[0].Direction = ParameterDirection.Input;

                                                        param[1] = new SqlParameter("p_m_attributesetinstance_id", Util.GetValueOfInt(dsChildRecord.Tables[0].Rows[j]["M_AttributeSetInstance_ID"]));
                                                        param[1].SqlDbType = SqlDbType.Int;
                                                        param[1].Direction = ParameterDirection.Input;

                                                        param[2] = new SqlParameter("p_ad_org_id", Util.GetValueOfInt(dsChildRecord.Tables[0].Rows[j]["AD_Org_ID"]));
                                                        param[2].SqlDbType = SqlDbType.Int;
                                                        param[2].Direction = ParameterDirection.Input;

                                                        param[3] = new SqlParameter("p_ad_client_id", Util.GetValueOfInt(dsChildRecord.Tables[0].Rows[j]["AD_Client_ID"]));
                                                        param[3].SqlDbType = SqlDbType.Int;
                                                        param[3].Direction = ParameterDirection.Input;

                                                        param[4] = new SqlParameter("p_quantity", Util.GetValueOfDecimal(dsChildRecord.Tables[0].Rows[j]["MovementQty"]));
                                                        param[4].SqlDbType = SqlDbType.Decimal;
                                                        param[4].Direction = ParameterDirection.Input;

                                                        param[5] = new SqlParameter("p_m_productionline_id", Util.GetValueOfInt(dsChildRecord.Tables[0].Rows[j]["M_ProductionLine_ID"]));
                                                        param[5].SqlDbType = SqlDbType.Int;
                                                        param[5].Direction = ParameterDirection.Input;

                                                        param[6] = new SqlParameter("p_m_warehouse_id", Util.GetValueOfInt(dsChildRecord.Tables[0].Rows[j]["M_Warehouse_ID"]));
                                                        param[6].SqlDbType = SqlDbType.Int;
                                                        param[6].Direction = ParameterDirection.Input;

                                                        param[7] = new SqlParameter("p_movementdate", Util.GetValueOfDateTime(dsChildRecord.Tables[0].Rows[j]["MovementDate"]));
                                                        param[7].SqlDbType = SqlDbType.Date;
                                                        param[7].Direction = ParameterDirection.Input;

                                                        param[8] = new SqlParameter("p_ismanual", "W"); // W - Manual, P - Process
                                                        param[8].SqlDbType = SqlDbType.Char;
                                                        param[8].Direction = ParameterDirection.Input;

                                                        DB.ExecuteProcedure("createcostqueueNotFRPT", param, Get_Trx());

                                                        // get Product Cost after Assembly
                                                        v_productcost = MCost.GetproductCosts(
                                                           Util.GetValueOfInt(dsChildRecord.Tables[0].Rows[j]["AD_Client_ID"]),
                                                           Util.GetValueOfInt(dsChildRecord.Tables[0].Rows[j]["AD_Org_ID"]),
                                                           Util.GetValueOfInt(dsChildRecord.Tables[0].Rows[j]["M_Product_ID"]),
                                                           Util.GetValueOfInt(dsChildRecord.Tables[0].Rows[j]["M_AttributeSetInstance_ID"]),
                                                           Get_Trx(),
                                                           Util.GetValueOfInt(dsChildRecord.Tables[0].Rows[j]["M_Warehouse_ID"]));


                                                        if (IsCostUpdation)
                                                        {

                                                            LineAmt = Util.GetValueOfDecimal(DB.ExecuteScalar($@"SELECT Amt FROM M_ProductionLine WHERE M_ProductionLine_ID = 
                                                                {Util.GetValueOfInt(dsChildRecord.Tables[0].Rows[j]["M_ProductionLine_ID"])}", null, Get_Trx()));
                                                            // Update Costing detial on Transaction
                                                            DB.ExecuteQuery($@"UPDATE M_Transaction
                                                                            SET
	                                                                            costinglevel = {GlobalVariable.TO_STRING(v_costinglevel)},
	                                                                            m_costelement_id = {v_definedcostelement_id},
	                                                                            productapproxcost = {v_productapproxcost},
	                                                                            productcost = {v_productcost}, 
                                                                                VAS_PostingCost = {LineAmt},
                                                                                UpdatedBy = {GetCtx().GetAD_User_ID()}, 
                                                                                Updated = {GlobalVariable.TO_DATE(DateTime.Now, false)}
                                                                            WHERE
	                                                                            m_transaction_id = {Util.GetValueOfInt(dsChildRecord.Tables[0].Rows[j]["M_Transaction_ID"])}", null, Get_Trx());
                                                        }

                                                        // Update Differnce Value on product Plan only
                                                        // Component Cost - Finish Good Qty
                                                        sql.Clear();
                                                        sql.Append($@" SELECT
                                                                    pp.m_productionplan_id,
                                                                    pp.m_production_id,
                                                                    pp.productionqty,
                                                                    pp.vas_isreverseassembly
                                                                FROM
                                                                    m_productionplan pp
                                                                    INNER JOIN m_product prod ON (prod.m_product_id = pp.m_product_id)
                                                                WHERE
                                                                    pp.m_production_id = {Util.GetValueOfInt(dsChildRecord.Tables[0].Rows[j]["M_Production_ID"])}
                                                                    AND prod.isbom = 'Y'
                                                                    AND prod.isverified = 'Y' 
                                                                ORDER BY
                                                                    pp.line,
                                                                    pp.m_product_id");
                                                        dsCostingMethod = DB.ExecuteDataset(sql.ToString(), null, Get_Trx());
                                                        if (dsCostingMethod != null && dsCostingMethod.Tables.Count > 0 && dsCostingMethod.Tables[0].Rows.Count > 0)
                                                        {
                                                            for (int k = 0; k < dsCostingMethod.Tables[0].Rows.Count; k++)
                                                            {
                                                                if (Util.GetValueOfString(dsCostingMethod.Tables[0].Rows[k]["vas_isreverseassembly"]).Equals("N") &&
                                                                   Util.GetValueOfDecimal(dsCostingMethod.Tables[0].Rows[k]["productionqty"]) < 0)
                                                                {
                                                                    sql.Clear();
                                                                    sql.Append($@" WITH production_costs AS (
                                                                    SELECT
                                                                        NVL(ABS(SUM(CASE WHEN MaterialType = 'C' AND NVL(M_Product_ID, 0) != 0 THEN MovementQty * Amt END)), 0) AS v_component_cost,
                                                                        NVL(ABS(SUM(CASE WHEN MaterialType = 'F' AND NVL(M_Product_ID, 0) != 0 THEN MovementQty * Amt END)), 0) AS v_finishgood_cost
                                                                    FROM
                                                                        m_productionline
                                                                    WHERE
                                                                        m_production_id = { Util.GetValueOfInt(dsCostingMethod.Tables[0].Rows[k]["m_production_id"]) }
                                                                        AND m_productionplan_id = { Util.GetValueOfInt(dsCostingMethod.Tables[0].Rows[k]["m_productionplan_id"]) }
                                                                )
                                                                UPDATE
                                                                    m_productionplan
                                                                SET
                                                                    VAS_DifferenceValue = (production_costs.v_component_cost - production_costs.v_finishgood_cost), 
                                                                                UpdatedBy = {GetCtx().GetAD_User_ID()}, 
                                                                                Updated = {GlobalVariable.TO_DATE(DateTime.Now, false)}
                                                                FROM
                                                                    production_costs
                                                                WHERE
                                                                    m_productionplan_id = { Util.GetValueOfInt(dsCostingMethod.Tables[0].Rows[k]["m_productionplan_id"]) } ");
                                                                }
                                                            }
                                                        }

                                                        // update prodution header 
                                                        //if (Util.GetValueOfString(dsChildRecord.Tables[0].Rows[j]["IsCostCalculated"]).Equals("N"))
                                                        //{
                                                        //    DB.ExecuteQuery("UPDATE M_Production SET IsCostCalculated='Y' WHERE M_Production_ID= " + Util.GetValueOfInt(dsRecord.Tables[0].Rows[z]["Record_Id"]), null, Get_Trx());
                                                        //}

                                                        //if (Util.GetValueOfString(dsChildRecord.Tables[0].Rows[j]["IsCostCalculated"]).Equals("Y") &&
                                                        //    !Util.GetValueOfString(dsChildRecord.Tables[0].Rows[j]["IsReversedCostCalculated"]).Equals("N") &&
                                                        //    Util.GetValueOfString(dsChildRecord.Tables[0].Rows[j]["IsReversed"]).Equals("Y"))
                                                        //{
                                                        //    DB.ExecuteQuery("UPDATE M_Production SET IsReversedCostCalculated='Y' WHERE M_Production_ID= " + Util.GetValueOfInt(dsRecord.Tables[0].Rows[z]["Record_Id"]), null, Get_Trx());
                                                        //}
                                                    }
                                                    catch (Exception ex)
                                                    {
                                                        _log.Severe("ReCostingCalculationTransaction: Production -> " + ex.Message);
                                                        Get_Trx().Rollback();
                                                    }
                                                    #endregion
                                                }
                                            }
                                        }
                                    }
                                    Get_Trx().Commit();
                                    continue;
                                }
                            }
                            catch
                            {
                                Get_Trx().Rollback();
                            }
                            #endregion

                            #region Cost Calculation For  Return to Vendor --
                            try
                            {
                                if (Util.GetValueOfString(dsRecord.Tables[0].Rows[z]["TableName"]) == "M_InOut" &&
                                    Util.GetValueOfString(dsRecord.Tables[0].Rows[z]["issotrx"]) == "N" &&
                                    Util.GetValueOfString(dsRecord.Tables[0].Rows[z]["isreturntrx"]) == "Y" &&
                                    (Util.GetValueOfString(dsRecord.Tables[0].Rows[z]["docstatus"]) == "CO" ||
                                     Util.GetValueOfString(dsRecord.Tables[0].Rows[z]["docstatus"]) == "CL"))
                                {

                                    CalculateCostForReturnToVendor(Util.GetValueOfInt(dsRecord.Tables[0].Rows[z]["Record_Id"]));

                                    CalculateCostForPurchaseInvoice(Util.GetValueOfInt(dsRecord.Tables[0].Rows[z]["Record_Id"]));

                                    continue;
                                }
                            }
                            catch { }
                            #endregion

                            #region Cost Calculation Against AP Credit Memo - During Return Cycle of Purchase --
                            try
                            {
                                if (Util.GetValueOfString(dsRecord.Tables[0].Rows[z]["TableName"]) == "M_MatchInv" &&
                                    (Util.GetValueOfString(dsRecord.Tables[0].Rows[z]["issotrx"]) == "N" &&
                                     Util.GetValueOfString(dsRecord.Tables[0].Rows[z]["isreturntrx"]) == "Y"))
                                {

                                    CalculationCostCreditMemo(Util.GetValueOfInt(dsRecord.Tables[0].Rows[z]["Record_Id"]));

                                    continue;
                                }
                            }
                            catch { }
                            #endregion

                            #region Cost Calculation For shipment --
                            try
                            {
                                if (Util.GetValueOfString(dsRecord.Tables[0].Rows[z]["TableName"]) == "M_InOut" &&
                                    Util.GetValueOfString(dsRecord.Tables[0].Rows[z]["issotrx"]) == "Y" &&
                                    Util.GetValueOfString(dsRecord.Tables[0].Rows[z]["isreturntrx"]) == "N" &&
                                    (Util.GetValueOfString(dsRecord.Tables[0].Rows[z]["docstatus"]) == "CO" ||
                                     Util.GetValueOfString(dsRecord.Tables[0].Rows[z]["docstatus"]) == "CL"))
                                {

                                    CalculateCostForShipment(Util.GetValueOfInt(dsRecord.Tables[0].Rows[z]["Record_Id"]));

                                    continue;
                                }
                            }
                            catch { }
                            #endregion

                            #region Cost Calculation For Customer Return --
                            try
                            {
                                if (Util.GetValueOfString(dsRecord.Tables[0].Rows[z]["TableName"]) == "M_InOut" &&
                                    Util.GetValueOfString(dsRecord.Tables[0].Rows[z]["issotrx"]) == "Y" &&
                                    Util.GetValueOfString(dsRecord.Tables[0].Rows[z]["isreturntrx"]) == "Y" &&
                                    (Util.GetValueOfString(dsRecord.Tables[0].Rows[z]["docstatus"]) == "CO" ||
                                     Util.GetValueOfString(dsRecord.Tables[0].Rows[z]["docstatus"]) == "CL"))
                                {

                                    CalculateCostForCustomerReturn(Util.GetValueOfInt(dsRecord.Tables[0].Rows[z]["Record_Id"]));

                                    continue;
                                }
                            }
                            catch { }
                            #endregion

                            #region Component Reduce for Production Execution --
                            try
                            {
                                if (count > 0)
                                {
                                    if (Util.GetValueOfString(dsRecord.Tables[0].Rows[z]["TableName"]) == "VAMFG_M_WrkOdrTransaction" &&
                                    (Util.GetValueOfString(dsRecord.Tables[0].Rows[z]["docstatus"]) == "CO" ||
                                     Util.GetValueOfString(dsRecord.Tables[0].Rows[z]["docstatus"]) == "CL"))
                                    {

                                        _log.Info("costng calculation start for production execution for document no =  " + Util.GetValueOfString(dsRecord.Tables[0].Rows[z]["documentno"]));
                                        CalculateCostForProduction(Util.GetValueOfInt(dsRecord.Tables[0].Rows[z]["Record_Id"]), Util.GetValueOfString(dsRecord.Tables[0].Rows[z]["issotrx"]));

                                        continue;
                                    }
                                }
                            }
                            catch (Exception exProductionExecution)
                            {
                                _log.Info("Error Occured during Production Execution costing " + exProductionExecution.ToString());
                            }


                            #endregion

                            //Reverse Record

                            #region Component Reduce for Production Execution --
                            try
                            {
                                if (count > 0)
                                {
                                    if (Util.GetValueOfString(dsRecord.Tables[0].Rows[z]["TableName"]) == "VAMFG_M_WrkOdrTransaction" &&
                                        Util.GetValueOfString(dsRecord.Tables[0].Rows[z]["docstatus"]) == "RE")
                                    {

                                        CalculateCostForProduction(Util.GetValueOfInt(dsRecord.Tables[0].Rows[z]["Record_Id"]), Util.GetValueOfString(dsRecord.Tables[0].Rows[z]["issotrx"]));

                                        continue;
                                    }
                                }
                            }
                            catch (Exception exProductionExecution)
                            {
                                _log.Info("Error Occured during Production Execution costing " + exProductionExecution.ToString());
                            }


                            #endregion

                            #region Cost Calculation For Customer Return --
                            try
                            {
                                if (Util.GetValueOfString(dsRecord.Tables[0].Rows[z]["TableName"]) == "M_InOut" &&
                                   Util.GetValueOfString(dsRecord.Tables[0].Rows[z]["issotrx"]) == "Y" &&
                                   Util.GetValueOfString(dsRecord.Tables[0].Rows[z]["isreturntrx"]) == "Y" &&
                                   Util.GetValueOfString(dsRecord.Tables[0].Rows[z]["docstatus"]) == "RE")
                                {

                                    CalculateCostForCustomerReturn(Util.GetValueOfInt(dsRecord.Tables[0].Rows[z]["Record_Id"]));

                                    continue;
                                }
                            }
                            catch { }
                            #endregion

                            #region Cost Calculation For shipment --
                            try
                            {
                                if (Util.GetValueOfString(dsRecord.Tables[0].Rows[z]["TableName"]) == "M_InOut" &&
                                   Util.GetValueOfString(dsRecord.Tables[0].Rows[z]["issotrx"]) == "Y" &&
                                   Util.GetValueOfString(dsRecord.Tables[0].Rows[z]["isreturntrx"]) == "N" &&
                                   Util.GetValueOfString(dsRecord.Tables[0].Rows[z]["docstatus"]) == "RE")
                                {

                                    CalculateCostForShipment(Util.GetValueOfInt(dsRecord.Tables[0].Rows[z]["Record_Id"]));

                                    continue;
                                }
                            }
                            catch { }
                            #endregion

                            #region Cost Calculation Against AP Credit Memo - During Return Cycle of Purchase - Reverse --
                            try
                            {
                                if (Util.GetValueOfString(dsRecord.Tables[0].Rows[z]["TableName"]) == "M_MatchInvCostTrack" &&
                                    (Util.GetValueOfString(dsRecord.Tables[0].Rows[z]["issotrx"]) == "N" &&
                                     Util.GetValueOfString(dsRecord.Tables[0].Rows[z]["isreturntrx"]) == "Y"))
                                {

                                    CalculateCostCreditMemoreversal(Util.GetValueOfInt(dsRecord.Tables[0].Rows[z]["Record_Id"]));

                                    continue;
                                }
                            }
                            catch { }
                            #endregion

                            #region Cost Calculation For  Return to Vendor --
                            try
                            {
                                if (Util.GetValueOfString(dsRecord.Tables[0].Rows[z]["TableName"]) == "M_InOut" &&
                                   Util.GetValueOfString(dsRecord.Tables[0].Rows[z]["issotrx"]) == "N" &&
                                   Util.GetValueOfString(dsRecord.Tables[0].Rows[z]["isreturntrx"]) == "Y" &&
                                   Util.GetValueOfString(dsRecord.Tables[0].Rows[z]["docstatus"]) == "RE")
                                {

                                    CalculateCostForReturnToVendor(Util.GetValueOfInt(dsRecord.Tables[0].Rows[z]["Record_Id"]));

                                    CalculateCostForPurchaseInvoice(Util.GetValueOfInt(dsRecord.Tables[0].Rows[z]["Record_Id"]));

                                    continue;
                                }
                            }
                            catch { }
                            #endregion

                            #region Cost Calculation for Inventory Move --
                            try
                            {
                                if (Util.GetValueOfString(dsRecord.Tables[0].Rows[z]["TableName"]) == "M_Movement" &&
                                  Util.GetValueOfString(dsRecord.Tables[0].Rows[z]["docstatus"]) == "RE")
                                {

                                    CalculateCostForMovement(Util.GetValueOfInt(dsRecord.Tables[0].Rows[z]["Record_Id"]));

                                    continue;
                                }
                            }
                            catch { }
                            #endregion

                            #region Cost Calculation for  Internal use inventory --
                            try
                            {
                                if (Util.GetValueOfString(dsRecord.Tables[0].Rows[z]["TableName"]) == "M_Inventory" &&
                                   Util.GetValueOfString(dsRecord.Tables[0].Rows[z]["docstatus"]) == "RE" &&
                                   Util.GetValueOfString(dsRecord.Tables[0].Rows[z]["IsInternalUse"]) == "Y")
                                {

                                    CalculateCostForInventory(Util.GetValueOfInt(dsRecord.Tables[0].Rows[z]["Record_Id"]));

                                    continue;
                                }
                            }
                            catch { }
                            #endregion

                            #region Cost Calculation for Physical Inventory --
                            try
                            {
                                if (Util.GetValueOfString(dsRecord.Tables[0].Rows[z]["TableName"]) == "M_Inventory" &&
                                  Util.GetValueOfString(dsRecord.Tables[0].Rows[z]["docstatus"]) == "RE" &&
                                  Util.GetValueOfString(dsRecord.Tables[0].Rows[z]["IsInternalUse"]) == "N")
                                {

                                    CalculateCostForInventory(Util.GetValueOfInt(dsRecord.Tables[0].Rows[z]["Record_Id"]));

                                    continue;
                                }
                            }
                            catch { }
                            #endregion

                            #region Cost Calculation for  PO Cycle Reverse --
                            try
                            {
                                if (Util.GetValueOfString(dsRecord.Tables[0].Rows[z]["TableName"]) == "M_MatchInvCostTrack" &&
                                    (Util.GetValueOfString(dsRecord.Tables[0].Rows[z]["issotrx"]) == "N" &&
                                     Util.GetValueOfString(dsRecord.Tables[0].Rows[z]["isreturntrx"]) == "N"))
                                {

                                    CalculateCostForMatchInvoiceReversal(Util.GetValueOfInt(dsRecord.Tables[0].Rows[z]["Record_Id"]));

                                    continue;
                                }
                            }
                            catch { }
                            #endregion

                            #region Cost Calculation for SO / CRMA / VRMA
                            try
                            {
                                if (Util.GetValueOfString(dsRecord.Tables[0].Rows[z]["TableName"]) == "C_Invoice" &&
                                   Util.GetValueOfString(dsRecord.Tables[0].Rows[z]["docstatus"]) == "RE")
                                {
                                    invoice = new MInvoice(GetCtx(), Util.GetValueOfInt(dsRecord.Tables[0].Rows[z]["Record_Id"]), Get_Trx());

                                    sql.Clear();
                                    if (invoice.GetDescription() != null && invoice.GetDescription().Contains("{->"))
                                    {
                                        sql.Append("SELECT * FROM C_InvoiceLine WHERE " + (M_AttributeSetInstance_ID > 0 ? $" M_AttributeSetInstance_ID = {M_AttributeSetInstance_ID} AND " : "") +
                                                    @"IsActive = 'Y' AND iscostcalculated = 'Y' AND IsReversedCostCalculated = 'N' " +
                                                    " AND C_Invoice_ID = " + invoice.GetC_Invoice_ID());
                                        if (!String.IsNullOrEmpty(productCategoryID) && String.IsNullOrEmpty(productID))
                                        {
                                            sql.Append(" AND M_Product_ID IN (SELECT M_Product_ID FROM M_Product WHERE M_Product_Category_ID IN (" + productCategoryID + " ) ) ");
                                        }
                                        else
                                        {
                                            sql.Append(" AND M_Product_ID IN (" + productID + " )");
                                        }
                                        sql.Append(" ORDER BY Line");
                                    }
                                    else
                                    {
                                        sql.Append("SELECT * FROM C_InvoiceLine WHERE " + (M_AttributeSetInstance_ID > 0 ? $" M_AttributeSetInstance_ID = {M_AttributeSetInstance_ID} AND " : "") +
                                                    @"IsActive = 'Y' AND iscostcalculated = 'N' " +
                                                    " AND C_Invoice_ID = " + invoice.GetC_Invoice_ID());
                                        sql.Append(@" AND IsCostImmediate = 'N' ");
                                        if (!String.IsNullOrEmpty(productCategoryID) && String.IsNullOrEmpty(productID))
                                        {
                                            sql.Append(" AND M_Product_ID IN (SELECT M_Product_ID FROM M_Product WHERE M_Product_Category_ID IN (" + productCategoryID + " ) ) ");
                                        }
                                        else
                                        {
                                            sql.Append(" AND M_Product_ID IN (" + productID + " )");
                                        }
                                        sql.Append(" ORDER BY Line");
                                    }
                                    dsChildRecord = DB.ExecuteDataset(sql.ToString(), null, Get_Trx());
                                    if (dsChildRecord != null && dsChildRecord.Tables.Count > 0 && dsChildRecord.Tables[0].Rows.Count > 0)
                                    {
                                        /*Costing Object*/
                                        costingCheck = new CostingCheck(GetCtx());
                                        costingCheck.dsAccountingSchema = costingCheck.GetAccountingSchema(GetAD_Client_ID());
                                        costingCheck.invoice = invoice;
                                        costingCheck.isReversal = invoice.IsReversal();

                                        for (int j = 0; j < dsChildRecord.Tables[0].Rows.Count; j++)
                                        {
                                            try
                                            {
                                                //VIS_0045: Reset Class parameters
                                                if (costingCheck != null)
                                                {
                                                    costingCheck.ResetProperty();
                                                }
                                                costingCheck.AD_Org_ID = invoice.GetAD_Org_ID();
                                                costingCheck.movementDate = invoice.GetDateAcct();
                                                costingCheck.isReversal = invoice.IsReversal();

                                                product = new MProduct(GetCtx(), Util.GetValueOfInt(dsChildRecord.Tables[0].Rows[j]["M_Product_ID"]), Get_Trx());
                                                invoiceLine = new MInvoiceLine(GetCtx(), dsChildRecord.Tables[0].Rows[j], Get_Trx());

                                                if (invoiceLine != null && invoiceLine.GetC_Invoice_ID() > 0 && invoiceLine.GetQtyInvoiced() == 0)
                                                    continue;

                                                if (invoiceLine != null && invoiceLine.Get_ID() > 0)
                                                {
                                                    ProductInvoiceLineCost = invoiceLine.GetProductLineCost(invoiceLine, true);
                                                }

                                                costingCheck.invoiceline = invoiceLine;
                                                costingCheck.product = product;

                                                if (invoiceLine.GetC_OrderLine_ID() > 0)
                                                {
                                                    if (invoiceLine.GetC_Charge_ID() > 0)
                                                    {
                                                        #region Landed Cost Allocation
                                                        if (!invoice.IsSOTrx() && !invoice.IsReturnTrx())
                                                        {
                                                            if (!MCostQueue.CreateProductCostsDetails(GetCtx(), invoice.GetAD_Client_ID(), invoice.GetAD_Org_ID(), null, 0, "Invoice(Vendor)",
                                                                null, null, null, invoiceLine, null, ProductInvoiceLineCost, 0, Get_Trx(), costingCheck, out conversionNotFoundInvoice, optionalstr: "window"))
                                                            {
                                                                if (!conversionNotFoundInvoice1.Contains(conversionNotFoundInvoice))
                                                                {
                                                                    conversionNotFoundInvoice1 += conversionNotFoundInvoice + " , ";
                                                                }
                                                                _log.Info("Cost not Calculated for Invoice(Vendor) for this Line ID = "
                                                                    + invoiceLine.GetC_InvoiceLine_ID() +
                                                                   " , " + (!string.IsNullOrEmpty(costingCheck.errorMessage) ? costingCheck.errorMessage : ""));
                                                            }
                                                            else
                                                            {
                                                                if (invoice.GetDescription() != null && invoice.GetDescription().Contains("{->"))
                                                                {
                                                                    //invoiceLine.SetIsReversedCostCalculated(true);
                                                                }
                                                                //invoiceLine.SetIsCostCalculated(true);
                                                                //if (client.IsCostImmediate() && !invoiceLine.IsCostImmediate())
                                                                {
                                                                    invoiceLine.SetIsCostImmediate(true);
                                                                }
                                                                if (!invoiceLine.Save(Get_Trx()))
                                                                {
                                                                    ValueNamePair pp = VLogger.RetrieveError();
                                                                    _log.Info("Error found for saving Invoice(Vendor) for this Line ID = " + invoiceLine.GetC_InvoiceLine_ID() +
                                                                               " Error Name is " + pp.GetName() + " And Error Type is " + pp.GetType());
                                                                    Get_Trx().Rollback();
                                                                }
                                                                else
                                                                {
                                                                    _log.Fine("Cost Calculation updated for C_InvoiceLine = " + invoiceLine.GetC_InvoiceLine_ID());
                                                                    Get_Trx().Commit();
                                                                }
                                                            }
                                                        }
                                                        #endregion
                                                    }
                                                    else
                                                    {
                                                        #region for Expense type product
                                                        if (product.GetProductType() == "E" && product.GetM_Product_ID() > 0)
                                                        {
                                                            if (!MCostQueue.CreateProductCostsDetails(GetCtx(), invoice.GetAD_Client_ID(), invoice.GetAD_Org_ID(), product, 0,
                                                                 "Invoice(Vendor)", null, null, null, invoiceLine, null, ProductInvoiceLineCost, 0, Get_Trx(),
                                                                 costingCheck, out conversionNotFoundInvoice, optionalstr: "window"))
                                                            {
                                                                if (!conversionNotFoundInvoice1.Contains(conversionNotFoundInvoice))
                                                                {
                                                                    conversionNotFoundInvoice1 += conversionNotFoundInvoice + " , ";
                                                                }
                                                                _log.Info("Cost not Calculated for Invoice(Vendor) for this Line ID = "
                                                                    + invoiceLine.GetC_InvoiceLine_ID() +
                                                                 " , " + (!string.IsNullOrEmpty(costingCheck.errorMessage) ? costingCheck.errorMessage : ""));
                                                            }
                                                            else
                                                            {
                                                                if (invoice.GetDescription() != null && invoice.GetDescription().Contains("{->"))
                                                                {
                                                                    //invoiceLine.SetIsReversedCostCalculated(true);
                                                                }
                                                                //invoiceLine.SetIsCostCalculated(true);
                                                                //if (client.IsCostImmediate() && !invoiceLine.IsCostImmediate())
                                                                {
                                                                    invoiceLine.SetIsCostImmediate(true);
                                                                }
                                                                if (!invoiceLine.Save(Get_Trx()))
                                                                {
                                                                    ValueNamePair pp = VLogger.RetrieveError();
                                                                    _log.Info("Error found for saving Invoice(Vendor) for this Line ID = " + invoiceLine.GetC_InvoiceLine_ID() +
                                                                               " Error Name is " + pp.GetName() + " And Error Type is " + pp.GetType());
                                                                    Get_Trx().Rollback();
                                                                }
                                                                else
                                                                {
                                                                    _log.Fine("Cost Calculation updated for C_InvoiceLine = " + invoiceLine.GetC_InvoiceLine_ID());
                                                                    Get_Trx().Commit();
                                                                }
                                                            }
                                                        }
                                                        #endregion

                                                        #region  for Item Type product
                                                        else if (product.GetProductType() == "I" && product.GetM_Product_ID() > 0)
                                                        {
                                                            if (countColumnExist > 0)
                                                            {
                                                                isCostAdjustableOnLost = product.IsCostAdjustmentOnLost();
                                                            }

                                                            MOrder order1 = new MOrder(GetCtx(), invoice.GetC_Order_ID(), Get_Trx());
                                                            MOrderLine ol1 = new MOrderLine(GetCtx(), invoiceLine.GetC_OrderLine_ID(), Get_Trx());
                                                            if (order1.GetC_Order_ID() == 0 || order1.GetC_Order_ID() != ol1.GetC_Order_ID())
                                                            {
                                                                order1 = new MOrder(GetCtx(), ol1.GetC_Order_ID(), Get_Trx());
                                                            }

                                                            costingCheck.order = order1;
                                                            costingCheck.orderline = ol1;

                                                            #region  Sales Order
                                                            if (order1.IsSOTrx() && !order1.IsReturnTrx())
                                                            {
                                                                if (!MCostQueue.CreateProductCostsDetails(GetCtx(), invoice.GetAD_Client_ID(), invoice.GetAD_Org_ID(), product, invoiceLine.GetM_AttributeSetInstance_ID(),
                                                                      "Invoice(Customer)", null, null, null, invoiceLine, null, Decimal.Negate(ProductInvoiceLineCost), Decimal.Negate(invoiceLine.GetQtyInvoiced()),
                                                                      Get_Trx(), costingCheck, out conversionNotFoundInvoice, optionalstr: "window"))
                                                                {
                                                                    if (!conversionNotFoundInvoice1.Contains(conversionNotFoundInvoice))
                                                                    {
                                                                        conversionNotFoundInvoice1 += conversionNotFoundInvoice + " , ";
                                                                    }
                                                                    _log.Info("Cost not Calculated for Invoice(Vendor) for this Line ID = "
                                                                        + invoiceLine.GetC_InvoiceLine_ID() +
                                                                   " , " + (!string.IsNullOrEmpty(costingCheck.errorMessage) ? costingCheck.errorMessage : ""));
                                                                }
                                                                else
                                                                {
                                                                    if (invoice.GetDescription() != null && invoice.GetDescription().Contains("{->"))
                                                                    {
                                                                        //invoiceLine.SetIsReversedCostCalculated(true);
                                                                    }
                                                                    if (invoiceLine.GetM_InOutLine_ID() > 0)
                                                                    {
                                                                        DataSet ds = DB.ExecuteDataset(@"SELECT M_InOutLine.CurrentCostPrice, M_InOut.M_Warehouse_ID 
                                                                                FROM M_InOutLine INNER JOIN M_InOut ON M_InOut.M_InOut_ID = M_InOutLine.M_InOut_ID
                                                                                WHERE M_InOutLine.M_InOutLIne_ID = " + invoiceLine.GetM_InOutLine_ID(), null, Get_Trx());
                                                                        if (ds != null && ds.Tables.Count > 0 && ds.Tables[0].Rows.Count > 0)
                                                                        {
                                                                            if (IsCostUpdation)
                                                                            {
                                                                                invoiceLine.SetCurrentCostPrice(Util.GetValueOfDecimal(ds.Tables[0].Rows[0]["CurrentCostPrice"]));
                                                                            }

                                                                            currentCostPrice = MCost.GetproductCostAndQtyMaterial(invoiceLine.GetAD_Client_ID(), invoiceLine.GetAD_Org_ID(),
                                                                                                       invoiceLine.GetM_Product_ID(), invoiceLine.GetM_AttributeSetInstance_ID(), Get_Trx(),
                                                                                                       Util.GetValueOfInt(ds.Tables[0].Rows[0]["M_Warehouse_ID"]), false);
                                                                            if (IsCostUpdation)
                                                                            {
                                                                                invoiceLine.SetPostCurrentCostPrice(currentCostPrice);
                                                                            }
                                                                        }
                                                                    }
                                                                    //invoiceLine.SetIsCostCalculated(true);
                                                                    //if (client.IsCostImmediate() && !invoiceLine.IsCostImmediate())
                                                                    {
                                                                        invoiceLine.SetIsCostImmediate(true);
                                                                    }
                                                                    if (!invoiceLine.Save(Get_Trx()))
                                                                    {
                                                                        ValueNamePair pp = VLogger.RetrieveError();
                                                                        _log.Info("Error found for saving Invoice(Vendor) for this Line ID = " + invoiceLine.GetC_InvoiceLine_ID() +
                                                                                   " Error Name is " + pp.GetName() + " And Error Type is " + pp.GetType());
                                                                        Get_Trx().Rollback();
                                                                    }
                                                                    else
                                                                    {
                                                                        _log.Fine("Cost Calculation updated for C_InvoiceLine = " + invoiceLine.GetC_InvoiceLine_ID());
                                                                        Get_Trx().Commit();
                                                                    }
                                                                }
                                                            }
                                                            #endregion

                                                            #region CRMA
                                                            else if (order1.IsSOTrx() && order1.IsReturnTrx())
                                                            {
                                                                if (!MCostQueue.CreateProductCostsDetails(GetCtx(), invoice.GetAD_Client_ID(), invoice.GetAD_Org_ID(), product, invoiceLine.GetM_AttributeSetInstance_ID(),
                                                                  "Invoice(Customer)", null, null, null, invoiceLine, null, ProductInvoiceLineCost, invoiceLine.GetQtyInvoiced(),
                                                                  Get_Trx(), costingCheck, out conversionNotFoundInvoice, optionalstr: "window"))
                                                                {
                                                                    if (!conversionNotFoundInvoice1.Contains(conversionNotFoundInvoice))
                                                                    {
                                                                        conversionNotFoundInvoice1 += conversionNotFoundInvoice + " , ";
                                                                    }
                                                                    _log.Info("Cost not Calculated for Invoice(Customer) for this Line ID = "
                                                                        + invoiceLine.GetC_InvoiceLine_ID() + " , " + (!string.IsNullOrEmpty(costingCheck.errorMessage) ? costingCheck.errorMessage : ""));
                                                                }
                                                                else
                                                                {
                                                                    if (invoice.GetDescription() != null && invoice.GetDescription().Contains("{->"))
                                                                    {
                                                                        //invoiceLine.SetIsReversedCostCalculated(true);
                                                                    }
                                                                    if (invoiceLine.GetM_InOutLine_ID() > 0)
                                                                    {
                                                                        DataSet ds = DB.ExecuteDataset(@"SELECT M_InOutLine.CurrentCostPrice, M_InOut.M_Warehouse_ID 
                                                                                FROM M_InOutLine INNER JOIN M_InOut ON M_InOut.M_InOut_ID = M_InOutLine.M_InOut_ID
                                                                                WHERE M_InOutLine.M_InOutLIne_ID = " + invoiceLine.GetM_InOutLine_ID(), null, Get_Trx());
                                                                        if (ds != null && ds.Tables.Count > 0 && ds.Tables[0].Rows.Count > 0)
                                                                        {
                                                                            if (IsCostUpdation)
                                                                            {
                                                                                invoiceLine.SetCurrentCostPrice(Util.GetValueOfDecimal(ds.Tables[0].Rows[0]["CurrentCostPrice"]));
                                                                            }

                                                                            currentCostPrice = MCost.GetproductCostAndQtyMaterial(invoiceLine.GetAD_Client_ID(), invoiceLine.GetAD_Org_ID(),
                                                                                                       invoiceLine.GetM_Product_ID(), invoiceLine.GetM_AttributeSetInstance_ID(), Get_Trx(),
                                                                                                       Util.GetValueOfInt(ds.Tables[0].Rows[0]["M_Warehouse_ID"]), false);
                                                                            if (IsCostUpdation)
                                                                            {
                                                                                invoiceLine.SetPostCurrentCostPrice(currentCostPrice);
                                                                            }
                                                                        }
                                                                    }
                                                                    //invoiceLine.SetIsCostCalculated(true);
                                                                    //if (client.IsCostImmediate() && !invoiceLine.IsCostImmediate())
                                                                    {
                                                                        invoiceLine.SetIsCostImmediate(true);
                                                                    }
                                                                    if (!invoiceLine.Save(Get_Trx()))
                                                                    {
                                                                        ValueNamePair pp = VLogger.RetrieveError();
                                                                        _log.Info("Error found for saving Invoice(Customer) for this Line ID = " + invoiceLine.GetC_InvoiceLine_ID() +
                                                                                   " Error Name is " + pp.GetName() + " And Error Type is " + pp.GetType());
                                                                        Get_Trx().Rollback();
                                                                    }
                                                                    else
                                                                    {
                                                                        _log.Fine("Cost Calculation updated for C_InvoiceLine = " + invoiceLine.GetC_InvoiceLine_ID());
                                                                        Get_Trx().Commit();
                                                                    }
                                                                }
                                                            }
                                                            #endregion

                                                            #region VRMA
                                                            else if (!order1.IsSOTrx() && order1.IsReturnTrx())
                                                            {
                                                                //change 12-5-2016
                                                                // when Ap Credit memo is alone then we will do a impact on costing.
                                                                // this is bcz of giving discount for particular product
                                                                // discount is given only when document type having setting as "Treat As Discount" = True
                                                                MDocType docType = new MDocType(GetCtx(), invoice.GetC_DocTypeTarget_ID(), Get_Trx());
                                                                if (docType.GetDocBaseType() == "APC" && docType.IsTreatAsDiscount() && invoiceLine.GetC_OrderLine_ID() == 0 && invoiceLine.GetM_InOutLine_ID() == 0 && invoiceLine.GetM_Product_ID() > 0)
                                                                {
                                                                    if (!MCostQueue.CreateProductCostsDetails(GetCtx(), invoice.GetAD_Client_ID(), invoice.GetAD_Org_ID(), product, invoiceLine.GetM_AttributeSetInstance_ID(),
                                                                      "Invoice(Vendor)", null, null, null, invoiceLine, null, Decimal.Negate(ProductInvoiceLineCost), Decimal.Negate(invoiceLine.GetQtyInvoiced()),
                                                                      Get_Trx(), costingCheck, out conversionNotFoundInvoice, optionalstr: "window"))
                                                                    {
                                                                        if (!conversionNotFoundInvoice1.Contains(conversionNotFoundInvoice))
                                                                        {
                                                                            conversionNotFoundInvoice1 += conversionNotFoundInvoice + " , ";
                                                                        }
                                                                        _log.Info("Cost not Calculated for Invoice(Vendor) for this Line ID = "
                                                                            + invoiceLine.GetC_InvoiceLine_ID() + " , " + (!string.IsNullOrEmpty(costingCheck.errorMessage) ? costingCheck.errorMessage : ""));
                                                                    }
                                                                    else
                                                                    {
                                                                        query.Clear();
                                                                        query.Append($@"SELECT NVL(iol.MovementQty, 0) AS MovementQty, iol.M_Locator_ID, io.M_Warehouse_ID FROM C_InvoiceLine il
                                                                        INNER JOIN M_InoutLine iol ON (il.M_InoutLine_ID = iol.M_InoutLine_ID)
                                                                        INNER JOIN M_Inout io ON (io.M_InOut_ID = iol.M_InOut_ID)
                                                                        WHERE il.C_InvoiceLine_ID =  { invoiceLine.Get_ValueAsInt("Ref_InvoiceLineOrg_ID")}");
                                                                        DataSet dsRefInOut = DB.ExecuteDataset(query.ToString(), null, Get_Trx());
                                                                        int M_Locator_ID = 0;
                                                                        int M_Warehouse_ID = invoiceLine.GetM_Warehouse_ID() > 0 ? invoiceLine.GetM_Warehouse_ID() : invoice.GetM_Warehouse_ID();
                                                                        if (dsRefInOut != null && dsRefInOut.Tables.Count > 0 && dsRefInOut.Tables[0].Rows.Count > 0)
                                                                        {
                                                                            M_Locator_ID = Util.GetValueOfInt(dsRefInOut.Tables[0].Rows[0]["M_Locator_ID"]);
                                                                            if (M_Warehouse_ID == 0)
                                                                            {
                                                                                M_Warehouse_ID = Util.GetValueOfInt(dsRefInOut.Tables[0].Rows[0]["M_Warehouse_ID"]);
                                                                            }
                                                                        }

                                                                        if (invoice.GetDescription() != null && invoice.GetDescription().Contains("{->"))
                                                                        {
                                                                            //invoiceLine.SetIsReversedCostCalculated(true);
                                                                        }
                                                                        //invoiceLine.SetIsCostCalculated(true);
                                                                        //if (client.IsCostImmediate() && !invoiceLine.IsCostImmediate())
                                                                        {
                                                                            invoiceLine.SetIsCostImmediate(true);
                                                                        }
                                                                        if (!invoiceLine.Save(Get_Trx()))
                                                                        {
                                                                            ValueNamePair pp = VLogger.RetrieveError();
                                                                            _log.Info("Error found for saving Invoice(Vendor) for this Line ID = " + invoiceLine.GetC_InvoiceLine_ID() +
                                                                                       " Error Name is " + pp.GetName() + " And Error Type is " + pp.GetType());
                                                                            Get_Trx().Rollback();
                                                                        }
                                                                        else
                                                                        {
                                                                            _log.Fine("Cost Calculation updated for C_InvoiceLine = " + invoiceLine.GetC_InvoiceLine_ID());
                                                                            Get_Trx().Commit();

                                                                            // get cost from Product Cost after cost calculation, and update on Product Transaction against Invoice
                                                                            currentCostPrice = MCost.GetproductCosts(GetAD_Client_ID(), GetAD_Org_ID(),
                                                                                               product.GetM_Product_ID(), invoiceLine.GetM_AttributeSetInstance_ID(), Get_Trx(), M_Warehouse_ID);
                                                                            UpdateTransactionCostForInvoice(currentCostPrice, invoiceLine.GetC_InvoiceLine_ID(), costingCheck);
                                                                            Get_Trx().Commit();
                                                                        }
                                                                    }
                                                                }
                                                            }
                                                            #endregion
                                                        }
                                                        #endregion
                                                    }
                                                }
                                                else
                                                {
                                                    #region for Landed Cost Allocation
                                                    if (invoiceLine.GetC_Charge_ID() > 0)
                                                    {
                                                        if (!invoice.IsSOTrx() && !invoice.IsReturnTrx())
                                                        {
                                                            if (!MCostQueue.CreateProductCostsDetails(GetCtx(), invoice.GetAD_Client_ID(), invoice.GetAD_Org_ID(), null, 0,
                                                                "Invoice(Vendor)", null, null, null, invoiceLine, null, ProductInvoiceLineCost, 0, Get_TrxName(),
                                                                costingCheck, out conversionNotFoundInvoice, optionalstr: "window"))
                                                            {
                                                                if (!conversionNotFoundInvoice1.Contains(conversionNotFoundInvoice))
                                                                {
                                                                    conversionNotFoundInvoice1 += conversionNotFoundInvoice + " , ";
                                                                }
                                                                _log.Info("Cost not Calculated for Invoice(Vendor) for this Line ID = "
                                                                    + invoiceLine.GetC_InvoiceLine_ID() + " , " + (!string.IsNullOrEmpty(costingCheck.errorMessage) ? costingCheck.errorMessage : ""));
                                                            }
                                                            else
                                                            {
                                                                if (invoice.GetDescription() != null && invoice.GetDescription().Contains("{->"))
                                                                {
                                                                    //invoiceLine.SetIsReversedCostCalculated(true);
                                                                }
                                                                //invoiceLine.SetIsCostCalculated(true);
                                                                //if (client.IsCostImmediate() && !invoiceLine.IsCostImmediate())
                                                                {
                                                                    invoiceLine.SetIsCostImmediate(true);
                                                                }
                                                                if (!invoiceLine.Save(Get_Trx()))
                                                                {
                                                                    ValueNamePair pp = VLogger.RetrieveError();
                                                                    _log.Info("Error found for saving Invoice(Vendor) for this Line ID = " + invoiceLine.GetC_InvoiceLine_ID() +
                                                                               " Error Name is " + pp.GetName() + " And Error Type is " + pp.GetType());
                                                                    Get_Trx().Rollback();
                                                                }
                                                                else
                                                                {
                                                                    _log.Fine("Cost Calculation updated for C_InvoiceLine = " + invoiceLine.GetC_InvoiceLine_ID());
                                                                    Get_Trx().Commit();
                                                                }
                                                            }
                                                        }
                                                    }
                                                    #endregion

                                                    #region for Expense type product
                                                    if (product.GetProductType() == "E" && product.GetM_Product_ID() > 0)
                                                    {
                                                        if (!MCostQueue.CreateProductCostsDetails(GetCtx(), invoice.GetAD_Client_ID(), invoice.GetAD_Org_ID(), product, 0,
                                                            "Invoice(Vendor)", null, null, null, invoiceLine, null, ProductInvoiceLineCost, 0,
                                                            Get_TrxName(), costingCheck, out conversionNotFoundInvoice, optionalstr: "window"))
                                                        {
                                                            if (!conversionNotFoundInvoice1.Contains(conversionNotFoundInvoice))
                                                            {
                                                                conversionNotFoundInvoice1 += conversionNotFoundInvoice + " , ";
                                                            }
                                                            _log.Info("Cost not Calculated for Invoice(Vendor) for this Line ID = "
                                                                + invoiceLine.GetC_InvoiceLine_ID() + " , " + (!string.IsNullOrEmpty(costingCheck.errorMessage) ? costingCheck.errorMessage : ""));
                                                        }
                                                        else
                                                        {
                                                            if (invoice.GetDescription() != null && invoice.GetDescription().Contains("{->"))
                                                            {
                                                                //invoiceLine.SetIsReversedCostCalculated(true);
                                                            }
                                                            //invoiceLine.SetIsCostCalculated(true);
                                                            //if (client.IsCostImmediate() && !invoiceLine.IsCostImmediate())
                                                            {
                                                                invoiceLine.SetIsCostImmediate(true);
                                                            }
                                                            if (!invoiceLine.Save(Get_Trx()))
                                                            {
                                                                ValueNamePair pp = VLogger.RetrieveError();
                                                                _log.Info("Error found for saving Invoice(Vendor) for this Line ID = " + invoiceLine.GetC_InvoiceLine_ID() +
                                                                           " Error Name is " + pp.GetName() + " And Error Type is " + pp.GetType());
                                                                Get_Trx().Rollback();
                                                            }
                                                            else
                                                            {
                                                                _log.Fine("Cost Calculation updated for C_InvoiceLine = " + invoiceLine.GetC_InvoiceLine_ID());
                                                                Get_Trx().Commit();
                                                            }
                                                        }
                                                    }
                                                    #endregion

                                                    #region  for Item Type product
                                                    else if (product.GetProductType() == "I" && product.GetM_Product_ID() > 0)
                                                    {
                                                        if (countColumnExist > 0)
                                                        {
                                                            isCostAdjustableOnLost = product.IsCostAdjustmentOnLost();
                                                        }

                                                        #region Sales Order
                                                        if (invoice.IsSOTrx() && !invoice.IsReturnTrx())
                                                        {
                                                            if (!MCostQueue.CreateProductCostsDetails(GetCtx(), invoice.GetAD_Client_ID(), invoice.GetAD_Org_ID(), product, invoiceLine.GetM_AttributeSetInstance_ID(),
                                                                  "Invoice(Customer)", null, null, null, invoiceLine, null, Decimal.Negate(ProductInvoiceLineCost), Decimal.Negate(invoiceLine.GetQtyInvoiced()),
                                                                  Get_Trx(), costingCheck, out conversionNotFoundInvoice, optionalstr: "window"))
                                                            {
                                                                if (!conversionNotFoundInvoice1.Contains(conversionNotFoundInvoice))
                                                                {
                                                                    conversionNotFoundInvoice1 += conversionNotFoundInvoice + " , ";
                                                                }
                                                                _log.Info("Cost not Calculated for Invoice(Vendor) for this Line ID = "
                                                                    + invoiceLine.GetC_InvoiceLine_ID() + " , " + (!string.IsNullOrEmpty(costingCheck.errorMessage) ? costingCheck.errorMessage : ""));
                                                            }
                                                            else
                                                            {
                                                                if (invoice.GetDescription() != null && invoice.GetDescription().Contains("{->"))
                                                                {
                                                                    //invoiceLine.SetIsReversedCostCalculated(true);
                                                                }
                                                                if (invoiceLine.GetM_InOutLine_ID() > 0)
                                                                {
                                                                    DataSet ds = DB.ExecuteDataset(@"SELECT M_InOutLine.CurrentCostPrice, M_InOut.M_Warehouse_ID 
                                                                                FROM M_InOutLine INNER JOIN M_InOut ON M_InOut.M_InOut_ID = M_InOutLine.M_InOut_ID
                                                                                WHERE M_InOutLine.M_InOutLIne_ID = " + invoiceLine.GetM_InOutLine_ID(), null, Get_Trx());
                                                                    if (ds != null && ds.Tables.Count > 0 && ds.Tables[0].Rows.Count > 0)
                                                                    {
                                                                        if (IsCostUpdation)
                                                                        {
                                                                            invoiceLine.SetCurrentCostPrice(Util.GetValueOfDecimal(ds.Tables[0].Rows[0]["CurrentCostPrice"]));
                                                                        }

                                                                        currentCostPrice = MCost.GetproductCostAndQtyMaterial(invoiceLine.GetAD_Client_ID(), invoiceLine.GetAD_Org_ID(),
                                                                                                   invoiceLine.GetM_Product_ID(), invoiceLine.GetM_AttributeSetInstance_ID(), Get_Trx(),
                                                                                                   Util.GetValueOfInt(ds.Tables[0].Rows[0]["M_Warehouse_ID"]), false);
                                                                        if (IsCostUpdation)
                                                                        {
                                                                            invoiceLine.SetPostCurrentCostPrice(currentCostPrice);
                                                                        }
                                                                    }
                                                                }
                                                                //invoiceLine.SetIsCostCalculated(true);
                                                                //if (client.IsCostImmediate() && !invoiceLine.IsCostImmediate())
                                                                {
                                                                    invoiceLine.SetIsCostImmediate(true);
                                                                }
                                                                if (!invoiceLine.Save(Get_Trx()))
                                                                {
                                                                    ValueNamePair pp = VLogger.RetrieveError();
                                                                    _log.Info("Error found for saving Invoice(Vendor) for this Line ID = " + invoiceLine.GetC_InvoiceLine_ID() +
                                                                               " Error Name is " + pp.GetName() + " And Error Type is " + pp.GetType());
                                                                    Get_Trx().Rollback();
                                                                }
                                                                else
                                                                {
                                                                    _log.Fine("Cost Calculation updated for C_InvoiceLine = " + invoiceLine.GetC_InvoiceLine_ID());
                                                                    Get_Trx().Commit();
                                                                }
                                                            }
                                                        }
                                                        #endregion

                                                        #region CRMA
                                                        else if (invoice.IsSOTrx() && invoice.IsReturnTrx())
                                                        {
                                                            if (!MCostQueue.CreateProductCostsDetails(GetCtx(), invoice.GetAD_Client_ID(), invoice.GetAD_Org_ID(), product, invoiceLine.GetM_AttributeSetInstance_ID(),
                                                              "Invoice(Customer)", null, null, null, invoiceLine, null, ProductInvoiceLineCost, invoiceLine.GetQtyInvoiced(),
                                                              Get_Trx(), costingCheck, out conversionNotFoundInvoice, optionalstr: "window"))
                                                            {
                                                                if (!conversionNotFoundInvoice1.Contains(conversionNotFoundInvoice))
                                                                {
                                                                    conversionNotFoundInvoice1 += conversionNotFoundInvoice + " , ";
                                                                }
                                                                _log.Info("Cost not Calculated for Invoice(Customer) for this Line ID = "
                                                                    + invoiceLine.GetC_InvoiceLine_ID() + " , " + (!string.IsNullOrEmpty(costingCheck.errorMessage) ? costingCheck.errorMessage : ""));
                                                            }
                                                            else
                                                            {
                                                                if (invoice.GetDescription() != null && invoice.GetDescription().Contains("{->"))
                                                                {
                                                                    //invoiceLine.SetIsReversedCostCalculated(true);
                                                                }
                                                                if (invoiceLine.GetM_InOutLine_ID() > 0)
                                                                {
                                                                    DataSet ds = DB.ExecuteDataset(@"SELECT M_InOutLine.CurrentCostPrice, M_InOut.M_Warehouse_ID 
                                                                                FROM M_InOutLine INNER JOIN M_InOut ON M_InOut.M_InOut_ID = M_InOutLine.M_InOut_ID
                                                                                WHERE M_InOutLine.M_InOutLIne_ID = " + invoiceLine.GetM_InOutLine_ID(), null, Get_Trx());
                                                                    if (ds != null && ds.Tables.Count > 0 && ds.Tables[0].Rows.Count > 0)
                                                                    {
                                                                        if (IsCostUpdation)
                                                                        {
                                                                            invoiceLine.SetCurrentCostPrice(Util.GetValueOfDecimal(ds.Tables[0].Rows[0]["CurrentCostPrice"]));
                                                                        }

                                                                        currentCostPrice = MCost.GetproductCostAndQtyMaterial(invoiceLine.GetAD_Client_ID(), invoiceLine.GetAD_Org_ID(),
                                                                                                   invoiceLine.GetM_Product_ID(), invoiceLine.GetM_AttributeSetInstance_ID(), Get_Trx(),
                                                                                                   Util.GetValueOfInt(ds.Tables[0].Rows[0]["M_Warehouse_ID"]), false);
                                                                        if (IsCostUpdation)
                                                                        {
                                                                            invoiceLine.SetPostCurrentCostPrice(currentCostPrice);
                                                                        }
                                                                    }
                                                                }
                                                                //invoiceLine.SetIsCostCalculated(true);
                                                                //if (client.IsCostImmediate() && !invoiceLine.IsCostImmediate())
                                                                {
                                                                    invoiceLine.SetIsCostImmediate(true);
                                                                }
                                                                if (!invoiceLine.Save(Get_Trx()))
                                                                {
                                                                    ValueNamePair pp = VLogger.RetrieveError();
                                                                    _log.Info("Error found for saving Invoice(Vendor) for this Line ID = " + invoiceLine.GetC_InvoiceLine_ID() +
                                                                               " Error Name is " + pp.GetName() + " And Error Type is " + pp.GetType());
                                                                    Get_Trx().Rollback();
                                                                }
                                                                else
                                                                {
                                                                    _log.Fine("Cost Calculation updated for C_InvoiceLine = " + invoiceLine.GetC_InvoiceLine_ID());
                                                                    Get_Trx().Commit();
                                                                }
                                                            }
                                                        }
                                                        #endregion

                                                        #region VRMA
                                                        else if (!invoice.IsSOTrx() && invoice.IsReturnTrx())
                                                        {
                                                            // when Ap Credit memo is alone then we will do a impact on costing.
                                                            // this is bcz of giving discount for particular product
                                                            // discount is given only when document type having setting as "Treat As Discount" = True
                                                            MDocType docType = new MDocType(GetCtx(), invoice.GetC_DocTypeTarget_ID(), Get_Trx());
                                                            if (docType.GetDocBaseType() == "APC" && docType.IsTreatAsDiscount() && invoiceLine.GetC_OrderLine_ID() == 0 && invoiceLine.GetM_InOutLine_ID() == 0 && invoiceLine.GetM_Product_ID() > 0)
                                                            {
                                                                if (!MCostQueue.CreateProductCostsDetails(GetCtx(), invoice.GetAD_Client_ID(), invoice.GetAD_Org_ID(), product, invoiceLine.GetM_AttributeSetInstance_ID(),
                                                                  "Invoice(Vendor)", null, null, null, invoiceLine, null, Decimal.Negate(ProductInvoiceLineCost), Decimal.Negate(invoiceLine.GetQtyInvoiced()),
                                                                  Get_Trx(), costingCheck, out conversionNotFoundInvoice, optionalstr: "window"))
                                                                {
                                                                    if (!conversionNotFoundInvoice1.Contains(conversionNotFoundInvoice))
                                                                    {
                                                                        conversionNotFoundInvoice1 += conversionNotFoundInvoice + " , ";
                                                                    }
                                                                    _log.Info("Cost not Calculated for Invoice(Vendor) for this Line ID = "
                                                                        + invoiceLine.GetC_InvoiceLine_ID() + " , " + (!string.IsNullOrEmpty(costingCheck.errorMessage) ? costingCheck.errorMessage : ""));
                                                                }
                                                                else
                                                                {
                                                                    query.Clear();
                                                                    query.Append($@"SELECT NVL(iol.MovementQty, 0) AS MovementQty, iol.M_Locator_ID, io.M_Warehouse_ID FROM C_InvoiceLine il
                                                                        INNER JOIN M_InoutLine iol ON (il.M_InoutLine_ID = iol.M_InoutLine_ID)
                                                                        INNER JOIN M_Inout io ON (io.M_InOut_ID = iol.M_InOut_ID)
                                                                        WHERE il.C_InvoiceLine_ID =  { invoiceLine.Get_ValueAsInt("Ref_InvoiceLineOrg_ID")}");
                                                                    DataSet dsRefInOut = DB.ExecuteDataset(query.ToString(), null, Get_Trx());
                                                                    int M_Locator_ID = 0;
                                                                    int M_Warehouse_ID = invoiceLine.GetM_Warehouse_ID() > 0 ? invoiceLine.GetM_Warehouse_ID() : invoice.GetM_Warehouse_ID();
                                                                    if (dsRefInOut != null && dsRefInOut.Tables.Count > 0 && dsRefInOut.Tables[0].Rows.Count > 0)
                                                                    {
                                                                        M_Locator_ID = Util.GetValueOfInt(dsRefInOut.Tables[0].Rows[0]["M_Locator_ID"]);
                                                                        if (M_Warehouse_ID == 0)
                                                                        {
                                                                            M_Warehouse_ID = Util.GetValueOfInt(dsRefInOut.Tables[0].Rows[0]["M_Warehouse_ID"]);
                                                                        }
                                                                    }


                                                                    if (invoice.GetDescription() != null && invoice.GetDescription().Contains("{->"))
                                                                    {
                                                                        //invoiceLine.SetIsReversedCostCalculated(true);
                                                                    }
                                                                    //invoiceLine.SetIsCostCalculated(true);
                                                                    //if (client.IsCostImmediate() && !invoiceLine.IsCostImmediate())
                                                                    {
                                                                        invoiceLine.SetIsCostImmediate(true);
                                                                    }
                                                                    if (!invoiceLine.Save(Get_Trx()))
                                                                    {
                                                                        ValueNamePair pp = VLogger.RetrieveError();
                                                                        _log.Info("Error found for saving Invoice(Vendor) for this Line ID = " + invoiceLine.GetC_InvoiceLine_ID() +
                                                                                   " Error Name is " + pp.GetName() + " And Error Type is " + pp.GetType());
                                                                        Get_Trx().Rollback();
                                                                    }
                                                                    else
                                                                    {
                                                                        _log.Fine("Cost Calculation updated for C_InvoiceLine = " + invoiceLine.GetC_InvoiceLine_ID());
                                                                        Get_Trx().Commit();

                                                                        // get cost from Product Cost after cost calculation, and update on Product Transaction against Invoice
                                                                        currentCostPrice = MCost.GetproductCosts(GetAD_Client_ID(), GetAD_Org_ID(),
                                                                                           product.GetM_Product_ID(), invoiceLine.GetM_AttributeSetInstance_ID(), Get_Trx(), M_Warehouse_ID);
                                                                        UpdateTransactionCostForInvoice(currentCostPrice, invoiceLine.GetC_InvoiceLine_ID(), costingCheck);
                                                                        Get_Trx().Commit();
                                                                    }
                                                                }
                                                            }
                                                        }
                                                        #endregion
                                                    }
                                                    #endregion
                                                }
                                            }
                                            catch { }
                                        }
                                    }
                                    //sql.Clear();
                                    //if (invoice.GetDescription() != null && invoice.GetDescription().Contains("{->"))
                                    //{
                                    //    sql.Append("SELECT COUNT(*) FROM C_InvoiceLine WHERE IsReversedCostCalculated = 'N' AND IsActive = 'Y' AND C_Invoice_ID = " + invoice.GetC_Invoice_ID());
                                    //}
                                    //else
                                    //{
                                    //    sql.Append("SELECT COUNT(*) FROM C_InvoiceLine WHERE IsCostCalculated = 'N' AND IsActive = 'Y' AND C_Invoice_ID = " + invoice.GetC_Invoice_ID());
                                    //}
                                    //if (Util.GetValueOfInt(DB.ExecuteScalar(sql.ToString(), null, Get_Trx())) <= 0)
                                    //{
                                    //    if (invoice.GetDescription() != null && invoice.GetDescription().Contains("{->"))
                                    //    {
                                    //        invoice.SetIsReversedCostCalculated(true);
                                    //    }
                                    //    invoice.SetIsCostCalculated(true);
                                    //    if (!invoice.Save(Get_Trx()))
                                    //    {
                                    //        ValueNamePair pp = VLogger.RetrieveError();
                                    //        _log.Info("Error found for saving C_Invoice for this Record ID = " + invoice.GetC_Invoice_ID() +
                                    //                   " Error Name is " + pp.GetName() + " And Error Type is " + pp.GetType());
                                    //    }
                                    //    else
                                    //    {
                                    //        _log.Fine("Cost Calculation updated for C_Invoice = " + invoice.GetC_Invoice_ID());
                                    //        Get_Trx().Commit();
                                    //    }
                                    //}
                                    continue;
                                }
                            }
                            catch { }
                            #endregion

                            #region Cost Calculation For Material Receipt --
                            try
                            {
                                if (Util.GetValueOfString(dsRecord.Tables[0].Rows[z]["TableName"]) == "M_InOut" &&
                                   Util.GetValueOfString(dsRecord.Tables[0].Rows[z]["issotrx"]) == "N" &&
                                   Util.GetValueOfString(dsRecord.Tables[0].Rows[z]["isreturntrx"]) == "N" &&
                                   Util.GetValueOfString(dsRecord.Tables[0].Rows[z]["docstatus"]) == "RE")
                                {

                                    CalculateCostForMaterialReversal(Util.GetValueOfInt(dsRecord.Tables[0].Rows[z]["Record_Id"]));

                                    CalculateCostForPurchaseInvoice(Util.GetValueOfInt(dsRecord.Tables[0].Rows[z]["Record_Id"]));

                                    continue;
                                }
                            }
                            catch { }
                            #endregion

                        }
                    }
                }

                // Re-Calculate Cost of those line whose costing not calculate 
                if (ListReCalculatedRecords != null && ListReCalculatedRecords.Count > 0)
                {
                    ReVerfyAndCalculateCost(ListReCalculatedRecords);
                }
            }
            catch (Exception ex)
            {
                _log.Info("Error Occured during costing " + ex.ToString());
                if (dsRecord != null)
                    dsRecord.Dispose();
                if (dsChildRecord != null)
                    dsChildRecord.Dispose();
            }
            finally
            {
                if (!string.IsNullOrEmpty(conversionNotFoundInOut1))
                {
                    conversionNotFoundInOut = Msg.GetMsg(GetCtx(), "ConvNotForMinout") + conversionNotFoundInOut1;
                }
                if (!string.IsNullOrEmpty(conversionNotFoundInvoice1))
                {
                    conversionNotFoundInvoice = Msg.GetMsg(GetCtx(), "ConvNotForInvoice") + conversionNotFoundInvoice1;
                }
                if (!string.IsNullOrEmpty(conversionNotFoundInventory1))
                {
                    conversionNotFoundInventory = Msg.GetMsg(GetCtx(), "ConvNotForInventry") + conversionNotFoundInventory1;
                }
                if (!string.IsNullOrEmpty(conversionNotFoundMovement1))
                {
                    conversionNotFoundMovement = Msg.GetMsg(GetCtx(), "ConvNotForMove") + conversionNotFoundMovement1;
                }
                if (!string.IsNullOrEmpty(conversionNotFoundProductionExecution1))
                {
                    conversionNotFoundProductionExecution = Msg.GetMsg(GetCtx(), "ConvNotForProduction") + conversionNotFoundProductionExecution1;
                }

                conversionNotFound = conversionNotFoundInOut + "\n" + conversionNotFoundInvoice + "\n" +
                                     conversionNotFoundInventory + "\n" + conversionNotFoundMovement + "\n" +
                                     conversionNotFoundProductionExecution;

                if (dsRecord != null)
                    dsRecord.Dispose();
                if (dsChildRecord != null)
                    dsChildRecord.Dispose();
                _log.Info("Successfully Ended Cost Calculation ");
            }
            return conversionNotFound;
        }

        /// <summary>
        /// This function i sused to get the Invoice which are linked with GRN 
        /// </summary>
        /// <param name="M_InOut_ID">GRN ID</param>
        /// <returns>Dataset of Invoiced linked</returns>
        private DataSet GetInvoices(int M_InOut_ID)
        {
            var pc = "(SELECT M_Product_ID FROM M_Product WHERE M_Product_Category_ID IN (" + productCategoryID + " ) )";
            sqlInvoice.Clear();
            sqlInvoice.Append(@"SELECT * FROM ( ");
            sqlInvoice.Append($@"SELECT ci.ad_client_id , ci.ad_org_id , ci.isactive ,");
            sqlInvoice.Append(@" COALESCE(TO_CHAR(io.created, 'DD-MON-YY HH24:MI:SS'), TO_CHAR(ci.created, 'DD-MON-YY HH24:MI:SS')) AS created,");
            sqlInvoice.Append($@" ci.createdby ,  
                                to_char(ci.updated, 'DD-MON-YY HH24:MI:SS') as updated , ci.updatedby ,
                                ci.documentno , ci.C_Invoice_id AS Record_Id , ci.issotrx , ci.isreturntrx , '' AS IsInternalUse, 'C_Invoice' AS TableName,
                                ci.docstatus, ci.DateAcct AS DateAcct, ci.iscostcalculated , ci.isreversedcostcalculated, io.M_InOut_ID 
                         FROM C_Invoice ci ");
            sqlInvoice.Append($@"LEFT JOIN 
                           (SELECT DISTINCT io.created, il.C_Invoice_ID, io.M_InOut_ID 
                            FROM M_Inout io
                            INNER JOIN M_InoutLine iil ON (io.M_Inout_ID = iil.M_Inout_ID)
                            INNER JOIN C_InvoiceLine il ON (il.M_InoutLine_ID = iil.M_InoutLine_ID)
                            WHERE io.dateacct = {GlobalVariable.TO_DATE(minDateRecord, true)} AND io.M_InOut_ID = {M_InOut_ID}
                              AND iil.IsActive = 'Y'
                              AND il.iscostcalculated = 'N'
                              AND il.IsCostImmediate = 'N'
                              AND il.M_Product_ID IN ( " + ((!String.IsNullOrEmpty(productCategoryID) && String.IsNullOrEmpty(productID)) ? pc : productID) + @" )
                           ) io ON ci.C_Invoice_ID = io.C_Invoice_ID");
            sqlInvoice.Append(@" WHERE ci.IsSOTrx = 'N' AND (ci.dateacct = " + GlobalVariable.TO_DATE(minDateRecord, true));
            // Get invoice which are linked with GRN / DO
            sqlInvoice.Append(@" OR 
                                ci.C_Invoice_ID IN (SELECT il.C_Invoice_ID FROM M_InoutLine iil 
                                    INNER JOIN M_InOut io ON (iil.M_Inout_ID = io.M_Inout_ID)
                                    INNER JOIN C_InvoiceLine il ON (il.M_InoutLine_ID = iil.M_InoutLine_ID)
                                    WHERE io.dateacct = " + GlobalVariable.TO_DATE(minDateRecord, true) +
                                    $@" AND io.M_InOut_ID = {M_InOut_ID} AND iil.IsActive = 'Y' AND il.iscostcalculated = 'N' AND il.IsCostImmediate = 'N' 
                                    AND il.M_Product_ID IN ( " + ((!String.IsNullOrEmpty(productCategoryID) && String.IsNullOrEmpty(productID)) ? pc : productID) + @" ) ) ");

            // Treat as Discount record
            sqlInvoice.Append(@" OR 
                                ci.C_Invoice_ID IN (SELECT tad.C_Invoice_ID FROM C_InvoiceLine tad 
                                    INNER JOIN C_InvoiceLine il ON (tad.Ref_InvoiceLineOrg_ID = il.C_InvoiceLine_ID) 
                                    INNER JOIN M_InoutLine iil ON (il.M_InoutLine_ID = iil.M_InoutLine_ID)
                                    INNER JOIN M_InOut io ON (iil.M_Inout_ID = io.M_Inout_ID)                                   
                                    WHERE io.dateacct = " + GlobalVariable.TO_DATE(minDateRecord, true) +
                                    $@" AND io.M_InOut_ID = {M_InOut_ID} AND tad.IsActive = 'Y' AND tad.iscostcalculated = 'N' AND tad.IsCostImmediate = 'N' 
                                    AND tad.M_Product_ID IN ( " + ((!String.IsNullOrEmpty(productCategoryID) && String.IsNullOrEmpty(productID)) ? pc : productID) + @" ) ) ");
            // Treat as Discount record where on Original Invoice not linked with GRN Line
            sqlInvoice.Append(@" OR 
                                ci.C_Invoice_ID IN (SELECT tad.C_Invoice_ID FROM C_InvoiceLine tad 
                                    INNER JOIN C_InvoiceLine il ON (tad.Ref_InvoiceLineOrg_ID = il.C_InvoiceLine_ID) 
                                    INNER JOIN M_InoutLine iil ON (il.M_InoutLine_ID = iil.M_InoutLine_ID)
                                    INNER JOIN M_InOut io ON (iil.M_Inout_ID = io.M_Inout_ID)                                   
                                    WHERE io.dateacct = " + GlobalVariable.TO_DATE(minDateRecord, true) +
                                    $@" AND io.M_InOut_ID = {M_InOut_ID} AND tad.IsActive = 'Y' AND tad.iscostcalculated = 'N' AND tad.IsCostImmediate = 'N' 
                                    AND tad.M_Product_ID IN ( " + ((!String.IsNullOrEmpty(productCategoryID) && String.IsNullOrEmpty(productID)) ? pc : productID) + @" ) ) ");
            sqlInvoice.Append(@" ) AND ci.isactive = 'Y'
                              AND ((ci.docstatus IN ('CO' , 'CL') AND ci.iscostcalculated = 'N' ) OR (ci.docstatus  IN ('RE') AND ci.iscostcalculated = 'Y'
                              AND ci.ISREVERSEDCOSTCALCULATED= 'N' AND ci.description LIKE '%{->%')) 
                         UNION 
                         SELECT i.ad_client_id ,  i.ad_org_id , i.isactive ,to_char(i.created, 'DD-MON-YY HH24:MI:SS') as  created ,  i.createdby ,  TO_CHAR(mi.updated, 'DD-MON-YY HH24:MI:SS') AS updated ,
                                i.updatedby ,  mi.documentno ,  M_MatchInv_Id AS Record_Id ,  i.issotrx ,  i.isreturntrx ,  ''           AS IsInternalUse,  'M_MatchInv' AS TableName,
                                i.docstatus,i.DateAcct AS DateAcct,  mi.iscostcalculated ,  i.isreversedcostcalculated, iol.M_Inout_ID 
                         FROM M_MatchInv mi 
                              INNER JOIN c_invoiceline il ON (il.c_invoiceline_id = mi.c_invoiceline_id)
                              INNER JOIN C_Invoice i ON (i.c_invoice_id = il.c_invoice_id)
                              INNER JOIN M_InoutLine iol ON (iol.M_InoutLine_ID = mi.M_InoutLine_ID)
                              WHERE i.IsSOTrx = 'N' AND mi.M_Product_ID IN ( " + ((!String.IsNullOrEmpty(productCategoryID) && String.IsNullOrEmpty(productID)) ? pc : productID) + @" )
                              AND (mi.dateacct = " + GlobalVariable.TO_DATE(minDateRecord, true));
            // When Match invoice cost is calculating on GRN Date
            sqlInvoice.Append(@" OR 
                                mi.M_InOutLine_ID IN (SELECT iil.M_InoutLine_ID FROM M_InoutLine iil INNER JOIN M_InOut io ON (iil.M_Inout_ID = io.M_Inout_ID) 
                                WHERE io.dateacct = " + GlobalVariable.TO_DATE(minDateRecord, true) +
                                $@" AND io.M_InOut_ID = {M_InOut_ID} AND iil.IsActive = 'Y'  
                                AND mi.M_Product_ID IN ( " + ((!String.IsNullOrEmpty(productCategoryID) && String.IsNullOrEmpty(productID)) ? pc : productID) + @" ) 
                                AND iil.M_Product_ID IN ( " + ((!String.IsNullOrEmpty(productCategoryID) && String.IsNullOrEmpty(productID)) ? pc : productID) + @" ) ) ");
            sqlInvoice.Append(@" ) AND i.isactive = 'Y' AND i.docstatus IN ('CO' , 'CL') AND mi.iscostcalculated = 'N' AND mi.iscostImmediate = 'N' ");
            sqlInvoice.Append($@"  UNION
                         SELECT DISTINCT il.ad_client_id ,il.ad_org_id ,il.isactive ,to_char(LCA.created, 'DD-MON-YY HH24:MI:SS') as created ,   i.createdby , TO_CHAR(i.updated, 'DD-MON-YY HH24:MI:SS') AS updated ,
                              i.updatedby , I.DOCUMENTNO , LCA.C_LANDEDCOSTALLOCATION_ID AS RECORD_ID ,   '' AS ISSOTRX , '' AS ISRETURNTRX , '' AS ISINTERNALUSE,
                              'LandedCost' as TABLENAME,  I.DOCSTATUS, i.DATEACCT as DATEACCT , LCA.ISCOSTCALCULATED , 'N' as ISREVERSEDCOSTCALCULATED, iol.M_Inout_ID 
                        FROM C_LANDEDCOSTALLOCATION LCA 
                        INNER JOIN C_INVOICELINE IL ON (IL.C_INVOICELINE_ID = LCA.C_INVOICELINE_ID)
                        INNER JOIN c_invoice i ON (I.C_INVOICE_ID = IL.C_INVOICE_ID)
                        INNER JOIN M_InOutLine iol ON (iol.M_InOutLine_ID = LCA.M_InOutLine_ID)
                        INNER JOIN M_InOut io ON (io.M_InOut_ID = iol.M_InOut_ID)
                        WHERE io.M_InOut_ID = {M_InOut_ID} AND io.dateacct = " + GlobalVariable.TO_DATE(minDateRecord, true) + @" AND il.isactive = 'Y' AND 
                        LCA.M_PRODUCT_ID IN ( " + ((!String.IsNullOrEmpty(productCategoryID) && String.IsNullOrEmpty(productID)) ? pc : productID) + @" ) 
                        AND lca.iscostcalculated = 'N' AND I.DOCSTATUS IN ('CO' , 'CL', 'RE', 'VO') AND I.ISSOTRX = 'N' AND I.ISRETURNTRX = 'N' ");
            sqlInvoice.Append(@")t ORDER BY dateacct, to_date(created, 'DD-MON-YY HH24:MI:SS') ");
            dsInvoice = DB.ExecuteDataset(sqlInvoice.ToString(), null, Get_Trx());
            return dsInvoice;
        }

        /// <summary>
        /// This function is used to get the data from Clost Closing Table
        /// Reason: When user want to run Re-costing Calculation from the specific Date then system will pick the Closing Cost and start costing calculation from that date only.
        /// </summary>
        /// <returns>Message, when Closing Cost not found</returns>
        public string GetClosingInfo()
        {
            sql.Clear();
            sql.Append($@"SELECT MAX(Created) FROM M_CostClosing 
                            WHERE TRUNC(Created) < {GlobalVariable.TO_DATE(DateFrom.Value, true)} ");
            if (!string.IsNullOrEmpty(productID))
            {
                sql.Append($@" AND M_Product_ID IN ({productID})");
            }
            else if (!string.IsNullOrEmpty(productCategoryID))
            {
                sql.Append($@" AND M_Product_ID IN (SELECT M_Product_ID FROM M_Product WHERE M_Product_Category_ID IN ({productCategoryID} ) )");
            }
            DateTime? LastClosingDate = Util.GetValueOfDateTime(DB.ExecuteScalar(sql.ToString(), null, Get_Trx()));
            if (LastClosingDate == null)
            {
                return Msg.GetMsg(GetCtx(), "VAS_CostClosingNotFound");
            }
            else
            {
                DateFrom = LastClosingDate.Value.AddDays(1);
            }
            return "";
        }

        public string InsertClosingCostonCost()
        {
            sql.Clear();
            sql.Append($@"INSERT INTO M_COST(
                M_Cost_ID, AD_CLIENT_ID, AD_ORG_ID, C_ACCTSCHEMA_ID, CREATED, CREATEDBY, CUMULATEDAMT, CUMULATEDQTY, CURRENTCOSTPRICE, CURRENTQTY,
                DESCRIPTION, FUTURECOSTPRICE, ISACTIVE, M_ATTRIBUTESETINSTANCE_ID, M_COSTELEMENT_ID, M_COSTTYPE_ID, M_PRODUCT_ID, PERCENTCOST, UPDATED,
                UPDATEDBY, BASISTYPE, ISTHISLEVEL, ISUSERDEFINED, LASTCOSTPRICE,A_ASSET_ID, ISASSETCOST, M_WAREHOUSE_ID
                )
            SELECT
                M_COSTClosing_ID, AD_CLIENT_ID, AD_ORG_ID, C_ACCTSCHEMA_ID, Current_Date, {GetAD_User_ID()}, CUMULATEDAMT, CUMULATEDQTY, CURRENTCOSTPRICE, CURRENTQTY, 
                DESCRIPTION, FUTURECOSTPRICE, ISACTIVE, M_ATTRIBUTESETINSTANCE_ID, M_COSTELEMENT_ID, M_COSTTYPE_ID, M_PRODUCT_ID, PERCENTCOST, Current_Date, 
                {GetAD_User_ID()}, BASISTYPE, ISTHISLEVEL, ISUSERDEFINED, LASTCOSTPRICE,
                CASE WHEN NVL(A_ASSET_ID , 0) = 0 THEN 0 ELSE A_ASSET_ID END AS A_ASSET_ID, ISASSETCOST, 
                CASE WHEN NVL(M_WAREHOUSE_ID , 0) = 0 THEN 0 ELSE M_WAREHOUSE_ID END M_WAREHOUSE_ID           
            FROM M_COSTClosing ");
            sql.Append($@" WHERE TRUNC(Created) = {GlobalVariable.TO_DATE(DateFrom.Value.AddDays(-1), true)} ");
            if (!string.IsNullOrEmpty(productID))
            {
                sql.Append($@" AND M_Product_ID IN ({productID})");
            }
            else if (!string.IsNullOrEmpty(productCategoryID))
            {
                sql.Append($@" AND M_Product_ID IN (SELECT M_Product_ID FROM M_Product WHERE M_Product_Category_ID IN ({productCategoryID} ) )");
            }
            int no = DB.ExecuteQuery(sql.ToString(), null, Get_Trx());
            if (no <= 0)
            {
                return Msg.GetMsg(GetCtx(), "VAS_CostClosingNotInserted");
            }
            return "";
        }

        /// <summary>
        ///  Get min date for re-costing calculation
        /// </summary>
        /// <param name="count">Manufacturing Module is updated or not</param>
        /// <param name="M_Product_ID">Minimum date of selected product from Transaction</param>
        /// <returns>DateTime -- form which date cost calculation process to be started </returns>
        public DateTime? SerachMinDate(int count, String M_Product_ID)
        {
            DateTime? minDate;
            //DateTime? tempDate;
            try
            {
                sql.Clear();
                sql.Append(@"SELECT Min(MovementDate) FROM M_Transaction WHERE IsActive = 'Y' AND AD_Client_ID = " + GetCtx().GetAD_Client_ID());
                if (!String.IsNullOrEmpty(M_Product_ID))
                {
                    sql.Append(@" AND M_Product_ID IN ( " + M_Product_ID + ")");
                }
                if (M_AttributeSetInstance_ID > 0)
                {
                    sql.Append(@" AND M_AttributesetInstance_ID = " + M_AttributeSetInstance_ID);
                }
                if (DateFrom != null)
                {
                    sql.Append($" AND trunc(movementdate) >= {GlobalVariable.TO_DATE(DateFrom, true)}");
                }
                minDate = Util.GetValueOfDateTime(DB.ExecuteScalar(sql.ToString(), null, Get_Trx()));
            }
            catch (Exception ex)
            {
                throw ex;
            }
            return minDate;
        }

        /// <summary>
        /// we will update IsCostCalculation / IsReversedCostCalculation / IsCostImmediate on the Tansaction
        /// we will delete records from the Product Costs 
        /// </summary>
        /// <param name="count">Manufacturing Module is updated or not</param>
        private void UpdateAndDeleteCostImpacts(int count)
        {
            int countRecord = 0;

            if (!String.IsNullOrEmpty(productCategoryID) && String.IsNullOrEmpty(productID))
            {
                #region when we select product Category only
                // for M_Inout / M_Inoutline
                sql.Clear();
                sql.Append($@"UPDATE m_inoutline SET  CurrentCostPrice = 0 , PostCurrentCostPrice = 0 ");
                sql.Append(@", iscostimmediate = 'N' , iscostcalculated = 'N',  isreversedcostcalculated = 'N'");
                sql.Append($@" WHERE M_Product_ID IN 
                                (SELECT M_Product_ID FROM M_Product WHERE M_Product_Category_ID IN (" + productCategoryID + " ) )");
                if (DateFrom != null)
                {
                    sql.Append($@" AND M_InOut_id IN (SELECT m.M_InOut_id FROM M_InOut m WHERE trunc(m.MovementDate) >= {GlobalVariable.TO_DATE(DateFrom, true)})");
                }
                if (M_AttributeSetInstance_ID > 0)
                {
                    sql.Append($@" AND M_AttributeSetInstance_ID = {M_AttributeSetInstance_ID}");
                }
                countRecord = DB.ExecuteQuery(sql.ToString(), null, Get_Trx());

                if (countRecord > 0)
                {
                    sql.Clear();
                    sql.Append($@"UPDATE M_Inout  SET  iscostcalculated = 'N',  isreversedcostcalculated = 'N' ");
                    sql.Append(@" , Posted ='N' ");
                    sql.Append($@" WHERE M_Inout_ID IN ( 
                                   SELECT M_Inout_ID FROM m_inoutline WHERE  M_Product_ID IN 
                                    (SELECT M_Product_ID FROM M_Product WHERE M_Product_Category_ID IN ({ productCategoryID}) ) )");
                    if (DateFrom != null)
                    {
                        sql.Append($@" AND trunc(MovementDate) >= {GlobalVariable.TO_DATE(DateFrom, true)} ");
                    }
                    countRecord = DB.ExecuteQuery(sql.ToString(), null, Get_Trx());

                    DeleteFactDetails(MInOut.Table_ID, MInOut.Table_Name);
                }

                // for C_Invoice / C_InvoiceLine
                countRecord = 0;
                sql.Clear();
                sql.Append($@"UPDATE C_Invoiceline SET  TotalInventoryAdjustment = 0,TotalCogsAdjustment = 0, PostCurrentCostPrice = 0");
                sql.Append(", iscostimmediate = 'N' , iscostcalculated = 'N',  isreversedcostcalculated = 'N' ");
                sql.Append($@" WHERE M_Product_ID IN (SELECT M_Product_ID FROM M_Product WHERE M_Product_Category_ID IN (" + productCategoryID + " ) )");
                if (DateFrom != null)
                {
                    sql.Append($@" AND C_Invoice_ID IN (SELECT m.C_Invoice_ID FROM C_Invoice m WHERE trunc(m.DateAcct) >= {GlobalVariable.TO_DATE(DateFrom, true)})");
                }
                if (M_AttributeSetInstance_ID > 0)
                {
                    sql.Append($@" AND M_AttributeSetInstance_ID = {M_AttributeSetInstance_ID} ");
                }
                countRecord = DB.ExecuteQuery(sql.ToString(), null, Get_Trx());
                if (countRecord > 0)
                {
                    sql.Clear();
                    sql.Append($@"UPDATE C_Invoice  SET  iscostcalculated = 'N',  isreversedcostcalculated = 'N' ");
                    sql.Append(@" , Posted ='N' ");
                    sql.Append($@" WHERE C_Invoice_ID IN ( 
                                        SELECT C_Invoice_ID FROM C_Invoiceline WHERE  M_Product_ID IN 
                                        (SELECT M_Product_ID FROM M_Product WHERE M_Product_Category_ID IN ({ productCategoryID } ) ) )");
                    if (DateFrom != null)
                    {
                        sql.Append($@" AND trunc(DateAcct) >= {GlobalVariable.TO_DATE(DateFrom, true)} ");
                    }
                    countRecord = DB.ExecuteQuery(sql.ToString(), null, Get_Trx());

                    DeleteFactDetails(MInvoice.Table_ID, MInvoice.Table_Name);
                }

                // for C_ProvisonalInvoice / C_ProvisionalInvoiceLine
                countRecord = 0;
                sql.Clear();
                sql.Append($@"UPDATE c_Provisionalinvoiceline SET iscostimmediate = 'N' , iscostcalculated = 'N',  isreversedcostcalculated = 'N' ");
                sql.Append($@" WHERE M_Product_ID IN (SELECT M_Product_ID FROM M_Product WHERE M_Product_Category_ID IN (" + productCategoryID + " ) ) ");
                if (DateFrom != null)
                {
                    sql.Append($@" AND C_ProvisionalInvoice_ID IN (SELECT m.C_ProvisionalInvoice_ID FROM C_ProvisionalInvoice m WHERE trunc(m.DateAcct) >= {GlobalVariable.TO_DATE(DateFrom, true)})");
                }
                if (M_AttributeSetInstance_ID > 0)
                {
                    sql.Append($@" AND M_AttributeSetInstance_ID = {M_AttributeSetInstance_ID} ");
                }
                countRecord = DB.ExecuteQuery(sql.ToString(), null, Get_Trx());
                if (countRecord > 0)
                {
                    sql.Clear();
                    sql.Append(@"UPDATE C_ProvisionalInvoice  SET  iscostcalculated = 'N',  isreversedcostcalculated = 'N'");
                    sql.Append(@" , Posted ='N' ");
                    sql.Append($@" WHERE C_ProvisionalInvoice_ID IN ( 
                                        SELECT C_ProvisionalInvoice_ID FROM C_ProvisionalInvoiceline WHERE  M_Product_ID IN 
                                        (SELECT M_Product_ID FROM M_Product WHERE M_Product_Category_ID IN (" + productCategoryID + " ) ) )");
                    if (DateFrom != null)
                    {
                        sql.Append($@" AND trunc(DateAcct) >= {GlobalVariable.TO_DATE(DateFrom, true)} ");
                    }
                    countRecord = DB.ExecuteQuery(sql.ToString(), null, Get_Trx());

                    DeleteFactDetails(MProvisionalInvoice.Table_ID, MProvisionalInvoice.Table_Name);

                }

                // For Landed Cost Allocation
                countRecord = 0;
                sql.Clear();
                sql.Append($@"UPDATE C_LANDEDCOSTALLOCATION SET  iscostcalculated = 'N' ");
                sql.Append($@"WHERE M_Product_ID IN (SELECT M_Product_ID FROM M_Product WHERE M_Product_Category_ID IN ({ productCategoryID } ) )");
                if (DateFrom != null)
                {
                    sql.Append($@" AND C_InvoiceLine_ID IN (SELECT C_InvoiceLine_ID FROM C_Invoice i INNER JOIN C_InvoiceLine il ON (i.C_Invoice_ID = il.C_Invoice_ID) 
                                WHERE TRUNC(i.DateAcct) >= {GlobalVariable.TO_DATE(DateFrom, true)})");
                }
                if (M_AttributeSetInstance_ID > 0)
                {
                    sql.Append($@" AND M_AttributeSetInstance_ID = {M_AttributeSetInstance_ID} ");
                }
                countRecord = DB.ExecuteQuery(sql.ToString(), null, Get_Trx());

                // expected landed cost
                //countRecord = 0;
                //countRecord = DB.ExecuteQuery(@"UPDATE C_Expectedcostdistribution Set Iscostcalculated  = 'N'
                //                                    Where C_Orderline_Id In (Select C_Orderline_Id
                //                                      FROM C_OrderLine WHERE M_Product_Id IN 
                //                                      ((SELECT M_Product_Id FROM M_Product WHERE M_Product_Category_Id IN (" + productCategoryID + "))))", null, Get_Trx());

                // for M_Inventory / M_InventoryLine
                countRecord = 0;
                sql.Clear();
                sql.Append($@"UPDATE m_inventoryline SET  CurrentCostPrice = 0 , PostCurrentCostPrice = 0 ");
                sql.Append($@", iscostimmediate = 'N' , iscostcalculated = 'N',  isreversedcostcalculated = 'N' ");
                sql.Append($@" WHERE M_Product_ID IN (SELECT M_Product_ID FROM M_Product WHERE M_Product_Category_ID IN (" + productCategoryID + " ) )");
                if (DateFrom != null)
                {
                    sql.Append($@" AND M_Inventory_ID IN (SELECT m.M_Inventory_ID FROM M_Inventory m WHERE trunc(m.MovementDate) >= {GlobalVariable.TO_DATE(DateFrom, true)})");
                }
                if (M_AttributeSetInstance_ID > 0)
                {
                    sql.Append($@" AND M_AttributeSetInstance_ID = {M_AttributeSetInstance_ID} ");
                }
                countRecord = DB.ExecuteQuery(sql.ToString(), null, Get_Trx());
                if (countRecord > 0)
                {
                    sql.Clear();
                    sql.Append($@"UPDATE m_inventory  SET  iscostcalculated = 'N',  isreversedcostcalculated = 'N'");
                    sql.Append(@" , Posted ='N' ");
                    sql.Append($@" WHERE m_inventory_ID IN ( 
                                        SELECT m_inventory_ID FROM m_inventoryline WHERE  M_Product_ID IN 
                                        (SELECT M_Product_ID FROM M_Product WHERE M_Product_Category_ID IN (" + productCategoryID + " ) ) )");
                    if (DateFrom != null)
                    {
                        sql.Append($@" AND trunc(MovementDate) >= {GlobalVariable.TO_DATE(DateFrom, true)} ");
                    }
                    countRecord = DB.ExecuteQuery(sql.ToString(), null, Get_Trx());

                    DeleteFactDetails(MInventory.Table_ID, MInventory.Table_Name);

                }

                // for VAFAM_AssetDisposal
                sql.Clear();
                sql.Append($@"UPDATE VAFAM_AssetDisposal SET iscostimmediate = 'N' , iscostcalculated = 'N',  isreversedcostcalculated = 'N' ");
                sql.Append(@" , Posted ='N' ");
                sql.Append($@"WHERE M_Product_ID IN (SELECT M_Product_ID FROM M_Product WHERE M_Product_Category_ID IN ({ productCategoryID } ) )");
                if (DateFrom != null)
                {
                    sql.Append($@" AND trunc(DateAcct) >= {GlobalVariable.TO_DATE(DateFrom, true)} ");
                }
                if (M_AttributeSetInstance_ID > 0)
                {
                    sql.Append($@" AND M_AttributeSetInstance_ID = {M_AttributeSetInstance_ID} ");
                }
                countRecord = DB.ExecuteQuery(sql.ToString(), null, Get_Trx());


                DeleteFactDetails(MTable.Get_Table_ID("VAFAM_AssetDisposal"), "VAFAM_AssetDisposal");


                // for M_Movement / M_MovementLine
                countRecord = 0;
                sql.Clear();
                sql.Append($@"UPDATE m_movementline SET  CurrentCostPrice = 0 , PostCurrentCostPrice = 0 , ToCurrentCostPrice = 0 , ToPostCurrentCostPrice = 0");
                sql.Append(@", iscostimmediate = 'N' , iscostcalculated = 'N',  isreversedcostcalculated = 'N' ");
                sql.Append($@" WHERE M_Product_ID IN (SELECT M_Product_ID FROM M_Product WHERE M_Product_Category_ID IN ({ productCategoryID } ) )");
                if (DateFrom != null)
                {
                    sql.Append($@" AND M_Movement_ID IN (SELECT m.M_Movement_ID FROM M_Movement m WHERE trunc(m.MovementDate) >= {GlobalVariable.TO_DATE(DateFrom, true)})");
                }
                if (M_AttributeSetInstance_ID > 0)
                {
                    sql.Append($@" AND M_AttributeSetInstance_ID = {M_AttributeSetInstance_ID} ");
                }
                countRecord = DB.ExecuteQuery(sql.ToString(), null, Get_Trx());
                if (countRecord > 0)
                {
                    sql.Clear();
                    sql.Append($@"UPDATE m_movement  SET  iscostcalculated = 'N',  isreversedcostcalculated = 'N'");
                    sql.Append(@" , Posted ='N' ");
                    sql.Append($@" WHERE m_movement_ID IN ( 
                                        SELECT m_movement_ID FROM m_movementline WHERE  M_Product_ID IN 
                                        (SELECT M_Product_ID FROM M_Product WHERE M_Product_Category_ID IN ({ productCategoryID } ) ) )");
                    if (DateFrom != null)
                    {
                        sql.Append($@" AND trunc(MovementDate) >= {GlobalVariable.TO_DATE(DateFrom, true)} ");
                    }
                    countRecord = DB.ExecuteQuery(sql.ToString(), null, Get_Trx());

                    DeleteFactDetails(MMovement.Table_ID, MMovement.Table_Name);
                }

                // for M_Production / M_ProductionLine
                countRecord = 0;
                sql.Clear();
                sql.Append($@"UPDATE m_productionline SET  Amt = 0");
                sql.Append(", IsCostImmediate = 'N'");
                sql.Append($@" WHERE NVL(C_Charge_ID, 0) = 0 AND M_Product_ID IN (SELECT M_Product_ID FROM M_Product WHERE M_Product_Category_ID IN ({ productCategoryID} ) )");
                if (DateFrom != null)
                {
                    sql.Append($@" AND M_Production_ID IN (SELECT m.M_Production_ID FROM M_Production m WHERE trunc(m.MovementDate) >= {GlobalVariable.TO_DATE(DateFrom, true)})");
                }
                if (M_AttributeSetInstance_ID > 0)
                {
                    sql.Append($@" AND M_AttributeSetInstance_ID = {M_AttributeSetInstance_ID} ");
                }
                countRecord = DB.ExecuteQuery(sql.ToString(), null, Get_Trx());
                if (countRecord > 0)
                {
                    sql.Clear();
                    sql.Append($@"UPDATE M_Production  SET  iscostcalculated = 'N',  isreversedcostcalculated = 'N' ");
                    sql.Append(@" , Posted ='N' ");
                    sql.Append($@"WHERE M_Production_ID IN ( 
                                        SELECT M_Production_ID FROM M_Productionline WHERE  M_Product_ID IN 
                                        (SELECT M_Product_ID FROM M_Product WHERE M_Product_Category_ID IN ({ productCategoryID } ) ) )");
                    if (DateFrom != null)
                    {
                        sql.Append($@" AND trunc(MovementDate) >= {GlobalVariable.TO_DATE(DateFrom, true)}");
                    }
                    countRecord = DB.ExecuteQuery(sql.ToString(), null, Get_Trx());

                    DeleteFactDetails(X_M_Production.Table_ID, X_M_Production.Table_Name);
                }

                // for M_MatchInv
                countRecord = 0;
                sql.Clear();
                sql.Append($@"UPDATE M_MatchInv SET  CurrentCostPrice = 0 , PostCurrentCostPrice = 0 ");
                sql.Append(@", iscostimmediate = 'N' , iscostcalculated = 'N' ");
                sql.Append(@" , Posted ='N' ");
                sql.Append($@" WHERE M_Product_ID IN (SELECT M_Product_ID FROM M_Product WHERE M_Product_Category_ID IN (" + productCategoryID + " ) )");
                if (DateFrom != null)
                {
                    sql.Append($@" AND trunc(DateAcct) >= {GlobalVariable.TO_DATE(DateFrom, true)} ");
                }
                if (M_AttributeSetInstance_ID > 0)
                {
                    sql.Append($@" AND M_AttributeSetInstance_ID = {M_AttributeSetInstance_ID} ");
                }
                countRecord = DB.ExecuteQuery(sql.ToString(), null, Get_Trx());

                DeleteFactDetails(MMatchInv.Table_ID, MMatchInv.Table_Name);


                // for M_MatchInvCostTrack
                countRecord = 0;
                countRecord = DB.ExecuteQuery(@"DELETE FROM M_MatchInvCostTrack
                                                 WHERE M_Product_ID IN 
                                                 (SELECT M_Product_ID FROM M_Product WHERE M_Product_Category_ID IN (" + productCategoryID + " ) )", null, Get_Trx());

                // for m_freightimpact
                DB.ExecuteQuery(@"DELETE FROM m_freightimpact WHERE M_Product_ID IN 
                                                 (SELECT M_Product_ID FROM M_Product WHERE M_Product_Category_ID IN (" + productCategoryID + " ) )", null, Get_Trx());

                // for VAMFG_M_WrkOdrTransaction  / VAMFG_M_WrkOdrTransactionLine
                if (count > 0)
                {
                    countRecord = 0;
                    sql.Clear();
                    sql.Append($@"UPDATE VAMFG_M_WrkOdrTrnsctionLine SET  CurrentCostPrice = 0 ");
                    sql.Append(@", iscostimmediate = 'N' , iscostcalculated = 'N',  isreversedcostcalculated = 'N' ");
                    sql.Append($@" WHERE M_Product_ID IN (SELECT M_Product_ID FROM M_Product WHERE M_Product_Category_ID IN ({ productCategoryID } ) )");
                    if (DateFrom != null)
                    {
                        sql.Append($@" AND VAMFG_M_WrkOdrTransaction_ID IN (SELECT m.VAMFG_M_WrkOdrTransaction_ID FROM VAMFG_M_WrkOdrTransaction m WHERE trunc(m.VAMFG_DateAcct) >= {GlobalVariable.TO_DATE(DateFrom, true)})");
                    }
                    if (M_AttributeSetInstance_ID > 0)
                    {
                        sql.Append($@" AND M_AttributeSetInstance_ID = {M_AttributeSetInstance_ID} ");
                    }
                    countRecord = DB.ExecuteQuery(sql.ToString(), null, Get_Trx());
                    if (countRecord > 0)
                    {
                        sql.Clear();
                        sql.Append($@"UPDATE VAMFG_M_WrkOdrTransaction  SET  iscostcalculated = 'N',  isreversedcostcalculated = 'N' ");
                        sql.Append(@" , Posted ='N' ");
                        sql.Append($@"WHERE VAMFG_M_WrkOdrTransaction_ID IN ( 
                                                               SELECT VAMFG_M_WrkOdrTransaction_ID FROM VAMFG_M_WrkOdrTrnsctionLine 
                                                                WHERE M_Product_ID IN 
                                                                 (SELECT M_Product_ID FROM M_Product WHERE M_Product_Category_ID IN ({ productCategoryID } ) ) )");
                        if (DateFrom != null)
                        {
                            sql.Append($@" AND trunc(VAMFG_DateAcct) >= {GlobalVariable.TO_DATE(DateFrom, true)}");
                        }
                        countRecord = DB.ExecuteQuery(sql.ToString(), null, Get_Trx());

                        DeleteFactDetails(MTable.Get_Table_ID("M_VAMFG_M_WrkOdrTransaction"), "VAMFG_M_WrkOdrTrnsctionLine");
                    }

                    sql.Clear();
                    sql.Append($@"UPDATE VAMFG_M_WrkOdrTransaction SET CurrentCostPrice = 0 ");
                    sql.Append(@", iscostcalculated = 'N',  isreversedcostcalculated = 'N' ");
                    sql.Append(@" , Posted ='N' ");
                    sql.Append($@" WHERE M_Product_ID IN ( SELECT M_Product_ID FROM M_Product WHERE M_Product_Category_ID IN (" + productCategoryID + " ) )");
                    if (DateFrom != null)
                    {
                        sql.Append($@" AND trunc(VAMFG_DateAcct) >= {GlobalVariable.TO_DATE(DateFrom, true)} ");
                    }
                    if (M_AttributeSetInstance_ID > 0)
                    {
                        sql.Append($@" AND M_AttributeSetInstance_ID = {M_AttributeSetInstance_ID} ");
                    }
                    countRecord = DB.ExecuteQuery(sql.ToString(), null, Get_Trx());

                    DeleteFactDetails(MTable.Get_Table_ID("M_VAMFG_M_WrkOdrTransaction"), "M_VAMFG_M_WrkOdrTransaction");
                }

                // Update Transaction
                sql.Clear();
                sql.Append($@"UPDATE M_Transaction SET ProductApproxCost = 0, ProductCost = 0, M_CostElement_ID = null, CostingLevel = null, VAS_LandedCost = 0, VAS_PostingCost = 0    
                                WHERE M_Product_ID IN (SELECT DISTINCT M_Product_ID FROM M_Product 
                                    WHERE M_Product_Category_ID IN ({ productCategoryID } ) ) ");
                if (DateFrom != null)
                {
                    sql.Append($@" AND trunc(MovementDate) >= {GlobalVariable.TO_DATE(DateFrom, true)} ");
                }
                if (M_AttributeSetInstance_ID > 0)
                {
                    sql.Append($@" AND M_AttributeSetInstance_ID = {M_AttributeSetInstance_ID} ");
                }
                countRecord = DB.ExecuteQuery(sql.ToString(), null, Get_Trx());

                // Delete Query 
                int M_CostElement_ID = GetStandardCostElement();
                DB.ExecuteQuery($@"delete from m_cost where " + (M_AttributeSetInstance_ID > 0 ? $" M_AttributeSetInstance_ID = {M_AttributeSetInstance_ID} AND " : "") + @"m_product_id IN 
                                   (SELECT M_Product_ID FROM M_Product WHERE M_Product_Category_ID IN ({ productCategoryID } ) ) AND M_CostElement_ID != {M_CostElement_ID}", null, Get_Trx());
                DB.ExecuteQuery($@"UPDATE M_Cost SET CurrentQty= 0, CumulatedAmt = 0, CumulatedQty = 0 
                                    WHERE " + (M_AttributeSetInstance_ID > 0 ? $" M_AttributeSetInstance_ID = {M_AttributeSetInstance_ID} AND " : "") +
                                    @"m_product_id IN  (SELECT M_Product_ID FROM M_Product WHERE M_Product_Category_ID IN ({ productCategoryID } ) )  AND M_CostElement_ID = {M_CostElement_ID}", null, Get_Trx());
                //DB.ExecuteQuery(@"delete from m_costdetail  where m_product_id IN 
                //                   (SELECT M_Product_ID FROM M_Product WHERE M_Product_Category_ID IN (" + productCategoryID + " ) )", null, Get_Trx());
                sql.Clear();
                sql.Append($@"delete from m_costdetail WHERE m_product_id IN 
                                   (SELECT M_Product_ID FROM M_Product WHERE M_Product_Category_ID IN (" + productCategoryID + " ) )");
                if (DateFrom != null)
                {
                    sql.Append($@" AND trunc(MovementDate) >= {GlobalVariable.TO_DATE(DateFrom, true)} ");
                }
                if (M_AttributeSetInstance_ID > 0)
                {
                    sql.Append($@" AND M_AttributeSetInstance_ID = {M_AttributeSetInstance_ID} ");
                }

                // Update Current Stock which is affected after the from date on Cost Queue
                if (DateFrom != null)
                {
                    UpdateCostQueue();
                }

                sql.Clear();
                sql.Append($@"delete from M_CostQueueTransaction WHERE m_product_id IN 
                                   (SELECT M_Product_ID FROM M_Product WHERE M_Product_Category_ID IN (" + productCategoryID + " ) )");
                if (DateFrom != null)
                {
                    sql.Append($@" AND trunc(MovementDate) >= {GlobalVariable.TO_DATE(DateFrom, true)} ");
                }
                if (M_AttributeSetInstance_ID > 0)
                {
                    sql.Append($@" AND M_AttributeSetInstance_ID = {M_AttributeSetInstance_ID} ");
                }
                countRecord = DB.ExecuteQuery(sql.ToString(), null, Get_Trx());

                sql.Clear();
                sql.Append($@"delete from m_costqueue WHERE m_product_id IN 
                                   (SELECT M_Product_ID FROM M_Product WHERE M_Product_Category_ID IN (" + productCategoryID + " ) )");
                if (DateFrom != null)
                {
                    sql.Append($@" AND trunc(MovementDate) >= {GlobalVariable.TO_DATE(DateFrom, true)} ");
                }
                if (M_AttributeSetInstance_ID > 0)
                {
                    sql.Append($@" AND M_AttributeSetInstance_ID = {M_AttributeSetInstance_ID} ");
                }
                countRecord = DB.ExecuteQuery(sql.ToString(), null, Get_Trx());

                sql.Clear();
                sql.Append($@"delete from m_costelementdetail WHERE m_product_id IN 
                                   (SELECT M_Product_ID FROM M_Product WHERE M_Product_Category_ID IN (" + productCategoryID + " ) )");
                if (DateFrom != null)
                {
                    sql.Append($@" AND trunc(MovementDate) >= {GlobalVariable.TO_DATE(DateFrom, true)} ");
                }
                if (M_AttributeSetInstance_ID > 0)
                {
                    sql.Append($@" AND M_AttributeSetInstance_ID = {M_AttributeSetInstance_ID} ");
                }
                countRecord = DB.ExecuteQuery(sql.ToString(), null, Get_Trx());
                #endregion
            }
            else if (!String.IsNullOrEmpty(productID))
            {
                #region when we select product
                // for M_Inout / M_Inoutline
                //countRecord = DB.ExecuteQuery(@"UPDATE m_inoutline SET iscostimmediate = 'N' , iscostcalculated = 'N',  isreversedcostcalculated = 'N'  
                //                                 WHERE M_Product_ID IN (" + productID + " )", null, Get_Trx());
                sql.Clear();
                sql.Append($@"UPDATE m_inoutline SET  CurrentCostPrice = 0 , PostCurrentCostPrice = 0");
                sql.Append(@", iscostimmediate = 'N' , iscostcalculated = 'N',  isreversedcostcalculated = 'N'");
                sql.Append($@" WHERE M_Product_ID IN ({ productID })");
                if (DateFrom != null)
                {
                    sql.Append($@" AND M_InOut_id IN (SELECT m.M_InOut_id FROM M_InOut m WHERE trunc(m.MovementDate) >= {GlobalVariable.TO_DATE(DateFrom, true)})");
                }
                if (M_AttributeSetInstance_ID > 0)
                {
                    sql.Append($@" AND M_AttributeSetInstance_ID = {M_AttributeSetInstance_ID} ");
                }
                sql.Append($@" AND AD_client_ID IN ({ GetAD_Client_ID() })");
                countRecord = DB.ExecuteQuery(sql.ToString(), null, Get_Trx());
                if (countRecord > 0)
                {
                    sql.Clear();
                    sql.Append($@"UPDATE M_Inout  SET  iscostcalculated = 'N',  isreversedcostcalculated = 'N' ");
                    sql.Append(@" , Posted ='N' ");
                    sql.Append($@" WHERE M_Inout_ID IN ( 
                                   SELECT M_Inout_ID FROM m_inoutline WHERE  M_Product_ID IN (" + productID + " ) )");
                    if (DateFrom != null)
                    {
                        sql.Append($@" AND trunc(MovementDate) >= {GlobalVariable.TO_DATE(DateFrom, true)} ");
                    }
                    sql.Append($@" AND AD_client_ID IN ({ GetAD_Client_ID() })");
                    countRecord = DB.ExecuteQuery(sql.ToString(), null, Get_Trx());

                    DeleteFactDetails(MInOut.Table_ID, MInOut.Table_Name);
                }

                // for C_Invoice / C_InvoiceLine
                countRecord = 0;
                //countRecord = DB.ExecuteQuery(@"UPDATE c_invoiceline SET iscostimmediate = 'N' , iscostcalculated = 'N',  isreversedcostcalculated = 'N' 
                //                                WHERE M_Product_ID IN  (" + productID + " ) ", null, Get_Trx());
                sql.Clear();
                sql.Append($@"UPDATE C_Invoiceline SET  TotalInventoryAdjustment = 0,TotalCogsAdjustment = 0, PostCurrentCostPrice = 0");
                sql.Append(", iscostimmediate = 'N' , iscostcalculated = 'N',  isreversedcostcalculated = 'N' ");
                sql.Append($@" WHERE M_Product_ID IN ({ productID })");
                if (DateFrom != null)
                {
                    sql.Append($@" AND C_Invoice_ID IN (SELECT m.C_Invoice_ID FROM C_Invoice m WHERE trunc(m.DateAcct) >= {GlobalVariable.TO_DATE(DateFrom, true)})");
                }
                if (M_AttributeSetInstance_ID > 0)
                {
                    sql.Append($@" AND M_AttributeSetInstance_ID = {M_AttributeSetInstance_ID} ");
                }
                sql.Append($@" AND AD_client_ID IN ({ GetAD_Client_ID() })");
                countRecord = DB.ExecuteQuery(sql.ToString(), null, Get_Trx());
                if (countRecord > 0)
                {
                    //DB.ExecuteQuery(@"UPDATE C_Invoice  SET  iscostcalculated = 'N',  isreversedcostcalculated = 'N'
                    //                   WHERE C_Invoice_ID IN ( 
                    //                    SELECT C_Invoice_ID FROM C_Invoiceline WHERE  M_Product_ID IN  (" + productID + " ) )", null, Get_Trx());
                    sql.Clear();
                    sql.Append($@"UPDATE C_Invoice  SET  iscostcalculated = 'N',  isreversedcostcalculated = 'N' ");
                    sql.Append(@" , Posted ='N' ");
                    sql.Append($@" WHERE C_Invoice_ID IN ( 
                                        SELECT C_Invoice_ID FROM C_Invoiceline WHERE  M_Product_ID IN (" + productID + " ) )");
                    if (DateFrom != null)
                    {
                        sql.Append($@" AND trunc(DateAcct) >= {GlobalVariable.TO_DATE(DateFrom, true)} ");
                    }
                    sql.Append($@" AND AD_client_ID IN ({ GetAD_Client_ID() })");
                    countRecord = DB.ExecuteQuery(sql.ToString(), null, Get_Trx());

                    DeleteFactDetails(MInvoice.Table_ID, MInvoice.Table_Name);
                }

                // for C_ProvisionalInvoice / C_ProvisionalInvoiceLine
                countRecord = 0;
                sql.Clear();
                sql.Append($@"UPDATE c_Provisionalinvoiceline SET iscostimmediate = 'N' , iscostcalculated = 'N',  isreversedcostcalculated = 'N' ");
                sql.Append($@" WHERE M_Product_ID IN (" + productID + " ) ");
                if (DateFrom != null)
                {
                    sql.Append($@" AND C_ProvisionalInvoice_ID IN (SELECT m.C_ProvisionalInvoice_ID FROM C_ProvisionalInvoice m WHERE trunc(m.DateAcct) >= {GlobalVariable.TO_DATE(DateFrom, true)})");
                }
                if (M_AttributeSetInstance_ID > 0)
                {
                    sql.Append($@" AND M_AttributeSetInstance_ID = {M_AttributeSetInstance_ID} ");
                }
                sql.Append($@" AND AD_client_ID IN ({ GetAD_Client_ID() })");
                countRecord = DB.ExecuteQuery(sql.ToString(), null, Get_Trx());
                if (countRecord > 0)
                {
                    sql.Clear();
                    sql.Append(@"UPDATE C_ProvisionalInvoice  SET  iscostcalculated = 'N',  isreversedcostcalculated = 'N'");
                    sql.Append(@" , Posted ='N' ");
                    sql.Append($@"WHERE C_ProvisionalInvoice_ID IN ( 
                                        SELECT C_ProvisionalInvoice_ID FROM C_ProvisionalInvoiceline WHERE  M_Product_ID IN  (" + productID + " ) )");
                    if (DateFrom != null)
                    {
                        sql.Append($@" AND trunc(DateAcct) >= {GlobalVariable.TO_DATE(DateFrom, true)} ");
                    }
                    sql.Append($@" AND AD_client_ID IN ({ GetAD_Client_ID() })");
                    countRecord = DB.ExecuteQuery(sql.ToString(), null, Get_Trx());

                    DeleteFactDetails(MProvisionalInvoice.Table_ID, MProvisionalInvoice.Table_Name);
                }

                // For Landed Cost Allocation
                countRecord = 0;
                sql.Clear();
                sql.Append($@"UPDATE C_LANDEDCOSTALLOCATION SET  iscostcalculated = 'N' ");
                sql.Append($@"WHERE M_Product_ID IN (" + productID + " ) ");
                if (DateFrom != null)
                {
                    sql.Append($@" AND C_InvoiceLine_ID IN (SELECT C_InvoiceLine_ID FROM C_Invoice i INNER JOIN C_InvoiceLine il ON (i.C_Invoice_ID = il.C_Invoice_ID) 
                                WHERE TRUNC(i.DateAcct) >= {GlobalVariable.TO_DATE(DateFrom, true)})");
                }
                if (M_AttributeSetInstance_ID > 0)
                {
                    sql.Append($@" AND M_AttributeSetInstance_ID = {M_AttributeSetInstance_ID} ");
                }
                sql.Append($@" AND AD_client_ID IN ({ GetAD_Client_ID() })");
                countRecord = DB.ExecuteQuery(sql.ToString(), null, Get_Trx());

                // expected landed cost
                //countRecord = 0;
                //countRecord = DB.ExecuteQuery(@"UPDATE C_Expectedcostdistribution Set IsCostCalculated  = 'N'
                //                                    Where C_Orderline_ID IN (Select C_Orderline_ID
                //                                      FROM C_OrderLine WHERE M_Product_ID IN (" + productID + "))", null, Get_Trx());

                // for M_Inventory / M_InventoryLine
                countRecord = 0;
                sql.Clear();
                sql.Append($@"UPDATE m_inventoryline SET  CurrentCostPrice = 0 , PostCurrentCostPrice = 0");
                sql.Append($@", iscostimmediate = 'N' , iscostcalculated = 'N',  isreversedcostcalculated = 'N' ");
                sql.Append($@" WHERE M_Product_ID IN ({ productID })");
                if (DateFrom != null)
                {
                    sql.Append($@" AND M_Inventory_ID IN (SELECT m.M_Inventory_ID FROM M_Inventory m WHERE trunc(m.MovementDate) >= {GlobalVariable.TO_DATE(DateFrom, true)})");
                }
                if (M_AttributeSetInstance_ID > 0)
                {
                    sql.Append($@" AND M_AttributeSetInstance_ID = {M_AttributeSetInstance_ID} ");
                }
                sql.Append($@" AND AD_client_ID IN ({ GetAD_Client_ID() })");
                countRecord = DB.ExecuteQuery(sql.ToString(), null, Get_Trx());
                if (countRecord > 0)
                {
                    sql.Clear();
                    sql.Append($@"UPDATE m_inventory  SET  iscostcalculated = 'N',  isreversedcostcalculated = 'N'");
                    sql.Append(@" , Posted ='N' ");
                    sql.Append($@" WHERE m_inventory_ID IN ( 
                                        SELECT m_inventory_ID FROM m_inventoryline WHERE  M_Product_ID IN (" + productID + " ) )");
                    if (DateFrom != null)
                    {
                        sql.Append($@" AND trunc(MovementDate) >= {GlobalVariable.TO_DATE(DateFrom, true)} ");
                    }
                    sql.Append($@" AND AD_client_ID IN ({ GetAD_Client_ID() })");
                    countRecord = DB.ExecuteQuery(sql.ToString(), null, Get_Trx());

                    DeleteFactDetails(MInventory.Table_ID, MInventory.Table_Name);
                }

                // for M_Movement / M_MovementLine
                countRecord = 0;
                sql.Clear();
                sql.Append($@"UPDATE m_movementline SET  CurrentCostPrice = 0 , PostCurrentCostPrice = 0 , ToCurrentCostPrice = 0 , ToPostCurrentCostPrice = 0");
                sql.Append(@", iscostimmediate = 'N' , iscostcalculated = 'N',  isreversedcostcalculated = 'N' ");
                sql.Append($@" WHERE M_Product_ID IN ({ productID })");
                if (DateFrom != null)
                {
                    sql.Append($@" AND M_Movement_ID IN (SELECT m.M_Movement_ID FROM M_Movement m WHERE trunc(m.MovementDate) >= {GlobalVariable.TO_DATE(DateFrom, true)})");
                }
                if (M_AttributeSetInstance_ID > 0)
                {
                    sql.Append($@" AND M_AttributeSetInstance_ID = {M_AttributeSetInstance_ID} ");
                }
                sql.Append($@" AND AD_client_ID IN ({ GetAD_Client_ID() })");
                countRecord = DB.ExecuteQuery(sql.ToString(), null, Get_Trx());
                if (countRecord > 0)
                {
                    sql.Clear();
                    sql.Append($@"UPDATE m_movement  SET  iscostcalculated = 'N',  isreversedcostcalculated = 'N'");
                    sql.Append(@" , Posted ='N' ");
                    sql.Append($@" WHERE m_movement_ID IN ( 
                                        SELECT m_movement_ID FROM m_movementline WHERE  M_Product_ID IN (" + productID + " ) )");
                    if (DateFrom != null)
                    {
                        sql.Append($@" AND trunc(MovementDate) >= {GlobalVariable.TO_DATE(DateFrom, true)} ");
                    }
                    sql.Append($@" AND AD_client_ID IN ({ GetAD_Client_ID() })");
                    countRecord = DB.ExecuteQuery(sql.ToString(), null, Get_Trx());


                    DeleteFactDetails(MMovement.Table_ID, MMovement.Table_Name);
                }

                // for VAFAM_AssetDisposal
                sql.Clear();
                sql.Append($@"UPDATE VAFAM_AssetDisposal SET iscostimmediate = 'N' , iscostcalculated = 'N',  isreversedcostcalculated = 'N' ");
                sql.Append(@" , Posted ='N' ");
                sql.Append($@"WHERE M_Product_ID IN (" + productID + " ) ");
                if (DateFrom != null)
                {
                    sql.Append($@" AND trunc(DateAcct) >= {GlobalVariable.TO_DATE(DateFrom, true)} ");
                }
                if (M_AttributeSetInstance_ID > 0)
                {
                    sql.Append($@" AND M_AttributeSetInstance_ID = {M_AttributeSetInstance_ID} ");
                }
                sql.Append($@" AND AD_client_ID IN ({ GetAD_Client_ID() })");
                countRecord = DB.ExecuteQuery(sql.ToString(), null, Get_Trx());

                DeleteFactDetails(MTable.Get_Table_ID("VAFAM_AssetDisposal"), "VAFAM_AssetDisposal");

                // for M_Production / M_ProductionLine
                countRecord = 0;
                sql.Clear();
                sql.Append($@"UPDATE m_productionline SET  Amt = 0");
                sql.Append(", IsCostImmediate = 'N'");
                sql.Append($@" WHERE NVL(C_Charge_ID, 0) = 0 AND M_Product_ID IN ({ productID })");
                if (DateFrom != null)
                {
                    sql.Append($@" AND M_Production_ID IN (SELECT m.M_Production_ID FROM M_Production m WHERE trunc(m.MovementDate) >= {GlobalVariable.TO_DATE(DateFrom, true)})");
                }
                if (M_AttributeSetInstance_ID > 0)
                {
                    sql.Append($@" AND M_AttributeSetInstance_ID = {M_AttributeSetInstance_ID} ");
                }
                sql.Append($@" AND AD_client_ID IN ({ GetAD_Client_ID() })");
                countRecord = DB.ExecuteQuery(sql.ToString(), null, Get_Trx());
                if (countRecord > 0)
                {
                    sql.Clear();
                    sql.Append($@"UPDATE M_Production  SET  iscostcalculated = 'N',  isreversedcostcalculated = 'N' ");
                    sql.Append(@" , Posted ='N' ");
                    sql.Append($@"WHERE M_Production_ID IN ( 
                                        SELECT M_Production_ID FROM M_Productionline WHERE  M_Product_ID IN (" + productID + " ) )");
                    if (DateFrom != null)
                    {
                        sql.Append($@" AND trunc(MovementDate) >= {GlobalVariable.TO_DATE(DateFrom, true)}");
                    }
                    sql.Append($@" AND AD_client_ID IN ({ GetAD_Client_ID() })");
                    countRecord = DB.ExecuteQuery(sql.ToString(), null, Get_Trx());

                    DeleteFactDetails(X_M_Production.Table_ID, X_M_Production.Table_Name);
                }

                // for M_MatchInv
                countRecord = 0;
                sql.Clear();
                sql.Append($@"UPDATE M_MatchInv SET  CurrentCostPrice = 0 , PostCurrentCostPrice = 0");
                sql.Append(@", iscostimmediate = 'N' , iscostcalculated = 'N' ");
                sql.Append(@" , Posted ='N' ");
                sql.Append($@" WHERE M_Product_ID IN ({ productID })");
                if (DateFrom != null)
                {
                    sql.Append($@" AND trunc(DateAcct) >= {GlobalVariable.TO_DATE(DateFrom, true)} ");
                }
                if (M_AttributeSetInstance_ID > 0)
                {
                    sql.Append($@" AND M_AttributeSetInstance_ID = {M_AttributeSetInstance_ID} ");
                }
                sql.Append($@" AND AD_client_ID IN ({ GetAD_Client_ID() })");
                countRecord = DB.ExecuteQuery(sql.ToString(), null, Get_Trx());

                DeleteFactDetails(MMatchInv.Table_ID, MMatchInv.Table_Name);

                // for M_MatchInvCostTrack
                countRecord = 0;
                countRecord = DB.ExecuteQuery($@"DELETE FROM M_MatchInvCostTrack
                                                 WHERE AD_client_ID IN ({ GetAD_Client_ID() }) AND M_Product_ID IN  (" + productID + " )", null, Get_Trx());

                // for m_freightimpact
                DB.ExecuteQuery($@"DELETE FROM m_freightimpact WHERE AD_client_ID IN ({ GetAD_Client_ID() }) AND M_Product_ID IN  (" + productID + " )", null, Get_Trx());

                // for VAMFG_M_WrkOdrTransaction  / VAMFG_M_WrkOdrTransactionLine
                if (count > 0)
                {
                    countRecord = 0;
                    sql.Clear();
                    sql.Append($@"UPDATE VAMFG_M_WrkOdrTrnsctionLine SET  CurrentCostPrice = 0");
                    sql.Append(@", iscostimmediate = 'N' , iscostcalculated = 'N',  isreversedcostcalculated = 'N' ");
                    sql.Append($@" WHERE M_Product_ID IN ({ productID })");
                    if (DateFrom != null)
                    {
                        sql.Append($@" AND VAMFG_M_WrkOdrTransaction_ID IN (SELECT m.VAMFG_M_WrkOdrTransaction_ID FROM VAMFG_M_WrkOdrTransaction m WHERE trunc(m.VAMFG_DateAcct) >= {GlobalVariable.TO_DATE(DateFrom, true)})");
                    }
                    if (M_AttributeSetInstance_ID > 0)
                    {
                        sql.Append($@" AND M_AttributeSetInstance_ID = {M_AttributeSetInstance_ID} ");
                    }
                    sql.Append($@" AND AD_client_ID IN ({ GetAD_Client_ID() })");
                    countRecord = DB.ExecuteQuery(sql.ToString(), null, Get_Trx());
                    if (countRecord > 0)
                    {
                        sql.Clear();
                        sql.Append($@"UPDATE VAMFG_M_WrkOdrTransaction  SET  iscostcalculated = 'N',  isreversedcostcalculated = 'N' ");
                        sql.Append(@" , Posted ='N' ");
                        sql.Append($@"WHERE VAMFG_M_WrkOdrTransaction_ID IN ( 
                                                               SELECT VAMFG_M_WrkOdrTransaction_ID FROM VAMFG_M_WrkOdrTrnsctionLine 
                                                                WHERE M_Product_ID IN  (" + productID + " ) )");
                        if (DateFrom != null)
                        {
                            sql.Append($@" AND trunc(VAMFG_DateAcct) >= {GlobalVariable.TO_DATE(DateFrom, true)}");
                        }
                        sql.Append($@" AND AD_client_ID IN ({ GetAD_Client_ID() })");
                        countRecord = DB.ExecuteQuery(sql.ToString(), null, Get_Trx());

                        DeleteFactDetails(MTable.Get_Table_ID("M_VAMFG_M_WrkOdrTransaction"), "VAMFG_M_WrkOdrTrnsctionLine");
                    }

                    sql.Clear();
                    sql.Append($@"UPDATE VAMFG_M_WrkOdrTransaction SET CurrentCostPrice = 0");
                    sql.Append(@", iscostcalculated = 'N',  isreversedcostcalculated = 'N' ");
                    sql.Append(@" , Posted ='N' ");
                    sql.Append($@" WHERE M_Product_ID IN ({ productID })");
                    if (DateFrom != null)
                    {
                        sql.Append($@" AND trunc(VAMFG_DateAcct) >= {GlobalVariable.TO_DATE(DateFrom, true)} ");
                    }
                    if (M_AttributeSetInstance_ID > 0)
                    {
                        sql.Append($@" AND M_AttributeSetInstance_ID = {M_AttributeSetInstance_ID} ");
                    }
                    sql.Append($@" AND AD_client_ID IN ({ GetAD_Client_ID() })");
                    countRecord = DB.ExecuteQuery(sql.ToString(), null, Get_Trx());

                    DeleteFactDetails(MTable.Get_Table_ID("M_VAMFG_M_WrkOdrTransaction"), "M_VAMFG_M_WrkOdrTransaction");
                }

                // Update Transaction
                sql.Clear();
                sql.Append($@"UPDATE M_Transaction SET ProductApproxCost = 0, ProductCost = 0, M_CostElement_ID = null, CostingLevel = null, VAS_LandedCost = 0, VAS_PostingCost = 0  WHERE M_Product_ID IN ({ productID })");
                if (DateFrom != null)
                {
                    sql.Append($@" AND trunc(MovementDate) >= {GlobalVariable.TO_DATE(DateFrom, true)} ");
                }
                if (M_AttributeSetInstance_ID > 0)
                {
                    sql.Append($@" AND M_AttributeSetInstance_ID = {M_AttributeSetInstance_ID} ");
                }
                sql.Append($@" AND AD_client_ID IN ({ GetAD_Client_ID() })");
                countRecord = DB.ExecuteQuery(sql.ToString(), null, Get_Trx());

                // Delete Query 
                int M_CostElement_ID = GetStandardCostElement();
                DB.ExecuteQuery($@"delete from m_cost where " + (M_AttributeSetInstance_ID > 0 ? $" M_AttributeSetInstance_ID = {M_AttributeSetInstance_ID} AND " : "") +
                    $@"m_product_id IN  ({ productID } ) AND M_CostElement_ID != {M_CostElement_ID}", null, Get_Trx());
                DB.ExecuteQuery($@"UPDATE M_Cost SET CurrentQty= 0, CumulatedAmt = 0, CumulatedQty = 0 
                                    WHERE " + (M_AttributeSetInstance_ID > 0 ? $" M_AttributeSetInstance_ID = {M_AttributeSetInstance_ID} AND " : "") +
                                    $@"m_product_id IN  ({ productID } )  AND M_CostElement_ID = {M_CostElement_ID}", null, Get_Trx());

                //DB.ExecuteQuery(@"delete from m_costdetail  where m_product_id IN  (" + productID + " ) ", null, Get_Trx());
                sql.Clear();
                sql.Append($@"delete from m_costdetail WHERE M_Product_ID IN ({ productID })");
                if (DateFrom != null)
                {
                    sql.Append($@" AND trunc(MovementDate) >= {GlobalVariable.TO_DATE(DateFrom, true)} ");
                }
                if (M_AttributeSetInstance_ID > 0)
                {
                    sql.Append($@" AND M_AttributeSetInstance_ID = {M_AttributeSetInstance_ID} ");
                }
                countRecord = DB.ExecuteQuery(sql.ToString(), null, Get_Trx());

                // Update Current Stock which is affected after the from date on Cost Queue
                if (DateFrom != null)
                {
                    UpdateCostQueue();
                }

                sql.Clear();
                sql.Append($@"delete from M_CostQueueTransaction WHERE M_Product_ID IN ({ productID })");
                if (DateFrom != null)
                {
                    sql.Append($@" AND trunc(MovementDate) >= {GlobalVariable.TO_DATE(DateFrom, true)} ");
                }
                if (M_AttributeSetInstance_ID > 0)
                {
                    sql.Append($@" AND M_AttributeSetInstance_ID = {M_AttributeSetInstance_ID} ");
                }
                countRecord = DB.ExecuteQuery(sql.ToString(), null, Get_Trx());

                sql.Clear();
                sql.Append($@"delete from m_costqueue WHERE M_Product_ID IN ({ productID })");
                if (DateFrom != null)
                {
                    sql.Append($@" AND trunc(MovementDate) >= {GlobalVariable.TO_DATE(DateFrom, true)} ");
                }
                if (M_AttributeSetInstance_ID > 0)
                {
                    sql.Append($@" AND M_AttributeSetInstance_ID = {M_AttributeSetInstance_ID} ");
                }
                countRecord = DB.ExecuteQuery(sql.ToString(), null, Get_Trx());

                sql.Clear();
                sql.Append($@"delete from m_costelementdetail WHERE M_Product_ID IN ({ productID })");
                if (DateFrom != null)
                {
                    sql.Append($@" AND trunc(MovementDate) >= {GlobalVariable.TO_DATE(DateFrom, true)} ");
                }
                if (M_AttributeSetInstance_ID > 0)
                {
                    sql.Append($@" AND M_AttributeSetInstance_ID = {M_AttributeSetInstance_ID} ");
                }
                countRecord = DB.ExecuteQuery(sql.ToString(), null, Get_Trx());
                #endregion
            }
            else if (onlyDeleteCosting.Equals("Y"))
            {
                #region when user select "Only Delete Costing"
                // for M_Inout / M_Inoutline
                sql.Clear();
                sql.Append($@"UPDATE m_inoutline SET  CurrentCostPrice = 0 , PostCurrentCostPrice = 0");
                sql.Append(@", iscostimmediate = 'N' , iscostcalculated = 'N',  isreversedcostcalculated = 'N'");
                sql.Append($@" WHERE AD_client_ID IN ({ GetAD_Client_ID() })");
                if (DateFrom != null)
                {
                    sql.Append($@" AND M_InOut_id IN (SELECT m.M_InOut_id FROM M_InOut m WHERE trunc(m.MovementDate) >= {GlobalVariable.TO_DATE(DateFrom, true)})");
                }
                countRecord = DB.ExecuteQuery(sql.ToString(), null, Get_Trx());

                sql.Clear();
                sql.Append($@"UPDATE M_Inout  SET  iscostcalculated = 'N',  isreversedcostcalculated = 'N' ");
                sql.Append(@" , Posted ='N' ");
                sql.Append($@" WHERE AD_client_ID =  " + GetAD_Client_ID());
                if (DateFrom != null)
                {
                    sql.Append($@" AND trunc(MovementDate) >= {GlobalVariable.TO_DATE(DateFrom, true)} ");
                }
                countRecord = DB.ExecuteQuery(sql.ToString(), null, Get_Trx());

                DeleteFactDetails(MInOut.Table_ID, MInOut.Table_Name);

                // for C_Invoice / C_InvoiceLine
                sql.Clear();
                sql.Append($@"UPDATE C_Invoiceline SET  TotalInventoryAdjustment = 0,TotalCogsAdjustment = 0, PostCurrentCostPrice = 0");
                sql.Append(", iscostimmediate = 'N' , iscostcalculated = 'N',  isreversedcostcalculated = 'N' ");
                sql.Append($@" WHERE AD_client_ID IN ({ GetAD_Client_ID() })");
                if (DateFrom != null)
                {
                    sql.Append($@" AND C_Invoice_ID IN (SELECT m.C_Invoice_ID FROM C_Invoice m WHERE trunc(m.DateAcct) >= {GlobalVariable.TO_DATE(DateFrom, true)})");
                }
                countRecord = DB.ExecuteQuery(sql.ToString(), null, Get_Trx());

                DB.ExecuteQuery(@"UPDATE C_Invoice  SET  iscostcalculated = 'N',  isreversedcostcalculated = 'N' WHERE AD_client_ID =  " + GetAD_Client_ID(), null, Get_Trx());
                sql.Clear();
                sql.Append($@"UPDATE C_Invoice  SET  iscostcalculated = 'N',  isreversedcostcalculated = 'N' ");
                sql.Append(@" , Posted ='N' ");
                sql.Append($@" WHERE AD_client_ID =  " + GetAD_Client_ID());
                if (DateFrom != null)
                {
                    sql.Append($@" AND trunc(DateAcct) >= {GlobalVariable.TO_DATE(DateFrom, true)} ");
                }
                countRecord = DB.ExecuteQuery(sql.ToString(), null, Get_Trx());

                DeleteFactDetails(MInvoice.Table_ID, MInvoice.Table_Name);


                // for C_ProvisionalInvoice / C_ProvisionalInvoiceLine
                sql.Clear();
                sql.Append($@"UPDATE c_Provisionalinvoiceline SET iscostimmediate = 'N' , iscostcalculated = 'N',  isreversedcostcalculated = 'N'");
                sql.Append($@" WHERE AD_client_ID =  " + GetAD_Client_ID());
                if (DateFrom != null)
                {
                    sql.Append($@" AND C_ProvisionalInvoice_ID IN (SELECT m.C_ProvisionalInvoice_ID FROM C_ProvisionalInvoice m WHERE trunc(m.DateAcct) >= {GlobalVariable.TO_DATE(DateFrom, true)})");
                }
                countRecord = DB.ExecuteQuery(sql.ToString(), null, Get_Trx());

                sql.Clear();
                sql.Append(@"UPDATE C_ProvisionalInvoice  SET  iscostcalculated = 'N',  isreversedcostcalculated = 'N'");
                sql.Append(@" , Posted ='N' ");
                sql.Append($@" WHERE AD_client_ID =  " + GetAD_Client_ID());
                if (DateFrom != null)
                {
                    sql.Append($@" AND trunc(DateAcct) >= {GlobalVariable.TO_DATE(DateFrom, true)} ");
                }
                countRecord = DB.ExecuteQuery(sql.ToString(), null, Get_Trx());

                DeleteFactDetails(MProvisionalInvoice.Table_ID, MProvisionalInvoice.Table_Name);

                // For Landed Cost Allocation
                sql.Clear();
                sql.Append($@"UPDATE C_LANDEDCOSTALLOCATION SET  iscostcalculated = 'N' ");
                sql.Append($@" WHERE AD_client_ID =  " + GetAD_Client_ID());
                if (DateFrom != null)
                {
                    sql.Append($@" AND C_InvoiceLine_ID IN (SELECT C_InvoiceLine_ID FROM C_Invoice i INNER JOIN C_InvoiceLine il ON (i.C_Invoice_ID = il.C_Invoice_ID) 
                                WHERE TRUNC(i.DateAcct) >= {GlobalVariable.TO_DATE(DateFrom, true)})");
                }
                countRecord = DB.ExecuteQuery(sql.ToString(), null, Get_Trx());

                // expected landed cost
                //DB.ExecuteQuery(@"UPDATE C_Expectedcostdistribution Set IsCostCalculated  = 'N' WHERE AD_client_ID =  " + GetAD_Client_ID(), null, Get_Trx());

                // for M_Inventory / M_InventoryLine
                sql.Clear();
                sql.Append($@"UPDATE m_inventoryline SET  CurrentCostPrice = 0 , PostCurrentCostPrice = 0");
                sql.Append($@", iscostimmediate = 'N' , iscostcalculated = 'N',  isreversedcostcalculated = 'N' ");
                sql.Append($@" WHERE AD_client_ID IN ({ GetAD_Client_ID() })");
                if (DateFrom != null)
                {
                    sql.Append($@" AND M_Inventory_ID IN (SELECT m.M_Inventory_ID FROM M_Inventory m WHERE trunc(m.MovementDate) >= {GlobalVariable.TO_DATE(DateFrom, true)})");
                }
                countRecord = DB.ExecuteQuery(sql.ToString(), null, Get_Trx());

                sql.Clear();
                sql.Append($@"UPDATE m_inventory  SET  iscostcalculated = 'N',  isreversedcostcalculated = 'N'");
                sql.Append(@" , Posted ='N' ");
                sql.Append($@" WHERE AD_client_ID IN ({ GetAD_Client_ID() })");
                if (DateFrom != null)
                {
                    sql.Append($@" AND trunc(MovementDate) >= {GlobalVariable.TO_DATE(DateFrom, true)} ");
                }
                countRecord = DB.ExecuteQuery(sql.ToString(), null, Get_Trx());

                DeleteFactDetails(MInventory.Table_ID, MInventory.Table_Name);

                // for VAFAM_AssetDisposal
                sql.Clear();
                sql.Append($@"UPDATE VAFAM_AssetDisposal SET iscostimmediate = 'N' , iscostcalculated = 'N',  isreversedcostcalculated = 'N' ");
                sql.Append(@" , Posted ='N' ");
                sql.Append($@" WHERE AD_client_ID IN ({ GetAD_Client_ID() })");
                if (DateFrom != null)
                {
                    sql.Append($@" AND trunc(DateAcct) >= {GlobalVariable.TO_DATE(DateFrom, true)} ");
                }
                countRecord = DB.ExecuteQuery(sql.ToString(), null, Get_Trx());

                DeleteFactDetails(MTable.Get_Table_ID("VAFAM_AssetDisposal"), "VAFAM_AssetDisposal");

                // for M_Movement / M_MovementLine
                sql.Clear();
                sql.Append($@"UPDATE m_movementline SET CurrentCostPrice = 0 , PostCurrentCostPrice = 0 , ToCurrentCostPrice = 0 , ToPostCurrentCostPrice = 0");
                sql.Append(@", iscostimmediate = 'N' , iscostcalculated = 'N',  isreversedcostcalculated = 'N' ");
                sql.Append($@" WHERE AD_client_ID IN ({ GetAD_Client_ID() })");
                if (DateFrom != null)
                {
                    sql.Append($@" AND M_Movement_ID IN (SELECT m.M_Movement_ID FROM M_Movement m WHERE trunc(m.MovementDate) >= {GlobalVariable.TO_DATE(DateFrom, true)})");
                }
                countRecord = DB.ExecuteQuery(sql.ToString(), null, Get_Trx());

                sql.Clear();
                sql.Append($@"UPDATE m_movement  SET  iscostcalculated = 'N',  isreversedcostcalculated = 'N'");
                sql.Append(@" , Posted ='N' ");
                sql.Append($@" WHERE AD_client_ID IN ({ GetAD_Client_ID() })");
                if (DateFrom != null)
                {
                    sql.Append($@" AND trunc(MovementDate) >= {GlobalVariable.TO_DATE(DateFrom, true)} ");
                }
                countRecord = DB.ExecuteQuery(sql.ToString(), null, Get_Trx());

                DeleteFactDetails(MMovement.Table_ID, MMovement.Table_Name);

                // for M_Production / M_ProductionLine
                sql.Clear();
                sql.Append($@"UPDATE m_productionline SET  Amt = 0");
                sql.Append(", IsCostImmediate = 'N'");
                sql.Append($@" WHERE NVL(C_Charge_ID, 0) = 0 AND AD_client_ID IN ({ GetAD_Client_ID() })");
                if (DateFrom != null)
                {
                    sql.Append($@" AND M_Production_ID IN (SELECT m.M_Production_ID FROM M_Production m WHERE trunc(m.MovementDate) >= {GlobalVariable.TO_DATE(DateFrom, true)})");
                }
                countRecord = DB.ExecuteQuery(sql.ToString(), null, Get_Trx());

                sql.Clear();
                sql.Append($@"UPDATE M_Production  SET  iscostcalculated = 'N',  isreversedcostcalculated = 'N' ");
                sql.Append(@" , Posted ='N' ");
                sql.Append($@" WHERE AD_client_ID IN ({ GetAD_Client_ID() })");
                if (DateFrom != null)
                {
                    sql.Append($@" AND trunc(MovementDate) >= {GlobalVariable.TO_DATE(DateFrom, true)}");
                }
                countRecord = DB.ExecuteQuery(sql.ToString(), null, Get_Trx());

                DeleteFactDetails(X_M_Production.Table_ID, X_M_Production.Table_Name);

                // for M_MatchInv
                sql.Clear();
                sql.Append($@"UPDATE M_MatchInv SET  CurrentCostPrice = 0 , PostCurrentCostPrice = 0");
                sql.Append(@", iscostimmediate = 'N' , iscostcalculated = 'N' ");
                sql.Append(@" , Posted ='N' ");
                sql.Append($@" WHERE AD_client_ID IN ({ GetAD_Client_ID() })");
                if (DateFrom != null)
                {
                    sql.Append($@" AND trunc(DateAcct) >= {GlobalVariable.TO_DATE(DateFrom, true)} ");
                }
                countRecord = DB.ExecuteQuery(sql.ToString(), null, Get_Trx());

                DeleteFactDetails(MMatchInv.Table_ID, MMatchInv.Table_Name);

                // for M_MatchInvCostTrack
                DB.ExecuteQuery(@"DELETE FROM M_MatchInvCostTrack WHERE AD_client_ID =  " + GetAD_Client_ID(), null, Get_Trx());

                // for m_freightimpact
                DB.ExecuteQuery(@"DELETE FROM m_freightimpact WHERE AD_client_ID =  " + GetAD_Client_ID(), null, Get_Trx());

                // for VAMFG_M_WrkOdrTransaction  / VAMFG_M_WrkOdrTransactionLine
                if (count > 0)
                {
                    sql.Clear();
                    sql.Append($@"UPDATE VAMFG_M_WrkOdrTrnsctionLine SET  CurrentCostPrice = 0 ");
                    sql.Append(@", iscostimmediate = 'N' , iscostcalculated = 'N',  isreversedcostcalculated = 'N' ");
                    sql.Append($@" WHERE AD_client_ID IN ({ GetAD_Client_ID() })");
                    if (DateFrom != null)
                    {
                        sql.Append($@" AND VAMFG_M_WrkOdrTransaction_ID IN (SELECT m.VAMFG_M_WrkOdrTransaction_ID FROM VAMFG_M_WrkOdrTransaction m WHERE trunc(m.VAMFG_DateAcct) >= {GlobalVariable.TO_DATE(DateFrom, true)})");
                    }
                    countRecord = DB.ExecuteQuery(sql.ToString(), null, Get_Trx());

                    sql.Clear();
                    sql.Append($@"UPDATE VAMFG_M_WrkOdrTransaction  SET  iscostcalculated = 'N',  isreversedcostcalculated = 'N' ");
                    sql.Append(@" , Posted ='N' ");
                    sql.Append($@"WHERE AD_client_ID =  " + GetAD_Client_ID());
                    if (DateFrom != null)
                    {
                        sql.Append($@" AND trunc(VAMFG_DateAcct) >= {GlobalVariable.TO_DATE(DateFrom, true)}");
                    }
                    countRecord = DB.ExecuteQuery(sql.ToString(), null, Get_Trx());

                    DeleteFactDetails(MTable.Get_Table_ID("M_VAMFG_M_WrkOdrTransaction"), "VAMFG_M_WrkOdrTrnsctionLine");

                    sql.Clear();
                    sql.Append($@"UPDATE VAMFG_M_WrkOdrTransaction SET CurrentCostPrice = 0 ");
                    sql.Append(@", iscostcalculated = 'N',  isreversedcostcalculated = 'N' ");
                    sql.Append(@" , Posted ='N' ");
                    sql.Append($@" WHERE AD_client_ID IN ({ GetAD_Client_ID() })");
                    if (DateFrom != null)
                    {
                        sql.Append($@" AND trunc(VAMFG_DateAcct) >= {GlobalVariable.TO_DATE(DateFrom, true)} ");
                    }
                    countRecord = DB.ExecuteQuery(sql.ToString(), null, Get_Trx());

                    DeleteFactDetails(MTable.Get_Table_ID("M_VAMFG_M_WrkOdrTransaction"), "M_VAMFG_M_WrkOdrTransaction");
                }

                // Update Transaction
                sql.Clear();
                sql.Append($@"UPDATE M_Transaction SET ProductApproxCost = 0, ProductCost = 0, M_CostElement_ID = null, CostingLevel = null, VAS_LandedCost = 0, VAS_PostingCost = 0  WHERE AD_client_ID IN ({ GetAD_Client_ID()  })");
                if (DateFrom != null)
                {
                    sql.Append($@" AND trunc(MovementDate) >= {GlobalVariable.TO_DATE(DateFrom, true)} ");
                }
                countRecord = DB.ExecuteQuery(sql.ToString(), null, Get_Trx());

                // Delete Query 
                int M_CostElement_ID = GetStandardCostElement();
                DB.ExecuteQuery($@"delete from m_cost WHERE M_CostElement_ID != {M_CostElement_ID} AND AD_client_ID =  " + GetAD_Client_ID(), null, Get_Trx());
                DB.ExecuteQuery($@"UPDATE M_Cost SET CurrentQty= 0, CumulatedAmt = 0, CumulatedQty = 0 
                                    WHERE M_CostElement_ID = {M_CostElement_ID}", null, Get_Trx());

                sql.Clear();
                sql.Append($@"delete from m_costdetail WHERE AD_client_ID IN ({ GetAD_Client_ID() })");
                if (DateFrom != null)
                {
                    sql.Append($@" AND trunc(MovementDate) >= {GlobalVariable.TO_DATE(DateFrom, true)} ");
                }
                countRecord = DB.ExecuteQuery(sql.ToString(), null, Get_Trx());

                // Update Current Stock which is affected after the from date on Cost Queue
                if (DateFrom != null)
                {
                    UpdateCostQueue();
                }

                sql.Clear();
                sql.Append($@"delete from M_CostQueueTransaction WHERE AD_client_ID IN ({ GetAD_Client_ID() })");
                if (DateFrom != null)
                {
                    sql.Append($@" AND trunc(MovementDate) >= {GlobalVariable.TO_DATE(DateFrom, true)} ");
                }
                countRecord = DB.ExecuteQuery(sql.ToString(), null, Get_Trx());


                sql.Clear();
                sql.Append($@"delete from m_costqueue WHERE AD_client_ID IN ({ GetAD_Client_ID() })");
                if (DateFrom != null)
                {
                    sql.Append($@" AND trunc(MovementDate) >= {GlobalVariable.TO_DATE(DateFrom, true)} ");
                }
                countRecord = DB.ExecuteQuery(sql.ToString(), null, Get_Trx());

                sql.Clear();
                sql.Append($@"delete from m_costelementdetail WHERE AD_client_ID IN ({ GetAD_Client_ID() })");
                if (DateFrom != null)
                {
                    sql.Append($@" AND trunc(MovementDate) >= {GlobalVariable.TO_DATE(DateFrom, true)} ");
                }
                countRecord = DB.ExecuteQuery(sql.ToString(), null, Get_Trx());

                #endregion
            }

            Get_Trx().Commit();

        }

        /// <summary>
        /// This function is used to update the Current Qunatity on Cost Queue when Re-Costing calculation process run with From data Parameter
        /// </summary>
        /// <returns></returns>
        public bool UpdateCostQueue()
        {
            if (DateFrom != null)
            {
                var pc = "(SELECT M_Product_ID FROM M_Product WHERE M_Product_Category_ID IN (" + productCategoryID + " ) )";
                sql.Clear();
                sql.Append($@"UPDATE M_CostQueue cq
                            SET CurrentQty =  (
                                SELECT ABS(SUM(cqt.MovementQty))
                                FROM M_CostQueueTransaction cqt
                                WHERE " + (M_AttributeSetInstance_ID > 0 ? $" M_AttributeSetInstance_ID = {M_AttributeSetInstance_ID} AND " : "") +
                                  $@"cq.M_CostQueue_ID = cqt.M_CostQueue_ID
                                  AND cq.M_Product_ID IN ({((!String.IsNullOrEmpty(productCategoryID) && String.IsNullOrEmpty(productID)) ? pc : productID)})
                                  AND NVL(cqt.C_Invoiceline_ID, 0) = 0 
                                  AND TRUNC(cqt.movementdate) < {GlobalVariable.TO_DATE(DateFrom, true)}
                            )
                            WHERE EXISTS (
                                SELECT 1
                                FROM M_CostQueueTransaction cqt
                                WHERE " + (M_AttributeSetInstance_ID > 0 ? $" M_AttributeSetInstance_ID = {M_AttributeSetInstance_ID} AND " : "") +
                                  $@"cq.M_CostQueue_ID = cqt.M_CostQueue_ID
                                  AND cq.M_Product_ID IN ({((!String.IsNullOrEmpty(productCategoryID) && String.IsNullOrEmpty(productID)) ? pc : productID)})
                                  AND NVL(cqt.C_Invoiceline_ID, 0) = 0 
                                  AND TRUNC(cqt.movementdate) < {GlobalVariable.TO_DATE(DateFrom, true)})");
                int no = DB.ExecuteQuery(sql.ToString(), null, Get_Trx());
            }
            return true;
        }

        public int GetStandardCostElement()
        {
            int M_CostElement_ID = 0;
            // get Costing element id where Costing Method is Standard Costing
            if (DateFrom == null)
            {
                sql.Clear();
                sql.Append(@"SELECT M_CostElement_ID FROM M_CostElement ce ");
                sql.Append($@" WHERE CostingMethod IN ('{X_M_Product_Category.COSTINGMETHOD_StandardCosting}')");
                sql.Append($" AND AD_Client_ID = {GetCtx().GetAD_Client_ID()}");
                M_CostElement_ID = Util.GetValueOfInt(DB.ExecuteScalar(sql.ToString()));
            }
            return M_CostElement_ID;
        }

        /// <summary>
        /// This function is used to delete the entry 
        /// </summary>
        /// <param name="AD_Table_ID">Table ID</param>
        /// <param name="TableName">Table Name</param>
        /// <returns>true, when record deleted</returns>
        private bool DeleteFactDetails(int AD_Table_ID, string TableName)
        {
            if (TableName.Equals(MInOut.Table_Name))
            {
                sql.Clear();
                sql.Append($@"SELECT DISTINCT io.M_InOut_ID FROM M_InOut io 
                                    INNER JOIN M_InOutLine iol ON (io.M_InOut_ID = iol.M_InOut_ID)
                                    INNER JOIN M_Product p ON (p.M_Product_ID = iol.M_Product_ID)");
                if (!String.IsNullOrEmpty(productCategoryID) && String.IsNullOrEmpty(productID))
                {
                    sql.Append($@" WHERE p.M_Product_Category_ID IN ({productCategoryID})");
                }
                else if (!String.IsNullOrEmpty(productID))
                {
                    sql.Append($@" WHERE p.M_Product_ID IN ({productID})");
                }
                else
                {
                    sql.Append($@" WHERE io.AD_Client_ID = ({GetAD_Client_ID()})");
                }
                if (DateFrom != null)
                {
                    sql.Append($@" AND trunc(io.MovementDate) >= {GlobalVariable.TO_DATE(DateFrom, true)} ");
                }
            }
            else if (TableName.Equals(MInvoice.Table_Name))
            {
                sql.Clear();
                sql.Append($@"SELECT DISTINCT io.C_Invoice_ID FROM C_Invoice io 
                                    INNER JOIN C_Invoiceline iol ON (io.C_Invoice_ID = iol.C_Invoice_ID)
                                    INNER JOIN M_Product p ON (p.M_Product_ID = iol.M_Product_ID)");
                if (!String.IsNullOrEmpty(productCategoryID) && String.IsNullOrEmpty(productID))
                {
                    sql.Append($@" WHERE p.M_Product_Category_ID IN ({productCategoryID})");
                }
                else if (!String.IsNullOrEmpty(productID))
                {
                    sql.Append($@" WHERE p.M_Product_ID IN ({productID})");
                }
                else
                {
                    sql.Append($@" WHERE io.AD_Client_ID = ({GetAD_Client_ID()})");
                }
                if (DateFrom != null)
                {
                    sql.Append($@" AND trunc(io.DateAcct) >= {GlobalVariable.TO_DATE(DateFrom, true)} ");
                }
            }
            else if (TableName.Equals(MProvisionalInvoice.Table_Name))
            {
                sql.Clear();
                sql.Append($@"SELECT DISTINCT io.C_ProvisionalInvoice_ID FROM C_ProvisionalInvoice io 
                                    INNER JOIN c_Provisionalinvoiceline iol ON (io.C_ProvisionalInvoice_ID = iol.C_ProvisionalInvoice_ID)
                                    INNER JOIN M_Product p ON (p.M_Product_ID = iol.M_Product_ID)");
                if (!String.IsNullOrEmpty(productCategoryID) && String.IsNullOrEmpty(productID))
                {
                    sql.Append($@" WHERE p.M_Product_Category_ID IN ({productCategoryID})");
                }
                else if (!String.IsNullOrEmpty(productID))
                {
                    sql.Append($@" WHERE p.M_Product_ID IN ({productID})");
                }
                else
                {
                    sql.Append($@" WHERE io.AD_Client_ID = ({GetAD_Client_ID()})");
                }
                if (DateFrom != null)
                {
                    sql.Append($@" AND trunc(io.DateAcct) >= {GlobalVariable.TO_DATE(DateFrom, true)} ");
                }
            }
            else if (TableName.Equals(MInventory.Table_Name))
            {
                sql.Clear();
                sql.Append($@"SELECT DISTINCT io.M_Inventory_ID FROM M_Inventory io 
                                    INNER JOIN M_Inventoryline iol ON (io.M_Inventory_ID = iol.M_Inventory_ID)
                                    INNER JOIN M_Product p ON (p.M_Product_ID = iol.M_Product_ID)");
                if (!String.IsNullOrEmpty(productCategoryID) && String.IsNullOrEmpty(productID))
                {
                    sql.Append($@" WHERE p.M_Product_Category_ID IN ({productCategoryID})");
                }
                else if (!String.IsNullOrEmpty(productID))
                {
                    sql.Append($@" WHERE p.M_Product_ID IN ({productID})");
                }
                else
                {
                    sql.Append($@" WHERE io.AD_Client_ID = ({GetAD_Client_ID()})");
                }
                if (DateFrom != null)
                {
                    sql.Append($@" AND trunc(io.MovementDate) >= {GlobalVariable.TO_DATE(DateFrom, true)} ");
                }
            }
            else if (TableName.Equals("VAFAM_AssetDisposal"))
            {
                sql.Clear();
                sql.Append($@"SELECT DISTINCT io.VAFAM_AssetDisposal_ID FROM VAFAM_AssetDisposal io 
                                    INNER JOIN M_Product p ON (p.M_Product_ID = io.M_Product_ID)");
                if (!String.IsNullOrEmpty(productCategoryID) && String.IsNullOrEmpty(productID))
                {
                    sql.Append($@" WHERE p.M_Product_Category_ID IN ({productCategoryID})");
                }
                else if (!String.IsNullOrEmpty(productID))
                {
                    sql.Append($@" WHERE p.M_Product_ID IN ({productID})");
                }
                else
                {
                    sql.Append($@" WHERE io.AD_Client_ID = ({GetAD_Client_ID()})");
                }
                if (DateFrom != null)
                {
                    sql.Append($@" AND trunc(io.DateAcct) >= {GlobalVariable.TO_DATE(DateFrom, true)} ");
                }
            }
            else if (TableName.Equals(MMovement.Table_Name))
            {
                sql.Clear();
                sql.Append($@"SELECT DISTINCT io.M_Movement_ID FROM M_Movement io 
                                    INNER JOIN M_Movementline iol ON (io.M_Movement_ID = iol.M_Movement_ID)
                                    INNER JOIN M_Product p ON (p.M_Product_ID = iol.M_Product_ID)");
                if (!String.IsNullOrEmpty(productCategoryID) && String.IsNullOrEmpty(productID))
                {
                    sql.Append($@" WHERE p.M_Product_Category_ID IN ({productCategoryID})");
                }
                else if (!String.IsNullOrEmpty(productID))
                {
                    sql.Append($@" WHERE p.M_Product_ID IN ({productID})");
                }
                else
                {
                    sql.Append($@" WHERE io.AD_Client_ID = ({GetAD_Client_ID()})");
                }
                if (DateFrom != null)
                {
                    sql.Append($@" AND trunc(io.MovementDate) >= {GlobalVariable.TO_DATE(DateFrom, true)} ");
                }
            }
            else if (TableName.Equals(X_M_Production.Table_Name))
            {
                sql.Clear();
                sql.Append($@"SELECT DISTINCT io.M_Production_ID FROM M_Production io 
                                    INNER JOIN M_Productionline iol ON (io.M_Production_ID = iol.M_Production_ID)
                                    INNER JOIN M_Product p ON (p.M_Product_ID = iol.M_Product_ID)");
                if (!String.IsNullOrEmpty(productCategoryID) && String.IsNullOrEmpty(productID))
                {
                    sql.Append($@" WHERE p.M_Product_Category_ID IN ({productCategoryID})");
                }
                else if (!String.IsNullOrEmpty(productID))
                {
                    sql.Append($@" WHERE p.M_Product_ID IN ({productID})");
                }
                else
                {
                    sql.Append($@" WHERE io.AD_Client_ID = ({GetAD_Client_ID()})");
                }
                if (DateFrom != null)
                {
                    sql.Append($@" AND trunc(io.MovementDate) >= {GlobalVariable.TO_DATE(DateFrom, true)} ");
                }
            }
            else if (TableName.Equals(MMatchInv.Table_Name))
            {
                sql.Clear();
                sql.Append($@"SELECT DISTINCT io.M_MatchInv_ID FROM M_MatchInv io 
                                    INNER JOIN M_Product p ON (p.M_Product_ID = io.M_Product_ID)");
                if (!String.IsNullOrEmpty(productCategoryID) && String.IsNullOrEmpty(productID))
                {
                    sql.Append($@" WHERE p.M_Product_Category_ID IN ({productCategoryID})");
                }
                else if (!String.IsNullOrEmpty(productID))
                {
                    sql.Append($@" WHERE p.M_Product_ID IN ({productID})");
                }
                else
                {
                    sql.Append($@" WHERE io.AD_Client_ID = ({GetAD_Client_ID()})");
                }
                if (DateFrom != null)
                {
                    sql.Append($@" AND trunc(io.DateAcct) >= {GlobalVariable.TO_DATE(DateFrom, true)} ");
                }
            }
            else if (TableName.Equals("VAMFG_M_WrkOdrTrnsctionLine"))
            {
                sql.Clear();
                sql.Append($@"SELECT DISTINCT io.VAMFG_M_WrkOdrTransaction_ID FROM VAMFG_M_WrkOdrTransaction io 
                                    INNER JOIN VAMFG_M_WrkOdrTrnsctionLine iol ON (io.M_VAMFG_M_WrkOdrTransaction_ID = iol.M_VAMFG_M_WrkOdrTransaction_ID)
                                    INNER JOIN M_Product p ON (p.M_Product_ID = iol.M_Product_ID)");
                if (!String.IsNullOrEmpty(productCategoryID) && String.IsNullOrEmpty(productID))
                {
                    sql.Append($@" WHERE p.M_Product_Category_ID IN ({productCategoryID})");
                }
                else if (!String.IsNullOrEmpty(productID))
                {
                    sql.Append($@" WHERE p.M_Product_ID IN ({productID})");
                }
                else
                {
                    sql.Append($@" WHERE io.AD_Client_ID = ({GetAD_Client_ID()})");
                }
                if (DateFrom != null)
                {
                    sql.Append($@" AND trunc(io.VAMFG_DateAcct) >= {GlobalVariable.TO_DATE(DateFrom, true)} ");
                }
            }
            else if (TableName.Equals("M_VAMFG_M_WrkOdrTransaction"))
            {
                sql.Clear();
                sql.Append($@"SELECT DISTINCT io.VAMFG_M_WrkOdrTransaction_ID FROM VAMFG_M_WrkOdrTransaction io 
                                    INNER JOIN M_Product p ON (p.M_Product_ID = io.M_Product_ID)");
                if (!String.IsNullOrEmpty(productCategoryID) && String.IsNullOrEmpty(productID))
                {
                    sql.Append($@" WHERE p.M_Product_Category_ID IN ({productCategoryID})");
                }
                else if (!String.IsNullOrEmpty(productID))
                {
                    sql.Append($@" WHERE p.M_Product_ID IN ({productID})");
                }
                else
                {
                    sql.Append($@" WHERE io.AD_Client_ID = ({GetAD_Client_ID()})");
                }
                if (DateFrom != null)
                {
                    sql.Append($@" AND trunc(io.VAMFG_DateAcct) >= {GlobalVariable.TO_DATE(DateFrom, true)} ");
                }
            }

            query.Clear();
            query.Append($@"DELETE FROM Fact_Acct ");
            query.Append($@" WHERE AD_Table_ID = {AD_Table_ID} ");
            query.Append($@" AND Record_ID IN ({sql.ToString()})");
            int no = DB.ExecuteQuery(query.ToString(), null, Get_Trx());
            return (no > 0);

        }

        /// <summary>
        /// This function is ued to calculate the Invoice Costing
        /// </summary>
        /// <param name="C_Invoice_ID">Invoice Record ID</param>
        private void CalculateCostForInvoice(int C_Invoice_ID)
        {
            invoice = new MInvoice(GetCtx(), C_Invoice_ID, Get_Trx());

            sql.Clear();
            if (invoice.GetDescription() != null && invoice.GetDescription().Contains("{->"))
            {
                sql.Append("SELECT * FROM C_InvoiceLine WHERE " + (M_AttributeSetInstance_ID > 0 ? $" M_AttributeSetInstance_ID = {M_AttributeSetInstance_ID} AND " : "") +
                            @"IsActive = 'Y' AND iscostcalculated = 'Y' AND IsReversedCostCalculated = 'N' " +
                            " AND C_Invoice_ID = " + invoice.GetC_Invoice_ID());
                if (!String.IsNullOrEmpty(productCategoryID) && String.IsNullOrEmpty(productID))
                {
                    sql.Append(" AND M_Product_ID IN (SELECT M_Product_ID FROM M_Product WHERE M_Product_Category_ID IN (" + productCategoryID + " ) ) ");
                }
                else
                {
                    sql.Append(" AND M_Product_ID IN (" + productID + " )");
                }
                sql.Append(" ORDER BY Line");
            }
            else
            {
                sql.Append("SELECT * FROM C_InvoiceLine WHERE " + (M_AttributeSetInstance_ID > 0 ? $" M_AttributeSetInstance_ID = {M_AttributeSetInstance_ID} AND " : "") +
                            @"IsActive = 'Y' AND iscostcalculated = 'N' " +
                            " AND C_Invoice_ID = " + invoice.GetC_Invoice_ID());
                sql.Append(@" AND IsCostimmediate = 'N' ");
                if (!String.IsNullOrEmpty(productCategoryID) && String.IsNullOrEmpty(productID))
                {
                    sql.Append(" AND M_Product_ID IN (SELECT M_Product_ID FROM M_Product WHERE M_Product_Category_ID IN (" + productCategoryID + " ) ) ");
                }
                else
                {
                    sql.Append(" AND M_Product_ID IN (" + productID + " )");
                }
                sql.Append(" ORDER BY Line");
            }
            dsChildRecord = DB.ExecuteDataset(sql.ToString(), null, Get_Trx());
            if (dsChildRecord != null && dsChildRecord.Tables.Count > 0 && dsChildRecord.Tables[0].Rows.Count > 0)
            {
                /*Costing Object*/
                costingCheck = new CostingCheck(GetCtx());
                costingCheck.dsAccountingSchema = costingCheck.GetAccountingSchema(GetAD_Client_ID());
                costingCheck.invoice = invoice;
                costingCheck.isReversal = invoice.IsReversal();
                for (int j = 0; j < dsChildRecord.Tables[0].Rows.Count; j++)
                {
                    try
                    {
                        //VIS_0045: Reset Class parameters
                        if (costingCheck != null)
                        {
                            costingCheck.ResetProperty();
                        }
                        costingCheck.AD_Org_ID = invoice.GetAD_Org_ID();
                        costingCheck.movementDate = invoice.GetDateAcct();
                        costingCheck.isReversal = invoice.IsReversal();

                        product = new MProduct(GetCtx(), Util.GetValueOfInt(dsChildRecord.Tables[0].Rows[j]["M_Product_ID"]), Get_Trx());
                        invoiceLine = new MInvoiceLine(GetCtx(), dsChildRecord.Tables[0].Rows[j], Get_Trx());

                        // when qtyInvoice is ZERO, then return
                        if (invoiceLine != null && invoiceLine.GetC_Invoice_ID() > 0 && invoiceLine.GetQtyInvoiced() == 0)
                            continue;

                        if (invoiceLine != null && invoiceLine.Get_ID() > 0)
                        {
                            ProductInvoiceLineCost = invoiceLine.GetProductLineCost(invoiceLine, true);
                        }

                        costingCheck.invoiceline = invoiceLine;
                        costingCheck.product = product;

                        if (invoiceLine.GetC_OrderLine_ID() > 0)
                        {
                            if (invoiceLine.GetC_Charge_ID() > 0)
                            {
                                #region Landed Cost Allocation
                                if (!invoice.IsSOTrx() && !invoice.IsReturnTrx())
                                {
                                    if (!MCostQueue.CreateProductCostsDetails(GetCtx(), invoice.GetAD_Client_ID(), invoice.GetAD_Org_ID(), null,
                                        0, "Invoice(Vendor)", null, null, null, invoiceLine, null, ProductInvoiceLineCost, 0, Get_Trx(),
                                        costingCheck, out conversionNotFoundInvoice, optionalstr: "window"))
                                    {
                                        if (!conversionNotFoundInvoice1.Contains(conversionNotFoundInvoice))
                                        {
                                            conversionNotFoundInvoice1 += conversionNotFoundInvoice + " , ";
                                        }
                                        _log.Info("Cost not Calculated for Invoice(Vendor) for this Line ID = "
                                            + invoiceLine.GetC_InvoiceLine_ID() +
                                           " , " + (!string.IsNullOrEmpty(costingCheck.errorMessage) ? costingCheck.errorMessage : ""));
                                    }
                                    else
                                    {
                                        queryTo.Clear();
                                        queryTo.Append(" UPDATE C_InvoiceLine SET IsCostImmediate = 'Y' ");
                                        //if (invoice.GetDescription() != null && invoice.GetDescription().Contains("{->"))
                                        //{
                                        //    queryTo.Append(" , IsReversedCostCalculated = 'Y'");
                                        //}
                                        //queryTo.Append(" , IsCostCalculated = 'Y'");
                                        queryTo.Append($" WHERE C_InvoiceLine_ID = {invoiceLine.GetC_InvoiceLine_ID()}");
                                        DB.ExecuteQuery(queryTo.ToString(), null, Get_Trx());
                                        _log.Fine("Cost Calculation updated for m_invoiceline = " + invoiceLine.GetC_InvoiceLine_ID());
                                        Get_Trx().Commit();
                                    }
                                }
                                #endregion
                            }
                            else
                            {
                                #region for Expense type product
                                if (product.GetProductType() == "E" && product.GetM_Product_ID() > 0)
                                {
                                    if (!MCostQueue.CreateProductCostsDetails(GetCtx(), invoice.GetAD_Client_ID(), invoice.GetAD_Org_ID(), product, 0,
                                         "Invoice(Vendor)", null, null, null, invoiceLine, null, ProductInvoiceLineCost, 0, Get_Trx(),
                                         costingCheck, out conversionNotFoundInvoice, optionalstr: "window"))
                                    {
                                        if (!conversionNotFoundInvoice1.Contains(conversionNotFoundInvoice))
                                        {
                                            conversionNotFoundInvoice1 += conversionNotFoundInvoice + " , ";
                                        }
                                        _log.Info("Cost not Calculated for Invoice(Vendor) for this Line ID = "
                                            + invoiceLine.GetC_InvoiceLine_ID() +
                                         " , " + (!string.IsNullOrEmpty(costingCheck.errorMessage) ? costingCheck.errorMessage : ""));
                                    }
                                    else
                                    {
                                        if (invoice.GetDescription() != null && invoice.GetDescription().Contains("{->"))
                                        {
                                            //    invoiceLine.SetIsReversedCostCalculated(true);
                                        }
                                        //invoiceLine.SetIsCostCalculated(true);
                                        //if (client.IsCostImmediate() && !invoiceLine.IsCostImmediate())
                                        {
                                            invoiceLine.SetIsCostImmediate(true);
                                        }
                                        if (!invoiceLine.Save(Get_Trx()))
                                        {
                                            ValueNamePair pp = VLogger.RetrieveError();
                                            _log.Info("Error found for saving Invoice(Vendor) for this Line ID = " + invoiceLine.GetC_InvoiceLine_ID() +
                                                       " Error Name is " + pp.GetName() + " And Error Type is " + pp.GetType());
                                            Get_Trx().Rollback();
                                        }
                                        else
                                        {
                                            _log.Fine("Cost Calculation updated for m_invoiceline = " + invoiceLine.GetC_InvoiceLine_ID());
                                            Get_Trx().Commit();
                                        }
                                    }
                                }
                                #endregion

                                #region  for Item Type product
                                else if (product.GetProductType() == "I" && product.GetM_Product_ID() > 0)
                                {
                                    if (countColumnExist > 0)
                                    {
                                        isCostAdjustableOnLost = product.IsCostAdjustmentOnLost();
                                    }
                                    MOrderLine ol1 = null;
                                    MOrder order1 = new MOrder(GetCtx(), invoice.GetC_Order_ID(), Get_Trx());
                                    ol1 = new MOrderLine(GetCtx(), invoiceLine.GetC_OrderLine_ID(), Get_Trx());
                                    if (ol1 != null && ol1.Get_ID() > 0)
                                    {
                                        ProductOrderLineCost = ol1.GetProductLineCost(ol1);
                                        ProductOrderPriceActual = ProductOrderLineCost / ol1.GetQtyEntered();
                                    }
                                    if (order1.GetC_Order_ID() == 0 || order1.GetC_Order_ID() != ol1.GetC_Order_ID())
                                    {
                                        order1 = new MOrder(GetCtx(), ol1.GetC_Order_ID(), Get_Trx());
                                    }

                                    costingCheck.order = order1;
                                    costingCheck.orderline = ol1;

                                    #region  Sales Cycle
                                    if (order1.IsSOTrx() && !order1.IsReturnTrx())
                                    {
                                        if (!MCostQueue.CreateProductCostsDetails(GetCtx(), invoice.GetAD_Client_ID(), invoice.GetAD_Org_ID(), product, invoiceLine.GetM_AttributeSetInstance_ID(),
                                              "Invoice(Customer)", null, null, null, invoiceLine, null, Decimal.Negate(ProductInvoiceLineCost), Decimal.Negate(invoiceLine.GetQtyInvoiced()),
                                              Get_Trx(), costingCheck, out conversionNotFoundInvoice, optionalstr: "window"))
                                        {
                                            if (!conversionNotFoundInvoice1.Contains(conversionNotFoundInvoice))
                                            {
                                                conversionNotFoundInvoice1 += conversionNotFoundInvoice + " , ";
                                            }
                                            _log.Info("Cost not Calculated for Invoice(Vendor) for this Line ID = "
                                                + invoiceLine.GetC_InvoiceLine_ID() +
                                             " , " + (!string.IsNullOrEmpty(costingCheck.errorMessage) ? costingCheck.errorMessage : ""));
                                        }
                                        else
                                        {
                                            if (invoice.GetDescription() != null && invoice.GetDescription().Contains("{->"))
                                            {
                                                //invoiceLine.SetIsReversedCostCalculated(true);
                                            }
                                            if (invoiceLine.GetM_InOutLine_ID() > 0)
                                            {
                                                DataSet ds = DB.ExecuteDataset(@"SELECT M_InOutLine.CurrentCostPrice, M_InOut.M_Warehouse_ID 
                                                                                    FROM M_InOutLine INNER JOIN M_InOut ON M_InOut.M_InOut_ID = M_InOutLine.M_InOut_ID
                                                                                    WHERE M_InOutLine.M_InOutLIne_ID = " + invoiceLine.GetM_InOutLine_ID(), null, Get_Trx());
                                                if (ds != null && ds.Tables.Count > 0 && ds.Tables[0].Rows.Count > 0)
                                                {
                                                    if (IsCostUpdation)
                                                    {
                                                        invoiceLine.SetCurrentCostPrice(Util.GetValueOfDecimal(ds.Tables[0].Rows[0]["CurrentCostPrice"]));
                                                    }

                                                    currentCostPrice = MCost.GetproductCosts(invoiceLine.GetAD_Client_ID(), invoiceLine.GetAD_Org_ID(),
                                                                               invoiceLine.GetM_Product_ID(), invoiceLine.GetM_AttributeSetInstance_ID(), Get_Trx(),
                                                                               Util.GetValueOfInt(ds.Tables[0].Rows[0]["M_Warehouse_ID"]));
                                                    if (IsCostUpdation)
                                                    {
                                                        invoiceLine.SetPostCurrentCostPrice(currentCostPrice);
                                                    }
                                                }
                                            }
                                            //invoiceLine.SetIsCostCalculated(true);
                                            //if (client.IsCostImmediate() && !invoiceLine.IsCostImmediate())
                                            {
                                                invoiceLine.SetIsCostImmediate(true);
                                            }
                                            if (!invoiceLine.Save(Get_Trx()))
                                            {
                                                ValueNamePair pp = VLogger.RetrieveError();
                                                _log.Info("Error found for saving Invoice(Vendor) for this Line ID = " + invoiceLine.GetC_InvoiceLine_ID() +
                                                           " Error Name is " + pp.GetName() + " And Error Type is " + pp.GetType());
                                                Get_Trx().Rollback();
                                            }
                                            else
                                            {
                                                _log.Fine("Cost Calculation updated for m_invoiceline = " + invoiceLine.GetC_InvoiceLine_ID());
                                                Get_Trx().Commit();
                                            }
                                        }
                                    }
                                    #endregion

                                    #region CRMA
                                    else if (order1.IsSOTrx() && order1.IsReturnTrx())
                                    {
                                        if (!MCostQueue.CreateProductCostsDetails(GetCtx(), invoice.GetAD_Client_ID(), invoice.GetAD_Org_ID(), product, invoiceLine.GetM_AttributeSetInstance_ID(),
                                          "Invoice(Customer)", null, null, null, invoiceLine, null, ProductInvoiceLineCost,
                                          invoiceLine.GetQtyInvoiced(), Get_Trx(), costingCheck, out conversionNotFoundInvoice, optionalstr: "window"))
                                        {
                                            if (!conversionNotFoundInvoice1.Contains(conversionNotFoundInvoice))
                                            {
                                                conversionNotFoundInvoice1 += conversionNotFoundInvoice + " , ";
                                            }
                                            _log.Info("Cost not Calculated for Invoice(Customer) for this Line ID = "
                                                + invoiceLine.GetC_InvoiceLine_ID() +
                                            " , " + (!string.IsNullOrEmpty(costingCheck.errorMessage) ? costingCheck.errorMessage : ""));
                                        }
                                        else
                                        {
                                            if (invoice.GetDescription() != null && invoice.GetDescription().Contains("{->"))
                                            {
                                                //invoiceLine.SetIsReversedCostCalculated(true);
                                            }
                                            if (invoiceLine.GetM_InOutLine_ID() > 0)
                                            {
                                                DataSet ds = DB.ExecuteDataset(@"SELECT M_InOutLine.CurrentCostPrice, M_InOut.M_Warehouse_ID 
                                                                                    FROM M_InOutLine INNER JOIN M_InOut ON M_InOut.M_InOut_ID = M_InOutLine.M_InOut_ID
                                                                                    WHERE M_InOutLine.M_InOutLIne_ID = " + invoiceLine.GetM_InOutLine_ID(), null, Get_Trx());
                                                if (ds != null && ds.Tables.Count > 0 && ds.Tables[0].Rows.Count > 0)
                                                {
                                                    if (IsCostUpdation)
                                                    {
                                                        invoiceLine.SetCurrentCostPrice(Util.GetValueOfDecimal(ds.Tables[0].Rows[0]["CurrentCostPrice"]));
                                                    }

                                                    currentCostPrice = MCost.GetproductCostAndQtyMaterial(invoiceLine.GetAD_Client_ID(), invoiceLine.GetAD_Org_ID(),
                                                                               invoiceLine.GetM_Product_ID(), invoiceLine.GetM_AttributeSetInstance_ID(), Get_Trx(),
                                                                               Util.GetValueOfInt(ds.Tables[0].Rows[0]["M_Warehouse_ID"]), false);
                                                    if (IsCostUpdation)
                                                    {
                                                        invoiceLine.SetPostCurrentCostPrice(currentCostPrice);
                                                    }
                                                }
                                            }
                                            //invoiceLine.SetIsCostCalculated(true);
                                            //if (client.IsCostImmediate() && !invoiceLine.IsCostImmediate())
                                            {
                                                invoiceLine.SetIsCostImmediate(true);
                                            }
                                            if (!invoiceLine.Save(Get_Trx()))
                                            {
                                                ValueNamePair pp = VLogger.RetrieveError();
                                                _log.Info("Error found for saving Invoice(Customer) for this Line ID = " + invoiceLine.GetC_InvoiceLine_ID() +
                                                           " Error Name is " + pp.GetName() + " And Error Type is " + pp.GetType());
                                                Get_Trx().Rollback();
                                            }
                                            else
                                            {
                                                _log.Fine("Cost Calculation updated for m_invoiceline = " + invoiceLine.GetC_InvoiceLine_ID());
                                                Get_Trx().Commit();
                                            }
                                        }
                                    }
                                    #endregion

                                    #region VRMA
                                    else if (!order1.IsSOTrx() && order1.IsReturnTrx())
                                    {
                                        //change 12-5-2016
                                        // when Ap Credit memo is alone then we will do a impact on costing.
                                        // this is bcz of giving discount for particular product
                                        // discount is given only when document type having setting as "Treat As Discount" = True
                                        MDocType docType = new MDocType(GetCtx(), invoice.GetC_DocTypeTarget_ID(), Get_Trx());
                                        if (docType.GetDocBaseType() == "APC" && docType.IsTreatAsDiscount() && invoiceLine.GetC_OrderLine_ID() == 0 && invoiceLine.GetM_InOutLine_ID() == 0 && invoiceLine.GetM_Product_ID() > 0)
                                        {
                                            query.Clear();
                                            query.Append($@"SELECT NVL(iol.MovementQty, 0) AS MovementQty, iol.M_Locator_ID, io.M_Warehouse_ID FROM C_InvoiceLine il
                                                                        INNER JOIN M_InoutLine iol ON (il.M_InoutLine_ID = iol.M_InoutLine_ID)
                                                                        INNER JOIN M_Inout io ON (io.M_InOut_ID = iol.M_InOut_ID)
                                                                        WHERE il.C_InvoiceLine_ID =  { invoiceLine.Get_ValueAsInt("Ref_InvoiceLineOrg_ID")}");
                                            DataSet dsRefInOut = DB.ExecuteDataset(query.ToString(), null, Get_Trx());
                                            if (dsRefInOut != null && dsRefInOut.Tables.Count > 0 && dsRefInOut.Tables[0].Rows.Count > 0)
                                            {
                                                costingCheck.M_Warehouse_ID = invoiceLine.GetM_Warehouse_ID() > 0 ? invoiceLine.GetM_Warehouse_ID() :
                                                                    (invoice.GetM_Warehouse_ID() > 0 ? invoice.GetM_Warehouse_ID() : Util.GetValueOfInt(dsRefInOut.Tables[0].Rows[0]["M_Warehouse_ID"]));
                                            }

                                            if (!MCostQueue.CreateProductCostsDetails(GetCtx(), invoice.GetAD_Client_ID(), invoice.GetAD_Org_ID(), product, invoiceLine.GetM_AttributeSetInstance_ID(),
                                              "Invoice(Vendor)", null, null, null, invoiceLine, null, Decimal.Negate(ProductInvoiceLineCost), Decimal.Negate(invoiceLine.GetQtyInvoiced()),
                                              Get_Trx(), costingCheck, out conversionNotFoundInvoice, optionalstr: "window"))
                                            {
                                                if (!conversionNotFoundInvoice1.Contains(conversionNotFoundInvoice))
                                                {
                                                    conversionNotFoundInvoice1 += conversionNotFoundInvoice + " , ";
                                                }
                                                _log.Info("Cost not Calculated for Invoice(Vendor) for this Line ID = "
                                                    + invoiceLine.GetC_InvoiceLine_ID() +
                                                " , " + (!string.IsNullOrEmpty(costingCheck.errorMessage) ? costingCheck.errorMessage : ""));
                                            }
                                            else
                                            {

                                                int M_Locator_ID = 0;
                                                int M_Warehouse_ID = invoiceLine.GetM_Warehouse_ID() > 0 ? invoiceLine.GetM_Warehouse_ID() : invoice.GetM_Warehouse_ID();
                                                if (dsRefInOut != null && dsRefInOut.Tables.Count > 0 && dsRefInOut.Tables[0].Rows.Count > 0)
                                                {
                                                    M_Locator_ID = Util.GetValueOfInt(dsRefInOut.Tables[0].Rows[0]["M_Locator_ID"]);
                                                    if (M_Warehouse_ID == 0)
                                                    {
                                                        M_Warehouse_ID = Util.GetValueOfInt(dsRefInOut.Tables[0].Rows[0]["M_Warehouse_ID"]);
                                                    }
                                                }

                                                if (invoice.GetDescription() != null && invoice.GetDescription().Contains("{->"))
                                                {
                                                    //invoiceLine.SetIsReversedCostCalculated(true);
                                                }
                                                if (invoiceLine.Get_ColumnIndex("PostCurrentCostPrice") >= 0 && invoiceLine.GetPostCurrentCostPrice() == 0)
                                                {
                                                    // get post cost after invoice cost calculation and update on invoice
                                                    currentCostPrice = MCost.GetproductCosts(invoiceLine.GetAD_Client_ID(), invoiceLine.GetAD_Org_ID(),
                                                                                                    product.GetM_Product_ID(), invoiceLine.GetM_AttributeSetInstance_ID(), Get_Trx(), M_Warehouse_ID);
                                                    if (IsCostUpdation)
                                                    {
                                                        invoiceLine.SetPostCurrentCostPrice(currentCostPrice);
                                                    }
                                                }
                                                //invoiceLine.SetIsCostCalculated(true);
                                                //if (client.IsCostImmediate() && !invoiceLine.IsCostImmediate())
                                                {
                                                    invoiceLine.SetIsCostImmediate(true);
                                                }
                                                if (!invoiceLine.Save(Get_Trx()))
                                                {
                                                    ValueNamePair pp = VLogger.RetrieveError();
                                                    _log.Info("Error found for saving Invoice(Vendor) for this Line ID = " + invoiceLine.GetC_InvoiceLine_ID() +
                                                               " Error Name is " + pp.GetName() + " And Error Type is " + pp.GetType());
                                                    Get_Trx().Rollback();
                                                }
                                                else
                                                {
                                                    _log.Fine("Cost Calculation updated for m_invoiceline = " + invoiceLine.GetC_InvoiceLine_ID());
                                                    Get_Trx().Commit();
                                                }

                                                // Update Product Cost on Product Transaction for the Invoice Line
                                                UpdateTransactionCostForInvoice(currentCostPrice, invoiceLine.GetC_InvoiceLine_ID(), costingCheck);
                                                Get_Trx().Commit();
                                            }
                                        }
                                    }
                                    #endregion
                                }
                                #endregion
                            }
                        }
                        else
                        {
                            #region for Landed Cost Allocation
                            if (invoiceLine.GetC_Charge_ID() > 0)
                            {
                                if (!invoice.IsSOTrx() && !invoice.IsReturnTrx())
                                {
                                    if (!MCostQueue.CreateProductCostsDetails(GetCtx(), invoice.GetAD_Client_ID(), invoice.GetAD_Org_ID(), null, 0,
                                        "Invoice(Vendor)", null, null, null, invoiceLine, null, ProductInvoiceLineCost, 0, Get_TrxName(),
                                        costingCheck, out conversionNotFoundInvoice, optionalstr: "window"))
                                    {
                                        if (!conversionNotFoundInvoice1.Contains(conversionNotFoundInvoice))
                                        {
                                            conversionNotFoundInvoice1 += conversionNotFoundInvoice + " , ";
                                        }
                                        _log.Info("Cost not Calculated for Invoice(Vendor) for this Line ID = " + invoiceLine.GetC_InvoiceLine_ID() +
                                             " , " + (!string.IsNullOrEmpty(costingCheck.errorMessage) ? costingCheck.errorMessage : ""));
                                    }
                                    else
                                    {
                                        if (invoice.GetDescription() != null && invoice.GetDescription().Contains("{->"))
                                        {
                                            //invoiceLine.SetIsReversedCostCalculated(true);
                                        }
                                        //invoiceLine.SetIsCostCalculated(true);
                                        //if (client.IsCostImmediate() && !invoiceLine.IsCostImmediate())
                                        {
                                            invoiceLine.SetIsCostImmediate(true);
                                        }
                                        if (!invoiceLine.Save(Get_Trx()))
                                        {
                                            ValueNamePair pp = VLogger.RetrieveError();
                                            _log.Info("Error found for saving Invoice(Vendor) for this Line ID = " + invoiceLine.GetC_InvoiceLine_ID() +
                                                       " Error Name is " + pp.GetName() + " And Error Type is " + pp.GetType());
                                            Get_Trx().Rollback();
                                        }
                                        else
                                        {
                                            _log.Fine("Cost Calculation updated for m_invoiceline = " + invoiceLine.GetC_InvoiceLine_ID());
                                            Get_Trx().Commit();
                                        }
                                    }
                                }
                            }
                            #endregion

                            #region for Expense type product
                            if (product.GetProductType() == "E" && product.GetM_Product_ID() > 0)
                            {
                                if (!MCostQueue.CreateProductCostsDetails(GetCtx(), invoice.GetAD_Client_ID(), invoice.GetAD_Org_ID(), product, 0,
                                    "Invoice(Vendor)", null, null, null, invoiceLine, null, ProductInvoiceLineCost, 0, Get_TrxName(),
                                    costingCheck, out conversionNotFoundInvoice, optionalstr: "window"))
                                {
                                    if (!conversionNotFoundInvoice1.Contains(conversionNotFoundInvoice))
                                    {
                                        conversionNotFoundInvoice1 += conversionNotFoundInvoice + " , ";
                                    }
                                    _log.Info("Cost not Calculated for Invoice(Vendor) for this Line ID = " + invoiceLine.GetC_InvoiceLine_ID() +
                                             " , " + (!string.IsNullOrEmpty(costingCheck.errorMessage) ? costingCheck.errorMessage : ""));
                                }
                                else
                                {
                                    if (invoice.GetDescription() != null && invoice.GetDescription().Contains("{->"))
                                    {
                                        //invoiceLine.SetIsReversedCostCalculated(true);
                                    }
                                    //invoiceLine.SetIsCostCalculated(true);
                                    //if (client.IsCostImmediate() && !invoiceLine.IsCostImmediate())
                                    {
                                        invoiceLine.SetIsCostImmediate(true);
                                    }
                                    if (!invoiceLine.Save(Get_Trx()))
                                    {
                                        ValueNamePair pp = VLogger.RetrieveError();
                                        _log.Info("Error found for saving Invoice(Vendor) for this Line ID = " + invoiceLine.GetC_InvoiceLine_ID() +
                                                   " Error Name is " + pp.GetName() + " And Error Type is " + pp.GetType());
                                        Get_Trx().Rollback();
                                    }
                                    else
                                    {
                                        _log.Fine("Cost Calculation updated for m_invoiceline = " + invoiceLine.GetC_InvoiceLine_ID());
                                        Get_Trx().Commit();
                                    }
                                }
                            }
                            #endregion

                            #region  for Item Type product
                            else if (product.GetProductType() == "I" && product.GetM_Product_ID() > 0)
                            {
                                if (countColumnExist > 0)
                                {
                                    isCostAdjustableOnLost = product.IsCostAdjustmentOnLost();
                                }

                                #region Sales Order
                                if (invoice.IsSOTrx() && !invoice.IsReturnTrx())
                                {
                                    if (!MCostQueue.CreateProductCostsDetails(GetCtx(), invoice.GetAD_Client_ID(), invoice.GetAD_Org_ID(), product, invoiceLine.GetM_AttributeSetInstance_ID(),
                                          "Invoice(Customer)", null, null, null, invoiceLine, null, Decimal.Negate(ProductInvoiceLineCost), Decimal.Negate(invoiceLine.GetQtyInvoiced()),
                                          Get_Trx(), costingCheck, out conversionNotFoundInvoice, optionalstr: "window"))
                                    {
                                        if (!conversionNotFoundInvoice1.Contains(conversionNotFoundInvoice))
                                        {
                                            conversionNotFoundInvoice1 += conversionNotFoundInvoice + " , ";
                                        }
                                        _log.Info("Cost not Calculated for Invoice(Vendor) for this Line ID = " + invoiceLine.GetC_InvoiceLine_ID() +
                                             " , " + (!string.IsNullOrEmpty(costingCheck.errorMessage) ? costingCheck.errorMessage : ""));
                                    }
                                    else
                                    {
                                        if (invoice.GetDescription() != null && invoice.GetDescription().Contains("{->"))
                                        {
                                            //invoiceLine.SetIsReversedCostCalculated(true);
                                        }
                                        if (invoiceLine.GetM_InOutLine_ID() > 0)
                                        {
                                            DataSet ds = DB.ExecuteDataset(@"SELECT M_InOutLine.CurrentCostPrice, M_InOut.M_Warehouse_ID 
                                                                                FROM M_InOutLine INNER JOIN M_InOut ON M_InOut.M_InOut_ID = M_InOutLine.M_InOut_ID
                                                                                WHERE M_InOutLine.M_InOutLIne_ID = " + invoiceLine.GetM_InOutLine_ID(), null, Get_Trx());
                                            if (ds != null && ds.Tables.Count > 0 && ds.Tables[0].Rows.Count > 0)
                                            {
                                                if (IsCostUpdation)
                                                {
                                                    invoiceLine.SetCurrentCostPrice(Util.GetValueOfDecimal(ds.Tables[0].Rows[0]["CurrentCostPrice"]));
                                                }

                                                currentCostPrice = MCost.GetproductCostAndQtyMaterial(invoiceLine.GetAD_Client_ID(), invoiceLine.GetAD_Org_ID(),
                                                                           invoiceLine.GetM_Product_ID(), invoiceLine.GetM_AttributeSetInstance_ID(), Get_Trx(),
                                                                           Util.GetValueOfInt(ds.Tables[0].Rows[0]["M_Warehouse_ID"]), false);
                                                if (IsCostUpdation)
                                                {
                                                    invoiceLine.SetPostCurrentCostPrice(currentCostPrice);
                                                }
                                            }
                                        }
                                        //invoiceLine.SetIsCostCalculated(true);
                                        //if (client.IsCostImmediate() && !invoiceLine.IsCostImmediate())
                                        {
                                            invoiceLine.SetIsCostImmediate(true);
                                        }
                                        if (!invoiceLine.Save(Get_Trx()))
                                        {
                                            ValueNamePair pp = VLogger.RetrieveError();
                                            _log.Info("Error found for saving Invoice(Vendor) for this Line ID = " + invoiceLine.GetC_InvoiceLine_ID() +
                                                       " Error Name is " + pp.GetName() + " And Error Type is " + pp.GetType());
                                            Get_Trx().Rollback();
                                        }
                                        else
                                        {
                                            _log.Fine("Cost Calculation updated for m_invoiceline = " + invoiceLine.GetC_InvoiceLine_ID());
                                            Get_Trx().Commit();
                                        }
                                    }
                                }
                                #endregion

                                #region CRMA
                                else if (invoice.IsSOTrx() && invoice.IsReturnTrx())
                                {
                                    if (!MCostQueue.CreateProductCostsDetails(GetCtx(), invoice.GetAD_Client_ID(), invoice.GetAD_Org_ID(), product, invoiceLine.GetM_AttributeSetInstance_ID(),
                                      "Invoice(Customer)", null, null, null, invoiceLine, null, ProductInvoiceLineCost, invoiceLine.GetQtyInvoiced(),
                                      Get_Trx(), costingCheck, out conversionNotFoundInvoice, optionalstr: "window"))
                                    {
                                        if (!conversionNotFoundInvoice1.Contains(conversionNotFoundInvoice))
                                        {
                                            conversionNotFoundInvoice1 += conversionNotFoundInvoice + " , ";
                                        }
                                        _log.Info("Cost not Calculated for Invoice(Customer) for this Line ID = " + invoiceLine.GetC_InvoiceLine_ID() +
                                             " , " + (!string.IsNullOrEmpty(costingCheck.errorMessage) ? costingCheck.errorMessage : ""));
                                    }
                                    else
                                    {
                                        if (invoice.GetDescription() != null && invoice.GetDescription().Contains("{->"))
                                        {
                                            //invoiceLine.SetIsReversedCostCalculated(true);
                                        }
                                        if (invoiceLine.GetM_InOutLine_ID() > 0)
                                        {
                                            DataSet ds = DB.ExecuteDataset(@"SELECT M_InOutLine.CurrentCostPrice, M_InOut.M_Warehouse_ID 
                                                                                FROM M_InOutLine INNER JOIN M_InOut ON M_InOut.M_InOut_ID = M_InOutLine.M_InOut_ID
                                                                                WHERE M_InOutLine.M_InOutLIne_ID = " + invoiceLine.GetM_InOutLine_ID(), null, Get_Trx());
                                            if (ds != null && ds.Tables.Count > 0 && ds.Tables[0].Rows.Count > 0)
                                            {
                                                if (IsCostUpdation)
                                                {
                                                    invoiceLine.SetCurrentCostPrice(Util.GetValueOfDecimal(ds.Tables[0].Rows[0]["CurrentCostPrice"]));
                                                }

                                                currentCostPrice = MCost.GetproductCostAndQtyMaterial(invoiceLine.GetAD_Client_ID(), invoiceLine.GetAD_Org_ID(),
                                                                           invoiceLine.GetM_Product_ID(), invoiceLine.GetM_AttributeSetInstance_ID(), Get_Trx(),
                                                                           Util.GetValueOfInt(ds.Tables[0].Rows[0]["M_Warehouse_ID"]), false);
                                                if (IsCostUpdation)
                                                {
                                                    invoiceLine.SetPostCurrentCostPrice(currentCostPrice);
                                                }
                                            }
                                        }
                                        //invoiceLine.SetIsCostCalculated(true);
                                        //if (client.IsCostImmediate() && !invoiceLine.IsCostImmediate())
                                        {
                                            invoiceLine.SetIsCostImmediate(true);
                                        }
                                        if (!invoiceLine.Save(Get_Trx()))
                                        {
                                            ValueNamePair pp = VLogger.RetrieveError();
                                            _log.Info("Error found for saving Invoice(Vendor) for this Line ID = " + invoiceLine.GetC_InvoiceLine_ID() +
                                                       " Error Name is " + pp.GetName() + " And Error Type is " + pp.GetType());
                                            Get_Trx().Rollback();
                                        }
                                        else
                                        {
                                            _log.Fine("Cost Calculation updated for m_invoiceline = " + invoiceLine.GetC_InvoiceLine_ID());
                                            Get_Trx().Commit();
                                        }
                                    }
                                }
                                #endregion

                                #region VRMA
                                else if (!invoice.IsSOTrx() && invoice.IsReturnTrx())
                                {
                                    // when Ap Credit memo is alone then we will do a impact on costing.
                                    // this is bcz of giving discount for particular product
                                    // discount is given only when document type having setting as "Treat As Discount" = True
                                    MDocType docType = new MDocType(GetCtx(), invoice.GetC_DocTypeTarget_ID(), Get_Trx());
                                    if (docType.GetDocBaseType() == "APC" && docType.IsTreatAsDiscount() && invoiceLine.GetC_OrderLine_ID() == 0 && invoiceLine.GetM_InOutLine_ID() == 0 && invoiceLine.GetM_Product_ID() > 0)
                                    {
                                        query.Clear();
                                        query.Append($@"SELECT NVL(iol.MovementQty, 0) AS MovementQty, iol.M_Locator_ID, io.M_Warehouse_ID FROM C_InvoiceLine il
                                                                        INNER JOIN M_InoutLine iol ON (il.M_InoutLine_ID = iol.M_InoutLine_ID)
                                                                        INNER JOIN M_Inout io ON (io.M_InOut_ID = iol.M_InOut_ID)
                                                                        WHERE il.C_InvoiceLine_ID =  { invoiceLine.Get_ValueAsInt("Ref_InvoiceLineOrg_ID")}");
                                        DataSet dsRefInOut = DB.ExecuteDataset(query.ToString(), null, Get_Trx());
                                        if (dsRefInOut != null && dsRefInOut.Tables.Count > 0 && dsRefInOut.Tables[0].Rows.Count > 0)
                                        {
                                            costingCheck.M_Warehouse_ID = invoiceLine.GetM_Warehouse_ID() > 0 ? invoiceLine.GetM_Warehouse_ID() :
                                                                (invoice.GetM_Warehouse_ID() > 0 ? invoice.GetM_Warehouse_ID() : Util.GetValueOfInt(dsRefInOut.Tables[0].Rows[0]["M_Warehouse_ID"]));
                                        }


                                        if (!MCostQueue.CreateProductCostsDetails(GetCtx(), invoice.GetAD_Client_ID(), invoice.GetAD_Org_ID(), product, invoiceLine.GetM_AttributeSetInstance_ID(),
                                          "Invoice(Vendor)", null, null, null, invoiceLine, null, Decimal.Negate(ProductInvoiceLineCost), Decimal.Negate(invoiceLine.GetQtyInvoiced()),
                                          Get_Trx(), costingCheck, out conversionNotFoundInvoice, optionalstr: "window"))
                                        {
                                            if (!conversionNotFoundInvoice1.Contains(conversionNotFoundInvoice))
                                            {
                                                conversionNotFoundInvoice1 += conversionNotFoundInvoice + " , ";
                                            }
                                            _log.Info("Cost not Calculated for Invoice(Vendor) for this Line ID = " + invoiceLine.GetC_InvoiceLine_ID() +
                                             " , " + (!string.IsNullOrEmpty(costingCheck.errorMessage) ? costingCheck.errorMessage : ""));
                                        }
                                        else
                                        {
                                            int M_Locator_ID = 0;
                                            int M_Warehouse_ID = invoiceLine.GetM_Warehouse_ID() > 0 ? invoiceLine.GetM_Warehouse_ID() : invoice.GetM_Warehouse_ID();
                                            if (dsRefInOut != null && dsRefInOut.Tables.Count > 0 && dsRefInOut.Tables[0].Rows.Count > 0)
                                            {
                                                M_Locator_ID = Util.GetValueOfInt(dsRefInOut.Tables[0].Rows[0]["M_Locator_ID"]);
                                                if (M_Warehouse_ID == 0)
                                                {
                                                    M_Warehouse_ID = Util.GetValueOfInt(dsRefInOut.Tables[0].Rows[0]["M_Warehouse_ID"]);
                                                }
                                            }


                                            if (invoice.GetDescription() != null && invoice.GetDescription().Contains("{->"))
                                            {
                                                //invoiceLine.SetIsReversedCostCalculated(true);
                                            }
                                            if (invoiceLine.Get_ColumnIndex("PostCurrentCostPrice") >= 0 && invoiceLine.GetPostCurrentCostPrice() == 0)
                                            {
                                                // get post cost after invoice cost calculation and update on invoice
                                                currentCostPrice = MCost.GetproductCosts(invoiceLine.GetAD_Client_ID(), invoiceLine.GetAD_Org_ID(),
                                                                   product.GetM_Product_ID(), invoiceLine.GetM_AttributeSetInstance_ID(), Get_Trx(), M_Warehouse_ID);
                                                if (IsCostUpdation)
                                                {
                                                    invoiceLine.SetPostCurrentCostPrice(currentCostPrice);
                                                }
                                            }
                                            //invoiceLine.SetIsCostCalculated(true);
                                            //if (client.IsCostImmediate() && !invoiceLine.IsCostImmediate())
                                            {
                                                invoiceLine.SetIsCostImmediate(true);
                                            }

                                            if (invoiceLine.Get_ColumnIndex("Ref_InvoiceLineOrg_ID") >= 0 && costingCheck.currentQtyonQueue != null)
                                            {
                                                invoiceLine.Set_Value("TotalInventoryAdjustment", Math.Sign(invoiceLine.GetQtyInvoiced()) * Decimal.Round(
                                                 (costingCheck.currentQtyonQueue.Value < Math.Abs(invoiceLine.GetQtyInvoiced()) ?
                                                 costingCheck.currentQtyonQueue.Value : invoiceLine.GetQtyInvoiced())
                                                 * ((invoiceLine.GetQtyEntered() / invoiceLine.GetQtyInvoiced()) * invoiceLine.GetPriceActual()), costingCheck.precision));
                                                invoiceLine.Set_Value("TotalCOGSAdjustment", Math.Sign(invoiceLine.GetQtyInvoiced()) * Decimal.Round
                                                    ((costingCheck.currentQtyonQueue.Value < Math.Abs(invoiceLine.GetQtyInvoiced()) ?
                                                    (Math.Abs(invoiceLine.GetQtyInvoiced()) - costingCheck.currentQtyonQueue.Value) : 0) *
                                                    ((invoiceLine.GetQtyEntered() / invoiceLine.GetQtyInvoiced()) * invoiceLine.GetPriceActual()), costingCheck.precision));
                                            }
                                            else if (invoiceLine.Get_ColumnIndex("Ref_InvoiceLineOrg_ID") >= 0)
                                            {
                                                if (costingCheck.onHandQty == 0)
                                                {
                                                    invoiceLine.Set_Value("TotalCOGSAdjustment", Decimal.Round(
                                                    ((invoiceLine.GetQtyEntered()) * invoiceLine.GetPriceActual()), costingCheck.precision));
                                                }
                                                else
                                                {
                                                    invoiceLine.Set_Value("TotalInventoryAdjustment", Decimal.Round(
                                                    ((invoiceLine.GetQtyEntered()) * invoiceLine.GetPriceActual()), costingCheck.precision));
                                                }
                                            }


                                            if (!invoiceLine.Save(Get_Trx()))
                                            {
                                                ValueNamePair pp = VLogger.RetrieveError();
                                                _log.Info("Error found for saving Invoice(Vendor) for this Line ID = " + invoiceLine.GetC_InvoiceLine_ID() +
                                                           " Error Name is " + pp.GetName() + " And Error Type is " + pp.GetType());
                                                Get_Trx().Rollback();
                                            }
                                            else
                                            {
                                                _log.Fine("Cost Calculation updated for m_invoiceline = " + invoiceLine.GetC_InvoiceLine_ID());
                                                Get_Trx().Commit();
                                            }

                                            // Update Product Cost on Product Transaction for the Invoice Line
                                            UpdateTransactionCostForInvoice(currentCostPrice, invoiceLine.GetC_InvoiceLine_ID(), costingCheck);
                                            Get_Trx().Commit();
                                        }
                                    }
                                }
                                #endregion
                            }
                            #endregion
                        }
                    }
                    catch { }
                }
            }
        }

        /// <summary>
        /// is used to calculate cost agianst shipment
        /// </summary>
        /// <param name="M_Inout_IDinout referenceparam>
        private void CalculateCostForShipment(int M_Inout_ID)
        {
            inout = new MInOut(GetCtx(), M_Inout_ID, Get_Trx());

            sql.Clear();
            sql.Append(@"SELECT il.* , ilma.M_AttributeSetInstance_ID AS M_AttributeSetInstance_IDMA , ilma.M_Transaction_ID, ilma.MovementQty AS MovementQtyMA 
                            FROM M_InoutLine il INNER JOIN M_InOutLineMA ilma ON (il.M_InoutLine_ID = ilma.M_InoutLine_ID) 
                        WHERE il.IsActive = 'Y' AND il.M_Inout_ID = " + inout.GetM_InOut_ID());
            if (inout.IsReversal())
            {
                sql.Append(" AND il.iscostcalculated = 'Y' AND il.IsReversedCostCalculated = 'N' ");
            }
            else
            {
                sql.Append(" AND il.iscostcalculated = 'N' ");
                sql.Append(" AND il.iscostImmediate = 'N' ");
            }
            if (!String.IsNullOrEmpty(productCategoryID) && String.IsNullOrEmpty(productID))
            {
                sql.Append(" AND il.M_Product_ID IN (SELECT M_Product_ID FROM M_Product WHERE M_Product_Category_ID IN (" + productCategoryID + " ) ) ");
            }
            else
            {
                sql.Append(" AND il.M_Product_ID IN (" + productID + " )");
            }
            if (M_AttributeSetInstance_ID > 0)
            {
                sql.Append(" AND NVL(ilma.M_AttributeSetInstance_ID, 0) = " + M_AttributeSetInstance_ID);
            }
            sql.Append(" ORDER BY il.Line");
            dsChildRecord = DB.ExecuteDataset(sql.ToString(), null, Get_Trx());
            if (dsChildRecord != null && dsChildRecord.Tables.Count > 0 && dsChildRecord.Tables[0].Rows.Count > 0)
            {
                /*Costing Object*/
                costingCheck = new CostingCheck(GetCtx());
                costingCheck.dsAccountingSchema = costingCheck.GetAccountingSchema(GetAD_Client_ID());
                costingCheck.inout = inout;
                costingCheck.isReversal = inout.IsReversal();

                for (int j = 0; j < dsChildRecord.Tables[0].Rows.Count; j++)
                {
                    try
                    {
                        //VIS_0045: Reset Class parameters
                        if (costingCheck != null)
                        {
                            costingCheck.ResetProperty();
                        }

                        inoutLine = new MInOutLine(GetCtx(), dsChildRecord.Tables[0].Rows[j], Get_Trx());
                        orderLine = new MOrderLine(GetCtx(), inoutLine.GetC_OrderLine_ID(), null);
                        if (orderLine != null && orderLine.GetC_Order_ID() > 0)
                        {
                            order = new MOrder(GetCtx(), orderLine.GetC_Order_ID(), null);
                            if (order.GetDocStatus() != "VO")
                            {
                                if (orderLine != null && orderLine.GetC_Order_ID() > 0 && orderLine.GetQtyOrdered() == 0)
                                    continue;
                            }
                            ProductOrderLineCost = orderLine.GetProductLineCost(orderLine);
                            ProductOrderPriceActual = ProductOrderLineCost / orderLine.GetQtyEntered();
                        }
                        product = new MProduct(GetCtx(), Util.GetValueOfInt(dsChildRecord.Tables[0].Rows[j]["M_Product_ID"]), Get_Trx());
                        if (product.GetProductType() == "I") // for Item Type product
                        {
                            costingCheck.AD_Org_ID = inoutLine.GetAD_Org_ID();
                            costingCheck.M_ASI_ID = Util.GetValueOfInt(dsChildRecord.Tables[0].Rows[j]["M_AttributeSetInstance_IDMA"]);
                            costingCheck.M_Warehouse_ID = inout.GetM_Warehouse_ID();
                            costingCheck.M_Transaction_ID = Util.GetValueOfInt(dsChildRecord.Tables[0].Rows[j]["M_Transaction_ID"]);
                            costingCheck.inoutline = inoutLine;
                            costingCheck.orderline = orderLine;
                            costingCheck.order = order;
                            costingCheck.product = product;

                            // Transaction Update Query
                            query.Clear();
                            query.Append("Update M_Transaction SET ");

                            #region shipment
                            if (inout.IsSOTrx() && !inout.IsReturnTrx())
                            {
                                if (inout.GetC_Order_ID() <= 0)
                                {
                                    break;
                                }

                                costingMethod = MCostElement.CheckLifoOrFifoMethod(GetCtx(), GetAD_Client_ID(), product.GetM_Product_ID(), Get_Trx());

                                #region get price from m_cost (Current Cost Price)
                                if (!client.IsCostImmediate() || inoutLine.GetCurrentCostPrice() == 0)
                                {
                                    // get price from m_cost (Current Cost Price)
                                    currentCostPrice = 0;
                                    currentCostPrice = MCost.GetproductCosts(inoutLine.GetAD_Client_ID(), inoutLine.GetAD_Org_ID(),
                                        inoutLine.GetM_Product_ID(), costingCheck.M_ASI_ID, Get_Trx(), inout.GetM_Warehouse_ID());
                                    if (IsCostUpdation)
                                    {
                                        DB.ExecuteQuery("UPDATE M_Inoutline SET CurrentCostPrice = " + currentCostPrice + " WHERE M_Inoutline_ID = " + inoutLine.GetM_InOutLine_ID(), null, Get_Trx());
                                    }
                                }
                                #endregion

                                if (!MCostQueue.CreateProductCostsDetails(GetCtx(), inout.GetAD_Client_ID(), inout.GetAD_Org_ID(), product, costingCheck.M_ASI_ID,
                                     "Shipment", null, inoutLine, null, null, null,
                                     order.GetDocStatus() != "VO" ? Decimal.Multiply(Decimal.Divide(ProductOrderLineCost, orderLine.GetQtyOrdered()),
                                     Decimal.Negate(Util.GetValueOfDecimal(dsChildRecord.Tables[0].Rows[j]["MovementQtyMA"])))
                                     : Decimal.Multiply(ProductOrderPriceActual, Decimal.Negate(Util.GetValueOfDecimal(dsChildRecord.Tables[0].Rows[j]["MovementQtyMA"]))),
                                     Decimal.Negate(Util.GetValueOfDecimal(dsChildRecord.Tables[0].Rows[j]["MovementQtyMA"])),
                                     Get_Trx(), costingCheck, out conversionNotFoundInOut, optionalstr: "window"))
                                {
                                    if (!conversionNotFoundInOut1.Contains(conversionNotFoundInOut))
                                    {
                                        conversionNotFoundInOut1 += conversionNotFoundInOut + " , ";
                                    }
                                    _log.Info("Cost not Calculated for Customer Return for this Line ID = " + inoutLine.GetM_InOutLine_ID() +
                                            " , " + (!string.IsNullOrEmpty(costingCheck.errorMessage) ? costingCheck.errorMessage : ""));
                                    ListReCalculatedRecords.Add(new ReCalculateRecord { WindowName = (int)windowName.Shipment, HeaderId = M_Inout_ID, LineId = inoutLine.GetM_InOutLine_ID(), IsReversal = false });
                                }
                                else
                                {
                                    // when costing method is LIFO or FIFO
                                    if (!string.IsNullOrEmpty(costingMethod))
                                    {
                                        currentCostPrice = MCost.GetLifoAndFifoCurrentCostFromCostQueueTransaction(GetCtx(), inoutLine.GetAD_Client_ID(),
                                            inoutLine.GetAD_Org_ID(), inoutLine.GetM_Product_ID(), costingCheck.M_ASI_ID, 0,
                                            inoutLine.GetM_InOutLine_ID(), costingMethod, inout.GetM_Warehouse_ID(), true, Get_Trx());

                                        if (IsCostUpdation)
                                        {
                                            inoutLine.SetCurrentCostPrice(currentCostPrice);
                                        }
                                    }
                                    else if (inoutLine.GetCurrentCostPrice() == 0)
                                    {
                                        // get price from m_cost (Current Cost Price)
                                        currentCostPrice = 0;
                                        currentCostPrice = MCost.GetproductCosts(inoutLine.GetAD_Client_ID(), inoutLine.GetAD_Org_ID(),
                                            inoutLine.GetM_Product_ID(), costingCheck.M_ASI_ID, Get_Trx(), inout.GetM_Warehouse_ID());

                                        if (IsCostUpdation)
                                        {
                                            inoutLine.SetCurrentCostPrice(currentCostPrice);
                                        }
                                    }
                                    if (inout.GetDescription() != null && inout.GetDescription().Contains("{->"))
                                    {
                                        //inoutLine.SetIsReversedCostCalculated(true);
                                    }
                                    //inoutLine.SetIsCostCalculated(true);
                                    //if (client.IsCostImmediate() && !inoutLine.IsCostImmediate())
                                    {
                                        inoutLine.SetIsCostImmediate(true);
                                    }
                                    if (!inoutLine.Save(Get_Trx()))
                                    {
                                        ValueNamePair pp = VLogger.RetrieveError();
                                        _log.Info("Error found for Customer Return for this Line ID = " + inoutLine.GetM_InOutLine_ID() +
                                                   " Error Name is " + pp.GetName() + " And Error Type is " + pp.GetType());
                                        Get_Trx().Rollback();
                                    }
                                    else
                                    {
                                        // Transaction Update Query
                                        if (!query.ToString().Contains("ProductApproxCost"))
                                        {
                                            query.Append(" ProductApproxCost = " + currentCostPrice);
                                        }

                                        query.Append(" , ProductCost = " + currentCostPrice);
                                        query.Append(" , M_CostElement_ID = " + costingCheck.definedCostingElement);
                                        query.Append(" , CostingLevel = " + GlobalVariable.TO_STRING(costingCheck.costinglevel));
                                        query.Append(" , VAS_PostingCost = " + currentCostPrice);
                                        query.Append(" WHERE M_Transaction_ID = " + costingCheck.M_Transaction_ID);
                                        if (IsCostUpdation)
                                        {
                                            DB.ExecuteQuery(query.ToString(), null, Get_Trx());
                                        }

                                        _log.Fine("Cost Calculation updated for M_InoutLine = " + inoutLine.GetM_InOutLine_ID());
                                        Get_Trx().Commit();
                                    }
                                }
                            }
                            #endregion
                        }
                    }
                    catch { }
                }
            }
            //sql.Clear();
            //sql.Append("SELECT COUNT(M_InOutLine_ID) FROM M_InOutLine WHERE  IsActive = 'Y' AND M_InOut_ID = " + inout.GetM_InOut_ID());
            //if (inout.IsReversal())
            //{
            //    sql.Append(" AND IsReversedCostCalculated = 'N' ");
            //}
            //else
            //{
            //    sql.Append(@" AND IsCostCalculated = 'N' ");
            //}
            //if (Util.GetValueOfInt(DB.ExecuteScalar(sql.ToString(), null, Get_Trx())) <= 0)
            //{
            //    if (inout.IsReversal())
            //    {
            //        inout.SetIsReversedCostCalculated(true);
            //    }
            //    inout.SetIsCostCalculated(true);
            //    if (!inout.Save(Get_Trx()))
            //    {
            //        ValueNamePair pp = VLogger.RetrieveError();
            //        _log.Info("Error found for saving M_inout for this Record ID = " + inout.GetM_InOut_ID() +
            //                   " Error Name is " + pp.GetName() + " And Error Type is " + pp.GetType());
            //    }
            //    else
            //    {
            //        _log.Fine("Cost Calculation updated for M_Inout = " + inout.GetM_InOut_ID());
            //        Get_Trx().Commit();
            //    }
            //}
        }

        /// <summary>
        /// Is used to calculate cost against Customer return
        /// </summary>
        /// <param name="M_Inout_ID">inout reference</param>
        private void CalculateCostForCustomerReturn(int M_Inout_ID)
        {
            inout = new MInOut(GetCtx(), M_Inout_ID, Get_Trx());

            sql.Clear();
            sql.Append(@"SELECT il.* , ilma.M_AttributeSetInstance_ID AS M_AttributeSetInstance_IDMA , ilma.M_Transaction_ID, ilma.MovementQty AS MovementQtyMA 
                            FROM M_InoutLine il INNER JOIN M_InOutLineMA ilma ON (il.M_InoutLine_ID = ilma.M_InoutLine_ID) 
                        WHERE il.IsActive = 'Y' AND il.M_Inout_ID = " + inout.GetM_InOut_ID());
            if (inout.IsReversal())
            {
                sql.Append(" AND il.iscostcalculated = 'Y' AND il.IsReversedCostCalculated = 'N' ");
            }
            else
            {
                sql.Append(" AND il.iscostcalculated = 'N' ");
                sql.Append(" AND il.iscostImmediate = 'N' ");
            }
            if (!String.IsNullOrEmpty(productCategoryID) && String.IsNullOrEmpty(productID))
            {
                sql.Append(" AND il.M_Product_ID IN (SELECT M_Product_ID FROM M_Product WHERE M_Product_Category_ID IN (" + productCategoryID + " ) ) ");
            }
            else
            {
                sql.Append(" AND il.M_Product_ID IN (" + productID + " )");
            }
            if (M_AttributeSetInstance_ID > 0)
            {
                sql.Append(" AND NVL(ilma.M_AttributeSetInstance_ID, 0) = " + M_AttributeSetInstance_ID);
            }
            sql.Append(" ORDER BY il.Line");

            dsChildRecord = DB.ExecuteDataset(sql.ToString(), null, Get_Trx());
            if (dsChildRecord != null && dsChildRecord.Tables.Count > 0 && dsChildRecord.Tables[0].Rows.Count > 0)
            {
                /*Costing Object*/
                costingCheck = new CostingCheck(GetCtx());
                costingCheck.dsAccountingSchema = costingCheck.GetAccountingSchema(GetAD_Client_ID());
                costingCheck.inout = inout;
                costingCheck.isReversal = inout.IsReversal();

                // get Original Shipment Details
                GetOriginalInoutDetail(inout.GetM_InOut_ID());

                for (int j = 0; j < dsChildRecord.Tables[0].Rows.Count; j++)
                {
                    try
                    {
                        //VIS_0045: Reset Class parameters
                        if (costingCheck != null)
                        {
                            costingCheck.ResetProperty();
                        }

                        inoutLine = new MInOutLine(GetCtx(), dsChildRecord.Tables[0].Rows[j], Get_Trx());
                        orderLine = new MOrderLine(GetCtx(), inoutLine.GetC_OrderLine_ID(), null);
                        if (orderLine != null && orderLine.GetC_Order_ID() > 0)
                        {
                            order = new MOrder(GetCtx(), orderLine.GetC_Order_ID(), null);
                            if (order.GetDocStatus() != "VO")
                            {
                                if (orderLine != null && orderLine.GetC_Order_ID() > 0 && orderLine.GetQtyOrdered() == 0)
                                    continue;
                            }
                            ProductOrderLineCost = orderLine.GetProductLineCost(orderLine);
                            ProductOrderPriceActual = ProductOrderLineCost / orderLine.GetQtyEntered();
                        }
                        product = new MProduct(GetCtx(), Util.GetValueOfInt(dsChildRecord.Tables[0].Rows[j]["M_Product_ID"]), Get_Trx());
                        if (product.GetProductType() == "I") // for Item Type product
                        {
                            costingCheck.AD_Org_ID = inoutLine.GetAD_Org_ID();
                            costingCheck.M_ASI_ID = Util.GetValueOfInt(dsChildRecord.Tables[0].Rows[j]["M_AttributeSetInstance_IDMA"]);
                            costingCheck.M_Warehouse_ID = inout.GetM_Warehouse_ID();
                            costingCheck.M_Transaction_ID = Util.GetValueOfInt(dsChildRecord.Tables[0].Rows[j]["M_Transaction_ID"]);
                            costingCheck.inoutline = inoutLine;
                            costingCheck.orderline = orderLine;
                            costingCheck.order = order;
                            costingCheck.product = product;
                            costingCheck.VAS_IsDOCost = Util.GetValueOfBool(client.Get_Value("VAS_IsDOCost"));

                            // Transaction Update Query
                            query.Clear();
                            query.Append("Update M_Transaction SET ");

                            #region Customer Return
                            if (inout.IsSOTrx() && inout.IsReturnTrx())
                            {
                                if (inout.GetOrig_Order_ID() <= 0)
                                {
                                    break;
                                }

                                costingMethod = MCostElement.CheckLifoOrFifoMethod(GetCtx(), GetAD_Client_ID(), product.GetM_Product_ID(), Get_Trx());

                                #region get price from m_cost (Current Cost Price)
                                if (!client.IsCostImmediate() || inoutLine.GetCurrentCostPrice() == 0)
                                {
                                    // get price from m_cost (Current Cost Price)
                                    if (CostOnOriginalDoc != null && CostOnOriginalDoc.Tables.Count > 0 && CostOnOriginalDoc.Tables[0].Rows.Count > 0)
                                    {
                                        //VIS_045: 04/Oct/2023, DevOps Task ID:2495 --> Get Cost Detail from the Original Document of Ship/Receipt
                                        // and update it on Return Document
                                        if (!costingCheck.VAS_IsDOCost)
                                        {
                                            currentCostPrice = MCost.GetproductCosts(inoutLine.GetAD_Client_ID(), inoutLine.GetAD_Org_ID(),
                                                                  inoutLine.GetM_Product_ID(), costingCheck.M_ASI_ID, Get_Trx(), inout.GetM_Warehouse_ID());
                                        }
                                        else
                                        {
                                            DataRow[] dr = CostOnOriginalDoc.Tables[0].Select("M_InOutLine_ID = " + inoutLine.GetM_InOutLine_ID());
                                            if (dr != null && dr.Length > 0)
                                            {
                                                currentCostPrice = Util.GetValueOfDecimal(dr[0]["CurrentCostPrice"]);
                                            }
                                        }
                                    }
                                    else
                                    {
                                        currentCostPrice = MCost.GetproductCosts(inoutLine.GetAD_Client_ID(), inoutLine.GetAD_Org_ID(),
                                            inoutLine.GetM_Product_ID(), inoutLine.GetM_AttributeSetInstance_ID(), Get_Trx(), inout.GetM_Warehouse_ID());
                                    }

                                    if (IsCostUpdation)
                                    {
                                        DB.ExecuteQuery("UPDATE M_Inoutline SET CurrentCostPrice = " + currentCostPrice + " WHERE M_Inoutline_ID = " + inoutLine.GetM_InOutLine_ID(), null, Get_Trx());
                                    }
                                }
                                #endregion

                                if (!MCostQueue.CreateProductCostsDetails(GetCtx(), inout.GetAD_Client_ID(), inout.GetAD_Org_ID(), product, costingCheck.M_ASI_ID,
                                      "Customer Return", null, inoutLine, null, null, null,
                                      order.GetDocStatus() != "VO" ?
                                          Decimal.Multiply(costingCheck.VAS_IsDOCost ? currentCostPrice : Decimal.Divide(ProductOrderLineCost, orderLine.GetQtyOrdered()),
                                                            Util.GetValueOfDecimal(dsChildRecord.Tables[0].Rows[j]["MovementQtyMA"]))
                                        : Decimal.Multiply(costingCheck.VAS_IsDOCost ? currentCostPrice : ProductOrderPriceActual, Util.GetValueOfDecimal(dsChildRecord.Tables[0].Rows[j]["MovementQtyMA"])),
                                      Util.GetValueOfDecimal(dsChildRecord.Tables[0].Rows[j]["MovementQtyMA"]),
                                      Get_Trx(), costingCheck, out conversionNotFoundInOut, optionalstr: "window"))
                                {
                                    if (!conversionNotFoundInOut1.Contains(conversionNotFoundInOut))
                                    {
                                        conversionNotFoundInOut1 += conversionNotFoundInOut + " , ";
                                    }
                                    _log.Info("Cost not Calculated for Customer Return for this Line ID = " + inoutLine.GetM_InOutLine_ID() +
                                            " , " + (!string.IsNullOrEmpty(costingCheck.errorMessage) ? costingCheck.errorMessage : ""));
                                    ListReCalculatedRecords.Add(new ReCalculateRecord { WindowName = (int)windowName.CustomerReturn, HeaderId = M_Inout_ID, LineId = inoutLine.GetM_InOutLine_ID(), IsReversal = false });
                                }
                                else
                                {
                                    // when costing method is LIFO or FIFO
                                    if (!string.IsNullOrEmpty(costingMethod))
                                    {
                                        if (inoutLine.GetC_OrderLine_ID() == 0)
                                        {
                                            currentCostPrice = MCost.GetLifoAndFifoCurrentCostFromCostQueueTransaction(GetCtx(), inoutLine.GetAD_Client_ID(),
                                            inoutLine.GetAD_Org_ID(), inoutLine.GetM_Product_ID(), costingCheck.M_ASI_ID, 0,
                                            inoutLine.GetM_InOutLine_ID(), costingMethod, inout.GetM_Warehouse_ID(), false, Get_Trx());
                                            if (IsCostUpdation)
                                            {
                                                inoutLine.SetCurrentCostPrice(currentCostPrice);
                                            }
                                        }
                                    }
                                    else if (inoutLine.GetCurrentCostPrice() == 0 && inoutLine.GetC_OrderLine_ID() == 0)
                                    {
                                        // get price from m_cost (Current Cost Price)
                                        currentCostPrice = 0;
                                        currentCostPrice = MCost.GetproductCostAndQtyMaterial(inoutLine.GetAD_Client_ID(), inoutLine.GetAD_Org_ID(),
                                            inoutLine.GetM_Product_ID(), costingCheck.M_ASI_ID, Get_Trx(), inout.GetM_Warehouse_ID(), false);
                                        if (IsCostUpdation)
                                        {
                                            inoutLine.SetCurrentCostPrice(currentCostPrice);
                                        }
                                    }
                                    if (inout.GetDescription() != null && inout.GetDescription().Contains("{->"))
                                    {
                                        //inoutLine.SetIsReversedCostCalculated(true);
                                    }
                                    //inoutLine.SetIsCostCalculated(true);
                                    //if (client.IsCostImmediate() && !inoutLine.IsCostImmediate())
                                    {
                                        inoutLine.SetIsCostImmediate(true);
                                    }
                                    if (!inoutLine.Save(Get_Trx()))
                                    {
                                        ValueNamePair pp = VLogger.RetrieveError();
                                        _log.Info("Error found for Customer Return for this Line ID = " + inoutLine.GetM_InOutLine_ID() +
                                                   " Error Name is " + pp.GetName() + " And Error Type is " + pp.GetType());
                                        Get_Trx().Rollback();
                                    }
                                    else
                                    {
                                        // Transaction Update Query
                                        if (!query.ToString().Contains("ProductApproxCost"))
                                        {
                                            query.Append(" ProductApproxCost = " + currentCostPrice);
                                        }

                                        query.Append(" , ProductCost = " + currentCostPrice);
                                        query.Append(" , M_CostElement_ID = " + costingCheck.definedCostingElement);
                                        query.Append(" , CostingLevel = " + GlobalVariable.TO_STRING(costingCheck.costinglevel));
                                        query.Append(" , VAS_PostingCost = " + currentCostPrice);
                                        query.Append(" WHERE M_Transaction_ID = " + costingCheck.M_Transaction_ID);
                                        if (IsCostUpdation)
                                        {
                                            DB.ExecuteQuery(query.ToString(), null, Get_Trx());
                                        }

                                        _log.Fine("Cost Calculation updated for M_InoutLine = " + inoutLine.GetM_InOutLine_ID());
                                        Get_Trx().Commit();
                                    }
                                }
                            }
                            #endregion
                        }
                    }
                    catch { }
                }
            }
            //sql.Clear();
            //sql.Append("SELECT COUNT(M_InOutLine_ID) FROM M_InOutLine WHERE  IsActive = 'Y' AND M_InOut_ID = " + inout.GetM_InOut_ID());
            //if (inout.IsReversal())
            //{
            //    sql.Append(" AND IsReversedCostCalculated = 'N' ");
            //}
            //else
            //{
            //    sql.Append(@" AND IsCostCalculated = 'N' ");
            //}
            //if (Util.GetValueOfInt(DB.ExecuteScalar(sql.ToString(), null, Get_Trx())) <= 0)
            //{
            //    if (inout.IsReversal())
            //    {
            //        inout.SetIsReversedCostCalculated(true);
            //    }
            //    inout.SetIsCostCalculated(true);
            //    if (!inout.Save(Get_Trx()))
            //    {
            //        ValueNamePair pp = VLogger.RetrieveError();
            //        _log.Info("Error found for saving M_inout for this Record ID = " + inout.GetM_InOut_ID() +
            //                   " Error Name is " + pp.GetName() + " And Error Type is " + pp.GetType());
            //    }
            //    else
            //    {
            //        _log.Fine("Cost Calculation updated for M_Inout = " + inout.GetM_InOut_ID());
            //        Get_Trx().Commit();
            //    }
            //}
        }

        public DataSet GetOriginalInoutDetail(int M_InOut_ID)
        {
            CostOnOriginalDoc = DB.ExecuteDataset($@"SELECT orgiol.CurrentCostPrice , orgiol.PostCurrentCostPrice, 
                                        retiol.C_OrderLine_ID AS RMALine_ID, retiol.M_InOutLine_ID 
                                        FROM M_InOutLine retiol
                                        INNER JOIN M_InOut i ON (i.M_InOut_ID = retiol.M_InOut_ID)
                                        INNER JOIN C_OrderLine rmaol ON (rmaol.C_OrderLine_ID = retiol.C_OrderLine_ID)
                                        INNER JOIN M_InOutLine orgiol ON (orgiol.M_InOutLine_ID = rmaol.Orig_InOutLine_ID)
                                        WHERE i.M_InOut_ID = {M_InOut_ID}");
            return CostOnOriginalDoc;
        }

        /// <summary>
        /// Is used to calculate cost against Return to Vendor
        /// </summary>
        /// <param name="M_Inout_ID">inout reference</param>
        private void CalculateCostForReturnToVendor(int M_Inout_ID)
        {
            inout = new MInOut(GetCtx(), M_Inout_ID, Get_Trx());

            sql.Clear();
            sql.Append(@"SELECT il.* , ilma.M_AttributeSetInstance_ID AS M_AttributeSetInstance_IDMA , ilma.M_Transaction_ID, ilma.MovementQty AS MovementQtyMA 
                            FROM M_InoutLine il INNER JOIN M_InOutLineMA ilma ON (il.M_InoutLine_ID = ilma.M_InoutLine_ID) 
                        WHERE il.IsActive = 'Y' AND il.M_Inout_ID = " + inout.GetM_InOut_ID());
            if (inout.IsReversal())
            {
                sql.Append(" AND il.iscostcalculated = 'Y' AND il.IsReversedCostCalculated = 'N' ");
            }
            else
            {
                sql.Append(" AND il.iscostcalculated = 'N' ");
                sql.Append(" AND il.iscostImmediate = 'N' ");
            }
            if (!String.IsNullOrEmpty(productCategoryID) && String.IsNullOrEmpty(productID))
            {
                sql.Append(" AND il.M_Product_ID IN (SELECT M_Product_ID FROM M_Product WHERE M_Product_Category_ID IN (" + productCategoryID + " ) ) ");
            }
            else
            {
                sql.Append(" AND il.M_Product_ID IN (" + productID + " )");
            }
            if (M_AttributeSetInstance_ID > 0)
            {
                sql.Append(" AND NVL(ilma.M_AttributeSetInstance_ID, 0) = " + M_AttributeSetInstance_ID);
            }
            sql.Append(" ORDER BY il.Line");
            dsChildRecord = DB.ExecuteDataset(sql.ToString(), null, Get_Trx());
            if (dsChildRecord != null && dsChildRecord.Tables.Count > 0 && dsChildRecord.Tables[0].Rows.Count > 0)
            {
                /*Costing Object*/
                costingCheck = new CostingCheck(GetCtx());
                costingCheck.dsAccountingSchema = costingCheck.GetAccountingSchema(GetAD_Client_ID());
                costingCheck.inout = inout;
                costingCheck.isReversal = inout.IsReversal();

                for (int j = 0; j < dsChildRecord.Tables[0].Rows.Count; j++)
                {
                    try
                    {
                        //VIS_0045: Reset Class parameters
                        if (costingCheck != null)
                        {
                            costingCheck.ResetProperty();
                        }

                        inoutLine = new MInOutLine(GetCtx(), dsChildRecord.Tables[0].Rows[j], Get_Trx());
                        orderLine = new MOrderLine(GetCtx(), inoutLine.GetC_OrderLine_ID(), null);
                        if (orderLine != null && orderLine.GetC_Order_ID() > 0)
                        {
                            order = new MOrder(GetCtx(), orderLine.GetC_Order_ID(), null);
                            if (order.GetDocStatus() != "VO")
                            {
                                if (orderLine != null && orderLine.GetC_Order_ID() > 0 && orderLine.GetQtyOrdered() == 0)
                                    continue;
                            }
                            ProductOrderLineCost = orderLine.GetProductLineCost(orderLine);
                            ProductOrderPriceActual = ProductOrderLineCost / orderLine.GetQtyEntered();
                        }
                        product = new MProduct(GetCtx(), Util.GetValueOfInt(dsChildRecord.Tables[0].Rows[j]["M_Product_ID"]), Get_Trx());
                        if (product.GetProductType() == "I") // for Item Type product
                        {
                            isCostAdjustableOnLost = product.IsCostAdjustmentOnLost();
                            costingCheck.AD_Org_ID = inoutLine.GetAD_Org_ID();
                            costingCheck.M_ASI_ID = Util.GetValueOfInt(dsChildRecord.Tables[0].Rows[j]["M_AttributeSetInstance_IDMA"]);
                            costingCheck.M_Warehouse_ID = inout.GetM_Warehouse_ID();
                            costingCheck.M_Transaction_ID = Util.GetValueOfInt(dsChildRecord.Tables[0].Rows[j]["M_Transaction_ID"]);
                            costingCheck.inoutline = inoutLine;
                            costingCheck.orderline = orderLine;
                            costingCheck.order = order;
                            costingCheck.product = product;
                            costingCheck.IsPOCostingethodBindedonProduct = MCostElement.IsPOCostingmethod(GetCtx(), GetAD_Client_ID(),
                                                                        product.GetM_Product_ID(), Get_Trx());

                            // Transaction Update Query
                            query.Clear();
                            query.Append("Update M_Transaction SET ");

                            #region  Return To Vendor
                            if (!inout.IsSOTrx() && inout.IsReturnTrx())
                            {
                                #region get price from m_cost (Current Cost Price)
                                if (!client.IsCostImmediate() || inoutLine.GetCurrentCostPrice() == 0)
                                {
                                    // get price from m_cost (Current Cost Price)
                                    currentCostPrice = 0;
                                    currentCostPrice = MCost.GetproductCosts(inoutLine.GetAD_Client_ID(), inoutLine.GetAD_Org_ID(),
                                        inoutLine.GetM_Product_ID(), costingCheck.M_ASI_ID, Get_Trx(), inout.GetM_Warehouse_ID());

                                    if (IsCostUpdation)
                                    {
                                        DB.ExecuteQuery("UPDATE M_Inoutline SET CurrentCostPrice = " + currentCostPrice + " WHERE M_Inoutline_ID = " + inoutLine.GetM_InOutLine_ID(), null, Get_Trx());
                                    }

                                    // Transaction Update Query
                                    if (currentCostPrice != 0)
                                    {
                                        // this column will be added when current cost available else to be added after cost calculation
                                        query.Append(" ProductApproxCost = " + currentCostPrice);
                                    }
                                }
                                #endregion

                                //VIS_045:20-May-2025, when vebdor return match with RMA using form then on Vendor Return header, system was not updating the Orig Order reference, so remove that check
                                if (orderLine == null || orderLine.GetC_OrderLine_ID() == 0)
                                {
                                    #region Return to Vendor against without Vendor RMA
                                    if (!MCostQueue.CreateProductCostsDetails(GetCtx(), inout.GetAD_Client_ID(), inout.GetAD_Org_ID(), product, costingCheck.M_ASI_ID,
                                   "Return To Vendor", null, inoutLine, null, null, null, 0,
                                   Decimal.Negate(Util.GetValueOfDecimal(dsChildRecord.Tables[0].Rows[j]["MovementQtyMA"])), Get_TrxName(), costingCheck, out conversionNotFoundInOut, optionalstr: "window"))
                                    {
                                        if (!conversionNotFoundInOut1.Contains(conversionNotFoundInOut))
                                        {
                                            conversionNotFoundInOut1 += conversionNotFoundInOut + " , ";
                                        }
                                        _log.Info("Cost not Calculated for Return To Vendor for this Line ID = " + inoutLine.GetM_InOutLine_ID() +
                                            " , " + (!string.IsNullOrEmpty(costingCheck.errorMessage) ? costingCheck.errorMessage : ""));
                                        ListReCalculatedRecords.Add(new ReCalculateRecord { WindowName = (int)windowName.ReturnVendor, HeaderId = M_Inout_ID, LineId = inoutLine.GetM_InOutLine_ID(), IsReversal = false });
                                    }
                                    else
                                    {
                                        // get price from m_cost (Current Cost Price)
                                        currentCostPrice = 0;
                                        currentCostPrice = MCost.GetproductCosts(inoutLine.GetAD_Client_ID(), inoutLine.GetAD_Org_ID(),
                                            inoutLine.GetM_Product_ID(), inoutLine.GetM_AttributeSetInstance_ID(), Get_Trx(), inout.GetM_Warehouse_ID());

                                        if (inoutLine.GetCurrentCostPrice() == 0)
                                        {
                                            if (IsCostUpdation)
                                            {
                                                inoutLine.SetCurrentCostPrice(currentCostPrice);
                                            }
                                        }
                                        if (inout.GetDescription() != null && inout.GetDescription().Contains("{->"))
                                        {
                                            //inoutLine.SetIsReversedCostCalculated(true);
                                        }
                                        //inoutLine.SetIsCostCalculated(true);
                                        //if (client.IsCostImmediate() && !inoutLine.IsCostImmediate())
                                        {
                                            inoutLine.SetIsCostImmediate(true);
                                        }
                                        if (costingCheck.IsPOCostingethodBindedonProduct.Value)
                                        {
                                            inoutLine.SetPostCurrentCostPrice(currentCostPrice);
                                        }
                                        if (!inoutLine.Save(Get_Trx()))
                                        {
                                            ValueNamePair pp = VLogger.RetrieveError();
                                            _log.Info("Error found for Return To Vendor for this Line ID = " + inoutLine.GetM_InOutLine_ID() +
                                                       " Error Name is " + pp.GetName() + " And Error Type is " + pp.GetType());
                                            Get_Trx().Rollback();
                                        }
                                        else
                                        {
                                            // Transaction Update Query
                                            if (!query.ToString().Contains("ProductApproxCost"))
                                            {
                                                query.Append(" ProductApproxCost = " + currentCostPrice);
                                            }
                                            query.Append(" , ProductCost = " + currentCostPrice);
                                            query.Append(" , M_CostElement_ID = " + costingCheck.definedCostingElement);
                                            query.Append(" , CostingLevel = " + GlobalVariable.TO_STRING(costingCheck.costinglevel));
                                            query.Append(" , VAS_PostingCost = " + Math.Abs(costingCheck.OrderLineAmtinBaseCurrency));
                                            query.Append(" WHERE M_Transaction_ID = " + costingCheck.M_Transaction_ID);
                                            if (IsCostUpdation)
                                            {
                                                DB.ExecuteQuery(query.ToString(), null, Get_Trx());
                                            }

                                            _log.Fine("Cost Calculation updated for M_InoutLine = " + inoutLine.GetM_InOutLine_ID());
                                            Get_Trx().Commit();
                                        }
                                    }
                                    #endregion
                                }
                                else
                                {
                                    #region Return to Vendor against with Vendor RMA

                                    amt = 0;
                                    if (isCostAdjustableOnLost && inoutLine.GetMovementQty() < orderLine.GetQtyOrdered() && order.GetDocStatus() != "VO")
                                    {
                                        if (inoutLine.GetMovementQty() < 0)
                                            amt = ProductOrderLineCost;
                                        else
                                            amt = Decimal.Negate(ProductOrderLineCost);
                                    }
                                    else if (!isCostAdjustableOnLost && inoutLine.GetMovementQty() < orderLine.GetQtyOrdered() && order.GetDocStatus() != "VO")
                                    {
                                        amt = Decimal.Multiply(Decimal.Divide(ProductOrderLineCost, orderLine.GetQtyOrdered()), Decimal.Negate(inoutLine.GetMovementQty()));
                                    }
                                    else if (order.GetDocStatus() != "VO")
                                    {
                                        amt = Decimal.Multiply(Decimal.Divide(ProductOrderLineCost, orderLine.GetQtyOrdered()), Decimal.Negate(inoutLine.GetMovementQty()));
                                    }
                                    else if (order.GetDocStatus() == "VO")
                                    {
                                        amt = Decimal.Multiply(ProductOrderPriceActual, Decimal.Negate(inoutLine.GetQtyEntered()));
                                    }

                                    if (!MCostQueue.CreateProductCostsDetails(GetCtx(), inout.GetAD_Client_ID(), inout.GetAD_Org_ID(), product, costingCheck.M_ASI_ID,
                                        "Return To Vendor", null, inoutLine, null, null, null, amt,
                                        Decimal.Negate(Util.GetValueOfDecimal(dsChildRecord.Tables[0].Rows[j]["MovementQtyMA"])),
                                        Get_TrxName(), costingCheck, out conversionNotFoundInOut, optionalstr: "window"))
                                    {
                                        if (!conversionNotFoundInOut1.Contains(conversionNotFoundInOut))
                                        {
                                            conversionNotFoundInOut1 += conversionNotFoundInOut + " , ";
                                        }
                                        _log.Info("Cost not Calculated for Return To Vendor for this Line ID = " + inoutLine.GetM_InOutLine_ID() +
                                            " , " + (!string.IsNullOrEmpty(costingCheck.errorMessage) ? costingCheck.errorMessage : ""));
                                        ListReCalculatedRecords.Add(new ReCalculateRecord { WindowName = (int)windowName.ReturnVendor, HeaderId = M_Inout_ID, LineId = inoutLine.GetM_InOutLine_ID(), IsReversal = false });
                                    }
                                    else
                                    {
                                        if (inout.GetDescription() != null && inout.GetDescription().Contains("{->"))
                                        {
                                            //inoutLine.SetIsReversedCostCalculated(true);
                                        }
                                        //inoutLine.SetIsCostCalculated(true);
                                        //if (client.IsCostImmediate() && !inoutLine.IsCostImmediate())
                                        {
                                            inoutLine.SetIsCostImmediate(true);
                                        }
                                        if (costingCheck.IsPOCostingethodBindedonProduct.Value)
                                        {
                                            currentCostPrice = MCost.GetproductCosts(inoutLine.GetAD_Client_ID(), inoutLine.GetAD_Org_ID(),
                                                         inoutLine.GetM_Product_ID(), inoutLine.GetM_AttributeSetInstance_ID(), Get_Trx(), inout.GetM_Warehouse_ID());
                                            inoutLine.SetPostCurrentCostPrice(currentCostPrice);
                                        }
                                        if (!inoutLine.Save(Get_Trx()))
                                        {
                                            ValueNamePair pp = VLogger.RetrieveError();
                                            _log.Info("Error found for Return To Vendor for this Line ID = " + inoutLine.GetM_InOutLine_ID() +
                                                       " Error Name is " + pp.GetName() + " And Error Type is " + pp.GetType());
                                            Get_Trx().Rollback();
                                        }
                                        else
                                        {
                                            // Transaction Update Query
                                            if (!query.ToString().Contains("ProductApproxCost"))
                                            {
                                                query.Append(" ProductApproxCost = " + currentCostPrice);
                                            }
                                            query.Append(" , ProductCost = " + currentCostPrice);
                                            query.Append(" , M_CostElement_ID = " + costingCheck.definedCostingElement);
                                            query.Append(" , CostingLevel = " + GlobalVariable.TO_STRING(costingCheck.costinglevel));
                                            query.Append(" , VAS_PostingCost = " + Math.Abs(costingCheck.OrderLineAmtinBaseCurrency));
                                            query.Append(" WHERE M_Transaction_ID = " + costingCheck.M_Transaction_ID);
                                            if (IsCostUpdation)
                                            {
                                                DB.ExecuteQuery(query.ToString(), null, Get_Trx());
                                            }

                                            _log.Fine("Cost Calculation updated for M_InoutLine = " + inoutLine.GetM_InOutLine_ID());
                                            Get_Trx().Commit();
                                        }
                                    }
                                    #endregion
                                }
                            }
                            #endregion
                        }
                    }
                    catch { }
                }
            }
            //sql.Clear();
            //sql.Append("SELECT COUNT(M_InOutLine_ID) FROM M_InOutLine WHERE  IsActive = 'Y' AND M_InOut_ID = " + inout.GetM_InOut_ID());
            //if (inout.IsReversal())
            //{
            //    sql.Append(" AND IsReversedCostCalculated = 'N' ");
            //}
            //else
            //{
            //    sql.Append(@" AND IsCostCalculated = 'N' ");
            //}
            //if (Util.GetValueOfInt(DB.ExecuteScalar(sql.ToString(), null, Get_Trx())) <= 0)
            //{
            //    if (inout.IsReversal())
            //    {
            //        inout.SetIsReversedCostCalculated(true);
            //    }
            //    inout.SetIsCostCalculated(true);
            //    if (!inout.Save(Get_Trx()))
            //    {
            //        ValueNamePair pp = VLogger.RetrieveError();
            //        _log.Info("Error found for saving M_inout for this Record ID = " + inout.GetM_InOut_ID() +
            //                   " Error Name is " + pp.GetName() + " And Error Type is " + pp.GetType());
            //    }
            //    else
            //    {
            //        _log.Fine("Cost Calculation updated for M_Inout = " + inout.GetM_InOut_ID());
            //        Get_Trx().Commit();
            //    }
            //}
        }

        /// <summary>
        /// Is used to calculate cost against Material receipt
        /// </summary>
        /// <param name="M_Inout_ID">inout refreence</param>
        private void CalculateCostForMaterial(int M_Inout_ID)
        {
            inout = new MInOut(GetCtx(), M_Inout_ID, Get_Trx());

            sql.Clear();
            sql.Append(@"SELECT il.* , ilma.M_AttributeSetInstance_ID AS M_AttributeSetInstance_IDMA , ilma.M_Transaction_ID, ilma.MovementQty AS MovementQtyMA 
                            FROM M_InoutLine il INNER JOIN M_InOutLineMA ilma ON (il.M_InoutLine_ID = ilma.M_InoutLine_ID) 
                        WHERE il.IsActive = 'Y' AND il.M_Inout_ID = " + inout.GetM_InOut_ID());
            if (inout.IsReversal())
            {
                sql.Append(" AND il.iscostcalculated = 'Y' AND il.IsReversedCostCalculated = 'N' ");
            }
            else
            {
                sql.Append(" AND il.iscostcalculated = 'N' AND il.IsCostImmediate = 'N' ");
            }
            if (!String.IsNullOrEmpty(productCategoryID) && String.IsNullOrEmpty(productID))
            {
                sql.Append(" AND il.M_Product_ID IN (SELECT M_Product_ID FROM M_Product WHERE M_Product_Category_ID IN (" + productCategoryID + " ) ) ");
            }
            else
            {
                sql.Append(" AND il.M_Product_ID IN (" + productID + " )");
            }
            if (M_AttributeSetInstance_ID > 0)
            {
                sql.Append(" AND NVL(ilma.M_AttributeSetInstance_ID, 0) = " + M_AttributeSetInstance_ID);
            }
            sql.Append(" ORDER BY il.Line");

            dsChildRecord = DB.ExecuteDataset(sql.ToString(), null, Get_Trx());
            if (dsChildRecord != null && dsChildRecord.Tables.Count > 0 && dsChildRecord.Tables[0].Rows.Count > 0)
            {
                /*Costing Object*/
                costingCheck = new CostingCheck(GetCtx());
                costingCheck.dsAccountingSchema = costingCheck.GetAccountingSchema(GetAD_Client_ID());
                costingCheck.inout = inout;
                costingCheck.isReversal = inout.IsReversal();

                for (int j = 0; j < dsChildRecord.Tables[0].Rows.Count; j++)
                {
                    try
                    {
                        //VIS_0045: Reset Class parameters
                        if (costingCheck != null)
                        {
                            costingCheck.ResetProperty();
                        }

                        inoutLine = new MInOutLine(GetCtx(), dsChildRecord.Tables[0].Rows[j], Get_Trx());
                        orderLine = new MOrderLine(GetCtx(), inoutLine.GetC_OrderLine_ID(), null);
                        if (orderLine != null && orderLine.GetC_Order_ID() > 0)
                        {
                            order = new MOrder(GetCtx(), orderLine.GetC_Order_ID(), null);
                            if (order.GetDocStatus() != "VO")
                            {
                                if (orderLine != null && orderLine.GetC_Order_ID() > 0 && orderLine.GetQtyOrdered() == 0)
                                    continue;
                            }
                            ProductOrderLineCost = orderLine.GetProductLineCost(orderLine);
                            ProductOrderPriceActual = ProductOrderLineCost / orderLine.GetQtyEntered();
                        }
                        product = new MProduct(GetCtx(), Util.GetValueOfInt(dsChildRecord.Tables[0].Rows[j]["M_Product_ID"]), Get_Trx());
                        if (product.GetProductType() == "I") // for Item Type product
                        {
                            isCostAdjustableOnLost = product.IsCostAdjustmentOnLost();
                            bool isUpdatePostCurrentcostPriceFromMR = MCostElement.IsPOCostingmethod(GetCtx(), inout.GetAD_Client_ID(), product.GetM_Product_ID(), Get_Trx());

                            costingCheck.AD_Org_ID = inoutLine.GetAD_Org_ID();
                            costingCheck.M_ASI_ID = Util.GetValueOfInt(dsChildRecord.Tables[0].Rows[j]["M_AttributeSetInstance_IDMA"]);
                            costingCheck.M_Warehouse_ID = inout.GetM_Warehouse_ID();
                            costingCheck.M_Transaction_ID = Util.GetValueOfInt(dsChildRecord.Tables[0].Rows[j]["M_Transaction_ID"]);
                            costingCheck.inoutline = inoutLine;
                            costingCheck.orderline = orderLine;
                            costingCheck.order = order;
                            costingCheck.product = product;
                            costingCheck.IsPOCostingethodBindedonProduct = isUpdatePostCurrentcostPriceFromMR;

                            // Transaction Update Query
                            query.Clear();
                            query.Append("Update M_Transaction SET ");

                            #region Material Receipt
                            if (!inout.IsSOTrx() && !inout.IsReturnTrx())
                            {
                                if (orderLine == null || orderLine.GetC_OrderLine_ID() == 0) //MR Without PO
                                {
                                    #region MR Without PO
                                    if (!client.IsCostImmediate() || !inoutLine.IsCostImmediate() || inoutLine.GetCurrentCostPrice() == 0)
                                    {
                                        // get price from m_cost (Current Cost Price)
                                        currentCostPrice = 0;
                                        currentCostPrice = MCost.GetproductCostAndQtyMaterial(inoutLine.GetAD_Client_ID(), inoutLine.GetAD_Org_ID(),
                                            inoutLine.GetM_Product_ID(), costingCheck.M_ASI_ID, Get_Trx(), inout.GetM_Warehouse_ID(), false);

                                        if (IsCostUpdation)
                                        {
                                            DB.ExecuteQuery("UPDATE M_Inoutline SET CurrentCostPrice = " + currentCostPrice + " WHERE M_Inoutline_ID = " + inoutLine.GetM_InOutLine_ID(), null, Get_Trx());
                                        }

                                        // Transaction Update Query
                                        if (currentCostPrice != 0)
                                        {
                                            // this column will be added when current cost available else to be added after cost calculation
                                            query.Append(" ProductApproxCost = " + currentCostPrice);
                                        }
                                    }
                                    if (!MCostQueue.CreateProductCostsDetails(GetCtx(), inout.GetAD_Client_ID(), inout.GetAD_Org_ID(), product, costingCheck.M_ASI_ID,
                                   "Material Receipt", null, inoutLine, null, null, null, 0, Util.GetValueOfDecimal(dsChildRecord.Tables[0].Rows[j]["MovementQtyMA"]),
                                   Get_Trx(), costingCheck, out conversionNotFoundInOut, optionalstr: "window"))
                                    {
                                        if (!conversionNotFoundInOut1.Contains(conversionNotFoundInOut))
                                        {
                                            conversionNotFoundInOut1 += conversionNotFoundInOut + " , ";
                                        }
                                        _log.Info("Cost not Calculated for Material Receipt for this Line ID = " + inoutLine.GetM_InOutLine_ID() +
                                            " , " + (!string.IsNullOrEmpty(costingCheck.errorMessage) ? costingCheck.errorMessage : ""));
                                        ListReCalculatedRecords.Add(new ReCalculateRecord { WindowName = (int)windowName.MaterialReceipt, HeaderId = M_Inout_ID, LineId = inoutLine.GetM_InOutLine_ID(), IsReversal = false });
                                    }
                                    else
                                    {
                                        if (inoutLine.GetCurrentCostPrice() == 0 || isUpdatePostCurrentcostPriceFromMR)
                                        {
                                            // get price from m_cost (Current Cost Price)
                                            currentCostPrice = 0;
                                            currentCostPrice = MCost.GetproductCostAndQtyMaterial(inoutLine.GetAD_Client_ID(), inoutLine.GetAD_Org_ID(),
                                                inoutLine.GetM_Product_ID(), costingCheck.M_ASI_ID, Get_Trx(), inout.GetM_Warehouse_ID(), false);
                                        }
                                        if (inoutLine.GetCurrentCostPrice() == 0 && IsCostUpdation)
                                        {
                                            inoutLine.SetCurrentCostPrice(currentCostPrice);
                                        }
                                        if (isUpdatePostCurrentcostPriceFromMR && inoutLine.GetPostCurrentCostPrice() == 0 && IsCostUpdation)
                                        {
                                            inoutLine.SetPostCurrentCostPrice(currentCostPrice);
                                        }
                                        if (inout.GetDescription() != null && inout.GetDescription().Contains("{->"))
                                        {
                                            //inoutLine.SetIsReversedCostCalculated(true);
                                        }
                                        //inoutLine.SetIsCostCalculated(true);
                                        //if (client.IsCostImmediate() && !inoutLine.IsCostImmediate())
                                        {
                                            inoutLine.SetIsCostImmediate(true);
                                        }
                                        if (!inoutLine.Save(Get_Trx()))
                                        {
                                            ValueNamePair pp = VLogger.RetrieveError();
                                            _log.Info("Error found for Material Receipt for this Line ID = " + inoutLine.GetM_InOutLine_ID() +
                                                       " Error Name is " + pp.GetName() + " And Error Type is " + pp.GetType());
                                            Get_Trx().Rollback();
                                        }
                                        else
                                        {
                                            // Transaction Update Query
                                            if (!query.ToString().Contains("ProductApproxCost"))
                                            {
                                                query.Append(" ProductApproxCost = " + currentCostPrice);
                                            }
                                            if (isUpdatePostCurrentcostPriceFromMR)
                                            {
                                                // when selected costing method is average po, weighted average po, last po
                                                // else to be updated from invoice
                                                query.Append(" , ProductCost = " + currentCostPrice);
                                            }
                                            query.Append(" , M_CostElement_ID = " + costingCheck.definedCostingElement);
                                            query.Append(" , CostingLevel = " + GlobalVariable.TO_STRING(costingCheck.costinglevel));
                                            query.Append(" , VAS_PostingCost = " + costingCheck.OrderLineAmtinBaseCurrency);
                                            query.Append(" WHERE M_Transaction_ID = " + costingCheck.M_Transaction_ID);
                                            if (IsCostUpdation)
                                            {
                                                DB.ExecuteQuery(query.ToString(), null, Get_Trx());
                                            }

                                            _log.Fine("Cost Calculation updated for m_inoutline = " + inoutLine.GetM_InOutLine_ID());
                                            Get_Trx().Commit();
                                        }
                                    }
                                    #endregion
                                }
                                else
                                {
                                    #region MR With PO
                                    if (!client.IsCostImmediate() || !inoutLine.IsCostImmediate() || inoutLine.GetCurrentCostPrice() == 0)
                                    {
                                        // get price from m_cost (Current Cost Price)
                                        currentCostPrice = 0;
                                        currentCostPrice = MCost.GetproductCostAndQtyMaterial(inoutLine.GetAD_Client_ID(), inoutLine.GetAD_Org_ID(),
                                            inoutLine.GetM_Product_ID(), costingCheck.M_ASI_ID, Get_Trx(), inout.GetM_Warehouse_ID(), false);
                                        if (IsCostUpdation)
                                        {
                                            inoutLine.SetCurrentCostPrice(currentCostPrice);
                                            _log.Info("product cost " + inoutLine.GetM_Product_ID() + " - " + currentCostPrice);
                                            if (!inoutLine.Save(Get_Trx()))
                                            {
                                                ValueNamePair pp = VLogger.RetrieveError();
                                                _log.Info("Error found for Material Receipt for this Line ID = " + inoutLine.GetM_InOutLine_ID() +
                                                           " Error Name is " + pp.GetName() + " And Error Type is " + pp.GetType());
                                                Get_Trx().Rollback();
                                            }
                                        }

                                        // Transaction Update Query
                                        if (currentCostPrice != 0)
                                        {
                                            // this column will be added when current cost available else to be added after cost calculation
                                            query.Append(" ProductApproxCost = " + currentCostPrice);
                                        }
                                    }

                                    amt = 0;
                                    if (isCostAdjustableOnLost && inoutLine.GetMovementQty() < orderLine.GetQtyOrdered() && order.GetDocStatus() != "VO")
                                    {
                                        amt = ProductOrderLineCost;
                                    }
                                    else if (!isCostAdjustableOnLost && inoutLine.GetMovementQty() < orderLine.GetQtyOrdered() && order.GetDocStatus() != "VO")
                                    {
                                        amt = Decimal.Multiply(Decimal.Divide(ProductOrderLineCost, orderLine.GetQtyOrdered()), inoutLine.GetMovementQty());
                                    }
                                    else if (order.GetDocStatus() != "VO")
                                    {
                                        amt = Decimal.Multiply(Decimal.Divide(ProductOrderLineCost, orderLine.GetQtyOrdered()), inoutLine.GetMovementQty());
                                    }
                                    else if (order.GetDocStatus() == "VO")
                                    {
                                        amt = Decimal.Multiply(ProductOrderPriceActual, inoutLine.GetQtyEntered());
                                    }
                                    _log.Info("product cost " + inoutLine.GetM_Product_ID());
                                    if (!MCostQueue.CreateProductCostsDetails(GetCtx(), inout.GetAD_Client_ID(), inout.GetAD_Org_ID(), product, costingCheck.M_ASI_ID,
                                       "Material Receipt", null, inoutLine, null, null, null, amt,
                                       Util.GetValueOfDecimal(dsChildRecord.Tables[0].Rows[j]["MovementQtyMA"]), Get_Trx(), costingCheck, out conversionNotFoundInOut, optionalstr: "window"))
                                    {
                                        if (!conversionNotFoundInOut1.Contains(conversionNotFoundInOut))
                                        {
                                            conversionNotFoundInOut1 += conversionNotFoundInOut + " , ";
                                        }
                                        _log.Info("Cost not Calculated for Material Receipt for this Line ID = " + inoutLine.GetM_InOutLine_ID() +
                                            " , " + (!string.IsNullOrEmpty(costingCheck.errorMessage) ? costingCheck.errorMessage : ""));
                                        ListReCalculatedRecords.Add(new ReCalculateRecord { WindowName = (int)windowName.MaterialReceipt, HeaderId = M_Inout_ID, LineId = inoutLine.GetM_InOutLine_ID(), IsReversal = false });
                                    }
                                    else
                                    {
                                        _log.Info("product cost 1 " + inoutLine.GetM_Product_ID() + "- " + inoutLine.GetCurrentCostPrice());
                                        if (inoutLine.GetCurrentCostPrice() == 0 || isUpdatePostCurrentcostPriceFromMR)
                                        {
                                            // get price from m_cost (Current Cost Price)
                                            currentCostPrice = 0;
                                            currentCostPrice = MCost.GetproductCostAndQtyMaterial(inoutLine.GetAD_Client_ID(), inoutLine.GetAD_Org_ID(),
                                                inoutLine.GetM_Product_ID(), costingCheck.M_ASI_ID, Get_Trx(), inout.GetM_Warehouse_ID(), false);
                                        }
                                        if (inoutLine.GetCurrentCostPrice() == 0 && IsCostUpdation)
                                        {
                                            inoutLine.SetCurrentCostPrice(currentCostPrice);
                                        }
                                        if (isUpdatePostCurrentcostPriceFromMR && inoutLine.GetPostCurrentCostPrice() == 0 && IsCostUpdation)
                                        {
                                            inoutLine.SetPostCurrentCostPrice(currentCostPrice);
                                        }
                                        if (inout.GetDescription() != null && inout.GetDescription().Contains("{->"))
                                        {
                                            //inoutLine.SetIsReversedCostCalculated(true);
                                        }
                                        //inoutLine.SetIsCostCalculated(true);
                                        //if (client.IsCostImmediate() && !inoutLine.IsCostImmediate())
                                        {
                                            inoutLine.SetIsCostImmediate(true);
                                        }

                                        // Update Landed Cost 
                                        if (costingCheck.ExpectedLandedCost != 0 && inoutLine.Get_ColumnIndex("VAS_LandedCost") >= 0)
                                        {
                                            inoutLine.Set_Value("VAS_LandedCost", costingCheck.ExpectedLandedCost);
                                        }

                                        if (!inoutLine.Save(Get_Trx()))
                                        {
                                            ValueNamePair pp = VLogger.RetrieveError();
                                            _log.Info("Error found for Material Receipt for this Line ID = " + inoutLine.GetM_InOutLine_ID() +
                                                       " Error Name is " + pp.GetName() + " And Error Type is " + pp.GetType());
                                            Get_Trx().Rollback();
                                        }
                                        else
                                        {
                                            // Transaction Update Query
                                            if (!query.ToString().Contains("ProductApproxCost"))
                                            {
                                                query.Append(" ProductApproxCost = " + currentCostPrice);
                                            }
                                            if (isUpdatePostCurrentcostPriceFromMR)
                                            {
                                                // when selected costing method is average po, weighted average po, last po
                                                // else to be updated from invoice
                                                query.Append(" , ProductCost = " + currentCostPrice);
                                            }
                                            query.Append(" , M_CostElement_ID = " + costingCheck.definedCostingElement);
                                            query.Append(" , CostingLevel = " + GlobalVariable.TO_STRING(costingCheck.costinglevel));
                                            query.Append(", VAS_LandedCost = " + costingCheck.ExpectedLandedCost);
                                            query.Append(" , VAS_PostingCost = " + costingCheck.OrderLineAmtinBaseCurrency);
                                            query.Append(" WHERE M_Transaction_ID = " + costingCheck.M_Transaction_ID);
                                            if (IsCostUpdation)
                                            {
                                                DB.ExecuteQuery(query.ToString(), null, Get_Trx());
                                            }

                                            _log.Fine("Cost Calculation updated for m_inoutline = " + inoutLine.GetM_InOutLine_ID());
                                            Get_Trx().Commit();
                                        }
                                    }
                                    #endregion
                                }
                            }
                            #endregion
                        }
                    }
                    catch
                    {

                    }
                }
            }
            //sql.Clear();
            //sql.Append("SELECT COUNT(M_InOutLine_ID) FROM M_InOutLine WHERE  IsActive = 'Y' AND M_InOut_ID = " + inout.GetM_InOut_ID());
            //if (inout.IsReversal())
            //{
            //    sql.Append(" AND IsReversedCostCalculated = 'N' ");
            //}
            //else
            //{
            //    sql.Append(@" AND IsCostImmediate = 'N' ");
            //}
            //if (Util.GetValueOfInt(DB.ExecuteScalar(sql.ToString(), null, Get_Trx())) <= 0)
            //{
            //    if (inout.IsReversal())
            //    {
            //        inout.SetIsReversedCostCalculated(true);
            //    }
            //    inout.SetIsCostCalculated(true);
            //    if (!inout.Save(Get_Trx()))
            //    {
            //        ValueNamePair pp = VLogger.RetrieveError();
            //        _log.Info("Error found for saving M_inout for this Record ID = " + inout.GetM_InOut_ID() +
            //                   " Error Name is " + pp.GetName() + " And Error Type is " + pp.GetType());

            //    }
            //    else
            //    {
            //        _log.Fine("Cost Calculation updated for m_inout = " + inout.GetM_InOut_ID());
            //        Get_Trx().Commit();
            //    }
            //}
        }

        /// <summary>
        /// This function is used to calculate the Purchadse Invoice Costing
        /// </summary>
        /// <param name="M_Inout_ID">Record ID</param>
        private void CalculateCostForPurchaseInvoice(int M_Inout_ID)
        {
            if (VAS_IsInvoiceRecostonGRNDate)
            {
                GetInvoices(M_Inout_ID);

                if (dsInvoice != null && dsInvoice.Tables.Count > 0 && dsInvoice.Tables[0].Rows.Count > 0)
                {
                    for (int inv = 0; inv < dsInvoice.Tables[0].Rows.Count; inv++)
                    {
                        #region Cost Calculation for SO / CRMA / VRMA
                        try
                        {
                            if (Util.GetValueOfString(dsInvoice.Tables[0].Rows[inv]["TableName"]) == "C_Invoice" &&
                                           (Util.GetValueOfString(dsInvoice.Tables[0].Rows[inv]["docstatus"]) == "CO" ||
                                            Util.GetValueOfString(dsInvoice.Tables[0].Rows[inv]["docstatus"]) == "CL"))
                            {
                                CalculateCostForInvoice(Util.GetValueOfInt(dsInvoice.Tables[0].Rows[inv]["Record_Id"]));
                                continue;
                            }
                        }
                        catch { }
                        #endregion

                        #region Cost Calculation for  PO Cycle --
                        try
                        {
                            if (Util.GetValueOfString(dsInvoice.Tables[0].Rows[inv]["TableName"]) == "M_MatchInv" &&
                                (Util.GetValueOfString(dsInvoice.Tables[0].Rows[inv]["issotrx"]) == "N" &&
                                 Util.GetValueOfString(dsInvoice.Tables[0].Rows[inv]["isreturntrx"]) == "N"))
                            {

                                CalculateCostForMatchInvoiced(Util.GetValueOfInt(dsInvoice.Tables[0].Rows[inv]["Record_Id"]));

                                continue;
                            }
                        }
                        catch { }
                        #endregion

                        #region Cost Calculation Against AP Credit Memo - During Return Cycle of Purchase
                        try
                        {
                            if (Util.GetValueOfString(dsInvoice.Tables[0].Rows[inv]["TableName"]) == "M_MatchInv" &&
                                (Util.GetValueOfString(dsInvoice.Tables[0].Rows[inv]["issotrx"]) == "N" &&
                                 Util.GetValueOfString(dsInvoice.Tables[0].Rows[inv]["isreturntrx"]) == "Y"))
                            {

                                CalculationCostCreditMemo(Util.GetValueOfInt(dsInvoice.Tables[0].Rows[inv]["Record_Id"]));

                                continue;
                            }
                        }
                        catch { }
                        #endregion

                        #region Cost Calculation for Landed cost Allocation
                        if (Util.GetValueOfString(dsInvoice.Tables[0].Rows[inv]["TableName"]) == "LandedCost")
                        {
                            CalculateLandedCost(Util.GetValueOfInt(dsInvoice.Tables[0].Rows[inv]["Record_Id"]));
                            continue;
                        }
                        #endregion
                    }
                }
            }
        }

        /// <summary>
        /// This function is used to calculate the Landed Cost 
        /// </summary>
        /// <param name="C_LandedCostAllocation_ID">Record ID</param>
        private void CalculateLandedCost(int C_LandedCostAllocation_ID)
        {
            /*Costing Object*/
            costingCheck = new CostingCheck(GetCtx());
            costingCheck.dsAccountingSchema = costingCheck.GetAccountingSchema(GetAD_Client_ID());

            landedCostAllocation = new MLandedCostAllocation(GetCtx(), C_LandedCostAllocation_ID, Get_Trx());
            MInvoiceLine invoiceLine = new MInvoiceLine(GetCtx(), landedCostAllocation.GetC_InvoiceLine_ID(), Get_Trx());
            ProductInvoiceLineCost = invoiceLine.GetProductLineCost(invoiceLine, true);
            MProduct product = MProduct.Get(GetCtx(), landedCostAllocation.GetM_Product_ID());

            costingCheck.invoiceline = invoiceLine;
            costingCheck.product = product;

            if (!MCostQueue.CreateProductCostsDetails(GetCtx(), landedCostAllocation.GetAD_Client_ID(), landedCostAllocation.GetAD_Org_ID(), product,
                 0, "LandedCost", null, null, null, invoiceLine, null, ProductInvoiceLineCost, 0, Get_Trx(), costingCheck, out conversionNotFoundInvoice, optionalstr: "window"))
            {
                if (!conversionNotFoundInvoice1.Contains(conversionNotFoundInvoice))
                {
                    conversionNotFoundInvoice1 += conversionNotFoundInvoice + " , ";
                }
                _log.Info("Cost not Calculated for landedCost for this Line ID = " + landedCostAllocation.GetC_InvoiceLine_ID() +
                        " , " + (!string.IsNullOrEmpty(costingCheck.errorMessage) ? costingCheck.errorMessage : ""));
            }
            else
            {
                _log.Fine("Cost Calculation updated for m_invoiceline = " + landedCostAllocation.GetC_InvoiceLine_ID());
                Get_Trx().Commit();
            }
        }

        /// <summary>
        /// Is used to reverese the impact of cost which comes through Material Reeceipt
        /// </summary>
        /// <param name="M_Inout_ID">Inout reference</param>
        private void CalculateCostForMaterialReversal(int M_Inout_ID)
        {
            inout = new MInOut(GetCtx(), M_Inout_ID, Get_Trx());

            sql.Clear();
            sql.Append(@"SELECT il.* , ilma.M_AttributeSetInstance_ID AS M_AttributeSetInstance_IDMA , ilma.M_Transaction_ID, ilma.MovementQty AS MovementQtyMA 
                            FROM M_InoutLine il INNER JOIN M_InOutLineMA ilma ON (il.M_InoutLine_ID = ilma.M_InoutLine_ID) 
                        WHERE il.IsActive = 'Y' AND il.M_Inout_ID = " + inout.GetM_InOut_ID());
            if (inout.IsReversal())
            {
                sql.Append(" AND il.iscostcalculated = 'Y' AND il.IsReversedCostCalculated = 'N' ");
            }
            else
            {
                sql.Append(" AND il.iscostcalculated = 'N' ");
                sql.Append(" AND il.iscostImmediate = 'N' ");
            }
            if (!String.IsNullOrEmpty(productCategoryID) && String.IsNullOrEmpty(productID))
            {
                sql.Append(" AND il.M_Product_ID IN (SELECT M_Product_ID FROM M_Product WHERE M_Product_Category_ID IN (" + productCategoryID + " ) ) ");
            }
            else if (!String.IsNullOrEmpty(productID))
            {
                sql.Append(" AND il.M_Product_ID IN (" + productID + " )");
            }
            if (M_AttributeSetInstance_ID > 0)
            {
                sql.Append(" AND NVL(ilma.M_AttributeSetInstance_ID, 0) = " + M_AttributeSetInstance_ID);
            }
            sql.Append(" ORDER BY il.Line");
            dsChildRecord = DB.ExecuteDataset(sql.ToString(), null, Get_Trx());
            if (dsChildRecord != null && dsChildRecord.Tables.Count > 0 && dsChildRecord.Tables[0].Rows.Count > 0)
            {
                /*Costing Object*/
                costingCheck = new CostingCheck(GetCtx());
                costingCheck.dsAccountingSchema = costingCheck.GetAccountingSchema(GetAD_Client_ID());
                costingCheck.inout = inout;
                costingCheck.isReversal = inout.IsReversal();

                for (int j = 0; j < dsChildRecord.Tables[0].Rows.Count; j++)
                {
                    try
                    {
                        //VIS_0045: Reset Class parameters
                        if (costingCheck != null)
                        {
                            costingCheck.ResetProperty();
                        }

                        inoutLine = new MInOutLine(GetCtx(), dsChildRecord.Tables[0].Rows[j], Get_Trx());
                        orderLine = new MOrderLine(GetCtx(), inoutLine.GetC_OrderLine_ID(), null);
                        if (orderLine != null && orderLine.GetC_Order_ID() > 0)
                        {
                            order = new MOrder(GetCtx(), orderLine.GetC_Order_ID(), null);
                            if (order.GetDocStatus() != "VO")
                            {
                                if (orderLine.GetQtyOrdered() == 0)
                                    continue;
                            }
                            ProductOrderLineCost = orderLine.GetProductLineCost(orderLine);
                            ProductOrderPriceActual = ProductOrderLineCost / orderLine.GetQtyEntered();
                        }
                        product = new MProduct(GetCtx(), Util.GetValueOfInt(dsChildRecord.Tables[0].Rows[j]["M_Product_ID"]), Get_Trx());
                        if (product.GetProductType() == "I") // for Item Type product
                        {
                            isCostAdjustableOnLost = product.IsCostAdjustmentOnLost();
                            costingCheck.AD_Org_ID = inoutLine.GetAD_Org_ID();
                            costingCheck.M_ASI_ID = Util.GetValueOfInt(dsChildRecord.Tables[0].Rows[j]["M_AttributeSetInstance_IDMA"]);
                            costingCheck.M_Warehouse_ID = inout.GetM_Warehouse_ID();
                            costingCheck.M_Transaction_ID = Util.GetValueOfInt(dsChildRecord.Tables[0].Rows[j]["M_Transaction_ID"]);
                            costingCheck.inoutline = inoutLine;
                            costingCheck.orderline = orderLine;
                            costingCheck.order = order;
                            costingCheck.product = product;

                            // Transaction Update Query
                            query.Clear();
                            query.Append("Update M_Transaction SET ");

                            #region Material Receipt
                            if (!inout.IsSOTrx() && !inout.IsReturnTrx())
                            {
                                if (orderLine == null || orderLine.GetC_OrderLine_ID() == 0)
                                {
                                    #region get price from m_cost (Current Cost Price)
                                    if (!client.IsCostImmediate() || inoutLine.GetCurrentCostPrice() == 0)
                                    {
                                        // get price from m_cost (Current Cost Price)
                                        currentCostPrice = 0;
                                        currentCostPrice = MCost.GetproductCosts(inoutLine.GetAD_Client_ID(), inoutLine.GetAD_Org_ID(),
                                            inoutLine.GetM_Product_ID(), inoutLine.GetM_AttributeSetInstance_ID(), Get_Trx(), inout.GetM_Warehouse_ID());
                                        if (IsCostUpdation)
                                        {
                                            inoutLine.SetCurrentCostPrice(currentCostPrice);
                                            if (!inoutLine.Save(Get_Trx()))
                                            {
                                                ValueNamePair pp = VLogger.RetrieveError();
                                                _log.Info("Error found for Material Receipt for this Line ID = " + inoutLine.GetM_InOutLine_ID() +
                                                           " Error Name is " + pp.GetName() + " And Error Type is " + pp.GetType());
                                                Get_Trx().Rollback();
                                            }
                                        }

                                        // Transaction Update Query
                                        if (currentCostPrice != 0)
                                        {
                                            // this column will be added when current cost available else to be added after cost calculation
                                            query.Append(" ProductApproxCost = " + currentCostPrice);
                                        }
                                    }
                                    #endregion

                                    if (!MCostQueue.CreateProductCostsDetails(GetCtx(), inout.GetAD_Client_ID(), inout.GetAD_Org_ID(), product, costingCheck.M_ASI_ID,
                                   "Material Receipt", null, inoutLine, null, null, null, 0, Util.GetValueOfDecimal(dsChildRecord.Tables[0].Rows[j]["MovementQtyMA"]),
                                   Get_Trx(), costingCheck, out conversionNotFoundInOut, optionalstr: "window"))
                                    {
                                        if (!conversionNotFoundInOut1.Contains(conversionNotFoundInOut))
                                        {
                                            conversionNotFoundInOut1 += conversionNotFoundInOut + " , ";
                                        }
                                        _log.Info("Cost not Calculated for Material Receipt for this Line ID = " + inoutLine.GetM_InOutLine_ID() +
                                            " , " + (!string.IsNullOrEmpty(costingCheck.errorMessage) ? costingCheck.errorMessage : ""));
                                        ListReCalculatedRecords.Add(new ReCalculateRecord { WindowName = (int)windowName.MaterialReceipt, HeaderId = M_Inout_ID, LineId = inoutLine.GetM_InOutLine_ID(), IsReversal = true });
                                    }
                                    else
                                    {
                                        if (inoutLine.GetCurrentCostPrice() == 0)
                                        {
                                            // get price from m_cost (Current Cost Price)
                                            currentCostPrice = 0;
                                            currentCostPrice = MCost.GetproductCosts(inoutLine.GetAD_Client_ID(), inoutLine.GetAD_Org_ID(),
                                                inoutLine.GetM_Product_ID(), inoutLine.GetM_AttributeSetInstance_ID(), Get_Trx(), inout.GetM_Warehouse_ID());
                                            if (IsCostUpdation)
                                            {
                                                inoutLine.SetCurrentCostPrice(currentCostPrice);
                                            }
                                        }
                                        if (inout.GetDescription() != null && inout.GetDescription().Contains("{->"))
                                        {
                                            //inoutLine.SetIsReversedCostCalculated(true);
                                        }
                                        //inoutLine.SetIsCostCalculated(true);
                                        //if (client.IsCostImmediate() && !inoutLine.IsCostImmediate())
                                        {
                                            inoutLine.SetIsCostImmediate(true);
                                        }
                                        if (!inoutLine.Save(Get_Trx()))
                                        {
                                            ValueNamePair pp = VLogger.RetrieveError();
                                            _log.Info("Error found for Material Receipt for this Line ID = " + inoutLine.GetM_InOutLine_ID() +
                                                       " Error Name is " + pp.GetName() + " And Error Type is " + pp.GetType());
                                            Get_Trx().Rollback();
                                        }
                                        else
                                        {
                                            // Transaction Update Query
                                            if (!query.ToString().Contains("ProductApproxCost"))
                                            {
                                                query.Append(" ProductApproxCost = " + currentCostPrice);
                                            }
                                            query.Append(" , M_CostElement_ID = " + costingCheck.definedCostingElement);
                                            query.Append(" , CostingLevel = " + GlobalVariable.TO_STRING(costingCheck.costinglevel));
                                            query.Append(" , VAS_PostingCost = " + costingCheck.OrderLineAmtinBaseCurrency);
                                            query.Append(" WHERE M_Transaction_ID = " + costingCheck.M_Transaction_ID);
                                            if (IsCostUpdation)
                                            {
                                                DB.ExecuteQuery(query.ToString(), null, Get_Trx());
                                            }

                                            _log.Fine("Cost Calculation updated for M_InoutLine = " + inoutLine.GetM_InOutLine_ID());
                                            Get_Trx().Commit();
                                        }
                                    }
                                }
                                else
                                {
                                    #region get price from m_cost (Current Cost Price)
                                    if (!client.IsCostImmediate() || inoutLine.GetCurrentCostPrice() == 0)
                                    {
                                        // get price from m_cost (Current Cost Price)
                                        currentCostPrice = 0;
                                        currentCostPrice = MCost.GetproductCosts(inoutLine.GetAD_Client_ID(), inoutLine.GetAD_Org_ID(),
                                            inoutLine.GetM_Product_ID(), inoutLine.GetM_AttributeSetInstance_ID(), Get_Trx(), inout.GetM_Warehouse_ID());
                                        if (IsCostUpdation)
                                        {
                                            inoutLine.SetCurrentCostPrice(currentCostPrice);
                                            if (!inoutLine.Save(Get_Trx()))
                                            {
                                                ValueNamePair pp = VLogger.RetrieveError();
                                                _log.Info("Error found for Material Receipt for this Line ID = " + inoutLine.GetM_InOutLine_ID() +
                                                           " Error Name is " + pp.GetName() + " And Error Type is " + pp.GetType());
                                                Get_Trx().Rollback();
                                            }
                                        }

                                        // Transaction Update Query
                                        if (currentCostPrice != 0)
                                        {
                                            // this column will be added when current cost available else to be added after cost calculation
                                            query.Append(" ProductApproxCost = " + currentCostPrice);
                                        }
                                    }
                                    #endregion

                                    amt = 0;
                                    if (isCostAdjustableOnLost && inoutLine.GetMovementQty() < orderLine.GetQtyOrdered() && order.GetDocStatus() != "VO")
                                    {
                                        if (inoutLine.GetMovementQty() > 0)
                                            amt = ProductOrderLineCost;
                                        else
                                            amt = Decimal.Negate(ProductOrderLineCost);
                                    }
                                    else if (!isCostAdjustableOnLost && inoutLine.GetMovementQty() < orderLine.GetQtyOrdered() && order.GetDocStatus() != "VO")
                                    {
                                        amt = Decimal.Multiply(Decimal.Divide(ProductOrderLineCost, orderLine.GetQtyOrdered()), inoutLine.GetMovementQty());
                                    }
                                    else if (order.GetDocStatus() != "VO")
                                    {
                                        amt = Decimal.Multiply(Decimal.Divide(ProductOrderLineCost, orderLine.GetQtyOrdered()), inoutLine.GetMovementQty());
                                    }
                                    else if (order.GetDocStatus() == "VO")
                                    {
                                        amt = Decimal.Multiply(ProductOrderPriceActual, inoutLine.GetQtyEntered());
                                    }

                                    if (!MCostQueue.CreateProductCostsDetails(GetCtx(), inout.GetAD_Client_ID(), inout.GetAD_Org_ID(), product, costingCheck.M_ASI_ID,
                                       "Material Receipt", null, inoutLine, null, null, null, amt, Util.GetValueOfDecimal(dsChildRecord.Tables[0].Rows[j]["MovementQtyMA"]),
                                       Get_Trx(), costingCheck, out conversionNotFoundInOut, optionalstr: "window"))
                                    {
                                        if (!conversionNotFoundInOut1.Contains(conversionNotFoundInOut))
                                        {
                                            conversionNotFoundInOut1 += conversionNotFoundInOut + " , ";
                                        }
                                        _log.Info("Cost not Calculated for Material Receipt for this Line ID = " + inoutLine.GetM_InOutLine_ID() +
                                            " , " + (!string.IsNullOrEmpty(costingCheck.errorMessage) ? costingCheck.errorMessage : ""));
                                        ListReCalculatedRecords.Add(new ReCalculateRecord { WindowName = (int)windowName.MaterialReceipt, HeaderId = M_Inout_ID, LineId = inoutLine.GetM_InOutLine_ID(), IsReversal = true });
                                    }
                                    else
                                    {
                                        if (inoutLine.GetCurrentCostPrice() == 0)
                                        {
                                            // get price from m_cost (Current Cost Price)
                                            currentCostPrice = 0;
                                            currentCostPrice = MCost.GetproductCosts(inoutLine.GetAD_Client_ID(), inoutLine.GetAD_Org_ID(),
                                                inoutLine.GetM_Product_ID(), inoutLine.GetM_AttributeSetInstance_ID(), Get_Trx(), inout.GetM_Warehouse_ID());
                                            if (IsCostUpdation)
                                            {
                                                inoutLine.SetCurrentCostPrice(currentCostPrice);
                                            }
                                        }
                                        if (inout.GetDescription() != null && inout.GetDescription().Contains("{->"))
                                        {
                                            //inoutLine.SetIsReversedCostCalculated(true);
                                        }
                                        // inoutLine.SetIsCostCalculated(true);
                                        //if (client.IsCostImmediate() && !inoutLine.IsCostImmediate())
                                        {
                                            inoutLine.SetIsCostImmediate(true);
                                        }
                                        if (!inoutLine.Save(Get_Trx()))
                                        {
                                            ValueNamePair pp = VLogger.RetrieveError();
                                            _log.Info("Error found for Material Receipt for this Line ID = " + inoutLine.GetM_InOutLine_ID() +
                                                       " Error Name is " + pp.GetName() + " And Error Type is " + pp.GetType());
                                            Get_Trx().Rollback();
                                        }
                                        else
                                        {
                                            // Transaction Update Query
                                            if (!query.ToString().Contains("ProductApproxCost"))
                                            {
                                                query.Append(" ProductApproxCost = " + currentCostPrice);
                                            }
                                            query.Append(" , M_CostElement_ID = " + costingCheck.definedCostingElement);
                                            query.Append(" , CostingLevel = " + GlobalVariable.TO_STRING(costingCheck.costinglevel));
                                            query.Append(" , VAS_PostingCost = " + costingCheck.OrderLineAmtinBaseCurrency);
                                            query.Append(" WHERE M_Transaction_ID = " + costingCheck.M_Transaction_ID);
                                            if (IsCostUpdation)
                                            {
                                                DB.ExecuteQuery(query.ToString(), null, Get_Trx());
                                            }

                                            _log.Fine("Cost Calculation updated for M_InoutLine = " + inoutLine.GetM_InOutLine_ID());
                                            Get_Trx().Commit();
                                        }
                                    }
                                }
                            }
                            #endregion
                        }
                    }
                    catch { }
                }
            }
            //sql.Clear();
            //sql.Append("SELECT COUNT(M_InOutLine_ID) FROM M_InOutLine WHERE  IsActive = 'Y' AND M_InOut_ID = " + inout.GetM_InOut_ID());
            //if (inout.IsReversal())
            //{
            //    sql.Append(" AND IsReversedCostCalculated = 'N' ");
            //}
            //else
            //{
            //    sql.Append(@" AND IsCostCalculated = 'N' ");
            //}
            //if (Util.GetValueOfInt(DB.ExecuteScalar(sql.ToString(), null, Get_Trx())) <= 0)
            //{
            //    if (inout.IsReversal())
            //    {
            //        inout.SetIsReversedCostCalculated(true);
            //    }
            //    inout.SetIsCostCalculated(true);
            //    if (!inout.Save(Get_Trx()))
            //    {
            //        ValueNamePair pp = VLogger.RetrieveError();
            //        _log.Info("Error found for saving M_inout for this Record ID = " + inout.GetM_InOut_ID() +
            //                   " Error Name is " + pp.GetName() + " And Error Type is " + pp.GetType());
            //    }
            //    else
            //    {
            //        _log.Fine("Cost Calculation updated for M_Inout = " + inout.GetM_InOut_ID());
            //        Get_Trx().Commit();
            //    }
            //}
        }

        /// <summary>
        /// Is used to caculate cost agaisnt match invoiced
        /// </summary>
        /// <param name="M_MatchInv_ID">match invoice reference</param>
        public void CalculateCostForMatchInvoiced(int M_MatchInv_ID)
        {
            /*Costing Object*/
            costingCheck = new CostingCheck(GetCtx());
            costingCheck.dsAccountingSchema = costingCheck.GetAccountingSchema(GetAD_Client_ID());

            matchInvoice = new MMatchInv(GetCtx(), M_MatchInv_ID, Get_Trx());
            inoutLine = new MInOutLine(GetCtx(), matchInvoice.GetM_InOutLine_ID(), Get_Trx());
            invoiceLine = new MInvoiceLine(GetCtx(), matchInvoice.GetC_InvoiceLine_ID(), Get_Trx());
            invoice = new MInvoice(GetCtx(), invoiceLine.GetC_Invoice_ID(), Get_Trx());
            product = new MProduct(GetCtx(), invoiceLine.GetM_Product_ID(), Get_Trx());
            int M_Warehouse_Id = inoutLine.GetM_Warehouse_ID();
            if (invoiceLine != null && invoiceLine.Get_ID() > 0)
            {
                ProductInvoiceLineCost = invoiceLine.GetProductLineCost(invoiceLine, true);
            }
            if (inoutLine.GetC_OrderLine_ID() > 0)
            {
                orderLine = new MOrderLine(GetCtx(), inoutLine.GetC_OrderLine_ID(), Get_Trx());
                order = new MOrder(GetCtx(), orderLine.GetC_Order_ID(), Get_Trx());
                ProductOrderLineCost = orderLine.GetProductLineCost(orderLine);
                ProductOrderPriceActual = ProductOrderLineCost / orderLine.GetQtyEntered();

                costingCheck.order = order;
                costingCheck.orderline = orderLine;
            }
            if (product.GetProductType() == "I" && product.GetM_Product_ID() > 0)
            {
                bool isUpdatePostCurrentcostPriceFromMR = MCostElement.IsPOCostingmethod(GetCtx(), product.GetAD_Client_ID(), product.GetM_Product_ID(), Get_Trx());
                if (countColumnExist > 0)
                {
                    isCostAdjustableOnLost = product.IsCostAdjustmentOnLost();
                }

                costingCheck.AD_Org_ID = matchInvoice.GetAD_Org_ID();
                costingCheck.M_Warehouse_ID = M_Warehouse_Id;
                costingCheck.M_ASI_ID = inoutLine.GetM_AttributeSetInstance_ID();
                costingCheck.inoutline = inoutLine;
                costingCheck.inout = inoutLine.GetParent();
                costingCheck.invoiceline = invoiceLine;
                costingCheck.invoice = invoice;
                costingCheck.product = product;
                costingCheck.IsPOCostingethodBindedonProduct = isUpdatePostCurrentcostPriceFromMR;

                // calculate cost of MR first if not calculate which is linked with that invoice line
                //if (!inoutLine.IsCostCalculated())
                if (!inoutLine.IsCostImmediate())
                {
                    // Transaction Update Query
                    query.Clear();
                    query.Append("Update M_Transaction SET ");

                    if (inoutLine.GetCurrentCostPrice() == 0)
                    {
                        // get price from m_cost (Current Cost Price)
                        currentCostPrice = 0;
                        currentCostPrice = MCost.GetproductCostAndQtyMaterial(inoutLine.GetAD_Client_ID(), inoutLine.GetAD_Org_ID(),
                            inoutLine.GetM_Product_ID(), inoutLine.GetM_AttributeSetInstance_ID(), Get_Trx(), M_Warehouse_Id, false);
                        _log.Info("product cost " + inoutLine.GetM_Product_ID() + " - " + currentCostPrice);
                        if (IsCostUpdation)
                        {
                            DB.ExecuteQuery("UPDATE M_Inoutline SET CurrentCostPrice = " + currentCostPrice + " WHERE M_Inoutline_ID = " + inoutLine.GetM_InOutLine_ID(), null, Get_Trx());
                        }
                    }

                    if (!MCostQueue.CreateProductCostsDetails(GetCtx(), inoutLine.GetAD_Client_ID(), inoutLine.GetAD_Org_ID(), product, inoutLine.GetM_AttributeSetInstance_ID(),
                        "Material Receipt", null, inoutLine, null, invoiceLine, null,
                        order != null && order.GetDocStatus() != "VO" ? Decimal.Multiply(Decimal.Divide(ProductOrderLineCost, orderLine.GetQtyOrdered()), inoutLine.GetMovementQty())
                        : Decimal.Multiply(ProductOrderPriceActual, inoutLine.GetQtyEntered()),
                inoutLine.GetMovementQty(), Get_Trx(), costingCheck, out conversionNotFoundInOut, optionalstr: "window"))
                    {
                        if (!conversionNotFoundInOut1.Contains(conversionNotFoundInOut))
                        {
                            conversionNotFoundInOut1 += conversionNotFoundInOut + " , ";
                        }
                        _log.Info("Cost not Calculated for Material Receipt for this Line ID = " + inoutLine.GetM_InOutLine_ID() +
                                            " , " + (!string.IsNullOrEmpty(costingCheck.errorMessage) ? costingCheck.errorMessage : ""));
                        ListReCalculatedRecords.Add(new ReCalculateRecord { WindowName = (int)windowName.MaterialReceipt, HeaderId = inoutLine.GetM_InOut_ID(), LineId = inoutLine.GetM_InOutLine_ID(), IsReversal = false });
                        return;
                    }
                    else
                    {
                        if (currentCostPrice != 0)
                        {
                            query.Append(" ProductApproxCost = " + currentCostPrice);
                        }
                        if (isUpdatePostCurrentcostPriceFromMR || inoutLine.GetCurrentCostPrice() == 0)
                        {
                            // get price from m_cost (Current Cost Price)
                            currentCostPrice = 0;
                            currentCostPrice = MCost.GetproductCostAndQtyMaterial(inoutLine.GetAD_Client_ID(), inoutLine.GetAD_Org_ID(),
                                inoutLine.GetM_Product_ID(), inoutLine.GetM_AttributeSetInstance_ID(), Get_Trx(), M_Warehouse_Id, false);
                        }
                        if (inoutLine.GetCurrentCostPrice() == 0 && IsCostUpdation)
                        {
                            _log.Info("product cost " + inoutLine.GetM_Product_ID() + " - " + currentCostPrice);
                            //DB.ExecuteQuery("UPDATE M_Inoutline SET CurrentCostPrice = " + currentCostPrice + " WHERE M_Inoutline_ID = " + inoutLine.GetM_InOutLine_ID(), null, Get_Trx());
                            inoutLine.SetCurrentCostPrice(currentCostPrice);
                        }
                        if (isUpdatePostCurrentcostPriceFromMR && inoutLine.GetPostCurrentCostPrice() == 0 && IsCostUpdation)
                        {
                            inoutLine.SetPostCurrentCostPrice(currentCostPrice);
                        }
                        //inoutLine.SetIsCostCalculated(true);
                        inoutLine.SetIsCostImmediate(true);
                        if (!inoutLine.Save(Get_Trx()))
                        {
                            ValueNamePair pp = VLogger.RetrieveError();
                            _log.Info("Error found for saving Invoice(Vendor) for this Line ID = " + invoiceLine.GetC_InvoiceLine_ID() +
                                       " Error Name is " + pp.GetName() + " And Error Type is " + pp.GetType());
                            Get_Trx().Rollback();
                            return;
                        }
                        else
                        {
                            // Transaction Update Query
                            if (!query.ToString().Contains("ProductApproxCost"))
                            {
                                query.Append(" ProductApproxCost = " + currentCostPrice);
                            }
                            if (isUpdatePostCurrentcostPriceFromMR)
                            {
                                query.Append(" , ProductCost = " + currentCostPrice);
                            }
                            query.Append(" , M_CostElement_ID = " + costingCheck.definedCostingElement);
                            query.Append(" , CostingLevel = " + GlobalVariable.TO_STRING(costingCheck.costinglevel));
                            query.Append(" , VAS_PostingCost = " + costingCheck.OrderLineAmtinBaseCurrency);
                            query.Append($@" WHERE M_Transaction_ID IN (SELECT M_Transaction_ID FROM M_InoutLineMA 
                                                WHERE M_InOutLine_ID = {inoutLine.GetM_InOutLine_ID()})");
                            if (IsCostUpdation)
                            {
                                DB.ExecuteQuery(query.ToString(), null, Get_Trx());
                            }
                            Get_Trx().Commit();
                        }
                    }
                }

                if (matchInvoice.Get_ColumnIndex("CurrentCostPrice") >= 0) { }

                // when isCostAdjustableOnLost = true on product and movement qty on MR is less than invoice qty then consider MR qty else invoice qty
                if (!MCostQueue.CreateProductCostsDetails(GetCtx(), invoiceLine.GetAD_Client_ID(), invoiceLine.GetAD_Org_ID(), product, invoiceLine.GetM_AttributeSetInstance_ID(),
                      "Invoice(Vendor)", null, inoutLine, null, invoiceLine, null,
                    isCostAdjustableOnLost && matchInvoice.GetQty() < invoiceLine.GetQtyInvoiced() ? (matchInvoice.GetQty() < 0 ? -1 : 1) * ProductInvoiceLineCost
                    : Decimal.Multiply(Decimal.Divide(ProductInvoiceLineCost, invoiceLine.GetQtyInvoiced()), matchInvoice.GetQty()),
                      matchInvoice.GetQty(), Get_Trx(), costingCheck, out conversionNotFoundInvoice, optionalstr: "window"))
                {
                    if (!conversionNotFoundInvoice1.Contains(conversionNotFoundInvoice))
                    {
                        conversionNotFoundInvoice1 += conversionNotFoundInvoice + " , ";
                    }
                    _log.Info("Cost not Calculated for Invoice(Vendor) for this Line ID = " + invoiceLine.GetC_InvoiceLine_ID() +
                                            " , " + (!string.IsNullOrEmpty(costingCheck.errorMessage) ? costingCheck.errorMessage : ""));
                    ListReCalculatedRecords.Add(new ReCalculateRecord { WindowName = (int)windowName.MatchInvoice, HeaderId = M_MatchInv_ID, LineId = invoiceLine.GetC_InvoiceLine_ID(), IsReversal = false });
                }
                else
                {
                    if (invoice.GetDescription() != null && invoice.GetDescription().Contains("{->"))
                    {
                        //invoiceLine.SetIsReversedCostCalculated(true);
                    }

                    if (IsCostUpdation)
                    {
                        invoiceLine.SetCurrentCostPrice(currentCostPrice);
                    }

                    // get cost from Product Cost after cost calculation
                    currentCostPrice = MCost.GetproductCostAndQtyMaterial(GetAD_Client_ID(), GetAD_Org_ID(),
                                                             product.GetM_Product_ID(), invoiceLine.GetM_AttributeSetInstance_ID(), Get_Trx(), M_Warehouse_Id, false);
                    if (IsCostUpdation)
                    {
                        invoiceLine.SetPostCurrentCostPrice(currentCostPrice);
                        if (!isUpdatePostCurrentcostPriceFromMR)
                        {
                            invoiceLine.SetCurrentCostPrice(currentCostPrice);
                        }
                    }

                    //invoiceLine.SetIsCostCalculated(true);
                    //if (client.IsCostImmediate() && !invoiceLine.IsCostImmediate())
                    {
                        query.Clear();
                        query.Append($@"SELECT CASE WHEN SUM(mi.Qty) = il.QtyInvoiced THEN 'Y' ELSE 'N' END AS IsCstImmediate 
                                                FROM M_MatchInv mi
                                                INNER JOIN C_InvoiceLine il ON (mi.C_InvoiceLine_id = il.C_InvoiceLine_id)
                                                WHERE il.C_InvoiceLine_ID={invoiceLine.GetC_InvoiceLine_ID()} GROUP BY il.QtyInvoiced ");
                        bool isCostImmediate = Util.GetValueOfString(DB.ExecuteScalar(query.ToString(), null, Get_Trx())).Equals("Y");
                        invoiceLine.SetIsCostImmediate(isCostImmediate);
                    }
                    if (!invoiceLine.Save(Get_Trx()))
                    {
                        ValueNamePair pp = VLogger.RetrieveError();
                        _log.Info("Error found for saving Invoice(Vendor) for this Line ID = " + invoiceLine.GetC_InvoiceLine_ID() +
                                   " Error Name is " + pp.GetName() + " And Error Type is " + pp.GetType());
                        Get_Trx().Rollback();
                    }
                    else
                    {
                        _log.Fine("Cost Calculation updated for m_invoiceline = " + invoiceLine.GetC_InvoiceLine_ID());
                        Get_Trx().Commit();

                        if (matchInvoice.Get_ColumnIndex("PostCurrentCostPrice") >= 0 && IsCostUpdation)
                        {
                            // get post cost after invoice cost calculation and update on match invoice
                            matchInvoice.SetPostCurrentCostPrice(currentCostPrice);
                        }
                        // set is cost calculation true on match invoice
                        //matchInvoice.SetIsCostCalculated(true);
                        matchInvoice.SetIsCostImmediate(true);

                        if (matchInvoice.Get_ColumnIndex("QueueQty") >= 0 && costingCheck != null && costingCheck.currentQtyonQueue != null)
                        {
                            matchInvoice.Set_Value("QueueQty", costingCheck.currentQtyonQueue);
                            matchInvoice.Set_Value("ConsumedQty", Decimal.Subtract(Util.GetValueOfDecimal(matchInvoice.GetQty()), costingCheck.currentQtyonQueue.Value));
                        }
                        else if (costingCheck.onHandQty == 0)
                        {
                            // 03-Mar-2025, for Costing Method other than LIFO / FIFO 
                            // and all stock out before receiving Invoice
                            matchInvoice.Set_Value("ConsumedQty", matchInvoice.GetQty());
                        }
                        else if (costingCheck != null && costingCheck.currentQtyonQueue == null && costingCheck.onHandQty == null)
                        {
                            matchInvoice.Set_Value("ConsumedQty", 0);
                        }

                        if (!matchInvoice.Save(Get_Trx()))
                        {
                            ValueNamePair pp = VLogger.RetrieveError();
                            _log.Info("Error found for saving Invoice(Vendor) for this Line ID = " + matchInvoice.GetC_InvoiceLine_ID() +
                                       " Error Name is " + pp.GetName() + " And Error Type is " + pp.GetType());
                            Get_Trx().Rollback();
                        }
                        else
                        {
                            Get_Trx().Commit();
                            // update the latest cost ON MR (Post Cost)
                            if (!isUpdatePostCurrentcostPriceFromMR && IsCostUpdation)
                            {
                                DB.ExecuteQuery("UPDATE M_InoutLine SET PostCurrentCostPrice = " + matchInvoice.GetPostCurrentCostPrice() +
                                        @" WHERE M_InoutLine_ID = " + inoutLine.GetM_InOutLine_ID(), null, Get_Trx());

                                DB.ExecuteQuery($"Update M_Transaction SET ProductCost = " + matchInvoice.GetPostCurrentCostPrice() +
                                        $@" WHERE M_Transaction_ID IN (SELECT M_Transaction_ID FROM M_InoutLineMA 
                                                WHERE M_InOutLine_ID = {inoutLine.GetM_InOutLine_ID()})", null, Get_Trx());
                                Get_Trx().Commit();
                            }

                            // calculate Pre Cost - means cost before updation price impact of current record
                            if (matchInvoice != null && matchInvoice.GetM_MatchInv_ID() > 0 && matchInvoice.Get_ColumnIndex("CurrentCostPrice") >= 0 && IsCostUpdation)
                            {
                                currentCostPrice = Util.GetValueOfDecimal(DB.ExecuteScalar(@"SELECT M_InOutLine.PostCurrentCostPrice FROM M_InOutLine 
                                                WHERE M_InOutLine.M_InOutLIne_ID = " + inoutLine.GetM_InOutLine_ID(), null, Get_Trx()));
                                DB.ExecuteQuery("UPDATE M_MatchInv SET CurrentCostPrice = " + currentCostPrice +
                                                 @" WHERE M_MatchInv_ID = " + matchInvoice.GetM_MatchInv_ID(), null, Get_Trx());

                            }

                            // Update Product Cost on Product Transaction for the Invoice Line
                            UpdateTransactionCostForInvoice(matchInvoice.GetPostCurrentCostPrice(), matchInvoice.GetC_InvoiceLine_ID(), costingCheck);
                            Get_Trx().Commit();
                        }
                    }
                }
            }
        }

        private bool UpdateTransactionCostForInvoice(decimal ProductCost, int C_InvoiceLine_ID, CostingCheck costingCheck)
        {
            if (IsCostUpdation)
            {
                string sql = $@"Update M_Transaction SET ProductCost = {ProductCost},
                                    M_CostElement_ID = {costingCheck.definedCostingElement}, 
                                    CostingLevel = {GlobalVariable.TO_STRING(costingCheck.costinglevel)}";

                if (costingCheck.materialCostingMethod.Equals(MProductCategory.COSTINGMETHOD_Fifo) ||
                   costingCheck.materialCostingMethod.Equals(MProductCategory.COSTINGMETHOD_Lifo))
                {
                    if (costingCheck.currentQtyonQueue != 0)
                    {
                        sql += $", VAS_PostingCost = {costingCheck.DifferenceAmtPOandInvInBaseCurrency} ";
                    }
                }
                else if (costingCheck.onHandQty != 0)
                {
                    sql += $", VAS_PostingCost = {costingCheck.DifferenceAmtPOandInvInBaseCurrency} ";
                }


                if (costingCheck.invoice != null && costingCheck.invoice.Get_ID() > 0)
                {
                    if (Util.GetValueOfBool(costingCheck.invoice.IsReturnTrx()))
                    {
                        sql += ", VAS_IsCreditNote = 'Y' ";
                    }
                    if (Util.GetValueOfBool(costingCheck.invoice.Get_Value("TreatAsDiscount")))
                    {
                        sql += ", TreatAsDiscount = 'Y' ";
                    }
                    if (costingCheck.invoiceline != null && costingCheck.invoiceline.Get_ID() > 0 && Util.GetValueOfBool(costingCheck.invoiceline.Get_Value("VAS_IsLandedCost")))
                    {
                        sql += ", VAS_IsLandedCost = 'Y' ";
                        sql += $", ProductCost = {costingCheck.PostCurrentCostPrice} ";
                    }
                }
                sql += $@" WHERE C_InvoiceLine_ID = {C_InvoiceLine_ID}";
                return DB.ExecuteQuery(sql, null, Get_Trx()) >= 0;
            }
            return true;
        }

        /// <summary>
        /// Cost Calculation for  PO Cycle - Reversal 
        /// Is used to reverse the impact of cost which is to be calculated througt invoice vendor (Match Invoiced)
        /// </summary>
        /// <param name="M_MatchInvCostTrack_ID">M_MatchInvCostTrack_ID reference</param>
        private void CalculateCostForMatchInvoiceReversal(int M_MatchInvCostTrack_ID)
        {
            /*Costing Object*/
            costingCheck = new CostingCheck(GetCtx());
            costingCheck.dsAccountingSchema = costingCheck.GetAccountingSchema(GetAD_Client_ID());

            matchInvCostReverse = new X_M_MatchInvCostTrack(GetCtx(), M_MatchInvCostTrack_ID, Get_Trx());
            inoutLine = new MInOutLine(GetCtx(), matchInvCostReverse.GetM_InOutLine_ID(), Get_Trx());
            invoiceLine = new MInvoiceLine(GetCtx(), matchInvCostReverse.GetRev_C_InvoiceLine_ID(), Get_Trx());
            invoice = new MInvoice(GetCtx(), invoiceLine.GetC_Invoice_ID(), Get_Trx());
            if (invoiceLine != null && invoiceLine.Get_ID() > 0)
            {
                ProductInvoiceLineCost = invoiceLine.GetProductLineCost(invoiceLine, true);
            }
            product = new MProduct(GetCtx(), invoiceLine.GetM_Product_ID(), Get_Trx());
            if (inoutLine.GetC_OrderLine_ID() > 0)
            {
                orderLine = new MOrderLine(GetCtx(), inoutLine.GetC_OrderLine_ID(), Get_Trx());
                order = new MOrder(GetCtx(), orderLine.GetC_Order_ID(), Get_Trx());

                costingCheck.order = order;
                costingCheck.orderline = orderLine;
            }
            if (product.GetProductType() == "I" && product.GetM_Product_ID() > 0)
            {
                if (countColumnExist > 0)
                {
                    isCostAdjustableOnLost = product.IsCostAdjustmentOnLost();
                }

                costingCheck.AD_Org_ID = matchInvoice.GetAD_Org_ID();
                costingCheck.M_Warehouse_ID = inoutLine.GetM_Warehouse_ID(); ;
                costingCheck.M_ASI_ID = inoutLine.GetM_AttributeSetInstance_ID();
                costingCheck.inoutline = inoutLine;
                costingCheck.inout = inoutLine.GetParent();
                costingCheck.invoiceline = invoiceLine;
                costingCheck.invoice = invoice;
                costingCheck.product = product;

                // when isCostAdjustableOnLost = true on product and movement qty on MR is less than invoice qty then consider MR qty else invoice qty
                if (!MCostQueue.CreateProductCostsDetails(GetCtx(), invoiceLine.GetAD_Client_ID(), invoiceLine.GetAD_Org_ID(), product, invoiceLine.GetM_AttributeSetInstance_ID(),
                      "Invoice(Vendor)", null, inoutLine, null, invoiceLine, null,
                    isCostAdjustableOnLost && matchInvCostReverse.GetQty() < Decimal.Negate(invoiceLine.GetQtyInvoiced()) ? ProductInvoiceLineCost : Decimal.Negate(Decimal.Multiply(Decimal.Divide(ProductInvoiceLineCost, invoiceLine.GetQtyInvoiced()), matchInvCostReverse.GetQty())),
                     decimal.Negate(matchInvCostReverse.GetQty()),
                      Get_Trx(), costingCheck, out conversionNotFoundInvoice, optionalstr: "window"))
                {
                    if (!conversionNotFoundInvoice1.Contains(conversionNotFoundInvoice))
                    {
                        conversionNotFoundInvoice1 += conversionNotFoundInvoice + " , ";
                    }
                    _log.Info("Cost not Calculated for Invoice(Vendor) for this Line ID = " + invoiceLine.GetC_InvoiceLine_ID() +
                                            " , " + (!string.IsNullOrEmpty(costingCheck.errorMessage) ? costingCheck.errorMessage : ""));
                    ListReCalculatedRecords.Add(new ReCalculateRecord { WindowName = (int)windowName.MatchInvoice, HeaderId = M_MatchInvCostTrack_ID, LineId = invoiceLine.GetC_InvoiceLine_ID(), IsReversal = true });
                }
                else
                {
                    //invoiceLine.SetIsReversedCostCalculated(true);
                    //invoiceLine.SetIsCostCalculated(true);
                    //if (client.IsCostImmediate() && !invoiceLine.IsCostImmediate())
                    {
                        invoiceLine.SetIsCostImmediate(true);
                    }
                    if (!invoiceLine.Save(Get_Trx()))
                    {
                        ValueNamePair pp = VLogger.RetrieveError();
                        _log.Info("Error found for saving Invoice(Vendor) for this Line ID = " + invoiceLine.GetC_InvoiceLine_ID() +
                                   " Error Name is " + pp.GetName() + " And Error Type is " + pp.GetType());
                        Get_Trx().Rollback();
                    }
                    else
                    {
                        _log.Fine("Cost Calculation updated for m_invoiceline = " + invoiceLine.GetC_InvoiceLine_ID());
                        Get_Trx().Commit();

                        if (IsCostUpdation)
                        {
                            // update the Post current price after Invoice receving on inoutline
                            DB.ExecuteQuery(@"UPDATE M_InoutLine SET PostCurrentCostPrice = 0 
                                                                  WHERE M_InoutLine_ID = " + matchInvCostReverse.GetM_InOutLine_ID(), null, Get_Trx());

                            DB.ExecuteQuery($"Update M_Transaction SET ProductCost = " + 0 +
                                           $@" WHERE M_Transaction_ID IN (SELECT M_Transaction_ID FROM M_InoutLineMA 
                                                WHERE M_InOutLine_ID = {inoutLine.GetM_InOutLine_ID()})", null, Get_Trx());
                        }

                        // set is cost calculation true on match invoice
                        if (!matchInvCostReverse.Delete(true, Get_Trx()))
                        {
                            ValueNamePair pp = VLogger.RetrieveError();
                            _log.Info(" Delete Record M_MatchInvCostTrack -- Error Name is " + pp.GetName() + " And Error Type is " + pp.GetType());
                            Get_Trx().Rollback();
                        }
                        else
                        {
                            Get_Trx().Commit();
                        }

                        // get cost from Product Cost after cost calculation, and update on Product Transaction against Invoice
                        currentCostPrice = MCost.GetproductCosts(GetAD_Client_ID(), GetAD_Org_ID(),
                                                                 product.GetM_Product_ID(), invoiceLine.GetM_AttributeSetInstance_ID(), Get_Trx(), inout.GetM_Warehouse_ID());
                        UpdateTransactionCostForInvoice(currentCostPrice, invoiceLine.GetC_InvoiceLine_ID(), costingCheck);
                        Get_Trx().Commit();
                    }
                }
            }
        }

        /// <summary>
        /// Is used to calculate cost for Physical Inventory or for Internal Use Inventory
        /// </summary>
        /// <param name="M_Inventory_ID">Inventory reference</param>
        private void CalculateCostForInventory(int M_Inventory_ID)
        {
            inventory = new MInventory(GetCtx(), M_Inventory_ID, Get_Trx());
            sql.Clear();
            sql.Append(@"SELECT il.* , ilma.M_AttributeSetInstance_ID AS M_AttributeSetInstance_IDMA , ilma.M_Transaction_ID, ilma.MovementQty AS MovementQtyMA 
                              FROM M_InventoryLine il INNER JOIN M_InventoryLineMA ilma ON (il.M_InventoryLine_ID = ilma.M_InventoryLine_ID) 
                            WHERE il.IsActive = 'Y' AND il.M_Inventory_ID = " + inventory.GetM_Inventory_ID());
            if (inventory.IsReversal())
            {
                sql.Append(" AND il.iscostcalculated = 'Y' AND il.IsReversedCostCalculated = 'N' ");
            }
            else
            {
                sql.Append(" AND il.iscostcalculated = 'N' ");
                sql.Append(" AND il.iscostImmediate = 'N' ");
            }

            if (!String.IsNullOrEmpty(productCategoryID) && String.IsNullOrEmpty(productID))
            {
                sql.Append(" AND il.M_Product_ID IN (SELECT M_Product_ID FROM M_Product WHERE M_Product_Category_ID IN (" + productCategoryID + " ) ) ");
            }
            else
            {
                sql.Append(" AND il.M_Product_ID IN (" + productID + " )");
            }
            if (M_AttributeSetInstance_ID > 0)
            {
                sql.Append(" AND NVL(ilma.M_AttributeSetInstance_ID, 0) = " + M_AttributeSetInstance_ID);
            }
            sql.Append(" ORDER BY il.Line");
            dsChildRecord = DB.ExecuteDataset(sql.ToString(), null, Get_Trx());
            if (dsChildRecord != null && dsChildRecord.Tables.Count > 0 && dsChildRecord.Tables[0].Rows.Count > 0)
            {
                /*Costing Object*/
                costingCheck = new CostingCheck(GetCtx());
                costingCheck.dsAccountingSchema = costingCheck.GetAccountingSchema(GetAD_Client_ID());
                costingCheck.inventory = inventory;
                costingCheck.isReversal = inventory.IsReversal();

                for (int j = 0; j < dsChildRecord.Tables[0].Rows.Count; j++)
                {
                    //VIS_0045: Reset Class parameters
                    if (costingCheck != null)
                    {
                        costingCheck.ResetProperty();
                    }

                    product = new MProduct(GetCtx(), Util.GetValueOfInt(dsChildRecord.Tables[0].Rows[j]["M_Product_ID"]), Get_Trx());
                    inventoryLine = new MInventoryLine(GetCtx(), dsChildRecord.Tables[0].Rows[j], Get_Trx());
                    if (product.GetProductType() == "I") // for Item Type product
                    {
                        costingCheck.AD_Org_ID = inventoryLine.GetAD_Org_ID();
                        costingCheck.M_ASI_ID = Util.GetValueOfInt(dsChildRecord.Tables[0].Rows[j]["M_AttributeSetInstance_IDMA"]);
                        costingCheck.M_Warehouse_ID = inventory.GetM_Warehouse_ID();
                        costingCheck.M_Transaction_ID = Util.GetValueOfInt(dsChildRecord.Tables[0].Rows[j]["M_Transaction_ID"]);
                        costingCheck.inventoryLine = inventoryLine;
                        costingCheck.product = product;

                        // Transaction Update Query
                        query.Clear();
                        query.Append("Update M_Transaction SET ");

                        costingMethod = MCostElement.CheckLifoOrFifoMethod(GetCtx(), GetAD_Client_ID(), product.GetM_Product_ID(), Get_Trx());
                        quantity = 0;
                        if (inventory.IsInternalUse())
                        {
                            #region for Internal use inventory

                            #region get price from m_cost (Current Cost Price)
                            if (!client.IsCostImmediate() || inventoryLine.GetCurrentCostPrice() == 0)
                            {
                                // get price from m_cost (Current Cost Price)
                                currentCostPrice = 0;
                                currentCostPrice = MCost.GetproductCosts(inventoryLine.GetAD_Client_ID(), inventoryLine.GetAD_Org_ID(),
                                    inventoryLine.GetM_Product_ID(), costingCheck.M_ASI_ID, Get_Trx(), inventory.GetM_Warehouse_ID());
                                if (IsCostUpdation)
                                {
                                    DB.ExecuteQuery("UPDATE M_InventoryLine SET CurrentCostPrice = " + currentCostPrice + @"
                                                   WHERE M_InventoryLine_ID = " + inventoryLine.GetM_InventoryLine_ID(), null, Get_Trx());
                                }

                                // Transaction Update Query
                                if (currentCostPrice != 0)
                                {
                                    // this column will be added when current cost available else to be added after cost calculation
                                    query.Append(" ProductApproxCost = " + currentCostPrice);
                                }
                            }
                            #endregion

                            quantity = Decimal.Negate(Util.GetValueOfDecimal(dsChildRecord.Tables[0].Rows[j]["MovementQtyMA"]));
                            //VIS:045: 12-Aug-2024, DevOps Task ID - 6102, Provide Cost from the inventory line if defined
                            // Change by mohit - Client id and organization was passed from context but neede to be passed from document itself as done in several other documents.-27/06/2017
                            if (!MCostQueue.CreateProductCostsDetails(GetCtx(), inventory.GetAD_Client_ID(), inventory.GetAD_Org_ID(), product, costingCheck.M_ASI_ID,
                           "Internal Use Inventory", inventoryLine, null, null, null, null,
                           ((quantity < 0 && !inventory.IsReversal()) || (quantity > 0 && inventory.IsReversal())) ? Util.GetValueOfDecimal(inventoryLine.Get_Value("PriceCost")) : 0,
                           quantity, Get_Trx(), costingCheck, out conversionNotFoundInventory, optionalstr: "window"))
                            {
                                if (!conversionNotFoundInventory1.Contains(conversionNotFoundInventory))
                                {
                                    conversionNotFoundInventory1 += conversionNotFoundInventory + " , ";
                                }
                                _log.Info("Cost not Calculated for Internal Use Inventory for this Line ID = " + inventoryLine.GetM_InventoryLine_ID() +
                                            " , " + (!string.IsNullOrEmpty(costingCheck.errorMessage) ? costingCheck.errorMessage : ""));
                                ListReCalculatedRecords.Add(new ReCalculateRecord { WindowName = (int)windowName.Inventory, HeaderId = M_Inventory_ID, LineId = inventoryLine.GetM_InventoryLine_ID(), IsReversal = false });
                            }
                            else
                            {
                                if (costingMethod != "" && quantity < 0)
                                {
                                    currentCostPrice = MCost.GetLifoAndFifoCurrentCostFromCostQueueTransaction(GetCtx(), inventoryLine.GetAD_Client_ID(), inventoryLine.GetAD_Org_ID(),
                                                       inventoryLine.GetM_Product_ID(), costingCheck.M_ASI_ID, 1,
                                                       inventoryLine.GetM_InventoryLine_ID(), costingMethod,
                                                       inventory.GetM_Warehouse_ID(), true, Get_Trx());
                                    if (IsCostUpdation)
                                    {
                                        inventoryLine.SetCurrentCostPrice(currentCostPrice);
                                    }
                                    if (query.ToString().Replace("Update M_Transaction SET ", "").Trim().Length > 0)
                                    {
                                        query.Append(" , VAS_PostingCost = " + currentCostPrice);
                                    }
                                    else
                                    {
                                        query.Append("  VAS_PostingCost = " + currentCostPrice + ", ");
                                    }
                                }

                                // when post current cost price is ZERO, than need to update cost here 
                                if (inventoryLine.GetPostCurrentCostPrice() == 0)
                                {
                                    currentCostPrice = MCost.GetproductCosts(inventoryLine.GetAD_Client_ID(), inventoryLine.GetAD_Org_ID(),
                                      inventoryLine.GetM_Product_ID(), costingCheck.M_ASI_ID, Get_Trx(), inventory.GetM_Warehouse_ID());
                                    if (IsCostUpdation)
                                    {
                                        inventoryLine.SetPostCurrentCostPrice(currentCostPrice);
                                    }
                                }
                                if (inventory.IsReversal())
                                {
                                    //inventoryLine.SetIsReversedCostCalculated(true);
                                }
                                //inventoryLine.SetIsCostCalculated(true);
                                //if (client.IsCostImmediate() && !inventoryLine.IsCostImmediate())
                                {
                                    inventoryLine.SetIsCostImmediate(true);
                                }
                                if (!inventoryLine.Save(Get_Trx()))
                                {
                                    ValueNamePair pp = VLogger.RetrieveError();
                                    _log.Info("Error found for saving Internal Use Inventory for this Line ID = " + inventoryLine.GetM_InventoryLine_ID() +
                                               " Error Name is " + pp.GetName() + " And Error Type is " + pp.GetType());
                                    Get_Trx().Rollback();
                                }
                                else
                                {
                                    // Transaction Update Query
                                    if (!query.ToString().Contains("ProductApproxCost"))
                                    {
                                        query.Append(" ProductApproxCost = " + currentCostPrice);
                                    }

                                    query.Append(" , ProductCost = " + currentCostPrice);
                                    query.Append(" , M_CostElement_ID = " + costingCheck.definedCostingElement);
                                    query.Append(" , CostingLevel = " + GlobalVariable.TO_STRING(costingCheck.costinglevel));
                                    if (!query.ToString().Contains("VAS_PostingCost"))
                                    {
                                        query.Append(" , VAS_PostingCost = " + currentCostPrice);
                                    }
                                    query.Append(" WHERE M_Transaction_ID = " + costingCheck.M_Transaction_ID);
                                    if (IsCostUpdation)
                                    {
                                        DB.ExecuteQuery(query.ToString(), null, Get_Trx());
                                    }

                                    _log.Fine("Cost Calculation updated for M_InventoryLine = " + inventoryLine.GetM_InventoryLine_ID());
                                    Get_Trx().Commit();
                                }
                            }
                            #endregion
                        }
                        else
                        {
                            #region for Physical Inventory

                            if (Decimal.Subtract(inventoryLine.GetQtyCount(), inventoryLine.GetQtyBook()) > 0)
                            {
                                quantity = Util.GetValueOfDecimal(dsChildRecord.Tables[0].Rows[j]["MovementQtyMA"]);
                            }
                            else
                            {
                                quantity = Decimal.Negate(Util.GetValueOfDecimal(dsChildRecord.Tables[0].Rows[j]["MovementQtyMA"]));
                            }


                            #region get price from m_cost (Current Cost Price)
                            if (!client.IsCostImmediate() || inventoryLine.GetCurrentCostPrice() == 0)
                            {
                                // get price from m_cost (Current Cost Price)
                                currentCostPrice = 0;
                                currentCostPrice = MCost.GetproductCosts(inventoryLine.GetAD_Client_ID(), inventoryLine.GetAD_Org_ID(),
                                    inventoryLine.GetM_Product_ID(), costingCheck.M_ASI_ID, Get_Trx(), inventory.GetM_Warehouse_ID());
                                if (IsCostUpdation)
                                {
                                    DB.ExecuteQuery("UPDATE M_InventoryLine SET CurrentCostPrice = " + currentCostPrice + @"
                                                   WHERE M_InventoryLine_ID = " + inventoryLine.GetM_InventoryLine_ID(), null, Get_Trx());
                                }

                                // Transaction Update Query
                                if (currentCostPrice != 0)
                                {
                                    // this column will be added when current cost available else to be added after cost calculation
                                    query.Append(" ProductApproxCost = " + currentCostPrice);
                                }
                            }
                            #endregion

                            //VIS:045: 12-Aug-2024, DevOps Task ID - 6102, Provide Cost from the inventory line if defined
                            if (!MCostQueue.CreateProductCostsDetails(GetCtx(), inventory.GetAD_Client_ID(), inventory.GetAD_Org_ID(), product, costingCheck.M_ASI_ID,
                           "Physical Inventory", inventoryLine, null, null, null, null, Util.GetValueOfDecimal(inventoryLine.Get_Value("PriceCost")), quantity,
                           Get_Trx(), costingCheck, out conversionNotFoundInventory, optionalstr: "window"))
                            {
                                if (!conversionNotFoundInventory1.Contains(conversionNotFoundInventory))
                                {
                                    conversionNotFoundInventory1 += conversionNotFoundInventory + " , ";
                                }
                                _log.Info("Cost not Calculated for Physical Inventory for this Line ID = " + inventoryLine.GetM_InventoryLine_ID() +
                                            " , " + (!string.IsNullOrEmpty(costingCheck.errorMessage) ? costingCheck.errorMessage : ""));
                                ListReCalculatedRecords.Add(new ReCalculateRecord { WindowName = (int)windowName.Inventory, HeaderId = M_Inventory_ID, LineId = inventoryLine.GetM_InventoryLine_ID(), IsReversal = false });
                            }
                            else
                            {
                                if (costingMethod != "" && quantity < 0)
                                {
                                    currentCostPrice = MCost.GetLifoAndFifoCurrentCostFromCostQueueTransaction(GetCtx(), inventoryLine.GetAD_Client_ID(), inventoryLine.GetAD_Org_ID(),
                                                       inventoryLine.GetM_Product_ID(), costingCheck.M_ASI_ID, 1,
                                                       inventoryLine.GetM_InventoryLine_ID(), costingMethod,
                                                       inventory.GetM_Warehouse_ID(), true, Get_Trx());
                                    if (IsCostUpdation)
                                    {
                                        inventoryLine.SetCurrentCostPrice(currentCostPrice);
                                    }

                                    if (query.ToString().Replace("Update M_Transaction SET ", "").Trim().Length > 0)
                                    {
                                        query.Append(" , VAS_PostingCost = " + currentCostPrice);
                                    }
                                    else
                                    {
                                        query.Append("  VAS_PostingCost = " + currentCostPrice + ", ");
                                    }
                                }

                                // when post current cost price is ZERO, than need to update cost here 
                                if (inventoryLine.GetPostCurrentCostPrice() == 0)
                                {
                                    currentCostPrice = MCost.GetproductCosts(inventoryLine.GetAD_Client_ID(), inventoryLine.GetAD_Org_ID(),
                                  inventoryLine.GetM_Product_ID(), costingCheck.M_ASI_ID, Get_Trx(), inventory.GetM_Warehouse_ID());
                                    if (IsCostUpdation)
                                    {
                                        inventoryLine.SetPostCurrentCostPrice(currentCostPrice);
                                    }
                                }
                                if (inventory.GetDescription() != null && inventory.GetDescription().Contains("{->"))
                                {
                                    //inventoryLine.SetIsReversedCostCalculated(true);
                                }
                                //inventoryLine.SetIsCostCalculated(true);
                                //if (client.IsCostImmediate() && !inventoryLine.IsCostImmediate())
                                {
                                    inventoryLine.SetIsCostImmediate(true);
                                }
                                if (!inventoryLine.Save(Get_Trx()))
                                {
                                    ValueNamePair pp = VLogger.RetrieveError();
                                    _log.Info("Error found for saving Internal Use Inventory for this Line ID = " + inventoryLine.GetM_InventoryLine_ID() +
                                               " Error Name is " + pp.GetName() + " And Error Type is " + pp.GetType());
                                    Get_Trx().Rollback();
                                }
                                else
                                {
                                    // Transaction Update Query
                                    if (!query.ToString().Contains("ProductApproxCost"))
                                    {
                                        query.Append(" ProductApproxCost = " + currentCostPrice);
                                    }

                                    query.Append(" , ProductCost = " + currentCostPrice);
                                    query.Append(" , M_CostElement_ID = " + costingCheck.definedCostingElement);
                                    query.Append(" , CostingLevel = " + GlobalVariable.TO_STRING(costingCheck.costinglevel));
                                    if (!query.ToString().Contains("VAS_PostingCost"))
                                    {
                                        query.Append(" , VAS_PostingCost = " + (Util.GetValueOfDecimal(inventoryLine.Get_Value("PriceCost")) != 0 ?
                                                        Util.GetValueOfDecimal(inventoryLine.Get_Value("PriceCost")) : currentCostPrice));
                                    }
                                    query.Append(" WHERE M_Transaction_ID = " + costingCheck.M_Transaction_ID);
                                    if (IsCostUpdation)
                                    {
                                        DB.ExecuteQuery(query.ToString(), null, Get_Trx());
                                    }

                                    _log.Fine("Cost Calculation updated for M_InventoryLine = " + inventoryLine.GetM_InventoryLine_ID());
                                    Get_Trx().Commit();
                                }
                            }
                            #endregion
                        }
                    }
                }
            }
            //sql.Clear();
            //sql.Append("SELECT COUNT(M_InventoryLine_ID) FROM M_InventoryLine WHERE IsActive = 'Y' AND M_Inventory_ID = " + inventory.GetM_Inventory_ID());
            //if (inventory.IsReversal())
            //{
            //    sql.Append(" AND IsReversedCostCalculated = 'N'  ");
            //}
            //else
            //{
            //    sql.Append(" AND IsCostCalculated = 'N'  ");
            //}

            //if (Util.GetValueOfInt(DB.ExecuteScalar(sql.ToString(), null, Get_Trx())) <= 0)
            //{
            //    if (inventory.IsReversal())
            //    {
            //        inventory.SetIsReversedCostCalculated(true);
            //    }
            //    inventory.SetIsCostCalculated(true);
            //    if (!inventory.Save(Get_Trx()))
            //    {
            //        ValueNamePair pp = VLogger.RetrieveError();
            //        if (pp != null)
            //            _log.Info("Error found for saving Internal Use Inventory for this Record ID = " + inventory.GetM_Inventory_ID() +
            //                       " Error Name is " + pp.GetName());
            //    }
            //    else
            //    {
            //        _log.Fine("Cost Calculation updated for M_Inventory = " + inventory.GetM_Inventory_ID());
            //        Get_Trx().Commit();
            //    }
            //}
        }

        /// <summary>
        /// Is used to calculate cost for AssetDisposal
        /// </summary>
        /// <param name="VAFAM_AssetDisposal_ID">AssetDisposal reference</param>
        /// <param name="M_Product_ID">AssetDisposal reference</param>
        private void CalculateCostForAssetDisposal(int VAFAM_AssetDisposal_ID, int M_Product_ID)
        {
            #region for Asset Disposal
            try
            {
                /*Costing Object*/
                costingCheck = new CostingCheck(GetCtx());
                costingCheck.dsAccountingSchema = costingCheck.GetAccountingSchema(GetAD_Client_ID());
                if (costingCheck != null)
                {
                    costingCheck.ResetProperty();
                }
                costingCheck.po = po_AssetDisposal;
                costingCheck.isReversal = po_AssetDisposal.Get_ValueAsInt("ReversalDoc_ID") > 0 ? true : false;

                product = new MProduct(GetCtx(), M_Product_ID, Get_Trx());
                if (product.GetProductType() == "I") // for Item Type product
                {
                    costingCheck.product = product;
                    costingCheck.AD_Org_ID = Util.GetValueOfInt(po_AssetDisposal.Get_Value("AD_Org_ID"));
                    costingCheck.M_ASI_ID = Util.GetValueOfInt(po_AssetDisposal.Get_Value("M_AttributeSetInstance_ID"));
                    costingCheck.M_Warehouse_ID = Util.GetValueOfInt(po_AssetDisposal.Get_Value("M_Warehouse_ID"));
                    costingCheck.M_Transaction_ID = Util.GetValueOfInt(DB.ExecuteScalar($@"SELECT M_Transaction_ID FROM M_Transaction 
                    WHERE VAFAM_AssetDisposal_ID = {VAFAM_AssetDisposal_ID}"));

                    // check costing method is LIFO or FIFO
                    String costingMethod = MCostElement.CheckLifoOrFifoMethod(GetCtx(), GetAD_Client_ID(), product.GetM_Product_ID(), Get_Trx());

                    // Transaction Update Query
                    query.Clear();
                    query.Append("Update M_Transaction SET ");

                    quantity = 0;
                    quantity = Decimal.Negate(Util.GetValueOfDecimal(po_AssetDisposal.Get_Value("VAFAM_Qty")));
                    if (!MCostQueue.CreateProductCostsDetails(GetCtx(), Util.GetValueOfInt(po_AssetDisposal.Get_Value("AD_Client_ID")), Util.GetValueOfInt(po_AssetDisposal.Get_Value("AD_Org_ID")),
                        product, Util.GetValueOfInt(po_AssetDisposal.Get_Value("M_AttributeSetInstance_ID")), "AssetDisposal",
                        null, null, null, null, po_AssetDisposal, 0, quantity, Get_Trx(), costingCheck, out conversionNotFoundInventory, optionalstr: "window"))
                    {
                        _log.Info("Cost not Calculated for Asset Dispose ID = " + VAFAM_AssetDisposal_ID +
                                            " , " + (!string.IsNullOrEmpty(costingCheck.errorMessage) ? costingCheck.errorMessage : ""));
                        ListReCalculatedRecords.Add(new ReCalculateRecord { WindowName = (int)windowName.AssetDisposal, HeaderId = VAFAM_AssetDisposal_ID, LineId = VAFAM_AssetDisposal_ID, IsReversal = false });
                    }
                    else
                    {
                        if (Util.GetValueOfInt(po_AssetDisposal.Get_Value("ReversalDoc_ID")) > 0)
                        {
                            //DB.ExecuteQuery("UPDATE VAFAM_AssetDisposal SET IsReversedCostCalculated='Y' WHERE VAFAM_AssetDisposal_ID = " + VAFAM_AssetDisposal_ID, null, Get_Trx());
                        }
                        else
                        {
                            //DB.ExecuteQuery("UPDATE VAFAM_AssetDisposal SET ISCostCalculated='Y' WHERE VAFAM_AssetDisposal_ID = " + VAFAM_AssetDisposal_ID, null, Get_Trx());
                            DB.ExecuteQuery("UPDATE VAFAM_AssetDisposal SET ISCostImmediate='Y' WHERE VAFAM_AssetDisposal_ID = " + VAFAM_AssetDisposal_ID, null, Get_Trx());
                        }

                        // Update M_Transaction with Cost Details
                        Decimal currentCostPrice = 0;
                        if (costingMethod != "" && Decimal.Negate(quantity) < 0)
                        {
                            currentCostPrice = MCost.GetLifoAndFifoCurrentCostFromCostQueueTransaction(GetCtx(), GetAD_Client_ID(), GetAD_Org_ID(),
                                              product.GetM_Product_ID(), costingCheck.M_ASI_ID, 4, VAFAM_AssetDisposal_ID, costingMethod,
                                               costingCheck.M_Warehouse_ID, true, Get_Trx());
                        }

                        if (currentCostPrice != 0)
                        {
                            query.Append($" ProductApproxCost = {currentCostPrice}");
                        }
                        if (currentCostPrice != 0)
                        {
                            currentCostPrice = MCost.GetproductCosts(GetAD_Client_ID(), GetAD_Org_ID(),
                                        product.GetM_Product_ID(), costingCheck.M_ASI_ID, Get_Trx(), costingCheck.M_Warehouse_ID);
                        }
                        if (!query.ToString().Contains("ProductApproxCost"))
                        {
                            query.Append($" ProductApproxCost = {currentCostPrice}");
                        }
                        query.Append($", ProductCost = {currentCostPrice}");
                        query.Append($", M_CostElement_ID = {costingCheck.definedCostingElement}");
                        query.Append($" , CostingLevel =  { GlobalVariable.TO_STRING(costingCheck.costinglevel)}");
                        query.Append($", VAS_PostingCost = {currentCostPrice}");
                        query.Append($" WHERE M_Transaction_ID = { costingCheck.M_Transaction_ID}");
                        if (IsCostUpdation)
                        {
                            DB.ExecuteQuery(query.ToString(), null, Get_Trx());
                        }

                        _log.Fine("Cost Calculation updated for VAFAM_AssetDispoal= " + VAFAM_AssetDisposal_ID);
                        Get_Trx().Commit();
                    }
                }
            }
            catch { }
            #endregion

        }

        /// <summary>
        /// Is used to calculate cost againt Inventory Move
        /// </summary>
        /// <param name="M_Movement_ID">Movement id reference</param>
        private void CalculateCostForMovement(int M_Movement_ID)
        {
            movement = new MMovement(GetCtx(), M_Movement_ID, Get_Trx());

            sql.Clear();
            sql.Append(@"SELECT il.* , ilma.M_AttributeSetInstance_ID AS M_AttributeSetInstance_IDMA , ilma.M_Transaction_ID, ilma.MovementQty AS MovementQtyMA,  
                            M_TransactionTo_ID 
                            FROM M_MovementLine il INNER JOIN M_MovementLineMA ilma ON (il.M_MovementLine_ID = ilma.M_MovementLine_ID) 
                         WHERE il.IsActive = 'Y' AND il.M_Movement_ID = " + movement.GetM_Movement_ID());
            if (movement.IsReversal())
            {
                sql.Append(" AND il.iscostcalculated = 'Y' AND il.IsReversedCostCalculated = 'N' ");
            }
            else
            {
                sql.Append(" AND il.iscostcalculated = 'N' ");
                sql.Append(" AND il.iscostImmediate = 'N' ");
            }
            if (!String.IsNullOrEmpty(productCategoryID) && String.IsNullOrEmpty(productID))
            {
                sql.Append(" AND il.M_Product_ID IN (SELECT M_Product_ID FROM M_Product WHERE M_Product_Category_ID IN (" + productCategoryID + " ) ) ");
            }
            else
            {
                sql.Append(" AND il.M_Product_ID IN (" + productID + " )");
            }
            if (M_AttributeSetInstance_ID > 0)
            {
                sql.Append(" AND NVL(ilma.M_AttributeSetInstance_ID, 0) = " + M_AttributeSetInstance_ID);
            }
            sql.Append(" ORDER BY il.Line");
            dsChildRecord = DB.ExecuteDataset(sql.ToString(), null, Get_Trx());
            if (dsChildRecord != null && dsChildRecord.Tables.Count > 0 && dsChildRecord.Tables[0].Rows.Count > 0)
            {
                /*Costing Object*/
                costingCheck = new CostingCheck(GetCtx());
                costingCheck.dsAccountingSchema = costingCheck.GetAccountingSchema(GetAD_Client_ID());
                costingCheck.movement = movement;
                costingCheck.isReversal = movement.IsReversal();

                for (int j = 0; j < dsChildRecord.Tables[0].Rows.Count; j++)
                {
                    postingCost = 0;
                    //VIS_0045: Reset Class parameters
                    if (costingCheck != null)
                    {
                        costingCheck.ResetProperty();
                    }

                    product = new MProduct(GetCtx(), Util.GetValueOfInt(dsChildRecord.Tables[0].Rows[j]["M_Product_ID"]), Get_Trx());
                    movementLine = new MMovementLine(GetCtx(), dsChildRecord.Tables[0].Rows[j], Get_Trx());
                    locatorTo = MLocator.Get(GetCtx(), Util.GetValueOfInt(dsChildRecord.Tables[0].Rows[j]["M_LocatorTo_ID"]));
                    costingMethod = MCostElement.CheckLifoOrFifoMethod(GetCtx(), GetAD_Client_ID(), product.GetM_Product_ID(), Get_Trx());

                    // Transaction Update Query
                    query.Clear();
                    query.Append("Update M_Transaction SET ");
                    queryTo.Clear();
                    queryTo.Append("Update M_Transaction SET ");

                    #region get price from m_cost (Current Cost Price)
                    if (!client.IsCostImmediate() || movementLine.GetCurrentCostPrice() == 0 || movementLine.GetToCurrentCostPrice() == 0)
                    {
                        currentCostPrice = MCost.GetproductCosts(movementLine.GetAD_Client_ID(), movementLine.GetAD_Org_ID(),
                            movementLine.GetM_Product_ID(), movementLine.GetM_AttributeSetInstance_ID(), Get_Trx(), movement.GetDTD001_MWarehouseSource_ID());
                        postingCost = currentCostPrice;

                        // For To Warehouse
                        toCurrentCostPrice = MCost.GetproductCosts(movementLine.GetAD_Client_ID(), locatorTo.GetAD_Org_ID(),
                           movementLine.GetM_Product_ID(), movementLine.GetM_AttributeSetInstance_ID(), Get_Trx(), locatorTo.GetM_Warehouse_ID());

                        if (IsCostUpdation)
                        {
                            DB.ExecuteQuery("UPDATE M_MovementLine SET  CurrentCostPrice = CASE WHEN CurrentCostPrice <> 0 THEN CurrentCostPrice ELSE " + currentCostPrice +
                            @" END , ToCurrentCostPrice = CASE WHEN ToCurrentCostPrice <> 0 THEN ToCurrentCostPrice ELSE " + toCurrentCostPrice + @"
                                                END  WHERE M_MovementLine_ID = " + movementLine.GetM_MovementLine_ID(), null, Get_Trx());
                        }

                        // Transaction Update Query
                        query.Append(" ProductApproxCost = " + currentCostPrice);
                        queryTo.Append(" ProductApproxCost = " + toCurrentCostPrice);
                    }
                    #endregion

                    // for Item Type product
                    if (product.GetProductType() == "I") //  && movement.GetAD_Org_ID() != warehouse.GetAD_Org_ID()
                    {
                        costingCheck.AD_Org_ID = movementLine.GetAD_Org_ID();
                        costingCheck.M_ASI_ID = Util.GetValueOfInt(dsChildRecord.Tables[0].Rows[j]["M_AttributeSetInstance_IDMA"]);
                        costingCheck.M_Warehouse_ID = movement.GetDTD001_MWarehouseSource_ID();
                        costingCheck.M_Transaction_ID = Util.GetValueOfInt(dsChildRecord.Tables[0].Rows[j]["M_Transaction_ID"]);
                        costingCheck.AD_OrgTo_ID = locatorTo.GetAD_Org_ID();
                        costingCheck.M_WarehouseTo_ID = locatorTo.GetM_Warehouse_ID();
                        costingCheck.M_TransactionTo_ID = Util.GetValueOfInt(dsChildRecord.Tables[0].Rows[j]["M_TransactionTo_ID"]);
                        costingCheck.movementline = movementLine;
                        costingCheck.product = product;

                        #region for inventory move
                        if (!MCostQueue.CreateProductCostsDetails(GetCtx(), movement.GetAD_Client_ID(), movement.GetAD_Org_ID(), product, costingCheck.M_ASI_ID,
                            "Inventory Move", null, null, movementLine, null, null, 0, Util.GetValueOfDecimal(dsChildRecord.Tables[0].Rows[j]["MovementQtyMA"]),
                            Get_Trx(), costingCheck, out conversionNotFoundMovement, optionalstr: "window"))
                        {
                            if (!conversionNotFoundMovement1.Contains(conversionNotFoundMovement))
                            {
                                conversionNotFoundMovement1 += conversionNotFoundMovement + " , ";
                            }
                            _log.Info("Cost not Calculated for Inventory Move for this Line ID = " + movementLine.GetM_MovementLine_ID() +
                                            " , " + (!string.IsNullOrEmpty(costingCheck.errorMessage) ? costingCheck.errorMessage : ""));
                            ListReCalculatedRecords.Add(new ReCalculateRecord { WindowName = (int)windowName.Movement, HeaderId = M_Movement_ID, LineId = movementLine.GetM_MovementLine_ID(), IsReversal = false });
                        }
                        else
                        {
                            if (movement.IsReversal())
                            {
                                //movementLine.SetIsReversedCostCalculated(true);

                                // for Posting Cost
                                if (movementLine.GetMovementQty() > 0)
                                {
                                    postingCost = movementLine.GetCurrentCostPrice();
                                }
                                else
                                {
                                    postingCost = movementLine.GetToCurrentCostPrice();
                                }
                            }
                            else
                            {
                                if (!String.IsNullOrEmpty(costingMethod))
                                {
                                    if (movementLine.GetMovementQty() > 0)
                                    {
                                        currentCostPrice = MCost.GetLifoAndFifoCurrentCostFromCostQueueTransaction(GetCtx(), movementLine.GetAD_Client_ID(), movementLine.GetAD_Org_ID(),
                                                           movementLine.GetM_Product_ID(), costingCheck.M_ASI_ID, 2, movementLine.GetM_MovementLine_ID(), costingMethod,
                                                           movement.GetDTD001_MWarehouseSource_ID(), true, Get_Trx());
                                        if (IsCostUpdation)
                                        {
                                            movementLine.SetCurrentCostPrice(currentCostPrice);
                                        }
                                        postingCost = currentCostPrice;
                                    }

                                    if (movementLine.GetMovementQty() < 0)
                                    {
                                        toCurrentCostPrice = MCost.GetLifoAndFifoCurrentCostFromCostQueueTransaction(GetCtx(), movementLine.GetAD_Client_ID(), movementLine.GetAD_Org_ID(),
                                                               movementLine.GetM_Product_ID(), costingCheck.M_ASI_ID, 2, movementLine.GetM_MovementLine_ID(), costingMethod,
                                                               locatorTo.GetM_Warehouse_ID(), true, Get_Trx());
                                        if (IsCostUpdation)
                                        {
                                            movementLine.SetToCurrentCostPrice(currentCostPrice);
                                        }
                                        postingCost = toCurrentCostPrice;
                                    }
                                }

                                if (movementLine.GetPostCurrentCostPrice() == 0)
                                {
                                    // For From warehouse
                                    currentCostPrice = MCost.GetproductCosts(movementLine.GetAD_Client_ID(), movementLine.GetAD_Org_ID(),
                                        movementLine.GetM_Product_ID(), costingCheck.M_ASI_ID, Get_Trx(), movement.GetDTD001_MWarehouseSource_ID());
                                    if (IsCostUpdation)
                                    {
                                        movementLine.SetPostCurrentCostPrice(currentCostPrice);
                                    }
                                }
                                if (movementLine.GetToPostCurrentCostPrice() == 0)
                                {
                                    // For To Warehouse
                                    toCurrentCostPrice = MCost.GetproductCosts(movementLine.GetAD_Client_ID(), locatorTo.GetAD_Org_ID(),
                                       movementLine.GetM_Product_ID(), costingCheck.M_ASI_ID, Get_Trx(), locatorTo.GetM_Warehouse_ID());
                                    if (IsCostUpdation)
                                    {
                                        movementLine.SetToPostCurrentCostPrice(toCurrentCostPrice);
                                    }
                                }
                            }
                            //movementLine.SetIsCostCalculated(true);
                            //if (client.IsCostImmediate() && !movementLine.IsCostImmediate())
                            {
                                movementLine.SetIsCostImmediate(true);
                            }
                            if (!movementLine.Save(Get_Trx()))
                            {
                                ValueNamePair pp = VLogger.RetrieveError();
                                _log.Info("Error found for saving Inventory Move for this Line ID = " + movementLine.GetM_MovementLine_ID() +
                                           " Error Name is " + pp.GetName() + " And Error Type is " + pp.GetType());
                                Get_Trx().Rollback();
                            }
                            else
                            {
                                // Transaction Update Query
                                if (!query.ToString().Contains("ProductApproxCost"))
                                {
                                    query.Append(" ProductApproxCost = " + currentCostPrice);
                                }
                                if (!queryTo.ToString().Contains("ProductApproxCost"))
                                {
                                    queryTo.Append(" ProductApproxCost = " + toCurrentCostPrice);
                                }
                                query.Append(" , ProductCost = " + currentCostPrice);
                                queryTo.Append(" , ProductCost = " + toCurrentCostPrice);
                                query.Append(" , M_CostElement_ID = " + costingCheck.definedCostingElement);
                                queryTo.Append(" , M_CostElement_ID = " + costingCheck.definedCostingElement);
                                query.Append(" , CostingLevel = " + GlobalVariable.TO_STRING(costingCheck.costinglevel));
                                queryTo.Append(" , CostingLevel = " + GlobalVariable.TO_STRING(costingCheck.costinglevel));
                                query.Append(" , VAS_PostingCost = " + postingCost);
                                queryTo.Append(" , VAS_PostingCost = " + postingCost);
                                query.Append(" WHERE M_Transaction_ID = " + costingCheck.M_Transaction_ID);
                                queryTo.Append(" WHERE M_Transaction_ID = " + costingCheck.M_TransactionTo_ID);
                                if (IsCostUpdation)
                                {
                                    DB.ExecuteQuery(query.ToString(), null, Get_Trx());
                                    DB.ExecuteQuery(queryTo.ToString(), null, Get_Trx());
                                }

                                _log.Fine("Cost Calculation updated for M_MovementLine = " + movementLine.GetM_MovementLine_ID());
                                Get_Trx().Commit();
                            }
                        }
                        #endregion
                    }
                }
            }
            //sql.Clear();
            //sql.Append("SELECT COUNT(M_MovementLine_ID) FROM M_MovementLine WHERE IsActive = 'Y' AND M_Movement_ID = " + movement.GetM_Movement_ID());
            //if (movement.IsReversal())
            //{
            //    sql.Append(@" AND IsReversedCostCalculated = 'N' ");
            //}
            //else
            //{
            //    sql.Append(@" AND IsCostCalculated = 'N' ");
            //}
            //if (Util.GetValueOfInt(DB.ExecuteScalar(sql.ToString(), null, Get_Trx())) <= 0)
            //{
            //    if (movement.IsReversal())
            //    {
            //        movement.SetIsReversedCostCalculated(true);
            //    }
            //    movement.SetIsCostCalculated(true);
            //    if (!movement.Save(Get_Trx()))
            //    {
            //        ValueNamePair pp = VLogger.RetrieveError();
            //        _log.Info("Error found for saving Inventory Move for this Record ID = " + movement.GetM_Movement_ID() +
            //                   " Error Name is " + pp.GetName() + " And Error Type is " + pp.GetType());
            //    }
            //    else
            //    {
            //        _log.Fine("Cost Calculation updated for M_Movement = " + movement.GetM_Movement_ID());
            //        Get_Trx().Commit();
            //    }
            //}
        }

        /// <summary>
        /// Component Reduce of those product which is to be consumed during Production Execution
        /// </summary>
        /// <param name="VAMFG_M_WrkOdrTransaction_ID">Production Execution Reference</param>
        /// <param name="IsSoTrx">During recalculation -- that product is available on product execution line</param>
        private void CalculateCostForProduction(int VAMFG_M_WrkOdrTransaction_ID, String IsSoTrx)
        {
            po_WrkOdrTransaction = tbl_WrkOdrTransaction.GetPO(GetCtx(), VAMFG_M_WrkOdrTransaction_ID, Get_Trx());

            // Production Execution Transaction Type
            woTrxType = Util.GetValueOfString(po_WrkOdrTransaction.Get_Value("VAMFG_WorkOrderTxnType"));

            sql.Clear();
            if (Util.GetValueOfString(po_WrkOdrTransaction.Get_Value("VAMFG_Description")) != null &&
                Util.GetValueOfString(po_WrkOdrTransaction.Get_Value("VAMFG_Description")).Contains("{->") &&
                (woTrxType.Equals(ViennaAdvantage.Model.X_VAMFG_M_WrkOdrTransaction.VAMFG_WORKORDERTXNTYPE_1_ComponentIssueToWorkOrder)
                || woTrxType.Equals(ViennaAdvantage.Model.X_VAMFG_M_WrkOdrTransaction.VAMFG_WORKORDERTXNTYPE_ComponentReturnFromWorkOrder) ||
                (countGOM01 <= 0 && (woTrxType.Equals(ViennaAdvantage.Model.X_VAMFG_M_WrkOdrTransaction.VAMFG_WORKORDERTXNTYPE_3_TransferAssemblyToStore)
                || woTrxType.Equals(ViennaAdvantage.Model.X_VAMFG_M_WrkOdrTransaction.VAMFG_WORKORDERTXNTYPE_AssemblyReturnFromInventory)))
                || woTrxType.Equals("PM")))
            {
                sql.Append("SELECT * FROM VAMFG_M_WrkOdrTrnsctionLine WHERE IsActive = 'Y' AND iscostcalculated = 'Y' AND IsReversedCostCalculated = 'N' " +
                            " AND VAMFG_M_WrkOdrTransaction_ID = " + VAMFG_M_WrkOdrTransaction_ID);
                if (!String.IsNullOrEmpty(productCategoryID) && String.IsNullOrEmpty(productID))
                {
                    sql.Append(" AND M_Product_ID IN (SELECT M_Product_ID FROM M_Product WHERE M_Product_Category_ID IN (" + productCategoryID + " ) ) ");
                }
                else
                {
                    sql.Append(" AND M_Product_ID IN (" + productID + " )");
                }
                if (M_AttributeSetInstance_ID > 0)
                {
                    sql.Append(" AND NVL(M_AttributeSetInstance_ID, 0) = " + M_AttributeSetInstance_ID);
                }
                sql.Append(" ORDER BY VAMFG_Line");
            }
            else if (woTrxType.Equals(ViennaAdvantage.Model.X_VAMFG_M_WrkOdrTransaction.VAMFG_WORKORDERTXNTYPE_1_ComponentIssueToWorkOrder)
                || woTrxType.Equals(ViennaAdvantage.Model.X_VAMFG_M_WrkOdrTransaction.VAMFG_WORKORDERTXNTYPE_ComponentReturnFromWorkOrder)
                || (countGOM01 <= 0 && (woTrxType.Equals(ViennaAdvantage.Model.X_VAMFG_M_WrkOdrTransaction.VAMFG_WORKORDERTXNTYPE_3_TransferAssemblyToStore)
                || woTrxType.Equals(ViennaAdvantage.Model.X_VAMFG_M_WrkOdrTransaction.VAMFG_WORKORDERTXNTYPE_AssemblyReturnFromInventory)))
                || woTrxType.Equals("PM"))
            {
                sql.Append("SELECT * FROM VAMFG_M_WrkOdrTrnsctionLine WHERE IsActive = 'Y' AND iscostcalculated = 'N' " +
                             " AND VAMFG_M_WrkOdrTransaction_ID = " + VAMFG_M_WrkOdrTransaction_ID);
                sql.Append(@" AND iscostImmediate = 'N' ");
                if (!String.IsNullOrEmpty(productCategoryID) && String.IsNullOrEmpty(productID))
                {
                    sql.Append(" AND M_Product_ID IN (SELECT M_Product_ID FROM M_Product WHERE M_Product_Category_ID IN (" + productCategoryID + " ) ) ");
                }
                else
                {
                    sql.Append(" AND M_Product_ID IN (" + productID + " )");
                }
                if (M_AttributeSetInstance_ID > 0)
                {
                    sql.Append(" AND NVL(M_AttributeSetInstance_ID, 0) = " + M_AttributeSetInstance_ID);
                }
                sql.Append(" ORDER BY VAMFG_Line");
            }
            if (!String.IsNullOrEmpty(sql.ToString()))
                dsChildRecord = DB.ExecuteDataset(sql.ToString(), null, Get_Trx());
            else
                dsChildRecord = null;
            if (dsChildRecord != null && dsChildRecord.Tables.Count > 0 && dsChildRecord.Tables[0].Rows.Count > 0)
            {
                for (int j = 0; j < dsChildRecord.Tables[0].Rows.Count; j++)
                {
                    try
                    {
                        /*Costing Object*/
                        costingCheck = new CostingCheck(GetCtx());
                        costingCheck.dsAccountingSchema = costingCheck.GetAccountingSchema(GetAD_Client_ID());

                        po_WrkOdrTrnsctionLine = tbl_WrkOdrTrnsctionLine.GetPO(GetCtx(), Util.GetValueOfInt(dsChildRecord.Tables[0].Rows[j]["VAMFG_M_WrkOdrTrnsctionLine_ID"]), Get_Trx());
                        costingCheck.M_Transaction_ID = GetTransactionIDForProduction(VAMFG_M_WrkOdrTransaction_ID, Util.GetValueOfInt(dsChildRecord.Tables[0].Rows[j]["VAMFG_M_WrkOdrTrnsctionLine_ID"]));

                        product = new MProduct(GetCtx(), Util.GetValueOfInt(dsChildRecord.Tables[0].Rows[j]["M_Product_ID"]), Get_Trx());
                        costingCheck.product = product;

                        costingMethod = MCostElement.CheckLifoOrFifoMethod(GetCtx(), GetAD_Client_ID(), product.GetM_Product_ID(), Get_Trx());

                        #region get price from m_cost (Current Cost Price)
                        // get price from m_cost (Current Cost Price)
                        if (Util.GetValueOfDecimal(po_WrkOdrTrnsctionLine.Get_Value("CurrentCostPrice")) == 0 &&
                           !(woTrxType.Equals(ViennaAdvantage.Model.X_VAMFG_M_WrkOdrTransaction.VAMFG_WORKORDERTXNTYPE_3_TransferAssemblyToStore)
                            || woTrxType.Equals(ViennaAdvantage.Model.X_VAMFG_M_WrkOdrTransaction.VAMFG_WORKORDERTXNTYPE_AssemblyReturnFromInventory)))
                        {
                            currentCostPrice = 0;
                            currentCostPrice = MCost.GetproductCosts(Util.GetValueOfInt(po_WrkOdrTrnsctionLine.Get_Value("AD_Client_ID")), Util.GetValueOfInt(po_WrkOdrTrnsctionLine.Get_Value("AD_Org_ID")),
                                Util.GetValueOfInt(dsChildRecord.Tables[0].Rows[j]["M_Product_ID"]), Util.GetValueOfInt(dsChildRecord.Tables[0].Rows[j]["M_AttributeSetInstance_ID"]), Get_Trx());
                            if (IsCostUpdation)
                            {
                                po_WrkOdrTrnsctionLine.Set_Value("CurrentCostPrice", currentCostPrice);
                                if (!po_WrkOdrTrnsctionLine.Save(Get_Trx()))
                                {
                                    ValueNamePair pp = VLogger.RetrieveError();
                                    _log.Info("Error found for Production execution Line for this Line ID = " + Util.GetValueOfInt(dsChildRecord.Tables[0].Rows[j]["VAMFG_M_WrkOdrTrnsctionLine_ID"]) +
                                               " Error Name is " + pp.GetName() + " And Error Type is " + pp.GetType());
                                    Get_Trx().Rollback();
                                }
                            }
                        }
                        else if (woTrxType.Equals(ViennaAdvantage.Model.X_VAMFG_M_WrkOdrTransaction.VAMFG_WORKORDERTXNTYPE_3_TransferAssemblyToStore)
                                || woTrxType.Equals(ViennaAdvantage.Model.X_VAMFG_M_WrkOdrTransaction.VAMFG_WORKORDERTXNTYPE_AssemblyReturnFromInventory))
                        {
                            // when product having checkbox "IsBAsedOnRollup" then not to calculate cot of finished Good
                            if (product.IsBasedOnRollup())
                            {
                                continue;
                            }

                            currentCostPrice = GetCostForProductionFinishedGood(Util.GetValueOfInt(po_WrkOdrTransaction.Get_Value("VAMFG_M_WorkOrder_ID")),
                                Util.GetValueOfString(po_WrkOdrTransaction.Get_Value("VAMFG_ProductionExecList")),
                                Util.GetValueOfDecimal(po_WrkOdrTransaction.Get_Value("VAMFG_QtyEntered")), Get_Trx());

                            // if currentCostPrice is ZERO, then not to calculate cost of finished Good
                            if (currentCostPrice == 0)
                            {
                                continue;
                            }

                            if (IsCostUpdation)
                            {
                                // Update cost on Record
                                DB.ExecuteQuery(@"UPDATE VAMFG_M_WrkOdrTransaction SET CurrentCostPrice = " + currentCostPrice + @" 
                                                                        WHERE VAMFG_M_WrkOdrTransaction_ID = " + VAMFG_M_WrkOdrTransaction_ID, null, Get_Trx());
                            }
                        }
                        #endregion

                        if (woTrxType.Equals(ViennaAdvantage.Model.X_VAMFG_M_WrkOdrTransaction.VAMFG_WORKORDERTXNTYPE_1_ComponentIssueToWorkOrder)
                            || woTrxType.Equals(ViennaAdvantage.Model.X_VAMFG_M_WrkOdrTransaction.VAMFG_WORKORDERTXNTYPE_AssemblyReturnFromInventory)
                            || woTrxType.Equals("PM"))
                        {
                            #region 1_Component Issue to Work Order (CI)  / Assembly Return from Inventory(AR)
                            if (!MCostQueue.CreateProductCostsDetails(GetCtx(), Util.GetValueOfInt(po_WrkOdrTrnsctionLine.Get_Value("AD_Client_ID")),
                                Util.GetValueOfInt(po_WrkOdrTrnsctionLine.Get_Value("AD_Org_ID")), product, Util.GetValueOfInt(po_WrkOdrTrnsctionLine.Get_Value("M_AttributeSetInstance_ID")),
                                woTrxType.Equals(ViennaAdvantage.Model.X_VAMFG_M_WrkOdrTransaction.VAMFG_WORKORDERTXNTYPE_AssemblyReturnFromInventory) ? "PE-FinishGood" : "Production Execution", null, null, null, null, po_WrkOdrTrnsctionLine,
                                woTrxType.Equals(ViennaAdvantage.Model.X_VAMFG_M_WrkOdrTransaction.VAMFG_WORKORDERTXNTYPE_AssemblyReturnFromInventory) ? currentCostPrice : 0,
                                countGOM01 > 0 ? Decimal.Negate(Util.GetValueOfDecimal(po_WrkOdrTrnsctionLine.Get_Value("GOM01_ActualQuantity"))) :
                                Decimal.Negate(Util.GetValueOfDecimal(po_WrkOdrTrnsctionLine.Get_Value("VAMFG_QtyEntered"))), Get_Trx(), costingCheck, out conversionNotFoundInOut, optionalstr: "window"))
                            {
                                if (!conversionNotFoundProductionExecution1.Contains(conversionNotFoundProductionExecution))
                                {
                                    conversionNotFoundProductionExecution1 += conversionNotFoundProductionExecution + " , ";
                                }
                                _log.Info("Cost not Calculated for Production Execution for this Line ID = "
                                    + Util.GetValueOfInt(dsChildRecord.Tables[0].Rows[j]["VAMFG_M_WrkOdrTrnsctionLine_ID"]) +
                                            " , " + (!string.IsNullOrEmpty(costingCheck.errorMessage) ? costingCheck.errorMessage : ""));
                            }
                            else
                            {
                                if (Util.GetValueOfString(po_WrkOdrTransaction.Get_Value("VAMFG_Description")) != null && Util.GetValueOfString(po_WrkOdrTransaction.Get_Value("VAMFG_Description")).Contains("{->"))
                                {
                                    //po_WrkOdrTrnsctionLine.Set_Value("IsReversedCostCalculated", true);
                                }
                                if (!string.IsNullOrEmpty(costingMethod) && !(woTrxType.Equals(ViennaAdvantage.Model.X_VAMFG_M_WrkOdrTransaction.VAMFG_WORKORDERTXNTYPE_3_TransferAssemblyToStore)
                                     || woTrxType.Equals(ViennaAdvantage.Model.X_VAMFG_M_WrkOdrTransaction.VAMFG_WORKORDERTXNTYPE_AssemblyReturnFromInventory)))
                                {
                                    currentCostPrice = MCost.GetLifoAndFifoCurrentCostFromCostQueueTransaction(GetCtx(),
                                        Util.GetValueOfInt(po_WrkOdrTrnsctionLine.Get_Value("AD_Client_ID")),
                                        Util.GetValueOfInt(po_WrkOdrTrnsctionLine.Get_Value("AD_Org_ID")),
                                        product.GetM_Product_ID(),
                                        Util.GetValueOfInt(po_WrkOdrTrnsctionLine.Get_Value("M_AttributeSetInstance_ID")),
                                        6, Util.GetValueOfInt(po_WrkOdrTrnsctionLine.Get_Value("VAMFG_M_WrkOdrTrnsctionLine_ID")),
                                        costingMethod, Util.GetValueOfInt(po_WrkOdrTransaction.Get_Value("M_Warehouse_ID")),
                                        true, Get_TrxName());
                                    if (IsCostUpdation)
                                    {
                                        po_WrkOdrTrnsctionLine.Set_Value("CurrentCostPrice", currentCostPrice);
                                    }
                                }

                                //po_WrkOdrTrnsctionLine.Set_Value("IsCostCalculated", true);
                                po_WrkOdrTrnsctionLine.Set_Value("IsCostImmediate", true);
                                if (!po_WrkOdrTrnsctionLine.Save(Get_Trx()))
                                {
                                    ValueNamePair pp = VLogger.RetrieveError();
                                    _log.Info("Error found for Production Execution for this Line ID = " + Util.GetValueOfInt(dsChildRecord.Tables[0].Rows[j]["VAMFG_M_WrkOdrTrnsctionLine_ID"]) +
                                               " Error Name is " + pp.GetName() + " And Error Type is " + pp.GetType());
                                    Get_Trx().Rollback();
                                }
                                else
                                {
                                    _log.Fine("Cost Calculation updated for Production Execution line ID = " + Util.GetValueOfInt(dsChildRecord.Tables[0].Rows[j]["VAMFG_M_WrkOdrTrnsctionLine_ID"]));

                                    query.Clear();
                                    query.Append("Update M_Transaction SET ");
                                    query.Append(" ProductApproxCost = " + currentCostPrice);
                                    query.Append(" , ProductCost = " + currentCostPrice);
                                    query.Append(" , VAS_PostingCost = " + currentCostPrice);
                                    query.Append(" , M_CostElement_ID = " + costingCheck.definedCostingElement);
                                    query.Append(" , CostingLevel = " + GlobalVariable.TO_STRING(costingCheck.costinglevel));
                                    query.Append(" WHERE M_Transaction_ID = " + costingCheck.M_Transaction_ID);
                                    int no = DB.ExecuteQuery(query.ToString(), null, Get_Trx());

                                    Get_Trx().Commit();
                                }
                            }
                            #endregion
                        }
                        else if (woTrxType.Equals(ViennaAdvantage.Model.X_VAMFG_M_WrkOdrTransaction.VAMFG_WORKORDERTXNTYPE_ComponentReturnFromWorkOrder)
                            || woTrxType.Equals(ViennaAdvantage.Model.X_VAMFG_M_WrkOdrTransaction.VAMFG_WORKORDERTXNTYPE_3_TransferAssemblyToStore))
                        {
                            #region  Component Return from Work Order (CR) / Assembly Return to Store(AI)
                            if (!MCostQueue.CreateProductCostsDetails(GetCtx(), Util.GetValueOfInt(po_WrkOdrTrnsctionLine.Get_Value("AD_Client_ID")),
                                Util.GetValueOfInt(po_WrkOdrTrnsctionLine.Get_Value("AD_Org_ID")), product, Util.GetValueOfInt(po_WrkOdrTrnsctionLine.Get_Value("M_AttributeSetInstance_ID")),
                                woTrxType.Equals(ViennaAdvantage.Model.X_VAMFG_M_WrkOdrTransaction.VAMFG_WORKORDERTXNTYPE_3_TransferAssemblyToStore) ? "PE-FinishGood" : "Production Execution", null, null, null, null, po_WrkOdrTrnsctionLine,
                                woTrxType.Equals(ViennaAdvantage.Model.X_VAMFG_M_WrkOdrTransaction.VAMFG_WORKORDERTXNTYPE_3_TransferAssemblyToStore) ? currentCostPrice : 0,
                                countGOM01 > 0 ? Util.GetValueOfDecimal(po_WrkOdrTrnsctionLine.Get_Value("GOM01_ActualQuantity")) :
                                Util.GetValueOfDecimal(po_WrkOdrTrnsctionLine.Get_Value("VAMFG_QtyEntered")), Get_Trx(), costingCheck, out conversionNotFoundInOut, optionalstr: "window"))
                            {
                                if (!conversionNotFoundProductionExecution1.Contains(conversionNotFoundProductionExecution))
                                {
                                    conversionNotFoundProductionExecution1 += conversionNotFoundProductionExecution + " , ";
                                }
                                _log.Info("Cost not Calculated for Production Execution for this Line ID = "
                                    + Util.GetValueOfInt(dsChildRecord.Tables[0].Rows[j]["VAMFG_M_WrkOdrTrnsctionLine_ID"]) +
                                            " , " + (!string.IsNullOrEmpty(costingCheck.errorMessage) ? costingCheck.errorMessage : ""));
                            }
                            else
                            {
                                if (Util.GetValueOfString(po_WrkOdrTransaction.Get_Value("VAMFG_Description")) != null && Util.GetValueOfString(po_WrkOdrTransaction.Get_Value("VAMFG_Description")).Contains("{->"))
                                {
                                    //po_WrkOdrTrnsctionLine.Set_Value("IsReversedCostCalculated", true);
                                }
                                //po_WrkOdrTrnsctionLine.Set_Value("IsCostCalculated", true);
                                po_WrkOdrTrnsctionLine.Set_Value("IsCostImmediate", true);
                                if (!po_WrkOdrTrnsctionLine.Save(Get_Trx()))
                                {
                                    ValueNamePair pp = VLogger.RetrieveError();
                                    _log.Info("Error found for Production Execution for this Line ID = " + Util.GetValueOfInt(dsChildRecord.Tables[0].Rows[j]["VAMFG_M_WrkOdrTrnsctionLine_ID"]) +
                                               " Error Name is " + pp.GetName() + " And Error Type is " + pp.GetType());
                                    Get_Trx().Rollback();
                                }
                                else
                                {
                                    _log.Fine("Cost Calculation updated for Production Execution line ID = " + Util.GetValueOfInt(dsChildRecord.Tables[0].Rows[j]["VAMFG_M_WrkOdrTrnsctionLine_ID"]));

                                    query.Clear();
                                    query.Append("Update M_Transaction SET ");
                                    query.Append(" ProductApproxCost = " + currentCostPrice);
                                    query.Append(" , ProductCost = " + currentCostPrice);
                                    query.Append(" , VAS_PostingCost = " + currentCostPrice);
                                    query.Append(" , M_CostElement_ID = " + costingCheck.definedCostingElement);
                                    query.Append(" , CostingLevel = " + GlobalVariable.TO_STRING(costingCheck.costinglevel));
                                    query.Append(" WHERE M_Transaction_ID = " + costingCheck.M_Transaction_ID);
                                    int no = DB.ExecuteQuery(query.ToString(), null, Get_Trx());

                                    Get_Trx().Commit();
                                }
                            }
                            #endregion
                        }
                    }
                    catch { }
                }
            }

            #region Calcuale Cost of Finished Good - (Process Manufacturing) -- gulfoil specific
            if (countGOM01 > 0 && IsSoTrx.Equals("Y") &&
                ((String)po_WrkOdrTransaction.Get_Value("VAMFG_WorkOrderTxnType") == "CI"
                || (String)po_WrkOdrTransaction.Get_Value("VAMFG_WorkOrderTxnType") == "CR"))
            {

                #region get price from m_cost (Current Cost Price) - update cost on header
                if (Util.GetValueOfDecimal(po_WrkOdrTransaction.Get_Value("CurrentCostPrice")) == 0)
                {
                    currentCostPrice = 0;
                    currentCostPrice = MCost.GetproductCosts(
                        Util.GetValueOfInt(po_WrkOdrTransaction.Get_Value("AD_Client_ID")),
                        Util.GetValueOfInt(po_WrkOdrTransaction.Get_Value("AD_Org_ID")),
                        Util.GetValueOfInt(po_WrkOdrTransaction.Get_Value("M_Product_ID")),
                        Util.GetValueOfInt(po_WrkOdrTransaction.Get_Value("M_AttributeSetInstance_ID")), Get_Trx());
                    DB.ExecuteQuery("UPDATE VAMFG_M_WrkOdrTransaction SET CurrentCostPrice = " + currentCostPrice +
                                @" WHERE VAMFG_M_WrkOdrTransaction_ID = " + VAMFG_M_WrkOdrTransaction_ID, null, Get_Trx());
                    Get_Trx().Commit();
                }
                #endregion

                #region calling for calculate cost of finished good.
                string className = "ViennaAdvantage.CMFG.Model.MVAMFGMWrkOdrTransaction";
                asm = System.Reflection.Assembly.Load("VAMFGSvc");
                type = asm.GetType(className);
                if (type != null)
                {
                    methodInfo = type.GetMethod("CalculateFinishedGoodCost");
                    if (methodInfo != null)
                    {
                        object result = "";

                        object[] parametersArrayConstructor = new object[] { GetCtx(),
                                                                VAMFG_M_WrkOdrTransaction_ID,
                                                                Get_Trx() };
                        object classInstance = Activator.CreateInstance(type, parametersArrayConstructor);

                        ParameterInfo[] parameters = methodInfo.GetParameters();
                        if (parameters.Length == 9)
                        {
                            object[] parametersArray = new object[] { GetCtx(),
                                                                Util.GetValueOfInt(po_WrkOdrTransaction.Get_Value("AD_Client_ID")),
                                                                Util.GetValueOfInt(po_WrkOdrTransaction.Get_Value("AD_Org_ID")),
                                                                Util.GetValueOfInt(po_WrkOdrTransaction.Get_Value("M_Product_ID")),
                                                                Util.GetValueOfInt(po_WrkOdrTransaction.Get_Value("M_AttributeSetInstance_ID")),
                                                                Util.GetValueOfInt(po_WrkOdrTransaction.Get_Value("VAMFG_M_WorkOrder_ID")),
                                                                Util.GetValueOfString(po_WrkOdrTransaction.Get_Value("GOM01_BatchNo")),
                                                                Util.GetValueOfDecimal(po_WrkOdrTransaction.Get_Value("GOM01_ActualLiter")),
                                                                Get_Trx() };
                            result = methodInfo.Invoke(classInstance, parametersArray);
                            if (!(bool)result)
                            {
                                _log.Info("Cost not Calculated for Production Execution for Finished Good = " + VAMFG_M_WrkOdrTransaction_ID);
                                Get_Trx().Rollback();
                            }
                            else
                            {
                                Get_Trx().Commit();
                            }
                        }
                    }
                }
                #endregion

                #region get price from m_cost (Current Cost Price) - update cost on header
                if (Util.GetValueOfDecimal(po_WrkOdrTransaction.Get_Value("CurrentCostPrice")) == 0)
                {
                    currentCostPrice = 0;
                    currentCostPrice = MCost.GetproductCosts(
                        Util.GetValueOfInt(po_WrkOdrTransaction.Get_Value("AD_Client_ID")),
                        Util.GetValueOfInt(po_WrkOdrTransaction.Get_Value("AD_Org_ID")),
                        Util.GetValueOfInt(po_WrkOdrTransaction.Get_Value("M_Product_ID")),
                        Util.GetValueOfInt(po_WrkOdrTransaction.Get_Value("M_AttributeSetInstance_ID")), Get_Trx());
                    DB.ExecuteQuery("UPDATE VAMFG_M_WrkOdrTransaction SET CurrentCostPrice = " + currentCostPrice +
                                @" WHERE VAMFG_M_WrkOdrTransaction_ID = " + VAMFG_M_WrkOdrTransaction_ID, null, Get_Trx());
                    Get_Trx().Commit();
                }
                #endregion
            }
            else if (countGOM01 > 0 && IsSoTrx.Equals("Y") &&
               ((String)po_WrkOdrTransaction.Get_Value("VAMFG_WorkOrderTxnType") == "AR"
               || (String)po_WrkOdrTransaction.Get_Value("VAMFG_WorkOrderTxnType") == "AI"))
            {
                #region 3_TransferAssemblyToStore | AssemblyReturnFromInventory
                // update Current cost price for Transfer Assembly to store as well as for Assembly Return form Inventory
                // calculation process is : 
                // get Actual qty in KG from Component Transaction Line
                // get cost from Component line where transaction type is "CI" (Component Issue to Work Order)
                // then divide the calculated value with Actual Qty in Kg fromProduction Execution Header
                // after that  we multiple density with  (sum of (qty * cost of each line) / Actual Qty in Kg from Production Execution Header)
                var sql1 = @"SELECT ROUND( wot.GOM01_ActualDensity * (SUM(wotl.GOM01_ActualQuantity * wotl.CurrentCostPrice) / wot.GOM01_ActualQuantity) , 10) as Currenctcost
                                                         FROM VAMFG_M_WrkOdrTransaction wot
                                                         INNER JOIN VAMFG_M_WorkOrder wo ON wo.VAMFG_M_WorkOrder_ID = wot.VAMFG_M_WorkOrder_ID
                                                         INNER JOIN VAMFG_M_WrkOdrTrnsctionLine wotl ON wot.VAMFG_M_WrkOdrTransaction_ID = wotl.VAMFG_M_WrkOdrTransaction_ID
                                                         WHERE wotl.IsActive = 'Y' AND wot.VAMFG_M_WorkOrder_ID = " + (int)po_WrkOdrTransaction.Get_Value("VAMFG_M_WorkOrder_ID") +
                             @" AND wot.VAMFG_WorkOrderTxnType = 'CI' " +
                              " AND wot.GOM01_BatchNo = '" + Util.GetValueOfString(po_WrkOdrTransaction.Get_Value("GOM01_BatchNo")) + @"' GROUP BY wot.GOM01_ActualQuantity ,  wot.GOM01_ActualDensity";
                decimal currentcostprice = VAdvantage.Utility.Util.GetValueOfDecimal(DB.ExecuteScalar(sql1, null, Get_TrxName()));
                currentcostprice = Decimal.Round(currentcostprice, 10);
                DB.ExecuteQuery("UPDATE VAMFG_M_WrkOdrTransaction SET CurrentCostPrice = " + currentcostprice +
                                 @" WHERE VAMFG_M_WrkOdrTransaction_ID = " + VAMFG_M_WrkOdrTransaction_ID, null, Get_Trx());
                Get_Trx().Commit();
                #endregion
            }
            #endregion

            //sql.Clear();
            //if (Util.GetValueOfString(po_WrkOdrTransaction.Get_Value("VAMFG_Description")) != null &&
            //    Util.GetValueOfString(po_WrkOdrTransaction.Get_Value("VAMFG_Description")).Contains("{->"))
            //{
            //    sql.Append(@"SELECT COUNT(*) FROM VAMFG_M_WrkOdrTrnsctionLine WHERE IsReversedCostCalculated = 'N'
            //                                         AND IsActive = 'Y' AND VAMFG_M_WrkOdrTransaction_ID = " + VAMFG_M_WrkOdrTransaction_ID);
            //}
            //else
            //{
            //    sql.Append(@"SELECT COUNT(*) FROM VAMFG_M_WrkOdrTrnsctionLine WHERE IsCostCalculated = 'N' AND IsActive = 'Y'
            //                               AND VAMFG_M_WrkOdrTransaction_ID = " + VAMFG_M_WrkOdrTransaction_ID);
            //}
            //if (Util.GetValueOfInt(DB.ExecuteScalar(sql.ToString(), null, Get_Trx())) <= 0)
            //{
            //    if (Util.GetValueOfString(po_WrkOdrTransaction.Get_Value("VAMFG_Description")) != null &&
            //        Util.GetValueOfString(po_WrkOdrTransaction.Get_Value("VAMFG_Description")).Contains("{->"))
            //    {
            //        po_WrkOdrTransaction.Set_Value("IsReversedCostCalculated", true);
            //    }
            //    po_WrkOdrTransaction.Set_Value("IsCostCalculated", true);
            //    if (!po_WrkOdrTransaction.Save(Get_Trx()))
            //    {
            //        ValueNamePair pp = VLogger.RetrieveError();
            //        _log.Info("Error found for saving Production execution for this Record ID = " + VAMFG_M_WrkOdrTransaction_ID +
            //                   " Error Name is " + pp.GetName() + " And Error Type is " + pp.GetType());
            //    }
            //    else
            //    {
            //        _log.Fine("Cost Calculation updated for Production Execution = " + VAMFG_M_WrkOdrTransaction_ID);
            //        Get_Trx().Commit();
            //    }
            //}
        }

        /// <summary>
        /// This function is used to get the Product transaction id against production execution
        /// </summary>
        /// <param name="VAMFG_M_WrkOdrTransaction_ID">Production Execution ID</param>
        /// <param name="VAMFG_M_WrkOdrTrnsctionLine_ID">Execution Line ID</param>
        /// <Author>VIS_0045: 11 Feb,2025</Author>
        /// <returns>M_TransactionID</returns>
        private int GetTransactionIDForProduction(int VAMFG_M_WrkOdrTransaction_ID, int VAMFG_M_WrkOdrTrnsctionLine_ID)
        {
            int M_TransactionID = 0;
            sql.Clear();
            sql.Append($@"SELECT M_Transaction_ID FROM M_Transaction WHERE VAMFG_M_WrkOdrTransaction_ID = {VAMFG_M_WrkOdrTransaction_ID}");
            if (VAMFG_M_WrkOdrTrnsctionLine_ID > 0)
            {
                sql.Append($@" AND VAMFG_M_WrkOdrTrnsctionLine_ID = {VAMFG_M_WrkOdrTrnsctionLine_ID}");
            }
            M_TransactionID = Util.GetValueOfInt(DB.ExecuteScalar(sql.ToString(), null, Get_Trx()));

            if (M_TransactionID == 0 && VAMFG_M_WrkOdrTrnsctionLine_ID == 0)
            {
                sql.Clear();
                sql.Append($@"SELECT M_Transaction_ID FROM M_Transaction WHERE VAMFG_M_WrkOdrTransaction_ID = {VAMFG_M_WrkOdrTransaction_ID}");
                sql.Append($@" AND VAMFG_M_WrkOdrTrnsctionLine_ID = 0 ");
                M_TransactionID = Util.GetValueOfInt(DB.ExecuteScalar(sql.ToString(), null, Get_Trx()));
            }
            return M_TransactionID;
        }

        /// <summary>
        /// Get - sum of all component whose available on "Component Issue To Work Order" transaction
        /// </summary>
        /// <param name="VAMFG_M_WorkOrder_ID">production Order</param>
        /// <param name="trxName">transaction</param>
        /// <returns>cost of finished good</returns>
        private Decimal GetCostForProductionFinishedGood(int VAMFG_M_WorkOrder_ID, String VAMFG_M_WrkOdrTransaction_Ids, Decimal AssembledQty, Trx trxName)
        {
            decimal currentcostprice = 0;

            // check any record havoing Zero cost, then return with ZERO Value
            String sql = @"SELECT COUNT(VAMFG_M_WrkOdrTrnsctionLine_ID) as NotFoundCurrentCost
                             FROM VAMFG_M_WrkOdrTransaction wot
                             INNER JOIN VAMFG_M_WorkOrder wo ON wo.VAMFG_M_WorkOrder_ID = wot.VAMFG_M_WorkOrder_ID
                             INNER JOIN VAMFG_M_WrkOdrTrnsctionLine wotl ON wot.VAMFG_M_WrkOdrTransaction_ID = wotl.VAMFG_M_WrkOdrTransaction_ID
                           WHERE wotl.IsActive = 'Y' AND wot.VAMFG_M_WorkOrder_ID = " + VAMFG_M_WorkOrder_ID +
                             @" AND wot.VAMFG_WorkOrderTxnType IN ( 'CI', 'PM') AND NVL(wotl.currentcostprice , 0) = 0 AND wot.DocStatus IN ('CO'  , 'CL')  ";
            if (!String.IsNullOrEmpty(VAMFG_M_WrkOdrTransaction_Ids))
            {
                sql += " AND wot.VAMFG_M_WrkOdrTransaction_ID IN (" + VAMFG_M_WrkOdrTransaction_Ids + ")";
            }
            //sql += " GROUP BY wot.VAMFG_QtyEntered";
            if (VAdvantage.Utility.Util.GetValueOfDecimal(DB.ExecuteScalar(sql, null, trxName)) == 0)
            {
                // sum of all component whose available on "Component Issue To Work Order" transaction
                sql = @"SELECT ROUND((SUM(CASE WHEN  Wot.Vamfg_Workordertxntype IN ( 'CI', 'PM') THEN
                                wotl.VAMFG_QtyEntered else -1 * wotl.VAMFG_QtyEntered END * wotl.CurrentCostPrice) / " + AssembledQty + @") , 10) as Currenctcost
                             FROM VAMFG_M_WrkOdrTransaction wot
                             INNER JOIN VAMFG_M_WorkOrder wo ON wo.VAMFG_M_WorkOrder_ID = wot.VAMFG_M_WorkOrder_ID
                             INNER JOIN VAMFG_M_WrkOdrTrnsctionLine wotl ON wot.VAMFG_M_WrkOdrTransaction_ID = wotl.VAMFG_M_WrkOdrTransaction_ID
                           WHERE wotl.IsActive = 'Y' AND wot.VAMFG_M_WorkOrder_ID = " + VAMFG_M_WorkOrder_ID +
                                 @" AND wot.VAMFG_WorkOrderTxnType IN( 'CI', 'PM', 'CR') AND wot.DocStatus IN ('CO'  , 'CL') ";
                if (!String.IsNullOrEmpty(VAMFG_M_WrkOdrTransaction_Ids))
                {
                    sql += " AND wot.VAMFG_M_WrkOdrTransaction_ID IN (" + VAMFG_M_WrkOdrTransaction_Ids + ")";
                }
                //sql += " GROUP BY wot.VAMFG_QtyEntered ";
                currentcostprice = VAdvantage.Utility.Util.GetValueOfDecimal(DB.ExecuteScalar(sql, null, trxName));
            }
            return currentcostprice;
        }

        /// <summary>
        /// Cost Calculation Against AP Credit Memo - During Return Cycle of Purchase 
        /// </summary>
        /// <param name="M_MatchInv_ID">Match Invoice reference</param>
        private void CalculationCostCreditMemo(int M_MatchInv_ID)
        {
            /*Costing Object*/
            costingCheck = new CostingCheck(GetCtx());
            costingCheck.dsAccountingSchema = costingCheck.GetAccountingSchema(GetAD_Client_ID());

            matchInvoice = new MMatchInv(GetCtx(), M_MatchInv_ID, Get_Trx());
            inoutLine = new MInOutLine(GetCtx(), matchInvoice.GetM_InOutLine_ID(), Get_Trx());
            invoiceLine = new MInvoiceLine(GetCtx(), matchInvoice.GetC_InvoiceLine_ID(), Get_Trx());
            invoice = new MInvoice(GetCtx(), invoiceLine.GetC_Invoice_ID(), Get_Trx());
            product = new MProduct(GetCtx(), invoiceLine.GetM_Product_ID(), Get_Trx());
            bool isUpdatePostCurrentcostPriceFromMR = MCostElement.IsPOCostingmethod(GetCtx(), GetAD_Client_ID(), product.GetM_Product_ID(), Get_Trx());
            if (invoiceLine != null && invoiceLine.Get_ID() > 0)
            {
                ProductInvoiceLineCost = invoiceLine.GetProductLineCost(invoiceLine);
            }
            if (inoutLine.GetC_OrderLine_ID() > 0)
            {
                orderLine = new MOrderLine(GetCtx(), inoutLine.GetC_OrderLine_ID(), Get_Trx());
                order = new MOrder(GetCtx(), orderLine.GetC_Order_ID(), Get_Trx());

                costingCheck.orderline = orderLine;
                costingCheck.order = order;
            }
            if (product.GetProductType() == "I" && product.GetM_Product_ID() > 0)
            {
                if (countColumnExist > 0)
                {
                    isCostAdjustableOnLost = product.IsCostAdjustmentOnLost();
                }
                costingCheck.AD_Org_ID = matchInvoice.GetAD_Org_ID();
                costingCheck.M_Warehouse_ID = inoutLine.GetM_Warehouse_ID();
                costingCheck.M_ASI_ID = inoutLine.GetM_AttributeSetInstance_ID();
                costingCheck.inoutline = inoutLine;
                costingCheck.inout = inoutLine.GetParent();
                costingCheck.invoiceline = invoiceLine;
                costingCheck.invoice = invoice;
                costingCheck.product = product;
                costingCheck.IsPOCostingethodBindedonProduct = isUpdatePostCurrentcostPriceFromMR;

                //if (inoutLine.IsCostCalculated())
                if (inoutLine.IsCostImmediate())
                {
                    // when isCostAdjustableOnLost = true on product and movement qty on MR is less than invoice qty then consider MR qty else invoice qty
                    if (!MCostQueue.CreateProductCostsDetails(GetCtx(), invoiceLine.GetAD_Client_ID(), invoiceLine.GetAD_Org_ID(), product, invoiceLine.GetM_AttributeSetInstance_ID(),
                          "Invoice(Vendor)-Return", null, inoutLine, null, invoiceLine, null,
                        isCostAdjustableOnLost && matchInvoice.GetQty() < invoiceLine.GetQtyInvoiced() ? Decimal.Negate(ProductInvoiceLineCost) : Decimal.Negate(Decimal.Multiply(Decimal.Divide(ProductInvoiceLineCost, invoiceLine.GetQtyInvoiced()), matchInvoice.GetQty())),
                         Decimal.Negate(matchInvoice.GetQty()), Get_Trx(), costingCheck, out conversionNotFoundInvoice, optionalstr: "window"))
                    {
                        if (!conversionNotFoundInvoice1.Contains(conversionNotFoundInvoice))
                        {
                            conversionNotFoundInvoice1 += conversionNotFoundInvoice + " , ";
                        }
                        _log.Info("Cost not Calculated for Invoice(Vendor) for this Line ID = " + invoiceLine.GetC_InvoiceLine_ID() +
                                            " , " + (!string.IsNullOrEmpty(costingCheck.errorMessage) ? costingCheck.errorMessage : ""));
                        ListReCalculatedRecords.Add(new ReCalculateRecord { WindowName = (int)windowName.CreditMemo, HeaderId = M_MatchInv_ID, LineId = invoiceLine.GetC_InvoiceLine_ID(), IsReversal = false });
                    }
                    else
                    {
                        if (invoice.GetDescription() != null && invoice.GetDescription().Contains("{->"))
                        {
                            //invoiceLine.SetIsReversedCostCalculated(true);
                        }
                        //invoiceLine.SetIsCostCalculated(true);
                        //if (client.IsCostImmediate() && !invoiceLine.IsCostImmediate())
                        {
                            invoiceLine.SetIsCostImmediate(true);
                        }
                        if (!invoiceLine.Save(Get_Trx()))
                        {
                            ValueNamePair pp = VLogger.RetrieveError();
                            _log.Info("Error found for saving Invoice(Vendor) for this Line ID = " + invoiceLine.GetC_InvoiceLine_ID() +
                                       " Error Name is " + pp.GetName() + " And Error Type is " + pp.GetType());
                            Get_Trx().Rollback();
                        }
                        else
                        {
                            _log.Fine("Cost Calculation updated for m_invoiceline = " + invoiceLine.GetC_InvoiceLine_ID());
                            Get_Trx().Commit();

                            // set is cost calculation true on match invoice
                            //matchInvoice.SetIsCostCalculated(true);
                            matchInvoice.SetIsCostImmediate(true);
                            if (matchInvoice.Get_ColumnIndex("PostCurrentCostPrice") >= 0)
                            {
                                // get cost from Product Cost after cost calculation
                                currentCostPrice = MCost.GetproductCosts(GetAD_Client_ID(), GetAD_Org_ID(),
                                                                         product.GetM_Product_ID(), invoiceLine.GetM_AttributeSetInstance_ID(), Get_Trx(), inoutLine.GetM_Warehouse_ID());
                                if (IsCostUpdation)
                                {
                                    matchInvoice.SetPostCurrentCostPrice(currentCostPrice);
                                }
                            }
                            if (!matchInvoice.Save(Get_Trx()))
                            {
                                ValueNamePair pp = VLogger.RetrieveError();
                                _log.Info("Error found for saving Invoice(Vendor) for this Line ID = " + matchInvoice.GetC_InvoiceLine_ID() +
                                           " Error Name is " + pp.GetName() + " And Error Type is " + pp.GetType());
                                Get_Trx().Rollback();
                            }
                            else
                            {
                                Get_Trx().Commit();
                                // update the Post current price after Invoice receving on inoutline
                                if (!isUpdatePostCurrentcostPriceFromMR)
                                {
                                    if (IsCostUpdation)
                                    {
                                        DB.ExecuteQuery(@"UPDATE M_InoutLine SET PostCurrentCostPrice =   " + currentCostPrice +
                                                    @" WHERE M_InoutLine_ID = " + matchInvoice.GetM_InOutLine_ID(), null, Get_Trx());

                                        DB.ExecuteQuery($"Update M_Transaction SET ProductCost = " + matchInvoice.GetPostCurrentCostPrice() +
                                           $@" WHERE M_Transaction_ID IN (SELECT M_Transaction_ID FROM M_InoutLineMA 
                                                WHERE M_InOutLine_ID = {inoutLine.GetM_InOutLine_ID()})", null, Get_Trx());
                                    }
                                }

                                // Update Product Cost on Product Transaction for the Invoice Line
                                UpdateTransactionCostForInvoice(matchInvoice.GetPostCurrentCostPrice(), matchInvoice.GetC_InvoiceLine_ID(), costingCheck);
                                Get_Trx().Commit();
                            }
                        }
                    }
                }
            }
        }

        /// <summary>
        ///  Cost Calculation Against AP Credit Memo - During Return Cycle of Purchase - Reverse 
        /// </summary>
        /// <param name="M_MatchInvCostTrack_ID">etamporary table record reference -- M_MatchInvCostTrack_ID </param>
        private void CalculateCostCreditMemoreversal(int M_MatchInvCostTrack_ID)
        {
            /*Costing Object*/
            costingCheck = new CostingCheck(GetCtx());
            costingCheck.dsAccountingSchema = costingCheck.GetAccountingSchema(GetAD_Client_ID());

            matchInvCostReverse = new X_M_MatchInvCostTrack(GetCtx(), M_MatchInvCostTrack_ID, Get_Trx());
            inoutLine = new MInOutLine(GetCtx(), matchInvCostReverse.GetM_InOutLine_ID(), Get_Trx());
            invoiceLine = new MInvoiceLine(GetCtx(), matchInvCostReverse.GetRev_C_InvoiceLine_ID(), Get_Trx());
            invoice = new MInvoice(GetCtx(), invoiceLine.GetC_Invoice_ID(), Get_Trx());
            if (invoiceLine != null && invoiceLine.Get_ID() > 0)
            {
                ProductInvoiceLineCost = invoiceLine.GetProductLineCost(invoiceLine);
            }
            product = new MProduct(GetCtx(), invoiceLine.GetM_Product_ID(), Get_Trx());
            if (inoutLine.GetC_OrderLine_ID() > 0)
            {
                orderLine = new MOrderLine(GetCtx(), inoutLine.GetC_OrderLine_ID(), Get_Trx());
                order = new MOrder(GetCtx(), orderLine.GetC_Order_ID(), Get_Trx());

                costingCheck.orderline = orderLine;
                costingCheck.order = order;
            }
            if (product.GetProductType() == "I" && product.GetM_Product_ID() > 0)
            {
                if (countColumnExist > 0)
                {
                    isCostAdjustableOnLost = product.IsCostAdjustmentOnLost();
                }

                costingCheck.AD_Org_ID = matchInvoice.GetAD_Org_ID();
                costingCheck.M_Warehouse_ID = inoutLine.GetM_Warehouse_ID();
                costingCheck.M_ASI_ID = inoutLine.GetM_AttributeSetInstance_ID();
                costingCheck.inoutline = inoutLine;
                costingCheck.inout = inoutLine.GetParent();
                costingCheck.invoiceline = invoiceLine;
                costingCheck.invoice = invoice;
                costingCheck.product = product;

                // when isCostAdjustableOnLost = true on product and movement qty on MR is less than invoice qty then consider MR qty else invoice qty
                if (!MCostQueue.CreateProductCostsDetails(GetCtx(), invoiceLine.GetAD_Client_ID(), invoiceLine.GetAD_Org_ID(), product, invoiceLine.GetM_AttributeSetInstance_ID(),
                      "Invoice(Vendor)-Return", null, inoutLine, null, invoiceLine, null,
                    isCostAdjustableOnLost && matchInvCostReverse.GetQty() < Decimal.Negate(invoiceLine.GetQtyInvoiced()) ? Decimal.Negate(ProductInvoiceLineCost) : (Decimal.Multiply(Decimal.Divide(ProductInvoiceLineCost, invoiceLine.GetQtyInvoiced()), matchInvCostReverse.GetQty())),
                     matchInvCostReverse.GetQty(),
                      Get_Trx(), costingCheck, out conversionNotFoundInvoice, optionalstr: "window"))
                {
                    if (!conversionNotFoundInvoice1.Contains(conversionNotFoundInvoice))
                    {
                        conversionNotFoundInvoice1 += conversionNotFoundInvoice + " , ";
                    }
                    _log.Info("Cost not Calculated for Invoice(Vendor) for this Line ID = " + invoiceLine.GetC_InvoiceLine_ID() +
                                            " , " + (!string.IsNullOrEmpty(costingCheck.errorMessage) ? costingCheck.errorMessage : ""));
                    ListReCalculatedRecords.Add(new ReCalculateRecord { WindowName = (int)windowName.CreditMemo, HeaderId = M_MatchInvCostTrack_ID, LineId = invoiceLine.GetC_InvoiceLine_ID(), IsReversal = true });
                }
                else
                {
                    //invoiceLine.SetIsReversedCostCalculated(true);
                    //invoiceLine.SetIsCostCalculated(true);
                    //if (client.IsCostImmediate() && !invoiceLine.IsCostImmediate())
                    {
                        invoiceLine.SetIsCostImmediate(true);
                    }
                    if (!invoiceLine.Save(Get_Trx()))
                    {
                        ValueNamePair pp = VLogger.RetrieveError();
                        _log.Info("Error found for saving Invoice(Vendor) for this Line ID = " + invoiceLine.GetC_InvoiceLine_ID() +
                                   " Error Name is " + pp.GetName() + " And Error Type is " + pp.GetType());
                        Get_Trx().Rollback();
                    }
                    else
                    {
                        _log.Fine("Cost Calculation updated for m_invoiceline = " + invoiceLine.GetC_InvoiceLine_ID());
                        Get_Trx().Commit();

                        // set is cost calculation true on match invoice
                        if (!matchInvCostReverse.Delete(true, Get_Trx()))
                        {
                            ValueNamePair pp = VLogger.RetrieveError();
                            _log.Info(" Delete Record M_MatchInvCostTrack -- Error Name is " + pp.GetName() + " And Error Type is " + pp.GetType());
                            Get_Trx().Rollback();
                        }
                        else
                        {
                            Get_Trx().Commit();
                        }

                        // get cost from Product Cost after cost calculation, and update on Product Transaction against Invoice
                        currentCostPrice = MCost.GetproductCosts(GetAD_Client_ID(), GetAD_Org_ID(),
                                                                 product.GetM_Product_ID(), invoiceLine.GetM_AttributeSetInstance_ID(), Get_Trx(), inout.GetM_Warehouse_ID());
                        UpdateTransactionCostForInvoice(currentCostPrice, invoiceLine.GetC_InvoiceLine_ID(), costingCheck);
                        Get_Trx().Commit();

                    }
                }
            }
        }

        /// <summary>
        /// Is used to calculate cost those record which are not calculate in first iteration due to qty unavialablity or other reason
        /// </summary>
        /// <param name="list">list of class -- ReCalculateRecord </param>
        private void ReVerfyAndCalculateCost(List<ReCalculateRecord> list)
        {
            if (list != null)
            {
                ReCalculateRecord objReCalculateRecord = null;
                int loopCount = list.Count;
                for (int i = 0; i < loopCount; i++)
                {
                    objReCalculateRecord = list[i];
                    if (objReCalculateRecord.WindowName == (int)windowName.Shipment)
                    {
                        CalculateCostForShipment(objReCalculateRecord.HeaderId);
                    }
                    else if (objReCalculateRecord.WindowName == (int)windowName.ReturnVendor)
                    {
                        CalculateCostForReturnToVendor(objReCalculateRecord.HeaderId);
                    }
                    else if (objReCalculateRecord.WindowName == (int)windowName.Movement)
                    {
                        CalculateCostForMovement(objReCalculateRecord.HeaderId);
                    }
                    else if (objReCalculateRecord.WindowName == (int)windowName.MaterialReceipt)
                    {
                        if (objReCalculateRecord.IsReversal)
                        {
                            CalculateCostForMaterialReversal(objReCalculateRecord.HeaderId);
                        }
                        else
                        {
                            CalculateCostForMaterial(objReCalculateRecord.HeaderId);
                        }
                    }
                    else if (objReCalculateRecord.WindowName == (int)windowName.MatchInvoice)
                    {
                        if (objReCalculateRecord.IsReversal)
                        {
                            CalculateCostForMatchInvoiceReversal(objReCalculateRecord.HeaderId);
                        }
                        else
                        {
                            CalculateCostForMatchInvoiced(objReCalculateRecord.HeaderId);
                        }
                    }
                    else if (objReCalculateRecord.WindowName == (int)windowName.Inventory)
                    {
                        CalculateCostForInventory(objReCalculateRecord.HeaderId);
                    }
                    else if (objReCalculateRecord.WindowName == (int)windowName.CustomerReturn)
                    {
                        CalculateCostForCustomerReturn(objReCalculateRecord.HeaderId);
                    }
                    else if (objReCalculateRecord.WindowName == (int)windowName.CreditMemo)
                    {
                        if (objReCalculateRecord.IsReversal)
                        {
                            CalculateCostCreditMemoreversal(objReCalculateRecord.HeaderId);
                        }
                        else
                        {
                            CalculationCostCreditMemo(objReCalculateRecord.HeaderId);
                        }
                    }
                    else if (objReCalculateRecord.WindowName == (int)windowName.AssetDisposal)
                    {
                        po_AssetDisposal = tbl_AssetDisposal.GetPO(GetCtx(), objReCalculateRecord.HeaderId, Get_Trx());
                        CalculateCostForAssetDisposal(objReCalculateRecord.HeaderId, Util.GetValueOfInt(Util.GetValueOfInt(po_AssetDisposal.Get_Value("M_Product_ID"))));
                    }
                }
            }
        }

    }

    public class ReCalculateRecord
    {
        public int WindowName { get; set; }
        public bool IsReversal { get; set; }
        public int HeaderId { get; set; }
        public int LineId { get; set; }
    }
}
