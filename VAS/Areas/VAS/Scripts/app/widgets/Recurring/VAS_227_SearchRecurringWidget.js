/************************************************************
 * Module Name    : VAS
 * Purpose        : Search Recurring Widget
 *                  Full-width 1x9 dashboard search bar for the Recurring
 *                  module. Searches recurring setups (C_Recurring) by setup
 *                  name, description, source document number, project value
 *                  or business partner, and shows the most relevant matches
 *                  in a dropdown. Clicking a result zooms to the setup.
 *
 *                  Design: dashboard-widgets.md §"Full-Width Dashboard Search
 *                  Widget" - transparent outer wrapper with vertical padding
 *                  only, an 80%-wide centred glass block (2px white border,
 *                  14px radius, soft shadow), the search icon at 1.125em and
 *                  the input text at 0.875em, all scaling from
 *                  `font-size: clamp(16px, 1.4cqi, 24px)` on the widget root.
 *                  No title or subtitle - the design does not call for them.
 *                  The chrome comes from the shared vas-dssrch-* stylesheet
 *                  (VAS_DocumentSearchWidgets.css), which the VAS_067 / 068 /
 *                  069 / 070 search bars already use; only the pieces unique
 *                  to recurring setups live in VAS_227_SearchRecurringWidget.css.
 *
 *                  No SQL and no DB call is made from the client: the term
 *                  goes to VAS_227_SearchRecurringWidget/SearchRecurring and
 *                  is bound as a parameter in
 *                  VASLogic.Models.VAS_227_SearchRecurringModel.
 *
 *                  The results dropdown is appended to <body> and positioned
 *                  as a fixed popover, so the dashboard cell's overflow:hidden
 *                  cannot clip it. It re-anchors on scroll and resize, and the
 *                  shared VAS.OverlayWatch closes it if the dashboard itself
 *                  goes away.
 *
 *                  Amounts are shown untouched, in the source document's own
 *                  currency (symbol, ISO fallback and that currency's standard
 *                  precision all come from the server) - nothing is converted.
 *
 *                  Summary Message Table
 *                   # | Current Text                                          | Message Key
 *                  ---+-------------------------------------------------------+--------------------------------
 *                   1 | Search recurring setups - Name, Document no, Type, Project or Business Partner | VAS_227_Placeholder
 *                   2 | Type at least 2 characters to search                   | VAS_227_TypeToSearch
 *                   3 | No matching recurring setups                           | VAS_227_NoResults
 *                   4 | Search failed. Please try again.                       | VAS_227_CouldntLoad
 *                   5 | results                                                | VAS_227_Results
 *                   6 | Clear                                                  | VAS_227_Clear
 *                   7 | Next run                                               | VAS_227_NextRun
 *                   8 | No next run                                            | VAS_227_NoNextRun
 *                   9 | Runs left                                              | VAS_227_RunsLeft
 *                  10 | On Hold                                                | VAS_227_StatusOnHold
 *                  11 | Invoice                                                | VAS_227_TypeInvoice
 *                  12 | Order                                                  | VAS_227_TypeOrder
 *                  13 | Payment                                                | VAS_227_TypePayment
 *                  14 | GL Journal                                             | VAS_227_TypeGLJournal
 *                  15 | GL Journal Batch                                       | VAS_227_TypeGLJournalBatch
 *                  16 | Project                                                | VAS_227_TypeProject
 *                  17 | Other                                                  | VAS_227_Other
 *                  18 | Daily                                                  | VAS_227_FreqDaily
 *                  19 | Weekly                                                 | VAS_227_FreqWeekly
 *                  20 | Monthly                                                | VAS_227_FreqMonthly
 *                  21 | Quarterly                                              | VAS_227_FreqQuarterly
 *
 * Chronological development:
 *   VAI154         Created  Date 2026-09-01
 ***********************************************************/
; VAS = window.VAS || {};

