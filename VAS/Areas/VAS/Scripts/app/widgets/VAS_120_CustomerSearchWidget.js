/**
 * VAS_120 Customer Search Widget (Customers module dashboard)
 * Purpose - Full-width glass search band with live, debounced auto-suggest across
 *           customers by company name, code, primary contact, e-mail, customer
 *           group (segment) and owner (sales rep). Shows at most seven
 *           relevance-ranked rows; each row has avatar, company, contact/segment/
 *           owner meta, an optional tier tag (2026-08-07: resolved from the
 *           tenant's own AD_Ref_List label for C_BPartner.Rating, so it appears
 *           without any per-tenant code change; a customer with no rating still
 *           shows no tag) and three quick actions (create task, schedule,
 *           log activity). Clicking a row zooms the host Customers window to that
 *           C_BPartner record in place; "See all matches" re-filters the same grid.
 * Design  - Design Specs/dashboard-widgets.md "Full-Width Dashboard Search
 *           Widget". Onfinity glass tokens; internal sizing in em; no inner
 *           scrollbar (autosuggest bounded to seven).
 *           CSS namespaced vas120-* (Prompt_Instructions MPC prefix rule).
 *           2026-08-06 - markup flattened to the shared single glass pill
 *           (icon + input + clear); the nested .vas120-field capsule that made
 *           this widget look unlike VAS_067/078/144/164 was removed.
 *
 * Backend - VAS_120_CustomerSearchWidget/SearchCustomers  (rows + total count)
 *
 * Routing - Record open + See-all use widgetFirevalueChanged (in-place zoom on the
 *           host window, Prompt_Instructions Scenario 1), mirroring VAS_067/VAS_083.
 *           2026-08-17 - that path only exists when the widget sits ON a window. From
 *           the Home / landing dashboard (windowNo < 0) there is no host grid, so the
 *           record (and the See-all set) is opened in the standard Customer window via
 *           the shared VAS.ZoomUtil helper, mirroring VAS_067's Home-page branch.
 * Quick actions - 2026-08-10: the standard-actions rule applies - New Task and
 *           New Appointment hand off to the platform's own forms so the UI and
 *           behaviour match the standard everywhere:
 *             task     -> VIS.AppointmentsForm.init(..., isTask = true)
 *             schedule -> VIS.AppointmentsForm.init(..., isTask = false)
 *           This is the same entry point the VIS toolbar (cmd_task /
 *           cmd_appointment) and the VAS_105 / VAS_123 right panels use. The
 *           widget-local task / appointment dialogs survive only as a fallback for
 *           a host where that form is unavailable (VIS.AppointmentsForm delegates
 *           to the WSP module and reports when it is absent), as do their
 *           SaveTask / SaveAppointment endpoints.
 *           The documented override VAS.VAS_120_openCustomerActivity(kind,
 *           C_BPartner_ID) still wins when an integrator has wired something else.
 *           New Email / New Letter are not actions on this widget; the platform's
 *           standard email entry point is VIS.Email in a VIS.CFrame (see VAS_105),
 *           and "Letter" exists as an activity category rather than its own form.
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
 * 13  | Close                                                     | VAS_120_Close
 * 14  | Cancel                                                    | VAS_120_Cancel
 * 15  | Save                                                      | VAS_120_Save
 * 16  | Call / Note / Meeting / Email                             | VAS_120_TypeCall … TypeEmail
 * 17  | Summary                                                   | VAS_120_Summary
 * 18  | What happened / next step…                                | VAS_120_SummaryPlaceholder
 * 19  | Please enter a summary.                                   | VAS_120_SummaryRequired
 * 21  | Schedule appointment                                      | VAS_120_ScheduleAppointment
 * 22  | Title                                                     | VAS_120_Title
 * 23  | Review                                                    | VAS_120_Review
 * 24  | Date                                                      | VAS_120_Date
 * 25  | Time                                                      | VAS_120_Time
 * 26  | Please enter a title.                                     | VAS_120_TitleRequired
 * 27  | Please pick a date.                                       | VAS_120_DateRequired
 * 28  | Could not schedule the appointment.                       | VAS_120_ScheduleSaveFailed
 * 29  | New task                                                  | VAS_120_NewTask
 * 30  | Task                                                      | VAS_120_Task
 * 31  | What needs to happen?                                     | VAS_120_TaskPlaceholder
 * 32  | Customer                                                  | VAS_120_Customer
 * 33  | Due                                                       | VAS_120_Due
 * 34  | Today / Tomorrow / In 3 days / Next week                  | VAS_120_DueToday … DueNextWeek
 * 35  | Add task                                                  | VAS_120_AddTask
 * 36  | Please enter a task.                                      | VAS_120_TaskRequired
 * 37  | Could not add the task.                                   | VAS_120_TaskSaveFailed
 * ──────────────────────────────────────────────────────────────────────────
 */
