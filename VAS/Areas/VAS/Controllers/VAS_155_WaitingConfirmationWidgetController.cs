using Newtonsoft.Json;
using System;
using System.Collections.Generic;
using System.Data;
using System.Data.SqlClient;
using System.Globalization;
using System.Web.Mvc;
using VAdvantage.DataBase;
using VAdvantage.Model;
using VAdvantage.Utility;
using VIS.Filters;

namespace VAS.Controllers
{
    /// <summary>
    /// Module Name : VAS_155_WaitingConfirmationWidget (Delivery Order dashboard)
    /// Purpose     : Data endpoints for the 3x2 "Waiting Confirmation" widget - a
    ///               worklist of drafted/in-progress CUSTOMER delivery confirmations
    ///               (M_InOutConfirm, joined to M_InOut with IsSOTrx='Y' AND
    ///               MovementType='C-' so vendor/receiving confirmations never
    ///               appear here), with a line-review-and-save flow, a Mark In
    ///               Dispute action (IsInDispute='Y' only, never touches DocStatus),
    ///               and a Complete Confirmation action that goes through the
    ///               standard document engine (MInOutConfirm.ProcessIt with
    ///               DOCACTION_Complete) rather than a raw DocStatus UPDATE - the
    ///               same pattern already proven by
    ///               VAS_090_ReceivingActionsWidgetController's GRN confirmation
    ///               flow (CompleteGRNConfirmation/SaveGRNConfirmationLine), just
    ///               re-scoped from the vendor-receiving side (IsSOTrx='N',
    ///               MovementType='V+') to the customer-delivery side. Target
    ///               Quantity is read-only from this widget by design - only
    ///               Scrap Locator, Confirmed Qty, Scrapped Qty and Description are
    ///               saved. MRole is applied to the primary fetched table
    ///               (M_InOutConfirm) on every read; all input is parameterized;
    ///               the SQL uses only COALESCE / CASE / IN (no NVL, DECODE,
    ///               TRUNC, TO_CHAR, LIMIT/OFFSET/FETCH/ROWNUM pagination - paging
    ///               is done in C# over the full open queue, per spec), so it runs
    ///               unchanged on Oracle and PostgreSQL.
    /// Widget number 155.
    /// Chronological development:
    ///   &lt;EmpCode&gt;   2026-07-21 Created
    /// </summary>
    public class VAS_155_WaitingConfirmationWidgetController : Controller
    {
        /// <summary>"dispute" / "drafted" / "inProgress" - the JS maps this to display text via message keys.</summary>
        private static string ConfirmationStatus(string docStatus, string inDispute)
        {
            if (inDispute == "Y") { return "dispute"; }
            if (docStatus == "IP") { return "inProgress"; }
            return "drafted";
        }

