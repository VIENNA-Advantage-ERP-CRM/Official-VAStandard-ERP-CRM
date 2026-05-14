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
        var $root = $('<div style="height:100%;font-family:Roboto,sans-serif;">');

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
                $contentArea.append('<div style="text-align:center;padding:16px;color:#748494;font-size:12px;">' + lbl("VIS_NoData", 'No data') + '</div>');
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

                return '<div style="margin-bottom: 10px;">' +
                    '<div style="display: flex; justify-content: space-between; font-size: 12px; margin-bottom: 4px; color: #102C3F;">' +
                    '<span>' + label + '</span>' +
                    '<span style="font-weight: 600; font-family: monospace;">' + formatCurrency(val) + '</span>' +
                    '</div>' +
                    '<div style="height: 8px; background: #EDF2F6; border-radius: 3px;">' +
                    '<div style="width: ' + width + '%; height: 100%; background: ' + color + '; border-radius: 3px; transition: width 0.8s ease-out;"></div>' +
                    '</div>' +
                    '</div>';
            }

            html += buildBucket(lbl("VIS_NotYetDue", 'Not yet due'), notDue, 'oklch(0.6 0.16 155)');
            html += buildBucket(lbl("VIS_Days1_30", '1–30 days late'), days1_30, 'oklch(0.6 0.16 70)');
            html += buildBucket(lbl("VIS_Days31_60", '31–60 days'), days31_60, 'oklch(0.6 0.16 40)');
            html += buildBucket(lbl("VIS_Days61_90", '61–90 days'), days61_90, 'oklch(0.6 0.16 25)');
            html += buildBucket(lbl("VIS_Days90Plus", '90+ days'), days90Plus, 'oklch(0.6 0.16 20)');

            // WHY block
            html += '<div style="font-size: 11px; color: #748494; margin-top: 6px; line-height: 1.45; font-style: normal;">' +
                '<span style="display: inline-flex; align-items: center; gap: 4px; background: oklch(0.96 0.03 220); padding: 1px 6px; border-radius: 100px; margin-inline-end: 6px; font-family: monospace; font-size: 9px; color: oklch(0.45 0.15 220); letter-spacing: 0.05em; font-weight: bold;">' + lbl("VIS_Why", "WHY") + '</span>' +
                lbl("VIS_AgingWhyText", "Older invoices are harder to collect. Focus on the 61+ buckets.") +
                '</div>';

            $contentArea.append(html);
        }

        /* ── Build DOM ── */
        function createWidget() {
            var $card = $(
                '<div style="' +
                'background:linear-gradient(180deg,rgba(255,255,255,0.82) 0%,rgba(255,255,255,0.58) 100%);' +
                'border:2px solid #fff;' +
                'border-radius:14px;' +
                'box-shadow:0 10px 24px rgba(15,61,97,0.06);' +
                'padding:16px 18px 18px;' +
                'height:100%;' +
                'box-sizing:border-box;' +
                'display:flex;flex-direction:column;' +
                '">'
            );

            /* ── Header ── */
            var $header = $(
                '<div style="display:flex;align-items:flex-start;gap:10px;margin-bottom:14px;">' +
                '<div style="' +
                'width:32px;height:32px;border-radius:8px;flex-shrink:0;' +
                'background:#BAEAFB;' +
                'display:flex;align-items:center;justify-content:center;' +
                'color:#0C7DB4;' +
                '">' +
                '<svg width="18" height="18" viewBox="0 0 24 24" fill="none" ' +
                'stroke="currentColor" stroke-width="1.8" ' +
                'stroke-linecap="round" stroke-linejoin="round">' +
                '<circle cx="12" cy="12" r="10"/>' +
                '<polyline points="12 6 12 12 16 14"/>' +
                '</svg>' +
                '</div>' +
                '<div>' +
                '<div style="font-size:14px;font-weight:700;color:#102C3F;line-height:1.2;margin-bottom:2px;">' + lbl("VIS_AgingReceivables", 'Aging Receivables') + '</div>' +
                '<div style="font-size:11px;color:#748494;letter-spacing:1px;text-transform:uppercase;font-family:monospace;">' + lbl("VIS_WhoOwesHowOld", 'Who owes, how old') + '</div>' +
                '</div>' +
                '</div>'
            );

            $contentArea = $('<div style="flex:1; display:flex; flex-direction:column; justify-content:center;">');
            $contentArea.append('<div style="text-align:center;color:#748494;font-size:12px;">' + lbl("VIS_Loading", 'Loading…') + '</div>');

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

    VIS.AgingReceivablesWidget.prototype.refreshWidget = function () { };

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
