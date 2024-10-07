/*******************************************************
       * Module Name    : VAS
       * Purpose        : Tab Panel For AP Matched PO and MatchedReceipt
       * chronological  : Development
       * Created Date   : 12 January 2024
       * Created by     : VAI066
      ******************************************************/
using Newtonsoft.Json;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Web;
using System.Web.Mvc;
using VAdvantage.Utility;
using VASLogic.Models;

namespace VAS.Areas.VAS.Controllers
{
    public class PoReceiptController : Controller
    {
        // GET: VAS/InvoiceTabPanel
        [Authorize]
        public ActionResult Index()
        {
            return View();
        }
        /// <summary>
        /// Used to get Data for Tab Panel
        /// </summary>
        /// <param name="parentID">parentID will be Invoice Line ID</param>
        /// <Author>VAI066 DevopsID 4216</Author>
        /// <returns>return the data needed for tab panel</returns>
        public JsonResult GetData(int parentID)
        {
            string retJSON = "";
            if (Session["ctx"] != null)
            {
                Ctx ctx = Session["ctx"] as Ctx;
                PoReceiptTabPanelModel obj = new PoReceiptTabPanelModel();
                List<TabPanel> result = obj.GetInvoiceLineData(ctx, parentID);
                retJSON = JsonConvert.SerializeObject(result);
            }
            return Json(retJSON, JsonRequestBehavior.AllowGet);
        }

        /// <summary>
        /// 16/1/2024 This function is Used to Get the Invoice tax data 
        /// </summary>
        /// <param name="InvoiceId">Invoice ID</param>
        /// <Author> VIS_427 Devops ID: 4261</Author>
        /// <returns>returns the Invoice tax data</returns>
        public JsonResult GetTaxData(int InvoiceId)
        {
            string retJSON = "";
            if (Session["ctx"] != null)
            {
                Ctx ctx = Session["ctx"] as Ctx;
                PoReceiptTabPanelModel obj = new PoReceiptTabPanelModel();
                List<TaxTabPanel> result = obj.GetInvoiceTaxData(ctx, InvoiceId);
                retJSON = JsonConvert.SerializeObject(result);
            }
            return Json(retJSON, JsonRequestBehavior.AllowGet);
        }

        /// <summary>
        /// 16/2/2024 This function is Used to Get the Order tax data 
        /// </summary>
        /// <param name="OrderId">Invoice ID</param>
        /// <Author> VAI051:- Devops ID: </Author>
        /// <returns>returns the Order tax data</returns>
        public JsonResult GetPurchaseOrderTaxData(int OrderId)
        {
            string retJSON = "";
            if (Session["ctx"] != null)
            {
                Ctx ctx = Session["ctx"] as Ctx;
                PoReceiptTabPanelModel obj = new PoReceiptTabPanelModel();
                List<PurchaseOrderTabPanel> result = obj.GetPurchaseOrderTaxData(ctx, OrderId);
                retJSON = JsonConvert.SerializeObject(result);
            }
            return Json(retJSON, JsonRequestBehavior.AllowGet);
        }

        /// <summary>
        /// 20/2/2024 This function is Used to Get the Order tax data 
        /// </summary>
        /// <param name="OrderLineId">Invoice ID</param>
        /// <Author> VAI051:- Devops ID: </Author>
        /// <returns>returns the LineHistory Data</returns>
        public JsonResult GetLineHistoryData(int OrderLineID)
        {
            string retJSON = "";
            if (Session["ctx"] != null)
            {
                Ctx ctx = Session["ctx"] as Ctx;
                PoReceiptTabPanelModel obj = new PoReceiptTabPanelModel();
                List<LineHistoryTabPanel> result = obj.GetLineHistoryTabPanel(ctx, OrderLineID);
                retJSON = JsonConvert.SerializeObject(result);
            }
            return Json(retJSON, JsonRequestBehavior.AllowGet);
        }

        /// <summary>
        /// VAI050-Get Purchase Order Lines
        /// </summary>
        /// <param name="OrderID"></param>
        /// <returns></returns>

