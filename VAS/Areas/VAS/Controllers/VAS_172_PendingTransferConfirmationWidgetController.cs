using Newtonsoft.Json;
using System;
using System.Collections.Generic;
using System.Data;
using System.Web.Mvc;
using VAdvantage.Classes;
using VAdvantage.DataBase;
using VAdvantage.Model;
using VAdvantage.Utility;
using VIS.Filters;

namespace VIS.Controllers
{
    /// <summary>
    /// Module Name : Pending Transfer Confirmation Widget (Material Transfer Dashboard)
    /// Purpose     : Operational work-queue of inbound transfers awaiting destination
    ///               confirmation (M_Movement with DocStatus IN ('IP', 'WC', 'DP', 'UC')).
    /// ID Prefix   : VAS_172_
    /// </summary>
    public class VAS_172_PendingTransferConfirmationWidgetController : Controller
    {
        private const string PendingStatusInList = "'IP', 'WC', 'DP', 'UC'";

        // Longest note the Description column takes; the pop-up enforces the same limit.
        private const int DescriptionMaxLength = 255;

        // Column-existence guards (a column missing on a deployment fails the whole query).
        // Same AD_Column check VAS_163 uses; results are cached per column.
        private static readonly Dictionary<string, bool> _columnExists = new Dictionary<string, bool>();
        private static readonly object _columnLock = new object();

        private static bool ColumnExists(string tableName, string columnName)
        {
            string cacheKey = tableName.ToUpperInvariant() + "." + columnName.ToUpperInvariant();
            lock (_columnLock)
            {
                bool cached;
                if (_columnExists.TryGetValue(cacheKey, out cached)) { return cached; }
            }

            string sql = @"
                SELECT COUNT(1)
                FROM AD_Column ColumnInfo
                INNER JOIN AD_Table TableInfo ON (TableInfo.AD_Table_ID=ColumnInfo.AD_Table_ID AND TableInfo.IsActive='Y')
                WHERE ColumnInfo.IsActive='Y'
                  AND UPPER(TableInfo.TableName)='" + tableName.ToUpperInvariant() + @"'
                  AND UPPER(ColumnInfo.ColumnName)='" + columnName.ToUpperInvariant() + "'";

            bool exists = Util.GetValueOfInt(DB.ExecuteScalar(sql, null, null)) > 0;
            lock (_columnLock) { _columnExists[cacheKey] = exists; }
            return exists;
        }

        [AjaxAuthorizeAttribute]
        [AjaxSessionFilterAttribute]
        public JsonResult GetPendingConfirmations()
        {
            if (Session["ctx"] == null)
                return Json(new { error = "Session Expired" }, JsonRequestBehavior.AllowGet);

            Ctx ctx = Session["ctx"] as Ctx;

            string sql = @"
                SELECT
                    mm.M_Movement_ID          AS TransferID,
                    mm.DocumentNo             AS TransferNo,
                    mm.DocStatus              AS ConfirmationStatus,
                    mm.MovementDate           AS MovementDate
                FROM M_Movement mm
                WHERE mm.IsActive = 'Y'
                  AND mm.DocStatus IN (" + PendingStatusInList + @")
                  AND EXISTS (SELECT 1 FROM M_MovementConfirm mc
                              WHERE mc.M_Movement_ID = mm.M_Movement_ID AND mc.IsActive = 'Y'
                                AND mc.DocStatus IN ('DR', 'IP'))
                ORDER BY mm.MovementDate ASC";

            sql = MRole.GetDefault(ctx).AddAccessSQL(sql, "mm", MRole.SQL_FULLYQUALIFIED, MRole.SQL_RO);

            var headers = new List<TransferHeader>();
            var records = new List<object>();
            IDataReader dr = null;
            try
            {
                dr = DB.ExecuteReader(sql);
                while (dr != null && dr.Read())
                {
                    headers.Add(new TransferHeader
                    {
                        TransferID = Util.GetValueOfInt(dr["TransferID"]),
                        TransferNo = Util.GetValueOfString(dr["TransferNo"]),
                        ConfirmationStatus = "Pending",
                        MovementDate = Util.GetValueOfDateTime(dr["MovementDate"]).GetValueOrDefault().ToString("o")
                    });
                }
                if (dr != null) { dr.Close(); dr.Dispose(); dr = null; }

                foreach (var h in headers)
                {
                    GetRouteDetail(h);
                    var lines = GetLines(ctx, h.TransferID);
                    records.Add(new
                    {
                        TransferID = h.TransferID,
                        TransferNo = h.TransferNo,
                        ConfirmationStatus = h.ConfirmationStatus,
                        MovementDate = h.MovementDate,
                        SourceWarehouse = h.SourceWarehouse,
                        DestWarehouse = h.DestWarehouse,
                        SourceLocator = h.SourceLocator,
                        DestLocator = h.DestLocator,
                        LineCount = lines.Count,
                        Lines = lines
                    });
                }

                return Json(JsonConvert.SerializeObject(new { records = records }), JsonRequestBehavior.AllowGet);
            }
            catch (Exception ex)
            {
                return Json(new { error = ex.Message }, JsonRequestBehavior.AllowGet);
            }
            finally
            {
                if (dr != null) { dr.Close(); dr.Dispose(); }
            }
        }

