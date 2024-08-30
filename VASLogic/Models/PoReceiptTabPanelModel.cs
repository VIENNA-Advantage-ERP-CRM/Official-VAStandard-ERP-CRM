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
                if (TotalAmt > 0)
                {
                    obj = new ARInvWidgData();
                    obj.arTotalAmtWidget = new List<ArTotalAmtWidget>();
                    ArTotalAmtWidget objAmt = new ArTotalAmtWidget();
                    objAmt.totalAmt = TotalAmt;
                    obj.arTotalAmtWidget.Add(objAmt);
                }
                ARInvWidgData.Add(obj);
            }
            return ARInvWidgData;
        }

        /// <summary>
        /// This function is Used to Get the ar/ap invoice data of top five business partners
        /// </summary>
        /// <param name="ISOtrx">ISOtrx</param>
        /// <param name="ctx">Context</param>
        /// <author>VIS_427</author>
        /// <returns>List of ar/ap invoice data of top five business partners</returns>
        public List<InvGrandTotalData> GetInvTotalGrandData(Ctx ctx, bool ISOtrx)
        {
            InvGrandTotalData obj = new InvGrandTotalData(); ;
            StringBuilder sql = new StringBuilder();
            List<InvGrandTotalData> invGrandTotalData = new List<InvGrandTotalData>();
            var C_Currency_ID = ctx.GetContextAsInt("$C_Currency_ID");

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
                             LEFT OUTER JOIN AD_Image custimg ON (custimg.AD_Image_ID = cb.Pic)
                             WHERE cd.DocBaseType IN ('ARI', 'ARC','API','APC') AND ci.DocStatus IN ('CO','CL')", "ci", MRole.SQL_FULLYQUALIFIED, MRole.SQL_RW
                     )})
                     SELECT
                         Name,
                         Pic,
                         custImgExtension,
                         min(DateInvoiced) AS minDateInvoiced,");
            if (ISOtrx)
            {
                sql.Append(@"NVL((SUM(CASE WHEN DocBaseType = 'ARI' THEN DueAmt ELSE 0 END) -
                             SUM(CASE WHEN DocBaseType = 'ARC' THEN DueAmt ELSE 0 END)),0) AS SumAmount");
            }
            else
            {
                sql.Append(@"NVL((SUM(CASE WHEN DocBaseType = 'API' THEN DueAmt ELSE 0 END) -
                          SUM(CASE WHEN DocBaseType = 'APC' THEN DueAmt ELSE 0 END)),0) AS SumAmount");
            }
            sql.Append(@" FROM
                         InvoiceData
                     Group by Name,Pic,custImgExtension
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
                 WHERE CURRENT_DATE BETWEEN cp.StartDate AND cp.EndDate
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
             WHERE ci.DocStatus IN ('CO', 'CL') AND cs.VA009_IsPaid='N' AND ci.IsInDispute = 'Y'", "ci", MRole.SQL_FULLYQUALIFIED, MRole.SQL_RW)); ;

            // Query for 'Hold'
            sql.Append(" UNION ALL ");
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
}