        /// <summary>
        /// The open (DR/IP) customer delivery-confirmation queue, newest first.
        /// Fetches the full queue and paginates in C# (3 per page per spec) - no
        /// DB-specific pagination syntax, matching VAS_090's GRN confirmation list.
        /// </summary>
        /// <returns>JSON { rows[] }.</returns>
        [AjaxAuthorizeAttribute]
        [AjaxSessionFilterAttribute]
        public JsonResult GetWaitingConfirmations()
        {
            if (Session["ctx"] == null)
            {
                return Fail(Msg.GetMsg(Env.GetCtx(), "SessionExpired") ?? "Session Expired");
            }

            Ctx ctx = Session["ctx"] as Ctx;

            string sql = @"
                SELECT Confirm.M_InOutConfirm_ID AS Confirm_ID,
                       Confirm.DocumentNo AS Confirm_No,
                       Confirm.DocStatus AS Doc_Status,
                       COALESCE(CAST(Confirm.IsInDispute AS VARCHAR(1)),'N') AS In_Dispute,
                       Confirm.Created AS Created_Date,
                       InOut.DocumentNo AS DO_No,
                       BPartner.Name AS Customer_Name,
                       __LINE_COUNT__ AS Line_Count
                FROM M_InOutConfirm Confirm
                INNER JOIN M_InOut InOut ON (InOut.M_InOut_ID=Confirm.M_InOut_ID AND InOut.IsActive='Y')
                INNER JOIN C_BPartner BPartner ON (BPartner.C_BPartner_ID=InOut.C_BPartner_ID AND BPartner.IsActive='Y')
                WHERE Confirm.IsActive='Y'
                  AND Confirm.ConfirmType='SC'
                  AND InOut.IsSOTrx='Y'
                  AND InOut.MovementType='C-'
                  AND Confirm.AD_Client_ID=@AD_Client_ID
                  AND Confirm.DocStatus IN ('DR','IP')";

            // The line-count subquery is injected AFTER AddAccessSQL: with
            // SQL_FULLYQUALIFIED it otherwise appends a predicate for the subquery's
            // LineConfirm alias to the OUTER where-clause, where it is out of scope
            // (ORA-00904 -> the widget shows "Unable to load confirmations").
            sql = MRole.GetDefault(ctx).AddAccessSQL(sql, "Confirm", MRole.SQL_FULLYQUALIFIED, MRole.SQL_RO);
            sql = sql.Replace("__LINE_COUNT__", @"(
                           SELECT COUNT(*)
                           FROM M_InOutLineConfirm LineConfirm
                           WHERE LineConfirm.M_InOutConfirm_ID = Confirm.M_InOutConfirm_ID
                             AND LineConfirm.IsActive = 'Y'
                       )");
            sql += @"
                ORDER BY Confirm.Created DESC, Confirm.M_InOutConfirm_ID DESC";

            List<object> rows = new List<object>();
            IDataReader dr = null;

            try
            {
                dr = DB.ExecuteReader(sql, new SqlParameter[]
                {
                    new SqlParameter("@AD_Client_ID", ctx.GetAD_Client_ID())
                });

                while (dr != null && dr.Read())
                {
                    rows.Add(new
                    {
                        confirmId = Util.GetValueOfInt(dr["Confirm_ID"]),
                        confirmNo = Util.GetValueOfString(dr["Confirm_No"]),
                        doNo = Util.GetValueOfString(dr["DO_No"]),
                        customer = Util.GetValueOfString(dr["Customer_Name"]),
                        lineCount = Util.GetValueOfInt(dr["Line_Count"]),
                        status = ConfirmationStatus(Util.GetValueOfString(dr["Doc_Status"]), Util.GetValueOfString(dr["In_Dispute"]))
                    });
                }

                return Ok(new { rows = rows });
            }
            catch (Exception ex)
            {
                return Fail(ex.Message);
            }
            finally
            {
                if (dr != null) { dr.Close(); dr.Dispose(); }
            }
        }

