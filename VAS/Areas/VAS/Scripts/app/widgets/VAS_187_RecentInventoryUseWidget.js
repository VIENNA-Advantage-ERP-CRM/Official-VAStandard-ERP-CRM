/**
 * VAS_187_RecentInventoryUseWidget
 * 4x2 Recent Issues List Widget for Inventory Use dashboard.
 * Displays real-time operational activity log of recent material issue transactions with status filtering,
 * paginated at 4 items per page, and direct tab opening via widgetFirevalueChanged.
 *
 * Summary Message Table
 *  # | Current Text                           | Message Key
 * ---+----------------------------------------+-----------------------------------
 *  1 | Recent Inventory Use                   | VAS_187_RecentInventoryUse
 *  2 | Latest material issue transactions     | VAS_187_LatestMaterialIssueTxns
 *  3 | All Statuses                           | VAS_187_AllStatuses
 *  4 | Completed                              | VAS_187_Completed
 *  5 | Drafted                                | VAS_187_Drafted
 *  6 | Couldn't load                           | VAS_187_CouldntLoad
 *
 * DocStatus chip labels (see STATUS_LABELS below), one per AD_Ref_List reference 131 value:
 *  7 | Closed                                 | VAS_187_Closed
 *  8 | In Process                             | VAS_187_InProcess
 *  9 | Approved                               | VAS_187_Approved
 * 10 | Not Approved                           | VAS_187_NotApproved
 * 11 | Waiting Confirmation                   | VAS_187_WaitingConfirmation
 * 12 | Waiting Payment                        | VAS_187_WaitingPayment
 * 13 | Invalid                                | VAS_187_Invalid
 * 14 | Reversed                               | VAS_187_Reversed
 * 15 | Voided                                 | VAS_187_Voided
 * 16 | Unknown                                | VAS_187_Unknown
 * 17 | No recent inventory use transactions.  | VAS_187_NoRecentTransactions
 * 18 | of                                     | VAS_187_Of
 * 19 | Warehouse                              | VAS_187_WarehouseFallback
 * 20 | lines                                  | VAS_187_Lines
 * 21 | Filter                                 | VAS_187_Filter
 * 22 | Amount                                 | VAS_187_Amount
 * 23 | Previous                               | VAS_187_Previous
 * 24 | Next                                   | VAS_187_Next
 *
 * NOTE (2026-09-18, Claude): redesigned to match the reference mock - status badges now
 * carry a small tone icon (check/pencil/clock), the meta line gets a calendar icon and
 * drops the org-name segment, the right side is two "Lines"/"Amount" stat columns with a
 * divider (replacing the old single "N lines · qty" line), the status <select> became a
 * "Filter" button that opens a small dropdown menu, and the pager buttons use chevron SVGs.
 * Also fixed in passing: formattedCompactVal/formattedFullVal were already being computed
 * every row but the Amount value rendered formatINR() (full precision) instead - the
 * compact one is now what's shown, with the full value as the title tooltip.
 */
; VAS = window.VAS || {};

