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
        public string GetInvoiceLineReport(Ctx ctx, int InvoiceLineId, int AD_WindowID)
        {
            string path = "";
            //Get invoice table id based on table name
            int AD_Table_ID = Util.GetValueOfInt(DB.ExecuteScalar(@"SELECT ad_table_id FROM  ad_table WHERE tablename = 'C_Invoice'"));
            //Get invoice id based on invoice line id
            int InvoiceId = Util.GetValueOfInt(DB.ExecuteScalar(@"SELECT C_Invoice_ID FROM C_InvoiceLine WHERE C_InvoiceLine_ID=" + InvoiceLineId));
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
        public List<UnAllocatedPayTabPanel> GetUnAllocatedPayData(Ctx ctx, int C_BPartner_ID, string IsSoTrx,int AD_Org_ID)
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
                          AND p.C_BPartner_ID=" + C_BPartner_ID +" AND p.AD_Org_ID="+ AD_Org_ID);
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
                int AD_Window_ID=Util.GetValueOfInt(DB.ExecuteScalar("SELECT AD_Window_ID FROM AD_Window WHERE Name='Payment'",null,null));
                for (int i = 0; i < ds.Tables[0].Rows.Count; i++)
                {
                    UnAllocatedPayTabPanel obj = new UnAllocatedPayTabPanel();
                    obj.AD_Org_ID = Util.GetValueOfInt(ds.Tables[0].Rows[i]["AD_Org_ID"]);
                    obj.C_Payment_ID = Util.GetValueOfInt(ds.Tables[0].Rows[i]["C_Payment_ID"]);
                    obj.PayAmt = Util.GetValueOfDecimal(ds.Tables[0].Rows[i]["PayAmt"])- Math.Abs(Util.GetValueOfDecimal(ds.Tables[0].Rows[i]["AllocatedAmt"]));
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
}
