/********************************************************
 * Module Name    : CRM Extension VAS
 * Purpose        : Service Contract Right Detail Panel — data model
 * Employee Code  : VAI154
 * Date           : 17-Sep-2026
 ******************************************************/
using System;
using System.Collections.Generic;
using System.Data;
using System.Data.SqlClient;
using System.Dynamic;
using System.Text;
using VAdvantage.Classes;
using VAdvantage.DataBase;
using VAdvantage.Logging;
using VAdvantage.Login;
using VAdvantage.Model;
using VAdvantage.ProcessEngine;
using VAdvantage.Utility;

namespace VAS.Models
{
    /// <summary>
    /// Module Name   : CRM Extension VAS
    /// Purpose       : Service Contract Right Detail Panel — data model.
    ///                 Returns contract header and schedule rows for the
    ///                 right-side detail panel on the Service Contract screen.
    /// Chronological development:
    ///   VAI154  17-Sep-2026  Created
    ///   VAI052  28-Oct-2026  Header actions: ScheduleContract/RenewContract flags, RunContractAction
    /// </summary>
    public class VAS_290_ServiceContractRightPanelModel
    {
        private static readonly VLogger _log = VLogger.GetVLogger(typeof(VAS_290_ServiceContractRightPanelModel).FullName);

        // ─────────────────────────────────────────────────────────
        // §1  Contract overview (header + all reference lookups)
        // ─────────────────────────────────────────────────────────

