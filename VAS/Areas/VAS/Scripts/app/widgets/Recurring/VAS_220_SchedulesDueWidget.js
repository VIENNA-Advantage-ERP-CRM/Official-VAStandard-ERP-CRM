/**
 * VAS_220_SchedulesDueWidget
 * 2x1 KPI tile + drill-down modal for the Recurring dashboard.
 *
 * Read-only. Answers "how many recurring setups are due to generate in the next
 * 30 days, and which ones are they?". The count, the "due today" subset and the
 * modal rows all come from one asynchronous call to
 * VASLogic.Models.VAS_220_SchedulesDueModel, so the card can never disagree with
 * the list behind it. No SQL and no DB call is made from the client.
 *
 * Layout (matches the widget.html build pack for this widget):
 *   line 1  [icon]  Schedules Due            (icon well + widget title)
 *                   Next 30 Days             (widget subtitle - the window)
 *   line 2  36                               (KPI value)
 *   line 3  12 due today            View list ›
 *
 * Clicking the card opens the modal: next run / setup / type / business partner /
 * frequency / currency / amount, paged from the footer. It closes only through its
 * own controls or Escape - never on a backdrop click.
 *
 * States:
 *   loading  busy overlay over the card, value keeps its last rendered text
 *   empty    renders 0 - nothing due is a real answer, not a takeover; the modal
 *            shows its own empty row
 *   error    value falls back to an em dash, the meta line carries the reason and
 *            the card stops being clickable
 *
 * Amounts are shown untouched, in the source document's own currency, with a
 * dedicated currency column beside them - nothing is converted, and figures in
 * different currencies are never implied to share a unit. Each row formats to its
 * own currency's standard precision.
 *
 * Summary Message Table
 *  # | Current Text                          | Message Key
 * ---+---------------------------------------+---------------------------------
 *  1 | Schedules Due                         | VAS_220_SchedulesDue
 *  2 | due today                             | VAS_220_DueToday
 *  3 | View list                             | VAS_220_ViewList
 *  4 | Next {0} Days                         | VAS_220_NextNDays
 *  5 | Recurrings scheduled to generate between {0} and {1} | VAS_220_ScheduledBetween
 *  6 | Next run                              | VAS_220_NextRun
 *  7 | Setup                                 | VAS_220_Setup
 *  8 | Type                                  | VAS_220_Type
 *  9 | Business partner                      | VAS_220_BusinessPartner
 * 10 | Amount                                | VAS_220_Amount
 * 11 | Frequency                             | VAS_220_Frequency
 * 11a| Currency                              | VAS_220_Currency
 * 12 | Invoice                               | VAS_220_TypeInvoice
 * 13 | Order                                 | VAS_220_TypeOrder
 * 14 | Payment                               | VAS_220_TypePayment
 * 15 | GL Journal                            | VAS_220_TypeGLJournal
 * 16 | GL Journal Batch                      | VAS_220_TypeGLJournalBatch
 * 17 | Project                               | VAS_220_TypeProject
 * 18 | Other                                 | VAS_220_Other
 * 19 | Daily                                 | VAS_220_FreqDaily
 * 20 | Weekly                                | VAS_220_FreqWeekly
 * 21 | Monthly                               | VAS_220_FreqMonthly
 * 22 | Quarterly                             | VAS_220_FreqQuarterly
 * 23 | Internal                              | VAS_220_Internal
 * 24 | Today                                 | VAS_220_Today
 * 25 | Showing                               | VAS_220_Showing
 * 26 | of                                    | VAS_220_Of
 * 27 | schedules due in the next {0} days     | VAS_220_DueInWindow
 * 28 | No records found                      | VAS_220_NoRecordsFound
 * 29 | Close                                 | VAS_220_Close
 * 30 | Previous                              | VAS_220_Previous
 * 31 | Next                                  | VAS_220_Next
 * 32 | Couldn't load                         | VAS_220_CouldntLoad
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

    /* Alarm-clock glyph (inline SVG, not an icon-font class - the host shell does
       not always load an icon font and a missing glyph leaves an empty box). */
    var ICON_SCHEDULE =
        '<svg viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2" ' +
        'stroke-linecap="round" stroke-linejoin="round" aria-hidden="true" focusable="false">' +
        '<circle cx="12" cy="13" r="8"></circle>' +
        '<path d="M12 9v4l2.5 1.5M5 3 2 6M19 3l3 3"></path>' +
        '</svg>';

    /* C_Recurring.RecurringType stored code -> message key + English fallback. The
       codes are produced by VASLogic.Models.VAS_220_SchedulesDueModel - keep both
       sides in lock-step. Labels are never resolved in SQL. */
    var TYPE_MAP = {
        'I': { key: 'VAS_220_TypeInvoice', text: 'Invoice' },
        'O': { key: 'VAS_220_TypeOrder', text: 'Order' },
        'P': { key: 'VAS_220_TypePayment', text: 'Payment' },
        'B': { key: 'VAS_220_TypeGLJournal', text: 'GL Journal' },
        'G': { key: 'VAS_220_TypeGLJournalBatch', text: 'GL Journal Batch' },
        'J': { key: 'VAS_220_TypeProject', text: 'Project' }
    };

    /* C_Recurring.FrequencyType stored code -> message key + English fallback. */
    var FREQUENCY_MAP = {
        'D': { key: 'VAS_220_FreqDaily', text: 'Daily' },
        'W': { key: 'VAS_220_FreqWeekly', text: 'Weekly' },
        'M': { key: 'VAS_220_FreqMonthly', text: 'Monthly' },
        'Q': { key: 'VAS_220_FreqQuarterly', text: 'Quarterly' }
    };

    /* Setups with no partner of their own - the amount and the counterparty are
       internal to the ledger. */
    var GL_TYPES = { 'B': true, 'G': true };

    var PAGE_SIZE = 10;

    /* Mirrors VAS_220_SchedulesDueModel.DEFAULT_WINDOW_DAYS. Used only to label the
       card before the first response lands; the rendered value is then taken from
       the window the server actually resolved. */
    var DEFAULT_WINDOW_DAYS = 30;

    VAS.VAS_220_SchedulesDueWidget = function () {

        this.frame;
        this.windowNo;

        var $root = $('<div class="vas-220-root">');
        var $card;
        var $valueEl;
        var $subtitleEl;
        var $todayEl;
        var $linkEl;
        var $metaEl;
        var $busy;

        var resizeObserver = null;
        var activeRequest = null;

        var cachedData = null;
        var currentPage = 0;
        var $modal = null;

        function label(key, fallback) {
            var translated = VIS.Msg.getMsg(key);
            return (translated && translated.charAt(0) !== '[') ? translated : fallback;
        }

        /* Placeholder substitution for the few messages that carry a number or a
           date range. Kept out of SQL and out of the server payload so translators
           can move the token anywhere in the sentence. */
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

        /* The endpoint returns a JSON string inside a JSON response, so the payload
           can arrive double-encoded depending on the host serializer. */
        function parseResponse(res) {
            var data = res;
            if (typeof data === 'string') { data = JSON.parse(data); }
            if (typeof data === 'string') { data = JSON.parse(data); }
            return data || {};
        }

        function showBusy(show) {
            if (!$busy || !$busy[0]) { return; }
            $busy.toggleClass('vas-220-hidden', !show);
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

        /* The window descriptor shown on the card subtitle and repeated in the modal
           title, so both name the same span in the same words. */
        function windowSubtitle(days) {
            return format(label("VAS_220_NextNDays", "Next {0} Days"), [formatCount(days)]);
        }

        function typeLabel(code) {
            var entry = TYPE_MAP[code];
            return entry ? label(entry.key, entry.text) : label("VAS_220_Other", "Other");
        }

        function frequencyLabel(code) {
            var entry = FREQUENCY_MAP[code];
            return entry ? label(entry.key, entry.text) : label("VAS_220_Other", "Other");
        }

        /* GL setups carry no business partner - they are labelled as internal and
           qualified by the journal description when one exists. */
        function partnerLabel(row) {
            if (row.BPartnerName) { return row.BPartnerName; }
            if (!GL_TYPES[row.RecurringType]) { return '—'; }

            var internal = label("VAS_220_Internal", "Internal");
            return row.JournalDescription ? (internal + ' · ' + row.JournalDescription) : internal;
        }

        this.Initalize = function () {
            createWidget();
            setupResizeObserver();
            loadSchedulesDue();
        };

        function setupResizeObserver() {
            if (typeof ResizeObserver === 'undefined') { return; }
            try {
                resizeObserver = new ResizeObserver(function (entries) {
                    for (var i = 0; i < entries.length; i++) {
                        var width = entries[i].contentRect.width;
                        if (width > 0 && $root[0]) {
                            $root[0].style.setProperty('--widget-inline-size', width + 'px');
                        }
                    }
                });
                resizeObserver.observe($root[0]);
            } catch (e) { }
        }

        function createWidget() {
            var title = label("VAS_220_SchedulesDue", "Schedules Due");

            /* The card names its window in a subtitle under the title. Seeded with
               the default so the card never renders a blank line, then corrected in
               renderMetric from the window the server actually resolved. */
            var subtitle = windowSubtitle(DEFAULT_WINDOW_DAYS);

            /* Header row (icon well + title), KPI value, meta row. A div carries the
               card so it inherits no native button chrome; role and tabindex keep the
               drill-down reachable by keyboard and announced as actionable. */
            $card = $(
                '<div class="vas-220-card vas-widget-bg" role="button" tabindex="0" aria-disabled="false" aria-label="' + escapeHtml(title) + '">' +
                '<div class="vas-220-top">' +
                '<span class="vas-220-icon">' + ICON_SCHEDULE + '</span>' +
                '<span class="vas-220-titles">' +
                '<span class="vas-220-label">' + escapeHtml(title) + '</span>' +
                '<span class="vas-220-subtitle">' + escapeHtml(subtitle) + '</span>' +
                '</span>' +
                '</div>' +
                '<div class="vas-220-value">—</div>' +
                '<div class="vas-220-meta">' +
                '<span class="vas-220-today"></span>' +
                '<span class="vas-220-link"></span>' +
                '</div>' +
                '<div class="vas-220-error"></div>' +
                '</div>'
            );

            $valueEl = $card.find('.vas-220-value');
            $subtitleEl = $card.find('.vas-220-subtitle');
            $todayEl = $card.find('.vas-220-today');
            $linkEl = $card.find('.vas-220-link');
            $metaEl = $card.find('.vas-220-error');

            $card.on('click', function (e) {
                e.preventDefault();
                if (isCardDisabled()) { return; }
                openModal();
            });

            /* role="button" does not bring the native keyboard behaviour with it, so
               Enter and Space are wired explicitly. */
            $card.on('keydown', function (e) {
                if (e.key !== 'Enter' && e.key !== ' ' && e.key !== 'Spacebar') { return; }
                e.preventDefault();
                if (isCardDisabled()) { return; }
                openModal();
            });

            $root.append($card);

            $busy = $('<div class="vas-220-busy vas-220-hidden"><div class="vis-busyindicatorinnerwrap"><i class="vis_widgetloader"></i></div></div>');
            $root.append($busy);
        }

        /* A div has no disabled property, so the state lives on aria-disabled and is
           honoured by both activation paths. */
        function isCardDisabled() {
            return $card.attr('aria-disabled') === 'true';
        }

        function setCardDisabled(disabled) {
            $card
                .attr('aria-disabled', disabled ? 'true' : 'false')
                .attr('tabindex', disabled ? '-1' : '0');
        }

        function loadSchedulesDue() {
            /* A refresh fired while an earlier request is still open would otherwise
               let the stale response win the race and overwrite the newer figures. */
            if (activeRequest) {
                activeRequest.abort();
                activeRequest = null;
            }

            showBusy(true);

            activeRequest = $.ajax({
                url: VIS.Application.contextUrl + 'VAS_220_SchedulesDueWidget/GetSchedulesDue',
                type: 'GET',
                cache: false,
                success: function (res) {
                    var data;
                    try {
                        data = parseResponse(res);
                    } catch (e) {
                        setError();
                        return;
                    }

                    if (data.error || !data.Loaded) { setError(); return; }
                    renderMetric(data);
                },
                error: function (xhr, status) {
                    /* An aborted request is this widget superseding itself, not a
                       failure - the newer request owns the card. */
                    if (status === 'abort') { return; }
                    setError();
                },
                complete: function () {
                    activeRequest = null;
                    showBusy(false);
                }
            });
        }

        /* Nothing due is a valid result and renders as 0. Only a failed load takes
           the card into its error state. */
        function renderMetric(data) {
            cachedData = data;

            var total = formatCount(data.SchedulesDue);
            $valueEl.text(total).attr('title', total);

            /* The server clamps the window, so the card names the span it really
               queried rather than the one that was asked for. */
            var subtitle = windowSubtitle(Number(data.WindowDays) || DEFAULT_WINDOW_DAYS);
            $subtitleEl.text(subtitle).attr('title', subtitle);

            var todayText = formatCount(data.DueToday) + ' ' + label("VAS_220_DueToday", "due today");
            $todayEl.text(todayText).attr('title', todayText);

            var linkText = label("VAS_220_ViewList", "View list");
            $linkEl.text(linkText + ' ›').attr('title', linkText);

            $metaEl.text('').removeAttr('title');
            $card.removeClass('vas-220-card-error');
            setCardDisabled(false);

            /* The modal may already be open when a refresh lands - repaint it in
               place rather than leaving the user looking at superseded rows. */
            if ($modal) { renderTable(); }
        }

        function setError() {
            cachedData = null;
            closeModal();

            $valueEl.text('—').removeAttr('title');
            $todayEl.text('').removeAttr('title');
            $linkEl.text('').removeAttr('title');

            var msg = label("VAS_220_CouldntLoad", "Couldn't load");
            $metaEl.text(msg).attr('title', msg);

            /* Nothing to drill into while the load is broken. */
            $card.addClass('vas-220-card-error');
            setCardDisabled(true);
        }

        /* =====================================================================
           DRILL-DOWN MODAL
           ===================================================================== */

        function openModal() {
            if (!cachedData) { return; }
            if ($modal) { closeModal(); }

            currentPage = 0;

            /* The window is named in the title; the subtitle spells out the two dates
               it actually resolved to, so the reader never has to work out what
               "next 30 days" means from today. */
            var days = Number(cachedData.WindowDays) || DEFAULT_WINDOW_DAYS;
            var title = label("VAS_220_SchedulesDue", "Schedules Due") + ' — ' + windowSubtitle(days);
            var subtitle = format(
                label("VAS_220_ScheduledBetween", "Recurrings scheduled to generate between {0} and {1}"),
                [formatDate(cachedData.DateFrom), formatDate(cachedData.DateTo)]
            );

            var closeText = label("VAS_220_Close", "Close");

            $modal = $(
                '<div class="vas-220-mask" role="dialog" aria-modal="true" aria-label="' + escapeHtml(title) + '">' +
                '<div class="vas-220-modal">' +
                '<div class="vas-220-modal-head">' +
                /* Same glyph and well as the card header, so the dialog reads as this
                   widget's own surface rather than a generic list. */
                '<span class="vas-220-modal-ico">' + ICON_SCHEDULE + '</span>' +
                '<div class="vas-220-htxt">' +
                '<h2 class="vas-220-mtitle">' + escapeHtml(title) + '</h2>' +
                '<div class="vas-220-msub">' + escapeHtml(subtitle) + '</div>' +
                '</div>' +
                '<button type="button" class="vas-220-xbtn" data-close="1" aria-label="' + escapeHtml(closeText) + '">' +
                '<svg viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2.2" stroke-linecap="round">' +
                '<path d="M18 6 6 18M6 6l12 12"/>' +
                '</svg>' +
                '</button>' +
                '</div>' +
                '<div class="vas-220-modal-body"></div>' +
                '<div class="vas-220-modal-foot">' +
                '<span class="vas-220-foot-note"></span>' +
                '<span class="vas-220-foot-actions">' +
                '<span class="vas-220-pager"></span>' +
                '<button type="button" class="vas-220-btn vas-220-btn-primary" data-close="1">' + escapeHtml(closeText) + '</button>' +
                '</span>' +
                '</div>' +
                '</div>' +
                '</div>'
            );

            $('body').append($modal);

            /* Only the explicit close controls dismiss the dialog. A click on the
               backdrop is deliberately inert - a stray click outside must not throw
               away the list the user is reading. */
            $modal.on('click', function (e) {
                if ($(e.target).closest('[data-close]').length > 0) {
                    closeModal();
                }
            });

            $(document).on('keydown.vas220modal', function (e) {
                if (e.key === 'Escape') { closeModal(); }
            });

            renderTable();
        }

        function closeModal() {
            if ($modal) {
                $modal.off();
                $modal.remove();
                $modal = null;
            }
            $(document).off('keydown.vas220modal');
        }

        function renderTable() {
            if (!$modal || !cachedData) { return; }

            var rows = cachedData.Rows || [];

            var totalRows = rows.length;
            var totalPages = Math.max(1, Math.ceil(totalRows / PAGE_SIZE));
            if (currentPage > totalPages - 1) { currentPage = totalPages - 1; }
            if (currentPage < 0) { currentPage = 0; }

            var start = currentPage * PAGE_SIZE;
            var slice = rows.slice(start, start + PAGE_SIZE);

            /* Amount sits in the trailing column, with the currency it is expressed in
               immediately before it. Amounts are not converted, so the header carries
               no currency of its own - each row states its unit in that column. */
            var headHtml =
                '<div class="vas-220-trow vas-220-due-grid vas-220-thead">' +
                headCell(label("VAS_220_NextRun", "Next run"), false) +
                headCell(label("VAS_220_Setup", "Setup"), false) +
                headCell(label("VAS_220_Type", "Type"), false) +
                headCell(label("VAS_220_BusinessPartner", "Business partner"), false) +
                headCell(label("VAS_220_Frequency", "Frequency"), false) +
                headCell(label("VAS_220_Currency", "Currency"), false) +
                headCell(label("VAS_220_Amount", "Amount"), true) +
                '</div>';

            var bodyHtml = '';
            for (var i = 0; i < slice.length; i++) {
                var row = slice[i];

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

                var todayChip = row.IsDueToday
                    ? '<span class="vas-220-chip">' + escapeHtml(label("VAS_220_Today", "Today")) + '</span>'
                    : '';

                bodyHtml +=
                    '<div class="vas-220-trow vas-220-due-grid vas-220-tbody-row' + (row.IsDueToday ? ' vas-220-row-today' : '') + '">' +
                    '<span class="vas-220-cell vas-220-c-num" title="' + escapeHtml(dateText) + '">' + escapeHtml(dateText) + todayChip + '</span>' +
                    '<span class="vas-220-cell vas-220-c-prim" title="' + escapeHtml(setupText) + '">' + escapeHtml(setupText) + '</span>' +
                    '<span class="vas-220-cell vas-220-c-std" title="' + escapeHtml(typeText) + '">' + escapeHtml(typeText) + '</span>' +
                    '<span class="vas-220-cell vas-220-c-std" title="' + escapeHtml(partnerText) + '">' + escapeHtml(partnerText) + '</span>' +
                    '<span class="vas-220-cell vas-220-c-std" title="' + escapeHtml(freqText) + '">' + escapeHtml(freqText) + '</span>' +
                    '<span class="vas-220-cell vas-220-c-cur" title="' + escapeHtml(currencyTitle) + '">' + escapeHtml(currencyText) + '</span>' +
                    '<span class="vas-220-cell vas-220-c-val" title="' + escapeHtml(amountTitle) + '">' + escapeHtml(amountText) + '</span>' +
                    '</div>';
            }

            if (slice.length === 0) {
                bodyHtml = '<div class="vas-220-empty">' +
                    escapeHtml(label("VAS_220_NoRecordsFound", "No records found")) + '</div>';
            }

            $modal.find('.vas-220-modal-body').html(
                '<div class="vas-220-tbl">' + headHtml + '<div class="vas-220-tbody">' + bodyHtml + '</div></div>'
            );

            /* Footer helper carries the dataset size, not the page size, so the user
               can see how much sits behind the pager. */
            var footNote = format(
                label("VAS_220_DueInWindow", "schedules due in the next {0} days"),
                [formatCount(cachedData.WindowDays)]
            );
            footNote = formatCount(totalRows) + ' ' + footNote;
            if (totalPages > 1) {
                footNote = label("VAS_220_Showing", "Showing") + ' ' + (start + 1) + '–' + (start + slice.length)
                    + ' ' + label("VAS_220_Of", "of") + ' ' + footNote;
            }
            $modal.find('.vas-220-foot-note').text(footNote).attr('title', footNote);

            var pagerHtml = '';
            if (totalPages > 1) {
                pagerHtml =
                    '<button type="button" class="vas-220-pbtn" data-page="prev"' + (currentPage === 0 ? ' disabled' : '') +
                    ' aria-label="' + escapeHtml(label("VAS_220_Previous", "Previous")) + '">' +
                    '<svg viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2.4" stroke-linecap="round"><polyline points="15 18 9 12 15 6"/></svg>' +
                    '</button>' +
                    '<span class="vas-220-ptxt">' + (currentPage + 1) + ' ' + escapeHtml(label("VAS_220_Of", "of")) + ' ' + totalPages + '</span>' +
                    '<button type="button" class="vas-220-pbtn" data-page="next"' + (currentPage >= totalPages - 1 ? ' disabled' : '') +
                    ' aria-label="' + escapeHtml(label("VAS_220_Next", "Next")) + '">' +
                    '<svg viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2.4" stroke-linecap="round"><polyline points="9 18 15 12 9 6"/></svg>' +
                    '</button>';
            }

            var $pager = $modal.find('.vas-220-pager');
            $pager.html(pagerHtml);
            $pager.find('[data-page]').on('click', function () {
                var dir = $(this).attr('data-page');
                if (dir === 'prev' && currentPage > 0) { currentPage--; }
                else if (dir === 'next') { currentPage++; }
                renderTable();
            });
        }

        function headCell(text, isRight) {
            return '<span class="vas-220-cell' + (isRight ? ' vas-220-c-right' : '') + '" title="' +
                escapeHtml(text) + '">' + escapeHtml(text) + '</span>';
        }

        /* Called by the platform Refresh button and whenever the host dashboard
           re-broadcasts a record change on the Recurring window. */
        this.refreshWidget = function () {
            loadSchedulesDue();
        };

        this.getRoot = function () { return $root; };

        this.disposeComponent = function () {
            if (activeRequest) {
                activeRequest.abort();
                activeRequest = null;
            }
            if (resizeObserver) {
                try { resizeObserver.disconnect(); } catch (e) { }
                resizeObserver = null;
            }
            closeModal();
            if ($card) { $card.off(); }
            $root.remove();
        };
    };

    VAS.VAS_220_SchedulesDueWidget.prototype.widgetFirevalueChanged = function (value) {
        if (this.listener) { this.listener.widgetFirevalueChanged(value); }
    };

    VAS.VAS_220_SchedulesDueWidget.prototype.addChangeListener = function (listener) {
        this.listener = listener;
    };

    VAS.VAS_220_SchedulesDueWidget.prototype.init = function (windowNo, frame) {
        this.frame = frame;
        this.AD_UserHomeWidgetID = frame.widgetInfo.AD_UserHomeWidgetID;
        this.windowNo = windowNo;
        this.Initalize();
        this.frame.getContentGrid().append(this.getRoot());
        ensureDashInlineSizeVar(this.getRoot());
    };

    VAS.VAS_220_SchedulesDueWidget.prototype.widgetSizeChange = function (height, width) { };

    VAS.VAS_220_SchedulesDueWidget.prototype.dispose = function () {
        this.disposeComponent();
        if (this.frame) { this.frame.dispose(); }
        this.frame = null;
    };

})(VAS, jQuery);
