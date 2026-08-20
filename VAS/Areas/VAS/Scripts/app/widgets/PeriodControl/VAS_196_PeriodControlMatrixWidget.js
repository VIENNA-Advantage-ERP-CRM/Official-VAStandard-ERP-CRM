/**
 * VAS_196_PeriodControlMatrixWidget
 * 3x2 grid widget for the Period Control dashboard.
 *
 * Cascading Calendar -> Year -> Period selectors above a matrix of the active
 * C_PeriodControl rows of the selected period: one row per document base type,
 * its current PeriodStatus, and the single action that status allows.
 *
 *   PeriodStatus | Display            | Button   | PeriodAction sent
 *   -------------|--------------------|----------|------------------
 *   O            | Open               | Close    | C
 *   C            | Closed             | Open     | O
 *   N            | Never Opened       | Open     | O
 *   P            | Permanently Closed | disabled | none
 *
 * The widget never writes PeriodStatus. Clicking an action asks for
 * confirmation, posts to the server, and repaints the row from the status the
 * server RE-READ after the standard open/close process finished - a failed run
 * leaves the row exactly as it was.
 *
 * The header link (top right) opens the period-level dialog: the same standard
 * process the framework runs from C_Period.Processing, with its own three
 * parameters - Organization and Document BaseType as multi-select lookups, and
 * Period Action. It opens with its parameters cleared, OK runs the process and
 * closes it, and the matrix is re-read afterwards. While it is open the card
 * behind it takes no input, so the period cannot change under the run. Like the
 * filter popover it has no outside-click dismisser and no modal overlay:
 * clicking elsewhere on the dashboard leaves it where it is and blocks nothing
 * outside this widget.
 *
 * Sizing follows design.md: the card carries the widget root anchor clamp, the
 * header reads --dash-inline-size (populated by ensureDashInlineSizeVar), rows
 * sit one step below the title, and the list never scrolls - the visible row
 * count is measured from the list's height at runtime and the rest is paged
 * through the canonical footer pager.
 *
 * Summary Message Table
 * Rows marked (reuse) already exist in the project under another key and are
 * NOT duplicated here.
 *  # | Current Text                            | Message Key
 * ---+-----------------------------------------+---------------------------------
 *  1 | Period Control Matrix                   | VAS_196_PeriodControlMatrix
 *  2 | By document base type                   | VAS_196_ByDocBaseType
 *  3 | Calendar                                | VAS_196_Calendar
 *  4 | Year                                    | VAS_196_Year
 *  5 | Period                                  | VAS_192_Period            (reuse)
 *  6 | Open                                    | VAS_192_Open              (reuse)
 *  7 | Closed                                  | VAS_192_Closed            (reuse)
 *  8 | Never Opened                            | VAS_192_NeverOpened       (reuse)
 *  9 | Permanently Closed                      | VAS_192_PermanentlyClosed (reuse)
 * 10 | Close                                   | VAS_018_Close             (reuse)
 * 11 | for                                     | VAS_196_For
 * 12 | Confirm                                 | VAS_062_Confirm           (reuse)
 * 13 | This period is permanently closed and cannot be reopened. | VAS_196_PermClosedHint
 * 14 | No period controls configured for this period.            | VAS_196_NoControls
 * 14a| Organization                            | VAS_196_Organization
 * 14b| Document Base Type                      | VAS_196_DocBaseType
 * 14c| No period controls match the filter.    | VAS_196_NoMatches
 * 14d| Clear                                   | VIS_Clear                 (reuse)
 * 14e| Apply                                   | VIS_Apply                 (reuse)
 * 14f| Filter                                  | VAS_034_Filter            (reuse)
 * 14g| Open / Close Period                     | VAS_196_OpenClosePeriod
 * 14h| Period Action                           | VAS_196_PeriodAction
 * 14i| Open Period                             | VAS_196_OpenPeriod
 * 14j| Close Period                            | VAS_196_ClosePeriod
 * 14k| Permanently Close Period                | VAS_196_PermanentlyClosePeriod
 * 14l| Select at least one organization.       | VAS_196_SelectOrganization
 * 14m| Select a period first.                  | VAS_196_SelectPeriod
 * 14n| Could not run the period process.       | VAS_196_ProcessRunFailed
 * 14o| OK                                      | OK                        (reuse)
 * 14p| Cancel                                  | Cancel                    (reuse)
 * 15 | Could not change the period status.     | VAS_196_ChangeFailed
 * 16 | The open/close process is not configured. | VAS_196_NoProcess
 * 17 | The selected period is no longer available. | VAS_196_InvalidSelection
 * 18 | Couldn't load                           | VAS_192_CouldntLoad       (reuse)
 * 19 | Showing                                 | VAS_026_Showing           (reuse)
 * 20 | of                                      | VAS_026_Of                (reuse)
 * 21 | Previous                                | VAS_026_Prev              (reuse)
 * 22 | Next                                    | VAS_026_Next              (reuse)
 */
; VAS = window.VAS || {};

