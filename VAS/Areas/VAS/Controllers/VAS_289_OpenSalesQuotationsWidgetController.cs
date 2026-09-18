/******************************************************
 * Module Name    : VAS
 * Purpose        : Sales Order dashboard "Open Sales Quotations" widget + Quotation -> Sales Order conversion wizard endpoints
 * chronological  : Development
 * Created Date   : 2026-09-15
 * Created by     : VAI052
 ******************************************************/

using Newtonsoft.Json;
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Data;
using System.Data.SqlClient;
using System.Globalization;
using System.Linq;
using System.Web.Mvc;
using VAdvantage.Classes;
using VAdvantage.DataBase;
using VAdvantage.Logging;
using VAdvantage.Model;
using VAdvantage.Utility;
using VIS.Filters;

namespace VAS.Controllers
{
    /// <summary>
    /// Module Name : VAS_289_OpenSalesQuotationsWidget
    /// Purpose     : Data + write endpoints for the 3x3 "Open Sales Quotations" widget
    ///               on the Sales Order dashboard and its Sales Quotation -&gt; Sales
    ///               Order conversion wizard.
    ///
    ///   Business definition per
    ///   22_Open_Sales_Quotations_Claude_Development_Prompt.txt (CONFIRMED - several
    ///   mock field names/rules in the paired HTML do not match the real schema and
    ///   are overridden here):
    ///     - Open quotation = C_Order with IsActive='Y', IsSOTrx='Y', non-return,
    ///       IsSalesQuotation='Y', DocStatus IN ('CO','IP'), not expired
    ///       (ValidTillDate IS NULL OR &gt;= today), with at least one active line
    ///       carrying pending quantity (quoted - already ordered &gt; 0).
    ///     - There is NO stored "AlreadyOrderedQty" column anywhere. Already-ordered
    ///       is always DERIVED by summing QtyOrdered from linked Sales Order lines
    ///       (C_OrderLine.C_Quotation_Line_ID = quotation C_OrderLine.C_OrderLine_ID)
    ///       whose parent order is active, IsSOTrx='Y', non-return, non-quotation and
    ///       DocStatus NOT IN ('VO','RE') - a merely Drafted/In-Process linked Sales
    ///       Order already consumes the quotation's quantity, by design, so a second
    ///       user cannot order the same balance again.
    ///     - Conversion status is a derived UI label, never stored in DocStatus:
    ///       CO + pending &gt; 0 + alreadyOrdered = 0 -&gt; "Ready to Convert" (chip-ok,
    ///       convertible); CO + pending &gt; 0 + alreadyOrdered &gt; 0 -&gt; "Partly Ordered"
    ///       (chip-warn, convertible); IP + pending &gt; 0 -&gt; "Awaiting Approval"
    ///       (chip-neutral, NOT convertible - IP quotations only navigate to the
    ///       existing Sales Quotation record, they never enter the line-selection step).
    ///     - The header "ready" chip counts CO quotations with pending qty &gt; 0 only
    ///       (Partly Ordered CO rows count; IP rows never do).
    ///     - "Segment" -&gt; Business Partner Group (C_BPartner.C_BP_Group_ID -&gt;
    ///       C_BP_Group.Name), informational only, never written to the new order.
    ///     - "Delivery mode" -&gt; Shipping Method (C_Order.DeliveryViaRule); "Rate" on
    ///       a line -&gt; Unit Price (C_OrderLine.PriceActual). No Road/Courier/Rail
    ///       mock values are ever used - real AD_Ref_List values only.
    ///     - Header Tax and header Print Description from the mock DO NOT exist on
    ///       C_Order and are removed entirely; both are real C_OrderLine fields
    ///       (C_Tax_ID, PrintDescription) copied per-line from the quotation line and
    ///       remain editable per line on the new order.
    ///
    ///   Creation rule: this is a NEW Sales Order, never a copy of the quotation's own
    ///   identity - it gets its own DocumentNo/DocStatus/document-type target via the
    ///   normal Sales Order creation path (MOrder/MOrderLine, the same model classes
    ///   VAS_204's Purchase Order creation and ModelLibrary/Process/CopyOrder.cs use -
    ///   never a hand-written INSERT). C_Order.C_Order_Quotation and
    ///   C_OrderLine.C_Quotation_Line_ID are the fixed, read-only source references.
    ///   DateOrdered defaults to today (never the quotation's own DateOrdered);
    ///   ValidTillDate is never mapped into DatePromised. Immediately before create,
    ///   every selected line's current pending quantity is recomputed fresh inside the
    ///   same transaction and re-validated - a concurrent conversion by another user
    ///   surfaces as a 409-shaped conflict response naming the affected line(s) rather
    ///   than silently reducing the requested quantity.
    ///
    ///   Per the CTE/MRole rule (Prompt_Instructions.txt "Case 1"): every read query
    ///   isolates a single "FROM C_Order q" WHERE-only fragment and calls AddAccessSQL
    ///   ONLY on that isolated fragment before embedding it as a CTE inside the larger
    ///   linked_orders/quote_balance statement - never on the combined multi-CTE query.
    /// Chronological development:
    ///   VAI052      2026-09-15 Created
    /// </summary>
    public class VAS_289_OpenSalesQuotationsWidgetController : Controller
    {
        private static readonly VLogger Log = VLogger.GetVLogger(typeof(VAS_289_OpenSalesQuotationsWidgetController).FullName);

        private const int DefaultPageSize = 6;
        private const int MaxPageSize = 10;

        #region Widget list

        /// <summary>
        /// One page of open Sales Quotations, sorted by earliest expiry first, plus the
        /// ready count (CO quotations with pending qty &gt; 0) across the complete result.
        /// </summary>
        [AjaxAuthorizeAttribute]
        [AjaxSessionFilterAttribute]
        public JsonResult GetOpenQuotations(int page = 0, int size = DefaultPageSize)
        {
            if (Session["ctx"] == null) { return Json("", JsonRequestBehavior.AllowGet); }
            Ctx ctx = Session["ctx"] as Ctx;
            if (page < 0) { page = 0; }
            if (size <= 0 || size > MaxPageSize) { size = DefaultPageSize; }

            try
            {
                string json = JsonConvert.SerializeObject(GetOpenQuotationsData(ctx, page, size));
                return Json(json, JsonRequestBehavior.AllowGet);
            }
            catch (Exception ex)
            {
                Log.Log(Level.SEVERE, "VAS_289_OpenSalesQuotationsWidget.GetOpenQuotations", ex);
                return ErrorResult(ctx);
            }
        }

        /// <summary>
        /// The single physical-table fragment - one "FROM C_Order q", WHERE-only, no
        /// GROUP BY. The ONLY thing AddAccessSQL is ever applied to for the widget list.
        /// </summary>
        private static string BuildBaseQuotationsSql()
        {
            return @"
                SELECT q.C_Order_ID AS C_Order_ID,
                       q.DocumentNo AS Document_No,
                       q.C_BPartner_ID AS BPartner_Id,
                       q.ValidTillDate AS Valid_Till_Date,
                       q.DocStatus AS Doc_Status_Code
                  FROM C_Order q
                 WHERE q.AD_Client_ID = @AD_Client_ID
                   AND q.IsActive = 'Y'
                   AND q.IsSOTrx = 'Y'
                   AND COALESCE(q.IsReturnTrx, 'N') = 'N'
                   AND q.IsSalesQuotation = 'Y'
                   AND q.DocStatus IN ('CO', 'IP')
                   AND (q.ValidTillDate IS NULL OR q.ValidTillDate >= @Today)";
        }

        /// <summary>Same "still-consumes-quantity" join used everywhere already-ordered is derived - never re-run through AddAccessSQL (it joins off the already-filtered base_quotations set).</summary>
        private const string LinkedOrdersCte = @"
            linked_orders AS (
                SELECT sol.C_Quotation_Line_ID AS Quotation_Line_Id,
                       SUM(COALESCE(sol.QtyOrdered, 0)) AS Already_Ordered_Qty
                  FROM C_OrderLine sol
                  INNER JOIN C_Order so ON ( so.C_Order_ID = sol.C_Order_ID
                                         AND so.IsActive = 'Y'
                                         AND so.IsSOTrx = 'Y'
                                         AND COALESCE(so.IsReturnTrx, 'N') = 'N'
                                         AND COALESCE(so.IsSalesQuotation, 'N') = 'N'
                                         AND so.DocStatus NOT IN ('VO', 'RE') )
                 WHERE sol.IsActive = 'Y'
                   AND sol.C_Quotation_Line_ID IS NOT NULL
                 GROUP BY sol.C_Quotation_Line_ID
            )";

