using Newtonsoft.Json;
using System;
using System.Collections.Generic;
using System.Dynamic;
using System.Linq;
using System.Web;
using System.Web.Mvc;
using VAdvantage.Utility;
using VASLogic.Models;
using VAdvantage.DataBase;

namespace VAS.Areas.VAS.Controllers
{
    public class VAS_TimeSheetInvoicesController : Controller
    {

        /// <summary>
        /// This Method is used to return the column id 
        /// </summary>
        /// <param name="ColumnData">Data of the Column</param>
        /// <returns>Column ID</returns>
        /// <author>VIS_427 </author>
        public JsonResult GetColumnID(string ColumnData)
        {
            int Column_ID = 0;
            List<ColumnInfo> columnInfoList = new List<ColumnInfo>();
            if (Session["ctx"] != null)
            {
                Ctx ctx = Session["ctx"] as Ctx;

                // Deserialize the JSON string to an array of objects
                dynamic columnDataArray = JsonConvert.DeserializeObject<dynamic[]>(ColumnData);

                // Iterate through the array
                foreach (var item in columnDataArray)
                {
                    // Extract column name and table name
                    string ColumnName = item.ColumnName;
                    string TableName = item.TableName;

                    // Construct SQL query to retrieve AD_Column_ID
                    string sql = @"SELECT AD_Column_ID FROM AD_Column 
                               WHERE ColumnName ='" + ColumnName + @"' 
                               AND AD_Table_ID = (SELECT AD_Table_ID FROM AD_Table WHERE TableName='" + TableName + @"')";
                    Column_ID = Util.GetValueOfInt(DB.ExecuteScalar(sql));
                    columnInfoList.Add(new ColumnInfo { ColumnName = ColumnName, ColumnID = Column_ID });
                }
            }

            // Return the JSON result
            return Json(columnInfoList, JsonRequestBehavior.AllowGet);
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
        /// <returns>List of data For time recording of task</returns>
        /// <author>VIS_427</author>
        public ActionResult LoadGridData(int AD_Client_ID, int AD_Org_ID, string C_BPartner_ID, string S_Resource_ID, string TimExpenSeDoc, string C_Project_ID, string R_Request_ID, string C_Task_ID, string FromDate, string toDate,int pageNo,int pageSize)
        {
            Ctx ctx = Session["ctx"] as Ctx;
            VAS_TimeSheetInvoice invoiceTimeSheet = new VAS_TimeSheetInvoice();
            return Json(JsonConvert.SerializeObject(invoiceTimeSheet.LoadGridData(ctx, AD_Client_ID, AD_Org_ID, C_BPartner_ID, S_Resource_ID, TimExpenSeDoc, C_Project_ID, R_Request_ID, C_Task_ID, FromDate, toDate, pageNo, pageSize)), JsonRequestBehavior.AllowGet);
        }
        /// <summary>
        ///This Functionis used for generating invoice
        /// </summary>
        /// <param name="ct">context</param>
        /// <param name="AD_Client_ID">AD_Client_ID</param>
        /// <param name="AD_Org_ID">AD_Org_ID</param>
        /// <param name="DataTobeInvoice">object of data which is to be invoice</param>
        /// <returns>message</returns>
        /// <author>VIS_427</author>
        public ActionResult GenerateInvoice(string DataTobeInvoice, int AD_Client_ID, int AD_Org_ID)
        {
            dynamic dataTobeInvoice = JsonConvert.DeserializeObject<List<ExpandoObject>>(DataTobeInvoice);
            Ctx ctx = Session["ctx"] as Ctx;
            VAS_TimeSheetInvoice invoiceTimeSheet = new VAS_TimeSheetInvoice();
            string _Paydata = invoiceTimeSheet.GenerateInvoice(ctx, dataTobeInvoice, AD_Client_ID, AD_Org_ID);
            return Json(JsonConvert.SerializeObject(_Paydata), JsonRequestBehavior.AllowGet);
        }

    }
    //Defined class with properties to store Column information
    public class ColumnInfo
    {
        public string ColumnName { get; set; }
        public int ColumnID { get; set; }
    }
}