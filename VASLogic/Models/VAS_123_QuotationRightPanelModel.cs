/// <summary>
/// Module Name : VAS
/// Purpose     : Sales Quotation Right Detail Panel — data model
/// Chronological development:
///   VAI154  20-Jul-2026
/// </summary>
using System;
using System.Collections.Generic;
using System.Data;
using System.Data.SqlClient;
using System.Dynamic;
using System.Text;
using VAdvantage.DataBase;
using VAdvantage.Logging;
using VAdvantage.Model;
using VAdvantage.Process;
using VAdvantage.ProcessEngine;
using VAdvantage.Utility;

namespace VAS.Models
{
    /// <summary>
    /// Module Name : VAS
    /// Purpose     : Sales Quotation Right Detail Panel — data model.
    ///               Provides header summary, next meeting, document action execution,
    ///               and quotation-to-sales-order conversion for the CRM right panel.
    /// Chronological development:
    ///   VAI154  20-Jul-2026
    /// </summary>
    public class VAS_123_QuotationRightPanelModel
    {
        private static readonly VLogger _log = VLogger.GetVLogger(typeof(VAS_123_QuotationRightPanelModel).FullName);

        // Cached per-deployment flag: null = not yet checked, true/false = exists.
        // Avoids a repeated AD_Column round-trip on every GetHeader call once the
        // column absence is confirmed (common on Oracle deployments that lack SO_CreditStatus).
        private static bool? _creditStatusColExists = null;

        // ─────────────────────────────────────────────────────────────────────
        // §1  AD_Table lookup
        // ─────────────────────────────────────────────────────────────────────

        /// <summary>
        /// Returns the AD_Table_ID for the C_Order table.
        /// Used downstream to query AppointmentsInfo by table + record key.
        /// </summary>
        /// <param name="ctx">Current session context.</param>
        /// <returns>AD_Table_ID for C_Order, or 0 on failure/not found.</returns>
        public int GetCOrderTableId(Ctx ctx)
        {
            string baseSql = @"SELECT MIN(t.AD_Table_ID)
                FROM AD_Table t
                WHERE t.TableName = 'C_Order'
                AND t.IsActive = 'Y'";

            string accessSql = MRole.GetDefault(ctx).AddAccessSQL(
                baseSql, "t", MRole.SQL_FULLYQUALIFIED, MRole.SQL_RO);

            try
            {
                object result = DB.ExecuteScalar(accessSql, null, null);
                if (result != null && result != DBNull.Value)
                    return Util.GetValueOfInt(result);
            }
            catch (Exception ex)
            {
                _log.SaveError("VAS_123_QuotationRightPanelModel.GetCOrderTableId", ex.Message);
            }

            return 0;
        }

        // ─────────────────────────────────────────────────────────────────────
        // §2  Quotation header
        // ─────────────────────────────────────────────────────────────────────

        /// <summary>
        /// Returns the full quotation header for the right panel, including
        /// business partner, contact, sales rep, currency, and any linked
        /// converted sales order.
        /// SO_CreditStatus is queried separately, guarded by a cached column-existence
        /// check so Oracle deployments that lack the column never produce ORA-00904.
        /// </summary>
        /// <param name="ctx">Current session context.</param>
        /// <param name="orderId">C_Order_ID of the sales quotation.</param>
        /// <returns>
        /// Dynamic object with all header fields, or <c>response.error = "not_found"</c>
        /// when no matching active quotation row exists.
        /// </returns>
        public dynamic GetHeader(Ctx ctx, int orderId)
        {
            dynamic response = new ExpandoObject();
            response.error = null;

            // Fetch DocStatus display names from AD_Ref_List for the session language.
            // The client uses this dictionary instead of VIS.Msg.getMsg('DocStatus_*')
            // so labels are always resolved from the database in the correct language.
            response.docStatusLabels = GetRefListNames(ctx, "DocStatus", "C_Order");

            // SO_CreditStatus may not exist in older schemas — read separately below.
            // Pricing & terms data (PaymentRule, PriorityRule, PriceListName, PaymentTermName)
            // is fetched by GetPricingTerms so that a schema-missing-column error only
            // breaks the terms section, not the entire panel.
            string baseSql = @"SELECT o.C_Order_ID AS C_Order_ID,
                o.DocumentNo AS DocumentNo,
                o.DateOrdered AS DateOrdered,
                o.OrderValidTo AS OrderValidTo,
                o.DocStatus AS DocStatus,
                o.DocAction AS DocAction,
                COALESCE(o.GrandTotal, 0) AS GrandTotal,
                COALESCE(o.TotalLines, 0) AS TotalLines,
                o.C_BPartner_ID AS C_BPartner_ID,
                bp.Name AS BPartnerName,
                o.AD_User_ID AS AD_User_ID,
                COALESCE(ct.Name, N'') AS ContactName,
                COALESCE(ct.Title, N'') AS ContactTitle,
                COALESCE(ct.EMail, N'') AS ContactEmail,
                COALESCE(ct.Mobile, ct.Phone, N'') AS ContactPhone,
                o.SalesRep_ID AS SalesRep_ID,
                COALESCE(sr.Name, N'') AS SalesRepName,
                o.C_Currency_ID AS C_Currency_ID,
                COALESCE(cur.ISO_Code, N'') AS CurrencyISO,
                COALESCE(cur.CurSymbol, N'') AS CurrencySymbol,
                COALESCE(cur.StdPrecision, 2) AS CurrencyPrecision,
                o.Updated AS LastDocumentActionDate,
                COALESCE(o.CustomerReference, o.POReference, N'') AS CustomerReference,
                (SELECT MIN(so.C_Order_ID) FROM C_Order so WHERE so.Ref_Order_ID = o.C_Order_ID AND so.IsActive = 'Y' AND so.IsSOTrx = 'Y' AND so.IsSalesQuotation = 'N') AS ConvertedOrder_ID,
                (SELECT MIN(so.DocumentNo) FROM C_Order so WHERE so.Ref_Order_ID = o.C_Order_ID AND so.IsActive = 'Y' AND so.IsSOTrx = 'Y' AND so.IsSalesQuotation = 'N') AS ConvertedOrderNo,
                (SELECT MIN(so.DateOrdered) FROM C_Order so WHERE so.Ref_Order_ID = o.C_Order_ID AND so.IsActive = 'Y' AND so.IsSOTrx = 'Y' AND so.IsSalesQuotation = 'N') AS ConvertedOrderDate
                FROM C_Order o
                INNER JOIN C_BPartner bp ON (bp.C_BPartner_ID = o.C_BPartner_ID)
                LEFT OUTER JOIN AD_User ct ON (ct.AD_User_ID = o.AD_User_ID)
                LEFT OUTER JOIN AD_User sr ON (sr.AD_User_ID = o.SalesRep_ID)
                LEFT OUTER JOIN C_Currency cur ON (cur.C_Currency_ID = o.C_Currency_ID)
                WHERE o.C_Order_ID = @orderId
                AND o.IsActive = 'Y'
                AND o.IsSOTrx = 'Y'
                AND o.IsSalesQuotation = 'Y'";

            string accessSql = MRole.GetDefault(ctx).AddAccessSQL(
                baseSql, "o", MRole.SQL_FULLYQUALIFIED, MRole.SQL_RO);

            var sqlParams = new SqlParameter[]
            {
                new SqlParameter("@orderId", orderId)
            };

            try
            {
                DataSet ds = DB.ExecuteDataset(accessSql, sqlParams, null);
                if (ds == null || ds.Tables.Count == 0 || ds.Tables[0].Rows.Count == 0)
                {
                    response.error = "not_found";
                    return response;
                }

                DataRow row = ds.Tables[0].Rows[0];

                // camelCase property names — JS accesses h.docStatus, h.documentNo, etc.
                response.c_Order_ID  = Util.GetValueOfInt(row["C_Order_ID"]);
                response.documentNo  = Util.GetValueOfString(row["DocumentNo"]);

                // dateOrdered — may be null for a draft quotation
                response.dateOrdered = row["DateOrdered"] != DBNull.Value
                    ? Convert.ToDateTime(row["DateOrdered"]) : (DateTime?)null;

                // orderValidTo — optional validity date
                response.orderValidTo = row["OrderValidTo"] != DBNull.Value
                    ? Convert.ToDateTime(row["OrderValidTo"]) : (DateTime?)null;

                response.docStatus   = Util.GetValueOfString(row["DocStatus"]);
                response.docAction   = Util.GetValueOfString(row["DocAction"]);
                response.grandTotal  = row["GrandTotal"] != DBNull.Value
                    ? Convert.ToDecimal(row["GrandTotal"]) : 0m;
                response.totalLines  = row["TotalLines"] != DBNull.Value
                    ? Convert.ToDecimal(row["TotalLines"]) : 0m;
                response.c_BPartner_ID  = Util.GetValueOfInt(row["C_BPartner_ID"]);
                response.bPartnerName   = Util.GetValueOfString(row["BPartnerName"]);
                response.ad_User_ID     = Util.GetValueOfInt(row["AD_User_ID"]);
                response.contactName    = Util.GetValueOfString(row["ContactName"]);
                response.contactTitle   = Util.GetValueOfString(row["ContactTitle"]);
                response.contactEmail   = Util.GetValueOfString(row["ContactEmail"]);
                response.contactPhone   = Util.GetValueOfString(row["ContactPhone"]);
                // contactMobile aliases contactPhone — JS uses both keys
                response.contactMobile  = response.contactPhone;
                response.salesRep_ID    = Util.GetValueOfInt(row["SalesRep_ID"]);
                response.salesRepName   = Util.GetValueOfString(row["SalesRepName"]);
                response.c_Currency_ID  = Util.GetValueOfInt(row["C_Currency_ID"]);
                response.currencyISO    = Util.GetValueOfString(row["CurrencyISO"]);
                // currencyCode is an alias consumed by renderTerms(); currencyISO serves Part 1
                response.currencyCode   = response.currencyISO;
                response.currencySymbol = Util.GetValueOfString(row["CurrencySymbol"]);
                response.currencyPrecision = row["CurrencyPrecision"] != DBNull.Value
                    ? Util.GetValueOfInt(row["CurrencyPrecision"]) : 2;
                response.lastDocumentActionDate = row["LastDocumentActionDate"] != DBNull.Value
                    ? Convert.ToDateTime(row["LastDocumentActionDate"]) : (DateTime?)null;
                response.customerReference = Util.GetValueOfString(row["CustomerReference"]);
                // poReference is an alias used by renderTerms() ("Customer reference" label per spec)
                response.poReference = response.customerReference;

                // Linked confirmed sales order (null when not yet converted)
                response.convertedOrder_ID = row["ConvertedOrder_ID"] != DBNull.Value
                    ? Util.GetValueOfInt(row["ConvertedOrder_ID"]) : (int?)null;
                response.convertedOrderNo = row["ConvertedOrderNo"] != DBNull.Value
                    ? Util.GetValueOfString(row["ConvertedOrderNo"]) : string.Empty;
                response.convertedOrderDate = row["ConvertedOrderDate"] != DBNull.Value
                    ? Convert.ToDateTime(row["ConvertedOrderDate"]) : (DateTime?)null;
            }
            catch (Exception ex)
            {
                _log.SaveError("VAS_123_QuotationRightPanelModel.GetHeader", ex.Message);
                response.error = "not_found";
                return response;
            }

            // ── SO_CreditStatus — only query when the column is confirmed to exist.
            //    We cache the result of the AD_Column check so the extra round-trip
            //    happens at most once per application lifetime (not on every panel load).
            //    This prevents OracleHelper from logging ORA-00904 as SEVERE when the
            //    column is absent — the try/catch in the query itself cannot suppress
            //    the Oracle-level log that fires before the exception propagates.
            response.creditStatus = string.Empty;
            if (_creditStatusColExists == null)
            {
                try
                {
                    string colCheckSql = @"SELECT COUNT(*) FROM AD_Column c
                        INNER JOIN AD_Table t ON (t.AD_Table_ID = c.AD_Table_ID)
                        WHERE t.TableName = 'C_BPartner'
                        AND c.ColumnName = 'SO_CreditStatus'
                        AND c.IsActive = 'Y'";
                    object colCheck = DB.ExecuteScalar(colCheckSql, null, null);
                    _creditStatusColExists = (colCheck != null && Util.GetValueOfInt(colCheck) > 0);
                }
                catch (Exception ex)
                {
                    _log.SaveError("VAS_123_QuotationRightPanelModel.CreditStatusColCheck", ex.Message);
                    _creditStatusColExists = false;
                }
            }

            if (_creditStatusColExists == true)
            {
                try
                {
                    string creditBaseSql = @"SELECT COALESCE(bp.SO_CreditStatus, N'') AS CreditStatus
                        FROM C_BPartner bp
                        INNER JOIN C_Order o ON (o.C_BPartner_ID = bp.C_BPartner_ID)
                        WHERE o.C_Order_ID = @orderId
                        AND o.IsActive = 'Y'";

                    string creditAccessSql = MRole.GetDefault(ctx).AddAccessSQL(
                        creditBaseSql, "bp", MRole.SQL_FULLYQUALIFIED, MRole.SQL_RO);

                    object creditResult = DB.ExecuteScalar(creditAccessSql,
                        new SqlParameter[] { new SqlParameter("@orderId", orderId) }, null);

                    if (creditResult != null && creditResult != DBNull.Value)
                        response.creditStatus = Util.GetValueOfString(creditResult);
                }
                catch (Exception ex)
                {
                    _log.SaveError("VAS_123_QuotationRightPanelModel.GetCreditStatus", ex.Message);
                    response.creditStatus = string.Empty;
                }
            }

            return response;
        }

        // ─────────────────────────────────────────────────────────────────────
        // §3  Next meeting
        // ─────────────────────────────────────────────────────────────────────