        private OpenQuotationsResult GetOpenQuotationsData(Ctx ctx, int page, int size)
        {
            OpenQuotationsResult result = new OpenQuotationsResult { Rows = new List<QuotationRow>() };
            if (ctx == null) { return result; }

            string baseQuotationsSql = MRole.GetDefault(ctx).AddAccessSQL(BuildBaseQuotationsSql(), "q", MRole.SQL_FULLYQUALIFIED, MRole.SQL_RO);

            string sql = @"
                WITH base_quotations AS (" + baseQuotationsSql + @"),
                " + LinkedOrdersCte + @",
                quote_balance AS (
                    SELECT ql.C_Order_ID AS Quotation_Id,
                           COUNT(*) AS Line_Count,
                           SUM(COALESCE(ql.QtyOrdered, 0)) AS Quoted_Qty,
                           SUM(COALESCE(lo.Already_Ordered_Qty, 0)) AS Already_Ordered_Qty,
                           SUM(
                               CASE WHEN COALESCE(ql.QtyOrdered, 0) > COALESCE(lo.Already_Ordered_Qty, 0)
                                    THEN COALESCE(ql.QtyOrdered, 0) - COALESCE(lo.Already_Ordered_Qty, 0)
                                    ELSE 0
                               END
                           ) AS Pending_Qty
                      FROM C_OrderLine ql
                      INNER JOIN base_quotations bq ON ( bq.C_Order_ID = ql.C_Order_ID )
                      LEFT OUTER JOIN linked_orders lo ON ( lo.Quotation_Line_Id = ql.C_OrderLine_ID )
                     WHERE ql.IsActive = 'Y'
                     GROUP BY ql.C_Order_ID
                )
                SELECT
                    bq.C_Order_ID AS Quotation_Id,
                    bq.Document_No AS Document_No,
                    bp.Name AS Business_Partner,
                    qb.Line_Count AS Line_Count,
                    qb.Quoted_Qty AS Quoted_Qty,
                    qb.Already_Ordered_Qty AS Already_Ordered_Qty,
                    qb.Pending_Qty AS Pending_Qty,
                    bq.Valid_Till_Date AS Valid_Till_Date,
                    bq.Doc_Status_Code AS Doc_Status_Code,
                    COUNT(1) OVER () AS Total_Rows,
                    SUM(CASE WHEN bq.Doc_Status_Code = 'CO' THEN 1 ELSE 0 END) OVER () AS Ready_Count
                  FROM base_quotations bq
                  INNER JOIN quote_balance qb ON ( qb.Quotation_Id = bq.C_Order_ID )
                  INNER JOIN C_BPartner bp ON ( bp.C_BPartner_ID = bq.BPartner_Id )
                 WHERE qb.Pending_Qty > 0
                 ORDER BY
                    CASE WHEN bq.Valid_Till_Date IS NULL THEN 1 ELSE 0 END,
                    bq.Valid_Till_Date ASC,
                    bq.Document_No ASC
                 OFFSET @Row_Offset ROWS FETCH NEXT @Page_Size ROWS ONLY";

            IDataReader dr = null;
            try
            {
                dr = DB.ExecuteReader(sql, new[]
                {
                    new SqlParameter("@AD_Client_ID", ctx.GetAD_Client_ID()),
                    new SqlParameter("@Today", DateTime.Today),
                    new SqlParameter("@Row_Offset", page * size),
                    new SqlParameter("@Page_Size", size)
                });

                int total = 0, readyCount = 0;
                while (dr != null && dr.Read())
                {
                    total = dr["Total_Rows"] == DBNull.Value ? 0 : Util.GetValueOfInt(dr["Total_Rows"]);
                    readyCount = dr["Ready_Count"] == DBNull.Value ? 0 : Util.GetValueOfInt(dr["Ready_Count"]);

                    string docStatusCode = Util.GetValueOfString(dr["Doc_Status_Code"]);
                    decimal alreadyOrdered = dr["Already_Ordered_Qty"] == DBNull.Value ? 0m : Util.GetValueOfDecimal(dr["Already_Ordered_Qty"]);
                    decimal pendingQty = dr["Pending_Qty"] == DBNull.Value ? 0m : Util.GetValueOfDecimal(dr["Pending_Qty"]);
                    DateTime? validTill = dr["Valid_Till_Date"] == DBNull.Value ? (DateTime?)null : Util.GetValueOfDateTime(dr["Valid_Till_Date"]);

                    string statusLabel, statusChip;
                    bool convertible;
                    ClassifyQuotationStatus(docStatusCode, alreadyOrdered, out statusLabel, out statusChip, out convertible);

                    result.Rows.Add(new QuotationRow
                    {
                        QuotationId = Util.GetValueOfInt(dr["Quotation_Id"]),
                        DocumentNo = Util.GetValueOfString(dr["Document_No"]),
                        BusinessPartner = Util.GetValueOfString(dr["Business_Partner"]),
                        LineCount = dr["Line_Count"] == DBNull.Value ? 0 : Util.GetValueOfInt(dr["Line_Count"]),
                        PendingQty = pendingQty,
                        ValidTillDate = validTill.HasValue ? validTill.Value.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture) : null,
                        DocStatus = docStatusCode,
                        StatusLabel = statusLabel,
                        StatusChipClass = statusChip,
                        IsConvertible = convertible
                    });
                }
                result.Total = total;
                result.ReadyCount = readyCount;
            }
            finally
            {
                CloseReader(dr);
            }

            return result;
        }

        /// <summary>Quotation-specific status vocabulary matches 22-open-sales-quotations.html verbatim (confirmed by the user over the dev prompt's schema-label correction) - "Accepted"/"Partly ordered"/"Awaiting approval" are UI workflow labels, never written back to C_Order.DocStatus.</summary>
        private static void ClassifyQuotationStatus(string docStatus, decimal alreadyOrdered, out string label, out string chipClass, out bool convertible)
        {
            if (docStatus == "IP")
            {
                label = "Awaiting approval"; chipClass = "neutral"; convertible = false;
                return;
            }
            if (alreadyOrdered > 0)
            {
                label = "Partly ordered"; chipClass = "warn"; convertible = true;
                return;
            }
            label = "Accepted"; chipClass = "ok"; convertible = true;
        }

        #endregion

        #region Step 2 - quotation lines

        /// <summary>Quotation header (for the step-2 summary strip) plus its active lines with derived already-ordered/pending and default warehouse/free-stock.</summary>
        [AjaxAuthorizeAttribute]
        [AjaxSessionFilterAttribute]
        public JsonResult GetQuotationLines(int quotationId)
        {
            if (Session["ctx"] == null) { return Json("", JsonRequestBehavior.AllowGet); }
            Ctx ctx = Session["ctx"] as Ctx;

            try
            {
                string json = JsonConvert.SerializeObject(GetQuotationLinesData(ctx, quotationId));
                return Json(json, JsonRequestBehavior.AllowGet);
            }
            catch (Exception ex)
            {
                Log.Log(Level.SEVERE, "VAS_289_OpenSalesQuotationsWidget.GetQuotationLines", ex);
                return ErrorResult(ctx);
            }
        }

        private QuotationLinesResult GetQuotationLinesData(Ctx ctx, int quotationId)
        {
            QuotationLinesResult result = new QuotationLinesResult { Lines = new List<QuotationLineRow>() };
            if (ctx == null || quotationId <= 0) { return result; }

            string headerSql = @"
                SELECT q.C_Order_ID AS Quotation_Id,
                       q.DocumentNo AS Document_No,
                       q.C_BPartner_ID AS BPartner_Id,
                       q.M_Warehouse_ID AS Warehouse_Id,
                       q.ValidTillDate AS Valid_Till_Date,
                       q.DocStatus AS Doc_Status_Code,
                       bp.Name AS Business_Partner,
                       bpg.Name AS Business_Partner_Group
                  FROM C_Order q
                  INNER JOIN C_BPartner bp ON ( bp.C_BPartner_ID = q.C_BPartner_ID )
                  LEFT OUTER JOIN C_BP_Group bpg ON ( bpg.C_BP_Group_ID = bp.C_BP_Group_ID )
                 WHERE q.C_Order_ID = @Quotation_Id
                   AND q.AD_Client_ID = @AD_Client_ID
                   AND q.IsActive = 'Y'
                   AND q.IsSalesQuotation = 'Y'";

            headerSql = MRole.GetDefault(ctx).AddAccessSQL(headerSql, "q", MRole.SQL_FULLYQUALIFIED, MRole.SQL_RO);

            int headerWarehouseId = 0;

            IDataReader dr = null;
            try
            {
                dr = DB.ExecuteReader(headerSql, new[]
                {
                    new SqlParameter("@Quotation_Id", quotationId),
                    new SqlParameter("@AD_Client_ID", ctx.GetAD_Client_ID())
                });

                if (dr == null || !dr.Read()) { return result; }

                headerWarehouseId = dr["Warehouse_Id"] == DBNull.Value ? 0 : Util.GetValueOfInt(dr["Warehouse_Id"]);
                DateTime? validTill = dr["Valid_Till_Date"] == DBNull.Value ? (DateTime?)null : Util.GetValueOfDateTime(dr["Valid_Till_Date"]);
                string docStatusCode = Util.GetValueOfString(dr["Doc_Status_Code"]);

                result.Quotation = new QuotationHeader
                {
                    QuotationId = Util.GetValueOfInt(dr["Quotation_Id"]),
                    DocumentNo = Util.GetValueOfString(dr["Document_No"]),
                    BusinessPartnerId = Util.GetValueOfInt(dr["BPartner_Id"]),
                    BusinessPartner = Util.GetValueOfString(dr["Business_Partner"]),
                    BusinessPartnerGroup = Util.GetValueOfString(dr["Business_Partner_Group"]),
                    WarehouseId = headerWarehouseId,
                    ValidTillDate = validTill.HasValue ? validTill.Value.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture) : null,
                    DocStatus = docStatusCode
                };
            }
            finally
            {
                CloseReader(dr);
            }

