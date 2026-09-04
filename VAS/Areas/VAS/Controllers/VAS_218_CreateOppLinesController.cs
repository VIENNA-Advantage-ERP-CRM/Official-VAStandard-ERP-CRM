/******************************************************
 * Module Name    : CRM Extension VAS_218
 * Purpose        : Create Opportunity Lines Bottom Panel — controller
 * Employee Code  : VAI154
 * Date           : 21-Aug-2026
 ******************************************************/

using Newtonsoft.Json;
using System.Collections.Generic;
using System.Web.Mvc;
using VAdvantage.Utility;
using VASLogic.Models;
using VIS.Filters;

namespace VAS.Controllers
{
    /// <summary>
    /// AJAX endpoints for the VAS_218_CreateOppLines tab panel. Each action reads the
    /// session Ctx, delegates to <see cref="VAS_218_CreateOppLinesModel"/> and returns
    /// the serialized result. Reads use GET; write actions use HttpPost + payload.
    /// Module Name : CRM Extension VAS_218
    /// Purpose     : Controller for the Create Opportunity Lines bottom panel.
    /// Chronological development:
    ///   VAI154     Created  21-Aug-2026
    /// </summary>
    public class VAS_218_CreateOppLinesController : Controller
    {
        /// <summary>Returns the parent-opportunity context and its saved lines.</summary>
        /// <param name="VAS_Opportunity_ID">parent opportunity</param>
        /// <param name="AD_Window_ID">source window</param>
        /// <param name="page">0-based page of saved lines</param>
        /// <returns>serialized panel view model</returns>
        [AjaxAuthorizeAttribute]
        [AjaxSessionFilterAttribute]
        public JsonResult GetPanelData(int VAS_Opportunity_ID, int AD_Window_ID, int page = 0)
        {
            string retJSON = "";
            if (Session["ctx"] != null)
            {
                Ctx ctx = Session["ctx"] as Ctx;
                VAS_218_CreateOppLinesModel model = new VAS_218_CreateOppLinesModel();
                retJSON = JsonConvert.SerializeObject(model.GetPanelData(ctx, VAS_Opportunity_ID, AD_Window_ID, page));
            }
            return Json(retJSON, JsonRequestBehavior.AllowGet);
        }

        /// <summary>Paged Product / Charge catalog search (50 rows + scroll paging).</summary>
        /// <param name="VAS_Opportunity_ID">parent opportunity (client scope)</param>
        /// <param name="query">typed keyword</param>
        /// <param name="pageSize">rows per page</param>
        /// <param name="offset">rows already loaded</param>
        /// <param name="rowContext">current line context JSON for val-rule filtering</param>
        /// <returns>serialized catalog rows</returns>
        [AjaxAuthorizeAttribute]
        [AjaxSessionFilterAttribute]
        public JsonResult SearchCatalog(int VAS_Opportunity_ID, string query, int pageSize, int offset, string rowContext)
        {
            string retJSON = "";
            if (Session["ctx"] != null)
            {
                Ctx ctx = Session["ctx"] as Ctx;
                Dictionary<string, object> rowValues = null;
                if (!string.IsNullOrEmpty(rowContext))
                {
                    try { rowValues = JsonConvert.DeserializeObject<Dictionary<string, object>>(rowContext); }
                    catch { rowValues = null; }
                }
                VAS_218_CreateOppLinesModel model = new VAS_218_CreateOppLinesModel();
                retJSON = JsonConvert.SerializeObject(
                    model.SearchProductsCharges(ctx, VAS_Opportunity_ID, query, pageSize, offset, rowValues));
            }
            return Json(retJSON, JsonRequestBehavior.AllowGet);
        }

