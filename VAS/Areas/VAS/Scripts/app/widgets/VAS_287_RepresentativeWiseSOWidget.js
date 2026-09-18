/**
 * VAS_287 Representative Wise SO Widget (Sales Order dashboard, 3x2 ranked bar list)
 * Purpose - The ten sales representatives with the highest booked Sales Order value
 *           for a selected Month/Year, ranked descending, bar width relative to
 *           rank 1. Booked = active real Sales Order, IsSOTrx='Y', non-quotation,
 *           non-return, DocStatus 'CO' or 'CL'. Representative is the ORDER-LEVEL
 *           C_Order.SalesRep_ID (never the customer's account-owner rep) - someone
 *           who has since left the company still appears for the months they
 *           actually booked in, so the AD_User join carries no IsActive filter.
 *           Five rows visible per widget page; the widget fetches all ten rows once
 *           per period and pages between them client-side without refetching.
 *           Period change re-fetches the ranking and resets the widget page to 1.
 *
 * Design  - 20-representative-wise-so.html / .md: structurally IDENTICAL to VAS_278
 *           Top 10 Customers by design ("two ranked lists on the same dashboard must
 *           look the same") - glass 3x2 tile, header with title + subtitle + a
 *           Month/Year period filter (stopPropagation so it never also triggers a
 *           row's click handler), an `.hlist` of baseline-aligned name/value rows
 *           each with a bar `.track` tinted by row index (blue/teal/amber/lilac/rose
 *           cycling every 5 rows). The one difference: the value cell carries a
 *           compound string, value first then order count after a middle dot
 *           (`₹ 1.58 Cr · 47`) - the count is deliberately unlabelled in the cell
 *           (the widget subtitle already establishes what these rows are) but fully
 *           labelled in the cell's `title` tooltip. Row click opens the
 *           representative's drill-down: a 4-card stat strip (SO value, SOs booked,
 *           Pending delivery, Avg cycle) and that representative's booked Sales
 *           Orders for the period in the standard column set, reusing the same
 *           shared record/lines child modals as every other widget on this
 *           dashboard. When the current role's access is org-restricted (not full
 *           access to all organizations), the footer helper note says so rather than
 *           presenting a permission-filtered ranking as a complete company ranking.
 *
 * Backend - VAS_287_RepresentativeWiseSOWidget/GetTopRepresentatives   (GET month,year -> ranked top 10 + access-scope flag)
 *           VAS_287_RepresentativeWiseSOWidget/GetRepresentativeSummary (GET repId,month,year -> 4-card stat strip)
 *           VAS_287_RepresentativeWiseSOWidget/GetRepresentativeOrders  (GET repId,month,year,page,size -> paginated documents)
 *           VAS_287_RepresentativeWiseSOWidget/GetSalesOrderDetail      (GET id -> order + lines)
 *
 * ── Labels / Message Keys ─────────────────────────────────────────────────
 *  #  | Current Text                                                          | Message Key
 * ----+------------------------------------------------------------------------+--------------------------------
 *  1  | Representative Wise SO                                               | VAS_287_Title
 *  2  | SO value booked by sales representative                              | VAS_287_Subtitle
 *  3  | ranked by SO value                                                   | VAS_287_RankedBy
 *  4  | Showing your own performance                                        | VAS_287_RestrictedHelper
 *  5  | No orders booked in                                                  | VAS_287_ZeroStatePrefix
 *  6  | Ranking unavailable                                                  | VAS_287_ErrorState
 *  7  | Sales orders booked by this representative ·                        | VAS_287_SubtitlePrefix
 *  8  | SO value                                                             | VAS_287_StatOrderValue
 *  9  | SOs booked                                                           | VAS_287_StatOrderCount
 * 10  | Pending delivery                                                     | VAS_287_StatPendingDelivery
 * 11  | Avg cycle                                                            | VAS_287_StatAvgCycle
 * 12  | days                                                                 | VAS_287_DaysSuffix
 * 13  | Sales orders                                                         | VAS_287_SectionHeading
 * 14  | click a row to open the record                                       | VAS_287_ClickRowHint
 * 15  | SO No                                                                | VAS_287_ColSoNo
 * 16  | SO date                                                              | VAS_287_SoDate
 * 17  | Customer                                                             | VAS_287_Customer
 * 18  | Warehouse                                                            | VAS_287_ColWarehouse
 * 19  | Representative                                                       | VAS_287_Representative
 * 20  | Value                                                                | VAS_287_ColValue
 * 21  | Delivery                                                             | VAS_287_ColDelivery
 * 22  | Status                                                               | VAS_287_ColStatus
 * 23  | Back                                                                 | VAS_287_Back
 * 24  | Close                                                                | VAS_287_Close
 * 25  | Date promised                                                        | VAS_287_DatePromised
 * 26  | SO value                                                             | VAS_287_SoValue
 * 27  | Ship from                                                            | VAS_287_ShipFrom
 * 28  | Delivery mode                                                        | VAS_287_DeliveryMode
 * 29  | Document status                                                      | VAS_287_DocumentStatus
 * 30  | Delivery status                                                      | VAS_287_DeliveryStatus
 * 31  | Sales order lines                                                    | VAS_287_SalesOrderLines
 * 32  | Line                                                                 | VAS_287_ColLine
 * 33  | Product                                                              | VAS_287_ColProduct
 * 34  | Attribute                                                            | VAS_287_ColAttribute
 * 35  | UoM                                                                  | VAS_287_ColUom
 * 36  | Ordered                                                              | VAS_287_ColOrdered
 * 37  | Delivered                                                            | VAS_287_ColDelivered
 * 38  | Pending                                                              | VAS_287_ColPending
 * 39  | In stock                                                             | VAS_287_ColInStock
 * 40  | Rate                                                                 | VAS_287_ColRate
 * 41  | Amount                                                               | VAS_287_ColAmount
 * 42  | Line status                                                          | VAS_287_ColLineStatus
 * 43  | Fully delivered                                                      | VAS_287_DeliveryFull
 * 44  | Partial                                                              | VAS_287_DeliveryPartial
 * 45  | Pending                                                              | VAS_287_DeliveryPending
 * 46  | Partly delivered                                                     | VAS_287_LinePartial
 * 47  | In process                                                           | VAS_287_LineInProcess
 * 48  | Sales order                                                          | VAS_287_SalesOrderPrefix
 * 49  | lines                                                                | VAS_287_LinesSuffix
 * 50  | qty ordered                                                          | VAS_287_QtyOrderedSuffix
 * 51  | qty short of stock                                                   | VAS_287_QtyShortSuffix
 * 52  | Previous page                                                        | VAS_287_PrevPage
 * 53  | Next page                                                            | VAS_287_NextPage
 * 54  | of                                                                   | VAS_287_Of
 * 55  | Showing                                                              | VAS_287_Showing
 * 56  | Search is unavailable right now. Try again in a moment.              | VAS_287_LoadError
 */
