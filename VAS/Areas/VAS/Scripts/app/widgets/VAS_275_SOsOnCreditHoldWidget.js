/**
 * VAS_275 SOs on Credit Hold Widget (Sales Order dashboard, 2x1 KPI tile)
 * Purpose - Orders commercially agreed and operationally ready but blocked from
 *           dispatch by the customer's credit position - the one blocker on this
 *           dashboard the warehouse cannot clear; it needs finance. DocStatus 'CO'
 *           only, held via the EFFECTIVE credit level (customer-level C_BPartner
 *           fields, or location-level C_BPartner_Location fields when
 *           CreditStatusSettingOn='CL'), and only when the effective
 *           CreditValidation code actually blocks shipment (B/D/E/F - never 'J'
 *           Warning on All). Click / Enter / Space opens a drill-down modal with a
 *           4-card stat strip, a release-path note, and a paginated,
 *           highest-order-value-first table naming a single fixed-vocabulary hold
 *           reason per row. SO number opens a record-preview child modal and the
 *           lines icon opens a lines-only child modal (both fed by one
 *           GetSalesOrderDetail fetch).
 *
 * Design  - 08-kpi-credit-hold.html / .md: glass 2x1 KPI tile, stacked
 *           title/headline/meta, "warn" (blocked, but resolvable) tone and border
 *           tint - flips to "ok" tone at zero. Modal shell, stat tiles and paginated
 *           table match the same design language as VAS_270-274's shared Sales
 *           Order record-preview modal - reused here as this widget's own
 *           self-contained copy, matching how every widget controller/JS pair in
 *           this codebase owns its own modal implementation.
 *
 * Modal   - Documents (top-level) -> Record / Lines (child, back-navigable), same
 *           { title, subtitle, size, render } config-stack engine as VAS_270-274 -
 *           "back" pops the stack and re-runs the popped config's render() from its
 *           own already-fetched state, never stale HTML.
 *
 * Backend - VAS_275_SOsOnCreditHoldWidget/GetSummary            (GET -> tile + stat-strip figures)
 *           VAS_275_SOsOnCreditHoldWidget/GetOrders             (GET page,size -> paginated documents, highest value first)
 *           VAS_275_SOsOnCreditHoldWidget/GetSalesOrderDetail   (GET id -> order + lines)
 *
 * ── Labels / Message Keys ─────────────────────────────────────────────────
 *  #  | Current Text                                                          | Message Key
 * ----+------------------------------------------------------------------------+--------------------------------
 *  1  | SOs on Credit Hold                                                    | VAS_275_Title
 *  2  | Blocked from dispatch until credit is released                       | VAS_275_Subtitle
 *  3  | blocked                                                               | VAS_275_BlockedSuffix
 *  4  | customers over limit                                                  | VAS_275_CustomersOverLimitSuffix
 *  5  | No orders blocked on credit                                           | VAS_275_ZeroState
 *  6  | Credit position unavailable                                           | VAS_275_ErrorState
 *  7  | SOs on hold                                                           | VAS_275_StatSOsOnHold
 *  8  | Value blocked                                                         | VAS_275_StatValueBlocked
 *  9  | Customers                                                             | VAS_275_StatCustomers
 * 10  | Oldest held SO                                                        | VAS_275_StatOldestHeldSO
 * 11  | days                                                                  | VAS_275_DaysSuffix
 * 12  | Release requires a credit note, an advance receipt, or a limit revision approved by finance. | VAS_275_ReleaseNote
 * 13  | Held sales orders                                                     | VAS_275_SectionHeading
 * 14  | highest value first                                                   | VAS_275_HighestValueFirst
 * 15  | SO No                                                                 | VAS_275_ColSoNo
 * 16  | SO date                                                               | VAS_275_SoDate
 * 17  | Customer                                                              | VAS_275_Customer
 * 18  | Credit limit                                                          | VAS_275_ColCreditLimit
 * 19  | Outstanding                                                           | VAS_275_ColOutstanding
 * 20  | Order value                                                           | VAS_275_ColOrderValue
 * 21  | Overdue invoices                                                      | VAS_275_ColOverdueInvoices
 * 22  | Hold reason                                                           | VAS_275_ColHoldReason
 * 23  | Limit exceeded                                                        | VAS_275_ReasonLimitExceeded
 * 24  | Invoice overdue 30d                                                   | VAS_275_ReasonOverdue30
 * 25  | Invoice overdue 60d                                                   | VAS_275_ReasonOverdue60
 * 26  | Awaiting advance                                                      | VAS_275_ReasonAwaitingAdvance
 * 27  | Back                                                                  | VAS_275_Back
 * 28  | Close                                                                 | VAS_275_Close
 * 29  | Date promised                                                         | VAS_275_DatePromised
 * 30  | SO value                                                              | VAS_275_SoValue
 * 31  | Ship from                                                             | VAS_275_ShipFrom
 * 32  | Delivery mode                                                         | VAS_275_DeliveryMode
 * 33  | Document status                                                       | VAS_275_DocumentStatus
 * 34  | Delivery status                                                       | VAS_275_DeliveryStatus
 * 35  | Sales order lines                                                     | VAS_275_SalesOrderLines
 * 36  | Line                                                                  | VAS_275_ColLine
 * 37  | Product                                                               | VAS_275_ColProduct
 * 38  | Attribute                                                             | VAS_275_ColAttribute
 * 39  | UoM                                                                   | VAS_275_ColUom
 * 40  | Ordered                                                               | VAS_275_ColOrdered
 * 41  | Delivered                                                             | VAS_275_ColDelivered
 * 42  | Pending                                                               | VAS_275_ColPending
 * 43  | In stock                                                              | VAS_275_ColInStock
 * 44  | Rate                                                                  | VAS_275_ColRate
 * 45  | Amount                                                                | VAS_275_ColAmount
 * 46  | Line status                                                           | VAS_275_ColLineStatus
 * 47  | Delivered                                                             | VAS_275_DeliveryFull
 * 48  | Partially delivered                                                   | VAS_275_DeliveryPartial
 * 49  | Not delivered                                                         | VAS_275_DeliveryNone
 * 50  | Partly delivered                                                      | VAS_275_LinePartial
 * 51  | In process                                                            | VAS_275_LineInProcess
 * 52  | Sales order                                                           | VAS_275_SalesOrderPrefix
 * 53  | lines                                                                 | VAS_275_LinesSuffix
 * 54  | qty ordered                                                           | VAS_275_QtyOrderedSuffix
 * 55  | qty short of stock                                                    | VAS_275_QtyShortSuffix
 * 56  | Previous page                                                         | VAS_275_PrevPage
 * 57  | Next page                                                             | VAS_275_NextPage
 * 58  | of                                                                    | VAS_275_Of
 * 59  | Showing                                                               | VAS_275_Showing
 * 60  | Search is unavailable right now. Try again in a moment.               | VAS_275_LoadError
 */
