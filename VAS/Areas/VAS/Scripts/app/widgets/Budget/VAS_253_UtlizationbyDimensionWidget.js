/************************************************************
 * Module Name    : VAS
 * Purpose        : Utilization by Dimension - a 4x2 paginated bar list for the
 *                  Budgeting dashboard.
 *
 *                  How much of the approved budget each value of ONE accounting
 *                  dimension has actually consumed:
 *
 *                    [~] Utilization by dimension   [ FY 2026 v ] [ Organization v ]
 *                        Actual expense against approved budget
 *
 *                    Head office                    $3.1M of $4.86M · 63.8%
 *                    ▓▓▓▓▓▓▓▓▓▓▓▓░░░░░░░░
 *
 *                    Mumbai plant                   $2.27M of $3.24M · 70.0%
 *                    ▓▓▓▓▓▓▓▓▓▓▓▓▓░░░░░░░
 *
 *                    Showing 1-4 of 11                        <  1 of 3  >
 *
 *                  THE DIMENSION IS CONFIGURATION, NOT CODE. The second pill is built
 *                  from the ACTIVE accounting-schema elements of the tenant's primary
 *                  accounting schema, each labelled with that element's OWN name. A
 *                  tenant that represents departments as Activity - or renames "User
 *                  List 1" to "Cost centre" - sees its own words, and no word in this
 *                  file names a dimension. Switching the pill changes only the labels
 *                  and the figures: the rhythm of the card is identical either way.
 *
 *                  A LABEL LINE AND A TRACK, NOT A TABLE. The dimension value is the
 *                  row's identity and takes the leading edge; what it consumed of what
 *                  it was given is the row's conclusion and sits against the trailing
 *                  edge; the bar underneath is the same fact drawn, so the row reads at
 *                  a glance and at full precision without a second card.
 *
 *                  THE BAR IS CAPPED, THE NUMBER IS NOT. A value over budget draws a
 *                  full red track and prints its true percentage - 118.4% - because the
 *                  overflow is exactly what the reader is looking for. The threshold
 *                  tones are the design system's: brand under 85%, warning from 85%,
 *                  danger from 100%. THE TONE IS NEVER THE ONLY SIGNAL - the percentage
 *                  is printed beside every bar.
 *
 *                  NOTHING IS INFERRED. No burn rate, no run rate, no elapsed-time
 *                  proration - all of those need an assumed spending pattern, and this
 *                  card deliberately has none. It is actual over budget.
 *
 *                  AND IT IS ACTUAL AGAINST WHAT WAS BUDGETED. The server counts an
 *                  actual only where the SAME account and the SAME dimension value carry
 *                  a budget for the year; spend on an account nobody budgeted is not
 *                  utilization of anything and belongs to the unbudgeted-actuals card
 *                  (VAS_256). The subtitle counts the values that HAVE a budget, which
 *                  is why it can be smaller than the number of values in the master.
 *
 *                  THE ROWS ARE NOT INTERACTIVE. There is no drill-down here and no row
 *                  is a button: the card answers "what is running out", which the line
 *                  and the bar already say in full. That is a deliberate difference from
 *                  VAS_256, whose rows DO open a transaction list.
 *
 *                  ONE CURRENCY, THE SCHEMA'S OWN. Every figure is an accounting amount
 *                  in the primary accounting schema's currency, printed with that
 *                  currency's symbol against the number - "$3.1M", never "USD 3.1M" -
 *                  falling back to the ISO code only when the currency has no symbol.
 *                  Nothing is converted and no scale is assumed: the ISO code decides
 *                  whether the compact form steps in lakh/crore or thousand/million.
 *
 *                  Design: design.md -> dashboard-widgets.md (Glass Widget, Widget
 *                  Header, Widget Footer Pager, Content Fit Budget, No Inner
 *                  Scrollbars) supplies the shell and the pager; the widget
 *                  specification supplies the utilization row and its threshold tones.
 *
 *                  Summary Message Table
 *                  Rows marked (reuse) already exist under another key and are NOT
 *                  duplicated here.
 *                   # | Current Text                       | Message Key
 *                  ---+------------------------------------+--------------------------
 *                   1 | Utilization by dimension           | VAS_253_UtilizationByDim
 *                   2 | Actual expense against approved    | VAS_253_UtilizationHint
 *                     |   budget                           |
 *                   3 | values                             | VAS_253_Values
 *                   4 | Dimension                          | VAS_253_Dimension
 *                   5 | No accounting dimensions are       | VAS_253_NoDimensions
 *                     |   configured                       |
 *                   6 | No budget data is available for    | VAS_253_NoUtilization
 *                     |   the selected year and dimension. |
 *                   7 | (Not assigned)                     | VAS_253_NotAssigned
 *                   8 | sorted by utilization              | VAS_253_SortedByUtilization
 *                   9 | Budget                             | VAS_254_Budget          (reuse)
 *                  10 | Actual                             | VAS_252_Actual          (reuse)
 *                  11 | Utilized                           | VAS_252_Utilized        (reuse)
 *                  12 | of                                 | VAS_020_Of              (reuse)
 *                  13 | Financial year                     | VAS_256_FinancialYear   (reuse)
 *                  14 | No financial years available       | VAS_256_NoYears         (reuse)
 *                  15 | No primary calendar is configured  | VAS_256_NoCalendar      (reuse)
 *                  16 | No primary accounting schema is    | VAS_256_NoAcctSchema    (reuse)
 *                     |   configured                       |
 *                  17 | Showing                            | VAS_020_Showing         (reuse)
 *                  18 | Previous                           | VAS_020_Prev            (reuse)
 *                  19 | Next                               | VAS_020_Next            (reuse)
 *                  20 | Couldn't load                      | VAS_192_CouldntLoad     (reuse)
 *
 * Chronological development:
 *   VAI154         Created  Date 2026-09-09
 ***********************************************************/
