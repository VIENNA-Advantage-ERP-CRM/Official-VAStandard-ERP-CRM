/// <summary>
/// Module Name : VASLogic
/// Purpose     : Goods Receipt Note (GRN) Overview tab panel data (read side).
///               Returns header identity, vendor, linked purchase order,
///               KPI aggregates (received value / lines / received qty / QC
///               applicable lines), a compact receipt timeline (PO -> expected
///               -> received -> posted) and the material lines for a selected
///               goods receipt (M_InOut with IsSOTrx = 'N').
/// Chronological development:
///   VAI163   2026-07-06  Created. Optional module columns
///                        (VA010_QualityPlan_ID, CurrentCostPrice,
///                        VA024_UnitPrice) are guarded through AD_Column so the
///                        panel works whether or not those modules are installed.
///   VAI163   2026-07-17  ReferenceInvoice now carries the linked AP invoice
///                        document no (M_InOut -> C_Order -> C_Invoice, latest
///                        first) instead of the free-text M_InOut.POReference.
///                        Added RefOrderDocNo (originating sales order, read
///                        through the linked PO's Ref_Order_ID, as VAS_092
///                        does). Supplier* members renamed to Vendor*.
/// </summary>

using System;
using System.Collections.Generic;
using System.Data;
using System.Data.SqlClient;
using System.Text;
using VAdvantage.DataBase;
using VAdvantage.Logging;
using VAdvantage.Model;
using VAdvantage.Utility;

namespace VASLogic.Models
{
    public class VAS_099_OverviewGRNModel
    {
        private static readonly VLogger _log =
            VLogger.GetVLogger(typeof(VAS_099_OverviewGRNModel).FullName);

