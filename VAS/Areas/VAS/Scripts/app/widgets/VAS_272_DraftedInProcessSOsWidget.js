/**
 * VAS_272 Drafted / In-Process SOs Widget (Sales Order dashboard, 3x1 KPI split tile)
 * Purpose - Work in the pipeline that has not yet become a firm commitment: Sales
 *           Orders with DocStatus 'DR' (Drafted - someone started them and stopped)
 *           or 'IP' (In Process - complete but held mid-workflow, usually awaiting
 *           approval/credit clearance). All-time/current pipeline, not month-scoped.
 *           These two states never appear in any other Sales Order KPI on this
 *           dashboard. Click / Enter / Space opens a drill-down modal with a 4-card
 *           stat strip and a paginated, newest-first table with a stage chip (neutral
 *           for Drafted, prop for In process - the one place those two tints appear
 *           together). SO number opens a record-preview child modal and the lines
 *           icon opens a lines-only child modal (both fed by one
 *           GetSalesOrderDetail fetch).
 *
 * Design  - 05-kpi-drafted-in-process.html / .md: glass 3x1 KPI tile with a split
 *           layout - headline + meta (just the combined value) on the left, a
 *           compound "N · M" figure over a two-line "drafted · / in process" label on
 *           the right - "info" (informational, neither good nor bad) tone and border
 *           tint. Modal shell, stat tiles, chips and paginated table match the same
 *           design language as VAS_270/271's shared Sales Order record-preview modal -
 *           reused here as this widget's own self-contained copy, matching how every
 *           widget controller/JS pair in this codebase owns its own modal
 *           implementation.
 *
 * Modal   - Documents (top-level) -> Record / Lines (child, back-navigable), same
 *           { title, subtitle, size, render } config-stack engine as VAS_270/271 -
 *           "back" pops the stack and re-runs the popped config's render() from its
 *           own already-fetched state, never stale HTML.
 *
 * Backend - VAS_272_DraftedInProcessSOsWidget/GetSummary            (GET -> tile + stat-strip figures)
 *           VAS_272_DraftedInProcessSOsWidget/GetOrders             (GET page,size -> paginated documents, newest first)
 *           VAS_272_DraftedInProcessSOsWidget/GetSalesOrderDetail   (GET id -> order + lines)
 *
 * ── Labels / Message Keys ─────────────────────────────────────────────────
 *  #  | Current Text                                                          | Message Key
 * ----+------------------------------------------------------------------------+--------------------------------
 *  1  | Drafted / In-Process SOs                                              | VAS_272_Title
 *  2  | drafted ·                                                             | VAS_272_SideLabelLine1
 *  3  | in process                                                            | VAS_272_SideLabelLine2
 *  4  | No documents in progress                                              | VAS_272_ZeroState
 *  5  | Figures unavailable                                                   | VAS_272_ErrorState
 *  6  | Total documents                                                       | VAS_272_StatTotalDocuments
 *  7  | Drafted                                                               | VAS_272_StatDrafted
 *  8  | In process                                                            | VAS_272_StatInProcess
 *  9  | Value                                                                 | VAS_272_StatValue
 * 10  | Documents                                                             | VAS_272_SectionHeading
 * 11  | newest first                                                          | VAS_272_NewestFirst
 * 12  | SO No                                                                 | VAS_272_ColSoNo
 * 13  | SO date                                                               | VAS_272_SoDate
 * 14  | Customer                                                              | VAS_272_Customer
 * 15  | Representative                                                        | VAS_272_Representative
 * 16  | Lines                                                                 | VAS_272_ColLines
 * 17  | Value                                                                 | VAS_272_ColValue
 * 18  | Stage                                                                 | VAS_272_ColStage
 * 19  | Drafted                                                               | VAS_272_StageDrafted
 * 20  | In process                                                            | VAS_272_StageInProcess
 * 21  | Back                                                                  | VAS_272_Back
 * 22  | Close                                                                 | VAS_272_Close
 * 23  | Date promised                                                         | VAS_272_DatePromised
 * 24  | SO value                                                              | VAS_272_SoValue
 * 25  | Ship from                                                             | VAS_272_ShipFrom
 * 26  | Delivery mode                                                         | VAS_272_DeliveryMode
 * 27  | Document status                                                       | VAS_272_DocumentStatus
 * 28  | Delivery status                                                       | VAS_272_DeliveryStatus
 * 29  | Sales order lines                                                     | VAS_272_SalesOrderLines
 * 30  | Line                                                                  | VAS_272_ColLine
 * 31  | Product                                                               | VAS_272_ColProduct
 * 32  | Attribute                                                             | VAS_272_ColAttribute
 * 33  | UoM                                                                   | VAS_272_ColUom
 * 34  | Ordered                                                               | VAS_272_ColOrdered
 * 35  | Delivered                                                             | VAS_272_ColDelivered
 * 36  | Pending                                                               | VAS_272_ColPending
 * 37  | In stock                                                              | VAS_272_ColInStock
 * 38  | Rate                                                                  | VAS_272_ColRate
 * 39  | Amount                                                                | VAS_272_ColAmount
 * 40  | Line status                                                           | VAS_272_ColLineStatus
 * 41  | Delivered                                                             | VAS_272_DeliveryFull
 * 42  | Partially delivered                                                   | VAS_272_DeliveryPartial
 * 43  | Not delivered                                                         | VAS_272_DeliveryNone
 * 44  | Not applicable                                                        | VAS_272_DeliveryNA
 * 45  | Partly delivered                                                      | VAS_272_LinePartial
 * 46  | In process                                                            | VAS_272_LineInProcess
 * 47  | Drafted                                                               | VAS_272_LineDrafted
 * 48  | Sales order                                                           | VAS_272_SalesOrderPrefix
 * 49  | lines                                                                 | VAS_272_LinesSuffix
 * 50  | qty ordered                                                           | VAS_272_QtyOrderedSuffix
 * 51  | qty short of stock                                                    | VAS_272_QtyShortSuffix
 * 52  | Previous page                                                         | VAS_272_PrevPage
 * 53  | Next page                                                             | VAS_272_NextPage
 * 54  | of                                                                    | VAS_272_Of
 * 55  | Showing                                                               | VAS_272_Showing
 * 56  | Search is unavailable right now. Try again in a moment.               | VAS_272_LoadError
 */
