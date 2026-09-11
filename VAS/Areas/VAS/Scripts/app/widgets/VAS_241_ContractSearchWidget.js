/**
 * VAS_241 Contract search widget (Service Contracts module dashboard)
 * Purpose - Full-width 9x1 glass finder over the whole accessible C_Contract
 *           portfolio (any lifecycle state, not live-only). Type-ahead matches
 *           DocumentNo, customer, product and the DECODED DocStatus / ContractType
 *           labels (never a raw code - the backend resolves those through the
 *           tenant's own AD_Ref_List/AD_Ref_List_Trl dictionary). Shows at most
 *           seven hits with a days-to-end pill, a status band chip and an
 *           Open-record quick action; "See all N matches" opens the same query
 *           paged seven at a time. A hit opens a detail modal: header (customer,
 *           contact, rep, billing frequency/cycles), a value/status/type/renewal/
 *           ends/notice-days/billed/unbilled stat grid, a "What needs attention"
 *           panel (billing overdue + the customer's open tickets) and the full
 *           C_ContractSchedule billing schedule.
 * Design  - service-contracts-dashboard.html, attached after the initial build,
 *           is the detail modal's pixel reference; the search band/dropdown still
 *           reuse the sibling full-width search widgets' tokens (VAS_120/140/144)
 *           per Design Specs/dashboard-widgets.md "Full-Width Dashboard Search
 *           Widget" since that file was not available for the band itself.
 * Scope   - "Renew" and "Generate invoice" run the REAL C_Contract.RenewContract /
 *           GenerateInvoice button processes (RenewContract / CreateContractInvoice)
 *           through the standard process engine - a confirm step gates both since
 *           they mutate real documents (a new invoice, an extended contract). The
 *           "N open tickets" figure is scoped to the contract's CUSTOMER
 *           (C_BPartner_ID), not the contract itself - R_Request carries no
 *           C_Contract_ID in this schema - and there is no assets/entitlement
 *           count since no table backs one for a contract in this codebase.
 *
 * Backend - VAS_241_ContractSearchWidget/SearchContracts     (dropdown: q -> rows[≤7] + total)
 *           VAS_241_ContractSearchWidget/GetContracts        (paged "See all": q, offset -> rows[≤7] + total)
 *           VAS_241_ContractSearchWidget/GetContract         (detail modal: id -> contract + windowId)
 *           VAS_241_ContractSearchWidget/RunRenew             (POST id -> { Success, Message })
 *           VAS_241_ContractSearchWidget/RunGenerateInvoice   (POST id -> { Success, Message })
 *
 * Routing - Prompt_Instructions "Scenario 1 / Scenario 2": hosted on the Service
 *           Contracts window itself, opening a hit navigates that host grid in
 *           place (widgetFirevalueChanged); everywhere else (Home / landing
 *           dashboard, or hosted on a different window) it zooms straight to the
 *           standard Service Contract window (Export_ID VAS_1000262 /
 *           AD_Window_ID 1000248) via VAS.ZoomUtil.zoomToRecord, which — given an
 *           AD_Window_ID it already knows — opens it directly, no window-name
 *           lookup round-trip.
 *
 * ── Labels / Message Keys ─────────────────────────────────────────────────
 *  #  | Current Text                                              | Message Key
 * ----+-----------------------------------------------------------+---------------------------------
 *  1  | Search N contracts by no., customer, product or status…  | VAS_241_SearchPlaceholder
 *  2  | Search contracts by no., customer, product or status…    | VAS_241_SearchPlaceholderNoCount
 *  3  | Searching…                                                | VAS_241_Searching
 *  4  | No contracts match                                        | VAS_241_NoMatches
 *  5  | Couldn't search.                                          | VAS_241_LoadError
 *  6  | See all                                                   | VAS_241_SeeAll
 *  7  | matches                                                   | VAS_241_Matches
 *  8  | Open record                                               | VAS_241_OpenRecord
 *  9  | Ended                                                     | VAS_241_Ended
 * 10  | d to end                                                  | VAS_241_DaysToEnd
 * 11  | Close                                                     | VAS_241_Close
 * 12  | Contract Search Results                                   | VAS_241_ListTitle
 * 13  | No contracts.                                             | VAS_241_NoContracts
 * 14  | Unable to load contracts.                                 | VAS_241_UnableToLoad
 * 15  | Retry                                                     | VAS_241_Retry
 * 16  | Showing                                                   | VAS_241_Showing
 * 17  | of                                                        | VAS_241_Of
 * 18  | Previous page                                             | VAS_241_PrevPage
 * 19  | Next page                                                 | VAS_241_NextPage
 * 20  | Service Contract                                          | VAS_241_ContractTitle
 * 21  | Customer                                                  | VAS_241_Customer
 * 22  | Product                                                   | VAS_241_Product
 * 23  | Contract cycle value                                      | VAS_241_Value
 * 24  | Status                                                    | VAS_241_Status
 * 25  | Type                                                      | VAS_241_Type
 * 26  | Renewal                                                   | VAS_241_Renewal
 * 27  | Ends                                                       | VAS_241_Ends
 * 28  | Couldn't load this contract.                              | VAS_241_DetailLoadError
 * 29  | Ended {n}d ago                                            | VAS_241_EndedAgo
 * 30  | in {n}d                                                   | VAS_241_EndsIn
 * 31  | Notice days                                               | VAS_241_NoticeDays
 * 32  | Billed amount                                             | VAS_241_Billed
 * 33  | Unbilled amount                                           | VAS_241_Unbilled
 * 34  | cycles                                                    | VAS_241_Cycles
 * 35  | rep                                                       | VAS_241_Rep
 * 36  | What needs attention                                      | VAS_241_NeedsAttention
 * 37  | Billing overdue                                           | VAS_241_BillingOverdue
 * 38  | Next period {n}d overdue                                  | VAS_241_NextPeriodOverdue
 * 39  | unbilled total                                            | VAS_241_UnbilledTotal
 * 40  | open tickets for this customer                            | VAS_241_OpenTickets
 * 41  | Billing schedule                                          | VAS_241_BillingSchedule
 * 42  | Period                                                    | VAS_241_Period
 * 43  | Invoiced                                                  | VAS_241_Invoiced
 * 44  | Unbilled                                                  | VAS_241_ScheduleUnbilled
 * 45  | Generate invoice                                          | VAS_241_GenerateInvoice
 * 46  | Renew                                                     | VAS_241_Renew
 * 47  | Run RenewContract for this contract now?                  | VAS_241_ConfirmRenew
 * 48  | Generate invoices for the overdue billing periods now?    | VAS_241_ConfirmGenerateInvoice
 * 49  | Working…                                                  | VAS_241_Working
 * 50  | The action failed.                                        | VAS_241_ActionFailed
 * ──────────────────────────────────────────────────────────────────────────
 */
