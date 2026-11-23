/**
 * Delivery Orders Status Mix Widget (Delivery Order dashboard)
 * Widget number 147.
 * Widget size: 4 columns x 2 rows.
 * For a selected Month + Year, shows each sales representative's outbound
 * customer Delivery Orders as a stacked status-mix bar (In Progress / Draft /
 * Completed / Voided) with a per-rep total, paged 5 reps at a time. Clicking a
 * rep opens a drill-down modal listing that rep's Delivery Orders for the
 * period (number, customer, date, status, value). Read-only. Lists are paged -
 * never an inner scrollbar in the widget. Plain DOM/jQuery at the framework
 * boundary only.
 * Backend - VAS_147_DeliveryStatusMixWidget/{GetStatusMix, GetRepDeliveryOrders}
 * Summary Message Table
 *  #  | Current Text                    | Message Key
 * ----+---------------------------------+---------------------------------
 *  1  | Delivery Orders Status Mix      | VAS_147_DSM_Title
 *  2  | In Progress                     | VAS_147_DSM_InProgress
 *  3  | Draft                           | VAS_147_DSM_Draft
 *  4  | Completed                       | VAS_147_DSM_Completed
 *  5  | Voided                          | VAS_147_DSM_Voided
 *  6  | Showing                         | VAS_147_DSM_Showing
 *  7  | of                              | VAS_147_DSM_Of
 *  8  | No delivery orders              | VAS_147_DSM_Empty
 *  9  | Unassigned                      | VAS_147_DSM_Unassigned
 * 10  | Delivery Orders                 | VAS_147_DSM_ModalTitle
 * 11  | orders                          | VAS_147_DSM_Orders
 * 12  | DO Number                       | VAS_147_DSM_DoNumber
 * 13  | Customer                        | VAS_147_DSM_Customer
 * 14  | Date                            | VAS_147_DSM_Date
 * 15  | Status                          | VAS_147_DSM_Status
 * 16  | Value                           | VAS_147_DSM_Value
 * 17  | Close                           | VAS_147_DSM_Close
 * 18  | Total value                     | VAS_147_DSM_TotalValue
 * 19  | Data unavailable                | VAS_147_DSM_DataUnavailable
 * 20  | Jan,Feb,...,Dec (comma list)    | VAS_147_DSM_Months
 * 21  | Filter by month                 | VAS_147_DSM_FilterByMonth
 * 22  | Filter by year                  | VAS_147_DSM_FilterByYear
 * 23  | View delivery orders for        | VAS_147_DSM_ViewOrdersFor (prefix, name appended in code)
 */
; VAS = window.VAS || {};