        /// <summary>
        /// Returns the next upcoming (non-deleted, non-cancelled) meeting linked to
        /// the given quotation record from AppointmentsInfo.
        /// Uses LIMIT 1 on PostgreSQL or a ROWNUM = 1 sub-select wrapper on Oracle
        /// so the same business logic runs on both database engines.
        /// AppointmentsInfo string columns are VARCHAR2 on Oracle — COALESCE with N''
        /// (NCHAR literal) would raise ORA-12704, so nulls are handled in C# instead.
        /// </summary>
        /// <param name="ctx">Current session context.</param>
        /// <param name="orderId">C_Order_ID of the quotation (Record_ID in AppointmentsInfo).</param>
        /// <param name="tableId">AD_Table_ID for C_Order, obtained from GetCOrderTableId().</param>
        /// <returns>
        /// Dynamic object with meeting fields, or <c>null</c> when no upcoming meeting exists.
        /// </returns>
        public dynamic GetNextMeeting(Ctx ctx, int orderId, int tableId)
        {
            string innerSql = @"SELECT ai.AppointmentsInfo_ID AS AppointmentsInfo_ID,
                ai.Subject AS Subject,
                ai.StartDate AS StartDate,
                ai.EndDate AS EndDate,
                ai.MeetingUrl AS MeetingUrl,
                ai.Location AS Location,
                ai.AttendeeInfo AS AttendeeInfo,
                ai.EmailToInfo AS EmailToInfo,
                ai.Description AS Description
                FROM AppointmentsInfo ai
                WHERE ai.AD_Table_ID = @tableId
                AND ai.Record_ID = @orderId
                AND ai.IsActive = 'Y'
                AND COALESCE(ai.IsDeleted, 'N') = 'N'
                AND COALESCE(ai.IsCancelled, 'N') = 'N'
                AND ai.StartDate >= @todayStart
                ORDER BY ai.StartDate ASC";

            string accessSql = MRole.GetDefault(ctx).AddAccessSQL(
                innerSql, "ai", MRole.SQL_FULLYQUALIFIED, MRole.SQL_RO);

            // Apply DB-engine-specific row limit
            string finalSql = DB.IsOracle()
                ? "SELECT * FROM (" + accessSql + ") WHERE ROWNUM = 1"
                : accessSql + " LIMIT 1";

            // Compare against midnight of today so that meetings scheduled earlier
            // in the same day are not excluded by the current time of day.
            DateTime todayStart = DateTime.Today;

            var sqlParams = new SqlParameter[]
            {
                new SqlParameter("@tableId",    tableId),
                new SqlParameter("@orderId",    orderId),
                new SqlParameter("@todayStart", todayStart)
            };

            try
            {
                DataSet ds = DB.ExecuteDataset(finalSql, sqlParams, null);
                if (ds == null || ds.Tables.Count == 0 || ds.Tables[0].Rows.Count == 0)
                    return null;

                DataRow row = ds.Tables[0].Rows[0];

                dynamic meeting = new ExpandoObject();
                meeting.appointmentsInfo_ID = Util.GetValueOfInt(row["AppointmentsInfo_ID"]);
                meeting.subject     = Util.GetValueOfString(row["Subject"]);
                meeting.startDate   = row["StartDate"] != DBNull.Value
                    ? Convert.ToDateTime(row["StartDate"]) : (DateTime?)null;
                meeting.endDate     = row["EndDate"] != DBNull.Value
                    ? Convert.ToDateTime(row["EndDate"]) : (DateTime?)null;
                meeting.meetingUrl  = Util.GetValueOfString(row["MeetingUrl"]);
                meeting.location    = Util.GetValueOfString(row["Location"]);
                meeting.attendeeInfo  = Util.GetValueOfString(row["AttendeeInfo"]);
                meeting.emailToInfo   = Util.GetValueOfString(row["EmailToInfo"]);
                meeting.description   = Util.GetValueOfString(row["Description"]);

                return meeting;
            }
            catch (Exception ex)
            {
                _log.SaveError("VAS_123_QuotationRightPanelModel.GetNextMeeting", ex.Message);
                return null;
            }
        }

        // ─────────────────────────────────────────────────────────────────────
        // §4  Document action
        // ─────────────────────────────────────────────────────────────────────

        /// <summary>
        /// Executes a document action (Complete, Void, Close, etc.) against the
        /// specified quotation using the standard DocumentEngine.
        /// The action string is passed verbatim to <c>DocumentEngine.ProcessIt</c>
        /// so it must match one of the <c>DocActionVariables.ACTION_*</c> constants.
        /// </summary>
        /// <param name="ctx">Current session context.</param>
        /// <param name="orderId">C_Order_ID of the quotation to act on.</param>
        /// <param name="docAction">DocAction constant string (e.g. "CO", "VO", "CL").</param>
        /// <returns>
        /// Dynamic with <c>success = true</c> and <c>newDocStatus</c> on success,
        /// or <c>success = false</c> and <c>error</c> message on failure.
        /// </returns>
        public dynamic ExecuteDocAction(Ctx ctx, int orderId, string docAction)
        {
            dynamic response = new ExpandoObject();
            response.success = false;
            response.error = null;
            response.newDocStatus = null;

            try
            {
                // Load the order record — returns ID = 0 if not found or inaccessible
                MOrder order = new MOrder(ctx, orderId, null);
                if (order.Get_ID() == 0)
                {
                    response.error = "not_found";
                    return response;
                }

                DocumentEngine de = new DocumentEngine(order, order.GetDocStatus());
                bool ok = de.ProcessIt(docAction, order.GetDocAction());

                if (ok)
                {
                    order.SetDocAction(DocActionVariables.ACTION_NONE);
                    order.Save();
                    response.success = true;
                    response.newDocStatus = order.GetDocStatus();
                }
                else
                {
                    string procMsg = order.GetProcessMsg();
                    response.error = !string.IsNullOrEmpty(procMsg) ? procMsg : "action_failed";
                }
            }
            catch (Exception ex)
            {
                _log.SaveError("VAS_123_QuotationRightPanelModel.ExecuteDocAction", ex.Message);
                response.error = ex.Message;
            }

            return response;
        }

        // ─────────────────────────────────────────────────────────────────────
        // §5  Convert quotation to sales order
        // ─────────────────────────────────────────────────────────────────────

        /// <summary>
        /// Converts a completed sales quotation to a confirmed sales order.
        /// Strategy (in order):
        ///   1. Validate that the quotation is Completed (DocStatus = 'CO') and
        ///      is indeed a quotation (IsSalesQuotation = 'Y').
        ///   2. Look up the conversion process in AD_Process by well-known Value
        ///      keys so the implementation is portable across environments that
        ///      may have different surrogate AD_Process_IDs.
        ///   3. If a process is found, execute it via ProcessCtl.
        ///   4. Regardless of whether a process ran, query back for a newly linked
        ///      C_Order row (Ref_Order_ID = orderId, IsSalesQuotation = 'N') so
        ///      the client can navigate to the new order immediately.
        /// </summary>
        /// <param name="ctx">Current session context.</param>
        /// <param name="orderId">C_Order_ID of the quotation to convert.</param>
        /// <returns>
        /// Dynamic with <c>success</c> (bool), <c>newOrderId</c> (int?),
        /// <c>newOrderNo</c> (string), and <c>error</c> (string).
        /// </returns>
        public dynamic ConvertToSalesOrder(Ctx ctx, int orderId)
        {
            dynamic response = new ExpandoObject();
            response.success = false;
            response.newOrderId = null;
            response.newOrderNo = string.Empty;
            response.error = null;

            if (orderId <= 0)
            {
                response.error = "invalid_order_id";
                return response;
            }

            // ── Validate the quotation record ─────────────────────────────────
            try
            {
                MOrder quotation = new MOrder(ctx, orderId, null);
                if (quotation.Get_ID() == 0)
                {
                    response.error = "not_found";
                    return response;
                }
                if (!DocActionVariables.STATUS_COMPLETED.Equals(quotation.GetDocStatus()))
                {
                    response.error = "not_completed";
                    return response;
                }
                if (!quotation.IsSOTrx() || !"Y".Equals(Util.GetValueOfString(quotation.Get_Value("IsSalesQuotation"))))
                {
                    response.error = "not_a_quotation";
                    return response;
                }
            }
            catch (Exception ex)
            {
                _log.SaveError("VAS_123_QuotationRightPanelModel.ConvertToSalesOrder.Validate", ex.Message);
                response.error = ex.Message;
                return response;
            }

            // ── Look up the conversion process in AD_Process ──────────────────
            // Three well-known Value keys are checked so the lookup succeeds
            // regardless of which implementation is installed in the environment.
            int processId = 0;
            string processName = string.Empty;
            try
            {
                string procBaseSql = @"SELECT MIN(p.AD_Process_ID) AS AD_Process_ID,
                    (SELECT p2.Name FROM AD_Process p2 WHERE p2.AD_Process_ID = MIN(p.AD_Process_ID)) AS ProcessName
                    FROM AD_Process p
                    WHERE p.IsActive = 'Y'
                    AND p.Value IN ('C_Order_CreateOrder', 'VAS_QuotationToOrder', 'CreateOrderFromQuotation')";

                string procAccessSql = MRole.GetDefault(ctx).AddAccessSQL(
                    procBaseSql, "p", MRole.SQL_FULLYQUALIFIED, MRole.SQL_RO);

                DataSet procDs = DB.ExecuteDataset(procAccessSql, null, null);
                if (procDs != null && procDs.Tables.Count > 0 && procDs.Tables[0].Rows.Count > 0)
                {
                    DataRow procRow = procDs.Tables[0].Rows[0];
                    if (procRow["AD_Process_ID"] != DBNull.Value)
                    {
                        processId   = Util.GetValueOfInt(procRow["AD_Process_ID"]);
                        processName = Util.GetValueOfString(procRow["ProcessName"]);
                    }
                }
            }
            catch (Exception ex)
            {
                _log.SaveError("VAS_123_QuotationRightPanelModel.ConvertToSalesOrder.LookupProcess", ex.Message);
                // Non-fatal: fall through to the Ref_Order_ID lookup
            }

            // ── Execute the conversion process (if one was found) ─────────────
            if (processId > 0)
            {
                try
                {
                    MPInstance instance = new MPInstance(ctx, processId, orderId);
                    if (!instance.Save())
                    {
                        _log.SaveError("VAS_123_QuotationRightPanelModel.ConvertToSalesOrder.MPInstance",
                            "Could not create AD_PInstance for process " + processId);
                    }
                    else
                    {
                        ProcessInfo pi = new ProcessInfo(processName, processId);
                        pi.SetAD_PInstance_ID(instance.GetAD_PInstance_ID());
                        pi.SetRecord_ID(orderId);
                        pi.SetAD_Client_ID(ctx.GetAD_Client_ID());
                        pi.SetAD_Org_ID(ctx.GetAD_Org_ID());
                        pi.SetAD_User_ID(ctx.GetAD_User_ID());

                        ProcessCtl worker = new ProcessCtl(ctx, null, pi, null);
                        worker.Run();

                        string summary = pi.GetSummary();
                        if (!string.IsNullOrEmpty(summary))
                            _log.Info("VAS_123 ConvertToSalesOrder process summary: " + summary);

                        if (pi.IsError())
                            response.error = !string.IsNullOrEmpty(summary) ? summary : "process_error";
                    }
                }
                catch (Exception ex)
                {
                    _log.SaveError("VAS_123_QuotationRightPanelModel.ConvertToSalesOrder.Run", ex.Message);
                    response.error = ex.Message;
                    // Still attempt the Ref_Order_ID lookup; the process may have committed
                }
            }
            else
            {
                response.error = "process_not_configured";
            }

            // ── Read back the newly created (or already existing) linked order ─
            try
            {
                string newBaseSql = @"SELECT MIN(so.C_Order_ID) AS new_order_id,
                    MIN(so.DocumentNo) AS new_order_no
                    FROM C_Order so
                    WHERE so.Ref_Order_ID = @orderId
                    AND so.IsActive = 'Y'
                    AND so.IsSalesQuotation = 'N'
                    AND so.IsSOTrx = 'Y'";

                string newAccessSql = MRole.GetDefault(ctx).AddAccessSQL(
                    newBaseSql, "so", MRole.SQL_FULLYQUALIFIED, MRole.SQL_RO);

                DataSet newDs = DB.ExecuteDataset(newAccessSql,
                    new SqlParameter[] { new SqlParameter("@orderId", orderId) }, null);

                if (newDs != null && newDs.Tables.Count > 0 && newDs.Tables[0].Rows.Count > 0)
                {
                    DataRow newRow = newDs.Tables[0].Rows[0];
                    if (newRow["new_order_id"] != DBNull.Value)
                    {
                        response.newOrderId = Util.GetValueOfInt(newRow["new_order_id"]);
                        response.newOrderNo = Util.GetValueOfString(newRow["new_order_no"]);

                        if (response.error == null || "process_not_configured".Equals((string)response.error))
                            response.success = true;
                    }
                }
            }
            catch (Exception ex)
            {
                _log.SaveError("VAS_123_QuotationRightPanelModel.ConvertToSalesOrder.ReadBack", ex.Message);
                if (response.error == null)
                    response.error = ex.Message;
            }

            return response;
        }

        // ─────────────────────────────────────────────────────────────────────
        // §6  AD_Ref_List helpers
        // ─────────────────────────────────────────────────────────────────────

        /// <summary>
        /// Returns a Value → display-name dictionary for the reference list attached
        /// to <paramref name="columnName"/> on <paramref name="tableName"/>,
        /// resolved in the session language with fallback to the base name or value.
        /// </summary>
        /// <param name="ctx">Current session context.</param>
        /// <param name="columnName">AD_Column.ColumnName to look up (e.g. "DocStatus").</param>
        /// <param name="tableName">AD_Table.TableName that owns the column (e.g. "C_Order").</param>
        /// <returns>Dictionary mapping each list Value to its translated display name.</returns>
        private Dictionary<string, string> GetRefListNames(Ctx ctx, string columnName, string tableName)
        {
            var result = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            try
            {
                string lang = ctx.GetAD_Language();

                string sql = @"SELECT rl.Value,
                    COALESCE(rlt.Name, rl.Name, rl.Value) AS DisplayName
                    FROM AD_Column c
                    INNER JOIN AD_Table t ON (t.AD_Table_ID = c.AD_Table_ID
                                              AND t.IsActive = 'Y')
                    INNER JOIN AD_Ref_List rl ON (rl.AD_Reference_ID = c.AD_Reference_Value_ID
                                                  AND rl.IsActive = 'Y')
                    LEFT OUTER JOIN AD_Ref_List_Trl rlt ON (rlt.AD_Ref_List_ID = rl.AD_Ref_List_ID
                                                             AND rlt.AD_Language = @lang
                                                             AND rlt.IsActive = 'Y')
                    WHERE c.ColumnName = @columnName
                    AND c.IsActive = 'Y'
                    AND t.TableName = @tableName";

                var sqlParams = new SqlParameter[]
                {
                    new SqlParameter("@lang",       lang),
                    new SqlParameter("@columnName", columnName),
                    new SqlParameter("@tableName",  tableName)
                };

                DataSet ds = DB.ExecuteDataset(sql, sqlParams, null);
                if (ds != null && ds.Tables.Count > 0)
                {
                    foreach (DataRow row in ds.Tables[0].Rows)
                    {
                        string val  = Util.GetValueOfString(row["Value"]);
                        string name = Util.GetValueOfString(row["DisplayName"]);
                        if (!string.IsNullOrEmpty(val) && !result.ContainsKey(val))
                            result[val] = name;
                    }
                }
            }
            catch (Exception ex)
            {
                _log.SaveError("VAS_123_QuotationRightPanelModel.GetRefListNames", ex.Message);
            }
            return result;
        }

        // ─────────────────────────────────────────────────────────────────────
        // §7  Generated Sales Orders (Part 2 — Section 2.1)
        // ─────────────────────────────────────────────────────────────────────

