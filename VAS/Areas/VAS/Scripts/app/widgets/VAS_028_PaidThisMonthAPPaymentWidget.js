
/**
* Paid This Month Widget
* Purpose - KPI card showing total payments received from customers in the current calendar month.
* Design   - Matches design2.md KPI/Summary widget: glass surface, tinted success icon,
*            large bold metric in success green, WHY pill with customer count + explanatory copy.
*
* ── Labels / Message Keys ──────────────────────────────────────────────────────────────
*  #  | Current Text                                        | Message Key                  | MsgText
* ----+-----------------------------------------------------+------------------------------+-----------------------------------------------------
*  1  | Paid this month                                     | VAS_028_MessagePaidThisMonth | Paid this month
*  2  | Cash paid                                           | VAS_028_MessageCashPaid      | Cash paid
*  3  | Paid to                                             | VAS_028_MessagePaidTo        | Paid to
*  4  | vendor / vendors                                    | VAS_028_MessageVendor / VAS_028_MessageVendors | vendor / vendors
*  5  | so far this month.                                  | VAS_028_MessageSoFarThisMonth | so far this month.
*  6  | Loading                                             | VAS_028_MessageLoading       | Loading
*  7  | No Data                                             | VAS_028_MessageNoData        | No Data
*  8  | No payments this month.                             | VAS_028_MessageNoPaymentsThisMonth | No payments this month.
* ──────────────────────────────────────────────────────────────────────────────────────
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
        var isDisposed = false;

        function lbl(key, fallback) {
            var text = VIS.Msg.getMsg(key);
            return text && text !== '[' + key + ']' ? text : fallback;
        }

        function showBusy(show) {
            if ($busy && $busy.length) {
                $busy.toggleClass('is-visible', !!show);
            }
        }

        function showState(show, message) {
            if ($state && $state.length) {
                $state.text(message || '').toggleClass('is-visible', !!show);
            }

            if ($body) {
                $body.toggle(!show);
            }

            if ($why) {
                $why.toggle(!show);
            }
        }

        this.Initalize = function () {
            createWidget();
            loadData();
        };

        function createWidget() {
            var uid = self.AD_UserHomeWidgetID || 'ptm';

            var $card = $('<div class="vas-ptm-card">');
            var $header = $('<div class="vas-ptm-header">');

            var $icon = $(
                '<div class="vas-ptm-icon">' +
                '<svg viewBox="0 0 24 24" fill="none" aria-hidden="true" focusable="false">' +
                '<polyline points="20 6 9 17 4 12"></polyline>' +
                '</svg>' +
                '</div>'
            );

            var $headerText = $('<div class="vas-ptm-header-text">');

            var $title = $('<div class="vas-ptm-title">').text(
                lbl('VAS_028_MessagePaidThisMonth', 'Paid this month')
            );

            var $subtitle = $('<div class="vas-ptm-subtitle">').text(
                lbl('VAS_028_MessageCashPaid', 'Cash paid')
            );

            $headerText.append($title).append($subtitle);
            $header.append($icon).append($headerText);

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
                .text(lbl('VAS_028_MessageLoading', 'Loading'));

            $why.append($whyText);
            $busy = $('<div class="vas-ptm-busy">').text(lbl('VAS_028_MessageLoading', 'Loading'));
            $state = $('<div class="vas-ptm-state-message">');

            $card.append($header).append($body).append($why).append($busy).append($state);
            $root.empty().append($card);
        }

        function loadData() {
            if (isDisposed) {
                return;
            }

            showBusy(true);
            showState(false, '');

            $.ajax({
                url: VIS.Application.contextUrl + 'VAS_028_PaidThisMonthAPPaymentWidget/GetPaidThisMonth',
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

                    renderMetric(data);
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

        function renderMetric(data) {
            var amount = Number(data.value);

            if (isNaN(amount)) {
                amount = Number(data.paidThisMonth);
            }

            if (isNaN(amount)) {
                amount = Number(data.totalPaidAmount);
            }

            if (isNaN(amount) || amount <= 0) {
                setNoData();
                return;
            }

            showState(false, '');

            var symbol = data.currencySymbol || data.symbol || '';
            var precision = normalizePrecision(data.precision);
            var vendorCount = Number(data.vendorCount || data.paymentCount || 0);

            if ($metricEl) {
                $metricEl.text(formatMetric(amount, symbol, precision));
            }

            if ($whyText) {
                $whyText.text(getWhyText(vendorCount, data.description));
            }
        }

        function getWhyText(vendorCount, fallbackDescription) {
            if (vendorCount > 0) {
                var vendorLabel = vendorCount === 1
                    ? lbl('VAS_028_MessageVendor', 'vendor')
                    : lbl('VAS_028_MessageVendors', 'vendors');

                return lbl('VAS_028_MessagePaidTo', 'Paid to') +
                    ' ' +
                    vendorCount +
                    ' ' +
                    vendorLabel +
                    ' ' +
                    lbl('VAS_028_MessageSoFarThisMonth', 'so far this month.');
            }

            return fallbackDescription || lbl('VAS_028_MessageNoPaymentsThisMonth', 'No payments this month.');
        }

        function formatMetric(value, symbol, precision) {
            var numericValue = Number(value || 0);
            var sign = numericValue < 0 ? '-' : '';
            var absValue = Math.abs(numericValue);

            return sign + symbol + formatAmount(absValue, precision);
        }

        function formatAmount(value, precision) {
            var numericValue = Number(value || 0);

            if (numericValue >= 1000000) {
                return (numericValue / 1000000).toFixed(1).replace(/\.0$/, '') + 'M';
            }

            if (numericValue >= 1000) {
                return Math.round(numericValue / 1000) + 'k';
            }

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

        function setNoData() {
            if ($metricEl) {
                $metricEl.text('—');
            }

            if ($whyText) {
                $whyText.text('');
            }

            showState(true, lbl('VAS_028_MessageNoData', 'No Data'));
        }

        this.refreshData = function () {
            loadData();
        };

        this.getRoot = function () {
            return $root;
        };

        this.disposeComponent = function () {
            isDisposed = true;

            $root.remove();

            $metricEl = null;
            $whyText = null;
            $body = null;
            $why = null;
            $busy = null;
            $state = null;
        };
    };

    VAS.VAS_028_PaidThisMonthAPPaymentWidget = VAS.VAS_028_PaidThisMonthAPPaymentWidget;

    VAS.VAS_028_PaidThisMonthAPPaymentWidget.prototype.init = function (windowNo, frame) {
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

    VAS.VAS_028_PaidThisMonthAPPaymentWidget.prototype.widgetSizeChange = function (height, width) {
        var $root = this.getRoot();

        if (!$root) {
            return;
        }

        $root.toggleClass(
            'vas-ptm-compact',
            (width && width < 240) || (height && height < 160)
        );
    };

    VAS.VAS_028_PaidThisMonthAPPaymentWidget.prototype.refreshWidget = function () {
        this.refreshData();
    };

    VAS.VAS_028_PaidThisMonthAPPaymentWidget.prototype.dispose = function () {
        this.disposeComponent();

        if (this.frame && this.frame.dispose) {
            this.frame.dispose();
        }

        this.frame = null;
    };

})(VAS, jQuery);
