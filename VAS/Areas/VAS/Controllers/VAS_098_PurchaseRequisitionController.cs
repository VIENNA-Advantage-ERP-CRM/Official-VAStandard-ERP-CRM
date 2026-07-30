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
/// </summary>

using Newtonsoft.Json;
using System;
using System.Data.SqlClient;
using System.Web.Mvc;
using VAdvantage.DataBase;
using VAdvantage.Model;
using VAdvantage.Process;
using VAdvantage.ProcessEngine;
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

        // --------------------------------------------------------------------- //
        //  Convert actions                                                      //
        // --------------------------------------------------------------------- //

        /// <summary>
        /// Creates the purchase order(s) for this requisition by running the core
        /// RequisitionPOCreate process. The requisition must be Completed — the
        /// process itself enforces that too — and only lines not already linked to
        /// an order line are converted.
        /// </summary>
        /// <param name="M_Requisition_ID">Selected requisition id.</param>
        [HttpPost]
        public JsonResult ConvertToPurchaseOrder(int M_Requisition_ID)
        {
            if (Session["ctx"] == null) return Fail("Session expired. Please sign in again.");
            Ctx ctx = Session["ctx"] as Ctx;
            try
            {
                string guard = CheckCompleted(ctx, M_Requisition_ID);
                if (guard != null) return Fail(guard);

                int processId = ResolveProcess(ctx,
                    new string[] { "VAdvantage.Process.RequisitionPOCreate", "RequisitionPOCreate" },
                    new string[] { "M_RequisitionPO", "RequisitionPOCreate" });
                if (processId <= 0)
                    return Fail("The 'Create PO from Requisition' process is not available on this system.");

                return RunProcess(ctx, processId, M_Requisition_ID,
                    new string[] { "ConsolidateDocument" }, new object[] { "N" },
                    "Purchase order created from this requisition.");
            }
            catch (Exception ex)
            {
                return Fail(ex.Message);
            }
        }

        /// <summary>
        /// Raises an RFQ from this requisition through the core
        /// CreateRFQFromRequisition process.
        ///
        /// That process also takes RFQ topic / quote type / response date. They are
        /// deliberately NOT invented here — only the requisition is passed, and if
        /// the process needs more it reports its own validation message back to the
        /// panel rather than an RFQ being created with made-up terms.
        /// </summary>
        /// <param name="M_Requisition_ID">Selected requisition id.</param>
        [HttpPost]
        public JsonResult CreateRFQ(int M_Requisition_ID)
        {
            if (Session["ctx"] == null) return Fail("Session expired. Please sign in again.");
            Ctx ctx = Session["ctx"] as Ctx;
            try
            {
                string guard = CheckCompleted(ctx, M_Requisition_ID);
                if (guard != null) return Fail(guard);

                int processId = ResolveProcess(ctx,
                    new string[] { "VAdvantage.Process.CreateRFQFromRequisition", "CreateRFQFromRequisition" },
                    new string[] { "CreateRFQFromRequisition", "M_RequisitionRfQ" });
                if (processId <= 0)
                    return Fail("The 'Create RFQ from Requisition' process is not available on this system.");

                return RunProcess(ctx, processId, M_Requisition_ID, null, null,
                    "RFQ created from this requisition.");
            }
            catch (Exception ex)
            {
                return Fail(ex.Message);
            }
        }

        /// <summary>
        /// Converts the requisition into a material transfer. This action belongs to
        /// the DTD001 distribution module, which is not part of this repository, so
        /// the process is resolved by name at runtime and the action reports back
        /// cleanly when that module is not installed.
        /// </summary>
        /// <param name="M_Requisition_ID">Selected requisition id.</param>
        [HttpPost]
        public JsonResult ConvertToMaterialTransfer(int M_Requisition_ID)
        {
            if (Session["ctx"] == null) return Fail("Session expired. Please sign in again.");
            Ctx ctx = Session["ctx"] as Ctx;
            try
            {
                string guard = CheckCompleted(ctx, M_Requisition_ID);
                if (guard != null) return Fail(guard);

                int processId = ResolveProcess(ctx,
                    new string[]
                    {
                        "VAdvantage.Process.DTD001_CreateMovementFromRequisition",
                        "VAdvantage.Process.CreateMovementFromRequisition",
                        "DTD001_CreateMovementFromRequisition"
                    },
                    new string[]
                    {
                        "DTD001_CreateMovementFromRequisition",
                        "DTD001_RequisitionMovement",
                        "M_RequisitionMovement"
                    });
                if (processId <= 0)
                {
                    return Fail("The 'Convert to Material Transfer' process is not available on this " +
                                "system. It ships with the DTD001 distribution module — run the action " +
                                "from the requisition window toolbar instead.");
                }

                return RunProcess(ctx, processId, M_Requisition_ID, null, null,
                    "Material transfer created from this requisition.");
            }
            catch (Exception ex)
            {
                return Fail(ex.Message);
            }
        }

        // --------------------------------------------------------------------- //
        //  Helpers                                                              //
        // --------------------------------------------------------------------- //

        /// <summary>
        /// Returns an error message when the requisition cannot be converted, or
        /// null when it is good to go.
        /// </summary>
        private string CheckCompleted(Ctx ctx, int M_Requisition_ID)
        {
            if (M_Requisition_ID <= 0) return "Invalid requisition.";

            string docStatus = Util.GetValueOfString(DB.ExecuteScalar(
                "SELECT DocStatus FROM M_Requisition WHERE M_Requisition_ID=@M_Requisition_ID",
                ReqParam(M_Requisition_ID), null));

            if (string.IsNullOrEmpty(docStatus)) return "Requisition not found.";
            if (docStatus == "VO" || docStatus == "RE")
                return "This requisition is voided / reversed and cannot be converted.";
            if (docStatus != "CO" && docStatus != "CL")
                return "Complete the requisition before converting it.";
            return null;
        }

        /// <summary>
        /// Runs an AD_Process for the requisition through the standard process
        /// engine, passing M_Requisition_ID plus any extra parameters.
        /// </summary>
        private JsonResult RunProcess(Ctx ctx, int processId, int M_Requisition_ID,
                                      string[] extraNames, object[] extraValues,
                                      string okMessage)
        {
            MPInstance instance = new MPInstance(ctx, processId, 0);
            if (!instance.Save()) return Fail("Could not start the process.");

            ProcessInfo pi = new ProcessInfo("", processId);
            pi.SetAD_PInstance_ID(instance.GetAD_PInstance_ID());
            pi.SetAD_Client_ID(ctx.GetAD_Client_ID());
            pi.SetRecord_ID(M_Requisition_ID);

            int seq = 10;
            MPInstancePara p = new MPInstancePara(instance, seq);
            p.setParameter("M_Requisition_ID", M_Requisition_ID.ToString());
            if (!p.Save()) return Fail("Could not prepare the process (Requisition).");

            if (extraNames != null)
            {
                for (int i = 0; i < extraNames.Length; i++)
                {
                    seq += 10;
                    MPInstancePara ep = new MPInstancePara(instance, seq);
                    ep.setParameter(extraNames[i], Util.GetValueOfString(extraValues[i]));
                    if (!ep.Save()) return Fail("Could not prepare the process (" + extraNames[i] + ").");
                }
            }

            ProcessCtl worker = new ProcessCtl(ctx, null, pi, null);
            worker.Run();

            if (pi.IsError())
            {
                string summary = pi.GetSummary();
                return Fail(string.IsNullOrEmpty(summary) ? "The process failed." : summary);
            }

            string resultSummary = pi.GetSummary();
            return Ok(new
            {
                success = true,
                message = string.IsNullOrEmpty(resultSummary) ? okMessage : resultSummary
            });
        }

        /// <summary>
        /// Resolves an AD_Process_ID, trying each Classname first (the reliable key
        /// for a process implemented in code) and then each Value. A client-specific
        /// record wins over the system one. Returns 0 when nothing matches.
        /// </summary>
        private int ResolveProcess(Ctx ctx, string[] classNames, string[] values)
        {
            for (int i = 0; i < classNames.Length; i++)
            {
                int id = LookupProcess(ctx, "Classname", classNames[i]);
                if (id > 0) return id;
            }
            for (int i = 0; i < values.Length; i++)
            {
                int id = LookupProcess(ctx, "Value", values[i]);
                if (id > 0) return id;
            }
            return 0;
        }

        /// <summary>Single AD_Process lookup on one column (0 when not found).</summary>
        private int LookupProcess(Ctx ctx, string columnName, string columnValue)
        {
            try
            {
                // columnName is a literal from this file's own call sites, never
                // user input; columnValue is bound.
                string sql = @"SELECT AD_Process_ID FROM AD_Process
                                WHERE UPPER(" + columnName + @") = UPPER(@Value)
                                  AND IsActive='Y'
                                  AND AD_Client_ID IN (0, @AD_Client_ID)
                                ORDER BY AD_Client_ID DESC FETCH FIRST 1 ROW ONLY";
                return Util.GetValueOfInt(DB.ExecuteScalar(sql, new SqlParameter[]
                {
                    new SqlParameter("@Value", columnValue),
                    new SqlParameter("@AD_Client_ID", ctx.GetAD_Client_ID())
                }, null));
            }
            catch { return 0; }
        }

        private SqlParameter[] ReqParam(int M_Requisition_ID)
        {
            return new SqlParameter[] { new SqlParameter("@M_Requisition_ID", M_Requisition_ID) };
        }

        /// <summary>Serializes a success payload.</summary>
        private JsonResult Ok(object result)
        {
            return Json(JsonConvert.SerializeObject(result), JsonRequestBehavior.AllowGet);
        }

        /// <summary>Serializes a failure message (success:false).</summary>
        private JsonResult Fail(string message)
        {
            return Json(JsonConvert.SerializeObject(new
            {
                success = false,
                error = message,
                message = message
            }), JsonRequestBehavior.AllowGet);
        }
    }
}
