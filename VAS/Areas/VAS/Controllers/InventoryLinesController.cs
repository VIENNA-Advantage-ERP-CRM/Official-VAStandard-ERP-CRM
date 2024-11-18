/********************************************************
 * Module Name    : VIS
 * Purpose        : Model class Inventory line form
 * Class Used     : 
 * Chronological Development
 * Megha Rana    20-sept-24
 ******************************************************
 **/
using Newtonsoft.Json;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Web;
using System.Web.Mvc;
using VAdvantage.Model;
using VAdvantage.Utility;
using VAS.Models;

namespace VAS.Controllers
{
    public class InventoryLinesController : Controller
    {

        public ActionResult Index()
        {
            return View();
        }
        /// <summary>
        /// VIS0336-using this method for fetching the users for create inventory line form
        /// </summary>
        /// <param name="SearchKey"></param>
        /// <returns>users</returns>
        public JsonResult GetUsers(string SearchKey)
        {
            List<KeyNamePair> result = null;
            if (Session["ctx"] != null)
            {
                var ctx = Session["ctx"] as Ctx;
                InventoryLinesModel obj = new InventoryLinesModel();
                result = obj.GetUsers(ctx,SearchKey);
            }
            return Json(JsonConvert.SerializeObject(result), JsonRequestBehavior.AllowGet);
        }
        /// <summary>
        /// VIS0336-using this method for fetching the cart names and detail for inventory form
        /// </summary>
        /// <param name="CartName"></param>
        /// <param name="UserId"></param>
        /// <param name="FromDate"></param>
        /// <param name="ToDate"></param>
        /// <param name="RefNo"></param>
        /// <returns>carts</returns>
        public JsonResult GetIventoryCartData(string CartName, string UserId,string FromDate,string ToDate, string RefNo,int windowID, int RecordId,string WindowName,int ToWarehouse,int DTDSrcWarehouse,int BPartnerId)
        {
            List<Dictionary<string, object>> result = null;
            if (Session["ctx"] != null)

            {
                Ctx ctx = Session["ctx"] as Ctx;
                InventoryLinesModel obj = new InventoryLinesModel();
                result = obj.GetIventoryCartData(ctx,CartName, UserId, FromDate, ToDate, RefNo, windowID, RecordId, WindowName, ToWarehouse, DTDSrcWarehouse, BPartnerId);
            }
            return Json(JsonConvert.SerializeObject(result), JsonRequestBehavior.AllowGet);
        }
        /// <summary>
        /// VIS0336-using this method for fetching the lines against the cart 
        /// </summary>
        /// <param name="CartId"></param>
        /// <returns></returns>
        public JsonResult GetIventoryCartLines(int CartId , string ScreenName, int RecordId)
        {
            List<Dictionary<string, object>> result = null;
            if (Session["ctx"] != null)

            {
                Ctx ctx = Session["ctx"] as Ctx;
                InventoryLinesModel obj = new InventoryLinesModel();
                result = obj.GetIventoryCartLines(ctx,CartId,  ScreenName,  RecordId);
            }
            return Json(JsonConvert.SerializeObject(result), JsonRequestBehavior.AllowGet);
        }
        /// <summary>
        /// VIS0336- for saving the lines in inventory line tab
        /// </summary>
        /// <param name="InventoryId"></param>
        /// <param name="lstScanDetail"></param>
        /// <param name="IsUpdateTrue"></param>
        /// <returns></returns>
        [HttpPost]
        public JsonResult SaveTransactions(int TransactionID, string lstScanDetail,bool IsUpdateTrue,int windowID, string RefNo, int FromLocatorId, int ToLocatorId)
        {
            VAdvantage.Utility.Ctx ctx = Session["ctx"] as Ctx;
            List<Inventoryline> lstInentoryLines = JsonConvert.DeserializeObject<List<Inventoryline>>(lstScanDetail); 
            InventoryLinesModel objInventoryCount = new InventoryLinesModel();
            return Json(JsonConvert.SerializeObject(objInventoryCount.SaveTransactions(ctx, TransactionID, lstInentoryLines, IsUpdateTrue, windowID, RefNo, FromLocatorId, ToLocatorId)), JsonRequestBehavior.AllowGet);
        }
    }
}