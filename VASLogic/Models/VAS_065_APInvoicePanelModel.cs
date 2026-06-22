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
            data.PaymentSchedule = LoadSchedule(C_Invoice_ID);
            data.GoodsReceipt = LoadGoodsReceipt(C_Invoice_ID);
            data.LandedCost = LoadLandedCost(C_Invoice_ID);
            data.PostedJournal = LoadPostedJournal(ctx, C_Invoice_ID);
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
            string sql = @"SELECT COALESCE(SUM(ABS(al.Amount)), 0)
                             FROM C_AllocationLine al
                             INNER JOIN C_AllocationHdr ah ON (al.C_AllocationHdr_ID = ah.C_AllocationHdr_ID)
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
        private List<ScheduleRow> LoadSchedule(int C_Invoice_ID)
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

                if (string.IsNullOrEmpty(gr.ReceiptDocumentNo)) gr.ReceiptDocumentNo = row.ReceiptDocumentNo;
                if (string.IsNullOrEmpty(gr.OrderDocumentNo)) gr.OrderDocumentNo = row.OrderDocumentNo;
                if (!gr.ReceivedDate.HasValue) gr.ReceivedDate = row.ReceivedDate;
                if (string.IsNullOrEmpty(gr.WarehouseName)) gr.WarehouseName = row.WarehouseName;
            }
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
                               and lca.m_product_id = il.m_product_id and lca.m_attributesetinstance_id = il.m_attributesetinstance_id)
                               INNER JOIN c_invoiceline lcail ON (lcail.c_invoiceline_id = lca.c_invoiceline_id and lcail.vas_islandedcost = 'Y')
                               INNER JOIN c_invoice lcaci ON (lcail.c_invoice_id = lcaci.c_invoice_id)
                               INNER JOIN M_Product pr ON (il.M_Product_ID = pr.M_Product_ID)
                               INNER JOIN C_UOM uom ON (il.C_UOM_ID     = uom.C_UOM_ID)
                               WHERE il.C_Invoice_ID = @C_Invoice_ID
                                 AND il.IsActive = 'Y'
                                 AND il.M_Product_ID IS NOT NULL
                               ORDER BY il.Line";

                DataSet ds = DB.ExecuteDataset(sql,
                    new SqlParameter[] { new SqlParameter("@C_Invoice_ID", C_Invoice_ID) }, null);
                if (ds == null || ds.Tables.Count == 0) return lc;

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
        private PostedJournalInfo LoadPostedJournal(Ctx ctx, int C_Invoice_ID)
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
                           ORDER BY fa.AmtAcctDr DESC";

            sql = MRole.GetDefault(ctx).AddAccessSQL(
                sql, "fa", MRole.SQL_FULLYQUALIFIED, MRole.SQL_RO);

            DataSet ds = DB.ExecuteDataset(sql,
                new SqlParameter[] { new SqlParameter("@C_Invoice_ID", C_Invoice_ID) }, null);
            if (ds == null || ds.Tables.Count == 0) return pj;

            decimal totDr = 0m, totCr = 0m;
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
                totDr += jr.AmtAcctDr;
                totCr += jr.AmtAcctCr;
                if (!pj.PostingDate.HasValue) pj.PostingDate = Util.GetValueOfDateTime(r["DateAcct"]);
                if (string.IsNullOrEmpty(pj.PeriodName)) pj.PeriodName = Util.GetValueOfString(r["PeriodName"]);
                if (string.IsNullOrEmpty(pj.CurSymbol))
                {
                    pj.CurSymbol = Util.GetValueOfString(r["AcctCurSymbol"]);
                    pj.StdPrecision = Util.GetValueOfInt(r["AcctStdPrecision"]);
                }
            }
            pj.TotalDr = totDr;
            pj.TotalCr = totCr;
            return pj;
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
            if (head.C_Invoice_ID <= 0) return meta;

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

            if (head.IsAPCreditNote)
            {
                meta.CreditNoteAmount = Math.Abs(head.NetPayable) - GetAllocatedAmount(C_Invoice_ID);
                if (meta.CreditNoteAmount < 0) meta.CreditNoteAmount = 0m;
                meta.OpenInvoices = LoadOpenInvoicesForCredit(ctx, head.AD_Client_ID, head.C_BPartner_ID, C_Invoice_ID);
                decimal totalOpen = 0m;
                foreach (OpenInvoiceRow oi in meta.OpenInvoices) totalOpen += oi.OpenAmount;
                meta.OpenInvoicesAvailable = totalOpen;
            }
            else
            {
                meta.NetOpenAmount = head.OpenAmount;
                meta.OnAccountPayments = LoadOnAccountPayments(ctx, head.AD_Client_ID, head.C_BPartner_ID);
                meta.CreditNotes = LoadVendorCreditNotes(ctx, head.AD_Client_ID, head.C_BPartner_ID);
                decimal avail = 0;
                foreach (AvailableCreditRow c in meta.OnAccountPayments)
                    avail += c.AvailableAmount;
                foreach (AvailableCreditRow c in meta.CreditNotes)
                    avail += c.AvailableAmount;
                meta.AvailableToApply = avail;
                meta.BankAccounts = LoadBankAccounts(role);
                meta.PaymentMethods = LoadPaymentMethods(role);
            }
            return meta;
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

        /// <summary>Available (unallocated) on-account vendor payments.</summary>
        private List<AvailableCreditRow> LoadOnAccountPayments(Ctx ctx, int AD_Client_ID, int C_BPartner_ID)
        {
            List<AvailableCreditRow> list = new List<AvailableCreditRow>();
            string sql = @"SELECT
                              p.C_Payment_ID,
                              p.DocumentNo,
                              p.DateTrx,
                              (ABS(COALESCE(p.PayAmt, 0)) - ABS(COALESCE(p.AllocatedAmt, 0))) AS AvailableAmount
                           FROM C_Payment p
                           WHERE p.AD_Client_ID = @AD_Client_ID
                             AND p.C_BPartner_ID = @C_BPartner_ID
                             AND p.IsReceipt = 'N'
                             AND p.IsActive = 'Y'
                             AND p.DocStatus IN ('CO','CL')
                             AND COALESCE(p.IsAllocated, 'N') = 'N'
                             AND p.C_Invoice_ID IS NULL
                             AND p.C_Charge_ID IS NULL
                             AND p.C_Order_ID IS NULL
                           ORDER BY p.DateAcct, p.DocumentNo";
            sql = MRole.GetDefault(ctx).AddAccessSQL(sql, "p", MRole.SQL_FULLYQUALIFIED, MRole.SQL_RO);

            DataSet ds = DB.ExecuteDataset(sql, new SqlParameter[]
            {
                new SqlParameter("@AD_Client_ID", AD_Client_ID),
                new SqlParameter("@C_BPartner_ID", C_BPartner_ID)
            }, null);
            if (ds == null || ds.Tables.Count == 0) return list;

            foreach (DataRow r in ds.Tables[0].Rows)
            {
                decimal avail = Util.GetValueOfDecimal(r["AvailableAmount"]);
                if (avail <= 0) continue;
                list.Add(new AvailableCreditRow
                {
                    SourceType = "PAYMENT",
                    Id = Util.GetValueOfInt(r["C_Payment_ID"]),
                    DocumentNo = Util.GetValueOfString(r["DocumentNo"]),
                    Date = Util.GetValueOfDateTime(r["DateTrx"]),
                    AvailableAmount = avail
                });
            }
            return list;
        }

        /// <summary>Open vendor credit notes available to apply on an AP invoice.</summary>
        private List<AvailableCreditRow> LoadVendorCreditNotes(Ctx ctx, int AD_Client_ID, int C_BPartner_ID)
        {
            List<AvailableCreditRow> list = new List<AvailableCreditRow>();
            string sql = @"SELECT
                              cn.C_Invoice_ID,
                              cn.DocumentNo,
                              cn.DateInvoiced,
                              COALESCE(cn.GrandTotalAfterWithholding, cn.GrandTotal) AS CreditAmount
                           FROM C_Invoice cn
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
                    DocumentNo = Util.GetValueOfString(r["DocumentNo"]),
                    Date = Util.GetValueOfDateTime(r["DateInvoiced"]),
                    AvailableAmount = credit
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
                             AND i.C_Invoice_ID <> @C_Invoice_ID
                           GROUP BY i.C_Invoice_ID, i.DocumentNo
                           ORDER BY i.DocumentNo";
            sql = MRole.GetDefault(ctx).AddAccessSQL(sql, "i", MRole.SQL_FULLYQUALIFIED, MRole.SQL_RO);

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
        private List<BankAccountOption> LoadBankAccounts(MRole role)
        {
            List<BankAccountOption> list = new List<BankAccountOption>();
            string sql = @"SELECT ba.C_BankAccount_ID, ba.AccountNo, b.Name AS BankName,
                                  ba.C_Currency_ID, cur.ISO_Code AS CurrencyISO, ba.IsDefault
                             FROM C_BankAccount ba
                             INNER JOIN C_Bank b ON (ba.C_Bank_ID = b.C_Bank_ID)
                             INNER JOIN C_Currency cur ON (ba.C_Currency_ID = cur.C_Currency_ID)
                            WHERE ba.IsActive = 'Y'
                            ORDER BY ba.IsDefault DESC, b.Name";
            sql = role.AddAccessSQL(sql, "ba", MRole.SQL_FULLYQUALIFIED, MRole.SQL_RO);

            DataSet ds = DB.ExecuteDataset(sql, null, null);
            if (ds == null || ds.Tables.Count == 0) return list;
            foreach (DataRow r in ds.Tables[0].Rows)
            {
                list.Add(new BankAccountOption
                {
                    C_BankAccount_ID = Util.GetValueOfInt(r["C_BankAccount_ID"]),
                    BankName = Util.GetValueOfString(r["BankName"]),
                    AccountNo = Util.GetValueOfString(r["AccountNo"]),
                    CurrencyISO = Util.GetValueOfString(r["CurrencyISO"]),
                    IsDefault = Util.GetValueOfString(r["IsDefault"]) == "Y"
                });
            }
            return list;
        }

        /// <summary>Active payment methods (with base type for tender).</summary>
        private List<PaymentMethodOption> LoadPaymentMethods(MRole role)
        {
            List<PaymentMethodOption> list = new List<PaymentMethodOption>();
            string sql = @"SELECT pm.VA009_PaymentMethod_ID, pm.VA009_Name, pm.VA009_PaymentBaseType
                             FROM VA009_PaymentMethod pm
                            WHERE pm.IsActive = 'Y'
                            ORDER BY pm.VA009_Name";
            try
            {
                sql = role.AddAccessSQL(sql, "pm", MRole.SQL_FULLYQUALIFIED, MRole.SQL_RO);
                DataSet ds = DB.ExecuteDataset(sql, null, null);
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
                    return result;
                }

                decimal open = Math.Abs(inv.GetGrandTotal(false)) - GetAllocatedAmount(req.C_Invoice_ID);
                if (open <= 0)
                {
                    result.Message = Msg.GetMsg(ctx, "VAS_065_NoOpenBalance");
                    return result;
                }

                MAllocationHdr hdr = new MAllocationHdr(ctx, true, DateTime.Today,
                    inv.GetC_Currency_ID(), Msg.GetMsg(ctx, "VAS_065_AllocApplyCredits"), trx);
                hdr.SetAD_Org_ID(inv.GetAD_Org_ID());
                if (!hdr.Save(trx))
                {
                    result.Message = RetrieveErr(ctx, "VAS_065_AllocationFailed");
                    trx.Rollback();
                    return result;
                }

                decimal applied = 0m, remaining = open;
                foreach (AllocationSource src in req.Sources)
                {
                    if (remaining <= 0) break;
                    decimal use = Math.Min(src.Amount, remaining);
                    if (use <= 0) continue;

                    if (src.SourceType == "PAYMENT")
                    {
                        // Payment applied directly to the invoice.
                        MAllocationLine line = new MAllocationLine(hdr, use, Env.ZERO, Env.ZERO, Env.ZERO);
                        line.SetDocInfo(inv.GetC_BPartner_ID(), 0, inv.GetC_Invoice_ID());
                        line.SetPaymentInfo(src.Id, 0);
                        if (!line.Save(trx)) { result.Message = RetrieveErr(ctx, "VAS_065_AllocationFailed"); trx.Rollback(); return result; }
                    }
                    else // CREDITNOTE -> two balanced lines (invoice +, credit note -)
                    {
                        MAllocationLine inLine = new MAllocationLine(hdr, use, Env.ZERO, Env.ZERO, Env.ZERO);
                        inLine.SetDocInfo(inv.GetC_BPartner_ID(), 0, inv.GetC_Invoice_ID());
                        if (!inLine.Save(trx)) { result.Message = RetrieveErr(ctx, "VAS_065_AllocationFailed"); trx.Rollback(); return result; }

                        MAllocationLine cnLine = new MAllocationLine(hdr, Decimal.Negate(use), Env.ZERO, Env.ZERO, Env.ZERO);
                        cnLine.SetDocInfo(inv.GetC_BPartner_ID(), 0, src.Id);
                        if (!cnLine.Save(trx)) { result.Message = RetrieveErr(ctx, "VAS_065_AllocationFailed"); trx.Rollback(); return result; }
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
                result.RemainingAmount = Math.Max(0m, open - applied);
            }
            catch (Exception ex)
            {
                try { trx.Rollback(); } catch { /* ignore */ }
                result.Message = ex.Message;
            }
            finally
            {
                try { trx.Close(); } catch { /* ignore */ }
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

                MAllocationHdr hdr = new MAllocationHdr(ctx, true, DateTime.Today,
                    cn.GetC_Currency_ID(), Msg.GetMsg(ctx, "VAS_065_AllocCreditNote"), trx);
                hdr.SetAD_Org_ID(cn.GetAD_Org_ID());
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
                    decimal invOpen = Math.Abs(GetInvoiceOpen(ctx, openInvId, trx));
                    decimal use = Math.Min(invOpen, remaining);
                    if (use <= 0) continue;

                    MAllocationLine inLine = new MAllocationLine(hdr, use, Env.ZERO, Env.ZERO, Env.ZERO);
                    inLine.SetDocInfo(cn.GetC_BPartner_ID(), 0, openInvId);
                    if (!inLine.Save(trx)) { result.Message = RetrieveErr(ctx, "VAS_065_AllocationFailed"); trx.Rollback(); return result; }

                    MAllocationLine cnLine = new MAllocationLine(hdr, Decimal.Negate(use), Env.ZERO, Env.ZERO, Env.ZERO);
                    cnLine.SetDocInfo(cn.GetC_BPartner_ID(), 0, req.C_Invoice_ID);
                    if (!cnLine.Save(trx)) { result.Message = RetrieveErr(ctx, "VAS_065_AllocationFailed"); trx.Rollback(); return result; }

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
                try { trx.Rollback(); } catch { /* ignore */ }
                result.Message = ex.Message;
            }
            finally
            {
                try { trx.Close(); } catch { /* ignore */ }
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

                decimal payAmt = req.PayAmt > 0 ? req.PayAmt : open;
                if (payAmt <= 0)
                {
                    result.Message = Msg.GetMsg(ctx, "VAS_065_AmountMustBePositive");
                    return result;
                }
                // Case 4: the amount cannot exceed the open invoice due amount.
                if (payAmt - open > (decimal)0.0001)
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
                if (inv.GetC_ConversionType_ID() > 0)
                {
                    payment.SetC_ConversionType_ID(inv.GetC_ConversionType_ID());
                }
                if (req.VA009_PaymentMethod_ID > 0)
                {
                    payment.SetVA009_PaymentMethod_ID(req.VA009_PaymentMethod_ID);
                }

                DateTime txDate = req.DateTrx ?? DateTime.Today;
                payment.SetDateTrx(txDate);
                payment.SetDateAcct(txDate);
                payment.SetPayAmt(payAmt);
                if (req.DiscountAmt > 0) payment.SetDiscountAmt(req.DiscountAmt);

                if (baseType.Equals(MPayment.TENDERTYPE_Check))
                {
                    payment.SetTenderType(MPayment.TENDERTYPE_Check);
                    payment.SetCheckDate(txDate);
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
                        // OverUnder = DueAmt - Amount (>0 underpayment, 0 when exact).
                        decimal ou = planDue[0] - planAlloc[0];
                        payment.SetOverUnderAmt(ou);
                        payment.SetIsOverUnderPayment(ou != 0);
                    }
                    //else
                    //{
                    //    // Invoice without an explicit pay schedule.
                    //    decimal ou = open - payAmt;
                    //    payment.SetOverUnderAmt(ou);
                    //    payment.SetIsOverUnderPayment(ou != 0);
                    //}
                }
                else
                {
                    // Case 3: amount spans several schedules -> applied via payment allocate
                    // lines below; the header itself carries no invoice/schedule link.
                    payment.SetOverUnderAmt(0);
                    payment.SetIsOverUnderPayment(false);
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
                    for (int i = 0; i < planSchedId.Count; i++)
                    {
                        decimal due = planDue[i];
                        decimal alloc = planAlloc[i];

                        MPaymentAllocate pa = new MPaymentAllocate(ctx, 0, trx);
                        pa.SetAD_Client_ID(payment.GetAD_Client_ID());
                        pa.SetAD_Org_ID(payment.GetAD_Org_ID());
                        pa.SetC_Payment_ID(payment.GetC_Payment_ID());
                        pa.SetC_Invoice_ID(req.C_Invoice_ID);
                        pa.SetC_InvoicePaySchedule_ID(planSchedId[i]);
                        pa.SetAmount(alloc);
                        pa.SetInvoiceAmt(due);
                        // When this schedule is only partly covered, record the shortfall so
                        // Amount + OverUnderAmt = InvoiceAmt stays balanced (0 when fully paid).
                        pa.SetOverUnderAmt(due - alloc);
                        if (!pa.Save(trx))
                        {
                            result.Message = RetrieveErr(ctx, "VAS_065_PaymentSaveFailed");
                            trx.Rollback();
                            return result;
                        }
                    }
                }

                bool processed;
                try { processed = payment.ProcessIt(MPayment.DOCACTION_Complete); }
                catch (Exception pex) { processed = false; result.Message = pex.Message; }
                if (!processed)
                {
                    if (string.IsNullOrEmpty(result.Message))
                        result.Message = RetrieveErr(ctx, "VAS_065_PaymentCompleteFailed");
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
                try { trx.Rollback(); } catch { /* ignore */ }
                result.Message = ex.Message;
            }
            finally
            {
                try { trx.Close(); } catch { /* ignore */ }
            }
            return result;
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
            public List<ScheduleRow> PaymentSchedule { get; set; }
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
            public List<JournalRow> Rows { get; set; }
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
            public string RecordMode { get; set; }
            public bool IsAPCreditNote { get; set; }
            public decimal VendorOutstanding { get; set; }
            public decimal GrossInvoice { get; set; }
            public decimal Withholding { get; set; }
            public decimal NetPayable { get; set; }
            public decimal NetOpenAmount { get; set; }
            public decimal AvailableToApply { get; set; }
            public decimal CreditNoteAmount { get; set; }
            public decimal OpenInvoicesAvailable { get; set; }
            public List<AvailableCreditRow> OnAccountPayments { get; set; }
            public List<AvailableCreditRow> CreditNotes { get; set; }
            public List<OpenInvoiceRow> OpenInvoices { get; set; }
            public List<BankAccountOption> BankAccounts { get; set; }
            public List<PaymentMethodOption> PaymentMethods { get; set; }
        }

        public class AvailableCreditRow
        {
            public string SourceType { get; set; }   // PAYMENT | CREDITNOTE
            public int Id { get; set; }
            public string DocumentNo { get; set; }
            public DateTime? Date { get; set; }
            public decimal AvailableAmount { get; set; }
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
            public string CurrencyISO { get; set; }
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
            public int C_BankAccount_ID { get; set; }
            public int VA009_PaymentMethod_ID { get; set; }
            public DateTime? DateTrx { get; set; }
            public decimal DiscountAmt { get; set; }
            public string ReferenceNo { get; set; }
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

        #endregion
    }
}