; (function (VAS, $) {

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

    VAS.VAS_147_DeliveryStatusMixWidget = function () {

        this.frame;
        this.windowNo;

        var $self = this;
        var $root = $('<div class="vas-dsm-root">');
        var $dialog;

        var rowsPerPage = 5;
        var page = 0;
        var rows = [];
        var years = [];
        var selMonth = 0;   // 1-12
        var selYear = 0;
        var loaded = false;

        // Drill-down modal pagination: 5 delivery orders per page. The modal body
        // is sized for 5 rows in CSS so it never shrinks when a page has fewer.
        var modalRows = [];
        var modalPage = 0;
        var modalTotalValue = 0;
        var MODAL_ROWS_PER_PAGE = 5;

        var STATUS_MAP = {
            'DR': { bucket: 'draft', key: 'VAS_147_DSM_Draft', fb: 'Draft' },
            'IP': { bucket: 'inprogress', key: 'VAS_147_DSM_InProgress', fb: 'In Progress' },
            'WC': { bucket: 'inprogress', key: 'VAS_147_DSM_InProgress', fb: 'In Progress' },
            'IN': { bucket: 'inprogress', key: 'VAS_147_DSM_InProgress', fb: 'In Progress' },
            'CO': { bucket: 'completed', key: 'VAS_147_DSM_Completed', fb: 'Completed' },
            'CL': { bucket: 'completed', key: 'VAS_147_DSM_Completed', fb: 'Completed' },
            'VO': { bucket: 'voided', key: 'VAS_147_DSM_Voided', fb: 'Voided' },
            'RE': { bucket: 'voided', key: 'VAS_147_DSM_Voided', fb: 'Voided' }
        };

        function lbl(key, fallback) {
            return VIS.Msg.getMsg(key);
        }

        function escapeHtml(value) {
            return String(value == null ? "" : value)
                .replace(/&/g, "&amp;").replace(/</g, "&lt;").replace(/>/g, "&gt;")
                .replace(/"/g, "&quot;").replace(/'/g, "&#039;");
        }

        function parseResponse(res) {
            var data = res;
            try {
                if (typeof data === 'string') { data = JSON.parse(data); }
                if (typeof data === 'string') { data = JSON.parse(data); }
            } catch (e) { return null; }
            return data || {};
        }

        function monthNames() {
            // VAS163 2026-11-12: month names from the browser culture, not a fixed English list.
            var list = [];
            for (var i = 0; i < 12; i++) { list.push(new Date(2000, i, 1).toLocaleDateString(window.navigator.language, { month: 'long' })); }
            return list;
        }

        var currency = {};

        // Formats an amount in the system currency (session base currency, returned
        // by the controller) instead of a hardcoded rupee.
        // VAS163 2026-11-12: the server currency (a row carries its order's currency; the
        // session currency otherwise) through the shared helper - no "token + ' ' + number",
        // no fixed 0 decimals; the browser culture places the symbol.
        function formatMoney(value, cur) {
            var c = cur || currency;
            return VAS.formatAmountByBrowser(Number(value || 0), c.iso || '', c.precision, { symbol: c.symbol || '' });
        }

        function rowCurrency(d) {
            return (d && d.currencyIso) ? { iso: d.currencyIso, symbol: d.currencySymbol, precision: d.currencyPrecision } : currency;
        }

        // The modal total: one currency when every row shares it, else a plain number (never guessed).
        function totalCurrency(list) {
            var first = null;
            for (var i = 0; i < list.length; i++) {
                var rc = rowCurrency(list[i]);
                if (first === null) { first = rc; } else if ((rc.iso || '') !== (first.iso || '')) { return { iso: '', symbol: '', precision: first.precision }; }
            }
            return first || currency;
        }

        function formatCount(value) {
            return (Number(value) || 0).toLocaleString(window.navigator.language, { maximumFractionDigits: 0 });
        }

        function periodText() {
            return new Date(selYear, selMonth - 1, 1).toLocaleDateString(window.navigator.language, { month: 'long', year: 'numeric' });
        }

        function formatDate(iso) {
            if (!iso) { return '-'; }
            var p = String(iso).split('-'); // yyyy-MM-dd
            if (p.length !== 3) { return iso; }
            // VAS163 2026-11-12: browser-locale date (was a fixed "dd Month yyyy" pattern);
            // parsed by parts as a local date.
            var d = new Date(parseInt(p[0], 10), parseInt(p[1], 10) - 1, parseInt(p[2], 10));
            if (isNaN(d.getTime())) { return iso; }
            return d.toLocaleDateString(window.navigator.language, { day: '2-digit', month: 'short', year: 'numeric' });
        }

        function repLabel(r) {
            return r.repName ? r.repName : lbl('VAS_147_DSM_Unassigned', 'Unassigned');
        }

        /* ================= WIDGET ================= */

        this.Initalize = function () {
            buildWidget();
            loadPeriod(0, 0);
        };

        function buildWidget() {
            $root.html(
                '<div class="vas-dsm-card vas-widget-bg">' +
                '<div class="vas-dsm-head">' +
                '<div class="vas-dsm-head-l">' +
                '<span class="vas-dsm-ico">' + truckIcon() + '</span>' +
                '<div class="vas-dsm-titles">' +
                '<div class="vas-dsm-title">' + escapeHtml(lbl('VAS_147_DSM_Title', 'Delivery Orders Status Mix')) + '</div>' +
                '<div class="vas-dsm-legend">' +
                legendItem('inprogress', lbl('VAS_147_DSM_InProgress', 'In Progress')) +
                legendItem('draft', lbl('VAS_147_DSM_Draft', 'Draft')) +
                legendItem('completed', lbl('VAS_147_DSM_Completed', 'Completed')) +
                legendItem('voided', lbl('VAS_147_DSM_Voided', 'Voided')) +
                '</div>' +
                '</div>' +
                '</div>' +
                '<div class="vas-dsm-filters">' +
                '<span class="vas-dsm-sel"><select class="vas-dsm-month vas-dsm-month-sel" aria-label="' + escapeHtml(lbl('VAS_147_DSM_FilterByMonth')) + '"></select></span>' +
                '<span class="vas-dsm-sel"><select class="vas-dsm-year vas-dsm-year-sel" aria-label="' + escapeHtml(lbl('VAS_147_DSM_FilterByYear')) + '"></select></span>' +
                '</div>' +
                '</div>' +
                '<div class="vas-dsm-body"></div>' +
                '<div class="vas-dsm-foot"></div>' +
                '</div>'
            );
        }

        function legendItem(bucket, text) {
            return '<span class="vas-dsm-lg"><span class="vas-dsm-sw ' + bucket + '"></span>' + escapeHtml(text) + '</span>';
        }

        function populateFilters() {
            var mn = monthNames();
            var $m = $root.find('.vas-dsm-month');
            if (!$m.children().length) {
                var mo = '';
                for (var i = 0; i < 12; i++) { mo += '<option value="' + (i + 1) + '">' + escapeHtml(mn[i]) + '</option>'; }
                $m.html(mo);
            }
            $m.val(selMonth);

            var $y = $root.find('.vas-dsm-year');
            var yo = '';
            for (var j = 0; j < years.length; j++) { yo += '<option value="' + years[j] + '">' + years[j] + '</option>'; }
            $y.html(yo);
            $y.val(selYear);
        }

        function loadPeriod(month, year) {
            $.ajax({
                url: VIS.Application.contextUrl + 'VAS_147_DeliveryStatusMixWidget/GetStatusMix',
                type: 'GET',
                data: { month: month, year: year },
                success: function (res) {
                    var data = parseResponse(res);
                    if (!data || data.error) { renderError(); return; }
                    years = data.years || [];
                    selMonth = Number(data.month || 0);
                    selYear = Number(data.year || 0);
                    rows = data.rows || [];
                    page = 0;
                    loaded = true;
                    populateFilters();
                    render();
                },
                error: function () { renderError(); }
            });
        }

        function renderError() {
            $root.find('.vas-dsm-body').html('<div class="vas-dsm-empty">' + escapeHtml(lbl('VAS_147_DSM_DataUnavailable', 'Data unavailable')) + '</div>');
            $root.find('.vas-dsm-foot').html('');
        }

        function seg(bucket, count, total) {
            if (count <= 0 || total <= 0) { return ''; }
            return '<span class="vas-dsm-seg ' + bucket + '" style="width:' + ((count / total) * 100).toFixed(1) + '%"></span>';
        }

        function render() {
            var $body = $root.find('.vas-dsm-body');
            if (!rows.length) {
                $body.html('<div class="vas-dsm-empty">' + escapeHtml(lbl('VAS_147_DSM_Empty', 'No delivery orders')) + '</div>');
                $root.find('.vas-dsm-foot').html('');
                return;
            }

            var pageCount = Math.max(1, Math.ceil(rows.length / rowsPerPage));
            if (page > pageCount - 1) { page = pageCount - 1; }
            var start = page * rowsPerPage;
            var slice = rows.slice(start, start + rowsPerPage);

            var html = '';
            for (var i = 0; i < slice.length; i++) {
                var r = slice[i];
                html +=
                    '<button type="button" class="vas-dsm-row" data-index="' + (start + i) + '" aria-label="' + escapeHtml(lbl('VAS_147_DSM_ViewOrdersFor') + ' ' + repLabel(r)) + '">' +
                    '<span class="vas-dsm-rep">' + escapeHtml(repLabel(r)) + '</span>' +
                    '<span class="vas-dsm-track">' +
                    seg('inprogress', r.inProgress, r.total) +
                    seg('draft', r.draft, r.total) +
                    seg('completed', r.completed, r.total) +
                    seg('voided', r.voided, r.total) +
                    '</span>' +
                    '<span class="vas-dsm-total">' + formatCount(r.total) + '</span>' +
                    '</button>';
            }
            $body.html(html);

            var last = Math.min(start + rowsPerPage, rows.length);
            $root.find('.vas-dsm-foot').html(
                '<span class="vas-dsm-help">' + escapeHtml(lbl('VAS_147_DSM_Showing', 'Showing')) + ' ' + formatCount(start + 1) + '–' + formatCount(last) + ' ' + escapeHtml(lbl('VAS_147_DSM_Of', 'of')) + ' ' + formatCount(rows.length) + '</span>' +
                '<span class="vas-dsm-pager">' +
                '<button type="button" class="vas-dsm-pgbtn vas-dsm-prev"' + (page === 0 ? ' disabled' : '') + '>' + chevL() + '</button>' +
                '<span class="vas-dsm-pgtext">' + formatCount(page + 1) + ' ' + escapeHtml(lbl('VAS_147_DSM_Of', 'of')) + ' ' + formatCount(pageCount) + '</span>' +
                '<button type="button" class="vas-dsm-pgbtn vas-dsm-next"' + (page >= pageCount - 1 ? ' disabled' : '') + '>' + chevR() + '</button>' +
                '</span>'
            );
        }

        /* ================= MODAL ================= */

        function ensureDialog() {
            if ($dialog) { return; }
            $dialog = $(
                '<div class="vas-dsm-overlay vas-dsm-hidden" role="dialog" aria-modal="true">' +
                '<div class="vas-dsm-modal">' +
                '<div class="vas-dsm-mhead">' +
                '<div class="vas-dsm-mhead-l"><div class="vas-dsm-mtitle"></div><div class="vas-dsm-msub"></div></div>' +
                '<button type="button" class="vas-dsm-mclose" aria-label="' + escapeHtml(lbl('VAS_147_DSM_Close', 'Close')) + '">' + closeIcon() + '</button>' +
                '</div>' +
                '<div class="vas-dsm-mbody"></div>' +
                '<div class="vas-dsm-mfoot"></div>' +
                '</div>' +
                '</div>'
            );
            $dialog.find('.vas-dsm-mclose').on('click', closeModal);
            $('body').append($dialog);
        }

        function openModal(entry) {
            ensureDialog();
            $dialog.removeClass('vas-dsm-hidden');
            $('body').addClass('vas-dsm-body-lock');

            var periodLabel = periodText(); // VAS163 2026-11-12
            $dialog.find('.vas-dsm-mtitle').text(lbl('VAS_147_DSM_ModalTitle', 'Delivery Orders') + ' · ' + repLabel(entry));
            $dialog.find('.vas-dsm-msub').text(periodLabel);
            $dialog.find('.vas-dsm-mbody').html('<div class="vas-dsm-mloading">…</div>');
            $dialog.find('.vas-dsm-mfoot').text('');

            $.ajax({
                url: VIS.Application.contextUrl + 'VAS_147_DeliveryStatusMixWidget/GetRepDeliveryOrders',
                type: 'GET',
                data: { repId: entry.repId, month: selMonth, year: selYear },
                success: function (res) {
                    var data = parseResponse(res);
                    if (!data || data.error) { $dialog.find('.vas-dsm-mbody').html('<div class="vas-dsm-empty">' + escapeHtml(lbl('VAS_147_DSM_DataUnavailable', 'Data unavailable')) + '</div>'); return; }
                    renderModalRows(entry, data);
                },
                error: function () {
                    $dialog.find('.vas-dsm-mbody').html('<div class="vas-dsm-empty">' + escapeHtml(lbl('VAS_147_DSM_DataUnavailable', 'Data unavailable')) + '</div>');
                }
            });
        }

        function renderModalRows(entry, data) {
            currency = data.currency || {};
            modalRows = data.rows || [];
            modalTotalValue = Number(data.totalValue || 0);
            modalPage = 0;
            renderModalPage(entry);
        }

        function renderModalPage(entry) {
            var periodLabel = periodText(); // VAS163 2026-11-12
            var total = modalRows.length;

            $dialog.find('.vas-dsm-msub').text(periodLabel + ' · ' + formatCount(total) + ' ' + lbl('VAS_147_DSM_Orders', 'orders'));

            var head =
                '<div class="vas-dsm-dohead">' +
                '<div>' + escapeHtml(lbl('VAS_147_DSM_DoNumber', 'DO Number')) + '</div>' +
                '<div>' + escapeHtml(lbl('VAS_147_DSM_Customer', 'Customer')) + '</div>' +
                '<div>' + escapeHtml(lbl('VAS_147_DSM_Date', 'Date')) + '</div>' +
                '<div>' + escapeHtml(lbl('VAS_147_DSM_Status', 'Status')) + '</div>' +
                '<div class="r">' + escapeHtml(lbl('VAS_147_DSM_Value', 'Value')) + '</div>' +
                '</div>';

            var pageCount = Math.max(1, Math.ceil(total / MODAL_ROWS_PER_PAGE));
            if (modalPage > pageCount - 1) { modalPage = pageCount - 1; }
            if (modalPage < 0) { modalPage = 0; }
            var start = modalPage * MODAL_ROWS_PER_PAGE;
            var slice = modalRows.slice(start, start + MODAL_ROWS_PER_PAGE);

            var body = '';
            for (var i = 0; i < slice.length; i++) {
                var d = slice[i];
                var st = STATUS_MAP[d.docStatus] || { bucket: 'draft', key: '', fb: d.docStatus };
                var stLabel = st.key ? lbl(st.key, st.fb) : st.fb;
                body +=
                    '<div class="vas-dsm-dorow">' +
                    '<div class="vas-dsm-doid">' + escapeHtml(d.documentNo) + '</div>' +
                    '<div class="vas-dsm-docell" title="' + escapeHtml(d.customerName) + '">' + escapeHtml(d.customerName || '-') + '</div>' +
                    '<div class="vas-dsm-docell">' + escapeHtml(formatDate(d.movementDate)) + '</div>' +
                    '<div><span class="vas-dsm-dostatus ' + st.bucket + '"><span class="vas-dsm-dot"></span>' + escapeHtml(stLabel) + '</span></div>' +
                    '<div class="vas-dsm-doamount">' + escapeHtml(formatMoney(d.value, rowCurrency(d))) + '</div>' +
                    '</div>';
            }
            if (!total) { body = '<div class="vas-dsm-empty">' + escapeHtml(lbl('VAS_147_DSM_Empty', 'No delivery orders')) + '</div>'; }

            $dialog.find('.vas-dsm-mbody').html(head + '<div class="vas-dsm-dolist">' + body + '</div>');

            var footHtml =
                '<span class="vas-dsm-mtotal">' + escapeHtml(lbl('VAS_147_DSM_TotalValue', 'Total value')) + ' ' + escapeHtml(formatMoney(modalTotalValue, totalCurrency(modalRows))) + '</span>' +
                '<span class="vas-dsm-pager">' +
                '<button type="button" class="vas-dsm-pgbtn vas-dsm-mprev"' + (modalPage <= 0 ? ' disabled' : '') + '>' + chevL() + '</button>' +
                '<span class="vas-dsm-pgtext">' + formatCount(modalPage + 1) + ' ' + lbl('VAS_147_DSM_Of', 'of') + ' ' + formatCount(pageCount) + '</span>' +
                '<button type="button" class="vas-dsm-pgbtn vas-dsm-mnext"' + (modalPage >= pageCount - 1 ? ' disabled' : '') + '>' + chevR() + '</button>' +
                '</span>';
            $dialog.find('.vas-dsm-mfoot').html(footHtml);
            $dialog.find('.vas-dsm-mprev').on('click', function () { if (modalPage > 0) { modalPage--; renderModalPage(entry); } });
            $dialog.find('.vas-dsm-mnext').on('click', function () { if (modalPage < pageCount - 1) { modalPage++; renderModalPage(entry); } });
        }

        function closeModal() {
            if (!$dialog) { return; }
            $dialog.addClass('vas-dsm-hidden');
            $('body').removeClass('vas-dsm-body-lock');
        }

        /* ================= icons ================= */
        function truckIcon() { return '<svg viewBox="0 0 24 24" fill="none" stroke="#0083DA" stroke-width="2" stroke-linecap="round" stroke-linejoin="round"><path d="M14 18V6a2 2 0 0 0-2-2H4a2 2 0 0 0-2 2v11a1 1 0 0 0 1 1h2"/><path d="M15 18H9"/><path d="M19 18h2a1 1 0 0 0 1-1v-3.65a1 1 0 0 0-.22-.624l-3.48-4.35A1 1 0 0 0 17.52 8H14"/><circle cx="17" cy="18" r="2"/><circle cx="7" cy="18" r="2"/></svg>'; }
        function chevD() { return '<svg class="vas-dsm-chev" viewBox="0 0 24 24" fill="none" stroke="#0083DA" stroke-width="2.2" stroke-linecap="round" stroke-linejoin="round"><path d="m6 9 6 6 6-6"/></svg>'; }
        function chevL() { return '<svg viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2.2" stroke-linecap="round" stroke-linejoin="round"><path d="m15 18-6-6 6-6"/></svg>'; }
        function chevR() { return '<svg viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2.2" stroke-linecap="round" stroke-linejoin="round"><path d="m9 18 6-6-6-6"/></svg>'; }
        function closeIcon() { return '<svg viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2" stroke-linecap="round" stroke-linejoin="round"><path d="M18 6 6 18"/><path d="m6 6 12 12"/></svg>'; }

        /* ================= events ================= */
        $root.on('change', '.vas-dsm-month', function () { loadPeriod(Number($(this).val()), selYear); });
        $root.on('change', '.vas-dsm-year', function () { loadPeriod(selMonth, Number($(this).val())); });
        $root.on('click', '.vas-dsm-prev', function () { if (page > 0) { page--; render(); } });
        $root.on('click', '.vas-dsm-next', function () { page++; render(); });
        $root.on('click', '.vas-dsm-row', function () {
            var idx = Number($(this).data('index'));
            if (rows[idx]) { openModal(rows[idx]); }
        });

        /* ================= lifecycle ================= */
        this.refreshWidget = function () { loadPeriod(selMonth, selYear); };
        this.getRoot = function () { return $root; };
        this.disposeComponent = function () {
            $('body').removeClass('vas-dsm-body-lock');
            if ($dialog) { $dialog.remove(); $dialog = null; }
            $root.remove();
        };
    };

    VAS.VAS_147_DeliveryStatusMixWidget.prototype.widgetFirevalueChanged = function (value) {
        if (this.listener) { this.listener.widgetFirevalueChanged(value); }
    };
    VAS.VAS_147_DeliveryStatusMixWidget.prototype.addChangeListener = function (listener) { this.listener = listener; };
    VAS.VAS_147_DeliveryStatusMixWidget.prototype.init = function (windowNo, frame) {
        this.frame = frame;
        this.AD_UserHomeWidgetID = frame.widgetInfo.AD_UserHomeWidgetID;
        this.windowNo = windowNo;
        this.Initalize();
        this.frame.getContentGrid().append(this.getRoot());
        ensureDashInlineSizeVar(this.getRoot());
    };
    VAS.VAS_147_DeliveryStatusMixWidget.prototype.widgetSizeChange = function (height, width) { };
    VAS.VAS_147_DeliveryStatusMixWidget.prototype.refreshWidget = function () { this.refreshWidget(); };
    VAS.VAS_147_DeliveryStatusMixWidget.prototype.dispose = function () {
        this.disposeComponent();
        if (this.frame) { this.frame.dispose(); }
        this.frame = null;
    };

})(VAS, jQuery);
/* VAS163 2026-11-12: VAS.formatAmountByBrowser(value, isoCode, precision, { compact, symbol })
   Amount in the BROWSER culture (window.navigator.language) with the currency the SERVER sent
   (ISO code + precision; the server symbol only as a fallback). Defined in each widget / panel
   that uses it - guarded, so the first file loaded defines it - so the shared util/CurrencyFormat.js
   stays as shipped. Compact amounts keep that file's default scale (VIS.Util.formatCompactAmount:
   L / Cr for Indian-numbering currencies, K / M / B otherwise, digits in the browser culture);
   separators, decimals and the position of the currency sign come from the browser culture. */
; (function () {
    var V = window.VAS = window.VAS || {};
    if (typeof V.formatAmountByBrowser === 'function') return;
    function browserLocale() { try { return window.navigator.language || undefined; } catch (e) { return undefined; } }
    function resolvePrecision(p) {
        var n = Number(p);
        if (p === null || p === undefined || p === '' || isNaN(n) || n < 0) {
            try { n = VIS.Env.getCtx().getStdPrecision(); } catch (e) { n = 2; }
        }
        n = Math.round(Number(n));
        return (isNaN(n) || n < 0) ? 2 : Math.min(n, 10);
    }
    function validIso(c) { c = String(c || '').trim().toUpperCase(); return /^[A-Z]{3}$/.test(c) ? c : ''; }
    function numberFormat(o) {
        try { return new Intl.NumberFormat(browserLocale(), o); } catch (e) { }
        if (o.currencyDisplay === 'narrowSymbol') {
            o.currencyDisplay = 'symbol';
            try { return new Intl.NumberFormat(browserLocale(), o); } catch (e2) { }
        }
        return null;
    }
    /* Currency sign and where the browser culture puts it for this ISO code. */
    var layoutCache = {};
    function currencyLayout(iso) {
        var key = browserLocale() + '|' + iso;
        if (layoutCache[key]) return layoutCache[key];
        var out = { sign: '', before: true, gap: '' };
        var f = iso ? numberFormat({ style: 'currency', currency: iso, currencyDisplay: 'narrowSymbol' }) : null;
        if (f && typeof f.formatToParts === 'function') {
            try {
                var parts = f.formatToParts(1234.5), ci = -1, ni = -1;
                for (var i = 0; i < parts.length; i++) {
                    if (parts[i].type === 'currency' && ci < 0) ci = i;
                    if (parts[i].type === 'integer' && ni < 0) ni = i;
                }
                if (ci >= 0) {
                    out.sign = parts[ci].value;
                    if (ni >= 0) {
                        out.before = ci < ni;
                        var near = out.before ? parts[ci + 1] : parts[ci - 1];
                        out.gap = (near && near.type === 'literal') ? near.value : '';
                    }
                }
            } catch (e) { }
        }
        layoutCache[key] = out;
        return out;
    }
    V.formatAmountByBrowser = function (value, isoCode, precision, options) {
        options = options || {};
        var num = Number(value) || 0;
        var p = resolvePrecision(precision);
        var iso = validIso(isoCode);
        var body;
        if (options.compact && window.VIS && VIS.Util && typeof VIS.Util.formatCompactAmount === 'function') {
            body = VIS.Util.formatCompactAmount(Math.abs(num), iso, p);
        } else {
            var f = numberFormat({ minimumFractionDigits: p, maximumFractionDigits: p });
            try { body = f ? f.format(Math.abs(num)) : Math.abs(num).toFixed(p); } catch (e) { body = String(Math.abs(num)); }
        }
        var minus = (num < 0 && /[1-9]/.test(body)) ? '-' : '';
        var lay = currencyLayout(iso);
        var sign = lay.sign || options.symbol || iso;
        if (!sign) return minus + body;
        if (!lay.sign) return minus + sign + ' ' + body;
        return lay.before ? (minus + sign + lay.gap + body) : (minus + body + lay.gap + sign);
    };
})();
