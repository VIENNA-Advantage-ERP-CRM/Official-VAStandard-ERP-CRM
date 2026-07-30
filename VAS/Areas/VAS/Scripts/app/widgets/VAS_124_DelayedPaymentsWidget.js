/**
 * VAS_124 Delayed Payments KPI Widget (Customers module dashboard)
 * Purpose - Static 2x1 glass KPI tile with a red risk tint. Main value is the
 *           total OUTSTANDING amount on overdue customer receivables (base tenant
 *           currency); the sub-line is the count of distinct customers in arrears.
 *           Read-only headline - the drill-down list lives in the separate Delayed
 *           Payments list widget. No click / hover-lift / navigation.
 * Design  - kpi-delayed-payments.html (attached) + Design Specs/dashboard-widgets.md
 *           "KPI And Summary Widget". Reference kpiWidget tile with the red risk
 *           tint; muted label on top, big bold value, muted sub-line at the bottom.
 *           Internal sizing in em against the widget-root clamp; borders/radii in
 *           px. CSS namespaced vas124-* (Prompt_Instructions MPC prefix rule).
 *
 * Backend - VAS_124_DelayedPaymentsWidget/GetDelayedPayments
 *
 * ── Labels / Message Keys ─────────────────────────────────────────────────
 *  # | Current Text                        | Message Key
 * ---+-------------------------------------+--------------------------------
 *  1 | Delayed payments                    | VAS_124_DelayedPayments
 *  2 | clients overdue                     | VAS_124_ClientsOverdue
 *  3 | client overdue                      | VAS_124_ClientOverdue
 *  4 | No clients overdue                  | VAS_124_NoClientsOverdue
 *  5 | Unable to load delayed payments.    | VAS_124_UnableToLoad
 * ──────────────────────────────────────────────────────────────────────────
 */
; VAS = window.VAS || {};

