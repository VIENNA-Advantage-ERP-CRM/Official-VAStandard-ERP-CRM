/**
 * VAS_139 High-Value Pipeline Widget (Customers module dashboard)
 * Purpose - 3x2 glass ranked list: customers with open opportunities ordered by
 *           total open pipeline value, each with a bar relative to the top client.
 *           Header shows "{clients} clients · {total} open". A row opens the pipeline
 *           modal: the customer's opportunities (status, close date, converted value)
 *           and a reconciled total-pipeline footer. The modal's "Customer" button
 *           opens the shared customer detail panel.
 * Design  - high-value-pipeline.html (attached) + Design Specs/dashboard-widgets.md
 *           "Grid Data Widget". Glass surface, icon-well header, paged rows (7),
 *           relative blue bars, circular pager. Internal sizing in em against the
 *           widget-root clamp; borders/radii in px. CSS namespaced vas139-*.
 *
 * Backend - VAS_139_HighValuePipelineWidget/GetRows            (paged ranked rows + totals)
 *           VAS_139_HighValuePipelineWidget/GetOpportunities   (one customer's opportunities)
 *           Customer detail reuses the generic VAS_126 endpoint:
 *             VAS_126_OpenTicketsWidget/GetCustomerDetail
 *
 * ── Labels / Message Keys ─────────────────────────────────────────────────
 *  #  | Current Text                       | Message Key
 * ----+------------------------------------+--------------------------------
 *  1  | High-value pipeline                | VAS_139_HighValuePipeline
 *  2  | clients                            | VAS_139_Clients
 *  3  | client                             | VAS_139_Client
 *  4  | open                               | VAS_139_Open
 *  5  | opp                                | VAS_139_Opp
 *  6  | opps                               | VAS_139_Opps
 *  7  | of                                 | VAS_139_Of
 * 7a  | Showing                            | VAS_139_Showing
 * 7b  | Previous page                      | VAS_139_PrevPage
 * 7c  | Next page                          | VAS_139_NextPage
 *  8  | Nothing here right now.            | VAS_139_NothingHere
 *  9  | Unable to load pipeline.           | VAS_139_UnableToLoad
 * 10  | Retry                              | VAS_139_Retry
 * 11  | Pipeline                           | VAS_139_Pipeline
 * 12  | open opportunity                   | VAS_139_OpenOpportunity
 * 13  | open opportunities                 | VAS_139_OpenOpportunities
 * 14  | total                              | VAS_139_Total
 * 15  | Total pipeline                     | VAS_139_TotalPipeline
 * 16  | No open opportunities.             | VAS_139_NoOpportunities
 * 17  | Customer                           | VAS_139_Customer
 * 18  | Open record                        | VAS_139_OpenRecord
 * 19  | Close                              | VAS_139_Close
 * 20  | Customer details                   | VAS_139_CustomerDetails
 * 21  | Draft                              | VAS_139_StatusDR
 * 22  | In progress                        | VAS_139_StatusIP
 * 23  | On hold                            | VAS_139_StatusHD
 * 24  | likely close                       | VAS_139_LikelyClose
 * ──────────────────────────────────────────────────────────────────────────
 */
; VAS = window.VAS || {};

