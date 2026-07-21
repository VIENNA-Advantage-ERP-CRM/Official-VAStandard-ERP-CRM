/**

* Scheduled
* Purpose - Displays AP invoice outstanding due amounts scheduled for payment during the current week, grouped by payment method.
*
* ── Labels / Message Keys ─────────────────────────────────────────────
* # | Current Text                         | Message Key
* ----+--------------------------------------+--------------------------------
* 1  | Due This Week                        | VAS_029_MessageScheduled
* 2  | Scheduled for payment this week      | VAS_029_ScheduledForPaymentThisWeek
* 3  | Loading                              | VAS_029_MessageLoading
* 4  | No Data                              | VAS_029_MessageNoData
* 5  | Could not load data                  | VAS_ErrorLoading
* 6  | Not Specified                        | VAS_029_MessageNotSpecified
* 7  | invoices                             | VAS_029_MessageInvoices
* 8  | Showing                              | VAS_Showing
* 9  | Of                                   | VAS_Of
* 10  | Close                                | VAS_Close
* 11  | Total due                            | VAS_029_MessageTotalDue
* 12  | Vendors                              | VAS_029_MessageVendors
* 13  | Payment methods                      | VAS_029_MessagePaymentMethods
* 14  | Invoice No.                          | VIS_InvoiceNo
* 15  | Invoice date                         | VIS_InvoiceDate
* 16  | Vendor                               | VAS_029_MessageVendor
* 17  | Due date                             | VIS_DueDate
* 18  | Currency                             | VAS_PaymentCurrency
* 19  | Amount                               | VAS_029_MessageAmount
* 20  | Method                               | VAS_029_MessageMethod
* 21  | Previous                             | VAS_Previous
* 22  | Next                                 | VAS_Next
* 23  | Payments due this week               | VAS_029_MessagePaymentsDueThisWeek
* ─────────────────────────────────────────────────────────────────────
  */



; VAS = window.VAS || {};

