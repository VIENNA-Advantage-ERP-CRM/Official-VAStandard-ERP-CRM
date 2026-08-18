/************************************************************
 * Module Name    : VAS
 * Purpose        : Payment Overview tab panel for the selected C_Payment record.
 *
 *                  Composition follows the approved reference layout
 *                  (ap_payment_overview_panel.html), section for section:
 *                    header -> action bar -> info trio -> payment lifecycle ->
 *                    payment details -> advance banner -> allocations ->
 *                    approval -> posted entry.
 *                  Apply advance is offered by the advance banner only, never
 *                  duplicated into the action bar.
 *                  The one deliberate difference: the reference's four-card
 *                  lifecycle stepper is NOT drawn - the Payment lifecycle rail
 *                  that sits in its place already carries those stages.
 *
 *                  The allocations are two distinct datasets, never merged:
 *                  "Payment Allocate" is what is PREPARED on the payment
 *                  (C_PaymentAllocate); "Allocation Detail" is the COMPLETED
 *                  history (C_AllocationHdr + C_AllocationLine).
 *
 *                  The block cards and the print / e-mail path follow
 *                  VAS_189_ARInvoiceDetailPanel: Send runs through the shared
 *                  VAS_SentEmailDoc / VA112 share panel and Download PDF through
 *                  JsonData/GeneratePrint, both off the hosting tab's own print
 *                  process.
 *
 *                  Data comes from
 *                  VAS_191_APPaymentDetailPanel/GetPaymentOverview; the two
 *                  document actions POST to CompletePayment / ReversePayment and
 *                  run the standard document engine server-side.
 *
 *                  The panel serves BOTH directions of C_Payment: it reads
 *                  IsReceipt and labels itself Payment or Receipt, so no AR / AP
 *                  wording is hard-coded and the same panel can be hung on either
 *                  window.
 *
 *                  There is deliberately NO generic "Allocate" button. Allocation
 *                  runs through Apply Advance, which opens the STANDARD Payment
 *                  Allocation form (AD_Form VAdvantage.Apps.AForms.VAllocation);
 *                  no second allocation UI is built here.
 *
 *                  Sections are declared in a registry - key, visibility
 *                  condition, renderer - and drawn by iterating it, each behind
 *                  its own guard. A section whose data is absent is not drawn at
 *                  all: no empty tables, no "no data" rows. An unposted payment
 *                  has no Posted Entry section, a payment with nothing prepared
 *                  has no Payment Allocate section, and one that has never been
 *                  allocated has no Allocation Detail section. Payment Allocate
 *                  is additionally hidden once the payment is Completed, Closed,
 *                  Reversed or Voided (DocStatus CO / CL / RE / VO) - the
 *                  prepared rows are spent by then and Allocation Detail is the
 *                  section that tells the story.
 *
 *                  The panel chrome (shell width, collapse strip, header, close
 *                  button, panel switcher) belongs to the VIS tab-panel host, not
 *                  to this file.
 *
 *                  All on-screen strings resolve through VIS.Msg.getMsg with an
 *                  English fallback, so an unseeded AD_Message key never renders
 *                  as a raw key.
 * Class Used     : VAS.VAS_191_APPaymentDetailPanel
 * Chronological development:
 *   VAI145   2026-08-17  Created.
 *   VAI145   2026-08-18  Payment Allocate section hidden for DocStatus
 *                        CO / CL / RE / VO.
 *
 * -- Labels / Message Keys ---------------------------------------------------
 *  Panel, header and summary cards
 *   No payment selected                   | VAS_191_NoData
 *   Payment overview (title fallback -    | VAS_191_PaymentOverview
 *     the C_DocType name is used when set)|
 *   Paid / Received                       | VAS_191_Paid / VAS_191_Received
 *   Paid {0} / Received {0}               | VAS_191_PaidOn / VAS_191_ReceivedOn
 *   Business Partner                      | VAS_191_BusinessPartner
 *     (no partner: bank account instead)  | VAS_191_PaidFrom / VAS_191_ReceivedIn
 *   Fully allocated / Unallocated         | VAS_191_FullyAllocated / VAS_191_Unallocated
 *   Partially allocated                   | VAS_191_PartiallyAllocated
 *   Unallocated amount                    | VAS_191_UnallocatedAmount
 *   Available to apply                    | VAS_191_AvailableToApply
 *   Payment fully allocated               | VAS_191_PaymentFullyAllocated
 *   Could not load the payment details.   | VAS_191_LoadFailed
 *
 *  Advance banner (the only place Apply advance is offered)
 *   {0} held as advance                   | VAS_191_HeldAsAdvance
 *   Unapplied from this payment ...       | VAS_191_AdvanceHint
 *     (no partner on the document)        | VAS_191_AdvanceHintNoBP
 *   Apply advance                         | VAS_191_ApplyAdvance
 *
 *  Approval
 *   Approval / Approved / Not approved    | VAS_191_Approval / VAS_191_Approved / VAS_191_NotApproved
 *   Entered / Reviewed & approved         | VAS_191_Entered / VAS_191_ReviewedApproved
 *   Posted to ledger                      | VAS_191_PostedToLedger
 *
 *  Actions
 *   Complete / Reverse                    | VAS_191_Complete / VAS_191_Reverse
 *   Send payment / Download PDF           | VAS_191_SendPayment / VAS_191_DownloadPDF
 *   View ledger entry                     | VAS_191_ViewLedgerEntry
 *   Reverse this payment? ...             | VAS_191_ConfirmReverse
 *   Confirm                               | VAS_191_Confirm
 *   The action could not be completed.    | VAS_191_ActionFailed
 *   Payment completed / Payment reversed  | VAS_191_PaymentCompleted / VAS_191_PaymentReversed
 *   Payment Allocation form not available | VAS_191_AllocationFormMissing
 *
 *  Lifecycle
 *   Payment lifecycle                     | VAS_191_Lifecycle
 *   Stage {0} of {1} / Complete           | VAS_191_StageOf / VAS_191_LifecycleComplete
 *   Created / Completed                   | VAS_191_Created / VAS_191_Completed
 *   Allocated / Reconciled                | VAS_191_Allocated / VAS_191_Reconciled
 *
 *  Payment details
 *   Payment details                       | VAS_191_PaymentDetails
 *   Document no. / Document type          | VAS_191_DocumentNo / VAS_191_DocumentType
 *   Document status / Posted status       | VAS_191_DocumentStatus / VAS_191_PostedStatus
 *   Payment date / Accounting date        | VAS_191_PaymentDate / VAS_191_AccountingDate
 *   Payment method / Tender type          | VAS_191_PaymentMethod / VAS_191_TenderType
 *   Bank account / Currency               | VAS_191_BankAccount / VAS_191_Currency
 *   Organization / Prepayment             | VAS_191_Organization / VAS_191_Prepayment
 *   Execution status / Reference          | VAS_191_ExecutionStatus / VAS_191_Reference
 *   Related invoice / Charge              | VAS_191_RelatedInvoice / VAS_191_Charge
 *   Description                           | VAS_191_Description
 *   Yes / No                              | VAS_191_Yes / VAS_191_No
 *   VAS_191_CheckNo                       | Check No.
 *  Payment Allocate
 *   Payment Allocate                      | VAS_191_PaymentAllocate
 *   Invoice / GL journal                  | VAS_191_InvoiceGLJournal
 *   Schedule / Amount                     | VAS_191_Schedule / VAS_191_Amount
 *   Discount / Write-off                  | VAS_191_Discount / VAS_191_WriteOff
 *   Due {0}                               | VAS_191_Due
 *   {0} entries                           | VAS_191_EntryCount
 *   Total applied                         | VAS_191_TotalApplied
 *   Date                                  | VAS_191_SchDate
 *  Allocation Detail
 *   Allocation Detail                     | VAS_191_AllocationDetail
 *   {0} allocation(s)                     | VAS_191_AllocationCount
 *   Reference / Allocation No.            | VAS_191_Reference / VAS_191_AllocationNo
 *   Date / Applied to                     | VAS_191_Date / VAS_191_AppliedTo
 *   Invoice / GL journal / Payment        | VAS_191_Invoice / VAS_191_GLJournal / VAS_191_PaymentRef
 *   reconciled / unreconciled             | VAS_191_IsReconciled / VAS_191_Unreconciled
 *   Receipt allocation / Payment allocation | VAS_191_ReceiptAllocation / VAS_191_PaymentAllocation
 *   Allocated {0} - Discount {1} - Write-off {2} | VAS_191_AllocationFooter
 *
 *  Posted entry
 *   Posted Entry / Posted                 | VAS_191_PostedEntry / VAS_191_Posted
 *   Account / Debit / Credit              | VAS_191_Account / VAS_191_Debit / VAS_191_Credit
 *   Trx organization                      | VAS_191_TrxOrganization
 *   Total                                 | VAS_191_Total
 *   Period {0} / Posting date {0}         | VAS_191_Period / VAS_191_PostingDate
 *   Balanced - Dr = Cr                    | VAS_191_Balanced
 *
 *  Pager
 *   Previous page / Next page / of        | VAS_191_Previous / VAS_191_Next / VAS_191_Of
 * ---------------------------------------------------------------------------
 ***********************************************************/
