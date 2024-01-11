﻿
using Newtonsoft.Json;
 
using System;

using System.Collections.Generic;

using System.Linq;

using System.Web;

using System.Web.Mvc;

using VAdvantage.Utility;

using VASLogic.Models;

using static VASLogic.Models.VASAttachUserToBP;
 
namespace VAS.Areas.VAS.Controllers

{
   
  /*******************************************************
         * Module Name    : VAS_Standard
         * Purpose        : To get User and update user
         * Chronological Development
         * Employee code : VAI050
        * Created Date:  19-dec-2023
         * Updated Date:  

        ******************************************************/

    public class VASAttachUserToBPController : Controller
    {
        

        // GET: VAS/VASAttachUserToBP

        public ActionResult Index()

        {

            return View();
        }

        /// <summary>
        /// To get User List
        /// </summary>
        /// <param name="searchKey"></param>
        /// <returns>returns User list</returns>

        public JsonResult GetUserList(string searchKey)
        {
            string retJSON = "";
            if (Session["ctx"] != null)
            {
                Ctx ctx = Session["ctx"] as Ctx;
                VASAttachUserToBP obj = new VASAttachUserToBP();
                List<Userdetail> result = obj.GetUserList(ctx, searchKey);
                retJSON = JsonConvert.SerializeObject(result);
            }
            return Json(retJSON, JsonRequestBehavior.AllowGet);
        }

        /// <summary>
        /// To update user
        /// </summary>
        /// <param name="userNames"></param>
        /// <param name="userIds"></param>
        /// <param name="c_BPartnerID"></param>
        /// <returns>return list of user which is not updated</returns>

        public JsonResult UpdateUser(List<String> userNames, List<int> userIds, int c_BPartnerID)
        {
            string retJSON = "";
            if (Session["ctx"] != null)
            {
                Ctx ctx = Session["ctx"] as Ctx;
                VASAttachUserToBP obj = new VASAttachUserToBP();
                retJSON = JsonConvert.SerializeObject(obj.UpdateUser(ctx, userNames, userIds, c_BPartnerID));
            }
            return Json(retJSON, JsonRequestBehavior.AllowGet);
        }
   }

}