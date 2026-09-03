/// <summary>
/// Module Name : VASLogic
/// Purpose     : Sales Order Overview tab panel data (read side) + two write
///               actions (Complete Sales Order, Create Contract from a line).
///               Returns header identity, customer + addresses, created-from
///               origin documents, order lines (with contract flag), delivery
///               readiness, deliveries, invoices, a merged activity timeline
///               and notes for a selected sales order (C_Order, IsSOTrx = 'Y').
///               Consumed by the VAS.VAS_106_OverviewSalesOrder tab panel.
///
///               Schema note: every table / column used here is verified to
///               exist in the platform model (C_Order, C_OrderLine, C_Contract,
///               VAS_Opportunity, M_InOut, C_Invoice, M_Storage, etc.). No
///               columns are invented. SQL avoids Oracle-only NVL / TRUNC /
///               TO_CHAR and PostgreSQL-only constructs (COALESCE + CASE only)
///               so a single query runs on both databases; date / currency
///               formatting is done client-side.
/// Chronological development:
///   VAI163   2026-07-08  Created
///   VAI163   2026-08-12  Added DeliveryRuleName: the DICTIONARY's own name for
///                        the shipping rule stored on the order, resolved through
///                        the COLUMN's reference list (AD_Column
///                        .AD_Reference_Value_ID -> AD_Ref_List, translated where
///                        the language has a translation) by the new
///                        LoadRefListName helper. The panel carried its own map of
///                        the values and was missing 'R' (After Receipt), so an
///                        order on that rule showed a bare "R"; reading the
///                        dictionary means the panel shows what the order screen
///                        shows, customer-added values and translations included.
///                        Its own map stays as the fallback.
///   VAI163   2026-08-12  - Lines and delivery-readiness rows carry their UOM name
///                          and their Attribute Set Instance. The ASI is joined
///                          only for a REAL instance (id > 0), so a line with no
///                          attributes cannot pick up the zero-record's "--"
///                          description.
///                        - Added Documents (LoadDocuments +
///                          LoadReceiptDocuments): the shipments, invoices and
///                          customer receipts raised against the order, in one
///                          flat list carrying TableName + RecordId, for the
///                          panel's single Documents section. Shipments and
///                          invoices are reused from the collections already
///                          loaded — so the section can never disagree with the
///                          KPIs derived from them — and the receipts are reached
///                          through C_AllocationLine -> C_Invoice, the sales-side
///                          mirror of the Purchase Order overview's payment rows.
///                          A shipment reports a null Amount: it has no monetary
///                          total, which is not the same as zero.
///   VAI163   2026-08-12  - Added CompletedDate (GetOrderCompletedDate: the
///                          workflow DocComplete stamp, falling back to the
///                          record's last change for an order completed outside
///                          the engine), which dates the progress line's Completed
///                          stage. It was dated by DateOrdered, a document field a
///                          user can back-date.
///                        - A chat note's author resolves from
///                          CM_ChatEntry.AD_User_ID falling back to CreatedBy: a
///                          note logged by the platform leaves AD_User_ID null, and
///                          those appeared in the feed with no name. Follows
///                          VAS_092.
///                        - Frequencies are no longer loaded on every panel read:
///                          they populated the inline contract form, which the
///                          panel no longer has. LoadFrequencies stays for the
///                          CreateContract endpoint.
///   VAI163   2026-08-12  - Order lines and delivery-readiness rows report their
///                          quantities and unit price in the line's SELECTED unit
///                          (QtyEntered / PriceEntered, with everything held in the
///                          base unit — QtyDelivered, QtyInvoiced, M_Storage's
///                          on-hand — scaled by the line's own base-per-selected
///                          ratio). The panel labels each row with that unit while
///                          being handed the BASE figures, so a line sold as 2 BOX
///                          of an EA-held product reported 24 at the per-EA price.
///                        - The readiness state is ready / instock / short; the
///                          "awaited" state is gone, matching the panel, which no
///                          longer has a tag for it.
///                        - The activity cap rose from 15 to 200. It was the same
///                          number the panel PAGES at, so the feed could never
///                          exceed one page and the sixteenth event onwards was
///                          unreachable rather than merely further down.
///                        - Added GetWindowId (ported from VAS_092), so the panel
///                          can name the SALES-side window of a dual-purpose table
///                          instead of letting the browser resolve the purchase
///                          one — an AR invoice was opening the AP Invoice screen.
///   VAI163   2026-08-12  - Added LoadHistory (C_OrderLineHistory): the prior
///                          versions of each order line, for the panel's per-line
///                          history drawer. Filtered on the history row's own
///                          C_Order_ID with a LEFT JOIN to the current line, so a
///                          line later removed still reports its snapshots. Ported
///                          from VAS_092, with COALESCE for NVL so it runs on both
///                          databases, and the optional columns dictionary-guarded.
///                        - Added LoadBlanketOrigin (C_Order.C_Order_Blanket): the
///                          blanket sales order this one was released against. Read
///                          in its own guarded statement — C_Order_Blanket is a
///                          module column, and putting it in the created-from query
///                          would have cost that query its other origins on a schema
///                          without it. An order released from a blanket showed no
///                          origin at all and the panel called it "Manual".
///                        - Added ColumnExists, which both of the above need.
///   VAI163   2026-08-12  - LoadCreatedFrom splits into one guarded statement per
///                          origin (quotation / opportunity / project / contract /
///                          blanket). They shared a single SELECT, so a schema
///                          missing any ONE of those optional module columns lost
///                          them ALL — the statement failed, the catch swallowed
///                          it, and an order raised from a project or a quotation
///                          reported no origin at all, which the panel then called
///                          "Manual". One absent column now costs only its own chip.
///                        - Added LoadContractOrigin: the contract master on the
///                          header (C_Order.VAS_ContractMaster_ID, the same link
///                          the PO overview's Contract chip uses — C_Order serves
///                          both sides) falling back to the service contract behind
///                          one of the lines (C_OrderLine.C_Contract_ID).
///                          ContractTable travels with it so the panel opens the
///                          right record.
///                        - The project origin carries its NUMBER (C_Project.Value)
///                          as well as its name: the strip names documents by their
///                          identifier.
///   VAI163   2026-08-12  Added GetWindowIdByTable (ported from VAS_102): the
///                        window a TABLE opens in, read from the dictionary, for a
///                        record whose screen cannot be named on the client. The
///                        Contract chip needs it — C_Contract and
///                        VAS_ContractMaster are maintained by module windows
///                        whose names cannot be hard-coded, so nothing else could
///                        resolve them.
///   VAI163   2026-08-12  - Activity now includes the e-mails sent against the
///                          order (LoadEmails / LoadEmailActivity: MailAttachment1
///                          reached by AD_Table_ID = C_Order + Record_ID): who it
///                          went to (MailAddress, with Cc / Bcc / From alongside),
///                          the subject (Title), the body (TextMsg), when (Created)
///                          and who sent it (CreatedBy). The body travels with the
///                          row so the panel can reveal it on click without a
///                          second round trip. Ported from VAS_092, with COALESCE
///                          for NVL so the statement runs on both databases.
///                        - E-mail bodies are flattened to readable text
///                          (MailBodyToText): a mail sent as HTML stores its markup
///                          in TextMsg, and the feed shows a body as text — so the
///                          reader would otherwise get tags instead of a message.
///                          No markup is ever handed to the panel.
///   VAI163   2026-08-17  - Order lines carry their LIST price
///                          (C_OrderLine.PriceList) for the panel's unit-price
///                          tooltip. Reported as stored, which is already the
///                          line's SELECTED unit: the platform prices from
///                          M_ProductPrice on the line's own C_UOM_ID and scales
///                          price list with price entered on a UOM change, so the
///                          pair sits on the scale the panel shows prices on.
///                        - Delivery readiness gains the "partial" state: a line
///                          part of which has already shipped reports that, ahead
///                          of the stock states, which only answer whether the
///                          REST could go out today. Its ordered / delivered pair
///                          travels with the row so the panel can name the
///                          quantities behind it.
///                        - LoadNotes reaches C_OrderLine through a LEFT OUTER JOIN
///                          (IsActive in the join, not the WHERE), so the header
///                          note — C_Order.Description — is returned even for an
///                          order with no active lines. The INNER JOIN dropped the
///                          whole result there, so a description the user had just
///                          typed and saved appeared nowhere and the Notes section
///                          was not drawn at all. A line note is labelled with its
///                          line NUMBER as well as the item, so two lines of the
///                          same product no longer read as one note repeated. Both
///                          follow VAS_092.
///   VAI163   2026-08-17  - Shipments carry their CREATED stamp (M_InOut.Created)
///                          as well as their movement date: the progress line's
///                          Shipped / Delivered stages are dated by when the
///                          delivery was RAISED, since MovementDate is a document
///                          field a user can back-date or set forward. Follows
///                          VAS_092's LastReceiptDate.
///                        - A shipment reports what it DELIVERED in money
///                          (DeliveredValue): each shipped line valued at its share
///                          of the order line's net amount — a share of the line's
///                          own money, so no view is needed on which unit either
///                          side is held in. The Documents list showed a blank
///                          amount against every shipment.
///                        - Delivery readiness is reported entirely in the
///                          PRODUCT's base unit, and labelled with it
///                          (M_Product.C_UOM_ID). It converted onto the line's
///                          selected unit, so a line sold by the box set a boxed
///                          pending figure against warehouse stock counted in each.
///                        - Order lines carry C_OrderLine.IsDropShip, for the
///                          panel's per-line Drop Shipment: Yes / No.
///                        - Activity reports edits FIELD BY FIELD
///                          (LoadOrderChangeActivity, AD_ChangeLog): one "Updated"
///                          row per changed column — the field's dictionary name,
///                          who changed it and when — for the header AND its lines,
///                          a line's row naming its line number. Ported from
///                          VAS_092.
///                        - The milestone row for a completed order is typed
///                          "Completed" rather than "Updated" (which now means an
///                          edit) and is dated by the workflow's DocComplete stamp,
///                          falling back to the record's last change.
///   VAI163   2026-08-17  Field-level activity carries the OLD and NEW values
///                        (AD_ChangeLog.OldValue / NewValue). Both are normalised
///                        through ChangeValue: the literal "null" the platform
///                        writes for a cleared field reads as empty, not as the
///                        word. A row whose two values are equal is dropped — a
///                        save that rewrote a field with the value it already had
///                        is not an edit, and the platform logs plenty of those.
///                        The trail said WHICH field moved but never what it moved
///                        from or to. Follows VAS_101 / VAS_104.
///                        The line number and item move out of the field's NAME
///                        into ChangeScope, where the panel draws them on their
///                        own sub-line rather than inside the headline.
///   VAI163   2026-08-20  - LoadProjectOrigin reads C_Order.C_ProjectRef_ID as well
///                          as C_Project_ID. Both name a C_Project — the platform's
///                          own revenue-recognition code posts C_ProjectRef_ID
///                          straight into a journal line's C_Project_ID — and an
///                          order that carries only the reference reported no
///                          origin at all, so the strip called it "Manual". The
///                          record's own C_Project_ID still wins where it has one;
///                          each column is guarded separately, so a schema with
///                          neither behaves exactly as before.
///                        - Invoices carry their CREATED stamp (C_Invoice.Created)
///                          alongside DateInvoiced, for the progress line's
///                          Invoiced stage. DateInvoiced is a document field a user
///                          can back-date, and the stage is dated by when the
///                          invoice was actually raised — the same treatment
///                          shipments were given for Shipped / Delivered.
///   VAI163   2026-08-20  The Quotation and Blanket Order origins are ATTEMPTED
///                        rather than gated on ColumnExists, and each falls back
///                        to the link the LINES carry. Both chips reported nothing
///                        and the strip called the order "Manual".
///                        Two independent faults, one per chip:
///                          * The dictionary guard was itself the failure. A
///                            deployment whose AD_Column has no row for the column
///                            — or whose AD_Table carries more than one row named
///                            C_Order, which makes the guard's scalar sub-select
///                            RAISE rather than answer — was told "no such column"
///                            however good the data was. VAS_092 reached this
///                            conclusion on its own blanket chip; both statements
///                            now run, and a genuinely missing column throws once
///                            and is remembered (the static _*LookupUsable flags).
///                          * The header column is not where the link always
///                            lives. C_Order.C_Order_Blanket is stamped only by
///                            CreateReleaseDocFromBO, while MOrder.CopyFrom writes
///                            C_OrderLine.C_OrderLine_Blanket_ID for ANY release
///                            document; CopyOrder stamps the quotation header only
///                            where C_Order carries the column, and the line
///                            references (C_OrderLine.C_Order_Quotation, else
///                            C_Quotation_Line_ID) separately. Reading the header
///                            alone found nothing on either.
///                        Every predicate is COALESCE rather than NVL, so the
///                        statements read the same on Oracle and PostgreSQL.
///   VAI163   2026-08-21  Activity: an appointment or task now carries the
///                        e-mails sent against IT - MailAttachment1 keyed on
///                        AppointmentsInfo rather than on this panel's own
///                        table - with the recipient (MailAddress), subject
///                        (Title), when (Created) and who sent it (CreatedBy).
///                        The body (TextMsg, flattened) travels with the row so
///                        the panel reveals it on click. Read in one query for
///                        the whole feed through VAS_ActivitySourcesModel.
///   VAI163   2026-08-24  - CustomerEmail: the address the panel's Send Invoice
///                          button seeds the share/e-mail form's recipient with,
///                          beside the customer's name. The order's own contact
///                          address is preferred; any active contact of the
///                          customer (MIN over AD_User, so the scalar sub-select
///                          cannot return two rows and raise on Oracle) stands in
///                          when the order names none. A blank one is not a
///                          failure — VAS_SentEmailDocModel resolves the recipient
///                          from AD_Table_ID + RecordID on the server.
///                        - IsEmailSent (C_Order.VAS_IsEmailSent) for the header's
///                          "Email Sent" badge, drawn only when the flag is set —
///                          the milestone rule the Posted badge beside it follows.
///                          Read in its OWN statement (LoadEmailSent) and ATTEMPTED
///                          rather than dictionary-guarded: it is a module column,
///                          so selecting it alongside the header would fail the
///                          WHOLE overview on a deployment that has not taken it.
///   VAI163   2026-08-26  - The e-mail feed was EMPTY on databases that have mails.
///                          Two causes, both here. The AD_Table id was a SCALAR
///                          sub-select, and AD_Table can carry more than one row
///                          named C_Order — which on Oracle RAISES rather than
///                          answering, taking the whole lookup into its catch. And
///                          AttachmentType was required to equal 'M', a value that
///                          varies between installations and is sometimes null. It
///                          is IN + UPPER for the table and "not 'I'" for the kind
///                          now: 'I' is a letter and anything else a mail, so the
///                          two partition the table — nothing hidden, and nothing
///                          double-counted against the shared loader, which takes
///                          the letters (it is called with includeMail: false).
///                        - IsShipConfirmTarget (LoadShipConfirmTarget):
///                          IsShipConfirm on C_DocTypeTarget_ID, falling back to
///                          the completed type. The progress line's Shipped and
///                          Delivered stages read the delivery order differently
///                          with confirmation on — where it SITS In Process
///                          awaiting confirmation — than with it off, where
///                          completion is the milestone. Its own attempted
///                          statement, like LoadEmailSent.
///                        - Deliveries, invoices and receipts each carry
///                          CompletedDate: the document's workflow DocComplete
///                          stamp, falling back to its own Updated stamp, and null
///                          while it is open. The Delivered / Invoiced / Paid
///                          stages date themselves by these — they went green on a
///                          document merely EXISTING before, so a drafted invoice
///                          reported an invoiced order. Read for each set in ONE
///                          statement (LoadCompletionStamps), not per document.
///   VAI163   2026-09-01  CustomerEmail came back BLANK on PostgreSQL, so Send
///                        Invoice fell through to the server recipient lookup,
///                        which failed the same way and left the user on the
///                        screen instead of the Preview and Share Document form.
///                        Cause: "EMail IS NOT NULL" is an Oracle-only test for
///                        "has an address" — on PostgreSQL an empty string is a
///                        real value that passes it, and since '' sorts first the
///                        MIN() picked the blank. Both the sub-select and the
///                        chosen address are now length-tested after TRIM, which
///                        reads the same on either engine. Same fix as VAS_092.
///   VAI163   2026-09-01  Times were wrong on PostgreSQL — appointments first, but
///                        every stamp the panel prints had the same defect. The
///                        DateTimeKind the PROVIDER tags a value with reached the
///                        JSON: Oracle says Unspecified and Npgsql says Utc or
///                        Local, Newtonsoft writes a zone designator for the latter
///                        two and none for the first, and the panel's parseDbDate
///                        reads the two shapes differently. EVERY date and
///                        timestamp this model emits now goes through Stamp() — the
///                        header dates, the delivery / invoice / payment stamps,
///                        the history and completion dates, the change log's
///                        EventOn and every activity EventTime — as do the shared
///                        appointment / task / call / letter sources in
///                        VAS_ActivitySourcesModel, where the helper lives. A no-op
///                        on Oracle.
/// </summary>

using System;
using System.Collections.Generic;
using System.Data;
using System.Data.SqlClient;
using System.Net;
using System.Text.RegularExpressions;
using VAdvantage.DataBase;
using VAdvantage.Logging;
using VAdvantage.Model;
using VAdvantage.Utility;

namespace VASLogic.Models
{
    public class VAS_106_OverviewSalesOrderModel
    {
        private static readonly VLogger _log =
            VLogger.GetVLogger(typeof(VAS_106_OverviewSalesOrderModel).FullName);

        // ================================================================= //
        //  READ SIDE                                                        //
        // ================================================================= //

