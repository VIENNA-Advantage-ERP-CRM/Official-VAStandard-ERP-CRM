/**
 * VAS_222_UpcomingRecurringSchedules
 * 6x2 grid widget for the Recurring dashboard.
 *
 * Answers "which recurring setups are next in the generation queue?" and lets one
 * of them be generated from its row. No SQL and no DB call is made from the client.
 *
 * Layout (matches the widget.html build pack for this widget):
 *   head   [icon]  Upcoming Recurring Schedules
 *                  Next runs · generate any schedule individually
 *   body   next run | setup | type | business partner | frequency | currency |
 *          amount | status | Generate
 *   foot   Showing 1–6 of 36                                  < 1 of 6 >
 *
 * Row count adapts to the cell: the body is measured at runtime and the page size
 * is recomputed from it, so the widget shows more rows on a tall dashboard and
 * fewer on a laptop without ever scrolling inside the cell. Paging happens in the
 * database, so a page turn is a fresh request rather than a slice of a downloaded
 * array.
 *
 * The Generate action CREATES a document (invoice / order / payment / journal /
 * project) and is not reversible, so it always asks for confirmation first. It is
 * disabled on rows the framework would refuse anyway - a setup that is not due
 * today, or one with no runs left.
 *
 * Amounts are shown untouched, in the source document's own currency, with a
 * dedicated currency column beside them - nothing is converted.
 *
 * Summary Message Table
 *  # | Current Text                          | Message Key
 * ---+---------------------------------------+---------------------------------
 *  1 | Upcoming Recurring Schedules          | VAS_222_UpcomingSchedules
 *  2 | Next runs · generate any schedule individually | VAS_222_Subtitle
 *  3 | Next run                              | VAS_222_NextRun
 *  4 | Setup                                 | VAS_222_Setup
 *  5 | Type                                  | VAS_222_Type
 *  6 | Business partner                      | VAS_222_BusinessPartner
 *  7 | Frequency                             | VAS_222_Frequency
 *  8 | Currency                              | VAS_222_Currency
 *  9 | Amount                                | VAS_222_Amount
 * 10 | Status                                | VAS_222_Status
 * 11 | Generate                              | VAS_222_Generate
 * 12 | Today                                 | VAS_222_StatusToday
 * 13 | Scheduled                             | VAS_222_StatusScheduled
 * 14 | On Hold                               | VAS_222_StatusOnHold
 * 15 | Invoice                               | VAS_222_TypeInvoice
 * 16 | Order                                 | VAS_222_TypeOrder
 * 17 | Payment                               | VAS_222_TypePayment
 * 18 | GL Journal                            | VAS_222_TypeGLJournal
 * 19 | GL Journal Batch                      | VAS_222_TypeGLJournalBatch
 * 20 | Project                               | VAS_222_TypeProject
 * 21 | Other                                 | VAS_222_Other
 * 22 | Daily                                 | VAS_222_FreqDaily
 * 23 | Weekly                                | VAS_222_FreqWeekly
 * 24 | Monthly                               | VAS_222_FreqMonthly
 * 25 | Quarterly                             | VAS_222_FreqQuarterly
 * 27 | Showing                               | VAS_222_Showing
 * 28 | of                                    | VAS_222_Of
 * 30 | No schedules queued                   | VAS_222_NoRecordsFound
 * 31 | Loading  (accessible name of the busy indicator) | VAS_222_Loading
 * 32 | Couldn't load                         | VAS_222_CouldntLoad
 * 33 | Previous                              | VAS_222_Previous
 * 34 | Next                                  | VAS_222_Next
 * 35 | Generate the next document for "{0}"? This creates a real document and cannot be undone. | VAS_222_ConfirmGenerate
 * 36 | Only schedules due today can be generated | VAS_222_NotDueYet
 * 37 | No runs left on this schedule         | VAS_222_NoRunsLeft
 * 38 | Could not generate the document       | VAS_222_GenerateFailed
 * 39 | Document generated                    | VAS_222_Generated
 */
