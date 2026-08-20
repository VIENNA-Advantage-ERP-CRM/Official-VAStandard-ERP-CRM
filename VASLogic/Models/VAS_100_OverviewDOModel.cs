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
///   VAI163   2026-08-13  OwnerName now reads M_InOut.CreatedBy (the user who
///                        created the delivery order) instead of SalesRep_ID —
///                        the sales rep is not the owner of the document.
///                        Customer block extended with the contact's first name
///                        and e-mail (LoadCustomerContact), and the shipping
///                        method (DeliveryViaRule + M_Shipper) is now returned.
///   VAI163   2026-08-13  Reference / "Generated From" origins added, matching
///                        the VAS_092 Purchase Order panel: the sales order the
///                        delivery was raised from, its project, the delivery
///                        order it reverses (Ref_InOut_ID) and the RMA
///                        (AD_Column-guarded — M_RMA is not in every schema).
///                        M_InOut.Created is returned so the timeline can date
///                        its Drafted stage.
///   VAI163   2026-08-13  M_InOut.Description is returned for the panel's Notes
///                        section, and each line carries the attribute set
///                        instance it was delivered against (AD_Column-guarded;
///                        instance id 0 is the dictionary's no-attributes row and
///                        is deliberately not joined).
///   VAI163   2026-08-14  - DeliveryValue is Σ (delivered qty x rate) — the sum of
///                          the line values the panel prints — ALWAYS. It used to
///                          prefer the linked sales order's GrandTotal whenever
///                          there was one, which is the value of the ORDER, not of
///                          this delivery: a part shipment of an 8,075 order
///                          reported 8,075 against two lines totalling 2,200, so
///                          the footer disagreed with the column above it.
///                        - Lines carry IsDropShip: the shipment line's own flag,
///                          falling back to the ORDER line's (where drop shipment
///                          is configured). Both dictionary-guarded; a schema with
///                          neither reports "No" for every line.
///                        - Added Documents (LoadDocuments): the customer invoices
///                          raised from this delivery's lines, the shipment
///                          confirmations recorded for it and the customer
///                          receipts allocated to those invoices, each carrying
///                          the table + record id the panel opens. The GRN
///                          overview's section with the sales / purchase polarity
///                          flipped.
///                        - Added Activity (LoadActivity), built to the VAS_092
///                          Purchase Order shape: created, the document lifecycle
///                          one row per completed workflow node, one "updated" row
///                          per FIELD edited on the header AND its lines, the chat
///                          notes and the e-mails sent against it.
///   VAI163   2026-08-14  Quality confirmation, for the panel's Confirmation Check
///                        / Accepted / Difference cards and its per-line quality
///                        parameters:
///                        - IsShipConfirmDocType (C_DocType.IsShipConfirm) and the
///                          confirmation quantities AcceptedQty (ConfirmedQty),
///                          DifferenceQty and ScrappedQty, summed over the
///                          delivery's M_InOutLineConfirm rows. Each is guarded on
///                          its own column (ConfirmQtyExpr), so a schema missing
///                          one still reports the others.
///                        - QualityParams + QaParamLineCount + the per-line
///                          QualityApplicable flag, read through the GRN
///                          overview's LoadQualityParams rather than a second copy
///                          of it. Every table that loader touches is shared by
///                          shipments and receipts and none of it looks at
///                          IsSOTrx, so the VA010 rules are identical here; the
///                          alternative was several hundred lines of intricate
///                          logic — the planned-vs-recorded fallback, the checking
///                          bands, the display-column probing — duplicated and
///                          kept in step by hand. The rows are mapped onto this
///                          panel's own DTO so the payload carries delivery-named
///                          types and the two panels can diverge in what they SHOW
///                          without touching how the rows are READ.
///   VAI163   2026-08-14  For the panel's new Shipment Details section and its
///                        Sales-Order-shaped customer block:
///                        - VehicleName (M_InOut.VAS_VehicleName), beside the
///                          registration number the panel already carried. Both
///                          are AD_Column-guarded module columns.
///                        - ShipToAddress / BillToAddress (LoadAddresses, mirroring
///                          VAS_106). The delivery's own C_BPartner_Location_ID IS
///                          the ship-to; the bill-to belongs to the ORDER
///                          (C_Order.Bill_Location_ID), so a delivery raised
///                          without a sales order simply has none.
///                        - ContactName / ContactPhone / ContactEmail — the
///                          contact LoadCustomerContact already resolved, now
///                          surfaced whole (it gained Phone) instead of only as a
///                          first name and an e-mail.
///   VAI163   2026-08-17  Field-level activity carries the OLD and NEW values
///                        (AD_ChangeLog.OldValue / NewValue). Both are normalised
///                        through ChangeValue: the literal "null" the platform
///                        writes for a cleared field reads as empty, not as the
///                        word. A row whose two values are equal is dropped — a
///                        save that rewrote a field with the value it already had
///                        is not an edit, and the platform logs plenty of those.
///                        The trail said WHICH field moved but never what it moved
///                        from or to. Follows VAS_101 / VAS_104.
/// </summary>

