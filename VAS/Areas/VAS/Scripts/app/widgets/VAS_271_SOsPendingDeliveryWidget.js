/**
 * VAS_271 SOs Pending Delivery Widget (Sales Order dashboard, 3x1 KPI split tile)
 * Purpose - All-time operational backlog: every Sales Order (DocStatus 'CO',
 *           DateOrdered up to today) that still has at least one active line with
 *           QtyOrdered > QtyDelivered. Deliberately ignores month boundaries - a
 *           March order still awaiting delivery in August is still pending. Click /
 *           Enter / Space opens a drill-down modal with a 4-card stat strip and a
 *           paginated table whose stock column is computed per order (not an
 *           allocation-aware simulation): every line's pending quantity compared
 *           against free stock at that order's ship-from warehouse, independently
 *           per order. SO number opens a record-preview child modal and the lines
 *           icon opens a lines-only child modal (both fed by one
 *           GetSalesOrderDetail fetch).
 *
 * Design  - 04-kpi-pending-delivery.html / .md: glass 3x1 KPI tile with a split
 *           layout (headline + meta on the left, a secondary "items pending
 *           delivery" figure on the right), "warn" (needs attention, not yet a
 *           failure) tone and border tint. Modal shell, stat tiles, chips and
 *           paginated table match the same design language as VAS_270's shared
 *           Sales Order record-preview modal - reused here as this widget's own
 *           self-contained copy, matching how every widget controller/JS pair in
 *           this codebase owns its own modal implementation.
 *
 * Modal   - Documents (top-level) -> Lines (child, back-navigable; opened by the
 *           lines icon - the SO number opens the record on the Sales Order screen in
 *           the same tab, 5-Nov Row 41), same
 *           { title, subtitle, size, render } config-stack engine as VAS_270 -
 *           "back" pops the stack and re-runs the popped config's render() from its
 *           own already-fetched state, never stale HTML.
 *
 * Backend - VAS_271_SOsPendingDeliveryWidget/GetSummary            (GET -> tile + stat-strip figures)
 *           VAS_271_SOsPendingDeliveryWidget/GetOrders             (GET page,size -> paginated backlog, stock-aware)
 *           VAS_271_SOsPendingDeliveryWidget/GetSalesOrderDetail   (GET id -> order + lines)
 *
 * ── Labels / Message Keys ─────────────────────────────────────────────────
 *  #  | Current Text                                                          | Message Key
 * ----+------------------------------------------------------------------------+--------------------------------
 *  1  | SOs Pending Delivery                                                  | VAS_271_Title
 *  2  | All SOs till date that are not fully delivered                       | VAS_271_Subtitle
 *  3  | Open till date                                                        | VAS_271_OpenTillDateLabel
 *  4  | undelivered                                                           | VAS_271_UndeliveredSuffix
 *  5  | past due                                                              | VAS_271_PastDueSuffix
 *  6  | items pending                                                         | VAS_271_ItemsPendingLabel1
 *  7  | delivery                                                              | VAS_271_ItemsPendingLabel2
 *  8  | Nothing pending — every order is delivered                           | VAS_271_ZeroState
 *  9  | Figures unavailable                                                   | VAS_271_ErrorState
 * 10  | Open SOs                                                              | VAS_271_StatOpenSOs
 * 11  | Items pending                                                         | VAS_271_StatItemsPending
 * 12  | Undelivered value                                                     | VAS_271_StatUndeliveredValue
 * 13  | Past due                                                              | VAS_271_StatPastDue
 * 14  | Stock column compares the pending quantity of every line against free stock at the ship-from warehouse. | VAS_271_StockNote
 * 15  | Stock coverage could not be evaluated.                                | VAS_271_StockUnavailableNote
 * 16  | Sales orders awaiting delivery                                        | VAS_271_SectionHeading
 * 17  | earliest promised first                                               | VAS_271_EarliestPromisedFirst
 * 18  | SO No                                                                 | VAS_271_ColSoNo
 * 19  | SO date                                                               | VAS_271_SoDate
 * 20  | Customer                                                              | VAS_271_Customer
 * 21  | Ship from                                                             | VAS_271_ShipFrom
 * 22  | Ordered                                                               | VAS_271_ColOrdered
 * 23  | Pending items                                                         | VAS_271_ColPendingItems
 * 24  | Pending value                                                         | VAS_271_ColPendingValue
 * 25  | Promised                                                              | VAS_271_ColPromised
 * 26  | Stock                                                                 | VAS_271_ColStock
 * 27  | Delivery                                                              | VAS_271_ColDelivery
 * 28  | Coverable                                                             | VAS_271_StockCoverable
 * 29  | Short                                                                 | VAS_271_StockShortPrefix
 * 30  | Pending                                                                | VAS_271_DeliveryPendingChip
 * 31  | Back                                                                  | VAS_271_Back
 * 32  | Close                                                                 | VAS_271_Close
 * 33  | Date promised                                                         | VAS_271_DatePromised
 * 34  | SO value                                                              | VAS_271_SoValue
 * 35  | Delivery mode                                                         | VAS_271_DeliveryMode
 * 36  | Document status                                                       | VAS_271_DocumentStatus
 * 37  | Delivery status                                                       | VAS_271_DeliveryStatus
 * 38  | Sales order lines                                                     | VAS_271_SalesOrderLines
 * 39  | Line                                                                  | VAS_271_ColLine
 * 40  | Product                                                               | VAS_271_ColProduct
 * 41  | Attribute                                                             | VAS_271_ColAttribute
 * 42  | UoM                                                                   | VAS_271_ColUom
 * 43  | Delivered                                                             | VAS_271_ColDelivered
 * 44  | Pending                                                               | VAS_271_ColPending
 * 45  | In stock                                                              | VAS_271_ColInStock
 * 46  | Rate                                                                  | VAS_271_ColRate
 * 47  | Amount                                                                | VAS_271_ColAmount
 * 48  | Line status                                                           | VAS_271_ColLineStatus
 * 49  | Delivered                                                             | VAS_271_DeliveryFull
 * 50  | Partially delivered                                                   | VAS_271_DeliveryPartial
 * 51  | Not delivered                                                         | VAS_271_DeliveryNone
 * 52  | Partly delivered                                                      | VAS_271_LinePartial
 * 53  | In process                                                            | VAS_271_LineInProcess
 * 54  | Sales order                                                           | VAS_271_SalesOrderPrefix
 * 55  | lines                                                                 | VAS_271_LinesSuffix
 * 56  | qty ordered                                                           | VAS_271_QtyOrderedSuffix
 * 57  | qty short of stock                                                    | VAS_271_QtyShortSuffix
 * 58  | Previous page                                                         | VAS_271_PrevPage
 * 59  | Next page                                                             | VAS_271_NextPage
 * 60  | of                                                                    | VAS_271_Of
 * 61  | Showing                                                               | VAS_271_Showing
 * 62  | Search is unavailable right now. Try again in a moment.               | VAS_271_LoadError
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
                try { text = VIS.Msg.getMsg('VAS_SO_Retry'); } catch (e) { text = ''; }
                if (!text || text === 'VAS_SO_Retry' || text === '[VAS_SO_Retry]') { text = 'Retry'; }
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
    var ZOOM_WINDOW_NAME = 'VAS_SalesOrder';
    var MONTHS = ['Jan', 'Feb', 'Mar', 'Apr', 'May', 'Jun', 'Jul', 'Aug', 'Sep', 'Oct', 'Nov', 'Dec'];

    /* design.md §Widget Header / §Measurement Setup: keep --dash-inline-size on
       :root equal to the dashboard container's current pixel width so the widget
       clamp resolves against the dashboard's visible width, not the viewport. A
       single document-level ResizeObserver serves every widget (matches VAS_269/270). */
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

    VAS.VAS_271_SOsPendingDeliveryWidget = function () {

        this.frame;
        this.windowNo;

        var $self = this;
        var $root = $('<div class="vas271-root">');
        var $tile;
        var $mask, $modal, $mHead, $mTitle, $mSub, $mBack, $mBody, $mFoot, $mClose;

        var SELF_ENDPOINT = 'VAS_271_SOsPendingDeliveryWidget/';
        var zoomWindowId = 0;
        // Per-instance event-namespace suffix (Sr 8.28): disposing an old instance must
        // not unbind the window handlers of a re-created one with the same widget id.
        var instanceTag = 'i' + Math.random().toString(36).slice(2, 9);

        var summary = null;           // last fetched GetSummary payload
        var currency = { iso: '', symbol: '', precision: null }; // VAS163: base currency from GetSummary
        var summaryState = 'loading'; // 'loading' | 'ready' | 'error'
        var docsState = { page: 0, size: MAX_ROWS_PER_PAGE, total: 0, rows: [], stockUnavailable: false };
        var lineState = null;         // { order, lines, page, size, tableId }
        var cfgStack = [];
        var currentCfg = null;

        // A missing message key must never render raw ("[KEY]") - fall back to the English text.
        function label(key, fallback) {
            var text = '';
            try { text = VIS.Msg.getMsg(key); } catch (e) { text = ''; }
            if (!text || text === key || text === '[' + key + ']') { return fallback || key; }
            return text;
        }

        // A table of THIS instance's modal, looked up inside its own body - never via
        // document.getElementById: a modal left on <body> by an earlier instance (one the
        // host re-created on "refresh all" without disposing) carries the same ids and
        // would be found first, so the new modal stayed empty.
        function tableEl(id) {
            return $mBody ? $mBody.find('#' + id)[0] : null;
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
            if (name === 'back') {
                return '<svg width="20" height="20" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2" stroke-linecap="round" stroke-linejoin="round"><path d="M19 12H5M12 19l-7-7 7-7"/></svg>';
            }
            if (name === 'prev') {
                return '<svg viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2.4" stroke-linecap="round"><polyline points="15 18 9 12 15 6"/></svg>';
            }
            if (name === 'next') {
                return '<svg viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2.4" stroke-linecap="round"><polyline points="9 18 15 12 9 6"/></svg>';
            }
            if (name === 'lines') {
                return '<svg viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2" stroke-linecap="round"><path d="M8 6h13M8 12h13M8 18h13M3 6h.01M3 12h.01M3 18h.01"/></svg>';
            }
            return '';
        }

        // Indian-numbering display convention (₹ x.xx Cr / ₹ x.xx L / grouped
        // integer), matching every other widget on this dashboard (VAS_268/270 fmtINR).
        // VAS163 2026-11-10: browser locale (6-Nov retest2).
        // Base (accounting-schema) currency sent by the server with the summary payload.
        function applyCurrency(data) {
            if (!data) { return; }
            currency.iso = data.CurrencyIso || '';
            currency.symbol = data.CurrencySymbol || '';
            currency.precision = (typeof data.CurrencyPrecision === 'number') ? data.CurrencyPrecision : null;
        }

        // VAS163 2026-11-10: browser locale (6-Nov retest2).
        function formatINR(value) {
            var num = Number(value || 0);
            var sym = currency.symbol || currency.iso || '';
            var text = (num < 0 ? '-' : '') + VIS.Util.formatCompactAmount(Math.abs(num), currency.iso, currency.precision);
            return sym ? sym + ' ' + text : text;
        }

        // VAS163 2026-11-11: Row 26 compact amount in a document's own currency (falls back to base).
        function formatDocAmount(value, iso, symbol, precision) {
            if (!iso && !symbol) { return formatINR(value); }
            var num = Number(value || 0);
            var sym = symbol || iso || '';
            var text = (num < 0 ? '-' : '') + VIS.Util.formatCompactAmount(Math.abs(num), iso, precision);
            return sym ? sym + ' ' + text : text;
        }

        // VAS163 2026-11-10: browser locale (6-Nov retest2).
        function formatNum(value) {
            return Math.round(Number(value || 0)).toLocaleString(window.navigator.language);
        }

        // Amounts in the order's (price list's) own currency and precision.
        // VAS163 2026-11-10: browser locale (6-Nov retest2).
        function formatMoney(value, symbol, precision) {
            var prec = (typeof precision === 'number' && precision >= 0) ? precision : 2;
            var text = Number(value || 0).toLocaleString(window.navigator.language, { minimumFractionDigits: prec, maximumFractionDigits: prec });
            return symbol ? symbol + ' ' + text : text;
        }

        // Sr 8.13: quantities are in the selected UOM and can be fractional.
        // VAS163 2026-11-10: browser locale (6-Nov retest2).
        function formatQty(value) {
            return Number(value || 0).toLocaleString(window.navigator.language, { maximumFractionDigits: 2 });
        }

        // Sr 8.17: a description made only of blanks / separators is not an attribute value (VAS_203 rule).
        function hasAttributeValue(text) {
            return !!text && /[^\s\-‐-―_.,:;()\[\]\/\\|]/.test(String(text));
        }

        // VAS163 2026-11-10: browser locale (6-Nov retest2).
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

        function parseResponse(res) {
            var data = res;
            if (typeof data === 'string') { data = JSON.parse(data); }
            if (typeof data === 'string') { data = JSON.parse(data); }
            return data || {};
        }

        function cellHtml(value, cls, align) {
            return '<span class="vas271-cell' + (align === 'right' ? ' right' : '') + ' ' + cls + '" title="' + escapeHtml(value) + '">' + escapeHtml(value) + '</span>';
        }

        function statTile(l, v) {
            return '<div class="vas271-mstat"><div class="l">' + escapeHtml(l) + '</div><div class="v" title="' + escapeHtml(v) + '">' + escapeHtml(v) + '</div></div>';
        }

        /* ============================================================
         * Tile
         * ============================================================ */
        function createWidget() {
            $tile = $(
                '<button type="button" class="vas271-tile" aria-label="' + escapeHtml(label('VAS_271_Title', 'SOs Pending Delivery')) + '">' +
                    '<p class="vas271-title">' + escapeHtml(label('VAS_271_Title', 'SOs Pending Delivery')) + '</p>' +
                    '<div class="vas271-row">' +
                        '<div class="vas271-left">' +
                            '<p class="vas271-val"><span class="vas271-skel"></span></p>' +
                            '<p class="vas271-meta"></p>' +
                        '</div>' +
                        '<div class="vas271-side">' +
                            '<div class="v"><span class="vas271-skel"></span></div>' +
                            '<div class="l"></div>' +
                        '</div>' +
                    '</div>' +
                '</button>'
            );
            $root.append($tile);
            bindTile();
        }

        /* Sr 8.28: (re)binds the tile click. Run on init AND on every refresh - after a
           "refresh all" from the mosaic panel the host can strip jQuery handlers from
           the widget's DOM (re-attaching the same nodes), which left a tile that
           rendered fine but opened nothing. off() first keeps it to one handler. */
        function bindTile() {
            if (!$tile) { return; }
            $tile.off('click.vas271').on('click.vas271', function () { openDocumentsModal(); });
        }

        function sideLabelHtml() {
            return escapeHtml(label('VAS_271_ItemsPendingLabel1', 'items pending')) + '<br/>' + escapeHtml(label('VAS_271_ItemsPendingLabel2', 'delivery'));
        }

        function renderTile() {
            var $val = $tile.find('.vas271-val');
            var $meta = $tile.find('.vas271-meta');
            var $sideV = $tile.find('.vas271-side .v');
            var $sideL = $tile.find('.vas271-side .l');

            $sideL.html(sideLabelHtml());

            if (summaryState === 'loading') {
                $val.html('<span class="vas271-skel"></span>');
                $meta.text('');
                $sideV.html('<span class="vas271-skel"></span>');
                $tile.removeAttr('aria-disabled');
                return;
            }
            if (summaryState === 'error' || !summary) {
                $val.removeClass().addClass('vas271-val error').text('—');
                $meta.text(label('VAS_271_ErrorState', 'Figures unavailable'));
                VASSONet.retry($root, $self);
                $sideV.text('—');
                $tile.attr('aria-disabled', 'true');
                return;
            }

            $tile.removeAttr('aria-disabled');
            $val.removeClass().addClass('vas271-val warn').text(formatNum(summary.OpenCount));
            $sideV.text(formatNum(summary.PendingItems));

            if (summary.OpenCount <= 0) {
                $meta.text(label('VAS_271_ZeroState', 'Nothing pending — every order is delivered'));
                return;
            }

            var meta = label('VAS_271_OpenTillDateLabel', 'Open till date') + ' · ' +
                formatINR(summary.UndeliveredValue) + ' ' + label('VAS_271_UndeliveredSuffix', 'undelivered') +
                ' · ' + formatNum(summary.PastDueCount) + ' ' + label('VAS_271_PastDueSuffix', 'past due');
            $meta.attr('title', meta).text(meta);
        }

        function loadSummary(done) {
            summaryState = 'loading';
            renderTile();

            VASSONet.ajax({
                url: VIS.Application.contextUrl + SELF_ENDPOINT + 'GetSummary',
                type: 'GET', dataType: 'json', cache: false,
                success: function (res) {
                    var parsed = parseResponse(res);
                    if (parsed.Error) { summaryState = 'error'; summary = null; }
                    else { summaryState = 'ready'; summary = parsed; applyCurrency(parsed); }
                    renderTile();
                    if (done) { done(!!summary); }
                },
                error: function () {
                    summaryState = 'error'; summary = null;
                    renderTile();
                    if (done) { done(false); }
                }
            });
        }

        /* ============================================================
         * Modal shell - lives on <body>, not inside $root, so the widget
         * cell's own overflow cannot clip it. Every screen is a
         * { title, subtitle, size, render } config; showScreen() pushes the
         * outgoing config onto cfgStack and paints the new one; backModal()
         * pops and re-renders the popped config from its own live state
         * (docsState / lineState) - never a frozen HTML snapshot.
         * ============================================================ */
        function createModal() {
            $mask = $('<div class="vas271-mask" role="dialog" aria-modal="true"></div>');
            $modal = $('<div class="vas271-modal"></div>');
            $mHead = $('<div class="vas271-mhead"></div>');
            var $htxt = $('<div class="vas271-htxt"></div>');
            $mBack = $('<button type="button" class="vas271-xbtn" aria-label="' + escapeHtml(label('VAS_271_Back', 'Back')) + '" hidden>' + icon('back') + '</button>');
            var $titleWrap = $('<div></div>');
            $mTitle = $('<h2></h2>');
            $mSub = $('<div class="vas271-msub"></div>');
            $titleWrap.append($mTitle, $mSub);
            $htxt.append($mBack, $titleWrap);
            $mClose = $('<button type="button" class="vas271-xbtn" aria-label="' + escapeHtml(label('VAS_271_Close', 'Close')) + '">' + icon('close') + '</button>');
            $mHead.append($htxt, $mClose);

            $mBody = $('<div class="vas271-mbody"></div>');
            $mFoot = $('<div class="vas271-mfoot"></div>');

            $modal.append($mHead, $mBody, $mFoot);
            $mask.append($modal);
            $('body').append($mask);

            bindModal();
        }

        /* 5-Nov Row 24: the modal's own handlers are (re)bound here under one namespace,
           off() first, on init AND on every refresh - like the tile, they must survive a
           host that strips jQuery handlers on "refresh all". */
        function bindModal() {
            if (!$mBody) { return; }
            var ns = '.vas271m-' + instanceTag;
            $mClose.off(ns).on('click' + ns, closeModal);
            $mBack.off(ns).on('click' + ns, backModal);
            $mBody.off(ns).on('click' + ns, function (event) {
                var pageBtn = event.target.closest ? event.target.closest('[data-dir]') : null;
                if (pageBtn) { turnPage(pageBtn.getAttribute('data-table'), Number(pageBtn.getAttribute('data-dir'))); return; }
                // VAS163 2026-11-11: Row 24 Back / Close buttons on the lines table's pager row.
                var actBtn = event.target.closest ? event.target.closest('[data-mact]') : null;
                if (actBtn) { if (actBtn.getAttribute('data-mact') === 'back') { backModal(); } else { closeModal(); } return; }
                // 5-Nov Row 41: the document number opens that record on the Sales Order
                // screen (same tab, VAS_268's Open Record pattern); the Line Details icon
                // opens the "Sales order lines" pop-up.
                var soBtn = event.target.closest ? event.target.closest('[data-so]') : null;
                if (soBtn) { openRecordInWindow(Number(soBtn.getAttribute('data-so'))); return; }
                var linesBtn = event.target.closest ? event.target.closest('[data-lines]') : null;
                if (linesBtn) { openLinesModal(Number(linesBtn.getAttribute('data-lines'))); return; }
            });
        }

        /* 5-Nov Row 41 - navigates to the Sales Order record in the SAME tab (VAS_268
           "Open Record"): hosted inside the Sales Order window (windowNo >= 0) the host's
           value-changed channel re-queries the window it is already in (TabWhereClause);
           only on the Home page, where there is no host window, the shared zoom helper
           opens the Sales Order window. */
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
            VAS.ZoomUtil.zoomToRecord('C_Order_ID', orderId, zoomWindowId, ZOOM_WINDOW_NAME, ZOOM_WINDOW_NAME)
                .done(function (id) { if (id > 0) { zoomWindowId = id; } });
        }

        function bindDocumentLevelEvents() {
            var ns = '.vas271-' + ($self.AD_UserHomeWidgetID || $self.windowNo || 'widget') + '-' + instanceTag;

            $(window).on('resize' + ns, function () {
                if ($mask.hasClass('is-open')) { fitAllTables(); }
            });
        }

        function paintChrome(cfg) {
            $mBack.prop('hidden', !cfgStack.length);
            $modal.removeClass('sm md').addClass(cfg.size || '');
            $mTitle.text(cfg.title || '');
            $mSub.text(cfg.subtitle || '');
        }

        function showScreen(cfg, push) {
            if (push && currentCfg) { cfgStack.push(currentCfg); }
            else if (!push) { cfgStack = []; }
            currentCfg = cfg;
            paintChrome(cfg);
            cfg.render();
            $mask.addClass('is-open');
            requestAnimationFrame(function () { fitAllTables(); requestAnimationFrame(fitAllTables); });
        }

        function backModal() {
            var prev = cfgStack.pop();
            if (!prev) { closeModal(); return; }
            currentCfg = prev;
            paintChrome(prev);
            prev.render();
            requestAnimationFrame(function () { fitAllTables(); requestAnimationFrame(fitAllTables); });
        }

        function closeModal() {
            $mask.removeClass('is-open');
            cfgStack = [];
            currentCfg = null;
            lineState = null;
        }

        function showLoading(title) {
            $mBack.prop('hidden', !cfgStack.length);
            $mTitle.text(title || '');
            $mSub.text('');
            $mBody.html('<div class="vas271-mstate">…</div>');
            $mFoot.html('');
            $mask.addClass('is-open');
        }

        function showLoadError() {
            $mBody.html('<div class="vas271-mstate">' + escapeHtml(label('VAS_271_LoadError', 'Search is unavailable right now. Try again in a moment.')) + '</div>');
        }

        /* ============================================================
         * Documents list (top-level modal)
         * ============================================================ */
        function buildDocsCfg() {
            return { title: label('VAS_271_Title', 'SOs Pending Delivery'), subtitle: label('VAS_271_Subtitle', 'All SOs till date that are not fully delivered'), size: '', render: renderDocumentsBody };
        }

        function openDocumentsModal() {
            // Sr 8.28: never swallow the click - when the tile's last load failed
            // (e.g. the request burst of a "refresh all"), the modal opens and loads
            // the figures again instead of doing nothing.
            showLoading(label('VAS_271_Title', 'SOs Pending Delivery'));

            function loadAndShow() {
                docsState.page = 0;
                fetchOrders(0, docsState.size, function (ok) {
                    if (!ok) { showLoadError(); return; }
                    showScreen(buildDocsCfg(), false);
                });
            }

            if (summary) { loadAndShow(); }
            else { loadSummary(function (ok) { if (ok) { loadAndShow(); } else { showLoadError(); } }); }
        }

        function fetchOrders(page, size, cb) {
            VASSONet.ajax({
                url: VIS.Application.contextUrl + SELF_ENDPOINT + 'GetOrders',
                type: 'GET', dataType: 'json', cache: false,
                data: { page: page, size: size },
                success: function (res) {
                    var parsed = parseResponse(res);
                    if (parsed.Error) { cb(false); return; }
                    docsState.page = page;
                    docsState.size = size;
                    docsState.total = Number(parsed.Total || 0);
                    docsState.stockUnavailable = !!parsed.StockUnavailable;
                    docsState.rows = (parsed.Rows || []).map(normalizeOrderRow);
                    cb(true);
                },
                error: function () { cb(false); }
            });
        }

        function normalizeOrderRow(row) {
            return {
                SalesOrderId: Number(row.SalesOrderId) || 0,
                SalesOrderNumber: row.SalesOrderNumber || '',
                SalesOrderDate: row.SalesOrderDate || '',
                PromisedDate: row.PromisedDate || '',
                CustomerName: row.CustomerName || '',
                WarehouseName: row.WarehouseName || '',
                OrderedQty: Number(row.OrderedQty) || 0,
                PendingQty: Number(row.PendingQty) || 0,
                PendingValue: Number(row.PendingValue) || 0,
                // VAS163 2026-11-11: Row 26 document (price list) currency of the Pending value column.
                CurrencyIso: row.CurrencyIso || '',
                CurrencySymbol: row.CurrencySymbol || '',
                CurrencyPrecision: (typeof row.CurrencyPrecision === 'number') ? row.CurrencyPrecision : null,
                ShortfallQty: (row.ShortfallQty === null || row.ShortfallQty === undefined) ? null : Number(row.ShortfallQty),
                DeliveryStatus: row.DeliveryStatus || '',
                // VAS163 2026-11-11: Row 25 Partial (ordered > pending) vs Pending (ordered == pending).
                IsPartial: !!row.IsPartial
            };
        }

        // VAS163 2026-11-11: Row 21 the Stock column (stockCellHtml) is removed from the SO list;
        // the line pop-up keeps its In stock column.

        function renderDocumentsBody() {
            var statsHtml = '<div class="vas271-mstats">' +
                statTile(label('VAS_271_StatOpenSOs', 'Open SOs'), formatNum(summary.OpenCount)) +
                statTile(label('VAS_271_StatItemsPending', 'Items pending'), formatNum(summary.PendingItems)) +
                statTile(label('VAS_271_StatUndeliveredValue', 'Undelivered value'), formatINR(summary.UndeliveredValue)) +
                statTile(label('VAS_271_StatPastDue', 'Past due'), formatNum(summary.PastDueCount)) +
            '</div>';

            // VAS163 2026-11-11: Row 19 the stock note under the summary cards is removed (the
            // Stock column it described is gone too - Row 21).
            var noteHtml = '';

            var secHtml = '<div class="vas271-msec">' + escapeHtml(label('VAS_271_SectionHeading', 'Sales orders awaiting delivery')) + '</div>';

            if (docsState.total <= 0) {
                $mBody.html(statsHtml + noteHtml + secHtml + '<div class="vas271-mstate">' + escapeHtml(label('VAS_271_ZeroState', 'Nothing pending — every order is delivered')) + '</div>');
            } else {
                $mBody.html(statsHtml + noteHtml + secHtml + '<div class="vas271-mtwrap"><div class="vas271-mtbl" id="vas271-docstbl"></div></div>');
                drawDocumentsTable();
            }
            // VAS163 2026-11-11: Row 23 no Close footer bar on the SO list (the X icon stays); empty footer is hidden by CSS.
            $mFoot.html('');
        }

        var DOC_COLS = [
            { key: 'icon', label: '', w: .32 },
            { key: 'so', label: label('VAS_271_ColSoNo', 'SO No'), w: 1.05 },
            { key: 'date', label: label('VAS_271_SoDate', 'SO date'), w: .85 },
            { key: 'customer', label: label('VAS_271_Customer', 'Customer'), w: 1.5 },
            { key: 'warehouse', label: label('VAS_271_ShipFrom', 'Ship from'), w: 1.1 },
            { key: 'ordered', label: label('VAS_271_ColOrdered', 'Ordered'), w: .75, align: 'right' },
            { key: 'pendingqty', label: label('VAS_271_ColPendingItems', 'Pending items'), w: .9, align: 'right' },
            { key: 'pendingvalue', label: label('VAS_271_ColPendingValue', 'Pending value'), w: .95, align: 'right' },
            { key: 'promised', label: label('VAS_271_ColPromised', 'Promised'), w: .85 },
            // VAS163 2026-11-11: Row 21 Stock column removed from the SO list.
            { key: 'delivery', label: label('VAS_271_ColDelivery', 'Delivery'), w: 1 }
        ];

        function drawDocumentsTable() {
            var el = tableEl('vas271-docstbl');
            if (!el) { return; }
            var tpl = DOC_COLS.map(function (c) { return 'minmax(0,' + c.w + 'fr)'; }).join(' ');
            var pages = Math.max(1, Math.ceil(docsState.total / docsState.size));

            var head = '<div class="vas271-mrow vas271-mhead-row" style="grid-template-columns:' + tpl + '">' +
                DOC_COLS.map(function (c) { return '<span class="vas271-cell' + (c.align === 'right' ? ' right' : '') + '" title="' + escapeHtml(c.label) + '">' + escapeHtml(c.label) + '</span>'; }).join('') +
            '</div>';

            var body = docsState.rows.map(function (row) {
                var cells = [
                    '<span class="vas271-cell center"><button type="button" class="vas271-iconbtn" data-lines="' + row.SalesOrderId + '" title="' + escapeHtml(row.SalesOrderNumber) + '">' + icon('lines') + '</button></span>',
                    '<span class="vas271-cell"><button type="button" class="vas271-lnk" data-so="' + row.SalesOrderId + '" title="' + escapeHtml(row.SalesOrderNumber) + '">' + escapeHtml(row.SalesOrderNumber) + '</button></span>',
                    cellHtml(formatDateFull(row.SalesOrderDate), 'vas271-c-std'),
                    cellHtml(row.CustomerName, 'vas271-c-prim'),
                    cellHtml(row.WarehouseName, 'vas271-c-std'),
                    cellHtml(formatQty(row.OrderedQty), 'vas271-c-std', 'right'),
                    cellHtml(formatQty(row.PendingQty), 'vas271-c-prim', 'right'),
                    // VAS163 2026-11-11: Row 26 Pending value in the order's own (price list) currency.
                    cellHtml(formatDocAmount(row.PendingValue, row.CurrencyIso, row.CurrencySymbol, row.CurrencyPrecision), 'vas271-c-emph', 'right'),
                    cellHtml(row.PromisedDate ? formatDateFull(row.PromisedDate) : '—', 'vas271-c-std'),
                    // VAS163 2026-11-11: Row 21 no Stock cell; Row 25 Partial chip in the warning tone.
                    '<span class="vas271-cell" title="' + escapeHtml(row.DeliveryStatus) + '"><span class="vas271-chip ' + (row.IsPartial ? 'vas271-chip-warn' : 'vas271-chip-neutral') + '">' + escapeHtml(row.DeliveryStatus) + '</span></span>'
                ];
                return '<div class="vas271-mrow" style="grid-template-columns:' + tpl + '">' + cells.join('') + '</div>';
            }).join('');

            var start = docsState.page * docsState.size;
            var showingLabel = label('VAS_271_Showing', 'Showing') + ' ' + (docsState.total ? (start + 1) : 0) + '–' + (start + docsState.rows.length) + ' ' + label('VAS_271_Of', 'of') + ' ' + docsState.total + ' · ' + label('VAS_271_EarliestPromisedFirst', 'earliest promised first');

            var foot = '<div class="vas271-mtfoot"><span class="vas271-helper">' + escapeHtml(showingLabel) + '</span>' +
                (pages > 1 ?
                    '<span class="vas271-pager">' +
                        '<button type="button" class="vas271-pbtn" data-table="docs" data-dir="-1"' + (docsState.page === 0 ? ' disabled' : '') + ' aria-label="' + escapeHtml(label('VAS_271_PrevPage', 'Previous page')) + '">' + icon('prev') + '</button>' +
                        '<span class="vas271-ptxt">' + (docsState.page + 1) + ' ' + escapeHtml(label('VAS_271_Of', 'of')) + ' ' + pages + '</span>' +
                        '<button type="button" class="vas271-pbtn" data-table="docs" data-dir="1"' + (docsState.page >= pages - 1 ? ' disabled' : '') + ' aria-label="' + escapeHtml(label('VAS_271_NextPage', 'Next page')) + '">' + icon('next') + '</button>' +
                    '</span>' : '<span></span>') +
            '</div>';

            el.innerHTML = head + '<div class="vas271-mbody-rows">' + body + '</div>' + foot;
        }

        function turnPage(table, dir) {
            if (table === 'docs') {
                var pages = Math.max(1, Math.ceil(docsState.total / docsState.size));
                var next = Math.min(pages - 1, Math.max(0, docsState.page + dir));
                if (next === docsState.page) { return; }
                fetchOrders(next, docsState.size, function (ok) { if (ok) { drawDocumentsTable(); } });
                return;
            }
            if (table === 'lines' && lineState) {
                var linePages = Math.max(1, Math.ceil(lineState.lines.length / lineState.size));
                lineState.page = Math.min(linePages - 1, Math.max(0, lineState.page + dir));
                drawLineTable(lineState.tableId);
            }
        }

        /* ============================================================
         * Record / lines child modals - both render from one
         * GetSalesOrderDetail fetch (order header + full line list).
         * ============================================================ */
        function lineStatus(line) {
            if (line.QtyDelivered >= line.QtyOrdered && line.QtyOrdered > 0) { return { text: label('VAS_271_DeliveryFull', 'Delivered'), cls: 'vas271-chip-info' }; }
            if (line.QtyDelivered > 0) { return { text: label('VAS_271_LinePartial', 'Partly delivered'), cls: 'vas271-chip-warn' }; }
            return { text: label('VAS_271_LineInProcess', 'In process'), cls: 'vas271-chip-prop' };
        }

        function fetchOrderDetail(orderId, cb) {
            VASSONet.ajax({
                url: VIS.Application.contextUrl + SELF_ENDPOINT + 'GetSalesOrderDetail',
                type: 'GET', dataType: 'json', cache: false,
                data: { id: orderId },
                success: function (res) {
                    var parsed = parseResponse(res);
                    var order = parsed && parsed.Order;
                    if (parsed.Error || !order || !order.SalesOrderId) { cb(null); return; }
                    cb({ order: order, lines: parsed.Lines || [] });
                },
                error: function () { cb(null); }
            });
        }

        /* The Line Details icon opens this "Sales order lines" pop-up (the document
           number navigates to the record instead - 5-Nov Row 41).
           VAS163 2026-11-11: Row 24 the footer summary text is removed; Back / Close sit on
           the table's "Showing x-y of n" / pager row (see drawLineTable). */
        function lineActionsHtml() {
            return '<span class="vas271-mtfoot-actions">' +
                '<button type="button" class="vas271-btn" data-mact="back">' + escapeHtml(label('VAS_271_Back', 'Back')) + '</button>' +
                '<button type="button" class="vas271-btn" data-mact="close">' + escapeHtml(label('VAS_271_Close', 'Close')) + '</button></span>';
        }

        function openLinesModal(orderId) {
            showLoading(label('VAS_271_SalesOrderLines', 'Sales order lines') + '…');
            fetchOrderDetail(orderId, function (detail) {
                if (!detail) { showLoadError(); return; }
                showScreen(buildLinesCfg(detail.order, detail.lines), true);
            });
        }

        function buildLinesCfg(order, lines) {
            return {
                title: label('VAS_271_SalesOrderLines', 'Sales order lines') + ' · ' + order.SalesOrderNumber,
                subtitle: [order.CustomerName, formatDateShort(order.SalesOrderDate), order.DeliveryStatus].filter(function (p) { return p; }).join(' · '),
                size: '',   // Sr 8.18: same modal size as the documents list
                render: function () { renderLinesBody(order, lines); }
            };
        }

        function renderLinesBody(order, lines) {
            // "Ordered" / "Pending" cards: Item-type product lines only (Sr 8.15).
            var qtyOrdered = 0, qtyPending = 0;
            lines.forEach(function (l) {
                if (!l.IsItem) { return; }
                qtyOrdered += Number(l.QtyOrdered) || 0;
                qtyPending += Number(l.QtyPending) || 0;
            });

            var breadcrumb = '<div class="vas271-polink">' + escapeHtml(label('VAS_271_SalesOrderPrefix', 'Sales order')) +
                ' <strong>' + escapeHtml(order.SalesOrderNumber) + '</strong>' +
                ' · ' + escapeHtml(formatDateFull(order.SalesOrderDate)) + ' · ' + escapeHtml(order.DocumentStatus) + '</div>';

            var stats = '<div class="vas271-mstats">' +
                statTile(label('VAS_271_ColLine', 'Line') + 's', String(lines.length)) +
                statTile(label('VAS_271_SoValue', 'SO value'), formatMoney(order.OrderValue, order.CurrencySymbol, order.CurrencyPrecision)) +
                statTile(label('VAS_271_ColOrdered', 'Ordered'), formatQty(qtyOrdered)) +
                statTile(label('VAS_271_ColPending', 'Pending'), formatQty(qtyPending)) +
            '</div>';

            var secHtml = '<div class="vas271-msec">' + escapeHtml(label('VAS_271_SalesOrderLines', 'Sales order lines')) + '</div>';
            $mBody.html(breadcrumb + stats + secHtml + '<div class="vas271-mtwrap"><div class="vas271-mtbl" id="vas271-linetbl-lines"></div></div>');

            var showAttribute = lines.some(function (l) { return hasAttributeValue(l.AttributeText); });
            lineState = { order: order, lines: lines, page: 0, size: MAX_ROWS_PER_PAGE, tableId: 'vas271-linetbl-lines', showAttribute: showAttribute };
            drawLineTable('vas271-linetbl-lines');

            // VAS163 2026-11-11: Row 24 no separate footer bar (buttons are on the pager row).
            $mFoot.html('');
        }

        var LINE_COLS = [
            { key: 'no', label: label('VAS_271_ColLine', 'Line'), w: .35, align: 'right' },
            { key: 'product', label: label('VAS_271_ColProduct', 'Product'), w: 1.5 },
            { key: 'attr', label: label('VAS_271_ColAttribute', 'Attribute'), w: 1.1 },
            { key: 'uom', label: label('VAS_271_ColUom', 'UoM'), w: .5 },
            { key: 'ordered', label: label('VAS_271_ColOrdered', 'Ordered'), w: .7, align: 'right' },
            { key: 'delivered', label: label('VAS_271_ColDelivered', 'Delivered'), w: .7, align: 'right' },
            { key: 'pending', label: label('VAS_271_ColPending', 'Pending'), w: .7, align: 'right' },
            { key: 'instock', label: label('VAS_271_ColInStock', 'In stock'), w: .7, align: 'right' },
            { key: 'rate', label: label('VAS_271_ColRate', 'Rate'), w: .8, align: 'right' },
            { key: 'amount', label: label('VAS_271_ColAmount', 'Amount'), w: .9, align: 'right' },
            { key: 'status', label: label('VAS_271_ColLineStatus', 'Line status'), w: 1 }
        ];

        // 5-Nov Row 26: on hand is in the product's base UOM; its name is shown with the
        // figure when the line was entered in another UOM.
        function inStockText(line) {
            // VAS163 2026-11-11: Row 58 never negative.
            var text = formatQty(Math.max(0, Number(line.FreeStock) || 0));
            if (line.BaseUomName && line.BaseUomName !== line.UomName) { text += ' ' + line.BaseUomName; }
            return text;
        }

        function drawLineTable(tableId) {
            var el = tableEl(tableId);
            if (!el || !lineState) { return; }

            // Sr 8.17: the Attribute column is shown only when some line of the order has
            // an attribute value (decided over all lines, so it does not flicker per page).
            var showAttribute = !!lineState.showAttribute;
            var cols = LINE_COLS.filter(function (c) { return showAttribute || c.key !== 'attr'; });
            var cur = lineState.order.CurrencySymbol;
            var prec = lineState.order.CurrencyPrecision;

            var tpl = cols.map(function (c) { return 'minmax(0,' + c.w + 'fr)'; }).join(' ');
            var pages = Math.max(1, Math.ceil(lineState.lines.length / lineState.size));
            if (lineState.page > pages - 1) { lineState.page = pages - 1; }
            var start = lineState.page * lineState.size;
            var slice = lineState.lines.slice(start, start + lineState.size);

            var head = '<div class="vas271-mrow vas271-mhead-row" style="grid-template-columns:' + tpl + '">' +
                cols.map(function (c) { return '<span class="vas271-cell' + (c.align === 'right' ? ' right' : '') + '" title="' + escapeHtml(c.label) + '">' + escapeHtml(c.label) + '</span>'; }).join('') +
            '</div>';

            var body = slice.map(function (line) {
                // Delivered / pending / in stock only for Item-type products (Sr 8.15);
                // charges and other product types show a dash.
                var isItem = !!line.IsItem;
                // 5-Nov Row 38: a line status only for Item-type product lines - blank
                // for charges and other product types.
                var st = isItem ? lineStatus(line) : null;
                var cells = [
                    cellHtml(String(line.LineNo), 'vas271-c-std', 'right'),
                    cellHtml(line.ProductName, 'vas271-c-prim'),
                    showAttribute ? cellHtml(hasAttributeValue(line.AttributeText) ? line.AttributeText : '', 'vas271-c-std') : '',
                    cellHtml(line.UomName, 'vas271-c-std'),
                    cellHtml(formatQty(line.QtyOrdered), 'vas271-c-std', 'right'),
                    cellHtml(isItem ? formatQty(line.QtyDelivered) : '—', 'vas271-c-std', 'right'),
                    cellHtml(isItem ? formatQty(line.QtyPending) : '—', 'vas271-c-prim', 'right'),
                    cellHtml(isItem ? inStockText(line) : '—', (isItem && Number(line.QtyShort) > 0 ? 'vas271-c-short' : 'vas271-c-ok'), 'right'),
                    cellHtml(formatMoney(line.Rate, cur, 2), 'vas271-c-std', 'right'),
                    cellHtml(formatMoney(line.Amount, cur, prec), 'vas271-c-emph', 'right')
                ].join('') + (st
                    ? '<span class="vas271-cell"><span class="vas271-chip ' + st.cls + '" title="' + escapeHtml(st.text) + '">' + escapeHtml(st.text) + '</span></span>'
                    : '<span class="vas271-cell"></span>');
                return '<div class="vas271-mrow" style="grid-template-columns:' + tpl + '">' + cells + '</div>';
            }).join('');

            var showingLabel = label('VAS_271_Showing', 'Showing') + ' ' + (lineState.lines.length ? (start + 1) : 0) + '–' + (start + slice.length) + ' ' + label('VAS_271_Of', 'of') + ' ' + lineState.lines.length;

            // VAS163 2026-11-11: Row 24 Back / Close on the right of the pager row.
            var foot = '<div class="vas271-mtfoot"><span class="vas271-helper">' + escapeHtml(showingLabel) + '</span>' +
                '<span class="vas271-mtfoot-right">' +
                (pages > 1 ?
                    '<span class="vas271-pager">' +
                        '<button type="button" class="vas271-pbtn" data-table="lines" data-dir="-1"' + (lineState.page === 0 ? ' disabled' : '') + ' aria-label="' + escapeHtml(label('VAS_271_PrevPage', 'Previous page')) + '">' + icon('prev') + '</button>' +
                        '<span class="vas271-ptxt">' + (lineState.page + 1) + ' ' + escapeHtml(label('VAS_271_Of', 'of')) + ' ' + pages + '</span>' +
                        '<button type="button" class="vas271-pbtn" data-table="lines" data-dir="1"' + (lineState.page >= pages - 1 ? ' disabled' : '') + ' aria-label="' + escapeHtml(label('VAS_271_NextPage', 'Next page')) + '">' + icon('next') + '</button>' +
                    '</span>' : '') +
                lineActionsHtml() + '</span>' +
            '</div>';

            el.innerHTML = head + '<div class="vas271-mbody-rows">' + body + '</div>' + foot;
        }

        /* Sizes each visible table's rows-per-page to the space actually left in
           the modal body, so the body never grows an inner scrollbar. The docs
           table is server-paginated, so a size change re-fetches the current
           page at the new size (clamped to the new page count) rather than
           forcing page 0; the guard on "n !== size" makes this converge instead
           of looping. */
        function fitTable(id, currentSize, onResize) {
            var el = tableEl(id);
            if (!el) { return; }
            var avail = el.clientHeight;
            if (avail < 40) { return; }
            var head = el.querySelector('.vas271-mhead-row');
            var foot = el.querySelector('.vas271-mtfoot');
            var row = el.querySelector('.vas271-mbody-rows .vas271-mrow');
            if (!head || !row) { return; }
            var rowHeight = row.getBoundingClientRect().height || 30;
            var used = head.getBoundingClientRect().height + (foot ? foot.getBoundingClientRect().height + 8 : 0);
            var n = Math.floor((avail - used) / rowHeight);
            n = Math.max(MIN_ROWS_PER_PAGE, Math.min(MAX_ROWS_PER_PAGE, n));
            if (n !== currentSize) { onResize(n); }
        }

        function fitAllTables() {
            if (!$mask.hasClass('is-open')) { return; }

            fitTable('vas271-docstbl', docsState.size, function (n) {
                var newPages = Math.max(1, Math.ceil(docsState.total / n));
                var page = Math.min(docsState.page, newPages - 1);
                fetchOrders(page, n, function (ok) { if (ok) { drawDocumentsTable(); } });
            });
            if (lineState) {
                fitTable(lineState.tableId, lineState.size, function (n) {
                    lineState.size = n;
                    drawLineTable(lineState.tableId);
                });
            }
        }

        this.Initalize = function () {
            createWidget();
            createModal();
            bindDocumentLevelEvents();
            loadSummary();
        };

        this.getRoot = function () { return $root; };

        this.disposeComponent = function () {
            var ns = '.vas271-' + ($self.AD_UserHomeWidgetID || $self.windowNo || 'widget') + '-' + instanceTag;
            $(document).off(ns);
            $(window).off(ns);
            if ($mask) { $mask.remove(); }
            $root.remove();
        };

        // Sr 8.28: a refresh also re-arms the tile and re-creates the modal shell if
        // the host removed it, so the widget stays clickable after "refresh all".
        this.refreshWidget = function () {
            if (!$mask || !$.contains(document.body, $mask[0])) { createModal(); }
            if ($tile && !$.contains($root[0], $tile[0])) { $root.append($tile); }
            bindTile();
            bindModal();
            loadSummary();
        };
    };

    VAS.VAS_271_SOsPendingDeliveryWidget.prototype.widgetFirevalueChanged = function (value) {
        if (this.listener) { this.listener.widgetFirevalueChanged(value); }
    };

    VAS.VAS_271_SOsPendingDeliveryWidget.prototype.addChangeListener = function (listener) {
        this.listener = listener;
    };

    VAS.VAS_271_SOsPendingDeliveryWidget.prototype.init = function (windowNo, frame) {
        this.frame = frame;
        this.AD_UserHomeWidgetID = frame.widgetInfo.AD_UserHomeWidgetID;
        this.windowNo = windowNo;
        this.Initalize();
        this.frame.getContentGrid().append(this.getRoot());
        ensureDashInlineSizeVar(this.getRoot());
    };

    VAS.VAS_271_SOsPendingDeliveryWidget.prototype.widgetSizeChange = function () { };

    VAS.VAS_271_SOsPendingDeliveryWidget.prototype.dispose = function () {
        if (this.disposeComponent) { this.disposeComponent(); }
    };

})(VAS, jQuery);
