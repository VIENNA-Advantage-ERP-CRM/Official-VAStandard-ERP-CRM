/************************************************************
 * Module Name    : VAS
 * Purpose        : Shared data layer of the Requisition screen widgets VAS_305 - VAS_315
 *                  (window VAS_Requisition) plus the requisition detail used by their popups.
 *
 *                  Every widget has its own controller and endpoint; this file only holds the
 *                  common read logic so all of them count a requisition line the same way:
 *
 *                    Ordered        = PO qty (C_OrderLine.M_RequisitionLine_ID, PO not VO/RE)
 *                                     + Material Transfer qty raised (M_MovementLine.M_RequisitionLine_ID,
 *                                     transfer not VO/RE)
 *                    Received       = GRN qty on those PO lines (C_OrderLine.QtyDelivered)
 *                                     + DTD001_DeliveredQty (completed transfers / internal use)
 *                    Not received   = (PO qty - GRN qty) + qty on transfers not yet completed
 *                    Not ordered    = Qty - PO qty - MAX(transfer qty, delivered qty)
 *                    System status  = live evaluation of the VAS_RequisitionStatus rule
 *                                     (MRequisition.UpdateRequisitionStatus): OP / PO / OR / PD / DE,
 *                                     fed with the Ordered / Received figures above. The stored column
 *                                     is not refreshed on every path, so it is recalculated here.
 *
 *                  SQL is ANSI (COALESCE / CASE / LEFT JOIN, no ROWNUM / LIMIT / NVL / TRUNC) so it
 *                  runs unchanged on Oracle and PostgreSQL. Dates are bound as parameters. Optional
 *                  columns (DTD001_*, AD_User_ID, requisition links) are checked in the dictionary
 *                  of the database first, like VAS_217 does, so a missing column degrades the figure
 *                  instead of failing the widget. Role access goes through a sub-query on
 *                  M_Requisition (MRole.AddAccessSQL cannot parse the aggregate statement).
 * Author         : VAS
 * Date           : 05 November 2026
 ***********************************************************/

using Newtonsoft.Json;
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Data;
using System.Data.SqlClient;
using System.Linq;
using System.Text;
using System.Web.Mvc;
using VAdvantage.DataBase;
using VAdvantage.Logging;
using VAdvantage.Model;
using VAdvantage.Utility;
using VIS.Filters;

namespace VIS.Controllers
{
    /// <summary>
    /// Requisition detail for the popups of the Requisition widgets (VAS_305 - VAS_315).
    /// </summary>
    public class VAS_RequisitionWidgetCommonController : Controller
    {
        /// <summary>
        /// Header, lines and fulfilment (POs, GRNs, transfers) of one requisition.
        /// </summary>
        /// <param name="requisitionId">M_Requisition_ID</param>
        /// <returns>JSON { header, lines, currency } or { error }</returns>
        [AjaxAuthorizeAttribute]
        [AjaxSessionFilterAttribute]
        public JsonResult GetRequisitionDetail(int requisitionId = 0)
        {
            Ctx ctx = Session["ctx"] as Ctx;
            if (ctx == null)
            {
                return VAS_ReqWidgetData.Error(Env.GetCtx(), "SessionExpired", "Session expired.");
            }
            if (requisitionId <= 0)
            {
                return VAS_ReqWidgetData.Error(ctx, "VAS_305_NoRecord", "Requisition not found.");
            }
            try
            {
                VAS_ReqCurrency cur = VAS_ReqWidgetData.GetSchemaCurrency(ctx);
                // VAS163 2026-11-13: row 1120 - the header is read on its own, so a requisition without lines
                // still opens the popup with its header details (the lines query needs at least one line).
                VAS_ReqLine h = VAS_ReqWidgetData.LoadHeader(ctx, requisitionId);
                if (h == null)
                {
                    return VAS_ReqWidgetData.Error(ctx, "VAS_305_NoRecord", "Requisition not found.");
                }
                List<SqlParameter> p = new List<SqlParameter> { new SqlParameter("@RQ_Req", requisitionId) };
                List<VAS_ReqLine> lines = VAS_ReqWidgetData.LoadLines(ctx, cur, "r.M_Requisition_ID = @RQ_Req", p);

                Dictionary<int, List<object>> docs = lines.Count > 0 ? VAS_ReqWidgetData.LoadFulfilmentDocs(requisitionId) : new Dictionary<int, List<object>>();
                DateTime today = DateTime.Now.Date;
                string status = VAS_ReqWidgetData.SystemStatus(lines);

                var header = new
                {
                    requisitionId = h.RequisitionId,
                    documentNo = h.DocumentNo,
                    docStatus = h.DocStatus,
                    docStatusName = VAS_ReqWidgetData.DocStatusName(ctx, h.DocStatus),
                    statusCode = status,
                    statusName = VAS_ReqWidgetData.StatusName(ctx, status),
                    dateDoc = VAS_ReqWidgetData.D(h.DateDoc),
                    // VAS163 2026-11-13: row 1122 - 'Raised' shows the creation date (M_Requisition.Created)
                    created = VAS_ReqWidgetData.D(h.Created),
                    dateRequired = VAS_ReqWidgetData.D(h.DateRequired),
                    // VAS163 2026-11-17: Sr 5 - the header field is the requisition's own Date Required (M_Requisition.DateRequired).
                    // It was the earliest line date (M_RequisitionLine.DTD001_DateRequired), which is not changed when the
                    // header date is edited, so the popup kept showing the old date (e.g. 900119: header 20-Dec, lines 20-Nov).
                    // The lines' own dates are still shown per line.
                    earliestNeedBy = VAS_ReqWidgetData.D(h.DateRequired
                                                   ?? lines.Where(l => l.OpenQty > 0 && l.NeedBy.HasValue).Select(l => l.NeedBy).Min()
                                                   ?? lines.Select(l => l.NeedBy).Min()),
                    requester = h.RequesterName,
                    warehouse = h.WarehouseName,
                    sourceWarehouse = h.SourceWarehouseName,
                    priority = VAS_ReqWidgetData.PriorityName(ctx, h.Priority),
                    description = h.Description,
                    isLate = lines.Any(l => l.IsLate(today)),
                    // VAS163 2026-11-17: Sr 14 / Sr 11 - Total = sum of the line amounts (LineNetAmt), Still to come = the
                    // share of each line amount not yet received (OpenQty / Qty, never above the line amount), both in
                    // the price-list currency of the requisition.
                    totalAmount = lines.Sum(l => l.DocAmount(l.Qty)),
                    openAmount = lines.Sum(l => l.DocAmount(l.OpenQty)),
                    lineCount = lines.Count,
                    receivedLines = lines.Count(l => l.Qty > 0 && l.OpenQty <= 0)
                };

                var lineRows = lines.Select(l => new
                {
                    lineId = l.LineId,
                    lineNo = l.LineNo,
                    product = l.ItemName,
                    productCode = l.ProductValue,
                    category = l.CategoryName,
                    attribute = l.AttributeName, // VAS163 2026-11-13: row 1130 - attribute set instance description (blank when none)
                    uom = l.UomName,
                    // VAS163 2026-11-17: Sr 7 - quantities in the line's own UOM (QtyEntered; ordered / received / pending
                    // are kept in the product UOM and converted with QtyEntered / Qty)
                    qty = l.QtyEntered,
                    // VAS163 2026-11-17: Sr 32 - 'Ord.' counts completed / closed POs and transfers only (a drafted PO /
                    // Material Transfer is not ordered yet), same figure the status uses
                    orderedQty = l.DoneOrderedQty * l.UomFactor,
                    receivedQty = l.ReceivedQty * l.UomFactor,
                    pendingQty = l.OpenQty * l.UomFactor,
                    // VAS163 2026-11-17: Sr 11 - line amount in the price-list currency
                    amount = l.DocAmount(l.Qty),
                    needBy = VAS_ReqWidgetData.D(l.NeedBy),
                    vendor = l.VendorName,
                    state = l.State(),
                    isLate = l.IsLate(today),
                    daysLate = l.NeedBy.HasValue ? (int)(today - l.NeedBy.Value.Date).TotalDays : 0,
                    documents = docs.ContainsKey(l.LineId) ? docs[l.LineId] : new List<object>()
                }).ToList();

                // VAS163 2026-11-17: Sr 11 - the popup shows the requisition in its price-list currency (schema currency
                // only when the requisition has no price list)
                VAS_ReqCurrency docCur = h.CurrencyId > 0
                    ? new VAS_ReqCurrency { Id = h.CurrencyId, Symbol = h.DocCurrencySymbol, Iso = h.DocCurrencyIso, Precision = h.DocCurrencyPrecision }
                    : cur;
                return Json(JsonConvert.SerializeObject(new { header, lines = lineRows, currency = docCur }), JsonRequestBehavior.AllowGet);
            }
            catch (Exception ex)
            {
                VAS_ReqWidgetData.Log.Log(Level.SEVERE, "VAS_RequisitionWidgetCommon.GetRequisitionDetail", ex);
                return VAS_ReqWidgetData.Error(ctx, "VAS_305_LoadError", "Could not load the data.");
            }
        }
    }

