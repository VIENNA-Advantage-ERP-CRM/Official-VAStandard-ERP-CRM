/******************************************************
 * Module Name    : CRM Extension VAS_107
 * Purpose        : Create Order Bottom Panel — controller
 * Employee Code  : VAI154
 * Date           : 09-Jul-2026
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
    /// AJAX endpoints for the VAS_107_CreateOrderBottomPanel tab panel. Each
    /// action reads the session Ctx, delegates to
    /// <see cref="VAS_107_CreateOrderBottomPanelModel"/> and returns the
    /// serialized result. Reads use GET; write actions use HttpPost + payload.
    /// </summary>
    public class VAS_107_CreateOrderBottomPanelController : Controller
    {
        /// <summary>Returns the parent-order context and its saved lines.</summary>
        /// <param name="C_Order_ID">parent order</param>
        /// <returns>serialized panel view model</returns>
        [AjaxAuthorizeAttribute]
        [AjaxSessionFilterAttribute]
        public JsonResult GetPanelData(int C_Order_ID, int AD_Window_ID, int page = 0)
        {
            string retJSON = "";
            if (Session["ctx"] != null)
            {
                Ctx ctx = Session["ctx"] as Ctx;
                VAS_107_CreateOrderBottomPanelModel model = new VAS_107_CreateOrderBottomPanelModel();
                retJSON = JsonConvert.SerializeObject(model.GetPanelData(ctx, C_Order_ID, AD_Window_ID, page));
            }
            return Json(retJSON, JsonRequestBehavior.AllowGet);
        }

        /// <summary>Paged Product / Charge catalog search (50 rows + scroll paging).</summary>
        /// <param name="C_Order_ID">parent order (client scope)</param>
        /// <param name="query">typed keyword</param>
        /// <param name="pageSize">rows per page</param>
        /// <param name="offset">rows already loaded</param>
        /// <returns>serialized catalog rows</returns>
        [AjaxAuthorizeAttribute]
        [AjaxSessionFilterAttribute]
        public JsonResult SearchCatalog(int C_Order_ID, string query, int pageSize, int offset, string rowContext)
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
                VAS_107_CreateOrderBottomPanelModel model = new VAS_107_CreateOrderBottomPanelModel();
                retJSON = JsonConvert.SerializeObject(
                    model.SearchProductsCharges(ctx, C_Order_ID, query, pageSize, offset, rowValues));
            }
            return Json(retJSON, JsonRequestBehavior.AllowGet);
        }

        /// <summary>
        /// Per-row lookup re-filter: returns the UOM + tax lists valid for the supplied
        /// line context (each column's AD_Val_Rule resolved against the line's current
        /// values + order header + session context).
        /// </summary>
        /// <param name="payload">serialized OrderLookupRequest</param>
        /// <returns>serialized per-row UOM + tax lists</returns>
        [HttpPost]
        [AjaxAuthorizeAttribute]
        [AjaxSessionFilterAttribute]
        public JsonResult GetLookupData(string payload)
        {
            string retJSON = "";
            if (Session["ctx"] != null && !string.IsNullOrEmpty(payload))
            {
                Ctx ctx = Session["ctx"] as Ctx;
                OrderLookupRequest req = JsonConvert.DeserializeObject<OrderLookupRequest>(payload);
                VAS_107_CreateOrderBottomPanelModel model = new VAS_107_CreateOrderBottomPanelModel();
                retJSON = JsonConvert.SerializeObject(model.GetLookupData(ctx, req));
            }
            return Json(retJSON, JsonRequestBehavior.AllowGet);
        }

        /// <summary>
        /// Generic FK lookup for a dynamic "more" field (Table / TableDir / Search):
        /// returns id + label rows filtered by keyword, the column's AD_Val_Rule in the
        /// line's context and the role's access. Id &gt; 0 resolves a single value's label.
        /// </summary>
        /// <param name="payload">serialized OrderRefLookupRequest</param>
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
                OrderRefLookupRequest req = JsonConvert.DeserializeObject<OrderRefLookupRequest>(payload);
                VAS_107_CreateOrderBottomPanelModel model = new VAS_107_CreateOrderBottomPanelModel();
                retJSON = JsonConvert.SerializeObject(model.GetRefLookup(ctx, req));
            }
            return Json(retJSON, JsonRequestBehavior.AllowGet);
        }

        /// <summary>
        /// Server-side line callout: recomputes UOM / price / tax / amounts for
        /// the current product-charge-qty-price-tax selection.
        /// </summary>
        /// <returns>serialized recomputed values + labels</returns>
        [AjaxAuthorizeAttribute]
        [AjaxSessionFilterAttribute]
        public JsonResult CalcLine(int C_Order_ID, string TriggerColumn, int M_Product_ID,
            int C_Charge_ID, int M_AttributeSetInstance_ID, decimal QtyOrdered, int C_UOM_ID,
            decimal PriceEntered, bool PriceOverride, int C_Tax_ID, decimal Discount)
        {
            string retJSON = "";
            if (Session["ctx"] != null)
            {
                Ctx ctx = Session["ctx"] as Ctx;
                OrderLineCalcRequest req = new OrderLineCalcRequest
                {
                    C_Order_ID = C_Order_ID,
                    TriggerColumn = TriggerColumn,
                    M_Product_ID = M_Product_ID,
                    C_Charge_ID = C_Charge_ID,
                    M_AttributeSetInstance_ID = M_AttributeSetInstance_ID,
                    QtyOrdered = QtyOrdered,
                    C_UOM_ID = C_UOM_ID,
                    PriceEntered = PriceEntered,
                    PriceOverride = PriceOverride,
                    C_Tax_ID = C_Tax_ID,
                    Discount = Discount
                };
                VAS_107_CreateOrderBottomPanelModel model = new VAS_107_CreateOrderBottomPanelModel();
                retJSON = JsonConvert.SerializeObject(model.CalcLine(ctx, req));
            }
            return Json(retJSON, JsonRequestBehavior.AllowGet);
        }

        /// <summary>
        /// Reads the changed column's AD_Column.Callout and runs the equivalent
        /// callout server-side, returning the changed columns as a value object
        /// the client patches back into the line. Used on Product / Charge change.
        /// </summary>
        /// <returns>serialized CalloutResult (Column + Callout + Values + Display)</returns>
        [AjaxAuthorizeAttribute]
        [AjaxSessionFilterAttribute]
        public JsonResult RunCallout(int C_Order_ID, string TriggerColumn, int M_Product_ID,
            int C_Charge_ID, int M_AttributeSetInstance_ID, decimal QtyOrdered, int C_UOM_ID,
            decimal PriceEntered, bool PriceOverride, int C_Tax_ID, decimal Discount)
        {
            string retJSON = "";
            if (Session["ctx"] != null)
            {
                Ctx ctx = Session["ctx"] as Ctx;
                OrderLineCalcRequest req = new OrderLineCalcRequest
                {
                    C_Order_ID = C_Order_ID,
                    TriggerColumn = TriggerColumn,
                    M_Product_ID = M_Product_ID,
                    C_Charge_ID = C_Charge_ID,
                    M_AttributeSetInstance_ID = M_AttributeSetInstance_ID,
                    QtyOrdered = QtyOrdered,
                    C_UOM_ID = C_UOM_ID,
                    PriceEntered = PriceEntered,
                    PriceOverride = PriceOverride,
                    C_Tax_ID = C_Tax_ID,
                    Discount = Discount
                };
                VAS_107_CreateOrderBottomPanelModel model = new VAS_107_CreateOrderBottomPanelModel();
                retJSON = JsonConvert.SerializeObject(model.RunColumnCallout(ctx, req));
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
                VAS_107_CreateOrderBottomPanelModel model = new VAS_107_CreateOrderBottomPanelModel();
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
                VAS_107_CreateOrderBottomPanelModel model = new VAS_107_CreateOrderBottomPanelModel();
                retJSON = JsonConvert.SerializeObject(model.GetInstanceValues(ctx, M_AttributeSetInstance_ID));
            }
            return Json(retJSON, JsonRequestBehavior.AllowGet);
        }

        /// <summary>Looks up a product / charge by a scanned barcode.</summary>
        /// <param name="C_Order_ID">parent order (client scope)</param>
        /// <param name="code">scanned code</param>
        /// <returns>serialized matched catalog row</returns>
        [AjaxAuthorizeAttribute]
        [AjaxSessionFilterAttribute]
        public JsonResult ScanLookup(int C_Order_ID, string code)
        {
            string retJSON = "";
            if (Session["ctx"] != null)
            {
                Ctx ctx = Session["ctx"] as Ctx;
                VAS_107_CreateOrderBottomPanelModel model = new VAS_107_CreateOrderBottomPanelModel();
                retJSON = JsonConvert.SerializeObject(model.ScanLookup(ctx, C_Order_ID, code));
            }
            return Json(retJSON, JsonRequestBehavior.AllowGet);
        }

        /// <summary>
        /// Creates or updates an M_AttributeSetInstance from the picker selection.
        /// Delegates entirely to <see cref="VAS_107_CreateOrderBottomPanelModel.SaveAttribute"/>
        /// so all framework dedup, mandatory-validation and AttrCode / UPC behaviour
        /// stays in the model layer.
        /// </summary>
        /// <param name="payload">serialized OrderAttributeSaveRequest</param>
        /// <returns>serialized OrderAttributeSaveResult (id 0 + Error on failure)</returns>
        [HttpPost]
        [AjaxAuthorizeAttribute]
        [AjaxSessionFilterAttribute]
        public JsonResult SaveAttribute(string payload)
        {
            string retJSON = "";
            if (Session["ctx"] != null && !string.IsNullOrEmpty(payload))
            {
                Ctx ctx = Session["ctx"] as Ctx;
                OrderAttributeSaveRequest req = JsonConvert.DeserializeObject<OrderAttributeSaveRequest>(payload);
                VAS_107_CreateOrderBottomPanelModel model = new VAS_107_CreateOrderBottomPanelModel();
                retJSON = JsonConvert.SerializeObject(model.SaveAttribute(ctx, req));
            }
            return Json(retJSON, JsonRequestBehavior.AllowGet);
        }

        /// <summary>Inserts / updates the supplied order lines through MOrderLine.</summary>
        /// <param name="payload">serialized OrderSaveLinesRequest</param>
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
                OrderSaveLinesRequest req = JsonConvert.DeserializeObject<OrderSaveLinesRequest>(payload);
                VAS_107_CreateOrderBottomPanelModel model = new VAS_107_CreateOrderBottomPanelModel();
                retJSON = JsonConvert.SerializeObject(model.SaveLines(ctx, req.C_Order_ID, req.AD_Window_ID, req.Lines, req.Page));
            }
            return Json(retJSON, JsonRequestBehavior.AllowGet);
        }

        /// <summary>Deletes the supplied saved order lines through MOrderLine.</summary>
        /// <param name="payload">serialized OrderDeleteLinesRequest</param>
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
                OrderDeleteLinesRequest req = JsonConvert.DeserializeObject<OrderDeleteLinesRequest>(payload);
                VAS_107_CreateOrderBottomPanelModel model = new VAS_107_CreateOrderBottomPanelModel();
                retJSON = JsonConvert.SerializeObject(model.DeleteLines(ctx, req.C_Order_ID, req.AD_Window_ID, req.LineIds, req.Page));
            }
            return Json(retJSON, JsonRequestBehavior.AllowGet);
        }
    }
}
