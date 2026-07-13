/**
 * Outstanding Sales Order Widget
 * Purpose - KPI card showing total unpaid (outstanding) sales value owed to the company.
 * Design  - Implements the design.md "Glass Widget" + "KPI/Summary Widget" pattern:
 *           glass surface, pale-blue (info) icon tint, large bold metric, and a WHY
 *           pill with explanatory copy. The card fills 100% of its grid cell and
 *           scales responsively via OutstandingSalesOrderWidget.css.
 *
 * ── Labels / Message Keys ───────────────────────────────────────────────────────────────────────
 *  #  | Current Text                                   | Message Key                                | MsgText
 * ----+------------------------------------------------+--------------------------------------------+------------------------------------------------
 *  1  | Outstanding                                    | VAS_057_Outstanding                        | Outstanding
 *  2  | Money owed to you                              | VAS_057_MoneyOwedToYou                     | Money owed to you
 *  3  | WHY                                            | VAS_057_Why                                | WHY
 *  4  | Total unpaid invoices across all customers.    | VAS_057_TotalUnpaidInvoices                | Total unpaid invoices across all customers.
 *  5  | unpaid order / unpaid orders                   | VAS_057_UnpaidOrder / VAS_057_UnpaidOrders | unpaid order / unpaid orders
 *  6  | across all customers.                          | VAS_057_AcrossAllCustomers                 | across all customers.
 *  7  | Largest:                                       | VAS_057_Largest                            | Largest:
 * ───────────────────────────────────────────────────────────────────────────────────────────────
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

    /* Rising-bars icon (self-contained tile: its own pale-blue well + blue bars
       and an upward trend arrow). Fills the icon well via width/height 100%. */
    var ICON_SVG =
        '<svg viewBox="0 0 40 40" width="100%" height="100%" preserveAspectRatio="xMidYMid meet">' +
        '<rect width="40" height="40" rx="10" fill="#EAF8FF"/>' +
        '<rect x="9" y="26" width="5" height="8" rx="1" fill="#0083DA" opacity="0.4"/>' +
        '<rect x="17" y="20" width="5" height="14" rx="1" fill="#0083DA" opacity="0.7"/>' +
        '<rect x="25" y="15" width="5" height="19" rx="1" fill="#0083DA"/>' +
        '<polyline points="23,12 27,7 31,12" fill="none" stroke="#0083DA" stroke-width="1.6" stroke-linecap="round" stroke-linejoin="round"/>' +
        '<line x1="27" y1="7" x2="27" y2="15" stroke="#0083DA" stroke-width="1.6" stroke-linecap="round"/>' +
        '</svg>';

    VIS.OutstandingSalesOrderWidget = function () {

        this.frame;
        this.windowNo;
        var $self = this;
        var $root = $('<div class="vas-oso-root">');

        var $metricEl;
        var $whyText;
        /* Busy/loading overlay shown while data is being fetched (initial load + refresh). */
        var $busy;

        /* Resolve a translated label, falling back to readable English text. */
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
                url: VIS.Application.contextUrl + 'OutstandingSalesOrder/GetOutstanding',
                type: 'GET',
                success: function (res) {
                    var data = typeof res === 'string' ? JSON.parse(res) : res;
                    if (data && !data.error) {
                        renderMetric(data.totalOutstanding, data.orderCount, data.topCustomer, data.curSymbol, data.isoCode, data.stdPrecision);
                    }
                },
                error: function () {
                    /* Leave placeholder values on error */
                },
                complete: function () { showBusy(false); }
            });
        }

        /* ── Format a currency amount into a compact, locale-aware string ──
           Magnitude formatting (Indian vs international numbering by base currency,
           kept to the currency precision) is delegated to VIS.Util.formatCompactAmount
           (which always returns a non-negative string). The sign goes FIRST, then the
           currency symbol, then the magnitude, e.g. -$623.864 (not $-623.864). */
        function formatCurrency(value, symbol, isoCode, precision) {
            return (value < 0 ? '-' : '') + (symbol || '') + VIS.Util.formatCompactAmount(value, isoCode, precision);
        }

        /* ── Build the WHY explanatory copy from the optional count / top-customer data ── */
        function buildWhyText(count, topCustomer) {
            var why = lbl("VAS_057_TotalUnpaidInvoices", 'Total unpaid invoices across all customers.');

            if (count > 0) {
                var orderLabel = count !== 1
                    ? lbl("VAS_057_UnpaidOrders", 'unpaid orders')
                    : lbl("VAS_057_UnpaidOrder", 'unpaid order');
                why = count + ' ' + orderLabel + ' ' + lbl("VAS_057_AcrossAllCustomers", 'across all customers.');
            }
            if (topCustomer) {
                why += ' ' + lbl("VAS_057_Largest", 'Largest:') + ' ' + topCustomer + '.';
            }
            return why;
        }

        /* ── Render metric values ── */
        function renderMetric(total, count, topCustomer, symbol, isoCode, precision) {
            if ($metricEl) {
                /* Sign, then base-currency symbol (from the accounting schema), then amount:
                   a negative shows as -$623.864, not $-623.864. */
                $metricEl.text(formatCurrency(total, symbol, isoCode, precision));
            }
            if ($whyText) {
                $whyText.text(buildWhyText(count, topCustomer));
            }
        }

        /* ── Build DOM ── */
        function createWidget() {
            var uid = $self.AD_UserHomeWidgetID || 'oso';

            /* Header: pale-blue icon well + title / subtitle */
            var $header = $(
                '<div class="vas-oso-header">' +
                '<div class="vas-oso-icon">' + ICON_SVG + '</div>' +
                '<div class="vas-oso-labels">' +
                '<div class="vas-oso-title">' + lbl("VAS_057_Outstanding", 'Outstanding') + '</div>' +
                '<div class="vas-oso-subtitle">' + lbl("VAS_057_MoneyOwedToYou", 'Money owed to you') + '</div>' +
                '</div>' +
                '</div>'
            );

            /* Large bold metric (placeholder until data loads) */
            $metricEl = $('<div id="vis-oso-metric-' + uid + '" class="vas-oso-metric">—</div>');

            /* WHY pill + explanatory text */
            $whyText = $(
                '<span class="vas-oso-why-text">' +
                lbl("VAS_057_TotalUnpaidInvoices", 'Total unpaid invoices across all customers.') +
                '</span>'
            );

            var $why = $('<div class="vas-oso-why-wrap">')
                /*.append('<span class="vas-oso-why-pill">' + lbl("VAS_057_Why", 'WHY') + '</span>')*/
                .append($whyText);

            $root.append(
                $('<div class="vas-oso-card">')
                    .append($header)
                    .append($metricEl)
                    .append($why)
            );

            /* Busy/loading overlay over the whole card, using the core spinner classes. Hidden until
               a fetch is in flight; shown for both initial load and refresh. */
            $busy = $('<div class="vas-oso-busy"><div class="vis-busyindicatorinnerwrap"><i class="vis_widgetloader"></i></div></div>');
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

    VIS.OutstandingSalesOrderWidget.prototype.refreshWidget = function () {
        this.refreshWidget();
    };

    VIS.OutstandingSalesOrderWidget.prototype.init = function (windowNo, frame) {
        this.frame = frame;
        this.AD_UserHomeWidgetID = frame.widgetInfo.AD_UserHomeWidgetID;
        this.windowNo = windowNo;
        this.Initalize();
        this.frame.getContentGrid().append(this.getRoot());

        /* Self-wire the dashboard-width CSS variable the title clamp reads. */
        ensureDashInlineSizeVar(this.getRoot());
    };

    VIS.OutstandingSalesOrderWidget.prototype.widgetSizeChange = function (height, width) { };

    VIS.OutstandingSalesOrderWidget.prototype.dispose = function () {
        this.disposeComponent();
        if (this.frame)
            this.frame.dispose();
        this.frame = null;
    };

})(VIS, jQuery);
