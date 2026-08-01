/**
 * Invoices Widget
 * Purpose - Show invoices needing attention on home/finance dashboard.
 *
 * Data flow:
 *   - Table rows come from Invoices/GetAttentionInvoices (server-side paged, 5 per page): only
 *     Overdue / Due soon (unpaid pay schedules) and Sent (completed/closed, e-mail not yet sent)
 *     rows, deduped to one row per invoice and converted to the client's base currency.
 *   - The duplicate alert banner + Review dialog come from Invoices/GetDuplicates (independent).
 *
 * ── Labels / Message Keys ──────────────────────────────────────────────────────────────
 *  #  | Current Text                                              | Message Key                    | MsgText
 * ----+-----------------------------------------------------------+--------------------------------+-----------------------------------------------------------
 *  1  | Invoices needing your attention                           | VAS_062_InvoicesNeedingAttention | Invoices needing your attention
 *  2  | Invoice                                                   | VAS_062_Invoice                | INVOICE
 *  3  | Customer                                                  | VAS_062_Customer               | CUSTOMER
 *  4  | DUE                                                       | VAS_Date                       | Date
 *  5  | Status                                                    | VAS_062_Status                 | STATUS
 *  6  | Amount                                                    | VAS_062_Amount                 | AMOUNT
 *  6d | Overdue                                                   | VAS_062_Overdue                | Overdue
 *  6e | Due soon                                                  | VAS_DueSoon                    | Due Soon
 *  6f | Sent (category)                                           | VAS_062_Sent                   | Sent
 *  6f1| Email sent (Sent row, e-mail done)                        | VAS_062_EmailSent              | Email sent
 *  6f2| Not sent (Sent row, e-mail pending)                       | VAS_062_EmailNotSent           | Not sent
 *  6g | Today                                                     | VAS_062_Today                  | Today
 *  6h | No data                                                   | VAS_062_NoData                 | No data
 *  6i | Previous / Next (pager)                                   | VAS_Previous / VAS_Next        | Previous / Next
 *  6j | Showing {from}–{to} of {total} (pager helper)             | VAS_Showing / VAS_Of           | Showing / of
 *  6a | + New invoice                                             | VAS_062_NewInvoice             | New invoice
 *  6b | Review                                                    | VAS_062_Review                 | Review
 *  6c | {n} duplicate suspected based on Amount and Customer      | VAS_062_DuplicateSuspectedBased | duplicate suspected based on Amount and Customer
 *  -  | Review-dialog keys (English fallbacks via lbl): VAS_062_DupBannerSub, VAS_062_DuplicateInvoiceReview,
 *     | VAS_062_DuplicateSets, VAS_062_SwitchTabsReview, VAS_062_MatchingField, VAS_062_DifferingField, VAS_062_PotentialDup,
 *     | VAS_062_DoubleBill, VAS_062_TripleBill, VAS_062_Newer, VAS_062_Newest, VAS_062_Original, VAS_062_Issued, VAS_062_Organization,
 *     | VAS_062_DocumentType, VAS_062_CustomerName, VAS_062_InvoiceDate, VAS_062_SalesOrderNo, VAS_062_DeliveryOrder, VAS_062_PaymentTerm,
 *     | VAS_062_DeliveryLocation, VAS_062_LineItems, VAS_062_NoLineItems, VAS_062_Product, VAS_062_AttributeSet, VAS_062_Charge,
 *     | VAS_062_Qty, VAS_062_UnitPrice, VAS_062_Amount, VAS_062_GrandTotal,
 *     | VAS_062_ReverseNewerInvoice, VAS_062_KeepBoth, VAS_Close, VAS_062_Confirm, VAS_062_ErrorOccurred, VAS_062_DupFooterNote
 *  7  | Duplicate suspected: ...                                  | VAS_062_DuplicateSuspected     | Duplicate suspected:
 *  8  | matches ... amount + customer                             | VAS_062_MatchesAmountCustomer  | matches {0} amount + customer
 *  9  | duplicate pairs suspected — ... customers affected        | VAS_062_DuplicatePairsSuspected | duplicate pairs suspected
 * 10  | customers affected                                        | VAS_062_CustomersAffected      | customers affected
 * 11  | Same customer, same ... amount, issued ... days apart     | VAS_062_SameCustomerSameAmount | Same customer, same {0} amount, issued {1} days apart
 * 12  | Same customer and amount ordered within 7 days            | VAS_062_SameCustomerWithin7Days | Same customer and amount ordered within 7 days — review the list below
 *  -  | Status labels (DR/IP/CO/CL/AP/NA/WP/WC/RE/VO/IN)        | VAS_062_StatusDraft etc.       | Draft / In Progress / Completed / ...
 * ──────────────────────────────────────────────────────────────────────────────────────
 */
; VIS = window.VIS || {};

