/**
 * Receipt Queue Widget (Material Receipt / GRN dashboard)
 * Purpose - 6x2 glass queue of in-progress and recent vendor GRNs:
 *           GRN #, supplier, linked PO, received quantity and the receipt's real
 *           document status (M_InOut.DocStatus, resolved to its system name via
 *           the DocStatus reference list, AD_Reference 131). Server-paged at five
 *           rows. Row click opens a GRN detail modal with header fields and lines.
 * Backend - VAS_088_ReceiptQueueWidget/GetReceiptQueue
 *           VAS_088_ReceiptQueueWidget/GetReceiptQueueLines
 * Status  - The pill text is the system DocStatus name (Drafted, In Progress,
 *           Completed, Closed, Reversed, Voided, ...); the pill colour is mapped
 *           from the raw DocStatus code in getStatusClass().
 * Summary Message Table: see Labels / Message Keys below.
 *
 * Labels / Message Keys
 *  #  | Current Text       | Message Key
 * ----+--------------------+-----------------------
 *  1  | Receipt Queue      | VAS_088_ReceiptQueue
 *  2  | GRN #              | VAS_088_GRNNo
 *  3  | Supplier           | VAS_088_Supplier
 *  4  | PO                 | VAS_088_PO
 *  5  | Qty                | VAS_088_Qty
 *  6  | Status             | VAS_088_Status
 *  7  | Received Lines     | VAS_088_ReceivedLines
 *  8  | Month              | Month
 *  9  | Year               | Year
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

    VAS.VAS_088_ReceiptQueueWidget = function () {

        this.frame;
        this.windowNo;

        var $root = $('<div class="vas-rq-root">');
        var $body, $busy, $footer, $pageInfo, $pageText, $prevBtn, $nextBtn;
        var $dialog, $dialogBody, $dialogTitle, $dialogStatus, $dialogBusy;

        var ROWS = [];
        var rowsById = {};
        var pageNo = 1;
        var pageSize = 5;
        var totalPages = 0;
        var totalRecords = 0;
        var loading = false;
        var filterYear = 0;
        var filterMonth = 0;
        var $monthSelect, $yearSelect;
        var rowResizeObserver = null;

        function lbl(key, fallback) {
            var t = VIS.Msg.getMsg(key);
            return (t && t.charAt(0) !== '[') ? t : fallback;
        }

        function escapeHtml(value) {
            return String(value == null ? "" : value)
                .replace(/&/g, "&amp;")
                .replace(/</g, "&lt;")
                .replace(/>/g, "&gt;")
                .replace(/"/g, "&quot;")
                .replace(/'/g, "&#039;");
        }

        function parseResponse(res) {
            var data = res;
            if (typeof data === 'string') { data = JSON.parse(data); }
            if (typeof data === 'string') { data = JSON.parse(data); }
            return data;
        }

        function showBusy(show) {
            if (!$busy || !$busy[0]) { return; }
            $busy.toggleClass('vas-rq-hidden', !show);
        }

        function showDialogBusy(show) {
            if (!$dialogBusy || !$dialogBusy[0]) { return; }
            $dialogBusy.toggleClass('vas-rq-hidden', !show);
        }

        function formatQty(value) {
            return Number(value || 0).toLocaleString(window.navigator.language, { maximumFractionDigits: 2 });
        }

        function formatDateTime(value) {
            if (!value) { return "-"; }
            var d = new Date(value);
            if (isNaN(d.getTime())) { return value; }
            return d.toLocaleString(window.navigator.language, {
                day: "2-digit",
                month: "short",
                year: "numeric",
                hour: "2-digit",
                minute: "2-digit"
            });
        }

        /* Pill colour for a row, chosen from the document's real DocStatus code. */
        function getStatusClass(statusCode) {
            switch (String(statusCode || "").toUpperCase()) {
                case "CO":   // Completed
                case "AP":   // Approved
                    return "ok";
                case "DR":   // Drafted
                case "IP":   // In Progress
                case "IN":   // In Progress
                case "WC":   // Waiting Confirmation
                case "WP":   // Waiting Payment
                    return "warn";
                case "VO":   // Voided
                case "RE":   // Reversed
                case "NA":   // Not Approved
                    return "bad";
                case "CL":   // Closed
                    return "neutral";
                default:
                    return "info";
            }
        }

        /* Display text comes straight from the system status name (controller-resolved). */
        function getStatusText(row) {
            if (row && row.statusText) { return row.statusText; }
            return row && row.statusCode ? row.statusCode : "";
        }

        function getLineStatus(orderedQty, receivedQty) {
            var ordered = Number(orderedQty || 0);
            var received = Number(receivedQty || 0);
            var diff = received - ordered;

            if (Math.abs(diff) < 0.000001) {
                return lbl("VAS_088_Matches", "Matches");
            }
            if (diff < 0) {
                return formatQty(Math.abs(diff)) + " " + lbl("VAS_088_Short", "short");
            }
            return formatQty(diff) + " " + lbl("VAS_088_Over", "over");
        }

        function buildMonthFilter() {
            if (!$monthSelect || !$yearSelect) { return; }

            var currentYear = new Date().getFullYear();
            $monthSelect.empty();
            for (var monthIndex = 1; monthIndex <= 12; monthIndex++) {
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
            if (!$body || !$body[0]) { return pageSize; }

            var $rows = $body.find('.vas-rq-rows');
            var listHeight = $rows.innerHeight();
            var rowHeight = $rows.find('.vas-rq-row').first().outerHeight(true) || 36;
            if (!listHeight || !rowHeight) { return pageSize; }

            return Math.max(3, Math.floor(listHeight / rowHeight));
        }

        function syncPageSize() {
            var nextPageSize = measurePageSize();
            if (nextPageSize === pageSize) { return; }

            var firstRecord = ((pageNo - 1) * pageSize) + 1;
            pageSize = nextPageSize;
            pageNo = Math.max(1, Math.ceil(firstRecord / pageSize));
            if (!loading) { loadReceiptQueue(pageNo); }
        }

        this.Initalize = function () {
            createWidget();
            createDialog();
            loadReceiptQueue(1);
        };

        function createWidget() {
            var chevL = '<svg viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2" stroke-linecap="round" stroke-linejoin="round"><polyline points="15 18 9 12 15 6"/></svg>';
            var chevR = '<svg viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2" stroke-linecap="round" stroke-linejoin="round"><polyline points="9 18 15 12 9 6"/></svg>';

            var $card = $('<div class="vas-rq-card vas-widget-bg">');
            var $header = $(
                '<div class="vas-rq-head">' +
                '<span class="vas-rq-ico">' +
                '<svg viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="1.8" stroke-linecap="round" stroke-linejoin="round"><path d="M3 7l9-4 9 4-9 4-9-4z"/><path d="M3 7v10l9 4 9-4V7"/><path d="M12 11v10"/></svg>' +
                '</span>' +
                '<div class="vas-rq-titles">' +
                '<div class="vas-rq-title">' + escapeHtml(lbl("VAS_088_ReceiptQueue", "Receipt Queue")) + '</div>' +
                '</div>' +
                '<span class="vas-rq-filter">' +
                '<select class="vas-rq-month" aria-label="' + escapeHtml(lbl("Month", "Month")) + '"></select>' +
                '<select class="vas-rq-year" aria-label="' + escapeHtml(lbl("Year", "Year")) + '"></select>' +
                '</span>' +
                '</div>'
            );

            $monthSelect = $header.find('.vas-rq-month');
            $yearSelect = $header.find('.vas-rq-year');
            buildMonthFilter();
            $monthSelect.on('change', function () {
                filterMonth = Number($(this).val());
                if (!loading) { loadReceiptQueue(1); }
            });
            $yearSelect.on('change', function () {
                filterYear = Number($(this).val());
                if (!loading) { loadReceiptQueue(1); }
            });

            $body = $(
                '<div class="vas-rq-body">' +
                '<div class="vas-rq-grid vas-rq-cols vas-rq-headers">' +
                '<div title="' + escapeHtml(lbl("VAS_088_GRNNo", "GRN #")) + '">' + escapeHtml(lbl("VAS_088_GRNNo", "GRN #")) + '</div>' +
                '<div title="' + escapeHtml(lbl("VAS_088_Supplier", "Supplier")) + '">' + escapeHtml(lbl("VAS_088_Supplier", "Supplier")) + '</div>' +
                '<div title="' + escapeHtml(lbl("VAS_088_PO", "PO")) + '">' + escapeHtml(lbl("VAS_088_PO", "PO")) + '</div>' +
                '<div class="vas-rq-right" title="' + escapeHtml(lbl("VAS_088_Qty", "Qty")) + '">' + escapeHtml(lbl("VAS_088_Qty", "Qty")) + '</div>' +
                '<div class="vas-rq-center" title="' + escapeHtml(lbl("VAS_088_Status", "Status")) + '">' + escapeHtml(lbl("VAS_088_Status", "Status")) + '</div>' +
                '</div>' +
                '<div class="vas-rq-rows"></div>' +
                '</div>'
            );

            $footer = $(
                '<div class="vas-rq-foot vas-rq-hidden">' +
                '<span class="vas-rq-foot-info"></span>' +
                '<div class="vas-rq-pager">' +
                '<button type="button" class="vas-rq-pgbtn vas-rq-prev" aria-label="' + escapeHtml(lbl("VAS_088_Previous", "Previous")) + '">' + chevL + '</button>' +
                '<span class="vas-rq-pgtext"></span>' +
                '<button type="button" class="vas-rq-pgbtn vas-rq-next" aria-label="' + escapeHtml(lbl("VAS_088_Next", "Next")) + '">' + chevR + '</button>' +
                '</div>' +
                '</div>'
            );

            $pageInfo = $footer.find('.vas-rq-foot-info');
            $pageText = $footer.find('.vas-rq-pgtext');
            $prevBtn = $footer.find('.vas-rq-prev');
            $nextBtn = $footer.find('.vas-rq-next');

            $card.append($header).append($body).append($footer);
            $root.append($card);

            $busy = $('<div class="vas-rq-busy vas-rq-hidden"><div class="vis-busyindicatorinnerwrap"><i class="vis_widgetloader"></i></div></div>');
            $root.append($busy);

            $body.on('click', '.vas-rq-row', function () {
                openReceiptDetail($(this).data('grnid'));
            });
            $prevBtn.on('click', function () {
                if (!loading && pageNo > 1) { loadReceiptQueue(pageNo - 1); }
            });
            $nextBtn.on('click', function () {
                if (!loading && pageNo < totalPages) { loadReceiptQueue(pageNo + 1); }
            });

            if (window.ResizeObserver) {
                rowResizeObserver = new ResizeObserver(function () {
                    window.setTimeout(syncPageSize, 0);
                });
                rowResizeObserver.observe($body.find('.vas-rq-rows')[0]);
            }
        }

        function loadReceiptQueue(page) {
            loading = true;
            showBusy(true);
            if ($prevBtn) { $prevBtn.prop("disabled", true); }
            if ($nextBtn) { $nextBtn.prop("disabled", true); }

            $.ajax({
                url: VIS.Application.contextUrl + 'VAS_088_ReceiptQueueWidget/GetReceiptQueue',
                type: 'GET',
                cache: false,
                data: { pageNo: page, pageSize: pageSize, year: filterYear, month: filterMonth },
                success: function (res) {
                    var data = parseResponse(res);
                    loading = false;
                    showBusy(false);

                    if (!data || data.error) {
                        renderReceiptQueue([], 0);
                        return;
                    }

                    pageNo = Number(data.pageNo || page);
                    totalPages = Number(data.totalPages || 0);
                    filterYear = Number(data.year || filterYear);
                    filterMonth = Number(data.month || filterMonth);
                    syncMonthFilter();
                    renderReceiptQueue(data.rows || [], Number(data.totalRecords || 0));
                    window.setTimeout(syncPageSize, 0);
                },
                error: function () {
                    loading = false;
                    showBusy(false);
                    renderReceiptQueue([], 0);
                }
            });
        }

        function renderReceiptQueue(rows, totalCount) {
            ROWS = rows || [];
            totalRecords = Number(totalCount || 0);
            rowsById = {};

            var $rows = $body.find('.vas-rq-rows');
            $rows.empty();

            if (ROWS.length === 0) {
                $rows.append('<div class="vas-rq-empty">' + escapeHtml(lbl("VAS_088_NoDataAvailable", "No data available")) + '</div>');
                updatePager();
                return;
            }

            for (var i = 0; i < ROWS.length; i++) {
                var r = ROWS[i];
                rowsById[r.grnId] = r;

                var statusText = getStatusText(r);
                var statusClass = getStatusClass(r.statusCode);

                $rows.append(
                    '<button type="button" class="vas-rq-row vas-rq-grid vas-rq-cols" data-grnid="' + escapeHtml(r.grnId) + '">' +
                    '<div class="vas-rq-cell vas-rq-strong" title="' + escapeHtml(r.grnNo) + '">' + escapeHtml(r.grnNo) + '</div>' +
                    '<div class="vas-rq-cell" title="' + escapeHtml(r.supplier) + '">' + escapeHtml(r.supplier) + '</div>' +
                    '<div class="vas-rq-cell" title="' + escapeHtml(r.poNo) + '">' + escapeHtml(r.poNo || "-") + '</div>' +
                    '<div class="vas-rq-cell vas-rq-right vas-rq-emph" title="' + escapeHtml(formatQty(r.receivedQty)) + '">' + escapeHtml(formatQty(r.receivedQty)) + '</div>' +
                    '<div class="vas-rq-cell vas-rq-center" title="' + escapeHtml(statusText) + '"><span class="vas-rq-pill ' + statusClass + '">' + escapeHtml(statusText) + '</span></div>' +
                    '</button>'
                );
            }

            updatePager();
        }

        function updatePager() {
            if ($pageText) {
                $pageText.text(totalPages > 1 ? (pageNo + ' ' + lbl("VAS_088_Of", "of") + ' ' + totalPages) : '');
            }
            if ($pageInfo) {
                if (totalRecords > 0) {
                    var from = (pageNo - 1) * pageSize + 1;
                    var to = Math.min(pageNo * pageSize, totalRecords);
                    $pageInfo.text(lbl("VAS_088_Showing", "Showing") + ' ' + from + '-' + to + ' ' + lbl("VAS_088_Of", "of") + ' ' + totalRecords);
                } else {
                    $pageInfo.text('');
                }
            }
            if ($prevBtn) { $prevBtn.prop('disabled', loading || pageNo <= 1); }
            if ($nextBtn) { $nextBtn.prop('disabled', loading || totalPages <= 1 || pageNo >= totalPages); }
            if ($footer) { $footer.toggleClass('vas-rq-hidden', totalRecords <= 0); }
        }

        function createDialog() {
            $dialog = $(
                '<div class="vas-rq-dialog vas-rq-hidden" role="dialog" aria-modal="true">' +
                '<div class="vas-rq-scrim"></div>' +
                '<div class="vas-rq-modal">' +
                '<div class="vas-rq-modal-head">' +
                '<div class="vas-rq-modal-title-wrap">' +
                '<h3 class="vas-rq-modal-title"></h3>' +
                '<span class="vas-rq-modal-status"></span>' +
                '</div>' +
                '<button type="button" class="vas-rq-modal-close" aria-label="' + escapeHtml(lbl("VAS_088_Close", "Close")) + '">' +
                '<svg viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="1.8" stroke-linecap="round" stroke-linejoin="round"><line x1="18" y1="6" x2="6" y2="18"/><line x1="6" y1="6" x2="18" y2="18"/></svg>' +
                '</button>' +
                '</div>' +
                '<div class="vas-rq-modal-body"></div>' +
                '<div class="vas-rq-modal-busy vas-rq-hidden"><div class="vis-busyindicatorinnerwrap"><i class="vis_widgetloader"></i></div></div>' +
                '</div>' +
                '</div>'
            );

            $dialogBody = $dialog.find('.vas-rq-modal-body');
            $dialogTitle = $dialog.find('.vas-rq-modal-title');
            $dialogStatus = $dialog.find('.vas-rq-modal-status');
            $dialogBusy = $dialog.find('.vas-rq-modal-busy');

            $dialog.find('.vas-rq-modal-close').on('click', function () { closeDetail(); });
            $dialog.find('.vas-rq-scrim').on('click', function () { closeDetail(); });
            $(document).on('keydown.vas-rq', function (e) {
                if (e.key === 'Escape' && !$dialog.hasClass('vas-rq-hidden')) { closeDetail(); }
            });

            $('body').append($dialog);
        }

        function field(label, value, strong) {
            var shown = value == null || value === "" ? "-" : value;
            return '<div class="vas-rq-field">' +
                '<div class="vas-rq-field-lbl">' + escapeHtml(label) + '</div>' +
                '<div class="vas-rq-field-val' + (strong ? ' strong' : '') + '" title="' + escapeHtml(shown) + '">' + escapeHtml(shown) + '</div>' +
                '</div>';
        }

        function openReceiptDetail(grnId) {
            var header = rowsById[grnId];
            if (!header || !$dialog) { return; }

            var statusText = getStatusText(header);
            var statusClass = getStatusClass(header.statusCode);

            $dialogTitle.text((header.grnNo || "") + " - " + (header.supplier || ""));
            $dialogStatus.html('<span class="vas-rq-pill ' + statusClass + '">' + escapeHtml(statusText) + '</span>');
            renderReceiptDetail(header, null);

            $dialog.removeClass('vas-rq-hidden');
            $('body').addClass('vas-rq-body-lock');
            showDialogBusy(true);

            $.ajax({
                url: VIS.Application.contextUrl + 'VAS_088_ReceiptQueueWidget/GetReceiptQueueLines',
                type: 'GET',
                cache: false,
                data: { grnId: grnId },
                success: function (res) {
                    var data = parseResponse(res);
                    showDialogBusy(false);
                    renderReceiptDetail(header, data && data.rows ? data.rows : []);
                },
                error: function () {
                    showDialogBusy(false);
                    renderReceiptDetail(header, []);
                }
            });
        }

        function renderReceiptDetail(header, lines) {
            var html =
                '<div class="vas-rq-form">' +
                field(lbl("VAS_088_GRN", "GRN"), header.grnNo, true) +
                field(lbl("VAS_088_Supplier", "Supplier"), header.supplier) +
                field(lbl("VAS_088_LinkedPO", "Linked PO"), header.poNo) +
                field(lbl("VAS_088_ReceivedQty", "Received Qty"), formatQty(header.receivedQty)) +
                field(lbl("VAS_088_By", "By"), header.receivedBy) +
                field(lbl("VAS_088_On", "On"), formatDateTime(header.receivedTime)) +
                '</div>' +
                '<div class="vas-rq-lines-title">' + escapeHtml(lbl("VAS_088_ReceivedLines", "Received Lines")) + '</div>' +
                '<div class="vas-rq-lines-wrap">';

            if (lines === null) {
                html += '</div>';
                $dialogBody.html(html);
                return;
            }

            if (!lines || lines.length === 0) {
                html += '<div class="vas-rq-empty">' + escapeHtml(lbl("VAS_088_NoDataAvailable", "No data available")) + '</div></div>';
                $dialogBody.html(html);
                return;
            }

            var body = '';
            for (var i = 0; i < lines.length; i++) {
                var ln = lines[i];
                var lineStatus = getLineStatus(ln.orderedQty, ln.receivedQty);
                body +=
                    '<tr>' +
                    '<td class="vas-rq-l-item" title="' + escapeHtml(ln.itemName) + '"><span class="vas-rq-trunc">' + escapeHtml(ln.itemName) + '</span></td>' +
                    '<td class="vas-rq-l-num" title="' + escapeHtml(formatQty(ln.orderedQty)) + '">' + escapeHtml(formatQty(ln.orderedQty)) + '</td>' +
                    '<td class="vas-rq-l-num" title="' + escapeHtml(formatQty(ln.receivedQty)) + '">' + escapeHtml(formatQty(ln.receivedQty)) + '</td>' +
                    '<td class="vas-rq-l-status" title="' + escapeHtml(lineStatus) + '">' + escapeHtml(lineStatus) + '</td>' +
                    '</tr>';
            }

            html +=
                '<table class="vas-rq-lines-table">' +
                '<thead><tr>' +
                '<th class="vas-rq-l-item">' + escapeHtml(lbl("VAS_088_Item", "Item")) + '</th>' +
                '<th class="vas-rq-l-num">' + escapeHtml(lbl("VAS_088_Ordered", "Ordered")) + '</th>' +
                '<th class="vas-rq-l-num">' + escapeHtml(lbl("VAS_088_Received", "Received")) + '</th>' +
                '<th class="vas-rq-l-status">' + escapeHtml(lbl("VAS_088_Status", "Status")) + '</th>' +
                '</tr></thead><tbody>' + body + '</tbody></table></div>';

            $dialogBody.html(html);
        }

        function closeDetail() {
            if (!$dialog) { return; }
            $dialog.addClass('vas-rq-hidden');
            $('body').removeClass('vas-rq-body-lock');
        }

        this.refreshWidget = function () {
            pageNo = 1;
            loadReceiptQueue(1);
        };

        this.getRoot = function () { return $root; };

        this.disposeComponent = function () {
            $(document).off('keydown.vas-rq');
            $('body').removeClass('vas-rq-body-lock');
            if ($dialog) { $dialog.remove(); $dialog = null; }
            if (rowResizeObserver) { rowResizeObserver.disconnect(); rowResizeObserver = null; }
            $root.remove();
        };
    };

    VAS.VAS_088_ReceiptQueueWidget.prototype.init = function (windowNo, frame) {
        this.frame = frame;
        this.AD_UserHomeWidgetID = frame.widgetInfo.AD_UserHomeWidgetID;
        this.windowNo = windowNo;
        this.Initalize();
        this.frame.getContentGrid().append(this.getRoot());
        ensureDashInlineSizeVar(this.getRoot());
    };

    VAS.VAS_088_ReceiptQueueWidget.prototype.widgetSizeChange = function (height, width) { };

    VAS.VAS_088_ReceiptQueueWidget.prototype.refreshWidget = function () {
        this.refreshWidget();
    };

    VAS.VAS_088_ReceiptQueueWidget.prototype.dispose = function () {
        this.disposeComponent();
        if (this.frame) { this.frame.dispose(); }
        this.frame = null;
    };

})(VAS, jQuery);
