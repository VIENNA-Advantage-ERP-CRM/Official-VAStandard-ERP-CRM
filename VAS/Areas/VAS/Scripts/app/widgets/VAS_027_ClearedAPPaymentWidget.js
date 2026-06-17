/**
 * Cleared AP Payment
 * Purpose - Shows the percentage of AP payments from the previous calendar month that have been reconciled.
 *
 * Labels / Message Keys
 * 1 | Cleared                                | VAS_027_messageCleared
 * 2 | Of last month's AP payments reconciled | VAS_027_messageAPPaymentClearedWhy
 * 3 | Loading                                | VAS_027_messageLoading
 * 4 | No Data                                | VAS_027_messageNoData
 */

; VAS = window.VAS || {};

; (function (VAS, $) {
    "use strict";

    var VAS_027_ClearedAPPaymentWidget = function () {
        this.frame = null;
        this.windowNo = 0;
        this.AD_UserHomeWidgetID = 0;

        var $root = $('<div class="vas-finance-kpi-root">');
        var $card = null;
        var $value = null;
        var $body = null;
        var $footer = null;
        var $busy = null;
        var $state = null;
        var $dialog = null;
        var $dialogTbody = null;
        var $dialogBusy = null;
        var $dialogSubtitle = null;
        var $summaryCount = null;
        var $summaryAmount = null;
        var $summaryOldest = null;
        var $summaryRate = null;
        var $pagerHelper = null;
        var $pagerPrev = null;
        var $pagerNext = null;
        var $pagerText = null;
        var isDisposed = false;
        var rowsLoaded = false;
        var rowsLoading = false;
        var pageNo = 1;
        var pageSize = 5;
        var totalPages = 0;
        var totalRecords = 0;

        function lbl(key, fallback) {
            var text = VIS.Msg.getMsg(key);
            return text && text !== '[' + key + ']' ? text : fallback;
        }

        function createWidget() {
            $card = $('<div class="vas-finance-kpi-card" role="button" tabindex="0">');

            var $header = $('<div class="vas-finance-kpi-header">');

            var $iconBox = $('<div class="vas-finance-kpi-icon-box">');

            var $icon = $(
                '<svg class="vas-finance-kpi-icon" viewBox="0 0 24 24" aria-hidden="true" focusable="false">' +
                '<path fill="currentColor" d="M9.2 16.6 4.95 12.35 6.36 10.94 9.2 13.77 17.64 5.34 19.05 6.75z"></path>' +
                '</svg>'
            );

            var $title = $('<div class="vas-finance-kpi-title">').text(
                lbl('VAS_027_messageCleared', 'Cleared')
            );

            $iconBox.append($icon);
            $header.append($iconBox).append($title);

            $body = $('<div class="vas-finance-kpi-body">');

            $value = $('<div class="vas-finance-kpi-value">').text('');

            $body.append($value);

            $footer = $('<div class="vas-finance-kpi-footer">');


            var $description = $('<div class="vas-finance-kpi-desc">').text(
                lbl('VAS_027_messageAPPaymentClearedWhy', "Of last month's AP payments reconciled")
            );

            $footer.append($description);

            $busy = $('<div class="vas-finance-kpi-busy">').text(lbl('VAS_027_messageLoading', 'Loading'));
            $state = $('<div class="vas-finance-kpi-state-message">');

            $card.append($header).append($body).append($footer).append($busy).append($state);
            $card.on('click', function () {
                openDialog();
            });
            $card.on('keydown', function (e) {
                if (e.key === 'Enter' || e.key === ' ') {
                    e.preventDefault();
                    openDialog();
                }
            });

            $root.empty().append($card);
            createDialog();
        }

        function loadData() {
            if (isDisposed) {
                return;
            }

            showBusy(true);
            showState(false, '');

            $.ajax({
                url: VIS.Application.contextUrl + 'VAS_027_ClearedAPPaymentWidget/GetClearedAPPayment',
                type: 'GET',
                dataType: 'json',
                cache: false,

                success: function (response) {
                    if (isDisposed) {
                        return;
                    }

                    var data = normalizeResponse(response);

                    if (!data || data.error) {
                        showState(true, lbl('VAS_ErrorLoading', 'Could not load data'));
                        return;
                    }

                    renderData(data);
                },

                error: function () {
                    if (!isDisposed) {
                        showState(true, lbl('VAS_ErrorLoading', 'Could not load data'));
                    }
                },

                complete: function () {
                    if (!isDisposed) {
                        showBusy(false);
                    }
                }
            });
        }

        function normalizeResponse(response) {
            if (typeof response !== 'string') {
                return response;
            }

            try {
                return JSON.parse(response);
            }
            catch (e) {
                return null;
            }
        }

        function loadRows() {
            if (isDisposed || !$dialogTbody) {
                return;
            }

            rowsLoading = true;
            showDialogBusy(true);
            updatePagerButtons();

            $.ajax({
                url: VIS.Application.contextUrl + 'VAS_027_ClearedAPPaymentWidget/GetUnreconciledAPPayments',
                type: 'GET',
                dataType: 'json',
                cache: false,
                data: { pageNo: pageNo, pageSize: pageSize },

                success: function (response) {
                    if (isDisposed) {
                        return;
                    }

                    var data = normalizeResponse(response);

                    if (!data || data.error) {
                        renderPageResult({ rows: [], totalRecords: 0, totalPages: 0, pageNo: pageNo });
                        return;
                    }

                    renderPageResult(data);
                    rowsLoaded = true;
                },

                error: function () {
                    if (!isDisposed) {
                        renderPageResult({ rows: [], totalRecords: 0, totalPages: 0, pageNo: pageNo });
                    }
                },

                complete: function () {
                    if (!isDisposed) {
                        rowsLoading = false;
                        showDialogBusy(false);
                        updatePagerButtons();
                    }
                }
            });
        }

        function renderPageResult(data) {
            var rows = data && data.rows ? data.rows : [];

            totalRecords = Number(data && data.totalRecords || 0);
            totalPages = Number(data && data.totalPages || 0);

            if (data && typeof data.pageNo !== 'undefined') {
                pageNo = Number(data.pageNo);
            }

            if (pageNo > totalPages && totalPages > 0) {
                pageNo = totalPages;
            }

            if (pageNo < 1) {
                pageNo = 1;
            }

            renderSummary(data || {});
            renderRows(rows);
            updatePager();
        }

        function renderSummary(data) {
            var stdPrecision = normalizePrecision(data.stdPrecision);
            var symbol = data.curSymbol || data.currencyIso || '';
            var totalAmountText = formatExactAmount(data.totalAmount, stdPrecision, symbol);
            var oldestDays = Number(data.oldestDays || 0);
            var autoMatchRate = Number(data.autoMatchRate || 0);

            if ($dialogSubtitle) {
                $dialogSubtitle.text(
                    formatCount(totalRecords) + ' ' +
                    lbl('VAS_027_messagePayments', 'payments') + ' - ' +
                    totalAmountText + ' ' +
                    lbl('VAS_027_messageAwaitingBankMatch', 'awaiting bank match')
                );
            }

            if ($summaryCount) {
                $summaryCount.text(formatCount(totalRecords));
            }

            if ($summaryAmount) {
                $summaryAmount.text(totalAmountText);
            }

            if ($summaryOldest) {
                $summaryOldest.text(oldestDays > 0 ? oldestDays + ' ' + lbl('VAS_027_messageDays', 'days') : '0');
            }

            if ($summaryRate) {
                $summaryRate.text(formatPercent(autoMatchRate, 0));
            }
        }

        function renderRows(rows) {
            if (!$dialogTbody) {
                return;
            }

            $dialogTbody.empty();

            if (!rows || rows.length === 0) {
                $dialogTbody.html(
                    '<tr><td class="vas-cpa-dialog-empty" colspan="8">' +
                    escapeHtml(lbl('VAS_027_messageNoUnreconciledPayments', 'No unreconciled payments')) +
                    '</td></tr>'
                );
                return;
            }

            for (var i = 0; i < rows.length; i++) {
                var row = rows[i];
                var dateText = formatDate(row.date);
                var paymentNo = row.paymentNo || '';
                var vendor = row.vendor || '';
                var bankText = formatBankAccount(row);
                var currencyIso = row.currencyIso || '';
                var stdPrecision = normalizePrecision(row.stdPrecision);
                var amountText = formatExactAmount(row.amount, stdPrecision, row.curSymbol || currencyIso || '');
                var method = row.method || '';
                var reason = row.whyUnreconciled || '';

                $dialogTbody.append(
                    '<tr>' +
                    '<td class="vas-cpa-td-doc" title="' + escapeHtml(paymentNo) + '"><span class="vas-cpa-truncate">' + escapeHtml(paymentNo) + '</span></td>' +
                    '<td class="vas-cpa-td-date" title="' + escapeHtml(dateText) + '">' + escapeHtml(dateText) + '</td>' +
                    '<td class="vas-cpa-td-vendor" title="' + escapeHtml(vendor) + '"><span class="vas-cpa-truncate">' + escapeHtml(vendor) + '</span></td>' +
                    '<td class="vas-cpa-td-bank" title="' + escapeHtml(bankText) + '"><span class="vas-cpa-truncate">' + escapeHtml(bankText) + '</span></td>' +
                    '<td class="vas-cpa-td-currency" title="' + escapeHtml(currencyIso) + '">' + escapeHtml(currencyIso) + '</td>' +
                    '<td class="vas-cpa-td-amount" title="' + escapeHtml(amountText) + '">' + escapeHtml(amountText) + '</td>' +
                    '<td class="vas-cpa-td-method" title="' + escapeHtml(method) + '">' + escapeHtml(method) + '</td>' +
                    '<td class="vas-cpa-td-reason" title="' + escapeHtml(reason) + '"><span class="vas-cpa-truncate">' + escapeHtml(reason) + '</span></td>' +
                    '</tr>'
                );
            }
        }

        function updatePager() {
            var from = totalRecords === 0 ? 0 : (pageNo - 1) * pageSize + 1;
            var to = Math.min((pageNo - 1) * pageSize + ($dialogTbody ? $dialogTbody.find('tr').not(':has(.vas-cpa-dialog-empty)').length : 0), totalRecords);

            if ($pagerHelper) {
                $pagerHelper.text(totalRecords > 0
                    ? lbl('VAS_Showing', 'Showing') + ' ' + from + '-' + to + ' ' + lbl('VAS_Of', 'of') + ' ' + totalRecords
                    : '');
            }

            if ($pagerText) {
                $pagerText.text(totalPages > 0 ? pageNo + ' ' + lbl('VAS_Of', 'of') + ' ' + totalPages : '');
            }

            updatePagerButtons();
        }

        function updatePagerButtons() {
            if ($pagerPrev) {
                $pagerPrev.prop('disabled', rowsLoading || pageNo <= 1);
            }

            if ($pagerNext) {
                $pagerNext.prop('disabled', rowsLoading || totalPages <= 1 || pageNo >= totalPages);
            }
        }

        function renderData(data) {
            var percentage = Number(data.value);

            if (isNaN(percentage)) {
                percentage = Number(data.clearedPercentage);
            }

            if (isNaN(percentage) || percentage <= 0) {
                showState(true, lbl('VAS_027_messageNoData', 'No Data'));
                return;
            }

            percentage = Math.max(0, Math.min(percentage, 100));

            showState(false, '');
            $value.text(formatPercent(percentage, data.precision));
        }

        function formatPercent(value, precision) {
            var stdPrecision = normalizePrecision(precision);

            return Number(value || 0).toLocaleString(window.navigator.language, {
                minimumFractionDigits: stdPrecision,
                maximumFractionDigits: stdPrecision
            }) + '%';
        }

        function formatCount(value) {
            return Number(value || 0).toLocaleString(window.navigator.language);
        }

        function formatExactAmount(value, precision, symbol) {
            var stdPrecision = normalizePrecision(precision);
            var numberText = Number(value || 0).toLocaleString(window.navigator.language, {
                minimumFractionDigits: stdPrecision,
                maximumFractionDigits: stdPrecision
            });

            return (symbol ? symbol : '') + numberText;
        }

        function formatDate(value) {
            if (!value) {
                return '';
            }

            var d = new Date(value);

            if (isNaN(d.getTime())) {
                return value;
            }

            return d.toLocaleDateString(window.navigator.language, {
                day: '2-digit',
                month: 'short',
                year: 'numeric'
            });
        }

        function formatBankAccount(row) {
            var bankName = row && row.bankName ? String(row.bankName).trim() : '';
            var accountNo = row && row.accountNo ? String(row.accountNo).trim() : '';
            var last4 = accountNo ? (accountNo.length > 4 ? accountNo.slice(-4) : accountNo) : '';

            if (bankName && last4) {
                return bankName + ' ****' + last4;
            }

            if (bankName) {
                return bankName;
            }

            return last4 ? '****' + last4 : '';
        }

        function escapeHtml(value) {
            return String(value == null ? '' : value)
                .replace(/&/g, '&amp;')
                .replace(/</g, '&lt;')
                .replace(/>/g, '&gt;')
                .replace(/"/g, '&quot;')
                .replace(/'/g, '&#039;');
        }

        function normalizePrecision(precision) {
            var stdPrecision = Number(precision);

            if (isNaN(stdPrecision) || stdPrecision < 0) {
                return 2;
            }

            return stdPrecision;
        }

        function showBusy(show) {
            if ($busy) {
                $busy.toggleClass('is-visible', !!show);
            }
        }

        function showDialogBusy(show) {
            if ($dialogBusy) {
                $dialogBusy.toggleClass('is-visible', !!show);
            }
        }

        function showState(show, message) {
            if ($state) {
                $state.text(message || '').toggleClass('is-visible', !!show);
            }

            if ($body) {
                $body.toggle(!show);
            }

            if ($footer) {
                $footer.toggle(!show);
            }
        }

        function openDialog() {
            if (!$dialog) {
                return;
            }

            $dialog.show();
            $('body').addClass('vas-cpa-body-lock');

            if (!rowsLoaded) {
                loadRows();
            }
        }

        function closeDialog() {
            if (!$dialog) {
                return;
            }

            $dialog.hide();
            $('body').removeClass('vas-cpa-body-lock');
            rowsLoaded = false;
            pageNo = 1;
        }

        function reconcileIconSvg() {
            return '<svg viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="1.8" stroke-linecap="round" stroke-linejoin="round" aria-hidden="true">' +
                '<polyline points="23 4 23 10 17 10"></polyline>' +
                '<polyline points="1 20 1 14 7 14"></polyline>' +
                '<path d="M3.51 9a9 9 0 0 1 14.85-3.36L23 10M1 14l4.64 4.36A9 9 0 0 0 20.49 15"></path>' +
                '</svg>';
        }

        function createDialog() {
            $dialog = $(
                '<div class="vas-cpa-dialog" style="display:none;" role="dialog" aria-modal="true">' +
                '<div class="vas-cpa-dialog-scrim"></div>' +
                '<div class="vas-cpa-dialog-card">' +
                '<div class="vas-cpa-dialog-header">' +
                '<div class="vas-cpa-dialog-icon">' + reconcileIconSvg() + '</div>' +
                '<div class="vas-cpa-dialog-title-group">' +
                '<div class="vas-cpa-dialog-title">' + escapeHtml(lbl('VAS_027_messageUnreconciledPayments', 'Unreconciled payments')) + '</div>' +
                '<div class="vas-cpa-dialog-subtitle"></div>' +
                '</div>' +
                '<button type="button" class="vas-cpa-dialog-close" aria-label="' + escapeHtml(lbl('VAS_Close', 'Close')) + '">' +
                '<svg viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="1.8" stroke-linecap="round" stroke-linejoin="round"><line x1="18" y1="6" x2="6" y2="18"/><line x1="6" y1="6" x2="18" y2="18"/></svg>' +
                '</button>' +
                '</div>' +
                '<div class="vas-cpa-summary">' +
                '<div><span>' + escapeHtml(lbl('VAS_027_messageUnreconciled', 'Unreconciled')) + '</span><strong class="vas-cpa-summary-count">0</strong></div>' +
                '<div><span>' + escapeHtml(lbl('VAS_027_messageAmount', 'Amount')) + '</span><strong class="vas-cpa-summary-amount">0</strong></div>' +
                '<div><span>' + escapeHtml(lbl('VAS_027_messageOldest', 'Oldest')) + '</span><strong class="vas-cpa-summary-oldest">0</strong></div>' +
                '<div><span>' + escapeHtml(lbl('VAS_027_messageAutoMatchRate', 'Auto-match rate')) + '</span><strong class="vas-cpa-summary-rate">0%</strong></div>' +
                '</div>' +
                '<div class="vas-cpa-dialog-body">' +
                '<div class="vas-cpa-dialog-busy"><div class="vis-busyindicatorinnerwrap"><i class="vis_widgetloader"></i></div></div>' +
                '<table class="vas-cpa-dialog-table">' +
                '<thead><tr>' +
                '<th>' + escapeHtml(lbl('VAS_027_messagePaymentNo', 'Payment No.')) + '</th>' +
                '<th>' + escapeHtml(lbl('VAS_027_messageDate', 'Date')) + '</th>' +
                '<th>' + escapeHtml(lbl('VAS_027_messageVendor', 'Vendor')) + '</th>' +
                '<th>' + escapeHtml(lbl('VAS_027_messageBankAccount', 'Bank Account')) + '</th>' +
                '<th>' + escapeHtml(lbl('VAS_027_messageCurrency', 'Currency')) + '</th>' +
                '<th>' + escapeHtml(lbl('VAS_027_messageAmount', 'Amount')) + '</th>' +
                '<th>' + escapeHtml(lbl('VAS_027_messageMethod', 'Method')) + '</th>' +
                '<th>' + escapeHtml(lbl('VAS_027_messageWhyUnreconciled', 'Why Unreconciled')) + '</th>' +
                '</tr></thead>' +
                '<tbody class="vas-cpa-dialog-tbody"></tbody>' +
                '</table>' +
                '</div>' +
                '<div class="vas-cpa-dialog-footer">' +
                '<span class="vas-cpa-pager-helper"></span>' +
                '<div class="vas-cpa-dialog-actions">' +
                '<button type="button" class="vas-cpa-action vas-cpa-auto-match">' + escapeHtml(lbl('VAS_027_messageAutoMatchRemaining', 'Auto-match remaining')) + '</button>' +
                '<button type="button" class="vas-cpa-action vas-cpa-action-primary vas-cpa-open-reconciliation">' + escapeHtml(lbl('VAS_027_messageOpenReconciliation', 'Open reconciliation')) + '</button>' +
                '</div>' +
                '<div class="vas-cpa-pager">' +
                '<button type="button" class="vas-cpa-pager-btn vas-cpa-pager-prev" aria-label="' + escapeHtml(lbl('VAS_Previous', 'Previous')) + '">' +
                '<svg viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2" stroke-linecap="round" stroke-linejoin="round"><polyline points="15 18 9 12 15 6"/></svg>' +
                '</button>' +
                '<span class="vas-cpa-pager-text"></span>' +
                '<button type="button" class="vas-cpa-pager-btn vas-cpa-pager-next" aria-label="' + escapeHtml(lbl('VAS_Next', 'Next')) + '">' +
                '<svg viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2" stroke-linecap="round" stroke-linejoin="round"><polyline points="9 18 15 12 9 6"/></svg>' +
                '</button>' +
                '</div>' +
                '</div>' +
                '</div>' +
                '</div>'
            );

            $dialogTbody = $dialog.find('.vas-cpa-dialog-tbody');
            $dialogBusy = $dialog.find('.vas-cpa-dialog-busy');
            $dialogSubtitle = $dialog.find('.vas-cpa-dialog-subtitle');
            $summaryCount = $dialog.find('.vas-cpa-summary-count');
            $summaryAmount = $dialog.find('.vas-cpa-summary-amount');
            $summaryOldest = $dialog.find('.vas-cpa-summary-oldest');
            $summaryRate = $dialog.find('.vas-cpa-summary-rate');
            $pagerHelper = $dialog.find('.vas-cpa-pager-helper');
            $pagerPrev = $dialog.find('.vas-cpa-pager-prev');
            $pagerNext = $dialog.find('.vas-cpa-pager-next');
            $pagerText = $dialog.find('.vas-cpa-pager-text');

            $dialog.find('.vas-cpa-dialog-close, .vas-cpa-dialog-scrim').on('click', closeDialog);
            $pagerPrev.on('click', function () {
                if (rowsLoading || pageNo <= 1) {
                    return;
                }

                pageNo--;
                loadRows();
            });
            $pagerNext.on('click', function () {
                if (rowsLoading || pageNo >= totalPages) {
                    return;
                }

                pageNo++;
                loadRows();
            });
            $dialog.find('.vas-cpa-auto-match').on('click', function () {
                rowsLoaded = false;
                loadRows();
            });
            $dialog.find('.vas-cpa-open-reconciliation').on('click', function () {
                if (VIS.ADialog && VIS.ADialog.info) {
                    VIS.ADialog.info(lbl('VAS_027_messageOpenReconciliationInfo', 'Open payment reconciliation from the reconciliation window.'));
                }
            });

            $(document).on('keydown.vas-cpa', function (e) {
                if (e.key === 'Escape' && $dialog && $dialog.is(':visible')) {
                    closeDialog();
                }
            });

            $('body').append($dialog);
        }

        this.Initalize = function () {
            createWidget();
            loadData();
        };

        this.refreshData = function () {
            loadData();
        };

        this.getRoot = function () {
            return $root;
        };

        this.disposeComponent = function () {
            isDisposed = true;
            $(document).off('keydown.vas-cpa');
            $('body').removeClass('vas-cpa-body-lock');

            if ($dialog) {
                $dialog.remove();
            }

            $root.remove();

            $card = null;
            $value = null;
            $body = null;
            $footer = null;
            $busy = null;
            $state = null;
            $dialog = null;
            $dialogTbody = null;
            $dialogBusy = null;
        };
    };

    VAS.VAS_027_ClearedAPPaymentWidget = VAS_027_ClearedAPPaymentWidget;

    VAS.VAS_027_ClearedAPPaymentWidget.prototype.init = function (windowNo, frame) {
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

    VAS.VAS_027_ClearedAPPaymentWidget.prototype.widgetSizeChange = function (height, width) {
        var $root = this.getRoot();

        if (!$root) {
            return;
        }

        $root.toggleClass(
            'vas-finance-kpi-compact',
            (width && width < 240) || (height && height < 160)
        );
    };

    VAS.VAS_027_ClearedAPPaymentWidget.prototype.refreshWidget = function () {
        this.refreshData();
    };

    VAS.VAS_027_ClearedAPPaymentWidget.prototype.dispose = function () {
        this.disposeComponent();

        if (this.frame && this.frame.dispose) {
            this.frame.dispose();
        }

        this.frame = null;
    };

})(VAS, jQuery);