        /// <summary>
        /// Returns all contract header data required by the right-side detail panel,
        /// including translated reference values for DocStatus, ContractType, and
        /// RenewalType and joined lookup names for customer, product, UOM, price list,
        /// payment term, currency, billing location, and frequency.
        /// </summary>
        /// <param name="ctx">Current session context.</param>
        /// <param name="contractId">C_Contract_ID of the selected service contract.</param>
        /// <returns>Dynamic object with all contract header fields, or an object with
        /// <c>error = "not_found"</c> when the contract is inaccessible.</returns>
        public dynamic GetContractOverview(Ctx ctx, int contractId)
        {
            dynamic response = new ExpandoObject();
            response.error = null;

            // The right panel may be invoked from the C_ContractSchedule child tab, which
            // passes C_ContractSchedule_ID as recordID instead of the parent C_Contract_ID.
            // Resolve it here so both header and schedule queries use the correct parent ID.
            contractId = ResolveToContractId(contractId);

            int adClientId = ctx.GetAD_Client_ID();
            string language = ctx.GetAD_Language();

            try
            {
                // Surcharge amount only exists once the surcharge-tax columns are installed.
                bool hasSurcharge = ColumnExists("C_Contract", "SurchargeAmt");

                var sb = new StringBuilder();
                sb.Append("SELECT c.C_Contract_ID,");
                sb.Append("       c.DocumentNo,");
                sb.Append("       c.DocStatus,");
                sb.Append("       c.Processed,");
                sb.Append("       c.ContractType,");
                sb.Append("       c.StartDate,");
                sb.Append("       c.EndDate,");
                sb.Append("       c.C_BPartner_ID,");
                sb.Append("       COALESCE(bp.Name, N'') AS BPartnerName,");
                sb.Append("       COALESCE(bpl.Name, N'') AS BillingLocationName,");
                sb.Append("       TRIM(COALESCE(l.Address1, N'') || ' ' || COALESCE(l.City, N'')) AS BillingLocationAddress,");
                sb.Append("       c.M_Product_ID,");
                sb.Append("       COALESCE(p.Name, N'') AS ProductName,");
                sb.Append("       c.M_AttributeSetInstance_ID,");
                sb.Append("       COALESCE(asi.Description, N'') AS AttributeDisplay,");
                sb.Append("       c.C_UOM_ID,");
                sb.Append("       COALESCE(u.Name, N'') AS UOMName,");
                sb.Append("       c.C_Frequency_ID,");
                sb.Append("       COALESCE(f.Name, N'') AS FrequencyName,");
                sb.Append("       c.RefContract,");
                sb.Append("       c.Ref_Contract_ID,");
                sb.Append("       COALESCE(rc.DocumentNo, N'') AS RefContractDocumentNo,");
                sb.Append("       c.C_Order_ID,");
                sb.Append("       COALESCE(o.DocumentNo, N'') AS OrderDocumentNo,");
                sb.Append("       c.C_OrderLine_ID,");
                sb.Append("       ol.Line AS OrderLineNo,");
                sb.Append("       COALESCE(ol.Description, N'') AS OrderDescription,");
                sb.Append("       c.PriceEntered,");
                sb.Append("       c.PriceActual,");
                sb.Append("       c.PriceList AS PriceListAmount,");
                sb.Append("       c.Discount,");
                sb.Append("       c.TaxAmt,");
                if (hasSurcharge)
                    sb.Append("       c.SurchargeAmt,");
                sb.Append("       c.LineNetAmt,");
                sb.Append("       c.GrandTotal,");
                sb.Append("       c.M_PriceList_ID,");
                sb.Append("       COALESCE(pl.Name, N'') AS PriceListName,");
                sb.Append("       c.C_PaymentTerm_ID,");
                sb.Append("       COALESCE(pt.Name, N'') AS PaymentTermName,");
                sb.Append("       c.C_Currency_ID,");
                sb.Append("       COALESCE(cur.ISO_Code, N'') AS CurrencyISOCode,");
                sb.Append("       COALESCE(cur.CurSymbol, cur.ISO_Code, N'') AS CurrencySymbol,");
                sb.Append("       cur.StdPrecision AS CurrencyPrecision,");
                sb.Append("       COALESCE(c.Description, N'') AS Description,");
                sb.Append("       c.RenewalType,");
                sb.Append("       c.ScheduleContract,");
                sb.Append("       c.RenewContract,");
                sb.Append("       c.CancelBeforeDays,");
                sb.Append("       c.CancellationDate");
                sb.Append("  FROM C_Contract c");
                sb.Append("  LEFT OUTER JOIN C_BPartner bp ON (bp.C_BPartner_ID = c.C_BPartner_ID AND bp.IsActive = 'Y')");
                sb.Append("  LEFT OUTER JOIN C_BPartner_Location bpl ON (bpl.C_BPartner_Location_ID = c.Bill_Location_ID AND bpl.IsActive = 'Y')");
                sb.Append("  LEFT OUTER JOIN C_Location l ON (l.C_Location_ID = bpl.C_Location_ID AND l.IsActive = 'Y')");
                sb.Append("  LEFT OUTER JOIN M_Product p ON (p.M_Product_ID = c.M_Product_ID AND p.IsActive = 'Y')");
                sb.Append("  LEFT OUTER JOIN M_AttributeSetInstance asi ON (asi.M_AttributeSetInstance_ID = c.M_AttributeSetInstance_ID)");
                sb.Append("  LEFT OUTER JOIN C_UOM u ON (u.C_UOM_ID = c.C_UOM_ID AND u.IsActive = 'Y')");
                sb.Append("  LEFT OUTER JOIN C_Frequency f ON (f.C_Frequency_ID = c.C_Frequency_ID AND f.IsActive = 'Y')");
                sb.Append("  LEFT OUTER JOIN C_Contract rc ON (rc.C_Contract_ID = c.Ref_Contract_ID AND rc.IsActive = 'Y')");
                sb.Append("  LEFT OUTER JOIN C_Order o ON (o.C_Order_ID = c.C_Order_ID AND o.IsActive = 'Y')");
                sb.Append("  LEFT OUTER JOIN C_OrderLine ol ON (ol.C_OrderLine_ID = c.C_OrderLine_ID AND ol.IsActive = 'Y')");
                sb.Append("  LEFT OUTER JOIN M_PriceList pl ON (pl.M_PriceList_ID = c.M_PriceList_ID AND pl.IsActive = 'Y')");
                sb.Append("  LEFT OUTER JOIN C_PaymentTerm pt ON (pt.C_PaymentTerm_ID = c.C_PaymentTerm_ID AND pt.IsActive = 'Y')");
                sb.Append("  LEFT OUTER JOIN C_Currency cur ON (cur.C_Currency_ID = c.C_Currency_ID AND cur.IsActive = 'Y')");
                sb.Append(" WHERE c.C_Contract_ID = @contractId");
                sb.Append("   AND c.AD_Client_ID = @adClientId");
                sb.Append("   AND c.IsActive = 'Y'");

                string baseSql = sb.ToString();
                string accessSql = MRole.GetDefault(ctx).AddAccessSQL(
                    baseSql, "c", MRole.SQL_FULLYQUALIFIED, MRole.SQL_RO);
                if (DB.IsOracle())
                    accessSql = RemovePrivateAccessClauses(accessSql);

                var sqlParams = new SqlParameter[]
                {
                    new SqlParameter("@contractId", contractId),
                    new SqlParameter("@adClientId", adClientId)
                };

                DataSet ds = DB.ExecuteDataset(accessSql, sqlParams, null);
                if (ds == null || ds.Tables.Count == 0 || ds.Tables[0].Rows.Count == 0)
                {
                    response.error = "not_found";
                    return response;
                }

                DataRow row = ds.Tables[0].Rows[0];

                response.id = Util.GetValueOfInt(row["C_Contract_ID"]);
                response.documentNo = Util.GetValueOfString(row["DocumentNo"]);
                response.docStatus = Util.GetValueOfString(row["DocStatus"]);
                response.processed = Util.GetValueOfString(row["Processed"]) == "Y";
                response.contractType = Util.GetValueOfString(row["ContractType"]);
                response.startDate = row["StartDate"] != DBNull.Value ? Convert.ToDateTime(row["StartDate"]).ToString("yyyy-MM-dd") : null;
                response.endDate = row["EndDate"] != DBNull.Value ? Convert.ToDateTime(row["EndDate"]).ToString("yyyy-MM-dd") : null;
                response.bPartnerId = Util.GetValueOfInt(row["C_BPartner_ID"]);
                response.bPartnerName = Util.GetValueOfString(row["BPartnerName"]);
                response.billingLocationName = Util.GetValueOfString(row["BillingLocationName"]);
                response.billingLocationAddress = Util.GetValueOfString(row["BillingLocationAddress"]);
                response.productId = Util.GetValueOfInt(row["M_Product_ID"]);
                response.productName = Util.GetValueOfString(row["ProductName"]);
                response.attributeDisplay = Util.GetValueOfString(row["AttributeDisplay"]);
                response.uomName = Util.GetValueOfString(row["UOMName"]);
                response.frequencyName = Util.GetValueOfString(row["FrequencyName"]);
                response.refContract = Util.GetValueOfString(row["RefContract"]);
                response.refContractDocumentNo = Util.GetValueOfString(row["RefContractDocumentNo"]);
                response.orderDocumentNo = Util.GetValueOfString(row["OrderDocumentNo"]);
                response.orderLineNo = Util.GetValueOfString(row["OrderLineNo"]);
                response.orderDescription = Util.GetValueOfString(row["OrderDescription"]);
                response.priceEntered = row["PriceEntered"] != DBNull.Value ? Convert.ToDecimal(row["PriceEntered"]) : (decimal?)null;
                response.priceActual = row["PriceActual"] != DBNull.Value ? Convert.ToDecimal(row["PriceActual"]) : (decimal?)null;
                response.priceListAmount = row["PriceListAmount"] != DBNull.Value ? Convert.ToDecimal(row["PriceListAmount"]) : (decimal?)null;
                response.discount = row["Discount"] != DBNull.Value ? Convert.ToDecimal(row["Discount"]) : (decimal?)null;
                response.taxAmt = row["TaxAmt"] != DBNull.Value ? Convert.ToDecimal(row["TaxAmt"]) : (decimal?)null;
                response.surchargeAmt = (hasSurcharge && row["SurchargeAmt"] != DBNull.Value) ? Convert.ToDecimal(row["SurchargeAmt"]) : (decimal?)null;
                response.lineNetAmt = row["LineNetAmt"] != DBNull.Value ? Convert.ToDecimal(row["LineNetAmt"]) : (decimal?)null;
                response.grandTotal = row["GrandTotal"] != DBNull.Value ? Convert.ToDecimal(row["GrandTotal"]) : (decimal?)null;
                response.priceListName = Util.GetValueOfString(row["PriceListName"]);
                response.paymentTermName = Util.GetValueOfString(row["PaymentTermName"]);
                response.currencyIsoCode = Util.GetValueOfString(row["CurrencyISOCode"]);
                response.currencySymbol = Util.GetValueOfString(row["CurrencySymbol"]);
                response.currencyPrecision = row["CurrencyPrecision"] != DBNull.Value ? Util.GetValueOfInt(row["CurrencyPrecision"]) : 2;
                response.description = Util.GetValueOfString(row["Description"]);
                response.renewalType = Util.GetValueOfString(row["RenewalType"]);

                // Header action availability. The same rule set re-checks every action on the
                // server (RunContractAction), so the buttons only mirror what is allowed.
                string scheduleContract = NormalizeYN(row["ScheduleContract"]);
                string renewContract    = NormalizeYN(row["RenewContract"]);
                string renewalTypeCode  = Util.GetValueOfString(row["RenewalType"]);
                response.scheduleContract     = scheduleContract;
                response.renewContract        = renewContract;
                response.canGenerateSchedule  = CanRunAction(ACTION_GENERATE_SCHEDULE, scheduleContract, renewContract, renewalTypeCode);
                response.canGenerateInvoice   = CanRunAction(ACTION_GENERATE_INVOICE,  scheduleContract, renewContract, renewalTypeCode);
                response.canRenew             = CanRunAction(ACTION_RENEW,             scheduleContract, renewContract, renewalTypeCode);

                response.cancelBeforeDays = row["CancelBeforeDays"] != DBNull.Value ? Util.GetValueOfInt(row["CancelBeforeDays"]) : (int?)null;
                response.cancellationDate = row["CancellationDate"] != DBNull.Value ? Convert.ToDateTime(row["CancellationDate"]).ToString("yyyy-MM-dd") : null;
            }
            catch (Exception ex)
            {
                _log.SaveError("VAS_290_ServiceContractRightPanelModel.GetContractOverview.Main", ex.Message);
                response.error = "not_found";
                return response;
            }

            // ── Order Line: the same text the window shows in its Order Line field ──
            // Separate, guarded query so a lookup problem falls back to the line number in the
            // panel instead of failing the whole header.
            if (!string.IsNullOrEmpty(Util.GetValueOfString(response.orderLineNo)))
            {
                response.orderLineDisplay = GetOrderLineDisplay(ctx, contractId);
            }

            // ── Total invoiced: the invoices generated for this contract ────────────
            response.invoicedTotal = GetInvoicedTotal(ctx, contractId);

            // ── Translate reference display values via AD_Ref_List ─────────────
            // These are resolved via separate targeted queries so that a missing
            // reference configuration silences only that one label, not the full panel.
            try
            {
                string docStatus = Util.GetValueOfString(response.docStatus);
                response.docStatusLabel = GetRefListLabel(ctx, "C_Contract", "DocStatus", docStatus, language);
            }
            catch (Exception ex)
            {
                _log.SaveError("VAS_290_ServiceContractRightPanelModel.GetContractOverview.DocStatus", ex.Message);
                response.docStatusLabel = response.docStatus;
            }

            try
            {
                string contractType = Util.GetValueOfString(response.contractType);
                response.contractTypeLabel = GetRefListLabel(ctx, "C_Contract", "ContractType", contractType, language);
            }
            catch (Exception ex)
            {
                _log.SaveError("VAS_290_ServiceContractRightPanelModel.GetContractOverview.ContractType", ex.Message);
                response.contractTypeLabel = response.contractType;
            }

            try
            {
                string renewalType = Util.GetValueOfString(response.renewalType);
                response.renewalTypeLabel = GetRefListLabel(ctx, "C_Contract", "RenewalType", renewalType, language);
            }
            catch (Exception ex)
            {
                _log.SaveError("VAS_290_ServiceContractRightPanelModel.GetContractOverview.RenewalType", ex.Message);
                response.renewalTypeLabel = response.renewalType;
            }

            return response;
        }

