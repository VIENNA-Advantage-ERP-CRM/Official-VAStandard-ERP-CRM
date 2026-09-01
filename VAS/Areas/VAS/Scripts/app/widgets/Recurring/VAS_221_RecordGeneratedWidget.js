/**
 * VAS_221_RecordGeneratedWidget
 * 2x1 KPI tile + drill-down modal for the Recurring dashboard.
 *
 * Read-only. Answers "how many records did recurring runs generate in the current
 * period, and how does that compare with the period before?". The reporting period
 * is the tenant's ACCOUNTING period, resolved server side by reusing the Period
 * Control resolvers, so this widget and the ledger agree on where a period starts
 * and ends. No SQL and no DB call is made from the client.
 *
 * Layout (matches the widget.html build pack for this widget):
 *   line 1  [icon]  Records Generated               (icon well + widget title)
 *                   Current period vs preceding period   (widget subtitle)
 *   line 2  1,284                                   (KPI value)
 *   line 3  Jun 2026 · +8.4% vs May       View list ›
 *
 * Clicking the card opens the modal: a three-stat banner (this period / previous
 * period / change) over a paged list of the generated documents. It closes only
 * through its own controls or Escape - never on a backdrop click.
 *
 * The card and the list are separate requests on purpose - a busy tenant generates
 * four-figure run counts in a period, so the list is paged in the database and only
 * fetched once the user opens the modal.
 *
 * States:
 *   loading  busy overlay over the card; the modal shows the same busy indicator
 *            in the table region while a page is in flight
 *   empty    renders 0 - nothing generated is a real answer, not a takeover
 *   error    value falls back to an em dash, the meta line carries the reason and
 *            the card stops being clickable
 *
 * Amounts are shown untouched, in the generated document's own currency, with a
 * dedicated currency column beside them - nothing is converted, and figures in
 * different currencies are never implied to share a unit. Each row formats to its
 * own currency's standard precision.
 *
 * Summary Message Table
 *  # | Current Text                          | Message Key
 * ---+---------------------------------------+---------------------------------
 *  1 | Records Generated                     | VAS_221_RecordsGenerated
 *  2 | vs                                    | VAS_221_Vs
 *  3 | View list                             | VAS_221_ViewList
 *  4 | no prior period                       | VAS_221_NoPriorPeriod
 *  5 | This period                           | VAS_221_ThisPeriod
 *  6 | Previous period                       | VAS_221_PreviousPeriod
 *  7 | Change                                | VAS_221_Change
 *  8 | Current period vs preceding period    | VAS_221_PeriodComparison
 *  9 | Calendar month - no accounting period configured  | VAS_221_CalendarFallback
 * 10 | Generated                             | VAS_221_Generated
 * 11 | Document no                           | VAS_221_DocumentNo
 * 12 | Type                                  | VAS_221_Type
 * 13 | Business partner                      | VAS_221_BusinessPartner
 * 14 | Currency                              | VAS_221_Currency
 * 14a| Amount                                | VAS_221_Amount
 * 15 | Invoice                               | VAS_221_TypeInvoice
 * 16 | Order                                 | VAS_221_TypeOrder
 * 17 | Payment                               | VAS_221_TypePayment
 * 18 | GL Journal                            | VAS_221_TypeGLJournal
 * 19 | GL Journal Batch                      | VAS_221_TypeGLJournalBatch
 * 20 | Project                               | VAS_221_TypeProject
 * 21 | Other                                 | VAS_221_Other
 * 22 | Internal                              | VAS_221_Internal
 * 23 | Showing                               | VAS_221_Showing
 * 24 | of                                    | VAS_221_Of
 * 25 | generated this period                 | VAS_221_GeneratedThisPeriod
 * 26 | No records found                      | VAS_221_NoRecordsFound
 * 27 | Loading  (accessible name of the busy indicator) | VAS_221_Loading
 * 28 | Close                                 | VAS_221_Close
 * 29 | Previous                              | VAS_221_Previous
 * 30 | Next                                  | VAS_221_Next
 * 31 | Couldn't load                         | VAS_221_CouldntLoad
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

    /* Document-with-check glyph (inline SVG, not an icon-font class - the host shell
       does not always load an icon font and a missing glyph leaves an empty box). */
    var ICON_GENERATED =
        '<svg viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2" ' +
        'stroke-linecap="round" stroke-linejoin="round" aria-hidden="true" focusable="false">' +
        '<path d="M14 2H6a2 2 0 0 0-2 2v16a2 2 0 0 0 2 2h12a2 2 0 0 0 2-2V8z"></path>' +
        '<path d="M14 2v6h6"></path>' +
        '<path d="M9 15l2 2 4-4"></path>' +
        '</svg>';

    /* Derived document-type code -> message key + English fallback. The codes are
       produced by VASLogic.Models.VAS_221_RecordGeneratedModel and are aligned with
       C_Recurring.RecurringType - keep both sides in lock-step. Labels are never
       resolved in SQL. */
    var TYPE_MAP = {
        'I': { key: 'VAS_221_TypeInvoice', text: 'Invoice' },
        'O': { key: 'VAS_221_TypeOrder', text: 'Order' },
        'P': { key: 'VAS_221_TypePayment', text: 'Payment' },
        'B': { key: 'VAS_221_TypeGLJournal', text: 'GL Journal' },
        'G': { key: 'VAS_221_TypeGLJournalBatch', text: 'GL Journal Batch' },
        'J': { key: 'VAS_221_TypeProject', text: 'Project' }
    };

    /* Runs with no partner of their own - the counterparty is internal to the
       ledger. */
    var GL_TYPES = { 'B': true, 'G': true };

    var PAGE_SIZE = 10;

    VAS.VAS_221_RecordGeneratedWidget = function () {

        this.frame;
        this.windowNo;

        var $root = $('<div class="vas-221-root">');
        var $card;
        var $valueEl;
        var $subtitleEl;
        var $trendEl;
        var $linkEl;
        var $errorEl;
        var $busy;

        var resizeObserver = null;
        var kpiRequest = null;
        var rowsRequest = null;

        var cachedKpi = null;
        var currentPage = 0;
        var $modal = null;

        function label(key, fallback) {
            var translated = VIS.Msg.getMsg(key);
            return (translated && translated.charAt(0) !== '[') ? translated : fallback;
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
            $busy.toggleClass('vas-221-hidden', !show);
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

        /* Signed, one decimal, locale-aware. The sign is explicit because the reader
           needs the direction before the magnitude. */
        function formatPercent(value) {
            var n = Number(value);
            if (!isFinite(n)) { return ''; }

            var body = Math.abs(n).toLocaleString(window.navigator.language, {
                minimumFractionDigits: 1,
                maximumFractionDigits: 1
            });
            var sign = n > 0 ? '+' : (n < 0 ? '−' : '');
            return sign + body + '%';
        }

        /* The server transports dates as yyyy-MM-dd; display formatting is the
           client's job. Parsed field by field so the string is never read as UTC and
           shifted a day backwards for users west of Greenwich. */
        function toDate(iso) {
            if (!iso) { return null; }
            var parts = String(iso).split('-');
            if (parts.length !== 3) { return null; }

            var d = new Date(Number(parts[0]), Number(parts[1]) - 1, Number(parts[2]));
            return isNaN(d.getTime()) ? null : d;
        }

        function formatDate(iso) {
            var d = toDate(iso);
            if (!d) { return '—'; }

            try {
                return d.toLocaleDateString(window.navigator.language, {
                    day: '2-digit', month: 'short', year: 'numeric'
                });
            } catch (e) {
                return iso;
            }
        }

        /* A period label: the accounting period's own name when the calendar
           resolved one, otherwise the month the fallback window covers. */
        function periodLabel(name, isoFrom) {
            if (name) { return name; }

            var d = toDate(isoFrom);
            if (!d) { return '—'; }

            try {
                return d.toLocaleDateString(window.navigator.language, {
                    month: 'short', year: 'numeric'
                });
            } catch (e) {
                return isoFrom;
            }
        }

        /* What the card and the modal are comparing, in one wording used by both.
           Under the calendar fallback the window is not an accounting period, and the
           line says so rather than implying a fiscal period that was never resolved. */
        function periodSubtitle(isCalendarFallback) {
            return isCalendarFallback
                ? label("VAS_221_CalendarFallback", "Calendar month - no accounting period configured")
                : label("VAS_221_PeriodComparison", "Current period vs preceding period");
        }

        function typeLabel(code) {
            var entry = TYPE_MAP[code];
            return entry ? label(entry.key, entry.text) : label("VAS_221_Other", "Other");
        }

        /* GL runs carry no business partner - they are labelled as internal and
           qualified by the journal description when one exists. */
        function partnerLabel(row) {
            if (row.BPartnerName) { return row.BPartnerName; }
            if (!GL_TYPES[row.DocumentType]) { return '—'; }

            var internal = label("VAS_221_Internal", "Internal");
            return row.JournalDescription ? (internal + ' · ' + row.JournalDescription) : internal;
        }

        this.Initalize = function () {
            createWidget();
            setupResizeObserver();
            loadKpi();
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
            var title = label("VAS_221_RecordsGenerated", "Records Generated");

            /* The card names what it is comparing in a subtitle under the title.
               Seeded with the accounting-period wording so the card never renders a
               blank line, then corrected in renderMetric if the tenant turns out to
               have no accounting calendar. */
            var subtitle = periodSubtitle(false);

            /* Header row (icon well + title), KPI value, meta row. A div carries the
               card so it inherits no native button chrome; role and tabindex keep the
               drill-down reachable by keyboard and announced as actionable. */
            $card = $(
                '<div class="vas-221-card vas-widget-bg" role="button" tabindex="0" aria-disabled="false" aria-label="' + escapeHtml(title) + '">' +
                '<div class="vas-221-top">' +
                '<span class="vas-221-icon">' + ICON_GENERATED + '</span>' +
                '<span class="vas-221-titles">' +
                '<span class="vas-221-label">' + escapeHtml(title) + '</span>' +
                '<span class="vas-221-subtitle">' + escapeHtml(subtitle) + '</span>' +
                '</span>' +
                '</div>' +
                '<div class="vas-221-value">—</div>' +
                '<div class="vas-221-meta">' +
                '<span class="vas-221-trend"></span>' +
                '<span class="vas-221-link"></span>' +
                '</div>' +
                '<div class="vas-221-error"></div>' +
                '</div>'
            );

            $valueEl = $card.find('.vas-221-value');
            $subtitleEl = $card.find('.vas-221-subtitle');
            $trendEl = $card.find('.vas-221-trend');
            $linkEl = $card.find('.vas-221-link');
            $errorEl = $card.find('.vas-221-error');

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

            $busy = $('<div class="vas-221-busy vas-221-hidden"><div class="vis-busyindicatorinnerwrap"><i class="vis_widgetloader"></i></div></div>');
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

        function loadKpi() {
            /* A refresh fired while an earlier request is still open would otherwise
               let the stale response win the race and overwrite the newer figures. */
            if (kpiRequest) {
                kpiRequest.abort();
                kpiRequest = null;
            }

            showBusy(true);

            kpiRequest = $.ajax({
                url: VIS.Application.contextUrl + 'VAS_221_RecordGeneratedWidget/GetRecordsGenerated',
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
                    kpiRequest = null;
                    showBusy(false);
                }
            });
        }

        /* Nothing generated is a valid result and renders as 0. Only a failed load
           takes the card into its error state. */
        function renderMetric(data) {
            cachedKpi = data;

            var total = formatCount(data.CurrentCount);
            $valueEl.text(total).attr('title', total);

            var subtitle = periodSubtitle(data.IsCalendarFallback);
            $subtitleEl.text(subtitle).attr('title', subtitle);

            var currentName = periodLabel(data.CurrentPeriodName, data.CurrentDateFrom);
            var trendText = currentName;
            var trendClass = '';

            if (data.ChangePercent === null || typeof data.ChangePercent === 'undefined') {
                /* No earlier period, or it generated nothing - growth from zero is
                   not a percentage, so the comparison is reported as unavailable
                   rather than shown as an invented figure. */
                trendText += ' · ' + label("VAS_221_NoPriorPeriod", "no prior period");
            } else {
                var previousName = periodLabel(data.PreviousPeriodName, data.PreviousDateFrom);
                trendText += ' · ' + formatPercent(data.ChangePercent)
                    + ' ' + label("VAS_221_Vs", "vs") + ' ' + previousName;
                trendClass = data.ChangePercent > 0 ? 'vas-221-pos'
                    : (data.ChangePercent < 0 ? 'vas-221-neg' : '');
            }

            $trendEl
                .removeClass('vas-221-pos vas-221-neg')
                .addClass(trendClass)
                .text(trendText)
                .attr('title', trendText);

            var linkText = label("VAS_221_ViewList", "View list");
            $linkEl.text(linkText + ' ›').attr('title', linkText);

            $errorEl.text('').removeAttr('title');
            $card.removeClass('vas-221-card-error');
            setCardDisabled(false);

            /* The modal may already be open when a refresh lands - repaint its banner
               and reload the page rather than leaving superseded figures on screen. */
            if ($modal) {
                renderBanner();
                loadRows();
            }
        }

        function setError() {
            cachedKpi = null;
            closeModal();

            $valueEl.text('—').removeAttr('title');
            $trendEl.removeClass('vas-221-pos vas-221-neg').text('').removeAttr('title');
            $linkEl.text('').removeAttr('title');

            var msg = label("VAS_221_CouldntLoad", "Couldn't load");
            $errorEl.text(msg).attr('title', msg);

            /* Nothing to drill into while the load is broken. */
            $card.addClass('vas-221-card-error');
            setCardDisabled(true);
        }

        /* =====================================================================
           DRILL-DOWN MODAL
           ===================================================================== */

        function openModal() {
            if (!cachedKpi) { return; }
            if ($modal) { closeModal(); }

            currentPage = 0;

            var periodName = periodLabel(cachedKpi.CurrentPeriodName, cachedKpi.CurrentDateFrom);
            var title = label("VAS_221_RecordsGenerated", "Records Generated") + ' — ' + periodName;

            var subtitle = periodSubtitle(cachedKpi.IsCalendarFallback);

            var closeText = label("VAS_221_Close", "Close");

            $modal = $(
                '<div class="vas-221-mask" role="dialog" aria-modal="true" aria-label="' + escapeHtml(title) + '">' +
                '<div class="vas-221-modal">' +
                '<div class="vas-221-modal-head">' +
                '<div class="vas-221-htxt">' +
                '<h2 class="vas-221-mtitle">' + escapeHtml(title) + '</h2>' +
                '<div class="vas-221-msub">' + escapeHtml(subtitle) + '</div>' +
                '</div>' +
                '<button type="button" class="vas-221-xbtn" data-close="1" aria-label="' + escapeHtml(closeText) + '">' +
                '<svg viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2.2" stroke-linecap="round">' +
                '<path d="M18 6 6 18M6 6l12 12"/>' +
                '</svg>' +
                '</button>' +
                '</div>' +
                '<div class="vas-221-modal-body">' +
                '<div class="vas-221-banner"></div>' +
                '<div class="vas-221-tbl-wrap"></div>' +
                '</div>' +
                '<div class="vas-221-modal-foot">' +
                '<span class="vas-221-foot-note"></span>' +
                '<span class="vas-221-foot-actions">' +
                '<span class="vas-221-pager"></span>' +
                '<button type="button" class="vas-221-btn vas-221-btn-primary" data-close="1">' + escapeHtml(closeText) + '</button>' +
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

            $(document).on('keydown.vas221modal', function (e) {
                if (e.key === 'Escape') { closeModal(); }
            });

            renderBanner();
            loadRows();
        }

        function closeModal() {
            if (rowsRequest) {
                rowsRequest.abort();
                rowsRequest = null;
            }
            if ($modal) {
                $modal.off();
                $modal.remove();
                $modal = null;
            }
            $(document).off('keydown.vas221modal');
        }

        /* Three-stat banner: this period, the period it is measured against, and the
           change between them. */
        function renderBanner() {
            if (!$modal || !cachedKpi) { return; }

            var currentName = periodLabel(cachedKpi.CurrentPeriodName, cachedKpi.CurrentDateFrom);

            var previousLabel = label("VAS_221_PreviousPeriod", "Previous period");
            var previousValue = '—';
            if (cachedKpi.PreviousPeriodFound) {
                previousLabel += ' (' + periodLabel(cachedKpi.PreviousPeriodName, cachedKpi.PreviousDateFrom) + ')';
                previousValue = formatCount(cachedKpi.PreviousCount);
            }

            var changeValue = '—';
            var changeClass = '';
            if (cachedKpi.ChangePercent !== null && typeof cachedKpi.ChangePercent !== 'undefined') {
                changeValue = formatPercent(cachedKpi.ChangePercent);
                changeClass = cachedKpi.ChangePercent > 0 ? ' vas-221-pos'
                    : (cachedKpi.ChangePercent < 0 ? ' vas-221-neg' : '');
            }

            $modal.find('.vas-221-banner').html(
                statHtml(label("VAS_221_ThisPeriod", "This period") + ' (' + currentName + ')',
                    formatCount(cachedKpi.CurrentCount), '') +
                statHtml(previousLabel, previousValue, '') +
                statHtml(label("VAS_221_Change", "Change"), changeValue, changeClass)
            );
        }

        function statHtml(labelText, valueText, valueClass) {
            return '<div class="vas-221-stat">' +
                '<span class="vas-221-stat-l" title="' + escapeHtml(labelText) + '">' + escapeHtml(labelText) + '</span>' +
                '<span class="vas-221-stat-v' + valueClass + '" title="' + escapeHtml(valueText) + '">' + escapeHtml(valueText) + '</span>' +
                '</div>';
        }

        /* The list is paged in the database, so every page turn is a fresh request
           rather than a slice of an already-downloaded array. */
        function loadRows() {
            if (!$modal) { return; }

            if (rowsRequest) {
                rowsRequest.abort();
                rowsRequest = null;
            }

            /* The platform busy indicator, not a text line - the same spinner the card
               shows while its own request is open, so both surfaces read as one
               widget loading. The label survives as the accessible name so the state
               is still announced. */
            $modal.find('.vas-221-tbl-wrap').html(
                '<div class="vas-221-state vas-221-state-busy" role="status" aria-live="polite" ' +
                'aria-label="' + escapeHtml(label("VAS_221_Loading", "Loading")) + '">' +
                '<div class="vis-busyindicatorinnerwrap"><i class="vis_widgetloader"></i></div>' +
                '</div>'
            );

            rowsRequest = $.ajax({
                url: VIS.Application.contextUrl + 'VAS_221_RecordGeneratedWidget/GetGeneratedRecords',
                type: 'GET',
                data: { page: currentPage, pageSize: PAGE_SIZE },
                cache: false,
                success: function (res) {
                    var data;
                    try {
                        data = parseResponse(res);
                    } catch (e) {
                        renderRowsError();
                        return;
                    }

                    if (data.error || !data.Loaded) { renderRowsError(); return; }
                    renderRows(data);
                },
                error: function (xhr, status) {
                    if (status === 'abort') { return; }
                    renderRowsError();
                },
                complete: function () {
                    rowsRequest = null;
                }
            });
        }

        function renderRowsError() {
            if (!$modal) { return; }

            var msg = label("VAS_221_CouldntLoad", "Couldn't load");
            $modal.find('.vas-221-tbl-wrap').html('<div class="vas-221-state">' + escapeHtml(msg) + '</div>');
            $modal.find('.vas-221-foot-note').text(msg).attr('title', msg);
            $modal.find('.vas-221-pager').empty();
        }

        function renderRows(data) {
            if (!$modal) { return; }

            var rows = data.Rows || [];

            /* The server clamps the page index, so a stale page number is corrected
               rather than returning an unexplained empty list. */
            currentPage = Number(data.Page) || 0;

            var pageSize = Number(data.PageSize) || PAGE_SIZE;
            var totalRows = Number(data.TotalRows) || 0;
            var totalPages = Math.max(1, Math.ceil(totalRows / pageSize));
            var start = currentPage * pageSize;

            /* Amount sits in the trailing column, with the currency it is expressed in
               immediately before it - same column order as VAS_220, so the two
               Recurring modals scan identically. Amounts are not converted, so the
               header carries no currency of its own. */
            var headHtml =
                '<div class="vas-221-trow vas-221-gen-grid vas-221-thead">' +
                headCell(label("VAS_221_Generated", "Generated"), false) +
                headCell(label("VAS_221_DocumentNo", "Document no"), false) +
                headCell(label("VAS_221_Type", "Type"), false) +
                headCell(label("VAS_221_BusinessPartner", "Business partner"), false) +
                headCell(label("VAS_221_Currency", "Currency"), false) +
                headCell(label("VAS_221_Amount", "Amount"), true) +
                '</div>';

            var bodyHtml = '';
            for (var i = 0; i < rows.length; i++) {
                var row = rows[i];

                var dateText = formatDate(row.DateDoc);
                var docNoText = row.DocumentNo || '—';
                var typeText = typeLabel(row.DocumentType);
                var partnerText = partnerLabel(row);

                /* The figure is the generated document's own, formatted to that
                   currency's standard precision - a zero-decimal currency must not be
                   printed with two. */
                var amountText = formatAmount(row.Amount, row.AmountPrecision);
                var currencyText = row.AmountCurrencyIso || '—';
                var currencyTitle = row.AmountCurrencySymbol
                    ? (currencyText + ' · ' + row.AmountCurrencySymbol)
                    : currencyText;
                var amountTitle = amountText + (row.AmountCurrencyIso ? ' ' + row.AmountCurrencyIso : '');

                /* The setup that produced the document is not a column of its own in
                   this design, so it rides along as the document number's tooltip -
                   available on demand without spending a column on it. */
                var docNoTitle = row.RecurringName ? (docNoText + ' · ' + row.RecurringName) : docNoText;

                bodyHtml +=
                    '<div class="vas-221-trow vas-221-gen-grid vas-221-tbody-row">' +
                    '<span class="vas-221-cell vas-221-c-num" title="' + escapeHtml(dateText) + '">' + escapeHtml(dateText) + '</span>' +
                    '<span class="vas-221-cell vas-221-c-prim" title="' + escapeHtml(docNoTitle) + '">' + escapeHtml(docNoText) + '</span>' +
                    '<span class="vas-221-cell vas-221-c-std" title="' + escapeHtml(typeText) + '">' + escapeHtml(typeText) + '</span>' +
                    '<span class="vas-221-cell vas-221-c-std" title="' + escapeHtml(partnerText) + '">' + escapeHtml(partnerText) + '</span>' +
                    '<span class="vas-221-cell vas-221-c-cur" title="' + escapeHtml(currencyTitle) + '">' + escapeHtml(currencyText) + '</span>' +
                    '<span class="vas-221-cell vas-221-c-val" title="' + escapeHtml(amountTitle) + '">' + escapeHtml(amountText) + '</span>' +
                    '</div>';
            }

            if (rows.length === 0) {
                bodyHtml = '<div class="vas-221-state">' +
                    escapeHtml(label("VAS_221_NoRecordsFound", "No records found")) + '</div>';
            }

            $modal.find('.vas-221-tbl-wrap').html(
                '<div class="vas-221-tbl">' + headHtml + '<div class="vas-221-tbody">' + bodyHtml + '</div></div>'
            );

            /* Footer helper carries the dataset size, not the page size, so the user
               can see how much sits behind the pager. */
            var footNote = formatCount(totalRows) + ' ' +
                label("VAS_221_GeneratedThisPeriod", "generated this period");
            if (totalRows > 0 && totalPages > 1) {
                footNote = label("VAS_221_Showing", "Showing") + ' ' + (start + 1) + '–' + (start + rows.length)
                    + ' ' + label("VAS_221_Of", "of") + ' ' + footNote;
            }
            $modal.find('.vas-221-foot-note').text(footNote).attr('title', footNote);

            var pagerHtml = '';
            if (totalPages > 1) {
                pagerHtml =
                    '<button type="button" class="vas-221-pbtn" data-page="prev"' + (currentPage === 0 ? ' disabled' : '') +
                    ' aria-label="' + escapeHtml(label("VAS_221_Previous", "Previous")) + '">' +
                    '<svg viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2.4" stroke-linecap="round"><polyline points="15 18 9 12 15 6"/></svg>' +
                    '</button>' +
                    '<span class="vas-221-ptxt">' + (currentPage + 1) + ' ' + escapeHtml(label("VAS_221_Of", "of")) + ' ' + totalPages + '</span>' +
                    '<button type="button" class="vas-221-pbtn" data-page="next"' + (currentPage >= totalPages - 1 ? ' disabled' : '') +
                    ' aria-label="' + escapeHtml(label("VAS_221_Next", "Next")) + '">' +
                    '<svg viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2.4" stroke-linecap="round"><polyline points="9 18 15 12 9 6"/></svg>' +
                    '</button>';
            }

            var $pager = $modal.find('.vas-221-pager');
            $pager.html(pagerHtml);
            $pager.find('[data-page]').on('click', function () {
                var dir = $(this).attr('data-page');
                if (dir === 'prev' && currentPage > 0) { currentPage--; }
                else if (dir === 'next' && currentPage < totalPages - 1) { currentPage++; }
                else { return; }
                loadRows();
            });
        }

        function headCell(text, isRight) {
            return '<span class="vas-221-cell' + (isRight ? ' vas-221-c-right' : '') + '" title="' +
                escapeHtml(text) + '">' + escapeHtml(text) + '</span>';
        }

        /* Called by the platform Refresh button and whenever the host dashboard
           re-broadcasts a record change on the Recurring window. */
        this.refreshWidget = function () {
            loadKpi();
        };

        this.getRoot = function () { return $root; };

        this.disposeComponent = function () {
            if (kpiRequest) {
                kpiRequest.abort();
                kpiRequest = null;
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

    VAS.VAS_221_RecordGeneratedWidget.prototype.widgetFirevalueChanged = function (value) {
        if (this.listener) { this.listener.widgetFirevalueChanged(value); }
    };

    VAS.VAS_221_RecordGeneratedWidget.prototype.addChangeListener = function (listener) {
        this.listener = listener;
    };

    VAS.VAS_221_RecordGeneratedWidget.prototype.init = function (windowNo, frame) {
        this.frame = frame;
        this.AD_UserHomeWidgetID = frame.widgetInfo.AD_UserHomeWidgetID;
        this.windowNo = windowNo;
        this.Initalize();
        this.frame.getContentGrid().append(this.getRoot());
        ensureDashInlineSizeVar(this.getRoot());
    };

    VAS.VAS_221_RecordGeneratedWidget.prototype.widgetSizeChange = function (height, width) { };

    VAS.VAS_221_RecordGeneratedWidget.prototype.dispose = function () {
        this.disposeComponent();
        if (this.frame) { this.frame.dispose(); }
        this.frame = null;
    };

})(VAS, jQuery);
