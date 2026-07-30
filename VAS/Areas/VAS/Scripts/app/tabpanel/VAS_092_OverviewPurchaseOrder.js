/************************************************************
 * Module Name    : VAS
 * Purpose        : Purchase Order Overview tab panel. Renders a
 *                  review-oriented overview of the selected purchase
 *                  order (C_Order, IsSoTrx = 'N'): identity, linked
 *                  origin docs, stat strip, 7-stage progress, line
 *                  items with received progress, line change history,
 *                  landed cost (per component + per-line distribution),
 *                  and a stacked Notes / Activity area. Data is fetched
 *                  from VAS_092_OverviewPurchaseOrder/GetPurchaseOrderOverview.
 * Chronological development:
 *   VAI163   2026-06-10  Created
 *   VAI163   2026-06-15  Added inline icons (address, contact, linked
 *                        chips, activity badge) and a Production Order
 *                        linked chip to match the reference design.
 *   VAI163   2026-06-17  Terms & Notes description value now read from
 *                        C_Order.POReference instead of C_Order.Description.
 *   VAI163   2026-06-17  Reworked the landed-cost section into a per-component
 *                        view: component name + source, distribution-method
 *                        tag, expected, actual (invoiced / awaiting), variance,
 *                        a totals footer and a methodology note.
 *   VAI163   2026-06-22  Redesigned to the canonical windows-and-panels.md
 *                        Right Panel Body language.
 *   VAI163   2026-07-01  Header reworked to a soft-gradient title strip above a
 *                        white two-column details card (vendor | terms).
 *   VAI163   2026-07-01  Snapshot metric grid + Generated From chip strip.
 *   VAI163   2026-07-01  Two-column Terms & Notes | Recent Activity footer.
 *   VAI163   2026-07-08  Generated From "Manual" fallback chip.
 *   VAI163   2026-07-17  - getMsg() fallbacks so missing AD_Message keys never
 *                          render raw (English defaults in MSG_DEFAULTS).
 *                        - Header mirrors the Sales Order overview: vendor left,
 *                          terms right (adds Pricelist); priority from the real
 *                          C_Order.PriorityRule field.
 *                        - Generated From now shows only origins that exist
 *                          (Sales Order / Requisition / Contract) as clickable
 *                          chips that open the source record.
 *                        - Order Total caption changed to "Inclusive Taxes".
 *                        - Stepper no longer shows "Partially Received" once the
 *                          order is fully received.
 *                        - New Line History section (C_OrderLineHistory).
 *                        - Landed cost now shows the per-line distribution
 *                          breakdown under each component.
 *                        - Notes & Activity stacked (Notes then Activity); Notes
 *                          sourced like the Sales Order overview.
 *   VAI163   2026-07-17  Completed stage now shows OrderCompletedDate (the real
 *                        DocComplete timestamp) instead of DateOrdered.
 *                      - New Documents section: the GRNs / vendor invoices
 *                        prepared from this PO, each row opening the underlying
 *                        document via the shared openRecord() zoom path.
 *   VAI163   2026-07-24  "Received: X of Y lines" now counts only deliverable
 *                        (stockable) lines, matching the corrected server-side
 *                        fully-received logic, so a PO with a freight / landed-
 *                        cost charge line no longer stays "Partially Received".
 *   VAI163   2026-07-27  - Line items drop the "SKU" prefix before the product
 *                          search key and show the unit of measure (UOM) next to
 *                          the ordered quantity.
 *                        - Line items show the Attribute Set Instance details
 *                          (size / lot / serial ...) when the product carries one.
 *                        - Timestamps (Created / activity / history / completion)
 *                          are parsed as UTC and rendered in the browser's local
 *                          zone; date-only fields keep their stored calendar day
 *                          without any zone shift (parseDbDate asUtc flag).
 *                        - Comments/chat notes render commenter name + timestamp
 *                          above the comment text.
 *                        - Priority Low (7) and Minor (9) now show a green badge,
 *                          driven straight from C_Order.PriorityRule.
 *                        - Order Progress "With Vendor" stage now shows the PO
 *                          completion date instead of the ordered date.
 *   VAI163   2026-07-27  - Line-item and line-history quantities now show the
 *                          entered-UOM quantity (QtyEntered) instead of the
 *                          base-UOM QtyOrdered.
 *                        - Long product / component names carry a title tooltip
 *                          so the full name is readable on hover when truncated.
 *   VAI163   2026-07-27  - Documents section now shows the GRN amount (total
 *                          received value), not just invoice amounts.
 *                        - Order Progress: a reached milestone renders green even
 *                          when it is the current stage, so Invoice Raised and
 *                          Payment Done turn green once the invoice / payment
 *                          exists (was staying orange / pending).
 *   VAI163   2026-07-27  - Activity feed: notes/chat now use the same symmetric
 *                          row layout as every other activity (tag | title |
 *                          right-aligned timestamp · author) instead of a
 *                          bespoke multi-line block.
 *                        - Order Progress Payment stage shows "Payment Completed"
 *                          when every invoice is fully paid, else "Pending Amount".
 *   VAI163   2026-07-27  - Documents section now lists linked AP Payments — coins
 *                          icon, "AP Payment · Discounted Amount: <DiscountAmt>"
 *                          sub-label, DateTrx / DocStatus / PayAmt, clicking the
 *                          number opens the AP Payment record. The activity feed's
 *                          payment entry now reads "AP Payment Created".
 *                        - A "Posted" badge renders beside the priority / status
 *                          pills when the document is posted (hidden otherwise).
 *                        - Line-items Subtotal now shows the net-of-tax amount
 *                          (SubTotal = GrandTotal − Tax) so a tax-inclusive price
 *                          list shows the correct subtotal and extracted tax.
 *   VAI163   2026-07-27  - The Sales Order origin chip now opens the Sales Order
 *                          window (openRecord passes IsSOTrx = true) instead of
 *                          wrongly opening the Purchase Order window.
 *                        - Line Items table paginates at 25 rows per page with a
 *                          prev / next footer pager.
 *                        - Line History is collapsed by default with a Show /
 *                          Hide Details toggle.
 *                        - New Budget section + header "Budget Breach" badge
 *                          surfacing the GL budget check (IsBudgetViolated,
 *                          MaxBudgetViolationAmount, per-line BudgetViolationAmount).
 *                        - Received card quantity now item-only (server side).
 *   VAI163   2026-07-27  - Line Attribute Set Instance sub-line is hidden when the
 *                          line has no real instance (blank / "--" placeholder).
 *   VAI163   2026-07-29  - A line's change history now sits under that line: an
 *                          edited line carries a History (n) chip that opens a
 *                          drawer directly beneath it, instead of the reader
 *                          matching line numbers against a separate table. The
 *                          drawer state survives paging and resets per record.
 *                          The standalone section is kept only for history whose
 *                          line was removed from the order ("Removed lines").
 *   VAI163   2026-07-29  - The history toggle moved to a trailing action column at
 *                          the right-hand end of the line row and is now icon-only,
 *                          with a tooltip / aria-label carrying the action and the
 *                          number of changes ("Show history (3)").
 *   VAI163   2026-07-29  - Recent Activity shows the e-mails sent against the
 *                          order (MailAttachment1): subject as the headline,
 *                          recipient beneath it, sender and time on the right, and
 *                          the message body revealed on click.
 *                        - Landed Cost columns explain themselves on hover; the
 *                          Actual cell names the invoice its figure came from and
 *                          the Variance cell spells out the direction.
 *   VAI163   2026-07-29  - E-mails also get their own Emails section above Recent
 *                          Activity: the complete correspondence, newest first,
 *                          where the activity feed only shows what survives its
 *                          cap. Same row shape and click-to-open body.
 *                        - Emails page client-side at 10 per page, with the pager
 *                          as the list card's footer. Which messages are open is
 *                          remembered per mail id, so paging away and back does
 *                          not fold one the reader had opened.
 *   VAI163   2026-07-30  - Removed the standalone Emails section: Recent Activity
 *                          already lists the same e-mails (type "email", same
 *                          subject / recipient row and click-to-open body), so the
 *                          panel was showing every message twice. E-mails stay in
 *                          the activity feed only; the server still loads them
 *                          (LoadEmails feeds the feed).
 *                        - Landed Cost now pages client-side at 10 components per
 *                          page, reusing the line-items pager. The totals footer
 *                          and the methodology note keep reporting every
 *                          component, not just the visible page.
 *                        - A line's history drawer drops the Received column and
 *                          shows Updated By instead (C_OrderLineHistory.UpdatedBy
 *                          resolved to AD_User.Name): who changed the line is what
 *                          the drawer is read for, and the received quantity is
 *                          a property of the line today, not of a past version.
 *                        - The Removed lines table also shows Updated By, as its
 *                          last column so both history views carry it in the same
 *                          position.
 ***********************************************************/
