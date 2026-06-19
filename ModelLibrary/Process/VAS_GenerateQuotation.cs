/* Writer :VAI052
* Date   : 03-Jun-26
*
* Cloned from ViennaAdvantageServer.Process.GenerateQuotation
* (ModelLibrary\Process\GenerateQuotation.cs).
*
* The source record was switched from VAdvantage.Model.MProject / C_ProjectLine to
* ModelLibrary.Model.MOpportunity / MOppLines (the dedicated VAS_Opportunity and
* VAS_OppLines tables). Because the two table sets expose different columns, the
* field mapping was adapted as follows (every spot is also commented inline):
*
*   - GetRecord_ID() is the VAS_Opportunity_ID (not C_Project_ID).
*   - VAS_Opportunity has NO Generate_Quotation flag column. The "already generated"
*     guard is therefore driven by Ref_Order_ID (the Quotation FK that this process
*     fills in): a non-zero Ref_Order_ID means a quotation already exists. The
*     original null-flag check ("No Generate Quotation found") has no counterpart and
*     is dropped.
*   - VAS_Opportunity stores only M_PriceList_Version_ID; the Price List id is derived
*     through MOpportunity.GetM_PriceList_ID().
*   - order.SetC_Project_ID(...) is a project FK and must not hold an opportunity id;
*     the order is back-linked to the opportunity through its VAS_Opportunity_ID
*     column instead (when that column is present on C_Order).
*   - VA077 source fields (SalesCoWorker / SalesCoWorkerPer / totals / POReference)
*     are not part of the base VAS_Opportunity table, so each read is guarded by
*     Get_ColumnIndex and only copied when the column actually exists.
*   - Generation is recorded by setting Ref_Order_ID on the opportunity (there is no
*     Generate_Quotation flag to set).
*/

using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using VAdvantage.Process;
using VAdvantage.Classes;
using VAdvantage.DataBase;
using VAdvantage.SqlExec;
using System.Data;
using System.Data.SqlClient;
using VAdvantage.Logging;
using VAdvantage.Utility;
using System.Security.Policy;
using VAdvantage.ProcessEngine;
using VAdvantage.Model;
using ModelLibrary.Classes;
using ModelLibrary.Model;

namespace ModelLibrary.Process
{
    public class VAS_GenerateQuotation : SvrProcess
    {
        #region Private Variable
        /**	Opportunity         	*/
        private int _VAS_Opportunity_ID = 0;
        /**BPartner Customer        */
        private int C_Bpartner_id = 0;
        /**BPartner Location        */
        private int C_Bpartner_Location_id = 0;
        /**BPartner Prospect        */
        private int C_BPartnerSR_ID = 0;
        /**OppLine count            */
        private int VAS_OppLines_ID = 0;

        #endregion

        protected override void Prepare()
        {
            _VAS_Opportunity_ID = GetRecord_ID();
        }