        /// <summary>
        /// Returns all active sales orders whose Ref_Order_ID points at the given
        /// quotation. Used by the Orders section of the right panel.
        /// </summary>
        /// <param name="ctx">Current session context.</param>
        /// <param name="orderId">C_Order_ID of the source quotation.</param>
        /// <returns>List of dynamic objects, one per generated order, ordered newest first.</returns>
        public List<dynamic> GetGeneratedOrders(Ctx ctx, int orderId)
        {
            var result = new List<dynamic>();

            string baseSql = @"SELECT so.C_Order_ID AS C_Order_ID,
                so.DocumentNo AS DocumentNo,
                so.DateOrdered AS DateOrdered,
                so.DatePromised AS DatePromised,
                so.DocStatus AS DocStatus,
                COALESCE(so.GrandTotal, 0) AS GrandTotal,
                so.C_Currency_ID AS C_Currency_ID,
                so.Ref_Order_ID AS Ref_Order_ID,
                so.Updated AS Updated
                FROM C_Order so
                WHERE so.Ref_Order_ID = @orderId
                AND so.IsSOTrx = 'Y'
                AND so.IsSalesQuotation = 'N'
                AND so.IsActive = 'Y'
                ORDER BY so.Created DESC";

            string accessSql = MRole.GetDefault(ctx).AddAccessSQL(
                baseSql, "so", MRole.SQL_FULLYQUALIFIED, MRole.SQL_RO);

            try
            {
                DataSet ds = DB.ExecuteDataset(accessSql,
                    new SqlParameter[] { new SqlParameter("@orderId", orderId) }, null);

                if (ds == null || ds.Tables.Count == 0) return result;

                // Fetch decoded DocStatus labels for display
                var statusLabels = GetRefListNames(ctx, "DocStatus", "C_Order");

                foreach (DataRow row in ds.Tables[0].Rows)
                {
                    dynamic o = new ExpandoObject();
                    o.c_Order_ID   = Util.GetValueOfInt(row["C_Order_ID"]);
                    o.documentNo   = Util.GetValueOfString(row["DocumentNo"]);
                    o.dateOrdered  = row["DateOrdered"] != DBNull.Value
                        ? Convert.ToDateTime(row["DateOrdered"]) : (DateTime?)null;
                    o.datePromised = row["DatePromised"] != DBNull.Value
                        ? Convert.ToDateTime(row["DatePromised"]) : (DateTime?)null;
                    o.docStatus    = Util.GetValueOfString(row["DocStatus"]);
                    o.grandTotal   = row["GrandTotal"] != DBNull.Value
                        ? Convert.ToDecimal(row["GrandTotal"]) : 0m;
                    o.c_Currency_ID = Util.GetValueOfInt(row["C_Currency_ID"]);
                    result.Add(o);
                }
            }
            catch (Exception ex)
            {
                _log.SaveError("VAS_123_QuotationRightPanelModel.GetGeneratedOrders", ex.Message);
            }

            return result;
        }

        // ─────────────────────────────────────────────────────────────────────
        // §8  Opportunity link (Part 2 — Section 2.2)
        // ─────────────────────────────────────────────────────────────────────

        /// <summary>
        /// Returns the VAS_Opportunity_ID stored on the quotation, or null when
        /// no opportunity is linked. The Opportunity display table is not yet confirmed,
        /// so only the foreign-key value is returned at this stage.
        /// </summary>
        /// <param name="ctx">Current session context.</param>
        /// <param name="orderId">C_Order_ID of the quotation.</param>
        /// <returns>Dynamic with vAS_Opportunity_ID, or null when not linked.</returns>
        public dynamic GetOpportunity(Ctx ctx, int orderId)
        {
            var sb = new StringBuilder();
            sb.Append("SELECT o.C_Order_ID AS C_Order_ID,");
            sb.Append("       op.VAS_Opportunity_ID AS VAS_Opportunity_ID,");
            sb.Append("       op.Name AS OpportunityName,");
            sb.Append("       op.VAS_OppStage AS Stage,");
            sb.Append("       TO_CHAR(op.VAS_DecisionDate, 'YYYY-MM-DD') AS ExpectedCloseDate,");
            sb.Append("       rep.Name AS SalesRepName,");
            sb.Append("       op.PlannedAmt AS Amount");
            sb.Append("  FROM C_Order o");
            sb.Append("  LEFT OUTER JOIN VAS_Opportunity op ON (op.VAS_Opportunity_ID = o.VAS_Opportunity_ID AND op.IsActive = 'Y')");
            sb.Append("  LEFT OUTER JOIN AD_User rep ON (rep.AD_User_ID = op.SalesRep_ID AND rep.IsActive = 'Y')");
            sb.Append(" WHERE o.C_Order_ID = @orderId");
            sb.Append("   AND o.IsSalesQuotation = 'Y'");
            sb.Append("   AND o.IsActive = 'Y'");

            string accessSql = MRole.GetDefault(ctx).AddAccessSQL(
                sb.ToString(), "o", MRole.SQL_FULLYQUALIFIED, MRole.SQL_RO);

            try
            {
                DataSet ds = DB.ExecuteDataset(accessSql,
                    new SqlParameter[] { new SqlParameter("@orderId", orderId) }, null);

                if (ds == null || ds.Tables.Count == 0 || ds.Tables[0].Rows.Count == 0)
                    return null;

                DataRow row    = ds.Tables[0].Rows[0];
                int oppIdValue = Util.GetValueOfInt(row["VAS_Opportunity_ID"]);

                // Return null when no opportunity is linked on this quotation
                if (oppIdValue <= 0) return null;

                string stageCode = Util.GetValueOfString(row["Stage"]);

                // Resolve stage display name via existing helper — keeps the main query simple
                var stageMap = GetRefListNames(ctx, "VAS_OppStage", "VAS_Opportunity");
                string stageName;
                stageMap.TryGetValue(stageCode, out stageName);
                if (string.IsNullOrEmpty(stageName)) stageName = stageCode;

                dynamic opp = new ExpandoObject();
                opp.vAS_Opportunity_ID = oppIdValue;
                opp.opportunityName    = Util.GetValueOfString(row["OpportunityName"]);
                opp.stage              = stageCode;
                opp.stageName          = stageName;
                opp.expectedCloseDate  = Util.GetValueOfString(row["ExpectedCloseDate"]);
                opp.salesRepName       = Util.GetValueOfString(row["SalesRepName"]);
                opp.amount             = row["Amount"] != DBNull.Value
                    ? (object)Convert.ToDecimal(row["Amount"]) : null;
                return opp;
            }
            catch (Exception ex)
            {
                _log.SaveError("VAS_123_QuotationRightPanelModel.GetOpportunity", ex.Message);
                return null;
            }
        }

        // ─────────────────────────────────────────────────────────────────────
        // §9  Quotation lines (Part 2 — Section 2.3)
        // ─────────────────────────────────────────────────────────────────────

        /// <summary>
        /// Returns all active lines for the quotation, ordered by line sequence.
        /// ProductType from M_Product is used to derive the isService flag
        /// (ProductType = 'S' → service, anything else → product).
        /// </summary>
        /// <param name="ctx">Current session context.</param>
        /// <param name="orderId">C_Order_ID of the quotation.</param>
        /// <returns>List of dynamic objects, one per active line.</returns>
        public List<dynamic> GetQuotationLines(Ctx ctx, int orderId)
        {
            var result = new List<dynamic>();

            // Discount is not a standard C_OrderLine column in all VIS deployments —
            // it is derived in C# from PriceEntered vs PriceActual to avoid a column-not-found error.
            string baseSql = @"SELECT ol.C_OrderLine_ID AS C_OrderLine_ID,
                ol.C_Order_ID AS C_Order_ID,
                ol.Line AS Line,
                ol.M_Product_ID AS M_Product_ID,
                ol.C_Charge_ID AS C_Charge_ID,
                ol.C_UOM_ID AS C_UOM_ID,
                COALESCE(ol.Description, N'') AS Description,
                COALESCE(ol.QtyEntered, 0) AS QtyEntered,
                COALESCE(ol.QtyOrdered, 0) AS QtyOrdered,
                COALESCE(ol.PriceEntered, 0) AS PriceEntered,
                COALESCE(ol.PriceActual, 0) AS PriceActual,
                COALESCE(ol.LineNetAmt, 0) AS LineNetAmt,
                ol.DatePromised AS DatePromised,
                COALESCE(p.Value, N'') AS ProductValue,
                COALESCE(p.Name, ch.Name, N'') AS ProductName,
                p.ProductType AS ProductType,
                (SELECT arl.Name FROM AD_Ref_List arl WHERE arl.Value = p.ProductType AND arl.AD_Reference_ID = (SELECT c.AD_Reference_Value_ID FROM AD_Column c INNER JOIN AD_Table t ON (t.AD_Table_ID = c.AD_Table_ID) WHERE UPPER(t.TableName) = 'M_PRODUCT' AND UPPER(c.ColumnName) = 'PRODUCTTYPE')) AS ProductTypeName,
                COALESCE(u.Name, N'') AS UOMName
                FROM C_OrderLine ol
                LEFT OUTER JOIN M_Product p ON (p.M_Product_ID = ol.M_Product_ID AND p.IsActive = 'Y')
                LEFT OUTER JOIN C_Charge ch ON (ch.C_Charge_ID = ol.C_Charge_ID AND ch.IsActive = 'Y')
                LEFT OUTER JOIN C_UOM u ON (u.C_UOM_ID = ol.C_UOM_ID AND u.IsActive = 'Y')
                WHERE ol.C_Order_ID = @orderId
                AND ol.IsActive = 'Y'
                ORDER BY ol.Line";

            string accessSql = MRole.GetDefault(ctx).AddAccessSQL(
                baseSql, "ol", MRole.SQL_FULLYQUALIFIED, MRole.SQL_RO);

            try
            {
                DataSet ds = DB.ExecuteDataset(accessSql,
                    new SqlParameter[] { new SqlParameter("@orderId", orderId) }, null);

                if (ds == null || ds.Tables.Count == 0) return result;

                foreach (DataRow row in ds.Tables[0].Rows)
                {
                    dynamic l = new ExpandoObject();
                    l.c_OrderLine_ID = Util.GetValueOfInt(row["C_OrderLine_ID"]);
                    l.c_Order_ID     = Util.GetValueOfInt(row["C_Order_ID"]);
                    l.line           = Util.GetValueOfInt(row["Line"]);
                    l.m_Product_ID   = Util.GetValueOfInt(row["M_Product_ID"]);
                    l.c_Charge_ID    = Util.GetValueOfInt(row["C_Charge_ID"]);
                    l.description    = Util.GetValueOfString(row["Description"]);
                    l.qtyEntered     = row["QtyEntered"] != DBNull.Value
                        ? Convert.ToDecimal(row["QtyEntered"]) : 0m;
                    l.priceEntered   = row["PriceEntered"] != DBNull.Value
                        ? Convert.ToDecimal(row["PriceEntered"]) : 0m;
                    l.priceActual    = row["PriceActual"] != DBNull.Value
                        ? Convert.ToDecimal(row["PriceActual"]) : 0m;

                    // Derive discount % from PriceEntered vs PriceActual —
                    // C_OrderLine.Discount is not a standard column in all VIS deployments.
                    l.discount = (l.priceEntered > 0 && l.priceActual < l.priceEntered)
                        ? Math.Round((l.priceEntered - l.priceActual) / l.priceEntered * 100, 2)
                        : 0m;

                    l.lineNetAmt     = row["LineNetAmt"] != DBNull.Value
                        ? Convert.ToDecimal(row["LineNetAmt"]) : 0m;
                    l.productValue   = Util.GetValueOfString(row["ProductValue"]);
                    l.productName    = Util.GetValueOfString(row["ProductName"]);
                    l.uOMName        = Util.GetValueOfString(row["UOMName"]);
                    // Derive service flag from product type — 'S' = Service in Compiere/VIS product model
                    var rawProductType = Util.GetValueOfString(row["ProductType"]);
                    l.isService        = "S".Equals(rawProductType);
                    // Display name resolved from AD_Ref_List; empty for charge lines (no M_Product)
                    l.productTypeName  = Util.GetValueOfString(row["ProductTypeName"]);
                    result.Add(l);
                }
            }
            catch (Exception ex)
            {
                _log.SaveError("VAS_123_QuotationRightPanelModel.GetQuotationLines", ex.Message);
            }

            return result;
        }

        // ─────────────────────────────────────────────────────────────────────
        // §9b  Line change history (C_OrderLineHistory)
        // ─────────────────────────────────────────────────────────────────────

        /// <summary>
        /// Module Name : VAS_123
        /// Purpose     : Returns the change history rows for all lines of a Sales
        ///               Quotation from C_OrderLineHistory. The JS client groups
        ///               them by C_OrderLine_ID and renders a collapsible drawer
        ///               beneath each line.
        /// </summary>
        /// <param name="ctx">Current session context.</param>
        /// <param name="orderId">C_Order_ID of the Sales Quotation.</param>
        /// <returns>List of history rows ordered by line then newest-first.</returns>
        public List<dynamic> GetLineHistory(Ctx ctx, int orderId)
        {
            var list = new List<dynamic>();
            try
            {
                // Guard columns that may not exist in older schema versions.
                string promisedExpr  = ColumnExists("C_OrderLineHistory", "DatePromised")
                    ? "TO_CHAR(olh.DatePromised, 'YYYY-MM-DD')" : "NULL";
                string deliveredExpr = ColumnExists("C_OrderLineHistory", "QtyDelivered")
                    ? "NVL(olh.QtyDelivered, 0)" : "0";

                var sb = new StringBuilder();
                sb.Append("SELECT olh.C_OrderLine_ID AS C_OrderLine_ID,");
                sb.Append("       TO_CHAR(olh.Updated, 'YYYY-MM-DD HH24:MI') AS ChangedOn,");
                sb.Append("       olh.PriceActual AS PriceActual,");
                sb.Append("       NVL(olh.QtyEntered, olh.QtyOrdered) AS QtyEntered,");
                sb.Append("       olh.QtyOrdered AS QtyOrdered,");
                sb.Append("       olh.LineNetAmt AS LineNetAmt,");
                sb.Append("       " + deliveredExpr + " AS QtyDelivered,");
                sb.Append("       " + promisedExpr + " AS DatePromised,");
                sb.Append("       u.UOMSymbol AS UOMSymbol,");
                sb.Append("       NVL(u.StdPrecision, 0) AS UOMPrecision,");
                sb.Append("       cur.StdPrecision AS CurrencyPrecision");
                sb.Append("  FROM C_OrderLineHistory olh");
                sb.Append("  INNER JOIN C_Order o ON (o.C_Order_ID = olh.C_Order_ID)");
                sb.Append("  LEFT OUTER JOIN C_UOM u ON (u.C_UOM_ID = olh.C_UOM_ID)");
                sb.Append("  INNER JOIN C_Currency cur ON (cur.C_Currency_ID = o.C_Currency_ID)");
                sb.Append(" WHERE olh.C_Order_ID = @orderId");
                sb.Append(" ORDER BY olh.C_OrderLine_ID, olh.Updated DESC");

                // Access is implicitly scoped: caller already validated access to the
                // parent C_Order row. C_OrderLineHistory is a system audit table without
                // a direct AD_Table / MRole entry.
                DataSet ds = DB.ExecuteDataset(sb.ToString(),
                    new SqlParameter[] { new SqlParameter("@orderId", orderId) }, null);
                if (ds == null || ds.Tables.Count == 0) return list;

                foreach (DataRow row in ds.Tables[0].Rows)
                {
                    dynamic h        = new ExpandoObject();
                    h.c_OrderLine_ID = Util.GetValueOfInt(row["C_OrderLine_ID"]);
                    h.changedOn      = Util.GetValueOfString(row["ChangedOn"]);
                    h.priceActual    = row["PriceActual"]  != DBNull.Value ? Convert.ToDecimal(row["PriceActual"])  : 0m;
                    h.qtyEntered     = row["QtyEntered"]   != DBNull.Value ? Convert.ToDecimal(row["QtyEntered"])   : 0m;
                    h.qtyOrdered     = row["QtyOrdered"]   != DBNull.Value ? Convert.ToDecimal(row["QtyOrdered"])   : 0m;
                    h.lineNetAmt     = row["LineNetAmt"]   != DBNull.Value ? Convert.ToDecimal(row["LineNetAmt"])   : 0m;
                    h.qtyDelivered   = row["QtyDelivered"] != DBNull.Value ? Convert.ToDecimal(row["QtyDelivered"]) : 0m;
                    h.datePromised   = Util.GetValueOfString(row["DatePromised"]);
                    h.uOMSymbol      = Util.GetValueOfString(row["UOMSymbol"]);
                    h.uOMPrecision   = Util.GetValueOfInt(row["UOMPrecision"]);
                    h.currencyPrec   = Util.GetValueOfInt(row["CurrencyPrecision"]);
                    list.Add(h);
                }
            }
            catch (Exception ex)
            {
                _log.SaveError("VAS_123_QuotationRightPanelModel.GetLineHistory", ex.Message);
            }
            return list;
        }