        /// <summary>
        /// Display text of the contract's Order Line, built by the framework's own TableDir lookup
        /// (the identifier columns of C_OrderLine) - the same text the window's Order Line field shows.
        /// </summary>
        /// <param name="ctx">Current session context.</param>
        /// <param name="contractId">C_Contract_ID.</param>
        /// <returns>Display text, or null when it cannot be resolved (caller falls back to the line number).</returns>
        private string GetOrderLineDisplay(Ctx ctx, int contractId)
        {
            try
            {
                Language language = Language.GetLoginLanguage(ctx);
                string embed = VLookUpFactory.GetLookup_TableDirEmbed(language, "C_OrderLine_ID", "c", "C_OrderLine_ID");
                if (string.IsNullOrEmpty(embed)) return null;

                string text = Util.GetValueOfString(DB.ExecuteScalar(
                    "SELECT (" + embed + ") FROM C_Contract c WHERE c.C_Contract_ID = @contractId",
                    new SqlParameter[] { new SqlParameter("@contractId", contractId) },
                    null));
                return string.IsNullOrEmpty(text) ? null : text;
            }
            catch (Exception ex)
            {
                _log.SaveError("VAS_290_ServiceContractRightPanelModel.GetOrderLineDisplay", ex.Message);
                return null;
            }
        }

