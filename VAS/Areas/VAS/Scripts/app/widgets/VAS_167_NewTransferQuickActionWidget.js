/**
 * New Transfer Quick Action Widget
 * 1x1 quick-action tile launching the Material Transfer window on a NEW record
 * via the widget framework's value-changed channel (IsTabInNewMode).
 * ID Prefix: VAS_167_
 *
 * Labels / Message Keys Table:
 *  #  | Fallback Text                                    | Message Key
 * ----+--------------------------------------------------+-----------------------------------
 *  1  | New Transfer                                     | VAS_167_NewTransfer
 *  2  | Move stock between sites                         | VAS_167_MoveStockBetweenSites
 */
; VAS = window.VAS || {};

; (function (VAS, $) {

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

    VAS.VAS_167_NewTransferQuickActionWidget = function () {

        this.frame;
        this.windowNo;

        var $self = this;
        var widgetObserver = null;
        var $root = $('<div class="vas-new-transfer-quick-action-root">');

        function lbl(key, fallback) {
            var msg = VIS.Msg.getMsg(key);
            return (!msg || msg === key || msg === '[' + key + ']') ? fallback : msg;
        }

        function escapeHtml(value) {
            return String(value == null ? '' : value)
                .replace(/&/g, '&amp;')
                .replace(/</g, '&lt;')
                .replace(/>/g, '&gt;')
                .replace(/"/g, '&quot;')
                .replace(/'/g, '&#039;');
        }

// ===== NEW CODE START — currency format (agent C01, 2026-08-19) =====
        /**
         * Formats a monetary value according to the organization's currency ISO code and symbol.
         * @param {number|string} val - Raw monetary amount
         * @param {Object} currencyInfo - Currency meta object { iso: string, symbol: string }
         * @returns {string} Formatted string
         */
        // VAS163 2026-11-12: no own Indian/international tiers, toFixed or fixed grouping -
        // currency (iso/precision/symbol) from the server, culture from the browser (shared helper).
        function formatCurrency(val, currencyInfo) {
            var c = currencyInfo || {};
            return VAS.formatAmountByBrowser(val, c.iso || '', c.precision, { compact: true, symbol: c.symbol || '' });
        }
// ===== NEW CODE END — currency format =====

// ----- OLD CODE (kept for rollback, do not delete) -----
// (No prior currency formatting function existed in this quick action tile)
// ----- END OLD CODE -----

        // Open the widget's configured window directly on a NEW record
        // through the widget framework's value-changed channel.
        // VAS163 2026-11-12: resolved Inventory Move window id, kept for the next click (rules A.3).
        var moveWindowId = 0;

        function openNewTransfer() {
            // VAS163 2026-11-12: hosted inside the Material Transfer window -> keep the
            // existing new-record channel; otherwise open the Inventory Move window via
            // VAS.ZoomUtil (Record_ID 0 = no record; silent no-op when unresolved).
            if ($self.listener) {
                var windowParam = {
                    "IsTabInNewMode": "true",
                    "TabIndex": "0"
                };
                $self.widgetFirevalueChanged(windowParam);
                return;
            }
            if (VAS.ZoomUtil && VAS.ZoomUtil.zoomToRecord) {
                VAS.ZoomUtil.zoomToRecord('M_Movement_ID', 0, moveWindowId, 'VAS_InventoryMove', 'Inventory Move')
                    .then(function (id) { if (id > 0) { moveWindowId = id; } });
            }
        }

        function createWidget() {
            var title = lbl('VAS_167_NewTransfer', 'New Transfer');
            var subtitle = lbl('VAS_167_MoveStockBetweenSites', 'Move stock between sites');

            var $card = $(
                '<button type="button" class="vas-new-transfer-quick-action-card" aria-label="' + escapeHtml(title) + '">' +
                    '<span class="vas-new-transfer-quick-action-iconrow">' +
                        '<span class="vas-new-transfer-quick-action-ico">' +
                            '<svg viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2.2" stroke-linecap="round" stroke-linejoin="round"><line x1="12" y1="5" x2="12" y2="19"/><line x1="5" y1="12" x2="19" y2="12"/></svg>' +
                        '</span>' +
                    '</span>' +
                    '<span class="vas-new-transfer-quick-action-text">' +
                        '<span class="vas-new-transfer-quick-action-title">' + escapeHtml(title) + '</span>' +
                        '<span class="vas-new-transfer-quick-action-sub">' + escapeHtml(subtitle) + '</span>' +
                    '</span>' +
                '</button>'
            );

            $card.on('click', function () { openNewTransfer(); });
            $root.append($card);

            /* Publish the widget size so the card can scale down when the cell is small (browser zoom). */
            if (typeof ResizeObserver !== 'undefined' && $root[0]) {
                widgetObserver = new ResizeObserver(function (entries) {
                    var r = entries[0].contentRect;
                    if (r.width > 0) { $root[0].style.setProperty('--widget-inline-size', r.width + 'px'); }
                    if (r.height > 0) { $root[0].style.setProperty('--widget-block-size', r.height + 'px'); }
                });
                widgetObserver.observe($root[0]);
            }
        }

        this.Initalize = function () {
            createWidget();
        };

        this.refreshWidget = function () { };

        this.getRoot = function () { return $root; };

        this.disposeComponent = function () {
            if (widgetObserver) { widgetObserver.disconnect(); widgetObserver = null; }
            $root.off();
            $root.remove();
        };
    };

    VAS.VAS_167_NewTransferQuickActionWidget.prototype.widgetFirevalueChanged = function (value) {
        if (this.listener) { this.listener.widgetFirevalueChanged(value); }
    };

    VAS.VAS_167_NewTransferQuickActionWidget.prototype.addChangeListener = function (listener) {
        this.listener = listener;
    };

    VAS.VAS_167_NewTransferQuickActionWidget.prototype.init = function (windowNo, frame) {
        this.frame = frame;
        this.AD_UserHomeWidgetID = frame.widgetInfo.AD_UserHomeWidgetID;
        this.windowNo = windowNo;
        this.Initalize();
        this.frame.getContentGrid().append(this.getRoot());
        ensureDashInlineSizeVar(this.getRoot());
    };

    VAS.VAS_167_NewTransferQuickActionWidget.prototype.widgetSizeChange = function (height, width) { };

    VAS.VAS_167_NewTransferQuickActionWidget.prototype.refreshWidget = function () {
        this.refreshWidget();
    };

    VAS.VAS_167_NewTransferQuickActionWidget.prototype.dispose = function () {
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