        /// <summary>
        /// Returns the full overview payload for the selected sales order.
        /// MRole access filtering is applied only on the primary physical table
        /// (C_Order alias "o"); child queries inherit the parent's authorization.
        /// </summary>
        /// <param name="ctx">User context (client/org/role).</param>
        /// <param name="C_Order_ID">Selected sales order id.</param>
        /// <returns>Populated <see cref="SalesOrderOverviewData"/>; an empty
        /// instance when the id is invalid or no accessible row is found.</returns>
        public SalesOrderOverviewData GetSalesOrderOverview(Ctx ctx, int C_Order_ID)
        {
            SalesOrderOverviewData result = new SalesOrderOverviewData();
            if (C_Order_ID <= 0) return result;

            string sql = @"SELECT
                              o.C_Order_ID,
                              o.DocumentNo,
                              o.POReference,
                              o.DateOrdered,
                              o.DatePromised,
                              o.DocStatus,
                              o.Posted,
                              o.PriorityRule,
                              o.Created,
                              o.GrandTotal,
                              o.TotalLines,
                              o.C_Currency_ID,
                              o.C_BPartner_ID,
                              o.C_BPartner_Location_ID   AS ShipLocationId,
                              o.Bill_Location_ID         AS BillLocationId,
                              o.AD_User_ID               AS ContactId,
                              o.DeliveryRule,
                              o.InvoiceRule,
                              bp.Name                    AS CustomerName,
                              bp.SOCreditStatus          AS CreditStatus,
                              contact.Name               AS ContactName,
                              contact.Phone              AS ContactPhone,
                              contact.EMail              AS ContactEmail,
                              -- The CUSTOMER's own e-mail address, used to seed the
                              -- Send Invoice recipient when the order names no
                              -- contact of its own. MIN, not a bare column: a
                              -- customer can carry several contacts and a scalar
                              -- sub-select that returns more than one row raises on
                              -- Oracle instead of answering.
                              --   IS NOT NULL alone is an ORACLE-ONLY filter: there
                              -- an empty string IS null, on PostgreSQL it is a real
                              -- value that survives the test — and because '' sorts
                              -- before every address, MIN then returns the BLANK for
                              -- any customer carrying one contact with no e-mail.
                              -- That is what left the Send Invoice recipient empty on
                              -- PostgreSQL. LENGTH(TRIM(..)) > 0 drops blank and
                              -- whitespace-only addresses on both engines (on Oracle
                              -- TRIM of a blank is null, so the row fails the test
                              -- there too).
                              (SELECT MIN(bpu.EMail)
                                 FROM AD_User bpu
                                WHERE bpu.C_BPartner_ID = o.C_BPartner_ID
                                  AND bpu.IsActive      = 'Y'
                                  AND bpu.EMail IS NOT NULL
                                  AND LENGTH(TRIM(bpu.EMail)) > 0) AS CustomerEMail,
                              sr.Name                    AS SalesRepName,
                              pt.Name                    AS PaymentTermName,
                              pl.Name                    AS PriceListName,
                              wh.Name                    AS WarehouseName,
                              cur.CurSymbol              AS CurSymbol,
                              cur.ISO_Code               AS ISO_Code,
                              cur.StdPrecision           AS StdPrecision
                            FROM C_Order o
                            INNER JOIN C_BPartner bp        ON (o.C_BPartner_ID   = bp.C_BPartner_ID)
                            LEFT OUTER JOIN AD_User contact  ON (o.AD_User_ID      = contact.AD_User_ID)
                            LEFT OUTER JOIN AD_User sr        ON (o.SalesRep_ID     = sr.AD_User_ID)
                            LEFT OUTER JOIN C_PaymentTerm pt  ON (o.C_PaymentTerm_ID = pt.C_PaymentTerm_ID)
                            LEFT OUTER JOIN M_PriceList pl    ON (o.M_PriceList_ID  = pl.M_PriceList_ID)
                            LEFT OUTER JOIN M_Warehouse wh    ON (o.M_Warehouse_ID  = wh.M_Warehouse_ID)
                            INNER JOIN C_Currency cur         ON (o.C_Currency_ID   = cur.C_Currency_ID)
                            WHERE o.C_Order_ID = @C_Order_ID
                              AND o.IsActive   = 'Y'
                              AND o.IsSOTrx    = 'Y'";

            sql = MRole.GetDefault(ctx).AddAccessSQL(
                sql, "o", MRole.SQL_FULLYQUALIFIED, MRole.SQL_RO);

            DataSet ds = DB.ExecuteDataset(sql, OrderParam(C_Order_ID), null);
            if (ds == null || ds.Tables.Count == 0 || ds.Tables[0].Rows.Count == 0)
                return result;

            DataRow r = ds.Tables[0].Rows[0];

            // ----- Header / identity -----
            result.C_Order_ID   = Util.GetValueOfInt(r["C_Order_ID"]);
            result.DocumentNo   = Util.GetValueOfString(r["DocumentNo"]);
            result.POReference  = Util.GetValueOfString(r["POReference"]);
            result.DateOrdered  = Stamp(r["DateOrdered"]);
            result.DatePromised = Stamp(r["DatePromised"]);
            result.DocStatus    = Util.GetValueOfString(r["DocStatus"]);
            result.Posted       = Util.GetValueOfString(r["Posted"]);
            result.PriorityRule = Util.GetValueOfString(r["PriorityRule"]);
            result.Created      = Stamp(r["Created"]);

            result.GrandTotal   = Util.GetValueOfDecimal(r["GrandTotal"]);
            result.TotalLines   = Util.GetValueOfDecimal(r["TotalLines"]);
            result.TaxAmt       = result.GrandTotal - result.TotalLines;

            result.C_Currency_ID = Util.GetValueOfInt(r["C_Currency_ID"]);
            result.CurSymbol     = Util.GetValueOfString(r["CurSymbol"]);
            result.ISO_Code      = Util.GetValueOfString(r["ISO_Code"]);
            result.StdPrecision  = Util.GetValueOfInt(r["StdPrecision"]);

            result.C_BPartner_ID  = Util.GetValueOfInt(r["C_BPartner_ID"]);
            result.CustomerName   = Util.GetValueOfString(r["CustomerName"]);
            result.CreditStatus   = Util.GetValueOfString(r["CreditStatus"]);
            result.ContactName    = Util.GetValueOfString(r["ContactName"]);
            result.ContactPhone   = Util.GetValueOfString(r["ContactPhone"]);
            result.ContactEmail   = Util.GetValueOfString(r["ContactEmail"]);
            // Recipient seed for the Send Invoice / share-document flow. The order's
            // own contact address is preferred — that is the person this sales order
            // was placed with — and any active contact of the customer stands in
            // when the order names none.
            //   Both candidates are TRIMMED before they are weighed: on PostgreSQL a
            // contact row can hold an empty (or whitespace-only) address where Oracle
            // would hold a null, and an all-blank string is not a recipient. What
            // survives is a real address or nothing at all, which is what the panel's
            // Send Invoice button needs to decide whether to seed the share form.
            result.CustomerEmail  = result.ContactEmail.Trim().Length > 0
                                    ? result.ContactEmail.Trim()
                                    : Util.GetValueOfString(r["CustomerEMail"]).Trim();
            result.SalesRepName   = Util.GetValueOfString(r["SalesRepName"]);
            result.PaymentTermName = Util.GetValueOfString(r["PaymentTermName"]);
            result.PriceListName  = Util.GetValueOfString(r["PriceListName"]);
            result.WarehouseName  = Util.GetValueOfString(r["WarehouseName"]);
            result.DeliveryRule   = Util.GetValueOfString(r["DeliveryRule"]);
            result.InvoiceRule    = Util.GetValueOfString(r["InvoiceRule"]);

            int shipLocId = Util.GetValueOfInt(r["ShipLocationId"]);
            int billLocId = Util.GetValueOfInt(r["BillLocationId"]);

            // The dictionary's own name for the shipping rule stored on the order,
            // so the panel reads what the order screen reads.
            result.DeliveryRuleName = LoadRefListName(ctx, "C_Order", "DeliveryRule", result.DeliveryRule);

            // When the order was actually completed, for the progress line's
            // Completed stage.
            result.CompletedDate = GetOrderCompletedDate(C_Order_ID);

            // Has the order been e-mailed to the customer? Drives the header's
            // "Email Sent" badge.
            LoadEmailSent(C_Order_ID, result);

            // Is this order shipped WITH a confirmation? The progress line's Shipped
            // and Delivered stages read the delivery order differently either way.
            LoadShipConfirmTarget(C_Order_ID, result);

            // ----- Child data -----
            LoadAddresses(shipLocId, billLocId, result);
            LoadCreatedFrom(C_Order_ID, result);
            result.Lines            = LoadLines(C_Order_ID);
            result.History          = LoadHistory(C_Order_ID);
            result.DeliveryReadiness = LoadDeliveryReadiness(C_Order_ID);
            result.Deliveries       = LoadDeliveries(C_Order_ID);
            result.Invoices         = LoadInvoices(C_Order_ID);
            // One flat list for the Documents section, built from the two above
            // plus the receipts allocated to those invoices.
            result.Documents        = LoadDocuments(C_Order_ID, result.Deliveries, result.Invoices);
            result.Activity         = LoadActivity(C_Order_ID, result.C_BPartner_ID);
            result.Notes            = LoadNotes(C_Order_ID);
            // Frequencies are no longer loaded: they populated the panel's inline
            // contract form, which is gone — the Contract cell is read-only now.
            // LoadFrequencies stays for the CreateContract endpoint's sake, so the
            // list is a query away if the flow ever returns; running it on every
            // panel load fed nothing.

            return result;
        }

        /// <summary>
        /// Resolves a window's AD_Window_ID from its name (AD_Window.Name) for the
        /// panel's record-open path.
        ///
        /// The documents this panel opens are all SALES-side records of tables that
        /// serve both sides — C_Invoice is an AR invoice here, C_Payment an AR
        /// receipt, M_InOut a shipment — and the browser's zoom lookup resolved the
        /// purchase-side window for them, so an invoice number opened the AP Invoice
        /// screen. Naming the window is what settles it.
        ///
        /// Restricted to windows this tenant can see (AD_Client_ID 0 or its own),
        /// preferring the tenant's own row over the system one. Whether the ROLE may
        /// open it is the platform's call, made when the window is started. Ported
        /// from VAS_092.
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
                                WHERE w.Name         = @Name
                                  AND w.IsActive     = 'Y'
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
        /// Resolves the window a TABLE's records open in: the table's own zoom
        /// target (AD_Table.AD_Window_ID), falling back to the first window that
        /// has a tab on the table.
        ///
        /// This is the panel's last resort for a record whose screen cannot be
        /// named on the client. A contract is the case that needed it: C_Contract
        /// and VAS_ContractMaster are maintained by module windows whose names
        /// cannot be hard-coded here, and the browser-side zoom lookup only knows
        /// tables the client has cached — so the Contract chip fell through to the
        /// "cannot open" toast. Reading the dictionary works for any installed
        /// module, and any future chip gets the same safety net. Ported from
        /// VAS_102.
        ///
        /// Each statement carries a single bind name, occurring once: positional
        /// binding gives a repeated name a second, unfilled placeholder.
        /// </summary>
        /// <param name="ctx">User context (unused today; kept for symmetry with
        /// <see cref="GetWindowId"/>, which filters by client).</param>
        /// <param name="tableName">Physical table name, e.g. "C_Contract".</param>
        /// <returns>The window id, or 0 when the table has no window at all.</returns>
        public int GetWindowIdByTable(Ctx ctx, string tableName)
        {
            if (string.IsNullOrEmpty(tableName)) return 0;
            string name = tableName.Trim();
            try
            {
                string sql = @"SELECT t.AD_Window_ID
                                 FROM AD_Table t
                                WHERE UPPER(t.TableName) = UPPER(@TableName)
                                  AND t.IsActive         = 'Y'
                                  AND COALESCE(t.AD_Window_ID, 0) > 0";
                DataSet ds = DB.ExecuteDataset(
                    sql, new SqlParameter[] { new SqlParameter("@TableName", name) }, null);
                if (ds != null && ds.Tables.Count > 0 && ds.Tables[0].Rows.Count > 0)
                {
                    int id = Util.GetValueOfInt(ds.Tables[0].Rows[0]["AD_Window_ID"]);
                    if (id > 0) return id;
                }

                // No zoom target on the table itself — take the window whose first
                // tab sits on this table, which is the screen that maintains it.
                sql = @"SELECT tb.AD_Window_ID
                          FROM AD_Tab tb
                         INNER JOIN AD_Table t ON (t.AD_Table_ID = tb.AD_Table_ID)
                         WHERE UPPER(t.TableName) = UPPER(@TableName)
                           AND tb.IsActive        = 'Y'
                           AND t.IsActive         = 'Y'
                           AND tb.SeqNo = (SELECT MIN(tb2.SeqNo)
                                             FROM AD_Tab tb2
                                            WHERE tb2.AD_Window_ID = tb.AD_Window_ID
                                              AND tb2.IsActive     = 'Y')
                         ORDER BY tb.SeqNo, tb.AD_Tab_ID";
                ds = DB.ExecuteDataset(
                    sql, new SqlParameter[] { new SqlParameter("@TableName", name) }, null);
                if (ds == null || ds.Tables.Count == 0 || ds.Tables[0].Rows.Count == 0)
                    return 0;
                return Util.GetValueOfInt(ds.Tables[0].Rows[0]["AD_Window_ID"]);
            }
            catch (Exception ex)
            {
                _log.Severe("GetWindowIdByTable (" + name + "): " + ex.Message);
                return 0;
            }
        }

        /// <summary>
        /// The moment the order was completed: the Created stamp of its workflow
        /// DocComplete activity, falling back to the record's own last change for
        /// an order that reached CO / CL outside the workflow engine.
        ///
        /// A standalone query, never a subselect of the MRole-rewritten header
        /// SELECT — the role filter rewrites that statement and an added subselect
        /// can fail against it.
        /// </summary>
        /// <param name="C_Order_ID">Selected sales order id.</param>
        /// <returns>The completion stamp, or null when the order has none.</returns>
        private DateTime? GetOrderCompletedDate(int C_Order_ID)
        {
            try
            {
                string sql = @"SELECT MAX(wfa.Created) AS CompletedDate
                                 FROM AD_WF_Process wfp
                                INNER JOIN AD_WF_Activity wfa
                                        ON (wfa.AD_WF_Process_ID = wfp.AD_WF_Process_ID)
                                INNER JOIN AD_Table adt
                                        ON (adt.AD_Table_ID = wfp.AD_Table_ID)
                                WHERE wfp.Record_ID  = @C_Order_ID
                                  AND adt.TableName  = 'C_Order'
                                  AND wfp.IsActive   = 'Y'
                                  AND wfa.IsActive   = 'Y'
                                  AND wfa.WFState    = 'CC'";
                DataSet ds = DB.ExecuteDataset(sql, OrderParam(C_Order_ID), null);
                if (ds != null && ds.Tables.Count > 0 && ds.Tables[0].Rows.Count > 0)
                {
                    DateTime? d = Stamp(ds.Tables[0].Rows[0]["CompletedDate"]);
                    if (d.HasValue) return d;
                }

                // Completed outside the workflow engine — the last change to the
                // record is the closest stamp there is.
                string fallback = @"SELECT o.Updated
                                      FROM C_Order o
                                     WHERE o.C_Order_ID = @C_Order_ID
                                       AND o.DocStatus IN ('CO', 'CL')";
                ds = DB.ExecuteDataset(fallback, OrderParam(C_Order_ID), null);
                if (ds == null || ds.Tables.Count == 0 || ds.Tables[0].Rows.Count == 0) return null;
                return Stamp(ds.Tables[0].Rows[0]["Updated"]);
            }
            catch (Exception ex)
            {
                _log.Severe("GetOrderCompletedDate (C_Order_ID=" + C_Order_ID + "): " + ex.Message);
                return null;
            }
        }

        /// <summary>
        /// The dictionary's display name for one value of a reference-list column
        /// — the label the record screen shows for it.
        ///
        /// The reference is reached through the COLUMN
        /// (AD_Column.AD_Reference_Value_ID), not by hard-coding a reference id, so
        /// a deployment that points the column at its own list is read correctly.
        /// Translated where the user's language has a translation.
        ///
        /// Returns "" when the value, the column or the list entry cannot be
        /// resolved, which leaves the panel on its own built-in labels.
        /// </summary>
        /// <param name="ctx">User context, for the language.</param>
        /// <param name="tableName">Table owning the column, e.g. "C_Order".</param>
        /// <param name="columnName">Reference-list column, e.g. "DeliveryRule".</param>
        /// <param name="value">The stored value, e.g. "R".</param>
        /// <returns>The list entry's name, or "".</returns>
        private string LoadRefListName(Ctx ctx, string tableName, string columnName, string value)
        {
            if (string.IsNullOrEmpty(value)) return "";
            try
            {
                string lang = (ctx == null) ? "" : ctx.GetAD_Language();

                // The translated name where one exists, else the base name. Each
                // bind name occurs exactly once: positional binding gives a
                // repeated name a second, unfilled placeholder.
                string sql = @"SELECT COALESCE(rlt.Name, rl.Name) AS Name
                                 FROM AD_Ref_List rl
                                INNER JOIN AD_Column c
                                        ON (c.AD_Reference_Value_ID = rl.AD_Reference_ID)
                                INNER JOIN AD_Table t
                                        ON (t.AD_Table_ID = c.AD_Table_ID)
                                 LEFT OUTER JOIN AD_Ref_List_Trl rlt
                                        ON (rlt.AD_Ref_List_ID = rl.AD_Ref_List_ID
                                            AND rlt.AD_Language = @AD_Language)
                                WHERE UPPER(t.TableName)  = UPPER(@TableName)
                                  AND UPPER(c.ColumnName) = UPPER(@ColumnName)
                                  AND rl.Value            = @Value
                                  AND rl.IsActive         = 'Y'";
                SqlParameter[] param = new SqlParameter[]
                {
                    new SqlParameter("@AD_Language", lang),
                    new SqlParameter("@TableName", tableName),
                    new SqlParameter("@ColumnName", columnName),
                    new SqlParameter("@Value", value)
                };
                DataSet ds = DB.ExecuteDataset(sql, param, null);
                if (ds == null || ds.Tables.Count == 0 || ds.Tables[0].Rows.Count == 0) return "";
                return Util.GetValueOfString(ds.Tables[0].Rows[0]["Name"]);
            }
            catch (Exception ex)
            {
                _log.Severe("LoadRefListName (" + tableName + "." + columnName + "=" + value + "): " + ex.Message);
                return "";
            }
        }

