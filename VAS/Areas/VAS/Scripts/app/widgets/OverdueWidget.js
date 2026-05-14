/**
 * Overdue Widget
 * Purpose - KPI card showing total overdue (past due date, unpaid) sales invoice amount.
 * Design   - Matches design2.md KPI/Summary widget: glass surface, tinted danger icon,
 *            large bold metric in danger red, WHY pill with count + explanatory copy.
 *
 * ── Labels / Message Keys ──────────────────────────────────────────────────────────────
 *  #  | Current Text                  | Message Key              | MsgText
 * ----+-------------------------------+--------------------------+------------------------
 *  1  | Overdue                       | VIS_OverDue              | Overdue
 *  2  | Past due date                 | VIS_PastDueDate          | Past Due Date
 *  3  | WHY                           | VIS_Why                  | WHY
 *  4  | Past due date · loading…      | VIS_PastDueDateLoading   | Past due date · loading…
 *  5  | chase these first.            | VIS_ChaseFirst           | chase these first.
 *  6  | No overdue invoices.          | VIS_NoOverdueInvoices    | No overdue invoices.
 *  7  | Past due date ·               | VIS_PastDueDatePrefix    | Past due date ·
 *  8  | invoice / invoices            | VIS_Invoice / VIS_Invoices | invoice / invoices
 * ──────────────────────────────────────────────────────────────────────────────────────
 */
; VIS = window.VIS || {};

; (function (VIS, $) {

    VIS.OverdueWidget = function () {

        this.frame;
        this.windowNo;
        var $self = this;
        var $root = $('<div style="height:100%;width:100%;font-family:Roboto,sans-serif;">');

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
                url: VIS.Application.contextUrl + 'Overdue/GetOverdue',
                type: 'GET',
                success: function (res) {
                    var data = typeof res === 'string' ? JSON.parse(res) : res;
                    if (data && !data.error) {
                        renderMetric(data.totalOverdue, data.invoiceCount);
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
                var invoiceLabel = count !== 1
                    ? lbl("VIS_Invoices", 'invoices')
                    : lbl("VIS_Invoice",  'invoice');
                var countStr = count > 0
                    ? count + ' ' + invoiceLabel + ' · ' + lbl("VIS_ChaseFirst", 'chase these first.')
                    : lbl("VIS_NoOverdueInvoices", 'No overdue invoices.');
                $whyText.text(lbl("VIS_PastDueDatePrefix", 'Past due date ·') + ' ' + countStr);
            }
        }

        /* ── Build DOM ── */
        function createWidget() {
            var uid = $self.AD_UserHomeWidgetID || 'ovd';

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

            /* ── Header row: icon + label ── */
            var $header = $(
                '<div style="display:flex;align-items:flex-start;gap:10px;margin-bottom:10px;">' +

                    /* Icon well — pale red/danger tint matching design2.md semantic danger surface */
                    '<div style="' +
                        'width:36px;height:36px;border-radius:8px;flex-shrink:0;' +
                        'background:oklch(0.95 0.04 25);' +
                        'display:flex;align-items:center;justify-content:center;' +
                    '">' +
                        /* Clock icon (lucide-style inline SVG) */
                        '<svg width="16" height="16" viewBox="0 0 24 24" fill="none" ' +
                            'stroke="oklch(0.45 0.17 25)" stroke-width="1.8" ' +
                            'stroke-linecap="round" stroke-linejoin="round">' +
                            '<circle cx="12" cy="12" r="10"/>' +
                            '<polyline points="12 6 12 12 16 14"/>' +
                        '</svg>' +
                    '</div>' +

                    '<div>' +
                        '<div id="VIS_Overdue" style="font-size:13px;font-weight:600;color:#102C3F;line-height:1.2;">' + lbl("VIS_OverDue", 'Overdue') + '</div>' +
                        '<div style="font-size:11px;color:#748494;letter-spacing:0.3px;text-transform:uppercase;margin-top:1px;">' + lbl("VIS_PastDueDate", 'Past due date') + '</div>' +
                    '</div>' +
                '</div>'
            );

            /* ── Metric value — danger red to match the screenshot ── */
            $metricEl = $(
                '<div id="vis-ovd-metric-' + uid + '" ' +
                    'style="font-size:40px;font-weight:700;color:#ED1C24;line-height:1;margin-bottom:8px;">' +
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
                '<span id="vis-ovd-why-' + uid + '" style="font-size:11px;color:#748494;line-height:1.45;">' +
                    lbl("VIS_PastDueDateLoading", 'Past due date · loading…') +
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

    VIS.OverdueWidget.prototype.refreshWidget = function () {};

    VIS.OverdueWidget.prototype.init = function (windowNo, frame) {
        this.frame               = frame;
        this.AD_UserHomeWidgetID = frame.widgetInfo.AD_UserHomeWidgetID;
        this.windowNo            = windowNo;
        this.Initalize();
        this.frame.getContentGrid().append(this.getRoot());
    };

    VIS.OverdueWidget.prototype.widgetSizeChange = function (height, width) {};

    VIS.OverdueWidget.prototype.dispose = function () {
        this.disposeComponent();
        if (this.frame)
            this.frame.dispose();
        this.frame = null;
    };

})(VIS, jQuery);
