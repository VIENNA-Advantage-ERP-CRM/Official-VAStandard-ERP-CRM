/******************************************************
 * Module Name    : VASLogic
 * Purpose        : AP Invoice / AP Credit Note overview tab panel data
 *                  (header, lines, withholding, schedule, goods-receipt
 *                  match, landed-cost distribution, posted journal) and the
 *                  Record Payment / Allocate Credit Note write actions.
 * chronological  : Development
 *   VAI_145        Created  16 June 2026
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
    /// Backing model for the VAS_065_APInvoicePanel tab panel. Every SELECT is
    /// filtered through MRole.AddAccessSQL on the main physical table alias only
    /// (i / p / al / fa). Queries use COALESCE + bind parameters so they run on
    /// both PostgreSQL and Oracle.
    /// </summary>
    public class VAS_065_APInvoicePanelModel
    {
        private static VLogger log = VLogger.GetVLogger(typeof(VAS_065_APInvoicePanelModel).FullName);

        #region Panel (read) data

        /// <summary>
        /// Builds the full view model for an AP invoice or AP credit note:
        /// header, KPI amounts, invoice details, lines, totals, withholding,
        /// payment schedule, goods-receipt match, landed-cost distribution and
        /// posted journal. Returns an empty object (C_Invoice_ID = 0) when the
        /// user has no role access to the record.
        /// </summary>
        /// <param name="ctx">session context</param>
        /// <param name="C_Invoice_ID">target invoice</param>
        /// <returns>panel view model</returns>
        public APInvoicePanelData GetPanelData(Ctx ctx, int C_Invoice_ID)
        {
            APInvoicePanelData data = new APInvoicePanelData();
            if (C_Invoice_ID <= 0) return data;

            LoadHeader(ctx, C_Invoice_ID, data);
            if (data.C_Invoice_ID <= 0) return data;   // no access / not found

            data.Lines = LoadLines(C_Invoice_ID);
            data.Taxes = LoadTaxes(C_Invoice_ID);
            data.PaymentSchedule = LoadSchedule(C_Invoice_ID, 0, SCHEDULE_PAGE_SIZE);
            LoadScheduleAggregate(C_Invoice_ID, data);
            data.GoodsReceipt = LoadGoodsReceipt(C_Invoice_ID);
            data.LandedCost = LoadLandedCost(C_Invoice_ID);
            data.PostedJournal = LoadPostedJournal(ctx, C_Invoice_ID, 0, JOURNAL_PAGE_SIZE, true);
            LoadWithholding(C_Invoice_ID, data);

            return data;
        }

        /// <summary>
        /// Loads the invoice header, business-partner, currency, payment term,
        /// document type and representative. Sets the document-type flags and
        /// the open/net-payable amounts used by the KPI cards.
        /// </summary>
        private void LoadHeader(Ctx ctx, int C_Invoice_ID, APInvoicePanelData data)
        {
            string sql = @"SELECT
                              i.C_Invoice_ID,
                              i.AD_Client_ID,
                              i.AD_Org_ID,
                              i.DocumentNo,
                              i.InvoiceReference        AS InvoiceReference,
                              COALESCE(o.DocumentNo, N'') AS OrderDocumentNo,
                              i.DateInvoiced,
                              i.DateAcct,
                              i.C_BPartner_ID,
                              bp.Name              AS BPartnerName,
                              bp.Value             AS BPartnerValue,
                              bp.TaxID             AS BPartnerTaxID,
                              COALESCE(cty.Name, loc.City) AS BPCity,
                              cntry.Name           AS BPCountry,
                              loc.Postal,
                              i.C_Currency_ID,
                              i.C_ConversionType_ID,
                              cur.ISO_Code         AS CurrencyISOCode,
                              cur.CurSymbol        AS CurrencySymbol,
                              cur.StdPrecision     AS StdPrecision,
                              i.C_PaymentTerm_ID,
                              pt.Name              AS PaymentTermName,
                              pm.VA009_Name        AS PaymentMethodName,
                              i.C_DocTypeTarget_ID,
                              dt.Name              AS DocumentTypeName,
                              i.DocStatus,
                              i.Posted,
                              i.Processed,
                              i.IsApproved,
                              i.Created,
                              cu.Name              AS CreatedByName,
                              i.Updated,
                              uu.Name              AS UpdatedByName,
                              i.GrandTotal,
                              i.TotalLines,
                              COALESCE(i.GrandTotalAfterWithholding, i.GrandTotal)              AS NetPayableAmount,
                              (i.GrandTotal - COALESCE(i.GrandTotalAfterWithholding, i.GrandTotal)) AS WithholdingAmount,
                              i.IsPaid,
                              i.IsSOTrx,
                              COALESCE(i.IsReturnTrx, 'N') AS IsReturnTrx,
                              i.SalesRep_ID,
                              u.Name               AS RepresentativeName,
                              (SELECT MIN(ips.DueDate)
                                 FROM C_InvoicePaySchedule ips
                                WHERE ips.C_Invoice_ID = i.C_Invoice_ID
                                  AND COALESCE(ips.VA009_IsPaid, 'N') = 'N')   AS NextDueDate,
                              (SELECT COALESCE(SUM(ips.DueAmt), 0)
                                 FROM C_InvoicePaySchedule ips
                                WHERE ips.C_Invoice_ID = i.C_Invoice_ID
                                  AND COALESCE(ips.VA009_IsPaid, 'N') = 'N')   AS OpenScheduleAmt,
                              (SELECT COUNT(1)
                                 FROM C_InvoicePaySchedule ips
                                WHERE ips.C_Invoice_ID = i.C_Invoice_ID)       AS ScheduleCount,
                              acur.ISO_Code        AS AcctCurrencyISO
                           FROM C_Invoice i
                           INNER JOIN C_BPartner bp ON (i.C_BPartner_ID = bp.C_BPartner_ID)
                           INNER JOIN C_Currency cur ON (i.C_Currency_ID = cur.C_Currency_ID)
                           INNER JOIN C_PaymentTerm pt ON (i.C_PaymentTerm_ID = pt.C_PaymentTerm_ID)
                           INNER JOIN VA009_PaymentMethod pm ON (i.VA009_PaymentMethod_ID = pm.VA009_PaymentMethod_ID)
                           INNER JOIN C_DocType dt ON (i.C_DocTypeTarget_ID = dt.C_DocType_ID)
                           INNER JOIN C_BPartner_Location bpl ON (i.C_BPartner_Location_ID = bpl.C_BPartner_Location_ID)
                           INNER JOIN AD_ClientInfo ci ON (i.AD_Client_ID = ci.AD_Client_ID)
                           INNER JOIN C_AcctSchema acs ON (ci.C_AcctSchema1_ID = acs.C_AcctSchema_ID)
                           INNER JOIN C_Currency acur ON (acs.C_Currency_ID = acur.C_Currency_ID)
                           LEFT JOIN C_Location loc ON (bpl.C_Location_ID  = loc.C_Location_ID)
                           LEFT JOIN C_City cty ON (loc.C_City_ID = cty.C_City_ID)
                           LEFT JOIN C_Country cntry ON (loc.C_Country_ID = cntry.C_Country_ID)
                           LEFT JOIN AD_User u ON (i.SalesRep_ID = u.AD_User_ID)
                           LEFT JOIN AD_User cu ON (i.CreatedBy = cu.AD_User_ID)
                           LEFT JOIN AD_User uu ON (i.UpdatedBy = uu.AD_User_ID)
                           LEFT JOIN C_Order o ON (i.C_Order_ID = o.C_Order_ID)
                           WHERE i.C_Invoice_ID = @C_Invoice_ID
                             AND i.IsSOTrx = 'N'
                             AND i.IsActive = 'Y'";

            sql = MRole.GetDefault(ctx).AddAccessSQL(
                sql, "i", MRole.SQL_FULLYQUALIFIED, MRole.SQL_RO);

            DataSet ds = DB.ExecuteDataset(sql,
                new SqlParameter[] { new SqlParameter("@C_Invoice_ID", C_Invoice_ID) }, null);
            if (ds == null || ds.Tables.Count == 0 || ds.Tables[0].Rows.Count == 0)
                return;

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
            // Reversing a purchase invoice (ReverseCorrectIt) leaves the document on
            // DocStatus 'RE'. The panel shows "Reversed" instead of "Completed" for it.
            data.IsReversed = data.DocStatus == "RE";
            data.Created = Util.GetValueOfDateTime(r["Created"]);
            data.CreatedByName = Util.GetValueOfString(r["CreatedByName"]);
            data.Updated = Util.GetValueOfDateTime(r["Updated"]);
            data.ApprovedByName = Util.GetValueOfString(r["UpdatedByName"]);
            data.GrandTotal = Util.GetValueOfDecimal(r["GrandTotal"]);
            data.TotalLines = Util.GetValueOfDecimal(r["TotalLines"]);
            data.NetPayable = Util.GetValueOfDecimal(r["NetPayableAmount"]);
            data.WithholdingAmount = Util.GetValueOfDecimal(r["WithholdingAmount"]);
            data.TaxAmt = data.GrandTotal - data.TotalLines;
            data.IsPaid = Util.GetValueOfString(r["IsPaid"]) == "Y";
            data.IsReturnTrx = Util.GetValueOfString(r["IsReturnTrx"]) == "Y";
            data.RepresentativeName = Util.GetValueOfString(r["RepresentativeName"]);
            data.AcctCurISO = Util.GetValueOfString(r["AcctCurrencyISO"]);
            data.DueDate = Util.GetValueOfDateTime(r["NextDueDate"]);
            data.ScheduleCount = Util.GetValueOfInt(r["ScheduleCount"]);

            // AP credit note vs AP invoice (IsSOTrx already constrained to 'N').
            data.IsAPCreditNote = data.IsReturnTrx;
            data.IsAPInvoice = !data.IsReturnTrx;

            // Open amount = unpaid schedule total when schedules exist, else the
            // net payable (less anything already allocated to the invoice).
            decimal openSchedule = Util.GetValueOfDecimal(r["OpenScheduleAmt"]);
            if (data.ScheduleCount > 0)
                data.OpenAmount = openSchedule;
            else
                data.OpenAmount = data.IsPaid ? 0m : Math.Abs(data.NetPayable) - GetAllocatedAmount(C_Invoice_ID);
            if (data.OpenAmount < 0) data.OpenAmount = 0m;

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
        /// completed/active allocation lines. Used to derive the open amount
        /// when the invoice has no pay schedule.
        /// </summary>
        private decimal GetAllocatedAmount(int C_Invoice_ID)
        {
            // Allocation lines are stored in the allocation header currency, which may
            // differ from the invoice currency (cross-currency apply). Convert each line
            // amount to the invoice currency on the allocation accounting date, using the
            // allocation conversion type and org, before summing.
            string sql = @"SELECT COALESCE(SUM(ABS(
                                 currencyConvert((al.Amount + al.writeoffamt+al.discountamt), ah.C_Currency_ID, i.C_Currency_ID,
                                                 ah.DateAcct, i.C_ConversionType_ID,
                                                 ah.AD_Client_ID, ah.AD_Org_ID))), 0)
                             FROM C_AllocationLine al
                             INNER JOIN C_AllocationHdr ah ON (al.C_AllocationHdr_ID = ah.C_AllocationHdr_ID)
                             INNER JOIN C_Invoice i ON (al.C_Invoice_ID = i.C_Invoice_ID)
                            WHERE al.C_Invoice_ID = @C_Invoice_ID
                              AND al.IsActive = 'Y'
                              AND ah.IsActive = 'Y'
                              AND ah.DocStatus IN ('CO','CL')";
            return Util.GetValueOfDecimal(DB.ExecuteScalar(sql,
                new SqlParameter[] { new SqlParameter("@C_Invoice_ID", C_Invoice_ID) }, null));
        }

        /// <summary>
        /// Resolves the translated display name of a List-reference column value
        /// (e.g. DocStatus, Posted) from AD_Ref_List / AD_Ref_List_Trl for the
        /// session language, per the "Fetch Reference List Values for a Column"
        /// rule. The AD_Column is located by TableName + ColumnName (stable across
        /// environments) rather than a hard-coded AD_Column_ID. Returns the raw
        /// code when the value is unmapped or the column is not list-based.
        /// </summary>
        /// <param name="ctx">session context (supplies the UI language)</param>
        /// <param name="tableName">physical table owning the column (e.g. C_Invoice)</param>
        /// <param name="columnName">list-reference column (e.g. DocStatus, Posted)</param>
        /// <param name="code">stored short code whose display name is required</param>
        /// <returns>translated display name, or the code itself when unmapped</returns>
        private string GetListReferenceName(Ctx ctx, string tableName, string columnName, string code)
        {
            if (string.IsNullOrEmpty(code)) return code;

            string sql = @"SELECT COALESCE(rlt.Name, rl.Name, rl.Value) AS DisplayName
                             FROM AD_Column c
                             INNER JOIN AD_Table t ON (t.AD_Table_ID = c.AD_Table_ID)
                             INNER JOIN AD_Ref_List rl ON (rl.AD_Reference_ID = c.AD_Reference_Value_ID)
                             LEFT OUTER JOIN AD_Ref_List_Trl rlt ON (rlt.AD_Ref_List_ID = rl.AD_Ref_List_ID
                                                                     AND rlt.AD_Language = @Language
                                                                     AND rlt.IsActive = 'Y')
                            WHERE t.TableName = @TableName
                              AND c.ColumnName = @ColumnName
                              AND c.IsActive = 'Y'
                              AND rl.IsActive = 'Y'
                              AND rl.Value = @Code";

            string name = Util.GetValueOfString(DB.ExecuteScalar(sql, new SqlParameter[]
            {
                new SqlParameter("@Language", ctx.GetAD_Language()),
                new SqlParameter("@TableName", tableName),
                new SqlParameter("@ColumnName", columnName),
                new SqlParameter("@Code", code)
            }, null));

            return string.IsNullOrEmpty(name) ? code : name;
        }

        /// <summary>Loads invoice lines with product/charge, UOM and tax.</summary>
        private List<LineRow> LoadLines(int C_Invoice_ID)
        {
            List<LineRow> list = new List<LineRow>();
            string sql = @"SELECT
                              il.C_InvoiceLine_ID,
                              il.Line,
                              il.M_Product_ID,
                              pr.Name           AS ProductName,
                              il.C_Charge_ID,
                              ch.Name           AS ChargeName,
                              il.Description,
                              il.QtyEntered,
                              il.C_UOM_ID,
                              uom.X12DE355 AS UOMSymbol,
                              il.PriceEntered,
                              il.LineNetAmt,
                              il.TaxAmt,
                              il.C_Tax_ID,
                              tax.Name          AS TaxName,
                              tax.Rate          AS TaxRate,
                              il.M_InOutLine_ID,
                              il.C_OrderLine_ID,
                              il.M_AttributeSetInstance_ID,
                              asi.Description   AS ASIDescription,
                              CASE WHEN il.M_Product_ID IS NOT NULL AND il.M_Product_ID > 0
                                   THEN 'Y' ELSE 'N' END AS IsProductLine
                           FROM C_InvoiceLine il
                           LEFT OUTER JOIN M_Product pr ON (il.M_Product_ID = pr.M_Product_ID)
                           LEFT OUTER JOIN C_Charge ch ON (il.C_Charge_ID = ch.C_Charge_ID)
                           LEFT OUTER JOIN C_UOM uom ON (il.C_UOM_ID = uom.C_UOM_ID)
                           LEFT OUTER JOIN C_Tax tax ON (il.C_Tax_ID = tax.C_Tax_ID)
                           LEFT OUTER JOIN M_AttributeSetInstance asi ON (il.M_AttributeSetInstance_ID = asi.M_AttributeSetInstance_ID
                                                                          AND il.M_AttributeSetInstance_ID > 0)
                           WHERE il.C_Invoice_ID = @C_Invoice_ID
                             AND il.IsActive = 'Y'
                           ORDER BY il.Line";

            DataSet ds = DB.ExecuteDataset(sql,
                new SqlParameter[] { new SqlParameter("@C_Invoice_ID", C_Invoice_ID) }, null);
            if (ds == null || ds.Tables.Count == 0) return list;

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
                list.Add(ln);
            }
            return list;
        }

        /// <summary>Loads the invoice tax summary rows.</summary>
        private List<TaxRow> LoadTaxes(int C_Invoice_ID)
        {
            List<TaxRow> list = new List<TaxRow>();
            string sql = @"SELECT
                              it.C_Tax_ID,
                              tax.Name      AS TaxName,
                              tax.Rate      AS TaxRate,
                              it.TaxBaseAmt,
                              it.TaxAmt
                           FROM C_InvoiceTax it
                           INNER JOIN C_Tax tax ON (it.C_Tax_ID = tax.C_Tax_ID)
                           WHERE it.C_Invoice_ID = @C_Invoice_ID
                             AND it.IsActive = 'Y'
                           ORDER BY tax.Name";

            DataSet ds = DB.ExecuteDataset(sql,
                new SqlParameter[] { new SqlParameter("@C_Invoice_ID", C_Invoice_ID) }, null);
            if (ds == null || ds.Tables.Count == 0) return list;

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
        /// C_Withholding reference, summing the withheld amount per
        /// C_Withholding_ID. Falls back to the header-derived amount when the
        /// withholding configuration tables are not present in the environment.
        /// </summary>
        private void LoadWithholding(int C_Invoice_ID, APInvoicePanelData data)
        {
            if (data.WithholdingAmount <= 0) return;     // not applicable -> hidden

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
                                  w.Name        AS WithholdingName,
                                  MAX(w.InvPercentage) AS InvPercentage,
                                  SUM(CASE WHEN w.InvCalculation = 'T'
                                           THEN COALESCE(il.TaxAmt, 0)
                                           ELSE COALESCE(il.LineNetAmt, 0) END) AS BaseAmt
                               FROM C_InvoiceLine il
                               INNER JOIN C_Withholding w ON (il.C_Withholding_ID = w.C_Withholding_ID)
                               WHERE il.C_Invoice_ID = @C_Invoice_ID
                                 AND il.IsActive = 'Y'
                               GROUP BY w.C_Withholding_ID, w.Name";

                DataSet ds = DB.ExecuteDataset(sql,
                    new SqlParameter[] { new SqlParameter("@C_Invoice_ID", C_Invoice_ID) }, null);
                if (ds != null && ds.Tables.Count > 0 && ds.Tables[0].Rows.Count > 0)
                {
                    DataRow r = ds.Tables[0].Rows[0];
                    data.Withholding.TypeName = Util.GetValueOfString(r["WithholdingName"]);
                    decimal pct = Util.GetValueOfDecimal(r["InvPercentage"]);
                    decimal bAmt = Util.GetValueOfDecimal(r["BaseAmt"]);
                    if (pct != 0) data.Withholding.Rate = pct;
                    if (bAmt != 0) data.Withholding.Base = bAmt;
                }
            }
            catch (Exception ex)
            {
                log.Info("VAS_065 withholding enrichment skipped: " + ex.Message);
            }
        }

        /// <summary>Loads the invoice pay-schedule rows.</summary>
        // Server-side page sizes (mirrored by the frontend pagers).
        private const int SCHEDULE_PAGE_SIZE = 5;
        private const int JOURNAL_PAGE_SIZE = 10;
        private const int ONACCOUNT_PAGE_SIZE = 5;

        /// <summary>
        /// Builds the DB-specific row-limit suffix for server-side paging. Oracle uses
        /// OFFSET/FETCH; PostgreSQL and MySQL use LIMIT/OFFSET. page/pageSize are integers
        /// (no injection risk), so they are inlined - some engines reject bound LIMIT params.
        /// </summary>
        private string PagingSuffix(int page, int pageSize)
        {
            if (page < 0) page = 0;
            if (pageSize <= 0) pageSize = 10;
            int offset = page * pageSize;
            if (DB.IsOracle())
                return " OFFSET " + offset + " ROWS FETCH NEXT " + pageSize + " ROWS ONLY";
            return " LIMIT " + pageSize + " OFFSET " + offset;
        }

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
                           WHERE ips.C_Invoice_ID = @C_Invoice_ID
                             AND ips.IsActive = 'Y'
                           ORDER BY ips.DueDate, ips.C_InvoicePaySchedule_ID";
            sql += PagingSuffix(page, pageSize);

            DataSet ds = DB.ExecuteDataset(sql,
                new SqlParameter[] { new SqlParameter("@C_Invoice_ID", C_Invoice_ID) }, null);
            if (ds == null || ds.Tables.Count == 0) return list;

            foreach (DataRow r in ds.Tables[0].Rows)
            {
                ScheduleRow s = new ScheduleRow();
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

        /// <summary>Schedule footer aggregate (total count, open amount, hold) over ALL
        /// schedules - the page rows alone cannot produce these.</summary>
        private void LoadScheduleAggregate(int C_Invoice_ID, APInvoicePanelData data)
        {
            DataSet ds = DB.ExecuteDataset(
                @"SELECT COUNT(ips.C_InvoicePaySchedule_ID) AS Total,
                         COALESCE(SUM(CASE WHEN COALESCE(ips.VA009_IsPaid,'N') <> 'Y' THEN ips.DueAmt ELSE 0 END), 0) AS OpenAmt,
                         MAX(CASE WHEN COALESCE(ips.IsHoldPayment,'N') = 'Y' THEN 1 ELSE 0 END) AS AnyHold
                    FROM C_InvoicePaySchedule ips
                   WHERE ips.C_Invoice_ID = @C_Invoice_ID AND ips.IsActive = 'Y'",
                new SqlParameter[] { new SqlParameter("@C_Invoice_ID", C_Invoice_ID) }, null);
            if (ds != null && ds.Tables.Count > 0 && ds.Tables[0].Rows.Count > 0)
            {
                DataRow r = ds.Tables[0].Rows[0];
                data.ScheduleTotal = Util.GetValueOfInt(r["Total"]);
                data.ScheduleOpenAmount = Util.GetValueOfDecimal(r["OpenAmt"]);
                data.ScheduleAnyHold = Util.GetValueOfInt(r["AnyHold"]) == 1;
            }
        }

        /// <summary>Page of schedule rows for the server-side pager.</summary>
        public List<ScheduleRow> GetSchedulePage(int C_Invoice_ID, int page, int pageSize)
        {
            return LoadSchedule(C_Invoice_ID, page, pageSize > 0 ? pageSize : SCHEDULE_PAGE_SIZE);
        }

        /// <summary>
        /// Loads goods-receipt / three-way-match rows for product lines that are
        /// linked to a receipt line. Charge-only / unlinked lines are excluded.
        /// </summary>
        private GoodsReceiptInfo LoadGoodsReceipt(int C_Invoice_ID)
        {
            GoodsReceiptInfo gr = new GoodsReceiptInfo { Rows = new List<GoodsReceiptRow>() };
            string sql = @"SELECT
                              il.C_InvoiceLine_ID,
                              pr.Name            AS ProductName,
                              il.QtyEntered      AS InvoiceQty,
                              il.PriceEntered    AS InvoicePrice,
                              io.DocumentNo      AS ReceiptDocumentNo,
                              io.MovementDate    AS ReceivedDate,
                              iol.MovementQty    AS ReceivedQty,
                              wh.Name            AS WarehouseName,
                              o.DocumentNo       AS OrderDocumentNo,
                              ol.QtyOrdered,
                              ol.PriceEntered    AS OrderPrice,
                              (il.QtyEntered - COALESCE(iol.MovementQty, il.QtyEntered)) AS QuantityVariance,
                              (il.PriceEntered - COALESCE(ol.PriceEntered, il.PriceEntered)) AS PriceVariance
                           FROM C_InvoiceLine il
                           INNER JOIN M_Product pr   ON (il.M_Product_ID  = pr.M_Product_ID)
                           LEFT OUTER JOIN M_InOutLine iol ON (il.M_InOutLine_ID = iol.M_InOutLine_ID)
                           LEFT OUTER JOIN M_InOut io  ON (iol.M_InOut_ID  = io.M_InOut_ID)
                           LEFT OUTER JOIN M_Warehouse wh ON (io.M_Warehouse_ID = wh.M_Warehouse_ID)
                           LEFT OUTER JOIN C_OrderLine ol ON (il.C_OrderLine_ID = ol.C_OrderLine_ID)
                           LEFT OUTER JOIN C_Order o   ON (ol.C_Order_ID   = o.C_Order_ID)
                           WHERE il.C_Invoice_ID = @C_Invoice_ID
                             AND il.IsActive = 'Y'
                             AND il.M_Product_ID IS NOT NULL
                             AND il.M_InOutLine_ID IS NOT NULL
                           ORDER BY il.Line";

            DataSet ds = DB.ExecuteDataset(sql,
                new SqlParameter[] { new SqlParameter("@C_Invoice_ID", C_Invoice_ID) }, null);
            if (ds == null || ds.Tables.Count == 0) return gr;

            decimal totalRecvQty = 0m, maxPriceVar = 0m, totalQtyVar = 0m;
            // Collect DISTINCT receipt/order doc nos, warehouses and received dates across
            // all matched lines (in first-seen order) so they can be shown comma-separated
            // instead of keeping only the first value.
            List<string> recvDocs = new List<string>();
            List<string> orderDocs = new List<string>();
            List<string> warehouses = new List<string>();
            List<DateTime> recvDates = new List<DateTime>();
            foreach (DataRow r in ds.Tables[0].Rows)
            {
                GoodsReceiptRow row = new GoodsReceiptRow();
                row.ProductName = Util.GetValueOfString(r["ProductName"]);
                row.ReceiptDocumentNo = Util.GetValueOfString(r["ReceiptDocumentNo"]);
                row.OrderDocumentNo = Util.GetValueOfString(r["OrderDocumentNo"]);
                row.ReceivedDate = Util.GetValueOfDateTime(r["ReceivedDate"]);
                row.ReceivedQty = Util.GetValueOfDecimal(r["ReceivedQty"]);
                row.WarehouseName = Util.GetValueOfString(r["WarehouseName"]);
                row.PriceVariance = Util.GetValueOfDecimal(r["PriceVariance"]);
                row.QuantityVariance = Util.GetValueOfDecimal(r["QuantityVariance"]);
                gr.Rows.Add(row);

                totalRecvQty += row.ReceivedQty;
                totalQtyVar += Math.Abs(row.QuantityVariance);
                if (Math.Abs(row.PriceVariance) > Math.Abs(maxPriceVar)) maxPriceVar = row.PriceVariance;

                if (!string.IsNullOrEmpty(row.ReceiptDocumentNo) && !recvDocs.Contains(row.ReceiptDocumentNo)) recvDocs.Add(row.ReceiptDocumentNo);
                if (!string.IsNullOrEmpty(row.OrderDocumentNo) && !orderDocs.Contains(row.OrderDocumentNo)) orderDocs.Add(row.OrderDocumentNo);
                if (!string.IsNullOrEmpty(row.WarehouseName) && !warehouses.Contains(row.WarehouseName)) warehouses.Add(row.WarehouseName);
                if (row.ReceivedDate.HasValue && !recvDates.Contains(row.ReceivedDate.Value.Date)) recvDates.Add(row.ReceivedDate.Value.Date);
            }
            // All distinct values, comma-separated. ReceivedDate keeps the first date for
            // the single-date consumers (matched-receipt step); ReceivedDates carries the
            // full distinct list for the comma-separated detail.
            gr.ReceiptDocumentNo = string.Join(", ", recvDocs);
            gr.OrderDocumentNo = string.Join(", ", orderDocs);
            gr.WarehouseName = string.Join(", ", warehouses);
            gr.ReceivedDates = recvDates;
            gr.ReceivedDate = recvDates.Count > 0 ? (DateTime?)recvDates[0] : null;
            gr.LineCount = gr.Rows.Count;
            gr.TotalReceived = totalRecvQty;
            gr.PriceVariance = maxPriceVar;
            gr.QtyVariance = totalQtyVar;
            gr.IsCleanMatch = (Math.Abs(maxPriceVar) == 0m && totalQtyVar == 0m);
            return gr;
        }

        /// <summary>
        /// Loads landed-cost distribution rows for product lines via
        /// C_LandedCostAllocation. Returns an empty distribution (section hidden)
        /// when no allocation exists or the table is absent.
        /// </summary>
        private LandedCostInfo LoadLandedCost(int C_Invoice_ID)
        {
            LandedCostInfo lc = new LandedCostInfo { Rows = new List<LandedCostRow>() };
            try
            {
                string sql = @"select il.C_Invoice_ID,lcaci.c_invoice_id,
                                  il.C_InvoiceLine_ID,lcail.C_InvoiceLine_ID, lcail.c_charge_id,
                                  il.M_InOutLine_ID,
                                  COALESCE(pr.Name,  il.Description) AS ItemName,
                                  il.QtyEntered,
                                  uom.UOMSymbol,
                                  currencyConvert(il.LineNetAmt, i.C_Currency_ID, acs.C_Currency_ID, i.DateInvoiced, i.C_ConversionType_ID, i.AD_Client_ID, i.AD_Org_ID) AS PurchaseValue,
                                  currencyConvert(lca.Amt, lcaci.C_Currency_ID, acs.C_Currency_ID, lcaci.DateInvoiced, lcaci.C_ConversionType_ID, lcaci.AD_Client_ID, lcaci.AD_Org_ID) AS AllocatedLandedCost,
                                  lcaci.DocumentNo       AS LandedCostDocumentNo
                               FROM C_InvoiceLine il
                               INNER JOIN c_invoice i ON (i.c_invoice_id = il.c_invoice_id)
                               INNER JOIN AD_ClientInfo ci ON (i.AD_Client_ID = ci.AD_Client_ID)
                               INNER JOIN C_AcctSchema acs ON (ci.C_AcctSchema1_ID = acs.C_AcctSchema_ID)
                               INNER JOIN C_LandedCostAllocation lca ON (il.m_inoutline_id  = lca.m_inoutline_id
                               AND lca.m_product_id = il.m_product_id AND NVL(lca.m_attributesetinstance_id, 0) = NVL(il.m_attributesetinstance_id, 0))
                               INNER JOIN c_invoiceline lcail ON (lcail.c_invoiceline_id = lca.c_invoiceline_id and lcail.vas_islandedcost = 'Y')
                               INNER JOIN c_invoice lcaci ON (lcail.c_invoice_id = lcaci.c_invoice_id)
                               INNER JOIN M_Product pr ON (il.M_Product_ID = pr.M_Product_ID)
                               INNER JOIN C_UOM uom ON (il.C_UOM_ID = uom.C_UOM_ID)
                               WHERE il.C_Invoice_ID = @C_Invoice_ID
                                 AND il.IsActive = 'Y'
                                 AND il.M_Product_ID IS NOT NULL
                               ORDER BY il.Line";

                DataSet ds = DB.ExecuteDataset(sql, new SqlParameter[] { new SqlParameter("@C_Invoice_ID", C_Invoice_ID) }, null);
                if (ds == null || ds.Tables.Count == 0)
                {
                    return lc;
                }

                decimal totPv = 0m, totLc = 0m, totIv = 0m;
                // A single receipt line (M_InOutLine_ID) can receive landed cost from
                // several landed-cost invoices, producing one DB row per allocation.
                // Collapse them into one display row: purchase value counted once,
                // allocated landed cost summed, source document numbers joined.
                Dictionary<int, LandedCostRow> rowByInOutLine = new Dictionary<int, LandedCostRow>();
                Dictionary<int, List<string>> docsByInOutLine = new Dictionary<int, List<string>>();
                foreach (DataRow r in ds.Tables[0].Rows)
                {
                    int inOutLineId = Util.GetValueOfInt(r["M_InOutLine_ID"]);
                    decimal allocated = Util.GetValueOfDecimal(r["AllocatedLandedCost"]);
                    string docNo = Util.GetValueOfString(r["LandedCostDocumentNo"]);

                    LandedCostRow row;
                    if (!rowByInOutLine.TryGetValue(inOutLineId, out row))
                    {
                        // First allocation for this receipt line: seed the display row.
                        row = new LandedCostRow();
                        row.ItemName = Util.GetValueOfString(r["ItemName"]);
                        row.QtyEntered = Util.GetValueOfDecimal(r["QtyEntered"]);
                        row.UOMSymbol = Util.GetValueOfString(r["UOMSymbol"]);
                        row.PurchaseValue = Util.GetValueOfDecimal(r["PurchaseValue"]);
                        rowByInOutLine[inOutLineId] = row;
                        docsByInOutLine[inOutLineId] = new List<string>();
                        lc.Rows.Add(row);
                        totPv += row.PurchaseValue;   // purchase value counted once per receipt line
                    }

                    row.AllocatedLandedCost += allocated;
                    totLc += allocated;
                    if (!string.IsNullOrEmpty(docNo) && !docsByInOutLine[inOutLineId].Contains(docNo))
                        docsByInOutLine[inOutLineId].Add(docNo);
                }

                // Finalise each grouped row: inventory cost, unit cost, joined source docs.
                foreach (KeyValuePair<int, LandedCostRow> kv in rowByInOutLine)
                {
                    LandedCostRow row = kv.Value;
                    row.InventoryCost = row.PurchaseValue + row.AllocatedLandedCost;
                    if (row.QtyEntered != 0)
                        row.UnitInventoryCost = Math.Round(row.InventoryCost / row.QtyEntered, 2);
                    row.SourceDocumentNo = string.Join(", ", docsByInOutLine[kv.Key].ToArray());
                    totIv += row.InventoryCost;
                }
                // Banner-level source: first display row carrying any source document.
                foreach (LandedCostRow row in lc.Rows)
                {
                    if (!string.IsNullOrEmpty(row.SourceDocumentNo)) { lc.SourceDocumentNo = row.SourceDocumentNo; break; }
                }

                lc.TotalPurchaseValue = totPv;
                lc.TotalLandedCost = totLc;
                lc.TotalInventoryCost = totIv;
                lc.ItemCount = lc.Rows.Count;
            }
            catch (Exception ex)
            {
                log.Info("VAS_065 landed cost skipped: " + ex.Message);
                lc.Rows.Clear();
            }
            return lc;
        }

        /// <summary>Loads posted accounting facts for the invoice.</summary>
        private PostedJournalInfo LoadPostedJournal(Ctx ctx, int C_Invoice_ID, int page, int pageSize, bool includeTotals)
        {
            PostedJournalInfo pj = new PostedJournalInfo { Rows = new List<JournalRow>() };
            string sql = @"SELECT
                              fa.DateAcct,
                              ev.Value      AS AccountValue,
                              ev.Name       AS AccountName,
                              fa.AmtAcctDr,
                              fa.AmtAcctCr,
                              fa.Description,
                              org.Name      AS OrgName,
                              bp.Name       AS BPName,
                              pr.Name       AS ProductName,
                              orgtrx.Name   AS OrgTrxName,
                              p.Name        AS PeriodName,
                              cur.CurSymbol AS AcctCurSymbol,
                              cur.StdPrecision AS AcctStdPrecision
                           FROM Fact_Acct fa
                           INNER JOIN C_ElementValue ev ON (fa.Account_ID = ev.C_ElementValue_ID)
                           INNER JOIN AD_Org org ON (fa.AD_Org_ID     = org.AD_Org_ID)
                           INNER JOIN C_Period p ON (fa.C_Period_ID = p.C_Period_ID)
                           INNER JOIN AD_ClientInfo ci ON (ci.AD_Client_ID = fa.AD_Client_ID)
                           INNER JOIN C_AcctSchema acs ON (acs.C_AcctSchema_ID = ci.C_AcctSchema1_ID)
                           INNER JOIN C_Currency cur ON (cur.C_Currency_ID = acs.C_Currency_ID)
                           LEFT OUTER JOIN C_BPartner bp ON (fa.C_BPartner_ID = bp.C_BPartner_ID)
                           LEFT OUTER JOIN M_Product pr ON (fa.M_Product_ID  = pr.M_Product_ID)
                           LEFT OUTER JOIN AD_Org orgtrx ON (fa.AD_OrgTrx_ID  = orgtrx.AD_Org_ID)
                           WHERE fa.AD_Table_ID = (SELECT AD_Table_ID FROM AD_Table WHERE TableName = 'C_Invoice')
                             AND fa.Record_ID = @C_Invoice_ID
                             AND fa.C_AcctSchema_ID = ci.C_AcctSchema1_ID
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
                    if (!pj.PostingDate.HasValue) pj.PostingDate = Util.GetValueOfDateTime(r["DateAcct"]);
                    if (string.IsNullOrEmpty(pj.PeriodName)) pj.PeriodName = Util.GetValueOfString(r["PeriodName"]);
                    if (string.IsNullOrEmpty(pj.CurSymbol))
                    {
                        pj.CurSymbol = Util.GetValueOfString(r["AcctCurSymbol"]);
                        pj.StdPrecision = Util.GetValueOfInt(r["AcctStdPrecision"]);
                    }
                }
            }

            // Footer totals + count over ALL fact lines (independent of the page). These
            // do not change between pages, so they are computed ONLY on the initial load
            // (includeTotals); page fetches skip this aggregate query.
            if (includeTotals)
            {
                string aggSql = @"SELECT COUNT(fa.Fact_Acct_ID) AS Total,
                                         COALESCE(SUM(fa.AmtAcctDr), 0) AS TotDr,
                                         COALESCE(SUM(fa.AmtAcctCr), 0) AS TotCr
                                    FROM Fact_Acct fa
                                    INNER JOIN AD_ClientInfo ci ON (ci.AD_Client_ID = fa.AD_Client_ID)
                                   WHERE fa.AD_Table_ID = (SELECT AD_Table_ID FROM AD_Table WHERE TableName = 'C_Invoice')
                                     AND fa.Record_ID = @C_Invoice_ID
                                     AND fa.C_AcctSchema_ID = ci.C_AcctSchema1_ID";
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

        /// <summary>Page of posted-journal rows for the pager (rows only - totals/count are
        /// already on the client from the initial load, so the aggregate is not recomputed).</summary>
        public PostedJournalInfo GetPostedJournalPage(Ctx ctx, int C_Invoice_ID, int page, int pageSize)
        {
            return LoadPostedJournal(ctx, C_Invoice_ID, page, pageSize > 0 ? pageSize : JOURNAL_PAGE_SIZE, false);
        }

        #endregion

        #region Payment / allocation modal meta

        /// <summary>
        /// Returns the data needed by the Record Payment / Allocate Credit Note
        /// modal: vendor position amounts, available on-account payments and
        /// vendor credit notes (AP invoice mode) or open AP invoices (AP credit
        /// note mode), plus payment methods and bank accounts.
        /// </summary>
        public PaymentModalMeta GetPaymentModalMeta(Ctx ctx, int C_Invoice_ID)
        {
            PaymentModalMeta meta = new PaymentModalMeta();
            APInvoicePanelData head = new APInvoicePanelData();
            LoadHeader(ctx, C_Invoice_ID, head);
            if (head.C_Invoice_ID <= 0)
            {
                return meta;
            }

            MRole role = MRole.GetDefault(ctx);

            meta.C_Invoice_ID = head.C_Invoice_ID;
            meta.C_BPartner_ID = head.C_BPartner_ID;
            meta.C_Currency_ID = head.C_Currency_ID;
            meta.CurSymbol = head.CurSymbol;
            meta.ISO_Code = head.CurISO;
            meta.StdPrecision = head.StdPrecision;
            meta.GrossInvoice = head.GrandTotal;
            meta.Withholding = head.WithholdingAmount;
            meta.NetPayable = head.NetPayable;
            meta.IsAPCreditNote = head.IsAPCreditNote;
            meta.RecordMode = head.IsAPCreditNote ? "AP_CREDIT_NOTE_ALLOCATION" : "AP_INVOICE_PAYMENT";
            meta.VendorOutstanding = GetVendorOutstanding(ctx, head.AD_Client_ID, head.C_BPartner_ID);

            // AP invoices (API) and AP credit notes (APC) use the SAME modal (on-account
            // credits + record payment); the only difference is the amount sign, applied
            // when the allocation / payment is created. The UI works with the positive open
            // amount, so the credit-note open is taken as an absolute value here.
            if (head.IsAPCreditNote)
            {
                meta.CreditNoteAmount = Math.Max(0m, Math.Abs(head.NetPayable) - GetAllocatedAmount(C_Invoice_ID));
                meta.NetOpenAmount = meta.CreditNoteAmount;
            }
            else
            {
                meta.NetOpenAmount = head.OpenAmount;
            }

            // Nothing left to settle: the invoice is flagged paid or every pay schedule
            // is marked paid (open amount consumed by payments / allocations). The modal
            // uses this to render the New Payment section read-only.
            meta.IsFullySettled = meta.NetOpenAmount <= 0m || (!head.IsAPCreditNote && head.IsPaid);

            // First page of on-account payments (server-side paged). The page rows are
            // converted for display; total count + "Available to apply" come from a
            // full aggregate pass so they are not limited to the page.
            meta.OnAccountPayments = LoadOnAccountPayments(ctx, head.AD_Client_ID, head.C_BPartner_ID, head.IsAPCreditNote, 0, ONACCOUNT_PAGE_SIZE);
            foreach (AvailableCreditRow c in meta.OnAccountPayments)
            {
                ConvertRowToInvoiceCurrency(ctx, c, head);
            }
            int oaTotal;
            decimal oaAvail;
            LoadOnAccountAggregate(ctx, head.AD_Client_ID, head.C_BPartner_ID, head.IsAPCreditNote, head, out oaTotal, out oaAvail);
            meta.OnAccountPaymentsTotal = oaTotal;
            meta.AvailableToApply = oaAvail;
            meta.BankAccounts = LoadBankAccounts(role, head.AD_Org_ID);
            meta.PaymentMethods = LoadPaymentMethods(role, head.AD_Org_ID);
            // Currency + conversion-type selectors on the modal.
            meta.Currencies = LoadCurrencies(ctx, head.AD_Client_ID);
            meta.ConversionTypes = LoadConversionTypes(role, head.AD_Client_ID, head.AD_Org_ID);
            meta.C_ConversionType_ID = head.C_ConversionType_ID;
            return meta;
        }

        /// <summary>Active currencies of the client (the "My Currency" list for the modal).</summary>
        private List<CurrencyOption> LoadCurrencies(Ctx ctx, int AD_Client_ID)
        {
            List<CurrencyOption> list = new List<CurrencyOption>();
            string sql = @"SELECT C_Currency_ID, ISO_Code, CurSymbol, StdPrecision
                             FROM C_Currency
                            WHERE IsActive = 'Y' AND IsMyCurrency = 'Y' 
                              AND AD_Client_ID IN (0, @AD_Client_ID)
                            ORDER BY ISO_Code";
            DataSet ds = DB.ExecuteDataset(sql,
                new SqlParameter[] { new SqlParameter("@AD_Client_ID", AD_Client_ID) }, null);
            if (ds == null || ds.Tables.Count == 0) return list;
            foreach (DataRow r in ds.Tables[0].Rows)
            {
                list.Add(new CurrencyOption
                {
                    C_Currency_ID = Util.GetValueOfInt(r["C_Currency_ID"]),
                    ISO_Code = Util.GetValueOfString(r["ISO_Code"]),
                    CurSymbol = Util.GetValueOfString(r["CurSymbol"]),
                    StdPrecision = Util.GetValueOfInt(r["StdPrecision"])
                });
            }
            return list;
        }

        /// <summary>Active conversion (rate) types of the invoice org, role-restricted.</summary>
        private List<ConversionTypeOption> LoadConversionTypes(MRole role, int AD_Client_ID, int AD_Org_ID)
        {
            List<ConversionTypeOption> list = new List<ConversionTypeOption>();
            string sql = @"SELECT ct.C_ConversionType_ID, ct.Name, COALESCE(ct.IsDefault, 'N') AS IsDefault
                             FROM C_ConversionType ct
                            WHERE ct.IsActive = 'Y'
                              AND ct.AD_Client_ID IN (0, @AD_Client_ID)
                              AND ct.AD_Org_ID IN (0, @AD_Org_ID)
                            ORDER BY IsDefault DESC, Name";
            sql = role.AddAccessSQL(sql, "ct", MRole.SQL_FULLYQUALIFIED, MRole.SQL_RO);
            DataSet ds = DB.ExecuteDataset(sql,
                new SqlParameter[] { new SqlParameter("@AD_Client_ID", AD_Client_ID), new SqlParameter("@AD_Org_ID", AD_Org_ID) }, null);
            if (ds == null || ds.Tables.Count == 0) return list;
            foreach (DataRow r in ds.Tables[0].Rows)
            {
                list.Add(new ConversionTypeOption
                {
                    C_ConversionType_ID = Util.GetValueOfInt(r["C_ConversionType_ID"]),
                    Name = Util.GetValueOfString(r["Name"]),
                    IsDefault = Util.GetValueOfString(r["IsDefault"]) == "Y"
                });
            }
            return list;
        }

        /// <summary>
        /// Sets <see cref="AvailableCreditRow.AvailableAmountInv"/> = the row's available
        /// amount converted into the invoice currency on the current date (default
        /// conversion type). When no conversion rate exists the converted amount is 0.
        /// </summary>
        private void ConvertRowToInvoiceCurrency(Ctx ctx, AvailableCreditRow row, APInvoicePanelData head)
        {
            if (row.C_Currency_ID <= 0 || row.C_Currency_ID == head.C_Currency_ID)
            {
                row.AvailableAmountInv = row.AvailableAmount;
                return;
            }
            decimal rate = MConversionRate.GetRate(row.C_Currency_ID, head.C_Currency_ID, row.DateAcct, row.C_ConversionType_ID,
                head.AD_Client_ID, row.AD_Org_ID);
            row.AvailableAmountInv = (rate == 0)
                ? 0m
                : MConversionRate.Convert(ctx, row.AvailableAmount, row.C_Currency_ID, head.C_Currency_ID,
                    row.DateAcct, row.C_ConversionType_ID, head.AD_Client_ID, row.AD_Org_ID);
        }

        /// <summary>Sum of open AP schedules for the vendor (outstanding position).</summary>
        private decimal GetVendorOutstanding(Ctx ctx, int AD_Client_ID, int C_BPartner_ID)
        {
            string sql = @"SELECT COALESCE(SUM(COALESCE(ips.DueAmt,
                                  COALESCE(i.GrandTotalAfterWithholding, i.GrandTotal))), 0)
                             FROM C_Invoice i
                             INNER JOIN C_InvoicePaySchedule ips ON (i.C_Invoice_ID = ips.C_Invoice_ID)
                            WHERE i.AD_Client_ID = @AD_Client_ID
                              AND i.C_BPartner_ID = @C_BPartner_ID
                              AND i.IsSOTrx = 'N'
                              AND i.IsActive = 'Y'
                              AND i.DocStatus IN ('CO','CL')
                              AND COALESCE(i.IsPaid, 'N') = 'N'
                              AND COALESCE(ips.VA009_IsPaid, 'N') = 'N'";
            sql = MRole.GetDefault(ctx).AddAccessSQL(sql, "i", MRole.SQL_FULLYQUALIFIED, MRole.SQL_RO);
            return Util.GetValueOfDecimal(DB.ExecuteScalar(sql, new SqlParameter[]
            {
                new SqlParameter("@AD_Client_ID", AD_Client_ID),
                new SqlParameter("@C_BPartner_ID", C_BPartner_ID)
            }, null));
        }

        /// <summary>
        /// SQL filter that keeps the displayable on-account payments. The sign of the
        /// available amount selects the set: invoice mode (not a credit note) keeps
        /// avail &lt;= 0; credit-note mode keeps avail &gt; 0. Pushed into SQL so paging and
        /// the row count are correct.
        /// </summary>
        private string OnAccountSignFilter(bool IsCreditNote)
        {
            return IsCreditNote
                ? " AND ALLOCPAYMENTAVAILABLE(p.C_Payment_ID) > 0"
                : " AND ALLOCPAYMENTAVAILABLE(p.C_Payment_ID) <= 0";
        }

        /// <summary>Available (unallocated) on-account vendor payments (one page).</summary>
        private List<AvailableCreditRow> LoadOnAccountPayments(Ctx ctx, int AD_Client_ID, int C_BPartner_ID, bool IsCreditNote, int page, int pageSize)
        {
            List<AvailableCreditRow> list = new List<AvailableCreditRow>();
            string sql = @"SELECT
                              p.AD_Org_ID,
                              p.C_Payment_ID,
                              p.DocumentNo,
                              pdt.Name AS DocTypeName,
                              p.DateTrx,
                              p.DateAcct,
                              p.C_Currency_ID,
                              p.C_ConversionType_ID,
                              pcur.CurSymbol AS CurSymbol,
                              pcur.ISO_Code  AS ISO_Code,
                              pcur.StdPrecision AS StdPrecision,
                              (ABS(COALESCE(p.vas_unallocatedamount, 0))) AS VAS_UnAllocatedAmount,
                              ALLOCPAYMENTAVAILABLE(p.C_Payment_ID) AS AvailableAmount
                           FROM C_Payment p
                           INNER JOIN C_Currency pcur ON (p.C_Currency_ID = pcur.C_Currency_ID)
                           LEFT JOIN C_DocType pdt ON (p.C_DocType_ID = pdt.C_DocType_ID)
                           WHERE p.AD_Client_ID = @AD_Client_ID
                             AND p.C_BPartner_ID = @C_BPartner_ID
                             AND p.IsReceipt = 'N'
                             AND p.IsActive = 'Y'
                             AND p.DocStatus IN ('CO','CL')
                             AND COALESCE(p.IsAllocated, 'N') = 'N'
                             AND p.C_Invoice_ID IS NULL
                             AND p.C_Charge_ID IS NULL
                             AND p.C_Order_ID IS NULL";
            sql += OnAccountSignFilter(IsCreditNote);
            sql += " ORDER BY p.DateAcct, p.DocumentNo, p.C_Payment_ID";
            sql = MRole.GetDefault(ctx).AddAccessSQL(sql, "p", MRole.SQL_FULLYQUALIFIED, MRole.SQL_RO);
            sql += PagingSuffix(page, pageSize);

            DataSet ds = DB.ExecuteDataset(sql, new SqlParameter[]
            {
                new SqlParameter("@AD_Client_ID", AD_Client_ID),
                new SqlParameter("@C_BPartner_ID", C_BPartner_ID)
            }, null);
            if (ds == null || ds.Tables.Count == 0) return list;

            foreach (DataRow r in ds.Tables[0].Rows)
            {
                decimal avail = Util.GetValueOfDecimal(r["AvailableAmount"]);
                list.Add(new AvailableCreditRow
                {
                    SourceType = "PAYMENT",
                    Id = Util.GetValueOfInt(r["C_Payment_ID"]),
                    DocTypeName = Util.GetValueOfString(r["DocTypeName"]),
                    DocumentNo = Util.GetValueOfString(r["DocumentNo"]),
                    Date = Util.GetValueOfDateTime(r["DateTrx"]),
                    DateAcct = Util.GetValueOfDateTime(r["DateAcct"]),
                    AvailableAmount = Math.Abs(avail),
                    AD_Org_ID = Util.GetValueOfInt(r["AD_Org_ID"]),
                    C_Currency_ID = Util.GetValueOfInt(r["C_Currency_ID"]),
                    C_ConversionType_ID = Util.GetValueOfInt(r["C_ConversionType_ID"]),
                    CurSymbol = Util.GetValueOfString(r["CurSymbol"]),
                    ISO_Code = Util.GetValueOfString(r["ISO_Code"]),
                    StdPrecision = Util.GetValueOfInt(r["StdPrecision"])
                });
            }
            return list;
        }

        /// <summary>
        /// Aggregate over ALL displayable on-account payments (independent of the page):
        /// total row count and the "available to apply" sum converted into the invoice
        /// currency. The conversion is per row, so this scans the full set (light columns).
        /// </summary>
        private void LoadOnAccountAggregate(Ctx ctx, int AD_Client_ID, int C_BPartner_ID, bool IsCreditNote,
            APInvoicePanelData head, out int total, out decimal availToApply)
        {
            total = 0;
            availToApply = 0m;
            string sql = @"SELECT p.AD_Org_ID, p.C_Currency_ID, p.C_ConversionType_ID, p.DateAcct,
                                  ALLOCPAYMENTAVAILABLE(p.C_Payment_ID) AS AvailableAmount
                             FROM C_Payment p
                            WHERE p.AD_Client_ID = @AD_Client_ID
                              AND p.C_BPartner_ID = @C_BPartner_ID
                              AND p.IsReceipt = 'N'
                              AND p.IsActive = 'Y'
                              AND p.DocStatus IN ('CO','CL')
                              AND COALESCE(p.IsAllocated, 'N') = 'N'
                              AND p.C_Invoice_ID IS NULL
                              AND p.C_Charge_ID IS NULL
                              AND p.C_Order_ID IS NULL";
            sql += OnAccountSignFilter(IsCreditNote);
            sql = MRole.GetDefault(ctx).AddAccessSQL(sql, "p", MRole.SQL_FULLYQUALIFIED, MRole.SQL_RO);
            DataSet ds = DB.ExecuteDataset(sql, new SqlParameter[]
            {
                new SqlParameter("@AD_Client_ID", AD_Client_ID),
                new SqlParameter("@C_BPartner_ID", C_BPartner_ID)
            }, null);
            if (ds == null || ds.Tables.Count == 0) return;

            foreach (DataRow r in ds.Tables[0].Rows)
            {
                total++;
                AvailableCreditRow tmp = new AvailableCreditRow
                {
                    AvailableAmount = Math.Abs(Util.GetValueOfDecimal(r["AvailableAmount"])),
                    AD_Org_ID = Util.GetValueOfInt(r["AD_Org_ID"]),
                    C_Currency_ID = Util.GetValueOfInt(r["C_Currency_ID"]),
                    C_ConversionType_ID = Util.GetValueOfInt(r["C_ConversionType_ID"]),
                    DateAcct = Util.GetValueOfDateTime(r["DateAcct"])
                };
                ConvertRowToInvoiceCurrency(ctx, tmp, head);
                availToApply += tmp.AvailableAmountInv;
            }
        }

        /// <summary>
        /// Page of on-account payment rows (converted for display) for the pager. The
        /// vendor, invoice currency and credit-note flag are passed from the frontend
        /// (already known from the modal meta) so the header is NOT re-queried per page;
        /// AD_Client_ID is taken from the trusted session ctx, not the client request.
        /// </summary>
        public List<AvailableCreditRow> GetOnAccountPaymentsPage(Ctx ctx, int C_BPartner_ID, bool IsCreditNote,
            int C_Currency_ID, int page, int pageSize)
        {
            if (C_BPartner_ID <= 0) return new List<AvailableCreditRow>();
            APInvoicePanelData head = new APInvoicePanelData
            {
                AD_Client_ID = ctx.GetAD_Client_ID(),
                C_Currency_ID = C_Currency_ID
            };
            List<AvailableCreditRow> rows = LoadOnAccountPayments(ctx, head.AD_Client_ID, C_BPartner_ID,
                IsCreditNote, page, pageSize > 0 ? pageSize : ONACCOUNT_PAGE_SIZE);
            foreach (AvailableCreditRow c in rows) ConvertRowToInvoiceCurrency(ctx, c, head);
            return rows;
        }

        /// <summary>Open vendor credit notes available to apply on an AP invoice.</summary>
        private List<AvailableCreditRow> LoadVendorCreditNotes(Ctx ctx, int AD_Client_ID, int C_BPartner_ID)
        {
            List<AvailableCreditRow> list = new List<AvailableCreditRow>();
            string sql = @"SELECT
                              cn.C_Invoice_ID,
                              cn.AD_Org_ID,
                              cn.DocumentNo,
                              cndt.Name AS DocTypeName,
                              cn.DateInvoiced,
                              cn.DateAcct,
                              cn.C_Currency_ID,
                              cn.C_ConversionType_ID,
                              cncur.CurSymbol AS CurSymbol,
                              cncur.ISO_Code  AS ISO_Code,
                              cncur.StdPrecision AS StdPrecision,
                              COALESCE(cn.GrandTotalAfterWithholding, cn.GrandTotal) AS CreditAmount
                           FROM C_Invoice cn
                           INNER JOIN C_Currency cncur ON (cn.C_Currency_ID = cncur.C_Currency_ID)
                           LEFT JOIN C_DocType cndt ON (cn.C_DocType_ID = cndt.C_DocType_ID)
                           WHERE cn.AD_Client_ID = @AD_Client_ID
                             AND cn.C_BPartner_ID = @C_BPartner_ID
                             AND cn.IsSOTrx = 'N'
                             AND cn.IsReturnTrx = 'Y'
                             AND cn.IsActive = 'Y'
                             AND cn.DocStatus IN ('CO','CL')
                             AND COALESCE(cn.IsPaid, 'N') = 'N'
                           ORDER BY cn.DateAcct, cn.DocumentNo";
            sql = MRole.GetDefault(ctx).AddAccessSQL(sql, "cn", MRole.SQL_FULLYQUALIFIED, MRole.SQL_RO);

            DataSet ds = DB.ExecuteDataset(sql, new SqlParameter[]
            {
                new SqlParameter("@AD_Client_ID", AD_Client_ID),
                new SqlParameter("@C_BPartner_ID", C_BPartner_ID)
            }, null);
            if (ds == null || ds.Tables.Count == 0) return list;

            foreach (DataRow r in ds.Tables[0].Rows)
            {
                int cnId = Util.GetValueOfInt(r["C_Invoice_ID"]);
                decimal credit = Math.Abs(Util.GetValueOfDecimal(r["CreditAmount"])) - GetAllocatedAmount(cnId);
                if (credit <= 0) continue;
                list.Add(new AvailableCreditRow
                {
                    SourceType = "CREDITNOTE",
                    Id = cnId,
                    DocTypeName = Util.GetValueOfString(r["DocTypeName"]),
                    DocumentNo = Util.GetValueOfString(r["DocumentNo"]),
                    Date = Util.GetValueOfDateTime(r["DateInvoiced"]),
                    DateAcct = Util.GetValueOfDateTime(r["DateAcct"]),
                    AvailableAmount = credit,
                    AD_Org_ID = Util.GetValueOfInt(r["AD_Org_ID"]),
                    C_Currency_ID = Util.GetValueOfInt(r["C_Currency_ID"]),
                    C_ConversionType_ID = Util.GetValueOfInt(r["C_ConversionType_ID"]),
                    CurSymbol = Util.GetValueOfString(r["CurSymbol"]),
                    ISO_Code = Util.GetValueOfString(r["ISO_Code"]),
                    StdPrecision = Util.GetValueOfInt(r["StdPrecision"])
                });
            }
            return list;
        }

        /// <summary>Open AP invoices of the same vendor for credit-note allocation.</summary>
        private List<OpenInvoiceRow> LoadOpenInvoicesForCredit(Ctx ctx, int AD_Client_ID, int C_BPartner_ID, int C_Invoice_ID)
        {
            List<OpenInvoiceRow> list = new List<OpenInvoiceRow>();
            string sql = @"SELECT
                              i.C_Invoice_ID,
                              i.DocumentNo,
                              COALESCE(MIN(ips.DueDate), MIN(i.DateInvoiced)) AS DueDate,
                              SUM(COALESCE(ips.DueAmt,
                                  COALESCE(i.GrandTotalAfterWithholding, i.GrandTotal))) AS OpenAmount
                           FROM C_Invoice i
                           INNER JOIN C_InvoicePaySchedule ips ON (i.C_Invoice_ID = ips.C_Invoice_ID)
                           WHERE i.AD_Client_ID = @AD_Client_ID
                             AND i.C_BPartner_ID = @C_BPartner_ID
                             AND i.IsSOTrx = 'N'
                             AND COALESCE(i.IsReturnTrx, 'N') = 'N'
                             AND i.IsActive = 'Y'
                             AND i.DocStatus IN ('CO','CL')
                             AND COALESCE(i.IsPaid, 'N') = 'N'
                             AND COALESCE(ips.VA009_IsPaid, 'N') = 'N'
                             AND i.C_Invoice_ID <> @C_Invoice_ID";
            sql = MRole.GetDefault(ctx).AddAccessSQL(sql, "i", MRole.SQL_FULLYQUALIFIED, MRole.SQL_RO);
            sql += @" GROUP BY i.C_Invoice_ID, i.DocumentNo
                           ORDER BY i.DocumentNo";

            DataSet ds = DB.ExecuteDataset(sql, new SqlParameter[]
            {
                new SqlParameter("@AD_Client_ID", AD_Client_ID),
                new SqlParameter("@C_BPartner_ID", C_BPartner_ID),
                new SqlParameter("@C_Invoice_ID", C_Invoice_ID)
            }, null);
            if (ds == null || ds.Tables.Count == 0) return list;

            foreach (DataRow r in ds.Tables[0].Rows)
            {
                decimal open = Util.GetValueOfDecimal(r["OpenAmount"]);
                if (open <= 0) continue;
                list.Add(new OpenInvoiceRow
                {
                    C_Invoice_ID = Util.GetValueOfInt(r["C_Invoice_ID"]),
                    DocumentNo = Util.GetValueOfString(r["DocumentNo"]),
                    DueDate = Util.GetValueOfDateTime(r["DueDate"]),
                    OpenAmount = open
                });
            }
            return list;
        }

        /// <summary>Active bank accounts (deposit/withdraw targets).</summary>
        private List<BankAccountOption> LoadBankAccounts(MRole role, int AD_Org_ID)
        {
            List<BankAccountOption> list = new List<BankAccountOption>();
            // Bank accounts of the invoice organization (or shared org 0), further
            // restricted to the orgs the role can access.
            string sql = @"SELECT ba.C_BankAccount_ID, ba.AccountNo, b.Name AS BankName,
                                  ba.C_Currency_ID, cur.ISO_Code AS CurrencyISO,
                                  cur.CurSymbol AS CurSymbol, cur.StdPrecision AS StdPrecision, ba.IsDefault
                             FROM C_BankAccount ba
                             INNER JOIN C_Bank b ON (ba.C_Bank_ID = b.C_Bank_ID)
                             INNER JOIN C_Currency cur ON (ba.C_Currency_ID = cur.C_Currency_ID)
                            WHERE ba.IsActive = 'Y'
                              AND ba.AD_Org_ID IN (0, @AD_Org_ID)
                            ORDER BY ba.IsDefault DESC, b.Name";
            sql = role.AddAccessSQL(sql, "ba", MRole.SQL_FULLYQUALIFIED, MRole.SQL_RO);

            DataSet ds = DB.ExecuteDataset(sql,
                new SqlParameter[] { new SqlParameter("@AD_Org_ID", AD_Org_ID) }, null);
            if (ds == null || ds.Tables.Count == 0) return list;
            foreach (DataRow r in ds.Tables[0].Rows)
            {
                list.Add(new BankAccountOption
                {
                    C_BankAccount_ID = Util.GetValueOfInt(r["C_BankAccount_ID"]),
                    BankName = Util.GetValueOfString(r["BankName"]),
                    AccountNo = Util.GetValueOfString(r["AccountNo"]),
                    C_Currency_ID = Util.GetValueOfInt(r["C_Currency_ID"]),
                    CurrencyISO = Util.GetValueOfString(r["CurrencyISO"]),
                    CurSymbol = Util.GetValueOfString(r["CurSymbol"]),
                    StdPrecision = Util.GetValueOfInt(r["StdPrecision"]),
                    IsDefault = Util.GetValueOfString(r["IsDefault"]) == "Y"
                });
            }
            return list;
        }

        /// <summary>Active payment methods (with base type for tender).</summary>
        private List<PaymentMethodOption> LoadPaymentMethods(MRole role, int AD_Org_ID)
        {
            List<PaymentMethodOption> list = new List<PaymentMethodOption>();
            // Payment methods of the invoice organization (or shared org 0), further
            // restricted to the orgs the role can access.
            string sql = @"SELECT pm.VA009_PaymentMethod_ID, pm.VA009_Name, pm.VA009_PaymentBaseType
                             FROM VA009_PaymentMethod pm
                            WHERE pm.IsActive = 'Y' AND pm.VA009_PaymentBaseType NOT IN ('B' , 'C')
                              AND pm.AD_Org_ID IN (0, @AD_Org_ID)
                            ORDER BY pm.VA009_Name";
            try
            {
                sql = role.AddAccessSQL(sql, "pm", MRole.SQL_FULLYQUALIFIED, MRole.SQL_RO);
                DataSet ds = DB.ExecuteDataset(sql,
                    new SqlParameter[] { new SqlParameter("@AD_Org_ID", AD_Org_ID) }, null);
                if (ds == null || ds.Tables.Count == 0) return list;
                foreach (DataRow r in ds.Tables[0].Rows)
                {
                    list.Add(new PaymentMethodOption
                    {
                        VA009_PaymentMethod_ID = Util.GetValueOfInt(r["VA009_PaymentMethod_ID"]),
                        Name = Util.GetValueOfString(r["VA009_Name"]),
                        BaseType = Util.GetValueOfString(r["VA009_PaymentBaseType"])
                    });
                }
            }
            catch (Exception ex)
            {
                log.Info("VAS_065 payment methods skipped: " + ex.Message);
            }
            return list;
        }

        /// <summary>
        /// Converts an invoice open amount into a chosen target currency on a given
        /// date using a chosen conversion type (both selected on the modal). Returns
        /// the converted amount plus the target currency symbol / ISO / precision so
        /// the modal can re-format the payment amount field. When the target currency
        /// matches the invoice currency the amount is returned unchanged (rate = 1).
        /// </summary>
        /// <param name="ctx">session context</param>
        /// <param name="req">invoice, target currency, conversion type, amount (invoice currency) and date</param>
        /// <returns>converted amount + target currency metadata</returns>
        public ConvertAmountResult ConvertOpenAmount(Ctx ctx, ConvertAmountRequest req)
        {
            ConvertAmountResult res = new ConvertAmountResult();
            if (req == null || req.C_Invoice_ID <= 0 || req.C_Currency_ID <= 0)
            {
                res.Message = Msg.GetMsg(ctx, "VAS_065_InvalidRequest");
                return res;
            }

            // Invoice currency + client/org.
            DataSet ds = DB.ExecuteDataset(
                @"SELECT C_Currency_ID, AD_Client_ID, AD_Org_ID
                    FROM C_Invoice WHERE C_Invoice_ID = @C_Invoice_ID",
                new SqlParameter[] { new SqlParameter("@C_Invoice_ID", req.C_Invoice_ID) }, null);
            if (ds == null || ds.Tables.Count == 0 || ds.Tables[0].Rows.Count == 0)
            {
                res.Message = Msg.GetMsg(ctx, "VAS_065_InvoiceNotFound");
                return res;
            }
            DataRow ir = ds.Tables[0].Rows[0];
            int invCurrency = Util.GetValueOfInt(ir["C_Currency_ID"]);
            int adClient = Util.GetValueOfInt(ir["AD_Client_ID"]);
            int adOrg = Util.GetValueOfInt(ir["AD_Org_ID"]);

            int targetCurrency = req.C_Currency_ID;
            int convType = req.C_ConversionType_ID;   // 0 -> default conversion type

            MCurrency targetCur = MCurrency.Get(ctx, targetCurrency);
            res.C_Currency_ID = targetCurrency;
            res.CurSymbol = targetCur.GetCurSymbol();
            res.ISO_Code = targetCur.GetISO_Code();
            res.StdPrecision = targetCur.GetStdPrecision();

            DateTime convDate = req.Date ?? DateTime.Today;

            if (targetCurrency == invCurrency)
            {
                res.IsSameCurrency = true;
                res.Rate = 1m;
                res.ConvertedAmount = req.Amount;
                res.Success = true;
                return res;
            }

            decimal rate = MConversionRate.GetRate(invCurrency, targetCurrency, convDate, convType, adClient, adOrg);
            if (rate == 0)
            {
                res.Message = Msg.GetMsg(ctx, "VAS_065_NoConversionRate");
                return res;
            }
            res.Rate = rate;
            res.ConvertedAmount = MConversionRate.Convert(ctx, req.Amount, invCurrency, targetCurrency,
                convDate, convType, adClient, adOrg);
            res.Success = true;
            return res;
        }

        #endregion

        #region Write actions

        /// <summary>
        /// Creates a single balanced allocation that applies the selected
        /// on-account vendor payments and/or vendor credit notes to the target
        /// AP invoice, up to the invoice open amount.
        /// </summary>
        /// <param name="ctx">session context</param>
        /// <param name="req">target invoice + selected sources</param>
        /// <returns>applied + remaining amounts and the allocation document no.</returns>
        public AllocationResult ApplyCredits(Ctx ctx, ApplyCreditsRequest req)
        {
            AllocationResult result = new AllocationResult();
            if (req == null || req.C_Invoice_ID <= 0 || req.Sources == null || req.Sources.Count == 0)
            {
                result.Message = Msg.GetMsg(ctx, "VAS_065_NothingSelected");
                return result;
            }

            Trx trx = Trx.GetTrx(Trx.CreateTrxName("VAS065ApplyCredits"));
            try
            {
                MInvoice inv = new MInvoice(ctx, req.C_Invoice_ID, trx);
                if (inv.GetC_Invoice_ID() <= 0 || inv.IsSOTrx())
                {
                    result.Message = Msg.GetMsg(ctx, "VAS_065_InvoiceNotFound");
                    trx.Rollback();
                    trx.Close();
                    trx = null;
                    return result;
                }

                int invCurrency = inv.GetC_Currency_ID();

                decimal open = Math.Abs(inv.GetGrandTotal(false)) - GetAllocatedAmount(req.C_Invoice_ID);
                if (open <= 0)
                {
                    result.Message = Msg.GetMsg(ctx, "VAS_065_NoOpenBalance");
                    trx.Rollback();
                    trx.Close();
                    trx = null;
                    return result;
                }

                // The allocation is created in the source (payment / credit note) currency,
                // dated to the source document date, booked in the source organization and
                // using the source conversion type. The UI restricts a selection to a single
                // currency + conversion type, so these come from the selected sources.
                int allocCurrency = invCurrency;
                DateTime allocDate = DateTime.Today;
                int allocOrg = inv.GetAD_Org_ID();
                int allocConvType = inv.GetC_ConversionType_ID();
                if (!ResolveSourceCurrencyDate(req.Sources, trx, out allocCurrency, out allocDate, out allocOrg, out allocConvType))
                {
                    result.Message = Msg.GetMsg(ctx, "VAS_065_SingleCurrencyOnly");
                    trx.Rollback();
                    trx.Close();
                    trx = null;
                    return result;
                }
                if (allocOrg <= 0) allocOrg = inv.GetAD_Org_ID();
                // Fall back to the invoice conversion type when the source has none.
                int useConvType = allocConvType > 0 ? allocConvType : inv.GetC_ConversionType_ID();

                // Invoice open amount expressed in the allocation (payment) currency. The
                // source amounts already arrive in the payment currency, so ONLY the invoice
                // open / schedule dues are converted - the source amounts are used as-is,
                // which avoids a double conversion of the source amount.
                decimal openAlloc = open;
                if (allocCurrency != invCurrency)
                {
                    if (MConversionRate.GetRate(invCurrency, allocCurrency, allocDate, useConvType,
                            inv.GetAD_Client_ID(), allocOrg) == 0)
                    {
                        result.Message = Msg.GetMsg(ctx, "VAS_065_NoConversionRate");
                        trx.Rollback();
                        trx.Close();
                        trx = null;
                        return result;
                    }
                    openAlloc = MConversionRate.Convert(ctx, open, invCurrency, allocCurrency, allocDate, useConvType,
                        inv.GetAD_Client_ID(), allocOrg);
                }

                // Open invoice pay-schedules (due-date order). Each schedule is loaded so it
                // can be split BEFORE the allocation line is created; schedRemaining tracks
                // the unallocated balance in the allocation (payment) currency.
                List<MInvoicePaySchedule> schedObjs = new List<MInvoicePaySchedule>();
                List<decimal> schedRemaining = new List<decimal>();
                DataSet schedDs = DB.ExecuteDataset(
                    @"SELECT C_InvoicePaySchedule_ID, DueAmt FROM C_InvoicePaySchedule
                       WHERE C_Invoice_ID = @C_Invoice_ID AND COALESCE(VA009_IsPaid,'N') = 'N'
                       ORDER BY DueDate, C_InvoicePaySchedule_ID",
                    new SqlParameter[] { new SqlParameter("@C_Invoice_ID", req.C_Invoice_ID) }, trx);
                if (schedDs != null && schedDs.Tables.Count > 0)
                {
                    foreach (DataRow sr in schedDs.Tables[0].Rows)
                    {
                        decimal due = Util.GetValueOfDecimal(sr["DueAmt"]);
                        if (allocCurrency != invCurrency)
                            due = MConversionRate.Convert(ctx, due, invCurrency, allocCurrency, allocDate, useConvType,
                                inv.GetAD_Client_ID(), allocOrg);
                        schedObjs.Add(new MInvoicePaySchedule(ctx, Util.GetValueOfInt(sr["C_InvoicePaySchedule_ID"]), trx));
                        schedRemaining.Add(due);
                    }
                }

                MAllocationHdr hdr = new MAllocationHdr(ctx, true, allocDate,
                    allocCurrency, Msg.GetMsg(ctx, "VAS_065_AllocApplyCredits"), trx);
                // Allocation is booked in the payment (source) organization.
                hdr.SetAD_Org_ID(allocOrg);
                // Use the source conversion type for the allocation -> invoice currency rate.
                if (useConvType > 0) hdr.SetC_ConversionType_ID(useConvType);
                if (!hdr.Save(trx))
                {
                    result.Message = RetrieveErr(ctx, "VAS_065_AllocationFailed");
                    trx.Rollback();
                    trx.Close();
                    trx = null;
                    return result;
                }

                // Source amounts are already in the allocation (payment) currency; cap them
                // against the invoice open (also in the payment currency) and cascade across
                // the open schedules - splitting each schedule before the line so every line
                // references a schedule sized exactly to its portion.
                decimal appliedAlloc = 0m, remainingAlloc = openAlloc;
                int schedIdx = 0;
                foreach (AllocationSource src in req.Sources)
                {
                    if (remainingAlloc <= 0) break;
                    decimal useAlloc = Math.Min(src.Amount, remainingAlloc);
                    if (useAlloc <= 0) continue;

                    decimal srcRem = useAlloc;
                    while (srcRem > 0 && schedIdx < schedObjs.Count)
                    {
                        if (schedRemaining[schedIdx] <= 0) { schedIdx++; continue; }
                        decimal portion = Math.Min(srcRem, schedRemaining[schedIdx]);
                        bool fullConsume = portion >= schedRemaining[schedIdx] - (decimal)0.0001;
                        decimal portionInv = portion;
                        if (allocCurrency != invCurrency)
                        {
                            portionInv = MConversionRate.Convert(ctx, portion, allocCurrency, invCurrency, allocDate, useConvType,
                                inv.GetAD_Client_ID(), allocOrg);
                        }

                        bool ok;
                        int refSchedId = PrepareScheduleLine(ctx, schedObjs, schedIdx, portionInv, fullConsume, trx, result, out ok);
                        if (!ok)
                        {
                            // PrepareScheduleLine already rolled back on failure.
                            trx.Close();
                            trx = null;
                            return result;
                        }
                        if (!CreateCreditAllocLines(ctx, hdr, inv, src, portion, refSchedId, trx, result))
                        {
                            // CreateCreditAllocLines already rolled back on failure.
                            trx.Close();
                            trx = null;
                            return result;
                        }
                        schedRemaining[schedIdx] -= portion;
                        srcRem -= portion;
                        if (schedRemaining[schedIdx] <= 0)
                        {
                            schedIdx++;
                        }
                    }
                    // No (more) schedules: book the remainder against the invoice only.
                    //if (srcRem > 0)
                    //{
                    //    if (!CreateCreditAllocLines(ctx, hdr, inv, src, srcRem, 0, trx, result))
                    //    {
                    //        // CreateCreditAllocLines already rolled back on failure.
                    //        trx.Close();
                    //        trx = null;
                    //        return result;
                    //    }
                    //}

                    appliedAlloc += useAlloc;
                    remainingAlloc -= useAlloc;
                }

                if (appliedAlloc <= 0)
                {
                    result.Message = Msg.GetMsg(ctx, "VAS_065_NothingSelected");
                    trx.Rollback();
                    trx.Close();
                    trx = null;
                    return result;
                }

                if (!CompleteAllocation(hdr, trx, ctx, result))
                {
                    // CompleteAllocation already rolled back on failure.
                    trx.Close();
                    trx = null;
                    return result;
                }

                // Applied amount returned in the invoice currency for the UI settlement
                // (single back-conversion of the applied total - not of each source amount).
                decimal appliedInv = appliedAlloc;
                if (allocCurrency != invCurrency)
                    appliedInv = MConversionRate.Convert(ctx, appliedAlloc, allocCurrency, invCurrency, allocDate, useConvType,
                        inv.GetAD_Client_ID(), allocOrg);

                trx.Commit();
                result.Success = true;
                result.DocumentNo = hdr.GetDocumentNo();
                result.AppliedAmount = appliedInv;
                result.RemainingAmount = Math.Max(0m, open - appliedInv);
            }
            catch (Exception ex)
            {
                try { if (trx != null) trx.Rollback(); } catch { /* ignore */ }
                result.Message = ex.Message;
            }
            finally
            {
                // trx was started -> it must be closed and nulled before returning
                // (runs on every exit path, including the early validation returns).
                if (trx != null) { try { trx.Close(); } catch { /* ignore */ } trx = null; }
            }
            return result;
        }

        /// <summary>
        /// Allocates the current AP credit note to one or more selected open AP
        /// invoices of the same vendor through a single balanced allocation.
        /// </summary>
        /// <param name="ctx">session context</param>
        /// <param name="req">credit note id + selected open invoices</param>
        /// <returns>applied + remaining credit and the allocation document no.</returns>
        public AllocationResult AllocateCreditNote(Ctx ctx, AllocateCreditNoteRequest req)
        {
            AllocationResult result = new AllocationResult();
            if (req == null || req.C_Invoice_ID <= 0 || req.C_Invoice_IDs == null || req.C_Invoice_IDs.Count == 0)
            {
                result.Message = Msg.GetMsg(ctx, "VAS_065_NothingSelected");
                return result;
            }

            Trx trx = Trx.GetTrx(Trx.CreateTrxName("VAS065AllocateCN"));
            try
            {
                MInvoice cn = new MInvoice(ctx, req.C_Invoice_ID, trx);
                if (cn.GetC_Invoice_ID() <= 0 || cn.IsSOTrx() || !cn.IsReturnTrx())
                {
                    result.Message = Msg.GetMsg(ctx, "VAS_065_InvoiceNotFound");
                    return result;
                }

                decimal creditOpen = Math.Abs(cn.GetGrandTotal(false)) - GetAllocatedAmount(req.C_Invoice_ID);
                if (creditOpen <= 0)
                {
                    result.Message = Msg.GetMsg(ctx, "VAS_065_NoOpenBalance");
                    return result;
                }

                // The allocation is created in the credit-note currency; the target open
                // invoices (and their schedules) are converted into this currency on the
                // allocation date using the credit-note conversion type.
                int allocCurrency = cn.GetC_Currency_ID();
                DateTime allocDate = DateTime.Today;
                int allocConvType = cn.GetC_ConversionType_ID();

                MAllocationHdr hdr = new MAllocationHdr(ctx, true, allocDate,
                    allocCurrency, Msg.GetMsg(ctx, "VAS_065_AllocCreditNote"), trx);
                hdr.SetAD_Org_ID(cn.GetAD_Org_ID());
                // Use the credit note conversion type for the allocation currency rate.
                if (allocConvType > 0) hdr.SetC_ConversionType_ID(allocConvType);
                if (!hdr.Save(trx))
                {
                    result.Message = RetrieveErr(ctx, "VAS_065_AllocationFailed");
                    trx.Rollback();
                    return result;
                }

                decimal applied = 0m, remaining = creditOpen;
                foreach (int openInvId in req.C_Invoice_IDs)
                {
                    if (remaining <= 0) break;

                    // Target invoice currency / client / org.
                    DataSet tds = DB.ExecuteDataset(
                        "SELECT C_Currency_ID, AD_Client_ID, AD_Org_ID FROM C_Invoice WHERE C_Invoice_ID = @id",
                        new SqlParameter[] { new SqlParameter("@id", openInvId) }, trx);
                    if (tds == null || tds.Tables.Count == 0 || tds.Tables[0].Rows.Count == 0) continue;
                    int tCur = Util.GetValueOfInt(tds.Tables[0].Rows[0]["C_Currency_ID"]);
                    int tClient = Util.GetValueOfInt(tds.Tables[0].Rows[0]["AD_Client_ID"]);
                    int tOrg = Util.GetValueOfInt(tds.Tables[0].Rows[0]["AD_Org_ID"]);

                    decimal tOpen = Math.Abs(GetInvoiceOpen(ctx, openInvId, trx));   // target currency
                    if (tOpen <= 0) continue;

                    // Target open expressed in the allocation (credit-note) currency.
                    decimal tOpenAlloc = tOpen;
                    if (tCur != allocCurrency)
                    {
                        if (MConversionRate.GetRate(tCur, allocCurrency, allocDate, allocConvType, tClient, tOrg) == 0)
                        {
                            result.Message = Msg.GetMsg(ctx, "VAS_065_NoConversionRate");
                            trx.Rollback();
                            return result;
                        }
                        tOpenAlloc = MConversionRate.Convert(ctx, tOpen, tCur, allocCurrency, allocDate, allocConvType, tClient, tOrg);
                    }

                    decimal use = Math.Min(tOpenAlloc, remaining);
                    if (use <= 0) continue;

                    // Open schedules of the target invoice. Loaded so each can be split BEFORE
                    // its allocation line; schedRemaining tracks the unallocated balance in the
                    // allocation (credit-note) currency.
                    List<MInvoicePaySchedule> schedObjs = new List<MInvoicePaySchedule>();
                    List<decimal> schedRemaining = new List<decimal>();
                    DataSet schedDs = DB.ExecuteDataset(
                        @"SELECT C_InvoicePaySchedule_ID, DueAmt FROM C_InvoicePaySchedule
                           WHERE C_Invoice_ID = @id AND COALESCE(VA009_IsPaid,'N') = 'N'
                           ORDER BY DueDate, C_InvoicePaySchedule_ID",
                        new SqlParameter[] { new SqlParameter("@id", openInvId) }, trx);
                    if (schedDs != null && schedDs.Tables.Count > 0)
                    {
                        foreach (DataRow sr in schedDs.Tables[0].Rows)
                        {
                            decimal due = Util.GetValueOfDecimal(sr["DueAmt"]);
                            if (tCur != allocCurrency)
                                due = MConversionRate.Convert(ctx, due, tCur, allocCurrency, allocDate, allocConvType, tClient, tOrg);
                            schedObjs.Add(new MInvoicePaySchedule(ctx, Util.GetValueOfInt(sr["C_InvoicePaySchedule_ID"]), trx));
                            schedRemaining.Add(due);
                        }
                    }

                    // Cascade the applied amount across the target schedules, splitting each
                    // schedule before its line so every line references a schedule sized
                    // exactly to its portion (in the target invoice currency).
                    decimal srcRem = use;
                    int schedIdx = 0;
                    while (srcRem > 0 && schedIdx < schedObjs.Count)
                    {
                        if (schedRemaining[schedIdx] <= 0) { schedIdx++; continue; }
                        decimal portion = Math.Min(srcRem, schedRemaining[schedIdx]);
                        bool fullConsume = portion >= schedRemaining[schedIdx] - (decimal)0.0001;
                        decimal portionInv = portion;
                        if (tCur != allocCurrency)
                            portionInv = MConversionRate.Convert(ctx, portion, allocCurrency, tCur, allocDate, allocConvType, tClient, tOrg);
                        bool ok;
                        int refSchedId = PrepareScheduleLine(ctx, schedObjs, schedIdx, portionInv, fullConsume, trx, result, out ok);
                        if (!ok) return result;
                        if (!CreateCreditNoteAllocLines(ctx, hdr, cn.GetC_BPartner_ID(), openInvId, req.C_Invoice_ID, portion, refSchedId, trx, result))
                            return result;
                        schedRemaining[schedIdx] -= portion;
                        srcRem -= portion;
                        if (schedRemaining[schedIdx] <= 0) schedIdx++;
                    }
                    if (srcRem > 0)
                    {
                        if (!CreateCreditNoteAllocLines(ctx, hdr, cn.GetC_BPartner_ID(), openInvId, req.C_Invoice_ID, srcRem, 0, trx, result))
                            return result;
                    }

                    applied += use;
                    remaining -= use;
                }

                if (applied <= 0)
                {
                    result.Message = Msg.GetMsg(ctx, "VAS_065_NothingSelected");
                    trx.Rollback();
                    return result;
                }

                if (!CompleteAllocation(hdr, trx, ctx, result)) return result;

                trx.Commit();
                result.Success = true;
                result.DocumentNo = hdr.GetDocumentNo();
                result.AppliedAmount = applied;
                result.RemainingAmount = Math.Max(0m, creditOpen - applied);
            }
            catch (Exception ex)
            {
                try { if (trx != null) trx.Rollback(); } catch { /* ignore */ }
                result.Message = ex.Message;
            }
            finally
            {
                // trx was started -> it must be closed and nulled before returning
                // (runs on every exit path, including the early validation returns).
                if (trx != null) { try { trx.Close(); } catch { /* ignore */ } trx = null; }
            }
            return result;
        }

        /// <summary>
        /// Creates and completes a vendor payment (C_Payment, IsReceipt='N')
        /// for the AP invoice net payable and allocates it against the invoice
        /// schedule(s). Overpayment is left on the payment as a vendor advance.
        /// </summary>
        /// <param name="ctx">session context</param>
        /// <param name="req">payment fields</param>
        /// <returns>payment document no. and amount</returns>
        public RecordPaymentResult RecordPayment(Ctx ctx, RecordPaymentRequest req)
        {
            RecordPaymentResult result = new RecordPaymentResult();
            if (req == null || req.C_Invoice_ID <= 0)
            {
                result.Message = Msg.GetMsg(ctx, "VAS_065_InvalidRequest");
                return result;
            }

            Trx trx = Trx.GetTrx(Trx.CreateTrxName("VAS065RecordPayment"));
            try
            {
                MInvoice inv = new MInvoice(ctx, req.C_Invoice_ID, trx);
                if (inv.GetC_Invoice_ID() <= 0 || inv.IsSOTrx())
                {
                    result.Message = Msg.GetMsg(ctx, "VAS_065_InvoiceNotFound");
                    return result;
                }
                if (inv.GetDocStatus() != "CO" && inv.GetDocStatus() != "CL")
                {
                    result.Message = Msg.GetMsg(ctx, "VAS_065_InvoiceNotCompleted");
                    return result;
                }

                decimal open = GetInvoiceOpen(ctx, req.C_Invoice_ID, trx);
                if (open <= 0)
                {
                    result.Message = Msg.GetMsg(ctx, "VAS_065_NoOpenBalance");
                    return result;
                }

                // Payment is recorded in the bank-account currency (req.C_Currency_ID).
                // When it differs from the invoice currency every amount below is in the
                // payment currency, so the invoice open amount and each schedule due
                // amount are converted on the payment date using the invoice conversion
                // type (matching the standard payment-screen behaviour).
                int invCurrency = inv.GetC_Currency_ID();
                int payCurrency = req.C_Currency_ID > 0 ? req.C_Currency_ID : invCurrency;
                DateTime txDate = req.DateTrx ?? DateTime.Today;
                // Use the conversion type chosen on the modal; fall back to the invoice's.
                int convTypeId = req.C_ConversionType_ID > 0 ? req.C_ConversionType_ID : inv.GetC_ConversionType_ID();

                decimal openPay = open;
                if (payCurrency != invCurrency)
                {
                    decimal payRate = MConversionRate.GetRate(invCurrency, payCurrency, txDate, convTypeId,
                        inv.GetAD_Client_ID(), inv.GetAD_Org_ID());
                    if (payRate == 0)
                    {
                        result.Message = Msg.GetMsg(ctx, "VAS_065_NoConversionRate");
                        return result;
                    }
                    openPay = MConversionRate.Convert(ctx, open, invCurrency, payCurrency, txDate, convTypeId,
                        inv.GetAD_Client_ID(), inv.GetAD_Org_ID());
                }

                decimal payAmt = req.PayAmt > 0 ? req.PayAmt : openPay;
                if (payAmt <= 0)
                {
                    result.Message = Msg.GetMsg(ctx, "VAS_065_AmountMustBePositive");
                    return result;
                }
                // Case 4: the amount cannot exceed the open invoice due amount
                // (compared in the payment currency).
                if (payAmt - openPay > (decimal)0.0001)
                {
                    result.Message = Msg.GetMsg(ctx, "VAS_065_AmountExceedsOpen");
                    return result;
                }

                // AP payment doc type.
                int appDocType = Util.GetValueOfInt(DB.ExecuteScalar(
                    @"SELECT C_DocType_ID FROM C_DocType
                       WHERE IsActive = 'Y' AND DocBaseType = 'APP'
                         AND AD_Client_ID = @AD_Client_ID AND AD_Org_ID IN (0, @AD_Org_ID)
                       ORDER BY IsDefault DESC, AD_Org_ID DESC",
                    new SqlParameter[]
                    {
                        new SqlParameter("@AD_Client_ID", ctx.GetAD_Client_ID()),
                        new SqlParameter("@AD_Org_ID", inv.GetAD_Org_ID())
                    }, trx));

                string baseType = "";
                if (req.VA009_PaymentMethod_ID > 0)
                {
                    baseType = Util.GetValueOfString(DB.ExecuteScalar(
                        "SELECT VA009_PaymentBaseType FROM VA009_PaymentMethod WHERE VA009_PaymentMethod_ID = @id",
                        new SqlParameter[] { new SqlParameter("@id", req.VA009_PaymentMethod_ID) }, trx));
                }

                MPayment payment = new MPayment(ctx, 0, trx);
                payment.SetAD_Client_ID(inv.GetAD_Client_ID());
                payment.SetAD_Org_ID(inv.GetAD_Org_ID());
                payment.SetIsReceipt(false);                 // AP -> outgoing
                if (appDocType > 0)
                {
                    payment.SetC_DocType_ID(appDocType);
                }
                if (req.C_BankAccount_ID > 0)
                {
                    payment.SetC_BankAccount_ID(req.C_BankAccount_ID);
                }
                payment.SetC_BPartner_ID(inv.GetC_BPartner_ID());
                payment.SetC_BPartner_Location_ID(inv.GetC_BPartner_Location_ID());
                payment.SetC_Currency_ID(req.C_Currency_ID > 0 ? req.C_Currency_ID : inv.GetC_Currency_ID());
                if (convTypeId > 0)
                {
                    payment.SetC_ConversionType_ID(convTypeId);
                }
                if (req.VA009_PaymentMethod_ID > 0)
                {
                    payment.SetVA009_PaymentMethod_ID(req.VA009_PaymentMethod_ID);
                }

                payment.SetDateTrx(txDate);
                payment.SetDateAcct(txDate);
                payment.SetPayAmt(inv.IsReturnTrx() ? decimal.Negate(payAmt) : payAmt);
                if (req.DiscountAmt > 0)
                {
                    payment.SetDiscountAmt(inv.IsReturnTrx() ? decimal.Negate(req.DiscountAmt) : req.DiscountAmt);
                }

                // VA009_PaymentBaseType code 'S' = Check (per the codebase remap in
                // MPaymentModel.GetBPartnerDetail; it maps to tender type 'K').
                if (baseType == "S")
                {
                    // Cheque payments require a check date and a reference (cheque no).
                    if (!req.CheckDate.HasValue || string.IsNullOrEmpty(req.ReferenceNo))
                    {
                        result.Message = Msg.GetMsg(ctx, "VAS_065_CheckDateRefRequired");
                        trx.Rollback();
                        return result;
                    }
                    payment.SetTenderType(MPayment.TENDERTYPE_Check);
                    payment.SetCheckDate(req.CheckDate.Value);
                    payment.SetCheckNo(req.ReferenceNo);
                }
                else
                {
                    //payment.SetTenderType(MPayment.TENDERTYPE_DirectDeposit);
                    if (!string.IsNullOrEmpty(req.ReferenceNo))
                    {
                        payment.SetTrxNo(req.ReferenceNo);
                    }
                }

                // Resolve the amount against unpaid pay schedules in due-date order
                // (lowest first). The amount cascades: each schedule is filled up to its
                // DueAmt before the next one is touched.
                DataSet schedDs = DB.ExecuteDataset(
                    @"SELECT C_InvoicePaySchedule_ID, DueAmt FROM C_InvoicePaySchedule
                       WHERE C_Invoice_ID = @C_Invoice_ID AND COALESCE(VA009_IsPaid,'N') = 'N'
                       ORDER BY DueDate, C_InvoicePaySchedule_ID",
                    new SqlParameter[] { new SqlParameter("@C_Invoice_ID", req.C_Invoice_ID) }, trx);

                List<int> planSchedId = new List<int>();
                List<decimal> planDue = new List<decimal>();
                List<decimal> planAlloc = new List<decimal>();
                if (schedDs != null && schedDs.Tables.Count > 0)
                {
                    decimal rem = payAmt;
                    foreach (DataRow sr in schedDs.Tables[0].Rows)
                    {
                        if (rem <= 0) break;
                        decimal due = Util.GetValueOfDecimal(sr["DueAmt"]);
                        // Schedule due amounts are stored in the invoice currency; express
                        // them in the payment currency so the cascade and the over/under
                        // amounts stay consistent with the recorded payment.
                        if (payCurrency != invCurrency)
                        {
                            due = MConversionRate.Convert(ctx, due, invCurrency, payCurrency, txDate, convTypeId,
                                inv.GetAD_Client_ID(), inv.GetAD_Org_ID());
                        }
                        decimal alloc = Math.Min(rem, due);
                        if (alloc <= 0) continue;
                        planSchedId.Add(Util.GetValueOfInt(sr["C_InvoicePaySchedule_ID"]));
                        planDue.Add(due);
                        planAlloc.Add(alloc);
                        rem -= alloc;
                    }
                }

                if (planSchedId.Count <= 1)
                {
                    // Case 1 (amount == schedule due) and Case 2 (amount < schedule due):
                    // a single schedule is settled directly on the payment header.
                    payment.SetC_Invoice_ID(req.C_Invoice_ID);
                    if (planSchedId.Count == 1)
                    {
                        payment.SetC_InvoicePaySchedule_ID(planSchedId[0]);
                        // OverUnder = DueAmt - Amount - Discount (>0 underpayment, 0 when exact).
                        decimal ou = planDue[0] - planAlloc[0] - req.DiscountAmt;
                        payment.SetOverUnderAmt(inv.IsReturnTrx() ? decimal.Negate(ou) : ou);
                        payment.SetIsOverUnderPayment(ou != 0);
                    }
                }
                else
                {
                    // Case 3: amount spans several schedules -> applied via payment allocate
                    // lines below; the header itself carries no invoice/schedule link. The
                    // discount is distributed onto the allocate lines (MPayment.AllocateIt
                    // uses the per-line discount, not the header), so clear the header
                    // discount here to avoid counting it twice.
                    payment.SetOverUnderAmt(0);
                    payment.SetIsOverUnderPayment(false);
                    payment.SetDiscountAmt(0);
                }

                payment.SetDocStatus(MPayment.DOCSTATUS_InProgress);
                payment.SetDocAction(MPayment.DOCACTION_Complete);
                if (!payment.Save(trx))
                {
                    result.Message = RetrieveErr(ctx, "VAS_065_PaymentSaveFailed");
                    trx.Rollback();
                    return result;
                }

                if (planSchedId.Count > 1)
                {
                    // Cascade the cash discount across the schedules that actually have a
                    // payment allocate entry (planSchedId), in order: each schedule absorbs
                    // discount up to its due amount (a schedule's due is never less than the
                    // discount applied to it) and any remainder carries to the next schedule.
                    // Discount is never applied to a schedule with no payment entry, so any
                    // leftover after the last covered schedule is simply not applied. Shares
                    // are positive magnitudes (compared on absolute values, since credit-note
                    // dues are negative); the credit-note sign is applied when the value is set
                    // on the allocate line.
                    //   e.g. discount 20, schedule 1 due 100 -> 20 on schedule 1.
                    //        discount 20, schedule 1 due 15  -> 15 on schedule 1, 5 on the next.
                    decimal[] discShare = new decimal[planSchedId.Count];
                    decimal remDisc = req.DiscountAmt;
                    for (int i = 0; i < planSchedId.Count && remDisc > 0; i++)
                    {
                        decimal share = Math.Min(remDisc, Math.Abs(planDue[i]));
                        discShare[i] = share;
                        remDisc -= share;
                    }

                    for (int i = 0; i < planSchedId.Count; i++)
                    {
                        decimal due = planDue[i];
                        decimal alloc = planAlloc[i];
                        decimal disc = discShare[i];

                        MPaymentAllocate pa = new MPaymentAllocate(ctx, 0, trx);
                        pa.SetAD_Client_ID(payment.GetAD_Client_ID());
                        pa.SetAD_Org_ID(payment.GetAD_Org_ID());
                        pa.SetC_Payment_ID(payment.GetC_Payment_ID());
                        pa.SetC_Invoice_ID(req.C_Invoice_ID);
                        pa.SetC_InvoicePaySchedule_ID(planSchedId[i]);
                        pa.SetAmount(inv.IsReturnTrx() ? decimal.Negate(alloc) : alloc);
                        pa.SetDiscountAmt(inv.IsReturnTrx() ? decimal.Negate(disc) : disc);
                        // Keep Amount + Discount + OverUnderAmt = InvoiceAmt balanced
                        // (OverUnderAmt = the shortfall, 0 when the schedule is fully settled).
                        pa.SetOverUnderAmt(inv.IsReturnTrx() ? decimal.Negate(due - alloc - disc) : (due - alloc - disc));
                        pa.SetInvoiceAmt(pa.GetAmount() + pa.GetDiscountAmt() + pa.GetWriteOffAmt() + pa.GetOverUnderAmt());
                        if (!pa.Save(trx))
                        {
                            result.Message = RetrieveErr(ctx, "VAS_065_PaymentSaveFailed");
                            trx.Rollback();
                            return result;
                        }
                    }
                }

                bool processed;
                try
                {
                    processed = payment.ProcessIt(MPayment.DOCACTION_Complete);
                }
                catch (Exception pex)
                {
                    processed = false;
                    result.Message = pex.Message;
                }
                if (!processed)
                {
                    result.Message = payment.GetProcessMsg();
                    if (string.IsNullOrEmpty(result.Message))
                    {
                        result.Message = RetrieveErr(ctx, "VAS_065_PaymentCompleteFailed");
                    }
                    trx.Rollback();
                    return result;
                }
                payment.Save(trx);

                trx.Commit();
                result.Success = true;
                result.C_Payment_ID = payment.GetC_Payment_ID();
                result.DocumentNo = payment.GetDocumentNo();
                result.PayAmt = payAmt;
            }
            catch (Exception ex)
            {
                try
                {
                    if (trx != null)
                    {
                        trx.Rollback();
                    }
                }
                catch
                { /* ignore */
                }
                result.Message = ex.Message;
            }
            finally
            {
                // trx was started -> it must be closed and nulled before returning
                // (runs on every exit path, including the early validation returns).
                if (trx != null)
                {
                    try
                    {
                        trx.Close();
                    }
                    catch
                    { /* ignore */
                    }
                    trx = null;
                }
            }
            return result;
        }

        /// <summary>
        /// Prepares the invoice pay-schedule an allocation line will reference, splitting it
        /// BEFORE the line is created (mirrors PaymentAllocation.cs). When the portion only
        /// partly settles the schedule, the EXISTING schedule (at <paramref name="idx"/>) is
        /// resized to the paid portion (in invoice currency) - this is the schedule the line
        /// references - and a NEW schedule is created for the remaining open balance, which
        /// replaces the list slot so the next portion splits the remainder. When the portion
        /// settles the whole remaining due, the existing schedule is referenced as-is.
        /// Returns the C_InvoicePaySchedule_ID to set on the allocation line (0 on failure).
        /// </summary>
        private int PrepareScheduleLine(Ctx ctx, List<MInvoicePaySchedule> schedObjs, int idx, decimal portionInv, bool fullConsume, Trx trx, AllocationResult result, out bool ok)
        {
            ok = true;
            MInvoicePaySchedule sch = schedObjs[idx];
            if (fullConsume)
            {
                // Whole remaining due is settled - reference the schedule itself (no resize,
                // so a cross-currency full payment does not alter the invoice schedule).
                return sch.GetC_InvoicePaySchedule_ID();
            }

            decimal curDue = sch.GetDueAmt();

            // New schedule carrying the remaining open balance; it becomes the current
            // schedule so subsequent portions split it further.
            MInvoicePaySchedule remainder = new MInvoicePaySchedule(ctx, 0, trx);
            PO.CopyValues(sch, remainder);
            remainder.SetAD_Client_ID(sch.GetAD_Client_ID());
            remainder.SetAD_Org_ID(sch.GetAD_Org_ID());
            remainder.ByPassValidatePayScheduleCondition(true);
            remainder.SetDueAmt(curDue - portionInv);
            if (!remainder.Save(trx))
            {
                result.Message = RetrieveErr(ctx, "VAS_065_AllocationFailed");
                trx.Rollback();
                ok = false;
                return 0;
            }

            // Resize the existing schedule to the paid portion - the line references this.
            sch.ByPassValidatePayScheduleCondition(true);
            sch.SetDueAmt(portionInv);
            if (!sch.Save(trx))
            {
                result.Message = RetrieveErr(ctx, "VAS_065_AllocationFailed");
                trx.Rollback();
                ok = false;
                return 0;
            }

            int refId = sch.GetC_InvoicePaySchedule_ID();
            schedObjs[idx] = remainder;   // next portion splits the remainder
            return refId;
        }

        /// <summary>
        /// Resolves the common currency, the (first) source document date and the
        /// (first) source organization for the selected allocation sources. The
        /// allocation is created in this currency, dated to this date and booked in
        /// this organization. Returns false when the selection mixes currencies (the
        /// UI already restricts this, so it is a defensive guard).
        /// </summary>
        private bool ResolveSourceCurrencyDate(List<AllocationSource> sources, Trx trx, out int currency, out DateTime date, out int org, out int convType)
        {
            currency = 0;
            date = DateTime.Today;
            org = 0;
            convType = 0;
            bool first = true;
            foreach (AllocationSource src in sources)
            {
                int cur, srcOrg, srcConv;
                DateTime? dt;
                if (src.SourceType == "PAYMENT")
                {
                    DataSet ds = DB.ExecuteDataset("SELECT C_Currency_ID, DateTrx, AD_Org_ID, COALESCE(C_ConversionType_ID,0) AS C_ConversionType_ID FROM C_Payment WHERE C_Payment_ID = @id",
                        new SqlParameter[] { new SqlParameter("@id", src.Id) }, trx);
                    if (ds == null || ds.Tables.Count == 0 || ds.Tables[0].Rows.Count == 0) continue;
                    cur = Util.GetValueOfInt(ds.Tables[0].Rows[0]["C_Currency_ID"]);
                    dt = Util.GetValueOfDateTime(ds.Tables[0].Rows[0]["DateTrx"]);
                    srcOrg = Util.GetValueOfInt(ds.Tables[0].Rows[0]["AD_Org_ID"]);
                    srcConv = Util.GetValueOfInt(ds.Tables[0].Rows[0]["C_ConversionType_ID"]);
                }
                else
                {
                    DataSet ds = DB.ExecuteDataset("SELECT C_Currency_ID, DateInvoiced, AD_Org_ID, COALESCE(C_ConversionType_ID,0) AS C_ConversionType_ID FROM C_Invoice WHERE C_Invoice_ID = @id",
                        new SqlParameter[] { new SqlParameter("@id", src.Id) }, trx);
                    if (ds == null || ds.Tables.Count == 0 || ds.Tables[0].Rows.Count == 0) continue;
                    cur = Util.GetValueOfInt(ds.Tables[0].Rows[0]["C_Currency_ID"]);
                    dt = Util.GetValueOfDateTime(ds.Tables[0].Rows[0]["DateInvoiced"]);
                    srcOrg = Util.GetValueOfInt(ds.Tables[0].Rows[0]["AD_Org_ID"]);
                    srcConv = Util.GetValueOfInt(ds.Tables[0].Rows[0]["C_ConversionType_ID"]);
                }
                if (first)
                {
                    currency = cur;
                    if (dt.HasValue) date = dt.Value;
                    org = srcOrg;
                    convType = srcConv;
                    first = false;
                }
                else if (cur != currency || srcConv != convType)
                {
                    return false;   // mixed currency / conversion type in one allocation
                }
            }
            return currency > 0;
        }

        /// <summary>
        /// Creates the allocation line(s) that apply <paramref name="amount"/> (allocation
        /// currency) of a single source to the target invoice, optionally referencing the
        /// invoice pay-schedule being settled. On-account payments produce one line linked
        /// to the payment; vendor credit notes produce a balanced pair (invoice +, credit
        /// note -). Returns false (and rolls back) on save failure.
        /// </summary>
        private bool CreateCreditAllocLines(Ctx ctx, MAllocationHdr hdr, MInvoice inv, AllocationSource src,
            decimal amount, int C_InvoicePaySchedule_ID, Trx trx, AllocationResult result)
        {
            //if (src.SourceType == "PAYMENT")
            //{
            // On-account payment -> invoice: a single line carrying the positive applied
            // amount with both the invoice and payment references. The posting derives
            // DR/CR from those references, so the amount is NOT negated (matches the
            // payment->invoice line in PaymentAllocation.SavePaymentData).
            MAllocationLine line = new MAllocationLine(hdr, inv.IsReturnTrx() ? amount : decimal.Negate(amount), Env.ZERO, Env.ZERO, Env.ZERO);
            line.SetDocInfo(inv.GetC_BPartner_ID(), 0, inv.GetC_Invoice_ID());
            line.SetPaymentInfo(src.Id, 0);
            if (C_InvoicePaySchedule_ID > 0) line.SetC_InvoicePaySchedule_ID(C_InvoicePaySchedule_ID);
            if (!line.Save(trx))
            {
                result.Message = RetrieveErr(ctx, "VAS_065_AllocationFailed");
                trx.Rollback();
                return false;
            }
            //}
            //else // CREDITNOTE -> balanced invoice-to-invoice pair (invoice +, credit note -)
            //{
            //    // Credit note -> invoice is an invoice-to-invoice settlement: the two lines
            //    // must cross-reference each other via Ref_C_Invoice_ID (as SavePaymentData
            //    // does), otherwise the allocation is not tracked/posted as a paired netting.
            //    MAllocationLine inLine = new MAllocationLine(hdr, amount, Env.ZERO, Env.ZERO, Env.ZERO);
            //    inLine.SetDocInfo(inv.GetC_BPartner_ID(), 0, inv.GetC_Invoice_ID());
            //    inLine.SetPaymentInfo(src.Id, 0);
            //    if (C_InvoicePaySchedule_ID > 0) inLine.SetC_InvoicePaySchedule_ID(C_InvoicePaySchedule_ID);
            //    if (!inLine.Save(trx)) { result.Message = RetrieveErr(ctx, "VAS_065_AllocationFailed"); trx.Rollback(); return false; }

            //    //MAllocationLine cnLine = new MAllocationLine(hdr, Decimal.Negate(amount), Env.ZERO, Env.ZERO, Env.ZERO);
            //    //cnLine.SetDocInfo(inv.GetC_BPartner_ID(), 0, src.Id);
            //    //if (!cnLine.Save(trx)) { result.Message = RetrieveErr(ctx, "VAS_065_AllocationFailed"); trx.Rollback(); return false; }
            //}
            return true;
        }

        /// <summary>
        /// Creates the balanced allocation line pair for a credit-note allocation: a
        /// positive line on the target invoice (optionally referencing the schedule it
        /// settles) and a negative line on the credit note. Returns false on save failure.
        /// </summary>
        private bool CreateCreditNoteAllocLines(Ctx ctx, MAllocationHdr hdr, int C_BPartner_ID,
            int targetInvoiceId, int creditNoteId, decimal amount, int C_InvoicePaySchedule_ID, Trx trx, AllocationResult result)
        {
            MAllocationLine inLine = new MAllocationLine(hdr, amount, Env.ZERO, Env.ZERO, Env.ZERO);
            inLine.SetDocInfo(C_BPartner_ID, 0, targetInvoiceId);
            if (C_InvoicePaySchedule_ID > 0) inLine.SetC_InvoicePaySchedule_ID(C_InvoicePaySchedule_ID);
            if (!inLine.Save(trx)) { result.Message = RetrieveErr(ctx, "VAS_065_AllocationFailed"); trx.Rollback(); return false; }

            MAllocationLine cnLine = new MAllocationLine(hdr, Decimal.Negate(amount), Env.ZERO, Env.ZERO, Env.ZERO);
            cnLine.SetDocInfo(C_BPartner_ID, 0, creditNoteId);
            if (!cnLine.Save(trx)) { result.Message = RetrieveErr(ctx, "VAS_065_AllocationFailed"); trx.Rollback(); return false; }
            return true;
        }

        /// <summary>Open amount for an invoice from its unpaid schedules (else grand total - allocated).</summary>
        private decimal GetInvoiceOpen(Ctx ctx, int C_Invoice_ID, Trx trx)
        {
            decimal sched = Util.GetValueOfDecimal(DB.ExecuteScalar(
                @"SELECT COALESCE(SUM(ips.DueAmt), 0) FROM C_InvoicePaySchedule ips
                   WHERE ips.C_Invoice_ID = @C_Invoice_ID AND COALESCE(ips.VA009_IsPaid,'N') = 'N'",
                new SqlParameter[] { new SqlParameter("@C_Invoice_ID", C_Invoice_ID) }, trx));
            if (sched > 0) return sched;

            decimal grand = Util.GetValueOfDecimal(DB.ExecuteScalar(
                @"SELECT COALESCE(GrandTotalAfterWithholding, GrandTotal) FROM C_Invoice WHERE C_Invoice_ID = @C_Invoice_ID",
                new SqlParameter[] { new SqlParameter("@C_Invoice_ID", C_Invoice_ID) }, trx));
            return Math.Max(0m, Math.Abs(grand) - GetAllocatedAmount(C_Invoice_ID));
        }

        /// <summary>Processes (completes) an allocation header, rolling back on failure.</summary>
        private bool CompleteAllocation(MAllocationHdr hdr, Trx trx, Ctx ctx, AllocationResult result)
        {
            bool ok;
            try { ok = hdr.ProcessIt(MAllocationHdr.DOCACTION_Complete); }
            catch (Exception ex) { ok = false; result.Message = ex.Message; }
            if (!ok)
            {
                if (string.IsNullOrEmpty(result.Message))
                    result.Message = RetrieveErr(ctx, "VAS_065_AllocationFailed");
                trx.Rollback();
                return false;
            }
            hdr.Save(trx);
            return true;
        }

        /// <summary>Builds an error string from the logger, falling back to a message key.</summary>
        private string RetrieveErr(Ctx ctx, string fallbackKey)
        {
            ValueNamePair pp = VLogger.RetrieveError();
            string baseMsg = Msg.GetMsg(ctx, fallbackKey);
            return (pp != null && !string.IsNullOrEmpty(pp.GetName()))
                   ? baseMsg + ": " + pp.GetName() : baseMsg;
        }

        #endregion

        #region DTOs

        public class APInvoicePanelData
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
            public decimal NetPayable { get; set; }
            public decimal WithholdingAmount { get; set; }
            public decimal OpenAmount { get; set; }
            public bool IsPaid { get; set; }
            public bool IsReturnTrx { get; set; }
            public bool IsAPCreditNote { get; set; }
            public bool IsAPInvoice { get; set; }
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
            public bool ScheduleAnyHold { get; set; }
            public GoodsReceiptInfo GoodsReceipt { get; set; }
            public LandedCostInfo LandedCost { get; set; }
            public PostedJournalInfo PostedJournal { get; set; }
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
            public DateTime? DueDate { get; set; }
            public decimal DueAmt { get; set; }
            public decimal DiscountAmt { get; set; }
            public bool IsPaid { get; set; }
            public bool IsHold { get; set; }
            public string Status { get; set; }   // Paid | OnHold | Open
        }

        public class GoodsReceiptInfo
        {
            public bool IsCleanMatch { get; set; }
            public string ReceiptDocumentNo { get; set; }
            public string OrderDocumentNo { get; set; }
            public DateTime? ReceivedDate { get; set; }
            // Distinct received dates across the matched lines (comma-separated in the UI).
            public List<DateTime> ReceivedDates { get; set; }
            public decimal TotalReceived { get; set; }
            public string WarehouseName { get; set; }
            public decimal PriceVariance { get; set; }
            public decimal QtyVariance { get; set; }
            public int LineCount { get; set; }
            public List<GoodsReceiptRow> Rows { get; set; }
        }

        public class GoodsReceiptRow
        {
            public string ProductName { get; set; }
            public string ReceiptDocumentNo { get; set; }
            public string OrderDocumentNo { get; set; }
            public DateTime? ReceivedDate { get; set; }
            public decimal ReceivedQty { get; set; }
            public string WarehouseName { get; set; }
            public decimal PriceVariance { get; set; }
            public decimal QuantityVariance { get; set; }
        }

        public class LandedCostInfo
        {
            public string SourceDocumentNo { get; set; }
            public int ItemCount { get; set; }
            public decimal TotalPurchaseValue { get; set; }
            public decimal TotalLandedCost { get; set; }
            public decimal TotalInventoryCost { get; set; }
            public List<LandedCostRow> Rows { get; set; }
        }

        public class LandedCostRow
        {
            public string ItemName { get; set; }
            public decimal QtyEntered { get; set; }
            public string UOMSymbol { get; set; }
            public string SourceDocumentNo { get; set; }
            public decimal PurchaseValue { get; set; }
            public decimal AllocatedLandedCost { get; set; }
            public decimal InventoryCost { get; set; }
            public decimal UnitInventoryCost { get; set; }
        }

        public class PostedJournalInfo
        {
            public decimal TotalDr { get; set; }
            public decimal TotalCr { get; set; }
            public DateTime? PostingDate { get; set; }
            public string PeriodName { get; set; }
            // Base/accounting currency (primary accounting schema) the posted amounts are in.
            public string CurSymbol { get; set; }
            public int StdPrecision { get; set; }
            public List<JournalRow> Rows { get; set; }   // first page only
            public int Total { get; set; }               // total fact lines (server-side paging)
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

        public class PaymentModalMeta
        {
            public int C_Invoice_ID { get; set; }
            public int C_BPartner_ID { get; set; }
            public int C_Currency_ID { get; set; }
            public string CurSymbol { get; set; }
            public string ISO_Code { get; set; }
            public int StdPrecision { get; set; }
            // Invoice conversion type - used to default the modal's conversion-type select.
            public int C_ConversionType_ID { get; set; }
            public string RecordMode { get; set; }
            public bool IsAPCreditNote { get; set; }
            public decimal VendorOutstanding { get; set; }
            public decimal GrossInvoice { get; set; }
            public decimal Withholding { get; set; }
            public decimal NetPayable { get; set; }
            public decimal NetOpenAmount { get; set; }
            // True when nothing is left to settle (invoice flagged paid or every pay
            // schedule paid). The modal then shows the New Payment section read-only.
            public bool IsFullySettled { get; set; }
            public decimal AvailableToApply { get; set; }
            public decimal CreditNoteAmount { get; set; }
            public decimal OpenInvoicesAvailable { get; set; }
            public List<AvailableCreditRow> OnAccountPayments { get; set; }   // first page only
            public int OnAccountPaymentsTotal { get; set; }                  // total (server-side paging)
            public List<AvailableCreditRow> CreditNotes { get; set; }
            public List<OpenInvoiceRow> OpenInvoices { get; set; }
            public List<BankAccountOption> BankAccounts { get; set; }
            public List<PaymentMethodOption> PaymentMethods { get; set; }
            public List<CurrencyOption> Currencies { get; set; }
            public List<ConversionTypeOption> ConversionTypes { get; set; }
        }

        public class CurrencyOption
        {
            public int C_Currency_ID { get; set; }
            public string ISO_Code { get; set; }
            public string CurSymbol { get; set; }
            public int StdPrecision { get; set; }
        }

        public class ConversionTypeOption
        {
            public int C_ConversionType_ID { get; set; }
            public string Name { get; set; }
            public bool IsDefault { get; set; }
        }

        public class AvailableCreditRow
        {
            public string SourceType { get; set; }   // PAYMENT | CREDITNOTE
            public int Id { get; set; }
            public int AD_Org_ID { get; set; }
            // Document type name of the source (e.g. "AP Payment", "AP Credit Memo").
            // Shown with the document no in the UI instead of a generic literal.
            public string DocTypeName { get; set; }
            public string DocumentNo { get; set; }
            public DateTime? Date { get; set; }
            // Accounting date of the source document. Cross-currency selections are
            // restricted to a single accounting date (one conversion date per allocation).
            public DateTime? DateAcct { get; set; }
            public decimal AvailableAmount { get; set; }
            // Currency of the source document (the payment / credit note).
            public int C_Currency_ID { get; set; }
            public int C_ConversionType_ID { get; set; }
            public string CurSymbol { get; set; }
            public string ISO_Code { get; set; }
            public int StdPrecision { get; set; }
            // AvailableAmount converted into the invoice currency on the current date
            // (0 when no conversion rate is available). The UI shows / sums this value
            // with the invoice currency symbol.
            public decimal AvailableAmountInv { get; set; }
        }

        public class OpenInvoiceRow
        {
            public int C_Invoice_ID { get; set; }
            public string DocumentNo { get; set; }
            public DateTime? DueDate { get; set; }
            public decimal OpenAmount { get; set; }
        }

        public class BankAccountOption
        {
            public int C_BankAccount_ID { get; set; }
            public string BankName { get; set; }
            public string AccountNo { get; set; }
            public int C_Currency_ID { get; set; }
            public string CurrencyISO { get; set; }
            public string CurSymbol { get; set; }
            public int StdPrecision { get; set; }
            public bool IsDefault { get; set; }
        }

        public class PaymentMethodOption
        {
            public int VA009_PaymentMethod_ID { get; set; }
            public string Name { get; set; }
            public string BaseType { get; set; }
        }

        public class ApplyCreditsRequest
        {
            public int C_Invoice_ID { get; set; }
            public List<AllocationSource> Sources { get; set; }
        }

        public class AllocationSource
        {
            public string SourceType { get; set; } // PAYMENT | CREDITNOTE
            public int Id { get; set; }
            public decimal Amount { get; set; }
        }

        public class AllocateCreditNoteRequest
        {
            public int C_Invoice_ID { get; set; }   // the credit note
            public List<int> C_Invoice_IDs { get; set; }   // selected open invoices
        }

        public class RecordPaymentRequest
        {
            public int C_Invoice_ID { get; set; }
            public decimal PayAmt { get; set; }
            public int C_Currency_ID { get; set; }
            public int C_ConversionType_ID { get; set; }   // selected conversion type (0 = default/invoice)
            public int C_BankAccount_ID { get; set; }
            public int VA009_PaymentMethod_ID { get; set; }
            public DateTime? DateTrx { get; set; }
            public decimal DiscountAmt { get; set; }
            public string ReferenceNo { get; set; }
            // Cheque date - mandatory (with ReferenceNo) when the payment method is a cheque.
            public DateTime? CheckDate { get; set; }
        }

        public class AllocationResult
        {
            public bool Success { get; set; }
            public string DocumentNo { get; set; }
            public decimal AppliedAmount { get; set; }
            public decimal RemainingAmount { get; set; }
            public string Message { get; set; }
        }

        public class RecordPaymentResult
        {
            public bool Success { get; set; }
            public int C_Payment_ID { get; set; }
            public string DocumentNo { get; set; }
            public decimal PayAmt { get; set; }
            public string Message { get; set; }
        }

        public class ConvertAmountRequest
        {
            public int C_Invoice_ID { get; set; }
            public int C_Currency_ID { get; set; }        // target (selected) currency
            public int C_ConversionType_ID { get; set; }  // selected conversion type (0 = default)
            public decimal Amount { get; set; }           // amount in the invoice (from) currency
            public DateTime? Date { get; set; }           // payment date driving the rate lookup
        }

        public class ConvertAmountResult
        {
            public bool Success { get; set; }
            public decimal ConvertedAmount { get; set; }
            public decimal Rate { get; set; }       // invoice -> bank multiply rate (1 when same)
            public int C_Currency_ID { get; set; }  // bank-account currency
            public string CurSymbol { get; set; }
            public string ISO_Code { get; set; }
            public int StdPrecision { get; set; }
            public bool IsSameCurrency { get; set; }
            public string Message { get; set; }
        }

        #endregion
    }
}
