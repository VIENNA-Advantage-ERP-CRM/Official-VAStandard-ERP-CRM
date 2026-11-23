/**
 * Completed Today KPI Widget (Material Transfer Dashboard)
 * Purpose - Read-only 2x1 glass KPI tile showing total count of stock transfers completed / fully received today.
 *           Renders success text color (#20A464) when count >= 1.
 * Prefix  - VAS_170_
 *
 * Labels / Message Keys Table:
 *  #  | Fallback Text                                    | Message Key
 * ----+--------------------------------------------------+-----------------------------------
 *  1  | Completed Today                                  | VAS_170_CompletedToday
 *  2  | Fully received                                   | VAS_170_FullyReceived
 *  3  | Unable to load completed today transfers        | VAS_170_UnableToLoadData
 */
; VAS = window.VAS || {};

; (function (VAS, $) {

    VAS.VAS_170_CompletedTodayWidget = function () {

        this.frame;
        this.windowNo;

        var $self = this;
        var $wrapper = $('<div class="vas-completed-today-container">');
        var $root = $('<div class="vas-completed-today-root">');
        var $valueEl;
        var $metaEl;
        var $busy;
        var widgetObserver = null;

        function lbl(key, fallback) {
            var msg = VIS.Msg.getMsg(key);
            return (!msg || msg === key || msg === '[' + key + ']') ? fallback : msg;
        }

        function showBusy(show) {
            if (!$busy || !$busy[0]) { return; }
            $busy.toggleClass('vas-completed-today-hidden', !show);
        }

        function formatCount(value) {
            return Number(value || 0).toLocaleString(window.navigator.language);
        }

        function escapeHtml(value) {
            return String(value == null ? "" : value)
                .replace(/&/g, "&amp;")
                .replace(/</g, "&lt;")
                .replace(/>/g, "&gt;")
                .replace(/"/g, "&quot;")
                .replace(/'/g, "&#039;");
        }

        function setupWidgetSizeObserver() {
            if (typeof ResizeObserver === 'undefined') { return; }
            widgetObserver = new ResizeObserver(function (entries) {
                for (var i = 0; i < entries.length; i++) {
                    var width = entries[i].contentRect.width;
                    if (width > 0) {
                        $root[0].style.setProperty('--widget-inline-size', width + 'px');
                        $root[0].style.setProperty('--widget-block-size', entries[i].contentRect.height + 'px');
                    }
                }
            });
            widgetObserver.observe($wrapper[0]);
        }


        /* DocStatus list and date filter — mirrors the backend predicate exactly
           so the drill-through list row count always equals the KPI number. */
        var COMPLETED_STATUS_LIST = "'CO', 'CL'";

        function todayIso() {
            var d = new Date();
            return d.getFullYear() + '-' +
                   String(d.getMonth() + 1).padStart(2, '0') + '-' +
                   String(d.getDate()).padStart(2, '0');
        }

        this.Initalize = function () {
            createWidget();
            loadKpi();
        };

        function loadKpi() {
            showBusy(true);

            $.ajax({
                url: VIS.Application.contextUrl + 'VAS_170_CompletedTodayWidget/GetCompletedTodayData',
                type: 'GET',
                cache: false,
                success: function (res) {
                    var data = typeof res === 'string' ? JSON.parse(res) : res;
                    if (data && typeof data === 'string') { data = JSON.parse(data); }

                    if (data && data.error) { setError(); return; }

                    renderMetric(data || {});
                },
                error: function () { setError(); },
                complete: function () { showBusy(false); }
            });
        }

// ===== NEW CODE START — currency format (agent C04, 2026-08-19) =====
        var currencyInfo = { iso: "", symbol: "" };

        // VAS163 2026-11-12: no own Cr/L/B/M tiers, toFixed or fixed grouping - server currency,
        // browser culture via the shared helper.
        function formatCurrency(val, currencyObj) {
            var cur = currencyObj || currencyInfo || {};
            return VAS.formatAmountByBrowser(val, cur.iso || '', cur.precision, { compact: true, symbol: cur.symbol || '' });
        }

        function formatCount(value) {
            var n = Number(value || 0);
            return n.toLocaleString(window.navigator.language);
        }

        function renderMetric(data) {
            var count = Number(data.count || 0);
            if (data.currency) {
                currencyInfo = data.currency;
            }

            if ($valueEl) {
                $valueEl.text(formatCount(count));
                $valueEl.attr('title', count.toLocaleString(window.navigator.language));
                $valueEl.attr('aria-live', 'polite');

                // Success tone (#20A464) when count >= 1, neutral (#102C3F) when 0
                if (count >= 1) {
                    $valueEl.addClass('vas-completed-today-success');
                } else {
                    $valueEl.removeClass('vas-completed-today-success');
                }
            }

            if ($metaEl) {
                var metaMsg = lbl("VAS_170_FullyReceived", "Fully received");
                $metaEl.text(metaMsg);
                $metaEl.attr('title', metaMsg);
            }
        }
// ===== NEW CODE END — currency format =====
// ----- OLD CODE (kept for rollback, do not delete) -----
/*
        function renderMetric(data) {
            var count = Number(data.count || 0);

            if ($valueEl) {
                $valueEl.text(formatCount(count));
                $valueEl.attr('title', formatCount(count));
                $valueEl.attr('aria-live', 'polite');

                // Success tone (#20A464) when count >= 1, neutral (#102C3F) when 0
                if (count >= 1) {
                    $valueEl.addClass('vas-completed-today-success');
                } else {
                    $valueEl.removeClass('vas-completed-today-success');
                }
            }

            if ($metaEl) {
                var metaMsg = lbl("VAS_170_FullyReceived", "Fully received");
                $metaEl.text(metaMsg);
                $metaEl.attr('title', metaMsg);
            }
        }
*/
// ----- END OLD CODE -----

        function setError() {
            if ($valueEl) {
                $valueEl.text('—');
                $valueEl.removeAttr('title');
                $valueEl.removeClass('vas-completed-today-success');
            }
            if ($metaEl) {
                var errText = lbl("VAS_170_UnableToLoadData", "Unable to load completed today transfers");
                $metaEl.text(errText);
                $metaEl.attr('title', errText);
            }
        }

        function createWidget() {
            var title = lbl("VAS_170_CompletedToday", "Completed Today");
            var metaText = lbl("VAS_170_FullyReceived", "Fully received");

            var $card = $(
                '<div class="vas-completed-today-card" aria-label="' + escapeHtml(title) + '">' +
                '<div class="vas-completed-today-label">' + escapeHtml(title) + '</div>' +
                '<div class="vas-completed-today-value">—</div>' +
                '<div class="vas-completed-today-meta">' + escapeHtml(metaText) + '</div>' +
                '</div>'
            );

            
            $valueEl = $card.find('.vas-completed-today-value');
            $metaEl = $card.find('.vas-completed-today-meta');

            $root.append($card);

            $busy = $('<div class="vas-completed-today-busy vas-completed-today-hidden"><div class="vis-busyindicatorinnerwrap"><i class="vis_widgetloader"></i></div></div>');
            $root.append($busy);

            $wrapper.append($root);
            setupWidgetSizeObserver();
        }

        this.refreshWidget = function () {
            loadKpi();
        };

        this.getRoot = function () { return $wrapper; };

        this.disposeComponent = function () {
            if (widgetObserver && $wrapper[0]) {
                widgetObserver.unobserve($wrapper[0]);
                widgetObserver = null;
            }
            $root.off();
            $root.remove();
            $wrapper.remove();
        };
    };

    VAS.VAS_170_CompletedTodayWidget.prototype.widgetFirevalueChanged = function (value) {
        if (this.listener) { this.listener.widgetFirevalueChanged(value); }
    };

    VAS.VAS_170_CompletedTodayWidget.prototype.addChangeListener = function (listener) {
        this.listener = listener;
    };

    VAS.VAS_170_CompletedTodayWidget.prototype.init = function (windowNo, frame) {
        this.frame = frame;
        this.AD_UserHomeWidgetID = frame.widgetInfo.AD_UserHomeWidgetID;
        this.windowNo = windowNo;
        this.Initalize();
        this.frame.getContentGrid().append(this.getRoot());
        ensureDashInlineSizeVar(this.getRoot());
    };

    VAS.VAS_170_CompletedTodayWidget.prototype.widgetSizeChange = function (height, width) { };

    VAS.VAS_170_CompletedTodayWidget.prototype.refreshWidget = function () {
        this.refreshWidget();
    };

    VAS.VAS_170_CompletedTodayWidget.prototype.dispose = function () {
        this.disposeComponent();
        if (this.frame) { this.frame.dispose(); }
        this.frame = null;
    };

})(VAS, jQuery);
/* VAS163 2026-11-12: VAS.formatAmountByBrowser(value, isoCode, precision, { compact, symbol })
   Amount in the BROWSER culture (window.navigator.language) with the currency the SERVER sent
   (ISO code + precision; the server symbol only as a fallback). Defined in each widget / panel
   that uses it - guarded, so the first file loaded defines it - so the shared util/CurrencyFormat.js
   stays as shipped. Compact amounts keep that file's default scale (VIS.Util.formatCompactAmount:
   L / Cr for Indian-numbering currencies, K / M / B otherwise, digits in the browser culture);
   separators, decimals and the position of the currency sign come from the browser culture. */
; (function () {
    var V = window.VAS = window.VAS || {};
    if (typeof V.formatAmountByBrowser === 'function') return;
    function browserLocale() { try { return window.navigator.language || undefined; } catch (e) { return undefined; } }
    function resolvePrecision(p) {
        var n = Number(p);
        if (p === null || p === undefined || p === '' || isNaN(n) || n < 0) {
            try { n = VIS.Env.getCtx().getStdPrecision(); } catch (e) { n = 2; }
        }
        n = Math.round(Number(n));
        return (isNaN(n) || n < 0) ? 2 : Math.min(n, 10);
    }
    function validIso(c) { c = String(c || '').trim().toUpperCase(); return /^[A-Z]{3}$/.test(c) ? c : ''; }
    function numberFormat(o) {
        try { return new Intl.NumberFormat(browserLocale(), o); } catch (e) { }
        if (o.currencyDisplay === 'narrowSymbol') {
            o.currencyDisplay = 'symbol';
            try { return new Intl.NumberFormat(browserLocale(), o); } catch (e2) { }
        }
        return null;
    }
    /* Currency sign and where the browser culture puts it for this ISO code. */
    var layoutCache = {};
    function currencyLayout(iso) {
        var key = browserLocale() + '|' + iso;
        if (layoutCache[key]) return layoutCache[key];
        var out = { sign: '', before: true, gap: '' };
        var f = iso ? numberFormat({ style: 'currency', currency: iso, currencyDisplay: 'narrowSymbol' }) : null;
        if (f && typeof f.formatToParts === 'function') {
            try {
                var parts = f.formatToParts(1234.5), ci = -1, ni = -1;
                for (var i = 0; i < parts.length; i++) {
                    if (parts[i].type === 'currency' && ci < 0) ci = i;
                    if (parts[i].type === 'integer' && ni < 0) ni = i;
                }
                if (ci >= 0) {
                    out.sign = parts[ci].value;
                    if (ni >= 0) {
                        out.before = ci < ni;
                        var near = out.before ? parts[ci + 1] : parts[ci - 1];
                        out.gap = (near && near.type === 'literal') ? near.value : '';
                    }
                }
            } catch (e) { }
        }
        layoutCache[key] = out;
        return out;
    }
    V.formatAmountByBrowser = function (value, isoCode, precision, options) {
        options = options || {};
        var num = Number(value) || 0;
        var p = resolvePrecision(precision);
        var iso = validIso(isoCode);
        var body;
        if (options.compact && window.VIS && VIS.Util && typeof VIS.Util.formatCompactAmount === 'function') {
            body = VIS.Util.formatCompactAmount(Math.abs(num), iso, p);
        } else {
            var f = numberFormat({ minimumFractionDigits: p, maximumFractionDigits: p });
            try { body = f ? f.format(Math.abs(num)) : Math.abs(num).toFixed(p); } catch (e) { body = String(Math.abs(num)); }
        }
        var minus = (num < 0 && /[1-9]/.test(body)) ? '-' : '';
        var lay = currencyLayout(iso);
        var sign = lay.sign || options.symbol || iso;
        if (!sign) return minus + body;
        if (!lay.sign) return minus + sign + ' ' + body;
        return lay.before ? (minus + sign + lay.gap + body) : (minus + body + lay.gap + sign);
    };
})();
