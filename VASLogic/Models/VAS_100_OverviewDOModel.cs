/// <summary>
/// Module Name : VASLogic
/// Purpose     : Delivery Order (DO) Overview tab panel data (read side).
///               Returns header identity, customer, linked sales order, KPI
///               aggregates (delivery value / lines / delivered qty / linked SO),
///               transport / dispatch details and the delivery lines for a
///               selected shipment (M_InOut with IsSOTrx = 'Y').
/// Chronological development:
///   VAI163   2026-07-06  Created. Optional module columns (CurrentCostPrice,
///                        VA024_UnitPrice and the VAS_* transport columns) are
///                        guarded through AD_Column so the panel works whether or
///                        not those modules are installed.
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
    public class VAS_100_OverviewDOModel
    {
        private static readonly VLogger _log =
            VLogger.GetVLogger(typeof(VAS_100_OverviewDOModel).FullName);

        /// <summary>
        /// Returns the full overview payload for the selected delivery order.
        /// MRole access filtering is applied ONLY on the main physical table
        /// (M_InOut alias "io"); the child line query inherits the parent's
        /// authorization and is not separately role-filtered.
        /// </summary>
        /// <param name="ctx">User context (client/org/role).</param>
        /// <param name="M_InOut_ID">Selected delivery order id.</param>
        /// <returns>Populated <see cref="DOOverviewData"/>; an empty instance
        /// when the id is invalid or no accessible row is found.</returns>
        public DOOverviewData GetDOOverview(Ctx ctx, int M_InOut_ID)
        {
            DOOverviewData result = new DOOverviewData();
            if (M_InOut_ID <= 0) return result;

            // Optional module columns — resolved once so the SQL below only
            // references columns that actually exist in this schema.
            bool hasCurrentCost = ColumnExists("M_InOutLine", "CurrentCostPrice");
            bool hasUnitPrice   = ColumnExists("M_InOutLine", "VA024_UnitPrice");
            bool hasTransportDoc = ColumnExists("M_InOut", "VAS_TransportDoc");
            bool hasVehicleNo    = ColumnExists("M_InOut", "VAS_VehicleRegistrationNo");
            bool hasGrossWeight  = ColumnExists("M_InOut", "VAS_GrossWeight");
            bool hasTareWeight   = ColumnExists("M_InOut", "VAS_TareWeight");

            // COALESCE(ol.PriceActual, [l.CurrentCostPrice], [l.VA024_UnitPrice], 0)
            string rateExpr = BuildRateExpr(hasCurrentCost, hasUnitPrice);

            // Optional transport columns, selected as NULL when absent so the
            // reader can address them by a stable alias regardless.
            string transportDocSel = hasTransportDoc ? "io.VAS_TransportDoc" : "NULL";
            string vehicleNoSel    = hasVehicleNo    ? "io.VAS_VehicleRegistrationNo" : "NULL";
            string grossWeightSel  = hasGrossWeight  ? "io.VAS_GrossWeight" : "NULL";
            string tareWeightSel   = hasTareWeight   ? "io.VAS_TareWeight" : "NULL";

            string sql = @"SELECT
                              io.M_InOut_ID,
                              io.DocumentNo,
                              io.DocStatus,
                              io.Processed,
                              io.Posted,
                              io.MovementDate,
                              io.PriorityRule,
                              io.POReference,
                              io.TrackingNo,
                              io.NoPackages,
                              " + transportDocSel + @"  AS TransportDoc,
                              " + vehicleNoSel + @"     AS VehicleNo,
                              " + grossWeightSel + @"   AS GrossWeight,
                              " + tareWeightSel + @"    AS TareWeight,
                              io.C_Order_ID,
                              so.DocumentNo    AS SO_DocumentNo,
                              so.DateOrdered   AS SO_DateOrdered,
                              so.DatePromised  AS SO_DatePromised,
                              so.GrandTotal    AS SO_GrandTotal,
                              bp.Name          AS CustomerName,
                              bp.TaxID         AS CustomerTaxID,
                              bpl.Name         AS CustomerLocationName,
                              loc.Address1     AS Address1,
                              loc.Address2     AS Address2,
                              loc.City         AS City,
                              loc.Postal       AS Postal,
                              ctry.Name        AS CountryName,
                              reg.Name         AS RegionName,
                              wh.Name          AS WarehouseName,
                              owner.Name       AS OwnerName,
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
                                  AND l.IsActive   = 'Y')                       AS DeliveredQty,
                              (SELECT NVL(SUM(NVL(l.MovementQty, 0) * " + rateExpr + @"), 0)
                                 FROM M_InOutLine l
                                 LEFT OUTER JOIN C_OrderLine ol ON (ol.C_OrderLine_ID = l.C_OrderLine_ID)
                                WHERE l.M_InOut_ID = io.M_InOut_ID
                                  AND l.IsActive   = 'Y')                       AS LineDeliveryValue
                            FROM M_InOut io
                            INNER JOIN C_BPartner bp        ON (io.C_BPartner_ID          = bp.C_BPartner_ID)
                            LEFT OUTER JOIN C_Order so       ON (io.C_Order_ID            = so.C_Order_ID)
                            LEFT OUTER JOIN C_BPartner_Location bpl ON (io.C_BPartner_Location_ID = bpl.C_BPartner_Location_ID)
                            LEFT OUTER JOIN C_Location loc   ON (bpl.C_Location_ID         = loc.C_Location_ID)
                            LEFT OUTER JOIN C_Country ctry   ON (loc.C_Country_ID          = ctry.C_Country_ID)
                            LEFT OUTER JOIN C_Region reg     ON (loc.C_Region_ID           = reg.C_Region_ID)
                            LEFT OUTER JOIN M_Warehouse wh   ON (io.M_Warehouse_ID         = wh.M_Warehouse_ID)
                            LEFT OUTER JOIN AD_User owner    ON (io.SalesRep_ID            = owner.AD_User_ID)
                            LEFT OUTER JOIN C_Currency cur   ON (so.C_Currency_ID          = cur.C_Currency_ID)
                            WHERE io.M_InOut_ID = @M_InOut_ID
                              AND io.IsActive   = 'Y'
                              AND io.IsSOTrx    = 'Y'";

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
            result.M_InOut_ID     = Util.GetValueOfInt(r["M_InOut_ID"]);
            result.DocumentNo     = Util.GetValueOfString(r["DocumentNo"]);
            result.StatusCode     = Util.GetValueOfString(r["DocStatus"]);
            result.Processed      = Util.GetValueOfString(r["Processed"]) == "Y";
            result.Posted         = Util.GetValueOfString(r["Posted"]) == "Y";
            result.MovementDate   = Util.GetValueOfDateTime(r["MovementDate"]);
            result.PriorityCode   = Util.GetValueOfString(r["PriorityRule"]);
            result.OrderReference = Util.GetValueOfString(r["POReference"]);

            // ----- Transport / dispatch -----
            result.TrackingNo   = Util.GetValueOfString(r["TrackingNo"]);
            result.PackageCount = Util.GetValueOfInt(r["NoPackages"]);
            result.TransportDoc = Util.GetValueOfString(r["TransportDoc"]);
            result.VehicleNo    = Util.GetValueOfString(r["VehicleNo"]);
            result.GrossWeight  = Util.GetValueOfDecimal(r["GrossWeight"]);
            result.TareWeight   = Util.GetValueOfDecimal(r["TareWeight"]);

            // ----- Linked sales order -----
            result.C_Order_ID    = Util.GetValueOfInt(r["C_Order_ID"]);
            result.SONo          = Util.GetValueOfString(r["SO_DocumentNo"]);
            result.SODateOrdered = Util.GetValueOfDateTime(r["SO_DateOrdered"]);
            result.SODatePromised = Util.GetValueOfDateTime(r["SO_DatePromised"]);

            // ----- Customer -----
            result.CustomerName         = Util.GetValueOfString(r["CustomerName"]);
            result.CustomerTaxID        = Util.GetValueOfString(r["CustomerTaxID"]);
            result.CustomerLocationName = Util.GetValueOfString(r["CustomerLocationName"]);
            result.WarehouseName        = Util.GetValueOfString(r["WarehouseName"]);
            result.OwnerName            = Util.GetValueOfString(r["OwnerName"]);

            result.CustomerAddress = BuildAddress(
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
            result.LineCount   = Util.GetValueOfInt(r["LineCount"]);
            result.DeliveredQty = Util.GetValueOfDecimal(r["DeliveredQty"]);

            // Delivery value = linked SO grand total where available, else the
            // sum of delivered line values.
            decimal soValue  = Util.GetValueOfDecimal(r["SO_GrandTotal"]);
            decimal lineValue = Util.GetValueOfDecimal(r["LineDeliveryValue"]);
            result.DeliveryValue = soValue > 0 ? soValue : lineValue;

            // ----- Delivery lines -----
            result.Lines = LoadLines(M_InOut_ID, result.StdPrecision, rateExpr);

            return result;
        }

        /// <summary>
        /// Builds the unit-rate SQL expression, preferring the linked SO line
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
        /// Loads M_InOutLine rows for the delivery order with product, locator and
        /// UOM metadata, the linked SO line ordered / delivered qty and a derived
        /// unit rate / line value. Child of an already authorized shipment, so no
        /// separate MRole filter is applied here.
        /// </summary>
        /// <param name="M_InOut_ID">Owning delivery order id.</param>
        /// <param name="defaultPrecision">Currency precision fallback.</param>
        /// <param name="rateExpr">Unit-rate SQL expression (schema-aware).</param>
        /// <returns>Ordered list of line rows (may be empty).</returns>
        private List<DOLineData> LoadLines(int M_InOut_ID, int defaultPrecision, string rateExpr)
        {
            List<DOLineData> lines = new List<DOLineData>();

            string sql = @"SELECT
                              l.M_InOutLine_ID,
                              l.Line,
                              l.Description     AS LineDescription,
                              l.M_Product_ID,
                              NVL(l.MovementQty, 0) AS DeliveredQty,
                              p.Value           AS ProductCode,
                              p.Name            AS ProductName,
                              loc.Value         AS LocatorCode,
                              COALESCE(loc.LocatorCombination, loc.Bin, loc.Value) AS LocatorName,
                              u.Name            AS UOMName,
                              NVL(u.StdPrecision, 0) AS UOMPrecision,
                              NVL(ol.QtyOrdered, 0)  AS OrderedQty,
                              " + rateExpr + @"                       AS UnitRate,
                              NVL(l.MovementQty, 0) * " + rateExpr + @" AS LineValue
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
                DOLineData ln = new DOLineData();
                ln.M_InOutLine_ID = Util.GetValueOfInt(r["M_InOutLine_ID"]);
                ln.Line           = Util.GetValueOfInt(r["Line"]);
                ln.Description    = Util.GetValueOfString(r["LineDescription"]);
                ln.M_Product_ID   = Util.GetValueOfInt(r["M_Product_ID"]);
                ln.DeliveredQty   = Util.GetValueOfDecimal(r["DeliveredQty"]);
                ln.ProductCode    = Util.GetValueOfString(r["ProductCode"]);
                ln.ProductName    = Util.GetValueOfString(r["ProductName"]);
                ln.LocatorCode    = Util.GetValueOfString(r["LocatorCode"]);
                ln.LocatorName    = Util.GetValueOfString(r["LocatorName"]);
                ln.UOMName        = Util.GetValueOfString(r["UOMName"]);
                ln.UOMPrecision   = Util.GetValueOfInt(r["UOMPrecision"]);
                ln.OrderedQty     = Util.GetValueOfDecimal(r["OrderedQty"]);
                ln.UnitRate       = Util.GetValueOfDecimal(r["UnitRate"]);
                ln.LineValue      = Util.GetValueOfDecimal(r["LineValue"]);

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

        public class DOLineData
        {
            public int      M_InOutLine_ID { get; set; }
            public int      Line           { get; set; }
            public string   Description    { get; set; }
            public int      M_Product_ID   { get; set; }
            public string   ProductCode    { get; set; }   // SKU
            public string   ProductName    { get; set; }
            public string   LocatorCode    { get; set; }
            public string   LocatorName    { get; set; }
            public string   UOMName        { get; set; }
            public int      UOMPrecision   { get; set; }
            public decimal  OrderedQty     { get; set; }
            public decimal  DeliveredQty   { get; set; }
            public decimal  UnitRate       { get; set; }
            public decimal  LineValue      { get; set; }
        }

        public class DOOverviewData
        {
            // Header / identity
            public int       M_InOut_ID     { get; set; }
            public string    DocumentNo     { get; set; }
            public string    StatusCode     { get; set; }   // DocStatus code
            public bool      Processed      { get; set; }
            public bool      Posted         { get; set; }
            public DateTime? MovementDate   { get; set; }
            public string    PriorityCode   { get; set; }   // PriorityRule code
            public string    OrderReference { get; set; }   // POReference

            // Transport / dispatch
            public string    TrackingNo     { get; set; }
            public int       PackageCount   { get; set; }
            public string    TransportDoc   { get; set; }
            public string    VehicleNo      { get; set; }
            public decimal   GrossWeight    { get; set; }
            public decimal   TareWeight     { get; set; }

            // Linked sales order
            public int       C_Order_ID     { get; set; }
            public string    SONo           { get; set; }
            public DateTime? SODateOrdered  { get; set; }
            public DateTime? SODatePromised { get; set; }

            // Customer
            public string    CustomerName         { get; set; }
            public string    CustomerTaxID        { get; set; }
            public string    CustomerLocationName { get; set; }
            public string    CustomerAddress      { get; set; }

            // Receipt / dispatch parties
            public string    WarehouseName  { get; set; }
            public string    OwnerName      { get; set; }

            // Currency
            public string    CurSymbol      { get; set; }
            public string    ISO_Code       { get; set; }
            public int       StdPrecision   { get; set; }

            // KPI aggregates
            public int       LineCount      { get; set; }
            public decimal   DeliveredQty   { get; set; }
            public decimal   DeliveryValue  { get; set; }

            // Collections
            public List<DOLineData> Lines   { get; set; }
        }
    }
}
