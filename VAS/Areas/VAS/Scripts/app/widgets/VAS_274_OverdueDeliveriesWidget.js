/**
 * VAS_274 Overdue Deliveries Widget (Sales Order dashboard, 2x1 KPI tile - risk tone)
 * Purpose - Broken promises: open Sales Orders (DocStatus 'CO', a strict subset of
 *           VAS_271 SOs Pending Delivery) whose C_Order.DatePromised has already
 *           passed. The only risk-toned tile on this dashboard, deliberately
 *           restrained (tinted border and a red numeral only - no fill, icon, or
 *           animation), and the only tile whose tone FLIPS to "ok" when the count is
 *           zero (a red zero would be a bug, not a status). Click / Enter / Space
 *           opens a drill-down modal with a 4-card stat strip, an ageing note, and a
 *           paginated, most-overdue-first table naming a single blocking reason per
 *           row. SO number opens a record-preview child modal and the lines icon
 *           opens a lines-only child modal (both fed by one GetSalesOrderDetail
 *           fetch).
 *
 * Design  - 07-kpi-overdue-deliveries.html / .md: glass 2x1 KPI tile, stacked
 *           title/headline/meta, "risk" tone and border tint (flips to "ok" at zero).
 *           Modal shell, stat tiles, chips and paginated table match the same design
 *           language as VAS_270-273's shared Sales Order record-preview modal -
 *           reused here as this widget's own self-contained copy, matching how every
 *           widget controller/JS pair in this codebase owns its own modal
 *           implementation.
 *
 * Modal   - Documents (top-level) -> Record / Lines (child, back-navigable), same
 *           { title, subtitle, size, render } config-stack engine as VAS_270-273 -
 *           "back" pops the stack and re-runs the popped config's render() from its
 *           own already-fetched state, never stale HTML.
 *
 * Backend - VAS_274_OverdueDeliveriesWidget/GetSummary            (GET -> tile + stat-strip figures)
 *           VAS_274_OverdueDeliveriesWidget/GetOrders             (GET page,size -> paginated documents, most overdue first)
 *           VAS_274_OverdueDeliveriesWidget/GetSalesOrderDetail   (GET id -> order + lines)
 *
 * ── Labels / Message Keys ─────────────────────────────────────────────────
 *  #  | Current Text                                                          | Message Key
 * ----+------------------------------------------------------------------------+--------------------------------
 *  1  | Overdue Deliveries                                                    | VAS_274_Title
 *  2  | Open sales orders past the date promised                             | VAS_274_Subtitle
 *  3  | past date promised                                                    | VAS_274_PastDatePromisedSuffix
 *  4  | days late on average                                                  | VAS_274_DaysLateOnAverageSuffix
 *  5  | Every open order is within its promised date                         | VAS_274_ZeroState
 *  6  | Figures unavailable                                                   | VAS_274_ErrorState
 *  7  | Overdue SOs                                                           | VAS_274_StatOverdueSOs
 *  8  | Pending value                                                         | VAS_274_StatPendingValue
 *  9  | Avg days late                                                         | VAS_274_StatAvgDaysLate
 * 10  | Worst case                                                            | VAS_274_StatWorstCase
 * 11  | days                                                                  | VAS_274_DaysSuffix
 * 12  | Ageing is measured against the date promised on the sales order, not the requested date. | VAS_274_AgeingNote
 * 13  | Overdue sales orders                                                  | VAS_274_SectionHeading
 * 14  | most overdue first                                                    | VAS_274_MostOverdueFirst
 * 15  | SO No                                                                 | VAS_274_ColSoNo
 * 16  | Customer                                                              | VAS_274_Customer
 * 17  | Ship from                                                             | VAS_274_ShipFrom
 * 18  | Date promised                                                         | VAS_274_DatePromised
 * 19  | Days late                                                             | VAS_274_ColDaysLate
 * 20  | Pending items                                                         | VAS_274_ColPendingItems
 * 21  | Pending value                                                         | VAS_274_ColPendingValue
 * 22  | Reason                                                                | VAS_274_ColReason
 * 23  | Delivery                                                              | VAS_274_ColDelivery
 * 24  | d                                                                     | VAS_274_DaysLateSuffix
 * 25  | Stock short                                                           | VAS_274_ReasonStockShort
 * 26  | Credit hold                                                           | VAS_274_ReasonCreditHold
 * 27  | Transport pending                                                     | VAS_274_ReasonTransportPending
 * 28  | Pending                                                               | VAS_274_DeliveryPendingChip
 * 29  | Back                                                                  | VAS_274_Back
 * 30  | Close                                                                 | VAS_274_Close
 * 31  | SO date                                                               | VAS_274_SoDate
 * 32  | SO value                                                              | VAS_274_SoValue
 * 33  | Delivery mode                                                         | VAS_274_DeliveryMode
 * 34  | Document status                                                       | VAS_274_DocumentStatus
 * 35  | Delivery status                                                       | VAS_274_DeliveryStatus
 * 36  | Sales order lines                                                     | VAS_274_SalesOrderLines
 * 37  | Line                                                                  | VAS_274_ColLine
 * 38  | Product                                                               | VAS_274_ColProduct
 * 39  | Attribute                                                             | VAS_274_ColAttribute
 * 40  | UoM                                                                   | VAS_274_ColUom
 * 41  | Ordered                                                               | VAS_274_ColOrdered
 * 42  | Delivered                                                             | VAS_274_ColDelivered
 * 43  | Pending                                                               | VAS_274_ColPending
 * 44  | In stock                                                              | VAS_274_ColInStock
 * 45  | Rate                                                                  | VAS_274_ColRate
 * 46  | Amount                                                                | VAS_274_ColAmount
 * 47  | Line status                                                           | VAS_274_ColLineStatus
 * 48  | Delivered                                                             | VAS_274_DeliveryFull
 * 49  | Partially delivered                                                   | VAS_274_DeliveryPartial
 * 50  | Not delivered                                                         | VAS_274_DeliveryNone
 * 51  | Partly delivered                                                      | VAS_274_LinePartial
 * 52  | In process                                                            | VAS_274_LineInProcess
 * 53  | Sales order                                                           | VAS_274_SalesOrderPrefix
 * 54  | lines                                                                 | VAS_274_LinesSuffix
 * 55  | qty ordered                                                           | VAS_274_QtyOrderedSuffix
 * 56  | qty short of stock                                                    | VAS_274_QtyShortSuffix
 * 57  | Previous page                                                         | VAS_274_PrevPage
 * 58  | Next page                                                             | VAS_274_NextPage
 * 59  | of                                                                    | VAS_274_Of
 * 60  | Showing                                                               | VAS_274_Showing
 * 61  | Search is unavailable right now. Try again in a moment.               | VAS_274_LoadError
 */