            if (result.Quotation == null) { return result; }

            string sql = @"
                WITH " + LinkedOrdersCte + @"
                SELECT
                    ql.C_OrderLine_ID AS Line_Id,
                    ql.M_Product_ID AS Product_Id,
                    COALESCE(p.Name, p.Value) AS Product_Name,
                    COALESCE(p.IsStocked, 'N') AS Is_Stocked,
                    ql.M_AttributeSetInstance_ID AS Asi_Id,
                    asi.Description AS Attribute_Text,
                    ql.C_UOM_ID AS Uom_Id,
                    u.Name AS Uom_Name,
                    COALESCE(ql.QtyOrdered, 0) AS Quoted_Qty,
                    COALESCE(lo.Already_Ordered_Qty, 0) AS Already_Ordered_Qty,
                    ql.PriceActual AS Rate,
                    ql.C_Tax_ID AS Tax_Id,
                    ql.DatePromised AS Date_Promised,
                    ql.Description AS Description,
                    ql.PrintDescription AS Print_Description,
                    COALESCE(ql.M_Warehouse_ID, @Header_Warehouse_Id) AS Warehouse_Id
                  FROM C_OrderLine ql
                  LEFT OUTER JOIN linked_orders lo ON ( lo.Quotation_Line_Id = ql.C_OrderLine_ID )
                  LEFT OUTER JOIN M_Product p ON ( p.M_Product_ID = ql.M_Product_ID )
                  LEFT OUTER JOIN C_UOM u ON ( u.C_UOM_ID = ql.C_UOM_ID )
                  LEFT OUTER JOIN M_AttributeSetInstance asi ON ( asi.M_AttributeSetInstance_ID = ql.M_AttributeSetInstance_ID )
                 WHERE ql.C_Order_ID = @Quotation_Id
                   AND ql.IsActive = 'Y'
                 ORDER BY ql.Line ASC";