using System;
using System.Collections.Generic;
using System.Data;
using System.Data.SqlClient;
using System.Net;
using System.Text;
using System.Text.RegularExpressions;
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
            bool hasVehicleName  = ColumnExists("M_InOut", "VAS_VehicleName");
            bool hasGrossWeight  = ColumnExists("M_InOut", "VAS_GrossWeight");
            bool hasTareWeight   = ColumnExists("M_InOut", "VAS_TareWeight");

            // The RMA origin only exists where the returns module is installed.
            // Both the reference column and the target table are checked, so a
            // schema without either simply reports no RMA origin.
            bool hasRma = ColumnExists("M_InOut", "M_RMA_ID")
                       && ColumnExists("M_RMA", "DocumentNo");

            // C_DocType.IsShipConfirm is what makes the platform raise a shipment
            // confirmation, so it decides whether this delivery has a confirmation
            // to report at all — and it is what the Confirmation Check card reads.
            // Guarded like everything else so an older dictionary degrades to "not
            // applicable" rather than failing the whole query.
            bool hasShipConfirm = ColumnExists("C_DocType", "IsShipConfirm");
            string shipConfirmExpr = hasShipConfirm ? "COALESCE(dt.IsShipConfirm, 'N')" : "'N'";

            // The confirmation quantities: what was accepted, the gap against the
            // target, and what was scrapped. Each is guarded on its own column so a
            // schema missing one still reports the others.
            string confirmedQtySel  = ConfirmQtyExpr("ConfirmedQty");
            string differenceQtySel = ConfirmQtyExpr("DifferenceQty");
            string scrappedQtySel   = ConfirmQtyExpr("ScrappedQty");

            // COALESCE(ol.PriceActual, [l.CurrentCostPrice], [l.VA024_UnitPrice], 0)
            string rateExpr = BuildRateExpr(hasCurrentCost, hasUnitPrice);

            // Optional transport columns, selected as NULL when absent so the
            // reader can address them by a stable alias regardless.
            string transportDocSel = hasTransportDoc ? "io.VAS_TransportDoc" : "NULL";
            string vehicleNoSel    = hasVehicleNo    ? "io.VAS_VehicleRegistrationNo" : "NULL";
            string vehicleNameSel  = hasVehicleName  ? "io.VAS_VehicleName" : "NULL";
            string grossWeightSel  = hasGrossWeight  ? "io.VAS_GrossWeight" : "NULL";
            string tareWeightSel   = hasTareWeight   ? "io.VAS_TareWeight" : "NULL";
            string rmaIdSel        = hasRma ? "io.M_RMA_ID"    : "NULL";
            string rmaNoSel        = hasRma ? "rma.DocumentNo" : "NULL";
            string rmaJoin         = hasRma
                ? "LEFT OUTER JOIN M_RMA rma ON (io.M_RMA_ID = rma.M_RMA_ID)"
                : "";

            string sql = @"SELECT
                              io.M_InOut_ID,
                              io.DocumentNo,
                              io.DocStatus,
                              io.Processed,
                              io.Posted,
                              io.Created,
                              io.MovementDate,
                              io.PriorityRule,
                              io.POReference,
                              io.Description   AS HeaderDescription,
                              io.TrackingNo,
                              io.NoPackages,
                              io.DeliveryViaRule,
                              " + transportDocSel + @"  AS TransportDoc,
                              " + vehicleNoSel + @"     AS VehicleNo,
                              " + vehicleNameSel + @"   AS VehicleName,
                              io.C_BPartner_Location_ID AS ShipLocationId,
                              so.Bill_Location_ID       AS BillLocationId,
                              " + grossWeightSel + @"   AS GrossWeight,
                              " + tareWeightSel + @"    AS TareWeight,
                              io.C_Order_ID,
                              io.C_Project_ID,
                              io.Ref_InOut_ID,
                              " + rmaIdSel + @"       AS RmaId,
                              " + rmaNoSel + @"       AS RmaDocNo,
                              refio.DocumentNo AS RefInOutDocNo,
                              pj.Value         AS ProjectValue,
                              pj.Name          AS ProjectName,
                              so.DocumentNo    AS SO_DocumentNo,
                              so.DateOrdered   AS SO_DateOrdered,
                              so.DatePromised  AS SO_DatePromised,
                              so.GrandTotal    AS SO_GrandTotal,
                              bp.Name          AS CustomerName,
                              bp.Name2         AS CustomerName2,
                              bp.EMail         AS CustomerBPEmail,
                              bp.TaxID         AS CustomerTaxID,
                              bpl.Name         AS CustomerLocationName,
                              loc.Address1     AS Address1,
                              loc.Address2     AS Address2,
                              loc.City         AS City,
                              loc.Postal       AS Postal,
                              ctry.Name        AS CountryName,
                              reg.Name         AS RegionName,
                              wh.Name          AS WarehouseName,
                              shp.Name         AS ShipperName,
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
                                  AND l.IsActive   = 'Y')                       AS LineDeliveryValue,
                              " + shipConfirmExpr  + @"                         AS IsShipConfirmDocType,
                              " + confirmedQtySel  + @"                         AS AcceptedQty,
                              " + differenceQtySel + @"                         AS DifferenceQty,
                              " + scrappedQtySel   + @"                         AS ScrappedQty
                            FROM M_InOut io
                            LEFT OUTER JOIN C_DocType dt     ON (io.C_DocType_ID         = dt.C_DocType_ID)
                            INNER JOIN C_BPartner bp        ON (io.C_BPartner_ID          = bp.C_BPartner_ID)
                            LEFT OUTER JOIN C_Order so       ON (io.C_Order_ID            = so.C_Order_ID)
                            LEFT OUTER JOIN C_BPartner_Location bpl ON (io.C_BPartner_Location_ID = bpl.C_BPartner_Location_ID)
                            LEFT OUTER JOIN C_Location loc   ON (bpl.C_Location_ID         = loc.C_Location_ID)
                            LEFT OUTER JOIN C_Country ctry   ON (loc.C_Country_ID          = ctry.C_Country_ID)
                            LEFT OUTER JOIN C_Region reg     ON (loc.C_Region_ID           = reg.C_Region_ID)
                            LEFT OUTER JOIN M_Warehouse wh   ON (io.M_Warehouse_ID         = wh.M_Warehouse_ID)
                            LEFT OUTER JOIN M_Shipper shp    ON (io.M_Shipper_ID           = shp.M_Shipper_ID)
                            -- Owner = the user who CREATED the delivery order.
                            -- SalesRep_ID is the rep the document is credited to,
                            -- which is a different person on most shipments.
                            LEFT OUTER JOIN AD_User owner    ON (io.CreatedBy              = owner.AD_User_ID)
                            LEFT OUTER JOIN C_Currency cur   ON (so.C_Currency_ID          = cur.C_Currency_ID)
                            LEFT OUTER JOIN C_Project pj     ON (io.C_Project_ID           = pj.C_Project_ID)
                            LEFT OUTER JOIN M_InOut refio    ON (io.Ref_InOut_ID           = refio.M_InOut_ID)
                            " + rmaJoin + @"
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
            result.Created        = Util.GetValueOfDateTime(r["Created"]);
            result.MovementDate   = Util.GetValueOfDateTime(r["MovementDate"]);
            result.PriorityCode   = Util.GetValueOfString(r["PriorityRule"]);
            result.OrderReference = Util.GetValueOfString(r["POReference"]);
            result.Description    = Util.GetValueOfString(r["HeaderDescription"]);

            // ----- Transport / dispatch -----
            result.TrackingNo   = Util.GetValueOfString(r["TrackingNo"]);
            result.PackageCount = Util.GetValueOfInt(r["NoPackages"]);
            result.TransportDoc = Util.GetValueOfString(r["TransportDoc"]);
            result.VehicleNo    = Util.GetValueOfString(r["VehicleNo"]);
            result.VehicleName  = Util.GetValueOfString(r["VehicleName"]);
            result.GrossWeight  = Util.GetValueOfDecimal(r["GrossWeight"]);
            result.TareWeight   = Util.GetValueOfDecimal(r["TareWeight"]);

            // ----- Shipping method -----
            result.DeliveryViaRule = Util.GetValueOfString(r["DeliveryViaRule"]);
            result.ShipperName     = Util.GetValueOfString(r["ShipperName"]);

            // ----- Linked sales order -----
            result.C_Order_ID    = Util.GetValueOfInt(r["C_Order_ID"]);
            result.SONo          = Util.GetValueOfString(r["SO_DocumentNo"]);
            result.SODateOrdered = Util.GetValueOfDateTime(r["SO_DateOrdered"]);
            result.SODatePromised = Util.GetValueOfDateTime(r["SO_DatePromised"]);

            // ----- Reference / origins (the documents this DO came from) -----
            result.C_Project_ID   = Util.GetValueOfInt(r["C_Project_ID"]);
            result.ProjectNo      = Util.GetValueOfString(r["ProjectValue"]);
            result.ProjectName    = Util.GetValueOfString(r["ProjectName"]);
            result.Ref_InOut_ID   = Util.GetValueOfInt(r["Ref_InOut_ID"]);
            result.RefInOutDocNo  = Util.GetValueOfString(r["RefInOutDocNo"]);
            result.M_RMA_ID       = Util.GetValueOfInt(r["RmaId"]);
            result.RmaDocNo       = Util.GetValueOfString(r["RmaDocNo"]);

            // ----- Customer -----
            result.CustomerName         = Util.GetValueOfString(r["CustomerName"]);
            result.CustomerTaxID        = Util.GetValueOfString(r["CustomerTaxID"]);
            result.CustomerLocationName = Util.GetValueOfString(r["CustomerLocationName"]);
            result.WarehouseName        = Util.GetValueOfString(r["WarehouseName"]);
            result.OwnerName            = Util.GetValueOfString(r["OwnerName"]);

            // Contact-level customer details. The shipment's own contact wins;
            // failing that the partner's best contact is used, and the partner
            // record itself is the last fallback for both fields.
            ContactData contact = LoadCustomerContact(M_InOut_ID);
            result.CustomerFirstName = !string.IsNullOrEmpty(contact.Name)
                ? FirstNameOf(contact.Name)
                : FirstNameOf(Util.GetValueOfString(r["CustomerName2"]));
            result.CustomerEmail = !string.IsNullOrEmpty(contact.EMail)
                ? contact.EMail
                : Util.GetValueOfString(r["CustomerBPEmail"]);

            // The contact as the Sales Order overview presents one: full name,
            // phone and e-mail, each shown as its own icon-led bit rather than as
            // a labelled field.
            result.ContactName  = contact.Name;
            result.ContactPhone = contact.Phone;
            result.ContactEmail = result.CustomerEmail;

            result.CustomerAddress = BuildAddress(
                Util.GetValueOfString(r["Address1"]),
                Util.GetValueOfString(r["Address2"]),
                Util.GetValueOfString(r["City"]),
                Util.GetValueOfString(r["RegionName"]),
                Util.GetValueOfString(r["Postal"]),
                Util.GetValueOfString(r["CountryName"]));

            // Ship-to and bill-to as their own addresses, the way the Sales Order
            // overview shows them. The delivery's own C_BPartner_Location_ID IS the
            // ship-to; the bill-to belongs to the ORDER (C_Order.Bill_Location_ID),
            // so a delivery raised without one simply has no bill-to line — the two
            // are frequently different places, which is the whole reason both are
            // shown.
            LoadAddresses(Util.GetValueOfInt(r["ShipLocationId"]),
                          Util.GetValueOfInt(r["BillLocationId"]), result);

            // ----- Currency -----
            result.CurSymbol    = Util.GetValueOfString(r["CurSymbol"]);
            result.ISO_Code     = Util.GetValueOfString(r["ISO_Code"]);
            result.StdPrecision = Util.GetValueOfInt(r["StdPrecision"]);

            // ----- KPI aggregates -----
            result.LineCount   = Util.GetValueOfInt(r["LineCount"]);
            result.DeliveredQty = Util.GetValueOfDecimal(r["DeliveredQty"]);

            // Delivery value is what THIS DELIVERY is worth: Σ (delivered qty x the
            // line's rate), which is exactly the sum of the line values the panel
            // prints above the total.
            //
            // It used to prefer the linked sales order's GrandTotal whenever there
            // was one, falling back to the lines only for a delivery raised
            // without an order. That is the value of the ORDER, not of the
            // delivery — a part shipment of a 8,075 order reported 8,075 against
            // two lines totalling 2,200, and the footer disagreed with the column
            // directly above it. The order's own total is not lost: it belongs to
            // the sales order, which the Reference strip already opens.
            result.DeliveryValue = Util.GetValueOfDecimal(r["LineDeliveryValue"]);

            // ----- Quality confirmation -----
            result.IsShipConfirmDocType =
                Util.GetValueOfString(r["IsShipConfirmDocType"]) == "Y";
            result.AcceptedQty   = Util.GetValueOfDecimal(r["AcceptedQty"]);
            result.DifferenceQty = Util.GetValueOfDecimal(r["DifferenceQty"]);
            result.ScrappedQty   = Util.GetValueOfDecimal(r["ScrappedQty"]);

            // ----- Delivery lines -----
            result.Lines = LoadLines(M_InOut_ID, result.StdPrecision, rateExpr);

            // ----- VA010 quality inspection rows -----
            //
            // Read through the GRN overview's loader rather than a second copy of
            // it. Every table it touches is shared by shipments and receipts and
            // none of it looks at IsSOTrx, so the rules are identical here; the
            // alternative was several hundred lines of intricate VA010 logic
            // (planned-vs-recorded fallback, checking bands, display-column
            // probing) duplicated and kept in step by hand.
            LoadQualityParams(M_InOut_ID, result);

            // ----- Documents raised against this delivery -----
            result.Documents = LoadDocuments(M_InOut_ID);

            // ----- Activity (audit trail) -----
            result.Activity = LoadActivity(M_InOut_ID);

            return result;
        }

        /// <summary>
        /// A confirmation-quantity subselect: Σ of the named M_InOutLineConfirm
        /// column over this delivery's confirmation lines, or a constant 0 when
        /// the schema does not carry the column.
        /// </summary>
        /// <param name="columnName">ConfirmedQty / DifferenceQty / ScrappedQty.</param>
        private string ConfirmQtyExpr(string columnName)
        {
            if (!ColumnExists("M_InOutLineConfirm", columnName)) return "0";
            return @"(SELECT NVL(SUM(NVL(lc." + columnName + @", 0)), 0)
                        FROM M_InOutLineConfirm lc
                       INNER JOIN M_InOutLine cl ON (cl.M_InOutLine_ID = lc.M_InOutLine_ID
                                                     AND cl.IsActive    = 'Y')
                       WHERE cl.M_InOut_ID = io.M_InOut_ID
                         AND lc.IsActive   = 'Y')";
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

            // Attribute set instance (lot / serial / attributes) — only referenced
            // when the column is actually there. Instance id 0 is the dictionary's
            // no-attributes row, whose description is a bare double dash, so only
            // a REAL instance is joined.
            // Drop shipment, per line. The flag is carried on the shipment line
            // itself, but a line raised from an order inherits it from the ORDER
            // LINE, which is where drop shipment is configured — so the order
            // line's value stands in wherever the shipment line does not carry
            // one. Both columns are dictionary-guarded: whichever the schema has
            // is used, and a schema with neither reports "No" for every line.
            bool hasLineDropShip  = ColumnExists("M_InOutLine", "IsDropShip");
            bool hasOrderDropShip = ColumnExists("C_OrderLine", "IsDropShip");
            string dropShipExpr;
            if (hasLineDropShip && hasOrderDropShip)
                dropShipExpr = "COALESCE(NULLIF(l.IsDropShip, 'N'), ol.IsDropShip, 'N')";
            else if (hasLineDropShip)  dropShipExpr = "COALESCE(l.IsDropShip, 'N')";
            else if (hasOrderDropShip) dropShipExpr = "COALESCE(ol.IsDropShip, 'N')";
            else                       dropShipExpr = "'N'";

            bool hasAsi = ColumnExists("M_InOutLine", "M_AttributeSetInstance_ID");
            string asiExpr = hasAsi ? "asi.Description" : "CAST(NULL AS VARCHAR(255))";
            string asiJoin = hasAsi
                ? @"LEFT OUTER JOIN M_AttributeSetInstance asi
                              ON (asi.M_AttributeSetInstance_ID = l.M_AttributeSetInstance_ID
                                  AND l.M_AttributeSetInstance_ID > 0)"
                : "";

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
                              " + asiExpr + @"  AS AttributeName,
                              " + dropShipExpr + @" AS IsDropShip,
                              NVL(u.StdPrecision, 0) AS UOMPrecision,
                              NVL(ol.QtyOrdered, 0)  AS OrderedQty,
                              " + rateExpr + @"                       AS UnitRate,
                              NVL(l.MovementQty, 0) * " + rateExpr + @" AS LineValue
                           FROM M_InOutLine l
                           LEFT OUTER JOIN C_OrderLine ol ON (ol.C_OrderLine_ID = l.C_OrderLine_ID)
                           LEFT OUTER JOIN M_Product   p  ON (p.M_Product_ID    = l.M_Product_ID)
                           LEFT OUTER JOIN C_UOM       u  ON (u.C_UOM_ID         = l.C_UOM_ID)
                           LEFT OUTER JOIN M_Locator   loc ON (loc.M_Locator_ID  = l.M_Locator_ID)
                           " + asiJoin + @"
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
                ln.AttributeName  = Util.GetValueOfString(r["AttributeName"]);
                ln.IsDropShip     = Util.GetValueOfString(r["IsDropShip"]) == "Y";
                ln.UOMPrecision   = Util.GetValueOfInt(r["UOMPrecision"]);
                ln.OrderedQty     = Util.GetValueOfDecimal(r["OrderedQty"]);
                ln.UnitRate       = Util.GetValueOfDecimal(r["UnitRate"]);
                ln.LineValue      = Util.GetValueOfDecimal(r["LineValue"]);

                lines.Add(ln);
            }
            return lines;
        }

        /// <summary>
        /// Resolves a window's AD_Window_ID from its name (AD_Window.Name) for the
        /// panel's record-open path. A record whose screen is not its table's
        /// default zoom target is opened by naming its window instead, and the
        /// name is only ever turned into an id here, against the dictionary.
        ///
        /// Restricted to windows this tenant can see (AD_Client_ID 0 or its own),
        /// preferring the tenant's own row over the system one. Whether the ROLE
        /// may open it is the platform's call, made when the window is started.
        /// </summary>
        /// <param name="ctx">User context (client).</param>
        /// <param name="windowName">Window name to resolve.</param>
        /// <returns>The window id, or 0 when the name resolves to nothing.</returns>
        public int GetWindowId(Ctx ctx, string windowName)
        {
            if (string.IsNullOrEmpty(windowName)) return 0;
            try
            {
                string sql = @"SELECT w.AD_Window_ID
                                 FROM AD_Window w
                                WHERE w.Name        = @Name
                                  AND w.IsActive    = 'Y'
                                  AND w.AD_Client_ID IN (0, @AD_Client_ID)
                                ORDER BY w.AD_Client_ID DESC";
                SqlParameter[] param = new SqlParameter[]
                {
                    new SqlParameter("@Name", windowName.Trim()),
                    new SqlParameter("@AD_Client_ID", ctx == null ? 0 : ctx.GetAD_Client_ID())
                };
                DataSet ds = DB.ExecuteDataset(sql, param, null);
                if (ds == null || ds.Tables.Count == 0 || ds.Tables[0].Rows.Count == 0)
                    return 0;
                return Util.GetValueOfInt(ds.Tables[0].Rows[0]["AD_Window_ID"]);
            }
            catch (Exception ex)
            {
                _log.Severe("GetWindowId (" + windowName + "): " + ex.Message);
                return 0;
            }
        }

        /// <summary>
        /// Resolves the customer contact whose name / e-mail the Customer block
        /// shows. Preference order, applied as an ORDER BY rather than as
        /// separate round trips: the contact recorded ON the shipment
        /// (M_InOut.AD_User_ID), then any other active contact of the same
        /// business partner, preferring one that actually carries an e-mail.
        /// Child of an already authorized shipment, so no separate MRole filter.
        /// </summary>
        /// <param name="M_InOut_ID">Owning delivery order id.</param>
        /// <returns>The chosen contact; an empty instance when the partner has
        /// no active contact at all.</returns>
        private ContactData LoadCustomerContact(int M_InOut_ID)
        {
            ContactData contact = new ContactData();

            string sql = @"SELECT u.Name AS ContactName,
                                  u.EMail AS ContactEmail,
                                  u.Phone AS ContactPhone
                             FROM M_InOut io
                             INNER JOIN AD_User u
                                ON (u.AD_User_ID    = io.AD_User_ID
                                 OR u.C_BPartner_ID = io.C_BPartner_ID)
                            WHERE io.M_InOut_ID = @M_InOut_ID
                              AND u.IsActive    = 'Y'
                            ORDER BY CASE WHEN u.AD_User_ID = io.AD_User_ID THEN 0 ELSE 1 END,
                                     CASE WHEN u.EMail IS NOT NULL THEN 0 ELSE 1 END,
                                     u.AD_User_ID";

            SqlParameter[] param = new SqlParameter[]
            {
                new SqlParameter("@M_InOut_ID", M_InOut_ID)
            };

            DataSet ds = DB.ExecuteDataset(sql, param, null);
            if (ds == null || ds.Tables.Count == 0 || ds.Tables[0].Rows.Count == 0)
                return contact;

            DataRow r = ds.Tables[0].Rows[0];
            contact.Name  = Util.GetValueOfString(r["ContactName"]);
            contact.EMail = Util.GetValueOfString(r["ContactEmail"]);
            contact.Phone = Util.GetValueOfString(r["ContactPhone"]);
            return contact;
        }

        /// <summary>
        /// Loads the ship-to and bill-to addresses (C_BPartner_Location +
        /// C_Location) and composes each into a single display string. Either id
        /// may be 0 — a delivery raised without a sales order has no bill-to — and
        /// the matching address is then simply not set.
        ///
        /// Mirrors the Sales Order overview's LoadAddresses, which is what the
        /// customer block on this panel is now built to look like.
        /// </summary>
        /// <param name="shipLocationId">M_InOut.C_BPartner_Location_ID.</param>
        /// <param name="billLocationId">C_Order.Bill_Location_ID; 0 when unlinked.</param>
        /// <param name="d">Overview payload being populated.</param>
        private void LoadAddresses(int shipLocationId, int billLocationId, DOOverviewData d)
        {
            if (shipLocationId <= 0 && billLocationId <= 0) return;
            try
            {
                string sql = @"SELECT
                                  bpl.C_BPartner_Location_ID AS LocId,
                                  loc.Address1 AS Address1,
                                  loc.Address2 AS Address2,
                                  loc.City     AS City,
                                  loc.Postal   AS Postal,
                                  reg.Name     AS RegionName,
                                  ctry.Name    AS CountryName
                                FROM C_BPartner_Location bpl
                                LEFT OUTER JOIN C_Location loc  ON (loc.C_Location_ID = bpl.C_Location_ID)
                                LEFT OUTER JOIN C_Country ctry  ON (loc.C_Country_ID  = ctry.C_Country_ID)
                                LEFT OUTER JOIN C_Region reg    ON (loc.C_Region_ID   = reg.C_Region_ID)
                                WHERE bpl.IsActive = 'Y'
                                  AND bpl.C_BPartner_Location_ID IN (@ship, @bill)";
                SqlParameter[] param = new SqlParameter[]
                {
                    new SqlParameter("@ship", shipLocationId),
                    new SqlParameter("@bill", billLocationId)
                };
                DataSet ds = DB.ExecuteDataset(sql, param, null);
                if (ds == null || ds.Tables.Count == 0) return;

                foreach (DataRow r in ds.Tables[0].Rows)
                {
                    int locId = Util.GetValueOfInt(r["LocId"]);
                    string addr = BuildAddress(
                        Util.GetValueOfString(r["Address1"]),
                        Util.GetValueOfString(r["Address2"]),
                        Util.GetValueOfString(r["City"]),
                        Util.GetValueOfString(r["RegionName"]),
                        Util.GetValueOfString(r["Postal"]),
                        Util.GetValueOfString(r["CountryName"]));
                    if (locId == shipLocationId) d.ShipToAddress = addr;
                    if (locId == billLocationId) d.BillToAddress = addr;
                }
            }
            catch (Exception ex)
            {
                _log.Severe("LoadAddresses (M_InOut_ID=" + d.M_InOut_ID + "): " + ex.Message);
            }
        }

        /// <summary>
        /// Leading word of a person name. Contacts are stored as one full-name
        /// string (AD_User has no separate given-name column), so the first
        /// whitespace-delimited token is the first name.
        /// </summary>
        private string FirstNameOf(string fullName)
        {
            if (string.IsNullOrEmpty(fullName)) return "";
            string trimmed = fullName.Trim();
            int space = trimmed.IndexOf(' ');
            return space > 0 ? trimmed.Substring(0, space) : trimmed;
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

        /// <summary>Single-parameter helper for the shipment-scoped queries.</summary>
        private SqlParameter[] InOutParam(int M_InOut_ID)
        {
            return new SqlParameter[] { new SqlParameter("@M_InOut_ID", M_InOut_ID) };
        }

        // ----------------------------------------------------------------- //
        //  Quality inspection (VA010)                                        //
        // ----------------------------------------------------------------- //

        /// <summary>
        /// Fills the delivery's VA010 inspection rows, the per-line "this product
        /// has QA parameters" flag and the count of lines that carry them.
        ///
        /// The rows come from the GRN overview's loader
        /// (<see cref="VAS_099_OverviewGRNModel.LoadQualityParams"/>): the VA010
        /// rules are about a document's LINES, not about which side of the trade
        /// it sits on, and every table involved is shared by shipments and
        /// receipts. Duplicating that loader and the helpers behind it would mean
        /// several hundred lines of intricate logic — the planned-vs-recorded
        /// fallback, the checking bands, the display-column probing — kept in step
        /// by hand across two files.
        ///
        /// The rows are mapped onto this panel's own DTO so the delivery payload
        /// carries delivery-named types, and so the two panels can diverge in what
        /// they SHOW without touching how the rows are READ.
        ///
        /// Wrapped in its own guard: a VA010 problem costs the panel its quality
        /// section, not the whole overview.
        /// </summary>
        /// <param name="M_InOut_ID">Selected delivery order id.</param>
        /// <param name="result">Overview payload being populated.</param>
        private void LoadQualityParams(int M_InOut_ID, DOOverviewData result)
        {
            result.QualityParams = new List<DOQualityParamData>();
            try
            {
                List<VAS_099_OverviewGRNModel.GRNQualityParamData> rows =
                    new VAS_099_OverviewGRNModel().LoadQualityParams(M_InOut_ID);
                if (rows == null) return;

                foreach (VAS_099_OverviewGRNModel.GRNQualityParamData q in rows)
                {
                    result.QualityParams.Add(new DOQualityParamData
                    {
                        LineNo            = q.LineNo,
                        M_Product_ID      = q.M_Product_ID,
                        ProductCode       = q.ProductCode,
                        ProductName       = q.ProductName,
                        ParameterName     = q.ParameterName,
                        QuantityToVerify  = q.QuantityToVerify,
                        AcceptableValueId = q.AcceptableValueId,
                        AcceptableValue   = q.AcceptableValue,
                        ActualValueId     = q.ActualValueId,
                        ActualValue       = q.ActualValue,
                        QAQCDate          = q.QAQCDate,
                        Remark            = q.Remark,
                        StatusCode        = q.StatusCode,
                        IsPlanned         = q.IsPlanned
                    });
                }

                // Which LINES carry a product with QA parameters. Several
                // parameters against one line count once — the card reports lines,
                // and the line table's Quality column marks the line, not the
                // parameter.
                List<int> seen = new List<int>();
                foreach (DOQualityParamData q in result.QualityParams)
                {
                    if (!seen.Contains(q.LineNo)) seen.Add(q.LineNo);
                }
                result.QaParamLineCount = seen.Count;

                if (result.Lines != null)
                {
                    foreach (DOLineData ln in result.Lines)
                        ln.QualityApplicable = seen.Contains(ln.Line);
                }
            }
            catch (Exception ex)
            {
                _log.Severe("LoadQualityParams (M_InOut_ID=" + M_InOut_ID + "): " + ex.Message);
                result.QualityParams = new List<DOQualityParamData>();
                result.QaParamLineCount = 0;
            }
        }

        // ----------------------------------------------------------------- //
        //  Documents raised against the delivery                             //
        // ----------------------------------------------------------------- //

        /// <summary>
        /// The documents that exist against this delivery order — the customer
        /// invoices raised from its lines, the shipment confirmations recorded for
        /// it and the customer receipts allocated to those invoices — newest
        /// first. Each carries the table name + record id, so the panel can open
        /// it, and in-progress documents are included (an invoice awaiting
        /// completion is exactly what a reader is looking for). Reversed and
        /// voided documents are excluded.
        ///
        /// The GRN overview's Documents section with the sales / purchase polarity
        /// flipped: same shape, same row data, same open path.
        ///
        /// Each source is guarded on its own so a DB issue degrades to a partial
        /// list rather than breaking the overview.
        /// </summary>
        /// <param name="M_InOut_ID">Selected delivery order id.</param>
        /// <returns>Document rows ordered newest-first (may be empty).</returns>
        private List<DODocumentData> LoadDocuments(int M_InOut_ID)
        {
            List<DODocumentData> docs = new List<DODocumentData>();
            LoadInvoiceDocuments(M_InOut_ID, docs);
            LoadConfirmationDocuments(M_InOut_ID, docs);
            LoadReceiptDocuments(M_InOut_ID, docs);

            // Newest first; entries with no document date sink to the bottom.
            docs.Sort(delegate (DODocumentData a, DODocumentData b)
            {
                return b.DocDate.GetValueOrDefault(DateTime.MinValue)
                        .CompareTo(a.DocDate.GetValueOrDefault(DateTime.MinValue));
            });
            return docs;
        }

        /// <summary>
        /// The customer invoices raised against this delivery's lines
        /// (C_InvoiceLine.M_InOutLine_ID — the delivery-scoped link, so an invoice
        /// covering another shipment of the same order is not claimed here).
        /// </summary>
        private void LoadInvoiceDocuments(int M_InOut_ID, List<DODocumentData> list)
        {
            try
            {
                string sql = @"SELECT inv.C_Invoice_ID,
                                      inv.DocumentNo,
                                      inv.DocStatus,
                                      inv.DateInvoiced,
                                      NVL(inv.GrandTotal, 0) AS GrandTotal,
                                      inv.IsPaid
                                 FROM C_Invoice inv
                                WHERE inv.IsActive   = 'Y'
                                  AND inv.IsSOTrx    = 'Y'
                                  AND inv.DocStatus NOT IN ('RE', 'VO')
                                  AND EXISTS (SELECT 1
                                                FROM C_InvoiceLine il
                                               INNER JOIN M_InOutLine iol
                                                       ON (iol.M_InOutLine_ID = il.M_InOutLine_ID)
                                               WHERE il.C_Invoice_ID = inv.C_Invoice_ID
                                                 AND il.IsActive     = 'Y'
                                                 AND iol.M_InOut_ID  = @M_InOut_ID)";
                DataSet ds = DB.ExecuteDataset(sql, InOutParam(M_InOut_ID), null);
                if (ds == null || ds.Tables.Count == 0) return;

                foreach (DataRow r in ds.Tables[0].Rows)
                {
                    list.Add(new DODocumentData
                    {
                        Type       = "invoice",
                        TableName  = "C_Invoice",
                        RecordId   = Util.GetValueOfInt(r["C_Invoice_ID"]),
                        DocumentNo = Util.GetValueOfString(r["DocumentNo"]),
                        DocStatus  = Util.GetValueOfString(r["DocStatus"]),
                        DocDate    = Util.GetValueOfDateTime(r["DateInvoiced"]),
                        Amount     = Util.GetValueOfDecimal(r["GrandTotal"]),
                        IsPaid     = Util.GetValueOfString(r["IsPaid"]) == "Y"
                    });
                }
            }
            catch (Exception ex)
            {
                _log.Severe("LoadInvoiceDocuments (M_InOut_ID=" + M_InOut_ID + "): " + ex.Message);
            }
        }

        /// <summary>
        /// The shipment confirmations recorded for this delivery (M_InOutConfirm),
        /// carrying their line count. A confirmation has no amount of its own, so
        /// its amount stays null and the panel prints nothing there.
        /// </summary>
        private void LoadConfirmationDocuments(int M_InOut_ID, List<DODocumentData> list)
        {
            try
            {
                string sql = @"SELECT c.M_InOutConfirm_ID,
                                      c.DocumentNo,
                                      c.DocStatus,
                                      c.Created,
                                      (SELECT COUNT(*)
                                         FROM M_InOutLineConfirm lc
                                        WHERE lc.M_InOutConfirm_ID = c.M_InOutConfirm_ID
                                          AND NVL(lc.IsActive, 'Y') = 'Y') AS LineCount
                                 FROM M_InOutConfirm c
                                WHERE c.M_InOut_ID  = @M_InOut_ID
                                  AND NVL(c.IsActive, 'Y') = 'Y'
                                  AND c.DocStatus NOT IN ('RE', 'VO')";
                DataSet ds = DB.ExecuteDataset(sql, InOutParam(M_InOut_ID), null);
                if (ds == null || ds.Tables.Count == 0) return;

                foreach (DataRow r in ds.Tables[0].Rows)
                {
                    list.Add(new DODocumentData
                    {
                        Type       = "confirmation",
                        TableName  = "M_InOutConfirm",
                        RecordId   = Util.GetValueOfInt(r["M_InOutConfirm_ID"]),
                        DocumentNo = Util.GetValueOfString(r["DocumentNo"]),
                        DocStatus  = Util.GetValueOfString(r["DocStatus"]),
                        DocDate    = Util.GetValueOfDateTime(r["Created"]),
                        LineCount  = Util.GetValueOfInt(r["LineCount"])
                    });
                }
            }
            catch (Exception ex)
            {
                // A schema without shipment confirmations simply lists none.
                _log.Severe("LoadConfirmationDocuments (M_InOut_ID=" + M_InOut_ID + "): " + ex.Message);
            }
        }

        /// <summary>
        /// The customer receipts (C_Payment, IsReceipt = 'Y') allocated to the
        /// invoices raised from this delivery, carrying the amount received and any
        /// discount taken.
        /// </summary>
        private void LoadReceiptDocuments(int M_InOut_ID, List<DODocumentData> list)
        {
            try
            {
                string sql = @"SELECT DISTINCT
                                      p.C_Payment_ID,
                                      p.DocumentNo,
                                      p.DocStatus,
                                      p.DateTrx,
                                      NVL(p.PayAmt, 0)      AS PayAmt,
                                      NVL(p.DiscountAmt, 0) AS DiscountAmt
                                 FROM C_Payment p
                                INNER JOIN C_AllocationLine al ON (al.C_Payment_ID = p.C_Payment_ID)
                                INNER JOIN C_InvoiceLine il    ON (il.C_Invoice_ID = al.C_Invoice_ID)
                                INNER JOIN M_InOutLine iol     ON (iol.M_InOutLine_ID = il.M_InOutLine_ID)
                                WHERE iol.M_InOut_ID = @M_InOut_ID
                                  AND p.IsActive     = 'Y'
                                  AND p.IsReceipt    = 'Y'
                                  AND p.DocStatus NOT IN ('RE', 'VO')";
                DataSet ds = DB.ExecuteDataset(sql, InOutParam(M_InOut_ID), null);
                if (ds == null || ds.Tables.Count == 0) return;

                foreach (DataRow r in ds.Tables[0].Rows)
                {
                    list.Add(new DODocumentData
                    {
                        Type        = "payment",
                        TableName   = "C_Payment",
                        RecordId    = Util.GetValueOfInt(r["C_Payment_ID"]),
                        DocumentNo  = Util.GetValueOfString(r["DocumentNo"]),
                        DocStatus   = Util.GetValueOfString(r["DocStatus"]),
                        DocDate     = Util.GetValueOfDateTime(r["DateTrx"]),
                        Amount      = Util.GetValueOfDecimal(r["PayAmt"]),
                        DiscountAmt = Util.GetValueOfDecimal(r["DiscountAmt"])
                    });
                }
            }
            catch (Exception ex)
            {
                _log.Severe("LoadReceiptDocuments (M_InOut_ID=" + M_InOut_ID + "): " + ex.Message);
            }
        }

        // ----------------------------------------------------------------- //
        //  Activity (audit trail)                                            //
        // ----------------------------------------------------------------- //

        /// <summary>
        /// The delivery order's audit trail, newest first — built to the same shape
        /// as the VAS_092 Purchase Order overview's:
        ///   * created — the record's own creation stamp;
        ///   * the document LIFECYCLE, one row per completed workflow node
        ///     (prepare / complete / re-activate / void / close / approve / reject);
        ///   * one "updated" row per FIELD edited, for the header AND its lines;
        ///   * the chat notes logged against it;
        ///   * the e-mails sent against it, body and all.
        ///
        /// Each source runs under its own guard, so a DB-level problem with one
        /// degrades to a partial trail (logged) rather than breaking the overview.
        /// </summary>
        /// <param name="M_InOut_ID">Selected delivery order id.</param>
        /// <returns>Activity rows, newest first; never null.</returns>
        private List<DOActivityData> LoadActivity(int M_InOut_ID)
        {
            // A runaway guard, not a headline count: the panel pages the feed 15
            // rows at a time, so it sits high enough that a real delivery never
            // reaches it.
            const int MAX_ENTRIES = 200;

            List<DOActivityData> activity = new List<DOActivityData>();
            LoadCreatedActivity(M_InOut_ID, activity);
            LoadWorkflowActivity(M_InOut_ID, activity);
            LoadChangeActivity(M_InOut_ID, activity);
            LoadNoteActivity(M_InOut_ID, activity);
            LoadEmailActivity(M_InOut_ID, activity);

            // Newest first; entries with no timestamp sink to the bottom.
            activity.Sort(delegate (DOActivityData a, DOActivityData b)
            {
                return b.Created.GetValueOrDefault(DateTime.MinValue)
                        .CompareTo(a.Created.GetValueOrDefault(DateTime.MinValue));
            });

            if (activity.Count > MAX_ENTRIES)
                activity = activity.GetRange(0, MAX_ENTRIES);
            return activity;
        }

        /// <summary>The delivery's own creation stamp and who made it.</summary>
        private void LoadCreatedActivity(int M_InOut_ID, List<DOActivityData> list)
        {
            try
            {
                string sql = @"SELECT io.Created, cu.Name AS CreatedByName
                                 FROM M_InOut io
                                 LEFT OUTER JOIN AD_User cu ON (io.CreatedBy = cu.AD_User_ID)
                                WHERE io.M_InOut_ID = @M_InOut_ID";
                DataSet ds = DB.ExecuteDataset(sql, InOutParam(M_InOut_ID), null);
                if (ds == null || ds.Tables.Count == 0 || ds.Tables[0].Rows.Count == 0) return;

                DataRow r = ds.Tables[0].Rows[0];
                list.Add(new DOActivityData
                {
                    Type     = "created",
                    UserName = Util.GetValueOfString(r["CreatedByName"]),
                    Created  = Util.GetValueOfDateTime(r["Created"])
                });
            }
            catch (Exception ex)
            {
                _log.Severe("LoadCreatedActivity (M_InOut_ID=" + M_InOut_ID + "): " + ex.Message);
            }
        }

        /// <summary>
        /// The delivery's document lifecycle, one row per completed workflow node
        /// (AD_WF_Process -> AD_WF_Activity -> AD_WF_Node, WFState 'CC') against
        /// this M_InOut: prepare, complete, re-activate, void, close, approve /
        /// reject — each with the node's own NAME as its headline, so a tenant that
        /// renamed its workflow nodes reads the trail in its own words.
        ///
        /// Read from the workflow rather than derived from M_InOut.Updated: that
        /// stamp is only ever the LAST save, so it can report neither a
        /// re-activation nor any completion but the most recent. Ported from
        /// VAS_092.
        /// </summary>
        private void LoadWorkflowActivity(int M_InOut_ID, List<DOActivityData> list)
        {
            try
            {
                string sql = @"SELECT wfa.Created              AS EventOn,
                                      COALESCE(wfn.Name, wfn.Value) AS NodeName,
                                      UPPER(TRIM(wfn.Value))   AS NodeValue,
                                      u.Name                   AS UserName
                                 FROM AD_WF_Process wfp
                                INNER JOIN AD_WF_Activity wfa
                                        ON (wfa.AD_WF_Process_ID = wfp.AD_WF_Process_ID)
                                INNER JOIN AD_WF_Node wfn
                                        ON (wfn.AD_WF_Node_ID = wfa.AD_WF_Node_ID)
                                INNER JOIN AD_Table adt
                                        ON (adt.AD_Table_ID = wfp.AD_Table_ID)
                                 LEFT OUTER JOIN AD_User u
                                        ON (u.AD_User_ID = wfa.CreatedBy)
                                WHERE wfp.Record_ID = @M_InOut_ID
                                  AND UPPER(adt.TableName) = 'M_INOUT'
                                  AND wfp.IsActive  = 'Y'
                                  AND wfa.IsActive  = 'Y'
                                  AND wfn.IsActive  = 'Y'
                                  AND wfa.WFState   = 'CC'
                                ORDER BY wfa.Created";
                DataSet ds = DB.ExecuteDataset(sql, InOutParam(M_InOut_ID), null);
                if (ds == null || ds.Tables.Count == 0) return;

                foreach (DataRow r in ds.Tables[0].Rows)
                {
                    string type = WorkflowActivityType(Util.GetValueOfString(r["NodeValue"]));
                    if (type == null) continue;      // routing / non-document node

                    list.Add(new DOActivityData
                    {
                        Type     = type,
                        Text     = Util.GetValueOfString(r["NodeName"]),
                        UserName = Util.GetValueOfString(r["UserName"]),
                        Created  = Util.GetValueOfDateTime(r["EventOn"])
                    });
                }
            }
            catch (Exception ex)
            {
                _log.Severe("LoadWorkflowActivity (M_InOut_ID=" + M_InOut_ID + "): " + ex.Message);
            }
        }

        /// <summary>
        /// Maps a workflow node value to an activity type the client can tag, or
        /// null for nodes that are pure routing (start / end / split) and carry no
        /// meaning for a reader. Ported from VAS_092.
        /// </summary>
        private static string WorkflowActivityType(string nodeValue)
        {
            if (string.IsNullOrEmpty(nodeValue)) return null;
            string v = nodeValue.Replace("(", "").Replace(")", "").Replace("_", "").Trim();

            if (v.Contains("REACTIVATE")) return "reactivated";   // before COMPLETE
            if (v.Contains("COMPLETE"))   return "completed";
            if (v.Contains("REJECT"))     return "rejected";
            if (v.Contains("APPROV"))     return "approval";
            if (v.Contains("VOID"))       return "voided";
            if (v.Contains("REVERSE"))    return "reversed";
            if (v.Contains("CLOSE"))      return "closed";
            if (v.Contains("PREPARE"))    return "prepared";
            if (v.Contains("INVALID"))    return "invalidated";
            return null;
        }

        /// <summary>
        /// One "updated" row per FIELD the user changed, read from the platform's
        /// change log (AD_ChangeLog). Each row names the field (the dictionary's
        /// display name for the column, falling back to the raw column name), who
        /// changed it and when.
        ///
        /// Both the header (M_InOut) and its LINES (M_InOutLine) are read. A
        /// delivery's substantive edits are its delivered quantities and locators,
        /// and those live on the lines — a header-only trail would report almost
        /// nothing. A line row is labelled with its line number and product.
        ///
        /// The table name is matched with UPPER so a dictionary holding it in
        /// another case still resolves: an equality on the stored spelling fails
        /// SILENTLY, which reads exactly like the loader was never written.
        ///
        /// Silently degrades when change logging is off for the ROLE that made the
        /// edit (AD_Role.IsChangeLog) — the platform writes no AD_ChangeLog rows at
        /// all in that case. That is a dictionary setting, not something this can
        /// fix.
        /// </summary>
        private void LoadChangeActivity(int M_InOut_ID, List<DOActivityData> list)
        {
            // ----- Header edits (M_InOut) -----
            try
            {
                // AD_Column is LEFT joined so a log row whose column has since been
                // removed from the dictionary still reports its change.
                string sql = @"SELECT cl.Created      AS EventOn,
                                      cl.OldValue     AS OldValue,
                                      cl.NewValue     AS NewValue,
                                      u.Name          AS UserName,
                                      col.Name        AS FieldLabel,
                                      col.ColumnName  AS FieldColumn
                                 FROM AD_ChangeLog cl
                                INNER JOIN AD_Table adt
                                        ON (adt.AD_Table_ID = cl.AD_Table_ID)
                                 LEFT OUTER JOIN AD_Column col
                                        ON (col.AD_Column_ID = cl.AD_Column_ID)
                                 LEFT OUTER JOIN AD_User u
                                        ON (u.AD_User_ID = cl.CreatedBy)
                                WHERE cl.Record_ID = @M_InOut_ID
                                  AND UPPER(adt.TableName) = 'M_INOUT'
                                  AND NVL(cl.IsActive, 'Y') = 'Y'
                                ORDER BY cl.Created DESC";
                DataSet ds = DB.ExecuteDataset(sql, InOutParam(M_InOut_ID), null);
                if (ds != null && ds.Tables.Count > 0)
                {
                    foreach (DataRow r in ds.Tables[0].Rows) AddChangeRow(r, "", list);
                }
            }
            catch (Exception ex)
            {
                _log.Severe("LoadChangeActivity/header (M_InOut_ID=" + M_InOut_ID + "): " + ex.Message);
            }

            // ----- Line edits (M_InOutLine) -----
            //
            // The line ids are reached through a join rather than a sub-select on
            // the same parameter, so the statement carries its bind name exactly
            // once: Oracle binds positionally, and a repeated name becomes a
            // second, unfilled placeholder.
            try
            {
                string sql = @"SELECT cl.Created      AS EventOn,
                                      cl.OldValue     AS OldValue,
                                      cl.NewValue     AS NewValue,
                                      u.Name          AS UserName,
                                      col.Name        AS FieldLabel,
                                      col.ColumnName  AS FieldColumn,
                                      l.Line          AS LineNo,
                                      p.Name          AS ProductName
                                 FROM AD_ChangeLog cl
                                INNER JOIN AD_Table adt
                                        ON (adt.AD_Table_ID = cl.AD_Table_ID)
                                INNER JOIN M_InOutLine l
                                        ON (l.M_InOutLine_ID = cl.Record_ID)
                                 LEFT OUTER JOIN M_Product p
                                        ON (p.M_Product_ID = l.M_Product_ID)
                                 LEFT OUTER JOIN AD_Column col
                                        ON (col.AD_Column_ID = cl.AD_Column_ID)
                                 LEFT OUTER JOIN AD_User u
                                        ON (u.AD_User_ID = cl.CreatedBy)
                                WHERE UPPER(adt.TableName) = 'M_INOUTLINE'
                                  AND NVL(cl.IsActive, 'Y') = 'Y'
                                  AND l.M_InOut_ID = @M_InOut_ID
                                ORDER BY cl.Created DESC";
                DataSet ds = DB.ExecuteDataset(sql, InOutParam(M_InOut_ID), null);
                if (ds != null && ds.Tables.Count > 0)
                {
                    foreach (DataRow r in ds.Tables[0].Rows)
                    {
                        // "#10 Steel Bolt M8" — the line number identifies the row,
                        // the product says what it is without a second lookup.
                        string scope = "#" + Util.GetValueOfInt(r["LineNo"]);
                        string prod  = Util.GetValueOfString(r["ProductName"]);
                        if (!string.IsNullOrEmpty(prod)) scope += " " + prod.Trim();
                        AddChangeRow(r, scope, list);
                    }
                }
            }
            catch (Exception ex)
            {
                _log.Severe("LoadChangeActivity/lines (M_InOut_ID=" + M_InOut_ID + "): " + ex.Message);
            }
        }

        /// <summary>
        /// Turns one AD_ChangeLog row into an "updated" activity entry. A change
        /// whose column the dictionary cannot name is skipped: it would render as a
        /// bare "Updated" identifying nothing, which is what naming the field
        /// exists to stop.
        /// </summary>
        private void AddChangeRow(DataRow r, string scope, List<DOActivityData> list)
        {
            DateTime? at = Util.GetValueOfDateTime(r["EventOn"]);
            if (!at.HasValue) return;

            string field = Util.GetValueOfString(r["FieldLabel"]);
            if (string.IsNullOrEmpty(field))
                field = Util.GetValueOfString(r["FieldColumn"]);
            if (string.IsNullOrEmpty(field)) return;

            // The move itself. A save that rewrites a field with the value it
            // already had is not an edit, and the platform logs plenty of those.
            string oldValue = ChangeValue(Util.GetValueOfString(r["OldValue"]));
            string newValue = ChangeValue(Util.GetValueOfString(r["NewValue"]));
            if (string.Equals(oldValue, newValue, StringComparison.Ordinal)) return;

            list.Add(new DOActivityData
            {
                Type        = "updated",
                FieldName   = field,
                OldValue    = oldValue,
                NewValue    = newValue,
                ChangeScope = scope,
                UserName    = Util.GetValueOfString(r["UserName"]),
                Created     = at
            });
        }

        /// <summary>
        /// Normalises a logged value for display. The platform writes the literal
        /// "null" into AD_ChangeLog for a cleared field, which would otherwise be
        /// shown to the reader as though it were the text "null". Follows VAS_101.
        /// </summary>
        private static string ChangeValue(string value)
        {
            if (string.IsNullOrEmpty(value)) return "";
            string v = value.Trim();
            return string.Equals(v, "null", StringComparison.OrdinalIgnoreCase) ? "" : v;
        }

        /// <summary>
        /// The chat notes (CM_ChatEntry) logged against this delivery. The author
        /// resolves from CM_ChatEntry.AD_User_ID falling back to CreatedBy: a note
        /// logged through the platform's own chat plumbing often leaves AD_User_ID
        /// null, which would print a comment with no name against it.
        /// </summary>
        private void LoadNoteActivity(int M_InOut_ID, List<DOActivityData> list)
        {
            try
            {
                string sql = @"SELECT ce.CharacterData,
                                      ce.Created,
                                      COALESCE(u.Name, cu.Name) AS UserName
                                 FROM CM_ChatEntry ce
                                INNER JOIN CM_Chat ch      ON (ce.CM_Chat_ID = ch.CM_Chat_ID)
                                 LEFT OUTER JOIN AD_User u  ON (ce.AD_User_ID = u.AD_User_ID)
                                 LEFT OUTER JOIN AD_User cu ON (ce.CreatedBy  = cu.AD_User_ID)
                                WHERE ch.AD_Table_ID IN
                                      (SELECT t.AD_Table_ID FROM AD_Table t
                                        WHERE UPPER(t.TableName) = 'M_INOUT')
                                  AND ch.Record_ID = @M_InOut_ID
                                  AND ce.IsActive  = 'Y'";
                DataSet ds = DB.ExecuteDataset(sql, InOutParam(M_InOut_ID), null);
                if (ds == null || ds.Tables.Count == 0) return;

                foreach (DataRow r in ds.Tables[0].Rows)
                {
                    list.Add(new DOActivityData
                    {
                        Type     = "note",
                        Text     = Util.GetValueOfString(r["CharacterData"]),
                        UserName = Util.GetValueOfString(r["UserName"]),
                        Created  = Util.GetValueOfDateTime(r["Created"])
                    });
                }
            }
            catch (Exception ex)
            {
                _log.Severe("LoadNoteActivity (M_InOut_ID=" + M_InOut_ID + "): " + ex.Message);
            }
        }

        /// <summary>
        /// The e-mails sent against this delivery (MailAttachment1, joined by
        /// AD_Table_ID = M_InOut + Record_ID) as "email" rows: recipients, subject
        /// (Title), body (TextMsg), when and who sent it. The body travels with the
        /// row so the panel can reveal it on click without a second round trip.
        ///
        /// "Has an address" is tested against a SPACE, not against ''. Oracle
        /// stores the empty string as NULL, so NVL(TRIM(x), '') yields NULL and
        /// `&lt;&gt; ''` compares against NULL — UNKNOWN for every row, including
        /// the ones that DO carry an address. Comparing to ' ' keeps the fallback
        /// non-null on Oracle, and SQL Server blank-pads the comparison so an empty
        /// address still fails it.
        /// </summary>
        private void LoadEmailActivity(int M_InOut_ID, List<DOActivityData> list)
        {
            try
            {
                string sql = @"SELECT ma.MailAddress,
                                      ma.MailAddressCc,
                                      ma.MailAddressBcc,
                                      ma.MailAddressFrom,
                                      ma.Title,
                                      ma.TextMsg,
                                      ma.Created,
                                      ma.IsMailSent,
                                      u.Name AS UserName
                                 FROM MailAttachment1 ma
                                 LEFT OUTER JOIN AD_User u ON (u.AD_User_ID = ma.CreatedBy)
                                WHERE ma.AD_Table_ID IN
                                      (SELECT t.AD_Table_ID FROM AD_Table t
                                        WHERE UPPER(t.TableName) = 'M_INOUT')
                                  AND ma.Record_ID          = @M_InOut_ID
                                  AND NVL(ma.IsActive, 'Y') = 'Y'
                                  AND (NVL(TRIM(ma.MailAddress), ' ')    <> ' '
                                    OR NVL(TRIM(ma.MailAddressCc), ' ')  <> ' '
                                    OR NVL(TRIM(ma.MailAddressBcc), ' ') <> ' ')
                                ORDER BY ma.Created DESC";
                DataSet ds = DB.ExecuteDataset(sql, InOutParam(M_InOut_ID), null);
                if (ds == null || ds.Tables.Count == 0) return;

                foreach (DataRow r in ds.Tables[0].Rows)
                {
                    list.Add(new DOActivityData
                    {
                        Type       = "email",
                        // Text is the row's headline everywhere in this feed; for
                        // an e-mail that is its subject.
                        Text       = Util.GetValueOfString(r["Title"]),
                        // A mail sent as HTML stores its markup in TextMsg; the
                        // panel shows a body as text, so it is flattened here.
                        Body       = MailBodyToText(Util.GetValueOfString(r["TextMsg"])),
                        MailTo     = Util.GetValueOfString(r["MailAddress"]),
                        MailCc     = Util.GetValueOfString(r["MailAddressCc"]),
                        MailBcc    = Util.GetValueOfString(r["MailAddressBcc"]),
                        MailFrom   = Util.GetValueOfString(r["MailAddressFrom"]),
                        IsMailSent = Util.GetValueOfString(r["IsMailSent"]) == "Y",
                        UserName   = Util.GetValueOfString(r["UserName"]),
                        Created    = Util.GetValueOfDateTime(r["Created"])
                    });
                }
            }
            catch (Exception ex)
            {
                // Non-fatal: a schema without MailAttachment1 just shows no e-mails.
                _log.Severe("LoadEmailActivity (M_InOut_ID=" + M_InOut_ID + "): " + ex.Message);
            }
        }

        /// <summary>
        /// Cheap "is this markup" test — a real tag, not a stray '&lt;' in a
        /// plain-text mail ("qty &lt; 10"), so a plain body is left untouched.
        /// </summary>
        private static readonly Regex HTML_BODY = new Regex(
            @"<\s*/?\s*(html|body|head|br|p|div|table|thead|tbody|tr|td|th|span|a|img|b|i|u"
            + @"|strong|em|ul|ol|li|h[1-6]|font|style|script)\b",
            RegexOptions.IgnoreCase | RegexOptions.Compiled);

        /// <summary>
        /// Renders a mail body (MailAttachment1.TextMsg) as readable plain text.
        ///
        /// A mail sent as HTML stores its markup here and the panel shows the body
        /// as text, so without this the reader gets tags instead of a message.
        /// Block-level markup becomes line breaks, table cells become tabs,
        /// everything else is dropped and entities are decoded LAST — so the
        /// browser still receives text it can safely escape and no markup is ever
        /// handed to the panel. A body with no markup is returned as stored.
        /// </summary>
        private static string MailBodyToText(string body)
        {
            if (string.IsNullOrEmpty(body)) return body;
            if (!HTML_BODY.IsMatch(body)) return body;      // plain-text mail

            try
            {
                string s = body;

                // Head matter, styles and scripts carry no reading content.
                s = Regex.Replace(s, @"<\s*(script|style|head)\b[^>]*>.*?<\s*/\s*\1\s*>", " ",
                                  RegexOptions.IgnoreCase | RegexOptions.Singleline);

                // Block boundaries become line breaks so paragraphs survive.
                s = Regex.Replace(s, @"<\s*br\s*/?\s*>", "\n", RegexOptions.IgnoreCase);
                s = Regex.Replace(s, @"<\s*/\s*(p|div|tr|li|h[1-6]|table|blockquote)\s*>", "\n",
                                  RegexOptions.IgnoreCase);
                // Opening tags too, so a <p> with no closing tag still breaks.
                // 'tr' is deliberately absent — </tr> already ends the row, and
                // breaking on both would leave a blank line between every row.
                s = Regex.Replace(s, @"<\s*(p|div|li|h[1-6])\b[^>]*>", "\n",
                                  RegexOptions.IgnoreCase);
                // Cells read better separated than run together.
                s = Regex.Replace(s, @"<\s*/\s*(td|th)\s*>", "\t", RegexOptions.IgnoreCase);

                // Everything left is presentation.
                s = Regex.Replace(s, @"<[^>]*>", string.Empty);

                // Entities last, so an escaped &lt;b&gt; in the text was never
                // treated as a tag above.
                s = WebUtility.HtmlDecode(s);
                s = s.Replace(' ', ' ');               // nbsp reads as a space

                // Normalise the whitespace the markup left behind.
                s = s.Replace("\r\n", "\n").Replace('\r', '\n');
                s = Regex.Replace(s, @"[^\S\n\t]+", " ");   // runs of spaces -> one
                s = Regex.Replace(s, @"\t{2,}", "\t");
                s = Regex.Replace(s, @"[ \t]*\n[ \t]*", "\n");   // incl. the last cell's tab
                s = Regex.Replace(s, @"\n{3,}", "\n\n");    // at most one blank line

                return s.Trim();
            }
            catch (Exception ex)
            {
                // Never lose the mail over a formatting failure — show it raw.
                _log.Severe("MailBodyToText: " + ex.Message);
                return body;
            }
        }

        // ----------------------------------------------------------------- //
        //  Data carriers                                                     //
        // ----------------------------------------------------------------- //

        /// <summary>Customer contact picked by <see cref="LoadCustomerContact"/>.</summary>
        private class ContactData
        {
            public string Name  { get; set; }
            public string EMail { get; set; }
            public string Phone { get; set; }
        }

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
            /// <summary>M_AttributeSetInstance.Description — the lot / serial /
            /// attributes the line was delivered against. Blank when it carries
            /// none.</summary>
            public string   AttributeName  { get; set; }
            /// <summary>True when this line is drop-shipped — the goods go from the
            /// vendor straight to the customer rather than out of our warehouse.
            /// Read from M_InOutLine.IsDropShip, falling back to the order line's
            /// own flag (where drop shipment is configured). False on a schema
            /// carrying neither column.</summary>
            public bool     IsDropShip     { get; set; }
            /// <summary>True when this line's product has QA parameters defined —
            /// what the line table's Quality column ticks and what decides whether
            /// the line carries an expand caret for its parameters.</summary>
            public bool     QualityApplicable { get; set; }
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
            public DateTime? Created        { get; set; }   // record creation stamp
            public DateTime? MovementDate   { get; set; }
            public string    PriorityCode   { get; set; }   // PriorityRule code
            public string    OrderReference { get; set; }   // POReference
            public string    Description    { get; set; }   // shown as Notes

            // Transport / dispatch
            public string    TrackingNo     { get; set; }
            public int       PackageCount   { get; set; }
            public string    TransportDoc   { get; set; }
            public string    VehicleNo      { get; set; }   // VAS_VehicleRegistrationNo
            public string    VehicleName    { get; set; }   // VAS_VehicleName
            public decimal   GrossWeight    { get; set; }
            public decimal   TareWeight     { get; set; }

            // Shipping method
            public string    DeliveryViaRule { get; set; }  // D / P / S
            public string    ShipperName     { get; set; }

            // Linked sales order
            public int       C_Order_ID     { get; set; }
            public string    SONo           { get; set; }
            public DateTime? SODateOrdered  { get; set; }
            public DateTime? SODatePromised { get; set; }

            // Reference / origins — each one the panel can open
            public int       C_Project_ID   { get; set; }
            public string    ProjectNo      { get; set; }
            public string    ProjectName    { get; set; }
            public int       Ref_InOut_ID   { get; set; }   // DO this one reverses
            public string    RefInOutDocNo  { get; set; }
            public int       M_RMA_ID       { get; set; }   // 0 when M_RMA is absent
            public string    RmaDocNo       { get; set; }

            // Customer
            public string    CustomerName         { get; set; }
            public string    CustomerTaxID        { get; set; }
            public string    CustomerLocationName { get; set; }
            public string    CustomerAddress      { get; set; }
            public string    CustomerFirstName    { get; set; }
            public string    CustomerEmail        { get; set; }
            /// <summary>Where the goods went — the delivery's own
            /// C_BPartner_Location_ID.</summary>
            public string    ShipToAddress        { get; set; }
            /// <summary>Where the invoice goes — the ORDER's Bill_Location_ID.
            /// Empty on a delivery raised without a sales order.</summary>
            public string    BillToAddress        { get; set; }
            // The contact, as the Sales Order overview presents one.
            public string    ContactName          { get; set; }
            public string    ContactPhone         { get; set; }
            public string    ContactEmail         { get; set; }

            // Receipt / dispatch parties
            public string    WarehouseName  { get; set; }
            public string    OwnerName      { get; set; }   // M_InOut.CreatedBy

            // Currency
            public string    CurSymbol      { get; set; }
            public string    ISO_Code       { get; set; }
            public int       StdPrecision   { get; set; }

            // KPI aggregates
            public int       LineCount      { get; set; }
            public decimal   DeliveredQty   { get; set; }
            public decimal   DeliveryValue  { get; set; }

            // Quality confirmation — the snapshot's Confirmation Check, Accepted
            // Quantity and Difference Quantity cards.
            /// <summary>C_DocType.IsShipConfirm — this document type raises a
            /// shipment confirmation. Decides whether the panel shows the three
            /// confirmation cards at all.</summary>
            public bool      IsShipConfirmDocType { get; set; }
            public decimal   AcceptedQty      { get; set; }   // Σ M_InOutLineConfirm.ConfirmedQty
            public decimal   DifferenceQty    { get; set; }   // Σ M_InOutLineConfirm.DifferenceQty
            public decimal   ScrappedQty      { get; set; }   // Σ M_InOutLineConfirm.ScrappedQty
            /// <summary>How many delivery lines carry a product with QA parameters
            /// defined — the Confirmation Check card's sub-label.</summary>
            public int       QaParamLineCount { get; set; }

            // Collections
            public List<DOLineData>         Lines         { get; set; }
            public List<DODocumentData>     Documents     { get; set; }
            public List<DOActivityData>     Activity      { get; set; }
            public List<DOQualityParamData> QualityParams { get; set; }
        }

        /// <summary>
        /// One VA010 quality-inspection row: a single test parameter checked
        /// against one product on the delivery. Read through the GRN overview's
        /// loader (the rules are identical for a shipment) and mapped here so the
        /// delivery payload carries delivery-named types.
        /// </summary>
        public class DOQualityParamData
        {
            public int      LineNo            { get; set; }   // owning M_InOutLine.Line
            public int      M_Product_ID      { get; set; }
            public string   ProductCode       { get; set; }
            public string   ProductName       { get; set; }
            public string   ParameterName     { get; set; }   // Colour / Size / Grade ...
            public decimal  QuantityToVerify  { get; set; }
            public int      AcceptableValueId { get; set; }
            public string   AcceptableValue   { get; set; }
            public int      ActualValueId     { get; set; }   // 0 = not inspected
            public string   ActualValue       { get; set; }
            public DateTime? QAQCDate         { get; set; }
            public string   Remark            { get; set; }
            public string   StatusCode        { get; set; }   // P passed, F failed, N pending
            /// <summary>True when the row comes from the quality PLAN rather than
            /// from a recorded inspection — what the confirmation is going to
            /// check, shown while the delivery is still drafted. Always Pending.</summary>
            public bool     IsPlanned         { get; set; }
        }

        /// <summary>
        /// One document raised against the delivery — a customer invoice, a
        /// shipment confirmation or a receipt. <see cref="TableName"/> +
        /// <see cref="RecordId"/> are what the panel opens. Mirrors the GRN
        /// overview's document row.
        /// </summary>
        public class DODocumentData
        {
            public string    Type        { get; set; }   // invoice | confirmation | payment
            public string    TableName   { get; set; }
            public int       RecordId    { get; set; }
            public string    DocumentNo  { get; set; }
            public string    DocStatus   { get; set; }
            public DateTime? DocDate     { get; set; }
            public int       LineCount   { get; set; }   // confirmations
            public decimal?  Amount      { get; set; }   // invoice total / receipt amount
            public decimal   DiscountAmt { get; set; }   // receipts
            public bool      IsPaid      { get; set; }   // invoices
        }

        /// <summary>One entry in the delivery order's audit trail.</summary>
        public class DOActivityData
        {
            /// <summary>The lifecycle and event types the client tags, matching
            /// VAS_092's set: created | prepared | completed | reactivated |
            /// rejected | approval | voided | reversed | closed | invalidated |
            /// updated (one per changed field) | note | email.</summary>
            public string    Type       { get; set; }
            public string    UserName   { get; set; }   // actor / mail sender
            public DateTime? Created    { get; set; }   // when
            /// <summary>The row's own headline where it has one: a note's comment,
            /// an e-mail's subject, a lifecycle row's workflow node name.</summary>
            public string    Text       { get; set; }

            // Field-level edit (AD_ChangeLog).
            /// <summary>The dictionary's label for the changed column.</summary>
            public string    FieldName  { get; set; }
            /// <summary>Which record the edit landed on: "" for the delivery
            /// header, else the line's number and product.</summary>
            public string    ChangeScope { get; set; }
            // The move itself, for an "updated" row: what the field held
            // before the edit and what it holds after. Either side is empty
            // where the log recorded no value — a field cleared, or filled
            // for the first time.
            public string    OldValue    { get; set; }
            public string    NewValue    { get; set; }

            // E-mail (MailAttachment1) — the body is revealed on click.
            public string    Body       { get; set; }   // TextMsg (flattened to text)
            public string    MailTo     { get; set; }   // MailAddress
            public string    MailCc     { get; set; }   // MailAddressCc
            public string    MailBcc    { get; set; }   // MailAddressBcc
            public string    MailFrom   { get; set; }   // MailAddressFrom
            public bool      IsMailSent { get; set; }
        }
    }
}
