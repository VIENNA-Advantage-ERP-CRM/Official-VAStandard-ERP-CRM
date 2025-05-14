using System;
using System.Collections.Generic;
using System.Linq;
using System.Web;
using System.Web.Mvc;
using Newtonsoft.Json;
using VASLogic.Models;
using VAdvantage.Utility;

namespace VAS.Controllers
{
    public class VAS_LeadController : Controller
    {
        public JsonResult GetUserImg(string rec_ID)
        {
            VAS_LeadModel model = new VAS_LeadModel();
            string result = model.UserImage(rec_ID);
            return Json(JsonConvert.SerializeObject(result), JsonRequestBehavior.AllowGet);
        }

        public JsonResult GetThreadID(int Table_ID, int rec_ID)
        {
            VAS_LeadModel model = new VAS_LeadModel();
            var result = model.GetThreadID(Table_ID, rec_ID);
            return Json(JsonConvert.SerializeObject(result), JsonRequestBehavior.AllowGet);
        }

        [HttpPost]
        public JsonResult SetThreadID(string field)
        {
            VAS_LeadModel model = new VAS_LeadModel();
            bool result = model.UpdateThreadID(field);
            return Json(JsonConvert.SerializeObject(result), JsonRequestBehavior.AllowGet);
        }

        [HttpPost]
        public JsonResult GetPromptMsg(int Table_ID, int rec_ID)
        {
            VAS_LeadModel model = new VAS_LeadModel();
            string result = model.GetPromptMsg(Table_ID, rec_ID);
            return Json(JsonConvert.SerializeObject(result), JsonRequestBehavior.AllowGet);
        }

        [HttpPost]
        public JsonResult ConvertProspect(int Record_ID)
        {
            Ctx ctx = null;
            if (Session["ctx"] != null)
            {
                ctx = Session["ctx"] as Ctx;
            }
            VAS_LeadModel model = new VAS_LeadModel();
            string result = model.ConvertProspect(ctx, Record_ID);
            return Json(JsonConvert.SerializeObject(result), JsonRequestBehavior.AllowGet);
        }

        [HttpPost]
        public JsonResult GenerateOpprtunity(int Record_ID)
        {
            Ctx ctx = null;
            if (Session["ctx"] != null)
            {
                ctx = Session["ctx"] as Ctx;
            }
            VAS_LeadModel model = new VAS_LeadModel();
            string result = model.GenerateOpprtunity(ctx, Record_ID);
            return Json(JsonConvert.SerializeObject(result), JsonRequestBehavior.AllowGet);
        }

        [HttpPost]
        public JsonResult GenerateLines(int Record_ID, List<ProductData> ProductData)
        {
            Ctx ctx = null;
            if (Session["ctx"] != null)
            {
                ctx = Session["ctx"] as Ctx;
            }
            VAS_LeadModel model = new VAS_LeadModel();
            string result = model.GenerateLines(ctx, Record_ID, ProductData);
            return Json(JsonConvert.SerializeObject(result), JsonRequestBehavior.AllowGet);
        }
    }
}