; VAS = window.VAS || {};

; (function (VAS, $) {

    /* CSS lives in VAS/Areas/VAS/Content/VAS_253_UtlizationbyDimensionWidget.css. All
       classes are namespaced `vas-253-` so they never collide with sibling widgets. */

    /* design.md §Widget Header / §Measurement Setup: keep --dash-inline-size on :root
       equal to the dashboard container's current pixel width so the header clamps
       resolve against the dashboard's visible content area, not the viewport. One
       document-level observer serves every widget. */
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

    /* Inline SVG, not an icon-font class - the host shell does not always load an icon
       font and a missing glyph leaves an empty box. Explicit width/height as well as a
       viewBox: an SVG with only a viewBox falls back to 300x150px if a stylesheet is
       stale, which would sprawl across the header. */
    var ICONS = {
        /* A bar chart on an axis: the card measures consumption per dimension value,
           which is exactly what this glyph says. */
        utilization: '<svg width="18" height="18" viewBox="0 0 24 24" fill="none" stroke="currentColor" ' +
            'stroke-width="1.8" stroke-linecap="round" stroke-linejoin="round" aria-hidden="true" focusable="false">' +
            '<path d="M3 3v18h18"></path><path d="M7 16h4"></path><path d="M7 11h9"></path><path d="M7 6h6"></path></svg>',
        chevron: '<svg width="11" height="11" viewBox="0 0 24 24" fill="none" stroke="currentColor" ' +
            'stroke-width="2.2" stroke-linecap="round" stroke-linejoin="round" aria-hidden="true" focusable="false">' +
            '<polyline points="6 9 12 15 18 9"></polyline></svg>',
        tick: '<svg width="16" height="16" viewBox="0 0 24 24" fill="none" stroke="currentColor" ' +
            'stroke-width="2.4" stroke-linecap="round" stroke-linejoin="round" aria-hidden="true" focusable="false">' +
            '<polyline points="20 6 9 17 4 12"></polyline></svg>',
        prev: '<svg width="14" height="14" viewBox="0 0 24 24" fill="none" stroke="currentColor" ' +
            'stroke-width="2.6" stroke-linecap="round" stroke-linejoin="round" aria-hidden="true" focusable="false">' +
            '<polyline points="15 18 9 12 15 6"></polyline></svg>',
        next: '<svg width="14" height="14" viewBox="0 0 24 24" fill="none" stroke="currentColor" ' +
            'stroke-width="2.6" stroke-linecap="round" stroke-linejoin="round" aria-hidden="true" focusable="false">' +
            '<polyline points="9 18 15 12 9 6"></polyline></svg>'
    };

    /* The missing-value placeholder. A GLYPH rather than a word: it needs no AD_Message
       key and reads the same in every language. */
    var NIL = '-';

    /* design.md threshold tones for a utilization track. Brand up to 85%, warning from
       85%, danger from 100% - and the percentage is always printed beside the bar, so
       the tone is never the only signal. */
    var WARN_PCT = 85;
    var RISK_PCT = 100;

    /* Paging. Four bars fit at 1280px in a 4x2 cell; the adaptive fit may ask for more
       in a taller cell, and the server clamps whatever is asked for. */
    var DEFAULT_PAGE_SIZE = 4;
    var MIN_PAGE_SIZE = 1;
    var MAX_PAGE_SIZE = 12;

    /* A label line plus a track - this is only the pre-measurement fallback, replaced by
       the tallest rendered row on the first paint. */
    var ROW_HEIGHT_FALLBACK = 56;

    /* Which pill a popover belongs to. */
    var PICK_YEAR = 'year';
    var PICK_DIMENSION = 'dimension';

    VAS.VAS_253_UtlizationbyDimensionWidget = function () {
        this.frame;
        this.windowNo;
        this.widgetInfo;

        var $self = this;
        var $root;
        var $card;
        var $yearBtn;
        var $dimBtn;
        var $list;
        var $foot;
        var $state;
        var $busy;
        var $picker;

        var widgetID = 0;

        /* Unique event namespace per instance - a widget can sit twice on one dashboard,
           and the picker binds document-level handlers. */
        var _ns = '';

        var _rows = [];
        var _years = [];
        var _dimensions = [];
        var _schema = null;
        var _yearId = 0;
        var _fiscalYear = '';
        var _dimension = '';
        var _dimensionLabel = '';
        var _page = 1;
        var _pageSize = DEFAULT_PAGE_SIZE;
        var _totalRows = 0;
        var _totalPages = 0;

        var _rowH = 0;
        var _needsSync = false;
        var _loading = false;
        var _pickerKind = '';
        var _pickerOpen = false;
        var _disposed = false;
        var _rootObserver = null;
        var _listObserver = null;

        /* ------------------------------------------------------------ */
        /* Lifecycle                                                    */
        /* ------------------------------------------------------------ */
        this.initalize = function () {
            widgetID = (VIS.Utility && VIS.Utility.Util
                ? VIS.Utility.Util.getValueOfInt($self.widgetInfo.AD_UserHomeWidgetID)
                : 0);
            if (widgetID === 0) { widgetID = $self.windowNo; }
            _ns = '.vas253_' + widgetID;

            buildSkeleton();
            createBusyIndicator();
            setupRootObserver();
        };

        /* The framework's own widget loader, overlaid on the whole card while a read is in
           flight - the same treatment every sibling VAS widget gives its loads. It covers
           EVERY read: the initial load, the Refresh button, a page turn and either filter
           change. Created visible so it is already up from the moment the widget mounts. */
        function createBusyIndicator() {
            $busy = $('<div class="vis-busyindicatorouterwrap vas-widget-busy-wrap">' +
                '<div class="vis-busyindicatorinnerwrap"><i class="vis_widgetloader"></i></div>' +
            '</div>');
            $busy[0].style.visibility = 'visible';
            $root.append($busy);
        }

        function showBusyIndicator() {
            if ($busy && $busy[0]) { $busy[0].style.visibility = 'visible'; }
        }

        function hideBusyIndicator() {
            if ($busy && $busy[0]) { $busy[0].style.visibility = 'hidden'; }
        }

        /* Publishes THIS widget's own pixel width as --widget-inline-size on its root,
           which is the first variable the card's font-size clamp reads (the dashboard
           width is only the fallback). Every sibling widget does exactly this - without it
           the card measures itself against the whole dashboard and renders a size larger
           than its neighbours. */
        function setupRootObserver() {
            if (typeof ResizeObserver === 'undefined') { return; }
            try {
                _rootObserver = new ResizeObserver(function (entries) {
                    if (!$root || !$root[0]) { return; }

                    for (var i = 0; i < entries.length; i++) {
                        var width = entries[i].contentRect.width;
                        if (width > 0) {
                            $root[0].style.setProperty('--widget-inline-size', width + 'px');
                        }
                    }
                });
                _rootObserver.observe($root[0]);
            } catch (e) { /* the clamp falls back to --dash-inline-size */ }
        }

        this.intialLoad = function () {
            fetchPage(1);
        };

        /* The dashboard's Refresh button calls this. It goes back to page 1: a refresh
           re-reads the whole year, postings may have landed since the last load, and the
           page the user was on is not reliably the same page afterwards. The chosen YEAR
           and DIMENSION are kept - those are filters the user set, not a position in a
           list. */
        this.refreshWidget = function () {
            closePicker();
            fetchPage(1);
        };

        /* ------------------------------------------------------------ */
        /* DOM skeleton                                                 */
        /* ------------------------------------------------------------ */
        function buildSkeleton() {
            $root = $('<div class="vas-253-root" id="vas-253-root-' + widgetID + '"></div>');

            var title = label('VAS_253_UtilizationByDim', 'Utilization by dimension');

            $card = $(
                '<div class="vas-253-card">' +
                    '<div class="vas-253-header">' +
                        '<span class="vas-253-icon">' + ICONS.utilization + '</span>' +
                        '<div class="vas-253-head-text">' +
                            '<div class="vas-253-title"></div>' +
                            '<div class="vas-253-subtitle"></div>' +
                        '</div>' +
                        '<div class="vas-253-filters">' +
                            '<button type="button" class="vas-253-pill vas-253-year" aria-haspopup="listbox">' +
                                '<span class="vas-253-pill-label"></span>' +
                                ICONS.chevron +
                            '</button>' +
                            '<button type="button" class="vas-253-pill vas-253-dim" aria-haspopup="listbox">' +
                                '<span class="vas-253-pill-label"></span>' +
                                ICONS.chevron +
                            '</button>' +
                        '</div>' +
                    '</div>' +
                    '<div class="vas-253-body">' +
                        '<div class="vas-253-list" role="list"></div>' +
                        '<div class="vas-253-pagerwrap"></div>' +
                    '</div>' +
                    '<div class="vas-253-state vas-253-hidden"></div>' +
                '</div>'
            );

            $card.find('.vas-253-title').text(title).attr('title', title);
            $card.find('.vas-253-list').attr('aria-label', title);

            /* The subtitle starts as the bare hint and gains its count and its dimension
               word once the first read lands - see paintSubtitle. */
            paintSubtitle();

            $yearBtn = $card.find('.vas-253-year');
            $dimBtn = $card.find('.vas-253-dim');
            $list = $card.find('.vas-253-list');
            $foot = $card.find('.vas-253-pagerwrap');
            $state = $card.find('.vas-253-state');

            paintYearLabel();
            paintDimensionLabel();

            $yearBtn.on('click' + _ns, function (e) {
                e.preventDefault();
                e.stopPropagation();
                togglePicker(PICK_YEAR);
            });

            $dimBtn.on('click' + _ns, function (e) {
                e.preventDefault();
                e.stopPropagation();
                togglePicker(PICK_DIMENSION);
            });

            $root.append($card);
        }

        /* ------------------------------------------------------------ */
        /* Data                                                         */
        /* ------------------------------------------------------------ */
        function fetchPage(pageNo) {
            if (_loading) { return; }
            _loading = true;
            showBusyIndicator();

            $.ajax({
                url: VIS.Application.contextUrl + 'VAS_253_UtlizationbyDimensionWidget/GetRows',
                type: 'GET',
                dataType: 'json',
                cache: false,
                /* Asynchronous, always - nothing here justifies blocking the UI thread. */
                async: true,
                data: {
                    yearId: _yearId,
                    dimension: _dimension,
                    pageNo: pageNo,
                    pageSize: _pageSize
                },
                success: function (raw) {
                    _loading = false;
                    if (_disposed) { return; }
                    hideBusyIndicator();

                    var data = parseResponse(raw);
                    if (!data || data.error) {
                        renderState(label('VAS_192_CouldntLoad', "Couldn't load"));
                        return;
                    }

                    /* The year and dimension lists come back with every read, so a newly
                       opened financial year or a newly activated accounting-schema element
                       appears on the next refresh without a second endpoint. */
                    _years = data.Years || [];
                    _dimensions = data.Dimensions || [];

                    /* A configuration gap is a different answer from a failed read, and it
                       says which piece of setup is missing rather than "couldn't load". */
                    if (data.ErrorCode) {
                        renderState(errorLabel(data.ErrorCode));
                        return;
                    }

                    if (!data.Loaded) {
                        renderState(label('VAS_192_CouldntLoad', "Couldn't load"));
                        return;
                    }

                    _schema = data.Schema || null;

                    _yearId = Number(data.C_Year_ID) || 0;
                    _fiscalYear = data.FiscalYear || '';
                    _dimension = data.Dimension || '';
                    _dimensionLabel = data.DimensionLabel || '';
                    paintYearLabel();
                    paintDimensionLabel();

                    _rows = data.Rows || [];
                    _page = Number(data.Page) || 1;
                    _pageSize = Number(data.PageSize) || _pageSize;
                    _totalRows = Number(data.TotalRows) || 0;
                    _totalPages = Number(data.TotalPages) || 0;

                    paintRows();
                    observeList();
                },
                error: function () {
                    _loading = false;
                    if (_disposed) { return; }
                    /* The overlay comes down on failure too - a spinner left running over
                       an error the user cannot see is the worst of both. */
                    hideBusyIndicator();
                    renderState(label('VAS_192_CouldntLoad', "Couldn't load"));
                }
            });
        }

        /* The controller returns a JSON string inside a JSON response. */
        function parseResponse(raw) {
            try {
                return (typeof raw === 'string') ? (raw ? JSON.parse(raw) : null) : raw;
            }
            catch (e) { if (window.console) { console.log(e); } return null; }
        }

        /* ------------------------------------------------------------ */
        /* Render                                                       */
        /* ------------------------------------------------------------ */

        /* A load failure or a missing configuration takes the card over. Having no budgeted
           values does NOT - that is handled inside the list, so the header, the subtitle
           and both filters stay put. */
        function renderState(text) {
            $card.find('.vas-253-body').addClass('vas-253-hidden');
            $state.removeClass('vas-253-hidden').text(text);
        }

        function errorLabel(code) {
            if (code === 'NOCALENDAR') {
                return label('VAS_256_NoCalendar', 'No primary calendar is configured');
            }
            if (code === 'NOACCTSCHEMA') {
                return label('VAS_256_NoAcctSchema', 'No primary accounting schema is configured');
            }
            if (code === 'NOYEAR') {
                return label('VAS_256_NoYears', 'No financial years available');
            }
            if (code === 'NODIMENSION') {
                return label('VAS_253_NoDimensions', 'No accounting dimensions are configured');
            }
            return label('VAS_192_CouldntLoad', "Couldn't load");
        }

        function paintYearLabel() {
            var text = _fiscalYear || yearNameOf(_yearId);
            $yearBtn.find('.vas-253-pill-label').text(text);
            $yearBtn.attr('title', label('VAS_256_FinancialYear', 'Financial year') + ': ' + text);
        }

        /* The dimension pill carries the ELEMENT'S OWN NAME, never a word from this file -
           that is the whole point of driving the list from the accounting schema. */
        function paintDimensionLabel() {
            var text = _dimensionLabel || dimensionNameOf(_dimension);
            $dimBtn.find('.vas-253-pill-label').text(text);
            $dimBtn.attr('title', label('VAS_253_Dimension', 'Dimension') + ': ' + text);
        }

        function yearNameOf(id) {
            for (var i = 0; i < _years.length; i++) {
                if (Number(_years[i].C_Year_ID) === id) { return _years[i].FiscalYear || ''; }
            }
            return '';
        }

        function dimensionNameOf(value) {
            for (var i = 0; i < _dimensions.length; i++) {
                if (_dimensions[i].Value === value) { return _dimensions[i].Label || ''; }
            }
            return '';
        }

        /* "Actual expense against approved budget · 6 Organization values" - the count is the WHOLE
           ranking, not the page, and the word for the values is the accounting schema
           element's own Name. Before the first read lands there is no count and no
           dimension to name, so the bare hint stands on its own rather than printing a
           zero the card does not yet know. */
        function paintSubtitle() {
            var text = label('VAS_253_UtilizationHint', 'Actual expense against approved budget');

            if (_dimensionLabel) {
                text += ' · ' + _totalRows + ' ' + _dimensionLabel + ' ' +
                    label('VAS_253_Values', 'values');
            }

            /* The subtitle truncates in a cell that is already sharing its header row with
               two pills, so the full sentence also goes on the title attribute - it moves to
               the tooltip rather than being lost. */
            $card.find('.vas-253-subtitle').text(text).attr('title', text);
        }

        function paintRows() {
            $state.addClass('vas-253-hidden');
            $card.find('.vas-253-body').removeClass('vas-253-hidden');

            paintSubtitle();

            if (!_rows || _rows.length === 0) {
                /* A dimension with nothing budgeted is a real answer, not an error - and
                   with no rows there is nothing to page, so the footer goes too. */
                $list.html('<div class="vas-253-empty">' +
                    escapeHtml(label('VAS_253_NoUtilization',
                        'No budget data is available for the selected year and dimension.')) + '</div>');
                $foot.empty();
                return;
            }

            var html = '';
            for (var i = 0; i < _rows.length; i++) { html += rowHtml(_rows[i]); }
            $list.html(html);

            paintFooter();

            /* Adapt the page size to the list height ONLY on the first paint / after a
               resize - never on manual navigation, which would flip pageSize under the user
               and re-clamp the page they just moved to. */
            if (_needsSync) { scheduleSync(); }
        }

        /* A label line over a track. Not a button - this card has no drill-down, so nothing
           here should look activatable. */
        function rowHtml(item) {
            var name = valueText(item);
            var pct = Number(item.UtilizedPct) || 0;
            var measure = measureText(item);

            /* The bar is capped at the track; the printed percentage is not. An overrun is
               a full track AND a number above 100, so the size of the overrun is never
               lost to the cap. */
            var width = pct;
            if (width < 0) { width = 0; }
            if (width > 100) { width = 100; }

            return '<div class="vas-253-brow" role="listitem" title="' +
                        escapeHtml(rowTooltip(item, name)) + '">' +
                '<div class="vas-253-btop">' +
                    lineCell('vas-253-name', name) +
                    '<span class="vas-253-measure" title="' + escapeHtml(measure) + '">' +
                        escapeHtml(measure) +
                    '</span>' +
                '</div>' +
                '<div class="vas-253-track">' +
                    '<i class="vas-253-fill ' + toneClass(pct) + '" style="width:' +
                        width.toFixed(1) + '%"></i>' +
                '</div>' +
            '</div>';
        }

        /* design.md threshold tones. The class only carries the colour - the percentage
           beside the bar carries the reading. */
        function toneClass(pct) {
            if (pct >= RISK_PCT) { return 'vas-253-risk'; }
            if (pct >= WARN_PCT) { return 'vas-253-warn'; }
            return 'vas-253-ok';
        }

        /* The row's identity. An empty value renders the missing-value dash rather than
           collapsing the line, which would change the row's height and unsettle the
           adaptive row fit. */
        function lineCell(cls, text) {
            if (isBlank(text)) {
                return '<span class="' + cls + ' vas-253-nil">' + NIL + '</span>';
            }

            return '<span class="' + cls + '" title="' + escapeHtml(text) + '">' +
                escapeHtml(text) + '</span>';
        }

        /* The dimension value's name, as the server resolved it from that dimension's own
           master table.

           TWO DIFFERENT KINDS OF EMPTY, and they must not be told the same way. A row with
           NO value (id 0) is postings that carry no value for the selected dimension - a
           real group with real money in it, named from AD_Message. A row that HAS a value
           the master table could not name is a gap in the data, and falls through to the
           missing-value dash like every other unnamed cell on the dashboard - calling it
           "not assigned" would state something that is not true. */
        function valueText(item) {
            var name = item.Label || '';
            if (name) { return name; }

            if ((Number(item.Dimension_ID) || 0) > 0) { return ''; }

            return label('VAS_253_NotAssigned', '(Not assigned)');
        }

        /* "$3.1M of $4.86M · 63.8%" - what was consumed, out of what was approved, and the
           ratio between them. Composed from the same two figures the server computed the
           percentage from, so the line and the bar can never disagree. */
        function measureText(item) {
            return compactAmount(Number(item.Actual) || 0) + ' ' +
                label('VAS_020_Of', 'of') + ' ' +
                compactAmount(Number(item.Budget) || 0) + ' · ' +
                percentText(Number(item.UtilizedPct) || 0);
        }

        /* Everything the row holds, at full precision - the cells above are compact. */
        function rowTooltip(item, name) {
            var lines = [];

            lines.push(isBlank(name) ? NIL : name);
            lines.push(label('VAS_254_Budget', 'Budget') + ': ' + fullAmount(Number(item.Budget) || 0));
            lines.push(label('VAS_252_Actual', 'Actual') + ': ' + fullAmount(Number(item.Actual) || 0));
            lines.push(label('VAS_252_Utilized', 'Utilized') + ': ' +
                percentText(Number(item.UtilizedPct) || 0));

            return lines.join('\n');
        }

        /* ---- Canonical Widget Footer Pager (design.md): "Showing a-b of N" left, compact
             prev / next control right. Hidden on a single page. ---- */
        function paintFooter() {
            if (_totalPages <= 1) { $foot.empty(); return; }

            var from = (_page - 1) * _pageSize + 1;
            var to = Math.min(_page * _pageSize, _totalRows);

            /* "Showing 1-4 of 11 · sorted by utilization" - the note says what the order
               means, which a list ranked by something other than its label needs to state
               somewhere. */
            var showing = label('VAS_020_Showing', 'Showing') + ' ' + from + '-' + to + ' ' +
                label('VAS_020_Of', 'of') + ' ' + _totalRows + ' · ' +
                label('VAS_253_SortedByUtilization', 'sorted by utilization');

            var prevDis = _page <= 1 ? ' disabled' : '';
            var nextDis = _page >= _totalPages ? ' disabled' : '';

            $foot.html(
                '<div class="vas-253-pager">' +
                    '<span class="vas-253-pager-info" title="' + escapeHtml(showing) + '">' +
                        escapeHtml(showing) + '</span>' +
                    '<div class="vas-253-pager-nav">' +
                        '<button type="button" class="vas-253-pgbtn vas-253-pg-prev" aria-label="' +
                            escapeHtml(label('VAS_020_Prev', 'Previous')) + '"' + prevDis + '>' + ICONS.prev + '</button>' +
                        '<span class="vas-253-pager-label">' + _page + ' ' +
                            escapeHtml(label('VAS_020_Of', 'of')) + ' ' + _totalPages + '</span>' +
                        '<button type="button" class="vas-253-pgbtn vas-253-pg-next" aria-label="' +
                            escapeHtml(label('VAS_020_Next', 'Next')) + '"' + nextDis + '>' + ICONS.next + '</button>' +
                    '</div>' +
                '</div>'
            );

            /* A page change keeps both filters - only the page number moves. */
            $foot.find('.vas-253-pg-prev').on('click', function () {
                if (!_loading && _page > 1) { fetchPage(_page - 1); }
            });
            $foot.find('.vas-253-pg-next').on('click', function () {
                if (!_loading && _page < _totalPages) { fetchPage(_page + 1); }
            });
        }

        /* ------------------------------------------------------------ */
        /* Adaptive row capacity (the VAS_020 pattern)                  */
        /* ------------------------------------------------------------ */
        function scheduleSync() {
            var raf = window.requestAnimationFrame || function (cb) { return window.setTimeout(cb, 16); };
            raf(function () { syncCapacity(); });
        }

        function syncCapacity() {
            if (_disposed || _loading || !$list || !$list[0]) { return; }
            if (!_rows || _rows.length === 0) { return; }

            var avail = $list[0].clientHeight;
            if (avail <= 0) {
                /* Layout has not settled yet - try again on the next frame. */
                if (_needsSync) { scheduleSync(); }
                return;
            }

            /* Size off the TALLEST rendered row. Every row here is a line over a track, but
               a long dimension value can still wrap the label on a narrow cell. */
            var rendered = $list[0].querySelectorAll('.vas-253-brow');
            var maxH = 0;
            for (var i = 0; i < rendered.length; i++) {
                if (rendered[i].offsetHeight > maxH) { maxH = rendered[i].offsetHeight; }
            }
            if (maxH > 0) { _rowH = maxH; }
            var rowH = _rowH > 0 ? _rowH : ROW_HEIGHT_FALLBACK;

            _needsSync = false;

            var capacity = Math.floor(avail / rowH);
            if (capacity < MIN_PAGE_SIZE) { capacity = MIN_PAGE_SIZE; }
            if (capacity > MAX_PAGE_SIZE) { capacity = MAX_PAGE_SIZE; }

            if (capacity !== _pageSize) {
                _pageSize = capacity;
                fetchPage(_page);
            }
        }

        function observeList() {
            if (typeof ResizeObserver === 'undefined' || !$list || !$list[0]) { return; }
            if (_listObserver) { try { _listObserver.disconnect(); } catch (e) { /* ignore */ } }

            _listObserver = new ResizeObserver(function () {
                if (_disposed || _loading) { return; }
                _needsSync = true;
                syncCapacity();
            });
            _listObserver.observe($list[0]);
        }

        /* ------------------------------------------------------------ */
        /* Filter pickers - anchored under a pill, on <body>             */
        /* ------------------------------------------------------------ */

        /* ONE panel serves both pills. They are never open at the same time, they are the
           same control at the same tier, and a second panel would be a second set of
           document listeners to leak. */
        function buildPicker() {
            $picker = $('<div class="vas-253-pp vas-253-hidden" role="listbox"></div>');
            $('body').append($picker);

            $picker.on('click', '.vas-253-pp-opt', function () {
                var kind = _pickerKind;
                var key = $(this).attr('data-id');
                closePicker();

                if (kind === PICK_YEAR) { selectYear(parseInt(key, 10) || 0); }
                else { selectDimension(key); }
            });
        }

        function activeButton() {
            return _pickerKind === PICK_YEAR ? $yearBtn : $dimBtn;
        }

        function fillPicker() {
            var isYear = _pickerKind === PICK_YEAR;

            var heading = isYear
                ? label('VAS_256_FinancialYear', 'Financial year')
                : label('VAS_253_Dimension', 'Dimension');

            var html = '<div class="vas-253-pp-h">' + escapeHtml(heading) + '</div>';
            var i;

            if (isYear) {
                if (_years.length === 0) {
                    html += '<div class="vas-253-pp-empty">' +
                        escapeHtml(label('VAS_256_NoYears', 'No financial years available')) + '</div>';
                }

                for (i = 0; i < _years.length; i++) {
                    var y = _years[i];
                    html += optionHtml(String(Number(y.C_Year_ID) || 0), y.FiscalYear || '',
                        Number(y.C_Year_ID) === _yearId);
                }
            }
            else {
                if (_dimensions.length === 0) {
                    html += '<div class="vas-253-pp-empty">' +
                        escapeHtml(label('VAS_253_NoDimensions', 'No accounting dimensions are configured')) + '</div>';
                }

                for (i = 0; i < _dimensions.length; i++) {
                    var d = _dimensions[i];
                    html += optionHtml(d.Value || '', d.Label || '', d.Value === _dimension);
                }
            }

            $picker.attr('aria-label', heading);
            $picker.html(html);
        }

        function optionHtml(key, text, selected) {
            return '<button type="button" class="vas-253-pp-opt" role="option" data-id="' +
                    escapeHtml(key) + '" aria-selected="' + (selected ? 'true' : 'false') + '">' +
                '<span class="vas-253-pp-name" title="' + escapeHtml(text) + '">' +
                    escapeHtml(text) + '</span>' +
                '<span class="vas-253-pp-tick">' + ICONS.tick + '</span>' +
            '</button>';
        }

        /* The panel is fixed and lives on <body>, so it only stays glued to the pill if
           something re-anchors it. The dashboard scrolls in its own container, not the
           window, and scroll events do not bubble - a CAPTURE listener on document is the
           only one that sees every scroll. Scrolling is not a dismissal: the panel travels
           with the pill and closes only on a pick, an outside click or Escape. */
        var _pickerW = 0;
        var _pickerH = 0;

        function measurePicker() {
            $picker.css('max-height', '');
            _pickerW = $picker.outerWidth();
            _pickerH = $picker.outerHeight();
        }

        function positionPicker() {
            var $btn = activeButton();
            if (!$picker || !$btn || !$btn[0]) { return; }

            var rect = $btn[0].getBoundingClientRect();
            var gap = 6;
            var edge = 8;

            var roomBelow = window.innerHeight - rect.bottom - gap - edge;
            var roomAbove = rect.top - gap - edge;

            var below = _pickerH <= roomBelow || roomBelow >= roomAbove;
            var room = below ? roomBelow : roomAbove;

            var ph = _pickerH;
            if (ph > room) {
                ph = Math.max(140, room);
                $picker.css('max-height', ph + 'px');
            } else {
                $picker.css('max-height', '');
            }

            var top = below ? rect.bottom + gap : rect.top - ph - gap;
            /* Right-aligned to the pill: the pills sit at the card's trailing edge, so a
               left-aligned panel would hang off the dashboard. */
            var left = Math.min(rect.right - _pickerW, window.innerWidth - _pickerW - edge);
            left = Math.max(edge, left);

            $picker.css({ left: Math.round(left) + 'px', top: Math.round(top) + 'px' });
        }

        function onAnchorScroll() {
            if (_pickerOpen) { positionPicker(); }
        }

        function openPicker(kind) {
            if (!$picker) { buildPicker(); }

            _pickerKind = kind;
            fillPicker();
            $picker.removeClass('vas-253-hidden');
            _pickerOpen = true;
            measurePicker();

            $(document).on('click' + _ns, onDocumentClick);
            $(document).on('keydown' + _ns, onPickerKeyDown);
            $(window).on('resize' + _ns, positionPicker);
            document.addEventListener('scroll', onAnchorScroll, true);

            positionPicker();
        }

        function closePicker() {
            if (!_pickerOpen) { return; }
            _pickerOpen = false;
            if ($picker) { $picker.addClass('vas-253-hidden'); }

            $(document).off('click' + _ns);
            $(document).off('keydown' + _ns);
            $(window).off('resize' + _ns);
            document.removeEventListener('scroll', onAnchorScroll, true);
        }

        function togglePicker(kind) {
            /* Clicking the OTHER pill while a panel is open moves the panel rather than
               closing it - the two pills are one control row. */
            if (_pickerOpen && _pickerKind === kind) { closePicker(); return; }
            if (_pickerOpen) { closePicker(); }
            openPicker(kind);
        }

        function onDocumentClick(e) {
            if (!$picker) { return; }
            if ($picker[0].contains(e.target)) { return; }
            if ($yearBtn[0] && $yearBtn[0].contains(e.target)) { return; }
            if ($dimBtn[0] && $dimBtn[0].contains(e.target)) { return; }
            closePicker();
        }

        function onPickerKeyDown(e) {
            if (e.key === 'Escape' || e.keyCode === 27) { closePicker(); }
        }

        /* Changing either filter re-reads from page 1 - the row the user was looking at is
           not on the same page of a different year or a different dimension, and the
           ranking has to be recomputed. */
        function selectYear(id) {
            if (id <= 0 || id === _yearId) { return; }

            _yearId = id;
            _fiscalYear = yearNameOf(id);
            paintYearLabel();
            fetchPage(1);
        }

        function selectDimension(value) {
            if (!value || value === _dimension) { return; }

            _dimension = value;
            _dimensionLabel = dimensionNameOf(value);
            paintDimensionLabel();
            fetchPage(1);
        }

        /* ------------------------------------------------------------ */
        /* Formatting - the accounting schema's own currency            */
        /* ------------------------------------------------------------ */
        function precision() {
            var p = _schema ? Number(_schema.Precision) : NaN;
            return (isNaN(p) || p < 0 || p > 6) ? 2 : p;
        }

        function symbol() {
            return (_schema && _schema.Symbol) ? _schema.Symbol : '';
        }

        function isoCode() {
            return (_schema && _schema.Iso) ? _schema.Iso : '';
        }

        /* The compact magnitude with the currency SYMBOL printed directly against it -
           "$1.9M", "₹74K" - never "USD 1.9M", and only falling back to the ISO code when
           the currency has no symbol configured (which is what the server's own
           COALESCE(CurSymbol, ISO_Code) already resolved). The ISO code drives the scale,
           so no lakh/crore or million step is ever assumed here. */
        function compactAmount(value) {
            var v = Number(value) || 0;
            var magnitude;

            try {
                if (VIS.Util && typeof VIS.Util.formatCompactAmount === 'function') {
                    magnitude = VIS.Util.formatCompactAmount(v, isoCode(), precision());
                }
            }
            catch (e) { if (window.console) { console.log(e); } }

            if (magnitude === undefined) { magnitude = String(Math.abs(v)); }

            return (v < 0 ? '−' : '') + symbol() + magnitude;
        }

        /* Full, non-compact amount for the tooltips: the exact figure behind the compact
           cell. Grouping and the decimal separator come from the browser locale, the
           decimals from the schema currency's precision. */
        function fullAmount(value) {
            var v = Number(value) || 0;
            var p = precision();
            var text;

            try {
                text = Math.abs(v).toLocaleString(window.navigator.language,
                    { minimumFractionDigits: p, maximumFractionDigits: p });
            }
            catch (e) { text = String(Math.abs(v)); }

            return (v < 0 ? '−' : '') + symbol() + text;
        }

        /* One decimal, and the separator from the reader's own locale - never a hard-coded
           dot, which reads as a thousands mark in half of Europe. */
        function percentText(value) {
            var v = Number(value) || 0;

            try {
                return v.toLocaleString(window.navigator.language,
                    { minimumFractionDigits: 1, maximumFractionDigits: 1 }) + '%';
            }
            catch (e) { return v.toFixed(1) + '%'; }
        }

        /* ------------------------------------------------------------ */
        /* Helpers                                                      */
        /* ------------------------------------------------------------ */

        /* True when a value is nothing the reader can be shown. A zero is NOT blank: it is
           a figure, and nothing spent against an approved budget is 0% utilized. */
        function isBlank(value) {
            return value === null || value === undefined || String(value) === '';
        }

        /* Every database-sourced string - and a dimension value's name is user-supplied
           text - goes through here before it reaches the DOM. */
        function escapeHtml(s) {
            var v = (s === null || s === undefined) ? '' : String(s);
            return v.replace(/&/g, '&amp;').replace(/"/g, '&quot;')
                .replace(/'/g, '&#39;').replace(/</g, '&lt;').replace(/>/g, '&gt;');
        }

        /* Every user-facing string goes through AD_Message; the fallback keeps the card
           readable when a key has not been seeded yet. */
        function label(key, fallback) {
            try {
                if (VIS.Msg && typeof VIS.Msg.getMsg === 'function') {
                    var v = VIS.Msg.getMsg(key);
                    if (v && v !== key && v.charAt(0) !== '[') { return v; }
                }
            }
            catch (e) { /* ignore */ }
            return fallback;
        }

        this.getRoot = function () { return $root; };

        /* Release everything that outlives the card: the body-mounted picker, the document
           and window listeners it registers, and both observers - a ResizeObserver left
           running keeps the whole subtree alive. */
        this.releasePanel = function () {
            _disposed = true;
            closePicker();

            if (_rootObserver) {
                try { _rootObserver.disconnect(); } catch (e) { /* ignore */ }
                _rootObserver = null;
            }
            if (_listObserver) {
                try { _listObserver.disconnect(); } catch (e) { /* ignore */ }
                _listObserver = null;
            }

            if ($picker) { $picker.off(); $picker.remove(); $picker = null; }
            if ($busy) { $busy.remove(); $busy = null; }
            if ($yearBtn) { $yearBtn.off(_ns); }
            if ($dimBtn) { $dimBtn.off(_ns); }
            if ($foot) { $foot.off(); }

            _rows = [];
            _years = [];
            _dimensions = [];
        };
    };

    /* ---------------------------------------------------------------- */
    /* Required prototype hooks (same surface as other VAS widgets)     */
    /* ---------------------------------------------------------------- */
    VAS.VAS_253_UtlizationbyDimensionWidget.prototype.init = function (windowNo, frame) {
        this.frame = frame;
        this.widgetInfo = frame.widgetInfo;
        this.windowNo = windowNo;
        this.initalize();
        this.frame.getContentGrid().append(this.getRoot());

        /* Self-wire the dashboard-width CSS variable the header clamps read. */
        ensureDashInlineSizeVar(this.getRoot());

        this.intialLoad();
    };

    /* No prototype refreshWidget: the constructor already defines the instance method,
       which shadows anything on the prototype. A prototype version calling
       this.refreshWidget() would be unreachable at best and infinite recursion the day
       the instance one is removed. */

    VAS.VAS_253_UtlizationbyDimensionWidget.prototype.widgetSizeChange = function (widget) {
        this.widgetInfo = widget;
    };

    VAS.VAS_253_UtlizationbyDimensionWidget.prototype.dispose = function () {
        try { this.releasePanel(); } catch (e) { /* ignore */ }
        if (this.frame && typeof this.frame.dispose === 'function') {
            try { this.frame.dispose(); } catch (e) { /* ignore */ }
        }
        this.frame = null;
        this.windowNo = null;
    };

})(VAS, jQuery);
