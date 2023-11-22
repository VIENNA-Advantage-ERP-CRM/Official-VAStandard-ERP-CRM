/********************************************************
 * Project Name   : VAdvantage
 * Class Name     : RfQCopyLines
 * Purpose        : Copy Lines	
 * Class Used     : ProcessEngine.SvrProcess
 * Chronological    Development
 * Raghunandan     10-Aug.-2009
  ******************************************************/
using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using VAdvantage.Classes;
using VAdvantage.Common;
using VAdvantage.Process;
using System.Windows.Forms;
using VAdvantage.Model;
using VAdvantage.DataBase;
using VAdvantage.SqlExec;
using VAdvantage.Utility;
using System.Data;
using VAdvantage.Logging;


using VAdvantage.ProcessEngine;
namespace VAdvantage.Process
{
    public class RfQCopyLines : ProcessEngine.SvrProcess
    {
        //From RfQ 			
        private int _From_RfQ_ID = 0;
        //	From RfQ 			
        private int p_To_RfQ_ID = 0;

        /// <summary>
        /// Prepare
        /// </summary>
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
                else if (name.Equals("C_RfQ_ID"))
                {
                    _From_RfQ_ID = Convert.ToInt32(para[i].GetParameter());//.intValue();
                }
                else
                {
                    log.Log(Level.SEVERE, "prepare - Unknown Parameter: " + name);
                }
            }
            p_To_RfQ_ID = GetRecord_ID();
        }

        /// <summary>
        /// Process
        /// VFramwork.Process.SvrProcess#doIt()
        /// </summary>
        /// <returns>message</returns>
        protected override String DoIt()
        {
            log.Info("doIt - From_RfQ_ID=" + _From_RfQ_ID + ", To_RfQ_ID=" + p_To_RfQ_ID);
            //
            MRfQ to = new MRfQ(GetCtx(), p_To_RfQ_ID, Get_TrxName());
            if (to.Get_ID() == 0)
            {
                throw new ArgumentException("No To RfQ found");
            }
            MRfQ from = new MRfQ(GetCtx(), _From_RfQ_ID, Get_TrxName());
            if (from.Get_ID() == 0)
            {
                throw new ArgumentException("No From RfQ found");
            }

            //	Copy Lines
            int counter = 0;
            StringBuilder msg = new StringBuilder();
            int LineNo = Util.GetValueOfInt(DB.ExecuteScalar("SELECT NVL(MAX(Line),0)+10 FROM C_RfQLine WHERE C_RfQ_ID=" + p_To_RfQ_ID, null, Get_Trx()));
            //VAI050-Get max Line no

            MRfQLine[] lines = from.GetLines();
            for (int i = 0; i < lines.Length; i++)
            {
                MRfQLine newLine = new MRfQLine(to);
                newLine.SetLine(LineNo);
                newLine.SetDescription(lines[i].GetDescription());
                newLine.SetHelp(lines[i].GetHelp());
                if (lines[i].GetM_Product_ID() > 0)
                {
                    newLine.SetM_Product_ID(lines[i].GetM_Product_ID());
                }
                else
                {
                    newLine.Set_Value("C_Charge_ID", lines[i].Get_Value("C_Charge_ID"));
                }
                newLine.SetM_AttributeSetInstance_ID(lines[i].GetM_AttributeSetInstance_ID());
                //	newLine.setDateWorkStart();
                //	newLine.setDateWorkComplete();
                newLine.SetDeliveryDays(lines[i].GetDeliveryDays());
                if (!newLine.Save())
                {
                    ValueNamePair vp = VLogger.RetrieveError();
                    if (vp != null && !string.IsNullOrEmpty(vp.GetName()))
                    {
                        msg.Append(vp.GetName());
                    }
                    Get_Trx().Rollback();
                    return msg.Append(Msg.GetMsg(GetCtx(), "VAS_LineNotSaved")).ToString();
                }
                else
                {
                    LineNo = LineNo + 10; //VAI050-Increment in Line 
                }

                //	Copy Qtys
                MRfQLineQty[] qtys = lines[i].GetQtys();
                for (int j = 0; j < qtys.Length; j++)
                {
                    MRfQLineQty newQty = new MRfQLineQty(newLine);
                    newQty.SetC_UOM_ID(qtys[j].GetC_UOM_ID());
                    newQty.SetQty(qtys[j].GetQty());
                    //newQty.SetBenchmarkPrice(qtys[j].GetBenchmarkPrice());
                    //newQty.Set_Value("LineNetAmt", qtys[j].GetQty() * qtys[j].GetBenchmarkPrice());
                    newQty.SetIsOfferQty(qtys[j].IsOfferQty());
                    newQty.SetIsPurchaseQty(qtys[j].IsPurchaseQty());
                    newQty.SetMargin(qtys[j].GetMargin());
                    if (!newQty.Save())
                    {
                        ValueNamePair vp = VLogger.RetrieveError();
                        if (vp != null && !string.IsNullOrEmpty(vp.GetName()))
                        {
                            msg.Append(vp.GetName());
                        }
                        Get_Trx().Rollback();
                        return msg.Append(Msg.GetMsg(GetCtx(), "VAS_QtyNotSaved")).ToString();
                    }
                }
                counter++;
            }   //	copy all lines	



            return "Copied=" + counter;
        }
    }
}