; VAS = window.VAS || {};
; (function (VAS, $) {

    VAS.VAS_092_OverviewPurchaseOrder = function () {
        this.record_ID = 0;
        this.table_ID = 0;
        this.windowNo = 0;
        this.curTab = null;
        this.selectedRow = null;
        this.panelWidth;

        var $self = this;
        var $root;
        var $busy;
        var $body;
        var $emptyState;
        var data = null;
        var linesPage = 1;      // current line-items page (1-based)
        var historyOpen = false; // Removed-lines history section expanded?
        // Which line items have their history drawer open, keyed by
        // C_OrderLine_ID. Survives a pager repaint; cleared per record.
        var lineHistOpen = {};
        var lcPage = 1;          // current Landed Cost page (1-based)

        // Some AD_Message keys may not be seeded yet; fall back to a readable
        // English default so the panel never renders raw keys.
        var MSG_DEFAULTS = {
            VAS_092_NoData: "No purchase order selected",
            VAS_092_PurchaseOrder: "Purchase Order",
            VAS_092_Created: "Created",
            VAS_092_Ordered: "Ordered",
            VAS_092_SupplierRef: "Vendor Ref",
            VAS_092_Buyer: "Buyer",
            VAS_092_Vendor: "Vendor",
            VAS_092_BillTo: "Bill To",
            VAS_092_ShipTo: "Warehouse",
            VAS_092_PaymentTerms: "Payment Term",
            VAS_092_Pricelist: "Pricelist",
            VAS_092_Currency: "Currency",
            // Priority (C_Order.PriorityRule)
            VAS_092_UrgentPriority: "Urgent priority",
            VAS_092_HighPriority: "High priority",
            VAS_092_MediumPriority: "Medium priority",
            VAS_092_LowPriority: "Low priority",
            VAS_092_MinorPriority: "Minor priority",
            VAS_092_NormalPriority: "Normal priority",
            // Delivery / status
            VAS_092_PaymentDone: "Payment Done",
            VAS_092_PaymentCompleted: "Payment Completed",
            VAS_092_PendingAmount: "Pending Amount",
            VAS_092_Completed: "Completed",
            VAS_092_PartialDelivered: "Partially Received",
            VAS_092_FullyReceived: "Received",
            VAS_092_WithVendor: "With Vendor",
            VAS_092_Drafted: "Drafted",
            // Generated From
            VAS_092_GeneratedFrom: "Generated From",
            VAS_092_Manual: "Manual",
            VAS_092_SalesOrder: "Sales Order",
            VAS_092_Origin: "Origin",
            VAS_092_Requisition: "Requisition",
            VAS_092_Contract: "Contract",
            VAS_092_More: "more",
            // Snapshot
            VAS_092_InclTaxFreight: "Inclusive Taxes",
            VAS_092_OrderTotal: "Order Total",
            VAS_092_ExpectedDelivery: "Expected Delivery",
            VAS_092_LineItems: "Line Items",
            VAS_092_Lines: "lines",
            VAS_092_UnitsOrdered: "units ordered",
            VAS_092_Of: "of",
            VAS_092_Received: "Received",
            // Progress
            VAS_092_InvoiceRaised: "Invoice Raised",
            VAS_092_OrderProgress: "Order Progress",
            VAS_092_Stage: "Stage",
            VAS_092_InProgress: "In progress",
            VAS_092_Pending: "Pending",
            VAS_092_Required: "Required",
            // Line items
            VAS_092_Items: "items",
            VAS_092_Units: "units",
            VAS_092_Item: "Item",
            VAS_092_UnitPrice: "Unit price",
            VAS_092_Qty: "Qty",
            VAS_092_ExpDelivery: "Exp. delivery",
            VAS_092_LineTotal: "Line Amount",
            VAS_092_Subtotal: "Subtotal",
            VAS_092_Tax: "Tax",
            VAS_092_GrandTotal: "Grand Total",
            VAS_092_SKU: "SKU",
            VAS_092_Delivered: "Received",
            VAS_092_Partial: "Partial",
            VAS_092_Awaiting: "Awaiting",
            // History
            VAS_092_History: "Line History",
            VAS_092_Changes: "changes",
            VAS_092_ChangedOn: "Changed On",
            VAS_092_UpdatedBy: "Updated By",
            VAS_092_ShowDetails: "Show Details",
            VAS_092_HideDetails: "Hide Details",
            VAS_092_ShowHistory: "Show history",
            VAS_092_HideHistory: "Hide history",
            VAS_092_RemovedLines: "Removed lines",
            // Pagination
            VAS_092_Showing: "Showing",
            VAS_092_Prev: "Previous",
            VAS_092_Next: "Next",
            // Budget (GL budget control / breach)
            VAS_092_Budget: "Budget",
            VAS_092_BudgetBreach: "Budget Breach",
            VAS_092_WithinBudget: "Within Budget",
            VAS_092_OverBudgetBy: "Over budget by",
            VAS_092_MaxLineBreach: "Highest line breach",
            VAS_092_LineBreaches: "Line breaches",
            VAS_092_NoBudgetBreach: "This order is within its allocated budget.",
            VAS_092_BudgetNote: "Budget breach amounts are shown in the accounting currency the GL budget is maintained in.",
            // Documents (GRNs / invoices raised from this PO)
            VAS_092_Documents: "Documents",
            VAS_092_Document: "Document",
            VAS_092_DocDate: "Date",
            VAS_092_DocStatus: "Status",
            VAS_092_Amount: "Amount",
            VAS_092_GoodsReceipt: "Goods Receipt",
            VAS_092_VendorInvoice: "Vendor Invoice",
            VAS_092_APPayment: "AP Payment",
            VAS_092_DiscountedAmount: "Discounted Amount",
            VAS_092_GRNsCount: "GRNs",
            VAS_092_InvoicesCount: "invoices",
            VAS_092_PaymentsCount: "payments",
            VAS_092_LinesCount: "lines",
            VAS_092_Paid: "Paid",
            VAS_092_Posted: "Posted",
            VAS_092_StDrafted: "Drafted",
            VAS_092_StInProgress: "In Progress",
            VAS_092_StCompleted: "Completed",
            VAS_092_StClosed: "Closed",
            VAS_092_StApproved: "Approved",
            VAS_092_StNotApproved: "Not Approved",
            VAS_092_StInvalid: "Invalid",
            VAS_092_StWaiting: "Waiting",
            VAS_092_StUnknown: "Unknown",
            // Landed cost
            VAS_092_ByValue: "By value",
            VAS_092_ByQuantity: "By quantity",
            VAS_092_ByWeight: "By weight",
            VAS_092_ByVolume: "By volume",
            VAS_092_Equally: "Equally",
            VAS_092_ByCosts: "By costs",
            VAS_092_NotSet: "Not set",
            VAS_092_LandedCost: "Landed Cost",
            VAS_092_CostComponent: "Cost Component",
            VAS_092_DistributionMethod: "Distribution Method",
            VAS_092_Expected: "Expected",
            VAS_092_Actual: "Actual",
            VAS_092_Variance: "Variance",
            VAS_092_Components: "components",
            VAS_092_Basis: "basis",
            VAS_092_MixedBasis: "mixed basis",
            VAS_092_Invoiced: "Invoiced",
            VAS_092_AwaitingInvoice: "Awaiting invoice",
            VAS_092_OnBudget: "On budget",
            // Landed cost column tooltips
            VAS_092_TipComponent: "The cost element charged on top of the goods (freight, duty, insurance …), with the document it came from underneath.",
            VAS_092_TipMethod: "How this cost is spread across the order lines — by invoice value, quantity, weight, volume, equally per line or by costs.",
            VAS_092_TipExpected: "Landed cost planned on this order, before any vendor invoice.",
            VAS_092_TipActual: "Landed cost actually charged, taken from completed vendor invoices allocated to this order's receipts.",
            VAS_092_TipAwaiting: "No vendor invoice has charged this cost yet, so there is nothing actual to show.",
            VAS_092_TipVariance: "Actual minus expected.",
            VAS_092_TipOver: "Actual is over expected by",
            VAS_092_TipUnder: "Actual is under expected by",
            VAS_092_TipOnBudget: "Actual matches what was expected.",
            VAS_092_ExpectedLandedCost: "Expected Landed Cost",
            VAS_092_ActualToDate: "Actual to Date",
            VAS_092_OpenNotInvoiced: "Open (not invoiced)",
            VAS_092_LandedValue: "Landed Value",
            VAS_092_LandedMethodology: "Actuals replace estimates as vendor charge invoices are completed —",
            VAS_092_ComponentsInvoiced: "components invoiced",
            VAS_092_DistributedAcross: "Distributed across lines",
            VAS_092_Line: "Line",
            // Notes / Activity
            VAS_092_Notes: "Notes",
            VAS_092_NotesCount: "notes",
            VAS_092_TagNote: "Note",
            VAS_092_TagEmail: "Email",
            VAS_092_MailTo: "To",
            VAS_092_MailFrom: "From",
            VAS_092_ShowMailBody: "Show message",
            VAS_092_HideMailBody: "Hide message",
            VAS_092_TagGRN: "GRN",
            VAS_092_ActGRN: "Goods received",
            VAS_092_TagInvoice: "Invoice",
            VAS_092_ActInvoice: "Vendor invoice",
            VAS_092_TagPayment: "AP Payment",
            VAS_092_ActPayment: "AP Payment Created",
            VAS_092_TagApproval: "Approved",
            VAS_092_ActApproval: "Order approved",
            VAS_092_TagCreated: "Created",
            VAS_092_ActCreated: "Order created",
            VAS_092_RecentActivity: "Activity",
            VAS_092_Updates: "updates",
            VAS_092_OpenRecord: "Open"
        };

        // Prefer the seeded AD_Message; else the English default; else the key.
        function getMsg(key) {
            try {
                var m = VIS.Msg.getMsg(key);
                if (m && m !== key) return m;
            } catch (e) { }
            return MSG_DEFAULTS.hasOwnProperty(key) ? MSG_DEFAULTS[key] : key;
        }

        this.init = function () {
            $root = $('<div class="MPC-vaspo-root"></div>');
            $body = $('<div class="MPC-vaspo-body"></div>');
            $emptyState = $('<div class="MPC-vaspo-empty" style="display:none;"></div>');
            $emptyState.text(getMsg("VAS_092_NoData"));
            $root.append($body).append($emptyState);
            createBusyIndicator();
            bindEvents();
        };

        function createBusyIndicator() {
            $busy = $('<div class="vis-apanel-busy">' +
                      '<div class="vis-busyindicatorinnerwrap"><i class="vis_widgetloader"></i></div>' +
                      '</div>');
            $busy.css({
                "position": "absolute", "width": "100%", "height": "100%",
                "text-align": "center", "z-index": "999"
            });
            $busy[0].style.visibility = "hidden";
            $root.append($busy);
        }

        function showBusy(show) {
            if (!$busy || !$busy[0]) return;
            $busy[0].style.visibility = show ? "visible" : "hidden";
        }

        this.fetchData = function (recordID) {
            showBusy(true);
            $.ajax({
                url: VIS.Application.contextUrl + "VAS_092_OverviewPurchaseOrder/GetPurchaseOrderOverview",
                type: "GET",
                dataType: "json",
                data: { C_Order_ID: recordID },
                success: function (raw) {
                    var parsed = (typeof raw === "string") ? jQuery.parseJSON(raw) : raw;
                    data = parsed;
                    // Reset per-record view state so a newly selected order starts
                    // on the first line page with History collapsed.
                    linesPage = 1;
                    historyOpen = false;
                    lineHistOpen = {};
                    lcPage = 1;
                    render();
                    showBusy(false);
                },
                error: function (err) {
                    console.log(err);
                    showBusy(false);
                }
            });
        };

        this.clear = function () {
            data = null;
            render();
        };

        function render() {
            $body.empty();

            if (!data || !data.C_Order_ID) {
                $body.hide();
                $emptyState.show();
                return;
            }

            $emptyState.hide();
            $body.show();

            // Body is a flat stack of self-contained sections.
            renderHeader();
            renderLinked();
            renderSnapshot();
            renderProgress();
            renderBudget();
            renderLines();
            renderDocuments();
            renderHistory();
            renderLandedCost();
            renderBottom();
        }

        // ----------------------------------------------------------------- //
        //  Section / primitive builders                                      //
        // ----------------------------------------------------------------- //

        function section(title, opts, $parent) {
            opts = opts || {};
            var $sec = $('<section class="MPC-vaspo-sec"></section>');
            var $head = $('<div class="MPC-vaspo-secHead"></div>');
            $head.append($('<h2 class="MPC-vaspo-secTitle"></h2>').text(title));

            var $right = $('<div class="MPC-vaspo-secRight"></div>');
            if (opts.summary) {
                $right.append($('<span class="MPC-vaspo-secSummary"></span>').text(opts.summary));
            }
            if (opts.action) {
                $right.append($('<a class="MPC-vaspo-secAction"></a>').text(opts.action));
            }
            if (opts.summary || opts.action) $head.append($right);

            $sec.append($head);
            ($parent || $body).append($sec);
            return $sec;
        }

        // Status pill (tinted). tone: info | success | warning | risk | neutral | purple
        function pill(label, tone) {
            return $('<span class="MPC-vaspo-pill"></span>')
                .addClass("tone-" + (tone || "neutral"))
                .text(label);
        }

        // ---------- Header (title strip + vendor / terms card) ---------- //

        // Maps the order's delivery / progress state to a semantic tone + label.
        // Fully received is checked before partial so a completed order never
        // reports "Partially Received".
        function statusTone(d) {
            if (d.IsPaymentDone)
                return { tone: "success", label: getMsg("VAS_092_PaymentDone") };
            if (d.IsFullyDelivered)
                return { tone: "success", label: getMsg("VAS_092_Completed") };
            if (d.IsPartialDelivered)
                return { tone: "warning", label: getMsg("VAS_092_PartialDelivered") };
            if (d.IsWithVendor)
                return { tone: "info", label: getMsg("VAS_092_WithVendor") };
            return { tone: "neutral", label: getMsg("VAS_092_Drafted") };
        }

        // Priority pill descriptor from the real C_Order.PriorityRule field
        // (1 Urgent / 3 High / 5 Medium / 7 Low), mirroring the Sales Order
        // overview, so a changed priority is reflected in the panel.
        function priorityMeta() {
            // Tone/label are driven by the real C_Order.PriorityRule value so the
            // panel always matches the record screen. Low (7) and Minor (9) both
            // render a green (success) badge; anything unmapped stays neutral.
            switch (data.PriorityRule) {
                case "1": return { tone: "risk",    icon: "chevUp", label: getMsg("VAS_092_UrgentPriority") };
                case "3": return { tone: "warning", icon: "chevUp", label: getMsg("VAS_092_HighPriority") };
                case "5": return { tone: "info",    icon: null,     label: getMsg("VAS_092_MediumPriority") };
                case "7": return { tone: "success", icon: null,     label: getMsg("VAS_092_LowPriority") };
                case "9": return { tone: "success", icon: null,     label: getMsg("VAS_092_MinorPriority") };
                default:  return { tone: "neutral", icon: null,     label: getMsg("VAS_092_NormalPriority") };
            }
        }

        // Header: soft-gradient title strip (title + subtitle, priority + status
        // pills) above a white two-column details card — vendor identity on the
        // left, payment / pricelist / currency / ship-to fields on the right,
        // mirroring the Sales Order overview.
        function renderHeader() {
            var st = statusTone(data);
            var pm = priorityMeta();

            var $strip = $('<section class="MPC-vaspo-hdr"></section>');
            var $top = $('<div class="MPC-vaspo-hdrTop"></div>');

            var $tl = $('<div class="MPC-vaspo-hdrTitleWrap"></div>');
            $tl.append($('<div class="MPC-vaspo-hdrTitle"></div>').text(
                getMsg("VAS_092_PurchaseOrder") +
                (data.DocumentNo ? " — " + data.DocumentNo : "")));

            var subBits = [];
            if (data.POReference) subBits.push(getMsg("VAS_092_SupplierRef") + " " + data.POReference);
            var ordered = formatDate(data.DateOrdered);
            if (ordered) subBits.push(getMsg("VAS_092_Ordered") + " " + ordered);
            if (subBits.length) {
                $tl.append($('<div class="MPC-vaspo-hdrSub"></div>').text(subBits.join(" · ")));
            }
            $top.append($tl);

            var $pills = $('<div class="MPC-vaspo-hdrPills"></div>');
            $pills.append(headerPill(pm.label, pm.tone, pm.icon, false));
            $pills.append(headerPill(st.label, st.tone, null, true));
            // Posted badge — shown only when the document has been posted to the
            // ledger (C_Order.Posted = 'Y'); hidden for unposted documents.
            if (data.Posted) {
                $pills.append(headerPill(getMsg("VAS_092_Posted"), "success", "check", false));
            }
            // Budget Breach badge — shown when the platform's budget check flagged
            // this order as over its GL budget (C_Order.IsBudgetViolated = 'Y').
            if (data.IsBudgetViolated) {
                $pills.append(headerPill(getMsg("VAS_092_BudgetBreach"), "risk", "alert", false));
            }
            $top.append($pills);

            $strip.append($top);
            $body.append($strip);

            // --- Details card: vendor identity (left) + terms fields (right) ---
            if (!data.VendorName && !data.VendorAddress &&
                !data.ContactName && !data.ContactPhone && !data.ContactEmail &&
                !data.PaymentTermName && !data.PriceListName && !data.WarehouseName &&
                !data.OrgName && !data.ISO_Code) {
                return;
            }

            var $card = $('<section class="MPC-vaspo-hdrCard"></section>');

            // Left column: vendor name + address + contact bits + bill to.
            var $left = $('<div class="MPC-vaspo-hdrColL"></div>');
            $left.append($('<div class="MPC-vaspo-fLabel"></div>').text(getMsg("VAS_092_Vendor")));
            $left.append($('<div class="MPC-vaspo-vendName"></div>').text(data.VendorName || ""));

            if (data.VendorAddress) {
                var $addr = $('<div class="MPC-vaspo-vendAddr"></div>');
                $addr.append(svgIcon("pin"));
                $addr.append($('<span></span>').text(data.VendorAddress));
                $left.append($addr);
            }

            var $contact = $('<div class="MPC-vaspo-vendContact"></div>');
            appendContactBit($contact, "user",  data.ContactName);
            appendContactBit($contact, "phone", data.ContactPhone);
            appendContactBit($contact, "mail",  data.ContactEmail);
            if ($contact.children().length) $left.append($contact);           
            $card.append($left);

            // Right column: labelled term fields (mirrors SO field set).
            var $right = $('<div class="MPC-vaspo-hdrColR"></div>');
            if (data.BuyerName)       $right.append(headerField(getMsg("VAS_092_Buyer"), data.BuyerName));
            if (data.PaymentTermName) $right.append(headerField(getMsg("VAS_092_PaymentTerms"), data.PaymentTermName));
            if (data.PriceListName)   $right.append(headerField(getMsg("VAS_092_Pricelist"), data.PriceListName));
            var cur = (data.ISO_Code || "") + (data.CurSymbol ? " (" + data.CurSymbol + ")" : "");
            if (cur.trim())           $right.append(headerField(getMsg("VAS_092_Currency"), cur));
            if (data.WarehouseName) $right.append(headerField(getMsg("VAS_092_ShipTo"), data.WarehouseName));
            //if (data.OrgName) $right.append(headerField(getMsg("VAS_092_BillTo"), data.OrgName));
            if ($right.children().length) $card.append($right);

            $body.append($card);
        }

        function headerPill(label, tone, icon, withDot) {
            var $p = $('<span class="MPC-vaspo-hdrPill"></span>')
                .addClass("tone-" + (tone || "neutral"));
            if (icon) $p.append(svgIcon(icon));
            if (withDot) $p.append($('<span class="MPC-vaspo-hdrDot"></span>'));
            $p.append($('<span></span>').text(label));
            return $p;
        }

        function headerField(label, value) {
            var $f = $('<div class="MPC-vaspo-hdrField"></div>');
            $f.append($('<div class="MPC-vaspo-fLabel"></div>').text(label));
            $f.append($('<div class="MPC-vaspo-fVal"></div>').text(value));
            return $f;
        }

        function appendContactBit($container, icon, value) {
            if (!value) return;
            var $bit = $('<span class="MPC-vaspo-contactBit"></span>');
            $bit.append(svgIcon(icon));
            $bit.append($('<span></span>').text(value));
            $container.append($bit);
        }

        // ---------- Generated From (chip strip) ---------- //

        // Shows only origins that actually exist — Sales Order (Ref_Order_ID),
        // Requisition (M_Requisition) and Contract reference (VAS_ContractMaster)
        // — each a clickable chip that opens the source record. When the PO has
        // no origin at all it shows a single "Manual" chip.
        function renderLinked() {
            var $strip = $('<section class="MPC-vaspo-genfrom"></section>');
            $strip.append($('<span class="MPC-vaspo-gfLabel"></span>')
                .text(getMsg("VAS_092_GeneratedFrom")));

            var $chips = $('<div class="MPC-vaspo-gfChips"></div>');
            var any = false;

            // Sales Order (origin) — from Ref_Order_ID. Opened as a sales
            // transaction (isSOTrx = true) so the framework resolves the Sales
            // Order window rather than the Purchase Order window (both live in
            // C_Order).
            if (data.RefOrderDocNo) {
                $chips.append(originChip("doc", getMsg("VAS_092_SalesOrder"), data.RefOrderDocNo,
                    pill(getMsg("VAS_092_Origin"), "info"), "info", "C_Order", data.RefOrderId, true));
                any = true;
            }

            // Requisition — the requisition(s) this PO was generated from.
            if (data.RequisitionDocNo) {
                var reqVal = data.RequisitionDocNo;
                if (data.RequisitionCount > 1)
                    reqVal += " +" + (data.RequisitionCount - 1) + " " + getMsg("VAS_092_More");
                $chips.append(originChip("clipboardCheck", getMsg("VAS_092_Requisition"), reqVal,
                    null, "success", "M_Requisition", data.RequisitionId));
                any = true;
            }

            // Contract reference — C_Order.VAS_ContractMaster_ID.
            if (data.ContractMasterId > 0) {
                $chips.append(originChip("doc", getMsg("VAS_092_Contract"),
                    data.ContractMasterNo || ("#" + data.ContractMasterId),
                    null, "purple", "VAS_ContractMaster", data.ContractMasterId));
                any = true;
            }

            if (!any) {
                $chips.append(originChip("pencil", getMsg("VAS_092_Manual"), null, null, "info", null, 0));
            }

            $strip.append($chips);
            $body.append($strip);
        }

        // Origin chip: leading icon (tinted by iconTone) + grey label + dark
        // value, with an optional trailing status pill. When a table + record id
        // is supplied the chip becomes a link that opens that record.
        function originChip(icon, label, value, $statusPill, iconTone, tableName, recordId, isSOTrx) {
            var $chip = $('<span class="MPC-vaspo-chip"></span>').addClass("ic-" + (iconTone || "muted"));
            var isLink = tableName && recordId && +recordId > 0;
            if (isLink) {
                $chip.addClass("is-link")
                    .attr("data-open-table", tableName)
                    .attr("data-open-id", recordId);
                // Sales-transaction records (e.g. the originating Sales Order in
                // C_Order) must open in their SO window, not the PO window.
                if (isSOTrx) $chip.attr("data-open-sotrx", "Y");
            }
            $chip.append(svgIcon(icon));
            $chip.append($('<span class="MPC-vaspo-chipLabel"></span>').text(label));
            if (value) $chip.append($('<span class="MPC-vaspo-chipVal"></span>').text(value));
            if ($statusPill) $chip.append($statusPill);
            if (isLink) $chip.append(svgIcon("arrowUpRight"));
            return $chip;
        }

        // ---------- Snapshot (metric grid) ---------- //

        function renderSnapshot() {
            var $snap = $('<section class="MPC-vaspo-snap"></section>');

            // Order Total — grand total (tax inclusive).
            var totalSub = (data.ISO_Code || "");
            var incl = getMsg("VAS_092_InclTaxFreight");
            totalSub = totalSub ? totalSub + " · " + incl : incl;
            $snap.append(metricCard("total", "coins", getMsg("VAS_092_OrderTotal"),
                formatAmount(+data.GrandTotal || 0, data.CurSymbol, data.ISO_Code, data.StdPrecision),
                totalSub, null));

            // Expected Delivery — promised date + delivery-status caption.
            var st = statusTone(data);
            $snap.append(metricCard("delivery", "calendar", getMsg("VAS_092_ExpectedDelivery"),
                formatDate(data.DatePromised) || "—", st.label, null));

            // Line Items — line count + total units ordered.
            $snap.append(metricCard("lines", "box", getMsg("VAS_092_LineItems"),
                (data.LineCount || 0) + " " + getMsg("VAS_092_Lines"),
                formatNumber(+data.TotalQtyOrdered || 0, 0) + " " + getMsg("VAS_092_UnitsOrdered"),
                null));

            // Received — delivered/ordered + percent and fully-received line count.
            var ordered = +data.TotalQtyOrdered || 0;
            var delivered = +data.TotalQtyDelivered || 0;
            var pct = ordered > 0 ? Math.round((delivered / ordered) * 100) : 0;
            // Denominator is the deliverable (stockable) line count, not the raw
            // line count — charge / service lines are never received, so counting
            // them would keep a fully received order below 100%.
            var recvSub = pct + "% · " + (data.FullyReceivedLineCount || 0) + " " +
                getMsg("VAS_092_Of") + " " + (data.DeliverableLineCount || 0) + " " +
                getMsg("VAS_092_Lines");
            $snap.append(metricCard("received", "inbox", getMsg("VAS_092_Received"),
                formatNumber(delivered, 0) + " / " + formatNumber(ordered, 0), recvSub, pct));

            $body.append($snap);
        }

        function metricCard(tone, icon, label, value, sub, pct) {
            var $c = $('<div class="MPC-vaspo-metric"></div>').addClass("tone-" + tone);

            var $head = $('<div class="MPC-vaspo-mHead"></div>');
            $head.append(svgIcon(icon));
            $head.append($('<span class="MPC-vaspo-mLabel"></span>').text(label));
            $c.append($head);

            $c.append($('<div class="MPC-vaspo-mVal"></div>').text(value));
            if (sub) $c.append($('<div class="MPC-vaspo-mSub"></div>').text(sub));

            if (pct != null) {
                var $bar = $('<div class="MPC-vaspo-mBar"><i></i></div>');
                $bar.find("i").css("width", Math.max(0, Math.min(100, pct)) + "%");
                $c.append($bar);
            }
            return $c;
        }

        // ---------- Order Progress (Timeline) ---------- //

        // 7-stage progress. The Partial Received stage relabels to "Received"
        // once the order is fully received so it never reads as partial.
        function progressStages() {
            var recvLabel = data.IsFullyDelivered
                ? getMsg("VAS_092_FullyReceived")
                : getMsg("VAS_092_PartialDelivered");
            // Payment stage: "Payment Completed" (green) once every invoice is
            // fully paid; otherwise "Pending Amount".
            var paymentLabel = data.IsPaymentDone
                ? getMsg("VAS_092_PaymentCompleted")
                : getMsg("VAS_092_PendingAmount");
            return [
                { key: "VAS_092_Drafted",          done: true,                     active: data.CurrentStage === 1, date: data.Created || data.DateOrdered },
                { key: "VAS_092_Completed",        done: data.IsCompleted,         active: data.CurrentStage === 2, date: data.OrderCompletedDate || data.DateOrdered },
                { key: "VAS_092_WithVendor",       done: data.IsWithVendor,        active: data.CurrentStage === 3, date: data.OrderCompletedDate || data.DateOrdered },
                { key: "VAS_092_ExpectedDelivery", done: data.IsExpectedDelivery,  active: data.CurrentStage === 4, date: data.DatePromised, required: true },
                { key: "VAS_092_PartialDelivered", label: recvLabel, done: data.IsPartialDelivered, active: data.CurrentStage === 5, date: data.LastReceiptDate },
                { key: "VAS_092_InvoiceRaised",    done: data.IsInvoiceRaised,     active: data.CurrentStage === 6, date: data.LastInvoiceDate },
                { key: "VAS_092_PaymentDone",      label: paymentLabel, done: data.IsPaymentDone, active: data.CurrentStage === 7, date: data.LastPaymentDate }
            ];
        }

        function renderProgress() {
            var stages = progressStages();
            var st = statusTone(data);

            var $sec = section(getMsg("VAS_092_OrderProgress"), {
                summary: getMsg("VAS_092_Stage") + " " + (data.CurrentStage || 1) +
                    " " + getMsg("VAS_092_Of") + " " + stages.length + " · " + st.label
            });

            var $tl = $('<div class="MPC-vaspo-stepper"></div>');
            for (var i = 0; i < stages.length; i++) {
                var s = stages[i];

                // A reached milestone is always green (done), even when it is the
                // current stage — so Invoice Raised / Payment Done turn green as
                // soon as the invoice / payment exists rather than staying orange.
                // The orange "in progress" state is reserved for a current stage
                // that has not yet been reached.
                var stateCls, statusText;
                if (s.done) {
                    stateCls = "is-done"; statusText = getMsg("VAS_092_Completed");
                } else if (s.active) {
                    stateCls = "is-active"; statusText = getMsg("VAS_092_InProgress");
                } else {
                    stateCls = "is-pending"; statusText = getMsg("VAS_092_Pending");
                }

                var dateText = formatDate(s.date);
                var metaText = statusText;
                if (s.done && dateText) {
                    metaText = s.required
                        ? getMsg("VAS_092_Required") + " " + dateText
                        : dateText;
                }

                $tl.append(stepEntry(i + 1, s.label || getMsg(s.key), metaText, s.done, stateCls));
            }
            $sec.append($tl);
        }

        function stepEntry(num, title, meta, done, stateCls) {
            var $entry = $('<div class="MPC-vaspo-step"></div>').addClass(stateCls || "");

            var $rail = $('<div class="MPC-vaspo-stepRail"></div>');
            $rail.append($('<span class="MPC-vaspo-stepLine MPC-vaspo-stepLine-l"></span>'));
            var $dot = $('<span class="MPC-vaspo-stepDot"></span>');
            if (done) {
                $dot.append(svgIcon("check"));
            } else {
                $dot.text(num);
            }
            $rail.append($dot);
            $rail.append($('<span class="MPC-vaspo-stepLine MPC-vaspo-stepLine-r"></span>'));
            $entry.append($rail);

            var $lbl = $('<div class="MPC-vaspo-stepLabel"></div>');
            $lbl.append($('<div class="MPC-vaspo-stepTitle"></div>').text(title));
            if (meta) $lbl.append($('<div class="MPC-vaspo-stepMeta"></div>').text(meta));
            $entry.append($lbl);

            return $entry;
        }

        // ---------- Budget (GL budget control / breach) ---------- //

        // Surfaces the platform's budget check result. The section renders only
        // when there is something to flag — the order is marked over budget
        // (C_Order.IsBudgetViolated) or at least one line carries a breach amount
        // (C_OrderLine.BudgetViolationAmount). It shows how far over budget the
        // order is and a per-line breakdown of which lines breached and by how
        // much. Amounts are in the accounting currency the GL budget is kept in.
        function renderBudget() {
            var lines = (data && data.Lines) || [];
            var lineBreaches = [];
            for (var i = 0; i < lines.length; i++) {
                if ((+lines[i].BudgetViolationAmount || 0) > 0) lineBreaches.push(lines[i]);
            }
            var maxBreach = +data.MaxBudgetViolationAmount || 0;
            var breached = data.IsBudgetViolated || maxBreach > 0 || lineBreaches.length > 0;
            if (!breached) return;   // within budget → nothing to surface

            var $sec = section(getMsg("VAS_092_Budget"), {
                summary: getMsg("VAS_092_BudgetBreach")
            });

            // Breach banner: alert icon + "Over budget by <amount>".
            var $card = $('<div class="MPC-vaspo-budget is-breach"></div>');
            var $head = $('<div class="MPC-vaspo-budgetHead"></div>');
            $head.append(svgIcon("alert"));
            $head.append($('<span class="MPC-vaspo-budgetTitle"></span>')
                .text(getMsg("VAS_092_BudgetBreach")));
            $card.append($head);

            if (maxBreach > 0) {
                var $amt = $('<div class="MPC-vaspo-budgetAmt"></div>');
                $amt.append($('<span class="MPC-vaspo-budgetLbl"></span>')
                    .text(getMsg("VAS_092_OverBudgetBy")));
                $amt.append($('<b></b>').text(formatAmount(maxBreach, data.CurSymbol,
                    data.ISO_Code, data.StdPrecision)));
                $card.append($amt);
            }
            $sec.append($card);

            // Per-line breakdown of the lines that breached budget.
            if (lineBreaches.length) {
                var $tbl = $('<div class="MPC-vaspo-table MPC-vaspo-budgetTable"></div>');
                var $h = $('<div class="MPC-vaspo-tRow MPC-vaspo-tHead"></div>');
                $h.append($('<span></span>').text(getMsg("VAS_092_Item")));
                $h.append($('<span class="ta-r"></span>').text(getMsg("VAS_092_OverBudgetBy")));
                $tbl.append($h);

                for (var j = 0; j < lineBreaches.length; j++) {
                    var ln = lineBreaches[j];
                    var $tr = $('<div class="MPC-vaspo-tRow MPC-vaspo-tBody"></div>');

                    var $item = $('<span class="MPC-vaspo-itItem"></span>');
                    $item.append($('<div class="MPC-vaspo-itName"></div>')
                        .text(ln.ProductName || "").attr("title", ln.ProductName || ""));
                    $item.append($('<div class="MPC-vaspo-itSku"></div>')
                        .text(getMsg("VAS_092_Line") + " " + ln.Line));
                    $tr.append($item);

                    $tr.append($('<span class="ta-r MPC-vaspo-budgetOver"></span>').text(
                        formatAmount(+ln.BudgetViolationAmount || 0, data.CurSymbol,
                            data.ISO_Code, data.StdPrecision)));
                    $tbl.append($tr);
                }
                $sec.append($tbl);
            }

            // Currency clarification note.
            var $note = $('<div class="MPC-vaspo-note"></div>');
            $note.append(svgIcon("info"));
            $note.append($('<span></span>').text(getMsg("VAS_092_BudgetNote")));
            $sec.append($note);
        }

        // ---------- Line Items (table) ---------- //

        // Maximum line-item rows shown per page; the table paginates beyond this.
        var LINES_PER_PAGE = 25;

        function renderLines() {
            var lines = (data && data.Lines) || [];
            if (!lines.length) return;

            var $sec = section(getMsg("VAS_092_LineItems"), {
                summary: (data.LineCount || 0) + " " + getMsg("VAS_092_Items") + " · " +
                    formatNumber(+data.TotalQtyOrdered || 0, 0) + " " + getMsg("VAS_092_Units") + " · " +
                    formatNumber(+data.TotalQtyDelivered || 0, 0) + " " + getMsg("VAS_092_Received")
            });

            var $tbl = $('<div class="MPC-vaspo-table MPC-vaspo-itTable"></div>');
            $sec.append($tbl);
            paintLinesTable($tbl, lines);
        }

        // (Re)paints the line-items table for the current page. Kept separate from
        // renderLines so the pager can repaint just the table without rebuilding
        // the whole panel. The totals footer always reflects the full order, not
        // the visible page.
        function paintLinesTable($tbl, lines) {
            $tbl.empty();

            var totalPages = Math.ceil(lines.length / LINES_PER_PAGE);
            if (linesPage < 1) linesPage = 1;
            if (linesPage > totalPages) linesPage = totalPages;
            var start = (linesPage - 1) * LINES_PER_PAGE;
            var end = Math.min(start + LINES_PER_PAGE, lines.length);

            var $head = $('<div class="MPC-vaspo-tRow MPC-vaspo-tHead"></div>');
            $head.append($('<span></span>').text(getMsg("VAS_092_Item")));
            $head.append($('<span></span>').text(getMsg("VAS_092_UnitPrice")));
            $head.append($('<span class="ta-c"></span>').text(getMsg("VAS_092_Qty")));
            $head.append($('<span></span>').text(getMsg("VAS_092_ExpDelivery")));
            $head.append($('<span class="ta-r"></span>').text(getMsg("VAS_092_LineTotal")));
            $head.append($('<span class="ta-r"></span>').text(getMsg("VAS_092_Received")));
            // Trailing action column (per-line history toggle) — deliberately
            // unlabelled; the button carries its own tooltip.
            $head.append($('<span></span>'));
            $tbl.append($head);

            // A line's change history is drawn directly beneath that line, in a
            // drawer the row's own History button opens — not in a separate table
            // further down the panel where the reader has to match line numbers up
            // by hand.
            var histByLine = historyByLine();
            for (var i = start; i < end; i++) {
                var ln = lines[i];
                var hist = histByLine[ln.C_OrderLine_ID] || [];
                $tbl.append(buildLineRow(ln, hist));
                if (hist.length) $tbl.append(buildLineHistory(ln, hist));
            }

            // Subtotal is the net-of-tax product amount (GrandTotal − TaxAmt). For
            // a tax-inclusive price list this differs from C_Order.TotalLines
            // (which carries the gross), so the Subtotal + Tax always sum to the
            // grand total for both inclusive and exclusive price lists.
            var $foot = $('<div class="MPC-vaspo-tFoot"></div>');
            $foot.append(buildTotalBit(getMsg("VAS_092_Subtotal"),
                formatAmount(+data.SubTotal || 0, data.CurSymbol, data.ISO_Code, data.StdPrecision), false));
            $foot.append(buildTotalBit(getMsg("VAS_092_Tax"),
                formatAmount(+data.TaxAmt || 0, data.CurSymbol, data.ISO_Code, data.StdPrecision), false));
            $foot.append(buildTotalBit(getMsg("VAS_092_GrandTotal"),
                formatAmount(+data.GrandTotal || 0, data.CurSymbol, data.ISO_Code, data.StdPrecision), true));
            $tbl.append($foot);

            if (totalPages > 1) {
                $tbl.append(buildLinesPager($tbl, lines, start, end, totalPages));
            }
        }

        // "Showing a–b of N" on the left, prev / "page of pages" / next on the
        // right. Prev/next repaint the table in place (paintLinesTable).
        function buildLinesPager($tbl, lines, start, end, totalPages) {
            var $pager = $('<div class="MPC-vaspo-pager"></div>');

            $pager.append($('<span class="MPC-vaspo-pagerInfo"></span>').text(
                getMsg("VAS_092_Showing") + " " + (start + 1) + "–" + end + " " +
                getMsg("VAS_092_Of") + " " + lines.length));

            var $nav = $('<div class="MPC-vaspo-pagerNav"></div>');

            var $prev = $('<button type="button" class="MPC-vaspo-pgBtn"></button>')
                .attr("aria-label", getMsg("VAS_092_Prev"));
            $prev.append(svgIcon("chevLeft"));
            if (linesPage <= 1) $prev.prop("disabled", true);
            $prev.on("click", function () {
                if (linesPage > 1) { linesPage--; paintLinesTable($tbl, lines); }
            });

            var $next = $('<button type="button" class="MPC-vaspo-pgBtn"></button>')
                .attr("aria-label", getMsg("VAS_092_Next"));
            $next.append(svgIcon("chevRight"));
            if (linesPage >= totalPages) $next.prop("disabled", true);
            $next.on("click", function () {
                if (linesPage < totalPages) { linesPage++; paintLinesTable($tbl, lines); }
            });

            $nav.append($prev);
            $nav.append($('<span class="MPC-vaspo-pagerLabel"></span>').text(
                linesPage + " " + getMsg("VAS_092_Of") + " " + totalPages));
            $nav.append($next);

            $pager.append($nav);
            return $pager;
        }

        // History rows grouped by the line they belong to (newest first, as the
        // model already ordered them).
        function historyByLine() {
            var map = {};
            var rows = (data && data.History) || [];
            for (var i = 0; i < rows.length; i++) {
                var id = rows[i].C_OrderLine_ID;
                if (!id) continue;
                if (!map[id]) map[id] = [];
                map[id].push(rows[i]);
            }
            return map;
        }

        function buildLineRow(ln, hist) {
            var $tr = $('<div class="MPC-vaspo-tRow MPC-vaspo-tBody"></div>');

            var $item = $('<span class="MPC-vaspo-itItem"></span>');
            // Full product name in a title tooltip so a truncated (ellipsised)
            // name is still readable in full on hover.
            $item.append($('<div class="MPC-vaspo-itName"></div>')
                .text(ln.ProductName || "").attr("title", ln.ProductName || ""));
            // Product search key (no "SKU" prefix) or, failing that, the line note.
            if (ln.ProductValue) {
                $item.append($('<div class="MPC-vaspo-itSku"></div>').text(ln.ProductValue));
            } else if (ln.Description) {
                $item.append($('<div class="MPC-vaspo-itSku"></div>').text(ln.Description));
            }
            // Attribute Set Instance details (size / colour / lot / serial ...).
            // Only when the line carries a real instance — a blank or "--" / "-"
            // placeholder (no M_AttributeSetInstance_ID) is not shown.
            var asi = (ln.AttributeSetInstance || "").trim();
            if (asi && asi !== "--" && asi !== "-") {
                $item.append($('<div class="MPC-vaspo-itAttr"></div>')
                    .text(asi).attr("title", asi));
            }
            $tr.append($item);

            $tr.append($('<span></span>').text(formatAmount(
                +ln.PriceActual || 0, data.CurSymbol, data.ISO_Code,
                ln.PricePrecision != null ? ln.PricePrecision : data.StdPrecision)));

            // Entered quantity (C_OrderLine.QtyEntered) with its unit of measure.
            var qtyText = formatNumber(+ln.QtyEntered || 0, +ln.UOMPrecision || 0);
            if (ln.UOMSymbol) qtyText += " " + ln.UOMSymbol;
            $tr.append($('<span class="ta-c"></span>').text(qtyText));

            var $exp = $('<span class="MPC-vaspo-expDate"></span>');
            $exp.append(document.createTextNode(formatDate(ln.DatePromised) || "—"));
            $exp.append($('<small></small>').text(recvLabel(ln.RecvState)));
            $tr.append($exp);

            $tr.append($('<span class="ta-r"></span>').text(formatAmount(
                +ln.LineNetAmt || 0, data.CurSymbol, data.ISO_Code, data.StdPrecision)));

            var ordered = +ln.QtyOrdered || 0;
            var delivered = +ln.QtyDelivered || 0;
            var pct = ordered > 0 ? Math.round((delivered / ordered) * 100) : 0;
            var $recv = $('<span class="MPC-vaspo-recv ta-r"></span>').addClass(ln.RecvState || "none");
            var $bar = $('<span class="MPC-vaspo-recvBar"><i></i></span>');
            $bar.find("i").css("width", Math.max(0, Math.min(100, pct)) + "%");
            $recv.append($bar);
            $recv.append(document.createTextNode(
                formatNumber(delivered, +ln.UOMPrecision || 0) + "/" +
                formatNumber(ordered, +ln.UOMPrecision || 0)));
            $tr.append($recv);

            // Trailing action column, right-hand edge of the row. Only a line that
            // was actually edited carries the toggle — an untouched order shows an
            // empty cell there, so every row keeps the same grid.
            var $act = $('<span class="MPC-vaspo-itAct"></span>');
            if (hist && hist.length) $act.append(buildHistToggle(ln, hist));
            $tr.append($act);

            return $tr;
        }

        // The per-line affordance: an icon button at the right-hand end of the row
        // that opens the drawer sitting immediately beneath it. Icon-only keeps the
        // action column narrow, so the tooltip (and aria-label) carries the meaning
        // and the change count.
        function buildHistToggle(ln, hist) {
            var open = !!lineHistOpen[ln.C_OrderLine_ID];

            var $b = $('<button type="button" class="MPC-vaspo-histBtn"></button>')
                .attr("aria-expanded", open ? "true" : "false")
                .attr("title", histToggleLabel(open, hist.length))
                .attr("aria-label", histToggleLabel(open, hist.length));
            $b.append(svgIcon("history"));
            if (open) $b.addClass("is-open");

            $b.on("click", function (e) {
                e.preventDefault();
                e.stopPropagation();
                var nowOpen = !lineHistOpen[ln.C_OrderLine_ID];
                lineHistOpen[ln.C_OrderLine_ID] = nowOpen;
                $b.toggleClass("is-open", nowOpen)
                  .attr("aria-expanded", nowOpen ? "true" : "false")
                  .attr("title", histToggleLabel(nowOpen, hist.length))
                  .attr("aria-label", histToggleLabel(nowOpen, hist.length));
                // The drawer is this row's next sibling — no id plumbing needed.
                $b.closest(".MPC-vaspo-tBody").next(".MPC-vaspo-lineHist").toggle(nowOpen);
            });

            return $b;
        }

        function histToggleLabel(open, count) {
            return open ? getMsg("VAS_092_HideHistory")
                        : getMsg("VAS_092_ShowHistory") + " (" + count + ")";
        }

        // The drawer itself: the prior versions of this one line, newest first.
        // Rendered collapsed unless this line was left open.
        //
        // It reuses the line-items table's own row classes, so every version sits
        // on the SAME six columns in the SAME order as the line above it — the
        // first cell carries the change timestamp in place of the item (the item
        // is the line it hangs under), then Unit Price, Qty, Exp. delivery, Line
        // Amount and Received exactly as the line renders them.
        function buildLineHistory(ln, rows) {
            var $wrap = $('<div class="MPC-vaspo-lineHist"></div>');
            if (!lineHistOpen[ln.C_OrderLine_ID]) $wrap.hide();

            var $tbl = $('<div class="MPC-vaspo-table MPC-vaspo-itTable MPC-vaspo-lhTable"></div>');

            var $h = $('<div class="MPC-vaspo-tRow MPC-vaspo-tHead"></div>');
            $h.append($('<span></span>').text(getMsg("VAS_092_ChangedOn")));
            $h.append($('<span></span>').text(getMsg("VAS_092_UnitPrice")));
            $h.append($('<span class="ta-c"></span>').text(getMsg("VAS_092_Qty")));
            $h.append($('<span></span>').text(getMsg("VAS_092_ExpDelivery")));
            $h.append($('<span class="ta-r"></span>').text(getMsg("VAS_092_LineTotal")));
            // Who made the change, in the track the line's Received bar occupies —
            // the drawer still lines up column-for-column with the line above it.
            $h.append($('<span></span>').text(getMsg("VAS_092_UpdatedBy")));
            $tbl.append($h);

            for (var i = 0; i < rows.length; i++) {
                $tbl.append(buildLineHistoryRow(ln, rows[i]));
            }

            $wrap.append($tbl);
            return $wrap;
        }

        function buildLineHistoryRow(ln, h) {
            var $r = $('<div class="MPC-vaspo-tRow MPC-vaspo-tBody"></div>');

            // Local system time (formatDateTime converts the UTC-stored value),
            // plus the note that version carried when it differs from now.
            var $when = $('<span class="MPC-vaspo-lhWhen"></span>');
            $when.append(document.createTextNode(formatDateTime(h.ChangedOn) || "—"));
            if (h.Description && h.Description !== ln.Description) {
                $when.append($('<small></small>').text(h.Description).attr("title", h.Description));
            }
            $r.append($when);

            $r.append($('<span></span>').text(formatAmount(
                +h.PriceActual || 0, data.CurSymbol, data.ISO_Code, h.StdPrecision)));

            var qty = formatNumber(+h.QtyEntered || 0, +h.UOMPrecision || 0);
            if (h.UOMSymbol) qty += " " + h.UOMSymbol;
            $r.append($('<span class="ta-c"></span>').text(qty));

            $r.append($('<span></span>').text(formatDate(h.DatePromised) || "—"));

            $r.append($('<span class="ta-r"></span>').text(formatAmount(
                +h.LineNetAmt || 0, data.CurSymbol, data.ISO_Code, h.StdPrecision)));

            // Who changed the line (C_OrderLineHistory.UpdatedBy). A snapshot
            // written by a background/platform process can carry no resolvable
            // user, so an unknown author shows a dash rather than an empty cell.
            var by = h.UpdatedByName || "";
            $r.append($('<span class="MPC-vaspo-lhBy"></span>')
                .text(by || "—").attr("title", by));

            return $r;
        }

        function recvLabel(state) {
            if (state === "full") return getMsg("VAS_092_Delivered");
            if (state === "part") return getMsg("VAS_092_Partial");
            return getMsg("VAS_092_Awaiting");
        }

        function buildTotalBit(label, value, isGrand) {
            var $bit = $('<span class="MPC-vaspo-tf"></span>');
            if (isGrand) $bit.addClass("is-grand");
            $bit.append(document.createTextNode(label));
            $bit.append($('<b></b>').text(value));
            return $bit;
        }

        // ---------- Line History (C_OrderLineHistory) ---------- //

        // Prior versions of the order lines the platform snapshots on
        // re-activate / edit. A live line's history is drawn inline, in the drawer
        // under that line — this section only carries what has nowhere to sit:
        // history belonging to lines that were since removed from the order.
        //
        // It stays collapsed by default (secondary, audit-style view). A "Show
        // Details" link in the section header expands it; the open/closed state is
        // remembered per record (historyOpen) until another order is selected.
        function renderHistory() {
            var all = (data && data.History) || [];
            if (!all.length) return;

            var lines = (data && data.Lines) || [];
            var live = {};
            for (var l = 0; l < lines.length; l++) live[lines[l].C_OrderLine_ID] = true;

            var rows = [];
            for (var i = 0; i < all.length; i++) {
                if (!all[i].C_OrderLine_ID || !live[all[i].C_OrderLine_ID]) rows.push(all[i]);
            }
            if (!rows.length) return;

            var $sec = section(getMsg("VAS_092_RemovedLines"), {
                summary: rows.length + " " + getMsg("VAS_092_Changes")
            });

            var $tbl = $('<div class="MPC-vaspo-table MPC-vaspo-histTable"></div>');

            var $h = $('<div class="MPC-vaspo-tRow MPC-vaspo-tHead"></div>');
            $h.append($('<span></span>').text(getMsg("VAS_092_Item")));
            $h.append($('<span></span>').text(getMsg("VAS_092_ChangedOn")));
            $h.append($('<span class="ta-c"></span>').text(getMsg("VAS_092_Qty")));
            $h.append($('<span class="ta-r"></span>').text(getMsg("VAS_092_UnitPrice")));
            $h.append($('<span class="ta-r"></span>').text(getMsg("VAS_092_LineTotal")));
            // Last column, same position the per-line history drawer puts it in.
            $h.append($('<span></span>').text(getMsg("VAS_092_UpdatedBy")));
            $tbl.append($h);

            for (var i = 0; i < rows.length; i++) {
                $tbl.append(buildHistoryRow(rows[i]));
            }
            $sec.append($tbl);

            // Collapse toggle in the section header (right side).
            var $toggle = $('<a href="#" class="MPC-vaspo-showDetails"></a>');
            var $right = $sec.find(".MPC-vaspo-secRight").first();
            if (!$right.length) {
                $right = $('<div class="MPC-vaspo-secRight"></div>');
                $sec.find(".MPC-vaspo-secHead").first().append($right);
            }
            $right.append($toggle);

            function paintHistory() {
                $tbl.toggle(historyOpen);
                $toggle.text(historyOpen
                    ? getMsg("VAS_092_HideDetails")
                    : getMsg("VAS_092_ShowDetails"));
            }
            $toggle.on("click", function (e) {
                e.preventDefault();
                historyOpen = !historyOpen;
                paintHistory();
            });
            paintHistory();
        }

        function buildHistoryRow(h) {
            var $tr = $('<div class="MPC-vaspo-tRow MPC-vaspo-tBody"></div>');

            var $item = $('<span class="MPC-vaspo-itItem"></span>');
            $item.append($('<div class="MPC-vaspo-itName"></div>')
                .text(h.ProductName || "").attr("title", h.ProductName || ""));
            var sub = getMsg("VAS_092_Line") + " " + h.LineNo;
            if (h.Description) sub += " · " + h.Description;
            $item.append($('<div class="MPC-vaspo-itSku"></div>').text(sub));
            $tr.append($item);

            // Local system time (formatDateTime converts the UTC-stored value).
            $tr.append($('<span></span>').text(formatDateTime(h.ChangedOn) || "—"));
            // Entered quantity snapshot (C_OrderLineHistory.QtyEntered).
            $tr.append($('<span class="ta-c"></span>').text(
                formatNumber(+h.QtyEntered || 0, +h.UOMPrecision || 0)));
            $tr.append($('<span class="ta-r"></span>').text(formatAmount(
                +h.PriceActual || 0, data.CurSymbol, data.ISO_Code, h.StdPrecision)));
            $tr.append($('<span class="ta-r"></span>').text(formatAmount(
                +h.LineNetAmt || 0, data.CurSymbol, data.ISO_Code, h.StdPrecision)));
            // Who changed the line; a platform/background snapshot leaves no
            // resolvable user, so an unknown author shows a dash.
            var by = h.UpdatedByName || "";
            $tr.append($('<span class="MPC-vaspo-lhBy"></span>')
                .text(by || "—").attr("title", by));

            return $tr;
        }

        // ---------- Documents (GRNs / invoices raised from this PO) ---------- //

        // DocStatus code -> label + tone. Codes the platform can return on a
        // receipt / invoice; anything unmapped falls back to a neutral chip.
        var DOC_STATUS = {
            "DR": { key: "VAS_092_StDrafted",     tone: "neutral" },
            "IP": { key: "VAS_092_StInProgress",  tone: "info"    },
            "CO": { key: "VAS_092_StCompleted",   tone: "success" },
            "CL": { key: "VAS_092_StClosed",      tone: "success" },
            "AP": { key: "VAS_092_StApproved",    tone: "success" },
            "NA": { key: "VAS_092_StNotApproved", tone: "warning" },
            "IN": { key: "VAS_092_StInvalid",     tone: "risk"    },
            "WC": { key: "VAS_092_StWaiting",     tone: "info"    },
            "WP": { key: "VAS_092_StWaiting",     tone: "info"    }
        };

        function docStatusPill(code) {
            var s = DOC_STATUS[code];
            return s ? pill(getMsg(s.key), s.tone)
                     : pill(code || getMsg("VAS_092_StUnknown"), "neutral");
        }

        // The GRNs and vendor invoices prepared from this PO. Each row opens the
        // underlying document through the shared openRecord() zoom path.
        function renderDocuments() {
            var rows = (data && data.Documents) || [];
            if (!rows.length) return;

            var $sec = section(getMsg("VAS_092_Documents"), {
                summary: buildDocumentsSummary(rows)
            });

            var $tbl = $('<div class="MPC-vaspo-table MPC-vaspo-docTable"></div>');

            var $h = $('<div class="MPC-vaspo-tRow MPC-vaspo-tHead"></div>');
            $h.append($('<span></span>').text(getMsg("VAS_092_Document")));
            $h.append($('<span></span>').text(getMsg("VAS_092_DocDate")));
            $h.append($('<span></span>').text(getMsg("VAS_092_DocStatus")));
            $h.append($('<span class="ta-r"></span>').text(getMsg("VAS_092_Amount")));
            $tbl.append($h);

            for (var i = 0; i < rows.length; i++) {
                $tbl.append(buildDocumentRow(rows[i]));
            }

            $sec.append($tbl);
        }

        // "2 GRNs · 1 invoices · 1 payments" — only the kinds actually present
        // are counted.
        function buildDocumentsSummary(rows) {
            var grn = 0, inv = 0, pay = 0;
            for (var i = 0; i < rows.length; i++) {
                if (rows[i].Type === "grn") grn++;
                else if (rows[i].Type === "invoice") inv++;
                else if (rows[i].Type === "payment") pay++;
            }
            var bits = [];
            if (grn) bits.push(grn + " " + getMsg("VAS_092_GRNsCount"));
            if (inv) bits.push(inv + " " + getMsg("VAS_092_InvoicesCount"));
            if (pay) bits.push(pay + " " + getMsg("VAS_092_PaymentsCount"));
            return bits.join(" · ");
        }

        function buildDocumentRow(d) {
            var $tr = $('<div class="MPC-vaspo-tRow MPC-vaspo-tBody"></div>');

            var canOpen = d.TableName && +d.RecordId > 0;
            if (canOpen) {
                $tr.addClass("is-link")
                    .attr("data-open-table", d.TableName)
                    .attr("data-open-id", d.RecordId);
            }

            // Identity: doc number + kind, with the open affordance on the right.
            var $item = $('<span class="MPC-vaspo-itItem MPC-vaspo-docItem"></span>');
            var docIcon = d.Type === "grn" ? "inbox"
                        : (d.Type === "payment" ? "coins" : "doc");
            $item.append(svgIcon(docIcon));
            var $txt = $('<span class="MPC-vaspo-docTxt"></span>');
            $txt.append($('<div class="MPC-vaspo-itName"></div>').text(d.DocumentNo || "—"));
            var sub;
            if (d.Type === "grn") {
                sub = getMsg("VAS_092_GoodsReceipt");
                if (d.LineCount)
                    sub += " · " + d.LineCount + " " + getMsg("VAS_092_LinesCount");
            } else if (d.Type === "payment") {
                // "AP Payment · Discounted Amount: <DiscountAmt>" — the discount
                // taken on the payment (C_Payment.DiscountAmt).
                sub = getMsg("VAS_092_APPayment") + " · " +
                    getMsg("VAS_092_DiscountedAmount") + ": " +
                    formatAmount(+d.DiscountAmt || 0, data.CurSymbol,
                        data.ISO_Code, data.StdPrecision);
            } else {
                sub = getMsg("VAS_092_VendorInvoice");
                if (d.IsPaid) sub += " · " + getMsg("VAS_092_Paid");
            }
            $txt.append($('<div class="MPC-vaspo-itSku"></div>').text(sub));
            $item.append($txt);
            if (canOpen) $item.append(svgIcon("arrowUpRight"));
            $tr.append($item);

            $tr.append($('<span></span>').text(formatDate(d.DocDate) || "—"));
            $tr.append($('<span></span>').append(docStatusPill(d.DocStatus)));

            // Amount: invoices show the grand total; GRNs show the total
            // received value (received qty × order-line price).
            var $amt = $('<span class="ta-r"></span>');
            if (d.Amount !== null && d.Amount !== undefined) {
                $amt.text(formatAmount(+d.Amount || 0, data.CurSymbol,
                    data.ISO_Code, data.StdPrecision));
            } else {
                $amt.text("—");
            }
            $tr.append($amt);

            return $tr;
        }

        // ---------- Landed Cost (table) ---------- //

        var LC_METHODS = {
            "I": { key: "VAS_092_ByValue",    tone: "info"    },
            "Q": { key: "VAS_092_ByQuantity", tone: "success" },
            "W": { key: "VAS_092_ByWeight",   tone: "purple"  },
            "V": { key: "VAS_092_ByVolume",   tone: "warning" },
            "L": { key: "VAS_092_Equally",    tone: "neutral" },
            "C": { key: "VAS_092_ByCosts",    tone: "neutral" }
        };

        function methodLabel(code) {
            var m = LC_METHODS[code];
            if (m) return getMsg(m.key);
            return code ? code : getMsg("VAS_092_NotSet");
        }

        function methodTone(code) {
            var m = LC_METHODS[code];
            return m ? m.tone : "neutral";
        }

        var LC_PER_PAGE = 10;

        // One row per cost component, followed (when available) by the per-line
        // distribution breakdown (C_ExpectedCostDistribution) showing how much of
        // that component was distributed onto each order line.
        function renderLandedCost() {
            var comps = (data && data.LandedCostComponents) || [];
            if (!comps.length) return;

            var $sec = section(getMsg("VAS_092_LandedCost"), {
                summary: buildLandedSummary(comps)
            });

            var $tbl = $('<div class="MPC-vaspo-table MPC-vaspo-ldTable"></div>');
            $sec.append($tbl);
            paintLandedTable($tbl, comps);

            $sec.append(buildLandedNote(comps));
        }

        // (Re)paints the landed-cost table for the current page. Kept separate from
        // renderLandedCost so the pager can repaint just this table without
        // rebuilding the panel. The totals footer always reflects every component,
        // not the visible page — same rule as the line-items table.
        function paintLandedTable($tbl, comps) {
            $tbl.empty();

            var totalPages = Math.max(1, Math.ceil(comps.length / LC_PER_PAGE));
            if (lcPage < 1) lcPage = 1;
            if (lcPage > totalPages) lcPage = totalPages;
            var start = (lcPage - 1) * LC_PER_PAGE;
            var end = Math.min(start + LC_PER_PAGE, comps.length);

            var $h = $('<div class="MPC-vaspo-tRow MPC-vaspo-tHead"></div>');
            // Each column explains itself on hover — "expected vs actual vs
            // variance" is the part of this table people read differently.
            $h.append($('<span></span>').text(getMsg("VAS_092_CostComponent"))
                .attr("title", getMsg("VAS_092_TipComponent")));
            $h.append($('<span></span>').text(getMsg("VAS_092_DistributionMethod"))
                .attr("title", getMsg("VAS_092_TipMethod")));
            $h.append($('<span class="ta-r"></span>').text(getMsg("VAS_092_Expected"))
                .attr("title", getMsg("VAS_092_TipExpected")));
            $h.append($('<span class="ta-r"></span>').text(getMsg("VAS_092_Actual"))
                .attr("title", getMsg("VAS_092_TipActual")));
            $h.append($('<span class="ta-r"></span>').text(getMsg("VAS_092_Variance"))
                .attr("title", getMsg("VAS_092_TipVariance")));
            $tbl.append($h);

            for (var i = start; i < end; i++) {
                $tbl.append(buildComponentRow(comps[i]));
                var $dist = buildDistRows(comps[i]);
                if ($dist) $tbl.append($dist);
            }

            $tbl.append(buildLandedFooter());

            if (totalPages > 1) {
                $tbl.append(buildLandedPager($tbl, comps, start, end, totalPages));
            }
        }

        // "Showing a–b of N" on the left, prev / "page of pages" / next on the
        // right. Prev/next repaint the table in place (paintLandedTable), the same
        // control the line-items table uses.
        function buildLandedPager($tbl, comps, start, end, totalPages) {
            var $pager = $('<div class="MPC-vaspo-pager"></div>');

            $pager.append($('<span class="MPC-vaspo-pagerInfo"></span>').text(
                getMsg("VAS_092_Showing") + " " + (start + 1) + "–" + end + " " +
                getMsg("VAS_092_Of") + " " + comps.length));

            var $nav = $('<div class="MPC-vaspo-pagerNav"></div>');

            var $prev = $('<button type="button" class="MPC-vaspo-pgBtn"></button>')
                .attr("aria-label", getMsg("VAS_092_Prev"))
                .attr("title", getMsg("VAS_092_Prev"));
            $prev.append(svgIcon("chevLeft"));
            if (lcPage <= 1) $prev.prop("disabled", true);
            $prev.on("click", function () {
                if (lcPage > 1) { lcPage--; paintLandedTable($tbl, comps); }
            });

            var $next = $('<button type="button" class="MPC-vaspo-pgBtn"></button>')
                .attr("aria-label", getMsg("VAS_092_Next"))
                .attr("title", getMsg("VAS_092_Next"));
            $next.append(svgIcon("chevRight"));
            if (lcPage >= totalPages) $next.prop("disabled", true);
            $next.on("click", function () {
                if (lcPage < totalPages) { lcPage++; paintLandedTable($tbl, comps); }
            });

            $nav.append($prev);
            $nav.append($('<span class="MPC-vaspo-pagerLabel"></span>').text(
                lcPage + " " + getMsg("VAS_092_Of") + " " + totalPages));
            $nav.append($next);

            $pager.append($nav);
            return $pager;
        }

        function buildLandedSummary(comps) {
            if (!comps.length) return "";
            var seen = {};
            for (var i = 0; i < comps.length; i++) {
                seen[methodLabel(comps[i].DistributionCode)] = true;
            }
            var methods = [];
            for (var k in seen) { if (seen.hasOwnProperty(k)) methods.push(k); }

            var count = comps.length + " " + getMsg("VAS_092_Components");
            if (methods.length === 1) {
                return count + " · " + getMsg("VAS_092_Basis") + ": " + methods[0];
            }
            return count + " · " + getMsg("VAS_092_MixedBasis");
        }

        function buildComponentRow(c) {
            var $tr = $('<div class="MPC-vaspo-tRow MPC-vaspo-tBody"></div>');

            var $name = $('<span class="MPC-vaspo-itItem"></span>');
            var compName = c.ComponentName || getMsg("VAS_092_LandedCost");
            $name.append($('<div class="MPC-vaspo-itName"></div>')
                .text(compName).attr("title", compName));
            if (c.SourceLabel) {
                $name.append($('<div class="MPC-vaspo-itSku"></div>').text(c.SourceLabel));
            }
            $tr.append($name);

            $tr.append($('<span></span>')
                .attr("title", getMsg("VAS_092_TipMethod"))
                .append(pill(methodLabel(c.DistributionCode), methodTone(c.DistributionCode))));

            $tr.append($('<span class="ta-r MPC-vaspo-ldExp"></span>')
                .attr("title", getMsg("VAS_092_TipExpected"))
                .text(formatAmount(+c.ExpectedAmt || 0, data.CurSymbol, data.ISO_Code, data.StdPrecision)));

            var $act = $('<span class="ta-r MPC-vaspo-ldAct"></span>');
            if (c.IsInvoiced) {
                // The tooltip names the invoice the actual came from, so the figure
                // can be traced without leaving the panel.
                var src = [];
                if (c.InvoiceNo) src.push(c.InvoiceNo);
                if (c.InvoiceReference) src.push(c.InvoiceReference);
                var invoiced = formatDate(c.LatestInvoiceDate);
                if (invoiced) src.push(invoiced);
                $act.attr("title", src.length
                    ? getMsg("VAS_092_Invoiced") + ": " + src.join(" · ")
                    : getMsg("VAS_092_TipActual"));
                $act.append($('<span class="MPC-vaspo-ldAmt"></span>').text(
                    formatAmount(+c.ActualAmt || 0, data.CurSymbol, data.ISO_Code, data.StdPrecision)));
                $act.append($('<span class="MPC-vaspo-ldFlag inv"></span>')
                    .text(getMsg("VAS_092_Invoiced")));
            } else {
                $act.addClass("is-pending");
                $act.attr("title", getMsg("VAS_092_TipAwaiting"));
                $act.append($('<span class="MPC-vaspo-ldAmt"></span>').text("—"));
                $act.append($('<span class="MPC-vaspo-ldFlag wait"></span>')
                    .text(getMsg("VAS_092_AwaitingInvoice")));
            }
            $tr.append($act);

            $tr.append(buildVarianceCell(c));
            return $tr;
        }

        // Per-line distribution breakdown for a component: a full-width block of
        // "Line N · label → distributed amount" rows.
        function buildDistRows(c) {
            var lines = (c && c.DistributionLines) || [];
            if (!lines.length) return null;

            var $wrap = $('<div class="MPC-vaspo-ldDist"></div>');
            $wrap.append($('<div class="MPC-vaspo-ldDistCap"></div>').text(getMsg("VAS_092_DistributedAcross")));
            for (var i = 0; i < lines.length; i++) {
                var l = lines[i];
                var $row = $('<div class="MPC-vaspo-ldDistRow"></div>');
                $row.append($('<span class="MPC-vaspo-ldDistItem"></span>')
                    .text(getMsg("VAS_092_Line") + " " + l.LineNo + " · " + (l.LineLabel || "")));
                $row.append($('<span class="MPC-vaspo-ldDistAmt"></span>').text(
                    formatAmount(+l.Amt || 0, data.CurSymbol, data.ISO_Code, data.StdPrecision)));
                $wrap.append($row);
            }
            return $wrap;
        }

        function buildVarianceCell(c) {
            var $v = $('<span class="ta-r MPC-vaspo-ldVar"></span>');
            var amt = formatAmount(Math.abs(+c.VarianceAmt || 0),
                data.CurSymbol, data.ISO_Code, data.StdPrecision);
            // The sign is easy to misread, so the tooltip spells the direction out.
            if (c.VarianceStatus === "over") {
                $v.addClass("over").text("+" + amt)
                  .attr("title", getMsg("VAS_092_TipOver") + " " + amt);
            } else if (c.VarianceStatus === "under") {
                $v.addClass("under").text("−" + amt)
                  .attr("title", getMsg("VAS_092_TipUnder") + " " + amt);
            } else if (c.VarianceStatus === "on_budget") {
                $v.addClass("flat").text(getMsg("VAS_092_OnBudget"))
                  .attr("title", getMsg("VAS_092_TipOnBudget"));
            } else {
                $v.addClass("flat").text("—")
                  .attr("title", getMsg("VAS_092_TipVariance"));
            }
            return $v;
        }

        function buildLandedFooter() {
            var $foot = $('<div class="MPC-vaspo-tFoot MPC-vaspo-ldFoot"></div>');
            $foot.append(buildLandedTotal(getMsg("VAS_092_ExpectedLandedCost"),
                formatAmount(+data.ExpectedLandedCost || 0, data.CurSymbol, data.ISO_Code, data.StdPrecision),
                false, false));
            $foot.append(buildLandedTotal(getMsg("VAS_092_ActualToDate"),
                formatAmount(+data.ActualToDate || 0, data.CurSymbol, data.ISO_Code, data.StdPrecision),
                false, false));
            $foot.append(buildLandedTotal(getMsg("VAS_092_OpenNotInvoiced"),
                formatAmount(+data.OpenNotInvoiced || 0, data.CurSymbol, data.ISO_Code, data.StdPrecision),
                false, true));
            $foot.append(buildLandedTotal(getMsg("VAS_092_LandedValue"),
                formatAmount(+data.LandedValue || 0, data.CurSymbol, data.ISO_Code, data.StdPrecision),
                true, false));
            return $foot;
        }

        function buildLandedTotal(label, value, isGrand, isWarn) {
            var $bit = $('<span class="MPC-vaspo-tf MPC-vaspo-lf"></span>');
            if (isGrand) $bit.addClass("is-grand");
            $bit.append(document.createTextNode(label));
            var $b = $('<b></b>').text(value);
            if (isWarn) $b.addClass("warn");
            $bit.append($b);
            return $bit;
        }

        function buildLandedNote(comps) {
            var $note = $('<div class="MPC-vaspo-note"></div>');
            $note.append(svgIcon("info"));

            var invoiced = data.InvoicedComponentCount || 0;
            var total = data.LandedComponentCount || comps.length;
            var text = getMsg("VAS_092_LandedMethodology") + " " +
                invoiced + " " + getMsg("VAS_092_Of") + " " + total + " " +
                getMsg("VAS_092_ComponentsInvoiced") + ".";
            $note.append($('<span></span>').text(text));
            return $note;
        }

        // ---------- Bottom (Notes stacked above Activity) ---------- //

        // Notes and Activity render one below the other (Notes then Activity),
        // each full width.
        function renderBottom() {
            var notes = (data && data.Notes) || [];
            var activity = (data && data.Activity) || [];
            if (!notes.length && !activity.length) return;

            var $stack = $('<div class="MPC-vaspo-bottom"></div>');
            $body.append($stack);

            if (notes.length) renderNotes($stack, notes);
            // E-mails are not given a section of their own: the activity feed
            // already lists them (type "email", with the same subject / recipient
            // row and click-to-open body), so a separate Emails section repeated
            // the same records twice in the panel.
            if (activity.length) renderActivity($stack, activity);
        }

        // ---------- Notes ---------- //

        // Order header note + per-line notes, mirroring the Sales Order overview.
        function renderNotes($parent, notes) {
            var $sec = section(getMsg("VAS_092_Notes"), {
                summary: notes.length + " " + getMsg("VAS_092_NotesCount")
            }, $parent);
            var $card = $('<div class="MPC-vaspo-textCard"></div>');
            for (var i = 0; i < notes.length; i++) {
                var t = (notes[i].Text || "").trim();
                if (t) $card.append($('<p></p>').text(t));
            }
            $sec.append($card);
        }

        // ---------- Recent Activity (typed feed) ---------- //

        var ACT_TYPES = {
            note:     { tone: "info",    icon: "mail",  tagKey: "VAS_092_TagNote",     titleKey: null },
            email:    { tone: "purple",  icon: "mail",  tagKey: "VAS_092_TagEmail",    titleKey: null },
            grn:      { tone: "success", icon: "inbox", tagKey: "VAS_092_TagGRN",      titleKey: "VAS_092_ActGRN" },
            invoice:  { tone: "info",    icon: "doc",   tagKey: "VAS_092_TagInvoice",  titleKey: "VAS_092_ActInvoice" },
            payment:  { tone: "success", icon: "coins", tagKey: "VAS_092_TagPayment",  titleKey: "VAS_092_ActPayment" },
            approval: { tone: "purple",  icon: "check", tagKey: "VAS_092_TagApproval", titleKey: "VAS_092_ActApproval" },
            created:  { tone: "neutral", icon: "doc",   tagKey: "VAS_092_TagCreated",  titleKey: "VAS_092_ActCreated" }
        };

        function renderActivity($parent, activity) {
            var $sec = section(getMsg("VAS_092_RecentActivity"), {
                summary: activity.length + " " + getMsg("VAS_092_Updates")
            }, $parent);

            var $card = $('<div class="MPC-vaspo-actList"></div>');
            for (var i = 0; i < activity.length; i++) {
                var a = activity[i];
                $card.append(activityRow(a));
                // An e-mail's body is heavy — it stays collapsed under its row and
                // opens only when the reader asks for it.
                var $body = activityBody(a);
                if ($body) $card.append($body);
            }
            $sec.append($card);
        }

        function activityRow(a) {
            var meta = ACT_TYPES[a.Type] || ACT_TYPES.note;

            var $row = $('<div class="MPC-vaspo-actRow"></div>');
            $row.append(activityTag(meta));

            // Every activity row shares one symmetric layout: tag | title |
            // right-aligned timestamp · author. For a note/chat the title is the
            // comment text; a title tooltip keeps a long comment fully readable.
            var title = activityTitle(a, meta);
            var $title = $('<span class="MPC-vaspo-actTitle"></span>');
            $title.append($('<span class="MPC-vaspo-actLead"></span>')
                .text(title).attr("title", title));

            // An e-mail also names its recipient, under the subject.
            if (a.Type === "email" && a.MailTo) {
                var to = getMsg("VAS_092_MailTo") + " " + a.MailTo;
                $title.append($('<small class="MPC-vaspo-actSub"></small>')
                    .text(to).attr("title", to));
            }
            $row.append($title);

            var when = formatDateTime(a.Created);
            if (a.UserName) when += " · " + a.UserName;
            $row.append($('<span class="MPC-vaspo-actWhen"></span>').text(when));

            // Rows carrying a body are clickable; the caret shows the state.
            if (hasActivityBody(a)) {
                $row.addClass("is-openable");
                $row.attr("title", getMsg("VAS_092_ShowMailBody"));
                var $caret = $('<span class="MPC-vaspo-actCaret"></span>').append(svgIcon("chevRight"));
                $row.append($caret);
                $row.on("click", function () {
                    var $panel = $row.next(".MPC-vaspo-actBody");
                    if (!$panel.length) return;
                    var nowOpen = !$row.hasClass("is-open");
                    $row.toggleClass("is-open", nowOpen)
                        .attr("title", nowOpen ? getMsg("VAS_092_HideMailBody")
                                               : getMsg("VAS_092_ShowMailBody"));
                    $panel.toggle(nowOpen);
                });
            }
            return $row;
        }

        function hasActivityBody(a) {
            return a && a.Type === "email" && !!(a.Body && String(a.Body).trim());
        }

        // The e-mail body, collapsed beneath its activity row.
        function activityBody(a) {
            if (!hasActivityBody(a)) return null;

            var $panel = $('<div class="MPC-vaspo-actBody" style="display:none;"></div>');
            if (a.MailFrom) {
                $panel.append($('<div class="MPC-vaspo-actMeta"></div>')
                    .text(getMsg("VAS_092_MailFrom") + " " + a.MailFrom));
            }
            $panel.append($('<p></p>').text(String(a.Body).trim()));
            return $panel;
        }

        function activityTag(meta) {
            var $t = $('<span class="MPC-vaspo-actTag"></span>').addClass("tone-" + meta.tone);
            if (meta.icon) $t.append(svgIcon(meta.icon));
            $t.append($('<span></span>').text(getMsg(meta.tagKey)));
            return $t;
        }

        function activityTitle(a, meta) {
            // Free-text types (note / e-mail) headline with their own text; an
            // untitled one falls back to what its tag says it is.
            if (!meta.titleKey) return a.Text || getMsg(meta.tagKey);

            var s = getMsg(meta.titleKey);
            if (a.Type === "grn" && a.Count > 0) {
                s += " · " + a.Count + " " + getMsg("VAS_092_Lines");
            }
            if (a.DocumentNo) s += " (" + a.DocumentNo + ")";
            return s;
        }

        // ----------------------------------------------------------------- //
        //  Events / record navigation                                        //
        // ----------------------------------------------------------------- //

        function bindEvents() {
            // Open a linked origin record from a Generated From chip.
            $root.on("click", ".MPC-vaspo-chip.is-link, .is-link[data-open-table]", function (e) {
                e.preventDefault();
                openRecord($(this).attr("data-open-table"), $(this).attr("data-open-id"),
                    $(this).attr("data-open-sotrx") === "Y");
            });
        }

        // Open the record's window filtered to that row, using the platform's
        // zoom API (the same pattern as VAS_105_AccountRightPanel): resolve the
        // table's default zoom window, then start it with an equal-query on the
        // table's key column (TableName_ID). Degrades to a toast so a click
        // never throws.
        function openRecord(tableName, recordId, isSOTrx) {
            if (!tableName || !recordId || +recordId <= 0 || !window.VIS) return;
            try {
                var windowId = 0;
                if (VIS.ZoomTarget && typeof VIS.ZoomTarget.getZoomAD_Window_ID === "function") {
                    // The 4th arg (IsSOTrx) picks the sales vs purchase window for
                    // dual-purpose tables like C_Order — true opens the Sales Order
                    // window, false the Purchase Order window.
                    windowId = VIS.ZoomTarget.getZoomAD_Window_ID(tableName, 0, null, !!isSOTrx) || 0;
                }
                if (windowId > 0 && VIS.viewManager && typeof VIS.viewManager.startWindow === "function") {
                    var zoomQuery = VIS.Query.prototype.getEqualQuery(tableName + "_ID", +recordId);
                    VIS.viewManager.startWindow(windowId, zoomQuery);
                    return;
                }
            } catch (e) { console.log(e); }
            toast(getMsg("VAS_092_OpenRecord") + " " + tableName + " #" + recordId, false);
        }

        function toast(message, isError) {
            var $t = $('<div class="MPC-vaspo-toast"></div>').addClass(isError ? "err" : "ok").text(message);
            $root.append($t);
            setTimeout(function () { $t.addClass("show"); }, 10);
            setTimeout(function () { $t.removeClass("show"); setTimeout(function () { $t.remove(); }, 300); }, 3200);
        }

        // ----------------------------------------------------------------- //
        //  Icon helpers                                                      //
        // ----------------------------------------------------------------- //

        var SVG_ICONS = {
            pin:      '<svg width="13" height="13" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="1.9" stroke-linecap="round" stroke-linejoin="round"><path d="M20 10c0 6-8 12-8 12s-8-6-8-12a8 8 0 0 1 16 0Z"/><circle cx="12" cy="10" r="3"/></svg>',
            user:     '<svg width="13" height="13" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="1.9" stroke-linecap="round" stroke-linejoin="round"><path d="M20 21v-2a4 4 0 0 0-4-4H8a4 4 0 0 0-4 4v2"/><circle cx="12" cy="7" r="4"/></svg>',
            phone:    '<svg width="13" height="13" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="1.9" stroke-linecap="round" stroke-linejoin="round"><path d="M22 16.92v3a2 2 0 0 1-2.18 2 19.79 19.79 0 0 1-8.63-3.07 19.5 19.5 0 0 1-6-6 19.79 19.79 0 0 1-3.07-8.67A2 2 0 0 1 4.11 2h3a2 2 0 0 1 2 1.72c.13.96.36 1.9.7 2.81a2 2 0 0 1-.45 2.11L8.09 9.91a16 16 0 0 0 6 6l1.27-1.27a2 2 0 0 1 2.11-.45c.91.34 1.85.57 2.81.7A2 2 0 0 1 22 16.92Z"/></svg>',
            mail:     '<svg width="13" height="13" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="1.9" stroke-linecap="round" stroke-linejoin="round"><rect x="3" y="5" width="18" height="14" rx="2"/><path d="m3 7 9 6 9-6"/></svg>',
            box:      '<svg width="20" height="20" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="1.9" stroke-linecap="round" stroke-linejoin="round"><path d="M21 8 12 3 3 8v8l9 5 9-5Z"/><path d="m3 8 9 5 9-5"/><path d="M12 13v8"/></svg>',
            coins:    '<svg width="20" height="20" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="1.9" stroke-linecap="round" stroke-linejoin="round"><circle cx="8" cy="8" r="5"/><path d="M14.5 4.2a5 5 0 0 1 0 15.6"/><path d="M7 18.7a5 5 0 0 0 6 0"/></svg>',
            info:     '<svg width="14" height="14" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2" stroke-linecap="round" stroke-linejoin="round"><circle cx="12" cy="12" r="10"/><path d="M12 16v-4"/><path d="M12 8h.01"/></svg>',
            check:    '<svg width="15" height="15" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="3" stroke-linecap="round" stroke-linejoin="round"><path d="M20 6 9 17l-5-5"/></svg>',
            chevUp:   '<svg width="12" height="12" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2.6" stroke-linecap="round" stroke-linejoin="round"><path d="m18 15-6-6-6 6"/></svg>',
            doc:      '<svg width="14" height="14" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="1.9" stroke-linecap="round" stroke-linejoin="round"><path d="M14 2H6a2 2 0 0 0-2 2v16a2 2 0 0 0 2 2h12a2 2 0 0 0 2-2V8Z"/><path d="M14 2v6h6"/><path d="M8 13h8"/><path d="M8 17h8"/></svg>',
            clipboardCheck: '<svg width="14" height="14" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="1.9" stroke-linecap="round" stroke-linejoin="round"><rect x="8" y="2" width="8" height="4" rx="1"/><path d="M16 4h2a2 2 0 0 1 2 2v14a2 2 0 0 1-2 2H6a2 2 0 0 1-2-2V6a2 2 0 0 1 2-2h2"/><path d="m9 14 2 2 4-4"/></svg>',
            factory:  '<svg width="14" height="14" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="1.9" stroke-linecap="round" stroke-linejoin="round"><path d="M2 20V9l6 4V9l6 4V9l6 4v7Z"/><path d="M2 20h20"/><path d="M7 20v-4"/><path d="M12 20v-4"/><path d="M17 20v-4"/></svg>',
            pencil:   '<svg width="14" height="14" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="1.9" stroke-linecap="round" stroke-linejoin="round"><path d="M12 20h9"/><path d="M16.5 3.5a2.12 2.12 0 0 1 3 3L7 19l-4 1 1-4Z"/></svg>',
            calendar: '<svg width="16" height="16" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="1.9" stroke-linecap="round" stroke-linejoin="round"><rect x="3" y="4" width="18" height="18" rx="2"/><path d="M16 2v4"/><path d="M8 2v4"/><path d="M3 10h18"/></svg>',
            inbox:    '<svg width="16" height="16" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="1.9" stroke-linecap="round" stroke-linejoin="round"><path d="M22 12h-6l-2 3h-4l-2-3H2"/><path d="M5.45 5.11 2 12v6a2 2 0 0 0 2 2h16a2 2 0 0 0 2-2v-6l-3.45-6.89A2 2 0 0 0 16.76 4H7.24a2 2 0 0 0-1.79 1.11Z"/></svg>',
            arrowUpRight: '<svg width="11" height="11" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2.2" stroke-linecap="round" stroke-linejoin="round"><path d="M7 17 17 7M7 7h10v10"/></svg>',
            chevLeft: '<svg width="14" height="14" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2.6" stroke-linecap="round" stroke-linejoin="round"><polyline points="15 18 9 12 15 6"/></svg>',
            chevRight: '<svg width="14" height="14" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2.6" stroke-linecap="round" stroke-linejoin="round"><polyline points="9 18 15 12 9 6"/></svg>',
            alert: '<svg width="16" height="16" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="1.9" stroke-linecap="round" stroke-linejoin="round"><path d="M10.29 3.86 1.82 18a2 2 0 0 0 1.71 3h16.94a2 2 0 0 0 1.71-3L13.71 3.86a2 2 0 0 0-3.42 0Z"/><path d="M12 9v4"/><path d="M12 17h.01"/></svg>',
            history: '<svg width="12" height="12" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2" stroke-linecap="round" stroke-linejoin="round"><path d="M3 12a9 9 0 1 0 3-6.7L3 8"/><path d="M3 3v5h5"/><path d="M12 7v5l3 2"/></svg>'
        };

        function svgIcon(name) {
            var $wrap = $('<span class="MPC-vaspo-ic"></span>');
            $wrap[0].innerHTML = SVG_ICONS[name] || "";
            return $wrap;
        }

        // ----------------------------------------------------------------- //
        //  Formatting helpers                                                //
        // ----------------------------------------------------------------- //

        function formatNumber(value, precision) {
            var p = (precision >= 0) ? precision : 0;
            return (+value || 0).toLocaleString(window.navigator.language, {
                minimumFractionDigits: p,
                maximumFractionDigits: p
            });
        }

        function formatAmount(value, symbol, iso, precision) {
            var sign = value < 0 ? "-" : "";
            var abs = Math.abs(value);
            var cur = symbol || iso || "";
            var p = (precision >= 0) ? precision : 2;
            var formatted = abs.toLocaleString(window.navigator.language, {
                minimumFractionDigits: p,
                maximumFractionDigits: p
            });
            return sign + (cur ? cur + " " : "") + formatted;
        }

        // Parses a .NET/Newtonsoft DB value into a Date.
        //
        // asUtc = true  → for genuine *timestamps* (Created, activity, history,
        //   completion time). The DB stores these in UTC and Newtonsoft emits no
        //   timezone designator (e.g. "2026-07-01T10:00:00"), which the browser
        //   would otherwise read as local. We tag it "Z" so toLocale* renders it
        //   in the viewer's own zone.
        // asUtc = false → for *date-only* fields (Ordered / Promised / Invoice /
        //   receipt dates). These carry no meaningful time-of-day, so we parse the
        //   wall-clock value as-is and never shift it — the calendar day shown
        //   always matches the day stored, regardless of the viewer's zone.
        // Strings that already carry a "Z" or ±hh:mm offset are left untouched.
        function parseDbDate(value, asUtc) {
            if (!value) return null;
            if (value instanceof Date) return isNaN(value.getTime()) ? null : value;
            var s = String(value);
            var hasTz = /(z|[+-]\d{2}:?\d{2})$/i.test(s);
            var isDateTime = /^\d{4}-\d{2}-\d{2}[T ]\d{2}:\d{2}/.test(s);
            if (asUtc && isDateTime && !hasTz) {
                s = s.replace(" ", "T") + "Z";
            } else if (!asUtc && isDateTime) {
                // Keep the calendar date: drop any timezone marker and parse the
                // date/time as local so no zone conversion can roll the day over.
                s = s.replace(" ", "T").replace(/(z|[+-]\d{2}:?\d{2})$/i, "");
            }
            var d = new Date(s);
            return isNaN(d.getTime()) ? null : d;
        }

        function formatDate(value) {
            var d = parseDbDate(value, false);
            if (!d) return "";
            try {
                return d.toLocaleDateString(window.navigator.language, {
                    year: "numeric", month: "short", day: "2-digit"
                });
            } catch (e) {
                return d.toDateString();
            }
        }

        function formatDateTime(value) {
            var d = parseDbDate(value, true);
            if (!d) return "";
            try {
                var datePart = d.toLocaleDateString(window.navigator.language, {
                    month: "short", day: "2-digit"
                });
                var timePart = d.toLocaleTimeString(window.navigator.language, {
                    hour: "2-digit", minute: "2-digit"
                });
                return datePart + ", " + timePart;
            } catch (e) {
                return d.toString();
            }
        }

        this.getRoot = function () {
            return $root;
        };
    };

    VAS.VAS_092_OverviewPurchaseOrder.prototype.startPanel = function (windowNo, curTab) {
        this.windowNo = windowNo;
        this.curTab = curTab;
        if (curTab && typeof curTab.getAD_Table_ID === "function") {
            this.table_ID = curTab.getAD_Table_ID();
        }
        this.init();
    };

    /* Update tab panel based on selected record */
    VAS.VAS_092_OverviewPurchaseOrder.prototype.refreshPanelData = function (recordID, selectedRow) {
        if (selectedRow == undefined || recordID <= 0) {
            this.clear();
            return;
        }
        this.record_ID = recordID;
        this.selectedRow = selectedRow;
        this.fetchData(recordID);
    };

    /* Set width as per window width */
    VAS.VAS_092_OverviewPurchaseOrder.prototype.sizeChanged = function (width) {
        this.panelWidth = width;
    };

    /* Release variables from memory */
    VAS.VAS_092_OverviewPurchaseOrder.prototype.dispose = function () {
        this.record_ID = 0;
        this.table_ID = 0;
        this.windowNo = 0;
        this.curTab = null;
        this.selectedRow = null;
        this.panelWidth = null;
    };

})(VAS, jQuery);
