/**
 * VAS_243 Active Contracts KPI Widget (Service Contracts module dashboard)
 * Purpose - 2x1 read-only KPI tile: the COUNT of "live" contracts in the
 *           accessible portfolio (completed, not cancelled, not yet ended -
 *           MRole governs visibility) with the combined base-currency
 *           portfolio value as the sub-line and an informational "live" tag.
 *           A positive, at-a-glance health read-out for the contracts
 *           manager - NOT clickable (no drill, no hover-lift, no arrow),
 *           unlike the Expiring / Renewal / Unbilled tiles on the same
 *           dashboard.
 * Design  - 2026-09-08: rebuilt directly against the attached
 *           kpi-active-contracts.html mock's own literal CSS/DOM shape - an
 *           earlier pass leaned on Design Specs/dashboard-widgets.md's generic
 *           "KPI And Summary Widget" section instead and, confirmed by
 *           screenshot comparison, no longer matched the mock (title-style
 *           label instead of a small muted caption, a tight top-anchored
 *           3-line stack instead of label-far-above-a-close-set value+foot
 *           block, and the "live" tag folded into the sub-line instead of its
 *           own element). Matching the mock's real output wins here. DOM
 *           shape: label, then a plain wrapper div holding the value and a
 *           foot row (sub-line + the separate "live" tag) - the wrapper keeps
 *           value+foot close together while the card's own space-between
 *           pushes the label to the opposite edge. See
 *           VAS_243_ActiveContractsWidget.css for the exact token values.
 *
 * Backend - VAS_243_ActiveContractsWidget/GetActiveContracts (GET -> LiveCount, LiveValueBase, currency)
 *
 * Routing - Not clickable; this tile has no zoom/drill target of its own (the
 *           live count only needs to reconcile with the Service Contract
 *           window's own live filter, per the build spec).
 *
 * ── Labels / Message Keys ─────────────────────────────────────────────────
 *  # | Current Text        | Message Key
 * ---+---------------------+--------------------------------
 *  1 | Active Contracts    | VAS_243_Title
 *  2 | portfolio value     | VAS_243_PortfolioValue
 *  3 | live                | VAS_243_Live
 *  4 | Couldn't load       | VAS_243_LoadError
 * ──────────────────────────────────────────────────────────────────────────
 */
; VAS = window.VAS || {};

