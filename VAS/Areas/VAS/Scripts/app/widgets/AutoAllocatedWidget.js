/**
 * Auto Allocated Widget
 * Purpose - KPI card showing percentage of AR receipts (current month) that
 *           are matched/allocated to invoices. Clicking the card opens a
 *           modal dialog listing every receipt with Date, Receipt No.,
 *           Customer, Bank Account, Payment Currency, Amount (in payment
 *           currency, no conversion), and a Matched flag.
 * Design   - Per design.md / dashboard-widgets.md §"KPI And Summary Widget":
 *            glass KPI shell on the em-based Widget Root Anchor
 *            (clamp(16px, --dash-inline-size*0.012, 20px)) — clamped icon
 *            well, Regular #102C3F title-label, 1.75em SemiBold (600)
 *            success-green percentage, and an xs muted "Receipt Match to
 *            Invoice" meta line.
 *
 * ── Labels / Message Keys (VAS_ prefix) ───────────────────────────────
 *  #  | Current Text                              | Message Key
 * ----+-------------------------------------------+----------------------------
 *  1  | Auto-allocated                            | VAS_AutoAllocated
 *  2  | Receipt allocation                        | VAS_ReceiptAllocation
 *  3  | Allocated vs unallocated receipts in      | VAS_AllocatedVsUnallocated
 *     | the last 30 days                          |
 *  4  | View                                      | VAS_View
 *  5  | Receipt Match to Invoice                  | VAS_ReceiptMatchToInvoice
 *  6  | Allocated                                 | VAS_Allocated
 *  7  | Unallocated                               | VAS_Unallocated
 *  8  | Date                                      | VAS_Date
 *  9  | Receipt No.                               | VAS_ReceiptNo
 * 10  | Customer                                  | VAS_Customer
 * 11  | Bank account                              | VAS_BankAccount
 * 12  | Payment Currency                          | VAS_PaymentCurrency
 * 13  | Amount                                    | VAS_Amount
 * 14  | Close                                     | VAS_Close
 * 15  | Loading…                                  | VAS_Loading
 * 16  | No receipts in this period                | VAS_NoReceiptsThisPeriod
 * 17  | receipt / receipts                        | VAS_Receipt / VAS_Receipts
 * 18  | Showing                                   | VAS_Showing
 * 19  | of                                        | VAS_Of
 * 20  | Previous                                  | VAS_Previous
 * 21  | Next                                      | VAS_Next
 * ─────────────────────────────────────────────────────────────────────
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

    VIS.AutoAllocatedWidget = function () {

        this.frame;
        this.windowNo;

        var $root = $('<div class="vas-aa-root">');
        var $metricEl;
        var $busy;
        var $dialog;
        var $dialogTbody;
        var $dialogSubtitle;
        var $dialogBusy;
        var $pagerHelper;
        var $pagerPrev;
        var $pagerNext;
        var $pagerText;
        var $tabAllocated;
        var $tabUnallocated;
        var $tabAllocatedCount;
        var $tabUnallocatedCount;

        var lastPeriod = "";
        var rowsLoading = false;

        /* Each tab keeps its own paging state so the user can switch back and
           still see the page they were on. activeFilter selects which tab is
           visible. */
        var pageSize = 10;
        var activeFilter = "allocated";
        var tabState = {
            allocated:   { pageNo: 1, totalPages: 0, totalRecords: 0, loaded: false },
            unallocated: { pageNo: 1, totalPages: 0, totalRecords: 0, loaded: false }
        };

        function lbl(key, fallback) {
            var t = VIS.Msg.getMsg(key);
            return (t && t.charAt(0) !== '[') ? t : fallback;
        }

        function showBusy(show) {
            if (!$busy || !$busy[0]) { return; }
            $busy[0].style.visibility = show ? 'visible' : 'hidden';
        }

        function showDialogBusy(show) {
            if (!$dialogBusy || !$dialogBusy[0]) { return; }
            $dialogBusy[0].style.visibility = show ? 'visible' : 'hidden';
        }

        function escapeHtml(value) {
            return String(value == null ? "" : value)
                .replace(/&/g, "&amp;")
                .replace(/</g, "&lt;")
                .replace(/>/g, "&gt;")
                .replace(/"/g, "&quot;")
                .replace(/'/g, "&#039;");
        }

        this.Initalize = function () {
            createWidget();
            loadKpi();
        };

        function loadKpi() {
            showBusy(true);

            $.ajax({
                url: VIS.Application.contextUrl + 'AutoAllocated/GetAutoAllocated',
                type: 'GET',
                cache: false,
                success: function (res) {
                    var data = typeof res === 'string' ? JSON.parse(res) : res;

                    if (data && typeof data === 'string') {
                        data = JSON.parse(data);
                    }

                    if (data && data.error) {
                        setNoData();
                        return;
                    }

                    renderMetric(data);
                },
                error: function () {
                    setNoData();
                },
                complete: function () { showBusy(false); }
            });
        }

        function loadRows() {
            if (!$dialogTbody) { return; }

            var filterAtRequest = activeFilter;
            var state = tabState[filterAtRequest];

            rowsLoading = true;
            showDialogBusy(true);
            if ($pagerPrev) { $pagerPrev.prop("disabled", true); }
            if ($pagerNext) { $pagerNext.prop("disabled", true); }

            $.ajax({
                url: VIS.Application.contextUrl + 'AutoAllocated/GetAutoAllocatedRows',
                type: 'GET',
                cache: false,
                data: {
                    pageNo: state.pageNo,
                    pageSize: pageSize,
                    filter: filterAtRequest
                },
                success: function (res) {
                    var data = typeof res === 'string' ? JSON.parse(res) : res;

                    if (data && typeof data === 'string') {
                        data = JSON.parse(data);
                    }

                    /* Drop the response if the user has already switched tabs —
                       its rows belong to a tab that is no longer visible. */
                    if (filterAtRequest !== activeFilter) {
                        return;
                    }

                    rowsLoading = false;
                    showDialogBusy(false);

                    if (data && data.error) {
                        renderPageResult(filterAtRequest, { rows: [], totalRecords: 0, totalPages: 0, pageNo: state.pageNo });
                        return;
                    }

                    renderPageResult(filterAtRequest, data || {});
                    tabState[filterAtRequest].loaded = true;
                },
                error: function () {
                    if (filterAtRequest !== activeFilter) { return; }
                    rowsLoading = false;
                    showDialogBusy(false);
                    renderPageResult(filterAtRequest, { rows: [], totalRecords: 0, totalPages: 0, pageNo: state.pageNo });
                }
            });
        }

        function renderPageResult(filter, data) {
            var state = tabState[filter];
            var rows = (data && data.rows) ? data.rows : [];

            state.totalRecords = Number(data && data.totalRecords || 0);
            state.totalPages = Number(data && data.totalPages || 0);

            if (data && typeof data.pageNo !== "undefined") {
                state.pageNo = Number(data.pageNo);
            }

            if (state.pageNo > state.totalPages && state.totalPages > 0) { state.pageNo = state.totalPages; }
            if (state.pageNo < 1) { state.pageNo = 1; }

            renderRows(rows);

            var from = state.totalRecords === 0 ? 0 : (state.pageNo - 1) * pageSize;
            var to = Math.min(from + rows.length, state.totalRecords);

            updatePagerControls(from, to);
        }

        function setNoData() {
            if ($metricEl) {
                $metricEl.text("0%");
            }
        }

        function formatPercent(value) {
            var absVal = Number(value || 0);
            var stdPrecision = 2;

            try {
                if (VIS.Env && VIS.Env.getCtx && VIS.Env.getCtx().getStdPrecision) {
                    stdPrecision = VIS.Env.getCtx().getStdPrecision();
                }
            }
            catch (e) {
                stdPrecision = 2;
            }

            return absVal.toLocaleString(window.navigator.language, {
                minimumFractionDigits: stdPrecision,
                maximumFractionDigits: stdPrecision
            }) + "%";
        }

        /* formatExactAmount returns { sign, magnitude } where sign is '-' for
           negative values or '' (empty) for positive/zero, and magnitude is the
           absolute value formatted to stdPrecision digits.
           Call sites prepend sign BEFORE the currency symbol so the result reads
           "$1,000.00" / "-$1,000.00". */
        function formatExactAmount(value) {
            var num = Number(value || 0);
            var stdPrecision = 2;

            try {
                if (VIS.Env && VIS.Env.getCtx && VIS.Env.getCtx().getStdPrecision) {
                    stdPrecision = VIS.Env.getCtx().getStdPrecision();
                }
            }
            catch (e) {
                stdPrecision = 2;
            }

            var sign = num < 0 ? '-' : '';
            var magnitude = Number(Math.abs(num)).toLocaleString(window.navigator.language, {
                minimumFractionDigits: stdPrecision,
                maximumFractionDigits: stdPrecision
            });

            return { sign: sign, magnitude: magnitude };
        }

        function renderMetric(data) {
            var percent = (data && data.autoAllocatedPercent) || 0;
            lastPeriod = (data && data.period) || "";

            if ($metricEl) {
                $metricEl.text(formatPercent(percent));
            }

            if ($dialogSubtitle) {
                $dialogSubtitle.text(lbl("VAS_AllocatedVsUnallocated", "Allocated vs unallocated receipts in the last 30 days"));
            }

            /* Populate tab counts from the KPI response so the user sees them
               without having to switch tabs to trigger a load. */
            var matched = Number(data && data.matchedReceipts || 0);
            var unmatched = Number(data && data.unmatchedReceipts || 0);
            tabState.allocated.totalRecords = matched;
            tabState.unallocated.totalRecords = unmatched;
            tabState.allocated.totalPages = pageSize === 0 ? 0 : Math.ceil(matched / pageSize);
            tabState.unallocated.totalPages = pageSize === 0 ? 0 : Math.ceil(unmatched / pageSize);
            updateTabCounts();

            if ($dialog && $dialog.is(':visible')) {
                loadRows();
            }
        }

        function updateTabCounts() {
            if ($tabAllocatedCount) {
                $tabAllocatedCount.text(tabState.allocated.totalRecords);
            }
            if ($tabUnallocatedCount) {
                $tabUnallocatedCount.text(tabState.unallocated.totalRecords);
            }
        }

        function switchTab(filter) {
            if (filter !== "allocated" && filter !== "unallocated") { return; }
            if (filter === activeFilter) { return; }

            activeFilter = filter;
            updateActiveTabStyles();
            loadRows();
        }

        function updateActiveTabStyles() {
            if (!$tabAllocated || !$tabUnallocated) { return; }

            $tabAllocated.toggleClass("vas-aa-tab-active", activeFilter === "allocated");
            $tabAllocated.attr("aria-selected", activeFilter === "allocated" ? "true" : "false");

            $tabUnallocated.toggleClass("vas-aa-tab-active", activeFilter === "unallocated");
            $tabUnallocated.attr("aria-selected", activeFilter === "unallocated" ? "true" : "false");
        }

        function formatDate(value) {
            if (!value) { return ""; }
            var d = new Date(value);
            if (isNaN(d.getTime())) { return value; }
            return d.toLocaleDateString(window.navigator.language, {
                day: "2-digit",
                month: "short",
                year: "numeric"
            });
        }

        /* Render bank info as "<Bank Name> · ****<last-4 of account>" when both
           parts are known; degrade gracefully when one is missing. */
        function formatBankAccount(row) {
            var bankName = (row && row.bankName) ? String(row.bankName).trim() : "";
            var accountNo = (row && row.accountNo) ? String(row.accountNo).trim() : "";

            var last4 = "";
            if (accountNo) {
                last4 = accountNo.length > 4 ? accountNo.slice(-4) : accountNo;
            }

            if (bankName && last4) {
                return bankName + ' · ****' + last4;
            }

            if (bankName) { return bankName; }
            if (last4) { return '****' + last4; }
            return "";
        }

        function renderRows(rows) {
            if (!$dialogTbody) { return; }
            $dialogTbody.empty();

            if (!rows || rows.length === 0) {
                $dialogTbody.html(
                    '<tr><td class="vas-aa-dialog-empty" colspan="6">' +
                    lbl("VAS_NoReceiptsThisPeriod", "No receipts in this period") +
                    '</td></tr>'
                );
                return;
            }

            for (var i = 0; i < rows.length; i++) {
                var row = rows[i];
                var dateText = formatDate(row.date);
                var docNo = row.documentNo || "";
                var customer = row.customer || "";
                var bankText = formatBankAccount(row);
                var currencyCode = row.paymentCurrency || "";
                var sym = row.paymentCurrencySymbol || currencyCode || "";
                var amountParts = formatExactAmount(row.amount);
                var amountSign = amountParts.sign;
                var amountMagnitude = amountParts.magnitude;
                /* Amount keeps its payment-currency symbol inline — no
                   conversion to base currency, per spec. Sign precedes the
                   symbol: "+$1,000.00" / "-$1,000.00". */
                var amountHtml = escapeHtml(amountSign) +
                    (sym ? '<span class="vas-aa-cur-inline">' + escapeHtml(sym) + '</span>' : '') +
                    escapeHtml(amountMagnitude);

                /* The Matched column was dropped — the active tab itself
                   indicates the allocated/unallocated state. */
                var $tr = $(
                    '<tr>' +
                    '<td class="vas-aa-td-date" title="' + escapeHtml(dateText) + '">' + escapeHtml(dateText) + '</td>' +
                    '<td class="vas-aa-td-doc" title="' + escapeHtml(docNo) + '">' +
                    '<span class="vas-aa-truncate">' + escapeHtml(docNo) + '</span>' +
                    '</td>' +
                    '<td class="vas-aa-td-customer" title="' + escapeHtml(customer) + '">' +
                    '<span class="vas-aa-truncate">' + escapeHtml(customer) + '</span>' +
                    '</td>' +
                    '<td class="vas-aa-td-bank" title="' + escapeHtml(bankText) + '">' +
                    '<span class="vas-aa-truncate">' + escapeHtml(bankText) + '</span>' +
                    '</td>' +
                    '<td class="vas-aa-td-currency" title="' + escapeHtml(currencyCode) + '">' + escapeHtml(currencyCode) + '</td>' +
                    '<td class="vas-aa-td-amount" title="' + escapeHtml(amountSign + (sym ? sym : '') + amountMagnitude) + '">' + amountHtml + '</td>' +
                    '</tr>'
                );

                $dialogTbody.append($tr);
            }
        }

        function updatePagerControls(from, to) {
            var state = tabState[activeFilter];

            /* Keep the tab count chip in sync — server returned the
               authoritative count for this tab. */
            updateTabCounts();

            if ($pagerHelper) {
                if (state.totalRecords > 0) {
                    var rcptLabel = state.totalRecords === 1
                        ? lbl("VAS_Receipt", "receipt")
                        : lbl("VAS_Receipts", "receipts");

                    $pagerHelper.text(
                        lbl("VAS_Showing", "Showing") + ' ' +
                        (from + 1) + '–' + to + ' ' +
                        lbl("VAS_Of", "of") + ' ' + state.totalRecords + ' ' + rcptLabel
                    );
                }
                else {
                    $pagerHelper.text("");
                }
            }

            if ($pagerText) {
                $pagerText.text(state.totalPages > 0 ? (state.pageNo + ' ' + lbl("VAS_Of", "of") + ' ' + state.totalPages) : "");
            }

            if ($pagerPrev) {
                $pagerPrev.prop("disabled", rowsLoading || state.pageNo <= 1);
            }

            if ($pagerNext) {
                $pagerNext.prop("disabled", rowsLoading || state.totalPages <= 1 || state.pageNo >= state.totalPages);
            }
        }

        function openDialog() {
            if (!$dialog) { return; }
            $dialog.show();
            $('body').addClass('vas-aa-body-lock');

            if (!tabState[activeFilter].loaded) {
                loadRows();
            }
        }

        function closeDialog() {
            if (!$dialog) { return; }
            $dialog.hide();
            $('body').removeClass('vas-aa-body-lock');

            /* Reset both tabs' paging state so the next openDialog() starts at
               page 1 of the default (allocated) tab with a fresh fetch. */
            activeFilter = "allocated";
            tabState.allocated.pageNo = 1;
            tabState.allocated.loaded = false;
            tabState.unallocated.pageNo = 1;
            tabState.unallocated.loaded = false;
            updateActiveTabStyles();
        }

        function createDialog() {
            $dialog = $(
                '<div class="vas-aa-dialog" style="display:none;" role="dialog" aria-modal="true">' +
                '<div class="vas-aa-dialog-scrim"></div>' +
                '<div class="vas-aa-dialog-card">' +

                '<div class="vas-aa-dialog-header">' +
                '<div class="vas-aa-dialog-icon">' +
                '<svg viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="1.8" stroke-linecap="round" stroke-linejoin="round">' +
                '<path d="M9 11l3 3L22 4"/>' +
                '<path d="M21 12v7a2 2 0 0 1-2 2H5a2 2 0 0 1-2-2V5a2 2 0 0 1 2-2h11"/>' +
                '</svg>' +
                '</div>' +
                '<div class="vas-aa-dialog-title-group">' +
                '<div class="vas-aa-dialog-title">' + lbl("VAS_ReceiptAllocation", "Receipt allocation") + '</div>' +
                '<div class="vas-aa-dialog-subtitle">' + lbl("VAS_AllocatedVsUnallocated", "Allocated vs unallocated receipts in the last 30 days") + '</div>' +
                '</div>' +
                '<button type="button" class="vas-aa-dialog-close" aria-label="' + escapeHtml(lbl("VAS_Close", "Close")) + '">' +
                '<svg viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="1.8" stroke-linecap="round" stroke-linejoin="round"><line x1="18" y1="6" x2="6" y2="18"/><line x1="6" y1="6" x2="18" y2="18"/></svg>' +
                '</button>' +
                '</div>' +

                /* Tab bar — two tabs with live counts; active one underlined. */
                '<div class="vas-aa-tabs" role="tablist">' +
                '<button type="button" class="vas-aa-tab vas-aa-tab-allocated vas-aa-tab-active" role="tab" aria-selected="true">' +
                '<span class="vas-aa-tab-label">' + lbl("VAS_Allocated", "Allocated") + '</span>' +
                '<span class="vas-aa-tab-count vas-aa-tab-count-allocated">0</span>' +
                '</button>' +
                '<button type="button" class="vas-aa-tab vas-aa-tab-unallocated" role="tab" aria-selected="false">' +
                '<span class="vas-aa-tab-label">' + lbl("VAS_Unallocated", "Unallocated") + '</span>' +
                '<span class="vas-aa-tab-count vas-aa-tab-count-unallocated">0</span>' +
                '</button>' +
                '</div>' +

                '<div class="vas-aa-dialog-body">' +
                '<div class="vas-aa-dialog-busy"><div class="vis-busyindicatorinnerwrap"><i class="vis_widgetloader"></i></div></div>' +
                '<table class="vas-aa-dialog-table">' +
                '<thead>' +
                '<tr>' +
                '<th class="vas-aa-th-date" title="' + escapeHtml(lbl("VAS_Date", "Date")) + '">' + lbl("VAS_Date", "Date") + '</th>' +
                '<th class="vas-aa-th-doc" title="' + escapeHtml(lbl("VAS_ReceiptNo", "Receipt No.")) + '">' + lbl("VAS_ReceiptNo", "Receipt No.") + '</th>' +
                '<th class="vas-aa-th-customer" title="' + escapeHtml(lbl("VAS_Customer", "Customer")) + '">' + lbl("VAS_Customer", "Customer") + '</th>' +
                '<th class="vas-aa-th-bank" title="' + escapeHtml(lbl("VAS_BankAccount", "Bank account")) + '">' + lbl("VAS_BankAccount", "Bank account") + '</th>' +
                '<th class="vas-aa-th-currency" title="' + escapeHtml(lbl("VAS_PaymentCurrency", "Payment Currency")) + '">' + lbl("VAS_PaymentCurrency", "Payment Currency") + '</th>' +
                '<th class="vas-aa-th-amount" title="' + escapeHtml(lbl("VAS_Amount", "Amount")) + '">' + lbl("VAS_Amount", "Amount") + '</th>' +
                '</tr>' +
                '</thead>' +
                '<tbody class="vas-aa-dialog-tbody"></tbody>' +
                '</table>' +
                '</div>' +

                '<div class="vas-aa-dialog-footer">' +
                '<span class="vas-aa-pager-helper"></span>' +
                '<div class="vas-aa-pager">' +
                '<button type="button" class="vas-aa-pager-btn vas-aa-pager-prev" aria-label="' + escapeHtml(lbl("VAS_Previous", "Previous")) + '">' +
                '<svg viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2" stroke-linecap="round" stroke-linejoin="round"><polyline points="15 18 9 12 15 6"/></svg>' +
                '</button>' +
                '<span class="vas-aa-pager-text"></span>' +
                '<button type="button" class="vas-aa-pager-btn vas-aa-pager-next" aria-label="' + escapeHtml(lbl("VAS_Next", "Next")) + '">' +
                '<svg viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2" stroke-linecap="round" stroke-linejoin="round"><polyline points="9 18 15 12 9 6"/></svg>' +
                '</button>' +
                '</div>' +
                '</div>' +

                '</div>' +
                '</div>'
            );

            $dialogTbody = $dialog.find('.vas-aa-dialog-tbody');
            $dialogSubtitle = $dialog.find('.vas-aa-dialog-subtitle');
            $dialogBusy = $dialog.find('.vas-aa-dialog-busy');
            $dialogBusy[0].style.visibility = 'hidden';
            $pagerHelper = $dialog.find('.vas-aa-pager-helper');
            $pagerPrev = $dialog.find('.vas-aa-pager-prev');
            $pagerNext = $dialog.find('.vas-aa-pager-next');
            $pagerText = $dialog.find('.vas-aa-pager-text');

            $tabAllocated = $dialog.find('.vas-aa-tab-allocated');
            $tabUnallocated = $dialog.find('.vas-aa-tab-unallocated');
            $tabAllocatedCount = $dialog.find('.vas-aa-tab-count-allocated');
            $tabUnallocatedCount = $dialog.find('.vas-aa-tab-count-unallocated');

            $dialog.find('.vas-aa-dialog-close').on('click', function (e) {
                e.stopPropagation();
                closeDialog();
            });

            $dialog.find('.vas-aa-dialog-scrim').on('click', function () {
                closeDialog();
            });

            $tabAllocated.on('click', function () { switchTab("allocated"); });
            $tabUnallocated.on('click', function () { switchTab("unallocated"); });

            $pagerPrev.on('click', function () {
                var state = tabState[activeFilter];
                if (rowsLoading || state.pageNo <= 1) { return; }
                state.pageNo--;
                loadRows();
            });

            $pagerNext.on('click', function () {
                var state = tabState[activeFilter];
                if (rowsLoading || state.pageNo >= state.totalPages) { return; }
                state.pageNo++;
                loadRows();
            });

            $(document).on('keydown.vas-aa', function (e) {
                if (e.key === 'Escape' && $dialog.is(':visible')) {
                    closeDialog();
                }
            });

            /* Attach to <body> so fixed positioning escapes any transformed
               dashboard ancestor. */
            $('body').append($dialog);
        }

        function createWidget() {
            var $card = $(
                '<div class="vas-aa-card" role="button" tabindex="0">' +

                '<div class="vas-aa-head">' +
                '<div class="vas-aa-head-left">' +
                '<div class="vas-aa-icon">' +
                '<svg viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="1.8" stroke-linecap="round" stroke-linejoin="round">' +
                '<path d="M9 11l3 3L22 4"/>' +
                '<path d="M21 12v7a2 2 0 0 1-2 2H5a2 2 0 0 1-2-2V5a2 2 0 0 1 2-2h11"/>' +
                '</svg>' +
                '</div>' +
                '<span class="vas-aa-label">' + lbl("VAS_AutoAllocated", "Auto-allocated") + '</span>' +
                '</div>' +
                '</div>' +

                '<div class="vas-aa-metric">—</div>' +

                '<span class="vas-aa-detail-text">' +
                lbl("VAS_ReceiptMatchToInvoice", "Receipt Match to Invoice") +
                '</span>' +

                '</div>'
            );

            $metricEl = $card.find('.vas-aa-metric');

            $card.on('click', function () {
                openDialog();
            });

            $card.on('keydown', function (e) {
                if (e.key === 'Enter' || e.key === ' ') {
                    e.preventDefault();
                    openDialog();
                }
            });

            $root.append($card);

            $busy = $('<div class="vas-aa-busy"><div class="vis-busyindicatorinnerwrap"><i class="vis_widgetloader"></i></div></div>');
            $busy[0].style.visibility = 'hidden';
            $root.append($busy);

            createDialog();
        }

        this.refreshWidget = function () {
            tabState.allocated.loaded = false;
            tabState.allocated.pageNo = 1;
            tabState.unallocated.loaded = false;
            tabState.unallocated.pageNo = 1;
            loadKpi();
        };

        this.getRoot = function () {
            return $root;
        };

        this.disposeComponent = function () {
            $(document).off('keydown.vas-aa');
            $('body').removeClass('vas-aa-body-lock');

            if ($dialog) {
                $dialog.remove();
                $dialog = null;
            }

            $root.remove();
        };
    };

    VIS.AutoAllocatedWidget.prototype.init = function (windowNo, frame) {
        this.frame = frame;
        this.AD_UserHomeWidgetID = frame.widgetInfo.AD_UserHomeWidgetID;
        this.windowNo = windowNo;

        this.Initalize();
        this.frame.getContentGrid().append(this.getRoot());

        /* Self-wire the dashboard-width CSS variable the title clamp reads. */
        ensureDashInlineSizeVar(this.getRoot());
    };

    VIS.AutoAllocatedWidget.prototype.widgetSizeChange = function (height, width) { };

    VIS.AutoAllocatedWidget.prototype.refreshWidget = function () {
        this.refreshWidget();
    };

    VIS.AutoAllocatedWidget.prototype.dispose = function () {
        this.disposeComponent();

        if (this.frame) {
            this.frame.dispose();
        }

        this.frame = null;
    };

})(VIS, jQuery);
