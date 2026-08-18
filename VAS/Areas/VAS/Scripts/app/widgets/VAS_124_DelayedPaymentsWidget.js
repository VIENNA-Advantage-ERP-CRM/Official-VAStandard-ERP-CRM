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
 * Routing - 2026-08-17: a drill-through row opens the customer. Hosted on a window the
 *           host grid is navigated in place (widgetFirevalueChanged); on the Home /
 *           landing dashboard (windowNo < 0) there is no host grid, so the record is
 *           opened in the standard Customer window via VAS.ZoomUtil.
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

    var CUSTOMER_WINDOW_NAME = 'Business Partner';

    /* 2026-08-17: zoom target when the widget is NOT hosted inside a window
       (windowNo < 0 - the Home / landing dashboard). There is no host grid to navigate
       there, so the record is opened in the standard Customer window; VAS.ZoomUtil
       resolves the AD_Window_ID from the new name, then the old name, then
       VAS_ZoomScreenConfig. */
    var ZOOM_WINDOW_NAME_NEW = 'VAS_CustomerMaster';
    var ZOOM_WINDOW_NAME_OLD = CUSTOMER_WINDOW_NAME;

    VAS.VAS_124_DelayedPaymentsWidget = function () {

        this.frame;
        this.windowNo;

        var $self = this;
        var $root = $('<div class="vas124-root">');
        var $card;
        var $value;
        var $meta;
        var drill;
        /* AD_Window_ID of the Customer window, resolved once on the first Home-page
           zoom and reused afterwards (0 = not resolved yet). */
        var zoomWindowId = 0;

        function label(key, fallback) {
            var translated = VIS.Msg.getMsg(key);
            return translated && translated.charAt(0) !== '[' ? translated : fallback;
        }

        function usesIndianNumbering(isoCode) {
            return VIS.Util.usesIndianNumbering(isoCode);
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

        // Compact currency amount (e.g. ₹3.42Cr / $34.2M / -$5.3T). The magnitude comes
        // from VIS.Util.formatCompactAmount, which scales against the base
        // (accounting-schema) currency's numbering system and renders at the
        // system-configured standard precision; the sign sits BEFORE the symbol.
        function formatCompactAmount(value, symbol, isoCode, precision) {
            var number = Number(value || 0);
            if (!isFinite(number)) { number = 0; }
            var sign = number < 0 ? '-' : '';
            var currency = symbol || isoCode || '';
            return sign + currency + VIS.Util.formatCompactAmount(number, isoCode, getPrecision(precision));
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
            var compact = formatCompactAmount(data.overdue_amount, data.currency_symbol, data.currency_iso, data.std_precision);
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
        // Delayed Payments list endpoint); a row opens the customer record - in the host
        // grid when the widget sits on a window, otherwise (Home / landing page,
        // windowNo < 0) in the standard Customer window.
        function zoomToCustomer(bpId) {
            if (!bpId) { return; }
            if (drill) { drill.close(); }
            try {
                if ($self.windowNo >= 0) {
                    $self.widgetFirevalueChanged({ "TabWhereClause": "C_BPartner.C_BPartner_ID=" + Number(bpId), "TabLayout": "Y", "TabIndex": "0", "ActionName": hostWindowName() || CUSTOMER_WINDOW_NAME, "ActionType": "W" });
                }
                else {
                    VAS.ZoomUtil.zoomToRecord("C_BPartner_ID", Number(bpId), zoomWindowId, ZOOM_WINDOW_NAME_NEW, ZOOM_WINDOW_NAME_OLD)
                        .done(function (id) {
                            if (id > 0) { zoomWindowId = id; }
                        });
                }
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
                // One row per customer in arrears, so the modal's count reconciles with
                // the "N clients overdue" sub-line on the tile.
                mapRow: function (item, cur, h) {
                    var days = Number(item.overdueDays || 0);
                    var invoices = Number(item.invoiceCount || 0);
                    var invoiceWord = invoices === 1 ? label('VAS_124_Invoice', 'invoice') : label('VAS_124_Invoices', 'invoices');
                    var meta = [
                        invoices > 0 ? h.formatCount(invoices) + ' ' + invoiceWord : '',
                        h.formatCount(days) + 'd ' + label('VAS_124_Overdue', 'overdue')
                    ].filter(function (p) { return p; }).join(' · ');
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
