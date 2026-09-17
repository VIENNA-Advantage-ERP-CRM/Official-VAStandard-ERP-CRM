/**
 * VAS_281 Category Wise SO Widget (Sales Order dashboard, 3x2 donut + legend)
 * Purpose - Product-mix composition: share of booked (DocStatus 'CO'/'CL') Sales
 *           Order value by DIRECT product category (M_Product.M_Product_Category_ID,
 *           no parent-hierarchy roll-up), for a selected Month/Year. Value is
 *           attributed at LINE level (C_OrderLine.LineNetAmt) - a single order can
 *           legitimately contribute to several categories. Top 4 real categories
 *           show individually; everything else combines into one explicit "Other"
 *           segment carrying its own underlying category id set (never a hard-coded
 *           fake id). Percentages are rounded server-side with the rounding residue
 *           assigned to the largest segment, so the legend always sums to 100.
 *
 * Design  - 14-category-wise-so.html / .md: glass 3x2 tile, header with title +
 *           subtitle + a Month/Year period filter (both selects call
 *           stopPropagation so they never trigger a segment/legend click), a donut
 *           (SVG, 0 0 42 42 viewBox, rotated -90deg so the first segment starts at
 *           twelve o'clock, r=15.9/stroke-width=8 so the circumference is ~100
 *           units and a segment's stroke-dasharray is its percent directly) beside
 *           an interactive legend. Hovering EITHER the donut segment or its legend
 *           row rewrites the centre label to "<pct>% / <category>"; leaving either
 *           restores the default "<total> / Total SO value" - both surfaces drive
 *           the same hover()/reset() functions so they never disagree. Segment and
 *           legend row are equivalent click targets opening the same drill-down
 *           modal (stat strip + that category's Sales Orders, standard column set),
 *           reusing the same shared record/lines child modals as every other widget.
 *
 * Backend - VAS_281_CategoryWiseSOWidget/GetCategoryMix       (GET month,year -> combined top-4+Other segments + total)
 *           VAS_281_CategoryWiseSOWidget/GetCategoryOrders    (GET categoryIds,month,year,page,size -> stat strip + paginated documents)
 *           VAS_281_CategoryWiseSOWidget/GetSalesOrderDetail  (GET id -> order + lines)
 *
 * ── Labels / Message Keys ─────────────────────────────────────────────────
 *  #  | Current Text                                                          | Message Key
 * ----+------------------------------------------------------------------------+--------------------------------
 *  1  | Category Wise SO                                                      | VAS_281_Title
 *  2  | Share of SO value                                                     | VAS_281_Subtitle
 *  3  | Other                                                                 | VAS_281_OtherCategory
 *  4  | Total SO value                                                        | VAS_281_TotalCaption
 *  5  | No orders booked                                                      | VAS_281_ZeroState
 *  6  | Mix unavailable                                                       | VAS_281_ErrorState
 *  7  | Category sales orders                                                 | VAS_281_SubtitlePrefix
 *  8  | of SO value                                                           | VAS_281_OfSoValueSuffix
 *  9  | Category value                                                        | VAS_281_StatCategoryValue
 * 10  | Share                                                                 | VAS_281_StatShare
 * 11  | SOs                                                                   | VAS_281_StatSos
 * 12  | Customers                                                             | VAS_281_StatCustomers
 * 13  | Sales orders in this category                                         | VAS_281_SectionHeading
 * 14  | click a row to open the record                                        | VAS_281_ClickRowHint
 * 15  | SO No                                                                 | VAS_281_ColSoNo
 * 16  | SO date                                                               | VAS_281_SoDate
 * 17  | Customer                                                              | VAS_281_Customer
 * 18  | Warehouse                                                             | VAS_281_ColWarehouse
 * 19  | Representative                                                        | VAS_281_Representative
 * 20  | Value                                                                 | VAS_281_ColValue
 * 21  | Delivery                                                              | VAS_281_ColDelivery
 * 22  | Status                                                                | VAS_281_ColStatus
 * 23  | Back                                                                  | VAS_281_Back
 * 24  | Close                                                                 | VAS_281_Close
 * 25  | Date promised                                                         | VAS_281_DatePromised
 * 26  | SO value                                                              | VAS_281_SoValue
 * 27  | Ship from                                                             | VAS_281_ShipFrom
 * 28  | Delivery mode                                                         | VAS_281_DeliveryMode
 * 29  | Document status                                                       | VAS_281_DocumentStatus
 * 30  | Delivery status                                                       | VAS_281_DeliveryStatus
 * 31  | Sales order lines                                                     | VAS_281_SalesOrderLines
 * 32  | Line                                                                  | VAS_281_ColLine
 * 33  | Product                                                               | VAS_281_ColProduct
 * 34  | Attribute                                                             | VAS_281_ColAttribute
 * 35  | UoM                                                                   | VAS_281_ColUom
 * 36  | Ordered                                                               | VAS_281_ColOrdered
 * 37  | Delivered                                                             | VAS_281_ColDelivered
 * 38  | Pending                                                               | VAS_281_ColPending
 * 39  | In stock                                                              | VAS_281_ColInStock
 * 40  | Rate                                                                  | VAS_281_ColRate
 * 41  | Amount                                                                | VAS_281_ColAmount
 * 42  | Line status                                                           | VAS_281_ColLineStatus
 * 43  | Fully delivered                                                       | VAS_281_DeliveryFull
 * 44  | Partial                                                               | VAS_281_DeliveryPartial
 * 45  | Pending                                                               | VAS_281_DeliveryPending
 * 46  | Partly delivered                                                      | VAS_281_LinePartial
 * 47  | In process                                                            | VAS_281_LineInProcess
 * 48  | Sales order                                                           | VAS_281_SalesOrderPrefix
 * 49  | lines                                                                 | VAS_281_LinesSuffix
 * 50  | qty ordered                                                           | VAS_281_QtyOrderedSuffix
 * 51  | qty short of stock                                                    | VAS_281_QtyShortSuffix
 * 52  | Previous page                                                         | VAS_281_PrevPage
 * 53  | Next page                                                             | VAS_281_NextPage
 * 54  | of                                                                    | VAS_281_Of
 * 55  | Showing                                                               | VAS_281_Showing
 * 56  | Search is unavailable right now. Try again in a moment.               | VAS_281_LoadError
 */
