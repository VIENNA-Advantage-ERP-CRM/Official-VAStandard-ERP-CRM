/******************************************************
 * Module Name    : VASLogic
 * Purpose        : AR Invoice / AR Credit Note detail tab panel data
 *                  (header, lines, withholding, payment schedule,
 *                  allocations, delivery detail, approval, posted
 *                  journal), the Record Payment / Allocate Credit Note
 *                  write actions and the recurring-invoice schedule.
 * chronological  : Development
 *   VAI_145        Created  04 August 2026
 ******************************************************/

using System;
using System.Collections.Generic;
using System.Data;
using System.Data.SqlClient;
using VAdvantage.DataBase;
using VAdvantage.Logging;
using VAdvantage.Model;
using VAdvantage.Utility;

namespace VASLogic.Models
{
    /// <summary>
    /// Module Name : VAS_189_ARInvoiceDetailPanel
    /// Purpose     : Backing model for the AR invoice detail tab panel. Every
    ///               SELECT is filtered through MRole.AddAccessSQL on the MAIN
    ///               physical table alias only (i / p / al / fa); joined aliases
    ///               used for lookups inherit that authorization. Queries use
    ///               COALESCE + bind parameters so they run on both PostgreSQL
    ///               and Oracle.
    /// Chronological development:
    ///   VAI_145   04 August 2026
    /// </summary>
    public partial class VAS_189_ARInvoiceDetailPanelModel
    {
        private static VLogger log = VLogger.GetVLogger(typeof(VAS_189_ARInvoiceDetailPanelModel).FullName);

        // Server-side page sizes (mirrored by the frontend pagers).
        private const int SCHEDULE_PAGE_SIZE = 5;
        private const int JOURNAL_PAGE_SIZE = 10;
        private const int ONACCOUNT_PAGE_SIZE = 5;
        private const int ALLOCATION_PAGE_SIZE = 5;

        #region Panel (read) data

        /// <summary>
        /// Builds the full view model for an AR invoice or AR credit note:
        /// header, hero amounts, invoice details, lines, totals, withholding,
        /// payment schedule, allocations, delivery detail, posted journal and
        /// the recurring-schedule banner state. Returns an empty object
        /// (C_Invoice_ID = 0) when the user has no role access to the record.
        /// </summary>
        /// <param name="ctx">session context</param>
        /// <param name="C_Invoice_ID">target invoice</param>
        /// <param name="IsSOTrx">sales transaction flag supplied by the caller</param>
        /// <returns>panel view model</returns>
        public ARInvoicePanelData GetPanelData(Ctx ctx, int C_Invoice_ID, bool IsSOTrx)
        {
            ARInvoicePanelData data = new ARInvoicePanelData();
            if (C_Invoice_ID <= 0)
            {
                return data;
            }

            LoadHeader(ctx, C_Invoice_ID, IsSOTrx, data);
            if (data.C_Invoice_ID <= 0)
            {
                return data;   // no access / not found
            }

            data.Lines = LoadLines(C_Invoice_ID);
            data.Taxes = LoadTaxes(C_Invoice_ID);
            data.PaymentSchedule = LoadSchedule(C_Invoice_ID, 0, SCHEDULE_PAGE_SIZE);
            LoadScheduleAggregate(C_Invoice_ID, data);
            data.Allocations = LoadAllocations(ctx, C_Invoice_ID, 0, ALLOCATION_PAGE_SIZE);
            LoadAllocationAggregate(ctx, C_Invoice_ID, data);
            data.Delivery = LoadDelivery(C_Invoice_ID);
            data.PostedJournal = LoadPostedJournal(ctx, C_Invoice_ID, 0, JOURNAL_PAGE_SIZE, true);
            LoadWithholding(C_Invoice_ID, data);
            data.Recurring = LoadRecurringInfo(ctx, C_Invoice_ID);

            return data;
        }