; (function (VAS, $) {

    // Generic customer detail endpoint (built with VAS_126).
    var CUSTOMER_ENDPOINT = 'VAS_126_OpenTicketsWidget/';
    var CUSTOMER_WINDOW_NAME = 'Business Partner';

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

    VAS.VAS_139_HighValuePipelineWidget = function () {

        this.frame;
        this.windowNo;

        var $self = this;
        var $root = $('<div class="vas139-root">');
        var $sub;
        var $body;

        var pageSize = 7;
        var pageOffset = 0;
        var listTotal = 0;
        var rowsSeq = 0;
        var widgetCurrency = { symbol: '', iso: '', precision: 2 };

        // Pipeline (opportunities) modal state.
        var $pipe, $pipeBody, $pipeCust, currentPipeId = 0, currentPipeLeadId = 0, currentPipeName = '';
        var pipeCurrency = { symbol: '', iso: '', precision: 2 };

        // Customer detail modal state (reuses VAS_126 generic endpoint).
        var $detail, $detailBody, $detailSummary, currentDetailId = 0, currentDetailName = '';
        var detailCurrency = { symbol: '', iso: '', precision: 2 };

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

        // Projects fact: name the customer's delivery project rather than only
        // counting it. The server sends the first project name plus the total, so
        // a customer with several reads "Name +2"; none renders the empty dash.
        function projectText(name, count, dash) {
            if (!name) { return dash; }
            var extra = Number(count || 0) - 1;
            return escapeHtml(extra > 0 ? name + ' +' + formatCount(extra) : name);
        }

        function formatCount(value) {
            var n = Number(value || 0);
            if (!isFinite(n)) { n = 0; }
            return Math.round(n).toLocaleString(window.navigator.language);
        }

        // Standard precision of the supplied base-currency descriptor, falling back to
        // the session context when the endpoint did not send one.
        function precisionOf(cur) {
            var p = Number(cur && cur.precision);
            if (!isNaN(p) && p >= 0) { return p; }
            if (VIS.Env && VIS.Env.getCtx && VIS.Env.getCtx().getStdPrecision) {
                p = Number(VIS.Env.getCtx().getStdPrecision());
            }
            return !isNaN(p) && p >= 0 ? p : 0;
        }

        // Compact money against the base (accounting-schema) currency the endpoint
        // reported. VIS.Util.formatCompactAmount supplies the scaled magnitude at the
        // system-configured precision; the sign is composed before the symbol here.
        function formatMoney(value, cur) {
            var n = Number(value || 0); if (!isFinite(n)) { n = 0; }
            var sign = n < 0 ? '-' : '';
            var iso = (cur && cur.iso) || '';
            var symbol = (cur && (cur.symbol || cur.iso)) || '';
            return sign + symbol + VIS.Util.formatCompactAmount(n, iso, precisionOf(cur));
        }

        function formatDate(iso) {
            if (!iso) { return ''; }
            var parts = String(iso).split('-');
            if (parts.length !== 3) { return iso; }
            var d = new Date(parseInt(parts[0], 10), parseInt(parts[1], 10) - 1, parseInt(parts[2], 10));
            if (isNaN(d.getTime())) { return iso; }
            return d.toLocaleDateString(window.navigator.language, { year: 'numeric', month: 'short', day: '2-digit' });
        }

        // VAS_Opportunity.VAS_OppStage codes. 16 (Won) and 17 (Lost/Archived) are
        // terminal and never reach this list; a blank stage is Open / Unassigned.
        function statusLabel(code) {
            if (code === '10') { return label('VAS_139_Stage10', 'Prospecting'); }
            if (code === '11') { return label('VAS_139_Stage11', 'Discovery/Design'); }
            if (code === '12') { return label('VAS_139_Stage12', 'Product Evaluation'); }
            if (code === '13') { return label('VAS_139_Stage13', 'Proposal'); }
            if (code === '15') { return label('VAS_139_Stage15', 'Negotiation'); }
            if (!code) { return label('VAS_139_StageNone', 'Unassigned'); }
            return code;
        }

        function icon(name) {
            if (name === 'trend') {
                return '<svg viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2" stroke-linecap="round" stroke-linejoin="round" aria-hidden="true"><polyline points="22 7 13.5 15.5 8.5 10.5 2 17"></polyline><polyline points="16 7 22 7 22 13"></polyline></svg>';
            }
            if (name === 'cash') {
                return '<svg viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2" stroke-linecap="round" stroke-linejoin="round" aria-hidden="true"><rect x="2" y="6" width="20" height="12" rx="2"></rect><circle cx="12" cy="12" r="2"></circle><path d="M6 12h.01M18 12h.01"></path></svg>';
            }
            if (name === 'ticket') {
                return '<svg viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2" stroke-linecap="round" stroke-linejoin="round" aria-hidden="true"><path d="M3 9a2 2 0 0 1 2-2h14a2 2 0 0 1 2 2v1a2 2 0 0 0 0 4v1a2 2 0 0 1-2 2H5a2 2 0 0 1-2-2v-1a2 2 0 0 0 0-4z"></path><line x1="9" y1="7" x2="9" y2="17"></line></svg>';
            }
            if (name === 'chev') {
                return '<svg viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2" stroke-linecap="round" stroke-linejoin="round" aria-hidden="true"><polyline points="9 18 15 12 9 6"></polyline></svg>';
            }
            if (name === 'chevL') {
                return '<svg viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2" stroke-linecap="round" stroke-linejoin="round" aria-hidden="true"><polyline points="15 18 9 12 15 6"></polyline></svg>';
            }
            if (name === 'arrow') {
                return '<svg viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2" stroke-linecap="round" stroke-linejoin="round" aria-hidden="true"><line x1="5" y1="12" x2="19" y2="12"></line><polyline points="12 5 19 12 12 19"></polyline></svg>';
            }
            if (name === 'close') {
                return '<svg viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2" stroke-linecap="round" aria-hidden="true"><path d="M18 6 6 18"></path><path d="m6 6 12 12"></path></svg>';
            }
            return '';
        }

        var AVATAR_COLORS = ['#1F83FF', '#5F4AA6', '#0B6B45', '#D78B10', '#0083DA', '#A33F3F'];
        function avatarColor(text) {
            var hash = 0, value = String(text || '');
            for (var i = 0; i < value.length; i++) { hash = (hash * 31 + value.charCodeAt(i)) % AVATAR_COLORS.length; }
            return AVATAR_COLORS[hash];
        }
        function initials(name) {
            return String(name || '').split(' ').slice(0, 2).map(function (w) { return w.charAt(0); }).join('').toUpperCase();
        }
        function tierTagHtml(tier) {
            if (!tier) { return ''; }
            var cls = 'vas139-tag-neutral';
            if (tier === 'Platinum') { cls = 'vas139-tag-violet'; }
            else if (tier === 'Gold') { cls = 'vas139-tag-amber'; }
            else if (tier === 'Silver') { cls = 'vas139-tag-info'; }
            return '<span class="vas139-tag ' + cls + '">' + escapeHtml(tier) + '</span>';
        }

        /* ---------- Rows ---------- */

        function loadRows() {
            var seq = ++rowsSeq;
            renderState(label('Loading', 'Loading…'));
            $.ajax({
                url: VIS.Application.contextUrl + 'VAS_139_HighValuePipelineWidget/GetRows',
                type: 'GET', cache: false,
                data: { offset: pageOffset, limit: pageSize },
                success: function (response) {
                    if (seq !== rowsSeq) { return; }
                    var data = parseResponse(response);
                    if (data && data.error) { renderErrorState(); return; }
                    listTotal = Number(data.total || 0);
                    widgetCurrency = { symbol: data.currency_symbol || '', iso: data.currency_iso || '', precision: data.std_precision };
                    var clients = listTotal;
                    var clientWord = clients === 1 ? label('VAS_139_Client', 'client') : label('VAS_139_Clients', 'clients');
                    $sub.text(formatCount(clients) + ' ' + clientWord + ' · ' + formatMoney(data.totalPipeline, widgetCurrency) + ' ' + label('VAS_139_Open', 'open'));
                    renderRows(data.items || []);
                },
                error: function () { if (seq === rowsSeq) { renderErrorState(); } }
            });
        }

        function renderState(message) {
            $body.html('<div class="vas139-state">' + escapeHtml(message) + '</div>');
        }

        function renderErrorState() {
            $body.html('<div class="vas139-state">' + escapeHtml(label('VAS_139_UnableToLoad', 'Unable to load pipeline.')) +
                ' <button type="button" class="vas139-retry">' + escapeHtml(label('VAS_139_Retry', 'Retry')) + '</button></div>');
        }

        function rowHtml(item, cur) {
            var opps = Number(item.openOpps || 0);
            var oppWord = opps === 1 ? label('VAS_139_Opp', 'opp') : label('VAS_139_Opps', 'opps');
            var bar = Math.max(0, Math.min(100, Number(item.barPercent || 0)));
            var name = item.customerName || '';
            return '<div class="vas139-row" data-id="' + Number(item.customerId) + '" data-lead="' + Number(item.leadId || 0) + '">' +
                '<span class="vas139-avatar" style="background:' + avatarColor(name) + '">' + escapeHtml(initials(name)) + '</span>' +
                '<span class="vas139-row-main">' +
                    '<span class="vas139-row-title" title="' + escapeHtml(name) + '">' + escapeHtml(name) + '</span>' +
                    '<span class="vas139-bar"><div style="width:' + bar + '%"></div></span>' +
                '</span>' +
                '<span class="vas139-row-amt">' +
                    '<span class="vas139-amt-val">' + escapeHtml(formatMoney(item.pipeline, cur)) + '</span>' +
                    '<span class="vas139-amt-lab">' + formatCount(opps) + ' ' + escapeHtml(oppWord) + '</span>' +
                '</span>' +
            '</div>';
        }

        function renderRows(items) {
            if (!items.length) {
                renderState(label('VAS_139_NothingHere', 'Nothing here right now.'));
                return;
            }
            var cur = widgetCurrency;
            var rows = items.map(function (it) { return rowHtml(it, cur); }).join('');
            var start = pageOffset + 1;
            var end = pageOffset + items.length;
            var pages = Math.max(1, Math.ceil(listTotal / pageSize));
            var current = Math.floor(pageOffset / pageSize);
            /* Footer pager (dashboard-widgets.md §"Widget Footer Pager"): helper
               left, compact prev · "N of M" · next right. The control stays put
               on a single page with both arrows disabled, so the widget's row
               grid never shifts height between pages. */
            var of = label('VAS_139_Of', 'of');
            var helper = label('VAS_139_Showing', 'Showing') + ' ' + start + '–' + end + ' ' + of + ' ' + formatCount(listTotal);
            var pager = '<div class="vas139-pager">' +
                '<span class="vas139-pglabel">' + escapeHtml(helper) + '</span>' +
                '<span class="vas139-pgctl">' +
                    '<button type="button" class="vas139-pgbtn" data-dir="prev" aria-label="' + escapeHtml(label('VAS_139_PrevPage', 'Previous page')) + '" ' + (current <= 0 ? 'disabled' : '') + '>' + icon('chevL') + '</button>' +
                    '<span class="vas139-pgtext">' + escapeHtml((current + 1) + ' ' + of + ' ' + pages) + '</span>' +
                    '<button type="button" class="vas139-pgbtn" data-dir="next" aria-label="' + escapeHtml(label('VAS_139_NextPage', 'Next page')) + '" ' + (current >= pages - 1 ? 'disabled' : '') + '>' + icon('chev') + '</button>' +
                '</span>' +
            '</div>';
            // Divide the body into pageSize equal rows so records fill the widget
            // top-to-bottom; partial pages stay top-aligned (empty tracks below).
            $body.html('<div class="vas139-list" style="grid-template-rows: repeat(' + pageSize + ', minmax(0, 1fr))">' + rows + '</div>' + pager);
        }

        function turnPage(direction) {
            var next = pageOffset + (direction === 'next' ? pageSize : -pageSize);
            if (next < 0) { next = 0; }
            if (next >= listTotal) { return; }
            pageOffset = next;
            loadRows();
        }

        /* ---------- Pipeline (opportunities) modal ---------- */

        function anyModalOpen() {
            return ($pipe && $pipe.hasClass('is-open')) || ($detail && $detail.hasClass('is-open'));
        }

        // An account row is backed by either a business partner or - for an
        // opportunity still attached to an unconverted lead - a C_Lead. Lead-backed
        // rows carry bpId 0, so the "open the customer record" actions are hidden;
        // there is no partner record to open.
        function openPipeline(bpId, leadId, name) {
            if (!bpId && !leadId) { return; }
            currentPipeId = bpId;
            currentPipeLeadId = leadId || 0;
            currentPipeName = name || '';
            $pipeCust.text(currentPipeName);
            $pipe.toggleClass('is-lead', !bpId);
            $pipe.addClass('is-open').attr('aria-hidden', 'false');
            $('body').addClass('vas139-modal-open');
            $pipeBody.html('<div class="vas139-state">' + escapeHtml(label('Loading', 'Loading…')) + '</div>');
            $.ajax({
                url: VIS.Application.contextUrl + 'VAS_139_HighValuePipelineWidget/GetOpportunities',
                type: 'GET', cache: false, data: { C_BPartner_ID: bpId || 0, C_Lead_ID: leadId || 0 },
                success: function (response) {
                    var data = parseResponse(response);
                    if (data && data.error) { $pipeBody.html('<div class="vas139-state">' + escapeHtml(label('VAS_139_UnableToLoad', 'Unable to load pipeline.')) + '</div>'); return; }
                    renderPipeline(data || {});
                },
                error: function () { $pipeBody.html('<div class="vas139-state">' + escapeHtml(label('VAS_139_UnableToLoad', 'Unable to load pipeline.')) + '</div>'); }
            });
        }
        function closePipeline() {
            if (!$pipe) { return; }
            if (document.activeElement && $pipe[0].contains(document.activeElement)) { document.activeElement.blur(); }
            $pipe.removeClass('is-open').attr('aria-hidden', 'true');
            if (!anyModalOpen()) { $('body').removeClass('vas139-modal-open'); }
        }

        function oppRowHtml(opp, cur) {
            var status = statusLabel(opp.statusCode);
            var close = formatDate(opp.closeDate);
            var metaParts = [];
            if (status) { metaParts.push(status); }
            if (close) { metaParts.push(label('VAS_139_LikelyClose', 'likely close') + ' ' + close); }
            var meta = metaParts.join(' · ');
            return '<div class="vas139-opp">' +
                '<span class="vas139-opp-ic">' + icon('trend') + '</span>' +
                '<span class="vas139-opp-main">' +
                    '<span class="vas139-opp-name" title="' + escapeHtml(opp.name) + '">' + escapeHtml(opp.name) + '</span>' +
                    (meta ? '<span class="vas139-opp-meta" title="' + escapeHtml(meta) + '">' + escapeHtml(meta) + '</span>' : '') +
                '</span>' +
                '<span class="vas139-opp-val">' + escapeHtml(formatMoney(opp.value, cur)) + '</span>' +
            '</div>';
        }

        function renderPipeline(data) {
            pipeCurrency = { symbol: data.currency_symbol || widgetCurrency.symbol, iso: data.currency_iso || widgetCurrency.iso, precision: data.std_precision };
            var opps = data.opportunities || [];
            var total = 0;
            for (var i = 0; i < opps.length; i++) { total += Number(opps[i].value || 0); }
            var name = currentPipeName;
            var count = opps.length;
            var oppWord = count === 1 ? label('VAS_139_OpenOpportunity', 'open opportunity') : label('VAS_139_OpenOpportunities', 'open opportunities');

            var identity = '<div class="vas139-id">' +
                '<span class="vas139-id-avatar" style="background:' + avatarColor(name) + '">' + escapeHtml(initials(name)) + '</span>' +
                '<div class="vas139-id-main"><div class="vas139-id-name" title="' + escapeHtml(name) + '">' + escapeHtml(name) + '</div>' +
                    '<div class="vas139-id-sub">' + formatCount(count) + ' ' + escapeHtml(oppWord) + ' · ' + escapeHtml(formatMoney(total, pipeCurrency)) + ' ' + escapeHtml(label('VAS_139_Total', 'total')) + '</div></div>' +
            '</div>';

            var rows = count
                ? opps.map(function (o) { return oppRowHtml(o, pipeCurrency); }).join('')
                : '<div class="vas139-state">' + escapeHtml(label('VAS_139_NoOpportunities', 'No open opportunities.')) + '</div>';

            var totalRow = count
                ? '<div class="vas139-opp vas139-opp-total">' +
                    '<span class="vas139-opp-main"><span class="vas139-opp-name">' + escapeHtml(label('VAS_139_TotalPipeline', 'Total pipeline')) + '</span></span>' +
                    '<span class="vas139-opp-val vas139-total-val">' + escapeHtml(formatMoney(total, pipeCurrency)) + '</span>' +
                '</div>'
                : '';

            $pipeBody.html(identity + '<div class="vas139-opps">' + rows + '</div>' + totalRow);
        }

        function zoomToCustomer(bpId) {
            if (!bpId) { return; }
            closeDetail();
            closePipeline();
            try {
                $self.widgetFirevalueChanged({ "TabWhereClause": "C_BPartner.C_BPartner_ID=" + Number(bpId), "TabLayout": "Y", "TabIndex": "0", "ActionName": CUSTOMER_WINDOW_NAME, "ActionType": "W" });
            } catch (e) { /* best-effort */ }
        }

        function createPipelineDialog() {
            $pipe = $(
                '<div class="vas139-pipe" role="dialog" aria-modal="true" aria-hidden="true" aria-label="' + escapeHtml(label('VAS_139_Pipeline', 'Pipeline')) + '">' +
                    '<div class="vas139-scrim" data-pipe-close></div>' +
                    '<section class="vas139-dpanel">' +
                        '<header class="vas139-phead"><h2 class="vas139-ptitle">' + escapeHtml(label('VAS_139_Pipeline', 'Pipeline')) + '</h2>' +
                            '<div class="vas139-phead-right"><span class="vas139-pcust"></span>' +
                                '<button type="button" class="vas139-close" data-pipe-close aria-label="' + escapeHtml(label('VAS_139_Close', 'Close')) + '">' + icon('close') + '</button></div></header>' +
                        '<div class="vas139-dbody"></div>' +
                        '<footer class="vas139-dfoot">' +
                            '<button type="button" class="vas139-btn vas139-btn-ghost" data-pipe-act="customer">' + escapeHtml(label('VAS_139_Customer', 'Customer')) + '</button>' +
                            '<button type="button" class="vas139-btn vas139-btn-primary" data-pipe-act="open">' + icon('arrow') + escapeHtml(label('VAS_139_OpenRecord', 'Open record')) + '</button>' +
                        '</footer>' +
                    '</section>' +
                '</div>'
            );
            $('body').append($pipe);
            $pipeBody = $pipe.find('.vas139-dbody');
            $pipeCust = $pipe.find('.vas139-pcust');
            $pipe.on('click', '[data-pipe-close]', closePipeline);
            // Both actions target a business-partner record; they are no-ops (and the
            // buttons are hidden via .is-lead) when the account is an unconverted lead.
            $pipe.on('click', '[data-pipe-act="open"]', function () { if (currentPipeId) { zoomToCustomer(currentPipeId); } });
            $pipe.on('click', '[data-pipe-act="customer"]', function () { if (currentPipeId) { openCustomer(currentPipeId, currentPipeName); } });
        }

        /* ---------- Customer detail modal (reuses VAS_126 endpoint) ---------- */

        function openCustomer(bpId, name) {
            if (!bpId) { return; }
            currentDetailId = bpId;
            currentDetailName = name || '';
            $detail.addClass('is-open').attr('aria-hidden', 'false');
            $('body').addClass('vas139-modal-open');
            $detailSummary.text('');
            $detailBody.html('<div class="vas139-state">' + escapeHtml(label('Loading', 'Loading…')) + '</div>');
            $.ajax({
                url: VIS.Application.contextUrl + CUSTOMER_ENDPOINT + 'GetCustomerDetail',
                type: 'GET', cache: false, data: { C_BPartner_ID: bpId },
                success: function (response) {
                    var data = parseResponse(response);
                    if (data && data.error) { $detailBody.html('<div class="vas139-state">' + escapeHtml(label('VAS_139_UnableToLoad', 'Unable to load pipeline.')) + '</div>'); return; }
                    renderCustomerDetail(data || {});
                },
                error: function () { $detailBody.html('<div class="vas139-state">' + escapeHtml(label('VAS_139_UnableToLoad', 'Unable to load pipeline.')) + '</div>'); }
            });
        }

        function fact(fallback, valueHtml) {
            return '<div class="vas139-fact"><div class="vas139-fl">' + escapeHtml(fallback) + '</div><div class="vas139-fv">' + valueHtml + '</div></div>';
        }
        function signalRow(iconName, color, titleHtml, detailText) {
            return '<div class="vas139-signal"><span class="vas139-sig-ic" style="color:' + color + ';background:' + color + '1f">' + icon(iconName) + '</span>' +
                '<div class="vas139-sig-main"><div class="vas139-sig-name">' + titleHtml + '</div><div class="vas139-sig-detail">' + escapeHtml(detailText) + '</div></div></div>';
        }

        function renderCustomerDetail(data) {
            detailCurrency = { symbol: data.currency_symbol || '', iso: data.currency_iso || '', precision: data.std_precision };
            var dash = '—';
            var name = data.name || currentDetailName || '';
            var sub = [data.contactName, data.contactEmail].filter(function (p) { return p; }).join(' · ');
            var tierLabel = data.tier || data.tierCode || '';
            var summaryParts = [];
            if (tierLabel) { summaryParts.push(tierLabel); }
            if (data.isKeyClient) { summaryParts.push(label('VAS_139_KeyClient', 'Key client')); }
            $detailSummary.text(summaryParts.join(' · '));

            var projects = Number(data.projects || 0), pipeline = Number(data.pipelineValue || 0);
            var facts = '<div class="vas139-factgrid">' +
                fact(label('VAS_139_Tier', 'Tier'), tierLabel ? escapeHtml(tierLabel) : dash) +
                fact(label('VAS_139_Segment', 'Segment'), escapeHtml(data.segment || dash)) +
                fact(label('VAS_139_Owner', 'Owner'), escapeHtml(data.rep || dash)) +
                fact(label('VAS_139_ARR', 'ARR'), escapeHtml(formatMoney(data.value, detailCurrency))) +
                fact(label('VAS_139_OpenTicketsFact', 'Open tickets'), escapeHtml(formatCount(data.openTickets || 0))) +
                fact(label('VAS_139_Projects', 'Projects'), projectText(data.projectName, projects, dash)) +
                fact(label('VAS_139_Pipeline', 'Pipeline'), pipeline > 0 ? escapeHtml(formatMoney(pipeline, detailCurrency)) : dash) +
                fact(label('VAS_139_Onboarding', 'Onboarding'), data.onboardingPercent == null ? dash : escapeHtml(formatCount(data.onboardingPercent) + '%')) +
            '</div>';

            var signals = '';
            var pipe = Number(data.pipelineValue || 0), pipeCount = Number(data.pipelineCount || 0);
            if (pipe > 0) {
                signals += signalRow('trend', '#0083DA', escapeHtml(formatMoney(pipe, detailCurrency) + ' ' + label('VAS_139_OpenPipelineSig', 'open pipeline')),
                    formatCount(pipeCount) + ' ' + (pipeCount === 1 ? label('VAS_139_OpenOpportunity', 'open opportunity') : label('VAS_139_OpenOpportunities', 'open opportunities')));
            }
            var overdue = Number(data.overdueAmount || 0);
            if (overdue > 0) {
                var invPart = data.overdueInvoice ? ' · ' + data.overdueInvoice : '';
                signals += signalRow('cash', '#ED1C24', escapeHtml(formatMoney(overdue, detailCurrency) + ' ' + label('VAS_139_Overdue', 'overdue') + invPart),
                    formatCount(data.overdueDays || 0) + ' ' + label('VAS_139_DaysPastDue', 'days past due'));
            }
            var openT = Number(data.openTickets || 0);
            if (openT > 0) {
                signals += signalRow('ticket', '#ED1C24', escapeHtml(formatCount(openT) + ' ' + label('VAS_139_OpenSupportTickets', 'open support tickets')),
                    data.isKeyClient ? label('VAS_139_KeyPrioritise', 'Key client — prioritise') : label('VAS_139_OpenRequests', 'Open support requests'));
            }
            var signalsBlock = signals ? '<div class="vas139-signals"><div class="vas139-sig-title">' + escapeHtml(label('VAS_139_Signals', 'Signals')) + '</div>' + signals + '</div>' : '';

            var identity = '<div class="vas139-id">' +
                '<span class="vas139-id-avatar" style="background:' + avatarColor(name) + '">' + escapeHtml(initials(name)) + '</span>' +
                '<div class="vas139-id-main"><div class="vas139-id-name" title="' + escapeHtml(name) + '">' + escapeHtml(name) + '</div>' +
                    '<div class="vas139-id-sub" title="' + escapeHtml(sub) + '">' + escapeHtml(sub) + '</div></div>' +
                (tierLabel ? '<div class="vas139-id-tier">' + tierTagHtml(data.tier || data.tierCode) + '</div>' : '') +
            '</div>';

            $detailBody.html(identity + facts + signalsBlock);
        }

        function closeDetail() {
            if (!$detail) { return; }
            if (document.activeElement && $detail[0].contains(document.activeElement)) { document.activeElement.blur(); }
            $detail.removeClass('is-open').attr('aria-hidden', 'true');
            if (!anyModalOpen()) { $('body').removeClass('vas139-modal-open'); }
        }

        function createDetailDialog() {
            $detail = $(
                '<div class="vas139-detail" role="dialog" aria-modal="true" aria-hidden="true" aria-label="' + escapeHtml(label('VAS_139_CustomerDetails', 'Customer details')) + '">' +
                    '<div class="vas139-scrim" data-detail-close></div>' +
                    '<section class="vas139-dpanel">' +
                        '<header class="vas139-phead"><h2 class="vas139-ptitle">' + escapeHtml(label('VAS_139_CustomerDetails', 'Customer details')) + '</h2>' +
                            '<div class="vas139-phead-right"><span class="vas139-dsummary"></span>' +
                                '<button type="button" class="vas139-close" data-detail-close aria-label="' + escapeHtml(label('VAS_139_Close', 'Close')) + '">' + icon('close') + '</button></div></header>' +
                        '<div class="vas139-dbody"></div>' +
                        '<footer class="vas139-dfoot">' +
                            '<button type="button" class="vas139-btn vas139-btn-primary" data-detail-act="open">' + icon('arrow') + escapeHtml(label('VAS_139_OpenRecord', 'Open record')) + '</button>' +
                        '</footer>' +
                    '</section>' +
                '</div>'
            );
            $('body').append($detail);
            $detailBody = $detail.find('.vas139-dbody');
            $detailSummary = $detail.find('.vas139-dsummary');
            $detail.on('click', '[data-detail-close]', closeDetail);
            $detail.on('click', '[data-detail-act="open"]', function () { var id = currentDetailId; closeDetail(); zoomToCustomer(id); });
        }

        /* ---------- Widget shell ---------- */

        function createWidget() {
            var $card = $(
                '<div class="vas139-card">' +
                    '<div class="vas139-head">' +
                        '<div class="vas139-head-l">' +
                            '<span class="vas139-iconwell">' + icon('trend') + '</span>' +
                            '<div class="vas139-head-txt"><div class="vas139-title">' + escapeHtml(label('VAS_139_HighValuePipeline', 'High-value pipeline')) + '</div>' +
                                '<div class="vas139-sub"></div></div>' +
                        '</div>' +
                    '</div>' +
                    '<div class="vas139-body"></div>' +
                '</div>'
            );
            $sub = $card.find('.vas139-sub');
            $body = $card.find('.vas139-body');

            $card.on('click', '.vas139-row', function () {
                openPipeline(Number($(this).attr('data-id')), Number($(this).attr('data-lead')), $(this).find('.vas139-row-title').text());
            });
            $card.on('click', '.vas139-pgbtn', function () { turnPage($(this).attr('data-dir')); });
            $card.on('click', '.vas139-retry', function () { loadRows(); });

            $root.append($card);
        }

        this.Initalize = function () {
            createWidget();
            createPipelineDialog();
            createDetailDialog();

            $(document).on('keydown.MPCvas139', function (event) {
                if (event.key !== 'Escape') { return; }
                if ($detail && $detail.hasClass('is-open')) { closeDetail(); }
                else if ($pipe && $pipe.hasClass('is-open')) { closePipeline(); }
            });

            loadRows();
        };

        this.refreshWidget = function () {
            pageOffset = 0;
            loadRows();
        };

        this.getRoot = function () { return $root; };

        this.disposeComponent = function () {
            $(document).off('keydown.MPCvas139');
            if ($pipe) { $pipe.remove(); $pipe = null; }
            if ($detail) { $detail.remove(); $detail = null; }
            $('body').removeClass('vas139-modal-open');
            $root.remove();
        };
    };

    VAS.VAS_139_HighValuePipelineWidget.prototype.widgetFirevalueChanged = function (value) {
        if (this.listener) { this.listener.widgetFirevalueChanged(value); }
    };
    VAS.VAS_139_HighValuePipelineWidget.prototype.addChangeListener = function (listener) {
        this.listener = listener;
    };
    VAS.VAS_139_HighValuePipelineWidget.prototype.init = function (windowNo, frame) {
        this.frame = frame;
        this.AD_UserHomeWidgetID = frame.widgetInfo.AD_UserHomeWidgetID;
        this.windowNo = windowNo;
        this.Initalize();
        this.frame.getContentGrid().append(this.getRoot());
        ensureDashInlineSizeVar(this.getRoot());
    };
    VAS.VAS_139_HighValuePipelineWidget.prototype.widgetSizeChange = function (height, width) { };
    VAS.VAS_139_HighValuePipelineWidget.prototype.refreshWidget = function () { this.refreshWidget(); };
    VAS.VAS_139_HighValuePipelineWidget.prototype.dispose = function () {
        this.disposeComponent();
        if (this.frame) { this.frame.dispose(); }
        this.frame = null;
    };

})(VAS, jQuery);
