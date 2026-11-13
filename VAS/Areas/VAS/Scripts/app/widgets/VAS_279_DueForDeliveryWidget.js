/**
 * VAS_279 Due for Delivery Widget (Sales Order dashboard, 2x2 KPI + preview)
 * Purpose - A near-term commitment window: firm (DocStatus 'CO') Sales Orders with
 *           pending delivery, promised for delivery in the CURRENT calendar month -
 *           no Month/Year filter, always recomputed against today. Shows a headline
 *           count, a supporting "value · next-7-days" meta line, and EVERY counted
 *           order, earliest promised first, in a scrollable list (Sr 8.31 - the list
 *           used to stop at 3 rows while the count could be higher). An overdue order promised earlier in the
 *           current month stays counted here while pending (this tile answers
 *           "promised in this month", not "not yet overdue" - VAS_274 Overdue
 *           Deliveries is the dedicated overdue view). The next-7-days figure is
 *           calendar days from today through today+7 inclusive and excludes rows
 *           that are already overdue.
 *
 * Design  - 12-due-for-delivery.html / .md: glass 2x2 tile, header (no filter),
 *           KPI headline + meta line directly under the header (not in a .kpi
 *           container), then the due orders separated by a top divider, paged by a
 *           footer pager whose page size follows the tile height (Sr 8.31: the list
 *           must account for every order in the headline count, and widgets never
 *           scroll). Each row is a full-width button: SO number (link
 *           color) over "<customer first word> · <promised date short>" on the
 *           left, order value on the right. Row click opens that order's record
 *           modal DIRECTLY - there is no intermediate documents-list modal for this
 *           widget, and no back button (nothing to go back to).
 *
 * Backend - VAS_279_DueForDeliveryWidget/GetSummary            (GET -> KPI figures + 3-row preview)
 *           VAS_279_DueForDeliveryWidget/GetSalesOrderDetail   (GET id -> order + lines)
 *
 * ── Labels / Message Keys ─────────────────────────────────────────────────
 *  #  | Current Text                                                          | Message Key
 * ----+------------------------------------------------------------------------+--------------------------------
 *  1  | Due for Delivery                                                      | VAS_279_Title
 *  2  | Promised in                                                           | VAS_279_SubtitlePrefix
 *  3  | due in the next 7 days                                                | VAS_279_Next7Suffix
 *  4  | Nothing promised for the rest of                                      | VAS_279_ZeroStateMetaPrefix
 *  5  | No deliveries due                                                     | VAS_279_ZeroStateList
 *  6  | Figures unavailable                                                   | VAS_279_ErrorState
 *  7  | Customer                                                              | VAS_279_Customer
 *  8  | SO date                                                               | VAS_279_SoDate
 *  9  | Date promised                                                         | VAS_279_DatePromised
 * 10  | SO value                                                              | VAS_279_SoValue
 * 11  | Ship from                                                             | VAS_279_ShipFrom
 * 12  | Delivery mode                                                         | VAS_279_DeliveryMode
 * 13  | Document status                                                       | VAS_279_DocumentStatus
 * 14  | Delivery status                                                       | VAS_279_DeliveryStatus
 * 15  | Sales order lines                                                     | VAS_279_SalesOrderLines
 * 16  | Line                                                                  | VAS_279_ColLine
 * 17  | Product                                                               | VAS_279_ColProduct
 * 18  | Attribute                                                             | VAS_279_ColAttribute
 * 19  | UoM                                                                   | VAS_279_ColUom
 * 20  | Ordered                                                               | VAS_279_ColOrdered
 * 21  | Delivered                                                             | VAS_279_ColDelivered
 * 22  | Pending                                                               | VAS_279_ColPending
 * 23  | In stock                                                              | VAS_279_ColInStock
 * 24  | Rate                                                                  | VAS_279_ColRate
 * 25  | Amount                                                                | VAS_279_ColAmount
 * 26  | Line status                                                           | VAS_279_ColLineStatus
 * 27  | Delivered                                                             | VAS_279_DeliveryFull
 * 28  | Partially delivered                                                   | VAS_279_DeliveryPartial
 * 29  | Not delivered                                                         | VAS_279_DeliveryNone
 * 30  | Partly delivered                                                      | VAS_279_LinePartial
 * 31  | In process                                                            | VAS_279_LineInProcess
 * 32  | lines                                                                 | VAS_279_LinesSuffix
 * 33  | qty ordered                                                           | VAS_279_QtyOrderedSuffix
 * 34  | qty short of stock                                                    | VAS_279_QtyShortSuffix
 * 35  | Close                                                                 | VAS_279_Close
 * 36  | Previous page                                                         | VAS_279_PrevPage
 * 37  | Next page                                                             | VAS_279_NextPage
 * 38  | of                                                                    | VAS_279_Of
 * 39  | Showing                                                               | VAS_279_Showing
 * 40  | Search is unavailable right now. Try again in a moment.               | VAS_279_LoadError
 * 41  | Overdue                                                               | VAS_279_OverdueTag
 */
; VAS = window.VAS || {};