    /// <summary>Accounting-schema currency used for every amount on the Requisition widgets.</summary>
    public class VAS_ReqCurrency
    {
        public int Id { get; set; }
        public string Symbol { get; set; }
        public string Iso { get; set; }
        public int Precision { get; set; }
    }

    /// <summary>One requisition line with its fulfilment figures.</summary>
    public class VAS_ReqLine
    {
        public int RequisitionId, LineId, LineNo, ProductId, ChargeId, OrgId, CurrencyId, UserId, CreatedBy, CategoryId;
        public string DocumentNo, DocStatus, Priority, RequesterName, WarehouseName, SourceWarehouseName, Description,
            ProductName, ProductValue, ChargeName, UomName, CategoryName, VendorName, AttributeName;
        public DateTime? DateDoc, DateRequired, LineNeedBy, Created;
        /// <summary>PoDoneQty: qty on completed / closed POs only (VAS163 2026-11-13: row 1143).</summary>
        public decimal Qty, Price, PoQty, PoRcvQty, TrfQty, TrfOpenQty, DeliveredQty, PoDoneQty;
        /// <summary>Requisition currency to accounting-schema currency factor.</summary>
        public decimal Rate = 1m;
        /// <summary>
        /// VAS163 2026-11-17: Sr 7 / Sr 14 - QtyEntered (in the line UOM C_UOM_ID) and LineNetAmt (amount of the line in
        /// the price-list currency). Qty and all ordered / received figures are in the product UOM, while PriceActual is a
        /// price per entered UOM, so Qty * PriceActual was wrong for every line entered in another UOM (e.g. 2000 ml at
        /// 0.33 = 660, but Qty 2 L * 0.33 = 0.66).
        /// </summary>
        public decimal QtyEntered, LineNet;
        /// <summary>VAS163 2026-11-17: Sr 11 - price-list (document) currency of the requisition.</summary>
        public string DocCurrencyIso, DocCurrencySymbol;
        public int DocCurrencyPrecision = 2;

        /// <summary>VAS163 2026-11-17: Sr 7 - factor from the product UOM (Qty) to the line UOM (QtyEntered).</summary>
        public decimal UomFactor { get { return (Qty != 0m && QtyEntered != 0m) ? QtyEntered / Qty : 1m; } }

        public DateTime? NeedBy { get { return LineNeedBy ?? DateRequired; } }
        public string ItemName { get { return !string.IsNullOrEmpty(ProductName) ? ProductName : (ChargeName ?? ""); } }
        public decimal OrderedQty { get { return PoQty + TrfQty; } }
        public decimal NotOrderedQty { get { return Math.Max(0m, Qty - PoQty - Math.Max(TrfQty, DeliveredQty)); } }
        // VAS163 2026-11-13: row 1135 - an over-ordered line (e.g. PO 6 + transfer 5 for 6 requested) counted 11 in
        // progress, so "Still to come" exceeded the total; the in-progress part is capped at what is still missing.
        public decimal InProgressQty { get { return Math.Min(Math.Max(0m, PoQty - PoRcvQty) + TrfOpenQty, Math.Max(0m, Qty - ReceivedQty - NotOrderedQty)); } }
        public decimal ReceivedQty { get { return Math.Min(Qty, PoRcvQty + DeliveredQty); } }
        /// <summary>Quantity still to come: not ordered + ordered but not received; never above requested - received.</summary>
        public decimal OpenQty { get { return Math.Max(0m, Math.Min(Qty - ReceivedQty, NotOrderedQty + InProgressQty)); } }
        /// <summary>
        /// VAS163 2026-11-13: row 1143 - quantity not yet ordered for the STATUS: only completed / closed purchase
        /// orders and transfers (or delivered qty) count as ordered, so a drafted PO / transfer never shows "Ordered".
        /// The fulfilment figures above (not ordered, in progress) still count drafts, so "Lines to order" does not
        /// suggest a second PO for a line already on a draft PO.
        /// </summary>
        public decimal StatusNotOrderedQty { get { return Math.Max(0m, Qty - PoDoneQty - Math.Max(TrfQty - TrfOpenQty, DeliveredQty)); } }

        /// <summary>
        /// VAS163 2026-11-17: Sr 32 - ordered quantity shown on the popup line (product UOM): completed / closed purchase
        /// orders + completed transfers (or the delivered qty of completed transfers / inventory use, whichever is higher).
        /// Drafted documents are left out.
        /// </summary>
        public decimal DoneOrderedQty { get { return PoDoneQty + Math.Max(TrfQty - TrfOpenQty, DeliveredQty); } }

        /// <summary>Amount of a part of the line (qty in the product UOM), in the accounting-schema currency.</summary>
        public decimal Amount(decimal qty)
        {
            // VAS163 2026-11-17: Sr 14 - share of the line amount (LineNetAmt), not qty * PriceActual (price per entered UOM)
            return Math.Round(DocAmountRaw(qty) * Rate, 2);
        }

        /// <summary>VAS163 2026-11-17: Sr 11 / Sr 14 - amount of a part of the line in the price-list currency of the requisition.</summary>
        public decimal DocAmount(decimal qty)
        {
            return Math.Round(DocAmountRaw(qty), Math.Max(0, Math.Min(DocCurrencyPrecision, 10)));
        }

        private decimal DocAmountRaw(decimal qty)
        {
            if (Qty == 0m) { return 0m; }
            return qty == Qty ? LineNet : LineNet * qty / Qty;
        }

        public bool IsLate(DateTime today)
        {
            return OpenQty > 0 && NeedBy.HasValue && NeedBy.Value.Date < today;
        }

        /// <summary>received / notordered / partordered / ordered</summary>
        public string State()
        {
            if (OpenQty <= 0) { return "received"; }
            // VAS163 2026-11-13: row 1143 - ordered only through completed / closed documents
            if (StatusNotOrderedQty >= Qty) { return "notordered"; }
            if (StatusNotOrderedQty > 0) { return "partordered"; }
            return "ordered";
        }
    }

    /// <summary>
    /// Shared read logic of the Requisition widgets. Read-only: nothing here writes to the database.
    /// </summary>
    public static class VAS_ReqWidgetData
    {
        internal static readonly VLogger Log = VLogger.GetVLogger(typeof(VAS_ReqWidgetData).FullName);
        private static readonly ConcurrentDictionary<string, bool> ColumnCache = new ConcurrentDictionary<string, bool>();

        public const string StatusDraft = "DR";
        public const string StatusClosed = "CL";
        /// <summary>VAS163 2026-11-20: Sr 44 / Sr 57 - voided / reversed requisitions get their own bucket (no longer with Closed).</summary>
        public const string StatusVoided = "VO";

        /// <summary>yyyy-MM-dd or empty.</summary>
        public static string D(DateTime? d)
        {
            return d.HasValue ? d.Value.ToString("yyyy-MM-dd") : "";
        }

