/**
 * Received This Month Widget
 * Purpose - Show total AR receipt amount received this month.
 *
 * ── Labels / Message Keys ─────────────────────────────────────────────
 *  #  | Current Text                       | Message Key
 * ----+------------------------------------+--------------------------------
 *  1  | Received this month                | VIS_ReceivedThisMonth
 *  2  | WHY                                | VIS_Why
 *  3  | Customer collections posted so far | VIS_CustomerCollectionsPostedSoFar
 *  4  | Loading…                           | VIS_Loading
 *  5  | No data                            | VIS_NoData
 * ─────────────────────────────────────────────────────────────────────
 */
; VIS = window.VIS || { }
;

; (function(VIS, $) {

    VIS.ReceivedThisMonthWidget = function() {

        this.frame;
        this.windowNo;

        var $root = $('<div class="vas-rtm-root">');
        var $amountText;
        var $whyDesc;

        function lbl(key, fallback)
        {
            var t = VIS.Msg.getMsg(key);
            return (t && t.charAt(0) !== '[') ? t : fallback;
        }

        this.Initalize = function() {
            createWidget();
            loadData();
        }
        ;

        function loadData()
        {
            setLoading();

            $.ajax({
            url: VIS.Application.contextUrl + 'TotalAmountReceivedThisMonth/GetAmountReceivedThisMonth',
                type: 'GET',
                success: function(res) {
                    var data = res;

                    if (typeof data === 'string')
                    {
                        data = JSON.parse(data);
                    }

                    if (typeof data === 'string')
                    {
                        data = JSON.parse(data);
                    }

                    if (data && data.error)
                    {
                        setNoData();
                        return;
                    }

                    renderData(data);
                },
                error: function() {
                    setNoData();
                }
            });
        }

        function setLoading()
        {
            if ($amountText) {
                $amountText.text("...");
            }

            if ($whyDesc) {
                $whyDesc.text(lbl("VIS_Loading", "Loading…"));
            }
        }

        function setNoData()
        {
            if ($amountText) {
                $amountText.text("₹0.00");
            }

            if ($whyDesc) {
                $whyDesc.text(lbl("VIS_NoData", "No data"));
            }
        }

        function renderData(data)
        {
            var amount = Number(data && data.totalAmountReceivedJanuary || 0);

            if ($amountText) {
                $amountText.text(formatCompactAmount(amount));
            }

            if ($whyDesc) {
                $whyDesc.text(lbl("VIS_CustomerCollectionsPostedSoFar", "Customer collections posted so far"));
            }
        }

        function formatCompactAmount(value)
        {
            value = Number(value || 0);

            if (value >= 10000000)
            {
                return  (value / 10000000).toFixed(2).replace(/\.00$/, "") + "Cr";
            }

            if (value >= 100000)
            {
                return  (value / 100000).toFixed(2).replace(/\.00$/, "") + "L";
            }

            if (value >= 1000)
            {
                return  (value / 1000).toFixed(2).replace(/\.00$/, "") + "K";
            }

            return value.toLocaleString("en-IN", {
            minimumFractionDigits: 2,
                maximumFractionDigits: 2
            });
        }

        function createWidget()
        {
            var $card = $(
                '<div class="widget kpi tint-info span-3x1">' +

                '<div class="kpi-head">' +
                '<div class="kpi-icon">' +
                '<svg viewBox="0 0 24 24" fill="none" stroke="currentColor" ' +
                'stroke-width="2" stroke-linecap="round" stroke-linejoin="round">' +
                '<polyline points="22 12 16 12 14 15 10 15 8 12 2 12"></polyline>' +
                '<path d="M5.45 5.11 2 12v6a2 2 0 0 0 2 2h16a2 2 0 0 0 2-2v-6l-3.45-6.89A2 2 0 0 0 16.76 4H7.24a2 2 0 0 0-1.79 1.11z"></path>' +
                '</svg>' +
                '</div>' +
                '<span class="kpi-label">' +
                lbl("VIS_ReceivedThisMonth", "Received this month") +
                '</span>' +
                '</div>' +

                '<span class="kpi-value">...</span>' +

                '<div class="kpi-why">' +
                '<span class="kpi-why-tag">' +
                lbl("VIS_Why", "WHY") +
                '</span>' +
                '<span>' +
                lbl("VIS_CustomerCollectionsPostedSoFar", "Customer collections posted so far") +
                '</span>' +
                '</div>' +

                '</div>'
            );

            $amountText = $card.find('.kpi-value');
            $whyDesc = $card.find('.kpi-why span:last-child');

            $root.append($card);
        }

        this.refreshWidget = function() {
            loadData();
        }
        ;

        this.getRoot = function() {
            return $root;
        }
        ;

        this.disposeComponent = function() {
            $root.remove();
        }
        ;
    }
    ;

    VIS.ReceivedThisMonthWidget.prototype.init = function(windowNo, frame) {
        this.frame = frame;
        this.AD_UserHomeWidgetID = frame.widgetInfo.AD_UserHomeWidgetID;
        this.windowNo = windowNo;

        this.Initalize();
        this.frame.getContentGrid().append(this.getRoot());
    }
    ;

    VIS.ReceivedThisMonthWidget.prototype.widgetSizeChange = function(height, width) {
    }
    ;

    VIS.ReceivedThisMonthWidget.prototype.refreshWidget = function() {
        this.refreshWidget();
    }
    ;

    VIS.ReceivedThisMonthWidget.prototype.dispose = function() {
        this.disposeComponent();

        if (this.frame)
        {
            this.frame.dispose();
        }

        this.frame = null;
    }
    ;

})(VIS, jQuery);