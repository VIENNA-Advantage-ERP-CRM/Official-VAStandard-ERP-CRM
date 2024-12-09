using CoreLibrary.DataBase;
using System;
using System.Collections.Generic;
using System.Data;
using System.Linq;
using System.Text;
using VAdvantage.Logging;
using VAdvantage.Model;
using VAdvantage.ProcessEngine;
using VAdvantage.Utility;

namespace VAdvantage.Process
{
    /******************************************************
 * Module Name    : VAS
 * Purpose        : VAStandard
 * Chronological Development
 * VIS0060:       11 July, 2023
 *  *****************************************************/
    public class CopyFromProjectLine : SvrProcess
    {

        int ProjectID = 0, _org_ID = 0, lineNo;
        string ProjectLine = null;
        MRequisitionLine requisitionLine = null;
        ValueNamePair vp = null;
        StringBuilder msg = new StringBuilder();
        List<int> ProjecLineExist = new List<int>();

        /// <summary>
        /// for fetching the parm data
        /// </summary>
        protected override void Prepare()
        {
            ProcessInfoParameter[] para = GetParameter();
            if (para != null & para.Length > 0)
            {
                foreach (var par in para)
                {
                    string name = par.GetParameterName();
                    if (name.Equals("C_Project_ID"))
                    {
                        ProjectID = Util.GetValueOfInt(par.GetParameter());
                    }
                    if (name.Equals("C_ProjectLine_ID"))
                    {
                        ProjectLine = Util.GetValueOfString(par.GetParameter());
                    }
                    else
                    {
                        log.Log(Level.SEVERE, "Unknown parameter:" + name);
                    }
                }


            }
        }
        /// <summary>
        /// VIS0336:For inserting the record into Requisituon Lines
        /// </summary>
        /// <returns></returns>
        protected override string DoIt()
        {
            DateTime? DateRequired = null;
            StringBuilder sql = new StringBuilder(@"SELECT AD_Org_ID,DateRequired FROM M_Requisition  WHERE M_Requisition_ID = " + GetRecord_ID());
            DataSet ds1 = DB.ExecuteDataset(sql.ToString(), null, null);
            if (ds1 != null && ds1.Tables[0].Rows.Count > 0)
            {
                _org_ID = Util.GetValueOfInt(ds1.Tables[0].Rows[0]["AD_Org_ID"]);
                DateRequired = Util.GetValueOfDateTime(ds1.Tables[0].Rows[0]["DateRequired"]);
            }
            sql.Clear();
            sql.Append(@"SELECT C_ProjectLine_ID FROM M_RequisitionLine  WHERE M_Requisition_ID = " + GetRecord_ID());
            DataSet ds = DB.ExecuteDataset(sql.ToString(), null, Get_Trx());

            if (ds != null && ds.Tables.Count > 0 && ds.Tables[0].Rows.Count > 0)
            {
                ProjecLineExist = ds.Tables[0].AsEnumerable().Select(x => Util.GetValueOfInt(x.Field<object>("C_ProjectLine_ID"))).Distinct().ToList();
            }

            sql.Clear();
            if (!string.IsNullOrEmpty(ProjectLine))
            {
                sql.Append(@"SELECT M_Product_ID,M_AttributeSetInstance_ID,C_Charge_ID,C_UOM_ID,PlannedPrice,PlannedQty,C_ProjectLine_ID FROM C_ProjectLine WHERE C_ProjectLine_ID IN (" + ProjectLine + ")");
            }
            else
            {
                sql.Append(@"SELECT M_Product_ID,M_AttributeSetInstance_ID,C_Charge_ID,C_UOM_ID,PlannedPrice,PlannedQty,C_ProjectLine_ID FROM C_ProjectLine WHERE C_Project_ID=" + ProjectID);

            }
            ds = DB.ExecuteDataset(sql.ToString(), null, Get_Trx());
            if (ds != null && ds.Tables.Count > 0 && ds.Tables[0].Rows.Count > 0)
            {
                lineNo = Util.GetValueOfInt(DB.ExecuteScalar("SELECT NVL(MAX(Line), 0)+10 AS LineNo FROM M_RequisitionLine  WHERE M_Requisition_ID = "
                    + GetRecord_ID(), null, Get_Trx()));
                for (int i = 0; i < ds.Tables[0].Rows.Count; i++)

                {
                    if (ProjecLineExist.Count > 0 && ProjecLineExist.Contains(Util.GetValueOfInt(ds.Tables[0].Rows[i]["C_ProjectLine_ID"])))
                    {
                        continue;
                    }

                    requisitionLine = new MRequisitionLine(GetCtx(), 0, Get_Trx());
                    requisitionLine.SetAD_Org_ID(_org_ID);
                    requisitionLine.SetM_Requisition_ID(GetRecord_ID());
                    requisitionLine.Set_Value("C_Project_ID", ProjectID);
                    requisitionLine.Set_Value("C_ProjectLine_ID", Util.GetValueOfInt(ds.Tables[0].Rows[i]["C_ProjectLine_ID"]));
                    requisitionLine.SetM_Product_ID(Util.GetValueOfInt(ds.Tables[0].Rows[i]["M_Product_ID"]));
                    requisitionLine.SetM_AttributeSetInstance_ID(Util.GetValueOfInt(ds.Tables[0].Rows[i]["M_AttributeSetInstance_ID"]));
                    requisitionLine.SetC_Charge_ID(Util.GetValueOfInt(ds.Tables[0].Rows[i]["C_Charge_ID"]));
                    requisitionLine.Set_ValueNoCheck("C_UOM_ID", Util.GetValueOfInt(ds.Tables[0].Rows[i]["C_UOM_ID"]));
                    requisitionLine.SetQty(Util.GetValueOfInt(ds.Tables[0].Rows[i]["PlannedQty"])); //VAI050-Set Reserved quantity
                    requisitionLine.SetPriceActual(Util.GetValueOfDecimal(ds.Tables[0].Rows[i]["PlannedPrice"]));
                    requisitionLine.SetQtyEntered(Util.GetValueOfInt(ds.Tables[0].Rows[i]["PlannedQty"]));
                    requisitionLine.Set_Value("DTD001_DateRequired", DateRequired);//VIS0336-set date required on line
                    requisitionLine.SetLine(lineNo);
                    if (!requisitionLine.Save())
                    {
                        vp = VLogger.RetrieveError();
                        if (vp != null && !string.IsNullOrEmpty(vp.GetName()))
                        {
                            msg.Append(vp.GetName());
                        }
                        msg.Append(Msg.GetMsg(GetCtx(), "VAS_ReqLinesNotSaved"));
                    }
                    else
                    {
                        lineNo += 10;
                    }
                }

            }

            if (msg.Length > 0)
            {
                return msg.ToString();
            }
            else
            {
                return Msg.GetMsg(GetCtx(), "VAS_RequLineAdded");

            }
        }
    }
}