        /// <summary>
        /// Sum of the grand totals of the invoices generated for the contract: every invoice that
        /// carries the contract (C_Invoice.C_Contract_ID, set by the Generate Invoice process) plus any
        /// invoice linked from one of its schedules. Voided and reversed invoices and the negative
        /// reversal documents are left out. Independent of whether the schedule rows kept their link.
        /// </summary>
        /// <param name="ctx">Current session context.</param>
        /// <param name="contractId">C_Contract_ID.</param>
        /// <returns>Total, or null when it cannot be read (the panel then falls back to the schedules).</returns>
        private decimal? GetInvoicedTotal(Ctx ctx, int contractId)
        {
            try
            {
                string sql = @"SELECT COALESCE(SUM(i.GrandTotal), 0)
                                 FROM C_Invoice i
                                WHERE i.AD_Client_ID = @adClientId
                                  AND i.IsActive = 'Y'
                                  AND i.DocStatus NOT IN ('VO', 'RE')
                                  AND i.GrandTotal > 0
                                  AND (i.C_Contract_ID = @contractId
                                       OR i.C_Invoice_ID IN (SELECT s.C_Invoice_ID
                                                               FROM C_ContractSchedule s
                                                              WHERE s.C_Contract_ID = @contractId
                                                                AND s.C_Invoice_ID IS NOT NULL))";
                object value = DB.ExecuteScalar(sql, new SqlParameter[]
                {
                    new SqlParameter("@adClientId", ctx.GetAD_Client_ID()),
                    new SqlParameter("@contractId", contractId)
                }, null);
                return (value == null || value == DBNull.Value) ? (decimal?)null : Convert.ToDecimal(value);
            }
            catch (Exception ex)
            {
                _log.SaveError("VAS_290_ServiceContractRightPanelModel.GetInvoicedTotal", ex.Message);
                return null;
            }
        }

        // ─────────────────────────────────────────────────────────
        // §1b  Header actions (Generate Schedule / Generate Invoice / Renew)
        // ─────────────────────────────────────────────────────────

        private const string ACTION_GENERATE_SCHEDULE = "GenerateSchedule";
        private const string ACTION_GENERATE_INVOICE  = "GenerateInvoice";
        private const string ACTION_RENEW             = "Renew";

        /// <summary>Y/N flag as stored; anything other than Y (including empty) counts as N.</summary>
        private static string NormalizeYN(object value)
        {
            return Util.GetValueOfString(value) == "Y" ? "Y" : "N";
        }

        /// <summary>
        /// Rules for the three header actions:
        ///   Generate Schedule : ScheduleContract = N
        ///   Generate Invoice  : ScheduleContract = Y
        ///   Renew             : ScheduleContract = Y AND RenewContract = N AND RenewalType != 'A'
        /// </summary>
        private static bool CanRunAction(string action, string scheduleContract, string renewContract, string renewalType)
        {
            switch (action)
            {
                case ACTION_GENERATE_SCHEDULE:
                    return scheduleContract == "N";
                case ACTION_GENERATE_INVOICE:
                    return scheduleContract == "Y";
                case ACTION_RENEW:
                    return scheduleContract == "Y" && renewContract == "N" && renewalType != X_C_Contract.RENEWALTYPE_Automatic;
                default:
                    return false;
            }
        }

        /// <summary>C_Contract button column whose AD_Column.AD_Process_ID is the process behind an action.</summary>
        private static string GetActionColumnName(string action)
        {
            switch (action)
            {
                case ACTION_GENERATE_SCHEDULE: return "ScheduleContract";
                case ACTION_GENERATE_INVOICE:  return "GenerateInvoice";
                case ACTION_RENEW:             return "RenewContract";
                default:                       return null;
            }
        }

        private static string GetMsgOr(Ctx ctx, string key, string fallback)
        {
            string text = Msg.GetMsg(ctx, key);
            return (string.IsNullOrEmpty(text) || text == key || text == "[" + key + "]") ? fallback : text;
        }