        /// <summary>
        /// Loads the invoice header, business partner, currency, payment term,
        /// document type and representative. Sets the document-type flags and
        /// the open / net-receivable amounts used by the hero card.
        /// </summary>
        /// <param name="ctx">session context</param>
        /// <param name="C_Invoice_ID">target invoice</param>
        /// <param name="IsSOTrx">sales transaction flag supplied by the caller</param>
        /// <param name="data">view model being filled</param>
        private void LoadHeader(Ctx ctx, int C_Invoice_ID, bool IsSOTrx, ARInvoicePanelData data)
        {
            string sql = @"SELECT
                              i.C_Invoice_ID,
                              i.AD_Client_ID,
                              i.AD_Org_ID,
                              i.DocumentNo,
                              i.InvoiceReference AS InvoiceReference,
                              COALESCE(o.DocumentNo, N'') AS OrderDocumentNo,
                              i.DateInvoiced,
                              i.DateAcct,
                              i.C_BPartner_ID,
                              bp.Name AS BPartnerName,
                              bp.Value AS BPartnerValue,
                              bp.TaxID AS BPartnerTaxID,
                              COALESCE(cty.Name, loc.City) AS BPCity,
                              cntry.Name AS BPCountry,
                              loc.Postal,
                              i.C_Currency_ID,
                              i.C_ConversionType_ID,
                              cur.ISO_Code AS CurrencyISOCode,
                              cur.CurSymbol AS CurrencySymbol,
                              cur.StdPrecision AS StdPrecision,
                              i.C_PaymentTerm_ID,
                              pt.Name AS PaymentTermName,
                              pm.VA009_Name AS PaymentMethodName,
                              i.C_DocTypeTarget_ID,
                              dt.Name AS DocumentTypeName,
                              i.DocStatus,
                              i.Posted,
                              i.Processed,
                              i.IsApproved,
                              COALESCE(i.VAS_IsEmailSent, 'N') AS VAS_IsEmailSent,
                              i.Created,
                              cu.Name AS CreatedByName,
                              i.Updated,
                              uu.Name AS UpdatedByName,
                              i.GrandTotal,
                              i.TotalLines,
                              COALESCE(i.GrandTotalAfterWithholding, i.GrandTotal) AS NetReceivableAmount,
                              (i.GrandTotal - COALESCE(i.GrandTotalAfterWithholding, i.GrandTotal)) AS WithholdingAmount,
                              i.IsPaid,
                              i.IsSOTrx,
                              COALESCE(i.IsReturnTrx, 'N') AS IsReturnTrx,
                              i.SalesRep_ID,
                              u.Name AS RepresentativeName,
                              con.Name AS ContactName,
                              con.EMail AS ContactEMail,
                              (SELECT MIN(ips.DueDate)
                                 FROM C_InvoicePaySchedule ips
                                WHERE ips.C_Invoice_ID = i.C_Invoice_ID
                                  AND COALESCE(ips.VA009_IsPaid, 'N') = 'N') AS NextDueDate,
                              (SELECT COALESCE(SUM(ips.DueAmt), 0)
                                 FROM C_InvoicePaySchedule ips
                                WHERE ips.C_Invoice_ID = i.C_Invoice_ID
                                  AND COALESCE(ips.VA009_IsPaid, 'N') = 'N') AS OpenScheduleAmt,
                              (SELECT COUNT(1)
                                 FROM C_InvoicePaySchedule ips
                                WHERE ips.C_Invoice_ID = i.C_Invoice_ID) AS ScheduleCount,
                              acur.ISO_Code AS AcctCurrencyISO
                           FROM C_Invoice i
                           INNER JOIN C_BPartner bp ON (i.C_BPartner_ID=bp.C_BPartner_ID)
                           INNER JOIN C_Currency cur ON (i.C_Currency_ID=cur.C_Currency_ID)
                           INNER JOIN C_PaymentTerm pt ON (i.C_PaymentTerm_ID=pt.C_PaymentTerm_ID)
                           INNER JOIN VA009_PaymentMethod pm ON (i.VA009_PaymentMethod_ID=pm.VA009_PaymentMethod_ID)
                           INNER JOIN C_DocType dt ON (i.C_DocTypeTarget_ID=dt.C_DocType_ID)
                           INNER JOIN C_BPartner_Location bpl ON (i.C_BPartner_Location_ID=bpl.C_BPartner_Location_ID)
                           INNER JOIN AD_ClientInfo ci ON (i.AD_Client_ID=ci.AD_Client_ID)
                           INNER JOIN C_AcctSchema acs ON (ci.C_AcctSchema1_ID=acs.C_AcctSchema_ID)
                           INNER JOIN C_Currency acur ON (acs.C_Currency_ID=acur.C_Currency_ID)
                           LEFT OUTER JOIN C_Location loc ON (bpl.C_Location_ID=loc.C_Location_ID)
                           LEFT OUTER JOIN C_City cty ON (loc.C_City_ID=cty.C_City_ID)
                           LEFT OUTER JOIN C_Country cntry ON (loc.C_Country_ID=cntry.C_Country_ID)
                           LEFT OUTER JOIN AD_User u ON (i.SalesRep_ID=u.AD_User_ID)
                           LEFT OUTER JOIN AD_User con ON (i.AD_User_ID=con.AD_User_ID)
                           LEFT OUTER JOIN AD_User cu ON (i.CreatedBy=cu.AD_User_ID)
                           LEFT OUTER JOIN AD_User uu ON (i.UpdatedBy=uu.AD_User_ID)
                           LEFT OUTER JOIN C_Order o ON (i.C_Order_ID=o.C_Order_ID)
                           WHERE i.C_Invoice_ID=@C_Invoice_ID
                             AND i.IsSOTrx=@IsSOTrx
                             AND i.IsActive='Y'";

            sql = MRole.GetDefault(ctx).AddAccessSQL(
                sql, "i", MRole.SQL_FULLYQUALIFIED, MRole.SQL_RO);

            DataSet ds = DB.ExecuteDataset(sql, new SqlParameter[]
            {
                new SqlParameter("@C_Invoice_ID", C_Invoice_ID),
                new SqlParameter("@IsSOTrx", IsSOTrx ? "Y" : "N")
            }, null);
            if (ds == null || ds.Tables.Count == 0 || ds.Tables[0].Rows.Count == 0)
            {
                return;
            }

            DataRow r = ds.Tables[0].Rows[0];
            data.C_Invoice_ID = Util.GetValueOfInt(r["C_Invoice_ID"]);
            data.AD_Client_ID = Util.GetValueOfInt(r["AD_Client_ID"]);
            data.AD_Org_ID = Util.GetValueOfInt(r["AD_Org_ID"]);
            data.DocumentNo = Util.GetValueOfString(r["DocumentNo"]);
            data.InvoiceReference = Util.GetValueOfString(r["InvoiceReference"]);
            data.OrderDocumentNo = Util.GetValueOfString(r["OrderDocumentNo"]);
            data.DateInvoiced = Util.GetValueOfDateTime(r["DateInvoiced"]);
            data.DateAcct = Util.GetValueOfDateTime(r["DateAcct"]);
            data.C_BPartner_ID = Util.GetValueOfInt(r["C_BPartner_ID"]);
            data.BPName = Util.GetValueOfString(r["BPartnerName"]);
            data.BPValue = Util.GetValueOfString(r["BPartnerValue"]);
            data.BPTaxID = Util.GetValueOfString(r["BPartnerTaxID"]);
            data.BPCity = Util.GetValueOfString(r["BPCity"]);
            data.BPCountry = Util.GetValueOfString(r["BPCountry"]);
            data.BPPostal = Util.GetValueOfString(r["Postal"]);
            data.ContactName = Util.GetValueOfString(r["ContactName"]);
            data.ContactEMail = Util.GetValueOfString(r["ContactEMail"]);
            data.C_Currency_ID = Util.GetValueOfInt(r["C_Currency_ID"]);
            data.C_ConversionType_ID = Util.GetValueOfInt(r["C_ConversionType_ID"]);
            data.CurISO = Util.GetValueOfString(r["CurrencyISOCode"]);
            data.CurSymbol = Util.GetValueOfString(r["CurrencySymbol"]);
            data.StdPrecision = Util.GetValueOfInt(r["StdPrecision"]);
            data.PaymentTermName = Util.GetValueOfString(r["PaymentTermName"]);
            data.PaymentMethodName = Util.GetValueOfString(r["PaymentMethodName"]);
            data.DocTypeName = Util.GetValueOfString(r["DocumentTypeName"]);
            data.DocStatus = Util.GetValueOfString(r["DocStatus"]);
            data.Posted = Util.GetValueOfString(r["Posted"]);
            // Resolve the List-reference display names (DocStatus, Posted) from the
            // application dictionary so the UI shows translated text, not raw codes.
            data.DocStatusName = GetListReferenceName(ctx, "C_Invoice", "DocStatus", data.DocStatus);
            data.PostedName = GetListReferenceName(ctx, "C_Invoice", "Posted", data.Posted);
            data.Processed = Util.GetValueOfString(r["Processed"]) == "Y";
            data.IsApproved = Util.GetValueOfString(r["IsApproved"]) == "Y";
            /* Custom column - the invoice e-mail flag lives on C_Invoice, not on
               MInvoice, and there is no companion sent-date column in this schema. */
            data.IsEmailSent = Util.GetValueOfString(r["VAS_IsEmailSent"]) == "Y";
            // Reversing an invoice (ReverseCorrectIt) leaves DocStatus 'RE'; the panel
            // then reads "Reversed" wherever it would otherwise read "Completed".
            data.IsReversed = data.DocStatus == "RE";
            data.Created = Util.GetValueOfDateTime(r["Created"]);
            data.CreatedByName = Util.GetValueOfString(r["CreatedByName"]);
            data.Updated = Util.GetValueOfDateTime(r["Updated"]);
            data.ApprovedByName = Util.GetValueOfString(r["UpdatedByName"]);
            data.GrandTotal = Util.GetValueOfDecimal(r["GrandTotal"]);
            data.TotalLines = Util.GetValueOfDecimal(r["TotalLines"]);
            data.NetReceivable = Util.GetValueOfDecimal(r["NetReceivableAmount"]);
            data.WithholdingAmount = Util.GetValueOfDecimal(r["WithholdingAmount"]);
            data.TaxAmt = data.GrandTotal - data.TotalLines;
            data.IsPaid = Util.GetValueOfString(r["IsPaid"]) == "Y";
            data.IsSOTrx = Util.GetValueOfString(r["IsSOTrx"]) == "Y";
            data.IsReturnTrx = Util.GetValueOfString(r["IsReturnTrx"]) == "Y";
            data.RepresentativeName = Util.GetValueOfString(r["RepresentativeName"]);
            data.AcctCurISO = Util.GetValueOfString(r["AcctCurrencyISO"]);
            data.DueDate = Util.GetValueOfDateTime(r["NextDueDate"]);
            data.ScheduleCount = Util.GetValueOfInt(r["ScheduleCount"]);

            // AR credit note vs AR invoice (IsSOTrx already constrained by the caller).
            data.IsARCreditNote = data.IsReturnTrx;
            data.IsARInvoice = !data.IsReturnTrx;

            // Open amount = unpaid schedule total when schedules exist, else the net
            // receivable less anything already allocated to the invoice.
            decimal openSchedule = Util.GetValueOfDecimal(r["OpenScheduleAmt"]);
            if (data.ScheduleCount > 0)
            {
                data.OpenAmount = openSchedule;
            }
            else
            {
                data.OpenAmount = data.IsPaid ? 0m : Math.Abs(data.NetReceivable) - GetAllocatedAmount(C_Invoice_ID);
            }
            if (data.OpenAmount < 0)
            {
                data.OpenAmount = 0m;
            }

            // Editability per spec: DR/IP/IN and not processed = editable.
            data.IsEditable = (data.DocStatus == "DR" || data.DocStatus == "IP" || data.DocStatus == "IN")
                              && !data.Processed;

            // Due-day delta (computed in C# to stay DB-neutral).
            if (data.DueDate.HasValue)
            {
                int diff = (int)Math.Round((data.DueDate.Value.Date - DateTime.Today).TotalDays);
                data.IsOverdue = diff < 0;
                data.DaysDifference = Math.Abs(diff);
            }
        }