        /// <summary>
        /// Returns the full overview payload for the selected goods receipt.
        /// MRole access filtering is applied ONLY on the main physical table
        /// (M_InOut alias "io"); the child line query inherits the parent's
        /// authorization and is not separately role-filtered.
        /// </summary>
        /// <param name="ctx">User context (client/org/role).</param>
        /// <param name="M_InOut_ID">Selected goods receipt id.</param>
        /// <returns>Populated <see cref="GRNOverviewData"/>; an empty instance
        /// when the id is invalid or no accessible row is found.</returns>
        public GRNOverviewData GetGRNOverview(Ctx ctx, int M_InOut_ID)
        {
            GRNOverviewData result = new GRNOverviewData();
            if (M_InOut_ID <= 0) return result;

            // Optional module columns on M_InOutLine — resolved once so the SQL
            // below only references columns that actually exist in this schema.
            bool hasQualityPlan = ColumnExists("M_InOutLine", "VA010_QualityPlan_ID");
            bool hasCurrentCost = ColumnExists("M_InOutLine", "CurrentCostPrice");
            bool hasUnitPrice   = ColumnExists("M_InOutLine", "VA024_UnitPrice");

            // COALESCE(ol.PriceActual, [l.CurrentCostPrice], [l.VA024_UnitPrice], 0)
            string rateExpr = BuildRateExpr(hasCurrentCost, hasUnitPrice);
            // 1 when the line carries a quality plan, else 0.
            string qcCountExpr = hasQualityPlan
                ? "CASE WHEN l.VA010_QualityPlan_ID IS NOT NULL THEN 1 ELSE 0 END"
                : "0";

            string sql = @"SELECT
                              io.M_InOut_ID,
                              io.DocumentNo,
                              io.DocStatus,
                              io.Processed,
                              io.Posted,
                              io.MovementDate,
                              io.DateAcct,
                              io.PriorityRule,
                              io.C_Order_ID,
                              po.DocumentNo    AS PO_DocumentNo,
                              po.DateOrdered   AS PODate,
                              po.DatePromised  AS ExpectedDate,
                              refo.DocumentNo  AS RefOrderDocNo,
                              bp.Name          AS VendorName,
                              bp.TaxID         AS VendorTaxID,
                              bpl.Name         AS VendorLocationName,
                              loc.Address1     AS Address1,
                              loc.Address2     AS Address2,
                              loc.City         AS City,
                              loc.Postal       AS Postal,
                              ctry.Name        AS CountryName,
                              reg.Name         AS RegionName,
                              contact.Name     AS ContactName,
                              contact.Phone2   AS ContactPhone,
                              contact.EMail    AS ContactEmail,
                              wh.Name          AS WarehouseName,
                              receiver.Name    AS ReceivedBy,
                              cur.CurSymbol    AS CurSymbol,
                              cur.ISO_Code     AS ISO_Code,
                              cur.StdPrecision AS StdPrecision,
                              (SELECT COUNT(*)
                                 FROM M_InOutLine l
                                WHERE l.M_InOut_ID = io.M_InOut_ID
                                  AND l.IsActive   = 'Y')                       AS LineCount,
                              (SELECT NVL(SUM(NVL(l.MovementQty, 0)), 0)
                                 FROM M_InOutLine l
                                WHERE l.M_InOut_ID = io.M_InOut_ID
                                  AND l.IsActive   = 'Y')                       AS ReceivedQty,
                              (SELECT NVL(SUM(" + qcCountExpr + @"), 0)
                                 FROM M_InOutLine l
                                WHERE l.M_InOut_ID = io.M_InOut_ID
                                  AND l.IsActive   = 'Y')                       AS QcLineCount,
                              (SELECT NVL(SUM(NVL(l.MovementQty, 0) * " + rateExpr + @"), 0)
                                 FROM M_InOutLine l
                                 LEFT OUTER JOIN C_OrderLine ol ON (ol.C_OrderLine_ID = l.C_OrderLine_ID)
                                WHERE l.M_InOut_ID = io.M_InOut_ID
                                  AND l.IsActive   = 'Y')                       AS ReceivedValue
                            FROM M_InOut io
                            INNER JOIN C_BPartner bp        ON (io.C_BPartner_ID          = bp.C_BPartner_ID)
                            LEFT OUTER JOIN C_Order po       ON (io.C_Order_ID            = po.C_Order_ID)
                            LEFT OUTER JOIN C_Order refo     ON (po.Ref_Order_ID          = refo.C_Order_ID)
                            LEFT OUTER JOIN C_BPartner_Location bpl ON (io.C_BPartner_Location_ID = bpl.C_BPartner_Location_ID)
                            LEFT OUTER JOIN C_Location loc   ON (bpl.C_Location_ID         = loc.C_Location_ID)
                            LEFT OUTER JOIN C_Country ctry   ON (loc.C_Country_ID          = ctry.C_Country_ID)
                            LEFT OUTER JOIN C_Region reg     ON (loc.C_Region_ID           = reg.C_Region_ID)
                            LEFT OUTER JOIN AD_User contact  ON (io.AD_User_ID             = contact.AD_User_ID)
                            LEFT OUTER JOIN M_Warehouse wh   ON (io.M_Warehouse_ID         = wh.M_Warehouse_ID)
                            LEFT OUTER JOIN AD_User receiver ON (io.SalesRep_ID            = receiver.AD_User_ID)
                            LEFT OUTER JOIN C_Currency cur   ON (po.C_Currency_ID          = cur.C_Currency_ID)
                            WHERE io.M_InOut_ID = @M_InOut_ID
                              AND io.IsActive   = 'Y'
                              AND io.IsSOTrx    = 'N'";

            // MRole access only on the primary physical table the user is fetching.
            sql = MRole.GetDefault(ctx).AddAccessSQL(
                sql, "io", MRole.SQL_FULLYQUALIFIED, MRole.SQL_RO);

            SqlParameter[] param = new SqlParameter[]
            {
                new SqlParameter("@M_InOut_ID", M_InOut_ID)
            };

            DataSet ds = DB.ExecuteDataset(sql, param, null);
            if (ds == null || ds.Tables.Count == 0 || ds.Tables[0].Rows.Count == 0)
                return result;

            DataRow r = ds.Tables[0].Rows[0];

            // ----- Header / identity -----
            result.M_InOut_ID       = Util.GetValueOfInt(r["M_InOut_ID"]);
            result.DocumentNo       = Util.GetValueOfString(r["DocumentNo"]);
            result.StatusCode       = Util.GetValueOfString(r["DocStatus"]);
            result.Processed        = Util.GetValueOfString(r["Processed"]) == "Y";
            result.Posted           = Util.GetValueOfString(r["Posted"]) == "Y";
            result.MovementDate     = Util.GetValueOfDateTime(r["MovementDate"]);
            result.PostingDate      = Util.GetValueOfDateTime(r["DateAcct"]);
            result.PriorityCode     = Util.GetValueOfString(r["PriorityRule"]);
            result.C_Order_ID       = Util.GetValueOfInt(r["C_Order_ID"]);
            result.PONo             = Util.GetValueOfString(r["PO_DocumentNo"]);
            result.PODate           = Util.GetValueOfDateTime(r["PODate"]);
            result.ExpectedDate     = Util.GetValueOfDateTime(r["ExpectedDate"]);
            result.RefOrderDocNo    = Util.GetValueOfString(r["RefOrderDocNo"]);

            // The receipt carries no invoice FK; invoices point at the order, so
            // the link is read back through the parent PO. Empty when the receipt
            // has no PO or the PO is not yet invoiced.
            result.ReferenceInvoice = GetLatestInvoiceDocNo(result.C_Order_ID);

            result.VendorName         = Util.GetValueOfString(r["VendorName"]);
            result.VendorTaxID        = Util.GetValueOfString(r["VendorTaxID"]);
            result.VendorLocationName = Util.GetValueOfString(r["VendorLocationName"]);
            result.ContactName        = Util.GetValueOfString(r["ContactName"]);
            result.ContactPhone       = Util.GetValueOfString(r["ContactPhone"]);
            result.ContactEmail       = Util.GetValueOfString(r["ContactEmail"]);
            result.WarehouseName      = Util.GetValueOfString(r["WarehouseName"]);
            result.ReceivedBy         = Util.GetValueOfString(r["ReceivedBy"]);

            result.VendorAddress = BuildAddress(
                Util.GetValueOfString(r["Address1"]),
                Util.GetValueOfString(r["Address2"]),
                Util.GetValueOfString(r["City"]),
                Util.GetValueOfString(r["RegionName"]),
                Util.GetValueOfString(r["Postal"]),
                Util.GetValueOfString(r["CountryName"]));

            // ----- Currency -----
            result.CurSymbol    = Util.GetValueOfString(r["CurSymbol"]);
            result.ISO_Code     = Util.GetValueOfString(r["ISO_Code"]);
            result.StdPrecision = Util.GetValueOfInt(r["StdPrecision"]);

            // ----- KPI aggregates -----
            result.LineCount     = Util.GetValueOfInt(r["LineCount"]);
            result.ReceivedQty   = Util.GetValueOfDecimal(r["ReceivedQty"]);
            result.QcLineCount   = Util.GetValueOfInt(r["QcLineCount"]);
            result.ReceivedValue = Util.GetValueOfDecimal(r["ReceivedValue"]);

            // ----- Material lines -----
            result.Lines = LoadLines(M_InOut_ID, result.StdPrecision, rateExpr, hasQualityPlan);

            return result;
        }

