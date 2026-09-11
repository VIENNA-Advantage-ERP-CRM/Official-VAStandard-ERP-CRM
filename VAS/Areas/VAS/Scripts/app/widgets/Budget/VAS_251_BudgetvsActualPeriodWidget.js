/************************************************************
 * Module Name    : VAS
 * Purpose        : Budget vs Actual by Period - a 6x2 grouped bar chart with a
 *                  period drill-down, for the Budgeting dashboard.
 *
 *                  Approved budget against posted actual, one pair of bars per
 *                  accounting period of the selected financial year:
 *
 *                    [▮] Budget vs actual by period            [ FY 2026 v ]
 *                        Expense budget against actual · 64.1% utilized · posted through Aug-26
 *
 *                        ▁▁ ▄▄ ▆▆ ██ ██ ▆▆ ▄▄ ▂▂ ▁  ▁  ▁  ▁
 *                        Jan Feb Mar Apr May Jun Jul Aug Sep Oct Nov Dec
 *                        ▪ Approved budget  ▪ Actual   Select a period …
 *
 *                  EVERY ACTIVE PERIOD IS DRAWN, POSTED OR NOT. A period with no
 *                  postings still has an approved budget worth seeing, and a chart
 *                  that dropped its empty months would change shape as the year
 *                  filled in - the axis is the year, not the data.
 *
 *                  THE PERIODS ARE THE CALENDAR'S, NOT MONTHS. They come from
 *                  C_Period in StartDate order, so a 4-4-5 calendar, a 13-period
 *                  year or an adjusting period all draw correctly with no special
 *                  case, and no month arithmetic happens anywhere.
 *
 *                  ONLY A POSTED PERIOD OPENS. A bar is a button when its period
 *                  carries ACTUAL postings and a plain div when it does not - decided
 *                  by the posting COUNT, never by the amount, because a period whose
 *                  postings net to nothing has still been posted to and its accounts
 *                  are still worth reading. A dead control that looks live is worse
 *                  than no control.
 *
 *                  THE BARS ARE MAGNITUDES, THE FIGURES ARE SIGNED. Amounts keep the
 *                  accounting sign convention - no ABS is applied to them - but a bar
 *                  is a SIZE and this chart's floor is zero, so a period that netted
 *                  negative still draws the height it moved and says so in its
 *                  tooltip. Both series share one scale, which is the whole point of
 *                  a grouped chart.
 *
 *                  BUDGETED LEDGER ACCOUNTS ONLY, ON BOTH SERIES. An account reaches
 *                  this chart - budget bar or actual bar - only when it carries a
 *                  budget posting somewhere in the selected financial year. An actual
 *                  on an account nobody budgeted has no approved figure behind it, and
 *                  left in it would raise a period's actual bar against a budget bar
 *                  that never moved; that spending belongs to the unbudgeted card
 *                  (VAS_256). The test's window is the YEAR, so an annual budget booked
 *                  in one period still admits that account's actuals in every other.
 *                  The server decides the set; this file never re-tests it.
 *
 *                  THE MODAL RECONCILES BY CONSTRUCTION. Its four metrics and its
 *                  account list are read from the SERVER for that period, not passed
 *                  in from the bar, so "Total posted" cannot drift from the bar that
 *                  opened it however the chart was rendered - and it is scoped to the
 *                  same budgeted accounts, so it can never list one the bar did not
 *                  count.
 *
 *                  ONE CURRENCY, THE SCHEMA'S OWN, AND ITS SYMBOL. Every figure is an
 *                  accounting amount in the primary accounting schema's currency,
 *                  printed with that currency's SYMBOL against the number - "$950K",
 *                  never "USD 950K" - falling back to the ISO code only when the
 *                  currency has no symbol configured. Nothing is converted and no
 *                  scale is assumed: the ISO code decides whether the compact form
 *                  steps in lakh/crore or thousand/million.
 *
 *                  Design: design.md -> dashboard-widgets.md (Glass Widget, Widget
 *                  Header, No Inner Scrollbars) supplies the shell and the header
 *                  typography; windows-and-panels.md (Panel Foundation) supplies the
 *                  modal; the widget specification supplies the chart anatomy, its
 *                  named series colours - budget #BFE4FF, actual #0083DA - and the
 *                  drill-down grid.
 *
 *                  Summary Message Table
 *                  Rows marked (reuse) already exist under another key and are NOT
 *                  duplicated here.
 *                   # | Current Text                       | Message Key
 *                  ---+------------------------------------+--------------------------
 *                   1 | Budget vs actual by period         | VAS_251_BudgetVsActual
 *                   2 | Expense budget against actual      | VAS_251_BudgetVsActualHint
 *                   3 | utilized                           | VAS_251_UtilizedLower
 *                   4 | posted through                     | VAS_251_PostedThrough
 *                   5 | no expense postings yet            | VAS_251_NoActualYet
 *                   6 | No budget or actual postings for   | VAS_251_NoPostings
 *                     |   the selected fiscal year         |
 *                   7 | Approved budget                    | VAS_251_ApprovedBudget
 *                   8 | Select a period for posted         | VAS_251_SelectPeriod
 *                     |   accounts                         |
 *                   9 | posted ledger accounts             | VAS_251_PostedAccounts
 *                  10 | Period actual reconciles to the    | VAS_251_ReconcilesTo
 *                  11 | budget vs actual chart             | VAS_251_ChartName
 *                  12 | Total posted                       | VAS_251_TotalPosted
 *                  13 | Postings                           | VAS_251_Postings
 *                  14 | No accounts were posted in this    | VAS_251_NoDetail
 *                     |   period.                          |
 *                  15 | Account                            | VAS_234_Account         (reuse)
 *                  16 | Organization Unit                  | VAS_256_OrganizationUnit(reuse)
 *                  17 | Amount                             | VAS_235_Amount          (reuse)
 *                  18 | Budget                             | VAS_254_Budget          (reuse)
 *                  19 | Actual                             | VAS_252_Actual          (reuse)
 *                  20 | Variance                           | VAS_254_Variance        (reuse)
 *                  21 | Utilized                           | VAS_252_Utilized        (reuse)
 *                  22 | Close                              | VAS_235_Close           (reuse)
 *                  23 | Showing                            | VAS_020_Showing         (reuse)
 *                  24 | of                                 | VAS_020_Of              (reuse)
 *                  25 | Previous                           | VAS_020_Prev            (reuse)
 *                  26 | Next                               | VAS_020_Next            (reuse)
 *                  27 | Financial year                     | VAS_256_FinancialYear   (reuse)
 *                  28 | No financial years available       | VAS_256_NoYears         (reuse)
 *                  29 | No primary calendar is configured  | VAS_256_NoCalendar      (reuse)
 *                  30 | No primary accounting schema is    | VAS_256_NoAcctSchema    (reuse)
 *                     |   configured                       |
 *                  31 | Couldn't load                      | VAS_192_CouldntLoad     (reuse)
 *
 * Chronological development:
 *   VAI145         Created  Date 2026-09-09
 ***********************************************************/
