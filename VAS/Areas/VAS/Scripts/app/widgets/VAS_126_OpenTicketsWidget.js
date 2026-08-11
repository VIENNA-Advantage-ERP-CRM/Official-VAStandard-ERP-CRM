/**
 * VAS_126 Open Tickets KPI Widget (Customers module dashboard)
 * Purpose - Clickable 2x1 KPI tile. Main value is the count of open support tickets
 *           (R_Request whose R_Status.IsOpen='Y' / IsClosed<>'Y'); the sub-line is
 *           the click affordance. Clicking (or Enter/Space) opens a paged triage
 *           list of customers with open tickets - key clients first, then by
 *           open-ticket count. Selecting a row zooms the host window to that
 *           C_BPartner record.
 * Design  - kpi-open-tickets.html (attached) + Design Specs/dashboard-widgets.md
 *           "KPI And Summary Widget". Reference kpiWidget clickable tile: muted
 *           label + tap arrow on top, big bold value, foot row (sub + "triage"
 *           CTA) at the bottom, on the standard glass surface - the spec forbids a
 *           semantic tint on the widget background, so urgency is carried by the
 *           "triage" CTA. Internal sizing in em against the widget-root clamp;
 *           borders/radii in px. CSS namespaced vas126-*.
 *
 * Backend - VAS_126_OpenTicketsWidget/GetOpenTickets       (KPI aggregate)
 *           VAS_126_OpenTicketsWidget/GetAffectedCustomers (paged triage list)
 *
 * ── Labels / Message Keys ─────────────────────────────────────────────────
 *  #  | Current Text                   | Message Key
 * ----+--------------------------------+--------------------------------
 *  1  | Open tickets                   | VAS_126_OpenTickets
 *  2  | click                          | VAS_126_Click
 *  5  | triage                         | VAS_126_Triage
 *  6  | Open triage list.              | VAS_126_OpenTriageList
 *  7  | Unable to load                 | VAS_126_UnableToLoad
 *  8  | Customers with open tickets    | VAS_126_CustomersWithOpenTickets
 *  9  | open                           | VAS_126_Open
 * 10  | Key client                     | VAS_126_KeyClient
 * 11  | Nothing here right now.        | VAS_126_NothingHere
 * 12  | of                             | VAS_126_Of
 * 12a | Showing                        | VAS_126_Showing
 * 13  | Close                          | VAS_126_Close
 * ──────────────────────────────────────────────────────────────────────────
 */
; VAS = window.VAS || {};