; (function (VAS, $) {

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

    VAS.VAS_187_RecentInventoryUseWidget = function () {

        this.frame;
        this.windowNo;

        var $self = this;
        var $root = $('<div class="vas-riu-root">');
        var $card;
        var $body;
        var $footHelper;
        var $pagerText;
        var $prevBtn;
        var $nextBtn;
        var $busy;
        var docClickNs;

        var selectedStatus = "ALL";
        var pageNo = 1;
        var pageSize = 3;
        var totalRecords = 0;
        var totalPages = 1;
        var recordsData = [];

// ===== NEW CODE START — currency format (agent A09, 2026-08-19) =====
        var currencyIso = '';
        var currencySymbol = '';
// ===== NEW CODE END — currency format =====

        function label(key, fallback) {
            return VIS.Msg.getMsg(key);
        }

        function escapeHtml(value) {
            return String(value == null ? "" : value)
                .replace(/&/g, "&amp;")
                .replace(/</g, "&lt;")
                .replace(/>/g, "&gt;")
                .replace(/"/g, "&quot;")
                .replace(/'/g, "&#039;");
        }

        function parseResponse(res) {
            var data = res;
            if (typeof data === 'string') { data = JSON.parse(data); }
            if (typeof data === 'string') { data = JSON.parse(data); }
            return data || {};
        }

        function formatQty(value) {
            var n = Number(value || 0);
            return n.toLocaleString(window.navigator.language);
        }

        // VAS163 2026-11-10: browser locale (6-Nov retest2).
        function formatIsoDate(iso, fallback) {
            var p = /^(\d{4})-(\d{2})-(\d{2})/.exec(String(iso || ''));
            if (!p) { return fallback || ''; }
            return new Date(Number(p[1]), Number(p[2]) - 1, Number(p[3])).toLocaleDateString(window.navigator.language, { day: '2-digit', month: 'short', year: 'numeric' });
        }

// ===== NEW CODE START — currency format (agent A09, 2026-08-19) =====
        /**
         * VAS163 2026-11-12: currency (iso + stdPrecision) from the server, formatting from the
         * browser culture via VAS.formatAmountByBrowser (was "symbol + ' ' + number", fixed 2 dp).
         * Compact form from 1,000 upwards.
         */
        var currencyPrecision;

        function formatCurrencyAmount(val, iso, symbol) {
            var num = parseFloat(val);
            if (isNaN(num)) { num = 0; }
            return VAS.formatAmountByBrowser(num, iso || '', currencyPrecision, { compact: Math.abs(num) >= 1000, symbol: symbol || '' });
        }

        function formatFullCurrency(val, iso, symbol) {
            var num = parseFloat(val);
            if (isNaN(num)) { num = 0; }
            return VAS.formatAmountByBrowser(num, iso || '', currencyPrecision, { symbol: symbol || '' });
        }

        // NOTE (2026-09-17): formatINR is called below but was never defined anywhere in this
        // file - a pre-existing bug that threw ReferenceError while rendering every row. Aliased
        // to the exact-value currency formatter already defined above.
        function formatINR(value) {
            return formatFullCurrency(value, currencyIso, currencySymbol);
        }

        // VAS163 2026-11-12: unused manual Indian grouping replaced by browser-locale grouping (currency precision).
        function formatIndianGrouping(num) {
            return VAS.formatAmountByBrowser(Number(num || 0), '', currencyPrecision);
        }
// ===== NEW CODE END — currency format =====
// ----- OLD CODE (kept for rollback, do not delete) -----
//        function formatINR(value) {
//            var val = Number(value || 0);
//            if (val >= 100000) {
//                return '₹' + (val / 100000).toFixed(1) + 'L';
//            } else if (val >= 1000) {
//                return '₹' + (val / 1000).toFixed(1) + 'k';
//            }
//            return '₹' + val.toLocaleString(window.navigator.language);
//        }
// ----- END OLD CODE -----
        /* DocStatus code -> [css tone, message key, English fallback].
           Codes come from AD_Ref_List reference 131. Previously only CO/CL and DR were translated
           and every other status fell through rendering the RAW CODE ("IP", "RE", "VO", "WC"),
           which was both untranslated and not the label the spec asks for ("In Process", not "IP"). */
        var STATUS_LABELS = {
            'CO': ['co', 'VAS_187_Completed', 'Completed'],
            'CL': ['co', 'VAS_187_Closed', 'Closed'],
            'DR': ['dr', 'VAS_187_Drafted', 'Drafted'],
            'IP': ['ip', 'VAS_187_InProcess', 'In Process'],
            'AP': ['ip', 'VAS_187_Approved', 'Approved'],
            'NA': ['dr', 'VAS_187_NotApproved', 'Not Approved'],
            'WC': ['ip', 'VAS_187_WaitingConfirmation', 'Waiting Confirmation'],
            'WP': ['ip', 'VAS_187_WaitingPayment', 'Waiting Payment'],
            'IN': ['dr', 'VAS_187_Invalid', 'Invalid'],
            'RE': ['dr', 'VAS_187_Reversed', 'Reversed'],
            'VO': ['dr', 'VAS_187_Voided', 'Voided'],
            '??': ['ip', 'VAS_187_Unknown', 'Unknown']
        };

        // One small icon per badge tone (co/dr/ip) rather than per status code - the tone is
        // already the visual grouping (STATUS_LABELS maps every code to one of the three), so
        // this keeps the icon set fixed at three instead of growing per status.
        var STATUS_ICONS = {
            co: '<svg viewBox="0 0 24 24" fill="currentColor"><path d="M12 2C6.48 2 2 6.48 2 12s4.48 10 10 10 10-4.48 10-10S17.52 2 12 2zm-1.999 14.413-3.713-3.713 1.414-1.414 2.299 2.298 5.586-5.586 1.414 1.414-7 7.001z"/></svg>',
            dr: '<svg viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2" stroke-linecap="round" stroke-linejoin="round"><path d="M11 4H4a2 2 0 0 0-2 2v14a2 2 0 0 0 2 2h14a2 2 0 0 0 2-2v-7"></path><path d="M18.5 2.5a2.121 2.121 0 0 1 3 3L12 15l-4 1 1-4 9.5-9.5z"></path></svg>',
            ip: '<svg viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2" stroke-linecap="round" stroke-linejoin="round"><circle cx="12" cy="12" r="10"></circle><polyline points="12 6 12 12 16 14"></polyline></svg>'
        };

        function getStatusBadge(status) {
            var entry = STATUS_LABELS[status];
            var tone = entry ? entry[0] : 'ip';
            // Genuinely unmapped code: show it rather than an empty chip, but keep it visible
            // as an anomaly instead of pretending it is a known status.
            var text = entry ? label(entry[1], entry[2]) : status;
            return '<span class="vas-riu-badge ' + tone + '">' +
                '<span class="vas-riu-badge-ico" aria-hidden="true">' + (STATUS_ICONS[tone] || '') + '</span>' +
                escapeHtml(text) +
                '</span>';
        }

        function showBusy(show) {
            if (!$busy || !$busy[0]) { return; }
            $busy.toggleClass('vas-riu-hidden', !show);
        }

        this.Initalize = function () {
            createWidget();
            setupResizeObserver();
            loadRecentIssues();
        };

        function setupResizeObserver() {
            if (typeof ResizeObserver === 'undefined') { return; }
            try {
                var ro = new ResizeObserver(function (entries) {
                    for (var i = 0; i < entries.length; i++) {
                        var width = entries[i].contentRect.width;
                        if (width > 0 && $root[0]) {
                            $root[0].style.setProperty('--widget-inline-size', width + 'px');
                        }
                    }
                });
                ro.observe($root[0]);
            } catch (e) { }
        }

// ===== NEW CODE START — currency format (agent A09, 2026-08-19) =====
        function loadRecentIssues() {
            showBusy(true);

            $.ajax({
                url: VIS.Application.contextUrl + 'VAS_187_RecentInventoryUseWidget/GetRecentIssues',
                type: 'GET',
                data: { status: selectedStatus, pageNo: pageNo, pageSize: pageSize },
                cache: false,
                success: function (res) {
                    var data = parseResponse(res);
                    recordsData = data.records || [];
                    totalRecords = data.totalRecords || 0;
                    if (data.currency) {
                        currencyIso = data.currency.iso || '';
                        currencySymbol = data.currency.symbol || '';
                        currencyPrecision = data.currency.stdPrecision; // VAS163 2026-11-12: server precision
                    }
                    renderRecords();
                },
                error: function () {
                    recordsData = [];
                    totalRecords = 0;
                    renderRecords();
                },
                complete: function () { showBusy(false); }
            });
        }
// ===== NEW CODE END — currency format =====
// ----- OLD CODE (kept for rollback, do not delete) -----
//        function loadRecentIssues() {
//            showBusy(true);
//
//            $.ajax({
//                url: VIS.Application.contextUrl + 'VAS_187_RecentInventoryUseWidget/GetRecentIssues',
//                type: 'GET',
//                data: { status: selectedStatus, pageNo: pageNo, pageSize: pageSize },
//                cache: false,
//                success: function (res) {
//                    var data = parseResponse(res);
//                    recordsData = data.records || [];
//                    totalRecords = data.totalRecords || 0;
//                    renderRecords();
//                },
//                error: function () {
//                    recordsData = [];
//                    totalRecords = 0;
//                    renderRecords();
//                },
//                complete: function () { showBusy(false); }
//            });
//        }
// ----- END OLD CODE -----

        function renderRecords() {
            if (!$body) { return; }

            totalPages = Math.max(1, Math.ceil(totalRecords / pageSize));
            if (pageNo > totalPages) { pageNo = totalPages; }

            if (recordsData.length === 0) {
                $body.html('<div class="vas-riu-empty">' + escapeHtml(label("VAS_187_NoRecentTransactions", "No recent inventory use transactions.")) + '</div>');
                if ($footHelper) { $footHelper.text('0 ' + label("VAS_187_Of", "of") + ' 0'); }
                if ($pagerText) { $pagerText.text('1 ' + label("VAS_187_Of", "of") + ' 1'); }
                if ($prevBtn) { $prevBtn.prop('disabled', true); }
                if ($nextBtn) { $nextBtn.prop('disabled', true); }
                return;
            }

            var startIndex = (pageNo - 1) * pageSize;
            var endIndex = Math.min(totalRecords, startIndex + recordsData.length);
            var rowsHtml = '';

// ===== NEW CODE START — currency format (agent A09, 2026-08-19) =====
            for (var i = 0; i < recordsData.length; i++) {
                var item = recordsData[i];
                var metaStr = (item.warehouseName || label("VAS_187_WarehouseFallback", "Warehouse")) + ' · ' + formatIsoDate(item.movementDateIso, item.movementDate);
                var formattedCompactVal = formatCurrencyAmount(item.totalValue, currencyIso, currencySymbol);
                var formattedFullVal = formatFullCurrency(item.totalValue, currencyIso, currencySymbol);

                rowsHtml +=
                    '<button type="button" class="vas-riu-row" data-invid="' + item.inventoryId + '">' +
                    '<div class="vas-riu-row-left">' +
                    '<div class="vas-riu-doc-head">' +
                    '<span class="vas-riu-doc-no">' + escapeHtml(item.documentNo) + '</span>' +
                    getStatusBadge(item.docStatus) +
                    '</div>' +
                    '<div class="vas-riu-doc-meta" title="' + escapeHtml(metaStr) + '">' +
                    '<svg class="vas-riu-cal-ico" aria-hidden="true" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2" stroke-linecap="round" stroke-linejoin="round"><rect x="3" y="4" width="18" height="18" rx="2"></rect><line x1="16" y1="2" x2="16" y2="6"></line><line x1="8" y1="2" x2="8" y2="6"></line><line x1="3" y1="10" x2="21" y2="10"></line></svg>' +
                    '<span>' + escapeHtml(metaStr) + '</span>' +
                    '</div>' +
                    '</div>' +
                    '<div class="vas-riu-row-right">' +
                    '<div class="vas-riu-stat">' +
                    '<div class="vas-riu-stat-lbl">' + escapeHtml(label("VAS_187_Lines", "Lines")) + '</div>' +
                    '<div class="vas-riu-stat-val">' + formatQty(item.lineCount) + '</div>' +
                    '</div>' +
                    '<div class="vas-riu-divider" aria-hidden="true"></div>' +
                    '<div class="vas-riu-stat">' +
                    '<div class="vas-riu-stat-lbl">' + escapeHtml(label("VAS_187_Amount", "Amount")) + '</div>' +
                    '<div class="vas-riu-stat-val" title="' + escapeHtml(formattedFullVal) + '">' + escapeHtml(formattedCompactVal) + '</div>' +
                    '</div>' +
                    '</div>' +
                    '</button>';
            }
// ===== NEW CODE END — currency format =====
// ----- OLD CODE (kept for rollback, do not delete) -----
//            for (var i = 0; i < recordsData.length; i++) {
//                var item = recordsData[i];
//                var metaStr = item.orgName + ' · ' + (item.warehouseName || 'Warehouse') + ' · ' + item.movementDate;
//
//                rowsHtml +=
//                    '<button type="button" class="vas-riu-row" data-invid="' + item.inventoryId + '">' +
//                    '<div class="vas-riu-row-left">' +
//                    '<div class="vas-riu-doc-head">' +
//                    '<span class="vas-riu-doc-no">' + escapeHtml(item.documentNo) + '</span>' +
//                    getStatusBadge(item.docStatus) +
//                    '</div>' +
//                    '<div class="vas-riu-doc-meta" title="' + escapeHtml(metaStr) + '">' + escapeHtml(metaStr) + '</div>' +
//                    '</div>' +
//                    '<div class="vas-riu-row-right">' +
//                    '<div class="vas-riu-lines-qty">' + item.lineCount + ' lines · ' + formatQty(item.totalQty) + '</div>' +
//                    '<div class="vas-riu-val">' + formatINR(item.totalValue) + '</div>' +
//                    '</div>' +
//                    '</button>';
//            }
// ----- END OLD CODE -----

            $body.html(rowsHtml);

            if ($footHelper) {
                $footHelper.text(formatQty(startIndex + 1) + '–' + formatQty(endIndex) + ' ' + label("VAS_187_Of", "of") + ' ' + formatQty(totalRecords)); // VAS163 2026-11-12: locale counts
            }
            if ($pagerText) {
                $pagerText.text(pageNo + ' ' + label("VAS_187_Of", "of") + ' ' + totalPages);
            }
            if ($prevBtn) { $prevBtn.prop('disabled', pageNo <= 1); }
            if ($nextBtn) { $nextBtn.prop('disabled', pageNo >= totalPages); }
        }

        /* Row click -> real Inventory Use record navigation. Mirrors VAS_184/VAS_186's own
           zoom-to-record pattern exactly: when hosted on a window, relay through
           widgetFirevalueChanged/ActionName so the host resolves the window BY NAME (a
           hardcoded AD_Window_ID has previously resolved to the wrong window on a real
           install - see VAS_244); otherwise (Home-page placement) fall back to
           VAS.ZoomUtil.zoomToRecord with a window id resolved once via VAS_178's existing
           GetMaterialIssueWindowId endpoint (same Inventory Use / Internal Use window every
           widget on this dashboard already targets - reused rather than re-deriving the same
           multi-name AD_Window lookup here).
           Replaces the previous openInventoryRecord(), which only sent
           {Record_ID, TabIndex} - no TabWhereClause/ActionName, and no fallback at all when
           windowNo < 0 - so the click did nothing on Home-page placement and, even on a
           window, had nothing to key off. */
        // VAS163 2026-11-12: real AD_Window name ('Inventory Use' is not a window name).
        var INVENTORY_USE_WINDOW_NAME = 'VAS_InternalUseInventory';
        var inventoryUseWindowId = 0;

        function hostWindowName() {
            try {
                var listener = $self.listener;
                for (var i = 0; i < 6 && listener; i++) {
                    if (listener.apanel && listener.apanel.gridWindow && listener.apanel.gridWindow.getName) { return listener.apanel.gridWindow.getName(); }
                    if (listener.gridWindow && listener.gridWindow.getName) { return listener.gridWindow.getName(); }
                    listener = listener.listener;
                }
            } catch (e) { /* best-effort */ }
            return '';
        }

        function openInventoryRecord(inventoryId) {
            if (!inventoryId) { return; }
            try {
                if ($self.windowNo >= 0) {
                    $self.widgetFirevalueChanged({
                        "TabWhereClause": "M_Inventory.M_Inventory_ID=" + Number(inventoryId),
                        "TabLayout": "Y", "TabIndex": "0",
                        "ActionName": hostWindowName() || INVENTORY_USE_WINDOW_NAME,
                        "ActionType": "W"
                    });
                    return;
                }
                // VAS163 2026-11-12: private GetMaterialIssueWindowId lookup replaced by
                // VAS.ZoomUtil name resolution (new name first, legacy second); id cached.
                if (window.VAS && VAS.ZoomUtil) {
                    VAS.ZoomUtil.zoomToRecord('M_Inventory_ID', Number(inventoryId), inventoryUseWindowId, 'VAS_InternalUseInventory', 'Internal Use Inventory')
                        .done(function (id) { if (id > 0) { inventoryUseWindowId = id; } });
                }
            } catch (e) { /* best-effort */ }
        }

        function createWidget() {
            var title = label("VAS_187_RecentInventoryUse", "Recent Inventory Use");
            var sub = label("VAS_187_LatestMaterialIssueTxns", "Latest material issue transactions");

            $card = $(
                '<div class="vas-riu-card vas-widget-bg">' +
                '<div class="vas-riu-head">' +
                '<div class="vas-riu-head-left">' +
                '<span class="vas-riu-ico" aria-hidden="true">' +
                '<svg viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2" stroke-linecap="round" stroke-linejoin="round"><circle cx="12" cy="12" r="10"></circle><polyline points="12 6 12 12 16 14"></polyline></svg>' +
                '</span>' +
                '<div>' +
                '<div class="vas-riu-title">' + escapeHtml(title) + '</div>' +
                '<div class="vas-riu-sub">' + escapeHtml(sub) + '</div>' +
                '</div>' +
                '</div>' +
                '<div class="vas-riu-filter-wrap">' +
                '<button type="button" class="vas-riu-filter-btn" aria-label="' + escapeHtml(label("VAS_187_Filter", "Filter")) + '" title="' + escapeHtml(label("VAS_187_Filter", "Filter")) + '">' +
                '<svg viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2" stroke-linecap="round" stroke-linejoin="round"><polygon points="22 3 2 3 10 12.46 10 19 14 21 14 12.46 22 3"></polygon></svg>' +
                '</button>' +
                '<div class="vas-riu-filter-menu vas-riu-hidden">' +
                '<button type="button" class="vas-riu-filter-opt active" data-status="ALL">' + escapeHtml(label("VAS_187_AllStatuses", "All Statuses")) + '</button>' +
                '<button type="button" class="vas-riu-filter-opt" data-status="CO">' + escapeHtml(label("VAS_187_Completed", "Completed")) + '</button>' +
                '<button type="button" class="vas-riu-filter-opt" data-status="CL">' + escapeHtml(label("VAS_187_Closed", "Closed")) + '</button>' +
                '<button type="button" class="vas-riu-filter-opt" data-status="DR">' + escapeHtml(label("VAS_187_Drafted", "Drafted")) + '</button>' +
                '<button type="button" class="vas-riu-filter-opt" data-status="IP">' + escapeHtml(label("VAS_187_InProcess", "In Process")) + '</button>' +
                '<button type="button" class="vas-riu-filter-opt" data-status="RE">' + escapeHtml(label("VAS_187_Reversed", "Reversed")) + '</button>' +
                '<button type="button" class="vas-riu-filter-opt" data-status="VO">' + escapeHtml(label("VAS_187_Voided", "Voided")) + '</button>' +
                '</div>' +
                '</div>' +
                '</div>' +
                '<div class="vas-riu-body"></div>' +
                '<div class="vas-riu-foot">' +
                '<div class="vas-riu-foot-helper">0 ' + escapeHtml(label("VAS_187_Of", "of")) + ' 0</div>' +
                '<div class="vas-riu-pager">' +
                '<button type="button" class="vas-riu-pager-btn vas-riu-prev" aria-label="' + escapeHtml(label("VAS_187_Previous", "Previous")) + '">' +
                '<svg viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2" stroke-linecap="round" stroke-linejoin="round"><polyline points="15 18 9 12 15 6"></polyline></svg>' +
                '</button>' +
                '<span class="vas-riu-pager-txt">1 ' + escapeHtml(label("VAS_187_Of", "of")) + ' 1</span>' +
                '<button type="button" class="vas-riu-pager-btn vas-riu-next" aria-label="' + escapeHtml(label("VAS_187_Next", "Next")) + '">' +
                '<svg viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2" stroke-linecap="round" stroke-linejoin="round"><polyline points="9 18 15 12 9 6"></polyline></svg>' +
                '</button>' +
                '</div>' +
                '</div>' +
                '</div>'
            );

            $body = $card.find('.vas-riu-body');
            $footHelper = $card.find('.vas-riu-foot-helper');
            $pagerText = $card.find('.vas-riu-pager-txt');
            $prevBtn = $card.find('.vas-riu-prev');
            $nextBtn = $card.find('.vas-riu-next');

            var $filterWrap = $card.find('.vas-riu-filter-wrap');
            var $filterBtn = $card.find('.vas-riu-filter-btn');
            var $filterMenu = $card.find('.vas-riu-filter-menu');
            var $filterOpts = $card.find('.vas-riu-filter-opt');

            $filterBtn.on('click', function (e) {
                e.stopPropagation();
                $filterMenu.toggleClass('vas-riu-hidden');
            });

            $filterOpts.on('click', function () {
                selectedStatus = $(this).data('status');
                $filterOpts.removeClass('active');
                $(this).addClass('active');
                $filterMenu.addClass('vas-riu-hidden');
                pageNo = 1;
                loadRecentIssues();
            });

            // Close the menu on any click outside the filter control. Namespaced so
            // disposeComponent() can remove exactly this handler and nothing else bound to
            // document by another widget instance or another widget entirely.
            docClickNs = 'click.vas-riu-' + (Math.random().toString(36).slice(2));
            $(document).on(docClickNs, function (e) {
                if (!$(e.target).closest($filterWrap).length) {
                    $filterMenu.addClass('vas-riu-hidden');
                }
            });

            $prevBtn.on('click', function () {
                if (pageNo > 1) { pageNo--; loadRecentIssues(); }
            });

            $nextBtn.on('click', function () {
                if (pageNo < totalPages) { pageNo++; loadRecentIssues(); }
            });

            $body.on('click', '.vas-riu-row', function () {
                var invId = Number($(this).data('invid') || 0);
                openInventoryRecord(invId);
            });

            $root.append($card);

            $busy = $('<div class="vas-riu-busy vas-riu-hidden"><div class="vis-busyindicatorinnerwrap"><i class="vis_widgetloader"></i></div></div>');
            $root.append($busy);
        }

        this.refreshWidget = function () {
            loadRecentIssues();
        };

        this.getRoot = function () { return $root; };

        this.disposeComponent = function () {
            if (docClickNs) { $(document).off(docClickNs); }
            $root.remove();
        };
    };

    VAS.VAS_187_RecentInventoryUseWidget.prototype.widgetFirevalueChanged = function (value) {
        if (this.listener) { this.listener.widgetFirevalueChanged(value); }
    };

    VAS.VAS_187_RecentInventoryUseWidget.prototype.addChangeListener = function (listener) {
        this.listener = listener;
    };

    VAS.VAS_187_RecentInventoryUseWidget.prototype.init = function (windowNo, frame) {
        this.frame = frame;
        this.AD_UserHomeWidgetID = frame.widgetInfo.AD_UserHomeWidgetID;
        this.windowNo = windowNo;
        this.Initalize();
        this.frame.getContentGrid().append(this.getRoot());
        ensureDashInlineSizeVar(this.getRoot());
    };

    VAS.VAS_187_RecentInventoryUseWidget.prototype.widgetSizeChange = function (height, width) { };

    VAS.VAS_187_RecentInventoryUseWidget.prototype.refreshWidget = function () {
        this.refreshWidget();
    };

    VAS.VAS_187_RecentInventoryUseWidget.prototype.dispose = function () {
        this.disposeComponent();
        if (this.frame) { this.frame.dispose(); }
        this.frame = null;
    };

})(VAS, jQuery);
/* VAS163 2026-11-12: VAS.formatAmountByBrowser(value, isoCode, precision, { compact, symbol })
   Amount in the BROWSER culture (window.navigator.language) with the currency the SERVER sent
   (ISO code + precision; the server symbol only as a fallback). Defined in each widget / panel
   that uses it - guarded, so the first file loaded defines it - so the shared util/CurrencyFormat.js
   stays as shipped. Compact amounts keep that file's default scale (VIS.Util.formatCompactAmount:
   L / Cr for Indian-numbering currencies, K / M / B otherwise, digits in the browser culture);
   separators, decimals and the position of the currency sign come from the browser culture. */
; (function () {
    var V = window.VAS = window.VAS || {};
    if (typeof V.formatAmountByBrowser === 'function') return;
    function browserLocale() { try { return window.navigator.language || undefined; } catch (e) { return undefined; } }
    function resolvePrecision(p) {
        var n = Number(p);
        if (p === null || p === undefined || p === '' || isNaN(n) || n < 0) {
            try { n = VIS.Env.getCtx().getStdPrecision(); } catch (e) { n = 2; }
        }
        n = Math.round(Number(n));
        return (isNaN(n) || n < 0) ? 2 : Math.min(n, 10);
    }
    function validIso(c) { c = String(c || '').trim().toUpperCase(); return /^[A-Z]{3}$/.test(c) ? c : ''; }
    function numberFormat(o) {
        try { return new Intl.NumberFormat(browserLocale(), o); } catch (e) { }
        if (o.currencyDisplay === 'narrowSymbol') {
            o.currencyDisplay = 'symbol';
            try { return new Intl.NumberFormat(browserLocale(), o); } catch (e2) { }
        }
        return null;
    }
    /* Currency sign and where the browser culture puts it for this ISO code. */
    var layoutCache = {};
    function currencyLayout(iso) {
        var key = browserLocale() + '|' + iso;
        if (layoutCache[key]) return layoutCache[key];
        var out = { sign: '', before: true, gap: '' };
        var f = iso ? numberFormat({ style: 'currency', currency: iso, currencyDisplay: 'narrowSymbol' }) : null;
        if (f && typeof f.formatToParts === 'function') {
            try {
                var parts = f.formatToParts(1234.5), ci = -1, ni = -1;
                for (var i = 0; i < parts.length; i++) {
                    if (parts[i].type === 'currency' && ci < 0) ci = i;
                    if (parts[i].type === 'integer' && ni < 0) ni = i;
                }
                if (ci >= 0) {
                    out.sign = parts[ci].value;
                    if (ni >= 0) {
                        out.before = ci < ni;
                        var near = out.before ? parts[ci + 1] : parts[ci - 1];
                        out.gap = (near && near.type === 'literal') ? near.value : '';
                    }
                }
            } catch (e) { }
        }
        layoutCache[key] = out;
        return out;
    }
    V.formatAmountByBrowser = function (value, isoCode, precision, options) {
        options = options || {};
        var num = Number(value) || 0;
        var p = resolvePrecision(precision);
        var iso = validIso(isoCode);
        var body;
        if (options.compact && window.VIS && VIS.Util && typeof VIS.Util.formatCompactAmount === 'function') {
            body = VIS.Util.formatCompactAmount(Math.abs(num), iso, p);
        } else {
            var f = numberFormat({ minimumFractionDigits: p, maximumFractionDigits: p });
            try { body = f ? f.format(Math.abs(num)) : Math.abs(num).toFixed(p); } catch (e) { body = String(Math.abs(num)); }
        }
        var minus = (num < 0 && /[1-9]/.test(body)) ? '-' : '';
        var lay = currencyLayout(iso);
        var sign = lay.sign || options.symbol || iso;
        if (!sign) return minus + body;
        if (!lay.sign) return minus + sign + ' ' + body;
        return lay.before ? (minus + sign + lay.gap + body) : (minus + body + lay.gap + sign);
    };
})();