; VAS = window.VAS || {};

; (function (VAS, $) {

    var MAX_ROWS_PER_PAGE = 10;
    var MIN_ROWS_PER_PAGE = 2;
    var DAYS_LATE_RISK_THRESHOLD = 10;
    var MONTHS = ['Jan', 'Feb', 'Mar', 'Apr', 'May', 'Jun', 'Jul', 'Aug', 'Sep', 'Oct', 'Nov', 'Dec'];

    /* design.md §Widget Header / §Measurement Setup: keep --dash-inline-size on
       :root equal to the dashboard container's current pixel width so the widget
       clamp resolves against the dashboard's visible width, not the viewport. A
       single document-level ResizeObserver serves every widget (matches VAS_269-273). */
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

    VAS.VAS_274_OverdueDeliveriesWidget = function () {

        this.frame;
        this.windowNo;

        var $self = this;
        var $root = $('<div class="vas274-root">');
        var $tile;
        var $mask, $modal, $mHead, $mTitle, $mSub, $mBack, $mBody, $mFoot;

        var SELF_ENDPOINT = 'VAS_274_OverdueDeliveriesWidget/';

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
        // integer), matching every other widget on this dashboard.
        function formatINR(value) {
            var num = Number(value || 0);
            if (num >= 1e7) { return '₹ ' + (num / 1e7).toFixed(2) + ' Cr'; }
            if (num >= 1e5) { return '₹ ' + (num / 1e5).toFixed(2) + ' L'; }
            return '₹ ' + Math.round(num).toLocaleString('en-IN');
        }

        function formatNum(value) {
            return Math.round(Number(value || 0)).toLocaleString('en-IN');
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

        function parseResponse(res) {
            var data = res;
            if (typeof data === 'string') { data = JSON.parse(data); }
            if (typeof data === 'string') { data = JSON.parse(data); }
            return data || {};
        }

        function cellHtml(value, cls, align) {
            return '<span class="vas274-cell' + (align === 'right' ? ' right' : '') + ' ' + cls + '" title="' + escapeHtml(value) + '">' + escapeHtml(value) + '</span>';
        }

        function statTile(l, v) {
            return '<div class="vas274-mstat"><div class="l">' + escapeHtml(l) + '</div><div class="v" title="' + escapeHtml(v) + '">' + escapeHtml(v) + '</div></div>';
        }

        /* ============================================================
         * Tile
         * ============================================================ */
        function createWidget() {
            $tile = $(
                '<button type="button" class="vas274-tile" aria-label="' + escapeHtml(label('VAS_274_Title', 'Overdue Deliveries')) + '">' +
                    '<p class="vas274-title">' + escapeHtml(label('VAS_274_Title', 'Overdue Deliveries')) + '</p>' +
                    '<div class="vas274-group">' +
                        '<p class="vas274-val"><span class="vas274-skel"></span></p>' +
                        '<p class="vas274-meta"></p>' +
                    '</div>' +
                '</button>'
            );
            $tile.on('click', function () { openDocumentsModal(); });
            $root.append($tile);
        }

        function renderTile() {
            var $val = $tile.find('.vas274-val');
            var $meta = $tile.find('.vas274-meta');

            if (summaryState === 'loading') {
                $val.html('<span class="vas274-skel"></span>');
                $meta.text('');
                $tile.removeAttr('aria-disabled');
                $tile.removeClass('vas274-border-ok');
                return;
            }
            if (summaryState === 'error' || !summary) {
                $val.removeClass().addClass('vas274-val error').text('—');
                $meta.text(label('VAS_274_ErrorState', 'Figures unavailable'));
                $tile.attr('aria-disabled', 'true');
                $tile.removeClass('vas274-border-ok');
                return;
            }

            $tile.removeAttr('aria-disabled');
            var isZero = summary.OverdueCount <= 0;
            $tile.toggleClass('vas274-border-ok', isZero);
            $val.removeClass().addClass('vas274-val ' + (isZero ? 'ok' : 'risk')).text(formatNum(summary.OverdueCount));

            if (isZero) {
                $meta.text(label('VAS_274_ZeroState', 'Every open order is within its promised date'));
                return;
            }

            var meta = formatINR(summary.PendingValue) + ' ' + label('VAS_274_PastDatePromisedSuffix', 'past date promised') +
                ' · ' + formatNum(Math.round(summary.AvgDaysLate)) + ' ' + label('VAS_274_DaysLateOnAverageSuffix', 'days late on average');
            $meta.attr('title', meta).text(meta);
        }

        function loadSummary(done) {
            summaryState = 'loading';
            renderTile();

            $.ajax({
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
            $mask = $('<div class="vas274-mask" role="dialog" aria-modal="true"></div>');
            $modal = $('<div class="vas274-modal"></div>');
            $mHead = $('<div class="vas274-mhead"></div>');
            var $htxt = $('<div class="vas274-htxt"></div>');
            $mBack = $('<button type="button" class="vas274-xbtn" aria-label="' + escapeHtml(label('VAS_274_Back', 'Back')) + '" hidden>' + icon('back') + '</button>');
            var $titleWrap = $('<div></div>');
            $mTitle = $('<h2></h2>');
            $mSub = $('<div class="vas274-msub"></div>');
            $titleWrap.append($mTitle, $mSub);
            $htxt.append($mBack, $titleWrap);
            var $closeBtn = $('<button type="button" class="vas274-xbtn" aria-label="' + escapeHtml(label('VAS_274_Close', 'Close')) + '">' + icon('close') + '</button>');
            $mHead.append($htxt, $closeBtn);

            $mBody = $('<div class="vas274-mbody"></div>');
            $mFoot = $('<div class="vas274-mfoot"></div>');

            $modal.append($mHead, $mBody, $mFoot);
            $mask.append($modal);
            $('body').append($mask);

            $closeBtn.on('click', closeModal);
            $mBack.on('click', backModal);
            $mBody.on('click', function (event) {
                var pageBtn = event.target.closest ? event.target.closest('[data-dir]') : null;
                if (pageBtn) { turnPage(pageBtn.getAttribute('data-table'), Number(pageBtn.getAttribute('data-dir'))); return; }
                var soBtn = event.target.closest ? event.target.closest('[data-so]') : null;
                if (soBtn) { openRecordModal(Number(soBtn.getAttribute('data-so'))); return; }
                var linesBtn = event.target.closest ? event.target.closest('[data-lines]') : null;
                if (linesBtn) { openLinesModal(Number(linesBtn.getAttribute('data-lines'))); return; }
            });
        }

        function bindDocumentLevelEvents() {
            var ns = '.vas274-' + ($self.AD_UserHomeWidgetID || $self.windowNo || 'widget');

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
            $mBody.html('<div class="vas274-mstate">…</div>');
            $mFoot.html('');
            $mask.addClass('is-open');
        }

        function showLoadError() {
            $mBody.html('<div class="vas274-mstate">' + escapeHtml(label('VAS_274_LoadError', 'Search is unavailable right now. Try again in a moment.')) + '</div>');
        }

        /* ============================================================
         * Documents list (top-level modal)
         * ============================================================ */
        function buildDocsCfg() {
            return { title: label('VAS_274_Title', 'Overdue Deliveries'), subtitle: label('VAS_274_Subtitle', 'Open sales orders past the date promised'), size: '', render: renderDocumentsBody };
        }

        function openDocumentsModal() {
            if (summaryState === 'error') { return; }
            showLoading(label('VAS_274_Title', 'Overdue Deliveries'));

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
            $.ajax({
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
                CustomerName: row.CustomerName || '',
                WarehouseName: row.WarehouseName || '',
                PromisedDate: row.PromisedDate || '',
                DaysLate: Number(row.DaysLate) || 0,
                PendingQty: Number(row.PendingQty) || 0,
                PendingValue: Number(row.PendingValue) || 0,
                BlockReason: row.BlockReason || '',
                DeliveryStatus: row.DeliveryStatus || ''
            };
        }

        function daysLateChipClass(daysLate) {
            return daysLate > DAYS_LATE_RISK_THRESHOLD ? 'vas274-chip-risk' : 'vas274-chip-warn';
        }

        function renderDocumentsBody() {
            var statsHtml = '<div class="vas274-mstats">' +
                statTile(label('VAS_274_StatOverdueSOs', 'Overdue SOs'), formatNum(summary.OverdueCount)) +
                statTile(label('VAS_274_StatPendingValue', 'Pending value'), formatINR(summary.PendingValue)) +
                statTile(label('VAS_274_StatAvgDaysLate', 'Avg days late'), formatNum(Math.round(summary.AvgDaysLate)) + ' ' + label('VAS_274_DaysSuffix', 'days')) +
                statTile(label('VAS_274_StatWorstCase', 'Worst case'), formatNum(summary.MaxDaysLate) + ' ' + label('VAS_274_DaysSuffix', 'days')) +
            '</div>';

            var noteHtml = '<div class="vas274-mnote">' + escapeHtml(label('VAS_274_AgeingNote', 'Ageing is measured against the date promised on the sales order, not the requested date.')) + '</div>';
            var secHtml = '<div class="vas274-msec">' + escapeHtml(label('VAS_274_SectionHeading', 'Overdue sales orders')) + '</div>';

            if (docsState.total <= 0) {
                $mBody.html(statsHtml + noteHtml + secHtml + '<div class="vas274-mstate">' + escapeHtml(label('VAS_274_ZeroState', 'Every open order is within its promised date')) + '</div>');
            } else {
                $mBody.html(statsHtml + noteHtml + secHtml + '<div class="vas274-mtwrap"><div class="vas274-mtbl" id="vas274-docstbl"></div></div>');
                drawDocumentsTable();
            }
            $mFoot.html('<span class="vas274-foot-note"></span><span><button type="button" class="vas274-btn" id="vas274-docsclose">' + escapeHtml(label('VAS_274_Close', 'Close')) + '</button></span>');
            $mFoot.find('#vas274-docsclose').on('click', closeModal);
        }

        var DOC_COLS = [
            { key: 'icon', label: '', w: .32 },
            { key: 'so', label: label('VAS_274_ColSoNo', 'SO No'), w: 1.1 },
            { key: 'customer', label: label('VAS_274_Customer', 'Customer'), w: 1.6 },
            { key: 'warehouse', label: label('VAS_274_ShipFrom', 'Ship from'), w: 1.1 },
            { key: 'promised', label: label('VAS_274_DatePromised', 'Date promised'), w: 1 },
            { key: 'dayslate', label: label('VAS_274_ColDaysLate', 'Days late'), w: .8, align: 'right' },
            { key: 'pendingqty', label: label('VAS_274_ColPendingItems', 'Pending items'), w: .9, align: 'right' },
            { key: 'pendingvalue', label: label('VAS_274_ColPendingValue', 'Pending value'), w: 1, align: 'right' },
            { key: 'reason', label: label('VAS_274_ColReason', 'Reason'), w: 1.15 },
            { key: 'delivery', label: label('VAS_274_ColDelivery', 'Delivery'), w: 1.0 }
        ];

        function drawDocumentsTable() {
            var el = document.getElementById('vas274-docstbl');
            if (!el) { return; }
            var tpl = DOC_COLS.map(function (c) { return 'minmax(0,' + c.w + 'fr)'; }).join(' ');
            var pages = Math.max(1, Math.ceil(docsState.total / docsState.size));

            var head = '<div class="vas274-mrow vas274-mhead-row" style="grid-template-columns:' + tpl + '">' +
                DOC_COLS.map(function (c) { return '<span class="vas274-cell' + (c.align === 'right' ? ' right' : '') + '" title="' + escapeHtml(c.label) + '">' + escapeHtml(c.label) + '</span>'; }).join('') +
            '</div>';

            var body = docsState.rows.map(function (row) {
                var daysLateText = row.DaysLate + ' ' + label('VAS_274_DaysLateSuffix', 'd');
                var cells = [
                    '<span class="vas274-cell center"><button type="button" class="vas274-iconbtn" data-lines="' + row.SalesOrderId + '" title="' + escapeHtml(row.SalesOrderNumber) + '">' + icon('lines') + '</button></span>',
                    '<span class="vas274-cell"><button type="button" class="vas274-lnk" data-so="' + row.SalesOrderId + '" title="' + escapeHtml(row.SalesOrderNumber) + '">' + escapeHtml(row.SalesOrderNumber) + '</button></span>',
                    cellHtml(row.CustomerName, 'vas274-c-prim'),
                    cellHtml(row.WarehouseName, 'vas274-c-std'),
                    cellHtml(formatDateFull(row.PromisedDate), 'vas274-c-std'),
                    '<span class="vas274-cell right" title="' + escapeHtml(daysLateText) + '"><span class="vas274-chip ' + daysLateChipClass(row.DaysLate) + '">' + escapeHtml(daysLateText) + '</span></span>',
                    cellHtml(formatNum(row.PendingQty), 'vas274-c-prim', 'right'),
                    cellHtml(formatINR(row.PendingValue), 'vas274-c-emph', 'right'),
                    cellHtml(row.BlockReason, 'vas274-c-std'),
                    '<span class="vas274-cell" title="' + escapeHtml(row.DeliveryStatus) + '"><span class="vas274-chip vas274-chip-neutral">' + escapeHtml(row.DeliveryStatus) + '</span></span>'
                ];
                return '<div class="vas274-mrow" style="grid-template-columns:' + tpl + '">' + cells.join('') + '</div>';
            }).join('');

            var start = docsState.page * docsState.size;
            var showingLabel = label('VAS_274_Showing', 'Showing') + ' ' + (docsState.total ? (start + 1) : 0) + '–' + (start + docsState.rows.length) + ' ' + label('VAS_274_Of', 'of') + ' ' + docsState.total + ' · ' + label('VAS_274_MostOverdueFirst', 'most overdue first');

            var foot = '<div class="vas274-mtfoot"><span class="vas274-helper">' + escapeHtml(showingLabel) + '</span>' +
                (pages > 1 ?
                    '<span class="vas274-pager">' +
                        '<button type="button" class="vas274-pbtn" data-table="docs" data-dir="-1"' + (docsState.page === 0 ? ' disabled' : '') + ' aria-label="' + escapeHtml(label('VAS_274_PrevPage', 'Previous page')) + '">' + icon('prev') + '</button>' +
                        '<span class="vas274-ptxt">' + (docsState.page + 1) + ' ' + escapeHtml(label('VAS_274_Of', 'of')) + ' ' + pages + '</span>' +
                        '<button type="button" class="vas274-pbtn" data-table="docs" data-dir="1"' + (docsState.page >= pages - 1 ? ' disabled' : '') + ' aria-label="' + escapeHtml(label('VAS_274_NextPage', 'Next page')) + '">' + icon('next') + '</button>' +
                    '</span>' : '<span></span>') +
            '</div>';

            el.innerHTML = head + '<div class="vas274-mbody-rows">' + body + '</div>' + foot;
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
            if (line.QtyDelivered >= line.QtyOrdered && line.QtyOrdered > 0) { return { text: label('VAS_274_DeliveryFull', 'Delivered'), cls: 'vas274-chip-info' }; }
            if (line.QtyDelivered > 0) { return { text: label('VAS_274_LinePartial', 'Partly delivered'), cls: 'vas274-chip-warn' }; }
            return { text: label('VAS_274_LineInProcess', 'In process'), cls: 'vas274-chip-prop' };
        }

        function fetchOrderDetail(orderId, cb) {
            $.ajax({
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
            showLoading(label('VAS_274_Title', 'Overdue Deliveries') + '…');
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
            return '<div class="vas274-mstats">' +
                statTile(label('VAS_274_Customer', 'Customer'), order.CustomerName) +
                statTile(label('VAS_274_SoDate', 'SO date'), formatDateFull(order.SalesOrderDate)) +
                statTile(label('VAS_274_DatePromised', 'Date promised'), formatDateFull(order.DatePromised)) +
                statTile(label('VAS_274_SoValue', 'SO value'), formatINR(order.OrderValue)) +
                statTile(label('VAS_274_ShipFrom', 'Ship from'), order.WarehouseName) +
                statTile(label('VAS_274_DeliveryMode', 'Delivery mode'), order.DeliveryMode) +
                statTile(label('VAS_274_DocumentStatus', 'Document status'), order.DocumentStatus) +
                statTile(label('VAS_274_DeliveryStatus', 'Delivery status'), order.DeliveryStatus) +
            '</div>';
        }

        function renderRecordBody(order, lines) {
            var secHtml = '<div class="vas274-msec">' + escapeHtml(label('VAS_274_SalesOrderLines', 'Sales order lines')) + '</div>';
            $mBody.html(soHeaderStats(order) + secHtml + '<div class="vas274-mtwrap"><div class="vas274-mtbl" id="vas274-linetbl-record"></div></div>');

            lineState = { order: order, lines: lines, page: 0, size: MAX_ROWS_PER_PAGE, tableId: 'vas274-linetbl-record' };
            drawLineTable('vas274-linetbl-record');

            $mFoot.html(lineFootHtml(order, lines));
            $mFoot.find('#vas274-mback').on('click', backModal);
            $mFoot.find('#vas274-mclose').on('click', closeModal);
        }

        function lineFootHtml(order, lines) {
            var qtyOrdered = 0, qtyShort = 0;
            lines.forEach(function (line) {
                qtyOrdered += Number(line.QtyOrdered) || 0;
                var pending = Number(line.QtyPending) || 0;
                var freeStock = Number(line.FreeStock) || 0;
                if (pending > freeStock) { qtyShort += (pending - freeStock); }
            });
            var footNote = lines.length + ' ' + label('VAS_274_LinesSuffix', 'lines') +
                ' · ' + formatNum(qtyOrdered) + ' ' + label('VAS_274_QtyOrderedSuffix', 'qty ordered') +
                ' · ' + order.DeliveryStatus +
                (qtyShort > 0 ? ' · ' + formatNum(qtyShort) + ' ' + label('VAS_274_QtyShortSuffix', 'qty short of stock') : '');

            return '<span class="vas274-foot-note">' + escapeHtml(footNote) + '</span>' +
                '<span><button type="button" class="vas274-btn" id="vas274-mback">' + escapeHtml(label('VAS_274_Back', 'Back')) + '</button> ' +
                '<button type="button" class="vas274-btn" id="vas274-mclose">' + escapeHtml(label('VAS_274_Close', 'Close')) + '</button></span>';
        }

        function openLinesModal(orderId) {
            showLoading(label('VAS_274_SalesOrderLines', 'Sales order lines') + '…');
            fetchOrderDetail(orderId, function (detail) {
                if (!detail) { showLoadError(); return; }
                showScreen(buildLinesCfg(detail.order, detail.lines), true);
            });
        }

        function buildLinesCfg(order, lines) {
            return {
                title: label('VAS_274_SalesOrderLines', 'Sales order lines') + ' · ' + order.SalesOrderNumber,
                subtitle: [order.CustomerName, formatDateShort(order.SalesOrderDate), order.DeliveryStatus].filter(function (p) { return p; }).join(' · '),
                size: 'md',
                render: function () { renderLinesBody(order, lines); }
            };
        }

        function renderLinesBody(order, lines) {
            var qtyOrdered = 0, qtyPending = 0;
            lines.forEach(function (l) { qtyOrdered += Number(l.QtyOrdered) || 0; qtyPending += Number(l.QtyPending) || 0; });

            var breadcrumb = '<div class="vas274-polink">' + escapeHtml(label('VAS_274_SalesOrderPrefix', 'Sales order')) +
                ' <button type="button" class="vas274-lnk" data-so="' + order.SalesOrderId + '">' + escapeHtml(order.SalesOrderNumber) + '</button>' +
                ' · ' + escapeHtml(formatDateFull(order.SalesOrderDate)) + ' · ' + escapeHtml(order.DocumentStatus) + '</div>';

            var stats = '<div class="vas274-mstats">' +
                statTile(label('VAS_274_ColLine', 'Line') + 's', String(lines.length)) +
                statTile(label('VAS_274_SoValue', 'SO value'), formatINR(order.OrderValue)) +
                statTile(label('VAS_274_ColOrdered', 'Ordered'), formatNum(qtyOrdered)) +
                statTile(label('VAS_274_ColPending', 'Pending'), formatNum(qtyPending)) +
            '</div>';

            var secHtml = '<div class="vas274-msec">' + escapeHtml(label('VAS_274_SalesOrderLines', 'Sales order lines')) + '</div>';
            $mBody.html(breadcrumb + stats + secHtml + '<div class="vas274-mtwrap"><div class="vas274-mtbl" id="vas274-linetbl-lines"></div></div>');

            lineState = { order: order, lines: lines, page: 0, size: MAX_ROWS_PER_PAGE, tableId: 'vas274-linetbl-lines' };
            drawLineTable('vas274-linetbl-lines');

            $mFoot.html(lineFootHtml(order, lines));
            $mFoot.find('#vas274-mback').on('click', backModal);
            $mFoot.find('#vas274-mclose').on('click', closeModal);
        }

        var LINE_COLS = [
            { key: 'no', label: label('VAS_274_ColLine', 'Line'), w: .35, align: 'right' },
            { key: 'product', label: label('VAS_274_ColProduct', 'Product'), w: 1.5 },
            { key: 'attr', label: label('VAS_274_ColAttribute', 'Attribute'), w: 1.1 },
            { key: 'uom', label: label('VAS_274_ColUom', 'UoM'), w: .5 },
            { key: 'ordered', label: label('VAS_274_ColOrdered', 'Ordered'), w: .7, align: 'right' },
            { key: 'delivered', label: label('VAS_274_ColDelivered', 'Delivered'), w: .7, align: 'right' },
            { key: 'pending', label: label('VAS_274_ColPending', 'Pending'), w: .7, align: 'right' },
            { key: 'instock', label: label('VAS_274_ColInStock', 'In stock'), w: .7, align: 'right' },
            { key: 'rate', label: label('VAS_274_ColRate', 'Rate'), w: .7, align: 'right' },
            { key: 'amount', label: label('VAS_274_ColAmount', 'Amount'), w: .9, align: 'right' },
            { key: 'status', label: label('VAS_274_ColLineStatus', 'Line status'), w: 1 }
        ];

        function drawLineTable(tableId) {
            var el = document.getElementById(tableId);
            if (!el || !lineState) { return; }

            var tpl = LINE_COLS.map(function (c) { return 'minmax(0,' + c.w + 'fr)'; }).join(' ');
            var pages = Math.max(1, Math.ceil(lineState.lines.length / lineState.size));
            if (lineState.page > pages - 1) { lineState.page = pages - 1; }
            var start = lineState.page * lineState.size;
            var slice = lineState.lines.slice(start, start + lineState.size);

            var head = '<div class="vas274-mrow vas274-mhead-row" style="grid-template-columns:' + tpl + '">' +
                LINE_COLS.map(function (c) { return '<span class="vas274-cell' + (c.align === 'right' ? ' right' : '') + '" title="' + escapeHtml(c.label) + '">' + escapeHtml(c.label) + '</span>'; }).join('') +
            '</div>';

            var body = slice.map(function (line) {
                var st = lineStatus(line);
                var cells = [
                    cellHtml(String(line.LineNo), 'vas274-c-std', 'right'),
                    cellHtml(line.ProductName, 'vas274-c-prim'),
                    cellHtml(line.AttributeText, 'vas274-c-std'),
                    cellHtml(line.UomName, 'vas274-c-std'),
                    cellHtml(formatNum(line.QtyOrdered), 'vas274-c-std', 'right'),
                    cellHtml(formatNum(line.QtyDelivered), 'vas274-c-std', 'right'),
                    cellHtml(formatNum(line.QtyPending), 'vas274-c-prim', 'right'),
                    cellHtml(formatNum(line.FreeStock), (line.FreeStock < line.QtyPending ? 'vas274-c-short' : 'vas274-c-ok'), 'right'),
                    cellHtml('₹ ' + formatNum(line.Rate), 'vas274-c-std', 'right'),
                    cellHtml(formatINR(line.Amount), 'vas274-c-emph', 'right')
                ].join('') + '<span class="vas274-cell"><span class="vas274-chip ' + st.cls + '" title="' + escapeHtml(st.text) + '">' + escapeHtml(st.text) + '</span></span>';
                return '<div class="vas274-mrow" style="grid-template-columns:' + tpl + '">' + cells + '</div>';
            }).join('');

            var showingLabel = label('VAS_274_Showing', 'Showing') + ' ' + (lineState.lines.length ? (start + 1) : 0) + '–' + (start + slice.length) + ' ' + label('VAS_274_Of', 'of') + ' ' + lineState.lines.length;

            var foot = '<div class="vas274-mtfoot"><span class="vas274-helper">' + escapeHtml(showingLabel) + '</span>' +
                (pages > 1 ?
                    '<span class="vas274-pager">' +
                        '<button type="button" class="vas274-pbtn" data-table="lines" data-dir="-1"' + (lineState.page === 0 ? ' disabled' : '') + ' aria-label="' + escapeHtml(label('VAS_274_PrevPage', 'Previous page')) + '">' + icon('prev') + '</button>' +
                        '<span class="vas274-ptxt">' + (lineState.page + 1) + ' ' + escapeHtml(label('VAS_274_Of', 'of')) + ' ' + pages + '</span>' +
                        '<button type="button" class="vas274-pbtn" data-table="lines" data-dir="1"' + (lineState.page >= pages - 1 ? ' disabled' : '') + ' aria-label="' + escapeHtml(label('VAS_274_NextPage', 'Next page')) + '">' + icon('next') + '</button>' +
                    '</span>' : '<span></span>') +
            '</div>';

            el.innerHTML = head + '<div class="vas274-mbody-rows">' + body + '</div>' + foot;
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
            var head = el.querySelector('.vas274-mhead-row');
            var foot = el.querySelector('.vas274-mtfoot');
            var row = el.querySelector('.vas274-mbody-rows .vas274-mrow');
            if (!head || !row) { return; }
            var rowHeight = row.getBoundingClientRect().height || 30;
            var used = head.getBoundingClientRect().height + (foot ? foot.getBoundingClientRect().height + 8 : 0);
            var n = Math.floor((avail - used) / rowHeight);
            n = Math.max(MIN_ROWS_PER_PAGE, Math.min(MAX_ROWS_PER_PAGE, n));
            if (n !== currentSize) { onResize(n); }
        }

        function fitAllTables() {
            if (!$mask.hasClass('is-open')) { return; }

            fitTable('vas274-docstbl', docsState.size, function (n) {
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
            var ns = '.vas274-' + ($self.AD_UserHomeWidgetID || $self.windowNo || 'widget');
            $(document).off(ns);
            $(window).off(ns);
            if ($mask) { $mask.remove(); }
            $root.remove();
        };

        this.refreshWidget = function () { loadSummary(); };
    };

    VAS.VAS_274_OverdueDeliveriesWidget.prototype.widgetFirevalueChanged = function (value) {
        if (this.listener) { this.listener.widgetFirevalueChanged(value); }
    };

    VAS.VAS_274_OverdueDeliveriesWidget.prototype.addChangeListener = function (listener) {
        this.listener = listener;
    };

    VAS.VAS_274_OverdueDeliveriesWidget.prototype.init = function (windowNo, frame) {
        this.frame = frame;
        this.AD_UserHomeWidgetID = frame.widgetInfo.AD_UserHomeWidgetID;
        this.windowNo = windowNo;
        this.Initalize();
        this.frame.getContentGrid().append(this.getRoot());
        ensureDashInlineSizeVar(this.getRoot());
    };

    VAS.VAS_274_OverdueDeliveriesWidget.prototype.widgetSizeChange = function () { };

    VAS.VAS_274_OverdueDeliveriesWidget.prototype.dispose = function () {
        if (this.disposeComponent) { this.disposeComponent(); }
    };

})(VAS, jQuery);