        /// <summary>
        /// Returns true when the specified column exists on the named table in AD_Column.
        /// Used to guard queries against optional columns that may be absent in older schemas.
        /// </summary>
        /// <param name="tableName">Physical table name (case-insensitive).</param>
        /// <param name="columnName">Column name to check (case-insensitive).</param>
        /// <returns>True if the column exists; false on any error.</returns>
        private bool ColumnExists(string tableName, string columnName)
        {
            try
            {
                string sql = "SELECT COUNT(*) FROM AD_Column" +
                             " WHERE UPPER(ColumnName) = UPPER(@ColumnName)" +
                             "   AND AD_Table_ID = (SELECT AD_Table_ID FROM AD_Table" +
                             "                       WHERE UPPER(TableName) = UPPER(@TableName))";
                var param = new SqlParameter[]
                {
                    new SqlParameter("@ColumnName", columnName),
                    new SqlParameter("@TableName",  tableName)
                };
                return Util.GetValueOfInt(DB.ExecuteScalar(sql, param, null)) > 0;
            }
            catch (Exception ex)
            {
                _log.SaveError("ColumnExists (" + tableName + "." + columnName + ")", ex.Message);
                return false;
            }
        }

        // ─────────────────────────────────────────────────────────────────────
        // §10  Selected billing/shipping addresses (Part 2 — Section 2.4)
        // ─────────────────────────────────────────────────────────────────────

        /// <summary>
        /// Returns the exact billing and shipping locations selected on the quotation
        /// (C_Order.Bill_Location_ID for billing, C_Order.C_BPartner_Location_ID for shipping).
        /// </summary>
        /// <param name="ctx">Current session context.</param>
        /// <param name="orderId">C_Order_ID of the quotation.</param>
        /// <returns>Dynamic with formatted billing and shipping address fields, or null on error.</returns>
        public dynamic GetAddresses(Ctx ctx, int orderId)
        {
            // TotalOpenBalance is fetched here rather than in GetHeader so that a missing
            // column on C_BPartner in older VIS deployments only affects this secondary
            // call rather than crashing the main header load.
            string baseSql = @"SELECT o.C_Order_ID AS C_Order_ID,
                o.C_BPartner_Location_ID AS ShippingLocation_ID,
                o.Bill_Location_ID AS BillingLocation_ID,
                COALESCE(bp.TotalOpenBalance, 0) AS TotalOpenBalance,
                COALESCE(shipbpl.Name, N'') AS ShippingLocationName,
                COALESCE(ship.Address1, N'') AS ShippingAddress1,
                COALESCE(ship.Address2, N'') AS ShippingAddress2,
                COALESCE(ship.City, N'') AS ShippingCity,
                COALESCE(ship.Postal, N'') AS ShippingPostal,
                COALESCE(ship.RegionName, N'') AS ShippingRegion,
                COALESCE(billbpl.Name, N'') AS BillingLocationName,
                COALESCE(bill.Address1, N'') AS BillingAddress1,
                COALESCE(bill.Address2, N'') AS BillingAddress2,
                COALESCE(bill.City, N'') AS BillingCity,
                COALESCE(bill.Postal, N'') AS BillingPostal,
                COALESCE(bill.RegionName, N'') AS BillingRegion
                FROM C_Order o
                INNER JOIN C_BPartner bp ON (bp.C_BPartner_ID = o.C_BPartner_ID AND bp.IsActive = 'Y')
                LEFT OUTER JOIN C_BPartner_Location shipbpl ON (shipbpl.C_BPartner_Location_ID = o.C_BPartner_Location_ID AND shipbpl.IsActive = 'Y')
                LEFT OUTER JOIN C_Location ship ON (ship.C_Location_ID = shipbpl.C_Location_ID AND ship.IsActive = 'Y')
                LEFT OUTER JOIN C_BPartner_Location billbpl ON (billbpl.C_BPartner_Location_ID = o.Bill_Location_ID AND billbpl.IsActive = 'Y')
                LEFT OUTER JOIN C_Location bill ON (bill.C_Location_ID = billbpl.C_Location_ID AND bill.IsActive = 'Y')
                WHERE o.C_Order_ID = @orderId
                AND o.IsSalesQuotation = 'Y'
                AND o.IsActive = 'Y'";

            string accessSql = MRole.GetDefault(ctx).AddAccessSQL(
                baseSql, "o", MRole.SQL_FULLYQUALIFIED, MRole.SQL_RO);

            try
            {
                DataSet ds = DB.ExecuteDataset(accessSql,
                    new SqlParameter[] { new SqlParameter("@orderId", orderId) }, null);

                if (ds == null || ds.Tables.Count == 0 || ds.Tables[0].Rows.Count == 0)
                    return null;

                DataRow row = ds.Tables[0].Rows[0];
                dynamic addr = new ExpandoObject();

                addr.shippingLocation_ID   = Util.GetValueOfInt(row["ShippingLocation_ID"]);
                addr.billingLocation_ID    = Util.GetValueOfInt(row["BillingLocation_ID"]);
                // totalOpenBalance read here so GetHeader SQL stays lean and schema-safe
                addr.totalOpenBalance      = row["TotalOpenBalance"] != DBNull.Value
                    ? Convert.ToDecimal(row["TotalOpenBalance"]) : 0m;
                addr.shippingLocationName  = Util.GetValueOfString(row["ShippingLocationName"]);
                addr.shippingAddress1      = Util.GetValueOfString(row["ShippingAddress1"]);
                addr.shippingAddress2      = Util.GetValueOfString(row["ShippingAddress2"]);
                addr.shippingCity          = Util.GetValueOfString(row["ShippingCity"]);
                addr.shippingPostal        = Util.GetValueOfString(row["ShippingPostal"]);
                addr.shippingRegion        = Util.GetValueOfString(row["ShippingRegion"]);
                addr.billingLocationName   = Util.GetValueOfString(row["BillingLocationName"]);
                addr.billingAddress1       = Util.GetValueOfString(row["BillingAddress1"]);
                addr.billingAddress2       = Util.GetValueOfString(row["BillingAddress2"]);
                addr.billingCity           = Util.GetValueOfString(row["BillingCity"]);
                addr.billingPostal         = Util.GetValueOfString(row["BillingPostal"]);
                addr.billingRegion         = Util.GetValueOfString(row["BillingRegion"]);

                return addr;
            }
            catch (Exception ex)
            {
                _log.SaveError("VAS_123_QuotationRightPanelModel.GetAddresses", ex.Message);
                return null;
            }
        }

        // ─────────────────────────────────────────────────────────────────────
        // §10  Pricing & Terms (Part 2 — Section 2.5)
        // ─────────────────────────────────────────────────────────────────────

        /// <summary>
        /// Returns pricing and payment-terms data for the selected quotation.
        /// Kept separate from GetHeader so that a missing column in an older schema
        /// (e.g. PaymentRule, PriorityRule) only breaks the terms section, not the
        /// entire panel.
        /// </summary>
        /// <param name="ctx">Current session context.</param>
        /// <param name="orderId">C_Order_ID of the quotation.</param>
        /// <returns>Dynamic with pricing/terms fields, or null on error.</returns>
        public dynamic GetPricingTerms(Ctx ctx, int orderId)
        {
            // PriorityRule is intentionally excluded from the main query — it does not
            // exist in all schema deployments. It is fetched separately below so that
            // a missing column only silences the Priority field, not the entire section.
            try
            {
                var sb = new StringBuilder();
                sb.Append("SELECT o.C_Order_ID AS C_Order_ID,");
                sb.Append("       o.PaymentRule AS PaymentRule,");
                sb.Append("       o.M_PriceList_ID AS M_PriceList_ID,");
                sb.Append("       pl.Name AS PriceListName,");
                sb.Append("       o.C_PaymentTerm_ID AS C_PaymentTerm_ID,");
                sb.Append("       pt.Name AS PaymentTermName,");
                sb.Append("       o.DateOrdered AS DateOrdered,");
                sb.Append("       o.OrderValidTo AS OrderValidTo,");
                sb.Append("       o.C_Currency_ID AS C_Currency_ID,");
                sb.Append("       cur.ISO_Code AS CurrencyISO");
                sb.Append("  FROM C_Order o");
                sb.Append("  LEFT OUTER JOIN M_PriceList pl ON (pl.M_PriceList_ID = o.M_PriceList_ID AND pl.IsActive = 'Y')");
                sb.Append("  LEFT OUTER JOIN C_PaymentTerm pt ON (pt.C_PaymentTerm_ID = o.C_PaymentTerm_ID AND pt.IsActive = 'Y')");
                sb.Append("  LEFT OUTER JOIN C_Currency cur ON (cur.C_Currency_ID = o.C_Currency_ID)");
                sb.Append(" WHERE o.C_Order_ID = @orderId");
                sb.Append("   AND o.IsActive = 'Y'");
                sb.Append("   AND o.IsSOTrx = 'Y'");
                sb.Append("   AND o.IsSalesQuotation = 'Y'");

                string accessSql = MRole.GetDefault(ctx).AddAccessSQL(
                    sb.ToString(), "o", MRole.SQL_FULLYQUALIFIED, MRole.SQL_RO);

                var sqlParams = new SqlParameter[] { new SqlParameter("@orderId", orderId) };
                DataSet ds = DB.ExecuteDataset(accessSql, sqlParams, null);

                if (ds == null || ds.Tables.Count == 0 || ds.Tables[0].Rows.Count == 0)
                    return null;

                DataRow row = ds.Tables[0].Rows[0];

                dynamic result           = new ExpandoObject();
                result.paymentRule       = Util.GetValueOfString(row["PaymentRule"]);
                result.priorityRule      = string.Empty; // populated by the defensive block below
                result.m_PriceList_ID    = Util.GetValueOfInt(row["M_PriceList_ID"]);
                result.priceListName     = Util.GetValueOfString(row["PriceListName"]);
                result.c_PaymentTerm_ID  = Util.GetValueOfInt(row["C_PaymentTerm_ID"]);
                result.paymentTermName   = Util.GetValueOfString(row["PaymentTermName"]);
                result.dateOrdered       = row["DateOrdered"] != DBNull.Value
                    ? Convert.ToDateTime(row["DateOrdered"]) : (DateTime?)null;
                result.orderValidTo      = row["OrderValidTo"] != DBNull.Value
                    ? Convert.ToDateTime(row["OrderValidTo"]) : (DateTime?)null;
                result.currencyISO       = Util.GetValueOfString(row["CurrencyISO"]);
                result.currencyCode      = result.currencyISO;

                // PriorityRule — column may not exist in older schema deployments.
                // Failure here must not null-out the already-populated result.
                try
                {
                    string priSql = "SELECT o.PriorityRule FROM C_Order o WHERE o.C_Order_ID = @orderId";
                    object priObj = DB.ExecuteScalar(priSql, new SqlParameter[] { new SqlParameter("@orderId", orderId) }, null);
                    result.priorityRule = (priObj != null && priObj != DBNull.Value) ? Util.GetValueOfString(priObj) : string.Empty;
                }
                catch
                {
                    // PriorityRule column absent — leave as empty string, Priority cell renders as —
                }

                // Decode PaymentRule and PriorityRule codes to human-readable labels.
                // Wrapped separately so a missing reference list does not discard the rest of the pricing data.
                try
                {
                    var payRuleLabels = GetRefListNames(ctx, "PaymentRule",  "C_Order");
                    string payRule    = Util.GetValueOfString(result.paymentRule);
                    result.paymentRuleLabel = (payRuleLabels.ContainsKey(payRule) && !string.IsNullOrEmpty(payRule))
                        ? payRuleLabels[payRule] : payRule;
                }
                catch (Exception ex)
                {
                    _log.SaveError("VAS_123_QuotationRightPanelModel.GetPricingTerms.PayRuleLabel", ex.Message);
                    result.paymentRuleLabel = result.paymentRule;
                }

                try
                {
                    var priRuleLabels = GetRefListNames(ctx, "PriorityRule", "C_Order");
                    string priRule    = Util.GetValueOfString(result.priorityRule);
                    result.priorityRuleLabel = (priRuleLabels.ContainsKey(priRule) && !string.IsNullOrEmpty(priRule))
                        ? priRuleLabels[priRule] : priRule;
                }
                catch (Exception ex)
                {
                    _log.SaveError("VAS_123_QuotationRightPanelModel.GetPricingTerms.PriRuleLabel", ex.Message);
                    result.priorityRuleLabel = result.priorityRule;
                }

                return result;
            }
            catch (Exception ex)
            {
                _log.SaveError("VAS_123_QuotationRightPanelModel.GetPricingTerms", ex.Message);
                return null;
            }
        }

        // ─────────────────────────────────────────────────────────────────────
        // §11  Open opportunities for customer (Part 2 — Modal D2)
        // ─────────────────────────────────────────────────────────────────────

        /// <summary>
        /// Returns open opportunities available for linking to the quotation.
        /// The Opportunity display table is not yet confirmed in the schema mapping;
        /// this method returns an empty list until the table is verified and added.
        /// </summary>
        /// <param name="ctx">Current session context.</param>
        /// <param name="bPartnerId">C_BPartner_ID of the quotation's customer.</param>
        /// <returns>Empty list — placeholder until Opportunity table mapping is confirmed.</returns>
        public List<dynamic> GetOpenOpportunities(Ctx ctx, int bPartnerId)
        {
            var result = new List<dynamic>();
            try
            {
                var sb = new StringBuilder();
                sb.Append("SELECT vo.VAS_Opportunity_ID AS VAS_Opportunity_ID,");
                sb.Append("       vo.Name AS OpportunityName,");
                sb.Append("       vo.VAS_OppStage AS Stage,");
                sb.Append("       vo.PlannedAmt AS Amount");
                sb.Append("  FROM VAS_Opportunity vo");
                sb.Append(" WHERE vo.IsActive = 'Y'");
                sb.Append("   AND (vo.C_BPartner_ID = @bpId OR vo.Ref_BPartner_ID = @bpId2)");

                string accessSql = MRole.GetDefault(ctx).AddAccessSQL(
                    sb.ToString(), "vo", MRole.SQL_FULLYQUALIFIED, MRole.SQL_RO);
                accessSql += " ORDER BY vo.Name ASC";

                DataSet ds = DB.ExecuteDataset(accessSql,
                    new SqlParameter[]
                    {
                        new SqlParameter("@bpId",  bPartnerId),
                        new SqlParameter("@bpId2", bPartnerId)
                    }, null);

                if (ds == null || ds.Tables.Count == 0) return result;

                // Resolve all stage names in a single extra query via existing helper
                var stageMap = GetRefListNames(ctx, "VAS_OppStage", "VAS_Opportunity");

                foreach (DataRow row in ds.Tables[0].Rows)
                {
                    string stageCode = Util.GetValueOfString(row["Stage"]);
                    string stageName;
                    stageMap.TryGetValue(stageCode, out stageName);
                    if (string.IsNullOrEmpty(stageName)) stageName = stageCode;

                    dynamic o = new ExpandoObject();
                    o.vAS_Opportunity_ID = Util.GetValueOfInt(row["VAS_Opportunity_ID"]);
                    o.opportunityName    = Util.GetValueOfString(row["OpportunityName"]);
                    o.stage              = stageCode;
                    o.stageName          = stageName;
                    o.amount             = row["Amount"] != DBNull.Value
                        ? (object)Convert.ToDecimal(row["Amount"]) : null;
                    result.Add(o);
                }
            }
            catch (Exception ex)
            {
                _log.SaveError("VAS_123_QuotationRightPanelModel.GetOpenOpportunities", ex.Message);
            }
            return result;
        }

