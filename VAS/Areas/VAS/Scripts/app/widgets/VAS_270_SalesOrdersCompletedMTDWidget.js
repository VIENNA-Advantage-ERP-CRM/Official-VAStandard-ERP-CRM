/**
 * VAS_270 Sales Orders Completed MTD Widget (Sales Order dashboard, 2x1 KPI tile)
 * Purpose - Month-to-date throughput: how many Sales Orders reached DocStatus 'CO'
 *           this month (DateOrdered from the 1st of the current month through
 *           today), with the delivered value and on-time-shipment rate for that
 *           same cohort. Click / Enter / Space opens a drill-down modal with a
 *           4-card stat strip and a paginated "Documents" table; each row's SO
 *           number and its lines icon open the same "Sales order lines" child
 *           modal (Sr 8.11, fed by one GetSalesOrderDetail fetch).
 *           No period selector - "MTD" is this tile's fixed definition.
 *
 *           Per 03_Sales_Orders_Completed_MTD_Claude_Development_Prompt.txt
 *           "CONFIRMED BUSINESS DEFINITION - OVERRIDES THE ORIGINAL DRAFT", the
 *           paired .md draft's Completed/Closed split, its write-off note, and a
 *           "Closed" stat card are explicitly NOT implemented: DocStatus 'CO'
 *           only, delivery state is never part of the headline count, and Closed
 *           ('CL') orders are excluded outright.
 *
 * Design  - 03-kpi-completed-mtd.html / .md: glass 2x1 KPI tile, stacked
 *           title/headline/meta, "ok" (healthy) tone and border tint. Modal
 *           shell, stat tiles, chips and paginated table match the same design
 *           language as VAS_268's shared Sales Order record-preview modal
 *           (Customer / SO date / Date promised / SO value / Ship from /
 *           Delivery mode / Document status / Delivery status stat tiles, and
 *           the same line-table columns) - reused here as this widget's own
 *           self-contained copy, matching how every widget controller/JS pair in
 *           this codebase (VAS_268 included) owns its own modal implementation
 *           rather than importing another module's private state.
 *
 * Modal   - Documents (top-level) -> Lines (child, back-navigable).
 *           Every screen is a { title, subtitle, size, render } config; "back"
 *           pops the stack and calls the popped config's render() again, which
 *           re-populates the body from that screen's own already-fetched state
 *           (docsState / lineState) - never stale HTML.
 *
 * Backend - VAS_270_SalesOrdersCompletedMTDWidget/GetSummary            (GET -> tile + stat-strip figures)
 *           VAS_270_SalesOrdersCompletedMTDWidget/GetOrders             (GET page,size -> paginated documents)
 *           VAS_270_SalesOrdersCompletedMTDWidget/GetSalesOrderDetail   (GET id -> order + lines)
 *
 * ── Labels / Message Keys ─────────────────────────────────────────────────
 *  #  | Current Text                                                          | Message Key
 * ----+------------------------------------------------------------------------+--------------------------------
 *  1  | Sales Orders Completed MTD                                            | VAS_270_Title
 *  2  | Date ordered                                                          | VAS_270_DateOrderedLabel
 *  3  | delivered                                                             | VAS_270_DeliveredSuffix
 *  4  | shipped on time                                                       | VAS_270_ShippedOnTimeSuffix
 *  5  | No orders completed yet this month                                    | VAS_270_ZeroState
 *  6  | Figures unavailable                                                   | VAS_270_ErrorState
 *  7  | Completed SOs                                                         | VAS_270_StatCompleted
 *  8  | SOs with delivery                                                     | VAS_270_StatWithDelivery
 *  9  | Delivered value                                                       | VAS_270_StatDeliveredValue
 * 10  | On-time shipment                                                      | VAS_270_StatOnTime
 * 11  | Documents                                                             | VAS_270_SectionDocuments
 * 12  | Close                                                                 | VAS_270_Close
 * 13  | Back                                                                  | VAS_270_Back
 * 14  | Customer                                                              | VAS_270_Customer
 * 15  | SO date                                                               | VAS_270_SoDate
 * 16  | Date promised                                                         | VAS_270_DatePromised
 * 17  | SO value                                                              | VAS_270_SoValue
 * 18  | Ship from                                                             | VAS_270_ShipFrom
 * 19  | Delivery mode                                                         | VAS_270_DeliveryMode
 * 20  | Document status                                                       | VAS_270_DocumentStatus
 * 21  | Delivery status                                                       | VAS_270_DeliveryStatus
 * 22  | Sales order lines                                                     | VAS_270_SalesOrderLines
 * 23  | Line                                                                  | VAS_270_ColLine
 * 24  | Product                                                               | VAS_270_ColProduct
 * 25  | Attribute                                                             | VAS_270_ColAttribute
 * 26  | UoM                                                                   | VAS_270_ColUom
 * 27  | Ordered                                                               | VAS_270_ColOrdered
 * 28  | Delivered                                                             | VAS_270_ColDelivered
 * 29  | Pending                                                               | VAS_270_ColPending
 * 30  | In stock                                                              | VAS_270_ColInStock
 * 31  | Rate                                                                  | VAS_270_ColRate
 * 32  | Amount                                                                | VAS_270_ColAmount
 * 33  | Line status                                                           | VAS_270_ColLineStatus
 * 34  | SO No                                                                 | VAS_270_ColSoNo
 * 35  | Warehouse                                                             | VAS_270_Warehouse
 * 36  | Representative                                                        | VAS_270_Representative
 * 37  | Value                                                                 | VAS_270_ColValue
 * 38  | Delivery                                                              | VAS_270_ColDelivery
 * 39  | Status                                                                | VAS_270_ColStatus
 * 40  | Not delivered                                                         | VAS_270_DeliveryNone
 * 41  | Partially delivered                                                   | VAS_270_DeliveryPartial
 * 42  | Delivered                                                             | VAS_270_DeliveryFull
 * 43  | In process                                                            | VAS_270_LineInProcess
 * 44  | Partly delivered                                                      | VAS_270_LinePartial
 * 45  | Sales order                                                           | VAS_270_SalesOrderPrefix
 * 46  | lines                                                                 | VAS_270_LinesSuffix
 * 47  | qty ordered                                                           | VAS_270_QtyOrderedSuffix
 * 48  | qty short of stock                                                    | VAS_270_QtyShortSuffix
 * 49  | Previous page                                                         | VAS_270_PrevPage
 * 50  | Next page                                                             | VAS_270_NextPage
 * 51  | of                                                                    | VAS_270_Of
 * 52  | Showing                                                               | VAS_270_Showing
 * 53  | Search is unavailable right now. Try again in a moment.               | VAS_270_LoadError
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
    var MONTHS = ['Jan', 'Feb', 'Mar', 'Apr', 'May', 'Jun', 'Jul', 'Aug', 'Sep', 'Oct', 'Nov', 'Dec'];

    /* design.md §Widget Header / §Measurement Setup: keep --dash-inline-size on
       :root equal to the dashboard container's current pixel width so the widget
       clamp resolves against the dashboard's visible width, not the viewport. A
       single document-level ResizeObserver serves every widget (matches VAS_269). */
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

    VAS.VAS_270_SalesOrdersCompletedMTDWidget = function () {

        this.frame;
        this.windowNo;

        var $self = this;
        var $root = $('<div class="vas270-root">');
        var $tile;
        var $mask, $modal, $mHead, $mTitle, $mSub, $mBack, $mBody, $mFoot;

        var SELF_ENDPOINT = 'VAS_270_SalesOrdersCompletedMTDWidget/';
        // Per-instance event-namespace suffix (Sr 8.28): disposing an old instance must
        // not unbind the window handlers of a re-created one with the same widget id.
        var instanceTag = 'i' + Math.random().toString(36).slice(2, 9);

        var summary = null;           // last fetched GetSummary payload
        var summaryState = 'loading'; // 'loading' | 'ready' | 'error'
        var docsState = { page: 0, size: MAX_ROWS_PER_PAGE, total: 0, rows: [] };
        var lineState = null;         // { order, lines, page, size, tableId }
        var cfgStack = [];
        var currentCfg = null;

        function label(key, fallback) {
            return VIS.Msg.getMsg(key);
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
        // integer), matching every other widget on this dashboard (VAS_268 fmtINR).
        function formatINR(value) {
            var num = Number(value || 0);
            if (num >= 1e7) { return '₹ ' + (num / 1e7).toFixed(2) + ' Cr'; }
            if (num >= 1e5) { return '₹ ' + (num / 1e5).toFixed(2) + ' L'; }
            return '₹ ' + Math.round(num).toLocaleString('en-IN');
        }

        function formatNum(value) {
            return Math.round(Number(value || 0)).toLocaleString('en-IN');
        }

        // Amounts in the order's (price list's) own currency and precision.
        function formatMoney(value, symbol, precision) {
            var prec = (typeof precision === 'number' && precision >= 0) ? precision : 2;
            var text = Number(value || 0).toLocaleString('en-IN', { minimumFractionDigits: prec, maximumFractionDigits: prec });
            return symbol ? symbol + ' ' + text : text;
        }

        // Sr 8.13: line quantities are in the selected UOM and can be fractional.
        function formatQty(value) {
            return Number(value || 0).toLocaleString('en-IN', { maximumFractionDigits: 2 });
        }

        // Sr 8.17: a description made only of blanks / separators is not an attribute value (VAS_203 rule).
        function hasAttributeValue(text) {
            return !!text && /[^\s\-‐-―_.,:;()\[\]\/\\|]/.test(String(text));
        }

        function parseIso(iso) {
            if (!iso) { return null; }
            var d = new Date(iso + 'T00:00:00');
            return isNaN(d.getTime()) ? null : d;
        }

        function formatDateShort(iso) {
            var d = parseIso(iso);
            if (!d) { return ''; }
            return (d.getDate() < 10 ? '0' : '') + d.getDate() + ' ' + MONTHS[d.getMonth()];
        }

        function formatDateFull(iso) {
            var d = parseIso(iso);
            if (!d) { return ''; }
            return (d.getDate() < 10 ? '0' : '') + d.getDate() + ' ' + MONTHS[d.getMonth()] + ' ' + d.getFullYear();
        }

        // "1-12 Aug 2026" style window - MTD always starts on the 1st of the
        // month PeriodEnd falls in, per 03_...txt "VISIBLE COPY".
        function formatPeriod(startIso, endIso) {
            var start = parseIso(startIso), end = parseIso(endIso);
            if (!start || !end) { return ''; }
            return start.getDate() + '-' + end.getDate() + ' ' + MONTHS[end.getMonth()] + ' ' + end.getFullYear();
        }

        function parseResponse(res) {
            var data = res;
            if (typeof data === 'string') { data = JSON.parse(data); }
            if (typeof data === 'string') { data = JSON.parse(data); }
            return data || {};
        }

        function cellHtml(value, cls, align) {
            return '<span class="vas270-cell' + (align === 'right' ? ' right' : '') + ' ' + cls + '" title="' + escapeHtml(value) + '">' + escapeHtml(value) + '</span>';
        }

        function statTile(l, v) {
            return '<div class="vas270-mstat"><div class="l">' + escapeHtml(l) + '</div><div class="v" title="' + escapeHtml(v) + '">' + escapeHtml(v) + '</div></div>';
        }

        /* ============================================================
         * Tile
         * ============================================================ */
        function createWidget() {
            $tile = $(
                '<button type="button" class="vas270-tile" aria-label="' + escapeHtml(label('VAS_270_Title', 'Sales Orders Completed MTD')) + '">' +
                    '<p class="vas270-title">' + escapeHtml(label('VAS_270_Title', 'Sales Orders Completed MTD')) + '</p>' +
                    '<div class="vas270-group">' +
                        '<p class="vas270-val"><span class="vas270-skel"></span></p>' +
                        '<p class="vas270-meta"></p>' +
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
            $tile.off('click.vas270').on('click.vas270', function () { openDocumentsModal(); });
        }

        function renderTile() {
            var $val = $tile.find('.vas270-val');
            var $meta = $tile.find('.vas270-meta');

            if (summaryState === 'loading') {
                $val.html('<span class="vas270-skel"></span>');
                $meta.text('');
                $tile.removeAttr('aria-disabled');
                return;
            }
            if (summaryState === 'error' || !summary) {
                $val.removeClass().addClass('vas270-val error').text('—');
                $meta.text(label('VAS_270_ErrorState', 'Figures unavailable'));
                VASSONet.retry($root, $self);
                $tile.attr('aria-disabled', 'true');
                return;
            }

            $tile.removeAttr('aria-disabled');
            $val.removeClass().addClass('vas270-val').text(formatNum(summary.CompletedCount));

            if (summary.CompletedCount <= 0) {
                $meta.text(label('VAS_270_ZeroState', 'No orders completed yet this month'));
                return;
            }

            var onTimeText = (summary.OnTimePercent === null || summary.OnTimePercent === undefined)
                ? '—' : (Math.round(summary.OnTimePercent) + '%');
            var meta = formatINR(summary.DeliveredValue) + ' ' + label('VAS_270_DeliveredSuffix', 'delivered') +
                ' · ' + onTimeText + ' ' + label('VAS_270_ShippedOnTimeSuffix', 'shipped on time');
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
                    else { summaryState = 'ready'; summary = parsed; }
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
            $mask = $('<div class="vas270-mask" role="dialog" aria-modal="true"></div>');
            $modal = $('<div class="vas270-modal"></div>');
            $mHead = $('<div class="vas270-mhead"></div>');
            var $htxt = $('<div class="vas270-htxt"></div>');
            $mBack = $('<button type="button" class="vas270-xbtn" aria-label="' + escapeHtml(label('VAS_270_Back', 'Back')) + '" hidden>' + icon('back') + '</button>');
            var $titleWrap = $('<div></div>');
            $mTitle = $('<h2></h2>');
            $mSub = $('<div class="vas270-msub"></div>');
            $titleWrap.append($mTitle, $mSub);
            $htxt.append($mBack, $titleWrap);
            var $closeBtn = $('<button type="button" class="vas270-xbtn" aria-label="' + escapeHtml(label('VAS_270_Close', 'Close')) + '">' + icon('close') + '</button>');
            $mHead.append($htxt, $closeBtn);

            $mBody = $('<div class="vas270-mbody"></div>');
            $mFoot = $('<div class="vas270-mfoot"></div>');

            $modal.append($mHead, $mBody, $mFoot);
            $mask.append($modal);
            $('body').append($mask);

            $closeBtn.on('click', closeModal);
            $mBack.on('click', backModal);
            $mBody.on('click', function (event) {
                var pageBtn = event.target.closest ? event.target.closest('[data-dir]') : null;
                if (pageBtn) { turnPage(pageBtn.getAttribute('data-table'), Number(pageBtn.getAttribute('data-dir'))); return; }
                // Sr 8.11: the document number and the Line Details icon open the SAME pop-up.
                var soBtn = event.target.closest ? event.target.closest('[data-so]') : null;
                if (soBtn) { openLinesModal(Number(soBtn.getAttribute('data-so'))); return; }
                var linesBtn = event.target.closest ? event.target.closest('[data-lines]') : null;
                if (linesBtn) { openLinesModal(Number(linesBtn.getAttribute('data-lines'))); return; }
            });
        }

        function bindDocumentLevelEvents() {
            var ns = '.vas270-' + ($self.AD_UserHomeWidgetID || $self.windowNo || 'widget') + '-' + instanceTag;

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
            $mBody.html('<div class="vas270-mstate">…</div>');
            $mFoot.html('');
            $mask.addClass('is-open');
        }

        function showLoadError() {
            $mBody.html('<div class="vas270-mstate">' + escapeHtml(label('VAS_270_LoadError', 'Search is unavailable right now. Try again in a moment.')) + '</div>');
        }

        /* ============================================================
         * Documents list (top-level modal)
         * ============================================================ */
        function documentsSubtitle() {
            return summary ? (label('VAS_270_DateOrderedLabel', 'Date ordered') + ' · ' + formatPeriod(summary.PeriodStart, summary.PeriodEnd)) : '';
        }

        function buildDocsCfg() {
            return { title: label('VAS_270_Title', 'Sales Orders Completed MTD'), subtitle: documentsSubtitle(), size: '', render: renderDocumentsBody };
        }

        function openDocumentsModal() {
            // Sr 8.28: never swallow the click - when the tile's last load failed
            // (e.g. the request burst of a "refresh all"), the modal opens and loads
            // the figures again instead of doing nothing.
            showLoading(label('VAS_270_Title', 'Sales Orders Completed MTD'));

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
                WarehouseName: row.WarehouseName || '',
                RepresentativeName: row.RepresentativeName || '',
                OrderValue: Number(row.OrderValue) || 0,
                DeliveryStatus: row.DeliveryStatus || '',
                DocumentStatus: row.DocumentStatus || ''
            };
        }

        function deliveryChipClass(text) {
            var t = String(text || '').toLowerCase();
            if (t === label('VAS_270_DeliveryFull', 'Delivered').toLowerCase()) { return 'vas270-chip-info'; }
            if (t === label('VAS_270_DeliveryPartial', 'Partially delivered').toLowerCase()) { return 'vas270-chip-warn'; }
            return 'vas270-chip-neutral';
        }

        function renderDocumentsBody() {
            var statsHtml = '<div class="vas270-mstats">' +
                statTile(label('VAS_270_StatCompleted', 'Completed SOs'), formatNum(summary.CompletedCount)) +
                statTile(label('VAS_270_StatWithDelivery', 'SOs with delivery'), formatNum(summary.DeliveredOrderCount)) +
                statTile(label('VAS_270_StatDeliveredValue', 'Delivered value'), formatINR(summary.DeliveredValue)) +
                statTile(label('VAS_270_StatOnTime', 'On-time shipment'), (summary.OnTimePercent === null || summary.OnTimePercent === undefined) ? '—' : (Math.round(summary.OnTimePercent) + '%')) +
            '</div>';

            var secHtml = '<div class="vas270-msec">' + escapeHtml(label('VAS_270_SectionDocuments', 'Documents')) + '</div>';

            if (docsState.total <= 0) {
                $mBody.html(statsHtml + secHtml + '<div class="vas270-mstate">' + escapeHtml(label('VAS_270_ZeroState', 'No orders completed yet this month')) + '</div>');
            } else {
                $mBody.html(statsHtml + secHtml + '<div class="vas270-mtwrap"><div class="vas270-mtbl" id="vas270-docstbl"></div></div>');
                drawDocumentsTable();
            }
            $mFoot.html('<span class="vas270-foot-note"></span><span><button type="button" class="vas270-btn" id="vas270-docsclose">' + escapeHtml(label('VAS_270_Close', 'Close')) + '</button></span>');
            $mFoot.find('#vas270-docsclose').on('click', closeModal);
        }

        var DOC_COLS = [
            { key: 'icon', label: '', w: .32 },
            { key: 'so', label: label('VAS_270_ColSoNo', 'SO No'), w: 1.15 },
            { key: 'date', label: label('VAS_270_SoDate', 'SO date'), w: .95 },
            { key: 'customer', label: label('VAS_270_Customer', 'Customer'), w: 1.6 },
            { key: 'warehouse', label: label('VAS_270_Warehouse', 'Warehouse'), w: 1.15 },
            { key: 'rep', label: label('VAS_270_Representative', 'Representative'), w: 1.15 },
            { key: 'value', label: label('VAS_270_ColValue', 'Value'), w: .85, align: 'right' },
            { key: 'delivery', label: label('VAS_270_ColDelivery', 'Delivery'), w: 1.05 },
            { key: 'status', label: label('VAS_270_ColStatus', 'Status'), w: .95 }
        ];

        function drawDocumentsTable() {
            var el = document.getElementById('vas270-docstbl');
            if (!el) { return; }
            var tpl = DOC_COLS.map(function (c) { return 'minmax(0,' + c.w + 'fr)'; }).join(' ');
            var pages = Math.max(1, Math.ceil(docsState.total / docsState.size));

            var head = '<div class="vas270-mrow vas270-mhead-row" style="grid-template-columns:' + tpl + '">' +
                DOC_COLS.map(function (c) { return '<span class="vas270-cell' + (c.align === 'right' ? ' right' : '') + '" title="' + escapeHtml(c.label) + '">' + escapeHtml(c.label) + '</span>'; }).join('') +
            '</div>';

            var body = docsState.rows.map(function (row) {
                var cells = [
                    '<span class="vas270-cell center"><button type="button" class="vas270-iconbtn" data-lines="' + row.SalesOrderId + '" title="' + escapeHtml(row.SalesOrderNumber) + '">' + icon('lines') + '</button></span>',
                    '<span class="vas270-cell"><button type="button" class="vas270-lnk" data-so="' + row.SalesOrderId + '" title="' + escapeHtml(row.SalesOrderNumber) + '">' + escapeHtml(row.SalesOrderNumber) + '</button></span>',
                    cellHtml(formatDateFull(row.SalesOrderDate), 'vas270-c-std'),
                    cellHtml(row.CustomerName, 'vas270-c-prim'),
                    cellHtml(row.WarehouseName, 'vas270-c-std'),
                    cellHtml(row.RepresentativeName, 'vas270-c-std'),
                    cellHtml(formatINR(row.OrderValue), 'vas270-c-emph', 'right'),
                    '<span class="vas270-cell" title="' + escapeHtml(row.DeliveryStatus) + '"><span class="vas270-chip ' + deliveryChipClass(row.DeliveryStatus) + '">' + escapeHtml(row.DeliveryStatus) + '</span></span>',
                    '<span class="vas270-cell" title="' + escapeHtml(row.DocumentStatus) + '"><span class="vas270-chip vas270-chip-ok">' + escapeHtml(row.DocumentStatus) + '</span></span>'
                ];
                return '<div class="vas270-mrow" style="grid-template-columns:' + tpl + '">' + cells.join('') + '</div>';
            }).join('');

            var start = docsState.page * docsState.size;
            var showingLabel = label('VAS_270_Showing', 'Showing') + ' ' + (docsState.total ? (start + 1) : 0) + '–' + (start + docsState.rows.length) + ' ' + label('VAS_270_Of', 'of') + ' ' + docsState.total;

            var foot = '<div class="vas270-mtfoot"><span class="vas270-helper">' + escapeHtml(showingLabel) + '</span>' +
                (pages > 1 ?
                    '<span class="vas270-pager">' +
                        '<button type="button" class="vas270-pbtn" data-table="docs" data-dir="-1"' + (docsState.page === 0 ? ' disabled' : '') + ' aria-label="' + escapeHtml(label('VAS_270_PrevPage', 'Previous page')) + '">' + icon('prev') + '</button>' +
                        '<span class="vas270-ptxt">' + (docsState.page + 1) + ' ' + escapeHtml(label('VAS_270_Of', 'of')) + ' ' + pages + '</span>' +
                        '<button type="button" class="vas270-pbtn" data-table="docs" data-dir="1"' + (docsState.page >= pages - 1 ? ' disabled' : '') + ' aria-label="' + escapeHtml(label('VAS_270_NextPage', 'Next page')) + '">' + icon('next') + '</button>' +
                    '</span>' : '<span></span>') +
            '</div>';

            el.innerHTML = head + '<div class="vas270-mbody-rows">' + body + '</div>' + foot;
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
            if (line.QtyDelivered >= line.QtyOrdered && line.QtyOrdered > 0) { return { text: label('VAS_270_DeliveryFull', 'Delivered'), cls: 'vas270-chip-info' }; }
            if (line.QtyDelivered > 0) { return { text: label('VAS_270_LinePartial', 'Partly delivered'), cls: 'vas270-chip-warn' }; }
            return { text: label('VAS_270_LineInProcess', 'In process'), cls: 'vas270-chip-prop' };
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

        /* Sr 8.11: the document number and the Line Details icon both open this one
           "Sales order lines" pop-up (the separate record-preview screen is retired). */
        function lineFootHtml(order, lines) {
            // Ordered / shortage count Item-type product lines only (Sr 8.15);
            // shortage = pending - on hand, never negative.
            var qtyOrdered = 0, qtyShort = 0;
            lines.forEach(function (line) {
                if (!line.IsItem) { return; }
                qtyOrdered += Number(line.QtyOrdered) || 0;
                var pending = Number(line.QtyPending) || 0;
                var onHand = Math.max(0, Number(line.FreeStock) || 0);
                if (pending > onHand) { qtyShort += (pending - onHand); }
            });
            var footNote = lines.length + ' ' + label('VAS_270_LinesSuffix', 'lines') +
                ' · ' + formatQty(qtyOrdered) + ' ' + label('VAS_270_QtyOrderedSuffix', 'qty ordered') +
                ' · ' + order.DeliveryStatus +
                (qtyShort > 0 ? ' · ' + formatQty(qtyShort) + ' ' + label('VAS_270_QtyShortSuffix', 'qty short of stock') : '');

            return '<span class="vas270-foot-note">' + escapeHtml(footNote) + '</span>' +
                '<span><button type="button" class="vas270-btn" id="vas270-mback">' + escapeHtml(label('VAS_270_Back', 'Back')) + '</button> ' +
                '<button type="button" class="vas270-btn" id="vas270-mclose">' + escapeHtml(label('VAS_270_Close', 'Close')) + '</button></span>';
        }

        function openLinesModal(orderId) {
            showLoading(label('VAS_270_SalesOrderLines', 'Sales order lines') + '…');
            fetchOrderDetail(orderId, function (detail) {
                if (!detail) { showLoadError(); return; }
                showScreen(buildLinesCfg(detail.order, detail.lines), true);
            });
        }

        function buildLinesCfg(order, lines) {
            return {
                title: label('VAS_270_SalesOrderLines', 'Sales order lines') + ' · ' + order.SalesOrderNumber,
                subtitle: [order.CustomerName, formatDateShort(order.SalesOrderDate), order.DeliveryStatus].filter(function (p) { return p; }).join(' · '),
                size: '',   // same modal size as the documents list (Sr 8.18)
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

            var breadcrumb = '<div class="vas270-polink">' + escapeHtml(label('VAS_270_SalesOrderPrefix', 'Sales order')) +
                ' <strong>' + escapeHtml(order.SalesOrderNumber) + '</strong>' +
                ' · ' + escapeHtml(formatDateFull(order.SalesOrderDate)) + ' · ' + escapeHtml(order.DocumentStatus) + '</div>';

            var stats = '<div class="vas270-mstats">' +
                statTile(label('VAS_270_ColLine', 'Line') + 's', String(lines.length)) +
                statTile(label('VAS_270_SoValue', 'SO value'), formatMoney(order.OrderValue, order.CurrencySymbol, order.CurrencyPrecision)) +
                statTile(label('VAS_270_ColOrdered', 'Ordered'), formatQty(qtyOrdered)) +
                statTile(label('VAS_270_ColPending', 'Pending'), formatQty(qtyPending)) +
            '</div>';

            var secHtml = '<div class="vas270-msec">' + escapeHtml(label('VAS_270_SalesOrderLines', 'Sales order lines')) + '</div>';
            $mBody.html(breadcrumb + stats + secHtml + '<div class="vas270-mtwrap"><div class="vas270-mtbl" id="vas270-linetbl-lines"></div></div>');

            var showAttribute = lines.some(function (l) { return hasAttributeValue(l.AttributeText); });
            lineState = { order: order, lines: lines, page: 0, size: MAX_ROWS_PER_PAGE, tableId: 'vas270-linetbl-lines', showAttribute: showAttribute };
            drawLineTable('vas270-linetbl-lines');

            $mFoot.html(lineFootHtml(order, lines));
            $mFoot.find('#vas270-mback').on('click', backModal);
            $mFoot.find('#vas270-mclose').on('click', closeModal);
        }

        var LINE_COLS = [
            { key: 'no', label: label('VAS_270_ColLine', 'Line'), w: .35, align: 'right' },
            { key: 'product', label: label('VAS_270_ColProduct', 'Product'), w: 1.5 },
            { key: 'attr', label: label('VAS_270_ColAttribute', 'Attribute'), w: 1.1 },
            { key: 'uom', label: label('VAS_270_ColUom', 'UoM'), w: .5 },
            { key: 'ordered', label: label('VAS_270_ColOrdered', 'Ordered'), w: .7, align: 'right' },
            { key: 'delivered', label: label('VAS_270_ColDelivered', 'Delivered'), w: .7, align: 'right' },
            { key: 'pending', label: label('VAS_270_ColPending', 'Pending'), w: .7, align: 'right' },
            { key: 'instock', label: label('VAS_270_ColInStock', 'In stock'), w: .7, align: 'right' },
            { key: 'rate', label: label('VAS_270_ColRate', 'Rate'), w: .8, align: 'right' },
            { key: 'amount', label: label('VAS_270_ColAmount', 'Amount'), w: .9, align: 'right' },
            { key: 'status', label: label('VAS_270_ColLineStatus', 'Line status'), w: 1 }
        ];

        function drawLineTable(tableId) {
            var el = document.getElementById(tableId);
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

            var head = '<div class="vas270-mrow vas270-mhead-row" style="grid-template-columns:' + tpl + '">' +
                cols.map(function (c) { return '<span class="vas270-cell' + (c.align === 'right' ? ' right' : '') + '" title="' + escapeHtml(c.label) + '">' + escapeHtml(c.label) + '</span>'; }).join('') +
            '</div>';

            var body = slice.map(function (line) {
                var st = lineStatus(line);
                // Delivered / pending / in stock only for Item-type products (Sr 8.15);
                // charges and other product types show a dash.
                var isItem = !!line.IsItem;
                var cells = [
                    cellHtml(String(line.LineNo), 'vas270-c-std', 'right'),
                    cellHtml(line.ProductName, 'vas270-c-prim'),
                    showAttribute ? cellHtml(hasAttributeValue(line.AttributeText) ? line.AttributeText : '', 'vas270-c-std') : '',
                    cellHtml(line.UomName, 'vas270-c-std'),
                    cellHtml(formatQty(line.QtyOrdered), 'vas270-c-std', 'right'),
                    cellHtml(isItem ? formatQty(line.QtyDelivered) : '—', 'vas270-c-std', 'right'),
                    cellHtml(isItem ? formatQty(line.QtyPending) : '—', 'vas270-c-prim', 'right'),
                    cellHtml(isItem ? formatQty(line.FreeStock) : '—', (isItem && line.FreeStock < line.QtyPending ? 'vas270-c-short' : 'vas270-c-ok'), 'right'),
                    cellHtml(formatMoney(line.Rate, cur, 2), 'vas270-c-std', 'right'),
                    cellHtml(formatMoney(line.Amount, cur, prec), 'vas270-c-emph', 'right')
                ].join('') + '<span class="vas270-cell"><span class="vas270-chip ' + st.cls + '" title="' + escapeHtml(st.text) + '">' + escapeHtml(st.text) + '</span></span>';
                return '<div class="vas270-mrow" style="grid-template-columns:' + tpl + '">' + cells + '</div>';
            }).join('');

            var showingLabel = label('VAS_270_Showing', 'Showing') + ' ' + (lineState.lines.length ? (start + 1) : 0) + '–' + (start + slice.length) + ' ' + label('VAS_270_Of', 'of') + ' ' + lineState.lines.length;

            var foot = '<div class="vas270-mtfoot"><span class="vas270-helper">' + escapeHtml(showingLabel) + '</span>' +
                (pages > 1 ?
                    '<span class="vas270-pager">' +
                        '<button type="button" class="vas270-pbtn" data-table="lines" data-dir="-1"' + (lineState.page === 0 ? ' disabled' : '') + ' aria-label="' + escapeHtml(label('VAS_270_PrevPage', 'Previous page')) + '">' + icon('prev') + '</button>' +
                        '<span class="vas270-ptxt">' + (lineState.page + 1) + ' ' + escapeHtml(label('VAS_270_Of', 'of')) + ' ' + pages + '</span>' +
                        '<button type="button" class="vas270-pbtn" data-table="lines" data-dir="1"' + (lineState.page >= pages - 1 ? ' disabled' : '') + ' aria-label="' + escapeHtml(label('VAS_270_NextPage', 'Next page')) + '">' + icon('next') + '</button>' +
                    '</span>' : '<span></span>') +
            '</div>';

            el.innerHTML = head + '<div class="vas270-mbody-rows">' + body + '</div>' + foot;
        }

        /* Sizes each visible table's rows-per-page to the space actually left in
           the modal body, so the body never grows an inner scrollbar. The docs
           table is server-paginated, so a size change re-fetches the current
           page at the new size (clamped to the new page count) rather than
           forcing page 0; the guard on "n !== size" makes this converge instead
           of looping. */
        function fitTable(id, currentSize, onResize) {
            var el = document.getElementById(id);
            if (!el) { return; }
            var avail = el.clientHeight;
            if (avail < 40) { return; }
            var head = el.querySelector('.vas270-mhead-row');
            var foot = el.querySelector('.vas270-mtfoot');
            var row = el.querySelector('.vas270-mbody-rows .vas270-mrow');
            if (!head || !row) { return; }
            var rowHeight = row.getBoundingClientRect().height || 30;
            var used = head.getBoundingClientRect().height + (foot ? foot.getBoundingClientRect().height + 8 : 0);
            var n = Math.floor((avail - used) / rowHeight);
            n = Math.max(MIN_ROWS_PER_PAGE, Math.min(MAX_ROWS_PER_PAGE, n));
            if (n !== currentSize) { onResize(n); }
        }

        function fitAllTables() {
            if (!$mask.hasClass('is-open')) { return; }

            fitTable('vas270-docstbl', docsState.size, function (n) {
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
            var ns = '.vas270-' + ($self.AD_UserHomeWidgetID || $self.windowNo || 'widget') + '-' + instanceTag;
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
            loadSummary();
        };
    };

    VAS.VAS_270_SalesOrdersCompletedMTDWidget.prototype.widgetFirevalueChanged = function (value) {
        if (this.listener) { this.listener.widgetFirevalueChanged(value); }
    };

    VAS.VAS_270_SalesOrdersCompletedMTDWidget.prototype.addChangeListener = function (listener) {
        this.listener = listener;
    };

    VAS.VAS_270_SalesOrdersCompletedMTDWidget.prototype.init = function (windowNo, frame) {
        this.frame = frame;
        this.AD_UserHomeWidgetID = frame.widgetInfo.AD_UserHomeWidgetID;
        this.windowNo = windowNo;
        this.Initalize();
        this.frame.getContentGrid().append(this.getRoot());
        ensureDashInlineSizeVar(this.getRoot());
    };

    VAS.VAS_270_SalesOrdersCompletedMTDWidget.prototype.widgetSizeChange = function () { };

    VAS.VAS_270_SalesOrdersCompletedMTDWidget.prototype.dispose = function () {
        if (this.disposeComponent) { this.disposeComponent(); }
    };

})(VAS, jQuery);