        /// <summary>
        /// Builds the unit-rate SQL expression, preferring the linked PO line
        /// price and falling back through whichever optional cost columns exist
        /// on M_InOutLine, ending at 0.
        /// </summary>
        private string BuildRateExpr(bool hasCurrentCost, bool hasUnitPrice)
        {
            StringBuilder sb = new StringBuilder("COALESCE(ol.PriceActual");
            if (hasCurrentCost) sb.Append(", l.CurrentCostPrice");
            if (hasUnitPrice)   sb.Append(", l.VA024_UnitPrice");
            sb.Append(", 0)");
            return sb.ToString();
        }

        /// <summary>
        /// Returns the document no of the most recent AP invoice raised against
        /// the receipt's purchase order, or an empty string when there is none.
        /// M_InOut has no invoice FK — C_Invoice carries C_Order_ID — so the
        /// invoice is reached through the parent PO. That link is order-scoped,
        /// not receipt-scoped: on a part-received PO the invoice returned may
        /// cover other receipts of the same order. Reversed and voided invoices
        /// are excluded. A DB issue degrades to "" so the overview still renders.
        /// </summary>
        /// <param name="C_Order_ID">Parent purchase order id; 0 when unlinked.</param>
        private string GetLatestInvoiceDocNo(int C_Order_ID)
        {
            if (C_Order_ID <= 0) return "";

            try
            {
                string sql = @"SELECT inv.DocumentNo
                                 FROM C_Invoice inv
                                WHERE inv.C_Order_ID = @C_Order_ID
                                  AND inv.IsActive   = 'Y'
                                  AND inv.IsSOTrx    = 'N'
                                  AND inv.DocStatus NOT IN ('RE', 'VO')
                                ORDER BY inv.DateInvoiced DESC, inv.C_Invoice_ID DESC";
                SqlParameter[] param = new SqlParameter[]
                {
                    new SqlParameter("@C_Order_ID", C_Order_ID)
                };

                DataSet ds = DB.ExecuteDataset(sql, param, null);
                if (ds == null || ds.Tables.Count == 0 || ds.Tables[0].Rows.Count == 0)
                    return "";

                return Util.GetValueOfString(ds.Tables[0].Rows[0]["DocumentNo"]);
            }
            catch (Exception ex)
            {
                _log.Severe("GetLatestInvoiceDocNo (C_Order_ID=" + C_Order_ID + "): " + ex.Message);
                return "";
            }
        }