        // ─────────────────────────────────────────────────────────────────────
        // §25  Quotations linked to a given opportunity
        // ─────────────────────────────────────────────────────────────────────

        /// <summary>
        /// Returns all active Sales Quotations that share the given VAS_Opportunity_ID,
        /// ordered by date (newest first). Used by the "linked quotations" modal.
        /// </summary>
        /// <param name="ctx">Current session context.</param>
        /// <param name="opportunityId">VAS_Opportunity_ID to filter by.</param>
        /// <returns>List of dynamic quotation summary objects.</returns>
        public List<dynamic> GetLinkedQuotations(Ctx ctx, int opportunityId)
        {
            var result = new List<dynamic>();
            try
            {
                var sb = new StringBuilder();
                sb.Append("SELECT o.C_Order_ID AS C_Order_ID,");
                sb.Append("       o.DocumentNo AS DocumentNo,");
                sb.Append("       o.GrandTotal AS GrandTotal,");
                sb.Append("       o.DocStatus AS DocStatus,");
                sb.Append("       bp.Name AS BPartnerName,");
                sb.Append("       cur.ISO_Code AS CurrencyISO,");
                sb.Append("       cur.CurSymbol AS CurrencySymbol,");
                sb.Append("       cur.StdPrecision AS CurrencyPrecision,");
                sb.Append("       TO_CHAR(o.DateOrdered, 'YYYY-MM-DD') AS DateOrdered");
                sb.Append("  FROM C_Order o");
                sb.Append("  LEFT OUTER JOIN C_BPartner bp ON (bp.C_BPartner_ID = o.C_BPartner_ID AND bp.IsActive = 'Y')");
                sb.Append("  LEFT OUTER JOIN C_Currency cur ON (cur.C_Currency_ID = o.C_Currency_ID)");
                sb.Append(" WHERE o.VAS_Opportunity_ID = @opportunityId");
                sb.Append("   AND o.IsSalesQuotation = 'Y'");
                sb.Append("   AND o.IsActive = 'Y'");

                string accessSql = MRole.GetDefault(ctx).AddAccessSQL(
                    sb.ToString(), "o", MRole.SQL_FULLYQUALIFIED, MRole.SQL_RO);
                accessSql += " ORDER BY o.DateOrdered DESC";

                DataSet ds = DB.ExecuteDataset(accessSql,
                    new SqlParameter[] { new SqlParameter("@opportunityId", opportunityId) }, null);

                if (ds == null || ds.Tables.Count == 0) return result;

                // Resolve DocStatus display names in one extra query
                var statusMap = GetRefListNames(ctx, "DocStatus", "C_Order");

                foreach (DataRow row in ds.Tables[0].Rows)
                {
                    string docStatus = Util.GetValueOfString(row["DocStatus"]);
                    string docStatusName;
                    statusMap.TryGetValue(docStatus, out docStatusName);
                    if (string.IsNullOrEmpty(docStatusName)) docStatusName = docStatus;

                    dynamic q = new ExpandoObject();
                    q.c_Order_ID        = Util.GetValueOfInt(row["C_Order_ID"]);
                    q.documentNo        = Util.GetValueOfString(row["DocumentNo"]);
                    q.docStatus         = docStatus;
                    q.docStatusName     = docStatusName;
                    q.bPartnerName      = Util.GetValueOfString(row["BPartnerName"]);
                    q.currencyISO       = Util.GetValueOfString(row["CurrencyISO"]);
                    q.currencySymbol    = Util.GetValueOfString(row["CurrencySymbol"]);
                    q.currencyPrecision = row["CurrencyPrecision"] != DBNull.Value
                        ? Util.GetValueOfInt(row["CurrencyPrecision"]) : 2;
                    q.grandTotal        = row["GrandTotal"] != DBNull.Value
                        ? Convert.ToDecimal(row["GrandTotal"]) : 0m;
                    q.dateOrdered       = Util.GetValueOfString(row["DateOrdered"]);
                    result.Add(q);
                }
            }
            catch (Exception ex)
            {
                _log.SaveError("VAS_123_QuotationRightPanelModel.GetLinkedQuotations", ex.Message);
            }
            return result;
        }

        // ─────────────────────────────────────────────────────────────────────
        // §12  Link opportunity to quotation (Part 2 — Modal D2)
        // ─────────────────────────────────────────────────────────────────────

        /// <summary>
        /// Writes the given VAS_Opportunity_ID on to the quotation record using the
        /// platform's MOrder model class, which handles optimistic locking and audit columns.
        /// </summary>
        /// <param name="ctx">Current session context.</param>
        /// <param name="orderId">C_Order_ID of the quotation to update.</param>
        /// <param name="opportunityId">VAS_Opportunity_ID to link.</param>
        /// <returns>Dynamic with success flag and error message.</returns>
        public dynamic LinkOpportunity(Ctx ctx, int orderId, int opportunityId)
        {
            dynamic response = new ExpandoObject();
            response.success = false;
            response.error   = null;

            try
            {
                MOrder order = new MOrder(ctx, orderId, null);
                if (order.Get_ID() == 0)
                {
                    response.error = "not_found";
                    return response;
                }

                // Verify the record is indeed a quotation before modifying it
                if (!"Y".Equals(Util.GetValueOfString(order.Get_Value("IsSalesQuotation"))))
                {
                    response.error = "not_a_quotation";
                    return response;
                }

                order.Set_Value("VAS_Opportunity_ID", opportunityId);
                if (order.Save())
                {
                    response.success = true;
                }
                else
                {
                    response.error = "save_failed";
                }
            }
            catch (Exception ex)
            {
                _log.SaveError("VAS_123_QuotationRightPanelModel.LinkOpportunity", ex.Message);
                response.error = ex.Message;
            }

            return response;
        }

        // ─────────────────────────────────────────────────────────────────────
        // §14  Tasks (Part 3 — Section 3.2)
        // ─────────────────────────────────────────────────────────────────────

        /// <summary>
        /// Returns tasks linked to the quotation from AppointmentsInfo (the table where
        /// VIS.AppointmentsForm.init saves tasks), filtered by AD_Table_ID + Record_ID.
        /// Returns an empty list if an error occurs so only the Tasks section is affected.
        /// </summary>
        /// <param name="ctx">Current session context.</param>
        /// <param name="orderId">C_Order_ID (= Record_ID in AppointmentsInfo).</param>
        /// <param name="tableId">AD_Table_ID for C_Order.</param>
        /// <returns>List of task dynamic objects ordered by closed status, priority, due date.</returns>
        public List<dynamic> GetTasks(Ctx ctx, int orderId, int tableId)
        {
            var list = new List<dynamic>();

            var sb = new StringBuilder();
            sb.Append("SELECT a.AppointmentsInfo_ID AS AppointmentsInfo_ID,");
            sb.Append("       a.Subject AS Subject,");
            sb.Append("       TO_CHAR(a.EndDate, 'YYYY-MM-DD') AS DueDate,");
            sb.Append("       a.PriorityKey AS PriorityKey,");
            sb.Append("       a.TaskStatus AS TaskStatus,");
            sb.Append("       COALESCE(SUBSTR(a.IsClosed, 1, 1), 'N') AS IsClosed,");
            sb.Append("       u.Name AS AssigneeName");
            sb.Append("  FROM AppointmentsInfo a");
            sb.Append("  LEFT OUTER JOIN AD_User u ON (u.AD_User_ID = a.AD_User_ID AND u.IsActive = 'Y')");
            sb.Append(" WHERE a.IsActive = 'Y'");
            sb.Append("   AND COALESCE(a.IsDeleted, 'N') = 'N'");
            sb.Append("   AND COALESCE(a.IsTask, 'N') = 'Y'");
            sb.Append("   AND a.AD_Table_ID = @tableId");
            sb.Append("   AND a.Record_ID = @orderId");

            string accessSql = MRole.GetDefault(ctx).AddAccessSQL(
                sb.ToString(), "a", MRole.SQL_FULLYQUALIFIED, MRole.SQL_RO);
            accessSql += " ORDER BY IsClosed ASC, PriorityKey ASC, DueDate ASC";

            var sqlParams = new SqlParameter[]
            {
                new SqlParameter("@tableId", tableId),
                new SqlParameter("@orderId", orderId)
            };

            try
            {
                DataSet ds = DB.ExecuteDataset(accessSql, sqlParams, null);
                if (ds == null || ds.Tables.Count == 0) return list;

                foreach (DataRow row in ds.Tables[0].Rows)
                {
                    dynamic t = new ExpandoObject();

                    // r_Request_ID is the field name the JS client reads for data-task-id;
                    // it now holds AppointmentsInfo_ID (the correct PK for this entity).
                    t.r_Request_ID = Util.GetValueOfInt(row["AppointmentsInfo_ID"]);
                    t.title        = Util.GetValueOfString(row["Subject"]);
                    t.due          = Util.GetValueOfString(row["DueDate"]);   // "YYYY-MM-DD" string
                    t.assigneeName = Util.GetValueOfString(row["AssigneeName"]);

                    // Raw platform priority code (U/3/5/7); client translates via AD_Message
                    t.priority = Util.GetValueOfString(row["PriorityKey"]);

                    // TaskStatus is the numeric completion percentage (0-100)
                    t.pct    = Util.GetValueOfString(row["TaskStatus"]);
                    t.closed = "Y".Equals(Util.GetValueOfString(row["IsClosed"]));

                    list.Add(t);
                }
            }
            catch (Exception ex)
            {
                _log.SaveError("VAS_123_QuotationRightPanelModel.GetTasks", ex.Message);
            }

            return list;
        }

        // ─────────────────────────────────────────────────────────────────────
        // §15  Engagement (Part 3 — Section 3.3)
        // ─────────────────────────────────────────────────────────────────────