        /// <summary>
        /// Per-row lookup re-filter: returns the UOM list valid for the supplied line context
        /// (each column's AD_Val_Rule resolved against the line's current values + opportunity
        /// header + session context).
        /// </summary>
        /// <param name="payload">serialized OppLookupRequest</param>
        /// <returns>serialized per-row UOM list</returns>
        [HttpPost]
        [AjaxAuthorizeAttribute]
        [AjaxSessionFilterAttribute]
        public JsonResult GetLookupData(string payload)
        {
            string retJSON = "";
            if (Session["ctx"] != null && !string.IsNullOrEmpty(payload))
            {
                Ctx ctx = Session["ctx"] as Ctx;
                OppLookupRequest req = JsonConvert.DeserializeObject<OppLookupRequest>(payload);
                VAS_218_CreateOppLinesModel model = new VAS_218_CreateOppLinesModel();
                retJSON = JsonConvert.SerializeObject(model.GetLookupData(ctx, req));
            }
            return Json(retJSON, JsonRequestBehavior.AllowGet);
        }

        /// <summary>
        /// Generic FK lookup for a dynamic "more" field (Table / TableDir / Search): returns
        /// id + label rows filtered by keyword, the column's AD_Val_Rule in the line's context
        /// and the role's access. Id greater than 0 resolves a single value's label.
        /// </summary>
        /// <param name="payload">serialized OppRefLookupRequest</param>
        /// <returns>serialized matching reference rows</returns>
        [HttpPost]
        [AjaxAuthorizeAttribute]
        [AjaxSessionFilterAttribute]
        public JsonResult GetRefLookup(string payload)
        {
            string retJSON = "";
            if (Session["ctx"] != null && !string.IsNullOrEmpty(payload))
            {
                Ctx ctx = Session["ctx"] as Ctx;
                OppRefLookupRequest req = JsonConvert.DeserializeObject<OppRefLookupRequest>(payload);
                VAS_218_CreateOppLinesModel model = new VAS_218_CreateOppLinesModel();
                retJSON = JsonConvert.SerializeObject(model.GetRefLookup(ctx, req));
            }
            return Json(retJSON, JsonRequestBehavior.AllowGet);
        }

        /// <summary>
        /// Reads the changed column's AD_Column.Callout and runs the equivalent callout
        /// server-side, returning the changed columns as a value object the client patches
        /// back into the line. Used on Product / Charge / UOM change to resolve the default
        /// UOM and display name without a client-side framework callout dependency.
        /// </summary>
        /// <param name="VAS_Opportunity_ID">parent opportunity</param>
        /// <param name="TriggerColumn">column that triggered the callout</param>
        /// <param name="M_Product_ID">selected product (0 when charge)</param>
        /// <param name="C_Charge_ID">selected charge (0 when product)</param>
        /// <param name="M_AttributeSetInstance_ID">selected attribute instance</param>
        /// <param name="PlannedQty">planned quantity</param>
        /// <param name="C_UOM_ID">unit of measure</param>
        /// <param name="PlannedPrice">planned price</param>
        /// <param name="PriceOverride">true when the user explicitly set the price</param>
        /// <returns>serialized OppCalloutResult (Column + Callout + Values + Display)</returns>
        [AjaxAuthorizeAttribute]
        [AjaxSessionFilterAttribute]
        public JsonResult RunCallout(int VAS_Opportunity_ID, string TriggerColumn, int M_Product_ID,
            int C_Charge_ID, int M_AttributeSetInstance_ID, decimal PlannedQty, int C_UOM_ID,
            decimal PlannedPrice, bool PriceOverride)
        {
            string retJSON = "";
            if (Session["ctx"] != null)
            {
                Ctx ctx = Session["ctx"] as Ctx;
                OppLineCalcRequest req = new OppLineCalcRequest
                {
                    VAS_Opportunity_ID = VAS_Opportunity_ID,
                    TriggerColumn = TriggerColumn,
                    M_Product_ID = M_Product_ID,
                    C_Charge_ID = C_Charge_ID,
                    M_AttributeSetInstance_ID = M_AttributeSetInstance_ID,
                    PlannedQty = PlannedQty,
                    C_UOM_ID = C_UOM_ID,
                    PlannedPrice = PlannedPrice,
                    PriceOverride = PriceOverride
                };
                VAS_218_CreateOppLinesModel model = new VAS_218_CreateOppLinesModel();
                retJSON = JsonConvert.SerializeObject(model.RunColumnCallout(ctx, req));
            }
            return Json(retJSON, JsonRequestBehavior.AllowGet);
        }