; (function (VAS, $) {
    "use strict";

    VAS.VAS_029_ScheduledAPPaymentWidget = function () {
        var self = this;

        this.frame = null;
        this.windowNo = 0;
        this.AD_UserHomeWidgetID = 0;

        var $root = $('<div class="vas-scheduled-ap-payment-root">');
        var $card = null;
        var $value = null;
        var $description = null;
        var $body = null;
        var $footer = null;
        var $busy = null;
        var $state = null;
        var $dialog = null;
        var $dialogTitle = null;
        var $dialogSubtitle = null;
        var $dialogTbody = null;
        var $dialogBusy = null;
        var $summaryTotal = null;
        var $summaryInvoices = null;
        var $summaryVendors = null;
        var $summaryMethods = null;
        var $pagerHelper = null;
        var $pagerPrev = null;
        var $pagerNext = null;
        var $pagerText = null;

        var isDisposed = false;
        var rowsLoaded = false;
        var rowsLoading = false;
        var pageNo = 1;
        var pageSize = 8;
        var totalPages = 0;
        var totalRecords = 0;
        var lastData = null;

        function lbl(key, fallback) {
            var text = VIS.Msg.getMsg(key);
            return text && text !== '[' + key + ']' ? text : fallback;
        }

        this.Initalize = function () {
            createWidget();
            loadData();
        };

        function createWidget() {
            $card = $('<div class="vas-scheduled-ap-payment-card">');
            $card.attr({
                role: 'button',
                tabindex: '0'
            });

            var $header = $('<div class="vas-scheduled-ap-payment-header">');
            var $iconBox = $('<div class="vas-scheduled-ap-payment-icon-box">');
            var $icon = $(
                '<svg class="vas-scheduled-ap-payment-icon" viewBox="0 0 24 24" fill="none" aria-hidden="true" focusable="false">' +
                '<circle cx="12" cy="12" r="10"></circle>' +
                '<polyline points="12 6 12 12 16 14"></polyline>' +
                '</svg>'
            );

            var $title = $('<div class="vas-scheduled-ap-payment-title">').text(
                lbl('VAS_029_MessageScheduled', 'Due This Week')
            );

            $iconBox.append($icon);
            $header.append($iconBox).append($title);

            $body = $('<div class="vas-scheduled-ap-payment-body">');
            $value = $('<div class="vas-scheduled-ap-payment-value">');
            $body.append($value);

            $footer = $('<div class="vas-scheduled-ap-payment-footer">');


            $description = $('<div class="vas-scheduled-ap-payment-desc">');

            $footer.append($description);
            $busy = $('<div class="vas-scheduled-ap-payment-busy"><div class="vis-busyindicatorinnerwrap"><i class="vis_widgetloader"></i></div></div>');
            $state = $('<div class="vas-scheduled-ap-payment-state-message">');

            $card.append($header).append($body).append($footer).append($busy).append($state);
            $root.empty().append($card);

            $card.on('click', function () {
                openDialog();
            });

            $card.on('keydown', function (e) {
                if (e.key === 'Enter' || e.key === ' ') {
                    e.preventDefault();
                    openDialog();
                }
            });

            createDialog();
        }

        function loadData() {
            if (isDisposed) {
                return;
            }

            showBusy(true);
            showState(false, '');

            $.ajax({
                url: VIS.Application.contextUrl + 'VAS_029_ScheduledAPPaymentWidget/GetScheduledAPPaymentThisWeek',
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

        function escapeHtml(value) {
            return String(value == null ? '' : value)
                .replace(/&/g, '&amp;')
                .replace(/</g, '&lt;')
                .replace(/>/g, '&gt;')
                .replace(/"/g, '&quot;')
                .replace(/'/g, '&#039;');
        }

        function renderData(data) {
            lastData = data || {};

            var totalAmount = Number(data.value);

            if (isNaN(totalAmount)) {
                totalAmount = Number(data.scheduledAmountThisWeek);
            }

            if (isNaN(totalAmount) || totalAmount <= 0) {
                setNoData();
                return;
            }

            showState(false, '');

            $value.text(formatWidgetCompactAmount(
                totalAmount,
                data.currencySymbol || data.symbol,
                data.currencyISO || data.currencyISOCode || data.isoCode,
                data.precision
            ));

            $description.text(
                data.description ||
                lbl(
                    'VAS_029_ScheduledForPaymentThisWeek',
                    'Scheduled for payment this week'
                )
            );

            refreshOpenDialog();
        }

        function refreshOpenDialog() {
            if ($dialog && $dialog.is(':visible')) {
                rowsLoaded = false;
                pageNo = 1;
                loadRows();
            }
        }

        function loadRows() {
            if (!$dialogTbody || isDisposed || rowsLoading) {
                return;
            }

            rowsLoading = true;
            showDialogBusy(true);
            updatePagerControls(0, 0);

            $.ajax({
                url: VIS.Application.contextUrl + 'VAS_029_ScheduledAPPaymentWidget/GetScheduledAPPaymentRows',
                type: 'GET',
                dataType: 'json',
                cache: false,
                data: {
                    pageNo: pageNo,
                    pageSize: pageSize
                },
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
                        updatePagerControls(
                            totalRecords === 0 ? 0 : (pageNo - 1) * pageSize,
                            Math.min((pageNo - 1) * pageSize + ($dialogTbody ? $dialogTbody.find('tr').not('.vas-scheduled-ap-payment-dialog-empty-row').length : 0), totalRecords)
                        );
                    }
                }
            });
        }

        function renderPageResult(data) {
            var rows = data && $.isArray(data.rows) ? data.rows : [];

            totalRecords = Number((data && data.totalRecords) || 0);
            totalPages = Number((data && data.totalPages) || 0);

            if (data && typeof data.pageNo !== 'undefined') {
                pageNo = Number(data.pageNo);
            }

            if (pageNo > totalPages && totalPages > 0) {
                pageNo = totalPages;
            }

            if (pageNo < 1) {
                pageNo = 1;
            }

            renderRows(rows);
            renderDialogSummary(data || {});

            var from = totalRecords === 0 ? 0 : (pageNo - 1) * pageSize;
            var to = Math.min(from + rows.length, totalRecords);

            updatePagerControls(from, to);
        }

        function renderRows(rows) {
            if (!$dialogTbody) {
                return;
            }

            $dialogTbody.empty();

            if (!rows || rows.length === 0) {
                $dialogTbody.html(
                    '<tr class="vas-scheduled-ap-payment-dialog-empty-row">' +
                    '<td class="vas-scheduled-ap-payment-dialog-empty" colspan="7">' +
                    escapeHtml(lbl('VAS_029_MessageNoData', 'No Data')) +
                    '</td>' +
                    '</tr>'
                );
                return;
            }

            for (var i = 0; i < rows.length; i++) {
                var row = rows[i] || {};
                var docNo = row.documentNo || '';
                var invoiceDateText = formatDate(row.invoiceDate);
                var vendor = row.vendor || '';
                var dueDateText = formatDate(row.dueDate);
                var invoiceCurrency = row.invoiceCurrency || '';
                var sym = row.invoiceCurrencySymbol || invoiceCurrency || '';
                var precision = normalizePrecision(row.precision);
                var amountText = formatAmount(Math.abs(Number(row.amount || 0)), precision);
                var sign = Number(row.amount || 0) < 0 ? '-' : '';
                var amountHtml = sign + (sym ? '<span class="vas-scheduled-ap-payment-cur-inline">' + escapeHtml(sym) + '</span>' : '') + escapeHtml(amountText);
                var paymentMethodName = row.paymentMethodName || lbl('VAS_029_MessageNotSpecified', 'Not Specified');

                $dialogTbody.append(
                    '<tr>' +
                    '<td class="vas-scheduled-ap-payment-td-doc" title="' + escapeHtml(docNo) + '">' +
                    '<span class="vas-scheduled-ap-payment-truncate">' + escapeHtml(docNo) + '</span>' +
                    '</td>' +
                    '<td class="vas-scheduled-ap-payment-td-invdate" title="' + escapeHtml(invoiceDateText) + '">' + escapeHtml(invoiceDateText) + '</td>' +
                    '<td class="vas-scheduled-ap-payment-td-vendor" title="' + escapeHtml(vendor) + '">' +
                    '<span class="vas-scheduled-ap-payment-truncate">' + escapeHtml(vendor) + '</span>' +
                    '</td>' +
                    '<td class="vas-scheduled-ap-payment-td-duedate" title="' + escapeHtml(dueDateText) + '">' + escapeHtml(dueDateText) + '</td>' +
                    '<td class="vas-scheduled-ap-payment-td-currency" title="' + escapeHtml(invoiceCurrency) + '">' + escapeHtml(invoiceCurrency) + '</td>' +
                    '<td class="vas-scheduled-ap-payment-td-method" title="' + escapeHtml(paymentMethodName) + '">' +
                    '<span class="vas-scheduled-ap-payment-truncate">' + escapeHtml(paymentMethodName) + '</span>' +
                    '</td>' +
                    '<td class="vas-scheduled-ap-payment-td-amount" title="' + escapeHtml((sym ? sym + ' ' : '') + amountText) + '">' + amountHtml + '</td>' +
                    '</tr>'
                );
            }
        }

        function renderDialogSummary(data) {
            var totalAmount = Number((lastData && (lastData.value || lastData.scheduledAmountThisWeek)) || 0);
            var symbol = (lastData && (lastData.currencySymbol || lastData.symbol)) || '';
            var precision = normalizePrecision(lastData && lastData.precision);
            var vendorCount = Number((data && data.vendorCount) || 0);
            var methodCount = Number((data && data.paymentMethodCount) || 0);

            if ($summaryTotal) {
                $summaryTotal.text(formatCurrencyAmount(totalAmount, symbol, lastData && lastData.currencyISO, precision));
            }

            if ($summaryInvoices) {
                $summaryInvoices.text(totalRecords.toLocaleString(window.navigator.language));
            }

            if ($summaryVendors) {
                $summaryVendors.text(vendorCount.toLocaleString(window.navigator.language));
            }

            if ($summaryMethods) {
                $summaryMethods.text(methodCount.toLocaleString(window.navigator.language));
            }

            if ($dialogSubtitle) {
                var datePart = '';

                if (lastData && lastData.dateFrom && lastData.dateTo) {
                    datePart = formatDate(lastData.dateFrom) + ' - ' + formatDate(lastData.dateTo) + ' · ';
                }

                $dialogSubtitle.text(
                    datePart +
                    totalRecords.toLocaleString(window.navigator.language) + ' ' +
                    lbl('VAS_029_MessageInvoices', 'invoices') +
                    ' · ' +
                    formatCurrencyAmount(totalAmount, symbol, lastData && lastData.currencyISO, precision)
                );
            }
        }

        function updatePagerControls(from, to) {
            if ($pagerHelper) {
                if (totalRecords > 0) {
                    $pagerHelper.text(
                        lbl('VAS_Showing', 'Showing') + ' ' +
                        (from + 1) + '-' + to + ' ' +
                        lbl('VAS_Of', 'of') + ' ' +
                        totalRecords + ' ' +
                        lbl('VAS_029_MessageInvoices', 'invoices')
                    );
                }
                else {
                    $pagerHelper.text('');
                }
            }

            if ($pagerText) {
                $pagerText.text(totalPages > 0 ? (pageNo + ' ' + lbl('VAS_Of', 'of') + ' ' + totalPages) : '');
            }

            if ($pagerPrev) {
                $pagerPrev.prop('disabled', rowsLoading || pageNo <= 1);
            }

            if ($pagerNext) {
                $pagerNext.prop('disabled', rowsLoading || totalPages <= 1 || pageNo >= totalPages);
            }
        }

        function openDialog() {
            if (!$dialog || ($state && $state.hasClass('is-visible'))) {
                return;
            }

            $dialog.show();
            $('body').addClass('vas-scheduled-ap-payment-body-lock');

            if (!rowsLoaded) {
                pageNo = 1;
                loadRows();
            }
        }

        function closeDialog() {
            if (!$dialog) {
                return;
            }

            $dialog.hide();
            $('body').removeClass('vas-scheduled-ap-payment-body-lock');
            pageNo = 1;
            rowsLoaded = false;
        }

        function createDialog() {
            $dialog = $(
                '<div class="vas-scheduled-ap-payment-dialog" style="display:none;" role="dialog" aria-modal="true">' +
                '<div class="vas-scheduled-ap-payment-dialog-scrim"></div>' +
                '<div class="vas-scheduled-ap-payment-dialog-card">' +

                '<div class="vas-scheduled-ap-payment-dialog-header">' +
                '<div class="vas-scheduled-ap-payment-dialog-icon">' +
                '<svg viewBox="0 0 24 24" fill="none" aria-hidden="true" focusable="false">' +
                '<circle cx="12" cy="12" r="10"></circle>' +
                '<polyline points="12 6 12 12 16 14"></polyline>' +
                '</svg>' +
                '</div>' +
                '<div class="vas-scheduled-ap-payment-dialog-title-group">' +
                '<div class="vas-scheduled-ap-payment-dialog-title"></div>' +
                '<div class="vas-scheduled-ap-payment-dialog-subtitle"></div>' +
                '</div>' +
                '<button type="button" class="vas-scheduled-ap-payment-dialog-close" aria-label="' + escapeHtml(lbl('VAS_Close', 'Close')) + '">' +
                '<svg viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="1.8" stroke-linecap="round" stroke-linejoin="round">' +
                '<line x1="18" y1="6" x2="6" y2="18"></line>' +
                '<line x1="6" y1="6" x2="18" y2="18"></line>' +
                '</svg>' +
                '</button>' +
                '</div>' +

                '<div class="vas-scheduled-ap-payment-dialog-summary">' +
                '<div><span>' + escapeHtml(lbl('VAS_029_MessageTotalDue', 'Total due')) + '</span><strong class="vas-scheduled-ap-payment-summary-total">-</strong></div>' +
                '<div><span>' + escapeHtml(lbl('VAS_029_MessageInvoices', 'Invoices')) + '</span><strong class="vas-scheduled-ap-payment-summary-invoices">-</strong></div>' +
                '<div><span>' + escapeHtml(lbl('VAS_029_MessageVendors', 'Vendors')) + '</span><strong class="vas-scheduled-ap-payment-summary-vendors">-</strong></div>' +
                '<div><span>' + escapeHtml(lbl('VAS_029_MessagePaymentMethods', 'Payment methods')) + '</span><strong class="vas-scheduled-ap-payment-summary-methods">-</strong></div>' +
                '</div>' +

                '<div class="vas-scheduled-ap-payment-dialog-body">' +
                '<div class="vas-scheduled-ap-payment-dialog-busy"><div class="vis-busyindicatorinnerwrap"><i class="vis_widgetloader"></i></div></div>' +
                '<table class="vas-scheduled-ap-payment-dialog-table">' +
                '<thead>' +
                '<tr>' +
                '<th class="vas-scheduled-ap-payment-th-doc">' + escapeHtml(lbl('VIS_InvoiceNo', 'Invoice No.')) + '</th>' +
                '<th class="vas-scheduled-ap-payment-th-invdate">' + escapeHtml(lbl('VIS_InvoiceDate', 'Invoice date')) + '</th>' +
                '<th class="vas-scheduled-ap-payment-th-vendor">' + escapeHtml(lbl('VAS_029_MessageVendor', 'Vendor')) + '</th>' +
                '<th class="vas-scheduled-ap-payment-th-duedate">' + escapeHtml(lbl('VIS_DueDate', 'Due date')) + '</th>' +
                '<th class="vas-scheduled-ap-payment-th-currency">' + escapeHtml(lbl('VAS_PaymentCurrency', 'Currency')) + '</th>' +
                '<th class="vas-scheduled-ap-payment-th-method">' + escapeHtml(lbl('VAS_029_MessageMethod', 'Method')) + '</th>' +
                '<th class="vas-scheduled-ap-payment-th-amount">' + escapeHtml(lbl('VAS_029_MessageAmount', 'Amount')) + '</th>' +
                '</tr>' +
                '</thead>' +
                '<tbody class="vas-scheduled-ap-payment-dialog-tbody"></tbody>' +
                '</table>' +
                '</div>' +

                '<div class="vas-scheduled-ap-payment-dialog-footer">' +
                '<span class="vas-scheduled-ap-payment-pager-helper"></span>' +
                '<div class="vas-scheduled-ap-payment-pager">' +
                '<button type="button" class="vas-scheduled-ap-payment-pager-btn vas-scheduled-ap-payment-pager-prev" aria-label="' + escapeHtml(lbl('VAS_Previous', 'Previous')) + '">' +
                '<svg viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2" stroke-linecap="round" stroke-linejoin="round"><polyline points="15 18 9 12 15 6"></polyline></svg>' +
                '</button>' +
                '<span class="vas-scheduled-ap-payment-pager-text"></span>' +
                '<button type="button" class="vas-scheduled-ap-payment-pager-btn vas-scheduled-ap-payment-pager-next" aria-label="' + escapeHtml(lbl('VAS_Next', 'Next')) + '">' +
                '<svg viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2" stroke-linecap="round" stroke-linejoin="round"><polyline points="9 18 15 12 9 6"></polyline></svg>' +
                '</button>' +
                '</div>' +
                '</div>' +

                '</div>' +
                '</div>'
            );

            $dialogTitle = $dialog.find('.vas-scheduled-ap-payment-dialog-title');
            $dialogSubtitle = $dialog.find('.vas-scheduled-ap-payment-dialog-subtitle');
            $dialogTbody = $dialog.find('.vas-scheduled-ap-payment-dialog-tbody');
            $dialogBusy = $dialog.find('.vas-scheduled-ap-payment-dialog-busy');
            $summaryTotal = $dialog.find('.vas-scheduled-ap-payment-summary-total');
            $summaryInvoices = $dialog.find('.vas-scheduled-ap-payment-summary-invoices');
            $summaryVendors = $dialog.find('.vas-scheduled-ap-payment-summary-vendors');
            $summaryMethods = $dialog.find('.vas-scheduled-ap-payment-summary-methods');
            $pagerHelper = $dialog.find('.vas-scheduled-ap-payment-pager-helper');
            $pagerPrev = $dialog.find('.vas-scheduled-ap-payment-pager-prev');
            $pagerNext = $dialog.find('.vas-scheduled-ap-payment-pager-next');
            $pagerText = $dialog.find('.vas-scheduled-ap-payment-pager-text');

            if ($dialogTitle) {
                $dialogTitle.text(lbl('VAS_029_MessagePaymentsDueThisWeek', 'Payments due this week'));
            }

            if ($dialogBusy && $dialogBusy[0]) {
                $dialogBusy[0].style.visibility = 'hidden';
            }

            $dialog.find('.vas-scheduled-ap-payment-dialog-close').on('click', function (e) {
                e.stopPropagation();
                closeDialog();
            });

            $dialog.find('.vas-scheduled-ap-payment-dialog-scrim').on('click', function () {
                closeDialog();
            });

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

            $(document).on('keydown.vas-scheduled-ap-payment-dialog-' + self.AD_UserHomeWidgetID, function (e) {
                if (e.key === 'Escape' && $dialog && $dialog.is(':visible')) {
                    closeDialog();
                }
            });

            $('body').append($dialog);
        }

        function showDialogBusy(show) {
            if (!$dialogBusy || !$dialogBusy[0]) {
                return;
            }

            $dialogBusy.toggleClass('is-visible', !!show);
            $dialogBusy[0].style.visibility = show ? 'visible' : 'hidden';
        }

        function formatDate(value) {
            if (!value) {
                return '';
            }

            var date = new Date(value);

            if (isNaN(date.getTime())) {
                return value;
            }

            return date.toLocaleDateString(window.navigator.language, {
                day: '2-digit',
                month: 'short',
                year: 'numeric'
            });
        }

        function formatCurrencyAmount(value, currencySymbol, currencyISO, precision) {
            var numericValue = Number(value || 0);
            var sign = numericValue < 0 ? '-' : '';
            var absValue = Math.abs(numericValue);
            var amount = formatAmount(absValue, normalizePrecision(precision));

            if (currencySymbol) {
                return sign + currencySymbol + amount;
            }

            return currencyISO ? sign + amount + ' ' + currencyISO : sign + amount;
        }

        function formatWidgetCompactAmount(value, currencySymbol, currencyISO, precision) {
            var numericValue = Number(value || 0);
            var sign = numericValue < 0 ? '-' : '';
            var stdPrecision = normalizePrecision(precision);
            var amount =
                VIS &&
                    VIS.Util &&
                    VIS.Util.formatCompactAmount
                    ? VIS.Util.formatCompactAmount(
                        Math.abs(numericValue),
                        currencyISO,
                        stdPrecision
                    )
                    : formatAmount(
                        Math.abs(numericValue),
                        stdPrecision
                    );

            if (currencySymbol) {
                return sign + currencySymbol + amount;
            }

            return currencyISO ? sign + amount + ' ' + currencyISO : sign + amount;
        }

        function formatAmount(value, precision) {
            var numericValue = Number(value || 0);

            return numericValue.toLocaleString(window.navigator.language, {
                minimumFractionDigits: precision,
                maximumFractionDigits: precision
            });
        }

        function normalizePrecision(precision) {
            var stdPrecision = Number(precision);

            if (!isNaN(stdPrecision) && stdPrecision >= 0) {
                return stdPrecision;
            }

            if (VIS && VIS.Env && VIS.Env.getCtx && VIS.Env.getCtx().getStdPrecision) {
                stdPrecision = Number(VIS.Env.getCtx().getStdPrecision());
            }

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

        function setNoData() {
            if ($description) {
                $description.text('');
            }

            showState(true, lbl('VAS_029_MessageNoData', 'No Data'));
        }

        this.refreshData = function () {
            rowsLoaded = false;
            rowsLoading = false;
            pageNo = 1;
            totalPages = 0;
            totalRecords = 0;
            loadData();
        };

        this.getRoot = function () {
            return $root;
        };

        this.disposeComponent = function () {
            isDisposed = true;
            $(document).off('keydown.vas-scheduled-ap-payment-dialog-' + self.AD_UserHomeWidgetID);
            $('body').removeClass('vas-scheduled-ap-payment-body-lock');

            if ($dialog) {
                $dialog.remove();
                $dialog = null;
            }

            $root.remove();

            $card = null;
            $value = null;
            $description = null;
            $body = null;
            $footer = null;
            $busy = null;
            $state = null;
            $dialogTitle = null;
            $dialogSubtitle = null;
            $dialogTbody = null;
            $dialogBusy = null;
            $summaryTotal = null;
            $summaryInvoices = null;
            $summaryVendors = null;
            $summaryMethods = null;
            $pagerHelper = null;
            $pagerPrev = null;
            $pagerNext = null;
            $pagerText = null;
            lastData = null;
        };
    };

    VAS.VAS_029_ScheduledAPPaymentWidget.prototype.init = function (windowNo, frame) {
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

    VAS.VAS_029_ScheduledAPPaymentWidget.prototype.widgetSizeChange = function (height, width) {
        var $root = this.getRoot();

        if (!$root) {
            return;
        }

        $root.toggleClass(
            'vas-scheduled-ap-payment-compact',
            (width && width < 240) || (height && height < 160)
        );
    };

    VAS.VAS_029_ScheduledAPPaymentWidget.prototype.refreshWidget = function () {
        this.refreshData();
    };

    VAS.VAS_029_ScheduledAPPaymentWidget.prototype.dispose = function () {
        this.disposeComponent();

        if (this.frame && this.frame.dispose) {
            this.frame.dispose();
        }

        this.frame = null;
    };

})(VAS, jQuery);
