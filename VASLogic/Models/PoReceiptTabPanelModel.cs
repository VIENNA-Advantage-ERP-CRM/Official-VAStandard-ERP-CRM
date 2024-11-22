/*******************************************************
       * Module Name    : VASLogic
       * Purpose        : Tab Panel For AP Matched PO and MatchedReceipt
       * chronological  : Development
       * Created Date   : 12 January 2024
       * Created by     : VAI066
      ******************************************************/

using System;
using System.Collections.Generic;
using System.Data;
using System.Dynamic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using VAdvantage.Utility;
using VAdvantage.Common;
using VAdvantage.Model;
using VAdvantage.DataBase;

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

            DataSet ds = DB.ExecuteDataset(sql, null, null);
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
            //Fixed query to get data for subtotal and grandtotal
            List<TaxTabPanel> InvocieTaxTabPanel = new List<TaxTabPanel>();
            String sql = @"SELECT t.Name,ct.TaxAmt,ct.TaxBaseAmt,ct.IsTaxIncluded, ci.TotalLines, ci.GrandTotal,SUM(cl.TaxBaseAmt) AS SumAmt, cy.CurSymbol,cy.StdPrecision FROM C_InvoiceTax ct 
                          INNER JOIN C_Invoice ci ON (ci.C_Invoice_ID = ct.C_Invoice_ID) 
                          INNER JOIN C_InvoiceLine cl ON (cl.C_Invoice_ID=ci.C_Invoice_ID)
                          INNER JOIN C_Tax t ON (t.C_Tax_ID = ct.C_Tax_ID) 
                          INNER JOIN C_Currency cy ON (cy.C_Currency_ID = ci.C_Currency_ID) WHERE ct.C_Invoice_ID = " + InvoiceId + " " +
                          " GROUP BY t.Name,ct.TaxAmt,ct.TaxBaseAmt,ct.IsTaxIncluded, ci.TotalLines, ci.GrandTotal,cy.CurSymbol,cy.StdPrecision Order By t.Name";

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
                    obj.CurSymbol = Util.GetValueOfString(ds.Tables[0].Rows[i]["CurSymbol"]);
                    obj.TotalLines = Util.GetValueOfDecimal(ds.Tables[0].Rows[i]["SumAmt"]); ;
                    obj.GrandTotal = Util.GetValueOfDecimal(ds.Tables[0].Rows[i]["GrandTotal"]);
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
            String sql = @"SELECT t.Name,(ci.DocumentNo || '_' || ci.DateOrdered) AS DocumentNo ,ct.TaxAmt,ct.TaxBaseAmt,ct.IsTaxIncluded,cy.StdPrecision FROM C_OrderTax ct 
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
                    obj.DocumentNo = Util.GetValueOfString(ds.Tables[0].Rows[i]["DocumentNo"]);
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
        public List<LineHistoryTabPanel> GetLineHistoryTabPanel(Ctx ctx, int OrderLineID)
        {
            List<LineHistoryTabPanel> LineHistoryTabPanel = new List<LineHistoryTabPanel>();
            String sql = @"SELECT ol.DateOrdered,ol.DatePromised,ol.Line,p.Name AS Product,c.Name AS Charge,u.Name AS UOM,ol.QtyEntered,ol.QtyOrdered,ol.PriceEntered,ol.PriceActual,
                          ol.PriceList,t.Name AS Tax,ol.Discount,ol.LineNetAmt,ol.Description,cy.StdPrecision FROM C_OrderLineHistory ol
                         INNER JOIN C_OrderLine o ON o.C_OrderLine_ID = ol.C_OrderLine_ID 
                          LEFT JOIN M_Product p ON p.M_Product_ID=ol.M_Product_ID
                          LEFT JOIN C_Charge c ON c.C_Charge_ID=ol.C_Charge_ID
                          LEFT JOIN C_UOM u ON u.C_UOM_ID=ol.C_UOM_ID
                          INNER JOIN C_Tax t ON t.C_Tax_ID=ol.C_Tax_ID
                            INNER JOIN C_Currency cy ON (cy.C_Currency_ID = o.C_Currency_ID)
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
                    obj.Tax = Util.GetValueOfString(ds.Tables[0].Rows[i]["Tax"]);
                    obj.Discount = Util.GetValueOfDecimal(ds.Tables[0].Rows[i]["Discount"]);
                    obj.LineAmount = Util.GetValueOfDecimal(ds.Tables[0].Rows[i]["LineNetAmt"]);
                    obj.Description = Util.GetValueOfString(ds.Tables[0].Rows[i]["Description"]);
                    obj.stdPrecision = Util.GetValueOfInt(ds.Tables[0].Rows[i]["StdPrecision"]);

                    LineHistoryTabPanel.Add(obj);
                }
            }
            return LineHistoryTabPanel;
        }

        /// <summary>
        /// VAI050-Get Purchase Order Lines
        /// </summary>
        /// <param name="ctx"></param>
        /// <param name="OrderID"></param>
        /// <returns></returns>
        public List<dynamic> GetPOLineData(Ctx ctx, int OrderID)
        {

            string sql = @"WITH StatusAndUPCData AS (SELECT ol.C_OrderLine_ID, i.ImageUrl, cu.Name AS UOM, p.Name AS ProductName,
                        CASE WHEN NVL(ol.M_AttributeSetInstance_ID, 0) > 0 THEN ma.Description ELSE NULL END AS AttributeName,
                        CASE WHEN (ol.QtyInvoiced = 0 AND ol.QtyDelivered = 0) THEN 'OP' 
                        WHEN (ol.QtyInvoiced = ol.QtyOrdered AND ol.QtyDelivered = ol.QtyOrdered) THEN 'DI'
                        WHEN (ol.QtyInvoiced = ol.QtyOrdered) THEN CASE WHEN (ol.QtyDelivered > 0 AND ol.QtyDelivered < ol.QtyOrdered)
                        THEN 'PF' ELSE 'IN' END 
                        WHEN (ol.QtyInvoiced > 0 AND ol.QtyInvoiced < ol.QtyOrdered) THEN CASE WHEN (ol.QtyDelivered = ol.QtyOrdered)
                        THEN 'FP' ELSE 'PI' END
                        WHEN (ol.QtyDelivered = ol.QtyOrdered) THEN 'DE'
                        WHEN (ol.QtyDelivered > 0 AND ol.QtyDelivered < ol.QtyOrdered) THEN 'PD' END AS OrderLineStatusValue,
                        o.VAS_OrderStatus AS OrderStatusValue,
                        COALESCE(attr.UPC, cuconv.UPC, p.UPC,p.Value) AS PreferredUPC,
                        ROW_NUMBER() OVER (PARTITION BY ol.C_OrderLine_ID ORDER BY
                        CASE WHEN attr.UPC IS NOT NULL THEN 1 WHEN cuconv.UPC IS NOT NULL THEN 2 ELSE 3 END) AS rn,              
                        CASE WHEN p.C_UOM_ID !=  ol.C_UOM_ID  THEN ROUND(ol.QtyDelivered/NULLIF(cuconv.dividerate, 0), 2)
                        ELSE ol.QtyDelivered END AS QtyDelivered
                        FROM C_OrderLine ol INNER JOIN C_Order o ON (ol.C_Order_ID = o.C_Order_ID)
                        INNER JOIN M_Product p ON (ol.M_Product_ID = p.M_Product_ID)
                        INNER JOIN C_UOM cu ON (cu.C_UOM_ID = ol.C_UOM_ID)
                        LEFT JOIN M_AttributeSetInstance ma ON (ma.M_AttributeSetInstance_ID = ol.M_AttributeSetInstance_ID)
                        LEFT JOIN AD_Image i ON (i.AD_Image_ID = p.AD_Image_ID)
                        LEFT JOIN M_ProductAttributes attr ON (attr.M_AttributeSetInstance_ID = ol.M_AttributeSetInstance_ID
                        AND attr.M_Product_ID = ol.M_Product_ID AND attr.C_UOM_ID = ol.C_UOM_ID AND attr.UPC IS NOT NULL)
                        LEFT JOIN C_UOM_Conversion cuconv ON (cuconv.C_UOM_ID = p.C_UOM_ID AND cuconv.C_UOM_To_ID = ol.C_UOM_ID
                        AND cuconv.M_Product_ID = ol.M_Product_ID) WHERE ol.C_Order_ID = " + OrderID + @")
                        SELECT sod.C_OrderLine_ID,sod.ImageUrl,sod.UOM, ol.QtyEntered AS QtyOrdered, sod.QtyDelivered,sod.PreferredUPC AS UPC,
                        sod.OrderLineStatusValue, arl.Name As OrderLineStatus, sod.OrderStatusValue, arlOrder.Name AS OrderStatus,
                        sod.ProductName, sod.AttributeName
                        FROM StatusAndUPCData sod
                        INNER JOIN C_OrderLine ol ON (sod.C_OrderLine_ID = ol.C_OrderLine_ID)
                        LEFT JOIN AD_Reference ar ON (ar.Name = 'VAS_OrderStatus')
                        LEFT JOIN AD_Ref_List arl ON (arl.AD_Reference_ID = ar.AD_Reference_ID
                        AND arl.Value = sod.OrderLineStatusValue)
                        LEFT JOIN AD_Ref_List arlOrder ON (arlOrder.AD_Reference_ID = ar.AD_Reference_ID
                        AND arlOrder.Value = sod.OrderStatusValue)
                        WHERE sod.rn = 1 ORDER BY ol.Line";
            DataSet ds = DB.ExecuteDataset(sql, null, null);
            if (ds != null && ds.Tables.Count > 0 && ds.Tables[0].Rows.Count > 0)
            {
                List<dynamic> POLines = new List<dynamic>();
                for (int i = 0; i < ds.Tables[0].Rows.Count; i++)
                {
                    dynamic obj = new ExpandoObject();
                    obj.ImageUrl = Util.GetValueOfString(ds.Tables[0].Rows[i]["ImageUrl"]);
                    obj.ProductName = Util.GetValueOfString(ds.Tables[0].Rows[i]["ProductName"]);
                    obj.UOM = Util.GetValueOfString(ds.Tables[0].Rows[i]["UOM"]);
                    obj.UPC = Util.GetValueOfString(ds.Tables[0].Rows[i]["UPC"]);
                    obj.OrderLineStatusValue = Util.GetValueOfString(ds.Tables[0].Rows[i]["OrderLineStatusValue"]);
                    obj.OrderLineStatus = Util.GetValueOfString(ds.Tables[0].Rows[i]["OrderLineStatus"]);
                    obj.OrderStatusValue = Util.GetValueOfString(ds.Tables[0].Rows[i]["OrderStatusValue"]);
                    obj.OrderStatus = Util.GetValueOfString(ds.Tables[0].Rows[i]["OrderStatus"]);
                    obj.AttributeName = Util.GetValueOfString(ds.Tables[0].Rows[i]["AttributeName"]);
                    obj.QtyOrdered = Util.GetValueOfDecimal(ds.Tables[0].Rows[i]["QtyOrdered"]);
                    obj.QtyDelivered = Util.GetValueOfDecimal(ds.Tables[0].Rows[i]["QtyDelivered"]);
                    POLines.Add(obj);
                }
                return POLines;
            }
            return null;
        }
        /// <summary>
        /// VIS-383: 26/07/24:- Get invoice detail based on invoice line
        /// </summary>
        /// <author>VIS-383</author>
        /// <param name="ctx">Context</param>
        /// <param name="InvoiceLineId">Invoice Line ID</param>
        /// <param name="AdWindowID">Window ID</param>
        /// <returns>Invoice details</returns>
        public string GetInvoiceLineReport(Ctx ctx, int InvoiceId, int AD_WindowID)
        {
            string path = "";
            //Get invoice table id based on table name
            int AD_Table_ID = Util.GetValueOfInt(DB.ExecuteScalar(@"SELECT ad_table_id FROM  ad_table WHERE tablename = 'C_Invoice'"));
            string sql = @"SELECT ad_tab.ad_process_id, ad_process.value FROM ad_tab
                            INNER JOIN ad_process ON(ad_tab.ad_process_id = ad_process.ad_process_id)
                            WHERE ad_tab.name = 'Invoice'
                            AND ad_tab.ad_window_id =" + AD_WindowID;
            DataSet ds = DB.ExecuteDataset(sql);
            if (ds != null && ds.Tables[0].Rows.Count > 0)
            {
                int ReportProcess_ID = Util.GetValueOfInt(ds.Tables[0].Rows[0]["ad_process_id"]);
                if (ReportProcess_ID > 0)
                {
                    Common Com = new Common();
                    Dictionary<string, object> d = new Dictionary<string, object>();
                    byte[] pdfReport;
                    string reportPath = "";
                    d = Com.GetReport(ctx, ReportProcess_ID, Util.GetValueOfString(ds.Tables[0].Rows[0]["value"]), AD_Table_ID, InvoiceId, 0, "", "P", out pdfReport, out reportPath);

                    if (pdfReport != null)
                    {
                        path = reportPath.Substring(reportPath.IndexOf("TempDownload"));
                    }
                }
            }
            return path;
        }

        /// <summary>
        /// 08/08/2024 This function is Used to Get the UnAllocated Payment data for particular business partner
        /// </summary>
        /// <param name="ctx">Context</param>
        /// <param name="C_BPartner_ID">Business Partner ID</param>
        /// <param name="AD_Org_ID">AD_Org_ID</param>
        /// <param name="IsSoTrx">IsSoTrx</param>
        /// <Author>VIS_427</Author>
        /// <returns>returns UnAllocated Payment data for particular business partner</returns>
        public List<UnAllocatedPayTabPanel> GetUnAllocatedPayData(Ctx ctx, int C_BPartner_ID, string IsSoTrx, int AD_Org_ID)
        {
            StringBuilder sql = new StringBuilder();
            List<UnAllocatedPayTabPanel> UnAllocatedTabPanel = new List<UnAllocatedPayTabPanel>();
            sql.Append(@"SELECT p.DateTrx,p.DateAcct, p.PayAmt,p.C_Payment_ID,p.AD_Org_ID,cy.StdPrecision,cy.ISO_Code,
                          p.DocumentNo,p.C_ConversionType_ID, SUM(currencyConvert(al.Amount,
                          ah.C_Currency_ID, p.C_Currency_ID,NVL(ah.DateAcct,ah.DateTrx),NVL(ah.C_ConversionType_ID,
                          p.C_ConversionType_ID), al.AD_Client_ID,al.AD_Org_ID)) AS AllocatedAmt
                          FROM C_Payment p
                          INNER JOIN C_Currency cy ON (cy.C_Currency_ID = p.C_Currency_ID) 
                          INNER JOIN C_Doctype doc ON (doc.C_DocType_ID=p.C_DocType_ID)
                          LEFT JOIN C_AllocationLine al ON (al.C_Payment_ID=p.C_Payment_ID)
                          LEFT JOIN C_AllocationHdr ah ON (al.C_AllocationHdr_ID=ah.C_AllocationHdr_ID AND ah.DocStatus IN ('CO','CL'))
                          WHERE p.IsAllocated='N'
                          AND p.Processed='Y' AND p.Processing ='N' AND p.DocStatus IN ('CO','CL')
                          AND p.C_BPartner_ID=" + C_BPartner_ID + " AND p.AD_Org_ID=" + AD_Org_ID);
            if (IsSoTrx == "true")
            {
                sql.Append(" AND doc.DocBaseType='ARR'");
            }
            else
            {
                sql.Append(" AND doc.DocBaseType='APP'");
            }
            sql.Append(@" GROUP BY p.DateTrx,
                         p.DateAcct,
                         p.PayAmt,
                         p.C_Payment_ID,
                         p.AD_Org_ID,
                         cy.StdPrecision,
                         cy.ISO_Code,
                         p.DocumentNo,
                         p.C_ConversionType_ID");
            sql.Append(" ORDER BY DateTrx ASC");
            string datasql = MRole.GetDefault(ctx).AddAccessSQL(sql.ToString(), "p", MRole.SQL_FULLYQUALIFIED, MRole.SQL_RO);
            DataSet ds = DB.ExecuteDataset(sql.ToString(), null, null);
            if (ds != null && ds.Tables[0].Rows.Count > 0)
            {
                int AD_Window_ID = Util.GetValueOfInt(DB.ExecuteScalar("SELECT AD_Window_ID FROM AD_Window WHERE Name='Payment'", null, null));
                for (int i = 0; i < ds.Tables[0].Rows.Count; i++)
                {
                    UnAllocatedPayTabPanel obj = new UnAllocatedPayTabPanel();
                    obj.AD_Org_ID = Util.GetValueOfInt(ds.Tables[0].Rows[i]["AD_Org_ID"]);
                    obj.C_Payment_ID = Util.GetValueOfInt(ds.Tables[0].Rows[i]["C_Payment_ID"]);
                    obj.PayAmt = Util.GetValueOfDecimal(ds.Tables[0].Rows[i]["PayAmt"]) - Math.Abs(Util.GetValueOfDecimal(ds.Tables[0].Rows[i]["AllocatedAmt"]));
                    obj.DateTrx = Util.GetValueOfDateTime(ds.Tables[0].Rows[i]["DateTrx"]);
                    obj.DateAcct = Util.GetValueOfDateTime(ds.Tables[0].Rows[i]["DateAcct"]);
                    obj.StdPrecision = Util.GetValueOfInt(ds.Tables[0].Rows[i]["StdPrecision"]);
                    obj.CurrencyName = Util.GetValueOfString(ds.Tables[0].Rows[i]["ISO_Code"]);
                    obj.DocumentNo = Util.GetValueOfString(ds.Tables[0].Rows[i]["DocumentNo"]);
                    obj.AD_Window_ID = AD_Window_ID;
                    obj.C_ConversionType_ID = Util.GetValueOfInt(ds.Tables[0].Rows[i]["C_ConversionType_ID"]);
                    UnAllocatedTabPanel.Add(obj);
                }
            }
            return UnAllocatedTabPanel;
        }

        /// <summary>
        /// This function is Used to Get the Order Total Summary data 
        /// </summary>
        /// <param name="ctx">Context</param>
        /// <param name="OrderId">Order ID</param>
        /// <returns>returns the Order Total Summary data</returns>
        public List<dynamic> GetOrderSummary(Ctx ctx, int OrderId)
        {
            List<dynamic> retData = new List<dynamic>();
            String sql = @"SELECT t.Name, ci.DocumentNo, ct.TaxAmt, ci.TotalLines, ci.GrandTotal, cy.CurSymbol, cy.StdPrecision
                          FROM C_OrderTax ct 
                          INNER JOIN C_Order ci ON (ci.C_Order_ID = ct.C_Order_ID) 
                          INNER JOIN C_Tax t ON (t.C_Tax_ID = ct.C_Tax_ID) 
                          INNER JOIN C_Currency cy ON (cy.C_Currency_ID = ci.C_Currency_ID) WHERE ct.C_Order_ID = " + OrderId + " Order By t.Name";

            DataSet ds = DB.ExecuteDataset(sql, null, null);
            if (ds != null && ds.Tables[0].Rows.Count > 0)
            {
                for (int i = 0; i < ds.Tables[0].Rows.Count; i++)
                {
                    dynamic obj = new ExpandoObject();
                    obj.TaxName = Util.GetValueOfString(ds.Tables[0].Rows[i]["Name"]);
                    obj.DocumentNo = Util.GetValueOfString(ds.Tables[0].Rows[i]["DocumentNo"]);
                    obj.TotalLines = Util.GetValueOfDecimal(ds.Tables[0].Rows[i]["TotalLines"]);
                    obj.TaxAmt = Util.GetValueOfDecimal(ds.Tables[0].Rows[i]["TaxAmt"]);
                    obj.GrandTotal = Util.GetValueOfDecimal(ds.Tables[0].Rows[i]["GrandTotal"]);
                    obj.CurSymbol = Util.GetValueOfString(ds.Tables[0].Rows[i]["CurSymbol"]);
                    obj.StdPrecision = Util.GetValueOfString(ds.Tables[0].Rows[i]["StdPrecision"]);
                    retData.Add(obj);
                }
            }
            return retData;
        }

        /// <summary>
        /// This function is Used to Get the AR Invoice Data for widget
        /// </summary>
        /// <param name="WidgetId">WidgetId</param>
        /// <param name="ctx">Context</param>
        /// <author>VIS_427</author>
        /// <returns>List of ar invoice data</returns>
        public List<ARInvWidgData> GetARInvSchData(Ctx ctx, bool ISOtrx)
        {
            ARInvWidgData obj = new ARInvWidgData();
            StringBuilder sql = new StringBuilder();
            var C_Currency_ID = ctx.GetContextAsInt("$C_Currency_ID");
            List<ARInvWidgData> ARInvWidgData = new List<ARInvWidgData>();
            string docBaseTypeARI_APT = ISOtrx ? "'ARI'" : "'API'";
            string docBaseTypeARC_APC = ISOtrx ? "'ARC'" : "'APC'";

            sql.Append($@"WITH InvoiceData AS (
                         {MRole.GetDefault(ctx).AddAccessSQL($@"SELECT
                             ci.AD_Client_ID,
                             cs.C_InvoicePaySchedule_ID,
                             cd.DocBaseType,
                             cs.DueDate AS DateInvoiced,
                             currencyConvert(cs.DueAmt ,cs.C_Currency_ID ," + C_Currency_ID + @",ci.DateAcct ,ci.C_ConversionType_ID ,cs.AD_Client_ID ,cs.AD_Org_ID ) AS DueAmt
                         FROM
                             C_Invoice ci
                             INNER JOIN C_InvoicePaySchedule cs ON (cs.C_Invoice_ID = ci.C_Invoice_ID)
                             INNER JOIN C_DocType cd ON (cd.C_DocType_ID = ci.C_DocTypeTarget_ID)
                             WHERE cd.DocBaseType IN ('ARI', 'ARC','API','APC') AND ci.DocStatus IN ('CO','CL') AND cs.VA009_IsPaid='N' AND cd.IsExpenseInvoice = 'N' ", "ci", MRole.SQL_FULLYQUALIFIED, MRole.SQL_RW
                     )})
                     SELECT
                         COUNT(C_InvoicePaySchedule_ID) AS countrec,
                                     NVL(
                                        (
                                            SUM(
                                                CASE WHEN DocBaseType = " + docBaseTypeARI_APT + @" THEN
                                                DueAmt
                                                ELSE 0
                                                END
                                            ) -
                                            SUM(
                                                CASE WHEN DocBaseType = " + docBaseTypeARC_APC + @" THEN
                                                DueAmt
                                                ELSE 0
                                                END
                                            )
                                        ),
                                        0
                                    ) AS total_dueamt
                     FROM
                         InvoiceData
                     WHERE
                         DateInvoiced <= Current_Date AND DateInvoiced >= Current_Date - 30
                     UNION ALL
                     SELECT
                         COUNT(C_InvoicePaySchedule_ID) AS countrec,
                          NVL(
                            (
                                SUM(
                                    CASE WHEN DocBaseType = " + docBaseTypeARI_APT + @" THEN
                                    DueAmt
                                    ELSE 0
                                    END
                                ) -
                                SUM(
                                    CASE WHEN DocBaseType = " + docBaseTypeARC_APC + @" THEN
                                    DueAmt
                                    ELSE 0
                                    END
                                )
                            ),
                            0
                        ) AS total_dueamt
                     FROM
                         InvoiceData
                     WHERE
                         DateInvoiced <= Current_Date - 31 AND DateInvoiced >= Current_Date - 60
                     UNION ALL
                     SELECT
                         COUNT(C_InvoicePaySchedule_ID) AS countrec,
                          NVL(
                            (
                                SUM(
                                    CASE WHEN DocBaseType = " + docBaseTypeARI_APT + @" THEN
                                    DueAmt
                                    ELSE 0
                                    END
                                ) -
                                SUM(
                                    CASE WHEN DocBaseType = " + docBaseTypeARC_APC + @" THEN
                                    DueAmt
                                    ELSE 0
                                    END
                                )
                            ),
                            0
                        ) AS total_dueamt
                     FROM
                         InvoiceData
                     WHERE
                        DateInvoiced <= Current_Date - 61 AND  DateInvoiced >= Current_Date - 90
                     UNION ALL
                     SELECT
                         COUNT(C_InvoicePaySchedule_ID) AS countrec,
                          NVL(
                            (
                                SUM(
                                    CASE WHEN DocBaseType = " + docBaseTypeARI_APT + @" THEN
                                    DueAmt
                                    ELSE 0
                                    END
                                ) -
                                SUM(
                                    CASE WHEN DocBaseType = " + docBaseTypeARC_APC + @" THEN
                                    DueAmt
                                    ELSE 0
                                    END
                                )
                            ),
                            0
                        ) AS total_dueamt
                     FROM
                         InvoiceData
                     WHERE
                        DateInvoiced <= Current_Date - 91 AND  DateInvoiced >= Current_Date - 120
                     UNION ALL
                     SELECT
                         COUNT(C_InvoicePaySchedule_ID) AS countrec,
                         NVL(
                            (
                                SUM(
                                    CASE WHEN DocBaseType = " + docBaseTypeARI_APT + @" THEN
                                    DueAmt
                                    ELSE 0
                                    END
                                ) -
                                SUM(
                                    CASE WHEN DocBaseType = " + docBaseTypeARC_APC + @" THEN
                                    DueAmt
                                    ELSE 0
                                    END
                                )
                            ),
                            0
                        ) AS total_dueamt
                     FROM
                         InvoiceData
                     WHERE DateInvoiced <= Current_Date - 120");
            DataSet ds = DB.ExecuteDataset(sql.ToString(), null, null);
            if (ds != null && ds.Tables[0].Rows.Count > 0)
            {

                sql.Clear();
                sql.Append(@"SELECT cy.StdPrecision,CASE WHEN Cy.Cursymbol IS NOT NULL THEN Cy.Cursymbol ELSE Cy.ISO_Code END AS Symbol
                     FROM AD_Client ac
                     INNER JOIN AD_ClientInfo aci ON (ac.AD_Client_ID=aci.AD_Client_ID)
                     INNER JOIN C_AcctSchema ca ON (ca.C_AcctSchema_ID=aci.C_AcctSchema1_ID)
                     INNER JOIN C_Currency cy ON (cy.C_Currency_ID=ca.C_Currency_ID)
                     WHERE ac.AD_Client_ID IN (" + ctx.GetAD_Client_ID() + ")");
                DataSet dsCurrency = DB.ExecuteDataset(sql.ToString(), null, null);
                decimal TotalAmt = 0;

                for (int i = 0; i < ds.Tables[0].Rows.Count; i++)
                {
                    TotalAmt = TotalAmt + Util.GetValueOfDecimal(ds.Tables[0].Rows[i]["total_dueamt"]);
                    obj = new ARInvWidgData();
                    obj.daysAmt = Util.GetValueOfDecimal(ds.Tables[0].Rows[i]["total_dueamt"]);
                    obj.Symbol = Util.GetValueOfString(dsCurrency.Tables[0].Rows[0]["Symbol"]);
                    obj.stdPrecision = Util.GetValueOfInt(dsCurrency.Tables[0].Rows[0]["StdPrecision"]);
                    ARInvWidgData.Add(obj);
                }
                obj = new ARInvWidgData();
                obj.arTotalAmtWidget = new List<ArTotalAmtWidget>();
                ArTotalAmtWidget objAmt = new ArTotalAmtWidget();
                objAmt.totalAmt = TotalAmt;
                obj.arTotalAmtWidget.Add(objAmt);
                ARInvWidgData.Add(obj);
            }
            return ARInvWidgData;
        }

        /// <summary>
        /// This function is Used to Get the ar/ap invoice data of top five business partners
        /// </summary>
        /// <param name="ISOtrx">ISOtrx</param>
        /// <param name="ListValue">ListValue</param>
        /// <param name="ctx">Context</param>
        /// <author>VIS_427</author>
        /// <returns>List of ar/ap invoice data of top five business partners</returns>
        public List<InvGrandTotalData> GetInvTotalGrandData(Ctx ctx, bool ISOtrx, string ListValue)
        {
            InvGrandTotalData obj = new InvGrandTotalData(); ;
            StringBuilder sql = new StringBuilder();
            List<InvGrandTotalData> invGrandTotalData = new List<InvGrandTotalData>();
            string BPCheck = (ISOtrx == true ? "cb.IsCustomer='Y'" : "cb.IsVendor='Y'");
            var C_Currency_ID = ctx.GetContextAsInt("$C_Currency_ID");
            int calendar_ID = 0;
            int StartYear = 0;
            int CurrentYear = 0;
            //Finding the calender id and Current Year to get data on this basis
            sql.Append(@"SELECT
                         DISTINCT cy.CalendarYears,CASE WHEN oi.C_Calendar_ID IS NOT NULL THEN oi.C_Calendar_ID
                         else ci.C_Calendar_ID END AS C_Calendar_ID
                         FROM C_Calendar cc
                         INNER JOIN AD_ClientInfo ci ON (ci.C_Calendar_ID=cc.C_Calendar_ID)
                         LEFT JOIN AD_OrgInfo oi ON (oi.C_Calendar_ID=cc.C_Calendar_ID)
                         INNER JOIN C_Year cy ON (cy.C_Calendar_ID=cc.C_Calendar_ID)
                         INNER JOIN C_Period cp  ON (cy.C_Year_ID = cp.C_Year_ID)
                         WHERE 
                         cy.IsActive = 'Y'
                         AND cp.IsActive = 'Y'
                         AND ci.IsActive='Y'
                         AND TRUNC(CURRENT_DATE) BETWEEN cp.StartDate AND cp.EndDate AND cc.AD_Client_ID=" + ctx.GetAD_Client_ID());
            // string yearSql = MRole.GetDefault(ctx).AddAccessSQL(sql.ToString(), "cc", MRole.SQL_FULLYQUALIFIED, MRole.SQL_RW);
            DataSet yearDs = DB.ExecuteDataset(sql.ToString(), null, null);
            if (yearDs != null && yearDs.Tables[0].Rows.Count > 0)
            {
                CurrentYear = Util.GetValueOfInt(yearDs.Tables[0].Rows[0]["CalendarYears"]);
                calendar_ID = Util.GetValueOfInt(yearDs.Tables[0].Rows[0]["C_Calendar_ID"]);
            }
            sql.Clear();
            sql.Append($@"WITH InvoiceData AS (
                        {MRole.GetDefault(ctx).AddAccessSQL($@"SELECT
                             cb.Name,
                             cd.DocBaseType,
                             cb.Pic,
                             custimg.ImageExtension AS custImgExtension,
                             ci.AD_Client_ID,
                             ci.DateInvoiced,
                             currencyConvert(ci.grandtotalafterwithholding ,ci.C_Currency_ID ," + C_Currency_ID + @",ci.DateAcct ,ci.C_ConversionType_ID ,ci.AD_Client_ID ,ci.AD_Org_ID ) AS DueAmt
                         FROM
                             C_Invoice ci
                             INNER JOIN C_BPartner cb ON (cb.C_BPartner_ID = ci.C_BPartner_ID)
                             INNER JOIN C_DocType cd ON (cd.C_DocType_ID = ci.C_DocTypeTarget_ID)
                             LEFT OUTER JOIN AD_Image custimg ON (custimg.AD_Image_ID = CAST(cb.Pic AS INT))
                             WHERE cd.DocBaseType IN ('ARI', 'ARC','API','APC') AND ci.DocStatus IN ('CO','CL') AND " + BPCheck, "ci", MRole.SQL_FULLYQUALIFIED, MRole.SQL_RW
                     )})");
            sql.Append(@",PeriodDetail AS (SELECT c_period.AD_Client_ID,Min(c_period.StartDate) AS StartDate,Max(c_period.EndDate) AS EndDate  FROM C_Year INNER JOIN C_Period on (C_Year.C_Year_ID=c_period.C_Year_ID) WHERE ");
            //Getting data according to Current month
            if (ListValue == "CM")
            {
                sql.Append(@" c_year.C_Calendar_ID =" + calendar_ID +
                            @" AND c_year.IsActive = 'Y' AND C_period.IsActive='Y'
                            AND CURRENT_DATE BETWEEN C_period.StartDate AND C_period.EndDate");
            }
            //Getting data according to Current Year
            else if (ListValue == "CY")
            {
                sql.Append(@" c_year.C_Calendar_ID =" + calendar_ID +
                            @" AND c_year.IsActive = 'Y' AND C_period.IsActive='Y'
                            AND C_Year.CALENDARYEARS='" + CurrentYear + "'");
            }
            //Getting data according to Last Year
            else if (ListValue == "LY")
            {
                CurrentYear = CurrentYear - 1;
                sql.Append(@" c_year.C_Calendar_ID =" + calendar_ID +
                            @" AND c_year.IsActive = 'Y' AND C_period.IsActive='Y'
                            AND C_Year.CALENDARYEARS='" + CurrentYear + "'");
            }
            //Getting data according to Last 3 Year
            else if (ListValue == "3Y")
            {
                StartYear = CurrentYear - 3;
                CurrentYear = CurrentYear - 1;
                sql.Append(@" c_year.C_Calendar_ID =" + calendar_ID +
                            @" AND c_year.IsActive = 'Y' AND C_period.IsActive='Y'
                            AND C_Year.CALENDARYEARS BETWEEN '" + StartYear + "' AND '" + CurrentYear + "'");
            }
            //Getting data according to Last 5 Year
            else if (ListValue == "5Y")
            {
                StartYear = CurrentYear - 5;
                CurrentYear = CurrentYear - 1;
                sql.Append(@" c_year.C_Calendar_ID =" + calendar_ID +
                            @" AND c_year.IsActive = 'Y' AND C_period.IsActive='Y'
                            AND C_Year.CALENDARYEARS BETWEEN '" + StartYear + "' AND '" + CurrentYear + "'");
            }
            sql.Append(@" GROUP BY c_period.AD_Client_ID)");
            sql.Append(@"SELECT
                         id.Name,
                         id.Pic,
                         id.custImgExtension,
                         min(id.DateInvoiced) AS minDateInvoiced,");
            if (ISOtrx)
            {
                sql.Append(@"NVL((SUM(CASE WHEN id.DocBaseType = 'ARI' THEN id.DueAmt ELSE 0 END) -
                             SUM(CASE WHEN id.DocBaseType = 'ARC' THEN id.DueAmt ELSE 0 END)),0) AS SumAmount");
            }
            else
            {
                sql.Append(@"NVL((SUM(CASE WHEN id.DocBaseType = 'API' THEN id.DueAmt ELSE 0 END) -
                          SUM(CASE WHEN id.DocBaseType = 'APC' THEN id.DueAmt ELSE 0 END)),0) AS SumAmount");
            }
            sql.Append(@" FROM
                         InvoiceData id 
                         INNER JOIN PeriodDetail pd ON (pd.AD_Client_ID=id.AD_Client_ID)
                     WHERE id.dateinvoiced BETWEEN pd.StartDate AND pd.EndDate
                     Group by id.Name,id.Pic,id.custImgExtension
                     Order by SumAmount desc 
                     FETCH FIRST 5 ROWS ONLY");
            DataSet ds = DB.ExecuteDataset(sql.ToString(), null, null);
            if (ds != null && ds.Tables[0].Rows.Count > 0)
            {

                sql.Clear();
                sql.Append(@"SELECT CASE WHEN Cursymbol IS NOT NULL THEN Cursymbol ELSE ISO_Code END AS Symbol,StdPrecision FROM C_Currency WHERE C_Currency_ID=" + C_Currency_ID);
                DataSet dsCurrency = DB.ExecuteDataset(sql.ToString(), null, null);

                for (int i = 0; i < ds.Tables[0].Rows.Count; i++)
                {
                    obj = new InvGrandTotalData();
                    obj.GrandTotalAmt = Util.GetValueOfDecimal(ds.Tables[0].Rows[i]["SumAmount"]);
                    obj.Symbol = Util.GetValueOfString(dsCurrency.Tables[0].Rows[0]["Symbol"]);
                    obj.stdPrecision = Util.GetValueOfInt(dsCurrency.Tables[0].Rows[0]["StdPrecision"]);
                    obj.SinceDate = Util.GetValueOfDateTime(ds.Tables[0].Rows[i]["minDateInvoiced"]).Value;
                    obj.Name = Util.GetValueOfString(ds.Tables[0].Rows[i]["Name"]);
                    if (Util.GetValueOfInt(ds.Tables[0].Rows[i]["Pic"]) != 0)
                    {
                        obj.ImageUrl = "Images/Thumb46x46/" + Util.GetValueOfInt(ds.Tables[0].Rows[i]["Pic"]) + Util.GetValueOfString(ds.Tables[0].Rows[i]["custImgExtension"]);

                    }
                    invGrandTotalData.Add(obj);
                }
            }
            return invGrandTotalData;
        }
        /// <summary>
        /// This function is Used to Amount which are in diffenernt states from AP/AR Screens
        /// </summary>
        /// <param name="ISOtrx">ISOtrx</param>
        /// <param name="ctx">Context</param>
        /// <author>VIS_427</author>
        /// <returns>List of Amount which are in diffenernt states from AP/AR Screens</returns>
        public List<PurchaseStateDetail> GetPurchaseStateDetail(Ctx ctx, bool ISOtrx)
        {
            PurchaseStateDetail obj = new PurchaseStateDetail(); ;
            StringBuilder sql = new StringBuilder();
            List<PurchaseStateDetail> invData = new List<PurchaseStateDetail>();
            var C_Currency_ID = ctx.GetContextAsInt("$C_Currency_ID");
            int calendar_ID = 0;
            string docBaseTypeARI_APT = ISOtrx ? "'ARI'" : "'API'";
            string docBaseTypeARC_APC = ISOtrx ? "'ARC'" : "'APC'";



            // Organization Calendar
            calendar_ID = Util.GetValueOfInt(DB.ExecuteScalar("SELECT C_Calendar_ID FROM AD_OrgInfo WHERE IsActive = 'Y' AND AD_Org_ID =" + ctx.GetAD_Org_ID(), null, null));
            if (calendar_ID == 0)
            {
                // Primary Calendar 
                calendar_ID = Util.GetValueOfInt(DB.ExecuteScalar(@"SELECT C_Calendar_ID FROM AD_ClientInfo WHERE 
                                    IsActive = 'Y' AND AD_Client_ID=" + ctx.GetAD_Client_ID(), null, null));
            }

            // Query for 'DueAmt'
            sql.Append(@"
             WITH PeriodDetail AS (
                 SELECT cp.StartDate, cp.EndDate, cp.AD_Client_ID
                 FROM C_Period cp
                 INNER JOIN C_Year cy ON (cy.C_Year_ID = cp.C_Year_ID)
                 WHERE TRUNC(CURRENT_DATE) BETWEEN cp.StartDate AND cp.EndDate
                 AND cp.IsActive='Y' AND cy.IsActive='Y' AND cy.C_Calendar_ID =" + calendar_ID + ")");

            sql.Append(MRole.GetDefault(ctx).AddAccessSQL($@" SELECT 'DueAmt' AS Type,
                    NVL(
                        (
                            SUM(
                                CASE WHEN cd.DocBaseType = " + docBaseTypeARI_APT + @"
                                THEN currencyConvert(cs.DueAmt, ci.C_Currency_ID, " + C_Currency_ID + @", ci.DateAcct, ci.C_ConversionType_ID, ci.AD_Client_ID, ci.AD_Org_ID)
                                ELSE 0
                                END
                            ) - 
                            SUM(
                                CASE WHEN cd.DocBaseType = " + docBaseTypeARC_APC + @"
                                THEN currencyConvert(cs.DueAmt, ci.C_Currency_ID, " + C_Currency_ID + @", ci.DateAcct, ci.C_ConversionType_ID, ci.AD_Client_ID, ci.AD_Org_ID)
                                ELSE 0
                                END
                            )
                        ), 
                        0
                    ) AS SumAmount
             FROM C_Invoice ci
             INNER JOIN C_InvoicePaySchedule cs ON (cs.C_Invoice_ID = ci.C_Invoice_ID)
             INNER JOIN C_DocType cd ON (cd.C_DocType_ID = ci.C_DocTypeTarget_ID)
             WHERE CURRENT_DATE > cs.DueDate
               AND cs.VA009_IsPaid = 'N'
               AND ci.DocStatus IN ('CO', 'CL') AND ci.IsInDispute = 'N'", "ci", MRole.SQL_FULLYQUALIFIED, MRole.SQL_RW));

            // Query for 'DueSoon'
            sql.Append(" UNION ALL ");
            sql.Append(MRole.GetDefault(ctx).AddAccessSQL($@"SELECT 'DueSoon',
                    NVL(
                        (
                            SUM(
                                CASE WHEN cd.DocBaseType = " + docBaseTypeARI_APT + @"
                                THEN currencyConvert(cs.DueAmt, ci.C_Currency_ID, " + C_Currency_ID + @", ci.DateAcct, ci.C_ConversionType_ID, ci.AD_Client_ID, ci.AD_Org_ID)
                                ELSE 0
                                END
                            ) - 
                            SUM(
                                CASE WHEN cd.DocBaseType = " + docBaseTypeARC_APC + @"
                                THEN currencyConvert(cs.DueAmt, ci.C_Currency_ID, " + C_Currency_ID + @", ci.DateAcct, ci.C_ConversionType_ID, ci.AD_Client_ID, ci.AD_Org_ID)
                                ELSE 0
                                END
                            )
                        ), 
                        0
                    ) AS SumAmount
             FROM C_Invoice ci
             INNER JOIN C_InvoicePaySchedule cs ON (cs.C_Invoice_ID = ci.C_Invoice_ID)
             INNER JOIN C_DocType cd ON (cd.C_DocType_ID = ci.C_DocTypeTarget_ID)
             WHERE CURRENT_DATE < cs.DueDate
               AND cs.VA009_IsPaid = 'N'
               AND ci.DocStatus IN ('CO', 'CL') AND ci.IsInDispute = 'N'", "ci", MRole.SQL_FULLYQUALIFIED, MRole.SQL_RW));
            // Query for 'Disputed'
            sql.Append(" UNION ALL ");
            sql.Append(MRole.GetDefault(ctx).AddAccessSQL($@"SELECT 'Disputed',
                    NVL(
                        (
                            SUM(
                                CASE WHEN cd.DocBaseType = " + docBaseTypeARI_APT + @"
                                THEN currencyConvert(cs.DueAmt, ci.C_Currency_ID, " + C_Currency_ID + @", ci.DateAcct, ci.C_ConversionType_ID, ci.AD_Client_ID, ci.AD_Org_ID)
                                ELSE 0
                                END
                            ) - 
                            SUM(
                                CASE WHEN cd.DocBaseType = " + docBaseTypeARC_APC + @"
                                THEN currencyConvert(cs.DueAmt, ci.C_Currency_ID, " + C_Currency_ID + @", ci.DateAcct, ci.C_ConversionType_ID, ci.AD_Client_ID, ci.AD_Org_ID)
                                ELSE 0
                                END
                            )
                        ), 
                        0
                    ) AS SumAmount
             FROM C_Invoice ci
             INNER JOIN C_InvoicePaySchedule cs ON (cs.C_Invoice_ID = ci.C_Invoice_ID)
             INNER JOIN C_DocType cd ON (cd.C_DocType_ID = ci.C_DocTypeTarget_ID)
             WHERE ci.DocStatus IN ('CO', 'CL') AND cs.VA009_IsPaid='N' AND ci.IsInDispute = 'Y'", "ci", MRole.SQL_FULLYQUALIFIED, MRole.SQL_RW));

            // Query for 'Unallocated Amount on AR Invoice'
            sql.Append(" UNION ALL ");
            if (ISOtrx)
            {
                sql.Append(MRole.GetDefault(ctx).AddAccessSQL($@"SELECT 'UnAllocated',
                    NVL(
                        (
                            SUM(
                                CASE WHEN cd.DocBaseType = " + docBaseTypeARI_APT + @"
                                THEN currencyConvert(cs.DueAmt, ci.C_Currency_ID, " + C_Currency_ID + @", ci.DateAcct, ci.C_ConversionType_ID, ci.AD_Client_ID, ci.AD_Org_ID)
                                ELSE 0
                                END
                            ) - 
                            SUM(
                                CASE WHEN cd.DocBaseType = " + docBaseTypeARC_APC + @"
                                THEN currencyConvert(cs.DueAmt, ci.C_Currency_ID, " + C_Currency_ID + @", ci.DateAcct, ci.C_ConversionType_ID, ci.AD_Client_ID, ci.AD_Org_ID)
                                ELSE 0
                                END
                            )
                        ), 
                        0
                    ) AS SumAmount
             FROM C_Invoice ci
             INNER JOIN C_InvoicePaySchedule cs ON (cs.C_Invoice_ID = ci.C_Invoice_ID)
             INNER JOIN C_DocType cd ON (cd.C_DocType_ID = ci.C_DocTypeTarget_ID)
             WHERE ci.DocStatus IN ('CO', 'CL') AND cs.VA009_IsPaid='N'", "ci", MRole.SQL_FULLYQUALIFIED, MRole.SQL_RW));
            }
            // Query for 'Hold' ON AP invoice
            else
            {
                sql.Append(MRole.GetDefault(ctx).AddAccessSQL($@"SELECT
                 'Hold',
                 nvl(
                     SUM(
                         CASE
                         WHEN ci.isholdpayment = 'Y' THEN
                             CASE
                             WHEN cd.docbasetype = " + docBaseTypeARI_APT + @" THEN
                             currencyconvert(
                                 cs.dueamt, ci.c_currency_id," + C_Currency_ID + @", ci.dateacct, ci.c_conversiontype_id, ci.ad_client_id, ci.ad_org_id
                             )
                             ELSE
                             0
                             END
                         ELSE
                         CASE
                         WHEN cd.docbasetype = " + docBaseTypeARI_APT + @"
                              AND cs.isholdpayment = 'Y' THEN
                             currencyconvert(
                                 cs.dueamt, ci.c_currency_id," + C_Currency_ID + @", ci.dateacct, ci.c_conversiontype_id, ci.ad_client_id, ci.ad_org_id
                             )
                         ELSE
                         0
                         END
                         END
                     ) - SUM(
                         CASE
                         WHEN ci.isholdpayment = 'Y' THEN
                             CASE
                             WHEN cd.docbasetype =" + docBaseTypeARC_APC + @"THEN
                             currencyconvert(
                                 cs.dueamt, ci.c_currency_id," + C_Currency_ID + @", ci.dateacct, ci.c_conversiontype_id, ci.ad_client_id, ci.ad_org_id
                             )
                             ELSE
                             0
                             END
                         ELSE
                         CASE
                         WHEN cd.docbasetype = " + docBaseTypeARC_APC + @"
                              AND cs.isholdpayment = 'Y' THEN
                             currencyconvert(
                                 cs.dueamt, ci.c_currency_id," + C_Currency_ID + @", ci.dateacct, ci.c_conversiontype_id, ci.ad_client_id, ci.ad_org_id
                             )
                         ELSE
                         0
                         END
                         END
                     ), 0
                 )      AS sumamount
             FROM
                 c_invoice ci
                 INNER JOIN c_invoicepayschedule cs ON ( cs.c_invoice_id = ci.c_invoice_id )
                 INNER JOIN c_doctype            cd ON ( cd.c_doctype_id = ci.c_doctypetarget_id )
             WHERE ci.docstatus IN ( 'CO', 'CL' ) AND cs.va009_ispaid = 'N'
             ", "ci", MRole.SQL_FULLYQUALIFIED, MRole.SQL_RW));
            }

            // Query for 'InProgress'
            sql.Append(" UNION ALL ");
            sql.Append(MRole.GetDefault(ctx).AddAccessSQL($@"SELECT 'InProgress',
                    NVL(
                        (
                            SUM(
                                CASE WHEN cd.DocBaseType = " + docBaseTypeARI_APT + @"
                                THEN currencyConvert(ci.grandtotalafterwithholding, ci.C_Currency_ID, " + C_Currency_ID + @", ci.DateAcct, ci.C_ConversionType_ID, ci.AD_Client_ID, ci.AD_Org_ID)
                                ELSE 0
                                END
                            ) - 
                            SUM(
                                CASE WHEN cd.DocBaseType = " + docBaseTypeARC_APC + @"
                                THEN currencyConvert(ci.grandtotalafterwithholding, ci.C_Currency_ID, " + C_Currency_ID + @", ci.DateAcct, ci.C_ConversionType_ID, ci.AD_Client_ID, ci.AD_Org_ID)
                                ELSE 0
                                END
                            )
                        ), 
                        0
                    ) AS SumAmount
             FROM C_Invoice ci
             INNER JOIN C_DocType cd ON (cd.C_DocType_ID = ci.C_DocTypeTarget_ID)
             WHERE ci.DocStatus = 'IP'
             ", "ci", MRole.SQL_FULLYQUALIFIED, MRole.SQL_RW));
            // Query for 'New'
            sql.Append(" UNION ALL ");
            sql.Append(MRole.GetDefault(ctx).AddAccessSQL($@"SELECT 'New',
                    NVL(
                        (
                            SUM(
                                CASE WHEN cd.DocBaseType = " + docBaseTypeARI_APT + @"
                                THEN currencyConvert(ci.grandtotalafterwithholding, ci.C_Currency_ID, " + C_Currency_ID + @", ci.DateAcct, ci.C_ConversionType_ID, ci.AD_Client_ID, ci.AD_Org_ID)
                                ELSE 0
                                END
                            ) - 
                            SUM(
                                CASE WHEN cd.DocBaseType = " + docBaseTypeARC_APC + @"
                                THEN currencyConvert(ci.grandtotalafterwithholding, ci.C_Currency_ID, " + C_Currency_ID + @", ci.DateAcct, ci.C_ConversionType_ID, ci.AD_Client_ID, ci.AD_Org_ID)
                                ELSE 0
                                END
                            )
                        ), 
                        0
                    ) AS SumAmount
             FROM C_Invoice ci
             INNER JOIN C_DocType cd ON cd.C_DocType_ID = ci.C_DocTypeTarget_ID
             INNER JOIN PeriodDetail pd ON (pd.AD_Client_ID=ci.AD_Client_ID)
             WHERE ci.DocStatus IN ('CO', 'CL') AND ci.DateInvoiced BETWEEN pd.StartDate AND pd.EndDate
            ", "ci", MRole.SQL_FULLYQUALIFIED, MRole.SQL_RW));

            // Query for 'Drafted'
            sql.Append(" UNION ALL ");
            sql.Append(MRole.GetDefault(ctx).AddAccessSQL($@"SELECT 'Drafted',
                    NVL(
                        (
                            SUM(
                                CASE WHEN cd.DocBaseType = " + docBaseTypeARI_APT + @"
                                THEN currencyConvert(ci.grandtotalafterwithholding, ci.C_Currency_ID, " + C_Currency_ID + @", ci.DateAcct, ci.C_ConversionType_ID, ci.AD_Client_ID, ci.AD_Org_ID)
                                ELSE 0
                                END
                            ) - 
                            SUM(
                                CASE WHEN cd.DocBaseType = " + docBaseTypeARC_APC + @"
                                THEN currencyConvert(ci.grandtotalafterwithholding, ci.C_Currency_ID, " + C_Currency_ID + @", ci.DateAcct, ci.C_ConversionType_ID, ci.AD_Client_ID, ci.AD_Org_ID)
                                ELSE 0
                                END
                            )
                        ), 
                        0
                    ) AS SumAmount
             FROM C_Invoice ci
             INNER JOIN C_DocType cd ON (cd.C_DocType_ID = ci.C_DocTypeTarget_ID)
             WHERE ci.DocStatus = 'DR'
             ", "ci", MRole.SQL_FULLYQUALIFIED, MRole.SQL_RW));


            DataSet ds = DB.ExecuteDataset(sql.ToString(), null, null);
            if (ds != null && ds.Tables[0].Rows.Count > 0)
            {
                sql.Clear();
                sql.Append(@"SELECT CASE WHEN Cursymbol IS NOT NULL THEN Cursymbol ELSE ISO_Code END AS Symbol,StdPrecision FROM C_Currency WHERE C_Currency_ID=" + C_Currency_ID);
                DataSet dsCurrency = DB.ExecuteDataset(sql.ToString(), null, null);

                for (int i = 0; i < ds.Tables[0].Rows.Count; i++)
                {
                    obj = new PurchaseStateDetail();
                    obj.TotalAmt = Util.GetValueOfDecimal(ds.Tables[0].Rows[i]["SumAmount"]);
                    obj.Symbol = Util.GetValueOfString(dsCurrency.Tables[0].Rows[0]["Symbol"]);
                    obj.stdPrecision = Util.GetValueOfInt(dsCurrency.Tables[0].Rows[0]["StdPrecision"]);
                    invData.Add(obj);
                }
            }
            return invData;
        }
        /// <summary>
        /// This function is Used to show Expected invoices against orders and GRN/Delivery Order
        /// </summary>
        /// <param name="ISOtrx">ISOtrx</param>
        /// <param name="ctx">Context</param>
        /// <param name="ListValue">ListValue</param>
        /// <param name="pageNo">pageNo</param>
        /// <param name="pageSize">pageSize</param>
        /// <author>VIS_427</author>
        /// <returns>List of data of Expected invoices against order and GRN</returns>
        public List<ExpectedInvoice> GetExpectedInvoiceData(Ctx ctx, bool ISOtrx, int pageNo, int pageSize, string ListValue)
        {
            ExpectedInvoice obj = new ExpectedInvoice();
            StringBuilder sql = new StringBuilder();
            StringBuilder sqlmain = new StringBuilder();
            List<ExpectedInvoice> invGrandTotalData = new List<ExpectedInvoice>();
            string OrderCheck = (ISOtrx == true ? " AND o.IsSOTrx='Y' " : " AND o.IsSOTrx='N' ");
            string DeliveryCheck = (ISOtrx == true ? "min.IsSOTrx='Y'" : "min.IsSOTrx='N'");
            var C_Currency_ID = ctx.GetContextAsInt("$C_Currency_ID");
            string BPCheck = (ISOtrx == true ? " AND cb.IsCustomer='Y' " : " AND cb.IsVendor='Y' ");
            sql.Append($@"SELECT * FROM  (");
            if (ListValue == "AL" || ListValue == "PO" || ListValue == "SO")
            {
                sqlmain.Append(MRole.GetDefault(ctx).AddAccessSQL($@"SELECT
                            'Order' AS Type,
                             cb.Pic,
                             o.DocumentNo,
                             o.DateOrdered,
                             o.DateOrdered AS FilterDate,
                             o.DatePromised AS PromisedDate,
                             custimg.ImageExtension,
                             cb.Name,
                             o.AD_Client_ID,
                             SUM(CASE
                                    WHEN mil.C_OrderLine_ID IS NOT NULL AND ci.M_InOutLine_id IS NOT NULL
                                          AND l.qtydelivered > l.qtyinvoiced THEN
                                             coalesce(
                                                 l.QtyOrdered, 0
                                             ) - coalesce(
                                                 l.QtyDelivered, 0)       
                                     WHEN ci.C_OrderLine_ID IS NOT NULL AND ci.M_InOutLine_id IS NOT NULL
                                          AND l.QtyDelivered < l.qtyinvoiced THEN
                                     COALESCE(
                                                 l.QtyOrdered, 0
                                             ) - coalesce(
                                                 l.qtyinvoiced, 0)
                                     ELSE COALESCE(
                                                 l.qtyordered, 0)
                                     END)            AS remainingquantity,
                                      SUM(
                                     CASE
                                     WHEN mil.C_OrderLine_ID IS NOT NULL AND ci.M_InOutLine_id IS NOT NULL
                                          AND l.qtydelivered >= l.qtyinvoiced THEN
                                     currencyconvert(
                                         round(
                                             (COALESCE(
                                                 l.qtyordered, 0
                                             ) - COALESCE(
                                                 l.qtydelivered, 0
                                             )) *(l.linetotalamt) / nullif(
                                                 l.qtyentered, 0
                                             ), cy.stdprecision
                                         ), o.c_currency_id," + C_Currency_ID + @", o.DateAcct, o.C_ConversionType_ID, o.AD_Client_ID, o.AD_Org_ID)
                                     WHEN ci.c_orderline_id IS NOT NULL AND ci.M_InOutLine_id IS NOT NULL
                                          AND l.qtydelivered < l.qtyinvoiced THEN
                                     currencyconvert(
                                         round(
                                             (COALESCE(
                                                 l.qtyordered, 0
                                             ) - COALESCE(
                                                 l.qtyinvoiced, 0
                                             )) *(l.linetotalamt) / nullif(
                                                 l.qtyentered, 0
                                             ), cy.stdprecision
                                         ), o.c_currency_id, " + C_Currency_ID + @", o.DateAcct, o.C_ConversionType_ID, o.AD_Client_ID, o.AD_Org_ID)
                                     ELSE
                                     currencyconvert(
                                         round(
                                             (COALESCE(l.qtyordered, 0)-COALESCE(l.qtyinvoiced, 0)-COALESCE(l.qtydelivered, 0)) * 
                                             (l.linetotalamt) / nullif(
                                                 l.qtyentered, 0
                                             ), cy.stdprecision
                                         ), o.c_currency_id, " + C_Currency_ID + @", o.DateAcct, o.C_ConversionType_ID, o.AD_Client_ID, o.AD_Org_ID)
                                     END
                                 )             AS totalvalue
                         FROM
                             c_order o
                             INNER JOIN C_OrderLine l ON (o.c_order_id = l.c_order_id)
                             INNER JOIN C_BPartner cb ON (o.C_BPartner_ID = cb.C_BPartner_ID)
                             INNER JOIN C_Currency cy ON (cy.C_Currency_ID=o.C_Currency_ID)
                             LEFT JOIN AD_Image custimg ON (custimg.AD_Image_ID = CAST(cb.Pic AS INTEGER))
                             LEFT JOIN C_InvoiceLine ci ON ( ci.C_OrderLine_ID = l.C_OrderLine_ID )
                             LEFT JOIN M_InOutLine   mil ON ( mil.C_OrderLine_ID = l.C_OrderLine_ID )", "o", MRole.SQL_FULLYQUALIFIED, MRole.SQL_RW));
                sqlmain.Append(OrderCheck + BPCheck + " AND o.DocStatus IN ('CO') ");
                sqlmain.Append(@"GROUP BY
                             cb.Pic, o.DocumentNo, o.DateOrdered,o.DateOrdered,o.DatePromised,custimg.ImageExtension, cb.Name,o.AD_Client_ID
                             HAVING SUM(
                                        CASE
                                        WHEN mil.c_orderline_id IS NOT NULL AND ci.M_InOutLine_id IS NOT NULL
                                             AND l.qtydelivered > l.qtyinvoiced THEN
                                                coalesce(
                                                    l.qtyordered, 0
                                                ) - coalesce(
                                                    l.qtydelivered, 0)
                                                
                                        WHEN ci.c_orderline_id IS NOT NULL AND ci.M_InOutLine_id IS NOT NULL
                                             AND l.qtydelivered < l.qtyinvoiced THEN
                                        coalesce(
                                                    l.qtyordered, 0
                                                ) - coalesce(
                                                    l.qtyinvoiced, 0)
                                        ELSe                        coalesce(
                                                    l.qtyordered, 0)
                                        END)  > 0 ");
            }
            if (ListValue == "AL")
            {
                sqlmain.Append(" UNION ALL ");
            }
            if (ListValue == "AL" || ListValue == "GR" || ListValue == "DO")
            {
                sqlmain.Append(MRole.GetDefault(ctx).AddAccessSQL($@"SELECT
                             'GRN' AS Type,
                             cb.Pic,
                             min.DocumentNo,
                             min.MovementDate AS DateOrdered,
                             CASE WHEN l.C_OrderLine_ID IS NOT NULL THEN o.DateOrdered
                             ELSE min.MovementDate
                             END AS FilterDate,
                             CASE WHEN l.C_OrderLine_ID IS NOT NULL THEN o.DatePromised
                             ELSE NULL
                             END AS PromisedDate,
                             custimg.ImageExtension,
                             cb.Name,
                             min.AD_Client_ID,
                             SUM(COALESCE(l.movementqty, 0) - COALESCE(ci.qtyinvoiced, 0)) AS RemainingQuantity,
                             SUM(
                                 CASE 
                                     WHEN l.C_OrderLine_ID IS NOT NULL THEN 
                                         currencyConvert(ROUND((COALESCE(l.movementqty, 0) - COALESCE(ci.qtyinvoiced, 0)) 
                                         * (ol.LineTotalAmt) / NULLIF(ol.QtyEntered, 0)
                                         ,cy.StdPrecision),o.C_Currency_ID, " + C_Currency_ID + @", o.DateAcct, o.C_ConversionType_ID, o.AD_Client_ID, o.AD_Org_ID)
                                     ELSE 
                                         (COALESCE(l.movementqty, 0) - COALESCE(ci.qtyinvoiced, 0)) * l.CurrentCostPrice
                                 END
                             ) AS TotalValue
                         FROM
                             M_InOut min
                             INNER JOIN M_InOutLine l ON (l.M_InOut_ID = min.M_InOut_ID)
                             INNER JOIN C_BPartner cb ON (min.C_BPartner_ID = cb.C_BPartner_ID)
                             LEFT JOIN C_InvoiceLine ci ON (ci.m_inoutline_ID = l.m_inoutline_ID)
                             LEFT JOIN C_OrderLine ol ON (ol.C_OrderLine_ID = l.C_OrderLine_ID)
                             LEFT JOIN C_Order o ON (o.C_Order_ID = ol.C_Order_ID)
                             LEFT JOIN C_Currency cy ON (cy.C_Currency_ID=o.C_Currency_ID)
                             LEFT JOIN AD_Image custimg ON (custimg.AD_Image_ID = CAST(cb.Pic AS INTEGER))
                         WHERE
                              min.DocStatus IN ('CO', 'CL')
                             AND NOT EXISTS (
                                 SELECT 1
                                 FROM c_orderline ol2
                                 WHERE ol2.C_OrderLine_ID = ol.C_OrderLine_ID
                                 AND COALESCE(ol2.qtyordered, 0) = COALESCE(ol2.qtyinvoiced, 0)
                             )
                             AND " + DeliveryCheck + BPCheck + " AND ol.qtyordered IS NOT NULL", "min", MRole.SQL_FULLYQUALIFIED, MRole.SQL_RW));
                sqlmain.Append(@" GROUP BY
                             cb.Pic, min.DocumentNo, min.MovementDate,CASE WHEN l.C_OrderLine_ID IS NOT NULL THEN o.DateOrdered
                             ELSE min.MovementDate
                             END,CASE WHEN l.C_OrderLine_ID IS NOT NULL THEN o.DatePromised
                             ELSE NULL END, custimg.ImageExtension, cb.Name,min.AD_Client_ID 
                              HAVING
                              SUM(coalesce(
                                  l.movementqty, 0
                              ) - coalesce(
                                  ci.qtyinvoiced, 0
                              )) > 0");
            }
            sql.Append(sqlmain);
            sql.Append(")T ORDER BY T.FilterDate");
            DataSet ds = DB.ExecuteDataset(sql.ToString(), null, null, pageSize, pageNo);
            if (ds != null && ds.Tables[0].Rows.Count > 0)
            {
                //fetching the record count to use it for pagination
                int RecordCount = Util.GetValueOfInt(DB.ExecuteScalar("SELECT COUNT(*) FROM (" + sqlmain.ToString() + ")t", null, null));
                sql.Clear();
                //this query is returning the field of base currency
                sql.Append(@"SELECT CASE WHEN Cursymbol IS NOT NULL THEN Cursymbol ELSE ISO_Code END AS Symbol,StdPrecision FROM C_Currency WHERE C_Currency_ID=" + C_Currency_ID);
                DataSet dsCurrency = DB.ExecuteDataset(sql.ToString(), null, null);

                for (int i = 0; i < ds.Tables[0].Rows.Count; i++)
                {
                    obj = new ExpectedInvoice();
                    obj.recordCount = RecordCount;
                    obj.TotalAmt = Util.GetValueOfDecimal(ds.Tables[0].Rows[i]["TotalValue"]);
                    obj.Symbol = Util.GetValueOfString(dsCurrency.Tables[0].Rows[0]["Symbol"]);
                    obj.DocumentNo = Util.GetValueOfString(ds.Tables[0].Rows[i]["DocumentNo"]);
                    obj.RecordType = Util.GetValueOfString(ds.Tables[0].Rows[i]["Type"]);
                    obj.stdPrecision = Util.GetValueOfInt(dsCurrency.Tables[0].Rows[0]["StdPrecision"]);
                    obj.OrderdDate = Util.GetValueOfDateTime(ds.Tables[0].Rows[i]["DateOrdered"]).Value;
                    obj.DatePromised = Util.GetValueOfDateTime(ds.Tables[0].Rows[i]["PromisedDate"]).Value;
                    obj.Name = Util.GetValueOfString(ds.Tables[0].Rows[i]["Name"]);
                    if (Util.GetValueOfInt(ds.Tables[0].Rows[i]["Pic"]) != 0)
                    {
                        obj.ImageUrl = "Images/Thumb46x46/" + Util.GetValueOfInt(ds.Tables[0].Rows[i]["Pic"]) + Util.GetValueOfString(ds.Tables[0].Rows[i]["ImageExtension"]);

                    }
                    invGrandTotalData.Add(obj);
                }
            }
            return invGrandTotalData;
        }
        /// <summary>
        /// This Method is used to return the refrence id 
        /// </summary>
        /// <param name="ct">context</param>
        /// <param name="ColumnData"></param>
        /// <returns>Dictionary with column name and refrence id</returns>
        /// <author>VIS_427 </author>
        public Dictionary<string, int> GetColumnIds(Ctx ct, string refernceName)
        {
            Dictionary<string, int> ColumnInfo = new Dictionary<string, int>();
            ColumnInfo["AD_Reference_ID"] = Util.GetValueOfInt(DB.ExecuteScalar(@"SELECT AD_Reference_ID FROM AD_Reference WHERE Name='" + refernceName + "'", null, null));
            return ColumnInfo;
        }
        /// <summary>
        /// This Method is used to return the refrence id 
        /// </summary>
        /// <param name="ct">context</param>
        /// <param name="columnDataArray"></param>
        /// <returns>Dictionary with column name and refrence id</returns>
        /// <author>VIS_427 </author>
        public Dictionary<string, int> GetColumnIDForExpPayment(Ctx ct, dynamic columnDataArray)
        {
            //Dictionary<string, int> ColumnInfo = new Dictionary<string, int>();
            //ColumnInfo["AD_Reference_ID"] = Util.GetValueOfInt(DB.ExecuteScalar(@"SELECT AD_Reference_ID FROM AD_Reference WHERE Name='" + refernceName + "'", null, null));
            //return ColumnInfo;
            Dictionary<string, int> ColumnInfo = new Dictionary<string, int>();
            foreach (var item in columnDataArray)
            {
                // Extract column name and table name
                string refernceName = item.refernceName;
                string ColumnName = item.ColumnName;
                if (!String.IsNullOrEmpty(refernceName))
                {
                    ColumnInfo[refernceName] = Util.GetValueOfInt(DB.ExecuteScalar(@"SELECT AD_Reference_ID FROM AD_Reference WHERE Name='" + refernceName + "'", null, null));
                }
                if (!String.IsNullOrEmpty(ColumnName))
                {
                    string sql = @"SELECT AD_Column_ID FROM AD_Column 
                               WHERE ColumnName ='" + ColumnName + @"' 
                               AND AD_Table_ID = (SELECT AD_Table_ID FROM AD_Table WHERE TableName='C_Payment')";
                    ColumnInfo[ColumnName] = Util.GetValueOfInt(DB.ExecuteScalar(sql, null, null));
                }
            }
            return ColumnInfo;
        }
        /// <summary>
        /// This function is Used to Get Top 10 Expense Amounts
        /// </summary>
        /// <param name="ListValue">ListValue</param>
        /// <param name="ctx">Context</param>
        /// <author>VIS_427</author>
        /// <returns>List of  Get Top 10 Expense Amounts</returns>
        public List<TopExpenseAmountData> GetTop10ExpenseAmountData(Ctx ctx, string ListValue)
        {
            TopExpenseAmountData obj = new TopExpenseAmountData();
            StringBuilder sql = new StringBuilder();
            StringBuilder sqlQuarter = new StringBuilder();
            List<TopExpenseAmountData> ExpenseAmountData = new List<TopExpenseAmountData>();
            //string BPCheck = (ISOtrx == true ? "cb.IsCustomer='Y'" : "cb.IsVendor='Y'");
            var C_Currency_ID = ctx.GetContextAsInt("$C_Currency_ID");
            int calendar_ID = 0;
            int CurrentYear = 0;
            int currentQuarter = 0;
            //Finding the calender id and Current Year to get data on this basis
            sql.Append(@"SELECT
                         DISTINCT cy.CalendarYears,CASE WHEN oi.C_Calendar_ID IS NOT NULL THEN oi.C_Calendar_ID
                         else ci.C_Calendar_ID END AS C_Calendar_ID
                         FROM C_Calendar cc
                         INNER JOIN AD_ClientInfo ci ON (ci.C_Calendar_ID=cc.C_Calendar_ID)
                         LEFT JOIN AD_OrgInfo oi ON (oi.C_Calendar_ID=cc.C_Calendar_ID)
                         INNER JOIN C_Year cy ON (cy.C_Calendar_ID=cc.C_Calendar_ID)
                         INNER JOIN C_Period cp  ON (cy.C_Year_ID = cp.C_Year_ID)
                         WHERE 
                         cy.IsActive = 'Y'
                         AND cp.IsActive = 'Y'
                         AND oi.IsActive='Y'
                         AND ci.IsActive='Y'
                         AND TRUNC(CURRENT_DATE) BETWEEN cp.StartDate AND cp.EndDate AND cc.AD_Client_ID=" + ctx.GetAD_Client_ID());
            // string yearSql = MRole.GetDefault(ctx).AddAccessSQL(sql.ToString(), "cc", MRole.SQL_FULLYQUALIFIED, MRole.SQL_RW);
            DataSet yearDs = DB.ExecuteDataset(sql.ToString(), null, null);
            if (yearDs != null && yearDs.Tables[0].Rows.Count > 0)
            {
                CurrentYear = Util.GetValueOfInt(yearDs.Tables[0].Rows[0]["CalendarYears"]);
                calendar_ID = Util.GetValueOfInt(yearDs.Tables[0].Rows[0]["C_Calendar_ID"]);
            }
            sql.Clear();
            //Here we are getting the current quarter
            sql.Append($@"SELECT PeriodNo, CEIL(PeriodNo/3) AS Quarter from C_Period p INNER JOIN C_Year y ON (p.C_Year_ID = y.c_year_ID)
                          WHERE y.CALENDARYEARS={VAdvantage.DataBase.GlobalVariable.TO_STRING(CurrentYear.ToString())} 
                          AND TRUNC(CURRENT_DATE) between p.startdate and p.enddate AND y.C_Calendar_ID ={calendar_ID}");
            DataSet quaterds = DB.ExecuteDataset(sql.ToString(), null, null);
            if (quaterds != null && quaterds.Tables[0].Rows.Count > 0)
            {
                currentQuarter = Util.GetValueOfInt(quaterds.Tables[0].Rows[0]["Quarter"]);
            }
            sql.Clear();
            //Main Query to get the data
            sql.Append($@"WITH FactData AS ({MRole.GetDefault(ctx).AddAccessSQL($@"SELECT 
                         acct.C_AcctSchema_ID,
                         fa.AD_Org_ID, 
                         fa.AD_Client_ID,
                         fa.DateAcct,
                         ele.C_Element_ID,
                         eleVal.Value || '_' || eleVal.NAME AS ExpenseName,
                         SUM(fa.AmtAcctDr - fa.AmtAcctCR) AS ExpenseAmount
                         FROM fact_acct fa
                         INNER JOIN C_AcctSchema acct ON (fa.C_AcctSchema_ID = acct.C_AcctSchema_ID)
                         INNER JOIN C_AcctSchema_Element acctEle ON (acctEle.C_AcctSchema_ID = acct.C_AcctSchema_ID AND ElementType = 'AC')
                         INNER JOIN C_Element ele ON (ele.C_Element_ID = acctEle.C_Element_ID)
                         INNER JOIN C_ElementValue eleVal ON (eleVal.C_Element_ID = ele.C_Element_ID AND AccountType = 'E' AND fa.Account_ID = eleVal.C_ElementValue_ID)
                         INNER JOIN AD_ClientInfo ci ON (ci.AD_Client_ID = fa.AD_Client_ID AND fa.C_AcctSchema_ID = ci.C_AcctSchema1_ID)
                         WHERE acctEle.IsActive = 'Y' AND eleVal.IsActive = 'Y' AND fa.PostingType='A'", "fa", MRole.SQL_FULLYQUALIFIED, MRole.SQL_RW
                     )}");
            sql.Append(@" GROUP BY 
                         acct.C_AcctSchema_ID,
                         fa.AD_Org_ID, 
                         fa.AD_Client_ID,
                         ele.C_Element_ID,
                         eleVal.Value || '_' || eleVal.NAME,
                         fa.DateAcct)");
            //If the user selected this quarter the add the with clauses of current quarter
            if (ListValue == "3")
            {
                sql.Append($@",curentPeriod AS
                               (SELECT PeriodNo, CEIL(PeriodNo / 3) AS Quarter from C_Period p INNER JOIN C_Year y ON(p.C_Year_ID = y.c_year_ID)
                               WHERE y.CalendarYears ={ VAdvantage.DataBase.GlobalVariable.TO_STRING(CurrentYear.ToString())}
                AND y.C_Calendar_ID ={ calendar_ID}
                AND TRUNC(CURRENT_DATE) between p.StartDate and p.EndDate ),
                               PeriodQuater AS
                               (SELECT PeriodNo, CEIL(PeriodNo/ 3) AS Quarter from C_Period p INNER JOIN C_Year y ON(p.C_Year_ID = y.c_year_ID)
                               WHERE y.CalendarYears ={ VAdvantage.DataBase.GlobalVariable.TO_STRING(CurrentYear.ToString())}
                AND y.C_Calendar_ID ={ calendar_ID})");
            }
            //If the user selected previous quarter the add the with clauses of current quarter
            else if (ListValue == "5")
            {

                //thi if clause is when current quarter is 1 the previous quarter will be of previous year
                if (currentQuarter == 1)
                {
                    CurrentYear = CurrentYear - 1;
                    sql.Append($@",PeriodPreviousQuater AS
                                              (SELECT CEIL(MAX(PeriodNo)/3) AS Quarter from C_Period p INNER JOIN C_Year y ON (p.C_Year_ID = y.c_year_ID)
                                              WHERE y.CalendarYears={VAdvantage.DataBase.GlobalVariable.TO_STRING(CurrentYear.ToString())} AND y.C_Calendar_ID={calendar_ID})
                                              ,PreviousPeriodQuater AS
                                              (SELECT PeriodNo, CEIL(PeriodNo/3) AS Quarter from C_Period p INNER JOIN C_Year y ON (p.C_Year_ID = y.c_year_ID)
                                              WHERE y.CalendarYears={VAdvantage.DataBase.GlobalVariable.TO_STRING(CurrentYear.ToString())} AND y.C_Calendar_ID={calendar_ID})");
                }
                else
                {
                    sql.Append($@",curentPeriod AS 
                                                    (SELECT PeriodNo, CEIL(PeriodNo/3) AS Quarter from C_Period p INNER JOIN C_Year y ON (p.C_Year_ID = y.c_year_ID)
                                                    WHERE y.CalendarYears={VAdvantage.DataBase.GlobalVariable.TO_STRING(CurrentYear.ToString())} AND y.C_Calendar_ID={calendar_ID}
                                                    AND TRUNC(CURRENT_DATE) between p.startdate and p.enddate ),
                                                    PeriodQuater AS
                                                    (SELECT PeriodNo, CEIL(PeriodNo/3) AS Quarter from C_Period p INNER JOIN C_Year y ON (p.C_Year_ID = y.c_year_ID)
                                                    WHERE y.CalendarYears={VAdvantage.DataBase.GlobalVariable.TO_STRING(CurrentYear.ToString())} AND y.C_Calendar_ID={calendar_ID})");
                }
            }
            sql.Append(@",PeriodDetail AS (SELECT c_period.AD_Client_ID,");
            //Getting data according to This Fiscal Year
            if (ListValue == "1")
            {
                sql.Append(GetYearSql("Min(C_Period.StartDate)", "Max(C_Period.EndDate)", $@"C_Year.C_Calendar_ID ={calendar_ID}
                    AND C_Year.IsActive = 'Y' AND C_Period.IsActive='Y' AND C_Year.CALENDARYEARS={VAdvantage.DataBase.GlobalVariable.TO_STRING(CurrentYear.ToString())}"));
            }
            //Getting data according to This Month
            else if (ListValue == "2")
            {
                sql.Append(GetYearSql("Min(C_Period.StartDate)", "Max(C_Period.EndDate)",
                            $@"C_Year.C_Calendar_ID ={calendar_ID}
                            AND C_Year.IsActive = 'Y' AND C_Period.IsActive='Y'
                            AND TRUNC(CURRENT_DATE) BETWEEN C_Period.StartDate AND C_Period.EndDate"));
            }
            ////Getting data according to this quarter
            else if (ListValue == "3")
            {

                sql.Append(GetYearSql("Min(c_period.StartDate)", "Max(c_period.EndDate)",
                                     $@" c_period.periodno IN (
                                     SELECT pq.periodno
                                     FROM PeriodQuater pq
                                     INNER JOIN curentPeriod cp ON (cp.Quarter = pq.Quarter)
                                     ) AND C_Year.C_Calendar_ID ={calendar_ID}
                                      AND C_Year.IsActive = 'Y' AND C_Period.IsActive='Y' AND C_Year.CALENDARYEARS={VAdvantage.DataBase.GlobalVariable.TO_STRING(CurrentYear.ToString())}"));
            }
            //Getting data according to LasT Year
            else if (ListValue == "4")
            {
                CurrentYear = CurrentYear - 1;
                sql.Append(GetYearSql("Min(c_period.StartDate)", "Max(c_period.EndDate)",
                           $@"C_Year.C_Calendar_ID ={calendar_ID}
                             AND C_Year.IsActive = 'Y' AND C_Period.IsActive='Y'
                            AND C_Year.CALENDARYEARS={VAdvantage.DataBase.GlobalVariable.TO_STRING(CurrentYear.ToString())}"));
            }
            else if (ListValue == "5")
            {
                if (currentQuarter == 1)
                {
                    sql.Append(GetYearSql("Min(c_period.StartDate)", "Max(c_period.EndDate)",
                                         $@" c_period.periodno IN (
                                     SELECT pq.PeriodNo FROM PreviousPeriodQuater pq INNER JOIN PeriodPreviousQuater cp ON (cp.Quarter = pq.Quarter))
                                      AND C_Year.C_Calendar_ID ={calendar_ID}
                                      AND C_Year.IsActive = 'Y' AND C_Period.IsActive='Y' AND C_Year.CALENDARYEARS={VAdvantage.DataBase.GlobalVariable.TO_STRING(CurrentYear.ToString())}"));
                }
                else
                {
                    sql.Append(GetYearSql("Min(c_period.StartDate)", "Max(c_period.EndDate)",
                                             $@" c_period.periodno IN (
                                    SELECT pq.PeriodNo FROM PeriodQuater pq INNER JOIN curentPeriod cp ON(cp.Quarter - 1 = pq.Quarter))
                                     AND C_Year.C_Calendar_ID ={calendar_ID}
                                      AND C_Year.IsActive = 'Y' AND C_Period.IsActive='Y' AND C_Year.CALENDARYEARS={VAdvantage.DataBase.GlobalVariable.TO_STRING(CurrentYear.ToString())}"));
                }
            }
            ////Getting data according Previous Month
            else if (ListValue == "6")
            {
                sql.Append(GetYearSql("TRUNC(ADD_MONTHS(TRUNC(Current_Date), -1), 'MM')", "LAST_DAY(ADD_MONTHS(TRUNC(Current_Date, 'MM'), -1))", ""));
            }
            //Last 6 Months Data
            else if (ListValue == "7")
            {
                if (DB.IsPostgreSQL())
                {
                    sql.Append(GetYearSql("DATE_TRUNC('MONTH', CURRENT_DATE) - INTERVAL '6 MONTHS'", "(DATE_TRUNC('MONTH', CURRENT_DATE) - INTERVAL '1 MONTH' + INTERVAL '1 MONTH - 1 day')", ""));
                }
                else
                {
                    sql.Append(GetYearSql("TRUNC(ADD_MONTHS(TRUNC(Current_Date), -6), 'MM')", "LAST_DAY(ADD_MONTHS(TRUNC(Current_Date, 'MM'), -1))", ""));
                }
            }
            //Last 12 Months Data
            else if (ListValue == "8")
            {
                if (DB.IsPostgreSQL())
                {
                    sql.Append(GetYearSql("DATE_TRUNC('month', CURRENT_DATE) - INTERVAL '12 months'", "(DATE_TRUNC('month', CURRENT_DATE) - INTERVAL '1 month' + INTERVAL '1 month - 1 day')", ""));
                }
                else
                {
                    sql.Append(GetYearSql("TRUNC(ADD_MONTHS(TRUNC(Current_Date), -12), 'MM')", "LAST_DAY(ADD_MONTHS(TRUNC(Current_Date, 'MM'), -1))", ""));
                }

            }
            sql.Append($@" GROUP BY c_period.AD_Client_ID)");
            sql.Append(@" SELECT SUM(ExpenseAmount) AS TotalExpenseAmount,
                         fa.ExpenseName 
                         FROM
                         FactData fa 
                         INNER JOIN PeriodDetail pd ON (pd.AD_Client_ID=fa.AD_Client_ID)
                     WHERE fa.DateAcct BETWEEN pd.StartDate AND pd.EndDate
                     GROUP BY fa.ExpenseName
                     ORDER BY TotalExpenseAmount DESC
                     FETCH FIRST 10 ROWS ONLY");
            DataSet ds = DB.ExecuteDataset(sql.ToString(), null, null);
            if (ds != null && ds.Tables[0].Rows.Count > 0)
            {

                for (int i = 0; i < ds.Tables[0].Rows.Count; i++)
                {
                    obj = new TopExpenseAmountData();
                    obj.ExpenseAmount = Util.GetValueOfDecimal(ds.Tables[0].Rows[i]["TotalExpenseAmount"]);
                    obj.ExpenseName = Util.GetValueOfString(ds.Tables[0].Rows[i]["ExpenseName"]);
                    ExpenseAmountData.Add(obj);
                }

            }
            return ExpenseAmountData;
        }
        /// <summary>
        /// This function to concatenate the query based on differnet scenarios
        /// </summary>
        /// <param name="StartDate">StartDate</param>
        /// <param name="EndDate">EndDate</param>
        /// <param name="whereClaues">whereClaues</param>
        /// <author>VIS_427</author>
        /// <returns>Concatenated Query String</returns>
        public string GetYearSql(string StartDate, string EndDate, string whereClaues)
        {
            StringBuilder sql = new StringBuilder();
            sql.Append(@" " + StartDate + " AS StartDate, " + EndDate + " AS EndDate " +
                " FROM C_Year INNER JOIN C_Period ON (C_Year.C_Year_ID=C_Period.C_Year_ID)");
            if (whereClaues != "")
            {
                sql.Append(" WHERE " + whereClaues);
            }
            return sql.ToString();
        }
        /// <summary>
        /// This function is Used to Get the Finance Instigh Data
        /// </summary>
        /// <param name="ctx">Context</param>
        /// <param name="ListValue">ListValue/param>
        /// <returns>returns Finance Instigh Data</returns>
        /// <author>VIS_427</author>
        public List<dynamic> GetFinInsightsData(Ctx ctx, string ListValue)
        {
            List<dynamic> retData = new List<dynamic>();
            String sql = @"SELECT VA113_DataObject,Name,VA113_REF_TABLE_VIEW, DisplayName, VA113_Result,AD_Org_ID FROM VA113_INSIGHTS";
            sql = MRole.GetDefault(ctx).AddAccessSQL(sql, "VA113_INSIGHTS", MRole.SQL_FULLYQUALIFIED, MRole.SQL_RO);
            DataSet ds = DB.ExecuteDataset(sql, null, null);
            if (ds != null && ds.Tables[0].Rows.Count > 0)
            {
                for (int i = 0; i < ds.Tables[0].Rows.Count; i++)
                {
                    dynamic obj = new ExpandoObject();
                    obj.DataObject = Util.GetValueOfString(ds.Tables[0].Rows[i]["VA113_DataObject"]);
                    obj.Name = Util.GetValueOfString(ds.Tables[0].Rows[i]["Name"]);
                    obj.TabelView = Util.GetValueOfString(ds.Tables[0].Rows[i]["VA113_REF_TABLE_VIEW"]);
                    obj.DisplayName = Util.GetValueOfString(ds.Tables[0].Rows[i]["DisplayName"]);
                    obj.Result = Util.GetValueOfString(ds.Tables[0].Rows[i]["VA113_Result"]);
                    obj.AD_Org_ID = Util.GetValueOfInt(ds.Tables[0].Rows[i]["AD_Org_ID"]);
                    retData.Add(obj);
                }
            }
            return retData;
        }
        /// <summary>
        /// This Function is use to get the data in grid
        /// </summary>
        /// <param name="ctx">Context</param>
        /// <param name="tableName">tableName</param>
        /// <param name="pageNo">pageNo</param>
        /// <param name="pageSize">pageSize</param>
        /// <returns>returns the data in grid</returns>
        /// <author>VIS_427</author>
        public List<dynamic> GetFinDataInsightGrid(Ctx ctx, string tableName, int pageNo, int pageSize, int AD_Org_ID)
        {
            string columnNames = string.Empty;
            dynamic obj = new ExpandoObject();
            List<dynamic> retData = new List<dynamic>();
            string[] NotIncludeCol = { "AD_Client_ID", "AD_Org_ID", "Export_ID", "CreatedBy", "UpdatedBy", "Created", "Updated", "IsActive", "DATA_OBJECT" };
            NotIncludeCol = NotIncludeCol.Select(s => s.ToUpper()).ToArray();

            string sql = @"SELECT * FROM " + tableName.ToUpper() + " WHERE AD_Client_ID = " + ctx.GetAD_Client_ID() + " AND AD_Org_ID = " + AD_Org_ID;
            //sql = MRole.GetDefault(ctx).AddAccessSQL(sql, tableName, MRole.SQL_FULLYQUALIFIED, MRole.SQL_RW);
            DataSet ds = DB.ExecuteDataset(sql.ToString(), null, null, pageSize, pageNo);
            if (ds != null && ds.Tables.Count > 0)
            {
                StringBuilder sb = new StringBuilder();
                foreach (DataColumn col in ds.Tables[0].Columns)
                {
                    if (!NotIncludeCol.Contains(Util.GetValueOfString(col.ColumnName)))
                    {
                        if (sb.Length > 0)
                        {
                            sb.Append(",");
                        }
                        sb.Append(Util.GetValueOfString(col.ColumnName));
                    }
                }
                columnNames = sb.ToString();
            }

            obj.ColName = columnNames;
            retData.Add(obj);
            string[] colName = columnNames.Split(',');
            if (ds != null && ds.Tables[0].Rows.Count > 0)
            {
                int RecordCount = Util.GetValueOfInt(DB.ExecuteScalar("SELECT COUNT(*) FROM (" + sql + ")t", null, null));
                for (int i = 0; i < ds.Tables[0].Rows.Count; i++)
                {
                    obj = new ExpandoObject();
                    IDictionary<string, object> dict = (IDictionary<string, object>)obj;

                    for (int j = 0; j < colName.Length; j++)
                    {
                        string trimmedColName = colName[j].Trim(); // Trim to avoid whitespace issues
                        dict[trimmedColName] = Util.GetValueOfString(ds.Tables[0].Rows[i][trimmedColName]);
                    }
                    dict["recid"] = i + 1;
                    dict["Count"] = RecordCount;
                    retData.Add(obj);
                }
            }
            return retData;
        }
        /// <summary>
        /// This Function is use to get the Name of Column  of Table
        /// </summary>
        /// <param name="ctx">Context</param>
        /// <param name="tableName">tableName</param>
        /// <param name="NotIncludeCol">NotIncludeCol</param>
        /// <returns>returns the Name of Column  of Table</returns>
        /// <author>VIS_427</author>
        public string GetDataGridColumn(Ctx ctx, string tablename, string[] NotIncludeCol)
        {
            String sql = $@"SELECT ac.ColumnName FROM AD_Column ac
                           INNER JOIN AD_Table at ON (at.AD_Table_ID=ac.AD_Table_ID) WHERE UPPER(at.TableName)= UPPER({VAdvantage.DataBase.GlobalVariable.TO_STRING(tablename.ToString())})
                            ORDER BY AD_Reference_ID ";
            DataSet ds = DB.ExecuteDataset(sql, null, null);
            StringBuilder sb = new StringBuilder();
            for (int i = 0; i < ds.Tables[0].Rows.Count; i++)
            {
                if (!NotIncludeCol.Contains(Util.GetValueOfString(ds.Tables[0].Rows[i]["ColumnName"])))
                {
                    if (sb.Length > 0)
                    {
                        sb.Append(",");
                    }
                    sb.Append(Util.GetValueOfString(ds.Tables[0].Rows[i]["ColumnName"]));
                }
            }
            return sb.ToString();
        }

        /// <summary>
        /// This function is used to Retrieve Data of Income and Expense
        /// </summary>
        /// <param name="ctx">Context</param>
        /// <param name="ListValue">List Value for filteration</param>
        /// <returns>Data</returns>
        /// <author>VIS_0045</author>
        public VAS_ExpenseRevenue GetExpenseRevenueDetails(Ctx ctx, string ListValue)
        {
            VAS_ExpenseRevenue lstExprevData = new VAS_ExpenseRevenue();
            decimal[] lstExpData = null;
            decimal[] lstRevData = null;
            decimal[] lstProfitData = null;
            string[] lstLabel = null;
            int CurrentYear = 0;
            int calendar_ID = 0;

            // Get Financial Year Data 
            DataSet dsFinancialYear = GetFinancialYearDetail(ctx, out string errorMessage);
            if (!string.IsNullOrEmpty(errorMessage))
            {
                lstExprevData.ErrorMessage = errorMessage;
                return lstExprevData;
            }
            else
            {
                CurrentYear = Util.GetValueOfInt(dsFinancialYear.Tables[0].Rows[0]["CalendarYears"]);
                calendar_ID = Util.GetValueOfInt(dsFinancialYear.Tables[0].Rows[0]["C_Calendar_ID"]);
            }

            // Get Expense/Income Data
            DataSet dsExpRev = GetExpenseRevenueData(ctx, ListValue, calendar_ID, CurrentYear, out errorMessage);
            if (!string.IsNullOrEmpty(errorMessage))
            {
                lstExprevData.ErrorMessage = errorMessage;
                return lstExprevData;
            }
            else
            {
                lstExprevData.Precision = Util.GetValueOfInt(dsExpRev.Tables[0].Rows[0]["StdPrecision"]);
                lstLabel = new string[dsExpRev.Tables[0].Rows.Count];
                lstExpData = new decimal[dsExpRev.Tables[0].Rows.Count];
                lstRevData = new decimal[dsExpRev.Tables[0].Rows.Count];
                lstProfitData = new decimal[dsExpRev.Tables[0].Rows.Count];
                DataRow dr = null;
                for (int i = 0; i < dsExpRev.Tables[0].Rows.Count; i++)
                {
                    dr = dsExpRev.Tables[0].Rows[i];

                    lstExpData[i] = Util.GetValueOfDecimal(dr["ExpenseAmount"]);
                    lstRevData[i] = Util.GetValueOfDecimal(dr["revenueAmount"]);
                    lstProfitData[i] = Util.GetValueOfDecimal(dr["revenueAmount"]) - Util.GetValueOfDecimal(dr["ExpenseAmount"]);
                    lstLabel[i] = Util.GetValueOfString(dr["Name"]);
                }

                lstExprevData.lstLabel = lstLabel;
                lstExprevData.lstExpData = lstExpData;
                lstExprevData.lstRevData = lstRevData;
                lstExprevData.lstProfitData = lstProfitData;
            }

            return lstExprevData;
        }

        /// <summary>
        /// This function is used to get Previous Year based on financial Year
        /// </summary>
        /// <param name="CalendarYear">Current Calendar Year</param>
        /// <param name="AD_Client_ID">Client ID</param>
        /// <param name="C_Calendar_ID">Calendar ID</param>
        /// <returns>DataSet</returns>
        /// <author>VIS_0045</author>
        public int GetPreviousPeriod(int CalendarYear, int AD_Client_ID, int C_Calendar_ID)
        {
            string sql = $@" WITH CurrentPeriod AS (
                            /* Fetch current year data and calculate previous period using LAG */
                            SELECT 
                                pl.periodno, 
                                pl.C_Period_ID, 
                                pl.StartDate, 
                                pl.EndDate,
                                pl.C_Year_ID,
                                pl.AD_Client_ID,
                                CAST(y.CalendarYears AS INT) AS CalendarYears,
                                LAG(pl.C_Period_ID) OVER (PARTITION BY y.CalendarYears ORDER BY pl.periodno) AS Previous_Period_ID
                            FROM 
                                C_Period pl
                                INNER JOIN C_Year y ON y.C_Year_ID = pl.C_Year_ID
                            WHERE pl.IsActive = 'Y' AND 
                                pl.AD_Client_ID = {AD_Client_ID}
                                AND y.C_Calendar_ID = {C_Calendar_ID}
                                AND y.CalendarYears = {GlobalVariable.TO_STRING(CalendarYear.ToString())}
                        ),";

            sql += $@" PreviousPeriod AS (
                        /* Fetch the previous year's last period (PeriodNo = MAX(PeriodNo)) */
                        SELECT 
                            pl.C_Period_ID
                        FROM 
                            C_Period pl
                            INNER JOIN C_Year y ON y.C_Year_ID = pl.C_Year_ID
                        WHERE 
                            pl.AD_Client_ID = {AD_Client_ID}
                            AND y.C_Calendar_ID = {C_Calendar_ID}
                            AND y.CalendarYears = {GlobalVariable.TO_STRING((CalendarYear - 1).ToString())}
                            AND pl.PeriodNo = (
                                /* Directly get the max period number in the same query */
                                SELECT MAX(PeriodNo) 
                                FROM C_Period pll 
                                WHERE pll.IsActive = 'Y' AND pll.C_Year_ID = pl.C_Year_ID )
                        )";

            sql += $@" SELECT 
                        COALESCE(cp.Previous_Period_ID,  pp.C_Period_ID) AS PreviousPeriod
                    FROM 
                        CurrentPeriod cp
                    JOIN 
                        PreviousPeriod pp ON (1=1)
                    WHERE Current_Date Between cp.StartDate and cp.EndDate";

            int C_Period_ID = Util.GetValueOfInt(DB.ExecuteScalar(sql));
            return C_Period_ID;

        }

        /// <summary>
        /// This function is used to get the Financial Year Details
        /// </summary>
        /// <param name="ctx">Context</param>
        /// <param name="ErrorMessage">Error Message if any</param>
        /// <returns>DataSet</returns>
        /// <author>VIS_0045</author>
        public DataSet GetFinancialYearDetail(Ctx ctx, out string ErrorMessage)
        {
            ErrorMessage = "";
            string sql = "";
            sql = @"SELECT
                         DISTINCT cy.CalendarYears,
                         CASE 
                         WHEN oi.C_Calendar_ID IS NOT NULL THEN oi.C_Calendar_ID
                         ELSE ci.C_Calendar_ID END AS C_Calendar_ID, 
                         cp.C_Period_ID,
                         CEIL(cp.PeriodNo/3) AS CurQuarter
                         FROM C_Calendar cc
                         INNER JOIN AD_ClientInfo ci ON (ci.C_Calendar_ID=cc.C_Calendar_ID)
                         LEFT JOIN AD_OrgInfo oi ON (oi.C_Calendar_ID=cc.C_Calendar_ID)
                         INNER JOIN C_Year cy ON (cy.C_Calendar_ID=cc.C_Calendar_ID)
                         INNER JOIN C_Period cp  ON (cy.C_Year_ID = cp.C_Year_ID)
                         WHERE 
                         cy.IsActive = 'Y'
                         AND cp.IsActive = 'Y'
                         AND oi.IsActive='Y'
                         AND ci.IsActive='Y'
                         AND TRUNC(CURRENT_DATE) BETWEEN cp.StartDate AND cp.EndDate AND cc.AD_Client_ID=" + ctx.GetAD_Client_ID();
            DataSet ds = DB.ExecuteDataset(sql);
            if (ds == null || (ds != null && ds.Tables.Count == 0) || (ds != null && ds.Tables[0].Rows.Count == 0))
            {
                ErrorMessage = Msg.GetMsg(ctx, "VAS_CalendarNotFound");
            }
            return ds;
        }

        /// <summary>
        /// This function is used to get the Expense / Income Data
        /// </summary>
        /// <param name="ctx">Context</param>
        /// <param name="ListValue">List Value for filter data</param>
        /// <param name="C_Calendar_ID">Calander ID</param>
        /// <param name="CalendarYears">Calendar Year</param>
        /// <param name="ErrorMessage"><Error Message if any/param>
        /// <returns>Dataset</returns>
        /// <author>VIS_0045</author>
        public DataSet GetExpenseRevenueData(Ctx ctx, string ListValue, int C_Calendar_ID, int CalendarYears, out string ErrorMessage)
        {
            ErrorMessage = "";
            string sql = $@" SELECT 
                         acct.C_AcctSchema_ID, 
                         fa.AD_Client_ID,
                         fa.C_Period_ID,
                         y.CalendarYears,
                         p.Name,
                         c.StdPrecision, 
                        SUM(CASE WHEN eleVal.AccountType = 'E' THEN (fa.AmtAcctDR - fa.AmtAcctCR) ELSE 0 END) AS ExpenseAmount,
                        SUM(CASE WHEN eleVal.AccountType = 'R' THEN (fa.AmtAcctCR - fa.AmtAcctDR) ELSE 0 END) AS revenueAmount
                         FROM fact_acct fa
                         INNER JOIN C_AcctSchema acct ON (fa.C_AcctSchema_ID = acct.C_AcctSchema_ID)
                         INNER JOIN C_AcctSchema_Element acctEle ON (acctEle.C_AcctSchema_ID = acct.C_AcctSchema_ID AND acctEle.ElementType = 'AC')
                         INNER JOIN C_Element ele ON (ele.C_Element_ID = acctEle.C_Element_ID)
                         INNER JOIN C_ElementValue eleVal ON (eleVal.C_Element_ID = ele.C_Element_ID AND eleVal.AccountType IN ('R', 'E') AND fa.Account_ID = eleVal.C_ElementValue_ID)
                         INNER JOIN AD_ClientInfo ci ON (ci.AD_Client_ID = fa.AD_Client_ID AND fa.C_AcctSchema_ID = ci.C_AcctSchema1_ID)
                         INNER JOIN C_Currency c ON (c.C_Currency_ID = acct.C_Currency_ID) 
                         INNER JOIN C_Period p ON (p.C_Period_ID = fa.C_Period_ID)
                         INNER JOIN C_Year y ON (y.C_Year_ID = p.C_Year_ID)
                         WHERE acctEle.IsActive = 'Y' 
                               AND eleVal.IsActive = 'Y' 
                               AND y.C_Calendar_ID = {C_Calendar_ID}
                               AND p.IsActive = 'Y' AND y.IsActive = 'Y' AND fa.PostingType='A' ";
            if (ListValue.Equals("01"))
            {
                /* Financial Year */
                sql += $@" AND y.CalendarYears = {GlobalVariable.TO_STRING(CalendarYears.ToString())}";
            }
            else if (ListValue.Equals("03"))
            {
                /* Previous Year */
                sql += $@" AND y.CalendarYears = {GlobalVariable.TO_STRING((CalendarYears - 1).ToString())}";
            }
            else if (ListValue.Equals("02"))
            {
                /* This Month */
                sql += $@" AND Trunc(Current_Date) Between p.StartDate and p.EndDate ";
            }
            else if (ListValue.Equals("04"))
            {
                /* Previous Month */
                int C_Period_ID = GetPreviousPeriod(CalendarYears, ctx.GetAD_Client_ID(), C_Calendar_ID);
                if (C_Period_ID > 0)
                {
                    sql += $@" AND p.C_Period_ID = { C_Period_ID } ";
                }
                else
                {
                    ErrorMessage = Msg.GetMsg(ctx, "VAS_PreviosuPeriodnotFound");
                    return null;
                }
            }
            else if (ListValue == "05")
            {
                //Last 6 Months Data
                if (DB.IsPostgreSQL())
                {
                    sql += " AND date_trunc(p.startdate) >=  DATE_TRUNC('MONTH', CURRENT_DATE) - INTERVAL '6 MONTHS'";
                    sql += " AND date_trunc(p.EndDate) <= (DATE_TRUNC('MONTH', CURRENT_DATE) - INTERVAL '1 day')";
                }
                else
                {
                    sql += " AND TRUNC(p.startdate) >= TRUNC(ADD_MONTHS(TRUNC(Current_Date), -6), 'MM')";
                    sql += " AND TRUNC(p.EndDate) <= LAST_DAY(ADD_MONTHS(TRUNC(Current_Date, 'MM'), -1))";
                }
            }
            else if (ListValue == "06")
            {
                //Last 12 Months Data
                if (DB.IsPostgreSQL())
                {
                    sql += " AND date_trunc(p.startdate) >=  DATE_TRUNC('month', CURRENT_DATE) - INTERVAL '12 months'";
                    sql += " AND date_trunc(p.EndDate) <= (DATE_TRUNC('MONTH', CURRENT_DATE) - INTERVAL '1 day')";
                }
                else
                {
                    sql += " AND TRUNC(p.startdate) >= TRUNC(ADD_MONTHS(TRUNC(Current_Date), -12), 'MM')";
                    sql += " AND TRUNC(p.EndDate) <= LAST_DAY(ADD_MONTHS(TRUNC(Current_Date, 'MM'), -1))";
                }
            }

            sql = MRole.GetDefault(ctx).AddAccessSQL(sql, "fa", MRole.SQL_FULLYQUALIFIED, MRole.SQL_RO);
            sql += @" GROUP BY
                      acct.C_AcctSchema_ID, 
                      fa.AD_Client_ID,
                      fa.C_Period_ID,
                      y.CalendarYears,
                      p.Name, 
                      c.StdPrecision 
                      order by
                      acct.C_AcctSchema_ID, 
                      fa.AD_Client_ID,
                      y.CalendarYears,
                      fa.C_Period_ID";

            DataSet dsExpRevData = DB.ExecuteDataset(sql);
            if (dsExpRevData == null || (dsExpRevData != null && dsExpRevData.Tables.Count == 0) || (dsExpRevData != null && dsExpRevData.Tables[0].Rows.Count == 0))
            {
                ErrorMessage = Msg.GetMsg(ctx, "VAS_ExpRevdatanotFound");
            }
            return dsExpRevData;
        }
        /// <summary>
        /// This Function is use to get the data of invoice schedule and payment/cash associated with it
        /// </summary>
        /// <param name="ctx">Context</param>
        /// <param name="InvoiceId">InvoiceId</param>
        /// <param name="pageNo">pageNo</param>
        /// <param name="pageSize">pageSize</param>
        /// <returns>returns the data</returns>
        /// <author>VIS_427</author>
        public List<VAS_ScheduleDetail> GetScheduleData(Ctx ctx, int InvoiceId, int pageNo, int pageSize)
        {
            List<VAS_ScheduleDetail> InvocieTaxTabPanel = new List<VAS_ScheduleDetail>();
            String sql = @"SELECT
                               cs.C_InvoicePaySchedule_ID,
                               cs.DueDate,
                               cs.DueAmt,
                               cs.VA009_PaymentMethod_ID,
                               cs.VA009_IsPaid,
                               cy.StdPrecision,
                               p.DocumentNo AS PaymentDoc,
                               pm.VA009_Name AS PayMethod,
                               p.DateAcct AS PaymentDateAcct,
                               p.C_BankAccount_ID,
                               b.Name || '_' || cb.AccountNo As AcctName,
                               COALESCE(bsl.TrxNo, bsl.EftCheckNo, p.CheckNo) AS CheckNo,
                               COALESCE(bsl.EftValutaDate, p.CheckDate) AS CheckDate,
                               ch.DateAcct AS CashAcctDate,
                               ch.DocumentNo AS CashDoc,
                               cs.C_CashLine_ID,
                               cs.C_Payment_ID
                           FROM 
                               C_InvoicePaySchedule cs 
                           INNER JOIN 
                               C_Invoice ci ON (ci.C_Invoice_ID = cs.C_Invoice_ID)
                           INNER JOIN 
                               C_Currency cy ON (cy.C_Currency_ID = ci.C_Currency_ID)
                           INNER JOIN 
                               VA009_PaymentMethod pm ON (cs.VA009_PaymentMethod_ID=pm.VA009_PaymentMethod_ID)
                           LEFT JOIN 
                               C_Payment p ON (cs.C_Payment_ID = p.C_Payment_ID)
                           LEFT JOIN 
                               C_CashLine cl ON (cl.C_CashLine_ID = cs.C_CashLine_ID)
                           LEFT JOIN 
                               C_Cash ch ON (cl.C_Cash_ID = ch.C_Cash_ID)
                           LEFT JOIN 
                               C_BankAccount cb ON (cb.C_BankAccount_ID = p.C_BankAccount_ID)
                           LEFT JOIN 
                               C_Bank b ON (b.C_Bank_ID = cb.C_Bank_ID)
                           LEFT JOIN 
                               C_BankStatementLine bsl ON (bsl.C_Payment_ID = p.C_Payment_ID OR bsl.C_CashLine_ID = cl.C_CashLine_ID)
                           WHERE 
                               cs.C_Invoice_ID  = " + InvoiceId;
            sql += " ORDER BY cs.DueDate ";

            DataSet ds = DB.ExecuteDataset(sql.ToString(), null, null, pageSize, pageNo);
            if (ds != null && ds.Tables[0].Rows.Count > 0)
            {
                int RecordCount = Util.GetValueOfInt(DB.ExecuteScalar("SELECT COUNT(*) FROM (" + sql + ")t", null, null));
                for (int i = 0; i < ds.Tables[0].Rows.Count; i++)
                {
                    VAS_ScheduleDetail obj = new VAS_ScheduleDetail();
                    obj.RecordCount = RecordCount;
                    obj.LineNum = i + 1;
                    obj.DueAmt = Util.GetValueOfDecimal(ds.Tables[0].Rows[i]["DueAmt"]);
                    obj.IsPaid = Util.GetValueOfString(ds.Tables[0].Rows[i]["VA009_IsPaid"]);
                    if (Util.GetValueOfInt(ds.Tables[0].Rows[i]["C_Payment_ID"]) != 0)
                    {
                        obj.DateAcct = Util.GetValueOfDateTime(ds.Tables[0].Rows[i]["PaymentDateAcct"]);
                        obj.DocumentNo = Util.GetValueOfString(ds.Tables[0].Rows[i]["PaymentDoc"]);
                        obj.CheckNo = Util.GetValueOfString(ds.Tables[0].Rows[i]["CheckNo"]);
                        obj.CheckDate = Util.GetValueOfDateTime(ds.Tables[0].Rows[i]["CheckDate"]);
                    }
                    else if (Util.GetValueOfInt(ds.Tables[0].Rows[i]["C_CASHLINE_ID"]) != 0)
                    {
                        obj.DateAcct = Util.GetValueOfDateTime(ds.Tables[0].Rows[i]["CashAcctDate"]);
                        obj.DocumentNo = Util.GetValueOfString(ds.Tables[0].Rows[i]["CashDoc"]);
                    }
                    obj.DueDate = Util.GetValueOfDateTime(ds.Tables[0].Rows[i]["DueDate"]).Value;
                    obj.PayMethod = Util.GetValueOfString(ds.Tables[0].Rows[i]["PayMethod"]);
                    obj.AccountNo = Util.GetValueOfString(ds.Tables[0].Rows[i]["AcctName"]);
                    obj.stdPrecision = Util.GetValueOfInt(ds.Tables[0].Rows[i]["StdPrecision"]);
                    obj.C_InvoicePaySchedule_ID = Util.GetValueOfInt(ds.Tables[0].Rows[i]["C_InvoicePaySchedule_ID"]);
                    InvocieTaxTabPanel.Add(obj);
                }
            }
            return InvocieTaxTabPanel;
        }
        /// <summary>
        /// This function is Used to show Expected Payments against orders and Invoice
        /// </summary>
        /// <param name="ISOtrx">ISOtrx</param>
        /// <param name="ctx">Context</param>
        /// <param name="ListValue">ListValue</param>
        /// <param name="pageNo">pageNo</param>
        /// <param name="pageSize">pageSize</param>
        /// <param name="C_BPartner_ID">C_BPartner_ID</param>
        /// <param name="FinancialPeriodValue">FinancialPeriodValue</param>
        /// <param name="fromDate">fromDate</param>
        /// <param name="toDate">toDate</param>
        /// <param name="docTypeValue">docTypeValue</param>
        /// <author>VIS_427</author>
        /// <returns>List of data of Expected Payment against order and Invoice</returns>
        public List<ExpectedPayment> GetExpectedPaymentData(Ctx ctx, bool ISOtrx, int pageNo, int pageSize, string FinancialPeriodValue,
               string C_BPartner_ID, string fromDate, string toDate, string docTypeValue)
        {
            ExpectedPayment obj = new ExpectedPayment();
            StringBuilder sqlmain = new StringBuilder();
            StringBuilder sql = new StringBuilder();
            int CurrentYear = 0;
            int calendar_ID = 0;
            int InvoiceWinId = 0;
            int OrderWinId = 0;
            int ExpInvID = 0;
            int PeriodID = 0;
            List<ExpectedPayment> invGrandTotalData = new List<ExpectedPayment>();
            string OrderCheck = (ISOtrx == true ? " AND co.IsSOTrx='Y' " : " AND co.IsSOTrx='N' ");
            string InvoiceCheck = (ISOtrx == true ? " AND ci.IsSOTrx='Y' " : " AND ci.IsSOTrx='N' ");
            var C_Currency_ID = ctx.GetContextAsInt("$C_Currency_ID");
           // string BPCheck = (ISOtrx == true ? " AND cb.IsCustomer='Y' " : " ");
            sql.Append($@"SELECT * FROM  (");
            //If Doctype Value is Null or ALL or Invoice
            if (docTypeValue == null || docTypeValue == "01" || docTypeValue == "03")
            {
                sqlmain.Append(MRole.GetDefault(ctx).AddAccessSQL($@"
                             SELECT DISTINCT ci.C_Invoice_ID AS Record_ID,cs.DueDate,
                             CASE WHEN cd.DocBaseType IN ('ARC','APC') THEN - cs.DueAmt
                             ELSE cs.DueAmt END AS DueAmt,
                             ci.DocumentNo,cb.Name,pm.VA009_Name,cy.ISO_Code,CASE WHEN cy.Cursymbol IS NOT NULL THEN cy.Cursymbol ELSE cy.ISO_Code END AS Symbol,
                             cy.StdPrecision,cb.pic,custimg.ImageExtension,'Invoice' AS WindowType,cd.IsExpenseInvoice AS IsExInv
                             FROM C_InvoicePaySchedule cs
                             INNER JOIN C_Invoice ci ON (cs.C_Invoice_ID = ci.C_Invoice_ID)
                             INNER JOIN C_DocType cd ON (cd.C_DocType_ID = ci.C_DocTypeTarget_ID)
                             INNER JOIN C_BPartner cb ON (ci.C_BPartner_ID = cb.C_BPartner_ID)
                             INNER JOIN C_Currency cy ON (cy.C_Currency_ID=ci.C_Currency_ID)
                             INNER JOIN VA009_PaymentMethod pm ON (cs.VA009_PaymentMethod_ID=pm.VA009_PaymentMethod_ID)
                             LEFT JOIN AD_Image custimg ON (custimg.AD_Image_ID = CAST(cb.Pic AS INTEGER))", "cs", MRole.SQL_FULLYQUALIFIED, MRole.SQL_RW));
                sqlmain.Append(InvoiceCheck + " AND ci.DocStatus IN ('CO','CL') AND cs.VA009_IsPaid='N'");
                //Added business partner condition 
                if (Util.GetValueOfInt(C_BPartner_ID) != 0)
                {
                    sqlmain.Append(" AND ci.C_BPartner_ID=" + Util.GetValueOfInt(C_BPartner_ID));
                }
                if (!String.IsNullOrEmpty(FinancialPeriodValue))
                {
                    //Getting the details of current financialYear
                    DataSet dsFinancialYear = GetFinancialYearDetail(ctx, out string errorMessage);
                    if (dsFinancialYear != null && dsFinancialYear.Tables[0].Rows.Count > 0)
                    {
                        CurrentYear = Util.GetValueOfInt(dsFinancialYear.Tables[0].Rows[0]["CalendarYears"]);
                        calendar_ID = Util.GetValueOfInt(dsFinancialYear.Tables[0].Rows[0]["C_Calendar_ID"]);
                        PeriodID = Util.GetValueOfInt(dsFinancialYear.Tables[0].Rows[0]["C_Period_ID"]);
                    }

                    // This month
                    if (FinancialPeriodValue == "01")
                    {
                        DataSet dsPeriod = null;
                        //this dataset returns start and end date of period
                        if (PeriodID > 0)
                        {
                            dsPeriod = GetPeriodData(ctx, PeriodID);
                        }
                        if (dsPeriod != null && dsPeriod.Tables[0].Rows.Count > 0)
                        {
                            string StartDate = Util.GetValueOfString(dsPeriod.Tables[0].Rows[0]["StartDate"]);
                            string EndDate = Util.GetValueOfString(dsPeriod.Tables[0].Rows[0]["EndDate"]);
                            sqlmain.Append(@" AND TRUNC(cs.DueDate) BETWEEN " +
                            (GlobalVariable.TO_DATE(Util.GetValueOfDateTime(StartDate), true)));
                            sqlmain.Append(@"AND " +
                            (GlobalVariable.TO_DATE(Util.GetValueOfDateTime(EndDate), true)));
                        }
                    }
                    // Next month
                    else if (FinancialPeriodValue == "02")
                    {
                        DataSet dsPeriod = null;
                        //this function returns the period id of next period
                        int C_Period_ID = GetNextPeriod(CurrentYear, ctx.GetAD_Client_ID(), calendar_ID);
                        if (C_Period_ID > 0)
                        {
                            dsPeriod = GetPeriodData(ctx, C_Period_ID);
                        }
                        if (dsPeriod != null && dsPeriod.Tables[0].Rows.Count > 0)
                        {
                            string StartDate = Util.GetValueOfString(dsPeriod.Tables[0].Rows[0]["StartDate"]);
                            string EndDate = Util.GetValueOfString(dsPeriod.Tables[0].Rows[0]["EndDate"]);
                            sqlmain.Append(@" AND TRUNC(cs.DueDate) BETWEEN " +
                            (GlobalVariable.TO_DATE(Util.GetValueOfDateTime(StartDate), true)));
                            sqlmain.Append(@"AND " +
                            (GlobalVariable.TO_DATE(Util.GetValueOfDateTime(EndDate), true)));
                        }
                    }
                    //Passed Due Date
                    else if (FinancialPeriodValue == "03")
                    {
                        sqlmain.Append(" AND Current_Date > cs.DueDate");
                    }
                }
                //if user enter from date but not to date and from date less then Current date then this condition will execute
                if (!String.IsNullOrEmpty(fromDate) && String.IsNullOrEmpty(toDate) && Util.GetValueOfDateTime(fromDate) < DateTime.Now)
                {
                    sqlmain.Append(@" AND TRUNC(cs.DueDate) BETWEEN " +
                    (GlobalVariable.TO_DATE(Util.GetValueOfDateTime(fromDate), true)));
                    sqlmain.Append(@"AND Current_Date");
                }
                //if user enter from date and to date then this condition will execute
                else if (!String.IsNullOrEmpty(fromDate) && !String.IsNullOrEmpty(toDate))
                {
                    sqlmain.Append(@" AND TRUNC(cs.DueDate) BETWEEN " +
                    (GlobalVariable.TO_DATE(Util.GetValueOfDateTime(fromDate), true)));
                    sqlmain.Append(@"AND " +
                    (GlobalVariable.TO_DATE(Util.GetValueOfDateTime(toDate), true)));
                }
                //if user enter does not enter from date but enters todate then this condition will execute
                else if (String.IsNullOrEmpty(fromDate) && !String.IsNullOrEmpty(toDate))
                {
                    sql.Append(@" AND TRUNC(cs.DueDate) <= " +
                    (GlobalVariable.TO_DATE(Util.GetValueOfDateTime(toDate), true)));
                }
                //if from date greater then today's date
                else if (Util.GetValueOfDateTime(fromDate) > DateTime.Now)
                {
                    toDate = fromDate;
                    sqlmain.Append(@" AND TRUNC(cs.DueDate) BETWEEN " +
                    (GlobalVariable.TO_DATE(Util.GetValueOfDateTime(fromDate), true)));
                    sqlmain.Append(@"AND " +
                    (GlobalVariable.TO_DATE(Util.GetValueOfDateTime(toDate), true)));
                }
            }
            //If Doctype Value is Null or ALL
            if (docTypeValue == null || docTypeValue == "01")
            {
                sqlmain.Append(" UNION ALL ");
            }
            //If Doctype Value is Null or ALL or Order
            if (docTypeValue == null || docTypeValue == "01" || docTypeValue == "02")
            {
                sqlmain.Append(MRole.GetDefault(ctx).AddAccessSQL($@"
                             SELECT DISTINCT co.C_Order_ID AS Record_ID,ps.DueDate,
                             ps.DueAmt AS DueAmt,
                             co.DocumentNo,cb.Name,pm.VA009_Name,cy.ISO_Code,CASE WHEN cy.Cursymbol IS NOT NULL THEN cy.Cursymbol ELSE cy.ISO_Code END AS Symbol,
                             cy.StdPrecision,cb.pic,custimg.ImageExtension,'Order' AS WindowType,'N' AS IsExInv
                             FROM VA009_OrderPaySchedule ps
                             INNER JOIN C_Order co ON (ps.C_Order_ID = co.C_Order_ID)
                             INNER JOIN C_BPartner cb ON (co.C_BPartner_ID = cb.C_BPartner_ID)
                             INNER JOIN C_Currency cy ON (cy.C_Currency_ID=co.C_Currency_ID)
                             INNER JOIN VA009_PaymentMethod pm ON (co.VA009_PaymentMethod_ID=pm.VA009_PaymentMethod_ID)
                             LEFT JOIN AD_Image custimg ON (custimg.AD_Image_ID = CAST(cb.Pic AS INTEGER))", "ps", MRole.SQL_FULLYQUALIFIED, MRole.SQL_RW));
                sqlmain.Append(OrderCheck + " AND co.DocStatus IN ('CO','CL') AND ps.VA009_IsPaid='N'");
                //Added business partner condition 
                if (Util.GetValueOfInt(C_BPartner_ID) != 0)
                {
                    sqlmain.Append(" AND co.C_BPartner_ID=" + Util.GetValueOfInt(C_BPartner_ID));
                }
                if (!String.IsNullOrEmpty(FinancialPeriodValue))
                {
                    DataSet dsFinancialYear = GetFinancialYearDetail(ctx, out string errorMessage);
                    if (dsFinancialYear != null && dsFinancialYear.Tables[0].Rows.Count > 0)
                    {
                        CurrentYear = Util.GetValueOfInt(dsFinancialYear.Tables[0].Rows[0]["CalendarYears"]);
                        calendar_ID = Util.GetValueOfInt(dsFinancialYear.Tables[0].Rows[0]["C_Calendar_ID"]);
                        PeriodID = Util.GetValueOfInt(dsFinancialYear.Tables[0].Rows[0]["C_Period_ID"]);
                    }

                    // This month
                    if (FinancialPeriodValue == "01")
                    {
                        DataSet dsPeriod = null;
                        //this dataset returns start and end date of period
                        if (PeriodID > 0)
                        {
                            dsPeriod = GetPeriodData(ctx, PeriodID);
                        }
                        if (dsPeriod != null && dsPeriod.Tables[0].Rows.Count > 0)
                        {
                            string StartDate = Util.GetValueOfString(dsPeriod.Tables[0].Rows[0]["StartDate"]);
                            string EndDate = Util.GetValueOfString(dsPeriod.Tables[0].Rows[0]["EndDate"]);
                            sqlmain.Append(@" AND TRUNC(ps.DueDate) BETWEEN " +
                            (GlobalVariable.TO_DATE(Util.GetValueOfDateTime(StartDate), true)));
                            sqlmain.Append(@"AND " +
                            (GlobalVariable.TO_DATE(Util.GetValueOfDateTime(EndDate), true)));
                        }
                    }
                    // Next month
                    else if (FinancialPeriodValue == "02")
                    {
                        int C_Period_ID = GetNextPeriod(CurrentYear, ctx.GetAD_Client_ID(), calendar_ID);
                        DataSet dsPeriod = null;
                        if (C_Period_ID > 0)
                        {
                            dsPeriod = GetPeriodData(ctx, C_Period_ID);
                        }
                        if (dsPeriod != null && dsPeriod.Tables[0].Rows.Count > 0)
                        {
                            string StartDate = Util.GetValueOfString(dsPeriod.Tables[0].Rows[0]["StartDate"]);
                            string EndDate = Util.GetValueOfString(dsPeriod.Tables[0].Rows[0]["EndDate"]);
                            sqlmain.Append(@" AND TRUNC(ps.DueDate) BETWEEN " +
                            (GlobalVariable.TO_DATE(Util.GetValueOfDateTime(StartDate), true)));
                            sqlmain.Append(@"AND " +
                            (GlobalVariable.TO_DATE(Util.GetValueOfDateTime(EndDate), true)));
                        }
                    }
                    //Passed Due Date
                    else if (FinancialPeriodValue == "03")
                    {
                        sqlmain.Append(" AND Current_Date > ps.DueDate");
                    }
                }
                //if user enter from date but not to date and from date less then Current date then this condition will execute
                if (!String.IsNullOrEmpty(fromDate) && String.IsNullOrEmpty(toDate) && Util.GetValueOfDateTime(fromDate) < DateTime.Now)
                {
                    sqlmain.Append(@" AND TRUNC(ps.DueDate) BETWEEN " +
                    (GlobalVariable.TO_DATE(Util.GetValueOfDateTime(fromDate), true)));
                    sqlmain.Append(@"AND Current_Date");
                }
                //if user enter from date and to date then this condition will execute
                else if (!String.IsNullOrEmpty(fromDate) && !String.IsNullOrEmpty(toDate))
                {
                    sqlmain.Append(@" AND TRUNC(ps.DueDate) BETWEEN " +
                    (GlobalVariable.TO_DATE(Util.GetValueOfDateTime(fromDate), true)));
                    sqlmain.Append(@"AND " +
                    (GlobalVariable.TO_DATE(Util.GetValueOfDateTime(toDate), true)));
                }
                //if user enter does not enter from date but enters todate then this condition will execute
                else if (String.IsNullOrEmpty(fromDate) && !String.IsNullOrEmpty(toDate))
                {
                    sqlmain.Append(@" AND TRUNC(ps.DueDate) <= " +
                    (GlobalVariable.TO_DATE(Util.GetValueOfDateTime(toDate), true)));
                }
                //if from date greater then today's date
                else if (Util.GetValueOfDateTime(fromDate) > DateTime.Now)
                {
                    toDate = fromDate;
                    sqlmain.Append(@" AND TRUNC(cs.DueDate) BETWEEN " +
                    (GlobalVariable.TO_DATE(Util.GetValueOfDateTime(fromDate), true)));
                    sqlmain.Append(@"AND " +
                    (GlobalVariable.TO_DATE(Util.GetValueOfDateTime(toDate), true)));
                }
            }
            sql.Append(sqlmain);
            sql.Append(")T ORDER BY T.DueDate");
            DataSet ds = DB.ExecuteDataset(sql.ToString(), null, null, pageSize, pageNo);
            if (ds != null && ds.Tables[0].Rows.Count > 0)
            {
                //fetching the record count to use it for pagination
                int RecordCount = Util.GetValueOfInt(DB.ExecuteScalar("SELECT COUNT(*) FROM (" + sqlmain.ToString() + ")t", null, null));
                sql.Clear();
                //Getting windows id to zoom the records
                if (ISOtrx)
                {
                    InvoiceWinId = Util.GetValueOfInt(DB.ExecuteScalar("SELECT AD_Window_ID FROM AD_Window WHERE Name='VAS_ARInvoice'", null, null));
                    OrderWinId = Util.GetValueOfInt(DB.ExecuteScalar("SELECT AD_Window_ID FROM AD_Window WHERE Name='VAS_SalesOrder'", null, null));
                }
                else
                {
                    ExpInvID= Util.GetValueOfInt(DB.ExecuteScalar("SELECT AD_Window_ID FROM AD_Window WHERE Name='VAS_ExpenseInvoice'", null, null));
                    InvoiceWinId = Util.GetValueOfInt(DB.ExecuteScalar("SELECT AD_Window_ID FROM AD_Window WHERE Name='VAS_APInvoice'", null, null));
                    OrderWinId = Util.GetValueOfInt(DB.ExecuteScalar("SELECT AD_Window_ID FROM AD_Window WHERE Name='VAS_PurchaseOrder'", null, null));
                }
                for (int i = 0; i < ds.Tables[0].Rows.Count; i++)
                {                   
                    obj = new ExpectedPayment();
                    obj.recordCount = RecordCount;
                    obj.Record_ID = Util.GetValueOfInt(ds.Tables[0].Rows[i]["Record_ID"]);
                    obj.TotalAmt = Util.GetValueOfDecimal(ds.Tables[0].Rows[i]["DueAmt"]);
                    obj.Symbol = Util.GetValueOfString(ds.Tables[0].Rows[i]["Symbol"]);
                    obj.DocumentNo = Util.GetValueOfString(ds.Tables[0].Rows[i]["DocumentNo"]);
                    obj.stdPrecision = Util.GetValueOfInt(ds.Tables[0].Rows[i]["StdPrecision"]);
                    obj.OrderdDate = Util.GetValueOfDateTime(ds.Tables[0].Rows[i]["DueDate"]).Value;
                    obj.Name = Util.GetValueOfString(ds.Tables[0].Rows[i]["Name"]);
                    obj.PayMethod = Util.GetValueOfString(ds.Tables[0].Rows[i]["VA009_Name"]);
                    obj.ISO_Code = Util.GetValueOfString(ds.Tables[0].Rows[i]["ISO_Code"]);
                    obj.windowType = Util.GetValueOfString(ds.Tables[0].Rows[i]["WindowType"]);
                    if(Util.GetValueOfString(ds.Tables[0].Rows[i]["WindowType"]) == "Order")
                    {
                        obj.Window_ID = OrderWinId;
                        obj.Primary_ID = "C_Order_ID";
                    }
                    else if(Util.GetValueOfString(ds.Tables[0].Rows[i]["IsExInv"]) == "Y")
                    {
                        obj.Window_ID = ExpInvID;
                        obj.Primary_ID = "C_Invoice_ID";
                    }
                    else
                    {
                        obj.Window_ID = InvoiceWinId;
                        obj.Primary_ID = "C_Invoice_ID";
                    }
                    if (Util.GetValueOfInt(ds.Tables[0].Rows[i]["Pic"]) != 0)
                    {
                        obj.ImageUrl = "Images/Thumb46x46/" + Util.GetValueOfInt(ds.Tables[0].Rows[i]["Pic"]) + Util.GetValueOfString(ds.Tables[0].Rows[i]["ImageExtension"]);

                    }
                    invGrandTotalData.Add(obj);
                }
            }
            return invGrandTotalData;
        }
        /// <summary>
        /// This function is used to get Periods Data based Period ID
        /// </summary>
        /// <param name="Period_ID">Period_IDr</param>
        /// <param name="ctx">Context</param>
        /// <returns>DataSet</returns>
        /// <author>VIS_427</author>
        public DataSet GetPeriodData(Ctx ctx, int Period_ID)
        {
            string sql = $@"SELECT Min(StartDate) AS StartDate,MAX(EndDate) AS EndDate FROM C_Period WHERE AD_Client_ID = {ctx.GetAD_Client_ID()} AND C_Period_ID=" + Period_ID;

            DataSet dsPeriod = DB.ExecuteDataset(sql, null, null);
            return dsPeriod;
        }
        /// <summary>
        /// This function is used to get Next Periods Period ID based on financial Year
        /// </summary>
        /// <param name="CalendarYear">Current Calendar Year</param>
        /// <param name="AD_Client_ID">Client ID</param>
        /// <param name="C_Calendar_ID">Calendar ID</param>
        /// <returns>DataSet</returns>
        /// <author>VIS_427</author>
        public int GetNextPeriod(int CalendarYear, int AD_Client_ID, int C_Calendar_ID)
        {
            string sql = $@" WITH CurrentPeriod AS (
                            /* Fetch current year data and calculate next period using LEAD */
                            SELECT 
                                pl.periodno, 
                                pl.C_Period_ID, 
                                pl.StartDate, 
                                pl.EndDate,
                                pl.C_Year_ID,
                                pl.AD_Client_ID,
                                CAST(y.CalendarYears AS INT) AS CalendarYears,
                                LEAD(pl.c_period_id) OVER(PARTITION BY y.calendaryears ORDER BY pl.periodno) AS Next_Period_ID
                            FROM 
                                C_Period pl
                                INNER JOIN C_Year y ON y.C_Year_ID = pl.C_Year_ID
                            WHERE pl.IsActive = 'Y' AND 
                                pl.AD_Client_ID = {AD_Client_ID}
                                AND y.C_Calendar_ID = {C_Calendar_ID}
                                AND y.CalendarYears = {GlobalVariable.TO_STRING(CalendarYear.ToString())}
                        ),";

            sql += $@" NextPeriod AS (
                        /* Fetch the Next year's First period (PeriodNo = MIN(PeriodNo)) */
                        SELECT 
                            pl.C_Period_ID
                        FROM 
                            C_Period pl
                            INNER JOIN C_Year y ON y.C_Year_ID = pl.C_Year_ID
                        WHERE 
                            pl.AD_Client_ID = {AD_Client_ID}
                            AND y.C_Calendar_ID = {C_Calendar_ID}
                            AND y.CalendarYears = {GlobalVariable.TO_STRING((CalendarYear + 1).ToString())}
                            AND pl.PeriodNo = (
                                /* Directly get the max period number in the same query */
                                SELECT MIN(PeriodNo) 
                                FROM C_Period pll 
                                WHERE pll.IsActive = 'Y' AND pll.C_Year_ID = pl.C_Year_ID )
                        )";

            sql += $@" SELECT 
                        COALESCE(cp.Next_Period_ID,  pp.C_Period_ID) AS NextPeriod
                    FROM 
                        CurrentPeriod cp
                    LEFT JOIN 
                        NextPeriod pp ON (1=1)
                    WHERE Current_Date Between cp.StartDate and cp.EndDate";

            int C_Period_ID = Util.GetValueOfInt(DB.ExecuteScalar(sql));
            return C_Period_ID;

        }

        /// <summary>
        /// This function is used to get the Invoice Line Details including Order and GRN details
        /// </summary>
        /// <param name="ctx">Context</param>
        /// <param name="InvoiceId">Invoice_ID</param>
        /// <param name="pageNo">Page No</param>
        /// <param name="pageSize">Page Size</param>
        /// <returns>Data</returns>
        /// <author>VIS_045, 16-Nov-2024</author>
        public List<VAS_InvoiceMatchingDetail> GetInvoiceLineMatchData(Ctx ctx, int InvoiceId, int pageNo, int pageSize)
        {
            List<VAS_InvoiceMatchingDetail> InvocieTaxTabPanel = new List<VAS_InvoiceMatchingDetail>();
            String sql = @"SELECT il.Line, p.Name AS ProductName, uom.Name AS UOMName, uom.StdPrecision AS UOMPrecision, c.StdPrecision AS CurrencyPrecision, 
                            il.C_Invoiceline_ID, il.QtyEntered, il.QtyInvoiced, il.C_OrderLine_ID, il.M_InOutLine_ID, il.PriceEntered AS InvoicePrice, i.DocStatus,
                            ol.QtyOrdered , ol.QtyDelivered AS OrderDelivered, ol.QtyInvoiced AS OrderInvoiced, ol.PriceEntered AS OrderPrice, 
                            (SELECT NVL(DueAmt, 0) FROM VA009_OrderPaySchedule ops WHERE ops.C_Order_ID = ol.C_Order_ID AND ops.VA009_IsPaid= 'N') AS NotPaidAdvanceOrder
                            FROM
                            C_Invoice i 
                            INNER JOIN C_InvoiceLine il ON (il.C_Invoice_ID = i.C_Invoice_ID)
                            INNER JOIN M_Product p ON (p.M_Product_ID = il.M_Product_ID)
                            INNER JOIN C_UOM uom ON (uom.C_UOM_ID = p.C_UOM_ID)
                            INNER JOIN C_Currency c ON (c.C_Currency_ID = i.C_Currency_ID) 
                            LEFT JOIN C_OrderLine ol ON (ol.C_OrderLIne_ID = il.C_OrderLine_ID)
                            WHERE il.IsActive = 'Y' AND p.ProductType = 'I' AND i.C_Invoice_ID = " + InvoiceId;
            sql += " ORDER BY il.Line ";

            DataSet ds = DB.ExecuteDataset(sql.ToString(), null, null, pageSize, pageNo);
            if (ds != null && ds.Tables[0].Rows.Count > 0)
            {
                int RecordCount = Util.GetValueOfInt(DB.ExecuteScalar("SELECT COUNT(*) FROM (" + sql + ")t", null, null));
                int DiscrepancyCount = 0;
                decimal TotalAdvanceAmt = 0;
                for (int i = 0; i < ds.Tables[0].Rows.Count; i++)
                {
                    if (CheckDiscrepancyInvoice(ds.Tables[0].Rows[i]))
                    {
                        DiscrepancyCount = DiscrepancyCount + 1;
                    }

                    TotalAdvanceAmt += Util.GetValueOfDecimal(ds.Tables[0].Rows[i]["NotPaidAdvanceOrder"]);
                }

                for (int i = 0; i < ds.Tables[0].Rows.Count; i++)
                {
                    VAS_InvoiceMatchingDetail obj = new VAS_InvoiceMatchingDetail();
                    obj.RecordCount = RecordCount;
                    obj.Line = Util.GetValueOfInt(ds.Tables[0].Rows[i]["Line"]);
                    obj.ProductName = Util.GetValueOfString(ds.Tables[0].Rows[i]["ProductName"]);
                    obj.UomName = Util.GetValueOfString(ds.Tables[0].Rows[i]["UOMName"]);
                    obj.C_Invoiceline_ID = Util.GetValueOfInt(ds.Tables[0].Rows[i]["C_Invoiceline_ID"]);
                    obj.QtyEntered = Util.GetValueOfDecimal(ds.Tables[0].Rows[i]["QtyEntered"]);
                    obj.QtyInvoiced = Util.GetValueOfDecimal(ds.Tables[0].Rows[i]["QtyInvoiced"]);
                    obj.C_OrderLine_ID = Util.GetValueOfInt(ds.Tables[0].Rows[i]["C_OrderLine_ID"]);
                    obj.M_InOutLine_ID = Util.GetValueOfInt(ds.Tables[0].Rows[i]["M_InOutLine_ID"]);
                    obj.QtyOrdered = Util.GetValueOfDecimal(ds.Tables[0].Rows[i]["QtyOrdered"]);
                    obj.OrderDelivered = Util.GetValueOfDecimal(ds.Tables[0].Rows[i]["OrderDelivered"]);
                    obj.OrderInvoiced = Util.GetValueOfDecimal(ds.Tables[0].Rows[i]["OrderInvoiced"]);
                    obj.ExpectedOrder = Util.GetValueOfDecimal(ds.Tables[0].Rows[i]["QtyOrdered"]) - Util.GetValueOfDecimal(ds.Tables[0].Rows[i]["OrderInvoiced"]);
                    obj.ExpectedGRN = Util.GetValueOfDecimal(ds.Tables[0].Rows[i]["OrderDelivered"]) - Util.GetValueOfDecimal(ds.Tables[0].Rows[i]["OrderInvoiced"]);
                    obj.OrderPrice = Util.GetValueOfDecimal(ds.Tables[0].Rows[i]["OrderPrice"]);
                    obj.InvoicePrice = Util.GetValueOfDecimal(ds.Tables[0].Rows[i]["InvoicePrice"]);
                    obj.UOMPrecision = Util.GetValueOfInt(ds.Tables[0].Rows[i]["UOMPrecision"]);
                    obj.CurrencyPrecision = Util.GetValueOfInt(ds.Tables[0].Rows[i]["CurrencyPrecision"]);
                    obj.DocStatus = Util.GetValueOfString(ds.Tables[0].Rows[i]["DocStatus"]);
                    obj.AdvanceAmt = Util.GetValueOfDecimal(ds.Tables[0].Rows[i]["NotPaidAdvanceOrder"]);
                    obj.IsDiscrepancy = CheckDiscrepancyInvoice(ds.Tables[0].Rows[i]);
                    obj.TotalAdvanceAmt = TotalAdvanceAmt;
                    obj.DiscrepancyCount = DiscrepancyCount;
                    InvocieTaxTabPanel.Add(obj);
                }
            }
            return InvocieTaxTabPanel;
        }

        /// <summary>
        /// This function is used to check any discrepancy found in reccord or not
        /// </summary>
        /// <param name="dr">DataRow</param>
        /// <returns>True when discrepancy found</returns>
        /// <author>VIS_045; 18-Nov-2024</author>
        public bool CheckDiscrepancyInvoice(DataRow dr)
        {
            bool IsDiscrepancy = false;

            // Check record is completed or closed
            bool Iscompleted = Util.GetValueOfString(dr["DocStatus"]).Equals("CO") || Util.GetValueOfString(dr["DocStatus"]).Equals("CL");

            if (Util.GetValueOfInt(dr["C_OrderLine_ID"]) == 0 && Util.GetValueOfInt(dr["M_InOutLine_ID"]) == 0)
            {
                IsDiscrepancy = false;
            }
            else if (Util.GetValueOfInt(dr["C_OrderLine_ID"]) == 0 || Util.GetValueOfInt(dr["M_InOutLine_ID"]) == 0)
            {
                IsDiscrepancy = true;
            }
            // check discrepany found in price in completeted Invoice 
            else if (Iscompleted && (Util.GetValueOfDecimal(dr["OrderPrice"]) - Util.GetValueOfDecimal(dr["InvoicePrice"]) < 0))
            {
                IsDiscrepancy = true;
            }
            // Check discrepany found in QtyOrdered and Qty Invoiced or not
            else if ((Util.GetValueOfDecimal(dr["QtyOrdered"]) - Util.GetValueOfDecimal(dr["OrderInvoiced"]) - (Iscompleted ? 0 : Util.GetValueOfDecimal(dr["QtyInvoiced"])) < 0) ||
                (Util.GetValueOfDecimal(dr["OrderDelivered"]) - Util.GetValueOfDecimal(dr["OrderInvoiced"]) - (Iscompleted ? 0 : Util.GetValueOfDecimal(dr["QtyInvoiced"])) < 0) ||
                (Util.GetValueOfDecimal(dr["OrderPrice"]) - Util.GetValueOfDecimal(dr["InvoicePrice"]) < 0))
            {
                IsDiscrepancy = true;
            }
            return IsDiscrepancy;
        }
        /// <summary>
        /// This function is used to get Cash Flow data
        /// </summary>
        /// <param name="ListValue">ListValue</param>
        /// <param name="ctx">Context</param>
        /// <returns>list of cash out and cash in amount</returns>
        /// <author>VIS_427</author>
        public List<CashFlowClass> GetCashFlowData(Ctx ctx, string ListValue)
        {
            CashFlowClass obj = new CashFlowClass();
            StringBuilder sqlmain = new StringBuilder();
            StringBuilder sql = new StringBuilder();
            StringBuilder sqlOrder = new StringBuilder();
            DataSet dsPeriod = null;
            DataSet dsYear = null;
            int PeriodID = 0;
            int C_Currency_ID = ctx.GetContextAsInt("$C_Currency_ID");
            List<CashFlowClass> invGrandTotalData = new List<CashFlowClass>();
            int CurrentYear = 0;
            int calendar_ID = 0;
            int currQuarter = 0;
            bool isNextYearQuarter = false;
            // Get Financial Year Data 
            DataSet dsFinancialYear = GetFinancialYearDetail(ctx, out string errorMessage);
            if (dsFinancialYear != null && dsFinancialYear.Tables[0].Rows.Count > 0)
            {
                CurrentYear = Util.GetValueOfInt(dsFinancialYear.Tables[0].Rows[0]["CalendarYears"]);
                calendar_ID = Util.GetValueOfInt(dsFinancialYear.Tables[0].Rows[0]["C_Calendar_ID"]);
                PeriodID = Util.GetValueOfInt(dsFinancialYear.Tables[0].Rows[0]["C_Period_ID"]);
                currQuarter = Util.GetValueOfInt(dsFinancialYear.Tables[0].Rows[0]["CurQuarter"]);
            }
            sql.Append(@"SELECT StdPrecision FROM C_Currency WHERE C_Currency_ID=" + C_Currency_ID);
            int precision = Util.GetValueOfInt(DB.ExecuteScalar(sql.ToString(), null, null));
            sql.Clear();
            sql.Append(@"SELECT SUM(t.CashOutbifurcated_Amount) AS CashOutAmt,SUM(t.CashInbifurcated_Amount) AS CashInAmt FROM (");
            sqlOrder.Append($@"SELECT 
                         o.DOCUMENTNO,
                         o.DATEPROMISED AS promised_date,
                         doc.DocBaseType,
                         o.IsSotrx,
                         o.IsReturnTrx,
                         o.GRANDTOTAL,
                         o.C_PAYMENTTERM_ID,
                         case when ps.C_PaySchedule_ID IS NULL THEN pt.NETDAYS ELSE ps.NETDAYS END AS NETDAYS,
                         ps.PERCENTAGE,
                         CASE WHEN o.IsSOtrx='N' THEN
                         currencyConvert(
                             CASE 
                                 WHEN ps.C_PaySchedule_ID IS NULL THEN 
                                     CASE WHEN o.IsReturnTrx = 'Y' THEN -1 ELSE 1 END * ROUND(NVL(o.GRANDTOTAL - SUM(NVL(il.LineTotalAmt, 0)), 0)," + precision + @")
                                 ELSE 
                                     CASE WHEN o.IsReturnTrx = 'Y' THEN -1 ELSE 1 END * ROUND((NVL((o.GRANDTOTAL - SUM(NVL(il.LineTotalAmt, 0))) * ps.PERCENTAGE, 0) / 100), " + precision + @")
                             END, 
                             o.C_Currency_ID, 
                             " + C_Currency_ID + @", 
                             o.DateOrdered, 
                             o.C_ConversionType_ID, 
                             o.AD_Client_ID, 
                             o.AD_Org_ID
                         )
                         ELSE 0 END AS CashOutbifurcated_Amount,
                         CASE WHEN o.IsSOtrx='Y' THEN
                         currencyConvert(
                             CASE 
                                 WHEN ps.C_PaySchedule_ID IS NULL THEN 
                                     CASE WHEN o.IsReturnTrx = 'Y' THEN -1 ELSE 1 END * ROUND(NVL(o.GRANDTOTAL - SUM(NVL(il.LineTotalAmt, 0)), 0), " + precision + @")
                                 ELSE 
                                     CASE WHEN o.IsReturnTrx = 'Y' THEN -1 ELSE 1 END * ROUND((NVL((o.GRANDTOTAL - SUM(NVL(il.LineTotalAmt, 0))) * ps.PERCENTAGE, 0) / 100), " + precision + @")
                             END, 
                             o.C_Currency_ID, 
                             " + C_Currency_ID + @", 
                             o.DateOrdered, 
                             o.C_ConversionType_ID, 
                             o.AD_Client_ID, 
                             o.AD_Org_ID
                         ) 
                         ELSE 0 END AS CashInbifurcated_Amount,");            
            if(DB.IsPostgreSQL())
            {
                sqlOrder.Append(@"(o.DATEPROMISED + (CASE 
                                 WHEN ps.C_PaySchedule_ID IS NULL THEN pt.NETDAYS
                                 ELSE ps.NETDAYS
                                 END || ' days')::interval) AS expected_due_date");
            }
            else
            {
                sqlOrder.Append("o.DATEPROMISED + case when ps.C_PaySchedule_ID IS NULL THEN pt.NETDAYS ELSE ps.NETDAYS END  AS expected_due_date");
            }
            sqlOrder.Append(@" FROM 
                         C_Order o
                     INNER JOIN C_OrderLine ol ON o.C_Order_ID = ol.C_Order_ID
                     INNER JOIN C_DocType doc ON doc.C_DocType_ID = o.C_DocTypeTarget_ID
                     INNER JOIN C_PAYMENTTERM pt ON o.C_PAYMENTTERM_ID = pt.C_PAYMENTTERM_ID AND pt.VA009_Advance = 'N'
                     LEFT JOIN C_PAYSCHEDULE ps ON pt.C_PAYMENTTERM_ID = ps.C_PAYMENTTERM_ID AND ps.VA009_Advance = 'N'
                     LEFT JOIN C_InvoiceLine il ON il.C_OrderLine_ID = ol.C_OrderLine_ID AND il.Processed = 'Y' AND il.ReversalDoc_ID IS NULL
                     WHERE 
                         o.DOCSTATUS IN ('CO', 'CL') 
                         AND doc.DocBaseType IN ('SOO','POO')");
            sqlmain.Append(MRole.GetDefault(ctx).AddAccessSQL(sqlOrder.ToString(), "o", MRole.SQL_FULLYQUALIFIED, MRole.SQL_RW));
            sqlmain.Append(@" GROUP BY 
                         o.DOCUMENTNO,
                         o.DATEPROMISED,
                         doc.DocBaseType,
                         o.IsSotrx,
                         o.IsReturnTrx,
                         o.GRANDTOTAL,
                         o.C_PAYMENTTERM_ID,
                         ps.C_PaySchedule_ID,
                         pt.NETDAYS, 
                         ps.NETDAYS,
                         ps.PERCENTAGE,
                         o.C_Currency_ID, 
                         o.DateOrdered, 
                         o.C_ConversionType_ID, 
                         o.AD_Client_ID, 
                         o.AD_Org_ID");
            sqlmain.Append(@" UNION ALL ");
            sqlmain.Append(MRole.GetDefault(ctx).AddAccessSQL($@"
                         SELECT 
                         o.DOCUMENTNO,
                         o.DATEInvoiced AS promised_date,
                         doc.DocBaseType,
                         o.IsSotrx,
                         o.IsReturnTrx,
                         o.GRANDTOTAL,
                         o.C_PAYMENTTERM_ID,
                         0 AS NETDAYS,
                         0 AS PERCENTAGE,
                         CASE WHEN o.IsSOtrx='N' THEN
                         currencyConvert(CASE WHEN o.IsReturnTrx = 'Y' THEN -1 ELSE 1 END * ips.DueAmt, o.C_Currency_ID, 
                             " + C_Currency_ID + @", 
                             o.DateInvoiced, 
                             o.C_ConversionType_ID, 
                             o.AD_Client_ID, 
                             o.AD_Org_ID
                         ) ELSE 0 END AS CashOutbifurcated_Amount,
                         CASE WHEN o.IsSOtrx='Y' THEN
                         currencyConvert(CASE WHEN o.IsReturnTrx = 'Y' THEN -1 ELSE 1 END * ips.DueAmt, o.C_Currency_ID, 
                             " + C_Currency_ID + @", 
                             o.DateInvoiced, 
                             o.C_ConversionType_ID, 
                             o.AD_Client_ID, 
                             o.AD_Org_ID
                         ) ELSE 0 END AS CashInbifurcated_Amount,
                         ips.DueDate AS expected_due_date
                     FROM 
                         C_Invoice o
                     INNER JOIN C_InvoicePaySchedule ips ON (o.C_Invoice_ID = ips.C_Invoice_ID) 
                     INNER JOIN C_DocType doc ON doc.C_DocType_ID = o.C_DocTypeTarget_ID
                     WHERE 
                          o.DOCSTATUS IN ( 'CO', 'CL')  
                          AND ips.VA009_IsPaid = 'N' AND doc.IsExpenseInvoice = 'N'", "o", MRole.SQL_FULLYQUALIFIED, MRole.SQL_RW));
            sqlmain.Append(" UNION ALL ");
            sqlmain.Append(MRole.GetDefault(ctx).AddAccessSQL($@" 
                          SELECT 
                         o.DOCUMENTNO,
                         o.DATEPROMISED AS promised_date,
                         doc.DocBaseType,
                         o.IsSotrx,
                         o.IsReturnTrx,
                         o.GRANDTOTAL,
                         o.C_PAYMENTTERM_ID,
                         0 AS NETDAYS,
                         0 AS PERCENTAGE,
                         CASE WHEN o.IsSOtrx='N' THEN
                         currencyConvert(CASE WHEN o.IsReturnTrx = 'Y' THEN -1 ELSE 1 END * ips.DueAmt, o.C_Currency_ID, 
                             " + C_Currency_ID + @", 
                             o.DateOrdered, 
                             o.C_ConversionType_ID, 
                             o.AD_Client_ID, 
                             o.AD_Org_ID
                         ) ELSE 0 END AS CashOutbifurcated_Amount,
                         CASE WHEN o.IsSOtrx='Y' THEN
                         currencyConvert(CASE WHEN o.IsReturnTrx = 'Y' THEN -1 ELSE 1 END * ips.DueAmt, o.C_Currency_ID, 
                             " + C_Currency_ID + @", 
                             o.DateOrdered, 
                             o.C_ConversionType_ID, 
                             o.AD_Client_ID, 
                             o.AD_Org_ID
                         ) ELSE 0 END AS CashInbifurcated_Amount,
                         ips.DueDate AS expected_due_date
                     FROM 
                         C_Order o
                     INNER JOIN VA009_OrderPaySchedule ips ON (o.C_Order_ID = ips.C_Order_ID) 
                     INNER JOIN C_DocType doc ON doc.C_DocType_ID = o.C_DocTypeTarget_ID
                     WHERE 
                          o.DOCSTATUS IN ( 'CO', 'CL') AND doc.DocBaseType IN ('SOO','POO')
                          AND ips.VA009_IsPaid = 'N'", "o", MRole.SQL_FULLYQUALIFIED, MRole.SQL_RW));
            sql.Append(sqlmain);
            sql.Append(")t WHERE ");

            // This month
            if (ListValue == "01")
            {
                //this dataset returns start and end date of period
                if (PeriodID > 0)
                {
                    dsPeriod = GetPeriodData(ctx, PeriodID);
                }
                if (dsPeriod != null && dsPeriod.Tables[0].Rows.Count > 0)
                {
                    string StartDate = Util.GetValueOfString(dsPeriod.Tables[0].Rows[0]["StartDate"]);
                    string EndDate = Util.GetValueOfString(dsPeriod.Tables[0].Rows[0]["EndDate"]);
                    sql.Append(@" TRUNC(t.expected_due_date) BETWEEN " +
                    (GlobalVariable.TO_DATE(Util.GetValueOfDateTime(StartDate), true)));
                    sql.Append(@"AND " +
                    (GlobalVariable.TO_DATE(Util.GetValueOfDateTime(EndDate), true)));
                }
            }
            //Next Month
            else if (ListValue == "02")
            {
                int C_Period_ID = GetNextPeriod(CurrentYear, ctx.GetAD_Client_ID(), calendar_ID);
                dsPeriod = null;
                if (C_Period_ID > 0)
                {
                    dsPeriod = GetPeriodData(ctx, C_Period_ID);
                }
                if (dsPeriod != null && dsPeriod.Tables[0].Rows.Count > 0)
                {
                    string StartDate = Util.GetValueOfString(dsPeriod.Tables[0].Rows[0]["StartDate"]);
                    string EndDate = Util.GetValueOfString(dsPeriod.Tables[0].Rows[0]["EndDate"]);
                    sqlmain.Append(@" AND TRUNC(t.expected_due_date) BETWEEN " +
                    (GlobalVariable.TO_DATE(Util.GetValueOfDateTime(StartDate), true)));
                    sqlmain.Append(@"AND " +
                    (GlobalVariable.TO_DATE(Util.GetValueOfDateTime(EndDate), true)));
                }
            }
            //This Quarter Year
            else if (ListValue.Equals("03"))
            {
                string quarterSql = $@"SELECT Min(p.StartDate) AS StartDate,MAX(p.EndDate) AS EndDate FROM C_Period p INNER JOIN C_Year y ON (p.C_Year_ID = y.C_Year_ID)
                                                    WHERE CEIL(p.PeriodNo/3)={currQuarter} AND y.CalendarYears={GlobalVariable.TO_STRING(CurrentYear.ToString())} AND y.C_Calendar_ID={calendar_ID}";
                dsYear = DB.ExecuteDataset(quarterSql);
                string StartDate = Util.GetValueOfString(dsYear.Tables[0].Rows[0]["StartDate"]);
                string EndDate = Util.GetValueOfString(dsYear.Tables[0].Rows[0]["EndDate"]);
                sql.Append(@" TRUNC(t.expected_due_date) BETWEEN " +
                (GlobalVariable.TO_DATE(Util.GetValueOfDateTime(StartDate), true)));
                sql.Append(@"AND " +
                (GlobalVariable.TO_DATE(Util.GetValueOfDateTime(EndDate), true)));
            }
            //Next Quarter
            else if (ListValue.Equals("04"))
            {
                int NextQuarter = 0;
                int MaxQuarter = GetMaxQuarter(calendar_ID, CurrentYear);
                //If max quarter and current quarter is same the the current year is going on else next year
                if (currQuarter != MaxQuarter)
                {
                    NextQuarter = currQuarter + 1;
                }
                else
                {
                    isNextYearQuarter = true;
                    NextQuarter = GetNextYearQuarter(calendar_ID, CurrentYear + 1);
                }
                if (NextQuarter > 0)
                {
                    CurrentYear = isNextYearQuarter == false ? CurrentYear : CurrentYear + 1;
                    string quarterSql = $@"SELECT Min(p.StartDate) AS StartDate,MAX(p.EndDate) AS EndDate FROM C_Period p INNER JOIN C_Year y ON (p.C_Year_ID = y.C_Year_ID)
                                           WHERE CEIL(p.PeriodNo/3)={NextQuarter} 
                                           AND y.CalendarYears={GlobalVariable.TO_STRING(CurrentYear.ToString())} 
                                           AND y.C_Calendar_ID={calendar_ID}";
                    dsYear = DB.ExecuteDataset(quarterSql);
                    string StartDate = Util.GetValueOfString(dsYear.Tables[0].Rows[0]["StartDate"]);
                    string EndDate = Util.GetValueOfString(dsYear.Tables[0].Rows[0]["EndDate"]);
                    sql.Append(@" TRUNC(t.expected_due_date) BETWEEN " +
                    (GlobalVariable.TO_DATE(Util.GetValueOfDateTime(StartDate), true)));
                    sql.Append(@"AND " +
                    (GlobalVariable.TO_DATE(Util.GetValueOfDateTime(EndDate), true)));
                }

            }
            //Next 6 months
            else if (ListValue == "05")
            {
                // Next 6 months data (current month + Next 5 months)
                if (DB.IsPostgreSQL())
                {
                    sql.Append("  date_trunc('MONTH', t.expected_due_date) >= DATE_TRUNC('MONTH', CURRENT_DATE)");
                    sql.Append(" AND date_trunc('MONTH', t.expected_due_date) <= DATE_TRUNC('MONTH', CURRENT_DATE) + INTERVAL '5 MONTHS'");
                }
                else
                {

                    sql.Append(" TRUNC(t.expected_due_date) >= TRUNC(Current_Date, 'MM')");
                    sql.Append(" AND TRUNC(t.expected_due_date) <= TRUNC(ADD_MONTHS(TRUNC(Current_Date), 5), 'MM')");
                }
            }
            DataSet ds = DB.ExecuteDataset(sql.ToString(), null, null);
            if (ds != null && ds.Tables[0].Rows.Count > 0)
            {
                obj = new CashFlowClass();
                obj.CashOutAmt = Util.GetValueOfDecimal(ds.Tables[0].Rows[0]["CashOutAmt"]);
                obj.CashInAmt = Util.GetValueOfDecimal(ds.Tables[0].Rows[0]["CashInAmt"]);
                obj.stdPrecision = precision;
                invGrandTotalData.Add(obj);
            }
            return invGrandTotalData;
        }
        /// <summary>
        /// This function is used to get Calender Year Data
        /// </summary>
        /// <param name="calenderYear">calenderYear</param>
        /// <param name="C_Calender_ID">C_Calender_ID</param>
        /// <param name="ctx">Context</param>
        /// <returns>DataSet</returns>
        /// <author>VIS_427</author>
        public DataSet GetYearData(Ctx ctx, int calenderYear, int C_Calender_ID)
        {
            string sql = $@"SELECT Min(p.StartDate) AS StartDate,MAX(p.EndDate) AS EndDate FROM C_Period p
                            INNER JOIN C_Year cy ON (cy.C_Year_ID = p.C_Year_ID) WHERE cy.AD_Client_ID = {ctx.GetAD_Client_ID()} AND cy.CalendarYears={calenderYear} AND cy.C_Calendar_ID={C_Calender_ID}";

            DataSet dsYear = DB.ExecuteDataset(sql, null, null);
            return dsYear;
        }
        /// <summary>
        /// This function is used to Maximum Quarter in year
        /// </summary>
        /// <param name="calenderYear">calenderYear</param>
        /// <param name="C_Calender_ID">C_Calender_ID</param>
        /// <returns>Maximum Quarter</returns>
        /// <author>VIS_427</author>
        public int GetMaxQuarter(int calendar_ID, int CalenderYear)
        {
            string sql = $@"SELECT CEIL(MAX(p.PeriodNo)/3) FROM C_Period p INNER JOIN C_Year y ON (p.C_Year_ID = y.c_year_ID)
                            WHERE y.CalendarYears={GlobalVariable.TO_STRING(CalenderYear.ToString())} AND y.C_Calendar_ID={calendar_ID}";
            int Quarter = Util.GetValueOfInt(DB.ExecuteScalar(sql, null, null));
            return Quarter;
        }
        /// <summary>
        /// This function is used to Next Year minimum Quarter
        /// </summary>
        /// <param name="calenderYear">calenderYear</param>
        /// <param name="C_Calender_ID">C_Calender_ID</param>
        /// <returns>Quarter</returns>
        /// <author>VIS_427</author>
        public int GetNextYearQuarter(int calendar_ID, int CalenderYear)
        {
            string sql = $@"SELECT CEIL(MIN(p.PeriodNo)/3) FROM C_Period p INNER JOIN C_Year y ON (p.C_Year_ID = y.c_year_ID)
                            WHERE y.CalendarYears={GlobalVariable.TO_STRING(CalenderYear.ToString())} AND y.C_Calendar_ID={calendar_ID}";
            int Quarter = Util.GetValueOfInt(DB.ExecuteScalar(sql, null, null));
            return Quarter;
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
        public string CurSymbol { get; set; }
        public decimal TotalLines { get; set; }
        public decimal GrandTotal { get; set; }

    }
    public class PurchaseOrderTabPanel
    {
        public string TaxName { get; set; }
        public string DocumentNo { get; set; }

        public decimal TaxPaybleAmt { get; set; }

        public decimal TaxAmt { get; set; }

        public string IsTaxIncluded { get; set; }

        public int stdPrecision { get; set; }
    }

    public class LineHistoryTabPanel
    {
        public int LineNo { get; set; }

        public DateTime? DateOrdered { get; set; }

        public DateTime? DatePromised { get; set; }

        public string Product { get; set; }

        public string Charge { get; set; }

        public decimal Quantity { get; set; }

        public string UOM { get; set; }

        public decimal QuantityOrdered { get; set; }

        public decimal Price { get; set; }

        public decimal ListPrice { get; set; }

        public decimal UnitPrice { get; set; }

        public string Tax { get; set; }

        public decimal Discount { get; set; }

        public decimal LineAmount { get; set; }

        public string Description { get; set; }

        public int stdPrecision { get; set; }

    }
    public class UnAllocatedPayTabPanel
    {
        public DateTime? DateTrx { get; set; }
        public DateTime? DateAcct { get; set; }
        public string DocumentNo { get; set; }
        public decimal PayAmt { get; set; }
        public int C_Payment_ID { get; set; }
        public int AD_Org_ID { get; set; }
        public int StdPrecision { get; set; }
        public string CurrencyName { get; set; }
        public int AD_Window_ID { get; set; }
        public int C_ConversionType_ID { get; set; }
    }
    public class ARInvWidgData
    {
        public List<ArTotalAmtWidget> arTotalAmtWidget { get; set; }
        public decimal daysAmt { get; set; }
        public string Symbol { get; set; }
        public int stdPrecision { get; set; }

    }

    public class ArTotalAmtWidget
    {
        public decimal totalAmt { get; set; }
    }

    public class InvGrandTotalData
    {
        public decimal GrandTotalAmt { get; set; }
        public string Symbol { get; set; }
        public int stdPrecision { get; set; }
        public string ImageUrl { get; set; }
        public string Name { get; set; }
        public DateTime SinceDate { get; set; }

    }
    public class PurchaseStateDetail
    {
        public decimal TotalAmt { get; set; }
        public string Symbol { get; set; }
        public int stdPrecision { get; set; }
    }
    public class ExpectedInvoice
    {
        public decimal TotalAmt { get; set; }
        public string Symbol { get; set; }
        public string RecordType { get; set; }
        public int stdPrecision { get; set; }
        public string ImageUrl { get; set; }
        public string Name { get; set; }
        public DateTime OrderdDate { get; set; }
        public DateTime DatePromised { get; set; }
        public string DocumentNo { get; set; }
        public int recordCount { get; set; }

    }
    public class TopExpenseAmountData
    {
        public decimal ExpenseAmount { get; set; }
        public string ExpenseName { get; set; }
        public int stdPrecision { get; set; }
    }

    public class VAS_ExpenseRevenue
    {
        public decimal[] lstExpData { get; set; }
        public decimal[] lstRevData { get; set; }
        public decimal[] lstProfitData { get; set; }
        public string[] lstLabel { get; set; }
        public string ErrorMessage { get; set; }
        public int Precision { get; set; }
    }
    public class VAS_ScheduleDetail
    {
        public decimal DueAmt { get; set; }
        public string IsPaid { get; set; }
        public DateTime DueDate { get; set; }
        public DateTime? DateAcct { get; set; }
        public string DocumentNo { get; set; }
        public string AccountNo { get; set; }
        public string CheckNo { get; set; }
        public DateTime? CheckDate { get; set; }
        public string PayMethod { get; set; }
        public int LineNum { get; set; }
        public int stdPrecision { get; set; }
        public int C_InvoicePaySchedule_ID { get; set; }
        public int RecordCount { get; set; }
    }
    public class ExpectedPayment
    {
        public int Record_ID { get; set; }
        public int Window_ID { get; set; }
        public string Primary_ID { get; set; }
        public decimal TotalAmt { get; set; }
        public string Symbol { get; set; }
        public string RecordType { get; set; }
        public int stdPrecision { get; set; }
        public string ImageUrl { get; set; }
        public string Name { get; set; }
        public DateTime OrderdDate { get; set; }
        public DateTime DatePromised { get; set; }
        public string DocumentNo { get; set; }
        public int recordCount { get; set; }
        public string ISO_Code { get; set; }
        public string PayMethod { get; set; }
        public string windowType { get; set; }

    }

    public class VAS_InvoiceMatchingDetail
    {
        public int Line { get; set; }
        public string ProductName { get; set; }
        public string UomName { get; set; }
        public decimal QtyEntered { get; set; }
        public decimal QtyInvoiced { get; set; }
        public int C_OrderLine_ID { get; set; }
        public int M_InOutLine_ID { get; set; }
        public decimal QtyOrdered { get; set; }
        public decimal OrderDelivered { get; set; }
        public decimal OrderInvoiced { get; set; }
        public int C_Invoiceline_ID { get; set; }
        public int UOMPrecision { get; set; }
        public int CurrencyPrecision { get; set; }
        public decimal InvoicePrice { get; set; }
        public decimal OrderPrice { get; set; }
        public decimal ExpectedOrder { get; set; }
        public decimal ExpectedGRN { get; set; }
        public string DocStatus { get; set; }
        public decimal AdvanceAmt { get; set; }
        public decimal TotalAdvanceAmt { get; set; }
        public bool IsDiscrepancy { get; set; }
        public int DiscrepancyCount { get; set; }
        public int RecordCount { get; set; }
    }
    public class CashFlowClass
    {
        public decimal CashOutAmt { get; set; }
        public decimal CashInAmt { get; set; }
        public int stdPrecision { get; set; }
    }
}