        /// <summary>
        /// Loads M_InOutLine rows for the goods receipt with product, locator and
        /// UOM metadata, the linked PO line ordered qty and a derived unit rate /
        /// line value. Child of an already authorized receipt, so no separate
        /// MRole filter is applied here.
        /// </summary>
        /// <param name="M_InOut_ID">Owning goods receipt id.</param>
        /// <param name="defaultPrecision">Currency precision fallback.</param>
        /// <param name="rateExpr">Unit-rate SQL expression (schema-aware).</param>
        /// <param name="hasQualityPlan">Whether the quality-plan column exists.</param>
        /// <returns>Ordered list of line rows (may be empty).</returns>
        private List<GRNLineData> LoadLines(
            int M_InOut_ID, int defaultPrecision, string rateExpr, bool hasQualityPlan)
        {
            List<GRNLineData> lines = new List<GRNLineData>();

            string qcCase = hasQualityPlan
                ? "CASE WHEN l.VA010_QualityPlan_ID IS NOT NULL THEN 'Y' ELSE 'N' END"
                : "'N'";

            string sql = @"SELECT
                              l.M_InOutLine_ID,
                              l.Line,
                              l.Description     AS LineDescription,
                              l.M_Product_ID,
                              NVL(l.MovementQty, 0) AS ReceivedQty,
                              p.Value           AS ProductCode,
                              p.Name            AS ProductName,
                              loc.Value         AS LocatorCode,
                              COALESCE(loc.LocatorCombination, loc.Bin, loc.Value) AS LocatorName,
                              u.Name            AS UOMName,
                              NVL(u.StdPrecision, 0) AS UOMPrecision,
                              NVL(ol.QtyOrdered, 0)  AS OrderedQty,
                              " + rateExpr + @"                       AS UnitRate,
                              NVL(l.MovementQty, 0) * " + rateExpr + @" AS LineValue,
                              " + qcCase + @"                          AS QualityApplicable
                           FROM M_InOutLine l
                           LEFT OUTER JOIN C_OrderLine ol ON (ol.C_OrderLine_ID = l.C_OrderLine_ID)
                           LEFT OUTER JOIN M_Product   p  ON (p.M_Product_ID    = l.M_Product_ID)
                           LEFT OUTER JOIN C_UOM       u  ON (u.C_UOM_ID         = l.C_UOM_ID)
                           LEFT OUTER JOIN M_Locator   loc ON (loc.M_Locator_ID  = l.M_Locator_ID)
                           WHERE l.M_InOut_ID = @M_InOut_ID
                             AND l.IsActive   = 'Y'
                           ORDER BY l.Line";

            SqlParameter[] param = new SqlParameter[]
            {
                new SqlParameter("@M_InOut_ID", M_InOut_ID)
            };

            DataSet ds = DB.ExecuteDataset(sql, param, null);
            if (ds == null || ds.Tables.Count == 0)
                return lines;

            foreach (DataRow r in ds.Tables[0].Rows)
            {
                GRNLineData ln = new GRNLineData();
                ln.M_InOutLine_ID     = Util.GetValueOfInt(r["M_InOutLine_ID"]);
                ln.Line               = Util.GetValueOfInt(r["Line"]);
                ln.Description        = Util.GetValueOfString(r["LineDescription"]);
                ln.M_Product_ID       = Util.GetValueOfInt(r["M_Product_ID"]);
                ln.ReceivedQty        = Util.GetValueOfDecimal(r["ReceivedQty"]);
                ln.ProductCode        = Util.GetValueOfString(r["ProductCode"]);
                ln.ProductName        = Util.GetValueOfString(r["ProductName"]);
                ln.LocatorCode        = Util.GetValueOfString(r["LocatorCode"]);
                ln.LocatorName        = Util.GetValueOfString(r["LocatorName"]);
                ln.UOMName            = Util.GetValueOfString(r["UOMName"]);
                ln.UOMPrecision       = Util.GetValueOfInt(r["UOMPrecision"]);
                ln.OrderedQty         = Util.GetValueOfDecimal(r["OrderedQty"]);
                ln.UnitRate           = Util.GetValueOfDecimal(r["UnitRate"]);
                ln.LineValue          = Util.GetValueOfDecimal(r["LineValue"]);
                ln.QualityApplicable  = Util.GetValueOfString(r["QualityApplicable"]) == "Y";

                lines.Add(ln);
            }
            return lines;
        }

