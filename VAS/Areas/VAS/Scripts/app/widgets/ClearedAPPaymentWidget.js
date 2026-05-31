/**
 * Cleared AP Payment
 * Purpose - Shows the percentage of AP payments from the previous calendar month that have been reconciled.
 *
 * ── Labels / Message Keys ─────────────────────────────────────────────
 *  #  | Current Text                         | Message Key
 * ----+--------------------------------------+--------------------------------
 *  1  | Cleared                              | VAS_Cleared
 *  2  | WHY                                  | VAS_Why
 *  3  | Of last month's AP payments reconciled | VAS_APPaymentClearedWhy
 *  4  | Loading                              | VAS_Loading
 *  5  | No Data                              | VAS_NoData
 * ─────────────────────────────────────────────────────────────────────
 */

; VIS = window.VIS || {};

; (function (VIS, $) {

    VIS.ClearedAPPaymentWidget = function () {

        this.frame;
        this.windowNo;
        this.AD_UserHomeWidgetID;

        var $self = this;
        var $root = $('<div class="vas-cleared-ap-payment-root">');
        var $card;
        var $title;
        var $value;
        var $badge;
        var $description;
        var $body;

        function lbl(key, fallback) {
            var text = VIS.Msg.getMsg(key);
            return text && text !== '[' + key + ']' ? text : fallback;
        }

        this.Initalize = function () {
            createWidget();
            loadData();
        };

        function createWidget() {
            $card = $('<div class="vas-cleared-ap-payment-card">');

            var $header = $('<div class="vas-cleared-ap-payment-header">');
            var $iconBox = $('<div class="vas-cleared-ap-payment-icon-box">');
            var $icon = $(
                '<svg class="vas-cleared-ap-payment-icon" viewBox="0 0 24 24" aria-hidden="true" focusable="false">' +
                '<path fill="currentColor" d="M9.2 16.6 4.95 12.35 6.36 10.94 9.2 13.77 17.64 5.34 19.05 6.75z"></path>' +
                '</svg>'
            );

            $title = $('<div class="vas-cleared-ap-payment-title">').text(lbl('VAS_Cleared', 'Cleared'));
            $iconBox.append($icon);
            $header.append($iconBox).append($title);

            $body = $('<div class="vas-cleared-ap-payment-body">');
            $value = $('<div class="vas-cleared-ap-payment-value">');
            $body.append($value);

            var $footer = $('<div class="vas-cleared-ap-payment-footer">');
            $badge = $('<div class="vas-cleared-ap-payment-badge">').text(lbl('VAS_Why', 'WHY'));
            $description = $('<div class="vas-cleared-ap-payment-desc">').text(lbl('VAS_APPaymentClearedWhy', "Of last month's AP payments reconciled"));
            $footer.append($badge).append($description);

            $card.append($header).append($body).append($footer);
            $root.empty().append($card);
        }

        function loadData() {
            setLoading();

            $.ajax({
                url: VIS.Application.contextUrl + 'ClearedAPPayment/GetClearedAPPayment',
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
            var percentage = Number(data.clearedPercentage);

            if (isNaN(percentage)) {
                setNoData();
                return;
            }

            if (percentage < 0) {
                percentage = 0;
            }

            if (percentage > 100) {
                percentage = 100;
            }

            $value.text(formatPercentage(percentage));
            $body.removeClass('vas-cleared-ap-payment-state');
        }

        function formatPercentage(value) {
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
            }) + '%';
        }

        function setLoading() {
            if ($value) {
                $value.text(lbl('VAS_Loading', 'Loading'));
            }

            if ($body) {
                $body.addClass('vas-cleared-ap-payment-state');
            }
        }

        function setNoData() {
            if ($value) {
                $value.text(lbl('VAS_NoData', 'No Data'));
            }

            if ($body) {
                $body.addClass('vas-cleared-ap-payment-state');
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
            $title = null;
            $value = null;
            $badge = null;
            $description = null;
            $body = null;
        };
    };

    VIS.ClearedAPPaymentWidget.prototype.init = function (windowNo, frame) {
        this.frame = frame;
        this.AD_UserHomeWidgetID = frame.widgetInfo.AD_UserHomeWidgetID;
        this.windowNo = windowNo;
        this.Initalize();
        this.frame.getContentGrid().append(this.getRoot());
    };

    VIS.ClearedAPPaymentWidget.prototype.widgetSizeChange = function (height, width) {
        var $root = this.getRoot();

        if (!$root) {
            return;
        }

        if ((width && width < 240) || (height && height < 160)) {
            $root.addClass('vas-cleared-ap-payment-compact');
        }
        else {
            $root.removeClass('vas-cleared-ap-payment-compact');
        }
    };

    VIS.ClearedAPPaymentWidget.prototype.refreshWidget = function () {
        this.refreshWidget();
    };

    VIS.ClearedAPPaymentWidget.prototype.dispose = function () {
        this.disposeComponent();

        if (this.frame) {
            this.frame.dispose();
        }

        this.frame = null;
    };

})(VIS, jQuery);