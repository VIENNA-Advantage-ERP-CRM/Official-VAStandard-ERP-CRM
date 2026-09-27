/**
 * VAS_282 Delivery Mode Widget (Sales Order dashboard, 2x2 proportional bar list)
 * Purpose - How a selected Month/Year's ACTUAL outbound dispatch value (completed
 *           M_InOut documents, DocStatus 'CO', IsSOTrx='Y', non-return) splits
 *           across the three known shipping methods on M_InOut.DeliveryViaRule:
 *           D (Delivery), P (Pickup), S (Shipper). Share is by dispatch VALUE
 *           (server-computed from M_InOutLine qty x Sales Order line price incl. tax -
 *           the spec's M_InOut.VA077_TotalSalesAmt is an obsolete column that does not
 *           exist in this environment), never document count. This is an
 *           explicit override of the paired HTML mock's Road/Courier/Rail/Customer-
 *           pickup labels, which the available schema does not support - only
 *           D/P/S ever render, and "Avg transit" is replaced by a "Dispatches"
 *           count (the schema gives no reliable dispatch-to-delivery date pair).
 *
 *           The server returns only the modes that actually have dispatches for the
 *           period; THIS WIDGET zero-fills the other known modes and computes
 *           displayed percentages (with the rounding residue assigned to the
 *           largest share so the bars always sum to 100 when total value > 0) -
 *           matching the prompt's own instruction that this overlay happens in JS.
 *
 * Design  - 15-delivery-mode-mix.html / .md: glass 2x2 tile, header with title +
 *           subtitle + a Month/Year period filter (stopPropagation so it never
 *           triggers a row's click handler), a centred `.mixlist` of three
 *           proportional bar rows (D, P, S, always in that fixed order) - name in
 *           regular weight (not bold, so it never competes with the bars), percent
 *           right-aligned, bar width equal to share so widths across all rows sum
 *           to 100% of the track. Colours are keyed to the mode code (not row
 *           position) so a month where one mode drops to zero never reshuffles the
 *           others. Row click opens that mode's dispatch drill-down (stat strip +
 *           its completed dispatches, minimal column set), reusing the same shared
 *           record/lines child modals as every other widget on this dashboard.
 *
 * Backend - VAS_282_DeliveryModeWidget/GetDeliveryModeMix     (GET month,year -> modes with dispatches + total value)
 *           VAS_282_DeliveryModeWidget/GetModeDispatches      (GET modeCode,month,year,page,size -> stat strip + paginated dispatches)
 *           VAS_282_DeliveryModeWidget/GetSalesOrderDetail    (GET id -> order + lines)
 *
 * ── Labels / Message Keys ─────────────────────────────────────────────────
 *  #  | Current Text                                                          | Message Key
 * ----+------------------------------------------------------------------------+--------------------------------
 *  1  | Delivery Mode                                                         | VAS_282_Title
 *  2  | Dispatches this month                                                 | VAS_282_Subtitle
 *  3  | No dispatches recorded this month                                     | VAS_282_ZeroState
 *  4  | Mix unavailable                                                       | VAS_282_ErrorState
 *  5  | Dispatches by delivery mode                                           | VAS_282_SubtitlePrefix
 *  6  | Share of dispatch value                                               | VAS_282_StatShare
 *  7  | Dispatch value                                                        | VAS_282_StatDispatchValue
 *  8  | Sales orders                                                          | VAS_282_StatSalesOrders
 *  9  | Dispatches                                                            | VAS_282_StatDispatches
 * 10  | Sales orders dispatched on this mode                                  | VAS_282_SectionHeading
 * 11  | SO No                                                                 | VAS_282_ColSoNo
 * 12  | Customer                                                              | VAS_282_Customer
 * 13  | Ship from                                                             | VAS_282_ShipFrom
 * 14  | Delivery mode                                                         | VAS_282_ColDeliveryMode
 * 15  | Promised                                                              | VAS_282_ColPromised
 * 16  | Value                                                                 | VAS_282_ColValue
 * 17  | Back                                                                  | VAS_282_Back
 * 18  | Close                                                                 | VAS_282_Close
 * 18a | SO date                                                               | VAS_282_SoDate
 * 19  | Date promised                                                         | VAS_282_DatePromised
 * 20  | SO value                                                              | VAS_282_SoValue
 * 21  | Document status                                                       | VAS_282_DocumentStatus
 * 22  | Delivery status                                                       | VAS_282_DeliveryStatus
 * 23  | Sales order lines                                                     | VAS_282_SalesOrderLines
 * 24  | Line                                                                  | VAS_282_ColLine
 * 25  | Product                                                               | VAS_282_ColProduct
 * 26  | Attribute                                                             | VAS_282_ColAttribute
 * 27  | UoM                                                                   | VAS_282_ColUom
 * 28  | Ordered                                                               | VAS_282_ColOrdered
 * 29  | Delivered                                                             | VAS_282_ColDelivered
 * 30  | Pending                                                               | VAS_282_ColPending
 * 31  | In stock                                                              | VAS_282_ColInStock
 * 32  | Rate                                                                  | VAS_282_ColRate
 * 33  | Amount                                                                | VAS_282_ColAmount
 * 34  | Line status                                                           | VAS_282_ColLineStatus
 * 35  | Delivered                                                             | VAS_282_DeliveryFull
 * 36  | Partially delivered                                                   | VAS_282_DeliveryPartial
 * 37  | Not delivered                                                         | VAS_282_DeliveryNone
 * 38  | Partly delivered                                                      | VAS_282_LinePartial
 * 39  | In process                                                            | VAS_282_LineInProcess
 * 40  | Sales order                                                           | VAS_282_SalesOrderPrefix
 * 41  | lines                                                                 | VAS_282_LinesSuffix
 * 42  | qty ordered                                                           | VAS_282_QtyOrderedSuffix
 * 43  | qty short of stock                                                    | VAS_282_QtyShortSuffix
 * 44  | Previous page                                                         | VAS_282_PrevPage
 * 45  | Next page                                                             | VAS_282_NextPage
 * 46  | of                                                                    | VAS_282_Of
 * 47  | Showing                                                               | VAS_282_Showing
 * 48  | Search is unavailable right now. Try again in a moment.               | VAS_282_LoadError
 */
