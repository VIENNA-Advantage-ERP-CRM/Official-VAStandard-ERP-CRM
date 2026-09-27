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
 * Modal   - Documents (top-level) -> Record / Lines (child, back-navigable), same
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

    var MAX_ROWS_PER_PAGE = 10;
    var MIN_ROWS_PER_PAGE = 2;
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
        var $mask, $modal, $mHead, $mTitle, $mSub, $mBack, $mBody, $mFoot;

        var SELF_ENDPOINT = 'VAS_271_SOsPendingDeliveryWidget/';

        var summary = null;           // last fetched GetSummary payload
        var summaryState = 'loading'; // 'loading' | 'ready' | 'error'
        var docsState = { page: 0, size: MAX_ROWS_PER_PAGE, total: 0, rows: [], stockUnavailable: false };
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
        // integer), matching every other widget on this dashboard (VAS_268/270 fmtINR).
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
            $tile.on('click', function () { openDocumentsModal(); });
            $root.append($tile);
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
            var $closeBtn = $('<button type="button" class="vas271-xbtn" aria-label="' + escapeHtml(label('VAS_271_Close', 'Close')) + '">' + icon('close') + '</button>');
            $mHead.append($htxt, $closeBtn);

            $mBody = $('<div class="vas271-mbody"></div>');
            $mFoot = $('<div class="vas271-mfoot"></div>');

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
            var ns = '.vas271-' + ($self.AD_UserHomeWidgetID || $self.windowNo || 'widget');

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
            if (summaryState === 'error') { return; }
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
                ShortfallQty: (row.ShortfallQty === null || row.ShortfallQty === undefined) ? null : Number(row.ShortfallQty),
                DeliveryStatus: row.DeliveryStatus || ''
            };
        }

        function stockCellHtml(row) {
            if (row.ShortfallQty === null) {
                return '<span class="vas271-cell" title="' + escapeHtml(label('VAS_271_StockUnavailableNote', 'Stock coverage could not be evaluated.')) + '">—</span>';
            }
            if (row.ShortfallQty <= 0) {
                var okText = label('VAS_271_StockCoverable', 'Coverable');
                return '<span class="vas271-cell" title="' + escapeHtml(okText) + '"><span class="vas271-chip vas271-chip-ok">' + escapeHtml(okText) + '</span></span>';
            }
            var shortText = label('VAS_271_StockShortPrefix', 'Short') + ' ' + formatNum(row.ShortfallQty);
            return '<span class="vas271-cell" title="' + escapeHtml(shortText) + '"><span class="vas271-chip vas271-chip-risk">' + escapeHtml(shortText) + '</span></span>';
        }

        function renderDocumentsBody() {
            var statsHtml = '<div class="vas271-mstats">' +
                statTile(label('VAS_271_StatOpenSOs', 'Open SOs'), formatNum(summary.OpenCount)) +
                statTile(label('VAS_271_StatItemsPending', 'Items pending'), formatNum(summary.PendingItems)) +
                statTile(label('VAS_271_StatUndeliveredValue', 'Undelivered value'), formatINR(summary.UndeliveredValue)) +
                statTile(label('VAS_271_StatPastDue', 'Past due'), formatNum(summary.PastDueCount)) +
            '</div>';

            var noteText = docsState.stockUnavailable
                ? label('VAS_271_StockUnavailableNote', 'Stock coverage could not be evaluated.')
                : label('VAS_271_StockNote', 'Stock column compares the pending quantity of every line against free stock at the ship-from warehouse.');
            var noteHtml = '<div class="vas271-mnote">' + escapeHtml(noteText) + '</div>';

            var secHtml = '<div class="vas271-msec">' + escapeHtml(label('VAS_271_SectionHeading', 'Sales orders awaiting delivery')) + '</div>';

            if (docsState.total <= 0) {
                $mBody.html(statsHtml + noteHtml + secHtml + '<div class="vas271-mstate">' + escapeHtml(label('VAS_271_ZeroState', 'Nothing pending — every order is delivered')) + '</div>');
            } else {
                $mBody.html(statsHtml + noteHtml + secHtml + '<div class="vas271-mtwrap"><div class="vas271-mtbl" id="vas271-docstbl"></div></div>');
                drawDocumentsTable();
            }
            $mFoot.html('<span class="vas271-foot-note"></span><span><button type="button" class="vas271-btn" id="vas271-docsclose">' + escapeHtml(label('VAS_271_Close', 'Close')) + '</button></span>');
            $mFoot.find('#vas271-docsclose').on('click', closeModal);
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
            { key: 'stock', label: label('VAS_271_ColStock', 'Stock'), w: .95 },
            { key: 'delivery', label: label('VAS_271_ColDelivery', 'Delivery'), w: 1 }
        ];

        function drawDocumentsTable() {
            var el = document.getElementById('vas271-docstbl');
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
                    cellHtml(formatNum(row.OrderedQty), 'vas271-c-std', 'right'),
                    cellHtml(formatNum(row.PendingQty), 'vas271-c-prim', 'right'),
                    cellHtml(formatINR(row.PendingValue), 'vas271-c-emph', 'right'),
                    cellHtml(row.PromisedDate ? formatDateFull(row.PromisedDate) : '—', 'vas271-c-std'),
                    stockCellHtml(row),
                    '<span class="vas271-cell" title="' + escapeHtml(row.DeliveryStatus) + '"><span class="vas271-chip vas271-chip-neutral">' + escapeHtml(row.DeliveryStatus) + '</span></span>'
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
            showLoading(label('VAS_271_Title', 'SOs Pending Delivery') + '…');
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
            return '<div class="vas271-mstats">' +
                statTile(label('VAS_271_Customer', 'Customer'), order.CustomerName) +
                statTile(label('VAS_271_SoDate', 'SO date'), formatDateFull(order.SalesOrderDate)) +
                statTile(label('VAS_271_DatePromised', 'Date promised'), formatDateFull(order.DatePromised)) +
                statTile(label('VAS_271_SoValue', 'SO value'), formatINR(order.OrderValue)) +
                statTile(label('VAS_271_ShipFrom', 'Ship from'), order.WarehouseName) +
                statTile(label('VAS_271_DeliveryMode', 'Delivery mode'), order.DeliveryMode) +
                statTile(label('VAS_271_DocumentStatus', 'Document status'), order.DocumentStatus) +
                statTile(label('VAS_271_DeliveryStatus', 'Delivery status'), order.DeliveryStatus) +
            '</div>';
        }

        function renderRecordBody(order, lines) {
            var secHtml = '<div class="vas271-msec">' + escapeHtml(label('VAS_271_SalesOrderLines', 'Sales order lines')) + '</div>';
            $mBody.html(soHeaderStats(order) + secHtml + '<div class="vas271-mtwrap"><div class="vas271-mtbl" id="vas271-linetbl-record"></div></div>');

            lineState = { order: order, lines: lines, page: 0, size: MAX_ROWS_PER_PAGE, tableId: 'vas271-linetbl-record' };
            drawLineTable('vas271-linetbl-record');

            $mFoot.html(lineFootHtml(order, lines));
            $mFoot.find('#vas271-mback').on('click', backModal);
            $mFoot.find('#vas271-mclose').on('click', closeModal);
        }

        function lineFootHtml(order, lines) {
            var qtyOrdered = 0, qtyShort = 0;
            lines.forEach(function (line) {
                qtyOrdered += Number(line.QtyOrdered) || 0;
                var pending = Number(line.QtyPending) || 0;
                var freeStock = Number(line.FreeStock) || 0;
                if (pending > freeStock) { qtyShort += (pending - freeStock); }
            });
            var footNote = lines.length + ' ' + label('VAS_271_LinesSuffix', 'lines') +
                ' · ' + formatNum(qtyOrdered) + ' ' + label('VAS_271_QtyOrderedSuffix', 'qty ordered') +
                ' · ' + order.DeliveryStatus +
                (qtyShort > 0 ? ' · ' + formatNum(qtyShort) + ' ' + label('VAS_271_QtyShortSuffix', 'qty short of stock') : '');

            return '<span class="vas271-foot-note">' + escapeHtml(footNote) + '</span>' +
                '<span><button type="button" class="vas271-btn" id="vas271-mback">' + escapeHtml(label('VAS_271_Back', 'Back')) + '</button> ' +
                '<button type="button" class="vas271-btn" id="vas271-mclose">' + escapeHtml(label('VAS_271_Close', 'Close')) + '</button></span>';
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
                size: 'md',
                render: function () { renderLinesBody(order, lines); }
            };
        }

        function renderLinesBody(order, lines) {
            var qtyOrdered = 0, qtyPending = 0;
            lines.forEach(function (l) { qtyOrdered += Number(l.QtyOrdered) || 0; qtyPending += Number(l.QtyPending) || 0; });

            var breadcrumb = '<div class="vas271-polink">' + escapeHtml(label('VAS_271_SalesOrderPrefix', 'Sales order')) +
                ' <button type="button" class="vas271-lnk" data-so="' + order.SalesOrderId + '">' + escapeHtml(order.SalesOrderNumber) + '</button>' +
                ' · ' + escapeHtml(formatDateFull(order.SalesOrderDate)) + ' · ' + escapeHtml(order.DocumentStatus) + '</div>';

            var stats = '<div class="vas271-mstats">' +
                statTile(label('VAS_271_ColLine', 'Line') + 's', String(lines.length)) +
                statTile(label('VAS_271_SoValue', 'SO value'), formatINR(order.OrderValue)) +
                statTile(label('VAS_271_ColOrdered', 'Ordered'), formatNum(qtyOrdered)) +
                statTile(label('VAS_271_ColPending', 'Pending'), formatNum(qtyPending)) +
            '</div>';

            var secHtml = '<div class="vas271-msec">' + escapeHtml(label('VAS_271_SalesOrderLines', 'Sales order lines')) + '</div>';
            $mBody.html(breadcrumb + stats + secHtml + '<div class="vas271-mtwrap"><div class="vas271-mtbl" id="vas271-linetbl-lines"></div></div>');

            lineState = { order: order, lines: lines, page: 0, size: MAX_ROWS_PER_PAGE, tableId: 'vas271-linetbl-lines' };
            drawLineTable('vas271-linetbl-lines');

            $mFoot.html(lineFootHtml(order, lines));
            $mFoot.find('#vas271-mback').on('click', backModal);
            $mFoot.find('#vas271-mclose').on('click', closeModal);
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
            { key: 'rate', label: label('VAS_271_ColRate', 'Rate'), w: .7, align: 'right' },
            { key: 'amount', label: label('VAS_271_ColAmount', 'Amount'), w: .9, align: 'right' },
            { key: 'status', label: label('VAS_271_ColLineStatus', 'Line status'), w: 1 }
        ];

        function drawLineTable(tableId) {
            var el = document.getElementById(tableId);
            if (!el || !lineState) { return; }

            var tpl = LINE_COLS.map(function (c) { return 'minmax(0,' + c.w + 'fr)'; }).join(' ');
            var pages = Math.max(1, Math.ceil(lineState.lines.length / lineState.size));
            if (lineState.page > pages - 1) { lineState.page = pages - 1; }
            var start = lineState.page * lineState.size;
            var slice = lineState.lines.slice(start, start + lineState.size);

            var head = '<div class="vas271-mrow vas271-mhead-row" style="grid-template-columns:' + tpl + '">' +
                LINE_COLS.map(function (c) { return '<span class="vas271-cell' + (c.align === 'right' ? ' right' : '') + '" title="' + escapeHtml(c.label) + '">' + escapeHtml(c.label) + '</span>'; }).join('') +
            '</div>';

            var body = slice.map(function (line) {
                var st = lineStatus(line);
                var cells = [
                    cellHtml(String(line.LineNo), 'vas271-c-std', 'right'),
                    cellHtml(line.ProductName, 'vas271-c-prim'),
                    cellHtml(line.AttributeText, 'vas271-c-std'),
                    cellHtml(line.UomName, 'vas271-c-std'),
                    cellHtml(formatNum(line.QtyOrdered), 'vas271-c-std', 'right'),
                    cellHtml(formatNum(line.QtyDelivered), 'vas271-c-std', 'right'),
                    cellHtml(formatNum(line.QtyPending), 'vas271-c-prim', 'right'),
                    cellHtml(formatNum(line.FreeStock), (line.FreeStock < line.QtyPending ? 'vas271-c-short' : 'vas271-c-ok'), 'right'),
                    cellHtml('₹ ' + formatNum(line.Rate), 'vas271-c-std', 'right'),
                    cellHtml(formatINR(line.Amount), 'vas271-c-emph', 'right')
                ].join('') + '<span class="vas271-cell"><span class="vas271-chip ' + st.cls + '" title="' + escapeHtml(st.text) + '">' + escapeHtml(st.text) + '</span></span>';
                return '<div class="vas271-mrow" style="grid-template-columns:' + tpl + '">' + cells + '</div>';
            }).join('');

            var showingLabel = label('VAS_271_Showing', 'Showing') + ' ' + (lineState.lines.length ? (start + 1) : 0) + '–' + (start + slice.length) + ' ' + label('VAS_271_Of', 'of') + ' ' + lineState.lines.length;

            var foot = '<div class="vas271-mtfoot"><span class="vas271-helper">' + escapeHtml(showingLabel) + '</span>' +
                (pages > 1 ?
                    '<span class="vas271-pager">' +
                        '<button type="button" class="vas271-pbtn" data-table="lines" data-dir="-1"' + (lineState.page === 0 ? ' disabled' : '') + ' aria-label="' + escapeHtml(label('VAS_271_PrevPage', 'Previous page')) + '">' + icon('prev') + '</button>' +
                        '<span class="vas271-ptxt">' + (lineState.page + 1) + ' ' + escapeHtml(label('VAS_271_Of', 'of')) + ' ' + pages + '</span>' +
                        '<button type="button" class="vas271-pbtn" data-table="lines" data-dir="1"' + (lineState.page >= pages - 1 ? ' disabled' : '') + ' aria-label="' + escapeHtml(label('VAS_271_NextPage', 'Next page')) + '">' + icon('next') + '</button>' +
                    '</span>' : '<span></span>') +
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
            var el = document.getElementById(id);
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
            var ns = '.vas271-' + ($self.AD_UserHomeWidgetID || $self.windowNo || 'widget');
            $(document).off(ns);
            $(window).off(ns);
            if ($mask) { $mask.remove(); }
            $root.remove();
        };

        this.refreshWidget = function () { loadSummary(); };
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