; (function (VAS, $) {

    /* Sales Order dashboard request queue (shared by VAS_270-289).
       The dashboard starts ~20 widget calls at once on one login session and the server
       answers the overflow with HTTP 500 ("request queue limit of the session exceeded").
       GET calls are therefore run at most 3 at a time and retried (up to 2 times, with a
       short back-off) when the server answers 500/503/0. The first widget file loaded
       creates the singleton; every other widget reuses it. A failed card also gets a
       Retry button so the user never has to close and re-open the screen. */
    var VASSONet = window.__vasSONet = window.__vasSONet || (function () {
        var MAX_PARALLEL = 3, MAX_RETRIES = 2, running = 0, waiting = [];

        function pump() {
            while (running < MAX_PARALLEL && waiting.length) { start(waiting.shift()); }
        }

        function start(job) {
            running++;
            var o = job.opts;
            $.ajax($.extend({}, o, {
                success: function () {
                    running--;
                    if (o.success) { o.success.apply(this, arguments); }
                    pump();
                },
                error: function (xhr) {
                    var self = this, args = arguments;
                    running--;
                    var st = xhr ? xhr.status : 0;
                    if (job.tries < MAX_RETRIES && (st === 500 || st === 503 || st === 0) && xhr.statusText !== 'abort') {
                        job.tries++;
                        setTimeout(function () { waiting.push(job); pump(); }, 400 * job.tries + Math.floor(Math.random() * 300));
                    } else if (o.error) {
                        o.error.apply(self, args);
                    }
                    pump();
                }
            }));
        }

        function injectStyle() {
            if (document.getElementById('vasso-retry-style')) { return; }
            var st = document.createElement('style');
            st.id = 'vasso-retry-style';
            st.textContent = '.vasso-retry{position:absolute;right:10px;bottom:8px;z-index:3;border:1px solid currentColor;' +
                'background:rgba(255,255,255,.85);color:#1a5fb4;border-radius:12px;padding:2px 12px;font-size:12px;' +
                'line-height:18px;cursor:pointer}.vasso-retry:hover,.vasso-retry:focus{background:#fff;outline:2px solid #1a5fb4}';
            document.head.appendChild(st);
        }

        return {
            // GET calls are queued/retried; anything else (e.g. a POST that creates a record) runs untouched.
            ajax: function (o) {
                if (o && o.type && String(o.type).toUpperCase() !== 'GET') { return $.ajax(o); }
                waiting.push({ opts: o, tries: 0 });
                pump();
            },
            // Adds a Retry button to a widget root that is showing its error state.
            retry: function ($root, widget) {
                injectStyle();
                $root.find('.vasso-retry').remove();
                if ($root.css('position') === 'static') { $root.css('position', 'relative'); }
                var text = '';
                try { text = VIS.Msg.translate(VIS.Env.getCtx(), 'VAS_SO_Retry'); } catch (e) { text = ''; }
                if (!text || typeof text !== 'string' || text === 'VAS_SO_Retry' || text.charAt(0) === '[') { text = 'Retry'; }
                $('<button type="button" class="vasso-retry"></button>').text(text)
                    .attr('title', text)
                    .on('click', function (e) {
                        e.stopPropagation();
                        $(this).remove();
                        widget.refreshWidget();
                    })
                    .appendTo($root);
            }
        };
    })();


    var MAX_ROWS_PER_PAGE = 10;
    var MIN_ROWS_PER_PAGE = 2;
    // VAS163 2026-11-11: Row 114 - Sales Order window opened by "Open Record" (as VAS_270 / VAS_274).
    var ZOOM_WINDOW_NAME = 'VAS_SalesOrder';
    var MONTHS = ['Jan', 'Feb', 'Mar', 'Apr', 'May', 'Jun', 'Jul', 'Aug', 'Sep', 'Oct', 'Nov', 'Dec'];

    /* design.md §Widget Header / §Measurement Setup: keep --dash-inline-size on
       :root equal to the dashboard container's current pixel width so the widget
       clamp resolves against the dashboard's visible width, not the viewport. A
       single document-level ResizeObserver serves every widget (matches VAS_269-278). */
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

    VAS.VAS_279_DueForDeliveryWidget = function () {

        this.frame;
        this.windowNo;

        var $self = this;
        var $root = $('<div class="vas279-root">');
        var $shell, $kpiVal, $kpiMeta, $list, $tileFoot, tileObserver;
        var $mask, $modal, $mHead, $mTitle, $mSub, $mBack, $mBody, $mFoot;

        var SELF_ENDPOINT = 'VAS_279_DueForDeliveryWidget/';
        // Per-instance event-namespace suffix (Sr 8.28): disposing an old instance must
        // not unbind the window handlers of a re-created one with the same widget id.
        var instanceTag = 'i' + Math.random().toString(36).slice(2, 9);

        var summaryState = 'loading'; // 'loading' | 'ready' | 'error'
        var summary = null;
        var currency = { iso: '', symbol: '', precision: null }; // VAS163: base currency from GetSummary

        var lineState = null; // { order, lines, page, size, tableId }
        var tileState = { rows: [], page: 0, size: 3 }; // preview rows on the tile
        var zoomWindowId = 0; // VAS163 2026-11-11: Row 114 - resolved Sales Order window id

        var translatedTexts = null;   // message-key -> translated text (see loadMessages)

        // Every message key used by this widget, translated in ONE call.
        function loadMessages() {
            var elements = [
                'VAS_279_Title', 'VAS_279_SubtitlePrefix', 'VAS_279_Next7Suffix', 'VAS_279_ZeroStateMetaPrefix',
                'VAS_279_ZeroStateList', 'VAS_279_ErrorState', 'VAS_279_Customer', 'VAS_279_SoDate', 'VAS_279_DatePromised',
                'VAS_279_SoValue', 'VAS_279_ShipFrom', 'VAS_279_DeliveryMode', 'VAS_279_DocumentStatus',
                'VAS_279_DeliveryStatus', 'VAS_279_SalesOrderLines', 'VAS_279_ColLine', 'VAS_279_ColProduct',
                'VAS_279_ColAttribute', 'VAS_279_ColUom', 'VAS_279_ColOrdered', 'VAS_279_ColDelivered', 'VAS_279_ColPending',
                'VAS_279_ColInStock', 'VAS_279_ColRate', 'VAS_279_ColAmount', 'VAS_279_ColLineStatus',
                'VAS_279_DeliveryFull', 'VAS_279_LinePartial', 'VAS_279_LineInProcess', 'VAS_279_LinesSuffix',
                'VAS_279_QtyOrderedSuffix', 'VAS_279_QtyShortSuffix', 'VAS_279_Close', 'VAS_279_PrevPage',
                'VAS_279_NextPage', 'VAS_279_Of', 'VAS_279_Showing', 'VAS_279_LoadError', 'VAS_279_OverdueTag'
            ];
            try { translatedTexts = VIS.Msg.translate(VIS.Env.getCtx(), elements, true) || {}; }
            catch (e) { translatedTexts = {}; }
        }

        function label(key, fallback) {
            if (translatedTexts === null) { loadMessages(); }
            var text = translatedTexts[key];
            if (text === undefined || text === null || typeof text !== 'string' || text === '' || text === key || text.charAt(0) === '[') {
                return fallback;
            }
            return text;
        }

        /* Sr 8.31b: the "Overdue" tag showed its raw key in brackets ("[VAS_279_OverdueTag]")
           because the key is not in the message table and the old guard only caught the
           bare key. A missing message is detected in either form (bare key, or the key
           wrapped in brackets) and the English text is used until the key is added. */
        function messageOr(key, fallback) {
            if (translatedTexts === null) { loadMessages(); }
            var text = translatedTexts[key];
            text = String(text == null ? '' : text).trim();
            if (!text || text === key || text === '[' + key + ']' || /^\[[^\]]*\]$/.test(text)) { return fallback; }
            return text;
        }

        function escapeHtml(value) {
            return String(value == null ? '' : value)
                .replace(/&/g, '&amp;')
                .replace(/</g, '&lt;')
                .replace(/>/g, '&gt;')
                .replace(/"/g, '&quot;')
                .replace(/'/g, '&#039;');
        }

        function icon(name) {
            if (name === 'close') {
                return '<svg width="20" height="20" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2" stroke-linecap="round"><path d="M18 6 6 18M6 6l12 12"/></svg>';
            }
            if (name === 'prev') {
                return '<svg viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2.4" stroke-linecap="round"><polyline points="15 18 9 12 15 6"/></svg>';
            }
            if (name === 'next') {
                return '<svg viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2.4" stroke-linecap="round"><polyline points="9 18 15 12 9 6"/></svg>';
            }
            return '';
        }

        // VAS163 2026-11-10: browser locale (6-Nov retest2).
        // Base (accounting-schema) currency sent by the server with the summary payload.
        function applyCurrency(data) {
            if (!data) { return; }
            currency.iso = data.CurrencyIso || '';
            currency.symbol = data.CurrencySymbol || '';
            currency.precision = (typeof data.CurrencyPrecision === 'number') ? data.CurrencyPrecision : null;
        }

        // VAS163 2026-11-10: browser locale (6-Nov retest2).
        // Compact amount in the base currency (shared helper picks Cr/L/K or K/M/B by ISO).
        // VAS163 2026-11-12: delegates to VAS.formatAmountByBrowser (browser culture; server ISO + precision).
        function formatINR(value) {
            return VAS.formatAmountByBrowser(Number(value || 0), currency.iso, currency.precision, { compact: true, symbol: currency.symbol });
        }

        // VAS163 2026-11-10: browser locale (6-Nov retest2).
        function formatNum(value) {
            return Math.round(Number(value || 0)).toLocaleString(window.navigator.language);
        }

        // VAS163 2026-11-10: browser locale (6-Nov retest2).
        // Amounts in the given currency and precision.
        // VAS163 2026-11-12: delegates to VAS.formatAmountByBrowser (browser culture; server ISO + precision).
        function formatMoney(value, symbol, precision, iso) {
            if (symbol === undefined && iso === undefined) { return VAS.formatAmountByBrowser(Number(value || 0), currency.iso, currency.precision, { symbol: currency.symbol }); } // no currency passed -> base currency
            return VAS.formatAmountByBrowser(Number(value || 0), iso || '', precision, { symbol: symbol || '' });
        }

        // VAS163 2026-11-11: Row 93 - compact amount in the order's own (price list) currency.
        // VAS163 2026-11-12: Intl compact currency (browser culture) with the order's ISO + precision from the server.
        function formatDocCompact(value, symbol, iso, precision) {
            var prec = (typeof precision === 'number') ? precision : null;
            return VAS.formatAmountByBrowser(Number(value || 0), iso || '', prec, { compact: true, symbol: symbol || '' });
        }

        // VAS163 2026-11-11: Row 105 - quantities in the line's UOM can be fractional (e.g. ml).
        function formatQty(value) {
            return Number(value || 0).toLocaleString(window.navigator.language, { maximumFractionDigits: 2 });
        }

        // VAS163 2026-11-11: Row 112 - a description made only of blanks / separators is not
        // an attribute value (same rule as VAS_270 / VAS_274).
        function hasAttributeValue(text) {
            return !!text && /[^\s\-‐-―_.,:;()\[\]\/\\|]/.test(String(text));
        }

        // VAS163 2026-11-10: browser locale (6-Nov retest2).
        // yyyy-MM-dd parsed by parts (local date, no UTC shift).
        function parseIso(iso) {
            var m = /^(\d{4})-(\d{2})-(\d{2})/.exec(String(iso || ''));
            if (!m) { return null; }
            var d = new Date(Number(m[1]), Number(m[2]) - 1, Number(m[3]));
            return isNaN(d.getTime()) ? null : d;
        }

        // VAS163 2026-11-10: browser locale (6-Nov retest2).
        function formatDateShort(iso) {
            var d = parseIso(iso);
            if (!d) { return ''; }
            return d.toLocaleDateString(window.navigator.language, { day: '2-digit', month: 'short' });
        }

        // VAS163 2026-11-10: browser locale (6-Nov retest2).
        function formatDateFull(iso) {
            var d = parseIso(iso);
            if (!d) { return ''; }
            return d.toLocaleDateString(window.navigator.language, { day: '2-digit', month: 'short', year: 'numeric' });
        }

        // VAS163 2026-11-10: browser locale (6-Nov retest2).
        // monthOnly: just the month name (was the first word of "Nov 2026").
        function currentMonthLabel(monthOnly) {
            var now = new Date();
            var opts = monthOnly ? { month: 'short' } : { month: 'short', year: 'numeric' };
            return new Date(now.getFullYear(), now.getMonth(), 1).toLocaleDateString(window.navigator.language, opts);
        }

        // Truncating the customer to its first word is deliberate (design.md §9):
        // in a 2-column cell the full name would consume the row, and the date is
        // the more useful half.
        function firstWord(name) {
            var trimmed = String(name || '').trim();
            if (!trimmed) { return ''; }
            var idx = trimmed.indexOf(' ');
            return idx === -1 ? trimmed : trimmed.substring(0, idx);
        }

        function parseResponse(res) {
            var data = res;
            if (typeof data === 'string') { data = JSON.parse(data); }
            if (typeof data === 'string') { data = JSON.parse(data); }
            return data || {};
        }

        function cellHtml(value, cls, align) {
            return '<span class="vas279-cell' + (align === 'right' ? ' right' : '') + ' ' + cls + '" title="' + escapeHtml(value) + '">' + escapeHtml(value) + '</span>';
        }

        function statTile(l, v) {
            return '<div class="vas279-mstat"><div class="l">' + escapeHtml(l) + '</div><div class="v" title="' + escapeHtml(v) + '">' + escapeHtml(v) + '</div></div>';
        }

        /* ============================================================
         * Widget shell: header (no filter) + KPI block + paged order list
         * ============================================================ */
        function createWidget() {
            $shell = $('<div class="vas279-shell"></div>');

            var $head = $('<div class="vas279-head"></div>');
            var $htxt = $('<div class="vas279-head-txt"></div>');
            $htxt.append('<p class="vas279-title">' + escapeHtml(label('VAS_279_Title', 'Due for Delivery')) + '</p>');
            $htxt.append('<p class="vas279-sub">' + escapeHtml(label('VAS_279_SubtitlePrefix', 'Promised in') + ' ' + currentMonthLabel()) + '</p>');
            $head.append($htxt);

            $kpiVal = $('<p class="vas279-kpi-val"><span class="vas279-skel-val"></span></p>');
            $kpiMeta = $('<p class="vas279-kpi-meta"></p>');
            $list = $('<div class="vas279-mini"></div>');
            $tileFoot = $('<div class="vas279-mtfoot" hidden></div>');

            $shell.append($head, $kpiVal, $kpiMeta, $list, $tileFoot);
            $root.append($shell);
            bindList();

            // Sr 8.31: rows per page follow the list's height (Adaptive Row Count).
            if (typeof ResizeObserver !== 'undefined') {
                tileObserver = new ResizeObserver(function () { fitTileRows(); });
                tileObserver.observe($list[0]);
            }
        }

        /* Sr 8.28: (re)binds the list click. Run on init AND on every refresh - after a
           "refresh all" from the mosaic panel the host can strip jQuery handlers from
           the widget's DOM (re-attaching the same nodes), which left rows that rendered
           fine but opened nothing. off() first keeps it to one handler. */
        function bindList() {
            if (!$list) { return; }
            $list.off('click.vas279').on('click.vas279', function (event) {
                var row = event.target.closest ? event.target.closest('[data-so]') : null;
                if (row) { openRecordModal(Number(row.getAttribute('data-so'))); }
            });
            $tileFoot.off('click.vas279').on('click.vas279', function (event) {
                var pageBtn = event.target.closest ? event.target.closest('[data-dir]') : null;
                if (pageBtn && !pageBtn.disabled) {
                    tileState.page += Number(pageBtn.getAttribute('data-dir'));
                    drawTileRows();
                }
            });
        }

        function skeletonRows() {
            var rows = '';
            for (var i = 0; i < 3; i++) {
                rows += '<div class="vas279-mrow2 vas279-skel-row"><span class="vas279-mtxt"><span class="vas279-skel-nm"></span><span class="vas279-skel-mt"></span></span></div>';
            }
            return rows;
        }

        function loadSummary() {
            summaryState = 'loading';
            renderWidget();

            VASSONet.ajax({
                url: VIS.Application.contextUrl + SELF_ENDPOINT + 'GetSummary',
                type: 'GET', dataType: 'json', cache: false,
                success: function (res) {
                    var parsed = parseResponse(res);
                    if (parsed.Error) { summaryState = 'error'; summary = null; }
                    else { summaryState = 'ready'; summary = parsed; applyCurrency(parsed); }
                    renderWidget();
                },
                error: function () {
                    summaryState = 'error'; summary = null;
                    renderWidget();
                }
            });
        }

        function renderWidget() {
            tileState.rows = [];
            $tileFoot.attr('hidden', true).html('');
            if (summaryState === 'loading') {
                $kpiVal.html('<span class="vas279-skel-val"></span>');
                $kpiMeta.text('');
                $list.html(skeletonRows());
                return;
            }
            if (summaryState === 'error' || !summary) {
                $kpiVal.removeClass().addClass('vas279-kpi-val error').text('—');
                $kpiMeta.text(label('VAS_279_ErrorState', 'Figures unavailable'));
                VASSONet.retry($root, $self);
                $list.html('');
                return;
            }

            $kpiVal.removeClass().addClass('vas279-kpi-val info').text(formatNum(summary.DueThisMonthCount));

            if (summary.DueThisMonthCount <= 0) {
                $kpiMeta.text(label('VAS_279_ZeroStateMetaPrefix', 'Nothing promised for the rest of') + ' ' + currentMonthLabel(true));
                $list.html('<div class="vas279-empty">' + escapeHtml(label('VAS_279_ZeroStateList', 'No deliveries due')) + '</div>');
                return;
            }

            // VAS163 2026-11-12: compact KPI in the base currency.
            var meta = formatINR(summary.DueThisMonthValue) + ' · ' + formatNum(summary.DueNext7DaysCount) + ' ' + label('VAS_279_Next7Suffix', 'due in the next 7 days');
            $kpiMeta.attr('title', meta).text(meta);

            tileState.rows = summary.Preview || [];
            tileState.page = 0;
            drawTileRows();
            fitTileRows();
        }

        /* Sr 8.31: the headline count covers every due order, so the tile pages through all
           of them; the "Showing a–b of N" helper always matches the rows on screen. */
        function fitTileRows() {
            if (!tileState.rows.length || !$list) { return; }
            var first = $list.children('.vas279-mrow2')[0];
            if (!first || !first.offsetHeight) { return; }
            var avail = $list[0].clientHeight - (parseFloat($list.css('padding-top')) || 0);
            var size = Math.max(1, Math.floor(avail / first.offsetHeight));
            if (size !== tileState.size) {
                tileState.size = size;
                drawTileRows();
            }
        }

        function drawTileRows() {
            var rows = tileState.rows;
            var pages = Math.max(1, Math.ceil(rows.length / tileState.size));
            tileState.page = Math.min(pages - 1, Math.max(0, tileState.page));
            var start = tileState.page * tileState.size;
            var slice = rows.slice(start, start + tileState.size);

            var todayStart = new Date(); todayStart.setHours(0, 0, 0, 0);
            $list.html(slice.map(function (row) {
                var meta2 = escapeHtml(firstWord(row.CustomerName)) + ' · ' + escapeHtml(formatDateShort(row.PromisedDate));
                // A promised date already behind us is flagged so the row is not mistaken for an upcoming delivery.
                var todayIso = new Date(); todayIso = todayIso.getFullYear() + '-' + ('0' + (todayIso.getMonth() + 1)).slice(-2) + '-' + ('0' + todayIso.getDate()).slice(-2);
                if (row.PromisedDate && String(row.PromisedDate).slice(0, 10) < todayIso) { meta2 += ' · ' + escapeHtml(messageOr('VAS_279_OverdueTag', 'Overdue')); }
                // VAS163 2026-11-11: Row 93 - order value with the order's price-list currency symbol.
                // VAS163 2026-11-12: document currency when the server sent its ISO, else base.
                var rowValue = row.CurrencyIso
                    ? formatDocCompact(row.OrderValue, row.CurrencySymbol, row.CurrencyIso, row.CurrencyPrecision)
                    : formatINR(row.OrderValue);
                return '<button type="button" class="vas279-mrow2" data-so="' + row.SalesOrderId + '" title="' + escapeHtml(row.SalesOrderNumber) + '">' +
                    '<span class="vas279-mtxt">' +
                        '<span class="vas279-mname" title="' + escapeHtml(row.SalesOrderNumber) + '">' + escapeHtml(row.SalesOrderNumber) + '</span>' +
                        '<span class="vas279-mmeta" title="' + escapeHtml(row.CustomerName) + ' · ' + escapeHtml(formatDateFull(row.PromisedDate)) + '">' + meta2 + '</span>' +
                    '</span>' +
                    '<span class="vas279-mval" title="' + escapeHtml(rowValue) + '">' + escapeHtml(rowValue) + '</span></button>';
            }).join(''));

            var showing = label('VAS_279_Showing', 'Showing') + ' ' + formatNum(start + 1) + '–' + formatNum(start + slice.length) + ' ' + label('VAS_279_Of', 'of') + ' ' + formatNum(rows.length);
            $tileFoot.removeAttr('hidden').html('<span class="vas279-helper" title="' + escapeHtml(showing) + '">' + escapeHtml(showing) + '</span>' +
                (pages > 1 ?
                    '<span class="vas279-pager">' +
                        '<button type="button" class="vas279-pbtn" data-dir="-1"' + (tileState.page === 0 ? ' disabled' : '') + ' aria-label="' + escapeHtml(label('VAS_279_PrevPage', 'Previous page')) + '">' + icon('prev') + '</button>' +
                        '<span class="vas279-ptxt">' + formatNum(tileState.page + 1) + ' ' + escapeHtml(label('VAS_279_Of', 'of')) + ' ' + formatNum(pages) + '</span>' +
                        '<button type="button" class="vas279-pbtn" data-dir="1"' + (tileState.page >= pages - 1 ? ' disabled' : '') + ' aria-label="' + escapeHtml(label('VAS_279_NextPage', 'Next page')) + '">' + icon('next') + '</button>' +
                    '</span>' : '<span></span>'));
        }

        /* ============================================================
         * Record modal - opened DIRECTLY by a preview row click. There is no
         * parent/documents-list modal for this widget, so the back button never
         * shows and closing always clears to a clean slate.
         * ============================================================ */
        function createModal() {
            $mask = $('<div class="vas279-mask" role="dialog" aria-modal="true"></div>');
            $modal = $('<div class="vas279-modal"></div>');
            $mHead = $('<div class="vas279-mhead"></div>');
            var $htxt = $('<div class="vas279-htxt"></div>');
            $mBack = $('<button type="button" class="vas279-xbtn" hidden></button>');
            var $titleWrap = $('<div></div>');
            $mTitle = $('<h2></h2>');
            $mSub = $('<div class="vas279-msub"></div>');
            $titleWrap.append($mTitle, $mSub);
            $htxt.append($mBack, $titleWrap);
            var $closeBtn = $('<button type="button" class="vas279-xbtn" aria-label="' + escapeHtml(label('VAS_279_Close', 'Close')) + '">' + icon('close') + '</button>');
            $mHead.append($htxt, $closeBtn);

            $mBody = $('<div class="vas279-mbody"></div>');
            $mFoot = $('<div class="vas279-mfoot"></div>');

            $modal.append($mHead, $mBody, $mFoot);
            $mask.append($modal);
            $('body').append($mask);

            $closeBtn.on('click', closeModal);
            $mBody.on('click', function (event) {
                var pageBtn = event.target.closest ? event.target.closest('[data-dir]') : null;
                if (pageBtn) { turnLinePage(Number(pageBtn.getAttribute('data-dir'))); }
            });
        }

        function bindDocumentLevelEvents() {
            var ns = '.vas279-' + ($self.AD_UserHomeWidgetID || $self.windowNo || 'widget') + '-' + instanceTag;

            $(window).on('resize' + ns, function () {
                if ($mask.hasClass('is-open')) { fitLineTable(); }
            });
        }

        function closeModal() {
            $mask.removeClass('is-open');
            lineState = null;
        }

        /* VAS163 2026-11-11: Row 114 - same as VAS_270 / VAS_274 openRecordInWindow: inside the
           Sales Order window (windowNo >= 0) the host's value-changed channel re-queries that
           window; on the Home page the shared zoom helper opens the Sales Order window. */
        function openRecordInWindow(orderId) {
            closeModal();
            if (!orderId) { return; }
            if ($self.windowNo >= 0) {
                $self.widgetFirevalueChanged({
                    "TabWhereClause": "C_Order.C_Order_ID=" + Number(orderId),
                    "TabLayout": "Y",
                    "TabIndex": "0"
                });
                return;
            }
            if (!window.VAS || !VAS.ZoomUtil) { return; }
            VAS.ZoomUtil.zoomToRecord('C_Order_ID', orderId, zoomWindowId, ZOOM_WINDOW_NAME, 'Sales Order') // VAS163 2026-11-12: legacy window name as fallback
                .done(function (id) { if (id > 0) { zoomWindowId = id; } });
        }

        function showLoading() {
            $mTitle.text('…');
            $mSub.text('');
            $mBody.html('<div class="vas279-mstate">…</div>');
            $mFoot.html('');
            $mask.addClass('is-open');
        }

        function showLoadError() {
            $mBody.html('<div class="vas279-mstate">' + escapeHtml(label('VAS_279_LoadError', 'Search is unavailable right now. Try again in a moment.')) + '</div>');
        }

        function lineStatus(line) {
            if (line.QtyDelivered >= line.QtyOrdered && line.QtyOrdered > 0) { return { text: label('VAS_279_DeliveryFull', 'Delivered'), cls: 'vas279-chip-info' }; }
            if (line.QtyDelivered > 0) { return { text: label('VAS_279_LinePartial', 'Partly delivered'), cls: 'vas279-chip-warn' }; }
            return { text: label('VAS_279_LineInProcess', 'In process'), cls: 'vas279-chip-prop' };
        }

        function openRecordModal(orderId) {
            showLoading();

            VASSONet.ajax({
                url: VIS.Application.contextUrl + SELF_ENDPOINT + 'GetSalesOrderDetail',
                type: 'GET', dataType: 'json', cache: false,
                data: { id: orderId },
                success: function (res) {
                    var parsed = parseResponse(res);
                    var order = parsed && parsed.Order;
                    if (parsed.Error || !order || !order.SalesOrderId) { showLoadError(); return; }
                    renderRecordModal(order, parsed.Lines || []);
                },
                error: function () { showLoadError(); }
            });
        }

        function renderRecordModal(order, lines) {
            $mTitle.text(order.SalesOrderNumber);
            $mSub.text([order.CustomerName, formatDateShort(order.SalesOrderDate), order.DocumentStatus].filter(function (p) { return p; }).join(' · '));

            // VAS163 2026-11-11: Row 103 - Customer / SO date / Document status / Delivery status
            // cards removed (4 cards left, one row of the 4-column grid).
            // Rows 108/109/113 - SO value in full (not K/L/Cr), ex-tax, in the order's currency.
            var statsHtml = '<div class="vas279-mstats">' +
                statTile(label('VAS_279_DatePromised', 'Date promised'), formatDateFull(order.DatePromised)) +
                statTile(label('VAS_279_SoValue', 'SO value'), formatMoney(order.OrderValue, order.CurrencySymbol, order.CurrencyPrecision, order.CurrencyIso)) +
                statTile(label('VAS_279_ShipFrom', 'Ship from'), order.WarehouseName) +
                statTile(label('VAS_279_DeliveryMode', 'Delivery mode'), order.DeliveryMode) +
            '</div>';

            var secHtml = '<div class="vas279-msec">' + escapeHtml(label('VAS_279_SalesOrderLines', 'Sales order lines')) + '</div>';
            $mBody.html(statsHtml + secHtml + '<div class="vas279-mtwrap"><div class="vas279-mtbl" id="vas279-linetbl"></div></div>');

            lineState = { order: order, lines: lines, page: 0, size: MAX_ROWS_PER_PAGE };
            drawLineTable();

            // VAS163 2026-11-11: Rows 105/110 - Item lines only, quantities in the line's UOM;
            // shortage stated by the server in the line's UOM (stock never negative).
            var qtyOrdered = 0, qtyShort = 0;
            lines.forEach(function (line) {
                if (!line.IsItem) { return; }
                qtyOrdered += Number(line.QtyOrdered) || 0;
                qtyShort += Math.max(0, Number(line.QtyShort) || 0);
            });
            var footNote = lines.length + ' ' + label('VAS_279_LinesSuffix', 'lines') +
                ' · ' + formatQty(qtyOrdered) + ' ' + label('VAS_279_QtyOrderedSuffix', 'qty ordered') +
                ' · ' + order.DeliveryStatus +
                (qtyShort > 0 ? ' · ' + formatQty(qtyShort) + ' ' + label('VAS_279_QtyShortSuffix', 'qty short of stock') : '');

            // VAS163 2026-11-11: Row 114 - "Open Record" next to Close opens the Sales Order
            // (existing key VAS_268_OpenRecord, same text, used by the SO search widget).
            $mFoot.html('<span class="vas279-foot-note">' + escapeHtml(footNote) + '</span>' +
                '<span><button type="button" class="vas279-btn" id="vas279-mclose">' + escapeHtml(label('VAS_279_Close', 'Close')) + '</button> ' +
                '<button type="button" class="vas279-btn vas279-btn-primary" id="vas279-mopen">' + escapeHtml(messageOr('VAS_268_OpenRecord', 'Open Record')) + '</button></span>');
            $mFoot.find('#vas279-mclose').on('click', closeModal);
            $mFoot.find('#vas279-mopen').on('click', function () { openRecordInWindow(order.SalesOrderId); });

            requestAnimationFrame(function () { fitLineTable(); requestAnimationFrame(fitLineTable); });
        }

        var LINE_COLS = [
            { key: 'no', label: label('VAS_279_ColLine', 'Line'), w: .35, align: 'right' },
            { key: 'product', label: label('VAS_279_ColProduct', 'Product'), w: 1.5 },
            { key: 'attr', label: label('VAS_279_ColAttribute', 'Attribute'), w: 1.1 },
            { key: 'uom', label: label('VAS_279_ColUom', 'UoM'), w: .5 },
            { key: 'ordered', label: label('VAS_279_ColOrdered', 'Ordered'), w: .7, align: 'right' },
            { key: 'delivered', label: label('VAS_279_ColDelivered', 'Delivered'), w: .7, align: 'right' },
            { key: 'pending', label: label('VAS_279_ColPending', 'Pending'), w: .7, align: 'right' },
            { key: 'instock', label: label('VAS_279_ColInStock', 'In stock'), w: .7, align: 'right' },
            { key: 'rate', label: label('VAS_279_ColRate', 'Rate'), w: .7, align: 'right' },
            { key: 'amount', label: label('VAS_279_ColAmount', 'Amount'), w: .9, align: 'right' },
            { key: 'status', label: label('VAS_279_ColLineStatus', 'Line status'), w: 1 }
        ];

        // VAS163 2026-11-11: In stock is in the product's base UOM; its name is shown when
        // the line was entered in another UOM (as VAS_270 / VAS_274). Never negative.
        function inStockText(line) {
            var text = formatQty(Math.max(0, Number(line.FreeStock) || 0));
            if (line.BaseUomName && line.BaseUomName !== line.UomName) { text += ' ' + line.BaseUomName; }
            return text;
        }

        function drawLineTable() {
            var el = document.getElementById('vas279-linetbl');
            if (!el || !lineState) { return; }

            // VAS163 2026-11-11: Row 112 - the Attribute column only when at least one line of
            // the order has an attribute value (decided over all lines, not per page).
            var showAttribute = lineState.lines.some(function (l) { return hasAttributeValue(l.AttributeText); });
            var cols = LINE_COLS.filter(function (c) { return showAttribute || c.key !== 'attr'; });
            // VAS163 2026-11-11: Row 109 - document (price list) currency in front of Rate / Amount.
            var cur = lineState.order.CurrencySymbol;
            var prec = lineState.order.CurrencyPrecision;

            var tpl = cols.map(function (c) { return 'minmax(0,' + c.w + 'fr)'; }).join(' ');
            var pages = Math.max(1, Math.ceil(lineState.lines.length / lineState.size));
            if (lineState.page > pages - 1) { lineState.page = pages - 1; }
            var start = lineState.page * lineState.size;
            var slice = lineState.lines.slice(start, start + lineState.size);

            var head = '<div class="vas279-mrow vas279-mhead-row" style="grid-template-columns:' + tpl + '">' +
                cols.map(function (c) { return '<span class="vas279-cell' + (c.align === 'right' ? ' right' : '') + '" title="' + escapeHtml(c.label) + '">' + escapeHtml(c.label) + '</span>'; }).join('') +
            '</div>';

            var body = slice.map(function (line) {
                // VAS163 2026-11-11: Rows 107/110 - Ordered / Delivered / Pending / In stock / Line
                // status only for Item-type products; other product types and charges show "-".
                var isItem = !!line.IsItem;
                var st = isItem ? lineStatus(line) : null;
                var cells = [
                    cellHtml(String(line.LineNo), 'vas279-c-std', 'right'),
                    // VAS163 2026-11-11: Row 106 - charge lines carry the charge name (server).
                    cellHtml(line.ProductName, 'vas279-c-prim'),
                    showAttribute ? cellHtml(hasAttributeValue(line.AttributeText) ? line.AttributeText : '', 'vas279-c-std') : '',
                    cellHtml(line.UomName, 'vas279-c-std'),
                    // VAS163 2026-11-11: Row 105 - quantities in the line's selected UOM (server).
                    cellHtml(isItem ? formatQty(line.QtyOrdered) : '-', 'vas279-c-std', 'right'),
                    cellHtml(isItem ? formatQty(line.QtyDelivered) : '-', 'vas279-c-std', 'right'),
                    cellHtml(isItem ? formatQty(line.QtyPending) : '-', 'vas279-c-prim', 'right'),
                    cellHtml(isItem ? inStockText(line) : '-', (isItem && Number(line.QtyShort) > 0 ? 'vas279-c-short' : 'vas279-c-ok'), 'right'),
                    // VAS163 2026-11-11: Rows 109/113 - ex-tax Rate / Amount in the order's currency.
                    cellHtml(formatMoney(line.Rate, cur, Math.max(2, (typeof prec === 'number') ? prec : 2), lineState.order.CurrencyIso), 'vas279-c-std', 'right'),
                    cellHtml(formatMoney(line.Amount, cur, prec, lineState.order.CurrencyIso), 'vas279-c-emph', 'right')
                ].join('') + (st
                    ? '<span class="vas279-cell"><span class="vas279-chip ' + st.cls + '" title="' + escapeHtml(st.text) + '">' + escapeHtml(st.text) + '</span></span>'
                    : '<span class="vas279-cell">-</span>');
                return '<div class="vas279-mrow" style="grid-template-columns:' + tpl + '">' + cells + '</div>';
            }).join('');

            var showingLabel = label('VAS_279_Showing', 'Showing') + ' ' + formatNum(lineState.lines.length ? (start + 1) : 0) + '–' + formatNum(start + slice.length) + ' ' + label('VAS_279_Of', 'of') + ' ' + formatNum(lineState.lines.length);

            var foot = '<div class="vas279-mtfoot"><span class="vas279-helper">' + escapeHtml(showingLabel) + '</span>' +
                (pages > 1 ?
                    '<span class="vas279-pager">' +
                        '<button type="button" class="vas279-pbtn" data-dir="-1"' + (lineState.page === 0 ? ' disabled' : '') + ' aria-label="' + escapeHtml(label('VAS_279_PrevPage', 'Previous page')) + '">' + icon('prev') + '</button>' +
                        '<span class="vas279-ptxt">' + formatNum(lineState.page + 1) + ' ' + escapeHtml(label('VAS_279_Of', 'of')) + ' ' + formatNum(pages) + '</span>' +
                        '<button type="button" class="vas279-pbtn" data-dir="1"' + (lineState.page >= pages - 1 ? ' disabled' : '') + ' aria-label="' + escapeHtml(label('VAS_279_NextPage', 'Next page')) + '">' + icon('next') + '</button>' +
                    '</span>' : '<span></span>') +
            '</div>';

            el.innerHTML = head + '<div class="vas279-mbody-rows">' + body + '</div>' + foot;
        }

        function turnLinePage(dir) {
            if (!lineState) { return; }
            var pages = Math.max(1, Math.ceil(lineState.lines.length / lineState.size));
            lineState.page = Math.min(pages - 1, Math.max(0, lineState.page + dir));
            drawLineTable();
        }

        /* Sizes the line table's rows-per-page to the space actually left in the
           modal body, so the body never grows an inner scrollbar. */
        function fitLineTable() {
            if (!$mask.hasClass('is-open') || !lineState) { return; }

            var el = document.getElementById('vas279-linetbl');
            if (!el) { return; }
            var avail = el.clientHeight;
            if (avail < 40) { return; }
            var head = el.querySelector('.vas279-mhead-row');
            var foot = el.querySelector('.vas279-mtfoot');
            var row = el.querySelector('.vas279-mbody-rows .vas279-mrow');
            if (!head || !row) { return; }
            var rowHeight = row.getBoundingClientRect().height || 30;
            var used = head.getBoundingClientRect().height + (foot ? foot.getBoundingClientRect().height + 8 : 0);
            var n = Math.floor((avail - used) / rowHeight);
            n = Math.max(MIN_ROWS_PER_PAGE, Math.min(MAX_ROWS_PER_PAGE, n));
            if (n !== lineState.size) {
                lineState.size = n;
                drawLineTable();
            }
        }

        this.Initalize = function () {
            loadMessages();
            createWidget();
            createModal();
            bindDocumentLevelEvents();
            loadSummary();
        };

        this.getRoot = function () { return $root; };

        this.disposeComponent = function () {
            var ns = '.vas279-' + ($self.AD_UserHomeWidgetID || $self.windowNo || 'widget') + '-' + instanceTag;
            $(document).off(ns);
            $(window).off(ns);
            if (tileObserver) { tileObserver.disconnect(); tileObserver = null; }
            if ($mask) { $mask.remove(); }
            $root.remove();
        };

        // Sr 8.28: a refresh also re-arms the list and re-creates the modal shell if
        // the host removed it, so the widget stays clickable after "refresh all".
        this.refreshWidget = function () {
            if (!$mask || !$.contains(document.body, $mask[0])) { createModal(); }
            if ($shell && !$.contains($root[0], $shell[0])) { $root.append($shell); }
            bindList();
            loadSummary();
        };
    };

    VAS.VAS_279_DueForDeliveryWidget.prototype.widgetFirevalueChanged = function (value) {
        if (this.listener) { this.listener.widgetFirevalueChanged(value); }
    };

    VAS.VAS_279_DueForDeliveryWidget.prototype.addChangeListener = function (listener) {
        this.listener = listener;
    };

    VAS.VAS_279_DueForDeliveryWidget.prototype.init = function (windowNo, frame) {
        this.frame = frame;
        this.AD_UserHomeWidgetID = frame.widgetInfo.AD_UserHomeWidgetID;
        this.windowNo = windowNo;
        this.Initalize();
        this.frame.getContentGrid().append(this.getRoot());
        ensureDashInlineSizeVar(this.getRoot());
    };

    VAS.VAS_279_DueForDeliveryWidget.prototype.widgetSizeChange = function () { };

    VAS.VAS_279_DueForDeliveryWidget.prototype.dispose = function () {
        if (this.disposeComponent) { this.disposeComponent(); }
    };

})(VAS, jQuery);
/* VAS163 2026-11-12: VAS.formatAmountByBrowser(value, isoCode, precision, { compact, symbol })
   Amount in the BROWSER culture (window.navigator.language) with the currency the SERVER sent
   (ISO code + precision; the server symbol only as a fallback). Defined in each widget / panel
   that uses it - guarded, so the first file loaded defines it - so the shared util/CurrencyFormat.js
   stays as shipped. Compact amounts keep that file's default scale (VIS.Util.formatCompactAmount:
   L / Cr for Indian-numbering currencies, K / M / B otherwise, digits in the browser culture);
   separators, decimals and the position of the currency sign come from the browser culture. */
; (function () {
    var V = window.VAS = window.VAS || {};
    if (typeof V.formatAmountByBrowser === 'function') return;
    function browserLocale() { try { return window.navigator.language || undefined; } catch (e) { return undefined; } }
    function resolvePrecision(p) {
        var n = Number(p);
        if (p === null || p === undefined || p === '' || isNaN(n) || n < 0) {
            try { n = VIS.Env.getCtx().getStdPrecision(); } catch (e) { n = 2; }
        }
        n = Math.round(Number(n));
        return (isNaN(n) || n < 0) ? 2 : Math.min(n, 10);
    }
    function validIso(c) { c = String(c || '').trim().toUpperCase(); return /^[A-Z]{3}$/.test(c) ? c : ''; }
    function numberFormat(o) {
        try { return new Intl.NumberFormat(browserLocale(), o); } catch (e) { }
        if (o.currencyDisplay === 'narrowSymbol') {
            o.currencyDisplay = 'symbol';
            try { return new Intl.NumberFormat(browserLocale(), o); } catch (e2) { }
        }
        return null;
    }
    /* Currency sign and where the browser culture puts it for this ISO code. */
    var layoutCache = {};
    function currencyLayout(iso) {
        var key = browserLocale() + '|' + iso;
        if (layoutCache[key]) return layoutCache[key];
        var out = { sign: '', before: true, gap: '' };
        var f = iso ? numberFormat({ style: 'currency', currency: iso, currencyDisplay: 'narrowSymbol' }) : null;
        if (f && typeof f.formatToParts === 'function') {
            try {
                var parts = f.formatToParts(1234.5), ci = -1, ni = -1;
                for (var i = 0; i < parts.length; i++) {
                    if (parts[i].type === 'currency' && ci < 0) ci = i;
                    if (parts[i].type === 'integer' && ni < 0) ni = i;
                }
                if (ci >= 0) {
                    out.sign = parts[ci].value;
                    if (ni >= 0) {
                        out.before = ci < ni;
                        var near = out.before ? parts[ci + 1] : parts[ci - 1];
                        out.gap = (near && near.type === 'literal') ? near.value : '';
                    }
                }
            } catch (e) { }
        }
        layoutCache[key] = out;
        return out;
    }
    V.formatAmountByBrowser = function (value, isoCode, precision, options) {
        options = options || {};
        var num = Number(value) || 0;
        var p = resolvePrecision(precision);
        var iso = validIso(isoCode);
        var body;
        if (options.compact && window.VIS && VIS.Util && typeof VIS.Util.formatCompactAmount === 'function') {
            body = VIS.Util.formatCompactAmount(Math.abs(num), iso, p);
        } else {
            var f = numberFormat({ minimumFractionDigits: p, maximumFractionDigits: p });
            try { body = f ? f.format(Math.abs(num)) : Math.abs(num).toFixed(p); } catch (e) { body = String(Math.abs(num)); }
        }
        var minus = (num < 0 && /[1-9]/.test(body)) ? '-' : '';
        var lay = currencyLayout(iso);
        var sign = lay.sign || options.symbol || iso;
        if (!sign) return minus + body;
        if (!lay.sign) return minus + sign + ' ' + body;
        return lay.before ? (minus + sign + lay.gap + body) : (minus + body + lay.gap + sign);
    };
})();
