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
        /// This Method is used to return the column ID Of column (LegalEntityOrg)
        /// </summary>
        /// <param name="ColumnName">Name of the Column</param>
        /// <returns>Column ID</returns>
        /// <author>VIS_427 BugId</author>
        public JsonResult GetColumnID(string ColumnName)
        {
            string retJSON = "";
            int Column_ID = 0;
            if (Session["ctx"] != null)
            {
                Ctx ctx = Session["ctx"] as Ctx;
                string sql = @"SELECT AD_Column_ID FROM AD_Column WHERE ColumnName ='" + ColumnName + "'";
                Column_ID = Util.GetValueOfInt(CoreLibrary.DataBase.DB.ExecuteScalar(sql));
                retJSON = JsonConvert.SerializeObject(Column_ID);
            }
            return Json(retJSON, JsonRequestBehavior.AllowGet);
        }

        public ActionResult LoadGridData(int AD_Cleint_ID, int AD_Org_ID, int C_BPartner_ID, int S_Resource_ID, int TimExpenSeDoc, int C_Project_ID, int R_Request_ID, int C_Task_ID, string FromDate, string toDate)
        {
            Ctx ctx = Session["ctx"] as Ctx;
            VAS_TimeSheetInvoice invoiceTimeSheet = new VAS_TimeSheetInvoice();
            return Json(JsonConvert.SerializeObject(invoiceTimeSheet.LoadGridData(ctx, AD_Cleint_ID, AD_Org_ID, C_BPartner_ID, S_Resource_ID, TimExpenSeDoc, C_Project_ID, R_Request_ID, C_Task_ID, FromDate, toDate)), JsonRequestBehavior.AllowGet);
        }
        public ActionResult GenerateInvoice(string DataTobeInvoice, int AD_Cleint_ID, int AD_Org_ID)
        {
            dynamic dataTobeInvoice = JsonConvert.DeserializeObject<List<ExpandoObject>>(DataTobeInvoice);
            Ctx ctx = Session["ctx"] as Ctx;
            VAS_TimeSheetInvoice invoiceTimeSheet = new VAS_TimeSheetInvoice();
            string _Paydata = invoiceTimeSheet.GenerateInvoice(ctx, dataTobeInvoice, AD_Cleint_ID, AD_Org_ID);
            return Json(JsonConvert.SerializeObject(_Paydata), JsonRequestBehavior.AllowGet);
        }
    }
}