        /// <summary>
        /// Runs one of the header actions through the standard process engine, exactly as the
        /// classic window's own button would. The posted contract id is never trusted on its own:
        /// the row is re-read with role access, and the action is re-validated against the same
        /// rules that drive the buttons before the process starts.
        /// </summary>
        /// <param name="ctx">Current session context.</param>
        /// <param name="contractId">C_Contract_ID (or a child schedule id) of the selected contract.</param>
        /// <param name="actionName">GenerateSchedule, GenerateInvoice or Renew.</param>
        /// <returns>Object with <c>success</c> and a user-facing <c>message</c>.</returns>
        public dynamic RunContractAction(Ctx ctx, int contractId, string actionName)
        {
            dynamic response = new ExpandoObject();
            response.success = false;
            response.message = null;

            contractId = ResolveToContractId(contractId);
            string buttonColumn = GetActionColumnName(actionName);
            if (contractId <= 0 || buttonColumn == null)
            {
                response.message = GetMsgOr(ctx, "VAS_290_ActionNotAllowed", "This action is not available for the selected contract.");
                return response;
            }

            try
            {
                string baseSql = "SELECT c.ScheduleContract, c.RenewContract, c.RenewalType"
                               + "  FROM C_Contract c"
                               + " WHERE c.C_Contract_ID = @contractId"
                               + "   AND c.AD_Client_ID = @adClientId"
                               + "   AND c.IsActive = 'Y'";
                string accessSql = MRole.GetDefault(ctx).AddAccessSQL(
                    baseSql, "c", MRole.SQL_FULLYQUALIFIED, MRole.SQL_RW);
                if (DB.IsOracle())
                    accessSql = RemovePrivateAccessClauses(accessSql);

                DataSet ds = DB.ExecuteDataset(accessSql, new SqlParameter[]
                {
                    new SqlParameter("@contractId", contractId),
                    new SqlParameter("@adClientId", ctx.GetAD_Client_ID())
                }, null);

                if (ds == null || ds.Tables.Count == 0 || ds.Tables[0].Rows.Count == 0)
                {
                    response.message = GetMsgOr(ctx, "VAS_290_ContractNotFound", "Contract not found or not accessible.");
                    return response;
                }

                DataRow row = ds.Tables[0].Rows[0];
                if (!CanRunAction(actionName, NormalizeYN(row["ScheduleContract"]), NormalizeYN(row["RenewContract"]),
                                  Util.GetValueOfString(row["RenewalType"])))
                {
                    response.message = GetMsgOr(ctx, "VAS_290_ActionNotAllowed", "This action is not available for the selected contract.");
                    return response;
                }

                int processId = GetColumnProcessId("C_Contract", buttonColumn);
                if (processId <= 0)
                {
                    response.message = GetMsgOr(ctx, "VAS_290_ProcessNotConfigured", "This action is not configured.");
                    return response;
                }

                MPInstance instance = new MPInstance(ctx, processId, contractId);
                if (!instance.Save())
                {
                    response.message = GetMsgOr(ctx, "VAS_290_ProcessStartFailed", "The process could not be started.");
                    return response;
                }

                ProcessInfo pi = new ProcessInfo(string.Empty, processId, MTable.Get_Table_ID("C_Contract"), contractId);
                pi.SetAD_PInstance_ID(instance.GetAD_PInstance_ID());
                pi.SetAD_Client_ID(ctx.GetAD_Client_ID());
                pi.SetAD_Org_ID(ctx.GetAD_Org_ID());
                pi.SetAD_User_ID(ctx.GetAD_User_ID());

                ProcessCtl worker = new ProcessCtl(ctx, null, pi, null);
                worker.Run();

                string summary = pi.GetSummary();
                if (pi.IsError())
                {
                    response.message = !string.IsNullOrEmpty(summary)
                        ? summary
                        : GetMsgOr(ctx, "VAS_290_ProcessFailed", "The process reported an error.");
                    return response;
                }

                response.success = true;
                response.message = summary;
            }
            catch (Exception ex)
            {
                _log.SaveError("VAS_290_ServiceContractRightPanelModel.RunContractAction(" + actionName + ")", ex.Message);
                response.success = false;
                response.message = ex.Message;
            }
            return response;
        }

        /// <summary>
        /// AD_Process_ID wired to a table button column (AD_Table -> AD_Column.AD_Process_ID).
        /// Resolved from the dictionary every time because the id differs per tenant/import.
        /// </summary>
        private static int GetColumnProcessId(string tableName, string columnName)
        {
            string sql = @"SELECT c.AD_Process_ID
                             FROM AD_Table t
                             JOIN AD_Column c ON (c.AD_Table_ID = t.AD_Table_ID)
                            WHERE t.TableName = @tbl
                              AND c.ColumnName = @col
                              AND t.IsActive = 'Y'
                              AND c.IsActive = 'Y'
                              AND c.AD_Process_ID IS NOT NULL";
            return Util.GetValueOfInt(DB.ExecuteScalar(sql, new SqlParameter[]
            {
                new SqlParameter("@tbl", tableName),
                new SqlParameter("@col", columnName)
            }, null));
        }

        // ─────────────────────────────────────────────────────────
        // §2  Contract schedules
        // ─────────────────────────────────────────────────────────

