/**
 * Bounced Cheques Widget
 * Purpose - Show count of bounced cheques from AR receipts.
 *
 * ── Labels / Message Keys ─────────────────────────────────────────────
 *  #  | Current Text              | Message Key
 * ----+---------------------------+--------------------------------
 *  1  | Bounced cheques           | VIS_BouncedCheques
 *  2  | Customer follow-up        | VIS_CustomerFollowUp
 *  3  | ACTION                    | VIS_Action
 *  4  | Loading…                  | VIS_Loading
 *  5  | No data                   | VIS_NoData
 * ─────────────────────────────────────────────────────────────────────
 */
; VIS = window.VIS || {};

; (function (VIS, $) {

    VIS.BouncedChequesWidget = function () {

        this.frame;
        this.windowNo;

        var $root = $('<div class="vas-bc-root">');
        var $countText;

        function lbl(key, fallback) {
            var t = VIS.Msg.getMsg(key);
            return (t && t.charAt(0) !== '[') ? t : fallback;
        }

        this.Initalize = function () {
            createWidget();
            loadData();
        };

        function loadData() {
            setLoading();

            $.ajax({
                url: VIS.Application.contextUrl + 'BouncedCheques/GetBouncedCheques',
                type: 'GET',
                success: function (res) {
                    var data = res;

                    if (typeof data === 'string') {
                        data = JSON.parse(data);
                    }

                    if (typeof data === 'string') {
                        data = JSON.parse(data);
                    }

                    if (data && data.error) {
                        setNoData();
                        return;
                    }

                    renderCount(data);
                },
                error: function () {
                    setNoData();
                }
            });
        }

        function setLoading() {
            if ($countText) {
                $countText.text(lbl("VIS_Loading", "Loading…"));
            }
        }

        function setNoData() {
            if ($countText) {
                $countText.text("0");
            }
        }

        function renderCount(data) {
            var count = 0;

            if (data) {
                count = data.bouncedChequeCount || 0;
            }

            if ($countText) {
                $countText.text(count);
            }
        }

        function createWidget() {
            var $card = $(
                '<div class="vas-bc-card">' +
                '<div class="vas-bc-header">' +
                '<div class="vas-bc-icon-wrap">' +
                '<svg width="25" height="25" viewBox="0 0 24 24" fill="none" ' +
                'stroke="#F3A8A8" stroke-width="1.9" stroke-linecap="round" stroke-linejoin="round">' +
                '<circle cx="12" cy="12" r="9"/>' +
                '<line x1="12" y1="7" x2="12" y2="13"/>' +
                '<line x1="12" y1="17" x2="12.01" y2="17"/>' +
                '</svg>' +
                '</div>' +
                '<div class="vas-bc-title">' +
                lbl("VIS_BouncedCheques", "Bounced cheques") +
                '</div>' +
                '</div>' +

                '<div class="vas-bc-count-row">' +
                '<span class="vas-bc-count">' +
                lbl("VIS_Loading", "Loading…") +
                '</span>' +
                '</div>' +

                '<div class="vas-bc-footer">' +
                '<span class="vas-bc-action">' +
                lbl("VIS_Action", "ACTION") +
                '</span>' +
                '<span class="vas-bc-desc">' +
                lbl("VIS_CustomerFollowUp", "Customer follow-up") +
                '</span>' +
                '</div>' +
                '</div>'
            );

            $countText = $card.find('.vas-bc-count');
            $root.append($card);
        }

        this.refreshWidget = function () {
            loadData();
        };

        this.getRoot = function () {
            return $root;
        };

        this.disposeComponent = function () {
            $root.remove();
        };
    };

    VIS.BouncedChequesWidget.prototype.init = function (windowNo, frame) {
        this.frame = frame;
        this.AD_UserHomeWidgetID = frame.widgetInfo.AD_UserHomeWidgetID;
        this.windowNo = windowNo;
        this.Initalize();
        this.frame.getContentGrid().append(this.getRoot());
    };

    VIS.BouncedChequesWidget.prototype.widgetSizeChange = function (height, width) {
    };

    VIS.BouncedChequesWidget.prototype.refreshWidget = function () {
        this.refreshWidget();
    };

    VIS.BouncedChequesWidget.prototype.dispose = function () {
        this.disposeComponent();

        if (this.frame) {
            this.frame.dispose();
        }

        this.frame = null;
    };

})(VIS, jQuery);