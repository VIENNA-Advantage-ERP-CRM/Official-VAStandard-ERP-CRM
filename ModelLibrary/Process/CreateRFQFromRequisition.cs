﻿using System;
using System.Collections.Generic;
using System.Data;
using System.Linq;
using System.Text;
using VAdvantage.DataBase;
using VAdvantage.Logging;
using VAdvantage.ProcessEngine;
using VAdvantage.Model;
using VAdvantage.Utility;

namespace VAdvantage.Process
{
    class CreateRFQFromRequisition : SvrProcess
    {
        private int AD_Org_ID = 0; // Variable for rfq organization input.
        private int Requisition_Org_ID = 0; // Variable for filtering requisition by organization.
        private int Warehouse_ID = 0; // Variable for filtering requisition by warehouse.
        private string Req = ""; // variable for processing only selected requisition.        
        private int RfQTopic_ID = 0; // Variable for rfq topic input.
        private string RfQtype = ""; // Variable for rfq type input.
        DateTime? DocDateFrom = null, DocDateTo = null; // Document date filter on requisition.
        DateTime? ReqDateFrom = null, ReqDateTo = null; // Requested date filter on requisition.
        DateTime? DateResponse = null;
        private bool isConsolidate = false; // Create consolidated document or not.
        private int C_Currency_ID = 0; // Currency to map on rfq header.