; VAS = window.VAS || {};
; (function (VAS, $) {

    // True when the tab sits on a row that has not been saved yet - whether it
    // came from New Record or from Copy Record. The authority is the GRID
    // TABLE's insert flag: GridTab does not expose it, it only holds the table
    // as .gridTable, so asking the tab itself always answers "no". The record id
    // cannot answer it either: a copied row still carries the SOURCE record's
    // key.
    function isTabInserting(curTab) {
        if (!curTab) return false;
        try {
            if (curTab.gridTable && typeof curTab.gridTable.getIsInserting === "function"
                && curTab.gridTable.getIsInserting()) {
                return true;
            }
        } catch (e) { }

        var probes = ["getIsInserting", "isInserting", "getIsNew", "isNew"];
        for (var i = 0; i < probes.length; i++) {
            try {
                if (typeof curTab[probes[i]] === "function" && curTab[probes[i]]()) return true;
            } catch (e2) { }
        }
        return false;
    }

    VAS.VAS_191_APPaymentDetailPanel = function () {
        this.record_ID = 0;
        this.table_ID = 0;
        this.windowNo = 0;
        this.curTab = null;
        this.selectedRow = null;
        this.panelWidth;

        var $self = this;
        var $root, $busy, $body, $emptyState;
        var data = null;

        var CLS = "vas_191-";

        /* The C_Payment_ID the panel is showing OR loading. 0 = nothing. */
        var shownRecordId = 0;

        /* How long refreshPanelData holds before it fetches. On New Record /
           Copy Record the framework can call it BEFORE GridTable raises its
           insert flag, so asking at that instant answers "no". Asking again
           after this pause gets the truth, and it collapses a burst of
           arrow-key row changes into one request. */
        var REFRESH_DELAY_MS = 150;
        /* Raised by every fetch, scheduled fetch and clear. A reply carrying a
           stale token belongs to a payment the panel has already left, so it is
           dropped instead of painting over the newer one. */
        var fetchToken = 0;
        var pendingFetch = null;
        /* A request is on the wire. Together with pendingFetch and shownRecordId
           this is what lets the panel tell "already loading this record" from
           "idle", so the two host entry points cannot each start their own load
           for the same selection. */
        var inFlight = false;

        /* Per-section page state, keyed by section key. Paging one section never
           touches another, and a record change resets every one of them. */
        var pages = {};
        var ALLOCATE_PER_PAGE = 8;
        var DETAIL_PER_PAGE = 5;
        /* Posted entry pages on the SERVER, so this must match the page size the
           initial payload was built with. */
        var JOURNAL_PER_PAGE = 10;

        /* Raised when Apply Advance opened the standard Allocation form. That
           form is a sibling frame, not a child window, so there is no completion
           callback to hook: the panel re-reads its data the next time the user
           comes back to it (pointer or focus), and the platform Refresh button
           and record navigation still work as usual. */
        var allocationFormOpened = false;

        /* Zoom targets. Ids differ per environment, names do not: each id is
           resolved from the name at runtime by VAS.ZoomUtil and cached there. */
        var PAYMENT_WINDOW_AR = "VAS_ARReceipt";
        var PAYMENT_WINDOW_AP = "VAS_APPayment";
        var PAYMENT_WINDOW_AP_OLD = "Payment";
        var INVOICE_WINDOW_AR = "VAS_ARInvoice";
        var INVOICE_WINDOW_AR_OLD = "Invoice (Customer)";
        var INVOICE_WINDOW_AP = "VAS_APInvoice";
        var INVOICE_WINDOW_AP_OLD = "Invoice (Vendor)";
        /* C_Invoice.IsExpenseInvoice = 'Y' records are maintained in their own
           window, not in the AR / AP invoice one. */
        var INVOICE_WINDOW_EXPENSE = "VAS_ExpenseInvoice";
        var INVOICE_WINDOW_APExp = "VAS_ExpenseInvoice";
        var ALLOCATION_WINDOW = "VAS_ViewAllocation";
        var JOURNAL_WINDOW = "VAS_GLJournal";
        var JOURNAL_WINDOW_OLD = "GL Journal";

        /* ---------------------------------------------------------------- */
        /*  Icons - inline SVG, never an icon-font class: the host shell may */
        /*  not load the font, which would leave blank boxes in the panel.   */
        /* ---------------------------------------------------------------- */
        var SVG = {
            check: '<svg viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="3" stroke-linecap="round" stroke-linejoin="round"><polyline points="20 6 9 17 4 12"/></svg>',
            cross: '<svg viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="3" stroke-linecap="round" stroke-linejoin="round"><line x1="18" y1="6" x2="6" y2="18"/><line x1="6" y1="6" x2="18" y2="18"/></svg>',
            invoice: '<svg viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2" stroke-linecap="round" stroke-linejoin="round"><path d="M14 2H6a2 2 0 0 0-2 2v16a2 2 0 0 0 2 2h12a2 2 0 0 0 2-2V8z"/><polyline points="14 2 14 8 20 8"/><line x1="8" y1="13" x2="16" y2="13"/><line x1="8" y1="17" x2="13" y2="17"/></svg>',
            journal: '<svg viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2" stroke-linecap="round" stroke-linejoin="round"><path d="M4 19.5A2.5 2.5 0 0 1 6.5 17H20"/><path d="M6.5 2H20v20H6.5A2.5 2.5 0 0 1 4 19.5v-15A2.5 2.5 0 0 1 6.5 2z"/></svg>',
            payment: '<svg viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2" stroke-linecap="round" stroke-linejoin="round"><rect x="2" y="5" width="20" height="14" rx="2"/><line x1="2" y1="10" x2="22" y2="10"/></svg>',
            wallet: '<svg viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2" stroke-linecap="round" stroke-linejoin="round"><path d="M20 12V8H6a2 2 0 0 1 0-4h12v4"/><path d="M4 6v12a2 2 0 0 0 2 2h14v-4"/><path d="M18 12a2 2 0 0 0 0 4h4v-4z"/></svg>',
            undo: '<svg viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2" stroke-linecap="round" stroke-linejoin="round"><polyline points="1 4 1 10 7 10"/><path d="M3.51 15a9 9 0 1 0 2.13-9.36L1 10"/></svg>',
            send: '<svg viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2" stroke-linecap="round" stroke-linejoin="round"><line x1="22" y1="2" x2="11" y2="13"/><polygon points="22 2 15 22 11 13 2 9 22 2"/></svg>',
            download: '<svg viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2" stroke-linecap="round" stroke-linejoin="round"><path d="M21 15v4a2 2 0 0 1-2 2H5a2 2 0 0 1-2-2v-4"/><polyline points="7 10 12 15 17 10"/><line x1="12" y1="15" x2="12" y2="3"/></svg>',
            book: '<svg viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2" stroke-linecap="round" stroke-linejoin="round"><path d="M4 19.5A2.5 2.5 0 0 1 6.5 17H20"/><path d="M6.5 2H20v20H6.5A2.5 2.5 0 0 1 4 19.5v-15A2.5 2.5 0 0 1 6.5 2z"/></svg>',
            chevLeft: '<svg viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2" stroke-linecap="round" stroke-linejoin="round"><polyline points="15 18 9 12 15 6"/></svg>',
            chevRight: '<svg viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2" stroke-linecap="round" stroke-linejoin="round"><polyline points="9 18 15 12 9 6"/></svg>'
        };

        function svgIcon(name) {
            return $('<span class="' + CLS + 'ic"></span>').html(SVG[name] || "");
        }

        /* ---------------------------------------------------------------- */
        /*  Messages                                                        */
        /* ---------------------------------------------------------------- */

        // Prefer the seeded AD_Message; else a readable English default; else the
        // key. VIS.Msg answers an unseeded key with the key BRACKETED, which is
        // never equal to the key, so a bracketed answer counts as "not found".
        function msg(key, fallback) {
            try {
                var m = VIS.Msg.getMsg(key);
                if (m && m !== key && !isMissingMsg(m)) return m;
            } catch (e) { }
            return (fallback !== null && fallback !== undefined) ? fallback : key;
        }

        function isMissingMsg(text) {
            var t = String(text);
            return t.length > 1 && t.charAt(0) === "[" && t.charAt(t.length - 1) === "]";
        }

        function info(text) {
            if (window.VIS && VIS.ADialog && VIS.ADialog.info) VIS.ADialog.info("", "", text);
            else if (window.console) console.log(text);
        }

        function error(text) {
            if (window.VIS && VIS.ADialog && VIS.ADialog.error) VIS.ADialog.error("", "", text);
            else if (window.console) console.log(text);
        }

        /* ---------------------------------------------------------------- */
        /*  Formatting                                                      */
        /* ---------------------------------------------------------------- */

        /* Amounts carry their currency symbol and the currency's own precision -
           the allocation currency can differ from the payment currency, so the
           caller always passes both rather than assuming the payment's. */
        function fmtMoney(value, symbol, precision) {
            var v = (+value) || 0;
            var p = (precision >= 0 && precision <= 10) ? precision : 2;
            var sign = v < 0 ? "-" : "";
            var text;
            try {
                text = Math.abs(v).toLocaleString(window.navigator.language, {
                    minimumFractionDigits: p, maximumFractionDigits: p
                });
            } catch (e) {
                text = Math.abs(v).toFixed(p);
            }
            /* Symbol sits flush against the figure - "$100.000", not "$ 100.000". */
            return sign + (symbol || "") + text;
        }

        /* The payment's own currency - the default for everything but the
           allocation history. */
        function money(value) {
            if (!data) return "";
            return fmtMoney(value, data.CurSymbol || data.CurISO, data.StdPrecision);
        }

        /* Server dates arrive as ISO yyyy-MM-dd, so they are parsed as a plain
           calendar date; the user's locale decides how it reads. */
        function fmtDate(value) {
            if (!value) return "";
            var s = String(value);
            var parts = s.length >= 10 ? s.substring(0, 10).split("-") : null;
            var d = (parts && parts.length === 3)
                ? new Date(+parts[0], (+parts[1]) - 1, +parts[2])
                : new Date(s);
            if (!d || isNaN(d.getTime())) return "";
            try {
                return d.toLocaleDateString(window.navigator.language, {
                    year: "numeric", month: "short", day: "2-digit"
                });
            } catch (e) {
                return d.toDateString();
            }
        }

        function joinBits(bits) {
            var kept = [];
            for (var i = 0; i < bits.length; i++) {
                if (bits[i] !== null && bits[i] !== undefined && String(bits[i]).length > 0) {
                    kept.push(bits[i]);
                }
            }
            return kept.join(" · ");
        }

        /* C_Payment.C_BPartner_ID is optional: a bank transfer or a charge payment
           carries no counterparty at all. The id is the test, not the name - a
           partner whose name the role cannot read still IS a partner. */
        function hasBPartner() {
            return !!(data && +data.C_BPartner_ID > 0);
        }

        /* The bank account the money moved through, as "Bank ****1234". The
           account number arrives already masked from the server. */
        function bankAccountText() {
            if (!data) return "";
            return joinBits([data.BankName, data.BankAccountNo]);
        }

        function isReversedOrVoided() {
            return !!(data && (data.DocStatus === "RE" || data.DocStatus === "VO"));
        }

        function isCompletedOrClosed() {
            return !!(data && (data.DocStatus === "CO" || data.DocStatus === "CL"));
        }

        function docStatusText() {
            if (!data) return "";
            return data.DocStatusName || data.DocStatus || "";
        }

        /* ---------------------------------------------------------------- */
        /*  Lifecycle                                                       */
        /* ---------------------------------------------------------------- */

        this.init = function () {
            $root = $('<div class="' + CLS + 'root"></div>');
            $body = $('<div class="' + CLS + 'body"></div>');
            $emptyState = $('<div class="' + CLS + 'empty" style="display:none;"></div>');
            $emptyState.text(msg("VAS_191_NoData", "No payment selected"));
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

        /* Delegated once on the root so the handlers survive every re-render.
           Every element carrying data-open-* is a real button, so it answers the
           keyboard the way a button does. */
        function bindEvents() {
            $root.on("click", "[data-open-target]", function (e) {
                e.preventDefault();
                openTarget($(this));
            });
            $root.on("keydown", "[data-open-target]", function (e) {
                if (e.which !== 13 && e.which !== 32) return;
                e.preventDefault();
                openTarget($(this));
            });

            /* Coming back from the standard Allocation form: the allocation the
               user may have created is not visible in this panel's data yet. */
            $root.on("mouseenter focusin", function () {
                if (!allocationFormOpened) return;
                allocationFormOpened = false;
                if ($self.record_ID > 0) $self.fetchData($self.record_ID);
            });
        }

        /* ---------------------------------------------------------------- */
        /*  Navigation                                                      */
        /* ---------------------------------------------------------------- */

        /* Opens a business document in its own window through the shared zoom
           helper. Never a full-page navigation - that crashes the host from
           inside a panel. Degrades silently: a click can never throw. */
        function openTarget($el) {
            var target = $el.attr("data-open-target");
            var id = +$el.attr("data-open-id") || 0;
            if (!target || id <= 0 || !VAS.ZoomUtil) return;

            try {
                if (target === "payment") {
                    zoom("C_Payment_ID", id,
                        data && data.IsReceipt ? PAYMENT_WINDOW_AR : PAYMENT_WINDOW_AP,
                        data && data.IsReceipt ? "" : PAYMENT_WINDOW_AP_OLD);
                } else if (target === "invoice") {
                    /* An expense invoice has its own window - it is not the AR /
                       AP invoice screen with a different filter - so the flag the
                       server put on the link decides the target before the
                       document's direction does. */
                    if ($el.attr("data-open-expense") === "Y") {
                        zoom("C_Invoice_ID", id, INVOICE_WINDOW_EXPENSE, "");
                    } else {
                        zoom("C_Invoice_ID", id,
                            data && data.IsReceipt ? INVOICE_WINDOW_AR : INVOICE_WINDOW_AP,
                            data && data.IsReceipt ? INVOICE_WINDOW_AR_OLD : INVOICE_WINDOW_AP_OLD);
                    }
                } else if (target === "allocation") {
                    zoom("C_AllocationHdr_ID", id, ALLOCATION_WINDOW, "");
                } else if (target === "journal") {
                    zoom("GL_Journal_ID", id, JOURNAL_WINDOW, JOURNAL_WINDOW_OLD);
                }
                /* No business-partner zoom: the target window differs for a
                   customer and a vendor, and this panel serves both directions -
                   guessing one of the two would open the wrong screen. */
            } catch (e) { if (window.console) console.log(e); }
        }

        function zoom(column, id, newName, oldName) {
            VAS.ZoomUtil.zoomToRecord(column, id, 0, newName, oldName || "");
        }

        /* ---------------------------------------------------------------- */
        /*  Request lifecycle                                               */
        /* ---------------------------------------------------------------- */

        /* Drops whatever the panel was loading: cancels a fetch still waiting on
           its delay and invalidates the token of one already on the wire, so
           neither can paint over what the caller is about to put on screen. */
        function invalidateFetch() {
            fetchToken++;
            if (pendingFetch) {
                clearTimeout(pendingFetch);
                pendingFetch = null;
            }
            /* A superseded request may still be on the wire, but its reply is
               already condemned by the token bump - it no longer counts as the
               load in progress. */
            inFlight = false;
        }

        this.abortPendingFetch = invalidateFetch;

        /* True when the panel is already showing, waiting to load, or loading this
           exact record.

           The host drives the panel from TWO independent entry points for one
           selection - refreshPanelData() and the tab's dataStatusChanged event -
           and their order is not guaranteed. Without this test the pair fetched
           the same payment twice on open: the data-status event got there first,
           started a load, and refreshPanelData then scheduled a second one
           regardless. The first reply was discarded by the token check, so the
           screen was right and only the server saw the duplicate. */
        function isCurrent(recordID) {
            var id = +recordID || 0;
            return id > 0 && id === shownRecordId
                   && (data !== null || inFlight || pendingFetch !== null);
        }

        /* An explicit reload (the platform Refresh button, the return from the
           allocation form, the refresh after a document action) calls fetchData
           directly and so is never blocked by the guard above. */
        this.scheduleFetch = function (recordID) {
            if (isCurrent(recordID)) return;

            invalidateFetch();
            var token = fetchToken;
            /* Claimed now, not when the timer fires: shownRecordId means "showing
               or loading", and leaving it stale through the wait would let the
               data-status listener fire a second fetch for the same row. */
            shownRecordId = +recordID || 0;
            showBusy(true);
            pendingFetch = setTimeout(function () {
                pendingFetch = null;
                if (token !== fetchToken) return;          // superseded while waiting
                if (isTabInserting($self.curTab)) {        // flag may only be up now
                    $self.record_ID = 0;
                    $self.clear();
                    return;
                }
                $self.fetchData(recordID);
            }, REFRESH_DELAY_MS);
        };

        this.fetchData = function (recordID) {
            invalidateFetch();
            var token = fetchToken;
            shownRecordId = +recordID || 0;
            /* Nothing of the previous payment may stay on screen while another
               record is loading - the body is emptied here rather than left
               painted under the spinner. The "no payment" placeholder is NOT
               raised: this is a load, not an empty selection. */
            data = null;
            if ($body) {
                $body.empty();
                $emptyState.hide();
                $body.show();
            }
            showBusy(true);
            inFlight = true;
            $.ajax({
                url: VIS.Application.contextUrl + "VAS/VAS_191_APPaymentDetailPanel/GetPaymentOverview",
                type: "GET",
                dataType: "json",
                data: { C_Payment_ID: recordID },
                success: function (raw) {
                    /* Reply for a payment the panel has already left. Whoever
                       superseded us owns the busy indicator now - and owns the
                       in-flight flag too, so this reply must not clear it. */
                    if (token !== fetchToken) return;
                    inFlight = false;
                    data = (typeof raw === "string") ? jQuery.parseJSON(raw) : raw;
                    pages = {};                // every section back to page 1
                    render();
                    showBusy(false);
                },
                error: function (err) {
                    if (token !== fetchToken) return;
                    inFlight = false;
                    if (window.console) console.log(err);
                    data = null;
                    render();
                    showBusy(false);
                    error(msg("VAS_191_LoadFailed", "Could not load the payment details."));
                }
            });
        };

        this.clear = function () {
            invalidateFetch();
            data = null;
            shownRecordId = 0;
            pages = {};
            render();
            /* A discarded reply never reaches its own showBusy(false), so the
               spinner would otherwise sit on the empty panel for good. */
            showBusy(false);
        };

        /* The framework notifies a tab panel when the selected record changes but
           NOT when the user starts a new one: GridController.dataNew() never
           reaches the tab panel. Listening to the tab's own data-status events
           closes that gap. */
        function onTabDataStatus(e) {
            var inserting = false;
            try {
                inserting = !!(e && typeof e.getIsInserting === "function" && e.getIsInserting());
            } catch (ex) {
                inserting = false;
            }
            if (!inserting) inserting = isTabInserting($self.curTab);

            var rid = 0;
            try {
                if ($self.curTab && typeof $self.curTab.getRecord_ID === "function") {
                    rid = +$self.curTab.getRecord_ID() || 0;
                }
            } catch (ex2) {
                rid = 0;
            }

            if (inserting || rid <= 0) {
                if (shownRecordId || data) {
                    $self.record_ID = 0;
                    $self.clear();
                }
                return;
            }
            /* Routed through scheduleFetch, not straight to fetchData, so this
               entry point goes through the same "already showing / already
               loading" guard and the same debounce as refreshPanelData. The two
               can now fire in either order for one selection and still produce a
               single request. */
            if (rid !== shownRecordId) {
                $self.record_ID = rid;
                $self.scheduleFetch(rid);
            }
        }

        this.tabDataListener = { dataStatusChanged: function (e) { onTabDataStatus(e); } };

        /* The platform Refresh button calls this. Without it the button silently
           does nothing, so it is exposed as an instance method here and as a
           prototype method below. */
        this.refreshWidget = function () {
            if ($self.record_ID > 0) $self.fetchData($self.record_ID);
            else $self.clear();
        };

        /* ---------------------------------------------------------------- */
        /*  Section registry                                                */
        /* ---------------------------------------------------------------- */

        function any(list) { return !!(list && list.length); }

        /* Payment Allocate is what is still only PREPARED on the payment
           (C_PaymentAllocate). Once the document is completed, closed, reversed
           or voided that intent has either been turned into real allocations -
           which the Allocation Detail section shows - or been discarded with the
           document, so the prepared rows are stale either way. Show the section
           only while the payment is still open. */
        function showPaymentAllocate() {
            if (!any(data.PaymentAllocate)) return false;
            return !isCompletedOrClosed() && !isReversedOrVoided();
        }

        /* key | when to draw it | what draws it. Rendering iterates this in
           order; nothing else decides which sections exist or where they sit.
           The order follows the panel's reading order: what the payment is ->
           what can be done with it -> where it stands -> its details -> what it
           is earmarked against -> what has actually been allocated. */
        /* Order and composition follow the approved reference layout - header,
           actions, info trio, lifecycle, payment details, advance banner,
           allocations, approval, posted entry - with ONE deliberate difference:
           the reference's four-card lifecycle stepper is not drawn, because the
           Payment lifecycle rail in its place says the same thing. */
        var SECTIONS = [
            { key: "head", condition: function () { return true; }, render: renderHeader },
            { key: "actions", condition: function () { return hasAnyAction(); }, render: renderActions },
            { key: "info", condition: function () { return true; }, render: renderInfoTrio },
            { key: "life", condition: function () { return true; }, render: renderLifecycle },
            { key: "details", condition: function () { return true; }, render: renderDetails },
            { key: "banner", condition: function () { return true; }, render: renderBanner },
            { key: "allocate", condition: function () { return showPaymentAllocate(); }, render: renderPaymentAllocate },
            { key: "detail", condition: function () { return any(data.AllocationDetail); }, render: renderAllocationDetail },
            { key: "approval", condition: function () { return true; }, render: renderApproval },
            { key: "posted", condition: function () { return hasPostedRows(); }, render: renderPostedJournal }
        ];

        function render() {
            if (!$body) return;    // the host can hand us a record before init()

            $body.empty();

            if (!data || !data.C_Payment_ID) {
                $body.hide();
                $emptyState.show();
                return;
            }

            $emptyState.hide();
            $body.show();

            /* Each section is drawn behind its own guard: one that throws costs
               only itself, never the sections below it. */
            for (var i = 0; i < SECTIONS.length; i++) {
                var sec = SECTIONS[i];
                try {
                    if (sec.condition()) sec.render();
                } catch (e) {
                    try { console.log("VAS_191 section '" + sec.key + "' failed to render:", e); } catch (e2) { }
                }
            }

            /* A different payment starts at the top of the panel. The ROOT is the
               scroller (see the panel CSS), so that is the element to reset. */
            try { if ($root && $root[0]) $root[0].scrollTop = 0; } catch (e3) { }
        }

        /* ---------------------------------------------------------------- */
        /*  Primitives                                                      */
        /* ---------------------------------------------------------------- */

        /* A headered section: title left, optional muted summary right. Returns
           the section element so the caller can append its content. */
        function section(title, summary) {
            var $sec = $('<section class="' + CLS + 'sec"></section>');
            var $head = $('<div class="' + CLS + 'secHead"></div>');
            $head.append($('<span class="' + CLS + 'secTitle"></span>').text(title).attr("title", title));
            if (summary) {
                $head.append($('<span class="' + CLS + 'secSum"></span>').text(summary).attr("title", summary));
            }
            $sec.append($head);
            $body.append($sec);
            return $sec;
        }

        /* A bordered block card - the shape the AR invoice panel uses for its
           detail, allocation and posting sections: title row on top, content in
           the middle, optional tinted footer band at the bottom. Returns the
           section so the caller can append its own content and footer. */
        function block(title, summary, extraClass) {
            var $sec = $('<section class="' + CLS + 'block"></section>');
            if (extraClass) $sec.addClass(extraClass);
            var $head = $('<div class="' + CLS + 'block-head"></div>');
            $head.append($('<span class="' + CLS + 'block-h"></span>').text(title).attr("title", title));
            if (summary) {
                $head.append($('<span class="' + CLS + 'block-r"></span>').text(summary).attr("title", summary));
            }
            $sec.append($head);
            $body.append($sec);
            return $sec;
        }

        /* A document number that opens its own record. Falls back to plain text
           when there is no id to zoom to, so a click never dead-ends on a link
           that cannot resolve a record.

           `isExpense` marks an invoice link as pointing at an EXPENSE invoice,
           which the click handler sends to its own window. */
        function docLink(text, target, recordId, isExpense) {
            var label = text || "";
            if (!label || !(recordId > 0)) {
                return $('<span></span>').text(label).attr("title", label);
            }
            var $a = $('<a href="javascript:void(0)" class="' + CLS + 'doclink"></a>')
                .text(label)
                .attr("title", label)
                .attr("data-open-target", target)
                .attr("data-open-id", recordId);
            if (isExpense) $a.attr("data-open-expense", "Y");
            return $a;
        }

        function chip(text, tone, onTint) {
            var $c = $('<span class="' + CLS + 'chip"></span>')
                .addClass(CLS + "tone-" + (tone || "neutral"))
                .text(text);
            if (onTint) $c.addClass(CLS + "onTint");
            return $c;
        }

        /* Paginates rows into a section: draws one page and, when there is more
           than one, a pager beneath it. Page state lives in `pages` under the
           section key, so each section pages independently and no section ever
           grows an inner scrollbar. */
        /* `$hostParent` lets a caller nest the rows inside an existing container
           (the data grid, which owns the shared column template) instead of
           dropping them straight into the section. */
        function paginate($sec, key, rows, perPage, buildRow, $after, $hostParent) {
            var $host = $('<div></div>');
            var $pager = $('<div class="' + CLS + 'pager"></div>');
            ($hostParent || $sec).append($host);

            function paint() {
                var pageCount = Math.max(1, Math.ceil(rows.length / perPage));
                var page = pages[key] || 0;
                if (page >= pageCount) page = pageCount - 1;
                if (page < 0) page = 0;
                pages[key] = page;

                var start = page * perPage;
                var end = Math.min(rows.length, start + perPage);

                $host.empty();
                for (var i = start; i < end; i++) $host.append(buildRow(rows[i], i));

                $pager.detach().empty();
                if (pageCount > 1) {
                    $pager.append(pagerButton("prev", page <= 0, function () {
                        pages[key] = page - 1; paint();
                    }));
                    $pager.append($('<span class="' + CLS + 'pgText"></span>').text(
                        (page + 1) + " " + msg("VAS_191_Of", "of") + " " + pageCount));
                    $pager.append(pagerButton("next", page >= pageCount - 1, function () {
                        pages[key] = page + 1; paint();
                    }));
                    $sec.append($pager);
                }
                if ($after) $sec.append($after);   // summary band stays last
            }
            paint();
        }

        function pagerButton(dir, disabled, handler) {
            var $b = $('<button type="button" class="' + CLS + 'pgBtn"></button>')
                .attr("aria-label", dir === "prev"
                    ? msg("VAS_191_Previous", "Previous page")
                    : msg("VAS_191_Next", "Next page"));
            $b.append(svgIcon(dir === "prev" ? "chevLeft" : "chevRight"));
            if (disabled) $b.prop("disabled", true);
            else $b.on("click", handler);
            return $b;
        }

        /* ---------------------------------------------------------------- */
        /*  1. Header                                                       */
        /* ---------------------------------------------------------------- */

        /* The document names itself: its TYPE is the title, its number and date the
           line underneath. The type comes from C_DocType, so an AP payment, an AR
           receipt and a prepayment each announce what they actually are; the
           generic label is only the fallback for a record with no document type. */
        function renderHeader() {
            var $h = $('<header class="' + CLS + 'head"></header>');

            var $left = $('<div class="' + CLS + 'headLeft"></div>');
            var title = data.DocTypeName || msg("VAS_191_PaymentOverview", "Payment overview");
            $left.append($('<div class="' + CLS + 'h1"></div>').text(title).attr("title", title));

            /* Document number as plain text - the panel is already showing this
               payment, so a link back to its own window would go nowhere useful.
               The date reads "Paid" or "Received" from the document's direction. */
            var docNo = data.DocumentNo || "—";
            var line = docNo;
            if (data.DateTrx) {
                line += " · " + (data.IsReceipt
                    ? msg("VAS_191_ReceivedOn", "Received {0}")
                    : msg("VAS_191_PaidOn", "Paid {0}")).replace("{0}", fmtDate(data.DateTrx));
            }
            $left.append($('<div class="' + CLS + 'sub"></div>').text(line).attr("title", line));
            $h.append($left);

            $body.append($h);
        }

        /* ---------------------------------------------------------------- */
        /*  2. Info trio                                                    */
        /* ---------------------------------------------------------------- */

        /* Three summary cards: who was paid, what was paid, and what is still
           sitting on account. The third card takes the amber treatment only while
           something remains to apply - a settled payment is not a warning. */
        function renderInfoTrio() {
            var unallocated = +data.UnallocatedAmount || 0;
            var open = !!data.CanApplyAdvance && unallocated > 0;

            var $info = $('<section class="' + CLS + 'info"></section>');

            /* 1. Business partner - the payee / payer, named not coded. A payment
                  can legitimately carry no partner (a bank transfer or a charge
                  payment), and an empty partner card says nothing; the account the
                  money actually moved through does. */
            $info.append(hasBPartner()
                ? icard({
                    eyebrow: msg("VAS_191_BusinessPartner", "Business partner"),
                    big: data.BPName || "—",
                    meta: joinBits([data.BPValue, data.OrgName]),
                    link: true
                })
                : icard({
                    eyebrow: data.IsReceipt
                        ? msg("VAS_191_ReceivedIn", "Received in")
                        : msg("VAS_191_PaidFrom", "Paid from"),
                    big: bankAccountText() || "—",
                    /* Bank reference of the transfer, then the organisation that
                       booked it. */
                    meta: joinBits([data.CheckNo, data.OrgName])
                }));

            /* 2. The amount and the date it moved. Payment method and currency are
                  deliberately not repeated here: the amount already carries its
                  currency symbol, and both facts have their own row in the Payment
                  details block - stacking them into this card only truncated it. */
            $info.append(icard({
                eyebrow: data.IsReceipt
                    ? msg("VAS_191_Received", "Received")
                    : msg("VAS_191_Paid", "Paid"),
                big: money(data.PayAmt),
                meta: data.DateTrx ? fmtDate(data.DateTrx) : ""
            }));

            /* 3. What is left on account. A zero balance still renders as zero -
                  the card never disappears and never becomes a "no data" state. */
            $info.append(icard({
                eyebrow: msg("VAS_191_UnallocatedAmount", "Unallocated amount"),
                big: money(unallocated),
                meta: open
                    ? msg("VAS_191_AvailableToApply", "Available to apply")
                    : (data.IsAllocated
                        ? msg("VAS_191_PaymentFullyAllocated", "Payment fully allocated")
                        : allocationStatusText()),
                amber: open
            }));

            $body.append($info);
        }

        /* One summary card: eyebrow label, headline value, qualifier underneath. */
        function icard(opts) {
            var $c = $('<div class="' + CLS + 'icard"></div>');
            if (opts.amber) $c.addClass(CLS + "amber");
            $c.append($('<div class="' + CLS + 'eyebrow"></div>').text(opts.eyebrow));
            var $big = $('<div class="' + CLS + 'big"></div>')
                .text(opts.big).attr("title", String(opts.big));
            if (opts.link) $big.addClass(CLS + "bigLink");
            $c.append($big);
            if (opts.meta) {
                $c.append($('<div class="' + CLS + 'imeta"></div>')
                    .text(opts.meta).attr("title", String(opts.meta)));
            }
            return $c;
        }

        /* ---------------------------------------------------------------- */
        /*  Advance banner                                                  */
        /* ---------------------------------------------------------------- */

        /* Shown only while an amount remains available to apply. Deliberately
           quiet - dashed outline, no warning colour - because an unapplied
           advance is often intentional. A settled payment raises no banner at
           all; the third summary card already says it is fully allocated. */
        function renderBanner() {
            var unallocated = +data.UnallocatedAmount || 0;
            if (!data.CanApplyAdvance || unallocated <= 0) return;

            var $b = $('<section class="' + CLS + 'banner"></section>');

            var $l = $('<div class="' + CLS + 'banner-l"></div>');
            $l.append($('<span class="' + CLS + 'well"></span>').append(svgIcon("wallet")));

            var $tx = $('<div class="' + CLS + 'banner-tx"></div>');
            $tx.append($('<div class="' + CLS + 'banner-a"></div>').text(
                msg("VAS_191_HeldAsAdvance", "{0} held as advance").replace("{0}", money(unallocated))));
            /* The hint names the partner when there is one; without a partner the
               generic wording is used rather than a sentence with a hole in it. */
            var hint = hasBPartner()
                ? msg("VAS_191_AdvanceHint",
                    "Unapplied from this payment · apply it to an open {0} document")
                    .replace("{0}", data.BPName)
                : msg("VAS_191_AdvanceHintNoBP",
                    "Unapplied from this payment · apply it to an open document");
            $tx.append($('<div class="' + CLS + 'banner-b"></div>').text(hint).attr("title", hint));
            $l.append($tx);
            $b.append($l);

            var $btn = $('<button type="button" class="' + CLS + 'pill ' + CLS + 'is-primary"></button>');
            $btn.append(svgIcon("wallet"));
            $btn.append($('<span></span>').text(msg("VAS_191_ApplyAdvance", "Apply advance")));
            $btn.on("click", function () { openAllocationForm(); });
            $b.append($btn);

            $body.append($b);
        }

        /* Fully allocated / partially allocated / unallocated, from the payment's
           own flag plus the amount the framework says is still available. */
        function allocationStatusText() {
            if (data.IsAllocated) return msg("VAS_191_FullyAllocated", "Fully allocated");
            var unallocated = +data.UnallocatedAmount || 0;
            var pay = Math.abs(+data.PayAmt || 0);
            if (unallocated > 0 && pay > 0 && unallocated < pay) {
                return msg("VAS_191_PartiallyAllocated", "Partially allocated");
            }
            return msg("VAS_191_Unallocated", "Unallocated");
        }

        /* ---------------------------------------------------------------- */
        /*  2. Action pill row                                              */
        /* ---------------------------------------------------------------- */

        /* The print-driven actions need the tab's print process; the document
           actions need the server's own availability flags. */
        function hasPrintProcess() {
            var tab = $self.curTab;
            return !!(tab && typeof tab.getAD_Process_ID === "function"
                && +tab.getAD_Process_ID() > 0);
        }

        function hasPostedRows() {
            return !!(data && data.PostedJournal && data.PostedJournal.Rows
                && data.PostedJournal.Rows.length);
        }

        /* CanApplyAdvance is NOT part of this test: Apply advance lives on the
           advance banner, so an otherwise action-less payment must not raise an
           empty action bar just because something is left to apply. */
        function hasAnyAction() {
            return !!(data && (data.CanComplete || data.CanReverse
                || hasPrintProcess() || hasPostedRows()));
        }

        /* Contextual by construction: the server decided which document actions
           this state offers, and only those are drawn. There is no generic
           Allocate button - allocation happens through Apply Advance on the
           advance banner, which opens the standard Payment Allocation form.

           Send / Download PDF both run off the tab's print process, so they share
           one enablement test: nothing can be printed or mailed without it. They
           are drawn disabled rather than hidden, so the user can see the action
           exists and why it is unavailable. */
        function renderActions() {
            var $row = $('<div class="' + CLS + 'actions"></div>');
            
            /* Apply advance is deliberately NOT in this bar. It belongs to the
               advance banner, which is the element that states there is something
               left to apply and offers the action next to that statement - the
               same placement as the reference layout. Repeating it here would give
               the panel two buttons for one action. */

            /* Send + Download PDF: same print process, same enablement, and the
               reference's neutral ghost treatment - they are utilities, not
               document actions. */
            var canPrint = hasPrintProcess();
            $row.append(actionPill("send", msg("VAS_191_SendPayment", "Send payment"), function () {
                sendPaymentEmail();
            }, "ghost", !canPrint));
            $row.append(actionPill("download", msg("VAS_191_DownloadPDF", "Download PDF"), function () {
                downloadPaymentPDF();
            }, "ghost", !canPrint));

            if (data.CanComplete) {
                $row.append(actionPill("check", msg("VAS_191_Complete", "Complete"), function () {
                    runDocumentAction("CompletePayment");
                }, "primary"));
            }
            if (data.CanReverse) {
                $row.append(actionPill("undo", msg("VAS_191_Reverse", "Reverse"), function () {
                    /* Reversal is irreversible, so it goes through the platform's
                       own confirmation dialog - never a browser confirm(). The
                       question is passed as resolved TEXT (3rd argument) rather
                       than as a message key, so an unseeded key can never surface
                       as "[VAS_191_CONFIRMREVERSE]" in the dialog. */
                    var question = msg("VAS_191_ConfirmReverse",
                        "Reverse this payment? The reversal cannot be undone.");
                    if (window.VIS && VIS.ADialog && typeof VIS.ADialog.confirm === "function") {
                        VIS.ADialog.confirm("", true, question,
                            msg("VAS_191_Confirm", "Confirm"),
                            function (ok) {
                                if (ok) runDocumentAction("ReversePayment");
                            });
                    } else {
                        error(question);
                    }
                }));
            }

            /* View ledger entry only exists when there are posted facts to jump
               to. It is the reference's text action and sits at the far end of
               the bar (margin-left:auto in CSS). */
            if (hasPostedRows()) {
                $row.append(actionPill("book", msg("VAS_191_ViewLedgerEntry", "View ledger entry"), function () {
                    scrollToSection(CLS + "journal");
                }, "text"));
            }

            $body.append($row);
        }

        /* `variant` picks one of the reference's four button treatments -
           "primary" (filled), "outline", "ghost" or "text"; anything else falls
           back to the outline. `disabled` draws the action visibly unavailable
           rather than hiding it. */
        function actionPill(icon, label, handler, variant, disabled) {
            var $b = $('<button type="button" class="' + CLS + 'pill"></button>');
            $b.addClass(CLS + "is-" + (variant || "outline"));
            $b.append(svgIcon(icon));
            $b.append($('<span></span>').text(label));
            if (disabled) $b.prop("disabled", true);
            else $b.on("click", handler);
            return $b;
        }

        /* Scrolls a section into view and flashes its outline once, so the user
           sees WHERE the action took them. */
        function scrollToSection(cssClass) {
            var el = $body.find("." + cssClass)[0];
            if (!el) return;
            try { el.scrollIntoView({ behavior: "smooth", block: "start" }); }
            catch (e) { el.scrollIntoView(); }
            var $el = $(el);
            $el.removeClass(CLS + "flash");
            void el.offsetWidth;                      // restart the animation
            $el.addClass(CLS + "flash");
        }

        /* Runs Complete / Reverse on the server. The server re-validates the
           document before it acts, so a stale panel cannot force an action the
           record no longer allows; the reply carries the status the document
           actually reached. */
        function runDocumentAction(endpoint) {
            if (!data || !data.C_Payment_ID) return;
            var paymentId = data.C_Payment_ID;

            showBusy(true);
            $root.find("." + CLS + "pill").prop("disabled", true);

            $.ajax({
                url: VIS.Application.contextUrl + "VAS/VAS_191_APPaymentDetailPanel/" + endpoint,
                type: "POST",
                dataType: "json",
                data: { C_Payment_ID: paymentId },
                success: function (raw) {
                    var resp = (typeof raw === "string") ? jQuery.parseJSON(raw) : raw;
                    showBusy(false);

                    if (resp && resp.Success) {
                        info(resp.Message || (endpoint === "CompletePayment"
                            ? msg("VAS_191_PaymentCompleted", "Payment completed")
                            : msg("VAS_191_PaymentReversed", "Payment reversed")));
                        /* The parent record's DocStatus / DocAction / IsAllocated
                           changed, so the hosting tab is repainted too - the user
                           never has to reopen the payment. */
                        refreshCurTab();
                        $self.fetchData(paymentId);
                    } else {
                        error((resp && resp.Message)
                            || msg("VAS_191_ActionFailed", "The action could not be completed."));
                        /* Even a failed run can have moved the document, so the
                           panel re-reads rather than trusting what it had. */
                        $self.fetchData(paymentId);
                    }
                },
                error: function (err) {
                    if (window.console) console.log(err);
                    showBusy(false);
                    error(msg("VAS_191_ActionFailed", "The action could not be completed."));
                    $self.fetchData(paymentId);
                }
            });
        }

        /* Repaints the hosting window's current tab so the parent grid / single
           view picks up the status the document action just changed. Guarded -
           the framework only sets curTab when the panel is started from a tab. */
        function refreshCurTab() {
            try {
                if ($self.curTab && typeof $self.curTab.dataRefreshAll === "function") {
                    $self.curTab.dataRefreshAll();
                }
            } catch (e) { if (window.console) console.log(e); }
        }

        /* Apply Advance opens the STANDARD Payment Allocation form
           (AD_Form.ClassName = VAdvantage.Apps.AForms.VAllocation) through the
           platform's own form-opening mechanism. No second allocation UI is
           built here.

           The current payment's context is handed to startForm's parameter
           object, which the platform passes on to the form's
           init(windowNo, frame, params). The shipped VAllocation form does not
           read that third argument today, so the values arrive but are not
           preselected; passing them costs nothing and keeps the contract right
           for the day the form starts honouring it. Patching the platform form
           is deliberately out of scope for this panel. */
        function openAllocationForm() {
            var formId = +(data && data.AllocationFormId) || 0;
            if (formId <= 0) {
                error(msg("VAS_191_AllocationFormMissing",
                    "The standard Payment Allocation form is not available."));
                return;
            }
            if (!window.VIS || !VIS.viewManager || typeof VIS.viewManager.startForm !== "function") {
                error(msg("VAS_191_AllocationFormMissing",
                    "The standard Payment Allocation form is not available."));
                return;
            }

            var params = {
                AD_Org_ID: data.AD_Org_ID,
                C_BPartner_ID: data.C_BPartner_ID,
                C_Payment_ID: data.C_Payment_ID,
                C_Currency_ID: data.C_Currency_ID,
                DateAcct: data.DateAcct,
                IsReceipt: data.IsReceipt ? "Y" : "N",
                AllocationFrom: "P",
                AllocationTo: "I"
            };

            try {
                VIS.viewManager.startForm(formId, params);
                allocationFormOpened = true;
            } catch (e) {
                if (window.console) console.log(e);
                error(msg("VAS_191_AllocationFormMissing",
                    "The standard Payment Allocation form is not available."));
            }
        }

        /* ---------------------------------------------------------------- */
        /*  Print + e-mail (same path as the AR invoice panel)              */
        /* ---------------------------------------------------------------- */

        /* AD_Process_ID / AD_Table_ID / AD_Window_ID for the print and share
           flows, read off the current grid tab. Without the tab there is no print
           process, and both actions are drawn disabled. */
        function printContext() {
            var tab = $self.curTab;
            return {
                AD_Process_ID: (tab && typeof tab.getAD_Process_ID === "function") ? tab.getAD_Process_ID() : 0,
                AD_Table_ID: (tab && typeof tab.getAD_Table_ID === "function") ? tab.getAD_Table_ID() : ($self.table_ID || 0),
                AD_Window_ID: (tab && typeof tab.getAD_Window_ID === "function") ? tab.getAD_Window_ID() : ($self.AD_Window_ID || 0),
                RecordID: $self.record_ID
            };
        }

        /* Generates the payment document through the framework print process and
           opens the produced file. Identical path to the AR invoice panel - only
           the origin name differs. */
        function downloadPaymentPDF() {
            if (!$self.record_ID || !$self.curTab) return;

            var pc = printContext();
            if (!pc.AD_Process_ID || !pc.AD_Table_ID) {
                error(msg("VAS_191_ActionFailed", "The action could not be completed."));
                return;
            }

            showBusy(true);
            $.ajax({
                url: VIS.Application.contextUrl + "JsonData/GeneratePrint/",
                dataType: "json",
                data: {
                    AD_Process_ID: pc.AD_Process_ID,
                    Name: "Print",
                    AD_Table_ID: pc.AD_Table_ID,
                    Record_ID: pc.RecordID,
                    WindowNo: $self.windowNo,
                    filetype: "P",                 // P = PDF
                    actionOrigin: "W",
                    originName: "Payment"
                },
                success: function (raw) {
                    var res = (typeof raw === "string") ? jQuery.parseJSON(raw) : raw;
                    if (!res) { showBusy(false); return; }

                    /* GeneratePrint writes to TempDownload and answers with the file
                       name; anything else is a failure worth naming. */
                    var file = res.ReportFilePath || res.FilePath || res.FileName || res.fileName || res.path;
                    if (!file) {
                        var reason = (res.ErrorText)
                            || (res.ReportProcessInfo && res.ReportProcessInfo.Summary)
                            || msg("VAS_191_ActionFailed", "The action could not be completed.");
                        error(reason);
                        if (window.console) console.log("GeneratePrint response:", res);
                        showBusy(false);
                        return;
                    }

                    showBusy(false);
                    window.open(VIS.Application.contextUrl + file, "_blank");
                },
                error: function (err) {
                    if (window.console) console.log(err);
                    showBusy(false);
                    error(msg("VAS_191_ActionFailed", "The action could not be completed."));
                }
            });
        }

        /* Sends the payment document through the shared VA112 share / e-mail
           panel. The recipient is left to VAS_SentEmailDoc, which resolves it on
           the server from AD_Table_ID + RecordID -> C_BPartner_ID -> AD_User, so
           the panel does not need a contact column on C_Payment. */
        function sendPaymentEmail() {
            if (!$self.record_ID || !$self.curTab) return;
            if (!VAS.VAS_SentEmailDoc || typeof VAS.VAS_SentEmailDoc.sendEmail !== "function") {
                error(msg("VAS_191_ActionFailed", "The action could not be completed."));
                return;
            }

            var pc = printContext();
            if (!pc.AD_Process_ID || !pc.AD_Table_ID || !pc.AD_Window_ID) {
                error(msg("VAS_191_ActionFailed", "The action could not be completed."));
                return;
            }

            /* Static entry point - no frame is needed for the share flow. */
            VAS.VAS_SentEmailDoc.sendEmail({
                windowNo: $self.windowNo,
                AD_Process_ID: pc.AD_Process_ID,
                AD_Table_ID: pc.AD_Table_ID,
                RecordID: pc.RecordID,
                AD_Window_ID: pc.AD_Window_ID,
                Name: "",
                EMailID: ""
            });
        }

        /* ---------------------------------------------------------------- */
        /*  3. Payment lifecycle                                            */
        /* ---------------------------------------------------------------- */

        /* Created -> Completed -> Allocated -> Reconciled, but only the stages
           that actually apply to THIS payment: allocation and reconciliation are
           not mandatory for every payment type, so a stage is included only when
           the payment has reached it or is genuinely waiting on it. */
        function buildStages() {
            var stages = [];

            stages.push({
                label: msg("VAS_191_Created", "Created"),
                state: "done"
            });

            if (isReversedOrVoided()) {
                stages.push({ label: docStatusText(), state: "blocked" });
            } else {
                stages.push({
                    label: msg("VAS_191_Completed", "Completed"),
                    state: isCompletedOrClosed() ? "done" : "active"
                });
            }

            var hasAllocationActivity = data.IsAllocated
                || any(data.AllocationDetail)
                || any(data.PaymentAllocate)
                || (isCompletedOrClosed() && (+data.UnallocatedAmount || 0) > 0);
            if (hasAllocationActivity) {
                stages.push({
                    label: msg("VAS_191_Allocated", "Allocated"),
                    state: data.IsAllocated ? "done" : (isCompletedOrClosed() ? "active" : "pending")
                });
            }

            /* Reconciliation only applies to a payment that moves through a bank
               account; a cash payment is never "waiting to be reconciled". */
            var reconciliationApplies = data.IsReconciled
                || (isCompletedOrClosed() && (+data.C_BankAccount_ID || 0) > 0);
            if (reconciliationApplies) {
                stages.push({
                    label: msg("VAS_191_Reconciled", "Reconciled"),
                    state: data.IsReconciled ? "done" : "pending"
                });
            }

            return stages;
        }

        function renderLifecycle() {
            var stages = buildStages();
            /* Below three stages the rail does not earn itself - the hero already
               carries the same facts as metrics. */
            if (stages.length < 3) return;

            var activeIdx = -1;
            var blocked = false;
            for (var i = 0; i < stages.length; i++) {
                if (stages[i].state === "active" && activeIdx < 0) activeIdx = i;
                if (stages[i].state === "blocked") blocked = true;
            }

            var summary;
            if (blocked) summary = docStatusText();
            else if (activeIdx >= 0) {
                summary = msg("VAS_191_StageOf", "Stage {0} of {1}")
                    .replace("{0}", activeIdx + 1).replace("{1}", stages.length);
            } else {
                summary = msg("VAS_191_LifecycleComplete", "Complete");
            }

            var $sec = section(msg("VAS_191_Lifecycle", "Payment lifecycle"), summary);

            var $pipe = $('<div class="' + CLS + 'pipe"></div>');
            var $grid = $('<div class="' + CLS + 'pipeGrid"></div>')
                .css("grid-template-columns", "repeat(" + stages.length + ", minmax(0, 1fr))");

            for (var s = 0; s < stages.length; s++) {
                var st = stages[s];
                var $col = $('<div class="' + CLS + 'pipeCol"></div>');

                /* The trailing segment turns blue once the circle on its left is
                   done - that is what makes the rail read as a progress bar. */
                if (s < stages.length - 1) {
                    $col.append($('<span class="' + CLS + 'pipeSeg ' +
                        (st.state === "done" ? CLS + "done" : "") + '"></span>'));
                }

                var $dot = $('<span class="' + CLS + 'pipeDot ' + CLS + st.state + '"></span>');
                if (st.state === "done") $dot.append(svgIcon("check"));
                else if (st.state === "blocked") $dot.append(svgIcon("cross"));
                else if (st.state === "active") $dot.append($("<i></i>"));
                $col.append($dot);

                $col.append($('<div class="' + CLS + 'pipeLbl ' + CLS + st.state + '"></div>')
                    .text(st.label).attr("title", st.label));

                $grid.append($col);
            }

            $pipe.append($grid);
            $sec.append($pipe);
        }

        /* ---------------------------------------------------------------- */
        /*  4. Payment details                                              */
        /* ---------------------------------------------------------------- */

        /* Payment details as the AR invoice panel renders them: a bordered block
           holding a two-column key / value list, with only the rows the record
           actually carries. An empty row is never drawn, and when nothing at all
           resolves the whole section stays away. */
        function renderDetails() {
            var rows = [
                { k: msg("VAS_191_Organization", "Organization"), v: data.OrgName },
                { k: msg("VAS_191_DocumentNo", "Document no."), v: data.DocumentNo },
                { k: msg("VAS_191_DocumentType", "Document type"), v: data.DocTypeName },
                { k: msg("VAS_191_BankAccount", "Bank account"), v: joinBits([data.BankName, data.BankAccountNo]) },
                { k: msg("VAS_191_Currency", "Currency"), v: data.CurISO },
                { k: msg("VAS_191_PaymentDate", "Payment date"), v: fmtDate(data.DateTrx) },
                { k: msg("VAS_191_AccountingDate", "Accounting date"), v: fmtDate(data.DateAcct) },
                { k: msg("VAS_191_PaymentMethod", "Payment method"), v: data.PaymentMethodName },
                { k: msg("VAS_191_DocumentStatus", "Document status"), v: docStatusText() },
                { k: msg("VAS_191_PostedStatus", "Posted status"), v: data.PostedName },
                { k: msg("VAS_191_Prepayment", "Prepayment"), v: data.IsPrepayment ? msg("VAS_191_Yes", "Yes") : msg("VAS_191_No", "No") },
                { k: msg("VAS_191_ExecutionStatus", "Execution status"), v: data.ExecutionStatusName },
                { k: msg("VAS_191_CheckNo", "Check No"), v: data.CheckNo },
                { k: msg("VAS_191_RelatedInvoice", "Related invoice"), v: data.InvoiceDocumentNo },
                { k: msg("VAS_191_Charge", "Charge"), v: data.ChargeName },
                { k: msg("VAS_191_Description", "Description"), v: data.Description }
            ];

            var visible = [];
            for (var i = 0; i < rows.length; i++) {
                if (rows[i].v !== null && rows[i].v !== undefined && String(rows[i].v).length > 0) {
                    visible.push(rows[i]);
                }
            }
            if (!visible.length) return;

            /* The reference gives this one block the enlarged treatment. */
            var $sec = block(msg("VAS_191_PaymentDetails", "Payment details"), "", CLS + "details-lg");
            var $kv = $('<div class="' + CLS + 'kv ' + CLS + 'cols2"></div>');
            for (var j = 0; j < visible.length; j++) {
                $kv.append($('<div class="' + CLS + 'kv-r"></div>')
                    .append($('<span class="' + CLS + 'kv-k"></span>').text(visible[j].k))
                    .append($('<span class="' + CLS + 'kv-v"></span>')
                        .text(visible[j].v).attr("title", String(visible[j].v))));
            }
            $sec.append($kv);
        }

        /* ---------------------------------------------------------------- */
        /*  5. Payment Allocate (C_PaymentAllocate)                         */
        /* ---------------------------------------------------------------- */

        /* The allocation entries PREPARED on the payment. This is not allocation
           history - the completed history is the section below - so the two are
           never merged into one table. The section is not drawn at all when the
           payment carries no prepared entries. */
        function renderPaymentAllocate() {
            var rows = data.PaymentAllocate || [];
            if (!rows.length) return;

            var $sec = block(
                msg("VAS_191_PaymentAllocate", "Payment Allocate"),
                msg("VAS_191_EntryCount", "{0} entries").replace("{0}", rows.length));

            var $head = $('<div class="' + CLS + 'pa-head"></div>');
            $head.append($('<span></span>').text(msg("VAS_191_InvoiceGLJournal", "Invoice / GL Journal")));
            $head.append($('<span></span>').text(msg("VAS_191_SchDate", "Date")));
            $head.append($('<span class="right"></span>').text(msg("VAS_191_Amount", "Amount")));
            $head.append($('<span class="right"></span>').text(msg("VAS_191_Discount", "Discount")));
            $head.append($('<span class="right"></span>').text(msg("VAS_191_WriteOff", "Write-off")));
            $sec.append($head);

            var $foot = $('<div class="' + CLS + 'block-foot"></div>');
            $foot.append($('<span></span>').text(msg("VAS_191_TotalApplied", "Total applied")));
            $foot.append($('<span></span>').append($('<b></b>').text(money(data.PaymentAllocateTotal))));

            paginate($sec, "allocate", rows, ALLOCATE_PER_PAGE, buildAllocateRow, $foot);
        }

        function buildAllocateRow(row) {
            var $r = $('<div class="' + CLS + 'pa-row"></div>');

            /* Never a raw id: the invoice's document number, or the PARENT GL
               journal's, is what the user sees - and it opens that record. */
            var docNo = row.ReferenceDocumentNo || "—";
            var target = row.ReferenceType === "Journal" ? "journal"
                : (row.ReferenceType === "Invoice" ? "invoice" : "");
            var $doc = $('<div class="p"></div>');
            $doc.append(target ? docLink(docNo, target, row.ReferenceID, row.IsExpenseInvoice)
                : $('<span></span>').text(docNo).attr("title", docNo));
            $r.append($doc);

            /* Schedule: the instalment's due date, or an em dash when the entry is
               against a GL journal and no schedule exists. */
            var schedule = row.ScheduleDate
                ? msg("VAS_191_Due", "Due {0}").replace("{0}", fmtDate(row.ScheduleDate))
                : "—";
            $r.append($('<div class="c"></div>').text(schedule).attr("title", schedule));

            $r.append($('<div class="amt"></div>').text(money(row.Amount)));
            $r.append($('<div class="amt"></div>').text(money(row.DiscountAmt)));
            $r.append($('<div class="amt"></div>').text(money(row.WriteOffAmt)));
            return $r;
        }

        /* ---------------------------------------------------------------- */
        /*  6. Allocation Detail (C_AllocationHdr + C_AllocationLine)       */
        /* ---------------------------------------------------------------- */

        /* The COMPLETED allocation history, laid out as the AR invoice panel lays
           out its allocations: Reference (the counterpart document, with what it
           is underneath) · Allocation No. · Date · Applied to · Amount, closed by
           a footer band carrying the allocated / discount / write-off totals.
           Nothing allocated yet -> no section. */
        function renderAllocationDetail() {
            var rows = data.AllocationDetail || [];
            if (!rows.length) return;

            var total = data.AllocationTotal || rows.length;
            var $sec = block(
                msg("VAS_191_AllocationDetail", "Allocations"),
                msg("VAS_191_AllocationCount", "{0} allocation(s)").replace("{0}", total));

            var $head = $('<div class="' + CLS + 'alloc-head"></div>');
            $head.append($('<span></span>').text(msg("VAS_191_Reference", "Reference")));
            $head.append($('<span></span>').text(msg("VAS_191_AllocationNo", "Allocation No.")));
            $head.append($('<span></span>').text(msg("VAS_191_Date", "Date")));
            $head.append($('<span></span>').text(msg("VAS_191_AppliedTo", "Applied to")));
            $head.append($('<span class="right"></span>').text(msg("VAS_191_Amount", "Amount")));
            $sec.append($head);

            /* Footer band: what kind of allocation this is on the left, the three
               money totals on the right. The totals are magnitudes, so the band
               reads the same whether the document paid out or took money in. */
            var curSymbol = rows[0].CurrencySymbol || rows[0].CurrencyIso;
            var curPrec = rows[0].Precision;
            var $foot = $('<div class="' + CLS + 'block-foot"></div>');
            $foot.append($('<span></span>').text(data.IsReceipt
                ? msg("VAS_191_ReceiptAllocation", "Receipt Allocation")
                : msg("VAS_191_PaymentAllocation", "Payment Allocation")));
            $foot.append($('<span></span>').text(
                msg("VAS_191_AllocationFooter", "Allocated {0} - Discount {1} - Write-off {2}")
                    .replace("{0}", fmtMoney(data.AllocationAmount, curSymbol, curPrec))
                    .replace("{1}", fmtMoney(data.AllocationDiscount, curSymbol, curPrec))
                    .replace("{2}", fmtMoney(data.AllocationWriteOff, curSymbol, curPrec))));

            paginate($sec, "detail", rows, DETAIL_PER_PAGE, buildDetailRow, $foot);
        }

        function buildDetailRow(row) {
            var $r = $('<div class="' + CLS + 'alloc-row"></div>');

            /* Reference cell: the counterpart document on top, what it is
               underneath (document type · payment method · reconciliation state).
               Each kind opens its own window. */
            var target = row.ReferenceType === "Invoice" ? "invoice"
                : (row.ReferenceType === "Payment" ? "payment"
                    : (row.ReferenceType === "Journal" ? "journal" : "allocation"));
            var $ref = $('<div class="col-a"></div>');
            $ref.append($('<div class="p"></div>')
                .append(docLink(row.ReferenceDocumentNo || "—", target,
                    row.ReferenceID, row.IsExpenseInvoice)));

            var bits = [];
            if (row.DocTypeName) {
                bits.push(row.DocTypeName);
            } else if (row.ReferenceType === "Journal") {
                bits.push(msg("VAS_191_GLJournal", "GL journal"));
            } else if (row.ReferenceType === "Payment") {
                bits.push(msg("VAS_191_PaymentRef", "Payment"));
            } else if (row.ReferenceType === "Invoice") {
                bits.push(msg("VAS_191_Invoice", "Invoice"));
            }
            /* Configured payment method, never derived from the tender code. */
            if (row.PaymentMethodName) bits.push(row.PaymentMethodName);
            /* Reconciliation is a payment's property; an invoice or a journal has
               none to report. */
            if (row.HasReconciliation) {
                bits.push(row.IsReconciled
                    ? msg("VAS_191_IsReconciled", "reconciled")
                    : msg("VAS_191_Unreconciled", "unreconciled"));
            }
            if (bits.length) {
                var sub = bits.join(" · ");
                $ref.append($('<div class="s"></div>').text(sub).attr("title", sub));
            }
            $r.append($ref);

            /* Allocation No. gets its own column, so the allocation document is
               reachable on every row whatever the counterpart is. */
            $r.append($('<div class="col-e c"></div>')
                .append(docLink(row.AllocationNo || "—", "allocation", row.C_AllocationHdr_ID)));

            $r.append($('<div class="col-b c"></div>').text(fmtDate(row.AllocationDate)));

            /* Applied to: the instalment the line settled, by ordinal. */
            var applied = row.ScheduleNo > 0
                ? msg("VAS_191_Schedule", "Schedule") + " " + row.ScheduleNo
                : "";
            $r.append($('<div class="col-c c"></div>').text(applied).attr("title", applied));

            /* Amounts are stored in the ALLOCATION header's currency, which need
               not be the payment's, so each row names its own. The magnitude is
               shown: the direction is already given by the section it sits in. */
            var amount = fmtMoney(Math.abs(+row.Amount || 0),
                row.CurrencySymbol || row.CurrencyIso, row.Precision);
            $r.append($('<div class="col-d amt"></div>').text(amount).attr("title", amount));
            return $r;
        }

        /* ---------------------------------------------------------------- */
        /*  7. Approval                                                     */
        /* ---------------------------------------------------------------- */

        /* The document's audit rail: who entered it and when, and the states it
           has since reached. It is NOT the lifecycle repeated - the lifecycle
           says where the payment stands, this says who moved it and on what date.
           Steps that have not happened are left out entirely. */
        function buildApprovalSteps() {
            var steps = [];

            steps.push({
                title: msg("VAS_191_Entered", "Entered"),
                meta: joinBits([data.CreatedByName, data.Created ? fmtDate(data.Created) : ""])
            });

            if (data.IsApproved) {
                steps.push({
                    title: msg("VAS_191_ReviewedApproved", "Reviewed & approved"),
                    meta: data.Updated ? fmtDate(data.Updated) : ""
                });
            }
            if (isCompletedOrClosed()) {
                steps.push({
                    title: msg("VAS_191_Completed", "Completed"),
                    meta: joinBits([docStatusText(), data.Updated ? fmtDate(data.Updated) : ""])
                });
            }
            if (data.Posted === "Y") {
                steps.push({
                    title: msg("VAS_191_PostedToLedger", "Posted to ledger"),
                    meta: data.DateAcct ? fmtDate(data.DateAcct) : ""
                });
            }
            if (data.IsReconciled) {
                steps.push({
                    title: msg("VAS_191_Reconciled", "Reconciled"),
                    meta: ""
                });
            }
            return steps;
        }

        function renderApproval() {
            var steps = buildApprovalSteps();
            /* One lone "Entered" step is not an audit trail worth a section. */
            if (steps.length < 2) return;

            var $sec = block(msg("VAS_191_Approval", "Approval"));
            $sec.find("." + CLS + "block-head").append(chip(
                data.IsApproved ? msg("VAS_191_Approved", "Approved")
                    : msg("VAS_191_NotApproved", "Not approved"),
                data.IsApproved ? "ok" : "neutral"));

            var $rail = $('<div class="' + CLS + 'vsteps"></div>');
            steps.forEach(function (st, i) {
                var $s = $('<div class="' + CLS + 'vstep"></div>');

                var $r = $('<div class="' + CLS + 'rail"></div>');
                $r.append($('<div class="' + CLS + 'node"></div>').append(svgIcon("check")));
                /* The connector is drawn by every step but the last, which has
                   nothing below it to connect to. */
                if (i < steps.length - 1) {
                    $r.append($('<div class="' + CLS + 'line"></div>'));
                }
                $s.append($r);

                var $c = $('<div class="' + CLS + 'vc"></div>');
                $c.append($('<div class="' + CLS + 'st"></div>').text(st.title).attr("title", st.title));
                if (st.meta) {
                    $c.append($('<div class="' + CLS + 'mt"></div>').text(st.meta).attr("title", st.meta));
                }
                $s.append($c);

                $rail.append($s);
            });
            $sec.append($rail);
        }

        /* ---------------------------------------------------------------- */
        /*  8. Posted entry (Fact_Acct)                                     */
        /* ---------------------------------------------------------------- */

        /* The accounting facts the payment posted, in the primary accounting
           schema. Account / Debit / Credit always show; each dimension column is
           dropped when NO row on the page carries a value for it. An unposted
           payment has no facts, and the section is then not drawn at all. */
        function renderPostedJournal() {
            var pj = data.PostedJournal;
            if (!pj || !pj.Rows || !pj.Rows.length) return;

            /* Posted amounts are ACCOUNTED amounts, so the accounting schema's
               currency applies here - not the payment's. */
            var acctCur = pj.CurSymbol || "";
            var acctPrec = (pj.StdPrecision >= 0) ? pj.StdPrecision : 2;

            var cols = [
                {
                    key: "OrgName", label: msg("VAS_191_Organization", "Organization"), w: "1.4fr", num: false,
                    get: function (jr) { return jr.OrgName || ""; }
                },
                {
                    key: "Account", label: msg("VAS_191_Account", "Account"), w: "2fr", num: false, always: true,
                    get: function (jr) {
                        return (jr.AccountValue ? jr.AccountValue + " · " : "") + (jr.AccountName || "");
                    }
                },
                {
                    key: "Dr", label: msg("VAS_191_Debit", "Debit"), w: "1fr", num: true, always: true,
                    get: function (jr) { return jr.AmtAcctDr; }
                },
                {
                    key: "Cr", label: msg("VAS_191_Credit", "Credit"), w: "1fr", num: true, always: true,
                    get: function (jr) { return jr.AmtAcctCr; }
                },
                {
                    key: "BPName", label: msg("VAS_191_BusinessPartner", "Business partner"), w: "1.4fr", num: false,
                    get: function (jr) { return jr.BPName || ""; }
                },
                {
                    key: "OrgTrxName", label: msg("VAS_191_TrxOrganization", "Trx organization"), w: "1.4fr", num: false,
                    get: function (jr) { return jr.OrgTrxName || ""; }
                }
            ].filter(function (c) {
                if (c.always) return true;
                return pj.Rows.some(function (jr) {
                    var v = jr[c.key];
                    return v !== null && v !== undefined && String(v).length > 0;
                });
            });
            var tmpl = cols.map(function (c) { return c.w; }).join(" ");

            var $sec = block(msg("VAS_191_PostedEntry", "Posted Entry"), "", CLS + "journal");
            $sec.find("." + CLS + "block-head")
                .append(chip(msg("VAS_191_Posted", "Posted"), "ok"));

            var $head = $('<div class="' + CLS + 'je-head"></div>').css("grid-template-columns", tmpl);
            cols.forEach(function (c) {
                $head.append($('<span></span>').addClass(c.num ? "right" : "")
                    .text(c.label).attr("title", c.label));
            });
            $sec.append($head);

            var $rows = $('<div></div>');
            $sec.append($rows);
            var $pager = $('<div class="' + CLS + 'pager"></div>');
            var $total = $('<div class="' + CLS + 'je-total"></div>').css("grid-template-columns", tmpl);
            var $foot = $('<div class="' + CLS + 'block-foot"></div>');

            var jeTotal = pj.Total || pj.Rows.length;
            var jePage = 0;
            var curRows = pj.Rows;

            function paintRows() {
                $rows.empty();
                curRows.forEach(function (jr) {
                    var $r = $('<div class="' + CLS + 'je-row"></div>').css("grid-template-columns", tmpl);
                    cols.forEach(function (c) {
                        if (c.num) {
                            $r.append(jeAmount(c.get(jr), acctCur, acctPrec));
                        } else if (c.key === "Account") {
                            /* Account on top, what the fact line is for underneath -
                               the reference's two-line account cell. */
                            var v = c.get(jr);
                            var $a = $('<div class="acct"></div>')
                                .append($('<div class="code"></div>').text(v).attr("title", v));
                            if (jr.Description) {
                                $a.append($('<div class="nm"></div>')
                                    .text(jr.Description).attr("title", jr.Description));
                            }
                            $r.append($a);
                        } else {
                            var d = c.get(jr);
                            $r.append($('<div class="dimc"></div>').text(d).attr("title", d));
                        }
                    });
                    $rows.append($r);
                });

                $pager.detach().empty();
                var pageCount = Math.max(1, Math.ceil(jeTotal / JOURNAL_PER_PAGE));
                if (pageCount > 1) {
                    $pager.append(pagerButton("prev", jePage <= 0, function () { gotoJePage(jePage - 1); }));
                    $pager.append($('<span class="' + CLS + 'pgText"></span>')
                        .text((jePage + 1) + " " + msg("VAS_191_Of", "of") + " " + pageCount));
                    $pager.append(pagerButton("next", jePage >= pageCount - 1, function () { gotoJePage(jePage + 1); }));
                    $rows.after($pager);
                }
            }

            /* Server-side paged: only the rows travel, the totals and the count
               came with the initial payload and do not change between pages. */
            function gotoJePage(p) {
                showBusy(true);
                $.ajax({
                    url: VIS.Application.contextUrl + "VAS/VAS_191_APPaymentDetailPanel/GetPostedJournalPage",
                    type: "GET",
                    dataType: "json",
                    data: { C_Payment_ID: $self.record_ID, page: p, pageSize: JOURNAL_PER_PAGE },
                    success: function (raw) {
                        showBusy(false);
                        var resp = (typeof raw === "string") ? jQuery.parseJSON(raw) : raw;
                        curRows = (resp && resp.Rows) ? resp.Rows : [];
                        jePage = p;
                        paintRows();
                    },
                    error: function (err) {
                        showBusy(false);
                        if (window.console) console.log(err);
                    }
                });
            }
            paintRows();

            /* Total row: the two money columns carry the document aggregate, the
               first column carries the label, everything else stays empty. */
            cols.forEach(function (c, idx) {
                if (c.key === "Dr") {
                    $total.append($('<div class="num"></div>').text(fmtMoney(pj.TotalDr, acctCur, acctPrec)));
                } else if (c.key === "Cr") {
                    $total.append($('<div class="num"></div>').text(fmtMoney(pj.TotalCr, acctCur, acctPrec)));
                } else if (idx === 0) {
                    $total.append($('<div class="tlab"></div>').text(msg("VAS_191_Total", "Total")));
                } else {
                    $total.append($('<div></div>'));
                }
            });
            $sec.append($total);

            var footBits = [];
            if (pj.PeriodName) {
                footBits.push(msg("VAS_191_Period", "Period {0}").replace("{0}", pj.PeriodName));
            }
            if (pj.PostingDate) {
                footBits.push(msg("VAS_191_PostingDate", "Posting date {0}").replace("{0}", fmtDate(pj.PostingDate)));
            }
            var balanced = Math.abs((+pj.TotalDr || 0) - (+pj.TotalCr || 0)) < 0.005;
            $foot.append($('<span></span>').text(footBits.join(" · ")));
            $foot.append($('<span></span>').text(balanced ? msg("VAS_191_Balanced", "Balanced - Dr = Cr") : ""));
            $sec.append($foot);
        }

        /* A posted amount cell. A zero leg reads as an em dash rather than as
           "0.00", so the eye lands on the side that actually moved. */
        function jeAmount(value, cur, prec) {
            var v = +value || 0;
            var $d = $('<div class="num"></div>');
            if (v === 0) {
                $d.addClass("zero").text("—");
            } else {
                $d.text(fmtMoney(v, cur, prec));
            }
            return $d.attr("title", $d.text());
        }

        this.getRoot = function () {
            return $root;
        };
    };

    VAS.VAS_191_APPaymentDetailPanel.prototype.startPanel = function (windowNo, curTab) {
        this.windowNo = windowNo;
        this.curTab = curTab;
        if (curTab && typeof curTab.getAD_Table_ID === "function") {
            this.table_ID = curTab.getAD_Table_ID();
        }
        this.init();
        /* Watch the tab itself so New Record / Copy Record (neither of which
           reliably calls refreshPanelData) still empties the panel. */
        if (curTab && typeof curTab.addDataStatusListener === "function") {
            try { curTab.addDataStatusListener(this.tabDataListener); } catch (e) { }
        }
    };

    /* Update the tab panel for the selected record. */
    VAS.VAS_191_APPaymentDetailPanel.prototype.refreshPanelData = function (recordID, selectedRow) {
        /* The insert check is what makes New Record / Copy Record behave: the id
           handed in for an unsaved row can still be the previously selected (or
           copied-from) payment's, so the tab's own insert state decides. */
        if (selectedRow == undefined || recordID <= 0 || isTabInserting(this.curTab)) {
            this.record_ID = 0;
            this.clear();
            return;
        }
        this.record_ID = recordID;
        this.selectedRow = selectedRow;
        /* Held rather than fetched outright: the insert flag is not always up yet
           when we get here, so scheduleFetch asks once more before loading. */
        this.scheduleFetch(recordID);
    };

    /* The platform Refresh button - exposed on the prototype as well as on the
       instance, since the host may reach either. */
    VAS.VAS_191_APPaymentDetailPanel.prototype.refreshWidget = function () {
        if (this.record_ID > 0) this.fetchData(this.record_ID);
        else this.clear();
    };

    /* Set width as per window width */
    VAS.VAS_191_APPaymentDetailPanel.prototype.sizeChanged = function (width) {
        this.panelWidth = width;
    };

    /* Release variables from memory */
    VAS.VAS_191_APPaymentDetailPanel.prototype.dispose = function () {
        /* Kill any held fetch first - its timer would otherwise fire against a
           panel whose curTab has just been nulled out below. */
        if (typeof this.abortPendingFetch === "function") {
            try { this.abortPendingFetch(); } catch (e) { }
        }
        if (this.curTab && typeof this.curTab.removeDataStatusListener === "function") {
            try { this.curTab.removeDataStatusListener(this.tabDataListener); } catch (e) { }
        }
        this.tabDataListener = null;
        this.record_ID = 0;
        this.table_ID = 0;
        this.windowNo = 0;
        this.curTab = null;
        this.selectedRow = null;
        this.panelWidth = null;
    };

})(VAS, jQuery);
