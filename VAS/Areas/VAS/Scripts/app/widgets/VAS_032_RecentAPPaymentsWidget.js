/**
 * Recent payments
 * Purpose - Shows the latest outgoing AP payments with vendor, document number, payment method,
 *           bank account, status, and amount. Clicking a row opens a payment detail popup.
 *
 * ── Labels / Message Keys ─────────────────────────────────────────────
 *  #  | Current Text                         | Message Key
 * ----+--------------------------------------+--------------------------------
 *  1  | Recent payments                      | VAS_032_MessageRecentPayments
 *  2  | Date                                 | VAS_032_MessageDate
 *  3  | Vendor                               | VAS_032_MessageVendor
 *  4  | Value (Document Number)              | VAS_032_MessageValueDocumentNumber
 *  5  | Method                               | VAS_032_MessageMethod
 *  6  | Bank Account Name                    | VAS_032_MessageBankAccountName
 *  7  | Status                               | VAS_032_MessageStatus
 *  8  | Amount                               | VAS_032_MessageAmount
 *  9  | Loading                              | VAS_032_MessageLoading
 * 10  | No Data                              | VAS_032_MessageNoData
 * 11  | Previous                             | VAS_Previous
 * 12  | Next                                 | VAS_Next
 * 13  | Could not load data                  | VAS_ErrorLoading
 * 14  | Of                                   | VAS_Of
 * 15  | Not Specified                        | VAS_032_MessageNotSpecified
 * 16  | Payment                              | VAS_032_MessagePayment
 * 17  | Payment date                         | VAS_032_MessagePaymentDate
 * 18  | Payment method                       | VAS_032_MessagePaymentMethod
 * 19  | Currency                             | VAS_032_MessageCurrency
 * 20  | Close                                | VAS_Close
 * 21  | Payment summary                      | VAS_032_MessagePaymentSummary
 * 22  | Bounced                              | VAS_032_MessageBounced
 * 23  | In transit                           | VAS_032_MessageInTransit
 * 24  | Cleared                              | VAS_032_MessageCleared
 * ─────────────────────────────────────────────────────────────────────
 */

; VAS = window.VAS || {};

