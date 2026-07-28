/**
 * Paid This Month Widget
 * Purpose - KPI card showing total payments received from customers in the current calendar month.
 * Design   - Matches design2.md KPI/Summary widget: glass surface, tinted success icon,
 *            large bold metric in success green, WHY pill with customer count + explanatory copy.
 *
 * ── Labels / Message Keys ──────────────────────────────────────────────────────────────
 *  #  | Current Text                                        | Message Key                       | MsgText
 * ----+-----------------------------------------------------+-----------------------------------+-----------------------------------------------------
 *  1  | Paid this month                                     | VAS_058_PaidThisMonth             | Paid this month
 *  2  | Cash received                                       | VAS_058_CashReceived              | Cash received
 *  3  | WHY                                                 | VAS_058_Why                       | WHY
 *  5  | Received from ... customer/s so far this month.     | VAS_058_ReceivedFrom              | Received from
 *  6  | customer / customers                                | VAS_058_Customer / VAS_058_Customers | customer / customers
 *  7  | so far this month.                                  | VAS_058_SoFarThisMonth            | so far this month.
 *  8  | No payments received this month.                    | VAS_058_NoPaymentsThisMonth       | No payments received this month.
 * ──────────────────────────────────────────────────────────────────────────────────────
 */
; VIS = window.VIS || {};

; (function (VIS, $) {

    /* design.md §Widget Header / §Measurement Setup: keep --dash-inline-size on
       :root equal to the dashboard container's current pixel width so the title
       clamp resolves against the dashboard's visible content area, not the
       viewport. A single document-level ResizeObserver serves every widget (the
       var is global); without a marked container — or without ResizeObserver —
       the CSS falls back to 100vw. */
    function ensureDashInlineSizeVar($el) {
        if (window.__vasDashInlineSizeObserver) { return; }
        if (typeof ResizeObserver === 'undefined') { return; }

        var container = $el.closest('.vis-widget-container, [data-dashboard-container]')[0];
        if (!container) { return; }

        var write = function () {
            document.documentElement.style.setProperty('--dash-inline-size', container.clientWidth + 'px');
        };

        window.__vasDashInlineSizeObserver = new ResizeObserver(write);
        window.__vasDashInlineSizeObserver.observe(container);
        write();
    }

    VIS.PaidthismonthWidget = function () {

        this.frame;
        this.windowNo;
        var $self = this;
        var $root = $('<div class="vas-ptm-root">');

        var $metricEl;
        var $whyText;
        /* Busy/loading overlay shown while data is being fetched (initial load + refresh). */
        var $busy;

        function lbl(key, fallback) {
            var t = VIS.Msg.getMsg(key);
            return (t && t.charAt(0) !== '[') ? t : fallback;
        }

        /* Toggle the busy/loading overlay. */
        function showBusy(show) {
            if (!$busy || !$busy[0]) { return; }
            $busy[0].style.visibility = show ? 'visible' : 'hidden';
        }

        /* ── Initialize ── */
        this.Initalize = function () {
            createWidget();
            loadData();
        };

        /* ── Load data from backend ── */
        function loadData() {
            showBusy(true);
            $.ajax({
                url: VIS.Application.contextUrl + 'PaidThisMonth/GetPaidThisMonth',
                type: 'GET',
                success: function (res) {
                    var data = typeof res === 'string' ? JSON.parse(res) : res;
                    if (data && !data.error) {
                        renderMetric(data.totalPaidAmount, data.customerCount, data.symbol, data.isoCode, data.stdPrecision);
                    }
                },
                error: function () {
                    /* Leave placeholder values on error */
                },
                complete: function () { showBusy(false); }
            });
        }

        /* Build metric markup with the base-currency symbol placed *before* the
           amount; the minus sign (if any) precedes the symbol (e.g. -$1.2M). The
           compact magnitude (Indian vs international numbering by base currency,
           kept to the currency precision) comes from VIS.Util.formatCompactAmount. */
        function formatMetric(value, symbol, isoCode, precision) {
            value = Number(value || 0);
            var sign = value < 0 ? '-' : '';
            var absStr = VIS.Util.formatCompactAmount(value, isoCode, precision);
            var symHtml = symbol ? '<span class="vas-ptm-cur">' + symbol + '</span>' : '';
            return sign + symHtml + absStr;
        }

        /* ── Render metric values ── */
        function renderMetric(total, count, symbol, isoCode, precision) {
            if ($metricEl) {
                $metricEl.html(formatMetric(total, symbol, isoCode, precision));
            }
            if ($whyText) {
                var customerLabel = count !== 1
                    ? lbl("VAS_058_Customers", 'customers')
                    : lbl("VAS_058_Customer", 'customer');
                var countStr = count > 0
                    ? lbl("VAS_058_ReceivedFrom", 'Received from') + ' ' + count + ' ' + customerLabel + ' ' + lbl("VAS_058_SoFarThisMonth", 'so far this month.')
                    : lbl("VAS_058_NoPaymentsThisMonth", 'No payments received this month.');
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
                '<div class="vas-ptm-title">' + lbl("VAS_058_PaidThisMonth", 'Paid This Month') + '</div>' +
                '<div class="vas-ptm-subtitle">' + lbl("VAS_058_CashReceived", 'Amount Received') + '</div>' +
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
                /*'<span class="vas-ptm-why-pill">' + lbl("VAS_058_Why", 'WHY') + '</span>'*/
            );

            /* Empty until data loads; the busy overlay covers the wait. */
            $whyText = $(
                '<span id="vis-ptm-why-' + uid + '" class="vas-ptm-why-text"></span>'
            );

            $why.append($pill).append($whyText);
            $card.append($header).append($metricEl).append($why);
            $root.append($card);

            /* Busy/loading overlay over the whole card, using the core spinner classes. Hidden until
               a fetch is in flight; shown for both initial load and refresh. */
            $busy = $('<div class="vas-ptm-busy"><div class="vis-busyindicatorinnerwrap"><i class="vis_widgetloader"></i></div></div>');
            $busy[0].style.visibility = 'hidden';
            $root.append($busy);
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

        /* Self-wire the dashboard-width CSS variable the title clamp reads. */
        ensureDashInlineSizeVar(this.getRoot());
    };

    VIS.PaidthismonthWidget.prototype.widgetSizeChange = function (height, width) { };

    VIS.PaidthismonthWidget.prototype.dispose = function () {
        this.disposeComponent();
        if (this.frame)
            this.frame.dispose();
        this.frame = null;
    };

})(VIS, jQuery);