; (function (VAS, $) {

    // Populates --dash-inline-size (a shared, dashboard-wide CSS var read by
    // .vas243-root's own clamp() font-size formulas) from the actual dashboard
    // grid container's width via a page-wide singleton ResizeObserver -
    // without this, clamp() falls back to 100vw and pegs near its max on any
    // normal desktop window, rendering everything larger than the mock. Same
    // helper ~180 other production widgets in this codebase already use (see
    // VAS_126_OpenTicketsWidget's own copy).
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

    VAS.VAS_243_ActiveContractsWidget = function () {

        this.frame;
        this.windowNo;

        var $self = this;
        var $root = $('<div class="vas243-root">');
        var $card;

        function label(key, fallback) {
            var t = VIS.Msg.getMsg(key);
            return t && t.charAt(0) !== '[' ? t : fallback;
        }

        function escapeHtml(value) {
            if (value == null) { return ''; }
            return String(value).replace(/[&<>"']/g, function (c) {
                return { '&': '&amp;', '<': '&lt;', '>': '&gt;', '"': '&quot;', "'": '&#39;' }[c];
            });
        }

        function parseResponse(response) {
            var parsed = response;
            if (typeof parsed === 'string' && parsed.length) { parsed = JSON.parse(parsed); }
            if (typeof parsed === 'string' && parsed.length) { parsed = JSON.parse(parsed); }
            return parsed || {};
        }

        function formatCount(value) {
            var n = Number(value || 0);
            if (!isFinite(n)) { n = 0; }
            return Math.round(n).toLocaleString(window.navigator.language);
        }

        // Compact K/M/B magnitude, 2 decimals - matches the mock's "$20.85M".
        function fmtCompact(value, precision) {
            var n = Number(value || 0); if (!isFinite(n)) { n = 0; }
            var p = Number(precision); if (isNaN(p) || p < 0) { p = 2; }
            var abs = Math.abs(n);
            var sign = n < 0 ? '-' : '';
            if (abs >= 1e9) { return sign + (abs / 1e9).toFixed(2) + 'B'; }
            if (abs >= 1e6) { return sign + (abs / 1e6).toFixed(2) + 'M'; }
            if (abs >= 1e3) { return sign + (abs / 1e3).toFixed(2) + 'K'; }
            return sign + abs.toFixed(p);
        }

        function formatMoney(value, iso, symbol, precision) {
            var tag = symbol || iso || '';
            if (window.VIS && VIS.Util && typeof VIS.Util.formatCompactAmount === 'function') {
                var n = Number(value || 0); if (!isFinite(n)) { n = 0; }
                var sign = n < 0 ? '-' : '';
                return sign + tag + VIS.Util.formatCompactAmount(Math.abs(n), iso || '', precision);
            }
            return tag + fmtCompact(value, precision);
        }

        function createWidget() {
            $card = $(
                '<div class="vas243-card">' +
                    '<div class="vas243-label">' + escapeHtml(label('VAS_243_Title', 'Active Contracts')) + '</div>' +
                    '<div class="vas243-valwrap">' +
                        '<div class="vas243-value vas243-skel-value">&nbsp;</div>' +
                        '<div class="vas243-foot">' +
                            '<span class="vas243-sub vas243-skel-sub">&nbsp;</span>' +
                            '<span class="vas243-tag"></span>' +
                        '</div>' +
                    '</div>' +
                '</div>'
            );
            $root.append($card);
        }

        function renderLoading() {
            $card.find('.vas243-value').addClass('vas243-skel-value').text(' ');
            $card.find('.vas243-sub').addClass('vas243-skel-sub').text(' ');
            $card.find('.vas243-tag').text('');
            $card.removeClass('is-error');
        }

        function renderError() {
            $card.find('.vas243-value').removeClass('vas243-skel-value').text('—');
            $card.find('.vas243-sub').removeClass('vas243-skel-sub').text(label('VAS_243_LoadError', "Couldn't load"));
            $card.find('.vas243-tag').text('');
            $card.addClass('is-error');
        }

        function renderData(data) {
            var count = Number(data.LiveCount || 0);
            var money = formatMoney(data.LiveValueBase, data.CurrencyIso, data.CurrencySymbol, data.CurrencyPrecision);

            $card.removeClass('is-error');
            $card.find('.vas243-value').removeClass('vas243-skel-value').text(formatCount(count));
            $card.find('.vas243-sub').removeClass('vas243-skel-sub').text(money + ' ' + label('VAS_243_PortfolioValue', 'portfolio value'));
            $card.find('.vas243-tag').text(label('VAS_243_Live', 'live'));
        }

        function load() {
            renderLoading();
            $.ajax({
                url: VIS.Application.contextUrl + 'VAS_243_ActiveContractsWidget/GetActiveContracts',
                type: 'GET', dataType: 'json', cache: false,
                success: function (response) {
                    var parsed = parseResponse(response);
                    if (parsed.Error) { renderError(); return; }
                    renderData(parsed);
                },
                error: function () { renderError(); }
            });
        }

        this.Initalize = function () {
            createWidget();
            load();
        };

        this.refreshWidget = function () { load(); };

        this.getRoot = function () { return $root; };

        this.disposeComponent = function () {
            $root.off();
            $root.remove();
        };
    };

    VAS.VAS_243_ActiveContractsWidget.prototype.widgetFirevalueChanged = function (value) {
        if (this.listener) { this.listener.widgetFirevalueChanged(value); }
    };

    VAS.VAS_243_ActiveContractsWidget.prototype.addChangeListener = function (listener) {
        this.listener = listener;
    };

    VAS.VAS_243_ActiveContractsWidget.prototype.init = function (windowNo, frame) {
        this.frame = frame;
        this.AD_UserHomeWidgetID = frame.widgetInfo.AD_UserHomeWidgetID;
        this.windowNo = windowNo;
        this.Initalize();
        this.frame.getContentGrid().append(this.getRoot());
        ensureDashInlineSizeVar(this.getRoot());
    };

    VAS.VAS_243_ActiveContractsWidget.prototype.widgetSizeChange = function (height, width) { };

    VAS.VAS_243_ActiveContractsWidget.prototype.refreshWidget = function () {
        this.refreshWidget();
    };

    VAS.VAS_243_ActiveContractsWidget.prototype.dispose = function () {
        this.disposeComponent();
        if (this.frame) { this.frame.dispose(); }
        this.frame = null;
    };

})(VAS, jQuery);
