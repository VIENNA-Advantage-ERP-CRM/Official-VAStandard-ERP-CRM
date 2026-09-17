/**
 * VAS_284 Delivery Performance Widget (Sales Order dashboard, 3x2 headline + bucket list)
 * Purpose - For a selected Month/Year, what share of Sales Orders PROMISED in that
 *           period were actually delivered on or before the date promised, and how
 *           the misses distribute between a recoverable slip and a real service
 *           failure. Denominator is always orders PROMISED in the period, never
 *           orders delivered in it - excluding "awaiting dispatch" from the
 *           denominator would flatter the number, so it is always included.
 *
 *           CONFIRMED bucket boundaries (17_Delivery_Performance_Claude_Development_
 *           Prompt.txt overrides the paired mock's "1-3 day"/"over 3 day" language):
 *             1. Delivered on time    - completion date <= promised date
 *             2. Delayed 1-7 Days     - completion date is 1-7 calendar days after
 *             3. Delayed over 7 days  - completion date is more than 7 days after
 *             4. Awaiting dispatch    - not fully delivered yet (zero or partial)
 *           Completion date is the LATEST completed outbound delivery for the order
 *           (the completing shipment, not the first - an order is only "delivered"
 *           once every line is). All day-difference math happens server-side in C#,
 *           never in SQL, per the prompt's cross-database rule.
 *
 *           Bucket bar width is the bucket's actual share of the denominator (the
 *           same proportional convention as every other bar-list widget on this
 *           dashboard, e.g. VAS_281/VAS_282) - not the mock's indicative severity
 *           weighting.
 *
 *           Zero orders promised in the period -> headline renders "-" with the
 *           meta line "No deliveries promised this month" and the bucket list is
 *           hidden (a 0% rate against an empty denominator is not a measurement).
 *           No qualifying orders in the previous period -> the movement chip is
 *           omitted entirely rather than showing a misleading "+100 pts" jump.
 *
 * Design  - 17-delivery-performance.html / .md: glass 3x2 tile, header with title +
 *           a Month/Year period filter (stopPropagation so it never triggers a row's
 *           click handler), a `.sumtop` row (headline % + `.kpi-meta` baseline note,
 *           and a points-movement chip that flips tone when negative) above a
 *           centred `.mixlist` of four fixed proportional bar rows in severity order.
 *           Row click opens that bucket's stat-strip + definition + paginated order
 *           list, reusing the same shared record/lines child modals as every other
 *           widget on this dashboard.
 *
 * Backend - VAS_284_DeliveryPerformanceWidget/GetDeliveryPerformance (GET month,year -> headline + buckets)
 *           VAS_284_DeliveryPerformanceWidget/GetBucketOrders        (GET bucketKey,month,year,page,size -> stat strip + definition + paginated orders)
 *           VAS_284_DeliveryPerformanceWidget/GetSalesOrderDetail    (GET id -> order + lines)
 *
 * ── Labels / Message Keys ─────────────────────────────────────────────────
 *  #  | Current Text                                                          | Message Key
 * ----+------------------------------------------------------------------------+--------------------------------
 *  1  | Delivery Performance                                                  | VAS_284_Title
 *  2  | On-time against date promised                                        | VAS_284_KpiMeta
 *  3  | No deliveries promised this month                                    | VAS_284_EmptyMeta
 *  4  | Performance unavailable                                              | VAS_284_ErrorState
 *  5  | Delivered on time                                                    | VAS_284_Bucket_ontime
 *  6  | Delayed 1-7 Days                                                     | VAS_284_Bucket_late1_7
 *  7  | Delayed over 7 days                                                  | VAS_284_Bucket_late_over7
 *  8  | Awaiting dispatch                                                    | VAS_284_Bucket_await
 *  9  | SOs                                                                  | VAS_284_SosSuffix
 * 10  | Delivery performance                                                 | VAS_284_SubtitlePrefix
 * 11  | Sales orders                                                         | VAS_284_StatSalesOrders
 * 12  | Share                                                                | VAS_284_StatShare
 * 13  | On-time overall                                                      | VAS_284_StatOnTimeOverall
 * 14  | Period                                                               | VAS_284_StatPeriod
 * 15  | Sales orders                                                         | VAS_284_SectionHeading
 * 16  | No orders in this bucket                                             | VAS_284_ZeroState
 * 17  | SO No                                                                | VAS_284_ColSoNo
 * 18  | Customer                                                             | VAS_284_Customer
 * 19  | Warehouse                                                            | VAS_284_ColWarehouse
 * 20  | Promised                                                             | VAS_284_ColPromised
 * 21  | Completed                                                            | VAS_284_ColCompleted
 * 22  | Value                                                                | VAS_284_ColValue
 * 23  | Status                                                               | VAS_284_ColStatus
 * 24  | Back                                                                 | VAS_284_Back
 * 25  | Close                                                                | VAS_284_Close
 * 26  | SO date                                                              | VAS_284_SoDate
 * 27  | Date promised                                                        | VAS_284_DatePromised
 * 28  | SO value                                                             | VAS_284_SoValue
 * 29  | Ship from                                                            | VAS_284_ShipFrom
 * 30  | Delivery mode                                                        | VAS_284_ColDeliveryMode
 * 31  | Document status                                                      | VAS_284_DocumentStatus
 * 32  | Delivery status                                                      | VAS_284_DeliveryStatus
 * 33  | Sales order lines                                                    | VAS_284_SalesOrderLines
 * 34  | Line                                                                 | VAS_284_ColLine
 * 35  | Product                                                              | VAS_284_ColProduct
 * 36  | Attribute                                                            | VAS_284_ColAttribute
 * 37  | UoM                                                                  | VAS_284_ColUom
 * 38  | Ordered                                                              | VAS_284_ColOrdered
 * 39  | Delivered                                                            | VAS_284_ColDelivered
 * 40  | Pending                                                              | VAS_284_ColPending
 * 41  | In stock                                                             | VAS_284_ColInStock
 * 42  | Rate                                                                 | VAS_284_ColRate
 * 43  | Amount                                                               | VAS_284_ColAmount
 * 44  | Line status                                                          | VAS_284_ColLineStatus
 * 45  | Delivered                                                            | VAS_284_DeliveryFull
 * 46  | Partially delivered                                                  | VAS_284_DeliveryPartial
 * 47  | Not delivered                                                        | VAS_284_DeliveryNone
 * 48  | Partly delivered                                                     | VAS_284_LinePartial
 * 49  | In process                                                           | VAS_284_LineInProcess
 * 50  | Sales order                                                          | VAS_284_SalesOrderPrefix
 * 51  | lines                                                                | VAS_284_LinesSuffix
 * 52  | qty ordered                                                          | VAS_284_QtyOrderedSuffix
 * 53  | qty short of stock                                                   | VAS_284_QtyShortSuffix
 * 54  | Previous page                                                        | VAS_284_PrevPage
 * 55  | Next page                                                            | VAS_284_NextPage
 * 56  | of                                                                   | VAS_284_Of
 * 57  | Showing                                                              | VAS_284_Showing
 * 58  | Search is unavailable right now. Try again in a moment.              | VAS_284_LoadError
 */
