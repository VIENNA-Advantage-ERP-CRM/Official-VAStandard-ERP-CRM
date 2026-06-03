/**
 * Recent payments
 * Purpose - Shows the latest outgoing AP payments with vendor, payment method, invoice/order reference, status, and amount.
 *
 * ── Labels / Message Keys ─────────────────────────────────────────────
 *  #  | Current Text                         | Message Key
 * ----+--------------------------------------+--------------------------------
 *  1  | Recent payments                      | VAS_032_MessageRecentPayments
 *  2  | + New payment                        | VAS_032_MessageNewPayment
 *  3  | Review                               | VAS_032_MessageReview
 *  4  | {0} payments auto-matched to bills   | VAS_032_MessagePaymentsAutoMatchedToBills
 *  5  | Aura reconciled {0} based on amount + vendor | VAS_032_MessageAuraReconciledBasedOnAmountVendor
 *  6  | Date                                 | VAS_032_MessageDate
 *  7  | Vendor                               | VAS_032_MessageVendor
 *  8  | Method                               | VAS_032_MessageMethod
 *  9  | Ref                                  | VAS_032_MessageRef
 * 10  | Status                               | VAS_032_MessageStatus
 * 11  | Amount                               | VAS_032_MessageAmount
 * 12  | Loading                              | VAS_032_MessageLoading
 * 13  | No Data                              | VAS_032_MessageNoData
 * ─────────────────────────────────────────────────────────────────────
 */

; VIS = window.VIS || {};

