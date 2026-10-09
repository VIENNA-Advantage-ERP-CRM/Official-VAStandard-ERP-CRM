/// <summary>
/// Module Name : VAS
/// Purpose     : Purchase Requisition overview tab panel endpoint. Returns the
///               read-only overview payload for the selected requisition
///               (M_Requisition) consumed by the
///               VAS.VAS_098_PurchaseRequisition tab panel.
/// Chronological development:
///   VAI163   2026-07-01  Created
///   VAI163   2026-07-28  Added the panel's three convert actions
///                        (ConvertToPurchaseOrder / CreateRFQ /
///                        ConvertToMaterialTransfer). Each resolves its
///                        AD_Process at runtime — by Classname first, then by
///                        Value — and runs it through the standard process
///                        engine; nothing is hardcoded and an unresolved process
///                        reports back instead of doing something arbitrary.
///   VAI163   2026-08-12  Added GetWindow_ID: resolves a window by NAME for the
///                        panel's record-open path, so a Reference chip or a
///                        Documents row whose screen is not the table's default
///                        zoom target can still be opened. Ported from VAS_092.
///   VAI163   2026-08-12  - Added GetWindowIdByTable, the last resort behind it:
///                          the window a TABLE opens in, read from the dictionary.
///                          The VA075 work order and field service request chips
///                          need it — that module is not part of this solution, so
///                          its screens cannot be named here. Ported from VAS_102.
///                        - Removed CreateRFQ and ConvertToMaterialTransfer with
///                          the panel buttons that called them; neither could
///                          succeed from a panel with no parameter screen (see the
///                          note where they were).
///   VAI163   2026-08-12  Removed ConvertToPurchaseOrder too, with the panel's
///                        whole convert strip, and the helpers that served only
///                        the three endpoints (CheckCompleted, RunProcess,
///                        ResolveProcess, LookupProcess, ReqParam, Ok, Fail) plus
///                        the usings they needed. The controller is read-only
///                        again, as it was when it was created.
/// </summary>

using Newtonsoft.Json;
using System.Web.Mvc;
using VAdvantage.Utility;
using VASLogic.Models;

namespace VAS.Controllers
{
    public class VAS_098_PurchaseRequisitionController : Controller
    {
        public ActionResult Index()
        {
            return View();
        }

        /// <summary>
        /// Returns the overview details for the selected M_Requisition record.
        /// </summary>
        /// <param name="M_Requisition_ID">Selected requisition id.</param>
        /// <returns>JSON-serialized <see cref="VAS_098_PurchaseRequisitionModel.RequisitionOverviewData"/>.</returns>
        public JsonResult GetRequisitionOverview(int M_Requisition_ID)
        {
            string retJSON = "";
            if (Session["ctx"] != null)
            {
                Ctx ctx = Session["ctx"] as Ctx;
                VAS_098_PurchaseRequisitionModel model = new VAS_098_PurchaseRequisitionModel();
                retJSON = JsonConvert.SerializeObject(
                    model.GetRequisitionOverview(ctx, M_Requisition_ID));
            }
            return Json(retJSON, JsonRequestBehavior.AllowGet);
        }

        /// <summary>
        /// Resolves a window's AD_Window_ID from its name, for the panel's
        /// record-open path. Used where the record's screen is not the table's
        /// default zoom target — an RFQ opens the VAS_RFQ window, not whatever
        /// C_RfQ's zoom target resolves to.
        /// </summary>
        /// <param name="fields">Window name (AD_Window.Name), as sent by
        /// VIS.dataContext.getJSONRecord.</param>
        /// <returns>The window id, or 0 when the name is unknown to this client.</returns>
        public JsonResult GetWindow_ID(string fields)
        {
            int windowId = 0;
            if (Session["ctx"] != null)
            {
                Ctx ctx = Session["ctx"] as Ctx;
                VAS_098_PurchaseRequisitionModel model = new VAS_098_PurchaseRequisitionModel();
                windowId = model.GetWindowId(ctx, fields);
            }
            return Json(JsonConvert.SerializeObject(windowId), JsonRequestBehavior.AllowGet);
        }

        /// <summary>
        /// Resolves the window a TABLE's records open in, for a Reference chip whose
        /// screen cannot be named on the client: VA075 is not part of this solution,
        /// so its work-order and field-service-request windows have no name that can
        /// be hard-coded, and the browser-side zoom lookup only knows tables the
        /// client has cached.
        /// </summary>
        /// <param name="fields">Physical table name, as sent by
        /// VIS.dataContext.getJSONRecord.</param>
        /// <returns>The window id, or 0 when the table has no window.</returns>
        public JsonResult GetWindowIdByTable(string fields)
        {
            int windowId = 0;
            if (Session["ctx"] != null)
            {
                Ctx ctx = Session["ctx"] as Ctx;
                VAS_098_PurchaseRequisitionModel model = new VAS_098_PurchaseRequisitionModel();
                windowId = model.GetWindowIdByTable(ctx, fields);
            }
            return Json(JsonConvert.SerializeObject(windowId), JsonRequestBehavior.AllowGet);
        }

        // --------------------------------------------------------------------- //
        //  Convert actions (removed)                                            //
        // --------------------------------------------------------------------- //

        // ConvertToPurchaseOrder, CreateRFQ and ConvertToMaterialTransfer are all
        // gone, with the panel's convert strip that called them — and with them
        // the helpers that served only those endpoints (CheckCompleted,
        // RunProcess, ResolveProcess, LookupProcess, ReqParam, Ok, Fail) and the
        // usings they needed.
        //
        // Two of the three could never have succeeded from a panel with no
        // parameter screen: CreateRFQFromRequisition selects its lines by an
        // ORGANISATION parameter and builds the RFQ from a topic, quote type and
        // response date, and the material-transfer process ships with the DTD001
        // module, which is not part of this solution. The purchase order action
        // did work, but it belongs with them on the requisition window, where the
        // process runs behind its own parameter screen and the document's full
        // validation.
        //
        // The controller is read-only again, as it was when it was created.
        // VAS_099 went the same way.
    }
}
