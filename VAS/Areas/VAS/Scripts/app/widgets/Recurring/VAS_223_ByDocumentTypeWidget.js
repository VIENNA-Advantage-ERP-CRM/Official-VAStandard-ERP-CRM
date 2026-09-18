/**
 * VAS_223_ByDocumentTypeWidget
 * 3x2 list widget for the Recurring dashboard.
 *
 * Read-only. Answers "how are active recurring setups distributed by document
 * type?". No SQL and no DB call is made from the client.
 *
 * Layout (matches the widget.html build pack for this widget):
 *   head   [icon]  By Document Type
 *                  Active setups
 *   body   Invoice                                              124
 *          50% of active setups
 *          ... one row per document type, largest first
 *
 * The bucket list is bounded by the RecurringType list reference (six codes plus an
 * unclassified bucket), so it arrives in one request and pages client-side. The row
 * count still adapts to the cell and a footer pager appears only when the buckets
 * outrun the space - the widget never scrolls inside its own cell.
 *
 * States:
 *   loading  platform busy indicator in the list region
 *   empty    "No active setups" - nothing configured is a real answer
 *   error    the list region carries the reason
 *
 * Summary Message Table
 *  # | Current Text                          | Message Key
 * ---+---------------------------------------+---------------------------------
 *  1 | By Document Type                      | VAS_223_ByDocumentType
 *  2 | Active setups                         | VAS_223_Subtitle
 *  3 | {0}% of active setups                 | VAS_223_ShareOfSetups
 *  4 | Invoice                               | VAS_223_TypeInvoice
 *  5 | Order                                 | VAS_223_TypeOrder
 *  6 | Payment                               | VAS_223_TypePayment
 *  7 | GL Journal                            | VAS_223_TypeGLJournal
 *  8 | GL Journal Batch                      | VAS_223_TypeGLJournalBatch
 *  9 | Project                               | VAS_223_TypeProject
 * 10 | Other                                 | VAS_223_Other
 * 11 | No active setups                      | VAS_223_NoRecordsFound
 * 12 | Loading  (accessible name of the busy indicator) | VAS_223_Loading
 * 13 | Couldn't load                         | VAS_223_CouldntLoad
 * 14 | of                                    | VAS_223_Of
 * 15 | Previous                              | VAS_223_Previous
 * 16 | Next                                  | VAS_223_Next
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

    /* Trend-chart glyph (inline SVG, not an icon-font class - the host shell does not
       always load an icon font and a missing glyph leaves an empty box). */
    var ICON_DISTRIBUTION =
        '<svg viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2" ' +
        'stroke-linecap="round" stroke-linejoin="round" aria-hidden="true" focusable="false">' +
        '<path d="M3 3v18h18"></path>' +
        '<path d="M7 14l3-3 3 2 4-5"></path>' +
        '</svg>';

    /* C_Recurring.RecurringType stored code -> message key + English fallback. The
       codes are produced by VASLogic.Models.VAS_223_ByDocumentTypeModel - keep both
       sides in lock-step. Labels are never resolved in SQL. */
    var TYPE_MAP = {
        'I': { key: 'VAS_223_TypeInvoice', text: 'Invoice' },
        'O': { key: 'VAS_223_TypeOrder', text: 'Order' },
        'P': { key: 'VAS_223_TypePayment', text: 'Payment' },
        'B': { key: 'VAS_223_TypeGLJournal', text: 'GL Journal' },
        'G': { key: 'VAS_223_TypeGLJournalBatch', text: 'GL Journal Batch' },
        'J': { key: 'VAS_223_TypeProject', text: 'Project' }
    };

    /* First-paint estimate of one two-line row, expressed in the card's own em so it
       tracks the widget root clamp: a fixed px constant under-counts on a wide
       dashboard, where the same row renders taller, and the widget then lays out more
       rows than actually fit and hides the pager that should have appeared.
       Padding (1.25em) + label (1.1375em) + meta (.975em) + pair margin + border. */
    var ROW_HEIGHT_EM = 3.5;

    /* Never show fewer than this, even if the cell is measured smaller than expected
       mid-layout. */
    var MIN_ROWS = 2;

    VAS.VAS_223_ByDocumentTypeWidget = function () {

        this.frame;
        this.windowNo;

        var $root = $('<div class="vas-223-root">');
        var $card;
        var $bodyEl;
        var $pagerEl;

        var widgetObserver = null;
        var bodyObserver = null;
        var listRequest = null;
        var resizeTimer = null;
        var lastMeasuredHeight = 0;

        var buckets = [];
        var currentPage = 0;
        var pageSize = 4;

        /* True height of a rendered row, learned from the DOM on the first paint. An
           estimate can only ever be close; the rendered row is the authority on how
           many of them fit. */
        var measuredRowHeight = 0;

        /* Re-entrancy guard: calibration can trigger one repaint, and that repaint
           must not calibrate again. */
        var calibrating = false;

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

        /* Two decimals at most, none when the share is clean - so a bucket holding
           exactly half reads "50%", not "50.00%". */
        function formatPercent(value) {
            var n = Number(value);
            if (!isFinite(n)) { n = 0; }
            return n.toLocaleString(window.navigator.language, {
                minimumFractionDigits: 0,
                maximumFractionDigits: 2
            });
        }

        function typeLabel(code) {
            var entry = TYPE_MAP[code];
            return entry ? label(entry.key, entry.text) : label("VAS_223_Other", "Other");
        }

        this.Initalize = function () {
            createWidget();
            setupWidgetObserver();
            setupBodyObserver();
            loadBuckets();
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

        /* The visible row count is derived from the cell, not hardcoded. Paging is
           client-side here - the whole bucket list is at most seven rows - so a
           resize only repaints; it never refetches. The height guard still applies so
           a repaint cannot wake the observer into a loop. */
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

        /* Learns the real row height from the DOM and, if it disagrees with the
           estimate the page was laid out from, repaints once at the corrected count.
           This is what makes the pager appear when the rows genuinely do not fit. */
        function calibrateRowHeight() {
            if (calibrating) { return; }

            var $first = $bodyEl.children('.vas-223-row').first();
            if (!$first.length) { return; }

            var height = Math.round($first[0].getBoundingClientRect().height);
            if (height <= 0 || height === measuredRowHeight) { return; }

            measuredRowHeight = height;

            var next = computePageSize();
            if (next <= 0 || next === pageSize) { return; }

            pageSize = next;

            calibrating = true;
            try { renderBuckets(); }
            finally { calibrating = false; }
        }

        function applyAdaptivePageSize() {
            resizeTimer = null;

            var next = computePageSize();
            if (next <= 0 || next === pageSize) { return; }

            /* Keep the user roughly where they were: the first row of the current
               page stays on screen after the page size changes. */
            var firstRowIndex = currentPage * pageSize;
            pageSize = next;
            currentPage = Math.floor(firstRowIndex / pageSize);

            renderBuckets();
        }

        function createWidget() {
            var title = label("VAS_223_ByDocumentType", "By Document Type");
            var subtitle = label("VAS_223_Subtitle", "Active setups");

            $card = $(
                '<div class="vas-223-card vas-widget-bg">' +
                '<div class="vas-223-head">' +
                '<span class="vas-223-icon">' + ICON_DISTRIBUTION + '</span>' +
                '<span class="vas-223-titles">' +
                '<span class="vas-223-title">' + escapeHtml(title) + '</span>' +
                '<span class="vas-223-subtitle">' + escapeHtml(subtitle) + '</span>' +
                '</span>' +
                '</div>' +
                '<div class="vas-223-body"></div>' +
                '<div class="vas-223-foot"><span class="vas-223-pager"></span></div>' +
                '</div>'
            );

            $bodyEl = $card.find('.vas-223-body');
            $pagerEl = $card.find('.vas-223-pager');

            $root.append($card);
        }

        function loadBuckets() {
            if (listRequest) {
                listRequest.abort();
                listRequest = null;
            }

            renderLoading();

            listRequest = $.ajax({
                url: VIS.Application.contextUrl + 'VAS_223_ByDocumentTypeWidget/GetByDocumentType',
                type: 'GET',
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

                    buckets = data.Buckets || [];
                    currentPage = 0;

                    /* First real measurement of the list box usually lands with the
                       first paint, so size the page before drawing rows. */
                    var measured = computePageSize();
                    if (measured > 0) { pageSize = measured; }

                    renderBuckets();
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
           state is still announced. */
        function renderLoading() {
            $bodyEl.html(
                '<div class="vas-223-state vas-223-state-busy" role="status" aria-live="polite" ' +
                'aria-label="' + escapeHtml(label("VAS_223_Loading", "Loading")) + '">' +
                '<div class="vis-busyindicatorinnerwrap"><i class="vis_widgetloader"></i></div>' +
                '</div>'
            );
            $pagerEl.find('[data-page]').prop('disabled', true);
        }

        function renderError() {
            buckets = [];

            var msg = label("VAS_223_CouldntLoad", "Couldn't load");
            $bodyEl.html('<div class="vas-223-state vas-223-state-error">' + escapeHtml(msg) + '</div>');
            $pagerEl.empty();
        }

        function renderBuckets() {
            if (buckets.length === 0) {
                /* No setup configured is a real answer, not a load failure. */
                $bodyEl.html('<div class="vas-223-state">' +
                    escapeHtml(label("VAS_223_NoRecordsFound", "No active setups")) + '</div>');
                $pagerEl.empty();
                return;
            }

            var totalPages = Math.max(1, Math.ceil(buckets.length / pageSize));
            if (currentPage > totalPages - 1) { currentPage = totalPages - 1; }
            if (currentPage < 0) { currentPage = 0; }

            var start = currentPage * pageSize;
            var slice = buckets.slice(start, start + pageSize);

            var html = '';
            for (var i = 0; i < slice.length; i++) {
                var bucket = slice[i];

                var labelText = typeLabel(bucket.RecurringType);
                var countText = formatCount(bucket.SetupCount);
                var metaText = format(
                    label("VAS_223_ShareOfSetups", "{0}% of active setups"),
                    [formatPercent(bucket.SetupPercent)]
                );

                html +=
                    '<div class="vas-223-row">' +
                    '<span class="vas-223-left">' +
                    '<span class="vas-223-label" title="' + escapeHtml(labelText) + '">' + escapeHtml(labelText) + '</span>' +
                    '<span class="vas-223-meta" title="' + escapeHtml(metaText) + '">' + escapeHtml(metaText) + '</span>' +
                    '</span>' +
                    '<span class="vas-223-value" title="' + escapeHtml(countText) + '">' + escapeHtml(countText) + '</span>' +
                    '</div>';
            }

            $bodyEl.html(html);
            renderPager(totalPages);

            /* Now that a row exists, check the estimate against reality - the page may
               need to shrink, and the pager to appear. */
            calibrateRowHeight();
        }

        /* The pager only appears when the buckets outrun the cell - with six document
           types plus an unclassified bucket that is the exception, not the rule. */
        function renderPager(totalPages) {
            var pagerHtml = '';
            if (totalPages > 1) {
                pagerHtml =
                    '<button type="button" class="vas-223-pbtn" data-page="prev"' + (currentPage === 0 ? ' disabled' : '') +
                    ' aria-label="' + escapeHtml(label("VAS_223_Previous", "Previous")) + '">' +
                    '<svg viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2.5" stroke-linecap="round" stroke-linejoin="round"><path d="M15 18l-6-6 6-6"/></svg>' +
                    '</button>' +
                    '<span class="vas-223-ptxt">' + (currentPage + 1) + ' ' + escapeHtml(label("VAS_223_Of", "of")) + ' ' + totalPages + '</span>' +
                    '<button type="button" class="vas-223-pbtn" data-page="next"' + (currentPage >= totalPages - 1 ? ' disabled' : '') +
                    ' aria-label="' + escapeHtml(label("VAS_223_Next", "Next")) + '">' +
                    '<svg viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2.5" stroke-linecap="round" stroke-linejoin="round"><path d="M9 18l6-6-6-6"/></svg>' +
                    '</button>';
            }

            $pagerEl.html(pagerHtml);
            $pagerEl.find('[data-page]').on('click', function () {
                var dir = $(this).attr('data-page');
                if (dir === 'prev' && currentPage > 0) { currentPage--; }
                else if (dir === 'next' && currentPage < totalPages - 1) { currentPage++; }
                else { return; }
                renderBuckets();
            });
        }

        /* Called by the platform Refresh button and whenever the host dashboard
           re-broadcasts a record change on the Recurring window. */
        this.refreshWidget = function () {
            loadBuckets();
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

    VAS.VAS_223_ByDocumentTypeWidget.prototype.widgetFirevalueChanged = function (value) {
        if (this.listener) { this.listener.widgetFirevalueChanged(value); }
    };

    VAS.VAS_223_ByDocumentTypeWidget.prototype.addChangeListener = function (listener) {
        this.listener = listener;
    };

    VAS.VAS_223_ByDocumentTypeWidget.prototype.init = function (windowNo, frame) {
        this.frame = frame;
        this.AD_UserHomeWidgetID = frame.widgetInfo.AD_UserHomeWidgetID;
        this.windowNo = windowNo;
        this.Initalize();
        this.frame.getContentGrid().append(this.getRoot());
        ensureDashInlineSizeVar(this.getRoot());
    };

    VAS.VAS_223_ByDocumentTypeWidget.prototype.widgetSizeChange = function (height, width) { };

    VAS.VAS_223_ByDocumentTypeWidget.prototype.dispose = function () {
        this.disposeComponent();
        if (this.frame) { this.frame.dispose(); }
        this.frame = null;
    };

})(VAS, jQuery);