; (function (VAS, $) {

    VAS.VAS_032_RecentAPPaymentsWidget = function () {

        this.frame;
        this.windowNo;
        this.AD_UserHomeWidgetID;

        var $root = $('<div class="vas-recent-ap-payments-root">');
        var $card;
        var $banner;
        var $bannerTitle;
        var $bannerSub;
        var $tableWrap;
        var $pager;
        var $pagerPrev;
        var $pagerNext;
        var $pagerText;
        var $showingText;
        var $busy;
        var $state;
        var $dialog;
        var $dialogTitle;
        var $dialogSub;
        var $dialogGrid;
        var $dialogAllocSection;
        var $dialogAllocWrap;

        var isDisposed = false;
        var paymentsData = [];
        var pageNo = 1;
        var pageSize = 7;
        var totalPages = 0;
        var resizeObserver = null;
        var widgetRowHeight = 44;
        var widgetMinimumRows = 3;
        var adaptiveAdjustCount = 0;

        function lbl(key, fallback) {
            var text = VIS.Msg.getMsg(key);
            return text && text !== '[' + key + ']' ? text : fallback;
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
            catch (error) {
                return null;
            }
        }

        function getResponseMessage(data, fallback) {
            var key;

            if (!data) {
                return fallback;
            }

            key = data.errorKey || data.messageKey;

            if (key) {
                return lbl(
                    key,
                    data.error ||
                    data.errorText ||
                    data.message ||
                    fallback
                );
            }

            return (
                data.error ||
                data.errorText ||
                data.message ||
                fallback
            );
        }

        this.Initalize = function () {
            createWidget();
            setupAdaptivePagination();
            loadData();
        };

        function createWidget() {
            $card = $('<div class="vas-recent-ap-payments-card">');

            var $head = $('<div class="vas-recent-ap-payments-head">');
            var $titleWrap = $('<div class="vas-recent-ap-payments-title-wrap">');
            var $iconBox = $('<span class="vas-recent-ap-payments-icon-box">');
            var $icon = $(
                '<svg class="vas-recent-ap-payments-icon" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2.2" stroke-linecap="round" stroke-linejoin="round" aria-hidden="true" focusable="false">' +
                '<line x1="22" y1="2" x2="11" y2="13"></line>' +
                '<polygon points="22 2 15 22 11 13 2 9 22 2"></polygon>' +
                '</svg>'
            );

            var $title = $('<div class="vas-recent-ap-payments-title">').text(lbl('VAS_032_MessageRecentPayments', 'Recent payments'));

            $pager = $('<div class="vas-recent-ap-payments-pager">');
            $pagerPrev = $('<button type="button" class="vas-recent-ap-payments-page-btn" aria-label="' + lbl('VAS_Previous', 'Previous') + '">' +
                '<svg viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2" stroke-linecap="round" stroke-linejoin="round">' +
                '<polyline points="15 18 9 12 15 6"></polyline>' +
                '</svg>' +
                '</button>');
            $pagerText = $('<span class="vas-recent-ap-payments-page-text">');
            $pagerNext = $('<button type="button" class="vas-recent-ap-payments-page-btn" aria-label="' + lbl('VAS_Next', 'Next') + '">' +
                '<svg viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2" stroke-linecap="round" stroke-linejoin="round">' +
                '<polyline points="9 18 15 12 9 6"></polyline>' +
                '</svg>' +
                '</button>');

            $pager.append($pagerPrev).append($pagerText).append($pagerNext);

            $iconBox.append($icon);
            $titleWrap.append($iconBox).append($title);

            $head.append($titleWrap);

            $tableWrap = $('<div class="vas-recent-ap-payments-table-wrap">');
            var $foot = $('<div class="vas-recent-ap-payments-footer">');
            $showingText = $('<div class="vas-recent-ap-payments-showing">');
            $busy = $('<div class="vas-recent-ap-payments-busy"><div class="vis-busyindicatorinnerwrap"><i class="vis_widgetloader"></i></div></div>');
            $state = $('<div class="vas-recent-ap-payments-state-message">');

            $foot.append($showingText).append($pager);
            $card.append($head).append($tableWrap).append($foot).append($busy).append($state);
            $root.empty().append($card);
            createDialog();

            $pagerPrev.on('click', function () {
                if (pageNo <= 1) {
                    return;
                }

                pageNo--;
                renderPage();
            });

            $pagerNext.on('click', function () {
                if (totalPages <= 1 || pageNo >= totalPages) {
                    return;
                }

                pageNo++;
                renderPage();
            });
        }

        function loadData() {
            if (isDisposed) {
                return;
            }

            showBusy(true);
            showState(false, '');

            $.ajax({
                url: VIS.Application.contextUrl + 'VAS_032_RecentAPPaymentsWidget/GetRecentAPPayments',
                type: 'GET',
                dataType: 'json',
                cache: false,
                success: function (response) {
                    if (isDisposed) {
                        return;
                    }

                    var data = response;

                    if (typeof response === 'string') {
                        try {
                            data = JSON.parse(response);
                        }
                        catch (e) {
                            showState(true, lbl('VAS_ErrorLoading', 'Could not load data'));
                            return;
                        }
                    }

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


        function renderData(data) {
            paymentsData = $.isArray(data.payments)
                ? $.grep(data.payments, function (payment) {
                    var amount = Number(payment.amount || 0);

                    return !isNaN(amount);
                })
                : [];

            if (paymentsData.length === 0) {
                setNoData();
                return;
            }

            pageNo = 1;
            totalPages = Math.ceil(paymentsData.length / pageSize);
            renderPage();
        }


        function renderPage() {
            if (!paymentsData || paymentsData.length === 0) {
                setNoData();
                return;
            }

            totalPages = Math.max(1, Math.ceil(paymentsData.length / pageSize));

            if (pageNo < 1) {
                pageNo = 1;
            }

            if (pageNo > totalPages) {
                pageNo = totalPages;
            }

            var startIndex = (pageNo - 1) * pageSize;
            var pagePayments = paymentsData.slice(startIndex, startIndex + pageSize);

            renderTable(pagePayments);
            updatePager();

            // Real rows exist now, so re-check the fit against their height.
            updateAdaptivePageSize();
        }

        /*
         * The fixed height is only a starting guess; once a row is on screen
         * its real height is used so the count matches what actually fits.
         */
        function measureWidgetRowHeight() {
            var $row = $tableWrap
                ? $tableWrap.find('tbody tr').first()
                : null;

            var measured = $row && $row.length
                ? $row.outerHeight(true)
                : 0;

            return measured > 0 ? measured : widgetRowHeight;
        }

        function updateAdaptivePageSize() {
            if (!$tableWrap || !$tableWrap[0]) {
                return;
            }

            var headerHeight = $tableWrap.find('thead').outerHeight() || 34;
            var availableHeight = Math.max(0, $tableWrap[0].clientHeight - headerHeight);
            var nextPageSize = Math.max(widgetMinimumRows, Math.floor(availableHeight / measureWidgetRowHeight()));

            if (nextPageSize === pageSize) {
                adaptiveAdjustCount = 0;
                return;
            }

            /*
             * Each render re-checks the fit, so cap the corrections to stop a
             * layout that never settles from looping.
             */
            if (adaptiveAdjustCount >= 4) {
                return;
            }

            adaptiveAdjustCount++;

            var firstVisibleRecord = ((pageNo - 1) * pageSize) + 1;

            pageSize = nextPageSize;
            pageNo = Math.max(1, Math.ceil(firstVisibleRecord / pageSize));

            if (paymentsData && paymentsData.length > 0) {
                renderPage();
            }
        }

        function setupAdaptivePagination() {
            if (!$tableWrap || !$tableWrap[0]) {
                return;
            }

            updateAdaptivePageSize();

            window.setTimeout(function () {
                updateAdaptivePageSize();
            }, 0);

            if (window.ResizeObserver) {
                resizeObserver = new ResizeObserver(function () {
                    updateAdaptivePageSize();
                });

                resizeObserver.observe($tableWrap[0]);
            }
        }

        function renderTable(payments) {
            showState(false, '');

            var $table = $('<table class="vas-recent-ap-payments-table">');
            var $thead = $('<thead>');
            var $headerRow = $('<tr>');

            $headerRow
                .append($('<th class="vas-recent-ap-payments-date">').text(lbl('VAS_032_MessageDate', 'Date')))
                .append($('<th class="vas-recent-ap-payments-value">').text(lbl('VAS_032_MessageValueDocumentNumber', 'Value')))
                .append($('<th class="vas-recent-ap-payments-vendor">').text(lbl('VAS_032_MessageVendor', 'Vendor')))
                .append($('<th class="vas-recent-ap-payments-method-col">').text(lbl('VAS_032_MessageMethod', 'Method')))
                .append($('<th class="vas-recent-ap-payments-bank-account">').text(lbl('VAS_032_MessageBankAccountName', 'Bank Account Name')))
                .append($('<th class="vas-recent-ap-payments-status-col">').text(lbl('VAS_032_MessageStatus', 'Status')))
                .append($('<th class="vas-recent-ap-payments-amount">').text(lbl('VAS_032_MessageAmount', 'Amount')));

            $thead.append($headerRow);
            $table.append($thead);

            var $tbody = $('<tbody>');

            for (var i = 0; i < payments.length; i++) {
                $tbody.append(createPaymentRow(payments[i]));
            }

            $table.append($tbody);
            $tableWrap.empty().append($table);
        }

        function updatePager() {
            if (!$pager) {
                return;
            }

            if ($showingText) {
                var count = paymentsData.length;
                var startIndex = count > 0 ? ((pageNo - 1) * pageSize) + 1 : 0;
                var endIndex = count > 0 ? Math.min(pageNo * pageSize, count) : 0;

                $showingText.text(
                    count > 0
                        ? lbl('VAS_Showing', 'Showing') + ' ' +
                          startIndex + '–' + endIndex + ' ' +
                          lbl('VAS_Of', 'of') + ' ' + count
                        : ''
                );
            }

            if ($pagerText) {
                if (totalPages > 1) {
                    $pagerText.text(pageNo + ' ' + lbl('VAS_Of', 'of') + ' ' + totalPages);
                }
                else {
                    $pagerText.text('');
                }
            }

            if ($pagerPrev) {
                $pagerPrev.prop('disabled', pageNo <= 1 || totalPages <= 1);
            }

            if ($pagerNext) {
                $pagerNext.prop('disabled', totalPages <= 1 || pageNo >= totalPages);
            }
        }

        function createPaymentRow(payment) {
            var $row = $('<tr class="vas-recent-ap-payments-row" tabindex="0">');

            var statusText = payment.statusName || getStatusText(payment.statusType);
            var statusClass = getStatusClass(payment.statusType, statusText);
            var documentNo = getDocumentNo(payment);
            var bankAccountText = getBankAccountText(payment);
            var vendorName = payment.vendorName || lbl('VAS_032_MessageNotSpecified', '-');
            var paymentMethodName = payment.paymentMethodName || lbl('VAS_032_MessageNotSpecified', '-');
            var amountText = formatWidgetCurrencyAmount(
                payment.amount,
                payment.currencySymbol,
                payment.currencyISO,
                payment.stdPrecision
            );

            $row
                .append($('<td class="vas-recent-ap-payments-date">')
                    .append($('<span class="vas-recent-ap-payments-cell-text">')
                        .attr('title', formatDate(payment.paymentDate))
                        .text(formatDate(payment.paymentDate))))

                .append($('<td class="vas-recent-ap-payments-value">')
                    .append($('<span class="vas-recent-ap-payments-cell-text">')
                        .attr('title', documentNo)
                        .text(documentNo)))

                .append($('<td class="vas-recent-ap-payments-vendor">')
                    .append($('<span class="vas-recent-ap-payments-cell-text">')
                        .attr('title', vendorName)
                        .text(vendorName)))

                .append($('<td class="vas-recent-ap-payments-method-col">')
                    .append($('<span class="vas-recent-ap-payments-method">')
                        .attr('title', paymentMethodName)
                        .text(paymentMethodName)))

                .append($('<td class="vas-recent-ap-payments-bank-account">')
                    .append($('<span class="vas-recent-ap-payments-cell-text">')
                        .attr('title', bankAccountText)
                        .text(bankAccountText)))

                .append($('<td class="vas-recent-ap-payments-status-col">')
                    .append($('<span class="vas-recent-ap-payments-status">')
                        .addClass(statusClass)
                        .text(statusText)))

                .append($('<td class="vas-recent-ap-payments-amount">')
                    .append($('<span class="vas-recent-ap-payments-cell-text">')
                        .attr('title', amountText)
                        .text(amountText)));

            $row.on('click', function () {
                openDialog(payment);
            });

            $row.on('keydown', function (e) {
                if (e.key === 'Enter' || e.key === ' ') {
                    e.preventDefault();
                    openDialog(payment);
                }
            });

            return $row;
        }

        function getDocumentNo(payment) {
            return payment.value || payment.documentNo || payment.referenceNo || '';
        }

        function last4(accountNo) {
            if (!accountNo) {
                return '';
            }

            var text = String(accountNo).trim();
            return text.length > 4 ? text.slice(-4) : text;
        }

        function getBankAccountText(payment) {
            var accountName = String(payment.bankAccountName || '').trim();
            var bankName = String(payment.bankName || '').trim();
            var accountTail = last4(
                payment.bankAccountNo ||
                payment.accountNo ||
                payment.AccountNo
            );
            var parsedAccount = parseBankAccountLabel(accountName);
            var accountLabel = parsedAccount.label || bankName;

            if (!accountTail) {
                accountTail = parsedAccount.tail;
            }

            if (accountLabel && accountTail) {
                return accountLabel + ' ****' + accountTail;
            }

            if (accountLabel) {
                return accountLabel;
            }

            return accountTail ? '****' + accountTail : '';
        }

        function parseBankAccountLabel(value) {
            var text = String(value || '').trim();
            var match;

            if (!text) {
                return {
                    label: '',
                    tail: ''
                };
            }

            match = /^(\d+)\s*[-–—]\s*(.+)$/.exec(text);

            if (match) {
                return {
                    label: match[2].trim(),
                    tail: last4(match[1])
                };
            }

            return {
                label: text,
                tail: ''
            };
        }

        function fieldHtml(label, valueHtml, extraClass, title) {
            var cls = 'vas-recent-ap-payments-dialog-field' + (extraClass ? ' ' + extraClass : '');
            var titleAttr = title ? ' title="' + escapeHtml(title) + '"' : '';

            return '<div class="' + cls + '">' +
                '<div class="vas-recent-ap-payments-dialog-label">' + escapeHtml(label) + '</div>' +
                '<div class="vas-recent-ap-payments-dialog-value"' + titleAttr + '>' + valueHtml + '</div>' +
                '</div>';
        }

        function openDialog(payment) {
            if (!$dialog || !payment) {
                return;
            }

            var documentNo = getDocumentNo(payment);
            var vendorName = payment.vendorName || lbl('VAS_032_MessageNotSpecified', '-');
            var methodName = payment.paymentMethodName || lbl('VAS_032_MessageNotSpecified', '-');
            var bankAccountText = getBankAccountText(payment);
            var statusText = payment.statusName || getStatusText(payment.statusType);
            var statusClass = getStatusClass(payment.statusType, statusText);
            var currencyText = payment.currencyISO || payment.currencySymbol || '';
            var amountText = formatCurrencyAmount(
                payment.amount,
                payment.currencySymbol,
                payment.currencyISO,
                payment.stdPrecision
            );

            if ($dialogTitle) {
                $dialogTitle.text(lbl('VAS_032_MessagePayment', 'Payment') + (documentNo ? ' ' + documentNo : ''));
            }

            if ($dialogSub) {
                $dialogSub.text(vendorName);
            }

            var gridHtml =
                fieldHtml(lbl('VAS_032_MessagePaymentDate', 'Payment date'), escapeHtml(formatDate(payment.paymentDate))) +
                fieldHtml(lbl('VAS_032_MessageValueDocumentNumber', 'Value (Document Number)'), '<span class="vas-recent-ap-payments-dialog-mono">' + escapeHtml(documentNo) + '</span>', null, documentNo) +
                fieldHtml(lbl('VAS_032_MessageVendor', 'Vendor'), '<strong>' + escapeHtml(vendorName) + '</strong>', null, vendorName) +
                fieldHtml(lbl('VAS_032_MessagePaymentMethod', 'Payment method'), escapeHtml(methodName), null, methodName) +
                fieldHtml(lbl('VAS_032_MessageBankAccountName', 'Bank Account Name'), escapeHtml(bankAccountText), null, bankAccountText) +
                fieldHtml(lbl('VAS_032_MessageStatus', 'Status'), '<span class="vas-recent-ap-payments-status ' + statusClass + '">' + escapeHtml(statusText) + '</span>') +
                fieldHtml(lbl('VAS_032_MessageAmount', 'Amount'), '<span class="vas-recent-ap-payments-dialog-amount">' + escapeHtml(amountText) + '</span>', 'vas-recent-ap-payments-dialog-amount-field', amountText);

            if (currencyText) {
                gridHtml += fieldHtml(lbl('VAS_032_MessageCurrency', 'Currency'), escapeHtml(currencyText));
            }

            if ($dialogGrid) {
                $dialogGrid.html(gridHtml);
            }

            if ($dialogAllocWrap) {
                $dialogAllocWrap.empty();
            }

            $dialog.show();
            $('body').addClass('vas-recent-ap-payments-body-lock');

            loadAllocationDetail(payment);
        }

        function closeDialog() {
            if (!$dialog) {
                return;
            }

            $dialog.hide();
            $('body').removeClass('vas-recent-ap-payments-body-lock');

            if ($dialogGrid) {
                $dialogGrid.empty();
            }

            if ($dialogAllocSection) {
                $dialogAllocSection.hide();
            }

            if ($dialogAllocWrap) {
                $dialogAllocWrap.empty();
            }
        }

        function createDialog() {
            if ($dialog) {
                return;
            }

            $dialog = $(
                '<div class="vas-recent-ap-payments-dialog" style="display:none;" role="dialog" aria-modal="true">' +
                '<div class="vas-recent-ap-payments-dialog-scrim"></div>' +
                '<div class="vas-recent-ap-payments-dialog-card">' +
                '<div class="vas-recent-ap-payments-dialog-header">' +
                '<div class="vas-recent-ap-payments-dialog-htext">' +
                '<span class="vas-recent-ap-payments-dialog-hicon">' +
                '<svg viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2" stroke-linecap="round" stroke-linejoin="round" aria-hidden="true">' +
                '<line x1="22" y1="2" x2="11" y2="13"></line>' +
                '<polygon points="22 2 15 22 11 13 2 9 22 2"></polygon>' +
                '</svg>' +
                '</span>' +
                '<div class="vas-recent-ap-payments-dialog-title-group">' +
                '<div class="vas-recent-ap-payments-dialog-title">' + escapeHtml(lbl('VAS_032_MessagePayment', 'Payment')) + '</div>' +
                '<div class="vas-recent-ap-payments-dialog-sub"></div>' +
                '</div>' +
                '</div>' +
                '<button type="button" class="vas-recent-ap-payments-dialog-close" aria-label="' + escapeHtml(lbl('VAS_Close', 'Close')) + '">' +
                '<svg viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="1.8" stroke-linecap="round" stroke-linejoin="round" aria-hidden="true"><line x1="18" y1="6" x2="6" y2="18"></line><line x1="6" y1="6" x2="18" y2="18"></line></svg>' +
                '</button>' +
                '</div>' +
                '<div class="vas-recent-ap-payments-dialog-body">' +
                '<div class="vas-recent-ap-payments-dialog-section-label">' + escapeHtml(lbl('VAS_032_MessagePaymentSummary', 'Payment summary')) + '</div>' +
                '<div class="vas-recent-ap-payments-dialog-grid"></div>' +
                '<div class="vas-recent-ap-payments-dialog-alloc-section" style="display:none;">' +
                '<div class="vas-recent-ap-payments-dialog-alloc-label">' + escapeHtml(lbl('VAS_032_AllocDetail', 'Allocation Detail')) + '</div>' +
                '<div class="vas-recent-ap-payments-dialog-alloc-wrap"></div>' +
                '</div>' +
                '</div>' +
                '</div>' +
                '</div>'
            );

            $dialogTitle = $dialog.find('.vas-recent-ap-payments-dialog-title');
            $dialogSub = $dialog.find('.vas-recent-ap-payments-dialog-sub');
            $dialogGrid = $dialog.find('.vas-recent-ap-payments-dialog-grid');
            $dialogAllocSection = $dialog.find('.vas-recent-ap-payments-dialog-alloc-section');
            $dialogAllocWrap = $dialog.find('.vas-recent-ap-payments-dialog-alloc-wrap');

            $dialog.find('.vas-recent-ap-payments-dialog-close').on('click', function (e) {
                e.stopPropagation();
                closeDialog();
            });

            $dialog.find('.vas-recent-ap-payments-dialog-scrim').on('click', function () {
                closeDialog();
            });

            $(document).on('keydown.vas-recent-ap-payments', function (e) {
                if (e.key === 'Escape' && $dialog && $dialog.is(':visible')) {
                    closeDialog();
                }
            });

            $('body').append($dialog);
        }

        function loadAllocationDetail(payment) {
            if (
                !$dialogAllocSection ||
                !$dialogAllocWrap
            ) {
                return;
            }

            $dialogAllocSection.show();

            var paymentId = payment && Number(payment.paymentId);

            if (!paymentId || paymentId <= 0) {
                renderAllocationDetail(null, payment);
                return;
            }

            $dialogAllocWrap.html(
                '<div class="vas-recent-ap-payments-alloc-loading">' +
                escapeHtml(lbl('VAS_032_MessageLoading', 'Loading')) +
                '&hellip;</div>'
            );

            $.ajax({
                url:
                    VIS.Application.contextUrl +
                    'VAS_032_RecentAPPaymentsWidget/GetPaymentAllocationDetail',

                type: 'GET',
                dataType: 'json',
                cache: false,

                data: {
                    paymentId: paymentId
                },

                success: function (response) {
                    renderAllocationDetail(
                        response,
                        payment
                    );
                },

                error: function () {
                    renderAllocationDetail(null, payment);
                }
            });
        }

        function renderAllocationDetail(response, payment) {
            if (
                !$dialogAllocSection ||
                !$dialogAllocWrap
            ) {
                return;
            }

            var data = response;

            if (typeof data === 'string') {
                try {
                    data = JSON.parse(data);
                }
                catch (e) {
                    $dialogAllocSection.hide();
                    return;
                }
            }

            if (
                !data ||
                !data.success ||
                !$.isArray(data.lines) ||
                data.lines.length === 0
            ) {
                $dialogAllocWrap.html(
                    '<div class="vas-recent-ap-payments-alloc-empty">' +
                    escapeHtml(lbl('VAS_032_AllocNoData', 'No data')) +
                    '</div>'
                );
                return;
            }

            var lines = data.lines;
            var firstLine = lines[0];
            var currencySymbol =
                firstLine.currencySymbol ||
                firstLine.currencyISO ||
                payment.currencySymbol ||
                '';
            var stdPrecision =
                Number(firstLine.stdPrecision) ||
                Number(payment.stdPrecision) ||
                2;

            var totalAllocated = 0;
            var totalDiscount = 0;
            var totalWriteOff = 0;

            var html =
                '<table class="vas-recent-ap-payments-alloc-table">' +
                '<thead><tr>' +
                '<th>' + escapeHtml(lbl('VAS_032_AllocInvoiceNo', 'Invoice No.')) + '</th>' +
                '<th>' + escapeHtml(lbl('VAS_032_AllocDate', 'Date')) + '</th>' +
                '<th class="vas-recent-ap-payments-alloc-num">' + escapeHtml(lbl('VAS_032_AllocAmount', 'Allocated')) + '</th>' +
                '<th class="vas-recent-ap-payments-alloc-num">' + escapeHtml(lbl('VAS_032_AllocDiscount', 'Discount')) + '</th>' +
                '<th class="vas-recent-ap-payments-alloc-num">' + escapeHtml(lbl('VAS_032_AllocWriteOff', 'Write-Off')) + '</th>' +
                '<th class="vas-recent-ap-payments-alloc-num">' + escapeHtml(lbl('VAS_032_AllocTotal', 'Total')) + '</th>' +
                '</tr></thead><tbody>';

            for (var i = 0; i < lines.length; i++) {
                var line = lines[i];
                var allocated = Number(line.allocatedAmount) || 0;
                var discount = Number(line.discountAmt) || 0;
                var writeOff = Number(line.writeOffAmt) || 0;
                var lineTotal = allocated + discount + writeOff;

                totalAllocated += allocated;
                totalDiscount += discount;
                totalWriteOff += writeOff;

                html +=
                    '<tr>' +
                    '<td>' + escapeHtml(line.invoiceDocumentNo || '—') + '</td>' +
                    '<td>' + escapeHtml(formatDate(line.invoiceDate) || '—') + '</td>' +
                    '<td class="vas-recent-ap-payments-alloc-num">' + formatAllocAmount(allocated, currencySymbol, stdPrecision) + '</td>' +
                    '<td class="vas-recent-ap-payments-alloc-num">' +
                    (discount !== 0
                        ? formatAllocAmount(discount, currencySymbol, stdPrecision)
                        : '<span class="vas-recent-ap-payments-alloc-dash">—</span>') +
                    '</td>' +
                    '<td class="vas-recent-ap-payments-alloc-num">' +
                    (writeOff !== 0
                        ? formatAllocAmount(writeOff, currencySymbol, stdPrecision)
                        : '<span class="vas-recent-ap-payments-alloc-dash">—</span>') +
                    '</td>' +
                    '<td class="vas-recent-ap-payments-alloc-num vas-recent-ap-payments-alloc-total-cell">' +
                    formatAllocAmount(lineTotal, currencySymbol, stdPrecision) +
                    '</td>' +
                    '</tr>';
            }

            var grandTotal = totalAllocated + totalDiscount + totalWriteOff;

            html +=
                '</tbody>' +
                '<tfoot><tr class="vas-recent-ap-payments-alloc-foot">' +
                '<td colspan="2"><strong>' + escapeHtml(lbl('VAS_Total', 'Total')) + '</strong></td>' +
                '<td class="vas-recent-ap-payments-alloc-num"><strong>' + formatAllocAmount(totalAllocated, currencySymbol, stdPrecision) + '</strong></td>' +
                '<td class="vas-recent-ap-payments-alloc-num"><strong>' +
                (totalDiscount !== 0
                    ? formatAllocAmount(totalDiscount, currencySymbol, stdPrecision)
                    : '<span class="vas-recent-ap-payments-alloc-dash">—</span>') +
                '</strong></td>' +
                '<td class="vas-recent-ap-payments-alloc-num"><strong>' +
                (totalWriteOff !== 0
                    ? formatAllocAmount(totalWriteOff, currencySymbol, stdPrecision)
                    : '<span class="vas-recent-ap-payments-alloc-dash">—</span>') +
                '</strong></td>' +
                '<td class="vas-recent-ap-payments-alloc-num vas-recent-ap-payments-alloc-total-cell"><strong>' +
                formatAllocAmount(grandTotal, currencySymbol, stdPrecision) +
                '</strong></td>' +
                '</tr></tfoot>' +
                '</table>';

            $dialogAllocWrap.html(html);
        }

        function formatAllocAmount(value, currencySymbol, stdPrecision) {
            var numericValue = Number(value || 0);
            var sign = numericValue < 0 ? '-' : '';

            var amount = Math.abs(numericValue).toLocaleString(
                window.navigator.language,
                {
                    minimumFractionDigits: stdPrecision,
                    maximumFractionDigits: stdPrecision
                }
            );

            return escapeHtml(
                currencySymbol
                    ? sign + currencySymbol + amount
                    : sign + amount
            );
        }

        function getStatusClass(statusType, statusText) {
            var rawStatus = ((statusType || '') + ' ' + (statusText || '')).toLowerCase();

            if (rawStatus.indexOf('bounce') >= 0 ||
                rawStatus.indexOf('fail') >= 0 ||
                rawStatus.indexOf('reject') >= 0 ||
                rawStatus.indexOf('cancel') >= 0 ||
                rawStatus.indexOf('void') >= 0 ||
                rawStatus.indexOf('error') >= 0 ||
                rawStatus.indexOf('declin') >= 0 ||
                rawStatus.indexOf('return') >= 0) {
                return 'vas-recent-ap-payments-status-bounced';
            }

            if (rawStatus.indexOf('pending') >= 0 ||
                rawStatus.indexOf('wait') >= 0 ||
                rawStatus.indexOf('queue') >= 0 ||
                rawStatus.indexOf('submitted') >= 0 ||
                rawStatus.indexOf('draft') >= 0 ||
                rawStatus.indexOf('not approved') >= 0) {
                return 'vas-recent-ap-payments-status-pending';
            }

            if (rawStatus.indexOf('transit') >= 0 ||
                rawStatus.indexOf('process') >= 0 ||
                rawStatus.indexOf('running') >= 0 ||
                rawStatus.indexOf('progress') >= 0) {
                return 'vas-recent-ap-payments-status-intransit';
            }

            if (rawStatus.indexOf('review') >= 0 ||
                rawStatus.indexOf('check') >= 0 ||
                rawStatus.indexOf('verify') >= 0) {
                return 'vas-recent-ap-payments-status-review';
            }

            if (rawStatus.indexOf('partial') >= 0) {
                return 'vas-recent-ap-payments-status-partial';
            }

            if (rawStatus.indexOf('clear') >= 0 ||
                rawStatus.indexOf('reconcile') >= 0 ||
                rawStatus.indexOf('allocat') >= 0 ||
                rawStatus.indexOf('complete') >= 0 ||
                rawStatus.indexOf('paid') >= 0 ||
                rawStatus.indexOf('success') >= 0 ||
                rawStatus.indexOf('approve') >= 0 ||
                rawStatus.indexOf('release') >= 0) {
                return 'vas-recent-ap-payments-status-cleared';
            }

            return 'vas-recent-ap-payments-status-neutral';
        }

        function getStatusText(statusType) {
            if (statusType === 'bounced') {
                return lbl('VAS_032_MessageBounced', 'Bounced');
            }

            if (statusType === 'intransit') {
                return lbl('VAS_032_MessageInTransit', 'In transit');
            }

            return lbl('VAS_032_MessageCleared', 'Cleared');
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
                month: 'short'
            });
        }

        function formatCurrencyAmount(value, currencySymbol, currencyISO, stdPrecision) {
            var numericValue = Number(value || 0);
            var precision = Number(stdPrecision);

            if (isNaN(precision) && VIS && VIS.Env && VIS.Env.getCtx && VIS.Env.getCtx().getStdPrecision) {
                precision = Number(VIS.Env.getCtx().getStdPrecision());
            }

            if (isNaN(precision) || precision < 0) {
                precision = 2;
            }

            var sign = numericValue < 0 ? '-' : '';

            var amount = Math.abs(numericValue).toLocaleString(window.navigator.language, {
                minimumFractionDigits: precision,
                maximumFractionDigits: precision
            });

            if (currencySymbol) {
                return sign + currencySymbol + amount;
            }

            return currencyISO ? sign + amount + ' ' + currencyISO : sign + amount;
        }

        function formatWidgetCurrencyAmount(value, currencySymbol, currencyISO, stdPrecision) {
            var numericValue = Number(value || 0);
            var sign = numericValue < 0 ? '-' : '';

            return sign + formatCurrencyAmount(
                Math.abs(numericValue),
                currencySymbol,
                currencyISO,
                stdPrecision
            );
        }
        function renderAllocationLinesTable(
            lines,
            header
        ) {
            var html = '';
            var index;
            var line;
            var amountText;
            var discountText;
            var writeOffText;
            var totalText;

            if (!lines || lines.length === 0) {
                return (
                    '<div class="vas-allocation-empty">' +
                    'No allocation lines found.' +
                    '</div>'
                );
            }

            html +=
                '<div class="vas-allocation-lines-table-wrap">' +
                '<table class="vas-allocation-lines-table">' +
                '<thead>' +
                '<tr>' +
                '<th>INVOICE NO.</th>' +
                '<th>VENDOR</th>' +
                '<th>INVOICE DATE</th>' +
                '<th>DUE DATE</th>' +
                '<th>AMOUNT</th>' +
                '<th>DISCOUNT</th>' +
                '<th>WRITEOFF</th>' +
                '<th>TOTAL ALLOCATED</th>' +
                '</tr>' +
                '</thead>' +
                '<tbody>';

            for (index = 0; index < lines.length; index++) {
                line = lines[index] || {};

                amountText = formatCurrencyAmount(
                    line.amount || 0,
                    header.currencySymbol,
                    header.currencyISO,
                    header.stdPrecision
                );

                discountText = formatCurrencyAmount(
                    line.discountAmt || 0,
                    header.currencySymbol,
                    header.currencyISO,
                    header.stdPrecision
                );

                writeOffText = formatCurrencyAmount(
                    line.writeOffAmt || 0,
                    header.currencySymbol,
                    header.currencyISO,
                    header.stdPrecision
                );

                totalText = formatCurrencyAmount(
                    line.allocatedTotal || 0,
                    header.currencySymbol,
                    header.currencyISO,
                    header.stdPrecision
                );

                html +=
                    '<tr>' +
                    '<td>' +
                    escapeHtml(
                        firstValue(
                            line.invoiceDocumentNo,
                            ''
                        )
                    ) +
                    '</td>' +

                    '<td>' +
                    escapeHtml(
                        firstValue(
                            line.vendorName,
                            ''
                        )
                    ) +
                    '</td>' +

                    '<td>' +
                    escapeHtml(
                        firstValue(
                            line.invoiceDate,
                            ''
                        )
                    ) +
                    '</td>' +

                    '<td>' +
                    escapeHtml(
                        firstValue(
                            line.dueDate,
                            ''
                        )
                    ) +
                    '</td>' +

                    '<td>' +
                    escapeHtml(amountText) +
                    '</td>' +

                    '<td>' +
                    escapeHtml(discountText) +
                    '</td>' +

                    '<td>' +
                    escapeHtml(writeOffText) +
                    '</td>' +

                    '<td>' +
                    escapeHtml(totalText) +
                    '</td>' +
                    '</tr>';
            }

            html +=
                '</tbody>' +
                '</table>' +
                '</div>';

            return html;
        }


        function openAllocationPopup(paymentRow) {
            var paymentId;

            paymentId = Number(
                paymentRow &&
                (
                    paymentRow.paymentId ||
                    paymentRow.cPaymentId ||
                    paymentRow.C_Payment_ID
                )
            );

            if (!paymentId || paymentId <= 0) {
                VIS.ADialog.error(
                    lbl(
                        'VAS_032_MessagePaymentIdRequired',
                        'Payment ID is required.'
                    )
                );
                return;
            }

            $.ajax({
                url:
                    VIS.Application.contextUrl +
                    'VAS_031_UpcomingAPRunsWidget/GetPaymentAllocationDetails',

                type: 'GET',
                dataType: 'json',
                cache: false,

                data: {
                    paymentId: paymentId
                },

                success: function (response) {
                    var data = normalizeResponse(response);

                    if (
                        !data ||
                        data.success === false ||
                        data.error
                    ) {
                        VIS.ADialog.error(
                            getResponseMessage(
                                data,
                                lbl(
                                    'VAS_032_CouldNotLoadAllocationDetails',
                                    'Could not load allocation details.'
                                )
                            )
                        );
                        return;
                    }

                    renderAllocationPopup(data);
                },

                error: function () {
                    VIS.ADialog.error(
                        lbl(
                            'VAS_032_CouldNotLoadAllocationDetails',
                            'Could not load allocation details.'
                        )
                    );
                }
            });
        }

        function renderAllocationPopup(data) {
            var header = data.header || {};
            var lines = $.isArray(data.lines)
                ? data.lines
                : [];

            var payAmtText = formatCurrencyAmount(
                header.payAmt || 0,
                header.currencySymbol,
                header.currencyISO,
                header.stdPrecision
            );

            var allocatedAmtText = formatCurrencyAmount(
                header.allocatedAmt || 0,
                header.currencySymbol,
                header.currencyISO,
                header.stdPrecision
            );

            var html =
                '<div class="vas-allocation-popup">' +

                '<div class="vas-allocation-popup-header">' +
                '<div class="vas-allocation-popup-title">' +
                escapeHtml(
                    firstValue(
                        header.documentNo,
                        'Payment'
                    )
                ) +
                ' · ' +
                escapeHtml(
                    firstValue(
                        header.description,
                        header.vendorName,
                        ''
                    )
                ) +
                '</div>' +

                '<div class="vas-allocation-popup-subtitle">' +
                'Posted · ' +
                escapeHtml(
                    firstValue(
                        header.postedDate,
                        header.dateTrx,
                        ''
                    )
                ) +
                '</div>' +
                '</div>' +

                '<div class="vas-allocation-summary-card">' +

                '<div class="vas-allocation-summary-grid">' +

                '<div class="vas-summary-item">' +
                '<div class="vas-summary-label">PAYMENT NO.</div>' +
                '<div class="vas-summary-value">' +
                escapeHtml(
                    firstValue(
                        header.documentNo,
                        ''
                    )
                ) +
                '</div>' +
                '</div>' +

                '<div class="vas-summary-item">' +
                '<div class="vas-summary-label">DATE</div>' +
                '<div class="vas-summary-value">' +
                escapeHtml(
                    firstValue(
                        header.dateTrx,
                        ''
                    )
                ) +
                '</div>' +
                '</div>' +

                '<div class="vas-summary-item">' +
                '<div class="vas-summary-label">STATUS</div>' +
                '<div class="vas-summary-value">' +
                escapeHtml(
                    firstValue(
                        header.statusText,
                        header.docStatus,
                        ''
                    )
                ) +
                '</div>' +
                '</div>' +

                '<div class="vas-summary-item">' +
                '<div class="vas-summary-label">ORGANIZATION</div>' +
                '<div class="vas-summary-value">' +
                escapeHtml(
                    firstValue(
                        header.organizationName,
                        ''
                    )
                ) +
                '</div>' +
                '</div>' +

                '<div class="vas-summary-item">' +
                '<div class="vas-summary-label">PAYMENT AMOUNT</div>' +
                '<div class="vas-summary-value">' +
                escapeHtml(payAmtText) +
                '</div>' +
                '</div>' +

                '<div class="vas-summary-item">' +
                '<div class="vas-summary-label">ALLOCATED</div>' +
                '<div class="vas-summary-value">' +
                escapeHtml(allocatedAmtText) +
                '</div>' +
                '</div>' +

                '<div class="vas-summary-item vas-summary-wide">' +
                '<div class="vas-summary-label">DESCRIPTION</div>' +
                '<div class="vas-summary-value">' +
                escapeHtml(
                    firstValue(
                        header.description,
                        header.vendorName,
                        ''
                    )
                ) +
                '</div>' +
                '</div>' +

                '</div>' +
                '</div>' +

                '<div class="vas-allocation-lines-title">' +
                'ALLOCATION LINES' +
                '</div>' +

                renderAllocationLinesTable(
                    lines,
                    header
                ) +

                '</div>';

            VIS.ADialog.info(
                html,
                true,
                'Allocation Details'
            );
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

            if ($banner) {
                $banner.toggle(!show);
            }

            if ($tableWrap) {
                $tableWrap.toggle(!show);
            }
        }

        function setNoData() {
            totalPages = 0;
            pageNo = 1;

            if ($tableWrap) {
                $tableWrap.empty();
            }

            updatePager();
            showState(true, lbl('VAS_032_MessageNoData', 'No Data'));
        }

        this.refreshWidget = function () {
            loadData();
        };

        this.getRoot = function () {
            return $root;
        };

        this.disposeComponent = function () {
            isDisposed = true;

            if (resizeObserver) {
                resizeObserver.disconnect();
                resizeObserver = null;
            }

            $root.remove();
            closeDialog();
            $(document).off('keydown.vas-recent-ap-payments');

            if ($dialog) {
                $dialog.remove();
            }

            $card = null;
            $banner = null;
            $bannerTitle = null;
            $bannerSub = null;
            $tableWrap = null;
            $pager = null;
            $pagerPrev = null;
            $pagerNext = null;
            $pagerText = null;
            $busy = null;
            $state = null;
            $dialog = null;
            $dialogTitle = null;
            $dialogSub = null;
            $dialogGrid = null;
            $dialogAllocSection = null;
            $dialogAllocWrap = null;
        };
    };

    VAS.VAS_032_RecentAPPaymentsWidget.prototype.init = function (windowNo, frame) {
        this.frame = frame;
        this.AD_UserHomeWidgetID = frame.widgetInfo.AD_UserHomeWidgetID;
        this.windowNo = windowNo;
        this.Initalize();
        this.frame.getContentGrid().append(this.getRoot());
    };

    VAS.VAS_032_RecentAPPaymentsWidget.prototype.widgetSizeChange = function (height, width) {
    };

    VAS.VAS_032_RecentAPPaymentsWidget.prototype.refreshWidget = function () {
        this.refreshWidget();
    };

    VAS.VAS_032_RecentAPPaymentsWidget.prototype.dispose = function () {
        this.disposeComponent();

        if (this.frame) {
            this.frame.dispose();
        }

        this.frame = null;
    };

})(VAS, jQuery);
