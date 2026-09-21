/**
 * VAS_276 Quotation Conversion Widget (Sales Order dashboard, 3x1 KPI split tile)
 * Purpose - How much of what was quoted this month actually became an order.
 *           Denominator is issued/accepted quotations (C_Order.IsSalesQuotation='Y',
 *           DocStatus='CO', DateOrdered in the current month) - Drafted/In-Process
 *           quotations are excluded. A quotation counts as converted for the
 *           headline the moment any actual Sales Order line references one of its
 *           lines via C_OrderLine.C_Quotation_Line_ID, even partially; the per-row
 *           percentage (converted qty / quoted qty) carries that nuance. Click /
 *           Enter / Space opens a drill-down modal with an 8-card stat strip (two
 *           even rows) and a paginated, newest-issued-first table of open
 *           quotations.
 *
 *           Unlike VAS_270-275, this widget has NO shared Sales Order record/lines
 *           child modal - per the confirmed UI override, the original draft's
 *           footer pointer to a (now nonexistent) conversion wizard is removed
 *           outright, and no replacement wizard is added here. Instead, the
 *           quotation number navigates directly to the existing Sales Quotation
 *           record window (VAS_SalesQuotation) via the shared cross-window zoom
 *           helper - the same mechanism VAS_268/269 already use - so this is a
 *           single-level modal with no back stack.
 *
 * Design  - 09-kpi-quotation-conversion.html / .md: glass 3x1 KPI tile with a split
 *           layout - percentage headline + fraction meta on the left, converted
 *           value over a two-line "value / converted" label on the right - "ok"
 *           (healthy) tone throughout. Modal shell, stat tiles and paginated table
 *           match the same design language as every other widget on this dashboard.
 *
 * Backend - VAS_276_QuotationConversionWidget/GetSummary       (GET -> tile + 8-card stat-strip figures)
 *           VAS_276_QuotationConversionWidget/GetQuotations    (GET page,size -> paginated open quotations, newest first)
 *
 * ── Labels / Message Keys ─────────────────────────────────────────────────
 *  #  | Current Text                                                          | Message Key
 * ----+------------------------------------------------------------------------+--------------------------------
 *  1  | Quotation Conversion                                                  | VAS_276_Title
 *  2  | of                                                                    | VAS_276_OfSuffix
 *  3  | quotations converted                                                  | VAS_276_QuotationsConvertedSuffix
 *  4  | value                                                                 | VAS_276_SideLabelLine1
 *  5  | converted                                                             | VAS_276_SideLabelLine2
 *  6  | No quotations issued this month                                       | VAS_276_ZeroState
 *  7  | Figures unavailable                                                   | VAS_276_ErrorState
 *  8  | Quotations issued against sales orders raised                        | VAS_276_ModalSubtitlePrefix
 *  9  | Quotations issued                                                     | VAS_276_StatQuotationsIssued
 * 10  | Converted                                                             | VAS_276_StatConverted
 * 11  | Conversion rate                                                       | VAS_276_StatConversionRate
 * 12  | Value converted                                                       | VAS_276_StatValueConverted
 * 13  | Value quoted                                                          | VAS_276_StatValueQuoted
 * 14  | Avg days to convert                                                   | VAS_276_StatAvgDaysToConvert
 * 15  | Expiring in 7 days                                                    | VAS_276_StatExpiringIn7Days
 * 16  | Lost / expired                                                        | VAS_276_StatLostOrExpired
 * 17  | days                                                                  | VAS_276_DaysSuffix
 * 18  | Open quotations                                                       | VAS_276_SectionHeading
 * 19  | Quotation                                                             | VAS_276_ColQuotation
 * 20  | Customer                                                              | VAS_276_Customer
 * 21  | Segment                                                               | VAS_276_ColSegment
 * 22  | Lines                                                                 | VAS_276_ColLines
 * 23  | Quoted qty                                                            | VAS_276_ColQuotedQty
 * 24  | Converted qty                                                         | VAS_276_ColConvertedQty
 * 25  | Conversion                                                            | VAS_276_ColConversion
 * 26  | Valid till                                                            | VAS_276_ColValidTill
 * 27  | Status                                                                | VAS_276_ColStatus
 * 28  | Fully converted                                                       | VAS_276_StatusFullyConverted
 * 29  | Partly Ordered                                                        | VAS_276_StatusPartlyOrdered
 * 30  | Expired                                                               | VAS_276_StatusExpired
 * 31  | Open                                                                  | VAS_276_StatusOpen
 * 32  | Close                                                                 | VAS_276_Close
 * 33  | Previous page                                                         | VAS_276_PrevPage
 * 34  | Next page                                                             | VAS_276_NextPage
 * 35  | of                                                                    | VAS_276_Of
 * 36  | Showing                                                               | VAS_276_Showing
 * 37  | Search is unavailable right now. Try again in a moment.               | VAS_276_LoadError
 */
