/**
 * VAS_125 Open Pipeline KPI Widget (Customers module dashboard)
 * Purpose - Static 2x1 glass KPI tile. Main value is the total UNWEIGHTED value of
 *           active, open opportunities (C_Project) converted to the tenant base
 *           currency; the sub-line is the count of distinct customers with at least
 *           one qualifying opportunity. Read-only headline - the drill-down is the
 *           separate High-Value Pipeline widget. No click / hover-lift / navigation.
 * Design  - kpi-open-pipeline.html (attached) + Design Specs/dashboard-widgets.md
 *           "KPI And Summary Widget". Reference kpiWidget tile: muted label on top,
 *           big bold value, muted sub-line at the bottom (space-between), default
 *           glass surface. Internal sizing in em against the widget-root clamp;
 *           borders/radii in px. CSS namespaced vas125-* (MPC prefix rule).
 *
 * Backend - VAS_125_OpenPipelineWidget/GetOpenPipeline
 *
 * Routing - 2026-08-17: a drill-through row opens the customer. Hosted on a window the
 *           host grid is navigated in place (widgetFirevalueChanged); on the Home /
 *           landing dashboard (windowNo < 0) there is no host grid, so the record is
 *           opened in the standard Customer window via VAS.ZoomUtil.
 *           2026-08-17 - the drill-through did not open anything: its endpoint
 *           (VAS_139/GetRows) raised ORA-01008 on Oracle, so no rows loaded at all, and
 *           the lead-backed rows it returns carry customerId 0, which the shared drill
 *           swallowed silently. The endpoint is fixed in
 *           VAS_139_HighValuePipelineWidgetController; a lead row is now tagged and opens
 *           its Lead record instead of doing nothing (see mapRow / zoomToLead below).
 *
 * ── Labels / Message Keys ─────────────────────────────────────────────────
 *  # | Current Text                | Message Key
 * ---+-----------------------------+--------------------------------
 *  1 | Open pipeline               | VAS_125_OpenPipeline
 *  2 | clients with opps           | VAS_125_ClientsWithOpps
 *  3 | client with opps            | VAS_125_ClientWithOpps
 *  4 | No open opportunities       | VAS_125_NoOpenOpportunities
 *  5 | Pipeline unavailable        | VAS_125_PipelineUnavailable
 *  6 | Currency rate unavailable   | VAS_125_RateUnavailable
 *  7 | Open pipeline (drill title) | VAS_125_DrillTitle
 *  8 | opp                         | VAS_125_Opp
 *  9 | opps                        | VAS_125_Opps
 * 10 | Lead                        | VAS_125_Lead
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

    /* 2026-08-17: a pipeline row may be backed by a C_Lead instead of a C_BPartner (see
       mapRow). A lead lives in its own window and on a different table, so it can never be
       reached by navigating the host Customer grid in place - it is always opened through
       VAS.ZoomUtil, which resolves the id from the new name, then the old name, then
       VAS_ZoomScreenConfig, and simply does not navigate when neither resolves. */
    var LEAD_WINDOW_NAME_NEW = 'VAS_Lead';
    var LEAD_WINDOW_NAME_OLD = 'Lead';

    VAS.VAS_125_OpenPipelineWidget = function () {

        this.frame;
        this.windowNo;

        var $self = this;
        var $root = $('<div class="vas125-root">');
        var $card;
        var $value;
        var $meta;
        var drill;
        /* AD_Window_ID of the Customer window, resolved once on the first Home-page
           zoom and reused afterwards (0 = not resolved yet). */
        var zoomWindowId = 0;
        /* Same, for the Lead window a lead-backed pipeline row opens. */
        var leadWindowId = 0;

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

        // Compact currency amount (e.g. ₹3.42Cr / €1.25M). The magnitude comes from
        // VIS.Util.formatCompactAmount, which scales against the base
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

        // "N clients with opps" / "1 client with opps".
        function clientsWithOppsText(count) {
            var n = Number(count || 0);
            if (n === 1) { return '1 ' + label('VAS_125_ClientWithOpps', 'client with opps'); }
            return n.toLocaleString(window.navigator.language) + ' ' + label('VAS_125_ClientsWithOpps', 'clients with opps');
        }

        function loadKpi() {
            // Layout-stable skeleton; never flash a zero (spec §States Loading).
            $value.text('—').removeAttr('title');
            $meta.text('…');
            $card.attr('aria-busy', 'true');
            $card.attr('aria-label', label('VAS_125_OpenPipeline', 'Open pipeline'));

            $.ajax({
                url: VIS.Application.contextUrl + 'VAS_125_OpenPipelineWidget/GetOpenPipeline',
                type: 'GET',
                cache: false,
                success: function (response) {
                    var data = parseResponse(response);
                    if (data && data.error) { showError(); return; }
                    renderMetric(data || {});
                },
                error: showError,
                complete: function () { $card.attr('aria-busy', 'false'); }
            });
        }

        function renderMetric(data) {
            var customerCount = Number(data.customer_count || 0);
            var pipelineValue = Number(data.pipeline_value || 0);

            // Missing exchange rate -> neutral unavailable state ONLY when nothing
            // could be converted at all. A partial miss (some opportunities in a
            // currency without a rate, but a real base-currency total exists) still
            // shows that total so the headline KPI stays useful.
            if (data.data_complete === false && pipelineValue === 0 && customerCount > 0) {
                $value.text('—').removeAttr('title');
                $meta.text(label('VAS_125_RateUnavailable', 'Currency rate unavailable')).removeAttr('title');
                $card.attr('aria-label', label('VAS_125_OpenPipeline', 'Open pipeline') + ': ' + label('VAS_125_RateUnavailable', 'Currency rate unavailable'));
                return;
            }

            // Empty: formatted zero + "No open opportunities" (spec §States Empty).
            if (customerCount <= 0) {
                var zero = formatCompactAmount(0, data.currency_symbol, data.currency_iso, data.std_precision);
                $value.text(zero).attr('title', formatFullAmount(0, data.currency_symbol, data.currency_iso, data.std_precision));
                $meta.text(label('VAS_125_NoOpenOpportunities', 'No open opportunities')).removeAttr('title');
                $card.attr('aria-label', label('VAS_125_OpenPipeline', 'Open pipeline') + ': ' + zero + '; ' + label('VAS_125_NoOpenOpportunities', 'No open opportunities'));
                return;
            }

            var compact = formatCompactAmount(data.pipeline_value, data.currency_symbol, data.currency_iso, data.std_precision);
            var full = formatFullAmount(data.pipeline_value, data.currency_symbol, data.currency_iso, data.std_precision);
            var countText = clientsWithOppsText(customerCount);

            $value.text(compact).attr('title', full);
            $meta.text(countText).removeAttr('title');
            $card.attr('aria-label', label('VAS_125_OpenPipeline', 'Open pipeline') + ': ' + full + '; ' + countText);
        }

        // Error state - keep the tile + label, show a dash, never substitute demo
        // values (spec §States Error).
        function showError() {
            $value.text('—').removeAttr('title');
            $meta.text(label('VAS_125_PipelineUnavailable', 'Pipeline unavailable')).removeAttr('title');
            $card.attr('aria-label', label('VAS_125_PipelineUnavailable', 'Pipeline unavailable'));
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

        // Drill-through: the customers whose open opportunities make up the pipeline
        // total (reuses the VAS_139 High-Value Pipeline endpoint); a row opens the
        // customer record.
        function zoomToCustomer(bpId) {
            if (!bpId) { return; }
            if (drill) { drill.close(); }
            try {
                if ($self.windowNo >= 0) {
                    $self.widgetFirevalueChanged({ "TabWhereClause": "C_BPartner.C_BPartner_ID=" + Number(bpId), "TabLayout": "Y", "TabIndex": "0", "ActionName": hostWindowName() || CUSTOMER_WINDOW_NAME, "ActionType": "W" });
                }
                else {
                    /* Home / landing page: no host grid, so open the standard Customer window. */
                    VAS.ZoomUtil.zoomToRecord("C_BPartner_ID", Number(bpId), zoomWindowId, ZOOM_WINDOW_NAME_NEW, ZOOM_WINDOW_NAME_OLD)
                        .done(function (id) {
                            if (id > 0) { zoomWindowId = id; }
                        });
                }
            } catch (e) { /* best-effort */ }
        }

        /* A lead-backed row opens the Lead record. There is no in-place option here: the
           host Customer grid is a C_BPartner tab, so a C_Lead can only be shown by opening
           its own window - the same on a window host and on the Home page. */
        function zoomToLead(leadId) {
            if (!leadId) { return; }
            if (drill) { drill.close(); }
            try {
                VAS.ZoomUtil.zoomToRecord("C_Lead_ID", Number(leadId), leadWindowId, LEAD_WINDOW_NAME_NEW, LEAD_WINDOW_NAME_OLD)
                    .done(function (id) {
                        if (id > 0) { leadWindowId = id; }
                    });
            } catch (e) { /* best-effort */ }
        }

        /* The drill hands back the id it rendered plus the kind of record it belongs to. */
        function openDrillRow(recordId, kind) {
            if (kind === 'LEAD') { zoomToLead(recordId); }
            else { zoomToCustomer(recordId); }
        }

        function createDrill() {
            drill = VAS.KpiDrill({
                title: label('VAS_125_DrillTitle', 'Open pipeline'),
                endpoint: 'VAS_139_HighValuePipelineWidget/GetRows',
                pageSize: 7,
                mapData: function (data) {
                    return {
                        items: data.items || [],
                        total: Number(data.total || 0),
                        currency: { symbol: data.currency_symbol || '', iso: data.currency_iso || '', precision: data.std_precision }
                    };
                },
                /* The endpoint ranks ACCOUNTS, and an account is either a business partner
                   or - while the opportunity still hangs off an unconverted lead - a
                   C_Lead, which arrives with customerId 0 and a leadId instead (VAS_139
                   documents the same rule). Both belong in this list because the KPI counts
                   both. A lead row used to carry bpId 0 and was silently unclickable; it now
                   opens the Lead record through navId/navKind, and is tagged so the row says
                   which kind of record it leads to. */
                mapRow: function (item, cur, h) {
                    var opps = Number(item.openOpps || 0);
                    var oppWord = opps === 1 ? label('VAS_125_Opp', 'opp') : label('VAS_125_Opps', 'opps');
                    var bpId = Number(item.customerId || 0);
                    var leadId = Number(item.leadId || 0);
                    var isLead = bpId <= 0;
                    var meta = h.formatCount(opps) + ' ' + oppWord;
                    if (isLead) { meta += ' · ' + label('VAS_125_Lead', 'Lead'); }
                    return {
                        bpId: bpId,
                        navId: isLead ? leadId : bpId,
                        navKind: isLead ? 'LEAD' : 'BP',
                        title: item.customerName,
                        meta: meta,
                        valueText: h.formatMoney(item.pipeline, cur)
                    };
                },
                navigate: openDrillRow
            });
        }

        this.Initalize = function () {
            $card = $(
                '<div class="vas125-card MPC-kpi-clickable" aria-live="polite" role="button" tabindex="0">' +
                    '<div class="vas125-label"></div>' +
                    '<div class="vas125-group">' +
                        '<div class="vas125-value">—</div>' +
                        '<div class="vas125-meta"></div>' +
                    '</div>' +
                '</div>'
            );

            $card.find('.vas125-label').text(label('VAS_125_OpenPipeline', 'Open pipeline'));
            $value = $card.find('.vas125-value');
            $meta = $card.find('.vas125-meta');
            $root.append($card);

            createDrill();
            $card.on('click', function () { drill.open(); });
            $card.on('keydown', function (e) {
                if (e.key === 'Enter' || e.key === ' ' || e.key === 'Spacebar') { e.preventDefault(); drill.open(); }
            });
            $(document).on('keydown.MPCvas125drill', function (e) {
                if (e.key === 'Escape' && drill && drill.isOpen()) { drill.close(); }
            });

            loadKpi();
        };

        this.refreshWidget = function () {
            loadKpi();
        };

        this.getRoot = function () { return $root; };

        this.disposeComponent = function () {
            $(document).off('keydown.MPCvas125drill');
            if (drill) { drill.dispose(); drill = null; }
            $root.remove();
        };
    };

    VAS.VAS_125_OpenPipelineWidget.prototype.widgetFirevalueChanged = function (value) {
        if (this.listener) { this.listener.widgetFirevalueChanged(value); }
    };

    VAS.VAS_125_OpenPipelineWidget.prototype.addChangeListener = function (listener) {
        this.listener = listener;
    };

    VAS.VAS_125_OpenPipelineWidget.prototype.init = function (windowNo, frame) {
        this.frame = frame;
        this.AD_UserHomeWidgetID = frame.widgetInfo.AD_UserHomeWidgetID;
        this.windowNo = windowNo;
        this.Initalize();
        this.frame.getContentGrid().append(this.getRoot());
        ensureDashInlineSizeVar(this.getRoot());
    };

    VAS.VAS_125_OpenPipelineWidget.prototype.widgetSizeChange = function (height, width) { };

    VAS.VAS_125_OpenPipelineWidget.prototype.refreshWidget = function () {
        this.refreshWidget();
    };

    VAS.VAS_125_OpenPipelineWidget.prototype.dispose = function () {
        this.disposeComponent();
        if (this.frame) { this.frame.dispose(); }
        this.frame = null;
    };

})(VAS, jQuery);
