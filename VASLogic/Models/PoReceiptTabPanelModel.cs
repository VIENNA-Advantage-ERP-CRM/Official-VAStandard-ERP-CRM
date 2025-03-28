﻿/*******************************************************
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
using VAdvantage.ProcessEngine;
using ViennaAdvantage.Process;

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
            string docBaseTypeAR_AP= ISOtrx ? "('ARI','ARC')" : "('API','APC')";

            sql.Append($@"WITH InvoiceData AS (
                         {MRole.GetDefault(ctx).AddAccessSQL($@"SELECT ci.AD_Client_ID,
                             cs.C_InvoicePaySchedule_ID,
                             cd.DocBaseType,
                             cs.DueDate AS DateInvoiced,
                             currencyConvert(cs.DueAmt,cs.C_Currency_ID ," + C_Currency_ID + @",ci.DateAcct,ci.C_ConversionType_ID,cs.AD_Client_ID,cs.AD_Org_ID) AS DueAmt
                             FROM C_Invoice ci
                             INNER JOIN C_InvoicePaySchedule cs ON (cs.C_Invoice_ID = ci.C_Invoice_ID)
                             INNER JOIN C_DocType cd ON (cd.C_DocType_ID = ci.C_DocTypeTarget_ID)
                             WHERE cd.DocBaseType IN " + docBaseTypeAR_AP + @" AND ci.DocStatus IN ('CO','CL') AND cs.VA009_IsPaid='N' AND cd.IsExpenseInvoice = 'N'", "ci", MRole.SQL_FULLYQUALIFIED, MRole.SQL_RW)})
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
                         DateInvoiced <= TRUNC(CURRENT_DATE) AND DateInvoiced >= TRUNC(CURRENT_DATE) - 30
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
                         DateInvoiced <= TRUNC(CURRENT_DATE) - 31 AND DateInvoiced >= TRUNC(CURRENT_DATE) - 60
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
                        DateInvoiced <= TRUNC(CURRENT_DATE) - 61 AND  DateInvoiced >= TRUNC(CURRENT_DATE) - 90
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
                        DateInvoiced <= TRUNC(CURRENT_DATE) - 91 AND  DateInvoiced >= TRUNC(CURRENT_DATE) - 120
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
                     WHERE DateInvoiced <= TRUNC(CURRENT_DATE) - 120");
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
            string docBaseTypeAR_AP = ISOtrx ? "('ARI','ARC')" : "('API','APC')";
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
                             currencyConvert(ci.grandtotalafterwithholding,ci.C_Currency_ID ," + C_Currency_ID + @",ci.DateAcct ,ci.C_ConversionType_ID ,ci.AD_Client_ID ,ci.AD_Org_ID) AS DueAmt
                             FROM
                             C_Invoice ci
                             INNER JOIN C_BPartner cb ON (cb.C_BPartner_ID=ci.C_BPartner_ID)
                             INNER JOIN C_DocType cd ON (cd.C_DocType_ID=ci.C_DocTypeTarget_ID)
                             LEFT OUTER JOIN AD_Image custimg ON (custimg.AD_Image_ID = CAST(cb.Pic AS INT))
                             WHERE cd.DocBaseType IN "+ docBaseTypeAR_AP + @" AND ci.DocStatus IN ('CO','CL') AND " + BPCheck, "ci", MRole.SQL_FULLYQUALIFIED, MRole.SQL_RW
                     )})");
            sql.Append(@",PeriodDetail AS (SELECT c_period.AD_Client_ID,Min(c_period.StartDate) AS StartDate,Max(c_period.EndDate) AS EndDate  FROM C_Year INNER JOIN C_Period on (C_Year.C_Year_ID=c_period.C_Year_ID) WHERE ");
            //Getting data according to Current month
            if (ListValue == "01")
            {
                sql.Append(@" c_year.C_Calendar_ID =" + calendar_ID +
                            @" AND c_year.IsActive = 'Y' AND C_period.IsActive='Y'
                            AND CURRENT_DATE BETWEEN C_period.StartDate AND C_period.EndDate");
            }
            //Getting data according to Current Year
            else if (ListValue == "02")
            {
                sql.Append(@" c_year.C_Calendar_ID =" + calendar_ID +
                            @" AND c_year.IsActive = 'Y' AND C_period.IsActive='Y'
                            AND C_Year.CALENDARYEARS='" + CurrentYear + "'");
            }
            //Getting data according to Last Year
            else if (ListValue == "03")
            {
                CurrentYear = CurrentYear - 1;
                sql.Append(@" c_year.C_Calendar_ID =" + calendar_ID +
                            @" AND c_year.IsActive = 'Y' AND C_period.IsActive='Y'
                            AND C_Year.CALENDARYEARS='" + CurrentYear + "'");
            }
            //Getting data according to Last 3 Year
            else if (ListValue == "04")
            {
                StartYear = CurrentYear - 3;
                CurrentYear = CurrentYear - 1;
                sql.Append(@" c_year.C_Calendar_ID =" + calendar_ID +
                            @" AND c_year.IsActive = 'Y' AND C_period.IsActive='Y'
                            AND C_Year.CALENDARYEARS BETWEEN '" + StartYear + "' AND '" + CurrentYear + "'");
            }
            //Getting data according to Last 5 Year
            else if (ListValue == "05")
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
            sql.Append(MRole.GetDefault(ctx).AddAccessSQL($@"SELECT 'DueSoon' AS Type,
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
            sql.Append(MRole.GetDefault(ctx).AddAccessSQL($@"SELECT 'Disputed' AS Type,
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
                sql.Append(MRole.GetDefault(ctx).AddAccessSQL($@"SELECT 'UnAllocated' AS Type,
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
                sql.Append(MRole.GetDefault(ctx).AddAccessSQL($@"SELECT 'Hold' AS Type,
                 nvl(SUM(CASE WHEN ci.isholdpayment = 'Y' THEN CASE WHEN cd.docbasetype = " + docBaseTypeARI_APT + @" THEN
                 currencyconvert(cs.dueamt, ci.c_currency_id," + C_Currency_ID + @", ci.dateacct, ci.c_conversiontype_id, ci.ad_client_id, ci.ad_org_id)
                 ELSE 0 END ELSE CASE WHEN cd.docbasetype = " + docBaseTypeARI_APT + @"
                 AND cs.isholdpayment = 'Y' THEN currencyconvert( cs.dueamt, ci.c_currency_id," + C_Currency_ID + @", 
                 ci.dateacct, ci.c_conversiontype_id, ci.ad_client_id, ci.ad_org_id) ELSE 0 END END) - SUM(CASE
                 WHEN ci.isholdpayment = 'Y' THEN CASE WHEN cd.docbasetype =" + docBaseTypeARC_APC + @"THEN
                 currencyconvert(cs.dueamt, ci.c_currency_id," + C_Currency_ID + @", ci.dateacct, ci.c_conversiontype_id, ci.ad_client_id, ci.ad_org_id)
                 ELSE 0 END ELSE
                 CASE WHEN cd.docbasetype = " + docBaseTypeARC_APC + @"
                 AND cs.isholdpayment = 'Y' THEN
                 currencyconvert(cs.dueamt, ci.c_currency_id," + C_Currency_ID + @", ci.dateacct, ci.c_conversiontype_id, ci.ad_client_id, ci.ad_org_id)
                 ELSE 0 END END ), 0) AS sumamount
                 FROM c_invoice ci
                 INNER JOIN c_invoicepayschedule cs ON (cs.c_invoice_id = ci.c_invoice_id)
                 INNER JOIN c_doctype cd ON (cd.c_doctype_id = ci.c_doctypetarget_id)
                 WHERE ci.docstatus IN ('CO','CL') AND cs.va009_ispaid = 'N'", "ci", MRole.SQL_FULLYQUALIFIED, MRole.SQL_RW));
            }

            // Query for 'InProgress'
            sql.Append(" UNION ALL ");
            sql.Append(MRole.GetDefault(ctx).AddAccessSQL($@"SELECT 'InProgress' AS Type,
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
            sql.Append(MRole.GetDefault(ctx).AddAccessSQL($@"SELECT 'New' AS Type,
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
             INNER JOIN PeriodDetail pd ON (pd.AD_Client_ID=ci.AD_Client_ID)
             WHERE ci.DocStatus IN ('CO', 'CL') AND ci.DateInvoiced BETWEEN pd.StartDate AND pd.EndDate
            ", "ci", MRole.SQL_FULLYQUALIFIED, MRole.SQL_RW));

            // Query for 'Drafted'
            sql.Append(" UNION ALL ");
            sql.Append(MRole.GetDefault(ctx).AddAccessSQL($@"SELECT 'Drafted' AS Type,
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
                    obj.Type= Util.GetValueOfString(ds.Tables[0].Rows[i]["Type"]);
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
        /// <param name="C_BPartner_ID">C_BPartner_ID</param>
        /// <param name="fromDate">fromDate</param>
        /// <param name="toDate">toDate</param>
        /// <author>VIS_427</author>
        /// <returns>List of data of Expected invoices against order and GRN</returns>
        public List<ExpectedInvoice> GetExpectedInvoiceData(Ctx ctx, bool ISOtrx, int pageNo, int pageSize, string ListValue, string C_BPartner_ID, string fromDate, string toDate)
        {
            ExpectedInvoice obj = new ExpectedInvoice();
            StringBuilder sql = new StringBuilder();
            StringBuilder sqlOrder = new StringBuilder();
            StringBuilder sqlGrn = new StringBuilder();
            StringBuilder sqlmain = new StringBuilder();
            List<ExpectedInvoice> invGrandTotalData = new List<ExpectedInvoice>();
            string OrderCheck = (ISOtrx == true ? " AND o.IsSOTrx='Y'" : " AND o.IsSOTrx='N'");
            string DeliveryCheck = (ISOtrx == true ? "min.IsSOTrx='Y'" : "min.IsSOTrx='N'");
            var C_Currency_ID = ctx.GetContextAsInt("$C_Currency_ID");
            string BPCheck = (ISOtrx == true ? " AND cb.IsCustomer='Y'" : " AND cb.IsVendor='Y'");
            string DocBaseTypeCheck = (ISOtrx == true ? " AND dt.DocBaseType IN ('SOO')" :" AND dt.DocBaseType IN ('POO')");
            sql.Append($@"SELECT * FROM  (");
            //AL=ALL ,PO=Purchase Order,SO=Sales Order

            if (ListValue == null || ListValue == "AL" || ListValue == "PO" || ListValue == "SO")
            {
                sqlmain.Append($@"SELECT 'Order' AS Type,Ord.Record_ID,Ord.Pic,");
                if (ISOtrx)
                {
                    sqlmain.Append(@"Ord.InvoiceRule,");
                }
                else
                {
                    sqlmain.Append(@"NULL AS InvoiceRule,");
                }
                sqlmain.Append(@"Ord.DocumentNo,
                             Ord.FilterDate AS FilterDate,
                             Ord.PromisedDate AS PromisedDate,
                             Ord.Name,
                             Ord.AD_Client_ID,
                             Ord.StdPrecision,
                             Ord.CurSymbol,
                             Ord.C_BPartner_ID,
                             Ord.AD_Org_ID,
                             'N' AS IsNotFullyDelivered,
                             SUM(TotalValue) AS TotalValue,
                             Ord.DateOrdered
                             FROM ( ");
                sqlOrder.Append($@"SELECT 'Order' AS Type,o.C_Order_ID AS Record_ID,cb.Pic,");
                if (ISOtrx)
                {
                    sqlOrder.Append(@"CASE WHEN o.IsSoTrx='Y' THEN rsf.Name ELSE NULL END AS InvoiceRule,");
                }
                else
                {
                    sqlOrder.Append(@"NULL AS InvoiceRule,");
                }
                sqlOrder.Append(@"o.DocumentNo,
                             o.DateOrdered AS FilterDate,
                             o.DatePromised AS PromisedDate,
                             cb.Name,
                             o.AD_Client_ID,
                             cy.StdPrecision,
                             cy.CurSymbol,
                             o.C_BPartner_ID,
                             l.C_OrderLine_ID,
                             o.AD_Org_ID,
                            'N' AS IsNotFullyDelivered,
                             ROUND(l.linetotalamt - SUM((case WHEN ci.c_orderline_id IS NOT NULL AND ci.M_InOutLine_id IS NOT null then (ci.QtyInvoiced) * (l.linetotalamt) /nullif(l.qtyordered, 0)
                             WHEN ci.c_orderline_id IS NOT NULL AND ci.M_InOutLine_id IS null AND ci.C_Charge_ID IS NULL then (ci.QtyInvoiced) * (l.linetotalamt)/nullif(l.qtyordered, 0)
                             WHEN ci.c_orderline_id IS NULL AND ci.M_InOutLine_id IS null and mil.c_orderline_id is not null  then (mil.movementqty) * (l.linetotalamt)/nullif(l.qtyordered, 0)
                             WHEN ci.c_orderline_id IS NOT NULL AND ci.M_InOutLine_id IS null AND ci.C_Charge_ID IS NOT NULL then (ci.linetotalamt) 
                             else 0 end)),cy.StdPrecision) AS TotalValue,
                             o.DateOrdered
                             FROM
                             C_Order o
                             INNER JOIN C_OrderLine l ON (o.c_order_id=l.c_order_id)
                             INNER JOIN C_DocType dt ON (dt.C_DocType_ID=o.C_DocTypeTarget_ID)
                             INNER JOIN C_BPartner cb ON (o.C_BPartner_ID=cb.C_BPartner_ID)
                             INNER JOIN C_Currency cy ON (cy.C_Currency_ID=o.C_Currency_ID)
                             INNER JOIN AD_Ref_List rsf ON (rsf.value=o.InvoiceRule)
                             INNER JOIN AD_Reference ar ON (ar.AD_Reference_ID=rsf.AD_Reference_ID)
                             LEFT JOIN C_InvoiceLine ci ON (ci.C_OrderLine_ID=l.C_OrderLine_ID AND ci.ReversalDoc_ID IS NULL)
                             LEFT JOIN M_Product cp ON (ci.M_Product_ID=cp.M_Product_ID)
                             LEFT JOIN M_InOutLine mil ON (mil.C_OrderLine_ID=l.C_OrderLine_ID)");
                sqlmain.Append(MRole.GetDefault(ctx).AddAccessSQL(sqlOrder.ToString(), "o", MRole.SQL_FULLYQUALIFIED, MRole.SQL_RW));
                sqlmain.Append(" AND o.DocStatus IN ('CO') AND NVL(dt.DocSubTypeSO,' ') NOT IN ('BO','ON', 'OB') AND ar.Name='C_Order InvoiceRule'" + OrderCheck + BPCheck + DocBaseTypeCheck + "");
                if (Util.GetValueOfInt(C_BPartner_ID) != 0)
                {
                    sqlmain.Append(" AND o.C_BPartner_ID=" + Util.GetValueOfInt(C_BPartner_ID));
                }
                /*if dates value are not null then implemeted fucntion to fetch sql*/
                if (!String.IsNullOrEmpty(fromDate) || !String.IsNullOrEmpty(toDate))
                {
                    sqlmain.Append(GetExpInvDateSql("TRUNC(o.DateOrdered)", fromDate, toDate));
                }
                sqlmain.Append(@" GROUP BY o.C_Order_ID,cb.Pic,o.IsSoTrx,rsf.Name,");
                sqlmain.Append(@"o.DocumentNo, o.DateOrdered,o.DateOrdered,o.DatePromised,cb.Name,o.AD_Client_ID,cy.StdPrecision,
                             cy.CurSymbol,o.C_BPartner_ID,l.C_OrderLine_ID,o.AD_Org_ID,l.linetotalamt");
                sqlmain.Append(" )Ord ");
                sqlmain.Append(@" GROUP BY Ord.Record_ID,Ord.Pic,Ord.InvoiceRule,
                             Ord.DocumentNo,
                             Ord.FilterDate,
                             Ord.PromisedDate,
                             Ord.Name,
                             Ord.AD_Client_ID,
                             Ord.StdPrecision,
                             Ord.CurSymbol,
                             Ord.C_BPartner_ID,
                             Ord.AD_Org_ID,
                             Ord.DateOrdered");
            }
            if (ListValue == null || ListValue == "AL")
            {
                sqlmain.Append(" UNION ALL ");
            }
            //AL=ALL ,GR=GRN,DO=Delivery Order
            if (ListValue == null || ListValue == "AL" || ListValue == "GR" || ListValue == "DO")
            {
                sqlGrn.Append($@"SELECT 'GRN' AS Type,min.M_InOut_ID AS Record_ID,cb.Pic,");
                if (ISOtrx)
                {
                    sqlGrn.Append(@"CASE WHEN o.IsSoTrx='Y' THEN invrule.Name ELSE NULL END AS InvoiceRule,");
                }
                else
                {
                    sqlGrn.Append(@"NULL AS InvoiceRule,");
                }
                sqlGrn.Append(@"min.DocumentNo,
                             CASE WHEN l.C_OrderLine_ID IS NOT NULL THEN o.DateOrdered
                             ELSE min.MovementDate
                             END AS FilterDate,
                             CASE WHEN l.C_OrderLine_ID IS NOT NULL THEN o.DatePromised
                             ELSE min.MovementDate
                             END AS PromisedDate,
                             cb.Name,
                             min.AD_Client_ID,
                             cy.StdPrecision,
                             cy.CurSymbol,
                             min.C_BPartner_ID,
                             min.AD_Org_ID,
                             CASE 
                             WHEN o.InvoiceRule = 'O'AND EXISTS (SELECT 1
                             FROM C_OrderLine oline WHERE oline.C_Order_ID = o.C_Order_ID
                             AND oline.QtyOrdered <> oline.QtyDelivered) THEN 'Y'
                             ELSE 'N' END AS IsNotFullyDelivered,
                             SUM(CASE WHEN l.C_OrderLine_ID IS NOT NULL THEN 
                             ROUND((COALESCE(l.movementqty, 0) - COALESCE(ci.qtyinvoiced, 0))*(ol.LineTotalAmt)/NULLIF(ol.qtyordered, 0),cy.StdPrecision)
                             ELSE (COALESCE(l.movementqty, 0) - COALESCE(ci.qtyinvoiced, 0))*l.CurrentCostPrice END) AS TotalValue,
                             min.MovementDate AS DateOrdered
                             FROM M_InOut min
                             INNER JOIN M_InOutLine l ON (l.M_InOut_ID=min.M_InOut_ID)
                             INNER JOIN C_BPartner cb ON (min.C_BPartner_ID=cb.C_BPartner_ID)
                             LEFT JOIN C_InvoiceLine ci ON (ci.m_inoutline_ID=l.m_inoutline_ID AND ci.ReversalDoc_ID IS NULL)
                             LEFT JOIN C_OrderLine ol ON (ol.C_OrderLine_ID=l.C_OrderLine_ID)
                             LEFT JOIN C_Order o ON (o.C_Order_ID=ol.C_Order_ID)
                             LEFT JOIN (SELECT rsf.NAME,rsf.VALUE FROM ad_ref_list rsf 
                             INNER JOIN ad_reference ar ON (ar.ad_reference_id=rsf.ad_reference_id AND ar.name='C_Order InvoiceRule')
                             WHERE rsf.IsActive='Y') invrule on (o.invoicerule=invrule.value)
                             LEFT JOIN C_Currency cy ON (cy.C_Currency_ID=o.C_Currency_ID)");
                sqlmain.Append(MRole.GetDefault(ctx).AddAccessSQL(sqlGrn.ToString(), "min", MRole.SQL_FULLYQUALIFIED, MRole.SQL_RW));
                sqlmain.Append(@" AND NOT EXISTS (SELECT 1 FROM c_orderline ol2
                             WHERE ol2.C_OrderLine_ID = ol.C_OrderLine_ID
                             AND COALESCE(ol2.qtyordered, 0) = COALESCE(ol2.qtyinvoiced, 0))
                             AND min.DocStatus IN ('CO','CL')
                             AND " + DeliveryCheck + BPCheck + "");
                sqlmain.Append(@"GROUP BY min.M_InOut_ID,cb.Pic,o.IsSoTrx,invrule.Name, o.InvoiceRule, min.DocumentNo, min.MovementDate,
                             CASE WHEN l.C_OrderLine_ID IS NOT NULL THEN o.DateOrdered
                             ELSE min.MovementDate
                             END,CASE WHEN l.C_OrderLine_ID IS NOT NULL THEN o.DatePromised
                             ELSE min.MovementDate END,
                             CASE WHEN o.InvoiceRule = 'O' AND EXISTS (SELECT 1 FROM C_OrderLine oline WHERE oline.C_Order_ID = o.C_Order_ID
                             AND oline.QtyOrdered <> oline.QtyDelivered) THEN 'Y' ELSE 'N' END,
                             cb.Name,min.AD_Client_ID,cy.StdPrecision,
                             cy.CurSymbol,min.C_BPartner_ID,min.AD_Org_ID
                             HAVING SUM(coalesce(l.movementqty, 0)-coalesce(ci.qtyinvoiced, 0))>0");

                if (Util.GetValueOfInt(C_BPartner_ID) != 0)
                {
                    sqlmain.Append(" AND min.C_BPartner_ID=" + Util.GetValueOfInt(C_BPartner_ID));
                }
                /*if dates value are not null then implemeted fucntion to fetch sql*/
                if (!String.IsNullOrEmpty(fromDate) || !String.IsNullOrEmpty(toDate))
                {
                    sqlmain.Append(GetExpInvDateSql("TRUNC(min.MovementDate)", fromDate, toDate));
                }
            }
            sql.Append(sqlmain);
            sql.Append(")T WHERE T.TotalValue > 0 ORDER BY T.FilterDate ASC,T.C_BPartner_ID ASC,T.DocumentNo ASC");
            DataSet ds = DB.ExecuteDataset(sql.ToString(), null, null, pageSize, pageNo);
            if (ds != null && ds.Tables[0].Rows.Count > 0)
            {
                int GRNId = 0;
                int OrderWinId = 0;
                int InvWindowId = 0;
                //fetching the record count to use it for pagination
                int RecordCount = Util.GetValueOfInt(DB.ExecuteScalar("SELECT COUNT(*) FROM (" + sqlmain.ToString() + ")t WHERE t.TotalValue > 0 ", null, null));
                sql.Clear();
                //this query is returning the field of base currency
                sql.Append(@"SELECT CASE WHEN Cursymbol IS NOT NULL THEN Cursymbol ELSE ISO_Code END AS Symbol,StdPrecision FROM C_Currency WHERE C_Currency_ID=" + C_Currency_ID);
                DataSet dsCurrency = DB.ExecuteDataset(sql.ToString(), null, null);
                //Getting window id for zoom 
                if (ISOtrx)
                {
                    //first parameter is new screen and second parameter is old screen
                    GRNId=GetWindowId("VAS_DeliveryOrder", "Shipment (Customer)");
                    OrderWinId= GetWindowId("VAS_SalesOrder", "Sales Order");
                    InvWindowId = GetWindowId("VAS_ARInvoice", "Invoice (Customer)");
                }
                else
                {
                    GRNId = GetWindowId("VAS_MaterialReceipt", "Material Receipt");
                    OrderWinId = GetWindowId("VAS_PurchaseOrder", "Purchase Order");
                    InvWindowId = GetWindowId("VAS_APInvoice", "Invoice (Vendor)");
                }
                for (int i = 0; i < ds.Tables[0].Rows.Count; i++)
                {
                        obj = new ExpectedInvoice();
                        obj.recordCount = RecordCount;
                        obj.TotalAmt = Util.GetValueOfDecimal(ds.Tables[0].Rows[i]["TotalValue"]);
                        //If currency symbol not found the pick base currency symbol and precision
                        if (!String.IsNullOrEmpty(Util.GetValueOfString(ds.Tables[0].Rows[i]["CurSymbol"])))
                        {
                            obj.Symbol = Util.GetValueOfString(ds.Tables[0].Rows[i]["CurSymbol"]);
                        }
                        else
                        {
                            obj.Symbol = Util.GetValueOfString(dsCurrency.Tables[0].Rows[0]["Symbol"]);
                        }
                        obj.DocumentNo = Util.GetValueOfString(ds.Tables[0].Rows[i]["DocumentNo"]);
                        obj.Record_ID = Util.GetValueOfInt(ds.Tables[0].Rows[i]["Record_ID"]);
                        obj.RecordType = Util.GetValueOfString(ds.Tables[0].Rows[i]["Type"]);
                        obj.IsFullyDelivered = Util.GetValueOfString(ds.Tables[0].Rows[i]["IsNotFullyDelivered"]);

                        obj.stdPrecision = (Util.GetValueOfInt(ds.Tables[0].Rows[i]["StdPrecision"]) != 0 ? Util.GetValueOfInt(ds.Tables[0].Rows[i]["StdPrecision"])
                            : Util.GetValueOfInt(dsCurrency.Tables[0].Rows[0]["StdPrecision"]));
                        obj.OrderdDate = Util.GetValueOfDateTime(ds.Tables[0].Rows[i]["DateOrdered"]).Value;
                        obj.DatePromised = Util.GetValueOfDateTime(ds.Tables[0].Rows[i]["PromisedDate"]).Value;
                        obj.Name = Util.GetValueOfString(ds.Tables[0].Rows[i]["Name"]);
                        obj.InvoiceRule = Util.GetValueOfString(ds.Tables[0].Rows[i]["InvoiceRule"]);

                        if (Util.GetValueOfString(ds.Tables[0].Rows[i]["Type"]) == "Order")
                        {
                            obj.Window_ID = OrderWinId;
                            obj.Primary_ID = "C_Order_ID";
                        }
                        else
                        {
                            obj.Window_ID = GRNId;
                            obj.Primary_ID = "M_InOut_ID";
                            obj.AD_Org_ID = Util.GetValueOfInt(ds.Tables[0].Rows[i]["AD_Org_ID"]);
                        }
                        obj.InvWinID = InvWindowId;
                        //if (Util.GetValueOfInt(ds.Tables[0].Rows[i]["Pic"]) != 0)
                        //{
                        //    obj.ImageUrl = "Images/Thumb46x46/" + Util.GetValueOfInt(ds.Tables[0].Rows[i]["Pic"]) + Util.GetValueOfString(ds.Tables[0].Rows[i]["ImageExtension"]);

                        //}
                        invGrandTotalData.Add(obj);
                    }
            }
            return invGrandTotalData;
        }
        /// <summary>
        /// This Method is used to return sql
        /// </summary>
        /// <param name="FlterCol">FlterCol</param>
        /// <param name="fromDate">fromDate</param>
        /// <param name="toDate">toDate</param>
        /// <returns>Returns Sql</returns>
        /// <author>VIS_427 </author>
        public string GetExpInvDateSql(string FlterCol, string fromDate, string toDate)
        {
            StringBuilder sql = new StringBuilder();
            if (!String.IsNullOrEmpty(fromDate) && String.IsNullOrEmpty(toDate) && Util.GetValueOfDateTime(fromDate) < DateTime.Now)
            {
                sql.Append(@" AND " + FlterCol + " BETWEEN " +
                (GlobalVariable.TO_DATE(Util.GetValueOfDateTime(fromDate), true)));
                sql.Append(@"AND Current_Date");
            }
            //if user enter from date and to date then this condition will execute
            else if (!String.IsNullOrEmpty(fromDate) && !String.IsNullOrEmpty(toDate))
            {
                sql.Append(@" AND " + FlterCol + " BETWEEN " +
                (GlobalVariable.TO_DATE(Util.GetValueOfDateTime(fromDate), true)));
                sql.Append(@"AND " +
                (GlobalVariable.TO_DATE(Util.GetValueOfDateTime(toDate), true)));
            }
            //if user enter does not enter from date but enters todate then this condition will execute
            else if (String.IsNullOrEmpty(fromDate) && !String.IsNullOrEmpty(toDate))
            {
                sql.Append(@" AND " + FlterCol + " <= " +
                (GlobalVariable.TO_DATE(Util.GetValueOfDateTime(toDate), true)));
            }
            //if from date greater then today's date
            else if (Util.GetValueOfDateTime(fromDate) > DateTime.Now)
            {
                toDate = fromDate;
                sql.Append(@" AND " + FlterCol + " BETWEEN " +
                (GlobalVariable.TO_DATE(Util.GetValueOfDateTime(fromDate), true)));
                sql.Append(@"AND " +
                (GlobalVariable.TO_DATE(Util.GetValueOfDateTime(toDate), true)));
            }
            return sql.ToString();
        }
        /// <summary>
        /// This Method is used to return Window ID for zoom functionality
        /// </summary>
        /// <param name="NewScreen">New Screen</param>
        /// <param name="OldScreen">Old Screen</param>
        /// <returns>Returns AD_Window_ID</returns>
        /// <author>VIS_427</author>
        public int GetWindowId(string NewScreen,string OldScreen)
        {
            int AD_Window_ID = 0;
            AD_Window_ID= Util.GetValueOfInt(DB.ExecuteScalar($@"SELECT AD_Window_ID FROM AD_Window WHERE Name={ GlobalVariable.TO_STRING(NewScreen)}", null, null));
            if (AD_Window_ID == 0)
            {
                AD_Window_ID= Util.GetValueOfInt(DB.ExecuteScalar($@"SELECT AD_Window_ID FROM AD_Window WHERE Name={ GlobalVariable.TO_STRING(OldScreen)}", null, null));
            }
            return AD_Window_ID;
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
                         else ci.C_Calendar_ID END AS C_Calendar_ID,
                         cp.PeriodNo,
                         CEIL(CAST(cp.PeriodNo AS NUMERIC)/3) AS Quarter
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
                currentQuarter = Util.GetValueOfInt(yearDs.Tables[0].Rows[0]["Quarter"]);
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
                         SUM(fa.AmtAcctDr - fa.AmtAcctCR) AS ExpenseAmount,
                         cy.StdPrecision
                         FROM fact_acct fa
                         INNER JOIN C_AcctSchema acct ON (fa.C_AcctSchema_ID = acct.C_AcctSchema_ID)
                         INNER JOIN C_AcctSchema_Element acctEle ON (acctEle.C_AcctSchema_ID = acct.C_AcctSchema_ID AND ElementType = 'AC')
                         INNER JOIN C_Element ele ON (ele.C_Element_ID = acctEle.C_Element_ID)
                         INNER JOIN C_ElementValue eleVal ON (eleVal.C_Element_ID = ele.C_Element_ID AND AccountType = 'E' AND fa.Account_ID = eleVal.C_ElementValue_ID)
                         INNER JOIN C_Currency cy ON (fa.C_Currency_ID = cy.C_Currency_ID)
                         INNER JOIN AD_ClientInfo ci ON (ci.AD_Client_ID = fa.AD_Client_ID AND fa.C_AcctSchema_ID = ci.C_AcctSchema1_ID)
                         WHERE acctEle.IsActive = 'Y' AND eleVal.IsActive = 'Y' AND fa.PostingType='A'", "fa", MRole.SQL_FULLYQUALIFIED, MRole.SQL_RW
                     )}");
            sql.Append(@" GROUP BY 
                         acct.C_AcctSchema_ID,
                         fa.AD_Org_ID, 
                         fa.AD_Client_ID,
                         ele.C_Element_ID,
                         eleVal.Value || '_' || eleVal.NAME,
                         fa.DateAcct,
                         cy.StdPrecision)");
            //If the user selected this quarter the add the with clauses of current quarter
            if (ListValue == "3")
            {
                sql.Append($@",curentPeriod AS
                               (SELECT PeriodNo, CEIL(CAST(p.PeriodNo AS NUMERIC)/3) AS Quarter from C_Period p INNER JOIN C_Year y ON(p.C_Year_ID = y.c_year_ID)
                               WHERE y.CalendarYears ={ VAdvantage.DataBase.GlobalVariable.TO_STRING(CurrentYear.ToString())}
                AND y.C_Calendar_ID ={ calendar_ID}
                AND TRUNC(CURRENT_DATE) between p.StartDate and p.EndDate ),
                               PeriodQuater AS
                               (SELECT PeriodNo, CEIL(CAST(p.PeriodNo AS NUMERIC)/3) AS Quarter from C_Period p INNER JOIN C_Year y ON(p.C_Year_ID = y.c_year_ID)
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
                                              (SELECT CEIL(CAST(MAX(p.PeriodNo) AS NUMERIC)/3) AS Quarter from C_Period p INNER JOIN C_Year y ON (p.C_Year_ID = y.c_year_ID)
                                              WHERE y.CalendarYears={VAdvantage.DataBase.GlobalVariable.TO_STRING(CurrentYear.ToString())} AND y.C_Calendar_ID={calendar_ID})
                                              ,PreviousPeriodQuater AS
                                              (SELECT PeriodNo, CEIL(PeriodNo/3) AS Quarter from C_Period p INNER JOIN C_Year y ON (p.C_Year_ID = y.c_year_ID)
                                              WHERE y.CalendarYears={VAdvantage.DataBase.GlobalVariable.TO_STRING(CurrentYear.ToString())} AND y.C_Calendar_ID={calendar_ID})");
                }
                else
                {
                    sql.Append($@",curentPeriod AS 
                                                    (SELECT PeriodNo, CEIL(CAST(p.PeriodNo AS NUMERIC)/3) AS Quarter from C_Period p INNER JOIN C_Year y ON (p.C_Year_ID = y.c_year_ID)
                                                    WHERE y.CalendarYears={VAdvantage.DataBase.GlobalVariable.TO_STRING(CurrentYear.ToString())} AND y.C_Calendar_ID={calendar_ID}
                                                    AND TRUNC(CURRENT_DATE) between p.startdate and p.enddate ),
                                                    PeriodQuater AS
                                                    (SELECT PeriodNo, CEIL(CAST(p.PeriodNo AS NUMERIC)/3) AS Quarter from C_Period p INNER JOIN C_Year y ON (p.C_Year_ID = y.c_year_ID)
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
                int C_Period_ID = GetPreviousPeriod(CurrentYear, ctx.GetAD_Client_ID(), calendar_ID);
                if (C_Period_ID > 0)
                {
                    sql.Append(GetYearSql("Min(c_period.StartDate)", "Max(c_period.EndDate)", " c_period.C_Period_ID=" + C_Period_ID));
                }
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
                         fa.ExpenseName,
                         fa.StdPrecision
                         FROM
                         FactData fa 
                         INNER JOIN PeriodDetail pd ON (pd.AD_Client_ID=fa.AD_Client_ID)
                     WHERE fa.DateAcct BETWEEN pd.StartDate AND pd.EndDate
                     GROUP BY fa.ExpenseName,fa.StdPrecision
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
                    obj.stdPrecision = Util.GetValueOfInt(ds.Tables[0].Rows[i]["StdPrecision"]);
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
                         CEIL(CAST(cp.PeriodNo AS NUMERIC)/3) AS CurQuarter
                         FROM C_Calendar cc
                         INNER JOIN AD_ClientInfo ci ON (ci.C_Calendar_ID=cc.C_Calendar_ID)
                         LEFT JOIN AD_OrgInfo oi ON (oi.C_Calendar_ID=cc.C_Calendar_ID)
                         INNER JOIN C_Year cy ON (cy.C_Calendar_ID=cc.C_Calendar_ID)
                         INNER JOIN C_Period cp  ON (cy.C_Year_ID = cp.C_Year_ID)
                         WHERE 
                         cy.IsActive = 'Y'
                         AND cp.IsActive = 'Y'
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
                    sql += " AND date_trunc('MONTH', p.StartDate) >=  DATE_TRUNC('MONTH', CURRENT_DATE) - INTERVAL '6 MONTHS'";
                    sql += " AND date_trunc('MONTH', p.EndDate) <= (DATE_TRUNC('MONTH', CURRENT_DATE) - INTERVAL '1 day')";
                }
                else
                {
                    sql += " AND TRUNC(p.startdate,'MM') >= TRUNC(ADD_MONTHS(TRUNC(Current_Date), -6), 'MM')";
                    sql += " AND TRUNC(p.EndDate,'MM') <= LAST_DAY(ADD_MONTHS(TRUNC(Current_Date, 'MM'), -1))";
                }
            }
            else if (ListValue == "06")
            {
                //Last 12 Months Data
                if (DB.IsPostgreSQL())
                {
                    sql += " AND date_trunc('MONTH', p.StartDate) >=  DATE_TRUNC('month', CURRENT_DATE) - INTERVAL '12 months'";
                    sql += " AND date_trunc('MONTH', p.EndDate) <= (DATE_TRUNC('MONTH', CURRENT_DATE) - INTERVAL '1 day')";
                }
                else
                {
                    sql += " AND TRUNC(p.startdate,'MM') >= TRUNC(ADD_MONTHS(TRUNC(Current_Date), -12), 'MM')";
                    sql += " AND TRUNC(p.EndDate,'MM') <= LAST_DAY(ADD_MONTHS(TRUNC(Current_Date, 'MM'), -1))";
                }
            }

            sql = MRole.GetDefault(ctx).AddAccessSQL(sql, "fa", MRole.SQL_FULLYQUALIFIED, MRole.SQL_RO);
            sql += @" GROUP BY
                      acct.C_AcctSchema_ID, 
                      fa.AD_Client_ID,
                      fa.C_Period_ID,
                      y.CalendarYears,
                      p.Name, 
                      c.StdPrecision,
                      p.PeriodNo
                      order by
                      acct.C_AcctSchema_ID, 
                      fa.AD_Client_ID,
                      y.CalendarYears,
                      p.PeriodNo,
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
                               cs.C_Payment_ID,
                               ci.DocStatus
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
                    obj.DocStatus = Util.GetValueOfString(ds.Tables[0].Rows[i]["DocStatus"]);
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
                            DateTime? StartDate = Util.GetValueOfDateTime(dsPeriod.Tables[0].Rows[0]["StartDate"]);
                            DateTime? EndDate = Util.GetValueOfDateTime(dsPeriod.Tables[0].Rows[0]["EndDate"]);
                            sqlmain.Append(@" AND TRUNC(cs.DueDate) BETWEEN " +
                            (GlobalVariable.TO_DATE(StartDate, true)));
                            sqlmain.Append(@" AND " +
                             (GlobalVariable.TO_DATE(EndDate, true)));
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
                            DateTime? StartDate = Util.GetValueOfDateTime(dsPeriod.Tables[0].Rows[0]["StartDate"]);
                            DateTime? EndDate = Util.GetValueOfDateTime(dsPeriod.Tables[0].Rows[0]["EndDate"]);
                            sqlmain.Append(@" AND TRUNC(cs.DueDate) BETWEEN " +
                            (GlobalVariable.TO_DATE(StartDate, true)));
                            sqlmain.Append(@" AND " +
                             (GlobalVariable.TO_DATE(EndDate, true)));
                        }
                    }
                    //Passed Due Date
                    else if (FinancialPeriodValue == "03")
                    {
                        sqlmain.Append(" AND Current_Date > cs.DueDate");
                    }
                }
                if (!String.IsNullOrEmpty(fromDate) || !String.IsNullOrEmpty(toDate))
                {
                    sqlmain.Append(GetExpInvDateSql("TRUNC(cs.DueDate)", fromDate, toDate));
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
                             INNER JOIN C_Doctype dt ON (dt.C_Doctype_ID = co.C_Doctype_ID)
                             INNER JOIN C_BPartner cb ON (co.C_BPartner_ID = cb.C_BPartner_ID)
                             INNER JOIN C_Currency cy ON (cy.C_Currency_ID=co.C_Currency_ID)
                             INNER JOIN VA009_PaymentMethod pm ON (co.VA009_PaymentMethod_ID=pm.VA009_PaymentMethod_ID)
                             LEFT JOIN AD_Image custimg ON (custimg.AD_Image_ID = CAST(cb.Pic AS INTEGER))", "ps", MRole.SQL_FULLYQUALIFIED, MRole.SQL_RW));
                sqlmain.Append(OrderCheck + " AND co.DocStatus IN ('CO','CL') AND NVL(dt.DocSubTypeSO,' ') NOT IN ('BO','ON', 'OB') AND ps.VA009_IsPaid='N'");
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
                            DateTime? StartDate = Util.GetValueOfDateTime(dsPeriod.Tables[0].Rows[0]["StartDate"]);
                            DateTime? EndDate = Util.GetValueOfDateTime(dsPeriod.Tables[0].Rows[0]["EndDate"]);
                            sqlmain.Append(@" AND TRUNC(ps.DueDate) BETWEEN " +
                            (GlobalVariable.TO_DATE(StartDate, true)));
                            sqlmain.Append(@" AND " +
                             (GlobalVariable.TO_DATE(EndDate, true)));
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
                            DateTime? StartDate = Util.GetValueOfDateTime(dsPeriod.Tables[0].Rows[0]["StartDate"]);
                            DateTime? EndDate = Util.GetValueOfDateTime(dsPeriod.Tables[0].Rows[0]["EndDate"]);
                            sqlmain.Append(@" AND TRUNC(ps.DueDate) BETWEEN " +
                            (GlobalVariable.TO_DATE(StartDate, true)));
                            sqlmain.Append(@" AND " +
                             (GlobalVariable.TO_DATE(EndDate, true)));
                        }
                    }
                    //Passed Due Date
                    else if (FinancialPeriodValue == "03")
                    {
                        sqlmain.Append(" AND Current_Date > ps.DueDate");
                    }
                }
                if (!String.IsNullOrEmpty(fromDate) || !String.IsNullOrEmpty(toDate))
                {
                    sqlmain.Append(GetExpInvDateSql("TRUNC(ps.DueDate)", fromDate, toDate));
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
                    OrderWinId = GetWindowId("VAS_SalesOrder", "Sales Order");
                    InvoiceWinId = GetWindowId("VAS_ARInvoice", "Invoice (Customer)");
                }
                else
                {
                    ExpInvID = GetWindowId("VAS_ExpenseInvoice", "Expense Invoice");
                    OrderWinId = GetWindowId("VAS_PurchaseOrder", "Purchase Order");
                    InvoiceWinId = GetWindowId("VAS_APInvoice", "Invoice (Vendor)");
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
                    if (Util.GetValueOfString(ds.Tables[0].Rows[i]["WindowType"]) == "Order")
                    {
                        obj.Window_ID = OrderWinId;
                        obj.Primary_ID = "C_Order_ID";
                    }
                    else if (Util.GetValueOfString(ds.Tables[0].Rows[i]["IsExInv"]) == "Y")
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
                            ipl.PricePrecision AS InvoicePriceListPrecision,
                            (SELECT NVL(DueAmt, 0) FROM VA009_OrderPaySchedule ops WHERE ops.C_Order_ID = ol.C_Order_ID AND ops.VA009_IsPaid= 'N') AS NotPaidAdvanceOrder
                            FROM
                            C_Invoice i 
                            INNER JOIN C_InvoiceLine il ON (il.C_Invoice_ID = i.C_Invoice_ID)
                            INNER JOIN M_Product p ON (p.M_Product_ID = il.M_Product_ID)
                            INNER JOIN M_PriceList ipl ON (ipl.M_PriceList_ID=i.M_PriceList_ID)
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
                    obj.InvoicePriceListPrecision= Util.GetValueOfInt(ds.Tables[0].Rows[i]["InvoicePriceListPrecision"]);
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
                IsDiscrepancy = false;
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
        public CashFlowClass GetCashFlowData(Ctx ctx, string ListValue)
        {
            string[] labels = null;
            decimal[] lstCashOutData = null;
            decimal[] lstCashInData = null;
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
            if (!string.IsNullOrEmpty(errorMessage))
            {
                obj.ErrorMessage = errorMessage;
                return obj;
            }
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
            //sql.Append(@"SELECT SUM(t.CashOutbifurcated_Amount) AS CashOutAmt,SUM(t.CashInbifurcated_Amount) AS CashInAmt FROM (");
            sql.Append("WITH CashData AS (");
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
                             CURRENT_DATE, 
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
                             CURRENT_DATE, 
                             o.C_ConversionType_ID, 
                             o.AD_Client_ID, 
                             o.AD_Org_ID
                         ) 
                         ELSE 0 END AS CashInbifurcated_Amount,");
            if (DB.IsPostgreSQL())
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
            sqlOrder.Append(@" FROM C_Order o
                     INNER JOIN C_OrderLine ol ON (o.C_Order_ID = ol.C_Order_ID)
                     INNER JOIN C_DocType doc ON (doc.C_DocType_ID = o.C_DocTypeTarget_ID)
                     INNER JOIN C_PAYMENTTERM pt ON (o.C_PAYMENTTERM_ID = pt.C_PAYMENTTERM_ID AND pt.VA009_Advance = 'N')
                     LEFT JOIN C_PAYSCHEDULE ps ON (pt.C_PAYMENTTERM_ID = ps.C_PAYMENTTERM_ID AND ps.VA009_Advance = 'N')
                     LEFT JOIN C_InvoiceLine il ON (il.C_OrderLine_ID = ol.C_OrderLine_ID AND il.Processed = 'Y' AND il.ReversalDoc_ID IS NULL)
                     WHERE 
                         o.DOCSTATUS IN ('CO', 'CL') AND NVL(doc.DocSubTypeSO,' ') NOT IN ('BO','ON', 'OB')
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
                             CURRENT_DATE, 
                             o.C_ConversionType_ID, 
                             o.AD_Client_ID, 
                             o.AD_Org_ID
                         ) ELSE 0 END AS CashOutbifurcated_Amount,
                         CASE WHEN o.IsSOtrx='Y' THEN
                         currencyConvert(CASE WHEN o.IsReturnTrx = 'Y' THEN -1 ELSE 1 END * ips.DueAmt, o.C_Currency_ID, 
                             " + C_Currency_ID + @", 
                             CURRENT_DATE, 
                             o.C_ConversionType_ID, 
                             o.AD_Client_ID, 
                             o.AD_Org_ID
                         ) ELSE 0 END AS CashInbifurcated_Amount,
                         ips.DueDate AS expected_due_date
                     FROM 
                         C_Invoice o
                     INNER JOIN C_InvoicePaySchedule ips ON (o.C_Invoice_ID = ips.C_Invoice_ID) 
                     INNER JOIN C_DocType doc ON (doc.C_DocType_ID = o.C_DocTypeTarget_ID)
                     WHERE 
                          o.DOCSTATUS IN ( 'CO', 'CL')
                          AND ips.VA009_IsPaid = 'N'", "o", MRole.SQL_FULLYQUALIFIED, MRole.SQL_RW));
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
                             CURRENT_DATE, 
                             o.C_ConversionType_ID, 
                             o.AD_Client_ID, 
                             o.AD_Org_ID
                         ) ELSE 0 END AS CashOutbifurcated_Amount,
                         CASE WHEN o.IsSOtrx='Y' THEN
                         currencyConvert(CASE WHEN o.IsReturnTrx = 'Y' THEN -1 ELSE 1 END * ips.DueAmt, o.C_Currency_ID, 
                             " + C_Currency_ID + @", 
                             CURRENT_DATE, 
                             o.C_ConversionType_ID, 
                             o.AD_Client_ID, 
                             o.AD_Org_ID
                         ) ELSE 0 END AS CashInbifurcated_Amount,
                         ips.DueDate AS expected_due_date
                     FROM 
                         C_Order o
                     INNER JOIN VA009_OrderPaySchedule ips ON (o.C_Order_ID = ips.C_Order_ID) 
                     INNER JOIN C_DocType doc ON (doc.C_DocType_ID = o.C_DocTypeTarget_ID)
                     WHERE 
                          o.DOCSTATUS IN ( 'CO', 'CL') AND doc.DocBaseType IN ('SOO','POO') AND NVL(doc.DocSubTypeSO,' ') NOT IN ('BO','ON', 'OB')
                          AND ips.VA009_IsPaid = 'N'", "o", MRole.SQL_FULLYQUALIFIED, MRole.SQL_RW));
            sql.Append(sqlmain);
            sql.Append(")");
            sql.Append($@",PeriodData AS (SELECT p.Name,p.StartDate,p.EndDate FROM
                          C_Period p INNER JOIN C_Year cy on (cy.C_Year_ID=p.C_Year_ID)
                          WHERE p.IsActive = 'Y' AND cy.C_Calendar_ID={calendar_ID})");
            sql.Append(@"SELECT
                         SUM(cd.cashoutbifurcated_amount) AS cashoutamt,
                         SUM(cd.cashinbifurcated_amount)  AS cashinamt,
                         pd.Name,pd.startdate
                         FROM CashData cd
                         INNER JOIN PeriodData pd on (1=1 and cd.expected_due_date BETWEEN pd.StartDate AND pd.EndDate)");

            sql.Append(" WHERE ");
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
                    DateTime? StartDate = Util.GetValueOfDateTime(dsPeriod.Tables[0].Rows[0]["StartDate"]);
                    DateTime? EndDate = Util.GetValueOfDateTime(dsPeriod.Tables[0].Rows[0]["EndDate"]);
                    sql.Append(@" TRUNC(cd.expected_due_date) BETWEEN " +
                    (GlobalVariable.TO_DATE(StartDate, true)));
                    sql.Append(@" AND " +
                     (GlobalVariable.TO_DATE(EndDate, true)));
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
                    DateTime? StartDate = Util.GetValueOfDateTime(dsPeriod.Tables[0].Rows[0]["StartDate"]);
                    DateTime? EndDate = Util.GetValueOfDateTime(dsPeriod.Tables[0].Rows[0]["EndDate"]);
                    sql.Append(@" TRUNC(cd.expected_due_date) BETWEEN " +
                    (GlobalVariable.TO_DATE(StartDate, true)));
                    sql.Append(@" AND " +
                     (GlobalVariable.TO_DATE(EndDate, true)));
                }
            }
            //This Quarter Year
            else if (ListValue.Equals("03"))
            {
                string quarterSql = $@"SELECT Min(p.StartDate) AS StartDate,MAX(p.EndDate) AS EndDate FROM C_Period p INNER JOIN C_Year y ON (p.C_Year_ID = y.C_Year_ID)
                                                    WHERE CEIL(CAST(p.PeriodNo AS NUMERIC)/3)={currQuarter} AND y.CalendarYears={GlobalVariable.TO_STRING(CurrentYear.ToString())} AND y.C_Calendar_ID={calendar_ID}";
                dsYear = DB.ExecuteDataset(quarterSql);
                DateTime? StartDate = Util.GetValueOfDateTime(dsYear.Tables[0].Rows[0]["StartDate"]);
                DateTime? EndDate = Util.GetValueOfDateTime(dsYear.Tables[0].Rows[0]["EndDate"]);
                sql.Append(@" TRUNC(cd.expected_due_date) BETWEEN " +
                (GlobalVariable.TO_DATE(StartDate, true)));
                sql.Append(@" AND " +
                 (GlobalVariable.TO_DATE(EndDate, true)));
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
                                           WHERE CEIL(CAST(p.PeriodNo AS NUMERIC)/3)={NextQuarter} 
                                           AND y.CalendarYears={GlobalVariable.TO_STRING(CurrentYear.ToString())} 
                                           AND y.C_Calendar_ID={calendar_ID}";
                    dsYear = DB.ExecuteDataset(quarterSql);
                    DateTime? StartDate = Util.GetValueOfDateTime(dsYear.Tables[0].Rows[0]["StartDate"]);
                    DateTime? EndDate = Util.GetValueOfDateTime(dsYear.Tables[0].Rows[0]["EndDate"]);
                    sql.Append(@" TRUNC(cd.expected_due_date) BETWEEN " +
                    (GlobalVariable.TO_DATE(StartDate, true)));
                    sql.Append(@" AND " +
                     (GlobalVariable.TO_DATE(EndDate, true)));
                }

            }
            //Next 6 months
            else if (ListValue == "05")
            {
                // Next 6 months data (current month + Next 5 months)
                if (DB.IsPostgreSQL())
                {
                    sql.Append(" date_trunc('MONTH', cd.expected_due_date) >= DATE_TRUNC('MONTH', CURRENT_DATE)");
                    sql.Append(" AND date_trunc('MONTH', cd.expected_due_date) <= DATE_TRUNC('MONTH', CURRENT_DATE) + INTERVAL '5 MONTHS'");
                }
                else
                {

                    sql.Append(" TRUNC(cd.expected_due_date) >= TRUNC(Current_Date, 'MM')");
                    sql.Append(" AND TRUNC(cd.expected_due_date) <= TRUNC(ADD_MONTHS(TRUNC(Current_Date), 5), 'MM')");
                }
            }
            sql.Append(@"Group BY pd.Name,pd.StartDate
                         ORDER BY pd.StartDate");
            DataSet ds = DB.ExecuteDataset(sql.ToString(), null, null);
            if (ds != null && ds.Tables[0].Rows.Count > 0)
            {

                lstCashInData = new decimal[ds.Tables[0].Rows.Count];
                lstCashOutData = new decimal[ds.Tables[0].Rows.Count];
                labels = new string[ds.Tables[0].Rows.Count];
                DataRow dr = null;
                for (int i = 0; i < ds.Tables[0].Rows.Count; i++)
                {
                    dr = ds.Tables[0].Rows[i];

                    lstCashInData[i] = Util.GetValueOfDecimal(dr["cashinamt"]);
                    lstCashOutData[i] = Util.GetValueOfDecimal(dr["cashoutamt"]);
                    labels[i] = Util.GetValueOfString(dr["Name"]);
                }
                obj.stdPrecision = precision;
                obj.labels = labels;
                obj.lstCashOutData = lstCashOutData;
                obj.lstCashInData = lstCashInData;
            }
            else
            {
                obj.ErrorMessage = Msg.GetMsg(ctx, "VAS_CashFlowDataNotFound");
            }
            return obj;
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
            string sql = $@"SELECT CEIL(CAST(p.PeriodNo AS NUMERIC)/3) FROM C_Period p INNER JOIN C_Year y ON (p.C_Year_ID = y.c_year_ID)
                            WHERE y.CalendarYears={GlobalVariable.TO_STRING(CalenderYear.ToString())} AND y.C_Calendar_ID={calendar_ID}";
            int Quarter = Util.GetValueOfInt(DB.ExecuteScalar(sql, null, null));
            return Quarter;
        }
        /// <summary>
        /// This function is used to Generate Invoice against GRN
        /// </summary>
        /// <param name="ctx">Context</param>
        /// <param name="grnid">M_InOut_ID</param>
        /// <param name="invRef">Invoice Reference Number</param>
        /// <param name="docId">C_Doctype_ID<param>
        /// <param name="IsGenCheck">Generate Chrges</param>
        /// <returns>Dictionary</returns>
        /// <author>VIS_427</author>
        public Dictionary<string, object> GenerateInvoice(Ctx ctx, int grnid, string invRef, int docId, bool IsGenCheck)
        {
            int C_Invoice_ID = 0;
            string exceptionMessage = string.Empty;
            InOutCreateInvoice obj = new InOutCreateInvoice();

            try
            {
                // Set parameters
                obj.SetParameter(invRef, docId, IsGenCheck, grnid,ctx);

                // Call the Generate method, which might throw an exception
                obj.Generate();

                // Get the Invoice ID from the object if no exception
                C_Invoice_ID = obj.C_Invoice_ID;
            }
            catch (Exception ex)
            {
                // Log the exception (you could log it to a file, database, or console depending on your requirements)
                exceptionMessage = ex.Message;
            }

            // Prepare the dictionary to return the Invoice ID and Exception message
            Dictionary<string, object> result = new Dictionary<string, object>
            {
                     { "C_Invoice_ID", C_Invoice_ID },
                     { "ExceptionMessage", exceptionMessage }
            };

            return result;
        }
        /// <summary>
        /// This function is used to Get The data for Monthly average balance
        /// </summary>
        /// <param name="ctx">Context</param>
        /// <param name="C_BankAccount_ID">C_BankAccount_ID</param>
        /// <returns>Monthly average balance data</returns>
        /// <author>VIS_427</author>
        public MonthlyAvBankBal GetMonthlyAvBankBalData(Ctx ctx,int C_BankAccount_ID)
        {
            string[] labels = null;
            decimal[] lstAPPayAmt = null;
            decimal[] lstARPayAmt = null;
            decimal[] lstEndingBal = null;
            MonthlyAvBankBal obj = new MonthlyAvBankBal();
            StringBuilder sqlmain = new StringBuilder();
            StringBuilder sql = new StringBuilder();
            int C_Currency_ID = ctx.GetContextAsInt("$C_Currency_ID");
            //fetched Default conversion type from context
            int C_ConversionType_ID = ctx.GetContextAsInt("C_ConversionType_ID");
            int precision = 2;string ISO_Code = "";
            List<MonthlyAvBankBal> payMonthlyAvBankBal = new List<MonthlyAvBankBal>();
            string CurrentYear = "";
            int calendar_ID = 0;
            int currQuarter = 0;
            decimal prevBalance = 0;
            // Get Financial Year Data 
            DataSet dsFinancialYear = GetFinancialYearDetail(ctx, out string errorMessage);
            if (!string.IsNullOrEmpty(errorMessage))
            {
                obj.ErrorMessage = errorMessage;
                return obj;
            }
            if (dsFinancialYear != null && dsFinancialYear.Tables[0].Rows.Count > 0)
            {
                CurrentYear = Util.GetValueOfString(dsFinancialYear.Tables[0].Rows[0]["CalendarYears"]);
                calendar_ID = Util.GetValueOfInt(dsFinancialYear.Tables[0].Rows[0]["C_Calendar_ID"]);
                currQuarter = Util.GetValueOfInt(dsFinancialYear.Tables[0].Rows[0]["CurQuarter"]);
            }
            //if bank Account id is not zero then fetched its cuurrency
            if(C_BankAccount_ID != 0)
            {
               C_Currency_ID = Util.GetValueOfInt(DB.ExecuteScalar("SELECT C_Currency_ID FROM C_BankAccount WHERE C_BankAccount_ID=" + C_BankAccount_ID,null,null));
            }
            //Fetched the currency details 
            sql.Append(@"SELECT StdPrecision,ISO_Code FROM C_Currency WHERE C_Currency_ID=" + C_Currency_ID);
             DataSet dsCurrency = DB.ExecuteDataset(sql.ToString(), null, null);
            if (dsCurrency != null && dsCurrency.Tables[0].Rows.Count > 0)
            {
                precision = Util.GetValueOfInt(dsCurrency.Tables[0].Rows[0]["StdPrecision"]);
                ISO_Code = Util.GetValueOfString(dsCurrency.Tables[0].Rows[0]["ISO_Code"]);
            }

            sql.Clear();
            sql.Append("WITH PaymentData AS (");
            sqlmain.Append(MRole.GetDefault(ctx).AddAccessSQL($@"
                          select C_Payment.AD_Client_ID, C_Payment.AD_Org_ID, C_Payment.C_Payment_id, C_Payment.isreceipt, C_Payment.dateacct,
                            CASE WHEN C_Payment.IsReceipt='Y' THEN CASE WHEN C_Payment.C_Currency_ID != " + C_Currency_ID + @" then ROUND(coalesce(currencyconvert(C_Payment.PayAmt,
                         		C_Payment.C_Currency_ID,
                         		" + C_Currency_ID + @",
                         		C_Payment.DateAcct,
                         		C_Payment.C_ConversionType_ID,
                         		C_Payment.AD_Client_ID,
                         		C_Payment.AD_Org_ID),
                         		0),
                         		" + precision + @")
                         		ELSE C_Payment.PayAmt
                         	END 
                             ELSE 0 END as ARPayAmt,
                            CASE WHEN C_Payment.IsReceipt='N' THEN
                            CASE WHEN
                         		C_Payment.C_Currency_ID != " + C_Currency_ID + @" then ROUND(COALESCE(currencyconvert(C_Payment.PayAmt,
                         		C_Payment.C_Currency_ID,
                         		" + C_Currency_ID + @",
                         		C_Payment.DateAcct,
                         		C_Payment.C_ConversionType_ID,
                         		C_Payment.AD_Client_ID,
                         		C_Payment.AD_Org_ID),
                         		0),
                         		" + precision + @")
                         		ELSE C_Payment.PayAmt
                         	 END ELSE 0 END as APPayAmt,
                         	C_Payment.C_Currency_ID
                         FROM C_Payment", "C_Payment", MRole.SQL_FULLYQUALIFIED, MRole.SQL_RW));
            sqlmain.Append(@" AND C_Payment.IsActive = 'Y'
                            and C_Payment.DocStatus IN ('CO','CL')");
            if(C_BankAccount_ID != 0)
            {
                sqlmain.Append(" AND C_Payment.C_BankAccount_ID=" + C_BankAccount_ID);
            }
            sql.Append(sqlmain +")");
            sql.Append(@",latest_bank_data AS (");
            sqlmain.Clear();
            sqlmain.Append(MRole.GetDefault(ctx).AddAccessSQL(@"SELECT C_BankAccount.AD_Client_ID,
                                 C_BankAccount.AD_Org_ID,
                                 C_BankAccount.c_bank_id,
                                 C_BankAccount.C_BankAccount_ID,
                                 CASE WHEN C_BankAccountline.StatementDate =  MAX(C_BankAccountline.StatementDate) OVER (PARTITION BY C_BankAccount.AD_Org_ID, 
                                 TO_CHAR(C_BankAccountline.StatementDate, 'YYYY-MM'), C_BankAccount.C_BankAccount_ID) THEN 'EndingBalance' 
                                 ELSE NULL END AS isendingbalance,
                                 C_BankAccountline.StatementDate,
                                 CASE WHEN C_BankAccount.C_Currency_ID !=" +C_Currency_ID+ @" THEN 
                                 ROUND(COALESCE(currencyconvert(
                                 C_BankAccountline.EndingBalance,
	                             C_BankAccount.C_Currency_ID,
	                             " + C_Currency_ID + @",
	                             MAX(C_BankAccountline.StatementDate) over (partition by C_BankAccount.AD_Org_ID,
	                             TO_CHAR(C_BankAccountline.StatementDate, 'YYYY-MM'),
	                             C_BankAccount.C_BankAccount_ID),
	                             " + C_ConversionType_ID + @",
	                             C_BankAccount.AD_Client_ID,
	                             C_BankAccount.AD_Org_ID),0),
                                 " + precision + @") ELSE C_BankAccountline.EndingBalance END AS EndBal,
                                 C_BankAccountline.EndingBalance
                             FROM C_BankAccountline
                             INNER JOIN C_BankAccount
                             ON (C_BankAccountline.C_BankAccount_ID = C_BankAccount.C_BankAccount_ID)", "C_BankAccountline", MRole.SQL_FULLYQUALIFIED, MRole.SQL_RW));
            sqlmain.Append(" AND C_BankAccount.isactive = 'Y' AND C_BankAccountline.isactive = 'Y'");
            if (C_BankAccount_ID != 0)
            {
                sqlmain.Append(" AND C_BankAccount.C_BankAccount_ID=" + C_BankAccount_ID);
            }
            sql.Append(sqlmain + ")");
            sql.Append($@",PeriodData AS (SELECT p.Name,p.StartDate,p.EndDate,p.PeriodNo FROM
                          C_Period p INNER JOIN C_Year cy on (cy.C_Year_ID=p.C_Year_ID)
                          WHERE p.IsActive = 'Y' AND cy.C_Calendar_ID={calendar_ID} AND cy.calendaryears={GlobalVariable.TO_STRING(CurrentYear)})");
            sql.Append(@",PaymentPeriodData as (SELECT 
                                                  pd.Name, 
                                                  pd.PeriodNo,
                                                  SUM(cd.ARPayAmt) AS ARPayAmt,
                                                  SUM(cd.APPayAmt) AS APPayAmt
                                              FROM 
                                                  PeriodData pd
                                              LEFT JOIN PaymentData cd 
                                                  ON (TRUNC(cd.DateAcct) BETWEEN pd.StartDate AND pd.EndDate)
                                              GROUP BY 
                                                  pd.Name, pd.PeriodNo)
                                              ,BankPeriodData as (
                                              SELECT 
                                                  pd.Name, 
                                                  pd.PeriodNo,
                                                  SUM(lbd.EndBal) AS EndingBalance
                                              FROM 
                                                  PeriodData pd
                                              LEFT JOIN latest_bank_data lbd 
                                                  ON (TRUNC(lbd.StatementDate) BETWEEN pd.StartDate AND pd.EndDate AND lbd.isendingbalance = 'EndingBalance')
                                              GROUP BY 
                                                  pd.Name, pd.PeriodNo)");
            sql.Append(@"SELECT ppd.PeriodNo,COALESCE(bpd.EndingBalance,0) AS EndingBalance,ppd.ARPayAmt,ppd.APPayAmt,ppd.Name
                         FROM BankPeriodData bpd INNER JOIN PaymentPeriodData ppd on (bpd.PeriodNo=ppd.PeriodNo)");
            sql.Append(@"ORDER BY PeriodNo");
            DataSet ds = DB.ExecuteDataset(sql.ToString(), null, null);
            if (ds != null && ds.Tables[0].Rows.Count > 0)
            {
                //list to store ap payment amount
                lstAPPayAmt = new decimal[ds.Tables[0].Rows.Count];
                //list to store ar payment amount
                lstARPayAmt = new decimal[ds.Tables[0].Rows.Count];
                //list to store bank account ending balance
                lstEndingBal = new decimal[ds.Tables[0].Rows.Count];
                //list to store period names
                labels = new string[ds.Tables[0].Rows.Count];
                DataRow dr = null;
                for (int i = 0; i < ds.Tables[0].Rows.Count; i++)
                {
                    dr = ds.Tables[0].Rows[i];

                    lstAPPayAmt[i] = Util.GetValueOfDecimal(dr["APPayAmt"]);
                    lstARPayAmt[i] = Util.GetValueOfDecimal(dr["ARPayAmt"]);
                    /*If for any month ending balance is not entered then system will pick 
                     previous months ending balance*/
                    if (Util.GetValueOfDecimal(dr["endingbalance"]) != 0)
                    {
                        prevBalance = Util.GetValueOfDecimal(dr["endingbalance"]);
                        lstEndingBal[i] = Util.GetValueOfDecimal(dr["endingbalance"]);
                    }
                    else
                    {
                        lstEndingBal[i] = prevBalance;
                    }
                    labels[i] = Util.GetValueOfString(dr["Name"]);
                }
                obj.stdPrecision = precision;
                obj.ISO_Code = ISO_Code;
                obj.labels = labels;
                obj.APPayAmt = lstAPPayAmt;
                obj.ARPayAmt = lstARPayAmt;
                obj.EndingBal = lstEndingBal;
            }
            else
            {
                obj.ErrorMessage = Msg.GetMsg(ctx, "VAS_CashFlowDataNotFound");
            }
            return obj;
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
        public string Type { get; set; }
    }
    public class ExpectedInvoice
    {
        public decimal TotalAmt { get; set; }
        public string Symbol { get; set; }
        public string RecordType { get; set; }
        public int stdPrecision { get; set; }
        public int Record_ID { get; set; }
        public int InvWinID { get; set; }
        public string Primary_ID { get; set; }
        public int Window_ID { get; set; }
        public string IsFullyDelivered { get; set; }
        public string ImageUrl { get; set; }
        public string Name { get; set; }
        public DateTime OrderdDate { get; set; }
        public DateTime DatePromised { get; set; }
        public string DocumentNo { get; set; }
        public int recordCount { get; set; }
        public int AD_Org_ID { get; set; }
        public string InvoiceRule { get; set; }

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
        public string DocStatus { get; set; }
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
        public int InvoicePriceListPrecision { get; set; }
    }
    public class CashFlowClass
    {
        public string ErrorMessage { get; set; }
        public int stdPrecision { get; set; }
        public string[] labels { get; set; }
        public decimal[] lstCashInData { get; set; }
        public decimal[] lstCashOutData { get; set; }
    }
    public class MonthlyAvBankBal
    {
        public string ErrorMessage { get; set; }
        public string ISO_Code { get; set; }
        public int stdPrecision { get; set; }
        public string[] labels { get; set; }
        public decimal[] APPayAmt { get; set; }
        public decimal[] ARPayAmt { get; set; }
        public decimal[] EndingBal { get; set; }
    }
}
