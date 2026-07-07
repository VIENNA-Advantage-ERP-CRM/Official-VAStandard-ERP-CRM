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
 *  6  | Receipt                               | VAS_081_Receipt
 *  7  | Linked PO                             | VAS_081_LinkedPO
 *  8  | Supplier                              | VAS_081_Supplier
 *  9  | Customer / Project                    | VAS_081_CustomerProject
 * 10  | Received on                           | VAS_081_ReceivedOn
 * 11  | Put-away on                           | VAS_081_PutAwayOn
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
        var $root = $('<div class="vas-mrr-root">');
        var $listBody;
        var $busy;
        var $footer, $pageInfo, $pageText, $prevBtn, $nextBtn;

        var $dialog, $dialogBody, $dialogTitle, $dialogBusy;

        var ROWS = [];
        var rowsById = {};
        var pageNo = 1;
        var pageSize = 5;
        var totalPages = 0;
        var totalRecords = 0;
        var loading = false;

        // Review #20: the register shows one calendar month (current by default);
        // 0 lets the server resolve the current year/month on the first load.
        var filterYear = 0;
        var filterMonth = 0;
        var $monthSelect, $yearSelect;

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

            $.ajax({
                url: VIS.Application.contextUrl + 'VAS_081_MaterialReceiptRegisterWidget/GetReceipts',
                type: 'GET',
                cache: false,
                data: { pageNo: p, pageSize: pageSize, year: filterYear, month: filterMonth },
                success: function (res) {
                    var data = parseResponse(res);
                    loading = false;
                    showBusy(false);
                    if (!data || data.error) { setEmpty(); return; }

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
                },
                error: function () { loading = false; showBusy(false); setEmpty(); }
            });
        }

        function setEmpty() {
            ROWS = []; rowsById = {}; totalPages = 0; totalRecords = 0;
            renderRows();
            updatePager();
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
                    var from = (pageNo - 1) * pageSize + 1;
                    var to = Math.min(pageNo * pageSize, totalRecords);
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

            $dialog.find('.vas-mrr-modal-close').on('click', function () { closeDetail(); });
            $dialog.find('.vas-mrr-scrim').on('click', function () { closeDetail(); });
            // Review #22: pager for the modal line table.
            $dialog.on('click', '.vas-mrr-lprev', function () {
                if (detailPage > 1) { detailPage--; renderLinesPage(); }
            });
            $dialog.on('click', '.vas-mrr-lnext', function () {
                detailPage++; renderLinesPage();
            });
            $(document).on('keydown.vas-mrr', function (e) {
                if (e.key === 'Escape' && !$dialog.hasClass('vas-mrr-hidden')) { closeDetail(); }
            });
            $('body').append($dialog);
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

            $dialogTitle.text((r.receiptNo || '') + ' - ' + (r.supplier || ''));

            var header =
                '<div class="vas-mrr-form">' +
                field("VAS_081_Receipt", "Receipt", r.receiptNo, true) +
                field("VAS_081_LinkedPO", "Linked PO", r.linkedPoNo || "—") +
                field("VAS_081_Supplier", "Supplier", r.supplier) +
                field("VAS_081_CustomerProject", "Customer / Project", r.customerProject || "—") +
                // Review #24: Warehouse and Representative shown on the modal.
                field("VAS_081_Warehouse", "Warehouse", r.warehouseName || "—") +
                field("VAS_081_Representative", "Representative", r.salesRepName || "—") +
                field("VAS_081_ReceivedOn", "Received on", formatFullDate(r.receivedOn)) +
                field("VAS_081_PutAwayOn", "Put-away on", formatFullDate(r.putAwayOn)) +
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
        var detailPageSize = 5;

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

            // Review #23: the PO Qty column was removed from the modal table.
            var body = '';
            for (var i = 0; i < pageLines.length; i++) {
                var ln = pageLines[i];
                body +=
                    '<tr>' +
                    '<td class="vas-mrr-l-item" title="' + escapeHtml(ln.itemName) + '"><span class="vas-mrr-trunc">' + escapeHtml(ln.itemName) + '</span></td>' +
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

    VAS.VAS_081_MaterialReceiptRegisterWidget.prototype.widgetSizeChange = function (height, width) { };

    VAS.VAS_081_MaterialReceiptRegisterWidget.prototype.refreshWidget = function () {
        this.refreshWidget();
    };

    VAS.VAS_081_MaterialReceiptRegisterWidget.prototype.dispose = function () {
        this.disposeComponent();
        if (this.frame) { this.frame.dispose(); }
        this.frame = null;
    };

})(VAS, jQuery);
