/**
 * Bounced
 * Purpose - Shows outgoing AP payments that were reversed/bounced and need re-issue.
 *
 * ── Labels / Message Keys ─────────────────────────────────────────────
 *  #  | Current Text                         | Message Key
 * ----+--------------------------------------+--------------------------------
 *  1  | Bounced                              | VAS_030_MessageBounced
 *  2  | Need re-issue                        | VAS_030_MessageNeedReissue
 *  3  | Loading                              | VAS_030_MessageLoading
 *  4  | No Data                              | VAS_030_MessageNoData
 * ─────────────────────────────────────────────────────────────────────
 */


; VAS = window.VAS || {};

; (function (VAS, $) {
    "use strict";

    VAS.VAS_030_BouncedAPPaymentWidget = function () {
        this.frame = null;
        this.windowNo = 0;
        this.AD_UserHomeWidgetID = 0;

        var $root = $('<div class="vas-bounced-ap-payment-root">');
        var $card = null;
        var $value = null;
        var $description = null;
        var $body = null;
        var $footer = null;
        var $busy = null;
        var $state = null;
        var isDisposed = false;

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
                '<svg class="vas-bounced-ap-payment-icon" viewBox="0 0 24 24" fill="none" aria-hidden="true" focusable="false">' +
                '<path d="M4 15s1-1 4-1 5 2 8 2 4-1 4-1V3s-1 1-4 1-5-2-8-2-4 1-4 1z"></path>' +
                '<line x1="4" y1="22" x2="4" y2="15"></line>' +
                '</svg>'
            );

            var $title = $('<div class="vas-bounced-ap-payment-title">').text(
                lbl('VAS_030_MessageBounced', 'Bounced')
            );

            $iconBox.append($icon);
            $header.append($iconBox).append($title);

            $body = $('<div class="vas-bounced-ap-payment-body">');
            $value = $('<div class="vas-bounced-ap-payment-value">');
            $body.append($value);

            $footer = $('<div class="vas-bounced-ap-payment-footer">');

     
            $description = $('<div class="vas-bounced-ap-payment-desc">').text(
                lbl('VAS_030_MessageNeedReissue', 'Need re-issue')
            );

            $footer.append($description);
            $busy = $('<div class="vas-bounced-ap-payment-busy">').text(lbl('VAS_030_MessageLoading', 'Loading'));
            $state = $('<div class="vas-bounced-ap-payment-state-message">');

            $card.append($header).append($body).append($footer).append($busy).append($state);
            $root.empty().append($card);
        }

        function loadData() {
            if (isDisposed) {
                return;
            }

            showBusy(true);
            showState(false, '');

            $.ajax({
                url: VIS.Application.contextUrl + 'VAS_030_BouncedAPPaymentWidget/GetBouncedAPPayments',
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

        function renderData(data) {
            var count = Number(data.value);

            if (isNaN(count)) {
                count = Number(data.bouncedPaymentCount);
            }

            if (isNaN(count) || count <= 0) {
                setNoData();
                return;
            }

            showState(false, '');
            $value.text(formatCount(count));

            if ($description && data.description) {
                $description.text(data.description);
            }
        }

        function formatCount(value) {
            return Number(value || 0).toLocaleString(window.navigator.language, {
                minimumFractionDigits: 0,
                maximumFractionDigits: 0
            });
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
            showState(true, lbl('VAS_030_MessageNoData', 'No Data'));
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

            $card = null;
            $value = null;
            $description = null;
            $body = null;
            $footer = null;
            $busy = null;
            $state = null;
        };
    };

    VAS.VAS_030_BouncedAPPaymentWidget.prototype.init = function (windowNo, frame) {
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

    VAS.VAS_030_BouncedAPPaymentWidget.prototype.widgetSizeChange = function (height, width) {
        var $root = this.getRoot();

        if (!$root) {
            return;
        }

        $root.toggleClass(
            'vas-bounced-ap-payment-compact',
            (width && width < 240) || (height && height < 160)
        );
    };

    VAS.VAS_030_BouncedAPPaymentWidget.prototype.refreshWidget = function () {
        this.refreshData();
    };

    VAS.VAS_030_BouncedAPPaymentWidget.prototype.dispose = function () {
        this.disposeComponent();

        if (this.frame && this.frame.dispose) {
            this.frame.dispose();
        }

        this.frame = null;
    };

})(VAS, jQuery);
