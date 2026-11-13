/**
 * VAS_182_SparesConsumablesIssuesWidget
 * 2x1 KPI tile for Inventory Use dashboard.
 * Displays percentage share of material issue value for Spares / Consumables Month-to-Date (MTD).
 *
 * Summary Message Table
 *  # | Current Text                    | Message Key
 * ---+---------------------------------+-----------------------------------
 *  1 | Spares / Consumables            | VAS_182_SparesConsumables
 *  2 | Of issued value MTD             | VAS_182_OfIssuedValueMTD
 *  3 | Couldn't load                   | VAS_182_CouldntLoad
 *
 * NOTE (2026-09-18, Claude): setupResizeObserver()/--widget-inline-size removed from
 * Initalize() to match VAS_180_IssuedMTDWidget's label/value/meta size. VAS_180 never
 * scopes --widget-inline-size to its own card, so its font-size clamp() falls through
 * to the dashboard-wide --dash-inline-size and lands near the clamp's midpoint
 * (~18.4px); this widget's own --widget-inline-size was scoped to its ~300px card,
 * which is small enough that the clamp always bottomed out at its 16px floor instead.
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

// ===== NEW CODE START — currency format (agent A04, 2026-08-19) =====
    /**
     * Formats currency values according to organization locale/currency settings (Section 1).
     * @param {number|string} val Numeric value to format
     * @param {string} iso Currency ISO code from the server (C_Currency.ISO_Code)
     * @param {string} symbol Currency symbol from the server (C_Currency.CurSymbol)
     * @returns {string} Formatted currency string
     */
    function formatCurrencyAmount(val, iso, symbol) {
        var num = parseFloat(val);
        if (isNaN(num)) { num = 0; }
        // VAS163 2026-11-12: server currency + browser culture via VAS.formatAmountByBrowser
        // (was "symbol + ' ' + compact number" with a fixed 2-dp precision).
        return VAS.formatAmountByBrowser(num, iso || '', undefined, { compact: true, symbol: symbol || '' });
    }

    // VAS163 2026-11-12: percent rendered with the browser-locale decimal separator.
    function formatPct(pct) {
        return Number(pct || 0).toLocaleString(window.navigator.language, { maximumFractionDigits: 2 }) + '%';
    }
