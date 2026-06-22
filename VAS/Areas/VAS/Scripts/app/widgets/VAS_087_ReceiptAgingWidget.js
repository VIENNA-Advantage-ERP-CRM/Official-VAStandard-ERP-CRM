/**
 * Receipt Aging Widget (Material Receipt / GRN dashboard)
 * Purpose - 3x2 glass age-bar chart of completed vendor receipts waiting on
 *           the dock. Bars are scaled in JavaScript from receipt age and open
 *           a detail modal with received lines.
 * Backend - VAS_087_ReceiptAgingWidget/GetReceiptAging
 *           VAS_087_ReceiptAgingWidget/GetReceiptAgingLines
 * Summary Message Table: see Labels / Message Keys below.
 *
 * Labels / Message Keys
 *  #  | Current Text                       | Message Key
 * ----+------------------------------------+---------------------------
 *  1  | Receipt Aging                     | VAS_ReceiptAging
 *  2  | Time on dock awaiting put-away    | VAS_ReceiptAgingSubtitle
 *  3  | receipt awaiting                   | VAS_ReceiptAwaiting
 *  4  | receipts awaiting                  | VAS_ReceiptsAwaiting
 *  5  | oldest                             | VAS_Oldest
 *  6  | Aging                              | VAS_Aging
 *  7  | Quality check                      | VAS_QualityCheck
 *  8  | Count mismatch                     | VAS_CountMismatch
 *  9  | Unloading                          | VAS_Unloading
 * 10  | Receipt Lines                      | VAS_ReceiptLines
 * 11  | Received                           | VAS_Received
 * 12  | Location                           | VAS_Location
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

    VAS.VAS_087_ReceiptAgingWidget = function () {

        this.frame;
        this.windowNo;

        var $root = $('<div class="vas-rag-root">');
        var $summary, $listBody, $busy;
        var $footer, $pageInfo, $pageText, $prevBtn, $nextBtn;
        var $dialog, $dialogBody, $dialogTitle, $dialogStatus, $dialogBusy;

        var rows = [];
        var rowsById = {};
        var pageNo = 1;
        var pageSize = 4;
        var totalPages = 0;
        var totalRecords = 0;
        var oldestReceivedOn = "";
        var loading = false;

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
            if (!$busy) { return; }
            $busy.toggleClass('vas-rag-hidden', !show);
        }

        function showDialogBusy(show) {
            if (!$dialogBusy) { return; }
            $dialogBusy.toggleClass('vas-rag-hidden', !show);
        }

        function icon(name) {
            if (name === "clock") {
                return '<svg viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="1.8" stroke-linecap="round" stroke-linejoin="round"><circle cx="12" cy="12" r="9"/><path d="M12 7v5l3 2"/></svg>';
            }
            if (name === "chevL") {
                return '<svg viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2" stroke-linecap="round" stroke-linejoin="round"><polyline points="15 18 9 12 15 6"/></svg>';
            }
            if (name === "chevR") {
                return '<svg viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2" stroke-linecap="round" stroke-linejoin="round"><polyline points="9 18 15 12 9 6"/></svg>';
            }
            return '<svg viewBox="0 0 24 24"></svg>';
        }

        function formatQty(value) {
            return Number(value || 0).toLocaleString(window.navigator.language, { maximumFractionDigits: 2 });
        }

        function formatQtyWithUom(value, uom) {
            var text = formatQty(value);
            return uom ? text + " " + uom : text;
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

        function ageHours(value) {
            if (!value) { return 0; }
            var d = new Date(value);
            if (isNaN(d.getTime())) { return 0; }
            return Math.max(0, (new Date().getTime() - d.getTime()) / 3600000);
        }

        function ageLabel(hours) {
            if (hours < 24) {
                return Math.floor(hours) + "h";
            }
            return Math.max(1, Math.round(hours / 24)) + "d";
        }

        function ageLongLabel(hours) {
            if (hours < 24) {
                var h = Math.floor(hours);
                return h + " " + (h === 1 ? lbl("VAS_Hour", "hour") : lbl("VAS_Hours", "hours"));
            }

            var days = Math.max(1, Math.round(hours / 24));
            return days + " " + (days === 1 ? lbl("VAS_Day", "day") : lbl("VAS_Days", "days"));
        }

        function ageSeverity(hours) {
            if (hours > 48) { return "bad"; }
            if (hours > 24) { return "warn"; }
            return "info";
        }

        function itemSummary(record) {
            var first = record.firstItemName || "-";
            var lineCount = Number(record.lineCount || 0);
            if (lineCount > 1) {
                return first + " + " + (lineCount - 1) + " " + lbl("VAS_More", "more");
            }
            return first;
        }

        function normalizeStatus(status) {
            var s = String(status || "").toLowerCase();
            if (s === "quality" || s === "quality check") { return "quality"; }
            if (s === "mismatch" || s === "count mismatch") { return "mismatch"; }
            return "unloading";
        }

        function getStatusText(status) {
            switch (normalizeStatus(status)) {
                case "quality": return lbl("VAS_QualityCheck", "Quality check");
                case "mismatch": return lbl("VAS_CountMismatch", "Count mismatch");
                default: return lbl("VAS_Unloading", "Unloading");
            }
        }

        function getStatusClass(status) {
            switch (normalizeStatus(status)) {
                case "quality": return "warn";
                case "mismatch": return "bad";
                default: return "info";
            }
        }

        this.Initalize = function () {
            createWidget();
            createDialog();
            loadPage(1);
        };

        function createWidget() {
            var $card = $('<div class="vas-rag-card vas-widget-bg">');
            var $header = $(
                '<div class="vas-rag-head">' +
                '<span class="vas-rag-ico">' + icon("clock") + '</span>' +
                '<div class="vas-rag-titles">' +
                '<div class="vas-rag-title">' + escapeHtml(lbl("VAS_ReceiptAging", "Receipt Aging")) + '</div>' +
                '<div class="vas-rag-sub">' + escapeHtml(lbl("VAS_ReceiptAgingSubtitle", "Time on dock awaiting put-away")) + '</div>' +
                '</div>' +
                '</div>'
            );

            $summary = $('<div class="vas-rag-summary">' + icon("clock") + '<span></span></div>');
            $listBody = $('<div class="vas-rag-list">');
            $footer = $(
                '<div class="vas-rag-foot vas-rag-hidden">' +
                '<span class="vas-rag-foot-info"></span>' +
                '<div class="vas-rag-pager">' +
                '<button type="button" class="vas-rag-pgbtn vas-rag-prev" aria-label="' + escapeHtml(lbl("VAS_Previous", "Previous")) + '">' + icon("chevL") + '</button>' +
                '<span class="vas-rag-pgtext"></span>' +
                '<button type="button" class="vas-rag-pgbtn vas-rag-next" aria-label="' + escapeHtml(lbl("VAS_Next", "Next")) + '">' + icon("chevR") + '</button>' +
                '</div>' +
                '</div>'
            );

            $pageInfo = $footer.find('.vas-rag-foot-info');
            $pageText = $footer.find('.vas-rag-pgtext');
            $prevBtn = $footer.find('.vas-rag-prev');
            $nextBtn = $footer.find('.vas-rag-next');

            $card.append($header).append($summary).append($listBody).append($footer);
            $root.append($card);

            $busy = $('<div class="vas-rag-busy vas-rag-hidden"><div class="vis-busyindicatorinnerwrap"><i class="vis_widgetloader"></i></div></div>');
            $root.append($busy);

            $listBody.on('click', '.vas-rag-row', function () {
                openReceiptDetail($(this).data('receiptid'));
            });
            $prevBtn.on('click', function () { if (!loading && pageNo > 1) { loadPage(pageNo - 1); } });
            $nextBtn.on('click', function () { if (!loading && pageNo < totalPages) { loadPage(pageNo + 1); } });
        }

        function loadPage(page) {
            loading = true;
            showBusy(true);
            if ($prevBtn) { $prevBtn.prop("disabled", true); }
            if ($nextBtn) { $nextBtn.prop("disabled", true); }

            $.ajax({
                url: VIS.Application.contextUrl + 'VAS_087_ReceiptAgingWidget/GetReceiptAging',
                type: 'GET',
                cache: false,
                data: { pageNo: page, pageSize: pageSize },
                success: function (res) {
                    var data = parseResponse(res);
                    loading = false;
                    showBusy(false);

                    if (!data || data.error) {
                        setEmpty();
                        return;
                    }

                    rows = data.rows || [];
                    rowsById = {};
                    for (var i = 0; i < rows.length; i++) {
                        rowsById[rows[i].receiptId] = rows[i];
                    }

                    pageNo = Number(data.pageNo || page);
                    totalPages = Number(data.totalPages || 0);
                    totalRecords = Number(data.totalRecords || 0);
                    oldestReceivedOn = data.oldestReceivedOn || "";
                    renderRows();
                    updatePager();
                },
                error: function () {
                    loading = false;
                    showBusy(false);
                    setEmpty();
                }
            });
        }

        function setEmpty() {
            rows = [];
            rowsById = {};
            totalRecords = 0;
            totalPages = 0;
            oldestReceivedOn = "";
            renderRows();
            updatePager();
        }

        function renderRows() {
            $listBody.empty();

            var summaryText = totalRecords + " " +
                (totalRecords === 1 ? lbl("VAS_ReceiptAwaiting", "receipt awaiting") : lbl("VAS_ReceiptsAwaiting", "receipts awaiting"));

            if (totalRecords > 0) {
                var oldestHours = ageHours(oldestReceivedOn);
                if (!oldestHours) {
                    for (var s = 0; s < rows.length; s++) {
                        oldestHours = Math.max(oldestHours, ageHours(rows[s].receivedOn));
                    }
                }
                summaryText += " - " + lbl("VAS_Oldest", "oldest") + " " + ageLongLabel(oldestHours);
            }

            $summary.find('span').text(summaryText);

            if (!rows || rows.length === 0) {
                $listBody.append('<div class="vas-rag-empty">' + escapeHtml(lbl("VAS_NoDataAvailable", "No data available")) + '</div>');
                return;
            }

            var maxAgeHours = 1;
            for (var i = 0; i < rows.length; i++) {
                maxAgeHours = Math.max(maxAgeHours, ageHours(rows[i].receivedOn));
            }

            for (var r = 0; r < rows.length; r++) {
                var record = rows[r];
                var hours = ageHours(record.receivedOn);
                var severity = ageSeverity(hours);
                var pct = Math.max(7, Math.round(hours / maxAgeHours * 100));
                var label = (record.grnNo || "-") + " - " + (record.supplier || "-");
                var sub = itemSummary(record) + " - " + formatQtyWithUom(record.totalQty, record.uom);

                $listBody.append(
                    '<button type="button" class="vas-rag-row" data-receiptid="' + escapeHtml(record.receiptId) + '">' +
                    '<div class="vas-rag-row-top">' +
                    '<span class="vas-rag-row-label" title="' + escapeHtml(label) + '">' + escapeHtml(label) + '</span>' +
                    '<span class="vas-rag-age ' + severity + '">' + escapeHtml(ageLabel(hours)) + '</span>' +
                    '</div>' +
                    '<div class="vas-rag-row-sub" title="' + escapeHtml(sub) + '">' + escapeHtml(sub) + '</div>' +
                    '<progress class="vas-rag-track ' + severity + '" max="100" value="' + pct + '" aria-label="' + escapeHtml(ageLongLabel(hours)) + '"></progress>' +
                    '</button>'
                );
            }
        }

        function updatePager() {
            if ($pageText) {
                $pageText.text(totalPages > 1 ? (pageNo + ' ' + lbl("VAS_Of", "of") + ' ' + totalPages) : '');
            }
            if ($pageInfo) {
                if (totalRecords > 0 && totalPages > 1) {
                    var from = (pageNo - 1) * pageSize + 1;
                    var to = Math.min(pageNo * pageSize, totalRecords);
                    $pageInfo.text(lbl("VAS_Showing", "Showing") + ' ' + from + '-' + to + ' ' + lbl("VAS_Of", "of") + ' ' + totalRecords);
                } else {
                    $pageInfo.text('');
                }
            }
            if ($prevBtn) { $prevBtn.prop('disabled', loading || pageNo <= 1); }
            if ($nextBtn) { $nextBtn.prop('disabled', loading || totalPages <= 1 || pageNo >= totalPages); }
            if ($footer) { $footer.toggleClass('vas-rag-hidden', totalPages <= 1); }
        }

        function createDialog() {
            $dialog = $(
                '<div class="vas-rag-dialog vas-rag-hidden" role="dialog" aria-modal="true">' +
                '<div class="vas-rag-scrim"></div>' +
                '<div class="vas-rag-modal">' +
                '<div class="vas-rag-modal-head">' +
                '<div class="vas-rag-modal-title-wrap">' +
                '<h3 class="vas-rag-modal-title"></h3>' +
                '<span class="vas-rag-modal-status"></span>' +
                '</div>' +
                '<button type="button" class="vas-rag-modal-close" aria-label="' + escapeHtml(lbl("VAS_Close", "Close")) + '">' +
                '<svg viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="1.8" stroke-linecap="round" stroke-linejoin="round"><line x1="18" y1="6" x2="6" y2="18"/><line x1="6" y1="6" x2="18" y2="18"/></svg>' +
                '</button>' +
                '</div>' +
                '<div class="vas-rag-modal-body"></div>' +
                '<div class="vas-rag-modal-busy vas-rag-hidden"><div class="vis-busyindicatorinnerwrap"><i class="vis_widgetloader"></i></div></div>' +
                '</div>' +
                '</div>'
            );

            $dialogBody = $dialog.find('.vas-rag-modal-body');
            $dialogTitle = $dialog.find('.vas-rag-modal-title');
            $dialogStatus = $dialog.find('.vas-rag-modal-status');
            $dialogBusy = $dialog.find('.vas-rag-modal-busy');

            $dialog.find('.vas-rag-modal-close').on('click', closeDetail);
            $dialog.find('.vas-rag-scrim').on('click', closeDetail);
            $(document).on('keydown.vas-rag', function (e) {
                if (e.key === 'Escape' && !$dialog.hasClass('vas-rag-hidden')) { closeDetail(); }
            });

            $('body').append($dialog);
        }

        function field(label, value, strong) {
            var shown = value == null || value === "" ? "-" : value;
            return '<div class="vas-rag-field">' +
                '<div class="vas-rag-field-lbl">' + escapeHtml(label) + '</div>' +
                '<div class="vas-rag-field-val' + (strong ? ' strong' : '') + '" title="' + escapeHtml(shown) + '">' + escapeHtml(shown) + '</div>' +
                '</div>';
        }

        function openReceiptDetail(receiptId) {
            var header = rowsById[receiptId];
            if (!header || !$dialog) { return; }

            var hours = ageHours(header.receivedOn);
            var severity = ageSeverity(hours);

            $dialogTitle.text((header.grnNo || "") + " - " + (header.supplier || ""));
            $dialogStatus.html('<span class="vas-rag-pill ' + severity + '">' + escapeHtml(lbl("VAS_Aging", "Aging") + " " + ageLabel(hours)) + '</span>');
            renderReceiptDetail(header, null);

            $dialog.removeClass('vas-rag-hidden');
            $('body').addClass('vas-rag-body-lock');
            showDialogBusy(true);

            $.ajax({
                url: VIS.Application.contextUrl + 'VAS_087_ReceiptAgingWidget/GetReceiptAgingLines',
                type: 'GET',
                cache: false,
                data: { receiptId: receiptId },
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
                '<div class="vas-rag-form">' +
                field(lbl("VAS_GRN", "GRN"), header.grnNo, true) +
                field(lbl("VAS_LinkedPO", "Linked PO"), header.linkedPoNo) +
                field(lbl("VAS_Supplier", "Supplier"), header.supplier) +
                field(lbl("VAS_QtyReceived", "Qty received"), formatQtyWithUom(header.totalQty, header.uom)) +
                field(lbl("VAS_ReceivedOn", "Received on"), formatDateTime(header.receivedOn)) +
                field(lbl("VAS_Status", "Status"), getStatusText(header.statusCode)) +
                '</div>' +
                '<div class="vas-rag-lines-title">' + escapeHtml(lbl("VAS_ReceiptLines", "Receipt Lines")) + '</div>' +
                '<div class="vas-rag-lines-wrap">';

            if (lines === null) {
                html += '</div>';
                $dialogBody.html(html);
                return;
            }

            if (!lines || lines.length === 0) {
                html += '<div class="vas-rag-empty">' + escapeHtml(lbl("VAS_NoDataAvailable", "No data available")) + '</div></div>';
                $dialogBody.html(html);
                return;
            }

            var body = "";
            for (var i = 0; i < lines.length; i++) {
                var ln = lines[i];
                body +=
                    '<tr>' +
                    '<td class="vas-rag-l-item" title="' + escapeHtml(ln.itemName) + '"><span class="vas-rag-trunc">' + escapeHtml(ln.itemName) + '</span></td>' +
                    '<td class="vas-rag-l-num" title="' + escapeHtml(formatQtyWithUom(ln.receivedQty, ln.uom)) + '">' + escapeHtml(formatQtyWithUom(ln.receivedQty, ln.uom)) + '</td>' +
                    '<td class="vas-rag-l-location" title="' + escapeHtml(ln.locatorCode || "-") + '">' + escapeHtml(ln.locatorCode || "-") + '</td>' +
                    '<td class="vas-rag-l-status" title="' + escapeHtml(ln.status || "-") + '">' + escapeHtml(ln.status || "-") + '</td>' +
                    '</tr>';
            }

            html +=
                '<table class="vas-rag-lines-table">' +
                '<thead><tr>' +
                '<th class="vas-rag-l-item">' + escapeHtml(lbl("VAS_Item", "Item")) + '</th>' +
                '<th class="vas-rag-l-num">' + escapeHtml(lbl("VAS_Received", "Received")) + '</th>' +
                '<th class="vas-rag-l-location">' + escapeHtml(lbl("VAS_Location", "Location")) + '</th>' +
                '<th class="vas-rag-l-status">' + escapeHtml(lbl("VAS_Status", "Status")) + '</th>' +
                '</tr></thead><tbody>' + body + '</tbody></table></div>';

            $dialogBody.html(html);
        }

        function closeDetail() {
            if (!$dialog) { return; }
            $dialog.addClass('vas-rag-hidden');
            $('body').removeClass('vas-rag-body-lock');
        }

        this.refreshWidget = function () {
            pageNo = 1;
            loadPage(1);
        };

        this.getRoot = function () { return $root; };

        this.disposeComponent = function () {
            $(document).off('keydown.vas-rag');
            $('body').removeClass('vas-rag-body-lock');
            if ($dialog) { $dialog.remove(); $dialog = null; }
            $root.remove();
        };
    };

    VAS.VAS_087_ReceiptAgingWidget.prototype.init = function (windowNo, frame) {
        this.frame = frame;
        this.AD_UserHomeWidgetID = frame.widgetInfo.AD_UserHomeWidgetID;
        this.windowNo = windowNo;
        this.Initalize();
        this.frame.getContentGrid().append(this.getRoot());
        ensureDashInlineSizeVar(this.getRoot());
    };

    VAS.VAS_087_ReceiptAgingWidget.prototype.widgetSizeChange = function (height, width) { };

    VAS.VAS_087_ReceiptAgingWidget.prototype.refreshWidget = function () {
        this.refreshWidget();
    };

    VAS.VAS_087_ReceiptAgingWidget.prototype.dispose = function () {
        this.disposeComponent();
        if (this.frame) { this.frame.dispose(); }
        this.frame = null;
    };

})(VAS, jQuery);
