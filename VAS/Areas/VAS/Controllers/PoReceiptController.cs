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
        public JsonResult GetARInvSchData(string WidgetId)
        {
            string retJSON = "";
            if (Session["ctx"] != null)
            {
                Ctx ctx = Session["ctx"] as Ctx;
                PoReceiptTabPanelModel obj = new PoReceiptTabPanelModel();
                List<ARInvWidgData> result = obj.GetARInvSchData(ctx, WidgetId);
                retJSON = JsonConvert.SerializeObject(result);
            }
            return Json(retJSON, JsonRequestBehavior.AllowGet);
        }
    }
}