        protected override string DoIt()
        {
            // Passed parameters info
            log.Info("Process start - Parameter - AD_Org_ID=" + AD_Org_ID
                + ", Requisition Organization=" + Requisition_Org_ID
                 + ", Warehouse=" + Warehouse_ID
                 + " Requisition=" + Req
                 + " RfQ Topic=" + RfQTopic_ID
                 + " RfQ Type=" + RfQtype
                + ", DocDate=" + DocDateFrom + "/" + DocDateTo
                + ", DateRequired=" + ReqDateFrom + "/" + ReqDateTo
                + ", Currency =" + C_Currency_ID
                + ", ConsolidateDocument" + isConsolidate);

            StringBuilder Sql = new StringBuilder();
            DataSet _dsReq = null;
            string Result = "";

            Sql.Append(@"SELECT 
                        ReqLine.M_requisition_ID,
                        ReqLine.M_requisitionLine_ID,
                        reqline.M_Product_ID,
                        reqline.M_AttributeSetInstance_ID,
                        reqline.Description,
                        reqline.C_Uom_ID,
                        (reqline.Qty - reqline.DTD001_DeliveredQty) as Qty,
                        reqline.PriceActual,
                        req.DateRequired                        
                        FROM M_RequisitionLine ReqLine
                        INNER JOIN M_requisition req
                        ON (reqline.M_requisition_ID  =req.M_requisition_ID)
                        WHERE ReqLine.IsActive        ='Y' AND req.IsActive        ='Y'
                        AND ReqLine.AD_org_ID         =" + Requisition_Org_ID + " AND req.DocStatus='CO' AND reqline.Qty!=reqline.DTD001_DeliveredQty  ");
            // Requisition selection check
            if (!string.IsNullOrEmpty(Req))
            {
                Sql.Append(" AND ReqLine.M_requisition_ID IN (" + Req + ") ");
            }
            // Warehouse selection check
            if (Warehouse_ID > 0)
            {
                Sql.Append(" AND Req.M_Warehouse_ID        =" + Warehouse_ID + " ");
            }
            // Document Date Check
            if (DocDateFrom != null && DocDateTo != null)
            {
                Sql.Append(" AND TRUNC( Req.DateDoc) BETWEEN " + GlobalVariable.TO_DATE(DocDateFrom, true) + "  AND " + GlobalVariable.TO_DATE(DocDateTo, true) + " ");
            }
            else if (DocDateFrom != null)
            {
                Sql.Append(" AND TRUNC( Req.DateDoc) >=" + GlobalVariable.TO_DATE(DocDateFrom, true) + " ");
            }
            else if (DocDateTo != null)
            {
                Sql.Append(" AND TRUNC( Req.DateDoc) =< " + GlobalVariable.TO_DATE(DocDateTo, true) + "");
            }
            // Required Date Check
            if (ReqDateFrom != null && ReqDateTo != null)
            {
                Sql.Append(" AND TRUNC( Req.DateRequired ) BETWEEN " + GlobalVariable.TO_DATE(ReqDateFrom, true) + "  AND " + GlobalVariable.TO_DATE(ReqDateTo, true) + " ");
            }
            else if (ReqDateFrom != null)
            {
                Sql.Append(" AND TRUNC( Req.DateRequired ) >=" + GlobalVariable.TO_DATE(ReqDateFrom, true) + " ");
            }
            else if (ReqDateTo != null)
            {
                Sql.Append(" AND TRUNC( Req.DateRequired ) =< " + GlobalVariable.TO_DATE(ReqDateTo, true) + "");
            }
            if (!isConsolidate)
            {
                Sql.Append(" ORDER BY ReqLine.M_requisition_ID ");
            }
            else
            {
                Sql.Append(" ORDER BY req.DateRequired ASC ");
            }
            try
            {
                _dsReq = DB.ExecuteDataset(Sql.ToString());
                if (_dsReq != null && _dsReq.Tables[0].Rows.Count > 0)
                {
                    Result = CreateRfQ(_dsReq);
                }
                else
                {
                    return Msg.GetMsg(GetCtx(), "NoReqFound");
                }

            }
            catch (Exception e)
            {
                if (_dsReq != null)
                {
                    _dsReq.Dispose();
                    _dsReq = null;
                }
                Get_TrxName().Rollback();
                return e.Message;
            }

            if (_dsReq != null)
            {
                _dsReq.Dispose();
                _dsReq = null;
            }

            log.Info(Result);
            return Result;
        }
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
                else if (name.Equals("AD_Org_ID"))
                {
                    AD_Org_ID = para[i].GetParameterAsInt();
                }
                else if (name.Equals("OrgColumn"))
                {
                    Requisition_Org_ID = para[i].GetParameterAsInt();
                }
                else if (name.Equals("M_Warehouse_ID"))
                {
                    Warehouse_ID = para[i].GetParameterAsInt();
                }
                else if (name.Equals("C_Currency_ID"))
                {
                    C_Currency_ID = para[i].GetParameterAsInt();
                }
                else if (name.Equals("M_Requisition_ID"))
                {
                    Req = para[i].GetParameter().ToString();
                }

                else if (name.Equals("DateDoc"))
                {
                    DocDateFrom = (DateTime?)para[i].GetParameter();
                    DocDateTo = (DateTime?)para[i].GetParameter_To();
                }
                else if (name.Equals("DateRequired"))
                {
                    ReqDateFrom = (DateTime?)para[i].GetParameter();
                    ReqDateTo = (DateTime?)para[i].GetParameter_To();
                }
                else if (name.Equals("QuoteType"))
                {
                    RfQtype = (String)para[i].GetParameter();
                }
                else if (name.Equals("C_RfQ_Topic_ID"))
                {
                    RfQTopic_ID = para[i].GetParameterAsInt();
                }
                else if (name.Equals("DateResponse"))
                {
                    DateResponse = (DateTime?)para[i].GetParameter();
                }
                else if (name.Equals("ConsolidateDocument"))
                {
                    isConsolidate = "Y".Equals(para[i].GetParameter());
                }
                else
                {
                    log.Log(Level.SEVERE, "Unknown Parameter: " + name);
                }
            }
        }