; VAS = window.VAS || {};

; (function (VAS, $) {

    var MAX_ROWS_PER_PAGE = 10;
    var MIN_ROWS_PER_PAGE = 2;
    var MONTHS = ['Jan', 'Feb', 'Mar', 'Apr', 'May', 'Jun', 'Jul', 'Aug', 'Sep', 'Oct', 'Nov', 'Dec'];
    var MIN_YEAR = 2024;
    var YEAR_SPAN = 3;

    // Fixed severity order and colour per bucket KEY (never row position).
    var BUCKET_META = {
        ontime: { fallback: 'Delivered on time', color: '#A3E0D4' },
        late1_7: { fallback: 'Delayed 1-7 Days', color: '#FFDCA1' },
        late_over7: { fallback: 'Delayed over 7 days', color: '#FFC7C7' },
        await: { fallback: 'Awaiting dispatch', color: '#D7E3EE' }
    };
    var BUCKET_ORDER = ['ontime', 'late1_7', 'late_over7', 'await'];

    var now = new Date();
    var CUR_M = now.getMonth();
    var CUR_Y = now.getFullYear();

    /* Keep --dash-inline-size on :root equal to the dashboard container's current
       pixel width so the widget clamp resolves against the dashboard's visible
       width, not the viewport. A single document-level ResizeObserver serves every
       widget (matches VAS_269-283). */
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

    VAS.VAS_284_DeliveryPerformanceWidget = function () {

        this.frame;
        this.windowNo;

        var $self = this;
        var $root = $('<div class="vas284-root">');
        var $shell, $list, $monthSel, $yearSel, $big, $meta, $chip;
        var $mask, $modal, $mHead, $mTitle, $mSub, $mBack, $mBody, $mFoot;

        var SELF_ENDPOINT = 'VAS_284_DeliveryPerformanceWidget/';

        var perfState = 'loading'; // 'loading' | 'ready' | 'empty' | 'error'
        var perfData = null;       // { TotalOrders, OnTimePercent, ChangePoints, Buckets }

        var bucketState = null;    // { key, label, definition, summary, docs:{page,size,total,rows} }
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
            return '<span class="vas284-cell' + (align === 'right' ? ' right' : '') + ' ' + cls + '" title="' + escapeHtml(value) + '">' + escapeHtml(value) + '</span>';
        }

        function statTile(l, v) {
            return '<div class="vas284-mstat"><div class="l">' + escapeHtml(l) + '</div><div class="v" title="' + escapeHtml(v) + '">' + escapeHtml(v) + '</div></div>';
        }

        /* ============================================================
         * Widget shell: header (title + Month/Year filter) + headline + bar list
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
            $shell = $('<div class="vas284-shell"></div>');

            var $head = $('<div class="vas284-head"></div>');
            var $htxt = $('<div class="vas284-head-txt"></div>');
            $htxt.append('<p class="vas284-title">' + escapeHtml(label('VAS_284_Title', 'Delivery Performance')) + '</p>');

            var $filter = $('<div class="vas284-mfilter"></div>');
            $monthSel = $('<select class="vas284-msel" aria-label="Month"></select>');
            $yearSel = $('<select class="vas284-msel" aria-label="Year"></select>');
            fillMonthSelect($monthSel);
            fillYearSelect($yearSel);
            $filter.append($monthSel, $yearSel);

            $head.append($htxt, $filter);

            var $sumtop = $('<div class="vas284-sumtop"></div>');
            var $sumleft = $('<div></div>');
            $big = $('<div class="vas284-big">…</div>');
            $meta = $('<div class="vas284-kpi-meta"></div>');
            $sumleft.append($big, $meta);
            $chip = $('<span class="vas284-chip vas284-chip-ok" hidden></span>');
            $sumtop.append($sumleft, $chip);

            $list = $('<div class="vas284-mixlist"></div>');

            $shell.append($head, $sumtop, $list);
            $root.append($shell);

            $monthSel.on('click', function (event) { event.stopPropagation(); });
            $yearSel.on('click', function (event) { event.stopPropagation(); });
            $monthSel.on('change', function () { loadDeliveryPerformance(); });
            $yearSel.on('change', function () { loadDeliveryPerformance(); });

            $list.on('click', function (event) {
                var row = event.target.closest ? event.target.closest('[data-bucket]') : null;
                if (row) { openBucketModal(row.getAttribute('data-bucket')); }
            });
        }

        function skeletonRows() {
            var rows2 = '';
            for (var i = 0; i < BUCKET_ORDER.length; i++) {
                rows2 += '<div class="vas284-mixrow vas284-skel-row">' +
                    '<span class="vas284-skel-line"></span>' +
                    '<span class="vas284-track"><span class="vas284-fill"></span></span></div>';
            }
            return rows2;
        }

        function loadDeliveryPerformance() {
            perfState = 'loading';
            renderAll();

            $.ajax({
                url: VIS.Application.contextUrl + SELF_ENDPOINT + 'GetDeliveryPerformance',
                type: 'GET', dataType: 'json', cache: false,
                data: { month: selectedMonth(), year: selectedYear() },
                success: function (res) {
                    var parsed = parseResponse(res);
                    if (parsed.Error) { perfState = 'error'; perfData = null; }
                    else {
                        perfData = parsed;
                        perfState = Number(parsed.TotalOrders || 0) > 0 ? 'ready' : 'empty';
                    }
                    renderAll();
                },
                error: function () {
                    perfState = 'error'; perfData = null;
                    renderAll();
                }
            });
        }

        function renderAll() {
            renderHeadline();
            renderList();
        }

        function renderHeadline() {
            if (perfState === 'loading') {
                $big.text('…');
                $meta.text('');
                $chip.prop('hidden', true);
                return;
            }
            if (perfState === 'error') {
                $big.text('—');
                $meta.text(escapeHtml(label('VAS_284_ErrorState', 'Performance unavailable')));
                $chip.prop('hidden', true);
                return;
            }
            if (perfState === 'empty') {
                $big.text('—');
                $meta.text(escapeHtml(label('VAS_284_EmptyMeta', 'No deliveries promised this month')));
                $chip.prop('hidden', true);
                return;
            }

            var pct = perfData.OnTimePercent;
            $big.text((pct === null || pct === undefined) ? '—' : Number(pct).toFixed(1) + '%');
            $meta.text(label('VAS_284_KpiMeta', 'On-time against date promised'));

            var points = perfData.ChangePoints;
            if (points === null || points === undefined) {
                $chip.prop('hidden', true);
            } else {
                var n = Number(points);
                var sign = n > 0 ? '+' : (n < 0 ? '' : '±');
                $chip.text(sign + n.toFixed(1) + ' pts')
                    .removeClass('vas284-chip-ok vas284-chip-risk')
                    .addClass(n < 0 ? 'vas284-chip-risk' : 'vas284-chip-ok')
                    .prop('hidden', false);
            }
        }

        function renderList() {
            if (perfState === 'loading') {
                $list.html(skeletonRows());
                return;
            }
            if (perfState === 'error' || perfState === 'empty') {
                $list.html('');
                return;
            }

            var buckets = perfData.Buckets || [];
            $list.html(buckets.map(function (b) {
                var meta = BUCKET_META[b.Key] || { fallback: b.Label, color: '#D7E3EE' };
                var name = label('VAS_284_Bucket_' + b.Key, meta.fallback);
                var valueText = formatNum(b.OrderCount) + ' ' + label('VAS_284_SosSuffix', 'SOs') + ' · ' + Number(b.Percent).toFixed(1) + '%';
                return '<button type="button" class="vas284-mixrow" data-bucket="' + b.Key + '">' +
                    '<span class="vas284-line"><span class="vas284-n" title="' + escapeHtml(name) + '">' + escapeHtml(name) + '</span>' +
                    '<span class="vas284-v" title="' + escapeHtml(valueText) + '">' + escapeHtml(valueText) + '</span></span>' +
                    '<span class="vas284-track"><span class="vas284-fill" style="width:' + Number(b.Percent) + '%;background:' + meta.color + '"></span></span></button>';
            }).join(''));
        }

        /* ============================================================
         * Modal shell - lives on <body>, not inside $root, so the widget
         * cell's own overflow cannot clip it. Every screen is a
         * { title, subtitle, size, render } config; showScreen() pushes the
         * outgoing config onto cfgStack and paints the new one; backModal()
         * pops and re-renders the popped config from its own live state
         * (bucketState / lineState) - never a frozen HTML snapshot.
         * ============================================================ */
        function createModal() {
            $mask = $('<div class="vas284-mask" role="dialog" aria-modal="true"></div>');
            $modal = $('<div class="vas284-modal"></div>');
            $mHead = $('<div class="vas284-mhead"></div>');
            var $htxt = $('<div class="vas284-htxt"></div>');
            $mBack = $('<button type="button" class="vas284-xbtn" aria-label="' + escapeHtml(label('VAS_284_Back', 'Back')) + '" hidden>' + icon('back') + '</button>');
            var $titleWrap = $('<div></div>');
            $mTitle = $('<h2></h2>');
            $mSub = $('<div class="vas284-msub"></div>');
            $titleWrap.append($mTitle, $mSub);
            $htxt.append($mBack, $titleWrap);
            var $closeBtn = $('<button type="button" class="vas284-xbtn" aria-label="' + escapeHtml(label('VAS_284_Close', 'Close')) + '">' + icon('close') + '</button>');
            $mHead.append($htxt, $closeBtn);

            $mBody = $('<div class="vas284-mbody"></div>');
            $mFoot = $('<div class="vas284-mfoot"></div>');

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
            var ns = '.vas284-' + ($self.AD_UserHomeWidgetID || $self.windowNo || 'widget');

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
            bucketState = null;
            lineState = null;
        }

        function showLoading(title) {
            $mBack.prop('hidden', !cfgStack.length);
            $mTitle.text(title || '');
            $mSub.text('');
            $mBody.html('<div class="vas284-mstate">…</div>');
            $mFoot.html('');
            $mask.addClass('is-open');
        }

        function showLoadError() {
            $mBody.html('<div class="vas284-mstate">' + escapeHtml(label('VAS_284_LoadError', 'Search is unavailable right now. Try again in a moment.')) + '</div>');
        }

        /* ============================================================
         * Bucket drill-down (top-level modal opened from a row click)
         * ============================================================ */
        function fetchBucketOrders(bucketKey, page, size, cb) {
            $.ajax({
                url: VIS.Application.contextUrl + SELF_ENDPOINT + 'GetBucketOrders',
                type: 'GET', dataType: 'json', cache: false,
                data: { bucketKey: bucketKey, month: selectedMonth(), year: selectedYear(), page: page, size: size },
                success: function (res) {
                    var parsed = parseResponse(res);
                    if (parsed.Error || !parsed.Summary) { cb(false); return; }
                    cb(true, parsed.Summary, parsed.Definition || '', Number(parsed.Total || 0), (parsed.Rows || []).map(normalizeBucketRow));
                },
                error: function () { cb(false); }
            });
        }

        function normalizeBucketRow(row) {
            return {
                SalesOrderId: Number(row.SalesOrderId) || 0,
                SalesOrderNumber: row.SalesOrderNumber || '',
                CustomerName: row.CustomerName || '',
                WarehouseName: row.WarehouseName || '',
                PromisedDate: row.PromisedDate || '',
                CompletionDate: row.CompletionDate || '',
                OrderValue: Number(row.OrderValue) || 0,
                StatusLabel: row.StatusLabel || '',
                StatusChipClass: row.StatusChipClass || 'neutral'
            };
        }

        function openBucketModal(bucketKey) {
            var meta = BUCKET_META[bucketKey];
            if (!meta) { return; }
            var bucketRow = ((perfData && perfData.Buckets) || []).filter(function (b) { return b.Key === bucketKey; })[0];
            var bucketLabel = label('VAS_284_Bucket_' + bucketKey, (bucketRow ? bucketRow.Label : meta.fallback));

            showLoading(bucketLabel);

            fetchBucketOrders(bucketKey, 0, MAX_ROWS_PER_PAGE, function (ok, summary, definition, total, list) {
                if (!ok) { showLoadError(); return; }
                bucketState = { key: bucketKey, label: bucketLabel, summary: summary, definition: definition, docs: { page: 0, size: MAX_ROWS_PER_PAGE, total: total, rows: list } };
                showScreen(buildBucketCfg(bucketLabel), false);
            });
        }

        function buildBucketCfg(bucketLabel) {
            return {
                title: bucketLabel,
                subtitle: label('VAS_284_SubtitlePrefix', 'Delivery performance') + ' · ' + periodLabel(),
                size: '',
                render: renderBucketBody
            };
        }

        function renderBucketBody() {
            var summary = bucketState.summary;
            var overall = summary.OnTimeOverallPercent;

            var statsHtml = '<div class="vas284-mstats">' +
                statTile(label('VAS_284_StatSalesOrders', 'Sales orders'), formatNum(summary.OrderCount)) +
                statTile(label('VAS_284_StatShare', 'Share'), Number(summary.SharePercent).toFixed(1) + '%') +
                statTile(label('VAS_284_StatOnTimeOverall', 'On-time overall'), (overall === null || overall === undefined) ? '—' : Number(overall).toFixed(1) + '%') +
                statTile(label('VAS_284_StatPeriod', 'Period'), periodLabel()) +
            '</div>';

            var noteHtml = '<div class="vas284-mnote">' + escapeHtml(bucketState.definition) + '</div>';
            var secHtml = '<div class="vas284-msec">' + escapeHtml(label('VAS_284_SectionHeading', 'Sales orders')) + '</div>';

            if (bucketState.docs.total <= 0) {
                $mBody.html(statsHtml + noteHtml + secHtml + '<div class="vas284-mstate">' + escapeHtml(label('VAS_284_ZeroState', 'No orders in this bucket')) + '</div>');
            } else {
                $mBody.html(statsHtml + noteHtml + secHtml + '<div class="vas284-mtwrap"><div class="vas284-mtbl" id="vas284-docstbl"></div></div>');
                drawDocumentsTable();
            }

            $mFoot.html('<span class="vas284-foot-note"></span><span><button type="button" class="vas284-btn" id="vas284-mclose">' + escapeHtml(label('VAS_284_Close', 'Close')) + '</button></span>');
            $mFoot.find('#vas284-mclose').on('click', closeModal);
        }

        var DOC_COLS = [
            { key: 'icon', label: '', w: .32 },
            { key: 'so', label: label('VAS_284_ColSoNo', 'SO No'), w: 1.05 },
            { key: 'customer', label: label('VAS_284_Customer', 'Customer'), w: 1.55 },
            { key: 'wh', label: label('VAS_284_ColWarehouse', 'Warehouse'), w: 1.1 },
            { key: 'promised', label: label('VAS_284_ColPromised', 'Promised'), w: .85 },
            { key: 'completed', label: label('VAS_284_ColCompleted', 'Completed'), w: .85 },
            { key: 'value', label: label('VAS_284_ColValue', 'Value'), w: .9, align: 'right' },
            { key: 'status', label: label('VAS_284_ColStatus', 'Status'), w: 1 }
        ];

        function drawDocumentsTable() {
            var el = document.getElementById('vas284-docstbl');
            if (!el || !bucketState) { return; }
            var docs = bucketState.docs;
            var tpl = DOC_COLS.map(function (c) { return 'minmax(0,' + c.w + 'fr)'; }).join(' ');
            var pages = Math.max(1, Math.ceil(docs.total / docs.size));

            var head = '<div class="vas284-mrow vas284-mhead-row" style="grid-template-columns:' + tpl + '">' +
                DOC_COLS.map(function (c) { return '<span class="vas284-cell' + (c.align === 'right' ? ' right' : '') + '" title="' + escapeHtml(c.label) + '">' + escapeHtml(c.label) + '</span>'; }).join('') +
            '</div>';

            var body = docs.rows.map(function (row) {
                var cells = [
                    '<span class="vas284-cell center"><button type="button" class="vas284-iconbtn" data-lines="' + row.SalesOrderId + '" title="' + escapeHtml(row.SalesOrderNumber) + '">' + icon('lines') + '</button></span>',
                    '<span class="vas284-cell"><button type="button" class="vas284-lnk" data-so="' + row.SalesOrderId + '" title="' + escapeHtml(row.SalesOrderNumber) + '">' + escapeHtml(row.SalesOrderNumber) + '</button></span>',
                    cellHtml(row.CustomerName, 'vas284-c-prim'),
                    cellHtml(row.WarehouseName || '—', 'vas284-c-std'),
                    cellHtml(row.PromisedDate ? formatDateFull(row.PromisedDate) : '—', 'vas284-c-std'),
                    cellHtml(row.CompletionDate ? formatDateFull(row.CompletionDate) : '—', 'vas284-c-std'),
                    cellHtml(formatINR(row.OrderValue), 'vas284-c-emph', 'right')
                ];
                var chip = '<span class="vas284-cell" title="' + escapeHtml(row.StatusLabel) + '"><span class="vas284-chip vas284-chip-' + row.StatusChipClass + '">' + escapeHtml(row.StatusLabel) + '</span></span>';
                return '<div class="vas284-mrow" style="grid-template-columns:' + tpl + '">' + cells.join('') + chip + '</div>';
            }).join('');

            var start = docs.page * docs.size;
            var showingLabel = label('VAS_284_Showing', 'Showing') + ' ' + (docs.total ? (start + 1) : 0) + '–' + (start + docs.rows.length) + ' ' + label('VAS_284_Of', 'of') + ' ' + docs.total + ' · ' + bucketState.label.toLowerCase();

            var foot = '<div class="vas284-mtfoot"><span class="vas284-helper">' + escapeHtml(showingLabel) + '</span>' +
                (pages > 1 ?
                    '<span class="vas284-pager">' +
                        '<button type="button" class="vas284-pbtn" data-table="docs" data-dir="-1"' + (docs.page === 0 ? ' disabled' : '') + ' aria-label="' + escapeHtml(label('VAS_284_PrevPage', 'Previous page')) + '">' + icon('prev') + '</button>' +
                        '<span class="vas284-ptxt">' + (docs.page + 1) + ' ' + escapeHtml(label('VAS_284_Of', 'of')) + ' ' + pages + '</span>' +
                        '<button type="button" class="vas284-pbtn" data-table="docs" data-dir="1"' + (docs.page >= pages - 1 ? ' disabled' : '') + ' aria-label="' + escapeHtml(label('VAS_284_NextPage', 'Next page')) + '">' + icon('next') + '</button>' +
                    '</span>' : '<span></span>') +
            '</div>';

            el.innerHTML = head + '<div class="vas284-mbody-rows">' + body + '</div>' + foot;
        }

        function turnPage(table, dir) {
            if (table === 'docs' && bucketState) {
                var docs = bucketState.docs;
                var pages = Math.max(1, Math.ceil(docs.total / docs.size));
                var next = Math.min(pages - 1, Math.max(0, docs.page + dir));
                if (next === docs.page) { return; }
                fetchBucketOrders(bucketState.key, next, docs.size, function (ok, summary, definition, total, list) {
                    if (!ok) { return; }
                    docs.page = next; docs.total = total; docs.rows = list;
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
            if (line.QtyDelivered >= line.QtyOrdered && line.QtyOrdered > 0) { return { text: label('VAS_284_DeliveryFull', 'Delivered'), cls: 'vas284-chip-info' }; }
            if (line.QtyDelivered > 0) { return { text: label('VAS_284_LinePartial', 'Partly delivered'), cls: 'vas284-chip-warn' }; }
            return { text: label('VAS_284_LineInProcess', 'In process'), cls: 'vas284-chip-neutral' };
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
            showLoading(bucketState ? bucketState.label : label('VAS_284_Title', 'Delivery Performance'));
            fetchOrderDetail(orderId, function (detail) {
                if (!detail) { showLoadError(); return; }
                showScreen(buildRecordCfg(detail.order, detail.lines), true);
            });
        }

        function buildRecordCfg(order, lines) {
            return {
                title: order.SalesOrderNumber,
                subtitle: [order.CustomerName, formatDateFull(order.SalesOrderDate), order.DocumentStatus].filter(function (p) { return p; }).join(' · '),
                size: '',
                render: function () { renderRecordBody(order, lines); }
            };
        }

        function soHeaderStats(order) {
            return '<div class="vas284-mstats">' +
                statTile(label('VAS_284_Customer', 'Customer'), order.CustomerName) +
                statTile(label('VAS_284_SoDate', 'SO date'), formatDateFull(order.SalesOrderDate)) +
                statTile(label('VAS_284_DatePromised', 'Date promised'), formatDateFull(order.DatePromised)) +
                statTile(label('VAS_284_SoValue', 'SO value'), formatINR(order.OrderValue)) +
                statTile(label('VAS_284_ShipFrom', 'Ship from'), order.WarehouseName) +
                statTile(label('VAS_284_ColDeliveryMode', 'Delivery mode'), order.DeliveryMode) +
                statTile(label('VAS_284_DocumentStatus', 'Document status'), order.DocumentStatus) +
                statTile(label('VAS_284_DeliveryStatus', 'Delivery status'), order.DeliveryStatus) +
            '</div>';
        }

        function renderRecordBody(order, lines) {
            var secHtml = '<div class="vas284-msec">' + escapeHtml(label('VAS_284_SalesOrderLines', 'Sales order lines')) + '</div>';
            $mBody.html(soHeaderStats(order) + secHtml + '<div class="vas284-mtwrap"><div class="vas284-mtbl" id="vas284-linetbl-record"></div></div>');

            lineState = { order: order, lines: lines, page: 0, size: MAX_ROWS_PER_PAGE, tableId: 'vas284-linetbl-record' };
            drawLineTable('vas284-linetbl-record');

            $mFoot.html(lineFootHtml(order, lines));
            $mFoot.find('#vas284-mback').on('click', backModal);
            $mFoot.find('#vas284-mclose').on('click', closeModal);
        }

        function lineFootHtml(order, lines) {
            var qtyOrdered = 0, qtyShort = 0;
            lines.forEach(function (line) {
                qtyOrdered += Number(line.QtyOrdered) || 0;
                var pending = Number(line.QtyPending) || 0;
                var freeStock = Number(line.FreeStock) || 0;
                if (pending > freeStock) { qtyShort += (pending - freeStock); }
            });
            var footNote = lines.length + ' ' + label('VAS_284_LinesSuffix', 'lines') +
                ' · ' + formatNum(qtyOrdered) + ' ' + label('VAS_284_QtyOrderedSuffix', 'qty ordered') +
                ' · ' + order.DeliveryStatus +
                (qtyShort > 0 ? ' · ' + formatNum(qtyShort) + ' ' + label('VAS_284_QtyShortSuffix', 'qty short of stock') : '');

            return '<span class="vas284-foot-note">' + escapeHtml(footNote) + '</span>' +
                '<span><button type="button" class="vas284-btn" id="vas284-mback">' + escapeHtml(label('VAS_284_Back', 'Back')) + '</button> ' +
                '<button type="button" class="vas284-btn" id="vas284-mclose">' + escapeHtml(label('VAS_284_Close', 'Close')) + '</button></span>';
        }

        function openLinesModal(orderId) {
            showLoading(label('VAS_284_SalesOrderLines', 'Sales order lines') + '…');
            fetchOrderDetail(orderId, function (detail) {
                if (!detail) { showLoadError(); return; }
                showScreen(buildLinesCfg(detail.order, detail.lines), true);
            });
        }

        function buildLinesCfg(order, lines) {
            return {
                title: label('VAS_284_SalesOrderLines', 'Sales order lines') + ' · ' + order.SalesOrderNumber,
                subtitle: [order.CustomerName, formatDateFull(order.SalesOrderDate), order.DeliveryStatus].filter(function (p) { return p; }).join(' · '),
                size: 'md',
                render: function () { renderLinesBody(order, lines); }
            };
        }

        function renderLinesBody(order, lines) {
            var qtyOrdered = 0, qtyPending = 0;
            lines.forEach(function (l) { qtyOrdered += Number(l.QtyOrdered) || 0; qtyPending += Number(l.QtyPending) || 0; });

            var breadcrumb = '<div class="vas284-polink">' + escapeHtml(label('VAS_284_SalesOrderPrefix', 'Sales order')) +
                ' <button type="button" class="vas284-lnk" data-so="' + order.SalesOrderId + '">' + escapeHtml(order.SalesOrderNumber) + '</button>' +
                ' · ' + escapeHtml(formatDateFull(order.SalesOrderDate)) + ' · ' + escapeHtml(order.DocumentStatus) + '</div>';

            var stats = '<div class="vas284-mstats">' +
                statTile(label('VAS_284_ColLine', 'Line') + 's', String(lines.length)) +
                statTile(label('VAS_284_SoValue', 'SO value'), formatINR(order.OrderValue)) +
                statTile(label('VAS_284_ColOrdered', 'Ordered'), formatNum(qtyOrdered)) +
                statTile(label('VAS_284_ColPending', 'Pending'), formatNum(qtyPending)) +
            '</div>';

            var secHtml = '<div class="vas284-msec">' + escapeHtml(label('VAS_284_SalesOrderLines', 'Sales order lines')) + '</div>';
            $mBody.html(breadcrumb + stats + secHtml + '<div class="vas284-mtwrap"><div class="vas284-mtbl" id="vas284-linetbl-lines"></div></div>');

            lineState = { order: order, lines: lines, page: 0, size: MAX_ROWS_PER_PAGE, tableId: 'vas284-linetbl-lines' };
            drawLineTable('vas284-linetbl-lines');

            $mFoot.html(lineFootHtml(order, lines));
            $mFoot.find('#vas284-mback').on('click', backModal);
            $mFoot.find('#vas284-mclose').on('click', closeModal);
        }

        var LINE_COLS = [
            { key: 'no', label: label('VAS_284_ColLine', 'Line'), w: .35, align: 'right' },
            { key: 'product', label: label('VAS_284_ColProduct', 'Product'), w: 1.5 },
            { key: 'attr', label: label('VAS_284_ColAttribute', 'Attribute'), w: 1.1 },
            { key: 'uom', label: label('VAS_284_ColUom', 'UoM'), w: .5 },
            { key: 'ordered', label: label('VAS_284_ColOrdered', 'Ordered'), w: .7, align: 'right' },
            { key: 'delivered', label: label('VAS_284_ColDelivered', 'Delivered'), w: .7, align: 'right' },
            { key: 'pending', label: label('VAS_284_ColPending', 'Pending'), w: .7, align: 'right' },
            { key: 'instock', label: label('VAS_284_ColInStock', 'In stock'), w: .7, align: 'right' },
            { key: 'rate', label: label('VAS_284_ColRate', 'Rate'), w: .7, align: 'right' },
            { key: 'amount', label: label('VAS_284_ColAmount', 'Amount'), w: .9, align: 'right' },
            { key: 'status', label: label('VAS_284_ColLineStatus', 'Line status'), w: 1 }
        ];

        function drawLineTable(tableId) {
            var el = document.getElementById(tableId);
            if (!el || !lineState) { return; }

            var tpl = LINE_COLS.map(function (c) { return 'minmax(0,' + c.w + 'fr)'; }).join(' ');
            var pages = Math.max(1, Math.ceil(lineState.lines.length / lineState.size));
            if (lineState.page > pages - 1) { lineState.page = pages - 1; }
            var start = lineState.page * lineState.size;
            var slice = lineState.lines.slice(start, start + lineState.size);

            var head = '<div class="vas284-mrow vas284-mhead-row" style="grid-template-columns:' + tpl + '">' +
                LINE_COLS.map(function (c) { return '<span class="vas284-cell' + (c.align === 'right' ? ' right' : '') + '" title="' + escapeHtml(c.label) + '">' + escapeHtml(c.label) + '</span>'; }).join('') +
            '</div>';

            var body = slice.map(function (line) {
                var st = lineStatus(line);
                var cells = [
                    cellHtml(String(line.LineNo), 'vas284-c-std', 'right'),
                    cellHtml(line.ProductName, 'vas284-c-prim'),
                    cellHtml(line.AttributeText, 'vas284-c-std'),
                    cellHtml(line.UomName, 'vas284-c-std'),
                    cellHtml(formatNum(line.QtyOrdered), 'vas284-c-std', 'right'),
                    cellHtml(formatNum(line.QtyDelivered), 'vas284-c-std', 'right'),
                    cellHtml(formatNum(line.QtyPending), 'vas284-c-prim', 'right'),
                    cellHtml(formatNum(line.FreeStock), (line.FreeStock < line.QtyPending ? 'vas284-c-short' : 'vas284-c-ok'), 'right'),
                    cellHtml('₹ ' + formatNum(line.Rate), 'vas284-c-std', 'right'),
                    cellHtml(formatINR(line.Amount), 'vas284-c-emph', 'right')
                ].join('') + '<span class="vas284-cell"><span class="vas284-chip ' + st.cls + '" title="' + escapeHtml(st.text) + '">' + escapeHtml(st.text) + '</span></span>';
                return '<div class="vas284-mrow" style="grid-template-columns:' + tpl + '">' + cells + '</div>';
            }).join('');

            var showingLabel = label('VAS_284_Showing', 'Showing') + ' ' + (lineState.lines.length ? (start + 1) : 0) + '–' + (start + slice.length) + ' ' + label('VAS_284_Of', 'of') + ' ' + lineState.lines.length;

            var foot = '<div class="vas284-mtfoot"><span class="vas284-helper">' + escapeHtml(showingLabel) + '</span>' +
                (pages > 1 ?
                    '<span class="vas284-pager">' +
                        '<button type="button" class="vas284-pbtn" data-table="lines" data-dir="-1"' + (lineState.page === 0 ? ' disabled' : '') + ' aria-label="' + escapeHtml(label('VAS_284_PrevPage', 'Previous page')) + '">' + icon('prev') + '</button>' +
                        '<span class="vas284-ptxt">' + (lineState.page + 1) + ' ' + escapeHtml(label('VAS_284_Of', 'of')) + ' ' + pages + '</span>' +
                        '<button type="button" class="vas284-pbtn" data-table="lines" data-dir="1"' + (lineState.page >= pages - 1 ? ' disabled' : '') + ' aria-label="' + escapeHtml(label('VAS_284_NextPage', 'Next page')) + '">' + icon('next') + '</button>' +
                    '</span>' : '<span></span>') +
            '</div>';

            el.innerHTML = head + '<div class="vas284-mbody-rows">' + body + '</div>' + foot;
        }

        /* Sizes each visible table's rows-per-page to the space actually left in
           the modal body, so the body never grows an inner scrollbar. */
        function fitTable(id, currentSize, onResize) {
            var el = document.getElementById(id);
            if (!el) { return; }
            var avail = el.clientHeight;
            if (avail < 40) { return; }
            var head = el.querySelector('.vas284-mhead-row');
            var foot = el.querySelector('.vas284-mtfoot');
            var row = el.querySelector('.vas284-mbody-rows .vas284-mrow');
            if (!head || !row) { return; }
            var rowHeight = row.getBoundingClientRect().height || 30;
            var used = head.getBoundingClientRect().height + (foot ? foot.getBoundingClientRect().height + 8 : 0);
            var n = Math.floor((avail - used) / rowHeight);
            n = Math.max(MIN_ROWS_PER_PAGE, Math.min(MAX_ROWS_PER_PAGE, n));
            if (n !== currentSize) { onResize(n); }
        }

        function fitAllTables() {
            if (!$mask.hasClass('is-open')) { return; }

            if (bucketState) {
                fitTable('vas284-docstbl', bucketState.docs.size, function (n) {
                    var docs = bucketState.docs;
                    var newPages = Math.max(1, Math.ceil(docs.total / n));
                    var page = Math.min(docs.page, newPages - 1);
                    fetchBucketOrders(bucketState.key, page, n, function (ok, summary, definition, total, list) {
                        if (!ok) { return; }
                        docs.page = page; docs.size = n; docs.total = total; docs.rows = list;
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
            loadDeliveryPerformance();
        };

        this.getRoot = function () { return $root; };

        this.disposeComponent = function () {
            var ns = '.vas284-' + ($self.AD_UserHomeWidgetID || $self.windowNo || 'widget');
            $(document).off(ns);
            $(window).off(ns);
            if ($mask) { $mask.remove(); }
            $root.remove();
        };

        this.refreshWidget = function () { loadDeliveryPerformance(); };
    };

    VAS.VAS_284_DeliveryPerformanceWidget.prototype.widgetFirevalueChanged = function (value) {
        if (this.listener) { this.listener.widgetFirevalueChanged(value); }
    };

    VAS.VAS_284_DeliveryPerformanceWidget.prototype.addChangeListener = function (listener) {
        this.listener = listener;
    };

    VAS.VAS_284_DeliveryPerformanceWidget.prototype.init = function (windowNo, frame) {
        this.frame = frame;
        this.AD_UserHomeWidgetID = frame.widgetInfo.AD_UserHomeWidgetID;
        this.windowNo = windowNo;
        this.Initalize();
        this.frame.getContentGrid().append(this.getRoot());
        ensureDashInlineSizeVar(this.getRoot());
    };

    VAS.VAS_284_DeliveryPerformanceWidget.prototype.widgetSizeChange = function () { };

    VAS.VAS_284_DeliveryPerformanceWidget.prototype.dispose = function () {
        if (this.disposeComponent) { this.disposeComponent(); }
    };

})(VAS, jQuery);
