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
 * ── Labels / Message Keys (new* / reused) ─────────────────────────────
 *  Material Receipt Register | VAS_MaterialReceiptRegister*
 *  Received into stores...   | VAS_MRRSubtitle*
 *  items                     | VAS_Items*        Today | VAS_Today*
 *  Yesterday | VAS_Yesterday*   Receipt No. | VAS_ReceiptNo
 *  Linked PO | VAS_LinkedPO*     Supplier | VAS_Supplier*
 *  Customer / Project | VAS_CustomerProject*   Received on | VAS_ReceivedOn*
 *  Put-away on | VAS_PutAwayOn*  Receipt Lines | VAS_ReceiptLines*
 *  Product | VAS_Product   PO Qty | VAS_POQty*   Received | VAS_Received*
 *  UOM | VAS_Uom   Close | VAS_Close   No data | VAS_NoDataAvailable
 *  Showing | VAS_Showing   of | VAS_Of   Previous | VAS_Previous   Next | VAS_Next
 * ─────────────────────────────────────────────────────────────────────
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
            if (diffDays === 0) { return lbl("VAS_Today", "Today"); }
            if (diffDays === 1) { return lbl("VAS_Yesterday", "Yesterday"); }
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
                data: { pageNo: p, pageSize: pageSize },
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

        function createWidget() {
            var $card = $('<div class="vas-mrr-card vas-widget-bg">');

            var $header = $(
                '<div class="vas-mrr-head">' +
                '<span class="vas-mrr-ico">' +
                '<svg viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="1.8" stroke-linecap="round" stroke-linejoin="round"><path d="M3 7l9-4 9 4-9 4-9-4z"/><path d="M3 7v10l9 4 9-4V7"/><path d="M12 11v10"/></svg>' +
                '</span>' +
                '<div class="vas-mrr-titles">' +
                '<div class="vas-mrr-title">' + escapeHtml(lbl("VAS_MaterialReceiptRegister", "Material Receipt Register")) + '</div>' +
                '<div class="vas-mrr-sub">' + escapeHtml(lbl("VAS_MRRSubtitle", "Received into stores - tap for detail")) + '</div>' +
                '</div>' +
                '</div>'
            );

            $listBody = $('<div class="vas-mrr-list">');

            var chevL = '<svg viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2" stroke-linecap="round" stroke-linejoin="round"><polyline points="15 18 9 12 15 6"/></svg>';
            var chevR = '<svg viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2" stroke-linecap="round" stroke-linejoin="round"><polyline points="9 18 15 12 9 6"/></svg>';
            $footer = $(
                '<div class="vas-mrr-foot vas-mrr-hidden">' +
                '<span class="vas-mrr-foot-info"></span>' +
                '<div class="vas-mrr-pager">' +
                '<button type="button" class="vas-mrr-pgbtn vas-mrr-prev" aria-label="' + escapeHtml(lbl("VAS_Previous", "Previous")) + '">' + chevL + '</button>' +
                '<span class="vas-mrr-pgtext"></span>' +
                '<button type="button" class="vas-mrr-pgbtn vas-mrr-next" aria-label="' + escapeHtml(lbl("VAS_Next", "Next")) + '">' + chevR + '</button>' +
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
                $listBody.append('<div class="vas-mrr-empty">' + escapeHtml(lbl("VAS_NoDataAvailable", "No data available")) + '</div>');
                return;
            }
            for (var i = 0; i < ROWS.length; i++) {
                var r = ROWS[i];
                var label = escapeHtml(r.receiptNo) + ' · ' + escapeHtml(r.supplier);
                var meta = escapeHtml(r.itemCount) + ' ' + escapeHtml(lbl("VAS_Items", "items")) + ' · ' + escapeHtml(formatRelDate(r.receivedOn));
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
                $pageText.text(totalPages > 1 ? (pageNo + ' ' + lbl("VAS_Of", "of") + ' ' + totalPages) : '');
            }
            if ($pageInfo) {
                if (totalRecords > 0) {
                    var from = (pageNo - 1) * pageSize + 1;
                    var to = Math.min(pageNo * pageSize, totalRecords);
                    $pageInfo.text(lbl("VAS_Showing", "Showing") + ' ' + from + '–' + to + ' ' + lbl("VAS_Of", "of") + ' ' + totalRecords);
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
                '<span class="vas-mrr-modal-badge"><span class="vas-mrr-pill-ok">' + escapeHtml(lbl("VAS_Received", "Received")) + '</span></span>' +
                '</div>' +
                '<button type="button" class="vas-mrr-modal-close" aria-label="' + escapeHtml(lbl("VAS_Close", "Close")) + '">' +
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
                field("VAS_Receipt", "Receipt", r.receiptNo, true) +
                field("VAS_LinkedPO", "Linked PO", r.linkedPoNo || "—") +
                field("VAS_Supplier", "Supplier", r.supplier) +
                field("VAS_CustomerProject", "Customer / Project", r.customerProject || "—") +
                field("VAS_ReceivedOn", "Received on", formatFullDate(r.receivedOn)) +
                field("VAS_PutAwayOn", "Put-away on", formatFullDate(r.putAwayOn)) +
                '</div>' +
                '<div class="vas-mrr-lines-title">' + escapeHtml(lbl("VAS_ReceiptLines", "Receipt Lines")) + '</div>' +
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

        function renderLines(lines) {
            var $wrap = $dialog.find('.vas-mrr-lines-wrap');
            if (!lines || lines.length === 0) {
                $wrap.html('<div class="vas-mrr-empty">' + escapeHtml(lbl("VAS_NoDataAvailable", "No data available")) + '</div>');
                return;
            }
            var body = '';
            for (var i = 0; i < lines.length; i++) {
                var ln = lines[i];
                body +=
                    '<tr>' +
                    '<td class="vas-mrr-l-item" title="' + escapeHtml(ln.itemName) + '"><span class="vas-mrr-trunc">' + escapeHtml(ln.itemName) + '</span></td>' +
                    '<td class="vas-mrr-l-num">' + escapeHtml(formatQty(ln.poQty)) + '</td>' +
                    '<td class="vas-mrr-l-num">' + escapeHtml(formatQty(ln.receivedQty)) + '</td>' +
                    '<td class="vas-mrr-l-uom" title="' + escapeHtml(ln.uom) + '">' + escapeHtml(ln.uom) + '</td>' +
                    '</tr>';
            }
            $wrap.html(
                '<table class="vas-mrr-lines-table">' +
                '<thead><tr>' +
                '<th class="vas-mrr-l-item">' + escapeHtml(lbl("VAS_Product", "Item")) + '</th>' +
                '<th class="vas-mrr-l-num">' + escapeHtml(lbl("VAS_POQty", "PO Qty")) + '</th>' +
                '<th class="vas-mrr-l-num">' + escapeHtml(lbl("VAS_Received", "Received")) + '</th>' +
                '<th class="vas-mrr-l-uom">' + escapeHtml(lbl("VAS_Uom", "UOM")) + '</th>' +
                '</tr></thead><tbody>' + body + '</tbody></table>'
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
