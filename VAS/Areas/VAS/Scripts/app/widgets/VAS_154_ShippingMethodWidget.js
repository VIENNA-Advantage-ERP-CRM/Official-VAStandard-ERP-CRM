/**
 * Shipping Method Widget (Delivery Order dashboard)
 * Widget number 154.
 * Widget size: 3 columns x 2 rows.
 * Read-only. For a selected month/year, splits outbound customer Delivery
 * Orders by shipping method (DeliveryViaRule: D=Delivery, P=Pickup,
 * S=Shipper) as a count-sized donut + a fixed 3-row legend (count, share %,
 * total value). Clicking a method (donut segment or legend row) opens a
 * read-only drill-down modal of that method's Delivery Orders. The Delivery
 * Order amount is the backend-computed value (line MovementQty x Sales Order
 * line unit price incl. tax) - the obsolete VA077_TotalSalesAmt column is not
 * used. Never creates/updates/deletes. Plain DOM/jQuery at the framework
 * boundary only.
 * Backend - VAS_154_ShippingMethodWidget/{GetSummary, GetDrillDown}
 * Summary Message Table
 *  #  | Current Text                              | Message Key
 * ----+-------------------------------------------+-----------------------------
 *  1  | Shipping Method                           | VAS_154_SHM_Title
 *  2  | Delivery orders by fulfillment type       | VAS_154_SHM_Subtitle
 *  3  | Delivery                                  | VAS_154_SHM_Delivery
 *  4  | Pickup                                    | VAS_154_SHM_Pickup
 *  5  | Shipper                                   | VAS_154_SHM_Shipper
 *  6  | DOs                                       | VAS_154_SHM_DOs
 *  7  | delivery orders                           | VAS_154_SHM_DeliveryOrders
 *  8  | Click a method for DO details             | VAS_154_SHM_FooterHint
 *  9  | Delivery Orders                           | VAS_154_SHM_ModalOrders
 * 10  | records                                   | VAS_154_SHM_Records
 * 11  | DO Number                                 | VAS_154_SHM_DoNumber
 * 12  | SO Number                                 | VAS_154_SHM_SoNumber
 * 13  | Customer                                  | VAS_154_SHM_Customer
 * 14  | Date                                      | VAS_154_SHM_Date
 * 15  | Amount                                    | VAS_154_SHM_Amount
 * 16  | Showing                                   | VAS_154_SHM_Showing
 * 17  | of                                        | VAS_154_SHM_Of
 * 18  | Close                                     | VAS_154_SHM_Close
 * 19  | Retry                                     | VAS_154_SHM_Retry
 * 20  | Data unavailable                          | VAS_154_SHM_DataUnavailable
 * 21  | No delivery orders for this method in {p}. | VAS_154_SHM_NoRecords
 * 22  | Jan,Feb,...,Dec                           | VAS_154_SHM_MonthsShort
 * 23  | January,...,December                      | VAS_154_SHM_MonthsFull
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

    VAS.VAS_154_ShippingMethodWidget = function () {

        this.frame;
        this.windowNo;

        var $self = this;
        var $root = $('<div class="vas-shm-root">');
        var $dialog;

        var selMonth = 0;   // 1-12
        var selYear = 0;
        var years = [];
        var methods = {};   // code -> { count, amount }
        var totalCount = 0;
        var loadToken = 0;

        // Drill-down modal pagination: 5 delivery orders per page. The modal body is
        // sized for 5 rows in CSS so it never shrinks when a page has fewer.
        var modalRows = [];
        var modalPage = 0;
        var modalTotalAmount = 0;
        var modalPeriodLabel = '';
        var MODAL_ROWS_PER_PAGE = 5;

        var METHODS = [
            { code: 'D', key: 'VAS_154_SHM_Delivery', fb: 'Delivery', color: '#0083DA' },
            { code: 'P', key: 'VAS_154_SHM_Pickup', fb: 'Pickup', color: '#1F83FF' },
            { code: 'S', key: 'VAS_154_SHM_Shipper', fb: 'Shipper', color: '#9ED1FF' }
        ];

        function lbl(key, fallback) {
            var t = VIS.Msg.getMsg(key);
            return (t && t.charAt(0) !== '[') ? t : fallback;
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

        function monthsShort() { return lbl('VAS_154_SHM_MonthsShort', 'Jan,Feb,Mar,Apr,May,Jun,Jul,Aug,Sep,Oct,Nov,Dec').split(','); }
        function monthsFull() { return lbl('VAS_154_SHM_MonthsFull', 'January,February,March,April,May,June,July,August,September,October,November,December').split(','); }
        function methodName(code) { for (var i = 0; i < METHODS.length; i++) { if (METHODS[i].code === code) { return lbl(METHODS[i].key, METHODS[i].fb); } } return code; }

        var currency = {};

        // Same currency-compaction rule used across the dashboard (see Top 10
        // Highest Selling Products): Indian-numbering currencies (INR and its
        // South-Asian neighbours) compact as Lakh/Crore; every other currency
        // (including this system's IQD) compacts as K/M/B. Pulled from the
        // session's system currency (currency.iso) returned by the controller -
        // never hardcoded.
        var INDIAN_NUMBERING_CURRENCIES = ['INR', 'PKR', 'BDT', 'NPR', 'BTN', 'LKR'];
        function usesIndianNumbering() { return INDIAN_NUMBERING_CURRENCIES.indexOf(String(currency.iso || '').toUpperCase()) >= 0; }
        // Strips trailing zeros from an already-toFixed string (e.g. "185.50" ->
        // "185.5", "185.00" -> "185") - same helper as Top 10 Highest Selling.
        function trimZeros(text) { return text.replace(/\.0+$/, '').replace(/(\.\d*?)0+$/, '$1'); }

        // Formats an amount in the system currency (session base currency, returned
        // by the controller) instead of a hardcoded rupee. Falls back to a plain
        // grouped number if the currency is unavailable.
        function formatMoney(value) {
            var num = Number(value || 0).toLocaleString(usesIndianNumbering() ? 'en-IN' : 'en-US', { maximumFractionDigits: 0 });
            var token = currency.iso || currency.symbol || '';
            return token ? (token + ' ' + num) : num;
        }

        // Compact amount for the tight legend column (e.g. "IQD 1.4M", "IQD 185.5K",
        // or "INR 12.5L" / "INR 1.2Cr" when the system currency uses Indian
        // numbering) so the shipping-method name keeps its space.
        function formatMoneyShort(value) {
            var n = Number(value || 0);
            var abs = Math.abs(n);
            var num;
            if (usesIndianNumbering()) {
                if (abs >= 1e7) { num = trimZeros((n / 1e7).toFixed(2)) + 'Cr'; }
                else if (abs >= 1e5) { num = trimZeros((n / 1e5).toFixed(2)) + 'L'; }
                else if (abs >= 1e3) { num = trimZeros((n / 1e3).toFixed(1)) + 'K'; }
                else { num = String(Math.round(n)); }
            } else {
                if (abs >= 1e9) { num = trimZeros((n / 1e9).toFixed(1)) + 'B'; }
                else if (abs >= 1e6) { num = trimZeros((n / 1e6).toFixed(1)) + 'M'; }
                else if (abs >= 1e3) { num = trimZeros((n / 1e3).toFixed(1)) + 'K'; }
                else { num = String(Math.round(n)); }
            }
            var token = currency.iso || currency.symbol || '';
            return token ? (token + ' ' + num) : num;
        }

        function formatDate(iso) {
            if (!iso) { return '-'; }
            var p = String(iso).split('-');
            if (p.length !== 3) { return iso; }
            var mi = parseInt(p[1], 10) - 1;
            return p[2] + ' ' + (monthsShort()[mi] || p[1]) + ' ' + p[0];
        }

        /* ================= WIDGET ================= */

        this.Initalize = function () {
            buildWidget();
            loadSummary(0, 0);
        };

        function buildWidget() {
            $root.html(
                '<div class="vas-shm-card vas-widget-bg">' +
                '<div class="vas-shm-head">' +
                '<div class="vas-shm-head-l">' +
                '<span class="vas-shm-ico">' + truckIcon() + '</span>' +
                '<div class="vas-shm-titles">' +
                '<div class="vas-shm-title">' + escapeHtml(lbl('VAS_154_SHM_Title', 'Shipping Method')) + '</div>' +
                '<div class="vas-shm-sub">' + escapeHtml(lbl('VAS_154_SHM_Subtitle', 'Delivery orders by fulfillment type')) + '</div>' +
                '</div>' +
                '</div>' +
                '<div class="vas-shm-filters">' +
                '<span class="vas-shm-sel"><select class="vas-shm-month" aria-label="Filter by month"></select>' + chevD() + '</span>' +
                '<span class="vas-shm-sel"><select class="vas-shm-year" aria-label="Filter by year"></select>' + chevD() + '</span>' +
                '</div>' +
                '</div>' +
                '<div class="vas-shm-body">' +
                '<div class="vas-shm-donut"></div>' +
                '<div class="vas-shm-legend"></div>' +
                '</div>' +
                '<div class="vas-shm-foot">' +
                '<span class="vas-shm-foot-l"></span>' +
                '<span class="vas-shm-foot-r">' + escapeHtml(lbl('VAS_154_SHM_FooterHint', 'Click a method for DO details')) + '</span>' +
                '</div>' +
                '</div>'
            );
        }

        function populateFilters() {
            var mn = monthsShort();
            var $m = $root.find('.vas-shm-month');
            if (!$m.children().length) {
                var mo = '';
                for (var i = 0; i < 12; i++) { mo += '<option value="' + (i + 1) + '">' + escapeHtml(mn[i]) + '</option>'; }
                $m.html(mo);
            }
            $m.val(selMonth);

            var $y = $root.find('.vas-shm-year');
            var yList = (years && years.length) ? years.slice() : [selYear, selYear - 1, selYear - 2];
            var yo = '';
            for (var j = 0; j < yList.length; j++) { yo += '<option value="' + yList[j] + '">' + yList[j] + '</option>'; }
            $y.html(yo);
            $y.val(selYear);
        }

        function loadSummary(month, year) {
            var myToken = ++loadToken;
            renderLoading();
            $.ajax({
                url: VIS.Application.contextUrl + 'VAS_154_ShippingMethodWidget/GetSummary',
                type: 'GET',
                data: { month: month, year: year },
                success: function (res) {
                    if (myToken !== loadToken) { return; }
                    var data = parseResponse(res);
                    if (!data || data.error) { renderError(); return; }
                    years = data.years || [];
                    currency = data.currency || {};
                    selMonth = Number(data.month || 0);
                    selYear = Number(data.year || 0);
                    methods = {};
                    totalCount = 0;
                    var r = data.rows || [];
                    for (var i = 0; i < r.length; i++) {
                        methods[r[i].methodCode] = { count: Number(r[i].doCount || 0), amount: Number(r[i].totalAmount || 0) };
                        totalCount += Number(r[i].doCount || 0);
                    }
                    populateFilters();
                    render();
                },
                error: function () { if (myToken === loadToken) { renderError(); } }
            });
        }

        function methodData(code) { return methods[code] || { count: 0, amount: 0 }; }

        function renderLoading() {
            // keep header/frame; show a light placeholder without resizing
            $root.find('.vas-shm-donut').html('<div class="vas-shm-loading">…</div>');
        }

        function renderError() {
            $root.find('.vas-shm-donut').html('');
            $root.find('.vas-shm-legend').html(
                '<div class="vas-shm-errbox">' +
                '<div class="vas-shm-errmsg">' + escapeHtml(lbl('VAS_154_SHM_DataUnavailable', 'Data unavailable')) + '</div>' +
                '<button type="button" class="vas-shm-retry">' + escapeHtml(lbl('VAS_154_SHM_Retry', 'Retry')) + '</button>' +
                '</div>'
            );
            $root.find('.vas-shm-foot-l').text('');
        }

        function render() {
            $root.find('.vas-shm-donut').html(donutSvg());
            $root.find('.vas-shm-legend').html(legendHtml());

            var mn = monthsShort();
            var periodLabel = (mn[selMonth - 1] || selMonth) + ' ' + selYear;
            $root.find('.vas-shm-foot-l').text(totalCount + ' ' + lbl('VAS_154_SHM_DeliveryOrders', 'delivery orders') + ' - ' + periodLabel);
        }

        function donutSvg() {
            var r = 44, cx = 60, cy = 60, sw = 14;
            var circ = 2 * Math.PI * r;
            var svg = '<svg viewBox="0 0 120 120" class="vas-shm-svg" role="group" aria-label="' + escapeHtml(lbl('VAS_154_SHM_Title', 'Shipping Method')) + '">';
            // background muted ring
            svg += '<circle cx="' + cx + '" cy="' + cy + '" r="' + r + '" fill="none" stroke="#EDF2F6" stroke-width="' + sw + '"></circle>';

            if (totalCount > 0) {
                var gap = 4; // circumference units between segments
                var acc = 0;
                svg += '<g transform="rotate(-90 ' + cx + ' ' + cy + ')">';
                for (var i = 0; i < METHODS.length; i++) {
                    var m = METHODS[i];
                    var count = methodData(m.code).count;
                    if (count <= 0) { continue; }
                    var frac = count / totalCount;
                    var len = frac * circ;
                    var dash = Math.max(len - gap, 0.001);
                    svg += '<circle class="vas-shm-seg" data-method="' + m.code + '" tabindex="0" role="button" ' +
                        'aria-label="' + escapeHtml(methodName(m.code) + ': ' + count + ' ' + lbl('VAS_154_SHM_DeliveryOrders', 'delivery orders')) + '" ' +
                        'cx="' + cx + '" cy="' + cy + '" r="' + r + '" fill="none" stroke="' + m.color + '" stroke-width="' + sw + '" ' +
                        'stroke-dasharray="' + dash.toFixed(2) + ' ' + (circ - dash).toFixed(2) + '" ' +
                        'stroke-dashoffset="' + (-acc).toFixed(2) + '"></circle>';
                    acc += len;
                }
                svg += '</g>';
            }
            svg += '<text x="' + cx + '" y="' + (cy - 2) + '" text-anchor="middle" class="vas-shm-ctr-num">' + totalCount + '</text>';
            svg += '<text x="' + cx + '" y="' + (cy + 14) + '" text-anchor="middle" class="vas-shm-ctr-lab">' + escapeHtml(lbl('VAS_154_SHM_DOs', 'DOs')) + '</text>';
            svg += '</svg>';
            return svg;
        }

        function legendHtml() {
            var html = '';
            for (var i = 0; i < METHODS.length; i++) {
                var m = METHODS[i];
                var d = methodData(m.code);
                var pct = totalCount > 0 ? Math.round((d.count / totalCount) * 100) : 0;
                html +=
                    '<button type="button" class="vas-shm-lg" data-method="' + m.code + '" aria-label="' + escapeHtml(methodName(m.code)) + '">' +
                    '<span class="vas-shm-sw" style="background:' + m.color + '"></span>' +
                    '<span class="vas-shm-lg-name">' + escapeHtml(methodName(m.code)) + '</span>' +
                    '<span class="vas-shm-lg-count">' + d.count + '</span>' +
                    '<span class="vas-shm-lg-pct">' + pct + '%</span>' +
                    '<span class="vas-shm-lg-amt">' + escapeHtml(formatMoneyShort(d.amount)) + '</span>' +
                    '</button>';
            }
            return html;
        }

        /* ================= MODAL ================= */

        function ensureDialog() {
            if ($dialog) { return; }
            $dialog = $(
                '<div class="vas-shm-overlay vas-shm-hidden" role="dialog" aria-modal="true">' +
                '<div class="vas-shm-modal">' +
                '<div class="vas-shm-mhead">' +
                '<div class="vas-shm-mhead-l"><div class="vas-shm-mtitle"></div><div class="vas-shm-msub"></div></div>' +
                '<button type="button" class="vas-shm-mclose" aria-label="' + escapeHtml(lbl('VAS_154_SHM_Close', 'Close')) + '">' + closeIcon() + '</button>' +
                '</div>' +
                '<div class="vas-shm-mbody"></div>' +
                '<div class="vas-shm-mfoot"></div>' +
                '</div>' +
                '</div>'
            );
            $dialog.find('.vas-shm-mclose').on('click', closeModal);
            $dialog.on('click', function (e) { if (e.target === $dialog[0]) { closeModal(); } });
            $(document).on('keydown.vas-shm', function (e) {
                if (e.key === 'Escape' && $dialog && !$dialog.hasClass('vas-shm-hidden')) { closeModal(); }
            });
            $('body').append($dialog);
        }

        function openModal(code) {
            if (code !== 'D' && code !== 'P' && code !== 'S') { return; }
            ensureDialog();
            $self._lastFocus = document.activeElement;
            $dialog.removeClass('vas-shm-hidden');
            $('body').addClass('vas-shm-body-lock');

            var full = monthsFull();
            var periodLabel = (full[selMonth - 1] || selMonth) + ' ' + selYear;
            $dialog.find('.vas-shm-mtitle').text(methodName(code) + ' - ' + lbl('VAS_154_SHM_ModalOrders', 'Delivery Orders'));
            $dialog.find('.vas-shm-msub').text(periodLabel);
            $dialog.find('.vas-shm-mbody').html('<div class="vas-shm-mloading">…</div>');
            $dialog.find('.vas-shm-mfoot').text('');
            $dialog.find('.vas-shm-mclose').focus();

            var myToken = ++loadToken;
            $.ajax({
                url: VIS.Application.contextUrl + 'VAS_154_ShippingMethodWidget/GetDrillDown',
                type: 'GET',
                data: { methodCode: code, month: selMonth, year: selYear },
                success: function (res) {
                    if (myToken !== loadToken) { return; }
                    var data = parseResponse(res);
                    if (!data || data.error) { $dialog.find('.vas-shm-mbody').html('<div class="vas-shm-empty">' + escapeHtml(lbl('VAS_154_SHM_DataUnavailable', 'Data unavailable')) + '</div>'); return; }
                    renderModal(code, data, periodLabel);
                },
                error: function () {
                    if (myToken !== loadToken) { return; }
                    $dialog.find('.vas-shm-mbody').html('<div class="vas-shm-empty">' + escapeHtml(lbl('VAS_154_SHM_DataUnavailable', 'Data unavailable')) + '</div>');
                }
            });
        }

        function renderModal(code, data, periodLabel) {
            modalRows = data.rows || [];
            modalTotalAmount = Number(data.totalAmount || 0);
            modalPeriodLabel = periodLabel;
            modalPage = 0;
            renderModalPage();
        }

        function renderModalPage() {
            var total = modalRows.length;
            $dialog.find('.vas-shm-msub').text(modalPeriodLabel + ' - ' + total + ' ' + lbl('VAS_154_SHM_Records', 'records'));

            if (!total) {
                $dialog.find('.vas-shm-mbody').html('<div class="vas-shm-empty">' + escapeHtml(lbl('VAS_154_SHM_NoRecords', 'No delivery orders for this method in {p}.').replace('{p}', modalPeriodLabel)) + '</div>');
                $dialog.find('.vas-shm-mfoot').html('');
                return;
            }

            var head =
                '<div class="vas-shm-dohead">' +
                '<div>' + escapeHtml(lbl('VAS_154_SHM_DoNumber', 'DO Number')) + '</div>' +
                '<div>' + escapeHtml(lbl('VAS_154_SHM_SoNumber', 'SO Number')) + '</div>' +
                '<div>' + escapeHtml(lbl('VAS_154_SHM_Customer', 'Customer')) + '</div>' +
                '<div>' + escapeHtml(lbl('VAS_154_SHM_Date', 'Date')) + '</div>' +
                '<div class="r">' + escapeHtml(lbl('VAS_154_SHM_Amount', 'Amount')) + '</div>' +
                '</div>';

            var pageCount = Math.max(1, Math.ceil(total / MODAL_ROWS_PER_PAGE));
            if (modalPage > pageCount - 1) { modalPage = pageCount - 1; }
            if (modalPage < 0) { modalPage = 0; }
            var start = modalPage * MODAL_ROWS_PER_PAGE;
            var slice = modalRows.slice(start, start + MODAL_ROWS_PER_PAGE);

            var body = '';
            for (var i = 0; i < slice.length; i++) {
                var d = slice[i];
                body +=
                    '<div class="vas-shm-dorow">' +
                    '<div class="vas-shm-doid">' + escapeHtml(d.doNumber) + '</div>' +
                    '<div class="vas-shm-docell">' + escapeHtml(d.soNumber || '-') + '</div>' +
                    '<div class="vas-shm-docell" title="' + escapeHtml(d.customerName) + '">' + escapeHtml(d.customerName || '-') + '</div>' +
                    '<div class="vas-shm-docell">' + escapeHtml(formatDate(d.doDate)) + '</div>' +
                    '<div class="vas-shm-doamount">' + escapeHtml(formatMoney(d.doAmount)) + '</div>' +
                    '</div>';
            }

            $dialog.find('.vas-shm-mbody').html(head + '<div class="vas-shm-dolist">' + body + '</div>');

            var footHtml =
                '<span class="vas-shm-mtot">' + escapeHtml(formatMoney(modalTotalAmount)) + '</span>' +
                '<span class="vas-shm-pager">' +
                '<button type="button" class="vas-shm-pgbtn vas-shm-mprev"' + (modalPage <= 0 ? ' disabled' : '') + '>' + chevL() + '</button>' +
                '<span class="vas-shm-pgtext">' + (modalPage + 1) + ' ' + lbl('VAS_154_SHM_Of', 'of') + ' ' + pageCount + '</span>' +
                '<button type="button" class="vas-shm-pgbtn vas-shm-mnext"' + (modalPage >= pageCount - 1 ? ' disabled' : '') + '>' + chevR() + '</button>' +
                '</span>';
            $dialog.find('.vas-shm-mfoot').html(footHtml);
            $dialog.find('.vas-shm-mprev').on('click', function () { if (modalPage > 0) { modalPage--; renderModalPage(); } });
            $dialog.find('.vas-shm-mnext').on('click', function () { if (modalPage < pageCount - 1) { modalPage++; renderModalPage(); } });
        }

        function closeModal() {
            if (!$dialog) { return; }
            $dialog.addClass('vas-shm-hidden');
            $('body').removeClass('vas-shm-body-lock');
            if ($self._lastFocus && $self._lastFocus.focus) { $self._lastFocus.focus(); }
        }

        /* ================= icons ================= */
        function truckIcon() { return '<svg viewBox="0 0 24 24" fill="none" stroke="#0083DA" stroke-width="2" stroke-linecap="round" stroke-linejoin="round"><path d="M14 18V6a2 2 0 0 0-2-2H4a2 2 0 0 0-2 2v11a1 1 0 0 0 1 1h2"/><path d="M15 18H9"/><path d="M19 18h2a1 1 0 0 0 1-1v-3.65a1 1 0 0 0-.22-.624l-3.48-4.35A1 1 0 0 0 17.52 8H14"/><circle cx="17" cy="18" r="2"/><circle cx="7" cy="18" r="2"/></svg>'; }
        function chevD() { return '<svg class="vas-shm-chev" viewBox="0 0 24 24" fill="none" stroke="#0083DA" stroke-width="2.2" stroke-linecap="round" stroke-linejoin="round"><path d="m6 9 6 6 6-6"/></svg>'; }
        function closeIcon() { return '<svg viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2" stroke-linecap="round" stroke-linejoin="round"><path d="M18 6 6 18"/><path d="m6 6 12 12"/></svg>'; }
        function chevL() { return '<svg viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2.2" stroke-linecap="round" stroke-linejoin="round"><path d="m15 18-6-6 6-6"/></svg>'; }
        function chevR() { return '<svg viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2.2" stroke-linecap="round" stroke-linejoin="round"><path d="m9 18 6-6-6-6"/></svg>'; }

        /* ================= events ================= */
        $root.on('change', '.vas-shm-month', function () { loadSummary(Number($(this).val()), selYear); });
        $root.on('change', '.vas-shm-year', function () { loadSummary(selMonth, Number($(this).val())); });
        $root.on('click', '.vas-shm-retry', function () { loadSummary(selMonth, selYear); });
        $root.on('click', '.vas-shm-lg', function () { openModal($(this).data('method')); });
        $root.on('click', '.vas-shm-seg', function () { openModal($(this).attr('data-method')); });
        $root.on('keydown', '.vas-shm-seg', function (e) {
            if (e.key === 'Enter' || e.key === ' ' || e.key === 'Spacebar') { e.preventDefault(); openModal($(this).attr('data-method')); }
        });

        /* ================= lifecycle ================= */
        this.refreshWidget = function () { loadSummary(selMonth, selYear); };
        this.getRoot = function () { return $root; };
        this.disposeComponent = function () {
            $(document).off('keydown.vas-shm');
            $('body').removeClass('vas-shm-body-lock');
            if ($dialog) { $dialog.remove(); $dialog = null; }
            $root.remove();
        };
    };

    VAS.VAS_154_ShippingMethodWidget.prototype.widgetFirevalueChanged = function (value) {
        if (this.listener) { this.listener.widgetFirevalueChanged(value); }
    };
    VAS.VAS_154_ShippingMethodWidget.prototype.addChangeListener = function (listener) { this.listener = listener; };
    VAS.VAS_154_ShippingMethodWidget.prototype.init = function (windowNo, frame) {
        this.frame = frame;
        this.AD_UserHomeWidgetID = frame.widgetInfo.AD_UserHomeWidgetID;
        this.windowNo = windowNo;
        this.Initalize();
        this.frame.getContentGrid().append(this.getRoot());
        ensureDashInlineSizeVar(this.getRoot());
    };
    VAS.VAS_154_ShippingMethodWidget.prototype.widgetSizeChange = function (height, width) { };
    VAS.VAS_154_ShippingMethodWidget.prototype.refreshWidget = function () { this.refreshWidget(); };
    VAS.VAS_154_ShippingMethodWidget.prototype.dispose = function () {
        this.disposeComponent();
        if (this.frame) { this.frame.dispose(); }
        this.frame = null;
    };

})(VAS, jQuery);
