/******************************************************
 * Module Name    : CRM Extension VAS_248
 * Purpose        : Delivery Order Bottom Panel — controller
 * Employee Code  : VAI154
 * Date           : 07-Sep-2026
 * Rebuilt on the VAS_240 controller pattern — 09-Sep-2026
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
    /// AJAX endpoints for the VAS_248_DeliveryOrderBottomPanel tab panel — the
    /// customer-shipment counterpart of VAS_240_RequisitionBottomPanel. Each action
    /// reads the session Ctx, delegates to
    /// <see cref="VAS_248_DeliveryOrderBottomPanelModel"/> and returns the serialized
    /// result. Reads use GET; write actions use HttpPost + payload. There is no
    /// pricing endpoint: a shipment line carries no price or tax.
    ///
    /// Every action is direction-guarded inside the model: a vendor receipt id posted
    /// to this route is refused, so these endpoints cannot be used to edit a GRN.
    /// </summary>
    public class VAS_248_DeliveryOrderBottomPanelController : Controller
    {
        /// <summary>Returns the parent shipment context and one page of its saved lines.</summary>
        /// <param name="M_InOut_ID">parent shipment</param>
        /// <param name="AD_Window_ID">source window (supplies the line tabs)</param>
        /// <param name="page">0-based page of saved lines</param>
        /// <returns>serialized panel view model</returns>
        [AjaxAuthorizeAttribute]
        [AjaxSessionFilterAttribute]
        public JsonResult GetPanelData(int M_InOut_ID, int AD_Window_ID, int page = 0)
        {
            string retJSON = "";
            if (Session["ctx"] != null)
            {
                Ctx ctx = Session["ctx"] as Ctx;
                VAS_248_DeliveryOrderBottomPanelModel model = new VAS_248_DeliveryOrderBottomPanelModel();
                retJSON = JsonConvert.SerializeObject(model.GetPanelData(ctx, M_InOut_ID, AD_Window_ID, page));
            }
            return Json(retJSON, JsonRequestBehavior.AllowGet);
        }

        /// <summary>Paged Product / Charge catalog search (50 rows + scroll paging).</summary>
        /// <param name="M_InOut_ID">parent shipment (guards the call, and client scope)</param>
        /// <param name="query">typed keyword</param>
        /// <param name="pageSize">rows per page</param>
        /// <param name="offset">rows already loaded</param>
        /// <param name="rowContext">compact JSON of the line's current values</param>
        /// <returns>serialized catalog rows</returns>
        [AjaxAuthorizeAttribute]
        [AjaxSessionFilterAttribute]
        public JsonResult SearchCatalog(int M_InOut_ID, string query, int pageSize, int offset, string rowContext)
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
                VAS_248_DeliveryOrderBottomPanelModel model = new VAS_248_DeliveryOrderBottomPanelModel();
                retJSON = JsonConvert.SerializeObject(
                    model.SearchProductsCharges(ctx, M_InOut_ID, query, pageSize, offset, rowValues));
            }
            return Json(retJSON, JsonRequestBehavior.AllowGet);
        }

        /// <summary>
        /// Per-row lookup re-filter: the UOM and locator lists valid for the supplied line
        /// context (each column's AD_Val_Rule resolved against the line's current values +
        /// shipment header + session context).
        /// </summary>
        /// <param name="payload">serialized DeliveryLookupRequest</param>
        /// <returns>serialized per-row option lists</returns>
        [HttpPost]
        [AjaxAuthorizeAttribute]
        [AjaxSessionFilterAttribute]
        public JsonResult GetLookupData(string payload)
        {
            string retJSON = "";
            if (Session["ctx"] != null && !string.IsNullOrEmpty(payload))
            {
                Ctx ctx = Session["ctx"] as Ctx;
                DeliveryLookupRequest req = JsonConvert.DeserializeObject<DeliveryLookupRequest>(payload);
                VAS_248_DeliveryOrderBottomPanelModel model = new VAS_248_DeliveryOrderBottomPanelModel();
                retJSON = JsonConvert.SerializeObject(model.GetLookupData(ctx, req));
            }
            return Json(retJSON, JsonRequestBehavior.AllowGet);
        }

        /// <summary>
        /// Generic FK lookup for a dynamic "more" field (Table / TableDir / Search):
        /// returns id + label rows filtered by keyword, the column's AD_Val_Rule in the
        /// line's context and the role's access. Id &gt; 0 resolves a single value's label.
        /// </summary>
        /// <param name="payload">serialized DeliveryRefLookupRequest</param>
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
                DeliveryRefLookupRequest req = JsonConvert.DeserializeObject<DeliveryRefLookupRequest>(payload);
                VAS_248_DeliveryOrderBottomPanelModel model = new VAS_248_DeliveryOrderBottomPanelModel();
                retJSON = JsonConvert.SerializeObject(model.GetRefLookup(ctx, req));
            }
            return Json(retJSON, JsonRequestBehavior.AllowGet);
        }

        /// <summary>
        /// Reads the changed column's AD_Column.Callout and returns the values the framework
        /// derives — the line's unit and the base-unit MovementQty — as a patch the client
        /// applies back onto the line. No row is written.
        /// </summary>
        /// <param name="M_InOut_ID">parent shipment</param>
        /// <param name="TriggerColumn">column that changed</param>
        /// <param name="M_Product_ID">product now on the line</param>
        /// <param name="C_Charge_ID">charge now on the line</param>
        /// <param name="M_AttributeSetInstance_ID">attribute-set instance now on the line</param>
        /// <param name="QtyEntered">quantity in the line's selected unit</param>
        /// <param name="C_UOM_ID">the line's selected unit</param>
        /// <param name="M_Locator_ID">the bin the goods leave from</param>
        /// <returns>serialized DeliveryCalloutResult (Column + Callout + Values + Display)</returns>
        [AjaxAuthorizeAttribute]
        [AjaxSessionFilterAttribute]
        public JsonResult RunCallout(int M_InOut_ID, string TriggerColumn, int M_Product_ID,
            int C_Charge_ID, int M_AttributeSetInstance_ID, decimal QtyEntered, int C_UOM_ID,
            int M_Locator_ID)
        {
            string retJSON = "";
            if (Session["ctx"] != null)
            {
                Ctx ctx = Session["ctx"] as Ctx;
                VAS_248_DeliveryOrderBottomPanelModel model = new VAS_248_DeliveryOrderBottomPanelModel();
                retJSON = JsonConvert.SerializeObject(model.RunColumnCallout(ctx, new DeliveryLineCalcRequest
                {
                    M_InOut_ID = M_InOut_ID,
                    TriggerColumn = TriggerColumn,
                    M_Product_ID = M_Product_ID,
                    C_Charge_ID = C_Charge_ID,
                    M_AttributeSetInstance_ID = M_AttributeSetInstance_ID,
                    QtyEntered = QtyEntered,
                    C_UOM_ID = C_UOM_ID,
                    M_Locator_ID = M_Locator_ID
                }));
            }
            return Json(retJSON, JsonRequestBehavior.AllowGet);
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
                VAS_248_DeliveryOrderBottomPanelModel model = new VAS_248_DeliveryOrderBottomPanelModel();
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
                VAS_248_DeliveryOrderBottomPanelModel model = new VAS_248_DeliveryOrderBottomPanelModel();
                retJSON = JsonConvert.SerializeObject(model.GetInstanceValues(ctx, M_AttributeSetInstance_ID));
            }
            return Json(retJSON, JsonRequestBehavior.AllowGet);
        }

        /// <summary>Looks up a product / charge by a scanned barcode.</summary>
        /// <param name="M_InOut_ID">parent shipment (client scope)</param>
        /// <param name="code">scanned code</param>
        /// <returns>serialized matched catalog row</returns>
        [AjaxAuthorizeAttribute]
        [AjaxSessionFilterAttribute]
        public JsonResult ScanLookup(int M_InOut_ID, string code)
        {
            string retJSON = "";
            if (Session["ctx"] != null)
            {
                Ctx ctx = Session["ctx"] as Ctx;
                VAS_248_DeliveryOrderBottomPanelModel model = new VAS_248_DeliveryOrderBottomPanelModel();
                retJSON = JsonConvert.SerializeObject(model.ScanLookup(ctx, M_InOut_ID, code));
            }
            return Json(retJSON, JsonRequestBehavior.AllowGet);
        }

        /// <summary>
        /// Creates or updates an M_AttributeSetInstance from the picker selection.
        /// Delegates entirely to the model so all framework dedup, mandatory-validation and
        /// AttrCode / UPC behaviour stays in the model layer.
        /// </summary>
        /// <param name="payload">serialized DeliveryAttributeSaveRequest</param>
        /// <returns>serialized DeliveryAttributeSaveResult (id 0 + Error on failure)</returns>
        [HttpPost]
        [AjaxAuthorizeAttribute]
        [AjaxSessionFilterAttribute]
        public JsonResult SaveAttribute(string payload)
        {
            string retJSON = "";
            if (Session["ctx"] != null && !string.IsNullOrEmpty(payload))
            {
                Ctx ctx = Session["ctx"] as Ctx;
                DeliveryAttributeSaveRequest req = JsonConvert.DeserializeObject<DeliveryAttributeSaveRequest>(payload);
                VAS_248_DeliveryOrderBottomPanelModel model = new VAS_248_DeliveryOrderBottomPanelModel();
                retJSON = JsonConvert.SerializeObject(model.SaveAttribute(ctx, req));
            }
            return Json(retJSON, JsonRequestBehavior.AllowGet);
        }

        /// <summary>Inserts / updates the supplied shipment lines through MInOutLine.</summary>
        /// <param name="payload">serialized DeliverySaveLinesRequest</param>
        /// <returns>serialized save result (refreshed page or error key)</returns>
        [HttpPost]
        [AjaxAuthorizeAttribute]
        [AjaxSessionFilterAttribute]
        public JsonResult SaveLines(string payload)
        {
            string retJSON = "";
            if (Session["ctx"] != null && !string.IsNullOrEmpty(payload))
            {
                Ctx ctx = Session["ctx"] as Ctx;
                DeliverySaveLinesRequest req = JsonConvert.DeserializeObject<DeliverySaveLinesRequest>(payload);
                VAS_248_DeliveryOrderBottomPanelModel model = new VAS_248_DeliveryOrderBottomPanelModel();
                retJSON = JsonConvert.SerializeObject(
                    model.SaveLines(ctx, req.M_InOut_ID, req.AD_Window_ID, req.Lines, req.Page));
            }
            return Json(retJSON, JsonRequestBehavior.AllowGet);
        }

        /// <summary>Deletes the supplied saved shipment lines through MInOutLine.</summary>
        /// <param name="payload">serialized DeliveryDeleteLinesRequest</param>
        /// <returns>serialized save result (refreshed page or error key)</returns>
        [HttpPost]
        [AjaxAuthorizeAttribute]
        [AjaxSessionFilterAttribute]
        public JsonResult DeleteLines(string payload)
        {
            string retJSON = "";
            if (Session["ctx"] != null && !string.IsNullOrEmpty(payload))
            {
                Ctx ctx = Session["ctx"] as Ctx;
                DeliveryDeleteLinesRequest req = JsonConvert.DeserializeObject<DeliveryDeleteLinesRequest>(payload);
                VAS_248_DeliveryOrderBottomPanelModel model = new VAS_248_DeliveryOrderBottomPanelModel();
                retJSON = JsonConvert.SerializeObject(
                    model.DeleteLines(ctx, req.M_InOut_ID, req.AD_Window_ID, req.LineIds, req.Page));
            }
            return Json(retJSON, JsonRequestBehavior.AllowGet);
        }
    }
}
