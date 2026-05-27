/**
 * Outstanding Sales Order Widget
 * Purpose - KPI card showing total unpaid (outstanding) sales value owed to the company.
 * Design  - Implements the design.md "Glass Widget" + "KPI/Summary Widget" pattern:
 *           glass surface, pale-blue (info) icon tint, large bold metric, and a WHY
 *           pill with explanatory copy. The card fills 100% of its grid cell and
 *           scales responsively via OutstandingSalesOrderWidget.css.
 *
 * ── Labels / Message Keys ───────────────────────────────────────────────────────────────
 *  #  | Current Text                                   | Message Key                        | MsgText
 * ----+------------------------------------------------+------------------------------------+------------------------------------------------
 *  1  | Outstanding                                    | VIS_Outstanding                    | Outstanding
 *  2  | Money owed to you                              | VIS_MoneyOwedToYou                 | Money owed to you
 *  3  | WHY                                            | VIS_Why                            | WHY
 *  4  | Total unpaid invoices across all customers.    | VIS_TotalUnpaidInvoices            | Total unpaid invoices across all customers.
 *  5  | unpaid order / unpaid orders                   | VIS_UnpaidOrder / VIS_UnpaidOrders | unpaid order / unpaid orders
 *  6  | across all customers.                          | VIS_AcrossAllCustomers             | across all customers.
 *  7  | Largest:                                       | VIS_Largest                        | Largest:
 * ───────────────────────────────────────────────────────────────────────────────────────
 */
; VIS = window.VIS || {};

; (function (VIS, $) {

    /* Dollar-circle icon (lucide-style). Stroke inherits the icon-well color via currentColor. */
    var ICON_SVG =
        '<svg viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="1.8" ' +
        'stroke-linecap="round" stroke-linejoin="round">' +
        '<line x1="12" y1="1" x2="12" y2="23"/>' +
        '<path d="M17 5H9.5a3.5 3.5 0 000 7h5a3.5 3.5 0 010 7H6"/>' +
        '</svg>';

    VIS.OutstandingSalesOrderWidget = function () {

        this.frame;
        this.windowNo;
        var $self = this;
        var $root = $('<div class="vas-oso-root">');

        var $metricEl;
        var $whyText;

        /* Resolve a translated label, falling back to readable English text. */
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
                url: VIS.Application.contextUrl + 'OutstandingSalesOrder/GetOutstanding',
                type: 'GET',
                success: function (res) {
                    var data = typeof res === 'string' ? JSON.parse(res) : res;
                    if (data && !data.error) {
                        renderMetric(data.totalOutstanding, data.orderCount, data.topCustomer, data.curSymbol);
                    }
                },
                error: function () {
                    /* Leave placeholder values on error */
                }
            });
        }

        /* ── Format a currency amount into a compact, locale-aware string ── */
        function formatCurrency(value) {
            var stdPrecision = VIS.Env.getCtx().getStdPrecision();
            var sign = value < 0 ? '-' : '';
            var absVal = Math.abs(value);

            if (absVal >= 1000000) {
                return sign + (absVal / 1000000).toFixed(1).replace(/\.0$/, '') + 'M';
            }
            if (absVal >= 1000) {
                return sign + Math.round(absVal / 1000) + 'k';
            }
            return sign + absVal.toLocaleString(window.navigator.language, { minimumFractionDigits: stdPrecision, maximumFractionDigits: stdPrecision });
        }

        /* ── Build the WHY explanatory copy from the optional count / top-customer data ── */
        function buildWhyText(count, topCustomer) {
            var why = lbl("VIS_TotalUnpaidInvoices", 'Total unpaid invoices across all customers.');

            if (count > 0) {
                var orderLabel = count !== 1
                    ? lbl("VIS_UnpaidOrders", 'unpaid orders')
                    : lbl("VIS_UnpaidOrder", 'unpaid order');
                why = count + ' ' + orderLabel + ' ' + lbl("VIS_AcrossAllCustomers", 'across all customers.');
            }
            if (topCustomer) {
                why += ' ' + lbl("VIS_Largest", 'Largest:') + ' ' + topCustomer + '.';
            }
            return why;
        }

        /* ── Render metric values ── */
        function renderMetric(total, count, topCustomer, symbol) {
            if ($metricEl) {
                /* Base-currency symbol (from the accounting schema) sits before the amount. */
                $metricEl.text((symbol || '') + formatCurrency(total));
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
                        '<div class="vas-oso-title">' + lbl("VIS_Outstanding", 'Outstanding') + '</div>' +
                        '<div class="vas-oso-subtitle">' + lbl("VIS_MoneyOwedToYou", 'Money owed to you') + '</div>' +
                    '</div>' +
                '</div>'
            );

            /* Large bold metric (placeholder until data loads) */
            $metricEl = $('<div id="vis-oso-metric-' + uid + '" class="vas-oso-metric">—</div>');

            /* WHY pill + explanatory text */
            $whyText = $(
                '<span class="vas-oso-why-text">' +
                lbl("VIS_TotalUnpaidInvoices", 'Total unpaid invoices across all customers.') +
                '</span>'
            );

            var $why = $('<div class="vas-oso-why-wrap">')
                /*.append('<span class="vas-oso-why-pill">' + lbl("VIS_Why", 'WHY') + '</span>')*/
                .append($whyText);

            $root.append(
                $('<div class="vas-oso-card">')
                    .append($header)
                    .append($metricEl)
                    .append($why)
            );
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
    };

    VIS.OutstandingSalesOrderWidget.prototype.widgetSizeChange = function (height, width) { };

    VIS.OutstandingSalesOrderWidget.prototype.dispose = function () {
        this.disposeComponent();
        if (this.frame)
            this.frame.dispose();
        this.frame = null;
    };

})(VIS, jQuery);