        /// <summary>
        /// Returns the absolute amount already allocated to an invoice from
        /// completed / active allocation lines, converted into the invoice
        /// currency. Used to derive the open amount when the invoice carries no
        /// pay schedule.
        /// </summary>
        /// <param name="C_Invoice_ID">target invoice</param>
        /// <returns>allocated amount in invoice currency</returns>
        private decimal GetAllocatedAmount(int C_Invoice_ID)
        {
            // Allocation lines are stored in the allocation header currency, which may
            // differ from the invoice currency (cross-currency apply). Convert each line
            // amount to the invoice currency on the allocation accounting date, using the
            // invoice conversion type and the allocation org, before summing.
            string sql = @"SELECT COALESCE(SUM(ABS(
                                 currencyConvert((al.Amount + al.WriteOffAmt + al.DiscountAmt), ah.C_Currency_ID, i.C_Currency_ID,
                                                 ah.DateAcct, i.C_ConversionType_ID,
                                                 ah.AD_Client_ID, ah.AD_Org_ID))), 0)
                             FROM C_AllocationLine al
                             INNER JOIN C_AllocationHdr ah ON (al.C_AllocationHdr_ID=ah.C_AllocationHdr_ID)
                             INNER JOIN C_Invoice i ON (al.C_Invoice_ID=i.C_Invoice_ID)
                            WHERE al.C_Invoice_ID=@C_Invoice_ID
                              AND al.IsActive='Y'
                              AND ah.IsActive='Y'
                              AND ah.DocStatus IN ('CO','CL')";
            return Util.GetValueOfDecimal(DB.ExecuteScalar(sql,
                new SqlParameter[] { new SqlParameter("@C_Invoice_ID", C_Invoice_ID) }, null));
        }

        /// <summary>
        /// Resolves the translated display name of a List-reference column value
        /// (e.g. DocStatus, Posted) from AD_Ref_List / AD_Ref_List_Trl for the
        /// session language. The AD_Column is located by TableName + ColumnName
        /// (stable across environments) rather than a hard-coded AD_Column_ID.
        /// </summary>
        /// <param name="ctx">session context (supplies the UI language)</param>
        /// <param name="tableName">physical table owning the column</param>
        /// <param name="columnName">list-reference column</param>
        /// <param name="code">stored short code</param>
        /// <returns>translated display name, or the code itself when unmapped</returns>
        private string GetListReferenceName(Ctx ctx, string tableName, string columnName, string code)
        {
            if (string.IsNullOrEmpty(code))
            {
                return code;
            }

            string sql = @"SELECT COALESCE(rlt.Name, rl.Name, rl.Value) AS DisplayName
                             FROM AD_Column c
                             INNER JOIN AD_Table t ON (t.AD_Table_ID=c.AD_Table_ID)
                             INNER JOIN AD_Ref_List rl ON (rl.AD_Reference_ID=c.AD_Reference_Value_ID)
                             LEFT OUTER JOIN AD_Ref_List_Trl rlt ON (rlt.AD_Ref_List_ID=rl.AD_Ref_List_ID
                                                                     AND rlt.AD_Language=@Language
                                                                     AND rlt.IsActive='Y')
                            WHERE t.TableName=@TableName
                              AND c.ColumnName=@ColumnName
                              AND c.IsActive='Y'
                              AND rl.IsActive='Y'
                              AND rl.Value=@Code";

            string name = Util.GetValueOfString(DB.ExecuteScalar(sql, new SqlParameter[]
            {
                new SqlParameter("@Language", ctx.GetAD_Language()),
                new SqlParameter("@TableName", tableName),
                new SqlParameter("@ColumnName", columnName),
                new SqlParameter("@Code", code)
            }, null));

            return string.IsNullOrEmpty(name) ? code : name;
        }

        /// <summary>Loads invoice lines with product / charge, UOM and tax.</summary>
        /// <param name="C_Invoice_ID">target invoice</param>
        /// <returns>ordered line rows</returns>
        private List<LineRow> LoadLines(int C_Invoice_ID)
        {
            List<LineRow> list = new List<LineRow>();
            string sql = @"SELECT
                              il.C_InvoiceLine_ID,
                              il.Line,
                              il.M_Product_ID,
                              pr.Name AS ProductName,
                              COALESCE(pr.ProductType, N'') AS ProductType,
                              COALESCE(pr.IsStocked, 'N') AS IsStocked,
                              il.C_Charge_ID,
                              ch.Name AS ChargeName,
                              il.Description,
                              il.QtyEntered,
                              il.C_UOM_ID,
                              uom.X12DE355 AS UOMSymbol,
                              il.PriceEntered,
                              il.LineNetAmt,
                              il.TaxAmt,
                              il.C_Tax_ID,
                              tax.Name AS TaxName,
                              tax.Rate AS TaxRate,
                              il.M_InOutLine_ID,
                              il.C_OrderLine_ID,
                              il.M_AttributeSetInstance_ID,
                              asi.Description AS ASIDescription,
                              CASE WHEN il.M_Product_ID IS NOT NULL AND il.M_Product_ID > 0
                                   THEN 'Y' ELSE 'N' END AS IsProductLine
                           FROM C_InvoiceLine il
                           LEFT OUTER JOIN M_Product pr ON (il.M_Product_ID=pr.M_Product_ID)
                           LEFT OUTER JOIN C_Charge ch ON (il.C_Charge_ID=ch.C_Charge_ID)
                           LEFT OUTER JOIN C_UOM uom ON (il.C_UOM_ID=uom.C_UOM_ID)
                           LEFT OUTER JOIN C_Tax tax ON (il.C_Tax_ID=tax.C_Tax_ID)
                           LEFT OUTER JOIN M_AttributeSetInstance asi ON (il.M_AttributeSetInstance_ID=asi.M_AttributeSetInstance_ID
                                                                          AND il.M_AttributeSetInstance_ID > 0)
                           WHERE il.C_Invoice_ID=@C_Invoice_ID
                             AND il.IsActive='Y'
                           ORDER BY il.Line";

            DataSet ds = DB.ExecuteDataset(sql,
                new SqlParameter[] { new SqlParameter("@C_Invoice_ID", C_Invoice_ID) }, null);
            if (ds == null || ds.Tables.Count == 0)
            {
                return list;
            }

            foreach (DataRow r in ds.Tables[0].Rows)
            {
                LineRow ln = new LineRow();
                ln.C_InvoiceLine_ID = Util.GetValueOfInt(r["C_InvoiceLine_ID"]);
                ln.M_Product_ID = Util.GetValueOfInt(r["M_Product_ID"]);
                ln.ProductName = Util.GetValueOfString(r["ProductName"]);
                ln.C_Charge_ID = Util.GetValueOfInt(r["C_Charge_ID"]);
                ln.ChargeName = Util.GetValueOfString(r["ChargeName"]);
                ln.Description = Util.GetValueOfString(r["Description"]);
                ln.QtyEntered = Util.GetValueOfDecimal(r["QtyEntered"]);
                ln.UOMSymbol = Util.GetValueOfString(r["UOMSymbol"]);
                ln.PriceEntered = Util.GetValueOfDecimal(r["PriceEntered"]);
                ln.LineNetAmt = Util.GetValueOfDecimal(r["LineNetAmt"]);
                ln.TaxAmt = Util.GetValueOfDecimal(r["TaxAmt"]);
                ln.TaxName = Util.GetValueOfString(r["TaxName"]);
                ln.TaxRate = Util.GetValueOfDecimal(r["TaxRate"]);
                ln.M_InOutLine_ID = Util.GetValueOfInt(r["M_InOutLine_ID"]);
                ln.C_OrderLine_ID = Util.GetValueOfInt(r["C_OrderLine_ID"]);
                ln.M_AttributeSetInstance_ID = Util.GetValueOfInt(r["M_AttributeSetInstance_ID"]);
                ln.ASIDescription = Util.GetValueOfString(r["ASIDescription"]);
                ln.IsProductLine = Util.GetValueOfString(r["IsProductLine"]) == "Y";
                // A physical item is a stocked ITEM product; services / expenses and
                // charge lines are not. Drives the recurring-eligibility banner and the
                // "Delivery type" read-out.
                ln.IsPhysicalItem = ln.IsProductLine
                                    && Util.GetValueOfString(r["ProductType"]) == "I"
                                    && Util.GetValueOfString(r["IsStocked"]) == "Y";
                list.Add(ln);
            }
            return list;
        }

        /// <summary>Loads the invoice tax summary rows.</summary>
        /// <param name="C_Invoice_ID">target invoice</param>
        /// <returns>tax rows</returns>
        private List<TaxRow> LoadTaxes(int C_Invoice_ID)
        {
            List<TaxRow> list = new List<TaxRow>();
            string sql = @"SELECT
                              it.C_Tax_ID,
                              tax.Name AS TaxName,
                              tax.Rate AS TaxRate,
                              it.TaxBaseAmt,
                              it.TaxAmt
                           FROM C_InvoiceTax it
                           INNER JOIN C_Tax tax ON (it.C_Tax_ID=tax.C_Tax_ID)
                           WHERE it.C_Invoice_ID=@C_Invoice_ID
                             AND it.IsActive='Y'
                           ORDER BY tax.Name";

            DataSet ds = DB.ExecuteDataset(sql,
                new SqlParameter[] { new SqlParameter("@C_Invoice_ID", C_Invoice_ID) }, null);
            if (ds == null || ds.Tables.Count == 0)
            {
                return list;
            }

            foreach (DataRow r in ds.Tables[0].Rows)
            {
                TaxRow t = new TaxRow();
                t.TaxName = Util.GetValueOfString(r["TaxName"]);
                t.TaxRate = Util.GetValueOfDecimal(r["TaxRate"]);
                t.TaxBaseAmt = Util.GetValueOfDecimal(r["TaxBaseAmt"]);
                t.TaxAmt = Util.GetValueOfDecimal(r["TaxAmt"]);
                list.Add(t);
            }
            return list;
        }

        /// <summary>
        /// Resolves the withholding type and rate from the line-level
        /// C_Withholding reference. Falls back to the header-derived amount when
        /// the withholding configuration tables are absent in the environment.
        /// </summary>
        /// <param name="C_Invoice_ID">target invoice</param>
        /// <param name="data">view model being filled</param>
        private void LoadWithholding(int C_Invoice_ID, ARInvoicePanelData data)
        {
            if (data.WithholdingAmount <= 0)
            {
                return;     // not applicable -> section hidden
            }

            data.Withholding = new WithholdingInfo
            {
                Amount = data.WithholdingAmount,
                Base = data.TotalLines,
                Rate = data.TotalLines != 0
                         ? Math.Round(data.WithholdingAmount / data.TotalLines * 100m, 2)
                         : 0m
            };

            // Best-effort enrichment from the withholding master. Wrapped because
            // C_Withholding / its columns may not exist in every environment.
            try
            {
                string sql = @"SELECT
                                  w.C_Withholding_ID,
                                  w.Name AS WithholdingName,
                                  MAX(w.InvPercentage) AS InvPercentage,
                                  SUM(CASE WHEN w.InvCalculation='T'
                                           THEN COALESCE(il.TaxAmt, 0)
                                           ELSE COALESCE(il.LineNetAmt, 0) END) AS BaseAmt
                               FROM C_InvoiceLine il
                               INNER JOIN C_Withholding w ON (il.C_Withholding_ID=w.C_Withholding_ID)
                               WHERE il.C_Invoice_ID=@C_Invoice_ID
                                 AND il.IsActive='Y'
                               GROUP BY w.C_Withholding_ID, w.Name";

                DataSet ds = DB.ExecuteDataset(sql,
                    new SqlParameter[] { new SqlParameter("@C_Invoice_ID", C_Invoice_ID) }, null);
                if (ds != null && ds.Tables.Count > 0 && ds.Tables[0].Rows.Count > 0)
                {
                    DataRow r = ds.Tables[0].Rows[0];
                    data.Withholding.TypeName = Util.GetValueOfString(r["WithholdingName"]);
                    decimal pct = Util.GetValueOfDecimal(r["InvPercentage"]);
                    decimal bAmt = Util.GetValueOfDecimal(r["BaseAmt"]);
                    if (pct != 0)
                    {
                        data.Withholding.Rate = pct;
                    }
                    if (bAmt != 0)
                    {
                        data.Withholding.Base = bAmt;
                    }
                }
            }
            catch (Exception ex)
            {
                log.Info("VAS_189 withholding enrichment skipped: " + ex.Message);
            }
        }

        /// <summary>
        /// Builds the DB-specific row-limit suffix for server-side paging. Oracle
        /// uses OFFSET/FETCH; PostgreSQL uses LIMIT/OFFSET. page / pageSize are
        /// integers (no injection risk), so they are inlined - some engines
        /// reject bound LIMIT parameters.
        /// </summary>
        /// <param name="page">zero-based page index</param>
        /// <param name="pageSize">rows per page</param>
        /// <returns>SQL suffix</returns>
        private string PagingSuffix(int page, int pageSize)
        {
            if (page < 0)
            {
                page = 0;
            }
            if (pageSize <= 0)
            {
                pageSize = 10;
            }
            int offset = page * pageSize;
            if (DB.IsOracle())
            {
                return " OFFSET " + offset + " ROWS FETCH NEXT " + pageSize + " ROWS ONLY";
            }
            return " LIMIT " + pageSize + " OFFSET " + offset;
        }

        /// <summary>Loads one page of invoice pay-schedule rows.</summary>
        /// <param name="C_Invoice_ID">target invoice</param>
        /// <param name="page">zero-based page index</param>
        /// <param name="pageSize">rows per page</param>
        /// <returns>schedule rows</returns>
        private List<ScheduleRow> LoadSchedule(int C_Invoice_ID, int page, int pageSize)
        {
            List<ScheduleRow> list = new List<ScheduleRow>();
            string sql = @"SELECT
                              ips.C_InvoicePaySchedule_ID,
                              ips.DueDate,
                              ips.DueAmt,
                              ips.DiscountDate,
                              ips.DiscountAmt,
                              COALESCE(ips.VA009_IsPaid, 'N') AS VA009_IsPaid,
                              COALESCE(ips.IsHoldPayment, 'N') AS IsHoldPayment
                           FROM C_InvoicePaySchedule ips
                           WHERE ips.C_Invoice_ID=@C_Invoice_ID
                             AND ips.IsActive='Y'
                           ORDER BY ips.DueDate, ips.C_InvoicePaySchedule_ID";
            sql += PagingSuffix(page, pageSize);

            DataSet ds = DB.ExecuteDataset(sql,
                new SqlParameter[] { new SqlParameter("@C_Invoice_ID", C_Invoice_ID) }, null);
            if (ds == null || ds.Tables.Count == 0)
            {
                return list;
            }

            foreach (DataRow r in ds.Tables[0].Rows)
            {
                ScheduleRow s = new ScheduleRow();
                s.C_InvoicePaySchedule_ID = Util.GetValueOfInt(r["C_InvoicePaySchedule_ID"]);
                s.DueDate = Util.GetValueOfDateTime(r["DueDate"]);
                s.DueAmt = Util.GetValueOfDecimal(r["DueAmt"]);
                s.DiscountAmt = Util.GetValueOfDecimal(r["DiscountAmt"]);
                s.IsPaid = Util.GetValueOfString(r["VA009_IsPaid"]) == "Y";
                s.IsHold = Util.GetValueOfString(r["IsHoldPayment"]) == "Y";
                s.Status = s.IsPaid ? "Paid" : (s.IsHold ? "OnHold" : "Open");
                list.Add(s);
            }
            return list;
        }

        /// <summary>
        /// Schedule footer aggregate (total count, settled / open amount, hold)
        /// over ALL schedules - the page rows alone cannot produce these.
        /// </summary>
        /// <param name="C_Invoice_ID">target invoice</param>
        /// <param name="data">view model being filled</param>
        private void LoadScheduleAggregate(int C_Invoice_ID, ARInvoicePanelData data)
        {
            DataSet ds = DB.ExecuteDataset(
                @"SELECT COUNT(ips.C_InvoicePaySchedule_ID) AS Total,
                         COALESCE(SUM(CASE WHEN COALESCE(ips.VA009_IsPaid,'N') <> 'Y' THEN ips.DueAmt ELSE 0 END), 0) AS OpenAmt,
                         COALESCE(SUM(CASE WHEN COALESCE(ips.VA009_IsPaid,'N') = 'Y' THEN ips.DueAmt ELSE 0 END), 0) AS PaidAmt,
                         COALESCE(SUM(CASE WHEN COALESCE(ips.VA009_IsPaid,'N') = 'Y' THEN 1 ELSE 0 END), 0) AS PaidCount,
                         MAX(CASE WHEN COALESCE(ips.IsHoldPayment,'N') = 'Y' THEN 1 ELSE 0 END) AS AnyHold
                    FROM C_InvoicePaySchedule ips
                   WHERE ips.C_Invoice_ID=@C_Invoice_ID AND ips.IsActive='Y'",
                new SqlParameter[] { new SqlParameter("@C_Invoice_ID", C_Invoice_ID) }, null);
            if (ds != null && ds.Tables.Count > 0 && ds.Tables[0].Rows.Count > 0)
            {
                DataRow r = ds.Tables[0].Rows[0];
                data.ScheduleTotal = Util.GetValueOfInt(r["Total"]);
                data.ScheduleOpenAmount = Util.GetValueOfDecimal(r["OpenAmt"]);
                data.ScheduleSettledAmount = Util.GetValueOfDecimal(r["PaidAmt"]);
                data.SchedulePaidCount = Util.GetValueOfInt(r["PaidCount"]);
                data.ScheduleAnyHold = Util.GetValueOfInt(r["AnyHold"]) == 1;
            }
        }

        /// <summary>Page of schedule rows for the server-side pager.</summary>
        /// <param name="C_Invoice_ID">target invoice</param>
        /// <param name="page">zero-based page index</param>
        /// <param name="pageSize">rows per page</param>
        /// <returns>schedule rows</returns>
        public List<ScheduleRow> GetSchedulePage(int C_Invoice_ID, int page, int pageSize)
        {
            return LoadSchedule(C_Invoice_ID, page, pageSize > 0 ? pageSize : SCHEDULE_PAGE_SIZE);
        }

        #endregion

        #region Allocations

        /// <summary>
        /// Loads one page of allocations applied to the invoice: the receipt /
        /// credit-note document that settled it, the date, the pay schedule it
        /// was applied to and the amount. Ordered newest first.
        /// </summary>
        /// <param name="ctx">session context</param>
        /// <param name="C_Invoice_ID">target invoice</param>
        /// <param name="page">zero-based page index</param>
        /// <param name="pageSize">rows per page</param>
        /// <returns>allocation rows</returns>
        private List<AllocationRow> LoadAllocations(Ctx ctx, int C_Invoice_ID, int page, int pageSize)
        {
            List<AllocationRow> list = new List<AllocationRow>();

            /* VA012 owns the cash-line reconciliation flag, so the column only
               exists where that module is installed. Selecting a constant keeps
               the query valid on an install without it (such a line simply reads
               as unreconciled). */
            string cashReconciled = Env.IsModuleInstalled("VA012_")
                ? "COALESCE(cl.VA012_IsReconciled, 'N')"
                : "'N'";

            string sql = @"SELECT
                              al.C_AllocationLine_ID,
                              al.C_InvoicePaySchedule_ID,
                              al.Amount,
                              al.DiscountAmt,
                              al.WriteOffAmt,
                              ah.C_AllocationHdr_ID,
                              ah.DocumentNo AS AllocationDocumentNo,
                              ah.DateTrx,
                              ah.DateAcct,
                              ah.C_Currency_ID,
                              acur.CurSymbol AS AllocCurSymbol,
                              acur.ISO_Code AS AllocCurISO,
                              acur.StdPrecision AS AllocStdPrecision,
                              p.C_Payment_ID,
                              p.DocumentNo AS PaymentDocumentNo,
                              p.TenderType,
                              COALESCE(p.IsReconciled, 'N') AS IsReconciled,
                              pdt.Name AS PaymentDocTypeName,
                              ppm.VA009_Name AS PaymentMethodName,
                              ci.C_Invoice_ID AS CreditInvoice_ID,
                              ci.DocumentNo AS CreditDocumentNo,
                              cidt.Name AS CreditDocTypeName,
                              cl.C_CashLine_ID,
                              csh.C_Cash_ID,
                              csh.DocumentNo AS CashDocumentNo,
                              cshdt.Name AS CashDocTypeName,
                              " + cashReconciled + @" AS CashIsReconciled,
                              j.GL_Journal_ID,
                              j.DocumentNo AS JournalDocumentNo,
                              jdt.Name AS JournalDocTypeName
                           FROM C_AllocationLine al
                           INNER JOIN C_AllocationHdr ah ON (al.C_AllocationHdr_ID=ah.C_AllocationHdr_ID)
                           INNER JOIN C_Currency acur ON (ah.C_Currency_ID=acur.C_Currency_ID)
                           LEFT OUTER JOIN C_Invoice ci ON (al.Ref_C_Invoice_ID=ci.C_Invoice_ID)
                           LEFT OUTER JOIN C_DocType cidt ON (ci.C_DocTypeTarget_ID=cidt.C_DocType_ID)
                           LEFT OUTER JOIN C_Payment p ON (al.C_Payment_ID=p.C_Payment_ID)
                           LEFT OUTER JOIN C_DocType pdt ON (p.C_DocType_ID=pdt.C_DocType_ID)
                           LEFT OUTER JOIN VA009_PaymentMethod ppm ON (p.VA009_PaymentMethod_ID=ppm.VA009_PaymentMethod_ID)
                           LEFT OUTER JOIN C_CashLine cl ON (al.C_CashLine_ID=cl.C_CashLine_ID AND cl.IsActive='Y')
                           LEFT OUTER JOIN C_Cash csh ON (cl.C_Cash_ID=csh.C_Cash_ID AND csh.IsActive='Y')
                           LEFT OUTER JOIN C_DocType cshdt ON (csh.C_DocType_ID=cshdt.C_DocType_ID)
                           LEFT OUTER JOIN GL_JournalLine jl ON (al.GL_JournalLine_ID=jl.GL_JournalLine_ID AND jl.IsActive='Y')
                           LEFT OUTER JOIN GL_Journal j ON (jl.GL_Journal_ID=j.GL_Journal_ID AND j.IsActive='Y')
                           LEFT OUTER JOIN C_DocType jdt ON (j.C_DocType_ID=jdt.C_DocType_ID)
                           WHERE al.C_Invoice_ID=@C_Invoice_ID
                             AND al.IsActive='Y'
                             AND ah.IsActive='Y'
                             AND ah.DocStatus IN ('CO','CL')";
            // Every settling-document lookup is a LEFT OUTER JOIN: an allocation line
            // references EXACTLY ONE of a payment, a cash-journal line or a credit
            // note, and the other columns are null. Joining any one of them with
            // INNER drops every row of the other kinds - which is why the footer
            // could count an allocation the grid never showed.
            //
            // ORDER BY is appended AFTER AddAccessSQL (and the paging suffix after
            // that) so the access predicate lands in the WHERE clause, not behind a
            // trailing clause.
            sql = MRole.GetDefault(ctx).AddAccessSQL(sql, "al", MRole.SQL_FULLYQUALIFIED, MRole.SQL_RO);
            sql += " ORDER BY ah.DateAcct DESC, al.C_AllocationLine_ID DESC";
            sql += PagingSuffix(page, pageSize);

            DataSet ds = DB.ExecuteDataset(sql,
                new SqlParameter[] { new SqlParameter("@C_Invoice_ID", C_Invoice_ID) }, null);
            if (ds == null || ds.Tables.Count == 0)
            {
                return list;
            }

            // Schedule ordinal ("Schedule 2") resolved once for the whole page.
            Dictionary<int, int> schedOrdinal = LoadScheduleOrdinals(C_Invoice_ID);

            foreach (DataRow r in ds.Tables[0].Rows)
            {
                AllocationRow a = new AllocationRow();
                a.C_Payment_ID = Util.GetValueOfInt(r["C_Payment_ID"]);
                a.C_CashLine_ID = Util.GetValueOfInt(r["C_CashLine_ID"]);
                a.C_Cash_ID = Util.GetValueOfInt(r["C_Cash_ID"]);
                a.GL_Journal_ID = Util.GetValueOfInt(r["GL_Journal_ID"]);
                a.Ref_C_Invoice_ID = Util.GetValueOfInt(r["CreditInvoice_ID"]);
                // Reference column: the settling document (receipt, cash journal,
                // GL journal or credit note); the allocation document number is the
                // fallback when none of them is set. A payment wins over a journal
                // line because MPayment.AllocateJournalLine stamps BOTH onto the same
                // allocation line and the payment is the document the user settled with.
                if (a.C_Payment_ID > 0)
                {
                    a.SourceType = "PAYMENT";
                    a.DocumentNo = Util.GetValueOfString(r["PaymentDocumentNo"]);
                    a.DocTypeName = Util.GetValueOfString(r["PaymentDocTypeName"]);
                    a.TenderType = Util.GetValueOfString(r["TenderType"]);
                    // Configured payment method, never derived from the tender code.
                    a.PaymentMethodName = Util.GetValueOfString(r["PaymentMethodName"]);
                    a.IsReconciled = Util.GetValueOfString(r["IsReconciled"]) == "Y";
                }
                else if (a.C_CashLine_ID > 0)
                {
                    // Cash-journal settlement: the reference is the journal, the method
                    // is cash by definition (C_CashLine carries no payment method), and
                    // reconciliation comes from the VA012 flag rather than C_Payment.
                    a.SourceType = "CASH";
                    a.DocumentNo = Util.GetValueOfString(r["CashDocumentNo"]);
                    a.DocTypeName = Util.GetValueOfString(r["CashDocTypeName"]);
                    a.IsReconciled = Util.GetValueOfString(r["CashIsReconciled"]) == "Y";
                }
                else if (a.GL_Journal_ID > 0)
                {
                    // GL-journal settlement (C_AllocationLine.GL_JournalLine_ID): the
                    // reference is the journal the line belongs to. A journal entry is
                    // not a payment, so it carries neither method nor reconciliation.
                    a.SourceType = "GLJOURNAL";
                    a.DocumentNo = Util.GetValueOfString(r["JournalDocumentNo"]);
                    a.DocTypeName = Util.GetValueOfString(r["JournalDocTypeName"]);
                }
                else if (a.Ref_C_Invoice_ID > 0)
                {
                    a.SourceType = "CREDITNOTE";
                    a.DocumentNo = Util.GetValueOfString(r["CreditDocumentNo"]);
                    a.DocTypeName = Util.GetValueOfString(r["CreditDocTypeName"]);
                }
                else
                {
                    a.SourceType = "ALLOCATION";
                    a.DocumentNo = Util.GetValueOfString(r["AllocationDocumentNo"]);
                }
                a.AllocationDocumentNo = Util.GetValueOfString(r["AllocationDocumentNo"]);
                /* The header the line belongs to — the record the allocation
                   number links to (window VAS_ViewAllocation). */
                a.C_AllocationHdr_ID = Util.GetValueOfInt(r["C_AllocationHdr_ID"]);
                a.Date = Util.GetValueOfDateTime(r["DateTrx"]);
                a.Amount = Math.Abs(Util.GetValueOfDecimal(r["Amount"]));
                a.DiscountAmt = Math.Abs(Util.GetValueOfDecimal(r["DiscountAmt"]));
                a.WriteOffAmt = Math.Abs(Util.GetValueOfDecimal(r["WriteOffAmt"]));
                a.CurSymbol = Util.GetValueOfString(r["AllocCurSymbol"]);
                a.CurISO = Util.GetValueOfString(r["AllocCurISO"]);
                a.StdPrecision = Util.GetValueOfInt(r["AllocStdPrecision"]);

                int schedId = Util.GetValueOfInt(r["C_InvoicePaySchedule_ID"]);
                a.C_InvoicePaySchedule_ID = schedId;
                if (schedId > 0 && schedOrdinal.ContainsKey(schedId))
                {
                    a.ScheduleNo = schedOrdinal[schedId];
                }
                list.Add(a);
            }
            return list;
        }

        /// <summary>
        /// Maps every pay schedule of the invoice to its 1-based ordinal so an
        /// allocation row can read "Schedule 2" instead of a surrogate id.
        /// </summary>
        /// <param name="C_Invoice_ID">target invoice</param>
        /// <returns>schedule id -> ordinal</returns>
        private Dictionary<int, int> LoadScheduleOrdinals(int C_Invoice_ID)
        {
            Dictionary<int, int> map = new Dictionary<int, int>();
            DataSet ds = DB.ExecuteDataset(
                @"SELECT ips.C_InvoicePaySchedule_ID
                    FROM C_InvoicePaySchedule ips
                   WHERE ips.C_Invoice_ID=@C_Invoice_ID AND ips.IsActive='Y'
                   ORDER BY ips.DueDate, ips.C_InvoicePaySchedule_ID",
                new SqlParameter[] { new SqlParameter("@C_Invoice_ID", C_Invoice_ID) }, null);
            if (ds == null || ds.Tables.Count == 0)
            {
                return map;
            }
            int idx = 1;
            foreach (DataRow r in ds.Tables[0].Rows)
            {
                int id = Util.GetValueOfInt(r["C_InvoicePaySchedule_ID"]);
                if (id > 0 && !map.ContainsKey(id))
                {
                    map.Add(id, idx);
                }
                idx++;
            }
            return map;
        }

        /// <summary>
        /// Allocation footer aggregate (row count, allocated / discount /
        /// write-off totals) over ALL allocation lines of the invoice.
        /// </summary>
        /// <param name="ctx">session context</param>
        /// <param name="C_Invoice_ID">target invoice</param>
        /// <param name="data">view model being filled</param>
        private void LoadAllocationAggregate(Ctx ctx, int C_Invoice_ID, ARInvoicePanelData data)
        {
            string sql = @"SELECT COUNT(al.C_AllocationLine_ID) AS Total,
                                  COALESCE(SUM(ABS(al.Amount)), 0) AS TotAlloc,
                                  COALESCE(SUM(ABS(al.DiscountAmt)), 0) AS TotDisc,
                                  COALESCE(SUM(ABS(al.WriteOffAmt)), 0) AS TotWriteOff
                             FROM C_AllocationLine al
                             INNER JOIN C_AllocationHdr ah ON (al.C_AllocationHdr_ID=ah.C_AllocationHdr_ID)
                            WHERE al.C_Invoice_ID=@C_Invoice_ID
                              AND al.IsActive='Y'
                              AND ah.IsActive='Y'
                              AND ah.DocStatus IN ('CO','CL')";
            sql = MRole.GetDefault(ctx).AddAccessSQL(sql, "al", MRole.SQL_FULLYQUALIFIED, MRole.SQL_RO);

            DataSet ds = DB.ExecuteDataset(sql,
                new SqlParameter[] { new SqlParameter("@C_Invoice_ID", C_Invoice_ID) }, null);
            if (ds != null && ds.Tables.Count > 0 && ds.Tables[0].Rows.Count > 0)
            {
                DataRow r = ds.Tables[0].Rows[0];
                data.AllocationTotal = Util.GetValueOfInt(r["Total"]);
                data.AllocationAmount = Util.GetValueOfDecimal(r["TotAlloc"]);
                data.AllocationDiscount = Util.GetValueOfDecimal(r["TotDisc"]);
                data.AllocationWriteOff = Util.GetValueOfDecimal(r["TotWriteOff"]);
            }
        }

        /// <summary>Page of allocation rows for the server-side pager.</summary>
        /// <param name="ctx">session context</param>
        /// <param name="C_Invoice_ID">target invoice</param>
        /// <param name="page">zero-based page index</param>
        /// <param name="pageSize">rows per page</param>
        /// <returns>allocation rows</returns>
        public List<AllocationRow> GetAllocationsPage(Ctx ctx, int C_Invoice_ID, int page, int pageSize)
        {
            return LoadAllocations(ctx, C_Invoice_ID, page, pageSize > 0 ? pageSize : ALLOCATION_PAGE_SIZE);
        }

        #endregion

        #region Delivery detail

        /// <summary>
        /// Loads the delivery (shipment) detail for the invoice: the linked
        /// customer shipment(s), sales order, delivery dates, warehouse and the
        /// delivered quantity. Lines with no shipment link contribute only to
        /// the "delivery type" verdict, so a services-only invoice still renders
        /// the block with "No physical items detected".
        /// </summary>
        /// <param name="C_Invoice_ID">target invoice</param>
        /// <returns>delivery info (never null)</returns>
        private DeliveryInfo LoadDelivery(int C_Invoice_ID)
        {
            DeliveryInfo dl = new DeliveryInfo { Rows = new List<DeliveryRow>() };
            string sql = @"SELECT
                              il.C_InvoiceLine_ID,
                              pr.Name AS ProductName,
                              il.QtyEntered AS InvoiceQty,
                              io.DocumentNo AS ShipmentDocumentNo,
                              io.MovementDate AS DeliveredDate,
                              iol.MovementQty AS DeliveredQty,
                              wh.Name AS WarehouseName,
                              o.DocumentNo AS OrderDocumentNo,
                              con.Name AS DeliveredToName,
                              (ABS(il.QtyEntered) - COALESCE(ABS(iol.MovementQty), ABS(il.QtyEntered))) AS QuantityVariance
                           FROM C_InvoiceLine il
                           INNER JOIN M_Product pr ON (il.M_Product_ID=pr.M_Product_ID)
                           INNER JOIN M_InOutLine iol ON (il.M_InOutLine_ID=iol.M_InOutLine_ID)
                           INNER JOIN M_InOut io ON (iol.M_InOut_ID=io.M_InOut_ID)
                           INNER JOIN M_Warehouse wh ON (io.M_Warehouse_ID=wh.M_Warehouse_ID)
                           LEFT OUTER JOIN AD_User con ON (io.AD_User_ID=con.AD_User_ID)
                           LEFT OUTER JOIN C_OrderLine ol ON (il.C_OrderLine_ID=ol.C_OrderLine_ID)
                           LEFT OUTER JOIN C_Order o ON (ol.C_Order_ID=o.C_Order_ID)
                           WHERE il.C_Invoice_ID=@C_Invoice_ID
                             AND il.IsActive='Y'
                             AND il.M_Product_ID IS NOT NULL
                             AND il.M_InOutLine_ID IS NOT NULL
                           ORDER BY il.Line";

            DataSet ds = DB.ExecuteDataset(sql,
                new SqlParameter[] { new SqlParameter("@C_Invoice_ID", C_Invoice_ID) }, null);
            if (ds == null || ds.Tables.Count == 0)
            {
                return dl;
            }

            decimal totalQty = 0m, totalVar = 0m;
            // Distinct shipment / order documents, warehouses and delivery dates across
            // the matched lines, kept in first-seen order for a comma-separated read-out.
            List<string> shipDocs = new List<string>();
            List<string> orderDocs = new List<string>();
            List<string> warehouses = new List<string>();
            List<string> contacts = new List<string>();
            List<DateTime> dates = new List<DateTime>();

            foreach (DataRow r in ds.Tables[0].Rows)
            {
                DeliveryRow row = new DeliveryRow();
                row.ProductName = Util.GetValueOfString(r["ProductName"]);
                row.ShipmentDocumentNo = Util.GetValueOfString(r["ShipmentDocumentNo"]);
                row.OrderDocumentNo = Util.GetValueOfString(r["OrderDocumentNo"]);
                row.DeliveredDate = Util.GetValueOfDateTime(r["DeliveredDate"]);
                row.DeliveredQty = Util.GetValueOfDecimal(r["DeliveredQty"]);
                row.WarehouseName = Util.GetValueOfString(r["WarehouseName"]);
                row.QuantityVariance = Util.GetValueOfDecimal(r["QuantityVariance"]);
                dl.Rows.Add(row);

                // Quantities are compared as magnitudes: a credit-memo line carries a
                // negative QtyEntered, and subtracting it from a positive delivered
                // quantity would ADD the two instead of differencing them.
                totalQty += Math.Abs(row.DeliveredQty);
                totalVar += Math.Abs(row.QuantityVariance);

                string contact = Util.GetValueOfString(r["DeliveredToName"]);
                if (!string.IsNullOrEmpty(row.ShipmentDocumentNo) && !shipDocs.Contains(row.ShipmentDocumentNo))
                {
                    shipDocs.Add(row.ShipmentDocumentNo);
                }
                if (!string.IsNullOrEmpty(row.OrderDocumentNo) && !orderDocs.Contains(row.OrderDocumentNo))
                {
                    orderDocs.Add(row.OrderDocumentNo);
                }
                if (!string.IsNullOrEmpty(row.WarehouseName) && !warehouses.Contains(row.WarehouseName))
                {
                    warehouses.Add(row.WarehouseName);
                }
                if (!string.IsNullOrEmpty(contact) && !contacts.Contains(contact))
                {
                    contacts.Add(contact);
                }
                if (row.DeliveredDate.HasValue && !dates.Contains(row.DeliveredDate.Value.Date))
                {
                    dates.Add(row.DeliveredDate.Value.Date);
                }
            }

            dl.ShipmentDocumentNo = string.Join(", ", shipDocs.ToArray());
            dl.OrderDocumentNo = string.Join(", ", orderDocs.ToArray());
            dl.WarehouseName = string.Join(", ", warehouses.ToArray());
            dl.AcknowledgedBy = string.Join(", ", contacts.ToArray());
            dl.DeliveredDates = dates;
            dl.DeliveredDate = dates.Count > 0 ? (DateTime?)dates[0] : null;
            dl.LineCount = dl.Rows.Count;
            dl.TotalDelivered = totalQty;
            dl.QtyVariance = totalVar;
            dl.IsFullyDelivered = dl.Rows.Count > 0 && totalVar == 0m;
            return dl;
        }

        #endregion

        #region Posted journal

        /// <summary>Loads posted accounting facts for the invoice.</summary>
        /// <param name="ctx">session context</param>
        /// <param name="C_Invoice_ID">target invoice</param>
        /// <param name="page">zero-based page index</param>
        /// <param name="pageSize">rows per page</param>
        /// <param name="includeTotals">compute the footer aggregate as well</param>
        /// <returns>posted journal info</returns>
        private PostedJournalInfo LoadPostedJournal(Ctx ctx, int C_Invoice_ID, int page, int pageSize, bool includeTotals)
        {
            PostedJournalInfo pj = new PostedJournalInfo { Rows = new List<JournalRow>() };
            string sql = @"SELECT
                              fa.DateAcct,
                              ev.Value AS AccountValue,
                              ev.Name AS AccountName,
                              fa.AmtAcctDr,
                              fa.AmtAcctCr,
                              fa.Description,
                              org.Name AS OrgName,
                              bp.Name AS BPName,
                              pr.Name AS ProductName,
                              orgtrx.Name AS OrgTrxName,
                              p.Name AS PeriodName,
                              cur.CurSymbol AS AcctCurSymbol,
                              cur.StdPrecision AS AcctStdPrecision
                           FROM Fact_Acct fa
                           INNER JOIN C_ElementValue ev ON (fa.Account_ID=ev.C_ElementValue_ID)
                           INNER JOIN AD_Org org ON (fa.AD_Org_ID=org.AD_Org_ID)
                           INNER JOIN C_Period p ON (fa.C_Period_ID=p.C_Period_ID)
                           INNER JOIN AD_ClientInfo ci ON (ci.AD_Client_ID=fa.AD_Client_ID)
                           INNER JOIN C_AcctSchema acs ON (acs.C_AcctSchema_ID=ci.C_AcctSchema1_ID)
                           INNER JOIN C_Currency cur ON (cur.C_Currency_ID=acs.C_Currency_ID)
                           LEFT OUTER JOIN C_BPartner bp ON (fa.C_BPartner_ID=bp.C_BPartner_ID)
                           LEFT OUTER JOIN M_Product pr ON (fa.M_Product_ID=pr.M_Product_ID)
                           LEFT OUTER JOIN AD_Org orgtrx ON (fa.AD_OrgTrx_ID=orgtrx.AD_Org_ID)
                           WHERE fa.AD_Table_ID=(SELECT AD_Table_ID FROM AD_Table WHERE TableName='C_Invoice')
                             AND fa.Record_ID=@C_Invoice_ID
                             AND fa.C_AcctSchema_ID=ci.C_AcctSchema1_ID
                           ORDER BY fa.AmtAcctDr DESC, fa.Fact_Acct_ID";

            sql = MRole.GetDefault(ctx).AddAccessSQL(
                sql, "fa", MRole.SQL_FULLYQUALIFIED, MRole.SQL_RO);
            sql += PagingSuffix(page, pageSize);

            DataSet ds = DB.ExecuteDataset(sql,
                new SqlParameter[] { new SqlParameter("@C_Invoice_ID", C_Invoice_ID) }, null);
            if (ds != null && ds.Tables.Count > 0)
            {
                foreach (DataRow r in ds.Tables[0].Rows)
                {
                    JournalRow jr = new JournalRow();
                    jr.AccountValue = Util.GetValueOfString(r["AccountValue"]);
                    jr.AccountName = Util.GetValueOfString(r["AccountName"]);
                    jr.Description = Util.GetValueOfString(r["Description"]);
                    jr.OrgName = Util.GetValueOfString(r["OrgName"]);
                    jr.BPName = Util.GetValueOfString(r["BPName"]);
                    jr.ProductName = Util.GetValueOfString(r["ProductName"]);
                    jr.OrgTrxName = Util.GetValueOfString(r["OrgTrxName"]);
                    jr.AmtAcctDr = Util.GetValueOfDecimal(r["AmtAcctDr"]);
                    jr.AmtAcctCr = Util.GetValueOfDecimal(r["AmtAcctCr"]);
                    pj.Rows.Add(jr);
                    if (!pj.PostingDate.HasValue)
                    {
                        pj.PostingDate = Util.GetValueOfDateTime(r["DateAcct"]);
                    }
                    if (string.IsNullOrEmpty(pj.PeriodName))
                    {
                        pj.PeriodName = Util.GetValueOfString(r["PeriodName"]);
                    }
                    if (string.IsNullOrEmpty(pj.CurSymbol))
                    {
                        pj.CurSymbol = Util.GetValueOfString(r["AcctCurSymbol"]);
                        pj.StdPrecision = Util.GetValueOfInt(r["AcctStdPrecision"]);
                    }
                }
            }

            // Footer totals + count over ALL fact lines (independent of the page).
            // These do not change between pages, so they are computed ONLY on the
            // initial load; page fetches skip this aggregate query.
            if (includeTotals)
            {
                string aggSql = @"SELECT COUNT(fa.Fact_Acct_ID) AS Total,
                                         COALESCE(SUM(fa.AmtAcctDr), 0) AS TotDr,
                                         COALESCE(SUM(fa.AmtAcctCr), 0) AS TotCr
                                    FROM Fact_Acct fa
                                    INNER JOIN AD_ClientInfo ci ON (ci.AD_Client_ID=fa.AD_Client_ID)
                                   WHERE fa.AD_Table_ID=(SELECT AD_Table_ID FROM AD_Table WHERE TableName='C_Invoice')
                                     AND fa.Record_ID=@C_Invoice_ID
                                     AND fa.C_AcctSchema_ID=ci.C_AcctSchema1_ID";
                aggSql = MRole.GetDefault(ctx).AddAccessSQL(aggSql, "fa", MRole.SQL_FULLYQUALIFIED, MRole.SQL_RO);
                DataSet ads = DB.ExecuteDataset(aggSql,
                    new SqlParameter[] { new SqlParameter("@C_Invoice_ID", C_Invoice_ID) }, null);
                if (ads != null && ads.Tables.Count > 0 && ads.Tables[0].Rows.Count > 0)
                {
                    DataRow ar = ads.Tables[0].Rows[0];
                    pj.Total = Util.GetValueOfInt(ar["Total"]);
                    pj.TotalDr = Util.GetValueOfDecimal(ar["TotDr"]);
                    pj.TotalCr = Util.GetValueOfDecimal(ar["TotCr"]);
                }
            }
            return pj;
        }

        /// <summary>
        /// Page of posted-journal rows for the pager (rows only - totals / count
        /// are already on the client from the initial load).
        /// </summary>
        /// <param name="ctx">session context</param>
        /// <param name="C_Invoice_ID">target invoice</param>
        /// <param name="page">zero-based page index</param>
        /// <param name="pageSize">rows per page</param>
        /// <returns>posted journal info</returns>
        public PostedJournalInfo GetPostedJournalPage(Ctx ctx, int C_Invoice_ID, int page, int pageSize)
        {
            return LoadPostedJournal(ctx, C_Invoice_ID, page, pageSize > 0 ? pageSize : JOURNAL_PAGE_SIZE, false);
        }

        #endregion

        #region DTOs (read)

        public class ARInvoicePanelData
        {
            public int C_Invoice_ID { get; set; }
            public int AD_Client_ID { get; set; }
            public int AD_Org_ID { get; set; }
            public string DocumentNo { get; set; }
            public string InvoiceReference { get; set; }
            public string OrderDocumentNo { get; set; }
            public DateTime? DateInvoiced { get; set; }
            public DateTime? DateAcct { get; set; }
            public DateTime? DueDate { get; set; }
            public int C_BPartner_ID { get; set; }
            public string BPName { get; set; }
            public string BPValue { get; set; }
            public string BPTaxID { get; set; }
            public string BPCity { get; set; }
            public string BPCountry { get; set; }
            public string BPPostal { get; set; }
            /// <summary>Invoice contact - seeds the Send Invoice recipient.</summary>
            public string ContactName { get; set; }
            public string ContactEMail { get; set; }
            public int C_Currency_ID { get; set; }
            public int C_ConversionType_ID { get; set; }
            public string CurISO { get; set; }
            public string CurSymbol { get; set; }
            public string AcctCurISO { get; set; }
            public int StdPrecision { get; set; }
            public string PaymentTermName { get; set; }
            public string PaymentMethodName { get; set; }
            public string DocTypeName { get; set; }
            public string DocStatus { get; set; }
            public string DocStatusName { get; set; }
            public string Posted { get; set; }
            public string PostedName { get; set; }
            public bool Processed { get; set; }
            public bool IsApproved { get; set; }
            /// <summary>C_Invoice.VAS_IsEmailSent — the invoice was e-mailed to the customer.</summary>
            public bool IsEmailSent { get; set; }
            /// <summary>Document was reversed (ReverseCorrectIt leaves DocStatus 'RE').</summary>
            public bool IsReversed { get; set; }
            public DateTime? Created { get; set; }
            public string CreatedByName { get; set; }
            public DateTime? Updated { get; set; }
            public string ApprovedByName { get; set; }
            public string RepresentativeName { get; set; }
            public decimal GrandTotal { get; set; }
            public decimal TotalLines { get; set; }
            public decimal TaxAmt { get; set; }
            public decimal NetReceivable { get; set; }
            public decimal WithholdingAmount { get; set; }
            public decimal OpenAmount { get; set; }
            public bool IsPaid { get; set; }
            public bool IsSOTrx { get; set; }
            public bool IsReturnTrx { get; set; }
            public bool IsARCreditNote { get; set; }
            public bool IsARInvoice { get; set; }
            public bool IsEditable { get; set; }
            public bool IsOverdue { get; set; }
            public int DaysDifference { get; set; }
            public int ScheduleCount { get; set; }

            public List<LineRow> Lines { get; set; }
            public List<TaxRow> Taxes { get; set; }
            public WithholdingInfo Withholding { get; set; }

            public List<ScheduleRow> PaymentSchedule { get; set; }   // first page only
            // Schedule footer aggregates over ALL schedules (server-side paging).
            public int ScheduleTotal { get; set; }
            public decimal ScheduleOpenAmount { get; set; }
            public decimal ScheduleSettledAmount { get; set; }
            public int SchedulePaidCount { get; set; }
            public bool ScheduleAnyHold { get; set; }

            public List<AllocationRow> Allocations { get; set; }     // first page only
            public int AllocationTotal { get; set; }
            public decimal AllocationAmount { get; set; }
            public decimal AllocationDiscount { get; set; }
            public decimal AllocationWriteOff { get; set; }

            public DeliveryInfo Delivery { get; set; }
            public PostedJournalInfo PostedJournal { get; set; }
            public RecurringInfo Recurring { get; set; }
        }

        public class LineRow
        {
            public int C_InvoiceLine_ID { get; set; }
            public int M_Product_ID { get; set; }
            public string ProductName { get; set; }
            public int C_Charge_ID { get; set; }
            public string ChargeName { get; set; }
            public string Description { get; set; }
            public decimal QtyEntered { get; set; }
            public string UOMSymbol { get; set; }
            public decimal PriceEntered { get; set; }
            public decimal LineNetAmt { get; set; }
            public decimal TaxAmt { get; set; }
            public string TaxName { get; set; }
            public decimal TaxRate { get; set; }
            public int M_InOutLine_ID { get; set; }
            public int C_OrderLine_ID { get; set; }
            public int M_AttributeSetInstance_ID { get; set; }
            public string ASIDescription { get; set; }
            public bool IsProductLine { get; set; }
            /// <summary>Stocked ITEM product (drives recurring eligibility).</summary>
            public bool IsPhysicalItem { get; set; }
        }

        public class TaxRow
        {
            public string TaxName { get; set; }
            public decimal TaxRate { get; set; }
            public decimal TaxBaseAmt { get; set; }
            public decimal TaxAmt { get; set; }
        }

        public class WithholdingInfo
        {
            public string TypeName { get; set; }
            public decimal Base { get; set; }
            public decimal Rate { get; set; }
            public decimal Amount { get; set; }
        }

        public class ScheduleRow
        {
            public int C_InvoicePaySchedule_ID { get; set; }
            public DateTime? DueDate { get; set; }
            public decimal DueAmt { get; set; }
            public decimal DiscountAmt { get; set; }
            public bool IsPaid { get; set; }
            public bool IsHold { get; set; }
            public string Status { get; set; }   // Paid | OnHold | Open
        }

        public class AllocationRow
        {
            public string SourceType { get; set; }   // PAYMENT | CASH | GLJOURNAL | CREDITNOTE | ALLOCATION
            public int C_Payment_ID { get; set; }
            /// <summary>Cash-journal line that settled the invoice; 0 for every other source.</summary>
            public int C_CashLine_ID { get; set; }
            /// <summary>Cash journal owning that line — the zoom target (window VAS_CashJournal).</summary>
            public int C_Cash_ID { get; set; }
            /// <summary>GL journal behind C_AllocationLine.GL_JournalLine_ID — the zoom target (window VAS_GLJournal).</summary>
            public int GL_Journal_ID { get; set; }
            public int Ref_C_Invoice_ID { get; set; }
            public string DocumentNo { get; set; }
            public string DocTypeName { get; set; }
            public string AllocationDocumentNo { get; set; }
            /// <summary>Allocation header the line belongs to — the zoom target behind the allocation number (window VAS_ViewAllocation).</summary>
            public int C_AllocationHdr_ID { get; set; }
            public string TenderType { get; set; }
            /// <summary>VA009_PaymentMethod.VA009_Name of the settling payment; empty for the other sources.</summary>
            public string PaymentMethodName { get; set; }
            public bool IsReconciled { get; set; }
            public DateTime? Date { get; set; }
            public decimal Amount { get; set; }
            public decimal DiscountAmt { get; set; }
            public decimal WriteOffAmt { get; set; }
            public int C_InvoicePaySchedule_ID { get; set; }
            /// <summary>1-based schedule ordinal ("Schedule 2"); 0 when unlinked.</summary>
            public int ScheduleNo { get; set; }
            public string CurSymbol { get; set; }
            public string CurISO { get; set; }
            public int StdPrecision { get; set; }
        }

        public class DeliveryInfo
        {
            public bool IsFullyDelivered { get; set; }
            public string ShipmentDocumentNo { get; set; }
            public string OrderDocumentNo { get; set; }
            public DateTime? DeliveredDate { get; set; }
            public List<DateTime> DeliveredDates { get; set; }
            public decimal TotalDelivered { get; set; }
            public string WarehouseName { get; set; }
            public string AcknowledgedBy { get; set; }
            public decimal QtyVariance { get; set; }
            public int LineCount { get; set; }
            public List<DeliveryRow> Rows { get; set; }
        }

        public class DeliveryRow
        {
            public string ProductName { get; set; }
            public string ShipmentDocumentNo { get; set; }
            public string OrderDocumentNo { get; set; }
            public DateTime? DeliveredDate { get; set; }
            public decimal DeliveredQty { get; set; }
            public string WarehouseName { get; set; }
            public decimal QuantityVariance { get; set; }
        }

        public class PostedJournalInfo
        {
            public decimal TotalDr { get; set; }
            public decimal TotalCr { get; set; }
            public DateTime? PostingDate { get; set; }
            public string PeriodName { get; set; }
            // Base / accounting currency (primary accounting schema) of the amounts.
            public string CurSymbol { get; set; }
            public int StdPrecision { get; set; }
            public List<JournalRow> Rows { get; set; }   // first page only
            public int Total { get; set; }               // total fact lines
        }

        public class JournalRow
        {
            public string AccountValue { get; set; }
            public string AccountName { get; set; }
            public string Description { get; set; }
            public string OrgName { get; set; }
            public string BPName { get; set; }
            public string ProductName { get; set; }
            public string OrgTrxName { get; set; }
            public decimal AmtAcctDr { get; set; }
            public decimal AmtAcctCr { get; set; }
        }

        #endregion
    }
}
