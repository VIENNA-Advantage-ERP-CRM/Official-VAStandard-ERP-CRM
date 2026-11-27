using Newtonsoft.Json;
using System;
using System.Collections.Generic;
using System.Data;
using System.Data.SqlClient;
using System.Globalization;
using System.Web.Mvc;
using VAdvantage.DataBase;
using VAdvantage.Logging;
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
    ///   VAS163      2026-11-27 Sr 53-62: quantity / precision / description checks, window locator rule,
    ///                          complete with differences, Movement Date, Quality Control (VA010) endpoints
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
                       InOut.MovementDate AS Movement_Date,
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

            string scrapLocatorSql = HasColumn("M_Locator", "LocatorCombination")
                ? "COALESCE(ScrapLocator.LocatorCombination, ScrapLocator.Value)"
                : "ScrapLocator.Value";

            string linesSql = @"
                SELECT LineConfirm.M_InOutLineConfirm_ID AS Line_Confirm_ID,
                       InOutLine.Line AS Line_No,
                       Product.Name AS Product_Name,
                       COALESCE(UomInfo.Name, UomInfo.UOMSymbol) AS UOM_Name,
                       UomInfo.StdPrecision AS UOM_Precision,
                       __QC_COUNT__ AS QC_Count,
                       __QC_MISSING__ AS QC_Missing,
                       AttributeInstance.Description AS Attribute_Description,
                       LineConfirm.M_Locator_ID AS Scrap_Locator_ID,
                       " + scrapLocatorSql + @" AS Scrap_Locator_Value,
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
            // VAS163 2026-11-27: Sr 62 quality parameter counts (VA010_ShipConfParameters) per line, injected AFTER
            // AddAccessSQL like the line count above (a subquery alias must not reach the outer access predicate).
            // The VA010 module is optional: without its table both counts are 0.
            if (HasQualityModule())
            {
                linesSql = linesSql.Replace("__QC_COUNT__", @"(SELECT COUNT(1)
                           FROM VA010_ShipConfParameters QAParam
                           WHERE QAParam.M_InOutLineConfirm_ID=LineConfirm.M_InOutLineConfirm_ID
                             AND QAParam.IsActive='Y')");
                linesSql = linesSql.Replace("__QC_MISSING__", @"(SELECT COUNT(1)
                           FROM VA010_ShipConfParameters QAMissing
                           WHERE QAMissing.M_InOutLineConfirm_ID=LineConfirm.M_InOutLineConfirm_ID
                             AND QAMissing.IsActive='Y'
                             AND (QAMissing.VA010_ActualValue IS NULL OR QAMissing.VA010_ActualValue='' OR QAMissing.VA010_ActualValue='0'))");
            }
            else
            {
                linesSql = linesSql.Replace("__QC_COUNT__", "0").Replace("__QC_MISSING__", "0");
            }
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
                    // VAS163 2026-11-27: Sr 61 the "Movement Date" field shows the delivery order's MovementDate.
                    DateTime? movementDate = Util.GetValueOfDateTime(dr["Movement_Date"]);
                    header = new
                    {
                        movementDate = movementDate.HasValue ? movementDate.Value.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture) : "",
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
                        uomPrecision = Util.GetValueOfInt(dr["UOM_Precision"]), // VAS163 2026-11-27: Sr 57
                        qcCount = Util.GetValueOfInt(dr["QC_Count"]),           // VAS163 2026-11-27: Sr 62
                        qcMissing = Util.GetValueOfInt(dr["QC_Missing"]),
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

            // VAS163 2026-11-27: Sr 58/59 same options as the M_Locator_ID field of the M_InOutLineConfirm tab
            // (Ship/Receipt Confirm and VAS_ShipReceiptConfirm windows): Table reference 191 (M_Locator) with no
            // validation rule and no reference WHERE clause, i.e. every active locator of the tenant the role can
            // access - not only the delivery order's warehouse. The DO warehouse's locators are listed first.
            string locatorNameSql = HasColumn("M_Locator", "LocatorCombination")
                ? "COALESCE(Locator.LocatorCombination, Locator.Value)"
                : "Locator.Value";

            string sql = @"
                SELECT Locator.M_Locator_ID AS Locator_ID,
                       " + locatorNameSql + @" AS Locator_Name,
                       Locator.IsDefault AS Is_Default,
                       Locator.M_Warehouse_ID AS Warehouse_ID,
                       Warehouse.Name AS Warehouse_Name
                FROM M_Locator Locator
                LEFT OUTER JOIN M_Warehouse Warehouse ON (Warehouse.M_Warehouse_ID=Locator.M_Warehouse_ID)
                WHERE Locator.IsActive='Y'
                  AND Locator.AD_Client_ID=@AD_Client_ID";

            sql = MRole.GetDefault(ctx).AddAccessSQL(sql, "Locator", MRole.SQL_FULLYQUALIFIED, MRole.SQL_RO);
            sql += @"
                ORDER BY (CASE WHEN Locator.M_Warehouse_ID=@Warehouse_ID THEN 0 ELSE 1 END), Warehouse.Name, Locator.IsDefault DESC, " + locatorNameSql;

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
                    string locatorName = Util.GetValueOfString(dr["Locator_Name"]);
                    string warehouseName = Util.GetValueOfString(dr["Warehouse_Name"]);
                    int locatorWarehouseId = Util.GetValueOfInt(dr["Warehouse_ID"]);
                    // Locators of another warehouse carry the warehouse name so equal locator values stay distinguishable.
                    if (locatorWarehouseId != warehouseId && !string.IsNullOrEmpty(warehouseName))
                    {
                        locatorName = locatorName + " (" + warehouseName + ")";
                    }
                    rows.Add(new
                    {
                        locatorId = Util.GetValueOfInt(dr["Locator_ID"]),
                        locatorName = locatorName,
                        warehouseId = locatorWarehouseId,
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
            // VAS163 2026-11-27: Sr 53 M_InOutLineConfirm.Description holds at most 255 characters.
            if (description != null && description.Length > 255)
            {
                return Fail(MsgOr(ctx, "VAS_155_WC_DescriptionTooLong", "Description cannot be longer than 255 characters."));
            }

            try
            {
                MInOutLineConfirm line = new MInOutLineConfirm(ctx, lineConfirmId, null);
                if (line.Get_ID() <= 0 || line.GetAD_Client_ID() != ctx.GetAD_Client_ID())
                {
                    return Fail(Msg.GetMsg(ctx, "NotFound") ?? "Not found");
                }

                // VAS163 2026-11-27: Sr 54/55/56 the same quantity rules (and messages) as the widget.
                decimal target = line.GetTargetQty();
                if (confirmed > target)
                {
                    return Fail(MsgOr(ctx, "VAS_090_ConfirmedAboveTarget", "Confirmed Quantity cannot be greater than Target Quantity."));
                }
                if (scrapped > target)
                {
                    return Fail(MsgOr(ctx, "VAS_090_ScrappedAboveTarget", "Scrapped Quantity cannot be greater than Target Quantity."));
                }
                if (confirmed + scrapped > target)
                {
                    return Fail(MsgOr(ctx, "VAS_172_ConfScrapExceedsTarget", "Quantity should not be greater than Target Quantity"));
                }

                // VAS163 2026-11-27: Sr 57 decimals limited to the line UOM's StdPrecision (0 -> whole numbers).
                int precision = GetLineUomPrecision(ctx, line);
                if (precision >= 0 && (decimal.Round(confirmed, precision) != confirmed || decimal.Round(scrapped, precision) != scrapped))
                {
                    return Fail(MsgOr(ctx, "VAS_155_WC_QtyPrecision", "Quantity exceeds the decimal places allowed for this UOM:")
                        + " " + precision.ToString(CultureInfo.InvariantCulture));
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
                    // VAS163 2026-11-27: Sr 58/59 aligned with the window's M_Locator_ID field (reference 191, no
                    // validation rule): any active, role-accessible locator of the tenant - no warehouse restriction.
                    string checkSql = @"
                        SELECT COUNT(1)
                        FROM M_Locator Locator
                        WHERE Locator.M_Locator_ID = @Locator_ID
                          AND Locator.IsActive = 'Y'
                          AND Locator.AD_Client_ID = @AD_Client_ID";
                    checkSql = MRole.GetDefault(ctx).AddAccessSQL(checkSql, "Locator", MRole.SQL_FULLYQUALIFIED, MRole.SQL_RO);
                    int match = Util.GetValueOfInt(DB.ExecuteScalar(checkSql, new SqlParameter[]
                    {
                        new SqlParameter("@Locator_ID", scrapLocatorId),
                        new SqlParameter("@AD_Client_ID", ctx.GetAD_Client_ID())
                    }, null));
                    if (match <= 0)
                    {
                        return Fail(MsgOr(ctx, "VAS_155_WC_ScrapLocatorInvalid", "The selected Scrap Locator is not valid."));
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
                    // VAS163 2026-11-27: Sr 59 show the model's own save error when there is one.
                    var saveError = VLogger.RetrieveError();
                    string saveText = saveError != null ? (saveError.GetName() ?? "") : "";
                    return Fail(!string.IsNullOrEmpty(saveText) ? saveText : (Msg.GetMsg(ctx, "SaveError") ?? "Save failed."));
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

                // VAS163 2026-11-27: Sr 60 no widget-side "all lines matched" block any more. The core
                // MInOutConfirm.PrepareIt marks a confirmation with differences In Dispute and CompleteIt
                // completes it (splitting the shipment when the document type says so); every refusal
                // (quality parameters, split document type, package, ...) comes back as the process message.
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

        /// <summary>
        /// VAS163 2026-11-27: Sr 62 the quality parameters (VA010_ShipConfParameters) of one customer
        /// confirmation line, with the Actual Value options of each (VA010_TestPrmtrList of the row's test
        /// parameter - the VA010_PrmList validation rule of the VA010_ActualValue column).
        /// </summary>
        /// <param name="lineConfirmId">M_InOutLineConfirm_ID.</param>
        /// <returns>JSON { rows[] } of qaRecordId, testParameter, acceptableValue, actualValueId, options[].</returns>
        [AjaxAuthorizeAttribute]
        [AjaxSessionFilterAttribute]
        public JsonResult GetLineQualityParameters(int lineConfirmId = 0)
        {
            if (Session["ctx"] == null)
            {
                return Fail(Msg.GetMsg(Env.GetCtx(), "SessionExpired") ?? "Session Expired");
            }

            Ctx ctx = Session["ctx"] as Ctx;
            List<object> rows = new List<object>();
            if (lineConfirmId <= 0 || !HasQualityModule()) { return Ok(new { rows = rows }); }

            string paramSql = @"
                SELECT QAParam.VA010_ShipConfParameters_ID AS QA_Record_ID,
                       QAParam.VA010_TestParameter_ID AS Test_Parameter_ID,
                       TestParam.VA010_TestPrmtrName AS Test_Parameter_Name,
                       QualParam.VA010_ParameterName AS Quality_Parameter_Name,
                       Acceptable.VA010_ParameterValue AS Acceptable_Value,
                       QAParam.VA010_ActualValue AS Actual_Value
                FROM VA010_ShipConfParameters QAParam
                INNER JOIN M_InOutLineConfirm LineConfirm ON (LineConfirm.M_InOutLineConfirm_ID=QAParam.M_InOutLineConfirm_ID AND LineConfirm.IsActive='Y')
                LEFT OUTER JOIN VA010_TestParameter TestParam ON (TestParam.VA010_TestParameter_ID=QAParam.VA010_TestParameter_ID)
                LEFT OUTER JOIN VA010_QualityParameters QualParam ON (QualParam.VA010_QualityParameters_ID=QAParam.VA010_QualityParameters_ID)
                LEFT OUTER JOIN VA010_TestPrmtrList Acceptable ON (Acceptable.VA010_TestPrmtrList_ID=QAParam.VA010_TestPrmtrList_ID)
                WHERE QAParam.IsActive='Y'
                  AND QAParam.M_InOutLineConfirm_ID=@Line_Confirm_ID
                  AND QAParam.AD_Client_ID=@AD_Client_ID";
            paramSql = MRole.GetDefault(ctx).AddAccessSQL(paramSql, "QAParam", MRole.SQL_FULLYQUALIFIED, MRole.SQL_RO);
            paramSql += " ORDER BY QAParam.VA010_ShipConfParameters_ID";

            // One query for the options of every test parameter of the line (no query per parameter).
            string optionSql = @"
                SELECT ValueList.VA010_TestPrmtrList_ID AS Value_ID,
                       ValueList.VA010_TestParameter_ID AS Test_Parameter_ID,
                       ValueList.VA010_ParameterValue AS Value_Name
                FROM VA010_TestPrmtrList ValueList
                WHERE ValueList.IsActive='Y'
                  AND ValueList.AD_Client_ID=@AD_Client_ID2
                  AND ValueList.VA010_TestParameter_ID IN (__QC_PARAMS__)";
            optionSql = MRole.GetDefault(ctx).AddAccessSQL(optionSql, "ValueList", MRole.SQL_FULLYQUALIFIED, MRole.SQL_RO);
            optionSql = optionSql.Replace("__QC_PARAMS__", @"SELECT QAOption.VA010_TestParameter_ID
                      FROM VA010_ShipConfParameters QAOption
                      WHERE QAOption.M_InOutLineConfirm_ID=@Line_Confirm_ID2
                        AND QAOption.IsActive='Y'");
            optionSql += " ORDER BY ValueList.VA010_TestParameter_ID, ValueList.VA010_TestPrmtrList_ID";

            try
            {
                if (!IsOpenCustomerLine(ctx, lineConfirmId)) { return Fail(MsgOr(ctx, "VAS_155_WC_NotFound", "Not found.")); }

                Dictionary<int, List<object>> options = new Dictionary<int, List<object>>();
                DataSet optionDs = DB.ExecuteDataset(optionSql, new SqlParameter[]
                {
                    new SqlParameter("@AD_Client_ID2", ctx.GetAD_Client_ID()),
                    new SqlParameter("@Line_Confirm_ID2", lineConfirmId)
                }, null);
                if (optionDs != null && optionDs.Tables.Count > 0)
                {
                    foreach (DataRow r in optionDs.Tables[0].Rows)
                    {
                        int testParameterId = Util.GetValueOfInt(r["Test_Parameter_ID"]);
                        if (!options.ContainsKey(testParameterId)) { options[testParameterId] = new List<object>(); }
                        options[testParameterId].Add(new
                        {
                            valueId = Util.GetValueOfInt(r["Value_ID"]),
                            valueName = Util.GetValueOfString(r["Value_Name"])
                        });
                    }
                }

                DataSet ds = DB.ExecuteDataset(paramSql, new SqlParameter[]
                {
                    new SqlParameter("@Line_Confirm_ID", lineConfirmId),
                    new SqlParameter("@AD_Client_ID", ctx.GetAD_Client_ID())
                }, null);
                if (ds != null && ds.Tables.Count > 0)
                {
                    foreach (DataRow r in ds.Tables[0].Rows)
                    {
                        int testParameterId = Util.GetValueOfInt(r["Test_Parameter_ID"]);
                        string name = Util.GetValueOfString(r["Test_Parameter_Name"]);
                        if (string.IsNullOrEmpty(name)) { name = Util.GetValueOfString(r["Quality_Parameter_Name"]); }
                        int actualValueId;
                        string rawActual = Util.GetValueOfString(r["Actual_Value"]);
                        if (!int.TryParse(rawActual, NumberStyles.Integer, CultureInfo.InvariantCulture, out actualValueId)) { actualValueId = 0; }
                        rows.Add(new
                        {
                            qaRecordId = Util.GetValueOfInt(r["QA_Record_ID"]),
                            testParameterId = testParameterId,
                            testParameter = System.Net.WebUtility.HtmlDecode(name ?? ""),
                            acceptableValue = System.Net.WebUtility.HtmlDecode(Util.GetValueOfString(r["Acceptable_Value"]) ?? ""),
                            actualValueId = actualValueId,
                            options = options.ContainsKey(testParameterId) ? options[testParameterId] : new List<object>()
                        });
                    }
                }

                return Ok(new { rows = rows });
            }
            catch (Exception ex)
            {
                return Fail(ex.Message);
            }
        }

        /// <summary>
        /// VAS163 2026-11-27: Sr 62 saves the Actual Value of the quality parameters of one customer
        /// confirmation line (VA010_ShipConfParameters.VA010_ActualValue = chosen VA010_TestPrmtrList_ID)
        /// through the table's PO in one transaction. Only open (DR/IP) customer confirmations.
        /// </summary>
        /// <param name="lineConfirmId">M_InOutLineConfirm_ID.</param>
        /// <param name="values">JSON array of { qaRecordId, actualValueId }.</param>
        /// <returns>JSON { success, saved } or { error }.</returns>
        [AjaxAuthorizeAttribute]
        [AjaxSessionFilterAttribute]
        [HttpPost]
        public JsonResult SaveLineQualityParameters(int lineConfirmId = 0, string values = "")
        {
            if (Session["ctx"] == null)
            {
                return Fail(Msg.GetMsg(Env.GetCtx(), "SessionExpired") ?? "Session Expired");
            }

            Ctx ctx = Session["ctx"] as Ctx;
            if (lineConfirmId <= 0 || !HasQualityModule()) { return Fail(MsgOr(ctx, "VAS_155_WC_NotFound", "Not found.")); }

            List<QualityValueInput> input;
            try
            {
                input = JsonConvert.DeserializeObject<List<QualityValueInput>>(values ?? "") ?? new List<QualityValueInput>();
            }
            catch (JsonException)
            {
                input = new List<QualityValueInput>();
            }
            input.RemoveAll(v => v == null || v.qaRecordId <= 0 || v.actualValueId <= 0);
            if (input.Count == 0)
            {
                return Fail(MsgOr(ctx, "VAS_155_WC_QcSelectValue", "Select an Actual Value for at least one quality parameter."));
            }

            Trx trx = null;
            try
            {
                if (!IsOpenCustomerLine(ctx, lineConfirmId))
                {
                    return Fail(MsgOr(ctx, "VAS_155_WC_ConfirmationCompleted", "This confirmation is already completed and can no longer be edited."));
                }

                // Valid (record, value) pairs of this line in one query: the value must belong to the record's own test parameter.
                string validSql = @"
                    SELECT QAParam.VA010_ShipConfParameters_ID AS QA_Record_ID,
                           ValueList.VA010_TestPrmtrList_ID AS Value_ID
                    FROM VA010_ShipConfParameters QAParam
                    INNER JOIN VA010_TestPrmtrList ValueList ON (ValueList.VA010_TestParameter_ID=QAParam.VA010_TestParameter_ID AND ValueList.IsActive='Y')
                    WHERE QAParam.IsActive='Y'
                      AND QAParam.M_InOutLineConfirm_ID=@Line_Confirm_ID
                      AND QAParam.AD_Client_ID=@AD_Client_ID";
                validSql = MRole.GetDefault(ctx).AddAccessSQL(validSql, "QAParam", MRole.SQL_FULLYQUALIFIED, MRole.SQL_RW);
                DataSet validDs = DB.ExecuteDataset(validSql, new SqlParameter[]
                {
                    new SqlParameter("@Line_Confirm_ID", lineConfirmId),
                    new SqlParameter("@AD_Client_ID", ctx.GetAD_Client_ID())
                }, null);
                HashSet<string> valid = new HashSet<string>();
                if (validDs != null && validDs.Tables.Count > 0)
                {
                    foreach (DataRow r in validDs.Tables[0].Rows)
                    {
                        valid.Add(Util.GetValueOfInt(r["QA_Record_ID"]) + "_" + Util.GetValueOfInt(r["Value_ID"]));
                    }
                }
                foreach (QualityValueInput v in input)
                {
                    if (!valid.Contains(v.qaRecordId + "_" + v.actualValueId))
                    {
                        return Fail(MsgOr(ctx, "VAS_155_WC_QcInvalidValue", "Select a valid Actual Value."));
                    }
                }

                trx = Trx.GetTrx(Trx.CreateTrxName("VAS155QC"));
                foreach (QualityValueInput v in input)
                {
                    PO qaRecord = MTable.GetPO(ctx, "VA010_ShipConfParameters", v.qaRecordId, trx);
                    if (qaRecord == null || qaRecord.Get_ID() != v.qaRecordId)
                    {
                        trx.Rollback();
                        return Fail(MsgOr(ctx, "VAS_155_WC_NotFound", "Not found."));
                    }
                    // VA010_ActualValue is a Table reference (AD_Reference 18 -> key of VA010_TestPrmtrList, an
                    // integer for the PO) stored in a VARCHAR2(10) column - the same value the window's lookup writes.
                    qaRecord.Set_Value("VA010_ActualValue", v.actualValueId);
                    if (!qaRecord.Save())
                    {
                        var saveError = VLogger.RetrieveError();
                        string saveText = saveError != null ? (saveError.GetName() ?? "") : "";
                        trx.Rollback();
                        return Fail(!string.IsNullOrEmpty(saveText) ? saveText : (Msg.GetMsg(ctx, "SaveError") ?? "Save failed."));
                    }
                }
                trx.Commit();
                return Ok(new { success = true, saved = input.Count });
            }
            catch (Exception ex)
            {
                if (trx != null) { trx.Rollback(); }
                return Fail(ex.Message);
            }
            finally
            {
                if (trx != null) { trx.Close(); }
            }
        }

        /// <summary>VAS163 2026-11-27: Sr 62 one Actual Value posted by the Quality Control pop-up.</summary>
        public class QualityValueInput
        {
            public int qaRecordId { get; set; }
            public int actualValueId { get; set; }
        }

        /// <summary>
        /// VAS163 2026-11-27: Sr 62 true when the line belongs to an open (DR/IP) customer delivery
        /// confirmation of this tenant that the role can access.
        /// </summary>
        private bool IsOpenCustomerLine(Ctx ctx, int lineConfirmId)
        {
            string sql = @"
                SELECT COUNT(1)
                FROM M_InOutLineConfirm LineConfirm
                INNER JOIN M_InOutConfirm Confirm ON (Confirm.M_InOutConfirm_ID=LineConfirm.M_InOutConfirm_ID AND Confirm.IsActive='Y')
                INNER JOIN M_InOut InOut ON (InOut.M_InOut_ID=Confirm.M_InOut_ID AND InOut.IsActive='Y')
                WHERE LineConfirm.IsActive='Y'
                  AND LineConfirm.M_InOutLineConfirm_ID=@Line_Confirm_ID
                  AND LineConfirm.AD_Client_ID=@AD_Client_ID
                  AND InOut.IsSOTrx='Y'
                  AND InOut.MovementType='C-'
                  AND Confirm.DocStatus IN ('DR','IP')";
            sql = MRole.GetDefault(ctx).AddAccessSQL(sql, "LineConfirm", MRole.SQL_FULLYQUALIFIED, MRole.SQL_RO);
            return Util.GetValueOfInt(DB.ExecuteScalar(sql, new SqlParameter[]
            {
                new SqlParameter("@Line_Confirm_ID", lineConfirmId),
                new SqlParameter("@AD_Client_ID", ctx.GetAD_Client_ID())
            }, null)) > 0;
        }

        /// <summary>VAS163 2026-11-27: Sr 62 true when the VA010 quality module table is present.</summary>
        private bool HasQualityModule()
        {
            return HasColumn("VA010_ShipConfParameters", "VA010_ShipConfParameters_ID")
                && HasColumn("VA010_TestPrmtrList", "VA010_ParameterValue")
                && HasColumn("VA010_TestParameter", "VA010_TestPrmtrName")
                && HasColumn("VA010_QualityParameters", "VA010_ParameterName");
        }

        /// <summary>
        /// VAS163 2026-11-27: Sr 57 StdPrecision of the confirmation line's UOM (line UOM, else the
        /// shipment line's, else the product's); -1 when no UOM is found (no check).
        /// </summary>
        private int GetLineUomPrecision(Ctx ctx, MInOutLineConfirm line)
        {
            int uomId = Util.GetValueOfInt(line.Get_Value("C_UOM_ID"));
            if (uomId <= 0 && line.GetM_InOutLine_ID() > 0)
            {
                MInOutLine inOutLine = new MInOutLine(ctx, line.GetM_InOutLine_ID(), null);
                uomId = inOutLine.GetC_UOM_ID();
                if (uomId <= 0 && inOutLine.GetM_Product_ID() > 0)
                {
                    uomId = MProduct.Get(ctx, inOutLine.GetM_Product_ID()).GetC_UOM_ID();
                }
            }
            return uomId > 0 ? MUOM.GetPrecision(ctx, uomId) : -1;
        }

        /// <summary>VAS163 2026-11-27: message text, or the English fallback while the key is not seeded.</summary>
        private static string MsgOr(Ctx ctx, string key, string fallback)
        {
            string text = Msg.GetMsg(ctx, key);
            if (string.IsNullOrEmpty(text) || text == key || text.StartsWith("[")) { return fallback; }
            return text;
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
