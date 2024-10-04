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
using System.Dynamic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using VAdvantage.Utility;
using VAdvantage.Common;
using VAdvantage.Model;

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

            string sql = @"WITH StatusAndUPCData AS (
                        SELECT ol.C_OrderLine_ID, i.ImageUrl,  cu.Name AS UOM,
                         p.Name AS  ProductName,
                          CASE  WHEN ol.M_AttributeSetInstance_ID IS NOT NULL AND ol.M_AttributeSetInstance_ID > 0 THEN ma.Description
                          ELSE NULL
                        END AS AttributeName,
                          CASE 
                        WHEN ol.QtyOrdered - ol.QtyDelivered = 0 THEN 'DE'
                        WHEN ol.QtyDelivered = 0 THEN 'OP'
                        ELSE 'PD'
                        END AS OrderLineStatusValue,
                        CASE 
                        WHEN SUM(ol.QtyOrdered - ol.QtyDelivered) OVER () = 0 THEN 'DE'
                        WHEN SUM(ol.QtyDelivered) OVER () = 0 THEN 'OP'
                        ELSE 'PD'
                        END AS OrderStatusValue,
                       COALESCE(attr.UPC, cuconv.UPC, p.UPC,p.Value) AS PreferredUPC,
                       ROW_NUMBER() OVER (PARTITION BY ol.C_OrderLine_ID ORDER BY
                       CASE
                       WHEN attr.UPC IS NOT NULL THEN 1
                       WHEN cuconv.UPC IS NOT NULL THEN 2
                       ELSE 3
                       END
                       ) AS rn,              
                       CASE WHEN p.C_UOM_ID !=  ol.C_UOM_ID  THEN ROUND(ol.QtyDelivered/NULLIF(cuconv.dividerate, 0), 2)
                       ELSE ol.QtyDelivered END AS QtyDelivered
                      FROM
                      C_OrderLine ol
                      INNER JOIN C_Order o ON (ol.C_Order_ID = o.C_Order_ID)
                      INNER JOIN M_Product p ON (ol.M_Product_ID = p.M_Product_ID)
                      INNER JOIN C_UOM cu ON (cu.C_UOM_ID = ol.C_UOM_ID)
                      LEFT JOIN M_AttributeSetInstance ma ON (ma.M_AttributeSetInstance_ID = ol.M_AttributeSetInstance_ID)
                      LEFT JOIN AD_Image i ON (i.AD_Image_ID = p.AD_Image_ID)
                      LEFT JOIN M_ProductAttributes attr ON (attr.M_AttributeSetInstance_ID = ol.M_AttributeSetInstance_ID
                      AND attr.M_Product_ID = ol.M_Product_ID
                      AND attr.C_UOM_ID = ol.C_UOM_ID
                      AND attr.UPC IS NOT NULL)
                      LEFT JOIN C_UOM_Conversion cuconv ON (cuconv.C_UOM_ID = p.C_UOM_ID
                      AND cuconv.C_UOM_To_ID = ol.C_UOM_ID
                     AND cuconv.M_Product_ID = ol.M_Product_ID)
                     WHERE ol.C_Order_ID = " + OrderID + @"
                    )
                   SELECT sod.C_OrderLine_ID,sod.ImageUrl,sod.UOM, ol.QtyEntered AS QtyOrdered, sod.QtyDelivered,sod.PreferredUPC AS UPC,
                   sod.OrderLineStatusValue, arlOrderLine.Name As OrderLineStatus,sod.OrderStatusValue,arlOrder.Name AS OrderStatus ,
                  sod.ProductName,sod.AttributeName
                   FROM StatusAndUPCData sod
                   INNER JOIN C_OrderLine ol ON (sod.C_OrderLine_ID = ol.C_OrderLine_ID)
                   LEFT JOIN AD_Reference ar ON (ar.Name = 'VAS_OrderStatus')
                  LEFT JOIN AD_Ref_List arlOrderLine ON (arlOrderLine.AD_Reference_ID = ar.AD_Reference_ID
                  AND arlOrderLine.Value = sod.OrderLineStatusValue)
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
            List<ARInvWidgData> ARInvWidgData = new List<ARInvWidgData>();
            string docBaseTypeARI_APT = ISOtrx ? "'ARI'" : "'API'";
            string docBaseTypeARC_APC = ISOtrx ? "'ARC'" : "'APC'";

            sql.Append($@"WITH InvoiceData AS (
                         {MRole.GetDefault(ctx).AddAccessSQL($@"SELECT
                             ci.AD_Client_ID,
                             cs.C_InvoicePaySchedule_ID,
                             cd.DocBaseType,
                             ci.DateInvoiced,
                             currencyConvert(cs.DueAmt ,cs.C_Currency_ID ,CAST(cs.VA009_BseCurrncy AS INTEGER),ci.DateAcct ,ci.C_ConversionType_ID ,cs.AD_Client_ID ,cs.AD_Org_ID ) AS DueAmt
                         FROM
                             C_Invoice ci
                             INNER JOIN C_InvoicePaySchedule cs ON (cs.C_Invoice_ID = ci.C_Invoice_ID)
                             INNER JOIN C_DocType cd ON (cd.C_DocType_ID = ci.C_DocTypeTarget_ID)
                             WHERE cd.DocBaseType IN ('ARI', 'ARC','API','APC') AND ci.DocStatus IN ('CO','CL') AND cs.VA009_IsPaid='N' ", "ci", MRole.SQL_FULLYQUALIFIED, MRole.SQL_RW
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
                         DateInvoiced >= Current_Date - 30
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
                             custimg.ImageExtension,
                             cb.Name,
                             o.AD_Client_ID,
                             SUM( CASE
                                    WHEN mil.C_OrderLine_ID IS NOT NULL
                                          AND l.qtydelivered > l.qtyinvoiced THEN
                                             coalesce(
                                                 l.QtyOrdered, 0
                                             ) - coalesce(
                                                 l.QtyDelivered, 0)
                                             
                                     WHEN ci.C_OrderLine_ID IS NOT NULL
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
                                     WHEN mil.C_OrderLine_ID IS NOT NULL
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
                                     WHEN ci.c_orderline_id IS NOT NULL
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
                                             COALESCE(
                                                 l.qtyordered, 0
                                             ) * (l.linetotalamt) / nullif(
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
                sqlmain.Append(OrderCheck + BPCheck + " AND o.DocStatus IN ('CO','CL') ");
                sqlmain.Append(@"GROUP BY
                             cb.Pic, o.DocumentNo, o.DateOrdered,o.DateOrdered, custimg.ImageExtension, cb.Name,o.AD_Client_ID
                             HAVING SUM(
                                        CASE
                                        WHEN mil.c_orderline_id IS NOT NULL
                                             AND l.qtydelivered > l.qtyinvoiced THEN
                                                coalesce(
                                                    l.qtyordered, 0
                                                ) - coalesce(
                                                    l.qtydelivered, 0)
                                                
                                        WHEN ci.c_orderline_id IS NOT NULL
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
                             END, custimg.ImageExtension, cb.Name,min.AD_Client_ID 
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
        /// This function is Used to Get Top 10 Expense Amounts
        /// </summary>
        /// <param name="ListValue">ListValue</param>
        /// <param name="ctx">Context</param>
        /// <author>VIS_427</author>
        /// <returns>List of  Get Top 10 Expense Amounts</returns>
        public List<TopExpenseAmountData> GetTop10ExpenseAmountData(Ctx ctx, string ListValue)
        {
            TopExpenseAmountData obj = new TopExpenseAmountData(); ;
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
            sql.Append($@"SELECT PeriodNo, FLOOR(PeriodNo/3) AS Quarter from C_Period p INNER JOIN C_Year y ON (p.C_Year_ID = y.c_year_ID)
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
                         WHERE acctEle.IsActive = 'Y' AND eleVal.IsActive = 'Y'", "fa", MRole.SQL_FULLYQUALIFIED, MRole.SQL_RW
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
                               (SELECT PeriodNo, FLOOR(PeriodNo / 3) AS Quarter from C_Period p INNER JOIN C_Year y ON(p.C_Year_ID = y.c_year_ID)
                               WHERE y.CalendarYears ={ VAdvantage.DataBase.GlobalVariable.TO_STRING(CurrentYear.ToString())}
                AND y.C_Calendar_ID ={ calendar_ID}
                AND TRUNC(CURRENT_DATE) between p.StartDate and p.EndDate ),
                               PeriodQuater AS
                               (SELECT PeriodNo, FLOOR(PeriodNo/ 3) AS Quarter from C_Period p INNER JOIN C_Year y ON(p.C_Year_ID = y.c_year_ID)
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
                                              (SELECT FLOOR(MAX(PeriodNo)/3) AS Quarter from C_Period p INNER JOIN C_Year y ON (p.C_Year_ID = y.c_year_ID)
                                              WHERE y.CalendarYears={VAdvantage.DataBase.GlobalVariable.TO_STRING(CurrentYear.ToString())} AND y.C_Calendar_ID={calendar_ID})
                                              ,PreviousPeriodQuater AS
                                              (SELECT PeriodNo, FLOOR(PeriodNo/3) AS Quarter from C_Period p INNER JOIN C_Year y ON (p.C_Year_ID = y.c_year_ID)
                                              WHERE y.CalendarYears={VAdvantage.DataBase.GlobalVariable.TO_STRING(CurrentYear.ToString())} AND y.C_Calendar_ID={calendar_ID})");
                }
                else
                {
                    sql.Append($@",curentPeriod AS 
                                                    (SELECT PeriodNo, FLOOR(PeriodNo/3) AS Quarter from C_Period p INNER JOIN C_Year y ON (p.C_Year_ID = y.c_year_ID)
                                                    WHERE y.CalendarYears={VAdvantage.DataBase.GlobalVariable.TO_STRING(CurrentYear.ToString())} AND y.C_Calendar_ID={calendar_ID}
                                                    AND TRUNC(CURRENT_DATE) between p.startdate and p.enddate ),
                                                    PeriodQuater AS
                                                    (SELECT PeriodNo, FLOOR(PeriodNo/3) AS Quarter from C_Period p INNER JOIN C_Year y ON (p.C_Year_ID = y.c_year_ID)
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
                                     FROM period_quarter pq
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
                    obj.Result= Util.GetValueOfString(ds.Tables[0].Rows[i]["VA113_Result"]);
                    obj.AD_Org_ID= Util.GetValueOfInt(ds.Tables[0].Rows[i]["AD_Org_ID"]);
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
        public List<dynamic> GetFinDataInsightGrid(Ctx ctx, string tableName, int pageNo, int pageSize,int AD_Org_ID)
        {
            List<dynamic> retData = new List<dynamic>();
            string[] NotIncludeCol = { "AD_Client_ID", "AD_Org_ID", "Export_ID", "CreatedBy", "UpdatedBy", "Created", "Updated", "IsActive", "DATA_OBJECT" };
            string columnNames = GetDataGridColumn(ctx, tableName, NotIncludeCol);
            dynamic obj = new ExpandoObject();
            string sql = @"SELECT " + columnNames + " FROM " + tableName.ToUpper()+" WHERE AD_Client_ID = "+ctx.GetAD_Client_ID()+" AND AD_Org_ID = "+ AD_Org_ID;
            //sql = MRole.GetDefault(ctx).AddAccessSQL(sql, tableName, MRole.SQL_FULLYQUALIFIED, MRole.SQL_RW);
            DataSet ds = DB.ExecuteDataset(sql.ToString(), null, null, pageSize, pageNo);
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
        public string DocumentNo { get; set; }
        public int recordCount { get; set; }

    }
    public class TopExpenseAmountData
    {
        public decimal ExpenseAmount { get; set; }
        public string ExpenseName { get; set; }
        public int stdPrecision { get; set; }
    }
}
