/**
 * VAS_028_PaidThisMonthAPPaymentWidget
 *
 * ── Labels / Message Keys ─────────────────────────────────────────────────────
 *  #  | Current Text                                      | Message Key
 * ----+---------------------------------------------------+------------------------------
 *  1  | Paid this month                                   | VAS_028_MessagePaidThisMonth
 *  2  | Loading                                           | VAS_028_MessageLoading
 *  3  | Could not load data                               | VAS_ErrorLoading
 *  4  | vendor                                            | VAS_028_MessageVendor
 *  5  | vendors                                           | VAS_028_MessageVendors
 *  6  | Paid to                                           | VAS_028_MessagePaidTo
 *  7  | so far this month.                                | VAS_028_MessageSoFarThisMonth
 *  8  | No payments this month.                           | VAS_028_MessageNoPaymentsThisMonth
 *  9  | No Data                                           | VAS_028_MessageNoData
 * 10  | Cash paid                                         | VAS_028_MessageCashPaid
 * 11  | payments                                          | VAS_028_MessagePayments
 * 12  | Not Specified                                     | VAS_028_MessageNotSpecified
 * 13  | Showing                                           | VAS_Showing
 * 14  | of                                                | VAS_Of
 * 15  | Cleared                                           | VAS_028_MessageCleared
 * 16  | Bounced                                           | VAS_028_MessageBounced
 * 17  | In transit                                        | VAS_028_MessageInTransit
 * 18  | Close                                             | VAS_Close
 * 19  | Total paid                                        | VAS_028_MessageTotalPaid
 * 20  | Avg ticket                                        | VAS_028_MessageAvgTicket
 * 21  | Largest                                           | VAS_028_MessageLargest
 * 22  | Payment No.                                       | VAS_028_MessagePaymentNo
 * 23  | Date                                              | VAS_028_MessageDate
 * 24  | Vendor                                            | VAS_028_MessageVendor
 * 25  | Bank account                                      | VAS_028_BankAccount
 * 26  | Currency                                          | VAS_PaymentCurrency
 * 27  | Amount                                            | VAS_028_MessageAmount
 * 28  | Method                                            | VAS_028_MessageMethod
 * 29  | Status                                            | VAS_028_MessageStatus
 * 30  | Previous                                          | VAS_Previous
 * 31  | Next                                              | VAS_Next
 * ──────────────────────────────────────────────────────────────────────────────
 */

/*
 * Labels / Message Keys
 * #  | Current Text                                      | Message Key
 * ---+---------------------------------------------------+------------------------------
 * 1  | Paid this month                                   | VAS_028_MessagePaidThisMonth
 * 2  | Loading                                           | VAS_028_MessageLoading
 * 3  | Could not load data                               | VAS_ErrorLoading
 * 4  | vendor                                            | VAS_028_MessageVendor
 * 5  | vendors                                           | VAS_028_MessageVendors
 * 6  | Paid to                                           | VAS_028_MessagePaidTo
 * 7  | so far this month.                                | VAS_028_MessageSoFarThisMonth
 * 8  | No payments this month.                           | VAS_028_MessageNoPaymentsThisMonth
 * 9  | No Data                                           | VAS_028_MessageNoData
 * 10 | Cash paid                                         | VAS_028_MessageCashPaid
 * 11 | payments                                          | VAS_028_MessagePayments
 * 12 | Not Specified                                     | VAS_028_MessageNotSpecified
 * 13 | Showing                                           | VAS_Showing
 * 14 | of                                                | VAS_Of
 * 15 | Cleared                                           | VAS_028_MessageCleared
 * 16 | Bounced                                           | VAS_028_MessageBounced
 * 17 | In transit                                        | VAS_028_MessageInTransit
 * 18 | Close                                             | VAS_Close
 * 19 | Total paid                                        | VAS_028_MessageTotalPaid
 * 20 | Avg ticket                                        | VAS_028_MessageAvgTicket
 * 21 | Largest                                           | VAS_028_MessageLargest
 * 22 | Payment No.                                       | VAS_028_MessagePaymentNo
 * 23 | Date                                              | VAS_028_MessageDate
 * 24 | Vendor                                            | VAS_028_MessageVendor
 * 25 | Bank account                                      | VAS_028_BankAccount
 * 26 | Currency                                          | VAS_PaymentCurrency
 * 27 | Amount                                            | VAS_028_MessageAmount
 * 28 | Method                                            | VAS_028_MessageMethod
 * 29 | Status                                            | VAS_028_MessageStatus
 * 30 | Previous                                          | VAS_Previous
 * 31 | Next                                              | VAS_Next
 */

