/******************************************************
 * Module Name    : VAS
 * Purpose        : AJAX endpoints for the VAS_074_CreateInvoiceLinePanel tab
 *                  panel (parent context + lines, catalog search, line callout,
 *                  product attributes, barcode scan lookup and the line
 *                  insert / update / delete write actions).
 * chronological  : Development
 *   VAI_XXX        Created  25 June 2026
 ******************************************************/
// NOTE: Replace VAI_XXX with your own Employee Code before committing.

using Newtonsoft.Json;
using System.Collections.Generic;
using System.Web.Mvc;
using VAdvantage.Model;
using VAdvantage.Utility;
using VASLogic.Models;
using VIS.Filters;
using VIS.Models;

namespace VAS.Controllers
{
    /// <summary>
    /// AJAX endpoints for the VAS_074_CreateInvoiceLinePanel tab panel. Each
    /// action reads the session Ctx, delegates to
    /// <see cref="VAS_074_CreateInvoiceLinePanelModel"/> and returns the
    /// serialized result. Reads use GET; write actions use HttpPost + payload.
    /// </summary>
    public class VAS_074_CreateInvoiceLinePanelController : Controller
    {
        /// <summary>Returns the parent-invoice context and its saved lines.</summary>
        /// <param name="C_Invoice_ID">parent invoice</param>
        /// <returns>serialized panel view model</returns>
        [AjaxAuthorizeAttribute]
        [AjaxSessionFilterAttribute]
        public JsonResult GetPanelData(int C_Invoice_ID, int AD_Window_ID, int page = 0)
        {
            string retJSON = "";
            if (Session["ctx"] != null)
            {
                Ctx ctx = Session["ctx"] as Ctx;
                VAS_074_CreateInvoiceLinePanelModel model = new VAS_074_CreateInvoiceLinePanelModel();
                retJSON = JsonConvert.SerializeObject(model.GetPanelData(ctx, C_Invoice_ID, AD_Window_ID, page));
            }
            return Json(retJSON, JsonRequestBehavior.AllowGet);
        }

