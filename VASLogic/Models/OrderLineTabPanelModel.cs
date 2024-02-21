﻿/*******************************************************
       * Module Name    : VASLogic
       * Purpose        : Tab Panel For PO Lines tab of Purchage Order window
       * chronological  : Development
       * Created Date   : 20 February 2024
       * Created by     : VIS430
******************************************************/
using CoreLibrary.DataBase;
using System;
using System.Collections.Generic;
using System.Data;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using VAdvantage.Utility;

namespace VASLogic.Models
{
    public class OrderLineTabPanelModel
    {
        /// <summary>
        /// VIS430: Get the RequitionLines  tab data for PO lines tab record of Purchase order
        /// </summary>
        /// <param name="ctx"></param>
        /// <param name="OrderLineId"></param>
        /// <returns></returns>
        public List<RequitionTabPanel> GetRequitionLinesData(Ctx ctx, int OrderLineId)
        {
            List<RequitionTabPanel> RequitionTabPanel = new List<RequitionTabPanel>();
            String sql = @"SELECT ch.Name AS chargename,req.DocumentNo AS requitiondocument,um.Name AS uomname,pr.Name As productname,re.Line,re.M_Requisition_ID,re.M_Product_ID,re.C_Charge_ID,re.Qty,re.C_UOM_ID,re.PriceActual,re.LineNetAmt,re.Description FROM M_RequisitionLine re 
                          INNER JOIN C_OrderLine ol ON (ol.C_OrderLine_ID = re.C_OrderLine_ID) 
                          LEFT JOIN M_Product pr ON (pr.M_Product_ID = re.M_Product_ID)
                          INNER JOIN C_UOM um ON (um.C_UOM_ID = re.C_UOM_ID)
                          INNER JOIN M_Requisition req ON (req.M_Requisition_ID = re.M_Requisition_ID)
                          LEFT JOIN C_Charge ch ON (ch.C_Charge_ID = re.C_Charge_ID)
                          WHERE re.C_OrderLine_ID = " + OrderLineId + " Order By re.Line";

            DataSet ds = DB.ExecuteDataset(sql, null, null);
            if (ds != null && ds.Tables.Count > 0 && ds.Tables[0].Rows.Count > 0)
            {
                for (int i = 0; i < ds.Tables[0].Rows.Count; i++)
                {
                    RequitionTabPanel obj = new RequitionTabPanel();
                    obj.LineNo = Util.GetValueOfInt(ds.Tables[0].Rows[i]["Line"]);
                    obj.RequisitionId = Util.GetValueOfInt(ds.Tables[0].Rows[i]["requitiondocument"]);
                    obj.ProductId = Util.GetValueOfString(ds.Tables[0].Rows[i]["productname"]);
                    obj.ChargeId = Util.GetValueOfString(ds.Tables[0].Rows[i]["chargename"]);
                    obj.UomId = Util.GetValueOfString(ds.Tables[0].Rows[i]["uomname"]);
                    obj.PriceActual = Util.GetValueOfDecimal(ds.Tables[0].Rows[i]["PriceActual"]);
                    obj.LineNetAmt = Util.GetValueOfDecimal(ds.Tables[0].Rows[i]["LineNetAmt"]);
                    obj.Description = Util.GetValueOfString(ds.Tables[0].Rows[i]["Description"]);
                    obj.Qty = Util.GetValueOfInt(ds.Tables[0].Rows[i]["Qty"]);
                    RequitionTabPanel.Add(obj);
                }
            }
            return RequitionTabPanel;
        }
        /// <summary>
        /// VIS430: Get the Matching tab data for PO lines tab record of Purchase order
        /// </summary>
        /// <param name="ctx"></param>
        /// <param name="OrderLineId"></param>
        /// <returns></returns>
        public List<MatchingTabPanel> GetMatchingData(Ctx ctx, int OrderLineId)
        {
            List<MatchingTabPanel> MatchingTabPanel = new List<MatchingTabPanel>();
            String sql = @"SELECT inv.DocumentNo AS invoice,ml.Line AS shipmentlineno,po.DocumentNo AS orderline,pr.Name AS productname,mo.DateTrx,mo.DocumentNo AS purchaseorderno,mo.C_OrderLine_ID,mo.M_InOutLine_ID,mo.C_InvoiceLine_ID,mo.M_Product_ID,mo.M_AttributeSetInstance_ID,mo.Qty FROM M_MatchPO mo 
                          INNER JOIN C_OrderLine ol ON (mo.C_OrderLine_ID = ol.C_OrderLine_ID) 
                          INNER JOIN C_Order po ON (po.C_Order_ID = ol.C_Order_ID) 
                          INNER JOIN M_Product pr ON (pr.M_Product_ID = mo.M_Product_ID)
                          INNER JOIN M_InOutLine ml ON (mo.M_InOutLine_ID = ml.M_InOutLine_ID)
                          INNER JOIN C_InvoiceLine iv ON (iv.C_InvoiceLine_ID = mo.C_InvoiceLine_ID)
                          INNER JOIN C_Invoice inv ON(inv.C_Invoice_ID = iv.C_Invoice_ID)
                          WHERE mo.C_OrderLine_ID = " + OrderLineId + " Order By mo.DocumentNo";

            DataSet ds = DB.ExecuteDataset(sql, null, null);
            if (ds != null && ds.Tables.Count > 0 && ds.Tables[0].Rows.Count > 0)
            {
                for (int i = 0; i < ds.Tables[0].Rows.Count; i++)
                {
                    MatchingTabPanel obj = new MatchingTabPanel();
                    obj.TransactionDate = Util.GetValueOfDateTime(ds.Tables[0].Rows[i]["DateTrx"]);
                    obj.PurchaseOrderNo = Util.GetValueOfInt(ds.Tables[0].Rows[i]["purchaseorderno"]);
                    obj.OrderLine = Util.GetValueOfInt(ds.Tables[0].Rows[i]["orderline"]);
                    obj.ShipmentLine = Util.GetValueOfInt(ds.Tables[0].Rows[i]["shipmentlineno"]);
                    obj.InvoiceLine = Util.GetValueOfInt(ds.Tables[0].Rows[i]["invoice"]);
                    obj.Product = Util.GetValueOfString(ds.Tables[0].Rows[i]["productname"]);
                    obj.AttributeSetInstance = Util.GetValueOfInt(ds.Tables[0].Rows[i]["M_AttributeSetInstance_ID"]);
                    obj.Quantity = Util.GetValueOfDecimal(ds.Tables[0].Rows[i]["Qty"]);
                    MatchingTabPanel.Add(obj);
                }
            }
            return MatchingTabPanel;
        }
    }
    public class RequitionTabPanel
    {
        public int LineNo { get; set; }

        public int RequisitionId { get; set; }

        public string ProductId { get; set; }

        public string ChargeId { get; set; }
        public int Qty { get; set; }
        public string UomId { get; set; }
        public decimal PriceActual { get; set; }
        public decimal LineNetAmt { get; set; }
        public string Description { get; set; }

    }

    public class MatchingTabPanel
    {
        public DateTime? TransactionDate { get; set; }

        public int PurchaseOrderNo { get; set; }

        public int OrderLine { get; set; }

        public int ShipmentLine { get; set; }
        public int InvoiceLine { get; set; }
        public string Product { get; set; }
        public int AttributeSetInstance { get; set; }
        public decimal Quantity { get; set; }
        

    }
}