; (function (VAS, $) {

    /* Keep --dash-inline-size on :root equal to the dashboard container width so
       the widget clamp resolves against the dashboard's visible width. */
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

    // Currencies of Indian-numbering countries get Indian digit grouping and
    // Lakh/Crore compact notation; all others get international grouping and K/M/B/T.
    var INDIAN_NUMBERING_CURRENCIES = ['INR', 'PKR', 'BDT', 'NPR', 'BTN', 'LKR'];

    var CUSTOMER_WINDOW_NAME = 'Business Partner';

    VAS.VAS_124_DelayedPaymentsWidget = function () {

        this.frame;
        this.windowNo;

        var $self = this;
        var $root = $('<div class="vas124-root">');
        var $card;
        var $value;
        var $meta;
        var drill;

        function label(key, fallback) {
            var translated = VIS.Msg.getMsg(key);
            return translated && translated.charAt(0) !== '[' ? translated : fallback;
        }

        function usesIndianNumbering(isoCode) {
            return INDIAN_NUMBERING_CURRENCIES.indexOf(String(isoCode || '').toUpperCase()) >= 0;
        }

        function currencyLocale(isoCode) {
            return usesIndianNumbering(isoCode) ? 'en-IN' : window.navigator.language;
        }

        function getPrecision(value) {
            var precision = Number(value);
            if (!isNaN(precision) && precision >= 0) { return precision; }
            if (VIS.Env && VIS.Env.getCtx && VIS.Env.getCtx().getStdPrecision) {
                precision = Number(VIS.Env.getCtx().getStdPrecision());
            }
            return !isNaN(precision) && precision >= 0 ? precision : 0;
        }

        function trimTrailingZeros(text) {
            return text.replace(/\.0+$/, '').replace(/(\.\d*?)0+$/, '$1');
        }

        // Compact currency amount (e.g. ₹3.42 Cr / $34.2M / -$5.3T). The sign sits
        // BEFORE the currency symbol and compaction is on the absolute value.
        function formatCompactAmount(value, symbol, isoCode) {
            var number = Number(value || 0);
            if (!isFinite(number)) { number = 0; }
            var sign = number < 0 ? '-' : '';
            var abs = Math.abs(number);
            var currency = symbol || isoCode || '';
            var compact;
            if (usesIndianNumbering(isoCode)) {
                if (abs >= 10000000) { compact = trimTrailingZeros((abs / 10000000).toFixed(2)) + ' Cr'; }
                else if (abs >= 100000) { compact = trimTrailingZeros((abs / 100000).toFixed(2)) + ' Lakh'; }
                else if (abs >= 1000) { compact = trimTrailingZeros((abs / 1000).toFixed(1)) + 'K'; }
                else { compact = abs.toLocaleString(currencyLocale(isoCode), { maximumFractionDigits: 2 }); }
            } else {
                if (abs >= 1000000000000) { compact = trimTrailingZeros((abs / 1000000000000).toFixed(1)) + 'T'; }
                else if (abs >= 1000000000) { compact = trimTrailingZeros((abs / 1000000000).toFixed(1)) + 'B'; }
                else if (abs >= 1000000) { compact = trimTrailingZeros((abs / 1000000).toFixed(1)) + 'M'; }
                else if (abs >= 1000) { compact = trimTrailingZeros((abs / 1000).toFixed(1)) + 'K'; }
                else { compact = abs.toLocaleString(currencyLocale(isoCode), { maximumFractionDigits: 2 }); }
            }
            return sign + currency + compact;
        }

        // Full, precise currency amount for the value tooltip.
        function formatFullAmount(value, symbol, isoCode, precision) {
            var number = Number(value || 0);
            if (!isFinite(number)) { number = 0; }
            var sign = number < 0 ? '-' : '';
            var abs = Math.abs(number);
            var currency = symbol || isoCode || '';
            var stdPrecision = getPrecision(precision);
            var formatted = abs.toLocaleString(currencyLocale(isoCode), {
                minimumFractionDigits: stdPrecision,
                maximumFractionDigits: stdPrecision
            });
            return sign + (currency ? currency + ' ' + formatted : formatted);
        }

        function parseResponse(response) {
            var parsed = response;
            if (typeof parsed === 'string' && parsed.length) { parsed = JSON.parse(parsed); }
            if (typeof parsed === 'string' && parsed.length) { parsed = JSON.parse(parsed); }
            return parsed || {};
        }

        // "No clients overdue" / "1 client overdue" / "N clients overdue".
        function overdueCountText(count) {
            var n = Number(count || 0);
            if (!isFinite(n) || n <= 0) { return label('VAS_124_NoClientsOverdue', 'No clients overdue'); }
            if (n === 1) { return '1 ' + label('VAS_124_ClientOverdue', 'client overdue'); }
            return n.toLocaleString(window.navigator.language) + ' ' + label('VAS_124_ClientsOverdue', 'clients overdue');
        }

        function loadKpi() {
            // Layout-stable skeleton; never flash a zero (spec §7 Loading).
            $value.text('—').removeAttr('title');
            $meta.text('…');
            $card.attr('aria-label', label('VAS_124_DelayedPayments', 'Delayed payments'));

            $.ajax({
                url: VIS.Application.contextUrl + 'VAS_124_DelayedPaymentsWidget/GetDelayedPayments',
                type: 'GET',
                cache: false,
                success: function (response) {
                    var data = parseResponse(response);
                    if (data && data.error) { showError(); return; }
                    renderMetric(data || {});
                },
                error: showError
            });
        }

        function renderMetric(data) {
            var compact = formatCompactAmount(data.overdue_amount, data.currency_symbol, data.currency_iso);
            var full = formatFullAmount(data.overdue_amount, data.currency_symbol, data.currency_iso, data.std_precision);
            var countText = overdueCountText(data.overdue_customer_count);

            $value.text(compact).attr('title', full);
            $meta.text(countText).removeAttr('title');

            // Accessible summary: amount + who is overdue (spec §10). Not red alone.
            $card.attr('aria-label', label('VAS_124_DelayedPayments', 'Delayed payments') + ': ' + full + '; ' + countText);
        }

        // Error state - keep the tile + label, show a dash, never substitute demo
        // values (spec §7 Error).
        function showError() {
            $value.text('—').removeAttr('title');
            $meta.text(label('VAS_124_UnableToLoad', 'Unable to load delayed payments.')).removeAttr('title');
            $card.attr('aria-label', label('VAS_124_UnableToLoad', 'Unable to load delayed payments.'));
        }

        // Resolve the window hosting this widget so the framework navigates the
        // current grid in place; falls back to the Business Partner window name.
        function hostWindowName() {
            try {
                var listener = $self.listener;
                for (var i = 0; i < 6 && listener; i++) {
                    if (listener.apanel && listener.apanel.gridWindow && listener.apanel.gridWindow.getName) {
                        return listener.apanel.gridWindow.getName();
                    }
                    if (listener.gridWindow && listener.gridWindow.getName) {
                        return listener.gridWindow.getName();
                    }
                    listener = listener.listener;
                }
            } catch (e) { /* best-effort */ }
            return '';
        }

        // Drill-through: the overdue schedules behind the total (reuses the VAS_138
        // Delayed Payments list endpoint); a row opens the customer record.
        function zoomToCustomer(bpId) {
            if (!bpId) { return; }
            if (drill) { drill.close(); }
            try {
                $self.widgetFirevalueChanged({ "TabWhereClause": "C_BPartner.C_BPartner_ID=" + Number(bpId), "TabLayout": "Y", "TabIndex": "0", "ActionName": hostWindowName() || CUSTOMER_WINDOW_NAME, "ActionType": "W" });
            } catch (e) { /* best-effort */ }
        }

        function createDrill() {
            drill = VAS.KpiDrill({
                title: label('VAS_124_DrillTitle', 'Overdue receivables'),
                endpoint: 'VAS_138_DelayedPaymentsListWidget/GetRows',
                pageSize: 7,
                mapData: function (data) {
                    return {
                        items: data.items || [],
                        total: Number(data.total || 0),
                        currency: { symbol: data.currency_symbol || '', iso: data.currency_iso || '', precision: data.std_precision }
                    };
                },
                mapRow: function (item, cur, h) {
                    var days = Number(item.overdueDays || 0);
                    var meta = [item.invoice, h.formatCount(days) + 'd ' + label('VAS_124_Overdue', 'overdue')].filter(function (p) { return p; }).join(' · ');
                    return { bpId: item.customerId, title: item.customerName, meta: meta, valueText: h.formatMoney(item.overdueAmt, cur), valueTone: 'danger' };
                },
                navigate: zoomToCustomer
            });
        }

        this.Initalize = function () {
            $card = $(
                '<div class="vas124-card MPC-kpi-clickable" aria-live="polite" role="button" tabindex="0">' +
                    '<div class="vas124-label"></div>' +
                    '<div class="vas124-group">' +
                        '<div class="vas124-value">—</div>' +
                        '<div class="vas124-meta"></div>' +
                    '</div>' +
                '</div>'
            );

            $card.find('.vas124-label').text(label('VAS_124_DelayedPayments', 'Delayed payments'));
            $value = $card.find('.vas124-value');
            $meta = $card.find('.vas124-meta');
            $root.append($card);

            createDrill();
            $card.on('click', function () { drill.open(); });
            $card.on('keydown', function (e) {
                if (e.key === 'Enter' || e.key === ' ' || e.key === 'Spacebar') { e.preventDefault(); drill.open(); }
            });
            $(document).on('keydown.MPCvas124drill', function (e) {
                if (e.key === 'Escape' && drill && drill.isOpen()) { drill.close(); }
            });

            loadKpi();
        };

        this.refreshWidget = function () {
            loadKpi();
        };

        this.getRoot = function () { return $root; };

        this.disposeComponent = function () {
            $(document).off('keydown.MPCvas124drill');
            if (drill) { drill.dispose(); drill = null; }
            $root.remove();
        };
    };

    VAS.VAS_124_DelayedPaymentsWidget.prototype.widgetFirevalueChanged = function (value) {
        if (this.listener) { this.listener.widgetFirevalueChanged(value); }
    };

    VAS.VAS_124_DelayedPaymentsWidget.prototype.addChangeListener = function (listener) {
        this.listener = listener;
    };

    VAS.VAS_124_DelayedPaymentsWidget.prototype.init = function (windowNo, frame) {
        this.frame = frame;
        this.AD_UserHomeWidgetID = frame.widgetInfo.AD_UserHomeWidgetID;
        this.windowNo = windowNo;
        this.Initalize();
        this.frame.getContentGrid().append(this.getRoot());
        ensureDashInlineSizeVar(this.getRoot());
    };

    VAS.VAS_124_DelayedPaymentsWidget.prototype.widgetSizeChange = function (height, width) { };

    VAS.VAS_124_DelayedPaymentsWidget.prototype.refreshWidget = function () {
        this.refreshWidget();
    };

    VAS.VAS_124_DelayedPaymentsWidget.prototype.dispose = function () {
        this.disposeComponent();
        if (this.frame) { this.frame.dispose(); }
        this.frame = null;
    };

})(VAS, jQuery);
