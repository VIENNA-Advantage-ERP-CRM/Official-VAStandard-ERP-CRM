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
using VAdvantage.Model;
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

            int adClientId = ctx.GetAD_Client_ID();
            string language = ctx.GetAD_Language();

            try
            {
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
                sb.Append("       COALESCE(CAST(ol.Line AS VARCHAR(20)), N'') AS OrderLineNo,");
                sb.Append("       COALESCE(ol.Description, N'') AS OrderDescription,");
                sb.Append("       c.PriceEntered,");
                sb.Append("       c.PriceActual,");
                sb.Append("       c.PriceList AS PriceListAmount,");
                sb.Append("       c.Discount,");
                sb.Append("       c.TaxAmt,");
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

                response.id                    = Util.GetValueOfInt(row["C_Contract_ID"]);
                response.documentNo            = Util.GetValueOfString(row["DocumentNo"]);
                response.docStatus             = Util.GetValueOfString(row["DocStatus"]);
                response.processed             = Util.GetValueOfString(row["Processed"]) == "Y";
                response.contractType          = Util.GetValueOfString(row["ContractType"]);
                response.startDate             = row["StartDate"] != DBNull.Value ? Convert.ToDateTime(row["StartDate"]).ToString("yyyy-MM-dd") : null;
                response.endDate               = row["EndDate"]   != DBNull.Value ? Convert.ToDateTime(row["EndDate"]).ToString("yyyy-MM-dd")   : null;
                response.bPartnerId            = Util.GetValueOfInt(row["C_BPartner_ID"]);
                response.bPartnerName          = Util.GetValueOfString(row["BPartnerName"]);
                response.billingLocationName   = Util.GetValueOfString(row["BillingLocationName"]);
                response.billingLocationAddress = Util.GetValueOfString(row["BillingLocationAddress"]);
                response.productId             = Util.GetValueOfInt(row["M_Product_ID"]);
                response.productName           = Util.GetValueOfString(row["ProductName"]);
                response.attributeDisplay      = Util.GetValueOfString(row["AttributeDisplay"]);
                response.uomName               = Util.GetValueOfString(row["UOMName"]);
                response.frequencyName         = Util.GetValueOfString(row["FrequencyName"]);
                response.refContract           = Util.GetValueOfString(row["RefContract"]);
                response.refContractDocumentNo = Util.GetValueOfString(row["RefContractDocumentNo"]);
                response.orderDocumentNo       = Util.GetValueOfString(row["OrderDocumentNo"]);
                response.orderLineNo           = Util.GetValueOfString(row["OrderLineNo"]);
                response.orderDescription      = Util.GetValueOfString(row["OrderDescription"]);
                response.priceEntered          = row["PriceEntered"]   != DBNull.Value ? Convert.ToDecimal(row["PriceEntered"])   : (decimal?)null;
                response.priceActual           = row["PriceActual"]    != DBNull.Value ? Convert.ToDecimal(row["PriceActual"])    : (decimal?)null;
                response.priceListAmount       = row["PriceListAmount"] != DBNull.Value ? Convert.ToDecimal(row["PriceListAmount"]) : (decimal?)null;
                response.discount              = row["Discount"]       != DBNull.Value ? Convert.ToDecimal(row["Discount"])       : (decimal?)null;
                response.taxAmt                = row["TaxAmt"]         != DBNull.Value ? Convert.ToDecimal(row["TaxAmt"])         : (decimal?)null;
                response.lineNetAmt            = row["LineNetAmt"]     != DBNull.Value ? Convert.ToDecimal(row["LineNetAmt"])     : (decimal?)null;
                response.grandTotal            = row["GrandTotal"]     != DBNull.Value ? Convert.ToDecimal(row["GrandTotal"])     : (decimal?)null;
                response.priceListName         = Util.GetValueOfString(row["PriceListName"]);
                response.paymentTermName       = Util.GetValueOfString(row["PaymentTermName"]);
                response.currencyIsoCode       = Util.GetValueOfString(row["CurrencyISOCode"]);
                response.currencySymbol        = Util.GetValueOfString(row["CurrencySymbol"]);
                response.currencyPrecision     = row["CurrencyPrecision"] != DBNull.Value ? Util.GetValueOfInt(row["CurrencyPrecision"]) : 2;
                response.description           = Util.GetValueOfString(row["Description"]);
                response.renewalType           = Util.GetValueOfString(row["RenewalType"]);
                response.cancelBeforeDays      = row["CancelBeforeDays"] != DBNull.Value ? Util.GetValueOfInt(row["CancelBeforeDays"]) : (int?)null;
                response.cancellationDate      = row["CancellationDate"] != DBNull.Value ? Convert.ToDateTime(row["CancellationDate"]).ToString("yyyy-MM-dd") : null;
            }
            catch (Exception ex)
            {
                _log.SaveError("VAS_290_ServiceContractRightPanelModel.GetContractOverview.Main", ex.Message);
                response.error = "not_found";
                return response;
            }

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

            int adClientId = ctx.GetAD_Client_ID();

            // Use application date (ctx date) as the as-of date; never use SYSDATE/NOW
            // inside SQL so the same date governs all schedule-status decisions.
            DateTime? _ctxDate = CommonFunctions.CovertMilliToDate(ctx.GetContextAsTime("#Date"));
            DateTime asOfDate = _ctxDate.HasValue ? _ctxDate.Value.Date : DateTime.Now.Date;

            try
            {
                var sb = new StringBuilder();
                sb.Append("SELECT s.C_ContractSchedule_ID,");
                sb.Append("       s.C_Contract_ID,");
                sb.Append("       s.FROMDATE,");
                sb.Append("       s.EndDate,");
                sb.Append("       s.TotalAmt,");
                sb.Append("       s.TaxAmt,");
                sb.Append("       s.GrandTotal,");
                sb.Append("       s.C_Invoice_ID,");
                sb.Append("       COALESCE(i.DocumentNo, N'') AS InvoiceDocumentNo,");
                sb.Append("       CASE");
                sb.Append("           WHEN s.C_Invoice_ID IS NOT NULL THEN 'Invoiced'");
                sb.Append("           WHEN s.FROMDATE <= @asOfDate THEN 'Due'");
                sb.Append("           ELSE 'Scheduled'");
                sb.Append("       END AS BillingStatus");
                sb.Append("  FROM C_ContractSchedule s");
                sb.Append("  LEFT OUTER JOIN C_Invoice i ON (i.C_Invoice_ID = s.C_Invoice_ID AND i.IsActive = 'Y')");
                sb.Append(" WHERE s.C_Contract_ID = @contractId");
                sb.Append("   AND s.AD_Client_ID = @adClientId");
                sb.Append("   AND s.IsActive = 'Y'");
                sb.Append(" ORDER BY s.FROMDATE, s.C_ContractSchedule_ID");

                string baseSql = sb.ToString();
                string accessSql = MRole.GetDefault(ctx).AddAccessSQL(
                    baseSql, "s", MRole.SQL_FULLYQUALIFIED, MRole.SQL_RO);

                var sqlParams = new SqlParameter[]
                {
                    new SqlParameter("@contractId", contractId),
                    new SqlParameter("@adClientId", adClientId),
                    new SqlParameter("@asOfDate",   asOfDate.Date)
                };

                DataSet ds = DB.ExecuteDataset(accessSql, sqlParams, null);
                if (ds == null || ds.Tables.Count == 0)
                    return response;

                var items = new List<dynamic>();
                foreach (DataRow row in ds.Tables[0].Rows)
                {
                    dynamic item = new ExpandoObject();
                    item.id                  = Util.GetValueOfInt(row["C_ContractSchedule_ID"]);
                    item.contractId          = Util.GetValueOfInt(row["C_Contract_ID"]);
                    item.fromDate            = row["FROMDATE"] != DBNull.Value ? Convert.ToDateTime(row["FROMDATE"]).ToString("yyyy-MM-dd") : null;
                    item.endDate             = row["EndDate"]  != DBNull.Value ? Convert.ToDateTime(row["EndDate"]).ToString("yyyy-MM-dd")  : null;
                    item.totalAmt            = row["TotalAmt"]    != DBNull.Value ? Convert.ToDecimal(row["TotalAmt"])    : (decimal?)null;
                    item.taxAmt              = row["TaxAmt"]      != DBNull.Value ? Convert.ToDecimal(row["TaxAmt"])      : (decimal?)null;
                    item.grandTotal          = row["GrandTotal"]  != DBNull.Value ? Convert.ToDecimal(row["GrandTotal"])  : (decimal?)null;
                    item.cInvoiceId          = row["C_Invoice_ID"] != DBNull.Value ? Util.GetValueOfInt(row["C_Invoice_ID"]) : (int?)null;
                    item.invoiceDocumentNo   = Util.GetValueOfString(row["InvoiceDocumentNo"]);
                    item.billingStatus       = Util.GetValueOfString(row["BillingStatus"]);
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
