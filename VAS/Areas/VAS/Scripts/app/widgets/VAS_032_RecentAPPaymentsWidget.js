/**
 * Recent payments
 * Purpose - Shows the latest outgoing AP payments with vendor, payment method, invoice/order reference, status, and amount.
 *
 * ── Labels / Message Keys ─────────────────────────────────────────────
 *   Current Text                         | Message Key
 * ----+--------------------------------------+--------------------------------
 *  1  | Recent payments                      | VAS_032_MessageRecentPayments
 *  2  | Date                                 | VAS_032_MessageDate
 *  3  | Vendor                               | VAS_032_MessageVendor
 *  4  | Method                               | VAS_032_MessageMethod
 *  5  | Ref                                  | VAS_032_MessageRef
 *  6  | Status                               | VAS_032_MessageStatus
 *  7  | Amount                               | VAS_032_MessageAmount
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

        var isDisposed = false;
        var paymentsData = [];
        var pageNo = 1;
        var pageSize = 7;
        var totalPages = 0;

        function lbl(key, fallback) {
            var text = VIS.Msg.getMsg(key);
            return text && text !== '[' + key + ']' ? text : fallback;
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
                .append($('<th class="vas-recent-ap-payments-vendor">').text(lbl('VAS_032_MessageVendor', 'Vendor')))
                .append($('<th class="vas-recent-ap-payments-method-col">').text(lbl('VAS_032_MessageMethod', 'Method')))
                .append($('<th class="vas-recent-ap-payments-ref">').text(lbl('VAS_032_MessageRef', 'Ref')))
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
            var $row = $('<tr>');

            var statusClass = getStatusClass(payment.statusType);
            var statusText = payment.statusName || getStatusText(payment.statusType);

            $row
                .append($('<td class="vas-recent-ap-payments-date">')
                    .append($('<span class="vas-recent-ap-payments-cell-text">')
                        .text(formatDate(payment.paymentDate))))

                .append($('<td class="vas-recent-ap-payments-vendor">')
                    .append($('<span class="vas-recent-ap-payments-cell-text">')
                        .text(payment.vendorName || lbl('VAS_032_MessageNotSpecified', 'Not Specified'))))

                .append($('<td class="vas-recent-ap-payments-method-col">')
                    .append($('<span class="vas-recent-ap-payments-method">')
                        .text(payment.paymentMethodName || lbl('VAS_032_MessageNotSpecified', 'Not Specified'))))

                .append($('<td class="vas-recent-ap-payments-ref">')
                    .append($('<span class="vas-recent-ap-payments-cell-text">')
                        .text(payment.referenceNo || '')))

                .append($('<td class="vas-recent-ap-payments-status-col">')
                    .append($('<span class="vas-recent-ap-payments-status">')
                        .addClass(statusClass)
                        .text(statusText)))

                .append($('<td class="vas-recent-ap-payments-amount">')
                    .append($('<span class="vas-recent-ap-payments-cell-text">')
                        .text(formatCurrencyAmount(
                            payment.amount,
                            payment.currencySymbol,
                            payment.currencyISO,
                            payment.stdPrecision
                        ))));

            return $row;
        }

        function getStatusClass(statusType) {
            if (statusType === 'bounced') {
                return 'vas-recent-ap-payments-status-bounced';
            }

            if (statusType === 'intransit') {
                return 'vas-recent-ap-payments-status-intransit';
            }

            return 'vas-recent-ap-payments-status-cleared';
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