        /// <summary>
        /// Returns all active schedule rows for the specified contract, with a
        /// derived BillingStatus for each row (Invoiced / Due / Scheduled) based
        /// on whether C_Invoice_ID is set and whether FROMDATE is on or before the
        /// current application date. Rows are sorted by FROMDATE, then schedule ID.
        /// </summary>
        /// <param name="ctx">Current session context.</param>
        /// <param name="contractId">C_Contract_ID of the parent service contract.</param>
        /// <returns>Dynamic object with <c>items</c> list of schedule rows.</returns>
        public dynamic GetContractSchedules(Ctx ctx, int contractId)
        {
            dynamic response = new ExpandoObject();
            response.items = new List<dynamic>();

            // Same parent-ID resolution as GetContractOverview.
            contractId = ResolveToContractId(contractId);

            int adClientId = ctx.GetAD_Client_ID();

            // Use application date (ctx date) as the as-of date; never use SYSDATE/NOW
            // inside SQL so the same date governs all schedule-status decisions.
            DateTime? _ctxDate = CommonFunctions.CovertMilliToDate(ctx.GetContextAsTime("#Date"));
            DateTime asOfDate = _ctxDate.HasValue ? _ctxDate.Value.Date : DateTime.Now.Date;
            _log.Info("VAS_290 GetContractSchedules: _ctxDate=" + (_ctxDate.HasValue ? _ctxDate.Value.ToString("yyyy-MM-dd HH:mm:ss") : "null")
                    + " | asOfDate=" + asOfDate.ToString("yyyy-MM-dd")
                    + " | asOfDate_formatted=" + asOfDate.ToString("yyyy-MM-dd")
                    + " | contractId=" + contractId);

            try
            {
                // SurchargeAmt only exists once the surcharge-tax columns are installed (the
                // Schedule Contract process guards it the same way), so read it only when present.
                bool hasSurcharge = ColumnExists("C_ContractSchedule", "SurchargeAmt");

                var sb = new StringBuilder();
                sb.Append("SELECT s.C_ContractSchedule_ID,");
                sb.Append("       s.C_Contract_ID,");
                sb.Append("       s.FROMDATE,");
                sb.Append("       s.EndDate,");
                sb.Append("       s.TotalAmt,");
                sb.Append("       s.TaxAmt,");
                sb.Append("       s.GrandTotal,");
                if (hasSurcharge)
                    sb.Append("       COALESCE(s.SurchargeAmt, 0) AS SurchargeAmt,");
                sb.Append("       s.C_Invoice_ID,");
                // Correlated subquery instead of LEFT JOIN so AddAccessSQL does not inject
                // "i.C_Invoice_ID NOT IN (SELECT Record_ID ...)" which causes ORA-01722 on Oracle
                // when C_Invoice_ID is NULL (LEFT JOIN miss) or when Record_ID type-mismatches NUMBER.
                sb.Append("       (SELECT inv.DocumentNo FROM C_Invoice inv WHERE inv.C_Invoice_ID = s.C_Invoice_ID AND inv.IsActive = 'Y') AS InvoiceDocumentNo, ");
                // The generated invoice's own amount and status (Total Invoiced in the Billing section
                // sums these rather than the schedule's planned amounts).
                sb.Append("       (SELECT inv.GrandTotal FROM C_Invoice inv WHERE inv.C_Invoice_ID = s.C_Invoice_ID AND inv.IsActive = 'Y') AS InvoiceGrandTotal, ");
                sb.Append("       (SELECT inv.DocStatus FROM C_Invoice inv WHERE inv.C_Invoice_ID = s.C_Invoice_ID AND inv.IsActive = 'Y') AS InvoiceDocStatus, ");
                sb.Append("       CASE");
                sb.Append("           WHEN s.C_Invoice_ID IS NOT NULL THEN 'Invoiced'");
                // Date literal is inlined for both dialects so no bind parameter is needed.
                // Oracle: TO_DATE(literal) avoids ODP.NET mapping DateTime params to NUMBER (ORA-00932).
                // PostgreSQL: ISO date string 'YYYY-MM-DD' is accepted natively; CONVERT is SQL Server only.
                sb.Append("           WHEN s.FROMDATE <= " + GlobalVariable.TO_DATE(asOfDate, true) + " THEN 'Due'");
                sb.Append("           ELSE 'Scheduled'");
                sb.Append("       END AS BillingStatus");
                sb.Append("  FROM C_ContractSchedule s");
                sb.Append(" WHERE s.C_Contract_ID = @contractId");
                sb.Append("   AND s.AD_Client_ID = @adClientId");
                sb.Append("   AND s.IsActive = 'Y'");

                string baseSql = sb.ToString();
                string accessSql = MRole.GetDefault(ctx).AddAccessSQL(
                    baseSql, "s", MRole.SQL_FULLYQUALIFIED, MRole.SQL_RO);
                // RULE 5: ORDER BY must be appended after AddAccessSQL, not inside baseSql.
                // AddAccessSQL injects WHERE predicates at the end of the SQL string; if ORDER BY
                // is already present those predicates land after it, producing invalid SQL that
                // causes DB.ExecuteDataset to return null.
                accessSql += " ORDER BY s.FROMDATE, s.C_ContractSchedule_ID";
                // Remove the NOT IN (SELECT Record_ID FROM AD_Private_Access ...) clause on all databases:
                // • Oracle: Record_ID is VARCHAR2 but PK is NUMBER → ORA-01722 on implicit conversion.
                // • SQL Server: if AD_Private_Access has NULL Record_ID values the NOT IN evaluates
                //   to NULL for every row, silently returning 0 rows.
                // C_ContractSchedule rows are already scoped to a secured parent contract, so the
                // private-access predicate is redundant and safe to omit.
                accessSql = RemovePrivateAccessClauses(accessSql);

                var sqlParams = new SqlParameter[]
                {
                    new SqlParameter("@contractId", contractId),
                    new SqlParameter("@adClientId", adClientId)
                };

                DataSet ds = DB.ExecuteDataset(accessSql, sqlParams, null);
                if (ds == null || ds.Tables.Count == 0)
                    return response;

                var items = new List<dynamic>();
                foreach (DataRow row in ds.Tables[0].Rows)
                {
                    dynamic item = new ExpandoObject();
                    item.id = Util.GetValueOfInt(row["C_ContractSchedule_ID"]);
                    item.contractId = Util.GetValueOfInt(row["C_Contract_ID"]);
                    item.fromDate = row["FROMDATE"] != DBNull.Value ? Convert.ToDateTime(row["FROMDATE"]).ToString("yyyy-MM-dd") : null;
                    item.endDate = row["EndDate"] != DBNull.Value ? Convert.ToDateTime(row["EndDate"]).ToString("yyyy-MM-dd") : null;
                    decimal? totalAmt   = row["TotalAmt"] != DBNull.Value ? Convert.ToDecimal(row["TotalAmt"]) : (decimal?)null;
                    decimal? taxAmt     = row["TaxAmt"] != DBNull.Value ? Convert.ToDecimal(row["TaxAmt"]) : (decimal?)null;
                    decimal? grandTotal = row["GrandTotal"] != DBNull.Value ? Convert.ToDecimal(row["GrandTotal"]) : (decimal?)null;
                    decimal surchargeAmt = (hasSurcharge && row["SurchargeAmt"] != DBNull.Value) ? Convert.ToDecimal(row["SurchargeAmt"]) : 0m;
                    item.totalAmt = totalAmt;
                    item.taxAmt = taxAmt;
                    item.grandTotal = grandTotal;
                    item.surchargeAmt = surchargeAmt;
                    // Taxable amount = the base the tax and surcharge are charged on. TotalAmt is not
                    // that: it carries the contract's line amount, which is tax-inclusive when the
                    // price list is, so it can equal the grand total.
                    item.taxableAmt = grandTotal.HasValue ? grandTotal.Value - (taxAmt ?? 0m) - surchargeAmt : totalAmt;
                    item.cInvoiceId = row["C_Invoice_ID"] != DBNull.Value ? Util.GetValueOfInt(row["C_Invoice_ID"]) : (int?)null;
                    item.invoiceDocumentNo = Util.GetValueOfString(row["InvoiceDocumentNo"]);
                    item.invoiceGrandTotal = row["InvoiceGrandTotal"] != DBNull.Value ? Convert.ToDecimal(row["InvoiceGrandTotal"]) : (decimal?)null;
                    item.invoiceDocStatus = Util.GetValueOfString(row["InvoiceDocStatus"]);
                    item.billingStatus = Util.GetValueOfString(row["BillingStatus"]);
                    items.Add(item);
                }

                response.items = items;
            }
            catch (Exception ex)
            {
                _log.SaveError("VAS_290_ServiceContractRightPanelModel.GetContractSchedules", ex.Message);
                response.error = ex.Message;
            }

            return response;
        }