; VAS = window.VAS || {};

; (function (VAS, $) {

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

    /* Calendar-with-clock glyph (inline SVG, not an icon-font class - the host shell
       does not always load an icon font and a missing glyph leaves an empty box). */
    var ICON_SCHEDULES =
        '<svg viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2" ' +
        'stroke-linecap="round" stroke-linejoin="round" aria-hidden="true" focusable="false">' +
        '<rect x="3" y="4" width="18" height="17" rx="2"></rect>' +
        '<path d="M16 2v4M8 2v4M3 10h18"></path>' +
        '<path d="M12 14l2 2"></path>' +
        '</svg>';

    /* Play-in-circle glyph for the row action. */
    var ICON_GENERATE =
        '<svg viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2" aria-hidden="true" focusable="false">' +
        '<circle cx="12" cy="12" r="9"></circle>' +
        '<path d="M10 8.5l5 3.5-5 3.5Z" fill="currentColor" stroke="none"></path>' +
        '</svg>';

    /* C_Recurring.RecurringType stored code -> message key + English fallback. The
       codes are produced by VASLogic.Models.VAS_222_UpcomingSchedulesModel - keep
       both sides in lock-step. Labels are never resolved in SQL. */
    var TYPE_MAP = {
        'I': { key: 'VAS_222_TypeInvoice', text: 'Invoice' },
        'O': { key: 'VAS_222_TypeOrder', text: 'Order' },
        'P': { key: 'VAS_222_TypePayment', text: 'Payment' },
        'B': { key: 'VAS_222_TypeGLJournal', text: 'GL Journal' },
        'G': { key: 'VAS_222_TypeGLJournalBatch', text: 'GL Journal Batch' },
        'J': { key: 'VAS_222_TypeProject', text: 'Project' }
    };

    /* C_Recurring.FrequencyType stored code -> message key + English fallback. */
    var FREQUENCY_MAP = {
        'D': { key: 'VAS_222_FreqDaily', text: 'Daily' },
        'W': { key: 'VAS_222_FreqWeekly', text: 'Weekly' },
        'M': { key: 'VAS_222_FreqMonthly', text: 'Monthly' },
        'Q': { key: 'VAS_222_FreqQuarterly', text: 'Quarterly' }
    };

    /* Row status token -> message key + chip tone. Tones follow the shared dashboard
       semantics; no new colour system is introduced for this widget. */
    var STATUS_MAP = {
        'TODAY': { key: 'VAS_222_StatusToday', text: 'Today', tone: 'warn' },
        'SCHEDULED': { key: 'VAS_222_StatusScheduled', text: 'Scheduled', tone: 'info' },
        'HOLD': { key: 'VAS_222_StatusOnHold', text: 'On Hold', tone: 'muted' }
    };

    /* Refusal codes the server can return from a Generate attempt, mapped to the
       message the user sees. */
    var REFUSAL_MAP = {
        'VAS_222_NotDueYet': 'Only schedules due today can be generated',
        'VAS_222_NoRunsLeft': 'No runs left on this schedule',
        'VAS_222_GenerateFailed': 'Could not generate the document',
        'RecordNotFound': 'Could not generate the document',
        'InvalidRequest': 'Could not generate the document'
    };

    /* Approximate rendered height of one body row, used to derive how many rows fit
       the cell. Kept as one constant so the measurement and the row markup can be
       kept in step from a single place. */
    var ROW_HEIGHT_PX = 38;

    /* Never show fewer than this, even if the cell is measured smaller than expected
       mid-layout. */
    var MIN_ROWS = 3;

    VAS.VAS_222_UpcomingRecurringSchedules = function () {

        this.frame;
        this.windowNo;

        var $root = $('<div class="vas-222-root">');
        var $card;
        var $bodyEl;
        var $footNoteEl;
        var $pagerEl;
        var $busy;

        var widgetObserver = null;
        var bodyObserver = null;
        var listRequest = null;
        var generateRequest = null;
        var resizeTimer = null;

        var currentPage = 0;

        /* Rows the measured cell can hold - what the widget ASKS the server for. */
        var desiredPageSize = 6;

        /* Rows the server actually served, after its own clamp. Pager maths uses this
           one; the resize decision uses desiredPageSize. Keeping them apart is what
           stops a clamped page size from refetching forever. */
        var pageSize = 6;

        /* Last body height the observer acted on, so an unchanged report is dropped
           instead of scheduling another fetch. */
        var lastMeasuredHeight = 0;

        var totalRows = 0;
        var totalPages = 1;
        var cachedRows = [];

        function label(key, fallback) {
            var translated = VIS.Msg.getMsg(key);
            return (translated && translated.charAt(0) !== '[') ? translated : fallback;
        }

        /* Placeholder substitution for the few messages that carry a value. Kept out
           of SQL and out of the server payload so translators can move the token
           anywhere in the sentence. */
        function format(text, values) {
            var out = String(text == null ? '' : text);
            for (var i = 0; i < values.length; i++) {
                out = out.split('{' + i + '}').join(values[i]);
            }
            return out;
        }

        function escapeHtml(value) {
            return String(value == null ? "" : value)
                .replace(/&/g, "&amp;")
                .replace(/</g, "&lt;")
                .replace(/>/g, "&gt;")
                .replace(/"/g, "&quot;")
                .replace(/'/g, "&#039;");
        }

        /* The endpoints return a JSON string inside a JSON response, so the payload
           can arrive double-encoded depending on the host serializer. */
        function parseResponse(res) {
            var data = res;
            if (typeof data === 'string') { data = JSON.parse(data); }
            if (typeof data === 'string') { data = JSON.parse(data); }
            return data || {};
        }

        function showBusy(show) {
            if (!$busy || !$busy[0]) { return; }
            $busy.toggleClass('vas-222-hidden', !show);
        }

        function formatCount(value) {
            var n = Number(value);
            if (!isFinite(n)) { n = 0; }
            return n.toLocaleString(window.navigator.language);
        }

        /* Group and decimal separators follow the user's locale rather than being
           hand-assembled, so the widget reads correctly in both point and comma
           cultures. */
        function formatAmount(value, precision) {
            var n = Number(value);
            if (!isFinite(n)) { n = 0; }
            var p = Number(precision);
            if (!isFinite(p) || p < 0 || p > 6) { p = 2; }
            return n.toLocaleString(window.navigator.language, {
                minimumFractionDigits: p,
                maximumFractionDigits: p
            });
        }

        /* The server transports dates as yyyy-MM-dd; display formatting is the
           client's job. Parsed field by field so the string is never read as UTC and
           shifted a day backwards for users west of Greenwich. */
        function formatDate(iso) {
            if (!iso) { return '—'; }
            var parts = String(iso).split('-');
            if (parts.length !== 3) { return iso; }

            var d = new Date(Number(parts[0]), Number(parts[1]) - 1, Number(parts[2]));
            if (isNaN(d.getTime())) { return iso; }

            try {
                return d.toLocaleDateString(window.navigator.language, {
                    day: '2-digit', month: 'short', year: 'numeric'
                });
            } catch (e) {
                return iso;
            }
        }

        function typeLabel(code) {
            var entry = TYPE_MAP[code];
            return entry ? label(entry.key, entry.text) : label("VAS_222_Other", "Other");
        }

        function frequencyLabel(code) {
            var entry = FREQUENCY_MAP[code];
            return entry ? label(entry.key, entry.text) : label("VAS_222_Other", "Other");
        }

        /* A setup with no C_BPartner reference shows the same em-dash placeholder the
           currency and date cells use, so every empty cell in the row reads alike. GL
           and project setups are the usual case - they post against the ledger, not a
           partner - but the rule is the same whatever the type. */
        function partnerLabel(row) {
            return row.BPartnerName ? row.BPartnerName : '—';
        }

        this.Initalize = function () {
            createWidget();
            setupWidgetObserver();
            setupBodyObserver();
            loadList();
        };

        function setupWidgetObserver() {
            if (typeof ResizeObserver === 'undefined') { return; }
            try {
                widgetObserver = new ResizeObserver(function (entries) {
                    for (var i = 0; i < entries.length; i++) {
                        var width = entries[i].contentRect.width;
                        if (width > 0 && $root[0]) {
                            $root[0].style.setProperty('--widget-inline-size', width + 'px');
                        }
                    }
                });
                widgetObserver.observe($root[0]);
            } catch (e) { }
        }

        /* The visible row count is derived from the cell, not hardcoded: the same
           widget shows fewer rows on a 1280px laptop than on a 2K dashboard, and the
           pager is the contract for "more rows exist".
           The observer must never react to a size change it caused itself, or
           rendering a page would schedule another fetch forever. Two guards: the
           callback ignores any report whose height matches the last one acted on, and
           the decision below compares against the size WE asked for rather than the
           size the server served. */
        function setupBodyObserver() {
            if (typeof ResizeObserver === 'undefined') { return; }
            try {
                bodyObserver = new ResizeObserver(function (entries) {
                    var height = 0;
                    if (entries && entries.length && entries[0].contentRect) {
                        height = Math.round(entries[0].contentRect.height);
                    }

                    /* Sub-pixel churn and repeat reports of an unchanged box are the
                       usual source of an endless refetch loop - drop them here. */
                    if (height === lastMeasuredHeight) { return; }
                    lastMeasuredHeight = height;

                    /* Resizes arrive in bursts; refetching on each one would hammer
                       the endpoint for no benefit. */
                    if (resizeTimer) { window.clearTimeout(resizeTimer); }
                    resizeTimer = window.setTimeout(applyAdaptivePageSize, 200);
                });
                bodyObserver.observe($bodyEl[0]);
            } catch (e) { }
        }

        /* Rows that fit the body box, or 0 while the widget has not been laid out yet
           (it is measured before the framework appends it to the dashboard). */
        function computePageSize() {
            var available = $bodyEl && $bodyEl[0] ? $bodyEl[0].clientHeight : 0;
            if (!available || available <= 0) { return 0; }

            var fits = Math.floor(available / ROW_HEIGHT_PX);
            return Math.max(MIN_ROWS, fits);
        }

        function applyAdaptivePageSize() {
            resizeTimer = null;

            var next = computePageSize();
            if (next <= 0) { return; }

            /* Compared against the REQUESTED size, not the served one. The server
               clamps the page size, so on a very tall cell the served value stays
               below the measured value permanently - comparing against it would leave
               the two never equal and refetch on every single tick. */
            if (next === desiredPageSize) { return; }

            /* Keep the user roughly where they were: the first row of the current
               page stays on screen after the page size changes. Measured against the
               OLD size, then re-expressed in the new one. */
            var firstRowIndex = currentPage * desiredPageSize;
            desiredPageSize = next;
            currentPage = Math.floor(firstRowIndex / desiredPageSize);

            loadList();
        }

        function createWidget() {
            var title = label("VAS_222_UpcomingSchedules", "Upcoming Recurring Schedules");
            var subtitle = label("VAS_222_Subtitle", "Next runs · generate any schedule individually");

            $card = $(
                '<div class="vas-222-card vas-widget-bg">' +
                '<div class="vas-222-head">' +
                '<span class="vas-222-icon">' + ICON_SCHEDULES + '</span>' +
                '<span class="vas-222-titles">' +
                '<span class="vas-222-title">' + escapeHtml(title) + '</span>' +
                '<span class="vas-222-subtitle">' + escapeHtml(subtitle) + '</span>' +
                '</span>' +
                '</div>' +
                '<div class="vas-222-thead vas-222-trow vas-222-grid">' +
                headCell(label("VAS_222_NextRun", "Next run"), false) +
                headCell(label("VAS_222_Setup", "Setup"), false) +
                headCell(label("VAS_222_Type", "Type"), false) +
                headCell(label("VAS_222_BusinessPartner", "Business partner"), false) +
                headCell(label("VAS_222_Frequency", "Frequency"), false) +
                headCell(label("VAS_222_Currency", "Currency"), false) +
                headCell(label("VAS_222_Amount", "Amount"), true) +
                headCell(label("VAS_222_Status", "Status"), false) +
                '<span class="vas-222-cell"></span>' +
                '</div>' +
                '<div class="vas-222-body"></div>' +
                '<div class="vas-222-foot">' +
                '<span class="vas-222-foot-note"></span>' +
                '<span class="vas-222-pager"></span>' +
                '</div>' +
                '</div>'
            );

            $bodyEl = $card.find('.vas-222-body');
            $footNoteEl = $card.find('.vas-222-foot-note');
            $pagerEl = $card.find('.vas-222-pager');

            $root.append($card);

            $busy = $('<div class="vas-222-busy vas-222-hidden"><div class="vis-busyindicatorinnerwrap"><i class="vis_widgetloader"></i></div></div>');
            $root.append($busy);
        }

        function headCell(text, isRight) {
            return '<span class="vas-222-cell' + (isRight ? ' vas-222-c-right' : '') + '" title="' +
                escapeHtml(text) + '">' + escapeHtml(text) + '</span>';
        }

        function loadList() {
            /* A refresh or page turn fired while an earlier request is still open
               would otherwise let the stale response win the race. */
            if (listRequest) {
                listRequest.abort();
                listRequest = null;
            }

            renderLoading();

            listRequest = $.ajax({
                url: VIS.Application.contextUrl + 'VAS_222_UpcomingRecurringSchedules/GetUpcomingSchedules',
                type: 'GET',
                data: { page: currentPage, pageSize: desiredPageSize },
                cache: false,
                success: function (res) {
                    var data;
                    try {
                        data = parseResponse(res);
                    } catch (e) {
                        renderError();
                        return;
                    }

                    if (data.error || !data.Loaded) { renderError(); return; }
                    renderList(data);
                },
                error: function (xhr, status) {
                    /* An aborted request is this widget superseding itself, not a
                       failure - the newer request owns the body. */
                    if (status === 'abort') { return; }
                    renderError();
                },
                complete: function () {
                    listRequest = null;
                }
            });
        }

        /* The platform busy indicator, not a text line - the same spinner the rest of
           the Recurring family uses. The label survives as the accessible name so the
           state is still announced.
           The footer is deliberately left standing: emptying it would change its
           height, which changes the body height, which wakes the resize observer and
           starts another fetch. The pager is disabled in place instead of removed. */
        function renderLoading() {
            $bodyEl.html(
                '<div class="vas-222-state vas-222-state-busy" role="status" aria-live="polite" ' +
                'aria-label="' + escapeHtml(label("VAS_222_Loading", "Loading")) + '">' +
                '<div class="vis-busyindicatorinnerwrap"><i class="vis_widgetloader"></i></div>' +
                '</div>'
            );
            $pagerEl.find('[data-page]').prop('disabled', true);
        }

        function renderError() {
            cachedRows = [];

            var msg = label("VAS_222_CouldntLoad", "Couldn't load");
            $bodyEl.html('<div class="vas-222-state vas-222-state-error">' + escapeHtml(msg) + '</div>');
            $footNoteEl.text(msg).attr('title', msg);
            $pagerEl.empty();
        }

        function renderList(data) {
            cachedRows = data.Rows || [];

            /* The server clamps the page index, so a stale page number is corrected
               rather than returning an unexplained empty list. */
            currentPage = Number(data.Page) || 0;
            pageSize = Number(data.PageSize) || pageSize;
            totalRows = Number(data.TotalRows) || 0;
            totalPages = Math.max(1, Math.ceil(totalRows / pageSize));

            renderRows();
            renderFooter();
        }

        function renderRows() {
            if (cachedRows.length === 0) {
                $bodyEl.html('<div class="vas-222-state">' +
                    escapeHtml(label("VAS_222_NoRecordsFound", "No schedules queued")) + '</div>');
                return;
            }

            var generateText = label("VAS_222_Generate", "Generate");
            var html = '';

            for (var i = 0; i < cachedRows.length; i++) {
                var row = cachedRows[i];

                var dateText = formatDate(row.DateNextRun);
                var setupText = row.RecurringName || '—';
                var typeText = typeLabel(row.RecurringType);
                var partnerText = partnerLabel(row);
                var freqText = frequencyLabel(row.FrequencyType);

                /* The figure is the source document's own, formatted to that
                   currency's standard precision - a zero-decimal currency must not be
                   printed with two. */
                var amountText = formatAmount(row.Amount, row.AmountPrecision);
                var currencyText = row.AmountCurrencyIso || '—';
                var currencyTitle = row.AmountCurrencySymbol
                    ? (currencyText + ' · ' + row.AmountCurrencySymbol)
                    : currencyText;
                var amountTitle = amountText + (row.AmountCurrencyIso ? ' ' + row.AmountCurrencyIso : '');

                var status = STATUS_MAP[row.StatusCode] || STATUS_MAP['SCHEDULED'];
                var statusText = label(status.key, status.text);

                /* Disabled rather than hidden, so the queue reads consistently and the
                   tooltip explains why this particular row cannot run yet. */
                var canGenerate = !!row.CanGenerate;
                var genTitle = canGenerate
                    ? generateText
                    : (row.StatusCode === 'HOLD'
                        ? label("VAS_222_NoRunsLeft", "No runs left on this schedule")
                        : label("VAS_222_NotDueYet", "Only schedules due today can be generated"));

                html +=
                    '<div class="vas-222-trow vas-222-grid vas-222-body-row" data-recurring="' + row.C_Recurring_ID + '">' +
                    '<span class="vas-222-cell vas-222-c-num" title="' + escapeHtml(dateText) + '">' + escapeHtml(dateText) + '</span>' +
                    '<span class="vas-222-cell vas-222-c-prim" title="' + escapeHtml(setupText) + '">' + escapeHtml(setupText) + '</span>' +
                    '<span class="vas-222-cell vas-222-c-std" title="' + escapeHtml(typeText) + '">' + escapeHtml(typeText) + '</span>' +
                    '<span class="vas-222-cell vas-222-c-std" title="' + escapeHtml(partnerText) + '">' + escapeHtml(partnerText) + '</span>' +
                    '<span class="vas-222-cell vas-222-c-std" title="' + escapeHtml(freqText) + '">' + escapeHtml(freqText) + '</span>' +
                    '<span class="vas-222-cell vas-222-c-cur" title="' + escapeHtml(currencyTitle) + '">' + escapeHtml(currencyText) + '</span>' +
                    '<span class="vas-222-cell vas-222-c-val" title="' + escapeHtml(amountTitle) + '">' + escapeHtml(amountText) + '</span>' +
                    '<span class="vas-222-cell vas-222-c-status">' +
                    '<span class="vas-222-chip vas-222-chip-' + status.tone + '" title="' + escapeHtml(statusText) + '">' +
                    escapeHtml(statusText) + '</span>' +
                    '</span>' +
                    '<span class="vas-222-cell vas-222-c-action">' +
                    '<button type="button" class="vas-222-btn-gen" data-generate="' + row.C_Recurring_ID + '"' +
                    (canGenerate ? '' : ' disabled') +
                    ' title="' + escapeHtml(genTitle) + '">' +
                    ICON_GENERATE + '<span>' + escapeHtml(generateText) + '</span>' +
                    '</button>' +
                    '</span>' +
                    '</div>';
            }

            $bodyEl.html(html);

            $bodyEl.find('[data-generate]').on('click', function () {
                var id = Number($(this).attr('data-generate'));
                confirmGenerate(id);
            });
        }

        function renderFooter() {
            var start = currentPage * pageSize;

            var note = '';
            if (totalRows > 0) {
                note = label("VAS_222_Showing", "Showing") + ' ' + (start + 1) + '–' + (start + cachedRows.length)
                    + ' ' + label("VAS_222_Of", "of") + ' ' + formatCount(totalRows);
            }
            $footNoteEl.text(note).attr('title', note);

            var pagerHtml = '';
            if (totalPages > 1) {
                pagerHtml =
                    '<button type="button" class="vas-222-pbtn" data-page="prev"' + (currentPage === 0 ? ' disabled' : '') +
                    ' aria-label="' + escapeHtml(label("VAS_222_Previous", "Previous")) + '">' +
                    '<svg viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2.5" stroke-linecap="round" stroke-linejoin="round"><path d="M15 18l-6-6 6-6"/></svg>' +
                    '</button>' +
                    '<span class="vas-222-ptxt">' + (currentPage + 1) + ' ' + escapeHtml(label("VAS_222_Of", "of")) + ' ' + totalPages + '</span>' +
                    '<button type="button" class="vas-222-pbtn" data-page="next"' + (currentPage >= totalPages - 1 ? ' disabled' : '') +
                    ' aria-label="' + escapeHtml(label("VAS_222_Next", "Next")) + '">' +
                    '<svg viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2.5" stroke-linecap="round" stroke-linejoin="round"><path d="M9 18l6-6-6-6"/></svg>' +
                    '</button>';
            }

            $pagerEl.html(pagerHtml);
            $pagerEl.find('[data-page]').on('click', function () {
                var dir = $(this).attr('data-page');
                if (dir === 'prev' && currentPage > 0) { currentPage--; }
                else if (dir === 'next' && currentPage < totalPages - 1) { currentPage++; }
                else { return; }
                loadList();
            });
        }

        /* =====================================================================
           GENERATE
           ===================================================================== */

        /* Generating creates a real, posted-to-the-ledger document and cannot be
           undone from the dashboard, so it is never a single unguarded click. */
        function confirmGenerate(recurringId) {
            if (!recurringId || generateRequest) { return; }

            var row = findRow(recurringId);
            if (!row || !row.CanGenerate) { return; }

            var prompt = format(
                label("VAS_222_ConfirmGenerate",
                    'Generate the next document for "{0}"? This creates a real document and cannot be undone.'),
                [row.RecurringName || '']
            );

            askConfirm(prompt, function () {
                runGenerate(recurringId);
            });
        }

        /* Platform confirm dialog.
           VIS.ADialog.confirm(messageKey, unused, extraText, titleKey, callback)
           builds its text as Msg.getMsg(messageKey) + "\n" + extraText and hands it to
           ADialogUI.ask(text, titleKey, callback), which answers through the callback -
           OK calls it with true, Cancel with false. The function itself always returns
           false, so the return value carries no answer and must never be read as one.

           The already-resolved sentence goes in the extraText slot: the messageKey slot
           is run through Msg.getMsg, which would mangle a literal sentence. A null
           titleKey gives the framework's default "Confirm" heading rather than risking
           a raw key on an unseeded message.

           Only an explicit true proceeds, and onYes is latched, so no answer shape can
           generate a document twice or by accident. The native prompt is the last
           resort if ADialog is unavailable or throws. */
        function askConfirm(message, onYes) {
            var fired = false;
            var accept = function () {
                if (fired) { return; }
                fired = true;
                onYes();
            };

            var dialog = (window.VIS && VIS.ADialog) ? VIS.ADialog : null;
            if (dialog && typeof dialog.confirm === 'function') {
                try {
                    dialog.confirm(null, null, message, null, function (ok) {
                        if (ok === true) { accept(); }
                    });
                    return;
                } catch (e) { }
            }

            if (window.confirm(message)) { accept(); }
        }

        /* Turns a server refusal code into the sentence the user reads. An unknown
           code falls back to the generic failure rather than surfacing a raw token. */
        function refusalMessage(messageCode) {
            var fallback = REFUSAL_MAP[messageCode];
            if (fallback) { return label(messageCode, fallback); }

            return label("VAS_222_GenerateFailed", "Could not generate the document");
        }

        function findRow(recurringId) {
            for (var i = 0; i < cachedRows.length; i++) {
                if (cachedRows[i].C_Recurring_ID === recurringId) { return cachedRows[i]; }
            }
            return null;
        }

        function runGenerate(recurringId) {
            showBusy(true);
            $bodyEl.find('[data-generate]').prop('disabled', true);

            generateRequest = $.ajax({
                url: VIS.Application.contextUrl + 'VAS_222_UpcomingRecurringSchedules/GenerateRun',
                type: 'POST',
                data: { C_Recurring_ID: recurringId },
                cache: false,
                success: function (res) {
                    var data;
                    try {
                        data = parseResponse(res);
                    } catch (e) {
                        reportGenerate(false, label("VAS_222_GenerateFailed", "Could not generate the document"));
                        return;
                    }

                    if (data.error || !data.Success) {
                        reportGenerate(false, refusalMessage(data.MessageCode));
                        return;
                    }

                    reportGenerate(true, label("VAS_222_Generated", "Document generated"));
                },
                error: function (xhr, status) {
                    if (status === 'abort') { return; }
                    reportGenerate(false, label("VAS_222_GenerateFailed", "Could not generate the document"));
                },
                complete: function () {
                    generateRequest = null;
                    showBusy(false);
                }
            });
        }

        /* The run changed the setup's next date and remaining runs, so the queue is
           reloaded rather than patched in place - the row may now belong on a
           different page entirely. */
        function reportGenerate(success, message) {
            var shown = false;
            if (window.VIS && VIS.ADialog && typeof VIS.ADialog.info === 'function') {
                try {
                    VIS.ADialog.info("", null, message, '');
                    shown = true;
                } catch (e) { }
            }

            /* The outcome must reach the user either way - a silent success is
               indistinguishable from a silent failure on an action this consequential. */
            if (!shown) { window.alert(message); }

            loadList();
        }

        this.refreshWidget = function () {
            loadList();
        };

        this.getRoot = function () { return $root; };

        this.disposeComponent = function () {
            if (listRequest) { listRequest.abort(); listRequest = null; }
            if (generateRequest) { generateRequest.abort(); generateRequest = null; }
            if (resizeTimer) { window.clearTimeout(resizeTimer); resizeTimer = null; }

            if (widgetObserver) {
                try { widgetObserver.disconnect(); } catch (e) { }
                widgetObserver = null;
            }
            if (bodyObserver) {
                try { bodyObserver.disconnect(); } catch (e) { }
                bodyObserver = null;
            }

            if ($bodyEl) { $bodyEl.find('[data-generate]').off(); }
            if ($pagerEl) { $pagerEl.find('[data-page]').off(); }

            $root.remove();
        };
    };

    VAS.VAS_222_UpcomingRecurringSchedules.prototype.widgetFirevalueChanged = function (value) {
        if (this.listener) { this.listener.widgetFirevalueChanged(value); }
    };

    VAS.VAS_222_UpcomingRecurringSchedules.prototype.addChangeListener = function (listener) {
        this.listener = listener;
    };

    VAS.VAS_222_UpcomingRecurringSchedules.prototype.init = function (windowNo, frame) {
        this.frame = frame;
        this.AD_UserHomeWidgetID = frame.widgetInfo.AD_UserHomeWidgetID;
        this.windowNo = windowNo;
        this.Initalize();
        this.frame.getContentGrid().append(this.getRoot());
        ensureDashInlineSizeVar(this.getRoot());
    };

    VAS.VAS_222_UpcomingRecurringSchedules.prototype.widgetSizeChange = function (height, width) { };

    VAS.VAS_222_UpcomingRecurringSchedules.prototype.dispose = function () {
        this.disposeComponent();
        if (this.frame) { this.frame.dispose(); }
        this.frame = null;
    };

})(VAS, jQuery);