; VAS = window.VAS || {};

; (function (VAS, $) {

    var MAX_ROWS_PER_PAGE = 10;
    var MIN_ROWS_PER_PAGE = 2;
    var MONTHS = ['Jan', 'Feb', 'Mar', 'Apr', 'May', 'Jun', 'Jul', 'Aug', 'Sep', 'Oct', 'Nov', 'Dec'];

    /* design.md §Widget Header / §Measurement Setup: keep --dash-inline-size on
       :root equal to the dashboard container's current pixel width so the widget
       clamp resolves against the dashboard's visible width, not the viewport. A
       single document-level ResizeObserver serves every widget (matches VAS_269/270/271). */
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

    VAS.VAS_272_DraftedInProcessSOsWidget = function () {

        this.frame;
        this.windowNo;

        var $self = this;
        var $root = $('<div class="vas272-root">');
        var $tile;
        var $mask, $modal, $mHead, $mTitle, $mSub, $mBack, $mBody, $mFoot;

        var SELF_ENDPOINT = 'VAS_272_DraftedInProcessSOsWidget/';

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
            return '<span class="vas272-cell' + (align === 'right' ? ' right' : '') + ' ' + cls + '" title="' + escapeHtml(value) + '">' + escapeHtml(value) + '</span>';
        }

        function statTile(l, v) {
            return '<div class="vas272-mstat"><div class="l">' + escapeHtml(l) + '</div><div class="v" title="' + escapeHtml(v) + '">' + escapeHtml(v) + '</div></div>';
        }

        /* ============================================================
         * Tile
         * ============================================================ */
        function createWidget() {
            $tile = $(
                '<button type="button" class="vas272-tile" aria-label="' + escapeHtml(label('VAS_272_Title', 'Drafted / In-Process SOs')) + '">' +
                    '<p class="vas272-title">' + escapeHtml(label('VAS_272_Title', 'Drafted / In-Process SOs')) + '</p>' +
                    '<div class="vas272-row">' +
                        '<div class="vas272-left">' +
                            '<p class="vas272-val"><span class="vas272-skel"></span></p>' +
                            '<p class="vas272-meta"></p>' +
                        '</div>' +
                        '<div class="vas272-side">' +
                            '<div class="v"><span class="vas272-skel"></span></div>' +
                            '<div class="l"></div>' +
                        '</div>' +
                    '</div>' +
                '</button>'
            );
            $tile.on('click', function () { openDocumentsModal(); });
            $root.append($tile);
        }

        function sideLabelHtml() {
            return escapeHtml(label('VAS_272_SideLabelLine1', 'drafted ·')) + '<br/>' + escapeHtml(label('VAS_272_SideLabelLine2', 'in process'));
        }

        function renderTile() {
            var $val = $tile.find('.vas272-val');
            var $meta = $tile.find('.vas272-meta');
            var $sideV = $tile.find('.vas272-side .v');
            var $sideL = $tile.find('.vas272-side .l');

            $sideL.html(sideLabelHtml());

            if (summaryState === 'loading') {
                $val.html('<span class="vas272-skel"></span>');
                $meta.text('');
                $sideV.html('<span class="vas272-skel"></span>');
                $tile.removeAttr('aria-disabled');
                return;
            }
            if (summaryState === 'error' || !summary) {
                $val.removeClass().addClass('vas272-val error').text('—');
                $meta.text(label('VAS_272_ErrorState', 'Figures unavailable'));
                $sideV.text('—');
                $tile.attr('aria-disabled', 'true');
                return;
            }

            $tile.removeAttr('aria-disabled');
            $val.removeClass().addClass('vas272-val info').text(formatNum(summary.TotalCount));
            $sideV.text(formatNum(summary.DraftedCount) + ' · ' + formatNum(summary.InProcessCount));

            if (summary.TotalCount <= 0) {
                $meta.text(label('VAS_272_ZeroState', 'No documents in progress'));
                return;
            }

            var meta = formatINR(summary.TotalValue);
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
            $mask = $('<div class="vas272-mask" role="dialog" aria-modal="true"></div>');
            $modal = $('<div class="vas272-modal"></div>');
            $mHead = $('<div class="vas272-mhead"></div>');
            var $htxt = $('<div class="vas272-htxt"></div>');
            $mBack = $('<button type="button" class="vas272-xbtn" aria-label="' + escapeHtml(label('VAS_272_Back', 'Back')) + '" hidden>' + icon('back') + '</button>');
            var $titleWrap = $('<div></div>');
            $mTitle = $('<h2></h2>');
            $mSub = $('<div class="vas272-msub"></div>');
            $titleWrap.append($mTitle, $mSub);
            $htxt.append($mBack, $titleWrap);
            var $closeBtn = $('<button type="button" class="vas272-xbtn" aria-label="' + escapeHtml(label('VAS_272_Close', 'Close')) + '">' + icon('close') + '</button>');
            $mHead.append($htxt, $closeBtn);

            $mBody = $('<div class="vas272-mbody"></div>');
            $mFoot = $('<div class="vas272-mfoot"></div>');

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
            var ns = '.vas272-' + ($self.AD_UserHomeWidgetID || $self.windowNo || 'widget');

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
            $mBody.html('<div class="vas272-mstate">…</div>');
            $mFoot.html('');
            $mask.addClass('is-open');
        }

        function showLoadError() {
            $mBody.html('<div class="vas272-mstate">' + escapeHtml(label('VAS_272_LoadError', 'Search is unavailable right now. Try again in a moment.')) + '</div>');
        }

        /* ============================================================
         * Documents list (top-level modal)
         * ============================================================ */
        function buildDocsCfg() {
            return { title: label('VAS_272_Title', 'Drafted / In-Process SOs'), subtitle: formatINR(summary.TotalValue), size: '', render: renderDocumentsBody };
        }

        function openDocumentsModal() {
            if (summaryState === 'error') { return; }
            showLoading(label('VAS_272_Title', 'Drafted / In-Process SOs'));

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
                OrderValue: Number(row.OrderValue) || 0,
                StageCode: row.StageCode || '',
                Stage: row.Stage || ''
            };
        }

        function stageChipClass(stageCode) {
            return stageCode === 'IP' ? 'vas272-chip-prop' : 'vas272-chip-neutral';
        }

        function renderDocumentsBody() {
            var statsHtml = '<div class="vas272-mstats">' +
                statTile(label('VAS_272_StatTotalDocuments', 'Total documents'), formatNum(summary.TotalCount)) +
                statTile(label('VAS_272_StatDrafted', 'Drafted'), formatNum(summary.DraftedCount)) +
                statTile(label('VAS_272_StatInProcess', 'In process'), formatNum(summary.InProcessCount)) +
                statTile(label('VAS_272_StatValue', 'Value'), formatINR(summary.TotalValue)) +
            '</div>';

            var secHtml = '<div class="vas272-msec">' + escapeHtml(label('VAS_272_SectionHeading', 'Documents')) + '</div>';

            if (docsState.total <= 0) {
                $mBody.html(statsHtml + secHtml + '<div class="vas272-mstate">' + escapeHtml(label('VAS_272_ZeroState', 'No documents in progress')) + '</div>');
            } else {
                $mBody.html(statsHtml + secHtml + '<div class="vas272-mtwrap"><div class="vas272-mtbl" id="vas272-docstbl"></div></div>');
                drawDocumentsTable();
            }
            $mFoot.html('<span class="vas272-foot-note"></span><span><button type="button" class="vas272-btn" id="vas272-docsclose">' + escapeHtml(label('VAS_272_Close', 'Close')) + '</button></span>');
            $mFoot.find('#vas272-docsclose').on('click', closeModal);
        }

        var DOC_COLS = [
            { key: 'icon', label: '', w: .32 },
            { key: 'so', label: label('VAS_272_ColSoNo', 'SO No'), w: 1.15 },
            { key: 'date', label: label('VAS_272_SoDate', 'SO date'), w: 1 },
            { key: 'customer', label: label('VAS_272_Customer', 'Customer'), w: 1.8 },
            { key: 'rep', label: label('VAS_272_Representative', 'Representative'), w: 1.3 },
            { key: 'lines', label: label('VAS_272_ColLines', 'Lines'), w: .6, align: 'right' },
            { key: 'value', label: label('VAS_272_ColValue', 'Value'), w: 1, align: 'right' },
            { key: 'stage', label: label('VAS_272_ColStage', 'Stage'), w: 1.1 }
        ];

        function drawDocumentsTable() {
            var el = document.getElementById('vas272-docstbl');
            if (!el) { return; }
            var tpl = DOC_COLS.map(function (c) { return 'minmax(0,' + c.w + 'fr)'; }).join(' ');
            var pages = Math.max(1, Math.ceil(docsState.total / docsState.size));

            var head = '<div class="vas272-mrow vas272-mhead-row" style="grid-template-columns:' + tpl + '">' +
                DOC_COLS.map(function (c) { return '<span class="vas272-cell' + (c.align === 'right' ? ' right' : '') + '" title="' + escapeHtml(c.label) + '">' + escapeHtml(c.label) + '</span>'; }).join('') +
            '</div>';

            var body = docsState.rows.map(function (row) {
                var cells = [
                    '<span class="vas272-cell center"><button type="button" class="vas272-iconbtn" data-lines="' + row.SalesOrderId + '" title="' + escapeHtml(row.SalesOrderNumber) + '">' + icon('lines') + '</button></span>',
                    '<span class="vas272-cell"><button type="button" class="vas272-lnk" data-so="' + row.SalesOrderId + '" title="' + escapeHtml(row.SalesOrderNumber) + '">' + escapeHtml(row.SalesOrderNumber) + '</button></span>',
                    cellHtml(formatDateFull(row.SalesOrderDate), 'vas272-c-std'),
                    cellHtml(row.CustomerName, 'vas272-c-prim'),
                    cellHtml(row.RepresentativeName || '—', 'vas272-c-std'),
                    cellHtml(formatNum(row.LineCount), 'vas272-c-std', 'right'),
                    cellHtml(formatINR(row.OrderValue), 'vas272-c-emph', 'right'),
                    '<span class="vas272-cell" title="' + escapeHtml(row.Stage) + '"><span class="vas272-chip ' + stageChipClass(row.StageCode) + '">' + escapeHtml(row.Stage) + '</span></span>'
                ];
                return '<div class="vas272-mrow" style="grid-template-columns:' + tpl + '">' + cells.join('') + '</div>';
            }).join('');

            var start = docsState.page * docsState.size;
            var showingLabel = label('VAS_272_Showing', 'Showing') + ' ' + (docsState.total ? (start + 1) : 0) + '–' + (start + docsState.rows.length) + ' ' + label('VAS_272_Of', 'of') + ' ' + docsState.total + ' · ' + label('VAS_272_NewestFirst', 'newest first');

            var foot = '<div class="vas272-mtfoot"><span class="vas272-helper">' + escapeHtml(showingLabel) + '</span>' +
                (pages > 1 ?
                    '<span class="vas272-pager">' +
                        '<button type="button" class="vas272-pbtn" data-table="docs" data-dir="-1"' + (docsState.page === 0 ? ' disabled' : '') + ' aria-label="' + escapeHtml(label('VAS_272_PrevPage', 'Previous page')) + '">' + icon('prev') + '</button>' +
                        '<span class="vas272-ptxt">' + (docsState.page + 1) + ' ' + escapeHtml(label('VAS_272_Of', 'of')) + ' ' + pages + '</span>' +
                        '<button type="button" class="vas272-pbtn" data-table="docs" data-dir="1"' + (docsState.page >= pages - 1 ? ' disabled' : '') + ' aria-label="' + escapeHtml(label('VAS_272_NextPage', 'Next page')) + '">' + icon('next') + '</button>' +
                    '</span>' : '<span></span>') +
            '</div>';

            el.innerHTML = head + '<div class="vas272-mbody-rows">' + body + '</div>' + foot;
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
        function lineStatus(order, line) {
            if (order.DocumentStatusCode === 'DR') { return { text: label('VAS_272_LineDrafted', 'Drafted'), cls: 'vas272-chip-neutral' }; }
            if (line.QtyDelivered >= line.QtyOrdered && line.QtyOrdered > 0) { return { text: label('VAS_272_DeliveryFull', 'Delivered'), cls: 'vas272-chip-info' }; }
            if (line.QtyDelivered > 0) { return { text: label('VAS_272_LinePartial', 'Partly delivered'), cls: 'vas272-chip-warn' }; }
            return { text: label('VAS_272_LineInProcess', 'In process'), cls: 'vas272-chip-prop' };
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
            showLoading(label('VAS_272_Title', 'Drafted / In-Process SOs') + '…');
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
            return '<div class="vas272-mstats">' +
                statTile(label('VAS_272_Customer', 'Customer'), order.CustomerName) +
                statTile(label('VAS_272_SoDate', 'SO date'), formatDateFull(order.SalesOrderDate)) +
                statTile(label('VAS_272_DatePromised', 'Date promised'), formatDateFull(order.DatePromised)) +
                statTile(label('VAS_272_SoValue', 'SO value'), formatINR(order.OrderValue)) +
                statTile(label('VAS_272_ShipFrom', 'Ship from'), order.WarehouseName) +
                statTile(label('VAS_272_DeliveryMode', 'Delivery mode'), order.DeliveryMode) +
                statTile(label('VAS_272_DocumentStatus', 'Document status'), order.DocumentStatus) +
                statTile(label('VAS_272_DeliveryStatus', 'Delivery status'), order.DeliveryStatus) +
            '</div>';
        }

        function renderRecordBody(order, lines) {
            var secHtml = '<div class="vas272-msec">' + escapeHtml(label('VAS_272_SalesOrderLines', 'Sales order lines')) + '</div>';
            $mBody.html(soHeaderStats(order) + secHtml + '<div class="vas272-mtwrap"><div class="vas272-mtbl" id="vas272-linetbl-record"></div></div>');

            lineState = { order: order, lines: lines, page: 0, size: MAX_ROWS_PER_PAGE, tableId: 'vas272-linetbl-record' };
            drawLineTable('vas272-linetbl-record');

            $mFoot.html(lineFootHtml(order, lines));
            $mFoot.find('#vas272-mback').on('click', backModal);
            $mFoot.find('#vas272-mclose').on('click', closeModal);
        }

        function lineFootHtml(order, lines) {
            var qtyOrdered = 0, qtyShort = 0;
            lines.forEach(function (line) {
                qtyOrdered += Number(line.QtyOrdered) || 0;
                var pending = Number(line.QtyPending) || 0;
                var freeStock = Number(line.FreeStock) || 0;
                if (pending > freeStock) { qtyShort += (pending - freeStock); }
            });
            var footNote = lines.length + ' ' + label('VAS_272_LinesSuffix', 'lines') +
                ' · ' + formatNum(qtyOrdered) + ' ' + label('VAS_272_QtyOrderedSuffix', 'qty ordered') +
                ' · ' + order.DeliveryStatus +
                (qtyShort > 0 ? ' · ' + formatNum(qtyShort) + ' ' + label('VAS_272_QtyShortSuffix', 'qty short of stock') : '');

            return '<span class="vas272-foot-note">' + escapeHtml(footNote) + '</span>' +
                '<span><button type="button" class="vas272-btn" id="vas272-mback">' + escapeHtml(label('VAS_272_Back', 'Back')) + '</button> ' +
                '<button type="button" class="vas272-btn" id="vas272-mclose">' + escapeHtml(label('VAS_272_Close', 'Close')) + '</button></span>';
        }

        function openLinesModal(orderId) {
            showLoading(label('VAS_272_SalesOrderLines', 'Sales order lines') + '…');
            fetchOrderDetail(orderId, function (detail) {
                if (!detail) { showLoadError(); return; }
                showScreen(buildLinesCfg(detail.order, detail.lines), true);
            });
        }

        function buildLinesCfg(order, lines) {
            return {
                title: label('VAS_272_SalesOrderLines', 'Sales order lines') + ' · ' + order.SalesOrderNumber,
                subtitle: [order.CustomerName, formatDateShort(order.SalesOrderDate), order.DeliveryStatus].filter(function (p) { return p; }).join(' · '),
                size: 'md',
                render: function () { renderLinesBody(order, lines); }
            };
        }

        function renderLinesBody(order, lines) {
            var qtyOrdered = 0, qtyPending = 0;
            lines.forEach(function (l) { qtyOrdered += Number(l.QtyOrdered) || 0; qtyPending += Number(l.QtyPending) || 0; });

            var breadcrumb = '<div class="vas272-polink">' + escapeHtml(label('VAS_272_SalesOrderPrefix', 'Sales order')) +
                ' <button type="button" class="vas272-lnk" data-so="' + order.SalesOrderId + '">' + escapeHtml(order.SalesOrderNumber) + '</button>' +
                ' · ' + escapeHtml(formatDateFull(order.SalesOrderDate)) + ' · ' + escapeHtml(order.DocumentStatus) + '</div>';

            var stats = '<div class="vas272-mstats">' +
                statTile(label('VAS_272_ColLine', 'Line') + 's', String(lines.length)) +
                statTile(label('VAS_272_SoValue', 'SO value'), formatINR(order.OrderValue)) +
                statTile(label('VAS_272_ColOrdered', 'Ordered'), formatNum(qtyOrdered)) +
                statTile(label('VAS_272_ColPending', 'Pending'), formatNum(qtyPending)) +
            '</div>';

            var secHtml = '<div class="vas272-msec">' + escapeHtml(label('VAS_272_SalesOrderLines', 'Sales order lines')) + '</div>';
            $mBody.html(breadcrumb + stats + secHtml + '<div class="vas272-mtwrap"><div class="vas272-mtbl" id="vas272-linetbl-lines"></div></div>');

            lineState = { order: order, lines: lines, page: 0, size: MAX_ROWS_PER_PAGE, tableId: 'vas272-linetbl-lines' };
            drawLineTable('vas272-linetbl-lines');

            $mFoot.html(lineFootHtml(order, lines));
            $mFoot.find('#vas272-mback').on('click', backModal);
            $mFoot.find('#vas272-mclose').on('click', closeModal);
        }

        var LINE_COLS = [
            { key: 'no', label: label('VAS_272_ColLine', 'Line'), w: .35, align: 'right' },
            { key: 'product', label: label('VAS_272_ColProduct', 'Product'), w: 1.5 },
            { key: 'attr', label: label('VAS_272_ColAttribute', 'Attribute'), w: 1.1 },
            { key: 'uom', label: label('VAS_272_ColUom', 'UoM'), w: .5 },
            { key: 'ordered', label: label('VAS_272_ColOrdered', 'Ordered'), w: .7, align: 'right' },
            { key: 'delivered', label: label('VAS_272_ColDelivered', 'Delivered'), w: .7, align: 'right' },
            { key: 'pending', label: label('VAS_272_ColPending', 'Pending'), w: .7, align: 'right' },
            { key: 'instock', label: label('VAS_272_ColInStock', 'In stock'), w: .7, align: 'right' },
            { key: 'rate', label: label('VAS_272_ColRate', 'Rate'), w: .7, align: 'right' },
            { key: 'amount', label: label('VAS_272_ColAmount', 'Amount'), w: .9, align: 'right' },
            { key: 'status', label: label('VAS_272_ColLineStatus', 'Line status'), w: 1 }
        ];

        function drawLineTable(tableId) {
            var el = document.getElementById(tableId);
            if (!el || !lineState) { return; }

            var tpl = LINE_COLS.map(function (c) { return 'minmax(0,' + c.w + 'fr)'; }).join(' ');
            var pages = Math.max(1, Math.ceil(lineState.lines.length / lineState.size));
            if (lineState.page > pages - 1) { lineState.page = pages - 1; }
            var start = lineState.page * lineState.size;
            var slice = lineState.lines.slice(start, start + lineState.size);

            var head = '<div class="vas272-mrow vas272-mhead-row" style="grid-template-columns:' + tpl + '">' +
                LINE_COLS.map(function (c) { return '<span class="vas272-cell' + (c.align === 'right' ? ' right' : '') + '" title="' + escapeHtml(c.label) + '">' + escapeHtml(c.label) + '</span>'; }).join('') +
            '</div>';

            var body = slice.map(function (line) {
                var st = lineStatus(lineState.order, line);
                var cells = [
                    cellHtml(String(line.LineNo), 'vas272-c-std', 'right'),
                    cellHtml(line.ProductName, 'vas272-c-prim'),
                    cellHtml(line.AttributeText, 'vas272-c-std'),
                    cellHtml(line.UomName, 'vas272-c-std'),
                    cellHtml(formatNum(line.QtyOrdered), 'vas272-c-std', 'right'),
                    cellHtml(formatNum(line.QtyDelivered), 'vas272-c-std', 'right'),
                    cellHtml(formatNum(line.QtyPending), 'vas272-c-prim', 'right'),
                    cellHtml(formatNum(line.FreeStock), (line.FreeStock < line.QtyPending ? 'vas272-c-short' : 'vas272-c-ok'), 'right'),
                    cellHtml('₹ ' + formatNum(line.Rate), 'vas272-c-std', 'right'),
                    cellHtml(formatINR(line.Amount), 'vas272-c-emph', 'right')
                ].join('') + '<span class="vas272-cell"><span class="vas272-chip ' + st.cls + '" title="' + escapeHtml(st.text) + '">' + escapeHtml(st.text) + '</span></span>';
                return '<div class="vas272-mrow" style="grid-template-columns:' + tpl + '">' + cells + '</div>';
            }).join('');

            var showingLabel = label('VAS_272_Showing', 'Showing') + ' ' + (lineState.lines.length ? (start + 1) : 0) + '–' + (start + slice.length) + ' ' + label('VAS_272_Of', 'of') + ' ' + lineState.lines.length;

            var foot = '<div class="vas272-mtfoot"><span class="vas272-helper">' + escapeHtml(showingLabel) + '</span>' +
                (pages > 1 ?
                    '<span class="vas272-pager">' +
                        '<button type="button" class="vas272-pbtn" data-table="lines" data-dir="-1"' + (lineState.page === 0 ? ' disabled' : '') + ' aria-label="' + escapeHtml(label('VAS_272_PrevPage', 'Previous page')) + '">' + icon('prev') + '</button>' +
                        '<span class="vas272-ptxt">' + (lineState.page + 1) + ' ' + escapeHtml(label('VAS_272_Of', 'of')) + ' ' + pages + '</span>' +
                        '<button type="button" class="vas272-pbtn" data-table="lines" data-dir="1"' + (lineState.page >= pages - 1 ? ' disabled' : '') + ' aria-label="' + escapeHtml(label('VAS_272_NextPage', 'Next page')) + '">' + icon('next') + '</button>' +
                    '</span>' : '<span></span>') +
            '</div>';

            el.innerHTML = head + '<div class="vas272-mbody-rows">' + body + '</div>' + foot;
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
            var head = el.querySelector('.vas272-mhead-row');
            var foot = el.querySelector('.vas272-mtfoot');
            var row = el.querySelector('.vas272-mbody-rows .vas272-mrow');
            if (!head || !row) { return; }
            var rowHeight = row.getBoundingClientRect().height || 30;
            var used = head.getBoundingClientRect().height + (foot ? foot.getBoundingClientRect().height + 8 : 0);
            var n = Math.floor((avail - used) / rowHeight);
            n = Math.max(MIN_ROWS_PER_PAGE, Math.min(MAX_ROWS_PER_PAGE, n));
            if (n !== currentSize) { onResize(n); }
        }

        function fitAllTables() {
            if (!$mask.hasClass('is-open')) { return; }

            fitTable('vas272-docstbl', docsState.size, function (n) {
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
            var ns = '.vas272-' + ($self.AD_UserHomeWidgetID || $self.windowNo || 'widget');
            $(document).off(ns);
            $(window).off(ns);
            if ($mask) { $mask.remove(); }
            $root.remove();
        };

        this.refreshWidget = function () { loadSummary(); };
    };

    VAS.VAS_272_DraftedInProcessSOsWidget.prototype.widgetFirevalueChanged = function (value) {
        if (this.listener) { this.listener.widgetFirevalueChanged(value); }
    };

    VAS.VAS_272_DraftedInProcessSOsWidget.prototype.addChangeListener = function (listener) {
        this.listener = listener;
    };

    VAS.VAS_272_DraftedInProcessSOsWidget.prototype.init = function (windowNo, frame) {
        this.frame = frame;
        this.AD_UserHomeWidgetID = frame.widgetInfo.AD_UserHomeWidgetID;
        this.windowNo = windowNo;
        this.Initalize();
        this.frame.getContentGrid().append(this.getRoot());
        ensureDashInlineSizeVar(this.getRoot());
    };

    VAS.VAS_272_DraftedInProcessSOsWidget.prototype.widgetSizeChange = function () { };

    VAS.VAS_272_DraftedInProcessSOsWidget.prototype.dispose = function () {
        if (this.disposeComponent) { this.disposeComponent(); }
    };

})(VAS, jQuery);