        /// <summary>
        /// </summary>
        /// <param name="_ds"></param> dataset contains all the records according to selection criteria.
        /// <returns></returns> returns the final message.
        private string CreateRfQ(DataSet _ds)
        {

            MRfQ rfq = null;
            int Requisition_ID = 0, LineNo = 0;
            string message = "";
            DataRow[] selectedTable = null;
            //VIS0336:changes done for inserting the record in vendor recommend tab when process run.
            DataSet dt = null;
            object[] RequisitionID = _ds.Tables[0].AsEnumerable().Select(r => r.Field<object>("M_requisition_ID")).ToArray();
            string result = string.Join(",", RequisitionID);

            if (Env.IsModuleInstalled("VA068_"))
            {
                string sql = "SELECT * FROM VA068_VendorRecomend v INNER JOIN M_RequisitionLine l ON l.M_RequisitionLine_ID=v.M_RequisitionLine_ID " +
                           "WHERE l.M_Requisition_ID IN (" + result + ")";
                dt = DB.ExecuteDataset(sql, null, Get_Trx());
            }

            for (int i = 0; i < _ds.Tables[0].Rows.Count; i++)
            {

                // If document is not consolidated
                if (!isConsolidate)
                {
                    if (rfq == null || Requisition_ID != Util.GetValueOfInt(_ds.Tables[0].Rows[i]["M_Requisition_ID"]))
                    {
                        LineNo = 0;
                        Requisition_ID = Util.GetValueOfInt(_ds.Tables[0].Rows[i]["M_Requisition_ID"]);
                        rfq = new MRfQ(GetCtx(), 0, Get_TrxName());
                        rfq.SetAD_Org_ID(AD_Org_ID);
                        rfq.SetName("Name");
                        rfq.SetSalesRep_ID(GetCtx().GetAD_User_ID());
                        rfq.SetC_RfQ_Topic_ID(RfQTopic_ID);
                        rfq.SetM_Requisition_ID(Requisition_ID);
                        rfq.SetDateWorkStart(System.DateTime.Now);
                        rfq.SetDateResponse(DateResponse);      // Added by Bharat on 15 Jan 2019 as asked by Puneet
                        if (Util.GetValueOfDateTime(_ds.Tables[0].Rows[i]["DateRequired"]) >= System.DateTime.Now)
                        {
                            rfq.SetDateWorkComplete(Util.GetValueOfDateTime(_ds.Tables[0].Rows[i]["DateRequired"]));
                        }
                        if (string.IsNullOrEmpty(RfQtype))
                        {
                            rfq.SetQuoteType("S");
                        }
                        else
                        {
                            rfq.SetQuoteType(RfQtype);
                        }
                        rfq.SetIsInvitedVendorsOnly(true);
                        rfq.SetIsQuoteAllQty(true);
                        rfq.SetIsRfQResponseAccepted(true);
                        rfq.SetC_Currency_ID(C_Currency_ID);
                        if (rfq.Save())
                        {
                            DB.ExecuteQuery("UPDATE C_Rfq SET Name='" + rfq.GetDocumentNo() + "' WHERE C_RfQ_ID= " + rfq.GetC_RfQ_ID(), null, Get_TrxName());
                            if (message == "")
                            {
                                message = Msg.GetMsg(GetCtx(), "RfQGeneratedSuccess") + " =" + rfq.GetDocumentNo();
                            }
                            else
                            {
                                message = message + "," + rfq.GetDocumentNo();
                            }
                        }
                        else
                        {
                            ValueNamePair vp = VLogger.RetrieveError();
                            if (vp != null)
                            {
                                Get_TrxName().Rollback();
                                return Msg.GetMsg(GetCtx(), "RfQHeadNotSaved") + "- " + vp.Name;
                            }
                            else
                            {
                                Get_TrxName().Rollback();
                                return Msg.GetMsg(GetCtx(), "RfQHeadNotSaved");
                            }

                        }
                    }
                }
                // If document is consolidated
                else
                {
                    if (rfq == null)
                    {
                        rfq = new MRfQ(GetCtx(), 0, Get_TrxName());
                        rfq.SetAD_Org_ID(AD_Org_ID);
                        rfq.SetName("Name");
                        rfq.SetSalesRep_ID(GetCtx().GetAD_User_ID());
                        rfq.SetC_RfQ_Topic_ID(RfQTopic_ID);
                        rfq.SetDateWorkStart(System.DateTime.Now);
                        rfq.SetDateResponse(DateResponse);      // Added by Bharat on 15 Jan 2019 as asked by Puneet
                        if (Util.GetValueOfDateTime(_ds.Tables[0].Rows[i]["DateRequired"]) >= System.DateTime.Now)
                        {
                            rfq.SetDateWorkComplete(Util.GetValueOfDateTime(_ds.Tables[0].Rows[i]["DateRequired"]));
                        }
                        if (string.IsNullOrEmpty(RfQtype))
                        {
                            rfq.SetQuoteType("S");
                        }
                        else
                        {
                            rfq.SetQuoteType(RfQtype);
                        }
                        rfq.SetIsInvitedVendorsOnly(true);
                        rfq.SetIsQuoteAllQty(true);
                        rfq.SetIsRfQResponseAccepted(true);
                        rfq.SetC_Currency_ID(C_Currency_ID);
                        if (!rfq.Save())
                        {
                            ValueNamePair vp = VLogger.RetrieveError();
                            if (vp != null)
                            {
                                Get_TrxName().Rollback();
                                return "RFQ Not Saved - " + vp.Name;
                            }
                            else
                            {
                                Get_TrxName().Rollback();
                                return "RFQ Not Saved";
                            }
                        }
                        else
                        {
                            DB.ExecuteQuery("UPDATE C_Rfq SET Name='" + rfq.GetDocumentNo() + "' WHERE C_RfQ_ID= " + rfq.GetC_RfQ_ID(), null, Get_TrxName());
                        }
                        message = "RfQ Generated =" + rfq.GetDocumentNo();

                    }
                }
                LineNo = LineNo + 10;
                // Create RfQ line
                MRfQLine RfqLine = new MRfQLine(rfq);
                RfqLine.SetLine(LineNo);
                RfqLine.SetM_RequisitionLine_ID(Util.GetValueOfInt(_ds.Tables[0].Rows[i]["M_RequisitionLine_ID"]));
                RfqLine.SetM_Product_ID(Util.GetValueOfInt(_ds.Tables[0].Rows[i]["M_Product_ID"]));
                if (Util.GetValueOfInt(_ds.Tables[0].Rows[i]["M_AttributeSetInstance_ID"]) > 0)
                {
                    RfqLine.SetM_AttributeSetInstance_ID(Util.GetValueOfInt(_ds.Tables[0].Rows[i]["M_AttributeSetInstance_ID"]));
                }
                RfqLine.SetDescription(Util.GetValueOfString(_ds.Tables[0].Rows[i]["Description"]));
                if (RfqLine.Save())
                {
                    //VIS0336:changes done for inserting the record in vendor recommend tab when process run.
                    if (Env.IsModuleInstalled("VA068_"))
                    {
                        MTable tbl = new MTable(GetCtx(), MTable.Get_Table_ID("VA068_VendorRecomend"), Get_Trx());
                        PO VendorRecommend = null;
                        if (dt != null && dt.Tables.Count > 0 && dt.Tables[0].Rows.Count > 0)
                        {
                            selectedTable = dt.Tables[0].Select(" M_RequisitionLine_ID=" + Util.GetValueOfInt(_ds.Tables[0].Rows[i]["M_requisitionLine_ID"]));
                            foreach (DataRow rows in selectedTable)
                            {
                                VendorRecommend = tbl.GetPO(GetCtx(), 0, Get_Trx());
                                VendorRecommend.Set_Value("C_RfQLine_ID", RfqLine.GetC_RfQLine_ID());
                                VendorRecommend.Set_Value("AD_Org_ID", Util.GetValueOfInt(rows["AD_Org_ID"]));
                                VendorRecommend.Set_Value("AD_Client_ID", Util.GetValueOfInt(rows["AD_Client_ID"]));
                                VendorRecommend.Set_Value("LineNo", Util.GetValueOfInt(rows["LineNo"]));
                                VendorRecommend.Set_Value("Name", Util.GetValueOfString(rows["Name"]));
                                VendorRecommend.Set_Value("Email", Util.GetValueOfString(rows["Email"]));
                                VendorRecommend.Set_Value("C_BPartner_Location_ID", Util.GetValueOfInt(rows["C_BPartner_Location_ID"]));
                                VendorRecommend.Set_Value("VA068_ContactName", Util.GetValueOfString(rows["VA068_ContactName"]));
                                VendorRecommend.Set_Value("VA068_Phone", Util.GetValueOfString(rows["VA068_Phone"]));
                                VendorRecommend.Set_Value("VA068_Email", Util.GetValueOfString(rows["VA068_Email"]));
                                VendorRecommend.Set_Value("VA068_Location_ID", Util.GetValueOfInt(rows["VA068_Location_ID"]));
                                VendorRecommend.Set_Value("VA068_Country_ID", Util.GetValueOfInt(rows["VA068_Country_ID"].ToString()));
                                VendorRecommend.Set_Value("VA068_Status", Util.GetValueOfString(rows["VA068_Status"].ToString()));
                                // VendorRecommend.Set_Value("IsApproved", Util.GetValueOfString(rows["IsApproved"].ToString()));
                                VendorRecommend.Set_Value("VA068_VendorType", Util.GetValueOfString(rows["VA068_VendorType"]));
                                VendorRecommend.Set_Value("C_BPartner_ID", Util.GetValueOfInt(rows["C_BPartner_ID"]));
                                VendorRecommend.Set_Value("VA068_VendorRegistration_ID", Util.GetValueOfInt(rows["VA068_VendorRegistration_ID"]));

                                if (!VendorRecommend.Save())
                                {
                                    ValueNamePair vp = VLogger.RetrieveError();
                                    if (vp != null)
                                    {
                                        Get_Trx().Rollback();
                                        return Msg.GetMsg(GetCtx(), "VA068_VendorRecomendNotSaved") + "- " + vp.Name;
                                    }
                                    else
                                    {
                                        Get_Trx().Rollback();
                                        return Msg.GetMsg(GetCtx(), "VA068_VendorRecomendNotSaved");
                                    }
                                }
                            }
                        }
                    }

                    // Create RfQ Qty
                    MRfQLineQty RfQLineQty = new MRfQLineQty(RfqLine);
                    RfQLineQty.SetC_UOM_ID(Util.GetValueOfInt(_ds.Tables[0].Rows[i]["C_UOM_ID"]));
                    RfQLineQty.SetQty(Util.GetValueOfDecimal(_ds.Tables[0].Rows[i]["Qty"]));
                    RfQLineQty.SetBenchmarkPrice(Util.GetValueOfDecimal(_ds.Tables[0].Rows[i]["PriceActual"]));
                    RfQLineQty.SetIsPurchaseQty(true);
                    RfQLineQty.SetIsRfQQty(true);
                    if (!RfQLineQty.Save())
                    {
                        ValueNamePair vp = VLogger.RetrieveError();
                        if (vp != null)
                        {
                            Get_TrxName().Rollback();
                            return Msg.GetMsg(GetCtx(), "RfQLineQtyNotSaved") + "- " + vp.Name;
                        }
                        else
                        {
                            Get_TrxName().Rollback();
                            return Msg.GetMsg(GetCtx(), "RfQLineQtyNotSaved");
                        }

                    }
                }
                else
                {
                    ValueNamePair vp = VLogger.RetrieveError();
                    if (vp != null)
                    {
                        Get_TrxName().Rollback();
                        return Msg.GetMsg(GetCtx(), "RfQLineNotSaved") + "- " + vp.Name;
                    }
                    else
                    {
                        Get_TrxName().Rollback();
                        return Msg.GetMsg(GetCtx(), "RfQLineNotSaved");
                    }
                }

            }

            return message;
        }


    }
}