; VAS = window.VAS || {};

; (function (VAS, $) {
    "use strict";

    VAS.VAS_028_PaidThisMonthAPPaymentWidget = function () {
        var self = this;

        this.frame = null;
        this.windowNo = 0;
        this.AD_UserHomeWidgetID = 0;

        var $root = $('<div class="vas-ptm-root vas-ptm-ap-root">');

        var $metricEl = null;
        var $whyText = null;
        var $body = null;
        var $why = null;
        var $busy = null;
        var $state = null;

        var $dialog = null;
        var $dialogTitle = null;
        var $dialogSubtitle = null;
        var $dialogTbody = null;
        var $dialogBusy = null;

        var $pagerHelper = null;
        var $pagerPrev = null;
        var $pagerNext = null;
        var $pagerText = null;

        var $summaryTotal = null;
        var $summaryCount = null;
        var $summaryAvg = null;
        var $summaryLargest = null;

        var isDisposed = false;
        var rowsLoaded = false;
        var rowsLoading = false;

        var pageNo = 1;
        var pageSize = 10;
        var totalPages = 0;
        var totalRecords = 0;

        var lastData = null;

        function lbl(key, fallback) {
            var text = VIS.Msg.getMsg(key);

            return text && text !== '[' + key + ']'
                ? text
                : fallback;
        }

        function escapeHtml(value) {
            return String(value == null ? '' : value)
                .replace(/&/g, '&amp;')
                .replace(/</g, '&lt;')
                .replace(/>/g, '&gt;')
                .replace(/"/g, '&quot;')
                .replace(/'/g, '&#039;');
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

        function showBusy(show) {
            if ($busy && $busy.length) {
                $busy.toggleClass('is-visible', !!show);
            }
        }

        function showState(show, message) {
            if ($state && $state.length) {
                $state
                    .text(message || '')
                    .toggleClass('is-visible', !!show);
            }

            if ($body) {
                $body.toggle(!show);
            }

            if ($why) {
                $why.toggle(!show);
            }
        }

        function showDialogBusy(show) {
            if ($dialogBusy && $dialogBusy.length) {
                $dialogBusy.toggleClass('is-visible', !!show);
            }
        }

        this.Initalize = function () {
            createWidget();
            loadData();
        };

        function createWidget() {
            var uid = self.AD_UserHomeWidgetID || 'ptm';

            var $card = $('<div class="vas-ptmonth-card">');

            $card.attr({
                role: 'button',
                tabindex: '0'
            });

            var $header = $('<div class="vas-ptmonth-header">');

            var $icon = $(
                '<div class="vas-ptm-icon">' +
                '<svg viewBox="0 0 24 24" fill="none" aria-hidden="true" focusable="false">' +
                '<polyline points="23 18 13.5 8.5 8.5 13.5 1 6"></polyline>' +
                '<polyline points="17 18 23 18 23 12"></polyline>' +
                '</svg>' +
                '</div>'
            );

            var $headerText = $('<div class="vas-ptm-header-text">');

            var $title = $('<div class="vas-ptm-title">').text(
                lbl(
                    'VAS_028_MessagePaidThisMonth',
                    'Paid this month'
                )
            );

            var $arrow = $(
                '<span class="vas-ptm-arrow" aria-hidden="true">' +
                '<svg viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2.6" stroke-linecap="round" stroke-linejoin="round">' +
                '<path d="M9 18l6-6-6-6"></path>' +
                '</svg>' +
                '</span>'
            );

            $headerText.append($title);
            $header.append($icon).append($headerText).append($arrow);

            $body = $('<div class="vas-ptm-body">');

            $metricEl = $('<div>')
                .attr('id', 'vis-ptm-metric-' + uid)
                .addClass('vas-ptm-metric')
                .text('—');

            $body.append($metricEl);

            $why = $('<div class="vas-ptm-why-wrap">');

            $whyText = $('<span>')
                .attr('id', 'vis-ptm-why-' + uid)
                .addClass('vas-ptm-why-text')
                .text(
                    lbl(
                        'VAS_028_MessageLoading',
                        'Loading'
                    )
                );

            $why.append($whyText);

            $busy = $(
                '<div class="vas-ptm-ap-busy">' +
                '<div class="vis-busyindicatorinnerwrap">' +
                '<i class="vis_widgetloader"></i>' +
                '</div>' +
                '</div>'
            );

            $state = $('<div class="vas-ptm-state-message">');

            $card
                .append($header)
                .append($body)
                .append($why)
                .append($busy)
                .append($state);

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
                url:
                    VIS.Application.contextUrl +
                    'VAS_028_PaidThisMonthAPPaymentWidget/GetPaidThisMonth',

                type: 'GET',
                dataType: 'json',
                cache: false,

                success: function (response) {
                    if (isDisposed) {
                        return;
                    }

                    var data = normalizeResponse(response);

                    if (!data || data.error) {
                        showState(
                            true,
                            lbl(
                                'VAS_ErrorLoading',
                                'Could not load data'
                            )
                        );

                        return;
                    }

                    renderMetric(data);
                },

                error: function () {
                    if (!isDisposed) {
                        showState(
                            true,
                            lbl(
                                'VAS_ErrorLoading',
                                'Could not load data'
                            )
                        );
                    }
                },

                complete: function () {
                    if (!isDisposed) {
                        showBusy(false);
                    }
                }
            });
        }

        function renderMetric(data) {
            lastData = data || {};

            var amount = Number(data.totalPaidAmount);

            if (isNaN(amount)) {
                amount = Number(data.paidThisMonth);
            }

            if (isNaN(amount)) {
                amount = Number(data.value);
            }

            if (isNaN(amount) || amount <= 0) {
                setNoData();
                return;
            }

            showState(false, '');

            var symbol =
                data.symbol ||
                data.currencySymbol ||
                '';

            var precision = normalizePrecision(
                data.precision
            );

            /*
             * The current controller returns customerCount.
             * For AP Payment this value represents distinct vendors.
             */
            var vendorCount = Number(
                data.vendorCount ||
                data.customerCount ||
                data.paymentCount ||
                0
            );

            if ($metricEl) {
                $metricEl.text(
                    formatMetric(
                        amount,
                        symbol,
                        precision
                    )
                );
            }

            if ($whyText) {
                $whyText.text(
                    getWhyText(
                        vendorCount,
                        data.description
                    )
                );
            }

            if ($dialog && $dialog.is(':visible')) {
                rowsLoaded = false;
                pageNo = 1;
                loadRows();
            }
        }

        function getWhyText(vendorCount, fallbackDescription) {
            if (vendorCount > 0) {
                var vendorLabel = vendorCount === 1
                    ? lbl(
                        'VAS_028_MessageVendor',
                        'vendor'
                    )
                    : lbl(
                        'VAS_028_MessageVendors',
                        'vendors'
                    );

                return (
                    lbl(
                        'VAS_028_MessagePaidTo',
                        'Paid to'
                    ) +
                    ' ' +
                    vendorCount +
                    ' ' +
                    vendorLabel +
                    ' ' +
                    lbl(
                        'VAS_028_MessageSoFarThisMonth',
                        'so far this month.'
                    )
                );
            }

            return (
                fallbackDescription ||
                lbl(
                    'VAS_028_MessageNoPaymentsThisMonth',
                    'No payments this month.'
                )
            );
        }

        function formatMetric(value, symbol, precision) {
            var numericValue = Number(value || 0);
            var sign = numericValue < 0 ? '-' : '';
            var absoluteValue = Math.abs(numericValue);

            return (
                sign +
                symbol +
                formatAmount(
                    absoluteValue,
                    precision
                )
            );
        }

        function formatAmount(value, precision) {
            var numericValue = Number(value || 0);

            return numericValue.toLocaleString(
                window.navigator.language,
                {
                    minimumFractionDigits: precision,
                    maximumFractionDigits: precision
                }
            );
        }

        function normalizePrecision(precision) {
            var stdPrecision = Number(precision);

            if (
                !isNaN(stdPrecision) &&
                stdPrecision >= 0
            ) {
                return stdPrecision;
            }

            if (
                VIS &&
                VIS.Env &&
                VIS.Env.getCtx &&
                VIS.Env.getCtx().getStdPrecision
            ) {
                stdPrecision = Number(
                    VIS.Env.getCtx().getStdPrecision()
                );
            }

            if (
                isNaN(stdPrecision) ||
                stdPrecision < 0
            ) {
                return 2;
            }

            return stdPrecision;
        }

        function setNoData() {
            lastData = null;

            if ($metricEl) {
                $metricEl.text('');
            }

            if ($whyText) {
                $whyText.text('');
            }

            showState(
                true,
                lbl(
                    'VAS_028_MessageNoData',
                    'No Data'
                )
            );
        }

        function openDialog() {
            if (!$dialog || isDisposed) {
                return;
            }

            if (
                $state &&
                $state.hasClass('is-visible')
            ) {
                return;
            }

            $dialog.show();

            $('body').addClass(
                'vas-ptm-body-lock'
            );

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

            $('body').removeClass(
                'vas-ptm-body-lock'
            );

            pageNo = 1;
            rowsLoaded = false;
        }

        function loadRows() {
            if (
                !$dialogTbody ||
                isDisposed ||
                rowsLoading
            ) {
                return;
            }

            rowsLoading = true;

            showDialogBusy(true);
            updatePagerControls(0, 0);

            $.ajax({
                url:
                    VIS.Application.contextUrl +
                    'VAS_028_PaidThisMonthAPPaymentWidget/GetPaidThisMonthRows',

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
                        renderRowsResult({
                            rows: [],
                            totalRecords: 0,
                            totalPages: 0,
                            pageNo: pageNo
                        });

                        return;
                    }

                    renderRowsResult(data);
                    rowsLoaded = true;
                },

                error: function () {
                    if (!isDisposed) {
                        renderRowsResult({
                            rows: [],
                            totalRecords: 0,
                            totalPages: 0,
                            pageNo: pageNo
                        });
                    }
                },

                complete: function () {
                    if (!isDisposed) {
                        rowsLoading = false;
                        showDialogBusy(false);
                        updatePagerFromCurrent();
                    }
                }
            });
        }

        function renderRowsResult(data) {
            var rows =
                data &&
                    $.isArray(data.rows)
                    ? data.rows
                    : [];

            totalRecords = Number(
                data && data.totalRecords
                    ? data.totalRecords
                    : 0
            );

            totalPages = Number(
                data && data.totalPages
                    ? data.totalPages
                    : 0
            );

            if (
                data &&
                typeof data.pageNo !== 'undefined'
            ) {
                pageNo = Number(data.pageNo);
            }

            if (
                pageNo > totalPages &&
                totalPages > 0
            ) {
                pageNo = totalPages;
            }

            if (pageNo < 1 || isNaN(pageNo)) {
                pageNo = 1;
            }

            renderDialogHeader(data || {});
            renderSummary(data || {}, rows);
            renderRows(rows);

            var from = totalRecords === 0
                ? 0
                : (pageNo - 1) * pageSize;

            var to = Math.min(
                from + rows.length,
                totalRecords
            );

            updatePagerControls(from, to);
        }

        function renderDialogHeader(data) {
            if ($dialogTitle) {
                $dialogTitle.text(
                    data.title ||
                    lbl(
                        'VAS_028_MessagePaidThisMonth',
                        'Paid this month'
                    )
                );
            }

            if ($dialogSubtitle) {
                $dialogSubtitle.text(
                    data.subtitle ||
                    getFallbackDialogSubtitle()
                );
            }
        }

        function getFallbackDialogSubtitle() {
            if (!lastData) {
                return lbl(
                    'VAS_028_MessageCashPaid',
                    'Cash paid'
                );
            }

            var paymentCount = Number(
                totalRecords ||
                lastData.paymentCount ||
                0
            );

            var amount = Number(
                lastData.totalPaidAmount ||
                lastData.paidThisMonth ||
                lastData.value ||
                0
            );

            var symbol =
                lastData.symbol ||
                lastData.currencySymbol ||
                '';

            var precision = normalizePrecision(
                lastData.precision
            );

            return (
                paymentCount +
                ' ' +
                lbl(
                    'VAS_028_MessagePayments',
                    'payments'
                ) +
                ' · MTD ' +
                formatCurrencyAmount(
                    amount,
                    symbol,
                    lastData.currencyISO,
                    precision
                )
            );
        }

        function renderSummary(data, rows) {
            rows = $.isArray(rows)
                ? rows
                : [];

            var totalAmount = Number(
                lastData &&
                    lastData.totalPaidAmount
                    ? lastData.totalPaidAmount
                    : 0
            );

            var paymentCount = Number(
                data.totalRecords ||
                totalRecords ||
                rows.length ||
                0
            );

            var pageTotal = 0;
            var largestPayment = 0;

            for (var i = 0; i < rows.length; i++) {
                var rowAmount = Number(
                    rows[i].amount || 0
                );

                pageTotal += rowAmount;

                if (rowAmount > largestPayment) {
                    largestPayment = rowAmount;
                }
            }

            var averagePayment = rows.length > 0
                ? pageTotal / rows.length
                : 0;

            var firstRow = rows.length > 0
                ? rows[0]
                : {};

            var symbol =
                lastData && lastData.symbol
                    ? lastData.symbol
                    : (
                        firstRow.paymentCurrencySymbol ||
                        ''
                    );

            var iso =
                firstRow.paymentCurrency ||
                '';

            var precision = normalizePrecision(
                firstRow.stdPrecision ||
                (
                    lastData
                        ? lastData.precision
                        : null
                )
            );

            if ($summaryTotal) {
                $summaryTotal.text(
                    formatCurrencyAmount(
                        totalAmount,
                        symbol,
                        iso,
                        precision
                    )
                );
            }

            if ($summaryCount) {
                $summaryCount.text(
                    paymentCount.toLocaleString(
                        window.navigator.language
                    )
                );
            }

            /*
             * The current controller does not return the monthly
             * average and largest payment. These two values are
             * calculated from the currently loaded page.
             */
            if ($summaryAvg) {
                $summaryAvg.text(
                    formatCurrencyAmount(
                        averagePayment,
                        symbol,
                        iso,
                        precision
                    )
                );
            }

            if ($summaryLargest) {
                $summaryLargest.text(
                    formatCurrencyAmount(
                        largestPayment,
                        symbol,
                        iso,
                        precision
                    )
                );
            }
        }

        function renderRows(rows) {
            if (!$dialogTbody) {
                return;
            }

            $dialogTbody.empty();

            if (!rows || rows.length === 0) {
                $dialogTbody.html(
                    '<tr>' +
                    '<td class="vas-ptm-dialog-empty" colspan="8">' +
                    escapeHtml(
                        lbl(
                            'VAS_028_MessageNoPaymentsThisMonth',
                            'No payments this month.'
                        )
                    ) +
                    '</td>' +
                    '</tr>'
                );

                return;
            }

            for (var i = 0; i < rows.length; i++) {
                var row = rows[i];

                var dateText = formatDate(
                    row.date
                );

                var bankText = formatBankAccount(
                    row
                );

                var precision = normalizePrecision(
                    row.stdPrecision
                );

                var amountText = formatCurrencyAmount(
                    row.amount,
                    row.paymentCurrencySymbol,
                    row.paymentCurrency,
                    precision
                );

                var statusType = row.allocated
                    ? 'cleared'
                    : 'intransit';

                var executionStatusName = row.executionStatusName;

                var statusClass = getStatusClass(
                    statusType
                );

                var statusText = getStatusText(
                    statusType, executionStatusName
                );

                var supplierName =
                    row.supplier || '';

                var paymentMethod =
                    row.paymentMethodName || '';

                $dialogTbody.append(
                    '<tr>' +

                    '<td class="vas-ptm-td-doc" title="' +
                    escapeHtml(row.documentNo || '') +
                    '">' +
                    escapeHtml(row.documentNo || '') +
                    '</td>' +

                    '<td class="vas-ptm-td-date" title="' +
                    escapeHtml(dateText) +
                    '">' +
                    escapeHtml(dateText) +
                    '</td>' +

                    '<td class="vas-ptm-td-vendor" title="' +
                    escapeHtml(supplierName) +
                    '">' +
                    escapeHtml(supplierName) +
                    '</td>' +

                    '<td class="vas-ptm-td-bank" title="' +
                    escapeHtml(bankText) +
                    '">' +
                    escapeHtml(bankText) +
                    '</td>' +

                    '<td class="vas-ptm-td-currency" title="' +
                    escapeHtml(row.paymentCurrency || '') +
                    '">' +
                    escapeHtml(row.paymentCurrency || '') +
                    '</td>' +

                    '<td class="vas-ptm-td-method" title="' +
                    escapeHtml(paymentMethod) +
                    '">' +
                    escapeHtml(paymentMethod) +
                    '</td>' +

                    '<td class="vas-ptm-td-status">' +
                    '<span class="vas-ptm-dialog-status ' +
                    statusClass +
                    '">' +
                    escapeHtml(statusText) +
                    '</span>' +
                    '</td>' +


                    '<td class="vas-ptm-td-amount" title="' +
                    escapeHtml(amountText) +
                    '">' +
                    escapeHtml(amountText) +
                    '</td>' +


                    '</tr>'
                );
            }
        }

        function updatePagerFromCurrent() {
            var from = totalRecords === 0
                ? 0
                : (pageNo - 1) * pageSize;

            var to = Math.min(
                pageNo * pageSize,
                totalRecords
            );

            updatePagerControls(from, to);
        }

        function updatePagerControls(from, to) {
            if ($pagerHelper) {
                if (totalRecords > 0) {
                    $pagerHelper.text(
                        lbl(
                            'VAS_Showing',
                            'Showing'
                        ) +
                        ' ' +
                        (from + 1) +
                        '-' +
                        to +
                        ' ' +
                        lbl(
                            'VAS_Of',
                            'of'
                        ) +
                        ' ' +
                        totalRecords
                    );
                }
                else {
                    $pagerHelper.text('');
                }
            }

            if ($pagerText) {
                $pagerText.text(
                    totalPages > 0
                        ? (
                            pageNo +
                            ' ' +
                            lbl(
                                'VAS_Of',
                                'of'
                            ) +
                            ' ' +
                            totalPages
                        )
                        : ''
                );
            }

            if ($pagerPrev) {
                $pagerPrev.prop(
                    'disabled',
                    rowsLoading ||
                    pageNo <= 1
                );
            }

            if ($pagerNext) {
                $pagerNext.prop(
                    'disabled',
                    rowsLoading ||
                    totalPages <= 1 ||
                    pageNo >= totalPages
                );
            }
        }

        function formatCurrencyAmount(
            value,
            symbol,
            iso,
            precision
        ) {
            var numericValue = Number(value || 0);

            var amount = numericValue.toLocaleString(
                window.navigator.language,
                {
                    minimumFractionDigits: precision,
                    maximumFractionDigits: precision
                }
            );

            if (symbol) {
                return symbol + amount;
            }

            return iso
                ? amount + ' ' + iso
                : amount;
        }

        function formatDate(value) {
            if (!value) {
                return '';
            }

            /*
             * The controller returns yyyy-MM-dd.
             * Adding T00:00:00 prevents timezone conversion
             * from changing the displayed date.
             */
            var normalizedValue = String(value);

            if (
                /^\d{4}-\d{2}-\d{2}$/.test(
                    normalizedValue
                )
            ) {
                normalizedValue += 'T00:00:00';
            }

            var date = new Date(
                normalizedValue
            );

            if (isNaN(date.getTime())) {
                return value;
            }

            return date.toLocaleDateString(
                window.navigator.language,
                {
                    day: '2-digit',
                    month: 'short',
                    year: 'numeric'
                }
            );
        }

        function formatBankAccount(row) {
            var bankName =
                row &&
                    row.bankName
                    ? String(row.bankName).trim()
                    : '';

            var accountNo =
                row &&
                    row.accountNo
                    ? String(row.accountNo).trim()
                    : '';

            var last4 = accountNo
                ? (
                    accountNo.length > 4
                        ? accountNo.slice(-4)
                        : accountNo
                )
                : '';

            if (bankName && last4) {
                return (
                    bankName +
                    ' ****' +
                    last4
                );
            }

            if (bankName) {
                return bankName;
            }

            return last4
                ? '****' + last4
                : '';
        }

        function getStatusClass(statusType) {
            if (statusType === 'cleared') {
                return 'vas-ptm-dialog-status-cleared';
            }

            if (
                statusType === 'bounced' ||
                statusType === 'B' ||
                statusType === 'C'
            ) {
                return 'vas-ptm-dialog-status-bounced';
            }

            return 'vas-ptm-dialog-status-intransit';
        }

        function getStatusText(statusType, executionStatusName = "") {
            if (statusType === 'cleared') {
                return lbl(
                    'VAS_028_MessageCleared',
                    'Cleared'
                );
            }

            if (
                statusType === 'bounced' ||
                statusType === 'B' ||
                statusType === 'C'
            ) {
                return lbl(
                    'VAS_028_MessageBounced',
                    'Bounced'
                );
            }

            return executionStatusName;
        }

        function createDialog() {
            if ($dialog) {
                return;
            }

            $dialog = $(
                '<div class="vas-ptm-dialog" style="display:none;" role="dialog" aria-modal="true">' +

                '<div class="vas-ptm-dialog-scrim"></div>' +

                '<div class="vas-ptm-dialog-card">' +

                '<div class="vas-ptm-dialog-header">' +

                '<div class="vas-ptm-dialog-icon">' +
                '<svg viewBox="0 0 24 24" fill="none" aria-hidden="true" focusable="false">' +
                '<polyline points="23 18 13.5 8.5 8.5 13.5 1 6"></polyline>' +
                '<polyline points="17 18 23 18 23 12"></polyline>' +
                '</svg>' +
                '</div>' +

                '<div class="vas-ptm-dialog-title-group">' +
                '<div class="vas-ptm-dialog-title"></div>' +
                '<div class="vas-ptm-dialog-subtitle"></div>' +
                '</div>' +

                '<button type="button" class="vas-ptm-dialog-close" aria-label="' +
                escapeHtml(
                    lbl(
                        'VAS_Close',
                        'Close'
                    )
                ) +
                '">' +
                '<svg viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="1.8" stroke-linecap="round" stroke-linejoin="round">' +
                '<line x1="18" y1="6" x2="6" y2="18"></line>' +
                '<line x1="6" y1="6" x2="18" y2="18"></line>' +
                '</svg>' +
                '</button>' +

                '</div>' +

                '<div class="vas-ptm-dialog-summary">' +

                '<div>' +
                '<span>' +
                escapeHtml(
                    lbl(
                        'VAS_028_MessageTotalPaid',
                        'Total paid'
                    )
                ) +
                '</span>' +
                '<strong class="vas-ptm-summary-total">-</strong>' +
                '</div>' +

                '<div>' +
                '<span>' +
                escapeHtml(
                    lbl(
                        'VAS_028_MessagePayments',
                        'Payments'
                    )
                ) +
                '</span>' +
                '<strong class="vas-ptm-summary-count">-</strong>' +
                '</div>' +

                '<div>' +
                '<span>' +
                escapeHtml(
                    lbl(
                        'VAS_028_MessageAvgTicket',
                        'Avg ticket'
                    )
                ) +
                '</span>' +
                '<strong class="vas-ptm-summary-avg">-</strong>' +
                '</div>' +

                '<div>' +
                '<span>' +
                escapeHtml(
                    lbl(
                        'VAS_028_MessageLargest',
                        'Largest'
                    )
                ) +
                '</span>' +
                '<strong class="vas-ptm-summary-largest">-</strong>' +
                '</div>' +

                '</div>' +

                '<div class="vas-ptm-dialog-body">' +

                '<div class="vas-ptm-dialog-busy">' +
                '<div class="vis-busyindicatorinnerwrap">' +
                '<i class="vis_widgetloader"></i>' +
                '</div>' +
                '</div>' +

                '<table class="vas-ptm-dialog-table">' +

                '<thead>' +
                '<tr>' +

                '<th>' +
                escapeHtml(
                    lbl(
                        'VAS_028_MessagePaymentNo',
                        'Payment No.'
                    )
                ) +
                '</th>' +

                '<th>' +
                escapeHtml(
                    lbl(
                        'VAS_028_MessageDate',
                        'Date'
                    )
                ) +
                '</th>' +

                '<th>' +
                escapeHtml(
                    lbl(
                        'VAS_028_MessageVendor',
                        'Vendor'
                    )
                ) +
                '</th>' +

                '<th>' +
                escapeHtml(
                    lbl(
                        'VAS_028_BankAccount',
                        'Bank account'
                    )
                ) +
                '</th>' +

                '<th>' +
                escapeHtml(
                    lbl(
                        'VAS_PaymentCurrency',
                        'Currency'
                    )
                ) +
                '</th>' +

                '<th>' +
                escapeHtml(
                    lbl(
                        'VAS_028_MessageMethod',
                        'Method'
                    )
                ) +
                '</th>' +

                '<th>' +
                escapeHtml(
                    lbl(
                        'VAS_028_MessageStatus',
                        'Status'
                    )
                ) +
                '</th>' +


                '<th class="vas-ptm-th-amount">' +
                escapeHtml(
                    lbl(
                        'VAS_028_MessageAmount',
                        'Amount'
                    )
                ) +
                '</th>' +


                '</tr>' +
                '</thead>' +

                '<tbody class="vas-ptm-dialog-tbody"></tbody>' +

                '</table>' +

                '</div>' +

                '<div class="vas-ptm-dialog-footer">' +

                '<span class="vas-ptm-pager-helper"></span>' +

                '<div class="vas-ptm-pager">' +

                '<button type="button" class="vas-ptm-pager-btn vas-ptm-pager-prev" aria-label="' +
                escapeHtml(
                    lbl(
                        'VAS_Previous',
                        'Previous'
                    )
                ) +
                '">‹</button>' +

                '<span class="vas-ptm-pager-text"></span>' +

                '<button type="button" class="vas-ptm-pager-btn vas-ptm-pager-next" aria-label="' +
                escapeHtml(
                    lbl(
                        'VAS_Next',
                        'Next'
                    )
                ) +
                '">›</button>' +

                '</div>' +

                '</div>' +

                '</div>' +

                '</div>'
            );

            $dialogTitle = $dialog.find(
                '.vas-ptm-dialog-title'
            );

            $dialogSubtitle = $dialog.find(
                '.vas-ptm-dialog-subtitle'
            );

            $dialogTbody = $dialog.find(
                '.vas-ptm-dialog-tbody'
            );

            $dialogBusy = $dialog.find(
                '.vas-ptm-dialog-busy'
            );

            $pagerHelper = $dialog.find(
                '.vas-ptm-pager-helper'
            );

            $pagerPrev = $dialog.find(
                '.vas-ptm-pager-prev'
            );

            $pagerNext = $dialog.find(
                '.vas-ptm-pager-next'
            );

            $pagerText = $dialog.find(
                '.vas-ptm-pager-text'
            );

            $summaryTotal = $dialog.find(
                '.vas-ptm-summary-total'
            );

            $summaryCount = $dialog.find(
                '.vas-ptm-summary-count'
            );

            $summaryAvg = $dialog.find(
                '.vas-ptm-summary-avg'
            );

            $summaryLargest = $dialog.find(
                '.vas-ptm-summary-largest'
            );

            $dialog
                .find('.vas-ptm-dialog-close')
                .on('click', function (e) {
                    e.stopPropagation();
                    closeDialog();
                });

            $dialog
                .find('.vas-ptm-dialog-scrim')
                .on('click', function () {
                    closeDialog();
                });

            $pagerPrev.on('click', function () {
                if (
                    rowsLoading ||
                    pageNo <= 1
                ) {
                    return;
                }

                pageNo--;
                loadRows();
            });

            $pagerNext.on('click', function () {
                if (
                    rowsLoading ||
                    pageNo >= totalPages
                ) {
                    return;
                }

                pageNo++;
                loadRows();
            });

            $(document).on(
                'keydown.vas-ptm-dialog-' +
                self.AD_UserHomeWidgetID,
                function (e) {
                    if (
                        e.key === 'Escape' &&
                        $dialog &&
                        $dialog.is(':visible')
                    ) {
                        closeDialog();
                    }
                }
            );

            $('body').append($dialog);

            renderDialogHeader({});
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

            $(document).off(
                'keydown.vas-ptm-dialog-' +
                self.AD_UserHomeWidgetID
            );

            $('body').removeClass(
                'vas-ptm-body-lock'
            );

            if ($dialog) {
                $dialog.remove();
                $dialog = null;
            }

            $root.remove();

            $metricEl = null;
            $whyText = null;
            $body = null;
            $why = null;
            $busy = null;
            $state = null;

            $dialogTitle = null;
            $dialogSubtitle = null;
            $dialogTbody = null;
            $dialogBusy = null;

            $pagerHelper = null;
            $pagerPrev = null;
            $pagerNext = null;
            $pagerText = null;

            $summaryTotal = null;
            $summaryCount = null;
            $summaryAvg = null;
            $summaryLargest = null;

            lastData = null;
        };
    };

    VAS.VAS_028_PaidThisMonthAPPaymentWidget.prototype.init = function (
        windowNo,
        frame
    ) {
        this.frame = frame;
        this.windowNo = windowNo;

        if (frame && frame.widgetInfo) {
            this.AD_UserHomeWidgetID =
                frame.widgetInfo.AD_UserHomeWidgetID;
        }

        this.Initalize();

        if (
            this.frame &&
            this.frame.getContentGrid
        ) {
            this.frame
                .getContentGrid()
                .append(this.getRoot());
        }
    };

    VAS.VAS_028_PaidThisMonthAPPaymentWidget.prototype.widgetSizeChange = function (
        height,
        width
    ) {
        var $root = this.getRoot();

        if (!$root) {
            return;
        }

        $root.toggleClass(
            'vas-ptm-compact',
            (width && width < 240) ||
            (height && height < 160)
        );
    };

    VAS.VAS_028_PaidThisMonthAPPaymentWidget.prototype.refreshWidget = function () {
        this.refreshData();
    };

    VAS.VAS_028_PaidThisMonthAPPaymentWidget.prototype.dispose = function () {
        this.disposeComponent();

        if (
            this.frame &&
            this.frame.dispose
        ) {
            this.frame.dispose();
        }

        this.frame = null;
    };

})(VAS, jQuery);
