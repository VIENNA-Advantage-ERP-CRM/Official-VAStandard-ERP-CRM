/**
 * Auto Allocated Widget
 * Purpose - Show percentage of AR receipts auto-matched to invoices.
 *
 * ── Labels / Message Keys ─────────────────────────────────────────────
 *  #  | Current Text                         | Message Key
 * ----+--------------------------------------+--------------------------------
 *  1  | Auto-allocated                       | VIS_AutoAllocated
 *  2  | Receipts auto-matched to invoices    | VIS_ReceiptsAutoMatchedInvoices
 *  3  | WHY                                  | VIS_Why
 *  4  | Loading…                             | VIS_Loading
 *  5  | No data                              | VIS_NoData
 * ─────────────────────────────────────────────────────────────────────
 */
; VIS = window.VIS || {};

; (function (VIS, $) {

    VIS.AutoAllocatedWidget = function () {

        this.frame;
        this.windowNo;

        var $root = $('<div class="vas-aa-root">');
        var $percentText;

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
                url: VIS.Application.contextUrl + 'AutoAllocated/GetAutoAllocated',
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

                    renderPercent(data);
                },
                error: function () {
                    setNoData();
                }
            });
        }

        function setLoading() {
            if ($percentText) {
                $percentText.text(lbl("VIS_Loading", "Loading…"));
            }
        }

        function setNoData() {
            if ($percentText) {
                $percentText.text("0%");
            }
        }

        function formatPercent(value) {
            value = Number(value || 0);
            return value.toFixed(0) + "%";
        }

        function renderPercent(data) {
            var percent = 0;

            if (data) {
                percent = data.autoAllocatedPercent || 0;
            }

            if ($percentText) {
                $percentText.text(formatPercent(percent));
            }
        }

        function createWidget() {
            var $card = $(
                '<div class="vas-aa-card">' +
                '<div class="vas-aa-header">' +
                '<div class="vas-aa-icon-wrap">' +
                '<svg width="25" height="25" viewBox="0 0 24 24" fill="none" ' +
                'stroke="#A06D13" stroke-width="1.9" stroke-linecap="round" stroke-linejoin="round">' +
                '<path d="M9 11l3 3L22 4"/>' +
                '<path d="M21 12v7a2 2 0 0 1-2 2H5a2 2 0 0 1-2-2V5a2 2 0 0 1 2-2h11"/>' +
                '</svg>' +
                '</div>' +
                '<div class="vas-aa-title">' +
                lbl("VIS_AutoAllocated", "Auto-allocated") +
                '</div>' +
                '</div>' +

                '<div class="vas-aa-percent-row">' +
                '<span class="vas-aa-percent">' +
                lbl("VIS_Loading", "Loading…") +
                '</span>' +
                '</div>' +

                '<div class="vas-aa-footer">' +
                '<span class="vas-aa-why">' +
                lbl("VIS_Why", "WHY") +
                '</span>' +
                '<span class="vas-aa-desc">' +
                lbl("VIS_ReceiptsAutoMatchedInvoices", "Receipts auto-matched to invoices") +
                '</span>' +
                '</div>' +
                '</div>'
            );

            $percentText = $card.find('.vas-aa-percent');
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

    VIS.AutoAllocatedWidget.prototype.init = function (windowNo, frame) {
        this.frame = frame;
        this.AD_UserHomeWidgetID = frame.widgetInfo.AD_UserHomeWidgetID;
        this.windowNo = windowNo;
        this.Initalize();
        this.frame.getContentGrid().append(this.getRoot());
    };

    VIS.AutoAllocatedWidget.prototype.widgetSizeChange = function (height, width) {
    };

    VIS.AutoAllocatedWidget.prototype.refreshWidget = function () {
        this.refreshWidget();
    };

    VIS.AutoAllocatedWidget.prototype.dispose = function () {
        this.disposeComponent();

        if (this.frame) {
            this.frame.dispose();
        }

        this.frame = null;
    };

})(VIS, jQuery);