        /// <summary>
        /// Returns all engagement touches linked to the quotation, combining:
        ///   • AppointmentsInfo rows (touchType = "MEETING")
        ///   • CM_ChatEntry rows via CM_Chat (touchType = "NOTE")
        ///   • MailAttachment1 rows (touchType = "EMAIL")
        ///   • VA048_CallDetails rows if VA048 module installed (touchType = "CALL")
        ///   • WSP_SMChatTopic rows if WSP module installed (touchType = "CHAT")
        /// Returns a structured response with counts and a combined sorted timeline.
        /// Each channel is fetched in its own try/catch so a missing table does not
        /// prevent other channels from loading.
        /// </summary>
        /// <param name="ctx">Current session context.</param>
        /// <param name="orderId">C_Order_ID of the quotation (= Record_ID).</param>
        /// <param name="tableId">AD_Table_ID for C_Order.</param>
        /// <returns>Dynamic object with counts and items list, sorted newest first.</returns>
        public dynamic GetEngagement(Ctx ctx, int orderId, int tableId)
        {
            dynamic response = new ExpandoObject();
            var allItems = new List<dynamic>();

            int countMeetings = 0, countNotes = 0, countEmails = 0, countCalls = 0, countChat = 0;
            int totalMeetingMins = 0, totalCallMins = 0;
            var allAttendeeIds = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            // ── Step 1: MEETINGS ─────────────────────────────────────────────
            if (tableId > 0)
            {
                try
                {
                    var sbMt = new StringBuilder();
                    sbMt.Append("SELECT MIN(a.AppointmentsInfo_ID) AS MeetingId,");
                    sbMt.Append("       TO_CHAR(MIN(a.StartDate),'YYYY-MM-DD HH24:MI') AS when_ts,");
                    sbMt.Append("       TO_CHAR(MIN(a.EndDate),'YYYY-MM-DD HH24:MI') AS end_date,");
                    sbMt.Append("       COALESCE(MIN(a.Subject), N'') AS title,");
                    sbMt.Append("       COALESCE(MIN(a.Location), N'') AS location,");
                    sbMt.Append("       COALESCE(MIN(SUBSTR(a.Comments, 1, 200)), N'') AS preview,");
                    sbMt.Append("       CASE WHEN MIN(atr.AppointmentsInfo_ID) IS NOT NULL THEN 'Y' ELSE 'N' END AS has_transcript,");
                    sbMt.Append("       MIN(u.Name) AS who");
                    sbMt.Append("  FROM AppointmentsInfo a");
                    sbMt.Append("  LEFT OUTER JOIN AD_User u ON (u.AD_User_ID = a.CreatedBy AND u.IsActive = 'Y')");
                    sbMt.Append("  LEFT OUTER JOIN AppointmentTranscript atr ON (atr.AppointmentsInfo_ID = a.AppointmentsInfo_ID)");
                    sbMt.Append(" WHERE a.IsActive = 'Y'");
                    sbMt.Append("   AND COALESCE(a.IsTask, 'N') = 'N'");
                    sbMt.Append("   AND a.AD_Table_ID = " + tableId);
                    sbMt.Append("   AND a.Record_ID = @orderId");

                    string mtAccessSql = MRole.GetDefault(ctx).AddAccessSQL(
                        sbMt.ToString(), "a", MRole.SQL_FULLYQUALIFIED, MRole.SQL_RO);
                    mtAccessSql += " GROUP BY a.StartDate, a.Subject";
                    mtAccessSql += " ORDER BY MIN(a.StartDate) DESC";

                    DataSet mtDs = DB.ExecuteDataset(mtAccessSql,
                        new SqlParameter[] { new SqlParameter("@orderId", orderId) }, null);
                    if (mtDs != null && mtDs.Tables.Count > 0)
                    {
                        foreach (DataRow row in mtDs.Tables[0].Rows)
                        {
                            dynamic item      = new ExpandoObject();
                            item.touchType    = "MEETING";
                            item.meetingId    = Util.GetValueOfInt(row["MeetingId"]);
                            item.whenTs       = Util.GetValueOfString(row["when_ts"]);
                            item.title        = Util.GetValueOfString(row["title"]);
                            item.location     = Util.GetValueOfString(row["location"]);
                            item.preview      = Util.GetValueOfString(row["preview"]);
                            item.hasTranscript = Util.GetValueOfString(row["has_transcript"]) == "Y";
                            item.who          = Util.GetValueOfString(row["who"]);
                            item.direction    = "";
                            item.durationMins = 0;
                            try
                            {
                                var startStr = Util.GetValueOfString(row["when_ts"]);
                                var endStr   = Util.GetValueOfString(row["end_date"]);
                                if (!string.IsNullOrEmpty(startStr) && !string.IsNullOrEmpty(endStr))
                                {
                                    DateTime dtS = DateTime.ParseExact(startStr, "yyyy-MM-dd HH:mm", null);
                                    DateTime dtE = DateTime.ParseExact(endStr,   "yyyy-MM-dd HH:mm", null);
                                    item.durationMins = (int)(dtE - dtS).TotalMinutes;
                                    if (item.durationMins > 0) totalMeetingMins += item.durationMins;
                                }
                            }
                            catch { /* duration calc failed — leave 0 */ }
                            allItems.Add(item);
                            countMeetings++;
                        }
                    }
                }
                catch (Exception ex)
                {
                    _log.SaveError("VAS_123_QuotationRightPanelModel.GetEngagement.Meetings", ex.Message);
                }

                // Collect unique attendee IDs for the stat card subtitle
                if (countMeetings > 0)
                {
                    try
                    {
                        var sbAtt = new StringBuilder();
                        sbAtt.Append("SELECT COALESCE(SUBSTR(a.AttendeeInfo, 1, 4000), N'') AS attendee_info");
                        sbAtt.Append("  FROM AppointmentsInfo a");
                        sbAtt.Append(" WHERE a.IsActive = 'Y'");
                        sbAtt.Append("   AND COALESCE(a.IsTask, 'N') = 'N'");
                        sbAtt.Append("   AND a.AD_Table_ID = " + tableId);
                        sbAtt.Append("   AND a.Record_ID = @orderId");
                        string attAccessSql = MRole.GetDefault(ctx).AddAccessSQL(
                            sbAtt.ToString(), "a", MRole.SQL_FULLYQUALIFIED, MRole.SQL_RO);
                        DataSet attDs = DB.ExecuteDataset(attAccessSql,
                            new SqlParameter[] { new SqlParameter("@orderId", orderId) }, null);
                        if (attDs != null && attDs.Tables.Count > 0)
                        {
                            foreach (DataRow ar in attDs.Tables[0].Rows)
                            {
                                var raw = Util.GetValueOfString(ar["attendee_info"]);
                                if (!string.IsNullOrEmpty(raw))
                                {
                                    foreach (var part in raw.Split(','))
                                    {
                                        var t = part.Trim();
                                        if (!string.IsNullOrEmpty(t)) allAttendeeIds.Add(t);
                                    }
                                }
                            }
                        }
                    }
                    catch { /* AttendeeInfo read failed — attendee count stays 0 */ }
                }
            }

            // ── Step 2: NOTES (CM_Chat + CM_ChatEntry) ──────────────────────
            if (tableId > 0)
            {
                try
                {
                    var sbNt = new StringBuilder();
                    sbNt.Append("SELECT e.CM_ChatEntry_ID AS note_id,");
                    sbNt.Append("       TO_CHAR(e.Created,'YYYY-MM-DD HH24:MI') AS when_ts,");
                    sbNt.Append("       COALESCE(e.Subject, N'') AS title,");
                    sbNt.Append("       SUBSTR(e.CharacterData, 1, 400) AS preview,");
                    sbNt.Append("       au.Name AS who");
                    sbNt.Append("  FROM CM_ChatEntry e");
                    sbNt.Append("  INNER JOIN CM_Chat c ON (c.CM_Chat_ID = e.CM_Chat_ID AND c.IsActive = 'Y'");
                    sbNt.Append("    AND c.AD_Table_ID = " + tableId);
                    sbNt.Append("    AND c.Record_ID = @orderId)");
                    sbNt.Append("  LEFT OUTER JOIN AD_User au ON (au.AD_User_ID = COALESCE(e.AD_User_ID, e.CreatedBy) AND au.IsActive = 'Y')");
                    sbNt.Append(" WHERE e.IsActive = 'Y'");

                    string ntAccessSql = MRole.GetDefault(ctx).AddAccessSQL(
                        sbNt.ToString(), "e", MRole.SQL_FULLYQUALIFIED, MRole.SQL_RO);
                    ntAccessSql += " ORDER BY e.Created DESC";

                    DataSet ntDs = DB.ExecuteDataset(ntAccessSql,
                        new SqlParameter[] { new SqlParameter("@orderId", orderId) }, null);
                    if (ntDs != null && ntDs.Tables.Count > 0)
                    {
                        foreach (DataRow row in ntDs.Tables[0].Rows)
                        {
                            dynamic item   = new ExpandoObject();
                            item.touchType = "NOTE";
                            item.noteId    = Util.GetValueOfInt(row["note_id"]);
                            item.whenTs    = Util.GetValueOfString(row["when_ts"]);
                            item.title     = Util.GetValueOfString(row["title"]);
                            item.preview   = row["preview"] == DBNull.Value ? "" : Util.GetValueOfString(row["preview"]);
                            item.who       = Util.GetValueOfString(row["who"]);
                            item.direction = "";
                            allItems.Add(item);
                            countNotes++;
                        }
                    }
                }
                catch (Exception ex) { _log.SaveError("VAS_123_QuotationRightPanelModel.GetEngagement.Notes", ex.Message); }
            }

            // ── Step 3: EMAILS ───────────────────────────────────────────────
            if (tableId > 0)
            {
                try
                {
                    var sbEm = new StringBuilder();
                    sbEm.Append("SELECT ma.MailAttachment1_ID AS email_id,");
                    sbEm.Append("       CASE WHEN ma.AttachmentType = 'I'");
                    sbEm.Append("            THEN TO_CHAR(ma.DateMailReceived,'YYYY-MM-DD HH24:MI')");
                    sbEm.Append("            ELSE TO_CHAR(ma.Created,'YYYY-MM-DD HH24:MI') END AS when_ts,");
                    sbEm.Append("       COALESCE(ma.Title, N'') AS title,");
                    sbEm.Append("       N'' AS preview,");
                    sbEm.Append("       u.Name AS who,");
                    sbEm.Append("       CASE WHEN ma.AttachmentType = 'I' THEN 'in' ELSE 'out' END AS direction");
                    sbEm.Append("  FROM MailAttachment1 ma");
                    sbEm.Append("  LEFT OUTER JOIN AD_User u ON (u.AD_User_ID = ma.CreatedBy)");
                    sbEm.Append(" WHERE ma.IsActive = 'Y'");
                    sbEm.Append("   AND ma.AD_Table_ID = " + tableId);
                    sbEm.Append("   AND ma.Record_ID = @orderId");
                    sbEm.Append("   AND ma.AttachmentType IN ('M', 'I')");

                    string emAccessSql = MRole.GetDefault(ctx).AddAccessSQL(
                        sbEm.ToString(), "ma", MRole.SQL_FULLYQUALIFIED, MRole.SQL_RO);

                    DataSet emDs = DB.ExecuteDataset(emAccessSql,
                        new SqlParameter[] { new SqlParameter("@orderId", orderId) }, null);
                    if (emDs != null && emDs.Tables.Count > 0)
                    {
                        foreach (DataRow row in emDs.Tables[0].Rows)
                        {
                            dynamic item   = new ExpandoObject();
                            item.touchType = "EMAIL";
                            item.emailId   = Util.GetValueOfInt(row["email_id"]);
                            item.whenTs    = Util.GetValueOfString(row["when_ts"]);
                            item.title     = Util.GetValueOfString(row["title"]);
                            item.preview   = "";
                            item.who       = Util.GetValueOfString(row["who"]);
                            item.direction = Util.GetValueOfString(row["direction"]);
                            allItems.Add(item);
                            countEmails++;
                        }
                    }
                }
                catch (Exception ex)
                {
                    _log.SaveError("VAS_123_QuotationRightPanelModel.GetEngagement.Emails", ex.Message);
                }
            }

            // ── Step 4: CALLS (VA048, only when installed) ───────────────────
            if (tableId > 0 && Env.IsModuleInstalled("VA048_"))
            {
                try
                {
                    var sbCl = new StringBuilder();
                    sbCl.Append("SELECT TO_CHAR(cd.Created,'YYYY-MM-DD HH24:MI') AS when_ts,");
                    sbCl.Append("       COALESCE(SUBSTR(cd.VA048_CallNotes, 1, 400), cd.VA048_To, N'') AS title,");
                    sbCl.Append("       N'' AS preview,");
                    sbCl.Append("       u.Name AS who");
                    sbCl.Append("  FROM VA048_CallDetails cd");
                    sbCl.Append("  LEFT OUTER JOIN AD_User u ON (u.AD_User_ID = cd.CreatedBy)");
                    sbCl.Append(" WHERE cd.IsActive = 'Y'");
                    sbCl.Append("   AND cd.Record_ID = @orderId");
                    sbCl.Append("   AND cd.AD_Table_ID = " + tableId);

                    string clAccessSql = MRole.GetDefault(ctx).AddAccessSQL(
                        sbCl.ToString(), "cd", MRole.SQL_FULLYQUALIFIED, MRole.SQL_RO);

                    DataSet clDs = DB.ExecuteDataset(clAccessSql,
                        new SqlParameter[] { new SqlParameter("@orderId", orderId) }, null);
                    if (clDs != null && clDs.Tables.Count > 0)
                    {
                        foreach (DataRow row in clDs.Tables[0].Rows)
                        {
                            dynamic item   = new ExpandoObject();
                            item.touchType = "CALL";
                            item.whenTs    = Util.GetValueOfString(row["when_ts"]);
                            item.title     = Util.GetValueOfString(row["title"]);
                            item.preview   = "";
                            item.who       = Util.GetValueOfString(row["who"]);
                            item.direction = "";
                            allItems.Add(item);
                            countCalls++;
                        }
                    }
                }
                catch (Exception ex)
                {
                    _log.SaveError("VAS_123_QuotationRightPanelModel.GetEngagement.Calls", ex.Message);
                }

                if (countCalls > 0)
                {
                    try
                    {
                        var sbCallAgg = new StringBuilder();
                        sbCallAgg.Append("SELECT COALESCE(SUM(COALESCE(cd.VA048_CallDuration, 0)), 0) AS total_mins");
                        sbCallAgg.Append("  FROM VA048_CallDetails cd");
                        sbCallAgg.Append(" WHERE cd.IsActive = 'Y'");
                        sbCallAgg.Append("   AND cd.Record_ID = @orderId");
                        sbCallAgg.Append("   AND cd.AD_Table_ID = " + tableId);
                        string callAggSql = MRole.GetDefault(ctx).AddAccessSQL(
                            sbCallAgg.ToString(), "cd", MRole.SQL_FULLYQUALIFIED, MRole.SQL_RO);
                        object callDurObj = DB.ExecuteScalar(callAggSql,
                            new SqlParameter[] { new SqlParameter("@orderId", orderId) }, null);
                        if (callDurObj != null && callDurObj != DBNull.Value)
                            totalCallMins = Util.GetValueOfInt(callDurObj);
                    }
                    catch { /* VA048_CallDuration may not exist */ }
                }
            }

            // ── Step 5: WHATSAPP CHAT ─────────────────────────────────────────────
            if (tableId > 0 && Env.IsModuleInstalled("WSP_"))
            {
                try
                {
                    var sbCh = new StringBuilder();
                    sbCh.Append("SELECT ct.WSP_SMChatTopic_ID AS topic_id,");
                    sbCh.Append("       COALESCE(ci.Name, N'') AS contact_name,");
                    sbCh.Append("       TO_CHAR(ct.Created, 'YYYY-MM-DD HH24:MI') AS when_ts");
                    sbCh.Append("  FROM WSP_SMChatTopic ct");
                    sbCh.Append("  LEFT OUTER JOIN WSP_SMChatIdentifier ci");
                    sbCh.Append("       ON (ci.WSP_SMChatIdentifier_ID = ct.WSP_SMChatIdentifier_ID");
                    sbCh.Append("           AND ci.IsActive = 'Y')");
                    sbCh.Append(" WHERE ct.IsActive = 'Y'");
                    sbCh.Append("   AND ct.AD_Table_ID = " + tableId);
                    sbCh.Append("   AND ct.Record_ID = @orderId");

                    string chAccessSql = MRole.GetDefault(ctx).AddAccessSQL(
                        sbCh.ToString(), "ct", MRole.SQL_FULLYQUALIFIED, MRole.SQL_RO);
                    chAccessSql += " ORDER BY ct.Created DESC";

                    DataSet chDs = DB.ExecuteDataset(chAccessSql,
                        new SqlParameter[] { new SqlParameter("@orderId", orderId) }, null);
                    if (chDs != null && chDs.Tables.Count > 0 && chDs.Tables[0].Rows.Count > 0)
                    {
                        var chTopicRows = new List<DataRow>();
                        var chTopicIds  = new List<int>();
                        foreach (DataRow chRow in chDs.Tables[0].Rows)
                        {
                            int tid = Util.GetValueOfInt(chRow["topic_id"]);
                            if (tid > 0) { chTopicRows.Add(chRow); chTopicIds.Add(tid); }
                        }

                        var lastMsgMap = new Dictionary<int, string>();
                        var senderMap  = new Dictionary<int, string>();
                        if (chTopicIds.Count > 0)
                        {
                            string idIn = string.Join(",", chTopicIds);
                            var sbMsg = new StringBuilder();
                            sbMsg.Append("SELECT m.WSP_SMChatTopic_ID AS topic_id,");
                            sbMsg.Append("       COALESCE(m.WSP_TextMsg, TO_CLOB(N'')) AS last_msg,");
                            sbMsg.Append("       COALESCE(m.WSP_IsSender, 'N') AS is_sender");
                            sbMsg.Append("  FROM WSP_SMChatMessage m");
                            sbMsg.Append(" WHERE m.IsActive = 'Y'");
                            sbMsg.Append("   AND m.WSP_SMChatTopic_ID IN (" + idIn + ")");
                            sbMsg.Append("   AND m.Created = (SELECT MAX(m2.Created)");
                            sbMsg.Append("                      FROM WSP_SMChatMessage m2");
                            sbMsg.Append("                     WHERE m2.WSP_SMChatTopic_ID = m.WSP_SMChatTopic_ID");
                            sbMsg.Append("                       AND m2.IsActive = 'Y')");
                            DataSet msgDs = DB.ExecuteDataset(sbMsg.ToString(), null, null);
                            if (msgDs != null && msgDs.Tables.Count > 0)
                            {
                                foreach (DataRow mRow in msgDs.Tables[0].Rows)
                                {
                                    int tid = Util.GetValueOfInt(mRow["topic_id"]);
                                    if (tid > 0 && !lastMsgMap.ContainsKey(tid))
                                    {
                                        lastMsgMap[tid] = Util.GetValueOfString(mRow["last_msg"]);
                                        senderMap[tid]  = Util.GetValueOfString(mRow["is_sender"]);
                                    }
                                }
                            }
                        }

                        foreach (DataRow chRow in chTopicRows)
                        {
                            int    topicId  = Util.GetValueOfInt(chRow["topic_id"]);
                            string lastMsg  = lastMsgMap.ContainsKey(topicId) ? lastMsgMap[topicId] : "";
                            string isSender = senderMap.ContainsKey(topicId)  ? senderMap[topicId]  : "N";
                            dynamic chatItem   = new ExpandoObject();
                            chatItem.touchType = "CHAT";
                            chatItem.topicId   = topicId;
                            chatItem.whenTs    = Util.GetValueOfString(chRow["when_ts"]);
                            chatItem.title     = "";
                            chatItem.preview   = lastMsg;
                            chatItem.who       = Util.GetValueOfString(chRow["contact_name"]);
                            chatItem.direction = (isSender == "Y") ? "out" : "in";
                            allItems.Add(chatItem);
                            countChat++;
                        }
                    }
                }
                catch (Exception ex)
                {
                    _log.SaveError("VAS_123_QuotationRightPanelModel.GetEngagement.Chat", ex.Message);
                }
            }

            // ── Step 6: merge, sort newest-first ─────────────────────────────
            allItems.Sort((a, b) =>
            {
                string ta = (a as IDictionary<string, object>).ContainsKey("whenTs")
                    ? (string)((IDictionary<string, object>)a)["whenTs"] : "";
                string tb = (b as IDictionary<string, object>).ContainsKey("whenTs")
                    ? (string)((IDictionary<string, object>)b)["whenTs"] : "";
                return string.Compare(tb, ta, StringComparison.Ordinal);
            });

            // ── Step 7: build response ────────────────────────────────────────
            dynamic counts = new ExpandoObject();
            counts.total            = allItems.Count;
            counts.meetings         = countMeetings;
            counts.notes            = countNotes;
            counts.emails           = countEmails;
            counts.calls            = countCalls;
            counts.chat             = countChat;
            counts.totalMeetingMins = totalMeetingMins;
            counts.meetingAttendees = allAttendeeIds.Count;
            counts.totalCallMins    = totalCallMins;
            counts.connectedCalls   = countCalls;

            response.counts = counts;
            response.items  = allItems;

            return response;
        }