        /// <summary>Paged Product / Charge catalog search (50 rows + scroll paging).</summary>
        /// <param name="C_Invoice_ID">parent invoice (client scope)</param>
        /// <param name="query">typed keyword</param>
        /// <param name="pageSize">rows per page</param>
        /// <param name="offset">rows already loaded</param>
        /// <returns>serialized catalog rows</returns>
        [AjaxAuthorizeAttribute]
        [AjaxSessionFilterAttribute]
        public JsonResult SearchCatalog(int C_Invoice_ID, string query, int pageSize, int offset, string rowContext)
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
                VAS_074_CreateInvoiceLinePanelModel model = new VAS_074_CreateInvoiceLinePanelModel();
                retJSON = JsonConvert.SerializeObject(
                    model.SearchProductsCharges(ctx, C_Invoice_ID, query, pageSize, offset, rowValues));
            }
            return Json(retJSON, JsonRequestBehavior.AllowGet);
        }

        /// <summary>
        /// Per-row lookup re-filter: returns the UOM + tax lists valid for the supplied
        /// line context (each column's AD_Val_Rule resolved against the line's current
        /// values + invoice header + session context).
        /// </summary>
        /// <param name="payload">serialized LookupRequest</param>
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
                LookupRequest req = JsonConvert.DeserializeObject<LookupRequest>(payload);
                VAS_074_CreateInvoiceLinePanelModel model = new VAS_074_CreateInvoiceLinePanelModel();
                retJSON = JsonConvert.SerializeObject(model.GetLookupData(ctx, req));
            }
            return Json(retJSON, JsonRequestBehavior.AllowGet);
        }

        /// <summary>
        /// Generic FK lookup for a dynamic "more" field (Table / TableDir / Search):
        /// returns id + label rows filtered by keyword, the column's AD_Val_Rule in the
        /// line's context and the role's access. Id &gt; 0 resolves a single value's label.
        /// </summary>
        /// <param name="payload">serialized RefLookupRequest</param>
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
                RefLookupRequest req = JsonConvert.DeserializeObject<RefLookupRequest>(payload);
                VAS_074_CreateInvoiceLinePanelModel model = new VAS_074_CreateInvoiceLinePanelModel();
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
        public JsonResult CalcLine(int C_Invoice_ID, string TriggerColumn, int M_Product_ID,
            int C_Charge_ID, int M_AttributeSetInstance_ID, decimal QtyEntered, int C_UOM_ID,
            decimal PriceEntered, bool PriceOverride, int C_Tax_ID, decimal Discount)
        {
            string retJSON = "";
            if (Session["ctx"] != null)
            {
                Ctx ctx = Session["ctx"] as Ctx;
                LineCalcRequest req = new LineCalcRequest
                {
                    C_Invoice_ID = C_Invoice_ID,
                    TriggerColumn = TriggerColumn,
                    M_Product_ID = M_Product_ID,
                    C_Charge_ID = C_Charge_ID,
                    M_AttributeSetInstance_ID = M_AttributeSetInstance_ID,
                    QtyEntered = QtyEntered,
                    C_UOM_ID = C_UOM_ID,
                    PriceEntered = PriceEntered,
                    PriceOverride = PriceOverride,
                    C_Tax_ID = C_Tax_ID,
                    Discount = Discount
                };
                VAS_074_CreateInvoiceLinePanelModel model = new VAS_074_CreateInvoiceLinePanelModel();
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
        public JsonResult RunCallout(int C_Invoice_ID, string TriggerColumn, int M_Product_ID,
            int C_Charge_ID, int M_AttributeSetInstance_ID, decimal QtyEntered, int C_UOM_ID,
            decimal PriceEntered, bool PriceOverride, int C_Tax_ID, decimal Discount)
        {
            string retJSON = "";
            if (Session["ctx"] != null)
            {
                Ctx ctx = Session["ctx"] as Ctx;
                LineCalcRequest req = new LineCalcRequest
                {
                    C_Invoice_ID = C_Invoice_ID,
                    TriggerColumn = TriggerColumn,
                    M_Product_ID = M_Product_ID,
                    C_Charge_ID = C_Charge_ID,
                    M_AttributeSetInstance_ID = M_AttributeSetInstance_ID,
                    QtyEntered = QtyEntered,
                    C_UOM_ID = C_UOM_ID,
                    PriceEntered = PriceEntered,
                    PriceOverride = PriceOverride,
                    C_Tax_ID = C_Tax_ID,
                    Discount = Discount
                };
                VAS_074_CreateInvoiceLinePanelModel model = new VAS_074_CreateInvoiceLinePanelModel();
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
                VAS_074_CreateInvoiceLinePanelModel model = new VAS_074_CreateInvoiceLinePanelModel();
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
                VAS_074_CreateInvoiceLinePanelModel model = new VAS_074_CreateInvoiceLinePanelModel();
                retJSON = JsonConvert.SerializeObject(model.GetInstanceValues(ctx, M_AttributeSetInstance_ID));
            }
            return Json(retJSON, JsonRequestBehavior.AllowGet);
        }

        /// <summary>Looks up a product / charge by a scanned barcode.</summary>
        /// <param name="C_Invoice_ID">parent invoice (client scope)</param>
        /// <param name="code">scanned code</param>
        /// <returns>serialized matched catalog row</returns>
        [AjaxAuthorizeAttribute]
        [AjaxSessionFilterAttribute]
        public JsonResult ScanLookup(int C_Invoice_ID, string code)
        {
            string retJSON = "";
            if (Session["ctx"] != null)
            {
                Ctx ctx = Session["ctx"] as Ctx;
                VAS_074_CreateInvoiceLinePanelModel model = new VAS_074_CreateInvoiceLinePanelModel();
                retJSON = JsonConvert.SerializeObject(model.ScanLookup(ctx, C_Invoice_ID, code));
            }
            return Json(retJSON, JsonRequestBehavior.AllowGet);
        }

        /// <summary>
        /// Creates (or reuses) an M_AttributeSetInstance from the picker selection
        /// by delegating to the framework's <see cref="PAttributesModel.SaveAttribute"/>
        /// (VIS.dll) so the dedup, mandatory-validation and AttrCode/UPC behaviour
        /// stays identical to the standard ASI control. The picker selection is
        /// translated into the positional <c>List&lt;KeyNamePair&gt;</c> that method
        /// expects (one entry per instance attribute, in M_AttributeSet order).
        /// </summary>
        /// <param name="payload">serialized AttributeSaveRequest</param>
        /// <returns>serialized new instance id + description (id 0 + Error on failure)</returns>
        [HttpPost]
        [AjaxAuthorizeAttribute]
        [AjaxSessionFilterAttribute]
        public JsonResult SaveAttribute(string payload)
        {
            string retJSON = "";
            if (Session["ctx"] != null && !string.IsNullOrEmpty(payload))
            {
                Ctx ctx = Session["ctx"] as Ctx;
                AttributeSaveRequest req = JsonConvert.DeserializeObject<AttributeSaveRequest>(payload);
                AttributeSaveResult res = new AttributeSaveResult();

                MProduct product = req != null && req.M_Product_ID > 0 ? MProduct.Get(ctx, req.M_Product_ID) : null;
                if (product != null && product.GetM_AttributeSet_ID() > 0)
                {
                    // Build the positional value list the framework indexes by attribute
                    // order (aset.GetMAttributes(true), i.e. instance attributes ordered
                    // by SeqNo). Every attribute must have an entry or the framework's
                    // unchecked editors[i] access goes out of range.
                    MAttributeSet aset = MAttributeSet.Get(ctx, product.GetM_AttributeSet_ID());
                    List<KeyNamePair> values = BuildAttributeValues(aset, req.Values);

                    bool isEdited = req.M_AttributeSetInstance_ID > 0;
                    AttributeInstance fres = new PAttributesModel().SaveAttribute(
                        0, req.Lot, req.SerNo, req.GuaranteeDate, "",
                        false, req.M_AttributeSetInstance_ID, req.M_Product_ID, 0,
                        "", isEdited, values, ctx);

                    if (fres != null)
                    {
                        if (string.IsNullOrEmpty(fres.Error))
                        {
                            res.M_AttributeSetInstance_ID = fres.M_AttributeSetInstance_ID;
                            res.Description = fres.M_AttributeSetInstanceName;
                        }
                        else
                        {
                            res.Error = fres.Error;
                        }
                    }
                }

                retJSON = JsonConvert.SerializeObject(res);
            }
            return Json(retJSON, JsonRequestBehavior.AllowGet);
        }

        /// <summary>
        /// Maps the picker selection onto the positional <c>List&lt;KeyNamePair&gt;</c>
        /// expected by <see cref="PAttributesModel.SaveAttribute"/>: one entry per
        /// instance attribute, in the same order as <c>aset.GetMAttributes(true)</c>,
        /// typed by the attribute's own value type (list / number / string). A missing
        /// selection yields an empty placeholder so positional indexing stays aligned
        /// (and the framework's mandatory check still fires).
        /// </summary>
        private List<KeyNamePair> BuildAttributeValues(MAttributeSet aset, List<AttributeValueSelection> selections)
        {
            List<KeyNamePair> values = new List<KeyNamePair>();
            if (aset == null) return values;

            // Index the incoming selections by attribute for positional lookup.
            Dictionary<int, AttributeValueSelection> byAttr = new Dictionary<int, AttributeValueSelection>();
            if (selections != null)
            {
                foreach (AttributeValueSelection sel in selections)
                {
                    if (sel != null && sel.M_Attribute_ID > 0)
                        byAttr[sel.M_Attribute_ID] = sel;
                }
            }

            MAttribute[] attributes = aset.GetMAttributes(true);
            foreach (MAttribute attr in attributes)
            {
                AttributeValueSelection sel;
                byAttr.TryGetValue(attr.Get_ID(), out sel);

                if (MAttribute.ATTRIBUTEVALUETYPE_List.Equals(attr.GetAttributeValueType()))
                {
                    // List: Key = M_AttributeValue_ID, Name = display label.
                    int valId = sel != null ? sel.M_AttributeValue_ID : 0;
                    string label = sel != null ? sel.DisplayValue : "";
                    values.Add(new KeyNamePair(valId, label));
                }
                else if (MAttribute.ATTRIBUTEVALUETYPE_Number.Equals(attr.GetAttributeValueType()))
                {
                    // Number: Name = numeric string ("0" when blank avoids the
                    // framework's Convert.ToDecimal on an empty string).
                    string num = sel != null && sel.NumberValue.HasValue
                        ? sel.NumberValue.Value.ToString(System.Globalization.CultureInfo.InvariantCulture)
                        : "0";
                    values.Add(new KeyNamePair(0, num));
                }
                else
                {
                    // String/text: Name = raw value.
                    values.Add(new KeyNamePair(0, sel != null ? (sel.StringValue ?? "") : ""));
                }
            }
            return values;
        }

        /// <summary>Inserts / updates the supplied invoice lines through MInvoiceLine.</summary>
        /// <param name="payload">serialized SaveLinesRequest</param>
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
                SaveLinesRequest req = JsonConvert.DeserializeObject<SaveLinesRequest>(payload);
                VAS_074_CreateInvoiceLinePanelModel model = new VAS_074_CreateInvoiceLinePanelModel();
                retJSON = JsonConvert.SerializeObject(model.SaveLines(ctx, req.C_Invoice_ID, req.AD_Window_ID, req.Lines, req.Page));
            }
            return Json(retJSON, JsonRequestBehavior.AllowGet);
        }

        /// <summary>Deletes the supplied saved invoice lines through MInvoiceLine.</summary>
        /// <param name="payload">serialized DeleteLinesRequest</param>
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
                DeleteLinesRequest req = JsonConvert.DeserializeObject<DeleteLinesRequest>(payload);
                VAS_074_CreateInvoiceLinePanelModel model = new VAS_074_CreateInvoiceLinePanelModel();
                retJSON = JsonConvert.SerializeObject(model.DeleteLines(ctx, req.C_Invoice_ID, req.AD_Window_ID, req.LineIds, req.Page));
            }
            return Json(retJSON, JsonRequestBehavior.AllowGet);
        }
    }
}
