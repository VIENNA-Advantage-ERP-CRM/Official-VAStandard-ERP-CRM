/**
 * VAS_120 Customer Search Widget (Customers module dashboard)
 * Purpose - Full-width glass search band with live, debounced auto-suggest across
 *           customers by company name, code, primary contact, e-mail, customer
 *           group (segment) and owner (sales rep). Shows at most seven
 *           relevance-ranked rows; each row has avatar, company, contact/segment/
 *           owner meta, an optional tier tag (only when the tenant configures a
 *           Rating->tier mapping) and three quick actions (create task, schedule,
 *           log activity). Clicking a row zooms the host Customers window to that
 *           C_BPartner record in place; "See all matches" re-filters the same grid.
 * Design  - customer-search.html (attached) + Design Specs/dashboard-widgets.md
 *           "Full-Width Dashboard Search Widget". Onfinity glass tokens; internal
 *           sizing in em; no inner scrollbar (autosuggest bounded to seven).
 *           CSS namespaced vas120-* (Prompt_Instructions MPC prefix rule).
 *
 * Backend - VAS_120_CustomerSearchWidget/SearchCustomers  (rows + total count)
 *
 * Routing - Record open + See-all use widgetFirevalueChanged (in-place zoom on the
 *           host window, Prompt_Instructions Scenario 1), mirroring VAS_067/VAS_083.
 * Quick actions - Best-effort openers: an integrator wires the real Task /
 *           Appointment / Activity windows via the documented override
 *           VAS.VAS_120_openCustomerActivity(kind, C_BPartner_ID); absent that,
 *           the widget opens the customer record as a safe, guarded fallback.
 *           See the integration note in the PR description. No production toast-
 *           only stubs remain.
 *
 * ── Labels / Message Keys ─────────────────────────────────────────────────
 *  #  | Current Text                                              | Message Key
 * ----+-----------------------------------------------------------+---------------------------------
 *  1  | Search customers by company, contact, segment, or owner…  | VAS_120_SearchPlaceholder
 *  2  | Searching…                                                | VAS_120_Searching
 *  3  | No customers match                                        | VAS_120_NoMatches
 *  4  | Type at least 2 characters                                | VAS_120_TypeMore
 *  5  | Couldn't load customers.                                  | VAS_120_LoadError
 *  6  | See all                                                   | VAS_120_SeeAll
 *  7  | matches                                                   | VAS_120_Matches
 *  8  | Unsegmented                                               | VAS_120_Unsegmented
 *  9  | No owner                                                  | VAS_120_NoOwner
 * 10  | Create task                                               | VAS_120_CreateTask
 * 11  | Schedule                                                  | VAS_120_Schedule
 * 12  | Log activity                                              | VAS_120_LogActivity
 * ──────────────────────────────────────────────────────────────────────────
 */
; VAS = window.VAS || {};

