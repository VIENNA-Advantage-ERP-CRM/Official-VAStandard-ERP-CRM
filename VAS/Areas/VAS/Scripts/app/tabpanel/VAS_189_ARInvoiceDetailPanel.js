/************************************************************
 * Module Name    : VAS
 * Purpose        : AR Invoice / AR Credit Note detail tab panel.
 *                  Renders the contextual sales-invoice panel (header,
 *                  action bar with Send Invoice, hero, lifecycle,
 *                  recurring banner, invoice details, line items,
 *                  totals, withholding, payment schedules, allocations,
 *                  delivery detail, approval, posted entry), the
 *                  Record Payment / Allocate Credit Note modal and the
 *                  Set up recurring invoice modal.
 * Class Used     : VAS.VAS_189_ARInvoiceDetailPanel
 * chronological  : Development
 *   VAI_145        Created  04 August 2026
 *
 * ── Labels / Message Keys ─────────────────────────────────────────────
 *  Panel
 *   No data to display                    | VAS_189_NoData
 *   Drafted / In progress / Completed     | VAS_189_Drafted / VAS_189_InProgress / VAS_189_Completed
 *   Closed / Voided / Reversed            | VAS_189_Closed / VAS_189_Voided / VAS_189_Reversed
 *   Sales invoice / Sales credit note     | VAS_189_SalesInvoice / VAS_189_SalesCreditNote
 *   Invoiced                              | VAS_189_Invoiced
 *   Record Receipt                        | VAS_189_RecordReceipt
 *   Allocate Credit Note                  | VAS_189_AllocateCreditNote
 *   Send Invoice                          | VAS_189_SendInvoice
 *   Download PDF                          | VAS_189_DownloadPDF
 *   View ledger entry                     | VAS_189_ViewLedgerEntry
 *   Gross Invoice Total                   | VAS_189_GrossInvoiceTotal
 *   Net Receivable / Paid                 | VAS_189_NetReceivable / VAS_189_Paid
 *   After Withholding                     | VAS_189_AfterWithholding
 *   Less Withholding                      | VAS_189_LessWithholding
 *   {0} days overdue / remaining          | VAS_189_DaysOverdue / VAS_189_DaysRemaining
 *   Lifecycle / Stage {0} of {1}          | VAS_189_Lifecycle / VAS_189_StageOf
 *   Complete / Posted / Delivered         | VAS_189_Complete / VAS_189_Posted / VAS_189_Delivered
 *   Payment Due                           | VAS_189_PaymentDue
 *   Eligible to make recurring            | VAS_189_EligibleToRecur
 *   Recurring schedule active             | VAS_189_RecurringActive
 *   All {0} lines are services or expenses| VAS_189_AllLinesServices
 *   No physical items detected            | VAS_189_NoPhysicalItems
 *   {0} of {1} lines are physical items   | VAS_189_SomePhysicalItems
 *   Next run {0} · {1} of {2} runs used   | VAS_189_RecurringNextRun
 *   Set up Recurring / Manage Recurring   | VAS_189_SetUpRecurring / VAS_189_ManageRecurring
 *   Invoice Details                       | VAS_189_InvoiceDetails
 *   Customer Reference No.                | VAS_189_CustomerReferenceNo
 *   Sales Order                           | VAS_189_SalesOrder
 *   Document Status / Posted Status       | VAS_189_DocumentStatus / VAS_189_PostedStatus
 *   Payment Terms / Payment Method        | VAS_189_PaymentTerms / VAS_189_PaymentMethod
 *   Invoice Currency / Accounting Currency| VAS_189_InvoiceCurrency / VAS_189_AccountingCurrency
 *   Representative                        | VAS_189_Representative
 *   Line Items / Item / UOM / Price / Tax | VAS_189_LineItems / VAS_189_Items / VAS_189_UOM / VAS_189_PriceEntered / VAS_189_Tax
 *   {0} goods lines                       | VAS_189_GoodsLinesCount
 *   Subtotal                              | VAS_189_Subtotal
 *   Withholding Details                   | VAS_189_WithholdingDetails
 *   Applied before customer payment       | VAS_189_AppliedBeforeCustomerPayment
 *   Withholding Type / Base Amount / Rate | VAS_189_WithholdingType / VAS_189_BaseAmount / VAS_189_Rate
 *   Withholding Amount                    | VAS_189_WithheldAmount
 *   Payment Schedules / Schedule / Status | VAS_189_PaymentSchedules / VAS_189_Schedule / VAS_189_Status
 *   Schedule {0} · {1}%                   | VAS_189_ScheduleLine
 *   {0} instalment(s)                     | VAS_189_InstalmentCount
 *   {0} of {1} schedules paid             | VAS_189_SchedulesPaid
 *   Settled {0} · Open {1}                | VAS_189_SettledOpen
 *   On hold                               | VAS_189_OnHold
 *   Allocations / {0} allocation(s)       | VAS_189_Allocations / VAS_189_AllocationCount
 *   Reference / Applied to                | VAS_189_Reference / VAS_189_AppliedTo
 *   Allocation No.                        | VAS_189_AllocationNo
 *   Receipt Allocation                    | VAS_189_ReceiptAllocation
 *   Allocated {0} · Discount {1} · Write-off {2} | VAS_189_AllocationFooter
 *   Credit Note                           | VAS_189_CreditNote
 *   Cash journal / cash                   | VAS_189_CashJournal / VAS_189_Cash
 *   GL journal                            | VAS_189_GLJournal
 *   reconciled / unreconciled             | VAS_189_Reconciled / VAS_189_Unreconciled
 *   No allocations yet                    | VAS_189_NoAllocations
 *   Delivery details / Delivery type      | VAS_189_DeliveryDetails / VAS_189_DeliveryType
 *   Goods / Services                      | VAS_189_Goods / VAS_189_Services
 *   Fulfilled / Partially fulfilled       | VAS_189_Fulfilled / VAS_189_PartiallyFulfilled
 *   Shipment / Delivered on / Delivered qty | VAS_189_Shipment / VAS_189_DeliveredOn / VAS_189_DeliveredQty
 *   Warehouse / Acknowledged by           | VAS_189_Warehouse / VAS_189_AcknowledgedBy
 *   Variance {0}                          | VAS_189_VarianceOf
 *   Approval / Approved                   | VAS_189_Approval / VAS_189_Approved
 *   Sent to customer                      | VAS_189_SentToCustomer
 *   Posted to ledger                      | VAS_189_PostedToLedger
 *   Posted Entry / Account / Debit/Credit | VAS_189_PostedJournalEntry / VAS_189_Account / VAS_189_Debit / VAS_189_Credit
 *   Total / Period {0} / Posting date {0} | VAS_189_Total / VAS_189_Period / VAS_189_PostingDate
 *   Balanced - Dr = Cr                    | VAS_189_Balanced
 *   Showing {0} of {1}                    | VAS_189_Showing
 *   Previous / Next / Date / Amount       | VAS_189_Previous / VAS_189_Next / VAS_189_Date / VAS_189_Amount
 *
 *  Receipt modal (mirrors the AP panel wording, sales side)
 *   Customer Outstanding                  | VAS_189_CustomerOutstanding
 *   Gross Invoice / Withholding           | VAS_189_GrossInvoice / VAS_189_Withholding
 *   Available to Apply                    | VAS_189_AvailableToApply
 *   Apply on-account & credits            | VAS_189_ApplyOnAccountCredits
 *   Use these first - no new receipt      | VAS_189_UseTheseFirst
 *   Payment / Credit                      | VAS_189_PaymentCredit
 *   Available / Selected / Allocated      | VAS_189_Available / VAS_189_SelectedState / VAS_189_Allocated
 *   Apply Selected Credits                | VAS_189_ApplySelectedCredits
 *   Credits applied                       | VAS_189_CreditsApplied
 *   {0} allocated. …recalculated.         | VAS_189_CreditsAppliedSummary
 *   {0} selected for allocation.          | VAS_189_SelectedForAllocation
 *   Select available payments…            | VAS_189_SelectCreditsHint
 *   Allocation transaction created…       | VAS_189_AllocationStatusMsg
 *   New Receipt                           | VAS_189_NewReceipt
 *   For the balance after credits         | VAS_189_ForBalanceAfterCredits
 *   No balance left to receive            | VAS_189_NoBalanceToReceive
 *   All payment schedules …are paid.      | VAS_189_AllSchedulesPaidMsg
 *   All payment schedules are paid - nothing to allocate | VAS_189_NothingOpenToAllocate
 *   Receipt Amount / Receipt Date         | VAS_189_ReceiptAmount / VAS_189_ReceiptDate
 *   Bank Account / Conversion Type        | VAS_189_BankAccount / VAS_189_ConversionType
 *   Discount / Select / Check Date/no     | VAS_189_Discount / VAS_189_SelectOption / VAS_189_CheckDate / VAS_189_CheckNo
 *   Settling this Invoice                 | VAS_189_SettlingThisInvoice
 *   Remaining Balance                     | VAS_189_RemainingBalance
 *   Overpayment …customer advance.        | VAS_189_OverpaymentNote
 *   Amount cannot exceed the open…        | VAS_189_AmountExceedsOpen
 *   No conversion rate found…             | VAS_189_NoConversionRate
 *   Invoice fully settled                 | VAS_189_InvoiceFullySettled
 *   {0} will remain open                  | VAS_189_WillRemainOpen
 *   Complete settlement                   | VAS_189_CompleteSettlement
 *   Only …single currency…                | VAS_189_SingleCurrencyOnly
 *   …same accounting date…                | VAS_189_SameAcctDateOnly
 *   …same conversion type…                | VAS_189_SameConvTypeOnly
 *   Enter a receipt amount before…discount| VAS_189_DiscountNeedsPayment
 *   Please select a payment method.       | VAS_189_PaymentMethodRequired
 *   Please select a bank account.         | VAS_189_BankAccountRequired
 *   Check date is required…               | VAS_189_CheckDateRequired
 *   Check number is required…             | VAS_189_ReferenceRequired
 *   Receipt recorded                      | VAS_189_PaymentRecorded
 *   The receipt {0} and allocation…       | VAS_189_PaymentRecordedMsg
 *   Credit applied / …allocated…          | VAS_189_CreditApplied / VAS_189_CreditAppliedMsg
 *   Allocation created                    | VAS_189_AllocationCreated
 *   Allocation document {0} created…      | VAS_189_AllocationCreatedNo
 *   Could not load the requested…         | VAS_189_LoadFailed
 *   The action could not be completed.    | VAS_189_ActionFailed
 *   Cancel / Done                         | VAS_189_Cancel / VAS_189_Done
 *
 *  Recurring modal
 *   Set up Recurring Invoice              | VAS_189_SetUpRecurringTitle
 *   Close                                 | VAS_189_Close
 *   Schedule                              | VAS_189_Schedule
 *   Defines when each invoice is generated| VAS_189_ScheduleHint
 *   Frequency Type                        | VAS_189_FrequencyType
 *   Weekly / Monthly / Quarterly / Annually | VAS_189_Weekly / VAS_189_Monthly / VAS_189_Quarterly / VAS_189_Annually
 *   Daily (no button; stored schedules only) | VAS_189_Daily
 *   day(s)/week(s)/month(s)/quarter(s)/year(s) | VAS_189_DayUnit / VAS_189_WeekUnit / VAS_189_MonthUnit / VAS_189_QuarterUnit / VAS_189_YearUnit
 *   Repeat Every                          | VAS_189_RepeatEvery
 *   Maximum Runs / Invoice(s)             | VAS_189_MaximumRuns / VAS_189_InvoiceUnit
 *   Increase / Decrease (stepper a11y)    | VAS_189_Increase / VAS_189_Decrease
 *   Date Next Run                         | VAS_189_DateNextRun
 *   Schedule Ends / after run {0}         | VAS_189_ScheduleEnds / VAS_189_AfterRun
 *   Description                           | VAS_189_Description
 *   Note shown on every generated invoice…| VAS_189_DescriptionPlaceholder
 *   Upcoming Occurrences                  | VAS_189_UpcomingOccurrences
 *   {0} invoice(s) · {1} each             | VAS_189_OccurrenceHint
 *   Date Last Run / Remaining Runs        | VAS_189_DateLastRun / VAS_189_RemainingRuns
 *   of {0}                                | VAS_189_OfCount
 *   Scheduled Value / Recurrence          | VAS_189_ScheduledValue / VAS_189_Recurrence
 *   Every {0} {1}                         | VAS_189_EveryN
 *   Run # / Expected Date                 | VAS_189_RunNo / VAS_189_ExpectedDate
 *   Created Invoice / Invoice no          | VAS_189_CreatedInvoiceNo
 *   Status                                | VAS_189_Status
 *   Document Amount / Currency            | VAS_189_DocumentAmount / VAS_189_Currency
 *   Created / Scheduled / To be created   | VAS_189_Created / VAS_189_Scheduled / VAS_189_ToBeCreated
 *   Showing the next {0} of {1} …         | VAS_189_ShowingNextOccurrences
 *   Fields marked / are required          | VAS_189_RequiredLegendPre / VAS_189_RequiredLegendPost
 *   Save Schedule                         | VAS_189_SaveSchedule
 *   Select a frequency type.              | VAS_189_SelectFrequencyType
 *   Frequency must be greater than zero.  | VAS_189_FrequencyGreaterZero
 *   Maximum Runs must be at least 1.      | VAS_189_MaxRunsAtLeastOne
 *   Enter the next run date.              | VAS_189_EnterNextRunDate
 *   The recurring schedule was saved…     | VAS_189_RecurringSaved
 *   The recurring schedule could not be…  | VAS_189_RecurringNotSaved
 *   Discard the changes made to this…     | VAS_189_DiscardRecurringChanges
 *   Confirm                               | VAS_189_Confirm
 * ──────────────────────────────────────────────────────────────────────
 ***********************************************************/
