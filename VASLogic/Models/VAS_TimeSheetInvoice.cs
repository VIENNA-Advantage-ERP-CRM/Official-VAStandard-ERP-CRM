using CoreLibrary.DataBase;
using System;
using System.Collections.Generic;
using System.Data;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using VAdvantage.Model;
using VAdvantage.Utility;

namespace VASLogic.Models
{
    public class VAS_TimeSheetInvoice
    {
        public List<ListOfGridData> LoadGridData(Ctx ctx, int AD_Cleint_ID, int AD_Org_ID, int C_BPartner_ID, int S_Resource_ID, int TimExpenSeDoc, int C_Project_ID, int R_Request_ID, int C_Task_ID, string FromDate, string toDate)
        {
            List<ListOfGridData> obj = new List<ListOfGridData>();

            var sql = "SELECT sc.DocumentNo, TO_NUMBER(sc.C_BPartner_ID) AS ResourceID,cb.Name AS ResourceName, "
                        + " st.M_Product_ID,st.C_Charge_ID,"
                       + " CASE"
                       + " WHEN st.m_product_id IS NOT NULL THEN p.name"
                       + " ELSE c.name"
                       + " END AS item_name,"
                       + " st.R_Request_ID,st.C_Project_ID,cp.Name AS ProjectName,rq.DocumentNo as RequestName,st.C_ProjectPhase_ID,rq.Summary,cpp.SeqNo,"
                       + " st.Qty, st.C_BPartner_ID AS CustomerId,cb1.Name AS CustomerName,mp.C_Currency_ID,cy.ISO_Code as CurrencyName,st.InvoicePrice AS Price,sc.M_PriceList_ID,"
                       + " st.C_BPartner_Location_ID,cb1.VA009_PaymentMethod_ID,0 AS VA075_Task_ID,st.C_Uom_ID,um.Name as UomName, NULL AS ValidFrom,cbl.Name AS LocationName,"
                       + " sc.DateReport AS RecordingDate, NULL AS EstimatedTime,cb1.C_PaymentTerm_ID"
                       + " FROM S_TimeExpense sc"
                       + " INNER JOIN S_TimeExpenseLine st ON (st.S_TimeExpense_ID=sc.S_TimeExpense_ID)"
                       + " INNER JOIN C_BPartner cb ON (sc.C_BPartner_ID=cb.C_BPartner_ID AND cb.IsEmployee='Y')"
                       + " INNER JOIN C_BPartner cb1 ON (st.C_BPartner_ID=cb1.C_BPartner_ID AND cb1.IsCustomer='Y')"
                       + " LEFT JOIN C_BPartner_Location cbl ON (cbl.C_BPartner_ID = cb1.C_BPartner_ID AND st.C_BPartner_Location_ID=cbl.C_BPartner_Location_ID)"
                       + " LEFT JOIN C_Charge c ON (st.C_Charge_ID = c.C_Charge_ID)"
                       + " LEFT JOIN M_Product p ON (st.M_Product_ID = p.M_Product_ID)"
                       + " LEFT JOIN R_Request rq ON (st.R_Request_ID = rq.R_Request_ID)"
                       + " LEFT JOIN C_Project cp ON (st.C_Project_ID = cp.C_Project_ID)"
                       + " LEFT JOIN C_ProjectPhase cpp ON (cpp.C_Project_ID = cp.C_Project_ID AND st.C_ProjectPhase_ID=cpp.C_ProjectPhase_ID)"
                       + " INNER JOIN M_PriceList mp ON (mp.M_PriceList_ID = sc.M_PriceList_ID)"
                       + " INNER JOIN C_Currency cy ON (cy.C_Currency_ID = mp.C_Currency_ID)"
                       + " INNER JOIN C_Uom um ON (um.C_Uom_ID = st.C_Uom_ID)"
                       + " WHERE sc.AD_Client_ID=" + AD_Cleint_ID + " AND st.IsInvoiced='Y'";

            if (AD_Org_ID != 0)
            {
                sql += " AND sc.AD_Org_ID = " + AD_Org_ID + " ";

            }
            if (C_BPartner_ID != 0)
            {
                sql += " AND st.C_BPartner_ID = " + C_BPartner_ID + " ";
            }
            if (S_Resource_ID != 0)
            {
                sql += " AND sc.C_BPartner_ID = (SELECT ad.C_BPartner_ID FROM AD_User ad INNER JOIN S_Resource rs ON (rs.AD_User_ID=ad.AD_User_ID) WHERE rs.S_Resource_ID=" + S_Resource_ID + ")";
            }
            if (C_Project_ID != 0)
            {
                sql += " AND st.C_Project_ID = " + C_Project_ID;
            }
            if (TimExpenSeDoc != 0)
            {
                sql += " AND sc.S_TimeExpense_ID = " + TimExpenSeDoc;
            }
            if (R_Request_ID != 0)
            {
                sql += " AND st.R_Request_ID = " + R_Request_ID;
            }
            //if (FromDate != string.Empty && toDate != string.Empty)
            //{
            //    sql=" and t.VA009_FollowupDate BETWEEN  ";
            //    sql.Append(GlobalVariable.TO_DATE(dateFrom, true) + " AND ");
            //    sql.Append(GlobalVariable.TO_DATE(dateTo, true));
            //}

            if (Env.IsModuleInstalled("VA075_"))
            {
                sql += " UNION SELECT st.Name AS DocumentNo, TO_NUMBER(wo.S_Resource_ID) AS ResourceID,rs.Name AS ResourceName, "
                       + " wo.M_Product_ID,wo.C_Charge_ID,"
                       + " CASE"
                       + " WHEN wo.M_Product_ID IS NOT NULL THEN p.Name"
                       + " ELSE c.Name"
                       + " END AS item_name,"
                       + " wo.R_Request_ID,wo.C_Project_ID,cp.Name AS ProjectName,rq.DocumentNo as RequestName,wo.C_ProjectPhase_ID,rq.Summary,cpp.SeqNo,"
                       + " wo.VA075_TimeSpent, wo.C_BPartner_ID AS CustomerId,cb.Name AS CustomerName,mp.C_Currency_ID,cy.ISO_Code as CurrencyName,pp.PriceList AS Price,wo.M_PriceList_ID,"
                       + " wo.C_BPartner_Location_ID, cb.VA009_PaymentMethod_ID,wo.VA075_Task_ID,wo.C_Uom_ID,um.Name as UomName,mpv.ValidFrom,cbl.Name AS LocationName,"
                       + " wos.VA075_ActualEndTime AS RecordingDate, wo.VA075_TimeEstimate AS EstimatedTime,cb.C_PaymentTerm_ID"
                       + " FROM VA075_WorkOrderOperation wo"
                       + " INNER JOIN VA075_WOTaskSchedule wos ON (wos.VA075_WorkOrderOperation_ID=wo.VA075_WorkOrderOperation_ID)"
                       + " INNER JOIN VA075_Task st ON (st.VA075_Task_ID=wo.VA075_Task_ID)"
                       + " INNER JOIN S_Resource rs ON (rs.S_Resource_ID=wo.S_Resource_ID)"
                       + " INNER JOIN C_BPartner cb ON (wo.C_BPartner_ID=cb.C_BPartner_ID AND  cb.IsCustomer='Y')"
                       + " LEFT JOIN C_BPartner_Location cbl ON (cbl.C_BPartner_ID = cb.C_BPartner_ID AND wo.C_BPartner_Location_ID=cbl.C_BPartner_Location_ID)"
                       + " LEFT JOIN C_Charge c ON (wo.C_Charge_ID = c.C_Charge_ID)"
                       + " LEFT JOIN M_Product p ON (wo.M_Product_ID = p.M_Product_ID)"
                       + " LEFT JOIN R_Request rq ON (wo.R_Request_ID = rq.R_Request_ID)"
                       + " LEFT JOIN C_Project cp ON (wo.C_Project_ID = cp.C_Project_ID)"
                       + " LEFT JOIN C_ProjectPhase cpp ON (cpp.C_Project_ID = cp.C_Project_ID AND wo.C_ProjectPhase_ID=cpp.C_ProjectPhase_ID)"
                       + " INNER JOIN M_PriceList mp on (mp.M_PriceList_ID = wo.M_PriceList_ID)"
                       + " INNER JOIN M_PriceList_Version mpv on (mpv.M_PriceList_ID = mp.M_PriceList_ID)"
                       + " INNER JOIN m_productprice pp ON (pp.M_Product_ID = p.M_Product_ID AND  pp.M_PriceList_Version_ID = mpv.M_PriceList_Version_ID)"
                       + " INNER JOIN C_Currency cy ON (cy.C_Currency_ID = mp.C_Currency_ID)"
                        + " INNER JOIN C_Uom um ON (um.C_Uom_ID = wo.C_Uom_ID)"
                       + " WHERE wo.AD_Client_ID=" + AD_Cleint_ID + " AND mpv.ValidFrom=(SELECT MAX(ValidFrom) AS max_validfrom FROM M_PriceList_Version where M_PriceList_ID=mpv.M_PriceList_ID AND ValidFrom<=wos.va075_ActualEndTime) AND wo.VA075_IsTaskPerformed='Y'";


                if (AD_Org_ID != 0)
                {
                    sql += " AND wo.AD_Org_ID = " + AD_Org_ID + " ";

                }
                if (C_BPartner_ID != 0)
                {
                    sql += " AND wo.C_BPartner_ID = " + C_BPartner_ID + " ";
                }
                if (S_Resource_ID != 0)
                {
                    sql += " AND wo.S_Resource_ID = " + S_Resource_ID;
                }
                if (C_Project_ID != 0)
                {
                    sql += " AND wo.C_Project_ID = " + C_Project_ID;
                }
                if (C_Task_ID != 0)
                {
                    sql += " AND wo.VA075_Task_ID = " + C_Task_ID;
                }
                if (R_Request_ID != 0)
                {
                    sql += " AND wo.R_Request_ID = " + R_Request_ID;
                }

            }


            DataSet ds = DB.ExecuteDataset(sql);
            if (ds != null && ds.Tables[0].Rows.Count > 0)
            {
                for (int i = 0; i < ds.Tables[0].Rows.Count; i++)
                {
                    ListOfGridData objc = new ListOfGridData();

                    objc.DocumentNo = Util.GetValueOfString(ds.Tables[0].Rows[i]["DocumentNo"]);
                    objc.ResourceID = Util.GetValueOfInt(ds.Tables[0].Rows[i]["ResourceID"]);
                    objc.ResourceName = Util.GetValueOfString(ds.Tables[0].Rows[i]["ResourceName"]);
                    if (Util.GetValueOfInt(ds.Tables[0].Rows[i]["M_Product_ID"]) != 0)
                    {
                        objc.M_Product_ID = Util.GetValueOfInt(ds.Tables[0].Rows[i]["M_Product_ID"]);
                    }
                    if (Util.GetValueOfInt(ds.Tables[0].Rows[i]["C_Charge_ID"]) != 0)
                    {
                        objc.C_Charge_ID = Util.GetValueOfInt(ds.Tables[0].Rows[i]["C_Charge_ID"]);
                    }
                    objc.ProductName = Util.GetValueOfString(ds.Tables[0].Rows[i]["item_name"]);
                    objc.Qty = Util.GetValueOfDecimal(ds.Tables[0].Rows[i]["Qty"]);
                    objc.CustomerId = Util.GetValueOfInt(ds.Tables[0].Rows[i]["CustomerId"]);
                    objc.CustomerName = Util.GetValueOfString(ds.Tables[0].Rows[i]["CustomerName"]);
                    objc.C_Currency_ID = Util.GetValueOfInt(ds.Tables[0].Rows[i]["C_Currency_ID"]);
                    objc.CurrencyName = Util.GetValueOfString(ds.Tables[0].Rows[i]["CurrencyName"]);
                    objc.PriceList = Util.GetValueOfDecimal(ds.Tables[0].Rows[i]["Price"]);
                    objc.M_PriceList_ID = Util.GetValueOfInt(ds.Tables[0].Rows[i]["M_PriceList_ID"]);
                    objc.VA009_PaymentMethod_ID = Util.GetValueOfInt(ds.Tables[0].Rows[i]["VA009_PaymentMethod_ID"]);
                    objc.C_Location_ID = Util.GetValueOfInt(ds.Tables[0].Rows[i]["C_BPartner_Location_ID"]);
                    objc.C_Uom_ID = Util.GetValueOfInt(ds.Tables[0].Rows[i]["C_Uom_ID"]);
                    objc.UomName = Util.GetValueOfString(ds.Tables[0].Rows[i]["UomName"]);
                    objc.LocationName = Util.GetValueOfString(ds.Tables[0].Rows[i]["LocationName"]);
                    objc.C_PaymentTerm_ID = Util.GetValueOfInt(ds.Tables[0].Rows[i]["C_PaymentTerm_ID"]);
                    if (Util.GetValueOfInt(ds.Tables[0].Rows[i]["C_Project_ID"]) != 0)
                    {
                        objc.ProjReq_ID = Util.GetValueOfInt(ds.Tables[0].Rows[i]["C_Project_ID"]);
                        objc.ProjReq_Name = Util.GetValueOfString(ds.Tables[0].Rows[i]["ProjectName"]);
                        string sqlPhase = @"SELECT Name FROM C_ProjectPhase WHERE SeqNo <= " + Util.GetValueOfDecimal(ds.Tables[0].Rows[i]["SeqNo"]) +
                                        " AND C_Project_ID = " + Util.GetValueOfInt(ds.Tables[0].Rows[i]["C_Project_ID"]) + " ORDER BY C_ProjectPhase_ID";
                        DataSet dsPhase = DB.ExecuteDataset(sqlPhase);
                        objc.PhaseInfo = new List<ProjectPhaseInfo>();
                        for (int j = 0; j < dsPhase.Tables[0].Rows.Count; j++)
                        {
                            ProjectPhaseInfo phaseInfo = new ProjectPhaseInfo();
                            phaseInfo.PhaseName = Util.GetValueOfString(dsPhase.Tables[0].Rows[j]["Name"]);
                            objc.PhaseInfo.Add(phaseInfo);
                        }

                    }
                    else if (Util.GetValueOfInt(ds.Tables[0].Rows[i]["R_Request_ID"]) != 0)
                    {
                        objc.ProjReq_ID = Util.GetValueOfInt(ds.Tables[0].Rows[i]["R_Request_ID"]);
                        objc.ProjReq_Name = Util.GetValueOfString(ds.Tables[0].Rows[i]["RequestName"]);
                        objc.RequestSummary = Util.GetValueOfString(ds.Tables[0].Rows[i]["Summary"]);
                    }

                    objc.RecordedDate = Util.GetValueOfDateTime(ds.Tables[0].Rows[i]["RecordingDate"]);
                    objc.EstimatedTime = Util.GetValueOfString(ds.Tables[0].Rows[i]["EstimatedTime"]);
                    obj.Add(objc);

                }
            }
            return obj;
        }
        public string GenerateInvoice(Ctx ct, IEnumerable<dynamic> DataTobeInvoice, int AD_Cleint_ID, int AD_Org_ID)
        {
            MInvoice inv = null;
            var sortedData = DataTobeInvoice.OrderBy(d => d.C_BPartner_ID)
                             .ThenBy(d => d.C_BPartner_Location_ID)
                             .ThenBy(d => d.M_PriceList_ID)
                             .ThenBy(d => d.VA009_PaymentMethod_ID).ToList();
            for (int i = 0; i < sortedData.Count; i++)
            {

                if (i == 0)
                {
                    inv = new MInvoice(ct, 0, null);
                    inv.SetAD_Client_ID(AD_Cleint_ID);
                    inv.SetAD_Org_ID(AD_Org_ID);
                    inv.SetC_BPartner_ID(Util.GetValueOfInt(sortedData[i].C_BPartner_ID));
                    inv.SetC_BPartner_Location_ID(Util.GetValueOfInt(sortedData[i].C_BPartner_Location_ID));
                    inv.SetC_Currency_ID(Util.GetValueOfInt(sortedData[i].C_Currency_ID));
                    inv.SetC_PaymentTerm_ID(Util.GetValueOfInt(sortedData[i].C_PaymentTerm_ID)); ;
                    inv.SetM_PriceList_ID(Util.GetValueOfInt(sortedData[i].M_PriceList_ID));
                    inv.SetVA009_PaymentMethod_ID(Util.GetValueOfInt(sortedData[i].VA009_PaymentMethod_ID));
                    inv.SetC_DocTypeTarget_ID(116);
                    if (inv.Save())
                    {
                        CreateLine(ct, inv, sortedData[i]);

                    }
                }
                else if (inv.GetC_BPartner_ID() == Util.GetValueOfInt(sortedData[i].C_BPartner_ID) && inv.GetC_BPartner_Location_ID() == Util.GetValueOfInt(sortedData[i].C_BPartner_Location_ID)
                    && inv.GetM_PriceList_ID() == Util.GetValueOfInt(sortedData[i].M_PriceList_ID) && inv.GetVA009_PaymentMethod_ID() == Util.GetValueOfInt(sortedData[i].VA009_PaymentMethod_ID))
                {
                    CreateLine(ct, inv, sortedData[i]);

                }
                else
                {
                    inv = new MInvoice(ct, 0, null);
                    inv.SetAD_Client_ID(AD_Cleint_ID);
                    inv.SetAD_Org_ID(AD_Org_ID);
                    inv.SetC_BPartner_ID(Util.GetValueOfInt(sortedData[i].C_BPartner_ID));
                    inv.SetC_BPartner_Location_ID(Util.GetValueOfInt(sortedData[i].C_BPartner_Location_ID));
                    inv.SetC_Currency_ID(Util.GetValueOfInt(sortedData[i].C_Currency_ID));
                    inv.SetC_PaymentTerm_ID(Util.GetValueOfInt(sortedData[i].C_PaymentTerm_ID)); ;
                    inv.SetM_PriceList_ID(Util.GetValueOfInt(sortedData[i].M_PriceList_ID));
                    inv.SetVA009_PaymentMethod_ID(Util.GetValueOfInt(sortedData[i].VA009_PaymentMethod_ID));
                    inv.SetC_DocTypeTarget_ID(116);
                    if (inv.Save())
                    {
                        CreateLine(ct, inv, sortedData[i]);

                    }
                }

            }
            return inv.GetDocumentNo();
        }

