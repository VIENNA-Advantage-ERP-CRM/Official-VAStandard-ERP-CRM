/**
 * Cash Journal – Cash In KPI Widget
 * Purpose  - 2x1 KPI card showing today's total cash-in from completed Cash
 *            Journal sessions (C_Cash / C_CashLine, positive amounts), the
 *            receipt count, and a +/- % delta vs. the 7-day daily average.
 * Design   - Onfinity Glass Widget (success tint), docs/dashboard-widgets.md
 *            KPI/Summary spec: label · TODAY badge · xl value · delta + meta.
 *
 * ── Labels / Message Keys ──────────────────────────────────────────────
 *  #  | Text               | Message Key
 * ----+--------------------+----------------------
 *  1  | Cash in            | VAS_010_CashIn
 *  2  | Today              | VAS_010_Today
 *  3  | vs 7-day avg       | VAS_010_Vs7DayAvg
 *  4  | receipt            | VAS_010_Receipt
 *  5  | receipts           | VAS_010_Receipts
 * ──────────────────────────────────────────────────────────────────────
 */
; VAS = window.VAS || {};

; (function (VAS, $) {

    VAS.VAS_010_CashGlCashInWidget = function () {

        this.frame;
        this.windowNo;

        var $self = this;
        var $root = $('<div class="vas-cashgl-root">');
        var $valueEl;
        var $deltaEl;
        var $metaEl;
        var $busy;

        function lbl(key, fallback) {
            var t = VIS.Msg.getMsg(key);
            return (t && t.charAt(0) !== '[') ? t : fallback;
        }

        function showBusy(show) {
            if (!$busy || !$busy[0]) { return; }
            $busy[0].style.visibility = show ? 'visible' : 'hidden';
        }

        function escapeHtml(value) {
            return String(value == null ? "" : value)
                .replace(/&/g, "&amp;")
                .replace(/</g, "&lt;")
                .replace(/>/g, "&gt;")
                .replace(/"/g, "&quot;")
                .replace(/'/g, "&#039;");
        }

        this.Initalize = function () {
            createWidget();
            loadKpi();
        };

        function loadKpi() {
            showBusy(true);

            $.ajax({
                url: VIS.Application.contextUrl + 'VAS_010_CashGlCashIn/GetCashInKpi',
                type: 'GET',
                cache: false,
                success: function (res) {
                    var data = typeof res === 'string' ? JSON.parse(res) : res;
                    if (data && typeof data === 'string') { data = JSON.parse(data); }

                    if (data && data.error) {
                        setNoData();
                        return;
                    }

                    renderKpi(data);
                },
                error: function () { setNoData(); },
                complete: function () { showBusy(false); }
            });
        }

        /* Compact-amount formatter: 10M→Cr, 100K→L, 1K→K; falls back to locale
           formatting for amounts under 1,000. */
        function formatCompactAmount(value) {
            value = Number(value || 0);

            if (value >= 10000000) {
                return (value / 10000000).toFixed(2).replace(/\.00$/, "") + "Cr";
            }
            if (value >= 100000) {
                return (value / 100000).toFixed(2).replace(/\.00$/, "") + "L";
            }
            if (value >= 1000) {
                return (value / 1000).toFixed(2).replace(/\.00$/, "") + "K";
            }

            var stdPrecision = 2;
            try {
                if (VIS.Env && VIS.Env.getCtx && VIS.Env.getCtx().getStdPrecision) {
                    stdPrecision = VIS.Env.getCtx().getStdPrecision();
                }
            } catch (e) { stdPrecision = 2; }

            return value.toLocaleString(window.navigator.language, {
                minimumFractionDigits: stdPrecision,
                maximumFractionDigits: stdPrecision
            });
        }

        function renderKpi(data) {
            var todayAmount = Number((data && data.todayAmount)    || 0);
            var todayCount  = Number((data && data.todayCount)     || 0);
            var avgAmount   = Number((data && data.avgDailyAmount) || 0);
            var symbol      = (data && data.symbol) || "";

            /* Value: currency symbol + compact amount */
            var sym = symbol
                ? '<span class="vas-cashgl-cur">' + escapeHtml(symbol) + '</span>'
                : '';
            if ($valueEl) {
                $valueEl.html(sym + escapeHtml(formatCompactAmount(todayAmount)));
            }

            /* Delta: % change vs 7-day average */
            var deltaHtml = "";
            if (avgAmount > 0) {
                var pct      = Math.round(((todayAmount - avgAmount) / avgAmount) * 100);
                var isUp     = pct >= 0;
                var dirClass = isUp ? 'vas-cashgl-delta--up' : 'vas-cashgl-delta--down';
                var sign     = isUp ? '+' : '';

                var arrowSvg = isUp
                    ? '<svg viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2.5" stroke-linecap="round" stroke-linejoin="round" aria-hidden="true"><polyline points="18 15 12 9 6 15"></polyline></svg>'
                    : '<svg viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2.5" stroke-linecap="round" stroke-linejoin="round" aria-hidden="true"><polyline points="6 9 12 15 18 9"></polyline></svg>';

                deltaHtml = '<span class="vas-cashgl-delta ' + dirClass + '">'
                    + arrowSvg
                    + escapeHtml(sign + pct + '%')
                    + '</span>';
            }

            /* Meta: "vs 7-day avg · N receipts" */
            var rcptLabel = todayCount === 1
                ? lbl("VAS_010_Receipt",  "receipt")
                : lbl("VAS_010_Receipts", "receipts");
            var metaText = lbl("VAS_010_Vs7DayAvg", "vs 7-day avg")
                + ' · ' + todayCount + ' ' + rcptLabel;

            if ($deltaEl) { $deltaEl.html(deltaHtml); }
            if ($metaEl)  { $metaEl.text(metaText); }
        }

        function setNoData() {
            if ($valueEl) { $valueEl.html("&mdash;"); }
            if ($deltaEl) { $deltaEl.html(""); }
            if ($metaEl)  { $metaEl.text(""); }
        }

        function createWidget() {
            var $card = $(
                '<div class="vas-cashgl-card">' +

                '<div class="vas-cashgl-row">' +
                '<span class="vas-cashgl-label">'
                    + escapeHtml(lbl("VAS_010_CashIn", "Cash in")) +
                '</span>' +
                '<span class="vas-cashgl-date">'
                    + escapeHtml(lbl("VAS_010_Today", "Today")) +
                '</span>' +
                '</div>' +

                '<div class="vas-cashgl-value">&mdash;</div>' +

                '<div class="vas-cashgl-footer">' +
                '<span class="vas-cashgl-delta"></span>' +
                '<span class="vas-cashgl-meta"></span>' +
                '</div>' +

                '</div>'
            );

            $valueEl = $card.find('.vas-cashgl-value');
            $deltaEl = $card.find('.vas-cashgl-delta');
            $metaEl  = $card.find('.vas-cashgl-meta');

            $root.append($card);

            $busy = $('<div class="vas-cashgl-busy"><div class="vis-busyindicatorinnerwrap"><i class="vis_widgetloader"></i></div></div>');
            $busy[0].style.visibility = 'hidden';
            $root.append($busy);
        }

        this.refreshWidget = function () {
            loadKpi();
        };

        this.getRoot = function () {
            return $root;
        };

        this.disposeComponent = function () {
            $root.remove();
        };
    };

    VAS.VAS_010_CashGlCashInWidget.prototype.init = function (windowNo, frame) {
        this.frame = frame;
        this.AD_UserHomeWidgetID = frame.widgetInfo.AD_UserHomeWidgetID;
        this.windowNo = windowNo;
        this.Initalize();
        this.frame.getContentGrid().append(this.getRoot());
    };

    VAS.VAS_010_CashGlCashInWidget.prototype.widgetSizeChange = function (height, width) { };

    VAS.VAS_010_CashGlCashInWidget.prototype.refreshWidget = function () {
        this.refreshWidget();
    };

    VAS.VAS_010_CashGlCashInWidget.prototype.dispose = function () {
        this.disposeComponent();

        if (this.frame) {
            this.frame.dispose();
        }

        this.frame = null;
    };

})(VAS, jQuery);