        public static JsonResult Error(Ctx ctx, string key, string fallback)
        {
            JsonResult r = new JsonResult();
            r.Data = JsonConvert.SerializeObject(new { error = MsgOr(ctx, key, fallback) });
            r.JsonRequestBehavior = JsonRequestBehavior.AllowGet;
            return r;
        }

        /// <summary>Msg.GetMsg returns the key itself when the message is missing; use readable text then.</summary>
        public static string MsgOr(Ctx ctx, string key, string fallback)
        {
            string msg = Msg.GetMsg(ctx, key);
            return (string.IsNullOrEmpty(msg) || msg == key || msg == "[" + key + "]") ? fallback : msg;
        }

        /// <summary>True when the column exists in this database (cached per process).</summary>
        public static bool HasColumn(string tableName, string columnName)
        {
            string key = (tableName + "." + columnName).ToUpperInvariant();
            bool found;
            if (ColumnCache.TryGetValue(key, out found))
            {
                return found;
            }
            string sql = DB.IsPostgreSQL()
                ? "SELECT COUNT(1) FROM information_schema.columns WHERE UPPER(table_name)=UPPER(@TableName) AND UPPER(column_name)=UPPER(@ColumnName)"
                : "SELECT COUNT(1) FROM USER_TAB_COLUMNS WHERE TABLE_NAME=UPPER(@TableName) AND COLUMN_NAME=UPPER(@ColumnName)";
            try
            {
                found = Util.GetValueOfInt(DB.ExecuteScalar(sql, new SqlParameter[]
                {
                    new SqlParameter("@TableName", tableName),
                    new SqlParameter("@ColumnName", columnName)
                }, null)) > 0;
            }
            catch (Exception ex)
            {
                Log.Warning("VAS_ReqWidgetData.HasColumn " + key + ": " + ex.Message);
                return false;
            }
            ColumnCache[key] = found;
            return found;
        }

        /// <summary>SQL expression of a line's need-by date (line date when the column exists, else the header date).</summary>
        public static string NeedBySql()
        {
            return HasColumn("M_RequisitionLine", "DTD001_DateRequired") ? "COALESCE(rl.DTD001_DateRequired, r.DateRequired)" : "r.DateRequired";
        }

        /// <summary>Condition "requested by the user": requester, or creator when no requester is set.</summary>
        public static string RequestedBySql()
        {
            return HasColumn("M_Requisition", "AD_User_ID")
                ? "(r.AD_User_ID = @RQ_User1 OR (r.AD_User_ID IS NULL AND r.CreatedBy = @RQ_User2))"
                : "(r.CreatedBy = @RQ_User2 OR r.CreatedBy = @RQ_User1)";
        }

        /// <summary>
        /// VAS163 2026-11-17: Sr 26 - condition (alias r = M_Requisition) "no document has been created against any line of
        /// the requisition": no purchase order line (incl. blanket PO), Material Transfer line, Inventory Use line or RFQ
        /// line / RFQ (header link) refers to it. Every document that is not voided / reversed counts, drafts included
        /// (the document has been created). Links whose column does not exist in this database are skipped.
        /// </summary>
        public static string NoDocumentSql()
        {
            StringBuilder s = new StringBuilder("1 = 1");
            if (HasColumn("C_OrderLine", "M_RequisitionLine_ID"))
            {
                s.Append(@" AND NOT EXISTS (SELECT 1 FROM M_RequisitionLine xl
                                INNER JOIN C_OrderLine xol ON xol.M_RequisitionLine_ID = xl.M_RequisitionLine_ID
                                INNER JOIN C_Order xo ON xo.C_Order_ID = xol.C_Order_ID
                                WHERE xl.M_Requisition_ID = r.M_Requisition_ID AND xol.IsActive = 'Y' AND xo.DocStatus NOT IN ('VO', 'RE'))");
            }
            if (HasColumn("M_MovementLine", "M_RequisitionLine_ID"))
            {
                s.Append(@" AND NOT EXISTS (SELECT 1 FROM M_RequisitionLine xl
                                INNER JOIN M_MovementLine xmv ON xmv.M_RequisitionLine_ID = xl.M_RequisitionLine_ID
                                INNER JOIN M_Movement xm ON xm.M_Movement_ID = xmv.M_Movement_ID
                                WHERE xl.M_Requisition_ID = r.M_Requisition_ID AND xmv.IsActive = 'Y' AND xm.DocStatus NOT IN ('VO', 'RE'))");
            }
            if (HasColumn("M_InventoryLine", "M_RequisitionLine_ID"))
            {
                s.Append(@" AND NOT EXISTS (SELECT 1 FROM M_RequisitionLine xl
                                INNER JOIN M_InventoryLine xil ON xil.M_RequisitionLine_ID = xl.M_RequisitionLine_ID
                                INNER JOIN M_Inventory xi ON xi.M_Inventory_ID = xil.M_Inventory_ID
                                WHERE xl.M_Requisition_ID = r.M_Requisition_ID AND xil.IsActive = 'Y' AND xi.DocStatus NOT IN ('VO', 'RE'))");
            }
            bool hasRfqStatus = HasColumn("C_RfQ", "DocStatus");
            string rfqAlive = hasRfqStatus ? " AND (xf.DocStatus IS NULL OR xf.DocStatus NOT IN ('VO', 'RE'))" : "";
            if (HasColumn("C_RfQLine", "M_RequisitionLine_ID"))
            {
                s.Append(@" AND NOT EXISTS (SELECT 1 FROM M_RequisitionLine xl
                                INNER JOIN C_RfQLine xq ON xq.M_RequisitionLine_ID = xl.M_RequisitionLine_ID
                                INNER JOIN C_RfQ xf ON xf.C_RfQ_ID = xq.C_RfQ_ID
                                WHERE xl.M_Requisition_ID = r.M_Requisition_ID AND xq.IsActive = 'Y' AND xf.IsActive = 'Y'" + rfqAlive + ")");
            }
            if (HasColumn("C_RfQ", "M_Requisition_ID"))
            {
                s.Append(@" AND NOT EXISTS (SELECT 1 FROM C_RfQ xf
                                WHERE xf.M_Requisition_ID = r.M_Requisition_ID AND xf.IsActive = 'Y'" + rfqAlive + ")");
            }
            return s.ToString();
        }

        /// <summary>Requisitions the current role may read (simple statement MRole can parse).</summary>
        public static string AccessSql(Ctx ctx)
        {
            return MRole.GetDefault(ctx).AddAccessSQL(
                "SELECT accessReq.M_Requisition_ID FROM M_Requisition accessReq WHERE accessReq.AD_Client_ID = " + ctx.GetAD_Client_ID(),
                "accessReq", MRole.SQL_FULLYQUALIFIED, MRole.SQL_RO);
        }

        /// <summary>Currency of the primary accounting schema (same lookup as VAS_205).</summary>
        public static VAS_ReqCurrency GetSchemaCurrency(Ctx ctx)
        {
            VAS_ReqCurrency cur = new VAS_ReqCurrency { Id = 0, Symbol = "", Iso = "", Precision = 2 };
            string sql = @"SELECT cs.C_Currency_ID, c.CurSymbol, c.StdPrecision, c.ISO_Code
                           FROM C_AcctSchema cs
                           INNER JOIN AD_ClientInfo ci ON (ci.C_AcctSchema1_ID = cs.C_AcctSchema_ID)
                           INNER JOIN C_Currency c ON (cs.C_Currency_ID = c.C_Currency_ID)
                           WHERE ci.AD_Client_ID = @AD_Client_ID AND ci.IsActive = 'Y' AND cs.IsActive = 'Y' AND c.IsActive = 'Y'";
            DataSet ds = DB.ExecuteDataset(sql, new SqlParameter[] { new SqlParameter("@AD_Client_ID", ctx.GetAD_Client_ID()) }, null);
            if (ds != null && ds.Tables.Count > 0 && ds.Tables[0].Rows.Count > 0)
            {
                DataRow r = ds.Tables[0].Rows[0];
                cur.Id = Util.GetValueOfInt(r["C_Currency_ID"]);
                cur.Symbol = Util.GetValueOfString(r["CurSymbol"]);
                cur.Iso = Util.GetValueOfString(r["ISO_Code"]);
                cur.Precision = Util.GetValueOfInt(r["StdPrecision"]);
            }
            if (cur.Id == 0)
            {
                cur.Id = ctx.GetContextAsInt("$C_Currency_ID");
            }
            return cur;
        }

