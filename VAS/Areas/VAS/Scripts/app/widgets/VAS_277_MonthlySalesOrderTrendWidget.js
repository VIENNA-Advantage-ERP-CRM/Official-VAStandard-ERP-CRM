/**
 * VAS_277 Monthly Sales Order Trend Widget (Sales Order dashboard, 4x2 column chart)
 * Purpose - Booked Sales Order value by month, over a user-controlled but hard-capped
 *           12-month range (from/to month selects, clamped client-side and re-checked
 *           server-side). Booked = same cohort as VAS_273 Order Value Booked MTD -
 *           active real Sales Order, IsSOTrx='Y', non-return, non-quotation, DocStatus
 *           'CO' or 'CL' - grouped by DateOrdered's calendar month. Missing months in
 *           the selected range are drawn as zero bars, never omitted.
 *
 *           Unlike every other widget on this dashboard, the widget root itself is
 *           NOT a single click target - only an individual chart bar opens the
 *           drill-down modal for that month (Click / Enter / Space on the bar).
 *
 * Design  - 10-monthly-so-trend.html / .md: glass 4x2 tile, header with title +
 *           subtitle + a dual month-select range filter, a column chart with a
 *           bottom axis of month labels. Month-click drill-down modal (4-card stat
 *           strip + paginated table) and the record/lines child modals match the
 *           same design language and { title, subtitle, size, render } config-stack
 *           modal engine as every other widget in this codebase - reused here as
 *           this widget's own self-contained copy.
 *
 * Filter  - fromIndex/toIndex are "year*12+zero-based-month" indices (monthIndex),
 *           never a formatted string, so range math and the color-palette lookup
 *           both compare/subscript on a plain integer. clampRange() keeps
 *           from <= to and the span at most MAX_SPAN months, snaps both ends back
 *           inside [MIN_IDX, MAX_IDX], and shows a transient toast whenever a
 *           requested range had to be narrowed - exactly the from/to clamp-and-toast
 *           algorithm in the HTML prototype.
 *
 * Backend - VAS_277_MonthlySalesOrderTrendWidget/GetSeries            (GET fromIndex,toIndex -> monthly series)
 *           VAS_277_MonthlySalesOrderTrendWidget/GetMonthOrders       (GET monthIndex,page,size -> month summary + paginated documents)
 *           VAS_277_MonthlySalesOrderTrendWidget/GetSalesOrderDetail  (GET id -> order + lines)
 *
 * ── Labels / Message Keys ─────────────────────────────────────────────────
 *  #  | Current Text                                                          | Message Key
 * ----+------------------------------------------------------------------------+--------------------------------
 *  1  | Monthly Sales Order Trend                                             | VAS_277_Title
 *  2  | Sales orders booked, by month                                        | VAS_277_Subtitle
 *  3  | to                                                                    | VAS_277_RangeTo
 *  4  | Range limited to 12 months                                            | VAS_277_RangeLimit
 *  5  | No data for the selected range                                        | VAS_277_NoData
 *  6  | Chart unavailable                                                     | VAS_277_ErrorState
 *  7  | Sales orders                                                          | VAS_277_StatOrders
 *  8  | Order value                                                           | VAS_277_StatOrderValue
 *  9  | Customers                                                             | VAS_277_StatCustomers
 * 10  | Avg order value                                                       | VAS_277_StatAvgOrderValue
 * 11  | Sales orders booked in                                                | VAS_277_SectionHeadingPrefix
 * 12  | No orders booked this month                                           | VAS_277_ZeroState
 * 13  | newest first                                                          | VAS_277_NewestFirst
 * 14  | SO No                                                                 | VAS_277_ColSoNo
 * 15  | SO date                                                               | VAS_277_SoDate
 * 16  | Customer                                                              | VAS_277_Customer
 * 17  | Representative                                                        | VAS_277_Representative
 * 18  | Lines                                                                 | VAS_277_ColLines
 * 19  | Qty                                                                   | VAS_277_ColQty
 * 20  | Order value                                                           | VAS_277_ColOrderValue
 * 21  | Status                                                                | VAS_277_ColStatus
 * 22  | Back                                                                  | VAS_277_Back
 * 23  | Close                                                                 | VAS_277_Close
 * 24  | Date promised                                                         | VAS_277_DatePromised
 * 25  | SO value                                                              | VAS_277_SoValue
 * 26  | Ship from                                                             | VAS_277_ShipFrom
 * 27  | Delivery mode                                                         | VAS_277_DeliveryMode
 * 28  | Document status                                                       | VAS_277_DocumentStatus
 * 29  | Delivery status                                                       | VAS_277_DeliveryStatus
 * 30  | Sales order lines                                                     | VAS_277_SalesOrderLines
 * 31  | Line                                                                  | VAS_277_ColLine
 * 32  | Product                                                               | VAS_277_ColProduct
 * 33  | Attribute                                                             | VAS_277_ColAttribute
 * 34  | UoM                                                                   | VAS_277_ColUom
 * 35  | Ordered                                                               | VAS_277_ColOrdered
 * 36  | Delivered                                                             | VAS_277_ColDelivered
 * 37  | Pending                                                               | VAS_277_ColPending
 * 38  | In stock                                                              | VAS_277_ColInStock
 * 39  | Rate                                                                  | VAS_277_ColRate
 * 40  | Amount                                                                | VAS_277_ColAmount
 * 41  | Line status                                                           | VAS_277_ColLineStatus
 * 42  | Delivered                                                             | VAS_277_DeliveryFull
 * 43  | Partially delivered                                                   | VAS_277_DeliveryPartial
 * 44  | Not delivered                                                         | VAS_277_DeliveryNone
 * 45  | Partly delivered                                                      | VAS_277_LinePartial
 * 46  | In process                                                            | VAS_277_LineInProcess
 * 47  | Sales order                                                           | VAS_277_SalesOrderPrefix
 * 48  | lines                                                                 | VAS_277_LinesSuffix
 * 49  | qty ordered                                                           | VAS_277_QtyOrderedSuffix
 * 50  | qty short of stock                                                    | VAS_277_QtyShortSuffix
 * 51  | Previous page                                                         | VAS_277_PrevPage
 * 52  | Next page                                                             | VAS_277_NextPage
 * 53  | of                                                                    | VAS_277_Of
 * 54  | Showing                                                               | VAS_277_Showing
 * 55  | Search is unavailable right now. Try again in a moment.               | VAS_277_LoadError
 */