        /// <summary>
        /// Returns the UOM list valid for the given product, enforcing the C_UOM_ID
        /// column's AD_Val_Rule.  Called by the per-row UOM dropdown in the JS panel.
        /// When M_Product_ID is 0 (no product selected) only the default UOMs are returned.
        /// </summary>
        /// <param name="M_Product_ID">selected product (0 = no product)</param>
        /// <param name="AD_Window_ID">source window (reserved for future use)</param>
        /// <returns>serialized list of OppUomItem</returns>
        [AjaxAuthorizeAttribute]
        [AjaxSessionFilterAttribute]
        public JsonResult GetUomList(int M_Product_ID, int AD_Window_ID)
        {
            string retJSON = "";
            if (Session["ctx"] != null)
            {
                Ctx ctx = Session["ctx"] as Ctx;
                VAS_218_CreateOppLinesModel model = new VAS_218_CreateOppLinesModel();
                retJSON = JsonConvert.SerializeObject(model.GetUomListForProduct(ctx, M_Product_ID));
            }
            return Json(retJSON, JsonRequestBehavior.AllowGet);
        }

        /// <summary>
        /// Returns whether the given product has an attribute set configured.
        /// Called by the JS attribute-picker button to decide whether to open the picker.
        /// </summary>
        /// <param name="M_Product_ID">product to check</param>
        /// <returns>serialized object with a HasAttributeSet boolean</returns>
        [AjaxAuthorizeAttribute]
        [AjaxSessionFilterAttribute]
        public JsonResult HasAttributeSet(int M_Product_ID)
        {
            bool has = false;
            if (Session["ctx"] != null && M_Product_ID > 0)
            {
                Ctx ctx = Session["ctx"] as Ctx;
                VAS_218_CreateOppLinesModel model = new VAS_218_CreateOppLinesModel();
                has = model.ProductHasAttributeSet(ctx, M_Product_ID);
            }
            return Json(JsonConvert.SerializeObject(new { HasAttributeSet = has }), JsonRequestBehavior.AllowGet);
        }

        /// <summary>Returns the product's attribute-set definition for the attribute picker.</summary>
        /// <param name="M_Product_ID">product whose attribute set is read</param>
        /// <returns>serialized attribute-set definition</returns>
        [AjaxAuthorizeAttribute]
        [AjaxSessionFilterAttribute]
        public JsonResult GetProductAttributes(int M_Product_ID)
        {
            string retJSON = "";
            if (Session["ctx"] != null)
            {
                Ctx ctx = Session["ctx"] as Ctx;
                VAS_218_CreateOppLinesModel model = new VAS_218_CreateOppLinesModel();
                retJSON = JsonConvert.SerializeObject(model.GetProductAttributes(ctx, M_Product_ID));
            }
            return Json(retJSON, JsonRequestBehavior.AllowGet);
        }

        /// <summary>Returns an existing attribute-set instance's stored values for the edit form.</summary>
        /// <param name="M_AttributeSetInstance_ID">instance whose values are read</param>
        /// <returns>serialized list of typed attribute values</returns>
        [AjaxAuthorizeAttribute]
        [AjaxSessionFilterAttribute]
        public JsonResult GetInstanceValues(int M_AttributeSetInstance_ID)
        {
            string retJSON = "";
            if (Session["ctx"] != null)
            {
                Ctx ctx = Session["ctx"] as Ctx;
                VAS_218_CreateOppLinesModel model = new VAS_218_CreateOppLinesModel();
                retJSON = JsonConvert.SerializeObject(model.GetInstanceValues(ctx, M_AttributeSetInstance_ID));
            }
            return Json(retJSON, JsonRequestBehavior.AllowGet);
        }