        private class TransferHeader
        {
            public int    TransferID;
            public string TransferNo;
            public string ConfirmationStatus;
            public string MovementDate;
            public string SourceWarehouse;
            public string DestWarehouse;
            public string SourceLocator;
            public string DestLocator;
        }

        private static void GetRouteDetail(TransferHeader h)
        {
            // A locator is shown by its NAME (combination, else bin), the code only as a last resort.
            string sql = @"
                SELECT
                    wSrc.Name  AS SourceWarehouse,
                    wDst.Name  AS DestWarehouse,
                    COALESCE(lSrc.LocatorCombination, lSrc.Bin, lSrc.Value) AS SourceLocator,
                    COALESCE(lDst.LocatorCombination, lDst.Bin, lDst.Value) AS DestLocator
                FROM M_MovementLine ml
                LEFT JOIN M_Locator lSrc ON lSrc.M_Locator_ID = ml.M_Locator_ID
                LEFT JOIN M_Warehouse wSrc ON wSrc.M_Warehouse_ID = lSrc.M_Warehouse_ID
                LEFT JOIN M_Locator lDst ON lDst.M_Locator_ID = ml.M_LocatorTo_ID
                LEFT JOIN M_Warehouse wDst ON wDst.M_Warehouse_ID = lDst.M_Warehouse_ID
                WHERE ml.M_Movement_ID = " + h.TransferID + @"
                  AND ml.IsActive = 'Y'
                ORDER BY ml.Line ASC, ml.M_MovementLine_ID ASC";

            IDataReader dr = null;
            try
            {
                dr = DB.ExecuteReader(sql);
                if (dr != null && dr.Read())
                {
                    h.SourceWarehouse = Util.GetValueOfString(dr["SourceWarehouse"]);
                    h.DestWarehouse   = Util.GetValueOfString(dr["DestWarehouse"]);
                    h.SourceLocator   = Util.GetValueOfString(dr["SourceLocator"]);
                    h.DestLocator     = Util.GetValueOfString(dr["DestLocator"]);
                }
            }
            finally
            {
                if (dr != null) { dr.Close(); dr.Dispose(); }
            }
        }

        /// <summary>
        /// A locator's display name - the same text the pop-up's route line shows ("John Warehouse
        /// locator"): the locator combination, then the bin, the search key only as a last resort.
        /// </summary>
        private static string LocatorNameSql(string alias)
        {
            return "COALESCE(" + alias + ".LocatorCombination, " + alias + ".Bin, " + alias + ".Value)";
        }

