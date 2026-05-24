/**
 * Bounced
 * Purpose - Shows outgoing AP payments that were reversed/bounced and need re-issue.
 *
 * ── Labels / Message Keys ─────────────────────────────────────────────
 *  #  | Current Text                         | Message Key
 * ----+--------------------------------------+--------------------------------
 *  1  | Bounced                              | VAS_Bounced
 *  2  | Action                               | VAS_Action
 *  3  | Need re-issue                        | VAS_NeedReissue
 *  4  | Loading                              | VAS_Loading
 *  5  | No Data                              | VAS_NoData
 * ─────────────────────────────────────────────────────────────────────
 */

; VIS = window.VIS || {};

; (function (VIS, $) {

    VIS.BouncedAPPaymentWidget = function () {

        this.frame;
        this.windowNo;
        this.AD_UserHomeWidgetID;

        var $root = $('<div class="vas-bounced-ap-payment-root">');
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
            $card = $('<div class="vas-bounced-ap-payment-card">');

            var $header = $('<div class="vas-bounced-ap-payment-header">');
            var $iconBox = $('<div class="vas-bounced-ap-payment-icon-box">');
            var $icon = $(
                '<svg class="vas-bounced-ap-payment-icon" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2" stroke-linecap="round" stroke-linejoin="round" aria-hidden="true" focusable="false">' +
                '<path d="M4 15s1-1 4-1 5 2 8 2 4-1 4-1V3s-1 1-4 1-5-2-8-2-4 1-4 1z"></path>' +
                '<line x1="4" y1="22" x2="4" y2="15"></line>' +
                '</svg>'
            );

            $title = $('<div class="vas-bounced-ap-payment-title">').text(lbl('VAS_Bounced', 'Bounced'));

            $iconBox.append($icon);
            $header.append($iconBox).append($title);

            $body = $('<div class="vas-bounced-ap-payment-body">');
            $value = $('<div class="vas-bounced-ap-payment-value">');
            $body.append($value);

            var $footer = $('<div class="vas-bounced-ap-payment-footer">');
            $badge = $('<div class="vas-bounced-ap-payment-badge">').text(lbl('VAS_Action', 'Action'));
            $description = $('<div class="vas-bounced-ap-payment-desc">').text(lbl('VAS_NeedReissue', 'Need re-issue'));

            $footer.append($badge).append($description);
            $card.append($header).append($body).append($footer);
            $root.empty().append($card);
        }

        function loadData() {
            setLoading();

            $.ajax({
                url: VIS.Application.contextUrl + 'BouncedAPPayment/GetBouncedAPPayments',
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
            var count = Number(data.bouncedPaymentCount);

            if (isNaN(count)) {
                setNoData();
                return;
            }

            $value.text(formatCount(count));
            $body.removeClass('vas-bounced-ap-payment-state');
        }

        function formatCount(value) {
            return Number(value || 0).toLocaleString(window.navigator.language, {
                minimumFractionDigits: 0,
                maximumFractionDigits: 0
            });
        }

        function setLoading() {
            if ($value) {
                $value.text(lbl('VAS_Loading', 'Loading'));
            }

            if ($body) {
                $body.addClass('vas-bounced-ap-payment-state');
            }
        }

        function setNoData() {
            if ($value) {
                $value.text(lbl('VAS_NoData', 'No Data'));
            }

            if ($body) {
                $body.addClass('vas-bounced-ap-payment-state');
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

    VIS.BouncedAPPaymentWidget.prototype.init = function (windowNo, frame) {
        this.frame = frame;
        this.AD_UserHomeWidgetID = frame.widgetInfo.AD_UserHomeWidgetID;
        this.windowNo = windowNo;
        this.Initalize();
        this.frame.getContentGrid().append(this.getRoot());
    };

    VIS.BouncedAPPaymentWidget.prototype.widgetSizeChange = function (height, width) {
    };

    VIS.BouncedAPPaymentWidget.prototype.refreshWidget = function () {
        this.refreshWidget();
    };

    VIS.BouncedAPPaymentWidget.prototype.dispose = function () {
        this.disposeComponent();

        if (this.frame) {
            this.frame.dispose();
        }

        this.frame = null;
    };

})(VIS, jQuery);