        /// <summary>
        /// One confirmation's header context and its lines for the detail modal.
        /// </summary>
        /// <param name="confirmId">M_InOutConfirm_ID.</param>
        /// <returns>JSON { header, lines[] }.</returns>
        [AjaxAuthorizeAttribute]
        [AjaxSessionFilterAttribute]
        public JsonResult GetConfirmationDetail(int confirmId = 0)
        {
            if (Session["ctx"] == null)
            {
                return Fail(Msg.GetMsg(Env.GetCtx(), "SessionExpired") ?? "Session Expired");
            }

            Ctx ctx = Session["ctx"] as Ctx;
            if (confirmId <= 0) { return Fail(Msg.GetMsg(ctx, "NotFound") ?? "Not found"); }

            string headerSql = @"
                SELECT Confirm.M_InOutConfirm_ID AS Confirm_ID,
                       Confirm.DocumentNo AS Confirm_No,
                       Confirm.DocStatus AS Doc_Status,
                       COALESCE(CAST(Confirm.IsInDispute AS VARCHAR(1)),'N') AS In_Dispute,
                       Confirm.Created AS Confirm_Date,
                       InOut.M_InOut_ID AS DO_ID,
                       InOut.DocumentNo AS DO_No,
                       InOut.M_Warehouse_ID AS Warehouse_ID,
                       BPartner.Name AS Customer_Name,
                       Warehouse.Name AS Warehouse_Name
                FROM M_InOutConfirm Confirm
                INNER JOIN M_InOut InOut ON (InOut.M_InOut_ID=Confirm.M_InOut_ID AND InOut.IsActive='Y')
                INNER JOIN C_BPartner BPartner ON (BPartner.C_BPartner_ID=InOut.C_BPartner_ID AND BPartner.IsActive='Y')
                LEFT OUTER JOIN M_Warehouse Warehouse ON (Warehouse.M_Warehouse_ID=InOut.M_Warehouse_ID AND Warehouse.IsActive='Y')
                WHERE Confirm.IsActive='Y'
                  AND Confirm.M_InOutConfirm_ID=@Confirm_ID
                  AND Confirm.AD_Client_ID=@AD_Client_ID1";

            headerSql = MRole.GetDefault(ctx).AddAccessSQL(headerSql, "Confirm", MRole.SQL_FULLYQUALIFIED, MRole.SQL_RO);

            string linesSql = @"
                SELECT LineConfirm.M_InOutLineConfirm_ID AS Line_Confirm_ID,
                       InOutLine.Line AS Line_No,
                       Product.Name AS Product_Name,
                       COALESCE(UomInfo.UOMSymbol, UomInfo.Name) AS UOM_Name,
                       AttributeInstance.Description AS Attribute_Description,
                       LineConfirm.M_Locator_ID AS Scrap_Locator_ID,
                       ScrapLocator.Value AS Scrap_Locator_Value,
                       LineConfirm.TargetQty AS Target_Qty,
                       LineConfirm.ConfirmedQty AS Confirmed_Qty,
                       LineConfirm.ScrappedQty AS Scrapped_Qty,
                       LineConfirm.DifferenceQty AS Difference_Qty,
                       LineConfirm.Description AS Line_Description
                FROM M_InOutLineConfirm LineConfirm
                INNER JOIN M_InOutLine InOutLine ON (InOutLine.M_InOutLine_ID=LineConfirm.M_InOutLine_ID AND InOutLine.IsActive='Y')
                LEFT OUTER JOIN M_Product Product ON (Product.M_Product_ID=InOutLine.M_Product_ID AND Product.IsActive='Y')
                LEFT OUTER JOIN C_UOM UomInfo ON (UomInfo.C_UOM_ID=COALESCE(LineConfirm.C_UOM_ID, InOutLine.C_UOM_ID, Product.C_UOM_ID) AND UomInfo.IsActive='Y')
                LEFT OUTER JOIN M_AttributeSetInstance AttributeInstance ON (AttributeInstance.M_AttributeSetInstance_ID=InOutLine.M_AttributeSetInstance_ID)
                LEFT OUTER JOIN M_Locator ScrapLocator ON (ScrapLocator.M_Locator_ID=LineConfirm.M_Locator_ID AND ScrapLocator.IsActive='Y')
                WHERE LineConfirm.IsActive='Y'
                  AND LineConfirm.M_InOutConfirm_ID=@Confirm_ID2
                  AND LineConfirm.AD_Client_ID=@AD_Client_ID3";

            linesSql = MRole.GetDefault(ctx).AddAccessSQL(linesSql, "LineConfirm", MRole.SQL_FULLYQUALIFIED, MRole.SQL_RO);
            linesSql += @"
                ORDER BY InOutLine.Line, LineConfirm.M_InOutLineConfirm_ID";

            IDataReader dr = null;

            try
            {
                object header = null;
                dr = DB.ExecuteReader(headerSql, new SqlParameter[]
                {
                    new SqlParameter("@Confirm_ID", confirmId),
                    new SqlParameter("@AD_Client_ID1", ctx.GetAD_Client_ID())
                });

                if (dr != null && dr.Read())
                {
                    DateTime? confirmDate = Util.GetValueOfDateTime(dr["Confirm_Date"]);
                    header = new
                    {
                        confirmId = Util.GetValueOfInt(dr["Confirm_ID"]),
                        confirmNo = Util.GetValueOfString(dr["Confirm_No"]),
                        doId = Util.GetValueOfInt(dr["DO_ID"]),
                        doNo = Util.GetValueOfString(dr["DO_No"]),
                        warehouseId = Util.GetValueOfInt(dr["Warehouse_ID"]),
                        warehouseName = Util.GetValueOfString(dr["Warehouse_Name"]),
                        customer = Util.GetValueOfString(dr["Customer_Name"]),
                        confirmDate = confirmDate.HasValue ? confirmDate.Value.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture) : "",
                        status = ConfirmationStatus(Util.GetValueOfString(dr["Doc_Status"]), Util.GetValueOfString(dr["In_Dispute"]))
                    };
                }

                dr.Close();
                dr.Dispose();
                dr = null;

                if (header == null) { return Fail(Msg.GetMsg(ctx, "NotFound") ?? "Not found"); }

                List<object> lines = new List<object>();
                dr = DB.ExecuteReader(linesSql, new SqlParameter[]
                {
                    new SqlParameter("@Confirm_ID2", confirmId),
                    new SqlParameter("@AD_Client_ID3", ctx.GetAD_Client_ID())
                });

                while (dr != null && dr.Read())
                {
                    decimal target = Util.GetValueOfDecimal(dr["Target_Qty"]);
                    decimal confirmedQty = Util.GetValueOfDecimal(dr["Confirmed_Qty"]);
                    decimal scrappedQty = Util.GetValueOfDecimal(dr["Scrapped_Qty"]);
                    string attr = Util.GetValueOfString(dr["Attribute_Description"]);

                    lines.Add(new
                    {
                        lineConfirmId = Util.GetValueOfInt(dr["Line_Confirm_ID"]),
                        lineNo = Util.GetValueOfInt(dr["Line_No"]),
                        productName = Util.GetValueOfString(dr["Product_Name"]),
                        uomName = Util.GetValueOfString(dr["UOM_Name"]),
                        attributeSetInstance = string.IsNullOrEmpty(attr) ? null : attr,
                        scrapLocatorId = Util.GetValueOfInt(dr["Scrap_Locator_ID"]),
                        scrapLocatorValue = Util.GetValueOfString(dr["Scrap_Locator_Value"]),
                        targetQty = target,
                        confirmedQty = confirmedQty,
                        scrappedQty = scrappedQty,
                        differenceQty = Util.GetValueOfDecimal(dr["Difference_Qty"]),
                        description = Util.GetValueOfString(dr["Line_Description"]),
                        matched = (confirmedQty + scrappedQty) >= target
                    });
                }

                return Ok(new { header = header, lines = lines });
            }
            catch (Exception ex)
            {
                return Fail(ex.Message);
            }
            finally
            {
                if (dr != null) { dr.Close(); dr.Dispose(); }
            }
        }

