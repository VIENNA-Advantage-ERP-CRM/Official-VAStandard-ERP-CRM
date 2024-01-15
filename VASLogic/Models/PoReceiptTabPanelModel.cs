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
        /// <param name="parentID">Take the parentID means the Order Line ID</param>
        /// <returns>returns the data</returns>
        public List<TabPanel> GetInvoiceLineData(Ctx ctx, int parentID)
        {
            List<TabPanel> tabPanels = new List<TabPanel>();
            String sql = @"SELECT
                        CASE WHEN OL.LINE > 0 THEN TO_CHAR(O.DOCUMENTNO) || '_' || TO_CHAR(OL.LINE)
                        ELSE ' ' END AS OrderDocumentLineNo,
                        CASE WHEN IL.LINE > 0 THEN TO_CHAR(I.DOCUMENTNO) || '_' || TO_CHAR(IL.LINE)
                        ELSE ' ' END AS InvoiceDocumentLineNo,
                        CASE WHEN IOL.LINE > 0 THEN TO_CHAR(INO.DOCUMENTNO) || '_' || TO_CHAR(IOL.LINE)
                        ELSE ' ' END AS InoutDocumentLineNo,
                        IL.QTYENTERED AS MPOQTY,
                        NVL(P.NAME, ' ') AS PRODUCTNAME,
                        NVL(ASI.DESCRIPTION,' ') AS MPOAttributeSetInstance
                        FROM
                         C_InvoiceLine IL
                        INNER JOIN C_Invoice I ON I.C_Invoice_ID = IL.C_Invoice_ID
                        LEFT JOIN M_Product P ON IL.M_Product_ID = P.M_Product_ID
                        LEFT JOIN M_ATTRIBUTESETINSTANCE ASI ON ASI.M_ATTRIBUTESETINSTANCE_ID = IL.M_ATTRIBUTESETINSTANCE_ID
                        LEFT JOIN C_OrderLine OL ON OL.C_OrderLine_ID = IL.C_OrderLine_ID
                        LEFT JOIN C_ORDER O ON O.C_ORDER_ID = OL.C_Order_ID
                        LEFT JOIN M_InoutLine IOL ON IOL.M_InoutLine_ID = IL.M_InoutLine_ID
                        LEFT JOIN M_Inout INO ON INO.M_Inout_ID = IOL.M_Inout_ID
                        WHERE
                            I.DOCSTATUS IN ('CO', 'CL') AND IL.C_InvoiceLine_ID = " + parentID;

            DataSet ds = DB.ExecuteDataset(sql,null,null);
            if (ds != null && ds.Tables[0].Rows.Count > 0)
            {
                for (int i = 0; i < ds.Tables[0].Rows.Count; i++)
                {
                    TabPanel obj = new TabPanel();

                    obj.OrderDocumentNo = Util.GetValueOfString(ds.Tables[0].Rows[i]["OrderDocumentLineNo"]);
                    obj.INVOICEDOCUMENTNO = Util.GetValueOfString(ds.Tables[0].Rows[i]["InvoiceDocumentLineNo"]);
                    obj.GRNDOCUMENTNO = Util.GetValueOfString(ds.Tables[0].Rows[i]["InoutDocumentLineNo"]);
                    //obj.OrderLINENO = Util.GetValueOfInt(ds.Tables[0].Rows[i]["OrderLINENO"]);
                    obj.MPOQTY = Util.GetValueOfInt(ds.Tables[0].Rows[i]["MPOQTY"]);
                    //obj.INVOICEDOCUMENTNO = Util.GetValueOfString(ds.Tables[0].Rows[i]["INVOICEDOCUMENTNO"]);
                    obj.PRODUCTNAME = Util.GetValueOfString(ds.Tables[0].Rows[i]["PRODUCTNAME"]);
                    obj.MPOAttributeSetInstance = Util.GetValueOfString(ds.Tables[0].Rows[i]["MPOAttributeSetInstance"]);
                    //obj.INVOICELINENO = Util.GetValueOfInt(ds.Tables[0].Rows[i]["INVOICELINENO"]);
                    tabPanels.Add(obj);
                }
            }
            return tabPanels;
        }
    }
    public class TabPanel
    {
        public string OrderDocumentNo { get; set; }

        public string GRNDOCUMENTNO { get; set; }

        public int MPOQTY { get; set; }

        public string INVOICEDOCUMENTNO { get; set; }

        public string PRODUCTNAME { get; set; }

        public string MPOAttributeSetInstance { get; set; }

    }
}
