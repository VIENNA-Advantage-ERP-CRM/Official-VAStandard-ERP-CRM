/**
 * VAS_122 Total Customers KPI Widget (Customers module dashboard)
 * Purpose - Static 2x1 glass KPI tile. Main value is the count of active
 *           customers (C_BPartner.IsCustomer='Y') in the logged-in tenant and the
 *           organizations the role may access. Sub-line is the truthful combined
 *           customer value (SUM of ActualLifeTimeValue) formatted in the tenant
 *           currency - never labelled ARR, because the schema has no recurring-
 *           revenue column. No click / navigation (static KPI).
 * Design  - kpi-total-customers.html (attached) + Design Specs/dashboard-widgets.md
 *           "KPI And Summary Widget". Matches the shipped Total-Value KPI tile
 *           (VAS_075): label, big value, muted meta. Internal sizing in em against
 *           the widget-root clamp; borders/radii/shadows in px. CSS namespaced
 *           vas122-* (Prompt_Instructions MPC prefix rule).
 *
 * Backend - VAS_122_TotalCustomersWidget/GetTotalCustomers
 *
 * ── Labels / Message Keys ─────────────────────────────────────────────────
 *  # | Current Text                | Message Key
 * ---+-----------------------------+--------------------------------
 *  1 | Total customers             | VAS_122_TotalCustomers
 *  2 | Combined lifetime value     | VAS_122_CombinedLifetimeValue
 *  3 | Combined ARR                | VAS_122_CombinedARR
 *  4 | Active customers            | VAS_122_ActiveCustomers
 *  5 | Unable to load customer total | VAS_122_UnableToLoad
 *  6 | Customers                   | VAS_122_DrillTitle
 *  7 | No contact                  | VAS_122_NoContact
 *  8 | more                        | VAS_122_MoreContacts
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

    VAS.VAS_122_TotalCustomersWidget = function () {

        this.frame;
        this.windowNo;

        var $self = this;
        var drill;
        var $root = $('<div class="vas122-root">');
        var $card;
        var $value;
        var $meta;

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

        // Full, precise integer count for the main value (localized, no compaction).
        function formatCount(value) {
            var number = Number(value || 0);
            if (!isFinite(number)) { number = 0; }
            return Math.round(number).toLocaleString(window.navigator.language);
        }

        // Compact currency amount for the sub-line (e.g. ₹3.42Cr / $34.2M / -$5.3T).
        // The magnitude comes from VIS.Util.formatCompactAmount, which scales against
        // the base (accounting-schema) currency's numbering system and renders at the
        // system-configured standard precision. Composition stays local: the sign sits
        // BEFORE the currency symbol ("-$5.3T", not "$-5.3T").
        function formatCompactAmount(value, symbol, isoCode, precision) {
            var number = Number(value || 0);
            if (!isFinite(number)) { number = 0; }
            var sign = number < 0 ? '-' : '';
            var currency = symbol || isoCode || '';
            return sign + currency + VIS.Util.formatCompactAmount(number, isoCode, getPrecision(precision));
        }

        // Full, precise currency amount for the sub-line tooltip. The sign sits
        // before the currency symbol to match the compact form.
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

        // Drill-through row meta: the customer's contact people, all of them when there
        // is more than one. The server caps the listed names and reports the full
        // count, so a long address book collapses to "…· +N more". The row's title
        // attribute carries the whole string, so truncation loses nothing.
        function contactMeta(item) {
            var contacts = (item.contacts || []).filter(function (name) { return name; });
            if (!contacts.length) { return label('VAS_122_NoContact', 'No contact'); }

            var meta = contacts.join(' · ');
            var hidden = Number(item.contactCount || 0) - contacts.length;
            if (hidden > 0) {
                meta += ' · +' + formatCount(hidden) + ' ' + label('VAS_122_MoreContacts', 'more');
            }
            return meta;
        }

        function valueModeLabel(mode) {
            if (mode === 'arr') { return label('VAS_122_CombinedARR', 'Combined ARR'); }
            if (mode === 'count_only') { return label('VAS_122_ActiveCustomers', 'Active customers'); }
            return label('VAS_122_CombinedLifetimeValue', 'Combined lifetime value');
        }

        function loadKpi() {
            // Layout-stable skeleton; never briefly render zero (spec §Loading).
            $value.text('—').removeAttr('title');
            $meta.text('…');

            $.ajax({
                url: VIS.Application.contextUrl + 'VAS_122_TotalCustomersWidget/GetTotalCustomers',
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
            var total = Number(data.total_customers || 0);
            var mode = data.value_mode || 'lifetime_value';

            $value.text(formatCount(total)).attr('title', formatCount(total));

            if (mode === 'count_only') {
                // Count-only mode: the meta is a plain descriptor, no amount.
                $meta.text(valueModeLabel(mode)).removeAttr('title');
                return;
            }

            var compact = formatCompactAmount(data.total_customer_value, data.currency_symbol, data.currency_iso, data.std_precision);
            var full = formatFullAmount(data.total_customer_value, data.currency_symbol, data.currency_iso, data.std_precision);
            var subLabel = valueModeLabel(mode);

            $meta.text(compact + ' ' + subLabel).attr('title', full + ' ' + subLabel);
        }

        // Error state - keep the tile + label, show a dash, do not break the
        // dashboard, never substitute demo values (spec §Error).
        function showError() {
            $value.text('—').removeAttr('title');
            $meta.text(label('VAS_122_UnableToLoad', 'Unable to load customer total')).removeAttr('title');
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

        // Drill-through: the customers behind the count, ranked by lifetime value
        // (VAS_122 GetCustomers); a row opens the customer record.
        function zoomToCustomer(bpId) {
            if (!bpId) { return; }
            if (drill) { drill.close(); }
            try {
                $self.widgetFirevalueChanged({ "TabWhereClause": "C_BPartner.C_BPartner_ID=" + Number(bpId), "TabLayout": "Y", "TabIndex": "0", "ActionName": hostWindowName() || CUSTOMER_WINDOW_NAME, "ActionType": "W" });
            } catch (e) { /* best-effort */ }
        }

        function createDrill() {
            drill = VAS.KpiDrill({
                title: label('VAS_122_DrillTitle', 'Customers'),
                endpoint: 'VAS_122_TotalCustomersWidget/GetCustomers',
                pageSize: 7,
                mapData: function (data) {
                    return {
                        items: data.items || [],
                        total: Number(data.total || 0),
                        currency: { symbol: data.currency_symbol || '', iso: data.currency_iso || '', precision: data.std_precision }
                    };
                },
                mapRow: function (item, cur, h) {
                    return { bpId: item.customerId, title: item.customerName, meta: contactMeta(item), valueText: h.formatMoney(item.customerValue, cur) };
                },
                navigate: zoomToCustomer
            });
        }

        this.Initalize = function () {
            $card = $(
                '<div class="vas122-card MPC-kpi-clickable" aria-live="polite" role="button" tabindex="0">' +
                    '<div class="vas122-label"></div>' +
                    '<div class="vas122-group">' +
                        '<div class="vas122-value">—</div>' +
                        '<div class="vas122-meta"></div>' +
                    '</div>' +
                '</div>'
            );

            $card.find('.vas122-label').text(label('VAS_122_TotalCustomers', 'Total Customers'));
            $value = $card.find('.vas122-value');
            $meta = $card.find('.vas122-meta');
            $root.append($card);

            createDrill();
            $card.on('click', function () { drill.open(); });
            $card.on('keydown', function (e) {
                if (e.key === 'Enter' || e.key === ' ' || e.key === 'Spacebar') { e.preventDefault(); drill.open(); }
            });
            $(document).on('keydown.MPCvas122drill', function (e) {
                if (e.key === 'Escape' && drill && drill.isOpen()) { drill.close(); }
            });

            loadKpi();
        };

        this.refreshWidget = function () {
            loadKpi();
        };

        this.getRoot = function () { return $root; };

        this.disposeComponent = function () {
            $(document).off('keydown.MPCvas122drill');
            if (drill) { drill.dispose(); drill = null; }
            $root.remove();
        };
    };

    VAS.VAS_122_TotalCustomersWidget.prototype.widgetFirevalueChanged = function (value) {
        if (this.listener) { this.listener.widgetFirevalueChanged(value); }
    };

    VAS.VAS_122_TotalCustomersWidget.prototype.addChangeListener = function (listener) {
        this.listener = listener;
    };

    VAS.VAS_122_TotalCustomersWidget.prototype.init = function (windowNo, frame) {
        this.frame = frame;
        this.AD_UserHomeWidgetID = frame.widgetInfo.AD_UserHomeWidgetID;
        this.windowNo = windowNo;
        this.Initalize();
        this.frame.getContentGrid().append(this.getRoot());
        ensureDashInlineSizeVar(this.getRoot());
    };

    VAS.VAS_122_TotalCustomersWidget.prototype.widgetSizeChange = function (height, width) { };

    VAS.VAS_122_TotalCustomersWidget.prototype.refreshWidget = function () {
        this.refreshWidget();
    };

    VAS.VAS_122_TotalCustomersWidget.prototype.dispose = function () {
        this.disposeComponent();
        if (this.frame) { this.frame.dispose(); }
        this.frame = null;
    };

})(VAS, jQuery);