; (function (VAS, $) {

    /* Keep --dash-inline-size on :root equal to the dashboard container's current
       pixel width so the header clamps resolve against the dashboard's visible
       width, not the viewport. One document-level observer serves every widget. */
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

    /* C_PeriodControl.PeriodStatus stored codes (list reference) -> display label,
       badge tone and the action the row offers. Kept in lock-step with
       VASLogic.Models.VAS_196_PeriodControlMatrixModel. */
    var STATUS_MAP = {
        'O': { key: 'VAS_192_Open', text: 'Open', tone: 'ok', actionKey: 'VAS_018_Close', actionText: 'Close', actionTone: 'close' },
        'C': { key: 'VAS_192_Closed', text: 'Closed', tone: 'plain', actionKey: 'VAS_192_Open', actionText: 'Open', actionTone: 'open' },
        'N': { key: 'VAS_192_NeverOpened', text: 'Never Opened', tone: 'fail', actionKey: 'VAS_192_Open', actionText: 'Open', actionTone: 'open' },
        'P': { key: 'VAS_192_PermanentlyClosed', text: 'Permanently Closed', tone: 'dark', actionKey: '', actionText: '', actionTone: '' }
    };

    /* The three actions VAdvantage.Process.PeriodStatus understands, in the order
       the standard process dialog offers them. Stored codes, kept in lock-step with
       MPeriodControl.PERIODACTION_*. */
    var PERIOD_ACTIONS = [
        { code: 'O', key: 'VAS_196_OpenPeriod', text: 'Open Period' },
        { code: 'C', key: 'VAS_196_ClosePeriod', text: 'Close Period' },
        { code: 'P', key: 'VAS_196_PermanentlyClosePeriod', text: 'Permanently Close Period' }
    ];

    /* Server error tokens -> message key + inline default. */
    var ERROR_MAP = {
        'INVALID': { key: 'VAS_196_InvalidSelection', text: 'The selected period is no longer available.' },
        'INVALIDACTION': { key: 'VAS_196_InvalidSelection', text: 'The selected period is no longer available.' },
        'NOTFOUND': { key: 'VAS_196_InvalidSelection', text: 'The selected period is no longer available.' },
        'PERMCLOSED': { key: 'VAS_196_PermClosedHint', text: 'This period is permanently closed and cannot be reopened.' },
        'NOPROCESS': { key: 'VAS_196_NoProcess', text: 'The open/close process is not configured.' },
        'SAVEFAILED': { key: 'VAS_196_ChangeFailed', text: 'Could not change the period status.' },
        'PROCESSFAILED': { key: 'VAS_196_ChangeFailed', text: 'Could not change the period status.' }
    };

    var MIN_ROWS = 1;          // never force more rows than physically fit
    var ROW_FALLBACK = 34;     // px, used only before a real row has been measured

    VAS.VAS_196_PeriodControlMatrixWidget = function () {

        this.frame;
        this.windowNo;

        var $self = this;
        var $root = $('<div class="vas-196-root">');
        var $card;
        var $list;
        var $pager;
        var $selCal;
        var $selYear;
        var $selPeriod;
        var $filterBtn;
        var $busy;

        /* Filter popover (lives on <body>, so it is NOT inside $root - always address
           it through these references, never through $root.find). Built once on first
           open and kept, so the two VIS lookup controls hold their selection between
           openings. Non-modal: it never blocks the rest of the dashboard. */
        var $filterPopup = null;
        var $orgField = null;
        var vOrgCtrl = null;
        var vDocTypeCtrl = null;
        var _popupOpen = false;

        /* Period process popover - the header link's own panel. Same mechanics as the
           filter popover (body-appended, non-modal, anchored to its own button, no
           outside-click dismisser) and the same per-instance namespace, so it belongs
           to this widget alone and leaves every other dashboard card alone. It runs
           the standard process on C_Period.Processing and carries exactly the three
           parameters that process declares. */
        var $processPopup = null;
        var $processBtn = null;
        var vProcOrgCtrl = null;
        var vProcDocCtrl = null;
        var $procAction = null;
        var _processPopupOpen = false;

        /* Per-instance event namespace - a dashboard can hold two of this widget, and
           each must unbind only its own document/window dismissers. */
        var _ns = '.vas196_' + (VAS.VAS_196_PeriodControlMatrixWidget._seq =
            (VAS.VAS_196_PeriodControlMatrixWidget._seq || 0) + 1);

        /* One namespace per popover: closing one must never unbind the other's
           dismissers, even though only one of them is ever open at a time. */
        var _nsFilter = _ns + 'f';
        var _nsProcess = _ns + 'p';

        /* Current selection (echoed back on every status change so the server can
           re-validate the whole hierarchy). */
        var _calendarId = 0;
        var _yearId = 0;
        var _periodId = 0;
        var _periodName = '';

        /* Client-side paging over the full control set. The set is bounded by the
           number of document base types, so it is fetched whole and paged here -
           a page change costs no round trip. Page size is ADAPTIVE: measured from
           the list's available height, re-measured by a ResizeObserver. */
        var _rows = [];
        /* Set-wide, not per row: the organization is only worth a column when the
           tenant actually maintains period control per org. If every control of the
           period is tenant-wide (AD_Org_ID = 0) the column would repeat "*" on every
           row, so it is dropped entirely. */
        var _showOrg = false;

        /* Committed filter terms (lower-cased). They are applied by Apply / Enter /
           Clear only - typing alone does not repaint, so the buttons stay meaningful.
           _view is the filtered projection of _rows that the pager and the list work
           against; _rows always stays the full set the server returned. */
        var _view = [];

        /* Committed filter selection, as the ids the lookups return. null = the field
           was left empty, so that dimension is not filtered at all. 0 is a REAL id
           here - the tenant-wide '*' organization - so it must never be treated as
           "nothing picked": picking '*' filters the matrix down to the tenant-wide
           controls. */
        var _filterOrgId = null;
        var _filterDocTypeId = null;

        var _page = 1;
        var _pageSize = 5;
        var _rowH = 0;
        var _needsSync = true;
        var _observer = null;
        var _busyCount = 0;

        function label(key, fallback) {
            var translated = VIS.Msg.getMsg(key);
            return (translated && translated.charAt(0) !== '[' && translated !== key) ? translated : fallback;
        }

        function escapeHtml(value) {
            return String(value == null ? "" : value)
                .replace(/&/g, "&amp;")
                .replace(/</g, "&lt;")
                .replace(/>/g, "&gt;")
                .replace(/"/g, "&quot;")
                .replace(/'/g, "&#039;");
        }

        function parseResponse(res) {
            var data = res;
            if (typeof data === 'string') { data = JSON.parse(data); }
            if (typeof data === 'string') { data = JSON.parse(data); }
            return data;
        }

        /* Reference-counted so overlapping requests can't unhide the overlay early. */
        function showBusy(show) {
            _busyCount = Math.max(0, _busyCount + (show ? 1 : -1));
            if (!$busy || !$busy[0]) { return; }
            $busy.toggleClass('vas-196-hidden', _busyCount === 0);
        }

        function showError(text) {
            if (VIS && VIS.ADialog && VIS.ADialog.error) { VIS.ADialog.error("", "", text); }
        }

        function showInfo(text) {
            if (VIS && VIS.ADialog && VIS.ADialog.info) { VIS.ADialog.info("", "", text); }
        }

        // ── Build ────────────────────────────────────────────────────────────

        this.Initalize = function () {
            createWidget();
            setupResizeObserver();
            loadBootstrap();
        };

        function createWidget() {
            var title = label('VAS_196_PeriodControlMatrix', 'Period Control Matrix');
            var subtitle = label('VAS_196_ByDocBaseType', 'By document base type');
            var filterLabel = label('VAS_034_Filter', 'Filter');
            var processLabel = label('VAS_196_OpenClosePeriod', 'Open / Close Period');

            $card = $(
                '<div class="vas-196-card vas-widget-bg" role="group" aria-label="' + escapeHtml(title) + '">' +
                    '<div class="vas-196-header">' +
                        '<span class="vas-196-icon">' +
                            '<svg viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2"' +
                                ' stroke-linecap="round" stroke-linejoin="round">' +
                                '<rect x="3" y="3" width="7" height="7" rx="1"/>' +
                                '<rect x="14" y="3" width="7" height="7" rx="1"/>' +
                                '<rect x="3" y="14" width="7" height="7" rx="1"/>' +
                                '<rect x="14" y="14" width="7" height="7" rx="1"/>' +
                            '</svg>' +
                        '</span>' +
                        '<div class="vas-196-head-text">' +
                            '<div class="vas-196-title" title="' + escapeHtml(title) + '">' + escapeHtml(title) + '</div>' +
                            '<div class="vas-196-subtitle" title="' + escapeHtml(subtitle) + '">' + escapeHtml(subtitle) + '</div>' +
                        '</div>' +
                        /* Header link, top right: opens the period process dialog. A
                           link and not a button - it is a route into the standard
                           process, not one of the matrix's own row actions. */
                        '<button type="button" class="vas-196-headlink" aria-haspopup="dialog"' +
                            ' title="' + escapeHtml(processLabel) + '">' + escapeHtml(processLabel) + '</button>' +
                    '</div>' +
                    '<div class="vas-196-body">' +
                        '<div class="vas-196-filters">' +
                            '<select class="vas-196-sel vas-196-sel-cal" aria-label="' +
                                escapeHtml(label('VAS_196_Calendar', 'Calendar')) + '"></select>' +
                            '<select class="vas-196-sel vas-196-sel-year" aria-label="' +
                                escapeHtml(label('VAS_196_Year', 'Year')) + '"></select>' +
                            '<select class="vas-196-sel vas-196-sel-period" aria-label="' +
                                escapeHtml(label('VAS_192_Period', 'Period')) + '"></select>' +
                            /* Funnel: opens the filter dialog. Keeping the lookups in a
                               dialog leaves the whole card height to the matrix. */
                            '<button type="button" class="vas-196-filter-btn" aria-haspopup="dialog"' +
                                ' aria-label="' + escapeHtml(filterLabel) + '"' +
                                ' title="' + escapeHtml(filterLabel) + '">' +
                                '<svg viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2"' +
                                    ' stroke-linecap="round" stroke-linejoin="round">' +
                                    '<polygon points="22 3 2 3 10 12.46 10 19 14 21 14 12.46 22 3"></polygon>' +
                                '</svg>' +
                            '</button>' +
                        '</div>' +
                        '<div class="vas-196-list"></div>' +
                        '<div class="vas-196-pager"></div>' +
                    '</div>' +
                '</div>'
            );

            $list = $card.find('.vas-196-list');
            $pager = $card.find('.vas-196-pager');
            $selCal = $card.find('.vas-196-sel-cal');
            $selYear = $card.find('.vas-196-sel-year');
            $selPeriod = $card.find('.vas-196-sel-period');
            $filterBtn = $card.find('.vas-196-filter-btn');
            $filterBtn.on('click', function (e) {
                e.stopPropagation();
                toggleFilterPopup();
            });

            $processBtn = $card.find('.vas-196-headlink');
            $processBtn.on('click', function (e) {
                e.stopPropagation();
                toggleProcessPopup();
            });

            /* Cascade: a calendar change clears year, period and the matrix; a year
               change clears period and the matrix. Nothing is kept from the previous
               branch of the hierarchy. */
            $selCal.on('change', function () {
                _calendarId = parseInt($(this).val(), 10) || 0;
                _yearId = 0; _periodId = 0; _periodName = '';
                fillOptions($selYear, []);
                fillOptions($selPeriod, []);
                setRows([]);
                if (_calendarId > 0) { loadYears(_calendarId, 0); }
            });

            $selYear.on('change', function () {
                _yearId = parseInt($(this).val(), 10) || 0;
                _periodId = 0; _periodName = '';
                fillOptions($selPeriod, []);
                setRows([]);
                if (_yearId > 0) { loadPeriods(_yearId, 0); }
            });

            $selPeriod.on('change', function () {
                _periodId = parseInt($(this).val(), 10) || 0;
                _periodName = $(this).find('option:selected').text();
                setRows([]);
                if (_periodId > 0) { loadControls(_periodId); }
            });

            /* Delegated so the handler survives every repaint of the list. */
            $list.on('click', '.vas-196-btn', function () {
                var $btn = $(this);
                if ($btn.prop('disabled')) { return; }
                onActionClick(parseInt($btn.attr('data-id'), 10) || 0);
            });

            $root.append($card);

            $busy = $('<div class="vas-196-busy vas-196-hidden"><div class="vis-busyindicatorinnerwrap"><i class="vis_widgetloader"></i></div></div>');
            $root.append($busy);
        }

        /* Publishes the widget's own measured width so the card anchor can scale on
           the widget instead of the whole dashboard when the cell is unusual. */
        function setupResizeObserver() {
            if (typeof ResizeObserver === 'undefined') { return; }
            try {
                var ro = new ResizeObserver(function (entries) {
                    for (var i = 0; i < entries.length; i++) {
                        var width = entries[i].contentRect.width;
                        if (width > 0 && $root[0]) {
                            $root[0].style.setProperty('--widget-inline-size', width + 'px');
                        }
                    }
                });
                ro.observe($root[0]);
            } catch (e) { }
        }

        // ── Cascading loads ──────────────────────────────────────────────────

        function loadBootstrap() {
            showBusy(true);
            $.ajax({
                url: VIS.Application.contextUrl + 'VAS_196_PeriodControlMatrixWidget/GetBootstrap',
                type: 'GET',
                cache: false,
                success: function (res) {
                    var data = null;
                    try { data = parseResponse(res); } catch (e) { }
                    if (!data || data.error) { renderState(label('VAS_192_CouldntLoad', "Couldn't load"), true); return; }

                    fillOptions($selCal, data.Calendars);
                    fillOptions($selYear, data.Years);
                    fillOptions($selPeriod, data.Periods);

                    _calendarId = data.C_Calendar_ID || 0;
                    _yearId = data.C_Year_ID || 0;
                    _periodId = data.C_Period_ID || 0;

                    if (_calendarId > 0) { $selCal.val(String(_calendarId)); }
                    if (_yearId > 0) { $selYear.val(String(_yearId)); }
                    if (_periodId > 0) {
                        $selPeriod.val(String(_periodId));
                        _periodName = $selPeriod.find('option:selected').text();
                        loadControls(_periodId);
                    } else {
                        setRows([]);
                    }
                },
                error: function () { renderState(label('VAS_192_CouldntLoad', "Couldn't load"), true); },
                complete: function () { showBusy(false); }
            });
        }

        function loadYears(calendarId, preselectId) {
            showBusy(true);
            $.ajax({
                url: VIS.Application.contextUrl + 'VAS_196_PeriodControlMatrixWidget/GetYears',
                type: 'GET',
                cache: false,
                data: { calendarId: calendarId },
                success: function (res) {
                    var data = null;
                    try { data = parseResponse(res); } catch (e) { }
                    if (!data || data.error) { renderState(label('VAS_192_CouldntLoad', "Couldn't load"), true); return; }

                    fillOptions($selYear, data);
                    _yearId = preselectId > 0 ? preselectId : (data.length > 0 ? data[0].Id : 0);
                    if (_yearId > 0) {
                        $selYear.val(String(_yearId));
                        loadPeriods(_yearId, 0);
                    }
                },
                error: function () { renderState(label('VAS_192_CouldntLoad', "Couldn't load"), true); },
                complete: function () { showBusy(false); }
            });
        }

        function loadPeriods(yearId, preselectId) {
            showBusy(true);
            $.ajax({
                url: VIS.Application.contextUrl + 'VAS_196_PeriodControlMatrixWidget/GetPeriods',
                type: 'GET',
                cache: false,
                data: { yearId: yearId },
                success: function (res) {
                    var data = null;
                    try { data = parseResponse(res); } catch (e) { }
                    if (!data || data.error) { renderState(label('VAS_192_CouldntLoad', "Couldn't load"), true); return; }

                    fillOptions($selPeriod, data);
                    _periodId = preselectId > 0 ? preselectId : (data.length > 0 ? data[0].Id : 0);
                    if (_periodId > 0) {
                        $selPeriod.val(String(_periodId));
                        _periodName = $selPeriod.find('option:selected').text();
                        loadControls(_periodId);
                    } else {
                        setRows([]);
                    }
                },
                error: function () { renderState(label('VAS_192_CouldntLoad', "Couldn't load"), true); },
                complete: function () { showBusy(false); }
            });
        }

        function loadControls(periodId) {
            showBusy(true);
            $.ajax({
                url: VIS.Application.contextUrl + 'VAS_196_PeriodControlMatrixWidget/GetPeriodControls',
                type: 'GET',
                cache: false,
                data: { periodId: periodId },
                success: function (res) {
                    var data = null;
                    try { data = parseResponse(res); } catch (e) { }
                    if (!data || data.error) { renderState(label('VAS_192_CouldntLoad', "Couldn't load"), true); return; }
                    /* A late response for a period the user has already moved away
                       from must not overwrite the current matrix. */
                    if (periodId !== _periodId) { return; }
                    setRows(data);
                },
                error: function () { renderState(label('VAS_192_CouldntLoad', "Couldn't load"), true); },
                complete: function () { showBusy(false); }
            });
        }

        function fillOptions($select, items) {
            var html = '';
            for (var i = 0; items && i < items.length; i++) {
                html += '<option value="' + items[i].Id + '">' + escapeHtml(items[i].Name) + '</option>';
            }
            $select.html(html);
            $select.prop('disabled', !items || items.length === 0);
        }

        // ── Matrix rendering ─────────────────────────────────────────────────

        function setRows(rows) {
            _rows = rows || [];
            _page = 1;
            _needsSync = true;

            _showOrg = false;
            for (var i = 0; i < _rows.length; i++) {
                if (_rows[i].AD_Org_ID > 0) { _showOrg = true; break; }
            }
            /* Gives the name column extra share once it has to carry the org too.
               One template still applies to every row in the list. */
            if ($list) { $list.toggleClass('vas-196-list-org', _showOrg); }

            /* No organizations in this period means nothing to pick - drop whatever
               the org lookup was still holding (the field itself is hidden when the
               dialog next opens). */
            if (!_showOrg && _filterOrgId !== null) {
                _filterOrgId = null;
                ctrlClear(vOrgCtrl);
                if ($filterBtn) {
                    $filterBtn.toggleClass('vas-196-filter-btn-active', _filterDocTypeId !== null);
                }
            }

            buildView();
            paintList();
        }

        /* Projects _rows through the committed filter selection. Each term is applied
           whenever it HAS a value - including id 0 for the tenant-wide organization -
           and skipped only when the lookup was left empty (null). */
        function buildView() {
            if (_filterOrgId === null && _filterDocTypeId === null) { _view = _rows; return; }

            _view = [];
            for (var i = 0; i < _rows.length; i++) {
                var row = _rows[i];
                if (_filterOrgId !== null && row.AD_Org_ID !== _filterOrgId) { continue; }
                if (_filterDocTypeId !== null && row.C_DocBaseType_ID !== _filterDocTypeId) { continue; }
                _view.push(row);
            }
        }

        // ── Filter dialog (VIS lookup controls) ──────────────────────────────

        /* Reads a VIS control's current value as an id. VTextBoxButton exposes both
           getValue() and .value depending on version - take whichever answers.
           Returns null for an empty control, NOT 0: AD_Org_ID 0 is the tenant-wide
           '*' organization and a legitimate selection, so "no value" and "0" have to
           stay distinguishable all the way through to buildView(). */
        function ctrlId(ctrl) {
            if (!ctrl) { return null; }
            var raw = (typeof ctrl.getValue === 'function') ? ctrl.getValue() : ctrl.value;
            if (raw === null || raw === undefined || raw === '') { return null; }
            var id = parseInt(raw, 10);
            return isNaN(id) ? null : id;
        }

        /* setValue(value, forceSet, fireEvent) - all three are needed for the control
           to repaint its display text without firing the change event back at us. */
        function ctrlClear(ctrl) {
            if (!ctrl) { return; }
            try { ctrl.setValue(null, true, false); }
            catch (e) { if (typeof ctrl.setValue === 'function') { ctrl.setValue(null); } }
        }

        /* Builds one lookup field: a bold caption on its own line above an underlined
           control row that carries the lookup text box and its button(s). The row
           owns the underline so it runs unbroken beneath them (same treatment as the
           VAS_063 / VAS_067 search filters). Returns null when the framework controls
           are unavailable or the lookup cannot be built, so a missing dictionary entry
           drops one field instead of breaking the popover.
           displayType selects the flavour: DisplayType.Search for the single-value
           filter fields, DisplayType.MultiKey for the process parameters - a MultiKey
           control returns its selection as a comma-separated id list and carries a
           second button, which is why both are appended when present. */
        function buildLookupField(columnName, validation, labelText, displayType, mandatory) {
            if (!VIS.MLookupFactory || !VIS.Controls || !VIS.Controls.VTextBoxButton) { return null; }

            var type = displayType || VIS.DisplayType.Search;
            var ctrl;
            try {
                var ctx = VIS.Env.getCtx();

                /* ctx, windowNo, column_ID, AD_Reference_ID, columnName,
                   AD_Reference_Value_ID, isParent, validationCode */
                var lookup = VIS.MLookupFactory.get(ctx, ($self.windowNo > 0 ? $self.windowNo : 0), 0,
                    type, columnName, 0, false, validation);

                /* No lookup, no field - the caller decides what to do instead. */
                if (!lookup) { return null; }

                /* columnName, mandatory, readOnly, updateable, displayType, lookup */
                ctrl = new VIS.Controls.VTextBoxButton(columnName, !!mandatory, false, true,
                    type, lookup);
            } catch (e) {
                if (window.console) { console.log('VAS_196: lookup ' + columnName + ' failed', e); }
                return null;
            }

            /* Prefer the framework's own translated element name for the column, so the
               field is captioned exactly as it is everywhere else; the AD_Message
               fallback only kicks in where there is no such text. */
            var text = (VIS.translatedTexts && VIS.translatedTexts[columnName])
                ? VIS.translatedTexts[columnName]
                : labelText;

            var $field = $('<div class="vas-196-frow">');
            var $caption = $('<div class="vas-196-flabel">').text(text);
            if (mandatory) { $caption.append('<span class="vas-196-freq">*</span>'); }
            var $row = $('<div class="vas-196-fctrlrow">');

            $row.append(ctrl.getControl()
                .addClass('vas-196-fctrl')
                .attr('data-hasbtn', ' ')
                .css('width', '100%'));

            /* Search offers one button (the lookup), MultiKey two (pick list and
               clear) - only ask for as many as the flavour actually has, so the
               filter fields keep the single trailing affordance they had. */
            var btnCount = (type === VIS.DisplayType.MultiKey) ? 2 : 1;
            for (var b = 0; b < btnCount; b++) {
                var btn = null;
                try { btn = ctrl.getBtn ? ctrl.getBtn(b) : null; } catch (e2) { btn = null; }
                /* jQuery object or bare element, depending on the control version. */
                if (btn && (typeof btn.length !== 'number' || btn.length > 0)) {
                    $row.append($('<span class="vas-196-fbtnwrap">').append(btn));
                }
            }

            $field.append($caption).append($row);

            return { field: $field, ctrl: ctrl };
        }

        /* Same field, built as a dropdown off a TableDir lookup.
           AD_Org_ID and C_DocBaseType_ID are TableDir columns in the dictionary, not
           Search ones - asking MLookupFactory for a Search lookup on them returns
           nothing to build a control from, which silently dropped both filter fields.
           Every other org picker in this codebase (vtrxmaterial, vinvoicegen,
           VAS_TimeSheetInvoice) uses TableDir + VComboBox for exactly this reason,
           and a bounded list of organizations / base types reads better as a
           dropdown than as a type-ahead anyway. */
        function buildComboField(columnName, validation, labelText, mandatory) {
            if (!VIS.MLookupFactory || !VIS.Controls || !VIS.Controls.VComboBox) { return null; }

            var ctrl;
            try {
                var lookup = VIS.MLookupFactory.get(VIS.Env.getCtx(),
                    ($self.windowNo > 0 ? $self.windowNo : 0), 0,
                    VIS.DisplayType.TableDir, columnName, 0, false, validation);

                if (!lookup) { return null; }

                /* columnName, mandatory, readOnly, updateable, lookup, size */
                ctrl = new VIS.Controls.VComboBox(columnName, !!mandatory, false, true, lookup, 50);
            } catch (e) {
                if (window.console) { console.log('VAS_196: combo ' + columnName + ' failed', e); }
                return null;
            }

            var text = (VIS.translatedTexts && VIS.translatedTexts[columnName])
                ? VIS.translatedTexts[columnName]
                : labelText;

            var $field = $('<div class="vas-196-frow">');
            var $caption = $('<div class="vas-196-flabel">').text(text);
            if (mandatory) { $caption.append('<span class="vas-196-freq">*</span>'); }

            var $row = $('<div class="vas-196-fctrlrow">')
                .append(ctrl.getControl().addClass('vas-196-fsel').css('width', '100%'));

            $field.append($caption).append($row);

            return { field: $field, ctrl: ctrl };
        }

        /* The popover is NOT modal and carries no overlay: every other widget on the
           dashboard stays clickable while it is open, and it belongs visually to this
           widget alone. It is appended to <body> (the card clips its own overflow, so
           an in-card popover would be cut off) and positioned against the funnel on
           every open, which is what makes it read as attached to the icon. */
        function buildFilterPopup() {
            $filterPopup = $('<div class="vas-196-fpop vas-196-hidden" role="dialog" aria-label="' +
                escapeHtml(label('VAS_034_Filter', 'Filter')) + '">');
            $filterPopup.append('<span class="vas-196-fpop-arrow"></span>');

            /* Organization - only offered when the period is controlled per org.
               Same restriction the standard org lookups use: no summary orgs (the
               tenant-wide '*' org, AD_Org_ID 0, is deliberately kept because
               tenant-wide period controls belong to it) and no cost / profit centres,
               which are accounting dimensions rather than postable organizations. */
            var org = buildComboField('AD_Org_ID',
                "AD_Org.IsActive='Y' AND (AD_Org.IsSummary='N' OR AD_Org.AD_Org_ID=0)" +
                " AND AD_Org.IsCostCenter='N' AND AD_Org.IsProfitCenter='N'",
                label('VAS_196_Organization', 'Organization'));
            $orgField = org ? org.field : $();
            vOrgCtrl = org ? org.ctrl : null;

            /* Document base type - the same table the matrix rows are resolved from,
               so the selected C_DocBaseType_ID matches a row id for id. */
            var doc = buildComboField('C_DocBaseType_ID', "C_DocBaseType.IsActive='Y'",
                label('VAS_196_DocBaseType', 'Document Base Type'));
            vDocTypeCtrl = doc ? doc.ctrl : null;

            /* Borderless action column - text buttons, no surface of their own. */
            var $foot = $('<div class="vas-196-fpop-foot">');
            var $clear = $('<button type="button" class="vas-196-linkbtn">')
                .text(label('VIS_Clear', 'Clear'));
            var $apply = $('<button type="button" class="vas-196-linkbtn vas-196-linkbtn-primary">')
                .text(label('VIS_Apply', 'Apply'));

            /* Clear resets the fields only - it neither applies nor closes, so the
               user can pick a different combination straight away. */
            $clear.on('click', function () {
                ctrlClear(vOrgCtrl);
                ctrlClear(vDocTypeCtrl);
            });

            $apply.on('click', function () {
                applyFilters();
                closeFilterPopup();
            });

            $foot.append($clear).append($apply);
            if (org) { $filterPopup.append($orgField); }
            if (doc) { $filterPopup.append(doc.field); }
            $filterPopup.append($foot);

            $filterPopup.on('keydown', function (e) {
                if (e.key === 'Escape' || e.keyCode === 27) {
                    e.stopPropagation();
                    closeFilterPopup();
                    if ($filterBtn) { $filterBtn.focus(); }
                }
            });

            $('body').append($filterPopup);
        }

        function positionFilterPopup() {
            positionPopup($filterPopup, $filterBtn);
        }

        function positionProcessPopup() {
            positionPopup($processPopup, $processBtn);
        }

        /* Anchors a popover under its own button, right edges aligned, and flips it
           above when the button sits too low in the viewport. */
        function positionPopup($popup, $btn) {
            if (!$popup || !$btn || !$btn[0]) { return; }

            var rect = $btn[0].getBoundingClientRect();
            var pw = $popup.outerWidth();
            var ph = $popup.outerHeight();
            var gap = 8;

            var left = rect.right - pw;
            left = Math.min(left, window.innerWidth - pw - gap);
            left = Math.max(gap, left);

            var top = rect.bottom + gap;
            var below = true;
            if (top + ph > window.innerHeight - gap) {
                var above = rect.top - ph - gap;
                if (above >= gap) { top = above; below = false; }
                else { top = Math.max(gap, window.innerHeight - ph - gap); }
            }

            $popup.css({ left: Math.round(left) + 'px', top: Math.round(top) + 'px' });
            $popup.toggleClass('vas-196-fpop-above', !below);

            /* Point the arrow at the button's centre wherever the panel ended up. */
            var caret = rect.left + (rect.width / 2) - left;
            caret = Math.max(12, Math.min(pw - 12, caret));
            $popup.find('.vas-196-fpop-arrow').css('left', Math.round(caret) + 'px');
        }

        function openFilterPopup() {
            if (!$filterPopup) { buildFilterPopup(); }

            /* One panel at a time: they anchor to neighbouring header controls and
               would otherwise overlap. */
            closeProcessPopup();

            /* Nothing to pick when every control of the period is tenant-wide. */
            $orgField.toggleClass('vas-196-hidden', !_showOrg);

            $filterPopup.removeClass('vas-196-hidden');
            positionFilterPopup();
            _popupOpen = true;
            if ($filterBtn) { $filterBtn.addClass('vas-196-filter-btn-open'); }

            /* Same as the process panel: nothing behind the filter can be picked
               while it is open - only the funnel it hangs from stays live. */
            blockCard(true, $filterBtn);

            /* Bound only while the popover is open, under this widget's own namespace,
               so two instances never fight over them.
               Deliberately NO outside-click dismisser: the panel stays put while the
               user works anywhere else on the screen - it closes on the funnel, on
               Apply, or on Escape, and nothing else. */
            $(document).on('keydown' + _nsFilter, onFilterKeyDown);
            /* Follow the funnel instead of closing: a lookup's Info window can shift
               the page, and closing there would pull the control out from under an
               interaction the user is in the middle of. */
            $(window).on('resize' + _nsFilter + ' scroll' + _nsFilter, positionFilterPopup);
        }

        function closeFilterPopup() {
            if (!_popupOpen) { return; }
            _popupOpen = false;
            if ($filterPopup) { $filterPopup.addClass('vas-196-hidden'); }
            if ($filterBtn) { $filterBtn.removeClass('vas-196-filter-btn-open'); }

            blockCard(false);

            $(document).off('keydown' + _nsFilter);
            $(window).off('resize' + _nsFilter + ' scroll' + _nsFilter);
        }

        function toggleFilterPopup() {
            if (_popupOpen) { closeFilterPopup(); } else { openFilterPopup(); }
        }

        function onFilterKeyDown(e) {
            if (e.key === 'Escape' || e.keyCode === 27) { closeFilterPopup(); }
        }

        // ── Period process dialog (C_Period.Processing) ──────────────────────

        /* Builds the header link's panel: the three parameters the standard process
           declares, then OK / Cancel. Built once and kept, but its parameters are
           cleared on every opening, so a run is always deliberate - nothing is ever
           carried over from the previous one. */
        function buildProcessPopup() {
            var procLabel = label('VAS_196_OpenClosePeriod', 'Open / Close Period');

            $processPopup = $('<div class="vas-196-fpop vas-196-ppop vas-196-hidden" role="dialog"' +
                ' aria-label="' + escapeHtml(procLabel) + '">');
            $processPopup.append('<span class="vas-196-fpop-arrow"></span>');

            /* Organization - mandatory, exactly as the process declares it. Same
               restriction as the filter lookup: no summary orgs except the
               tenant-wide '*' (AD_Org_ID 0), which owns the tenant-wide controls,
               and no cost / profit centres. MultiKey: the process takes a list. */
            var orgValidation = "AD_Org.IsActive='Y' AND (AD_Org.IsSummary='N' OR AD_Org.AD_Org_ID=0)" +
                " AND AD_Org.IsCostCenter='N' AND AD_Org.IsProfitCenter='N'";
            var orgLabel = label('VAS_196_Organization', 'Organization');

            var org = buildLookupField('AD_Org_ID', orgValidation, orgLabel,
                VIS.DisplayType.MultiKey, true)
                /* Falls back to a single-select dropdown where the MultiKey lookup
                   cannot be built - one organization per run instead of several,
                   which the process handles just as well, rather than no field. */
                || buildComboField('AD_Org_ID', orgValidation, orgLabel, true);
            vProcOrgCtrl = org ? org.ctrl : null;

            /* Document base type - optional; empty means every base type of the
               period, which is how the process reads a missing parameter. */
            var docValidation = "C_DocBaseType.IsActive='Y'";
            var docLabel = label('VAS_196_DocBaseType', 'Document Base Type');

            var doc = buildLookupField('C_DocBaseType_ID', docValidation, docLabel,
                VIS.DisplayType.MultiKey, false)
                || buildComboField('C_DocBaseType_ID', docValidation, docLabel, false);
            vProcDocCtrl = doc ? doc.ctrl : null;

            /* Period Action - the process's own list parameter. Plain select: the
               three stored codes are fixed by MPeriodControl, so there is nothing to
               look up. */
            var $actionField = $('<div class="vas-196-frow">');
            var $actionCaption = $('<div class="vas-196-flabel">')
                .text(label('VAS_196_PeriodAction', 'Period Action'))
                .append('<span class="vas-196-freq">*</span>');
            $procAction = $('<select class="vas-196-fsel">');
            for (var i = 0; i < PERIOD_ACTIONS.length; i++) {
                $procAction.append($('<option>')
                    .attr('value', PERIOD_ACTIONS[i].code)
                    .text(label(PERIOD_ACTIONS[i].key, PERIOD_ACTIONS[i].text)));
            }
            $actionField.append($actionCaption)
                .append($('<div class="vas-196-fctrlrow">').append($procAction));

            var $foot = $('<div class="vas-196-fpop-foot">');
            var $cancel = $('<button type="button" class="vas-196-linkbtn">')
                .text(label('Cancel', 'Cancel'));
            var $ok = $('<button type="button" class="vas-196-linkbtn vas-196-linkbtn-primary">')
                .text(label('OK', 'OK'));

            $ok.on('click', function () { runPeriodProcess(); });
            $cancel.on('click', function () { closeProcessPopup(); });

            $foot.append($cancel).append($ok);
            if (org) { $processPopup.append(org.field); }
            if (doc) { $processPopup.append(doc.field); }
            $processPopup.append($actionField);
            $processPopup.append($foot);

            $processPopup.on('keydown', function (e) {
                if (e.key === 'Escape' || e.keyCode === 27) {
                    e.stopPropagation();
                    closeProcessPopup();
                    if ($processBtn) { $processBtn.focus(); }
                }
            });

            $('body').append($processPopup);
        }

        function openProcessPopup() {
            if (!$processPopup) { buildProcessPopup(); }

            closeFilterPopup();

            /* Always opens empty: a parameter left over from the previous run would
               be applied without the user having picked it this time. */
            ctrlClear(vProcOrgCtrl);
            ctrlClear(vProcDocCtrl);

            $processPopup.removeClass('vas-196-hidden');
            positionProcessPopup();
            _processPopupOpen = true;
            if ($processBtn) { $processBtn.addClass('vas-196-headlink-open'); }

            /* The panel owns the widget while it is open: the selectors, the funnel
               and the row buttons behind it stop responding, so the period the
               process is about to run against cannot change under it. Only this
               card is covered - the rest of the dashboard stays live. */
            blockCard(true, $processBtn);

            /* Same contract as the filter popover: no outside-click dismisser, its
               own namespace, and it follows its button rather than closing when the
               page moves under a lookup's Info window. */
            $(document).on('keydown' + _nsProcess, onProcessKeyDown);
            $(window).on('resize' + _nsProcess + ' scroll' + _nsProcess, positionProcessPopup);
        }

        function closeProcessPopup() {
            if (!_processPopupOpen) { return; }
            _processPopupOpen = false;
            if ($processPopup) { $processPopup.addClass('vas-196-hidden'); }
            if ($processBtn) { $processBtn.removeClass('vas-196-headlink-open'); }

            blockCard(false);

            $(document).off('keydown' + _nsProcess);
            $(window).off('resize' + _nsProcess + ' scroll' + _nsProcess);
        }

        /* Makes the card inert while one of the two popovers is open - the card only,
           never the dashboard. Nothing behind the panel can be picked: not the
           Calendar / Year / Period selectors, not the row Open / Close buttons, not
           the other popover's button.
           $live is the control the open panel hangs from (the funnel or the header
           link); it is left alive so the panel can always be toggled shut from where
           it was opened. */
        function blockCard(block, $live) {
            if (!$card) { return; }
            $card.toggleClass('vas-196-card-blocked', !!block);

            /* The cover stops the mouse and survives every repaint of the list, but
               it does not stop the keyboard - so the three selectors and the funnel
               are disabled as well. Their previous state is remembered: a selector
               with no options is disabled on its own account and must stay that way
               when the panel closes. */
            if (!$selCal) { return; }
            var $stable = $selCal.add($selYear).add($selPeriod).add($filterBtn);
            if (block && $live && $live.length) { $stable = $stable.not($live); }

            $stable.each(function () {
                var $el = $(this);
                if (block) {
                    $el.attr('data-vas196-dis', $el.prop('disabled') ? '1' : '0');
                    $el.prop('disabled', true);
                } else if ($el.attr('data-vas196-dis') !== undefined) {
                    $el.prop('disabled', $el.attr('data-vas196-dis') === '1');
                    $el.removeAttr('data-vas196-dis');
                }
            });

            $card.find('.vas-196-blocked-live').removeClass('vas-196-blocked-live');
            if (block && $live) { $live.addClass('vas-196-blocked-live'); }
        }

        function toggleProcessPopup() {
            if (_processPopupOpen) { closeProcessPopup(); } else { openProcessPopup(); }
        }

        function onProcessKeyDown(e) {
            if (e.key === 'Escape' || e.keyCode === 27) { closeProcessPopup(); }
        }

        /* Reads a MultiKey control as the comma-separated id list the process expects.
           Empty stays empty - the process reads a missing parameter as "all of them". */
        function ctrlIdList(ctrl) {
            if (!ctrl) { return ''; }
            var raw = (typeof ctrl.getValue === 'function') ? ctrl.getValue() : ctrl.value;
            if (raw === null || raw === undefined) { return ''; }
            return String(raw).replace(/\s/g, '');
        }

        /* Hands the three parameters to the standard process on C_Period.Processing.
           Nothing is computed here: the server re-validates the whole selection, runs
           the process through the process engine and answers with its summary. */
        function runPeriodProcess() {
            if (_periodId <= 0) {
                showError(label('VAS_196_SelectPeriod', 'Select a period first.'));
                return;
            }

            var orgIds = ctrlIdList(vProcOrgCtrl);
            if (!orgIds) {
                showError(label('VAS_196_SelectOrganization', 'Select at least one organization.'));
                return;
            }

            var docIds = ctrlIdList(vProcDocCtrl);
            var action = $procAction ? $procAction.val() : '';
            if (!action) { return; }

            /* Everything needed is read, so the panel goes away now - the busy overlay
               carries the run from here, and the matrix repaints under it. A rejected
               parameter above keeps the panel open instead, so the user can fix it. */
            closeProcessPopup();

            showBusy(true);
            $.ajax({
                url: VIS.Application.contextUrl + 'VAS_196_PeriodControlMatrixWidget/RunPeriodProcess',
                type: 'POST',
                cache: false,
                data: {
                    calendarId: _calendarId,
                    yearId: _yearId,
                    periodId: _periodId,
                    orgIds: orgIds,
                    docBaseTypeIds: docIds,
                    periodAction: action
                },
                success: function (res) {
                    var data = null;
                    try { data = parseResponse(res); } catch (e) { }

                    if (!data || data.error) {
                        showError(label('VAS_196_ProcessRunFailed', 'Could not run the period process.'));
                        return;
                    }

                    if (!data.Success) {
                        var err = ERROR_MAP[data.ErrorCode] ||
                            { key: 'VAS_196_ProcessRunFailed', text: 'Could not run the period process.' };
                        var text = label(err.key, err.text);
                        if (data.ErrorMessage) { text += ' ' + data.ErrorMessage; }
                        showError(text);
                        return;
                    }

                    /* The process updated C_PeriodControl rows directly, so the matrix
                       is re-read rather than patched from the request. */
                    if (_periodId > 0) { loadControls(_periodId); }

                    if (data.Summary) { showInfo(data.Summary); }
                },
                error: function () {
                    showError(label('VAS_196_ProcessRunFailed', 'Could not run the period process.'));
                },
                complete: function () { showBusy(false); }
            });
        }

        /* Commits what the two lookups hold. Both are plain id comparisons against the
           loaded rows - no display-name matching, so a renamed or translated
           organization / base type can never break the filter. */
        function applyFilters() {
            _filterOrgId = _showOrg ? ctrlId(vOrgCtrl) : null;
            _filterDocTypeId = ctrlId(vDocTypeCtrl);

            if ($filterBtn) {
                $filterBtn.toggleClass('vas-196-filter-btn-active',
                    _filterOrgId !== null || _filterDocTypeId !== null);
            }

            _page = 1;
            _needsSync = true;
            buildView();
            paintList();
        }

        function renderState(text, isError) {
            if (!$list) { return; }
            $list.html('<div class="vas-196-state' + (isError ? ' vas-196-state-error' : '') + '">' +
                escapeHtml(text) + '</div>');
            $pager.empty();
        }

        function paintList() {
            if (!$list) { return; }

            if (!_rows || _rows.length === 0) {
                renderState(label('VAS_196_NoControls', 'No period controls configured for this period.'), false);
                return;
            }

            /* Distinct from the state above: the period does have controls, the
               filter just excluded them all - say which of the two it is. */
            if (!_view || _view.length === 0) {
                renderState(label('VAS_196_NoMatches', 'No period controls match the filter.'), false);
                return;
            }

            var totalPages = _pageSize > 0 ? Math.ceil(_view.length / _pageSize) : 1;
            if (_page > totalPages) { _page = totalPages; }
            if (_page < 1) { _page = 1; }

            var from = (_page - 1) * _pageSize;
            var to = Math.min(from + _pageSize, _view.length);

            var html = '';
            for (var i = from; i < to; i++) {
                html += buildRow(_view[i]);
            }
            $list.html(html);

            $pager.html(pagerHtml(_page, totalPages, from + 1, to, _view.length));
            $pager.find('.vas-196-pg-prev').on('click', function () {
                if (_page > 1) { _page--; paintList(); }
            });
            $pager.find('.vas-196-pg-next').on('click', function () {
                if (_page < totalPages) { _page++; paintList(); }
            });

            /* Adapt the row capacity on the first paint after a data/size change
               only - never on manual page navigation. */
            if (_needsSync) { scheduleSync(); }
        }

        function buildRow(row) {
            var status = STATUS_MAP[row.PeriodStatus] || STATUS_MAP['N'];
            var statusText = label(status.key, status.text);

            /* The cell shows the translated AD_Ref_List name; the stored DocBaseType
               code rides along as a data attribute so the row can still be located /
               keyed by its value without the code ever being displayed. Only when the
               reference carries no name does the code surface as the label. */
            var code = row.DocBaseType || '';
            var name = row.DocBaseTypeName || code;

            /* Organization ahead of the document base type, and only when the period
               really is controlled per org (see _showOrg). A tenant-wide row inside
               such a mixed set keeps its own AD_Org name ('*'), so it stays obvious
               which rows apply to every organization. */
            var orgName = _showOrg ? (row.OrgName || '') : '';
            var orgPrefix = orgName
                ? '<span class="vas-196-cell-org">' + escapeHtml(orgName) + '</span>' +
                  '<span class="vas-196-cell-sep">·</span>'
                : '';
            var cellTitle = orgName ? orgName + ' · ' + name : name;

            var action;
            if (row.CanToggle && status.actionKey) {
                action = '<button type="button" class="vas-196-btn vas-196-btn-' + status.actionTone +
                    '" data-id="' + row.C_PeriodControl_ID + '">' +
                    escapeHtml(label(status.actionKey, status.actionText)) + '</button>';
            } else {
                /* Permanently closed: read-only, no click handler, tooltip explains why. */
                var hint = label('VAS_196_PermClosedHint', 'This period is permanently closed and cannot be reopened.');
                action = '<button type="button" class="vas-196-btn vas-196-btn-disabled" disabled title="' +
                    escapeHtml(hint) + '">' +
                    escapeHtml(label('VAS_192_Open', 'Open')) + '</button>';
            }

            return '<div class="vas-196-row" data-row-id="' + row.C_PeriodControl_ID +
                    '" data-docbasetype="' + escapeHtml(code) +
                    '" data-org-id="' + (row.AD_Org_ID || 0) + '">' +
                '<span class="vas-196-cell-name" data-docbasetype="' + escapeHtml(code) +
                    '" title="' + escapeHtml(cellTitle) + '">' +
                    orgPrefix + escapeHtml(name) + '</span>' +
                '<span class="vas-196-pill vas-196-pill-' + status.tone + '" title="' + escapeHtml(statusText) + '">' +
                    escapeHtml(statusText) + '</span>' +
                action +
            '</div>';
        }

        /* Canonical Widget Footer Pager (design.md): "Showing a–b of N" left,
           compact prev / "n of m" / next right. Hidden on a single page. */
        function pagerHtml(pageNo, totalPages, from, to, total) {
            if (totalPages <= 1) { return ''; }
            var prevDis = pageNo <= 1 ? ' disabled' : '';
            var nextDis = pageNo >= totalPages ? ' disabled' : '';
            var ofTxt = label('VAS_026_Of', 'of');
            var showing = label('VAS_026_Showing', 'Showing') + ' ' + from + '–' + to + ' ' + ofTxt + ' ' + total;

            return '<div class="vas-196-pager-row">' +
                '<span class="vas-196-pager-info" title="' + escapeHtml(showing) + '">' + escapeHtml(showing) + '</span>' +
                '<div class="vas-196-pager-nav">' +
                    '<button type="button" class="vas-196-pgbtn vas-196-pg-prev" aria-label="' +
                        escapeHtml(label('VAS_026_Prev', 'Previous')) + '"' + prevDis + '>' +
                        '<svg viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2.5" stroke-linecap="round" stroke-linejoin="round"><polyline points="15 18 9 12 15 6"/></svg>' +
                    '</button>' +
                    '<span class="vas-196-pager-label">' + pageNo + ' ' + escapeHtml(ofTxt) + ' ' + totalPages + '</span>' +
                    '<button type="button" class="vas-196-pgbtn vas-196-pg-next" aria-label="' +
                        escapeHtml(label('VAS_026_Next', 'Next')) + '"' + nextDis + '>' +
                        '<svg viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2.5" stroke-linecap="round" stroke-linejoin="round"><polyline points="9 18 15 12 9 6"/></svg>' +
                    '</button>' +
                '</div>' +
            '</div>';
        }

        // ── Adaptive row capacity (design.md "Adaptive Row Count") ───────────

        function scheduleSync() {
            var raf = window.requestAnimationFrame || function (cb) { return window.setTimeout(cb, 16); };
            raf(function () { syncCapacity(); });
        }

        function syncCapacity() {
            if (!$list || !$list[0]) { return; }
            if (!_view || _view.length === 0) { return; }

            var avail = $list[0].clientHeight;
            if (avail <= 0) {
                if (_needsSync) { scheduleSync(); }   // layout not settled yet - retry
                return;
            }

            /* Size off the tallest rendered row so a wrapped label never clips. */
            var painted = $list[0].querySelectorAll('.vas-196-row');
            var maxH = 0;
            for (var i = 0; i < painted.length; i++) {
                if (painted[i].offsetHeight > maxH) { maxH = painted[i].offsetHeight; }
            }
            if (maxH > 0) { _rowH = maxH; }
            var rowH = _rowH > 0 ? _rowH : ROW_FALLBACK;

            _needsSync = false;
            var capacity = Math.max(MIN_ROWS, Math.floor(avail / rowH));
            if (capacity !== _pageSize) {
                _pageSize = capacity;
                paintList();
            }
        }

        function observeList() {
            if (typeof ResizeObserver === 'undefined' || !$list || !$list[0]) { return; }
            if (_observer) { _observer.disconnect(); }
            _observer = new ResizeObserver(function () {
                _needsSync = true;
                syncCapacity();
            });
            _observer.observe($list[0]);
        }

        // ── Status change ────────────────────────────────────────────────────

        function findRow(periodControlId) {
            for (var i = 0; i < _rows.length; i++) {
                if (_rows[i].C_PeriodControl_ID === periodControlId) { return _rows[i]; }
            }
            return null;
        }

        function onActionClick(periodControlId) {
            var row = findRow(periodControlId);
            if (!row || !row.CanToggle) { return; }

            var status = STATUS_MAP[row.PeriodStatus] || STATUS_MAP['N'];
            if (!status.actionKey) { return; }

            /* Changing a period status changes what can be posted, so it is always
               confirmed first: "Close AP Invoice for May 2026?" - carrying the
               organization too whenever the period is controlled per org, so the
               user can see which org's control is about to change. */
            var name = row.DocBaseTypeName || row.DocBaseType || '';
            if (_showOrg && row.OrgName) { name = row.OrgName + ' · ' + name; }
            var question = label(status.actionKey, status.actionText) + ' ' + name +
                (_periodName ? ' ' + label('VAS_196_For', 'for') + ' ' + _periodName : '') + '?';

            if (VIS && VIS.ADialog && VIS.ADialog.confirm) {
                VIS.ADialog.confirm("", false, question, label('VAS_062_Confirm', 'Confirm'), function (ok) {
                    if (ok) { changeStatus(row); }
                });
            } else {
                changeStatus(row);
            }
        }

        function changeStatus(row) {
            var $btn = $list.find('.vas-196-btn[data-id="' + row.C_PeriodControl_ID + '"]');
            $btn.prop('disabled', true).addClass('vas-196-btn-working');
            showBusy(true);

            $.ajax({
                url: VIS.Application.contextUrl + 'VAS_196_PeriodControlMatrixWidget/ChangePeriodStatus',
                type: 'POST',
                cache: false,
                data: {
                    calendarId: _calendarId,
                    yearId: _yearId,
                    periodId: _periodId,
                    periodControlId: row.C_PeriodControl_ID
                },
                success: function (res) {
                    var data = null;
                    try { data = parseResponse(res); } catch (e) { }

                    if (!data || data.error) {
                        showError(label('VAS_196_ChangeFailed', 'Could not change the period status.'));
                        repaintRow(row);
                        return;
                    }

                    /* Repaint from the status the server re-read - never from the
                       action that was requested. */
                    if (data.PeriodStatus) {
                        row.PeriodStatus = data.PeriodStatus;
                        row.CanToggle = !!data.CanToggle;
                    }
                    repaintRow(row);

                    if (!data.Success) {
                        var err = ERROR_MAP[data.ErrorCode] ||
                            { key: 'VAS_196_ChangeFailed', text: 'Could not change the period status.' };
                        var text = label(err.key, err.text);
                        if (data.ErrorMessage) { text += ' ' + data.ErrorMessage; }
                        showError(text);
                    }
                },
                error: function () {
                    showError(label('VAS_196_ChangeFailed', 'Could not change the period status.'));
                    repaintRow(row);
                },
                complete: function () { showBusy(false); }
            });
        }

        /* Replaces one row in place so paging state and the other rows are untouched. */
        function repaintRow(row) {
            if (!$list) { return; }
            var $existing = $list.find('.vas-196-row[data-row-id="' + row.C_PeriodControl_ID + '"]');
            if ($existing.length === 0) { return; }
            $existing.replaceWith(buildRow(row));
        }

        // ── Framework contract ───────────────────────────────────────────────

        this.refreshWidget = function () {
            /* Refresh means "start clean" - drop the filter with the data. The
               process dialog's own parameters are left as they were; it is closed,
               not reset, because it is not part of what the matrix displays. */
            closeFilterPopup();
            closeProcessPopup();
            _filterOrgId = null;
            _filterDocTypeId = null;
            ctrlClear(vOrgCtrl);
            ctrlClear(vDocTypeCtrl);
            if ($filterBtn) { $filterBtn.removeClass('vas-196-filter-btn-active'); }

            _rows = [];
            _view = [];
            _page = 1;
            _needsSync = true;
            loadBootstrap();
        };

        this.getRoot = function () { return $root; };

        this.startObserving = function () { observeList(); };

        this.disposeComponent = function () {
            if (_observer) { _observer.disconnect(); _observer = null; }
            if ($list) { $list.off(); }
            if ($selCal) { $selCal.off(); }
            if ($selYear) { $selYear.off(); }
            if ($selPeriod) { $selPeriod.off(); }
            if ($filterBtn) { $filterBtn.off(); }
            if ($processBtn) { $processBtn.off(); }

            /* Both popovers were appended to <body>, so removing $root would leave
               them behind - close each (which unbinds the document/window dismissers
               under this instance's namespaces) and tear them down explicitly. */
            closeFilterPopup();
            if ($filterPopup) {
                $filterPopup.off();
                $filterPopup.remove();
                $filterPopup = null;
                $orgField = null;
                vOrgCtrl = null;
                vDocTypeCtrl = null;
            }

            closeProcessPopup();
            if ($processPopup) {
                $processPopup.off();
                $processPopup.remove();
                $processPopup = null;
                $procAction = null;
                vProcOrgCtrl = null;
                vProcDocCtrl = null;
            }

            $root.remove();
        };
    };

    VAS.VAS_196_PeriodControlMatrixWidget.prototype.widgetFirevalueChanged = function (value) {
        if (this.listener) { this.listener.widgetFirevalueChanged(value); }
    };

    VAS.VAS_196_PeriodControlMatrixWidget.prototype.addChangeListener = function (listener) {
        this.listener = listener;
    };

    VAS.VAS_196_PeriodControlMatrixWidget.prototype.init = function (windowNo, frame) {
        this.frame = frame;
        this.AD_UserHomeWidgetID = frame.widgetInfo.AD_UserHomeWidgetID;
        this.windowNo = windowNo;
        this.Initalize();
        this.frame.getContentGrid().append(this.getRoot());
        ensureDashInlineSizeVar(this.getRoot());
        this.startObserving();
    };

    VAS.VAS_196_PeriodControlMatrixWidget.prototype.widgetSizeChange = function (height, width) { };

    VAS.VAS_196_PeriodControlMatrixWidget.prototype.dispose = function () {
        this.disposeComponent();
        if (this.frame) { this.frame.dispose(); }
        this.frame = null;
    };

})(VAS, jQuery);
