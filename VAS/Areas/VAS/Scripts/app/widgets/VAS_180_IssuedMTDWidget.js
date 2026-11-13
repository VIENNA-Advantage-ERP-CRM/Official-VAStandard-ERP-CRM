/**
 * VAS_180_IssuedMTDWidget
 * 2x1 KPI tile for Inventory Use dashboard.
 * Displays aggregate count of material issue lines posted Month-to-Date (MTD).
 *
 * Summary Message Table
 *  # | Current Text                    | Message Key
 * ---+---------------------------------+-----------------------------------
 *  1 | Issued MTD                      | VAS_180_IssuedMTD
 *  2 | Issue lines this month          | VAS_180_IssueLinesThisMonth
 *  3 | No issue lines MTD              | VAS_180_NoIssueLinesMTD
 *  4 | Couldn't load                   | VAS_180_CouldntLoad
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

    VAS.VAS_180_IssuedMTDWidget = function () {

        this.frame;
        this.windowNo;

        var $self = this;
        var $root = $('<div class="vas-imtd-root">');
        var $card;
        var $valueEl;
        var $metaEl;
        var $busy;
        var lastCount = 0;
        var inventoryUseWindowId = 0; // VAS163 2026-11-12: cached by VAS.ZoomUtil
        // DB-ready date literals for the current month window (Oracle TO_DATE(...) or
        // CAST(...AS DATE) elsewhere, per the controller's ToSqlDate) - populated from
        // the KPI response and reused verbatim by openIssuedMTDList's TabWhereClause,
        // instead of that click handler reconstructing the same month window with
        // Oracle-only SYSDATE/TRUNC/ADD_MONTHS syntax (broke the drill-through on
        // this install's actual Postgres backend).
        var monthStartSql = null;
        var nextMonthStartSql = null;

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

// ===== NEW CODE START — currency format (agent A02, 2026-08-19) =====
        var currencyInfo = { iso: '', symbol: '' };

        function formatCount(value) {
            var n = Number(value || 0);
            return n.toLocaleString(window.navigator.language);
        }

        function formatCurrency(amount, currInfo) {
            var val = Number(amount || 0);
            var info = currInfo || currencyInfo || {};
            // VAS163 2026-11-12: server currency + browser culture via VAS.formatAmountByBrowser
            // (was "symbol + ' ' + compact number" with a fixed 2-dp precision).
            return VAS.formatAmountByBrowser(val, info.iso || '', info.stdPrecision, { compact: true, symbol: info.symbol || '' });
        }

        function renderMetric(data) {
            lastCount = Number(data.count || 0);
            if (data.currency) {
                currencyInfo = data.currency;
            }

            if ($valueEl) {
                var formattedVal = formatCount(lastCount);
                $valueEl.text(formattedVal);
                $valueEl.attr('title', formatCount(lastCount)); // VAS163 2026-11-12: locale count
            }
            if ($metaEl) {
                $metaEl.text(lastCount === 0
                    ? label("VAS_180_NoIssueLinesMTD", "No issue lines MTD")
                    : label("VAS_180_IssueLinesThisMonth", "Issue lines this month"));
            }
            if ($card) { $card.prop('disabled', false); }
        }

        function setError() {
            lastCount = 0;
            if ($valueEl) {
                $valueEl.text('—');
                $valueEl.removeAttr('title');
            }
            if ($metaEl) { $metaEl.text(label("VAS_180_CouldntLoad", "Couldn't load")); }
            if ($card) { $card.prop('disabled', true); }
        }

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

        function openIssuedMTDList() {
            // Keep in lock-step with GetIssuedMTDCountData in the controller. This drills through
            // at DOCUMENT level, so it can carry the IsInternalUse filter but not the line-level
            // QtyInternalUse one - it lands on the issue documents behind the counted lines.
            // VAS163 2026-11-12: Home page (windowNo < 0) has no host to relay the filter to, so
            // the click did nothing - open the Inventory Use window via VAS.ZoomUtil instead.
            if ($self.windowNo < 0) {
                if (window.VAS && VAS.ZoomUtil) {
                    VAS.ZoomUtil.zoomToRecord('M_Inventory_ID', 0, inventoryUseWindowId, 'VAS_InternalUseInventory', 'Internal Use Inventory')
                        .done(function (id) { if (id > 0) { inventoryUseWindowId = id; } });
                }
                return;
            }
            if (!monthStartSql || !nextMonthStartSql) { return; }

            // VAS163 2026-11-13: row 29 - same predicates as the (now document) count, including the
            // issue-line EXISTS, so the filtered window shows exactly the documents the tile counts.
            var where = "M_Inventory.IsActive = 'Y' AND M_Inventory.DocStatus IN ('CO', 'CL') AND COALESCE(M_Inventory.IsInternalUse, 'N') = 'Y' AND M_Inventory.MovementDate >= " + monthStartSql + " AND M_Inventory.MovementDate < " + nextMonthStartSql +
                " AND EXISTS (SELECT 1 FROM M_InventoryLine l WHERE l.M_Inventory_ID = M_Inventory.M_Inventory_ID AND l.IsActive = 'Y' AND COALESCE(l.QtyInternalUse, 0) > 0)";
            var windowParam = {
                "TabWhereClause": where,
                "TabLayout": "N",
                "TabIndex": "0",
                "ActionName": "VAS_InternalUseInventory",
                "ActionType": "W"
            };
            $self.widgetFirevalueChanged(windowParam);
        }
// ===== NEW CODE END — currency format =====
// ----- OLD CODE (kept for rollback, do not delete) -----
//        function formatCount(value) {
//            var n = Number(value || 0);
//            return n.toLocaleString(window.navigator.language);
//        }
//
//        function renderMetric(data) {
//            lastCount = Number(data.count || 0);
//
//            if ($valueEl) {
//                $valueEl.text(formatCount(lastCount));
//                $valueEl.attr('title', formatCount(lastCount));
//            }
//            if ($metaEl) {
//                $metaEl.text(lastCount === 0
//                    ? label("VAS_NoIssueLinesMTD", "No issue lines MTD")
//                    : label("VAS_IssueLinesThisMonth", "Issue lines this month"));
//            }
//            if ($card) { $card.prop('disabled', false); }
//        }
//
//        function openIssuedMTDList() {
//            var where = "M_Inventory.IsActive = 'Y' AND M_Inventory.DocStatus IN ('CO', 'CL') AND M_Inventory.MovementDate >= TRUNC(SYSDATE, 'MM') AND M_Inventory.MovementDate < ADD_MONTHS(TRUNC(SYSDATE, 'MM'), 1)";
//            var windowParam = {
//                "TabWhereClause": where,
//                "TabLayout": "N",
//                "TabIndex": "0"
//            };
//            $self.widgetFirevalueChanged(windowParam);
//        }
// ----- END OLD CODE -----

        function showBusy(show) {
            if (!$busy || !$busy[0]) { return; }
            $busy.toggleClass('vas-imtd-hidden', !show);
        }

        function loadKpi() {
            showBusy(true);

            $.ajax({
                url: VIS.Application.contextUrl + 'VAS_180_IssuedMTDWidget/GetIssuedMTDCount',
                type: 'GET',
                cache: false,
                success: function (res) {
                    var data = parseResponse(res);
                    if (data.error) { setError(); return; }
                    monthStartSql = data.monthStartSql || null;
                    nextMonthStartSql = data.nextMonthStartSql || null;
                    renderMetric(data);
                },
                error: function () { setError(); },
                complete: function () { showBusy(false); }
            });
        }

        function createWidget() {
            var title = label("VAS_180_IssuedMTD", "Issued MTD");
            $card = $(
                '<button type="button" class="vas-imtd-card vas-widget-bg" aria-label="' + escapeHtml(title) + '">' +
                '<div class="vas-imtd-label">' + escapeHtml(title) + '</div>' +
                '<div class="vas-imtd-value">—</div>' +
                '<div class="vas-imtd-meta"></div>' +
                '</button>'
            );

            $valueEl = $card.find('.vas-imtd-value');
            $metaEl = $card.find('.vas-imtd-meta');

            $card.on('click', function () { openIssuedMTDList(); });
            $root.append($card);

            $busy = $('<div class="vas-imtd-busy vas-imtd-hidden"><div class="vis-busyindicatorinnerwrap"><i class="vis_widgetloader"></i></div></div>');
            $root.append($busy);
        }

        this.Initalize = function () {
            createWidget();
            setupResizeObserver();
            loadKpi();
        };

        this.refreshWidget = function () {
            loadKpi();
        };

        this.getRoot = function () { return $root; };

        this.disposeComponent = function () {
            if ($card) { $card.off('click'); }
            $root.remove();
        };
    };

    VAS.VAS_180_IssuedMTDWidget.prototype.widgetFirevalueChanged = function (value) {
        if (this.listener) { this.listener.widgetFirevalueChanged(value); }
    };

    VAS.VAS_180_IssuedMTDWidget.prototype.addChangeListener = function (listener) {
        this.listener = listener;
    };

    VAS.VAS_180_IssuedMTDWidget.prototype.init = function (windowNo, frame) {
        this.frame = frame;
        this.AD_UserHomeWidgetID = frame.widgetInfo.AD_UserHomeWidgetID;
        this.windowNo = windowNo;
        this.Initalize();
        this.frame.getContentGrid().append(this.getRoot());
        ensureDashInlineSizeVar(this.getRoot());
    };

    VAS.VAS_180_IssuedMTDWidget.prototype.widgetSizeChange = function (height, width) { };

    VAS.VAS_180_IssuedMTDWidget.prototype.refreshWidget = function () {
        this.refreshWidget();
    };

    VAS.VAS_180_IssuedMTDWidget.prototype.dispose = function () {
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
