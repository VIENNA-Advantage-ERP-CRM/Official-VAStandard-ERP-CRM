/**
 * Recent payments
 * Purpose - Shows the latest outgoing AP payments with vendor, document number, payment method,
 *           bank account, status, and amount. Clicking a row opens a payment detail popup.
 *
 * ── Labels / Message Keys ─────────────────────────────────────────────
 *   Current Text                         | Message Key
 * ----+--------------------------------------+--------------------------------
 *  1  | Recent payments                      | VAS_032_MessageRecentPayments
 *  2  | Date                                 | VAS_032_MessageDate
 *  3  | Vendor                               | VAS_032_MessageVendor
 *  4  | Value (Document Number)              | VAS_032_MessageValueDocumentNumber
 *  5  | Method                               | VAS_032_MessageMethod
 *  6  | Bank Account Name                    | VAS_032_MessageBankAccountName
 *  7  | Status                               | VAS_032_MessageStatus
 *  8  | Amount                               | VAS_032_MessageAmount
 *  8  | Loading                              | VAS_032_MessageLoading
 *  9  | No Data                              | VAS_032_MessageNoData
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
        var $busy;
        var $state;
        var $dialog;
        var $dialogTitle;
        var $dialogSub;
        var $dialogGrid;

        var isDisposed = false;
        var paymentsData = [];
        var pageNo = 1;
        var pageSize = 7;
        var totalPages = 0;

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

        this.Initalize = function () {
            createWidget();
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
            $pagerPrev = $('<button type="button" class="vas-recent-ap-payments-page-btn" aria-label="' + lbl('VAS_Previous', 'Previous') + '">‹</button>');
            $pagerText = $('<span class="vas-recent-ap-payments-page-text">');
            $pagerNext = $('<button type="button" class="vas-recent-ap-payments-page-btn" aria-label="' + lbl('VAS_Next', 'Next') + '">›</button>');

            $pager.append($pagerPrev).append($pagerText).append($pagerNext);

            $iconBox.append($icon);
            $titleWrap.append($iconBox).append($title);

            $head.append($titleWrap).append($pager);

            $tableWrap = $('<div class="vas-recent-ap-payments-table-wrap">');
            $busy = $('<div class="vas-recent-ap-payments-busy">').text(lbl('VAS_032_MessageLoading', 'Loading'));
            $state = $('<div class="vas-recent-ap-payments-state-message">');

            $card.append($head).append($tableWrap).append($busy).append($state);
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

                    return !isNaN(amount) && amount > 0;
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
            var vendorName = payment.vendorName || lbl('VAS_032_MessageNotSpecified', 'Not Specified');
            var paymentMethodName = payment.paymentMethodName || lbl('VAS_032_MessageNotSpecified', 'Not Specified');
            var amountText = formatCurrencyAmount(
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
            var accountTail = last4(payment.bankAccountNo);

            if (accountName) {
                return accountName;
            }

            if (bankName) {
                return bankName;
            }

            return accountTail ? '****' + accountTail : '';
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
            var vendorName = payment.vendorName || lbl('VAS_032_MessageNotSpecified', 'Not Specified');
            var methodName = payment.paymentMethodName || lbl('VAS_032_MessageNotSpecified', 'Not Specified');
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

            $dialog.show();
            $('body').addClass('vas-recent-ap-payments-body-lock');
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
                '</div>' +
                '</div>' +
                '</div>'
            );

            $dialogTitle = $dialog.find('.vas-recent-ap-payments-dialog-title');
            $dialogSub = $dialog.find('.vas-recent-ap-payments-dialog-sub');
            $dialogGrid = $dialog.find('.vas-recent-ap-payments-dialog-grid');

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

            var amount = numericValue.toLocaleString(window.navigator.language, {
                minimumFractionDigits: precision,
                maximumFractionDigits: precision
            });

            if (currencySymbol) {
                return currencySymbol + amount;
            }

            return currencyISO ? amount + ' ' + currencyISO : amount;
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