; (function (VIS, $) {

    VIS.VAS_032_RecentAPPaymentsWidget = function () {

        this.frame;
        this.windowNo;
        this.AD_UserHomeWidgetID;

        var $root = $('<div class="vas-recent-ap-payments-root">');
        var $card;
        var $banner;
        var $bannerTitle;
        var $bannerSub;
        var $tableWrap;

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
            
            $iconBox.append($icon);
            $titleWrap.append($iconBox).append($title);
            $head.append($titleWrap);

            $banner = $('<div class="vas-recent-ap-payments-banner">');
            var $bannerIcon = $(
                '<div class="vas-recent-ap-payments-banner-icon">' +
                '<svg viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2" stroke-linecap="round" stroke-linejoin="round" aria-hidden="true" focusable="false">' +
                '<polygon points="13 2 3 14 12 14 11 22 21 10 12 10 13 2"></polygon>' +
                '</svg>' +
                '</div>'
            );
            var $bannerText = $('<div class="vas-recent-ap-payments-banner-text">');
            $bannerTitle = $('<div class="vas-recent-ap-payments-banner-title">');
            $bannerSub = $('<div class="vas-recent-ap-payments-banner-sub">');
            var $review = $('<button type="button" class="vas-recent-ap-payments-review">').text(lbl('VAS_032_MessageReview', 'Review'));

            $bannerText.append($bannerTitle).append($bannerSub);
            $banner.append($bannerIcon).append($bannerText).append($review);

            $tableWrap = $('<div class="vas-recent-ap-payments-table-wrap">');

            $card.append($head).append($banner).append($tableWrap);
            $root.empty().append($card);
        }

        function loadData() {
            setLoading();

            $.ajax({
                url: VIS.Application.contextUrl + 'VAS_032_RecentAPPaymentsWidget/GetRecentAPPayments',
                type: 'GET',
                dataType: 'json',
                cache: false,
                success: function (response) {
                    var data = response;

                    if (typeof response === 'string') {
                        try {
                            data = JSON.parse(response);
                        }
                        catch (e) {
                            setNoData();
                            return;
                        }
                    }

                    if (!data || data.error) {
                        setNoData();
                        return;
                    }

                    renderData(data);
                },
                error: function () {
                    setNoData();
                }
            });
        }

        function renderData(data) {
            var payments = $.isArray(data.payments) ? data.payments : [];

            if (payments.length === 0) {
                setNoData();
                return;
            }

            renderBanner(data);
            renderTable(payments);
        }

        function renderBanner(data) {
            var count = Number(data.autoMatchedCount || 0);
            var refs = $.isArray(data.autoMatchedRefs) ? data.autoMatchedRefs.join(', ') : '';

            if (count <= 0) {
                $banner.hide();
                return;
            }

            $banner.show();

            $bannerTitle.text(
                lbl('VAS_032_MessagePaymentsAutoMatchedToBills', '{0} payments auto-matched to bills').replace('{0}', count.toLocaleString(window.navigator.language))
            );

            $bannerSub.text(
                lbl('VAS_032_MessageAuraReconciledBasedOnAmountVendor', 'Aura reconciled {0} based on amount + vendor').replace('{0}', refs || count.toLocaleString(window.navigator.language))
            );
        }

        function renderTable(payments) {
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

            for (var i = 0; i < payments.length && i < 7; i++) {
                $tbody.append(createPaymentRow(payments[i]));
            }

            $table.append($tbody);
            $tableWrap.empty().append($table);
        }

        function createPaymentRow(payment) {
            var $row = $('<tr>');

            var statusClass = getStatusClass(payment.statusType);
            var statusText = payment.statusName || getStatusText(payment.statusType);

            $row
                .append($('<td class="vas-recent-ap-payments-date">').append($('<span class="vas-recent-ap-payments-cell-text">').text(formatDate(payment.paymentDate))))
                .append($('<td class="vas-recent-ap-payments-vendor">').append($('<span class="vas-recent-ap-payments-cell-text">').text(payment.vendorName || lbl('VAS_032_MessageNotSpecified', 'Not Specified'))))
                .append($('<td class="vas-recent-ap-payments-method-col">').append($('<span class="vas-recent-ap-payments-method">').text(payment.paymentMethodName || lbl('VAS_032_MessageNotSpecified', 'Not Specified'))))
                .append($('<td class="vas-recent-ap-payments-ref">').append($('<span class="vas-recent-ap-payments-cell-text">').text(payment.referenceNo || '')))
                .append($('<td class="vas-recent-ap-payments-status-col">').append($('<span class="vas-recent-ap-payments-status">').addClass(statusClass).text(statusText)))
                .append($('<td class="vas-recent-ap-payments-amount">').append($('<span class="vas-recent-ap-payments-cell-text">').text(formatCurrencyAmount(payment.amount, payment.currencySymbol, payment.currencyISO))));

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

        function formatCurrencyAmount(value, currencySymbol, currencyISO) {
            var numericValue = Number(value || 0);
            var stdPrecision = 2;

            if (VIS && VIS.Env && VIS.Env.getCtx && VIS.Env.getCtx().getStdPrecision) {
                stdPrecision = Number(VIS.Env.getCtx().getStdPrecision());
            }

            if (isNaN(stdPrecision) || stdPrecision < 0) {
                stdPrecision = 2;
            }

            return numericValue.toLocaleString(window.navigator.language, {
                minimumFractionDigits: stdPrecision,
                maximumFractionDigits: stdPrecision
            });
        }

        function setLoading() {
            if ($banner) {
                $banner.hide();
            }

            if ($tableWrap) {
                $tableWrap.empty().append($('<div class="vas-recent-ap-payments-state">').text(lbl('VAS_032_MessageLoading', 'Loading')));
            }
        }

        function setNoData() {
            if ($banner) {
                $banner.hide();
            }

            if ($tableWrap) {
                $tableWrap.empty().append($('<div class="vas-recent-ap-payments-state">').text(lbl('VAS_032_MessageNoData', 'No Data')));
            }
        }

        this.refreshWidget = function () {
            loadData();
        };

        this.getRoot = function () {
            return $root;
        };

        this.disposeComponent = function () {
            $root.remove();
            $card = null;
            $banner = null;
            $bannerTitle = null;
            $bannerSub = null;
            $tableWrap = null;
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
