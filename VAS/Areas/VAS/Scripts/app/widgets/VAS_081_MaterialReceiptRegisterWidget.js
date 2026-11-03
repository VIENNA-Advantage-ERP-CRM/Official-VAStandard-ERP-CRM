/**
 * Material Receipt Register Widget (Material Receipt / GRN dashboard)
 * Purpose - 3x2 glass list of recent vendor goods receipts (newest first), one
 *           row per GRN: "Receipt No · Supplier" (label), "N items · when" (meta)
 *           and the total received QUANTITY on the right (never value). Server-
 *           side paged with a footer pager (no inner scrollbar). Clicking a row
 *           opens a receipt-detail modal (header fields + a Receipt Lines table).
 * Design  - design.md (Onfinity) "Data Table / Worklist" + "Widget Footer Pager"
 *           + shared modal. Sizes in `em`; title clamp anchored to
 *           --dash-inline-size. Namespaced vas-mrr-*.
 *
 * Backend - VAS_081_MaterialReceiptRegisterWidget/GetReceipts      (paged list)
 *           VAS_081_MaterialReceiptRegisterWidget/GetReceiptLines  (detail lines)
 * Summary Message Table: see Labels / Message Keys below.
 *
 * Labels / Message Keys
 *  #  | Current Text                          | Message Key
 * ----+---------------------------------------+-----------------------------------
 *  1  | Material Receipt Register             | VAS_081_MaterialReceiptRegister
 *  2  | Received into stores - tap for detail | VAS_081_MRRSubtitle
 *  3  | items                                 | VAS_081_Items
 *  4  | Today                                 | VAS_081_Today
 *  5  | Yesterday                             | VAS_081_Yesterday
 *  6  | (removed - review #23)                | VAS_081_Receipt
 *  7  | Linked PO                             | VAS_081_LinkedPO
 *  8  | (removed - review #23)                | VAS_081_Supplier
 *  9  | (removed - review #23)                | VAS_081_CustomerProject
 * 10  | Movement Date                         | VAS_081_MovementDate (was VAS_081_ReceivedOn)
 * 11  | (removed - review #23)                | VAS_081_PutAwayOn
 * 12  | Receipt Lines                         | VAS_081_ReceiptLines
 * 13  | Item                                  | VAS_081_Product
 * 14  | PO Qty                                | VAS_081_POQty
 * 15  | Received                              | VAS_081_Received
 * 16  | UOM                                   | VAS_081_Uom
 * 17  | Close                                 | VAS_081_Close
 * 18  | No data available                     | VAS_081_NoDataAvailable
 * 19  | Showing                               | VAS_081_Showing
 * 20  | of                                    | VAS_081_Of
 * 21  | Previous                              | VAS_081_Previous
 * 22  | Next                                  | VAS_081_Next
 * 23  | Warehouse                             | VAS_081_Warehouse
 * 24  | Representative                        | VAS_081_Representative
 * 25  | Month                                 | Month
 * 26  | Year                                  | Year
 * 27  | Attribute                             | VAS_081_Attribute
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

    VAS.VAS_081_MaterialReceiptRegisterWidget = function () {

        this.frame;
        this.windowNo;

        var $self = this;
        var grnZoomWindowId = 0;
        var $root = $('<div class="vas-mrr-root">');
        var $listBody;
        var $busy;
        var $footer, $pageInfo, $pageText, $prevBtn, $nextBtn;

        var $dialog, $dialogBody, $dialogTitle, $dialogBusy;

        var ROWS = [];
        var rowsById = {};
        var pageNo = 1;
        var pageSize = 5;
        var loadedPageSize = 5;   // size the rows on screen were fetched with (pager text uses it)
        var totalPages = 0;
        var totalRecords = 0;
        var loading = false;
        var rowResizeObserver = null;

        // Review #20: the register shows one calendar month (current by default);
        // 0 lets the server resolve the current year/month on the first load.
        var filterYear = 0;
        var filterMonth = 0;
        var $monthSelect, $yearSelect;

        // A missing message comes back empty, as the key itself or as "[key]";
        // fall back to the English text in each case.
        function lbl(key, fallback) {
            var t = "";
            try { t = VIS.Msg.getMsg(key); } catch (e) { t = ""; }
            if (t && t !== key && t !== "[" + key + "]") { return t; }
            return (fallback !== undefined) ? fallback : key;
        }

        function escapeHtml(value) {
            return String(value == null ? "" : value)
                .replace(/&/g, "&amp;").replace(/</g, "&lt;").replace(/>/g, "&gt;")
                .replace(/"/g, "&quot;").replace(/'/g, "&#039;");
        }

        function parseResponse(res) {
            var data = res;
            if (typeof data === 'string') { data = JSON.parse(data); }
            if (typeof data === 'string') { data = JSON.parse(data); }
            return data;
        }

        function showBusy(show) {
            if (!$busy || !$busy[0]) { return; }
            $busy.toggleClass('vas-mrr-hidden', !show);
        }
        function showDialogBusy(show) {
            if (!$dialogBusy || !$dialogBusy[0]) { return; }
            $dialogBusy.toggleClass('vas-mrr-hidden', !show);
        }

        function formatQty(value) {
            return Number(value || 0).toLocaleString(window.navigator.language, { maximumFractionDigits: 2 });
        }

        /* today / yesterday / "DD Mon YYYY". */
        function formatRelDate(value) {
            if (!value) { return ""; }
            var d = new Date(value);
            if (isNaN(d.getTime())) { return value; }
            var now = new Date();
            var t = new Date(now.getFullYear(), now.getMonth(), now.getDate());
            var dd = new Date(d.getFullYear(), d.getMonth(), d.getDate());
            var diffDays = Math.round((t - dd) / 86400000);
            if (diffDays === 0) { return lbl("VAS_081_Today", "Today"); }
            if (diffDays === 1) { return lbl("VAS_081_Yesterday", "Yesterday"); }
            return d.toLocaleDateString(window.navigator.language, { day: "2-digit", month: "short", year: "numeric" });
        }

        function formatFullDate(value) {
            if (!value) { return "—"; }
            var d = new Date(value);
            if (isNaN(d.getTime())) { return value; }
            return d.toLocaleDateString(window.navigator.language, { day: "2-digit", month: "short", year: "numeric" });
        }

        this.Initalize = function () {
            createWidget();
            createDialog();
            loadPage(1);
        };

        /* ── List fetch (server-paged) ── */
        function loadPage(p) {
            loading = true;
            showBusy(true);
            if ($prevBtn) { $prevBtn.prop("disabled", true); }
            if ($nextBtn) { $nextBtn.prop("disabled", true); }
            var requestedSize = pageSize;

            $.ajax({
                url: VIS.Application.contextUrl + 'VAS_081_MaterialReceiptRegisterWidget/GetReceipts',
                type: 'GET',
                cache: false,
                data: { pageNo: p, pageSize: requestedSize, year: filterYear, month: filterMonth },
                success: function (res) {
                    var data = parseResponse(res);
                    loading = false;
                    showBusy(false);
                    if (!data || data.error) { setEmpty(); return; }

                    /* 29-Oct 4.13: the page size can be re-measured while this request is in flight
                       (syncPageSize skips the reload then); the rows were cut at the size SENT, so
                       re-fetch at the current size instead of showing e.g. 5 rows under "1–4 of 30". */
                    var servedSize = Number(data.pageSize || requestedSize);
                    if (servedSize !== pageSize) {
                        var firstShown = ((Number(data.pageNo || p) - 1) * servedSize) + 1;
                        loadPage(Math.max(1, Math.ceil(firstShown / pageSize)));
                        return;
                    }
                    loadedPageSize = servedSize;

                    ROWS = data.rows || [];
                    rowsById = {};
                    for (var i = 0; i < ROWS.length; i++) { rowsById[ROWS[i].receiptId] = ROWS[i]; }

                    pageNo = Number(data.pageNo || p);
                    totalPages = Number(data.totalPages || 0);
                    totalRecords = Number(data.totalRecords || 0);

                    // Review #20: keep the filter in step with what the server used
                    // (it resolves the current month on the first load).
                    filterYear = Number(data.year || filterYear);
                    filterMonth = Number(data.month || filterMonth);
                    syncMonthFilter();

                    renderRows();
                    updatePager();
                    window.setTimeout(syncPageSize, 0);
                },
                error: function () { loading = false; showBusy(false); setEmpty(); }
            });
        }

        function setEmpty() {
            ROWS = []; rowsById = {}; totalPages = 0; totalRecords = 0;
            renderRows();
            updatePager();
            window.setTimeout(syncPageSize, 0);
        }

        // Review #20: months use the browser's own month names; years cover the
        // last six years including the current one.
        function buildMonthFilter() {
            if (!$monthSelect || !$yearSelect) { return; }
            var currentYear = new Date().getFullYear();
            var monthIndex;
            $monthSelect.empty();
            for (monthIndex = 1; monthIndex <= 12; monthIndex++) {
                $('<option>')
                    .val(monthIndex)
                    .text(new Date(2000, monthIndex - 1, 1).toLocaleDateString(window.navigator.language, { month: 'short' }))
                    .appendTo($monthSelect);
            }
            $yearSelect.empty();
            for (var year = currentYear; year >= currentYear - 5; year--) {
                $('<option>').val(year).text(year).appendTo($yearSelect);
            }
        }

        function syncMonthFilter() {
            if (!$monthSelect || !$yearSelect) { return; }
            if (filterMonth >= 1 && filterMonth <= 12) { $monthSelect.val(String(filterMonth)); }
            if (filterYear > 0) {
                if (!$yearSelect.find('option[value="' + filterYear + '"]').length) {
                    $('<option>').val(filterYear).text(filterYear).appendTo($yearSelect);
                }
                $yearSelect.val(String(filterYear));
            }
        }

        function measurePageSize() {
            if (!$listBody || !$listBody[0]) { return pageSize; }

            var listHeight = $listBody.innerHeight();
            var rowHeight = $listBody.find('.vas-mrr-row').first().outerHeight(true) || 44;
            if (!listHeight || !rowHeight) { return pageSize; }

            /* Review #45: the register fits 4 records on the standard 2x3 card
               (compact rows); the count still grows with taller screens/zoom. */
            // Capped at the server's own maximum (50) so the size sent is the size served.
            return Math.min(50, Math.max(4, Math.floor(listHeight / rowHeight)));
        }

        function syncPageSize() {
            var nextPageSize = measurePageSize();
            if (nextPageSize === pageSize) { return; }

            /* First record of the page on screen - laid out at the size it was loaded with. */
            var firstRecord = ((pageNo - 1) * loadedPageSize) + 1;
            pageSize = nextPageSize;
            pageNo = Math.max(1, Math.ceil(firstRecord / pageSize));
            if (!loading) { loadPage(pageNo); }
        }

        function createWidget() {
            var $card = $('<div class="vas-mrr-card vas-widget-bg">');

            var $header = $(
                '<div class="vas-mrr-head">' +
                '<span class="vas-mrr-ico">' +
                '<svg viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="1.8" stroke-linecap="round" stroke-linejoin="round"><path d="M3 7l9-4 9 4-9 4-9-4z"/><path d="M3 7v10l9 4 9-4V7"/><path d="M12 11v10"/></svg>' +
                '</span>' +
                '<div class="vas-mrr-titles">' +
                '<div class="vas-mrr-title">' + escapeHtml(lbl("VAS_081_MaterialReceiptRegister", "Material Receipt Register")) + '</div>' +
                '<div class="vas-mrr-sub">' + escapeHtml(lbl("VAS_081_MRRSubtitle", "Received into stores - tap for detail")) + '</div>' +
                '</div>' +
                '<span class="vas-mrr-filter">' +
                '<select class="vas-mrr-month" aria-label="' + escapeHtml(lbl("Month", "Month")) + '"></select>' +
                '<select class="vas-mrr-year" aria-label="' + escapeHtml(lbl("Year", "Year")) + '"></select>' +
                '</span>' +
                '</div>'
            );

            // Review #20: top-right month/year filter, defaulting to the current month.
            $monthSelect = $header.find('.vas-mrr-month');
            $yearSelect = $header.find('.vas-mrr-year');
            buildMonthFilter();
            $monthSelect.on('change', function () {
                filterMonth = Number($(this).val());
                if (!loading) { loadPage(1); }
            });
            $yearSelect.on('change', function () {
                filterYear = Number($(this).val());
                if (!loading) { loadPage(1); }
            });

            $listBody = $('<div class="vas-mrr-list">');

            var chevL = '<svg viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2" stroke-linecap="round" stroke-linejoin="round"><polyline points="15 18 9 12 15 6"/></svg>';
            var chevR = '<svg viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2" stroke-linecap="round" stroke-linejoin="round"><polyline points="9 18 15 12 9 6"/></svg>';
            $footer = $(
                '<div class="vas-mrr-foot vas-mrr-hidden">' +
                '<span class="vas-mrr-foot-info"></span>' +
                '<div class="vas-mrr-pager">' +
                '<button type="button" class="vas-mrr-pgbtn vas-mrr-prev" aria-label="' + escapeHtml(lbl("VAS_081_Previous", "Previous")) + '">' + chevL + '</button>' +
                '<span class="vas-mrr-pgtext"></span>' +
                '<button type="button" class="vas-mrr-pgbtn vas-mrr-next" aria-label="' + escapeHtml(lbl("VAS_081_Next", "Next")) + '">' + chevR + '</button>' +
                '</div>' +
                '</div>'
            );
            $pageInfo = $footer.find('.vas-mrr-foot-info');
            $pageText = $footer.find('.vas-mrr-pgtext');
            $prevBtn = $footer.find('.vas-mrr-prev');
            $nextBtn = $footer.find('.vas-mrr-next');

            $card.append($header).append($listBody).append($footer);
            $root.append($card);

            $busy = $('<div class="vas-mrr-busy vas-mrr-hidden"><div class="vis-busyindicatorinnerwrap"><i class="vis_widgetloader"></i></div></div>');
            $root.append($busy);

            $listBody.on('click', '.vas-mrr-row', function () {
                openDetail($(this).data('receiptid'));
            });
            $prevBtn.on('click', function () { if (!loading && pageNo > 1) { loadPage(pageNo - 1); } });
            $nextBtn.on('click', function () { if (!loading && pageNo < totalPages) { loadPage(pageNo + 1); } });

            if (window.ResizeObserver) {
                rowResizeObserver = new ResizeObserver(function () {
                    window.setTimeout(syncPageSize, 0);
                });
                rowResizeObserver.observe($listBody[0]);
            }
        }

        function renderRows() {
            $listBody.empty();
            if (!ROWS || ROWS.length === 0) {
                $listBody.append('<div class="vas-mrr-empty">' + escapeHtml(lbl("VAS_081_NoDataAvailable", "No data available")) + '</div>');
                return;
            }
            for (var i = 0; i < ROWS.length; i++) {
                var r = ROWS[i];
                var label = escapeHtml(r.receiptNo) + ' · ' + escapeHtml(r.supplier);
                var meta = escapeHtml(r.itemCount) + ' ' + escapeHtml(lbl("VAS_081_Items", "items")) + ' · ' + escapeHtml(formatRelDate(r.receivedOn));
                var $row = $(
                    '<button type="button" class="vas-mrr-row" data-receiptid="' + escapeHtml(r.receiptId) + '">' +
                    '<div class="vas-mrr-row-main">' +
                    '<div class="vas-mrr-row-label" title="' + label + '">' + label + '</div>' +
                    '<div class="vas-mrr-row-meta">' + meta + '</div>' +
                    '</div>' +
                    '<div class="vas-mrr-row-qty" title="' + escapeHtml(formatQty(r.totalReceivedQty)) + '">' + escapeHtml(formatQty(r.totalReceivedQty)) + '</div>' +
                    '</button>'
                );
                $listBody.append($row);
            }
        }

        function updatePager() {
            if ($pageText) {
                $pageText.text(totalPages > 1 ? (pageNo + ' ' + lbl("VAS_081_Of", "of") + ' ' + totalPages) : '');
            }
            if ($pageInfo) {
                if (totalRecords > 0) {
                    // 29-Oct 4.13: count what is actually listed, so "Showing" always matches the rows.
                    var from = (pageNo - 1) * loadedPageSize + 1;
                    var to = Math.min(from + Math.max(ROWS.length, 1) - 1, totalRecords);
                    $pageInfo.text(lbl("VAS_081_Showing", "Showing") + ' ' + from + '–' + to + ' ' + lbl("VAS_081_Of", "of") + ' ' + totalRecords);
                } else { $pageInfo.text(''); }
            }
            if ($prevBtn) { $prevBtn.prop('disabled', loading || pageNo <= 1); }
            if ($nextBtn) { $nextBtn.prop('disabled', loading || totalPages <= 1 || pageNo >= totalPages); }
            if ($footer) { $footer.toggleClass('vas-mrr-hidden', totalRecords <= 0); }
        }

        /* ── Detail modal ── */
        function createDialog() {
            $dialog = $(
                '<div class="vas-mrr-dialog vas-mrr-hidden" role="dialog" aria-modal="true">' +
                '<div class="vas-mrr-scrim"></div>' +
                '<div class="vas-mrr-modal">' +
                '<div class="vas-mrr-modal-head">' +
                '<div class="vas-mrr-modal-title-wrap">' +
                '<h3 class="vas-mrr-modal-title"></h3>' +
                '<span class="vas-mrr-modal-badge"><span class="vas-mrr-pill-ok">' + escapeHtml(lbl("VAS_081_Received", "Received")) + '</span></span>' +
                '</div>' +
                '<button type="button" class="vas-mrr-modal-close" aria-label="' + escapeHtml(lbl("VAS_081_Close", "Close")) + '">' +
                '<svg viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="1.8" stroke-linecap="round" stroke-linejoin="round"><line x1="18" y1="6" x2="6" y2="18"/><line x1="6" y1="6" x2="18" y2="18"/></svg>' +
                '</button>' +
                '</div>' +
                '<div class="vas-mrr-modal-body"></div>' +
                '<div class="vas-mrr-modal-busy vas-mrr-hidden"><div class="vis-busyindicatorinnerwrap"><i class="vis_widgetloader"></i></div></div>' +
                '</div>' +
                '</div>'
            );
            $dialogBody = $dialog.find('.vas-mrr-modal-body');
            $dialogTitle = $dialog.find('.vas-mrr-modal-title');
            $dialogBusy = $dialog.find('.vas-mrr-modal-busy');

            /* Closes only through the Close button - clicking outside the popup does nothing (QA sheet GRN #94). */
            $dialog.find('.vas-mrr-modal-close').on('click', function () { closeDetail(); });
            $dialog.on('click', '.vas-mrr-grn-link', function () {
                var receiptId = Number($(this).attr('data-receiptid'));
                if (!receiptId) { return; }
                closeDetail();
                zoomToGRN(receiptId);
            });
            // Review #22: pager for the modal line table.
            $dialog.on('click', '.vas-mrr-lprev', function () {
                if (detailPage > 1) { detailPage--; renderLinesPage(); }
            });
            $dialog.on('click', '.vas-mrr-lnext', function () {
                detailPage++; renderLinesPage();
            });
            $('body').append($dialog);
        }

        /* The framework navigates IN-PLACE (no new window) only when the payload's ActionName
           equals the name of the window currently HOSTING this widget; resolve it from the
           listener chain (same as VAS_091_MaterialReceiptSearchWidget's zoomTo). */
        function hostWindowName() {
            try {
                var l = $self.listener;
                for (var i = 0; i < 6 && l; i++) {
                    if (l.apanel && l.apanel.gridWindow && l.apanel.gridWindow.getName) {
                        return l.apanel.gridWindow.getName();
                    }
                    if (l.gridWindow && l.gridWindow.getName) {
                        return l.gridWindow.getName();
                    }
                    l = l.listener;
                }
            } catch (e) { }
            return '';
        }

        /* 29-Oct 4.15: the GRN opens on the Goods Receipt Note screen in the SAME tab - hosted in a
           window, the host grid navigates to the record; only on the Home page (no host window) the
           Material Receipt window is opened by name. */
        function zoomToGRN(receiptId) {
            try {
                if ($self.listener && $self.windowNo >= 0) {
                    $self.widgetFirevalueChanged({
                        "TabWhereClause": "M_InOut.M_InOut_ID=" + receiptId,
                        "TabLayout": "Y",
                        "TabIndex": "0",
                        "ActionName": hostWindowName() || "VAS_MaterialReceipt",
                        "ActionType": "W"
                    });
                    return;
                }
                if (!VAS.ZoomUtil) { return; }
                VAS.ZoomUtil.zoomToRecord('M_InOut_ID', receiptId, grnZoomWindowId, 'VAS_MaterialReceipt', 'Material Receipt')
                    .done(function (id) {
                        if (id > 0) { grnZoomWindowId = id; }
                    });
            } catch (e) { /* zoom is best-effort */ }
        }

        function field(labelKey, fallback, value, strong) {
            return '<div class="vas-mrr-field">' +
                '<div class="vas-mrr-field-lbl">' + escapeHtml(lbl(labelKey, fallback)) + '</div>' +
                '<div class="vas-mrr-field-val' + (strong ? ' strong' : '') + '" title="' + escapeHtml(value) + '">' + escapeHtml(value) + '</div>' +
                '</div>';
        }

        function openDetail(receiptId) {
            var r = rowsById[receiptId];
            if (!r || !$dialog) { return; }

            /* QA sheet GRN #107 / 29-Oct 4.15: the receipt number opens that GRN on the Goods Receipt Note screen (same tab). */
            $dialogTitle.html(
                '<button type="button" class="vas-mrr-grn-link" data-receiptid="' + escapeHtml(r.receiptId) + '">' + escapeHtml(r.receiptNo || '') + '</button>' +
                escapeHtml(' - ' + (r.supplier || '')));

            // Review #23 (follow-up): Receipt, Supplier, Customer/Project and
            // Put-away on removed from the modal - the title bar already
            // carries "Receipt No - Supplier".
            var header =
                '<div class="vas-mrr-form">' +
                field("VAS_081_LinkedPO", "Linked PO", r.linkedPoNo || "—") +
                // Review #24: Warehouse and Representative shown on the modal.
                field("VAS_081_Warehouse", "Warehouse", r.warehouseName || "—") +
                field("VAS_081_Representative", "Representative", r.salesRepName || "—") +
                field("VAS_081_MovementDate", "Movement Date", formatFullDate(r.receivedOn)) +
                '</div>' +
                '<div class="vas-mrr-lines-title">' + escapeHtml(lbl("VAS_081_ReceiptLines", "Receipt Lines")) + '</div>' +
                '<div class="vas-mrr-lines-wrap"></div>';

            $dialogBody.html(header);

            $dialog.removeClass('vas-mrr-hidden');
            $('body').addClass('vas-mrr-body-lock');
            loadLines(receiptId);
        }

        function loadLines(receiptId) {
            showDialogBusy(true);
            $.ajax({
                url: VIS.Application.contextUrl + 'VAS_081_MaterialReceiptRegisterWidget/GetReceiptLines',
                type: 'GET',
                cache: false,
                data: { receiptId: receiptId },
                success: function (res) {
                    var data = parseResponse(res);
                    showDialogBusy(false);
                    renderLines((data && data.rows) ? data.rows : []);
                },
                error: function () { showDialogBusy(false); renderLines([]); }
            });
        }

        // Review #22: the modal line table is paginated client-side.
        var detailLines = [];
        var detailPage = 1;
        var detailPageSize = 5;   // fixed: 5 lines per page, pager only above 5

        function renderLines(lines) {
            detailLines = lines || [];
            detailPage = 1;
            renderLinesPage();
        }

        function renderLinesPage() {
            var $wrap = $dialog.find('.vas-mrr-lines-wrap');
            if (!detailLines.length) {
                $wrap.html('<div class="vas-mrr-empty">' + escapeHtml(lbl("VAS_081_NoDataAvailable", "No data available")) + '</div>');
                return;
            }

            var detailTotalPages = Math.max(1, Math.ceil(detailLines.length / detailPageSize));
            if (detailPage > detailTotalPages) { detailPage = detailTotalPages; }
            if (detailPage < 1) { detailPage = 1; }
            var start = (detailPage - 1) * detailPageSize;
            var pageLines = detailLines.slice(start, start + detailPageSize);

            // 29-Oct 4.11: an Attribute column next to the product, shown when ANY line of the
            // receipt (not only this page) has an attribute value; each line shows its own values.
            var showAttrs = false;
            for (var k = 0; k < detailLines.length; k++) {
                if (detailLines[k].attributes && detailLines[k].attributes.length) { showAttrs = true; break; }
            }

            // Review #23: the PO Qty column was removed from the modal table.
            var body = '';
            for (var i = 0; i < pageLines.length; i++) {
                var ln = pageLines[i];
                var attrText = '';
                var attrs = ln.attributes || [];
                for (var a = 0; a < attrs.length; a++) {
                    attrText += (a ? ', ' : '') + attrs[a].label + ': ' + attrs[a].value;
                }
                body +=
                    '<tr>' +
                    '<td class="vas-mrr-l-item" title="' + escapeHtml(ln.itemName) + '"><span class="vas-mrr-trunc">' + escapeHtml(ln.itemName) + '</span></td>' +
                    (showAttrs
                        ? '<td class="vas-mrr-l-attrcol" title="' + escapeHtml(attrText) + '"><span class="vas-mrr-trunc">' + escapeHtml(attrText || '—') + '</span></td>'
                        : '') +
                    '<td class="vas-mrr-l-num">' + escapeHtml(formatQty(ln.receivedQty)) + '</td>' +
                    '<td class="vas-mrr-l-uom" title="' + escapeHtml(ln.uom) + '">' + escapeHtml(ln.uom) + '</td>' +
                    '</tr>';
            }

            var pager = '';
            if (detailTotalPages > 1) {
                var chevL = '<svg viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2" stroke-linecap="round" stroke-linejoin="round"><polyline points="15 18 9 12 15 6"/></svg>';
                var chevR = '<svg viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2" stroke-linecap="round" stroke-linejoin="round"><polyline points="9 18 15 12 9 6"/></svg>';
                pager =
                    '<div class="vas-mrr-lines-foot">' +
                    '<span>' + escapeHtml(lbl("VAS_081_Showing", "Showing")) + ' ' + (start + 1) + '–' + (start + pageLines.length) + ' ' + escapeHtml(lbl("VAS_081_Of", "of")) + ' ' + detailLines.length + '</span>' +
                    '<span class="vas-mrr-pager">' +
                    '<button type="button" class="vas-mrr-pgbtn vas-mrr-lprev" aria-label="' + escapeHtml(lbl("VAS_081_Previous", "Previous")) + '"' + (detailPage === 1 ? ' disabled' : '') + '>' + chevL + '</button>' +
                    '<span class="vas-mrr-pgtext">' + detailPage + ' ' + escapeHtml(lbl("VAS_081_Of", "of")) + ' ' + detailTotalPages + '</span>' +
                    '<button type="button" class="vas-mrr-pgbtn vas-mrr-lnext" aria-label="' + escapeHtml(lbl("VAS_081_Next", "Next")) + '"' + (detailPage === detailTotalPages ? ' disabled' : '') + '>' + chevR + '</button>' +
                    '</span>' +
                    '</div>';
            }

            $wrap.html(
                '<table class="vas-mrr-lines-table">' +
                '<thead><tr>' +
                '<th class="vas-mrr-l-item">' + escapeHtml(lbl("VAS_081_Product", "Item")) + '</th>' +
                (showAttrs ? '<th class="vas-mrr-l-attrcol">' + escapeHtml(lbl("VAS_081_Attribute", "Attribute")) + '</th>' : '') +
                '<th class="vas-mrr-l-num">' + escapeHtml(lbl("VAS_081_Received", "Received")) + '</th>' +
                '<th class="vas-mrr-l-uom">' + escapeHtml(lbl("VAS_081_Uom", "UOM")) + '</th>' +
                '</tr></thead><tbody>' + body + '</tbody></table>' + pager
            );
        }

        function closeDetail() {
            if (!$dialog) { return; }
            $dialog.addClass('vas-mrr-hidden');
            $('body').removeClass('vas-mrr-body-lock');
        }

        this.refreshWidget = function () {
            pageNo = 1;
            loadPage(1);
        };

        this.getRoot = function () { return $root; };

        this.disposeComponent = function () {
            $(document).off('keydown.vas-mrr');
            $('body').removeClass('vas-mrr-body-lock');
            if (rowResizeObserver) { rowResizeObserver.disconnect(); rowResizeObserver = null; }
            if ($dialog) { $dialog.remove(); $dialog = null; }
            $root.remove();
        };
    };

    VAS.VAS_081_MaterialReceiptRegisterWidget.prototype.init = function (windowNo, frame) {
        this.frame = frame;
        this.AD_UserHomeWidgetID = frame.widgetInfo.AD_UserHomeWidgetID;
        this.windowNo = windowNo;
        this.Initalize();
        this.frame.getContentGrid().append(this.getRoot());
        ensureDashInlineSizeVar(this.getRoot());
    };

    VAS.VAS_081_MaterialReceiptRegisterWidget.prototype.addChangeListener = function (listener) {
        this.listener = listener;
    };

    VAS.VAS_081_MaterialReceiptRegisterWidget.prototype.widgetFirevalueChanged = function (value) {
        if (this.listener) { this.listener.widgetFirevalueChanged(value); }
    };

    VAS.VAS_081_MaterialReceiptRegisterWidget.prototype.widgetSizeChange = function (height, width) { };

    VAS.VAS_081_MaterialReceiptRegisterWidget.prototype.refreshWidget = function () {
        this.refreshWidget();
    };

    VAS.VAS_081_MaterialReceiptRegisterWidget.prototype.dispose = function () {
        this.disposeComponent();
        if (this.frame) { this.frame.dispose(); }
        this.frame = null;
    };

    /* Review #46: dashboard records reference this widget by class-name string
       (AD_Widget.Name), and the home and window-specific (GRN screen) catalogs
       hold separate records whose names can differ - one uses the un-numbered
       form. Registering the constructor under both names makes the widget load
       regardless of which record a screen resolves it through. */
    VAS.VAS_MaterialReceiptRegisterWidget = VAS.VAS_081_MaterialReceiptRegisterWidget;

})(VAS, jQuery);