        /// <summary>
        /// Returns true when the given column exists on the given table, using
        /// the AD_Column dictionary. A DB issue degrades to "absent" (false) so a
        /// lookup failure never breaks the overview query.
        /// </summary>
        private bool ColumnExists(string tableName, string columnName)
        {
            try
            {
                string sql = @"SELECT COUNT(*) FROM AD_Column
                                WHERE UPPER(ColumnName) = UPPER(@ColumnName)
                                  AND AD_Table_ID = (SELECT AD_Table_ID FROM AD_Table
                                                      WHERE UPPER(TableName) = UPPER(@TableName))";
                SqlParameter[] param = new SqlParameter[]
                {
                    new SqlParameter("@ColumnName", columnName),
                    new SqlParameter("@TableName", tableName)
                };
                return Util.GetValueOfInt(DB.ExecuteScalar(sql, param, null)) > 0;
            }
            catch (Exception ex)
            {
                _log.Severe("ColumnExists (" + tableName + "." + columnName + "): " + ex.Message);
                return false;
            }
        }

        /// <summary>
        /// Concatenates the available C_Location parts into a single display
        /// address, skipping empty segments.
        /// </summary>
        private string BuildAddress(string address1, string address2, string city,
                                    string region, string postal, string country)
        {
            List<string> parts = new List<string>();
            if (!string.IsNullOrEmpty(address1)) parts.Add(address1.Trim());
            if (!string.IsNullOrEmpty(address2)) parts.Add(address2.Trim());

            List<string> cityLine = new List<string>();
            if (!string.IsNullOrEmpty(city))    cityLine.Add(city.Trim());
            if (!string.IsNullOrEmpty(region))  cityLine.Add(region.Trim());
            if (!string.IsNullOrEmpty(postal))  cityLine.Add(postal.Trim());
            if (cityLine.Count > 0) parts.Add(string.Join(" ", cityLine));

            if (!string.IsNullOrEmpty(country)) parts.Add(country.Trim());
            return string.Join(", ", parts);
        }

        // ----------------------------------------------------------------- //
        //  Data carriers                                                     //
        // ----------------------------------------------------------------- //

        public class GRNLineData
        {
            public int      M_InOutLine_ID   { get; set; }
            public int      Line             { get; set; }
            public string   Description      { get; set; }
            public int      M_Product_ID     { get; set; }
            public string   ProductCode      { get; set; }   // SKU
            public string   ProductName      { get; set; }
            public string   LocatorCode      { get; set; }
            public string   LocatorName      { get; set; }
            public string   UOMName          { get; set; }
            public int      UOMPrecision     { get; set; }
            public decimal  OrderedQty       { get; set; }
            public decimal  ReceivedQty      { get; set; }
            public decimal  UnitRate         { get; set; }
            public decimal  LineValue        { get; set; }
            public bool     QualityApplicable { get; set; }
        }

        public class GRNOverviewData
        {
            // Header / identity
            public int       M_InOut_ID       { get; set; }
            public string    DocumentNo       { get; set; }
            public string    StatusCode       { get; set; }   // DocStatus code
            public bool      Processed        { get; set; }
            public bool      Posted           { get; set; }
            public DateTime? MovementDate     { get; set; }   // received date
            public DateTime? PostingDate      { get; set; }   // DateAcct
            public string    PriorityCode     { get; set; }   // PriorityRule code
            public string    ReferenceInvoice { get; set; }   // linked AP invoice DocumentNo

            // Linked purchase order
            public int       C_Order_ID       { get; set; }
            public string    PONo             { get; set; }
            public DateTime? PODate           { get; set; }
            public DateTime? ExpectedDate     { get; set; }
            public string    RefOrderDocNo    { get; set; }   // originating sales order

            // Vendor
            public string    VendorName         { get; set; }
            public string    VendorTaxID        { get; set; }   // GSTIN / Tax ID
            public string    VendorLocationName { get; set; }
            public string    VendorAddress      { get; set; }
            public string    ContactName        { get; set; }
            public string    ContactPhone       { get; set; }
            public string    ContactEmail       { get; set; }

            // Receipt
            public string    WarehouseName    { get; set; }
            public string    ReceivedBy       { get; set; }

            // Currency
            public string    CurSymbol        { get; set; }
            public string    ISO_Code         { get; set; }
            public int       StdPrecision     { get; set; }

            // KPI aggregates
            public int       LineCount        { get; set; }
            public decimal   ReceivedQty      { get; set; }
            public int       QcLineCount      { get; set; }
            public decimal   ReceivedValue    { get; set; }

            // Collections
            public List<GRNLineData> Lines    { get; set; }
        }
    }
}