            IDataReader dr2 = null;
            try
            {
                dr2 = DB.ExecuteReader(sql, new[]
                {
                    new SqlParameter("@Header_Warehouse_Id", headerWarehouseId),
                    new SqlParameter("@Quotation_Id", quotationId)
                });

                while (dr2 != null && dr2.Read())
                {
                    decimal quoted = dr2["Quoted_Qty"] == DBNull.Value ? 0m : Util.GetValueOfDecimal(dr2["Quoted_Qty"]);
                    decimal already = dr2["Already_Ordered_Qty"] == DBNull.Value ? 0m : Util.GetValueOfDecimal(dr2["Already_Ordered_Qty"]);
                    decimal pending = Math.Max(0m, quoted - already);
                    if (already > quoted)
                    {
                        Log.Log(Level.WARNING, "VAS_289_OpenSalesQuotationsWidget: negative pending qty clamped to zero for quotation line " + Util.GetValueOfInt(dr2["Line_Id"]));
                    }
                    if (pending <= 0) { continue; } // fully consumed lines are never selectable

                    int productId = dr2["Product_Id"] == DBNull.Value ? 0 : Util.GetValueOfInt(dr2["Product_Id"]);
                    int lineWarehouseId = dr2["Warehouse_Id"] == DBNull.Value ? headerWarehouseId : Util.GetValueOfInt(dr2["Warehouse_Id"]);
                    bool isStocked = Util.GetValueOfString(dr2["Is_Stocked"]) == "Y";
                    DateTime? datePromised = dr2["Date_Promised"] == DBNull.Value ? (DateTime?)null : Util.GetValueOfDateTime(dr2["Date_Promised"]);

                    result.Lines.Add(new QuotationLineRow
                    {
                        LineId = Util.GetValueOfInt(dr2["Line_Id"]),
                        ProductId = productId,
                        ProductName = Util.GetValueOfString(dr2["Product_Name"]),
                        IsStocked = isStocked,
                        AttributeSetInstanceId = dr2["Asi_Id"] == DBNull.Value ? 0 : Util.GetValueOfInt(dr2["Asi_Id"]),
                        AttributeText = Util.GetValueOfString(dr2["Attribute_Text"]),
                        UomId = dr2["Uom_Id"] == DBNull.Value ? 0 : Util.GetValueOfInt(dr2["Uom_Id"]),
                        UomName = Util.GetValueOfString(dr2["Uom_Name"]),
                        QuotedQty = quoted,
                        AlreadyOrderedQty = already,
                        PendingQty = pending,
                        Rate = dr2["Rate"] == DBNull.Value ? 0m : Util.GetValueOfDecimal(dr2["Rate"]),
                        TaxId = dr2["Tax_Id"] == DBNull.Value ? 0 : Util.GetValueOfInt(dr2["Tax_Id"]),
                        DatePromised = datePromised.HasValue ? datePromised.Value.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture) : null,
                        Description = Util.GetValueOfString(dr2["Description"]),
                        PrintDescription = Util.GetValueOfString(dr2["Print_Description"]),
                        WarehouseId = lineWarehouseId,
                        FreeStock = isStocked ? (decimal?)ResolveFreeStock(ctx, productId, lineWarehouseId, dr2["Asi_Id"] == DBNull.Value ? 0 : Util.GetValueOfInt(dr2["Asi_Id"])) : null
                    });
                }
            }
            finally
            {
                CloseReader(dr2);
            }

            result.Quotation.LineCount = result.Lines.Count;
            result.Quotation.QuotedQty = result.Lines.Sum(l => l.QuotedQty);
            result.Quotation.AlreadyOrderedQty = result.Lines.Sum(l => l.AlreadyOrderedQty);
            result.Quotation.PendingQty = result.Lines.Sum(l => l.PendingQty);
            string statusLabel, statusChip; bool convertible;
            ClassifyQuotationStatus(result.Quotation.DocStatus, result.Quotation.AlreadyOrderedQty, out statusLabel, out statusChip, out convertible);
            result.Quotation.StatusLabel = statusLabel;
            result.Quotation.StatusChipClass = statusChip;

            return result;
        }

        /// <summary>Warehouse-level free stock for one product+attribute: QtyOnHand - QtyReserved - QtyDedicated - QtyAllocated, summed across the warehouse's active locators. Called only for stocked products (M_Product.IsStocked='Y').</summary>
        [AjaxAuthorizeAttribute]
        [AjaxSessionFilterAttribute]
        public JsonResult GetFreeStock(int productId, int warehouseId, int attributeSetInstanceId = 0)
        {
            if (Session["ctx"] == null) { return Json("", JsonRequestBehavior.AllowGet); }
            Ctx ctx = Session["ctx"] as Ctx;

            try
            {
                decimal freeStock = ResolveFreeStock(ctx, productId, warehouseId, attributeSetInstanceId);
                return Json(JsonConvert.SerializeObject(new { FreeStock = freeStock }), JsonRequestBehavior.AllowGet);
            }
            catch (Exception ex)
            {
                Log.Log(Level.SEVERE, "VAS_289_OpenSalesQuotationsWidget.GetFreeStock", ex);
                return ErrorResult(ctx);
            }
        }

        private decimal ResolveFreeStock(Ctx ctx, int productId, int warehouseId, int attributeSetInstanceId)
        {
            if (productId <= 0 || warehouseId <= 0) { return 0m; }

            string sql = @"
                SELECT COALESCE(SUM(COALESCE(s.QtyOnHand, 0) - COALESCE(s.QtyReserved, 0)
                              - COALESCE(s.QtyDedicated, 0) - COALESCE(s.QtyAllocated, 0)), 0) AS Free_Stock
                  FROM M_Storage s
                  INNER JOIN M_Locator loc ON ( loc.M_Locator_ID = s.M_Locator_ID )
                 WHERE s.M_Product_ID = @M_Product_ID
                   AND loc.M_Warehouse_ID = @M_Warehouse_ID
                   AND loc.IsActive = 'Y'
                   AND COALESCE(s.M_AttributeSetInstance_ID, 0) = @Asi_Id";

            sql = MRole.GetDefault(ctx).AddAccessSQL(sql, "s", MRole.SQL_FULLYQUALIFIED, MRole.SQL_RO);

            return Util.GetValueOfDecimal(DB.ExecuteScalar(sql, new[]
            {
                new SqlParameter("@M_Product_ID", productId),
                new SqlParameter("@M_Warehouse_ID", warehouseId),
                new SqlParameter("@Asi_Id", attributeSetInstanceId)
            }, null));
        }

        #endregion

        #region Step 3 - order form defaults / lookups

        /// <summary>Prefilled header defaults from the quotation, plus every dropdown option list the order-details step needs.</summary>
        [AjaxAuthorizeAttribute]
        [AjaxSessionFilterAttribute]
        public JsonResult GetOrderFormDefaults(int quotationId)
        {
            if (Session["ctx"] == null) { return Json("", JsonRequestBehavior.AllowGet); }
            Ctx ctx = Session["ctx"] as Ctx;

            try
            {
                string json = JsonConvert.SerializeObject(GetOrderFormDefaultsData(ctx, quotationId));
                return Json(json, JsonRequestBehavior.AllowGet);
            }
            catch (Exception ex)
            {
                Log.Log(Level.SEVERE, "VAS_289_OpenSalesQuotationsWidget.GetOrderFormDefaults", ex);
                return ErrorResult(ctx);
            }
        }

        private OrderFormDefaultsResult GetOrderFormDefaultsData(Ctx ctx, int quotationId)
        {
            OrderFormDefaultsResult result = new OrderFormDefaultsResult();
            if (ctx == null || quotationId <= 0) { return result; }

            string headerSql = @"
                SELECT q.C_Order_ID AS Quotation_Id,
                       q.C_BPartner_ID AS BPartner_Id,
                       q.C_BPartner_Location_ID AS Location_Id,
                       q.AD_User_ID AS Contact_Id,
                       q.SalesRep_ID AS Sales_Rep_Id,
                       q.M_Warehouse_ID AS Warehouse_Id,
                       q.PriorityRule AS Priority_Rule,
                       q.DatePromised AS Date_Promised,
                       q.POReference AS Po_Reference,
                       q.C_PaymentTerm_ID AS Payment_Term_Id,
                       q.DeliveryRule AS Delivery_Rule,
                       q.DeliveryViaRule AS Delivery_Via_Rule,
                       q.M_PriceList_ID AS Price_List_Id,
                       q.C_Currency_ID AS Currency_Id,
                       q.C_ConversionType_ID AS Conversion_Type_Id,
                       q.C_IncoTerm_ID AS Inco_Term_Id,
                       q.Description AS Description,
                       bp.Name AS Business_Partner,
                       loc.Name AS Location_Name,
                       usr.Name AS Contact_Name,
                       rep.Name AS Sales_Rep_Name
                  FROM C_Order q
                  INNER JOIN C_BPartner bp ON ( bp.C_BPartner_ID = q.C_BPartner_ID )
                  LEFT OUTER JOIN C_BPartner_Location loc ON ( loc.C_BPartner_Location_ID = q.C_BPartner_Location_ID )
                  LEFT OUTER JOIN AD_User usr ON ( usr.AD_User_ID = q.AD_User_ID )
                  LEFT OUTER JOIN AD_User rep ON ( rep.AD_User_ID = q.SalesRep_ID )
                 WHERE q.C_Order_ID = @Quotation_Id
                   AND q.AD_Client_ID = @AD_Client_ID
                   AND q.IsActive = 'Y'
                   AND q.IsSalesQuotation = 'Y'";

            headerSql = MRole.GetDefault(ctx).AddAccessSQL(headerSql, "q", MRole.SQL_FULLYQUALIFIED, MRole.SQL_RO);

            int bpartnerId = 0;

            IDataReader dr = null;
            try
            {
                dr = DB.ExecuteReader(headerSql, new[]
                {
                    new SqlParameter("@Quotation_Id", quotationId),
                    new SqlParameter("@AD_Client_ID", ctx.GetAD_Client_ID())
                });

                if (dr == null || !dr.Read()) { return result; }

                bpartnerId = Util.GetValueOfInt(dr["BPartner_Id"]);
                DateTime? datePromised = dr["Date_Promised"] == DBNull.Value ? (DateTime?)null : Util.GetValueOfDateTime(dr["Date_Promised"]);

                result.Defaults = new OrderDefaults
                {
                    BusinessPartnerId = bpartnerId,
                    BusinessPartnerName = Util.GetValueOfString(dr["Business_Partner"]),
                    LocationId = dr["Location_Id"] == DBNull.Value ? 0 : Util.GetValueOfInt(dr["Location_Id"]),
                    LocationName = Util.GetValueOfString(dr["Location_Name"]),
                    ContactId = dr["Contact_Id"] == DBNull.Value ? 0 : Util.GetValueOfInt(dr["Contact_Id"]),
                    ContactName = Util.GetValueOfString(dr["Contact_Name"]),
                    SalesRepId = dr["Sales_Rep_Id"] == DBNull.Value ? 0 : Util.GetValueOfInt(dr["Sales_Rep_Id"]),
                    SalesRepName = Util.GetValueOfString(dr["Sales_Rep_Name"]),
                    WarehouseId = dr["Warehouse_Id"] == DBNull.Value ? 0 : Util.GetValueOfInt(dr["Warehouse_Id"]),
                    PriorityRule = Util.GetValueOfString(dr["Priority_Rule"]),
                    DatePromised = datePromised.HasValue ? datePromised.Value.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture) : DateTime.Today.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture),
                    POReference = Util.GetValueOfString(dr["Po_Reference"]),
                    PaymentTermId = dr["Payment_Term_Id"] == DBNull.Value ? 0 : Util.GetValueOfInt(dr["Payment_Term_Id"]),
                    DeliveryRule = Util.GetValueOfString(dr["Delivery_Rule"]),
                    DeliveryViaRule = Util.GetValueOfString(dr["Delivery_Via_Rule"]),
                    PriceListId = dr["Price_List_Id"] == DBNull.Value ? 0 : Util.GetValueOfInt(dr["Price_List_Id"]),
                    CurrencyId = dr["Currency_Id"] == DBNull.Value ? 0 : Util.GetValueOfInt(dr["Currency_Id"]),
                    ConversionTypeId = dr["Conversion_Type_Id"] == DBNull.Value ? 0 : Util.GetValueOfInt(dr["Conversion_Type_Id"]),
                    IncoTermId = dr["Inco_Term_Id"] == DBNull.Value ? 0 : Util.GetValueOfInt(dr["Inco_Term_Id"]),
                    Description = Util.GetValueOfString(dr["Description"]),
                    DateOrdered = DateTime.Today.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture)
                };
            }
            finally
            {
                CloseReader(dr);
            }

            if (result.Defaults == null) { return result; }

            result.Defaults.DocTypeTargetId = ResolveSalesOrderDocType(ctx);
            result.Options = LoadFormOptions(ctx);
            BPartnerOptions bpOptions = LoadBPartnerOptions(ctx, bpartnerId);
            result.Options.Locations = bpOptions.Locations;
            result.Options.Contacts = bpOptions.Contacts;

            return result;
        }

        /// <summary>Location/contact lists for a Business Partner - re-fetched by the client whenever the user changes Business Partner on the order-details step.</summary>
        [AjaxAuthorizeAttribute]
        [AjaxSessionFilterAttribute]
        public JsonResult GetBPartnerOptions(int bpartnerId)
        {
            if (Session["ctx"] == null) { return Json("", JsonRequestBehavior.AllowGet); }
            Ctx ctx = Session["ctx"] as Ctx;

            try
            {
                BPartnerOptions data = LoadBPartnerOptions(ctx, bpartnerId);
                return Json(JsonConvert.SerializeObject(data), JsonRequestBehavior.AllowGet);
            }
            catch (Exception ex)
            {
                Log.Log(Level.SEVERE, "VAS_289_OpenSalesQuotationsWidget.GetBPartnerOptions", ex);
                return ErrorResult(ctx);
            }
        }

        private BPartnerOptions LoadBPartnerOptions(Ctx ctx, int bpartnerId)
        {
            BPartnerOptions data = new BPartnerOptions { Locations = new List<OptionItem>(), Contacts = new List<OptionItem>() };
            if (ctx == null || bpartnerId <= 0) { return data; }

            /* C_Location has no Name column (Address1/Address2/City/Postal only) - the
               location's display label is its address, matching the classic UI's
               BPartner Location combo (Address1, falling back to City). */
            string locSql = @"
                SELECT bl.C_BPartner_Location_ID AS Id, COALESCE(l.Address1, l.City) AS Name
                  FROM C_BPartner_Location bl
                  LEFT OUTER JOIN C_Location l ON ( l.C_Location_ID = bl.C_Location_ID )
                 WHERE bl.C_BPartner_ID = @BPartner_ID
                   AND bl.IsActive = 'Y'
                 ORDER BY COALESCE(l.Address1, l.City)";
            IDataReader dr = null;
            try
            {
                dr = DB.ExecuteReader(locSql, new[] { new SqlParameter("@BPartner_ID", bpartnerId) });
                while (dr != null && dr.Read())
                {
                    data.Locations.Add(new OptionItem { Id = Util.GetValueOfInt(dr["Id"]), Name = Util.GetValueOfString(dr["Name"]) });
                }
            }
            finally { CloseReader(dr); }

            string contactSql = @"
                SELECT u.AD_User_ID AS Id, u.Name AS Name
                  FROM AD_User u
                 WHERE u.C_BPartner_ID = @BPartner_ID
                   AND u.IsActive = 'Y'
                 ORDER BY u.Name";
            IDataReader dr2 = null;
            try
            {
                dr2 = DB.ExecuteReader(contactSql, new[] { new SqlParameter("@BPartner_ID", bpartnerId) });
                while (dr2 != null && dr2.Read())
                {
                    data.Contacts.Add(new OptionItem { Id = Util.GetValueOfInt(dr2["Id"]), Name = Util.GetValueOfString(dr2["Name"]) });
                }
            }
            finally { CloseReader(dr2); }

            return data;
        }

        /// <summary>True when docTypeId is a real, active DocBaseType='SOO' document type for this client - guards the client-supplied Step 3 selection before it is trusted.</summary>
        private bool IsValidSalesOrderDocType(Ctx ctx, int docTypeId)
        {
            string sql = @"
                SELECT COUNT(*) FROM C_DocType
                 WHERE C_DocType_ID = @DocTypeId AND IsActive = 'Y' AND DocBaseType = 'SOO' AND AD_Client_ID IN (0, @AD_Client_ID)";
            return Util.GetValueOfInt(DB.ExecuteScalar(sql, new[]
            {
                new SqlParameter("@DocTypeId", docTypeId),
                new SqlParameter("@AD_Client_ID", ctx.GetAD_Client_ID())
            }, null)) > 0;
        }

        /// <summary>Resolves a valid Sales Order target document type (DocBaseType='SOO') for this client - never the quotation's own document type.</summary>
        private int ResolveSalesOrderDocType(Ctx ctx)
        {
            string sql = @"
                SELECT C_DocType_ID FROM C_DocType
                 WHERE IsActive = 'Y' AND DocBaseType = 'SOO' AND AD_Client_ID IN (0, @AD_Client_ID)
                 ORDER BY AD_Client_ID DESC, IsDefault DESC, C_DocType_ID ASC";
            return Util.GetValueOfInt(DB.ExecuteScalar(sql, new[] { new SqlParameter("@AD_Client_ID", ctx.GetAD_Client_ID()) }, null));
        }

        private static readonly ConcurrentDictionary<string, bool> ColumnExistsCache = new ConcurrentDictionary<string, bool>(StringComparer.OrdinalIgnoreCase);

        private bool ColumnExists(string tableName, string columnName)
        {
            string key = tableName + "." + columnName;
            bool cached;
            if (ColumnExistsCache.TryGetValue(key, out cached)) { return cached; }

            int count = Util.GetValueOfInt(DB.ExecuteScalar(
                "SELECT COUNT(*) FROM AD_Column c INNER JOIN AD_Table t ON (c.AD_Table_ID = t.AD_Table_ID) WHERE t.TableName = @Tbl AND c.ColumnName = @Col AND c.IsActive = 'Y'",
                new[] { new SqlParameter("@Tbl", tableName), new SqlParameter("@Col", columnName) }, null));

            bool exists = count > 0;
            ColumnExistsCache[key] = exists;
            return exists;
        }

        /// <summary>
        /// VA009_PaymentMethod's display-name column is not guaranteed - some deployments
        /// carry VA009_Name, some Name, some only Value. Same fallback chain as
        /// VAS_033_PaymentMethodsWidgetController.GetPaymentMethodNameColumn.
        /// </summary>
        private string ResolvePaymentMethodNameColumn()
        {
            if (ColumnExists("VA009_PaymentMethod", "VA009_Name")) { return "VA009_Name"; }
            if (ColumnExists("VA009_PaymentMethod", "Name")) { return "Name"; }
            if (ColumnExists("VA009_PaymentMethod", "Value")) { return "Value"; }
            return string.Empty;
        }

        private FormOptions LoadFormOptions(Ctx ctx)
        {
            FormOptions options = new FormOptions();
            int clientId = ctx.GetAD_Client_ID();

            options.DocTypes = LoadOptionList("SELECT C_DocType_ID AS Id, Name AS Name FROM C_DocType WHERE IsActive='Y' AND DocBaseType='SOO' AND AD_Client_ID IN (0, @c) ORDER BY Name", clientId);
            options.PaymentTerms = LoadOptionList("SELECT C_PaymentTerm_ID AS Id, Name AS Name FROM C_PaymentTerm WHERE IsActive='Y' AND AD_Client_ID IN (0, @c) ORDER BY Name", clientId);
            options.PriceLists = LoadOptionList("SELECT M_PriceList_ID AS Id, Name AS Name FROM M_PriceList WHERE IsActive='Y' AND IsSOPriceList='Y' AND AD_Client_ID IN (0, @c) ORDER BY Name", clientId);
            options.Currencies = LoadCurrencyOptions(clientId);
            options.ConversionTypes = LoadOptionList("SELECT C_ConversionType_ID AS Id, Name AS Name FROM C_ConversionType WHERE IsActive='Y' AND AD_Client_ID IN (0, @c) ORDER BY Name", clientId);
            options.IncoTerms = LoadOptionList("SELECT C_IncoTerm_ID AS Id, Name AS Name FROM C_IncoTerm WHERE IsActive='Y' ORDER BY Name", clientId);
            options.Warehouses = LoadOptionList("SELECT M_Warehouse_ID AS Id, Name AS Name FROM M_Warehouse WHERE IsActive='Y' AND AD_Client_ID = @c ORDER BY Name", clientId);
            options.Taxes = LoadOptionList("SELECT C_Tax_ID AS Id, Name AS Name FROM C_Tax WHERE IsActive='Y' AND COALESCE(IsSummary,'N')='N' AND AD_Client_ID IN (0, @c) ORDER BY Name", clientId);

            options.DeliveryRules = LoadRefListOptions("C_Order", "DeliveryRule");
            options.DeliveryViaRules = LoadRefListOptions("C_Order", "DeliveryViaRule");
            options.PriorityRules = LoadRefListOptions("C_Order", "PriorityRule");

            if (ColumnExists("C_Order", "VA009_PaymentMethod_ID") && ColumnExists("VA009_PaymentMethod", "VA009_PaymentMethod_ID"))
            {
                string nameCol = ResolvePaymentMethodNameColumn();
                options.PaymentMethods = string.IsNullOrEmpty(nameCol)
                    ? new List<OptionItem>()
                    : LoadOptionList("SELECT VA009_PaymentMethod_ID AS Id, " + nameCol + " AS Name FROM VA009_PaymentMethod WHERE IsActive='Y' AND AD_Client_ID IN (0, @c) ORDER BY " + nameCol, clientId);
            }
            else
            {
                options.PaymentMethods = new List<OptionItem>();
            }

            return options;
        }

        private List<OptionItem> LoadOptionList(string sql, int clientId)
        {
            List<OptionItem> list = new List<OptionItem>();
            IDataReader dr = null;
            try
            {
                dr = DB.ExecuteReader(sql, new[] { new SqlParameter("@c", clientId) });
                while (dr != null && dr.Read())
                {
                    list.Add(new OptionItem { Id = Util.GetValueOfInt(dr["Id"]), Name = Util.GetValueOfString(dr["Name"]) });
                }
            }
            catch (Exception ex)
            {
                Log.Log(Level.WARNING, "VAS_289_OpenSalesQuotationsWidget.LoadOptionList", ex);
            }
            finally { CloseReader(dr); }
            return list;
        }

        private List<CurrencyOption> LoadCurrencyOptions(int clientId)
        {
            List<CurrencyOption> list = new List<CurrencyOption>();
            string sql = "SELECT C_Currency_ID AS Id, ISO_Code AS Code, CurSymbol AS Symbol FROM C_Currency WHERE IsActive='Y' ORDER BY ISO_Code";
            IDataReader dr = null;
            try
            {
                dr = DB.ExecuteReader(sql, new SqlParameter[0]);
                while (dr != null && dr.Read())
                {
                    list.Add(new CurrencyOption { Id = Util.GetValueOfInt(dr["Id"]), Code = Util.GetValueOfString(dr["Code"]), Symbol = Util.GetValueOfString(dr["Symbol"]) });
                }
            }
            finally { CloseReader(dr); }
            return list;
        }

        /// <summary>Real AD_Ref_List values for a List-reference column - never a hardcoded mock vocabulary.</summary>
        private List<OptionItem> LoadRefListOptions(string tableName, string columnName)
        {
            List<OptionItem> list = new List<OptionItem>();
            string sql = @"
                SELECT rl.Value AS Id, COALESCE(rl.Name, rl.Value) AS Name
                  FROM AD_Ref_List rl
                  INNER JOIN AD_Column c ON ( c.AD_Reference_Value_ID = rl.AD_Reference_ID )
                  INNER JOIN AD_Table t ON ( c.AD_Table_ID = t.AD_Table_ID )
                 WHERE t.TableName = @Tbl AND c.ColumnName = @Col
                   AND rl.IsActive = 'Y' AND c.IsActive = 'Y'
                 ORDER BY rl.Value";
            IDataReader dr = null;
            try
            {
                dr = DB.ExecuteReader(sql, new[] { new SqlParameter("@Tbl", tableName), new SqlParameter("@Col", columnName) });
                while (dr != null && dr.Read())
                {
                    list.Add(new OptionItem { Code = Util.GetValueOfString(dr["Id"]), Name = Util.GetValueOfString(dr["Name"]) });
                }
            }
            finally { CloseReader(dr); }
            return list;
        }

        #endregion

        #region Step 5 - create

        /// <summary>
        /// Transactional Sales Order creation from selected quotation lines. Revalidates
        /// the quotation and every selected line's current pending quantity immediately
        /// before writing, inside the same transaction, so a concurrent conversion by
        /// another user surfaces as a conflict rather than silently under-filling the
        /// request. Uses MOrder/MOrderLine exclusively - never a hand-written INSERT.
        /// </summary>
        [HttpPost]
        [AjaxAuthorizeAttribute]
        [AjaxSessionFilterAttribute]
        public JsonResult CreateSalesOrder(string requestJson)
        {
            if (Session["ctx"] == null) { return Json("", JsonRequestBehavior.AllowGet); }
            Ctx ctx = Session["ctx"] as Ctx;

            CreateOrderRequest request;
            try
            {
                request = JsonConvert.DeserializeObject<CreateOrderRequest>(requestJson ?? "{}");
            }
            catch (Exception ex)
            {
                Log.Log(Level.WARNING, "VAS_289_OpenSalesQuotationsWidget.CreateSalesOrder(parse)", ex);
                return ConflictResult(ctx, "Invalid request.", null);
            }

            if (request == null || request.QuotationId <= 0 || request.Lines == null || request.Lines.Count == 0)
            {
                return ConflictResult(ctx, Msg.GetMsg(ctx, "VAS_289_SelectLinesRequired") ?? "Select at least one line to order.", null);
            }
            if (request.WarehouseId <= 0)
            {
                return ConflictResult(ctx, Msg.GetMsg(ctx, "VAS_289_WarehouseRequired") ?? "A ship-from warehouse is required.", null);
            }

            Trx trx = null;
            try
            {
                trx = Trx.Get("VAS_289_Convert_" + DateTime.Now.Ticks);

                // 1) Quotation must still exist, be active, a quotation, firm, and not expired.
                string qSql = @"
                    SELECT q.C_Order_ID, q.AD_Org_ID, q.C_BPartner_ID, q.DocStatus, q.ValidTillDate
                      FROM C_Order q
                     WHERE q.C_Order_ID = @Quotation_Id
                       AND q.AD_Client_ID = @AD_Client_ID
                       AND q.IsActive = 'Y'
                       AND q.IsSalesQuotation = 'Y'";
                DataSet qds = DB.ExecuteDataset(qSql, new[]
                {
                    new SqlParameter("@Quotation_Id", request.QuotationId),
                    new SqlParameter("@AD_Client_ID", ctx.GetAD_Client_ID())
                }, trx);

                if (qds == null || qds.Tables.Count == 0 || qds.Tables[0].Rows.Count == 0)
                {
                    trx.Rollback();
                    return ConflictResult(ctx, Msg.GetMsg(ctx, "VAS_289_QuotationNotFound") ?? "The quotation could not be found.", null);
                }

                DataRow qRow = qds.Tables[0].Rows[0];
                string quotationDocStatus = Util.GetValueOfString(qRow["DocStatus"]);
                DateTime? validTill = qRow["ValidTillDate"] == DBNull.Value ? (DateTime?)null : Util.GetValueOfDateTime(qRow["ValidTillDate"]);
                int orgId = Util.GetValueOfInt(qRow["AD_Org_ID"]);

                if (quotationDocStatus != "CO")
                {
                    trx.Rollback();
                    return ConflictResult(ctx, Msg.GetMsg(ctx, "VAS_289_QuotationNotReady") ?? "This quotation is awaiting approval and cannot be converted yet.", null);
                }
                if (validTill.HasValue && validTill.Value.Date < DateTime.Today)
                {
                    trx.Rollback();
                    return ConflictResult(ctx, Msg.GetMsg(ctx, "VAS_289_QuotationExpired") ?? "This quotation has expired.", null);
                }

                // 2) Revalidate every selected line's CURRENT pending quantity, fresh, inside this transaction.
                List<ConflictLine> conflicts = new List<ConflictLine>();
                Dictionary<int, QuotationLineSnapshot> snapshots = new Dictionary<int, QuotationLineSnapshot>();

                foreach (CreateOrderLineInput lineInput in request.Lines)
                {
                    string lineSql = @"
                        SELECT ql.C_OrderLine_ID, ql.C_Order_ID, ql.M_Product_ID, ql.M_AttributeSetInstance_ID,
                               ql.C_UOM_ID, ql.QtyOrdered, ql.PriceActual, ql.C_Tax_ID, ql.DatePromised,
                               ql.Description, ql.PrintDescription, ql.M_Warehouse_ID,
                               ( SELECT COALESCE(SUM(sol.QtyOrdered), 0)
                                   FROM C_OrderLine sol
                                   INNER JOIN C_Order so ON ( so.C_Order_ID = sol.C_Order_ID
                                                          AND so.IsActive = 'Y' AND so.IsSOTrx = 'Y'
                                                          AND COALESCE(so.IsReturnTrx, 'N') = 'N'
                                                          AND COALESCE(so.IsSalesQuotation, 'N') = 'N'
                                                          AND so.DocStatus NOT IN ('VO', 'RE') )
                                  WHERE sol.C_Quotation_Line_ID = ql.C_OrderLine_ID
                                    AND sol.IsActive = 'Y'
                               ) AS Already_Ordered_Qty
                          FROM C_OrderLine ql
                         WHERE ql.C_OrderLine_ID = @Line_Id
                           AND ql.C_Order_ID = @Quotation_Id
                           AND ql.IsActive = 'Y'";
                    DataSet lds = DB.ExecuteDataset(lineSql, new[]
                    {
                        new SqlParameter("@Line_Id", lineInput.QuotationLineId),
                        new SqlParameter("@Quotation_Id", request.QuotationId)
                    }, trx);

                    if (lds == null || lds.Tables.Count == 0 || lds.Tables[0].Rows.Count == 0)
                    {
                        conflicts.Add(new ConflictLine { QuotationLineId = lineInput.QuotationLineId, Reason = "Line no longer belongs to this quotation.", CurrentPendingQty = 0m });
                        continue;
                    }

                    DataRow lRow = lds.Tables[0].Rows[0];
                    decimal quoted = Util.GetValueOfDecimal(lRow["QtyOrdered"]);
                    decimal already = Util.GetValueOfDecimal(lRow["Already_Ordered_Qty"]);
                    decimal pending = Math.Max(0m, quoted - already);

                    if (lineInput.Qty <= 0 || lineInput.Qty > pending)
                    {
                        conflicts.Add(new ConflictLine { QuotationLineId = lineInput.QuotationLineId, Reason = "Pending quantity changed.", CurrentPendingQty = pending });
                        continue;
                    }

                    DateTime? overrideDatePromised = null;
                    DateTime parsedDatePromised;
                    if (!string.IsNullOrEmpty(lineInput.DatePromised) && DateTime.TryParse(lineInput.DatePromised, CultureInfo.InvariantCulture, DateTimeStyles.None, out parsedDatePromised))
                    {
                        overrideDatePromised = parsedDatePromised;
                    }

                    snapshots[lineInput.QuotationLineId] = new QuotationLineSnapshot
                    {
                        LineId = Util.GetValueOfInt(lRow["C_OrderLine_ID"]),
                        ProductId = Util.GetValueOfInt(lRow["M_Product_ID"]),
                        AsiId = lRow["M_AttributeSetInstance_ID"] == DBNull.Value ? 0 : Util.GetValueOfInt(lRow["M_AttributeSetInstance_ID"]),
                        UomId = Util.GetValueOfInt(lRow["C_UOM_ID"]),
                        Rate = Util.GetValueOfDecimal(lRow["PriceActual"]),
                        // Step 4 lets the user override tax and date promised per line; 0/blank keeps the quotation line's own value.
                        TaxId = lineInput.TaxId > 0 ? lineInput.TaxId : (lRow["C_Tax_ID"] == DBNull.Value ? 0 : Util.GetValueOfInt(lRow["C_Tax_ID"])),
                        DatePromised = overrideDatePromised ?? (lRow["DatePromised"] == DBNull.Value ? (DateTime?)null : Util.GetValueOfDateTime(lRow["DatePromised"])),
                        Description = Util.GetValueOfString(lRow["Description"]),
                        PrintDescription = Util.GetValueOfString(lRow["PrintDescription"]),
                        Qty = lineInput.Qty
                    };
                }

                if (conflicts.Count > 0)
                {
                    trx.Rollback();
                    return ConflictResult(ctx, Msg.GetMsg(ctx, "VAS_289_LinesChanged") ?? "Some selected lines changed since this screen loaded. Please review and try again.", conflicts);
                }

                // 3) Create the Sales Order header. A client-supplied doc type is revalidated against
                // the real SOO list for this client - never trusted blindly.
                int docTypeId = (request.DocTypeId > 0 && IsValidSalesOrderDocType(ctx, request.DocTypeId))
                    ? request.DocTypeId
                    : ResolveSalesOrderDocType(ctx);
                if (docTypeId <= 0)
                {
                    trx.Rollback();
                    return ConflictResult(ctx, Msg.GetMsg(ctx, "VAS_289_NoDocType") ?? "No Sales Order document type is configured for this client.", null);
                }

                MOrder newOrder = new MOrder(ctx, 0, trx);
                newOrder.SetAD_Client_ID(ctx.GetAD_Client_ID());
                newOrder.SetAD_Org_ID(orgId);
                newOrder.SetIsSOTrx(true);
                newOrder.Set_Value("IsSalesQuotation", false);
                newOrder.Set_Value("IsReturnTrx", false);
                newOrder.SetC_BPartner_ID(request.BusinessPartnerId);
                if (request.LocationId > 0) { newOrder.SetC_BPartner_Location_ID(request.LocationId); }
                if (request.ContactId > 0) { newOrder.SetAD_User_ID(request.ContactId); }
                if (request.SalesRepId > 0) { newOrder.SetSalesRep_ID(request.SalesRepId); }
                newOrder.SetM_Warehouse_ID(request.WarehouseId);
                if (!string.IsNullOrEmpty(request.PriorityRule)) { newOrder.SetPriorityRule(request.PriorityRule); }
                DateTime datePromised;
                newOrder.SetDatePromised(DateTime.TryParse(request.DatePromised, CultureInfo.InvariantCulture, DateTimeStyles.None, out datePromised) ? datePromised : DateTime.Today);
                if (!string.IsNullOrEmpty(request.POReference)) { newOrder.SetPOReference(request.POReference); }
                if (request.PaymentTermId > 0) { newOrder.SetC_PaymentTerm_ID(request.PaymentTermId); }
                if (request.PaymentMethodId > 0 && newOrder.Get_ColumnIndex("VA009_PaymentMethod_ID") >= 0)
                {
                    newOrder.Set_Value("VA009_PaymentMethod_ID", request.PaymentMethodId);
                }
                if (!string.IsNullOrEmpty(request.DeliveryRule) && newOrder.Get_ColumnIndex("DeliveryRule") >= 0)
                {
                    newOrder.Set_Value("DeliveryRule", request.DeliveryRule);
                }
                if (!string.IsNullOrEmpty(request.DeliveryViaRule)) { newOrder.SetDeliveryViaRule(request.DeliveryViaRule); }
                if (request.PriceListId > 0) { newOrder.SetM_PriceList_ID(request.PriceListId); }
                if (request.CurrencyId > 0) { newOrder.SetC_Currency_ID(request.CurrencyId); }
                if (request.ConversionTypeId > 0) { newOrder.SetC_ConversionType_ID(request.ConversionTypeId); }
                if (request.IncoTermId > 0 && newOrder.Get_ColumnIndex("C_IncoTerm_ID") >= 0) { newOrder.SetC_IncoTerm_ID(request.IncoTermId); }
                if (!string.IsNullOrEmpty(request.Description)) { newOrder.SetDescription(request.Description); }

                DateTime dateOrdered;
                if (!string.IsNullOrEmpty(request.DateOrdered) && DateTime.TryParse(request.DateOrdered, CultureInfo.InvariantCulture, DateTimeStyles.None, out dateOrdered))
                {
                    newOrder.SetDateOrdered(dateOrdered);
                }
                else
                {
                    dateOrdered = DateTime.Today;
                    newOrder.SetDateOrdered(dateOrdered);
                }
                if (newOrder.Get_ColumnIndex("DateAcct") >= 0) { newOrder.SetDateAcct(dateOrdered); }

                newOrder.SetC_DocType_ID(docTypeId);
                newOrder.SetC_DocTypeTarget_ID(docTypeId);
                if (newOrder.Get_ColumnIndex("C_Order_Quotation") >= 0)
                {
                    newOrder.Set_Value("C_Order_Quotation", request.QuotationId);
                }

                newOrder.SetDocStatus(MOrder.DOCSTATUS_Drafted);
                newOrder.SetDocAction(MOrder.DOCACTION_Complete);

                if (!newOrder.Save(trx))
                {
                    trx.Rollback();
                    return ConflictResult(ctx, GetSaveError(ctx, "VAS_289_OrderNotSaved", "The Sales Order could not be saved."), null);
                }

                // 4) Create the Sales Order lines - source product/quotation reference are fixed.
                int lineSeq = 10;
                decimal orderTotal = 0m;
                foreach (CreateOrderLineInput lineInput in request.Lines)
                {
                    QuotationLineSnapshot snap = snapshots[lineInput.QuotationLineId];

                    MOrderLine ol = new MOrderLine(newOrder);
                    ol.SetLine(lineSeq);
                    lineSeq += 10;
                    ol.SetM_Product_ID(snap.ProductId);
                    if (snap.AsiId > 0) { ol.SetM_AttributeSetInstance_ID(snap.AsiId); }
                    ol.SetC_UOM_ID(snap.UomId);
                    ol.SetQtyEntered(snap.Qty);
                    ol.SetQtyOrdered(snap.Qty);
                    ol.SetPriceEntered(snap.Rate);
                    ol.SetPriceActual(snap.Rate);
                    if (snap.TaxId > 0) { ol.SetC_Tax_ID(snap.TaxId); }
                    ol.SetDatePromised(snap.DatePromised ?? newOrder.GetDatePromised());
                    if (!string.IsNullOrEmpty(snap.Description)) { ol.SetDescription(snap.Description); }
                    if (!string.IsNullOrEmpty(snap.PrintDescription) && ol.Get_ColumnIndex("PrintDescription") >= 0)
                    {
                        ol.Set_Value("PrintDescription", snap.PrintDescription);
                    }
                    ol.SetM_Warehouse_ID(request.WarehouseId);
                    decimal lineNet = Math.Round(snap.Qty * snap.Rate, 2);
                    ol.SetLineNetAmt(lineNet);
                    orderTotal += lineNet;
                    if (ol.Get_ColumnIndex("C_Quotation_Line_ID") >= 0)
                    {
                        ol.Set_Value("C_Quotation_Line_ID", snap.LineId);
                    }

                    if (!ol.Save(trx))
                    {
                        trx.Rollback();
                        return ConflictResult(ctx, GetSaveError(ctx, "VAS_289_LineNotSaved", "A Sales Order line could not be saved."), null);
                    }
                }

                newOrder.SetTotalLines(orderTotal);
                newOrder.Save(trx);

                trx.Commit();

                return Json(JsonConvert.SerializeObject(new
                {
                    Success = true,
                    SalesOrderId = newOrder.GetC_Order_ID(),
                    SalesOrderNumber = newOrder.GetDocumentNo(),
                    BusinessPartnerName = request.BusinessPartnerName,
                    LineCount = request.Lines.Count,
                    OrderTotal = orderTotal
                }), JsonRequestBehavior.AllowGet);
            }
            catch (Exception ex)
            {
                if (trx != null) { trx.Rollback(); }
                Log.Log(Level.SEVERE, "VAS_289_OpenSalesQuotationsWidget.CreateSalesOrder", ex);
                return ConflictResult(ctx, Msg.GetMsg(ctx, "Error") ?? "Error", null);
            }
            finally
            {
                if (trx != null) { trx.Close(); }
            }
        }

        private string GetSaveError(Ctx ctx, string fallbackKey, string fallback)
        {
            ValueNamePair pp = VLogger.RetrieveError();
            string error = pp != null ? pp.GetName() : "";
            if (string.IsNullOrEmpty(error)) { error = pp != null ? Msg.GetMsg(ctx, pp.GetValue()) : ""; }
            if (string.IsNullOrEmpty(error)) { error = Msg.GetMsg(ctx, fallbackKey); }
            return string.IsNullOrEmpty(error) ? fallback : error;
        }

        private JsonResult ConflictResult(Ctx ctx, string message, List<ConflictLine> conflicts)
        {
            string json = JsonConvert.SerializeObject(new { Error = message, Conflicts = conflicts });
            return Json(json, JsonRequestBehavior.AllowGet);
        }

        #endregion

        #region Shared helpers

        private void CloseReader(IDataReader reader)
        {
            if (reader == null) { return; }
            reader.Close();
            reader.Dispose();
        }

        private JsonResult ErrorResult(Ctx ctx)
        {
            string message = Msg.GetMsg(ctx, "Error") ?? "Error";
            string json = JsonConvert.SerializeObject(new { Error = message });
            return Json(json, JsonRequestBehavior.AllowGet);
        }

        #endregion

        #region DTOs

        private class OpenQuotationsResult
        {
            public int ReadyCount { get; set; }
            public int Total { get; set; }
            public List<QuotationRow> Rows { get; set; }
        }

        private class QuotationRow
        {
            public int QuotationId { get; set; }
            public string DocumentNo { get; set; }
            public string BusinessPartner { get; set; }
            public int LineCount { get; set; }
            public decimal PendingQty { get; set; }
            public string ValidTillDate { get; set; }
            public string DocStatus { get; set; }
            public string StatusLabel { get; set; }
            public string StatusChipClass { get; set; }
            public bool IsConvertible { get; set; }
        }

        private class QuotationLinesResult
        {
            public QuotationHeader Quotation { get; set; }
            public List<QuotationLineRow> Lines { get; set; }
        }

        private class QuotationHeader
        {
            public int QuotationId { get; set; }
            public string DocumentNo { get; set; }
            public int BusinessPartnerId { get; set; }
            public string BusinessPartner { get; set; }
            public string BusinessPartnerGroup { get; set; }
            public int WarehouseId { get; set; }
            public string ValidTillDate { get; set; }
            public string DocStatus { get; set; }
            public string StatusLabel { get; set; }
            public string StatusChipClass { get; set; }
            public int LineCount { get; set; }
            public decimal QuotedQty { get; set; }
            public decimal AlreadyOrderedQty { get; set; }
            public decimal PendingQty { get; set; }
        }

        private class QuotationLineRow
        {
            public int LineId { get; set; }
            public int ProductId { get; set; }
            public string ProductName { get; set; }
            public bool IsStocked { get; set; }
            public int AttributeSetInstanceId { get; set; }
            public string AttributeText { get; set; }
            public int UomId { get; set; }
            public string UomName { get; set; }
            public decimal QuotedQty { get; set; }
            public decimal AlreadyOrderedQty { get; set; }
            public decimal PendingQty { get; set; }
            public decimal Rate { get; set; }
            public int TaxId { get; set; }
            public string DatePromised { get; set; }
            public string Description { get; set; }
            public string PrintDescription { get; set; }
            public int WarehouseId { get; set; }
            public decimal? FreeStock { get; set; }
        }

        private class OrderFormDefaultsResult
        {
            public OrderDefaults Defaults { get; set; }
            public FormOptions Options { get; set; }
        }

        private class OrderDefaults
        {
            public int BusinessPartnerId { get; set; }
            public string BusinessPartnerName { get; set; }
            public int LocationId { get; set; }
            public string LocationName { get; set; }
            public int ContactId { get; set; }
            public string ContactName { get; set; }
            public int SalesRepId { get; set; }
            public string SalesRepName { get; set; }
            public int WarehouseId { get; set; }
            public string PriorityRule { get; set; }
            public string DateOrdered { get; set; }
            public string DatePromised { get; set; }
            public string POReference { get; set; }
            public int PaymentTermId { get; set; }
            public string DeliveryRule { get; set; }
            public string DeliveryViaRule { get; set; }
            public int PriceListId { get; set; }
            public int CurrencyId { get; set; }
            public int ConversionTypeId { get; set; }
            public int IncoTermId { get; set; }
            public string Description { get; set; }
            public int DocTypeTargetId { get; set; }
        }

        private class FormOptions
        {
            public List<OptionItem> DocTypes { get; set; }
            public List<OptionItem> PaymentTerms { get; set; }
            public List<OptionItem> PaymentMethods { get; set; }
            public List<OptionItem> Warehouses { get; set; }
            public List<OptionItem> Taxes { get; set; }
            public List<OptionItem> DeliveryRules { get; set; }
            public List<OptionItem> DeliveryViaRules { get; set; }
            public List<OptionItem> PriorityRules { get; set; }
            public List<OptionItem> PriceLists { get; set; }
            public List<CurrencyOption> Currencies { get; set; }
            public List<OptionItem> ConversionTypes { get; set; }
            public List<OptionItem> IncoTerms { get; set; }
            public List<OptionItem> Locations { get; set; }
            public List<OptionItem> Contacts { get; set; }
        }

        private class BPartnerOptions
        {
            public List<OptionItem> Locations { get; set; }
            public List<OptionItem> Contacts { get; set; }
        }

        private class OptionItem
        {
            public int Id { get; set; }
            public string Code { get; set; }
            public string Name { get; set; }
        }

        private class CurrencyOption
        {
            public int Id { get; set; }
            public string Code { get; set; }
            public string Symbol { get; set; }
        }

        private class CreateOrderRequest
        {
            public int QuotationId { get; set; }
            public int BusinessPartnerId { get; set; }
            public string BusinessPartnerName { get; set; }
            public int LocationId { get; set; }
            public int ContactId { get; set; }
            public int SalesRepId { get; set; }
            public int WarehouseId { get; set; }
            public string PriorityRule { get; set; }
            public string DatePromised { get; set; }
            public string POReference { get; set; }
            public int PaymentTermId { get; set; }
            public int PaymentMethodId { get; set; }
            public string DeliveryRule { get; set; }
            public string DeliveryViaRule { get; set; }
            public int PriceListId { get; set; }
            public int CurrencyId { get; set; }
            public int ConversionTypeId { get; set; }
            public int IncoTermId { get; set; }
            public string Description { get; set; }
            public int DocTypeId { get; set; }
            public string DateOrdered { get; set; }
            public List<CreateOrderLineInput> Lines { get; set; }
        }

        private class CreateOrderLineInput
        {
            public int QuotationLineId { get; set; }
            public decimal Qty { get; set; }
            /// <summary>Optional per-line override from Step 4 (line editing); 0/null keeps the quotation line's own value.</summary>
            public int TaxId { get; set; }
            /// <summary>Optional per-line override from Step 4; blank keeps the quotation line's own value.</summary>
            public string DatePromised { get; set; }
        }

        private class QuotationLineSnapshot
        {
            public int LineId { get; set; }
            public int ProductId { get; set; }
            public int AsiId { get; set; }
            public int UomId { get; set; }
            public decimal Rate { get; set; }
            public int TaxId { get; set; }
            public DateTime? DatePromised { get; set; }
            public string Description { get; set; }
            public string PrintDescription { get; set; }
            public decimal Qty { get; set; }
        }

        private class ConflictLine
        {
            public int QuotationLineId { get; set; }
            public string Reason { get; set; }
            public decimal CurrentPendingQty { get; set; }
        }

        #endregion
    }
}