        /// <summary>
        /// Active locators for one warehouse, for the Scrap Locator select in the
        /// line-review state. Restricted to the source delivery order's own
        /// warehouse per spec (M_Locator.M_Warehouse_ID = M_InOut.M_Warehouse_ID).
        /// </summary>
        /// <returns>JSON { rows[] }.</returns>
        [AjaxAuthorizeAttribute]
        [AjaxSessionFilterAttribute]
        public JsonResult GetWarehouseLocators(int warehouseId = 0)
        {
            if (Session["ctx"] == null)
            {
                return Fail(Msg.GetMsg(Env.GetCtx(), "SessionExpired") ?? "Session Expired");
            }

            Ctx ctx = Session["ctx"] as Ctx;
            if (warehouseId <= 0) { return Ok(new { rows = new List<object>() }); }

            string locatorNameSql = HasColumn("M_Locator", "LocatorCombination")
                ? "COALESCE(Locator.LocatorCombination, Locator.Value)"
                : "Locator.Value";

            string sql = @"
                SELECT Locator.M_Locator_ID AS Locator_ID,
                       " + locatorNameSql + @" AS Locator_Name,
                       Locator.IsDefault AS Is_Default
                FROM M_Locator Locator
                WHERE Locator.IsActive='Y'
                  AND Locator.AD_Client_ID=@AD_Client_ID
                  AND Locator.M_Warehouse_ID=@Warehouse_ID
                ORDER BY Locator.IsDefault DESC, " + locatorNameSql;

            List<object> rows = new List<object>();
            IDataReader dr = null;

            try
            {
                dr = DB.ExecuteReader(sql, new SqlParameter[]
                {
                    new SqlParameter("@AD_Client_ID", ctx.GetAD_Client_ID()),
                    new SqlParameter("@Warehouse_ID", warehouseId)
                });
                while (dr != null && dr.Read())
                {
                    rows.Add(new
                    {
                        locatorId = Util.GetValueOfInt(dr["Locator_ID"]),
                        locatorName = Util.GetValueOfString(dr["Locator_Name"]),
                        isDefault = Util.GetValueOfString(dr["Is_Default"]) == "Y"
                    });
                }

                return Ok(new { rows = rows });
            }
            catch (Exception ex)
            {
                return Fail(ex.Message);
            }
            finally
            {
                if (dr != null) { dr.Close(); dr.Dispose(); }
            }
        }

