/********************************************************
    * Project Name   : 
    * Class Name     : CreateCostForCombination
    * Purpose        : Calculate Cost of Multiple costing element
    * Class Used     : ProcessEngine.SvrProcess
    * Chronological    Development
    * Amit Bansal     08-April-2016
******************************************************/

using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using VAdvantage.ProcessEngine;
using System.Data;
using VAdvantage.DataBase;
using VAdvantage.Utility;
using VAdvantage.Logging;
using VAdvantage.Model;

namespace ViennaAdvantageServer.Process
{
    public class CreateCostForCombination : SvrProcess
    {
        private int costElement_ID = 0;
        private String sql = null;
        private DataSet dsCostCombination = null;
        private DataSet dsProductCost = null;
        MCost costcombination = null;
        private int _m_Product_ID = 0;
        private int _ad_Org_ID = 0;
        private int _m_Attributesetinstance_ID = 0;
        private int _m_Warehouse_ID = 0;
        private int _c_AcctSchema_ID = 0;
        List<int> costElement = new List<int>();
        MCostElement ce = null;
        string MaterialCostingMethod = "";
        int MaterialCostingElement_ID = 0;

        protected override void Prepare()
        {
            costElement_ID = GetRecord_ID();
        }

        protected override string DoIt()
        {
            try
            {
                // Get Combination Record
                sql = $@"SELECT ce.M_CostElement_ID ,  ce.Name ,  cel.lineno ,  cel.m_ref_costelement, refEle.costingmethod 
                            FROM M_CostElement ce 
                            INNER JOIN m_costelementline cel ON (ce.M_CostElement_ID = cel.M_CostElement_ID)
                            INNER JOIN M_CostElement refEle ON (CAST(cel.M_Ref_CostElement AS INTEGER) = refEle.M_CostElement_ID) 
                               WHERE ce.AD_Client_ID = { GetAD_Client_ID() } AND ce.M_CostElement_ID = { costElement_ID }
                               AND ce.IsActive='Y'  AND cel.IsActive='Y'";
                dsCostCombination = DB.ExecuteDataset(sql, null, null);
                if (dsCostCombination != null && dsCostCombination.Tables.Count > 0 && dsCostCombination.Tables[0].Rows.Count > 0)
                {
                    for (int i = 0; i < dsCostCombination.Tables[0].Rows.Count; i++)
                    {
                        if (!string.IsNullOrEmpty(Util.GetValueOfString(dsCostCombination.Tables[0].Rows[i]["costingmethod"])))
                        {
                            MaterialCostingElement_ID = Util.GetValueOfInt(dsCostCombination.Tables[0].Rows[i]["m_ref_costelement"]);
                            MaterialCostingMethod = Util.GetValueOfString(dsCostCombination.Tables[0].Rows[i]["costingmethod"]);
                        }

                        costElement.Add(Util.GetValueOfInt(dsCostCombination.Tables[0].Rows[i]["m_ref_costelement"]));
                    }
                }

                string costElements = string.Join(",", costElement);

                // Get All Product
                sql = $@"SELECT c.ad_client_id ,  c.ad_org_id ,  c.m_product_id ,  c.m_attributesetinstance_id ,  c.c_acctschema_id , c.m_warehouse_id,
                           c.m_costtype_id ,   c.m_costelement_id ,  c.cumulatedamt ,  c.cumulatedqty ,  c.currentcostprice ,  c.currentqty
                      FROM m_cost c 
                      INNER JOIN C_AcctSchema acct ON (c.C_AcctSchema_ID = acct.C_AcctSchema_ID AND c.m_costtype_id = acct.m_costtype_id)";
                sql += $@" WHERE c.ad_client_id = { GetAD_Client_ID() } ";

                /* Filter those record from product those elements is belongs to selected Cost Combination record*/
                if (!string.IsNullOrEmpty(costElements))
                {
                    sql += $" AND c.m_costelement_id IN ({costElements})";
                }

                /* Consider those product on which that Cost Element is linked which is linked on the selected Cost Combination Record */
                sql += $@" AND M_Product_ID IN (SELECT M_Product_ID FROM M_Product mp 
                        INNER JOIN M_Product_Category mpc ON (mp.M_Product_Category_ID = mpc.M_Product_Category_ID)
                        WHERE mpc.CostingMethod = {GlobalVariable.TO_STRING(MaterialCostingMethod)} OR mpc.M_CostElement_ID IN (
                        SELECT ce.M_CostElement_ID FROM M_CostElement ce
                        INNER JOIN M_CostElementLine cel ON (ce.M_CostElement_ID = cel.M_CostElement_ID)
                        INNER JOIN M_CostElement refEle ON (CAST(cel.M_Ref_CostElement AS INTEGER) = refEle.M_CostElement_ID AND refEle.costingmethod IS NOT NULL)
                        WHERE CAST(cel.M_Ref_CostElement AS INTEGER)= {MaterialCostingElement_ID}) )";

                sql += @" ORDER BY c.m_product_id ,   c.ad_org_id ,  c.m_attributesetinstance_id ,  c.c_acctschema_id, c.m_warehouse_id ";
                dsProductCost = DB.ExecuteDataset(sql, null, null);

                if (dsProductCost != null && dsProductCost.Tables.Count > 0 && dsProductCost.Tables[0].Rows.Count > 0)
                {
                    // update all record of m_Cost having cost Element = costElement_ID
                    sql = "UPDATE M_Cost SET currentcostprice = 0 , currentqty = 0 , cumulatedamt = 0 , cumulatedqty = 0 WHERE M_CostElement_ID = " + costElement_ID +
                         " AND AD_Client_ID = " + GetAD_Client_ID();
                    int no = Util.GetValueOfInt(DB.ExecuteScalar(sql, null, Get_TrxName()));

                    for (int i = 0; i < dsProductCost.Tables[0].Rows.Count; i++)
                    {
                        if (!costElement.Contains(Util.GetValueOfInt(dsProductCost.Tables[0].Rows[i]["m_costelement_id"])))
                        {
                            continue;
                        }

                        if (_m_Product_ID != Util.GetValueOfInt(dsProductCost.Tables[0].Rows[i]["m_product_id"]) ||
                             _ad_Org_ID != Util.GetValueOfInt(dsProductCost.Tables[0].Rows[i]["ad_org_id"]) ||
                             _m_Attributesetinstance_ID != Util.GetValueOfInt(dsProductCost.Tables[0].Rows[i]["m_attributesetinstance_id"]) ||
                            _c_AcctSchema_ID != Util.GetValueOfInt(dsProductCost.Tables[0].Rows[i]["c_acctschema_id"]) ||
                            _m_Warehouse_ID != Util.GetValueOfInt(dsProductCost.Tables[0].Rows[i]["M_Warehouse_ID"]))
                        {
                            _m_Product_ID = Util.GetValueOfInt(dsProductCost.Tables[0].Rows[i]["m_product_id"]);
                            _ad_Org_ID = Util.GetValueOfInt(dsProductCost.Tables[0].Rows[i]["ad_org_id"]);
                            _m_Attributesetinstance_ID = Util.GetValueOfInt(dsProductCost.Tables[0].Rows[i]["m_attributesetinstance_id"]);
                            _c_AcctSchema_ID = Util.GetValueOfInt(dsProductCost.Tables[0].Rows[i]["c_acctschema_id"]);
                            _m_Warehouse_ID = Util.GetValueOfInt(dsProductCost.Tables[0].Rows[i]["M_Warehouse_ID"]);

                            MProduct product = new MProduct(GetCtx(), _m_Product_ID, Get_TrxName());
                            MAcctSchema acctSchema = new MAcctSchema(GetCtx(), _c_AcctSchema_ID, Get_TrxName());

                            costcombination = MCost.Get(product, _m_Attributesetinstance_ID, acctSchema, _ad_Org_ID,
                                Util.GetValueOfInt(dsCostCombination.Tables[0].Rows[0]["M_CostElement_ID"]), _m_Warehouse_ID);
                        }

                        // created object of Cost elemnt for checking iscalculated = true/ false
                        ce = MCostElement.Get(GetCtx(), Util.GetValueOfInt(dsProductCost.Tables[0].Rows[i]["m_costelement_id"]));

                        costcombination.SetCurrentCostPrice(Decimal.Add(costcombination.GetCurrentCostPrice(), Util.GetValueOfDecimal(dsProductCost.Tables[0].Rows[i]["currentcostprice"])));
                        costcombination.SetCumulatedAmt(Decimal.Add(costcombination.GetCumulatedAmt(), Util.GetValueOfDecimal(dsProductCost.Tables[0].Rows[i]["cumulatedamt"])));

                        // if calculated = true then we added qty else not and costing method is Standard Costing
                        if (ce.IsCalculated() || ce.GetCostingMethod() == MCostElement.COSTINGMETHOD_StandardCosting)
                        {
                            costcombination.SetCurrentQty(Decimal.Add(costcombination.GetCurrentQty(), Util.GetValueOfDecimal(dsProductCost.Tables[0].Rows[i]["currentqty"])));
                            costcombination.SetCumulatedQty(Decimal.Add(costcombination.GetCumulatedQty(), Util.GetValueOfDecimal(dsProductCost.Tables[0].Rows[i]["cumulatedqty"])));
                        }
                        if (costcombination.Save())
                        {
                            Commit();
                        }
                        else
                        {
                            log.Info("Cost Combination not updated for this product <===> " + Util.GetValueOfInt(dsProductCost.Tables[0].Rows[i]["m_product_id"]));
                        }
                    }
                    dsProductCost.Dispose();
                }
            }
            catch
            {
                if (dsProductCost != null)
                {
                    dsProductCost.Dispose();
                }
                if (dsCostCombination != null)
                {
                    dsCostCombination.Dispose();
                }
            }
            return Msg.GetMsg(GetCtx(), "VAS_CostCombSucessfullyUpdated");
        }
    }
}