        // ─────────────────────────────────────────────────────────────────────
        // §16  Post note (Part 3 — Section 3.3 composer)
        // ─────────────────────────────────────────────────────────────────────

        /// <summary>
        /// Creates an internal note (AD_Note) linked to the quotation.
        /// Uses the platform's MNote / AD_Note model class when available;
        /// falls back to a direct SQL INSERT as an alternative path.
        /// </summary>
        /// <param name="ctx">Current session context.</param>
        /// <param name="orderId">C_Order_ID (= Record_ID in AD_Note).</param>
        /// <param name="tableId">AD_Table_ID for C_Order.</param>
        /// <param name="noteText">The note body text supplied by the user.</param>
        /// <returns>Dynamic with success flag and optional error message.</returns>
        public dynamic PostNote(Ctx ctx, int orderId, int tableId, string noteText)
        {
            dynamic response = new ExpandoObject();
            response.success = false;
            response.message = null;

            if (string.IsNullOrWhiteSpace(noteText))
            {
                response.message = "empty_note";
                return response;
            }

            try
            {
                int clientId = ctx.GetAD_Client_ID();
                int orgId    = ctx.GetAD_Org_ID();
                int userId   = ctx.GetAD_User_ID();

                // Step 1: Find existing CM_Chat for this C_Order record
                int chatId = 0;
                string findChatSql =
                    "SELECT CM_Chat_ID FROM CM_Chat " +
                    "WHERE AD_Table_ID = @tableId AND Record_ID = @recordId " +
                    "  AND AD_Client_ID = @clientId AND IsActive = 'Y'";
                var findParams = new SqlParameter[]
                {
                    new SqlParameter("@tableId",  tableId),
                    new SqlParameter("@recordId", orderId),
                    new SqlParameter("@clientId", clientId)
                };
                object existingId = DB.ExecuteScalar(findChatSql, findParams, null);
                if (existingId != null && existingId != DBNull.Value)
                    chatId = Util.GetValueOfInt(existingId);

                // Step 2: Create CM_Chat if none exists for this C_Order
                if (chatId <= 0)
                {
                    chatId = DB.GetNextID(ctx, "CM_Chat", null);
                    if (chatId <= 0)
                    {
                        response.message = "Could not obtain sequence ID for CM_Chat";
                        return response;
                    }

                    string createChatSql =
                        "INSERT INTO CM_Chat " +
                        "(CM_Chat_ID, AD_Client_ID, AD_Org_ID, IsActive, Created, CreatedBy, Updated, UpdatedBy, " +
                        " AD_Table_ID, Record_ID, Description, ConfidentialType, ModerationType) " +
                        "VALUES " +
                        "(@chatId, @clientId, @orgId, 'Y', CURRENT_TIMESTAMP, @createdBy, CURRENT_TIMESTAMP, @updatedBy, " +
                        " @tableId, @recordId, @description, 'A', 'A')";

                    var createChatParams = new SqlParameter[]
                    {
                        new SqlParameter("@chatId",      chatId),
                        new SqlParameter("@clientId",    clientId),
                        new SqlParameter("@orgId",       orgId),
                        new SqlParameter("@createdBy",   userId),
                        new SqlParameter("@updatedBy",   userId),
                        new SqlParameter("@tableId",     tableId),
                        new SqlParameter("@recordId",    orderId),
                        new SqlParameter("@description", "C_Order Notes")
                    };

                    int chatRows = DB.ExecuteQuery(createChatSql, createChatParams, null);
                    if (chatRows <= 0)
                    {
                        response.message = "Failed to create CM_Chat record";
                        return response;
                    }
                }

                // Step 3: Insert CM_ChatEntry linked to the chat
                int entryId = DB.GetNextID(ctx, "CM_ChatEntry", null);
                if (entryId <= 0)
                {
                    response.message = "Could not obtain sequence ID for CM_ChatEntry";
                    return response;
                }

                string insertEntrySql =
                    "INSERT INTO CM_ChatEntry " +
                    "(CM_ChatEntry_ID, AD_Client_ID, AD_Org_ID, IsActive, Created, CreatedBy, Updated, UpdatedBy, " +
                    " CM_Chat_ID, AD_User_ID, CharacterData, ConfidentialType, ChatEntryType) " +
                    "VALUES " +
                    "(@entryId, @clientId, @orgId, 'Y', CURRENT_TIMESTAMP, @createdBy, CURRENT_TIMESTAMP, @updatedBy, " +
                    " @chatId, @adUserId, @charData, 'A', 'N')";

                var entryParams = new SqlParameter[]
                {
                    new SqlParameter("@entryId",   entryId),
                    new SqlParameter("@clientId",  clientId),
                    new SqlParameter("@orgId",     orgId),
                    new SqlParameter("@createdBy", userId),
                    new SqlParameter("@updatedBy", userId),
                    new SqlParameter("@chatId",    chatId),
                    new SqlParameter("@adUserId",  userId),
                    new SqlParameter("@charData",  noteText)
                };

                int entryRows = DB.ExecuteQuery(insertEntrySql, entryParams, null);
                response.success = (entryRows == 1);
                if (!response.success)
                    response.message = "Insert into CM_ChatEntry returned 0 rows";
            }
            catch (Exception ex)
            {
                _log.SaveError("VAS_123_QuotationRightPanelModel.PostNote", ex.Message);
                response.message = ex.Message;
            }

            return response;
        }

        // ─────────────────────────────────────────────────────────────────────
        // §17  User suggest for assignee typeahead (Part 3 — Modal F1)
        // ─────────────────────────────────────────────────────────────────────

        /// <summary>
        /// Returns up to 10 AD_User rows whose Name or login name contains the query
        /// string, excluding the IDs listed in <paramref name="excludeIds"/>.
        /// Used by the assignee typeahead in the Task form modal (F1).
        /// </summary>
        /// <param name="ctx">Current session context.</param>
        /// <param name="query">Partial name or email to search for.</param>
        /// <param name="excludeIds">Comma-separated AD_User_IDs to exclude (already selected).</param>
        /// <returns>List of up to 10 user suggestion objects.</returns>
        public List<dynamic> GetUserSuggest(Ctx ctx, string query, string excludeIds)
        {
            var list = new List<dynamic>();
            if (string.IsNullOrWhiteSpace(query)) return list;

            // Build a safe exclusion IN-list from the comma-separated integer IDs.
            // Non-numeric tokens are silently ignored; an empty list uses IN (0).
            string safeExclusion = "0";
            if (!string.IsNullOrWhiteSpace(excludeIds))
            {
                var ids = new List<string>();
                foreach (string token in excludeIds.Split(','))
                {
                    string t = token.Trim();
                    int parsed;
                    if (int.TryParse(t, out parsed) && parsed > 0) ids.Add(parsed.ToString());
                }
                if (ids.Count > 0) safeExclusion = string.Join(",", ids);
            }

            string baseSql = string.Format(@"SELECT u.AD_User_ID AS AD_User_ID,
                COALESCE(u.Name, N'') AS Name,
                COALESCE(u.EMail, N'') AS Email
                FROM AD_User u
                WHERE u.IsActive = 'Y'
                AND u.AD_Client_ID IN ({0})
                AND (u.Name LIKE @q OR u.EMail LIKE @q)
                AND u.AD_User_ID NOT IN ({1})",
                ctx.GetAD_Client_ID(), safeExclusion);

            string accessSql = MRole.GetDefault(ctx).AddAccessSQL(
                baseSql, "u", MRole.SQL_FULLYQUALIFIED, MRole.SQL_RO);

            // Limit to 10 results using DB-engine-specific syntax
            string finalSql = DB.IsOracle()
                ? "SELECT * FROM (" + accessSql + ") WHERE ROWNUM <= 10"
                : accessSql + " LIMIT 10";

            var sqlParams = new SqlParameter[]
            {
                new SqlParameter("@q", "%" + query.Replace("%", "\\%") + "%")
            };

            try
            {
                DataSet ds = DB.ExecuteDataset(finalSql, sqlParams, null);
                if (ds == null || ds.Tables.Count == 0) return list;

                foreach (DataRow row in ds.Tables[0].Rows)
                {
                    dynamic u = new ExpandoObject();
                    u.aD_User_ID = Util.GetValueOfInt(row["AD_User_ID"]);
                    u.name       = Util.GetValueOfString(row["Name"]);
                    u.email      = Util.GetValueOfString(row["Email"]);
                    list.Add(u);
                }
            }
            catch (Exception ex)
            {
                _log.SaveError("VAS_123_QuotationRightPanelModel.GetUserSuggest", ex.Message);
            }

            return list;
        }

        // ─────────────────────────────────────────────────────────────────────
        // §19  Note detail — single CM_ChatEntry by ID
        // ─────────────────────────────────────────────────────────────────────

        /// <summary>
        /// Returns full detail for a single CM_ChatEntry note, including body and author.
        /// </summary>
        /// <param name="ctx">Current session context.</param>
        /// <param name="noteId">CM_ChatEntry_ID to retrieve.</param>
        /// <returns>Dynamic with id, title, body, whenTs, who fields.</returns>
        public dynamic GetNoteDetail(Ctx ctx, int noteId)
        {
            dynamic response = new ExpandoObject();
            response.id     = noteId;
            response.title  = "";
            response.body   = "";
            response.whenTs = "";
            response.who    = "";
            try
            {
                if (noteId <= 0) { response.id = 0; return response; }

                var sb = new StringBuilder();
                sb.Append("SELECT e.CM_ChatEntry_ID AS Id,");
                sb.Append("       e.Subject AS Title,");
                sb.Append("       SUBSTR(e.CharacterData, 1, 4000) AS Body,");
                sb.Append("       TO_CHAR(e.Created, 'YYYY-MM-DD HH24:MI') AS WhenTs,");
                sb.Append("       au.Name AS Who");
                sb.Append("  FROM CM_ChatEntry e");
                sb.Append("  LEFT OUTER JOIN AD_User au ON (au.AD_User_ID = COALESCE(e.AD_User_ID, e.CreatedBy) AND au.IsActive = 'Y')");
                sb.Append(" WHERE e.IsActive = 'Y'");
                sb.Append("   AND e.CM_ChatEntry_ID = @noteId");

                string accessSql = MRole.GetDefault(ctx).AddAccessSQL(
                    sb.ToString(), "e", MRole.SQL_FULLYQUALIFIED, MRole.SQL_RO);
                DataSet ds = DB.ExecuteDataset(accessSql,
                    new SqlParameter[] { new SqlParameter("@noteId", noteId) }, null);

                if (ds != null && ds.Tables.Count > 0 && ds.Tables[0].Rows.Count > 0)
                {
                    DataRow row = ds.Tables[0].Rows[0];
                    response.title  = Util.GetValueOfString(row["Title"]);
                    response.body   = Util.GetValueOfString(row["Body"]);
                    response.whenTs = Util.GetValueOfString(row["WhenTs"]);
                    response.who    = Util.GetValueOfString(row["Who"]);
                }
            }
            catch (Exception ex)
            {
                _log.SaveError("VAS_123_QuotationRightPanelModel.GetNoteDetail", ex.Message);
                response.error = ex.Message;
            }
            return response;
        }

        // ─────────────────────────────────────────────────────────────────────
        // §20  Email detail — single MailAttachment1 by ID
        // ─────────────────────────────────────────────────────────────────────

        /// <summary>
        /// Returns full detail for a single email from MailAttachment1.
        /// </summary>
        /// <param name="ctx">Current session context.</param>
        /// <param name="emailId">MailAttachment1_ID to retrieve.</param>
        /// <returns>Dynamic with id, subject, body, whenTs, direction, fromEmail, toEmail, who.</returns>
        public dynamic GetEmailDetail(Ctx ctx, int emailId)
        {
            dynamic response = new ExpandoObject();
            response.id        = emailId;
            response.subject   = "";
            response.body      = "";
            response.whenTs    = "";
            response.direction = "";
            response.fromEmail = "";
            response.toEmail   = "";
            response.who       = "";
            try
            {
                if (emailId <= 0) { response.id = 0; return response; }

                var sb = new StringBuilder();
                sb.Append("SELECT ma.MailAttachment1_ID AS Id,");
                sb.Append("       ma.Title AS Subject,");
                sb.Append("       SUBSTR(ma.TextMsg, 1, 4000) AS Body,");
                sb.Append("       CASE WHEN ma.AttachmentType = 'I'");
                sb.Append("            THEN TO_CHAR(ma.DateMailReceived, 'YYYY-MM-DD HH24:MI')");
                sb.Append("            ELSE TO_CHAR(ma.Created, 'YYYY-MM-DD HH24:MI') END AS WhenTs,");
                sb.Append("       CASE WHEN ma.AttachmentType = 'I' THEN 'in' ELSE 'out' END AS Direction,");
                sb.Append("       ma.MailAddressFrom AS FromEmail,");
                sb.Append("       ma.MailAddress AS ToEmail,");
                sb.Append("       u.Name AS Who");
                sb.Append("  FROM MailAttachment1 ma");
                sb.Append("  LEFT OUTER JOIN AD_User u ON (u.AD_User_ID = ma.CreatedBy AND u.IsActive = 'Y')");
                sb.Append(" WHERE ma.IsActive = 'Y'");
                sb.Append("   AND ma.MailAttachment1_ID = @emailId");

                string accessSql = MRole.GetDefault(ctx).AddAccessSQL(
                    sb.ToString(), "ma", MRole.SQL_FULLYQUALIFIED, MRole.SQL_RO);
                DataSet ds = DB.ExecuteDataset(accessSql,
                    new SqlParameter[] { new SqlParameter("@emailId", emailId) }, null);

                if (ds != null && ds.Tables.Count > 0 && ds.Tables[0].Rows.Count > 0)
                {
                    DataRow row = ds.Tables[0].Rows[0];
                    response.subject   = Util.GetValueOfString(row["Subject"]);
                    response.body      = Util.GetValueOfString(row["Body"]);
                    response.whenTs    = Util.GetValueOfString(row["WhenTs"]);
                    response.direction = Util.GetValueOfString(row["Direction"]);
                    response.fromEmail = Util.GetValueOfString(row["FromEmail"]);
                    response.toEmail   = Util.GetValueOfString(row["ToEmail"]);
                    response.who       = Util.GetValueOfString(row["Who"]);
                }
            }
            catch (Exception ex)
            {
                _log.SaveError("VAS_123_QuotationRightPanelModel.GetEmailDetail", ex.Message);
                response.error = ex.Message;
            }
            return response;
        }