; VAS = window.VAS || {};

; (function (VAS, $) {

    var MAX_ROWS_PER_PAGE = 10;
    var MIN_ROWS_PER_PAGE = 2;
    var MONTHS = ['Jan', 'Feb', 'Mar', 'Apr', 'May', 'Jun', 'Jul', 'Aug', 'Sep', 'Oct', 'Nov', 'Dec'];
    var MIN_YEAR = 2024;
    var YEAR_SPAN = 3;

    // Fixed order and colour per mode CODE (not row position) - a month where a mode
    // drops to zero never reshuffles the others. Fallback English names match the
    // confirmed D/P/S override mapping exactly.
    var MODE_SCAFFOLD = [
        { code: 'D', fallback: 'Delivery', color: '#A9D2FF' },
        { code: 'P', fallback: 'Pickup', color: '#A3E0D4' },
        { code: 'S', fallback: 'Shipper', color: '#FFDCA1' }
    ];

    var now = new Date();
    var CUR_M = now.getMonth();
    var CUR_Y = now.getFullYear();

    /* design.md §Widget Header / §Measurement Setup: keep --dash-inline-size on
       :root equal to the dashboard container's current pixel width so the widget
       clamp resolves against the dashboard's visible width, not the viewport. A
       single document-level ResizeObserver serves every widget (matches VAS_269-281). */
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

    VAS.VAS_282_DeliveryModeWidget = function () {

        this.frame;
        this.windowNo;

        var $self = this;
        var $root = $('<div class="vas282-root">');
        var $shell, $list, $monthSel, $yearSel;
        var $mask, $modal, $mHead, $mTitle, $mSub, $mBack, $mBody, $mFoot;

        var SELF_ENDPOINT = 'VAS_282_DeliveryModeWidget/';

        var mixState = 'loading'; // 'loading' | 'ready' | 'error'
        var rows = [];             // always 3 rows, D/P/S fixed order, zero-filled + overlaid

        var docState = null;       // { modeCode, modeName, summary, docs:{page,size,total,rows} }
        var lineState = null;      // { order, lines, page, size, tableId }
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
            return '<span class="vas282-cell' + (align === 'right' ? ' right' : '') + ' ' + cls + '" title="' + escapeHtml(value) + '">' + escapeHtml(value) + '</span>';
        }

        function statTile(l, v) {
            return '<div class="vas282-mstat"><div class="l">' + escapeHtml(l) + '</div><div class="v" title="' + escapeHtml(v) + '">' + escapeHtml(v) + '</div></div>';
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
            $shell = $('<div class="vas282-shell"></div>');

            var $head = $('<div class="vas282-head"></div>');
            var $htxt = $('<div class="vas282-head-txt"></div>');
            $htxt.append('<p class="vas282-title">' + escapeHtml(label('VAS_282_Title', 'Delivery Mode')) + '</p>');
            $htxt.append('<p class="vas282-sub">' + escapeHtml(label('VAS_282_Subtitle', 'Dispatches this month')) + '</p>');

            var $filter = $('<div class="vas282-mfilter"></div>');
            $monthSel = $('<select class="vas282-msel" aria-label="Month"></select>');
            $yearSel = $('<select class="vas282-msel" aria-label="Year"></select>');
            fillMonthSelect($monthSel);
            fillYearSelect($yearSel);
            $filter.append($monthSel, $yearSel);

            $head.append($htxt, $filter);

            $list = $('<div class="vas282-mixlist"></div>');

            $shell.append($head, $list);
            $root.append($shell);

            $monthSel.on('click', function (event) { event.stopPropagation(); });
            $yearSel.on('click', function (event) { event.stopPropagation(); });
            $monthSel.on('change', function () { loadDeliveryModeMix(); });
            $yearSel.on('change', function () { loadDeliveryModeMix(); });

            $list.on('click', function (event) {
                var row = event.target.closest ? event.target.closest('[data-mode]') : null;
                if (row) { openModeModal(row.getAttribute('data-mode')); }
            });
        }

        function skeletonRows() {
            var rows2 = '';
            for (var i = 0; i < MODE_SCAFFOLD.length; i++) {
                rows2 += '<div class="vas282-mixrow vas282-skel-row">' +
                    '<span class="vas282-skel-line"></span>' +
                    '<span class="vas282-track"><span class="vas282-fill"></span></span></div>';
            }
            return rows2;
        }

        function loadDeliveryModeMix() {
            mixState = 'loading';
            renderList();

            $.ajax({
                url: VIS.Application.contextUrl + SELF_ENDPOINT + 'GetDeliveryModeMix',
                type: 'GET', dataType: 'json', cache: false,
                data: { month: selectedMonth(), year: selectedYear() },
                success: function (res) {
                    var parsed = parseResponse(res);
                    if (parsed.Error) { mixState = 'error'; rows = []; }
                    else { mixState = 'ready'; rows = buildScaffoldRows(parsed.Modes || []); }
                    renderList();
                },
                error: function () {
                    mixState = 'error'; rows = [];
                    renderList();
                }
            });
        }

        // Zero-fills the fixed D/P/S scaffold with whatever modes the server
        // actually returned (per the prompt's own instruction that this overlay
        // happens in JS, not the API), then computes rounded percentages with the
        // residue assigned to the largest share so the bars always sum to 100.
        function buildScaffoldRows(serverModes) {
            var byCode = {};
            serverModes.forEach(function (m) { byCode[String(m.ModeCode || '').toUpperCase()] = m; });

            var totalValue = 0;
            var scaffolded = MODE_SCAFFOLD.map(function (s) {
                var m = byCode[s.code];
                var value = m ? Number(m.Value) || 0 : 0;
                totalValue += value;
                return {
                    ModeCode: s.code,
                    ModeName: m ? m.ModeName : s.fallback,
                    Color: s.color,
                    Value: value,
                    Percent: 0
                };
            });

            if (totalValue > 0) {
                var sum = 0, largestIdx = 0, largestValue = -1;
                scaffolded.forEach(function (r, i) {
                    var pct = Math.round((r.Value / totalValue) * 100);
                    r.Percent = pct;
                    sum += pct;
                    if (r.Value > largestValue) { largestValue = r.Value; largestIdx = i; }
                });
                var residue = 100 - sum;
                if (residue !== 0) { scaffolded[largestIdx].Percent += residue; }
            }

            return scaffolded;
        }

        function renderList() {
            if (mixState === 'loading') {
                $list.html(skeletonRows());
                return;
            }
            if (mixState === 'error') {
                $list.html('<div class="vas282-empty">' + escapeHtml(label('VAS_282_ErrorState', 'Mix unavailable')) + '</div>');
                return;
            }

            var totalValue = rows.reduce(function (sum, r) { return sum + r.Value; }, 0);
            if (totalValue <= 0) {
                $list.html('<div class="vas282-empty">' + escapeHtml(label('VAS_282_ZeroState', 'No dispatches recorded this month')) + '</div>');
                return;
            }

            $list.html(rows.map(function (r) {
                return '<button type="button" class="vas282-mixrow" data-mode="' + r.ModeCode + '">' +
                    '<span class="vas282-line"><span class="vas282-n" title="' + escapeHtml(r.ModeName) + '">' + escapeHtml(r.ModeName) + '</span>' +
                    '<span class="vas282-v" title="' + r.Percent + '%">' + r.Percent + '%</span></span>' +
                    '<span class="vas282-track"><span class="vas282-fill" style="width:' + r.Percent + '%;background:' + r.Color + '"></span></span></button>';
            }).join(''));
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
            $mask = $('<div class="vas282-mask" role="dialog" aria-modal="true"></div>');
            $modal = $('<div class="vas282-modal"></div>');
            $mHead = $('<div class="vas282-mhead"></div>');
            var $htxt = $('<div class="vas282-htxt"></div>');
            $mBack = $('<button type="button" class="vas282-xbtn" aria-label="' + escapeHtml(label('VAS_282_Back', 'Back')) + '" hidden>' + icon('back') + '</button>');
            var $titleWrap = $('<div></div>');
            $mTitle = $('<h2></h2>');
            $mSub = $('<div class="vas282-msub"></div>');
            $titleWrap.append($mTitle, $mSub);
            $htxt.append($mBack, $titleWrap);
            var $closeBtn = $('<button type="button" class="vas282-xbtn" aria-label="' + escapeHtml(label('VAS_282_Close', 'Close')) + '">' + icon('close') + '</button>');
            $mHead.append($htxt, $closeBtn);

            $mBody = $('<div class="vas282-mbody"></div>');
            $mFoot = $('<div class="vas282-mfoot"></div>');

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
            var ns = '.vas282-' + ($self.AD_UserHomeWidgetID || $self.windowNo || 'widget');

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
            $mBody.html('<div class="vas282-mstate">…</div>');
            $mFoot.html('');
            $mask.addClass('is-open');
        }

        function showLoadError() {
            $mBody.html('<div class="vas282-mstate">' + escapeHtml(label('VAS_282_LoadError', 'Search is unavailable right now. Try again in a moment.')) + '</div>');
        }

        /* ============================================================
         * Mode drill-down (top-level modal opened from a row click)
         * ============================================================ */
        function openModeModal(modeCode) {
            var row = rows.filter(function (r) { return r.ModeCode === modeCode; })[0];
            if (!row) { return; }

            showLoading(row.ModeName);

            fetchModeDispatches(modeCode, 0, MAX_ROWS_PER_PAGE, function (ok, summary, total, list) {
                if (!ok) { showLoadError(); return; }
                docState = { modeCode: modeCode, modeName: row.ModeName, summary: summary, docs: { page: 0, size: MAX_ROWS_PER_PAGE, total: total, rows: list } };
                showScreen(buildModeCfg(row), false);
            });
        }

        function buildModeCfg(row) {
            return {
                title: row.ModeName,
                subtitle: label('VAS_282_SubtitlePrefix', 'Dispatches by delivery mode') + ' · ' + periodLabel(),
                size: '',
                render: renderModeBody
            };
        }

        function fetchModeDispatches(modeCode, page, size, cb) {
            $.ajax({
                url: VIS.Application.contextUrl + SELF_ENDPOINT + 'GetModeDispatches',
                type: 'GET', dataType: 'json', cache: false,
                data: { modeCode: modeCode, month: selectedMonth(), year: selectedYear(), page: page, size: size },
                success: function (res) {
                    var parsed = parseResponse(res);
                    if (parsed.Error || !parsed.Summary) { cb(false); return; }
                    cb(true, parsed.Summary, Number(parsed.Total || 0), (parsed.Rows || []).map(normalizeDispatchRow));
                },
                error: function () { cb(false); }
            });
        }

        function normalizeDispatchRow(row) {
            return {
                SalesOrderId: Number(row.SalesOrderId) || 0,
                SalesOrderNumber: row.SalesOrderNumber || '',
                CustomerName: row.CustomerName || '',
                WarehouseName: row.WarehouseName || '',
                ModeLabel: row.ModeLabel || '',
                PromisedDate: row.PromisedDate || '',
                DispatchValue: Number(row.DispatchValue) || 0
            };
        }

        function renderModeBody() {
            var summary = docState.summary;
            var share = summary.TotalValue > 0 ? Math.round((summary.ModeValue / summary.TotalValue) * 100) : 0;

            var statsHtml = '<div class="vas282-mstats">' +
                statTile(label('VAS_282_StatShare', 'Share of dispatch value'), share + '%') +
                statTile(label('VAS_282_StatDispatchValue', 'Dispatch value'), formatINR(summary.ModeValue)) +
                statTile(label('VAS_282_StatSalesOrders', 'Sales orders'), formatNum(summary.OrderCount)) +
                statTile(label('VAS_282_StatDispatches', 'Dispatches'), formatNum(summary.DispatchCount)) +
            '</div>';

            var secHtml = '<div class="vas282-msec">' + escapeHtml(label('VAS_282_SectionHeading', 'Sales orders dispatched on this mode')) + '</div>';

            if (docState.docs.total <= 0) {
                $mBody.html(statsHtml + secHtml + '<div class="vas282-mstate">' + escapeHtml(label('VAS_282_ZeroState', 'No dispatches recorded this month')) + '</div>');
            } else {
                $mBody.html(statsHtml + secHtml + '<div class="vas282-mtwrap"><div class="vas282-mtbl" id="vas282-docstbl"></div></div>');
                drawDocumentsTable();
            }

            $mFoot.html('<span class="vas282-foot-note"></span><span><button type="button" class="vas282-btn" id="vas282-mclose">' + escapeHtml(label('VAS_282_Close', 'Close')) + '</button></span>');
            $mFoot.find('#vas282-mclose').on('click', closeModal);
        }

        // Minimal column set per the confirmed override (SO No / Customer /
        // Ship from / Delivery mode / Promised / Value) - the leading icon column
        // is preserved from the shared reference pattern (view lines quick-access),
        // but there is no Representative/Status/Delivery-status chip here.
        var DOC_COLS = [
            { key: 'icon', label: '', w: .32 },
            { key: 'so', label: label('VAS_282_ColSoNo', 'SO No'), w: 1.15 },
            { key: 'customer', label: label('VAS_282_Customer', 'Customer'), w: 1.7 },
            { key: 'wh', label: label('VAS_282_ShipFrom', 'Ship from'), w: 1.2 },
            { key: 'mode', label: label('VAS_282_ColDeliveryMode', 'Delivery mode'), w: 1.2 },
            { key: 'promised', label: label('VAS_282_ColPromised', 'Promised'), w: 1 },
            { key: 'value', label: label('VAS_282_ColValue', 'Value'), w: .95, align: 'right' }
        ];

        function drawDocumentsTable() {
            var el = document.getElementById('vas282-docstbl');
            if (!el || !docState) { return; }
            var docs = docState.docs;
            var tpl = DOC_COLS.map(function (c) { return 'minmax(0,' + c.w + 'fr)'; }).join(' ');
            var pages = Math.max(1, Math.ceil(docs.total / docs.size));

            var head = '<div class="vas282-mrow vas282-mhead-row" style="grid-template-columns:' + tpl + '">' +
                DOC_COLS.map(function (c) { return '<span class="vas282-cell' + (c.align === 'right' ? ' right' : '') + '" title="' + escapeHtml(c.label) + '">' + escapeHtml(c.label) + '</span>'; }).join('') +
            '</div>';

            var body = docs.rows.map(function (row) {
                var cells = [
                    '<span class="vas282-cell center"><button type="button" class="vas282-iconbtn" data-lines="' + row.SalesOrderId + '" title="' + escapeHtml(row.SalesOrderNumber) + '">' + icon('lines') + '</button></span>',
                    '<span class="vas282-cell"><button type="button" class="vas282-lnk" data-so="' + row.SalesOrderId + '" title="' + escapeHtml(row.SalesOrderNumber) + '">' + escapeHtml(row.SalesOrderNumber) + '</button></span>',
                    cellHtml(row.CustomerName, 'vas282-c-prim'),
                    cellHtml(row.WarehouseName || '—', 'vas282-c-std'),
                    cellHtml(row.ModeLabel, 'vas282-c-std'),
                    cellHtml(formatDateFull(row.PromisedDate), 'vas282-c-std'),
                    cellHtml(formatINR(row.DispatchValue), 'vas282-c-emph', 'right')
                ];
                return '<div class="vas282-mrow" style="grid-template-columns:' + tpl + '">' + cells.join('') + '</div>';
            }).join('');

            var start = docs.page * docs.size;
            var showingLabel = label('VAS_282_Showing', 'Showing') + ' ' + (docs.total ? (start + 1) : 0) + '–' + (start + docs.rows.length) + ' ' + label('VAS_282_Of', 'of') + ' ' + docs.total + ' · ' + docState.modeName.toLowerCase();

            var foot = '<div class="vas282-mtfoot"><span class="vas282-helper">' + escapeHtml(showingLabel) + '</span>' +
                (pages > 1 ?
                    '<span class="vas282-pager">' +
                        '<button type="button" class="vas282-pbtn" data-table="docs" data-dir="-1"' + (docs.page === 0 ? ' disabled' : '') + ' aria-label="' + escapeHtml(label('VAS_282_PrevPage', 'Previous page')) + '">' + icon('prev') + '</button>' +
                        '<span class="vas282-ptxt">' + (docs.page + 1) + ' ' + escapeHtml(label('VAS_282_Of', 'of')) + ' ' + pages + '</span>' +
                        '<button type="button" class="vas282-pbtn" data-table="docs" data-dir="1"' + (docs.page >= pages - 1 ? ' disabled' : '') + ' aria-label="' + escapeHtml(label('VAS_282_NextPage', 'Next page')) + '">' + icon('next') + '</button>' +
                    '</span>' : '<span></span>') +
            '</div>';

            el.innerHTML = head + '<div class="vas282-mbody-rows">' + body + '</div>' + foot;
        }

        function turnPage(table, dir) {
            if (table === 'docs' && docState) {
                var docs = docState.docs;
                var pages = Math.max(1, Math.ceil(docs.total / docs.size));
                var next = Math.min(pages - 1, Math.max(0, docs.page + dir));
                if (next === docs.page) { return; }
                fetchModeDispatches(docState.modeCode, next, docs.size, function (ok, summary, total, list) {
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
            if (line.QtyDelivered >= line.QtyOrdered && line.QtyOrdered > 0) { return { text: label('VAS_282_DeliveryFull', 'Delivered'), cls: 'vas282-chip-info' }; }
            if (line.QtyDelivered > 0) { return { text: label('VAS_282_LinePartial', 'Partly delivered'), cls: 'vas282-chip-warn' }; }
            return { text: label('VAS_282_LineInProcess', 'In process'), cls: 'vas282-chip-neutral' };
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
            showLoading((docState ? docState.modeName : label('VAS_282_Title', 'Delivery Mode')) + '…');
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
            return '<div class="vas282-mstats">' +
                statTile(label('VAS_282_Customer', 'Customer'), order.CustomerName) +
                statTile(label('VAS_282_SoDate', 'SO date'), formatDateFull(order.SalesOrderDate)) +
                statTile(label('VAS_282_DatePromised', 'Date promised'), formatDateFull(order.DatePromised)) +
                statTile(label('VAS_282_SoValue', 'SO value'), formatINR(order.OrderValue)) +
                statTile(label('VAS_282_ShipFrom', 'Ship from'), order.WarehouseName) +
                statTile(label('VAS_282_ColDeliveryMode', 'Delivery mode'), order.DeliveryMode) +
                statTile(label('VAS_282_DocumentStatus', 'Document status'), order.DocumentStatus) +
                statTile(label('VAS_282_DeliveryStatus', 'Delivery status'), order.DeliveryStatus) +
            '</div>';
        }

        function renderRecordBody(order, lines) {
            var secHtml = '<div class="vas282-msec">' + escapeHtml(label('VAS_282_SalesOrderLines', 'Sales order lines')) + '</div>';
            $mBody.html(soHeaderStats(order) + secHtml + '<div class="vas282-mtwrap"><div class="vas282-mtbl" id="vas282-linetbl-record"></div></div>');

            lineState = { order: order, lines: lines, page: 0, size: MAX_ROWS_PER_PAGE, tableId: 'vas282-linetbl-record' };
            drawLineTable('vas282-linetbl-record');

            $mFoot.html(lineFootHtml(order, lines));
            $mFoot.find('#vas282-mback').on('click', backModal);
            $mFoot.find('#vas282-mclose').on('click', closeModal);
        }

        function lineFootHtml(order, lines) {
            var qtyOrdered = 0, qtyShort = 0;
            lines.forEach(function (line) {
                qtyOrdered += Number(line.QtyOrdered) || 0;
                var pending = Number(line.QtyPending) || 0;
                var freeStock = Number(line.FreeStock) || 0;
                if (pending > freeStock) { qtyShort += (pending - freeStock); }
            });
            var footNote = lines.length + ' ' + label('VAS_282_LinesSuffix', 'lines') +
                ' · ' + formatNum(qtyOrdered) + ' ' + label('VAS_282_QtyOrderedSuffix', 'qty ordered') +
                ' · ' + order.DeliveryStatus +
                (qtyShort > 0 ? ' · ' + formatNum(qtyShort) + ' ' + label('VAS_282_QtyShortSuffix', 'qty short of stock') : '');

            return '<span class="vas282-foot-note">' + escapeHtml(footNote) + '</span>' +
                '<span><button type="button" class="vas282-btn" id="vas282-mback">' + escapeHtml(label('VAS_282_Back', 'Back')) + '</button> ' +
                '<button type="button" class="vas282-btn" id="vas282-mclose">' + escapeHtml(label('VAS_282_Close', 'Close')) + '</button></span>';
        }

        function openLinesModal(orderId) {
            showLoading(label('VAS_282_SalesOrderLines', 'Sales order lines') + '…');
            fetchOrderDetail(orderId, function (detail) {
                if (!detail) { showLoadError(); return; }
                showScreen(buildLinesCfg(detail.order, detail.lines), true);
            });
        }

        function buildLinesCfg(order, lines) {
            return {
                title: label('VAS_282_SalesOrderLines', 'Sales order lines') + ' · ' + order.SalesOrderNumber,
                subtitle: [order.CustomerName, formatDateFull(order.SalesOrderDate), order.DeliveryStatus].filter(function (p) { return p; }).join(' · '),
                size: 'md',
                render: function () { renderLinesBody(order, lines); }
            };
        }

        function renderLinesBody(order, lines) {
            var qtyOrdered = 0, qtyPending = 0;
            lines.forEach(function (l) { qtyOrdered += Number(l.QtyOrdered) || 0; qtyPending += Number(l.QtyPending) || 0; });

            var breadcrumb = '<div class="vas282-polink">' + escapeHtml(label('VAS_282_SalesOrderPrefix', 'Sales order')) +
                ' <button type="button" class="vas282-lnk" data-so="' + order.SalesOrderId + '">' + escapeHtml(order.SalesOrderNumber) + '</button>' +
                ' · ' + escapeHtml(formatDateFull(order.SalesOrderDate)) + ' · ' + escapeHtml(order.DocumentStatus) + '</div>';

            var stats = '<div class="vas282-mstats">' +
                statTile(label('VAS_282_ColLine', 'Line') + 's', String(lines.length)) +
                statTile(label('VAS_282_SoValue', 'SO value'), formatINR(order.OrderValue)) +
                statTile(label('VAS_282_ColOrdered', 'Ordered'), formatNum(qtyOrdered)) +
                statTile(label('VAS_282_ColPending', 'Pending'), formatNum(qtyPending)) +
            '</div>';

            var secHtml = '<div class="vas282-msec">' + escapeHtml(label('VAS_282_SalesOrderLines', 'Sales order lines')) + '</div>';
            $mBody.html(breadcrumb + stats + secHtml + '<div class="vas282-mtwrap"><div class="vas282-mtbl" id="vas282-linetbl-lines"></div></div>');

            lineState = { order: order, lines: lines, page: 0, size: MAX_ROWS_PER_PAGE, tableId: 'vas282-linetbl-lines' };
            drawLineTable('vas282-linetbl-lines');

            $mFoot.html(lineFootHtml(order, lines));
            $mFoot.find('#vas282-mback').on('click', backModal);
            $mFoot.find('#vas282-mclose').on('click', closeModal);
        }

        var LINE_COLS = [
            { key: 'no', label: label('VAS_282_ColLine', 'Line'), w: .35, align: 'right' },
            { key: 'product', label: label('VAS_282_ColProduct', 'Product'), w: 1.5 },
            { key: 'attr', label: label('VAS_282_ColAttribute', 'Attribute'), w: 1.1 },
            { key: 'uom', label: label('VAS_282_ColUom', 'UoM'), w: .5 },
            { key: 'ordered', label: label('VAS_282_ColOrdered', 'Ordered'), w: .7, align: 'right' },
            { key: 'delivered', label: label('VAS_282_ColDelivered', 'Delivered'), w: .7, align: 'right' },
            { key: 'pending', label: label('VAS_282_ColPending', 'Pending'), w: .7, align: 'right' },
            { key: 'instock', label: label('VAS_282_ColInStock', 'In stock'), w: .7, align: 'right' },
            { key: 'rate', label: label('VAS_282_ColRate', 'Rate'), w: .7, align: 'right' },
            { key: 'amount', label: label('VAS_282_ColAmount', 'Amount'), w: .9, align: 'right' },
            { key: 'status', label: label('VAS_282_ColLineStatus', 'Line status'), w: 1 }
        ];

        function drawLineTable(tableId) {
            var el = document.getElementById(tableId);
            if (!el || !lineState) { return; }

            var tpl = LINE_COLS.map(function (c) { return 'minmax(0,' + c.w + 'fr)'; }).join(' ');
            var pages = Math.max(1, Math.ceil(lineState.lines.length / lineState.size));
            if (lineState.page > pages - 1) { lineState.page = pages - 1; }
            var start = lineState.page * lineState.size;
            var slice = lineState.lines.slice(start, start + lineState.size);

            var head = '<div class="vas282-mrow vas282-mhead-row" style="grid-template-columns:' + tpl + '">' +
                LINE_COLS.map(function (c) { return '<span class="vas282-cell' + (c.align === 'right' ? ' right' : '') + '" title="' + escapeHtml(c.label) + '">' + escapeHtml(c.label) + '</span>'; }).join('') +
            '</div>';

            var body = slice.map(function (line) {
                var st = lineStatus(line);
                var cells = [
                    cellHtml(String(line.LineNo), 'vas282-c-std', 'right'),
                    cellHtml(line.ProductName, 'vas282-c-prim'),
                    cellHtml(line.AttributeText, 'vas282-c-std'),
                    cellHtml(line.UomName, 'vas282-c-std'),
                    cellHtml(formatNum(line.QtyOrdered), 'vas282-c-std', 'right'),
                    cellHtml(formatNum(line.QtyDelivered), 'vas282-c-std', 'right'),
                    cellHtml(formatNum(line.QtyPending), 'vas282-c-prim', 'right'),
                    cellHtml(formatNum(line.FreeStock), (line.FreeStock < line.QtyPending ? 'vas282-c-short' : 'vas282-c-ok'), 'right'),
                    cellHtml('₹ ' + formatNum(line.Rate), 'vas282-c-std', 'right'),
                    cellHtml(formatINR(line.Amount), 'vas282-c-emph', 'right')
                ].join('') + '<span class="vas282-cell"><span class="vas282-chip ' + st.cls + '" title="' + escapeHtml(st.text) + '">' + escapeHtml(st.text) + '</span></span>';
                return '<div class="vas282-mrow" style="grid-template-columns:' + tpl + '">' + cells + '</div>';
            }).join('');

            var showingLabel = label('VAS_282_Showing', 'Showing') + ' ' + (lineState.lines.length ? (start + 1) : 0) + '–' + (start + slice.length) + ' ' + label('VAS_282_Of', 'of') + ' ' + lineState.lines.length;

            var foot = '<div class="vas282-mtfoot"><span class="vas282-helper">' + escapeHtml(showingLabel) + '</span>' +
                (pages > 1 ?
                    '<span class="vas282-pager">' +
                        '<button type="button" class="vas282-pbtn" data-table="lines" data-dir="-1"' + (lineState.page === 0 ? ' disabled' : '') + ' aria-label="' + escapeHtml(label('VAS_282_PrevPage', 'Previous page')) + '">' + icon('prev') + '</button>' +
                        '<span class="vas282-ptxt">' + (lineState.page + 1) + ' ' + escapeHtml(label('VAS_282_Of', 'of')) + ' ' + pages + '</span>' +
                        '<button type="button" class="vas282-pbtn" data-table="lines" data-dir="1"' + (lineState.page >= pages - 1 ? ' disabled' : '') + ' aria-label="' + escapeHtml(label('VAS_282_NextPage', 'Next page')) + '">' + icon('next') + '</button>' +
                    '</span>' : '<span></span>') +
            '</div>';

            el.innerHTML = head + '<div class="vas282-mbody-rows">' + body + '</div>' + foot;
        }

        /* Sizes each visible table's rows-per-page to the space actually left in
           the modal body, so the body never grows an inner scrollbar. */
        function fitTable(id, currentSize, onResize) {
            var el = document.getElementById(id);
            if (!el) { return; }
            var avail = el.clientHeight;
            if (avail < 40) { return; }
            var head = el.querySelector('.vas282-mhead-row');
            var foot = el.querySelector('.vas282-mtfoot');
            var row = el.querySelector('.vas282-mbody-rows .vas282-mrow');
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
                fitTable('vas282-docstbl', docState.docs.size, function (n) {
                    var docs = docState.docs;
                    var newPages = Math.max(1, Math.ceil(docs.total / n));
                    var page = Math.min(docs.page, newPages - 1);
                    fetchModeDispatches(docState.modeCode, page, n, function (ok, summary, total, list) {
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
            loadDeliveryModeMix();
        };

        this.getRoot = function () { return $root; };

        this.disposeComponent = function () {
            var ns = '.vas282-' + ($self.AD_UserHomeWidgetID || $self.windowNo || 'widget');
            $(document).off(ns);
            $(window).off(ns);
            if ($mask) { $mask.remove(); }
            $root.remove();
        };

        this.refreshWidget = function () { loadDeliveryModeMix(); };
    };

    VAS.VAS_282_DeliveryModeWidget.prototype.widgetFirevalueChanged = function (value) {
        if (this.listener) { this.listener.widgetFirevalueChanged(value); }
    };

    VAS.VAS_282_DeliveryModeWidget.prototype.addChangeListener = function (listener) {
        this.listener = listener;
    };

    VAS.VAS_282_DeliveryModeWidget.prototype.init = function (windowNo, frame) {
        this.frame = frame;
        this.AD_UserHomeWidgetID = frame.widgetInfo.AD_UserHomeWidgetID;
        this.windowNo = windowNo;
        this.Initalize();
        this.frame.getContentGrid().append(this.getRoot());
        ensureDashInlineSizeVar(this.getRoot());
    };

    VAS.VAS_282_DeliveryModeWidget.prototype.widgetSizeChange = function () { };

    VAS.VAS_282_DeliveryModeWidget.prototype.dispose = function () {
        if (this.disposeComponent) { this.disposeComponent(); }
    };

})(VAS, jQuery);