; VAS = window.VAS || {};

; (function (VAS, $) {

    var MAX_ROWS_PER_PAGE = 10;
    var MIN_ROWS_PER_PAGE = 2;
    var MAX_SPAN = 12; // months - server-enforced cap, re-checked here client-side
    var MONTHS = ['Jan', 'Feb', 'Mar', 'Apr', 'May', 'Jun', 'Jul', 'Aug', 'Sep', 'Oct', 'Nov', 'Dec'];
    var MIN_IDX = 2024 * 12;

    var now = new Date();
    var CUR_IDX = now.getFullYear() * 12 + now.getMonth();
    var MAX_IDX = CUR_IDX + 4;

    // 12-entry [fill, border] palette, indexed by monthIndex % 12 so a given
    // calendar month always draws the same color across every render.
    var MONTH_TINTS = [
        ['#CFE8FF', '#1F83FF'], ['#CDEFE0', '#0B6B45'], ['#FCE3C6', '#B4690E'],
        ['#E5DBFA', '#5F4AA6'], ['#FBD6DC', '#A33F3F'], ['#CFF3EE', '#0E8C7C'],
        ['#FDE7B0', '#9A6500'], ['#D6E4FF', '#2C4E9A'], ['#E6D9CE', '#7A4B29'],
        ['#D9F0C7', '#4C7A1F'], ['#F5D4EC', '#9C3E7E'], ['#D6EEF7', '#1C6E8C']
    ];

    /* design.md §Widget Header / §Measurement Setup: keep --dash-inline-size on
       :root equal to the dashboard container's current pixel width so the widget
       clamp resolves against the dashboard's visible width, not the viewport. A
       single document-level ResizeObserver serves every widget (matches VAS_269-276). */
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

    VAS.VAS_277_MonthlySalesOrderTrendWidget = function () {

        this.frame;
        this.windowNo;

        var $self = this;
        var $root = $('<div class="vas277-root">');
        var $shell, $chart, $plot, $axis, $fromSel, $toSel, $toast;
        var $mask, $modal, $mHead, $mTitle, $mSub, $mBack, $mBody, $mFoot;

        var SELF_ENDPOINT = 'VAS_277_MonthlySalesOrderTrendWidget/';

        var fromIndex = Math.max(MIN_IDX, CUR_IDX - 11);
        var toIndex = CUR_IDX;

        var seriesState = 'loading'; // 'loading' | 'ready' | 'error'
        var months = [];             // [{ MonthIndex, Value, OrderCount }]

        var monthState = null;       // { monthIndex, summary, docs:{page,size,total,rows} }
        var lineState = null;        // { order, lines, page, size, tableId }
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

        // monthIndex (year*12 + zero-based month) -> "Aug 2026"
        function idxLabel(i) {
            var year = Math.floor(i / 12);
            var month = ((i % 12) + 12) % 12;
            return MONTHS[month] + ' ' + year;
        }

        function idxLabelShort(i) {
            var month = ((i % 12) + 12) % 12;
            return MONTHS[month];
        }

        function parseResponse(res) {
            var data = res;
            if (typeof data === 'string') { data = JSON.parse(data); }
            if (typeof data === 'string') { data = JSON.parse(data); }
            return data || {};
        }

        function cellHtml(value, cls, align) {
            return '<span class="vas277-cell' + (align === 'right' ? ' right' : '') + ' ' + cls + '" title="' + escapeHtml(value) + '">' + escapeHtml(value) + '</span>';
        }

        function statTile(l, v) {
            return '<div class="vas277-mstat"><div class="l">' + escapeHtml(l) + '</div><div class="v" title="' + escapeHtml(v) + '">' + escapeHtml(v) + '</div></div>';
        }

        /* ============================================================
         * Widget shell: header (title/subtitle + range filter) + chart
         * ============================================================ */
        function comboOpts(selectedIndex) {
            var html = '';
            for (var i = MIN_IDX; i <= MAX_IDX; i++) {
                html += '<option value="' + i + '"' + (i === selectedIndex ? ' selected' : '') + '>' + escapeHtml(idxLabel(i)) + '</option>';
            }
            return html;
        }

        function createWidget() {
            $shell = $('<div class="vas277-shell"></div>');

            var $head = $('<div class="vas277-head"></div>');
            var $htxt = $('<div></div>');
            $htxt.append('<p class="vas277-title">' + escapeHtml(label('VAS_277_Title', 'Monthly Sales Order Trend')) + '</p>');
            $htxt.append('<p class="vas277-subtitle">' + escapeHtml(label('VAS_277_Subtitle', 'Sales orders booked, by month')) + '</p>');

            var $filter = $('<div class="vas277-filter"></div>');
            $fromSel = $('<select aria-label="From month"></select>');
            $toSel = $('<select aria-label="To month"></select>');
            $filter.append($fromSel, '<span class="vas277-filter-sep">' + escapeHtml(label('VAS_277_RangeTo', 'to')) + '</span>', $toSel);

            $head.append($htxt, $filter);

            $chart = $('<div class="vas277-chart"></div>');
            $plot = $('<div class="vas277-plot"></div>');
            $axis = $('<div class="vas277-axis"></div>');
            $chart.append($plot, $axis);

            $toast = $('<div class="vas277-toast"></div>');

            $shell.append($head, $chart, $toast);
            $root.append($shell);

            populateSelects();

            $fromSel.on('change', function () {
                clampRange('from', Number($fromSel.val()));
                loadSeries();
            });
            $toSel.on('change', function () {
                clampRange('to', Number($toSel.val()));
                loadSeries();
            });

            $plot.on('click', function (event) {
                var wrap = event.target.closest ? event.target.closest('[data-month]') : null;
                if (wrap) { openMonthModal(Number(wrap.getAttribute('data-month'))); }
            });
            $plot.on('keydown', function (event) {
                if (event.key !== 'Enter' && event.key !== ' ') { return; }
                var wrap = event.target.closest ? event.target.closest('[data-month]') : null;
                if (wrap) { event.preventDefault(); openMonthModal(Number(wrap.getAttribute('data-month'))); }
            });
        }

        function populateSelects() {
            $fromSel.html(comboOpts(fromIndex));
            $toSel.html(comboOpts(toIndex));
        }

        function showToast(message) {
            $toast.text(message).addClass('is-visible');
            window.clearTimeout($toast.data('timer'));
            $toast.data('timer', window.setTimeout(function () { $toast.removeClass('is-visible'); }, 2400));
        }

        // Keeps from <= to and the span at most MAX_SPAN months, snapping both
        // ends back inside [MIN_IDX, MAX_IDX] - the exact from/to clamp-and-toast
        // algorithm in the HTML prototype. "changed" says which select the user
        // just moved, so the OTHER end is the one adjusted to make room.
        function clampRange(changed, value) {
            if (changed === 'from') { fromIndex = value; } else { toIndex = value; }

            var clamped = false;

            if (fromIndex > toIndex) {
                if (changed === 'from') { toIndex = fromIndex; } else { fromIndex = toIndex; }
                clamped = true;
            }
            if (toIndex - fromIndex + 1 > MAX_SPAN) {
                if (changed === 'from') { toIndex = fromIndex + MAX_SPAN - 1; }
                else { fromIndex = toIndex - MAX_SPAN + 1; }
                clamped = true;
            }
            if (fromIndex < MIN_IDX) { fromIndex = MIN_IDX; clamped = true; }
            if (toIndex > MAX_IDX) { toIndex = MAX_IDX; clamped = true; }
            if (toIndex < fromIndex) { toIndex = fromIndex; }

            populateSelects();
            if (clamped) { showToast(label('VAS_277_RangeLimit', 'Range limited to 12 months')); }
        }

        function loadSeries() {
            seriesState = 'loading';
            renderChart();

            $.ajax({
                url: VIS.Application.contextUrl + SELF_ENDPOINT + 'GetSeries',
                type: 'GET', dataType: 'json', cache: false,
                data: { fromIndex: fromIndex, toIndex: toIndex },
                success: function (res) {
                    var parsed = parseResponse(res);
                    if (parsed.Error || !parsed.Months) { seriesState = 'error'; months = []; }
                    else { seriesState = 'ready'; months = parsed.Months; }
                    renderChart();
                },
                error: function () {
                    seriesState = 'error'; months = [];
                    renderChart();
                }
            });
        }

        function renderChart() {
            if (seriesState === 'loading') {
                $plot.html('');
                $axis.html('');
                $chart.find('.vas277-empty').remove();
                return;
            }
            if (seriesState === 'error') {
                $plot.html('');
                $axis.html('');
                if (!$chart.find('.vas277-empty').length) {
                    $chart.append('<div class="vas277-empty">' + escapeHtml(label('VAS_277_ErrorState', 'Chart unavailable')) + '</div>');
                }
                return;
            }

            $chart.find('.vas277-empty').remove();

            var max = 0;
            months.forEach(function (m) { if (m.Value > max) { max = m.Value; } });

            if (max <= 0) {
                $plot.html('');
                $axis.html('');
                $chart.append('<div class="vas277-empty">' + escapeHtml(label('VAS_277_NoData', 'No data for the selected range')) + '</div>');
                return;
            }

            var scale = max * 1.18;

            var barsHtml = months.map(function (m) {
                var tint = MONTH_TINTS[((m.MonthIndex % 12) + 12) % 12];
                var heightPct = scale > 0 ? Math.max(1, (m.Value / scale) * 100) : 1;
                return '<div class="vas277-colwrap" tabindex="0" role="button" data-month="' + m.MonthIndex + '" aria-label="' + escapeHtml(idxLabel(m.MonthIndex)) + ' - ' + escapeHtml(formatINR(m.Value)) + '">' +
                    '<span class="vas277-colval">' + escapeHtml(formatINR(m.Value)) + '</span>' +
                    '<span class="vas277-col" style="height:' + heightPct.toFixed(2) + '%;background:' + tint[0] + ';border-color:' + tint[1] + ';"></span>' +
                '</div>';
            }).join('');

            var axisHtml = months.map(function (m) {
                return '<span class="vas277-axis-lbl">' + escapeHtml(idxLabelShort(m.MonthIndex)) + '</span>';
            }).join('');

            $plot.html(barsHtml);
            $axis.html(axisHtml);
        }

        /* ============================================================
         * Modal shell - lives on <body>, not inside $root, so the widget
         * cell's own overflow cannot clip it. Every screen is a
         * { title, subtitle, size, render } config; showScreen() pushes the
         * outgoing config onto cfgStack and paints the new one; backModal()
         * pops and re-renders the popped config from its own live state
         * (monthState / lineState) - never a frozen HTML snapshot.
         * ============================================================ */
        function createModal() {
            $mask = $('<div class="vas277-mask" role="dialog" aria-modal="true"></div>');
            $modal = $('<div class="vas277-modal"></div>');
            $mHead = $('<div class="vas277-mhead"></div>');
            var $htxt = $('<div class="vas277-htxt"></div>');
            $mBack = $('<button type="button" class="vas277-xbtn" aria-label="' + escapeHtml(label('VAS_277_Back', 'Back')) + '" hidden>' + icon('back') + '</button>');
            var $titleWrap = $('<div></div>');
            $mTitle = $('<h2></h2>');
            $mSub = $('<div class="vas277-msub"></div>');
            $titleWrap.append($mTitle, $mSub);
            $htxt.append($mBack, $titleWrap);
            var $closeBtn = $('<button type="button" class="vas277-xbtn" aria-label="' + escapeHtml(label('VAS_277_Close', 'Close')) + '">' + icon('close') + '</button>');
            $mHead.append($htxt, $closeBtn);

            $mBody = $('<div class="vas277-mbody"></div>');
            $mFoot = $('<div class="vas277-mfoot"></div>');

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
            var ns = '.vas277-' + ($self.AD_UserHomeWidgetID || $self.windowNo || 'widget');

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
            monthState = null;
            lineState = null;
        }

        function showLoading(title) {
            $mBack.prop('hidden', !cfgStack.length);
            $mTitle.text(title || '');
            $mSub.text('');
            $mBody.html('<div class="vas277-mstate">…</div>');
            $mFoot.html('');
            $mask.addClass('is-open');
        }

        function showLoadError() {
            $mBody.html('<div class="vas277-mstate">' + escapeHtml(label('VAS_277_LoadError', 'Search is unavailable right now. Try again in a moment.')) + '</div>');
        }

        /* ============================================================
         * Month drill-down (top-level modal opened from a bar click)
         * ============================================================ */
        function openMonthModal(monthIndex) {
            showLoading(idxLabel(monthIndex));

            fetchMonthOrders(monthIndex, 0, MAX_ROWS_PER_PAGE, function (ok, summary, total, rows) {
                if (!ok) { showLoadError(); return; }
                monthState = { monthIndex: monthIndex, summary: summary, docs: { page: 0, size: MAX_ROWS_PER_PAGE, total: total, rows: rows } };
                showScreen(buildMonthCfg(monthIndex), false);
            });
        }

        function buildMonthCfg(monthIndex) {
            return {
                title: idxLabel(monthIndex),
                subtitle: label('VAS_277_SectionHeadingPrefix', 'Sales orders booked in') + ' ' + idxLabel(monthIndex),
                size: '',
                render: renderMonthBody
            };
        }

        function fetchMonthOrders(monthIndex, page, size, cb) {
            $.ajax({
                url: VIS.Application.contextUrl + SELF_ENDPOINT + 'GetMonthOrders',
                type: 'GET', dataType: 'json', cache: false,
                data: { monthIndex: monthIndex, page: page, size: size },
                success: function (res) {
                    var parsed = parseResponse(res);
                    if (parsed.Error) { cb(false); return; }
                    cb(true, parsed.Summary || {}, Number(parsed.Total || 0), (parsed.Rows || []).map(normalizeOrderRow));
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
                DocumentStatus: row.DocumentStatus || ''
            };
        }

        function renderMonthBody() {
            var summary = monthState.summary;
            var statsHtml = '<div class="vas277-mstats">' +
                statTile(label('VAS_277_StatOrderValue', 'Order value'), formatINR(summary.OrderValue)) +
                statTile(label('VAS_277_StatOrders', 'Sales orders'), formatNum(summary.OrderCount)) +
                statTile(label('VAS_277_StatAvgOrderValue', 'Avg order value'), formatINR(summary.AvgOrderValue)) +
                statTile(label('VAS_277_StatCustomers', 'Customers'), formatNum(summary.CustomerCount)) +
            '</div>';

            if (monthState.docs.total <= 0) {
                $mBody.html(statsHtml + '<div class="vas277-mstate">' + escapeHtml(label('VAS_277_ZeroState', 'No orders booked this month')) + '</div>');
            } else {
                $mBody.html(statsHtml + '<div class="vas277-mtwrap"><div class="vas277-mtbl" id="vas277-docstbl"></div></div>');
                drawDocumentsTable();
            }
            $mFoot.html('<span class="vas277-foot-note"></span><span><button type="button" class="vas277-btn" id="vas277-mclose">' + escapeHtml(label('VAS_277_Close', 'Close')) + '</button></span>');
            $mFoot.find('#vas277-mclose').on('click', closeModal);
        }

        var DOC_COLS = [
            { key: 'icon', label: '', w: .32 },
            { key: 'so', label: label('VAS_277_ColSoNo', 'SO No'), w: 1.15 },
            { key: 'date', label: label('VAS_277_SoDate', 'SO date'), w: 1 },
            { key: 'customer', label: label('VAS_277_Customer', 'Customer'), w: 1.7 },
            { key: 'rep', label: label('VAS_277_Representative', 'Representative'), w: 1.25 },
            { key: 'lines', label: label('VAS_277_ColLines', 'Lines'), w: .55, align: 'right' },
            { key: 'qty', label: label('VAS_277_ColQty', 'Qty'), w: .75, align: 'right' },
            { key: 'value', label: label('VAS_277_ColOrderValue', 'Order value'), w: 1, align: 'right' },
            { key: 'status', label: label('VAS_277_ColStatus', 'Status'), w: 1.1 }
        ];

        function drawDocumentsTable() {
            var el = document.getElementById('vas277-docstbl');
            if (!el || !monthState) { return; }
            var docs = monthState.docs;
            var tpl = DOC_COLS.map(function (c) { return 'minmax(0,' + c.w + 'fr)'; }).join(' ');
            var pages = Math.max(1, Math.ceil(docs.total / docs.size));

            var head = '<div class="vas277-mrow vas277-mhead-row" style="grid-template-columns:' + tpl + '">' +
                DOC_COLS.map(function (c) { return '<span class="vas277-cell' + (c.align === 'right' ? ' right' : '') + '" title="' + escapeHtml(c.label) + '">' + escapeHtml(c.label) + '</span>'; }).join('') +
            '</div>';

            var body = docs.rows.map(function (row) {
                var cells = [
                    '<span class="vas277-cell center"><button type="button" class="vas277-iconbtn" data-lines="' + row.SalesOrderId + '" title="' + escapeHtml(row.SalesOrderNumber) + '">' + icon('lines') + '</button></span>',
                    '<span class="vas277-cell"><button type="button" class="vas277-lnk" data-so="' + row.SalesOrderId + '" title="' + escapeHtml(row.SalesOrderNumber) + '">' + escapeHtml(row.SalesOrderNumber) + '</button></span>',
                    cellHtml(formatDateFull(row.SalesOrderDate), 'vas277-c-std'),
                    cellHtml(row.CustomerName, 'vas277-c-prim'),
                    cellHtml(row.RepresentativeName || '—', 'vas277-c-std'),
                    cellHtml(formatNum(row.LineCount), 'vas277-c-std', 'right'),
                    cellHtml(formatNum(row.OrderQty), 'vas277-c-std', 'right'),
                    cellHtml(formatINR(row.OrderValue), 'vas277-c-emph', 'right'),
                    '<span class="vas277-cell" title="' + escapeHtml(row.DocumentStatus) + '"><span class="vas277-chip vas277-chip-ok">' + escapeHtml(row.DocumentStatus) + '</span></span>'
                ];
                return '<div class="vas277-mrow" style="grid-template-columns:' + tpl + '">' + cells.join('') + '</div>';
            }).join('');

            var start = docs.page * docs.size;
            var showingLabel = label('VAS_277_Showing', 'Showing') + ' ' + (docs.total ? (start + 1) : 0) + '–' + (start + docs.rows.length) + ' ' + label('VAS_277_Of', 'of') + ' ' + docs.total + ' · ' + label('VAS_277_NewestFirst', 'newest first');

            var foot = '<div class="vas277-mtfoot"><span class="vas277-helper">' + escapeHtml(showingLabel) + '</span>' +
                (pages > 1 ?
                    '<span class="vas277-pager">' +
                        '<button type="button" class="vas277-pbtn" data-table="docs" data-dir="-1"' + (docs.page === 0 ? ' disabled' : '') + ' aria-label="' + escapeHtml(label('VAS_277_PrevPage', 'Previous page')) + '">' + icon('prev') + '</button>' +
                        '<span class="vas277-ptxt">' + (docs.page + 1) + ' ' + escapeHtml(label('VAS_277_Of', 'of')) + ' ' + pages + '</span>' +
                        '<button type="button" class="vas277-pbtn" data-table="docs" data-dir="1"' + (docs.page >= pages - 1 ? ' disabled' : '') + ' aria-label="' + escapeHtml(label('VAS_277_NextPage', 'Next page')) + '">' + icon('next') + '</button>' +
                    '</span>' : '<span></span>') +
            '</div>';

            el.innerHTML = head + '<div class="vas277-mbody-rows">' + body + '</div>' + foot;
        }

        function turnPage(table, dir) {
            if (table === 'docs' && monthState) {
                var docs = monthState.docs;
                var pages = Math.max(1, Math.ceil(docs.total / docs.size));
                var next = Math.min(pages - 1, Math.max(0, docs.page + dir));
                if (next === docs.page) { return; }
                fetchMonthOrders(monthState.monthIndex, next, docs.size, function (ok, summary, total, rows) {
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
            if (line.QtyDelivered >= line.QtyOrdered && line.QtyOrdered > 0) { return { text: label('VAS_277_DeliveryFull', 'Delivered'), cls: 'vas277-chip-info' }; }
            if (line.QtyDelivered > 0) { return { text: label('VAS_277_LinePartial', 'Partly delivered'), cls: 'vas277-chip-warn' }; }
            return { text: label('VAS_277_LineInProcess', 'In process'), cls: 'vas277-chip-prop' };
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
            showLoading(label('VAS_277_Title', 'Monthly Sales Order Trend') + '…');
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
            return '<div class="vas277-mstats">' +
                statTile(label('VAS_277_Customer', 'Customer'), order.CustomerName) +
                statTile(label('VAS_277_SoDate', 'SO date'), formatDateFull(order.SalesOrderDate)) +
                statTile(label('VAS_277_DatePromised', 'Date promised'), formatDateFull(order.DatePromised)) +
                statTile(label('VAS_277_SoValue', 'SO value'), formatINR(order.OrderValue)) +
                statTile(label('VAS_277_ShipFrom', 'Ship from'), order.WarehouseName) +
                statTile(label('VAS_277_DeliveryMode', 'Delivery mode'), order.DeliveryMode) +
                statTile(label('VAS_277_DocumentStatus', 'Document status'), order.DocumentStatus) +
                statTile(label('VAS_277_DeliveryStatus', 'Delivery status'), order.DeliveryStatus) +
            '</div>';
        }

        function renderRecordBody(order, lines) {
            var secHtml = '<div class="vas277-msec">' + escapeHtml(label('VAS_277_SalesOrderLines', 'Sales order lines')) + '</div>';
            $mBody.html(soHeaderStats(order) + secHtml + '<div class="vas277-mtwrap"><div class="vas277-mtbl" id="vas277-linetbl-record"></div></div>');

            lineState = { order: order, lines: lines, page: 0, size: MAX_ROWS_PER_PAGE, tableId: 'vas277-linetbl-record' };
            drawLineTable('vas277-linetbl-record');

            $mFoot.html(lineFootHtml(order, lines));
            $mFoot.find('#vas277-mback').on('click', backModal);
            $mFoot.find('#vas277-mclose').on('click', closeModal);
        }

        function lineFootHtml(order, lines) {
            var qtyOrdered = 0, qtyShort = 0;
            lines.forEach(function (line) {
                qtyOrdered += Number(line.QtyOrdered) || 0;
                var pending = Number(line.QtyPending) || 0;
                var freeStock = Number(line.FreeStock) || 0;
                if (pending > freeStock) { qtyShort += (pending - freeStock); }
            });
            var footNote = lines.length + ' ' + label('VAS_277_LinesSuffix', 'lines') +
                ' · ' + formatNum(qtyOrdered) + ' ' + label('VAS_277_QtyOrderedSuffix', 'qty ordered') +
                ' · ' + order.DeliveryStatus +
                (qtyShort > 0 ? ' · ' + formatNum(qtyShort) + ' ' + label('VAS_277_QtyShortSuffix', 'qty short of stock') : '');

            return '<span class="vas277-foot-note">' + escapeHtml(footNote) + '</span>' +
                '<span><button type="button" class="vas277-btn" id="vas277-mback">' + escapeHtml(label('VAS_277_Back', 'Back')) + '</button> ' +
                '<button type="button" class="vas277-btn" id="vas277-mclose">' + escapeHtml(label('VAS_277_Close', 'Close')) + '</button></span>';
        }

        function openLinesModal(orderId) {
            showLoading(label('VAS_277_SalesOrderLines', 'Sales order lines') + '…');
            fetchOrderDetail(orderId, function (detail) {
                if (!detail) { showLoadError(); return; }
                showScreen(buildLinesCfg(detail.order, detail.lines), true);
            });
        }

        function buildLinesCfg(order, lines) {
            return {
                title: label('VAS_277_SalesOrderLines', 'Sales order lines') + ' · ' + order.SalesOrderNumber,
                subtitle: [order.CustomerName, formatDateShort(order.SalesOrderDate), order.DeliveryStatus].filter(function (p) { return p; }).join(' · '),
                size: 'md',
                render: function () { renderLinesBody(order, lines); }
            };
        }

        function renderLinesBody(order, lines) {
            var qtyOrdered = 0, qtyPending = 0;
            lines.forEach(function (l) { qtyOrdered += Number(l.QtyOrdered) || 0; qtyPending += Number(l.QtyPending) || 0; });

            var breadcrumb = '<div class="vas277-polink">' + escapeHtml(label('VAS_277_SalesOrderPrefix', 'Sales order')) +
                ' <button type="button" class="vas277-lnk" data-so="' + order.SalesOrderId + '">' + escapeHtml(order.SalesOrderNumber) + '</button>' +
                ' · ' + escapeHtml(formatDateFull(order.SalesOrderDate)) + ' · ' + escapeHtml(order.DocumentStatus) + '</div>';

            var stats = '<div class="vas277-mstats">' +
                statTile(label('VAS_277_ColLine', 'Line') + 's', String(lines.length)) +
                statTile(label('VAS_277_SoValue', 'SO value'), formatINR(order.OrderValue)) +
                statTile(label('VAS_277_ColOrdered', 'Ordered'), formatNum(qtyOrdered)) +
                statTile(label('VAS_277_ColPending', 'Pending'), formatNum(qtyPending)) +
            '</div>';

            var secHtml = '<div class="vas277-msec">' + escapeHtml(label('VAS_277_SalesOrderLines', 'Sales order lines')) + '</div>';
            $mBody.html(breadcrumb + stats + secHtml + '<div class="vas277-mtwrap"><div class="vas277-mtbl" id="vas277-linetbl-lines"></div></div>');

            lineState = { order: order, lines: lines, page: 0, size: MAX_ROWS_PER_PAGE, tableId: 'vas277-linetbl-lines' };
            drawLineTable('vas277-linetbl-lines');

            $mFoot.html(lineFootHtml(order, lines));
            $mFoot.find('#vas277-mback').on('click', backModal);
            $mFoot.find('#vas277-mclose').on('click', closeModal);
        }

        var LINE_COLS = [
            { key: 'no', label: label('VAS_277_ColLine', 'Line'), w: .35, align: 'right' },
            { key: 'product', label: label('VAS_277_ColProduct', 'Product'), w: 1.5 },
            { key: 'attr', label: label('VAS_277_ColAttribute', 'Attribute'), w: 1.1 },
            { key: 'uom', label: label('VAS_277_ColUom', 'UoM'), w: .5 },
            { key: 'ordered', label: label('VAS_277_ColOrdered', 'Ordered'), w: .7, align: 'right' },
            { key: 'delivered', label: label('VAS_277_ColDelivered', 'Delivered'), w: .7, align: 'right' },
            { key: 'pending', label: label('VAS_277_ColPending', 'Pending'), w: .7, align: 'right' },
            { key: 'instock', label: label('VAS_277_ColInStock', 'In stock'), w: .7, align: 'right' },
            { key: 'rate', label: label('VAS_277_ColRate', 'Rate'), w: .7, align: 'right' },
            { key: 'amount', label: label('VAS_277_ColAmount', 'Amount'), w: .9, align: 'right' },
            { key: 'status', label: label('VAS_277_ColLineStatus', 'Line status'), w: 1 }
        ];

        function drawLineTable(tableId) {
            var el = document.getElementById(tableId);
            if (!el || !lineState) { return; }

            var tpl = LINE_COLS.map(function (c) { return 'minmax(0,' + c.w + 'fr)'; }).join(' ');
            var pages = Math.max(1, Math.ceil(lineState.lines.length / lineState.size));
            if (lineState.page > pages - 1) { lineState.page = pages - 1; }
            var start = lineState.page * lineState.size;
            var slice = lineState.lines.slice(start, start + lineState.size);

            var head = '<div class="vas277-mrow vas277-mhead-row" style="grid-template-columns:' + tpl + '">' +
                LINE_COLS.map(function (c) { return '<span class="vas277-cell' + (c.align === 'right' ? ' right' : '') + '" title="' + escapeHtml(c.label) + '">' + escapeHtml(c.label) + '</span>'; }).join('') +
            '</div>';

            var body = slice.map(function (line) {
                var st = lineStatus(line);
                var cells = [
                    cellHtml(String(line.LineNo), 'vas277-c-std', 'right'),
                    cellHtml(line.ProductName, 'vas277-c-prim'),
                    cellHtml(line.AttributeText, 'vas277-c-std'),
                    cellHtml(line.UomName, 'vas277-c-std'),
                    cellHtml(formatNum(line.QtyOrdered), 'vas277-c-std', 'right'),
                    cellHtml(formatNum(line.QtyDelivered), 'vas277-c-std', 'right'),
                    cellHtml(formatNum(line.QtyPending), 'vas277-c-prim', 'right'),
                    cellHtml(formatNum(line.FreeStock), (line.FreeStock < line.QtyPending ? 'vas277-c-short' : 'vas277-c-ok'), 'right'),
                    cellHtml('₹ ' + formatNum(line.Rate), 'vas277-c-std', 'right'),
                    cellHtml(formatINR(line.Amount), 'vas277-c-emph', 'right')
                ].join('') + '<span class="vas277-cell"><span class="vas277-chip ' + st.cls + '" title="' + escapeHtml(st.text) + '">' + escapeHtml(st.text) + '</span></span>';
                return '<div class="vas277-mrow" style="grid-template-columns:' + tpl + '">' + cells + '</div>';
            }).join('');

            var showingLabel = label('VAS_277_Showing', 'Showing') + ' ' + (lineState.lines.length ? (start + 1) : 0) + '–' + (start + slice.length) + ' ' + label('VAS_277_Of', 'of') + ' ' + lineState.lines.length;

            var foot = '<div class="vas277-mtfoot"><span class="vas277-helper">' + escapeHtml(showingLabel) + '</span>' +
                (pages > 1 ?
                    '<span class="vas277-pager">' +
                        '<button type="button" class="vas277-pbtn" data-table="lines" data-dir="-1"' + (lineState.page === 0 ? ' disabled' : '') + ' aria-label="' + escapeHtml(label('VAS_277_PrevPage', 'Previous page')) + '">' + icon('prev') + '</button>' +
                        '<span class="vas277-ptxt">' + (lineState.page + 1) + ' ' + escapeHtml(label('VAS_277_Of', 'of')) + ' ' + pages + '</span>' +
                        '<button type="button" class="vas277-pbtn" data-table="lines" data-dir="1"' + (lineState.page >= pages - 1 ? ' disabled' : '') + ' aria-label="' + escapeHtml(label('VAS_277_NextPage', 'Next page')) + '">' + icon('next') + '</button>' +
                    '</span>' : '<span></span>') +
            '</div>';

            el.innerHTML = head + '<div class="vas277-mbody-rows">' + body + '</div>' + foot;
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
            var head = el.querySelector('.vas277-mhead-row');
            var foot = el.querySelector('.vas277-mtfoot');
            var row = el.querySelector('.vas277-mbody-rows .vas277-mrow');
            if (!head || !row) { return; }
            var rowHeight = row.getBoundingClientRect().height || 30;
            var used = head.getBoundingClientRect().height + (foot ? foot.getBoundingClientRect().height + 8 : 0);
            var n = Math.floor((avail - used) / rowHeight);
            n = Math.max(MIN_ROWS_PER_PAGE, Math.min(MAX_ROWS_PER_PAGE, n));
            if (n !== currentSize) { onResize(n); }
        }

        function fitAllTables() {
            if (!$mask.hasClass('is-open')) { return; }

            if (monthState) {
                fitTable('vas277-docstbl', monthState.docs.size, function (n) {
                    var docs = monthState.docs;
                    var newPages = Math.max(1, Math.ceil(docs.total / n));
                    var page = Math.min(docs.page, newPages - 1);
                    fetchMonthOrders(monthState.monthIndex, page, n, function (ok, summary, total, rows) {
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
            loadSeries();
        };

        this.getRoot = function () { return $root; };

        this.disposeComponent = function () {
            var ns = '.vas277-' + ($self.AD_UserHomeWidgetID || $self.windowNo || 'widget');
            $(document).off(ns);
            $(window).off(ns);
            if ($mask) { $mask.remove(); }
            $root.remove();
        };

        this.refreshWidget = function () { loadSeries(); };
    };

    VAS.VAS_277_MonthlySalesOrderTrendWidget.prototype.widgetFirevalueChanged = function (value) {
        if (this.listener) { this.listener.widgetFirevalueChanged(value); }
    };

    VAS.VAS_277_MonthlySalesOrderTrendWidget.prototype.addChangeListener = function (listener) {
        this.listener = listener;
    };

    VAS.VAS_277_MonthlySalesOrderTrendWidget.prototype.init = function (windowNo, frame) {
        this.frame = frame;
        this.AD_UserHomeWidgetID = frame.widgetInfo.AD_UserHomeWidgetID;
        this.windowNo = windowNo;
        this.Initalize();
        this.frame.getContentGrid().append(this.getRoot());
        ensureDashInlineSizeVar(this.getRoot());
    };

    VAS.VAS_277_MonthlySalesOrderTrendWidget.prototype.widgetSizeChange = function () { };

    VAS.VAS_277_MonthlySalesOrderTrendWidget.prototype.dispose = function () {
        if (this.disposeComponent) { this.disposeComponent(); }
    };

})(VAS, jQuery);
