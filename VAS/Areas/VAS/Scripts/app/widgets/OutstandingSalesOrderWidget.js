/**
 * Outstanding Sales Order Widget
 * Purpose - KPI card showing total unpaid (outstanding) sales order value owed to the company.
 * Design   - Matches design2.md KPI/Summary widget: glass surface, tinted pale-blue icon,
 *            large bold metric, WHY pill with explanatory copy.
 *
 * ── Labels / Message Keys ──────────────────────────────────────────────────────────────
 *  #  | Current Text                                   | Message Key              | MsgText
 * ----+------------------------------------------------+--------------------------+-----------------------------------------------
 *  1  | Outstanding                                    | VIS_Outstanding          | Outstanding
 *  2  | Money owed to you                              | VIS_MoneyOwedToYou       | Money owed to you
 *  3  | WHY                                            | VIS_Why                  | WHY
 *  4  | Total unpaid invoices across all customers.    | VIS_TotalUnpaidInvoices  | Total unpaid invoices across all customers.
 *  5  | unpaid order / unpaid orders                   | VIS_UnpaidOrder / VIS_UnpaidOrders | unpaid order / unpaid orders
 *  6  | across all customers.                          | VIS_AcrossAllCustomers   | across all customers.
 *  7  | Largest:                                       | VIS_Largest              | Largest:
 * ──────────────────────────────────────────────────────────────────────────────────────
 */
; VIS = window.VIS || {};

; (function (VIS, $) {

    VIS.OutstandingSalesOrderWidget = function () {

        this.frame;
        this.windowNo;
        var $self = this;
        var $root = $('<div class="vas-oso-root">');

        var $metricEl;
        var $whyText;
        var $trendEl;

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
                        renderMetric(data.totalOutstanding, data.orderCount, data.topCustomer);
                    }
                },
                error: function () {
                    /* Leave placeholder values on error */
                }
            });
        }

        /* ── Format currency ── */
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

        /* ── Render metric values ── */
        function renderMetric(total, count, topCustomer) {
            if ($metricEl) {
                $metricEl.text(formatCurrency(total));
            }
            if ($whyText) {
                var orderLabel = count !== 1
                    ? lbl("VIS_UnpaidOrders", 'unpaid orders')
                    : lbl("VIS_UnpaidOrder", 'unpaid order');
                var why = lbl("VIS_TotalUnpaidInvoices", 'Total unpaid invoices across all customers.');
                if (count > 0) {
                    why = count + ' ' + orderLabel + ' ' + lbl("VIS_AcrossAllCustomers", 'across all customers.');
                }
                if (topCustomer) {
                    why += ' ' + lbl("VIS_Largest", 'Largest:') + ' ' + topCustomer + '.';
                }
                $whyText.text(why);
            }
        }

        /* ── Build DOM ── */
        function createWidget() {
            var uid = $self.AD_UserHomeWidgetID || 'oso';

            var $card = $(
                '<div class="vas-oso-card">'
            );

            /* ── Header row: icon + labels ── */
            var $header = $(
                '<div class="vas-oso-header">' +

                /* Icon well — pale blue, matching design2.md KPI tinted info surface */
                '<div class="vas-oso-icon">' +
                /* Dollar-circle icon (lucide-style inline SVG) */
                '<svg width="16" height="16" viewBox="0 0 24 24" fill="none" ' +
                'stroke="oklch(0.45 0.17 220)" stroke-width="1.8" ' +
                'stroke-linecap="round" stroke-linejoin="round">' +
                '<line x1="12" y1="1" x2="12" y2="23"/>' +
                '<path d="M17 5H9.5a3.5 3.5 0 000 7h5a3.5 3.5 0 010 7H6"/>' +
                '</svg>' +
                '</div>' +

                '<div>' +
                '<div class="vas-oso-title">' + lbl("VIS_Outstanding", 'Outstanding') + '</div>' +
                '<div class="vas-oso-subtitle">' + lbl("VIS_MoneyOwedToYou", 'Money owed to you') + '</div>' +
                '</div>' +
                '</div>'
            );

            /* ── Metric value ── */
            $metricEl = $(
                '<div id="vis-oso-metric-' + uid + '" class="vas-oso-metric">' +
                '—' +
                '</div>'
            );

            /* ── WHY pill + explanatory text ── */
            var $why = $(
                '<div class="vas-oso-why-wrap">'
            );

            var $pill = $(
                '<span class="vas-oso-why-pill">' + lbl("VIS_Why", 'WHY') + '</span>'
            );

            $whyText = $(
                '<span class="vas-oso-why-text">' +
                lbl("VIS_TotalUnpaidInvoices", 'Total unpaid invoices across all customers.') +
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