; VAS = window.VAS || {};

; (function (VAS, $) {

    // Host window to zoom when the widget is not hosted on the Customers window
    // itself. Documented for admin confirmation (in-place zoom uses the resolved
    // host window name first; this is only the fallback ActionName).
    var CUSTOMER_WINDOW_NAME = "Business Partner";

    // 2026-08-17: zoom target when the widget is NOT hosted inside a window
    // (windowNo < 0 - the Home / landing dashboard). There is no host grid to
    // navigate there, so the Customer window is opened through VAS.ZoomUtil, which
    // resolves the AD_Window_ID from the new name, then the old name, then
    // VAS_ZoomScreenConfig.
    var ZOOM_TABLE = "C_BPartner";
    var ZOOM_WINDOW_NAME_NEW = "VAS_CustomerMaster";
    var ZOOM_WINDOW_NAME_OLD = CUSTOMER_WINDOW_NAME;


    // Own endpoints for the task / appointment popups (AppointmentsInfo).
    var WIDGET_ENDPOINT = "VAS_120_CustomerSearchWidget/";

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
        // AD_Window_ID of the Customer window, resolved once on the first Home-page
        // zoom and reused afterwards (0 = not resolved yet).
        var zoomWindowId = 0;
        // AD_Table_ID of C_BPartner, supplied with the search rows. The standard
        // Task / Appointment forms need the record's table context, and a dashboard
        // widget has no framework-supplied table_ID the way a tab panel does.
        var bpTableId = 0;

        // Quick-action dialog roots + the customer the open dialog acts on.
        var $schedule;
        var $task;
        var currentBpId = 0;
        var currentBpName = '';


        // Relative due dates for the New task popup. The offset (in days) is sent
        // to the server, which resolves it against its own date, so the stored due
        // date never depends on the browser clock or time zone.
        var DUE_OPTIONS = [
            { days: 0, key: 'VAS_120_DueToday', text: 'Today' },
            { days: 1, key: 'VAS_120_DueTomorrow', text: 'Tomorrow' },
            { days: 3, key: 'VAS_120_DueIn3Days', text: 'In 3 days' },
            { days: 7, key: 'VAS_120_DueNextWeek', text: 'Next week' }
        ];

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
            if (name === 'check') {
                return '<svg viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2" stroke-linecap="round" stroke-linejoin="round" aria-hidden="true"><polyline points="20 6 9 17 4 12"></polyline></svg>';
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

        // Tier tag colour class per the Onfinity tier palette. The backend now sends
        // the tenant's own AD_Ref_List label for C_BPartner.Rating, so the three
        // known tiers keep their palette colour (case-insensitively) and any other
        // configured label falls back to the neutral grey tag rather than rendering
        // an uncoloured, near-invisible chip. A customer with no rating still sends
        // no tier at all, so no tag is drawn.
        function tierClass(tier) {
            var name = String(tier || '').trim().toLowerCase();
            if (name === 'platinum') { return 'vas120-tag-violet'; }
            if (name === 'gold') { return 'vas120-tag-amber'; }
            if (name === 'silver') { return 'vas120-tag-info'; }
            return 'vas120-tag-neutral';
        }

        this.Initalize = function () {
            createWidget();
            createSuggestionList();
            createScheduleDialog();
            createTaskDialog();
            bindEvents();
        };

        function createWidget() {
            var placeholder = label('VAS_120_SearchPlaceholder', 'Search customers by company, contact, segment, or owner…');
            var suggestId = 'vas120-suggestions-' + escapeHtml(String($self.windowNo || ''));

            // Single glass pill: icon, input and clear sit directly inside the
            // band — no nested capsule (shared dashboard search-widget design).
            $root.html(
                '<div class="vas120-searchbar">' +
                    '<span class="vas120-search-icon">' + icon('search') + '</span>' +
                    '<input class="vas120-input" type="text" autocomplete="off" role="combobox" aria-autocomplete="list" aria-expanded="false" aria-controls="' + suggestId + '" placeholder="' + escapeHtml(placeholder) + '">' +
                    '<button type="button" class="vas120-clear" aria-label="' + escapeHtml(label('Clear', 'Clear')) + '">' + icon('close') + '</button>' +
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
                // The dialogs show the customer name, so resolve the row before
                // the popover is torn down.
                var customer = suggestions[Number($(this).closest('.vas120-option').attr('data-index'))];
                closeSuggestions();
                openCustomerActivity(kind, bpId, customer ? (customer.Name || customer.Value || '') : '');
            });
            $suggest.on('mousedown', '.vas120-more', function (event) {
                event.preventDefault();
                openSeeAll();
            });

            $(document).on('mousedown' + ns, function (event) {
                if (!$(event.target).closest('.vas120-searchbar, .vas120-suggest, .vas120-modal').length) {
                    closeSuggestions();
                }
            });
            // Escape dismisses the topmost surface: an open dialog first, then the
            // suggestion popover.
            $(document).on('keydown' + ns, function (event) {
                if (event.key !== 'Escape') { return; }
                var $open = openModal();
                if ($open) { closeDialog($open); }
                else { closeSuggestions(); }
            });

            // A dashboard scroll must not tear a modal down, so only the popover
            // reacts to scrolling.
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
                    if (parsed.BPartnerTableId) { bpTableId = Number(parsed.BPartnerTableId); }
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

        // Open the Customer window on a where clause when the widget is NOT hosted on a
        // window (Home / landing page). The window id is resolved from its name and the
        // clause is handed to the standard window as a query restriction, so the grid
        // lands on the same set the host grid would have shown. Best-effort: an
        // unresolved window or an unavailable framework simply does not navigate.
        function openCustomerWindowWhere(whereClause) {
            if (!window.VAS || !VAS.ZoomUtil) { return; }
            VAS.ZoomUtil.getWindowId(ZOOM_WINDOW_NAME_NEW, ZOOM_WINDOW_NAME_OLD)
                .done(function (id) {
                    id = Number(id) || 0;
                    if (id <= 0) { return; }
                    zoomWindowId = id;
                    if (!window.VIS || !VIS.viewManager || typeof VIS.viewManager.startWindow !== 'function') { return; }

                    var query = null;
                    try {
                        if (typeof VIS.Query === 'function') {
                            query = new VIS.Query(ZOOM_TABLE);
                            query.addRestriction(whereClause);
                        }
                    } catch (e) { query = null; }

                    try { VIS.viewManager.startWindow(id, query); } catch (e) { /* best-effort */ }
                });
        }

        // Open one C_BPartner record in place (Prompt_Instructions Scenario 1).
        // The id is numeric, so the where clause carries no user text.
        // 2026-08-17: on the Home / landing page (windowNo < 0) there is no host grid
        // to navigate, so the record is opened in the standard Customer window.
        function zoomToCustomer(bpId) {
            if (!bpId) { return; }
            try {
                if ($self.windowNo >= 0) {
                    $self.widgetFirevalueChanged({
                        "TabWhereClause": "C_BPartner.C_BPartner_ID=" + Number(bpId),
                        "TabLayout": "Y",
                        "TabIndex": "0",
                        "ActionName": hostWindowName() || CUSTOMER_WINDOW_NAME,
                        "ActionType": "W"
                    });
                }
                else {
                    VAS.ZoomUtil.zoomToRecord("C_BPartner_ID", Number(bpId), zoomWindowId, ZOOM_WINDOW_NAME_NEW, ZOOM_WINDOW_NAME_OLD)
                        .done(function (id) {
                            if (id > 0) { zoomWindowId = id; }
                        });
                }
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
                if ($self.windowNo >= 0) {
                    $self.widgetFirevalueChanged({
                        "TabWhereClause": where,
                        "TabLayout": "N",
                        "TabIndex": "0",
                        "ActionName": hostWindowName() || CUSTOMER_WINDOW_NAME,
                        "ActionType": "W"
                    });
                }
                else {
                    /* Home / landing page: open the standard Customer window on the same set. */
                    openCustomerWindowWhere(where);
                }
            } catch (e) { /* best-effort */ }
        }

        // Quick action opener. A host-provided integration hook still wins when it
        // is wired to the real Task / Appointment / Activity windows; otherwise the
        // widget's own dialog handles the action inline and saves it.
        // Standard-actions rule: New Task and New Appointment must hand off to the
        // platform's own forms, so the UI and behaviour match the standard everywhere
        // - never a widget-local re-implementation. VIS.AppointmentsForm.init is the
        // same entry point the VIS toolbar (cmd_task / cmd_appointment) and the
        // VAS_105 / VAS_123 right panels already use; its 5th argument selects task
        // (true) over appointment (false).
        function openStandardAppointmentForm(bpId, isTask) {
            if (!window.VIS || !VIS.AppointmentsForm || typeof VIS.AppointmentsForm.init !== 'function') { return false; }
            if (!bpTableId) { return false; }

            var userId = (VIS.context && typeof VIS.context.getAD_User_ID === 'function') ? VIS.context.getAD_User_ID() : 0;
            var userName = (VIS.context && typeof VIS.context.getAD_UserName === 'function') ? VIS.context.getAD_UserName() : '';
            VIS.AppointmentsForm.init(bpTableId, bpId, userId, userName, isTask);
            return true;
        }

        function openCustomerActivity(kind, bpId, name) {
            if (!bpId) { return; }
            if (typeof VAS.VAS_120_openCustomerActivity === 'function') {
                try { VAS.VAS_120_openCustomerActivity(kind, bpId); return; } catch (e) { /* fall through */ }
            }

            currentBpId = bpId;
            currentBpName = name || '';

            // The built-in dialogs remain only as a fallback for a host page where the
            // platform form is unavailable (e.g. the WSP module is not deployed, which
            // VIS.AppointmentsForm reports itself).
            if (kind === 'task') {
                if (!openStandardAppointmentForm(bpId, true)) { openTaskDialog(); }
            } else if (kind === 'schedule') {
                if (!openStandardAppointmentForm(bpId, false)) { openScheduleDialog(); }
            }
        }

        /* ── Quick-action dialogs ────────────────────────────────────────────
         * One shared modal shell (scrim + panel + head/body/foot) so the three
         * popups differ only in their form body and primary button, matching the
         * dialog chrome the sibling Customers widgets already use.
         * ------------------------------------------------------------------ */

        // Field ids are per-widget-instance so two dashboards hosting this widget
        // never collide on a <label for>.
        function fieldId(name) {
            return 'vas120-' + name + '-' + String($self.AD_UserHomeWidgetID || $self.windowNo || '0');
        }

        function buildDialog(modifier, titleText, bodyHtml, confirmText) {
            var $dialog = $(
                '<div class="vas120-modal ' + modifier + '" role="dialog" aria-modal="true" aria-hidden="true" aria-label="' + escapeHtml(titleText) + '">' +
                    '<div class="vas120-scrim" data-vas120-close></div>' +
                    '<section class="vas120-panel">' +
                        '<header class="vas120-mhead">' +
                            '<h2 class="vas120-mtitle">' + escapeHtml(titleText) + '</h2>' +
                            '<span class="vas120-mhead-right">' +
                                '<span class="vas120-mcust"></span>' +
                                '<button type="button" class="vas120-mclose" data-vas120-close aria-label="' + escapeHtml(label('VAS_120_Close', 'Close')) + '">' + icon('close') + '</button>' +
                            '</span>' +
                        '</header>' +
                        '<div class="vas120-mbody">' + bodyHtml +
                            '<div class="vas120-merror" role="alert"></div>' +
                        '</div>' +
                        '<footer class="vas120-mfoot">' +
                            '<button type="button" class="vas120-btn vas120-btn-ghost" data-vas120-close>' + escapeHtml(label('VAS_120_Cancel', 'Cancel')) + '</button>' +
                            '<button type="button" class="vas120-btn vas120-btn-primary" data-vas120-save>' + icon('check') + escapeHtml(confirmText) + '</button>' +
                        '</footer>' +
                    '</section>' +
                '</div>'
            );

            $('body').append($dialog);
            $dialog.find('.vas120-merror').hide();
            $dialog.on('click', '[data-vas120-close]', function () { closeDialog($dialog); });
            return $dialog;
        }

        function openDialog($dialog) {
            $dialog.find('.vas120-mcust').text(currentBpName);
            showError($dialog, '');
            setBusy($dialog, false);
            $dialog.addClass('is-open').attr('aria-hidden', 'false');
            $('body').addClass('vas120-modal-open');
        }

        function closeDialog($dialog) {
            if (!$dialog) { return; }
            // Blur first: an aria-hidden container must not keep focus.
            if (document.activeElement && $dialog[0].contains(document.activeElement)) { document.activeElement.blur(); }
            $dialog.removeClass('is-open').attr('aria-hidden', 'true');
            if (!openModal()) { $('body').removeClass('vas120-modal-open'); }
        }

        // The single dialog currently on screen, or null. Only one can be open.
        function openModal() {
            if ($schedule && $schedule.hasClass('is-open')) { return $schedule; }
            if ($task && $task.hasClass('is-open')) { return $task; }
            return null;
        }

        function showError($dialog, message) {
            var $error = $dialog.find('.vas120-merror');
            if (!message) { $error.hide().text(''); return; }
            $error.text(message).show();
        }

        function setBusy($dialog, isBusy) {
            $dialog.find('[data-vas120-save]').prop('disabled', !!isBusy);
        }

        function focusField($dialog, field) {
            window.setTimeout(function () { $dialog.find('[data-field="' + field + '"]').focus(); }, 0);
        }

        // Posts one quick action and closes the dialog on success. All three
        // endpoints answer { success:true, … } or { error:"…" }.
        function saveAction($dialog, endpoint, data, failText) {
            setBusy($dialog, true);
            showError($dialog, '');

            $.ajax({
                url: VIS.Application.contextUrl + endpoint,
                type: 'POST',
                data: data,
                success: function (response) {
                    setBusy($dialog, false);
                    var parsed = parseResponse(response);
                    if (parsed && parsed.error) { showError($dialog, parsed.error); return; }
                    if (!parsed || !parsed.success) { showError($dialog, failText); return; }
                    closeDialog($dialog);
                },
                error: function () {
                    setBusy($dialog, false);
                    showError($dialog, failText);
                }
            });
        }

        /* ---------- Schedule appointment (title + date + time) --------------- */

        function createScheduleDialog() {
            var titleFieldId = fieldId('apptitle');
            var dateId = fieldId('apdate');
            var timeId = fieldId('aptime');

            var body =
                '<div class="vas120-field">' +
                    '<label class="vas120-flabel" for="' + titleFieldId + '">' + escapeHtml(label('VAS_120_Title', 'Title')) + '</label>' +
                    '<input type="text" class="vas120-finput" id="' + titleFieldId + '" data-field="title" autocomplete="off">' +
                '</div>' +
                '<div class="vas120-frow">' +
                    '<div class="vas120-field">' +
                        '<label class="vas120-flabel" for="' + dateId + '">' + escapeHtml(label('VAS_120_Date', 'Date')) + '</label>' +
                        '<input type="date" class="vas120-finput" id="' + dateId + '" data-field="date">' +
                    '</div>' +
                    '<div class="vas120-field">' +
                        '<label class="vas120-flabel" for="' + timeId + '">' + escapeHtml(label('VAS_120_Time', 'Time')) + '</label>' +
                        '<input type="time" class="vas120-finput" id="' + timeId + '" data-field="time">' +
                    '</div>' +
                '</div>';

            $schedule = buildDialog('vas120-modal-schedule', label('VAS_120_ScheduleAppointment', 'Schedule appointment'), body, label('VAS_120_Schedule', 'Schedule'));
            $schedule.on('click', '[data-vas120-save]', saveAppointment);
        }

        function openScheduleDialog() {
            var suggested = label('VAS_120_Review', 'Review') + (currentBpName ? ' — ' + currentBpName : '');
            $schedule.find('[data-field="title"]').val(suggested);
            $schedule.find('[data-field="date"]').val('');
            $schedule.find('[data-field="time"]').val('');
            openDialog($schedule);
            focusField($schedule, 'date');
        }

        function saveAppointment() {
            var title = ($schedule.find('[data-field="title"]').val() || '').trim();
            var date = $schedule.find('[data-field="date"]').val() || '';
            var time = $schedule.find('[data-field="time"]').val() || '';

            if (!title) {
                showError($schedule, label('VAS_120_TitleRequired', 'Please enter a title.'));
                return;
            }
            if (!date) {
                showError($schedule, label('VAS_120_DateRequired', 'Please pick a date.'));
                return;
            }

            // date/time carry the input elements' ISO values (yyyy-MM-dd / HH:mm),
            // which are locale-independent, so the server parses them exactly.
            saveAction($schedule, WIDGET_ENDPOINT + 'SaveAppointment',
                { C_BPartner_ID: currentBpId, title: title, date: date, time: time },
                label('VAS_120_ScheduleSaveFailed', 'Could not schedule the appointment.'));
        }

        /* ---------- New task (task + customer + due) ------------------------- */

        function createTaskDialog() {
            var subjectId = fieldId('tasksubject');
            var customerId = fieldId('taskcustomer');
            var dueId = fieldId('taskdue');

            var dueOptions = DUE_OPTIONS.map(function (option) {
                return '<option value="' + option.days + '">' + escapeHtml(label(option.key, option.text)) + '</option>';
            }).join('');

            var body =
                '<div class="vas120-field">' +
                    '<label class="vas120-flabel" for="' + subjectId + '">' + escapeHtml(label('VAS_120_Task', 'Task')) + '</label>' +
                    '<input type="text" class="vas120-finput" id="' + subjectId + '" data-field="subject" autocomplete="off" placeholder="' + escapeHtml(label('VAS_120_TaskPlaceholder', 'What needs to happen?')) + '">' +
                '</div>' +
                '<div class="vas120-frow">' +
                    '<div class="vas120-field">' +
                        '<label class="vas120-flabel" for="' + customerId + '">' + escapeHtml(label('VAS_120_Customer', 'Customer')) + '</label>' +
                        '<input type="text" class="vas120-finput" id="' + customerId + '" data-field="customer" readonly>' +
                    '</div>' +
                    '<div class="vas120-field">' +
                        '<label class="vas120-flabel" for="' + dueId + '">' + escapeHtml(label('VAS_120_Due', 'Due')) + '</label>' +
                        '<select class="vas120-fselect" id="' + dueId + '" data-field="due">' + dueOptions + '</select>' +
                    '</div>' +
                '</div>';

            $task = buildDialog('vas120-modal-task', label('VAS_120_NewTask', 'New task'), body, label('VAS_120_AddTask', 'Add task'));
            $task.on('click', '[data-vas120-save]', saveTask);
        }

        function openTaskDialog() {
            $task.find('[data-field="subject"]').val('');
            $task.find('[data-field="customer"]').val(currentBpName);
            $task.find('[data-field="due"]').val(String(DUE_OPTIONS[0].days));
            openDialog($task);
            focusField($task, 'subject');
        }

        function saveTask() {
            var subject = ($task.find('[data-field="subject"]').val() || '').trim();
            if (!subject) {
                showError($task, label('VAS_120_TaskRequired', 'Please enter a task.'));
                return;
            }

            saveAction($task, WIDGET_ENDPOINT + 'SaveTask',
                { C_BPartner_ID: currentBpId, subject: subject, dueDays: Number($task.find('[data-field="due"]').val() || 0) },
                label('VAS_120_TaskSaveFailed', 'Could not add the task.'));
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
            // The dialogs live on <body>, so they must be torn down explicitly.
            if ($schedule) { $schedule.remove(); $schedule = null; }
            if ($task) { $task.remove(); $task = null; }
            $('body').removeClass('vas120-modal-open');
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