        // ─────────────────────────────────────────────────────────
        // Private helpers
        // ─────────────────────────────────────────────────────────

        /// <summary>Whether the dictionary defines an active column on a table.</summary>
        private static bool ColumnExists(string tableName, string columnName)
        {
            try
            {
                int count = Util.GetValueOfInt(DB.ExecuteScalar(
                    @"SELECT COUNT(*)
                        FROM AD_Column c
                        JOIN AD_Table t ON (t.AD_Table_ID = c.AD_Table_ID)
                       WHERE t.TableName = @tbl AND c.ColumnName = @col
                         AND t.IsActive = 'Y' AND c.IsActive = 'Y'",
                    new SqlParameter[]
                    {
                        new SqlParameter("@tbl", tableName),
                        new SqlParameter("@col", columnName)
                    }, null));
                return count > 0;
            }
            catch (Exception ex)
            {
                _log.SaveError("VAS_290_ServiceContractRightPanelModel.ColumnExists", ex.Message);
                return false;
            }
        }

        /// <summary>
        /// Resolves a possible C_ContractSchedule_ID to its parent C_Contract_ID.
        /// The right panel may be invoked from the C_ContractSchedule child tab, in which
        /// case the framework passes the selected schedule row's PK (C_ContractSchedule_ID)
        /// rather than the parent C_Contract_ID. If possibleId is found in C_ContractSchedule
        /// the method returns that row's C_Contract_ID; otherwise possibleId is returned
        /// unchanged (it is already a C_Contract_ID).
        /// No MRole security is applied here — the caller's main queries apply it on the
        /// resolved ID, so security is not bypassed.
        /// </summary>
        /// <param name="possibleId">ID received by the controller — may be either a C_Contract_ID
        /// or a C_ContractSchedule_ID depending on which tab triggered the panel.</param>
        /// <returns>Resolved C_Contract_ID, or possibleId when no resolution was needed.</returns>
        private int ResolveToContractId(int possibleId)
        {
            if (possibleId <= 0) return possibleId;
            try
            {
                object result = DB.ExecuteScalar(
                    "SELECT C_Contract_ID FROM C_ContractSchedule WHERE C_ContractSchedule_ID = @id AND IsActive = 'Y'",
                    new SqlParameter[] { new SqlParameter("@id", possibleId) },
                    null);
                if (result != null && result != DBNull.Value)
                {
                    int parentId = Util.GetValueOfInt(result);
                    if (parentId > 0 && parentId != possibleId)
                    {
                        _log.Info("VAS_290 ResolveToContractId: C_ContractSchedule_ID=" + possibleId
                                + " resolved to C_Contract_ID=" + parentId);
                        return parentId;
                    }
                }
            }
            catch (Exception ex)
            {
                _log.SaveError("VAS_290_ServiceContractRightPanelModel.ResolveToContractId", ex.Message);
            }
            return possibleId;
        }

        /// <summary>
        /// Removes all "AND alias.PK NOT IN (SELECT Record_ID FROM AD_Private_Access ...)"
        /// clauses that AddAccessSQL injects into a SQL string on Oracle.
        /// The Record_ID column in AD_Private_Access is VARCHAR2, but primary-key columns
        /// are NUMBER; the implicit type conversion causes ORA-01722 on any query that
        /// touches AD_Private_Access rows whose Record_ID values are not numeric strings.
        /// This helper locates every occurrence of the AD_Private_Access table name in
        /// the SQL, walks back to the enclosing NOT IN subquery, then removes the whole
        /// "AND ... NOT IN (...)" clause using balanced-paren tracking.
        /// </summary>
        /// <param name="sql">SQL string returned by AddAccessSQL.</param>
        /// <returns>SQL string with all AD_Private_Access NOT IN clauses removed.</returns>
        private static string RemovePrivateAccessClauses(string sql)
        {
            const string PA_TABLE = "AD_Private_Access";
            const string NOT_IN_P = "NOT IN (";

            int searchPos = 0;
            while (true)
            {
                // Locate the next occurrence of the private-access table name.
                int paIdx = sql.IndexOf(PA_TABLE, searchPos, StringComparison.OrdinalIgnoreCase);
                if (paIdx < 0) break;

                // Walk backward from the table-name position to find "NOT IN (" that encloses it.
                int notInIdx = sql.LastIndexOf(NOT_IN_P, paIdx, StringComparison.OrdinalIgnoreCase);
                if (notInIdx < 0) { searchPos = paIdx + PA_TABLE.Length; continue; }

                // Walk backward from "NOT IN (" to find the "AND " that starts the clause.
                int andIdx = sql.LastIndexOf("AND ", notInIdx, StringComparison.OrdinalIgnoreCase);
                if (andIdx < 0) { searchPos = paIdx + PA_TABLE.Length; continue; }

                // The opening paren of the NOT IN subquery sits right after "NOT IN ".
                int openParen = notInIdx + NOT_IN_P.Length - 1; // index of '('
                if (openParen >= sql.Length || sql[openParen] != '(')
                {
                    searchPos = paIdx + PA_TABLE.Length;
                    continue;
                }

                // Track paren depth to find the matching closing paren.
                int depth = 0, closeParen = -1;
                for (int ci = openParen; ci < sql.Length; ci++)
                {
                    if (sql[ci] == '(') depth++;
                    else if (sql[ci] == ')') { if (--depth == 0) { closeParen = ci; break; } }
                }

                if (closeParen < 0) { searchPos = paIdx + PA_TABLE.Length; continue; }

                // Remove from "AND " through the closing paren (inclusive).
                sql = sql.Remove(andIdx, closeParen + 1 - andIdx);

                // Don't advance searchPos — the string shrank; the next iteration
                // starts from the same position and will catch any further clauses.
            }

            return sql;
        }

