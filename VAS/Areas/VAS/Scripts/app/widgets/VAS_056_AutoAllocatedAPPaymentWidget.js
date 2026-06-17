/**
 * Auto Allocated AP Payment Widget
 * Purpose - KPI card showing percentage of outgoing AP payments in the last
 *           30 days that are allocated/matched, with the same drill-down
 *           dialog pattern used by AutoAllocatedWidget.
 */
; VAS = window.VAS || {};

; (function (VAS, $) {
    "use strict";

    VAS.VAS_056_AutoAllocatedAPPaymentWidget = function () {
        this.frame = null;
        this.windowNo = 0;
        this.AD_UserHomeWidgetID = 0;
        var self = this;

        var $root = $('<div class="vas-aa-root vas-aap-root">');
        var $metricEl = null;
        var $busy = null;
        var $dialog = null;
        var $dialogTbody = null;
        var $dialogBusy = null;
        var $pagerHelper = null;
        var $pagerPrev = null;
        var $pagerNext = null;
        var $pagerText = null;
        var $tabAllocated = null;
        var $tabUnallocated = null;
        var $tabAllocatedCount = null;
        var $tabUnallocatedCount = null;

        var rowsLoading = false;
        var pageSize = 10;
        var activeFilter = "allocated";
        var isDisposed = false;
        var tabState = {
            allocated: { pageNo: 1, totalPages: 0, totalRecords: 0, loaded: false },
            unallocated: { pageNo: 1, totalPages: 0, totalRecords: 0, loaded: false }
        };

        function lbl(key, fallback) {
            var text = VIS.Msg.getMsg(key);
            return text && text !== '[' + key + ']' ? text : fallback;
        }

        function escapeHtml(value) {
            return String(value == null ? "" : value)
                .replace(/&/g, "&amp;")
                .replace(/</g, "&lt;")
                .replace(/>/g, "&gt;")
                .replace(/"/g, "&quot;")
                .replace(/'/g, "&#039;");
        }

        function parseResponse(response) {
            if (typeof response !== 'string') { return response; }

            try {
                var data = JSON.parse(response);
                return typeof data === 'string' ? JSON.parse(data) : data;
            }
            catch (e) {
                return null;
            }
        }

        function showBusy(show) {
            if ($busy && $busy[0]) {
                $busy[0].style.visibility = show ? 'visible' : 'hidden';
            }
        }

        function showDialogBusy(show) {
            if ($dialogBusy && $dialogBusy[0]) {
                $dialogBusy[0].style.visibility = show ? 'visible' : 'hidden';
            }
        }

        this.Initalize = function () {
            createWidget();
            loadKpi();
        };

        function loadKpi() {
            showBusy(true);

            $.ajax({
                url: VIS.Application.contextUrl + 'VAS_056_AutoAllocatedAPPaymentWidget/GetAutoAllocatedAPPayments',
                type: 'GET',
                cache: false,
                success: function (response) {
                    if (isDisposed) { return; }

                    var data = parseResponse(response);

                    if (!data || data.error) {
                        setNoData();
                        return;
                    }

                    renderMetric(data);
                },
                error: setNoData,
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
                url: VIS.Application.contextUrl + 'VAS_056_AutoAllocatedAPPaymentWidget/GetAutoAllocatedAPPaymentRows',
                type: 'GET',
                cache: false,
                data: {
                    pageNo: state.pageNo,
                    pageSize: pageSize,
                    filter: filterAtRequest
                },
                success: function (response) {
                    if (isDisposed || filterAtRequest !== activeFilter) { return; }

                    rowsLoading = false;
                    showDialogBusy(false);

                    var data = parseResponse(response);
                    renderPageResult(filterAtRequest, data && !data.error ? data : {});
                    tabState[filterAtRequest].loaded = true;
                },
                error: function () {
                    if (isDisposed || filterAtRequest !== activeFilter) { return; }

                    rowsLoading = false;
                    showDialogBusy(false);
                    renderPageResult(filterAtRequest, {});
                }
            });
        }

        function renderMetric(data) {
            var percent = Number(data.autoAllocatedPercent || 0);

            if ($metricEl) {
                $metricEl.text(formatPercent(percent));
            }

            var matched = Number(data.matchedPayments || 0);
            var unmatched = Number(data.unmatchedPayments || 0);
            tabState.allocated.totalRecords = matched;
            tabState.unallocated.totalRecords = unmatched;
            tabState.allocated.totalPages = pageSize === 0 ? 0 : Math.ceil(matched / pageSize);
            tabState.unallocated.totalPages = pageSize === 0 ? 0 : Math.ceil(unmatched / pageSize);
            updateTabCounts();

            if ($dialog && $dialog.is(':visible')) {
                loadRows();
            }
        }

        function renderPageResult(filter, data) {
            var state = tabState[filter];
            var rows = data && data.rows ? data.rows : [];

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

        function renderRows(rows) {
            $dialogTbody.empty();

            if (!rows || rows.length === 0) {
                $dialogTbody.html(
                    '<tr><td class="vas-aa-dialog-empty" colspan="6">' +
                    escapeHtml(lbl("VAS_056_NoPaymentsThisPeriod", "No payments in this period")) +
                    '</td></tr>'
                );
                return;
            }

            for (var i = 0; i < rows.length; i++) {
                var row = rows[i];
                var dateText = formatDate(row.date);
                var docNo = row.documentNo || "";
                var vendor = row.vendor || "";
                var bankText = formatBankAccount(row);
                var currencyCode = row.paymentCurrency || "";
                var symbol = row.paymentCurrencySymbol || currencyCode || "";
                var amountText = formatAmount(row.amount);
                var amountHtml = (symbol ? '<span class="vas-aa-cur-inline">' + escapeHtml(symbol) + '</span>' : '') + escapeHtml(amountText);

                $dialogTbody.append(
                    '<tr>' +
                    '<td class="vas-aa-td-date" title="' + escapeHtml(dateText) + '">' + escapeHtml(dateText) + '</td>' +
                    '<td class="vas-aa-td-doc" title="' + escapeHtml(docNo) + '"><span class="vas-aa-truncate">' + escapeHtml(docNo) + '</span></td>' +
                    '<td class="vas-aa-td-customer" title="' + escapeHtml(vendor) + '"><span class="vas-aa-truncate">' + escapeHtml(vendor) + '</span></td>' +
                    '<td class="vas-aa-td-bank" title="' + escapeHtml(bankText) + '"><span class="vas-aa-truncate">' + escapeHtml(bankText) + '</span></td>' +
                    '<td class="vas-aa-td-currency" title="' + escapeHtml(currencyCode) + '">' + escapeHtml(currencyCode) + '</td>' +
                    '<td class="vas-aa-td-amount" title="' + escapeHtml((symbol ? symbol + ' ' : '') + amountText) + '">' + amountHtml + '</td>' +
                    '</tr>'
                );
            }
        }

        function updatePagerControls(from, to) {
            var state = tabState[activeFilter];
            updateTabCounts();

            if ($pagerHelper) {
                if (state.totalRecords > 0) {
                    var paymentLabel = state.totalRecords === 1
                        ? lbl("VAS_056_Payment", "payment")
                        : lbl("VAS_056_Payments", "payments");

                    $pagerHelper.text(
                        lbl("VAS_Showing", "Showing") + ' ' +
                        (from + 1) + '-' + to + ' ' +
                        lbl("VAS_Of", "of") + ' ' + state.totalRecords + ' ' + paymentLabel
                    );
                }
                else {
                    $pagerHelper.text("");
                }
            }

            if ($pagerText) {
                $pagerText.text(state.totalPages > 0 ? (state.pageNo + ' ' + lbl("VAS_Of", "of") + ' ' + state.totalPages) : "");
            }

            if ($pagerPrev) { $pagerPrev.prop("disabled", rowsLoading || state.pageNo <= 1); }
            if ($pagerNext) { $pagerNext.prop("disabled", rowsLoading || state.totalPages <= 1 || state.pageNo >= state.totalPages); }
        }

        function updateTabCounts() {
            if ($tabAllocatedCount) { $tabAllocatedCount.text(tabState.allocated.totalRecords); }
            if ($tabUnallocatedCount) { $tabUnallocatedCount.text(tabState.unallocated.totalRecords); }
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

        function setNoData() {
            if ($metricEl) { $metricEl.text("0%"); }
        }

        function formatPercent(value) {
            return Number(value || 0).toLocaleString(window.navigator.language, {
                minimumFractionDigits: getStdPrecision(),
                maximumFractionDigits: getStdPrecision()
            }) + "%";
        }

        function formatAmount(value) {
            return Number(value || 0).toLocaleString(window.navigator.language, {
                minimumFractionDigits: getStdPrecision(),
                maximumFractionDigits: getStdPrecision()
            });
        }

        function getStdPrecision() {
            var stdPrecision = 2;

            try {
                if (VIS.Env && VIS.Env.getCtx && VIS.Env.getCtx().getStdPrecision) {
                    stdPrecision = Number(VIS.Env.getCtx().getStdPrecision());
                }
            }
            catch (e) {
                stdPrecision = 2;
            }

            return isNaN(stdPrecision) || stdPrecision < 0 ? 2 : stdPrecision;
        }

        function formatDate(value) {
            if (!value) { return ""; }
            var date = new Date(value);
            if (isNaN(date.getTime())) { return value; }

            return date.toLocaleDateString(window.navigator.language, {
                day: "2-digit",
                month: "short",
                year: "numeric"
            });
        }

        function formatBankAccount(row) {
            var bankName = row && row.bankName ? String(row.bankName).trim() : "";
            var accountNo = row && row.accountNo ? String(row.accountNo).trim() : "";
            var last4 = accountNo ? (accountNo.length > 4 ? accountNo.slice(-4) : accountNo) : "";

            if (bankName && last4) { return bankName + ' - ****' + last4; }
            if (bankName) { return bankName; }
            if (last4) { return '****' + last4; }
            return "";
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
            activeFilter = "allocated";
            tabState.allocated.pageNo = 1;
            tabState.allocated.loaded = false;
            tabState.unallocated.pageNo = 1;
            tabState.unallocated.loaded = false;
            updateActiveTabStyles();
        }

        function createDialog() {
            $dialog = $(
                '<div class="vas-aa-dialog vas-aap-dialog" style="display:none;" role="dialog" aria-modal="true">' +
                '<div class="vas-aa-dialog-scrim"></div>' +
                '<div class="vas-aa-dialog-card">' +
                '<div class="vas-aa-dialog-header">' +
                '<div class="vas-aa-dialog-icon"><svg viewBox="0 0 24 24" fill="none" aria-hidden="true" focusable="false"><circle cx="12" cy="12" r="10"></circle><polyline points="12 6 12 12 16 14"></polyline></svg></div>' +
                '<div class="vas-aa-dialog-title-group">' +
                '<div class="vas-aa-dialog-title">' + escapeHtml(lbl("VAS_056_PaymentAllocation", "Payment allocation")) + '</div>' +
                '<div class="vas-aa-dialog-subtitle">' + escapeHtml(lbl("VAS_056_AllocatedVsUnallocated", "Allocated vs unallocated payments in the last 30 days")) + '</div>' +
                '</div>' +
                '<button type="button" class="vas-aa-dialog-close" aria-label="' + escapeHtml(lbl("VAS_Close", "Close")) + '"><svg viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="1.8" stroke-linecap="round" stroke-linejoin="round"><line x1="18" y1="6" x2="6" y2="18"/><line x1="6" y1="6" x2="18" y2="18"/></svg></button>' +
                '</div>' +
                '<div class="vas-aa-tabs" role="tablist">' +
                '<button type="button" class="vas-aa-tab vas-aap-tab-allocated vas-aa-tab-active" role="tab" aria-selected="true"><span class="vas-aa-tab-label">' + escapeHtml(lbl("VAS_Allocated", "Allocated")) + '</span><span class="vas-aa-tab-count vas-aap-tab-count-allocated">0</span></button>' +
                '<button type="button" class="vas-aa-tab vas-aap-tab-unallocated" role="tab" aria-selected="false"><span class="vas-aa-tab-label">' + escapeHtml(lbl("VAS_Unallocated", "Unallocated")) + '</span><span class="vas-aa-tab-count vas-aap-tab-count-unallocated">0</span></button>' +
                '</div>' +
                '<div class="vas-aa-dialog-body">' +
                '<div class="vas-aa-dialog-busy"><div class="vis-busyindicatorinnerwrap"><i class="vis_widgetloader"></i></div></div>' +
                '<table class="vas-aa-dialog-table"><thead><tr>' +
                '<th class="vas-aa-th-date">' + escapeHtml(lbl("VAS_Date", "Date")) + '</th>' +
                '<th class="vas-aa-th-doc">' + escapeHtml(lbl("VAS_056_PaymentNo", "Payment No.")) + '</th>' +
                '<th class="vas-aa-th-customer">' + escapeHtml(lbl("VAS_Vendor", "Vendor")) + '</th>' +
                '<th class="vas-aa-th-bank">' + escapeHtml(lbl("VAS_BankAccount", "Bank account")) + '</th>' +
                '<th class="vas-aa-th-currency">' + escapeHtml(lbl("VAS_PaymentCurrency", "Payment Currency")) + '</th>' +
                '<th class="vas-aa-th-amount">' + escapeHtml(lbl("VAS_Amount", "Amount")) + '</th>' +
                '</tr></thead><tbody class="vas-aap-dialog-tbody"></tbody></table>' +
                '</div>' +
                '<div class="vas-aa-dialog-footer"><span class="vas-aa-pager-helper"></span><div class="vas-aa-pager">' +
                '<button type="button" class="vas-aa-pager-btn vas-aap-pager-prev" aria-label="' + escapeHtml(lbl("VAS_Previous", "Previous")) + '"><svg viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2" stroke-linecap="round" stroke-linejoin="round"><polyline points="15 18 9 12 15 6"/></svg></button>' +
                '<span class="vas-aa-pager-text"></span>' +
                '<button type="button" class="vas-aa-pager-btn vas-aap-pager-next" aria-label="' + escapeHtml(lbl("VAS_Next", "Next")) + '"><svg viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2" stroke-linecap="round" stroke-linejoin="round"><polyline points="9 18 15 12 9 6"/></svg></button>' +
                '</div></div>' +
                '</div></div>'
            );

            $dialogTbody = $dialog.find('.vas-aap-dialog-tbody');
            $dialogBusy = $dialog.find('.vas-aa-dialog-busy');
            $dialogBusy[0].style.visibility = 'hidden';
            $pagerHelper = $dialog.find('.vas-aa-pager-helper');
            $pagerPrev = $dialog.find('.vas-aap-pager-prev');
            $pagerNext = $dialog.find('.vas-aap-pager-next');
            $pagerText = $dialog.find('.vas-aa-pager-text');
            $tabAllocated = $dialog.find('.vas-aap-tab-allocated');
            $tabUnallocated = $dialog.find('.vas-aap-tab-unallocated');
            $tabAllocatedCount = $dialog.find('.vas-aap-tab-count-allocated');
            $tabUnallocatedCount = $dialog.find('.vas-aap-tab-count-unallocated');

            $dialog.find('.vas-aa-dialog-close, .vas-aa-dialog-scrim').on('click', closeDialog);
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

            $(document).on('keydown.vas-aap-' + self.AD_UserHomeWidgetID, function (e) {
                if (e.key === 'Escape' && $dialog.is(':visible')) {
                    closeDialog();
                }
            });

            $('body').append($dialog);
        }

        function createWidget() {
            var $card = $(
                '<div class="vas-aa-card vas-aap-card" role="button" tabindex="0">' +
                '<div class="vas-aa-head">' +
                '<div class="vas-aa-head-left">' +
                '<div class="vas-aa-icon"><svg viewBox="0 0 24 24" fill="none" aria-hidden="true" focusable="false"><circle cx="12" cy="12" r="10"></circle><polyline points="12 6 12 12 16 14"></polyline></svg></div>' +
                '<span class="vas-aa-label">' + escapeHtml(lbl("VAS_056_AutoAllocatedAPPayments", "Auto-allocated AP")) + '</span>' +
                '</div>' +
                '<button type="button" class="vas-aa-view"><svg viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2.4" stroke-linecap="round" stroke-linejoin="round"><path d="M7 17 17 7"/><path d="M7 7h10v10"/></svg><span>' + escapeHtml(lbl("VAS_View", "View")) + '</span></button>' +
                '</div>' +
                '<div class="vas-aa-metric">-</div>' +
                '<span class="vas-aa-detail-text">' + escapeHtml(lbl("VAS_056_PaymentMatchToInvoice", "Payment Match to Invoice")) + '</span>' +
                '</div>'
            );

            $metricEl = $card.find('.vas-aa-metric');

            $card.on('click', openDialog);
            $card.on('keydown', function (e) {
                if (e.key === 'Enter' || e.key === ' ') {
                    e.preventDefault();
                    openDialog();
                }
            });
            $card.find('.vas-aa-view').on('click', function (e) {
                e.stopPropagation();
                openDialog();
            });

            $root.append($card);
            $busy = $('<div class="vas-aa-busy"><div class="vis-busyindicatorinnerwrap"><i class="vis_widgetloader"></i></div></div>');
            $busy[0].style.visibility = 'hidden';
            $root.append($busy);
            createDialog();
        }

        this.refreshData = function () {
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
            isDisposed = true;
            $(document).off('keydown.vas-aap-' + this.AD_UserHomeWidgetID);
            $('body').removeClass('vas-aa-body-lock');

            if ($dialog) {
                $dialog.remove();
                $dialog = null;
            }

            $root.remove();
        };
    };

    VAS.VAS_056_AutoAllocatedAPPaymentWidget.prototype.init = function (windowNo, frame) {
        this.frame = frame;
        this.windowNo = windowNo;

        if (frame && frame.widgetInfo) {
            this.AD_UserHomeWidgetID = frame.widgetInfo.AD_UserHomeWidgetID;
        }

        this.Initalize();

        if (this.frame && this.frame.getContentGrid) {
            this.frame.getContentGrid().append(this.getRoot());
        }
    };

    VAS.VAS_056_AutoAllocatedAPPaymentWidget.prototype.widgetSizeChange = function () { };

    VAS.VAS_056_AutoAllocatedAPPaymentWidget.prototype.refreshWidget = function () {
        this.refreshData();
    };

    VAS.VAS_056_AutoAllocatedAPPaymentWidget.prototype.dispose = function () {
        this.disposeComponent();

        if (this.frame && this.frame.dispose) {
            this.frame.dispose();
        }

        this.frame = null;
    };

})(VAS, jQuery);