; VAS = window.VAS || {};

; (function (VAS, $) {

    var MAX_ROWS_PER_PAGE = 10;
    var MIN_ROWS_PER_PAGE = 2;
    var MONTHS = ['Jan', 'Feb', 'Mar', 'Apr', 'May', 'Jun', 'Jul', 'Aug', 'Sep', 'Oct', 'Nov', 'Dec'];

    /* design.md §Widget Header / §Measurement Setup: keep --dash-inline-size on
       :root equal to the dashboard container's current pixel width so the widget
       clamp resolves against the dashboard's visible width, not the viewport. A
       single document-level ResizeObserver serves every widget (matches VAS_269-274). */
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

    VAS.VAS_275_SOsOnCreditHoldWidget = function () {

        this.frame;
        this.windowNo;

        var $self = this;
        var $root = $('<div class="vas275-root">');
        var $tile;
        var $mask, $modal, $mHead, $mTitle, $mSub, $mBack, $mBody, $mFoot;

        var SELF_ENDPOINT = 'VAS_275_SOsOnCreditHoldWidget/';

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
            return '<span class="vas275-cell' + (align === 'right' ? ' right' : '') + ' ' + cls + '" title="' + escapeHtml(value) + '">' + escapeHtml(value) + '</span>';
        }

        function statTile(l, v) {
            return '<div class="vas275-mstat"><div class="l">' + escapeHtml(l) + '</div><div class="v" title="' + escapeHtml(v) + '">' + escapeHtml(v) + '</div></div>';
        }

        /* ============================================================
         * Tile
         * ============================================================ */
        function createWidget() {
            $tile = $(
                '<button type="button" class="vas275-tile" aria-label="' + escapeHtml(label('VAS_275_Title', 'SOs on Credit Hold')) + '">' +
                    '<p class="vas275-title">' + escapeHtml(label('VAS_275_Title', 'SOs on Credit Hold')) + '</p>' +
                    '<div class="vas275-group">' +
                        '<p class="vas275-val"><span class="vas275-skel"></span></p>' +
                        '<p class="vas275-meta"></p>' +
                    '</div>' +
                '</button>'
            );
            $tile.on('click', function () { openDocumentsModal(); });
            $root.append($tile);
        }

        function renderTile() {
            var $val = $tile.find('.vas275-val');
            var $meta = $tile.find('.vas275-meta');

            if (summaryState === 'loading') {
                $val.html('<span class="vas275-skel"></span>');
                $meta.text('');
                $tile.removeAttr('aria-disabled');
                $tile.removeClass('vas275-border-ok');
                return;
            }
            if (summaryState === 'error' || !summary) {
                $val.removeClass().addClass('vas275-val error').text('—');
                $meta.text(label('VAS_275_ErrorState', 'Credit position unavailable'));
                $tile.attr('aria-disabled', 'true');
                $tile.removeClass('vas275-border-ok');
                return;
            }

            $tile.removeAttr('aria-disabled');
            var isZero = summary.HeldCount <= 0;
            $tile.toggleClass('vas275-border-ok', isZero);
            $val.removeClass().addClass('vas275-val ' + (isZero ? 'ok' : 'warn')).text(formatNum(summary.HeldCount));

            if (isZero) {
                $meta.text(label('VAS_275_ZeroState', 'No orders blocked on credit'));
                return;
            }

            var meta = formatINR(summary.BlockedValue) + ' ' + label('VAS_275_BlockedSuffix', 'blocked') +
                ' · ' + formatNum(summary.CustomerCount) + ' ' + label('VAS_275_CustomersOverLimitSuffix', 'customers over limit');
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
            $mask = $('<div class="vas275-mask" role="dialog" aria-modal="true"></div>');
            $modal = $('<div class="vas275-modal"></div>');
            $mHead = $('<div class="vas275-mhead"></div>');
            var $htxt = $('<div class="vas275-htxt"></div>');
            $mBack = $('<button type="button" class="vas275-xbtn" aria-label="' + escapeHtml(label('VAS_275_Back', 'Back')) + '" hidden>' + icon('back') + '</button>');
            var $titleWrap = $('<div></div>');
            $mTitle = $('<h2></h2>');
            $mSub = $('<div class="vas275-msub"></div>');
            $titleWrap.append($mTitle, $mSub);
            $htxt.append($mBack, $titleWrap);
            var $closeBtn = $('<button type="button" class="vas275-xbtn" aria-label="' + escapeHtml(label('VAS_275_Close', 'Close')) + '">' + icon('close') + '</button>');
            $mHead.append($htxt, $closeBtn);

            $mBody = $('<div class="vas275-mbody"></div>');
            $mFoot = $('<div class="vas275-mfoot"></div>');

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
            var ns = '.vas275-' + ($self.AD_UserHomeWidgetID || $self.windowNo || 'widget');

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
            $mBody.html('<div class="vas275-mstate">…</div>');
            $mFoot.html('');
            $mask.addClass('is-open');
        }

        function showLoadError() {
            $mBody.html('<div class="vas275-mstate">' + escapeHtml(label('VAS_275_LoadError', 'Search is unavailable right now. Try again in a moment.')) + '</div>');
        }

        /* ============================================================
         * Documents list (top-level modal)
         * ============================================================ */
        function buildDocsCfg() {
            return { title: label('VAS_275_Title', 'SOs on Credit Hold'), subtitle: label('VAS_275_Subtitle', 'Blocked from dispatch until credit is released'), size: '', render: renderDocumentsBody };
        }

        function openDocumentsModal() {
            if (summaryState === 'error') { return; }
            showLoading(label('VAS_275_Title', 'SOs on Credit Hold'));

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
                CreditLimit: Number(row.CreditLimit) || 0,
                Outstanding: Number(row.Outstanding) || 0,
                OrderValue: Number(row.OrderValue) || 0,
                OverdueInvoiceCount: Number(row.OverdueInvoiceCount) || 0,
                HoldReason: row.HoldReason || ''
            };
        }

        function renderDocumentsBody() {
            var statsHtml = '<div class="vas275-mstats">' +
                statTile(label('VAS_275_StatSOsOnHold', 'SOs on hold'), formatNum(summary.HeldCount)) +
                statTile(label('VAS_275_StatValueBlocked', 'Value blocked'), formatINR(summary.BlockedValue)) +
                statTile(label('VAS_275_StatCustomers', 'Customers'), formatNum(summary.CustomerCount)) +
                statTile(label('VAS_275_StatOldestHeldSO', 'Oldest held SO'), formatNum(summary.OldestHeldSoDays) + ' ' + label('VAS_275_DaysSuffix', 'days')) +
            '</div>';

            var noteHtml = '<div class="vas275-mnote">' + escapeHtml(label('VAS_275_ReleaseNote', 'Release requires a credit note, an advance receipt, or a limit revision approved by finance.')) + '</div>';
            var secHtml = '<div class="vas275-msec">' + escapeHtml(label('VAS_275_SectionHeading', 'Held sales orders')) + '</div>';

            if (docsState.total <= 0) {
                $mBody.html(statsHtml + noteHtml + secHtml + '<div class="vas275-mstate">' + escapeHtml(label('VAS_275_ZeroState', 'No orders blocked on credit')) + '</div>');
            } else {
                $mBody.html(statsHtml + noteHtml + secHtml + '<div class="vas275-mtwrap"><div class="vas275-mtbl" id="vas275-docstbl"></div></div>');
                drawDocumentsTable();
            }
            $mFoot.html('<span class="vas275-foot-note"></span><span><button type="button" class="vas275-btn" id="vas275-docsclose">' + escapeHtml(label('VAS_275_Close', 'Close')) + '</button></span>');
            $mFoot.find('#vas275-docsclose').on('click', closeModal);
        }

        var DOC_COLS = [
            { key: 'icon', label: '', w: .32 },
            { key: 'so', label: label('VAS_275_ColSoNo', 'SO No'), w: 1.1 },
            { key: 'date', label: label('VAS_275_SoDate', 'SO date'), w: .95 },
            { key: 'customer', label: label('VAS_275_Customer', 'Customer'), w: 1.6 },
            { key: 'creditlimit', label: label('VAS_275_ColCreditLimit', 'Credit limit'), w: .95, align: 'right' },
            { key: 'outstanding', label: label('VAS_275_ColOutstanding', 'Outstanding'), w: .95, align: 'right' },
            { key: 'ordervalue', label: label('VAS_275_ColOrderValue', 'Order value'), w: .95, align: 'right' },
            { key: 'overdue', label: label('VAS_275_ColOverdueInvoices', 'Overdue invoices'), w: .95, align: 'right' },
            { key: 'reason', label: label('VAS_275_ColHoldReason', 'Hold reason'), w: 1.25 }
        ];

        function drawDocumentsTable() {
            var el = document.getElementById('vas275-docstbl');
            if (!el) { return; }
            var tpl = DOC_COLS.map(function (c) { return 'minmax(0,' + c.w + 'fr)'; }).join(' ');
            var pages = Math.max(1, Math.ceil(docsState.total / docsState.size));

            var head = '<div class="vas275-mrow vas275-mhead-row" style="grid-template-columns:' + tpl + '">' +
                DOC_COLS.map(function (c) { return '<span class="vas275-cell' + (c.align === 'right' ? ' right' : '') + '" title="' + escapeHtml(c.label) + '">' + escapeHtml(c.label) + '</span>'; }).join('') +
            '</div>';

            var body = docsState.rows.map(function (row) {
                var cells = [
                    '<span class="vas275-cell center"><button type="button" class="vas275-iconbtn" data-lines="' + row.SalesOrderId + '" title="' + escapeHtml(row.SalesOrderNumber) + '">' + icon('lines') + '</button></span>',
                    '<span class="vas275-cell"><button type="button" class="vas275-lnk" data-so="' + row.SalesOrderId + '" title="' + escapeHtml(row.SalesOrderNumber) + '">' + escapeHtml(row.SalesOrderNumber) + '</button></span>',
                    cellHtml(formatDateFull(row.SalesOrderDate), 'vas275-c-std'),
                    cellHtml(row.CustomerName, 'vas275-c-prim'),
                    cellHtml(formatINR(row.CreditLimit), 'vas275-c-std', 'right'),
                    cellHtml(formatINR(row.Outstanding), 'vas275-c-std', 'right'),
                    cellHtml(formatINR(row.OrderValue), 'vas275-c-emph', 'right'),
                    cellHtml(row.OverdueInvoiceCount > 0 ? formatNum(row.OverdueInvoiceCount) : '—', 'vas275-c-std', 'right'),
                    cellHtml(row.HoldReason, 'vas275-c-std')
                ];
                return '<div class="vas275-mrow" style="grid-template-columns:' + tpl + '">' + cells.join('') + '</div>';
            }).join('');

            var start = docsState.page * docsState.size;
            var showingLabel = label('VAS_275_Showing', 'Showing') + ' ' + (docsState.total ? (start + 1) : 0) + '–' + (start + docsState.rows.length) + ' ' + label('VAS_275_Of', 'of') + ' ' + docsState.total + ' · ' + label('VAS_275_HighestValueFirst', 'highest value first');

            var foot = '<div class="vas275-mtfoot"><span class="vas275-helper">' + escapeHtml(showingLabel) + '</span>' +
                (pages > 1 ?
                    '<span class="vas275-pager">' +
                        '<button type="button" class="vas275-pbtn" data-table="docs" data-dir="-1"' + (docsState.page === 0 ? ' disabled' : '') + ' aria-label="' + escapeHtml(label('VAS_275_PrevPage', 'Previous page')) + '">' + icon('prev') + '</button>' +
                        '<span class="vas275-ptxt">' + (docsState.page + 1) + ' ' + escapeHtml(label('VAS_275_Of', 'of')) + ' ' + pages + '</span>' +
                        '<button type="button" class="vas275-pbtn" data-table="docs" data-dir="1"' + (docsState.page >= pages - 1 ? ' disabled' : '') + ' aria-label="' + escapeHtml(label('VAS_275_NextPage', 'Next page')) + '">' + icon('next') + '</button>' +
                    '</span>' : '<span></span>') +
            '</div>';

            el.innerHTML = head + '<div class="vas275-mbody-rows">' + body + '</div>' + foot;
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
            if (line.QtyDelivered >= line.QtyOrdered && line.QtyOrdered > 0) { return { text: label('VAS_275_DeliveryFull', 'Delivered'), cls: 'vas275-chip-info' }; }
            if (line.QtyDelivered > 0) { return { text: label('VAS_275_LinePartial', 'Partly delivered'), cls: 'vas275-chip-warn' }; }
            return { text: label('VAS_275_LineInProcess', 'In process'), cls: 'vas275-chip-prop' };
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
            showLoading(label('VAS_275_Title', 'SOs on Credit Hold') + '…');
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
            return '<div class="vas275-mstats">' +
                statTile(label('VAS_275_Customer', 'Customer'), order.CustomerName) +
                statTile(label('VAS_275_SoDate', 'SO date'), formatDateFull(order.SalesOrderDate)) +
                statTile(label('VAS_275_DatePromised', 'Date promised'), formatDateFull(order.DatePromised)) +
                statTile(label('VAS_275_SoValue', 'SO value'), formatINR(order.OrderValue)) +
                statTile(label('VAS_275_ShipFrom', 'Ship from'), order.WarehouseName) +
                statTile(label('VAS_275_DeliveryMode', 'Delivery mode'), order.DeliveryMode) +
                statTile(label('VAS_275_DocumentStatus', 'Document status'), order.DocumentStatus) +
                statTile(label('VAS_275_DeliveryStatus', 'Delivery status'), order.DeliveryStatus) +
            '</div>';
        }

        function renderRecordBody(order, lines) {
            var secHtml = '<div class="vas275-msec">' + escapeHtml(label('VAS_275_SalesOrderLines', 'Sales order lines')) + '</div>';
            $mBody.html(soHeaderStats(order) + secHtml + '<div class="vas275-mtwrap"><div class="vas275-mtbl" id="vas275-linetbl-record"></div></div>');

            lineState = { order: order, lines: lines, page: 0, size: MAX_ROWS_PER_PAGE, tableId: 'vas275-linetbl-record' };
            drawLineTable('vas275-linetbl-record');

            $mFoot.html(lineFootHtml(order, lines));
            $mFoot.find('#vas275-mback').on('click', backModal);
            $mFoot.find('#vas275-mclose').on('click', closeModal);
        }

        function lineFootHtml(order, lines) {
            var qtyOrdered = 0, qtyShort = 0;
            lines.forEach(function (line) {
                qtyOrdered += Number(line.QtyOrdered) || 0;
                var pending = Number(line.QtyPending) || 0;
                var freeStock = Number(line.FreeStock) || 0;
                if (pending > freeStock) { qtyShort += (pending - freeStock); }
            });
            var footNote = lines.length + ' ' + label('VAS_275_LinesSuffix', 'lines') +
                ' · ' + formatNum(qtyOrdered) + ' ' + label('VAS_275_QtyOrderedSuffix', 'qty ordered') +
                ' · ' + order.DeliveryStatus +
                (qtyShort > 0 ? ' · ' + formatNum(qtyShort) + ' ' + label('VAS_275_QtyShortSuffix', 'qty short of stock') : '');

            return '<span class="vas275-foot-note">' + escapeHtml(footNote) + '</span>' +
                '<span><button type="button" class="vas275-btn" id="vas275-mback">' + escapeHtml(label('VAS_275_Back', 'Back')) + '</button> ' +
                '<button type="button" class="vas275-btn" id="vas275-mclose">' + escapeHtml(label('VAS_275_Close', 'Close')) + '</button></span>';
        }

        function openLinesModal(orderId) {
            showLoading(label('VAS_275_SalesOrderLines', 'Sales order lines') + '…');
            fetchOrderDetail(orderId, function (detail) {
                if (!detail) { showLoadError(); return; }
                showScreen(buildLinesCfg(detail.order, detail.lines), true);
            });
        }

        function buildLinesCfg(order, lines) {
            return {
                title: label('VAS_275_SalesOrderLines', 'Sales order lines') + ' · ' + order.SalesOrderNumber,
                subtitle: [order.CustomerName, formatDateShort(order.SalesOrderDate), order.DeliveryStatus].filter(function (p) { return p; }).join(' · '),
                size: 'md',
                render: function () { renderLinesBody(order, lines); }
            };
        }

        function renderLinesBody(order, lines) {
            var qtyOrdered = 0, qtyPending = 0;
            lines.forEach(function (l) { qtyOrdered += Number(l.QtyOrdered) || 0; qtyPending += Number(l.QtyPending) || 0; });

            var breadcrumb = '<div class="vas275-polink">' + escapeHtml(label('VAS_275_SalesOrderPrefix', 'Sales order')) +
                ' <button type="button" class="vas275-lnk" data-so="' + order.SalesOrderId + '">' + escapeHtml(order.SalesOrderNumber) + '</button>' +
                ' · ' + escapeHtml(formatDateFull(order.SalesOrderDate)) + ' · ' + escapeHtml(order.DocumentStatus) + '</div>';

            var stats = '<div class="vas275-mstats">' +
                statTile(label('VAS_275_ColLine', 'Line') + 's', String(lines.length)) +
                statTile(label('VAS_275_SoValue', 'SO value'), formatINR(order.OrderValue)) +
                statTile(label('VAS_275_ColOrdered', 'Ordered'), formatNum(qtyOrdered)) +
                statTile(label('VAS_275_ColPending', 'Pending'), formatNum(qtyPending)) +
            '</div>';

            var secHtml = '<div class="vas275-msec">' + escapeHtml(label('VAS_275_SalesOrderLines', 'Sales order lines')) + '</div>';
            $mBody.html(breadcrumb + stats + secHtml + '<div class="vas275-mtwrap"><div class="vas275-mtbl" id="vas275-linetbl-lines"></div></div>');

            lineState = { order: order, lines: lines, page: 0, size: MAX_ROWS_PER_PAGE, tableId: 'vas275-linetbl-lines' };
            drawLineTable('vas275-linetbl-lines');

            $mFoot.html(lineFootHtml(order, lines));
            $mFoot.find('#vas275-mback').on('click', backModal);
            $mFoot.find('#vas275-mclose').on('click', closeModal);
        }

        var LINE_COLS = [
            { key: 'no', label: label('VAS_275_ColLine', 'Line'), w: .35, align: 'right' },
            { key: 'product', label: label('VAS_275_ColProduct', 'Product'), w: 1.5 },
            { key: 'attr', label: label('VAS_275_ColAttribute', 'Attribute'), w: 1.1 },
            { key: 'uom', label: label('VAS_275_ColUom', 'UoM'), w: .5 },
            { key: 'ordered', label: label('VAS_275_ColOrdered', 'Ordered'), w: .7, align: 'right' },
            { key: 'delivered', label: label('VAS_275_ColDelivered', 'Delivered'), w: .7, align: 'right' },
            { key: 'pending', label: label('VAS_275_ColPending', 'Pending'), w: .7, align: 'right' },
            { key: 'instock', label: label('VAS_275_ColInStock', 'In stock'), w: .7, align: 'right' },
            { key: 'rate', label: label('VAS_275_ColRate', 'Rate'), w: .7, align: 'right' },
            { key: 'amount', label: label('VAS_275_ColAmount', 'Amount'), w: .9, align: 'right' },
            { key: 'status', label: label('VAS_275_ColLineStatus', 'Line status'), w: 1 }
        ];

        function drawLineTable(tableId) {
            var el = document.getElementById(tableId);
            if (!el || !lineState) { return; }

            var tpl = LINE_COLS.map(function (c) { return 'minmax(0,' + c.w + 'fr)'; }).join(' ');
            var pages = Math.max(1, Math.ceil(lineState.lines.length / lineState.size));
            if (lineState.page > pages - 1) { lineState.page = pages - 1; }
            var start = lineState.page * lineState.size;
            var slice = lineState.lines.slice(start, start + lineState.size);

            var head = '<div class="vas275-mrow vas275-mhead-row" style="grid-template-columns:' + tpl + '">' +
                LINE_COLS.map(function (c) { return '<span class="vas275-cell' + (c.align === 'right' ? ' right' : '') + '" title="' + escapeHtml(c.label) + '">' + escapeHtml(c.label) + '</span>'; }).join('') +
            '</div>';

            var body = slice.map(function (line) {
                var st = lineStatus(line);
                var cells = [
                    cellHtml(String(line.LineNo), 'vas275-c-std', 'right'),
                    cellHtml(line.ProductName, 'vas275-c-prim'),
                    cellHtml(line.AttributeText, 'vas275-c-std'),
                    cellHtml(line.UomName, 'vas275-c-std'),
                    cellHtml(formatNum(line.QtyOrdered), 'vas275-c-std', 'right'),
                    cellHtml(formatNum(line.QtyDelivered), 'vas275-c-std', 'right'),
                    cellHtml(formatNum(line.QtyPending), 'vas275-c-prim', 'right'),
                    cellHtml(formatNum(line.FreeStock), (line.FreeStock < line.QtyPending ? 'vas275-c-short' : 'vas275-c-ok'), 'right'),
                    cellHtml('₹ ' + formatNum(line.Rate), 'vas275-c-std', 'right'),
                    cellHtml(formatINR(line.Amount), 'vas275-c-emph', 'right')
                ].join('') + '<span class="vas275-cell"><span class="vas275-chip ' + st.cls + '" title="' + escapeHtml(st.text) + '">' + escapeHtml(st.text) + '</span></span>';
                return '<div class="vas275-mrow" style="grid-template-columns:' + tpl + '">' + cells + '</div>';
            }).join('');

            var showingLabel = label('VAS_275_Showing', 'Showing') + ' ' + (lineState.lines.length ? (start + 1) : 0) + '–' + (start + slice.length) + ' ' + label('VAS_275_Of', 'of') + ' ' + lineState.lines.length;

            var foot = '<div class="vas275-mtfoot"><span class="vas275-helper">' + escapeHtml(showingLabel) + '</span>' +
                (pages > 1 ?
                    '<span class="vas275-pager">' +
                        '<button type="button" class="vas275-pbtn" data-table="lines" data-dir="-1"' + (lineState.page === 0 ? ' disabled' : '') + ' aria-label="' + escapeHtml(label('VAS_275_PrevPage', 'Previous page')) + '">' + icon('prev') + '</button>' +
                        '<span class="vas275-ptxt">' + (lineState.page + 1) + ' ' + escapeHtml(label('VAS_275_Of', 'of')) + ' ' + pages + '</span>' +
                        '<button type="button" class="vas275-pbtn" data-table="lines" data-dir="1"' + (lineState.page >= pages - 1 ? ' disabled' : '') + ' aria-label="' + escapeHtml(label('VAS_275_NextPage', 'Next page')) + '">' + icon('next') + '</button>' +
                    '</span>' : '<span></span>') +
            '</div>';

            el.innerHTML = head + '<div class="vas275-mbody-rows">' + body + '</div>' + foot;
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
            var head = el.querySelector('.vas275-mhead-row');
            var foot = el.querySelector('.vas275-mtfoot');
            var row = el.querySelector('.vas275-mbody-rows .vas275-mrow');
            if (!head || !row) { return; }
            var rowHeight = row.getBoundingClientRect().height || 30;
            var used = head.getBoundingClientRect().height + (foot ? foot.getBoundingClientRect().height + 8 : 0);
            var n = Math.floor((avail - used) / rowHeight);
            n = Math.max(MIN_ROWS_PER_PAGE, Math.min(MAX_ROWS_PER_PAGE, n));
            if (n !== currentSize) { onResize(n); }
        }

        function fitAllTables() {
            if (!$mask.hasClass('is-open')) { return; }

            fitTable('vas275-docstbl', docsState.size, function (n) {
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
            var ns = '.vas275-' + ($self.AD_UserHomeWidgetID || $self.windowNo || 'widget');
            $(document).off(ns);
            $(window).off(ns);
            if ($mask) { $mask.remove(); }
            $root.remove();
        };

        this.refreshWidget = function () { loadSummary(); };
    };

    VAS.VAS_275_SOsOnCreditHoldWidget.prototype.widgetFirevalueChanged = function (value) {
        if (this.listener) { this.listener.widgetFirevalueChanged(value); }
    };

    VAS.VAS_275_SOsOnCreditHoldWidget.prototype.addChangeListener = function (listener) {
        this.listener = listener;
    };

    VAS.VAS_275_SOsOnCreditHoldWidget.prototype.init = function (windowNo, frame) {
        this.frame = frame;
        this.AD_UserHomeWidgetID = frame.widgetInfo.AD_UserHomeWidgetID;
        this.windowNo = windowNo;
        this.Initalize();
        this.frame.getContentGrid().append(this.getRoot());
        ensureDashInlineSizeVar(this.getRoot());
    };

    VAS.VAS_275_SOsOnCreditHoldWidget.prototype.widgetSizeChange = function () { };

    VAS.VAS_275_SOsOnCreditHoldWidget.prototype.dispose = function () {
        if (this.disposeComponent) { this.disposeComponent(); }
    };

})(VAS, jQuery);