        /// <summary>
        /// Saves one confirmation line's editable fields (Scrap Locator, Confirmed
        /// Qty, Scrapped Qty, Description). Target Quantity is never accepted from
        /// this endpoint - it stays exactly as loaded, matching the spec's
        /// read-only rule. DifferenceQty is computed server-side
        /// (Target - Confirmed - Scrapped) and persisted alongside.
        /// </summary>
        [AjaxAuthorizeAttribute]
        [AjaxSessionFilterAttribute]
        [HttpPost]
        public JsonResult SaveConfirmationLine(int lineConfirmId = 0, int scrapLocatorId = 0, string confirmedQty = "", string scrappedQty = "", string description = "")
        {
            if (Session["ctx"] == null)
            {
                return Fail(Msg.GetMsg(Env.GetCtx(), "SessionExpired") ?? "Session Expired");
            }

            Ctx ctx = Session["ctx"] as Ctx;
            if (lineConfirmId <= 0) { return Fail(Msg.GetMsg(ctx, "NotFound") ?? "Not found"); }

            decimal confirmed, scrapped;
            if (!decimal.TryParse(confirmedQty, NumberStyles.Any, CultureInfo.InvariantCulture, out confirmed) || confirmed < 0)
            {
                return Fail(Msg.GetMsg(ctx, "VAS_155_WC_InvalidConfirmedQty") ?? "Confirmed quantity must be zero or a positive number.");
            }
            if (!decimal.TryParse(string.IsNullOrEmpty(scrappedQty) ? "0" : scrappedQty, NumberStyles.Any, CultureInfo.InvariantCulture, out scrapped) || scrapped < 0)
            {
                return Fail(Msg.GetMsg(ctx, "VAS_155_WC_InvalidScrappedQty") ?? "Scrapped quantity must be zero or a positive number.");
            }
            if (scrapped > 0 && scrapLocatorId <= 0)
            {
                return Fail(Msg.GetMsg(ctx, "VAS_155_WC_ScrapLocatorRequired") ?? "Scrap Locator is required when Scrapped Quantity is greater than zero.");
            }

            try
            {
                MInOutLineConfirm line = new MInOutLineConfirm(ctx, lineConfirmId, null);
                if (line.Get_ID() <= 0 || line.GetAD_Client_ID() != ctx.GetAD_Client_ID())
                {
                    return Fail(Msg.GetMsg(ctx, "NotFound") ?? "Not found");
                }

                decimal target = line.GetTargetQty();
                if (confirmed + scrapped > target)
                {
                    return Fail(Msg.GetMsg(ctx, "VAS_155_WC_OverTarget") ?? "Confirmed plus Scrapped quantity cannot exceed the Target quantity.");
                }

                MInOutConfirm parent = new MInOutConfirm(ctx, line.GetM_InOutConfirm_ID(), null);
                if (parent.Get_ID() <= 0 || parent.GetAD_Client_ID() != ctx.GetAD_Client_ID())
                {
                    return Fail(Msg.GetMsg(ctx, "NotFound") ?? "Not found");
                }
                if (parent.GetDocStatus() != "DR" && parent.GetDocStatus() != "IP")
                {
                    return Fail(Msg.GetMsg(ctx, "VAS_155_WC_ConfirmationCompleted") ?? "This confirmation is already completed and can no longer be edited.");
                }

                if (scrapLocatorId > 0)
                {
                    string checkSql = @"
                        SELECT COUNT(*)
                        FROM M_Locator Locator
                        INNER JOIN M_InOut InOut ON (InOut.M_InOut_ID = @DO_ID)
                        WHERE Locator.M_Locator_ID = @Locator_ID
                          AND Locator.IsActive = 'Y'
                          AND Locator.M_Warehouse_ID = InOut.M_Warehouse_ID";
                    int match = Util.GetValueOfInt(DB.ExecuteScalar(checkSql, new SqlParameter[]
                    {
                        new SqlParameter("@Locator_ID", scrapLocatorId),
                        new SqlParameter("@DO_ID", parent.GetM_InOut_ID())
                    }, null));
                    if (match <= 0)
                    {
                        return Fail(Msg.GetMsg(ctx, "VAS_155_WC_ScrapLocatorWrongWarehouse") ?? "The selected Scrap Locator does not belong to the delivery order's warehouse.");
                    }
                }

                // DifferenceQty is NOT set here - MInOutLineConfirm.BeforeSave() already
                // recalculates it as Target - Confirmed - Scrapped on every Save(), the
                // same auto-calc the verified VAS_090 GRN confirmation flow relies on.
                line.SetConfirmedQty(confirmed);
                line.SetScrappedQty(scrapped);
                line.SetDescription(description ?? "");
                line.SetM_Locator_ID(scrapLocatorId);

                if (!line.Save())
                {
                    return Fail(Msg.GetMsg(ctx, "SaveError") ?? "Save failed.");
                }

                return Ok(new
                {
                    success = true,
                    lineConfirmId = lineConfirmId,
                    differenceQty = line.GetDifferenceQty(),
                    matched = (confirmed + scrapped) >= target
                });
            }
            catch (Exception ex)
            {
                return Fail(ex.Message);
            }
        }

