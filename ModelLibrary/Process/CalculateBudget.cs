using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using VAdvantage.Classes;
using ViennaAdvantage.Process;

using VAdvantage.DataBase;
using VAdvantage.SqlExec;
using VAdvantage.Utility;
using System.Data;
using VAdvantage.Logging;
using VAdvantage.ProcessEngine;
using VAdvantage.Model;
using ModelLibrary.Classes;
using System.Reflection;

namespace VAdvantage.Process
{
    /// <summary>
    /// Calculate Budget - This Process is used to calculate available budget and set on Requisition Line.
    /// VIS0060: 31 Jan, 2023
    /// </summary>
    public class CalculateBudget : SvrProcess
    {
        /// <summary>
        /// Prepare - e.g., get Parameters.
        /// </summary>
        /// 
        protected override void Prepare()
        {

        }

        /// <summary>
        /// Process Logic
        /// </summary>
        /// <returns>Message</returns>
        protected override String DoIt()
        {
            DataSet dsRecordData;
            DataRow[] drRecordData = null;
            DataRow[] drBudgetControl = null;
            DataSet dsBudgetControlDimension;
            DataRow[] drBudgetControlDimension = null;
            List<BudgetControl> _budgetControl = new List<BudgetControl>();
            StringBuilder sql = new StringBuilder();
            BudgetCheck budget = new BudgetCheck();
            MRequisition requisition = new MRequisition(GetCtx(), GetRecord_ID(), Get_Trx());

            sql.Clear();
            sql.Append(@"SELECT GL_Budget.GL_Budget_ID , GL_Budget.BudgetControlBasis, GL_Budget.C_Year_ID , GL_Budget.C_Period_ID,GL_Budget.Name As BudgetName, 
                  GL_BudgetControl.C_AcctSchema_ID, GL_BudgetControl.CommitmentType, GL_BudgetControl.BudgetControlScope,  GL_BudgetControl.GL_BudgetControl_ID, GL_BudgetControl.Name AS ControlName,GL_BudgetControl.BudgetBreachPercent
                FROM GL_Budget INNER JOIN GL_BudgetControl ON GL_Budget.GL_Budget_ID = GL_BudgetControl.GL_Budget_ID
                INNER JOIN Ad_ClientInfo ON Ad_ClientInfo.AD_Client_ID = GL_Budget.AD_Client_ID
                WHERE GL_BudgetControl.IsActive = 'Y' AND GL_Budget.IsActive = 'Y' AND GL_BudgetControl.AD_Org_ID IN (0 , " + GetAD_Org_ID() + @")  
                   AND GL_BudgetControl.CommitmentType IN ('B' ) AND 
                  (( GL_Budget.BudgetControlBasis = 'P' AND GL_Budget.C_Period_ID =
                  (SELECT C_Period.C_Period_ID FROM C_Period INNER JOIN c_year ON c_year.c_year_ID = C_Period.c_year_ID
                  WHERE C_Period.IsActive  = 'Y'  AND c_year.C_Calendar_ID = Ad_ClientInfo.C_Calendar_ID
                  AND " + GlobalVariable.TO_DATE(requisition.GetDateDoc(), true) + @" BETWEEN C_Period.startdate AND C_Period.enddate )) 
                OR ( GL_Budget.BudgetControlBasis = 'A' AND GL_Budget.C_Year_ID =
                  (SELECT C_Period.C_Year_ID FROM C_Period INNER JOIN c_year ON c_year.c_year_ID = C_Period.c_year_ID
                  WHERE C_Period.IsActive  = 'Y'   AND c_year.C_Calendar_ID = Ad_ClientInfo.C_Calendar_ID  
                AND " + GlobalVariable.TO_DATE(requisition.GetDateDoc(), true) + @" BETWEEN C_Period.startdate AND C_Period.enddate) ) ) 
                AND (SELECT COUNT(fact_acct_id) FROM fact_acct
                WHERE gl_budget_id = GL_Budget.GL_Budget_ID
                AND (c_period_id  IN ( NVL(GL_Budget.C_Period_ID ,0 ))
                OR c_period_id    IN (SELECT C_Period_ID FROM C_Period   WHERE C_Year_ID = NVL(GL_Budget.C_Year_ID , 0) ) ) ) > 0");
            DataSet dsBudgetControl = DB.ExecuteDataset(sql.ToString(), null, Get_Trx());
            if (dsBudgetControl != null && dsBudgetControl.Tables.Count > 0 && dsBudgetControl.Tables[0].Rows.Count > 0)
            {
                // get budget control ids
                object[] budgetControlIds = dsBudgetControl.Tables[0].AsEnumerable().Select(r => r.Field<object>("GL_BUDGETCONTROL_ID")).ToArray();
                string result = string.Join(",", budgetControlIds);
                dsBudgetControlDimension = budget.GetBudgetDimension(result);

                // get record posting data 
                dsRecordData = BudgetControlling(requisition);
                if (dsRecordData != null && dsRecordData.Tables.Count > 0)
                {
                    // datarows of Debit values which to be controlled
                    drRecordData = dsRecordData.Tables[0].Select("Debit > 0 ", " Account_ID ASC");
                    if (drRecordData != null)
                    {
                        // loop on PO record data which is to be debited only 
                        for (int i = 0; i < drRecordData.Length; i++)
                        {
                            // datarows of Budget, of selected accouting schema
                            drBudgetControl = dsBudgetControl.Tables[0].Select("C_AcctSchema_ID  = " + Util.GetValueOfInt(drRecordData[i]["C_AcctSchema_ID"]));

                            // loop on Budget which to be controlled 
                            if (drBudgetControl != null)
                            {
                                for (int j = 0; j < drBudgetControl.Length; j++)
                                {
                                    // get budget Dimension datarow 
                                    drBudgetControlDimension = dsBudgetControlDimension.Tables[0].Select("GL_BudgetControl_ID  = "
                                                                + Util.GetValueOfInt(drBudgetControl[j]["GL_BudgetControl_ID"]));

                                    // get BUdgeted Controlled Value based on dimension
                                    _budgetControl = budget.GetBudgetControlValue(drRecordData[i], drBudgetControl[j], drBudgetControlDimension,
                                        requisition.GetDateDoc(), _budgetControl, Get_Trx(), 'R', 0);

                                    // Update available Budget amount on Requisiiton Line
                                    UpdateAvailableBudget(drRecordData[i], drBudgetControl[j], drBudgetControlDimension, _budgetControl);
                                }
                            }
                        }
                    }
                }
            }
            return "";
        }

        /// <summary>
        /// This Function is used to get data based on Posting Logic, which is to be posted after completion.
        /// </summary>
        /// <returns>DataSet of Posting Records</returns>
        private DataSet BudgetControlling(MRequisition req)
        {
            int ad_window_id = Util.GetValueOfInt(DB.ExecuteScalar("SELECT AD_Window_ID FROM AD_Window WHERE  Export_ID = 'VIS_322'"));
            DataSet result = new DataSet();
            Type type = null;
            MethodInfo methodInfo = null;
            string className = "FRPTSvc.Controllers.PostAccLocalizationVO";
            type = ClassTypeContainer.GetClassType(className, "FRPTSvc");
            if (type != null)
            {
                methodInfo = type.GetMethod("BudgetControlled");
                if (methodInfo != null)
                {
                    ParameterInfo[] parameters = methodInfo.GetParameters();
                    if (parameters.Length == 8)
                    {
                        object[] parametersArray = new object[] { GetCtx(),
                                                                Util.GetValueOfInt(GetAD_Client_ID()),
                                                                Util.GetValueOfInt(X_M_Requisition.Table_ID),//MTable.Get(GetCtx() , "M_Requisition").GetAD_Table_ID()
                                                                Util.GetValueOfInt(req.GetM_Requisition_ID()),
                                                                true,
                                                                Util.GetValueOfInt(GetAD_Org_ID()),
                                                                ad_window_id,
                                                                Util.GetValueOfInt(req.GetC_DocType_ID()) };
                        result = (DataSet)methodInfo.Invoke(null, parametersArray);
                    }
                }
            }
            return result;
        }

        /// <summary>
        /// This Function is used to Reduce From Budget controlled amount
        /// </summary>
        /// <param name="drDataRecord">document Posting Record</param>
        /// <param name="drBUdgetControl">BUdget Control information</param>
        /// <param name="drBudgetComtrolDimension">Budget Control dimension which is applicable</param>
        /// <param name="_listBudgetControl">list of Budget controls</param>
        /// <returns>modified list Budget Control</returns>
        public void UpdateAvailableBudget(DataRow drDataRecord, DataRow drBUdgetControl, DataRow[] drBudgetComtrolDimension, List<BudgetControl> _listBudgetControl)
        {
            BudgetControl _budgetControl = null;
            List<String> selectedDimension = new List<string>();
            if (drBudgetComtrolDimension != null)
            {
                for (int i = 0; i < drBudgetComtrolDimension.Length; i++)
                {
                    selectedDimension.Add(Util.GetValueOfString(drBudgetComtrolDimension[i]["ElementType"]));
                }
            }

            if (_listBudgetControl.Exists(x => (x.GL_Budget_ID == Util.GetValueOfInt(drBUdgetControl["GL_Budget_ID"])) &&
                                              (x.GL_BudgetControl_ID == Util.GetValueOfInt(drBUdgetControl["GL_BudgetControl_ID"])) &&
                                              (x.Account_ID == Util.GetValueOfInt(drDataRecord["Account_ID"])) &&
                                              (x.AD_Org_ID == (selectedDimension.Contains(X_C_AcctSchema_Element.ELEMENTTYPE_Organization) ? Util.GetValueOfInt(drDataRecord["AD_Org_ID"]) : 0)) &&
                                              (x.C_BPartner_ID == (selectedDimension.Contains(X_C_AcctSchema_Element.ELEMENTTYPE_BPartner) ? Util.GetValueOfInt(drDataRecord["C_BPartner_ID"]) : 0)) &&
                                              (x.M_Product_ID == (selectedDimension.Contains(X_C_AcctSchema_Element.ELEMENTTYPE_Product) ? Util.GetValueOfInt(drDataRecord["M_Product_ID"]) : 0)) &&
                                              (x.C_Activity_ID == (selectedDimension.Contains(X_C_AcctSchema_Element.ELEMENTTYPE_Activity) ? Util.GetValueOfInt(drDataRecord["C_Activity_ID"]) : 0)) &&
                                              (x.C_LocationFrom_ID == (selectedDimension.Contains(X_C_AcctSchema_Element.ELEMENTTYPE_LocationFrom) ? Util.GetValueOfInt(drDataRecord["C_LocationFrom_ID"]) : 0)) &&
                                              (x.C_LocationTo_ID == (selectedDimension.Contains(X_C_AcctSchema_Element.ELEMENTTYPE_LocationTo) ? Util.GetValueOfInt(drDataRecord["C_LocationTo_ID"]) : 0)) &&
                                              (x.C_Campaign_ID == (selectedDimension.Contains(X_C_AcctSchema_Element.ELEMENTTYPE_Campaign) ? Util.GetValueOfInt(drDataRecord["C_Campaign_ID"]) : 0)) &&
                                              (x.AD_OrgTrx_ID == (selectedDimension.Contains(X_C_AcctSchema_Element.ELEMENTTYPE_OrgTrx) ? Util.GetValueOfInt(drDataRecord["AD_OrgTrx_ID"]) : 0)) &&
                                              (x.C_Project_ID == (selectedDimension.Contains(X_C_AcctSchema_Element.ELEMENTTYPE_Project) ? Util.GetValueOfInt(drDataRecord["C_Project_ID"]) : 0)) &&
                                              (x.C_SalesRegion_ID == (selectedDimension.Contains(X_C_AcctSchema_Element.ELEMENTTYPE_SalesRegion) ? Util.GetValueOfInt(drDataRecord["C_SalesRegion_ID"]) : 0)) &&
                                              (x.UserList1_ID == (selectedDimension.Contains(X_C_AcctSchema_Element.ELEMENTTYPE_UserList1) ? Util.GetValueOfInt(drDataRecord["UserList1_ID"]) : 0)) &&
                                              (x.UserList2_ID == (selectedDimension.Contains(X_C_AcctSchema_Element.ELEMENTTYPE_UserList2) ? Util.GetValueOfInt(drDataRecord["UserList2_ID"]) : 0)) &&
                                              (x.UserElement1_ID == (selectedDimension.Contains(X_C_AcctSchema_Element.ELEMENTTYPE_UserElement1) ? Util.GetValueOfInt(drDataRecord["UserElement1_ID"]) : 0)) &&
                                              (x.UserElement2_ID == (selectedDimension.Contains(X_C_AcctSchema_Element.ELEMENTTYPE_UserElement2) ? Util.GetValueOfInt(drDataRecord["UserElement2_ID"]) : 0)) &&
                                              (x.UserElement3_ID == (selectedDimension.Contains(X_C_AcctSchema_Element.ELEMENTTYPE_UserElement3) ? Util.GetValueOfInt(drDataRecord["UserElement3_ID"]) : 0)) &&
                                              (x.UserElement4_ID == (selectedDimension.Contains(X_C_AcctSchema_Element.ELEMENTTYPE_UserElement4) ? Util.GetValueOfInt(drDataRecord["UserElement4_ID"]) : 0)) &&
                                              (x.UserElement5_ID == (selectedDimension.Contains(X_C_AcctSchema_Element.ELEMENTTYPE_UserElement5) ? Util.GetValueOfInt(drDataRecord["UserElement5_ID"]) : 0)) &&
                                              (x.UserElement6_ID == (selectedDimension.Contains(X_C_AcctSchema_Element.ELEMENTTYPE_UserElement6) ? Util.GetValueOfInt(drDataRecord["UserElement6_ID"]) : 0)) &&
                                              (x.UserElement7_ID == (selectedDimension.Contains(X_C_AcctSchema_Element.ELEMENTTYPE_UserElement7) ? Util.GetValueOfInt(drDataRecord["UserElement7_ID"]) : 0)) &&
                                              (x.UserElement8_ID == (selectedDimension.Contains(X_C_AcctSchema_Element.ELEMENTTYPE_UserElement8) ? Util.GetValueOfInt(drDataRecord["UserElement8_ID"]) : 0)) &&
                                              (x.UserElement9_ID == (selectedDimension.Contains(X_C_AcctSchema_Element.ELEMENTTYPE_UserElement9) ? Util.GetValueOfInt(drDataRecord["UserElement9_ID"]) : 0))
                                             ))
            {
                _budgetControl = _listBudgetControl.Find(x => (x.GL_Budget_ID == Util.GetValueOfInt(drBUdgetControl["GL_Budget_ID"])) &&
                                              (x.GL_BudgetControl_ID == Util.GetValueOfInt(drBUdgetControl["GL_BudgetControl_ID"])) &&
                                              (x.Account_ID == Util.GetValueOfInt(drDataRecord["Account_ID"])) &&
                                              (x.AD_Org_ID == (selectedDimension.Contains(X_C_AcctSchema_Element.ELEMENTTYPE_Organization) ? Util.GetValueOfInt(drDataRecord["AD_Org_ID"]) : 0)) &&
                                              (x.C_BPartner_ID == (selectedDimension.Contains(X_C_AcctSchema_Element.ELEMENTTYPE_BPartner) ? Util.GetValueOfInt(drDataRecord["C_BPartner_ID"]) : 0)) &&
                                              (x.M_Product_ID == (selectedDimension.Contains(X_C_AcctSchema_Element.ELEMENTTYPE_Product) ? Util.GetValueOfInt(drDataRecord["M_Product_ID"]) : 0)) &&
                                              (x.C_Activity_ID == (selectedDimension.Contains(X_C_AcctSchema_Element.ELEMENTTYPE_Activity) ? Util.GetValueOfInt(drDataRecord["C_Activity_ID"]) : 0)) &&
                                              (x.C_LocationFrom_ID == (selectedDimension.Contains(X_C_AcctSchema_Element.ELEMENTTYPE_LocationFrom) ? Util.GetValueOfInt(drDataRecord["C_LocationFrom_ID"]) : 0)) &&
                                              (x.C_LocationTo_ID == (selectedDimension.Contains(X_C_AcctSchema_Element.ELEMENTTYPE_LocationTo) ? Util.GetValueOfInt(drDataRecord["C_LocationTo_ID"]) : 0)) &&
                                              (x.C_Campaign_ID == (selectedDimension.Contains(X_C_AcctSchema_Element.ELEMENTTYPE_Campaign) ? Util.GetValueOfInt(drDataRecord["C_Campaign_ID"]) : 0)) &&
                                              (x.AD_OrgTrx_ID == (selectedDimension.Contains(X_C_AcctSchema_Element.ELEMENTTYPE_OrgTrx) ? Util.GetValueOfInt(drDataRecord["AD_OrgTrx_ID"]) : 0)) &&
                                              (x.C_Project_ID == (selectedDimension.Contains(X_C_AcctSchema_Element.ELEMENTTYPE_Project) ? Util.GetValueOfInt(drDataRecord["C_Project_ID"]) : 0)) &&
                                              (x.C_SalesRegion_ID == (selectedDimension.Contains(X_C_AcctSchema_Element.ELEMENTTYPE_SalesRegion) ? Util.GetValueOfInt(drDataRecord["C_SalesRegion_ID"]) : 0)) &&
                                              (x.UserList1_ID == (selectedDimension.Contains(X_C_AcctSchema_Element.ELEMENTTYPE_UserList1) ? Util.GetValueOfInt(drDataRecord["UserList1_ID"]) : 0)) &&
                                              (x.UserList2_ID == (selectedDimension.Contains(X_C_AcctSchema_Element.ELEMENTTYPE_UserList2) ? Util.GetValueOfInt(drDataRecord["UserList2_ID"]) : 0)) &&
                                              (x.UserElement1_ID == (selectedDimension.Contains(X_C_AcctSchema_Element.ELEMENTTYPE_UserElement1) ? Util.GetValueOfInt(drDataRecord["UserElement1_ID"]) : 0)) &&
                                              (x.UserElement2_ID == (selectedDimension.Contains(X_C_AcctSchema_Element.ELEMENTTYPE_UserElement2) ? Util.GetValueOfInt(drDataRecord["UserElement2_ID"]) : 0)) &&
                                              (x.UserElement3_ID == (selectedDimension.Contains(X_C_AcctSchema_Element.ELEMENTTYPE_UserElement3) ? Util.GetValueOfInt(drDataRecord["UserElement3_ID"]) : 0)) &&
                                              (x.UserElement4_ID == (selectedDimension.Contains(X_C_AcctSchema_Element.ELEMENTTYPE_UserElement4) ? Util.GetValueOfInt(drDataRecord["UserElement4_ID"]) : 0)) &&
                                              (x.UserElement5_ID == (selectedDimension.Contains(X_C_AcctSchema_Element.ELEMENTTYPE_UserElement5) ? Util.GetValueOfInt(drDataRecord["UserElement5_ID"]) : 0)) &&
                                              (x.UserElement6_ID == (selectedDimension.Contains(X_C_AcctSchema_Element.ELEMENTTYPE_UserElement6) ? Util.GetValueOfInt(drDataRecord["UserElement6_ID"]) : 0)) &&
                                              (x.UserElement7_ID == (selectedDimension.Contains(X_C_AcctSchema_Element.ELEMENTTYPE_UserElement7) ? Util.GetValueOfInt(drDataRecord["UserElement7_ID"]) : 0)) &&
                                              (x.UserElement8_ID == (selectedDimension.Contains(X_C_AcctSchema_Element.ELEMENTTYPE_UserElement8) ? Util.GetValueOfInt(drDataRecord["UserElement8_ID"]) : 0)) &&
                                              (x.UserElement9_ID == (selectedDimension.Contains(X_C_AcctSchema_Element.ELEMENTTYPE_UserElement9) ? Util.GetValueOfInt(drDataRecord["UserElement9_ID"]) : 0))
                                             );

                int cnt = DB.ExecuteQuery("UPDATE M_RequisitionLine SET VAS_AvailableBudget = " + _budgetControl.AvailableBudget + " WHERE M_RequisitionLine_ID ="
                        + Util.GetValueOfInt(drDataRecord["Line_ID"]), null, Get_Trx());

                log.Info("Available Budget Updated - " + Util.GetValueOfString(drBUdgetControl["BudgetName"]) + " - "
                                    + Util.GetValueOfString(drBUdgetControl["ControlName"]) + " - (" + _budgetControl.AvailableBudget + ") - Table ID : " +
                                    Util.GetValueOfInt(drDataRecord["LineTable_ID"]) + " - Record ID : " + Util.GetValueOfInt(drDataRecord["Line_ID"]));
            }
            
        }

    }
}
