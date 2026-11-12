/**
 * VAS_273 Order Value Booked MTD Widget (Sales Order dashboard, 2x1 KPI tile)
 * Purpose - The commercial top line and reconciliation anchor for this dashboard:
 *           total pre-tax value of Sales Orders booked this month (DateOrdered from
 *           the 1st of the current month through today), regardless of delivery or
 *           payment state. Booked = DocStatus 'CO' or 'CL' only - Drafted/In-Process
 *           are excluded and stay in VAS_272. Click / Enter / Space opens a
 *           drill-down modal with an 8-card stat strip (two even rows) and a
 *           paginated, newest-first table. SO number opens a record-preview child
 *           modal and the lines icon opens a lines-only child modal (both fed by one
 *           GetSalesOrderDetail fetch).
 *
 *           The month-over-month comparison in the meta line and stat strip compares
 *           an EQUIVALENT elapsed window in the previous month (same number of
 *           calendar days from the 1st, clamped to that month's length) - never a
 *           partial month against a full one - and is omitted entirely (not shown as
 *           +0%/-%) when the previous window has no value to compare against.
 *
 * Design  - 06-kpi-order-value-booked-mtd.html / .md: glass 2x1 KPI tile, stacked
 *           title/currency-headline/meta, "info" (informational, neither good nor
 *           bad) tone and border tint. Modal shell, stat tiles, chips and paginated
 *           table match the same design language as VAS_270/271/272's shared Sales
 *           Order record-preview modal - reused here as this widget's own
 *           self-contained copy, matching how every widget controller/JS pair in
 *           this codebase owns its own modal implementation.
 *
 * Modal   - Documents (top-level) -> Record / Lines (child, back-navigable), same
 *           { title, subtitle, size, render } config-stack engine as VAS_270/271/272 -
 *           "back" pops the stack and re-runs the popped config's render() from its
 *           own already-fetched state, never stale HTML.
 *
 * Backend - VAS_273_OrderValueBookedMTDWidget/GetSummary            (GET -> tile + 8-card stat-strip figures)
 *           VAS_273_OrderValueBookedMTDWidget/GetOrders             (GET page,size -> paginated documents, newest first)
 *           VAS_273_OrderValueBookedMTDWidget/GetSalesOrderDetail   (GET id -> order + lines)
 *
 * ── Labels / Message Keys ─────────────────────────────────────────────────
 *  #  | Current Text                                                          | Message Key
 * ----+------------------------------------------------------------------------+--------------------------------
 *  1  | Order Value Booked MTD                                                | VAS_273_Title
 *  2  | All sales orders booked                                               | VAS_273_SubtitlePrefix
 *  3  | sales orders                                                          | VAS_273_SalesOrdersSuffix
 *  4  | vs                                                                    | VAS_273_VsPrefix
 *  5  | No orders booked yet this month                                       | VAS_273_ZeroState
 *  6  | Figures unavailable                                                   | VAS_273_ErrorState
 *  7  | Order value                                                           | VAS_273_StatOrderValue
 *  8  | Sales orders                                                          | VAS_273_StatSalesOrders
 *  9  | Avg order value                                                       | VAS_273_StatAvgOrderValue
 * 10  | Customers billed                                                      | VAS_273_StatCustomersBilled
 * 11  | New customers                                                         | VAS_273_StatNewCustomers
 * 12  | From quotations                                                       | VAS_273_StatFromQuotations
 * 13  | Direct orders                                                         | VAS_273_StatDirectOrders
 * 14  | SOs                                                                   | VAS_273_SOsSuffix
 * 15  | Sales orders booked this month                                        | VAS_273_SectionHeading
 * 16  | newest first                                                          | VAS_273_NewestFirst
 * 17  | SO No                                                                 | VAS_273_ColSoNo
 * 18  | SO date                                                               | VAS_273_SoDate
 * 19  | Customer                                                              | VAS_273_Customer
 * 20  | Representative                                                        | VAS_273_Representative
 * 21  | Lines                                                                 | VAS_273_ColLines
 * 22  | Qty                                                                   | VAS_273_ColQty
 * 23  | Order value                                                           | VAS_273_ColOrderValue
 * 24  | Status                                                                | VAS_273_ColStatus
 * 25  | Back                                                                  | VAS_273_Back
 * 26  | Close                                                                 | VAS_273_Close
 * 27  | Date promised                                                         | VAS_273_DatePromised
 * 28  | SO value                                                              | VAS_273_SoValue
 * 29  | Ship from                                                             | VAS_273_ShipFrom
 * 30  | Delivery mode                                                         | VAS_273_DeliveryMode
 * 31  | Document status                                                       | VAS_273_DocumentStatus
 * 32  | Delivery status                                                       | VAS_273_DeliveryStatus
 * 33  | Sales order lines                                                     | VAS_273_SalesOrderLines
 * 34  | Line                                                                  | VAS_273_ColLine
 * 35  | Product                                                               | VAS_273_ColProduct
 * 36  | Attribute                                                             | VAS_273_ColAttribute
 * 37  | UoM                                                                   | VAS_273_ColUom
 * 38  | Ordered                                                               | VAS_273_ColOrdered
 * 39  | Delivered                                                             | VAS_273_ColDelivered
 * 40  | Pending                                                               | VAS_273_ColPending
 * 41  | In stock                                                              | VAS_273_ColInStock
 * 42  | Rate                                                                  | VAS_273_ColRate
 * 43  | Amount                                                                | VAS_273_ColAmount
 * 44  | Line status                                                           | VAS_273_ColLineStatus
 * 45  | Delivered                                                             | VAS_273_DeliveryFull
 * 46  | Partially delivered                                                   | VAS_273_DeliveryPartial
 * 47  | Not delivered                                                         | VAS_273_DeliveryNone
 * 48  | Not applicable                                                        | VAS_273_DeliveryNA
 * 49  | Partly delivered                                                      | VAS_273_LinePartial
 * 50  | In process                                                            | VAS_273_LineInProcess
 * 51  | Sales order                                                           | VAS_273_SalesOrderPrefix
 * 52  | lines                                                                 | VAS_273_LinesSuffix
 * 53  | qty ordered                                                           | VAS_273_QtyOrderedSuffix
 * 54  | qty short of stock                                                    | VAS_273_QtyShortSuffix
 * 55  | Previous page                                                         | VAS_273_PrevPage
 * 56  | Next page                                                             | VAS_273_NextPage
 * 57  | of                                                                    | VAS_273_Of
 * 58  | Showing                                                               | VAS_273_Showing
 * 59  | Search is unavailable right now. Try again in a moment.               | VAS_273_LoadError
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
                if (!text || text === 'VAS_SO_Retry' || String(text).charAt(0) === '[') { text = 'Retry'; }
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
    // VAS163 2026-11-11: Row 46 - the lines pop-up paginates after 7 lines.
    var MAX_LINE_ROWS_PER_PAGE = 7;
    // VAS163 2026-11-11: Row 39 - Sales Order window opened by the document number (as VAS_270).
    var ZOOM_WINDOW_NAME = 'VAS_SalesOrder';
    var MONTHS = ['Jan', 'Feb', 'Mar', 'Apr', 'May', 'Jun', 'Jul', 'Aug', 'Sep', 'Oct', 'Nov', 'Dec'];

    /* design.md §Widget Header / §Measurement Setup: keep --dash-inline-size on
       :root equal to the dashboard container's current pixel width so the widget
       clamp resolves against the dashboard's visible width, not the viewport. A
       single document-level ResizeObserver serves every widget (matches VAS_269-272). */
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

    VAS.VAS_273_OrderValueBookedMTDWidget = function () {

        this.frame;
        this.windowNo;

        var $self = this;
        var $root = $('<div class="vas273-root">');
        var $tile;
        var $mask, $modal, $mHead, $mTitle, $mSub, $mBack, $mBody, $mFoot;

        var SELF_ENDPOINT = 'VAS_273_OrderValueBookedMTDWidget/';
        // Per-instance event-namespace suffix (Sr 8.28 pattern of VAS_270/271): disposing an
        // old instance must not unbind the handlers of a re-created one with the same id.
        var instanceTag = 'i' + Math.random().toString(36).slice(2, 9);
        var $mClose;

        var summary = null;           // last fetched GetSummary payload
        var currency = { iso: '', symbol: '', precision: null }; // VAS163: base currency from GetSummary
        var summaryState = 'loading'; // 'loading' | 'ready' | 'error'
        var docsState = { page: 0, size: MAX_ROWS_PER_PAGE, total: 0, rows: [] };
        var lineState = null;         // { order, lines, page, size, tableId }
        var cfgStack = [];
        var currentCfg = null;
        var zoomWindowId = 0;         // VAS163 2026-11-11: Row 39 - resolved Sales Order window id

        var translatedTexts = null;

        /** Loads every message key this widget uses with a single VIS.Msg.translate call. */
        function loadMessages() {
            var elements = [
                'VAS_273_Title', 'VAS_273_ErrorState', 'VAS_273_ZeroState', 'VAS_273_SalesOrdersSuffix',
                'VAS_273_VsPrefix', 'VAS_273_Back', 'VAS_273_Close', 'VAS_273_LoadError', 'VAS_273_SubtitlePrefix',
                'VAS_273_StatOrderValue', 'VAS_273_StatSalesOrders', 'VAS_273_StatAvgOrderValue',
                'VAS_273_StatCustomersBilled', 'VAS_273_StatNewCustomers', 'VAS_273_StatFromQuotations',
                'VAS_273_SOsSuffix', 'VAS_273_StatDirectOrders', 'VAS_273_SectionHeading', 'VAS_273_ColSoNo',
                'VAS_273_SoDate', 'VAS_273_Customer', 'VAS_273_Representative', 'VAS_273_ColLines',
                'VAS_273_ColQty', 'VAS_273_ColOrderValue', 'VAS_273_ColStatus', 'VAS_273_Showing', 'VAS_273_Of',
                'VAS_273_NewestFirst', 'VAS_273_PrevPage', 'VAS_273_NextPage', 'VAS_273_DeliveryFull',
                'VAS_273_LinePartial', 'VAS_273_LineInProcess', 'VAS_273_DatePromised', 'VAS_273_SoValue',
                'VAS_273_ShipFrom', 'VAS_273_DeliveryMode', 'VAS_273_DocumentStatus', 'VAS_273_DeliveryStatus',
                'VAS_273_SalesOrderLines', 'VAS_273_LinesSuffix', 'VAS_273_QtyOrderedSuffix',
                'VAS_273_QtyShortSuffix', 'VAS_273_SalesOrderPrefix', 'VAS_273_ColLine', 'VAS_273_ColOrdered',
                'VAS_273_ColPending', 'VAS_273_ColProduct', 'VAS_273_ColAttribute', 'VAS_273_ColUom',
                'VAS_273_ColDelivered', 'VAS_273_ColInStock', 'VAS_273_ColRate', 'VAS_273_ColAmount',
                'VAS_273_ColLineStatus'
            ];
            try { translatedTexts = VIS.Msg.translate(VIS.Env.getCtx(), elements, true) || {}; }
            catch (e) { translatedTexts = {}; }
        }

        // A missing message key must never render raw ("[KEY]") - fall back to the English text.
        function label(key, fallback) {
            if (translatedTexts === null) { loadMessages(); }
            var text = translatedTexts[key];
            if (!text || text === key || String(text).charAt(0) === '[') { return fallback || key; }
            return text;
        }

        // Browser culture drives every date / number format (window.navigator.language).
        function lang() { return window.navigator.language; }

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
        // integer), matching every other widget on this dashboard.
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

        // VAS163 2026-11-10: browser locale (6-Nov retest2).
        function formatNum(value) {
            return Math.round(Number(value || 0)).toLocaleString(window.navigator.language);
        }

        // VAS163 2026-11-10: browser locale (6-Nov retest2).
        function formatMoney(value, symbol, precision) {
            var prec = (typeof precision === 'number' && precision >= 0) ? precision : 2;
            var text = Number(value || 0).toLocaleString(window.navigator.language, { minimumFractionDigits: prec, maximumFractionDigits: prec });
            return symbol ? symbol + ' ' + text : text;
        }

        // VAS163 2026-11-11: Row 42 - quantities in the line's UOM can be fractional (e.g. ml).
        function formatQty(value) {
            return Number(value || 0).toLocaleString(window.navigator.language, { maximumFractionDigits: 2 });
        }

        // VAS163 2026-11-11: Row 44 - a description made only of blanks / separators is not
        // an attribute value (same rule as VAS_270).
        function hasAttributeValue(text) {
            return !!text && /[^\s\-‐-―_.,:;()\[\]\/\\|]/.test(String(text));
        }

        function formatSignedPercent(value) {
            var num = Number(value || 0);
            try {
                return new Intl.NumberFormat(lang(), { style: 'percent', minimumFractionDigits: 1, maximumFractionDigits: 1, signDisplay: 'always' }).format(num / 100);
            } catch (e) {
                var sign = num > 0 ? '+' : (num < 0 ? '' : '+');
                return sign + num.toFixed(1) + '%';
            }
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

        // VAS163 2026-11-10: browser locale (6-Nov retest2).
        function formatMonthYear(iso) {
            var d = parseIso(iso);
            if (!d) { return ''; }
            return new Date(d.getFullYear(), d.getMonth(), 1).toLocaleDateString(window.navigator.language, { month: 'short', year: 'numeric' });
        }

        // VAS163 2026-11-10: browser locale (6-Nov retest2).
        // MTD window (1st of the month to PeriodEnd).
        function formatPeriod(startIso, endIso) {
            var start = parseIso(startIso), end = parseIso(endIso);
            if (!start || !end) { return ''; }
            var fmt = new Intl.DateTimeFormat(window.navigator.language, { day: '2-digit', month: 'short', year: 'numeric' });
            if (typeof fmt.formatRange === 'function') { return fmt.formatRange(start, end); }
            return fmt.format(start) + ' - ' + fmt.format(end);
        }

        function parseResponse(res) {
            var data = res;
            if (typeof data === 'string') { data = JSON.parse(data); }
            if (typeof data === 'string') { data = JSON.parse(data); }
            return data || {};
        }

        function cellHtml(value, cls, align) {
            return '<span class="vas273-cell' + (align === 'right' ? ' right' : '') + ' ' + cls + '" title="' + escapeHtml(value) + '">' + escapeHtml(value) + '</span>';
        }

        function statTile(l, v) {
            return '<div class="vas273-mstat"><div class="l">' + escapeHtml(l) + '</div><div class="v" title="' + escapeHtml(v) + '">' + escapeHtml(v) + '</div></div>';
        }

        /* ============================================================
         * Tile
         * ============================================================ */
        function createWidget() {
            $tile = $(
                '<button type="button" class="vas273-tile" aria-label="' + escapeHtml(label('VAS_273_Title', 'Order Value Booked MTD')) + '">' +
                    '<p class="vas273-title">' + escapeHtml(label('VAS_273_Title', 'Order Value Booked MTD')) + '</p>' +
                    '<div class="vas273-group">' +
                        '<p class="vas273-val"><span class="vas273-skel"></span></p>' +
                        '<p class="vas273-meta"></p>' +
                    '</div>' +
                '</button>'
            );
            $root.append($tile);
            bindTile();
        }

        /* 5-Nov Row 24 (Sr 8.28 pattern of VAS_270/271): (re)binds the tile click. Run on
           init AND on every refresh - after a "refresh all" from the mosaic panel the host
           can strip jQuery handlers from the widget's DOM (re-attaching the same nodes),
           which left a tile that rendered fine but opened nothing. off() first keeps it
           to one handler. */
        function bindTile() {
            if (!$tile) { return; }
            $tile.off('click.vas273').on('click.vas273', function () { openDocumentsModal(); });
        }

        function renderTile() {
            var $val = $tile.find('.vas273-val');
            var $meta = $tile.find('.vas273-meta');

            if (summaryState === 'loading') {
                $val.html('<span class="vas273-skel"></span>');
                $meta.text('');
                $tile.removeAttr('aria-disabled');
                return;
            }
            if (summaryState === 'error' || !summary) {
                $val.removeClass().addClass('vas273-val error').text('—');
                $meta.text(label('VAS_273_ErrorState', 'Figures unavailable'));
                VASSONet.retry($root, $self);
                $tile.attr('aria-disabled', 'true');
                return;
            }

            $tile.removeAttr('aria-disabled');
            $val.removeClass().addClass('vas273-val info').text(formatAmount(summary.OrderValue));

            if (summary.OrderCount <= 0) {
                $meta.text(label('VAS_273_ZeroState', 'No orders booked yet this month'));
                return;
            }

            var meta = formatNum(summary.OrderCount) + ' ' + label('VAS_273_SalesOrdersSuffix', 'sales orders');
            if (summary.MomPercent !== null && summary.MomPercent !== undefined) {
                meta += ' · ' + formatSignedPercent(summary.MomPercent) + ' ' + label('VAS_273_VsPrefix', 'vs') + ' ' + formatMonthYear(summary.ComparisonMonthStart);
            }
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
            $mask = $('<div class="vas273-mask" role="dialog" aria-modal="true"></div>');
            $modal = $('<div class="vas273-modal"></div>');
            $mHead = $('<div class="vas273-mhead"></div>');
            var $htxt = $('<div class="vas273-htxt"></div>');
            $mBack = $('<button type="button" class="vas273-xbtn" aria-label="' + escapeHtml(label('VAS_273_Back', 'Back')) + '" hidden>' + icon('back') + '</button>');
            var $titleWrap = $('<div></div>');
            $mTitle = $('<h2></h2>');
            $mSub = $('<div class="vas273-msub"></div>');
            $titleWrap.append($mTitle, $mSub);
            $htxt.append($mBack, $titleWrap);
            $mClose = $('<button type="button" class="vas273-xbtn" aria-label="' + escapeHtml(label('VAS_273_Close', 'Close')) + '">' + icon('close') + '</button>');
            $mHead.append($htxt, $mClose);

            $mBody = $('<div class="vas273-mbody"></div>');
            $mFoot = $('<div class="vas273-mfoot"></div>');

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
            var ns = '.vas273m-' + instanceTag;
            $mClose.off(ns).on('click' + ns, closeModal);
            $mBack.off(ns).on('click' + ns, backModal);
            $mBody.off(ns).on('click' + ns, function (event) {
                var pageBtn = event.target.closest ? event.target.closest('[data-dir]') : null;
                if (pageBtn) { turnPage(pageBtn.getAttribute('data-table'), Number(pageBtn.getAttribute('data-dir'))); return; }
                // VAS163 2026-11-11: Row 39 - the document number opens the record on the
                // Sales Order screen (VAS_270 pattern) instead of the preview pop-up.
                var soBtn = event.target.closest ? event.target.closest('[data-so]') : null;
                if (soBtn) { openRecordInWindow(Number(soBtn.getAttribute('data-so'))); return; }
                var linesBtn = event.target.closest ? event.target.closest('[data-lines]') : null;
                if (linesBtn) { openLinesModal(Number(linesBtn.getAttribute('data-lines'))); return; }
            });
        }

        /* VAS163 2026-11-11: Row 39 - same as VAS_270 openRecordInWindow: inside the Sales
           Order window (windowNo >= 0) the host's value-changed channel re-queries that
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
            VAS.ZoomUtil.zoomToRecord('C_Order_ID', orderId, zoomWindowId, ZOOM_WINDOW_NAME, ZOOM_WINDOW_NAME)
                .done(function (id) { if (id > 0) { zoomWindowId = id; } });
        }

        function bindDocumentLevelEvents() {
            var ns = '.vas273-' + ($self.AD_UserHomeWidgetID || $self.windowNo || 'widget') + '-' + instanceTag;

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
            $mBody.html('<div class="vas273-mstate">…</div>');
            $mFoot.html('');
            $mask.addClass('is-open');
        }

        function showLoadError() {
            $mBody.html('<div class="vas273-mstate">' + escapeHtml(label('VAS_273_LoadError', 'Search is unavailable right now. Try again in a moment.')) + '</div>');
        }

        /* ============================================================
         * Documents list (top-level modal)
         * ============================================================ */
        function documentsSubtitle() {
            return label('VAS_273_SubtitlePrefix', 'All sales orders booked') + ' ' + formatPeriod(summary.PeriodStart, summary.PeriodEnd);
        }

        function buildDocsCfg() {
            return { title: label('VAS_273_Title', 'Order Value Booked MTD'), subtitle: documentsSubtitle(), size: '', render: renderDocumentsBody };
        }

        function openDocumentsModal() {
            // 5-Nov Row 24 (Sr 8.28 pattern): never swallow the click - when the tile's
            // last load failed (e.g. the HTTP 500 request burst of a "refresh all"), the
            // modal opens and loads the figures again instead of doing nothing.
            showLoading(label('VAS_273_Title', 'Order Value Booked MTD'));

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
                CustomerName: row.CustomerName || '',
                RepresentativeName: row.RepresentativeName || '',
                LineCount: Number(row.LineCount) || 0,
                OrderQty: Number(row.OrderQty) || 0,
                OrderValue: Number(row.OrderValue) || 0,
                // VAS163 2026-11-11: Row 37 - document (price list) currency of OrderValue.
                CurrencySymbol: row.CurrencySymbol || '',
                CurrencyPrecision: (typeof row.CurrencyPrecision === 'number') ? row.CurrencyPrecision : 2,
                DocumentStatusCode: row.DocumentStatusCode || '',
                DocumentStatus: row.DocumentStatus || ''
            };
        }

        function renderDocumentsBody() {
            // VAS163 2026-11-11: Row 40 (settles rows 14/36) - the "Sales orders", previous-
            // window ("vs 1–5 Oct"), "Customers billed" and "New customers" cards are removed;
            // the remaining four fill one even row of the 4-column strip. Row 41: the Order
            // value / Avg cards are in the base currency (server converts each order).
            var statsHtml = '<div class="vas273-mstats">' +
                statTile(label('VAS_273_StatOrderValue', 'Order value'), formatINR(summary.OrderValue)) +
                statTile(label('VAS_273_StatAvgOrderValue', 'Avg order value'), formatINR(summary.AvgOrderValue)) +
                statTile(label('VAS_273_StatFromQuotations', 'From quotations'), formatNum(summary.FromQuotationCount) + ' ' + label('VAS_273_SOsSuffix', 'SOs')) +
                statTile(label('VAS_273_StatDirectOrders', 'Direct orders'), formatNum(summary.DirectOrderCount) + ' ' + label('VAS_273_SOsSuffix', 'SOs')) +
            '</div>';

            var secHtml = '<div class="vas273-msec">' + escapeHtml(label('VAS_273_SectionHeading', 'Sales orders booked this month')) + '</div>';

            if (docsState.total <= 0) {
                $mBody.html(statsHtml + secHtml + '<div class="vas273-mstate">' + escapeHtml(label('VAS_273_ZeroState', 'No orders booked yet this month')) + '</div>');
            } else {
                $mBody.html(statsHtml + secHtml + '<div class="vas273-mtwrap"><div class="vas273-mtbl" id="vas273-docstbl"></div></div>');
                drawDocumentsTable();
            }
            $mFoot.html('<span class="vas273-foot-note"></span><span><button type="button" class="vas273-btn" id="vas273-docsclose">' + escapeHtml(label('VAS_273_Close', 'Close')) + '</button></span>');
            $mFoot.find('#vas273-docsclose').on('click', closeModal);
        }

        var DOC_COLS = [
            { key: 'icon', label: '', w: .32 },
            { key: 'so', label: label('VAS_273_ColSoNo', 'SO No'), w: 1.15 },
            { key: 'date', label: label('VAS_273_SoDate', 'SO date'), w: 1 },
            { key: 'customer', label: label('VAS_273_Customer', 'Customer'), w: 1.7 },
            { key: 'rep', label: label('VAS_273_Representative', 'Representative'), w: 1.25 },
            { key: 'lines', label: label('VAS_273_ColLines', 'Lines'), w: .55, align: 'right' },
            { key: 'qty', label: label('VAS_273_ColQty', 'Qty'), w: .75, align: 'right' },
            { key: 'value', label: label('VAS_273_ColOrderValue', 'Order value'), w: 1, align: 'right' },
            { key: 'status', label: label('VAS_273_ColStatus', 'Status'), w: 1.1 }
        ];

        function drawDocumentsTable() {
            var el = tableEl('vas273-docstbl');
            if (!el) { return; }
            var tpl = DOC_COLS.map(function (c) { return 'minmax(0,' + c.w + 'fr)'; }).join(' ');
            var pages = Math.max(1, Math.ceil(docsState.total / docsState.size));

            var head = '<div class="vas273-mrow vas273-mhead-row" style="grid-template-columns:' + tpl + '">' +
                DOC_COLS.map(function (c) { return '<span class="vas273-cell' + (c.align === 'right' ? ' right' : '') + '" title="' + escapeHtml(c.label) + '">' + escapeHtml(c.label) + '</span>'; }).join('') +
            '</div>';

            var body = docsState.rows.map(function (row) {
                var cells = [
                    '<span class="vas273-cell center"><button type="button" class="vas273-iconbtn" data-lines="' + row.SalesOrderId + '" title="' + escapeHtml(row.SalesOrderNumber) + '">' + icon('lines') + '</button></span>',
                    '<span class="vas273-cell"><button type="button" class="vas273-lnk" data-so="' + row.SalesOrderId + '" title="' + escapeHtml(row.SalesOrderNumber) + '">' + escapeHtml(row.SalesOrderNumber) + '</button></span>',
                    cellHtml(formatDateFull(row.SalesOrderDate), 'vas273-c-std'),
                    cellHtml(row.CustomerName, 'vas273-c-prim'),
                    cellHtml(row.RepresentativeName || '—', 'vas273-c-std'),
                    cellHtml(formatNum(row.LineCount), 'vas273-c-std', 'right'),
                    cellHtml(formatNum(row.OrderQty), 'vas273-c-std', 'right'),
                    // VAS163 2026-11-11: Row 37 - in the document (price list) currency.
                    cellHtml(formatMoney(row.OrderValue, row.CurrencySymbol, row.CurrencyPrecision), 'vas273-c-emph', 'right'),
                    '<span class="vas273-cell" title="' + escapeHtml(row.DocumentStatus) + '"><span class="vas273-chip vas273-chip-ok">' + escapeHtml(row.DocumentStatus) + '</span></span>'
                ];
                return '<div class="vas273-mrow" style="grid-template-columns:' + tpl + '">' + cells.join('') + '</div>';
            }).join('');

            var start = docsState.page * docsState.size;
            var showingLabel = label('VAS_273_Showing', 'Showing') + ' ' + formatNum(docsState.total ? (start + 1) : 0) + '–' + formatNum(start + docsState.rows.length) + ' ' + label('VAS_273_Of', 'of') + ' ' + formatNum(docsState.total) + ' · ' + label('VAS_273_NewestFirst', 'newest first');

            var foot = '<div class="vas273-mtfoot"><span class="vas273-helper">' + escapeHtml(showingLabel) + '</span>' +
                (pages > 1 ?
                    '<span class="vas273-pager">' +
                        '<button type="button" class="vas273-pbtn" data-table="docs" data-dir="-1"' + (docsState.page === 0 ? ' disabled' : '') + ' aria-label="' + escapeHtml(label('VAS_273_PrevPage', 'Previous page')) + '">' + icon('prev') + '</button>' +
                        '<span class="vas273-ptxt">' + formatNum(docsState.page + 1) + ' ' + escapeHtml(label('VAS_273_Of', 'of')) + ' ' + formatNum(pages) + '</span>' +
                        '<button type="button" class="vas273-pbtn" data-table="docs" data-dir="1"' + (docsState.page >= pages - 1 ? ' disabled' : '') + ' aria-label="' + escapeHtml(label('VAS_273_NextPage', 'Next page')) + '">' + icon('next') + '</button>' +
                    '</span>' : '<span></span>') +
            '</div>';

            el.innerHTML = head + '<div class="vas273-mbody-rows">' + body + '</div>' + foot;
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
            if (line.QtyDelivered >= line.QtyOrdered && line.QtyOrdered > 0) { return { text: label('VAS_273_DeliveryFull', 'Delivered'), cls: 'vas273-chip-info' }; }
            if (line.QtyDelivered > 0) { return { text: label('VAS_273_LinePartial', 'Partly delivered'), cls: 'vas273-chip-warn' }; }
            return { text: label('VAS_273_LineInProcess', 'In process'), cls: 'vas273-chip-prop' };
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

        function openRecordModal(orderId) {
            showLoading(label('VAS_273_Title', 'Order Value Booked MTD') + '…');
            fetchOrderDetail(orderId, function (detail) {
                if (!detail) { showLoadError(); return; }
                showScreen(buildRecordCfg(detail.order, detail.lines), true);
            });
        }

        function buildRecordCfg(order, lines) {
            return {
                title: order.SalesOrderNumber,
                subtitle: [order.CustomerName, formatDateShort(order.SalesOrderDate), order.DocumentStatus].filter(function (p) { return p; }).join(' · '),
                size: '',
                render: function () { renderRecordBody(order, lines); }
            };
        }

        function soHeaderStats(order) {
            return '<div class="vas273-mstats">' +
                statTile(label('VAS_273_Customer', 'Customer'), order.CustomerName) +
                statTile(label('VAS_273_SoDate', 'SO date'), formatDateFull(order.SalesOrderDate)) +
                statTile(label('VAS_273_DatePromised', 'Date promised'), formatDateFull(order.DatePromised)) +
                // VAS163 2026-11-11: Rows 37/38 - ex-tax SO value in the document currency.
                statTile(label('VAS_273_SoValue', 'SO value'), formatMoney(order.OrderValue, order.CurrencySymbol, order.CurrencyPrecision)) +
                statTile(label('VAS_273_ShipFrom', 'Ship from'), order.WarehouseName) +
                statTile(label('VAS_273_DeliveryMode', 'Delivery mode'), order.DeliveryMode) +
                statTile(label('VAS_273_DocumentStatus', 'Document status'), order.DocumentStatus) +
                statTile(label('VAS_273_DeliveryStatus', 'Delivery status'), order.DeliveryStatus) +
            '</div>';
        }

        function renderRecordBody(order, lines) {
            var secHtml = '<div class="vas273-msec">' + escapeHtml(label('VAS_273_SalesOrderLines', 'Sales order lines')) + '</div>';
            $mBody.html(soHeaderStats(order) + secHtml + '<div class="vas273-mtwrap"><div class="vas273-mtbl" id="vas273-linetbl-record"></div></div>');

            lineState = { order: order, lines: lines, page: 0, size: MAX_LINE_ROWS_PER_PAGE, tableId: 'vas273-linetbl-record' };
            drawLineTable('vas273-linetbl-record');

            $mFoot.html(lineFootHtml(order, lines));
            $mFoot.find('#vas273-mback').on('click', backModal);
            $mFoot.find('#vas273-mclose').on('click', closeModal);
        }

        function lineFootHtml(order, lines) {
            // Item-type product lines only (5-Nov Row 36) - charges and other product
            // types carry no ordered / pending / stock figure.
            // VAS163 2026-11-11: Rows 42/58 - shortage stated by the server in the line's UOM
            // (QtyShort; stock is in the base UOM and never negative).
            var qtyOrdered = 0, qtyShort = 0;
            lines.forEach(function (line) {
                if (!line.IsItem) { return; }
                qtyOrdered += Number(line.QtyOrdered) || 0;
                qtyShort += Math.max(0, Number(line.QtyShort) || 0);
            });
            var footNote = lines.length + ' ' + label('VAS_273_LinesSuffix', 'lines') +
                ' · ' + formatQty(qtyOrdered) + ' ' + label('VAS_273_QtyOrderedSuffix', 'qty ordered') +
                ' · ' + order.DeliveryStatus +
                (qtyShort > 0 ? ' · ' + formatQty(qtyShort) + ' ' + label('VAS_273_QtyShortSuffix', 'qty short of stock') : '');

            return '<span class="vas273-foot-note">' + escapeHtml(footNote) + '</span>' +
                '<span><button type="button" class="vas273-btn" id="vas273-mback">' + escapeHtml(label('VAS_273_Back', 'Back')) + '</button> ' +
                '<button type="button" class="vas273-btn" id="vas273-mclose">' + escapeHtml(label('VAS_273_Close', 'Close')) + '</button></span>';
        }

        function openLinesModal(orderId) {
            showLoading(label('VAS_273_SalesOrderLines', 'Sales order lines') + '…');
            fetchOrderDetail(orderId, function (detail) {
                if (!detail) { showLoadError(); return; }
                showScreen(buildLinesCfg(detail.order, detail.lines), true);
            });
        }

        function buildLinesCfg(order, lines) {
            return {
                title: label('VAS_273_SalesOrderLines', 'Sales order lines') + ' · ' + order.SalesOrderNumber,
                subtitle: [order.CustomerName, formatDateShort(order.SalesOrderDate), order.DeliveryStatus].filter(function (p) { return p; }).join(' · '),
                size: '',   // VAS163 2026-11-11: Row 46 - same size as the documents list (no change on Back)
                render: function () { renderLinesBody(order, lines); }
            };
        }

        function renderLinesBody(order, lines) {
            // 5-Nov Row 36: the "Ordered" / "Pending" cards count Item-type product lines
            // only - other product types and charges are excluded.
            var qtyOrdered = 0, qtyPending = 0;
            lines.forEach(function (l) {
                if (!l.IsItem) { return; }
                qtyOrdered += Number(l.QtyOrdered) || 0;
                qtyPending += Number(l.QtyPending) || 0;
            });

            var breadcrumb = '<div class="vas273-polink">' + escapeHtml(label('VAS_273_SalesOrderPrefix', 'Sales order')) +
                ' <button type="button" class="vas273-lnk" data-so="' + order.SalesOrderId + '">' + escapeHtml(order.SalesOrderNumber) + '</button>' +
                ' · ' + escapeHtml(formatDateFull(order.SalesOrderDate)) + ' · ' + escapeHtml(order.DocumentStatus) + '</div>';

            var stats = '<div class="vas273-mstats">' +
                statTile(label('VAS_273_ColLine', 'Line') + 's', String(lines.length)) +
                // VAS163 2026-11-11: Rows 37/38/42 - ex-tax SO value in the document currency;
                // quantities in the lines' selected UOM.
                statTile(label('VAS_273_SoValue', 'SO value'), formatMoney(order.OrderValue, order.CurrencySymbol, order.CurrencyPrecision)) +
                statTile(label('VAS_273_ColOrdered', 'Ordered'), formatQty(qtyOrdered)) +
                statTile(label('VAS_273_ColPending', 'Pending'), formatQty(qtyPending)) +
            '</div>';

            var secHtml = '<div class="vas273-msec">' + escapeHtml(label('VAS_273_SalesOrderLines', 'Sales order lines')) + '</div>';
            $mBody.html(breadcrumb + stats + secHtml + '<div class="vas273-mtwrap"><div class="vas273-mtbl" id="vas273-linetbl-lines"></div></div>');

            lineState = { order: order, lines: lines, page: 0, size: MAX_LINE_ROWS_PER_PAGE, tableId: 'vas273-linetbl-lines' };
            drawLineTable('vas273-linetbl-lines');

            $mFoot.html(lineFootHtml(order, lines));
            $mFoot.find('#vas273-mback').on('click', backModal);
            $mFoot.find('#vas273-mclose').on('click', closeModal);
        }

        var LINE_COLS = [
            { key: 'no', label: label('VAS_273_ColLine', 'Line'), w: .35, align: 'right' },
            { key: 'product', label: label('VAS_273_ColProduct', 'Product'), w: 1.5 },
            { key: 'attr', label: label('VAS_273_ColAttribute', 'Attribute'), w: 1.1 },
            { key: 'uom', label: label('VAS_273_ColUom', 'UoM'), w: .5 },
            { key: 'ordered', label: label('VAS_273_ColOrdered', 'Ordered'), w: .7, align: 'right' },
            { key: 'delivered', label: label('VAS_273_ColDelivered', 'Delivered'), w: .7, align: 'right' },
            { key: 'pending', label: label('VAS_273_ColPending', 'Pending'), w: .7, align: 'right' },
            { key: 'instock', label: label('VAS_273_ColInStock', 'In stock'), w: .7, align: 'right' },
            { key: 'rate', label: label('VAS_273_ColRate', 'Rate'), w: .7, align: 'right' },
            { key: 'amount', label: label('VAS_273_ColAmount', 'Amount'), w: .9, align: 'right' },
            { key: 'status', label: label('VAS_273_ColLineStatus', 'Line status'), w: 1 }
        ];

        // VAS163 2026-11-11: In stock is in the product's base UOM; its name is shown when
        // the line was entered in another UOM (as VAS_270).
        function inStockText(line) {
            var text = formatQty(Math.max(0, Number(line.FreeStock) || 0));
            if (line.BaseUomName && line.BaseUomName !== line.UomName) { text += ' ' + line.BaseUomName; }
            return text;
        }

        function drawLineTable(tableId) {
            var el = tableEl(tableId);
            if (!el || !lineState) { return; }

            // VAS163 2026-11-11: Row 44 - the Attribute column only when at least one line of
            // the order has an attribute value (decided over all lines, not per page).
            var showAttribute = lineState.lines.some(function (l) { return hasAttributeValue(l.AttributeText); });
            var cols = LINE_COLS.filter(function (c) { return showAttribute || c.key !== 'attr'; });
            var cur = lineState.order.CurrencySymbol;
            var prec = lineState.order.CurrencyPrecision;

            var tpl = cols.map(function (c) { return 'minmax(0,' + c.w + 'fr)'; }).join(' ');
            var pages = Math.max(1, Math.ceil(lineState.lines.length / lineState.size));
            if (lineState.page > pages - 1) { lineState.page = pages - 1; }
            var start = lineState.page * lineState.size;
            var slice = lineState.lines.slice(start, start + lineState.size);

            var head = '<div class="vas273-mrow vas273-mhead-row" style="grid-template-columns:' + tpl + '">' +
                cols.map(function (c) { return '<span class="vas273-cell' + (c.align === 'right' ? ' right' : '') + '" title="' + escapeHtml(c.label) + '">' + escapeHtml(c.label) + '</span>'; }).join('') +
            '</div>';

            var body = slice.map(function (line) {
                // VAS163 2026-11-11: Rows 45/80 - Delivered / Pending / In stock / Line status
                // only for Item-type products; other product types and charges show "-".
                var isItem = !!line.IsItem;
                var st = isItem ? lineStatus(line) : null;
                var cells = [
                    cellHtml(String(line.LineNo), 'vas273-c-std', 'right'),
                    cellHtml(line.ProductName, 'vas273-c-prim'),
                    showAttribute ? cellHtml(hasAttributeValue(line.AttributeText) ? line.AttributeText : '', 'vas273-c-std') : '',
                    cellHtml(line.UomName, 'vas273-c-std'),
                    // VAS163 2026-11-11: Row 42 - quantities in the line's selected UOM (server).
                    cellHtml(formatQty(line.QtyOrdered), 'vas273-c-std', 'right'),
                    cellHtml(isItem ? formatQty(line.QtyDelivered) : '-', 'vas273-c-std', 'right'),
                    cellHtml(isItem ? formatQty(line.QtyPending) : '-', 'vas273-c-prim', 'right'),
                    // VAS163 2026-11-11: Row 58 - In stock never negative.
                    cellHtml(isItem ? inStockText(line) : '-', (isItem && Number(line.QtyShort) > 0 ? 'vas273-c-short' : 'vas273-c-ok'), 'right'),
                    // VAS163 2026-11-11: Row 47 - Rate with 2 decimals; Rows 37/38 - document
                    // currency, ex-tax amounts.
                    cellHtml(formatMoney(line.Rate, cur, 2), 'vas273-c-std', 'right'),
                    cellHtml(formatMoney(line.Amount, cur, prec), 'vas273-c-emph', 'right')
                ].join('') + (st
                    ? '<span class="vas273-cell"><span class="vas273-chip ' + st.cls + '" title="' + escapeHtml(st.text) + '">' + escapeHtml(st.text) + '</span></span>'
                    : '<span class="vas273-cell">-</span>');
                return '<div class="vas273-mrow" style="grid-template-columns:' + tpl + '">' + cells + '</div>';
            }).join('');

            var showingLabel = label('VAS_273_Showing', 'Showing') + ' ' + formatNum(lineState.lines.length ? (start + 1) : 0) + '–' + formatNum(start + slice.length) + ' ' + label('VAS_273_Of', 'of') + ' ' + formatNum(lineState.lines.length);

            var foot = '<div class="vas273-mtfoot"><span class="vas273-helper">' + escapeHtml(showingLabel) + '</span>' +
                (pages > 1 ?
                    '<span class="vas273-pager">' +
                        '<button type="button" class="vas273-pbtn" data-table="lines" data-dir="-1"' + (lineState.page === 0 ? ' disabled' : '') + ' aria-label="' + escapeHtml(label('VAS_273_PrevPage', 'Previous page')) + '">' + icon('prev') + '</button>' +
                        '<span class="vas273-ptxt">' + formatNum(lineState.page + 1) + ' ' + escapeHtml(label('VAS_273_Of', 'of')) + ' ' + formatNum(pages) + '</span>' +
                        '<button type="button" class="vas273-pbtn" data-table="lines" data-dir="1"' + (lineState.page >= pages - 1 ? ' disabled' : '') + ' aria-label="' + escapeHtml(label('VAS_273_NextPage', 'Next page')) + '">' + icon('next') + '</button>' +
                    '</span>' : '<span></span>') +
            '</div>';

            el.innerHTML = head + '<div class="vas273-mbody-rows">' + body + '</div>' + foot;
        }

        /* Sizes each visible table's rows-per-page to the space actually left in
           the modal body, so the body never grows an inner scrollbar. The docs
           table is server-paginated, so a size change re-fetches the current
           page at the new size (clamped to the new page count) rather than
           forcing page 0; the guard on "n !== size" makes this converge instead
           of looping. */
        function fitTable(id, currentSize, onResize, maxRows) {
            var el = tableEl(id);
            if (!el) { return; }
            var avail = el.clientHeight;
            if (avail < 40) { return; }
            var head = el.querySelector('.vas273-mhead-row');
            var foot = el.querySelector('.vas273-mtfoot');
            var row = el.querySelector('.vas273-mbody-rows .vas273-mrow');
            if (!head || !row) { return; }
            var rowHeight = row.getBoundingClientRect().height || 30;
            var used = head.getBoundingClientRect().height + (foot ? foot.getBoundingClientRect().height + 8 : 0);
            var n = Math.floor((avail - used) / rowHeight);
            n = Math.max(MIN_ROWS_PER_PAGE, Math.min(maxRows || MAX_ROWS_PER_PAGE, n));
            if (n !== currentSize) { onResize(n); }
        }

        function fitAllTables() {
            if (!$mask.hasClass('is-open')) { return; }

            fitTable('vas273-docstbl', docsState.size, function (n) {
                var newPages = Math.max(1, Math.ceil(docsState.total / n));
                var page = Math.min(docsState.page, newPages - 1);
                fetchOrders(page, n, function (ok) { if (ok) { drawDocumentsTable(); } });
            });
            if (lineState) {
                // VAS163 2026-11-11: Row 46 - at most 7 lines per page.
                fitTable(lineState.tableId, lineState.size, function (n) {
                    lineState.size = n;
                    drawLineTable(lineState.tableId);
                }, MAX_LINE_ROWS_PER_PAGE);
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
            var ns = '.vas273-' + ($self.AD_UserHomeWidgetID || $self.windowNo || 'widget') + '-' + instanceTag;
            $(document).off(ns);
            $(window).off(ns);
            if ($mask) { $mask.remove(); }
            $root.remove();
        };

        // 5-Nov Row 24: a refresh also re-arms the tile and the modal and re-creates the
        // modal shell if the host removed it, so the widget stays clickable after
        // "refresh all" (same as VAS_270/271).
        this.refreshWidget = function () {
            if (!$mask || !$.contains(document.body, $mask[0])) { createModal(); }
            if ($tile && !$.contains($root[0], $tile[0])) { $root.append($tile); }
            bindTile();
            bindModal();
            loadSummary();
        };
    };

    VAS.VAS_273_OrderValueBookedMTDWidget.prototype.widgetFirevalueChanged = function (value) {
        if (this.listener) { this.listener.widgetFirevalueChanged(value); }
    };

    VAS.VAS_273_OrderValueBookedMTDWidget.prototype.addChangeListener = function (listener) {
        this.listener = listener;
    };

    VAS.VAS_273_OrderValueBookedMTDWidget.prototype.init = function (windowNo, frame) {
        this.frame = frame;
        this.AD_UserHomeWidgetID = frame.widgetInfo.AD_UserHomeWidgetID;
        this.windowNo = windowNo;
        this.Initalize();
        this.frame.getContentGrid().append(this.getRoot());
        ensureDashInlineSizeVar(this.getRoot());
    };

    VAS.VAS_273_OrderValueBookedMTDWidget.prototype.widgetSizeChange = function () { };

    VAS.VAS_273_OrderValueBookedMTDWidget.prototype.dispose = function () {
        if (this.disposeComponent) { this.disposeComponent(); }
    };

})(VAS, jQuery);
