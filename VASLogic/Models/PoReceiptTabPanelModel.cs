/*******************************************************
       * Module Name    : VASLogic
       * Purpose        : Tab Panel For AP Matched PO and MatchedReceipt
       * chronological  : Development
       * Created Date   : 12 January 2024
       * Created by     : VAI066
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
    public class PoReceiptTabPanelModel
    {
        /// <summary>
        /// Used to Get data 
        /// </summary>
        /// <param name="ctx">Take the context</param>
        /// <param name="parentID">Take the parentID means the Invoice Line ID</param>
        /// <Author>VAI066 Devops ID: 4216</Author>
        /// <returns>returns the data</returns>
        public List<TabPanel> GetInvoiceLineData(Ctx ctx, int parentID)
        {
            List<TabPanel> tabPanels = new List<TabPanel>();
            String sql = @"SELECT
                        CASE WHEN OL.Line > 0 THEN Cast(O.DocumentNo as varchar(1000)) || '_' || Cast(OL.Line as varchar(1000))
                        ELSE ' ' END AS OrderDocumentLineNo,
                        CASE WHEN IL.Line > 0 THEN Cast(I.DocumentNo as varchar(1000)) || '_' || Cast(IL.Line as varchar(1000))
                        ELSE ' ' END AS InvoiceDocumentLineNo,
                        CASE WHEN IOL.Line > 0 THEN Cast(INO.DocumentNo as varchar(1000)) || '_' || Cast(IOL.Line as varchar(1000))
                        ELSE ' ' END AS InoutDocumentLineNo,
                        IL.QtyInvoiced AS Qty,
                        NVL(P.Name, ' ') AS ProductName,
                        NVL(ASI.Description,' ') AS AttributeSetInstance
                        FROM
                         C_InvoiceLine IL
                        INNER JOIN C_Invoice I ON I.C_Invoice_ID = IL.C_Invoice_ID
                        LEFT JOIN M_Product P ON IL.M_Product_ID = P.M_Product_ID
                        LEFT JOIN M_AttributeSetInstance ASI ON ASI.M_AttributeSetInstance_ID = IL.M_AttributeSetInstance_ID
                        LEFT JOIN C_OrderLine OL ON OL.C_OrderLine_ID = IL.C_OrderLine_ID
                        LEFT JOIN C_Order O ON O.C_Order_ID = OL.C_Order_ID
                        LEFT JOIN M_InoutLine IOL ON IOL.M_InoutLine_ID = IL.M_InoutLine_ID
                        LEFT JOIN M_Inout INO ON INO.M_Inout_ID = IOL.M_Inout_ID
                        WHERE
                            I.DocStatus IN ('CO', 'CL') AND IL.C_InvoiceLine_ID = " + parentID;

            DataSet ds = DB.ExecuteDataset(sql,null,null);
            if (ds != null && ds.Tables[0].Rows.Count > 0)
            {
                for (int i = 0; i < ds.Tables[0].Rows.Count; i++)
                {
                    TabPanel obj = new TabPanel();

                    obj.OrderDocumentNo = Util.GetValueOfString(ds.Tables[0].Rows[i]["OrderDocumentLineNo"]);
                    obj.InvoiceDocumentNo = Util.GetValueOfString(ds.Tables[0].Rows[i]["InvoiceDocumentLineNo"]);
                    obj.GRNDocumentNo = Util.GetValueOfString(ds.Tables[0].Rows[i]["InoutDocumentLineNo"]);
                    obj.Qty = Util.GetValueOfDecimal(ds.Tables[0].Rows[i]["Qty"]);
                    obj.ProductName = Util.GetValueOfString(ds.Tables[0].Rows[i]["ProductName"]);
                    obj.AttributeSetInstance = Util.GetValueOfString(ds.Tables[0].Rows[i]["AttributeSetInstance"]);
                    tabPanels.Add(obj);
                }
            }
            return tabPanels;
        }

        /// <summary>
        /// 16/1/2024 This function is Used to Get the Invoice tax data 
        /// </summary>
        /// <param name="ctx">Context</param>
        /// <param name="InvoiceId">Invoice ID</param>
        /// <Author>VIS_427 Devops ID: 4261</Author>
        /// <returns>returns the Invoice tax data</returns>
        public List<TaxTabPanel> GetInvoiceTaxData(Ctx ctx, int InvoiceId)
        {
            List<TaxTabPanel> InvocieTaxTabPanel = new List<TaxTabPanel>();
            String sql = @"SELECT t.Name,ct.TaxAmt,ct.TaxBaseAmt,ct.IsTaxIncluded,cy.StdPrecision FROM C_InvoiceTax ct 
                          INNER JOIN C_Invoice ci ON (ci.C_Invoice_ID = ct.C_Invoice_ID) 
                          INNER JOIN C_Tax t ON (t.C_Tax_ID = ct.C_Tax_ID) 
                          INNER JOIN C_Currency cy ON (cy.C_Currency_ID = ci.C_Currency_ID) WHERE ct.C_Invoice_ID = " + InvoiceId + " Order By t.Name";

            DataSet ds = DB.ExecuteDataset(sql, null, null);
            if (ds != null && ds.Tables[0].Rows.Count > 0)
            {
                for (int i = 0; i < ds.Tables[0].Rows.Count; i++)
                {
                    TaxTabPanel obj = new TaxTabPanel();
                    obj.TaxName = Util.GetValueOfString(ds.Tables[0].Rows[i]["Name"]);
                    obj.TaxPaybleAmt = Util.GetValueOfDecimal(ds.Tables[0].Rows[i]["TaxBaseAmt"]);
                    obj.TaxAmt = Util.GetValueOfDecimal(ds.Tables[0].Rows[i]["TaxAmt"]);
                    obj.IsTaxIncluded = Util.GetValueOfString(ds.Tables[0].Rows[i]["IsTaxIncluded"]);
                    obj.stdPrecision = Util.GetValueOfInt(ds.Tables[0].Rows[i]["StdPrecision"]);
                    InvocieTaxTabPanel.Add(obj);
                }
            }
            return InvocieTaxTabPanel;
        }

        /// <summary>
        /// 16/2/2024 This function is Used to Get the Order tax data 
        /// </summary>
        /// <param name="ctx">Context</param>
        /// <param name="OrderId">Order ID</param>
        /// <Author>VAI051:- Devops ID:</Author>
        /// <returns>returns the Order tax data</returns>
        public List<PurchaseOrderTabPanel> GetPurchaseOrderTaxData(Ctx ctx, int OrderId)
        {
            List<PurchaseOrderTabPanel> PurchaseOrderTabPanel = new List<PurchaseOrderTabPanel>();
            String sql = @"SELECT t.Name,ct.TaxAmt,ct.TaxBaseAmt,ct.IsTaxIncluded,cy.StdPrecision FROM C_OrderTax ct 
                          INNER JOIN C_Order ci ON (ci.C_Order_ID = ct.C_Order_ID) 
                          INNER JOIN C_Tax t ON (t.C_Tax_ID = ct.C_Tax_ID) 
                          INNER JOIN C_Currency cy ON (cy.C_Currency_ID = ci.C_Currency_ID) WHERE ct.C_Order_ID = " + OrderId + " Order By t.Name";

            DataSet ds = DB.ExecuteDataset(sql, null, null);
            if (ds != null && ds.Tables[0].Rows.Count > 0)
            {
                for (int i = 0; i < ds.Tables[0].Rows.Count; i++)
                {
                    PurchaseOrderTabPanel obj = new PurchaseOrderTabPanel();
                    obj.TaxName = Util.GetValueOfString(ds.Tables[0].Rows[i]["Name"]);
                    obj.TaxPaybleAmt = Util.GetValueOfDecimal(ds.Tables[0].Rows[i]["TaxBaseAmt"]);
                    obj.TaxAmt = Util.GetValueOfDecimal(ds.Tables[0].Rows[i]["TaxAmt"]);
                    obj.IsTaxIncluded = Util.GetValueOfString(ds.Tables[0].Rows[i]["IsTaxIncluded"]);
                    obj.stdPrecision = Util.GetValueOfInt(ds.Tables[0].Rows[i]["StdPrecision"]);
                    PurchaseOrderTabPanel.Add(obj);
                }
            }
            return PurchaseOrderTabPanel;
        }
        /// <summary>
        /// 20/2/2024 This function is Used to Get the Order tax data 
        /// </summary>
        /// <param name="ctx">Context</param>
        /// <param name="OrderLineId">Order ID</param>
        /// <Author>VAI051:- Devops ID:</Author>
        /// <returns>returns the Order tax data</returns>
        public List<LineHistoryTabPanel>GetLineHistoryTabPanel(Ctx ctx, int OrderLineID)
        {
            List<LineHistoryTabPanel> LineHistoryTabPanel = new List<LineHistoryTabPanel>();
            String sql = @"SELECT ol.DateOrdered,ol.DatePromised,ol.Line,p.Name AS Product,c.Name AS Charge,u.Name AS UOM,ol.QtyEntered,ol.QtyOrdered,ol.PriceEntered,ol.PriceActual,
                          ol.PriceList,t.Name AS Tax,ol.Discount,ol.LineNetAmt,ol.Description FROM C_OrderLineHistory ol
                          LEFT JOIN M_Product p ON p.M_Product_ID=ol.M_Product_ID
                          LEFT JOIN C_Charge c ON c.C_Charge_ID=ol.C_Charge_ID
                          LEFT JOIN C_UOM u ON u.C_UOM_ID=ol.C_UOM_ID
                          INNER JOIN C_Tax t ON t.C_Tax_ID=ol.C_Tax_ID
                          WHERE ol.C_OrderLine_ID = " + OrderLineID + " Order By t.Name";
             DataSet ds = DB.ExecuteDataset(sql, null, null);
            if (ds != null && ds.Tables.Count > 0 && ds.Tables[0].Rows.Count > 0)
            {
                for (int i = 0; i < ds.Tables[0].Rows.Count; i++)
                {
                    LineHistoryTabPanel obj = new LineHistoryTabPanel();
                    obj.LineNo = Util.GetValueOfInt(ds.Tables[0].Rows[i]["Line"]);
                    obj.DateOrdered = Util.GetValueOfDateTime(ds.Tables[0].Rows[i]["DateOrdered"]);
                    obj.DatePromised = Util.GetValueOfDateTime(ds.Tables[0].Rows[i]["DatePromised"]);
                    obj.Product = Util.GetValueOfString(ds.Tables[0].Rows[i]["Product"]);
                    obj.Charge = Util.GetValueOfString(ds.Tables[0].Rows[i]["Charge"]);
                    obj.Quantity = Util.GetValueOfDecimal(ds.Tables[0].Rows[i]["QtyEntered"]);
                    obj.UOM = Util.GetValueOfString(ds.Tables[0].Rows[i]["UOM"]);
                    obj.QuantityOrdered = Util.GetValueOfDecimal(ds.Tables[0].Rows[i]["QtyOrdered"]);
                    obj.Price = Util.GetValueOfDecimal(ds.Tables[0].Rows[i]["PriceEntered"]);
                    obj.UnitPrice = Util.GetValueOfDecimal(ds.Tables[0].Rows[i]["PriceActual"]);
                    obj.ListPrice = Util.GetValueOfDecimal(ds.Tables[0].Rows[i]["PriceList"]);
                    obj.Tax =    Util.GetValueOfString(ds.Tables[0].Rows[i]["Tax"]);
                    obj.Discount = Util.GetValueOfDecimal(ds.Tables[0].Rows[i]["Discount"]);
                    obj.LineAmount= Util.GetValueOfDecimal(ds.Tables[0].Rows[i]["LineNetAmt"]);
                    obj.Description= Util.GetValueOfString(ds.Tables[0].Rows[i]["Description"]);

                    LineHistoryTabPanel.Add(obj);
                }
            }
            return LineHistoryTabPanel;
        }

    }
    public class TabPanel
    {
        public string OrderDocumentNo { get; set; }

        public string GRNDocumentNo { get; set; }

        public Decimal Qty { get; set; }

        public string InvoiceDocumentNo { get; set; }

        public string ProductName { get; set; }

        public string AttributeSetInstance { get; set; }

    }

    public class TaxTabPanel
    {
        public string TaxName { get; set; }

        public decimal TaxPaybleAmt { get; set; }

        public decimal TaxAmt { get; set; }

        public string IsTaxIncluded { get; set; }

        public int stdPrecision { get; set; }

    }
    public class PurchaseOrderTabPanel
    {
        public string TaxName { get; set; }

        public decimal TaxPaybleAmt { get; set; }

        public decimal TaxAmt { get; set; }

        public string IsTaxIncluded { get; set; }

        public int stdPrecision { get; set; }
    }

    public class LineHistoryTabPanel
    {
        public int LineNo { get; set; }

        public DateTime? DateOrdered{ get; set; }

        public DateTime? DatePromised { get; set; }

        public string Product {get;set;}

        public string Charge { get; set; }

        public decimal Quantity { get; set; }

        public string UOM { get; set; }

        public decimal QuantityOrdered { get; set; }

        public decimal Price { get; set; }

        public decimal ListPrice { get; set; }

        public decimal  UnitPrice { get; set; }

        public string Tax { get; set; }

        public decimal Discount { get; set; }

        public decimal LineAmount { get; set; }

        public  string Description { get; set; }

    }
}