; VAS = window.VAS || {};

; (function (VAS, $) {

    var MAX_ROWS_PER_PAGE = 10;
    var MIN_ROWS_PER_PAGE = 2;
    var MONTHS = ['Jan', 'Feb', 'Mar', 'Apr', 'May', 'Jun', 'Jul', 'Aug', 'Sep', 'Oct', 'Nov', 'Dec'];
    var MIN_YEAR = 2024;
    var YEAR_SPAN = 3;

    // Position-based palette: first 4 real segments, 5th reserved for "Other" (which
    // the server always places last, so index 4 is only ever reached by it).
    var SEGMENT_COLORS = ['#A9D2FF', '#A3E0D4', '#FFDCA1', '#CFC9F5', '#FFC7C7'];

    var now = new Date();
    var CUR_M = now.getMonth();
    var CUR_Y = now.getFullYear();

    /* design.md §Widget Header / §Measurement Setup: keep --dash-inline-size on
       :root equal to the dashboard container's current pixel width so the widget
       clamp resolves against the dashboard's visible width, not the viewport. A
       single document-level ResizeObserver serves every widget (matches VAS_269-280). */
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

    VAS.VAS_281_CategoryWiseSOWidget = function () {

        this.frame;
        this.windowNo;

        var $self = this;
        var $root = $('<div class="vas281-root">');
        var $shell, $svg, $ctr, $legend, $monthSel, $yearSel;
        var $mask, $modal, $mHead, $mTitle, $mSub, $mBack, $mBody, $mFoot;

        var SELF_ENDPOINT = 'VAS_281_CategoryWiseSOWidget/';

        var mixState = 'loading'; // 'loading' | 'ready' | 'error'
        var segments = [];        // combined top-4+Other segments from the server
        var totalValue = 0;

        var docState = null;      // { categoryIds, categoryName, percent, summary, docs:{page,size,total,rows} }
        var lineState = null;     // { order, lines, page, size, tableId }
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
            return '<span class="vas281-cell' + (align === 'right' ? ' right' : '') + ' ' + cls + '" title="' + escapeHtml(value) + '">' + escapeHtml(value) + '</span>';
        }

        function statTile(l, v) {
            return '<div class="vas281-mstat"><div class="l">' + escapeHtml(l) + '</div><div class="v" title="' + escapeHtml(v) + '">' + escapeHtml(v) + '</div></div>';
        }

        /* ============================================================
         * Widget shell: header (title/subtitle + Month/Year filter) + donut/legend
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
            $shell = $('<div class="vas281-shell"></div>');

            var $head = $('<div class="vas281-head"></div>');
            var $htxt = $('<div class="vas281-head-txt"></div>');
            $htxt.append('<p class="vas281-title">' + escapeHtml(label('VAS_281_Title', 'Category Wise SO')) + '</p>');
            $htxt.append('<p class="vas281-sub">' + escapeHtml(label('VAS_281_Subtitle', 'Share of SO value')) + '</p>');

            var $filter = $('<div class="vas281-mfilter"></div>');
            $monthSel = $('<select class="vas281-msel" aria-label="Month"></select>');
            $yearSel = $('<select class="vas281-msel" aria-label="Year"></select>');
            fillMonthSelect($monthSel);
            fillYearSelect($yearSel);
            $filter.append($monthSel, $yearSel);

            $head.append($htxt, $filter);

            var $catwrap = $('<div class="vas281-catwrap"></div>');
            var $donut = $('<div class="vas281-donut"></div>');
            $svg = $('<svg viewBox="0 0 42 42" width="100%" height="100%" style="transform:rotate(-90deg)"></svg>');
            $ctr = $('<div class="vas281-ctr"></div>');
            $donut.append($svg, $ctr);
            $legend = $('<div class="vas281-catlegend"></div>');
            $catwrap.append($donut, $legend);

            $shell.append($head, $catwrap);
            $root.append($shell);

            $monthSel.on('click', function (event) { event.stopPropagation(); });
            $yearSel.on('click', function (event) { event.stopPropagation(); });
            $monthSel.on('change', function () { loadCategoryMix(); });
            $yearSel.on('change', function () { loadCategoryMix(); });

            $svg.on('mouseover', function (event) {
                var seg = event.target.closest ? event.target.closest('[data-idx]') : null;
                if (seg) { hoverSegment(Number(seg.getAttribute('data-idx'))); }
            });
            $svg.on('mouseout', resetCenter);
            $svg.on('click', function (event) {
                var seg = event.target.closest ? event.target.closest('[data-idx]') : null;
                if (seg) { openCategoryModal(Number(seg.getAttribute('data-idx'))); }
            });

            $legend.on('mouseover', function (event) {
                var row = event.target.closest ? event.target.closest('[data-idx]') : null;
                if (row) { hoverSegment(Number(row.getAttribute('data-idx'))); }
            });
            $legend.on('mouseout', resetCenter);
            $legend.on('click', function (event) {
                var row = event.target.closest ? event.target.closest('[data-idx]') : null;
                if (row) { openCategoryModal(Number(row.getAttribute('data-idx'))); }
            });
        }

        function resetCenter() {
            if (mixState === 'loading') { $ctr.html(''); return; }
            if (mixState === 'error') {
                $ctr.html('<span>' + escapeHtml(label('VAS_281_ErrorState', 'Mix unavailable')) + '</span>');
                return;
            }
            if (totalValue <= 0) {
                $ctr.html('<b>₹ 0</b><span>' + escapeHtml(label('VAS_281_ZeroState', 'No orders booked')) + '</span>');
                return;
            }
            $ctr.html('<b>' + escapeHtml(formatINR(totalValue)) + '</b><span>' + escapeHtml(label('VAS_281_TotalCaption', 'Total SO value')) + '</span>');
        }

        function hoverSegment(idx) {
            var seg = segments[idx];
            if (!seg) { return; }
            $ctr.html('<b>' + seg.Percent + '%</b><span>' + escapeHtml(seg.CategoryName) + '</span>');
        }

        function loadCategoryMix() {
            mixState = 'loading';
            renderMix();

            $.ajax({
                url: VIS.Application.contextUrl + SELF_ENDPOINT + 'GetCategoryMix',
                type: 'GET', dataType: 'json', cache: false,
                data: { month: selectedMonth(), year: selectedYear() },
                success: function (res) {
                    var parsed = parseResponse(res);
                    if (parsed.Error || !parsed.Categories) { mixState = 'error'; segments = []; totalValue = 0; }
                    else { mixState = 'ready'; segments = parsed.Categories; totalValue = Number(parsed.TotalValue || 0); }
                    renderMix();
                },
                error: function () {
                    mixState = 'error'; segments = []; totalValue = 0;
                    renderMix();
                }
            });
        }

        function skeletonLegend() {
            var rows = '';
            for (var i = 0; i < 4; i++) {
                rows += '<div class="vas281-skel-row"><span class="vas281-skel-dot"></span><span class="vas281-skel-bar"></span></div>';
            }
            return rows;
        }

        function backgroundRingSvg() {
            return '<circle cx="21" cy="21" r="15.9" fill="none" stroke="#EEF3F8" stroke-width="8"/>';
        }

        function renderMix() {
            if (mixState === 'loading') {
                $svg.html(backgroundRingSvg());
                $legend.html(skeletonLegend());
                resetCenter();
                return;
            }
            if (mixState === 'error') {
                $svg.html(backgroundRingSvg());
                $legend.html('');
                resetCenter();
                return;
            }
            if (totalValue <= 0 || segments.length === 0) {
                $svg.html(backgroundRingSvg());
                $legend.html('<div class="vas281-empty">' + escapeHtml(label('VAS_281_ZeroState', 'No orders booked')) + '</div>');
                resetCenter();
                return;
            }

            // Ring math per design.md §Donut: r=15.9, stroke-width=8 makes the
            // circumference ~100 units, so a segment's stroke-dasharray is its
            // percent directly - "<pct-0.8> <100-pct+0.8>" leaves a small gap
            // between segments; stroke-dashoffset walks the cumulative percent so
            // segments chain around the ring without overlapping.
            var svgHtml = backgroundRingSvg();
            var cumulative = 0;
            segments.forEach(function (seg, i) {
                var color = SEGMENT_COLORS[i % SEGMENT_COLORS.length];
                var pct = seg.Percent;
                svgHtml += '<circle class="vas281-seg" data-idx="' + i + '" cx="21" cy="21" r="15.9" fill="none" stroke="' + color + '" stroke-width="8" ' +
                    'stroke-dasharray="' + (pct - 0.8) + ' ' + (100 - pct + 0.8) + '" stroke-dashoffset="' + (-cumulative) + '">' +
                    '<title>' + escapeHtml(seg.CategoryName) + ' — ' + pct + '%</title></circle>';
                cumulative += pct;
            });
            $svg.html(svgHtml);

            $legend.html(segments.map(function (seg, i) {
                var color = SEGMENT_COLORS[i % SEGMENT_COLORS.length];
                return '<button type="button" class="vas281-catrow" data-idx="' + i + '">' +
                    '<span class="vas281-dot" style="background:' + color + '"></span>' +
                    '<span class="vas281-cn" title="' + escapeHtml(seg.CategoryName) + '">' + escapeHtml(seg.CategoryName) + '</span>' +
                    '<span class="vas281-camt" title="' + escapeHtml(formatINR(seg.Value)) + '">' + escapeHtml(formatINR(seg.Value)) + '</span>' +
                    '<span class="vas281-cp">' + seg.Percent + '%</span></button>';
            }).join(''));

            resetCenter();
        }

        /* ============================================================
         * Modal shell - lives on <body>, not inside $root, so the widget
         * cell's own overflow cannot clip it. Every screen is a
         * { title, subtitle, size, render } config; showScreen() pushes the
         * outgoing config onto cfgStack and paints the new one; backModal()
         * pops and re-renders the popped config from its own live state
         * (docState / lineState) - never a frozen HTML snapshot.
         * ============================================================ */
        function createModal() {
            $mask = $('<div class="vas281-mask" role="dialog" aria-modal="true"></div>');
            $modal = $('<div class="vas281-modal"></div>');
            $mHead = $('<div class="vas281-mhead"></div>');
            var $htxt = $('<div class="vas281-htxt"></div>');
            $mBack = $('<button type="button" class="vas281-xbtn" aria-label="' + escapeHtml(label('VAS_281_Back', 'Back')) + '" hidden>' + icon('back') + '</button>');
            var $titleWrap = $('<div></div>');
            $mTitle = $('<h2></h2>');
            $mSub = $('<div class="vas281-msub"></div>');
            $titleWrap.append($mTitle, $mSub);
            $htxt.append($mBack, $titleWrap);
            var $closeBtn = $('<button type="button" class="vas281-xbtn" aria-label="' + escapeHtml(label('VAS_281_Close', 'Close')) + '">' + icon('close') + '</button>');
            $mHead.append($htxt, $closeBtn);

            $mBody = $('<div class="vas281-mbody"></div>');
            $mFoot = $('<div class="vas281-mfoot"></div>');

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
            var ns = '.vas281-' + ($self.AD_UserHomeWidgetID || $self.windowNo || 'widget');

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
            docState = null;
            lineState = null;
        }

        function showLoading(title) {
            $mBack.prop('hidden', !cfgStack.length);
            $mTitle.text(title || '');
            $mSub.text('');
            $mBody.html('<div class="vas281-mstate">…</div>');
            $mFoot.html('');
            $mask.addClass('is-open');
        }

        function showLoadError() {
            $mBody.html('<div class="vas281-mstate">' + escapeHtml(label('VAS_281_LoadError', 'Search is unavailable right now. Try again in a moment.')) + '</div>');
        }

        /* ============================================================
         * Category drill-down (top-level modal opened from a segment/legend click)
         * ============================================================ */
        function openCategoryModal(idx) {
            var seg = segments[idx];
            if (!seg) { return; }

            showLoading(seg.CategoryName);

            var categoryIds = seg.CategoryIds.join(',');
            fetchCategoryOrders(categoryIds, 0, MAX_ROWS_PER_PAGE, function (ok, summary, total, rows) {
                if (!ok) { showLoadError(); return; }
                docState = { categoryIds: categoryIds, categoryName: seg.CategoryName, percent: seg.Percent, summary: summary, docs: { page: 0, size: MAX_ROWS_PER_PAGE, total: total, rows: rows } };
                showScreen(buildCategoryCfg(seg), false);
            });
        }

        function buildCategoryCfg(seg) {
            return {
                title: seg.CategoryName,
                subtitle: label('VAS_281_SubtitlePrefix', 'Category sales orders') + ' · ' + periodLabel() + ' · ' + seg.Percent + '% ' + label('VAS_281_OfSoValueSuffix', 'of SO value'),
                size: '',
                render: renderCategoryBody
            };
        }

        function fetchCategoryOrders(categoryIds, page, size, cb) {
            $.ajax({
                url: VIS.Application.contextUrl + SELF_ENDPOINT + 'GetCategoryOrders',
                type: 'GET', dataType: 'json', cache: false,
                data: { categoryIds: categoryIds, month: selectedMonth(), year: selectedYear(), page: page, size: size },
                success: function (res) {
                    var parsed = parseResponse(res);
                    if (parsed.Error) { cb(false); return; }
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
            if (code === 'FULL') { return 'vas281-chip-ok'; }
            if (code === 'PARTIAL') { return 'vas281-chip-warn'; }
            return 'vas281-chip-neutral';
        }

        function renderCategoryBody() {
            var summary = docState.summary;
            var statsHtml = '<div class="vas281-mstats">' +
                statTile(label('VAS_281_StatCategoryValue', 'Category value'), formatINR(summary.Value)) +
                statTile(label('VAS_281_StatShare', 'Share'), summary.Percent + '%') +
                statTile(label('VAS_281_StatSos', 'SOs'), formatNum(summary.OrderCount)) +
                statTile(label('VAS_281_StatCustomers', 'Customers'), formatNum(summary.CustomerCount)) +
            '</div>';

            var secHtml = '<div class="vas281-msec">' + escapeHtml(label('VAS_281_SectionHeading', 'Sales orders in this category')) + '</div>';

            if (docState.docs.total <= 0) {
                $mBody.html(statsHtml + secHtml + '<div class="vas281-mstate">' + escapeHtml(label('VAS_281_LoadError', 'Search is unavailable right now. Try again in a moment.')) + '</div>');
            } else {
                $mBody.html(statsHtml + secHtml + '<div class="vas281-mtwrap"><div class="vas281-mtbl" id="vas281-docstbl"></div></div>');
                drawDocumentsTable();
            }

            $mFoot.html('<span class="vas281-foot-note"></span><span><button type="button" class="vas281-btn" id="vas281-mclose">' + escapeHtml(label('VAS_281_Close', 'Close')) + '</button></span>');
            $mFoot.find('#vas281-mclose').on('click', closeModal);
        }

        var DOC_COLS = [
            { key: 'icon', label: '', w: .32 },
            { key: 'so', label: label('VAS_281_ColSoNo', 'SO No'), w: 1.2 },
            { key: 'date', label: label('VAS_281_SoDate', 'SO date'), w: 1 },
            { key: 'customer', label: label('VAS_281_Customer', 'Customer'), w: 1.7 },
            { key: 'wh', label: label('VAS_281_ColWarehouse', 'Warehouse'), w: 1.2 },
            { key: 'rep', label: label('VAS_281_Representative', 'Representative'), w: 1.2 },
            { key: 'value', label: label('VAS_281_ColValue', 'Value'), w: .9, align: 'right' },
            { key: 'delivery', label: label('VAS_281_ColDelivery', 'Delivery'), w: 1.05 },
            { key: 'status', label: label('VAS_281_ColStatus', 'Status'), w: 1.1 }
        ];

        function drawDocumentsTable() {
            var el = document.getElementById('vas281-docstbl');
            if (!el || !docState) { return; }
            var docs = docState.docs;
            var tpl = DOC_COLS.map(function (c) { return 'minmax(0,' + c.w + 'fr)'; }).join(' ');
            var pages = Math.max(1, Math.ceil(docs.total / docs.size));

            var head = '<div class="vas281-mrow vas281-mhead-row" style="grid-template-columns:' + tpl + '">' +
                DOC_COLS.map(function (c) { return '<span class="vas281-cell' + (c.align === 'right' ? ' right' : '') + '" title="' + escapeHtml(c.label) + '">' + escapeHtml(c.label) + '</span>'; }).join('') +
            '</div>';

            var body = docs.rows.map(function (row) {
                var cells = [
                    '<span class="vas281-cell center"><button type="button" class="vas281-iconbtn" data-lines="' + row.SalesOrderId + '" title="' + escapeHtml(row.SalesOrderNumber) + '">' + icon('lines') + '</button></span>',
                    '<span class="vas281-cell"><button type="button" class="vas281-lnk" data-so="' + row.SalesOrderId + '" title="' + escapeHtml(row.SalesOrderNumber) + '">' + escapeHtml(row.SalesOrderNumber) + '</button></span>',
                    cellHtml(formatDateFull(row.SalesOrderDate), 'vas281-c-std'),
                    cellHtml(row.CustomerName, 'vas281-c-prim'),
                    cellHtml(row.WarehouseName || '—', 'vas281-c-std'),
                    cellHtml(row.RepresentativeName || '—', 'vas281-c-std'),
                    cellHtml(formatINR(row.OrderValue), 'vas281-c-emph', 'right'),
                    '<span class="vas281-cell" title="' + escapeHtml(row.DeliveryStatus) + '"><span class="vas281-chip ' + deliveryChipClass(row.DeliveryStatusCode) + '">' + escapeHtml(row.DeliveryStatus) + '</span></span>',
                    '<span class="vas281-cell" title="' + escapeHtml(row.DocumentStatus) + '"><span class="vas281-chip vas281-chip-ok">' + escapeHtml(row.DocumentStatus) + '</span></span>'
                ];
                return '<div class="vas281-mrow" style="grid-template-columns:' + tpl + '">' + cells.join('') + '</div>';
            }).join('');

            var start = docs.page * docs.size;
            var showingLabel = label('VAS_281_Showing', 'Showing') + ' ' + (docs.total ? (start + 1) : 0) + '–' + (start + docs.rows.length) + ' ' + label('VAS_281_Of', 'of') + ' ' + docs.total + ' · ' + docState.categoryName;

            var foot = '<div class="vas281-mtfoot"><span class="vas281-helper">' + escapeHtml(showingLabel) + '</span>' +
                (pages > 1 ?
                    '<span class="vas281-pager">' +
                        '<button type="button" class="vas281-pbtn" data-table="docs" data-dir="-1"' + (docs.page === 0 ? ' disabled' : '') + ' aria-label="' + escapeHtml(label('VAS_281_PrevPage', 'Previous page')) + '">' + icon('prev') + '</button>' +
                        '<span class="vas281-ptxt">' + (docs.page + 1) + ' ' + escapeHtml(label('VAS_281_Of', 'of')) + ' ' + pages + '</span>' +
                        '<button type="button" class="vas281-pbtn" data-table="docs" data-dir="1"' + (docs.page >= pages - 1 ? ' disabled' : '') + ' aria-label="' + escapeHtml(label('VAS_281_NextPage', 'Next page')) + '">' + icon('next') + '</button>' +
                    '</span>' : '<span></span>') +
            '</div>';

            el.innerHTML = head + '<div class="vas281-mbody-rows">' + body + '</div>' + foot;
        }

        function turnPage(table, dir) {
            if (table === 'docs' && docState) {
                var docs = docState.docs;
                var pages = Math.max(1, Math.ceil(docs.total / docs.size));
                var next = Math.min(pages - 1, Math.max(0, docs.page + dir));
                if (next === docs.page) { return; }
                fetchCategoryOrders(docState.categoryIds, next, docs.size, function (ok, summary, total, rows) {
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
            if (line.QtyDelivered >= line.QtyOrdered && line.QtyOrdered > 0) { return { text: label('VAS_281_DeliveryFull', 'Fully delivered'), cls: 'vas281-chip-ok' }; }
            if (line.QtyDelivered > 0) { return { text: label('VAS_281_LinePartial', 'Partly delivered'), cls: 'vas281-chip-warn' }; }
            return { text: label('VAS_281_LineInProcess', 'In process'), cls: 'vas281-chip-neutral' };
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
            showLoading((docState ? docState.categoryName : label('VAS_281_Title', 'Category Wise SO')) + '…');
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
            return '<div class="vas281-mstats">' +
                statTile(label('VAS_281_Customer', 'Customer'), order.CustomerName) +
                statTile(label('VAS_281_SoDate', 'SO date'), formatDateFull(order.SalesOrderDate)) +
                statTile(label('VAS_281_DatePromised', 'Date promised'), formatDateFull(order.DatePromised)) +
                statTile(label('VAS_281_SoValue', 'SO value'), formatINR(order.OrderValue)) +
                statTile(label('VAS_281_ShipFrom', 'Ship from'), order.WarehouseName) +
                statTile(label('VAS_281_DeliveryMode', 'Delivery mode'), order.DeliveryMode) +
                statTile(label('VAS_281_DocumentStatus', 'Document status'), order.DocumentStatus) +
                statTile(label('VAS_281_DeliveryStatus', 'Delivery status'), order.DeliveryStatus) +
            '</div>';
        }

        function renderRecordBody(order, lines) {
            var secHtml = '<div class="vas281-msec">' + escapeHtml(label('VAS_281_SalesOrderLines', 'Sales order lines')) + '</div>';
            $mBody.html(soHeaderStats(order) + secHtml + '<div class="vas281-mtwrap"><div class="vas281-mtbl" id="vas281-linetbl-record"></div></div>');

            lineState = { order: order, lines: lines, page: 0, size: MAX_ROWS_PER_PAGE, tableId: 'vas281-linetbl-record' };
            drawLineTable('vas281-linetbl-record');

            $mFoot.html(lineFootHtml(order, lines));
            $mFoot.find('#vas281-mback').on('click', backModal);
            $mFoot.find('#vas281-mclose').on('click', closeModal);
        }

        function lineFootHtml(order, lines) {
            var qtyOrdered = 0, qtyShort = 0;
            lines.forEach(function (line) {
                qtyOrdered += Number(line.QtyOrdered) || 0;
                var pending = Number(line.QtyPending) || 0;
                var freeStock = Number(line.FreeStock) || 0;
                if (pending > freeStock) { qtyShort += (pending - freeStock); }
            });
            var footNote = lines.length + ' ' + label('VAS_281_LinesSuffix', 'lines') +
                ' · ' + formatNum(qtyOrdered) + ' ' + label('VAS_281_QtyOrderedSuffix', 'qty ordered') +
                ' · ' + order.DeliveryStatus +
                (qtyShort > 0 ? ' · ' + formatNum(qtyShort) + ' ' + label('VAS_281_QtyShortSuffix', 'qty short of stock') : '');

            return '<span class="vas281-foot-note">' + escapeHtml(footNote) + '</span>' +
                '<span><button type="button" class="vas281-btn" id="vas281-mback">' + escapeHtml(label('VAS_281_Back', 'Back')) + '</button> ' +
                '<button type="button" class="vas281-btn" id="vas281-mclose">' + escapeHtml(label('VAS_281_Close', 'Close')) + '</button></span>';
        }

        function openLinesModal(orderId) {
            showLoading(label('VAS_281_SalesOrderLines', 'Sales order lines') + '…');
            fetchOrderDetail(orderId, function (detail) {
                if (!detail) { showLoadError(); return; }
                showScreen(buildLinesCfg(detail.order, detail.lines), true);
            });
        }

        function buildLinesCfg(order, lines) {
            return {
                title: label('VAS_281_SalesOrderLines', 'Sales order lines') + ' · ' + order.SalesOrderNumber,
                subtitle: [order.CustomerName, formatDateShort(order.SalesOrderDate), order.DeliveryStatus].filter(function (p) { return p; }).join(' · '),
                size: 'md',
                render: function () { renderLinesBody(order, lines); }
            };
        }

        function renderLinesBody(order, lines) {
            var qtyOrdered = 0, qtyPending = 0;
            lines.forEach(function (l) { qtyOrdered += Number(l.QtyOrdered) || 0; qtyPending += Number(l.QtyPending) || 0; });

            var breadcrumb = '<div class="vas281-polink">' + escapeHtml(label('VAS_281_SalesOrderPrefix', 'Sales order')) +
                ' <button type="button" class="vas281-lnk" data-so="' + order.SalesOrderId + '">' + escapeHtml(order.SalesOrderNumber) + '</button>' +
                ' · ' + escapeHtml(formatDateFull(order.SalesOrderDate)) + ' · ' + escapeHtml(order.DocumentStatus) + '</div>';

            var stats = '<div class="vas281-mstats">' +
                statTile(label('VAS_281_ColLine', 'Line') + 's', String(lines.length)) +
                statTile(label('VAS_281_SoValue', 'SO value'), formatINR(order.OrderValue)) +
                statTile(label('VAS_281_ColOrdered', 'Ordered'), formatNum(qtyOrdered)) +
                statTile(label('VAS_281_ColPending', 'Pending'), formatNum(qtyPending)) +
            '</div>';

            var secHtml = '<div class="vas281-msec">' + escapeHtml(label('VAS_281_SalesOrderLines', 'Sales order lines')) + '</div>';
            $mBody.html(breadcrumb + stats + secHtml + '<div class="vas281-mtwrap"><div class="vas281-mtbl" id="vas281-linetbl-lines"></div></div>');

            lineState = { order: order, lines: lines, page: 0, size: MAX_ROWS_PER_PAGE, tableId: 'vas281-linetbl-lines' };
            drawLineTable('vas281-linetbl-lines');

            $mFoot.html(lineFootHtml(order, lines));
            $mFoot.find('#vas281-mback').on('click', backModal);
            $mFoot.find('#vas281-mclose').on('click', closeModal);
        }

        var LINE_COLS = [
            { key: 'no', label: label('VAS_281_ColLine', 'Line'), w: .35, align: 'right' },
            { key: 'product', label: label('VAS_281_ColProduct', 'Product'), w: 1.5 },
            { key: 'attr', label: label('VAS_281_ColAttribute', 'Attribute'), w: 1.1 },
            { key: 'uom', label: label('VAS_281_ColUom', 'UoM'), w: .5 },
            { key: 'ordered', label: label('VAS_281_ColOrdered', 'Ordered'), w: .7, align: 'right' },
            { key: 'delivered', label: label('VAS_281_ColDelivered', 'Delivered'), w: .7, align: 'right' },
            { key: 'pending', label: label('VAS_281_ColPending', 'Pending'), w: .7, align: 'right' },
            { key: 'instock', label: label('VAS_281_ColInStock', 'In stock'), w: .7, align: 'right' },
            { key: 'rate', label: label('VAS_281_ColRate', 'Rate'), w: .7, align: 'right' },
            { key: 'amount', label: label('VAS_281_ColAmount', 'Amount'), w: .9, align: 'right' },
            { key: 'status', label: label('VAS_281_ColLineStatus', 'Line status'), w: 1 }
        ];

        function drawLineTable(tableId) {
            var el = document.getElementById(tableId);
            if (!el || !lineState) { return; }

            var tpl = LINE_COLS.map(function (c) { return 'minmax(0,' + c.w + 'fr)'; }).join(' ');
            var pages = Math.max(1, Math.ceil(lineState.lines.length / lineState.size));
            if (lineState.page > pages - 1) { lineState.page = pages - 1; }
            var start = lineState.page * lineState.size;
            var slice = lineState.lines.slice(start, start + lineState.size);

            var head = '<div class="vas281-mrow vas281-mhead-row" style="grid-template-columns:' + tpl + '">' +
                LINE_COLS.map(function (c) { return '<span class="vas281-cell' + (c.align === 'right' ? ' right' : '') + '" title="' + escapeHtml(c.label) + '">' + escapeHtml(c.label) + '</span>'; }).join('') +
            '</div>';

            var body = slice.map(function (line) {
                var st = lineStatus(line);
                var cells = [
                    cellHtml(String(line.LineNo), 'vas281-c-std', 'right'),
                    cellHtml(line.ProductName, 'vas281-c-prim'),
                    cellHtml(line.AttributeText, 'vas281-c-std'),
                    cellHtml(line.UomName, 'vas281-c-std'),
                    cellHtml(formatNum(line.QtyOrdered), 'vas281-c-std', 'right'),
                    cellHtml(formatNum(line.QtyDelivered), 'vas281-c-std', 'right'),
                    cellHtml(formatNum(line.QtyPending), 'vas281-c-prim', 'right'),
                    cellHtml(formatNum(line.FreeStock), (line.FreeStock < line.QtyPending ? 'vas281-c-short' : 'vas281-c-ok'), 'right'),
                    cellHtml('₹ ' + formatNum(line.Rate), 'vas281-c-std', 'right'),
                    cellHtml(formatINR(line.Amount), 'vas281-c-emph', 'right')
                ].join('') + '<span class="vas281-cell"><span class="vas281-chip ' + st.cls + '" title="' + escapeHtml(st.text) + '">' + escapeHtml(st.text) + '</span></span>';
                return '<div class="vas281-mrow" style="grid-template-columns:' + tpl + '">' + cells + '</div>';
            }).join('');

            var showingLabel = label('VAS_281_Showing', 'Showing') + ' ' + (lineState.lines.length ? (start + 1) : 0) + '–' + (start + slice.length) + ' ' + label('VAS_281_Of', 'of') + ' ' + lineState.lines.length;

            var foot = '<div class="vas281-mtfoot"><span class="vas281-helper">' + escapeHtml(showingLabel) + '</span>' +
                (pages > 1 ?
                    '<span class="vas281-pager">' +
                        '<button type="button" class="vas281-pbtn" data-table="lines" data-dir="-1"' + (lineState.page === 0 ? ' disabled' : '') + ' aria-label="' + escapeHtml(label('VAS_281_PrevPage', 'Previous page')) + '">' + icon('prev') + '</button>' +
                        '<span class="vas281-ptxt">' + (lineState.page + 1) + ' ' + escapeHtml(label('VAS_281_Of', 'of')) + ' ' + pages + '</span>' +
                        '<button type="button" class="vas281-pbtn" data-table="lines" data-dir="1"' + (lineState.page >= pages - 1 ? ' disabled' : '') + ' aria-label="' + escapeHtml(label('VAS_281_NextPage', 'Next page')) + '">' + icon('next') + '</button>' +
                    '</span>' : '<span></span>') +
            '</div>';

            el.innerHTML = head + '<div class="vas281-mbody-rows">' + body + '</div>' + foot;
        }

        /* Sizes each visible table's rows-per-page to the space actually left in
           the modal body, so the body never grows an inner scrollbar. */
        function fitTable(id, currentSize, onResize) {
            var el = document.getElementById(id);
            if (!el) { return; }
            var avail = el.clientHeight;
            if (avail < 40) { return; }
            var head = el.querySelector('.vas281-mhead-row');
            var foot = el.querySelector('.vas281-mtfoot');
            var row = el.querySelector('.vas281-mbody-rows .vas281-mrow');
            if (!head || !row) { return; }
            var rowHeight = row.getBoundingClientRect().height || 30;
            var used = head.getBoundingClientRect().height + (foot ? foot.getBoundingClientRect().height + 8 : 0);
            var n = Math.floor((avail - used) / rowHeight);
            n = Math.max(MIN_ROWS_PER_PAGE, Math.min(MAX_ROWS_PER_PAGE, n));
            if (n !== currentSize) { onResize(n); }
        }

        function fitAllTables() {
            if (!$mask.hasClass('is-open')) { return; }

            if (docState) {
                fitTable('vas281-docstbl', docState.docs.size, function (n) {
                    var docs = docState.docs;
                    var newPages = Math.max(1, Math.ceil(docs.total / n));
                    var page = Math.min(docs.page, newPages - 1);
                    fetchCategoryOrders(docState.categoryIds, page, n, function (ok, summary, total, rows) {
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
            loadCategoryMix();
        };

        this.getRoot = function () { return $root; };

        this.disposeComponent = function () {
            var ns = '.vas281-' + ($self.AD_UserHomeWidgetID || $self.windowNo || 'widget');
            $(document).off(ns);
            $(window).off(ns);
            if ($mask) { $mask.remove(); }
            $root.remove();
        };

        this.refreshWidget = function () { loadCategoryMix(); };
    };

    VAS.VAS_281_CategoryWiseSOWidget.prototype.widgetFirevalueChanged = function (value) {
        if (this.listener) { this.listener.widgetFirevalueChanged(value); }
    };

    VAS.VAS_281_CategoryWiseSOWidget.prototype.addChangeListener = function (listener) {
        this.listener = listener;
    };

    VAS.VAS_281_CategoryWiseSOWidget.prototype.init = function (windowNo, frame) {
        this.frame = frame;
        this.AD_UserHomeWidgetID = frame.widgetInfo.AD_UserHomeWidgetID;
        this.windowNo = windowNo;
        this.Initalize();
        this.frame.getContentGrid().append(this.getRoot());
        ensureDashInlineSizeVar(this.getRoot());
    };

    VAS.VAS_281_CategoryWiseSOWidget.prototype.widgetSizeChange = function () { };

    VAS.VAS_281_CategoryWiseSOWidget.prototype.dispose = function () {
        if (this.disposeComponent) { this.disposeComponent(); }
    };

})(VAS, jQuery);
