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
        var $root = $('<div class="vas-ovd-root">');

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
                        '<div id="VIS_Overdue" class="vas-ovd-title">' + lbl("VIS_OverDue", 'Overdue') + '</div>' +
                        '<div class="vas-ovd-subtitle">' + lbl("VIS_PastDueDate", 'Past due date') + '</div>' +
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
                '<span class="vas-ovd-why-pill">' + lbl("VIS_Why", 'WHY') + '</span>'
            );

            $whyText = $(
                '<span id="vis-ovd-why-' + uid + '" class="vas-ovd-why-text">' +
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

    VIS.OverdueWidget.prototype.refreshWidget = function () {
        this.refreshWidget();
    };

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
