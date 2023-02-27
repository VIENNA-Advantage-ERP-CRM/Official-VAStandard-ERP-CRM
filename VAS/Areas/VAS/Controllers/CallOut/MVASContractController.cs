﻿/********************************************************
    * Project Name   : Vienna Standard
    * Class Name     : MVASContractController
    * Chronological  : Development
    * Manjot         : 07/FEB/2023
******************************************************/
using System;
using System.Collections.Generic;
using System.Linq;
using System.Web;
using System.Web.Mvc;
using Newtonsoft.Json;
using VAdvantage.Model;
using VAdvantage.Utility;
using VIS.Models;

namespace VIS.Controllers
{
    public class MVASContractController : Controller
    {
        //Get Contract Detail
        public JsonResult GetContractDetails(string fields)
        {

            string retJSON = "";
            if (Session["ctx"] != null)
            {
                VAdvantage.Utility.Ctx ctx = Session["ctx"] as Ctx;
                MVASContractModel objContractDtl = new MVASContractModel();
                retJSON = JsonConvert.SerializeObject(objContractDtl.GetContractDetails(ctx, fields));
            }
            return Json(retJSON, JsonRequestBehavior.AllowGet);
        }

    }
}