; VAS = window.VAS || {};

; (function (VAS, $) {

    // Zoom target: Service Contract window (Export_ID VAS_1000262 / AD_Window
    // 1000248), given directly by the build spec. VAS.ZoomUtil.zoomToRecord
    // zooms straight away when it is handed an AD_Window_ID > 0 - no window-name
    // lookup round-trip is needed the way the Home-page fallback in VAS_120/140
    // requires for widgets that only know a display name.
    var ZOOM_WINDOW_ID = 1000248;
    var ZOOM_TABLE = 'C_Contract';

    // Fallback ActionName for the in-window (Scenario 1) navigation when the host
    // window's own name cannot be resolved. Documented for admin confirmation,
    // same convention as VAS_120's CUSTOMER_WINDOW_NAME.
    var CONTRACT_WINDOW_NAME = 'Service Contract';

    var WIDGET_ENDPOINT = 'VAS_241_ContractSearchWidget/';
    var LIST_PAGE = 7;

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

    // Safe highlight: escape first, wrap only the matched slice in <mark>. Query
    // text never reaches innerHTML unescaped.
    function highlight(text, term) {
        var raw = String(text == null ? '' : text);
        if (!term) { return escapeHtml(raw); }
        var index = raw.toLowerCase().indexOf(term.toLowerCase());
        if (index < 0) { return escapeHtml(raw); }
        return escapeHtml(raw.slice(0, index)) +
            '<mark>' + escapeHtml(raw.slice(index, index + term.length)) + '</mark>' +
            escapeHtml(raw.slice(index + term.length));
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

    function formatMoney(value, iso, symbol, precision) {
        var n = Number(value || 0); if (!isFinite(n)) { n = 0; }
        var p = Number(precision); if (isNaN(p) || p < 0) { p = 2; }
        var sign = n < 0 ? '-' : '';
        var tag = symbol || iso || '';
        if (window.VIS && VIS.Util && typeof VIS.Util.formatCompactAmount === 'function') {
            return sign + tag + VIS.Util.formatCompactAmount(Math.abs(n), iso || '', p);
        }
        return sign + tag + Math.abs(n).toFixed(p).replace(/\B(?=(\d{3})+(?!\d))/g, ',');
    }

    // Days-to-end pill: ended (past) is a cold/neutral tag, then red/amber/green
    // by how close the end date is - mirrors the urgency banding the rest of the
    // Service Contracts dashboard already uses (contracts-expiring: ≤30d red,
    // 31-90d amber, beyond that green).
    function daysPill(days) {
        var d = Number(days || 0);
        if (d < 0) { return { cls: 'vas241-days-cold', text: label('VAS_241_Ended', 'Ended') }; }
        var text = formatCount(d) + label('VAS_241_DaysToEnd', 'd to end');
        if (d <= 30) { return { cls: 'vas241-days-red', text: text }; }
        if (d <= 90) { return { cls: 'vas241-days-amber', text: text }; }
        return { cls: 'vas241-days-green', text: text };
    }

    // Status band chip colour, by the server-computed LIFECYCLE status code
    // (ACTIVE/EXPIRING/ENDED/CANCELLED/VOIDED/REVERSED/DRAFT) - NOT the raw
    // DocStatus code. A completed contract reads Active/Expiring/Ended by its
    // own EndDate (see VAS_241_ContractSearchWidgetController.
    // ComputeLifecycleStatusCode); the label shown is always the server's text.
    function bandClass(lifecycleStatusCode) {
        var code = String(lifecycleStatusCode || '').toUpperCase();
        if (code === 'ACTIVE') { return 'vas241-band-ok'; }
        if (code === 'EXPIRING') { return 'vas241-band-warn'; }
        if (code === 'CANCELLED' || code === 'VOIDED' || code === 'REVERSED' || code === 'ENDED') { return 'vas241-band-danger'; }
        return 'vas241-band-info';
    }

    function icon(name) {
        if (name === 'search') {
            return '<svg viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2" stroke-linecap="round" stroke-linejoin="round" aria-hidden="true"><circle cx="11" cy="11" r="8"></circle><path d="m21 21-4.35-4.35"></path></svg>';
        }
        if (name === 'close') {
            return '<svg viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2" stroke-linecap="round" aria-hidden="true"><path d="M18 6 6 18"></path><path d="m6 6 12 12"></path></svg>';
        }
        if (name === 'chevron') {
            return '<svg viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2" stroke-linecap="round" stroke-linejoin="round" aria-hidden="true"><path d="m9 18 6-6-6-6"></path></svg>';
        }
        if (name === 'arrow') {
            return '<svg viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2" stroke-linecap="round" stroke-linejoin="round" aria-hidden="true"><line x1="5" y1="12" x2="19" y2="12"></line><polyline points="12 5 19 12 12 19"></polyline></svg>';
        }
        if (name === 'open') {
            return '<svg viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2" stroke-linecap="round" stroke-linejoin="round" aria-hidden="true"><path d="M18 13v6a2 2 0 0 1-2 2H5a2 2 0 0 1-2-2V8a2 2 0 0 1 2-2h6"></path><path d="M15 3h6v6"></path><path d="M10 14 21 3"></path></svg>';
        }
        if (name === 'doc') {
            return '<svg viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2" stroke-linecap="round" stroke-linejoin="round" aria-hidden="true"><path d="M14 2H6a2 2 0 0 0-2 2v16a2 2 0 0 0 2 2h12a2 2 0 0 0 2-2V8z"></path><path d="M14 2v6h6"></path></svg>';
        }
        if (name === 'chevL') {
            return '<svg viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2" stroke-linecap="round" stroke-linejoin="round" aria-hidden="true"><polyline points="15 18 9 12 15 6"></polyline></svg>';
        }
        if (name === 'ticket') {
            return '<svg viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2" stroke-linecap="round" stroke-linejoin="round" aria-hidden="true"><path d="M2 9a3 3 0 0 1 0 6v2a2 2 0 0 0 2 2h16a2 2 0 0 0 2-2v-2a3 3 0 0 1 0-6V7a2 2 0 0 0-2-2H4a2 2 0 0 0-2 2z"></path><path d="M13 5v2M13 11v2M13 17v2"></path></svg>';
        }
        if (name === 'refresh') {
            return '<svg viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2" stroke-linecap="round" stroke-linejoin="round" aria-hidden="true"><path d="M21 12a9 9 0 1 1-2.64-6.36"></path><polyline points="21 3 21 9 15 9"></polyline></svg>';
        }
        if (name === 'warning') {
            return '<svg viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2" stroke-linecap="round" stroke-linejoin="round" aria-hidden="true"><path d="m21.73 18-8-14a2 2 0 0 0-3.48 0l-8 14A2 2 0 0 0 4 21h16a2 2 0 0 0 1.73-3Z"></path><line x1="12" y1="9" x2="12" y2="13"></line><line x1="12" y1="17" x2="12.01" y2="17"></line></svg>';
        }
        if (name === 'invoice') {
            return '<svg viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2" stroke-linecap="round" stroke-linejoin="round" aria-hidden="true"><path d="M14 2H6a2 2 0 0 0-2 2v16a2 2 0 0 0 2 2h12a2 2 0 0 0 2-2V8z"></path><path d="M14 2v6h6"></path><path d="M9 13h6M9 17h4"></path></svg>';
        }
        return '';
    }

    // Deterministic avatar tint from the customer name (same idea as VAS_120's
    // customer avatar).
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
        return String(name || '')
            .split(' ')
            .slice(0, 2)
            .map(function (word) { return word.charAt(0); })
            .join('')
            .toUpperCase();
    }

    // "ENDS" stat text: "in Nd" ahead of the end date, "Ended Nd ago" past it.
    function endsText(days) {
        var d = Number(days || 0);
        if (d < 0) { return label('VAS_241_EndedAgo', 'Ended {n}d ago').replace('{n}', formatCount(-d)); }
        return label('VAS_241_EndsIn', 'in {n}d').replace('{n}', formatCount(d));
    }

    // Populates --dash-inline-size (a shared, dashboard-wide CSS var read by
    // .vas241-root's own clamp() font-size formulas) from the actual dashboard
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

    VAS.VAS_241_ContractSearchWidget = function () {

        this.frame;
        this.windowNo;

        var $self = this;
        var $root = $('<div class="vas241-root">');
        var $input;
        var $clear;
        var $suggest;
        var $dashboardScroll;

        var searchTimer = null;
        var requestSequence = 0;
        var suggestions = [];
        var suggestionIndex = -1;
        var lastTotal = 0;
        var lastQuery = '';

        // Detail modal.
        var $detail, $detailBody;

        // "See all" paged list modal.
        var $list, $listBody, $listPager, $listTitle;
        var listQuery = '', listOffset = 0, listTotal = 0, listSeq = 0;

        function ns() { return '.MPCvas241-' + ($self.AD_UserHomeWidgetID || $self.windowNo || 'widget'); }

        this.Initalize = function () {
            createWidget();
            createSuggestionList();
            createDetailDialog();
            createConfirmDialog();
            createListDialog();
            bindEvents();
        };

        function createWidget() {
            var placeholder = label('VAS_241_SearchPlaceholderNoCount', 'Search contracts by no., customer, product or status…');
            var suggestId = 'vas241-suggestions-' + escapeHtml(String($self.windowNo || ''));

            $root.html(
                '<div class="vas241-searchbar">' +
                    '<span class="vas241-search-icon">' + icon('search') + '</span>' +
                    '<input class="vas241-input" type="text" autocomplete="off" role="combobox" aria-autocomplete="list" aria-expanded="false" aria-controls="' + suggestId + '" placeholder="' + escapeHtml(placeholder) + '">' +
                    '<button type="button" class="vas241-clear" aria-label="' + escapeHtml(label('Clear', 'Clear')) + '">' + icon('close') + '</button>' +
                '</div>'
            );

            $input = $root.find('.vas241-input');
            $clear = $root.find('.vas241-clear');
        }

        function createSuggestionList() {
            // Fixed on <body> so the dashboard cell's overflow/stacking cannot
            // clip the floating dropdown (same approach as the sibling search
            // widgets); the band itself never resizes on typing.
            $suggest = $('<div class="vas241-suggest" id="vas241-suggestions-' + escapeHtml(String($self.windowNo || '')) + '" role="listbox">');
            $('body').append($suggest);
        }

        function positionSuggest() {
            if (!$suggest) { return; }
            var field = $root.find('.vas241-searchbar')[0];
            if (!field) { return; }
            var rect = field.getBoundingClientRect();
            $suggest.css({
                left: Math.round(rect.left) + 'px',
                top: Math.round(rect.bottom + 6) + 'px',
                width: Math.round(rect.width) + 'px'
            });
        }

        function bindEvents() {
            var namespace = ns();

            $input.on('input', function () {
                $clear.css('display', $input.val() ? 'grid' : 'none');
                scheduleSearch();
            });
            $input.on('focus', function () {
                if ($input.val().trim().length > 0) { scheduleSearch(); }
            });
            $input.on('keydown', handleInputKeydown);

            $clear.on('click', function () {
                $input.val('');
                $clear.css('display', 'none');
                requestSequence += 1;
                closeSuggestions();
                $input.focus();
            });

            // mousedown (not click) so the row action fires before input blur closes the popover.
            $suggest.on('mousedown', '.vas241-option', function (event) {
                if ($(event.target).closest('.vas241-action').length) { return; }
                event.preventDefault();
                selectSuggestion(Number($(this).attr('data-index')));
            });
            $suggest.on('mousedown', '.vas241-action', function (event) {
                event.preventDefault();
                event.stopPropagation();
                var id = Number($(this).attr('data-id'));
                closeSuggestions();
                zoomToContract(id);
            });
            $suggest.on('mousedown', '.vas241-more', function (event) {
                event.preventDefault();
                openSeeAll();
            });

            $(document).on('mousedown' + namespace, function (event) {
                if (!$(event.target).closest('.vas241-searchbar, .vas241-suggest, .vas241-modal, .vas241-dialog').length) {
                    closeSuggestions();
                }
            });
            $(document).on('keydown' + namespace, function (event) {
                if (event.key !== 'Escape') { return; }
                if ($confirm && $confirm.hasClass('is-open')) { closeConfirm(); return; }
                if ($detail && $detail.hasClass('is-open')) { closeDetail(); return; }
                if ($list && $list.hasClass('is-open')) { closeList(); return; }
                closeSuggestions();
            });

            $(window).on('scroll' + namespace, closeSuggestions);
            $(window).on('resize' + namespace, function () {
                if ($suggest && $suggest.hasClass('is-open')) { positionSuggest(); }
            });

            $dashboardScroll = $root.closest('.vis-widget-container, [data-dashboard-container]');
            if ($dashboardScroll.length) {
                $dashboardScroll.on('scroll' + namespace, closeSuggestions);
            }
        }

        function scheduleSearch() {
            if (searchTimer) { clearTimeout(searchTimer); }

            var searchText = $input.val().trim();
            if (searchText.length === 0) {
                requestSequence += 1;
                closeSuggestions();
                return;
            }

            searchTimer = setTimeout(function () { searchContracts(searchText); }, 250);
        }

        function searchContracts(searchText) {
            var sequence = ++requestSequence;
            renderState(label('VAS_241_Searching', 'Searching…'));

            $.ajax({
                url: VIS.Application.contextUrl + WIDGET_ENDPOINT + 'SearchContracts',
                type: 'GET',
                dataType: 'json',
                cache: false,
                data: { q: searchText },
                success: function (response) {
                    if (sequence !== requestSequence) { return; }
                    var parsed = parseResponse(response);
                    if (parsed.Error) { renderState(parsed.Error); return; }

                    lastQuery = searchText;
                    lastTotal = Number(parsed.Total || 0);
                    suggestions = parsed.Rows || [];
                    suggestionIndex = suggestions.length ? 0 : -1;
                    renderSuggestions(searchText);
                },
                error: function () {
                    if (sequence !== requestSequence) { return; }
                    renderState(label('VAS_241_LoadError', "Couldn't search."));
                }
            });
        }

        function suggestionRowHtml(row, index, searchText) {
            var pill = daysPill(row.DaysToEnd);
            var band = bandClass(row.LifecycleStatusCode);
            var openLabel = label('VAS_241_OpenRecord', 'Open record');
            var meta = escapeHtml(row.ProductName || '') + ' · ' +
                escapeHtml(formatMoney(row.GrandTotal, row.CurrencyIso, row.CurrencySymbol, row.CurrencyPrecision)) +
                (row.EndDate ? ' · ' + escapeHtml(row.EndDate) : '');

            return '<div class="vas241-option' + (index === suggestionIndex ? ' is-active' : '') + '" role="option" aria-selected="' + (index === suggestionIndex ? 'true' : 'false') + '" data-index="' + index + '">' +
                '<span class="vas241-days ' + pill.cls + '">' + escapeHtml(pill.text) + '</span>' +
                '<span class="vas241-option-main">' +
                    '<span class="vas241-option-name" title="' + escapeHtml(row.DocumentNo + ' · ' + row.CustomerName) + '">' +
                        highlight(row.DocumentNo, searchText) + ' · ' + highlight(row.CustomerName, searchText) +
                    '</span>' +
                    '<span class="vas241-option-meta" title="' + escapeHtml(row.ProductName || '') + '">' + meta + '</span>' +
                '</span>' +
                '<span class="vas241-band ' + band + '">' + escapeHtml(row.LifecycleStatus || '') + '</span>' +
                '<span class="vas241-actions">' +
                    '<button type="button" class="vas241-action" data-id="' + row.ContractId + '" title="' + escapeHtml(openLabel) + '" aria-label="' + escapeHtml(openLabel) + '">' + icon('open') + '</button>' +
                '</span>' +
                '<span class="vas241-option-chev">' + icon('chevron') + '</span>' +
            '</div>';
        }

        function renderSuggestions(searchText) {
            if (!suggestions.length) {
                renderState(label('VAS_241_NoMatches', 'No contracts match') + ' "' + escapeHtml(searchText) + '".');
                return;
            }

            var html = suggestions.map(function (row, index) { return suggestionRowHtml(row, index, searchText); }).join('');

            if (lastTotal > suggestions.length) {
                var moreText = label('VAS_241_SeeAll', 'See all') + ' ' + lastTotal + ' ' + label('VAS_241_Matches', 'matches');
                html += '<div class="vas241-more" role="button" tabindex="0">' + escapeHtml(moreText) + ' ' + icon('arrow') + '</div>';
            }

            $suggest.html(html).addClass('is-open');
            positionSuggest();
            $input.attr('aria-expanded', 'true');
        }

        function renderState(message) {
            suggestions = [];
            suggestionIndex = -1;
            $suggest.html('<div class="vas241-state">' + escapeHtml(message) + '</div>').addClass('is-open');
            positionSuggest();
            $input.attr('aria-expanded', 'true');
        }

        function closeSuggestions() {
            if (!$suggest) { return; }
            $suggest.removeClass('is-open').empty();
            $input.attr('aria-expanded', 'false');
            suggestionIndex = -1;
        }

        function handleInputKeydown(event) {
            if (event.key === 'Enter') {
                if (suggestionIndex >= 0 && suggestions[suggestionIndex]) {
                    event.preventDefault();
                    selectSuggestion(suggestionIndex);
                } else if (suggestions.length || lastTotal) {
                    event.preventDefault();
                    openSeeAll();
                }
                return;
            }
            if (event.key === 'Escape') { closeSuggestions(); return; }
            if (!$suggest.hasClass('is-open') || !suggestions.length) { return; }

            if (event.key === 'ArrowDown') {
                event.preventDefault();
                suggestionIndex = (suggestionIndex + 1) % suggestions.length;
                renderSuggestions(lastQuery);
            } else if (event.key === 'ArrowUp') {
                event.preventDefault();
                suggestionIndex = suggestionIndex <= 0 ? suggestions.length - 1 : suggestionIndex - 1;
                renderSuggestions(lastQuery);
            }
        }

        function selectSuggestion(index) {
            var row = suggestions[index];
            if (!row) { return; }
            closeSuggestions();
            openDetail(row.ContractId);
        }

        /* ---------- Detail modal ---------- */

        var $detailMsg;
        var detailBusy = false;

        var $confirm, $confirmMsg;
        var confirmCallback = null;

        function createDetailDialog() {
            $detail = $(
                '<div class="vas241-modal" role="dialog" aria-modal="true" aria-hidden="true">' +
                    '<div class="vas241-scrim" data-detail-close></div>' +
                    '<section class="vas241-panel">' +
                        '<header class="vas241-mhead">' +
                            '<h2 class="vas241-mtitle">' + escapeHtml(label('VAS_241_ContractTitle', 'Service Contract')) + '</h2>' +
                            '<span class="vas241-mhead-meta"></span>' +
                            '<button type="button" class="vas241-mclose" data-detail-close aria-label="' + escapeHtml(label('VAS_241_Close', 'Close')) + '">' + icon('close') + '</button>' +
                        '</header>' +
                        '<div class="vas241-mbody"></div>' +
                        '<div class="vas241-mmsg" role="status"></div>' +
                        '<footer class="vas241-mfoot">' +
                            '<button type="button" class="vas241-btn vas241-btn-ghost" data-detail-invoice>' + icon('invoice') + escapeHtml(label('VAS_241_GenerateInvoice', 'Generate invoice')) + '</button>' +
                            '<button type="button" class="vas241-btn vas241-btn-ghost" data-detail-renew>' + icon('refresh') + escapeHtml(label('VAS_241_Renew', 'Renew')) + '</button>' +
                            '<button type="button" class="vas241-btn vas241-btn-primary" data-detail-open>' + icon('open') + escapeHtml(label('VAS_241_OpenRecord', 'Open record')) + '</button>' +
                        '</footer>' +
                    '</section>' +
                '</div>'
            );
            $('body').append($detail);
            $detailMsg = $detail.find('.vas241-mmsg');
            $detail.on('click', '[data-detail-close]', closeDetail);
            $detail.on('click', '[data-detail-open]', function () { zoomToContract(Number($detail.attr('data-cid'))); });
            $detail.on('click', '[data-detail-renew]', function () { runContractAction('RunRenew', label('VAS_241_ConfirmRenew', 'Run RenewContract for this contract now?')); });
            $detail.on('click', '[data-detail-invoice]', function () { runContractAction('RunGenerateInvoice', label('VAS_241_ConfirmGenerateInvoice', 'Generate invoices for the overdue billing periods now?')); });
        }

        /* ---------- Confirm dialog (app-styled - replaces the native window.confirm()) ---------- */

        function createConfirmDialog() {
            $confirm = $(
                '<div class="vas241-confirm-overlay" role="alertdialog" aria-modal="true" aria-hidden="true">' +
                    '<div class="vas241-confirm-scrim" data-confirm-cancel></div>' +
                    '<div class="vas241-confirm-box">' +
                        '<p class="vas241-confirm-msg"></p>' +
                        '<div class="vas241-confirm-actions">' +
                            '<button type="button" class="vas241-btn vas241-btn-ghost" data-confirm-cancel>' + escapeHtml(label('VAS_241_Cancel', 'Cancel')) + '</button>' +
                            '<button type="button" class="vas241-btn vas241-btn-primary" data-confirm-ok>' + escapeHtml(label('VAS_241_Ok', 'OK')) + '</button>' +
                        '</div>' +
                    '</div>' +
                '</div>'
            );
            $('body').append($confirm);
            $confirmMsg = $confirm.find('.vas241-confirm-msg');
            $confirm.on('click', '[data-confirm-cancel]', closeConfirm);
            $confirm.on('click', '[data-confirm-ok]', function () {
                var callback = confirmCallback;
                closeConfirm();
                if (typeof callback === 'function') { callback(); }
            });
        }

        function showConfirm(message, onConfirm) {
            confirmCallback = onConfirm;
            $confirmMsg.text(message);
            $confirm.addClass('is-open').attr('aria-hidden', 'false');
        }

        function closeConfirm() {
            confirmCallback = null;
            $confirm.removeClass('is-open').attr('aria-hidden', 'true');
        }

        function statHtml(labelKey, fallback, value) {
            return '<div class="vas241-stat">' +
                '<span class="vas241-slabel">' + escapeHtml(label(labelKey, fallback)) + '</span>' +
                '<span class="vas241-sval">' + escapeHtml(value == null ? '' : String(value)) + '</span>' +
            '</div>';
        }

        function showDetailMessage(message, isError) {
            if (!$detailMsg) { return; }
            if (!message) { $detailMsg.removeClass('is-error is-open').empty(); return; }
            $detailMsg.text(message).toggleClass('is-error', !!isError).addClass('is-open');
        }

        function setDetailBusy(busy) {
            detailBusy = busy;
            $detail.find('[data-detail-renew], [data-detail-invoice]').prop('disabled', busy);
        }

        function openDetail(contractId) {
            if (!contractId) { return; }
            $detail.attr('data-cid', contractId);
            $detailBody = $detail.find('.vas241-mbody');
            $detail.find('.vas241-mhead-meta').empty();
            $detailBody.html('<div class="vas241-state">' + escapeHtml(label('Loading', 'Loading…')) + '</div>');
            showDetailMessage('');
            $detail.addClass('is-open').attr('aria-hidden', 'false');
            $('body').addClass('vas241-modal-open');

            $.ajax({
                url: VIS.Application.contextUrl + WIDGET_ENDPOINT + 'GetContract',
                type: 'GET', dataType: 'json', cache: false,
                data: { id: contractId },
                success: function (response) {
                    var parsed = parseResponse(response);
                    var row = parsed && parsed.Contract;
                    if (!row || !row.ContractId) {
                        $detailBody.html('<div class="vas241-state">' + escapeHtml(label('VAS_241_DetailLoadError', "Couldn't load this contract.")) + '</div>');
                        return;
                    }
                    renderDetail(row);
                },
                error: function () {
                    $detailBody.html('<div class="vas241-state">' + escapeHtml(label('VAS_241_DetailLoadError', "Couldn't load this contract.")) + '</div>');
                }
            });
        }

        // Manual-renewal notice window (kpi-renewal-action.queries.md): a manual
        // (never auto) contract with no active successor whose notice window is
        // already open (NoticeSlackDays = daysToEnd - CancelBeforeDays <= 0) needs
        // action - shown first since it is the most time-critical card.
        function isManualRenewal(code) {
            var c = String(code || '').toUpperCase();
            return c === 'M' || c === 'MNL';
        }

        function attentionCards(row) {
            var cards = '';

            if (isManualRenewal(row.RenewalTypeCode) && !row.HasSuccessor && row.NoticeSlackDays != null && row.NoticeSlackDays <= 0) {
                var noticeTitle = row.NoticeSlackDays < 0
                    ? label('VAS_241_RenewalNoticeOverdue', 'Renewal notice overdue')
                    : label('VAS_241_RenewalNoticeDueToday', 'Renewal notice due today');
                var noticeDescParts = [label('VAS_241_ManualRenewal', 'Manual renewal')];
                if (row.NoticeSlackDays < 0) {
                    noticeDescParts.push(label('VAS_241_NoticeOverdueBy', 'notice {n}d overdue').replace('{n}', formatCount(-row.NoticeSlackDays)));
                }
                noticeDescParts.push(label('VAS_241_EndsPrefix', 'ends') + ' ' + endsText(row.DaysToEnd));
                cards += '<div class="vas241-attn-card">' +
                    '<span class="vas241-attn-ic vas241-attn-ic-danger">' + icon('warning') + '</span>' +
                    '<span class="vas241-attn-main">' +
                        '<span class="vas241-attn-title2">' + escapeHtml(noticeTitle) + '</span>' +
                        '<span class="vas241-attn-desc">' + escapeHtml(noticeDescParts.join(' · ')) + '</span>' +
                    '</span>' +
                '</div>';
            }

            if (Number(row.OverdueDays || 0) > 0) {
                var overdueDesc = label('VAS_241_NextPeriodOverdue', 'Next period {n}d overdue').replace('{n}', formatCount(row.OverdueDays)) +
                    ' · ' + formatMoney(row.OverdueAmount, row.CurrencyIso, row.CurrencySymbol, row.CurrencyPrecision) +
                    ' · ' + formatMoney(row.Unbilled, row.CurrencyIso, row.CurrencySymbol, row.CurrencyPrecision) + ' ' + label('VAS_241_UnbilledTotal', 'unbilled total');
                cards += '<div class="vas241-attn-card">' +
                    '<span class="vas241-attn-ic">' + icon('invoice') + '</span>' +
                    '<span class="vas241-attn-main">' +
                        '<span class="vas241-attn-title2">' + escapeHtml(label('VAS_241_BillingOverdue', 'Billing overdue')) + '</span>' +
                        '<span class="vas241-attn-desc">' + escapeHtml(overdueDesc) + '</span>' +
                    '</span>' +
                '</div>';
            }

            if (!cards) {
                cards = '<div class="vas241-attn-empty">' + escapeHtml(label('VAS_241_NoOpenActions', 'No open actions — contract is healthy.')) + '</div>';
            }

            return '<div class="vas241-attn">' +
                '<div class="vas241-attn-title">' + escapeHtml(label('VAS_241_NeedsAttention', 'What needs attention')) + '</div>' +
                cards +
            '</div>';
        }

        function scheduleHtml(row) {
            var rows = row.Schedule || [];
            if (!rows.length) { return ''; }

            var periodLabel = label('VAS_241_Period', 'Period');
            var invoicedLabel = label('VAS_241_Invoiced', 'Invoiced');
            var pendingLabel = label('VAS_241_SchedulePending', 'Pending');

            var body = rows.map(function (period) {
                var dateRange = (period.FromDate || period.ToDate) ? ' · ' + escapeHtml(period.FromDate || '') + ' – ' + escapeHtml(period.ToDate || '') : '';
                var left = periodLabel + ' ' + period.Period + (period.FrequencyName ? ' · ' + escapeHtml(period.FrequencyName) : '') + dateRange;
                var badgeClass = period.Invoiced ? 'vas241-band-ok' : 'vas241-band-warn';
                var badgeText = period.Invoiced ? invoicedLabel : pendingLabel;
                return '<div class="vas241-sched-row">' +
                    '<span class="vas241-sched-left">' + left + '</span>' +
                    '<span class="vas241-sched-right">' +
                        '<span class="vas241-sched-amt">' + escapeHtml(formatMoney(period.Amount, row.CurrencyIso, row.CurrencySymbol, row.CurrencyPrecision)) + '</span>' +
                        '<span class="vas241-band ' + badgeClass + '">' + escapeHtml(badgeText) + '</span>' +
                    '</span>' +
                '</div>';
            }).join('');

            return '<div class="vas241-sched">' +
                '<div class="vas241-sched-title">' + escapeHtml(label('VAS_241_BillingSchedule', 'Billing schedule')) + '</div>' +
                '<div class="vas241-sched-list">' + body + '</div>' +
            '</div>';
        }

        function renderDetail(row) {
            $detail.find('.vas241-mhead-meta').text(row.DocumentNo + ' · ' + (row.LifecycleStatus || ''));

            var band = bandClass(row.LifecycleStatusCode);
            var subtitleParts = [row.DocumentNo, row.ProductName, row.ContactName, row.SalesRepName ? label('VAS_241_Rep', 'rep') + ' ' + row.SalesRepName : '']
                .filter(function (p) { return p; });
            var cyclesText = [row.FrequencyName, (row.Cycles != null ? row.Cycles + ' ' + label('VAS_241_Cycles', 'cycles') : '')]
                .filter(function (p) { return p; }).join(' · ');

            var html =
                '<div class="vas241-dtop2">' +
                    '<span class="vas241-davatar" style="background:' + avatarColor(row.CustomerName) + '">' + escapeHtml(initials(row.CustomerName)) + '</span>' +
                    '<span class="vas241-dhead-main">' +
                        '<span class="vas241-dname">' + escapeHtml(row.CustomerName || '') + '</span>' +
                        '<span class="vas241-dsub">' + escapeHtml(subtitleParts.join(' · ')) + '</span>' +
                    '</span>' +
                    '<span class="vas241-dhead-right">' +
                        '<span class="vas241-band ' + band + '">' + escapeHtml(row.LifecycleStatus || '') + '</span>' +
                        (cyclesText ? '<span class="vas241-dcycles">' + escapeHtml(cyclesText) + '</span>' : '') +
                    '</span>' +
                '</div>' +
                '<div class="vas241-stats">' +
                    statHtml('VAS_241_Value', 'Contract cycle value', formatMoney(row.GrandTotal, row.CurrencyIso, row.CurrencySymbol, row.CurrencyPrecision)) +
                    statHtml('VAS_241_Status', 'Status', row.LifecycleStatus) +
                    statHtml('VAS_241_Type', 'Type', row.ContractType) +
                    statHtml('VAS_241_Renewal', 'Renewal', row.RenewalType) +
                    statHtml('VAS_241_Ends', 'Ends', endsText(row.DaysToEnd)) +
                    statHtml('VAS_241_NoticeDays', 'Notice days', row.NoticeDays != null ? row.NoticeDays : '') +
                    statHtml('VAS_241_Billed', 'Billed amount', formatMoney(row.Billed, row.CurrencyIso, row.CurrencySymbol, row.CurrencyPrecision)) +
                    statHtml('VAS_241_Unbilled', 'Unbilled amount', formatMoney(row.Unbilled, row.CurrencyIso, row.CurrencySymbol, row.CurrencyPrecision)) +
                '</div>' +
                attentionCards(row) +
                scheduleHtml(row);

            $detailBody.html(html);
        }

        // Runs a mutating contract action (Renew / Generate invoice) through its
        // POST endpoint, gated by a confirm step since both mutate real documents.
        // On success the detail is re-fetched so the stat grid / schedule reflect
        // what the process actually did.
        function runContractAction(endpoint, confirmMessage) {
            if (detailBusy) { return; }
            var contractId = Number($detail.attr('data-cid'));
            if (!contractId) { return; }

            showConfirm(confirmMessage, function () {
                setDetailBusy(true);
                showDetailMessage(label('VAS_241_Working', 'Working…'), false);

                $.ajax({
                    url: VIS.Application.contextUrl + WIDGET_ENDPOINT + endpoint,
                    type: 'POST', dataType: 'json', cache: false,
                    data: { id: contractId },
                    success: function (response) {
                        setDetailBusy(false);
                        var parsed = parseResponse(response);
                        if (!parsed || !parsed.Success) {
                            showDetailMessage((parsed && parsed.Message) || label('VAS_241_ActionFailed', 'The action failed.'), true);
                            return;
                        }
                        showDetailMessage(parsed.Message || '', false);
                        openDetail(contractId);
                    },
                    error: function () {
                        setDetailBusy(false);
                        showDetailMessage(label('VAS_241_ActionFailed', 'The action failed.'), true);
                    }
                });
            });
        }

        function closeDetail() {
            if (!$detail) { return; }
            $detail.removeClass('is-open').attr('aria-hidden', 'true');
            if (!($list && $list.hasClass('is-open'))) { $('body').removeClass('vas241-modal-open'); }
        }

        /* ---------- "See all N matches" paged list modal ---------- */

        function createListDialog() {
            $list = $(
                '<div class="vas241-dialog" role="dialog" aria-modal="true" aria-hidden="true">' +
                    '<div class="vas241-scrim" data-list-close></div>' +
                    '<section class="vas241-panel">' +
                        '<header class="vas241-phead"><h2 class="vas241-ptitle"></h2>' +
                            '<button type="button" class="vas241-close" data-list-close aria-label="' + escapeHtml(label('VAS_241_Close', 'Close')) + '">' + icon('close') + '</button></header>' +
                        '<div class="vas241-pbody"></div>' +
                        '<footer class="vas241-pfoot"><div class="vas241-pager"></div></footer>' +
                    '</section>' +
                '</div>'
            );
            $('body').append($list);
            $listBody = $list.find('.vas241-pbody');
            $listPager = $list.find('.vas241-pager');
            $listTitle = $list.find('.vas241-ptitle');
            $list.on('click', '[data-list-close]', closeList);
            $list.on('click', '.vas241-row', function () { openDetail(Number($(this).attr('data-cid'))); });
            $list.on('click', '.vas241-pgbtn', function () { turnPage($(this).attr('data-dir')); });
        }

        function openSeeAll() {
            var term = (lastQuery || $input.val() || '').trim();
            if (!term) { return; }
            closeSuggestions();
            listQuery = term;
            listOffset = 0;
            $listTitle.text(label('VAS_241_ListTitle', 'Contract Search Results') + ' · "' + term + '"');
            $list.addClass('is-open').attr('aria-hidden', 'false');
            $('body').addClass('vas241-modal-open');
            loadList();
        }

        function closeList() {
            if (!$list) { return; }
            $list.removeClass('is-open').attr('aria-hidden', 'true');
            if (!($detail && $detail.hasClass('is-open'))) { $('body').removeClass('vas241-modal-open'); }
        }

        function loadList() {
            var seq = ++listSeq;
            $listBody.html('<div class="vas241-state">' + escapeHtml(label('Loading', 'Loading…')) + '</div>');
            $listPager.empty();

            $.ajax({
                url: VIS.Application.contextUrl + WIDGET_ENDPOINT + 'GetContracts',
                type: 'GET', dataType: 'json', cache: false,
                data: { q: listQuery, offset: listOffset },
                success: function (response) {
                    if (seq !== listSeq) { return; }
                    var parsed = parseResponse(response);
                    if (parsed.Error) {
                        $listBody.html('<div class="vas241-state">' + escapeHtml(label('VAS_241_UnableToLoad', 'Unable to load contracts.')) + '</div>');
                        return;
                    }
                    listTotal = Number(parsed.Total || 0);
                    renderList(parsed.Rows || []);
                },
                error: function () {
                    if (seq === listSeq) {
                        $listBody.html('<div class="vas241-state">' + escapeHtml(label('VAS_241_UnableToLoad', 'Unable to load contracts.')) + '</div>');
                    }
                }
            });
        }

        function listRowHtml(row) {
            var pill = daysPill(row.DaysToEnd);
            var band = bandClass(row.LifecycleStatusCode);
            var meta = [row.ProductName, row.ContractType, row.RenewalType].filter(function (p) { return p; }).join(' · ');

            return '<div class="vas241-row" data-cid="' + Number(row.ContractId) + '">' +
                '<span class="vas241-row-ic">' + icon('doc') + '</span>' +
                '<span class="vas241-row-main">' +
                    '<span class="vas241-row-title" title="' + escapeHtml(row.DocumentNo + ' · ' + row.CustomerName) + '">' + escapeHtml(row.DocumentNo) + ' · ' + escapeHtml(row.CustomerName) + '</span>' +
                    '<span class="vas241-row-meta" title="' + escapeHtml(meta) + '">' + escapeHtml(meta) + '</span>' +
                '</span>' +
                '<span class="vas241-band ' + band + '">' + escapeHtml(row.LifecycleStatus || '') + '</span>' +
                '<span class="vas241-row-right">' +
                    '<span class="vas241-row-val">' + escapeHtml(formatMoney(row.GrandTotal, row.CurrencyIso, row.CurrencySymbol, row.CurrencyPrecision)) + '</span>' +
                    '<span class="vas241-days ' + pill.cls + '">' + escapeHtml(pill.text) + '</span>' +
                '</span>' +
            '</div>';
        }

        function renderList(rows) {
            if (!rows.length) {
                $listBody.html('<div class="vas241-state">' + escapeHtml(label('VAS_241_NoContracts', 'No contracts.')) + '</div>');
                $listPager.empty();
                return;
            }

            $listBody.html('<div class="vas241-list">' + rows.map(listRowHtml).join('') + '</div>');

            var start = listOffset + 1, end = listOffset + rows.length;
            var pages = Math.max(1, Math.ceil(listTotal / LIST_PAGE));
            var current = Math.floor(listOffset / LIST_PAGE);
            var of = label('VAS_241_Of', 'of');
            var helper = label('VAS_241_Showing', 'Showing') + ' ' + start + '–' + end + ' ' + of + ' ' + formatCount(listTotal);

            $listPager.html(
                '<span class="vas241-pglabel">' + escapeHtml(helper) + '</span>' +
                '<span class="vas241-pgctl">' +
                    '<button type="button" class="vas241-pgbtn" data-dir="prev" aria-label="' + escapeHtml(label('VAS_241_PrevPage', 'Previous page')) + '" ' + (current <= 0 ? 'disabled' : '') + '>' + icon('chevL') + '</button>' +
                    '<span class="vas241-pgtext">' + escapeHtml((current + 1) + ' ' + of + ' ' + pages) + '</span>' +
                    '<button type="button" class="vas241-pgbtn" data-dir="next" aria-label="' + escapeHtml(label('VAS_241_NextPage', 'Next page')) + '" ' + (current >= pages - 1 ? 'disabled' : '') + '>' + icon('chevron') + '</button>' +
                '</span>'
            );
        }

        function turnPage(direction) {
            var next = listOffset + (direction === 'next' ? LIST_PAGE : -LIST_PAGE);
            if (next < 0) { next = 0; }
            if (next >= listTotal) { return; }
            listOffset = next;
            loadList();
        }

        /* ---------- Zoom to record (Scenario 1 in-window / Scenario 2 standalone) ---------- */

        // Resolve the name of the window currently hosting this widget so the
        // in-place navigation targets the current grid rather than always the
        // fallback name (same approach as VAS_120/140).
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

        function zoomToContract(contractId) {
            if (!contractId) { return; }
            closeDetail();
            closeList();
            try {
                if ($self.windowNo >= 0) {
                    // Scenario 1: hosted on a window - navigate that grid in place.
                    $self.widgetFirevalueChanged({
                        "TabWhereClause": "C_Contract.C_Contract_ID=" + Number(contractId),
                        "TabLayout": "Y",
                        "TabIndex": "0",
                        "ActionName": hostWindowName() || CONTRACT_WINDOW_NAME,
                        "ActionType": "W"
                    });
                } else {
                    // Scenario 2: no host grid (Home / landing dashboard) - zoom
                    // straight to the standard Service Contract window.
                    VAS.ZoomUtil.zoomToRecord(ZOOM_TABLE + '_ID', Number(contractId), ZOOM_WINDOW_ID, null, null);
                }
            } catch (e) { /* zoom is best-effort */ }
        }

        this.refreshWidget = function () {
            requestSequence += 1;
            suggestions = [];
            lastTotal = 0;
            lastQuery = '';
            if (searchTimer) { clearTimeout(searchTimer); }
            if ($input) { $input.val(''); }
            if ($clear) { $clear.css('display', 'none'); }
            closeSuggestions();
        };

        this.getRoot = function () { return $root; };

        this.disposeComponent = function () {
            var namespace = ns();
            if (searchTimer) { clearTimeout(searchTimer); }
            $(document).off(namespace);
            $(window).off(namespace);
            if ($dashboardScroll && $dashboardScroll.length) { $dashboardScroll.off(namespace); }
            if ($suggest) { $suggest.remove(); $suggest = null; }
            if ($detail) { $detail.remove(); $detail = null; }
            if ($confirm) { $confirm.remove(); $confirm = null; }
            if ($list) { $list.remove(); $list = null; }
            $('body').removeClass('vas241-modal-open');
            $root.remove();
        };
    };

    VAS.VAS_241_ContractSearchWidget.prototype.widgetFirevalueChanged = function (value) {
        if (this.listener) { this.listener.widgetFirevalueChanged(value); }
    };

    VAS.VAS_241_ContractSearchWidget.prototype.addChangeListener = function (listener) {
        this.listener = listener;
    };

    VAS.VAS_241_ContractSearchWidget.prototype.init = function (windowNo, frame) {
        this.frame = frame;
        this.AD_UserHomeWidgetID = frame.widgetInfo.AD_UserHomeWidgetID;
        this.windowNo = windowNo;
        this.Initalize();
        this.frame.getContentGrid().append(this.getRoot());
        ensureDashInlineSizeVar(this.getRoot());
    };

    VAS.VAS_241_ContractSearchWidget.prototype.widgetSizeChange = function (height, width) { };

    VAS.VAS_241_ContractSearchWidget.prototype.refreshWidget = function () {
        this.refreshWidget();
    };

    VAS.VAS_241_ContractSearchWidget.prototype.dispose = function () {
        this.disposeComponent();
        if (this.frame) { this.frame.dispose(); }
        this.frame = null;
    };

})(VAS, jQuery);
