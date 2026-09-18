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
        var pageSize = 4;
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

// ===== NEW CODE START — currency format (agent A09, 2026-08-19) =====
        /**
         * Formats currency values according to organization locale/currency settings.
         * Indian ISOs: Lakh/Crore notation.
         * Other ISOs: Standard thousand separators, compact M/B notation.
         */
        function formatCurrencyAmount(val, iso, symbol) {
            var num = parseFloat(val);
            if (isNaN(num)) { num = 0; }
            symbol = symbol || '';
            iso = (iso || '').toUpperCase();

            var indianIsos = ['INR', 'PKR', 'BDT', 'NPR', 'BTN', 'LKR'];
            var isIndian = indianIsos.indexOf(iso) !== -1;
            var formattedVal = '';

            if (isIndian) {
                var absVal = Math.abs(num);
                if (absVal >= 10000000) {
                    formattedVal = (num / 10000000).toFixed(2).replace(/\.00$/, '') + ' Cr';
                } else if (absVal >= 100000) {
                    formattedVal = (num / 100000).toFixed(2).replace(/\.00$/, '') + ' L';
                } else if (absVal >= 1000) {
                    formattedVal = (num / 1000).toFixed(1).replace(/\.0$/, '') + ' k';
                } else {
                    formattedVal = formatIndianGrouping(num);
                }
            } else {
                var absVal = Math.abs(num);
                if (absVal >= 1000000000) {
                    formattedVal = (num / 1000000000).toFixed(2).replace(/\.00$/, '') + ' B';
                } else if (absVal >= 1000000) {
                    formattedVal = (num / 1000000).toFixed(2).replace(/\.00$/, '') + ' M';
                } else if (absVal >= 1000) {
                    formattedVal = (num / 1000).toFixed(1).replace(/\.0$/, '') + ' K';
                } else {
                    formattedVal = num.toLocaleString();
                }
            }

            return symbol ? (symbol + ' ' + formattedVal) : formattedVal;
        }

        function formatFullCurrency(val, iso, symbol) {
            var num = parseFloat(val);
            if (isNaN(num)) { num = 0; }
            symbol = symbol || '';
            iso = (iso || '').toUpperCase();

            var indianIsos = ['INR', 'PKR', 'BDT', 'NPR', 'BTN', 'LKR'];
            var isIndian = indianIsos.indexOf(iso) !== -1;
            var fullStr = isIndian ? formatIndianGrouping(num) : num.toLocaleString();
            return symbol ? (symbol + ' ' + fullStr) : fullStr;
        }

        // NOTE (2026-09-17): formatINR is called below but was never defined anywhere in this
        // file - a pre-existing bug that threw ReferenceError while rendering every row. Aliased
        // to the exact-value currency formatter already defined above.
        function formatINR(value) {
            return formatFullCurrency(value, currencyIso, currencySymbol);
        }

        function formatIndianGrouping(num) {
            var parts = num.toString().split('.');
            var integerPart = parts[0];
            var decimalPart = parts.length > 1 ? '.' + parts[1] : '';
            var isNegative = false;

            if (integerPart.indexOf('-') === 0) {
                isNegative = true;
                integerPart = integerPart.substring(1);
            }

            var lastThree = integerPart.substring(integerPart.length - 3);
            var otherNumbers = integerPart.substring(0, integerPart.length - 3);
            if (otherNumbers !== '') {
                lastThree = ',' + lastThree;
            }
            var formatted = otherNumbers.replace(/\B(?=(\d{2})+(?!\d))/g, ",") + lastThree + decimalPart;
            return isNegative ? '-' + formatted : formatted;
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
                var metaStr = (item.warehouseName || label("VAS_187_WarehouseFallback", "Warehouse")) + ' · ' + item.movementDate;
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
                $footHelper.text((startIndex + 1) + '–' + endIndex + ' ' + label("VAS_187_Of", "of") + ' ' + totalRecords);
            }
            if ($pagerText) {
                $pagerText.text(pageNo + ' ' + label("VAS_187_Of", "of") + ' ' + totalPages);
            }
            if ($prevBtn) { $prevBtn.prop('disabled', pageNo <= 1); }
            if ($nextBtn) { $nextBtn.prop('disabled', pageNo >= totalPages); }
        }

        function openInventoryRecord(inventoryId) {
            var windowParam = {
                "Record_ID": inventoryId,
                "TabIndex": "0"
            };
            $self.widgetFirevalueChanged(windowParam);
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
                '<button type="button" class="vas-riu-filter-btn">' +
                '<svg viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2" stroke-linecap="round" stroke-linejoin="round"><polygon points="22 3 2 3 10 12.46 10 19 14 21 14 12.46 22 3"></polygon></svg>' +
                '<span>' + escapeHtml(label("VAS_187_Filter", "Filter")) + '</span>' +
                '</button>' +
                '<div class="vas-riu-filter-menu vas-riu-hidden">' +
                '<button type="button" class="vas-riu-filter-opt active" data-status="ALL">' + escapeHtml(label("VAS_187_AllStatuses", "All Statuses")) + '</button>' +
                '<button type="button" class="vas-riu-filter-opt" data-status="CO">' + escapeHtml(label("VAS_187_Completed", "Completed")) + '</button>' +
                '<button type="button" class="vas-riu-filter-opt" data-status="DR">' + escapeHtml(label("VAS_187_Drafted", "Drafted")) + '</button>' +
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