        public string CreateLine(Ctx ctx, MInvoice inv, dynamic sortedData)
        {
            MInvoiceLine invLine = new MInvoiceLine(inv);
            if (sortedData.M_Product_ID > 0)
                invLine.SetM_Product_ID(Util.GetValueOfInt(sortedData.M_Product_ID));
            else if (sortedData.C_Charge_ID > 0)
                invLine.SetC_Charge_ID(Util.GetValueOfInt(sortedData.C_Charge_ID));

            invLine.SetC_UOM_ID(Util.GetValueOfInt(sortedData.C_Charge_ID));
            invLine.SetQtyEntered(Util.GetValueOfDecimal(sortedData.Qty));
            invLine.SetPrice(Util.GetValueOfDecimal(sortedData.Price));
            invLine.SetPriceList(Util.GetValueOfDecimal(sortedData.Price));
            if (invLine.Save())
            {
                return "";
            }
            return "";
        }
        public class ListOfGridData
        {
            public string DocumentNo { get; set; }
            public int ResourceID { get; set; }
            public string ResourceName { get; set; }
            public int M_Product_ID { get; set; }
            public int C_Charge_ID { get; set; }
            public string ProductName { get; set; }
            public string ChargeName { get; set; }
            public decimal Qty { get; set; }
            public int CustomerId { get; set; }
            public string CustomerName { get; set; }
            public string CurrencyName { get; set; }
            public int C_Currency_ID { get; set; }
            public decimal PriceList { get; set; }
            public int M_PriceList_ID { get; set; }
            public int VA009_PaymentMethod_ID { get; set; }
            public int C_Location_ID { get; set; }
            public int C_Uom_ID { get; set; }
            public string UomName { get; set; }
            public string LocationName { get; set; }

            public DateTime? RecordedDate { get; set; }

            public string EstimatedTime { get; set; }

            public int ProjReq_ID { get; set; }

            public string ProjReq_Name { get; set; }
            public string RequestSummary { get; set; }
            public int C_PaymentTerm_ID { get; set; }
            public List<ProjectPhaseInfo> PhaseInfo { get; set; }

        }
        public class ProjectPhaseInfo
        {
            public string PhaseName { get; set; }
        }
    }
}