        /// <summary>
        /// Marks the confirmation as In Dispute. Only IsInDispute changes - DocStatus
        /// is never touched here.
        /// </summary>
        [AjaxAuthorizeAttribute]
        [AjaxSessionFilterAttribute]
        [HttpPost]
        public JsonResult MarkInDispute(int confirmId = 0)
        {
            if (Session["ctx"] == null)
            {
                return Fail(Msg.GetMsg(Env.GetCtx(), "SessionExpired") ?? "Session Expired");
            }

            Ctx ctx = Session["ctx"] as Ctx;
            if (confirmId <= 0) { return Fail(Msg.GetMsg(ctx, "NotFound") ?? "Not found"); }

            try
            {
                MInOutConfirm confirm = new MInOutConfirm(ctx, confirmId, null);
                if (confirm.Get_ID() <= 0 || confirm.GetAD_Client_ID() != ctx.GetAD_Client_ID())
                {
                    return Fail(Msg.GetMsg(ctx, "NotFound") ?? "Not found");
                }
                if (confirm.GetDocStatus() != "DR" && confirm.GetDocStatus() != "IP")
                {
                    return Fail(Msg.GetMsg(ctx, "VAS_155_WC_ConfirmationCompleted") ?? "This confirmation is already completed and can no longer be edited.");
                }

                confirm.SetIsInDispute(true);
                if (!confirm.Save())
                {
                    return Fail(Msg.GetMsg(ctx, "SaveError") ?? "Save failed.");
                }

                return Ok(new { success = true, status = "dispute" });
            }
            catch (Exception ex)
            {
                return Fail(ex.Message);
            }
        }