// ===== NEW CODE END — currency format =====

    VAS.VAS_182_SparesConsumablesIssuesWidget = function () {

        this.frame;
        this.windowNo;

        var $self = this;
        var $root = $('<div class="vas-sci-root">');
        var $card;
        var $valueEl;
        var $metaEl;
        var $busy;

// ===== NEW CODE START — currency format (agent A04, 2026-08-19) =====
        var currencyIso = '';
        var currencySymbol = '';
        // Work-order columns this installation actually has, reported by the KPI endpoint
        // (they are manufacturing-module only and are absent on some databases).
        var workOrderColumns = [];
// ===== NEW CODE END — currency format =====

        function label(key, fallback) {
            return VIS.Msg.getMsg(key);
        }

        function escapeHtml(value) {
            return String(value == null ? "" : value)
                .replace(/&/g, "&amp;")
                .replace(/</g, "&lt;")
                .replace(/>/g, "&gt;")
                .replace(/"/g, "&quot;")
                .replace(/'/g, "&#039;");
        }

        function parseResponse(res) {
            var data = res;
            if (typeof data === 'string') { data = JSON.parse(data); }
            if (typeof data === 'string') { data = JSON.parse(data); }
            return data || {};
        }

        function showBusy(show) {
            if (!$busy || !$busy[0]) { return; }
            $busy.toggleClass('vas-sci-hidden', !show);
        }

        this.Initalize = function () {
            createWidget();
            loadKpi();
        };

        function setupResizeObserver() {
            if (typeof ResizeObserver === 'undefined') { return; }
            try {
                var ro = new ResizeObserver(function (entries) {
                    for (var i = 0; i < entries.length; i++) {
                        var width = entries[i].contentRect.width;
                        if (width > 0 && $root[0]) {
                            $root[0].style.setProperty('--widget-inline-size', width + 'px');
                        }
                    }
                });
                ro.observe($root[0]);
            } catch (e) { }
        }

// ===== NEW CODE START — currency format (agent A04, 2026-08-19) =====
        function loadCurrencyInfo() {
            $.ajax({
                url: VIS.Application.contextUrl + 'VAS_182_SparesConsumablesIssuesWidget/GetCurrencyInfo',
                type: 'GET',
                cache: false,
                success: function (res) {
                    var data = parseResponse(res);
                    if (data) {
                        currencyIso = data.iso || '';
                        currencySymbol = data.symbol || '';
                    }
                }
            });
        }

        function loadKpi() {
            showBusy(true);
            loadCurrencyInfo();

            $.ajax({
                url: VIS.Application.contextUrl + 'VAS_182_SparesConsumablesIssuesWidget/GetSparesConsumablesPercentage',
                type: 'GET',
                cache: false,
                success: function (res) {
                    var data = parseResponse(res);
                    if (data.error) { setError(); return; }
                    if (data.workOrderColumns) { workOrderColumns = data.workOrderColumns; }
                    renderMetric(data);
                },
                error: function () { setError(); },
                complete: function () { showBusy(false); }
            });
        }

        function renderMetric(data) {
            var pct = Number(data.percentage || 0);

            if ($valueEl) {
                $valueEl.text(formatPct(pct)); // VAS163 2026-11-12: locale percent
                $valueEl.attr('title', formatPct(pct));
            }
            if ($metaEl) {
                $metaEl.text(label("VAS_182_OfIssuedValueMTD", "Of issued value MTD"));
            }
            if ($card) { $card.prop('disabled', false); }
        }
// ===== NEW CODE END — currency format =====

// ----- OLD CODE (kept for rollback, do not delete) -----
        function loadKpi_Old() {
            showBusy(true);

            $.ajax({
                url: VIS.Application.contextUrl + 'VAS_182_SparesConsumablesIssuesWidget/GetSparesConsumablesPercentage',
                type: 'GET',
                cache: false,
                success: function (res) {
                    var data = parseResponse(res);
                    if (data.error) { setError(); return; }
                    renderMetric(data);
                },
                error: function () { setError(); },
                complete: function () { showBusy(false); }
            });
        }

        function renderMetric_Old(data) {
            var pct = Number(data.percentage || 0);

            if ($valueEl) {
                $valueEl.text(formatPct(pct)); // VAS163 2026-11-12: locale percent
                $valueEl.attr('title', formatPct(pct));
            }
            if ($metaEl) {
                $metaEl.text(label("VAS_182_OfIssuedValueMTD", "Of issued value MTD"));
            }
            if ($card) { $card.prop('disabled', false); }
        }
// ----- END OLD CODE -----

        function setError() {
            if ($valueEl) {
                $valueEl.text('—');
                $valueEl.removeAttr('title');
            }
            if ($metaEl) { $metaEl.text(label("VAS_182_CouldntLoad", "Couldn't load")); }
            if ($card) { $card.prop('disabled', true); }
        }

        // The framework navigates IN-PLACE (no new window, no half-drawn screen) only when the
        // payload's ActionName equals the name of the window currently HOSTING this widget;
        // otherwise VIS.dynamicWidget resolves ActionName through UserPreference/GetWindowID and
        // opens a second window. Resolve the host window name from the listener chain.
        // Established pattern - see VAS_091_MaterialReceiptSearchWidget.js zoomTo() (also 067/069/070/128).
        function hostWindowName() {
            try {
                var l = $self.listener;
                for (var i = 0; i < 6 && l; i++) {
                    if (l.apanel && l.apanel.gridWindow && l.apanel.gridWindow.getName) {
                        return l.apanel.gridWindow.getName();
                    }
                    if (l.gridWindow && l.gridWindow.getName) {
                        return l.gridWindow.getName();
                    }
                    l = l.listener;
                }
            } catch (e) { }
            return '';
        }

        // Drill to the EXACT documents behind the KPI: completed internal-use documents for the
        // current month that carry at least one spares / consumables line. The EXISTS predicate
        // mirrors the line-level classification in GetSparesConsumablesPercentageData() one-for-one,
        // so the list can never drift from the percentage on the tile.
        // Portability: the work-order columns (VA075_WorkOrder_ID / VAMFG_M_WorkOrder_ID) are
        // manufacturing-module only and are absent on DB 1, so the controller reports which ones
        // this installation actually has. With none of them every issue line is spares/consumables
        // (the production KPI is a hard 0%), so the drill carries no work-order clause at all -
        // an unresolved column would make the grid query throw instead of opening.
        // Home-page fallback for openSparesConsumablesList(): there is no host window to relay
        // a multi-record TabWhereClause to off-window, and VAS.ZoomUtil only supports zooming
        // to a SINGLE record (see VAS_184/186/187's zoomToInventoryRecord). Landing on the
        // newest matching document (ids[0], per GetSparesConsumablesIdsData's own ordering) is
        // the closest available stand-in for "see the list" without inventing a new zoom
        // capability. Resolves the Inventory Use window id once via VAS_178's existing
        // GetMaterialIssueWindowId endpoint (shared with VAS_181/184/186/187), then caches it.
        var inventoryUseWindowId = 0;

        function zoomToFirstSparesConsumable(ids) {
            var firstId = Number((ids && ids[0]) || 0);
            if (!firstId || !window.VAS || !VAS.ZoomUtil) { return; }

            // VAS163 2026-11-12: private GetMaterialIssueWindowId lookup (ORDER BY AD_Window_ID
            // could pick a legacy window) replaced by VAS.ZoomUtil name resolution; id cached.
            VAS.ZoomUtil.zoomToRecord('M_Inventory_ID', firstId, inventoryUseWindowId, 'VAS_InternalUseInventory', 'Internal Use Inventory')
                .done(function (id) { if (id > 0) { inventoryUseWindowId = id; } });
        }

        function openSparesConsumablesList() {
            // Keep in lock-step with GetSparesConsumablesIdsData in the controller.
            // The TabWhereClause is a flat M_Inventory_ID IN (...) list, NOT a
            // correlated EXISTS(SELECT 1 FROM M_InventoryLine ...) subquery - the
            // host window's own "duplicate DocumentNo" grid diagnostic does naive,
            // parenthesis-unaware text surgery on the TabWhereClause looking for a
            // FROM to lift out, and it mishandled the nested EXISTS(...) (confirmed
            // via the app log: it produced malformed SQL and Oracle rejected it with
            // ORA-00933, which is what was actually hanging this drill-through, the
            // same bug VAS_181 hit and fixed the same way). A flat ID list has no
            // FROM/subquery in it at all, so there is nothing for that diagnostic
            // query to mishandle.
            $.ajax({
                url: VIS.Application.contextUrl + 'VAS_182_SparesConsumablesIssuesWidget/GetSparesConsumablesIds',
                type: 'GET',
                cache: false,
                success: function (res) {
                    var data = parseResponse(res);
                    if (data.error) { return; }
                    var ids = data.ids || [];

                    if ($self.windowNo >= 0) {
                        var idList = ids.length ? ids.join(',') : '-1';
                        var where = "M_Inventory.M_Inventory_ID IN (" + idList + ")";
                        $self.widgetFirevalueChanged({
                            "TabWhereClause": where,
                            "TabLayout": "N",
                            "TabIndex": "0",
                            "ActionName": hostWindowName() || "VAS_InternalUseInventory",
                            "ActionType": "W"
                        });
                        return;
                    }

                    zoomToFirstSparesConsumable(ids);
                }
            });
        }

        function createWidget() {
            var title = label("VAS_182_SparesConsumables", "Spares / Consumables");
            $card = $(
                '<button type="button" class="vas-sci-card vas-widget-bg" aria-label="' + escapeHtml(title) + '">' +
                '<div class="vas-sci-label">' + escapeHtml(title) + '</div>' +
                '<div class="vas-sci-value">—</div>' +
                '<div class="vas-sci-meta"></div>' +
                '</button>'
            );

            $valueEl = $card.find('.vas-sci-value');
            $metaEl = $card.find('.vas-sci-meta');

            $card.on('click', function () { openSparesConsumablesList(); });
            $root.append($card);

            $busy = $('<div class="vas-sci-busy vas-sci-hidden"><div class="vis-busyindicatorinnerwrap"><i class="vis_widgetloader"></i></div></div>');
            $root.append($busy);
        }

        this.refreshWidget = function () {
            loadKpi();
        };

        this.getRoot = function () { return $root; };

        this.disposeComponent = function () {
            if ($card) { $card.off('click'); }
            $root.remove();
        };
    };

    VAS.VAS_182_SparesConsumablesIssuesWidget.prototype.widgetFirevalueChanged = function (value) {
        if (this.listener) { this.listener.widgetFirevalueChanged(value); }
    };

    VAS.VAS_182_SparesConsumablesIssuesWidget.prototype.addChangeListener = function (listener) {
        this.listener = listener;
    };

    VAS.VAS_182_SparesConsumablesIssuesWidget.prototype.init = function (windowNo, frame) {
        this.frame = frame;
        this.AD_UserHomeWidgetID = frame.widgetInfo.AD_UserHomeWidgetID;
        this.windowNo = windowNo;
        this.Initalize();
        this.frame.getContentGrid().append(this.getRoot());
        ensureDashInlineSizeVar(this.getRoot());
    };

    VAS.VAS_182_SparesConsumablesIssuesWidget.prototype.widgetSizeChange = function (height, width) { };

    VAS.VAS_182_SparesConsumablesIssuesWidget.prototype.refreshWidget = function () {
        this.refreshWidget();
    };

    VAS.VAS_182_SparesConsumablesIssuesWidget.prototype.dispose = function () {
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
