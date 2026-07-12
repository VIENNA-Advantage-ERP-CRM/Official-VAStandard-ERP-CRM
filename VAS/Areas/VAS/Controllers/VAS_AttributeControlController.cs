/******************************************************
 * Module Name    : VAS
 * Purpose        : Shared AJAX endpoints for the reusable product Attribute
 *                  (M_AttributeSetInstance) picker control
 *                  (Scripts/app/util/AttributeControl.js). Any panel can call
 *                  these instead of wiring its own attribute actions:
 *                    - GetProductAttributes : attribute-set definition for a product
 *                    - GetInstanceValues    : stored values of an existing instance
 *                    - SaveAttribute        : create / reuse an instance (via the
 *                                             framework PAttributesModel)
 *                  The two reads reuse the generic, product-based model methods on
 *                  VAS_074_CreateInvoiceLinePanelModel; SaveAttribute mirrors that
 *                  panel's action (translate the picker selection into the positional
 *                  KeyNamePair list PAttributesModel.SaveAttribute expects).
 * chronological  : Development
 *   VAI_XXX        Created  30 June 2026
 ******************************************************/
// NOTE: Replace VAI_XXX with your own Employee Code before committing.

using Newtonsoft.Json;
using System.Collections.Generic;
using System.Web.Mvc;
using System.Web.SessionState;
using VAdvantage.Model;
using VAdvantage.Utility;
using VASLogic.Models;
using VIS.Filters;
using VIS.Models;

namespace VAS.Controllers
{
    /// <summary>
    /// Shared backend for <c>VIS.AttributeControl</c>. Stateless: each action reads the
    /// session Ctx and returns the serialized result. Reads use GET; SaveAttribute is POST.
    /// </summary>
    /// <remarks>
    /// ReadOnly session state: every action only READS Session["ctx"] (never writes it), so
    /// the controller takes a shared reader lock instead of ASP.NET's default exclusive writer
    /// lock. Without it, an in-flight line save (or any other ReadWrite request in the session)
    /// serializes behind the exclusive lock and blocks the picker's GetProductAttributes /
    /// GetInstanceValues while it runs. Same pattern as VAS_074_CreateInvoiceLinePanelController
    /// and CalloutJsonDataController; SaveAttribute's DB writes stay protected by their own Trx.
    /// </remarks>
    [SessionState(SessionStateBehavior.ReadOnly)]
    public class VAS_AttributeControlController : Controller
    {
        /// <summary>Returns a product's attribute-set definition (instance attributes + values).</summary>
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

        /// <summary>
        /// Creates (or reuses) an M_AttributeSetInstance from the picker selection by
        /// delegating to the framework's <see cref="PAttributesModel.SaveAttribute"/>
        /// (VIS.dll), so dedup, mandatory validation and AttrCode/UPC behaviour match the
        /// standard ASI control. The selection is translated into the positional
        /// <c>List&lt;KeyNamePair&gt;</c> that method expects (one entry per instance
        /// attribute, in M_AttributeSet order).
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
                    // Positional value list indexed by attribute order (aset.GetMAttributes(true),
                    // instance attributes by SeqNo). Every attribute needs an entry or the
                    // framework's unchecked editors[i] access goes out of range.
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
        /// expected by <see cref="PAttributesModel.SaveAttribute"/>: one entry per instance
        /// attribute, in the same order as <c>aset.GetMAttributes(true)</c>, typed by the
        /// attribute's own value type. A missing selection yields an empty placeholder so
        /// positional indexing stays aligned (and the framework's mandatory check still fires).
        /// </summary>
        private List<KeyNamePair> BuildAttributeValues(MAttributeSet aset, List<AttributeValueSelection> selections)
        {
            List<KeyNamePair> values = new List<KeyNamePair>();
            if (aset == null) return values;

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
                    int valId = sel != null ? sel.M_AttributeValue_ID : 0;
                    string label = sel != null ? sel.DisplayValue : "";
                    values.Add(new KeyNamePair(valId, label));
                }
                else if (MAttribute.ATTRIBUTEVALUETYPE_Number.Equals(attr.GetAttributeValueType()))
                {
                    string num = sel != null && sel.NumberValue.HasValue
                        ? sel.NumberValue.Value.ToString(System.Globalization.CultureInfo.InvariantCulture)
                        : "0";
                    values.Add(new KeyNamePair(0, num));
                }
                else
                {
                    values.Add(new KeyNamePair(0, sel != null ? (sel.StringValue ?? "") : ""));
                }
            }
            return values;
        }
    }
}
