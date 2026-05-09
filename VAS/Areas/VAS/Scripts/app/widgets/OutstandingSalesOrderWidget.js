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
        var $root = $('<div style="height:100%;font-family:Roboto,sans-serif;">');

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
            if (value >= 1000000) {
                return '$' + (value / 1000000).toFixed(1).replace(/\.0$/, '') + 'M';
            }
            if (value >= 1000) {
                return '$' + Math.round(value / 1000) + 'k';
            }
            return '$' + Math.round(value).toLocaleString('en-US');
        }

        /* ── Render metric values ── */
        function renderMetric(total, count, topCustomer) {
            if ($metricEl) {
                $metricEl.text(formatCurrency(total));
            }
            if ($whyText) {
                var orderLabel = count !== 1
                    ? lbl("VIS_UnpaidOrders", 'unpaid orders')
                    : lbl("VIS_UnpaidOrder",  'unpaid order');
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
                '<div style="' +
                    'background:linear-gradient(180deg,rgba(255,255,255,0.82) 0%,rgba(255,255,255,0.58) 100%);' +
                    'border:2px solid #fff;' +
                    'border-radius:14px;' +
                    'box-shadow:0 10px 24px rgba(15,61,97,0.06);' +
                    'padding:16px 18px 18px;' +
                    'height:100%;' +
                    'box-sizing:border-box;' +
                    'display:flex;flex-direction:column;justify-content:space-between;' +
                '">'
            );

            /* ── Header row: icon + labels ── */
            var $header = $(
                '<div style="display:flex;align-items:flex-start;gap:10px;margin-bottom:10px;">' +

                    /* Icon well — pale blue, matching design2.md KPI tinted info surface */
                    '<div style="' +
                        'width:36px;height:36px;border-radius:8px;flex-shrink:0;' +
                        'background:oklch(0.92 0.07 220);' +
                        'display:flex;align-items:center;justify-content:center;' +
                    '">' +
                        /* Dollar-circle icon (lucide-style inline SVG) */
                        '<svg width="16" height="16" viewBox="0 0 24 24" fill="none" ' +
                            'stroke="oklch(0.45 0.17 220)" stroke-width="1.8" ' +
                            'stroke-linecap="round" stroke-linejoin="round">' +
                            '<line x1="12" y1="1" x2="12" y2="23"/>' +
                            '<path d="M17 5H9.5a3.5 3.5 0 000 7h5a3.5 3.5 0 010 7H6"/>' +
                        '</svg>' +
                    '</div>' +

                    '<div>' +
                        '<div style="font-size:13px;font-weight:600;color:#102C3F;line-height:1.2;">' + lbl("VIS_Outstanding", 'Outstanding') + '</div>' +
                        '<div style="font-size:11px;color:#748494;letter-spacing:0.3px;text-transform:uppercase;margin-top:1px;">' + lbl("VIS_MoneyOwedToYou", 'Money owed to you') + '</div>' +
                    '</div>' +
                '</div>'
            );

            /* ── Metric value ── */
            $metricEl = $(
                '<div id="vis-oso-metric-' + uid + '" ' +
                    'style="font-size:40px;font-weight:700;color:#102C3F;line-height:1;margin-bottom:8px;">' +
                    '—' +
                '</div>'
            );

            /* ── WHY pill + explanatory text ── */
            var $why = $(
                '<div style="display:flex;align-items:flex-start;gap:6px;margin-top:auto;">'
            );

            var $pill = $(
                '<span style="' +
                    'display:inline-flex;align-items:center;' +
                    'background:oklch(0.96 0.03 220);' +
                    'padding:1px 7px;border-radius:999px;flex-shrink:0;margin-top:2px;' +
                    'font-size:9px;font-weight:700;letter-spacing:0.08em;' +
                    'color:oklch(0.45 0.15 220);font-family:Roboto,monospace;' +
                '">' + lbl("VIS_Why", 'WHY') + '</span>'
            );

            $whyText = $(
                '<span style="font-size:11px;color:#748494;line-height:1.45;">' +
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

    VIS.OutstandingSalesOrderWidget.prototype.refreshWidget = function () {};

    VIS.OutstandingSalesOrderWidget.prototype.init = function (windowNo, frame) {
        this.frame               = frame;
        this.AD_UserHomeWidgetID = frame.widgetInfo.AD_UserHomeWidgetID;
        this.windowNo            = windowNo;
        this.Initalize();
        this.frame.getContentGrid().append(this.getRoot());
    };

    VIS.OutstandingSalesOrderWidget.prototype.widgetSizeChange = function (height, width) {};

    VIS.OutstandingSalesOrderWidget.prototype.dispose = function () {
        this.disposeComponent();
        if (this.frame)
            this.frame.dispose();
        this.frame = null;
    };

})(VIS, jQuery);
