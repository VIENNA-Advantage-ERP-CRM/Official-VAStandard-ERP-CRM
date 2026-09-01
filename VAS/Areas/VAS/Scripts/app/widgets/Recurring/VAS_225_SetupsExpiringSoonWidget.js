/**
 * VAS_225_SetupsExpiringSoonWidget
 * 3x2 list widget for the Recurring dashboard.
 *
 * Read-only. Answers "which recurring setups are close to finishing based on
 * remaining runs?". No SQL and no DB call is made from the client.
 *
 * Layout (matches the widget.html build pack for this widget):
 *   head   [icon]  Setups Expiring Soon
 *                  3 or fewer runs left
 *   body   REC-INV-0210                              [1 run left]
 *          Invoice · ends 30 Jun 2026
 *          ... one row per setup, most urgent first
 *
 * Rows are paged in the database, and the visible row count adapts to the cell: the
 * list box is measured at runtime and the true row height is learned from the first
 * painted row, so the pager appears exactly when the rows stop fitting and the
 * widget never scrolls inside its own cell.
 *
 * The chip carries the urgency: a setup on its last run is warn-toned, anything
 * further out is muted.
 *
 * Summary Message Table
 *  # | Current Text                          | Message Key
 * ---+---------------------------------------+---------------------------------
 *  1 | Setups Expiring Soon                  | VAS_225_SetupsExpiringSoon
 *  2 | Ending within 60 days                 | VAS_225_Subtitle
 *  3 | {0} · ends {1}                        | VAS_225_TypeAndEnd
 *  4 | 1 run left                            | VAS_225_OneRunLeft
 *  5 | {0} runs left                         | VAS_225_RunsLeft
 *  6 | Invoice                               | VAS_225_TypeInvoice
 *  7 | Order                                 | VAS_225_TypeOrder
 *  8 | Payment                               | VAS_225_TypePayment
 *  9 | GL Journal                            | VAS_225_TypeGLJournal
 * 10 | GL Journal Batch                      | VAS_225_TypeGLJournalBatch
 * 11 | Project                               | VAS_225_TypeProject
 * 12 | Other                                 | VAS_225_Other
 * 13 | No setups expiring soon               | VAS_225_NoRecordsFound
 * 14 | Loading  (accessible name of the busy indicator) | VAS_225_Loading
 * 15 | Couldn't load                         | VAS_225_CouldntLoad
 * 16 | of                                    | VAS_225_Of
 * 16a| Showing                               | VAS_225_Showing
 * 17 | Previous                              | VAS_225_Previous
 * 18 | Next                                  | VAS_225_Next
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

    /* Clock glyph (inline SVG, not an icon-font class - the host shell does not
       always load an icon font and a missing glyph leaves an empty box). */
    var ICON_EXPIRING =
        '<svg viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2" ' +
        'stroke-linecap="round" stroke-linejoin="round" aria-hidden="true" focusable="false">' +
        '<circle cx="12" cy="12" r="9"></circle>' +
        '<path d="M12 7v5l3 2"></path>' +
        '</svg>';

    /* C_Recurring.RecurringType stored code -> message key + English fallback. The
       codes are produced by VASLogic.Models.VAS_225_SetupsExpiringSoonModel - keep
       both sides in lock-step. Labels are never resolved in SQL. */
    var TYPE_MAP = {
        'I': { key: 'VAS_225_TypeInvoice', text: 'Invoice' },
        'O': { key: 'VAS_225_TypeOrder', text: 'Order' },
        'P': { key: 'VAS_225_TypePayment', text: 'Payment' },
        'B': { key: 'VAS_225_TypeGLJournal', text: 'GL Journal' },
        'G': { key: 'VAS_225_TypeGLJournalBatch', text: 'GL Journal Batch' },
        'J': { key: 'VAS_225_TypeProject', text: 'Project' }
    };

    /* First-paint estimate of one two-line row, expressed in the card's own em so it
       tracks the widget root clamp: a fixed px constant under-counts on a wide
       dashboard, where the same row renders taller, and the widget then lays out more
       rows than actually fit and hides the pager that should have appeared. */
    var ROW_HEIGHT_EM = 3.5;

    /* Never show fewer than this, even if the cell is measured smaller than expected
       mid-layout. */
    var MIN_ROWS = 2;

    VAS.VAS_225_SetupsExpiringSoonWidget = function () {

        this.frame;
        this.windowNo;

        var $root = $('<div class="vas-225-root">');
        var $card;
        var $subtitleEl;
        var $bodyEl;
        var $footNoteEl;
        var $pagerEl;

        var widgetObserver = null;
        var bodyObserver = null;
        var listRequest = null;
        var resizeTimer = null;
        var lastMeasuredHeight = 0;

        /* Only the current page arrives in a response, so a page turn and a resize are
           both fresh requests. */
        var rows = [];
        var currentPage = 0;

        /* Rows the measured cell can hold - what the widget ASKS the server for. */
        var desiredPageSize = 4;

        /* Rows the server actually served, after its own clamp. Pager maths uses this
           one; the resize decision uses desiredPageSize. Keeping them apart is what
           stops a clamped page size from refetching forever. */
        var pageSize = 4;

        var totalRows = 0;
        var totalPages = 1;

        /* True height of a rendered row, learned from the DOM on the first paint. An
           estimate can only ever be close; the rendered row is the authority on how
           many of them fit. */
        var measuredRowHeight = 0;

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

        /* The endpoint returns a JSON string inside a JSON response, so the payload
           can arrive double-encoded depending on the host serializer. */
        function parseResponse(res) {
            var data = res;
            if (typeof data === 'string') { data = JSON.parse(data); }
            if (typeof data === 'string') { data = JSON.parse(data); }
            return data || {};
        }

        function formatCount(value) {
            var n = Number(value);
            if (!isFinite(n)) { n = 0; }
            return n.toLocaleString(window.navigator.language);
        }

        /* The server transports dates as yyyy-MM-dd; display formatting is the
           client's job. Parsed field by field so the string is never read as UTC and
           shifted a day backwards for users west of Greenwich. */
        function formatDate(iso) {
            if (!iso) { return ''; }
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
            return entry ? label(entry.key, entry.text) : label("VAS_225_Other", "Other");
        }

        /* Singular and plural are separate messages rather than a suffixed "s" -
           plural rules differ by language and cannot be built by concatenation. */
        function runsLeftLabel(runsRemaining) {
            if (Number(runsRemaining) === 1) { return label("VAS_225_OneRunLeft", "1 run left"); }
            return format(label("VAS_225_RunsLeft", "{0} runs left"), [formatCount(runsRemaining)]);
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

        /* The observer must never react to a size change it caused itself, or
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

                    if (height === lastMeasuredHeight) { return; }
                    lastMeasuredHeight = height;

                    if (resizeTimer) { window.clearTimeout(resizeTimer); }
                    resizeTimer = window.setTimeout(applyAdaptivePageSize, 200);
                });
                bodyObserver.observe($bodyEl[0]);
            } catch (e) { }
        }

        /* Height of one row: the measured value once a row has been painted,
           otherwise an em-derived estimate against the card's resolved font size so
           the very first layout is already close. */
        function rowHeight() {
            if (measuredRowHeight > 0) { return measuredRowHeight; }

            var base = 16;
            try {
                var resolved = parseFloat(window.getComputedStyle($card[0]).fontSize);
                if (isFinite(resolved) && resolved > 0) { base = resolved; }
            } catch (e) { }

            return base * ROW_HEIGHT_EM;
        }

        function computePageSize() {
            var available = $bodyEl && $bodyEl[0] ? $bodyEl[0].clientHeight : 0;
            if (!available || available <= 0) { return 0; }

            var fits = Math.floor(available / rowHeight());
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
               page stays on screen after the page size changes. */
            var firstRowIndex = currentPage * desiredPageSize;
            desiredPageSize = next;
            currentPage = Math.floor(firstRowIndex / desiredPageSize);

            loadList();
        }

        /* Learns the real row height from the DOM and, if it disagrees with the
           estimate the page was laid out from, refetches once at the corrected count.
           This is what makes the pager appear when the rows genuinely do not fit. */
        function calibrateRowHeight() {
            var $first = $bodyEl.children('.vas-225-row').first();
            if (!$first.length) { return; }

            var height = Math.round($first[0].getBoundingClientRect().height);
            if (height <= 0 || height === measuredRowHeight) { return; }

            measuredRowHeight = height;
            applyAdaptivePageSize();
        }

        function createWidget() {
            var title = label("VAS_225_SetupsExpiringSoon", "Setups Expiring Soon");
            var subtitle = label("VAS_225_Subtitle", "Ending within 60 days");

            $card = $(
                '<div class="vas-225-card vas-widget-bg">' +
                '<div class="vas-225-head">' +
                '<span class="vas-225-icon">' + ICON_EXPIRING + '</span>' +
                '<span class="vas-225-titles">' +
                '<span class="vas-225-title">' + escapeHtml(title) + '</span>' +
                '<span class="vas-225-subtitle">' + escapeHtml(subtitle) + '</span>' +
                '</span>' +
                '</div>' +
                '<div class="vas-225-body"></div>' +
                '<div class="vas-225-foot">' +
                '<span class="vas-225-foot-note"></span>' +
                '<span class="vas-225-pager"></span>' +
                '</div>' +
                '</div>'
            );

            $subtitleEl = $card.find('.vas-225-subtitle');
            $bodyEl = $card.find('.vas-225-body');
            $footNoteEl = $card.find('.vas-225-foot-note');
            $pagerEl = $card.find('.vas-225-pager');

            $root.append($card);
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
                url: VIS.Application.contextUrl + 'VAS_225_SetupsExpiringSoonWidget/GetExpiringSoon',
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
                '<div class="vas-225-state vas-225-state-busy" role="status" aria-live="polite" ' +
                'aria-label="' + escapeHtml(label("VAS_225_Loading", "Loading")) + '">' +
                '<div class="vis-busyindicatorinnerwrap"><i class="vis_widgetloader"></i></div>' +
                '</div>'
            );
            $pagerEl.find('[data-page]').prop('disabled', true);
        }

        function renderError() {
            rows = [];

            var msg = label("VAS_225_CouldntLoad", "Couldn't load");
            $bodyEl.html('<div class="vas-225-state vas-225-state-error">' + escapeHtml(msg) + '</div>');
            $footNoteEl.text(msg).attr('title', msg);
            $pagerEl.empty();
        }

        function renderList(data) {
            rows = data.Rows || [];

            /* The server clamps the page index, so a stale page number is corrected
               rather than returning an unexplained empty list. */
            currentPage = Number(data.Page) || 0;
            pageSize = Number(data.PageSize) || pageSize;
            totalRows = Number(data.TotalRows) || 0;
            totalPages = Math.max(1, Math.ceil(totalRows / pageSize));

            renderRows();
            renderFooter();
        }

        /* Footer helper carries the whole filtered set's size, not the page size, so
           the user can see how much sits behind the pager. */
        function renderFooter() {
            var start = currentPage * pageSize;

            var note = '';
            if (totalRows > 0) {
                note = label("VAS_225_Showing", "Showing") + ' ' + (start + 1) + '–' + (start + rows.length)
                    + ' ' + label("VAS_225_Of", "of") + ' ' + formatCount(totalRows);
            }
            $footNoteEl.text(note).attr('title', note);

            renderPager();
        }

        function renderRows() {
            if (rows.length === 0) {
                /* Nothing near its end is a real answer, not a load failure. */
                $bodyEl.html('<div class="vas-225-state">' +
                    escapeHtml(label("VAS_225_NoRecordsFound", "No setups expiring soon")) + '</div>');
                return;
            }

            var html = '';
            for (var i = 0; i < rows.length; i++) {
                var row = rows[i];

                var nameText = row.RecurringName || '—';
                var typeText = typeLabel(row.RecurringType);

                /* The projected end is omitted rather than shown as a placeholder when
                   the setup has no next run to project from - a date that does not
                   exist should not occupy the line. */
                var endText = formatDate(row.ProjectedEndDate);
                var metaText = endText
                    ? format(label("VAS_225_TypeAndEnd", "{0} · ends {1}"), [typeText, endText])
                    : typeText;

                var runsText = runsLeftLabel(row.RunsRemaining);

                /* A setup on its last run is the one worth noticing; anything further
                   out stays muted so the warn tone keeps its meaning. */
                var tone = Number(row.RunsRemaining) === 1 ? 'warn' : 'muted';

                html +=
                    '<div class="vas-225-row">' +
                    '<span class="vas-225-left">' +
                    '<span class="vas-225-label" title="' + escapeHtml(nameText) + '">' + escapeHtml(nameText) + '</span>' +
                    '<span class="vas-225-meta" title="' + escapeHtml(metaText) + '">' + escapeHtml(metaText) + '</span>' +
                    '</span>' +
                    '<span class="vas-225-chipwrap">' +
                    '<span class="vas-225-chip vas-225-chip-' + tone + '" title="' + escapeHtml(runsText) + '">' +
                    escapeHtml(runsText) + '</span>' +
                    '</span>' +
                    '</div>';
            }

            $bodyEl.html(html);

            /* Now that a row exists, check the estimate against reality - the page may
               need to shrink, and the pager to appear. */
            calibrateRowHeight();
        }

        function renderPager() {
            var pagerHtml = '';
            if (totalPages > 1) {
                pagerHtml =
                    '<button type="button" class="vas-225-pbtn" data-page="prev"' + (currentPage === 0 ? ' disabled' : '') +
                    ' aria-label="' + escapeHtml(label("VAS_225_Previous", "Previous")) + '">' +
                    '<svg viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2.5" stroke-linecap="round" stroke-linejoin="round"><path d="M15 18l-6-6 6-6"/></svg>' +
                    '</button>' +
                    '<span class="vas-225-ptxt">' + (currentPage + 1) + ' ' + escapeHtml(label("VAS_225_Of", "of")) + ' ' + totalPages + '</span>' +
                    '<button type="button" class="vas-225-pbtn" data-page="next"' + (currentPage >= totalPages - 1 ? ' disabled' : '') +
                    ' aria-label="' + escapeHtml(label("VAS_225_Next", "Next")) + '">' +
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

        /* Called by the platform Refresh button and whenever the host dashboard
           re-broadcasts a record change on the Recurring window. */
        this.refreshWidget = function () {
            loadList();
        };

        this.getRoot = function () { return $root; };

        this.disposeComponent = function () {
            if (listRequest) { listRequest.abort(); listRequest = null; }
            if (resizeTimer) { window.clearTimeout(resizeTimer); resizeTimer = null; }

            if (widgetObserver) {
                try { widgetObserver.disconnect(); } catch (e) { }
                widgetObserver = null;
            }
            if (bodyObserver) {
                try { bodyObserver.disconnect(); } catch (e) { }
                bodyObserver = null;
            }

            if ($pagerEl) { $pagerEl.find('[data-page]').off(); }

            $root.remove();
        };
    };

    VAS.VAS_225_SetupsExpiringSoonWidget.prototype.widgetFirevalueChanged = function (value) {
        if (this.listener) { this.listener.widgetFirevalueChanged(value); }
    };

    VAS.VAS_225_SetupsExpiringSoonWidget.prototype.addChangeListener = function (listener) {
        this.listener = listener;
    };

    VAS.VAS_225_SetupsExpiringSoonWidget.prototype.init = function (windowNo, frame) {
        this.frame = frame;
        this.AD_UserHomeWidgetID = frame.widgetInfo.AD_UserHomeWidgetID;
        this.windowNo = windowNo;
        this.Initalize();
        this.frame.getContentGrid().append(this.getRoot());
        ensureDashInlineSizeVar(this.getRoot());
    };

    VAS.VAS_225_SetupsExpiringSoonWidget.prototype.widgetSizeChange = function (height, width) { };

    VAS.VAS_225_SetupsExpiringSoonWidget.prototype.dispose = function () {
        this.disposeComponent();
        if (this.frame) { this.frame.dispose(); }
        this.frame = null;
    };

})(VAS, jQuery);
