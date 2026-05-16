/**
 * Aging Receivables Widget
 * Purpose - Shows bucketted outstanding invoices in an aging chart format.
 */
; VIS = window.VIS || {};

; (function (VIS, $) {

    VIS.AgingReceivablesWidget = function () {

        this.frame;
        this.windowNo;
        var $self = this;
        var $root = $('<div class="vas-ar-root">');

        var $contentArea;

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
                url: VIS.Application.contextUrl + 'AgingReceivables/GetAgingReceivables',
                type: 'GET',
                success: function (res) {
                    var data = typeof res === 'string' ? JSON.parse(res) : res;
                    if (data && typeof data === 'object') {
                        renderRows(data);
                    }
                },
                error: function () { /* leave loading placeholder on error */ }
            });
        }

        /* ── Format currency ── */
        function formatCurrency(value) {
            var sign = value < 0 ? '-' : '';
            var absVal = Math.abs(value);

            if (absVal >= 1000000) {
                return sign + (absVal / 1000000).toFixed(1).replace(/\.0$/, '') + 'M';
            }
            if (absVal >= 1000) {
                return sign + Math.round(absVal / 1000) + 'k';
            }
            return sign + absVal.toLocaleString(window.navigator.language, { minimumFractionDigits: 2, maximumFractionDigits: 2 });
        }

        /* ── Render bucket chart ── */
        function renderRows(data) {
            if (!$contentArea) return;
            $contentArea.empty();

            if (!data || (Array.isArray(data) && data.length === 0) || Object.keys(data).length === 0) {
                $contentArea.append('<div class="vas-ar-nodata">' + lbl("VIS_NoData", 'No data') + '</div>');
                return;
            }

            var r = Array.isArray(data) ? data[0] : data; // Handle both arrays and single objects safely

            var notDue = r.notDueAmount || 0;
            var days1_30 = r.days1To30Amount || 0;
            var days31_60 = r.days31To60Amount || 0;
            var days61_90 = r.days61To90Amount || 0;
            var days90Plus = (r.days91To120Amount || 0) + (r.daysOver120Amount || 0);

            var maxVal = Math.max(notDue, days1_30, days31_60, days61_90, days90Plus);
            if (maxVal <= 0) maxVal = 1;

            var html = '';

            function buildBucket(label, val, color) {
                var width = (val / maxVal * 100);
                if (width > 100) width = 100;
                if (width < 0) width = 0;

                return '<div class="vas-ar-bucket">' +
                    '<div class="vas-ar-bucket-label-wrap">' +
                    '<span>' + label + '</span>' +
                    '<span class="vas-ar-bucket-val">' + formatCurrency(val) + '</span>' +
                    '</div>' +
                    '<div class="vas-ar-bucket-track">' +
                    '<div class="vas-ar-bucket-bar" style="width: ' + width + '%; background: ' + color + ';"></div>' +
                    '</div>' +
                    '</div>';
            }

            html += buildBucket(lbl("VIS_NotYetDue", 'Not yet due'), notDue, 'oklch(0.6 0.16 155)');
            html += buildBucket(lbl("VIS_Days1_30", '1–30 days late'), days1_30, 'oklch(0.6 0.16 70)');
            html += buildBucket(lbl("VIS_Days31_60", '31–60 days'), days31_60, 'oklch(0.6 0.16 40)');
            html += buildBucket(lbl("VIS_Days61_90", '61–90 days'), days61_90, 'oklch(0.6 0.16 25)');
            html += buildBucket(lbl("VIS_Days90Plus", '90+ days'), days90Plus, 'oklch(0.6 0.16 20)');

            // WHY block
            html += '<div class="vas-ar-why-block">' +
                '<span class="vas-ar-why-pill">' + lbl("VIS_Why", "WHY") + '</span>' +
                lbl("VIS_AgingWhyText", "Older invoices are harder to collect. Focus on the 61+ buckets.") +
                '</div>';

            $contentArea.append(html);
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
                '<div class="vas-ar-title">' + lbl("VIS_AgingReceivables", 'Aging Receivables') + '</div>' +
                '<div class="vas-ar-subtitle">' + lbl("VIS_WhoOwesHowOld", 'Who owes, how old') + '</div>' +
                '</div>' +
                '</div>'
            );

            $contentArea = $('<div class="vas-ar-content-area">');
            $contentArea.append('<div class="vas-ar-loading">' + lbl("VIS_Loading", 'Loading…') + '</div>');

            $card.append($header).append($contentArea);
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

    VIS.AgingReceivablesWidget.prototype.refreshWidget = function () {
        this.refreshWidget();
    };

    VIS.AgingReceivablesWidget.prototype.init = function (windowNo, frame) {
        this.frame = frame;
        this.AD_UserHomeWidgetID = frame.widgetInfo.AD_UserHomeWidgetID;
        this.windowNo = windowNo;
        this.Initalize();
        this.frame.getContentGrid().append(this.getRoot());
    };

    VIS.AgingReceivablesWidget.prototype.widgetSizeChange = function (height, width) { };

    VIS.AgingReceivablesWidget.prototype.dispose = function () {
        this.disposeComponent();
        if (this.frame)
            this.frame.dispose();
        this.frame = null;
    };

})(VIS, jQuery);
