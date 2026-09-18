/**
 * VAS_280 Short Supply Blocking Dispatch Widget (Sales Order dashboard, 4x2 table)
 * Purpose - The stock-constraint worklist: Product+ship-from-Warehouse combinations
 *           where committed (DocStatus 'CO') Sales Order demand exceeds free stock,
 *           ranked by VALUE BLOCKED (shortQty * weighted rate) - never by shortfall
 *           quantity, so a small shortfall on an expensive item outranks a large
 *           shortfall on a cheap one. Free stock = QtyOnHand - QtyReserved -
 *           QtyDedicated - QtyAllocated (the same formula as every other widget on
 *           this dashboard) - no open PO/expected receipt/in-transit stock ever
 *           offsets the shortfall. Only rows with a real shortfall (shortQty > 0)
 *           appear. The header chip totals value blocked across the COMPLETE result,
 *           not just the visible page, and flips to the ok tone ("Fully covered")
 *           the moment nothing is short.
 *
 * Design  - 13-short-supply-blocking-dispatch.html / .md: glass 4x2 tile, header
 *           with title only (no subtitle) + a risk-toned chip in the right slot, a
 *           CSS-grid data table (7 columns, header and body sharing one identical
 *           grid-template-columns string) with 7 rows per page, and a footer pager.
 *           Row click opens the product's shortage detail: an 8-card stat strip,
 *           a remediation note, and the Sales Orders held on this product
 *           (standard SO column set) reusing the same shared record/lines child
 *           modals as every other widget on this dashboard.
 *
 * Backend - VAS_280_ShortSupplyBlockingDispatchWidget/GetShortSupply              (GET page,size -> ranked page + total value blocked)
 *           VAS_280_ShortSupplyBlockingDispatchWidget/GetProductWarehouseDetail   (GET productId,warehouseId,page,size -> stat strip + paginated SOs)
 *           VAS_280_ShortSupplyBlockingDispatchWidget/GetSalesOrderDetail         (GET id -> order + lines)
 *
 * ── Labels / Message Keys ─────────────────────────────────────────────────
 *  #  | Current Text                                                          | Message Key
 * ----+------------------------------------------------------------------------+--------------------------------
 *  1  | Short Supply Blocking Dispatch                                        | VAS_280_Title
 *  2  | held                                                                  | VAS_280_HeldSuffix
 *  3  | Fully covered                                                         | VAS_280_FullyCoveredChip
 *  4  | All open orders are covered by free stock                             | VAS_280_ZeroState
 *  5  | Stock position unavailable                                            | VAS_280_ErrorState
 *  6  | Product                                                               | VAS_280_ColProduct
 *  7  | Warehouse                                                             | VAS_280_ColWarehouse
 *  8  | On open SO                                                            | VAS_280_OnOpenSo
 *  9  | Free stock                                                            | VAS_280_FreeStock
 * 10  | Short qty                                                             | VAS_280_ShortQty
 * 11  | Value blocked                                                         | VAS_280_ValueBlocked
 * 12  | SOs                                                                   | VAS_280_ColSos
 * 13  | highest value blocked first                                           | VAS_280_RankedBy
 * 14  | Short supply ·                                                        | VAS_280_TitlePrefix
 * 15  | short against open sales orders                                       | VAS_280_ShortAgainstSuffix
 * 16  | Coverage                                                              | VAS_280_StatCoverage
 * 17  | SOs affected                                                          | VAS_280_StatSosAffected
 * 18  | Rate                                                                  | VAS_280_StatRate
 * 19  | Ship-from warehouse                                                   | VAS_280_StatShipFrom
 * 20  | Free stock excludes quantity already allocated to picking and dispatch documents. Raise a purchase requisition or transfer stock from another warehouse to release these dispatches. | VAS_280_Note
 * 21  | Sales orders held on this product                                     | VAS_280_SectionHeading
 * 22  | held for stock                                                        | VAS_280_HeldForStockLabel
 * 23  | SO No                                                                 | VAS_280_ColSoNo
 * 24  | SO date                                                               | VAS_280_SoDate
 * 25  | Customer                                                              | VAS_280_Customer
 * 26  | Representative                                                        | VAS_280_Representative
 * 27  | Value                                                                 | VAS_280_ColValue
 * 28  | Delivery                                                              | VAS_280_ColDelivery
 * 29  | Status                                                                | VAS_280_ColStatus
 * 30  | Back                                                                  | VAS_280_Back
 * 31  | Close                                                                 | VAS_280_Close
 * 32  | Date promised                                                         | VAS_280_DatePromised
 * 33  | SO value                                                              | VAS_280_SoValue
 * 34  | Delivery mode                                                         | VAS_280_DeliveryMode
 * 35  | Document status                                                       | VAS_280_DocumentStatus
 * 36  | Delivery status                                                       | VAS_280_DeliveryStatus
 * 37  | Sales order lines                                                     | VAS_280_SalesOrderLines
 * 38  | Line                                                                  | VAS_280_ColLine
 * 39  | Attribute                                                             | VAS_280_ColAttribute
 * 40  | UoM                                                                   | VAS_280_ColUom
 * 41  | Ordered                                                               | VAS_280_ColOrdered
 * 42  | Delivered                                                             | VAS_280_ColDelivered
 * 43  | Pending                                                               | VAS_280_ColPending
 * 44  | In stock                                                              | VAS_280_ColInStock
 * 45  | Amount                                                                | VAS_280_ColAmount
 * 46  | Line status                                                           | VAS_280_ColLineStatus
 * 47  | Delivered                                                             | VAS_280_DeliveryFull
 * 48  | Partially delivered                                                   | VAS_280_DeliveryPartial
 * 49  | Not delivered                                                         | VAS_280_DeliveryNone
 * 50  | Partly delivered                                                      | VAS_280_LinePartial
 * 51  | In process                                                            | VAS_280_LineInProcess
 * 51a | Sales order                                                           | VAS_280_SalesOrderPrefix
 * 52  | lines                                                                 | VAS_280_LinesSuffix
 * 53  | qty ordered                                                           | VAS_280_QtyOrderedSuffix
 * 54  | qty short of stock                                                    | VAS_280_QtyShortSuffix
 * 55  | Previous page                                                         | VAS_280_PrevPage
 * 56  | Next page                                                             | VAS_280_NextPage
 * 57  | of                                                                    | VAS_280_Of
 * 58  | Showing                                                               | VAS_280_Showing
 * 59  | Search is unavailable right now. Try again in a moment.               | VAS_280_LoadError
 * 60  | sales orders                                                          | VAS_280_SalesOrdersSuffix
 */