        public JsonResult GetPOLineData(int OrderID)
        {
            string retJSON = "";
            if (Session["ctx"] != null)
            {
                Ctx ctx = Session["ctx"] as Ctx;
                PoReceiptTabPanelModel obj = new PoReceiptTabPanelModel();
                List<dynamic> result = obj.GetPOLineData(ctx, OrderID);
                retJSON = JsonConvert.SerializeObject(result);
            }
            return Json(retJSON, JsonRequestBehavior.AllowGet);
        }
        /// <summary>
        /// VIS-383: 27/07/24:- Get invoice detail based on invoice line
        /// </summary>
        /// <param name="InvoiceLineId">Invoice LineID</param>
        /// <param name="AD_WindowID">Window ID</param>
        /// <returns>Invoice details</returns>
        public JsonResult GetInvoiceLineReport(int InvoiceId, int AD_WindowID)
        {
            string retJSON = "";
            if (Session["ctx"] != null)
            {
                Ctx ctx = Session["ctx"] as Ctx;
                PoReceiptTabPanelModel obj = new PoReceiptTabPanelModel();
                retJSON = obj.GetInvoiceLineReport(ctx, InvoiceId, AD_WindowID);
            }
            return Json(JsonConvert.SerializeObject(retJSON), JsonRequestBehavior.AllowGet);
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
        public JsonResult GetUnAllocatedPayData(int C_BPartner_ID,string IsSoTrx,int AD_Org_ID)
        {
            string retJSON = "";
            if (Session["ctx"] != null)
            {
                Ctx ctx = Session["ctx"] as Ctx;
                PoReceiptTabPanelModel obj = new PoReceiptTabPanelModel();
                List<UnAllocatedPayTabPanel> result = obj.GetUnAllocatedPayData(ctx, C_BPartner_ID, IsSoTrx, AD_Org_ID);
                retJSON = JsonConvert.SerializeObject(result);
            }
            return Json(retJSON, JsonRequestBehavior.AllowGet);
        }

        /// <summary>
        /// This function is Used to Get the Order Total Summary data 
        /// </summary>
        /// <param name="OrderId">Order ID</param>
        /// <returns>returns the Order tax data</returns>
        public JsonResult GetOrderSummary(int OrderId)
        {
            string retJSON = "";
            if (Session["ctx"] != null)
            {
                Ctx ctx = Session["ctx"] as Ctx;
                PoReceiptTabPanelModel obj = new PoReceiptTabPanelModel();
                List<dynamic> result = obj.GetOrderSummary(ctx, OrderId);
                retJSON = JsonConvert.SerializeObject(result);
            }
            return Json(retJSON, JsonRequestBehavior.AllowGet);
        }
        /// <summary>
        /// This function is Used to Get the AR Invoice Data for widget
        /// </summary>
        /// <param name="WidgetId">WidgetId</param>
        /// <author>VIS_427</author>
        /// <returns>List of ar invoice data</returns>
        public JsonResult GetARInvSchData(bool ISOtrx)
        {
            string retJSON = "";
            if (Session["ctx"] != null)
            {
                Ctx ctx = Session["ctx"] as Ctx;
                PoReceiptTabPanelModel obj = new PoReceiptTabPanelModel();
                List<ARInvWidgData> result = obj.GetARInvSchData(ctx, ISOtrx);
                retJSON = JsonConvert.SerializeObject(result);
            }
            return Json(retJSON, JsonRequestBehavior.AllowGet);
        }
        /// <summary>
        /// This function is Used to Get the ar/ap invoice data of top five business partners
        /// </summary>
        /// <param name="ISOtrx">ISOtrx</param>
        /// <param name="ListValue">ListValue</param>
        /// <author>VIS_427</author>
        /// <returns>List of ar/ap invoice data of top five business partners</returns>
        public JsonResult GetInvTotalGrandData(bool ISOtrx,string ListValue)
        {
            string retJSON = "";
            if (Session["ctx"] != null)
            {
                Ctx ctx = Session["ctx"] as Ctx;
                PoReceiptTabPanelModel obj = new PoReceiptTabPanelModel();
                List<InvGrandTotalData> result = obj.GetInvTotalGrandData(ctx, ISOtrx, ListValue);
                retJSON = JsonConvert.SerializeObject(result);
            }
            return Json(retJSON, JsonRequestBehavior.AllowGet);
        }
        // <summary>
        /// This function is Used to Amount which are in diffenernt states from AP/AR Screens
        /// </summary>
        /// <param name="ISOtrx">ISOtrx</param>
        /// <param name="ctx">Context</param>
        /// <author>VIS_427</author>
        /// <returns>List of Amount which are in diffenernt states from AP/AR Screens</returns>
        public JsonResult GetPurchaseStateDetail(bool ISOtrx)
        {
            string retJSON = "";
            if (Session["ctx"] != null)
            {
                Ctx ctx = Session["ctx"] as Ctx;
                PoReceiptTabPanelModel obj = new PoReceiptTabPanelModel();
                List<PurchaseStateDetail> result = obj.GetPurchaseStateDetail(ctx, ISOtrx);
                retJSON = JsonConvert.SerializeObject(result);
            }
            return Json(retJSON, JsonRequestBehavior.AllowGet);
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
        public JsonResult GetExpectedInvoiceData(bool ISOtrx,int pageNo,int pageSize,string ListValue)
        {
            string retJSON = "";
            if (Session["ctx"] != null)
            {
                Ctx ctx = Session["ctx"] as Ctx;
                PoReceiptTabPanelModel obj = new PoReceiptTabPanelModel();
                List<ExpectedInvoice> result = obj.GetExpectedInvoiceData(ctx, ISOtrx, pageNo, pageSize, ListValue);
                retJSON = JsonConvert.SerializeObject(result);
            }
            return Json(retJSON, JsonRequestBehavior.AllowGet);
        }
        /// <summary>
        /// This Method is used to return the refrence id 
        /// </summary>
        /// <param name="ColumnData"></param>
        /// <returns>Dictionary with column name and refrence id</returns>
        /// <author>VIS_427 </author>
        public JsonResult GetColumnID( string refernceName)
        {
            Ctx ctx = Session["ctx"] as Ctx;
            PoReceiptTabPanelModel refernceId = new PoReceiptTabPanelModel();
            Dictionary<string, int> columnData = refernceId.GetColumnIds(ctx, refernceName);
            return Json(JsonConvert.SerializeObject(columnData), JsonRequestBehavior.AllowGet);
        }
        /// <summary>
        /// This function is Used to Get Top 10 Expense Amounts
        /// </summary>
        /// <param name="ListValue">ListValue</param>
        /// <author>VIS_427</author>
        /// <returns>List of  Get Top 10 Expense Amounts</returns>
        public JsonResult GetTop10ExpenseAmountData(string ListValue)
        {
            Ctx ctx = Session["ctx"] as Ctx;
            PoReceiptTabPanelModel yearBasedExpenseData = new PoReceiptTabPanelModel();
            List<TopExpenseAmountData> ExpenseData = yearBasedExpenseData.GetTop10ExpenseAmountData(ctx, ListValue);
            return Json(JsonConvert.SerializeObject(ExpenseData), JsonRequestBehavior.AllowGet);
        }
        /// <summary>
        /// This function is Used to Get the Finance Instigh Data
        /// </summary>
        /// <param name="ctx">Context</param>
        /// <param name="ListValue">ListValue/param>
        /// <returns>returns Finance Instigh Data</returns>
        /// <author>VIS_427</author>
        public JsonResult GetFinInsightsData(string ListValue)
        {
            Ctx ctx = Session["ctx"] as Ctx;
            PoReceiptTabPanelModel yearBasedExpenseData = new PoReceiptTabPanelModel();
            List<dynamic> ExpenseData = yearBasedExpenseData.GetFinInsightsData(ctx, ListValue);
            return Json(JsonConvert.SerializeObject(ExpenseData), JsonRequestBehavior.AllowGet);
        }
        /// <summary>
        /// This Function is use to get the data in grid
        /// </summary>
        /// <param name="tableName">tableName</param>
        /// <param name="pageNo">pageNo</param>
        /// <param name="pageSize">pageSize</param>
        /// <returns>returns the data in grid</returns>
        /// <author>VIS_427</author>
        public JsonResult GetFinDataInsightGrid(string tableName,int pageNo,int pageSize,int AD_Org_ID)
        {
            Ctx ctx = Session["ctx"] as Ctx;
            PoReceiptTabPanelModel yearBasedExpenseData = new PoReceiptTabPanelModel();
            List<dynamic> ExpenseData = yearBasedExpenseData.GetFinDataInsightGrid(ctx, tableName, pageNo, pageSize, AD_Org_ID);
            return Json(JsonConvert.SerializeObject(ExpenseData), JsonRequestBehavior.AllowGet);
        }

        /// <summary>
        /// This function is used to get Data of Income and Revenue
        /// </summary>
        /// <param name="ListValue">List Value for data filterartion</param>
        /// <returns>Data</returns>
        /// <author>VIS_0045</author>
        public JsonResult GetIncomeAndExpenseData(string ListValue)
        {
            Ctx ctx = Session["ctx"] as Ctx;
            PoReceiptTabPanelModel objIncomeAndExpenseData = new PoReceiptTabPanelModel();
            VAS_ExpenseRevenue lstIncomeAndExpenseData = objIncomeAndExpenseData.GetExpenseRevenueDetails(ctx, ListValue);
            return Json(JsonConvert.SerializeObject(lstIncomeAndExpenseData), JsonRequestBehavior.AllowGet);
        }
    }
}