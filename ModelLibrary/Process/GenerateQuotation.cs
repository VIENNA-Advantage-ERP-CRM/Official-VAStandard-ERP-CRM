﻿using System;
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
//using ViennaAdvantage.Model;

namespace ViennaAdvantageServer.Process
{
    public class GenerateQuotation : SvrProcess
    {
        #region Private Variable
        /**	Project         		*/
        private int _C_Project_ID = 0;
        /**BPartner Customer        */
        private int C_Bpartner_id = 0;
        /**BPartner Location        */
        private int C_Bpartner_Location_id = 0;
        /**BPartner Prospect        */
        private int C_BPartnerSR_ID = 0;
        /*Order                   	*/
        //  private int C_Order_ID = 0;
        /**ProjectLine       */
        private int C_ProjectLine_ID = 0;

        #endregion

        protected override void Prepare()
        {
            _C_Project_ID = GetRecord_ID();
        }

        /// <summary>
        /// Generate Quotation
        /// </summary>
        /// <returns>Process Message</returns>
        protected override string DoIt()
        {
            // Int32 value = 0;
            string msg = "";
            ValueNamePair vp = null;
            MBPartner bp = null;
            MOrderLine ol = null;

            log.Info("C_Project_ID=" + _C_Project_ID);
            if (_C_Project_ID == 0)
            {
                throw new ArgumentException("C_Project_ID == 0");
            }

            MProject fromProject = new MProject(GetCtx(), _C_Project_ID, Get_TrxName());

            if (fromProject.GetGenerate_Quotation() == null)
            {
                throw new ArgumentException("No Generate Quotation found on Project.");
            }
            if (fromProject.GetGenerate_Quotation().Trim() == "Y")
            {
                throw new ArgumentException("Sales Quotation already generated");
            }

            // if Business Partner or Prospect is not selected then gives error
            if (fromProject.GetC_BPartner_ID() == 0 && fromProject.GetC_BPartnerSR_ID() == 0)
            {
                return Msg.GetMsg(GetCtx(), "SelectBP/Prospect");
            }

            //JID_1200: if Business Partner/Prospect Location is not selected then gives error
            if (fromProject.GetC_BPartner_Location_ID() == 0)
            {
                return Msg.GetMsg(GetCtx(), "SelectBPLocation");
            }

            MOrder order = new MOrder(GetCtx(), 0, Get_TrxName());
            order.SetAD_Client_ID(fromProject.GetAD_Client_ID());
            order.SetAD_Org_ID(fromProject.GetAD_Org_ID());
            C_Bpartner_id = fromProject.GetC_BPartner_ID();
            C_Bpartner_Location_id = fromProject.GetC_BPartner_Location_ID();
            C_BPartnerSR_ID = fromProject.GetC_BPartnerSR_ID();

            MBPartnerLocation bpartnerloc = new MBPartnerLocation(GetCtx(), C_Bpartner_Location_id, Get_TrxName());
            //String currentdate = DateTime.Now.ToString();
            String sqlprjln = "SELECT COUNT(C_ProjectLine_ID) FROM C_ProjectLine WHERE C_Project_ID=" + _C_Project_ID;
            C_ProjectLine_ID = Util.GetValueOfInt(DB.ExecuteScalar(sqlprjln, null, Get_TrxName()));
            if (C_ProjectLine_ID != 0)
            {
                order.SetDateOrdered(DateTime.Now.ToLocalTime());
                order.SetDatePromised(DateTime.Now.ToLocalTime());
                if (C_Bpartner_id != 0)
                {
                    order.SetC_BPartner_ID(fromProject.GetC_BPartner_ID());
                    if (bpartnerloc.IsShipTo() == true)
                    {
                        order.SetC_BPartner_Location_ID(fromProject.GetC_BPartner_Location_ID());
                        order.SetAD_User_ID(fromProject.GetAD_User_ID());
                    }
                    if (bpartnerloc.IsBillTo() == true)
                    {
                        order.SetBill_Location_ID(fromProject.GetC_BPartner_Location_ID());
                        order.SetBill_User_ID(fromProject.GetAD_User_ID());
                    }
                }
                if (C_BPartnerSR_ID != 0)
                {

                    order.SetC_BPartner_ID(fromProject.GetC_BPartnerSR_ID());
                    if (bpartnerloc.IsShipTo() == true)
                    {
                        order.SetC_BPartner_Location_ID(fromProject.GetC_BPartner_Location_ID());
                        order.SetAD_User_ID(fromProject.GetAD_User_ID());
                    }
                    if (bpartnerloc.IsBillTo() == true)
                    {
                        order.SetBill_Location_ID(fromProject.GetC_BPartner_Location_ID());
                        order.SetBill_User_ID(fromProject.GetAD_User_ID());
                    }
                }

                String sql = "SELECT C_DocType_ID FROM C_DocType WHERE DocBaseType = 'SOO' AND DocSubTypeSO = 'ON' AND IsReturnTrx = 'N' AND IsActive = 'Y' AND AD_Client_ID = "
                            + GetCtx().GetAD_Client_ID() + " AND AD_Org_ID IN (0, " + GetAD_Org_ID() + ") ORDER BY  AD_Org_ID DESC";
                int Doctype_id = Util.GetValueOfInt(DB.ExecuteScalar(sql, null, Get_TrxName()));
                int MPriceList_id = fromProject.GetM_PriceList_ID();
                order.SetM_PriceList_ID(MPriceList_id);

                order.SetC_Project_ID(GetRecord_ID());
                if (fromProject.GetSalesRep_ID() > 0)
                    order.SetSalesRep_ID(fromProject.GetSalesRep_ID());
                order.SetC_Currency_ID(fromProject.GetC_Currency_ID());
                if (C_Bpartner_id != 0)
                {
                    bp = new MBPartner(GetCtx(), C_Bpartner_id, Get_TrxName());
                    if (bp.GetC_Campaign_ID() == 0 && fromProject.GetC_Campaign_ID() > 0)
                        bp.SetC_Campaign_ID(fromProject.GetC_Campaign_ID());
                    //bp.SetAD_Client_ID(fromProject.GetAD_Client_ID());
                    //bp.SetAD_Org_ID(fromProject.GetAD_Org_ID());
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
                    if (bp.GetC_Campaign_ID() == 0 && fromProject.GetC_Campaign_ID() > 0)
                        bp.SetC_Campaign_ID(fromProject.GetC_Campaign_ID());
                    //bp.SetAD_Client_ID(fromProject.GetAD_Client_ID());
                    //bp.SetAD_Org_ID(fromProject.GetAD_Org_ID());
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
                if (order.GetC_Campaign_ID() == 0 && fromProject.GetC_Campaign_ID() > 0)
                    order.SetC_Campaign_ID(fromProject.GetC_Campaign_ID());
                order.SetDocStatus("IP");
                order.SetC_DocType_ID(Doctype_id);
                order.SetC_DocTypeTarget_ID(Doctype_id);
                order.SetIsSOTrx(true);
                order.Set_Value("IsSalesQuotation", true);

                //Set VA077 values on header level
                if (Env.IsModuleInstalled("VA077_"))
                {
                    //Get the org count of legal entity org
                    sql = @"SELECT Count(AD_Org_ID) FROM AD_Org WHERE IsActive='Y' 
                           AND (IsProfitCenter ='Y' OR IsCostCenter ='Y') AND 
                           AD_Client_Id=" + fromProject.GetAD_Client_ID() + @" AND LegalEntityOrg = " + fromProject.GetAD_Org_ID();
                    int result = Util.GetValueOfInt(DB.ExecuteScalar(sql));
                    if (result > 0)
                    {
                        order.SetVA077_IsLegalEntity(true);
                    }
                    order.SetVA077_SalesCoWorker(fromProject.GetVA077_SalesCoWorker());
                    order.SetVA077_SalesCoWorkerPer(fromProject.GetVA077_SalesCoWorkerPer());
                    order.Set_Value("VA077_TotalMarginAmt", fromProject.Get_Value("VA077_TotalMarginAmt"));
                    order.Set_Value("VA077_TotalPurchaseAmt", fromProject.Get_Value("VA077_TotalPurchaseAmt"));
                    order.Set_Value("VA077_TotalSalesAmt", fromProject.Get_Value("VA077_TotalSalesAmt"));
                    order.Set_Value("VA077_MarginPercent", fromProject.Get_Value("VA077_MarginPercent"));
                    order.Set_Value("VA077_OrderRef", fromProject.GetPOReference());
                }

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
                MProjectLine[] lines = fromProject.GetLines();
                for (int i = 0; i < lines.Length; i++)
                {
                    ol = new MOrderLine(order);
                    ol.SetLine(lines[i].GetLine());
                    ol.SetDescription(lines[i].GetDescription());
                    ol.SetM_Product_ID(lines[i].GetM_Product_ID(), true);
                    ol.SetQtyEntered(lines[i].GetPlannedQty());
                    ol.SetQtyOrdered(lines[i].GetPlannedQty());
                    ol.SetPriceEntered(lines[i].GetPlannedPrice());
                    ol.SetPriceActual(lines[i].GetPlannedPrice());
                    ol.SetPriceList(lines[i].GetPriceList());

                    // Set Attribute and UOM from Opportunity Lines
                    if (lines[i].Get_ColumnIndex("M_AttributeSetInstance_ID") >= 0)
                    {
                        ol.SetM_AttributeSetInstance_ID(lines[i].GetM_AttributeSetInstance_ID());
                    }

                    if (lines[i].Get_ColumnIndex("C_UOM_ID") >= 0)
                    {
                        ol.SetC_UOM_ID(Util.GetValueOfInt(lines[i].Get_Value("C_UOM_ID")));
                    }

                    //Set VA077 values on line level
                    if (Env.IsModuleInstalled("VA077_"))
                    {
                        ol.Set_Value("VA077_MarginPercent", lines[i].Get_Value("VA077_MarginPercent"));
                        ol.Set_Value("VA077_MarginAmt", lines[i].Get_Value("VA077_MarginAmt"));
                        ol.Set_Value("VA077_PurchasePrice", lines[i].Get_Value("VA077_PurchasePrice"));
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

                fromProject.SetRef_Order_ID(order.GetC_Order_ID());
                fromProject.SetGenerate_Quotation("Y");
                if (!fromProject.Save())
                {
                    Get_TrxName().Rollback();
                    log.SaveError("ProjectNotSaved", "");
                    return Msg.GetMsg(GetCtx(), "ProjectNotSaved");
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

                msg = Msg.GetMsg(GetCtx(), "QuotationGenerated") + order.GetDocumentNo();
            }
            else
                msg = Msg.GetMsg(GetCtx(), "NoLines");
            return msg;
        }
    }
}