; VAS = window.VAS || {};

; (function (VAS, $) {

    var MAX_ROWS_PER_PAGE = 10;
    var MIN_ROWS_PER_PAGE = 2;
    var WIDGET_PAGE_SIZE = 5;   // rows visible per widget page (client-side, no refetch)
    var TOP_COUNT = 10;
    var MONTHS = ['Jan', 'Feb', 'Mar', 'Apr', 'May', 'Jun', 'Jul', 'Aug', 'Sep', 'Oct', 'Nov', 'Dec'];
    var MIN_YEAR = 2024;
    var YEAR_SPAN = 3; // MIN_YEAR .. MIN_YEAR + YEAR_SPAN - 1, covers "current + 1"

    // 5-color cycle by row index, matching the reference bar palette (and VAS_278).
    var BAR_COLORS = ['#A9D2FF', '#A3E0D4', '#FFDCA1', '#CFC9F5', '#FFC7C7'];

    var now = new Date();
    var CUR_M = now.getMonth();
    var CUR_Y = now.getFullYear();

    /* Keep --dash-inline-size on :root equal to the dashboard container's current
       pixel width so the widget clamp resolves against the dashboard's visible
       width, not the viewport. A single document-level ResizeObserver serves every
       widget (matches VAS_269-286). */
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

    VAS.VAS_287_RepresentativeWiseSOWidget = function () {

        this.frame;
        this.windowNo;

        var $self = this;
        var $root = $('<div class="vas287-root">');
        var $shell, $list, $helper, $pageTxt, $prevBtn, $nextBtn, $monthSel, $yearSel;
        var $mask, $modal, $mHead, $mTitle, $mSub, $mBack, $mBody, $mFoot;

        var SELF_ENDPOINT = 'VAS_287_RepresentativeWiseSOWidget/';

        var rankState = 'loading'; // 'loading' | 'ready' | 'error'
        var reps = [];              // all 10 rows for the current period
        var isFullAccess = true;
        var widgetPage = 0;

        var repState = null;       // { repId, repName, summary, docs:{page,size,total,rows} }
        var lineState = null;      // { order, lines, page, size, tableId }
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

        function formatINR(value) {
            var num = Number(value || 0);
            if (num >= 1e7) { return '₹ ' + (num / 1e7).toFixed(2) + ' Cr'; }
            if (num >= 1e5) { return '₹ ' + (num / 1e5).toFixed(2) + ' L'; }
            return '₹ ' + Math.round(num).toLocaleString('en-IN');
        }

        function formatNum(value) {
            return Math.round(Number(value || 0)).toLocaleString('en-IN');
        }

        function formatAvgCycle(value) {
            if (value === null || value === undefined) { return '—'; }
            return Number(value).toFixed(1) + ' ' + label('VAS_287_DaysSuffix', 'days');
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

        function periodLabel() {
            return MONTHS[selectedMonth()] + ' ' + selectedYear();
        }

        function selectedMonth() { return $monthSel && $monthSel.length ? Number($monthSel.val()) : CUR_M; }
        function selectedYear() { return $yearSel && $yearSel.length ? Number($yearSel.val()) : CUR_Y; }

        function parseResponse(res) {
            var data = res;
            if (typeof data === 'string') { data = JSON.parse(data); }
            if (typeof data === 'string') { data = JSON.parse(data); }
            return data || {};
        }

        function cellHtml(value, cls, align) {
            return '<span class="vas287-cell' + (align === 'right' ? ' right' : '') + ' ' + cls + '" title="' + escapeHtml(value) + '">' + escapeHtml(value) + '</span>';
        }

        function statTile(l, v) {
            return '<div class="vas287-mstat"><div class="l">' + escapeHtml(l) + '</div><div class="v" title="' + escapeHtml(v) + '">' + escapeHtml(v) + '</div></div>';
        }

        /* ============================================================
         * Widget shell: header (title/subtitle + Month/Year filter) + bar list
         * ============================================================ */
        function fillMonthSelect($sel) {
            $sel.html(MONTHS.map(function (m, i) {
                return '<option value="' + i + '"' + (i === CUR_M ? ' selected' : '') + '>' + m + '</option>';
            }).join(''));
        }

        function fillYearSelect($sel) {
            var html = '';
            for (var y = MIN_YEAR; y < MIN_YEAR + YEAR_SPAN; y++) {
                html += '<option value="' + y + '"' + (y === CUR_Y ? ' selected' : '') + '>' + y + '</option>';
            }
            $sel.html(html);
        }

        function createWidget() {
            $shell = $('<div class="vas287-shell"></div>');

            var $head = $('<div class="vas287-head"></div>');
            var $htxt = $('<div class="vas287-head-txt"></div>');
            $htxt.append('<p class="vas287-title">' + escapeHtml(label('VAS_287_Title', 'Representative Wise SO')) + '</p>');
            $htxt.append('<p class="vas287-sub">' + escapeHtml(label('VAS_287_Subtitle', 'SO value booked by sales representative')) + '</p>');

            var $filter = $('<div class="vas287-mfilter"></div>');
            $monthSel = $('<select class="vas287-msel" aria-label="Month"></select>');
            $yearSel = $('<select class="vas287-msel" aria-label="Year"></select>');
            fillMonthSelect($monthSel);
            fillYearSelect($yearSel);
            $filter.append($monthSel, $yearSel);

            $head.append($htxt, $filter);

            $list = $('<div class="vas287-hlist"></div>');

            var $foot = $('<div class="vas287-wfoot"></div>');
            $helper = $('<span class="vas287-helper"></span>');
            var $pager = $('<div class="vas287-pager"></div>');
            $prevBtn = $('<button type="button" class="vas287-pbtn" aria-label="' + escapeHtml(label('VAS_287_PrevPage', 'Previous page')) + '">' + icon('prev') + '</button>');
            $pageTxt = $('<span class="vas287-ptxt"></span>');
            $nextBtn = $('<button type="button" class="vas287-pbtn" aria-label="' + escapeHtml(label('VAS_287_NextPage', 'Next page')) + '">' + icon('next') + '</button>');
            $pager.append($prevBtn, $pageTxt, $nextBtn);
            $foot.append($helper, $pager);

            $shell.append($head, $list, $foot);
            $root.append($shell);

            $monthSel.on('click', function (event) { event.stopPropagation(); });
            $yearSel.on('click', function (event) { event.stopPropagation(); });
            $monthSel.on('change', function () { loadTopRepresentatives(); });
            $yearSel.on('change', function () { loadTopRepresentatives(); });

            $prevBtn.on('click', function () { turnWidgetPage(-1); });
            $nextBtn.on('click', function () { turnWidgetPage(1); });

            $list.on('click', function (event) {
                var row = event.target.closest ? event.target.closest('[data-rid]') : null;
                if (row) { openRepresentativeModal(Number(row.getAttribute('data-rid')), row.getAttribute('data-rname')); }
            });
        }

        function loadTopRepresentatives() {
            rankState = 'loading';
            widgetPage = 0;
            renderList();

            $.ajax({
                url: VIS.Application.contextUrl + SELF_ENDPOINT + 'GetTopRepresentatives',
                type: 'GET', dataType: 'json', cache: false,
                data: { month: selectedMonth(), year: selectedYear() },
                success: function (res) {
                    var parsed = parseResponse(res);
                    if (parsed.Error || !parsed.Reps) { rankState = 'error'; reps = []; }
                    else { rankState = 'ready'; reps = parsed.Reps; isFullAccess = parsed.IsFullAccess !== false; }
                    renderList();
                },
                error: function () {
                    rankState = 'error'; reps = [];
                    renderList();
                }
            });
        }

        function skeletonHtml() {
            var rows = '';
            for (var i = 0; i < WIDGET_PAGE_SIZE; i++) {
                rows += '<div class="vas287-hrow vas287-skel-row">' +
                    '<span class="vas287-line"><span class="vas287-skel-nm"></span></span>' +
                    '<span class="vas287-track"><span class="vas287-fill"></span></span></div>';
            }
            return rows;
        }

        function renderList() {
            if (rankState === 'loading') {
                $list.html(skeletonHtml());
                $helper.text('');
                $pageTxt.text('');
                $prevBtn.prop('disabled', true);
                $nextBtn.prop('disabled', true);
                return;
            }
            if (rankState === 'error') {
                $list.html('<div class="vas287-empty">' + escapeHtml(label('VAS_287_ErrorState', 'Ranking unavailable')) + '</div>');
                $helper.text('');
                $pageTxt.text('');
                $prevBtn.prop('disabled', true);
                $nextBtn.prop('disabled', true);
                return;
            }
            if (reps.length <= 0) {
                $list.html('<div class="vas287-empty">' + escapeHtml(label('VAS_287_ZeroStatePrefix', 'No orders booked in') + ' ' + periodLabel()) + '</div>');
                $helper.text('');
                $pageTxt.text('');
                $prevBtn.prop('disabled', true);
                $nextBtn.prop('disabled', true);
                return;
            }

            var pages = Math.max(1, Math.ceil(reps.length / WIDGET_PAGE_SIZE));
            if (widgetPage > pages - 1) { widgetPage = pages - 1; }

            var topValue = reps.length ? Number(reps[0].OrderValue || 0) : 0;
            var start = widgetPage * WIDGET_PAGE_SIZE;
            var slice = reps.slice(start, start + WIDGET_PAGE_SIZE);

            // A full page of rows fills the cell edge-to-edge (space-between, per
            // design); a sparse/partial page stacks from the top instead of
            // stretching just 1-4 rows to the two extreme edges of the cell.
            $list.toggleClass('vas287-hlist-top', slice.length < WIDGET_PAGE_SIZE);

            $list.html(slice.map(function (r, i) {
                var pct = topValue > 0 ? Math.max(2, Math.round((Number(r.OrderValue || 0) / topValue) * 100)) : 0;
                var color = BAR_COLORS[(start + i) % BAR_COLORS.length];
                var compactValue = formatINR(r.OrderValue) + ' · ' + formatNum(r.OrderCount);
                var fullValue = formatINR(r.OrderValue) + ' · ' + formatNum(r.OrderCount) + ' SOs';
                return '<button type="button" class="vas287-hrow" data-rid="' + r.RepId + '" data-rname="' + escapeHtml(r.RepName) + '">' +
                    '<span class="vas287-line"><span class="vas287-nm" title="' + escapeHtml(r.RepName) + '">' + escapeHtml(r.RepName) + '</span>' +
                    '<span class="vas287-vl" title="' + escapeHtml(fullValue) + '">' + escapeHtml(compactValue) + '</span></span>' +
                    '<span class="vas287-track"><span class="vas287-fill" style="width:' + pct + '%;background:' + color + '"></span></span></button>';
            }).join(''));

            var helperText = label('VAS_287_Showing', 'Showing') + ' ' + (start + 1) + '–' + (start + slice.length) + ' ' + label('VAS_287_Of', 'of') + ' ' + reps.length + ' · ' + label('VAS_287_RankedBy', 'ranked by SO value');
            if (!isFullAccess) { helperText += ' · ' + label('VAS_287_RestrictedHelper', 'Showing your own performance'); }
            $helper.attr('title', helperText).text(helperText);
            $pageTxt.text((widgetPage + 1) + ' ' + label('VAS_287_Of', 'of') + ' ' + pages);
            $prevBtn.prop('disabled', widgetPage === 0);
            $nextBtn.prop('disabled', widgetPage >= pages - 1);
        }

        function turnWidgetPage(dir) {
            var pages = Math.max(1, Math.ceil(reps.length / WIDGET_PAGE_SIZE));
            var next = Math.min(pages - 1, Math.max(0, widgetPage + dir));
            if (next === widgetPage) { return; }
            widgetPage = next;
            renderList();
        }

        /* ============================================================
         * Modal shell - lives on <body>, not inside $root, so the widget
         * cell's own overflow cannot clip it. Every screen is a
         * { title, subtitle, size, render } config; showScreen() pushes the
         * outgoing config onto cfgStack and paints the new one; backModal()
         * pops and re-renders the popped config from its own live state
         * (repState / lineState) - never a frozen HTML snapshot.
         * ============================================================ */
        function createModal() {
            $mask = $('<div class="vas287-mask" role="dialog" aria-modal="true"></div>');
            $modal = $('<div class="vas287-modal"></div>');
            $mHead = $('<div class="vas287-mhead"></div>');
            var $htxt = $('<div class="vas287-htxt"></div>');
            $mBack = $('<button type="button" class="vas287-xbtn" aria-label="' + escapeHtml(label('VAS_287_Back', 'Back')) + '" hidden>' + icon('back') + '</button>');
            var $titleWrap = $('<div></div>');
            $mTitle = $('<h2></h2>');
            $mSub = $('<div class="vas287-msub"></div>');
            $titleWrap.append($mTitle, $mSub);
            $htxt.append($mBack, $titleWrap);
            var $closeBtn = $('<button type="button" class="vas287-xbtn" aria-label="' + escapeHtml(label('VAS_287_Close', 'Close')) + '">' + icon('close') + '</button>');
            $mHead.append($htxt, $closeBtn);

            $mBody = $('<div class="vas287-mbody"></div>');
            $mFoot = $('<div class="vas287-mfoot"></div>');

            $modal.append($mHead, $mBody, $mFoot);
            $mask.append($modal);
            $('body').append($mask);

            $closeBtn.on('click', closeModal);
            $mBack.on('click', backModal);
            $mask.on('mousedown', function (event) {
                if (event.target === $mask[0]) { closeModal(); }
            });
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
            var ns = '.vas287-' + ($self.AD_UserHomeWidgetID || $self.windowNo || 'widget');

            $(document).on('keydown' + ns, function (event) {
                if (event.key !== 'Escape') { return; }
                if ($mask.hasClass('is-open')) { closeModal(); }
            });
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
            repState = null;
            lineState = null;
        }

        function showLoading(title) {
            $mBack.prop('hidden', !cfgStack.length);
            $mTitle.text(title || '');
            $mSub.text('');
            $mBody.html('<div class="vas287-mstate">…</div>');
            $mFoot.html('');
            $mask.addClass('is-open');
        }

        function showLoadError() {
            $mBody.html('<div class="vas287-mstate">' + escapeHtml(label('VAS_287_LoadError', 'Search is unavailable right now. Try again in a moment.')) + '</div>');
        }

        /* ============================================================
         * Representative drill-down (top-level modal opened from a row click)
         * ============================================================ */
        function openRepresentativeModal(repId, repName) {
            showLoading(repName);

            fetchRepresentativeSummary(repId, function (summaryOk, summary) {
                if (!summaryOk) { showLoadError(); return; }
                fetchRepresentativeOrders(repId, 0, MAX_ROWS_PER_PAGE, function (ordersOk, total, rows) {
                    if (!ordersOk) { showLoadError(); return; }
                    repState = {
                        repId: repId,
                        repName: repName,
                        summary: summary,
                        docs: { page: 0, size: MAX_ROWS_PER_PAGE, total: total, rows: rows }
                    };
                    showScreen(buildRepresentativeCfg(repName), false);
                });
            });
        }

        function buildRepresentativeCfg(repName) {
            return {
                title: repName,
                subtitle: label('VAS_287_SubtitlePrefix', 'Sales orders booked by this representative') + ' · ' + periodLabel(),
                size: '',
                render: renderRepresentativeBody
            };
        }

        function fetchRepresentativeSummary(repId, cb) {
            $.ajax({
                url: VIS.Application.contextUrl + SELF_ENDPOINT + 'GetRepresentativeSummary',
                type: 'GET', dataType: 'json', cache: false,
                data: { repId: repId, month: selectedMonth(), year: selectedYear() },
                success: function (res) {
                    var parsed = parseResponse(res);
                    if (parsed.Error) { cb(false); return; }
                    cb(true, parsed);
                },
                error: function () { cb(false); }
            });
        }

        function fetchRepresentativeOrders(repId, page, size, cb) {
            $.ajax({
                url: VIS.Application.contextUrl + SELF_ENDPOINT + 'GetRepresentativeOrders',
                type: 'GET', dataType: 'json', cache: false,
                data: { repId: repId, month: selectedMonth(), year: selectedYear(), page: page, size: size },
                success: function (res) {
                    var parsed = parseResponse(res);
                    if (parsed.Error) { cb(false); return; }
                    cb(true, Number(parsed.Total || 0), (parsed.Rows || []).map(normalizeOrderRow));
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
            if (code === 'FULL') { return 'vas287-chip-ok'; }
            if (code === 'PARTIAL') { return 'vas287-chip-warn'; }
            return 'vas287-chip-neutral';
        }

        function renderRepresentativeBody() {
            var summary = repState.summary;
            var statsHtml = '<div class="vas287-mstats">' +
                statTile(label('VAS_287_StatOrderValue', 'SO value'), formatINR(summary.OrderValue)) +
                statTile(label('VAS_287_StatOrderCount', 'SOs booked'), formatNum(summary.OrderCount)) +
                statTile(label('VAS_287_StatPendingDelivery', 'Pending delivery'), formatNum(summary.PendingDelivery)) +
                statTile(label('VAS_287_StatAvgCycle', 'Avg cycle'), formatAvgCycle(summary.AvgCycleDays)) +
            '</div>';

            var secHtml = '<div class="vas287-msec">' + escapeHtml(label('VAS_287_SectionHeading', 'Sales orders')) + '</div>';

            $mBody.html(statsHtml + secHtml + '<div class="vas287-mtwrap"><div class="vas287-mtbl" id="vas287-docstbl"></div></div>');
            drawDocumentsTable();

            $mFoot.html('<span class="vas287-foot-note"></span><span><button type="button" class="vas287-btn" id="vas287-mclose">' + escapeHtml(label('VAS_287_Close', 'Close')) + '</button></span>');
            $mFoot.find('#vas287-mclose').on('click', closeModal);
        }

        var DOC_COLS = [
            { key: 'icon', label: '', w: .32 },
            { key: 'so', label: label('VAS_287_ColSoNo', 'SO No'), w: 1.2 },
            { key: 'date', label: label('VAS_287_SoDate', 'SO date'), w: 1 },
            { key: 'customer', label: label('VAS_287_Customer', 'Customer'), w: 1.7 },
            { key: 'wh', label: label('VAS_287_ColWarehouse', 'Warehouse'), w: 1.2 },
            { key: 'rep', label: label('VAS_287_Representative', 'Representative'), w: 1.2 },
            { key: 'value', label: label('VAS_287_ColValue', 'Value'), w: .9, align: 'right' },
            { key: 'delivery', label: label('VAS_287_ColDelivery', 'Delivery'), w: 1.05 },
            { key: 'status', label: label('VAS_287_ColStatus', 'Status'), w: 1.1 }
        ];

        function drawDocumentsTable() {
            var el = document.getElementById('vas287-docstbl');
            if (!el || !repState) { return; }
            var docs = repState.docs;
            var tpl = DOC_COLS.map(function (c) { return 'minmax(0,' + c.w + 'fr)'; }).join(' ');
            var pages = Math.max(1, Math.ceil(docs.total / docs.size));

            var head = '<div class="vas287-mrow vas287-mhead-row" style="grid-template-columns:' + tpl + '">' +
                DOC_COLS.map(function (c) { return '<span class="vas287-cell' + (c.align === 'right' ? ' right' : '') + '" title="' + escapeHtml(c.label) + '">' + escapeHtml(c.label) + '</span>'; }).join('') +
            '</div>';

            var body = docs.rows.map(function (row) {
                var cells = [
                    '<span class="vas287-cell center"><button type="button" class="vas287-iconbtn" data-lines="' + row.SalesOrderId + '" title="' + escapeHtml(row.SalesOrderNumber) + '">' + icon('lines') + '</button></span>',
                    '<span class="vas287-cell"><button type="button" class="vas287-lnk" data-so="' + row.SalesOrderId + '" title="' + escapeHtml(row.SalesOrderNumber) + '">' + escapeHtml(row.SalesOrderNumber) + '</button></span>',
                    cellHtml(formatDateFull(row.SalesOrderDate), 'vas287-c-std'),
                    cellHtml(row.CustomerName, 'vas287-c-prim'),
                    cellHtml(row.WarehouseName || '—', 'vas287-c-std'),
                    cellHtml(row.RepresentativeName || '—', 'vas287-c-std'),
                    cellHtml(formatINR(row.OrderValue), 'vas287-c-emph', 'right'),
                    '<span class="vas287-cell" title="' + escapeHtml(row.DeliveryStatus) + '"><span class="vas287-chip ' + deliveryChipClass(row.DeliveryStatusCode) + '">' + escapeHtml(row.DeliveryStatus) + '</span></span>',
                    '<span class="vas287-cell" title="' + escapeHtml(row.DocumentStatus) + '"><span class="vas287-chip vas287-chip-ok">' + escapeHtml(row.DocumentStatus) + '</span></span>'
                ];
                return '<div class="vas287-mrow" style="grid-template-columns:' + tpl + '">' + cells.join('') + '</div>';
            }).join('');

            var start = docs.page * docs.size;
            var showingLabel = label('VAS_287_Showing', 'Showing') + ' ' + (docs.total ? (start + 1) : 0) + '–' + (start + docs.rows.length) + ' ' + label('VAS_287_Of', 'of') + ' ' + docs.total + ' · ' + label('VAS_287_ClickRowHint', 'click a row to open the record');

            var foot = '<div class="vas287-mtfoot"><span class="vas287-helper">' + escapeHtml(showingLabel) + '</span>' +
                (pages > 1 ?
                    '<span class="vas287-pager">' +
                        '<button type="button" class="vas287-pbtn" data-table="docs" data-dir="-1"' + (docs.page === 0 ? ' disabled' : '') + ' aria-label="' + escapeHtml(label('VAS_287_PrevPage', 'Previous page')) + '">' + icon('prev') + '</button>' +
                        '<span class="vas287-ptxt">' + (docs.page + 1) + ' ' + escapeHtml(label('VAS_287_Of', 'of')) + ' ' + pages + '</span>' +
                        '<button type="button" class="vas287-pbtn" data-table="docs" data-dir="1"' + (docs.page >= pages - 1 ? ' disabled' : '') + ' aria-label="' + escapeHtml(label('VAS_287_NextPage', 'Next page')) + '">' + icon('next') + '</button>' +
                    '</span>' : '<span></span>') +
            '</div>';

            el.innerHTML = head + '<div class="vas287-mbody-rows">' + body + '</div>' + foot;
        }

        function turnPage(table, dir) {
            if (table === 'docs' && repState) {
                var docs = repState.docs;
                var pages = Math.max(1, Math.ceil(docs.total / docs.size));
                var next = Math.min(pages - 1, Math.max(0, docs.page + dir));
                if (next === docs.page) { return; }
                fetchRepresentativeOrders(repState.repId, next, docs.size, function (ok, total, rows) {
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
            if (line.QtyDelivered >= line.QtyOrdered && line.QtyOrdered > 0) { return { text: label('VAS_287_DeliveryFull', 'Fully delivered'), cls: 'vas287-chip-ok' }; }
            if (line.QtyDelivered > 0) { return { text: label('VAS_287_LinePartial', 'Partly delivered'), cls: 'vas287-chip-warn' }; }
            return { text: label('VAS_287_LineInProcess', 'In process'), cls: 'vas287-chip-prop' };
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
            showLoading((repState ? repState.repName : label('VAS_287_Title', 'Representative Wise SO')) + '…');
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
            return '<div class="vas287-mstats">' +
                statTile(label('VAS_287_Customer', 'Customer'), order.CustomerName) +
                statTile(label('VAS_287_SoDate', 'SO date'), formatDateFull(order.SalesOrderDate)) +
                statTile(label('VAS_287_DatePromised', 'Date promised'), formatDateFull(order.DatePromised)) +
                statTile(label('VAS_287_SoValue', 'SO value'), formatINR(order.OrderValue)) +
                statTile(label('VAS_287_ShipFrom', 'Ship from'), order.WarehouseName) +
                statTile(label('VAS_287_DeliveryMode', 'Delivery mode'), order.DeliveryMode) +
                statTile(label('VAS_287_DocumentStatus', 'Document status'), order.DocumentStatus) +
                statTile(label('VAS_287_DeliveryStatus', 'Delivery status'), order.DeliveryStatus) +
            '</div>';
        }

        function renderRecordBody(order, lines) {
            var secHtml = '<div class="vas287-msec">' + escapeHtml(label('VAS_287_SalesOrderLines', 'Sales order lines')) + '</div>';
            $mBody.html(soHeaderStats(order) + secHtml + '<div class="vas287-mtwrap"><div class="vas287-mtbl" id="vas287-linetbl-record"></div></div>');

            lineState = { order: order, lines: lines, page: 0, size: MAX_ROWS_PER_PAGE, tableId: 'vas287-linetbl-record' };
            drawLineTable('vas287-linetbl-record');

            $mFoot.html(lineFootHtml(order, lines));
            $mFoot.find('#vas287-mback').on('click', backModal);
            $mFoot.find('#vas287-mclose').on('click', closeModal);
        }

        function lineFootHtml(order, lines) {
            var qtyOrdered = 0, qtyShort = 0;
            lines.forEach(function (line) {
                qtyOrdered += Number(line.QtyOrdered) || 0;
                var pending = Number(line.QtyPending) || 0;
                var freeStock = Number(line.FreeStock) || 0;
                if (pending > freeStock) { qtyShort += (pending - freeStock); }
            });
            var footNote = lines.length + ' ' + label('VAS_287_LinesSuffix', 'lines') +
                ' · ' + formatNum(qtyOrdered) + ' ' + label('VAS_287_QtyOrderedSuffix', 'qty ordered') +
                ' · ' + order.DeliveryStatus +
                (qtyShort > 0 ? ' · ' + formatNum(qtyShort) + ' ' + label('VAS_287_QtyShortSuffix', 'qty short of stock') : '');

            return '<span class="vas287-foot-note">' + escapeHtml(footNote) + '</span>' +
                '<span><button type="button" class="vas287-btn" id="vas287-mback">' + escapeHtml(label('VAS_287_Back', 'Back')) + '</button> ' +
                '<button type="button" class="vas287-btn" id="vas287-mclose">' + escapeHtml(label('VAS_287_Close', 'Close')) + '</button></span>';
        }

        function openLinesModal(orderId) {
            showLoading(label('VAS_287_SalesOrderLines', 'Sales order lines') + '…');
            fetchOrderDetail(orderId, function (detail) {
                if (!detail) { showLoadError(); return; }
                showScreen(buildLinesCfg(detail.order, detail.lines), true);
            });
        }

        function buildLinesCfg(order, lines) {
            return {
                title: label('VAS_287_SalesOrderLines', 'Sales order lines') + ' · ' + order.SalesOrderNumber,
                subtitle: [order.CustomerName, formatDateShort(order.SalesOrderDate), order.DeliveryStatus].filter(function (p) { return p; }).join(' · '),
                size: 'md',
                render: function () { renderLinesBody(order, lines); }
            };
        }

        function renderLinesBody(order, lines) {
            var qtyOrdered = 0, qtyPending = 0;
            lines.forEach(function (l) { qtyOrdered += Number(l.QtyOrdered) || 0; qtyPending += Number(l.QtyPending) || 0; });

            var breadcrumb = '<div class="vas287-polink">' + escapeHtml(label('VAS_287_SalesOrderPrefix', 'Sales order')) +
                ' <button type="button" class="vas287-lnk" data-so="' + order.SalesOrderId + '">' + escapeHtml(order.SalesOrderNumber) + '</button>' +
                ' · ' + escapeHtml(formatDateFull(order.SalesOrderDate)) + ' · ' + escapeHtml(order.DocumentStatus) + '</div>';

            var stats = '<div class="vas287-mstats">' +
                statTile(label('VAS_287_ColLine', 'Line') + 's', String(lines.length)) +
                statTile(label('VAS_287_SoValue', 'SO value'), formatINR(order.OrderValue)) +
                statTile(label('VAS_287_ColOrdered', 'Ordered'), formatNum(qtyOrdered)) +
                statTile(label('VAS_287_ColPending', 'Pending'), formatNum(qtyPending)) +
            '</div>';

            var secHtml = '<div class="vas287-msec">' + escapeHtml(label('VAS_287_SalesOrderLines', 'Sales order lines')) + '</div>';
            $mBody.html(breadcrumb + stats + secHtml + '<div class="vas287-mtwrap"><div class="vas287-mtbl" id="vas287-linetbl-lines"></div></div>');

            lineState = { order: order, lines: lines, page: 0, size: MAX_ROWS_PER_PAGE, tableId: 'vas287-linetbl-lines' };
            drawLineTable('vas287-linetbl-lines');

            $mFoot.html(lineFootHtml(order, lines));
            $mFoot.find('#vas287-mback').on('click', backModal);
            $mFoot.find('#vas287-mclose').on('click', closeModal);
        }

        var LINE_COLS = [
            { key: 'no', label: label('VAS_287_ColLine', 'Line'), w: .35, align: 'right' },
            { key: 'product', label: label('VAS_287_ColProduct', 'Product'), w: 1.5 },
            { key: 'attr', label: label('VAS_287_ColAttribute', 'Attribute'), w: 1.1 },
            { key: 'uom', label: label('VAS_287_ColUom', 'UoM'), w: .5 },
            { key: 'ordered', label: label('VAS_287_ColOrdered', 'Ordered'), w: .7, align: 'right' },
            { key: 'delivered', label: label('VAS_287_ColDelivered', 'Delivered'), w: .7, align: 'right' },
            { key: 'pending', label: label('VAS_287_ColPending', 'Pending'), w: .7, align: 'right' },
            { key: 'instock', label: label('VAS_287_ColInStock', 'In stock'), w: .7, align: 'right' },
            { key: 'rate', label: label('VAS_287_ColRate', 'Rate'), w: .7, align: 'right' },
            { key: 'amount', label: label('VAS_287_ColAmount', 'Amount'), w: .9, align: 'right' },
            { key: 'status', label: label('VAS_287_ColLineStatus', 'Line status'), w: 1 }
        ];

        function drawLineTable(tableId) {
            var el = document.getElementById(tableId);
            if (!el || !lineState) { return; }

            var tpl = LINE_COLS.map(function (c) { return 'minmax(0,' + c.w + 'fr)'; }).join(' ');
            var pages = Math.max(1, Math.ceil(lineState.lines.length / lineState.size));
            if (lineState.page > pages - 1) { lineState.page = pages - 1; }
            var start = lineState.page * lineState.size;
            var slice = lineState.lines.slice(start, start + lineState.size);

            var head = '<div class="vas287-mrow vas287-mhead-row" style="grid-template-columns:' + tpl + '">' +
                LINE_COLS.map(function (c) { return '<span class="vas287-cell' + (c.align === 'right' ? ' right' : '') + '" title="' + escapeHtml(c.label) + '">' + escapeHtml(c.label) + '</span>'; }).join('') +
            '</div>';

            var body = slice.map(function (line) {
                var st = lineStatus(line);
                var cells = [
                    cellHtml(String(line.LineNo), 'vas287-c-std', 'right'),
                    cellHtml(line.ProductName, 'vas287-c-prim'),
                    cellHtml(line.AttributeText, 'vas287-c-std'),
                    cellHtml(line.UomName, 'vas287-c-std'),
                    cellHtml(formatNum(line.QtyOrdered), 'vas287-c-std', 'right'),
                    cellHtml(formatNum(line.QtyDelivered), 'vas287-c-std', 'right'),
                    cellHtml(formatNum(line.QtyPending), 'vas287-c-prim', 'right'),
                    cellHtml(formatNum(line.FreeStock), (line.FreeStock < line.QtyPending ? 'vas287-c-short' : 'vas287-c-ok'), 'right'),
                    cellHtml('₹ ' + formatNum(line.Rate), 'vas287-c-std', 'right'),
                    cellHtml(formatINR(line.Amount), 'vas287-c-emph', 'right')
                ].join('') + '<span class="vas287-cell"><span class="vas287-chip ' + st.cls + '" title="' + escapeHtml(st.text) + '">' + escapeHtml(st.text) + '</span></span>';
                return '<div class="vas287-mrow" style="grid-template-columns:' + tpl + '">' + cells + '</div>';
            }).join('');

            var showingLabel = label('VAS_287_Showing', 'Showing') + ' ' + (lineState.lines.length ? (start + 1) : 0) + '–' + (start + slice.length) + ' ' + label('VAS_287_Of', 'of') + ' ' + lineState.lines.length;

            var foot = '<div class="vas287-mtfoot"><span class="vas287-helper">' + escapeHtml(showingLabel) + '</span>' +
                (pages > 1 ?
                    '<span class="vas287-pager">' +
                        '<button type="button" class="vas287-pbtn" data-table="lines" data-dir="-1"' + (lineState.page === 0 ? ' disabled' : '') + ' aria-label="' + escapeHtml(label('VAS_287_PrevPage', 'Previous page')) + '">' + icon('prev') + '</button>' +
                        '<span class="vas287-ptxt">' + (lineState.page + 1) + ' ' + escapeHtml(label('VAS_287_Of', 'of')) + ' ' + pages + '</span>' +
                        '<button type="button" class="vas287-pbtn" data-table="lines" data-dir="1"' + (lineState.page >= pages - 1 ? ' disabled' : '') + ' aria-label="' + escapeHtml(label('VAS_287_NextPage', 'Next page')) + '">' + icon('next') + '</button>' +
                    '</span>' : '<span></span>') +
            '</div>';

            el.innerHTML = head + '<div class="vas287-mbody-rows">' + body + '</div>' + foot;
        }

        /* Sizes each visible table's rows-per-page to the space actually left in
           the modal body, so the body never grows an inner scrollbar. */
        function fitTable(id, currentSize, onResize) {
            var el = document.getElementById(id);
            if (!el) { return; }
            var avail = el.clientHeight;
            if (avail < 40) { return; }
            var head = el.querySelector('.vas287-mhead-row');
            var foot = el.querySelector('.vas287-mtfoot');
            var row = el.querySelector('.vas287-mbody-rows .vas287-mrow');
            if (!head || !row) { return; }
            var rowHeight = row.getBoundingClientRect().height || 30;
            var used = head.getBoundingClientRect().height + (foot ? foot.getBoundingClientRect().height + 8 : 0);
            var n = Math.floor((avail - used) / rowHeight);
            n = Math.max(MIN_ROWS_PER_PAGE, Math.min(MAX_ROWS_PER_PAGE, n));
            if (n !== currentSize) { onResize(n); }
        }

        function fitAllTables() {
            if (!$mask.hasClass('is-open')) { return; }

            if (repState) {
                fitTable('vas287-docstbl', repState.docs.size, function (n) {
                    var docs = repState.docs;
                    var newPages = Math.max(1, Math.ceil(docs.total / n));
                    var page = Math.min(docs.page, newPages - 1);
                    fetchRepresentativeOrders(repState.repId, page, n, function (ok, total, rows) {
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
            loadTopRepresentatives();
        };

        this.getRoot = function () { return $root; };

        this.disposeComponent = function () {
            var ns = '.vas287-' + ($self.AD_UserHomeWidgetID || $self.windowNo || 'widget');
            $(document).off(ns);
            $(window).off(ns);
            if ($mask) { $mask.remove(); }
            $root.remove();
        };

        this.refreshWidget = function () { loadTopRepresentatives(); };
    };

    VAS.VAS_287_RepresentativeWiseSOWidget.prototype.widgetFirevalueChanged = function (value) {
        if (this.listener) { this.listener.widgetFirevalueChanged(value); }
    };

    VAS.VAS_287_RepresentativeWiseSOWidget.prototype.addChangeListener = function (listener) {
        this.listener = listener;
    };

    VAS.VAS_287_RepresentativeWiseSOWidget.prototype.init = function (windowNo, frame) {
        this.frame = frame;
        this.AD_UserHomeWidgetID = frame.widgetInfo.AD_UserHomeWidgetID;
        this.windowNo = windowNo;
        this.Initalize();
        this.frame.getContentGrid().append(this.getRoot());
        ensureDashInlineSizeVar(this.getRoot());
    };

    VAS.VAS_287_RepresentativeWiseSOWidget.prototype.widgetSizeChange = function () { };

    VAS.VAS_287_RepresentativeWiseSOWidget.prototype.dispose = function () {
        if (this.disposeComponent) { this.disposeComponent(); }
    };

})(VAS, jQuery);