; (function (VAS, $) {

    // Host window to zoom when the widget is not hosted on the Customers window
    // itself (documented for admin confirmation; the resolved host name wins).
    var CUSTOMER_WINDOW_NAME = "Business Partner";

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

    VAS.VAS_126_OpenTicketsWidget = function () {

        this.frame;
        this.windowNo;

        var $self = this;
        var $root = $('<div class="vas126-root">');
        var $card;
        var $value;
        var $sub;
        var $dialog;
        var $dialogBody;
        var $dialogCount;
        var $dialogPager;
        var $detail;
        var $detailBody;
        var $detailSummary;
        var currentDetailId = 0;
        var currentDetailName = '';

        var lastCount = 0;
        var pageSize = 7;
        var pageOffset = 0;
        var listTotal = 0;
        var listLoading = false;
        var searchCurrency = { symbol: '', iso: '', precision: 0 };

        function label(key, fallback) {
            var translated = VIS.Msg.getMsg(key);
            return translated && translated.charAt(0) !== '[' ? translated : fallback;
        }

        function escapeHtml(value) {
            if (value == null) { return ''; }
            return String(value).replace(/[&<>"']/g, function (character) {
                return { '&': '&amp;', '<': '&lt;', '>': '&gt;', '"': '&quot;', "'": '&#39;' }[character];
            });
        }

        function icon(name) {
            if (name === 'arrow') {
                return '<svg viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2" stroke-linecap="round" stroke-linejoin="round" aria-hidden="true"><line x1="5" y1="12" x2="19" y2="12"></line><polyline points="12 5 19 12 12 19"></polyline></svg>';
            }
            if (name === 'trend') {
                return '<svg viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2" stroke-linecap="round" stroke-linejoin="round" aria-hidden="true"><polyline points="22 17 13.5 8.5 8.5 13.5 2 7"></polyline><polyline points="16 17 22 17 22 11"></polyline></svg>';
            }
            if (name === 'close') {
                return '<svg viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2" stroke-linecap="round" aria-hidden="true"><path d="M18 6 6 18"></path><path d="m6 6 12 12"></path></svg>';
            }
            if (name === 'chevL') {
                return '<svg viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2" stroke-linecap="round" stroke-linejoin="round" aria-hidden="true"><polyline points="15 18 9 12 15 6"></polyline></svg>';
            }
            if (name === 'chevR') {
                return '<svg viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2" stroke-linecap="round" stroke-linejoin="round" aria-hidden="true"><polyline points="9 18 15 12 9 6"></polyline></svg>';
            }
            if (name === 'ticket') {
                return '<svg viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2" stroke-linecap="round" stroke-linejoin="round" aria-hidden="true"><path d="M3 9a2 2 0 0 1 2-2h14a2 2 0 0 1 2 2v1a2 2 0 0 0 0 4v1a2 2 0 0 1-2 2H5a2 2 0 0 1-2-2v-1a2 2 0 0 0 0-4z"></path><line x1="9" y1="7" x2="9" y2="17"></line></svg>';
            }
            if (name === 'cash') {
                return '<svg viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2" stroke-linecap="round" stroke-linejoin="round" aria-hidden="true"><rect x="2" y="6" width="20" height="12" rx="2"></rect><circle cx="12" cy="12" r="2"></circle><path d="M6 12h.01M18 12h.01"></path></svg>';
            }
            if (name === 'phone') {
                return '<svg viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2" stroke-linecap="round" stroke-linejoin="round" aria-hidden="true"><path d="M22 16.92v3a2 2 0 0 1-2.18 2 19.79 19.79 0 0 1-8.63-3.07 19.5 19.5 0 0 1-6-6 19.79 19.79 0 0 1-3.07-8.67A2 2 0 0 1 4.11 2h3a2 2 0 0 1 2 1.72c.13.96.36 1.9.7 2.81a2 2 0 0 1-.45 2.11L8.09 9.91a16 16 0 0 0 6 6l1.27-1.27a2 2 0 0 1 2.11-.45c.91.34 1.85.57 2.81.7A2 2 0 0 1 22 16.92z"></path></svg>';
            }
            if (name === 'check') {
                return '<svg viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2.2" stroke-linecap="round" stroke-linejoin="round" aria-hidden="true"><polyline points="20 6 9 17 4 12"></polyline></svg>';
            }
            return '';
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

        // Deterministic avatar tint from the company name (matches VAS_120).
        var AVATAR_COLORS = ['#1F83FF', '#5F4AA6', '#0B6B45', '#D78B10', '#0083DA', '#A33F3F'];
        function avatarColor(text) {
            var hash = 0;
            var value = String(text || '');
            for (var i = 0; i < value.length; i++) {
                hash = (hash * 31 + value.charCodeAt(i)) % AVATAR_COLORS.length;
            }
            return AVATAR_COLORS[hash];
        }
        function initials(name) {
            return String(name || '').split(' ').slice(0, 2)
                .map(function (word) { return word.charAt(0); }).join('').toUpperCase();
        }

        function tierClass(tier) {
            if (tier === 'Platinum') { return 'vas126-tag-violet'; }
            if (tier === 'Gold') { return 'vas126-tag-amber'; }
            if (tier === 'Silver') { return 'vas126-tag-info'; }
            return 'vas126-tag-neutral';
        }

        // Tier tag: mapped tier -> tier-coloured tag; raw Rating code -> neutral tag;
        // nothing -> empty (never a guessed tier name).
        function tierTagHtml(tier, tierCode) {
            if (tier) { return '<span class="vas126-tag ' + tierClass(tier) + '">' + escapeHtml(tier) + '</span>'; }
            if (tierCode) { return '<span class="vas126-tag vas126-tag-neutral">' + escapeHtml(tierCode) + '</span>'; }
            return '';
        }

        // Standard precision of the base currency reported by the endpoint, falling
        // back to the session context.
        function currencyPrecision() {
            var p = Number(searchCurrency.precision);
            if (!isNaN(p) && p >= 0) { return p; }
            if (VIS.Env && VIS.Env.getCtx && VIS.Env.getCtx().getStdPrecision) {
                p = Number(VIS.Env.getCtx().getStdPrecision());
            }
            return !isNaN(p) && p >= 0 ? p : 0;
        }

        // Compact "ARR"/value in the base (accounting-schema) currency. The magnitude
        // comes from VIS.Util.formatCompactAmount, so the scale follows the base
        // currency's numbering system (K/M/B or K/L/Cr) at the configured precision.
        function formatArr(value) {
            var n = Number(value || 0);
            if (!isFinite(n)) { n = 0; }
            var sign = n < 0 ? '-' : '';
            var symbol = searchCurrency.symbol || searchCurrency.iso || '';
            return sign + symbol + VIS.Util.formatCompactAmount(n, searchCurrency.iso, currencyPrecision());
        }
        function formatArrFull(value) {
            var n = Number(value || 0);
            if (!isFinite(n)) { n = 0; }
            var sign = n < 0 ? '-' : '';
            var abs = Math.abs(n);
            var symbol = searchCurrency.symbol || searchCurrency.iso || '';
            var precision = currencyPrecision();
            var formatted = abs.toLocaleString(window.navigator.language, {
                minimumFractionDigits: precision, maximumFractionDigits: precision
            });
            return sign + (symbol ? symbol + ' ' + formatted : formatted);
        }

        // Segment = the marketing target list(s) the customer belongs to. The server
        // sends the first one plus how many there are in total, so a customer in
        // several lists reads "West Region Prospect +2" instead of hiding the rest.
        // No membership at all renders the same em dash the other empty cells use.
        function segmentText(name, count) {
            if (!name) { return '—'; }
            var extra = Number(count || 0) - 1;
            return extra > 0 ? name + ' +' + formatCount(extra) : name;
        }

        // Sub-line is the click affordance only. The "N key clients affected" figure
        // was removed on request; key-client classification stays configuration-driven
        // and still tags rows inside the triage list.
        function subText() {
            return label('VAS_126_Click', 'click');
        }

        /* ---------- KPI ---------- */

        function loadKpi() {
            $value.text('—');
            $sub.text('…');
            setCardLabel(label('VAS_126_OpenTickets', 'Open tickets'));

            $.ajax({
                url: VIS.Application.contextUrl + 'VAS_126_OpenTicketsWidget/GetOpenTickets',
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
            lastCount = Number(data.open_ticket_count || 0);

            $value.text(formatCount(lastCount)).attr('title', formatCount(lastCount));
            $sub.text(subText());
            setCardLabel(label('VAS_126_OpenTickets', 'Open tickets') + ': ' + formatCount(lastCount) + '. ' + label('VAS_126_OpenTriageList', 'Open triage list.'));
        }

        function showError() {
            lastCount = 0;
            $value.text('—').removeAttr('title');
            $sub.text(label('VAS_126_UnableToLoad', 'Unable to load'));
            setCardLabel(label('VAS_126_UnableToLoad', 'Unable to load'));
        }

        function setCardLabel(text) {
            if ($card) { $card.attr('aria-label', text); }
        }

        /* ---------- Triage dialog ---------- */

        function openTriage() {
            pageOffset = 0;
            $dialog.addClass('is-open').attr('aria-hidden', 'false');
            $('body').addClass('vas126-modal-open');
            loadList();
        }

        function closeTriage() {
            if (!$dialog) { return; }
            if (document.activeElement && $dialog[0].contains(document.activeElement)) {
                if ($card && $card.length) { $card.focus(); } else { document.activeElement.blur(); }
            }
            $dialog.removeClass('is-open').attr('aria-hidden', 'true');
            $('body').removeClass('vas126-modal-open');
        }

        function loadList() {
            listLoading = true;
            renderListState(label('Loading', 'Loading…'));
            $dialogPager.empty();

            $.ajax({
                url: VIS.Application.contextUrl + 'VAS_126_OpenTicketsWidget/GetAffectedCustomers',
                type: 'GET',
                cache: false,
                data: { offset: pageOffset, limit: pageSize },
                success: function (response) {
                    listLoading = false;
                    var data = parseResponse(response);
                    if (data && data.error) { renderListState(label('VAS_126_UnableToLoad', 'Unable to load')); return; }
                    listTotal = Number(data.total || 0);
                    searchCurrency.symbol = data.currency_symbol || '';
                    searchCurrency.iso = data.currency_iso || '';
                    searchCurrency.precision = data.std_precision;
                    updateCount(listTotal);
                    renderList(data.items || []);
                },
                error: function () {
                    listLoading = false;
                    renderListState(label('VAS_126_UnableToLoad', 'Unable to load'));
                }
            });
        }

        function updateCount(total) {
            if (!$dialogCount) { return; }
            var word = total === 1 ? label('VAS_126_Customer', 'customer') : label('VAS_126_Customers', 'customers');
            $dialogCount.text(formatCount(total) + ' ' + word);
        }

        function renderListState(message) {
            $dialogBody.html('<div class="vas126-list-state">' + escapeHtml(message) + '</div>');
        }

        function renderList(items) {
            if (!items.length) {
                renderListState(label('VAS_126_NothingHere', 'Nothing here right now.'));
                $dialogPager.empty();
                return;
            }

            var head = '<div class="vas126-grid-head">' +
                '<span>' + escapeHtml(label('VAS_126_Customer', 'Customer')) + '</span>' +
                '<span>' + escapeHtml(label('VAS_126_Tier', 'Tier')) + '</span>' +
                '<span>' + escapeHtml(label('VAS_126_Owner', 'Owner')) + '</span>' +
                '<span class="is-right">' + escapeHtml(label('VAS_126_ARR', 'ARR')) + '</span>' +
                '<span class="is-right">' + escapeHtml(label('VAS_126_Segment', 'Segment')) + '</span>' +
            '</div>';

            // Bare em dash for "no owner on this customer". Deliberately NOT read from
            // the message dictionary: AD_Message.VAS_126_NoOwner is stored double-
            // encoded in this database and renders as "a EUR ..." mojibake, and a
            // punctuation placeholder carries nothing to translate anyway.
            var noOwner = '—';
            var rows = items.map(function (customer) {
                var name = customer.customerName || '';
                var contact = customer.contact || '';
                var owner = customer.rep || noOwner;
                var segment = segmentText(customer.segment, customer.segmentCount);
                var arr = formatArr(customer.value);
                return '<button type="button" class="vas126-grid-row" data-id="' + Number(customer.customerId) + '">' +
                    '<span class="vas126-c-cust">' +
                        '<span class="vas126-avatar" style="background:' + avatarColor(name) + '">' + escapeHtml(initials(name)) + '</span>' +
                        '<span class="vas126-c-cust-main">' +
                            '<span class="vas126-c-name" title="' + escapeHtml(name) + '">' + escapeHtml(name) + '</span>' +
                            '<span class="vas126-c-contact" title="' + escapeHtml(contact) + '">' + escapeHtml(contact) + '</span>' +
                        '</span>' +
                    '</span>' +
                    '<span class="vas126-c-tier">' + tierTagHtml(customer.tier, customer.tierCode) + '</span>' +
                    '<span class="vas126-c-owner" title="' + escapeHtml(owner) + '">' + escapeHtml(owner) + '</span>' +
                    '<span class="vas126-c-arr is-right" title="' + escapeHtml(formatArrFull(customer.value)) + '">' + escapeHtml(arr) + '</span>' +
                    '<span class="vas126-c-seg is-right" title="' + escapeHtml(segment) + '">' + escapeHtml(segment) + '</span>' +
                '</button>';
            }).join('');

            $dialogBody.html('<div class="vas126-grid">' + head + '<div class="vas126-grid-body">' + rows + '</div></div>');
            renderPager(items.length);
        }

        function renderPager(pageItemCount) {
            var start = pageOffset + 1;
            var end = pageOffset + pageItemCount;
            var pages = Math.max(1, Math.ceil(listTotal / pageSize));
            var currentPage = Math.floor(pageOffset / pageSize);

            /* Footer pager (dashboard-widgets.md §"Widget Footer Pager"): helper
               left, compact prev · "N of M" · next right. */
            var of = label('VAS_126_Of', 'of');
            $dialogPager.html(
                '<span class="vas126-pglabel">' + escapeHtml(label('VAS_126_Showing', 'Showing') + ' ' + start + '–' + end + ' ' + of + ' ' + formatCount(listTotal)) + '</span>' +
                '<span class="vas126-pgctl">' +
                    '<button type="button" class="vas126-pgbtn" data-dir="prev" ' + (currentPage <= 0 ? 'disabled' : '') + ' aria-label="' + escapeHtml(label('VAS_126_PrevPage', 'Previous')) + '">' + icon('chevL') + '</button>' +
                    '<span class="vas126-pgtext">' + escapeHtml((currentPage + 1) + ' ' + of + ' ' + pages) + '</span>' +
                    '<button type="button" class="vas126-pgbtn" data-dir="next" ' + (currentPage >= pages - 1 ? 'disabled' : '') + ' aria-label="' + escapeHtml(label('VAS_126_NextPage', 'Next')) + '">' + icon('chevR') + '</button>' +
                '</span>'
            );
        }

        function turnPage(direction) {
            if (listLoading) { return; }
            var next = pageOffset + (direction === 'next' ? pageSize : -pageSize);
            if (next < 0) { next = 0; }
            if (next >= listTotal) { return; }
            pageOffset = next;
            loadList();
        }

        // "Open in browser": open the host Customers window so the user can work the
        // full list in the app (best-effort; documented for admin confirmation).
        function openInBrowser() {
            closeTriage();
            try {
                $self.widgetFirevalueChanged({
                    "TabLayout": "N",
                    "TabIndex": "0",
                    "ActionName": hostWindowName() || CUSTOMER_WINDOW_NAME,
                    "ActionType": "W"
                });
            } catch (e) { /* best-effort */ }
        }

        // Resolve the host window name so the framework zooms the current grid in
        // place (no new window) when the widget sits on the Customers window.
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

        function zoomToCustomer(bpId) {
            if (!bpId) { return; }
            closeTriage();
            try {
                $self.widgetFirevalueChanged({
                    "TabWhereClause": "C_BPartner.C_BPartner_ID=" + Number(bpId),
                    "TabLayout": "Y",
                    "TabIndex": "0",
                    "ActionName": hostWindowName() || CUSTOMER_WINDOW_NAME,
                    "ActionType": "W"
                });
            } catch (e) { /* zoom is best-effort */ }
        }

        /* ---------- Customer detail modal ---------- */

        function openCustomerDetail(bpId) {
            if (!bpId) { return; }
            currentDetailId = bpId;
            $detail.addClass('is-open').attr('aria-hidden', 'false');
            $('body').addClass('vas126-modal-open');
            $detailSummary.text('');
            $detailBody.html('<div class="vas126-list-state">' + escapeHtml(label('Loading', 'Loading…')) + '</div>');

            $.ajax({
                url: VIS.Application.contextUrl + 'VAS_126_OpenTicketsWidget/GetCustomerDetail',
                type: 'GET',
                cache: false,
                data: { C_BPartner_ID: bpId },
                success: function (response) {
                    var data = parseResponse(response);
                    if (data && data.error) { $detailBody.html('<div class="vas126-list-state">' + escapeHtml(label('VAS_126_UnableToLoad', 'Unable to load')) + '</div>'); return; }
                    renderCustomerDetail(data || {});
                },
                error: function () {
                    $detailBody.html('<div class="vas126-list-state">' + escapeHtml(label('VAS_126_UnableToLoad', 'Unable to load')) + '</div>');
                }
            });
        }

        function fact(labelKey, fallback, valueHtml) {
            return '<div class="vas126-fact">' +
                '<div class="vas126-fl">' + escapeHtml(label(labelKey, fallback)) + '</div>' +
                '<div class="vas126-fv">' + valueHtml + '</div>' +
            '</div>';
        }

        function signalRow(iconName, color, titleHtml, detailText) {
            return '<div class="vas126-signal">' +
                '<span class="vas126-sig-ic" style="color:' + color + ';background:' + color + '1f">' + icon(iconName) + '</span>' +
                '<div class="vas126-sig-main">' +
                    '<div class="vas126-sig-name">' + titleHtml + '</div>' +
                    '<div class="vas126-sig-detail">' + escapeHtml(detailText) + '</div>' +
                '</div>' +
            '</div>';
        }

        function renderCustomerDetail(data) {
            searchCurrency.symbol = data.currency_symbol || '';
            searchCurrency.iso = data.currency_iso || '';
            searchCurrency.precision = data.std_precision;

            var dash = '—';
            var name = data.name || '';
            currentDetailName = name;
            var sub = [data.contactName, data.contactTitle, data.contactEmail]
                .filter(function (part) { return part; }).join(' · ');
            var tierLabel = data.tier || data.tierCode || '';

            var summaryParts = [];
            if (tierLabel) { summaryParts.push(tierLabel); }
            if (data.isKeyClient) { summaryParts.push(label('VAS_126_KeyClient', 'Key client')); }
            $detailSummary.text(summaryParts.join(' · '));

            var tierFactVal = tierLabel ? escapeHtml(tierLabel) : dash;
            var projects = Number(data.projects || 0);
            var pipeline = Number(data.pipelineValue || 0);

            var facts = '<div class="vas126-factgrid">' +
                fact('VAS_126_Tier', 'Tier', tierFactVal) +
                fact('VAS_126_Segment', 'Segment', escapeHtml(data.segment || dash)) +
                fact('VAS_126_Owner', 'Owner', escapeHtml(data.rep || dash)) +
                fact('VAS_126_ARR', 'ARR', escapeHtml(formatArr(data.value))) +
                fact('VAS_126_OpenTicketsFact', 'Open tickets', escapeHtml(formatCount(data.openTickets || 0))) +
                fact('VAS_126_Projects', 'Projects', projectText(data.projectName, projects, dash)) +
                fact('VAS_126_Pipeline', 'Pipeline', pipeline > 0 ? escapeHtml(formatArr(pipeline)) : dash) +
                fact('VAS_126_Onboarding', 'Onboarding', data.onboardingPercent == null ? dash : escapeHtml(formatCount(data.onboardingPercent) + '%')) +
            '</div>';

            var signals = '';
            var openT = Number(data.openTickets || 0);
            if (openT > 0) {
                signals += signalRow('ticket', '#ED1C24',
                    escapeHtml(formatCount(openT) + ' ' + label('VAS_126_OpenSupportTickets', 'open support tickets')),
                    data.isKeyClient ? label('VAS_126_KeyPrioritise', 'Key client — prioritise') : label('VAS_126_OpenRequests', 'Open support requests'));
            }
            var overdue = Number(data.overdueAmount || 0);
            if (overdue > 0) {
                var invPart = data.overdueInvoice ? ' · ' + data.overdueInvoice : '';
                signals += signalRow('cash', '#ED1C24',
                    escapeHtml(formatArr(overdue) + ' ' + label('VAS_126_Overdue', 'overdue') + invPart),
                    formatCount(data.overdueDays || 0) + ' ' + label('VAS_126_DaysPastDue', 'days past due'));
            }
            var signalsBlock = signals
                ? '<div class="vas126-signals"><div class="vas126-sig-title">' + escapeHtml(label('VAS_126_Signals', 'Signals')) + '</div>' + signals + '</div>'
                : '';

            var identity = '<div class="vas126-id">' +
                '<span class="vas126-id-avatar" style="background:' + avatarColor(name) + '">' + escapeHtml(initials(name)) + '</span>' +
                '<div class="vas126-id-main">' +
                    '<div class="vas126-id-name" title="' + escapeHtml(name) + '">' + escapeHtml(name) + '</div>' +
                    '<div class="vas126-id-sub" title="' + escapeHtml(sub) + '">' + escapeHtml(sub) + '</div>' +
                '</div>' +
                (tierLabel ? '<div class="vas126-id-tier">' + tierTagHtml(data.tier, data.tierCode) + '</div>' : '') +
            '</div>';

            $detailBody.html(identity + facts + signalsBlock);
        }

        function closeDetail() {
            if (!$detail) { return; }
            if (document.activeElement && $detail[0].contains(document.activeElement)) { document.activeElement.blur(); }
            $detail.removeClass('is-open').attr('aria-hidden', 'true');
            if (!$dialog || !$dialog.hasClass('is-open')) { $('body').removeClass('vas126-modal-open'); }
        }

        function createDetailDialog() {
            $detail = $(
                '<div class="vas126-detail" role="dialog" aria-modal="true" aria-hidden="true" aria-label="' + escapeHtml(label('VAS_126_CustomerDetails', 'Customer details')) + '">' +
                    '<div class="vas126-scrim" data-detail-close></div>' +
                    '<section class="vas126-dpanel">' +
                        '<header class="vas126-dhead">' +
                            '<h2 class="vas126-panel-title">' + escapeHtml(label('VAS_126_CustomerDetails', 'Customer details')) + '</h2>' +
                            '<div class="vas126-dhead-right">' +
                                '<span class="vas126-dsummary"></span>' +
                                '<button type="button" class="vas126-close" data-detail-close aria-label="' + escapeHtml(label('VAS_126_Close', 'Close')) + '">' + icon('close') + '</button>' +
                            '</div>' +
                        '</header>' +
                        '<div class="vas126-dbody"></div>' +
                        '<footer class="vas126-dfoot">' +
                            '<button type="button" class="vas126-btn vas126-btn-primary" data-detail-act="open">' + icon('arrow') + escapeHtml(label('VAS_126_OpenRecord', 'Open record')) + '</button>' +
                        '</footer>' +
                    '</section>' +
                '</div>'
            );
            $('body').append($detail);
            $detailBody = $detail.find('.vas126-dbody');
            $detailSummary = $detail.find('.vas126-dsummary');

            $detail.on('click', '[data-detail-close]', closeDetail);
            $detail.on('click', '[data-detail-act="open"]', function () {
                var id = currentDetailId;
                closeDetail();
                zoomToCustomer(id);
            });
        }

        function createDialog() {
            $dialog = $(
                '<div class="vas126-dialog" role="dialog" aria-modal="true" aria-hidden="true" aria-label="' + escapeHtml(label('VAS_126_CustomersWithOpenTickets', 'Customers with open tickets')) + '">' +
                    '<div class="vas126-scrim"></div>' +
                    '<section class="vas126-panel">' +
                        '<header class="vas126-panel-head">' +
                            '<h2 class="vas126-panel-title">' + escapeHtml(label('VAS_126_CustomersWithOpenTickets', 'Customers with open tickets')) + '</h2>' +
                            '<div class="vas126-head-right">' +
                                '<span class="vas126-count"></span>' +
                                '<button type="button" class="vas126-close" aria-label="' + escapeHtml(label('VAS_126_Close', 'Close')) + '">' + icon('close') + '</button>' +
                            '</div>' +
                        '</header>' +
                        '<div class="vas126-panel-body"></div>' +
                        '<footer class="vas126-panel-foot">' +
                            '<div class="vas126-pager"></div>' +
                            '<div class="vas126-foot-actions">' +
                                '<button type="button" class="vas126-btn vas126-btn-ghost" data-act="close">' + escapeHtml(label('VAS_126_Close', 'Close')) + '</button>' +
                                '<button type="button" class="vas126-btn vas126-btn-primary" data-act="open">' + icon('arrow') + escapeHtml(label('VAS_126_OpenInBrowser', 'Open in browser')) + '</button>' +
                            '</div>' +
                        '</footer>' +
                    '</section>' +
                '</div>'
            );
            $('body').append($dialog);
            $dialogBody = $dialog.find('.vas126-panel-body');
            $dialogCount = $dialog.find('.vas126-count');
            $dialogPager = $dialog.find('.vas126-pager');

            $dialog.on('click', '.vas126-close, .vas126-scrim, [data-act="close"]', closeTriage);
            $dialog.on('click', '[data-act="open"]', openInBrowser);
            $dialog.on('click', '.vas126-grid-row', function () {
                openCustomerDetail(Number($(this).attr('data-id')));
            });
            $dialog.on('click', '.vas126-pgbtn', function () {
                turnPage($(this).attr('data-dir'));
            });
        }

        this.Initalize = function () {
            $card = $(
                '<div class="vas126-card" role="button" tabindex="0" aria-live="polite">' +
                    '<div class="vas126-label">' + escapeHtml(label('VAS_126_OpenTickets', 'Open tickets')) +
                        '<span class="vas126-tap">' + icon('arrow') + '</span>' +
                    '</div>' +
                    '<div class="vas126-group">' +
                        '<div class="vas126-value">—</div>' +
                        '<div class="vas126-foot">' +
                            '<span class="vas126-sub"></span>' +
                            '<span class="vas126-cta">' + icon('trend') + escapeHtml(label('VAS_126_Triage', 'triage')) + '</span>' +
                        '</div>' +
                    '</div>' +
                '</div>'
            );

            $value = $card.find('.vas126-value');
            $sub = $card.find('.vas126-sub');

            $card.on('click', openTriage);
            $card.on('keydown', function (event) {
                if (event.key === 'Enter' || event.key === ' ' || event.key === 'Spacebar') {
                    event.preventDefault();
                    openTriage();
                }
            });

            $root.append($card);
            createDialog();
            createDetailDialog();

            $(document).on('keydown.MPCvas126', function (event) {
                if (event.key !== 'Escape') { return; }
                else if ($detail && $detail.hasClass('is-open')) { closeDetail(); }
                else if ($dialog.hasClass('is-open')) { closeTriage(); }
            });

            loadKpi();
        };

        this.refreshWidget = function () {
            loadKpi();
        };

        this.getRoot = function () { return $root; };

        this.disposeComponent = function () {
            $(document).off('keydown.MPCvas126');
            if ($dialog) { $dialog.remove(); $dialog = null; }
            if ($detail) { $detail.remove(); $detail = null; }
            $('body').removeClass('vas126-modal-open');
            $root.remove();
        };
    };

    /* Relay a fired value (zoom params) to the registered host. */
    VAS.VAS_126_OpenTicketsWidget.prototype.widgetFirevalueChanged = function (value) {
        if (this.listener) { this.listener.widgetFirevalueChanged(value); }
    };

    VAS.VAS_126_OpenTicketsWidget.prototype.addChangeListener = function (listener) {
        this.listener = listener;
    };

    VAS.VAS_126_OpenTicketsWidget.prototype.init = function (windowNo, frame) {
        this.frame = frame;
        this.AD_UserHomeWidgetID = frame.widgetInfo.AD_UserHomeWidgetID;
        this.windowNo = windowNo;
        this.Initalize();
        this.frame.getContentGrid().append(this.getRoot());
        ensureDashInlineSizeVar(this.getRoot());
    };

    VAS.VAS_126_OpenTicketsWidget.prototype.widgetSizeChange = function (height, width) { };

    VAS.VAS_126_OpenTicketsWidget.prototype.refreshWidget = function () {
        this.refreshWidget();
    };

    VAS.VAS_126_OpenTicketsWidget.prototype.dispose = function () {
        this.disposeComponent();
        if (this.frame) { this.frame.dispose(); }
        this.frame = null;
    };

})(VAS, jQuery);