        /// <summary>
        /// Requisition lines of the client the role may read, with their PO / GRN / transfer figures.
        /// </summary>
        /// <param name="ctx">Context</param>
        /// <param name="cur">Accounting-schema currency (amounts are converted to it)</param>
        /// <param name="where">Extra condition on aliases r (M_Requisition) and rl (M_RequisitionLine), or null</param>
        /// <param name="whereParams">Parameters used in <paramref name="where"/> (names must start with @RQ_)</param>
        public static List<VAS_ReqLine> LoadLines(Ctx ctx, VAS_ReqCurrency cur, string where, List<SqlParameter> whereParams)
        {
            int clientId = ctx.GetAD_Client_ID();
            bool hasLineDate = HasColumn("M_RequisitionLine", "DTD001_DateRequired");
            bool hasDelivered = HasColumn("M_RequisitionLine", "DTD001_DeliveredQty");
            bool hasLineUom = HasColumn("M_RequisitionLine", "C_UOM_ID");
            bool hasUser = HasColumn("M_Requisition", "AD_User_ID");
            bool hasSource = HasColumn("M_Requisition", "DTD001_MWarehouseSource_ID");
            bool hasPoLink = HasColumn("C_OrderLine", "M_RequisitionLine_ID");
            bool hasMoveLink = HasColumn("M_MovementLine", "M_RequisitionLine_ID");
            bool hasAsi = HasColumn("M_RequisitionLine", "M_AttributeSetInstance_ID"); // VAS163 2026-11-13: row 1130
            // VAS163 2026-11-17: Sr 7 / Sr 14 - entered qty (line UOM) and line amount
            bool hasQtyEntered = HasColumn("M_RequisitionLine", "QtyEntered");
            bool hasLineNet = HasColumn("M_RequisitionLine", "LineNetAmt");

            StringBuilder sql = new StringBuilder();
            sql.Append(@"
                SELECT r.M_Requisition_ID, r.DocumentNo, r.DocStatus, r.DateDoc, r.DateRequired, r.PriorityRule,
                       r.AD_Org_ID, r.CreatedBy, r.Description, w.Name AS WarehouseName, cu.Name AS CreatedByName,
                       pl.C_Currency_ID, dcur.ISO_Code AS DocCurrencyIso, dcur.CurSymbol AS DocCurrencySymbol,
                       dcur.StdPrecision AS DocCurrencyPrecision,
                       rl.M_RequisitionLine_ID, rl.Line, rl.M_Product_ID, rl.C_Charge_ID,
                       rl.Qty, rl.PriceActual, p.Name AS ProductName, p.Value AS ProductValue, ch.Name AS ChargeName,
                       uom.Name AS UomName, pc.M_Product_Category_ID, pc.Name AS CategoryName, vnd.VendorName,
                       COALESCE(po.PoQty, 0) AS PoQty, COALESCE(po.PoRcvQty, 0) AS PoRcvQty,
                       COALESCE(mv.TrfQty, 0) AS TrfQty, COALESCE(mv.TrfOpenQty, 0) AS TrfOpenQty,
                       COALESCE(po.PoDoneQty, 0) AS PoDoneQty");
            if (hasAsi) { sql.Append(", asi.Description AS AttributeName"); }
            sql.Append(hasQtyEntered ? ", COALESCE(rl.QtyEntered, rl.Qty) AS QtyEntered" : ", rl.Qty AS QtyEntered");
            sql.Append(hasLineNet ? ", COALESCE(rl.LineNetAmt, 0) AS LineNetAmt" : ", 0 AS LineNetAmt");
            sql.Append(hasLineDate ? ", rl.DTD001_DateRequired AS LineNeedBy" : ", r.DateRequired AS LineNeedBy");
            sql.Append(hasDelivered ? ", COALESCE(rl.DTD001_DeliveredQty, 0) AS DeliveredQty" : ", 0 AS DeliveredQty");
            sql.Append(hasUser ? ", r.AD_User_ID, u.Name AS RequesterName" : ", 0 AS AD_User_ID, cu.Name AS RequesterName");
            sql.Append(hasSource ? ", sw.Name AS SourceWarehouseName" : ", w.Name AS SourceWarehouseName");
            // VAS163 2026-11-17: Sr 24 / Sr 30 - requester (business partner, field 'Requester' of window VAS_Requisition)
            bool hasRequester = HasRequesterColumn();
            sql.Append(hasRequester ? ", rbp.Name AS RequesterBpName" : ", NULL AS RequesterBpName");
            sql.Append(@"
                FROM M_RequisitionLine rl
                INNER JOIN M_Requisition r ON r.M_Requisition_ID = rl.M_Requisition_ID
                LEFT JOIN M_Warehouse w ON w.M_Warehouse_ID = r.M_Warehouse_ID
                LEFT JOIN AD_User cu ON cu.AD_User_ID = r.CreatedBy
                LEFT JOIN M_PriceList pl ON pl.M_PriceList_ID = r.M_PriceList_ID
                LEFT JOIN C_Currency dcur ON dcur.C_Currency_ID = pl.C_Currency_ID
                LEFT JOIN M_Product p ON p.M_Product_ID = rl.M_Product_ID
                LEFT JOIN M_Product_Category pc ON pc.M_Product_Category_ID = p.M_Product_Category_ID
                LEFT JOIN C_Charge ch ON ch.C_Charge_ID = rl.C_Charge_ID");
            sql.Append(hasLineUom
                ? " LEFT JOIN C_UOM uom ON uom.C_UOM_ID = COALESCE(rl.C_UOM_ID, p.C_UOM_ID)"
                : " LEFT JOIN C_UOM uom ON uom.C_UOM_ID = p.C_UOM_ID");
            if (hasUser) { sql.Append(" LEFT JOIN AD_User u ON u.AD_User_ID = r.AD_User_ID"); }
            if (hasRequester) { sql.Append(" LEFT JOIN C_BPartner rbp ON rbp.C_BPartner_ID = r.C_BPartner_ID"); }
            if (hasSource) { sql.Append(" LEFT JOIN M_Warehouse sw ON sw.M_Warehouse_ID = r.DTD001_MWarehouseSource_ID"); }
            if (hasAsi) { sql.Append(" LEFT JOIN M_AttributeSetInstance asi ON asi.M_AttributeSetInstance_ID = rl.M_AttributeSetInstance_ID AND asi.M_AttributeSetInstance_ID > 0"); }
            sql.Append(@"
                LEFT JOIN (SELECT ppo.M_Product_ID, MIN(vbp.Name) AS VendorName
                           FROM M_Product_PO ppo
                           INNER JOIN C_BPartner vbp ON vbp.C_BPartner_ID = ppo.C_BPartner_ID
                           WHERE ppo.AD_Client_ID = @RQC_Vendor AND ppo.IsActive = 'Y' AND ppo.IsCurrentVendor = 'Y'
                           GROUP BY ppo.M_Product_ID) vnd ON vnd.M_Product_ID = rl.M_Product_ID");
            if (hasPoLink)
            {
                sql.Append(@"
                LEFT JOIN (SELECT ol.M_RequisitionLine_ID, SUM(ol.QtyOrdered) AS PoQty, SUM(ol.QtyDelivered) AS PoRcvQty,
                                  SUM(CASE WHEN o.DocStatus IN ('CO', 'CL') THEN ol.QtyOrdered ELSE 0 END) AS PoDoneQty
                           FROM C_OrderLine ol
                           INNER JOIN C_Order o ON o.C_Order_ID = ol.C_Order_ID
                           WHERE o.AD_Client_ID = @RQC_Order AND o.IsSOTrx = 'N' AND o.DocStatus NOT IN ('VO', 'RE')
                             AND ol.IsActive = 'Y' AND ol.M_RequisitionLine_ID > 0
                           GROUP BY ol.M_RequisitionLine_ID) po ON po.M_RequisitionLine_ID = rl.M_RequisitionLine_ID");
            }
            else
            {
                sql.Append(" LEFT JOIN (SELECT 0 AS M_RequisitionLine_ID, 0 AS PoQty, 0 AS PoRcvQty, 0 AS PoDoneQty FROM AD_Client WHERE AD_Client_ID = @RQC_Order) po ON po.M_RequisitionLine_ID = rl.M_RequisitionLine_ID");
            }
            if (hasMoveLink)
            {
                sql.Append(@"
                LEFT JOIN (SELECT ml.M_RequisitionLine_ID, SUM(ml.MovementQty) AS TrfQty,
                                  SUM(CASE WHEN m.DocStatus IN ('CO', 'CL') THEN 0 ELSE ml.MovementQty END) AS TrfOpenQty
                           FROM M_MovementLine ml
                           INNER JOIN M_Movement m ON m.M_Movement_ID = ml.M_Movement_ID
                           WHERE m.AD_Client_ID = @RQC_Move AND m.DocStatus NOT IN ('VO', 'RE')
                             AND ml.IsActive = 'Y' AND ml.M_RequisitionLine_ID > 0
                           GROUP BY ml.M_RequisitionLine_ID) mv ON mv.M_RequisitionLine_ID = rl.M_RequisitionLine_ID");
            }
            else
            {
                sql.Append(" LEFT JOIN (SELECT 0 AS M_RequisitionLine_ID, 0 AS TrfQty, 0 AS TrfOpenQty FROM AD_Client WHERE AD_Client_ID = @RQC_Move) mv ON mv.M_RequisitionLine_ID = rl.M_RequisitionLine_ID");
            }
            sql.Append(@"
                WHERE r.AD_Client_ID = @RQC_Client AND r.IsActive = 'Y' AND rl.IsActive = 'Y'
                  AND r.M_Requisition_ID IN (" + AccessSql(ctx) + ")");
            if (!string.IsNullOrEmpty(where))
            {
                sql.Append(" AND (" + where + ")");
            }
            sql.Append(" ORDER BY r.DocumentNo, rl.Line");

            List<SqlParameter> prm = new List<SqlParameter>
            {
                new SqlParameter("@RQC_Vendor", clientId),
                new SqlParameter("@RQC_Order", clientId),
                new SqlParameter("@RQC_Move", clientId),
                new SqlParameter("@RQC_Client", clientId)
            };
            if (whereParams != null) { prm.AddRange(whereParams); }

            List<VAS_ReqLine> lines = new List<VAS_ReqLine>();
            DataSet ds = DB.ExecuteDataset(sql.ToString(), prm.ToArray(), null);
            if (ds == null || ds.Tables.Count == 0)
            {
                return lines;
            }
            Dictionary<string, decimal> rates = new Dictionary<string, decimal>();
            foreach (DataRow row in ds.Tables[0].Rows)
            {
                VAS_ReqLine l = new VAS_ReqLine
                {
                    RequisitionId = Util.GetValueOfInt(row["M_Requisition_ID"]),
                    DocumentNo = Util.GetValueOfString(row["DocumentNo"]),
                    DocStatus = Util.GetValueOfString(row["DocStatus"]),
                    DateDoc = Util.GetValueOfDateTime(row["DateDoc"]),
                    DateRequired = Util.GetValueOfDateTime(row["DateRequired"]),
                    LineNeedBy = Util.GetValueOfDateTime(row["LineNeedBy"]),
                    Priority = Util.GetValueOfString(row["PriorityRule"]),
                    OrgId = Util.GetValueOfInt(row["AD_Org_ID"]),
                    CreatedBy = Util.GetValueOfInt(row["CreatedBy"]),
                    UserId = Util.GetValueOfInt(row["AD_User_ID"]),
                    Description = Util.GetValueOfString(row["Description"]),
                    WarehouseName = Util.GetValueOfString(row["WarehouseName"]),
                    SourceWarehouseName = Util.GetValueOfString(row["SourceWarehouseName"]),
                    RequesterName = Util.GetValueOfString(row["RequesterName"]),
                    CurrencyId = Util.GetValueOfInt(row["C_Currency_ID"]),
                    LineId = Util.GetValueOfInt(row["M_RequisitionLine_ID"]),
                    LineNo = Util.GetValueOfInt(row["Line"]),
                    ProductId = Util.GetValueOfInt(row["M_Product_ID"]),
                    ChargeId = Util.GetValueOfInt(row["C_Charge_ID"]),
                    Qty = Util.GetValueOfDecimal(row["Qty"]),
                    Price = Util.GetValueOfDecimal(row["PriceActual"]),
                    ProductName = Util.GetValueOfString(row["ProductName"]),
                    ProductValue = Util.GetValueOfString(row["ProductValue"]),
                    ChargeName = Util.GetValueOfString(row["ChargeName"]),
                    UomName = Util.GetValueOfString(row["UomName"]),
                    CategoryId = Util.GetValueOfInt(row["M_Product_Category_ID"]),
                    CategoryName = Util.GetValueOfString(row["CategoryName"]),
                    VendorName = Util.GetValueOfString(row["VendorName"]),
                    PoQty = Util.GetValueOfDecimal(row["PoQty"]),
                    PoRcvQty = Util.GetValueOfDecimal(row["PoRcvQty"]),
                    TrfQty = Util.GetValueOfDecimal(row["TrfQty"]),
                    TrfOpenQty = Util.GetValueOfDecimal(row["TrfOpenQty"]),
                    DeliveredQty = Util.GetValueOfDecimal(row["DeliveredQty"]),
                    PoDoneQty = Util.GetValueOfDecimal(row["PoDoneQty"]),
                    AttributeName = hasAsi ? Util.GetValueOfString(row["AttributeName"]) : "",
                    // VAS163 2026-11-17: Sr 7 / Sr 11 / Sr 14
                    QtyEntered = Util.GetValueOfDecimal(row["QtyEntered"]),
                    LineNet = Util.GetValueOfDecimal(row["LineNetAmt"]),
                    DocCurrencyIso = Util.GetValueOfString(row["DocCurrencyIso"]),
                    DocCurrencySymbol = Util.GetValueOfString(row["DocCurrencySymbol"]),
                    DocCurrencyPrecision = row["DocCurrencyPrecision"] == DBNull.Value ? 2 : Util.GetValueOfInt(row["DocCurrencyPrecision"])
                };
                if (!hasLineNet)
                {
                    // no LineNetAmt column: price is per entered UOM
                    l.LineNet = Math.Round(l.QtyEntered * l.Price, 2);
                }
                l.RequesterName = RequesterOf(row);
                if (string.IsNullOrEmpty(l.CategoryName))
                {
                    l.CategoryName = l.ChargeId > 0 ? MsgOr(ctx, "VAS_315_Charges", "Charges") : MsgOr(ctx, "VAS_315_Uncategorised", "Uncategorised");
                }
                l.Rate = ConversionRate(ctx, cur, l, rates);
                lines.Add(l);
            }
            return lines;
        }

        /// <summary>
        /// VAS163 2026-11-13: row 1120 - header of one requisition (the role may read), without its lines, so a
        /// requisition that has no line yet still opens the popup. Also carries Created (row 1122). Null when not found.
        /// </summary>
        public static VAS_ReqLine LoadHeader(Ctx ctx, int requisitionId)
        {
            StringBuilder sql = HeaderSelectSql();
            sql.Append(@"
                WHERE r.M_Requisition_ID = @RQ_Req AND r.AD_Client_ID = @RQC_Client AND r.IsActive = 'Y'
                  AND r.M_Requisition_ID IN (" + AccessSql(ctx) + ")");
            DataSet ds = DB.ExecuteDataset(sql.ToString(), new SqlParameter[]
            {
                new SqlParameter("@RQ_Req", requisitionId),
                new SqlParameter("@RQC_Client", ctx.GetAD_Client_ID())
            }, null);
            if (ds == null || ds.Tables.Count == 0 || ds.Tables[0].Rows.Count == 0)
            {
                return null;
            }
            return ReadHeader(ds.Tables[0].Rows[0]);
        }

        /// <summary>
        /// VAS163 2026-11-20: Sr 61 / Sr 46 - requisitions (the role may read) that have no active line yet, as one
        /// header-only entry each (LineId 0, Qty 0, amount 0), so drafts without lines still appear on the widgets
        /// that list requisitions of every status (Requisitions by Status, My Requisitions). LoadLines needs a line.
        /// </summary>
        /// <param name="ctx">Context</param>
        /// <param name="where">Extra condition on alias r (M_Requisition), or null</param>
        /// <param name="whereParams">Parameters used in <paramref name="where"/> (names must start with @RQ_; new instances,
        /// a parameter cannot be shared with another statement)</param>
        /// <returns>Header-only entries</returns>
        public static List<VAS_ReqLine> LoadHeadersWithoutLines(Ctx ctx, string where, List<SqlParameter> whereParams)
        {
            StringBuilder sql = HeaderSelectSql();
            sql.Append(@"
                WHERE r.AD_Client_ID = @RQC_Client AND r.IsActive = 'Y'
                  AND r.M_Requisition_ID IN (" + AccessSql(ctx) + @")
                  AND NOT EXISTS (SELECT 1 FROM M_RequisitionLine xrl WHERE xrl.M_Requisition_ID = r.M_Requisition_ID AND xrl.IsActive = 'Y')");
            if (!string.IsNullOrEmpty(where))
            {
                sql.Append(" AND (" + where + ")");
            }
            List<SqlParameter> prm = new List<SqlParameter> { new SqlParameter("@RQC_Client", ctx.GetAD_Client_ID()) };
            if (whereParams != null) { prm.AddRange(whereParams); }
            List<VAS_ReqLine> list = new List<VAS_ReqLine>();
            DataSet ds = DB.ExecuteDataset(sql.ToString(), prm.ToArray(), null);
            if (ds == null || ds.Tables.Count == 0)
            {
                return list;
            }
            foreach (DataRow row in ds.Tables[0].Rows)
            {
                list.Add(ReadHeader(row));
            }
            return list;
        }

        /// <summary>VAS163 2026-11-20: Sr 61 - SELECT ... FROM of the requisition header (alias r), shared by LoadHeader / LoadHeadersWithoutLines.</summary>
        private static StringBuilder HeaderSelectSql()
        {
            bool hasUser = HasColumn("M_Requisition", "AD_User_ID");
            bool hasSource = HasColumn("M_Requisition", "DTD001_MWarehouseSource_ID");
            StringBuilder sql = new StringBuilder(@"
                SELECT r.M_Requisition_ID, r.DocumentNo, r.DocStatus, r.DateDoc, r.DateRequired, r.PriorityRule,
                       r.AD_Org_ID, r.CreatedBy, r.Created, r.Description, w.Name AS WarehouseName, cu.Name AS CreatedByName,
                       pl.C_Currency_ID, dcur.ISO_Code AS DocCurrencyIso, dcur.CurSymbol AS DocCurrencySymbol,
                       dcur.StdPrecision AS DocCurrencyPrecision");
            sql.Append(hasUser ? ", u.Name AS RequesterName" : ", cu.Name AS RequesterName");
            sql.Append(hasSource ? ", sw.Name AS SourceWarehouseName" : ", w.Name AS SourceWarehouseName");
            // VAS163 2026-11-17: Sr 24 - requester business partner (field 'Requester' of window VAS_Requisition)
            bool hasRequester = HasRequesterColumn();
            sql.Append(hasRequester ? ", rbp.Name AS RequesterBpName" : ", NULL AS RequesterBpName");
            // VAS163 2026-11-17: Sr 11 - price-list currency of the requisition (popup amounts are shown in it)
            sql.Append(@"
                FROM M_Requisition r
                LEFT JOIN M_PriceList pl ON pl.M_PriceList_ID = r.M_PriceList_ID
                LEFT JOIN C_Currency dcur ON dcur.C_Currency_ID = pl.C_Currency_ID
                LEFT JOIN M_Warehouse w ON w.M_Warehouse_ID = r.M_Warehouse_ID
                LEFT JOIN AD_User cu ON cu.AD_User_ID = r.CreatedBy");
            if (hasUser) { sql.Append(" LEFT JOIN AD_User u ON u.AD_User_ID = r.AD_User_ID"); }
            if (hasRequester) { sql.Append(" LEFT JOIN C_BPartner rbp ON rbp.C_BPartner_ID = r.C_BPartner_ID"); }
            if (hasSource) { sql.Append(" LEFT JOIN M_Warehouse sw ON sw.M_Warehouse_ID = r.DTD001_MWarehouseSource_ID"); }
            return sql;
        }

        /// <summary>VAS163 2026-11-20: Sr 61 - header entry (no line figures) from a row of HeaderSelectSql.</summary>
        private static VAS_ReqLine ReadHeader(DataRow row)
        {
            VAS_ReqLine h = new VAS_ReqLine
            {
                RequisitionId = Util.GetValueOfInt(row["M_Requisition_ID"]),
                DocumentNo = Util.GetValueOfString(row["DocumentNo"]),
                DocStatus = Util.GetValueOfString(row["DocStatus"]),
                DateDoc = Util.GetValueOfDateTime(row["DateDoc"]),
                DateRequired = Util.GetValueOfDateTime(row["DateRequired"]),
                Created = Util.GetValueOfDateTime(row["Created"]),
                Priority = Util.GetValueOfString(row["PriorityRule"]),
                OrgId = Util.GetValueOfInt(row["AD_Org_ID"]),
                CreatedBy = Util.GetValueOfInt(row["CreatedBy"]),
                Description = Util.GetValueOfString(row["Description"]),
                WarehouseName = Util.GetValueOfString(row["WarehouseName"]),
                SourceWarehouseName = Util.GetValueOfString(row["SourceWarehouseName"]),
                RequesterName = Util.GetValueOfString(row["RequesterName"]),
                // VAS163 2026-11-17: Sr 11
                CurrencyId = Util.GetValueOfInt(row["C_Currency_ID"]),
                DocCurrencyIso = Util.GetValueOfString(row["DocCurrencyIso"]),
                DocCurrencySymbol = Util.GetValueOfString(row["DocCurrencySymbol"]),
                DocCurrencyPrecision = row["DocCurrencyPrecision"] == DBNull.Value ? 2 : Util.GetValueOfInt(row["DocCurrencyPrecision"])
            };
            h.RequesterName = RequesterOf(row);
            return h;
        }

        /// <summary>
        /// VAS163 2026-11-17: Sr 24 / Sr 30 - True when M_Requisition has C_BPartner_ID, the 'Requester' field of window
        /// VAS_Requisition (the AD_User_ID field there is the 'Preparer').
        /// </summary>
        public static bool HasRequesterColumn()
        {
            return HasColumn("M_Requisition", "C_BPartner_ID");
        }

        /// <summary>
        /// VAS163 2026-11-17: Sr 24 / Sr 30 - 'Requested by' = name of the requester business partner (M_Requisition.C_BPartner_ID);
        /// when no requester is entered, the preparer (AD_User_ID), then the creator, so the column is never blank.
        /// Expects columns RequesterBpName, RequesterName (preparer) and CreatedByName.
        /// </summary>
        public static string RequesterOf(DataRow row)
        {
            string name = Util.GetValueOfString(row["RequesterBpName"]);
            if (string.IsNullOrEmpty(name)) { name = Util.GetValueOfString(row["RequesterName"]); }
            if (string.IsNullOrEmpty(name)) { name = Util.GetValueOfString(row["CreatedByName"]); }
            return name;
        }

        /// <summary>Factor from the requisition's price-list currency to the accounting-schema currency.</summary>
        private static decimal ConversionRate(Ctx ctx, VAS_ReqCurrency cur, VAS_ReqLine l, Dictionary<string, decimal> cache)
        {
            if (l.CurrencyId <= 0 || cur.Id <= 0 || l.CurrencyId == cur.Id)
            {
                return 1m;
            }
            DateTime date = (l.DateDoc ?? DateTime.Now).Date;
            string key = l.CurrencyId + "|" + date.ToString("yyyyMMdd") + "|" + l.OrgId;
            decimal rate;
            if (cache.TryGetValue(key, out rate))
            {
                return rate;
            }
            rate = 1m;
            try
            {
                decimal conv = MConversionRate.Convert(ctx, 1m, l.CurrencyId, cur.Id, date, 0, ctx.GetAD_Client_ID(), l.OrgId);
                if (conv > 0m) { rate = conv; }
            }
            catch (Exception ex)
            {
                Log.Warning("VAS_ReqWidgetData: conversion " + l.CurrencyId + "->" + cur.Id + " failed: " + ex.Message);
            }
            cache[key] = rate;
            return rate;
        }

        /// <summary>
        /// Live evaluation of the VAS_RequisitionStatus rule (MRequisition.UpdateRequisitionStatus) on the
        /// lines of one requisition: OP Open, PO Partially Ordered, OR Ordered, PD Partially Delivered, DE Delivered.
        /// Same status values and decision order as the system rule, fed with the figures every widget uses:
        /// ordered = qty on a PO or a Material Transfer (Qty - not ordered), delivered = GRN qty + transfer-delivered qty.
        /// (The system rule reads only PO qty and DTD001_DeliveredQty, so a requisition fully received by GRN
        /// would stay "Ordered" and one served by an open transfer would stay "Open".)
        /// </summary>
        public static string SystemStatus(IEnumerable<VAS_ReqLine> reqLines)
        {
            decimal qty = 0m, ord = 0m, del = 0m;
            foreach (VAS_ReqLine l in reqLines)
            {
                qty += l.Qty;
                ord += l.Qty - l.StatusNotOrderedQty; // VAS163 2026-11-13: row 1143 - drafted PO / transfer is not "Ordered"
                del += l.ReceivedQty;
            }
            if (del > 0m)
            {
                // VAS163 2026-11-17: Sr 31 - 'Delivered' only when every line's requested qty is received. 'del == ord'
                // made a requisition 'Delivered' after a partial Material Transfer / Inventory Use (e.g. 900126: 5 of 8
                // transferred -> ordered 5 = delivered 5). ReceivedQty is capped at the line Qty (product UOM, same unit
                // as PO / transfer / internal-use quantities), so the sum reaches qty only when all lines are complete.
                return del >= qty ? "DE" : "PD";
            }
            if (ord <= 0m) { return "OP"; }
            return ord >= qty ? "OR" : "PO";
        }

        /// <summary>
        /// Display name of a VAS_RequisitionStatus value (DR / CL handled by the caller).
        /// VAS163 2026-11-17: Sr 21 - the status badge text comes ONLY from these messages (no reference-list
        /// name, no hard-coded fallback), chosen by the status code:
        ///   OP -> VAS_305_ReqStatusOpen, PO -> VAS_305_ReqStatusPartiallyOrdered, OR -> VAS_305_ReqStatusOrdered,
        ///   PD -> VAS_305_ReqStatusPartiallyReceived, DE -> VAS_305_ReqStatusDelivered.
        /// Msg.GetMsg(ctx, key, true) returns the message text only (the 2-argument form appends the tip).
        /// </summary>
        public static string StatusName(Ctx ctx, string code)
        {
            string key;
            switch (code)
            {
                case "PO": key = "VAS_305_ReqStatusPartiallyOrdered"; break;
                case "OR": key = "VAS_305_ReqStatusOrdered"; break;
                case "PD": key = "VAS_305_ReqStatusPartiallyReceived"; break;
                case "DE": key = "VAS_305_ReqStatusDelivered"; break;
                default: key = "VAS_305_ReqStatusOpen"; break;
            }
            return Msg.GetMsg(ctx, key, true);
        }

        /// <summary>Translated DocStatus name (reference 131).</summary>
        public static string DocStatusName(Ctx ctx, string docStatus)
        {
            string name = MRefList.GetListName(ctx, 131, docStatus);
            return string.IsNullOrEmpty(name) ? docStatus : name;
        }

        /// <summary>Translated priority name (reference 154).</summary>
        public static string PriorityName(Ctx ctx, string priority)
        {
            if (string.IsNullOrEmpty(priority)) { return ""; }
            string name = MRefList.GetListName(ctx, 154, priority);
            return string.IsNullOrEmpty(name) ? priority : name;
        }

        /// <summary>
        /// Bucket of a requisition on the status donut / chips: DR draft, CL closed, VO voided / reversed,
        /// otherwise the live system status (OP / PO / OR / PD / DE).
        /// </summary>
        public static string StatusBucket(string docStatus, IEnumerable<VAS_ReqLine> reqLines)
        {
            if (docStatus == "DR" || docStatus == "IP" || docStatus == "IN" || docStatus == "NA" || docStatus == "WC")
            {
                return StatusDraft;
            }
            // VAS163 2026-11-20: Sr 44 / Sr 57 - Closed and Voided are separate buckets (were one 'Closed / Voided')
            if (docStatus == "VO" || docStatus == "RE")
            {
                return StatusVoided;
            }
            if (docStatus == "CL")
            {
                return StatusClosed;
            }
            return SystemStatus(reqLines);
        }

        /// <summary>Name of a donut / chip bucket.</summary>
        public static string BucketName(Ctx ctx, string bucket)
        {
            if (bucket == StatusDraft) { return MsgOr(ctx, "VAS_314_Draft", "Draft"); }
            // VAS163 2026-11-20: Sr 44 / Sr 57 - new message keys VAS_314_Closed / VAS_314_Voided
            if (bucket == StatusClosed) { return MsgOr(ctx, "VAS_314_Closed", "Closed"); }
            if (bucket == StatusVoided) { return MsgOr(ctx, "VAS_314_Voided", "Voided"); }
            return StatusName(ctx, bucket);
        }

        /// <summary>One row per requisition built from its lines (used by the KPI and list popups).</summary>
        /// <param name="docAmountOf">VAS163 2026-11-17: Sr 27 - the same amount in the requisition's price-list currency
        /// (null = whole line amount); sent as docAmount with currencyIso / currencyPrecision / currencySymbol per row.</param>
        public static List<object> RequisitionRows(Ctx ctx, IEnumerable<VAS_ReqLine> lines, Func<VAS_ReqLine, decimal> amountOf,
            Func<VAS_ReqLine, decimal> docAmountOf = null)
        {
            DateTime today = DateTime.Now.Date;
            return lines.GroupBy(l => l.RequisitionId).Select(g =>
            {
                VAS_ReqLine h = g.First();
                string bucket = StatusBucket(h.DocStatus, g);
                DateTime? need = g.Where(l => l.OpenQty > 0 && l.NeedBy.HasValue).Select(l => l.NeedBy).Min() ?? g.Select(l => l.NeedBy).Min();
                return new
                {
                    requisitionId = h.RequisitionId,
                    documentNo = h.DocumentNo,
                    requester = h.RequesterName,
                    warehouse = h.WarehouseName,
                    dateDoc = D(h.DateDoc),
                    needBy = D(need),
                    amount = amountOf == null ? g.Sum(l => l.Amount(l.Qty)) : g.Sum(amountOf),
                    // VAS163 2026-11-17: Sr 27 - value in the price-list currency of the requisition (all its lines share it;
                    // no price list -> schema currency, empty ISO: the page then uses the list currency)
                    docAmount = docAmountOf == null ? g.Sum(l => l.DocAmount(l.Qty)) : g.Sum(docAmountOf),
                    currencyIso = h.CurrencyId > 0 ? h.DocCurrencyIso : "",
                    currencySymbol = h.CurrencyId > 0 ? h.DocCurrencySymbol : "",
                    currencyPrecision = h.CurrencyId > 0 ? h.DocCurrencyPrecision : -1,
                    // VAS163 2026-11-20: Sr 61 - a requisition without lines comes as one header-only entry (LineId 0)
                    lines = g.Count(l => l.LineId > 0),
                    // VAS163 2026-11-20: Sr 60 - document status of the requisition (column 'Requisition status')
                    docStatus = h.DocStatus,
                    docStatusName = DocStatusName(ctx, h.DocStatus),
                    status = bucket,
                    statusName = BucketName(ctx, bucket),
                    isLate = g.Any(l => l.IsLate(today)),
                    sortDate = need
                };
            }).OrderBy(x => x.sortDate ?? DateTime.MaxValue).ThenBy(x => x.documentNo)
              .Select(x => (object)new
              {
                  x.requisitionId, x.documentNo, x.requester, x.warehouse, x.dateDoc, x.needBy, x.amount,
                  x.docAmount, x.currencyIso, x.currencySymbol, x.currencyPrecision,
                  x.lines, x.docStatus, x.docStatusName, x.status, x.statusName, x.isLate
              })
              .ToList();
        }

        /// <summary>PO / GRN and transfer documents per requisition line, for the detail popup.</summary>
        public static Dictionary<int, List<object>> LoadFulfilmentDocs(int requisitionId)
        {
            Dictionary<int, List<object>> map = new Dictionary<int, List<object>>();
            Action<int, object> add = (lineId, o) =>
            {
                if (!map.ContainsKey(lineId)) { map[lineId] = new List<object>(); }
                map[lineId].Add(o);
            };
            if (HasColumn("C_OrderLine", "M_RequisitionLine_ID"))
            {
                // VAS163 2026-11-13: row 1142 - a blanket purchase order is listed as such (BPO)
                bool hasBlanket = HasColumn("C_Order", "IsBlanketTrx");
                DataSet ds = DB.ExecuteDataset(@"SELECT ol.M_RequisitionLine_ID, o.DocumentNo, o.DocStatus, ol.QtyOrdered, ol.QtyDelivered"
                        + (hasBlanket ? ", o.IsBlanketTrx" : "") + @"
                        FROM C_OrderLine ol
                        INNER JOIN C_Order o ON o.C_Order_ID = ol.C_Order_ID
                        INNER JOIN M_RequisitionLine rl ON rl.M_RequisitionLine_ID = ol.M_RequisitionLine_ID
                        WHERE rl.M_Requisition_ID = @RQ_Req AND o.DocStatus NOT IN ('VO', 'RE') AND ol.IsActive = 'Y'
                        ORDER BY o.DocumentNo",
                    new SqlParameter[] { new SqlParameter("@RQ_Req", requisitionId) }, null);
                if (ds != null && ds.Tables.Count > 0)
                {
                    foreach (DataRow r in ds.Tables[0].Rows)
                    {
                        add(Util.GetValueOfInt(r["M_RequisitionLine_ID"]), new
                        {
                            type = hasBlanket && Util.GetValueOfString(r["IsBlanketTrx"]) == "Y" ? "BPO" : "PO",
                            documentNo = Util.GetValueOfString(r["DocumentNo"]),
                            docStatus = Util.GetValueOfString(r["DocStatus"]),
                            qty = Util.GetValueOfDecimal(r["QtyOrdered"]),
                            receivedQty = Util.GetValueOfDecimal(r["QtyDelivered"])
                        });
                    }
                }
            }
            if (HasColumn("M_MovementLine", "M_RequisitionLine_ID"))
            {
                DataSet ds = DB.ExecuteDataset(@"SELECT ml.M_RequisitionLine_ID, m.DocumentNo, m.DocStatus, ml.MovementQty
                        FROM M_MovementLine ml
                        INNER JOIN M_Movement m ON m.M_Movement_ID = ml.M_Movement_ID
                        INNER JOIN M_RequisitionLine rl ON rl.M_RequisitionLine_ID = ml.M_RequisitionLine_ID
                        WHERE rl.M_Requisition_ID = @RQ_Req AND m.DocStatus NOT IN ('VO', 'RE') AND ml.IsActive = 'Y'
                        ORDER BY m.DocumentNo",
                    new SqlParameter[] { new SqlParameter("@RQ_Req", requisitionId) }, null);
                if (ds != null && ds.Tables.Count > 0)
                {
                    foreach (DataRow r in ds.Tables[0].Rows)
                    {
                        string st = Util.GetValueOfString(r["DocStatus"]);
                        decimal q = Util.GetValueOfDecimal(r["MovementQty"]);
                        add(Util.GetValueOfInt(r["M_RequisitionLine_ID"]), new
                        {
                            type = "MT",
                            documentNo = Util.GetValueOfString(r["DocumentNo"]),
                            docStatus = st,
                            qty = q,
                            receivedQty = (st == "CO" || st == "CL") ? q : 0m
                        });
                    }
                }
            }
            // VAS163 2026-11-13: row 1142 - Inventory Use (internal use lines) raised from the requisition line
            if (HasColumn("M_InventoryLine", "M_RequisitionLine_ID"))
            {
                DataSet ds = DB.ExecuteDataset(@"SELECT il.M_RequisitionLine_ID, i.DocumentNo, i.DocStatus, il.QtyInternalUse
                        FROM M_InventoryLine il
                        INNER JOIN M_Inventory i ON i.M_Inventory_ID = il.M_Inventory_ID
                        INNER JOIN M_RequisitionLine rl ON rl.M_RequisitionLine_ID = il.M_RequisitionLine_ID
                        WHERE rl.M_Requisition_ID = @RQ_Req AND i.IsInternalUse = 'Y' AND i.DocStatus NOT IN ('VO', 'RE') AND il.IsActive = 'Y'
                        ORDER BY i.DocumentNo",
                    new SqlParameter[] { new SqlParameter("@RQ_Req", requisitionId) }, null);
                if (ds != null && ds.Tables.Count > 0)
                {
                    foreach (DataRow r in ds.Tables[0].Rows)
                    {
                        string st = Util.GetValueOfString(r["DocStatus"]);
                        decimal q = Util.GetValueOfDecimal(r["QtyInternalUse"]);
                        add(Util.GetValueOfInt(r["M_RequisitionLine_ID"]), new
                        {
                            type = "IU",
                            documentNo = Util.GetValueOfString(r["DocumentNo"]),
                            docStatus = st,
                            qty = q,
                            receivedQty = (st == "CO" || st == "CL") ? q : 0m
                        });
                    }
                }
            }
            // VAS163 2026-11-13: row 1142 - RFQ lines raised from the requisition line
            if (HasColumn("C_RfQLine", "M_RequisitionLine_ID"))
            {
                bool hasRfqStatus = HasColumn("C_RfQ", "DocStatus");
                DataSet ds = DB.ExecuteDataset(@"SELECT q.M_RequisitionLine_ID, f.DocumentNo, f.Processed"
                        + (hasRfqStatus ? ", f.DocStatus" : "") + @"
                        FROM C_RfQLine q
                        INNER JOIN C_RfQ f ON f.C_RfQ_ID = q.C_RfQ_ID
                        INNER JOIN M_RequisitionLine rl ON rl.M_RequisitionLine_ID = q.M_RequisitionLine_ID
                        WHERE rl.M_Requisition_ID = @RQ_Req AND q.IsActive = 'Y' AND f.IsActive = 'Y'
                        ORDER BY f.DocumentNo",
                    new SqlParameter[] { new SqlParameter("@RQ_Req", requisitionId) }, null);
                if (ds != null && ds.Tables.Count > 0)
                {
                    foreach (DataRow r in ds.Tables[0].Rows)
                    {
                        string st = hasRfqStatus ? Util.GetValueOfString(r["DocStatus"]) : "";
                        if (string.IsNullOrEmpty(st)) { st = Util.GetValueOfString(r["Processed"]) == "Y" ? "CO" : "DR"; }
                        if (st == "VO" || st == "RE") { continue; }
                        add(Util.GetValueOfInt(r["M_RequisitionLine_ID"]), new
                        {
                            type = "RFQ",
                            documentNo = Util.GetValueOfString(r["DocumentNo"]),
                            docStatus = st,
                            qty = 0m,
                            receivedQty = 0m
                        });
                    }
                }
            }
            return map;
        }
    }
}