; (function (VAS, $) {

    /* C_Recurring.RecurringType stored code -> message key, English fallback and
       the chip tone it is drawn with. The codes are produced by
       VASLogic.Models.VAS_227_SearchRecurringModel - keep both sides in lock-step.
       Labels are never resolved in SQL. */
    var TYPE_MAP = {
        'I': { key: 'VAS_227_TypeInvoice', text: 'Invoice', chip: 'invoice' },
        'O': { key: 'VAS_227_TypeOrder', text: 'Order', chip: 'order' },
        'P': { key: 'VAS_227_TypePayment', text: 'Payment', chip: 'payment' },
        'B': { key: 'VAS_227_TypeGLJournal', text: 'GL Journal', chip: 'gljournal' },
        'G': { key: 'VAS_227_TypeGLJournalBatch', text: 'GL Journal Batch', chip: 'gljournal' },
        'J': { key: 'VAS_227_TypeProject', text: 'Project', chip: 'project' }
    };

    /* C_Recurring.FrequencyType stored code -> message key + English fallback. */
    var FREQUENCY_MAP = {
        'D': { key: 'VAS_227_FreqDaily', text: 'Daily' },
        'W': { key: 'VAS_227_FreqWeekly', text: 'Weekly' },
        'M': { key: 'VAS_227_FreqMonthly', text: 'Monthly' },
        'Q': { key: 'VAS_227_FreqQuarterly', text: 'Quarterly' }
    };

    /* design.md §Widget Header / §Measurement Setup: keep --dash-inline-size on
       :root equal to the dashboard container's current pixel width so any clamp
       that reads it resolves against the dashboard's visible content area, not the
       viewport. A single document-level ResizeObserver serves every widget (the
       var is global); without a marked container - or without ResizeObserver - the
       CSS falls back to 100vw. */
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

    VAS.VAS_227_SearchRecurringWidget = function () {
        /* ---- Per-widget configuration ---- */
        var ENDPOINT = 'VAS_227_SearchRecurringWidget/SearchRecurring';
        var ZOOM_TABLE = 'C_Recurring';
        /* Zoom target when the widget is NOT hosted inside the Recurring window
           (windowNo < 0). Addressed by NAME - never by AD_Window_ID, which differs
           per environment; VAS.ZoomUtil resolves the id from the new name, falling
           back to the classic one, and the resolved id is cached for later clicks. */
        var ZOOM_WINDOW_NAME_NEW = 'VAS_Recurring';
        var ZOOM_WINDOW_NAME_OLD = 'Recurring';

        this.frame;
        this.windowNo;
        this.widgetInfo;

        var $self = this;
        var $bsyDiv;
        var $root = $('<div class="h-100 w-100 vas-widget-bg vas-dssrch-root vas-227-root">');
        var $bar, $input, $panel;
        var widgetID = 0;

        var windowId = 0;
        var debounceTimer = null;
        /* Every request carries the sequence it was issued with, so a slow answer to
           an older term can never overwrite the answer to the current one. */
        var requestSeq = 0;
        var MIN_LEN = 2;
        var DEBOUNCE_MS = 300;
        var PAGE_SIZE = 25;
        var currentTerm = '';
        var loadedCount = 0;
        var hasMore = false;
        var isLoadingMore = false;

        /* ------------------------------------------------------------ */
        /* Lifecycle                                                    */
        /* ------------------------------------------------------------ */
        this.initalize = function () {
            widgetID = (VIS.Utility && VIS.Utility.Util
                ? VIS.Utility.Util.getValueOfInt($self.widgetInfo.AD_UserHomeWidgetID)
                : 0);
            if (widgetID === 0) { widgetID = $self.windowNo; }

            createBusyIndicator();
            buildShell();
            $bsyDiv[0].style.visibility = 'hidden';
        };

        /* Nothing is loaded up front - the widget has no data until a term is typed.
           The framework hook only clears the busy indicator. */
        this.intialLoad = function () {
            $bsyDiv[0].style.visibility = 'hidden';
        };

        /* ------------------------------------------------------------ */
        /* DOM skeleton                                                 */
        /* ------------------------------------------------------------ */
        function buildShell() {
            var placeholder = getMsg('VAS_227_Placeholder',
                'Search recurring setups - Name, Document no, Type, Project or Business Partner');

            $bar = $('<div class="vas-dssrch-bar" id="vas_227_bar_' + widgetID + '">');
            $bar.append(
                '<svg class="vas-dssrch-icon" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2" stroke-linecap="round" stroke-linejoin="round" aria-hidden="true" focusable="false">' +
                    '<circle cx="11" cy="11" r="7"></circle><line x1="21" y1="21" x2="16.65" y2="16.65"></line>' +
                '</svg>' +
                '<span class="vas-dssrch-spin"></span>'
            );

            $input = $('<input type="text" class="vas-dssrch-input" autocomplete="off" spellcheck="false">')
                .attr('placeholder', placeholder)
                .attr('aria-label', placeholder);
            $bar.append($input);

            var clearLabel = getMsg('VAS_227_Clear', 'Clear');
            var $clear = $('<button type="button" class="vas-dssrch-clear" tabindex="-1" title="' +
                escapeHtml(clearLabel) + '" aria-label="' + escapeHtml(clearLabel) + '">&#215;</button>');
            $bar.append($clear);
            $root.append($bar);

            /* The dropdown lives on <body> so the dashboard cell's overflow:hidden
               cannot clip it; it is positioned as a fixed popover under the bar. */
            $panel = $('<div class="vas-dssrch-panel vas-227-panel" id="vas_227_panel_' + widgetID + '">');
            $('body').append($panel);
            $panel.on('scroll', onPanelScroll);

            wireEvents($clear);
        }

        function wireEvents($clear) {
            $input.on('input', function () {
                var term = $.trim($input.val());
                $bar.toggleClass('vas-dssrch-has-text', term.length > 0);
                scheduleSearch(term);
            });
            $input.on('focus', function () {
                var term = $.trim($input.val());
                if (term.length >= MIN_LEN && $panel.children().length > 0) { openPanel(); }
            });
            $input.on('keydown', function (e) {
                if (e.key === 'Escape' || e.keyCode === 27) { closePanel(); }
            });
            $clear.on('click', function () {
                if (debounceTimer) { window.clearTimeout(debounceTimer); debounceTimer = null; }
                /* Bump the sequence so an in-flight answer cannot repopulate the
                   dropdown after it has been cleared. */
                requestSeq++;
                $input.val('');
                $bar.removeClass('vas-dssrch-has-text');
                setBusy(false);
                $panel.empty();
                closePanel();
                $input.focus();
            });

            $self._onDocClick = function (e) {
                if (!$panel.hasClass('vas-dssrch-open')) { return; }
                if ($bar[0].contains(e.target) || $panel[0].contains(e.target)) { return; }
                closePanel();
            };
            /* The panel is position:fixed against the bar, so it re-anchors on every
               scroll. The dashboard scrolls its OWN container and scroll events do
               not bubble, hence the capture-phase listener registered with `true`. */
            $self._onReflow = function () {
                if ($panel.hasClass('vas-dssrch-open')) { positionPanel(); }
            };
            document.addEventListener('mousedown', $self._onDocClick, true);
            window.addEventListener('resize', $self._onReflow, true);
            window.addEventListener('scroll', $self._onReflow, true);
        }

        /* ------------------------------------------------------------ */
        /* Debounced search                                             */
        /* ------------------------------------------------------------ */
        function scheduleSearch(term) {
            if (debounceTimer) { window.clearTimeout(debounceTimer); debounceTimer = null; }
            if (term.length === 0) { setBusy(false); closePanel(); return; }
            if (term.length < MIN_LEN) { setBusy(false); renderHint(); return; }
            setBusy(true);
            debounceTimer = window.setTimeout(function () { runSearch(term); }, DEBOUNCE_MS);
        }

        function runSearch(term) {
            var mySeq = ++requestSeq;
            currentTerm = term;
            loadedCount = 0;
            hasMore = false;
            isLoadingMore = false;
            $.ajax({
                url: VIS.Application.contextUrl + ENDPOINT,
                type: 'GET',
                data: { query: term, maxRows: PAGE_SIZE, offset: 0 },
                dataType: 'json',
                cache: false,
                success: function (res) {
                    if (mySeq !== requestSeq) { return; }
                    setBusy(false);
                    var data = parsePayload(res);
                    if (!data || data.error || data.Loaded === false) { renderError(); return; }
                    var rows = data.Rows || [];
                    loadedCount = rows.length;
                    hasMore = !!data.HasMore;
                    renderResults(rows);
                },
                error: function () {
                    if (mySeq !== requestSeq) { return; }
                    setBusy(false);
                    renderError();
                }
            });
        }

        /* ---- Infinite scroll: fetch the next page and append ---- */
        function onPanelScroll() {
            if (!hasMore || isLoadingMore) { return; }
            if (!$panel.hasClass('vas-dssrch-open')) { return; }
            var el = $panel[0];
            if (el.scrollTop + el.clientHeight >= el.scrollHeight - 56) { loadMore(); }
        }

        function loadMore() {
            if (isLoadingMore || !hasMore) { return; }
            isLoadingMore = true;
            var mySeq = requestSeq;
            var term = currentTerm;
            var offset = loadedCount;
            showMoreSpinner(true);
            $.ajax({
                url: VIS.Application.contextUrl + ENDPOINT,
                type: 'GET',
                data: { query: term, maxRows: PAGE_SIZE, offset: offset },
                dataType: 'json',
                cache: false,
                success: function (res) {
                    isLoadingMore = false;
                    showMoreSpinner(false);
                    if (mySeq !== requestSeq) { return; }
                    var data = parsePayload(res);
                    if (!data || data.error || data.Loaded === false) { return; }
                    var rows = data.Rows || [];
                    appendResults(rows);
                    loadedCount += rows.length;
                    hasMore = !!data.HasMore;
                    updateCount();
                },
                error: function () {
                    isLoadingMore = false;
                    showMoreSpinner(false);
                }
            });
        }

        /* The controller returns a JSON string inside a JSON response, so the payload
           is unwrapped once before it is read. */
        function parsePayload(res) {
            try { return (typeof res === 'string') ? JSON.parse(res) : res; }
            catch (e) { return null; }
        }

        /* ------------------------------------------------------------ */
        /* Rendering                                                    */
        /* ------------------------------------------------------------ */
        function renderHint() {
            $panel.html(stateHtml(searchSvg(), getMsg('VAS_227_TypeToSearch', 'Type at least 2 characters to search'), false));
            openPanel();
        }

        function renderError() {
            $panel.html(stateHtml(alertSvg(), getMsg('VAS_227_CouldntLoad', 'Search failed. Please try again.'), true));
            openPanel();
        }

        function renderResults(rows) {
            if (!rows || rows.length === 0) {
                $panel.html(stateHtml(searchSvg(), getMsg('VAS_227_NoResults', 'No matching recurring setups'), false));
                openPanel();
                return;
            }

            var html = '<div class="vas-dssrch-count"></div><div class="vas-dssrch-list">';
            for (var i = 0; i < rows.length; i++) {
                html += buildRow(rows[i]);
            }
            html += '</div><div class="vas-dssrch-more"><span class="vas-dssrch-more-spin"></span></div>';
            $panel.html(html);

            bindRowClicks($panel.find('.vas-dssrch-list .vas-dssrch-row'));
            updateCount();
            $panel.scrollTop(0);
            openPanel();
        }

        function appendResults(rows) {
            if (!rows || rows.length === 0) { return; }
            var html = '';
            for (var i = 0; i < rows.length; i++) {
                html += buildRow(rows[i]);
            }
            var $rows = $(html).filter('.vas-dssrch-row');
            $panel.find('.vas-dssrch-list').append($rows);
            bindRowClicks($rows);
        }

        function bindRowClicks($rows) {
            $rows.on('click', function () {
                if ($(this).hasClass('vas-dssrch-nozoom')) { return; }
                zoomTo(VIS.Utility.Util.getValueOfInt($(this).attr('data-id')));
            });
        }

        function updateCount() {
            $panel.find('.vas-dssrch-count')
                .text(loadedCount + (hasMore ? '+' : '') + ' ' + getMsg('VAS_227_Results', 'results'));
        }

        function showMoreSpinner(on) {
            $panel.find('.vas-dssrch-more').toggleClass('vas-dssrch-more-active', !!on);
        }

        /* The row is led by the recurring TYPE chip - what a setup generates is the
           first thing that tells the hits apart - then the setup name with its
           frequency, the partner when the setup has one, and a muted
           detail line carrying the source document and the runs left. Amount and next
           run keep the right-hand column, which is what lets the figures align down
           the list. */
        function buildRow(row) {
            var recurringId = VIS.Utility.Util.getValueOfInt(row.C_Recurring_ID);
            var type = TYPE_MAP[row.RecurringType];
            var typeLabel = type ? getMsg(type.key, type.text) : getMsg('VAS_227_Other', 'Other');
            var chipClass = 'vas-dssrch-chip-' + (type ? type.chip : 'other');
            /* GL journal / journal-batch setups copy an internal document and have no
               business partner at all; the line is dropped rather than filled with a
               placeholder, so the row closes up instead of showing an empty gap. */
            var partner = $.trim(row.BPartnerName || '');

            return (
                '<div class="vas-dssrch-row' + (recurringId > 0 ? '' : ' vas-dssrch-nozoom') + '" data-id="' + recurringId + '">' +
                    '<span class="vas-dssrch-chip ' + chipClass + '" title="' + escapeHtml(typeLabel) + '">' + escapeHtml(typeLabel) + '</span>' +
                    '<div class="vas-dssrch-main">' +
                        '<div class="vas-dssrch-docline">' +
                            '<span class="vas-dssrch-docno">' + escapeHtml(row.RecurringName || '') + '</span>' +
                            frequencyPill(row) +
                            holdPill(row) +
                        '</div>' +
                        (partner ? '<div class="vas-dssrch-title">' + escapeHtml(partner) + '</div>' : '') +
                        '<div class="vas-dssrch-rowmeta">' + detailLine(row) + '</div>' +
                    '</div>' +
                    '<div class="vas-dssrch-meta">' +
                        '<div class="vas-dssrch-amount">' + escapeHtml(formatAmount(row)) + '</div>' +
                        '<div class="vas-dssrch-date">' + escapeHtml(nextRunLabel(row)) + '</div>' +
                    '</div>' +
                '</div>'
            );
        }

        /* "Monthly" on its own, or "Monthly · every 3" when the setup skips periods -
           the plain interval reads as noise when it is 1. */
        function frequencyPill(row) {
            var frequency = FREQUENCY_MAP[row.FrequencyType];
            if (!frequency) { return ''; }
            var label = getMsg(frequency.key, frequency.text);
            var every = VIS.Utility.Util.getValueOfInt(row.Frequency);
            if (every > 1) { label += ' · ' + every; }
            return '<span class="vas-dssrch-status vas-dssrch-status-info">' + escapeHtml(label) + '</span>';
        }

        /* A setup with nothing left to run cannot generate anything, which is worth
           seeing in the result itself rather than after opening it. */
        function holdPill(row) {
            if (VIS.Utility.Util.getValueOfInt(row.RunsRemaining) > 0) { return ''; }
            return '<span class="vas-dssrch-status vas-dssrch-status-muted">' +
                escapeHtml(getMsg('VAS_227_StatusOnHold', 'On Hold')) + '</span>';
        }

        /* Source document and remaining runs, only the parts that exist. */
        function detailLine(row) {
            var parts = [];
            var source = $.trim(row.SourceDocumentNo || '');
            if (source) { parts.push(escapeHtml(source)); }
            var runsRemaining = VIS.Utility.Util.getValueOfInt(row.RunsRemaining);
            if (runsRemaining > 0) {
                parts.push(escapeHtml(getMsg('VAS_227_RunsLeft', 'Runs left') + ': ' + runsRemaining));
            }
            return parts.join('<span class="vas-dssrch-detail-sep">&middot;</span>');
        }

        function nextRunLabel(row) {
            var formatted = formatDate(row.DateNextRun);
            if (!formatted) { return getMsg('VAS_227_NoNextRun', 'No next run'); }
            return getMsg('VAS_227_NextRun', 'Next run') + ': ' + formatted;
        }

        /* ------------------------------------------------------------ */
        /* Zoom                                                         */
        /* ------------------------------------------------------------ */
        function zoomTo(recurringId) {
            if (!recurringId) { return; }
            closePanel();
            try {
                if ($self.windowNo >= 0) {
                    /* Navigate the CURRENT window's grid to the clicked record, so no
                       second window is opened on top of the one being used. */
                    $self.widgetFirevalueChanged({
                        "TabWhereClause": ZOOM_TABLE + "." + ZOOM_TABLE + "_ID=" + recurringId,
                        "TabLayout": "Y",
                        "TabIndex": "0"
                    });
                }
                else if (window.VAS && VAS.ZoomUtil && typeof VAS.ZoomUtil.zoomToRecord === 'function') {
                    VAS.ZoomUtil.zoomToRecord(ZOOM_TABLE + "_ID", recurringId, windowId, ZOOM_WINDOW_NAME_NEW, ZOOM_WINDOW_NAME_OLD)
                        .done(function (id) {
                            /* Cache the resolved id so a second click skips the lookup. */
                            if (id > 0) { windowId = id; }
                        });
                }
            } catch (e) { /* zoom is best-effort */ }
        }

        /* The panel is mounted on <body>, so it outlives the dashboard being hidden -
           moving to another window would leave it floating over it. The shared
           watchdog closes it as soon as the bar itself stops being laid out. */
        var overlayWatch = VAS.OverlayWatch({
            anchor: function () { return $bar ? $bar[0] : null; },
            isOpen: function () { return !!$panel && $panel.hasClass('vas-dssrch-open'); },
            onHidden: function () { closePanel(); }
        });

        /* ------------------------------------------------------------ */
        /* Panel helpers                                                */
        /* ------------------------------------------------------------ */
        function openPanel() {
            positionPanel();
            $panel.addClass('vas-dssrch-open');
            $bar.addClass('vas-dssrch-bar-focus');
            overlayWatch.start();
        }

        function closePanel() {
            $panel.removeClass('vas-dssrch-open');
            $bar.removeClass('vas-dssrch-bar-focus');
        }

        function positionPanel() {
            if (!$bar || !$bar[0]) { return; }
            var rect = $bar[0].getBoundingClientRect();
            $panel.css({
                left: Math.round(rect.left) + 'px',
                top: Math.round(rect.bottom + 6) + 'px',
                width: Math.round(rect.width) + 'px'
            });
        }

        function setBusy(on) { $bar.toggleClass('vas-dssrch-busy', !!on); }

        /* ------------------------------------------------------------ */
        /* Formatters / helpers                                         */
        /* ------------------------------------------------------------ */
        /* The amount is NOT converted server-side: it is the source document's own
           amount in its OWN currency, so it is formatted with that currency's symbol
           (its ISO code when the currency has no symbol) and its own standard
           precision - never the login / schema currency. Setups with no source
           document carry no currency and show nothing rather than a bare 0. */
        function formatAmount(row) {
            var raw = row ? row.Amount : null;
            var value = (typeof raw === 'number') ? raw : parseFloat(raw);
            if (isNaN(value)) { return ''; }

            var symbol = (row.AmountCurrencySymbol || row.AmountCurrencyIso || '');
            if (!symbol && value === 0) { return ''; }

            var precision = VIS.Utility.Util.getValueOfInt(row.AmountPrecision);
            if (precision < 0 || precision > 6) { precision = 2; }

            /* Sign BEFORE the currency symbol, and no space between symbol and
               amount (e.g. -$28,000.00, not $ -28,000.00). */
            var sign = value < 0 ? '-' : '';
            var formatted = Math.abs(value).toLocaleString(window.navigator.language, {
                minimumFractionDigits: precision,
                maximumFractionDigits: precision
            });
            return sign + symbol + formatted;
        }

        /* The server always sends yyyy-MM-dd; the display format is the user's own. */
        function formatDate(iso) {
            if (!iso) { return ''; }
            var parts = String(iso).split('-');
            if (parts.length !== 3) { return iso; }
            var d = new Date(parseInt(parts[0], 10), parseInt(parts[1], 10) - 1, parseInt(parts[2], 10));
            if (isNaN(d.getTime())) { return iso; }
            return d.toLocaleDateString(window.navigator.language, { year: 'numeric', month: 'short', day: '2-digit' });
        }

        function getMsg(key, fallback) {
            try {
                if (VIS.Msg && typeof VIS.Msg.getMsg === 'function') {
                    var v = VIS.Msg.getMsg(key);
                    if (v && v !== key && v.charAt(0) !== '[') { return v; }
                }
            }
            catch (e) { /* ignore */ }
            return fallback;
        }

        function escapeHtml(str) {
            return String(str === null || str === undefined ? '' : str)
                .replace(/&/g, '&amp;').replace(/</g, '&lt;')
                .replace(/>/g, '&gt;').replace(/"/g, '&quot;');
        }

        function searchSvg() {
            return '<svg viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="1.8" stroke-linecap="round" stroke-linejoin="round" aria-hidden="true" focusable="false"><circle cx="11" cy="11" r="7"></circle><line x1="21" y1="21" x2="16.65" y2="16.65"></line></svg>';
        }

        function alertSvg() {
            return '<svg viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="1.8" stroke-linecap="round" stroke-linejoin="round" aria-hidden="true" focusable="false"><circle cx="12" cy="12" r="10"></circle><line x1="12" y1="8" x2="12" y2="12"></line><line x1="12" y1="16" x2="12.01" y2="16"></line></svg>';
        }

        function stateHtml(svg, text, isError) {
            return '<div class="vas-dssrch-state' + (isError ? ' vas-dssrch-state-error' : '') + '">' +
                svg + escapeHtml(text) + '</div>';
        }

        function createBusyIndicator() {
            $bsyDiv = $('<div class="vis-busyindicatorouterwrap vas-widget-busy-wrap"><div class="vis-busyindicatorinnerwrap"><i class="vis_widgetloader"></i></div></div>');
            $bsyDiv[0].style.visibility = 'hidden';
            $root.append($bsyDiv);
        }

        /* A refresh returns the widget to its neutral state - an empty box with no
           dropdown - rather than re-running a stale term. */
        this.refreshWidget = function () {
            if (debounceTimer) { window.clearTimeout(debounceTimer); debounceTimer = null; }
            requestSeq++;
            currentTerm = '';
            loadedCount = 0;
            hasMore = false;
            isLoadingMore = false;
            $input.val('');
            $bar.removeClass('vas-dssrch-has-text');
            setBusy(false);
            $panel.empty();
            closePanel();
        };

        this.getRoot = function () { return $root; };

        this._teardown = function () {
            if (debounceTimer) { window.clearTimeout(debounceTimer); debounceTimer = null; }
            requestSeq++;
            overlayWatch.stop();
            if ($self._onDocClick) { document.removeEventListener('mousedown', $self._onDocClick, true); }
            if ($self._onReflow) {
                window.removeEventListener('resize', $self._onReflow, true);
                window.removeEventListener('scroll', $self._onReflow, true);
            }
            /* The body-mounted layer must go with the widget, or it outlives the
               dashboard cell. */
            if ($panel) { $panel.off('scroll', onPanelScroll); $panel.remove(); $panel = null; }
        };
    };

    /* ---------------------------------------------------------------- */
    /* Required prototype hooks (same surface as other VAS widgets)     */
    /* ---------------------------------------------------------------- */
    VAS.VAS_227_SearchRecurringWidget.prototype.init = function (windowNo, frame) {
        this.frame = frame;
        this.widgetInfo = frame.widgetInfo;
        this.windowNo = windowNo;
        this.initalize();
        this.frame.getContentGrid().append(this.getRoot());

        /* Self-wire the dashboard-width CSS variable the shared widget clamps read. */
        ensureDashInlineSizeVar(this.getRoot());

        var self = this;
        window.setTimeout(function () { self.intialLoad(); }, 50);
    };

    VAS.VAS_227_SearchRecurringWidget.prototype.refreshWidget = function () {
        this.refreshWidget();
    };

    VAS.VAS_227_SearchRecurringWidget.prototype.addChangeListener = function (listener) {
        this.listener = listener;
    };

    /* Fired on a result click when the widget is hosted on the Recurring window
       itself: the host frame navigates its own grid to the clicked setup. */
    VAS.VAS_227_SearchRecurringWidget.prototype.widgetFirevalueChanged = function (value) {
        if (this.listener && typeof this.listener.widgetFirevalueChanged === 'function') {
            this.listener.widgetFirevalueChanged(value);
        }
    };

    VAS.VAS_227_SearchRecurringWidget.prototype.widgetSizeChange = function (widget) {
        this.widgetInfo = widget;
    };

    VAS.VAS_227_SearchRecurringWidget.prototype.dispose = function () {
        if (this._teardown) { this._teardown(); }
        if (this.frame && typeof this.frame.dispose === 'function') {
            try { this.frame.dispose(); } catch (e) { /* ignore */ }
        }
        this.frame = null;
        this.windowNo = null;
    };

})(VAS, jQuery);