; (function (VAS, $) {

    // Host window to zoom when the widget is not hosted on the Customers window
    // itself. Documented for admin confirmation (in-place zoom uses the resolved
    // host window name first; this is only the fallback ActionName).
    var CUSTOMER_WINDOW_NAME = "Business Partner";

    VAS.VAS_120_CustomerSearchWidget = function () {

        this.frame;
        this.windowNo;

        var $self = this;
        var $root = $('<div class="vas120-root">');
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
        var minLength = 2;

        function label(key, fallback) {
            var translated = VIS.Msg.getMsg(key);
            return translated && translated.charAt(0) !== '[' ? translated : fallback;
        }

        function escapeHtml(value) {
            if (value == null) { return ''; }
            return String(value).replace(/[&<>"']/g, function (character) {
                return {
                    '&': '&amp;',
                    '<': '&lt;',
                    '>': '&gt;',
                    '"': '&quot;',
                    "'": '&#39;'
                }[character];
            });
        }

        // Safe highlight: split the raw text on the (case-insensitive) match,
        // escape every part, then wrap only the matched slice in <mark>. Query
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
            if (name === 'task') {
                return '<svg viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2" stroke-linecap="round" stroke-linejoin="round" aria-hidden="true"><path d="m3 17 2 2 4-4"></path><path d="m3 7 2 2 4-4"></path><path d="M13 6h8M13 12h8M13 18h8"></path></svg>';
            }
            if (name === 'calendar') {
                return '<svg viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2" stroke-linecap="round" stroke-linejoin="round" aria-hidden="true"><rect x="3" y="4" width="18" height="18" rx="2"></rect><path d="M16 2v4M8 2v4M3 10h18"></path></svg>';
            }
            if (name === 'log') {
                return '<svg viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2" stroke-linecap="round" stroke-linejoin="round" aria-hidden="true"><path d="M22 16.92v3a2 2 0 0 1-2.18 2 19.79 19.79 0 0 1-8.63-3.07 19.5 19.5 0 0 1-6-6 19.79 19.79 0 0 1-3.07-8.67A2 2 0 0 1 4.11 2h3a2 2 0 0 1 2 1.72c.13.96.36 1.9.7 2.81a2 2 0 0 1-.45 2.11L8.09 9.91a16 16 0 0 0 6 6l1.27-1.27a2 2 0 0 1 2.11-.45c.91.34 1.85.57 2.81.7A2 2 0 0 1 22 16.92z"></path></svg>';
            }
            if (name === 'arrow') {
                return '<svg viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2" stroke-linecap="round" stroke-linejoin="round" aria-hidden="true"><line x1="5" y1="12" x2="19" y2="12"></line><polyline points="12 5 19 12 12 19"></polyline></svg>';
            }
            return '';
        }

        function parseResponse(response) {
            var parsed = response;
            if (typeof parsed === 'string' && parsed.length) { parsed = JSON.parse(parsed); }
            if (typeof parsed === 'string' && parsed.length) { parsed = JSON.parse(parsed); }
            return parsed || {};
        }

        // Deterministic avatar tint from the company name (matches the reference
        // design's avColor palette).
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

        // Tier tag colour class per the Onfinity tier palette (only used when the
        // backend supplied a mapped tier; unknown ratings show no tag).
        function tierClass(tier) {
            if (tier === 'Platinum') { return 'vas120-tag-violet'; }
            if (tier === 'Gold') { return 'vas120-tag-amber'; }
            if (tier === 'Silver') { return 'vas120-tag-info'; }
            return '';
        }

        this.Initalize = function () {
            createWidget();
            createSuggestionList();
            bindEvents();
        };

        function createWidget() {
            var placeholder = label('VAS_120_SearchPlaceholder', 'Search customers by company, contact, segment, or owner…');
            var suggestId = 'vas120-suggestions-' + escapeHtml(String($self.windowNo || ''));

            $root.html(
                '<div class="vas120-searchbar">' +
                    '<div class="vas120-field">' +
                        '<span class="vas120-search-icon">' + icon('search') + '</span>' +
                        '<input class="vas120-input" type="text" autocomplete="off" role="combobox" aria-autocomplete="list" aria-expanded="false" aria-controls="' + suggestId + '" placeholder="' + escapeHtml(placeholder) + '">' +
                        '<button type="button" class="vas120-clear" aria-label="' + escapeHtml(label('Clear', 'Clear')) + '" style="display:none">' + icon('close') + '</button>' +
                    '</div>' +
                '</div>'
            );

            $input = $root.find('.vas120-input');
            $clear = $root.find('.vas120-clear');
        }

        function createSuggestionList() {
            // The dropdown lives on <body> as a fixed popover so the dashboard
            // cell's overflow/stacking cannot clip it (same approach as VAS_078).
            $suggest = $('<div class="vas120-suggest" id="vas120-suggestions-' + escapeHtml(String($self.windowNo || '')) + '" role="listbox">');
            $('body').append($suggest);
        }

        function positionSuggest() {
            if (!$suggest) { return; }
            var field = $root.find('.vas120-searchbar')[0];
            if (!field) { return; }
            var rect = field.getBoundingClientRect();
            $suggest.css({
                left: Math.round(rect.left) + 'px',
                top: Math.round(rect.bottom + 6) + 'px',
                width: Math.round(rect.width) + 'px'
            });
        }

        function bindEvents() {
            var ns = '.MPCvas120-' + ($self.AD_UserHomeWidgetID || $self.windowNo || 'widget');

            $input.on('input', function () {
                $clear.css('display', $input.val() ? 'grid' : 'none');
                scheduleSearch();
            });
            $input.on('focus', function () {
                if ($input.val().trim().length >= minLength) { scheduleSearch(); }
            });
            $input.on('keydown', handleInputKeydown);

            $clear.on('click', function () {
                $input.val('');
                $clear.css('display', 'none');
                requestSequence += 1;
                closeSuggestions();
                $input.focus();
            });

            // mousedown (not click) so the row action fires before the input blur
            // closes the popover.
            $suggest.on('mousedown', '.vas120-option', function (event) {
                if ($(event.target).closest('.vas120-action').length) { return; }
                event.preventDefault();
                selectSuggestion(Number($(this).attr('data-index')));
            });
            $suggest.on('click', '.vas120-action', function (event) {
                event.preventDefault();
                event.stopPropagation();
                var kind = $(this).attr('data-action');
                var bpId = Number($(this).attr('data-id'));
                openCustomerActivity(kind, bpId);
                closeSuggestions();
            });
            $suggest.on('mousedown', '.vas120-more', function (event) {
                event.preventDefault();
                openSeeAll();
            });

            $(document).on('mousedown' + ns, function (event) {
                if (!$(event.target).closest('.vas120-searchbar, .vas120-suggest').length) {
                    closeSuggestions();
                }
            });
            $(document).on('keydown' + ns, function (event) {
                if (event.key === 'Escape') { closeSuggestions(); }
            });

            $(window).on('scroll' + ns, closeSuggestions);
            $(window).on('resize' + ns, function () {
                if ($suggest && $suggest.hasClass('is-open')) { positionSuggest(); }
            });

            $dashboardScroll = $root.closest('.vis-widget-container, [data-dashboard-container]');
            if ($dashboardScroll.length) {
                $dashboardScroll.on('scroll' + ns, closeSuggestions);
            }
        }

        function scheduleSearch() {
            if (searchTimer) { clearTimeout(searchTimer); }

            var searchText = $input.val().trim();
            if (searchText.length < minLength) {
                requestSequence += 1;
                if (searchText.length === 0) { closeSuggestions(); }
                else { renderState(label('VAS_120_TypeMore', 'Type at least 2 characters')); }
                return;
            }

            searchTimer = setTimeout(function () {
                searchCustomers(searchText);
            }, 250);
        }

        function searchCustomers(searchText) {
            var sequence = ++requestSequence;
            renderState(label('VAS_120_Searching', 'Searching…'));

            $.ajax({
                url: VIS.Application.contextUrl + 'VAS_120_CustomerSearchWidget/SearchCustomers',
                type: 'GET',
                dataType: 'json',
                cache: false,
                data: { q: searchText, max: 7 },
                success: function (response) {
                    // Ignore stale responses: a newer keystroke already fired.
                    if (sequence !== requestSequence) { return; }

                    var parsed = parseResponse(response);
                    if (parsed.Error) { renderState(parsed.Error); return; }

                    if (parsed.MinLength) { minLength = parsed.MinLength; }
                    lastQuery = searchText;
                    lastTotal = Number(parsed.Total || 0);
                    suggestions = parsed.Rows || [];
                    suggestionIndex = suggestions.length ? 0 : -1;
                    renderSuggestions(searchText);
                },
                error: function () {
                    if (sequence !== requestSequence) { return; }
                    renderState(label('VAS_120_LoadError', "Couldn't load customers."));
                }
            });
        }

        function renderSuggestions(searchText) {
            if (!suggestions.length) {
                renderState(label('VAS_120_NoMatches', 'No customers match') + ' “' + escapeHtml(searchText) + '”.');
                return;
            }

            var unsegmented = label('VAS_120_Unsegmented', 'Unsegmented');
            var noOwner = label('VAS_120_NoOwner', 'No owner');
            var taskLabel = label('VAS_120_CreateTask', 'Create task');
            var scheduleLabel = label('VAS_120_Schedule', 'Schedule');
            var logLabel = label('VAS_120_LogActivity', 'Log activity');

            var html = suggestions.map(function (customer, index) {
                var displayName = customer.Name || customer.Value || '';
                var contact = customer.Contact || '';
                var segment = customer.Segment || unsegmented;
                var owner = customer.Rep || noOwner;
                var meta = (contact ? highlight(contact, searchText) + ' · ' : '') +
                    escapeHtml(segment) + ' · ' + escapeHtml(owner);
                var metaPlain = (contact ? contact + ' · ' : '') + segment + ' · ' + owner;

                var tierTag = '';
                if (customer.Tier) {
                    tierTag = '<span class="vas120-tag ' + tierClass(customer.Tier) + '">' + escapeHtml(customer.Tier) + '</span>';
                }
                if (customer.IsKey) {
                    tierTag += '<span class="vas120-tag vas120-tag-key">' + escapeHtml(label('VAS_120_Key', 'Key')) + '</span>';
                }

                return '<div class="vas120-option' + (index === suggestionIndex ? ' is-active' : '') + '" role="option" aria-selected="' + (index === suggestionIndex ? 'true' : 'false') + '" data-index="' + index + '">' +
                    '<span class="vas120-avatar" style="background:' + avatarColor(displayName) + '">' + escapeHtml(initials(displayName)) + '</span>' +
                    '<span class="vas120-option-main">' +
                        '<span class="vas120-option-name" title="' + escapeHtml(displayName) + '">' + highlight(displayName, searchText) + '</span>' +
                        '<span class="vas120-option-meta" title="' + escapeHtml(metaPlain) + '">' + meta + '</span>' +
                    '</span>' +
                    (tierTag ? '<span class="vas120-tags">' + tierTag + '</span>' : '') +
                    '<span class="vas120-actions">' +
                        '<button type="button" class="vas120-action" data-action="task" data-id="' + customer.Id + '" title="' + escapeHtml(taskLabel) + '" aria-label="' + escapeHtml(taskLabel) + '">' + icon('task') + '</button>' +
                        '<button type="button" class="vas120-action" data-action="schedule" data-id="' + customer.Id + '" title="' + escapeHtml(scheduleLabel) + '" aria-label="' + escapeHtml(scheduleLabel) + '">' + icon('calendar') + '</button>' +
                        '<button type="button" class="vas120-action" data-action="log" data-id="' + customer.Id + '" title="' + escapeHtml(logLabel) + '" aria-label="' + escapeHtml(logLabel) + '">' + icon('log') + '</button>' +
                    '</span>' +
                    '<span class="vas120-option-chev">' + icon('chevron') + '</span>' +
                '</div>';
            }).join('');

            if (lastTotal > suggestions.length) {
                var moreText = label('VAS_120_SeeAll', 'See all') + ' ' + lastTotal + ' ' + label('VAS_120_Matches', 'matches');
                html += '<div class="vas120-more" role="button" tabindex="0">' + escapeHtml(moreText) + ' ' + icon('arrow') + '</div>';
            }

            $suggest.html(html).addClass('is-open');
            positionSuggest();
            $input.attr('aria-expanded', 'true');
        }

        function renderState(message) {
            suggestions = [];
            suggestionIndex = -1;
            $suggest.html('<div class="vas120-state">' + message + '</div>').addClass('is-open');
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
            }
            else if (event.key === 'ArrowUp') {
                event.preventDefault();
                suggestionIndex = suggestionIndex <= 0 ? suggestions.length - 1 : suggestionIndex - 1;
                renderSuggestions(lastQuery);
            }
        }

        function selectSuggestion(index) {
            var customer = suggestions[index];
            if (!customer) { return; }
            $input.val(customer.Name || customer.Value || '');
            closeSuggestions();
            zoomToCustomer(customer.Id);
        }

        // Resolve the name of the window currently hosting this widget so the
        // framework navigates the current grid in place (no new window) when the
        // widget already sits on the Customers window. Falls back to the Customer
        // window name otherwise.
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

        // Open one C_BPartner record in place (Prompt_Instructions Scenario 1).
        // The id is numeric, so the where clause carries no user text.
        function zoomToCustomer(bpId) {
            if (!bpId) { return; }
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

        // "See all matches": re-filter the host Customers grid to the search set.
        // Only C_BPartner columns are filterable in a single-table where clause;
        // the term is embedded as a SQL literal, so single quotes are doubled to
        // keep the fragment safe.
        function openSeeAll() {
            var term = (lastQuery || $input.val() || '').trim();
            if (!term) { return; }
            closeSuggestions();
            var safe = term.replace(/'/g, "''").toUpperCase();
            var like = "'%" + safe + "%'";
            var where = "C_BPartner.IsCustomer='Y' AND (" +
                "UPPER(C_BPartner.Name) LIKE " + like +
                " OR UPPER(C_BPartner.Value) LIKE " + like +
                " OR UPPER(COALESCE(C_BPartner.EMail,'')) LIKE " + like + ")";
            try {
                $self.widgetFirevalueChanged({
                    "TabWhereClause": where,
                    "TabLayout": "N",
                    "TabIndex": "0",
                    "ActionName": hostWindowName() || CUSTOMER_WINDOW_NAME,
                    "ActionType": "W"
                });
            } catch (e) { /* best-effort */ }
        }

        // Quick action opener. Prefers a host-provided integration hook wired to
        // the real Task / Appointment / Activity windows; otherwise opens the
        // customer record so the action can be created from there. Guarded so a
        // missing target never breaks the dashboard. See integration note.
        function openCustomerActivity(kind, bpId) {
            if (!bpId) { return; }
            if (typeof VAS.VAS_120_openCustomerActivity === 'function') {
                try { VAS.VAS_120_openCustomerActivity(kind, bpId); return; } catch (e) { /* fall through */ }
            }
            zoomToCustomer(bpId);
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
            var ns = '.MPCvas120-' + ($self.AD_UserHomeWidgetID || $self.windowNo || 'widget');
            if (searchTimer) { clearTimeout(searchTimer); }
            $(document).off(ns);
            $(window).off(ns);
            if ($dashboardScroll && $dashboardScroll.length) { $dashboardScroll.off(ns); }
            if ($suggest) { $suggest.remove(); $suggest = null; }
            $root.remove();
        };
    };

    /* Relay a fired value (zoom / drill-through params) to the registered host. */
    VAS.VAS_120_CustomerSearchWidget.prototype.widgetFirevalueChanged = function (value) {
        if (this.listener) { this.listener.widgetFirevalueChanged(value); }
    };

    /* The widget host registers itself here so the widget can drive the host. */
    VAS.VAS_120_CustomerSearchWidget.prototype.addChangeListener = function (listener) {
        this.listener = listener;
    };

    VAS.VAS_120_CustomerSearchWidget.prototype.init = function (windowNo, frame) {
        this.frame = frame;
        this.AD_UserHomeWidgetID = frame.widgetInfo.AD_UserHomeWidgetID;
        this.windowNo = windowNo;
        this.Initalize();
        this.frame.getContentGrid().append(this.getRoot());
    };

    VAS.VAS_120_CustomerSearchWidget.prototype.widgetSizeChange = function (height, width) { };

    VAS.VAS_120_CustomerSearchWidget.prototype.refreshWidget = function () {
        this.refreshWidget();
    };

    VAS.VAS_120_CustomerSearchWidget.prototype.dispose = function () {
        this.disposeComponent();
        if (this.frame) { this.frame.dispose(); }
        this.frame = null;
    };

})(VAS, jQuery);
