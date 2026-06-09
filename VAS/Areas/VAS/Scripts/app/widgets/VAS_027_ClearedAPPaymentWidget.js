/**
 * Cleared AP Payment
 * Purpose - Shows the percentage of AP payments from the previous calendar month that have been reconciled.
 *
 * Labels / Message Keys
 * 1 | Cleared                                | VAS_027_messageCleared
 * 2 | WHY                                    | VAS_027_messageWhy
 * 3 | Of last month's AP payments reconciled | VAS_027_messageAPPaymentClearedWhy
 * 4 | Loading                                | VAS_027_messageLoading
 * 5 | No Data                                | VAS_027_messageNoData
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
        var isDisposed = false;

        function lbl(key, fallback) {
            var text = VIS.Msg.getMsg(key);
            return text && text !== '[' + key + ']' ? text : fallback;
        }

        function createWidget() {
            $card = $('<div class="vas-finance-kpi-card">');

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

            $value = $('<div class="vas-finance-kpi-value">').text(
                lbl('VAS_027_messageLoading', 'Loading')
            );

            $body.append($value);

            var $footer = $('<div class="vas-finance-kpi-footer">');


            var $description = $('<div class="vas-finance-kpi-desc">').text(
                lbl('VAS_027_messageAPPaymentClearedWhy', "Of last month's AP payments reconciled")
            );

            $footer.append($description);

            $card.append($header).append($body).append($footer);

            $root.empty().append($card);
        }

        function loadData() {
            if (isDisposed) {
                return;
            }

            setState(lbl('VAS_027_messageLoading', 'Loading'), true);

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
                        setState(lbl('VAS_027_messageNoData', 'No Data'), true);
                        return;
                    }

                    renderData(data);
                },

                error: function () {
                    if (!isDisposed) {
                        setState(lbl('VAS_027_messageNoData', 'No Data'), true);
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

        function renderData(data) {
            var percentage = Number(data.value);

            if (isNaN(percentage)) {
                percentage = Number(data.clearedPercentage);
            }

            if (isNaN(percentage)) {
                setState(lbl('VAS_027_messageNoData', 'No Data'), true);
                return;
            }

            percentage = Math.max(0, Math.min(percentage, 100));

            setState(formatPercent(percentage, data.precision), false);
        }

        function formatPercent(value, precision) {
            var stdPrecision = normalizePrecision(precision);

            return Number(value || 0).toLocaleString(window.navigator.language, {
                minimumFractionDigits: stdPrecision,
                maximumFractionDigits: stdPrecision
            }) + '%';
        }

        function normalizePrecision(precision) {
            var stdPrecision = Number(precision);

            if (isNaN(stdPrecision) || stdPrecision < 0) {
                return 2;
            }

            return stdPrecision;
        }

        function setState(text, isStateText) {
            if ($value) {
                $value.text(text);
            }

            if ($body) {
                $body.toggleClass('vas-finance-kpi-state', !!isStateText);
            }
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

            $root.remove();

            $card = null;
            $value = null;
            $body = null;
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