        // ─────────────────────────────────────────────────────────────────────
        // §21  Meeting detail — single AppointmentsInfo by ID with transcript
        // ─────────────────────────────────────────────────────────────────────

        /// <summary>
        /// Returns full meeting detail including transcript and resolved attendee names.
        /// </summary>
        /// <param name="ctx">Current session context.</param>
        /// <param name="meetingId">AppointmentsInfo_ID to retrieve.</param>
        /// <returns>Dynamic with id, subject, startDate, endDate, location, meetingUrl, comments, transcript, attendees, durationMins.</returns>
        public dynamic GetMeetingDetail(Ctx ctx, int meetingId)
        {
            dynamic response = new ExpandoObject();
            response.id           = meetingId;
            response.subject      = "";
            response.startDate    = "";
            response.endDate      = "";
            response.location     = "";
            response.meetingUrl   = "";
            response.comments     = "";
            response.transcript   = "";
            response.attendees    = "";
            response.durationMins = 0;
            try
            {
                if (meetingId <= 0) return response;

                var sb = new StringBuilder();
                sb.Append("SELECT a.AppointmentsInfo_ID AS Id,");
                sb.Append("       a.Subject AS Subject,");
                sb.Append("       TO_CHAR(a.StartDate,'YYYY-MM-DD HH24:MI') AS StartDate,");
                sb.Append("       TO_CHAR(a.EndDate,'YYYY-MM-DD HH24:MI') AS EndDate,");
                sb.Append("       a.Location AS Location,");
                sb.Append("       a.MeetingUrl AS MeetingUrl,");
                sb.Append("       SUBSTR(a.Comments, 1, 4000) AS Comments,");
                sb.Append("       COALESCE(SUBSTR(a.AttendeeInfo, 1, 4000), TO_CHAR(a.AD_User_ID)) AS AttendeeInfo,");
                sb.Append("       SUBSTR(atr.Transcript, 1, 4000) AS Transcript");
                sb.Append("  FROM AppointmentsInfo a");
                sb.Append("  LEFT OUTER JOIN AppointmentTranscript atr ON (atr.AppointmentsInfo_ID = a.AppointmentsInfo_ID)");
                sb.Append(" WHERE a.IsActive = 'Y'");
                sb.Append("   AND a.AppointmentsInfo_ID = @meetingId");

                string accessSql = MRole.GetDefault(ctx).AddAccessSQL(
                    sb.ToString(), "a", MRole.SQL_FULLYQUALIFIED, MRole.SQL_RO);
                DataSet ds = DB.ExecuteDataset(accessSql,
                    new SqlParameter[] { new SqlParameter("@meetingId", meetingId) }, null);

                if (ds != null && ds.Tables.Count > 0 && ds.Tables[0].Rows.Count > 0)
                {
                    DataRow row = ds.Tables[0].Rows[0];
                    response.subject    = Util.GetValueOfString(row["Subject"]);
                    response.startDate  = Util.GetValueOfString(row["StartDate"]);
                    response.endDate    = Util.GetValueOfString(row["EndDate"]);
                    response.location   = Util.GetValueOfString(row["Location"]);
                    response.meetingUrl = Util.GetValueOfString(row["MeetingUrl"]);
                    response.comments   = Util.GetValueOfString(row["Comments"]);
                    response.transcript = Util.GetValueOfString(row["Transcript"]);

                    try
                    {
                        var startStr = Util.GetValueOfString(row["StartDate"]);
                        var endStr   = Util.GetValueOfString(row["EndDate"]);
                        if (!string.IsNullOrEmpty(startStr) && !string.IsNullOrEmpty(endStr))
                        {
                            DateTime dtS = DateTime.ParseExact(startStr, "yyyy-MM-dd HH:mm", null);
                            DateTime dtE = DateTime.ParseExact(endStr,   "yyyy-MM-dd HH:mm", null);
                            response.durationMins = (int)(dtE - dtS).TotalMinutes;
                        }
                    }
                    catch { }

                    // Resolve attendee IDs to names
                    var attendeeRaw = Util.GetValueOfString(row["AttendeeInfo"]);
                    if (!string.IsNullOrEmpty(attendeeRaw))
                    {
                        var ids = new List<string>();
                        foreach (var s in attendeeRaw.Split(','))
                        {
                            var tkn = s.Trim();
                            bool isNum = true;
                            foreach (char c in tkn) { if (!char.IsDigit(c)) { isNum = false; break; } }
                            if (!string.IsNullOrEmpty(tkn) && isNum && !ids.Contains(tkn)) ids.Add(tkn);
                        }
                        if (ids.Count > 0)
                        {
                            var paramNames     = new List<string>();
                            var nameParamList  = new List<SqlParameter>();
                            for (int idx = 0; idx < ids.Count; idx++)
                            {
                                paramNames.Add("@uid" + idx);
                                int uid; int.TryParse(ids[idx], out uid);
                                nameParamList.Add(new SqlParameter("@uid" + idx, uid));
                            }
                            string namesSql = "SELECT Name FROM AD_User WHERE IsActive = 'Y' AND AD_User_ID IN (" +
                                string.Join(",", paramNames) + ") ORDER BY Name";
                            DataSet nameDs = DB.ExecuteDataset(namesSql, nameParamList.ToArray(), null);
                            if (nameDs != null && nameDs.Tables.Count > 0)
                            {
                                var names = new List<string>();
                                foreach (DataRow nr in nameDs.Tables[0].Rows)
                                {
                                    var n = Util.GetValueOfString(nr["Name"]);
                                    if (!string.IsNullOrEmpty(n)) names.Add(n);
                                }
                                response.attendees = string.Join(", ", names);
                            }
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                _log.SaveError("VAS_123_QuotationRightPanelModel.GetMeetingDetail", ex.Message);
            }
            return response;
        }

        // ─────────────────────────────────────────────────────────────────────
        // §22  SaveMeetingComments — update Comments and MeetingUrl on a meeting
        // ─────────────────────────────────────────────────────────────────────

        /// <summary>
        /// Updates Comments and MeetingUrl on an AppointmentsInfo record.
        /// </summary>
        /// <param name="ctx">Current session context.</param>
        /// <param name="meetingId">AppointmentsInfo_ID to update.</param>
        /// <param name="comments">New comments text.</param>
        /// <param name="meetingUrl">New meeting URL.</param>
        /// <returns>Dynamic with success flag.</returns>
        public dynamic SaveMeetingComments(Ctx ctx, int meetingId, string comments, string meetingUrl)
        {
            dynamic response = new ExpandoObject();
            response.success = false;
            try
            {
                if (meetingId <= 0) return response;

                var sb = new StringBuilder();
                sb.Append("UPDATE AppointmentsInfo");
                sb.Append("   SET Comments   = @comments,");
                sb.Append("       MeetingUrl = @meetingUrl,");
                sb.Append("       UpdatedBy  = @userId,");
                sb.Append("       Updated    = CURRENT_TIMESTAMP");
                sb.Append(" WHERE AppointmentsInfo_ID = @meetingId");
                sb.Append("   AND IsActive = 'Y'");

                var sqlParams = new SqlParameter[]
                {
                    new SqlParameter("@comments",   Util.GetValueOfString(comments)),
                    new SqlParameter("@meetingUrl", Util.GetValueOfString(meetingUrl)),
                    new SqlParameter("@userId",     ctx.GetAD_User_ID()),
                    new SqlParameter("@meetingId",  meetingId)
                };

                int rows = DB.ExecuteQuery(sb.ToString(), sqlParams, null);
                response.success = (rows >= 0);
                if (rows < 0)
                    response.error = "save_failed";
            }
            catch (Exception ex)
            {
                _log.SaveError("VAS_123_QuotationRightPanelModel.SaveMeetingComments", ex.Message);
                response.error = ex.Message;
            }
            return response;
        }

        // ─────────────────────────────────────────────────────────────────────
        // §23  GetWhatsAppChat — most recent WSP topic + messages for quotation
        // ─────────────────────────────────────────────────────────────────────

        /// <summary>
        /// Returns the WhatsApp chat topic and messages linked to the quotation.
        /// Returns empty response when the WSP module is not installed.
        /// </summary>
        /// <param name="ctx">Current session context.</param>
        /// <param name="orderId">C_Order_ID of the quotation (= Record_ID).</param>
        /// <param name="tableId">AD_Table_ID for C_Order.</param>
        /// <param name="topicId">Specific WSP_SMChatTopic_ID to load; 0 = most recent.</param>
        /// <returns>Dynamic with topic (topicId, contactName, chatDate) and messages list.</returns>
        public dynamic GetWhatsAppChat(Ctx ctx, int orderId, int tableId, int topicId = 0)
        {
            dynamic response = new ExpandoObject();
            response.topic    = null;
            response.messages = new List<dynamic>();

            if (!Env.IsModuleInstalled("WSP_"))
                return response;

            try
            {
                var sbTopic = new StringBuilder();
                sbTopic.Append("SELECT ct.WSP_SMChatTopic_ID AS topic_id,");
                sbTopic.Append("       COALESCE(ci.Name, N'') AS contact_name,");
                sbTopic.Append("       TO_CHAR(ct.Created, 'YYYY-MM-DD HH24:MI') AS chat_date");
                sbTopic.Append("  FROM WSP_SMChatTopic ct");
                sbTopic.Append("  LEFT OUTER JOIN WSP_SMChatIdentifier ci");
                sbTopic.Append("       ON (ci.WSP_SMChatIdentifier_ID = ct.WSP_SMChatIdentifier_ID");
                sbTopic.Append("           AND ci.IsActive = 'Y')");
                sbTopic.Append(" WHERE ct.IsActive = 'Y'");

                var topicParamList = new List<SqlParameter>();
                if (topicId > 0)
                {
                    sbTopic.Append("   AND ct.WSP_SMChatTopic_ID = @topicId");
                    topicParamList.Add(new SqlParameter("@topicId", topicId));
                }
                else
                {
                    sbTopic.Append("   AND ct.AD_Table_ID = @orderTableId");
                    sbTopic.Append("   AND ct.Record_ID = @orderId");
                    topicParamList.Add(new SqlParameter("@orderTableId", tableId));
                    topicParamList.Add(new SqlParameter("@orderId",      orderId));
                }

                string topicAccessSql = MRole.GetDefault(ctx).AddAccessSQL(
                    sbTopic.ToString(), "ct", MRole.SQL_FULLYQUALIFIED, MRole.SQL_RO);
                topicAccessSql += " ORDER BY ct.Created DESC FETCH FIRST 1 ROWS ONLY";

                DataSet topicDs = DB.ExecuteDataset(topicAccessSql, topicParamList.ToArray(), null);
                if (topicDs == null || topicDs.Tables.Count == 0 || topicDs.Tables[0].Rows.Count == 0)
                    return response;

                DataRow topicRow        = topicDs.Tables[0].Rows[0];
                int     resolvedTopicId = Util.GetValueOfInt(topicRow["topic_id"]);

                dynamic topic     = new ExpandoObject();
                topic.topicId     = resolvedTopicId;
                topic.contactName = Util.GetValueOfString(topicRow["contact_name"]);
                topic.chatDate    = Util.GetValueOfString(topicRow["chat_date"]);
                response.topic    = topic;

                if (resolvedTopicId <= 0) return response;

                var sbMsg = new StringBuilder();
                sbMsg.Append("SELECT m.WSP_IsSender AS is_sender,");
                sbMsg.Append("       COALESCE(m.WSP_TextMsg, TO_CLOB(N'')) AS text_msg,");
                sbMsg.Append("       TO_CHAR(m.Created, 'YYYY-MM-DD HH24:MI') AS msg_date");
                sbMsg.Append("  FROM WSP_SMChatMessage m");
                sbMsg.Append(" WHERE m.IsActive = 'Y'");
                sbMsg.Append("   AND m.WSP_SMChatTopic_ID = @resolvedTopicId");

                string msgAccessSql = MRole.GetDefault(ctx).AddAccessSQL(
                    sbMsg.ToString(), "m", MRole.SQL_FULLYQUALIFIED, MRole.SQL_RO);
                msgAccessSql += " ORDER BY m.Created ASC";

                DataSet msgDs = DB.ExecuteDataset(msgAccessSql,
                    new SqlParameter[] { new SqlParameter("@resolvedTopicId", resolvedTopicId) }, null);
                if (msgDs != null && msgDs.Tables.Count > 0)
                {
                    foreach (DataRow row in msgDs.Tables[0].Rows)
                    {
                        dynamic m  = new ExpandoObject();
                        m.isSender = Util.GetValueOfString(row["is_sender"]);
                        m.textMsg  = Util.GetValueOfString(row["text_msg"]);
                        m.msgDate  = Util.GetValueOfString(row["msg_date"]);
                        ((List<dynamic>)response.messages).Add(m);
                    }
                }
            }
            catch (Exception ex)
            {
                _log.SaveError("VAS_123_QuotationRightPanelModel.GetWhatsAppChat", ex.Message);
                response.error = ex.Message;
            }
            return response;
        }

        // ─────────────────────────────────────────────────────────────────────
        // §24  GetWhatsAppTopicMeta — chatId + mobile for WSP/Inbox/CreateChat
        // ─────────────────────────────────────────────────────────────────────

        /// <summary>
        /// Returns the WSP_SMChat_ID and mobile number for a given chat topic.
        /// Used by the WhatsApp send-message flow in the engagement modal.
        /// </summary>
        /// <param name="ctx">Current session context.</param>
        /// <param name="topicId">WSP_SMChatTopic_ID to look up.</param>
        /// <returns>Dynamic with chatId (int) and mobile (string).</returns>
        public dynamic GetWhatsAppTopicMeta(Ctx ctx, int topicId)
        {
            dynamic response = new ExpandoObject();
            response.chatId = 0;
            response.mobile = "";

            if (topicId <= 0 || !Env.IsModuleInstalled("WSP_"))
                return response;

            try
            {
                object chatIdObj = DB.ExecuteScalar(
                    "SELECT WSP_SMChat_ID FROM WSP_SMChatTopic WHERE IsActive = 'Y' AND WSP_SMChatTopic_ID = @topicId",
                    new SqlParameter[] { new SqlParameter("@topicId", topicId) }, null);
                if (chatIdObj != null && chatIdObj != DBNull.Value)
                    response.chatId = Util.GetValueOfInt(chatIdObj);
            }
            catch { /* column may differ — leave chatId = 0 */ }

            try
            {
                object mobileObj = DB.ExecuteScalar(
                    "SELECT ci.Identifier FROM WSP_SMChatIdentifier ci" +
                    " INNER JOIN WSP_SMChatTopic ct ON (ct.WSP_SMChatIdentifier_ID = ci.WSP_SMChatIdentifier_ID AND ct.IsActive = 'Y')" +
                    " WHERE ci.IsActive = 'Y' AND ct.WSP_SMChatTopic_ID = @topicId",
                    new SqlParameter[] { new SqlParameter("@topicId", topicId) }, null);
                if (mobileObj != null && mobileObj != DBNull.Value)
                    response.mobile = Util.GetValueOfString(mobileObj);
            }
            catch { /* column may differ — leave mobile = "" */ }

            return response;
        }

    }
}