; VAS = window.VAS || {};
; (function (VAS, $) {

    VAS.VAS_189_ARInvoiceDetailPanel = function () {
        this.record_ID = 0;
        this.table_ID = 0;
        this.windowNo = 0;
        this.AD_Window_ID = 0;
        this.curTab = null;
        this.selectedRow = null;
        this.panelWidth;
        // Sales panel: every server call carries the transaction flag from here
        // rather than letting the server assume it.
        this.IsSOTrx = true;

        var $self = this;
        var $root, $busy, $body, $emptyState;
        var data = null;
        var meta = null;
        var $scrim = null;
        var $recScrim = null;
        // Set once an allocation / receipt is created from the modal: the panel data
        // (open amount, schedules, action buttons) is stale and must be re-fetched
        // when the modal closes, so "Record payment" turns read-only once nothing is
        // open.
        var panelDirty = false;
        var LINES_PER_PAGE = 20;
        var linePage = 0;

        var CLS = "vas_189_arinv-";

        var ARInvZoomWindowId = 0;
        var ARInv_ZOOM_WINDOW_NAME = 'VAS_ARInvoice';

        /* Allocation header and AR receipt zoom targets. Each id is resolved from
           the name at runtime (ids differ per environment) and cached for later
           clicks. */
        var AllocZoomWindowId = 0;
        var Alloc_ZOOM_WINDOW_NAME = 'VAS_ViewAllocation';

        var ReceiptZoomWindowId = 0;
        var Receipt_ZOOM_WINDOW_NAME = 'VAS_ARReceipt';

        /* Cash / GL journal targets also carry the pre-VAS window name, which is
           what an older install still has the window registered under. */
        var CashZoomWindowId = 0;
        var Cash_ZOOM_WINDOW_NAME = 'VAS_CashJournal';
        var Cash_ZOOM_WINDOW_NAME_OLD = 'Cash Journal';

        var JournalZoomWindowId = 0;
        var Journal_ZOOM_WINDOW_NAME = 'VAS_GLJournal';
        var Journal_ZOOM_WINDOW_NAME_OLD = 'GL Journal';

        /* ---------- short helpers ---------- */
        // Returns the dictionary message for `key`. VIS.Msg.getMsg wraps unknown keys
        // in '[...]'; when that happens we use `fallback` if one was supplied,
        // otherwise the raw '[KEY]' is kept.
        function lbl(key, fallback) {
            var t = VIS.Msg.getMsg(key);
            if (t && t.charAt(0) !== '[') return t;
            return (fallback !== undefined) ? fallback : t;
        }

        function fmtAmount(value, precision) {
            var v = +value || 0;
            var sign = v < 0 ? "-" : "";
            var abs = Math.abs(v);
            var cur = (data && (data.CurSymbol || data.CurISO)) || "";
            var p = (precision >= 0) ? precision : ((data && data.StdPrecision >= 0) ? data.StdPrecision : 2);
            return sign + (cur ? cur : "") + abs.toLocaleString(window.navigator.language, {
                minimumFractionDigits: p, maximumFractionDigits: p
            });
        }

        function fmtAmountCur(value, cur, precision) {
            var v = +value || 0;
            var p = (precision >= 0) ? precision : 2;
            return (cur ? cur : "") + Math.abs(v).toLocaleString(window.navigator.language, {
                minimumFractionDigits: p, maximumFractionDigits: p
            });
        }

        function fmtNumber(value, precision) {
            var p = (precision >= 0) ? precision : 0;
            return (+value || 0).toLocaleString(window.navigator.language, {
                minimumFractionDigits: p, maximumFractionDigits: p
            });
        }

        function fmtPct(value) {
            return fmtNumber(value, 2) + "%";
        }

        function fmtDate(value) {
            if (!value) return "";
            var d = (value instanceof Date) ? value : new Date(value);
            if (isNaN(d.getTime())) return "";
            try {
                return d.toLocaleDateString(window.navigator.language, {
                    year: "numeric", month: "short", day: "2-digit"
                });
            } catch (e) { return d.toDateString(); }
        }

        /* Normalises a date value to its yyyy-mm-dd portion for equality comparison. */
        function acctKey(value) {
            if (!value) return "";
            var s = (value instanceof Date) ? value.toISOString() : String(value);
            return s.slice(0, 10);
        }

        /* Whole-day difference between a date and today (positive = future). */
        function dayDelta(value) {
            if (!value) return 0;
            var d = (value instanceof Date) ? value : new Date(value);
            if (isNaN(d.getTime())) return 0;
            var due = new Date(d.getFullYear(), d.getMonth(), d.getDate());
            var now = new Date();
            var today = new Date(now.getFullYear(), now.getMonth(), now.getDate());
            return Math.round((due - today) / 86400000);
        }

        function docStatusLabel(code) {
            switch (code) {
                case "DR": return lbl("VAS_189_Drafted", "Drafted");
                case "IP": return lbl("VAS_189_InProgress", "In progress");
                case "CO": return lbl("VAS_189_Completed", "Completed");
                case "CL": return lbl("VAS_189_Closed", "Closed");
                case "VO": return lbl("VAS_189_Voided", "Voided");
                case "RE": return lbl("VAS_189_Reversed", "Reversed");
            }
            return code || "";
        }

        // Reversing an invoice leaves the document on DocStatus 'RE'. The panel says
        // "Reversed" wherever it would otherwise say "Completed" - a reversed document
        // is finished, but it is not a good invoice.
        function isReversed() {
            return !!(data && (data.IsReversed || data.DocStatus === "RE"));
        }

        // The document reached its end state (nothing further happens on its own).
        function isDocFinal() {
            return !!(data && ["CO", "CL", "RE", "VO"].indexOf(data.DocStatus) >= 0);
        }

        // Status wording for the lifecycle / section chips: the in-progress, reversed
        // and voided states win over the generic "Completed".
        function docStateLabel() {
            if (isReversed()) return lbl("VAS_189_Reversed", "Reversed");
            if (data && data.DocStatus === "VO") return lbl("VAS_189_Voided", "Voided");
            if (data && data.DocStatus === "CL") return lbl("VAS_189_Closed", "Closed");
            if (data && data.DocStatus === "IP") return lbl("VAS_189_InProgress", "In progress");
            return lbl("VAS_189_Completed", "Completed");
        }

        function escapeHtml(s) {
            return String(s == null ? "" : s)
                .replace(/&/g, "&amp;").replace(/</g, "&lt;").replace(/>/g, "&gt;")
                .replace(/"/g, "&quot;").replace(/'/g, "&#39;");
        }

        function info(msg) {
            if (VIS && VIS.ADialog && VIS.ADialog.info) VIS.ADialog.info("", "", msg); else console.log(msg);
        }

        function error(msg) {
            if (VIS && VIS.ADialog && VIS.ADialog.error) VIS.ADialog.error("", "", msg); else console.log(msg);
        }

        /* ---------- lifecycle ---------- */
        this.init = function () {
            $root = $('<div class="' + CLS + 'root"></div>');
            $body = $('<div class="' + CLS + 'body"></div>');
            $emptyState = $('<div class="' + CLS + 'empty" style="display:none;"></div>');
            $emptyState.text(lbl("VAS_189_NoData", "No data to display"));
            $root.append($body).append($emptyState);
            createBusyIndicator();
        };

        function createBusyIndicator() {
            $busy = $('<div class="vis-apanel-busy">' +
                '<div class="vis-busyindicatorinnerwrap"><i class="vis_widgetloader"></i></div>' +
                '</div>');
            $busy.css({ "position": "absolute", "width": "100%", "height": "100%", "text-align": "center", "z-index": "999" });
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
                url: VIS.Application.contextUrl + "VAS_189_ARInvoiceDetailPanel/GetPanelData",
                type: "GET",
                dataType: "json",
                // The sales flag is supplied by the panel, not assumed on the server.
                data: { C_Invoice_ID: recordID, IsSOTrx: !!$self.IsSOTrx },
                success: function (raw) {
                    data = (typeof raw === "string") ? jQuery.parseJSON(raw) : raw;
                    linePage = 0;
                    render();
                    // New record -> always start at the top (the scroll position must
                    // not carry over from the previously selected record).
                    if ($root && $root[0]) {
                        $root.scrollTop(0);
                    }
                    showBusy(false);
                },
                error: function (err) { console.log(err); showBusy(false); }
            });
        };

        this.clear = function () { data = null; render(); };

        /* ---------- render ---------- */
        function render() {
            $body.empty();
            if (!data || !data.C_Invoice_ID) {
                $body.hide();
                $emptyState.show();
                return;
            }
            $emptyState.hide();
            $body.show();

            var $panel = $('<div class="' + CLS + 'panel"></div>');
            $panel.append(buildHeader());
            $panel.append(buildActions());
            $panel.append(buildHero());

            var $life = buildLifecycle();
            if ($life) $panel.append($life);

            // Recurring banner sits directly under the lifecycle (design reference).
            var $rec = buildRecurringBanner();
            if ($rec) $panel.append($rec);

            var $details = buildInvoiceDetails();
            if ($details) $panel.append($details);

            var $lines = buildLineItems();
            if ($lines) $panel.append($lines);

            $panel.append(buildTotals());

            var $wh = buildWithholding();
            if ($wh) $panel.append($wh);

            // Reading order per the design: Schedule -> Allocation -> Delivery
            // detail + Approval -> Posting detail.
            var $sched = buildPaymentSchedule();
            if ($sched) $panel.append($sched);

            var $alloc = buildAllocations();
            if ($alloc) $panel.append($alloc);

            var $twoCol = buildDeliveryAndApproval();
            if ($twoCol) $panel.append($twoCol);

            var $journal = buildPostedJournal();
            if ($journal) $panel.append($journal);

            $body.append($panel);
        }

        /* ---------- shared primitives ---------- */

        /* Section header: title + optional summary and chip. */
        function section(title, summary, chipText, chipTone) {
            var $sec = $('<section class="' + CLS + 'sec"></section>');
            var $head = $('<div class="' + CLS + 'sec-head"></div>');
            $head.append($('<span class="' + CLS + 'sec-h"></span>').text(title));
            if (summary || chipText) {
                var $r = $('<span class="' + CLS + 'sec-r"></span>');
                if (summary) $r.append($('<span></span>').text(summary));
                if (chipText) $r.append(chip(chipText, chipTone));
                $head.append($r);
            }
            $sec.append($head);
            return $sec;
        }

        function chip(text, tone) {
            return $('<span class="' + CLS + 'chip ' + (tone || "neutral") + '"></span>').text(text);
        }

        /* One metric-grid cell. `value` may be a string or a node. */
        function metric($grid, label, value, meta2, tone) {
            var isNode = !!(value && (value.jquery || value.nodeType));
            if (!isNode && (value == null || String(value).length === 0)) return;
            var $c = $('<div class="' + CLS + 'mcell"></div>');
            $c.append($('<div class="k"></div>').text(label));
            var $v = $('<div class="v ' + (tone || "") + '"></div>');
            if (isNode) { $v.append(value); } else { $v.text(value).attr("title", String(value)); }
            $c.append($v);
            if (meta2) $c.append($('<div class="m"></div>').text(meta2).attr("title", String(meta2)));
            $grid.append($c);
        }

        /* Metric cell whose qualifier sits INLINE after the value - "value (note)". */
        function metricNote($grid, label, value, note, tone) {
            var full = value + (note ? " (" + note + ")" : "");
            var $c = $('<div class="' + CLS + 'mcell"></div>');
            $c.append($('<div class="k"></div>').text(label));
            var $v = $('<div class="v ' + (tone || "") + '"></div>').text(value).attr("title", full);
            if (note) $v.append($('<span class="mi"></span>').text("(" + note + ")"));
            $c.append($v);
            $grid.append($c);
        }

        /* Footer pager: "Showing x-y of N" left, Previous / Next right.
           `go(page)` takes the zero-based target page. */
        function footPager($foot, total, page, perPage, shown, go) {
            $foot.empty().show();
            var pageCount = Math.max(1, Math.ceil(total / perPage));
            var start = page * perPage;
            $foot.append($('<span></span>').text(
                lbl("VAS_189_Showing", "Showing {0} of {1}")
                    .replace("{0}", (start + 1) + "–" + (start + shown))
                    .replace("{1}", total)));
            var $nav = $('<span class="' + CLS + 'pager"></span>');
            var $prev = $('<button type="button" class="' + CLS + 'pagebtn"></button>').text(lbl("VAS_189_Previous", "Previous"));
            var $next = $('<button type="button" class="' + CLS + 'pagebtn"></button>').text(lbl("VAS_189_Next", "Next"));
            $prev.prop("disabled", page <= 0).on("click", function () { if (page > 0) go(page - 1); });
            $next.prop("disabled", page >= pageCount - 1).on("click", function () { if (page < pageCount - 1) go(page + 1); });
            $nav.append($prev).append($next);
            $foot.append($nav);
        }

        /* Keeps a metric cell occupied when the record has no value for it, so the
           fixed left/right pairing of the hero card never shifts. */
        function dash(v) {
            return (v == null || String(v).length === 0) ? "—" : v;
        }

        function subLine(name, date) {
            var bits = [];
            if (name) bits.push(name);
            if (date) bits.push(fmtDate(date));
            return bits.join(" · ");
        }

        /* Header (customer name + context line) */
        function buildHeader() {
            var $h = $(
                '<header class="' + CLS + 'head">' +
                '<div class="' + CLS + 'headLeft">' +
                '<div class="' + CLS + 'h1 js-bp-name"></div>' +
                '<div class="' + CLS + 'sub js-bp-sub"></div>' +
                '</div>' +
                '<div class="' + CLS + 'ctx">' +
                '<b class="js-doc-no"></b>' +
                '<span class="js-doc-type"></span>' +
                '<span class="js-doc-date"></span>' +
                '</div>' +
                '</header>'
            );
            $h.find(".js-bp-name").text(data.BPName || "");
            // Sub-line: customer location (city, country, postal) then the BP code.
            var locBits = [];
            if (data.BPCity) locBits.push(data.BPCity);
            if (data.BPCountry) locBits.push(data.BPCountry);
            if (data.BPPostal) locBits.push(data.BPPostal);
            var sub = [];
            if (locBits.length) sub.push(locBits.join(", "));
            if (data.BPValue) sub.push(data.BPValue);
            $h.find(".js-bp-sub").text(sub.join(" · "));
            $h.find(".js-doc-no").text(data.DocumentNo || "");
            var typeText = data.DocTypeName
                || (data.IsARCreditNote ? lbl("VAS_189_SalesCreditNote", "Sales credit note")
                    : lbl("VAS_189_SalesInvoice", "Sales invoice"));
            $h.find(".js-doc-type").text(" · " + typeText);
            if (data.DateInvoiced) {
                $h.find(".js-doc-date").text(" · " + lbl("VAS_189_Invoiced", "Invoiced") + " " + fmtDate(data.DateInvoiced));
            }
            return $h;
        }

        /* Action bar */
        function buildActions() {
            var $a = $('<div class="' + CLS + 'actions"></div>');

            var isCN = data.IsARCreditNote;
            var openAmt = +data.OpenAmount || 0;
            var isCompleted = (data.DocStatus === "CO" || data.DocStatus === "CL");
            // Every pay schedule marked paid (VA009_IsPaid) leaves nothing to record,
            // even if the invoice header has not been flagged paid yet.
            var allSchedulesPaid = (+data.ScheduleTotal > 0) && ((+data.ScheduleOpenAmount || 0) <= 0);
            var settled = !!data.IsPaid || allSchedulesPaid || openAmt <= 0;
            var canPay = !settled && isCompleted;

            // One primary action per document type: credit notes get "Allocate credit
            // note", everything else "Record payment". Read-only (disabled) unless an
            // open balance remains on a completed / closed document.
            var $payBtn = $('<button type="button" class="' + CLS + 'btn is-primary"><i class="fa fa-credit-card" aria-hidden="true"></i></button>')
                .append($('<span></span>').text(isCN ? lbl("VAS_189_AllocateCreditNote", "Allocate credit note")
                    : lbl("VAS_189_RecordReceipt", "Record Receipt")))
                .prop("disabled", !canPay)
                .on("click", function () { if (canPay) openPaymentModal(); });
            if (settled && isCompleted) {
                $payBtn.attr("title", lbl("VAS_189_AllSchedulesPaidMsg",
                    "All payment schedules of this invoice are paid. No further payment can be recorded."));
            }
            $a.append($payBtn);

            // Send invoice and Download PDF both run off the tab's print process, so
            // they share the same enablement test (nothing to print without it).
            var hasPrintProcess = $self.curTab
                && typeof $self.curTab.getAD_Process_ID === "function"
                && +$self.curTab.getAD_Process_ID() > 0;

            $a.append($('<button type="button" class="' + CLS + 'btn"><i class="fa fa-paper-plane-o" aria-hidden="true"></i></button>')
                .append($('<span></span>').text(lbl("VAS_189_SendInvoice", "Send invoice")))
                .prop("disabled", !hasPrintProcess)
                .on("click", function () { if (hasPrintProcess) sendInvoiceEmail(); }));

            $a.append($('<button type="button" class="' + CLS + 'btn"><i class="fa fa-download" aria-hidden="true"></i></button>')
                .append($('<span></span>').text(lbl("VAS_189_DownloadPDF", "Download PDF")))
                .prop("disabled", !hasPrintProcess)
                .on("click", function () { if (hasPrintProcess) downloadInvoicePDF(); }));

            // View ledger entry only when posted facts exist.
            if (data.PostedJournal && data.PostedJournal.Rows && data.PostedJournal.Rows.length) {
                $a.append($('<button type="button" class="' + CLS + 'btn"><i class="fa fa-book" aria-hidden="true"></i></button>')
                    .append($('<span></span>').text(lbl("VAS_189_ViewLedgerEntry", "View ledger entry")))
                    .on("click", function () { scrollToSection(CLS + "journal"); }));
            }
            return $a;
        }

        /* Hero status card - the panel's single headline fact (what is still owed,
           or that the invoice is settled). */
        function buildHero() {
            var openAmt = +data.OpenAmount || 0;
            var settled = data.IsPaid || openAmt <= 0;
            var dayDiff = data.DueDate ? dayDelta(data.DueDate) : 0;
            var overdue = !settled && data.DueDate && dayDiff < 0;

            var tone = settled ? "ok" : (overdue ? "risk" : (openAmt > 0 ? "warn" : "info"));
            var $hero = $('<div class="' + CLS + 'hero ' + tone + '"></div>');

            // Fixed 3x2 reading order - left column identifies the document, right
            // column carries the money. Cells always render (em-dash when a value is
            // missing) so the pairing never shifts.
            var $grid = $('<div class="' + CLS + 'mgrid"></div>');
            var hasWh = +data.WithholdingAmount > 0;

            metric($grid, lbl("VAS_189_BPartner_ID", "Customer"), dash(data.BPName), data.BPTaxID || "");
            metric($grid, lbl("VAS_189_GrossInvoiceTotal", "Gross invoice total"), fmtAmount(data.GrandTotal));

            metric($grid, lbl("InvoiceNo"), dash(data.DocumentNo));
            metricNote($grid,
                settled ? lbl("VAS_189_Paid", "Paid") : lbl("VAS_189_NetReceivable", "Net receivable"),
                fmtAmount(settled ? data.NetReceivable : openAmt),
                hasWh ? lbl("VAS_189_AfterWithholding", "After withholding") : "",
                tone === "info" ? "" : tone);

            var dueMeta = "";
            if (data.DueDate && !settled) {
                dueMeta = overdue
                    ? lbl("VAS_189_DaysOverdue", "{0} days overdue").replace("{0}", Math.abs(dayDiff))
                    : lbl("VAS_189_DaysRemaining", "{0} days remaining").replace("{0}", Math.abs(dayDiff));
            }
            metricNote($grid, lbl("DueDate"), dash(data.DueDate ? fmtDate(data.DueDate) : ""), dueMeta);
            metric($grid, lbl("VAS_189_LessWithholding", "Less withholding"),
                hasWh ? fmtAmount(data.WithholdingAmount) : "—", "", hasWh ? "warn" : "");

            $hero.append($grid);
            return $hero;
        }

        /* Lifecycle stepper - Drafted -> (In progress / Completed / Reversed) ->
           Delivered -> Posted -> Paid / Payment Due. */
        function buildLifecycle() {
            var steps = [];
            var reachedEnd = isDocFinal();
            var isDraft = data.DocStatus === "DR";

            steps.push({
                t: lbl("VAS_189_Drafted", "Drafted"),
                sub: subLine(data.CreatedByName, data.Created),
                done: !isDraft,
                active: isDraft
            });
            // Where the document went after the draft. A document still on Drafted has
            // not reached this stage, so it is left out.
            if (!isDraft) {
                steps.push({
                    t: docStateLabel(),
                    sub: subLine("", data.Updated),
                    done: reachedEnd,
                    active: !reachedEnd
                });
            }

            var dl = data.Delivery;
            var hasDelivery = dl && dl.Rows && dl.Rows.length;
            if (hasDelivery) {
                steps.push({
                    t: lbl("VAS_189_Delivered", "Delivered"),
                    sub: subLine(dl.ShipmentDocumentNo, dl.DeliveredDate),
                    done: true
                });
            }
            if (data.Posted === "Y") {
                steps.push({ t: lbl("VAS_189_Posted", "Posted"), sub: subLine("", data.DateAcct), done: true });
            }
            if (data.IsPaid) {
                steps.push({ t: lbl("VAS_189_Paid", "Paid"), sub: "", done: true });
            } else if (+data.OpenAmount > 0 && (data.DocStatus === "CO" || data.DocStatus === "CL")) {
                steps.push({
                    t: lbl("VAS_189_PaymentDue", "Payment Due"),
                    sub: data.DueDate ? lbl("DueDate") + " " + fmtDate(data.DueDate) : "",
                    done: false, active: true
                });
            }

            if (steps.length < 2) return null;

            var activeIdx = -1;
            for (var k = 0; k < steps.length; k++) {
                if (steps[k].active) activeIdx = k;
            }
            // A reversed / voided document is not "Complete" - name the end state it
            // actually reached.
            var summary = (activeIdx >= 0)
                ? lbl("VAS_189_StageOf", "Stage {0} of {1}").replace("{0}", activeIdx + 1).replace("{1}", steps.length)
                : ((isReversed() || data.DocStatus === "VO")
                    ? docStateLabel()
                    : lbl("VAS_189_Complete", "Complete"));

            var $sec = section(lbl("VAS_189_Lifecycle", "Lifecycle"), summary);
            var $pipe = $('<div class="' + CLS + 'pipe"></div>');
            var $grid = $('<div class="' + CLS + 'pipe-grid"></div>')
                .css("grid-template-columns", "repeat(" + steps.length + ", minmax(0, 1fr))");

            for (var i = 0; i < steps.length; i++) {
                var st = steps[i];
                var state = st.active ? "active" : (st.done ? "done" : "pending");
                var $col = $('<div class="' + CLS + 'pipe-col"></div>');
                // Trailing rail segment: blue once the circle on its left is done.
                if (i < steps.length - 1) {
                    $col.append($('<span class="' + CLS + 'pipe-seg ' + (st.done ? "done" : "") + '"></span>'));
                }
                var $dot = $('<span class="' + CLS + 'pipe-dot ' + state + '"></span>');
                if (state === "done") $dot.append('<i class="fa fa-check" aria-hidden="true"></i>');
                else if (state === "active") $dot.append('<i></i>');
                $col.append($dot);
                $col.append($('<div class="' + CLS + 'pipe-lbl ' + state + '"></div>').text(st.t).attr("title", st.t));
                // Each "·"-separated fact gets its own line so a stage never truncates
                // the parts that follow the separator.
                if (st.sub) {
                    (function (sub) {
                        sub.split("·").forEach(function (part) {
                            var t = part.trim();
                            if (t) $col.append($('<div class="' + CLS + 'pipe-sub"></div>').text(t).attr("title", sub));
                        });
                    })(st.sub);
                }
                $grid.append($col);
            }
            $pipe.append($grid);
            $sec.append($pipe);
            return $sec;
        }

        /* Recurring banner - sits under the lifecycle. Shown on a completed /
           closed document only: the recurring process copies a finished invoice.
           Reads "Eligible to make recurring" when no schedule exists yet, and the
           active schedule state once one does. */
        function buildRecurringBanner() {
            if (!data || (data.DocStatus !== "CO" && data.DocStatus !== "CL")) return null;

            var rec = data.Recurring || {};
            var lines = data.Lines || [];
            var physical = lines.filter(function (l) { return l.IsPhysicalItem; }).length;
            var servicesOnly = lines.length > 0 && physical === 0;

            var $b = $(
                '<section class="' + CLS + 'banner">' +
                '<div class="' + CLS + 'banner-l">' +
                '<span class="' + CLS + 'well"><i class="fa fa-refresh" aria-hidden="true"></i></span>' +
                '<div class="' + CLS + 'banner-tx">' +
                '<div class="' + CLS + 'banner-a"></div>' +
                '<div class="' + CLS + 'banner-b"></div>' +
                '</div>' +
                '</div>' +
                '<button type="button" class="' + CLS + 'btn is-primary js-recur"></button>' +
                '</section>'
            );

            if (rec.Exists) {
                $b.addClass("is-active");
                $b.find("." + CLS + "banner-a").text(lbl("VAS_189_RecurringActive", "Recurring schedule active"));
                $b.find("." + CLS + "banner-b").text(
                    lbl("VAS_189_RecurringNextRun", "Next run {0} · {1} of {2} runs used")
                        .replace("{0}", rec.DateNextRun ? fmtDate(rec.DateNextRun) : "—")
                        .replace("{1}", +rec.GeneratedRuns || 0)
                        .replace("{2}", +rec.RunsMax || 0));
                $b.find(".js-recur").text(lbl("VAS_189_ManageRecurring", "Manage recurring"));
            } else {
                $b.find("." + CLS + "banner-a").text(lbl("VAS_189_EligibleToRecur", "Eligible to make recurring"));
                // Sub-line states what the lines actually are, so the claim is checkable.
                var subText = servicesOnly
                    ? lbl("VAS_189_AllLinesServices", "All {0} lines are services or expenses").replace("{0}", lines.length)
                    + " · " + lbl("VAS_189_NoPhysicalItems", "No physical items detected")
                    : lbl("VAS_189_SomePhysicalItems", "{0} of {1} lines are physical items")
                        .replace("{0}", physical).replace("{1}", lines.length);
                $b.find("." + CLS + "banner-b").text(subText);
                $b.find(".js-recur").text(lbl("VAS_189_SetUpRecurring", "Set up recurring"));
            }

            $b.find(".js-recur").on("click", function () { openRecurringModal(); });
            return $b;
        }

        /* Invoice details (key/value pairs - only non-empty rows) */
        function buildInvoiceDetails() {
            var rows = [
                { k: lbl("VAS_189_CustomerReferenceNo", "Customer Reference No."), v: data.InvoiceReference },
                { k: lbl("InvoiceNo"), v: data.DocumentNo },
                { k: lbl("VAS_189_SalesOrder", "Sales Order"), v: data.OrderDocumentNo },
                { k: lbl("VAS_189_DocumentStatus", "Document Status"), v: data.DocStatusName || docStatusLabel(data.DocStatus) },
                { k: lbl("VAS_189_PostedStatus", "Posted Status"), v: data.PostedName || (data.Posted ? data.Posted : "") },
                { k: lbl("VAS_189_PaymentTerms", "Payment Terms"), v: data.PaymentTermName },
                { k: lbl("VAS_189_PaymentMethod", "Payment Method"), v: data.PaymentMethodName },
                { k: lbl("VAS_189_InvoiceCurrency", "Invoice Currency"), v: data.CurISO },
                { k: lbl("VAS_189_AccountingCurrency", "Accounting Currency"), v: data.AcctCurISO },
                { k: lbl("VAS_189_Representative", "Representative"), v: data.RepresentativeName }
            ];
            var visible = rows.filter(function (r) { return r.v != null && String(r.v).length > 0; });
            if (!visible.length) return null;

            var $sec = $(
                '<section class="' + CLS + 'block">' +
                '<div class="' + CLS + 'block-head">' +
                '<span class="' + CLS + 'block-h"></span>' +
                '</div>' +
                '<div class="' + CLS + 'kv cols2 js-kv"></div>' +
                '</section>'
            );
            $sec.find("." + CLS + "block-h").text(lbl("VAS_189_InvoiceDetails", "Invoice details"));

            var $kv = $sec.find(".js-kv");
            visible.forEach(function (r) {
                $kv.append($('<div class="' + CLS + 'kv-r"></div>')
                    .append($('<span class="' + CLS + 'kv-k"></span>').text(r.k))
                    .append($('<span class="' + CLS + 'kv-v"></span>').text(r.v).attr("title", String(r.v))));
            });
            return $sec;
        }

        /* One key/value row. `tone` tints the row + value and `note` adds a second
           line under the value (e.g. the size of a variance). */
        function kvRow($kv, k, v, tone, note) {
            if (v == null || String(v).length === 0) return;
            var $r = $('<div class="' + CLS + 'kv-r"></div>');
            if (tone) $r.addClass("variance");
            var $v = $('<span class="' + CLS + 'kv-v ' + (tone || "") + '"></span>')
                .text(v).attr("title", String(v));
            if (note) $v.append($('<span class="delta"></span>').text(note));
            $kv.append($r.append($('<span class="' + CLS + 'kv-k"></span>').text(k)).append($v));
        }

        /* ---------- line items ---------- */
        function buildLineItems() {
            var lines = (data.Lines || []);
            if (!lines.length) return null;

            var anyTax = lines.some(function (l) { return +l.TaxRate > 0; });
            var goodsCount = lines.filter(function (l) { return l.IsProductLine; }).length;

            var $sec = $(
                '<section class="' + CLS + 'block">' +
                '<div class="' + CLS + 'block-head">' +
                '<span class="' + CLS + 'block-h"></span>' +
                '<span class="' + CLS + 'block-r js-count"></span>' +
                '</div>' +
                '<div class="' + CLS + 'li-head">' +
                '<span class="js-c-desc"></span>' +
                '<span class="right js-c-qty"></span>' +
                '<span class="js-c-uom"></span>' +
                '<span class="right js-c-rate"></span>' +
                '<span class="right js-c-tax"></span>' +
                '<span class="right js-c-amt"></span>' +
                '</div>' +
                '<div class="js-rows"></div>' +
                '<div class="' + CLS + 'block-foot js-foot" style="display:none;"></div>' +
                '</section>'
            );
            $sec.find("." + CLS + "block-h").text(lbl("VAS_189_LineItems", "Line Items"));
            $sec.find(".js-count").text(lbl("VAS_189_GoodsLinesCount", "{0} goods lines").replace("{0}", goodsCount));
            $sec.find(".js-c-desc").text(lbl("VAS_189_Items", "Item"));
            $sec.find(".js-c-qty").text(lbl("Quantity"));
            $sec.find(".js-c-uom").text(lbl("VAS_189_UOM", "UOM"));
            $sec.find(".js-c-rate").text(lbl("VAS_189_PriceEntered", "Price"));
            $sec.find(".js-c-tax").text(lbl("VAS_189_Tax", "Tax"));
            $sec.find(".js-c-amt").text(lbl("VAS_189_Amount", "Amount"));
            if (!anyTax) $sec.addClass("no-tax");

            renderLineRows($sec, lines);
            return $sec;
        }

        function renderLineRows($sec, lines) {
            var $rows = $sec.find(".js-rows").empty();
            var total = lines.length;
            var paged = total > LINES_PER_PAGE;
            var start = paged ? linePage * LINES_PER_PAGE : 0;
            var end = paged ? Math.min(start + LINES_PER_PAGE, total) : total;

            for (var i = start; i < end; i++) {
                var ln = lines[i];
                var name = ln.ProductName || ln.ChargeName || ln.Description || "";
                var $r = $('<div class="' + CLS + 'lirow"></div>');
                var $desc = $('<div class="' + CLS + 'desc"></div>');
                $desc.append($('<div class="' + CLS + 'd"></div>').text(name).attr("title", name));
                // Attribute-set-instance detail (lot / serial / attributes) below the
                // item name. Skipped when absent or only a placeholder.
                var asiText = (ln.ASIDescription || "").trim();
                if (asiText && asiText.replace(/[-\s—–_]/g, "") !== "") {
                    $desc.append($('<div class="' + CLS + 'ds asi"></div>').text(asiText).attr("title", asiText));
                }
                if (ln.Description && (ln.ProductName || ln.ChargeName)) {
                    $desc.append($('<div class="' + CLS + 'ds"></div>').text(ln.Description).attr("title", ln.Description));
                }
                $r.append($desc);
                $r.append($('<div class="' + CLS + 'qty"></div>').text(fmtNumber(ln.QtyEntered, 0)));
                $r.append($('<div class="' + CLS + 'uom"></div>').text(ln.UOMSymbol || ""));
                $r.append($('<div class="' + CLS + 'rate"></div>').text(fmtAmount(ln.PriceEntered)));
                $r.append($('<div class="' + CLS + 'tax"></div>').text(+ln.TaxRate > 0 ? fmtPct(ln.TaxRate) : ""));
                $r.append($('<div class="' + CLS + 'amt"></div>').text(fmtAmount(ln.LineNetAmt)));
                $rows.append($r);
            }

            var $foot = $sec.find(".js-foot");
            if (paged) {
                footPager($foot, total, linePage, LINES_PER_PAGE, end - start, function (p) {
                    linePage = p;
                    renderLineRows($sec, lines);
                });
            } else {
                $foot.hide();
            }
        }

        /* Totals */
        function buildTotals() {
            var hasTax = +data.TaxAmt > 0;
            var hasWh = +data.WithholdingAmount > 0;
            var $wrap = $('<div class="' + CLS + 'totals-wrap"><div class="' + CLS + 'totals"></div></div>');
            var $t = $wrap.find("." + CLS + "totals");

            $t.append(totalRow(lbl("VAS_189_Subtotal", "Subtotal"), fmtAmount(data.TotalLines), ""));
            if (hasTax) {
                $t.append(totalRow(lbl("VAS_189_Tax", "Tax"), fmtAmount(data.TaxAmt), ""));
            }
            $t.append(totalRow(lbl("VAS_189_GrossInvoiceTotal", "Gross invoice total"), fmtAmount(data.GrandTotal), ""));
            if (hasWh) {
                $t.append(totalRow(lbl("VAS_189_LessWithholding", "Less withholding"), "(" + fmtAmount(data.WithholdingAmount) + ")", ""));
            }
            $t.append(totalRow(lbl("VAS_189_NetReceivable", "Net receivable"), fmtAmount(data.NetReceivable), "grand"));
            return $wrap;
        }

        function totalRow(k, v, cls) {
            return $('<div class="' + CLS + 'trow ' + (cls || "") + '"></div>')
                .append($('<span class="' + CLS + 'trow-k"></span>').text(k))
                .append($('<span class="' + CLS + 'trow-v"></span>').text(v));
        }

        /* Withholding - only when withholding applies */
        function buildWithholding() {
            if (!(+data.WithholdingAmount > 0) || !data.Withholding) return null;
            var w = data.Withholding;
            var $sec = $(
                '<section class="' + CLS + 'withholding">' +
                '<div class="' + CLS + 'wh-head">' +
                '<span class="' + CLS + 'wh-h"></span>' +
                '<span class="' + CLS + 'wh-r"></span>' +
                '</div>' +
                '<div class="' + CLS + 'wh-grid">' +
                '<div class="cell"><div class="k js-k1"></div><div class="v js-v1"></div></div>' +
                '<div class="cell"><div class="k js-k2"></div><div class="v js-v2"></div></div>' +
                '<div class="cell"><div class="k js-k3"></div><div class="v js-v3"></div></div>' +
                '<div class="cell"><div class="k js-k4"></div><div class="v warn js-v4"></div></div>' +
                '</div>' +
                '</section>'
            );
            $sec.find("." + CLS + "wh-h").text(lbl("VAS_189_WithholdingDetails", "Withholding Details"));
            $sec.find("." + CLS + "wh-r").text(lbl("VAS_189_AppliedBeforeCustomerPayment", "Applied before customer payment"));
            $sec.find(".js-k1").text(lbl("VAS_189_WithholdingType", "Withholding Type"));
            $sec.find(".js-v1").text(w.TypeName || lbl("VAS_189_Withholding", "Withholding"));
            $sec.find(".js-k2").text(lbl("VAS_189_BaseAmount", "Base Amount"));
            $sec.find(".js-v2").text(fmtAmount(w.Base));
            $sec.find(".js-k3").text(lbl("VAS_189_Rate", "Rate"));
            $sec.find(".js-v3").text(fmtPct(w.Rate));
            $sec.find(".js-k4").text(lbl("VAS_189_WithheldAmount", "Withholding Amount"));
            $sec.find(".js-v4").text(fmtAmount(w.Amount));
            return $sec;
        }

        /* ---------- payment schedules ---------- */
        function buildPaymentSchedule() {
            // Server-side paged: data.PaymentSchedule is the FIRST page; the totals
            // come from the server aggregate over ALL schedules.
            var firstPage = data.PaymentSchedule || [];
            var total = data.ScheduleTotal || firstPage.length;
            if (!total) return null;

            var $sec = $(
                '<section class="' + CLS + 'block">' +
                '<div class="' + CLS + 'block-head">' +
                '<span class="' + CLS + 'block-h"></span>' +
                '<span class="' + CLS + 'block-r js-sum"></span>' +
                '</div>' +
                '<div class="' + CLS + 't4-head">' +
                '<span class="js-h1"></span><span class="js-h2"></span><span class="js-h3"></span><span class="right js-h4"></span>' +
                '</div>' +
                '<div class="js-rows"></div>' +
                '<div class="' + CLS + 'block-foot js-sched-pager" style="display:none;"></div>' +
                '<div class="' + CLS + 'block-foot js-foot"></div>' +
                '</section>'
            );
            $sec.find("." + CLS + "block-h").text(lbl("VAS_189_PaymentSchedules", "Payment schedules"));
            $sec.find(".js-sum").text((data.PaymentTermName ? data.PaymentTermName + " · " : "") +
                lbl("VAS_189_InstalmentCount", "{0} instalment(s)").replace("{0}", total));
            $sec.find(".js-h1").text(lbl("VAS_189_Schedule", "Schedule"));
            $sec.find(".js-h2").text(lbl("DueDate"));
            $sec.find(".js-h3").text(lbl("VAS_189_Status", "Status"));
            $sec.find(".js-h4").text(lbl("VAS_189_Amount", "Amount"));

            var $rows = $sec.find(".js-rows");
            var $pager = $sec.find(".js-sched-pager");
            var $foot = $sec.find(".js-foot");
            var SCHED_PER_PAGE = 5;
            var schedPage = 0;
            var curRows = firstPage;
            var pct = Math.round(100 / (total || 1));

            function renderSchedRows() {
                $rows.empty();
                var start = schedPage * SCHED_PER_PAGE;
                curRows.forEach(function (s, idx) {
                    var absIdx = start + idx;
                    var paid = s.Status === "Paid";
                    var hold = s.Status === "OnHold";
                    var $r = $('<div class="' + CLS + 't4-row"></div>');
                    // The first column truncates on narrow panels - carry the full
                    // text as its tooltip.
                    var schedLabel = lbl("VAS_189_ScheduleLine", "Schedule {0} · {1}%")
                        .replace("{0}", (absIdx + 1)).replace("{1}", pct);
                    $r.append($('<div class="col-a p"></div>').text(schedLabel).attr("title", schedLabel));
                    $r.append($('<div class="col-b c"></div>').text(fmtDate(s.DueDate)));
                    var stText = paid ? lbl("VAS_189_Paid", "Paid")
                        : (hold ? lbl("VAS_189_OnHold", "On hold") : lbl("Open"));
                    $r.append($('<div class="col-c"></div>').append(chip(stText, paid ? "ok" : "warn")));
                    $r.append($('<div class="col-d amt"></div>').text(fmtAmount(s.DueAmt)));
                    $rows.append($r);
                });

                if (total > SCHED_PER_PAGE) {
                    footPager($pager, total, schedPage, SCHED_PER_PAGE, curRows.length, gotoSchedPage);
                } else {
                    $pager.hide();
                }
            }

            // Fetch a schedule page from the server, then re-render.
            function gotoSchedPage(p) {
                showBusy(true);
                $.ajax({
                    url: VIS.Application.contextUrl + "VAS_189_ARInvoiceDetailPanel/GetSchedulePage",
                    type: "GET", dataType: "json",
                    data: { C_Invoice_ID: $self.record_ID, page: p, pageSize: SCHED_PER_PAGE },
                    success: function (raw) {
                        showBusy(false);
                        curRows = (typeof raw === "string") ? jQuery.parseJSON(raw) : raw;
                        if (!curRows) curRows = [];
                        schedPage = p;
                        renderSchedRows();
                    },
                    error: function (err) { showBusy(false); console.log(err); }
                });
            }
            renderSchedRows();

            // Footer mirrors the design: "n of m schedules paid" on the left,
            // settled / open amounts on the right.
            $foot.append($('<span></span>').text(
                lbl("VAS_189_SchedulesPaid", "{0} of {1} schedules paid")
                    .replace("{0}", +data.SchedulePaidCount || 0).replace("{1}", total) +
                (data.ScheduleAnyHold ? " · " + lbl("VAS_189_OnHold", "On hold") : "")));
            $foot.append($('<span></span>').html(
                escapeHtml(lbl("VAS_189_SettledOpen", "Settled {0} · Open {1}")
                    .replace("{0}", fmtAmount(data.ScheduleSettledAmount))
                    .replace("{1}", fmtAmount(data.ScheduleOpenAmount)))));
            return $sec;
        }

        /* ---------- allocations ---------- */
        function buildAllocations() {
            var firstPage = data.Allocations || [];
            var total = data.AllocationTotal || firstPage.length;
            if (!total) return null;

            var $sec = $(
                '<section class="' + CLS + 'block">' +
                '<div class="' + CLS + 'block-head">' +
                '<span class="' + CLS + 'block-h"></span>' +
                '<span class="' + CLS + 'block-r js-sum"></span>' +
                '</div>' +
                '<div class="' + CLS + 't4-head ' + CLS + 'alloc-head">' +
                '<span class="js-h1"></span><span class="js-h5"></span><span class="js-h2"></span><span class="js-h3"></span><span class="right js-h4"></span>' +
                '</div>' +
                '<div class="js-rows"></div>' +
                '<div class="' + CLS + 'block-foot js-alloc-pager" style="display:none;"></div>' +
                '<div class="' + CLS + 'block-foot js-foot"></div>' +
                '</section>'
            );
            $sec.find("." + CLS + "block-h").text(lbl("VAS_189_Allocations", "Allocations"));
            $sec.find(".js-sum").text(lbl("VAS_189_AllocationCount", "{0} allocation(s)").replace("{0}", total));
            $sec.find(".js-h1").text(lbl("VAS_189_Reference", "Reference"));
            $sec.find(".js-h5").text(lbl("VAS_189_AllocationNo", "Allocation No."));
            $sec.find(".js-h2").text(lbl("VAS_189_Date", "Date"));
            $sec.find(".js-h3").text(lbl("VAS_189_AppliedTo", "Applied to"));
            $sec.find(".js-h4").text(lbl("VAS_189_Amount", "Amount"));

            var $rows = $sec.find(".js-rows");
            var $pager = $sec.find(".js-alloc-pager");
            var $foot = $sec.find(".js-foot");
            var ALLOC_PER_PAGE = 5;
            var allocPage = 0;
            var curRows = firstPage;

            /* A document number that opens its own record. Falls back to plain text
               when there is no id to zoom to, so a click never dead-ends on a link
               that cannot resolve a record. */
            function docNode(text, recordId, zoom) {
                var label = text || "";
                if (!label || !recordId) {
                    return $('<span></span>').text(label).attr("title", label);
                }
                return $('<a href="javascript:void(0)" class="' + CLS + 'doclink"></a>')
                    .text(label)
                    .attr("title", label)
                    .on("click", function () { zoom(recordId); });
            }

            function renderAllocRows() {
                $rows.empty();
                curRows.forEach(function (a) {
                    var $r = $('<div class="' + CLS + 't4-row ' + CLS + 'alloc-row"></div>');

                    // Reference cell: settling document on top, its nature underneath
                    // (document type · payment method · reconciliation state). Each
                    // source opens its own window; an ALLOCATION row has no settling
                    // document, so its reference is the allocation itself.
                    var $ref = $('<div class="col-a"></div>');
                    var $refTop = $('<div class="p"></div>');
                    if (a.SourceType === "PAYMENT") {
                        $refTop.append(docNode(a.DocumentNo, a.C_Payment_ID, zoomToReceipt));
                    } else if (a.SourceType === "CASH") {
                        $refTop.append(docNode(a.DocumentNo, a.C_Cash_ID, zoomToCashJournal));
                    } else if (a.SourceType === "GLJOURNAL") {
                        $refTop.append(docNode(a.DocumentNo, a.GL_Journal_ID, zoomToGLJournal));
                    } else if (a.SourceType === "ALLOCATION") {
                        $refTop.append(docNode(a.DocumentNo, a.C_AllocationHdr_ID, zoomToAllocation));
                    } else {
                        $refTop.text(a.DocumentNo || "").attr("title", a.DocumentNo || "");
                    }
                    $ref.append($refTop);
                    var bits = [];
                    if (a.DocTypeName) bits.push(a.DocTypeName);
                    else if (a.SourceType === "CREDITNOTE") bits.push(lbl("VAS_189_CreditNote", "Credit note"));
                    else if (a.SourceType === "CASH") bits.push(lbl("VAS_189_CashJournal", "Cash journal"));
                    else if (a.SourceType === "GLJOURNAL") bits.push(lbl("VAS_189_GLJournal", "GL journal"));

                    // Payment method comes from VA009_PaymentMethod, never from the
                    // tender code. A cash-journal line carries no payment method of
                    // its own - settling from the journal IS paying in cash.
                    if (a.SourceType === "CASH") bits.push(lbl("VAS_189_Cash", "cash"));
                    else if (a.PaymentMethodName) bits.push(a.PaymentMethodName);

                    // Reconciliation: C_Payment.IsReconciled for a receipt,
                    // C_CashLine.VA012_IsReconciled for a cash-journal line.
                    if (a.SourceType === "PAYMENT" || a.SourceType === "CASH") {
                        bits.push(a.IsReconciled ? lbl("VAS_189_Reconciled", "reconciled")
                            : lbl("VAS_189_Unreconciled", "unreconciled"));
                    }
                    if (bits.length) {
                        $ref.append($('<div class="s"></div>').text(bits.join(" · ")).attr("title", bits.join(" · ")));
                    }
                    $r.append($ref);

                    // Allocation No.: its own column, so the allocation document is
                    // reachable on every row regardless of what settled the invoice.
                    $r.append($('<div class="col-e c"></div>')
                        .append(docNode(a.AllocationDocumentNo, a.C_AllocationHdr_ID, zoomToAllocation)));

                    $r.append($('<div class="col-b c"></div>').text(fmtDate(a.Date)));
                    // Applied to: the schedule the line settled, when it references one.
                    var applied = a.ScheduleNo > 0
                        ? lbl("VAS_189_Schedule", "Schedule") + " " + a.ScheduleNo
                        : "";
                    $r.append($('<div class="col-c c"></div>').text(applied).attr("title", applied));
                    // Allocation amounts are stored in the allocation header currency.
                    $r.append($('<div class="col-d amt"></div>')
                        .text(fmtAmountCur(a.Amount, a.CurSymbol || a.CurISO, a.StdPrecision)));
                    $rows.append($r);
                });

                if (total > ALLOC_PER_PAGE) {
                    footPager($pager, total, allocPage, ALLOC_PER_PAGE, curRows.length, gotoAllocPage);
                } else {
                    $pager.hide();
                }
            }

            function gotoAllocPage(p) {
                showBusy(true);
                $.ajax({
                    url: VIS.Application.contextUrl + "VAS_189_ARInvoiceDetailPanel/GetAllocationsPage",
                    type: "GET", dataType: "json",
                    data: { C_Invoice_ID: $self.record_ID, page: p, pageSize: ALLOC_PER_PAGE },
                    success: function (raw) {
                        showBusy(false);
                        curRows = (typeof raw === "string") ? jQuery.parseJSON(raw) : raw;
                        if (!curRows) curRows = [];
                        allocPage = p;
                        renderAllocRows();
                    },
                    error: function (err) { showBusy(false); console.log(err); }
                });
            }
            renderAllocRows();

            $foot.append($('<span></span>').text(lbl("VAS_189_ReceiptAllocation", "Receipt allocation")));
            $foot.append($('<span></span>').text(
                lbl("VAS_189_AllocationFooter", "Allocated {0} · Discount {1} · Write-off {2}")
                    .replace("{0}", fmtAmount(data.AllocationAmount))
                    .replace("{1}", fmtAmount(data.AllocationDiscount))
                    .replace("{2}", fmtAmount(data.AllocationWriteOff))));
            return $sec;
        }

        /* ---------- delivery detail + approval (two columns) ---------- */
        function buildDeliveryAndApproval() {
            var $dl = buildDeliveryDetails();
            var $ap = buildApproval();
            if (!$dl && !$ap) return null;

            var $two = $('<div class="' + CLS + 'two-col"></div>');
            if ($dl) $two.append($dl);
            if ($ap) $two.append($ap);
            // Only one block present -> span the full width.
            if (!($dl && $ap)) $two.addClass("single");
            return $two;
        }

        function buildDeliveryDetails() {
            var dl = data.Delivery;
            var lines = data.Lines || [];
            if (!lines.length) return null;

            var hasShipment = !!(dl && dl.Rows && dl.Rows.length);
            // No shipment linked to any line -> there is no delivery to report, so the
            // section is hidden rather than rendered with only a "Delivery type" row.
            if (!hasShipment) return null;

            var physical = lines.filter(function (l) { return l.IsPhysicalItem; }).length;
            var servicesOnly = physical === 0;

            // Status chip: fulfilled when every matched line was delivered in full,
            // partially fulfilled when a quantity variance remains.
            var chipText, chipTone;
            if (dl.IsFullyDelivered) {
                chipText = lbl("VAS_189_Fulfilled", "Fulfilled");
                chipTone = "ok";
            } else {
                chipText = lbl("VAS_189_PartiallyFulfilled", "Partially fulfilled");
                chipTone = "warn";
            }

            var $sec = $(
                '<section class="' + CLS + 'block">' +
                '<div class="' + CLS + 'block-head">' +
                '<span class="' + CLS + 'block-h"></span>' +
                '<span class="' + CLS + 'spill ' + (chipTone === "ok" ? "ok" : "warn") + '">' +
                '<span class="' + CLS + 'dot"></span><span class="js-st"></span></span>' +
                '</div>' +
                '<div class="' + CLS + 'kv js-kv"></div>' +
                '</section>'
            );
            $sec.find("." + CLS + "block-h").text(lbl("VAS_189_DeliveryDetails", "Delivery details"));
            $sec.find(".js-st").text(chipText);

            var $kv = $sec.find(".js-kv");
            // Delivery type states what the lines are, with the physical-item verdict
            // as the qualifier underneath.
            kvRow($kv, lbl("VAS_189_DeliveryType", "Delivery type"),
                servicesOnly ? lbl("VAS_189_Services", "Services") : lbl("VAS_189_Goods", "Goods"),
                "",
                servicesOnly
                    ? lbl("VAS_189_NoPhysicalItems", "No physical items detected")
                    : lbl("VAS_189_SomePhysicalItems", "{0} of {1} lines are physical items")
                        .replace("{0}", physical).replace("{1}", lines.length));

            var deliveredOn = (dl.DeliveredDates && dl.DeliveredDates.length)
                ? dl.DeliveredDates.map(function (d) { return fmtDate(d); }).join(", ")
                : fmtDate(dl.DeliveredDate);
            kvRow($kv, lbl("VAS_189_Shipment", "Shipment"), dl.ShipmentDocumentNo);
            kvRow($kv, lbl("VAS_189_SalesOrder", "Sales Order"), dl.OrderDocumentNo);
            kvRow($kv, lbl("VAS_189_DeliveredOn", "Delivered on"), deliveredOn);
            var qtyVar = +dl.QtyVariance || 0;
            kvRow($kv, lbl("VAS_189_DeliveredQty", "Delivered quantity"),
                fmtNumber(dl.TotalDelivered, 0),
                qtyVar !== 0 ? "warn" : "",
                qtyVar !== 0
                    ? lbl("VAS_189_VarianceOf", "Variance {0}").replace("{0}", fmtNumber(qtyVar, 0))
                    : "");
            kvRow($kv, lbl("VAS_189_Warehouse", "Warehouse"), dl.WarehouseName);
            kvRow($kv, lbl("VAS_189_AcknowledgedBy", "Acknowledged by"), dl.AcknowledgedBy);
            return $sec;
        }

        /* Approval rail - the document's own status milestones. */
        function buildApproval() {
            var steps = [];
            steps.push({
                st: lbl("VAS_189_Drafted", "Drafted"),
                mt: subLine(data.CreatedByName, data.Created), done: true
            });
            if (data.IsApproved) {
                steps.push({
                    st: lbl("VAS_189_Approved", "Approved"),
                    mt: subLine(data.ApprovedByName, data.Updated), done: true
                });
            }
            // C_Invoice.VAS_IsEmailSent. The schema keeps no sent-date column, so the
            // sub-line falls back to the last-updated stamp - the same substitute the
            // sibling invoice widgets use for a completion date.
            if (data.IsEmailSent) {
                steps.push({
                    st: lbl("VAS_189_SentToCustomer", "Sent to customer"),
                    mt: fmtDate(data.Updated), done: true
                });
            }
            var dl = data.Delivery;
            if (dl && dl.Rows && dl.Rows.length) {
                steps.push({
                    st: lbl("VAS_189_Delivered", "Delivered"),
                    mt: fmtDate(dl.DeliveredDate),
                    nt: dl.ShipmentDocumentNo, done: true
                });
            }
            if (data.Posted === "Y") {
                steps.push({ st: lbl("VAS_189_PostedToLedger", "Posted to ledger"), mt: fmtDate(data.DateAcct), done: true });
            }
            if (steps.length < 2) return null;

            var $sec = $(
                '<section class="' + CLS + 'block">' +
                '<div class="' + CLS + 'block-head">' +
                '<span class="' + CLS + 'block-h"></span>' +
                '<span class="' + CLS + 'spill ok"><span class="' + CLS + 'dot"></span><span class="js-st"></span></span>' +
                '</div>' +
                '<div class="' + CLS + 'vsteps js-steps"></div>' +
                '</section>'
            );
            $sec.find("." + CLS + "block-h").text(lbl("VAS_189_Approval", "Approval"));
            // The document's own status - a reversed document reads "Reversed" here
            // even when DocStatusName lags behind.
            $sec.find(".js-st").text(isReversed()
                ? lbl("VAS_189_Reversed", "Reversed")
                : (data.DocStatusName || docStatusLabel(data.DocStatus)));
            if (isReversed() || data.DocStatus === "VO") {
                $sec.find("." + CLS + "spill").removeClass("ok").addClass("warn");
            }

            var $steps = $sec.find(".js-steps");
            steps.forEach(function (s) {
                var $v = $('<div class="' + CLS + 'vstep done"></div>');
                $v.append('<div class="rail"><div class="node"><i class="fa fa-check" aria-hidden="true"></i></div><div class="line"></div></div>');
                var $c = $('<div class="c"></div>');
                $c.append($('<div class="st"></div>').text(s.st));
                if (s.mt) $c.append($('<div class="mt"></div>').text(s.mt));
                if (s.nt) $c.append($('<div class="nt"></div>').text(s.nt));
                $v.append($c);
                $steps.append($v);
            });
            return $sec;
        }

        /* ---------- posted journal ---------- */
        function buildPostedJournal() {
            var pj = data.PostedJournal;
            if (!pj || !pj.Rows || !pj.Rows.length) return null;

            // Posted amounts are in the base / accounting currency (primary acct
            // schema), so that currency's symbol is used - not the transaction one.
            var acctCur = pj.CurSymbol || "";
            var acctPrec = (pj.StdPrecision >= 0) ? pj.StdPrecision : 2;

            // Account / Debit / Credit always show; each dimension column is dropped
            // when NO row carries a value for it.
            var cols = [
                {
                    key: "OrgName", label: lbl("AD_Org_ID", "Organization"), w: "1.4fr", num: false,
                    get: function (jr) { return jr.OrgName || ""; }
                },
                {
                    key: "Account", label: lbl("VAS_189_Account", "Account"), w: "2fr", num: false, always: true,
                    get: function (jr) { return (jr.AccountValue ? jr.AccountValue + " · " : "") + (jr.AccountName || ""); }
                },
                {
                    key: "Dr", label: lbl("VAS_189_Debit", "Debit"), w: "1fr", num: true, always: true,
                    get: function (jr) { return jr.AmtAcctDr; }
                },
                {
                    key: "Cr", label: lbl("VAS_189_Credit", "Credit"), w: "1fr", num: true, always: true,
                    get: function (jr) { return jr.AmtAcctCr; }
                },
                {
                    key: "BPName", label: lbl("VAS_189_BPartner_ID", "Customer"), w: "1.4fr", num: false,
                    get: function (jr) { return jr.BPName || ""; }
                },
                {
                    key: "ProductName", label: lbl("M_Product_ID", "Product"), w: "1.4fr", num: false,
                    get: function (jr) { return jr.ProductName || ""; }
                },
                {
                    key: "OrgTrxName", label: lbl("AD_OrgTrx_ID", "Trx Organization"), w: "1.4fr", num: false,
                    get: function (jr) { return jr.OrgTrxName || ""; }
                }
            ].filter(function (c) {
                if (c.always) return true;
                return pj.Rows.some(function (jr) { var v = jr[c.key]; return v != null && String(v).length > 0; });
            });
            var tmpl = cols.map(function (c) { return c.w; }).join(" ");

            var $sec = $(
                '<section class="' + CLS + 'block ' + CLS + 'journal">' +
                '<div class="' + CLS + 'block-head">' +
                '<span class="' + CLS + 'block-h"></span>' +
                '<span class="' + CLS + 'spill ok"><span class="' + CLS + 'dot"></span><span class="js-st"></span></span>' +
                '</div>' +
                '<div class="' + CLS + 'je-head"></div>' +
                '<div class="js-rows"></div>' +
                '<div class="' + CLS + 'je-total"></div>' +
                '<div class="' + CLS + 'block-foot js-je-pager" style="display:none;"></div>' +
                '<div class="' + CLS + 'block-foot js-foot"></div>' +
                '</section>'
            );
            $sec.find("." + CLS + "block-h").text(lbl("VAS_189_PostedJournalEntry", "Posted Entry"));
            $sec.find(".js-st").text(lbl("VAS_189_Posted", "Posted"));

            var $head = $sec.find("." + CLS + "je-head").css("grid-template-columns", tmpl);
            cols.forEach(function (c) {
                $head.append($('<span></span>').addClass(c.num ? "right" : "").text(c.label).attr("title", c.label));
            });

            var $rows = $sec.find(".js-rows");
            var $jePager = $sec.find(".js-je-pager");
            var $foot = $sec.find(".js-foot");
            var JE_PER_PAGE = 10;
            var jePage = 0;
            // Server-side paged: pj.Rows is the FIRST page; pj.Total is the row count
            // and pj.TotalDr/TotalCr are aggregates over all fact lines. Columns are
            // derived from the first page and kept stable across pages.
            var curRows = pj.Rows || [];
            var jeTotal = pj.Total || curRows.length;

            function renderJeRows() {
                $rows.empty();
                curRows.forEach(function (jr) {
                    var $r = $('<div class="' + CLS + 'je-row"></div>').css("grid-template-columns", tmpl);
                    cols.forEach(function (c) {
                        if (c.num) {
                            $r.append(jeAmt(c.get(jr), c.key === "Dr" ? "dr" : "cr", acctCur, acctPrec));
                        } else if (c.key === "Account") {
                            var v = c.get(jr);
                            $r.append($('<div class="acct"></div>').append($('<div class="code"></div>').text(v).attr("title", v)));
                        } else {
                            $r.append($('<div class="dimc"></div>').text(c.get(jr)).attr("title", c.get(jr)));
                        }
                    });
                    $rows.append($r);
                });

                if (jeTotal > JE_PER_PAGE) {
                    footPager($jePager, jeTotal, jePage, JE_PER_PAGE, curRows.length, gotoJePage);
                } else {
                    $jePager.hide();
                }
            }

            function gotoJePage(p) {
                showBusy(true);
                $.ajax({
                    url: VIS.Application.contextUrl + "VAS_189_ARInvoiceDetailPanel/GetPostedJournalPage",
                    type: "GET", dataType: "json",
                    data: { C_Invoice_ID: $self.record_ID, page: p, pageSize: JE_PER_PAGE },
                    success: function (raw) {
                        showBusy(false);
                        var resp = (typeof raw === "string") ? jQuery.parseJSON(raw) : raw;
                        curRows = (resp && resp.Rows) ? resp.Rows : [];
                        jePage = p;
                        renderJeRows();
                    },
                    error: function (err) { showBusy(false); console.log(err); }
                });
            }
            renderJeRows();

            var $jeTot = $sec.find("." + CLS + "je-total").css("grid-template-columns", tmpl);
            cols.forEach(function (c, idx) {
                if (c.key === "Dr") $jeTot.append($('<div class="num dr"></div>').text(fmtAmountCur(pj.TotalDr, acctCur, acctPrec)));
                else if (c.key === "Cr") $jeTot.append($('<div class="num cr"></div>').text(fmtAmountCur(pj.TotalCr, acctCur, acctPrec)));
                else if (idx === 0) $jeTot.append($('<div class="tlab"></div>').text(lbl("VAS_189_Total", "Total")));
                else $jeTot.append($('<div></div>'));
            });

            var footBits = [];
            if (pj.PeriodName) footBits.push(lbl("VAS_189_Period", "Period {0}").replace("{0}", pj.PeriodName));
            if (pj.PostingDate) footBits.push(lbl("VAS_189_PostingDate", "Posting date {0}").replace("{0}", fmtDate(pj.PostingDate)));
            var balanced = Math.abs((+pj.TotalDr || 0) - (+pj.TotalCr || 0)) < 0.005;
            $foot.append($('<span></span>').text(footBits.join(" · ")))
                .append($('<span></span>').text(balanced ? lbl("VAS_189_Balanced", "Balanced - Dr = Cr") : ""));
            return $sec;
        }

        function jeAmt(value, cls, cur, prec) {
            var v = +value || 0;
            var $d = $('<div class="num ' + cls + '"></div>');
            if (v === 0) { $d.addClass("zero").text("—"); }
            else { $d.text((v < 0 ? "-" : "") + fmtAmountCur(v, cur, prec)); }
            return $d.attr("title", $d.text());
        }

        /* ---------- misc ---------- */
        function scrollToSection(cssClass) {
            var el = $body.find("." + cssClass)[0];
            if (!el) return;
            el.scrollIntoView({ behavior: "smooth", block: "start" });
            var $el = $(el);
            $el.removeClass("flash"); void el.offsetWidth; $el.addClass("flash");
        }

        /* AD_Process_ID / AD_Table_ID / AD_Window_ID for the print + share flows,
           read off the current grid tab exactly as Download PDF does. */
        function printContext() {
            var tab = $self.curTab;
            return {
                AD_Process_ID: (tab && typeof tab.getAD_Process_ID === "function") ? tab.getAD_Process_ID() : 0,
                AD_Table_ID: (tab && typeof tab.getAD_Table_ID === "function") ? tab.getAD_Table_ID() : ($self.table_ID || 0),
                AD_Window_ID: (tab && typeof tab.getAD_Window_ID === "function") ? tab.getAD_Window_ID() : ($self.AD_Window_ID || 0),
                RecordID: $self.record_ID,
                ToName: (data && data.ContactName) ? data.ContactName : "",
                ToEmail: (data && data.ContactEMail) ? data.ContactEMail : ""
            };
        }

        // Generate the invoice PDF via the framework print process and download it.
        function downloadInvoicePDF() {
            if (!$self.record_ID || !$self.curTab) return;

            var ctxRes = printContext();
            if (!ctxRes.AD_Process_ID || !ctxRes.AD_Table_ID) {
                error(lbl("VAS_189_ActionFailed", "The action could not be completed."));
                return;
            }
            showBusy(true);
            $.ajax({
                url: VIS.Application.contextUrl + "JsonData/GeneratePrint/",
                dataType: "json",
                data: {
                    AD_Process_ID: ctxRes.AD_Process_ID,
                    Name: "Print",
                    AD_Table_ID: ctxRes.AD_Table_ID,
                    Record_ID: ctxRes.RecordID,
                    WindowNo: $self.windowNo,
                    filetype: "P",                 // P = PDF
                    actionOrigin: "W",
                    originName: "AR Invoice"
                },
                success: function (raw) {
                    var res = (typeof raw === "string") ? jQuery.parseJSON(raw) : raw;
                    if (!res) { showBusy(false); return; }

                    // GeneratePrint writes to TempDownload and returns the file name.
                    var file = res.ReportFilePath || res.FilePath || res.FileName || res.fileName || res.path;
                    if (!file) {
                        var msg = (res.ErrorText) ||
                            (res.ReportProcessInfo && res.ReportProcessInfo.Summary) ||
                            lbl("VAS_189_ActionFailed", "The action could not be completed.");
                        error(msg);
                        console.log("GeneratePrint response:", res);
                        showBusy(false);
                        return;
                    }

                    showBusy(false);
                    window.open(VIS.Application.contextUrl + file, "_blank");
                },
                error: function (err) { console.log(err); showBusy(false); }
            });
        }

        // Send the invoice through the shared VA112 share/email panel. The recipient
        // is seeded from the invoice contact; when it is blank VAS_SentEmailDoc
        // resolves it on the server from AD_Table_ID + RecordID.
        function sendInvoiceEmail() {
            if (!$self.record_ID || !$self.curTab) return;
            if (!VAS.VAS_SentEmailDoc || typeof VAS.VAS_SentEmailDoc.sendEmail !== "function") {
                error(lbl("VAS_189_ActionFailed", "The action could not be completed."));
                return;
            }

            var ctxRes = printContext();
            if (!ctxRes.AD_Process_ID || !ctxRes.AD_Table_ID || !ctxRes.AD_Window_ID) {
                error(lbl("VAS_189_ActionFailed", "The action could not be completed."));
                return;
            }

            var pv = new VAS.VAS_SentEmailDoc.sendEmail({
                windowNo: $self.windowNo,
                AD_Process_ID: ctxRes.AD_Process_ID,
                AD_Table_ID: ctxRes.AD_Table_ID,
                RecordID: ctxRes.RecordID,
                AD_Window_ID: ctxRes.AD_Window_ID,
                Name: ctxRes.ToName,
                EMailID: ctxRes.ToEmail
            });
            return pv;
        }

        /* Platform Refresh button: full rebuild from the server (instance method -
           the prototype method below forwards to the same path). */
        this.refreshWidget = function () {
            if ($self.record_ID > 0) { $self.fetchData($self.record_ID); }
        };

        /* Tear down anything mounted outside the panel root (both modals are
           appended to <body>) plus the document-level key handlers. */
        this.disposeComponent = function () {
            $(document).off("keydown.vas189Modal");
            $(document).off("keydown.vas189Recur");
            if ($scrim) { $scrim.remove(); $scrim = null; }
            if ($recScrim) { $recScrim.remove(); $recScrim = null; }
            if ($root) { $root.remove(); }
            data = null;
            meta = null;
        };

        /* ===================== Receipt / allocation modal ===================== */

        function openPaymentModal() {
            if (!data || !data.C_Invoice_ID) return;
            panelDirty = false;   // fresh modal session
            showBusy(true);
            $.ajax({
                url: VIS.Application.contextUrl + "VAS_189_ARInvoiceDetailPanel/GetPaymentModalMeta",
                type: "GET",
                dataType: "json",
                data: { C_Invoice_ID: $self.record_ID, IsSOTrx: !!$self.IsSOTrx },
                success: function (raw) {
                    showBusy(false);
                    meta = (typeof raw === "string") ? jQuery.parseJSON(raw) : raw;
                    if (!meta || !meta.C_BPartner_ID) {
                        info(lbl("VAS_189_LoadFailed", "Could not load the requested information."));
                        return;
                    }
                    renderModal();
                },
                error: function (err) {
                    showBusy(false); console.log(err);
                    info(lbl("VAS_189_LoadFailed", "Could not load the requested information."));
                }
            });
        }

        // Toggle the modal-scoped busy overlay (created in renderModal).
        function showModalBusy(show) {
            if (!$scrim) return;
            $scrim.find("." + CLS + "m-busy").toggleClass("show", !!show);
        }

        // Re-fetch the modal meta and rebuild the modal so it reflects the new state
        // (reduced open balance, consumed credits) after an allocation is created and
        // completed. The busy overlay stays up until renderModal swaps in the fresh
        // modal. When allocDocNo is given, a confirmation carrying the allocation
        // document number is shown once the refreshed modal is in place.
        function refreshPaymentModal(allocDocNo) {
            if (!data || !data.C_Invoice_ID) return;
            // An allocation was just created -> the panel behind the modal is stale.
            panelDirty = true;
            showModalBusy(true);
            $.ajax({
                url: VIS.Application.contextUrl + "VAS_189_ARInvoiceDetailPanel/GetPaymentModalMeta",
                type: "GET",
                dataType: "json",
                data: { C_Invoice_ID: $self.record_ID, IsSOTrx: !!$self.IsSOTrx },
                success: function (raw) {
                    meta = (typeof raw === "string") ? jQuery.parseJSON(raw) : raw;
                    if (!meta || !meta.C_BPartner_ID) {
                        showModalBusy(false);
                        info(lbl("VAS_189_LoadFailed", "Could not load the requested information."));
                        return;
                    }
                    renderModal();   // rebuilds the scrim/modal fresh (clears the overlay)
                    if (allocDocNo) {
                        info(lbl("VAS_189_AllocationCreatedNo", "Allocation document {0} created successfully.")
                            .replace("{0}", allocDocNo));
                    }
                },
                error: function (err) {
                    showModalBusy(false); console.log(err);
                    info(lbl("VAS_189_LoadFailed", "Could not load the requested information."));
                }
            });
        }

        function renderModal() {
            removeModal();
            var isCN = meta.RecordMode === "AR_CREDIT_NOTE_ALLOCATION";
            var p = meta.StdPrecision >= 0 ? meta.StdPrecision : 2;
            var cur = meta.CurSymbol || meta.ISO_Code || "";

            $scrim = $('<div class="' + CLS + 'scrim ' + (isCN ? "cn-mode" : "invoice-mode") +
                '" role="dialog" aria-modal="true"></div>');
            var $modal = $('<div class="' + CLS + 'modal"></div>');
            $scrim.append($modal);

            var $head = $(
                '<div class="' + CLS + 'm-head">' +
                '<div><h2 class="js-title"></h2><div class="ms js-sub"></div></div>' +
                '<button type="button" class="' + CLS + 'm-x" aria-label="Close"><i class="fa fa-times"></i></button>' +
                '</div>'
            );
            $head.find(".js-title").text(isCN ? lbl("VAS_189_AllocateCreditNote", "Allocate Credit Note")
                : lbl("VAS_189_RecordReceipt", "Record Receipt"));
            $head.find(".js-sub").text((data.BPName || "") + " · " + (data.DocumentNo || ""));
            $head.find("." + CLS + "m-x").on("click", closeModal);
            $modal.append($head);

            var $form = $('<div class="js-form"></div>');
            var $bodyM = $('<div class="' + CLS + 'm-body"></div>');

            var $stats = $('<div class="' + CLS + 'm-stats"></div>');
            statCard($stats, lbl("VAS_189_CustomerOutstanding", "Customer Outstanding"), fmtAmountCur(meta.CustomerOutstanding, cur, p), "");
            statCard($stats, lbl("VAS_189_GrossInvoice", "Gross Invoice"), fmtAmountCur(meta.GrossInvoice, cur, p), "amber");
            if (+meta.Withholding > 0) {
                statCard($stats, lbl("VAS_189_Withholding", "Withholding"), fmtAmountCur(meta.Withholding, cur, p), "amber");
            }
            statCard($stats, lbl("VAS_189_NetReceivable", "Net Receivable"), fmtAmountCur(meta.NetReceivable, cur, p), "green");
            statCard($stats, lbl("VAS_189_AvailableToApply", "Available to Apply"), fmtAmountCur(meta.AvailableToApply, cur, p), "green");
            $bodyM.append($stats);

            // AR invoices and AR credit notes share the same modal body (on-account
            // credits + new receipt); only the amount sign differs, and that is
            // applied when the allocation / payment is created on the server.
            buildReceiptSections($bodyM, p, cur);

            $form.append($bodyM);
            $form.append(buildModalFooter());
            $modal.append($form);

            $modal.append(buildSuccessView());

            // Modal-scoped busy overlay. The scrim sits at body level above the
            // panel, so the panel busy indicator cannot cover it.
            $modal.append('<div class="' + CLS + 'm-busy" aria-hidden="true"><div class="vis-busyindicatorinnerwrap"><i class="vis_widgetloader"></i></div></div>');

            $("body").append($scrim);
            // Force reflow then open for the transition.
            void $scrim[0].offsetWidth;
            $scrim.addClass("open");

            $scrim.on("click", function (e) { if (e.target === $scrim[0]) closeModal(); });
            $(document).off("keydown.vas189Modal").on("keydown.vas189Modal", function (e) {
                if (e.key === "Escape") closeModal();
            });
        }

        function statCard($wrap, label, value, tone) {
            $wrap.append($('<div class="' + CLS + 'm-stat ' + (tone || "") + '"></div>')
                .append($('<div class="l"></div>').text(label))
                .append($('<div class="v"></div>').text(value)));
        }

        /* Apply credits + new receipt + settlement summary. */
        function buildReceiptSections($bodyM, p, cur) {
            // Server-side paged: meta.OnAccountPayments is the FIRST page and
            // meta.OnAccountPaymentsTotal the total count.
            var curCredits = meta.OnAccountPayments || [];
            var creditTotal = meta.OnAccountPaymentsTotal || curCredits.length;
            // Open amount still to settle. ZERO is a valid value (every schedule
            // paid) - it must NOT fall back to the net receivable, otherwise a
            // fully-settled invoice would offer its whole total as a new receipt.
            var invOpen = (meta.NetOpenAmount === null || typeof meta.NetOpenAmount === "undefined")
                ? (+meta.NetReceivable || 0) : (+meta.NetOpenAmount || 0);
            var fullySettled = !!meta.IsFullySettled || invOpen <= 0;

            var state = { applied: 0, allocationCreated: false, selected: {} };
            var CREDITS_PER_PAGE = 5;
            var creditPage = 0;
            var $creditRows = null, $creditFoot = null;

            // Selection stores the FULL row object (keyed by source) so a selection
            // on a page no longer in the DOM still survives and can be applied.
            function creditKey(r) { return r.SourceType + ":" + r.Id; }
            function isCreditSelected(r) { return !!state.selected[creditKey(r)]; }
            function selectedCreditRows() {
                return Object.keys(state.selected).map(function (k) { return state.selected[k]; });
            }

            function renderCreditRows() {
                if (!$creditRows) return;
                $creditRows.empty();
                var paged = creditTotal > CREDITS_PER_PAGE;

                for (var i = 0; i < curCredits.length; i++) {
                    (function (r) {
                        var selected = isCreditSelected(r);
                        var locked = selected && state.allocationCreated;

                        var $row = $('<div class="' + CLS + 't4-row ' + CLS + 'credit-t4row"></div>');
                        if (selected) $row.addClass("on");
                        // Nothing open to allocate against -> rows show but do not select.
                        if (locked || fullySettled) $row.addClass("locked");
                        else $row.addClass("selectable");

                        var docLabel = (r.DocTypeName ? r.DocTypeName + " - " : "") + (r.DocumentNo || "");
                        docLabel = docLabel || (r.DocumentNo || "");
                        $row.append($('<div class="col-a p"></div>').text(docLabel).attr("title", docLabel));
                        $row.append($('<div class="col-b c"></div>').text(fmtDate(r.Date)));

                        var stText, stTone;
                        if (locked) { stText = lbl("VAS_189_Allocated", "Allocated"); stTone = "ok"; }
                        else if (selected) { stText = lbl("VAS_189_SelectedState", "Selected"); stTone = "info"; }
                        else { stText = lbl("VAS_189_Available", "Available"); stTone = "warn"; }
                        $row.append($('<div class="col-c"></div>').append(chip(stText, stTone)));

                        // Amount in the source's own currency; when that differs from the
                        // invoice currency the invoice-currency equivalent rides along.
                        var rSym = r.CurSymbol || cur;
                        var rPrec = (r.StdPrecision >= 0) ? r.StdPrecision : p;
                        var $amt = $('<div class="col-d amt"></div>').text(fmtAmountCur(r.AvailableAmount, rSym, rPrec));
                        if (r.C_Currency_ID && r.C_Currency_ID !== meta.C_Currency_ID) {
                            $amt.append($('<span class="conv"></span>').text(fmtAmountCur(r.AvailableAmountInv, cur, p)));
                        }
                        $row.append($amt);

                        $row.on("click", function () {
                            if (state.allocationCreated || fullySettled) return;
                            if (!isCreditSelected(r)) {
                                // Single-currency rule: only sources sharing the currency of
                                // the already-selected rows can be added at one time. Across
                                // currencies they must also share one accounting date AND one
                                // conversion type (one conversion basis per allocation).
                                var curConflict = false, dateConflict = false, convConflict = false;
                                var crossCur = r.C_Currency_ID && r.C_Currency_ID !== meta.C_Currency_ID;
                                selectedCreditRows().forEach(function (other) {
                                    if (other.C_Currency_ID !== r.C_Currency_ID) curConflict = true;
                                    if (crossCur && acctKey(other.DateAcct) !== acctKey(r.DateAcct)) dateConflict = true;
                                    if (crossCur && (other.C_ConversionType_ID || 0) !== (r.C_ConversionType_ID || 0)) convConflict = true;
                                });
                                if (curConflict) {
                                    info(lbl("VAS_189_SingleCurrencyOnly", "Only payments or credits of a single currency can be selected at a time."));
                                    return;
                                }
                                if (dateConflict) {
                                    info(lbl("VAS_189_SameAcctDateOnly", "For a different currency, only payments with the same accounting date can be selected together."));
                                    return;
                                }
                                if (convConflict) {
                                    info(lbl("VAS_189_SameConvTypeOnly", "For a different currency, only payments with the same conversion type can be selected together."));
                                    return;
                                }
                                state.selected[creditKey(r)] = r;
                            } else {
                                delete state.selected[creditKey(r)];
                            }
                            renderCreditRows();
                            updateCreditSummary();
                            recompute();
                        });
                        $creditRows.append($row);
                    })(curCredits[i]);
                }

                if (paged) {
                    footPager($creditFoot, creditTotal, creditPage, CREDITS_PER_PAGE, curCredits.length, gotoCreditPage);
                } else {
                    $creditFoot.hide();
                }
            }

            // Fetch a page of on-account receipts, then re-render. The selection
            // survives because it stores full row objects, not page indexes.
            function gotoCreditPage(pg) {
                if (state.allocationCreated) return;
                showModalBusy(true);
                $.ajax({
                    url: VIS.Application.contextUrl + "VAS_189_ARInvoiceDetailPanel/GetOnAccountPaymentsPage",
                    type: "GET", dataType: "json",
                    // Customer / credit-note flag / invoice currency come from meta so
                    // the server does not re-query the invoice header on every page.
                    data: {
                        C_BPartner_ID: meta.C_BPartner_ID,
                        IsCreditNote: !!meta.IsARCreditNote,
                        C_Currency_ID: meta.C_Currency_ID,
                        page: pg, pageSize: CREDITS_PER_PAGE
                    },
                    success: function (raw) {
                        showModalBusy(false);
                        curCredits = (typeof raw === "string") ? jQuery.parseJSON(raw) : raw;
                        if (!curCredits) curCredits = [];
                        creditPage = pg;
                        renderCreditRows();
                    },
                    error: function (err) { showModalBusy(false); console.log(err); }
                });
            }

            if (creditTotal) {
                var $sec = $('<div class="' + CLS + 'm-sec ' + CLS + 'm-sec-boxed"></div>');
                $sec.append($('<div class="' + CLS + 'm-sec-h"></div>')
                    .append($('<span class="t"></span>').text(lbl("VAS_189_ApplyOnAccountCredits", "Apply on-account & credits")))
                    .append($('<span class="hint"></span>').text(lbl("VAS_189_UseTheseFirst", "Use these first - no new receipt"))));

                var $cHead = $('<div class="' + CLS + 't4-head"></div>');
                $cHead.append($('<span></span>').text(lbl("VAS_189_PaymentCredit", "Payment / Credit")));
                $cHead.append($('<span></span>').text(lbl("VAS_189_Date", "Date")));
                $cHead.append($('<span></span>').text(lbl("VAS_189_Status", "Status")));
                $cHead.append($('<span class="right"></span>').text(lbl("VAS_189_Amount", "Amount")));
                $sec.append($cHead);

                $creditRows = $('<div class="js-credit-rows"></div>');
                $creditFoot = $('<div class="' + CLS + 'block-foot js-credit-foot" style="display:none;"></div>');
                $sec.append($creditRows).append($creditFoot);
                renderCreditRows();

                var $actions = $('<div class="' + CLS + 'credit-actions"></div>');
                var $summary = $('<span class="summary js-credit-summary"></span>')
                    .text(lbl("VAS_189_SelectCreditsHint", "Select available payments or credits to allocate before recording a new receipt."));
                var $applyBtn = $('<button type="button" class="' + CLS + 'btn is-primary js-apply-credits"></button>')
                    .text(lbl("VAS_189_ApplySelectedCredits", "Apply Selected Credits")).prop("disabled", true);
                $actions.append($summary).append($applyBtn);
                $sec.append($actions);
                $sec.append($('<div class="' + CLS + 'alloc-status js-alloc-status"></div>')
                    .text(lbl("VAS_189_AllocationStatusMsg", "Allocation transaction created. Remaining balance is now available for a new receipt.")));

                $applyBtn.on("click", function () { applySelectedCredits(); });
                $bodyM.append($sec);
            }

            // New receipt form. When every pay schedule is already paid the whole
            // section renders read-only - there is no balance left to receive.
            var $pay = $('<div class="' + CLS + 'm-sec js-newpay"></div>');
            $pay.append($('<div class="' + CLS + 'm-sec-h"></div>')
                .append($('<span class="t"></span>').text(lbl("VAS_189_NewReceipt", "New Receipt")))
                .append($('<span class="hint"></span>').text(fullySettled
                    ? lbl("VAS_189_NoBalanceToReceive", "No balance left to receive")
                    : lbl("VAS_189_ForBalanceAfterCredits", "For the balance after credits"))));
            if (fullySettled) {
                $pay.append($('<div class="' + CLS + 'paid-note"></div>')
                    .append('<i class="fa fa-check-circle"></i>')
                    .append($('<span></span>').text(lbl("VAS_189_AllSchedulesPaidMsg",
                        "All payment schedules of this invoice are paid. No further payment can be recorded."))));
            }

            var $grid = $('<div class="' + CLS + 'fgrid"></div>');
            var $amtField = field(lbl("VAS_189_ReceiptAmount", "Receipt Amount"),
                '<div class="control"><span class="pfx">' + escapeHtml(cur) +
                '</span><input class="js-pay-amt" type="text" inputmode="decimal" /></div>', "fa fa-calculator");
            var $dateField = field(lbl("VAS_189_ReceiptDate", "Receipt Date"),
                '<div class="control"><input class="js-pay-date" type="date" /></div>', "fa fa-calendar");

            var $methodSel = $('<select class="js-pay-method"></select>');
            // Empty placeholder so nothing is selected by default.
            $methodSel.append($('<option></option>').val("").text(lbl("VAS_189_SelectOption", "Select")));
            (meta.PaymentMethods || []).forEach(function (m) {
                $methodSel.append($('<option></option>').val(m.VA009_PaymentMethod_ID).text(m.Name));
            });
            var $methodField = field(lbl("PaymentMethod"), $('<div class="control sel"></div>').append($methodSel), "fa fa-credit-card");

            var $bankSel = $('<select class="js-pay-bank"></select>');
            $bankSel.append($('<option></option>').val("").text(lbl("VAS_189_SelectOption", "Select")));
            (meta.BankAccounts || []).forEach(function (b) {
                var t = (b.BankName || "") + (b.AccountNo ? " · ****" + b.AccountNo.slice(-4) : "") + (b.CurrencyISO ? " . " + b.CurrencyISO : "");
                $bankSel.append($('<option></option>').val(b.C_BankAccount_ID).text(t));
            });
            var $bankField = field(lbl("VAS_189_BankAccount", "Bank Account"), $('<div class="control sel"></div>').append($bankSel), "fa fa-university");

            // Currency selector (My Currency only) - defaults to the invoice currency.
            var $currencySel = $('<select class="js-pay-currency"></select>');
            (meta.Currencies || []).forEach(function (c) {
                $currencySel.append($('<option></option>').val(c.C_Currency_ID).text(c.ISO_Code || c.CurSymbol));
            });
            $currencySel.val(meta.C_Currency_ID);
            var $currencyField = field(lbl("Currency"), $('<div class="control sel"></div>').append($currencySel), "fa fa-money");

            var $convTypeSel = $('<select class="js-pay-convtype"></select>');
            (meta.ConversionTypes || []).forEach(function (t) {
                $convTypeSel.append($('<option></option>').val(t.C_ConversionType_ID).text(t.Name));
            });
            if (meta.C_ConversionType_ID) $convTypeSel.val(meta.C_ConversionType_ID);
            var $convTypeField = field(lbl("VAS_189_ConversionType", "Conversion Type"), $('<div class="control sel"></div>').append($convTypeSel), "fa fa-exchange");

            var $discField = field(lbl("VAS_189_Discount", "Discount"),
                '<div class="control"><span class="pfx">' + escapeHtml(cur) +
                '</span><input class="js-pay-disc" type="text" inputmode="decimal" value="0.00" /></div>', "fa fa-tag");
            var $refField = field(lbl("VAS_Reference"), '<div class="control"><input class="js-pay-ref" type="text" /></div>', "fa fa-file-text-o");
            // Check date - shown only for a cheque method; then the check date and
            // reference are mandatory (validated on submit).
            var $checkDateField = field(lbl("VAS_189_CheckDate", "Check date"),
                '<div class="control"><input class="js-pay-checkdate" type="date" /></div>', "fa fa-calendar-check-o");
            $checkDateField.hide();

            $grid.append($bankField).append($currencyField).append($dateField).append($convTypeField)
                .append($methodField).append($amtField).append($discField).append($refField).append($checkDateField);
            $pay.append($grid);
            $bodyM.append($pay);

            // Cheque methods (VA009 base type "S"): show the check-date column and
            // rename Reference to "Check no".
            var $refLabel = $refField.find("label");
            function methodIsCheck() {
                var id = parseInt($methodSel.val(), 10) || 0;
                if (!id) return false;
                var m = (meta.PaymentMethods || []).filter(function (x) { return +x.VA009_PaymentMethod_ID === id; })[0];
                return !!(m && m.BaseType === "S");
            }
            function toggleCheckDate() {
                var isCheck = methodIsCheck();
                $checkDateField.toggle(isCheck);
                $refLabel.text(isCheck ? lbl("VAS_189_CheckNo", "Check no") : lbl("VAS_Reference"));
            }
            $methodSel.on("change", toggleCheckDate);
            toggleCheckDate();

            var $settle = $(
                '<div class="' + CLS + 'settle">' +
                '<div class="srow"><span class="k js-sk1"></span><span class="v js-sv1"></span></div>' +
                '<div class="srow js-wh-row"><span class="k js-sk2"></span><span class="v js-sv2"></span></div>' +
                '<div class="srow"><span class="k js-sk4"></span><span class="v js-pay"></span></div>' +
                '<div class="srow"><span class="k js-sk5"></span><span class="v js-disc"></span></div>' +
                '<div class="srow tot"><span class="k js-sk6"></span><span class="v js-settle"></span></div>' +
                '<div class="bar"><span class="js-bar"></span></div>' +
                '<div class="srow"><span class="k js-sk7"></span><span class="v js-remain"></span></div>' +
                '<div class="note js-over"></div>' +
                '</div>'
            );
            $settle.find(".js-sk1").text(lbl("VAS_189_GrossInvoiceTotal", "Gross Invoice Total"));
            $settle.find(".js-sv1").text(fmtAmountCur(meta.GrossInvoice, cur, p));
            $settle.find(".js-sk2").text(lbl("VAS_189_LessWithholding", "Less Withholding"));
            $settle.find(".js-sv2").text(fmtAmountCur(meta.Withholding, cur, p));
            if (!(+meta.Withholding > 0)) $settle.find(".js-wh-row").hide();
            $settle.find(".js-sk4").text(lbl("VAS_189_NewReceipt", "New Receipt"));
            $settle.find(".js-sk5").text(lbl("VAS_189_Discount", "Discount"));
            $settle.find(".js-sk6").text(lbl("VAS_189_SettlingThisInvoice", "Settling this Invoice"));
            $settle.find(".js-sk7").text(lbl("VAS_189_RemainingBalance", "Remaining Balance"));
            $settle.find(".js-over").text(lbl("VAS_189_OverpaymentNote", "Overpayment will be held as a customer advance (on account)."));
            $bodyM.append($settle);

            var $payAmt = $bodyM.find(".js-pay-amt");
            var $payDisc = $bodyM.find(".js-pay-disc");
            $bodyM.find(".js-pay-date").val(new Date().toISOString().slice(0, 10));

            // Receipt currency context. The receipt is settled in the bank-account
            // currency: initially the invoice currency, but once a bank account with a
            // different currency is selected (or the date changes) the open amount is
            // converted and the field symbol / precision follow. `rate` is the
            // invoice -> receipt multiply rate (1 when the same). Mutated in place so
            // closures keep a live reference.
            var payCtx = { curId: meta.C_Currency_ID, sym: cur, prec: p, rate: 1, noRate: false };

            // Locale number format: the ERP user's configured decimal separator drives
            // how amounts are typed, parsed and re-displayed.
            var dotDecimal = (VIS.Env && typeof VIS.Env.isDecimalPoint === "function") ? VIS.Env.isDecimalPoint() : true;
            var decSep = dotDecimal ? "." : ",";
            var grpSep = dotDecimal ? "," : ".";

            function parseNum(s) {
                s = String(s).split(grpSep).join("");
                if (decSep !== ".") s = s.split(decSep).join(".");
                var n = parseFloat(s.replace(/[^0-9.\-]/g, ""));
                return isNaN(n) ? 0 : n;
            }
            function roundTo(v, prec) { var f = Math.pow(10, prec >= 0 ? prec : 0); return Math.round((+v || 0) * f) / f; }

            // Format for an editable amount field in the current locale (no grouping,
            // so it stays easy to edit). Pairs with parseNum for a clean round-trip.
            function fmtAmtInput(v, prec) {
                var s = roundTo(v, prec).toFixed(prec >= 0 ? prec : 0);
                return (decSep !== ".") ? s.replace(".", decSep) : s;
            }

            // Strip anything that is not a digit, the locale decimal separator (at
            // most one) or a LEADING minus.
            function sanitizeAmount(s) {
                s = String(s);
                var out = "", seenDec = false;
                for (var i = 0; i < s.length; i++) {
                    var c = s.charAt(i);
                    if (c === "-" && out.length === 0) out += c;
                    else if (c >= "0" && c <= "9") out += c;
                    else if (c === decSep && !seenDec) { out += decSep; seenDec = true; }
                }
                return out;
            }
            function bindAmountInput($inp) {
                $inp.attr({ inputmode: "decimal", autocomplete: "off" });
                $inp.on("keypress", function (e) {
                    if (e.ctrlKey || e.metaKey || e.which === 0 || e.which === 8) return;
                    var ch = String.fromCharCode(e.which);
                    if (ch === decSep) { if (this.value.indexOf(decSep) !== -1) e.preventDefault(); return; }
                    if (ch === grpSep) return;
                    if (ch === "-") {
                        if (this.selectionStart !== 0 || this.value.indexOf("-") !== -1) e.preventDefault();
                        return;
                    }
                    if (!/[0-9]/.test(ch)) e.preventDefault();
                });
                $inp.on("paste drop", function () {
                    var el = this;
                    setTimeout(function () { el.value = sanitizeAmount(el.value); $(el).trigger("input"); }, 0);
                });
            }

            function invRemaining() { return Math.max(0, invOpen - state.applied); }
            function payRemaining() { return roundTo(invRemaining() * payCtx.rate, payCtx.prec); }
            function payTotalBase() { return roundTo(invOpen * payCtx.rate, payCtx.prec); }

            function selectedBank() {
                var id = parseInt($bankSel.val(), 10) || 0;
                var list = meta.BankAccounts || [];
                for (var i = 0; i < list.length; i++) if (list[i].C_BankAccount_ID === id) return list[i];
                return null;
            }
            function selectedCurrencyId() { return parseInt($currencySel.val(), 10) || meta.C_Currency_ID; }
            function selectedConvTypeId() { return parseInt($convTypeSel.val(), 10) || 0; }
            function currencyOption(id) {
                var list = meta.Currencies || [];
                for (var i = 0; i < list.length; i++) if (list[i].C_Currency_ID === id) return list[i];
                return null;
            }

            // Reflect the receipt-currency symbol / precision in the amount + discount
            // fields and reset the amount to the (converted) open balance.
            function syncPayUI(resetAmount) {
                $payAmt.closest(".control").find(".pfx").text(payCtx.sym);
                $payDisc.closest(".control").find(".pfx").text(payCtx.sym);
                if (resetAmount) {
                    $payAmt.val(fmtAmtInput(payRemaining(), payCtx.prec));
                    $payDisc.val(fmtAmtInput(0, payCtx.prec));
                    $payDisc.closest(".control").removeClass("invalid");
                }
                rebaseSettleAmount();
            }

            function applyPaymentCurrency(forceReset) {
                var curId = selectedCurrencyId();
                if (curId === meta.C_Currency_ID) {
                    // Invoice currency: no conversion. Reset the amount only when the
                    // basis actually changed (currency / bank), not on a same-currency
                    // date or conversion-type change.
                    payCtx.curId = meta.C_Currency_ID; payCtx.sym = cur; payCtx.prec = p; payCtx.rate = 1; payCtx.noRate = false;
                    syncPayUI(!!forceReset);
                    recompute();
                    return;
                }
                var opt = currencyOption(curId);
                var payDate = $bodyM.find(".js-pay-date").val();
                showModalBusy(true);
                $.ajax({
                    url: VIS.Application.contextUrl + "VAS_189_ARInvoiceDetailPanel/ConvertOpenAmount",
                    type: "GET",
                    dataType: "json",
                    data: {
                        C_Invoice_ID: $self.record_ID, C_Currency_ID: curId,
                        C_ConversionType_ID: selectedConvTypeId(), Amount: invRemaining(), Date: payDate
                    },
                    success: function (raw) {
                        showModalBusy(false);
                        var resp = (typeof raw === "string") ? jQuery.parseJSON(raw) : raw;
                        if (resp && resp.Success) {
                            payCtx.curId = resp.C_Currency_ID;
                            payCtx.sym = resp.CurSymbol || (opt && opt.CurSymbol) || cur;
                            payCtx.prec = (resp.StdPrecision >= 0) ? resp.StdPrecision : p;
                            payCtx.rate = (resp.Rate > 0) ? resp.Rate : 1;
                            payCtx.noRate = false;
                            syncPayUI(true);
                            recompute();
                        } else {
                            // No conversion rate: keep the selected currency but set the
                            // amount to ZERO (nothing can be recorded without a rate).
                            payCtx.curId = (resp && resp.C_Currency_ID) ? resp.C_Currency_ID : curId;
                            payCtx.sym = (resp && resp.CurSymbol) || (opt && opt.CurSymbol) || cur;
                            payCtx.prec = (resp && resp.StdPrecision >= 0) ? resp.StdPrecision : (opt && opt.StdPrecision >= 0 ? opt.StdPrecision : p);
                            payCtx.rate = 0;
                            payCtx.noRate = true;
                            syncPayUI(false);
                            $payAmt.val(fmtAmtInput(0, payCtx.prec));
                            $payDisc.val(fmtAmtInput(0, payCtx.prec));
                            rebaseSettleAmount();
                            recompute();
                            error((resp && resp.Message) || lbl("VAS_189_NoConversionRate", "No conversion rate found for the selected currency."));
                        }
                    },
                    error: function (xhr) {
                        showModalBusy(false); console.log(xhr);
                        error(lbl("VAS_189_ActionFailed", "The action could not be completed."));
                    }
                });
            }

            function selectedCreditAmount() {
                // Worked entirely in the invoice currency (AvailableAmountInv), over
                // the state-model selection so off-page selections still count.
                var remaining = invOpen, total = 0;
                selectedCreditRows().forEach(function (r) {
                    var use = Math.min(+r.AvailableAmountInv, remaining);
                    if (use > 0) { total += use; remaining -= use; }
                });
                return total;
            }

            function updateCreditSummary() {
                var sel = selectedCreditAmount();
                var $btn = $bodyM.find(".js-apply-credits");
                var $sum = $bodyM.find(".js-credit-summary");
                if (state.allocationCreated) {
                    $btn.text(lbl("VAS_189_CreditsApplied", "Credits applied")).prop("disabled", true);
                    $sum.text(lbl("VAS_189_CreditsAppliedSummary", "{0} allocated. New receipt amount has been recalculated.")
                        .replace("{0}", fmtAmountCur(state.applied, cur, p)));
                    return;
                }
                if (fullySettled) {
                    $btn.prop("disabled", true);
                    $sum.text(lbl("VAS_189_NothingOpenToAllocate", "All payment schedules are paid - there is nothing left to allocate."));
                    return;
                }
                $btn.prop("disabled", sel <= 0);
                $sum.text(sel > 0
                    ? lbl("VAS_189_SelectedForAllocation", "{0} selected for allocation.").replace("{0}", fmtAmountCur(sel, cur, p))
                    : lbl("VAS_189_SelectCreditsHint", "Select available payments or credits to allocate before recording a new receipt."));
            }

            function recompute() {
                // All settlement figures are shown in the receipt (bank) currency.
                var rate = payCtx.rate, prec = payCtx.prec, sym = payCtx.sym;
                var creditPay = roundTo(state.applied * rate, prec);
                var totalBase = payTotalBase();
                var pay = parseNum($payAmt.val());

                // Rule 1: a discount can only be entered while this line has something
                // to settle. The test uses the settled amount, not the cash amount, so
                // a discount that drove the receipt to zero can still be reduced.
                if (!fullySettled) {
                    var canDiscount = Math.abs(settleAmount || 0) > 0;
                    $payDisc.prop("disabled", !canDiscount);
                    $payDisc.closest("." + CLS + "field").toggleClass("is-disabled", !canDiscount);
                    if (!canDiscount && parseNum($payDisc.val()) !== 0) {
                        $payDisc.val(fmtAmtInput(0, payCtx.prec));
                        $payDisc.closest(".control").removeClass("invalid");
                    }
                }

                var disc = parseNum($payDisc.val());
                var settle = Math.min(creditPay + pay + disc, totalBase);
                var over = Math.max(0, (creditPay + pay + disc) - totalBase);
                var remain = Math.max(0, totalBase - creditPay - pay - disc);
                // Applied amount (credits + receipt + discount) may not exceed the open
                // due; overpayment is blocked rather than advanced.
                var exceedsOpen = over > 1e-6;

                $settle.find(".js-sv1").text(fmtAmountCur(meta.GrossInvoice * rate, sym, prec));
                $settle.find(".js-sv2").text(fmtAmountCur(meta.Withholding * rate, sym, prec));
                $settle.find(".js-pay").text(fmtAmountCur(pay, sym, prec));
                $settle.find(".js-disc").text(fmtAmountCur(disc, sym, prec));
                $settle.find(".js-settle").text(fmtAmountCur(settle, sym, prec));
                $settle.find(".js-remain").text(fmtAmountCur(remain, sym, prec));
                $settle.find(".js-bar").css("width", Math.min(100, (settle / (totalBase || 1)) * 100) + "%");

                var noRate = !!payCtx.noRate;
                $payAmt.closest(".control").toggleClass("invalid", exceedsOpen || noRate);
                $settle.find(".js-over")
                    .text(noRate
                        ? lbl("VAS_189_NoConversionRate", "No conversion rate found for the selected currency.")
                        : lbl("VAS_189_AmountExceedsOpen", "Amount cannot exceed the open invoice due amount"))
                    .toggleClass("err show", exceedsOpen || noRate);

                var $submit = $scrim.find(".js-submit");
                $submit.prop("disabled", exceedsOpen || noRate || fullySettled)
                    .text(Math.abs(pay) <= 1e-6 && creditPay > 0
                        ? lbl("VAS_189_CompleteSettlement", "Complete settlement")
                        : lbl("VAS_189_RecordReceipt", "Record Receipt"));

                var $foot = $scrim.find(".js-foot-msg");
                if (fullySettled) { $foot.text(lbl("VAS_189_InvoiceFullySettled", "Invoice fully settled")); }
                else if (noRate) { $foot.text(lbl("VAS_189_NoConversionRate", "No conversion rate found for the selected currency.")); }
                else if (exceedsOpen) { $foot.text(lbl("VAS_189_AmountExceedsOpen", "Amount cannot exceed the open invoice due amount")); }
                else if (remain <= 1e-6) { $foot.text(lbl("VAS_189_InvoiceFullySettled", "Invoice fully settled")); }
                else { $foot.text(lbl("VAS_189_WillRemainOpen", "{0} will remain open").replace("{0}", fmtAmountCur(remain, sym, prec))); }
            }

            function applySelectedCredits() {
                var sel = selectedCreditAmount();
                if (sel <= 0 || state.allocationCreated) return;

                // Sources carry their FULL available amount in the source currency; the
                // server caps them against the invoice open (converted to that
                // currency), so the source amount is never converted twice.
                var sources = [];
                selectedCreditRows().forEach(function (r) {
                    if (+r.AvailableAmount > 0) sources.push({ SourceType: r.SourceType, Id: r.Id, Amount: +r.AvailableAmount });
                });

                var $btn = $bodyM.find(".js-apply-credits").prop("disabled", true);
                showModalBusy(true);
                $.ajax({
                    url: VIS.Application.contextUrl + "VAS_189_ARInvoiceDetailPanel/ApplyCredits",
                    type: "POST",
                    dataType: "json",
                    data: { payload: JSON.stringify({ C_Invoice_ID: $self.record_ID, Sources: sources }) },
                    success: function (raw) {
                        var resp = (typeof raw === "string") ? jQuery.parseJSON(raw) : raw;
                        if (resp && resp.Success) {
                            // Allocation created & completed -> refresh the modal with fresh
                            // server state and confirm with the allocation document number.
                            refreshPaymentModal(resp.DocumentNo);
                        } else {
                            showModalBusy(false);
                            $btn.prop("disabled", false);
                            error((resp && resp.Message) || lbl("VAS_189_ActionFailed", "The action could not be completed."));
                        }
                    },
                    error: function (xhr) {
                        showModalBusy(false); $btn.prop("disabled", false); console.log(xhr);
                        error(lbl("VAS_189_ActionFailed", "The action could not be completed."));
                    }
                });
            }

            // Expose the closures the footer / row handlers need.
            $bodyM.data("getPayState", function () {
                return {
                    state: state, payAmt: $payAmt, payDisc: $payDisc, invOpen: invOpen,
                    parseNum: parseNum, payCtx: payCtx, fullySettled: fullySettled
                };
            });

            bindAmountInput($payAmt);
            bindAmountInput($payDisc);

            // Receipt / discount rules:
            //  1. the discount can only be entered while there is an amount to settle,
            //  2. it can never exceed that amount,
            //  3. entering it REDUCES the receipt amount by the same value,
            //  4. receipt + discount can never exceed the open amount, and
            //  5. raising the receipt trims an already-entered discount to fit.
            // All comparisons are on magnitudes, so a negative line behaves the same
            // way with a negative discount. `settleAmount` is what this line settles
            // in total - cash plus discount.
            var settleAmount = 0;

            function rebaseSettleAmount() {
                settleAmount = roundTo(parseNum($payAmt.val()) + parseNum($payDisc.val()), payCtx.prec);
            }

            function openToSettle() {
                return Math.abs(payRemaining());
            }

            function applyPaymentAmountChange() {
                var open = openToSettle();
                var pay = parseNum($payAmt.val());
                var disc = parseNum($payDisc.val());
                var paySign = pay < 0 ? -1 : 1;
                var discSign = disc < 0 ? -1 : 1;
                var payCapped = Math.abs(pay) > open + 1e-6;

                if (payCapped) {
                    pay = paySign * open;
                    $payAmt.val(fmtAmtInput(pay, payCtx.prec));
                }

                // Room left for the discount after the receipt takes its share.
                var room = Math.max(0, roundTo(open - Math.abs(pay), payCtx.prec));
                var discTrimmed = Math.abs(disc) > room + 1e-6;

                if (discTrimmed) {
                    disc = discSign * room;
                    $payDisc.val(fmtAmtInput(disc, payCtx.prec));
                }

                rebaseSettleAmount();
                recompute();

                // After recompute (which owns the amount field's invalid state) so the
                // clamp feedback is not immediately cleared.
                $payAmt.closest(".control").toggleClass("invalid", payCapped);
                $payDisc.closest(".control").toggleClass("invalid", discTrimmed);
            }

            // Rule 3 (+2): pay = settle - discount. The sign always follows the settled
            // amount and only MAGNITUDES are compared, so the discount can never be
            // larger than what is being settled and the receipt never flips sign.
            function applyDiscountToPayment() {
                var base = settleAmount || 0;
                var sign = base < 0 ? -1 : 1;
                var entered = parseNum($payDisc.val());
                var disc = sign * Math.min(Math.abs(entered), Math.abs(base));
                var wasFixed = Math.abs(entered - disc) > 1e-6;

                if (wasFixed) {
                    $payDisc.val(fmtAmtInput(disc, payCtx.prec));
                }

                $payAmt.val(fmtAmtInput(roundTo(base - disc, payCtx.prec), payCtx.prec));
                recompute();
                $payDisc.closest(".control").toggleClass("invalid", wasFixed);
            }

            $payAmt.on("input", applyPaymentAmountChange);
            $payDisc.on("input", applyDiscountToPayment);

            // On blur, round to the selected currency's precision.
            function roundFieldToCurrency($inp) {
                $inp.val(fmtAmtInput(parseNum($inp.val()), payCtx.prec));
                if ($inp === $payDisc) { applyDiscountToPayment(); }
                else { applyPaymentAmountChange(); }
            }
            $payAmt.on("blur", function () { roundFieldToCurrency($payAmt); });
            $payDisc.on("blur", function () { roundFieldToCurrency($payDisc); });

            // Selecting a bank account sets the currency to the bank's, then converts.
            // Changing the currency or conversion type re-converts. Changing the date
            // re-converts only when the currency differs from the invoice.
            $bankSel.on("change", function () {
                var bank = selectedBank();
                if (bank && bank.C_Currency_ID) $currencySel.val(bank.C_Currency_ID);
                applyPaymentCurrency(true);
            });
            $currencySel.on("change", function () { applyPaymentCurrency(true); });
            $convTypeSel.on("change", function () { applyPaymentCurrency(true); });
            $bodyM.find(".js-pay-date").on("change", function () { applyPaymentCurrency(false); });

            // Initial state: invoice currency, amount seeded with the open balance,
            // both fields in the user's locale number format.
            $payAmt.val(fmtAmtInput(invOpen, p));
            $payDisc.val(fmtAmtInput(parseNum($payDisc.val()), p));

            // Every schedule paid: freeze the section - amount / discount are zero and
            // every field is disabled so nothing can be entered.
            if (fullySettled) {
                $payAmt.val(fmtAmtInput(0, p));
                $payDisc.val(fmtAmtInput(0, p));
                $pay.addClass("is-readonly")
                    .find("input, select").prop("disabled", true).attr("tabindex", "-1");
            }

            rebaseSettleAmount();

            // Bridge for the row handlers declared earlier in this closure.
            updateCreditSummaryRef = updateCreditSummary;
            recomputeRef = recompute;

            updateCreditSummary();
            recompute();
        }

        var updateCreditSummaryRef = function () { };
        var recomputeRef = function () { };
        function updateCreditSummary() { updateCreditSummaryRef(); }
        function recompute() { recomputeRef(); }

        function buildModalFooter() {
            var $foot = $('<div class="' + CLS + 'm-foot"></div>');
            $foot.append($('<span class="spacer js-foot-msg"></span>')
                .text(lbl("VAS_189_InvoiceFullySettled", "Invoice fully settled")));
            $foot.append($('<button type="button" class="' + CLS + 'btn js-cancel"></button>')
                .text(lbl("VAS_189_Cancel", "Cancel")).on("click", closeModal));
            $foot.append($('<button type="button" class="' + CLS + 'btn is-primary js-submit"></button>')
                .text(lbl("VAS_189_RecordReceipt", "Record Receipt")).on("click", submitPayment));
            return $foot;
        }

        function submitPayment() {
            var $bodyM = $scrim.find("." + CLS + "m-body");
            var getState = $bodyM.data("getPayState");
            if (!getState) return;
            var st = getState();
            var payCtx = st.payCtx || {
                curId: meta.C_Currency_ID, sym: (meta.CurSymbol || meta.ISO_Code || ""),
                prec: (meta.StdPrecision >= 0 ? meta.StdPrecision : 2), rate: 1
            };
            var pay = st.parseNum(st.payAmt.val());
            var disc = st.parseNum(st.payDisc.val());

            // Fully settled: the section is read-only, nothing to record.
            if (st.fullySettled) {
                info(lbl("VAS_189_AllSchedulesPaidMsg",
                    "All payment schedules of this invoice are paid. No further payment can be recorded."));
                return;
            }

            if (Math.abs(pay) <= 1e-6 && st.state.applied > 0) {
                // Settlement fully covered by the already-created allocation (shown in
                // the invoice currency - the allocation is in invoice currency).
                var curSym = meta.CurSymbol || meta.ISO_Code || "";
                showSuccess(lbl("VAS_189_CreditApplied", "Credit applied"),
                    lbl("VAS_189_CreditAppliedMsg", "The selected on-account payment and credit have been allocated to this invoice."),
                    [{ k: lbl("VAS_189_AllocationCreated", "Allocation created"), v: fmtAmountCur(st.state.applied, curSym, meta.StdPrecision) }]);
                return;
            }

            // A discount rides along with an actual receipt - it never stands on its
            // own. Both sides are tested on their magnitude.
            if (Math.abs(disc) > 1e-6 && Math.abs(pay) <= 1e-6) {
                error(lbl("VAS_189_DiscountNeedsPayment", "Enter a receipt amount before recording a discount."));
                return;
            }

            var bankId = parseInt($bodyM.find(".js-pay-bank").val(), 10) || 0;
            var methodId = parseInt($bodyM.find(".js-pay-method").val(), 10) || 0;
            if (methodId <= 0) {
                error(lbl("VAS_189_PaymentMethodRequired", "Please select a payment method."));
                return;
            }
            if (bankId <= 0) {
                error(lbl("VAS_189_BankAccountRequired", "Please select a bank account."));
                return;
            }

            // Cheque receipts require a check date and a reference number.
            var payIsCheck = (meta.PaymentMethods || []).some(function (m) {
                return +m.VA009_PaymentMethod_ID === methodId && m.BaseType === "S";
            });
            var checkDate = $bodyM.find(".js-pay-checkdate").val();
            var refNo = $bodyM.find(".js-pay-ref").val();
            if (payIsCheck) {
                if (!checkDate) {
                    error(lbl("VAS_189_CheckDateRequired", "Check date is required for check payments."));
                    return;
                }
                if (!refNo || !String(refNo).trim()) {
                    error(lbl("VAS_189_ReferenceRequired", "Check number is required for check payments."));
                    return;
                }
            }

            // Guard: applied credits + receipt + discount cannot exceed the open due
            // (compared in the receipt currency).
            var rate = payCtx.rate || 1;
            var totalBase = st.invOpen * rate;
            var creditPay = (+st.state.applied || 0) * rate;
            if ((creditPay + pay + disc) - totalBase > 1e-6) {
                error(lbl("VAS_189_AmountExceedsOpen", "Amount cannot exceed the open invoice due amount"));
                return;
            }

            var payload = {
                C_Invoice_ID: $self.record_ID,
                PayAmt: pay,
                C_Currency_ID: payCtx.curId || meta.C_Currency_ID,
                C_ConversionType_ID: parseInt($bodyM.find(".js-pay-convtype").val(), 10) || 0,
                C_BankAccount_ID: bankId,
                VA009_PaymentMethod_ID: methodId,
                DateTrx: $bodyM.find(".js-pay-date").val(),
                DiscountAmt: disc,
                ReferenceNo: refNo,
                CheckDate: checkDate
            };
            var $submit = $scrim.find(".js-submit").prop("disabled", true);
            showModalBusy(true);
            $.ajax({
                url: VIS.Application.contextUrl + "VAS_189_ARInvoiceDetailPanel/RecordPayment",
                type: "POST",
                dataType: "json",
                data: { payload: JSON.stringify(payload) },
                success: function (raw) {
                    showModalBusy(false);
                    var resp = (typeof raw === "string") ? jQuery.parseJSON(raw) : raw;
                    if (resp && resp.Success) {
                        showSuccess(lbl("VAS_189_PaymentRecorded", "Receipt recorded"),
                            lbl("VAS_189_PaymentRecordedMsg", "The receipt {0} and allocation have been created against the net receivable amount.")
                                .replace("{0}", resp.DocumentNo || ""),
                            [{ k: lbl("VAS_189_NewReceipt", "New receipt"), v: fmtAmountCur(resp.PayAmt, payCtx.sym, payCtx.prec) }]);
                    } else {
                        $submit.prop("disabled", false);
                        error((resp && resp.Message) || lbl("VAS_189_ActionFailed", "The action could not be completed."));
                    }
                },
                error: function (xhr) {
                    showModalBusy(false); $submit.prop("disabled", false); console.log(xhr);
                    error(lbl("VAS_189_ActionFailed", "The action could not be completed."));
                }
            });
        }

        function buildSuccessView() {
            return $(
                '<div class="' + CLS + 'success js-success" style="display:none;">' +
                '<div class="' + CLS + 'm-success">' +
                '<div class="circle"><i class="fa fa-check"></i></div>' +
                '<h3 class="js-ok-title"></h3>' +
                '<p class="js-ok-msg"></p>' +
                '<div class="recap js-ok-recap"></div>' +
                '</div>' +
                '<div class="' + CLS + 'm-foot">' +
                '<button type="button" class="' + CLS + 'btn is-primary js-done"></button>' +
                '</div>' +
                '</div>'
            );
        }

        function showSuccess(title, message, recapRows) {
            if (!$scrim) return;
            // The document changed -> refresh the panel when the modal closes (Done,
            // X, Esc and scrim click all funnel through closeModal).
            panelDirty = true;
            $scrim.find(".js-form").hide();
            // Use flex (not .show()'s display:block) so the success body can scroll.
            var $s = $scrim.find(".js-success").css("display", "flex");
            $s.find(".js-ok-title").text(title);
            $s.find(".js-ok-msg").text(message);
            var $recap = $s.find(".js-ok-recap").empty();
            (recapRows || []).forEach(function (r) {
                $recap.append($('<div class="rr"></div>')
                    .append($('<span class="k"></span>').text(r.k))
                    .append($('<span class="v"></span>').text(r.v)));
            });
            $s.find(".js-done").text(lbl("VAS_189_Done", "Done")).off("click").on("click", function () {
                closeModal();   // closeModal re-fetches the panel data (panelDirty)
            });
        }

        function field(labelText, controlHtml, iconCls) {
            var $f = $('<div class="' + CLS + 'field"></div>');
            $f.append($('<label></label>').text(labelText));
            var $control = $(controlHtml);
            if (iconCls) $control.prepend($('<i class="' + CLS + 'field-ic ' + iconCls + '" aria-hidden="true"></i>'));
            // Make the whole date field open the native picker (its native right-side
            // indicator is hidden in CSS). showPicker() must run from a user gesture.
            var $dateInput = $control.find('input[type="date"]');
            if ($dateInput.length) {
                $control.css("cursor", "pointer").on("click", function () {
                    var el = $dateInput[0];
                    if (el && typeof el.showPicker === "function") {
                        try { el.showPicker(); } catch (e) { /* unsupported / not allowed */ }
                    }
                });
            }
            $f.append($control);
            return $f;
        }

        function closeModal() {
            if (!$scrim) return;
            $scrim.removeClass("open");
            $(document).off("keydown.vas189Modal");
            setTimeout(removeModal, 220);
            // An allocation / receipt was created while the modal was open: reload the
            // panel so the hero, the schedule table, the allocations and the action
            // buttons show the new state.
            if (panelDirty) {
                panelDirty = false;
                $self.fetchData($self.record_ID);
            }
        }

        function removeModal() {
            if ($scrim) { $scrim.remove(); $scrim = null; }
        }

        /* ===================== Set up recurring invoice ===================== */

        /* Inline SVG for the recurring dialog. Inline (not an icon font / CSS class
           library) because the host shell may not load an icon font, which would
           leave blank boxes. `currentColor` lets each well tint its own glyph. */
        function rsvg(paths, stroke) {
            return '<svg viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="' +
                (stroke || 2) + '" stroke-linecap="round" stroke-linejoin="round" aria-hidden="true" focusable="false">' +
                paths + '</svg>';
        }
        var RSVG = {
            repeat: rsvg('<path d="m17 2 4 4-4 4"/><path d="M3 11v-1a4 4 0 0 1 4-4h14"/><path d="m7 22-4-4 4-4"/><path d="M21 13v1a4 4 0 0 1-4 4H3"/>'),
            repeatSm: rsvg('<path d="m17 2 4 4-4 4"/><path d="M3 11v-1a4 4 0 0 1 4-4h14"/><path d="m7 22-4-4 4-4"/><path d="M21 13v1a4 4 0 0 1-4 4H3"/>'),
            close: rsvg('<path d="M18 6 6 18M6 6l12 12"/>', 2.2),
            calendar: rsvg('<rect width="18" height="18" x="3" y="4" rx="2"/><path d="M16 2v4M8 2v4M3 10h18"/>'),
            calendarDots: rsvg('<rect width="18" height="18" x="3" y="4" rx="2"/><path d="M16 2v4M8 2v4M3 10h18M8 14h.01M12 14h.01M16 14h.01"/>'),
            chart: rsvg('<path d="M3 3v18h18"/><path d="m7 15 4-4 4 4 5-6"/>'),
            clock: rsvg('<circle cx="12" cy="12" r="9"/><path d="M12 7v5l3 2"/>'),
            calendarClock: rsvg('<path d="M21 7.5V6a2 2 0 0 0-2-2H5a2 2 0 0 0-2 2v14a2 2 0 0 0 2 2h5.5"/><path d="M16 2v4M8 2v4M3 10h18"/><circle cx="18" cy="18" r="4"/><path d="M18 16.5V18l1 1"/>'),
            list: rsvg('<path d="M10 6h11M10 12h11M10 18h11"/><path d="M4 6h1v4M4 10h2M6 18H4c0-1 2-2 2-3s-1-1.5-2-1"/>'),
            docCheck: rsvg('<path d="M4 22V4a2 2 0 0 1 2-2h9l5 5v3"/><path d="M15 2v5h5"/><circle cx="16" cy="17" r="5"/><path d="m14.5 17 1.2 1.2 2.3-2.4"/>'),
            lines: rsvg('<path d="M17 6.1H3M21 12.1H3M15.1 18H3"/>'),
            rows: rsvg('<path d="M8 6h13M8 12h13M8 18h13"/><path d="M3 6h.01M3 12h.01M3 18h.01"/>'),
            save: rsvg('<path d="M19 21H5a2 2 0 0 1-2-2V5a2 2 0 0 1 2-2h11l5 5v11a2 2 0 0 1-2 2z"/><path d="M17 21v-8H7v8M7 3v5h8"/>', 2.2),
            up: rsvg('<path d="m18 15-6-6-6 6"/>', 3.5),
            down: rsvg('<path d="m6 9 6 6 6-6"/>', 3.5)
        };

        // The frequency choices the dialog offers. `token` is the semantic value sent
        // to the server, which maps it onto the constants MRecurring exposes - the
        // browser never guesses a stored short code. `unit` names the interval.
        //
        // DAILY carries no button (btn: false): the dialog does not offer it, but
        // MRecurring supports it and a schedule created elsewhere may use it. Keeping
        // the entry means such a record still shows the right unit and is saved back
        // unchanged instead of being silently rewritten to another frequency.
        var RECUR_TYPES = [
            { token: "DAILY", label: "VAS_189_Daily", labelText: "Daily", unit: "VAS_189_DayUnit", unitText: "day(s)", btn: false },
            { token: "WEEKLY", label: "VAS_189_Weekly", labelText: "Weekly", unit: "VAS_189_WeekUnit", unitText: "week(s)", icon: RSVG.calendar },
            { token: "MONTHLY", label: "VAS_189_Monthly", labelText: "Monthly", unit: "VAS_189_MonthUnit", unitText: "month(s)", icon: RSVG.calendarDots },
            { token: "QUARTERLY", label: "VAS_189_Quarterly", labelText: "Quarterly", unit: "VAS_189_QuarterUnit", unitText: "quarter(s)", icon: RSVG.chart },
            { token: "ANNUALLY", label: "VAS_189_Annually", labelText: "Annually", unit: "VAS_189_YearUnit", unitText: "year(s)", icon: RSVG.clock }
        ];

        /* Slim section divider: tinted icon well + title + rule + optional hint. */
        function recSection(tone, icon, title, hint) {
            var $s = $('<div class="' + CLS + 'rec-sec"></div>');
            $s.append($('<span class="' + CLS + 'rec-well ' + tone + '"></span>').append(icon));
            $s.append($('<h4></h4>').text(title));
            $s.append('<span class="' + CLS + 'rec-rule"></span>');
            $s.append($('<span class="' + CLS + 'rec-hint js-hint"></span>').text(hint || ""));
            return $s;
        }

        /* Field card: tinted icon well + label (+ required marker) + a body the
           caller fills with the control. */
        function recFieldCard(tone, icon, labelText, required, forId) {
            var $c = $('<div class="' + CLS + 'rec-fc"></div>');
            $c.append($('<span class="' + CLS + 'rec-well ' + tone + ' iw"></span>').append(icon));
            var $b = $('<div class="' + CLS + 'rec-fbody js-fbody"></div>');
            var $l = forId
                ? $('<label for="' + forId + '"></label>')
                : $('<span class="' + CLS + 'rec-lb"></span>');
            $l.text(labelText);
            if (required) { $l.append('<span class="' + CLS + 'rec-req" aria-hidden="true">*</span>'); }
            $b.append($l);
            $c.append($b);
            return $c;
        }

        /* Up/down stepper bound to a number input. Clamps to the input's own
           min/max and fires `change` so the preview recomputes. */
        function recStepper($input, min, max) {
            var $w = $('<div class="' + CLS + 'rec-sbtns"></div>');
            function step(delta) {
                var v = (parseInt($input.val(), 10) || min) + delta;
                if (v < min) v = min;
                if (v > max) v = max;
                $input.val(v).trigger("change");
            }
            $w.append($('<button type="button" class="' + CLS + 'rec-sbtn"></button>')
                .attr("aria-label", lbl("VAS_189_Increase", "Increase"))
                .append(RSVG.up)
                .on("click", function () { step(1); }));
            $w.append($('<button type="button" class="' + CLS + 'rec-sbtn"></button>')
                .attr("aria-label", lbl("VAS_189_Decrease", "Decrease"))
                .append(RSVG.down)
                .on("click", function () { step(-1); }));
            return $w;
        }

        function recurType(token) {
            for (var i = 0; i < RECUR_TYPES.length; i++) {
                if (RECUR_TYPES[i].token === token) return RECUR_TYPES[i];
            }
            return RECUR_TYPES[2];   // Monthly
        }

        /* ---- occurrence maths (client-side preview only) ----
           The server validates and recalculates the schedule before saving; this
           preview exists purely for immediate feedback. */

        function isLastDayOfMonth(d) {
            return d.getDate() === new Date(d.getFullYear(), d.getMonth() + 1, 0).getDate();
        }

        // Add whole months without JavaScript's date overflow (31 Jan + 1 month must
        // land on 28/29 Feb, never 3 March). A start date sitting on the last day of
        // its month keeps landing on the last day of the target month.
        function addMonths(date, months) {
            var anchorLast = isLastDayOfMonth(date);
            var y = date.getFullYear();
            var m = date.getMonth() + months;
            var lastDay = new Date(y, m + 1, 0).getDate();
            var day = anchorLast ? lastDay : Math.min(date.getDate(), lastDay);
            return new Date(y, m, day);
        }

        function addDays(date, days) {
            var d = new Date(date.getTime());
            d.setDate(d.getDate() + days);
            return d;
        }

        // Nth occurrence (n = 0 is the next-run date itself) for a token + interval.
        function occurrenceDate(startDate, token, interval, n) {
            var step = Math.max(1, interval | 0);
            switch (token) {
                case "DAILY": return addDays(startDate, step * n);
                case "WEEKLY": return addDays(startDate, step * 7 * n);
                case "QUARTERLY": return addMonths(startDate, step * 3 * n);
                case "ANNUALLY": return addMonths(startDate, step * 12 * n);
                default: return addMonths(startDate, step * n);   // MONTHLY
            }
        }

        function parseISODate(s) {
            if (!s) return null;
            var parts = String(s).slice(0, 10).split("-");
            if (parts.length !== 3) return null;
            var d = new Date(+parts[0], (+parts[1]) - 1, +parts[2]);
            return isNaN(d.getTime()) ? null : d;
        }

        function toISODate(d) {
            if (!d) return "";
            var m = d.getMonth() + 1, day = d.getDate();
            return d.getFullYear() + "-" + (m < 10 ? "0" + m : m) + "-" + (day < 10 ? "0" + day : day);
        }

        function openRecurringModal() {
            if (!data || !data.C_Invoice_ID) return;
            showBusy(true);
            $.ajax({
                url: VIS.Application.contextUrl + "VAS_189_ARInvoiceDetailPanel/GetRecurringMeta",
                type: "GET",
                dataType: "json",
                data: { C_Invoice_ID: $self.record_ID, IsSOTrx: !!$self.IsSOTrx },
                success: function (raw) {
                    showBusy(false);
                    var rmeta = (typeof raw === "string") ? jQuery.parseJSON(raw) : raw;
                    if (!rmeta || !rmeta.Success) {
                        error((rmeta && rmeta.Message) || lbl("VAS_189_LoadFailed", "Could not load the requested information."));
                        return;
                    }
                    renderRecurringModal(rmeta);
                },
                error: function (err) {
                    showBusy(false); console.log(err);
                    error(lbl("VAS_189_LoadFailed", "Could not load the requested information."));
                }
            });
        }

        function renderRecurringModal(rmeta) {
            removeRecurringModal();

            var rec = rmeta.Recurring || {};
            var prec = (rmeta.StdPrecision >= 0) ? rmeta.StdPrecision : 2;
            var curSym = rmeta.CurSymbol || rmeta.CurISO || "";
            var previewLimit = rmeta.PreviewLimit > 0 ? rmeta.PreviewLimit : 20;

            // Editing an existing schedule seeds every field from it; a new one starts
            // Monthly / 1 / 1 run, next run today.
            var stateR = {
                token: rec.Exists ? (rec.FrequencyToken || "MONTHLY") : "MONTHLY",
                frequency: rec.Exists ? Math.max(1, +rec.DisplayFrequency || 1) : 1,
                runsMax: rec.Exists ? Math.max(1, +rec.RunsMax || 1) : 1,
                nextRun: rec.Exists && rec.DateNextRun ? (String(rec.DateNextRun).slice(0, 10)) : toISODate(new Date()),
                description: rec.Description || "",
                dirty: false,
                saving: false
            };

            $recScrim = $('<div class="' + CLS + 'rec-scrim" role="dialog" aria-modal="true" aria-labelledby="' + CLS + 'rec-title"></div>');
            var $m = $('<div class="' + CLS + 'rec-modal"></div>');
            // Cancel / close consult this before discarding, so the confirmation is
            // only raised once the user has actually changed something.
            $m.data("dirtyCheck", function () { return !!stateR.dirty; });
            $recScrim.append($m);

            /* --- header: brand icon + title + close chip --- */
            var $head = $(
                '<div class="' + CLS + 'rec-head">' +
                '<span class="' + CLS + 'rec-brand">' + RSVG.repeat + '</span>' +
                '<div class="' + CLS + 'rec-titles">' +
                '<h2 id="' + CLS + 'rec-title" class="js-title"></h2>' +
                '</div>' +
                '<div class="' + CLS + 'rec-head-actions">' +
                '<button type="button" class="' + CLS + 'rec-x" aria-label="' + escapeHtml(lbl("VAS_189_Close", "Close")) + '">' + RSVG.close + '</button>' +
                '</div>' +
                '</div>'
            );
            $head.find(".js-title").text(lbl("VAS_189_SetUpRecurringTitle", "Set up recurring invoice"));
            $m.append($head);

            var $body2 = $('<div class="' + CLS + 'rec-body"></div>');

            /* --- section: schedule --- */
            $body2.append(recSection("b", RSVG.calendar,
                lbl("VAS_189_Schedule", "Schedule"),
                lbl("VAS_189_ScheduleHint", "Defines when each invoice is generated")));

            // Frequency type - segmented buttons, one selection at a time. A radiogroup
            // (not aria-pressed toggles): exactly one type is always in effect.
            var $segRow = $('<div class="' + CLS + 'rec-segrow"></div>');
            $segRow.append($('<span class="' + CLS + 'rec-seglab"></span>')
                .text(lbl("VAS_189_FrequencyType", "Frequency type"))
                .append('<span class="' + CLS + 'rec-req" aria-hidden="true">*</span>'));
            var $group = $('<div class="' + CLS + 'rec-seg" role="radiogroup"></div>')
                .attr("aria-label", lbl("VAS_189_FrequencyType", "Frequency type"));
            RECUR_TYPES.forEach(function (t) {
                if (t.btn === false) { return; }   // supported, but not offered here
                var $b = $('<button type="button" class="' + CLS + 'rec-seg-btn" role="radio"></button>')
                    .attr("data-token", t.token)
                    .attr("aria-checked", stateR.token === t.token ? "true" : "false")
                    .append(t.icon || "")
                    .append($('<span></span>').text(lbl(t.label, t.labelText)));
                if (stateR.token === t.token) $b.addClass("on");
                $b.on("click", function () {
                    if (stateR.token === t.token) return;
                    stateR.token = t.token;
                    stateR.dirty = true;
                    $group.find("." + CLS + "rec-seg-btn").removeClass("on").attr("aria-checked", "false");
                    $b.addClass("on").attr("aria-checked", "true");
                    syncUnit();
                    renderPreview();
                });
                $group.append($b);
            });
            $segRow.append($group);
            $body2.append($segRow);

            /* --- field-card grid --- */
            var $grid = $('<div class="' + CLS + 'rec-grid"></div>');

            // Repeat every: interval + stepper + the selected type's unit.
            var $freqInput = $('<input type="number" min="1" max="99" id="' + CLS + 'rec-freq" class="' + CLS + 'rec-num js-freq" />')
                .val(stateR.frequency);
            var $unitLabel = $('<span class="' + CLS + 'rec-unit js-unit"></span>');
            var $freqCard = recFieldCard("b", RSVG.repeatSm,
                lbl("VAS_189_RepeatEvery", "Repeat every"), true, CLS + "rec-freq");
            $freqCard.find(".js-fbody").append(
                $('<div class="' + CLS + 'rec-stepper"></div>')
                    .append($freqInput)
                    .append(recStepper($freqInput, 1, 99))
                    .append($unitLabel));
            $grid.append($freqCard);

            // Maximum runs - total invoices the schedule may create (never "remaining").
            var $runsInput = $('<input type="number" min="1" max="999" id="' + CLS + 'rec-runs" class="' + CLS + 'rec-num js-runs" />')
                .val(stateR.runsMax);
            var $runsCard = recFieldCard("t", RSVG.list,
                lbl("VAS_189_MaximumRuns", "Maximum runs"), true, CLS + "rec-runs");
            $runsCard.find(".js-fbody").append(
                $('<div class="' + CLS + 'rec-stepper"></div>')
                    .append($runsInput)
                    .append(recStepper($runsInput, 1, 999))
                    .append($('<span class="' + CLS + 'rec-unit"></span>')
                        .text(lbl("VAS_189_InvoiceUnit", "invoice(s)"))));
            $grid.append($runsCard);

            // Date next run - the Onfinity date control is not available inside a
            // detached panel dialog, so the native date input is used and the value is
            // exchanged in ISO form; the browser renders it in the user's locale.
            var $dateInput = $('<input type="date" id="' + CLS + 'rec-date" class="' + CLS + 'rec-date js-date" />')
                .val(stateR.nextRun);
            var $dateCard = recFieldCard("a", RSVG.calendarClock,
                lbl("VAS_189_DateNextRun", "Date next run"), true, CLS + "rec-date");
            $dateCard.find(".js-fbody").append($dateInput);
            $grid.append($dateCard);

            // Schedule ends - derived, never entered.
            var $endsCard = recFieldCard("p", RSVG.docCheck,
                lbl("VAS_189_ScheduleEnds", "Schedule ends"), false, "");
            $endsCard.addClass("is-readonly");
            var $endsVal = $('<div class="' + CLS + 'rec-val js-ends"></div>');
            $endsCard.find(".js-fbody").append($endsVal);
            $grid.append($endsCard);

            // Description - optional free text carried onto every generated invoice.
            var $descInput = $('<textarea id="' + CLS + 'rec-desc" class="' + CLS + 'rec-textarea js-desc" rows="2" maxlength="255"></textarea>')
                .attr("placeholder", lbl("VAS_189_DescriptionPlaceholder",
                    "Note shown on every generated invoice — e.g. Monthly retainer, contract CT-4471"))
                .val(stateR.description);
            var $descCard = recFieldCard("b", RSVG.lines,
                lbl("VAS_189_Description", "Description"), false, CLS + "rec-desc");
            $descCard.addClass("span-all");
            $descCard.find(".js-fbody").append($descInput);
            $grid.append($descCard);

            $body2.append($grid);

            /* --- section: upcoming occurrences --- */
            var $occSec = recSection("t", RSVG.rows,
                lbl("VAS_189_UpcomingOccurrences", "Upcoming occurrences"), "");
            var $occHint = $occSec.find(".js-hint");
            $body2.append($occSec);

            /* --- summary strip --- */
            var $strip = $(
                '<div class="' + CLS + 'rec-strip">' +
                '<div class="' + CLS + 'rec-stat"><div class="k js-k1"></div><div class="v js-lastrun"></div></div>' +
                '<div class="' + CLS + 'rec-stat"><div class="k js-k2"></div><div class="v js-remaining"></div></div>' +
                '<div class="' + CLS + 'rec-stat"><div class="k js-k3"></div><div class="v js-value"></div></div>' +
                '<div class="' + CLS + 'rec-stat"><div class="k js-k4"></div><div class="v js-rule"></div></div>' +
                '</div>'
            );
            $strip.find(".js-k1").text(lbl("VAS_189_DateLastRun", "Date last run"));
            $strip.find(".js-k2").text(lbl("VAS_189_RemainingRuns", "Remaining runs"));
            $strip.find(".js-k3").text(lbl("VAS_189_ScheduledValue", "Scheduled value"));
            $strip.find(".js-k4").text(lbl("VAS_189_Recurrence", "Recurrence"));
            // Date last run is the actual last successful execution - never Updated.
            $strip.find(".js-lastrun").text(rec.DateLastRun ? fmtDate(rec.DateLastRun) : "—");
            $body2.append($strip);

            /* --- occurrence table --- */
            var $tbl = $(
                '<div class="' + CLS + 'rec-tblwrap">' +
                '<table class="' + CLS + 'rec-tbl">' +
                '<thead><tr>' +
                '<th class="c-run js-c1"></th>' +
                '<th class="c-date js-c2"></th>' +
                '<th class="js-c3"></th>' +
                '<th class="c-status js-c4"></th>' +
                '<th class="c-cur js-c5"></th>' +
                '<th class="c-amt right js-c6"></th>' +
                '</tr></thead>' +
                '<tbody class="js-prows"></tbody>' +
                '</table>' +
                '</div>' +
                '<div class="' + CLS + 'rec-pnote js-pnote"></div>'
            );
            $tbl.filter("." + CLS + "rec-tblwrap").find(".js-c1").text(lbl("VAS_189_RunNo", "Run #"));
            $tbl.filter("." + CLS + "rec-tblwrap").find(".js-c2").text(lbl("VAS_189_ExpectedDate", "Expected date"));
            $tbl.filter("." + CLS + "rec-tblwrap").find(".js-c3").text(lbl("VAS_189_CreatedInvoiceNo", "Created invoice / Invoice no"));
            $tbl.filter("." + CLS + "rec-tblwrap").find(".js-c4").text(lbl("VAS_189_Status", "Status"));
            $tbl.filter("." + CLS + "rec-tblwrap").find(".js-c5").text(lbl("VAS_189_Currency", "Currency"));
            $tbl.filter("." + CLS + "rec-tblwrap").find(".js-c6").text(lbl("VAS_189_DocumentAmount", "Document amount"));
            $body2.append($tbl);
            $m.append($body2);

            /* --- footer --- */
            var $foot = $(
                '<div class="' + CLS + 'rec-foot">' +
                '<span class="' + CLS + 'rec-legend js-legend"></span>' +
                '<div class="' + CLS + 'rec-actions">' +
                '<button type="button" class="' + CLS + 'rec-btn js-rec-cancel"></button>' +
                '<button type="button" class="' + CLS + 'rec-btn is-primary js-rec-save"></button>' +
                '</div>' +
                '</div>'
            );
            $foot.find(".js-legend").html(
                escapeHtml(lbl("VAS_189_RequiredLegendPre", "Fields marked")) +
                ' <b>*</b> ' +
                escapeHtml(lbl("VAS_189_RequiredLegendPost", "are required")));
            $foot.find(".js-rec-cancel").text(lbl("VAS_189_Cancel", "Cancel"));
            $foot.find(".js-rec-save")
                .append(RSVG.save)
                .append($('<span></span>').text(lbl("VAS_189_SaveSchedule", "Save schedule")));
            $m.append($foot);

            // Dialog-scoped busy overlay. The scrim sits at body level above the
            // panel, so the panel's own indicator cannot be seen through it.
            $m.append('<div class="' + CLS + 'rec-busy" aria-hidden="true">' +
                '<div class="vis-busyindicatorinnerwrap"><i class="vis_widgetloader"></i></div></div>');

            var $prows = $body2.find(".js-prows");
            var $pnote = $body2.find(".js-pnote");
            var $remaining = $strip.find(".js-remaining");
            var $schedValue = $strip.find(".js-value");
            var $schedRule = $strip.find(".js-rule");
            var $saveBtn = $foot.find(".js-rec-save");

            /* Effective token + interval sent to the server. */
            function effective() {
                var freq = Math.max(1, parseInt($freqInput.val(), 10) || 0);
                return { token: stateR.token, freq: freq };
            }

            /* The unit beside the Frequency field follows the selected type. */
            function syncUnit() {
                var t = recurType(stateR.token);
                $unitLabel.text(lbl(t.unit, t.unitText));
            }

            /* Rebuild the occurrence preview + the summary strip. Runs already
               generated show their real invoice and a Created chip; future ones read
               "To be created" with a Scheduled chip. The first ungenerated row is
               marked `.is-next` - it is the one that fires next. */
            function renderPreview() {
                $prows.empty();
                var eff = effective();
                var start = parseISODate($dateInput.val());
                var runsMax = Math.max(1, parseInt($runsInput.val(), 10) || 0);
                var runs = (rec.Runs || []);
                var t = recurType(stateR.token);

                // Recurrence read-out is valid even without a date.
                $schedRule.text(lbl("VAS_189_EveryN", "Every {0} {1}")
                    .replace("{0}", eff.freq)
                    .replace("{1}", lbl(t.unit, t.unitText)));
                $remaining.html(escapeHtml(String(Math.max(0, runsMax - runs.length))) +
                    ' <small>' + escapeHtml(lbl("VAS_189_OfCount", "of {0}").replace("{0}", runsMax)) + '</small>');
                $schedValue.html(escapeHtml(fmtAmountCur(rmeta.GrandTotal * runsMax, curSym, prec)) +
                    ' <small>' + escapeHtml(rmeta.CurISO || "") + '</small>');

                if (!start) {
                    $pnote.text(lbl("VAS_189_EnterNextRunDate", "Enter the next run date.")).addClass("err");
                    $endsVal.text("—");
                    $occHint.text("");
                    return;
                }
                $pnote.removeClass("err");

                var shown = Math.min(runsMax, previewLimit);
                var lastDate = start;
                var nextMarked = false;

                for (var i = 0; i < runsMax; i++) {
                    var done = runs[i];
                    // Projected occurrence: n counts from the next-run date, so the
                    // first ungenerated row sits on that date itself.
                    var n = i - runs.length;
                    if (n < 0) n = 0;
                    var d = done && done.DateDoc ? new Date(done.DateDoc) : occurrenceDate(start, eff.token, eff.freq, n);
                    if (!isNaN(d.getTime())) { lastDate = d; }

                    // Rows past the preview cap still drive the end date, but are not
                    // rendered - the note below says how many were left out.
                    if (i >= shown) { continue; }

                    var $r = $('<tr></tr>');
                    if (!done && !nextMarked) { $r.addClass("is-next"); nextMarked = true; }
                    $r.append($('<td class="c-run"></td>').text(i + 1));
                    $r.append($('<td class="c-date"></td>').text(fmtDate(d)));

                    var $c3 = $('<td></td>');
                    if (done) {
                        var docLabel = (done.DocTypeName ? done.DocTypeName + "/" : "") + (done.DocumentNo || "");
                        if (done.IsCreated) {
                            // Only linked when the user can actually read that invoice.
                            $c3.append($('<a href="javascript:void(0)" class="' + CLS + 'rec-link"></a>')
                                .text(docLabel)
                                .on("click", (function (invId) {
                                    return function () { zoomToInvoice(invId); };
                                })(done.C_Invoice_ID)));
                        } else {
                            $c3.append($('<span></span>').text(docLabel));
                        }
                    } else {
                        $c3.append($('<span class="' + CLS + 'rec-tbc"></span>')
                            .text(lbl("VAS_189_ToBeCreated", "To be created")));
                    }
                    $r.append($c3);

                    $r.append($('<td></td>').append(done
                        ? chip(lbl("VAS_189_Created", "Created"), "ok")
                        : chip(lbl("VAS_189_Scheduled", "Scheduled"), "info")));

                    // Currency leads the amount so the figure sits at the row's end.
                    $r.append($('<td class="c-cur"></td>').text(done
                        ? (done.CurISO || rmeta.CurISO || "")
                        : (rmeta.CurISO || "")));
                    // The amount is informational: the generated invoice may differ if
                    // taxes / prices / rates are recalculated at run time.
                    $r.append($('<td class="c-amt right"></td>').text(done
                        ? fmtAmountCur(done.GrandTotal, done.CurSymbol || done.CurISO || curSym,
                            (done.StdPrecision >= 0) ? done.StdPrecision : prec)
                        : fmtAmountCur(rmeta.GrandTotal, curSym, prec)));

                    $prows.append($r);
                }

                $endsVal.html(escapeHtml(fmtDate(lastDate)) +
                    ' <small>· ' + escapeHtml(lbl("VAS_189_AfterRun", "after run {0}").replace("{0}", runsMax)) + '</small>');
                $occHint.text(lbl("VAS_189_OccurrenceHint", "{0} invoice(s) · {1} each")
                    .replace("{0}", runsMax)
                    .replace("{1}", fmtAmountCur(rmeta.GrandTotal, curSym, prec)));

                // Say plainly when the preview is truncated - never let a capped list
                // read as the whole schedule.
                $pnote.text(runsMax > shown
                    ? lbl("VAS_189_ShowingNextOccurrences", "Showing the next {0} of {1} scheduled occurrences.")
                        .replace("{0}", shown).replace("{1}", runsMax)
                    : "");
            }

            function markDirty() { stateR.dirty = true; }

            $freqInput.on("input change", function () { markDirty(); renderPreview(); });
            $runsInput.on("input change", function () { markDirty(); renderPreview(); });
            $dateInput.on("input change", function () { markDirty(); renderPreview(); });
            $descInput.on("input", markDirty);

            $foot.find(".js-rec-cancel").on("click", function () { closeRecurringModal(); });
            $head.find("." + CLS + "rec-x").on("click", function () { closeRecurringModal(); });
            $recScrim.on("click", function (e) { if (e.target === $recScrim[0]) closeRecurringModal(); });
            $(document).off("keydown.vas189Recur").on("keydown.vas189Recur", function (e) {
                if (e.key === "Escape") closeRecurringModal();
            });

            $saveBtn.on("click", function () { saveRecurring(); });

            /* Validate, then create / update the schedule. The dialog closes only
               after a successful save; the button is disabled while it runs so a
               double click cannot submit twice. */
            function saveRecurring() {
                if (stateR.saving) return;

                var eff = effective();
                var runsMax = parseInt($runsInput.val(), 10) || 0;
                var nextRun = $dateInput.val();

                if (!stateR.token) {
                    error(lbl("VAS_189_SelectFrequencyType", "Select a frequency type."));
                    return;
                }
                if (eff.freq < 1) {
                    error(lbl("VAS_189_FrequencyGreaterZero", "Frequency must be greater than zero."));
                    return;
                }
                if (runsMax < 1) {
                    error(lbl("VAS_189_MaxRunsAtLeastOne", "Maximum Runs must be at least 1."));
                    return;
                }
                if (!parseISODate(nextRun)) {
                    error(lbl("VAS_189_EnterNextRunDate", "Enter the next run date."));
                    return;
                }

                // Busy state: the dialog-scoped overlay blocks the whole form (the
                // panel indicator sits BEHIND the scrim and would not be visible),
                // and the button reports progress in place.
                stateR.saving = true;
                showRecurringBusy(true);
                $saveBtn.prop("disabled", true).addClass("is-busy");

                function endBusy() {
                    stateR.saving = false;
                    showRecurringBusy(false);
                    $saveBtn.prop("disabled", false).removeClass("is-busy");
                }

                $.ajax({
                    url: VIS.Application.contextUrl + "VAS_189_ARInvoiceDetailPanel/SaveRecurring",
                    type: "POST",
                    dataType: "json",
                    data: {
                        payload: JSON.stringify({
                            C_Invoice_ID: $self.record_ID,
                            FrequencyType: eff.token,
                            Frequency: eff.freq,
                            RunsMax: runsMax,
                            DateNextRun: nextRun,
                            Description: $descInput.val()
                        })
                    },
                    success: function (raw) {
                        var resp = (typeof raw === "string") ? jQuery.parseJSON(raw) : raw;
                        if (resp && resp.Success) {
                            // Saved: drop the dirty flag first so the close does not
                            // raise the discard confirmation, take the dialog down,
                            // then reload the panel behind it (the banner changed).
                            stateR.dirty = false;
                            stateR.saving = false;
                            dismissRecurringModal();
                            info(resp.Message || lbl("VAS_189_RecurringSaved", "The recurring schedule was saved successfully."));
                            $self.fetchData($self.record_ID);
                            return;
                        }
                        endBusy();
                        error((resp && resp.Message) || lbl("VAS_189_RecurringNotSaved", "The recurring schedule could not be saved."));
                    },
                    error: function (xhr) {
                        endBusy();
                        console.log(xhr);
                        error(lbl("VAS_189_RecurringNotSaved", "The recurring schedule could not be saved."));
                    }
                });
            }

            $("body").append($recScrim);
            void $recScrim[0].offsetWidth;
            $recScrim.addClass("open");

            syncUnit();
            renderPreview();
            // Focus the first control so the dialog is keyboard-usable immediately.
            $group.find("." + CLS + "rec-seg-btn.on").focus();
        }

        /* Close without saving. Confirmation is asked only when something changed,
           and neither the invoice nor the existing schedule is modified.
           VIS.ADialog.confirm is asynchronous, so the dismissal happens in its
           callback - the dialog stays put until the user actually answers. */
        function closeRecurringModal(force) {
            if (!$recScrim) return;

            var $modal = $recScrim.find("." + CLS + "rec-modal");
            var dirty = !force && $modal.length && $modal.data("dirtyCheck") ? $modal.data("dirtyCheck")() : false;

            if (!dirty) {
                dismissRecurringModal();
                return;
            }

            if (VIS && VIS.ADialog && typeof VIS.ADialog.confirm === "function") {
                VIS.ADialog.confirm("VAS_189_DiscardRecurringChanges", true, "",
                    lbl("VAS_189_Confirm", "Confirm"),
                    function (ok) {
                        if (ok) { dismissRecurringModal(); }
                    });
                return;
            }

            // No framework dialog available: close rather than trap the user.
            dismissRecurringModal();
        }

        /* Toggle the recurring dialog's own busy overlay. */
        function showRecurringBusy(show) {
            if (!$recScrim) return;
            $recScrim.find("." + CLS + "rec-busy").toggleClass("show", !!show);
        }

        /* Actually take the dialog down (no further prompting). */
        function dismissRecurringModal() {
            if (!$recScrim) return;
            $recScrim.removeClass("open");
            $(document).off("keydown.vas189Recur");
            setTimeout(removeRecurringModal, 200);
        }

        function removeRecurringModal() {
            if ($recScrim) { $recScrim.remove(); $recScrim = null; }
        }

        /* Open a generated invoice in its own window. The target window id is
           resolved at runtime from the table + sales flag - never hardcoded, since
           ids differ per environment. startWindow keeps the current screen alive;
           a full-page navigation would tear down the panel hosting this dialog. */
        function zoomToInvoice(C_Invoice_ID) {
            if (!C_Invoice_ID) return;

            /* Take the recurring dialog down first: it is a body-level modal
               scrim, so leaving it up would cover the window we are about to
               open. Dismiss rather than close - the user asked for the invoice,
               and the discard-changes prompt is both wrong here and async, so
               it would race the zoom. */
            dismissRecurringModal();

            try {
                VAS.ZoomUtil.zoomToRecord('C_Invoice_ID', C_Invoice_ID, ARInvZoomWindowId, ARInv_ZOOM_WINDOW_NAME, '')
                    .done(function (windowId) {
                        if (windowId > 0) { ARInvZoomWindowId = windowId; }
                    });
            } catch (e) {
                console.log(e);
            }
        }

        /* Open the allocation header behind an allocation number. Same contract as
           zoomToInvoice: pass 0 for the window id the first time and let ZoomUtil
           resolve 'VAS_ViewAllocation' by name, then cache what it resolves to. */
        function zoomToAllocation(C_AllocationHdr_ID) {
            if (!C_AllocationHdr_ID) return;
            try {
                VAS.ZoomUtil.zoomToRecord('C_AllocationHdr_ID', C_AllocationHdr_ID, AllocZoomWindowId, Alloc_ZOOM_WINDOW_NAME, '')
                    .done(function (windowId) {
                        if (windowId > 0) { AllocZoomWindowId = windowId; }
                    });
            } catch (e) {
                console.log(e);
            }
        }

        /* Open the receipt that settled the invoice, in VAS_ARReceipt. */
        function zoomToReceipt(C_Payment_ID) {
            if (!C_Payment_ID) return;
            try {
                VAS.ZoomUtil.zoomToRecord('C_Payment_ID', C_Payment_ID, ReceiptZoomWindowId, Receipt_ZOOM_WINDOW_NAME, '')
                    .done(function (windowId) {
                        if (windowId > 0) { ReceiptZoomWindowId = windowId; }
                    });
            } catch (e) {
                console.log(e);
            }
        }

        /* Open the cash journal a cash-line settlement belongs to. The zoom targets
           the journal header, not the line - C_Cash_ID is the window's key. */
        function zoomToCashJournal(C_Cash_ID) {
            if (!C_Cash_ID) return;
            try {
                VAS.ZoomUtil.zoomToRecord('C_Cash_ID', C_Cash_ID, CashZoomWindowId, Cash_ZOOM_WINDOW_NAME, Cash_ZOOM_WINDOW_NAME_OLD)
                    .done(function (windowId) {
                        if (windowId > 0) { CashZoomWindowId = windowId; }
                    });
            } catch (e) {
                console.log(e);
            }
        }

        /* Open the GL journal behind C_AllocationLine.GL_JournalLine_ID - again the
           header, reached through the line's GL_Journal_ID. */
        function zoomToGLJournal(GL_Journal_ID) {
            if (!GL_Journal_ID) return;
            try {
                VAS.ZoomUtil.zoomToRecord('GL_Journal_ID', GL_Journal_ID, JournalZoomWindowId, Journal_ZOOM_WINDOW_NAME, Journal_ZOOM_WINDOW_NAME_OLD)
                    .done(function (windowId) {
                        if (windowId > 0) { JournalZoomWindowId = windowId; }
                    });
            } catch (e) {
                console.log(e);
            }
        }

        this.getRoot = function () { return $root; };
    };

    VAS.VAS_189_ARInvoiceDetailPanel.prototype.startPanel = function (windowNo, curTab) {
        this.windowNo = windowNo;
        this.curTab = curTab;
        if (curTab && typeof curTab.getAD_Table_ID === "function") {
            this.table_ID = curTab.getAD_Table_ID();
        }
        if (curTab && typeof curTab.getAD_Window_ID === "function") {
            this.AD_Window_ID = curTab.getAD_Window_ID();
        }
        this.init();
    };

    /* Update tab panel based on selected record */
    VAS.VAS_189_ARInvoiceDetailPanel.prototype.refreshPanelData = function (recordID, selectedRow) {
        if (selectedRow == undefined || recordID <= 0) { this.clear(); return; }
        this.record_ID = recordID;
        this.selectedRow = selectedRow;
        this.fetchData(recordID);
    };

    /* Set width as per window width */
    VAS.VAS_189_ARInvoiceDetailPanel.prototype.sizeChanged = function (width) {
        this.panelWidth = width;
    };

    /* Platform Refresh button - full rebuild of the panel from the server. */
    VAS.VAS_189_ARInvoiceDetailPanel.prototype.refreshWidget = function () {
        if (this.record_ID > 0) { this.fetchData(this.record_ID); }
    };

    /* Release variables from memory */
    VAS.VAS_189_ARInvoiceDetailPanel.prototype.dispose = function () {
        this.disposeComponent();
        this.record_ID = 0;
        this.table_ID = 0;
        this.windowNo = 0;
        this.AD_Window_ID = 0;
        this.curTab = null;
        this.selectedRow = null;
        this.panelWidth = null;
    };

})(VAS, jQuery);