; VAS = window.VAS || {};

; (function (VAS, $) {

    /* CSS lives in VAS/Areas/VAS/Content/VAS_251_BudgetvsActualPeriodWidget.css. All classes
       are namespaced `vas-251-` so they never collide with sibling widgets. */

    /* design.md §Widget Header / §Measurement Setup: keep --dash-inline-size on :root equal
       to the dashboard container's current pixel width so the header clamps resolve against
       the dashboard's visible content area, not the viewport. One document-level observer
       serves every widget. */
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

    /* Inline SVG, not an icon-font class - the host shell does not always load an icon font
       and a missing glyph leaves an empty box. Explicit width/height as well as a viewBox: an
       SVG with only a viewBox falls back to 300x150px if a stylesheet is stale, which would
       sprawl across the header. */
    var ICONS = {
        /* Bars on an axis: the card is a chart, and its icon says so. */
        chart: '<svg width="18" height="18" viewBox="0 0 24 24" fill="none" stroke="currentColor" ' +
            'stroke-width="1.8" stroke-linecap="round" stroke-linejoin="round" aria-hidden="true" focusable="false">' +
            '<path d="M3 3v18h18"></path><path d="M7 14v4"></path><path d="M12 9v9"></path>' +
            '<path d="M17 5v13"></path></svg>',
        chevron: '<svg width="11" height="11" viewBox="0 0 24 24" fill="none" stroke="currentColor" ' +
            'stroke-width="2.2" stroke-linecap="round" stroke-linejoin="round" aria-hidden="true" focusable="false">' +
            '<polyline points="6 9 12 15 18 9"></polyline></svg>',
        tick: '<svg width="16" height="16" viewBox="0 0 24 24" fill="none" stroke="currentColor" ' +
            'stroke-width="2.4" stroke-linecap="round" stroke-linejoin="round" aria-hidden="true" focusable="false">' +
            '<polyline points="20 6 9 17 4 12"></polyline></svg>',
        close: '<svg width="15" height="15" viewBox="0 0 24 24" fill="none" stroke="currentColor" ' +
            'stroke-width="2.2" stroke-linecap="round" stroke-linejoin="round" aria-hidden="true" focusable="false">' +
            '<path d="M18 6 6 18"></path><path d="M6 6l12 12"></path></svg>',
        prev: '<svg width="14" height="14" viewBox="0 0 24 24" fill="none" stroke="currentColor" ' +
            'stroke-width="2.6" stroke-linecap="round" stroke-linejoin="round" aria-hidden="true" focusable="false">' +
            '<polyline points="15 18 9 12 15 6"></polyline></svg>',
        next: '<svg width="14" height="14" viewBox="0 0 24 24" fill="none" stroke="currentColor" ' +
            'stroke-width="2.6" stroke-linecap="round" stroke-linejoin="round" aria-hidden="true" focusable="false">' +
            '<polyline points="9 18 15 12 9 6"></polyline></svg>'
    };

    /* The modal's rows region is paged, never scrolled - the panel is a fixed height, so the
       number of rows that fit is measured from it rather than guessed at. These are only the
       floor and the pre-measurement fallback. */
    var DLG_MIN_PAGE_SIZE = 3;
    var DLG_ROW_HEIGHT_FALLBACK = 34;

    /* The missing-value placeholder. A GLYPH rather than a word: it needs no AD_Message key
       and reads the same in every language. */
    var NIL = '—';

    VAS.VAS_251_BudgetvsActualPeriodWidget = function () {
        this.frame;
        this.windowNo;
        this.widgetInfo;

        var $self = this;
        var $root;
        var $card;
        var $yearBtn;
        var $plot;
        var $axis;
        var $state;
        var $busy;
        var $picker;
        var $dlg;

        var widgetID = 0;

        /* Unique event namespace per instance - a widget can sit twice on one dashboard, and
           both the picker and the modal bind document-level handlers. */
        var _ns = '';

        var _periods = [];
        var _years = [];
        var _schema = null;
        var _yearId = 0;
        var _fiscalYear = '';
        var _periodCount = 0;
        var _postedCount = 0;
        var _utilized = 0;
        var _hasUtilization = false;
        var _postedThrough = '';

        var _loading = false;
        var _pickerOpen = false;
        var _disposed = false;
        var _rootObserver = null;

        /* Modal state. The trigger is kept only so the bar that opened the panel can be
           blurred - focus is NOT returned to it when the panel closes. */
        var $dlgRows;
        var $dlgFoot;

        var _dlgPeriodId = 0;
        var _dlgLoading = false;
        var _dlgTrigger = null;

        /* The account list, held in full and PAGED in the panel - the read is one round trip
           and turning a page costs nothing. */
        var _dlgRows = [];
        var _dlgTotalRows = 0;
        var _dlgPage = 1;
        var _dlgPageSize = DLG_MIN_PAGE_SIZE;
        var _dlgTotalPages = 0;
        var _dlgRowH = 0;
        var _dlgNeedsSync = false;
        var _dlgObserver = null;

        /* ------------------------------------------------------------ */
        /* Lifecycle                                                    */
        /* ------------------------------------------------------------ */
        this.initalize = function () {
            widgetID = (VIS.Utility && VIS.Utility.Util
                ? VIS.Utility.Util.getValueOfInt($self.widgetInfo.AD_UserHomeWidgetID)
                : 0);
            if (widgetID === 0) { widgetID = $self.windowNo; }
            _ns = '.vas251_' + widgetID;

            buildSkeleton();
            buildDialog();
            createBusyIndicator();
            setupRootObserver();
        };

        /* The framework's own widget loader, overlaid on the whole card while a read is in
           flight - the same treatment every sibling VAS widget gives its loads. The shell
           stays put underneath it, so nothing shifts when the bars land. */
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

        /* Publishes THIS widget's own pixel width as --widget-inline-size on its root, which
           is the first variable the card's font-size clamp reads (the dashboard width is only
           the fallback). Every sibling widget does exactly this - without it the card measures
           itself against the whole dashboard and renders a size larger than its neighbours.

           The chart itself needs no redraw on resize: the plot is flex and every bar height is
           a percentage, so the whole thing reflows in CSS. */
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
            fetchChart();
        };

        /* The dashboard's Refresh button calls this. The chosen YEAR is kept - that is a
           filter the user set - and any open modal is closed first: its accounts belong to a
           read that is about to be replaced. */
        this.refreshWidget = function () {
            closePicker();
            closeDialog();
            fetchChart();
        };

        /* ------------------------------------------------------------ */
        /* DOM skeleton                                                 */
        /* ------------------------------------------------------------ */
        function buildSkeleton() {
            $root = $('<div class="vas-251-root" id="vas-251-root-' + widgetID + '"></div>');

            var title = label('VAS_251_BudgetVsActual', 'Budget vs actual by period');

            $card = $(
                '<div class="vas-251-card">' +
                    '<div class="vas-251-header">' +
                        '<span class="vas-251-icon">' + ICONS.chart + '</span>' +
                        '<div class="vas-251-head-text">' +
                            '<div class="vas-251-title"></div>' +
                            '<div class="vas-251-subtitle"></div>' +
                        '</div>' +
                        '<button type="button" class="vas-251-year" aria-haspopup="listbox">' +
                            '<span class="vas-251-year-label"></span>' +
                            ICONS.chevron +
                        '</button>' +
                    '</div>' +
                    '<div class="vas-251-body">' +
                        '<div class="vas-251-plot" role="list"></div>' +
                        '<div class="vas-251-xaxis" aria-hidden="true"></div>' +
                        '<div class="vas-251-legend">' +
                            '<span class="vas-251-lg">' +
                                '<i class="vas-251-sw vas-251-sw--budget"></i>' +
                                '<span class="vas-251-lg-t"></span>' +
                            '</span>' +
                            '<span class="vas-251-lg">' +
                                '<i class="vas-251-sw vas-251-sw--actual"></i>' +
                                '<span class="vas-251-lg-t"></span>' +
                            '</span>' +
                            '<span class="vas-251-hint"></span>' +
                        '</div>' +
                    '</div>' +
                    '<div class="vas-251-state vas-251-hidden"></div>' +
                '</div>'
            );

            $card.find('.vas-251-title').text(title).attr('title', title);

            $yearBtn = $card.find('.vas-251-year');
            $plot = $card.find('.vas-251-plot');
            $axis = $card.find('.vas-251-xaxis');
            $state = $card.find('.vas-251-state');

            $plot.attr('aria-label', title);
            paintLegend();
            paintYearLabel();

            $yearBtn.on('click' + _ns, function (e) {
                e.preventDefault();
                e.stopPropagation();
                togglePicker();
            });

            /* Delegated: the columns are rebuilt on every read, so the handler is bound once
               here rather than per bar. Only a posted period renders as a button, so only a
               posted period can ever reach this. */
            $plot.on('click' + _ns, '.vas-251-col', function () {
                openDialog(parseInt($(this).attr('data-period'), 10) || 0, this);
            });

            $root.append($card);
        }

        /* The legend names the two series and the hint says what a bar does. Both are
           AD_Message text, so neither is a word this file spells out. */
        function paintLegend() {
            var $lg = $card.find('.vas-251-lg-t');
            $lg.eq(0).text(label('VAS_251_ApprovedBudget', 'Approved budget'));
            $lg.eq(1).text(label('VAS_252_Actual', 'Actual'));

            var hint = label('VAS_251_SelectPeriod', 'Select a period for posted accounts');
            $card.find('.vas-251-hint').text(hint).attr('title', hint);
        }

        /* ------------------------------------------------------------ */
        /* Data                                                         */
        /* ------------------------------------------------------------ */
        function fetchChart() {
            if (_loading) { return; }
            _loading = true;
            showBusyIndicator();

            $.ajax({
                url: VIS.Application.contextUrl + 'VAS_251_BudgetvsActualPeriodWidget/GetChart',
                type: 'GET',
                dataType: 'json',
                cache: false,
                /* Asynchronous, always - nothing here justifies blocking the UI thread. */
                async: true,
                data: { yearId: _yearId },
                success: function (raw) {
                    _loading = false;
                    if (_disposed) { return; }
                    hideBusyIndicator();

                    var data = parseResponse(raw);
                    if (!data || data.error) {
                        renderState(label('VAS_192_CouldntLoad', "Couldn't load"));
                        return;
                    }

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

                    /* The year list comes back with every read, so a newly opened financial
                       year appears on the next refresh without a second endpoint. */
                    _years = data.Years || [];
                    _yearId = Number(data.C_Year_ID) || 0;
                    _fiscalYear = data.FiscalYear || '';
                    paintYearLabel();

                    _periods = data.Periods || [];
                    _periodCount = Number(data.PeriodCount) || 0;
                    _postedCount = Number(data.PostedPeriodCount) || 0;
                    _utilized = Number(data.UtilizedPct) || 0;
                    _hasUtilization = !!data.HasUtilization;
                    _postedThrough = data.PostedThrough || '';

                    paintSubtitle();
                    paintChart();
                },
                error: function () {
                    _loading = false;
                    if (_disposed) { return; }
                    /* The overlay comes down on failure too - a spinner left running over an
                       error the user cannot see is the worst of both. And no stale bar is left
                       standing as though it were current: renderState takes the body over. */
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
        /* Render - the chart                                           */
        /* ------------------------------------------------------------ */

        /* A load failure or a missing configuration takes the card over. A year with nothing
           posted does NOT - that is said inside the plot, so the header and the year pill stay
           put and the reader can pick another year. */
        function renderState(text) {
            $card.find('.vas-251-body').addClass('vas-251-hidden');
            $state.removeClass('vas-251-hidden').text(text);
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
            return label('VAS_192_CouldntLoad', "Couldn't load");
        }

        function paintYearLabel() {
            var text = _fiscalYear || yearNameOf(_yearId);
            $yearBtn.find('.vas-251-year-label').text(text);
            $yearBtn.attr('title', label('VAS_256_FinancialYear', 'Financial year') + ': ' + text);
        }

        function yearNameOf(id) {
            for (var i = 0; i < _years.length; i++) {
                if (Number(_years[i].C_Year_ID) === id) { return _years[i].FiscalYear || ''; }
            }
            return '';
        }

        /* "Expense budget against actual · 64.1% utilized · posted through Aug-26".

           The first clause says what the chart IS - expense accounts only, budget against
           actual - and stands alone until the year is read. It is kept SHORT on purpose:
           the subtitle is one non-wrapping line sharing its row with the year pill, and a
           longer hint pushes the utilization and posted-through clauses - the figures the
           reader actually came for - past the ellipsis. The middle and last clauses are
           earned, not assumed: a year with no actual postings yet says so instead of
           printing "0.0% utilized · posted through" and a dangling blank, and a year with
           actuals but no budget prints a dash for a ratio nobody can compute. */
        function paintSubtitle() {
            var $sub = $card.find('.vas-251-subtitle');

            var text = label('VAS_251_BudgetVsActualHint', 'Expense budget against actual');

            if (_periodCount === 0) { $sub.text(text).attr('title', text); return; }

            if (_postedCount === 0) {
                text += ' · ' + label('VAS_251_NoActualYet', 'no expense postings yet');
            }
            else {
                text += ' · ' + (_hasUtilization ? percentText(_utilized, 1) : NIL) + ' ' +
                    label('VAS_251_UtilizedLower', 'utilized');

                if (_postedThrough) {
                    text += ' · ' + label('VAS_251_PostedThrough', 'posted through') + ' ' +
                        _postedThrough;
                }
            }

            /* The subtitle truncates in a cell that is already sharing its header row with the
               year pill, so the full sentence also goes on the title attribute - it moves to
               the tooltip rather than being lost. */
            $sub.text(text).attr('title', text);
        }

        function paintChart() {
            $state.addClass('vas-251-hidden');
            $card.find('.vas-251-body').removeClass('vas-251-hidden');

            /* Nothing budgeted and nothing posted is a real answer, not an error - and an
               empty plot with an axis under it would read as a rendering fault. */
            if (!_periods || _periods.length === 0 || !hasAnyFigure()) {
                $plot.html('<div class="vas-251-empty">' +
                    escapeHtml(label('VAS_251_NoPostings',
                        'No budget or actual postings for the selected fiscal year')) + '</div>');
                $axis.empty();
                return;
            }

            var bars = '';
            var axis = '';

            for (var i = 0; i < _periods.length; i++) {
                bars += columnHtml(_periods[i]);
                axis += axisHtml(_periods[i]);
            }

            $plot.html(bars);
            $axis.html(axis);
        }

        /* True when the year has any figure at all to draw. A budget alone is enough - the
           bars are worth showing before anything is posted. */
        function hasAnyFigure() {
            for (var i = 0; i < _periods.length; i++) {
                if ((Number(_periods[i].Budget) || 0) !== 0) { return true; }
                if ((Number(_periods[i].Actual) || 0) !== 0) { return true; }
                if ((Number(_periods[i].ActualCount) || 0) > 0) { return true; }
            }
            return false;
        }

        /* One period's pair of bars. A BUTTON when the period carries actual postings and a
           plain div when it does not: a period nobody has posted to has no accounts to open,
           and a control that does nothing is worse than no control. The heights are the
           server's - both series scaled together - so the client never re-derives them. */
        function columnHtml(item) {
            var posted = !!item.HasActual;
            var periodId = Number(item.C_Period_ID) || 0;

            var budgetPct = clampPct(item.BudgetBarPct);
            var actualPct = clampPct(item.ActualBarPct);

            var tag = posted ? 'button' : 'div';
            var attrs = 'class="vas-251-col' + (posted ? '' : ' vas-251-col--flat') + '" role="listitem"' +
                ' title="' + escapeHtml(periodTooltip(item)) + '"';

            if (posted) {
                attrs += ' type="button" data-period="' + periodId + '"' +
                    ' aria-label="' + escapeHtml(periodAriaLabel(item)) + '"';
            }

            return '<' + tag + ' ' + attrs + '>' +
                '<i class="vas-251-bar vas-251-bar--budget" style="height:' + budgetPct.toFixed(1) + '%"></i>' +
                '<i class="vas-251-bar vas-251-bar--actual" style="height:' + actualPct.toFixed(1) + '%"></i>' +
            '</' + tag + '>';
        }

        function axisHtml(item) {
            var name = item.Name || '';
            return '<span title="' + escapeHtml(name) + '">' + escapeHtml(name) + '</span>';
        }

        function clampPct(value) {
            var v = Number(value) || 0;
            if (v < 0) { return 0; }
            return v > 100 ? 100 : v;
        }

        /* Everything the period holds, at full precision - the bars are only a proportion. */
        function periodTooltip(item) {
            var budget = Number(item.Budget) || 0;
            var actual = Number(item.Actual) || 0;

            var lines = [item.Name || NIL];

            lines.push(label('VAS_254_Budget', 'Budget') + ': ' + fullAmount(budget));
            lines.push(label('VAS_252_Actual', 'Actual') + ': ' + fullAmount(actual));
            lines.push(label('VAS_254_Variance', 'Variance') + ': ' + signedFullAmount(budget - actual));
            lines.push(label('VAS_252_Utilized', 'Utilized') + ': ' +
                (budget !== 0 ? percentText(actual * 100 / budget, 1) : NIL));

            return lines.join('\n');
        }

        /* What a screen reader announces on a posted bar: the period, and that activating it
           opens its accounts. */
        function periodAriaLabel(item) {
            return (item.Name || '') + ' — ' +
                label('VAS_251_PostedAccounts', 'posted ledger accounts');
        }

        /* ------------------------------------------------------------ */
        /* Financial year picker - anchored under the pill, on <body>    */
        /* ------------------------------------------------------------ */
        function buildPicker() {
            $picker = $('<div class="vas-251-pp vas-251-hidden" role="listbox" aria-label="' +
                escapeHtml(label('VAS_256_FinancialYear', 'Financial year')) + '"></div>');
            $('body').append($picker);

            $picker.on('click', '.vas-251-pp-opt', function () {
                var id = parseInt($(this).attr('data-id'), 10) || 0;
                closePicker();
                selectYear(id);
            });
        }

        function fillPicker() {
            var html = '<div class="vas-251-pp-h">' +
                escapeHtml(label('VAS_256_FinancialYear', 'Financial year')) + '</div>';

            if (_years.length === 0) {
                html += '<div class="vas-251-pp-empty">' +
                    escapeHtml(label('VAS_256_NoYears', 'No financial years available')) + '</div>';
            }

            for (var i = 0; i < _years.length; i++) {
                var y = _years[i];
                html += optionHtml(Number(y.C_Year_ID) || 0, y.FiscalYear || '');
            }

            $picker.html(html);
        }

        function optionHtml(id, text) {
            var selected = id === _yearId;

            return '<button type="button" class="vas-251-pp-opt" role="option" data-id="' + id +
                    '" aria-selected="' + (selected ? 'true' : 'false') + '">' +
                '<span class="vas-251-pp-name" title="' + escapeHtml(text) + '">' +
                    escapeHtml(text) + '</span>' +
                '<span class="vas-251-pp-tick">' + ICONS.tick + '</span>' +
            '</button>';
        }

        /* The panel is fixed and lives on <body>, so it only stays glued to the pill if
           something re-anchors it. The dashboard scrolls in its own container, not the window,
           and scroll events do not bubble - a CAPTURE listener on document is the only one
           that sees every scroll. Scrolling is not a dismissal: the panel travels with the
           pill and closes only on a pick, an outside click or Escape. */
        var _pickerW = 0;
        var _pickerH = 0;

        function measurePicker() {
            $picker.css('max-height', '');
            _pickerW = $picker.outerWidth();
            _pickerH = $picker.outerHeight();
        }

        function positionPicker() {
            if (!$picker || !$yearBtn || !$yearBtn[0]) { return; }

            var rect = $yearBtn[0].getBoundingClientRect();
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
            /* Right-aligned to the pill: it sits at the card's trailing edge, so a
               left-aligned panel would hang off the dashboard. */
            var left = Math.min(rect.right - _pickerW, window.innerWidth - _pickerW - edge);
            left = Math.max(edge, left);

            $picker.css({ left: Math.round(left) + 'px', top: Math.round(top) + 'px' });
        }

        function onAnchorScroll() {
            if (_pickerOpen) { positionPicker(); }
        }

        function openPicker() {
            if (!$picker) { buildPicker(); }

            fillPicker();
            $picker.removeClass('vas-251-hidden');
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
            if ($picker) { $picker.addClass('vas-251-hidden'); }

            $(document).off('click' + _ns);
            $(document).off('keydown' + _ns);
            $(window).off('resize' + _ns);
            document.removeEventListener('scroll', onAnchorScroll, true);
        }

        function togglePicker() {
            if (_pickerOpen) { closePicker(); } else { openPicker(); }
        }

        function onDocumentClick(e) {
            if (!$picker) { return; }
            if ($picker[0].contains(e.target)) { return; }
            if ($yearBtn[0] && $yearBtn[0].contains(e.target)) { return; }
            closePicker();
        }

        function onPickerKeyDown(e) {
            if (e.key === 'Escape' || e.keyCode === 27) { closePicker(); }
        }

        /* Changing the year re-reads everything and drops any open drill-down: its accounts
           belong to a period of the year being left behind. */
        function selectYear(id) {
            if (id <= 0 || id === _yearId) { return; }

            _yearId = id;
            _fiscalYear = yearNameOf(id);
            paintYearLabel();

            closeDialog();
            fetchChart();
        }

        /* ------------------------------------------------------------ */
        /* Drill-down modal                                             */
        /* ------------------------------------------------------------ */
        function buildDialog() {
            $dlg = $(
                '<div class="vas-251-dlg-wrap vas-251-hidden">' +
                    '<div class="vas-251-dlg-backdrop"></div>' +
                    '<div class="vas-251-dlg" role="dialog" aria-modal="true" ' +
                            'aria-labelledby="vas-251-dlg-title-' + widgetID + '">' +
                        '<div class="vas-251-dlg-head">' +
                            /* THE CARD'S OWN ICON WELL, IN THE PANEL HEAD. The modal is the
                               chart opened up, not a window of its own, and the well is what
                               says so at a glance - the same #EAF8FF tile and the same chart
                               glyph the card is headed with, sized off the panel's px chrome
                               rather than the card's em lever. */
                            '<span class="vas-251-icon vas-251-dlg-icon" aria-hidden="true">' +
                                ICONS.chart +
                            '</span>' +
                            '<div class="vas-251-dlg-titles">' +
                                '<div class="vas-251-dlg-title" id="vas-251-dlg-title-' + widgetID + '"></div>' +
                                '<div class="vas-251-dlg-meta"></div>' +
                            '</div>' +
                            '<button type="button" class="vas-251-x" aria-label="' +
                                escapeHtml(label('VAS_235_Close', 'Close')) + '">' + ICONS.close + '</button>' +
                        '</div>' +
                        /* THE PANEL IS A FIXED HEIGHT AND NOTHING INSIDE IT SCROLLS. The
                           metrics and the column header are chrome and stay put; only the
                           ROWS region changes between pages, and it is paged rather than
                           scrolled - which is what stops the column header from scrolling
                           away from the figures it names. The total and the pager are bands
                           of their own BELOW the rows, so the total sits just above the
                           footer instead of travelling with the list. */
                        '<div class="vas-251-dlg-bodywrap">' +
                            '<div class="vas-251-dlg-body">' +
                                '<div class="vas-251-metrics"></div>' +
                                '<div class="vas-251-dg-h vas-251-dg-row vas-251-hidden" role="row"></div>' +
                                '<div class="vas-251-rows" role="rowgroup"></div>' +
                            '</div>' +
                            '<div class="vas-251-dlg-busy vis-busyindicatorouterwrap vas-251-hidden">' +
                                '<div class="vis-busyindicatorinnerwrap"><i class="vis_widgetloader"></i></div>' +
                            '</div>' +
                        '</div>' +
                        '<div class="vas-251-dg-total vas-251-hidden">' +
                            '<span class="vas-251-dg-total-l"></span>' +
                            '<span class="vas-251-dg-total-v"></span>' +
                        '</div>' +
                        '<div class="vas-251-dlg-foot"></div>' +
                    '</div>' +
                '</div>'
            );

            $('body').append($dlg);

            $dlgRows = $dlg.find('.vas-251-rows');
            $dlgFoot = $dlg.find('.vas-251-dlg-foot');

            /* Wrapped rather than passed straight through: jQuery hands the handler the event
               object, which would arrive as closeDialog's own argument. */
            $dlg.find('.vas-251-x').on('click', function () { closeDialog(); });

            /* THE BACKDROP DELIBERATELY DOES NOT DISMISS - no handler is bound to it at all.
               The panel is a paged list of accounts the reader works down, and a stray click
               on the surround throwing that away is a worse failure than the extra click it
               would have saved. Escape and the close button remain. (VAS_235's drill-down is
               the same, for the same reason.) */
        }

        function openDialog(periodId, trigger) {
            if (periodId <= 0) { return; }

            _dlgPeriodId = periodId;
            _dlgTrigger = trigger || null;
            _dlgRows = [];
            _dlgPage = 1;
            _dlgTotalPages = 0;

            /* The title is known from the bar before the accounts arrive, so the modal opens
               named rather than blank. */
            var name = periodNameOf(periodId);
            $dlg.find('.vas-251-dlg-title').text(
                name + ' · ' + label('VAS_251_PostedAccounts', 'posted ledger accounts'));
            $dlg.find('.vas-251-dlg-meta').text(metaText());

            $dlg.find('.vas-251-metrics').empty();
            $dlg.find('.vas-251-dg-h').empty().addClass('vas-251-hidden');
            $dlg.find('.vas-251-dg-total').addClass('vas-251-hidden');
            $dlgRows.empty();
            $dlgFoot.empty();

            $dlg.removeClass('vas-251-hidden');
            $(document).on('keydown' + _ns + '_dlg', onDialogKeyDown);

            /* NOTHING BEHIND THE SCRIM STAYS LIT. The class suppresses the column's hover and
               focus treatment for as long as the panel is up - a belt to the focus move's
               braces, because the pointer is still sitting on the bar that was just clicked
               and a lit control outside the dialog reads as though it were still the one in
               play. */
            $root.addClass('vas-251-modal-open');

            blurWidgetFocus();

            fetchDetail(periodId);
            focusDialog();
        }

        /* THE WIDGET BEHIND GIVES UP FOCUS. The bar that opened the modal is a button and
           keeps its focus ring otherwise - a lit control behind a scrim, outside the dialog
           the reader is now in. Whatever inside the card holds focus is blurred, not only the
           trigger: the year pill can be the active element too. */
        function blurWidgetFocus() {
            var active = document.activeElement;

            if (active && $root && $root[0] && $root[0].contains(active) &&
                    typeof active.blur === 'function') {
                try { active.blur(); } catch (e) { /* ignore */ }
            }

            if (_dlgTrigger && typeof _dlgTrigger.blur === 'function') {
                try { _dlgTrigger.blur(); } catch (e) { /* ignore */ }
            }
        }

        /* Focus lands on the close button, which is what aria-modal promises a screen reader.
           It is set TWICE - once now and once on the next frame - because the click that
           opened the panel is still bubbling through the dashboard shell, and a host handler
           that focuses the widget frame on its way past would otherwise pull focus straight
           back out of the dialog. The second pass is a no-op when nothing did. */
        function focusDialog() {
            var focusClose = function () {
                if (_disposed || !$dlg || $dlg.hasClass('vas-251-hidden')) { return; }

                var x = $dlg.find('.vas-251-x')[0];
                if (!x || typeof x.focus !== 'function') { return; }
                if (document.activeElement === x) { return; }

                try { x.focus({ preventScroll: true }); }
                catch (e) { try { x.focus(); } catch (e2) { /* ignore */ } }
            };

            focusClose();

            var raf = window.requestAnimationFrame || function (cb) { return window.setTimeout(cb, 16); };
            raf(focusClose);
        }

        /* "Period actual reconciles to the FY 2026 budget vs actual chart" - the year is a
           value between two message keys, never inside one. */
        function metaText() {
            return label('VAS_251_ReconcilesTo', 'Period actual reconciles to the') + ' ' +
                (_fiscalYear || '') + ' ' +
                label('VAS_251_ChartName', 'budget vs actual chart');
        }

        function periodNameOf(id) {
            for (var i = 0; i < _periods.length; i++) {
                if (Number(_periods[i].C_Period_ID) === id) { return _periods[i].Name || ''; }
            }
            return '';
        }

        /* THE BAR IS NOT RE-LIT ON THE WAY OUT. Focus is not handed back to the column that
           opened the panel: the host shell's focus treatment is a filled, ringed box the full
           height of the plot, which on a closed panel reads as a period still selected rather
           than as a cursor position. The chart is a set of readings, not a form to tab through
           - so the panel takes focus with it and leaves the card as it found it. */
        function closeDialog() {
            if (!$dlg) { return; }

            $dlg.addClass('vas-251-hidden');
            $(document).off('keydown' + _ns + '_dlg');

            /* The chart is interactive again the moment the scrim is gone. */
            if ($root) { $root.removeClass('vas-251-modal-open'); }

            _dlgPeriodId = 0;
            _dlgLoading = false;

            if (_dlgObserver) {
                try { _dlgObserver.disconnect(); } catch (e) { /* ignore */ }
                _dlgObserver = null;
            }

            /* Whatever the panel left focused goes with it - the close button and the pager
               are inside a subtree that is now hidden, and a focused element inside a hidden
               subtree is a ring nobody can see driving a Tab order nobody can follow. */
            blurDialogFocus();

            _dlgTrigger = null;
        }

        /* Drops focus if it is still inside the panel or on the bar behind it, leaving it at
           the document rather than on a control the reader has finished with. */
        function blurDialogFocus() {
            var active = document.activeElement;
            if (!active || typeof active.blur !== 'function') { return; }

            var inDialog = $dlg && $dlg[0] && $dlg[0].contains(active);
            var inCard = $root && $root[0] && $root[0].contains(active);

            if (inDialog || inCard) {
                try { active.blur(); } catch (e) { /* ignore */ }
            }
        }

        function onDialogKeyDown(e) {
            if (e.key === 'Escape' || e.keyCode === 27) { closeDialog(); }
        }

        function showDialogBusy(on) {
            $dlg.find('.vas-251-dlg-busy').toggleClass('vas-251-hidden', !on);
        }

        function fetchDetail(periodId) {
            if (_dlgLoading) { return; }
            _dlgLoading = true;
            showDialogBusy(true);

            $.ajax({
                url: VIS.Application.contextUrl + 'VAS_251_BudgetvsActualPeriodWidget/GetPeriodDetail',
                type: 'GET',
                dataType: 'json',
                cache: false,
                async: true,
                data: { periodId: periodId },
                success: function (raw) {
                    _dlgLoading = false;
                    if (_disposed) { return; }
                    showDialogBusy(false);

                    /* The reader may have closed the modal, or opened another period, while
                       this was in flight - in either case these rows are not what is on
                       screen and must not be painted into it. */
                    if (_dlgPeriodId !== periodId) { return; }

                    var data = parseResponse(raw);
                    if (!data || data.error || !data.Loaded) {
                        renderDetailMessage(label('VAS_192_CouldntLoad', "Couldn't load"));
                        return;
                    }

                    paintDetail(data);
                },
                error: function () {
                    _dlgLoading = false;
                    if (_disposed) { return; }
                    showDialogBusy(false);
                    renderDetailMessage(label('VAS_192_CouldntLoad', "Couldn't load"));
                }
            });
        }

        /* A failure or an empty period is said in the ROWS region, not over the whole panel:
           the metrics above it are the period's own figures and are still true. */
        function renderDetailMessage(text) {
            $dlg.find('.vas-251-dg-h').empty().addClass('vas-251-hidden');
            $dlgFoot.empty();
            $dlgRows.html('<div class="vas-251-dlg-empty">' + escapeHtml(text) + '</div>');
        }

        /* The modal: four metrics, the accounts behind them, and the total those accounts add
           up to. Every figure is the SERVER's for this period - none of it is passed in from
           the bar - so the total cannot drift from the chart.

           THE PANEL DOES NOT SCROLL. The metrics and the column header are chrome; only the
           rows region changes, and it is PAGED - which is what keeps the column header over
           the figures it names instead of scrolling away from them. */
        function paintDetail(data) {
            var budget = Number(data.Budget) || 0;
            var actual = Number(data.Actual) || 0;

            _dlgRows = data.Rows || [];
            _dlgTotalRows = Number(data.TotalRows) || _dlgRows.length;
            _dlgPage = 1;
            _dlgNeedsSync = true;

            /* The title and meta are re-stated from the server's own period, in case the bar
               that opened the modal named it differently. */
            $dlg.find('.vas-251-dlg-title').text(
                (data.PeriodName || '') + ' · ' +
                label('VAS_251_PostedAccounts', 'posted ledger accounts'));

            var variance = Number(data.Variance) || 0;
            var utilized = data.HasUtilization ? percentText(Number(data.UtilizedPct) || 0, 1) : NIL;

            /* The metrics are COMPACT - four figures across one narrow band, where the exact
               digits would wrap - with the full figure on each one's tooltip. */
            $dlg.find('.vas-251-metrics').html(
                metricHtml(label('VAS_251_ApprovedBudget', 'Approved budget'),
                    compactAmount(budget), fullAmount(budget)) +
                metricHtml(label('VAS_252_Actual', 'Actual'),
                    compactAmount(actual), fullAmount(actual)) +
                metricHtml(label('VAS_254_Variance', 'Variance'),
                    signedCompactAmount(variance), signedFullAmount(variance)) +
                metricHtml(label('VAS_252_Utilized', 'Utilized'), utilized, utilized)
            );

            /* THE TOTAL IS THE PERIOD'S OWN ACTUAL, not a sum of the rows on this page - and
               not a sum of every row either, since the list is capped and the total is not.
               It sits in its own band just above the footer, so it stays put while the pages
               turn underneath it. */
            $dlg.find('.vas-251-dg-total-l').text(label('VAS_251_TotalPosted', 'Total posted'));
            $dlg.find('.vas-251-dg-total-v')
                .text(compactAmount(actual))
                .attr('title', fullAmount(actual));

            if (_dlgRows.length === 0) {
                $dlg.find('.vas-251-dg-total').removeClass('vas-251-hidden');
                renderDetailMessage(label('VAS_251_NoDetail', 'No accounts were posted in this period.'));
                return;
            }

            $dlg.find('.vas-251-dg-h').removeClass('vas-251-hidden').html(
                '<span role="columnheader">' + escapeHtml(label('VAS_234_Account', 'Account')) + '</span>' +
                '<span role="columnheader">' +
                    escapeHtml(label('VAS_256_OrganizationUnit', 'Organization Unit')) + '</span>' +
                '<span class="vas-251-num" role="columnheader">' +
                    escapeHtml(label('VAS_251_Postings', 'Postings')) + '</span>' +
                '<span class="vas-251-num" role="columnheader">' +
                    escapeHtml(label('VAS_235_Amount', 'Amount')) + '</span>'
            );

            $dlg.find('.vas-251-dg-total').removeClass('vas-251-hidden');

            paintDetailPage();
            observeDialogRows();
        }

        /* One page of accounts, and the footer that says where in the list it sits. */
        function paintDetailPage() {
            var pages = Math.max(1, Math.ceil(_dlgRows.length / _dlgPageSize));
            if (_dlgPage > pages) { _dlgPage = pages; }
            if (_dlgPage < 1) { _dlgPage = 1; }
            _dlgTotalPages = pages;

            var start = (_dlgPage - 1) * _dlgPageSize;
            var end = Math.min(start + _dlgPageSize, _dlgRows.length);

            var html = '';
            for (var i = start; i < end; i++) { html += detailRowHtml(_dlgRows[i]); }
            $dlgRows.html(html);

            paintDetailFooter(start + 1, end);

            /* Fit the page to the panel ONLY after a fresh read or a resize - never on a page
               turn, which would flip the page size under the reader mid-navigation. */
            if (_dlgNeedsSync) { scheduleDialogSync(); }
        }

        /* "Showing 1–8 of 34" left, compact prev / next right - the widget footer pager, in
           the panel. It is rendered even on a single page: it states where the list ends
           rather than leaving the reader to infer it from a missing control. */
        function paintDetailFooter(from, to) {
            var showing = label('VAS_020_Showing', 'Showing') + ' ' + from + '–' + to + ' ' +
                label('VAS_020_Of', 'of') + ' ' + _dlgRows.length;

            /* The server caps what it sends; when it did, the footer says so on hover rather
               than letting the reader assume they are seeing every account. */
            var tip = showing;
            if (_dlgTotalRows > _dlgRows.length) {
                tip = showing + ' (' + label('VAS_020_Of', 'of') + ' ' + _dlgTotalRows + ')';
            }

            var prevDis = _dlgPage <= 1 ? ' disabled' : '';
            var nextDis = _dlgPage >= _dlgTotalPages ? ' disabled' : '';

            $dlgFoot.html(
                '<span class="vas-251-foot-info" title="' + escapeHtml(tip) + '">' +
                    escapeHtml(showing) + '</span>' +
                '<div class="vas-251-pager">' +
                    '<button type="button" class="vas-251-pgbtn vas-251-pg-prev" aria-label="' +
                        escapeHtml(label('VAS_020_Prev', 'Previous')) + '"' + prevDis + '>' + ICONS.prev + '</button>' +
                    '<span class="vas-251-pager-label">' + _dlgPage + ' ' +
                        escapeHtml(label('VAS_020_Of', 'of')) + ' ' + _dlgTotalPages + '</span>' +
                    '<button type="button" class="vas-251-pgbtn vas-251-pg-next" aria-label="' +
                        escapeHtml(label('VAS_020_Next', 'Next')) + '"' + nextDis + '>' + ICONS.next + '</button>' +
                '</div>'
            );

            $dlgFoot.find('.vas-251-pg-prev').on('click', function () {
                if (_dlgPage > 1) { _dlgPage--; paintDetailPage(); }
            });
            $dlgFoot.find('.vas-251-pg-next').on('click', function () {
                if (_dlgPage < _dlgTotalPages) { _dlgPage++; paintDetailPage(); }
            });
        }

        /* ---- Fitting the page to the panel ----
           The panel is a fixed height, so the number of rows that fit is a property of the
           panel and not a constant to guess at: it is measured from the rows region and the
           tallest row actually rendered. */
        function scheduleDialogSync() {
            var raf = window.requestAnimationFrame || function (cb) { return window.setTimeout(cb, 16); };
            raf(function () { syncDialogCapacity(); });
        }

        function syncDialogCapacity() {
            if (_disposed || !$dlgRows || !$dlgRows[0]) { return; }
            if (!_dlgRows || _dlgRows.length === 0) { return; }

            var avail = $dlgRows[0].clientHeight;
            if (avail <= 0) {
                /* Layout has not settled yet - try again on the next frame. */
                if (_dlgNeedsSync) { scheduleDialogSync(); }
                return;
            }

            var rendered = $dlgRows[0].querySelectorAll('.vas-251-dg-r');
            var maxH = 0;
            for (var i = 0; i < rendered.length; i++) {
                if (rendered[i].offsetHeight > maxH) { maxH = rendered[i].offsetHeight; }
            }
            if (maxH > 0) { _dlgRowH = maxH; }
            var rowH = _dlgRowH > 0 ? _dlgRowH : DLG_ROW_HEIGHT_FALLBACK;

            _dlgNeedsSync = false;

            var capacity = Math.floor(avail / rowH);
            if (capacity < DLG_MIN_PAGE_SIZE) { capacity = DLG_MIN_PAGE_SIZE; }

            if (capacity !== _dlgPageSize) {
                _dlgPageSize = capacity;
                paintDetailPage();
            }
        }

        function observeDialogRows() {
            if (typeof ResizeObserver === 'undefined' || !$dlgRows || !$dlgRows[0]) { return; }
            if (_dlgObserver) { try { _dlgObserver.disconnect(); } catch (e) { /* ignore */ } }

            _dlgObserver = new ResizeObserver(function () {
                if (_disposed || _dlgLoading) { return; }
                _dlgNeedsSync = true;
                syncDialogCapacity();
            });
            _dlgObserver.observe($dlgRows[0]);
        }

        function metricHtml(labelText, valueText, titleText) {
            return '<div class="vas-251-metric">' +
                '<div class="vas-251-metric-l">' + escapeHtml(labelText) + '</div>' +
                '<div class="vas-251-metric-v" title="' + escapeHtml(titleText) + '">' +
                    escapeHtml(valueText) + '</div>' +
            '</div>';
        }

        function detailRowHtml(item) {
            var amount = Number(item.Amount) || 0;

            return '<div class="vas-251-dg-r vas-251-dg-row" role="row">' +
                detailCell('vas-251-dg-p', accountText(item)) +
                detailCell('vas-251-dg-c', item.DimensionName || '') +
                detailCell('vas-251-dg-n vas-251-num', String(Number(item.PostingCount) || 0)) +
                /* Compact in the cell, exact on its tooltip - the grid is scanned down a
                   column, and the digits that matter are one hover away. */
                detailCell('vas-251-dg-n vas-251-num', compactAmount(amount), fullAmount(amount)) +
            '</div>';
        }

        /* One cell. titleText defaults to the displayed text - it differs only where the cell
           is a rounded form of something exact. An empty value renders the missing-value dash
           rather than nothing at all, so a gap never reads as a rendering fault. */
        function detailCell(cls, text, titleText) {
            if (isBlank(text)) {
                return '<span class="' + cls + ' vas-251-nil" role="cell">' + NIL + '</span>';
            }

            var tip = (titleText === undefined || titleText === null) ? text : titleText;

            return '<span class="' + cls + '" role="cell" title="' + escapeHtml(tip) + '">' +
                escapeHtml(text) + '</span>';
        }

        /* "{Account Value} — {Account Name}". Either half may be missing on a badly seeded
           chart of accounts, so the em dash is only printed when there are two sides to it. */
        function accountText(item) {
            var value = item.AccountValue || '';
            var name = item.AccountName || '';

            if (value && name) { return value + ' — ' + name; }
            return value || name;
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
           "$950K", "₹4.86Cr" - never "USD 950K", and only falling back to the ISO code when
           the currency has no symbol configured (which is what the server's own
           COALESCE(CurSymbol, ISO_Code) already resolved). The ISO code still drives the
           SCALE, so no lakh/crore or million step is ever assumed here.

           The helper returns a magnitude by contract, so a negative keeps its sign here - the
           amounts on this card are signed and a card that printed "-900" as "900" would state
           the opposite of the figure behind it. */
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

        /* Full, non-compact amount for the tooltips and the modal: the exact figure. Grouping
           and the decimal separator come from the browser locale, the decimals from the schema
           currency's precision. */
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

        /* The variance, with its sign ALWAYS printed - a plus on a favourable gap as much as a
           minus on an unfavourable one. The sign is what carries the direction when the colour
           cannot: in print, for a colour-blind reader, or anywhere the tone is lost. */
        function signedFullAmount(value) {
            var v = Number(value) || 0;
            var text = fullAmount(v);
            return v < 0 ? text : '+' + text;
        }

        function signedCompactAmount(value) {
            var v = Number(value) || 0;
            var text = compactAmount(v);
            return v < 0 ? text : '+' + text;
        }

        /* The separator comes from the reader's own locale, never a hard-coded dot, which
           reads as a thousands mark in half of Europe. */
        function percentText(value, decimals) {
            var v = Number(value) || 0;
            var d = Number(decimals) || 0;

            try {
                return v.toLocaleString(window.navigator.language,
                    { minimumFractionDigits: d, maximumFractionDigits: d }) + '%';
            }
            catch (e) { return v.toFixed(d) + '%'; }
        }

        /* ------------------------------------------------------------ */
        /* Helpers                                                      */
        /* ------------------------------------------------------------ */

        /* True when a value is nothing the reader can be shown. A zero is NOT blank: it is a
           figure. */
        function isBlank(value) {
            return value === null || value === undefined || String(value) === '';
        }

        /* Every database-sourced string - an account name, an organisation name, a period name
           - goes through here before it reaches the DOM. */
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

        /* Release everything that outlives the card: the body-mounted picker and modal, the
           document and window listeners they register, and the observer - a ResizeObserver
           left running keeps the whole subtree alive. */
        this.releasePanel = function () {
            _disposed = true;
            closePicker();
            closeDialog();

            if (_rootObserver) {
                try { _rootObserver.disconnect(); } catch (e) { /* ignore */ }
                _rootObserver = null;
            }

            if ($picker) { $picker.off(); $picker.remove(); $picker = null; }
            if ($dlg) { $dlg.off(); $dlg.remove(); $dlg = null; }
            if ($busy) { $busy.remove(); $busy = null; }
            if ($yearBtn) { $yearBtn.off(_ns); }
            if ($plot) { $plot.off(_ns); }

            _periods = [];
            _years = [];
        };
    };

    /* ---------------------------------------------------------------- */
    /* Required prototype hooks (same surface as other VAS widgets)     */
    /* ---------------------------------------------------------------- */
    VAS.VAS_251_BudgetvsActualPeriodWidget.prototype.init = function (windowNo, frame) {
        this.frame = frame;
        this.widgetInfo = frame.widgetInfo;
        this.windowNo = windowNo;
        this.initalize();
        this.frame.getContentGrid().append(this.getRoot());

        /* Self-wire the dashboard-width CSS variable the header clamps read. */
        ensureDashInlineSizeVar(this.getRoot());

        this.intialLoad();
    };

    /* No prototype refreshWidget: the constructor already defines the instance method, which
       shadows anything on the prototype. A prototype version calling this.refreshWidget()
       would be unreachable at best and infinite recursion the day the instance one is
       removed. */

    VAS.VAS_251_BudgetvsActualPeriodWidget.prototype.widgetSizeChange = function (widget) {
        this.widgetInfo = widget;
    };

    VAS.VAS_251_BudgetvsActualPeriodWidget.prototype.dispose = function () {
        try { this.releasePanel(); } catch (e) { /* ignore */ }
        if (this.frame && typeof this.frame.dispose === 'function') {
            try { this.frame.dispose(); } catch (e) { /* ignore */ }
        }
        this.frame = null;
        this.windowNo = null;
    };

})(VAS, jQuery);
