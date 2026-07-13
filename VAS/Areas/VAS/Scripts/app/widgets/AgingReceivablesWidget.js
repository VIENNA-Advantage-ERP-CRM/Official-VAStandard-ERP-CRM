/**
 * Aging Receivables Widget
 * Purpose - Shows outstanding (unpaid) sales-invoice amounts bucketed by how far past due
 *           they are, as a horizontal bar chart on the finance/receivables dashboard.
 * Design  - Onfinity glass widget (design.md §8): light glass surface, tinted icon well,
 *           title + subtitle header, bucket rows with a labelled track bar, and a WHY block.
 *           Amounts are prefixed with the base (accounting-schema) currency symbol.
 *
 * ── Labels / Message Keys ──────────────────────────────────────────────────────────────
 *  #  | Current Text                                          | Message Key              | MsgText
 * ----+-------------------------------------------------------+--------------------------+----------------------------------------------------
 *  1  | Aging Receivables                                     | VAS_060_AgingReceivables | Aging Receivables
 *  2  | Who owes, how old                                     | VAS_060_WhoOwesHowOld    | Who owes, how old
 *  3  | Not yet due                                           | VAS_060_NotYetDue        | Not yet due
 *  4  | 1–30 days late                                        | VAS_060_Days1_30         | 1–30 days late
 *  5  | 31–60 days                                            | VAS_060_Days31_60        | 31–60 days
 *  6  | 61–90 days                                            | VAS_060_Days61_90        | 61–90 days
 *  7  | 90+ days                                              | VAS_060_Days90Plus       | 90+ days
 *  8  | WHY                                                   | VAS_060_Why              | WHY
 *  9  | Older invoices are harder to collect. Focus on the…   | VAS_060_AgingWhyText     | Older invoices are harder to collect. Focus on the 61+ buckets.
 *  11 | No data                                               | VAS_060_NoData           | No data
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

    /* Bucket bar colours — aligned to the Onfinity semantic palette
       (foundations.md > Semantic Colors), progressing success → warning →
       danger as overdue age (collection risk) increases. */
    var BUCKET_COLORS = {
        notDue:    '#019D89', // success (not yet due)
        days1_30:  '#D78B10', // warning
        days31_60: '#9A6500', // warning (deep)
        days61_90: '#D14545', // danger (mid)
        days90Plus:'#ED1C24'  // danger
    };

    VIS.AgingReceivablesWidget = function () {

        this.frame;
        this.windowNo;
        var $self = this;
        var $root = $('<div class="vas-ar-root">');

        var $contentArea;
        /* WHY caption — a sibling of the content area (not nested inside it). */
        var $whyBlock;
        /* Base-currency symbol from the backend; prefixed before every amount. */
        var currencySymbol = '';
        /* Base-currency ISO code (drives Indian vs international abbreviation) and precision. */
        var currencyIso = '';
        var currencyPrecision = null;
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
                url: VIS.Application.contextUrl + 'AgingReceivables/GetAgingReceivables',
                type: 'GET',
                success: function (res) {
                    var data = typeof res === 'string' ? JSON.parse(res) : res;
                    if (data && typeof data === 'object') {
                        renderRows(data);
                    }
                },
                error: function () { /* leave loading placeholder on error */ },
                complete: function () { showBusy(false); }
            });
        }

        /* Build amount markup with the base-currency symbol placed *before* the amount;
           the minus sign (if any) precedes the symbol (e.g. -$1.2M). The compact
           magnitude (Indian vs international numbering by base currency, kept to the
           currency precision) comes from VIS.Util.formatCompactAmount. */
        function formatMetric(value, symbol) {
            value = Number(value || 0);
            var sign = value < 0 ? '-' : '';
            var absStr = VIS.Util.formatCompactAmount(value, currencyIso, currencyPrecision);
            var symHtml = symbol ? '<span class="vas-ar-cur">' + symbol + '</span>' : '';
            return sign + symHtml + absStr;
        }

        /* ── One bucket row: label, amount, and a proportional track bar ── */
        function buildBucket(label, val, color, maxVal) {
            var width = (val / maxVal * 100);
            if (width > 100) width = 100;
            if (width < 0) width = 0;

            return '<div class="vas-ar-bucket">' +
                '<div class="vas-ar-bucket-label-wrap">' +
                '<span>' + label + '</span>' +
                '<span class="vas-ar-bucket-val">' + formatMetric(val, currencySymbol) + '</span>' +
                '</div>' +
                '<div class="vas-ar-bucket-track">' +
                '<div class="vas-ar-bucket-bar" style="width: ' + width + '%; background: ' + color + ';"></div>' +
                '</div>' +
                '</div>';
        }

        /* ── Render bucket chart ── */
        function renderRows(data) {
            if (!$contentArea) return;
            $contentArea.empty();

            if (!data || (Array.isArray(data) && data.length === 0) || Object.keys(data).length === 0) {
                $contentArea.append('<div class="vas-ar-nodata">' + lbl("VAS_060_NoData", 'No data') + '</div>');
                if ($whyBlock) { $whyBlock.empty().css('display', 'none'); }
                return;
            }

            var r = Array.isArray(data) ? data[0] : data; // Handle both arrays and single objects safely

            currencySymbol = r.symbol || '';
            currencyIso = r.isoCode || '';
            currencyPrecision = (r.stdPrecision === undefined || r.stdPrecision === null) ? null : r.stdPrecision;

            var notDue = r.notDueAmount || 0;
            var days1_30 = r.days1To30Amount || 0;
            var days31_60 = r.days31To60Amount || 0;
            var days61_90 = r.days61To90Amount || 0;
            var days90Plus = (r.days91To120Amount || 0) + (r.daysOver120Amount || 0);

            var maxVal = Math.max(notDue, days1_30, days31_60, days61_90, days90Plus);
            if (maxVal <= 0) maxVal = 1;

            var html = '';
            html += buildBucket(lbl("VAS_060_NotYetDue", 'Not yet due'), notDue, BUCKET_COLORS.notDue, maxVal);
            html += buildBucket(lbl("VAS_060_Days1_30", '1–30 days late'), days1_30, BUCKET_COLORS.days1_30, maxVal);
            html += buildBucket(lbl("VAS_060_Days31_60", '31–60 days'), days31_60, BUCKET_COLORS.days31_60, maxVal);
            html += buildBucket(lbl("VAS_060_Days61_90", '61–90 days'), days61_90, BUCKET_COLORS.days61_90, maxVal);
            html += buildBucket(lbl("VAS_060_Days90Plus", '90+ days'), days90Plus, BUCKET_COLORS.days90Plus, maxVal);

            $contentArea.append(html);

            /* WHY caption lives OUTSIDE the content area (as its sibling). */
            if ($whyBlock) {
                $whyBlock.text(lbl("VAS_060_AgingWhyText", "Older invoices are harder to collect. Focus on the 61+ buckets."));
                $whyBlock.css('display', '');
            }
        }

        /* ── Build DOM ── */
        function createWidget() {
            var $card = $(
                '<div class="vas-ar-card">'
            );

            /* ── Header ── */
            var $header = $(
                '<div class="vas-ar-header">' +
                '<div class="vas-ar-icon">' +
                '<svg width="18" height="18" viewBox="0 0 24 24" fill="none" ' +
                'stroke="currentColor" stroke-width="1.8" ' +
                'stroke-linecap="round" stroke-linejoin="round">' +
                '<circle cx="12" cy="12" r="10"/>' +
                '<polyline points="12 6 12 12 16 14"/>' +
                '</svg>' +
                '</div>' +
                '<div>' +
                '<div class="vas-ar-title">' + lbl("VAS_060_AgingReceivables", 'Aging Receivables') + '</div>' +
                '<div class="vas-ar-subtitle">' + lbl("VAS_060_WhoOwesHowOld", 'Who owes, how old') + '</div>' +
                '</div>' +
                '</div>'
            );

            /* Empty until data loads; the busy overlay covers the wait. */
            $contentArea = $('<div class="vas-ar-content-area">');

            /* WHY caption — sibling of the content area (not nested inside it),
               populated/toggled by renderRows. Hidden until data loads. */
            $whyBlock = $('<div class="vas-ar-why-block" style="display:none;">');

            $card.append($header).append($contentArea).append($whyBlock);
            $root.append($card);

            /* Busy/loading overlay over the whole card, using the core spinner classes. Hidden until
               a fetch is in flight; shown for both initial load and refresh. */
            $busy = $('<div class="vas-ar-busy"><div class="vis-busyindicatorinnerwrap"><i class="vis_widgetloader"></i></div></div>');
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

    VIS.AgingReceivablesWidget.prototype.refreshWidget = function () {
        this.refreshWidget();
    };

    VIS.AgingReceivablesWidget.prototype.init = function (windowNo, frame) {
        this.frame = frame;
        this.AD_UserHomeWidgetID = frame.widgetInfo.AD_UserHomeWidgetID;
        this.windowNo = windowNo;
        this.Initalize();
        this.frame.getContentGrid().append(this.getRoot());

        /* Self-wire the dashboard-width CSS variable the title clamp reads. */
        ensureDashInlineSizeVar(this.getRoot());
    };

    VIS.AgingReceivablesWidget.prototype.widgetSizeChange = function (height, width) { };

    VIS.AgingReceivablesWidget.prototype.dispose = function () {
        this.disposeComponent();
        if (this.frame)
            this.frame.dispose();
        this.frame = null;
    };

})(VIS, jQuery);
