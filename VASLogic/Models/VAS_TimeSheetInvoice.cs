using System;
using System.Collections.Generic;
using System.Data;
using System.Linq;
using System.Text;
using VAdvantage.DataBase;
using VAdvantage.Logging;
using VAdvantage.Model;
using VAdvantage.Utility;

namespace VASLogic.Models
{
    public class VAS_TimeSheetInvoice
    {
        StringBuilder errorMessage = new StringBuilder();
        StringBuilder docno = new StringBuilder();
        private int C_ConverType_ID = 0;
        private int C_DocType_ID = 0;
        StringBuilder sql = new StringBuilder();

        /// <summary>
        ///This Function returns the  Data for Time Recording in order to create AR Invoice 
        /// </summary>
        /// <param name="ctx">context</param>
        /// <param name="AD_Client_ID">AD_Client_ID</param>
        /// <param name="AD_Org_ID">AD_Org_ID</param>
        /// <param name="C_BPartner_ID">C_BPartner_ID</param>
        /// <param name="C_Project_ID">C_Project_ID</param>
        /// <param name="C_Task_ID">C_Task_ID</param>
        /// <param name="FromDate">FromDate</param>
        /// <param name="pageNo">pageNo</param>
        /// <param name="pageSize">pageSize</param>
        /// <param name="R_Request_ID">R_Request_ID</param>
        /// <param name="S_Resource_ID">S_Resource_ID</param>
        /// <param name="TimExpenSeDoc">TimExpenSeDoc</param>
        /// <param name="toDate">toDate</param>
        /// <param name="TaskType">TaskType</param>
        /// <returns>List of data For time recording of task</returns>
        /// <author>VIS_427</author>
        public List<TimeRecordingData> LoadGridData(Ctx ctx, int AD_Client_ID, int AD_Org_ID, string C_BPartner_ID, string S_Resource_ID, string TimExpenSeDoc, string C_Project_ID,
            string R_Request_ID, string C_Task_ID, string FromDate, string toDate, string TaskType, int pageNo, int pageSize)
        {
            DataRow[] drPhaseData = null;
            int countRecords = 0;
            sql.Clear();
            List<TimeRecordingData> timeRecordingDataList = new List<TimeRecordingData>();
            // when Time Expense or Task record is not selected than make value as 0
            if (TimExpenSeDoc.Length == 0 && C_Task_ID.Length > 0)
            {
                TimExpenSeDoc = "0";
            }
            else if (C_Task_ID.Length == 0 && TimExpenSeDoc.Length > 0)
            {
                C_Task_ID = "0";
            }

            sql.Append("SELECT t.*,loc.Name AS LocationName FROM (");
            sql.Append(@"SELECT sc.DocumentNo, sc.C_BPartner_ID AS ResourceID,emp.Name AS ResourceName, 
                        st.M_Product_ID,st.C_Charge_ID,
                        CASE
                        WHEN st.M_Product_ID IS NOT NULL THEN p.Name
                        ELSE c.Name
                        END AS item_name,
                        st.R_Request_ID,st.C_Project_ID,cp.Name AS ProjectName,rq.DocumentNo as RequestName,st.C_ProjectPhase_ID,
                         rq.Summary,cpp.SeqNo,
                        st.Qty, st.C_BPartner_ID AS CustomerId,cust.Name AS CustomerName,st.C_Currency_ID,cy.ISO_Code as CurrencyName,
                        st.ConvertedAmt AS Price,sc.M_PriceList_ID,
                        CASE WHEN  st.C_BPartner_Location_ID IS NOT NULL then st.C_BPartner_Location_ID ELSE 
                        (First_VALUE(cbl.C_BPartner_Location_ID) OVER (PARTITION BY st.C_BPartner_ID
                        ORDER BY cbl.IsRemitTo DESC, cbl.C_BPartner_Location_ID DESC)) END AS C_BPartner_Location_ID,cust.VA009_PaymentMethod_ID,0 AS VA075_Task_ID,st.C_Uom_ID,um.Name as UomName, 
                        NULL AS ValidFrom,
                        sc.DateReport AS RecordingDate, NULL AS EstimatedTime,cust.C_PaymentTerm_ID,0 as PriceList,
                        0 as PriceLimit,cy.StdPrecision,mp.EnforcePriceLimit,st.S_TimeExpenseLine_ID,0 As VA075_WorkOrderOperation_ID,cust.Pic,
                        img.ImageExtension,p.AD_Image_ID
                        FROM S_TimeExpense sc
                        INNER JOIN S_TimeExpenseLine st ON (st.S_TimeExpense_ID=sc.S_TimeExpense_ID)
                        INNER JOIN C_BPartner emp ON (sc.C_BPartner_ID=emp.C_BPartner_ID AND emp.IsEmployee='Y')
                        INNER JOIN C_BPartner cust ON (st.C_BPartner_ID=cust.C_BPartner_ID AND cust.IsCustomer='Y')
                        LEFT OUTER JOIN C_BPartner_Location cbl ON (cbl.C_BPartner_ID = cust.C_BPartner_ID)
                        LEFT OUTER JOIN C_Charge c ON (st.C_Charge_ID = c.C_Charge_ID)
                        LEFT OUTER JOIN M_Product p ON (st.M_Product_ID = p.M_Product_ID)
                        LEFT OUTER JOIN AD_Image img ON (img.AD_Image_ID = p.AD_Image_ID)
                        LEFT OUTER JOIN R_Request rq ON (st.R_Request_ID = rq.R_Request_ID)
                        LEFT OUTER JOIN C_Project cp ON (st.C_Project_ID = cp.C_Project_ID)
                        LEFT OUTER JOIN C_ProjectPhase cpp ON (cpp.C_Project_ID = cp.C_Project_ID AND st.C_ProjectPhase_ID=cpp.C_ProjectPhase_ID)
                        INNER JOIN M_PriceList mp ON (mp.M_PriceList_ID = sc.M_PriceList_ID)
                        INNER JOIN C_Currency cy ON (cy.C_Currency_ID = st.C_Currency_ID)
                        INNER JOIN C_Uom um ON (um.C_Uom_ID = st.C_Uom_ID)
                        WHERE st.IsInvoiced='Y' AND st.C_Invoice_ID IS NULL AND sc.DocStatus IN ('CO','CL')
                        AND sc.AD_Client_ID = " + AD_Client_ID + " ");

            if (AD_Org_ID != 0)
            {
                sql.Append(" AND sc.AD_Org_ID = " + AD_Org_ID + " ");

            }
            if (C_BPartner_ID.Length > 0)
            {
                sql.Append(" AND st.C_BPartner_ID IN (" + C_BPartner_ID + ") ");
            }
            if (S_Resource_ID.Length > 0)
            {
                sql.Append(" AND sc.C_BPartner_ID IN (SELECT ad.C_BPartner_ID FROM AD_User ad INNER JOIN S_Resource rs ON (rs.AD_User_ID=ad.AD_User_ID) " +
                    "WHERE rs.S_Resource_ID IN (" + S_Resource_ID + "))");
            }
            if (C_Project_ID.Length > 0)
            {
                sql.Append(" AND st.C_Project_ID IN (" + C_Project_ID + ") ");
            }
            if (TimExpenSeDoc.Length > 0)
            {
                sql.Append(" AND sc.S_TimeExpense_ID IN (" + TimExpenSeDoc + ") ");
            }
            if (R_Request_ID.Length > 0)
            {
                sql.Append(" AND st.R_Request_ID IN (" + R_Request_ID + ") ");
            }
            if (FromDate != string.Empty && toDate != string.Empty)
            {
                sql.Append(@" AND TRUNC(sc.DateReport) BETWEEN " +
                (GlobalVariable.TO_DATE(Util.GetValueOfDateTime(FromDate), true)));
                sql.Append(@" AND " +
                (GlobalVariable.TO_DATE(Util.GetValueOfDateTime(toDate), true)));
            }

            if (Env.IsModuleInstalled("VA075_"))
            {
                sql.Append(@" UNION (SELECT st.Name AS DocumentNo, TO_NUMBER(wo.S_Resource_ID) AS ResourceID,rs.Name AS ResourceName, 
                        NULL AS M_Product_ID,
                        wo.C_Charge_ID,
                        c.Name AS item_name,
                        wo.R_Request_ID,wo.C_Project_ID,cp.Name AS ProjectName,rq.DocumentNo as RequestName,wo.C_ProjectPhase_ID,rq.Summary,cpp.SeqNo,
                        wo.VA075_TimeSpent, wo.C_BPartner_ID AS CustomerId,cust.Name AS CustomerName,wo.C_Currency_ID,cy.ISO_Code as CurrencyName,
                        c.ChargeAmt AS Price,wo.M_PriceList_ID,
                        CASE WHEN  wo.C_BPartner_Location_ID IS NOT NULL THEN wo.C_BPartner_Location_ID ELSE 
                        (First_VALUE(cbl.C_BPartner_Location_ID) OVER (PARTITION BY wo.C_BPartner_ID
                        ORDER BY cbl.IsRemitTo DESC, cbl.C_BPartner_Location_ID DESC)) END AS C_BPartner_Location_ID, cust.VA009_PaymentMethod_ID,wo.VA075_Task_ID,wo.C_Uom_ID,
                        um.Name as UomName,NULL AS ValidFrom,
                        wo.VA075_TASKENDDATE AS RecordingDate, wo.VA075_TimeEstimate AS EstimatedTime,
                        cust.C_PaymentTerm_ID,0 AS PriceList,0 AS PriceLimit,cy.StdPrecision,'N' AS EnforcePriceLimit,Null AS S_TimeExpenseLine_ID,wo.VA075_WorkOrderOperation_ID,cust.Pic,
                        NULL AS ImageExtension,NULL AS AD_Image_ID
                        FROM VA075_WorkOrderOperation wo
                        INNER JOIN VA075_Task st ON (st.VA075_Task_ID=wo.VA075_Task_ID)
                        INNER JOIN S_Resource rs ON (rs.S_Resource_ID=wo.S_Resource_ID)
                        INNER JOIN C_BPartner cust ON (wo.C_BPartner_ID=cust.C_BPartner_ID AND  cust.IsCustomer='Y')
                        LEFT OUTER JOIN C_BPartner_Location cbl ON (cbl.C_BPartner_ID = cust.C_BPartner_ID)
                        INNER JOIN C_Charge c ON (wo.C_Charge_ID = c.C_Charge_ID)
                        LEFT OUTER JOIN R_Request rq ON (wo.R_Request_ID = rq.R_Request_ID)
                        LEFT OUTER JOIN C_Project cp ON (wo.C_Project_ID = cp.C_Project_ID)
                        LEFT OUTER JOIN C_ProjectPhase cpp ON (cpp.C_Project_ID = cp.C_Project_ID AND wo.C_ProjectPhase_ID=cpp.C_ProjectPhase_ID)
                        INNER JOIN M_PriceList mp on (mp.M_PriceList_ID = wo.M_PriceList_ID)
                        INNER JOIN C_Currency cy ON (cy.C_Currency_ID = wo.C_Currency_ID)
                        INNER JOIN C_Uom um ON (um.C_Uom_ID = wo.C_Uom_ID)
                        WHERE wo.C_Invoice_ID IS NULL AND wo.AD_Client_ID=" + AD_Client_ID + " AND " +
                        "wo.VA075_IsTaskPerformed='Y' ");
                if (AD_Org_ID != 0)
                {
                    sql.Append(" AND wo.AD_Org_ID = " + AD_Org_ID + " ");

                }
                if (C_BPartner_ID.Length > 0)
                {
                    sql.Append(" AND wo.C_BPartner_ID IN (" + C_BPartner_ID + ")");
                }
                if (S_Resource_ID.Length > 0)
                {
                    sql.Append(" AND wo.S_Resource_ID IN (" + S_Resource_ID + ") ");
                }
                if (C_Project_ID.Length > 0)
                {
                    sql.Append(" AND wo.C_Project_ID IN (" + C_Project_ID + ") ");
                }
                if (C_Task_ID.Length > 0)
                {
                    sql.Append(" AND wo.VA075_Task_ID IN (" + C_Task_ID + ") ");
                }
                if (R_Request_ID.Length > 0)
                {
                    sql.Append(" AND wo.R_Request_ID IN (" + R_Request_ID + ") ");
                }
                /*Here If tasktype is billable then records shown will be billabe else it will show non billable records
                if task type is null then it will return all records*/
                if (TaskType.Length > 0)
                {
                    sql.Append(TaskType == "BL" ? " AND st.VA075_IsBillable='Y' " : " AND st.VA075_IsBillable='N' ");
                }
                if (FromDate != string.Empty && toDate != string.Empty)
                {
                    sql.Append(@" AND TRUNC(wo.VA075_TaskEndDate) BETWEEN " +
                    (GlobalVariable.TO_DATE(Util.GetValueOfDateTime(FromDate), true)));
                    sql.Append(@"AND " +
                    (GlobalVariable.TO_DATE(Util.GetValueOfDateTime(toDate), true)));
                }
                sql.Append(@"UNION SELECT st.Name AS DocumentNo, TO_NUMBER(wo.S_Resource_ID) AS ResourceID, rs.Name AS ResourceName, 
                        wo.M_Product_ID,
                        NULL AS C_Charge_ID,
                        p.Name AS item_name,
                        wo.R_Request_ID,wo.C_Project_ID,cp.Name AS ProjectName,rq.DocumentNo as RequestName,wo.C_ProjectPhase_ID,rq.Summary,cpp.SeqNo,
                        wo.VA075_TimeSpent, wo.C_BPartner_ID AS CustomerId,cb.Name AS CustomerName,wo.C_Currency_ID,cy.ISO_Code as CurrencyName,
                        pp.PriceStd AS Price,wo.M_PriceList_ID,
                        CASE WHEN  wo.C_BPartner_Location_ID IS NOT NULL THEN wo.C_BPartner_Location_ID ELSE 
                        (First_VALUE(cbl.C_BPartner_Location_ID) OVER (PARTITION BY wo.C_BPartner_ID
                        ORDER BY cbl.IsRemitTo DESC, cbl.C_BPartner_Location_ID DESC)) END AS C_BPartner_Location_ID, cb.VA009_PaymentMethod_ID,wo.VA075_Task_ID,wo.C_Uom_ID,
                        um.Name as UomName,mpv.ValidFrom,
                        wo.VA075_TASKENDDATE AS RecordingDate, wo.VA075_TimeEstimate AS EstimatedTime,
                        cb.C_PaymentTerm_ID,pp.PriceList,pp.PriceLimit,cy.StdPrecision,mp.EnforcePriceLimit,Null AS S_TimeExpenseLine_ID,wo.VA075_WorkOrderOperation_ID,cb.Pic,
                        img.ImageExtension,p.AD_Image_ID
                        FROM VA075_WorkOrderOperation wo
                        INNER JOIN VA075_Task st ON (st.VA075_Task_ID = wo.VA075_Task_ID)
                        INNER JOIN S_Resource rs ON (rs.S_Resource_ID = wo.S_Resource_ID)
                        INNER JOIN C_BPartner cb ON (wo.C_BPartner_ID = cb.C_BPartner_ID AND  cb.IsCustomer = 'Y')
                        LEFT OUTER JOIN C_BPartner_Location cbl ON(cbl.C_BPartner_ID = cb.C_BPartner_ID)
                        INNER JOIN M_Product p ON (wo.M_Product_ID = p.M_Product_ID)
                        LEFT OUTER JOIN AD_Image img ON (img.AD_Image_ID = p.AD_Image_ID)
                        LEFT OUTER JOIN R_Request rq ON (wo.R_Request_ID = rq.R_Request_ID)
                        LEFT OUTER JOIN C_Project cp ON (wo.C_Project_ID = cp.C_Project_ID)
                        LEFT OUTER JOIN C_ProjectPhase cpp ON (cpp.C_Project_ID = cp.C_Project_ID AND wo.C_ProjectPhase_ID= cpp.C_ProjectPhase_ID)
                        INNER JOIN M_PriceList mp ON (mp.M_PriceList_ID = wo.M_PriceList_ID)
                        INNER JOIN M_PriceList_Version mpv ON (mpv.M_PriceList_ID = mp.M_PriceList_ID)
                        LEFT OUTER JOIN M_ProductPrice pp ON ( pp.M_PriceList_Version_ID = mpv.M_PriceList_Version_ID AND pp.M_Product_ID=wo.M_Product_ID)
                        INNER JOIN C_Currency cy ON (cy.C_Currency_ID = wo.C_Currency_ID)
                        INNER JOIN C_Uom um ON (um.C_Uom_ID = wo.C_Uom_ID)
                        WHERE wo.C_Invoice_ID IS NULL AND wo.AD_Client_ID = " + AD_Client_ID + " AND " +
                            "mpv.ValidFrom=(SELECT MAX(ValidFrom) AS max_validfrom FROM M_PriceList_Version WHERE M_PriceList_ID=wo.M_PriceList_ID) AND wo.VA075_IsTaskPerformed='Y'");


                if (AD_Org_ID != 0)
                {
                    sql.Append(" AND wo.AD_Org_ID = " + AD_Org_ID + " ");

                }
                if (C_BPartner_ID.Length > 0)
                {
                    sql.Append(" AND wo.C_BPartner_ID IN (" + C_BPartner_ID + ")");
                }
                if (S_Resource_ID.Length > 0)
                {
                    sql.Append(" AND wo.S_Resource_ID IN (" + S_Resource_ID + ") ");
                }
                if (C_Project_ID.Length > 0)
                {
                    sql.Append(" AND wo.C_Project_ID IN (" + C_Project_ID + ") ");
                }
                if (C_Task_ID.Length > 0)
                {
                    sql.Append(" AND wo.VA075_Task_ID IN (" + C_Task_ID + ") ");
                }
                if (R_Request_ID.Length > 0)
                {
                    sql.Append(" AND wo.R_Request_ID IN (" + R_Request_ID + ") ");
                }
                /*Here If tasktype is billable then records shown will be billabe else it will show non billable records
                if task type is null then it will return all records*/
                if (TaskType.Length > 0)
                {
                    sql.Append(TaskType == "BL" ? " AND st.VA075_IsBillable='Y' " : " AND st.VA075_IsBillable='N' ");
                }
                if (FromDate != string.Empty && toDate != string.Empty)
                {
                    sql.Append(@" AND TRUNC(wo.VA075_TaskEndDate) BETWEEN " +
                    (GlobalVariable.TO_DATE(Util.GetValueOfDateTime(FromDate), true)));
                    sql.Append(@"AND " +
                    (GlobalVariable.TO_DATE(Util.GetValueOfDateTime(toDate), true)));
                }
                sql.Append(" ))t INNER JOIN C_BPartner_Location loc on (loc.C_BPartner_Location_ID=t.C_BPartner_Location_ID)" +
                    " ORDER BY t.DocumentNo DESC,t.CustomerName DESC,t.ResourceName DESC,t.Item_Name DESC");
            }

            TimeRecordingData timeRecordingDataObj = null;
            DataSet ds = DB.ExecuteDataset(sql.ToString(), null, null, pageSize, pageNo);
            if (ds != null && ds.Tables[0].Rows.Count > 0)
            {
                if (pageNo == 1)
                    countRecords = Util.GetValueOfInt(DB.ExecuteScalar("SELECT COUNT(*) FROM ( " + sql + " ) t"));
                DataSet dsPhase = GetPhaseData(ds);
                for (int i = 0; i < ds.Tables[0].Rows.Count; i++)
                {
                    timeRecordingDataObj = new TimeRecordingData();

                    timeRecordingDataObj.DocumentNo = Util.GetValueOfString(ds.Tables[0].Rows[i]["DocumentNo"]);
                    timeRecordingDataObj.ResourceID = Util.GetValueOfInt(ds.Tables[0].Rows[i]["ResourceID"]);
                    timeRecordingDataObj.ResourceName = Util.GetValueOfString(ds.Tables[0].Rows[i]["ResourceName"]);
                    if (Util.GetValueOfInt(ds.Tables[0].Rows[i]["M_Product_ID"]) != 0)
                    {
                        timeRecordingDataObj.M_Product_ID = Util.GetValueOfInt(ds.Tables[0].Rows[i]["M_Product_ID"]);
                    }
                    if (Util.GetValueOfInt(ds.Tables[0].Rows[i]["C_Charge_ID"]) != 0)
                    {
                        timeRecordingDataObj.C_Charge_ID = Util.GetValueOfInt(ds.Tables[0].Rows[i]["C_Charge_ID"]);
                    }
                    timeRecordingDataObj.ProductName = Util.GetValueOfString(ds.Tables[0].Rows[i]["item_name"]);
                    timeRecordingDataObj.Qty = Util.GetValueOfDecimal(ds.Tables[0].Rows[i]["Qty"]);
                    timeRecordingDataObj.CustomerId = Util.GetValueOfInt(ds.Tables[0].Rows[i]["CustomerId"]);
                    timeRecordingDataObj.CustomerName = Util.GetValueOfString(ds.Tables[0].Rows[i]["CustomerName"]);
                    timeRecordingDataObj.C_Currency_ID = Util.GetValueOfInt(ds.Tables[0].Rows[i]["C_Currency_ID"]);
                    timeRecordingDataObj.CurrencyName = Util.GetValueOfString(ds.Tables[0].Rows[i]["CurrencyName"]);
                    timeRecordingDataObj.PriceList = Util.GetValueOfDecimal(ds.Tables[0].Rows[i]["Price"]);
                    timeRecordingDataObj.M_PriceList_ID = Util.GetValueOfInt(ds.Tables[0].Rows[i]["M_PriceList_ID"]);
                    timeRecordingDataObj.VA009_PaymentMethod_ID = Util.GetValueOfInt(ds.Tables[0].Rows[i]["VA009_PaymentMethod_ID"]);
                    timeRecordingDataObj.C_Location_ID = Util.GetValueOfInt(ds.Tables[0].Rows[i]["C_BPartner_Location_ID"]);
                    timeRecordingDataObj.C_Uom_ID = Util.GetValueOfInt(ds.Tables[0].Rows[i]["C_Uom_ID"]);
                    timeRecordingDataObj.ISO_Code = Util.GetValueOfString(ds.Tables[0].Rows[i]["CurrencyName"]);
                    timeRecordingDataObj.UomName = Util.GetValueOfString(ds.Tables[0].Rows[i]["UomName"]);
                    timeRecordingDataObj.LocationName = Util.GetValueOfString(ds.Tables[0].Rows[i]["LocationName"]);
                    timeRecordingDataObj.C_PaymentTerm_ID = Util.GetValueOfInt(ds.Tables[0].Rows[i]["C_PaymentTerm_ID"]);
                    timeRecordingDataObj.PriceLimit = Util.GetValueOfDecimal(ds.Tables[0].Rows[i]["PriceLimit"]);
                    timeRecordingDataObj.PriceStd = Util.GetValueOfDecimal(ds.Tables[0].Rows[i]["PriceList"]);
                    timeRecordingDataObj.stdPrecision = Util.GetValueOfInt(ds.Tables[0].Rows[i]["StdPrecision"]);
                    timeRecordingDataObj.EnforcePriceLimit = Util.GetValueOfString(ds.Tables[0].Rows[i]["EnforcePriceLimit"]);
                    if (Util.GetValueOfInt(ds.Tables[0].Rows[i]["C_Project_ID"]) != 0)
                    {
                        timeRecordingDataObj.ProjReq_ID = Util.GetValueOfInt(ds.Tables[0].Rows[i]["C_Project_ID"]);
                        timeRecordingDataObj.ProjReq_Name = Util.GetValueOfString(ds.Tables[0].Rows[i]["ProjectName"]);
                        timeRecordingDataObj.PhaseInfo = new List<ProjectPhaseInfo>();
                        drPhaseData = dsPhase.Tables[0].Select($@"C_Project_ID = {Util.GetValueOfInt(ds.Tables[0].Rows[i]["C_Project_ID"])} AND SeqNo <= {Util.GetValueOfInt(ds.Tables[0].Rows[i]["SeqNo"])}");
                        foreach (DataRow dr in drPhaseData)
                        {
                            ProjectPhaseInfo phaseInfo = new ProjectPhaseInfo();
                            phaseInfo.PhaseName = Util.GetValueOfString(dr["Name"]);
                            timeRecordingDataObj.PhaseInfo.Add(phaseInfo);
                        }

                    }
                    else if (Util.GetValueOfInt(ds.Tables[0].Rows[i]["R_Request_ID"]) != 0)
                    {
                        timeRecordingDataObj.ProjReq_ID = Util.GetValueOfInt(ds.Tables[0].Rows[i]["R_Request_ID"]);
                        timeRecordingDataObj.ProjReq_Name = Util.GetValueOfString(ds.Tables[0].Rows[i]["RequestName"]);
                        timeRecordingDataObj.RequestSummary = Util.GetValueOfString(ds.Tables[0].Rows[i]["Summary"]);
                    }
                    if (Util.GetValueOfInt(ds.Tables[0].Rows[i]["S_TimeExpenseLine_ID"]) != 0)
                    {
                        timeRecordingDataObj.S_TimeExpenseLine_ID = Util.GetValueOfInt(ds.Tables[0].Rows[i]["S_TimeExpenseLine_ID"]);
                    }
                    if (Util.GetValueOfInt(ds.Tables[0].Rows[i]["VA075_WorkOrderOperation_ID"]) != 0)
                    {
                        timeRecordingDataObj.VA075_WorkOrderOperation_ID = Util.GetValueOfInt(ds.Tables[0].Rows[i]["VA075_WorkOrderOperation_ID"]);
                    }
                    timeRecordingDataObj.RecordedDate = Util.GetValueOfDateTime(ds.Tables[0].Rows[i]["RecordingDate"]);
                    timeRecordingDataObj.EstimatedTime = Util.GetValueOfString(ds.Tables[0].Rows[i]["EstimatedTime"]);
                    if (Util.GetValueOfInt(ds.Tables[0].Rows[i]["Pic"]) != 0)
                    {
                        string extension = ".png";
                        timeRecordingDataObj.ImageUrl = "Images/Thumb46x46/" + Util.GetValueOfInt(ds.Tables[0].Rows[i]["Pic"]) + extension;

                    }
                    if (Util.GetValueOfInt(ds.Tables[0].Rows[i]["AD_Image_ID"]) > 0)
                    {
                        timeRecordingDataObj.productImgUrl = "Images/Thumb46x46/" + Util.GetValueOfInt(ds.Tables[0].Rows[i]["AD_Image_ID"]) + Util.GetValueOfString(ds.Tables[0].Rows[i]["ImageExtension"]);
                    }
                    timeRecordingDataObj.countRecords = countRecords;
                    timeRecordingDataList.Add(timeRecordingDataObj);

                }
            }
            return timeRecordingDataList;
        }

        /// <summary>
        ///This Functionis used for generating invoice
        /// </summary>
        /// <param name="ct">context</param>
        /// <param name="AD_Client_ID">AD_Client_ID</param>
        /// <param name="AD_Org_ID">AD_Org_ID</param>
        /// <param name="DataTobeInvoice">object of data which is to be invoice</param>
        /// <returns>message</returns>
        /// <author>VIS_427</author>
        public string GenerateInvoice(Ctx ct, IEnumerable<dynamic> DataTobeInvoice, int AD_Client_ID, int AD_Org_ID)
        {
            string message = Msg.GetMsg(ct, "VAS_InvoiceSaved");
            MInvoice inv = null;
            List<VAS_InvoiceDetail> invoiceList = new List<VAS_InvoiceDetail>();
            StringBuilder InvoiceIdList = new StringBuilder();
            dynamic sortedData = DataTobeInvoice.OrderBy(d => d.C_BPartner_ID)
                             .ThenBy(d => d.C_BPartner_Location_ID)
                             .ThenBy(d => d.M_PriceList_ID)
                             .ThenBy(d => d.VA009_PaymentMethod_ID).ToList();

            for (int i = 0; i < sortedData.Count; i++)
            {
                if (inv != null && inv.GetC_BPartner_ID() == Util.GetValueOfInt(sortedData[i].C_BPartner_ID)
                      && inv.GetC_BPartner_Location_ID() == Util.GetValueOfInt(sortedData[i].C_BPartner_Location_ID)
                  && inv.GetM_PriceList_ID() == Util.GetValueOfInt(sortedData[i].M_PriceList_ID)
                  && inv.GetVA009_PaymentMethod_ID() == Util.GetValueOfInt(sortedData[i].VA009_PaymentMethod_ID))
                {
                    CreateInvoiceLine(ct, inv, sortedData[i]);

                }

                else
                {
                    inv = CreateInvoiceHeader(ct, inv, sortedData[i], AD_Client_ID, AD_Org_ID);
                    if (inv.Save())
                    {
                        invoiceList.Add(new VAS_InvoiceDetail
                        {
                            Invoice = inv,
                            Invoice_ID = inv.GetC_Invoice_ID()
                        });

                        CreateInvoiceLine(ct, inv, sortedData[i]);
                    }
                    else
                    {
                        ValueNamePair vp = VLogger.RetrieveError();
                        ValueNamePairError(ct, vp, "VAS_InvoiceNotSaved");

                    }

                }

            }
            invoiceList = DeleteInvoices(ct, invoiceList);
            if (invoiceList.Any())
            {
                docno.Append(string.Join(", ", invoiceList.Select(invoiceDetail => invoiceDetail.Invoice.GetDocumentNo())));
            }
            if (docno.Length > 0)
            {
                message += docno.ToString();
                errorMessage.Append(Environment.NewLine + message);
            }
            return errorMessage.ToString();
        }
        /// <summary>
        ///This Function Used to create the Invoice line 
        /// </summary>
        /// <param name="ctx">context</param>
        /// <param name="inv">Object of invoice</param>
        /// <param name="sortedData">Data</param>
        /// <author>VIS_427</author>
        public void CreateInvoiceLine(Ctx ctx, MInvoice inv, dynamic sortedData)
        {
            MProduct product = null;
            MInvoiceLine invLine = new MInvoiceLine(inv);
            if (sortedData.M_Product_ID > 0)
            {
                invLine.SetM_Product_ID(Util.GetValueOfInt(sortedData.M_Product_ID));
                product = MProduct.Get(ctx, Util.GetValueOfInt(sortedData.M_Product_ID));
            }
            else if (sortedData.C_Charge_ID > 0)
            {
                invLine.SetC_Charge_ID(Util.GetValueOfInt(sortedData.C_Charge_ID));
            }

            invLine.SetC_UOM_ID(Util.GetValueOfInt(sortedData.C_Uom_ID));
            invLine.SetQtyEntered(Util.GetValueOfDecimal(sortedData.Qty));
            invLine.SetQtyInvoiced(Util.GetValueOfDecimal(sortedData.Qty));
            invLine.SetPriceLimit(Util.GetValueOfDecimal(sortedData.PriceLimit) != 0 ? Util.GetValueOfDecimal(sortedData.PriceLimit) : Util.GetValueOfDecimal(sortedData.Price));
            invLine.SetPriceList(Util.GetValueOfDecimal(sortedData.PriceStd) != 0 ? Util.GetValueOfDecimal(sortedData.PriceStd) : Util.GetValueOfDecimal(sortedData.Price));
            invLine.SetPriceEntered(Util.GetValueOfDecimal(sortedData.Price));
            invLine.SetPriceActual(Util.GetValueOfDecimal(sortedData.Price));
            if (sortedData.M_Product_ID > 0 && product.GetC_UOM_ID() != Util.GetValueOfInt(sortedData.C_Uom_ID))
            {
                Decimal? QtyEntered = invLine.GetQtyEntered();
                int priceListPrcision = MUOM.GetPrecision(ctx, Util.GetValueOfInt(sortedData.C_Uom_ID));
                Decimal? convertedQty = MUOMConversion.ConvertProductFrom(ctx, Util.GetValueOfInt(sortedData.M_Product_ID), Util.GetValueOfInt(sortedData.C_Uom_ID), QtyEntered);
                if (convertedQty != null)
                {
                    invLine.SetQtyInvoiced(convertedQty);
                }
            }
            if (!invLine.Save())
            {
                ValueNamePair vp = VLogger.RetrieveError();
                ValueNamePairError(ctx, vp, "VAS_InvoiceLineNotSaved");
            }
            else
            {
                SetInvoiceForTask(ctx, sortedData, inv);
            }
        }
        /// <summary>
        ///This Function Used to Fetch Data of project phase 
        /// </summary>
        /// <param name="ds">dataset</param>
        /// <returns>the data set of project phase</returns>
        /// <author>VIS_427</author>
        public DataSet GetPhaseData(DataSet ds)
        {
            List<int> ProjectIds = ds.Tables[0].AsEnumerable()
                      .Select(row => Util.GetValueOfInt(row["c_project_id"]))
                      .Distinct()
                      .ToList();
            string sqlPhase = @"SELECT C_Project_ID,Name,SeqNo FROM C_ProjectPhase WHERE C_Project_ID IN (" + string.Join(",", ProjectIds) + ") ORDER BY C_ProjectPhase_ID";
            DataSet dsPhase = DB.ExecuteDataset(sqlPhase);
            return dsPhase;
        }

        /// <summary>
        ///This Functionis used for creating header of invoice
        /// </summary>
        /// <param name="ct">context</param>
        /// <param name="AD_Client_ID">AD_Client_ID</param>
        /// <param name="AD_Org_ID">AD_Org_ID</param>
        /// <param name="sortedData">object of data which is to be invoice</param>
        /// <param name="inv">invoice</param>
        /// <returns>object of invoice</returns>
        /// <author>VIS_427</author>
        public MInvoice CreateInvoiceHeader(Ctx ct, MInvoice inv, dynamic sortedData, int AD_Client_ID, int AD_Org_ID)
        {
            inv = new MInvoice(ct, 0, null);
            inv.SetAD_Client_ID(AD_Client_ID);
            inv.SetAD_Org_ID(AD_Org_ID);
            inv.SetC_BPartner_ID(Util.GetValueOfInt(Util.GetValueOfInt(sortedData.C_BPartner_ID)));
            inv.SetC_BPartner_Location_ID(Util.GetValueOfInt(Util.GetValueOfInt(sortedData.C_BPartner_Location_ID)));
            inv.SetC_Currency_ID(Util.GetValueOfInt(Util.GetValueOfInt(sortedData.C_Currency_ID)));
            inv.SetC_PaymentTerm_ID(Util.GetValueOfInt(Util.GetValueOfInt(sortedData.C_PaymentTerm_ID))); ;
            inv.SetM_PriceList_ID(Util.GetValueOfInt(Util.GetValueOfInt(sortedData.M_PriceList_ID)));
            inv.SetVA009_PaymentMethod_ID(Util.GetValueOfInt(Util.GetValueOfInt(sortedData.VA009_PaymentMethod_ID)));
            inv.SetSalesRep_ID(ct.GetAD_User_ID());
            inv.SetDateAcct(DateTime.Now);
            inv.SetDateInvoiced(DateTime.Now);
            inv.SetIsSOTrx(true);
            inv.SetIsReturnTrx(false);
            inv.SetIsExpenseInvoice(false);

            if (GetDocTypeID(AD_Client_ID, AD_Org_ID) != 0)
            {
                inv.SetC_DocTypeTarget_ID(C_DocType_ID);
                inv.SetC_DocType_ID(C_DocType_ID);
            }
            if (GetConversionTypeID(AD_Client_ID, AD_Org_ID) != 0)
            {
                inv.SetC_ConversionType_ID(C_ConverType_ID);
            }
            return inv;
        }
        /// <summary>
        ///This Function Used to Get Doctype id
        /// </summary>
        /// <param name="AD_Client_ID">AD_Client_ID</param>
        /// <param name="AD_Org_ID">AD_Org_ID</param>
        /// <returns>this function returns the Doctype id</returns>
        /// <author>VIS_427</author>
        public int GetDocTypeID(int AD_Client_ID, int AD_Org_ID)
        {
            sql.Clear();
            sql.Append("SELECT cd.C_DocType_ID FROM C_DocType cd INNER JOIN C_DocBaseType cbd ON " +
                             " (cbd.DocBaseType = cd.DocBaseType) WHERE cd.IsActive = 'Y' AND cd.IsSOTrx='Y' AND cd.IsReturnTrx='N' AND cd.IsExpenseInvoice = 'N' AND cbd.DocBaseType='ARI' " +
                              " AND cd.AD_Org_ID IN " +
                              " (0," + AD_Org_ID + ")  AND cd.AD_Client_ID IN (0," + AD_Client_ID + ") ORDER BY cd.AD_Org_ID DESC, cd.AD_Client_ID DESC ");

            C_DocType_ID = Util.GetValueOfInt(DB.ExecuteScalar(sql.ToString(), null, null));
            return C_DocType_ID;
        }
        /// <summary>
        ///This Function Used to Get ConvertionType id
        /// </summary>
        /// <param name="AD_Client_ID">AD_Client_ID</param>
        /// <param name="AD_Org_ID">AD_Org_ID</param>
        /// <returns>this function returns the ConvertionType id</returns>
        /// <author>VIS_427</author>
        public int GetConversionTypeID(int AD_Client_ID, int AD_Org_ID)
        {
            C_ConverType_ID = Util.GetValueOfInt(DB.ExecuteScalar("SELECT C_ConversionType_ID FROM C_ConversionType WHERE IsActive = 'Y' " +
                           " AND AD_Org_ID IN (0," + AD_Org_ID + ") AND AD_Client_ID IN (0," + AD_Client_ID + ") ORDER BY AD_Org_ID DESC,AD_Client_ID DESC,IsDefault DESC", null, null));
            return C_ConverType_ID;
        }
        /// <summary>
        ///This Function Used to error messaage
        /// </summary>
        /// <param name="vp">object of valuename pair</param>
        /// <param name="message">message</param>
        /// // <param name="ctx">context</param>
        /// <returns>this function returns error messaage</returns>
        /// <author>VIS_427</author>
        public void ValueNamePairError(Ctx ctx, ValueNamePair vp, string message)
        {
            if (vp != null)
            {
                string val = vp.GetName();
                if (String.IsNullOrEmpty(val))
                {
                    val = vp.GetValue();
                }
                errorMessage.Append(val);
            }
            if (string.IsNullOrEmpty(errorMessage.ToString()))
            {
                errorMessage.Append(Msg.GetMsg(ctx, message));
            }
        }

        /// <summary>
        ///This Function Used to delete invoice
        /// </summary>
        /// <param name="ctx">context</param>
        /// <param name="invoiceList">list of invoices saved</param>
        /// // <param name="ctx">context</param>
        /// <returns>this function returns the list of deleted invoices</returns>
        /// <author>VIS_427</author>

        public List<VAS_InvoiceDetail> DeleteInvoices(Ctx ctx, List<VAS_InvoiceDetail> invoiceList)
        {
            sql.Clear();
            List<int> invoiceIds = invoiceList.Select(invoiceDetail => invoiceDetail.Invoice_ID).ToList();

            sql.Append(@"SELECT COALESCE(COUNT(cil.C_InvoiceLine_ID), 0) AS InvoiceLineCount, ci.C_Invoice_ID 
                   FROM C_Invoice ci 
                   LEFT JOIN C_InvoiceLine cil ON (ci.C_Invoice_ID = cil.C_Invoice_ID) 
                   WHERE ci.C_Invoice_ID IN (" + string.Join(",", invoiceIds) + @")
                   GROUP BY ci.C_Invoice_ID");

            DataSet ds = DB.ExecuteDataset(sql.ToString());
            if (ds != null && ds.Tables[0].Rows.Count > 0)
            {
                List<int> invoicesToDelete = new List<int>();
                for (int i = 0; i < ds.Tables[0].Rows.Count; i++)
                {
                    if (Util.GetValueOfInt(ds.Tables[0].Rows[i]["InvoiceLineCount"]) == 0)
                    {
                        int invoiceId = Util.GetValueOfInt(ds.Tables[0].Rows[i]["C_Invoice_ID"]);
                        invoicesToDelete.Add(invoiceId);
                    }
                }

                if (invoicesToDelete.Any())
                {
                    string deleteSql = "DELETE FROM C_Invoice WHERE C_Invoice_ID IN (" + string.Join(",", invoicesToDelete) + ")";
                    int invDeletedCount = Util.GetValueOfInt(CoreLibrary.DataBase.DB.ExecuteQuery(deleteSql, null));
                    if (invDeletedCount > 0)
                    {
                        invoiceList.RemoveAll(invoiceDetail => invoicesToDelete.Contains(invoiceDetail.Invoice_ID));
                    }
                }
            }
            return invoiceList;
        }
        /// <summary>
        ///This Function Used to Set Invoice  on table S_TimeExpenseLine and VA075_WorkOrderOperation
        /// </summary>
        /// <param name="ctx">context</param>
        /// <param name="sortedData">Object of data</param>
        /// // <param name="invoice">Object of invoice</param>
        /// <returns>this function returns the list of deleted invoices</returns>
        /// <author>VIS_427</author>
        public void SetInvoiceForTask(Ctx ctx, dynamic sortedData, MInvoice invoice)
        {
            if (sortedData.S_TimeExpenseLine_ID != 0)
            {
                DB.ExecuteQuery("UPDATE S_TimeExpenseLine SET C_Invoice_ID=" + invoice.GetC_Invoice_ID() + " WHERE S_TimeExpenseLine_ID="
                     + Util.GetValueOfInt(sortedData.S_TimeExpenseLine_ID), null, null);
            }
            else if (sortedData.VA075_WorkOrderOperation_ID != 0)
            {
                DB.ExecuteQuery("UPDATE VA075_WorkOrderOperation SET C_Invoice_ID=" + invoice.GetC_Invoice_ID() + " WHERE VA075_WorkOrderOperation_ID="
                     + Util.GetValueOfInt(sortedData.VA075_WorkOrderOperation_ID), null, null);
            }
        }
        /// <summary>
        /// This Method is used to return the column id 
        /// </summary>
        /// <param name="ct">context</param>
        /// <param name="ColumnData">Data of the Column</param>
        /// <returns>Dictionary with column name and column id</returns>
        /// <author>VIS_427 </author>
        public Dictionary<string, int> GetColumnIds(Ctx ct, dynamic columnDataArray)
        {
            Dictionary<string, int> ColumnInfo = new Dictionary<string, int>();
            foreach (var item in columnDataArray)
            {
                // Extract column name and table name
                string ColumnName = item.ColumnName;
                string TableName = item.TableName;

                // Construct SQL query to retrieve AD_Column_ID
                string sql = @"SELECT AD_Column_ID FROM AD_Column 
                               WHERE ColumnName ='" + ColumnName + @"' 
                               AND AD_Table_ID = (SELECT AD_Table_ID FROM AD_Table WHERE TableName='" + TableName + @"')";
                ColumnInfo[ColumnName] = Util.GetValueOfInt(DB.ExecuteScalar(sql));
            }
            if (Env.IsModuleInstalled("VA075_"))
            {
                ColumnInfo["AD_Reference_ID"] = Util.GetValueOfInt(DB.ExecuteScalar(@"SELECT AD_Reference_ID FROM AD_Reference WHERE Name='VAS_TaskType'", null,null));
            }
            return ColumnInfo;
        }
        public class TimeRecordingData
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

            public decimal PriceLimit { get; set; }
            public decimal PriceStd { get; set; }
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
            public int stdPrecision { get; set; }
            public int countRecords { get; set; }

            public string EnforcePriceLimit { get; set; }
            public int S_TimeExpenseLine_ID { get; set; }
            public int VA075_WorkOrderOperation_ID { get; set; }
            public int Pic_ID { get; set; }
            public string ImageUrl { get; set; }

            public string productImgUrl { get; set; }
            public string ISO_Code { get; set; }
            public List<ProjectPhaseInfo> PhaseInfo { get; set; }

        }
        public class ProjectPhaseInfo
        {
            public string PhaseName { get; set; }
        }
        public class VAS_InvoiceDetail
        {
            public MInvoice Invoice { get; set; }
            public int Invoice_ID { get; set; }
        }
    }
}