        /// <summary>
        /// Completes the confirmation through the standard document engine
        /// (DOCACTION_Complete), after verifying every active line is fully
        /// accounted for (ConfirmedQty + ScrappedQty = TargetQty). Never updates
        /// DocStatus directly.
        /// </summary>
        [AjaxAuthorizeAttribute]
        [AjaxSessionFilterAttribute]
        [HttpPost]
        public JsonResult CompleteConfirmation(int confirmId = 0)
        {
            if (Session["ctx"] == null)
            {
                return Fail(Msg.GetMsg(Env.GetCtx(), "SessionExpired") ?? "Session Expired");
            }

            Ctx ctx = Session["ctx"] as Ctx;
            if (confirmId <= 0) { return Fail(Msg.GetMsg(ctx, "NotFound") ?? "Not found"); }

            try
            {
                MInOutConfirm confirm = new MInOutConfirm(ctx, confirmId, null);
                if (confirm.Get_ID() <= 0 || confirm.GetAD_Client_ID() != ctx.GetAD_Client_ID())
                {
                    return Fail(Msg.GetMsg(ctx, "NotFound") ?? "Not found");
                }
                if (confirm.GetDocStatus() != "DR" && confirm.GetDocStatus() != "IP")
                {
                    return Fail(Msg.GetMsg(ctx, "VAS_155_WC_ConfirmationCompleted") ?? "This confirmation is already completed.");
                }

                string unaccountedSql = @"
                    SELECT COUNT(*)
                    FROM M_InOutLineConfirm LineConfirm
                    WHERE LineConfirm.M_InOutConfirm_ID = @Confirm_ID
                      AND LineConfirm.IsActive = 'Y'
                      AND (COALESCE(LineConfirm.ConfirmedQty,0) + COALESCE(LineConfirm.ScrappedQty,0)) <> COALESCE(LineConfirm.TargetQty,0)";
                int unaccounted = Util.GetValueOfInt(DB.ExecuteScalar(unaccountedSql, new SqlParameter[]
                {
                    new SqlParameter("@Confirm_ID", confirmId)
                }, null));
                if (unaccounted > 0)
                {
                    return Fail(Msg.GetMsg(ctx, "VAS_155_WC_LinesNotAccounted") ?? "Complete all confirmation lines before processing the confirmation.");
                }

                bool processed = confirm.ProcessIt(X_M_InOutConfirm.DOCACTION_Complete);
                confirm.Save();

                if (!processed || (confirm.GetDocStatus() != "CO" && confirm.GetDocStatus() != "CL"))
                {
                    string processMsg = confirm.GetProcessMsg();
                    return Fail(!string.IsNullOrEmpty(processMsg)
                        ? processMsg
                        : (Msg.GetMsg(ctx, "VAS_155_WC_CompleteFailed") ?? "The confirmation could not be completed."));
                }

                return Ok(new { success = true, status = "completed" });
            }
            catch (Exception ex)
            {
                return Fail(ex.Message);
            }
        }

        /// <summary>True when the physical column exists on the active database.</summary>
        private bool HasColumn(string tableName, string columnName)
        {
            string sql;
            if (DB.IsPostgreSQL())
            {
                sql = @"
                    SELECT COUNT(1)
                    FROM information_schema.columns
                    WHERE UPPER(table_name)=UPPER(@TableName)
                      AND UPPER(column_name)=UPPER(@ColumnName)";
            }
            else
            {
                sql = @"
                    SELECT COUNT(1)
                    FROM USER_TAB_COLUMNS
                    WHERE TABLE_NAME=UPPER(@TableName)
                      AND COLUMN_NAME=UPPER(@ColumnName)";
            }

            try
            {
                return Util.GetValueOfInt(DB.ExecuteScalar(sql, new SqlParameter[]
                {
                    new SqlParameter("@TableName", tableName),
                    new SqlParameter("@ColumnName", columnName)
                }, null)) > 0;
            }
            catch
            {
                return false;
            }
        }

        /// <summary>Wraps a success payload as a serialized JSON result.</summary>
        private JsonResult Ok(object result)
        {
            return Json(JsonConvert.SerializeObject(result), JsonRequestBehavior.AllowGet);
        }

        /// <summary>Wraps a failure message as a serialized JSON result.</summary>
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
