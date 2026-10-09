/******************************************************
 * Module Name    : CRM Extension VAS_240
 * Purpose        : Requisition Bottom Panel — controller
 * Employee Code  : VAI163
 * Date           : 03-Sep-2026
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
    /// AJAX endpoints for the VAS_240_RequisitionBottomPanel tab panel — the
    /// requisition counterpart of VAS_107_CreateOrderBottomPanel. Each action
    /// reads the session Ctx, delegates to
    /// <see cref="VAS_240_RequisitionBottomPanelModel"/> and returns the
    /// serialized result. Reads use GET; write actions use HttpPost + payload.
    /// There is no tax endpoint: a requisition line carries no tax.
    /// </summary>
    public class VAS_240_RequisitionBottomPanelController : Controller
    {
        /// <summary>Returns the parent-requisition context and its saved lines.</summary>
        /// <param name="M_Requisition_ID">parent requisition</param>
        /// <param name="AD_Window_ID">source window (supplies the line tabs)</param>
        /// <param name="page">0-based page of saved lines</param>
        /// <returns>serialized panel view model</returns>
        [AjaxAuthorizeAttribute]
        [AjaxSessionFilterAttribute]
        public JsonResult GetPanelData(int M_Requisition_ID, int AD_Window_ID, int page = 0)
        {
            string retJSON = "";
            if (Session["ctx"] != null)
            {
                Ctx ctx = Session["ctx"] as Ctx;
                VAS_240_RequisitionBottomPanelModel model = new VAS_240_RequisitionBottomPanelModel();
                retJSON = JsonConvert.SerializeObject(model.GetPanelData(ctx, M_Requisition_ID, AD_Window_ID, page));
            }
            return Json(retJSON, JsonRequestBehavior.AllowGet);
        }

        /// <summary>Paged Product / Charge catalog search (50 rows + scroll paging).</summary>
        /// <param name="M_Requisition_ID">parent requisition (client scope)</param>
        /// <param name="query">typed keyword</param>
        /// <param name="pageSize">rows per page</param>
        /// <param name="offset">rows already loaded</param>
        /// <param name="rowContext">compact JSON of the line's current values</param>
        /// <returns>serialized catalog rows</returns>
        [AjaxAuthorizeAttribute]
        [AjaxSessionFilterAttribute]
        public JsonResult SearchCatalog(int M_Requisition_ID, string query, int pageSize, int offset, string rowContext)
        {
            string retJSON = "";
            if (Session["ctx"] != null)
            {
                Ctx ctx = Session["ctx"] as Ctx;
                // Current line context (compact JSON) so a per-row product / charge
                // AD_Val_Rule re-filters the catalog; absent / malformed -> header only.
                Dictionary<string, object> rowValues = null;
                if (!string.IsNullOrEmpty(rowContext))
                {
                    try { rowValues = JsonConvert.DeserializeObject<Dictionary<string, object>>(rowContext); }
                    catch { rowValues = null; }
                }
                VAS_240_RequisitionBottomPanelModel model = new VAS_240_RequisitionBottomPanelModel();
                retJSON = JsonConvert.SerializeObject(
                    model.SearchProductsCharges(ctx, M_Requisition_ID, query, pageSize, offset, rowValues));
            }
            return Json(retJSON, JsonRequestBehavior.AllowGet);
        }

        /// <summary>
        /// Per-row lookup re-filter: returns the UOM list valid for the supplied line
        /// context (the column's AD_Val_Rule resolved against the line's current values
        /// + requisition header + session context).
        /// </summary>
        /// <param name="payload">serialized RequisitionLookupRequest</param>
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
                RequisitionLookupRequest req = JsonConvert.DeserializeObject<RequisitionLookupRequest>(payload);
                VAS_240_RequisitionBottomPanelModel model = new VAS_240_RequisitionBottomPanelModel();
                retJSON = JsonConvert.SerializeObject(model.GetLookupData(ctx, req));
            }
            return Json(retJSON, JsonRequestBehavior.AllowGet);
        }

        /// <summary>
        /// Generic FK lookup for a dynamic "more" field (Table / TableDir / Search):
        /// returns id + label rows filtered by keyword, the column's AD_Val_Rule in the
        /// line's context and the role's access. Id &gt; 0 resolves a single value's label.
        /// </summary>
        /// <param name="payload">serialized RequisitionRefLookupRequest</param>
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
                RequisitionRefLookupRequest req = JsonConvert.DeserializeObject<RequisitionRefLookupRequest>(payload);
                VAS_240_RequisitionBottomPanelModel model = new VAS_240_RequisitionBottomPanelModel();
                retJSON = JsonConvert.SerializeObject(model.GetRefLookup(ctx, req));
            }
            return Json(retJSON, JsonRequestBehavior.AllowGet);
        }

        /// <summary>
        /// Server-side line callout: recomputes UOM / price / quantity / amount for the
        /// current product-charge-qty-price selection.
        /// </summary>
        /// <returns>serialized recomputed values + labels</returns>
        [AjaxAuthorizeAttribute]
        [AjaxSessionFilterAttribute]
        public JsonResult CalcLine(int M_Requisition_ID, string TriggerColumn, int M_Product_ID,
            int C_Charge_ID, int M_AttributeSetInstance_ID, decimal QtyEntered, int C_UOM_ID,
            decimal PriceActual, bool PriceOverride, decimal Discount)
        {
            string retJSON = "";
            if (Session["ctx"] != null)
            {
                Ctx ctx = Session["ctx"] as Ctx;
                VAS_240_RequisitionBottomPanelModel model = new VAS_240_RequisitionBottomPanelModel();
                retJSON = JsonConvert.SerializeObject(model.CalcLine(ctx,
                    BuildCalcRequest(M_Requisition_ID, TriggerColumn, M_Product_ID, C_Charge_ID,
                        M_AttributeSetInstance_ID, QtyEntered, C_UOM_ID, PriceActual, PriceOverride, Discount)));
            }
            return Json(retJSON, JsonRequestBehavior.AllowGet);
        }

        /// <summary>
        /// Reads the changed column's AD_Column.Callout and runs the equivalent callout
        /// server-side, returning the changed columns as a value object the client
        /// patches back into the line. Used on Product / Charge change.
        /// </summary>
        /// <returns>serialized RequisitionCalloutResult (Column + Callout + Values + Display)</returns>
        [AjaxAuthorizeAttribute]
        [AjaxSessionFilterAttribute]
        public JsonResult RunCallout(int M_Requisition_ID, string TriggerColumn, int M_Product_ID,
            int C_Charge_ID, int M_AttributeSetInstance_ID, decimal QtyEntered, int C_UOM_ID,
            decimal PriceActual, bool PriceOverride, decimal Discount)
        {
            string retJSON = "";
            if (Session["ctx"] != null)
            {
                Ctx ctx = Session["ctx"] as Ctx;
                VAS_240_RequisitionBottomPanelModel model = new VAS_240_RequisitionBottomPanelModel();
                retJSON = JsonConvert.SerializeObject(model.RunColumnCallout(ctx,
                    BuildCalcRequest(M_Requisition_ID, TriggerColumn, M_Product_ID, C_Charge_ID,
                        M_AttributeSetInstance_ID, QtyEntered, C_UOM_ID, PriceActual, PriceOverride, Discount)));
            }
            return Json(retJSON, JsonRequestBehavior.AllowGet);
        }

        /// <summary>Packs the query-string arguments both callout actions take.</summary>
        private static RequisitionLineCalcRequest BuildCalcRequest(int M_Requisition_ID, string TriggerColumn,
            int M_Product_ID, int C_Charge_ID, int M_AttributeSetInstance_ID, decimal QtyEntered,
            int C_UOM_ID, decimal PriceActual, bool PriceOverride, decimal Discount)
        {
            return new RequisitionLineCalcRequest
            {
                M_Requisition_ID = M_Requisition_ID,
                TriggerColumn = TriggerColumn,
                M_Product_ID = M_Product_ID,
                C_Charge_ID = C_Charge_ID,
                M_AttributeSetInstance_ID = M_AttributeSetInstance_ID,
                QtyEntered = QtyEntered,
                C_UOM_ID = C_UOM_ID,
                PriceActual = PriceActual,
                PriceOverride = PriceOverride,
                Discount = Discount
            };
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
                VAS_240_RequisitionBottomPanelModel model = new VAS_240_RequisitionBottomPanelModel();
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
                VAS_240_RequisitionBottomPanelModel model = new VAS_240_RequisitionBottomPanelModel();
                retJSON = JsonConvert.SerializeObject(model.GetInstanceValues(ctx, M_AttributeSetInstance_ID));
            }
            return Json(retJSON, JsonRequestBehavior.AllowGet);
        }

        /// <summary>Looks up a product / charge by a scanned barcode.</summary>
        /// <param name="M_Requisition_ID">parent requisition (client scope)</param>
        /// <param name="code">scanned code</param>
        /// <returns>serialized matched catalog row</returns>
        [AjaxAuthorizeAttribute]
        [AjaxSessionFilterAttribute]
        public JsonResult ScanLookup(int M_Requisition_ID, string code)
        {
            string retJSON = "";
            if (Session["ctx"] != null)
            {
                Ctx ctx = Session["ctx"] as Ctx;
                VAS_240_RequisitionBottomPanelModel model = new VAS_240_RequisitionBottomPanelModel();
                retJSON = JsonConvert.SerializeObject(model.ScanLookup(ctx, M_Requisition_ID, code));
            }
            return Json(retJSON, JsonRequestBehavior.AllowGet);
        }

        /// <summary>
        /// Creates or updates an M_AttributeSetInstance from the picker selection.
        /// Delegates entirely to <see cref="VAS_240_RequisitionBottomPanelModel.SaveAttribute"/>
        /// so all framework dedup, mandatory-validation and AttrCode / UPC behaviour
        /// stays in the model layer.
        /// </summary>
        /// <param name="payload">serialized RequisitionAttributeSaveRequest</param>
        /// <returns>serialized RequisitionAttributeSaveResult (id 0 + Error on failure)</returns>
        [HttpPost]
        [AjaxAuthorizeAttribute]
        [AjaxSessionFilterAttribute]
        public JsonResult SaveAttribute(string payload)
        {
            string retJSON = "";
            if (Session["ctx"] != null && !string.IsNullOrEmpty(payload))
            {
                Ctx ctx = Session["ctx"] as Ctx;
                RequisitionAttributeSaveRequest req = JsonConvert.DeserializeObject<RequisitionAttributeSaveRequest>(payload);
                VAS_240_RequisitionBottomPanelModel model = new VAS_240_RequisitionBottomPanelModel();
                retJSON = JsonConvert.SerializeObject(model.SaveAttribute(ctx, req));
            }
            return Json(retJSON, JsonRequestBehavior.AllowGet);
        }

        /// <summary>Inserts / updates the supplied requisition lines through MRequisitionLine.</summary>
        /// <param name="payload">serialized RequisitionSaveLinesRequest</param>
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
                RequisitionSaveLinesRequest req = JsonConvert.DeserializeObject<RequisitionSaveLinesRequest>(payload);
                VAS_240_RequisitionBottomPanelModel model = new VAS_240_RequisitionBottomPanelModel();
                retJSON = JsonConvert.SerializeObject(
                    model.SaveLines(ctx, req.M_Requisition_ID, req.AD_Window_ID, req.Lines, req.Page));
            }
            return Json(retJSON, JsonRequestBehavior.AllowGet);
        }

        /// <summary>Deletes the supplied saved requisition lines through MRequisitionLine.</summary>
        /// <param name="payload">serialized RequisitionDeleteLinesRequest</param>
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
                RequisitionDeleteLinesRequest req = JsonConvert.DeserializeObject<RequisitionDeleteLinesRequest>(payload);
                VAS_240_RequisitionBottomPanelModel model = new VAS_240_RequisitionBottomPanelModel();
                retJSON = JsonConvert.SerializeObject(
                    model.DeleteLines(ctx, req.M_Requisition_ID, req.AD_Window_ID, req.LineIds, req.Page));
            }
            return Json(retJSON, JsonRequestBehavior.AllowGet);
        }
    }
}
