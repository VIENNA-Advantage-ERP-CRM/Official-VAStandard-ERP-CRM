/***********************************************************************************
 * Module Name    : VAS
 * Purpose        : This class Created to generate AR invoice against Time recording
 * chronological  : Development
 * Created Date   : 07 May 2024
 * Created by     : VIS_427
***********************************************************************************/

using Newtonsoft.Json;
using System;
using System.Collections.Generic;
using System.Dynamic;
using System.Linq;
using System.Web;
using System.Web.Mvc;
using VAdvantage.Utility;
using VASLogic.Models;

namespace VAS.Areas.VAS.Controllers
{
    public class VAS_TimeSheetInvoicesController : Controller
    {

        /// <summary>
        /// This Method is used to return the column id 
        /// </summary>
        /// <param name="ColumnData">Data of the Column</param>
        /// <returns>Dictionary with column name and column id</returns>
        /// <author>Devops Id: 5719 VIS_427</author>
        public JsonResult GetColumnID(string ColumnData)
        {
            dynamic columnDataArray = JsonConvert.DeserializeObject<dynamic[]>(ColumnData);
            Ctx ctx = Session["ctx"] as Ctx;
            VAS_TimeSheetInvoice invoiceTimeSheet = new VAS_TimeSheetInvoice();
            Dictionary<string, int> columnData = invoiceTimeSheet.GetColumnIds(ctx, columnDataArray);
            return Json(JsonConvert.SerializeObject(columnData), JsonRequestBehavior.AllowGet);
        }

        /// <summary>
        ///This Function returns the  Data for Time Recording in order to create AR Invoice 
        /// </summary>
        /// <param name="ctx">context</param>
        /// <param name="AD_Client_ID">AD_Client_ID</param>
        /// <param name="AD_Org_ID">AD_Org_ID</param>
        /// <param name="C_BPartner_ID">C_BPartner_ID</param>
        /// <param name="C_Project_ID">C_Project_ID</param>
        /// <param name="C_Task_ID">C_Task_ID</param>
        /// <param name="FromDate">FromDate</param>
        /// <param name="pageNo">pageNo</param>
        /// <param name="pageSize">pageSize</param>
        /// <param name="R_Request_ID">R_Request_ID</param>
        /// <param name="S_Resource_ID">S_Resource_ID</param>
        /// <param name="TimExpenSeDoc">TimExpenSeDoc</param>
        /// <param name="toDate">toDate</param>
        /// /// <param name="TaskType">Task Type</param>
        /// <returns>List of data For time recording of task</returns>
        /// <author> Devops Id: 5719 VIS_427</author>
        public ActionResult LoadGridData(int AD_Client_ID, int AD_Org_ID, string C_BPartner_ID, string S_Resource_ID, string TimExpenSeDoc, string C_Project_ID, string R_Request_ID, string C_Task_ID, string FromDate, string toDate, string TaskType, int pageNo, int pageSize)
        {
            Ctx ctx = Session["ctx"] as Ctx;
            VAS_TimeSheetInvoice invoiceTimeSheet = new VAS_TimeSheetInvoice();
            return Json(JsonConvert.SerializeObject(invoiceTimeSheet.LoadGridData(ctx, AD_Client_ID, AD_Org_ID, C_BPartner_ID, S_Resource_ID, TimExpenSeDoc, C_Project_ID, R_Request_ID, C_Task_ID, FromDate, toDate, TaskType, pageNo, pageSize)), JsonRequestBehavior.AllowGet);
        }
        /// <summary>
        ///This Functionis used for generating invoice
        /// </summary>
        /// <param name="ct">context</param>
        /// <param name="AD_Client_ID">AD_Client_ID</param>
        /// <param name="AD_Org_ID">AD_Org_ID</param>
        /// <param name="DataTobeInvoice">object of data which is to be invoice</param>
        /// <returns>message</returns>
        /// <author>Devops Id: 5719 VIS_427</author>
        public ActionResult GenerateInvoice(string DataTobeInvoice, int AD_Client_ID, int AD_Org_ID)
        {
            dynamic dataTobeInvoice = JsonConvert.DeserializeObject<List<ExpandoObject>>(DataTobeInvoice);
            Ctx ctx = Session["ctx"] as Ctx;
            VAS_TimeSheetInvoice invoiceTimeSheet = new VAS_TimeSheetInvoice();
            string _Paydata = invoiceTimeSheet.GenerateInvoice(ctx, dataTobeInvoice, AD_Client_ID, AD_Org_ID);
            var invoiceData = Json(JsonConvert.SerializeObject(_Paydata), JsonRequestBehavior.AllowGet);
            invoiceData.MaxJsonLength = int.MaxValue;
            return invoiceData;
        }

    }
}