        private List<object> GetLines(Ctx ctx, int movementId)
        {
            var lines = new List<object>();

            // The receiving locator is the confirmation line's own VAS_ReceivingLocator_ID where the
            // schema carries it, else the movement line's destination (same rule as VAS_103).
            bool hasRecvLoc = ColumnExists("M_MovementLineConfirm", "VAS_ReceivingLocator_ID")
                           && ColumnExists("M_MovementLineConfirm", "M_MovementLine_ID");
            string recvIdExpr = hasRecvLoc
                ? @"COALESCE(NULLIF((SELECT MAX(lc.VAS_ReceivingLocator_ID)
                                       FROM M_MovementLineConfirm lc
                                      WHERE lc.M_MovementLine_ID = ml.M_MovementLine_ID
                                        AND lc.IsActive = 'Y'), 0), ml.M_LocatorTo_ID)"
                : "ml.M_LocatorTo_ID";

            // The line's own unit decides the full name and the precision; the product's unit stands in
            // for a line that carries none.
            string sql = @"
                SELECT
                    ml.M_MovementLine_ID        AS LineID,
                    ml.Line                     AS LineNo,
                    p.Name                      AS ItemName,
                    COALESCE(u.Name, u.UOMSymbol) AS UOM,
                    COALESCE(u.StdPrecision, 0) AS UOMPrecision,
                    COALESCE(asi.Description, N'') AS AttributeSetInstance,
                    COALESCE(ml.M_LocatorTo_ID, 0) AS ScrapLocatorID,
                    COALESCE(" + LocatorNameSql("lDst") + @", N'') AS ScrapLocator,
                    COALESCE(lRecv.M_Locator_ID, 0) AS ReceivingLocatorID,
                    COALESCE(" + LocatorNameSql("lRecv") + @", N'') AS ReceivingLocator,
                    COALESCE(ml.MovementQty, 0) AS TargetQty,
                    ml.ConfirmedQty             AS ConfirmedQty,
                    COALESCE(ml.ScrappedQty, 0) AS ScrappedQty,
                    ml.Description              AS Description
                FROM M_MovementLine ml
                LEFT JOIN M_Product p ON p.M_Product_ID = ml.M_Product_ID
                LEFT JOIN C_UOM u ON u.C_UOM_ID = COALESCE(NULLIF(ml.C_UOM_ID, 0), p.C_UOM_ID)
                LEFT JOIN M_AttributeSetInstance asi ON asi.M_AttributeSetInstance_ID = ml.M_AttributeSetInstance_ID
                LEFT JOIN M_Locator lDst ON lDst.M_Locator_ID = ml.M_LocatorTo_ID
                LEFT JOIN M_Locator lRecv ON lRecv.M_Locator_ID = " + recvIdExpr + @"
                WHERE ml.M_Movement_ID = " + movementId + @"
                  AND ml.IsActive = 'Y'
                ORDER BY ml.Line ASC";

            IDataReader dr = null;
            try
            {
                dr = DB.ExecuteReader(sql);
                while (dr != null && dr.Read())
                {
                    lines.Add(new
                    {
                        LineID = Util.GetValueOfInt(dr["LineID"]),
                        LineNo = Util.GetValueOfInt(dr["LineNo"]),
                        ItemName = Util.GetValueOfString(dr["ItemName"]),
                        UOM = Util.GetValueOfString(dr["UOM"]),
                        UOMPrecision = Util.GetValueOfInt(dr["UOMPrecision"]),
                        AttributeSetInstance = Util.GetValueOfString(dr["AttributeSetInstance"]),
                        ScrapLocatorID = Util.GetValueOfInt(dr["ScrapLocatorID"]),
                        ScrapLocator = Util.GetValueOfString(dr["ScrapLocator"]),
                        ReceivingLocatorID = Util.GetValueOfInt(dr["ReceivingLocatorID"]),
                        ReceivingLocator = Util.GetValueOfString(dr["ReceivingLocator"]),
                        TargetQty = Util.GetValueOfDecimal(dr["TargetQty"]),
                        ConfirmedQty = dr["ConfirmedQty"] == DBNull.Value ? (decimal?)null : Util.GetValueOfDecimal(dr["ConfirmedQty"]),
                        ScrappedQty = Util.GetValueOfDecimal(dr["ScrappedQty"]),
                        Description = Util.GetValueOfString(dr["Description"])
                    });
                }
            }
            finally
            {
                if (dr != null) { dr.Close(); dr.Dispose(); }
            }
            return lines;
        }

        /// <summary>
        /// Locators the line can be received / scrapped into: every active locator of the transfer's
        /// To Warehouse (M_Movement.M_Warehouse_ID - the same rule as the window's To Locator field),
        /// shown by NAME, not the search key. A transfer without a To Warehouse falls back to the
        /// warehouse of the line's current To Locator. Client-scoped by hand (a plain lookup the role
        /// filter adds nothing to).
        /// </summary>
        [AjaxAuthorizeAttribute]
        [AjaxSessionFilterAttribute]
        public JsonResult GetLocators(int lineId)
        {
            if (Session["ctx"] == null)
                return Json(new { error = "Session Expired" }, JsonRequestBehavior.AllowGet);

            Ctx ctx = Session["ctx"] as Ctx;
            var list = new List<object>();
            IDataReader dr = null;
            try
            {
                string sql = @"
                    SELECT loc.M_Locator_ID AS LocatorID,
                           " + LocatorNameSql("loc") + @" AS LocatorName
                    FROM M_Locator loc
                    WHERE loc.IsActive = 'Y'
                      AND loc.AD_Client_ID = " + ctx.GetAD_Client_ID() + @"
                      AND loc.M_Warehouse_ID = (
                            SELECT COALESCE(NULLIF(mv.M_Warehouse_ID, 0), lt.M_Warehouse_ID)
                            FROM M_MovementLine ml
                            INNER JOIN M_Movement mv ON mv.M_Movement_ID = ml.M_Movement_ID
                            LEFT JOIN M_Locator lt ON lt.M_Locator_ID = ml.M_LocatorTo_ID
                            WHERE ml.M_MovementLine_ID = " + lineId + @")
                    ORDER BY " + LocatorNameSql("loc");

                dr = DB.ExecuteReader(sql);
                while (dr != null && dr.Read())
                {
                    list.Add(new
                    {
                        LocatorID = Util.GetValueOfInt(dr["LocatorID"]),
                        LocatorName = Util.GetValueOfString(dr["LocatorName"])
                    });
                }
                return Json(JsonConvert.SerializeObject(new { locators = list }), JsonRequestBehavior.AllowGet);
            }
            catch (Exception ex)
            {
                return Json(new { error = ex.Message }, JsonRequestBehavior.AllowGet);
            }
            finally
            {
                if (dr != null) { dr.Close(); dr.Dispose(); }
            }
        }

        [HttpPost]
        [AjaxAuthorizeAttribute]
        [AjaxSessionFilterAttribute]
        public JsonResult SaveConfirmationLine()
        {
            try
            {
                string json = "";
                if (Request.InputStream != null)
                {
                    Request.InputStream.Position = 0;
                    using (var reader = new System.IO.StreamReader(Request.InputStream))
                    {
                        json = reader.ReadToEnd();
                    }
                }
                if (string.IsNullOrEmpty(json) && Request.Form.Keys.Count > 0)
                {
                    json = Request.Form[0];
                }

                var data = JsonConvert.DeserializeObject<Dictionary<string, object>>(json);

                int transferId = Util.GetValueOfInt(data.ContainsKey("TransferID") ? data["TransferID"] : 0);
                int lineNo = Util.GetValueOfInt(data.ContainsKey("LineNo") ? data["LineNo"] : 0);
                int lineId = Util.GetValueOfInt(data.ContainsKey("LineID") ? data["LineID"] : 0);
                int scrapLocatorId = Util.GetValueOfInt(data.ContainsKey("ScrapLocatorID") ? data["ScrapLocatorID"] : 0);
                decimal confirmedQty = Util.GetValueOfDecimal(data.ContainsKey("ConfirmedQty") ? data["ConfirmedQty"] : 0);
                decimal scrappedQty = Util.GetValueOfDecimal(data.ContainsKey("ScrappedQty") ? data["ScrappedQty"] : 0);
                string description = data.ContainsKey("Description") ? Util.GetValueOfString(data["Description"]) : "";
                if (description.Length > DescriptionMaxLength)
                    description = description.Substring(0, DescriptionMaxLength);

                // Receiving locator picked in the pop-up (sent only when the user changed it).
                int receivingLocatorId = Util.GetValueOfInt(data.ContainsKey("ReceivingLocatorID") ? data["ReceivingLocatorID"] : 0);

                // The scrap lands in the line's destination locator, so changing it moves that locator.
                // A changed receiving locator is that same destination and takes precedence.
                int locatorToId = receivingLocatorId > 0 ? receivingLocatorId : scrapLocatorId;
                string locatorSet = locatorToId > 0 ? ", M_LocatorTo_ID = " + locatorToId : "";

                string sql = "";
                if (lineId > 0)
                {
                    sql = @"UPDATE M_MovementLine SET
                        ConfirmedQty = " + confirmedQty.ToString(System.Globalization.CultureInfo.InvariantCulture) + @",
                        ScrappedQty  = " + scrappedQty.ToString(System.Globalization.CultureInfo.InvariantCulture) + @",
                        Description  = '" + description.Replace("'", "''") + @"'" + locatorSet + @"
                        WHERE M_MovementLine_ID = " + lineId;
                }
                else
                {
                    sql = @"UPDATE M_MovementLine SET
                        ConfirmedQty = " + confirmedQty.ToString(System.Globalization.CultureInfo.InvariantCulture) + @",
                        ScrappedQty  = " + scrappedQty.ToString(System.Globalization.CultureInfo.InvariantCulture) + @",
                        Description  = '" + description.Replace("'", "''") + @"'" + locatorSet + @"
                        WHERE M_Movement_ID = " + transferId + " AND Line = " + lineNo;
                }

                DB.ExecuteQuery(sql);

                // The confirmation line's own receiving locator overrides the movement line's
                // destination when it is read back (GetLines), so it is kept in step as well.
                if (receivingLocatorId > 0
                    && ColumnExists("M_MovementLineConfirm", "VAS_ReceivingLocator_ID")
                    && ColumnExists("M_MovementLineConfirm", "M_MovementLine_ID"))
                {
                    if (lineId <= 0 && transferId > 0)
                    {
                        lineId = Util.GetValueOfInt(DB.ExecuteScalar("SELECT M_MovementLine_ID FROM M_MovementLine WHERE M_Movement_ID = "
                            + transferId + " AND Line = " + lineNo, null, null));
                    }
                    if (lineId > 0)
                    {
                        DB.ExecuteQuery("UPDATE M_MovementLineConfirm SET VAS_ReceivingLocator_ID = " + receivingLocatorId
                            + " WHERE M_MovementLine_ID = " + lineId + " AND IsActive = 'Y'");
                    }
                }

                return Json(new { success = true }, JsonRequestBehavior.AllowGet);
            }
            catch (Exception ex)
            {
                return Json(new { error = ex.Message }, JsonRequestBehavior.AllowGet);
            }
        }

        [HttpPost]
        [AjaxAuthorizeAttribute]
        [AjaxSessionFilterAttribute]
        public JsonResult CompleteTransfer()
        {
            try
            {
                string json = "";
                if (Request.InputStream != null)
                {
                    Request.InputStream.Position = 0;
                    using (var reader = new System.IO.StreamReader(Request.InputStream))
                    {
                        json = reader.ReadToEnd();
                    }
                }
                if (string.IsNullOrEmpty(json) && Request.Form.Keys.Count > 0)
                {
                    json = Request.Form[0];
                }

                var data = JsonConvert.DeserializeObject<Dictionary<string, object>>(json);
                int transferId = Util.GetValueOfInt(data.ContainsKey("TransferID") ? data["TransferID"] : 0);

                if (transferId > 0)
                {
                    string sql = "UPDATE M_Movement SET DocStatus = 'CO', Processed = 'Y' WHERE M_Movement_ID = " + transferId;
                    DB.ExecuteQuery(sql);
                }

                return Json(new { success = true }, JsonRequestBehavior.AllowGet);
            }
            catch (Exception ex)
            {
                return Json(new { error = ex.Message }, JsonRequestBehavior.AllowGet);
            }
        }

        // ===== NEW CODE START — currency format (agent C06, 2026-08-19) =====
        [AjaxAuthorizeAttribute]
        [AjaxSessionFilterAttribute]
        public JsonResult GetCurrencyInfo()
        {
            if (Session["ctx"] == null)
                return Json(new { iso = "", symbol = "" }, JsonRequestBehavior.AllowGet);

            Ctx ctx = Session["ctx"] as Ctx;
            var info = GetCurrencyInfo(ctx);
            return Json(new { iso = info.iso, symbol = info.symbol }, JsonRequestBehavior.AllowGet);
        }

        public class CurrencyInfoResult
        {
            public string iso { get; set; }
            public string symbol { get; set; }
        }

        public static CurrencyInfoResult GetCurrencyInfo(Ctx ctx)
        {
            var res = new CurrencyInfoResult { iso = "", symbol = "" };
            if (ctx == null) return res;
            string sql = @"
                SELECT
                    c.ISO_Code,
                    COALESCE(c.CurSymbol, c.ISO_Code) AS CurSymbol
                FROM AD_ClientInfo ci
                INNER JOIN C_AcctSchema a ON (a.C_AcctSchema_ID = ci.C_AcctSchema1_ID)
                INNER JOIN C_Currency c ON (c.C_Currency_ID = a.C_Currency_ID)
                WHERE ci.AD_Client_ID = " + ctx.GetAD_Client_ID();

            IDataReader dr = null;
            try
            {
                dr = DB.ExecuteReader(sql);
                if (dr != null && dr.Read())
                {
                    res.iso = Util.GetValueOfString(dr["ISO_Code"]);
                    res.symbol = Util.GetValueOfString(dr["CurSymbol"]);
                }
            }
            catch { }
            finally
            {
                if (dr != null) { dr.Close(); dr.Dispose(); }
            }
            return res;
        }
        // ===== NEW CODE END — currency format =====
        // ----- OLD CODE (kept for rollback, do not delete) -----
        // ----- END OLD CODE -----
    }
}