; VAS = window.VAS || {};

; (function (VAS, $) {

    var WIDGET_PAGE_SIZE = 7;   // rows per widget page
    var MAX_ROWS_PER_PAGE = 10;
    var MIN_ROWS_PER_PAGE = 2;
    var MONTHS = ['Jan', 'Feb', 'Mar', 'Apr', 'May', 'Jun', 'Jul', 'Aug', 'Sep', 'Oct', 'Nov', 'Dec'];

    // Header row + every body row share this exact grid-template-columns string
    // (design.md §10): Product / Warehouse / On open SO / Free stock / Short qty /
    // Value blocked / SOs.
    var TABLE_TEMPLATE = 'minmax(0,1.6fr) minmax(0,1.15fr) minmax(0,.8fr) minmax(0,.8fr) minmax(0,.8fr) minmax(0,.95fr) minmax(0,.5fr)';

    /* design.md §Widget Header / §Measurement Setup: keep --dash-inline-size on
       :root equal to the dashboard container's current pixel width so the widget
       clamp resolves against the dashboard's visible width, not the viewport. A
       single document-level ResizeObserver serves every widget (matches VAS_269-279). */
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

    VAS.VAS_280_ShortSupplyBlockingDispatchWidget = function () {

        this.frame;
        this.windowNo;

        var $self = this;
        var $root = $('<div class="vas280-root">');
        var $shell, $chip, $tbody, $helper, $pageTxt, $prevBtn, $nextBtn;
        var $mask, $modal, $mHead, $mTitle, $mSub, $mBack, $mBody, $mFoot;

        var SELF_ENDPOINT = 'VAS_280_ShortSupplyBlockingDispatchWidget/';

        var listState = 'loading'; // 'loading' | 'ready' | 'error'
        var docsState = { page: 0, size: WIDGET_PAGE_SIZE, total: 0, rows: [], totalValueBlocked: 0 };

        var detailState = null;  // { productId, warehouseId, summary, docs:{page,size,total,rows} }
        var lineState = null;    // { order, lines, page, size, tableId }
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

        function formatDateFull(iso) {
            var d = parseIso(iso);
            if (!d) { return ''; }
            return (d.getDate() < 10 ? '0' : '') + d.getDate() + ' ' + MONTHS[d.getMonth()] + ' ' + d.getFullYear();
        }

        function formatDateShort(iso) {
            var d = parseIso(iso);
            if (!d) { return ''; }
            return (d.getDate() < 10 ? '0' : '') + d.getDate() + ' ' + MONTHS[d.getMonth()];
        }

        function parseResponse(res) {
            var data = res;
            if (typeof data === 'string') { data = JSON.parse(data); }
            if (typeof data === 'string') { data = JSON.parse(data); }
            return data || {};
        }

        function statTile(l, v) {
            return '<div class="vas280-mstat"><div class="l">' + escapeHtml(l) + '</div><div class="v" title="' + escapeHtml(v) + '">' + escapeHtml(v) + '</div></div>';
        }

        /* ============================================================
         * Widget shell: header (title + chip) + shortage table + pager
         * ============================================================ */
        function createWidget() {
            $shell = $('<div class="vas280-shell"></div>');

            var $head = $('<div class="vas280-head"></div>');
            var $htxt = $('<div class="vas280-head-txt"></div>');
            $htxt.append('<p class="vas280-title">' + escapeHtml(label('VAS_280_Title', 'Short Supply Blocking Dispatch')) + '</p>');
            $chip = $('<span class="vas280-chip vas280-chip-risk"></span>');
            $head.append($htxt, $chip);

            var $tbl = $('<div class="vas280-tbl"></div>');
            var $thead = $('<div class="vas280-trow vas280-thead"></div>').css('grid-template-columns', TABLE_TEMPLATE);
            $thead.html([
                { l: label('VAS_280_ColProduct', 'Product'), right: false },
                { l: label('VAS_280_ColWarehouse', 'Warehouse'), right: false },
                { l: label('VAS_280_OnOpenSo', 'On open SO'), right: true },
                { l: label('VAS_280_FreeStock', 'Free stock'), right: true },
                { l: label('VAS_280_ShortQty', 'Short qty'), right: true },
                { l: label('VAS_280_ValueBlocked', 'Value blocked'), right: true },
                { l: label('VAS_280_ColSos', 'SOs'), right: true }
            ].map(function (c) {
                return '<span class="vas280-cell' + (c.right ? ' right' : '') + '" title="' + escapeHtml(c.l) + '">' + escapeHtml(c.l) + '</span>';
            }).join(''));

            $tbody = $('<div class="vas280-tbody"></div>');
            $tbl.append($thead, $tbody);

            var $foot = $('<div class="vas280-wfoot"></div>');
            $helper = $('<span class="vas280-helper"></span>');
            var $pager = $('<div class="vas280-pager"></div>');
            $prevBtn = $('<button type="button" class="vas280-pbtn" aria-label="' + escapeHtml(label('VAS_280_PrevPage', 'Previous page')) + '">' + icon('prev') + '</button>');
            $pageTxt = $('<span class="vas280-ptxt"></span>');
            $nextBtn = $('<button type="button" class="vas280-pbtn" aria-label="' + escapeHtml(label('VAS_280_NextPage', 'Next page')) + '">' + icon('next') + '</button>');
            $pager.append($prevBtn, $pageTxt, $nextBtn);
            $foot.append($helper, $pager);

            $shell.append($head, $tbl, $foot);
            $root.append($shell);

            $prevBtn.on('click', function () { turnWidgetPage(-1); });
            $nextBtn.on('click', function () { turnWidgetPage(1); });

            $tbody.on('click', function (event) {
                var row = event.target.closest ? event.target.closest('[data-pid]') : null;
                if (row) { openProductWarehouseModal(Number(row.getAttribute('data-pid')), Number(row.getAttribute('data-wid'))); }
            });
        }

        function skeletonRows() {
            var tpl = TABLE_TEMPLATE;
            var rows = '';
            for (var i = 0; i < WIDGET_PAGE_SIZE; i++) {
                rows += '<div class="vas280-trow" style="grid-template-columns:' + tpl + ';cursor:default">' +
                    '<span class="vas280-cell"><span class="vas280-skel-cell" style="width:70%"></span></span>' +
                    '<span class="vas280-cell"><span class="vas280-skel-cell" style="width:60%"></span></span>' +
                    '<span class="vas280-cell right"><span class="vas280-skel-cell" style="width:50%;margin-left:auto"></span></span>' +
                    '<span class="vas280-cell right"><span class="vas280-skel-cell" style="width:50%;margin-left:auto"></span></span>' +
                    '<span class="vas280-cell right"><span class="vas280-skel-cell" style="width:50%;margin-left:auto"></span></span>' +
                    '<span class="vas280-cell right"><span class="vas280-skel-cell" style="width:60%;margin-left:auto"></span></span>' +
                    '<span class="vas280-cell right"><span class="vas280-skel-cell" style="width:40%;margin-left:auto"></span></span>' +
                '</div>';
            }
            return rows;
        }

        function loadShortSupply(page) {
            listState = 'loading';
            renderWidget();

            $.ajax({
                url: VIS.Application.contextUrl + SELF_ENDPOINT + 'GetShortSupply',
                type: 'GET', dataType: 'json', cache: false,
                data: { page: page, size: WIDGET_PAGE_SIZE },
                success: function (res) {
                    var parsed = parseResponse(res);
                    if (parsed.Error) { listState = 'error'; }
                    else {
                        listState = 'ready';
                        docsState.page = page;
                        docsState.total = Number(parsed.Total || 0);
                        docsState.totalValueBlocked = Number(parsed.TotalValueBlocked || 0);
                        docsState.rows = parsed.Rows || [];
                    }
                    renderWidget();
                },
                error: function () {
                    listState = 'error';
                    renderWidget();
                }
            });
        }

        function renderWidget() {
            if (listState === 'loading') {
                $chip.hide();
                $tbody.html(skeletonRows());
                $helper.text('');
                $pageTxt.text('');
                $prevBtn.prop('disabled', true);
                $nextBtn.prop('disabled', true);
                return;
            }
            if (listState === 'error') {
                $chip.hide();
                $tbody.html('<div class="vas280-empty">' + escapeHtml(label('VAS_280_ErrorState', 'Stock position unavailable')) + '</div>');
                $helper.text('');
                $pageTxt.text('');
                $prevBtn.prop('disabled', true);
                $nextBtn.prop('disabled', true);
                return;
            }

            $chip.show();
            if (docsState.total <= 0) {
                $chip.removeClass('vas280-chip-risk').addClass('vas280-chip-ok').text(label('VAS_280_FullyCoveredChip', 'Fully covered'));
                $tbody.html('<div class="vas280-empty">' + escapeHtml(label('VAS_280_ZeroState', 'All open orders are covered by free stock')) + '</div>');
                $helper.text('');
                $pageTxt.text('');
                $prevBtn.prop('disabled', true);
                $nextBtn.prop('disabled', true);
                return;
            }

            var chipText = formatINR(docsState.totalValueBlocked) + ' ' + label('VAS_280_HeldSuffix', 'held');
            $chip.removeClass('vas280-chip-ok').addClass('vas280-chip-risk').attr('title', chipText).text(chipText);

            var tpl = TABLE_TEMPLATE;
            $tbody.html(docsState.rows.map(function (row) {
                var qtyTitle = function (qty) { return formatNum(qty) + ' ' + row.UomName; };
                return '<button type="button" class="vas280-trow" style="grid-template-columns:' + tpl + '" data-pid="' + row.ProductId + '" data-wid="' + row.WarehouseId + '">' +
                    '<span class="vas280-cell vas280-c-prim" title="' + escapeHtml(row.ProductName) + '">' + escapeHtml(row.ProductName) + '</span>' +
                    '<span class="vas280-cell vas280-c-std" title="' + escapeHtml(row.WarehouseName) + '">' + escapeHtml(row.WarehouseName) + '</span>' +
                    '<span class="vas280-cell right vas280-c-dark" title="' + escapeHtml(qtyTitle(row.DemandQty)) + '">' + formatNum(row.DemandQty) + '</span>' +
                    '<span class="vas280-cell right vas280-c-std" title="' + escapeHtml(qtyTitle(row.FreeStock)) + '">' + formatNum(row.FreeStock) + '</span>' +
                    '<span class="vas280-cell right vas280-c-short" title="' + escapeHtml(qtyTitle(row.ShortQty)) + '">' + formatNum(row.ShortQty) + '</span>' +
                    '<span class="vas280-cell right vas280-c-emph" title="' + escapeHtml(formatINR(row.ValueBlocked)) + '">' + escapeHtml(formatINR(row.ValueBlocked)) + '</span>' +
                    '<span class="vas280-cell right vas280-c-dark" title="' + row.AffectedOrderCount + ' ' + escapeHtml(label('VAS_280_SalesOrdersSuffix', 'sales orders')) + '">' + row.AffectedOrderCount + '</span>' +
                '</button>';
            }).join(''));

            var pages = Math.max(1, Math.ceil(docsState.total / docsState.size));
            var start = docsState.page * docsState.size;
            var helperText = label('VAS_280_Showing', 'Showing') + ' ' + (start + 1) + '–' + (start + docsState.rows.length) + ' ' + label('VAS_280_Of', 'of') + ' ' + docsState.total + ' · ' + label('VAS_280_RankedBy', 'highest value blocked first');
            $helper.attr('title', helperText).text(helperText);
            $pageTxt.text((docsState.page + 1) + ' ' + label('VAS_280_Of', 'of') + ' ' + pages);
            $prevBtn.prop('disabled', docsState.page === 0);
            $nextBtn.prop('disabled', docsState.page >= pages - 1);
        }

        function turnWidgetPage(dir) {
            var pages = Math.max(1, Math.ceil(docsState.total / docsState.size));
            var next = Math.min(pages - 1, Math.max(0, docsState.page + dir));
            if (next === docsState.page) { return; }
            loadShortSupply(next);
        }

        /* ============================================================
         * Modal shell - lives on <body>, not inside $root, so the widget
         * cell's own overflow cannot clip it. Every screen is a
         * { title, subtitle, size, render } config; showScreen() pushes the
         * outgoing config onto cfgStack and paints the new one; backModal()
         * pops and re-renders the popped config from its own live state
         * (detailState / lineState) - never a frozen HTML snapshot.
         * ============================================================ */
        function createModal() {
            $mask = $('<div class="vas280-mask" role="dialog" aria-modal="true"></div>');
            $modal = $('<div class="vas280-modal"></div>');
            $mHead = $('<div class="vas280-mhead"></div>');
            var $htxt = $('<div class="vas280-htxt"></div>');
            $mBack = $('<button type="button" class="vas280-xbtn" aria-label="' + escapeHtml(label('VAS_280_Back', 'Back')) + '" hidden>' + icon('back') + '</button>');
            var $titleWrap = $('<div></div>');
            $mTitle = $('<h2></h2>');
            $mSub = $('<div class="vas280-msub"></div>');
            $titleWrap.append($mTitle, $mSub);
            $htxt.append($mBack, $titleWrap);
            var $closeBtn = $('<button type="button" class="vas280-xbtn" aria-label="' + escapeHtml(label('VAS_280_Close', 'Close')) + '">' + icon('close') + '</button>');
            $mHead.append($htxt, $closeBtn);

            $mBody = $('<div class="vas280-mbody"></div>');
            $mFoot = $('<div class="vas280-mfoot"></div>');

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
            var ns = '.vas280-' + ($self.AD_UserHomeWidgetID || $self.windowNo || 'widget');

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
            detailState = null;
            lineState = null;
        }

        function showLoading(title) {
            $mBack.prop('hidden', !cfgStack.length);
            $mTitle.text(title || '');
            $mSub.text('');
            $mBody.html('<div class="vas280-mstate">…</div>');
            $mFoot.html('');
            $mask.addClass('is-open');
        }

        function showLoadError() {
            $mBody.html('<div class="vas280-mstate">' + escapeHtml(label('VAS_280_LoadError', 'Search is unavailable right now. Try again in a moment.')) + '</div>');
        }

        /* ============================================================
         * Product+Warehouse drill-down (top-level modal opened from a row click)
         * ============================================================ */
        function openProductWarehouseModal(productId, warehouseId) {
            showLoading();

            fetchDetail(productId, warehouseId, 0, MAX_ROWS_PER_PAGE, function (ok, summary, total, rows) {
                if (!ok) { showLoadError(); return; }
                detailState = { productId: productId, warehouseId: warehouseId, summary: summary, docs: { page: 0, size: MAX_ROWS_PER_PAGE, total: total, rows: rows } };
                showScreen(buildDetailCfg(summary), false);
            });
        }

        function buildDetailCfg(summary) {
            return {
                title: label('VAS_280_TitlePrefix', 'Short supply ·') + ' ' + summary.ProductName,
                subtitle: summary.WarehouseName + ' · ' + formatNum(summary.ShortQty) + ' ' + summary.UomName + ' ' + label('VAS_280_ShortAgainstSuffix', 'short against open sales orders'),
                size: '',
                render: renderDetailBody
            };
        }

        function fetchDetail(productId, warehouseId, page, size, cb) {
            $.ajax({
                url: VIS.Application.contextUrl + SELF_ENDPOINT + 'GetProductWarehouseDetail',
                type: 'GET', dataType: 'json', cache: false,
                data: { productId: productId, warehouseId: warehouseId, page: page, size: size },
                success: function (res) {
                    var parsed = parseResponse(res);
                    if (parsed.Error || !parsed.Summary) { cb(false); return; }
                    cb(true, parsed.Summary, Number(parsed.Total || 0), (parsed.Rows || []).map(normalizeOrderRow));
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
                DocumentStatus: row.DocumentStatus || '',
                DeliveryStatus: row.DeliveryStatus || '',
                DeliveryStatusCode: row.DeliveryStatusCode || 'PENDING'
            };
        }

        function deliveryChipClass(code) {
            if (code === 'FULL') { return 'vas280-mchip-info'; }
            if (code === 'PARTIAL') { return 'vas280-mchip-warn'; }
            return 'vas280-mchip-neutral';
        }

        function renderDetailBody() {
            var summary = detailState.summary;
            var coveragePercent = summary.DemandQty > 0 ? Math.min(100, Math.round((summary.FreeStock / summary.DemandQty) * 100)) : 100;

            var statsHtml = '<div class="vas280-mstats">' +
                statTile(label('VAS_280_OnOpenSo', 'On open SO'), formatNum(summary.DemandQty) + ' ' + summary.UomName) +
                statTile(label('VAS_280_FreeStock', 'Free stock'), formatNum(summary.FreeStock) + ' ' + summary.UomName) +
                statTile(label('VAS_280_ShortQty', 'Short qty'), formatNum(summary.ShortQty) + ' ' + summary.UomName) +
                statTile(label('VAS_280_StatCoverage', 'Coverage'), coveragePercent + '%') +
                statTile(label('VAS_280_ValueBlocked', 'Value blocked'), formatINR(summary.ValueBlocked)) +
                statTile(label('VAS_280_StatSosAffected', 'SOs affected'), String(summary.AffectedOrderCount)) +
                statTile(label('VAS_280_StatRate', 'Rate'), '₹ ' + formatNum(summary.Rate)) +
                statTile(label('VAS_280_StatShipFrom', 'Ship-from warehouse'), summary.WarehouseName) +
            '</div>';

            var noteHtml = '<div class="vas280-mnote">' + escapeHtml(label('VAS_280_Note',
                'Free stock excludes quantity already allocated to picking and dispatch documents. Raise a purchase requisition or transfer stock from another warehouse to release these dispatches.'
            )) + '</div>';

            var secHtml = '<div class="vas280-msec">' + escapeHtml(label('VAS_280_SectionHeading', 'Sales orders held on this product')) + '</div>';

            $mBody.html(statsHtml + noteHtml + secHtml + '<div class="vas280-mtwrap"><div class="vas280-mtbl" id="vas280-docstbl"></div></div>');
            drawDocumentsTable();

            $mFoot.html('<span class="vas280-foot-note"></span><span><button type="button" class="vas280-btn" id="vas280-mclose">' + escapeHtml(label('VAS_280_Close', 'Close')) + '</button></span>');
            $mFoot.find('#vas280-mclose').on('click', closeModal);
        }

        var DOC_COLS = [
            { key: 'icon', label: '', w: .32 },
            { key: 'so', label: label('VAS_280_ColSoNo', 'SO No'), w: 1.2 },
            { key: 'date', label: label('VAS_280_SoDate', 'SO date'), w: 1 },
            { key: 'customer', label: label('VAS_280_Customer', 'Customer'), w: 1.7 },
            { key: 'wh', label: label('VAS_280_ColWarehouse', 'Warehouse'), w: 1.2 },
            { key: 'rep', label: label('VAS_280_Representative', 'Representative'), w: 1.2 },
            { key: 'value', label: label('VAS_280_ColValue', 'Value'), w: .9, align: 'right' },
            { key: 'delivery', label: label('VAS_280_ColDelivery', 'Delivery'), w: 1.05 },
            { key: 'status', label: label('VAS_280_ColStatus', 'Status'), w: 1.1 }
        ];

        function mcell(value, cls, align) {
            return '<span class="vas280-mcell' + (align === 'right' ? ' right' : '') + ' ' + cls + '" title="' + escapeHtml(value) + '">' + escapeHtml(value) + '</span>';
        }

        function drawDocumentsTable() {
            var el = document.getElementById('vas280-docstbl');
            if (!el || !detailState) { return; }
            var docs = detailState.docs;
            var tpl = DOC_COLS.map(function (c) { return 'minmax(0,' + c.w + 'fr)'; }).join(' ');
            var pages = Math.max(1, Math.ceil(docs.total / docs.size));

            var head = '<div class="vas280-mrow vas280-mhead-row" style="grid-template-columns:' + tpl + '">' +
                DOC_COLS.map(function (c) { return '<span class="vas280-mcell' + (c.align === 'right' ? ' right' : '') + '" title="' + escapeHtml(c.label) + '">' + escapeHtml(c.label) + '</span>'; }).join('') +
            '</div>';

            var body = docs.rows.map(function (row) {
                var cells = [
                    '<span class="vas280-mcell center"><button type="button" class="vas280-iconbtn" data-lines="' + row.SalesOrderId + '" title="' + escapeHtml(row.SalesOrderNumber) + '">' + icon('lines') + '</button></span>',
                    '<span class="vas280-mcell"><button type="button" class="vas280-lnk" data-so="' + row.SalesOrderId + '" title="' + escapeHtml(row.SalesOrderNumber) + '">' + escapeHtml(row.SalesOrderNumber) + '</button></span>',
                    mcell(formatDateFull(row.SalesOrderDate), 'vas280-c-std'),
                    mcell(row.CustomerName, 'vas280-c-prim'),
                    mcell(row.WarehouseName || '—', 'vas280-c-std'),
                    mcell(row.RepresentativeName || '—', 'vas280-c-std'),
                    mcell(formatINR(row.OrderValue), 'vas280-c-emph', 'right'),
                    '<span class="vas280-mcell" title="' + escapeHtml(row.DeliveryStatus) + '"><span class="vas280-mchip ' + deliveryChipClass(row.DeliveryStatusCode) + '">' + escapeHtml(row.DeliveryStatus) + '</span></span>',
                    '<span class="vas280-mcell" title="' + escapeHtml(row.DocumentStatus) + '"><span class="vas280-mchip vas280-mchip-ok">' + escapeHtml(row.DocumentStatus) + '</span></span>'
                ];
                return '<div class="vas280-mrow" style="grid-template-columns:' + tpl + '">' + cells.join('') + '</div>';
            }).join('');

            var start = docs.page * docs.size;
            var showingLabel = label('VAS_280_Showing', 'Showing') + ' ' + (docs.total ? (start + 1) : 0) + '–' + (start + docs.rows.length) + ' ' + label('VAS_280_Of', 'of') + ' ' + docs.total + ' · ' + label('VAS_280_HeldForStockLabel', 'held for stock');

            var foot = '<div class="vas280-mtfoot"><span class="vas280-mhelper">' + escapeHtml(showingLabel) + '</span>' +
                (pages > 1 ?
                    '<span class="vas280-mpager">' +
                        '<button type="button" class="vas280-mpbtn" data-table="docs" data-dir="-1"' + (docs.page === 0 ? ' disabled' : '') + ' aria-label="' + escapeHtml(label('VAS_280_PrevPage', 'Previous page')) + '">' + icon('prev') + '</button>' +
                        '<span class="vas280-mptxt">' + (docs.page + 1) + ' ' + escapeHtml(label('VAS_280_Of', 'of')) + ' ' + pages + '</span>' +
                        '<button type="button" class="vas280-mpbtn" data-table="docs" data-dir="1"' + (docs.page >= pages - 1 ? ' disabled' : '') + ' aria-label="' + escapeHtml(label('VAS_280_NextPage', 'Next page')) + '">' + icon('next') + '</button>' +
                    '</span>' : '<span></span>') +
            '</div>';

            el.innerHTML = head + '<div class="vas280-mbody-rows">' + body + '</div>' + foot;
        }

        function turnPage(table, dir) {
            if (table === 'docs' && detailState) {
                var docs = detailState.docs;
                var pages = Math.max(1, Math.ceil(docs.total / docs.size));
                var next = Math.min(pages - 1, Math.max(0, docs.page + dir));
                if (next === docs.page) { return; }
                fetchDetail(detailState.productId, detailState.warehouseId, next, docs.size, function (ok, summary, total, rows) {
                    if (!ok) { return; }
                    docs.page = next; docs.total = total; docs.rows = rows;
                    drawDocumentsTable();
                });
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
            if (line.QtyDelivered >= line.QtyOrdered && line.QtyOrdered > 0) { return { text: label('VAS_280_DeliveryFull', 'Delivered'), cls: 'vas280-mchip-info' }; }
            if (line.QtyDelivered > 0) { return { text: label('VAS_280_LinePartial', 'Partly delivered'), cls: 'vas280-mchip-warn' }; }
            return { text: label('VAS_280_LineInProcess', 'In process'), cls: 'vas280-mchip-neutral' };
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
            showLoading(label('VAS_280_Title', 'Short Supply Blocking Dispatch') + '…');
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
            return '<div class="vas280-mstats">' +
                statTile(label('VAS_280_Customer', 'Customer'), order.CustomerName) +
                statTile(label('VAS_280_SoDate', 'SO date'), formatDateFull(order.SalesOrderDate)) +
                statTile(label('VAS_280_DatePromised', 'Date promised'), formatDateFull(order.DatePromised)) +
                statTile(label('VAS_280_SoValue', 'SO value'), formatINR(order.OrderValue)) +
                statTile(label('VAS_280_StatShipFrom', 'Ship-from warehouse'), order.WarehouseName) +
                statTile(label('VAS_280_DeliveryMode', 'Delivery mode'), order.DeliveryMode) +
                statTile(label('VAS_280_DocumentStatus', 'Document status'), order.DocumentStatus) +
                statTile(label('VAS_280_DeliveryStatus', 'Delivery status'), order.DeliveryStatus) +
            '</div>';
        }

        function renderRecordBody(order, lines) {
            var secHtml = '<div class="vas280-msec">' + escapeHtml(label('VAS_280_SalesOrderLines', 'Sales order lines')) + '</div>';
            $mBody.html(soHeaderStats(order) + secHtml + '<div class="vas280-mtwrap"><div class="vas280-mtbl" id="vas280-linetbl-record"></div></div>');

            lineState = { order: order, lines: lines, page: 0, size: MAX_ROWS_PER_PAGE, tableId: 'vas280-linetbl-record' };
            drawLineTable('vas280-linetbl-record');

            $mFoot.html(lineFootHtml(order, lines));
            $mFoot.find('#vas280-mback').on('click', backModal);
            $mFoot.find('#vas280-mclose').on('click', closeModal);
        }

        function lineFootHtml(order, lines) {
            var qtyOrdered = 0, qtyShort = 0;
            lines.forEach(function (line) {
                qtyOrdered += Number(line.QtyOrdered) || 0;
                var pending = Number(line.QtyPending) || 0;
                var freeStock = Number(line.FreeStock) || 0;
                if (pending > freeStock) { qtyShort += (pending - freeStock); }
            });
            var footNote = lines.length + ' ' + label('VAS_280_LinesSuffix', 'lines') +
                ' · ' + formatNum(qtyOrdered) + ' ' + label('VAS_280_QtyOrderedSuffix', 'qty ordered') +
                ' · ' + order.DeliveryStatus +
                (qtyShort > 0 ? ' · ' + formatNum(qtyShort) + ' ' + label('VAS_280_QtyShortSuffix', 'qty short of stock') : '');

            return '<span class="vas280-foot-note">' + escapeHtml(footNote) + '</span>' +
                '<span><button type="button" class="vas280-btn" id="vas280-mback">' + escapeHtml(label('VAS_280_Back', 'Back')) + '</button> ' +
                '<button type="button" class="vas280-btn" id="vas280-mclose">' + escapeHtml(label('VAS_280_Close', 'Close')) + '</button></span>';
        }

        function openLinesModal(orderId) {
            showLoading(label('VAS_280_SalesOrderLines', 'Sales order lines') + '…');
            fetchOrderDetail(orderId, function (detail) {
                if (!detail) { showLoadError(); return; }
                showScreen(buildLinesCfg(detail.order, detail.lines), true);
            });
        }

        function buildLinesCfg(order, lines) {
            return {
                title: label('VAS_280_SalesOrderLines', 'Sales order lines') + ' · ' + order.SalesOrderNumber,
                subtitle: [order.CustomerName, formatDateShort(order.SalesOrderDate), order.DeliveryStatus].filter(function (p) { return p; }).join(' · '),
                size: 'md',
                render: function () { renderLinesBody(order, lines); }
            };
        }

        function renderLinesBody(order, lines) {
            var qtyOrdered = 0, qtyPending = 0;
            lines.forEach(function (l) { qtyOrdered += Number(l.QtyOrdered) || 0; qtyPending += Number(l.QtyPending) || 0; });

            var breadcrumb = '<div class="vas280-mnote">' + escapeHtml(label('VAS_280_SalesOrderPrefix', 'Sales order')) +
                ' <button type="button" class="vas280-lnk" data-so="' + order.SalesOrderId + '" style="display:inline">' + escapeHtml(order.SalesOrderNumber) + '</button>' +
                ' · ' + escapeHtml(formatDateFull(order.SalesOrderDate)) + ' · ' + escapeHtml(order.DocumentStatus) + '</div>';

            var stats = '<div class="vas280-mstats">' +
                statTile(label('VAS_280_ColLine', 'Line') + 's', String(lines.length)) +
                statTile(label('VAS_280_SoValue', 'SO value'), formatINR(order.OrderValue)) +
                statTile(label('VAS_280_ColOrdered', 'Ordered'), formatNum(qtyOrdered)) +
                statTile(label('VAS_280_ColPending', 'Pending'), formatNum(qtyPending)) +
            '</div>';

            var secHtml = '<div class="vas280-msec">' + escapeHtml(label('VAS_280_SalesOrderLines', 'Sales order lines')) + '</div>';
            $mBody.html(breadcrumb + stats + secHtml + '<div class="vas280-mtwrap"><div class="vas280-mtbl" id="vas280-linetbl-lines"></div></div>');

            lineState = { order: order, lines: lines, page: 0, size: MAX_ROWS_PER_PAGE, tableId: 'vas280-linetbl-lines' };
            drawLineTable('vas280-linetbl-lines');

            $mFoot.html(lineFootHtml(order, lines));
            $mFoot.find('#vas280-mback').on('click', backModal);
            $mFoot.find('#vas280-mclose').on('click', closeModal);
        }

        var LINE_COLS = [
            { key: 'no', label: label('VAS_280_ColLine', 'Line'), w: .35, align: 'right' },
            { key: 'product', label: label('VAS_280_ColProduct', 'Product'), w: 1.5 },
            { key: 'attr', label: label('VAS_280_ColAttribute', 'Attribute'), w: 1.1 },
            { key: 'uom', label: label('VAS_280_ColUom', 'UoM'), w: .5 },
            { key: 'ordered', label: label('VAS_280_ColOrdered', 'Ordered'), w: .7, align: 'right' },
            { key: 'delivered', label: label('VAS_280_ColDelivered', 'Delivered'), w: .7, align: 'right' },
            { key: 'pending', label: label('VAS_280_ColPending', 'Pending'), w: .7, align: 'right' },
            { key: 'instock', label: label('VAS_280_ColInStock', 'In stock'), w: .7, align: 'right' },
            { key: 'rate', label: label('VAS_280_StatRate', 'Rate'), w: .7, align: 'right' },
            { key: 'amount', label: label('VAS_280_ColAmount', 'Amount'), w: .9, align: 'right' },
            { key: 'status', label: label('VAS_280_ColLineStatus', 'Line status'), w: 1 }
        ];

        function drawLineTable(tableId) {
            var el = document.getElementById(tableId);
            if (!el || !lineState) { return; }

            var tpl = LINE_COLS.map(function (c) { return 'minmax(0,' + c.w + 'fr)'; }).join(' ');
            var pages = Math.max(1, Math.ceil(lineState.lines.length / lineState.size));
            if (lineState.page > pages - 1) { lineState.page = pages - 1; }
            var start = lineState.page * lineState.size;
            var slice = lineState.lines.slice(start, start + lineState.size);

            var head = '<div class="vas280-mrow vas280-mhead-row" style="grid-template-columns:' + tpl + '">' +
                LINE_COLS.map(function (c) { return '<span class="vas280-mcell' + (c.align === 'right' ? ' right' : '') + '" title="' + escapeHtml(c.label) + '">' + escapeHtml(c.label) + '</span>'; }).join('') +
            '</div>';

            var body = slice.map(function (line) {
                var st = lineStatus(line);
                var cells = [
                    mcell(String(line.LineNo), 'vas280-c-std', 'right'),
                    mcell(line.ProductName, 'vas280-c-prim'),
                    mcell(line.AttributeText, 'vas280-c-std'),
                    mcell(line.UomName, 'vas280-c-std'),
                    mcell(formatNum(line.QtyOrdered), 'vas280-c-std', 'right'),
                    mcell(formatNum(line.QtyDelivered), 'vas280-c-std', 'right'),
                    mcell(formatNum(line.QtyPending), 'vas280-c-prim', 'right'),
                    mcell(formatNum(line.FreeStock), (line.FreeStock < line.QtyPending ? 'vas280-c-short' : 'vas280-c-ok'), 'right'),
                    mcell('₹ ' + formatNum(line.Rate), 'vas280-c-std', 'right'),
                    mcell(formatINR(line.Amount), 'vas280-c-emph', 'right')
                ].join('') + '<span class="vas280-mcell"><span class="vas280-mchip ' + st.cls + '" title="' + escapeHtml(st.text) + '">' + escapeHtml(st.text) + '</span></span>';
                return '<div class="vas280-mrow" style="grid-template-columns:' + tpl + '">' + cells + '</div>';
            }).join('');

            var showingLabel = label('VAS_280_Showing', 'Showing') + ' ' + (lineState.lines.length ? (start + 1) : 0) + '–' + (start + slice.length) + ' ' + label('VAS_280_Of', 'of') + ' ' + lineState.lines.length;

            var foot = '<div class="vas280-mtfoot"><span class="vas280-mhelper">' + escapeHtml(showingLabel) + '</span>' +
                (pages > 1 ?
                    '<span class="vas280-mpager">' +
                        '<button type="button" class="vas280-mpbtn" data-table="lines" data-dir="-1"' + (lineState.page === 0 ? ' disabled' : '') + ' aria-label="' + escapeHtml(label('VAS_280_PrevPage', 'Previous page')) + '">' + icon('prev') + '</button>' +
                        '<span class="vas280-mptxt">' + (lineState.page + 1) + ' ' + escapeHtml(label('VAS_280_Of', 'of')) + ' ' + pages + '</span>' +
                        '<button type="button" class="vas280-mpbtn" data-table="lines" data-dir="1"' + (lineState.page >= pages - 1 ? ' disabled' : '') + ' aria-label="' + escapeHtml(label('VAS_280_NextPage', 'Next page')) + '">' + icon('next') + '</button>' +
                    '</span>' : '<span></span>') +
            '</div>';

            el.innerHTML = head + '<div class="vas280-mbody-rows">' + body + '</div>' + foot;
        }

        /* Sizes each visible table's rows-per-page to the space actually left in
           the modal body, so the body never grows an inner scrollbar. */
        function fitTable(id, currentSize, onResize) {
            var el = document.getElementById(id);
            if (!el) { return; }
            var avail = el.clientHeight;
            if (avail < 40) { return; }
            var head = el.querySelector('.vas280-mhead-row');
            var foot = el.querySelector('.vas280-mtfoot');
            var row = el.querySelector('.vas280-mbody-rows .vas280-mrow');
            if (!head || !row) { return; }
            var rowHeight = row.getBoundingClientRect().height || 30;
            var used = head.getBoundingClientRect().height + (foot ? foot.getBoundingClientRect().height + 8 : 0);
            var n = Math.floor((avail - used) / rowHeight);
            n = Math.max(MIN_ROWS_PER_PAGE, Math.min(MAX_ROWS_PER_PAGE, n));
            if (n !== currentSize) { onResize(n); }
        }

        function fitAllTables() {
            if (!$mask.hasClass('is-open')) { return; }

            if (detailState) {
                fitTable('vas280-docstbl', detailState.docs.size, function (n) {
                    var docs = detailState.docs;
                    var newPages = Math.max(1, Math.ceil(docs.total / n));
                    var page = Math.min(docs.page, newPages - 1);
                    fetchDetail(detailState.productId, detailState.warehouseId, page, n, function (ok, summary, total, rows) {
                        if (!ok) { return; }
                        docs.page = page; docs.size = n; docs.total = total; docs.rows = rows;
                        drawDocumentsTable();
                    });
                });
            }
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
            loadShortSupply(0);
        };

        this.getRoot = function () { return $root; };

        this.disposeComponent = function () {
            var ns = '.vas280-' + ($self.AD_UserHomeWidgetID || $self.windowNo || 'widget');
            $(document).off(ns);
            $(window).off(ns);
            if ($mask) { $mask.remove(); }
            $root.remove();
        };

        this.refreshWidget = function () { loadShortSupply(0); };
    };

    VAS.VAS_280_ShortSupplyBlockingDispatchWidget.prototype.widgetFirevalueChanged = function (value) {
        if (this.listener) { this.listener.widgetFirevalueChanged(value); }
    };

    VAS.VAS_280_ShortSupplyBlockingDispatchWidget.prototype.addChangeListener = function (listener) {
        this.listener = listener;
    };

    VAS.VAS_280_ShortSupplyBlockingDispatchWidget.prototype.init = function (windowNo, frame) {
        this.frame = frame;
        this.AD_UserHomeWidgetID = frame.widgetInfo.AD_UserHomeWidgetID;
        this.windowNo = windowNo;
        this.Initalize();
        this.frame.getContentGrid().append(this.getRoot());
        ensureDashInlineSizeVar(this.getRoot());
    };

    VAS.VAS_280_ShortSupplyBlockingDispatchWidget.prototype.widgetSizeChange = function () { };

    VAS.VAS_280_ShortSupplyBlockingDispatchWidget.prototype.dispose = function () {
        if (this.disposeComponent) { this.disposeComponent(); }
    };

})(VAS, jQuery);
