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

    /* Server error tokens -> message key + inline default. */
    var ERROR_MAP = {
        'INVALID': { key: 'VAS_196_InvalidSelection', text: 'The selected period is no longer available.' },
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

        var $root = $('<div class="vas-196-root">');
        var $card;
        var $list;
        var $pager;
        var $selCal;
        var $selYear;
        var $selPeriod;
        var $busy;

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

        // ── Build ────────────────────────────────────────────────────────────

        this.Initalize = function () {
            createWidget();
            setupResizeObserver();
            loadBootstrap();
        };

        function createWidget() {
            var title = label('VAS_196_PeriodControlMatrix', 'Period Control Matrix');
            var subtitle = label('VAS_196_ByDocBaseType', 'By document base type');

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
                    '</div>' +
                    '<div class="vas-196-body">' +
                        '<div class="vas-196-filters">' +
                            '<select class="vas-196-sel vas-196-sel-cal" aria-label="' +
                                escapeHtml(label('VAS_196_Calendar', 'Calendar')) + '"></select>' +
                            '<select class="vas-196-sel vas-196-sel-year" aria-label="' +
                                escapeHtml(label('VAS_196_Year', 'Year')) + '"></select>' +
                            '<select class="vas-196-sel vas-196-sel-period" aria-label="' +
                                escapeHtml(label('VAS_192_Period', 'Period')) + '"></select>' +
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

            var totalPages = _pageSize > 0 ? Math.ceil(_rows.length / _pageSize) : 1;
            if (_page > totalPages) { _page = totalPages; }
            if (_page < 1) { _page = 1; }

            var from = (_page - 1) * _pageSize;
            var to = Math.min(from + _pageSize, _rows.length);

            var html = '';
            for (var i = from; i < to; i++) {
                html += buildRow(_rows[i]);
            }
            $list.html(html);

            $pager.html(pagerHtml(_page, totalPages, from + 1, to, _rows.length));
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
            if (!_rows || _rows.length === 0) { return; }

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
            _rows = [];
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
