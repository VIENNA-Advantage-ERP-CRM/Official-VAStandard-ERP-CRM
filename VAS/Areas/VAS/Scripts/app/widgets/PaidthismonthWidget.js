/**
 * Paid This Month Widget
 * Purpose - KPI card showing total payments received from customers in the current calendar month.
 * Design   - Matches design2.md KPI/Summary widget: glass surface, tinted success icon,
 *            large bold metric in success green, WHY pill with customer count + explanatory copy.
 *
 * ── Labels / Message Keys ──────────────────────────────────────────────────────────────
 *  #  | Current Text                                        | Message Key                  | MsgText
 * ----+-----------------------------------------------------+------------------------------+-----------------------------------------------------
 *  1  | Paid this month                                     | VIS_PaidThisMonth            | Paid this month
 *  2  | Cash received                                       | VIS_CashReceived             | Cash received
 *  3  | WHY                                                 | VIS_Why                      | WHY
 *  4  | Loading…                                            | VIS_Loading                  | Loading…
 *  5  | Received from ... customer/s so far this month.     | VIS_ReceivedFromCustomers    | Received from
 *  6  | customer / customers                                | VIS_Customer / VIS_Customers | customer / customers
 *  7  | so far this month.                                  | VIS_SoFarThisMonth           | so far this month.
 *  8  | No payments received this month.                    | VIS_NoPaymentsThisMonth      | No payments received this month.
 * ──────────────────────────────────────────────────────────────────────────────────────
 */
; VIS = window.VIS || {};

; (function (VIS, $) {

    VIS.PaidthismonthWidget = function () {

        this.frame;
        this.windowNo;
        var $self = this;
        var $root = $('<div class="vas-ptm-root">');

        var $metricEl;
        var $whyText;

        function lbl(key, fallback) {
            var t = VIS.Msg.getMsg(key);
            return (t && t.charAt(0) !== '[') ? t : fallback;
        }

        /* ── Initialize ── */
        this.Initalize = function () {
            createWidget();
            loadData();
        };

        /* ── Load data from backend ── */
        function loadData() {
            $.ajax({
                url: VIS.Application.contextUrl + 'PaidThisMonth/GetPaidThisMonth',
                type: 'GET',
                success: function (res) {
                    var data = typeof res === 'string' ? JSON.parse(res) : res;
                    if (data && !data.error) {
                        renderMetric(data.totalPaidAmount, data.customerCount);
                    }
                },
                error: function () {
                    /* Leave placeholder values on error */
                }
            });
        }

        /* ── Format currency ── */
        function formatCurrency(value) {
            if (value >= 1000000) {
                return (value / 1000000).toFixed(1).replace(/\.0$/, '') + 'M';
            }
            if (value >= 1000) {
                return Math.round(value / 1000) + 'k';
            }
            return value.toLocaleString(window.navigator.language, { minimumFractionDigits: 2, maximumFractionDigits: 2 });
        }

        /* ── Render metric values ── */
        function renderMetric(total, count) {
            if ($metricEl) {
                $metricEl.text(formatCurrency(total));
            }
            if ($whyText) {
                var customerLabel = count !== 1
                    ? lbl("VIS_Customers", 'customers')
                    : lbl("VIS_Customer", 'customer');
                var countStr = count > 0
                    ? lbl("VIS_ReceivedFromCustomers", 'Received from') + ' ' + count + ' ' + customerLabel + ' ' + lbl("VIS_SoFarThisMonth", 'so far this month.')
                    : lbl("VIS_NoPaymentsThisMonth", 'No payments received this month.');
                $whyText.text(countStr);
            }
        }

        /* ── Build DOM ── */
        function createWidget() {
            var uid = $self.AD_UserHomeWidgetID || 'ptm';

            var $card = $(
                '<div class="vas-ptm-card">'
            );

            /* ── Header row: icon + label ── */
            var $header = $(
                '<div class="vas-ptm-header">' +

                /* Icon well — pale green/success tint matching design2.md semantic success surface */
                '<div class="vas-ptm-icon">' +
                /* Checkmark icon (lucide-style inline SVG) */
                '<svg width="16" height="16" viewBox="0 0 24 24" fill="none" ' +
                'stroke="oklch(0.40 0.14 155)" stroke-width="1.8" ' +
                'stroke-linecap="round" stroke-linejoin="round">' +
                '<polyline points="20 6 9 17 4 12"/>' +
                '</svg>' +
                '</div>' +

                '<div>' +
                '<div class="vas-ptm-title">' + lbl("VIS_PaidThisMonth", 'Paid this month') + '</div>' +
                '<div class="vas-ptm-subtitle">' + lbl("VIS_CashReceived", 'Cash received') + '</div>' +
                '</div>' +
                '</div>'
            );

            /* ── Metric value — success green ── */
            $metricEl = $(
                '<div id="vis-ptm-metric-' + uid + '" class="vas-ptm-metric">' +
                '—' +
                '</div>'
            );

            /* ── WHY pill + explanatory text ── */
            var $why = $(
                '<div class="vas-ptm-why-wrap">'
            );

            var $pill = $(
                '<span class="vas-ptm-why-pill">' + lbl("VIS_Why", 'WHY') + '</span>'
            );

            $whyText = $(
                '<span id="vis-ptm-why-' + uid + '" class="vas-ptm-why-text">' +
                lbl("VIS_Loading", 'Loading…') +
                '</span>'
            );

            $why.append($pill).append($whyText);
            $card.append($header).append($metricEl).append($why);
            $root.append($card);
        }

        /* ── Refresh ── */
        this.refreshWidget = function () {
            loadData();
        };

        /* ── Root accessor ── */
        this.getRoot = function () {
            return $root;
        };

        this.disposeComponent = function () {
            $root.remove();
        };
    };

    VIS.PaidthismonthWidget.prototype.refreshWidget = function () {
        this.refreshWidget();
    };

    VIS.PaidthismonthWidget.prototype.init = function (windowNo, frame) {
        this.frame = frame;
        this.AD_UserHomeWidgetID = frame.widgetInfo.AD_UserHomeWidgetID;
        this.windowNo = windowNo;
        this.Initalize();
        this.frame.getContentGrid().append(this.getRoot());
    };

    VIS.PaidthismonthWidget.prototype.widgetSizeChange = function (height, width) { };

    VIS.PaidthismonthWidget.prototype.dispose = function () {
        this.disposeComponent();
        if (this.frame)
            this.frame.dispose();
        this.frame = null;
    };

})(VIS, jQuery);