        /// <summary>
        /// Loads the ship-to and bill-to addresses (C_BPartner_Location +
        /// C_Location) and composes each into a single display string.
        /// </summary>
        private void LoadAddresses(int shipLocationId, int billLocationId, SalesOrderOverviewData d)
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
                _log.Severe("LoadAddresses (C_Order_ID=" + d.C_Order_ID + "): " + ex.Message);
            }
        }

        /// <summary>
        /// Loads the origin documents the order was created from: quotation
        /// (C_Order.C_Order_Quotation, else the same reference on the lines),
        /// opportunity (C_Order.VAS_Opportunity_ID -> VAS_Opportunity.Name),
        /// project (C_Order.C_Project_ID, else C_ProjectRef_ID -> C_Project) and
        /// the blanket it was released against (C_Order.C_Order_Blanket, else
        /// C_OrderLine.C_OrderLine_Blanket_ID). Each source column is
        /// module-optional, so a missing one degrades to "no created-from" for
        /// that chip alone rather than breaking the overview.
        /// </summary>
        private void LoadCreatedFrom(int C_Order_ID, SalesOrderOverviewData d)
        {
            // Each origin is read under its OWN guard and its own statement.
            //
            // They used to share one SELECT, which meant a schema missing any ONE
            // of the module columns lost them ALL: the statement failed, the catch
            // swallowed it, and an order raised from a project or a quotation
            // reported no origin at all — the strip then called it "Manual". The
            // columns are optional precisely because not every deployment carries
            // them, so one absent column must only ever cost its own chip.
            LoadQuotationOrigin(C_Order_ID, d);
            LoadOpportunityOrigin(C_Order_ID, d);
            LoadProjectOrigin(C_Order_ID, d);
            LoadContractOrigin(C_Order_ID, d);
            LoadBlanketOrigin(C_Order_ID, d);
        }

        /// <summary>
        /// Remembers whether the header / line quotation lookups are usable against
        /// this schema, so a database that genuinely has neither column reports its
        /// error once rather than on every order the panel opens.
        /// Null = not tried yet, false = the statement failed, true = it ran.
        /// </summary>
        private static bool? _quotationLookupUsable;
        private static bool? _quotationLineLookupUsable;

        /// <summary>
        /// The quotation this order was raised from — C_Order.C_Order_Quotation,
        /// falling back to the same reference on the LINES.
        ///
        /// The statement is ATTEMPTED rather than gated on ColumnExists. The
        /// dictionary guard was the single point at which this feature failed: a
        /// deployment whose AD_Column has no row for the column — or whose AD_Table
        /// carries more than one row named C_Order, which makes the guard's scalar
        /// sub-select RAISE instead of answer — reported "no such column" and the
        /// chip never appeared, however good the data was. Running the query and
        /// letting a genuinely missing column throw once is both more accurate and
        /// cheaper than asking the dictionary to describe the schema. This is what
        /// VAS_092 learnt on its own blanket chip, and the reason an order raised
        /// from a quotation reported no origin at all and the strip called it
        /// "Manual".
        ///
        /// COALESCE, not NVL: the statement has to read the same on Oracle and
        /// PostgreSQL.
        /// </summary>
        /// <param name="C_Order_ID">Selected sales order id.</param>
        /// <param name="d">Overview being populated.</param>
        private void LoadQuotationOrigin(int C_Order_ID, SalesOrderOverviewData d)
        {
            // The header reference first — it names the quotation outright.
            if (_quotationLookupUsable != false)
            {
                try
                {
                    string sql = @"SELECT q.C_Order_ID AS QuotationId,
                                          q.DocumentNo AS QuotationNo
                                     FROM C_Order o
                                    INNER JOIN C_Order q
                                            ON (q.C_Order_ID = o.C_Order_Quotation)
                                    WHERE o.C_Order_ID = @C_Order_ID
                                      AND COALESCE(q.IsActive, 'Y') = 'Y'";
                    DataSet ds = DB.ExecuteDataset(sql, OrderParam(C_Order_ID), null);
                    _quotationLookupUsable = true;
                    if (ds != null && ds.Tables.Count > 0 && ds.Tables[0].Rows.Count > 0)
                    {
                        DataRow r = ds.Tables[0].Rows[0];
                        d.QuotationId = Util.GetValueOfInt(r["QuotationId"]);
                        d.QuotationNo = Util.GetValueOfString(r["QuotationNo"]);
                        return;
                    }
                }
                catch (Exception ex)
                {
                    // Almost certainly "no such column" on a schema without the
                    // quotation module. Recorded so the next order skips the attempt.
                    _quotationLookupUsable = false;
                    _log.Severe("LoadQuotationOrigin/header (C_Order_ID=" + C_Order_ID + "): " + ex.Message);
                }
            }

            LoadQuotationOriginFromLines(C_Order_ID, d);
        }

        /// <summary>
        /// The quotation this order was raised from, resolved through its LINES.
        ///
        /// This is the fallback that makes the Quotation chip appear for orders the
        /// header column does not describe, and it is not a rare case: the two
        /// records of the link are written under DIFFERENT conditions by the same
        /// copy. CopyOrder stamps the header only where C_Order carries the column
        /// (Get_ColumnIndex("C_Order_Quotation") > 0), and stamps the LINE
        /// separately — C_OrderLine.C_Order_Quotation, holding the quotation's own
        /// C_Order_ID, alongside C_Quotation_Line_ID which holds the quotation LINE
        /// it came from. An order copied on a schema carrying the line columns but
        /// not the header one records its quotation on the lines ONLY, and a panel
        /// reading just the header found nothing to show.
        ///
        /// Both line columns are tried, the direct order reference first: it needs
        /// no second hop, and it is the one CopyOrder writes last. Ordered by id so
        /// the choice is stable between loads.
        /// </summary>
        /// <param name="C_Order_ID">Selected sales order id.</param>
        /// <param name="d">Overview being populated.</param>
        private void LoadQuotationOriginFromLines(int C_Order_ID, SalesOrderOverviewData d)
        {
            if (_quotationLineLookupUsable == false) return;

            // C_OrderLine.C_Order_Quotation -> the quotation order itself.
            if (TryQuotationFromLines(C_Order_ID, d,
                    @"SELECT q.C_Order_ID AS QuotationId,
                             MAX(q.DocumentNo) AS QuotationNo
                        FROM C_OrderLine ol
                       INNER JOIN C_Order q ON (q.C_Order_ID = ol.C_Order_Quotation)
                       WHERE ol.C_Order_ID = @C_Order_ID
                         AND COALESCE(ol.IsActive, 'Y') = 'Y'
                         AND COALESCE(q.IsActive, 'Y')  = 'Y'
                         AND q.C_Order_ID <> ol.C_Order_ID
                       GROUP BY q.C_Order_ID
                       ORDER BY q.C_Order_ID", "C_Order_Quotation"))
                return;

            // C_OrderLine.C_Quotation_Line_ID -> the quotation's LINE -> its order.
            TryQuotationFromLines(C_Order_ID, d,
                @"SELECT q.C_Order_ID AS QuotationId,
                         MAX(q.DocumentNo) AS QuotationNo
                    FROM C_OrderLine ol
                   INNER JOIN C_OrderLine ql ON (ql.C_OrderLine_ID = ol.C_Quotation_Line_ID)
                   INNER JOIN C_Order q      ON (q.C_Order_ID      = ql.C_Order_ID)
                   WHERE ol.C_Order_ID = @C_Order_ID
                     AND COALESCE(ol.IsActive, 'Y') = 'Y'
                     AND COALESCE(q.IsActive, 'Y')  = 'Y'
                     AND q.C_Order_ID <> ol.C_Order_ID
                   GROUP BY q.C_Order_ID
                   ORDER BY q.C_Order_ID", "C_Quotation_Line_ID");
        }

        /// <summary>
        /// Runs one line-level quotation lookup and fills the chip from its first
        /// row. Returns true when it found one, so the caller stops.
        ///
        /// A failure marks the whole line-level route unusable: both statements rest
        /// on line columns from the same module, so if one is absent the other is
        /// too, and there is nothing to be gained by asking again on the next order.
        /// </summary>
        /// <param name="C_Order_ID">Selected sales order id.</param>
        /// <param name="d">Overview being populated.</param>
        /// <param name="sql">The query. Must select QuotationId + QuotationNo and
        /// bind @C_Order_ID exactly once.</param>
        /// <param name="what">The column being read, for the log line only.</param>
        private bool TryQuotationFromLines(int C_Order_ID, SalesOrderOverviewData d,
                                           string sql, string what)
        {
            try
            {
                DataSet ds = DB.ExecuteDataset(sql, OrderParam(C_Order_ID), null);
                _quotationLineLookupUsable = true;
                if (ds == null || ds.Tables.Count == 0 || ds.Tables[0].Rows.Count == 0) return false;

                DataRow r = ds.Tables[0].Rows[0];
                d.QuotationId = Util.GetValueOfInt(r["QuotationId"]);
                d.QuotationNo = Util.GetValueOfString(r["QuotationNo"]);
                return d.QuotationId > 0;
            }
            catch (Exception ex)
            {
                _quotationLineLookupUsable = false;
                _log.Severe("LoadQuotationOrigin/lines " + what +
                            " (C_Order_ID=" + C_Order_ID + "): " + ex.Message);
                return false;
            }
        }

        /// <summary>The opportunity this order was won from
        /// (C_Order.VAS_Opportunity_ID).</summary>
        private void LoadOpportunityOrigin(int C_Order_ID, SalesOrderOverviewData d)
        {
            if (!ColumnExists("C_Order", "VAS_Opportunity_ID")) return;
            try
            {
                string sql = @"SELECT opp.VAS_Opportunity_ID AS OpportunityId, opp.Name AS OpportunityName
                                 FROM C_Order o
                                INNER JOIN VAS_Opportunity opp
                                        ON (opp.VAS_Opportunity_ID = o.VAS_Opportunity_ID
                                            AND opp.IsActive = 'Y')
                                WHERE o.C_Order_ID = @C_Order_ID";
                DataSet ds = DB.ExecuteDataset(sql, OrderParam(C_Order_ID), null);
                if (ds == null || ds.Tables.Count == 0 || ds.Tables[0].Rows.Count == 0) return;

                DataRow r = ds.Tables[0].Rows[0];
                d.OpportunityId   = Util.GetValueOfInt(r["OpportunityId"]);
                d.OpportunityName = Util.GetValueOfString(r["OpportunityName"]);
            }
            catch (Exception ex)
            {
                _log.Severe("LoadOpportunityOrigin (C_Order_ID=" + C_Order_ID + "): " + ex.Message);
            }
        }

        /// <summary>
        /// The project this order was raised for — C_Order.C_Project_ID, else
        /// C_Order.C_ProjectRef_ID.
        ///
        /// Both columns name a C_Project. C_ProjectRef_ID is the reference an order
        /// is keyed against on screens that carry it instead of the dimension
        /// column, and the platform treats the two as the same thing: its
        /// revenue-recognition runs post C_ProjectRef_ID straight into a journal
        /// line's C_Project_ID. Reading only the first meant an order raised
        /// against a project reference reported no origin at all, and the Created
        /// From strip called it "Manual".
        ///
        /// C_Project_ID wins where the record has one; the reference answers for
        /// the rest. Each column is guarded on its own, so the reference is read on
        /// a schema without the dimension column and vice versa, and a schema with
        /// neither contributes no chip exactly as before.
        ///
        /// The project's NUMBER (C_Project.Value) travels with its name: the strip
        /// names documents by their identifier, and the name is what the chip's
        /// tooltip carries.
        /// </summary>
        private void LoadProjectOrigin(int C_Order_ID, SalesOrderOverviewData d)
        {
            bool hasProject = ColumnExists("C_Order", "C_Project_ID");
            bool hasRef     = ColumnExists("C_Order", "C_ProjectRef_ID");
            if (!hasProject && !hasRef) return;

            // NULLIF keeps a stored 0 from being taken for a project id, so the
            // reference is still reached on an order whose dimension column is
            // present but empty — which is every order keyed the reference way.
            string idExpr;
            if (hasProject && hasRef)
                idExpr = "COALESCE(NULLIF(o.C_Project_ID, 0), NULLIF(o.C_ProjectRef_ID, 0))";
            else if (hasProject)
                idExpr = "NULLIF(o.C_Project_ID, 0)";
            else
                idExpr = "NULLIF(o.C_ProjectRef_ID, 0)";

            try
            {
                string sql = @"SELECT p.C_Project_ID AS ProjectId,
                                      p.Value        AS ProjectNo,
                                      p.Name         AS ProjectName
                                 FROM C_Order o
                                INNER JOIN C_Project p
                                        ON (p.C_Project_ID = " + idExpr + @" AND p.IsActive = 'Y')
                                WHERE o.C_Order_ID = @C_Order_ID";
                DataSet ds = DB.ExecuteDataset(sql, OrderParam(C_Order_ID), null);
                if (ds == null || ds.Tables.Count == 0 || ds.Tables[0].Rows.Count == 0) return;

                DataRow r = ds.Tables[0].Rows[0];
                d.ProjectId   = Util.GetValueOfInt(r["ProjectId"]);
                d.ProjectNo   = Util.GetValueOfString(r["ProjectNo"]);
                d.ProjectName = Util.GetValueOfString(r["ProjectName"]);
            }
            catch (Exception ex)
            {
                _log.Severe("LoadProjectOrigin (C_Order_ID=" + C_Order_ID + "): " + ex.Message);
            }
        }

        /// <summary>
        /// The contract this order references.
        ///
        /// Two links are possible and both are read, header first:
        ///   * C_Order.VAS_ContractMaster_ID — the contract master the order was
        ///     raised against, the same link the Purchase Order overview's Contract
        ///     chip uses (C_Order serves both sides, so the column is shared);
        ///   * C_OrderLine.C_Contract_ID — the service contract tied to one of the
        ///     order's lines, which is what the panel's own Contract column shows.
        ///
        /// Both are module columns, so each is dictionary-guarded and read in its
        /// own statement. An order referencing neither reports no contract and the
        /// strip falls through to its next origin.
        /// </summary>
        private void LoadContractOrigin(int C_Order_ID, SalesOrderOverviewData d)
        {
            // ----- The contract master on the header -----
            if (ColumnExists("C_Order", "VAS_ContractMaster_ID") &&
                ColumnExists("VAS_ContractMaster", "DocumentNo"))
            {
                try
                {
                    string sql = @"SELECT cm.VAS_ContractMaster_ID AS ContractId,
                                          cm.DocumentNo            AS ContractNo
                                     FROM C_Order o
                                    INNER JOIN VAS_ContractMaster cm
                                            ON (cm.VAS_ContractMaster_ID = o.VAS_ContractMaster_ID)
                                    WHERE o.C_Order_ID = @C_Order_ID";
                    DataSet ds = DB.ExecuteDataset(sql, OrderParam(C_Order_ID), null);
                    if (ds != null && ds.Tables.Count > 0 && ds.Tables[0].Rows.Count > 0)
                    {
                        DataRow r = ds.Tables[0].Rows[0];
                        d.ContractId    = Util.GetValueOfInt(r["ContractId"]);
                        d.ContractNo    = Util.GetValueOfString(r["ContractNo"]);
                        d.ContractTable = "VAS_ContractMaster";
                        return;
                    }
                }
                catch (Exception ex)
                {
                    _log.Severe("LoadContractOrigin/master (C_Order_ID=" + C_Order_ID + "): " + ex.Message);
                }
            }

            // ----- Else the service contract behind one of the lines -----
            if (!ColumnExists("C_OrderLine", "C_Contract_ID")) return;
            try
            {
                string sql = @"SELECT DISTINCT c.C_Contract_ID AS ContractId,
                                               c.DocumentNo    AS ContractNo
                                 FROM C_OrderLine ol
                                INNER JOIN C_Contract c
                                        ON (c.C_Contract_ID = ol.C_Contract_ID)
                                WHERE ol.C_Order_ID = @C_Order_ID
                                  AND ol.IsActive   = 'Y'
                                ORDER BY c.DocumentNo";
                DataSet ds = DB.ExecuteDataset(sql, OrderParam(C_Order_ID), null);
                if (ds == null || ds.Tables.Count == 0 || ds.Tables[0].Rows.Count == 0) return;

                DataRow r = ds.Tables[0].Rows[0];
                d.ContractId    = Util.GetValueOfInt(r["ContractId"]);
                d.ContractNo    = Util.GetValueOfString(r["ContractNo"]);
                d.ContractTable = "C_Contract";
                // Several lines can carry different contracts; the first is named
                // and the rest counted on the chip.
                d.ContractCount = ds.Tables[0].Rows.Count;
            }
            catch (Exception ex)
            {
                _log.Severe("LoadContractOrigin/line (C_Order_ID=" + C_Order_ID + "): " + ex.Message);
            }
        }

        /// <summary>
        /// Remembers whether the header / line blanket lookups are usable against
        /// this schema, so a database that genuinely has neither column reports its
        /// error once rather than on every order the panel opens.
        /// </summary>
        private static bool? _blanketLookupUsable;
        private static bool? _blanketLineLookupUsable;

        /// <summary>
        /// The blanket sales order this order was released against —
        /// C_Order.C_Order_Blanket, falling back to the link its LINES carry.
        ///
        /// The statement is ATTEMPTED rather than gated on ColumnExists, for the
        /// reason set out on <see cref="LoadQuotationOrigin"/>: the dictionary guard
        /// was itself the thing that failed, and an order released from a blanket
        /// showed no origin at all while the strip called it "Manual".
        ///
        /// IsBlanketTrx on the parent is deliberately NOT required: the release
        /// order's own reference is the part of the link the platform writes, and
        /// demanding a second optional flag only adds another way for the statement
        /// to fail. Follows VAS_092.
        /// </summary>
        /// <param name="C_Order_ID">Selected sales order id.</param>
        /// <param name="d">Overview being populated.</param>
        private void LoadBlanketOrigin(int C_Order_ID, SalesOrderOverviewData d)
        {
            // The header reference first — it names the blanket outright.
            if (_blanketLookupUsable != false)
            {
                try
                {
                    // COALESCE, not NVL: this statement has to read the same on
                    // Oracle and PostgreSQL.
                    string sql = @"SELECT bo.C_Order_ID AS BlanketOrderId,
                                          bo.DocumentNo AS BlanketOrderNo
                                     FROM C_Order o
                                    INNER JOIN C_Order bo
                                            ON (bo.C_Order_ID = o.C_Order_Blanket)
                                    WHERE o.C_Order_ID = @C_Order_ID
                                      AND COALESCE(bo.IsActive, 'Y') = 'Y'";
                    DataSet ds = DB.ExecuteDataset(sql, OrderParam(C_Order_ID), null);
                    _blanketLookupUsable = true;
                    if (ds != null && ds.Tables.Count > 0 && ds.Tables[0].Rows.Count > 0)
                    {
                        DataRow r = ds.Tables[0].Rows[0];
                        d.BlanketOrderId = Util.GetValueOfInt(r["BlanketOrderId"]);
                        d.BlanketOrderNo = Util.GetValueOfString(r["BlanketOrderNo"]);
                        return;
                    }
                }
                catch (Exception ex)
                {
                    _blanketLookupUsable = false;
                    _log.Severe("LoadBlanketOrigin/header (C_Order_ID=" + C_Order_ID + "): " + ex.Message);
                }
            }

            LoadBlanketOriginFromLines(C_Order_ID, d);
        }

        /// <summary>
        /// The blanket this order was released against, resolved through its LINES
        /// (C_OrderLine.C_OrderLine_Blanket_ID -> the blanket's own order line ->
        /// that line's order).
        ///
        /// This is the fallback that makes the Blanket Order chip appear for release
        /// orders the header column does not describe, and it is not a rare case:
        /// the two records of the link are written by DIFFERENT code. The header
        /// column C_Order.C_Order_Blanket is stamped only by the
        /// CreateReleaseDocFromBO process, which sets it explicitly after copying,
        /// whereas the LINE reference is written by MOrder.CopyFrom for ANY document
        /// whose type IsReleaseDocument(). A release order raised through any other
        /// path therefore records its blanket on the lines ONLY.
        ///
        /// The purchase-side panel reached exactly this conclusion
        /// (VAS_092.LoadBlanketOriginFromLines); this is its sales-side mirror, and
        /// C_OrderLine is the same table on both sides.
        /// </summary>
        /// <param name="C_Order_ID">Selected sales order id.</param>
        /// <param name="d">Overview being populated.</param>
        private void LoadBlanketOriginFromLines(int C_Order_ID, SalesOrderOverviewData d)
        {
            if (_blanketLineLookupUsable == false) return;

            try
            {
                string sql = @"SELECT bo.C_Order_ID AS BlanketOrderId,
                                      MAX(bo.DocumentNo) AS BlanketOrderNo
                                 FROM C_OrderLine ol
                                INNER JOIN C_OrderLine bol
                                        ON (bol.C_OrderLine_ID = ol.C_OrderLine_Blanket_ID)
                                INNER JOIN C_Order bo
                                        ON (bo.C_Order_ID = bol.C_Order_ID)
                                WHERE ol.C_Order_ID = @C_Order_ID
                                  AND COALESCE(ol.IsActive, 'Y') = 'Y'
                                  AND COALESCE(bo.IsActive, 'Y') = 'Y'
                                  AND bo.C_Order_ID <> ol.C_Order_ID
                                GROUP BY bo.C_Order_ID
                                ORDER BY bo.C_Order_ID";
                DataSet ds = DB.ExecuteDataset(sql, OrderParam(C_Order_ID), null);
                _blanketLineLookupUsable = true;
                if (ds == null || ds.Tables.Count == 0 || ds.Tables[0].Rows.Count == 0) return;

                DataRow r = ds.Tables[0].Rows[0];
                d.BlanketOrderId = Util.GetValueOfInt(r["BlanketOrderId"]);
                d.BlanketOrderNo = Util.GetValueOfString(r["BlanketOrderNo"]);
            }
            catch (Exception ex)
            {
                // A schema without C_OrderLine_Blanket_ID simply has no line-level
                // link to read. Recorded so the next order skips the attempt.
                _blanketLineLookupUsable = false;
                _log.Severe("LoadBlanketOrigin/lines (C_Order_ID=" + C_Order_ID + "): " + ex.Message);
            }
        }

        /// <summary>
        /// Loads the line change history (C_OrderLineHistory) for every line of the
        /// order — the prior versions the platform snapshots when a completed order
        /// line is re-activated and edited — newest change first per line.
        ///
        /// Filtered on the history row's OWN C_Order_ID with a LEFT JOIN to the
        /// current line, so a line that was later removed still reports its
        /// snapshots (falling back to the snapshot's own Line sequence).
        ///
        /// The panel draws a version beneath its line using the same columns as the
        /// line itself, so the snapshot's entered quantity and unit price are what
        /// it reads. Both are on the SELECTED unit where the snapshot carries it
        /// (QtyEntered / PriceEntered), matching the line above. Optional columns
        /// are dictionary-guarded: a schema whose history table omits one simply
        /// reports nothing for it. Ported from VAS_092, with COALESCE in place of
        /// NVL so the statement runs on both databases.
        /// </summary>
        /// <param name="C_Order_ID">Selected sales order id.</param>
        /// <returns>History rows, ordered by line then newest change; never null.</returns>
        private List<LineHistoryData> LoadHistory(int C_Order_ID)
        {
            List<LineHistoryData> history = new List<LineHistoryData>();
            try
            {
                string qtyEnteredExpr = ColumnExists("C_OrderLineHistory", "QtyEntered")
                    ? "COALESCE(olh.QtyEntered, olh.QtyOrdered)" : "olh.QtyOrdered";
                string priceEnteredExpr = ColumnExists("C_OrderLineHistory", "PriceEntered")
                    ? "COALESCE(olh.PriceEntered, olh.PriceActual)" : "olh.PriceActual";
                string discountExpr = ColumnExists("C_OrderLineHistory", "Discount")
                    ? "COALESCE(olh.Discount, 0)" : "0";

                string sql = @"SELECT olh.C_OrderLine_ID,
                                      COALESCE(ol.Line, olh.Line) AS LineNo,
                                      olh.Updated      AS ChangedOn,
                                      uu.Name          AS UpdatedByName,
                                      " + qtyEnteredExpr   + @" AS QtyEntered,
                                      " + priceEnteredExpr + @" AS PriceEntered,
                                      " + discountExpr     + @" AS Discount,
                                      COALESCE(olh.LineNetAmt, 0) AS LineNetAmt,
                                      olh.Description  AS LineDescription,
                                      uom.Name         AS UOMName,
                                      COALESCE(uom.StdPrecision, 0) AS UOMPrecision
                                 FROM C_OrderLineHistory olh
                                INNER JOIN C_Order o           ON (o.C_Order_ID = olh.C_Order_ID)
                                 LEFT OUTER JOIN C_OrderLine ol ON (ol.C_OrderLine_ID = olh.C_OrderLine_ID)
                                 LEFT OUTER JOIN C_UOM uom      ON (uom.C_UOM_ID = olh.C_UOM_ID)
                                 LEFT OUTER JOIN AD_User uu     ON (uu.AD_User_ID = olh.UpdatedBy)
                                WHERE olh.C_Order_ID = @C_Order_ID
                                ORDER BY COALESCE(ol.Line, olh.Line), olh.Updated DESC";
                DataSet ds = DB.ExecuteDataset(sql, OrderParam(C_Order_ID), null);
                if (ds == null || ds.Tables.Count == 0) return history;

                foreach (DataRow r in ds.Tables[0].Rows)
                {
                    LineHistoryData h = new LineHistoryData();
                    h.C_OrderLine_ID = Util.GetValueOfInt(r["C_OrderLine_ID"]);
                    h.LineNo         = Util.GetValueOfInt(r["LineNo"]);
                    h.ChangedOn      = Stamp(r["ChangedOn"]);
                    h.UpdatedByName  = Util.GetValueOfString(r["UpdatedByName"]);
                    h.QtyEntered     = Util.GetValueOfDecimal(r["QtyEntered"]);
                    h.PriceEntered   = Util.GetValueOfDecimal(r["PriceEntered"]);
                    h.Discount       = Util.GetValueOfDecimal(r["Discount"]);
                    h.LineNetAmt     = Util.GetValueOfDecimal(r["LineNetAmt"]);
                    h.Description    = Util.GetValueOfString(r["LineDescription"]);
                    h.UOMName        = Util.GetValueOfString(r["UOMName"]);
                    h.UOMPrecision   = Util.GetValueOfInt(r["UOMPrecision"]);
                    history.Add(h);
                }
            }
            catch (Exception ex)
            {
                // A schema without C_OrderLineHistory reaches here; the panel then
                // shows no history button on any line.
                _log.Severe("LoadHistory (C_Order_ID=" + C_Order_ID + "): " + ex.Message);
            }
            return history;
        }

        /// <summary>
        /// Loads C_OrderLine rows for the order with product / charge metadata,
        /// UOM symbol, the line-level contract flag (C_OrderLine.IsContract) and
        /// any created contract (C_OrderLine.C_Contract_ID -> C_Contract).
        /// </summary>
        private List<SalesOrderLineData> LoadLines(int C_Order_ID)
        {
            List<SalesOrderLineData> lines = new List<SalesOrderLineData>();
            try
            {
                string sql = @"SELECT
                                  ol.C_OrderLine_ID,
                                  ol.Line,
                                  COALESCE(ol.QtyOrdered, 0)   AS QtyOrdered,
                                  COALESCE(ol.QtyEntered, 0)   AS QtyEntered,
                                  COALESCE(ol.QtyDelivered, 0) AS QtyDelivered,
                                  COALESCE(ol.QtyInvoiced, 0)  AS QtyInvoiced,
                                  COALESCE(ol.PriceActual, 0)  AS PriceActual,
                                  COALESCE(ol.PriceEntered, 0) AS PriceEntered,
                                  COALESCE(ol.PriceList, 0)    AS PriceList,
                                  COALESCE(ol.Discount, 0)     AS Discount,
                                  COALESCE(ol.LineNetAmt, 0)   AS LineNetAmt,
                                  ol.Description               AS LineDescription,
                                  ol.M_Product_ID,
                                  ol.C_Charge_ID,
                                  ol.IsContract,
                                  ol.IsDropShip,
                                  ol.C_Contract_ID,
                                  p.Value        AS ProductValue,
                                  p.Name         AS ProductName,
                                  p.ProductType  AS ProductType,
                                  ch.Name        AS ChargeName,
                                  uom.UOMSymbol  AS UOMSymbol,
                                  uom.Name       AS UOMName,
                                  COALESCE(uom.StdPrecision, 0) AS UOMPrecision,
                                  asi.Description AS AttributeSetInstance,
                                  con.DocumentNo AS ContractNo
                                FROM C_OrderLine ol
                                LEFT OUTER JOIN M_Product  p   ON (ol.M_Product_ID = p.M_Product_ID)
                                LEFT OUTER JOIN C_Charge   ch  ON (ol.C_Charge_ID  = ch.C_Charge_ID)
                                LEFT OUTER JOIN C_UOM      uom ON (ol.C_UOM_ID     = uom.C_UOM_ID)
                                -- Only a REAL instance is joined: id 0 is the
                                -- dictionary's no-attributes row, whose
                                -- description is a bare double dash that would
                                -- otherwise print against every line carrying no
                                -- attributes at all.
                                LEFT OUTER JOIN M_AttributeSetInstance asi
                                       ON (asi.M_AttributeSetInstance_ID = ol.M_AttributeSetInstance_ID
                                           AND ol.M_AttributeSetInstance_ID > 0)
                                LEFT OUTER JOIN C_Contract con ON (ol.C_Contract_ID = con.C_Contract_ID)
                                WHERE ol.C_Order_ID = @C_Order_ID
                                  AND ol.IsActive   = 'Y'
                                ORDER BY ol.Line";
                DataSet ds = DB.ExecuteDataset(sql, OrderParam(C_Order_ID), null);
                if (ds == null || ds.Tables.Count == 0) return lines;

                foreach (DataRow r in ds.Tables[0].Rows)
                {
                    SalesOrderLineData ln = new SalesOrderLineData();
                    ln.C_OrderLine_ID = Util.GetValueOfInt(r["C_OrderLine_ID"]);
                    ln.Line           = Util.GetValueOfInt(r["Line"]);
                    // The row is reported in the line's SELECTED unit — the one it
                    // is labelled with (C_UOM_ID). C_OrderLine keeps two scales:
                    // QtyEntered / PriceEntered are in that unit, while QtyOrdered,
                    // QtyDelivered and PriceActual are in the product's BASE unit.
                    // The panel showed the base figures against the selected unit's
                    // name, so a line sold as 2 BOX of an EA-held product read as 24
                    // at the per-EA price.
                    //
                    // UomRatio is how many BASE units one SELECTED unit is (12, for
                    // a 12-EA box), derived from the line's own two quantities so it
                    // is exactly the ratio the line was saved with. Everything the
                    // database holds in the base unit is brought onto the selected
                    // scale with it.
                    decimal baseQty = Util.GetValueOfDecimal(r["QtyOrdered"]);
                    decimal entered = Util.GetValueOfDecimal(r["QtyEntered"]);
                    if (entered == 0) entered = baseQty;
                    ln.UomRatio       = (entered != 0 && baseQty != 0) ? baseQty / entered : 1;

                    ln.QtyOrdered     = entered;
                    ln.QtyDelivered   = Util.GetValueOfDecimal(r["QtyDelivered"]);
                    if (ln.UomRatio != 0) ln.QtyDelivered = ln.QtyDelivered / ln.UomRatio;
                    ln.QtyInvoiced    = Util.GetValueOfDecimal(r["QtyInvoiced"]);
                    if (ln.UomRatio != 0) ln.QtyInvoiced = ln.QtyInvoiced / ln.UomRatio;

                    // Price per SELECTED unit. PriceEntered is stored in it; a line
                    // saved without one falls back to the base price scaled by the
                    // ratio, which leaves the line's own total untouched:
                    // QtyEntered x (PriceActual x ratio) = QtyOrdered x PriceActual.
                    ln.PriceActual    = Util.GetValueOfDecimal(r["PriceEntered"]);
                    if (ln.PriceActual == 0)
                        ln.PriceActual = Util.GetValueOfDecimal(r["PriceActual"]) * ln.UomRatio;
                    // The list price the line was priced off (C_OrderLine.PriceList),
                    // which the panel reveals on the unit price's hover tooltip.
                    //
                    // Taken as it stands, NOT scaled by the ratio above: PriceList
                    // travels with PriceEntered, on the line's SELECTED unit. The
                    // platform reads it out of M_ProductPrice for the line's own
                    // C_UOM_ID, and its UOM callout multiplies price entered and
                    // price list by the same conversion rate (MOrderLineModel
                    // .GetPricesOnUomChange), so the pair is always on one scale —
                    // which is the scale the price beside it is shown on. 0 when the
                    // line carries no list price at all.
                    ln.PriceList      = Util.GetValueOfDecimal(r["PriceList"]);
                    ln.Discount       = Util.GetValueOfDecimal(r["Discount"]);
                    ln.LineNetAmt     = Util.GetValueOfDecimal(r["LineNetAmt"]);
                    ln.Description    = Util.GetValueOfString(r["LineDescription"]);
                    ln.M_Product_ID   = Util.GetValueOfInt(r["M_Product_ID"]);
                    ln.C_Charge_ID    = Util.GetValueOfInt(r["C_Charge_ID"]);
                    ln.IsContractFlag = Util.GetValueOfString(r["IsContract"]) == "Y";
                    // Whether this line ships straight to the customer rather than
                    // out of our own warehouse (C_OrderLine.IsDropShip). The panel
                    // states it on every line, Yes or No: which lines are drop
                    // shipped changes who is expected to move the goods, and it was
                    // nowhere on the row.
                    ln.IsDropShip     = Util.GetValueOfString(r["IsDropShip"]) == "Y";
                    ln.C_Contract_ID  = Util.GetValueOfInt(r["C_Contract_ID"]);
                    ln.ContractNo     = Util.GetValueOfString(r["ContractNo"]);
                    ln.ProductValue   = Util.GetValueOfString(r["ProductValue"]);
                    ln.ProductName    = Util.GetValueOfString(r["ProductName"]);
                    ln.ProductType    = Util.GetValueOfString(r["ProductType"]);
                    ln.ChargeName     = Util.GetValueOfString(r["ChargeName"]);
                    ln.UOMSymbol      = Util.GetValueOfString(r["UOMSymbol"]);
                    ln.UOMName        = Util.GetValueOfString(r["UOMName"]);
                    ln.UOMPrecision   = Util.GetValueOfInt(r["UOMPrecision"]);
                    ln.AttributeSetInstance = Util.GetValueOfString(r["AttributeSetInstance"]);

                    if (string.IsNullOrEmpty(ln.ProductName) && !string.IsNullOrEmpty(ln.ChargeName))
                        ln.ProductName = ln.ChargeName;

                    // Line family: product (stockable) / service / charge.
                    if (ln.C_Charge_ID > 0)
                        ln.LineType = "charge";
                    else if (ln.ProductType == "S")
                        ln.LineType = "service";
                    else if (ln.ProductType == "I")
                        ln.LineType = "product";
                    else
                        ln.LineType = "other";

                    // Delivered state (stockable lines only surface a bar).
                    if (ln.QtyOrdered > 0 && ln.QtyDelivered >= ln.QtyOrdered)
                        ln.DeliveredState = "full";
                    else if (ln.QtyDelivered > 0)
                        ln.DeliveredState = "part";
                    else
                        ln.DeliveredState = "none";

                    lines.Add(ln);
                }
            }
            catch (Exception ex)
            {
                _log.Severe("LoadLines (C_Order_ID=" + C_Order_ID + "): " + ex.Message);
            }
            return lines;
        }

        /// <summary>
        /// Loads delivery readiness for the pending stockable lines only
        /// (ProductType = 'I'): pending-to-deliver (QtyOrdered - QtyDelivered)
        /// against on-hand stock (SUM M_Storage.QtyOnHand) in the fulfilment
        /// warehouse (line warehouse, else order warehouse). Service / charge
        /// lines are excluded. On Hand only — no available-to-promise.
        ///
        /// Every quantity here is the product's BASE unit — the unit stock is held
        /// and counted in — and the row is labelled with that unit (M_Product
        /// .C_UOM_ID), not the line's selected one. This is a stock question, and
        /// M_Storage answers it in base units: converting the pending quantity onto
        /// a line's selected unit meant a line sold by the box reported a boxed
        /// figure against warehouse stock counted in each, and the two columns the
        /// reader compares were no longer on one scale.
        /// </summary>
        private List<DeliveryReadinessData> LoadDeliveryReadiness(int C_Order_ID)
        {
            List<DeliveryReadinessData> rows = new List<DeliveryReadinessData>();
            try
            {
                string sql = @"SELECT
                                  ol.C_OrderLine_ID,
                                  p.M_Product_ID,
                                  p.Value AS ProductValue,
                                  p.Name  AS ProductName,
                                  uom.Name AS UOMName,
                                  asi.Description AS AttributeSetInstance,
                                  wh.Name AS WarehouseName,
                                  COALESCE(ol.QtyOrdered, 0)   AS QtyOrdered,
                                  COALESCE(ol.QtyDelivered, 0) AS QtyDelivered,
                                  COALESCE(ol.QtyOrdered, 0) - COALESCE(ol.QtyDelivered, 0) AS PendingQty,
                                  COALESCE(SUM(COALESCE(s.QtyOnHand, 0)), 0) AS QtyOnHand
                                FROM C_Order o
                                INNER JOIN C_OrderLine ol ON (ol.C_Order_ID = o.C_Order_ID)
                                INNER JOIN M_Product p     ON (p.M_Product_ID = ol.M_Product_ID)
                                -- The PRODUCT's own unit, not the line's: every
                                -- quantity on this row is a base-unit figure.
                                LEFT OUTER JOIN C_UOM uom  ON (uom.C_UOM_ID = p.C_UOM_ID)
                                -- A real instance only; see LoadLines.
                                LEFT OUTER JOIN M_AttributeSetInstance asi
                                       ON (asi.M_AttributeSetInstance_ID = ol.M_AttributeSetInstance_ID
                                           AND ol.M_AttributeSetInstance_ID > 0)
                                LEFT OUTER JOIN M_Warehouse wh
                                       ON (wh.M_Warehouse_ID = COALESCE(ol.M_Warehouse_ID, o.M_Warehouse_ID))
                                LEFT OUTER JOIN M_Locator loc
                                       ON (loc.M_Warehouse_ID = wh.M_Warehouse_ID AND loc.IsActive = 'Y')
                                LEFT OUTER JOIN M_Storage s
                                       ON (s.M_Locator_ID = loc.M_Locator_ID
                                           AND s.M_Product_ID = ol.M_Product_ID
                                           AND s.IsActive = 'Y')
                                WHERE o.C_Order_ID = @C_Order_ID
                                  AND o.IsActive   = 'Y'
                                  AND o.IsSOTrx    = 'Y'
                                  AND ol.IsActive  = 'Y'
                                  AND p.ProductType = 'I'
                                GROUP BY ol.C_OrderLine_ID, p.M_Product_ID, p.Value, p.Name,
                                         uom.Name, asi.Description, wh.Name,
                                         COALESCE(ol.QtyOrdered, 0),
                                         COALESCE(ol.QtyDelivered, 0)
                                ORDER BY ol.C_OrderLine_ID";
                DataSet ds = DB.ExecuteDataset(sql, OrderParam(C_Order_ID), null);
                if (ds == null || ds.Tables.Count == 0) return rows;

                foreach (DataRow r in ds.Tables[0].Rows)
                {
                    DeliveryReadinessData rd = new DeliveryReadinessData();
                    rd.C_OrderLine_ID = Util.GetValueOfInt(r["C_OrderLine_ID"]);
                    rd.ProductValue   = Util.GetValueOfString(r["ProductValue"]);
                    rd.ProductName    = Util.GetValueOfString(r["ProductName"]);
                    rd.UOMName        = Util.GetValueOfString(r["UOMName"]);
                    rd.AttributeSetInstance = Util.GetValueOfString(r["AttributeSetInstance"]);
                    rd.WarehouseName  = Util.GetValueOfString(r["WarehouseName"]);

                    // Every quantity is reported exactly as the database holds it —
                    // the product's BASE unit, which is the unit the row is labelled
                    // with (M_Product.C_UOM_ID, joined above) and the unit M_Storage
                    // counts stock in. Nothing is converted onto the line's selected
                    // unit any more: this table sets pending against on-hand, and
                    // that comparison only means anything with both on one scale.
                    rd.PendingQty     = Util.GetValueOfDecimal(r["PendingQty"]);
                    rd.QtyOnHand      = Util.GetValueOfDecimal(r["QtyOnHand"]);
                    // The ordered / delivered pair travels with the row as well, on
                    // the same scale: the partial state below is a statement about
                    // how much of the line has gone out, and the panel names the
                    // quantities behind it.
                    rd.QtyOrdered     = Util.GetValueOfDecimal(r["QtyOrdered"]);
                    rd.QtyDelivered   = Util.GetValueOfDecimal(r["QtyDelivered"]);

                    // Delivered in full, delivered in PART, or not delivered at all —
                    // and only that last case is a question about stock.
                    //
                    // A line the warehouse has already shipped something against is
                    // reported as partially delivered whatever it still holds: the
                    // row's news is that the delivery has begun and is not finished,
                    // which "Ready to ship" and "Short by n" both hide. The stock
                    // states stay exactly as they were for a line nothing has gone
                    // out against.
                    if (rd.PendingQty <= 0)                 rd.Readiness = "ready";     // fully delivered
                    else if (rd.QtyDelivered > 0)           rd.Readiness = "partial";   // some of it has shipped
                    else if (rd.QtyOnHand >= rd.PendingQty) rd.Readiness = "instock";   // can ship now
                    else                                    rd.Readiness = "short";     // not enough on hand
                    rows.Add(rd);
                }
            }
            catch (Exception ex)
            {
                _log.Severe("LoadDeliveryReadiness (C_Order_ID=" + C_Order_ID + "): " + ex.Message);
            }
            return rows;
        }

        /// <summary>
        /// Loads delivery orders (M_InOut, IsSOTrx = 'Y') linked to the sales
        /// order, excluding reversed / voided, with a line count and total
        /// movement quantity.
        /// </summary>
        private List<DeliveryData> LoadDeliveries(int C_Order_ID)
        {
            List<DeliveryData> rows = new List<DeliveryData>();
            try
            {
                string sql = @"SELECT
                                  io.M_InOut_ID,
                                  io.DocumentNo,
                                  io.DocStatus,
                                  io.MovementDate,
                                  io.TrackingNo,
                                  io.Created,
                                  io.Updated,
                                  wh.Name AS WarehouseName,
                                  COALESCE(SUM(COALESCE(iol.MovementQty, 0)), 0) AS DeliveredQty,
                                  COUNT(iol.M_InOutLine_ID) AS LineCount,
                                  -- What the shipment was WORTH: each shipped line
                                  -- valued at the share of its order line's net
                                  -- amount that went out with it. Taken as a share
                                  -- of the line's own money rather than quantity x
                                  -- price, so the figure needs no view on which unit
                                  -- either side is held in — both quantities are the
                                  -- product's base unit, so the ratio cancels.
                                  COALESCE(SUM(CASE WHEN COALESCE(ol.QtyOrdered, 0) <> 0
                                                    THEN COALESCE(iol.MovementQty, 0)
                                                         / ol.QtyOrdered
                                                         * COALESCE(ol.LineNetAmt, 0)
                                                    ELSE 0 END), 0) AS DeliveredValue
                                FROM M_InOut io
                                LEFT OUTER JOIN M_InOutLine iol
                                       ON (iol.M_InOut_ID = io.M_InOut_ID AND iol.IsActive = 'Y')
                                LEFT OUTER JOIN C_OrderLine ol
                                       ON (ol.C_OrderLine_ID = iol.C_OrderLine_ID)
                                LEFT OUTER JOIN M_Warehouse wh ON (wh.M_Warehouse_ID = io.M_Warehouse_ID)
                                WHERE io.C_Order_ID = @C_Order_ID
                                  AND io.IsActive   = 'Y'
                                  AND io.IsSOTrx    = 'Y'
                                  AND io.DocStatus NOT IN ('RE', 'VO')
                                GROUP BY io.M_InOut_ID, io.DocumentNo, io.DocStatus, io.MovementDate,
                                         io.TrackingNo, io.Created, io.Updated, wh.Name
                                ORDER BY io.MovementDate DESC, io.DocumentNo DESC";
                DataSet ds = DB.ExecuteDataset(sql, OrderParam(C_Order_ID), null);
                if (ds == null || ds.Tables.Count == 0) return rows;

                // When each shipment COMPLETED, for the progress line's Shipped and
                // Delivered stages. Read for the whole set in one statement.
                Dictionary<int, DateTime> stamps = LoadCompletionStamps("M_InOut",
                    "SELECT dio.M_InOut_ID FROM M_InOut dio WHERE dio.C_Order_ID = "
                    + C_Order_ID + " AND dio.IsSOTrx = 'Y'");

                foreach (DataRow r in ds.Tables[0].Rows)
                {
                    DeliveryData dv = new DeliveryData();
                    dv.M_InOut_ID    = Util.GetValueOfInt(r["M_InOut_ID"]);
                    dv.DocumentNo    = Util.GetValueOfString(r["DocumentNo"]);
                    dv.DocStatus     = Util.GetValueOfString(r["DocStatus"]);
                    dv.MovementDate  = Stamp(r["MovementDate"]);
                    dv.TrackingNo    = Util.GetValueOfString(r["TrackingNo"]);
                    dv.WarehouseName = Util.GetValueOfString(r["WarehouseName"]);
                    dv.DeliveredQty  = Util.GetValueOfDecimal(r["DeliveredQty"]);
                    dv.LineCount     = Util.GetValueOfInt(r["LineCount"]);
                    // When the shipment was RAISED, for the progress line's Shipped
                    // and Delivered stages. MovementDate is a document field a user
                    // can back-date or set forward, so the stage could report a day
                    // on which nothing had yet been entered. Follows VAS_092.
                    dv.Created       = Stamp(r["Created"]);
                    dv.DeliveredValue = Util.GetValueOfDecimal(r["DeliveredValue"]);
                    // Null until the shipment is completed — the Delivered stage
                    // dates itself with this, and an open shipment has no such date.
                    dv.CompletedDate = CompletedOn(stamps, dv.M_InOut_ID, dv.DocStatus,
                                                   Stamp(r["Updated"]));
                    rows.Add(dv);
                }
            }
            catch (Exception ex)
            {
                _log.Severe("LoadDeliveries (C_Order_ID=" + C_Order_ID + "): " + ex.Message);
            }
            return rows;
        }

        /// <summary>
        /// Loads invoices (C_Invoice, IsSOTrx = 'Y') linked to the sales order,
        /// excluding reversed / voided.
        /// </summary>
        private List<InvoiceData> LoadInvoices(int C_Order_ID)
        {
            List<InvoiceData> rows = new List<InvoiceData>();
            try
            {
                string sql = @"SELECT
                                  inv.C_Invoice_ID,
                                  inv.DocumentNo,
                                  inv.DocStatus,
                                  inv.DateInvoiced,
                                  inv.Created,
                                  inv.Updated,
                                  COALESCE(inv.GrandTotal, 0) AS GrandTotal,
                                  inv.IsPaid
                                FROM C_Invoice inv
                                WHERE inv.C_Order_ID = @C_Order_ID
                                  AND inv.IsActive   = 'Y'
                                  AND inv.IsSOTrx    = 'Y'
                                  AND inv.DocStatus NOT IN ('RE', 'VO')
                                ORDER BY inv.DateInvoiced DESC, inv.DocumentNo DESC";
                DataSet ds = DB.ExecuteDataset(sql, OrderParam(C_Order_ID), null);
                if (ds == null || ds.Tables.Count == 0) return rows;

                // When each invoice COMPLETED — the Invoiced stage reports the
                // LATEST of these, and a drafted invoice contributes none.
                Dictionary<int, DateTime> stamps = LoadCompletionStamps("C_Invoice",
                    "SELECT div.C_Invoice_ID FROM C_Invoice div WHERE div.C_Order_ID = "
                    + C_Order_ID + " AND div.IsSOTrx = 'Y'");

                foreach (DataRow r in ds.Tables[0].Rows)
                {
                    InvoiceData iv = new InvoiceData();
                    iv.C_Invoice_ID = Util.GetValueOfInt(r["C_Invoice_ID"]);
                    iv.DocumentNo   = Util.GetValueOfString(r["DocumentNo"]);
                    iv.DocStatus    = Util.GetValueOfString(r["DocStatus"]);
                    iv.DateInvoiced = Stamp(r["DateInvoiced"]);
                    iv.Created      = Stamp(r["Created"]);
                    iv.GrandTotal   = Util.GetValueOfDecimal(r["GrandTotal"]);
                    iv.IsPaid       = Util.GetValueOfString(r["IsPaid"]) == "Y";
                    iv.CompletedDate = CompletedOn(stamps, iv.C_Invoice_ID, iv.DocStatus,
                                                   Stamp(r["Updated"]));
                    rows.Add(iv);
                }
            }
            catch (Exception ex)
            {
                _log.Severe("LoadInvoices (C_Order_ID=" + C_Order_ID + "): " + ex.Message);
            }
            return rows;
        }

        /// <summary>
        /// Every document raised FROM this sales order, for the panel's Documents
        /// section: the shipments that delivered it, the invoices that billed it
        /// and the customer receipts that paid those invoices.
        ///
        /// Built like the Purchase Order overview's own Documents list — one flat
        /// collection, each row carrying the table + record id the panel opens it
        /// with — so the section navigates and reads the same way on both screens.
        /// It replaces the panel's separate Deliveries and Invoices sections; the
        /// Deliveries / Invoices collections themselves stay, because the KPI strip
        /// and the progress stepper are derived from them.
        ///
        /// Each source is loaded independently and swallows its own failure, so one
        /// DB-level problem drops only its own rows.
        /// </summary>
        /// <param name="C_Order_ID">Selected sales order id.</param>
        /// <param name="deliveries">Already-loaded shipments, reused rather than re-queried.</param>
        /// <param name="invoices">Already-loaded invoices, reused rather than re-queried.</param>
        /// <returns>Documents, newest first; never null.</returns>
        private List<SalesOrderDocumentData> LoadDocuments(
            int C_Order_ID, List<DeliveryData> deliveries, List<InvoiceData> invoices)
        {
            List<SalesOrderDocumentData> docs = new List<SalesOrderDocumentData>();

            // Shipments and invoices are already in hand — the same rows the KPIs
            // are derived from, so the section can never disagree with them.
            if (deliveries != null)
            {
                foreach (DeliveryData dv in deliveries)
                {
                    docs.Add(new SalesOrderDocumentData
                    {
                        Type       = "delivery",
                        TableName  = "M_InOut",
                        RecordId   = dv.M_InOut_ID,
                        DocumentNo = dv.DocumentNo,
                        DocStatus  = dv.DocStatus,
                        DocDate    = dv.MovementDate,
                        // A shipment carries no total of its own, so the row reports
                        // what it DELIVERED: each shipped line valued at its share of
                        // the order line's net amount, in the order's currency like
                        // every other amount in this list. The column was blank for
                        // every shipment, which said nothing about a document whose
                        // whole point is the value that left the warehouse.
                        //
                        // Still null — not zero — when the shipment values to
                        // nothing at all: no line of it reaches an order line, so
                        // there is no figure to report rather than a figure of zero.
                        Amount     = dv.DeliveredValue != 0 ? (decimal?)dv.DeliveredValue : null,
                        LineCount  = dv.LineCount,
                        Extra      = dv.TrackingNo
                    });
                }
            }
            if (invoices != null)
            {
                foreach (InvoiceData iv in invoices)
                {
                    docs.Add(new SalesOrderDocumentData
                    {
                        Type       = "invoice",
                        TableName  = "C_Invoice",
                        RecordId   = iv.C_Invoice_ID,
                        DocumentNo = iv.DocumentNo,
                        DocStatus  = iv.DocStatus,
                        DocDate    = iv.DateInvoiced,
                        Amount     = iv.GrandTotal,
                        IsPaid     = iv.IsPaid
                    });
                }
            }

            LoadReceiptDocuments(C_Order_ID, docs);

            // Newest first; entries with no document date sink to the bottom.
            docs.Sort((a, b) =>
                b.DocDate.GetValueOrDefault(DateTime.MinValue)
                 .CompareTo(a.DocDate.GetValueOrDefault(DateTime.MinValue)));
            return docs;
        }

        /// <summary>
        /// Adds the customer receipts (C_Payment, IsReceipt = 'Y') allocated to
        /// this order's invoices, through C_AllocationLine -> C_Invoice. DISTINCT,
        /// so a receipt allocated across several of the order's invoices is listed
        /// once. Reversed / voided payments are excluded. The sales-side mirror of
        /// the Purchase Order overview's payment rows.
        /// </summary>
        private void LoadReceiptDocuments(int C_Order_ID, List<SalesOrderDocumentData> list)
        {
            try
            {
                string sql = @"SELECT DISTINCT p.C_Payment_ID,
                                               p.DocumentNo,
                                               p.DocStatus,
                                               p.DateTrx,
                                               p.Updated,
                                               COALESCE(p.PayAmt, 0)      AS PayAmt,
                                               COALESCE(p.DiscountAmt, 0) AS DiscountAmt
                                 FROM C_Payment p
                                INNER JOIN C_AllocationLine al ON (al.C_Payment_ID = p.C_Payment_ID)
                                INNER JOIN C_Invoice ci        ON (al.C_Invoice_ID = ci.C_Invoice_ID)
                                WHERE ci.C_Order_ID = @C_Order_ID
                                  AND p.IsActive    = 'Y'
                                  AND p.IsReceipt   = 'Y'
                                  AND p.DocStatus NOT IN ('RE', 'VO')";
                DataSet ds = DB.ExecuteDataset(sql, OrderParam(C_Order_ID), null);
                if (ds == null || ds.Tables.Count == 0) return;

                // When each receipt COMPLETED — the Paid stage reports the LATEST of
                // these, and a drafted receipt contributes none.
                Dictionary<int, DateTime> stamps = LoadCompletionStamps("C_Payment",
                    @"SELECT DISTINCT dal.C_Payment_ID FROM C_AllocationLine dal
                       INNER JOIN C_Invoice dci ON (dal.C_Invoice_ID = dci.C_Invoice_ID)
                       WHERE dci.C_Order_ID = " + C_Order_ID + " AND dal.C_Payment_ID IS NOT NULL");

                foreach (DataRow r in ds.Tables[0].Rows)
                {
                    int paymentId = Util.GetValueOfInt(r["C_Payment_ID"]);
                    string status = Util.GetValueOfString(r["DocStatus"]);
                    list.Add(new SalesOrderDocumentData
                    {
                        Type        = "receipt",
                        TableName   = "C_Payment",
                        RecordId    = paymentId,
                        DocumentNo  = Util.GetValueOfString(r["DocumentNo"]),
                        DocStatus   = status,
                        DocDate     = Stamp(r["DateTrx"]),
                        CompletedDate = CompletedOn(stamps, paymentId, status,
                                                    Stamp(r["Updated"])),
                        Amount      = Util.GetValueOfDecimal(r["PayAmt"]),
                        DiscountAmt = Util.GetValueOfDecimal(r["DiscountAmt"])
                    });
                }
            }
            catch (Exception ex)
            {
                _log.Severe("LoadReceiptDocuments (C_Order_ID=" + C_Order_ID + "): " + ex.Message);
            }
        }

        /// <summary>
        /// Builds the activity timeline by merging chat notes (CM_ChatEntry),
        /// e-mails sent against the order (MailAttachment1), deliveries (M_InOut),
        /// invoices (C_Invoice) and the order's own create / confirm milestones
        /// (C_Order), newest-first. Each source is guarded so a DB-level issue with
        /// one degrades to a partial feed.
        /// (AppointmentsInfo / R_Request are intentionally not joined here —
        /// no verified direct C_Order link exists for them.)
        /// </summary>
        private List<ActivityData> LoadActivity(int C_Order_ID, int C_BPartner_ID)
        {
            // A runaway guard, not a headline count. It used to be 15 — the same
            // number the panel PAGES at — so the feed could never exceed one page,
            // the pager never appeared, and the sixteenth event onwards was
            // unreachable rather than merely further down. 200 matches the other
            // overview panels.
            const int MAX_ENTRIES = 200;
            List<ActivityData> activity = new List<ActivityData>();

            LoadNoteActivity(C_Order_ID, activity);
            LoadEmailActivity(C_Order_ID, activity);
            // ...and the mail filed against the CUSTOMER rather than against this
            // order, which is where the platform's mail sync anchors anything it
            // matches by correspondent instead of by document.
            LoadPartnerEmailActivity(C_BPartner_ID, activity);
            LoadDeliveryActivity(C_Order_ID, activity);
            LoadInvoiceActivity(C_Order_ID, activity);
            LoadOrderMilestoneActivity(C_Order_ID, activity);
            LoadOrderChangeActivity(C_Order_ID, activity);
            // Appointments, tasks, calls and letters filed against the order.
            LoadSharedSourceActivity(C_Order_ID, activity);

            activity.Sort((a, b) =>
                b.EventTime.GetValueOrDefault(DateTime.MinValue)
                 .CompareTo(a.EventTime.GetValueOrDefault(DateTime.MinValue)));

            if (activity.Count > MAX_ENTRIES)
                activity = activity.GetRange(0, MAX_ENTRIES);
            return activity;
        }

        private void LoadNoteActivity(int C_Order_ID, List<ActivityData> list)
        {
            try
            {
                // The author resolves from CM_ChatEntry.AD_User_ID falling back to
                // CreatedBy: a note logged by the platform itself leaves AD_User_ID
                // null, and those notes appeared in the feed with no name against
                // them. Follows VAS_092.
                //
                // The table id is looked up with IN + UPPER, like the e-mail and
                // partner-mail loaders below and like VAS_100's own note loader.
                // It used to be a case-sensitive SCALAR sub-select, which failed two
                // ways: AD_Table can carry more than one row named C_Order (a
                // duplicated or differently-cased dictionary entry) and a scalar
                // sub-select returning several rows RAISES on Oracle, taking every
                // note into the catch below; and a dictionary that spells the name
                // any other way matched nothing at all. Either way the notes simply
                // vanished from the feed.
                string sql = @"SELECT ce.CharacterData, ce.Created, u.Name AS UserName
                                 FROM CM_ChatEntry ce
                                 INNER JOIN CM_Chat ch     ON (ce.CM_Chat_ID = ch.CM_Chat_ID)
                                 LEFT OUTER JOIN AD_User u
                                        ON (u.AD_User_ID = COALESCE(ce.AD_User_ID, ce.CreatedBy))
                                WHERE ch.AD_Table_ID IN
                                      (SELECT t.AD_Table_ID FROM AD_Table t
                                        WHERE UPPER(t.TableName) = 'C_ORDER')
                                  AND ch.Record_ID = @C_Order_ID
                                  AND ce.IsActive  = 'Y'";
                DataSet ds = DB.ExecuteDataset(sql, OrderParam(C_Order_ID), null);
                if (ds == null || ds.Tables.Count == 0) return;
                foreach (DataRow r in ds.Tables[0].Rows)
                {
                    list.Add(new ActivityData
                    {
                        EventType   = "Note",
                        Title       = Util.GetValueOfString(r["CharacterData"]),
                        ActorName   = Util.GetValueOfString(r["UserName"]),
                        EventTime   = Stamp(r["Created"])
                    });
                }
            }
            catch (Exception ex)
            {
                _log.Severe("LoadNoteActivity (C_Order_ID=" + C_Order_ID + "): " + ex.Message);
            }
        }

        /// <summary>
        /// Adds the e-mails sent against this order to the feed as "Email" rows.
        ///
        /// The mails are reached through MailAttachment1 by the pair that identifies
        /// the record — AD_Table_ID = C_Order and Record_ID = the order id — and
        /// each row carries who it went to (MailAddress, with the Cc / Bcc / From
        /// addresses alongside), the subject (Title), the body (TextMsg), when it
        /// was sent (Created) and who sent it (CreatedBy).
        ///
        /// The body travels with the row so the panel can reveal it on click
        /// without a second round trip. Ported from VAS_092.
        /// </summary>
        /// <param name="C_Order_ID">Owning sales order id.</param>
        /// <param name="list">Activity list being populated.</param>
        private void LoadEmailActivity(int C_Order_ID, List<ActivityData> list)
        {
            try
            {
                // Two things kept real mails off this feed, and both are here:
                //
                //   - The table id was a SCALAR sub-select. AD_Table can carry more
                //     than one row named C_Order (a differently-cased or duplicated
                //     dictionary entry), and a scalar sub-select returning more than
                //     one row RAISES on Oracle — taking the whole lookup, and with
                //     it every e-mail, into the catch below. IN + UPPER answers
                //     whichever rows there are.
                //   - AttachmentType was required to equal 'M'. That value varies
                //     between installations and some rows leave it null, so
                //     demanding 'M' hid mails that were really there. The two kinds
                //     on this table PARTITION it: a letter is 'I' and an e-mail is
                //     anything else. Reading it as not-'I' hides nothing — and it
                //     still has to be read, because the shared sources loader brings
                //     the letters in separately and dropping the test altogether
                //     would list every letter twice.
                //
                // COALESCE, not NVL: this panel's SQL runs on both databases.
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
                                        WHERE UPPER(t.TableName) = 'C_ORDER')
                                  AND ma.Record_ID = @C_Order_ID
                                  AND COALESCE(ma.IsActive, 'Y')        = 'Y'
                                  AND COALESCE(to_char(ma.AttachmentType), 'M') <> 'I'
                                ORDER BY ma.Created DESC";
                DataSet ds = DB.ExecuteDataset(sql, OrderParam(C_Order_ID), null);
                if (ds == null || ds.Tables.Count == 0) return;

                foreach (DataRow r in ds.Tables[0].Rows)
                {
                    list.Add(new ActivityData
                    {
                        EventType  = "Email",
                        // Title is the row's headline everywhere in this feed; for
                        // an e-mail that is its subject.
                        Title      = Util.GetValueOfString(r["Title"]),
                        // Mails sent as HTML store their markup in TextMsg; the
                        // panel shows a body as text, so it is flattened here.
                        Body       = MailBodyToText(Util.GetValueOfString(r["TextMsg"])),
                        MailTo     = Util.GetValueOfString(r["MailAddress"]),
                        MailCc     = Util.GetValueOfString(r["MailAddressCc"]),
                        MailBcc    = Util.GetValueOfString(r["MailAddressBcc"]),
                        MailFrom   = Util.GetValueOfString(r["MailAddressFrom"]),
                        IsMailSent = Util.GetValueOfString(r["IsMailSent"]) == "Y",
                        ActorName  = Util.GetValueOfString(r["UserName"]),
                        EventTime  = Stamp(r["Created"])
                    });
                }
            }
            catch (Exception ex)
            {
                // Non-fatal: a schema without MailAttachment1 simply shows no
                // e-mail rows, and the rest of the feed is unaffected.
                _log.Severe("LoadEmailActivity (C_Order_ID=" + C_Order_ID + "): " + ex.Message);
            }
        }

        /// <summary>
        /// The mail filed against the CUSTOMER (MailAttachment1 anchored on
        /// C_BPartner + Record_ID = C_BPartner_ID), added to the feed alongside
        /// the order's own correspondence.
        ///
        /// The platform's mail sync anchors an incoming message by WHO it is from,
        /// not by which document it concerns: with TABLEATTACH = C_BPartner it
        /// files the row against the partner (see AttachMailToBP), and only a
        /// message whose subject carries the encoded table/record marker is ever
        /// anchored to the order itself. Correspondence with a customer therefore
        /// never reached this feed, while a partner-level history panel listed it
        /// in full.
        ///
        /// These rows are NOT about this order — every order of that customer's
        /// carries the same ones — so each is flagged IsPartnerMail and the panel
        /// tags it apart rather than letting it read as this document's trail.
        ///
        /// Capped independently of the feed's own guard: a long-standing customer
        /// can carry years of correspondence, and without a cap of its own it
        /// would crowd out the order's real history once the feed is trimmed.
        /// </summary>
        /// <param name="C_BPartner_ID">Customer on the order.</param>
        /// <param name="list">Activity list being populated.</param>
        private void LoadPartnerEmailActivity(int C_BPartner_ID, List<ActivityData> list)
        {
            if (C_BPartner_ID <= 0) return;

            // The newest correspondence only. Trimmed here rather than in SQL so
            // the statement stays the same on both databases.
            const int MAX_PARTNER_MAILS = 50;

            try
            {
                // Same shape as LoadEmailActivity, anchored on the partner: IN +
                // UPPER over AD_Table (a scalar sub-select RAISES on Oracle when
                // the dictionary carries more than one row of that name), and
                // "not 'I'" for the type, so a letter is left to the shared
                // sources reader and cannot be listed twice.
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
                                        WHERE UPPER(t.TableName) = 'C_BPARTNER')
                                  AND ma.Record_ID = @C_BPartner_ID
                                  AND COALESCE(ma.IsActive, 'Y')        = 'Y'
                                  AND COALESCE(TO_CHAR(ma.AttachmentType), 'M') <> 'I'
                                ORDER BY ma.Created DESC";
                SqlParameter[] param = new SqlParameter[]
                {
                    new SqlParameter("@C_BPartner_ID", C_BPartner_ID)
                };
                DataSet ds = DB.ExecuteDataset(sql, param, null);
                if (ds == null || ds.Tables.Count == 0) return;

                int taken = 0;
                foreach (DataRow r in ds.Tables[0].Rows)
                {
                    if (taken >= MAX_PARTNER_MAILS) break;
                    taken++;

                    list.Add(new ActivityData
                    {
                        EventType     = "Email",
                        IsPartnerMail = true,
                        Title         = Util.GetValueOfString(r["Title"]),
                        // Mails sent as HTML store their markup in TextMsg; the
                        // panel shows a body as text, so it is flattened here.
                        Body          = MailBodyToText(Util.GetValueOfString(r["TextMsg"])),
                        MailTo        = Util.GetValueOfString(r["MailAddress"]),
                        MailCc        = Util.GetValueOfString(r["MailAddressCc"]),
                        MailBcc       = Util.GetValueOfString(r["MailAddressBcc"]),
                        MailFrom      = Util.GetValueOfString(r["MailAddressFrom"]),
                        IsMailSent    = Util.GetValueOfString(r["IsMailSent"]) == "Y",
                        ActorName     = Util.GetValueOfString(r["UserName"]),
                        EventTime     = Stamp(r["Created"])
                    });
                }
            }
            catch (Exception ex)
            {
                // Non-fatal, exactly as the order's own pass: the rest of the feed
                // is unaffected.
                _log.Severe("LoadPartnerEmailActivity (C_BPartner_ID=" + C_BPartner_ID + "): " + ex.Message);
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
        /// A mail sent as HTML stores its markup here, and the panel shows the
        /// body as text — so without this the reader gets tags instead of a
        /// message. Block-level markup becomes line breaks, table cells become
        /// tabs, everything else is dropped and entities are decoded last, so
        /// the browser still receives text it can safely escape: no markup is
        /// ever handed to the panel. A body with no markup is returned as it
        /// was stored. Ported from VAS_092.
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
                s = s.Replace('\u00A0', ' ');               // nbsp reads as a space

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

        private void LoadDeliveryActivity(int C_Order_ID, List<ActivityData> list)
        {
            try
            {
                string sql = @"SELECT io.DocumentNo, io.DocStatus, io.Updated, u.Name AS UserName
                                 FROM M_InOut io
                                 LEFT OUTER JOIN AD_User u ON (io.UpdatedBy = u.AD_User_ID)
                                WHERE io.C_Order_ID = @C_Order_ID
                                  AND io.IsActive   = 'Y'
                                  AND io.IsSOTrx    = 'Y'
                                  AND io.DocStatus NOT IN ('RE', 'VO')";
                DataSet ds = DB.ExecuteDataset(sql, OrderParam(C_Order_ID), null);
                if (ds == null || ds.Tables.Count == 0) return;
                foreach (DataRow r in ds.Tables[0].Rows)
                {
                    list.Add(new ActivityData
                    {
                        EventType  = "Delivery",
                        Title      = Util.GetValueOfString(r["DocumentNo"]),
                        ActorName  = Util.GetValueOfString(r["UserName"]),
                        EventTime  = Stamp(r["Updated"])
                    });
                }
            }
            catch (Exception ex)
            {
                _log.Severe("LoadDeliveryActivity (C_Order_ID=" + C_Order_ID + "): " + ex.Message);
            }
        }

        private void LoadInvoiceActivity(int C_Order_ID, List<ActivityData> list)
        {
            try
            {
                string sql = @"SELECT inv.DocumentNo, COALESCE(inv.GrandTotal, 0) AS GrandTotal,
                                      inv.Updated, u.Name AS UserName
                                 FROM C_Invoice inv
                                 LEFT OUTER JOIN AD_User u ON (inv.UpdatedBy = u.AD_User_ID)
                                WHERE inv.C_Order_ID = @C_Order_ID
                                  AND inv.IsActive   = 'Y'
                                  AND inv.IsSOTrx    = 'Y'
                                  AND inv.DocStatus NOT IN ('RE', 'VO')";
                DataSet ds = DB.ExecuteDataset(sql, OrderParam(C_Order_ID), null);
                if (ds == null || ds.Tables.Count == 0) return;
                foreach (DataRow r in ds.Tables[0].Rows)
                {
                    list.Add(new ActivityData
                    {
                        EventType  = "Invoice",
                        Title      = Util.GetValueOfString(r["DocumentNo"]),
                        Amount     = Util.GetValueOfDecimal(r["GrandTotal"]),
                        ActorName  = Util.GetValueOfString(r["UserName"]),
                        EventTime  = Stamp(r["Updated"])
                    });
                }
            }
            catch (Exception ex)
            {
                _log.Severe("LoadInvoiceActivity (C_Order_ID=" + C_Order_ID + "): " + ex.Message);
            }
        }

        private void LoadOrderMilestoneActivity(int C_Order_ID, List<ActivityData> list)
        {
            try
            {
                string sql = @"SELECT o.Created, o.Updated, o.DocStatus, o.DocumentNo,
                                      cu.Name AS CreatedByName, uu.Name AS UpdatedByName
                                 FROM C_Order o
                                 LEFT OUTER JOIN AD_User cu ON (o.CreatedBy = cu.AD_User_ID)
                                 LEFT OUTER JOIN AD_User uu ON (o.UpdatedBy = uu.AD_User_ID)
                                WHERE o.C_Order_ID = @C_Order_ID";
                DataSet ds = DB.ExecuteDataset(sql, OrderParam(C_Order_ID), null);
                if (ds == null || ds.Tables.Count == 0 || ds.Tables[0].Rows.Count == 0) return;

                DataRow r = ds.Tables[0].Rows[0];
                list.Add(new ActivityData
                {
                    EventType = "Created",
                    Title     = Util.GetValueOfString(r["DocumentNo"]),
                    ActorName = Util.GetValueOfString(r["CreatedByName"]),
                    EventTime = Stamp(r["Created"])
                });

                string docStatus = Util.GetValueOfString(r["DocStatus"]);
                if (docStatus == "CO" || docStatus == "CL")
                {
                    // "Completed", not "Updated": the row is the document reaching
                    // that state, and "Updated" now belongs to the field-by-field
                    // edits below, which are actual updates. Dated by the workflow's
                    // own DocComplete stamp where there is one, since o.Updated is
                    // merely the last time anything on the record changed.
                    list.Add(new ActivityData
                    {
                        EventType = "Completed",
                        Title     = Util.GetValueOfString(r["DocumentNo"]),
                        ActorName = Util.GetValueOfString(r["UpdatedByName"]),
                        EventTime = GetOrderCompletedDate(C_Order_ID)
                                    ?? Stamp(r["Updated"])
                    });
                }
            }
            catch (Exception ex)
            {
                _log.Severe("LoadOrderMilestoneActivity (C_Order_ID=" + C_Order_ID + "): " + ex.Message);
            }
        }

        /// <summary>
        /// Field-level edits to the order, read from the platform's change log
        /// (AD_ChangeLog) as ONE ROW PER FIELD: each names the field that changed —
        /// the dictionary's display name for the column, falling back to the raw
        /// column name — who changed it and when, so a reader can follow a single
        /// field through the order's life.
        ///
        /// Both the header and its LINES are read, because "the record was modified"
        /// means either to the person who changed it: a price edited on line 2 is a
        /// change to the order. A line's row is labelled with its line number so the
        /// two are told apart.
        ///
        /// Silently degrades when change logging is off for the table (no rows) or
        /// the schema has no AD_ChangeLog at all. Ported from VAS_092, with COALESCE
        /// for NVL so the statement runs on both databases.
        /// </summary>
        /// <param name="C_Order_ID">Selected sales order id.</param>
        /// <param name="list">Activity list being populated.</param>
        private void LoadOrderChangeActivity(int C_Order_ID, List<ActivityData> list)
        {
            try
            {
                // AD_Column is LEFT joined so a log row whose column has since been
                // removed from the dictionary still reports its change.
                //
                // The line-side rows are reached by Record_ID IN (the order's line
                // ids) under the C_OrderLine table, and carry that line's number.
                string sql = @"SELECT cl.Created     AS EventOn,
                                      cl.OldValue    AS OldValue,
                                      cl.NewValue    AS NewValue,
                                      u.Name         AS UserName,
                                      col.Name       AS FieldLabel,
                                      col.ColumnName AS FieldColumn,
                                      col.AD_Reference_ID       AS RefType,
                                      col.AD_Reference_Value_ID AS RefValueId,
                                      adt.TableName  AS ChangedTable,
                                      ol.Line        AS LineNo,
                                      p.Name         AS ProductName,
                                      ch.Name        AS ChargeName
                                 FROM AD_ChangeLog cl
                                INNER JOIN AD_Table adt
                                        ON (adt.AD_Table_ID = cl.AD_Table_ID)
                                 LEFT OUTER JOIN AD_Column col
                                        ON (col.AD_Column_ID = cl.AD_Column_ID)
                                 LEFT OUTER JOIN AD_User u
                                        ON (u.AD_User_ID = cl.CreatedBy)
                                 LEFT OUTER JOIN C_OrderLine ol
                                        ON (adt.TableName = 'C_OrderLine'
                                            AND ol.C_OrderLine_ID = cl.Record_ID)
                                 LEFT OUTER JOIN M_Product p  ON (p.M_Product_ID = ol.M_Product_ID)
                                 LEFT OUTER JOIN C_Charge  ch ON (ch.C_Charge_ID  = ol.C_Charge_ID)
                                WHERE COALESCE(cl.IsActive, 'Y') = 'Y'
                                  AND ((adt.TableName = 'C_Order'
                                        AND cl.Record_ID = @C_Order_ID)
                                    OR (adt.TableName = 'C_OrderLine'
                                        AND ol.C_Order_ID = @C_Order_ID2))
                                ORDER BY cl.Created DESC";
                DataSet ds = DB.ExecuteDataset(
                    sql,
                    new SqlParameter[]
                    {
                        // Positional binding: one placeholder per occurrence, in the
                        // order the statement reads them.
                        new SqlParameter("@C_Order_ID", C_Order_ID),
                        new SqlParameter("@C_Order_ID2", C_Order_ID)
                    },
                    null);
                if (ds == null || ds.Tables.Count == 0) return;

                foreach (DataRow r in ds.Tables[0].Rows)
                {
                    string field = Util.GetValueOfString(r["FieldLabel"]);
                    if (string.IsNullOrEmpty(field))
                        field = Util.GetValueOfString(r["FieldColumn"]);
                    // A row that can name no field renders as a bare "Updated" with
                    // nothing to identify it — worse than not showing it at all.
                    if (string.IsNullOrEmpty(field)) continue;

                    // A change made on a LINE names the line it was made on, so an
                    // edit to line 20's price is not read as an edit to the order.
                    // The line number and its item travel in ChangeScope, beside the
                    // field rather than inside its name.
                    string scope = "";
                    int lineNo = Util.GetValueOfInt(r["LineNo"]);
                    if (Util.GetValueOfString(r["ChangedTable"]) == "C_OrderLine" && lineNo > 0)
                    {
                        scope = "#" + lineNo;
                        string item = Util.GetValueOfString(r["ProductName"]);
                        if (string.IsNullOrEmpty(item)) item = Util.GetValueOfString(r["ChargeName"]);
                        if (!string.IsNullOrEmpty(item)) scope += " " + item.Trim();
                    }

                    // The move itself. A save that rewrites a field with the value it
                    // already had is not an edit, and the platform logs plenty of those.
                    // Compared on the RAW values, before either is resolved: two
                    // records can share a name, and dropping such a row would hide a
                    // real edit.
                    string oldValue = ChangeValue(Util.GetValueOfString(r["OldValue"]));
                    string newValue = ChangeValue(Util.GetValueOfString(r["NewValue"]));
                    if (string.Equals(oldValue, newValue, StringComparison.Ordinal)) continue;

                    // ... and then reported as the field SHOWS them, not as the log
                    // stored them: a reference reads as the referenced record's
                    // identifier, a list value as its label, a date as the date alone.
                    string column  = Util.GetValueOfString(r["FieldColumn"]);
                    int refType    = Util.GetValueOfInt(r["RefType"]);
                    int refValueId = Util.GetValueOfInt(r["RefValueId"]);

                    list.Add(new ActivityData
                    {
                        EventType   = "Updated",
                        FieldName   = field,
                        OldValue    = _changeValues.Display(oldValue, column, refType, refValueId),
                        NewValue    = _changeValues.Display(newValue, column, refType, refValueId),
                        ChangeScope = scope,
                        ActorName   = Util.GetValueOfString(r["UserName"]),
                        EventTime   = Stamp(r["EventOn"])
                    });
                }
            }
            catch (Exception ex)
            {
                // Change logging is optional; a schema without it simply shows no
                // update rows and the rest of the feed is unaffected.
                _log.Severe("LoadOrderChangeActivity (C_Order_ID=" + C_Order_ID + "): " + ex.Message);
            }
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
        /// Loads notes: the order header note (C_Order.Description) plus each
        /// line's own note (line number + product / charge name + line
        /// description). Composed in C# so the SQL stays portable (no DB-specific
        /// string functions).
        /// </summary>
        private List<NoteData> LoadNotes(int C_Order_ID)
        {
            List<NoteData> notes = new List<NoteData>();
            try
            {
                // LEFT OUTER JOIN to C_OrderLine, with IsActive in the JOIN rather
                // than the WHERE, so the C_Order row — and with it the header note
                // (C_Order.Description) — comes back even when the order has no
                // active lines. An INNER JOIN dropped the whole result for a
                // line-less order, so a description the user had just typed and
                // saved showed nowhere and the Notes section was not drawn at all.
                // The same fix VAS_092 carries.
                string sql = @"SELECT
                                  o.Description AS OrderNote,
                                  ol.Line       AS LineNo,
                                  ol.Description AS LineDescription,
                                  p.Name        AS ProductName,
                                  ch.Name       AS ChargeName
                                FROM C_Order o
                                LEFT OUTER JOIN C_OrderLine ol ON (ol.C_Order_ID = o.C_Order_ID
                                                                   AND ol.IsActive = 'Y')
                                LEFT OUTER JOIN M_Product p  ON (p.M_Product_ID = ol.M_Product_ID)
                                LEFT OUTER JOIN C_Charge  ch ON (ch.C_Charge_ID  = ol.C_Charge_ID)
                                WHERE o.C_Order_ID = @C_Order_ID
                                  AND o.IsActive   = 'Y'
                                ORDER BY ol.Line";
                DataSet ds = DB.ExecuteDataset(sql, OrderParam(C_Order_ID), null);
                if (ds == null || ds.Tables.Count == 0) return notes;

                bool headerAdded = false;
                foreach (DataRow r in ds.Tables[0].Rows)
                {
                    // Header note once (identical across lines).
                    if (!headerAdded)
                    {
                        string headerNote = Util.GetValueOfString(r["OrderNote"]);
                        if (!string.IsNullOrEmpty(headerNote))
                            notes.Add(new NoteData { NoteType = "header", Text = headerNote.Trim() });
                        headerAdded = true;
                    }

                    // Per-line note: only when the line carries its own description.
                    string lineDesc = Util.GetValueOfString(r["LineDescription"]);
                    if (string.IsNullOrEmpty(lineDesc)) continue;

                    string prod = Util.GetValueOfString(r["ProductName"]);
                    if (string.IsNullOrEmpty(prod)) prod = Util.GetValueOfString(r["ChargeName"]);

                    // Labelled with the line NUMBER as well as the item, so a note
                    // is attributable to the row it was written on — two lines of
                    // the same product otherwise read as one note repeated.
                    int lineNo = Util.GetValueOfInt(r["LineNo"]);
                    string label = lineNo > 0 ? "#" + lineNo : "";
                    if (!string.IsNullOrEmpty(prod))
                        label = string.IsNullOrEmpty(label) ? prod.Trim() : label + " " + prod.Trim();

                    string text = string.IsNullOrEmpty(label)
                        ? lineDesc.Trim()
                        : label + " — " + lineDesc.Trim();
                    notes.Add(new NoteData { NoteType = "line", Text = text });
                }
            }
            catch (Exception ex)
            {
                _log.Severe("LoadNotes (C_Order_ID=" + C_Order_ID + "): " + ex.Message);
            }
            return notes;
        }

        /// <summary>
        /// Loads the active billing frequencies (C_Frequency) for the contract
        /// form's Billing Frequency selector.
        /// </summary>
        private List<FrequencyData> LoadFrequencies(Ctx ctx)
        {
            List<FrequencyData> list = new List<FrequencyData>();
            try
            {
                string sql = @"SELECT C_Frequency_ID, Name
                                 FROM C_Frequency
                                WHERE IsActive = 'Y'
                                  AND AD_Client_ID IN (0, @client)
                                ORDER BY Name";
                SqlParameter[] param = new SqlParameter[]
                {
                    new SqlParameter("@client", ctx.GetAD_Client_ID())
                };
                DataSet ds = DB.ExecuteDataset(sql, param, null);
                if (ds == null || ds.Tables.Count == 0) return list;
                foreach (DataRow r in ds.Tables[0].Rows)
                {
                    list.Add(new FrequencyData
                    {
                        C_Frequency_ID = Util.GetValueOfInt(r["C_Frequency_ID"]),
                        Name           = Util.GetValueOfString(r["Name"])
                    });
                }
            }
            catch (Exception ex)
            {
                _log.Severe("LoadFrequencies: " + ex.Message);
            }
            return list;
        }

        // ================================================================= //
        //  WRITE SIDE — Complete Sales Order                                //
        // ================================================================= //

        /// <summary>
        /// Completes the sales order via the platform document engine
        /// (MOrder.ProcessIt("CO")). Refuses to run when the order is already
        /// completed / closed / voided / reversed, so completion is one-way and
        /// idempotent. Runs in its own transaction.
        /// </summary>
        public ActionResultData CompleteSalesOrder(Ctx ctx, int C_Order_ID)
        {
            ActionResultData res = new ActionResultData();
            if (C_Order_ID <= 0) { res.Message = "Invalid order"; return res; }

            Trx trx = Trx.GetTrx(Trx.CreateTrxName("VAS106Complete"));
            try
            {
                MOrder order = new MOrder(ctx, C_Order_ID, trx);
                if (order.Get_ID() != C_Order_ID || !order.IsSOTrx())
                {
                    res.Message = "Sales order not found";
                    return res;
                }

                string status = order.GetDocStatus();
                if (status == "CO" || status == "CL" || status == "VO" || status == "RE")
                {
                    res.Message = "Order is already " + status + " — cannot complete again";
                    return res;
                }

                bool processed;
                try
                {
                    order.SetDocAction(MOrder.DOCACTION_Complete);
                    processed = order.ProcessIt(MOrder.DOCACTION_Complete);
                }
                catch (Exception pex)
                {
                    processed = false;
                    res.Message = pex.Message;
                }

                if (!processed)
                {
                    if (string.IsNullOrEmpty(res.Message))
                        res.Message = order.GetProcessMsg();
                    trx.Rollback();
                    return res;
                }

                order.Save(trx);
                trx.Commit();

                res.Success    = true;
                res.DocStatus  = order.GetDocStatus();
                res.DocumentNo = order.GetDocumentNo();
                res.Message    = order.GetProcessMsg();
            }
            catch (Exception ex)
            {
                try { trx.Rollback(); } catch { }
                res.Message = ex.Message;
                _log.Severe("CompleteSalesOrder (C_Order_ID=" + C_Order_ID + "): " + ex.Message);
            }
            finally
            {
                try { trx.Close(); } catch { }
            }
            return res;
        }

        // ================================================================= //
        //  WRITE SIDE — Create Contract from an order line                  //
        // ================================================================= //

        /// <summary>
        /// Creates a single C_Contract from a service / charge order line,
        /// mirroring the platform's OLineCreateSalesContract logic (records the
        /// contract schedule on the order line, then builds the draft contract
        /// and links it back via C_OrderLine.C_Contract_ID). Guards: order
        /// completed, line belongs to order, line is service / charge, no
        /// existing contract, required fields present. One contract per line.
        /// </summary>
        public ActionResultData CreateContract(Ctx ctx, int C_Order_ID, int C_OrderLine_ID,
            int C_Frequency_ID, int noOfCycle, decimal qtyPerCycle,
            DateTime? startDate, DateTime? endDate)
        {
            ActionResultData res = new ActionResultData();
            if (C_Order_ID <= 0 || C_OrderLine_ID <= 0) { res.Message = "Invalid line"; return res; }
            if (C_Frequency_ID <= 0)   { res.Message = "Billing Frequency is required"; return res; }
            if (!startDate.HasValue)   { res.Message = "Start Date is required"; return res; }

            Trx trx = Trx.GetTrx(Trx.CreateTrxName("VAS106Contract"));
            try
            {
                MOrder order = new MOrder(ctx, C_Order_ID, trx);
                if (order.Get_ID() != C_Order_ID)
                {
                    res.Message = "Sales order not found"; return res;
                }
                // Contracts are only reachable once the order is completed.
                string status = order.GetDocStatus();
                if (status != "CO" && status != "CL")
                {
                    res.Message = "Complete the sales order before creating contracts"; return res;
                }

                MOrderLine line = new MOrderLine(ctx, C_OrderLine_ID, trx);
                if (line.Get_ID() != C_OrderLine_ID || line.GetC_Order_ID() != C_Order_ID)
                {
                    res.Message = "Order line does not belong to this order"; return res;
                }
                // Product (stockable) lines never carry a contract.
                if (line.GetC_Charge_ID() == 0 && line.GetM_Product_ID() > 0)
                {
                    string prodType = Util.GetValueOfString(DB.ExecuteScalar(
                        "SELECT ProductType FROM M_Product WHERE M_Product_ID = " + line.GetM_Product_ID(),
                        null, null));
                    if (prodType == "I")
                    {
                        res.Message = "Contracts apply to service / charge lines only"; return res;
                    }
                }
                if (line.GetC_Contract_ID() > 0)
                {
                    res.Message = "A contract already exists for this line"; return res;
                }

                if (qtyPerCycle <= 0) qtyPerCycle = line.GetQtyOrdered();
                if (qtyPerCycle <= 0) qtyPerCycle = 1;

                // Record the contract schedule on the line (matches the platform
                // process, which reads these back), then flag it as a contract.
                line.SetC_Frequency_ID(C_Frequency_ID);
                line.SetStartDate(startDate.Value);
                if (endDate.HasValue) line.SetEndDate(endDate.Value);
                line.SetQtyPerCycle(qtyPerCycle);
                if (noOfCycle > 0) line.SetNoofCycle(noOfCycle);
                line.SetIsContract(true);
                if (!line.Save(trx))
                {
                    res.Message = "Could not update the order line"; trx.Rollback(); return res;
                }

                // ---- Build the draft contract (X_C_Contract) ----
                X_C_Contract contract = new X_C_Contract(ctx, 0, trx);
                contract.SetAD_Client_ID(order.GetAD_Client_ID());
                contract.SetAD_Org_ID(order.GetAD_Org_ID());
                contract.SetDescription(order.GetDescription());
                contract.SetC_Order_ID(order.GetC_Order_ID());
                contract.SetC_OrderLine_ID(line.GetC_OrderLine_ID());
                contract.SetStartDate(startDate.Value);
                if (endDate.HasValue) contract.SetEndDate(endDate.Value);

                contract.SetC_BPartner_ID(order.GetC_BPartner_ID());
                contract.SetBill_Location_ID(order.GetBill_Location_ID());
                contract.SetBill_User_ID(order.GetBill_User_ID());
                contract.SetSalesRep_ID(order.GetSalesRep_ID());

                contract.SetC_Currency_ID(order.GetC_Currency_ID());
                contract.SetC_ConversionType_ID(order.GetC_ConversionType_ID());
                contract.SetC_PaymentTerm_ID(order.GetC_PaymentTerm_ID());
                contract.SetM_PriceList_ID(order.GetM_PriceList_ID());
                contract.SetC_Frequency_ID(C_Frequency_ID);
                contract.SetC_Project_ID(order.GetC_Project_ID());

                if (line.GetM_Product_ID() > 0) contract.SetM_Product_ID(line.GetM_Product_ID());
                if (line.GetC_UOM_ID() > 0)     contract.SetC_UOM_ID(line.GetC_UOM_ID());
                contract.SetC_Tax_ID(line.GetC_Tax_ID());

                // Prices come straight off the line (works for service + charge).
                decimal price = line.GetPriceActual();
                contract.SetPriceList(price);
                contract.SetPriceActual(price);
                contract.SetPriceEntered(price);
                contract.SetQtyEntered(qtyPerCycle);
                contract.SetDiscount(line.GetDiscount());

                decimal lineNet = decimal.Multiply(qtyPerCycle, price);
                decimal taxAmt = ComputeTaxAmt(line.GetC_Tax_ID(), price, qtyPerCycle);
                contract.SetLineNetAmt(lineNet);
                contract.SetTaxAmt(taxAmt);
                contract.SetGrandTotal(decimal.Add(lineNet, taxAmt));

                // Cycle count: honour the form; else derive from dates + frequency.
                int cycles = noOfCycle;
                if (cycles <= 0)
                    cycles = ComputeCycleCount(C_Frequency_ID, startDate.Value, endDate);
                contract.SetTotalInvoice(cycles);

                contract.SetDocStatus("DR");
                contract.SetRenewContract("N");

                if (!contract.Save(trx))
                {
                    res.Message = "Could not create the contract"; trx.Rollback(); return res;
                }

                // Link the contract back onto the line (enforces one per line).
                line.SetC_Contract_ID(contract.GetC_Contract_ID());
                line.Save(trx);

                trx.Commit();
                res.Success      = true;
                res.C_Contract_ID = contract.GetC_Contract_ID();
                res.DocumentNo    = contract.GetDocumentNo();
                res.Message       = "Contract created";
            }
            catch (Exception ex)
            {
                try { trx.Rollback(); } catch { }
                res.Message = ex.Message;
                _log.Severe("CreateContract (C_OrderLine_ID=" + C_OrderLine_ID + "): " + ex.Message);
            }
            finally
            {
                try { trx.Close(); } catch { }
            }
            return res;
        }

        /// <summary>Tax amount = round(price * rate/100, 2) * qty.</summary>
        private decimal ComputeTaxAmt(int C_Tax_ID, decimal price, decimal qty)
        {
            if (C_Tax_ID <= 0) return 0;
            try
            {
                object o = DB.ExecuteScalar(
                    "SELECT Rate FROM C_Tax WHERE C_Tax_ID = " + C_Tax_ID, null, null);
                decimal rate = Util.GetValueOfDecimal(o);
                if (rate <= 0) return 0;
                decimal per = decimal.Round(decimal.Multiply(price, decimal.Divide(rate, 100)),
                                            2, MidpointRounding.AwayFromZero);
                return decimal.Multiply(per, qty);
            }
            catch { return 0; }
        }

        /// <summary>
        /// Cycle count from start/end dates against the frequency's NoOfDays
        /// (mirrors OLineCreateSalesContract). Falls back to 1.
        /// </summary>
        private int ComputeCycleCount(int C_Frequency_ID, DateTime start, DateTime? end)
        {
            if (!end.HasValue) return 1;
            try
            {
                object o = DB.ExecuteScalar(
                    "SELECT NoOfDays FROM C_Frequency WHERE C_Frequency_ID = " + C_Frequency_ID, null, null);
                int days = Util.GetValueOfInt(o);
                int total = (end.Value - start).Days;
                if (days > 0 && total > 0) return total / days;
            }
            catch { }
            return 1;
        }

        // ================================================================= //
        //  Helpers                                                          //
        // ================================================================= //

        /// <summary>
        /// Resolves a change-log value into the text the field shows — a reference
        /// into the referenced record's identifier, a list code into its label, a
        /// timestamp into the date alone. Shared with the other overview panels
        /// (VAS_ChangeLogValueModel). One per request, so its caches last exactly
        /// as long as the feed being built.
        /// </summary>
        private readonly VAS_ChangeLogValueModel _changeValues = new VAS_ChangeLogValueModel();

        /// <summary>Reads the appointment / task / call / letter sources every
        /// overview panel shares (VAS_ActivitySourcesModel).</summary>
        private readonly VAS_ActivitySourcesModel _activitySources = new VAS_ActivitySourcesModel();

        /// <summary>
        /// Every date and timestamp this panel hands the client is read through
        /// here rather than through Util.GetValueOfDateTime directly, so the
        /// DateTimeKind the PROVIDER tagged the value with cannot reach the JSON.
        /// Oracle tags Unspecified and Npgsql tags Utc or Local; Newtonsoft writes
        /// a zone designator for the latter two and none for the first, and the
        /// panel's parseDbDate reads the two shapes differently — which is why
        /// times were hours out on PostgreSQL. A no-op for a value that is already
        /// Unspecified, so the Oracle path is untouched. See
        /// VAS_ActivitySourcesModel.Stamp for the full account.
        /// </summary>
        private static DateTime? Stamp(object value)
        {
            return VAS_ActivitySourcesModel.Stamp(value);
        }

        /// <summary>
        /// The correspondence and engagement sources shared with every other
        /// overview panel: appointments and tasks (AppointmentsInfo, split on
        /// IsTask), calls (VA048_CallDetails) and letters (MailAttachment1,
        /// AttachmentType 'I'), each pinned to the order by AD_Table_ID +
        /// Record_ID.
        ///
        /// Mails stay with LoadEmailActivity, which carries the recipient and body
        /// detail the mail drawer needs and already asks for AttachmentType 'M',
        /// so the two kinds cannot overlap.
        ///
        /// This is what the header comment on LoadActivity used to rule out — it
        /// said AppointmentsInfo had "no verified direct C_Order link". There is
        /// one: the same polymorphic AD_Table_ID + Record_ID pair every other
        /// correspondence table uses, which VAS_105 and VAS_190 both read.
        /// </summary>
        private void LoadSharedSourceActivity(int C_Order_ID, List<ActivityData> list)
        {
            List<VAS_ActivitySourceRow> rows =
                _activitySources.Load("C_Order", C_Order_ID, false);
            foreach (VAS_ActivitySourceRow s in rows)
            {
                list.Add(new ActivityData
                {
                    // appointment | task | call | letter
                    EventType   = s.Kind,
                    Title       = s.Title,
                    Body        = s.Body,
                    Location    = s.Location,
                    IsClosed    = s.IsClosed,
                    IsCancelled = s.IsCancelled,
                    MailTo      = s.MailTo,
                    MailCc      = s.MailCc,
                    MailBcc     = s.MailBcc,
                    MailFrom    = s.MailFrom,
                    IsMailSent  = s.IsMailSent,
                    // An appointment or task brings the mails sent against it.
                    Mails       = s.Mails,
                    ActorName   = s.ActorName,
                    EventTime   = s.EventTime
                });
            }
        }

        private SqlParameter[] OrderParam(int C_Order_ID)
        {
            return new SqlParameter[] { new SqlParameter("@C_Order_ID", C_Order_ID) };
        }

        /// <summary>
        /// Remembers whether C_Order.VAS_IsEmailSent is readable against this
        /// schema, so a deployment without the column reports it once rather than
        /// on every order the panel opens.
        /// </summary>
        private static bool? _emailSentLookupUsable;

        /// <summary>
        /// Whether the sales order has been e-mailed to the customer
        /// (C_Order.VAS_IsEmailSent = 'Y'), which the header reports as an "Email
        /// Sent" badge — shown only when the flag is set, the same milestone rule
        /// the Posted badge beside it follows.
        ///
        /// Read in its OWN statement rather than as a column of the header SELECT,
        /// and ATTEMPTED rather than gated on a dictionary guard: VAS_IsEmailSent is
        /// a module column, so selecting it alongside the header would fail the
        /// WHOLE overview on a deployment that has not taken it. A genuinely missing
        /// column throws once here, is remembered, and the badge simply never shows.
        /// </summary>
        /// <param name="C_Order_ID">Selected sales order id.</param>
        /// <param name="d">Overview being populated.</param>
        private void LoadEmailSent(int C_Order_ID, SalesOrderOverviewData d)
        {
            if (_emailSentLookupUsable == false) return;

            try
            {
                // COALESCE, not NVL: this statement has to read the same on Oracle
                // and PostgreSQL.
                string sql = @"SELECT COALESCE(o.VAS_IsEmailSent, 'N') AS IsEmailSent
                                 FROM C_Order o
                                WHERE o.C_Order_ID = @C_Order_ID";
                object v = DB.ExecuteScalar(sql, OrderParam(C_Order_ID), null);
                _emailSentLookupUsable = true;
                d.IsEmailSent = Util.GetValueOfString(v) == "Y";
            }
            catch (Exception ex)
            {
                // Almost certainly "no such column" on a schema without the module
                // column. Recorded so the next order skips the attempt.
                _emailSentLookupUsable = false;
                _log.Severe("LoadEmailSent (C_Order_ID=" + C_Order_ID + "): " + ex.Message);
            }
        }

        /// <summary>
        /// Remembers whether the target document type's IsShipConfirm can be read
        /// against this schema, for the same reason
        /// <see cref="_emailSentLookupUsable"/> exists.
        /// </summary>
        private static bool? _shipConfirmLookupUsable;

        /// <summary>
        /// Whether this order's TARGET document type asks for a shipment
        /// confirmation (C_DocTypeTarget_ID -> C_DocType.IsShipConfirm).
        ///
        /// The target type, not the completed one: it is the type the order is being
        /// processed AS — the one the user picks on the record — and C_DocType_ID
        /// only catches up with it when the document completes. The completed type
        /// is kept as the fallback, so a schema that leaves the target unset still
        /// answers from the type the order ended on.
        ///
        /// This is what splits the progress line's Shipped and Delivered stages in
        /// two. With confirmation ON, the delivery order sits In Process awaiting its
        /// confirmation and never reaches Completed on its own, so the stages have to
        /// read that state as progress rather than as nothing having happened; with
        /// it OFF, completion is the milestone.
        ///
        /// Its own statement, ATTEMPTED rather than gated: reading it beside the
        /// header would fail the whole overview on a schema that lacks the column.
        /// </summary>
        /// <param name="C_Order_ID">Selected sales order id.</param>
        /// <param name="d">Overview being populated.</param>
        private void LoadShipConfirmTarget(int C_Order_ID, SalesOrderOverviewData d)
        {
            if (_shipConfirmLookupUsable == false) return;

            try
            {
                string sql = @"SELECT COALESCE(dtt.IsShipConfirm, dt.IsShipConfirm, 'N') AS IsShipConfirm
                                 FROM C_Order o
                                 LEFT OUTER JOIN C_DocType dtt ON (dtt.C_DocType_ID = o.C_DocTypeTarget_ID)
                                 LEFT OUTER JOIN C_DocType dt  ON (dt.C_DocType_ID  = o.C_DocType_ID)
                                WHERE o.C_Order_ID = @C_Order_ID";
                object v = DB.ExecuteScalar(sql, OrderParam(C_Order_ID), null);
                _shipConfirmLookupUsable = true;
                d.IsShipConfirmTarget = Util.GetValueOfString(v) == "Y";
            }
            catch (Exception ex)
            {
                _shipConfirmLookupUsable = false;
                _log.Severe("LoadShipConfirmTarget (C_Order_ID=" + C_Order_ID + "): " + ex.Message);
            }
        }

        /// <summary>
        /// When each of a set of documents was COMPLETED — the Created stamp of its
        /// workflow DocComplete activity — keyed by record id.
        ///
        /// One statement for the whole set rather than one per document: the
        /// progress line needs the completion moment of every delivery, invoice and
        /// receipt the order touches, and asking per record would put an unbounded
        /// number of round trips behind one panel load.
        ///
        /// Both the table name and the id source are written in as literals. They
        /// are this file's own SQL, never user text, and it keeps the statement free
        /// of binds entirely — the app's Oracle layer binds by POSITION, so an id
        /// source carrying a bind name would have to be kept in step with the
        /// parameter array for no gain.
        /// </summary>
        /// <param name="tableName">AD_Table.TableName of the documents' table.</param>
        /// <param name="recordIdSource">SELECT yielding the record ids to look up.</param>
        /// <returns>Record id -> completion moment; empty when none has completed.</returns>
        private Dictionary<int, DateTime> LoadCompletionStamps(string tableName, string recordIdSource)
        {
            Dictionary<int, DateTime> map = new Dictionary<int, DateTime>();
            try
            {
                string sql = @"SELECT wfp.Record_ID, MAX(wfa.Created) AS CompletedOn
                                 FROM AD_WF_Process wfp
                                INNER JOIN AD_WF_Activity wfa
                                        ON (wfa.AD_WF_Process_ID = wfp.AD_WF_Process_ID)
                                INNER JOIN AD_WF_Node wfn
                                        ON (wfn.AD_WF_Node_ID = wfa.AD_WF_Node_ID)
                                INNER JOIN AD_Table adt
                                        ON (adt.AD_Table_ID = wfp.AD_Table_ID)
                                WHERE UPPER(adt.TableName) = '" + tableName.ToUpper() + @"'
                                  AND wfp.Record_ID IN (" + recordIdSource + @")
                                  AND wfp.IsActive = 'Y'
                                  AND wfa.IsActive = 'Y'
                                  AND wfn.IsActive = 'Y'
                                  AND wfa.WFState  = 'CC'
                                  AND UPPER(TRIM(wfn.Value)) IN ('DOCCOMPLETE', 'COMPLETE', '(DOCCOMPLETE)')
                                GROUP BY wfp.Record_ID";
                DataSet ds = DB.ExecuteDataset(sql, null, null);
                if (ds == null || ds.Tables.Count == 0) return map;

                foreach (DataRow r in ds.Tables[0].Rows)
                {
                    int id = Util.GetValueOfInt(r["Record_ID"]);
                    DateTime? on = Stamp(r["CompletedOn"]);
                    if (id > 0 && on.HasValue) map[id] = on.Value;
                }
            }
            catch (Exception ex)
            {
                // Non-fatal: every caller falls back to the document's own Updated
                // stamp, which is the closest thing a document completed outside the
                // workflow engine has.
                _log.Severe("LoadCompletionStamps (" + tableName + "): " + ex.Message);
            }
            return map;
        }

        /// <summary>
        /// The completion moment for one document: its workflow stamp where there is
        /// one, else the document's own last-updated stamp — but only for a document
        /// that IS completed. An open document has no completion date, and reporting
        /// its Updated stamp as one would date a milestone it has not reached.
        /// </summary>
        private static DateTime? CompletedOn(Dictionary<int, DateTime> stamps, int recordId,
                                             string docStatus, DateTime? updated)
        {
            if (docStatus != "CO" && docStatus != "CL") return null;
            if (stamps != null && stamps.ContainsKey(recordId)) return stamps[recordId];
            return updated;
        }

        /// <summary>
        /// Returns true when the given column exists on the given table, using the
        /// AD_Column dictionary. A DB issue degrades to "absent" (false), which
        /// just drops the optional value that depends on it.
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

        private string BuildAddress(string address1, string address2, string city,
                                    string region, string postal, string country)
        {
            List<string> parts = new List<string>();
            if (!string.IsNullOrEmpty(address1)) parts.Add(address1.Trim());
            if (!string.IsNullOrEmpty(address2)) parts.Add(address2.Trim());

            List<string> cityLine = new List<string>();
            if (!string.IsNullOrEmpty(city))   cityLine.Add(city.Trim());
            if (!string.IsNullOrEmpty(region)) cityLine.Add(region.Trim());
            if (!string.IsNullOrEmpty(postal)) cityLine.Add(postal.Trim());
            if (cityLine.Count > 0) parts.Add(string.Join(" ", cityLine));

            if (!string.IsNullOrEmpty(country)) parts.Add(country.Trim());
            return string.Join(", ", parts);
        }

        // ================================================================= //
        //  Data carriers                                                    //
        // ================================================================= //

        public class SalesOrderLineData
        {
            public int     C_OrderLine_ID { get; set; }
            public int     Line           { get; set; }
            // Quantities and the unit price are all in the line's SELECTED unit,
            // the one the row is labelled with. UomRatio carries the base scale the
            // database stores, for converting anything read from stock.
            public decimal QtyOrdered     { get; set; }
            public decimal QtyDelivered   { get; set; }
            public decimal QtyInvoiced    { get; set; }
            public decimal UomRatio       { get; set; }   // base units per selected unit
            public decimal PriceActual    { get; set; }
            // C_OrderLine.PriceList as stored, which is the line's SELECTED unit —
            // what it would have cost at list, shown on the unit price's tooltip.
            // 0 when the line carries none.
            public decimal PriceList      { get; set; }
            public decimal Discount       { get; set; }
            public decimal LineNetAmt     { get; set; }
            public string  Description    { get; set; }
            public int     M_Product_ID   { get; set; }
            public int     C_Charge_ID    { get; set; }
            public bool    IsContractFlag { get; set; }   // C_OrderLine.IsContract
            // C_OrderLine.IsDropShip — the line ships direct to the customer.
            public bool    IsDropShip     { get; set; }
            public int     C_Contract_ID  { get; set; }
            public string  ContractNo     { get; set; }
            public string  ProductValue   { get; set; }   // SKU
            public string  ProductName    { get; set; }
            public string  ProductType    { get; set; }   // I / S / ...
            public string  ChargeName     { get; set; }
            public string  UOMSymbol      { get; set; }
            public string  UOMName        { get; set; }   // C_UOM.Name, for the row's sub-line
            public int     UOMPrecision   { get; set; }
            // M_AttributeSetInstance.Description — the lot / serial / attributes
            // the line was sold against. Blank when the line carries none.
            public string  AttributeSetInstance { get; set; }
            public string  LineType       { get; set; }   // product | service | charge | other
            public string  DeliveredState { get; set; }   // full | part | none
        }

        /// <summary>
        /// One prior version of an order line (C_OrderLineHistory) — what it said
        /// before a change, and who made it. Keyed to its line by C_OrderLine_ID;
        /// the panel draws it in a drawer under that line.
        /// </summary>
        public class LineHistoryData
        {
            public int       C_OrderLine_ID { get; set; }
            public int       LineNo         { get; set; }
            public DateTime? ChangedOn      { get; set; }
            public string    UpdatedByName  { get; set; }
            // On the SELECTED unit, like the line the drawer hangs under.
            public decimal   QtyEntered     { get; set; }
            public decimal   PriceEntered   { get; set; }
            public decimal   Discount       { get; set; }
            public decimal   LineNetAmt     { get; set; }
            public string    Description    { get; set; }
            public string    UOMName        { get; set; }
            public int       UOMPrecision   { get; set; }
        }

        public class DeliveryReadinessData
        {
            public int     C_OrderLine_ID { get; set; }
            public string  ProductValue   { get; set; }
            public string  ProductName    { get; set; }
            // The PRODUCT's base unit (M_Product.C_UOM_ID) — the unit every quantity
            // on this row is counted in, stock included.
            public string  UOMName        { get; set; }
            public string  AttributeSetInstance { get; set; }
            public string  WarehouseName  { get; set; }
            public decimal PendingQty     { get; set; }
            public decimal QtyOnHand      { get; set; }
            // What was ordered and what has gone out, on the same (selected) unit —
            // the pair the partial state is worked out from, and the pair the panel
            // names beside it.
            public decimal QtyOrdered     { get; set; }   // base unit, like every figure here
            public decimal QtyDelivered   { get; set; }
            public string  Readiness      { get; set; }   // ready | partial | instock | short
        }

        /// <summary>
        /// One document raised from the sales order — a shipment, an invoice or a
        /// customer receipt. <see cref="TableName"/> + <see cref="RecordId"/> are
        /// what the panel opens the record with.
        /// </summary>
        public class SalesOrderDocumentData
        {
            public string    Type        { get; set; }   // delivery | invoice | receipt
            public string    TableName   { get; set; }
            public int       RecordId    { get; set; }
            public string    DocumentNo  { get; set; }
            public string    DocStatus   { get; set; }
            public DateTime? DocDate     { get; set; }
            /// <summary>When the document COMPLETED — its workflow DocComplete
            /// stamp, falling back to its own last-updated stamp. Null while it is
            /// open, so the progress line can tell "not yet" from "on this
            /// date". Carried for the receipts, which the Paid stage dates
            /// itself by.</summary>
            public DateTime? CompletedDate { get; set; }
            // Null for a document with no monetary total of its own (a shipment) —
            // distinct from a genuine zero.
            public decimal?  Amount      { get; set; }
            public decimal   DiscountAmt { get; set; }   // receipts
            public int       LineCount   { get; set; }   // shipments
            public bool      IsPaid      { get; set; }   // invoices
            public string    Extra       { get; set; }   // shipment tracking no
        }

        public class DeliveryData
        {
            public int      M_InOut_ID    { get; set; }
            public string   DocumentNo    { get; set; }
            public string   DocStatus     { get; set; }
            public DateTime? MovementDate { get; set; }
            // When the shipment RECORD was raised (M_InOut.Created) — what the
            // progress line's Shipped / Delivered stages are dated by, since
            // MovementDate can be back-dated.
            public DateTime? Created      { get; set; }
            /// <summary>When the shipment COMPLETED — its workflow DocComplete
            /// stamp, falling back to its own last-updated stamp; null while it is
            /// open. The Delivered stage dates itself by this, where Shipped reports
            /// when the shipment was RAISED.</summary>
            public DateTime? CompletedDate { get; set; }
            public string   TrackingNo    { get; set; }
            public string   WarehouseName { get; set; }
            public decimal  DeliveredQty  { get; set; }
            // What went out, in money: each shipped line valued at its share of the
            // order line's net amount.
            public decimal  DeliveredValue { get; set; }
            public int      LineCount     { get; set; }
        }

        public class InvoiceData
        {
            public int      C_Invoice_ID { get; set; }
            public string   DocumentNo   { get; set; }
            public string   DocStatus    { get; set; }
            public DateTime? DateInvoiced { get; set; }
            // When the invoice RECORD was raised (C_Invoice.Created) — what the
            // progress line's Invoiced stage is dated by, since DateInvoiced can be
            // back-dated. Same treatment as DeliveryData.Created.
            public DateTime? Created      { get; set; }
            /// <summary>When the invoice COMPLETED — its workflow DocComplete stamp,
            /// falling back to its own last-updated stamp; null while it is drafted.
            /// The Invoiced stage reports the LATEST of these.</summary>
            public DateTime? CompletedDate { get; set; }
            public decimal  GrandTotal   { get; set; }
            public bool     IsPaid       { get; set; }
        }

        public class ActivityData
        {
            // Note | Email | Delivery | Invoice | Created | Completed | Updated
            public string   EventType { get; set; }
            public string   Title     { get; set; }   // the row's headline; an e-mail's subject
            // The field an "Updated" row is about — the dictionary's display name
            // for the changed column. Empty on every other event type.
            public string   FieldName { get; set; }
            // The move itself: what the field held before the edit and what it holds
            // after. Either side is empty where the log recorded no value — a field
            // cleared, or filled for the first time.
            public string   OldValue  { get; set; }
            public string   NewValue  { get; set; }
            // Which record the edit landed on: "" for the order header, else the
            // line's number and item ("#20 Bolt M8").
            public string   ChangeScope { get; set; }
            public string   ActorName { get; set; }
            public decimal  Amount    { get; set; }
            public DateTime? EventTime { get; set; }

            // E-mail (MailAttachment1) — the body is revealed on click, so it
            // travels with the row rather than costing a second round trip.
            public string   Body       { get; set; }   // TextMsg, flattened to text
            public string   MailTo     { get; set; }   // MailAddress
            public string   MailCc     { get; set; }   // MailAddressCc
            public string   MailBcc    { get; set; }   // MailAddressBcc
            public string   MailFrom   { get; set; }   // MailAddressFrom
            public bool     IsMailSent { get; set; }

            /// <summary>True when the mail is filed against the CUSTOMER
            /// (MailAttachment1 anchored on C_BPartner) rather than against this
            /// order. It is correspondence with the partner, not about this
            /// document, so the panel tags it apart — every order of theirs
            /// carries the same rows.</summary>
            public bool     IsPartnerMail { get; set; }

            // Appointment / task rows (AppointmentsInfo): where the meeting is and
            // whether it has been dealt with. Empty on every other event type.
            public string   Location    { get; set; }
            public bool     IsClosed    { get; set; }
            public bool     IsCancelled { get; set; }

            /// <summary>The e-mails sent against an APPOINTMENT or TASK itself
            /// (MailAttachment1 anchored on AppointmentsInfo): recipient, subject,
            /// body, when and by whom. Distinct from the mail fields above, which
            /// are correspondence about the ORDER. Empty on every other event
            /// type; the bodies travel with the row so the panel reveals them on
            /// click without a second round trip.</summary>
            public List<VAS_ActivityMailRow> Mails { get; set; }
        }

        public class NoteData
        {
            public string NoteType { get; set; }   // header | line
            public string Text     { get; set; }
        }

        public class FrequencyData
        {
            public int    C_Frequency_ID { get; set; }
            public string Name           { get; set; }
        }

        public class ActionResultData
        {
            public bool   Success       { get; set; }
            public string Message       { get; set; }
            public string DocStatus     { get; set; }
            public string DocumentNo    { get; set; }
            public int    C_Contract_ID { get; set; }
        }

        public class SalesOrderOverviewData
        {
            // Header / identity
            public int       C_Order_ID   { get; set; }
            public string    DocumentNo   { get; set; }
            public string    POReference  { get; set; }
            public DateTime? DateOrdered  { get; set; }
            // When the order was completed (workflow DocComplete stamp, else the
            // record's last change) — the progress line's Completed stage.
            public DateTime? CompletedDate { get; set; }
            public DateTime? DatePromised { get; set; }
            public string    DocStatus    { get; set; }
            public string    Posted       { get; set; }
            public string    PriorityRule { get; set; }
            public DateTime? Created      { get; set; }

            // Totals
            public decimal   GrandTotal   { get; set; }
            public decimal   TotalLines   { get; set; }
            public decimal   TaxAmt       { get; set; }

            // Currency
            public int       C_Currency_ID { get; set; }
            public string    CurSymbol     { get; set; }
            public string    ISO_Code      { get; set; }
            public int       StdPrecision  { get; set; }

            // Customer / contact
            public int       C_BPartner_ID  { get; set; }
            public string    CustomerName   { get; set; }
            public string    CreditStatus   { get; set; }   // O/H/S/W/X
            public string    ContactName    { get; set; }
            public string    ContactPhone   { get; set; }
            public string    ContactEmail   { get; set; }
            /// <summary>Customer e-mail address that seeds the Send Invoice
            /// recipient: the order's own contact address, falling back to any
            /// active contact of the customer.</summary>
            public string    CustomerEmail  { get; set; }
            /// <summary>C_Order.VAS_IsEmailSent = 'Y' — the order was e-mailed to
            /// the customer. Drives the header's "Email Sent" badge, which is drawn
            /// only when the flag is set.</summary>
            public bool      IsEmailSent    { get; set; }
            /// <summary>IsShipConfirm on the order's TARGET document type
            /// (C_DocTypeTarget_ID, falling back to the completed type) — this order
            /// is to be shipped with a confirmation. It is what decides whether the
            /// progress line's Shipped and Delivered stages read the delivery order's
            /// IN PROCESS state as progress or wait for it to complete.</summary>
            public bool      IsShipConfirmTarget { get; set; }
            public string    SalesRepName   { get; set; }
            public string    PaymentTermName { get; set; }
            public string    PriceListName  { get; set; }
            public string    WarehouseName  { get; set; }
            public string    DeliveryRule   { get; set; }   // shipping rule A/F/L/M/O/R
            // The dictionary's own name for that value, so the panel shows exactly
            // what the order screen shows. "" when it cannot be resolved.
            public string    DeliveryRuleName { get; set; }
            public string    InvoiceRule    { get; set; }
            public string    BillToAddress  { get; set; }
            public string    ShipToAddress  { get; set; }

            // Created from
            public int       QuotationId    { get; set; }
            public string    QuotationNo    { get; set; }
            // The blanket sales order this one was released against
            // (C_Order.C_Order_Blanket). 0 / "" when it was not.
            public int       BlanketOrderId { get; set; }
            public string    BlanketOrderNo { get; set; }
            public int       OpportunityId  { get; set; }
            public string    OpportunityName { get; set; }
            public int       ProjectId      { get; set; }
            public string    ProjectNo      { get; set; }   // C_Project.Value
            public string    ProjectName    { get; set; }
            // The contract the order references. ContractTable says WHICH table it
            // is — the contract master on the header, or the service contract behind
            // a line — so the panel opens the right record.
            public int       ContractId     { get; set; }
            public string    ContractNo     { get; set; }
            public string    ContractTable  { get; set; }   // VAS_ContractMaster | C_Contract
            public int       ContractCount  { get; set; }

            // Collections
            public List<SalesOrderLineData>   Lines            { get; set; }
            public List<DeliveryReadinessData> DeliveryReadiness { get; set; }
            // Prior versions of the order lines, keyed to their line by
            // C_OrderLine_ID. Empty on a schema without C_OrderLineHistory.
            public List<LineHistoryData>      History          { get; set; }
            public List<DeliveryData>         Deliveries       { get; set; }
            public List<InvoiceData>          Invoices         { get; set; }
            // The two above merged with the order's receipts, for the Documents
            // section. They stay in their own right: the KPI strip and the
            // progress stepper are derived from them.
            public List<SalesOrderDocumentData> Documents      { get; set; }
            public List<ActivityData>         Activity         { get; set; }
            public List<NoteData>             Notes            { get; set; }
            public List<FrequencyData>        Frequencies      { get; set; }
        }
    }
}