        /// <summary>
        /// Resolves the translated display name for a reference-list value on a
        /// specific table column from AD_Ref_List / AD_Ref_List_Trl.
        /// Falls back to the raw code when no translation is found.
        /// MRole is not applied here because AD_Ref_List is a configuration table,
        /// not a tenant-owned business-data table.
        /// </summary>
        /// <param name="ctx">Current session context (for language).</param>
        /// <param name="tableName">Physical table name (e.g. "C_Contract").</param>
        /// <param name="columnName">Column name (e.g. "DocStatus").</param>
        /// <param name="value">Raw stored code value (e.g. "CO").</param>
        /// <param name="language">AD_Language code (e.g. "en_US").</param>
        /// <returns>Translated display name, or <paramref name="value"/> as fallback.</returns>
        private string GetRefListLabel(Ctx ctx, string tableName, string columnName, string value, string language)
        {
            if (string.IsNullOrWhiteSpace(value)) return value;

            var sb = new StringBuilder();
            sb.Append("SELECT COALESCE(rlt.Name, rl.Name, rl.Value) AS DisplayName");
            sb.Append("  FROM AD_Ref_List rl");
            sb.Append("  INNER JOIN AD_Column col ON (col.AD_Reference_Value_ID = rl.AD_Reference_ID");
            sb.Append("                               AND col.ColumnName = @colName AND col.IsActive = 'Y')");
            sb.Append("  INNER JOIN AD_Table tab ON (tab.AD_Table_ID = col.AD_Table_ID");
            sb.Append("                              AND tab.TableName = @tabName AND tab.IsActive = 'Y')");
            sb.Append("  LEFT OUTER JOIN AD_Ref_List_Trl rlt ON (rlt.AD_Ref_List_ID = rl.AD_Ref_List_ID");
            sb.Append("                                           AND rlt.AD_Language = @lang AND rlt.IsActive = 'Y')");
            sb.Append(" WHERE rl.Value = @val AND rl.IsActive = 'Y'");

            var sqlParams = new SqlParameter[]
            {
                new SqlParameter("@colName", columnName),
                new SqlParameter("@tabName", tableName),
                new SqlParameter("@lang",    language),
                new SqlParameter("@val",     value)
            };

            object result = DB.ExecuteScalar(sb.ToString(), sqlParams, null);
            if (result != null && result != DBNull.Value)
            {
                string label = Util.GetValueOfString(result);
                if (!string.IsNullOrWhiteSpace(label)) return label;
            }

            return value; // fallback to raw code
        }

        // ─────────────────────────────────────────────────────────
        // §4  GetWindowIdByTable — generic zoom helper
        // ─────────────────────────────────────────────────────────

        /// <summary>
        /// Returns the AD_Window_ID for a physical table name so the client can
        /// navigate to the correct VA window. Checks AD_Table.AD_Window_ID first;
        /// falls back to the window whose first-sequence tab covers that table.
        /// </summary>
        /// <param name="ctx">Current session context.</param>
        /// <param name="tableName">Physical table name (case-insensitive).</param>
        /// <returns>AD_Window_ID, or 0 when no window is found.</returns>
        public int GetWindowIdByTable(Ctx ctx, string tableName)
        {
            if (string.IsNullOrWhiteSpace(tableName)) return 0;
            string name = tableName.Trim();
            try
            {
                // Primary: AD_Window_ID set directly on the table record.
                var sb = new StringBuilder();
                sb.Append("SELECT t.AD_Window_ID");
                sb.Append("  FROM AD_Table t");
                sb.Append(" WHERE UPPER(t.TableName) = UPPER(@TableName)");
                sb.Append("   AND t.IsActive = 'Y'");
                sb.Append("   AND COALESCE(t.AD_Window_ID, 0) > 0");

                DataSet ds = DB.ExecuteDataset(
                    sb.ToString(),
                    new SqlParameter[] { new SqlParameter("@TableName", name) },
                    null);

                if (ds != null && ds.Tables.Count > 0 && ds.Tables[0].Rows.Count > 0)
                {
                    int wId = Util.GetValueOfInt(ds.Tables[0].Rows[0]["AD_Window_ID"]);
                    if (wId > 0) return wId;
                }

                // Fallback: window whose first-sequence tab sits on this table.
                var sb2 = new StringBuilder();
                sb2.Append("SELECT tb.AD_Window_ID");
                sb2.Append("  FROM AD_Tab tb");
                sb2.Append("  INNER JOIN AD_Table t ON (t.AD_Table_ID = tb.AD_Table_ID)");
                sb2.Append(" WHERE UPPER(t.TableName) = UPPER(@TableName)");
                sb2.Append("   AND tb.IsActive = 'Y'");
                sb2.Append("   AND t.IsActive = 'Y'");
                sb2.Append("   AND tb.SeqNo = (SELECT MIN(tb2.SeqNo)");
                sb2.Append("                     FROM AD_Tab tb2");
                sb2.Append("                    WHERE tb2.AD_Window_ID = tb.AD_Window_ID");
                sb2.Append("                      AND tb2.IsActive = 'Y')");
                sb2.Append(" ORDER BY tb.SeqNo, tb.AD_Tab_ID");

                ds = DB.ExecuteDataset(
                    sb2.ToString(),
                    new SqlParameter[] { new SqlParameter("@TableName", name) },
                    null);

                if (ds == null || ds.Tables.Count == 0 || ds.Tables[0].Rows.Count == 0) return 0;
                return Util.GetValueOfInt(ds.Tables[0].Rows[0]["AD_Window_ID"]);
            }
            catch (Exception ex)
            {
                _log.SaveError("VAS_290_ServiceContractRightPanelModel.GetWindowIdByTable", ex.Message);
                return 0;
            }
        }
    }
}
