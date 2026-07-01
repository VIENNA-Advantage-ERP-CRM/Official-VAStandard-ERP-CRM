/**
 * Overdue Widget
 * Purpose - KPI card showing total overdue (past due date, unpaid) sales invoice amount.
 * Design   - Matches design2.md KPI/Summary widget: glass surface, tinted danger icon,
 *            large bold metric in danger red, WHY pill with count + explanatory copy.
 *
 * ── Labels / Message Keys ──────────────────────────────────────────────────────────────
 *  #  | Current Text                  | Message Key                | MsgText
 * ----+-------------------------------+----------------------------+------------------------
 *  1  | Overdue                       | VAS_059_Overdue            | Overdue
 *  2  | Past due date                 | VAS_059_PastDueDate         | Past Due Date
 *  3  | WHY                           | VAS_059_Why                | WHY
 *  4  | Past due date · loading…      | VAS_059_PastDueDateLoading | Past due date · loading…
 *  5  | chase these first.            | VAS_059_ChaseFirst         | chase these first.
 *  6  | No overdue invoices.          | VAS_059_NoOverdueInvoices  | No overdue invoices.
 *  7  | Past due date ·               | VAS_059_PastDueDatePrefix  | Past due date ·
 *  8  | invoice / invoices            | VAS_059_Invoice / VAS_059_Invoices | invoice / invoices
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

    VIS.OverdueWidget = function () {

        this.frame;
        this.windowNo;
        var $self = this;
        var $root = $('<div class="vas-ovd-root">');

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
                url: VIS.Application.contextUrl + 'Overdue/GetOverdue',
                type: 'GET',
                success: function (res) {
                    var data = typeof res === 'string' ? JSON.parse(res) : res;
                    if (data && !data.error) {
                        renderMetric(data.totalOverdue, data.invoiceCount, data.symbol, data.isoCode, data.stdPrecision);
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
            var symHtml = symbol ? '<span class="vas-ovd-cur">' + symbol + '</span>' : '';
            return sign + symHtml + absStr;
        }

        /* ── Render metric values ── */
        function renderMetric(total, count, symbol, isoCode, precision) {
            if ($metricEl) {
                $metricEl.html(formatMetric(total, symbol, isoCode, precision));
            }
            if ($whyText) {
                var invoiceLabel = count !== 1
                    ? lbl("VAS_059_Invoices", 'invoices')
                    : lbl("VAS_059_Invoice", 'invoice');
                var countStr = count > 0
                    ? count + ' ' + invoiceLabel + ' · ' + lbl("VAS_059_ChaseFirst", 'chase these first.')
                    : lbl("VAS_059_NoOverdueInvoices", 'No overdue invoices.');
                $whyText.text(lbl("VAS_059_PastDueDatePrefix", 'Past due date ·') + ' ' + countStr);
            }
        }

        /* ── Build DOM ── */
        function createWidget() {
            var uid = $self.AD_UserHomeWidgetID || 'ovd';

            var $card = $(
                '<div class="vas-ovd-card">'
            );

            /* ── Header row: icon + label ── */
            var $header = $(
                '<div class="vas-ovd-header">' +

                /* Icon well — pale red/danger tint matching design2.md semantic danger surface */
                '<div class="vas-ovd-icon">' +
                /* Clock icon (lucide-style inline SVG) */
                '<svg width="16" height="16" viewBox="0 0 24 24" fill="none" ' +
                'stroke="oklch(0.45 0.17 25)" stroke-width="1.8" ' +
                'stroke-linecap="round" stroke-linejoin="round">' +
                '<circle cx="12" cy="12" r="10"/>' +
                '<polyline points="12 6 12 12 16 14"/>' +
                '</svg>' +
                '</div>' +

                '<div>' +
                '<div id="VIS_Overdue" class="vas-ovd-title">' + lbl("VAS_059_Overdue", 'Overdue') + '</div>' +
                '<div class="vas-ovd-subtitle">' + lbl("VAS_059_PastDueDate", 'Past due date') + '</div>' +
                '</div>' +
                '</div>'
            );

            /* ── Metric value — danger red to match the screenshot ── */
            $metricEl = $(
                '<div id="vis-ovd-metric-' + uid + '" class="vas-ovd-metric">' +
                '—' +
                '</div>'
            );

            /* ── WHY pill + explanatory text ── */
            var $why = $(
                '<div class="vas-ovd-why-wrap">'
            );

            var $pill = $(
                /*'<span class="vas-ovd-why-pill">' + lbl("VAS_059_Why", 'WHY') + '</span>'*/
            );

            $whyText = $(
                '<span id="vis-ovd-why-' + uid + '" class="vas-ovd-why-text">' +
                lbl("VAS_059_PastDueDateLoading", 'Past due date · loading…') +
                '</span>'
            );

            $why.append($pill).append($whyText);
            $card.append($header).append($metricEl).append($why);
            $root.append($card);

            /* Busy/loading overlay over the whole card, using the core spinner classes. Hidden until
               a fetch is in flight; shown for both initial load and refresh. */
            $busy = $('<div class="vas-ovd-busy"><div class="vis-busyindicatorinnerwrap"><i class="vis_widgetloader"></i></div></div>');
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

    VIS.OverdueWidget.prototype.refreshWidget = function () {
        this.refreshWidget();
    };

    VIS.OverdueWidget.prototype.init = function (windowNo, frame) {
        this.frame = frame;
        this.AD_UserHomeWidgetID = frame.widgetInfo.AD_UserHomeWidgetID;
        this.windowNo = windowNo;
        this.Initalize();
        this.frame.getContentGrid().append(this.getRoot());

        /* Self-wire the dashboard-width CSS variable the title clamp reads. */
        ensureDashInlineSizeVar(this.getRoot());
    };

    VIS.OverdueWidget.prototype.widgetSizeChange = function (height, width) { };

    VIS.OverdueWidget.prototype.dispose = function () {
        this.disposeComponent();
        if (this.frame)
            this.frame.dispose();
        this.frame = null;
    };

})(VIS, jQuery);
