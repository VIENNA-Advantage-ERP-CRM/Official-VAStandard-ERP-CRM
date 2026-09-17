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

    var MAX_ROWS_PER_PAGE = 10;
    var MIN_ROWS_PER_PAGE = 2;
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

        var summary = null;           // last fetched GetSummary payload
        var summaryState = 'loading'; // 'loading' | 'ready' | 'error'
        var docsState = { page: 0, size: MAX_ROWS_PER_PAGE, total: 0, rows: [] };
        var lineState = null;         // { order, lines, page, size, tableId }
        var cfgStack = [];
        var currentCfg = null;

        function label(key, fallback) {
            var translated = VIS.Msg.getMsg(key);
            return (translated && translated.charAt(0) !== '[') ? translated : fallback;
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

        function formatSignedPercent(value) {
            var num = Number(value || 0);
            var sign = num > 0 ? '+' : (num < 0 ? '' : '+');
            return sign + num.toFixed(1) + '%';
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

        function formatMonthYear(iso) {
            var d = parseIso(iso);
            if (!d) { return ''; }
            return MONTHS[d.getMonth()] + ' ' + d.getFullYear();
        }

        // "1-10 Aug 2026" style window - MTD always starts on the 1st of the month
        // PeriodEnd falls in.
        function formatPeriod(startIso, endIso) {
            var start = parseIso(startIso), end = parseIso(endIso);
            if (!start || !end) { return ''; }
            return start.getDate() + '–' + end.getDate() + ' ' + MONTHS[end.getMonth()] + ' ' + end.getFullYear();
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
            $tile.on('click', function () { openDocumentsModal(); });
            $root.append($tile);
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
                $tile.attr('aria-disabled', 'true');
                return;
            }

            $tile.removeAttr('aria-disabled');
            $val.removeClass().addClass('vas273-val info').text(formatINR(summary.OrderValue));

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
            var $closeBtn = $('<button type="button" class="vas273-xbtn" aria-label="' + escapeHtml(label('VAS_273_Close', 'Close')) + '">' + icon('close') + '</button>');
            $mHead.append($htxt, $closeBtn);

            $mBody = $('<div class="vas273-mbody"></div>');
            $mFoot = $('<div class="vas273-mfoot"></div>');

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
            var ns = '.vas273-' + ($self.AD_UserHomeWidgetID || $self.windowNo || 'widget');

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
            if (summaryState === 'error') { return; }
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
                SalesOrderDate: row.SalesOrderDate || '',
                CustomerName: row.CustomerName || '',
                RepresentativeName: row.RepresentativeName || '',
                LineCount: Number(row.LineCount) || 0,
                OrderQty: Number(row.OrderQty) || 0,
                OrderValue: Number(row.OrderValue) || 0,
                DocumentStatusCode: row.DocumentStatusCode || '',
                DocumentStatus: row.DocumentStatus || ''
            };
        }

        function renderDocumentsBody() {
            var momValue = (summary.MomPercent !== null && summary.MomPercent !== undefined) ? formatSignedPercent(summary.MomPercent) : '—';
            var momLabel = label('VAS_273_VsPrefix', 'vs') + ' ' + formatMonthYear(summary.ComparisonMonthStart);

            var statsHtml = '<div class="vas273-mstats">' +
                statTile(label('VAS_273_StatOrderValue', 'Order value'), formatINR(summary.OrderValue)) +
                statTile(label('VAS_273_StatSalesOrders', 'Sales orders'), formatNum(summary.OrderCount)) +
                statTile(label('VAS_273_StatAvgOrderValue', 'Avg order value'), formatINR(summary.AvgOrderValue)) +
                statTile(momLabel, momValue) +
                statTile(label('VAS_273_StatCustomersBilled', 'Customers billed'), formatNum(summary.CustomersBilled)) +
                statTile(label('VAS_273_StatNewCustomers', 'New customers'), formatNum(summary.NewCustomers)) +
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
            var el = document.getElementById('vas273-docstbl');
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
                    cellHtml(formatINR(row.OrderValue), 'vas273-c-emph', 'right'),
                    '<span class="vas273-cell" title="' + escapeHtml(row.DocumentStatus) + '"><span class="vas273-chip vas273-chip-ok">' + escapeHtml(row.DocumentStatus) + '</span></span>'
                ];
                return '<div class="vas273-mrow" style="grid-template-columns:' + tpl + '">' + cells.join('') + '</div>';
            }).join('');

            var start = docsState.page * docsState.size;
            var showingLabel = label('VAS_273_Showing', 'Showing') + ' ' + (docsState.total ? (start + 1) : 0) + '–' + (start + docsState.rows.length) + ' ' + label('VAS_273_Of', 'of') + ' ' + docsState.total + ' · ' + label('VAS_273_NewestFirst', 'newest first');

            var foot = '<div class="vas273-mtfoot"><span class="vas273-helper">' + escapeHtml(showingLabel) + '</span>' +
                (pages > 1 ?
                    '<span class="vas273-pager">' +
                        '<button type="button" class="vas273-pbtn" data-table="docs" data-dir="-1"' + (docsState.page === 0 ? ' disabled' : '') + ' aria-label="' + escapeHtml(label('VAS_273_PrevPage', 'Previous page')) + '">' + icon('prev') + '</button>' +
                        '<span class="vas273-ptxt">' + (docsState.page + 1) + ' ' + escapeHtml(label('VAS_273_Of', 'of')) + ' ' + pages + '</span>' +
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
                statTile(label('VAS_273_SoValue', 'SO value'), formatINR(order.OrderValue)) +
                statTile(label('VAS_273_ShipFrom', 'Ship from'), order.WarehouseName) +
                statTile(label('VAS_273_DeliveryMode', 'Delivery mode'), order.DeliveryMode) +
                statTile(label('VAS_273_DocumentStatus', 'Document status'), order.DocumentStatus) +
                statTile(label('VAS_273_DeliveryStatus', 'Delivery status'), order.DeliveryStatus) +
            '</div>';
        }

        function renderRecordBody(order, lines) {
            var secHtml = '<div class="vas273-msec">' + escapeHtml(label('VAS_273_SalesOrderLines', 'Sales order lines')) + '</div>';
            $mBody.html(soHeaderStats(order) + secHtml + '<div class="vas273-mtwrap"><div class="vas273-mtbl" id="vas273-linetbl-record"></div></div>');

            lineState = { order: order, lines: lines, page: 0, size: MAX_ROWS_PER_PAGE, tableId: 'vas273-linetbl-record' };
            drawLineTable('vas273-linetbl-record');

            $mFoot.html(lineFootHtml(order, lines));
            $mFoot.find('#vas273-mback').on('click', backModal);
            $mFoot.find('#vas273-mclose').on('click', closeModal);
        }

        function lineFootHtml(order, lines) {
            var qtyOrdered = 0, qtyShort = 0;
            lines.forEach(function (line) {
                qtyOrdered += Number(line.QtyOrdered) || 0;
                var pending = Number(line.QtyPending) || 0;
                var freeStock = Number(line.FreeStock) || 0;
                if (pending > freeStock) { qtyShort += (pending - freeStock); }
            });
            var footNote = lines.length + ' ' + label('VAS_273_LinesSuffix', 'lines') +
                ' · ' + formatNum(qtyOrdered) + ' ' + label('VAS_273_QtyOrderedSuffix', 'qty ordered') +
                ' · ' + order.DeliveryStatus +
                (qtyShort > 0 ? ' · ' + formatNum(qtyShort) + ' ' + label('VAS_273_QtyShortSuffix', 'qty short of stock') : '');

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
                size: 'md',
                render: function () { renderLinesBody(order, lines); }
            };
        }

        function renderLinesBody(order, lines) {
            var qtyOrdered = 0, qtyPending = 0;
            lines.forEach(function (l) { qtyOrdered += Number(l.QtyOrdered) || 0; qtyPending += Number(l.QtyPending) || 0; });

            var breadcrumb = '<div class="vas273-polink">' + escapeHtml(label('VAS_273_SalesOrderPrefix', 'Sales order')) +
                ' <button type="button" class="vas273-lnk" data-so="' + order.SalesOrderId + '">' + escapeHtml(order.SalesOrderNumber) + '</button>' +
                ' · ' + escapeHtml(formatDateFull(order.SalesOrderDate)) + ' · ' + escapeHtml(order.DocumentStatus) + '</div>';

            var stats = '<div class="vas273-mstats">' +
                statTile(label('VAS_273_ColLine', 'Line') + 's', String(lines.length)) +
                statTile(label('VAS_273_SoValue', 'SO value'), formatINR(order.OrderValue)) +
                statTile(label('VAS_273_ColOrdered', 'Ordered'), formatNum(qtyOrdered)) +
                statTile(label('VAS_273_ColPending', 'Pending'), formatNum(qtyPending)) +
            '</div>';

            var secHtml = '<div class="vas273-msec">' + escapeHtml(label('VAS_273_SalesOrderLines', 'Sales order lines')) + '</div>';
            $mBody.html(breadcrumb + stats + secHtml + '<div class="vas273-mtwrap"><div class="vas273-mtbl" id="vas273-linetbl-lines"></div></div>');

            lineState = { order: order, lines: lines, page: 0, size: MAX_ROWS_PER_PAGE, tableId: 'vas273-linetbl-lines' };
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

        function drawLineTable(tableId) {
            var el = document.getElementById(tableId);
            if (!el || !lineState) { return; }

            var tpl = LINE_COLS.map(function (c) { return 'minmax(0,' + c.w + 'fr)'; }).join(' ');
            var pages = Math.max(1, Math.ceil(lineState.lines.length / lineState.size));
            if (lineState.page > pages - 1) { lineState.page = pages - 1; }
            var start = lineState.page * lineState.size;
            var slice = lineState.lines.slice(start, start + lineState.size);

            var head = '<div class="vas273-mrow vas273-mhead-row" style="grid-template-columns:' + tpl + '">' +
                LINE_COLS.map(function (c) { return '<span class="vas273-cell' + (c.align === 'right' ? ' right' : '') + '" title="' + escapeHtml(c.label) + '">' + escapeHtml(c.label) + '</span>'; }).join('') +
            '</div>';

            var body = slice.map(function (line) {
                var st = lineStatus(line);
                var cells = [
                    cellHtml(String(line.LineNo), 'vas273-c-std', 'right'),
                    cellHtml(line.ProductName, 'vas273-c-prim'),
                    cellHtml(line.AttributeText, 'vas273-c-std'),
                    cellHtml(line.UomName, 'vas273-c-std'),
                    cellHtml(formatNum(line.QtyOrdered), 'vas273-c-std', 'right'),
                    cellHtml(formatNum(line.QtyDelivered), 'vas273-c-std', 'right'),
                    cellHtml(formatNum(line.QtyPending), 'vas273-c-prim', 'right'),
                    cellHtml(formatNum(line.FreeStock), (line.FreeStock < line.QtyPending ? 'vas273-c-short' : 'vas273-c-ok'), 'right'),
                    cellHtml('₹ ' + formatNum(line.Rate), 'vas273-c-std', 'right'),
                    cellHtml(formatINR(line.Amount), 'vas273-c-emph', 'right')
                ].join('') + '<span class="vas273-cell"><span class="vas273-chip ' + st.cls + '" title="' + escapeHtml(st.text) + '">' + escapeHtml(st.text) + '</span></span>';
                return '<div class="vas273-mrow" style="grid-template-columns:' + tpl + '">' + cells + '</div>';
            }).join('');

            var showingLabel = label('VAS_273_Showing', 'Showing') + ' ' + (lineState.lines.length ? (start + 1) : 0) + '–' + (start + slice.length) + ' ' + label('VAS_273_Of', 'of') + ' ' + lineState.lines.length;

            var foot = '<div class="vas273-mtfoot"><span class="vas273-helper">' + escapeHtml(showingLabel) + '</span>' +
                (pages > 1 ?
                    '<span class="vas273-pager">' +
                        '<button type="button" class="vas273-pbtn" data-table="lines" data-dir="-1"' + (lineState.page === 0 ? ' disabled' : '') + ' aria-label="' + escapeHtml(label('VAS_273_PrevPage', 'Previous page')) + '">' + icon('prev') + '</button>' +
                        '<span class="vas273-ptxt">' + (lineState.page + 1) + ' ' + escapeHtml(label('VAS_273_Of', 'of')) + ' ' + pages + '</span>' +
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
        function fitTable(id, currentSize, onResize) {
            var el = document.getElementById(id);
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
            n = Math.max(MIN_ROWS_PER_PAGE, Math.min(MAX_ROWS_PER_PAGE, n));
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
            var ns = '.vas273-' + ($self.AD_UserHomeWidgetID || $self.windowNo || 'widget');
            $(document).off(ns);
            $(window).off(ns);
            if ($mask) { $mask.remove(); }
            $root.remove();
        };

        this.refreshWidget = function () { loadSummary(); };
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