; VAS = window.VAS || {};

; (function (VAS, $) {

    var MAX_ROWS_PER_PAGE = 10;
    var MIN_ROWS_PER_PAGE = 2;
    var MONTHS = ['Jan', 'Feb', 'Mar', 'Apr', 'May', 'Jun', 'Jul', 'Aug', 'Sep', 'Oct', 'Nov', 'Dec'];
    var ZOOM_WINDOW_NAME = 'VAS_SalesQuotation';

    /* design.md §Widget Header / §Measurement Setup: keep --dash-inline-size on
       :root equal to the dashboard container's current pixel width so the widget
       clamp resolves against the dashboard's visible width, not the viewport. A
       single document-level ResizeObserver serves every widget (matches VAS_269-275). */
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

    VAS.VAS_276_QuotationConversionWidget = function () {

        this.frame;
        this.windowNo;

        var $self = this;
        var $root = $('<div class="vas276-root">');
        var $tile;
        var $mask, $modal, $mTitle, $mSub, $mBody, $mFoot;

        var SELF_ENDPOINT = 'VAS_276_QuotationConversionWidget/';

        var summary = null;           // last fetched GetSummary payload
        var summaryState = 'loading'; // 'loading' | 'ready' | 'error'
        var docsState = { page: 0, size: MAX_ROWS_PER_PAGE, total: 0, rows: [] };
        var zoomWindowId = 0;

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
            if (name === 'prev') {
                return '<svg viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2.4" stroke-linecap="round"><polyline points="15 18 9 12 15 6"/></svg>';
            }
            if (name === 'next') {
                return '<svg viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2.4" stroke-linecap="round"><polyline points="9 18 15 12 9 6"/></svg>';
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

        function parseResponse(res) {
            var data = res;
            if (typeof data === 'string') { data = JSON.parse(data); }
            if (typeof data === 'string') { data = JSON.parse(data); }
            return data || {};
        }

        function cellHtml(value, cls, align) {
            return '<span class="vas276-cell' + (align === 'right' ? ' right' : '') + ' ' + cls + '" title="' + escapeHtml(value) + '">' + escapeHtml(value) + '</span>';
        }

        function statTile(l, v) {
            return '<div class="vas276-mstat"><div class="l">' + escapeHtml(l) + '</div><div class="v" title="' + escapeHtml(v) + '">' + escapeHtml(v) + '</div></div>';
        }

        function statusChipClass(status) {
            if (status === label('VAS_276_StatusFullyConverted', 'Fully converted')) { return 'vas276-chip-ok'; }
            if (status === label('VAS_276_StatusPartlyOrdered', 'Partly Ordered')) { return 'vas276-chip-warn'; }
            if (status === label('VAS_276_StatusExpired', 'Expired')) { return 'vas276-chip-risk'; }
            return 'vas276-chip-neutral';
        }

        /* ============================================================
         * Tile
         * ============================================================ */
        function createWidget() {
            $tile = $(
                '<button type="button" class="vas276-tile" aria-label="' + escapeHtml(label('VAS_276_Title', 'Quotation Conversion')) + '">' +
                    '<p class="vas276-title">' + escapeHtml(label('VAS_276_Title', 'Quotation Conversion')) + '</p>' +
                    '<div class="vas276-row">' +
                        '<div class="vas276-left">' +
                            '<p class="vas276-val"><span class="vas276-skel"></span></p>' +
                            '<p class="vas276-meta"></p>' +
                        '</div>' +
                        '<div class="vas276-side">' +
                            '<div class="v"><span class="vas276-skel"></span></div>' +
                            '<div class="l"></div>' +
                        '</div>' +
                    '</div>' +
                '</button>'
            );
            $tile.on('click', function () { openDocumentsModal(); });
            $root.append($tile);
        }

        function sideLabelHtml() {
            return escapeHtml(label('VAS_276_SideLabelLine1', 'value')) + '<br/>' + escapeHtml(label('VAS_276_SideLabelLine2', 'converted'));
        }

        function renderTile() {
            var $val = $tile.find('.vas276-val');
            var $meta = $tile.find('.vas276-meta');
            var $sideV = $tile.find('.vas276-side .v');
            var $sideL = $tile.find('.vas276-side .l');

            $sideL.html(sideLabelHtml());

            if (summaryState === 'loading') {
                $val.html('<span class="vas276-skel"></span>');
                $meta.text('');
                $sideV.html('<span class="vas276-skel"></span>');
                $tile.removeAttr('aria-disabled');
                return;
            }
            if (summaryState === 'error' || !summary) {
                $val.removeClass().addClass('vas276-val error').text('—');
                $meta.text(label('VAS_276_ErrorState', 'Figures unavailable'));
                $sideV.text('—');
                $tile.attr('aria-disabled', 'true');
                return;
            }

            $tile.removeAttr('aria-disabled');
            $sideV.text(formatINR(summary.ValueConverted));

            if (summary.QuotationsIssued <= 0) {
                $val.removeClass().addClass('vas276-val ok').text('—');
                $meta.text(label('VAS_276_ZeroState', 'No quotations issued this month'));
                return;
            }

            var rate = Math.round(100 * summary.Converted / summary.QuotationsIssued);
            $val.removeClass().addClass('vas276-val ok').text(rate + '%');

            var meta = formatNum(summary.Converted) + ' ' + label('VAS_276_OfSuffix', 'of') + ' ' + formatNum(summary.QuotationsIssued) + ' ' + label('VAS_276_QuotationsConvertedSuffix', 'quotations converted') +
                ' · ' + formatMonthYear(summary.PeriodStart);
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
         * Modal - single level, no back stack: this widget has no shared
         * Sales Order record/lines child modal. The quotation number
         * navigates directly to the Sales Quotation window instead (see
         * openQuotationInWindow).
         * ============================================================ */
        function createModal() {
            $mask = $('<div class="vas276-mask" role="dialog" aria-modal="true"></div>');
            $modal = $('<div class="vas276-modal"></div>');
            var $mHead = $('<div class="vas276-mhead"></div>');
            var $htxt = $('<div class="vas276-htxt"></div>');
            $mTitle = $('<h2></h2>');
            $mSub = $('<div class="vas276-msub"></div>');
            $htxt.append($mTitle, $mSub);
            var $closeBtn = $('<button type="button" class="vas276-xbtn" aria-label="' + escapeHtml(label('VAS_276_Close', 'Close')) + '">' + icon('close') + '</button>');
            $mHead.append($htxt, $closeBtn);

            $mBody = $('<div class="vas276-mbody"></div>');
            $mFoot = $('<div class="vas276-mfoot"></div>');

            $modal.append($mHead, $mBody, $mFoot);
            $mask.append($modal);
            $('body').append($mask);

            $closeBtn.on('click', closeModal);
            $mBody.on('click', function (event) {
                var pageBtn = event.target.closest ? event.target.closest('[data-dir]') : null;
                if (pageBtn) { turnPage(Number(pageBtn.getAttribute('data-dir'))); return; }
                var quoBtn = event.target.closest ? event.target.closest('[data-quo]') : null;
                if (quoBtn) { openQuotationInWindow(Number(quoBtn.getAttribute('data-quo'))); return; }
            });
        }

        function bindDocumentLevelEvents() {
            var ns = '.vas276-' + ($self.AD_UserHomeWidgetID || $self.windowNo || 'widget');

            $(window).on('resize' + ns, function () {
                if ($mask.hasClass('is-open')) { fitDocumentsTable(); }
            });
        }

        function closeModal() {
            $mask.removeClass('is-open');
        }

        function showLoading() {
            $mTitle.text(label('VAS_276_Title', 'Quotation Conversion'));
            $mSub.text('');
            $mBody.html('<div class="vas276-mstate">…</div>');
            $mFoot.html('');
            $mask.addClass('is-open');
        }

        function showLoadError() {
            $mBody.html('<div class="vas276-mstate">' + escapeHtml(label('VAS_276_LoadError', 'Search is unavailable right now. Try again in a moment.')) + '</div>');
        }

        // Navigates to the existing Sales Quotation record window (VAS_SalesQuotation,
        // header AD_Tab_ID 1002560 / C_Order, line AD_Tab_ID 1002561 / C_OrderLine -
        // ZoomUtil resolves the window by name and lands on the correct tab) via the
        // shared cross-window zoom helper (Prompt_Instructions "Scenario 2"). No
        // preview step - the confirmed behaviour is direct navigation on click.
        function openQuotationInWindow(quotationId) {
            if (!window.VAS || !VAS.ZoomUtil) { return; }
            VAS.ZoomUtil.zoomToRecord('C_Order_ID', quotationId, zoomWindowId, ZOOM_WINDOW_NAME, ZOOM_WINDOW_NAME)
                .done(function (id) { if (id > 0) { zoomWindowId = id; } });
        }

        /* ============================================================
         * Documents list (the only modal screen)
         * ============================================================ */
        function openDocumentsModal() {
            if (summaryState === 'error') { return; }
            showLoading();

            function loadAndShow() {
                docsState.page = 0;
                fetchQuotations(0, docsState.size, function (ok) {
                    if (!ok) { showLoadError(); return; }
                    $mTitle.text(label('VAS_276_Title', 'Quotation Conversion'));
                    $mSub.text(label('VAS_276_ModalSubtitlePrefix', 'Quotations issued against sales orders raised') + ' · ' + formatMonthYear(summary.PeriodStart));
                    renderDocumentsBody();
                    requestAnimationFrame(function () { fitDocumentsTable(); requestAnimationFrame(fitDocumentsTable); });
                });
            }

            if (summary) { loadAndShow(); }
            else { loadSummary(function (ok) { if (ok) { loadAndShow(); } else { showLoadError(); } }); }
        }

        function fetchQuotations(page, size, cb) {
            $.ajax({
                url: VIS.Application.contextUrl + SELF_ENDPOINT + 'GetQuotations',
                type: 'GET', dataType: 'json', cache: false,
                data: { page: page, size: size },
                success: function (res) {
                    var parsed = parseResponse(res);
                    if (parsed.Error) { cb(false); return; }
                    docsState.page = page;
                    docsState.size = size;
                    docsState.total = Number(parsed.Total || 0);
                    docsState.rows = (parsed.Rows || []).map(normalizeQuotationRow);
                    cb(true);
                },
                error: function () { cb(false); }
            });
        }

        function normalizeQuotationRow(row) {
            return {
                QuotationId: Number(row.QuotationId) || 0,
                QuotationNo: row.QuotationNo || '',
                ValidTill: row.ValidTill || '',
                CustomerName: row.CustomerName || '',
                SegmentName: row.SegmentName || '',
                LineCount: Number(row.LineCount) || 0,
                QuotedQty: Number(row.QuotedQty) || 0,
                ConvertedQty: Number(row.ConvertedQty) || 0,
                ConversionPercent: (row.ConversionPercent === null || row.ConversionPercent === undefined) ? null : Number(row.ConversionPercent),
                Status: row.Status || ''
            };
        }

        function renderDocumentsBody() {
            var avgDaysText = (summary.AvgDaysToConvert === null || summary.AvgDaysToConvert === undefined)
                ? '—' : (Number(summary.AvgDaysToConvert).toFixed(1) + ' ' + label('VAS_276_DaysSuffix', 'days'));
            var rateText = summary.QuotationsIssued > 0 ? (Math.round(100 * summary.Converted / summary.QuotationsIssued) + '%') : '—';

            var statsHtml = '<div class="vas276-mstats">' +
                statTile(label('VAS_276_StatQuotationsIssued', 'Quotations issued'), formatNum(summary.QuotationsIssued)) +
                statTile(label('VAS_276_StatConverted', 'Converted'), formatNum(summary.Converted)) +
                statTile(label('VAS_276_StatConversionRate', 'Conversion rate'), rateText) +
                statTile(label('VAS_276_StatValueConverted', 'Value converted'), formatINR(summary.ValueConverted)) +
                statTile(label('VAS_276_StatValueQuoted', 'Value quoted'), formatINR(summary.ValueQuoted)) +
                statTile(label('VAS_276_StatAvgDaysToConvert', 'Avg days to convert'), avgDaysText) +
                statTile(label('VAS_276_StatExpiringIn7Days', 'Expiring in 7 days'), formatNum(summary.ExpiringIn7Days)) +
                statTile(label('VAS_276_StatLostOrExpired', 'Lost / expired'), formatNum(summary.LostOrExpired)) +
            '</div>';

            var secHtml = '<div class="vas276-msec">' + escapeHtml(label('VAS_276_SectionHeading', 'Open quotations')) + '</div>';

            if (docsState.total <= 0) {
                $mBody.html(statsHtml + secHtml + '<div class="vas276-mstate">' + escapeHtml(label('VAS_276_ZeroState', 'No quotations issued this month')) + '</div>');
            } else {
                $mBody.html(statsHtml + secHtml + '<div class="vas276-mtwrap"><div class="vas276-mtbl" id="vas276-docstbl"></div></div>');
                drawDocumentsTable();
            }
            $mFoot.html('<span class="vas276-foot-note"></span><span><button type="button" class="vas276-btn" id="vas276-docsclose">' + escapeHtml(label('VAS_276_Close', 'Close')) + '</button></span>');
            $mFoot.find('#vas276-docsclose').on('click', closeModal);
        }

        var DOC_COLS = [
            { key: 'quo', label: label('VAS_276_ColQuotation', 'Quotation'), w: 1 },
            { key: 'customer', label: label('VAS_276_Customer', 'Customer'), w: 1.7 },
            { key: 'segment', label: label('VAS_276_ColSegment', 'Segment'), w: 1.1 },
            { key: 'lines', label: label('VAS_276_ColLines', 'Lines'), w: .55, align: 'right' },
            { key: 'quotedqty', label: label('VAS_276_ColQuotedQty', 'Quoted qty'), w: .9, align: 'right' },
            { key: 'convertedqty', label: label('VAS_276_ColConvertedQty', 'Converted qty'), w: 1, align: 'right' },
            { key: 'conversion', label: label('VAS_276_ColConversion', 'Conversion'), w: .9, align: 'right' },
            { key: 'validtill', label: label('VAS_276_ColValidTill', 'Valid till'), w: 1 },
            { key: 'status', label: label('VAS_276_ColStatus', 'Status'), w: 1.2 }
        ];

        function drawDocumentsTable() {
            var el = document.getElementById('vas276-docstbl');
            if (!el) { return; }
            var tpl = DOC_COLS.map(function (c) { return 'minmax(0,' + c.w + 'fr)'; }).join(' ');
            var pages = Math.max(1, Math.ceil(docsState.total / docsState.size));

            var head = '<div class="vas276-mrow vas276-mhead-row" style="grid-template-columns:' + tpl + '">' +
                DOC_COLS.map(function (c) { return '<span class="vas276-cell' + (c.align === 'right' ? ' right' : '') + '" title="' + escapeHtml(c.label) + '">' + escapeHtml(c.label) + '</span>'; }).join('') +
            '</div>';

            var body = docsState.rows.map(function (row) {
                var conversionText = row.ConversionPercent === null ? '—' : (row.ConversionPercent + '%');
                var cells = [
                    '<span class="vas276-cell"><button type="button" class="vas276-lnk" data-quo="' + row.QuotationId + '" title="' + escapeHtml(row.QuotationNo) + '">' + escapeHtml(row.QuotationNo) + '</button></span>',
                    cellHtml(row.CustomerName, 'vas276-c-prim'),
                    cellHtml(row.SegmentName || '—', 'vas276-c-std'),
                    cellHtml(formatNum(row.LineCount), 'vas276-c-std', 'right'),
                    cellHtml(formatNum(row.QuotedQty), 'vas276-c-std', 'right'),
                    cellHtml(row.ConvertedQty > 0 ? formatNum(row.ConvertedQty) : '—', 'vas276-c-std', 'right'),
                    cellHtml(conversionText, 'vas276-c-emph', 'right'),
                    cellHtml(row.ValidTill ? formatDateFull(row.ValidTill) : '—', 'vas276-c-std'),
                    '<span class="vas276-cell" title="' + escapeHtml(row.Status) + '"><span class="vas276-chip ' + statusChipClass(row.Status) + '">' + escapeHtml(row.Status) + '</span></span>'
                ];
                return '<div class="vas276-mrow" style="grid-template-columns:' + tpl + '">' + cells.join('') + '</div>';
            }).join('');

            var start = docsState.page * docsState.size;
            var showingLabel = label('VAS_276_Showing', 'Showing') + ' ' + (docsState.total ? (start + 1) : 0) + '–' + (start + docsState.rows.length) + ' ' + label('VAS_276_Of', 'of') + ' ' + docsState.total;

            var foot = '<div class="vas276-mtfoot"><span class="vas276-helper">' + escapeHtml(showingLabel) + '</span>' +
                (pages > 1 ?
                    '<span class="vas276-pager">' +
                        '<button type="button" class="vas276-pbtn" data-dir="-1"' + (docsState.page === 0 ? ' disabled' : '') + ' aria-label="' + escapeHtml(label('VAS_276_PrevPage', 'Previous page')) + '">' + icon('prev') + '</button>' +
                        '<span class="vas276-ptxt">' + (docsState.page + 1) + ' ' + escapeHtml(label('VAS_276_Of', 'of')) + ' ' + pages + '</span>' +
                        '<button type="button" class="vas276-pbtn" data-dir="1"' + (docsState.page >= pages - 1 ? ' disabled' : '') + ' aria-label="' + escapeHtml(label('VAS_276_NextPage', 'Next page')) + '">' + icon('next') + '</button>' +
                    '</span>' : '<span></span>') +
            '</div>';

            el.innerHTML = head + '<div class="vas276-mbody-rows">' + body + '</div>' + foot;
        }

        function turnPage(dir) {
            var pages = Math.max(1, Math.ceil(docsState.total / docsState.size));
            var next = Math.min(pages - 1, Math.max(0, docsState.page + dir));
            if (next === docsState.page) { return; }
            fetchQuotations(next, docsState.size, function (ok) { if (ok) { drawDocumentsTable(); } });
        }

        /* Sizes the table's rows-per-page to the space actually left in the modal
           body, so the body never grows an inner scrollbar. Server-paginated, so a
           size change re-fetches the current page at the new size (clamped to the
           new page count) rather than forcing page 0; the guard on "n !== size"
           makes this converge instead of looping. */
        function fitDocumentsTable() {
            if (!$mask.hasClass('is-open')) { return; }
            var el = document.getElementById('vas276-docstbl');
            if (!el) { return; }
            var avail = el.clientHeight;
            if (avail < 40) { return; }
            var head = el.querySelector('.vas276-mhead-row');
            var foot = el.querySelector('.vas276-mtfoot');
            var row = el.querySelector('.vas276-mbody-rows .vas276-mrow');
            if (!head || !row) { return; }
            var rowHeight = row.getBoundingClientRect().height || 30;
            var used = head.getBoundingClientRect().height + (foot ? foot.getBoundingClientRect().height + 8 : 0);
            var n = Math.floor((avail - used) / rowHeight);
            n = Math.max(MIN_ROWS_PER_PAGE, Math.min(MAX_ROWS_PER_PAGE, n));
            if (n === docsState.size) { return; }
            var newPages = Math.max(1, Math.ceil(docsState.total / n));
            var page = Math.min(docsState.page, newPages - 1);
            fetchQuotations(page, n, function (ok) { if (ok) { drawDocumentsTable(); } });
        }

        this.Initalize = function () {
            createWidget();
            createModal();
            bindDocumentLevelEvents();
            loadSummary();
        };

        this.getRoot = function () { return $root; };

        this.disposeComponent = function () {
            var ns = '.vas276-' + ($self.AD_UserHomeWidgetID || $self.windowNo || 'widget');
            $(document).off(ns);
            $(window).off(ns);
            if ($mask) { $mask.remove(); }
            $root.remove();
        };

        this.refreshWidget = function () { loadSummary(); };
    };

    VAS.VAS_276_QuotationConversionWidget.prototype.widgetFirevalueChanged = function (value) {
        if (this.listener) { this.listener.widgetFirevalueChanged(value); }
    };

    VAS.VAS_276_QuotationConversionWidget.prototype.addChangeListener = function (listener) {
        this.listener = listener;
    };

    VAS.VAS_276_QuotationConversionWidget.prototype.init = function (windowNo, frame) {
        this.frame = frame;
        this.AD_UserHomeWidgetID = frame.widgetInfo.AD_UserHomeWidgetID;
        this.windowNo = windowNo;
        this.Initalize();
        this.frame.getContentGrid().append(this.getRoot());
        ensureDashInlineSizeVar(this.getRoot());
    };

    VAS.VAS_276_QuotationConversionWidget.prototype.widgetSizeChange = function () { };

    VAS.VAS_276_QuotationConversionWidget.prototype.dispose = function () {
        if (this.disposeComponent) { this.disposeComponent(); }
    };

})(VAS, jQuery);