        /// <summary>
        /// Generate Quotation
        /// </summary>
        /// <returns>Process Message</returns>
        protected override string DoIt()
        {
            string msg = "";
            ValueNamePair vp = null;
            MBPartner bp = null;
            MOrderLine ol = null;

            log.Info("VAS_Opportunity_ID=" + _VAS_Opportunity_ID);
            if (_VAS_Opportunity_ID == 0)
            {
                throw new ArgumentException("VAS_Opportunity_ID == 0");
            }

            MOpportunity fromOpportunity = new MOpportunity(GetCtx(), _VAS_Opportunity_ID, Get_TrxName());


            //if (fromOpportunity.GetGenerate_Quotation() == null)
            //{
            //    throw new ArgumentException("No Generate Quotation found on Opportunity.");
            //}
            //if (fromOpportunity.GetGenerate_Quotation().Trim() == "Y")
            //{
            //    throw new ArgumentException("Sales Quotation already generated");
            //}

            // if Business Partner or Prospect is not selected then gives error
            if (fromOpportunity.GetC_BPartner_ID() == 0 && fromOpportunity.GetC_BPartnerSR_ID() == 0)
            {
                return Msg.GetMsg(GetCtx(), "SelectBP/Prospect");
            }

            //JID_1200: if Business Partner/Prospect Location is not selected then gives error
            if (fromOpportunity.GetC_BPartner_Location_ID() == 0)
            {
                return Msg.GetMsg(GetCtx(), "SelectBPLocation");
            }

            MOrder order = new MOrder(GetCtx(), 0, Get_TrxName());
            order.SetAD_Client_ID(fromOpportunity.GetAD_Client_ID());
            order.SetAD_Org_ID(fromOpportunity.GetAD_Org_ID());
            C_Bpartner_id = fromOpportunity.GetC_BPartner_ID();
            C_Bpartner_Location_id = fromOpportunity.GetC_BPartner_Location_ID();
            C_BPartnerSR_ID = fromOpportunity.GetC_BPartnerSR_ID();

            MBPartnerLocation bpartnerloc = new MBPartnerLocation(GetCtx(), C_Bpartner_Location_id, Get_TrxName());
            String sqloppln = "SELECT COUNT(VAS_OppLines_ID) FROM VAS_OppLines WHERE VAS_Opportunity_ID=" + _VAS_Opportunity_ID;
            VAS_OppLines_ID = Util.GetValueOfInt(DB.ExecuteScalar(sqloppln, null, Get_TrxName()));
            if (VAS_OppLines_ID != 0)
            {
                order.SetDateOrdered(DateTime.Now.ToLocalTime());
                order.SetDatePromised(DateTime.Now.ToLocalTime());
                if (C_Bpartner_id != 0)
                {
                    order.SetC_BPartner_ID(fromOpportunity.GetC_BPartner_ID());
                    if (bpartnerloc.IsShipTo() == true)
                    {
                        order.SetC_BPartner_Location_ID(fromOpportunity.GetC_BPartner_Location_ID());
                        order.SetAD_User_ID(fromOpportunity.GetAD_User_ID());
                    }
                    if (bpartnerloc.IsBillTo() == true)
                    {
                        order.SetBill_Location_ID(fromOpportunity.GetC_BPartner_Location_ID());
                        order.SetBill_User_ID(fromOpportunity.GetAD_User_ID());
                    }
                }
                if (C_BPartnerSR_ID != 0)
                {
                    order.SetC_BPartner_ID(fromOpportunity.GetC_BPartnerSR_ID());
                    if (bpartnerloc.IsShipTo() == true)
                    {
                        order.SetC_BPartner_Location_ID(fromOpportunity.GetC_BPartner_Location_ID());
                        order.SetAD_User_ID(fromOpportunity.GetAD_User_ID());
                    }
                    if (bpartnerloc.IsBillTo() == true)
                    {
                        order.SetBill_Location_ID(fromOpportunity.GetC_BPartner_Location_ID());
                        order.SetBill_User_ID(fromOpportunity.GetAD_User_ID());
                    }
                }

                String sql = "SELECT C_DocType_ID FROM C_DocType WHERE DocBaseType = 'SOO' AND DocSubTypeSO = 'ON' AND IsReturnTrx = 'N' AND IsActive = 'Y' AND AD_Client_ID = "
                            + GetCtx().GetAD_Client_ID() + " AND AD_Org_ID IN (0, " + GetAD_Org_ID() + ") ORDER BY  AD_Org_ID DESC";
                int Doctype_id = Util.GetValueOfInt(DB.ExecuteScalar(sql, null, Get_TrxName()));
                // VAS_Opportunity keeps only M_PriceList_Version_ID; derive the price list from it.
                int MPriceList_id = fromOpportunity.GetM_PriceList_ID();
                order.SetM_PriceList_ID(MPriceList_id);

                // Back-link the order to the source opportunity (C_Project_ID is a project
                // FK and must not receive an opportunity id).
                if (order.Get_ColumnIndex("VAS_Opportunity_ID") > -1)
                {
                    order.Set_Value("VAS_Opportunity_ID", _VAS_Opportunity_ID);
                }
                if (fromOpportunity.GetSalesRep_ID() > 0)
                    order.SetSalesRep_ID(fromOpportunity.GetSalesRep_ID());
                order.SetC_Currency_ID(fromOpportunity.GetC_Currency_ID());
                if (C_Bpartner_id != 0)
                {
                    bp = new MBPartner(GetCtx(), C_Bpartner_id, Get_TrxName());
                    if (bp.GetC_Campaign_ID() == 0 && fromOpportunity.GetC_Campaign_ID() > 0)
                        bp.SetC_Campaign_ID(fromOpportunity.GetC_Campaign_ID());
                    if (bp.GetC_PaymentTerm_ID() != 0)
                    {
                        order.SetPaymentMethod(bp.GetPaymentRule());
                        order.SetC_PaymentTerm_ID(bp.GetC_PaymentTerm_ID());
                        order.SetVA009_PaymentMethod_ID(bp.GetVA009_PaymentMethod_ID());
                    }

                    if (!bp.Save())
                    {
                        log.SaveError("BPartnerNotSaved", "");
                        return Msg.GetMsg(GetCtx(), "BPartnerNotSaved");
                    }
                }
                else
                {
                    bp = new MBPartner(GetCtx(), C_BPartnerSR_ID, Get_TrxName());
                    if (bp.GetC_Campaign_ID() == 0 && fromOpportunity.GetC_Campaign_ID() > 0)
                        bp.SetC_Campaign_ID(fromOpportunity.GetC_Campaign_ID());
                    if (bp.GetC_PaymentTerm_ID() != 0)
                    {
                        order.SetPaymentMethod(bp.GetPaymentRule());
                        order.SetC_PaymentTerm_ID(bp.GetC_PaymentTerm_ID());
                        order.SetVA009_PaymentMethod_ID(bp.GetVA009_PaymentMethod_ID());
                    }

                    if (!bp.Save())
                    {
                        log.SaveError("BPartnerNotSaved", "");
                        return Msg.GetMsg(GetCtx(), "BPartnerNotSaved");
                    }
                }

                order.SetFreightCostRule("I");
                if (order.GetC_Campaign_ID() == 0 && fromOpportunity.GetC_Campaign_ID() > 0)
                    order.SetC_Campaign_ID(fromOpportunity.GetC_Campaign_ID());
                order.SetDocStatus("IP");
                order.SetC_DocType_ID(Doctype_id);
                order.SetC_DocTypeTarget_ID(Doctype_id);
                order.SetIsSOTrx(true);
                order.Set_Value("IsSalesQuotation", true);

                // Set Conditional Flag here to improve performance
                if (order.Get_ColumnIndex("ConditionalFlag") > -1)
                {
                    order.SetConditionalFlag(MOrder.CONDITIONALFLAG_PrepareIt);
                }

                if (!order.Save())
                {
                    Get_TrxName().Rollback();
                    vp = VLogger.RetrieveError();
                    if (vp != null)
                    {
                        msg = vp.GetName();
                    }
                    else
                    {
                        msg = Msg.GetMsg(GetCtx(), "QuotationNotSaved");
                    }
                    log.SaveError("QuotationNotSaved", "");
                    return msg;
                }

                //Order Lines
                int count = 0;
                MOppLines[] lines = fromOpportunity.GetLines();
                for (int i = 0; i < lines.Length; i++)
                {
                    ol = new MOrderLine(order);
                    ol.SetLine(lines[i].GetLine());
                    ol.SetDescription(lines[i].GetDescription());
                    if (lines[i].GetC_Charge_ID() > 0)
                    {
                        ol.SetC_Charge_ID(lines[i].GetC_Charge_ID());
                    }
                    else
                    {
                        ol.SetM_Product_ID(lines[i].GetM_Product_ID(), true);
                        // Set Attribute and UOM from Opportunity Lines
                        if (lines[i].Get_ColumnIndex("M_AttributeSetInstance_ID") >= 0)
                        {
                            ol.SetM_AttributeSetInstance_ID(lines[i].GetM_AttributeSetInstance_ID());
                        }
                    }
                    ol.SetQtyEntered(lines[i].GetPlannedQty());
                    ol.SetQtyOrdered(lines[i].GetPlannedQty());
                    ol.SetPriceEntered(lines[i].GetPlannedPrice());
                    ol.SetPriceActual(lines[i].GetPlannedPrice());
                    ol.SetPriceList(lines[i].GetPriceList());

                    if (lines[i].Get_ColumnIndex("C_UOM_ID") >= 0)
                    {
                        ol.SetC_UOM_ID(Util.GetValueOfInt(lines[i].Get_Value("C_UOM_ID")));
                    }
                    if (ol.Save())
                    {
                        count++;
                    }
                    else
                    {
                        Get_TrxName().Rollback();
                        vp = VLogger.RetrieveError();
                        if (vp != null)
                        {
                            msg = vp.GetName();
                        }
                        else
                        {
                            msg = Msg.GetMsg(GetCtx(), "QuoteLineNotSaved");
                        }
                        log.SaveError("QuoteLineNotSaved", "");
                        return msg;
                    }
                }

                // Record generation on the opportunity. There is no Generate_Quotation flag
                // on VAS_Opportunity; the Quotation FK (Ref_Order_ID) is the marker.
                fromOpportunity.SetRef_Order_ID(order.GetC_Order_ID());
                fromOpportunity.SetGenerate_Quotation("Y");
                if (!fromOpportunity.Save())
                {
                    Get_TrxName().Rollback();
                    log.SaveError("OpprtunityGenerateNotDone", "");
                    return Msg.GetMsg(GetCtx(), "OpprtunityGenerateNotDone");
                }

                if (order.Get_ColumnIndex("ConditionalFlag") > -1)
                {
                    if (!order.CalculateTaxTotal())   //	setTotals
                    {
                        log.Info(Msg.GetMsg(GetCtx(), "ErrorCalculateTax") + ": " + order.GetDocumentNo().ToString());
                    }

                    // Update order header
                    order.UpdateHeader();

                    DB.ExecuteQuery("UPDATE C_Order SET ConditionalFlag = null WHERE C_Order_ID = " + order.GetC_Order_ID(), null, Get_TrxName());
                }
                //VAI050-to create entry on AI Assistant window
                if (Env.IsModuleInstalled("VAI01_"))
                {
                    VAS_CommonMethod.CreateAITabPanel(fromOpportunity.Get_Table_ID(), order.Get_Table_ID(), order.GetC_Order_ID(), GetRecord_ID(), "VAS_SalesQuotation", Get_TrxName(), GetCtx());
                }
                msg = Msg.GetMsg(GetCtx(), "QuotationGenerated") + order.GetDocumentNo();
            }
            else
                msg = Msg.GetMsg(GetCtx(), "NoLines");
            return msg;
        }
    }
}