; (function (VIS, $) {

    /* design.md §Widget Header / §Measurement Setup: keep --dash-inline-size on
       :root equal to the dashboard container's current pixel width so the title
       clamp resolves against the dashboard's visible content area, not the
       viewport. A single document-level ResizeObserver serves every widget (the
       var is global); without a marked container — or without ResizeObserver —
       the CSS falls back to 100vw. */
    function ensureDashInlineSizeVar($el) {
        if (window.__vasDashInlineSizeObserver) { return; }
        if (typeof ResizeObserver === 'undefined') { return; }

        var container = $el.closest('.vis-widget-container, [data-dashboard-container]')[0];
        if (!container) { return; }

        var write = function () {
            document.documentElement.style.setProperty('--dash-inline-size', container.clientWidth + 'px');
        };

        window.__vasDashInlineSizeObserver = new ResizeObserver(write);
        window.__vasDashInlineSizeObserver.observe(container);
        write();
    }

    VIS.InvoicesWidget = function () {

        this.frame;
        this.windowNo;
        var $self = this;
        var $root = $('<div class="vas-inv-root">');

        var $tableBody;
        /* Scroll region (holds the sticky head + body) and the head itself — both measured by the
           adaptive capacity sync, so they are widget-scoped (not locals of createWidget). */
        var $tableWrap;
        var $tableHead;
        var $alertBanner;
        /* Busy/loading overlay shown while the attention rows are being fetched. */
        var $busy;
        /* Footer pager (server-side paging) — design.md "Widget Footer Pager":
           left result-range helper + right compact pager control. */
        var $footer;
        var $pageInfo;
        var $pageText;
        var $prevBtn;
        var $nextBtn;
        var pageNo = 1;
        /* Adaptive server-side paging (mirrors VAS_021_RealTimeAlertsWidget): the client measures how
           many invoice rows physically fit the list region and sends that as pageSize; the server
           clamps + echoes it back. A ResizeObserver re-measures and re-pages on resize, so the list
           never grows an inner scrollbar (design.md "Internal Content Must Not Resize The Widget"). */
        var pageSize = 5;        // initial guess; replaced by the adaptive measure + server echo
        var totalPages = 0;
        var totalRecords = 0;
        var measuredRowH = 0;    // last measured invoice-row height (px)
        var loading = false;     // a fetch is in flight — suppress re-entrant capacity syncs
        var bodyObserver = null;
        var needsCapacitySync = true; // measure capacity on first load only; resize is the observer's job
        var pendingSync = false; // a resize arrived mid-fetch — re-measure once the fetch finishes
        var MIN_ROWS = 1;        // never force more rows than physically fit (no clipping)
        var ROW_H_FALLBACK = 44; // px, used only before a real row is measured
        /* Base-currency symbol from the backend; rendered before every amount. */
        var currencySymbol = '';
        /* Duplicate sets (grouped by customer) and the open review-dialog overlay. */
        var reviewSets = [];
        var $review = null;

        var ZOOM_WINDOW_NAME_NEW = 'VAS_ARInvoice';
        var windowId = 0;

        var INVOICES = [];
        /* Columns match the header order: invoice, customer, due, status, amount. */
        var COL_TMPL = '1fr 1.4fr minmax(60px,0.8fr) minmax(80px,1.1fr) minmax(70px,0.9fr)';

        function lbl(key, fallback) {
            var t = VIS.Msg.getMsg(key);
            return (t && t.charAt(0) !== '[') ? t : fallback;
        }

        /* Escape DB-sourced text before inserting it as HTML (XSS-safe). */
        function escapeHtml(s) {
            if (s == null) return '';
            return String(s).replace(/[&<>"']/g, function (c) {
                return { '&': '&amp;', '<': '&lt;', '>': '&gt;', '"': '&quot;', "'": '&#39;' }[c];
            });
        }

        /* Amount markup with the base-currency symbol placed before the number
           (sign, if any, precedes the symbol). */
        function formatAmount(value) {
            var v = Number(value) || 0;
            var sign = v < 0 ? '-' : '';
            var stdPrecision = VIS.Env.getCtx().getStdPrecision();
            var num = Math.abs(v).toLocaleString(window.navigator.language, { minimumFractionDigits: stdPrecision, maximumFractionDigits: stdPrecision });
            var sym = currencySymbol ? '<span class="vas-inv-cur">' + escapeHtml(currencySymbol) + '</span>' : '';
            return sign + sym + num;
        }

        /* Attention-status chips (keyed by the backend attention_status text), per design.md §8.
           These are distinct from the document-status codes in STATUS_CONFIG below. */
        var ATTENTION_STATUS = {
            'Overdue': { key: 'VAS_062_Overdue', fallback: 'Overdue', bg: '#FFE4E4', color: '#A33F3F' },
            'Due soon': { key: 'VAS_DueSoon', fallback: 'Due soon', bg: '#FFF6E2', color: '#9A6500' },
            'Sent': { key: 'VAS_062_Sent', fallback: 'Sent', bg: '#EEF6FF', color: '#0F69AC' }
        };

        /* Email-state chips for the 'Sent' category: completed invoices now appear whether or not the
           invoice e-mail has gone out. 'Y' = already e-mailed (done, green); otherwise the e-mail is
           still pending (amber, needs action). */
        var SENT_EMAIL_STATUS = {
            'Y': { key: 'VAS_062_EmailSent', fallback: 'Email sent', bg: '#DDF4E8', color: '#0B6B45' },
            'N': { key: 'VAS_062_EmailNotSent', fallback: 'Not sent', bg: '#FFF6E2', color: '#9A6500' }
        };

        var STATUS_CONFIG = {
            DR: { label: lbl("VAS_062_StatusDraft", 'Draft'), bg: '#F1F4F8', color: '#41576A' },
            IP: { label: lbl("VAS_062_StatusInProgress", 'In Progress'), bg: '#FFF6E2', color: '#9A6500' },
            CO: { label: lbl("VAS_062_StatusCompleted", 'Completed'), bg: '#E7F7EF', color: '#0B6B45' },
            CL: { label: lbl("VAS_062_StatusClosed", 'Closed'), bg: '#EEF6FF', color: '#0F69AC' },
            AP: { label: lbl("VAS_062_StatusApproved", 'Approved'), bg: '#E7F7EF', color: '#0B6B45' },
            NA: { label: lbl("VAS_062_StatusNotApproved", 'Not Approved'), bg: '#FCEFEF', color: '#A33F3F' },
            WP: { label: lbl("VAS_062_StatusWaitingPayment", 'Waiting Payment'), bg: '#FFF6E2', color: '#9A6500' },
            WC: { label: lbl("VAS_062_StatusWaitingConfirm", 'Waiting Confirm'), bg: '#FFF6E2', color: '#9A6500' },
            RE: { label: lbl("VAS_062_StatusReversed", 'Reversed'), bg: '#FCEFEF', color: '#A33F3F' },
            VO: { label: lbl("VAS_062_StatusVoided", 'Voided'), bg: '#FCEFEF', color: '#A33F3F' },
            IN: { label: lbl("VAS_062_StatusInvalid", 'Invalid'), bg: '#FCEFEF', color: '#A33F3F' }
        };

        /* ── Initialize ── */
        this.Initalize = function () {
            createWidget();
            bindEvents();
            /* Two independent data sources: the attention list fills the table (with paging); the
               duplicate scan only drives the alert banner + Review dialog. */
            loadAttention();
            loadDuplicates();
        };

        /* Defensively unwrap the controller payload — actions return Json(JsonConvert.Serialize(...)),
           so the body can arrive double-encoded depending on the MVC pipeline. */
        function parseResponse(res) {
            var data = res;
            if (typeof data === 'string') { data = JSON.parse(data); }
            if (typeof data === 'string') { data = JSON.parse(data); }
            return data;
        }

        /* ── Load the attention rows (Overdue / Due soon / Sent), one page at a time ── */
        function loadAttention() {
            loading = true;
            showBusy(true);
            $.ajax({
                url: VIS.Application.contextUrl + 'Invoices/GetAttentionInvoices',
                type: 'GET',
                data: { pageNo: pageNo, pageSize: pageSize },
                success: function (res) {
                    var data = parseResponse(res);
                    if (!data || data.error) {
                        INVOICES = [];
                        totalPages = 0;
                        totalRecords = 0;
                        renderRows();
                        updatePager();
                        showBusy(false);
                        loading = false;
                        return;
                    }
                    /* Symbol from whichever endpoint answers first; both report the same base currency. */
                    currencySymbol = data.symbol || currencySymbol || '';
                    pageNo = Number(data.pageNo || pageNo);
                    totalPages = Number(data.totalPages || 0);
                    totalRecords = Number(data.totalRecords || 0);

                    /* Requested a page past the last one (e.g. a zoom-out enlarged the page after the
                       measure used a stale count): the server returns an empty slice rather than
                       clamping. Step back to the real last page and re-fetch instead of flashing "No
                       data". pageNo is now <= totalPages, so this can run at most once. */
                    if (totalRecords > 0 && pageNo > totalPages) {
                        pageNo = totalPages;
                        loadAttention();
                        return;
                    }

                    var rows = data.rows || [];
                    INVOICES = $.map(rows, function (r) {
                        return {
                            id: r.documentNo,
                            cInvoiceId: r.cInvoiceId,
                            cBPartnerId: r.cBPartnerId,
                            customer: r.customerName,
                            due: r.dueDate,
                            daysDelta: r.daysDelta,
                            status: r.attentionStatus,
                            /* Y/N invoice e-mail state; drives the sent / not-sent chip on 'Sent' rows. */
                            emailSent: r.emailSent,
                            amount: parseFloat(r.amountBase) || 0
                        };
                    });
                    renderRows();
                    updatePager();
                    showBusy(false);
                    loading = false;

                    /* Measure capacity on the first load (adapt from the initial guess of 5) or when a
                       resize arrived while this fetch was in flight (pendingSync). Manual paging alone
                       does not re-measure — that would flip pageSize mid-navigation; genuine size
                       changes come through the ResizeObserver. */
                    if (needsCapacitySync || pendingSync) {
                        pendingSync = false;
                        scheduleCapacitySync();
                    }
                },
                error: function () {
                    INVOICES = [];
                    totalPages = 0;
                    totalRecords = 0;
                    renderRows();
                    updatePager();
                    showBusy(false);
                    loading = false;
                    pendingSync = false;
                }
            });
        }

        /* ── Load duplicate-suspected pairs for the alert banner + Review dialog ── */
        function loadDuplicates() {
            $.ajax({
                url: VIS.Application.contextUrl + 'Invoices/GetDuplicates',
                type: 'GET',
                success: function (res) {
                    var data = parseResponse(res);
                    /* Backend returns { symbol, pairs[] }; tolerate a bare array too. */
                    if (!currencySymbol) { currencySymbol = (data && data.symbol) || ''; }
                    var pairs = (data && data.pairs) ? data.pairs : (Array.isArray(data) ? data : []);
                    if (pairs.length > 0) {
                        /* Group the pairs into duplicate sets (one per customer) for the
                           Review dialog, and headline the banner with the set count. The
                           dialog tabs (vas-dup-tabs) are ordered by customer name. */
                        reviewSets = buildSets(pairs).sort(function (a, b) {
                            return String(a.customer || '').localeCompare(String(b.customer || ''), undefined, { sensitivity: 'base' });
                        });
                        var title = reviewSets.length + ' ' + lbl("VAS_062_DuplicateSuspectedBased", 'duplicate suspected based on Amount and Customer');
                        var sub = lbl("VAS_062_DupBannerSub", 'Same customer and amount — open Review to compare.');
                        $alertBanner.find('.vis-inv-dup-title').text(title);
                        $alertBanner.find('.vis-inv-dup-sub').text(sub);
                        $alertBanner.css('display', 'flex');
                    } else {
                        reviewSets = [];
                        $alertBanner.hide();
                    }
                },
                error: function () {
                    reviewSets = [];
                    $alertBanner.hide();
                }
            });
        }

        /* ── Build DOM ── */
        function createWidget() {
            var $card = $(
                '<div class="vas-inv-card">'
            );

            /* Header */
            var $header = $(
                '<div class="vas-inv-header">' +
                '<div class="vas-inv-header-left">' +
                '<div class="vas-inv-icon">' +
                '<svg viewBox="0 0 40 40" width="100%" height="100%" preserveAspectRatio="xMidYMid meet">' +
                '<rect width="40" height="40" rx="10" fill="#EAF8FF"/>' +
                '<rect x="10" y="9" width="16" height="21" rx="2" fill="none" stroke="#0083DA" stroke-width="1.6" stroke-linejoin="round"/>' +
                '<line x1="13.5" y1="15" x2="22.5" y2="15" stroke="#0083DA" stroke-width="1.3" stroke-linecap="round"/>' +
                '<line x1="13.5" y1="19" x2="22.5" y2="19" stroke="#0083DA" stroke-width="1.3" stroke-linecap="round"/>' +
                '<line x1="13.5" y1="23" x2="19" y2="23" stroke="#0083DA" stroke-width="1.3" stroke-linecap="round"/>' +
                '<circle cx="28" cy="28" r="8" fill="#0083DA"/>' +
                '<line x1="28" y1="24.5" x2="28" y2="28.5" stroke="#FFFFFF" stroke-width="1.8" stroke-linecap="round"/>' +
                '<circle cx="28" cy="31" r="1" fill="#FFFFFF"/>' +
                '</svg>' +
                '</div>' +
                '<span class="vas-inv-title">' + lbl("VAS_062_InvoicesNeedingAttention", 'Invoices needing your attention') + '</span>' +
                '</div>' +
                /* Right: open a new invoice on the same screen */
                '<button type="button" id="vis-inv-newbtn-' + $self.AD_UserHomeWidgetID + '" class="vas-inv-newbtn">' +
                '<span class="vas-inv-newbtn-plus">+</span> ' + lbl("VAS_062_NewInvoice", 'New Invoice') +
                '</button>' +
                '</div>'
            );

            /* Duplicate alert banner */
            $alertBanner = $(
                '<div id="vis-inv-alert-' + $self.AD_UserHomeWidgetID + '" ' +
                'class="vas-inv-alert-banner" style="display:none;">' +
                '<svg class="vas-inv-alert-icon" width="16" height="16" viewBox="0 0 24 24" fill="none" stroke="#D78B10" stroke-width="2.2" stroke-linecap="round" stroke-linejoin="round">' +
                '<path d="M4 15s1-1 4-1 5 2 8 2 4-1 4-1V3s-1 1-4 1-5-2-8-2-4 1-4 1z"/>' +
                '<line x1="4" y1="22" x2="4" y2="15"/>' +
                '</svg>' +
                '<div class="vas-inv-alert-text">' +
                '<div class="vis-inv-dup-title vas-inv-dup-title"></div>' +
                '<div class="vis-inv-dup-sub vas-inv-dup-sub"></div>' +
                '</div>' +
                '<button type="button" id="vis-inv-review-' + $self.AD_UserHomeWidgetID + '" class="vas-inv-review-btn">' +
                lbl("VAS_062_Review", 'Review') +
                '</button>' +
                '</div>'
            );

            /* Table wrapper */
            $tableWrap = $('<div class="vas-inv-table-wrap">');

            /* Table header row — frozen above the scrolling body (CSS sticky). */
            $tableHead = $(
                '<div class="vas-inv-table-head" style="grid-template-columns:' + COL_TMPL + ';">' +
                '<span class="vas-inv-th">' + lbl("VAS_062_Invoice", 'Invoice') + '</span>' +
                '<span class="vas-inv-th-customer">' + lbl("VAS_062_Customer", 'Customer') + '</span>' +
                '<span class="vas-inv-th">' + lbl("VAS_Date", 'Date') + '</span>' +
                '<span class="vas-inv-th">' + lbl("VAS_062_Status", 'Status') + '</span>' +
                '<span class="vas-inv-th">' + lbl("VAS_062_Amount", 'Amount') + '</span>' +
                '</div>'
            );

            /* Table body */
            $tableBody = $('<div id="vis-inv-tbody-' + $self.AD_UserHomeWidgetID + '">');
            renderRows();

            $tableWrap.append($tableHead).append($tableBody);

            /* Footer pager (design.md "Widget Footer Pager"): two zones — left result-range helper
               ("Showing X–Y of Z") and a right-aligned compact pager (prev · "X of N" · next) with
               rounded-square buttons. Hidden while there is only a single page. */
            var chevL = '<svg width="14" height="14" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2" stroke-linecap="round" stroke-linejoin="round"><polyline points="15 18 9 12 15 6"/></svg>';
            var chevR = '<svg width="14" height="14" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2" stroke-linecap="round" stroke-linejoin="round"><polyline points="9 18 15 12 9 6"/></svg>';
            $footer = $(
                '<div class="vas-inv-footer" style="display:none;">' +
                '<span class="vas-inv-footer-info"></span>' +
                '<div class="vas-inv-pager">' +
                '<button type="button" class="vas-inv-page-btn vas-inv-prev" aria-label="' + escapeHtml(lbl("VAS_Previous", 'Previous')) + '">' + chevL + '</button>' +
                '<span class="vas-inv-page-text"></span>' +
                '<button type="button" class="vas-inv-page-btn vas-inv-next" aria-label="' + escapeHtml(lbl("VAS_Next", 'Next')) + '">' + chevR + '</button>' +
                '</div>' +
                '</div>'
            );
            $pageInfo = $footer.find('.vas-inv-footer-info');
            $pageText = $footer.find('.vas-inv-page-text');
            $prevBtn = $footer.find('.vas-inv-prev');
            $nextBtn = $footer.find('.vas-inv-next');

            $card.append($header).append($alertBanner).append($tableWrap).append($footer);
            $root.append($card);

            /* Busy/loading overlay over the whole card, using the core spinner classes
               (vis-busyindicatorinnerwrap / vis_widgetloader). Hidden until a fetch is in flight. */
            $busy = $('<div class="vas-inv-busy"><div class="vis-busyindicatorinnerwrap"><i class="vis_widgetloader"></i></div></div>');
            $busy[0].style.visibility = 'hidden';
            $root.append($busy);

            /* Watch the scroll region so a resize re-measures how many rows fit and re-pages. */
            observeBody();
        }

        /* Toggle the busy/loading overlay. */
        function showBusy(show) {
            if (!$busy || !$busy[0]) { return; }
            $busy[0].style.visibility = show ? 'visible' : 'hidden';
        }

        /* ── Adaptive row count (No-Inner-Scrollbars rule, mirrors VAS_021) ── */

        function scheduleCapacitySync() {
            var raf = window.requestAnimationFrame || function (cb) { return window.setTimeout(cb, 16); };
            raf(function () { syncCapacity(); });
        }

        /* Measure how many invoice rows physically fit the list region (the scroll wrapper minus its
           sticky header) and, when that differs from the page size we last requested, adopt it and
           reload the current page. Converges in one step (a re-render yields the same fit). */
        function syncCapacity() {
            if (loading || !$tableWrap || !$tableWrap[0]) { return; }
            if (totalRecords <= 0) { return; }  // no real data row to size from yet

            var headH = ($tableHead && $tableHead[0]) ? $tableHead[0].offsetHeight : 0;
            var avail = $tableWrap[0].clientHeight - headH;
            if (avail <= 0) {
                // Layout not settled yet — retry next frame while an initial sync is pending.
                if (needsCapacitySync) { scheduleCapacitySync(); }
                return;
            }

            // Rows are variable-height (the customer cell wraps to 1–2 lines), so size capacity off the
            // TALLEST rendered row — sizing off the shortest row would let a two-line row overflow and
            // clip. Guarantees the whole page fits, never scrolls.
            var rows = $tableBody[0].querySelectorAll('.vas-inv-row');
            var maxH = 0;
            for (var i = 0; i < rows.length; i++) {
                if (rows[i].offsetHeight > maxH) { maxH = rows[i].offsetHeight; }
            }
            if (maxH > 0) { measuredRowH = maxH; }
            var rowH = measuredRowH > 0 ? measuredRowH : ROW_H_FALLBACK;

            needsCapacitySync = false;  // measurement succeeded — stop the initial one-shot
            var capacity = Math.max(MIN_ROWS, Math.floor(avail / rowH));
            if (capacity !== pageSize) {
                pageSize = capacity;
                /* A larger page (zoom-out) can leave the current page past the new last page. The
                   server does not clamp pageNo, so step back to the last page that still has data
                   before reloading (totalRecords is from the last load; the load handler backstops
                   any residual mismatch). */
                var newTotalPages = Math.max(1, Math.ceil(totalRecords / pageSize));
                if (pageNo > newTotalPages) { pageNo = newTotalPages; }
                loadAttention();
            }
        }

        function observeBody() {
            if (typeof ResizeObserver === 'undefined' || !$tableWrap || !$tableWrap[0]) { return; }
            bodyObserver = new ResizeObserver(function () {
                /* A resize that lands mid-fetch (e.g. the widget reaching its final size while the
                   first page is still loading) must not be dropped, or the capacity stays stuck at the
                   intermediate measure. Defer it and re-measure when the load finishes. */
                if (loading) { pendingSync = true; return; }
                syncCapacity();
            });
            bodyObserver.observe($tableWrap[0]);
        }

        /* Human-readable due value: "Today" for the current day, "Mon DD" otherwise, em dash when no
           due date (e.g. the Sent rows, which have no pay schedule). */
        function formatDue(value) {
            if (!value) { return '—'; }
            var d = new Date(value);
            if (isNaN(d.getTime())) { return escapeHtml(value); }
            var now = new Date();
            if (d.getFullYear() === now.getFullYear() && d.getMonth() === now.getMonth() && d.getDate() === now.getDate()) {
                return escapeHtml(lbl("VAS_062_Today", 'Today'));
            }
            return escapeHtml(d.toLocaleDateString(window.navigator.language, { day: '2-digit', month: 'short' }));
        }

        /* Chip text: Overdue rows append how many days past due (e.g. "Overdue 3d"); daysDelta is
           DueDate - today, so an overdue invoice is 0 (due today) or negative. The label text is
           computed here, never from the status filtering rule. */
        function statusLabel(inv) {
            var cfg = ATTENTION_STATUS[inv.status];
            var base = cfg ? lbl(cfg.key, cfg.fallback) : inv.status;
            if (inv.status === 'Overdue' && inv.daysDelta != null && inv.daysDelta !== '' && Number(inv.daysDelta) < 0) {
                return base + ' ' + Math.abs(Number(inv.daysDelta)) + 'd';
            }
            return base;
        }

        /* Resolve the status chip (label + colours) for a row. 'Sent' rows split on the e-mail state
           (sent vs not sent); every other row keeps its attention-status colours, with the Overdue day
           suffix supplied by statusLabel. */
        function statusChip(inv) {
            if (inv.status === 'Sent') {
                var es = SENT_EMAIL_STATUS[inv.emailSent === 'Y' ? 'Y' : 'N'];
                return { label: lbl(es.key, es.fallback), bg: es.bg, color: es.color };
            }
            var cfg = ATTENTION_STATUS[inv.status] || { bg: '#EDEDED', color: '#505050' };
            return { label: statusLabel(inv), bg: cfg.bg, color: cfg.color };
        }

        /* ── Render rows ── */
        function renderRows() {
            $tableBody.empty();

            if (!INVOICES || INVOICES.length === 0) {
                $tableBody.append('<div class="vas-inv-nodata">' + escapeHtml(lbl("VAS_062_NoData", 'No data')) + '</div>');
                return;
            }

            $.each(INVOICES, function (i, inv) {
                var chip = statusChip(inv);
                var isLast = (i === INVOICES.length - 1);
                var safeId = escapeHtml(inv.id);
                /* Escaped once; reused as both the visible text and the title tooltip. */
                var safeCustomer = escapeHtml(inv.customer);

                var $row = $(
                    '<div data-invid="' + safeId + '" ' +
                    'data-cinvid="' + escapeHtml(inv.cInvoiceId) + '" ' +
                    'class="vas-inv-row' + (isLast ? '' : ' vas-inv-row-divided') + '" ' +
                    'style="grid-template-columns:' + COL_TMPL + ';">' +
                    '<span class="vas-inv-cell-id vas-inv-doclink" role="link" tabindex="0" ' +
                    'title="' + safeId + '">' + safeId + '</span>' +
                    '<span class="vas-inv-cell-customer" title="' + safeCustomer + '">' + safeCustomer + '</span>' +
                    '<span class="vas-inv-cell-due">' + formatDue(inv.due) + '</span>' +
                    '<span>' +
                    '<span class="vas-inv-status-chip" style="' +
                    'background:' + chip.bg + ';color:' + chip.color + ';">' +
                    escapeHtml(chip.label) +
                    '</span>' +
                    '</span>' +
                    '<span class="vas-inv-cell-amount">' + formatAmount(inv.amount) + '</span>' +
                    '</div>'
                );

                $tableBody.append($row);
            });
        }

        /* ── Pager state ── */
        function updatePager() {
            /* Right pager: "X of N" between the arrows. */
            if ($pageText) {
                $pageText.text(totalPages > 1 ? (pageNo + ' ' + lbl("VAS_Of", 'of') + ' ' + totalPages) : '');
            }
            /* Left helper: "Showing X–Y of Z" result range. */
            if ($pageInfo) {
                if (totalRecords > 0) {
                    var from = (pageNo - 1) * pageSize + 1;
                    var to = Math.min(pageNo * pageSize, totalRecords);
                    $pageInfo.text(lbl("VAS_Showing", 'Showing') + ' ' + from + '–' + to + ' ' + lbl("VAS_Of", 'of') + ' ' + totalRecords);
                } else {
                    $pageInfo.text('');
                }
            }
            if ($prevBtn) {
                $prevBtn.prop('disabled', pageNo <= 1 || totalPages <= 1);
            }
            if ($nextBtn) {
                $nextBtn.prop('disabled', totalPages <= 1 || pageNo >= totalPages);
            }
            if ($footer) {
                $footer.css('display', totalPages > 1 ? 'flex' : 'none');
            }
        }

        /* ── Events ── */
        function bindEvents() {

            /* Review — open the duplicate-invoice review dialog */
            $root.on('click', '#vis-inv-review-' + $self.AD_UserHomeWidgetID, function () {
                openReview();
            });

            /* New invoice */
            $root.on('click', '#vis-inv-newbtn-' + $self.AD_UserHomeWidgetID, function () {
                onNewInvoice();
            });

            /* Document number — zoom the host window's invoice tab to that record. */
            $root.on('click', '.vas-inv-doclink', function () {
                zoomInvoice($(this).closest('.vas-inv-row').data('cinvid'));
            });
            /* Keyboard activation (Enter / Space) for the focusable doc-no link. */
            $root.on('keydown', '.vas-inv-doclink', function (e) {
                if (e.key === 'Enter' || e.key === ' ' || e.keyCode === 13 || e.keyCode === 32) {
                    e.preventDefault();
                    zoomInvoice($(this).closest('.vas-inv-row').data('cinvid'));
                }
            });

            /* Pager — previous / next page of attention rows. */
            $root.on('click', '.vas-inv-prev', function () {
                if (pageNo <= 1) { return; }
                pageNo--;
                loadAttention();
            });

            $root.on('click', '.vas-inv-next', function () {
                if (totalPages <= 1 || pageNo >= totalPages) { return; }
                pageNo++;
                loadAttention();
            });
        }

        /* The widget sits on the landing page of the same window, so open a new record in the
           first tab in-place (Scenario 1) instead of navigating away. The widget host registers
           itself through addChangeListener and reacts to this fired value to switch the tab into
           new-record mode; existing filters and widget state are left untouched. */
        function onNewInvoice() {
            var windowParam = {
                "IsTabInNewMode": "true",
                "TabIndex": "0"
            };
            if ($self.windowNo >= 0) {
                $self.widgetFirevalueChanged(windowParam);
            }
            else {
                VAS.ZoomUtil.zoomToRecord("C_Invoice_ID", 0, windowId, ZOOM_WINDOW_NAME_NEW, "")
                    .done(function (id) {
                        if (id > 0) { windowId = id; }
                    });
            }

        }

        /* Zoom: fire the value the host listens for to open the clicked invoice in the window's first
           tab, filtered to the single record (single/form layout). */
        function zoomInvoice(cInvoiceId) {
            if (!cInvoiceId) { return; }
            if ($self.windowNo >= 0) {
                $self.widgetFirevalueChanged({
                    "TabWhereClause": "C_Invoice.C_Invoice_ID=" + cInvoiceId,
                    "TabLayout": "Y",   /* 'N' Grid, 'Y' Single, 'C' Card */
                    "TabIndex": "0"
                });
            } else {
                VAS.ZoomUtil.zoomToRecord("C_Invoice_ID", cInvoiceId, windowId, ZOOM_WINDOW_NAME_NEW, "")
                    .done(function (id) {
                        if (id > 0) { windowId = id; }
                    });
            }
        }

        /* ── Duplicate review dialog ────────────────────────────────────────────────
           UI per the approved duplicate_invoice_review.html design. Tabs come from the duplicate
           sets (GetDuplicates); each tab's full header + line-level detail (organization, customer,
           sales/delivery order, payment term, delivery location, grand total, line items, and the
           green/red match dots) is fetched lazily from GetDuplicateReview when first shown and cached
           on the set. The overlay is appended to <body> so it is never clipped by the card overflow. */

        /* Group duplicate pairs into one set per duplicate key (business partner + currency + grand
           total), matching the backend duplicate definition. Each set carries the invoice IDs needed
           to fetch its line-level detail (GetDuplicateReview) plus a lazy detail cache. */
        function buildSets(pairs) {
            var byKey = {};
            var order = [];

            function addInvoice(set, id, no, date, status) {
                if (!id || set.idMap[id]) return;
                set.idMap[id] = true;
                set.invoices.push({ id: id, no: no, date: date, status: status });
                set.invoiceIds.push(id);
            }

            $.each(pairs, function (_, p) {
                var key = (p.cBPartnerId || 0) + '|' + (p.cCurrencyId || 0) + '|' + (parseFloat(p.amount) || 0);
                if (!byKey[key]) {
                    byKey[key] = {
                        customer: p.customer || '',
                        amount: parseFloat(p.amount) || 0,
                        cBPartnerId: p.cBPartnerId,
                        cCurrencyId: p.cCurrencyId,
                        /* Invoice-currency symbol for this set (the set shares one currency); used for
                           the legend-context amount instead of the base-currency symbol. */
                        curSymbol: p.curSymbol || '',
                        idMap: {}, invoices: [], invoiceIds: [],
                        detail: null, loaded: false
                    };
                    order.push(key);
                }
                var set = byKey[key];
                addInvoice(set, p.invoiceAId, p.invoiceA, p.dateA, p.docStatusA);
                addInvoice(set, p.invoiceBId, p.invoiceB, p.dateB, p.docStatusB);
            });

            return order.map(function (k) { return byKey[k]; });
        }

        /* Group the flat header+line detail rows into one object per invoice, preserving the
           backend order (newest invoice first). */
        function groupDetail(rows) {
            var byInv = {};
            var order = [];
            $.each(rows, function (_, r) {
                var id = r.cInvoiceId;
                if (!byInv[id]) {
                    byInv[id] = {
                        id: id, no: r.invoiceDocumentNo, panelRole: r.panelRole,
                        docStatus: r.docStatus, invoiceStatus: r.invoiceStatus, date: r.invoiceDate,
                        org: r.organizationName, docType: r.documentTypeName, docBaseType: r.docBaseType,
                        customer: r.customerName,
                        salesOrderNo: r.salesOrderNo, deliveryOrderNo: r.deliveryOrderNo,
                        paymentTerm: r.paymentTermName, deliveryLocation: r.deliveryLocation,
                        grandTotal: r.grandTotal, currencyIso: r.currencyIso,
                        isSameCustomer: r.isSameCustomer, isSameGrandTotal: r.isSameGrandTotal,
                        isSameCurrency: r.isSameCurrency, isSamePaymentTerm: r.isSamePaymentTerm,
                        isSameInvoiceDate: r.isSameInvoiceDate,
                        lines: []
                    };
                    order.push(id);
                }
                if (r.cInvoiceLineId) {
                    byInv[id].lines.push({
                        lineNo: r.lineNo, product: r.productName, charge: r.chargeName,
                        attr: r.attributeSetInstance, qty: r.qtyEntered, unit: r.unitPrice,
                        net: r.lineNetAmount, match: r.lineMatchStatus
                    });
                }
            });
            return order.map(function (id) { return byInv[id]; });
        }

        /* Fetch (once) the line-level review detail for a set's invoice IDs, caching on the set. */
        function fetchDetail(set, cb) {
            if (set.loaded) { cb(true); return; }
            $.ajax({
                url: VIS.Application.contextUrl + 'Invoices/GetDuplicateReview',
                type: 'GET',
                data: { invoiceIds: set.invoiceIds.join(',') },
                success: function (res) {
                    var data = parseResponse(res);
                    if (!data || data.error) { cb(false); return; }
                    if (data.symbol && !currencySymbol) { currencySymbol = data.symbol; }
                    set.detail = groupDetail(data.rows || []);
                    set.loaded = true;
                    cb(true);
                },
                error: function () { cb(false); }
            });
        }

        function billWord(n) {
            if (n === 2) return lbl("VAS_062_DoubleBill", 'double-bill');
            if (n === 3) return lbl("VAS_062_TripleBill", 'triple-bill');
            return n + '×-bill';
        }

        /* Plain-text amount with a currency symbol before the number. Pass the set's invoice-currency
           symbol for duplicate amounts; falls back to the base symbol when none is given. */
        function money(value, symbol) {
            var stdPrecision = VIS.Env.getCtx().getStdPrecision();
            var num = (Number(value) || 0).toLocaleString(window.navigator.language, { minimumFractionDigits: stdPrecision, maximumFractionDigits: stdPrecision });
            var sym = (symbol != null && symbol !== '') ? symbol : currencySymbol;
            return escapeHtml(sym) + num;
        }

        /* Status chip — reuses the row STATUS_CONFIG colours. */
        function statusPill(code) {
            var cfg = STATUS_CONFIG[code] || { label: code || '—', bg: '#EDEDED', color: '#505050' };
            return '<span class="vas-dup-pill" style="background:' + cfg.bg + ';color:' + cfg.color + ';">' + escapeHtml(cfg.label) + '</span>';
        }

        /* Field row with an optional match/diff dot: 'match' (green) or 'diff' (red). The value sits in
           an inner text span so it stays on one line and trims with an ellipsis, while the match/diff
           dot (::after on the value) stays visible beside it. titleText (the full, plain value) is set
           as a tooltip on the text span so the trimmed-off content is still readable on hover. */
        function fieldRow(label, valueHtml, state, titleText) {
            var cls = state === 'diff' ? ' is-diff' : (state === 'match' ? ' is-match' : '');
            var titleAttr = (titleText != null && titleText !== '') ? ' title="' + escapeHtml(titleText) + '"' : '';
            return '<div class="vas-dup-field">' +
                '<span class="vas-dup-field-label">' + label + '</span>' +
                '<span class="vas-dup-field-value' + cls + '"><span class="vas-dup-field-text"' + titleAttr + '>' + valueHtml + '</span></span>' +
                '</div>';
        }

        /* Amount with the invoice's own ISO code (a duplicate set shares one currency). Used for the
           card grand total; line-item amounts are shown as plain numbers (no currency). */
        function moneyIso(value, iso) {
            var stdPrecision = VIS.Env.getCtx().getStdPrecision();
            var num = (Number(value) || 0).toLocaleString(window.navigator.language, { minimumFractionDigits: stdPrecision, maximumFractionDigits: stdPrecision });
            return escapeHtml(num) + (iso ? ' ' + escapeHtml(iso) : '');
        }

        /* Plain number at the standard precision — no currency symbol/ISO (line-item amounts). */
        function numAmt(value) {
            var stdPrecision = VIS.Env.getCtx().getStdPrecision();
            return escapeHtml((Number(value) || 0).toLocaleString(window.navigator.language, { minimumFractionDigits: stdPrecision, maximumFractionDigits: stdPrecision }));
        }

        /* Quantity at its natural precision (no forced decimals). */
        function numQty(value) {
            return escapeHtml((Number(value) || 0).toLocaleString(window.navigator.language));
        }

        /* Map a backend 'Y'/'N' same-flag to a dot state. */
        function flagState(sameFlag) {
            return sameFlag === 'N' ? 'diff' : (sameFlag === 'Y' ? 'match' : undefined);
        }

        /* Line-item table for one invoice. Fixed column layout with horizontal scroll so the card
           width never fluctuates between tabs; long Product/Attribute/Charge values are trimmed with
           ellipsis and carry a title tooltip. Amounts are plain numbers (no currency). */
        function buildLineTable(inv) {
            if (!inv.lines || inv.lines.length === 0) {
                return '<div class="vas-dup-lines-empty">' + escapeHtml(lbl("VAS_062_NoLineItems", 'No line items.')) + '</div>';
            }
            var body = '';
            $.each(inv.lines, function (_, ln) {
                var product = ln.product || '—';
                var attr = ln.attr || '—';
                var charge = ln.charge || '—';
                var matchCls = ln.match === 'matching' ? ' is-matching' : (ln.match === 'differing' ? ' is-differing' : '');
                body += '<tr class="vas-dup-line' + matchCls + '">' +
                    '<td class="vas-dup-line-trunc" title="' + escapeHtml(product) + '">' + escapeHtml(product) + '</td>' +
                    '<td class="vas-dup-line-trunc" title="' + escapeHtml(attr) + '">' + escapeHtml(attr) + '</td>' +
                    '<td class="vas-dup-line-trunc" title="' + escapeHtml(charge) + '">' + escapeHtml(charge) + '</td>' +
                    '<td class="vas-dup-line-num">' + numQty(ln.qty) + '</td>' +
                    '<td class="vas-dup-line-num">' + numAmt(ln.unit) + '</td>' +
                    '<td class="vas-dup-line-num">' + numAmt(ln.net) + '</td>' +
                    '</tr>';
            });
            return '<div class="vas-dup-lines-scroll"><table class="vas-dup-lines-table">' +
                '<thead><tr>' +
                '<th class="vas-dup-line-trunc">' + lbl("VAS_062_Product", 'Product') + '</th>' +
                '<th class="vas-dup-line-trunc">' + lbl("VAS_062_AttributeSet", 'Attribute Set') + '</th>' +
                '<th class="vas-dup-line-trunc">' + lbl("VAS_062_Charge", 'Charge') + '</th>' +
                '<th class="vas-dup-line-num">' + lbl("VAS_062_Qty", 'Qty') + '</th>' +
                '<th class="vas-dup-line-num">' + lbl("VAS_062_UnitPrice", 'Unit Price') + '</th>' +
                '<th class="vas-dup-line-num">' + lbl("VAS_062_Amount", 'Amount') + '</th>' +
                '</tr></thead><tbody>' + body + '</tbody></table></div>';
        }

        /* One invoice card built from the line-level review detail. */
        function buildDetailCard(inv, total, sameLocation) {
            var dash = '<span class="vas-dup-dash">—</span>';
            var role = inv.panelRole;
            var ageLabel = role === 'Newer' ? (total > 2 ? lbl("VAS_062_Newest", 'Newest') : lbl("VAS_062_Newer", 'Newer'))
                : (role === 'Original' ? lbl("VAS_062_Original", 'Original') : '');
            var dateLine = lbl("VAS_062_Issued", 'Issued') + ' ' + escapeHtml(inv.date || '—') + (ageLabel ? ' · ' + ageLabel : '');
            function val(v) { return v ? escapeHtml(v) : dash; }
            var totalState = inv.isSameGrandTotal === 'Y' ? ' is-match' : (inv.isSameGrandTotal === 'N' ? ' is-diff' : '');

            /* Credit memos (AR/AP) present their grand total as a reduction, so show it negative. */
            var isCreditMemo = inv.docBaseType === 'ARC' || inv.docBaseType === 'APC';
            var grandTotalDisplay = isCreditMemo ? -Math.abs(Number(inv.grandTotal) || 0) : inv.grandTotal;

            return '<div class="vas-dup-card">' +
                '<div class="vas-dup-card-head">' +
                '<div><div class="vas-dup-card-no">' + escapeHtml(inv.no || '—') + '</div>' +
                '<div class="vas-dup-card-date">' + dateLine + '</div></div>' +
                statusPill(inv.docStatus) +
                '</div>' +
                '<div class="vas-dup-fields">' +
                fieldRow(lbl("VAS_062_Organization", 'Organization'), val(inv.org), null, inv.org) +
                fieldRow(lbl("VAS_062_DocumentType", 'Document Type'), val(inv.docType), null, inv.docType) +
                fieldRow(lbl("VAS_062_CustomerName", 'Customer Name'), val(inv.customer), flagState(inv.isSameCustomer), inv.customer) +
                fieldRow(lbl("VAS_062_InvoiceDate", 'Invoice Date'), val(inv.date), flagState(inv.isSameInvoiceDate), inv.date) +
                fieldRow(lbl("VAS_062_SalesOrderNo", 'Sales Order No.'), val(inv.salesOrderNo), null, inv.salesOrderNo) +
                fieldRow(lbl("VAS_062_DeliveryOrder", 'Delivery Order'), val(inv.deliveryOrderNo), null, inv.deliveryOrderNo) +
                fieldRow(lbl("VAS_062_PaymentTerm", 'Payment Term'), val(inv.paymentTerm), flagState(inv.isSamePaymentTerm), inv.paymentTerm) +
                fieldRow(lbl("VAS_062_DeliveryLocation", 'Delivery Location'), val(inv.deliveryLocation), sameLocation ? 'match' : 'diff', inv.deliveryLocation) +
                '</div>' +
                '<div class="vas-dup-lines-wrap">' +
                '<div class="vas-dup-lines-title">' + lbl("VAS_062_LineItems", 'Line items') + '</div>' +
                buildLineTable(inv) +
                '</div>' +
                '<div class="vas-dup-card-total"><span class="lbl">' + lbl("VAS_062_GrandTotal", 'Grand total') + '</span>' +
                '<span class="val' + totalState + '">' + moneyIso(grandTotalDisplay, inv.currencyIso) + '</span></div>' +
                '</div>';
        }

        /* Build the card grid HTML for a loaded set. */
        function renderDetailCards(set) {
            var invs = set.detail || [];
            if (invs.length === 0) {
                return '<div class="vas-dup-lines-empty">' + escapeHtml(lbl("VAS_062_NoData", 'No data')) + '</div>';
            }
            /* Delivery location is "same" only when every compared invoice shares it. */
            var locs = {};
            $.each(invs, function (_, v) { locs[v.deliveryLocation || ''] = true; });
            var sameLocation = Object.keys(locs).length <= 1;
            var cards = '';
            $.each(invs, function (_, inv) { cards += buildDetailCard(inv, invs.length, sameLocation); });
            return cards;
        }

        /* Skeleton card mirroring the real card's structure/size, so the grid is already at its
           final shape and does not shift when the fetched detail replaces it. */
        function buildSkeletonCard() {
            var fields = '';
            for (var f = 0; f < 7; f++) {
                fields += '<div class="vas-dup-field"><span class="vas-dup-sk vas-dup-sk-label"></span><span class="vas-dup-sk vas-dup-sk-value"></span></div>';
            }
            return '<div class="vas-dup-card vas-dup-card-skeleton" aria-hidden="true">' +
                '<div class="vas-dup-card-head"><div><div class="vas-dup-sk vas-dup-sk-no"></div><div class="vas-dup-sk vas-dup-sk-date"></div></div><span class="vas-dup-sk vas-dup-sk-pill"></span></div>' +
                '<div class="vas-dup-fields">' + fields + '</div>' +
                '<div class="vas-dup-lines-wrap"><div class="vas-dup-lines-title">' + lbl("VAS_062_LineItems", 'Line items') + '</div><div class="vas-dup-sk vas-dup-sk-lines"></div></div>' +
                '<div class="vas-dup-card-total"><span class="lbl">' + lbl("VAS_062_GrandTotal", 'Grand total') + '</span><span class="vas-dup-sk vas-dup-sk-total"></span></div>' +
                '</div>';
        }

        /* One skeleton per invoice in the set (count is known before the fetch). */
        function buildSkeletonCards(count) {
            var n = count > 0 ? count : 2;
            var html = '';
            for (var i = 0; i < n; i++) { html += buildSkeletonCard(); }
            return html;
        }

        /* Panel shell — pre-filled with one skeleton card per invoice so the grid is already at its
           final shape; the line-level detail is fetched lazily on first show. */
        function buildPanel(i, count) {
            return '<div class="vas-dup-panel' + (i === 0 ? ' is-active' : '') + '" role="tabpanel" id="vas-dup-panel-' + i + '"' + (i === 0 ? '' : ' hidden') + '>' +
                '<div class="vas-dup-grid">' + buildSkeletonCards(count) + '</div></div>';
        }

        function buildReviewDialog(sets) {
            var boxSvg = '<svg viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2" stroke-linecap="round" stroke-linejoin="round"><rect x="8" y="8" width="12" height="12" rx="2"/><path d="M16 8V6a2 2 0 0 0-2-2H6a2 2 0 0 0-2 2v8a2 2 0 0 0 2 2h2"/></svg>';
            var closeSvg = '<svg viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="1.8" stroke-linecap="round" stroke-linejoin="round"><line x1="18" y1="6" x2="6" y2="18"/><line x1="6" y1="6" x2="18" y2="18"/></svg>';
            var infoSvg = '<svg viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2" stroke-linecap="round" stroke-linejoin="round"><circle cx="12" cy="12" r="10"/><line x1="12" y1="8" x2="12" y2="12"/><line x1="12" y1="16" x2="12.01" y2="16"/></svg>';

            var tabsHtml = '', panelsHtml = '';
            $.each(sets, function (i, set) {
                tabsHtml += '<button class="vas-dup-tab" type="button" role="tab" aria-selected="' + (i === 0 ? 'true' : 'false') + '">' +
                    '<span class="vas-dup-tab-dot"></span> ' + escapeHtml(set.customer || '—') +
                    ' <span class="vas-dup-tab-badge">' + set.invoices.length + '</span></button>';
                panelsHtml += buildPanel(i, set.invoices.length);
            });

            var subtitle = sets.length + ' ' + lbl("VAS_062_DuplicateSets", 'duplicate sets') + ' · ' + lbl("VAS_062_SwitchTabsReview", 'switch tabs to review each');

            var overlay = $(
                '<div class="vas-dup-overlay" role="dialog" aria-modal="true">' +
                '<div class="vas-dup-dialog">' +
                '<div class="vas-dup-header">' +
                '<div class="vas-dup-header-left">' +
                '<span class="vas-dup-header-icon">' + boxSvg + '</span>' +
                '<div class="vas-dup-title-wrap">' +
                '<div class="vas-dup-title">' + lbl("VAS_062_DuplicateInvoiceReview", 'Duplicate Invoice Review') + '</div>' +
                '<div class="vas-dup-subtitle">' + subtitle + '</div>' +
                '</div></div>' +
                '<div class="vas-dup-header-right">' +
                '<button class="vas-dup-close" type="button" aria-label="' + escapeHtml(lbl("VAS_Close", 'Close')) + '">' + closeSvg + '</button>' +
                '</div></div>' +
                '<div class="vas-dup-tabs" role="tablist">' + tabsHtml + '</div>' +
                '<div class="vas-dup-legend">' +
                '<span class="vas-dup-legend-item"><span class="vas-dup-legend-dot match"></span> ' + lbl("VAS_062_MatchingField", 'Matching field') + '</span>' +
                '<span class="vas-dup-legend-item"><span class="vas-dup-legend-dot diff"></span> ' + lbl("VAS_062_DifferingField", 'Differing field') + '</span>' +
                '<span class="vas-dup-legend-context"></span>' +
                '</div>' +
                '<div class="vas-dup-body">' + panelsHtml + '</div>' +
                '<div class="vas-dup-footer">' +
                '<div class="vas-dup-footer-note">' + infoSvg + '<span></span></div>' +
                '<div class="vas-dup-actions">' +
                '<button class="vas-dup-btn danger" type="button" data-dup-act="void">' + lbl("VAS_062_ReverseNewerInvoice", 'Reverse Recent Invoice') + '</button>' +
                '<button class="vas-dup-btn primary" type="button" data-dup-act="keep">' + lbl("VAS_062_KeepBoth", 'Keep All') + '</button>' +
                '</div></div>' +
                /* Busy overlay shown while a footer action (Keep both / Reverse) is in flight; reuses
                   the core spinner classes. */
                '<div class="vas-dup-busy" style="visibility:hidden;"><div class="vis-busyindicatorinnerwrap"><i class="vis_widgetloader"></i></div></div>' +
                '</div></div>'
            );

            var $tabs = overlay.find('.vas-dup-tab');
            var $panels = overlay.find('.vas-dup-panel');
            var $ctx = overlay.find('.vas-dup-legend-context');
            var $note = overlay.find('.vas-dup-footer-note span');
            var $subtitle = overlay.find('.vas-dup-subtitle');
            /* Index of the duplicate set whose tab is currently shown; the footer actions
               (Reverse newer / Keep both) always operate on this set. */
            var currentIdx = 0;

            function applyMeta(i) {
                var s = sets[i];
                if (!s) return;
                $ctx.html(escapeHtml(s.customer || '') + ' · <b>' + lbl("VAS_062_PotentialDup", 'potential') + ' ' + billWord(s.invoices.length) + ' ' + money(s.amount, s.curSymbol) + '</b>');
                $note.text(lbl("VAS_062_DupFooterNote", 'Header amounts match — review the invoices before deciding.'));
            }

            /* Lazily fetch + render a set's line-level detail the first time its tab is shown. */
            function ensurePanel(i) {
                var set = sets[i];
                var $panel = $panels.eq(i);
                if (!set || !$panel.length || $panel.attr('data-rendered') === '1') return;
                var $grid = $panel.find('.vas-dup-grid');
                if (set.loaded) {
                    $grid.html(renderDetailCards(set));
                    $panel.attr('data-rendered', '1');
                    return;
                }
                /* Skeleton cards are already showing (buildPanel) — leave them until the data lands. */
                fetchDetail(set, function (ok) {
                    /* The dialog may have been closed before the response arrived. */
                    if (!$review || $panel.closest('body').length === 0) return;
                    $grid.html(ok ? renderDetailCards(set) : '<div class="vas-dup-lines-empty">' + escapeHtml(lbl("VAS_062_NoData", 'No data')) + '</div>');
                    $panel.attr('data-rendered', '1');
                });
            }

            function selectTab(i) {
                currentIdx = i;
                $tabs.each(function (idx) { $(this).attr('aria-selected', idx === i ? 'true' : 'false'); });
                $panels.each(function (idx) {
                    var on = idx === i;
                    $(this).toggleClass('is-active', on);
                    if (on) { $(this).removeAttr('hidden'); } else { $(this).attr('hidden', ''); }
                });
                applyMeta(i);
                ensurePanel(i);
                var body = overlay.find('.vas-dup-body')[0];
                if (body) body.scrollTop = 0;
            }

            /* While a footer action is in flight: show the dialog spinner and disable both buttons
               (prevents double-submit). */
            function setBusy(busy) {
                var el = overlay.find('.vas-dup-busy')[0];
                if (el) { el.style.visibility = busy ? 'visible' : 'hidden'; }
                overlay.find('.vas-dup-btn').prop('disabled', !!busy);
            }

            /* Show a backend error message (raw text) using the core dialog. The ("", false, text)
               form renders the literal text rather than treating it as a message key. */
            function showError(data) {
                var txt = (data && data.error) ? data.error : lbl("VAS_062_ErrorOccurred", 'An error occurred.');
                VIS.ADialog.error("", false, txt, "");
            }

            /* Keep the header subtitle's set count in sync after a tab is removed. */
            function updateSubtitle() {
                if ($subtitle && $subtitle.length) {
                    $subtitle.text(sets.length + ' ' + lbl("VAS_062_DuplicateSets", 'duplicate sets') + ' · ' + lbl("VAS_062_SwitchTabsReview", 'switch tabs to review each'));
                }
            }

            /* After a successful action on the current set, drop just that tab + panel (and its data)
               and stay on an adjacent set; close the dialog only when no sets remain. The cached
               $tabs/$panels collections are re-queried so positional indexing stays aligned with sets[]. */
            function removeCurrentTab() {
                var i = currentIdx;
                sets.splice(i, 1);
                $tabs.eq(i).remove();
                $panels.eq(i).remove();
                $tabs = overlay.find('.vas-dup-tab');
                $panels = overlay.find('.vas-dup-panel');
                if (sets.length === 0) {
                    closeReview();
                    return;
                }
                updateSubtitle();
                selectTab(i < sets.length ? i : sets.length - 1);
            }

            /* The newer invoice of a set is the one the review detail tagged panelRole 'Newer'
               (Newest_Rank=1). Returns 0 until the set's detail has been fetched. */
            function newerInvoiceId(set) {
                var detail = set && set.detail;
                if (!detail) { return 0; }
                for (var k = 0; k < detail.length; k++) {
                    if (detail[k].panelRole === 'Newer') { return detail[k].id; }
                }
                return 0;
            }

            /* "Keep both" — flag every invoice in the set as not-a-duplicate; the set then drops
               out of GetDuplicates, so reloading clears the banner. */
            function doKeepBoth(set) {
                if (!set || !set.invoiceIds || set.invoiceIds.length === 0) { return; }
                setBusy(true);
                $.ajax({
                    url: VIS.Application.contextUrl + 'Invoices/KeepBothInvoices',
                    type: 'POST',
                    data: { invoiceIds: set.invoiceIds.join(',') },
                    success: function (res) {
                        var data = parseResponse(res);
                        setBusy(false);
                        if (!data || data.error) { showError(data); return; }
                        /* Keep the dialog open — just drop this set's tab — and refresh the banner. */
                        removeCurrentTab();
                        loadDuplicates();
                    },
                    error: function () { setBusy(false); showError(null); }
                });
            }

            /* Post the Reverse-Correct request for the set's newer invoice, then refresh. */
            function reverseNewer(set) {
                var invId = newerInvoiceId(set);
                if (!invId) { showError(null); return; }
                setBusy(true);
                $.ajax({
                    url: VIS.Application.contextUrl + 'Invoices/ReverseNewerInvoice',
                    type: 'POST',
                    data: { cInvoiceId: invId },
                    success: function (res) {
                        var data = parseResponse(res);
                        setBusy(false);
                        if (!data || data.error) { showError(data); return; }
                        /* Keep the dialog open — just drop this set's tab. The reversed invoice changes
                           status (RE), so refresh both the duplicate banner and the attention list. */
                        removeCurrentTab();
                        loadDuplicates();
                        loadAttention();
                    },
                    error: function () { setBusy(false); showError(null); }
                });
            }

            /* "Reverse Recent Invoice" — confirm first (a reversal is hard to undo), then run the
               document Reverse-Correct action. The set's line detail carries the 'Newer' marker, so
               fetch it first if the tab's detail has not loaded yet. */
            function doReverseNewer(set) {
                if (!set) { return; }
                VIS.ADialog.confirm("VAS_062_ReverseNewerInvoice", true, "", lbl("VAS_062_Confirm", 'Confirm'), function (ok) {
                    if (!ok) { return; }
                    if (set.loaded) { reverseNewer(set); return; }
                    setBusy(true);
                    fetchDetail(set, function (fetched) {
                        setBusy(false);
                        if (!fetched) { showError(null); return; }
                        reverseNewer(set);
                    });
                });
            }

            applyMeta(0);
            ensurePanel(0);

            overlay.on('click', '.vas-dup-tab', function () { selectTab($tabs.index(this)); });

            /* Footer actions operate on the currently selected duplicate set. */
            overlay.on('click', '[data-dup-act]', function () {
                var act = $(this).attr('data-dup-act');
                var set = sets[currentIdx];
                if (act === 'keep') { doKeepBoth(set); }
                else if (act === 'void') { doReverseNewer(set); }
            });

            /* X and backdrop dismiss the dialog. */
            overlay.on('click', '.vas-dup-close', function () { closeReview(); });
            overlay.on('click', function (e) { if (e.target === overlay[0]) closeReview(); });

            return overlay;
        }

        function openReview() {
            if (!reviewSets || reviewSets.length === 0) return;
            closeReview();
            $review = buildReviewDialog(reviewSets);
            $('body').append($review);
            /* Force a reflow so the open transition runs, then reveal. */
            if ($review[0]) { void $review[0].offsetHeight; }
            $review.addClass('is-open');
            $(document).on('keydown.vasInvDup', function (e) {
                if (e.key === 'Escape' || e.keyCode === 27) closeReview();
            });
        }

        function closeReview() {
            $(document).off('keydown.vasInvDup');
            if ($review) {
                $review.remove();
                $review = null;
            }
        }

        /* ── Refresh ── */
        this.refreshWidget = function () {
            INVOICES = [];
            pageNo = 1;
            loadAttention();
            loadDuplicates();
        };

        /* ── Root accessor ── */
        this.getRoot = function () {
            return $root;
        };

        this.disposeComponent = function () {
            if (bodyObserver) { bodyObserver.disconnect(); bodyObserver = null; }
            closeReview();
            $root.remove();
        };
    };

    VIS.InvoicesWidget.prototype.refreshWidget = function () {
        this.refreshWidget();
    };

    /* Relay a fired value (e.g. open-in-new-mode params) to the registered widget host. */
    VIS.InvoicesWidget.prototype.widgetFirevalueChanged = function (value) {
        if (this.listener)
            this.listener.widgetFirevalueChanged(value);
    };

    /* The widget host registers itself here so the widget can drive the host (Scenario 1). */
    VIS.InvoicesWidget.prototype.addChangeListener = function (listener) {
        this.listener = listener;
    };

    VIS.InvoicesWidget.prototype.init = function (windowNo, frame) {
        this.frame = frame;
        this.AD_UserHomeWidgetID = frame.widgetInfo.AD_UserHomeWidgetID;
        this.windowNo = windowNo;
        this.Initalize();
        this.frame.getContentGrid().append(this.getRoot());

        /* Self-wire the dashboard-width CSS variable the title clamp reads. */
        ensureDashInlineSizeVar(this.getRoot());
    };

    VIS.InvoicesWidget.prototype.widgetSizeChange = function (height, width) { };

    VIS.InvoicesWidget.prototype.dispose = function () {
        this.disposeComponent();
        if (this.frame)
            this.frame.dispose();
        this.frame = null;
    };

})(VIS, jQuery);