        /// <summary>Looks up a product / charge by a scanned barcode.</summary>
        /// <param name="VAS_Opportunity_ID">parent opportunity (client scope)</param>
        /// <param name="code">scanned code</param>
        /// <returns>serialized matched catalog row</returns>
        [AjaxAuthorizeAttribute]
        [AjaxSessionFilterAttribute]
        public JsonResult ScanLookup(int VAS_Opportunity_ID, string code)
        {
            string retJSON = "";
            if (Session["ctx"] != null)
            {
                Ctx ctx = Session["ctx"] as Ctx;
                VAS_218_CreateOppLinesModel model = new VAS_218_CreateOppLinesModel();
                retJSON = JsonConvert.SerializeObject(model.ScanLookup(ctx, VAS_Opportunity_ID, code));
            }
            return Json(retJSON, JsonRequestBehavior.AllowGet);
        }

        /// <summary>
        /// Creates or updates an M_AttributeSetInstance from the picker selection.
        /// Delegates entirely to <see cref="VAS_218_CreateOppLinesModel.SaveAttribute"/>
        /// so all framework dedup, mandatory-validation and AttrCode / UPC behaviour
        /// stays in the model layer.
        /// </summary>
        /// <param name="payload">serialized OppAttributeSaveRequest</param>
        /// <returns>serialized OppAttributeSaveResult (id 0 + Error on failure)</returns>
        [HttpPost]
        [AjaxAuthorizeAttribute]
        [AjaxSessionFilterAttribute]
        public JsonResult SaveAttribute(string payload)
        {
            string retJSON = "";
            if (Session["ctx"] != null && !string.IsNullOrEmpty(payload))
            {
                Ctx ctx = Session["ctx"] as Ctx;
                OppAttributeSaveRequest req = JsonConvert.DeserializeObject<OppAttributeSaveRequest>(payload);
                VAS_218_CreateOppLinesModel model = new VAS_218_CreateOppLinesModel();
                retJSON = JsonConvert.SerializeObject(model.SaveAttribute(ctx, req));
            }
            return Json(retJSON, JsonRequestBehavior.AllowGet);
        }

        /// <summary>Inserts / updates the supplied opportunity lines through MVAS_OppLines.</summary>
        /// <param name="payload">serialized OppSaveLinesRequest</param>
        /// <returns>serialized save result (refreshed lines or error key)</returns>
        [HttpPost]
        [AjaxAuthorizeAttribute]
        [AjaxSessionFilterAttribute]
        public JsonResult SaveLines(string payload)
        {
            string retJSON = "";
            if (Session["ctx"] != null && !string.IsNullOrEmpty(payload))
            {
                Ctx ctx = Session["ctx"] as Ctx;
                OppSaveLinesRequest req = JsonConvert.DeserializeObject<OppSaveLinesRequest>(payload);
                VAS_218_CreateOppLinesModel model = new VAS_218_CreateOppLinesModel();
                retJSON = JsonConvert.SerializeObject(model.SaveLines(ctx, req.VAS_Opportunity_ID, req.AD_Window_ID, req.Lines, req.Page));
            }
            return Json(retJSON, JsonRequestBehavior.AllowGet);
        }

        /// <summary>Deletes the supplied saved opportunity lines through MVAS_OppLines.</summary>
        /// <param name="payload">serialized OppDeleteLinesRequest</param>
        /// <returns>serialized save result (refreshed lines or error key)</returns>
        [HttpPost]
        [AjaxAuthorizeAttribute]
        [AjaxSessionFilterAttribute]
        public JsonResult DeleteLines(string payload)
        {
            string retJSON = "";
            if (Session["ctx"] != null && !string.IsNullOrEmpty(payload))
            {
                Ctx ctx = Session["ctx"] as Ctx;
                OppDeleteLinesRequest req = JsonConvert.DeserializeObject<OppDeleteLinesRequest>(payload);
                VAS_218_CreateOppLinesModel model = new VAS_218_CreateOppLinesModel();
                retJSON = JsonConvert.SerializeObject(model.DeleteLines(ctx, req.VAS_Opportunity_ID, req.AD_Window_ID, req.LineIds, req.Page));
            }
            return Json(retJSON, JsonRequestBehavior.AllowGet);
        }
    }
}
