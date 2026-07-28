/**
 * Receiving Actions Widget (Material Receipt / GRN dashboard)
 * Purpose - 3x2 action launcher for receiving against PO, completing GRN
 *           confirmations (review #30), and GRN label print/search flows.
 * Backend - VAS_090_ReceivingActionsWidget/GetOpenPurchaseOrders
 *           VAS_090_ReceivingActionsWidget/GetPurchaseOrderLines
 *           VAS_090_ReceivingActionsWidget/GetWarehouseLocators
 *           VAS_090_ReceivingActionsWidget/CreateGRN
 *           VAS_090_ReceivingActionsWidget/GetGRNConfirmations
 *           VAS_090_ReceivingActionsWidget/GetGRNConfirmationDetail
 *           VAS_090_ReceivingActionsWidget/SaveGRNConfirmationLine
 *           VAS_090_ReceivingActionsWidget/CompleteGRNConfirmation
 *           VAS_090_ReceivingActionsWidget/DisputeGRNConfirmation
 *           VAS_090_ReceivingActionsWidget/SearchGRNLabels
 *           VAS_090_ReceivingActionsWidget/QueueGRNLabelPrint
 * Summary Message Table: see Labels / Message Keys below.
 *
 * Labels / Message Keys
 *  #  | Current Text                    | Message Key
 * ----+---------------------------------+------------------------------
 *  1  | Receiving Actions               | VAS_090_ReceivingActions
 *  2  | Receive Against PO              | VAS_090_ReceiveAgainstPO
 *  3  | Enter received quantities       | VAS_090_EnterReceivedQuantities
 *  4  | Complete GRN Confirmation       | VAS_090_CompleteGRNConfirmation
 *  5  | Review pending confirmations    | VAS_090_ReviewPendingConfirmations
 *  6  | Print GRN Label                 | VAS_090_PrintGRNLabel
 *  7  | Search a GRN to print           | VAS_090_SearchGRNToPrint
 *  8  | Make GRN                        | VAS_090_MakeGRN
 *  9  | Pending GRN Confirmations       | VAS_090_PendingGRNConfirmations
 * 10  | Review Confirmation Line        | VAS_090_ReviewConfirmationLine
 * 11  | Print Label                     | VAS_090_PrintLabel
 * 12  | GRN number or supplier...       | VAS_090_GRNSearchPlaceholder
 * 13  | pending                         | VAS_090_Pending2 ("pending" badge count)
 * 14  | Pending / In Dispute / Completed| VAS_090_Pending / VAS_090_InDispute / VAS_090_Completed
 * 15  | Matched / Short / Reviewed      | VAS_090_Matched / VAS_090_Short / VAS_090_Reviewed
 * 16  | Mark In Dispute                 | VAS_090_MarkInDispute
 * 17  | Save Line                       | VAS_090_SaveLine
 * 18  | Back to pending confirmations   | VAS_090_BackToConfirmations
 * 19  | Confirmation Lines              | VAS_090_ConfirmationLines
 * 20  | This GRN confirmation is completed. | VAS_090_ConfirmationCompleted
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

    VAS.VAS_090_ReceivingActionsWidget = function () {

        this.frame;
        this.windowNo;

        var widgetSelf = this;

        var $root = $('<div class="vas-ra-root">');
        var $busy;
        var $dialog, $dialogBody, $dialogTitle, $dialogStatus, $dialogBusy;

        var purchaseOrders = [];
        var purchaseOrdersById = {};
        var currentPO = null;
        var currentLines = [];
        var poPageNo = 1;
        var poSearchText = "";
        var poSearchTimer = null;
        /* Review #49: receive-form header data and line-level locators. */
        /* Document Type is now editable: the receive form offers every Material
           Receipt type of the PO's org and the chosen one drives the created GRN. */
        var poDocTypes = [];
        var poSelectedDocTypeId = 0;
        var poWarehouses = [];
        var poSelectedWarehouseId = 0;
        var poLocators = [];
        /* The line table shows at most 5 data lines per page (no scrolling);
           typed quantities and locator choices live here so they survive
           page switches. poLineId -> { qty, locatorId }. */
        var RCV_PAGE_SIZE = 5;
        var rcvPageNo = 1;
        var lineEntry = {};
        var poPageSize = 5;
        var poTotalPages = 0;
        var poTotalRecords = 0;
        var poLoading = false;

        /* Review #30: Complete GRN Confirmation flow (list → detail → line). */
        var gcRows = [];
        var gcRowsById = {};
        var gcPageNo = 1;
        var gcPageSize = 6;
        var gcTotalPages = 0;
        var gcTotalRecords = 0;
        var gcPendingCount = 0;
        var gcLoading = false;
        var gcDetail = null;
        var gcLineIndex = -1;

        var labelRows = [];
        var labelRowsById = {};
        var labelSearchText = "";
        var labelSearchTimer = null;
        var labelPageNo = 1;
        var labelPageSize = 5;
        var labelTotalPages = 0;
        var labelTotalRecords = 0;
        var labelLoading = false;

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

        function outerHeight($el) {
            return $el && $el.length ? Math.ceil($el.outerHeight(true)) : 0;
        }

        function measureRowsInDialog(rowSelector, fallbackRowHeight, minRows) {
            if (!$dialog || !$dialog.length || !$dialogBody || !$dialogBody.length || $dialog.hasClass('vas-ra-hidden')) {
                return 0;
            }

            var available = Math.floor($dialogBody.innerHeight());
            available -= outerHeight($dialogBody.find('.vas-ra-note:first'));
            available -= outerHeight($dialogBody.find('.vas-ra-search:first'));
            available -= outerHeight($dialogBody.find('.vas-ra-search-pill:first'));
            available -= outerHeight($dialogBody.find('.vas-ra-modal-pager:first'));
            available -= outerHeight($dialogBody.find('.vas-ra-label-table thead:first'));
            available -= 12;

            var $sample = $dialogBody.find(rowSelector + ':first');
            var rowHeight = $sample.length ? Math.ceil($sample.outerHeight(true)) : fallbackRowHeight;
            if (rowHeight <= 0) { rowHeight = fallbackRowHeight; }

            return Math.max(minRows, Math.floor(available / rowHeight));
        }

        function syncPOPageSize() {
            /* The Receive Against PO list is FIXED at 5 rows per page - the
               same count from the first open onward, no dynamic re-measuring
               (which caused a taller first render plus a scrollbar). */
        }

        function syncLabelPageSize() {
            if (labelLoading || !$dialogBody.find('.vas-ra-label-shell').length) { return; }

            var nextPageSize = measureRowsInDialog('.vas-ra-label-table tbody tr', 40, 3);
            if (!nextPageSize || nextPageSize === labelPageSize) { return; }

            var firstRecord = ((labelPageNo - 1) * labelPageSize) + 1;
            labelPageSize = nextPageSize;
            searchGRNLabels(labelSearchText, Math.max(1, Math.ceil(firstRecord / labelPageSize)));
        }

        function syncGCPageSize() {
            if (gcLoading || gcDetail || !$dialogBody.find('.vas-ra-gc-list').length) { return; }

            var nextPageSize = measureRowsInDialog('.vas-ra-gc-row', 54, 3);
            if (!nextPageSize || nextPageSize === gcPageSize) { return; }

            var firstRecord = ((gcPageNo - 1) * gcPageSize) + 1;
            gcPageSize = nextPageSize;
            loadGRNConfirmations(Math.max(1, Math.ceil(firstRecord / gcPageSize)));
        }

        function syncOpenModalPageSize() {
            syncPOPageSize();
            syncGCPageSize();
            syncLabelPageSize();
        }

        function showBusy(show) {
            if (!$busy) { return; }
            $busy.toggleClass('vas-ra-hidden', !show);
        }

        function showDialogBusy(show) {
            if (!$dialogBusy) { return; }
            $dialogBusy.toggleClass('vas-ra-hidden', !show);
        }

        function icon(name) {
            if (name === "clipboard") {
                return '<svg viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="1.8" stroke-linecap="round" stroke-linejoin="round"><path d="M9 4h6"/><path d="M9 2h6v4H9z"/><path d="M8 4H6a2 2 0 0 0-2 2v14a2 2 0 0 0 2 2h12a2 2 0 0 0 2-2V6a2 2 0 0 0-2-2h-2"/></svg>';
            }
            if (name === "receive") {
                return '<svg viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="1.8" stroke-linecap="round" stroke-linejoin="round"><path d="M3 7l9-4 9 4-9 4-9-4z"/><path d="M3 7v10l9 4 9-4V7"/><path d="M12 11v10"/><path d="M17 3v5"/><path d="M14.5 5.5H19.5"/></svg>';
            }
            if (name === "shield") {
                return '<svg viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="1.8" stroke-linecap="round" stroke-linejoin="round"><path d="M12 22s8-4 8-10V5l-8-3-8 3v7c0 6 8 10 8 10z"/><path d="M9 12l2 2 4-5"/></svg>';
            }
            if (name === "printer") {
                return '<svg viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="1.8" stroke-linecap="round" stroke-linejoin="round"><path d="M6 9V2h12v7"/><path d="M6 18H4a2 2 0 0 1-2-2v-5a2 2 0 0 1 2-2h16a2 2 0 0 1 2 2v5a2 2 0 0 1-2 2h-2"/><path d="M6 14h12v8H6z"/><path d="M18 13h.01"/></svg>';
            }
            if (name === "search") {
                return '<svg viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="1.8" stroke-linecap="round" stroke-linejoin="round"><circle cx="11" cy="11" r="7"/><path d="M21 21l-4.35-4.35"/></svg>';
            }
            if (name === "file") {
                return '<svg viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="1.8" stroke-linecap="round" stroke-linejoin="round"><path d="M14 2H6a2 2 0 0 0-2 2v16a2 2 0 0 0 2 2h12a2 2 0 0 0 2-2V8z"/><path d="M14 2v6h6"/></svg>';
            }
            if (name === "package") {
                return '<svg viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="1.8" stroke-linecap="round" stroke-linejoin="round"><path d="M3 7l9-4 9 4-9 4-9-4z"/><path d="M3 7v10l9 4 9-4V7"/><path d="M12 11v10"/></svg>';
            }
            if (name === "scale") {
                return '<svg viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="1.8" stroke-linecap="round" stroke-linejoin="round"><path d="M12 3v18"/><path d="M5 7h14"/><path d="M6 7l-3 6h6L6 7z"/><path d="M18 7l-3 6h6l-3-6z"/></svg>';
            }
            if (name === "calendar") {
                return '<svg viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="1.8" stroke-linecap="round" stroke-linejoin="round"><rect x="3" y="4" width="18" height="18" rx="2"/><path d="M16 2v4"/><path d="M8 2v4"/><path d="M3 10h18"/></svg>';
            }
            if (name === "edit") {
                return '<svg viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="1.8" stroke-linecap="round" stroke-linejoin="round"><path d="M12 20h9"/><path d="M16.5 3.5a2.1 2.1 0 0 1 3 3L7 19l-4 1 1-4 12.5-12.5z"/></svg>';
            }
            if (name === "check") {
                return '<svg viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2.1" stroke-linecap="round" stroke-linejoin="round"><path d="M20 6L9 17l-5-5"/></svg>';
            }
            if (name === "chevL") {
                return '<svg viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2" stroke-linecap="round" stroke-linejoin="round"><polyline points="15 18 9 12 15 6"/></svg>';
            }
            if (name === "chevR") {
                return '<svg viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2" stroke-linecap="round" stroke-linejoin="round"><polyline points="9 18 15 12 9 6"/></svg>';
            }
            if (name === "chevD") {
                return '<svg viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2.1" stroke-linecap="round" stroke-linejoin="round"><polyline points="6 9 12 15 18 9"/></svg>';
            }
            if (name === "kebab") {
                return '<svg viewBox="0 0 24 24" fill="currentColor"><circle cx="12" cy="5" r="1.7"/><circle cx="12" cy="12" r="1.7"/><circle cx="12" cy="19" r="1.7"/></svg>';
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

        function toInputValue(value) {
            var n = Number(value || 0);
            return isFinite(n) ? String(n) : "0";
        }

        function todayYmd() {
            var d = new Date();
            var month = d.getMonth() + 1;
            var day = d.getDate();
            return d.getFullYear() + '-' + (month < 10 ? '0' + month : month) + '-' + (day < 10 ? '0' + day : day);
        }

        function notify(message) {
            if (VIS && VIS.ADialog) {
                VIS.ADialog.info("", true, message, null);
            }
        }

        function fieldHtml(label, value, strong) {
            var shown = value == null || value === "" ? "-" : value;
            return '<div class="vas-ra-field">' +
                '<div class="vas-ra-field-label">' + escapeHtml(label) + '</div>' +
                '<div class="vas-ra-field-value' + (strong ? ' strong' : '') + '" title="' + escapeHtml(shown) + '">' + escapeHtml(shown) + '</div>' +
                '</div>';
        }

        function disabledQAField(iconName, label, value, right) {
            var shown = value == null || value === "" ? "-" : value;
            return '<div class="vas-ra-qc-field disabled">' +
                '<div class="vas-ra-qc-ico">' + icon(iconName) + '</div>' +
                '<div class="vas-ra-qc-main">' +
                '<div class="vas-ra-qc-label">' + escapeHtml(label) + '</div>' +
                '<div class="vas-ra-qc-value' + (right ? ' right' : '') + '" title="' + escapeHtml(shown) + '">' + escapeHtml(shown) + '</div>' +
                '</div>' +
                '</div>';
        }

        function setModal(title, badgeText, badgeClass) {
            $dialogTitle.text(title || "");
            if (badgeText) {
                $dialogStatus.html('<span class="vas-ra-pill ' + escapeHtml(badgeClass || "info") + '">' + escapeHtml(badgeText) + '</span>');
            } else {
                $dialogStatus.empty();
            }
        }

        function openDialog() {
            $dialog.removeClass('vas-ra-hidden');
            $('body').addClass('vas-ra-body-lock');
        }

        function closeDialog() {
            if (!$dialog) { return; }
            $dialog.addClass('vas-ra-hidden');
            $('body').removeClass('vas-ra-body-lock');
            currentPO = null;
            currentLines = [];
            gcDetail = null;
            gcLineIndex = -1;
            showDialogBusy(false);
        }

        this.Initalize = function () {
            createWidget();
            createDialog();
        };

        function createWidget() {
            var $card = $('<div class="vas-ra-card vas-widget-bg">');
            var $header = $(
                '<div class="vas-ra-head">' +
                '<span class="vas-ra-ico">' + icon("clipboard") + '</span>' +
                '<div class="vas-ra-titles">' +
                '<div class="vas-ra-title">' + escapeHtml(lbl("VAS_090_ReceivingActions", "Receiving Actions")) + '</div>' +
                '</div>' +
                '</div>'
            );

            var $actions = $(
                '<div class="vas-ra-actions">' +
                '<button type="button" class="vas-ra-action-btn" data-action="receive">' +
                '<span class="vas-ra-action-ico">' + icon("receive") + '</span>' +
                '<span class="vas-ra-action-main"><span class="vas-ra-action-title">' + escapeHtml(lbl("VAS_090_ReceiveAgainstPO", "Receive Against PO")) + '</span><span class="vas-ra-action-sub">' + escapeHtml(lbl("VAS_090_EnterReceivedQuantities", "Enter received quantities")) + '</span></span>' +
                '</button>' +
                '<button type="button" class="vas-ra-action-btn" data-action="grnconfirm">' +
                '<span class="vas-ra-action-ico">' + icon("shield") + '</span>' +
                '<span class="vas-ra-action-main"><span class="vas-ra-action-title">' + escapeHtml(lbl("VAS_090_CompleteGRNConfirmation", "Complete GRN Confirmation")) + '</span><span class="vas-ra-action-sub">' + escapeHtml(lbl("VAS_090_ReviewPendingConfirmations", "Review pending confirmations")) + '</span></span>' +
                '</button>' +
                '<button type="button" class="vas-ra-action-btn" data-action="print">' +
                '<span class="vas-ra-action-ico">' + icon("printer") + '</span>' +
                '<span class="vas-ra-action-main"><span class="vas-ra-action-title">' + escapeHtml(lbl("VAS_090_PrintGRNLabel", "Print GRN Label")) + '</span><span class="vas-ra-action-sub">' + escapeHtml(lbl("VAS_090_SearchGRNToPrint", "Search a GRN to print")) + '</span></span>' +
                '</button>' +
                '</div>'
            );

            $card.append($header).append($actions);
            $root.append($card);

            $busy = $('<div class="vas-ra-busy vas-ra-hidden"><div class="vis-busyindicatorinnerwrap"><i class="vis_widgetloader"></i></div></div>');
            $root.append($busy);

            $actions.on('click', '.vas-ra-action-btn', function () {
                var action = $(this).data('action');
                if (action === 'receive') { openReceiveAgainstPOModal(); }
                if (action === 'grnconfirm') { openGRNConfirmModal(); }
                if (action === 'print') { openPrintGRNLabelModal(); }
            });
        }

        function createDialog() {
            $dialog = $(
                '<div class="vas-ra-dialog vas-ra-hidden" role="dialog" aria-modal="true">' +
                '<div class="vas-ra-scrim"></div>' +
                '<div class="vas-ra-modal">' +
                '<div class="vas-ra-modal-head">' +
                '<div class="vas-ra-modal-title-wrap">' +
                '<h3 class="vas-ra-modal-title"></h3>' +
                '<span class="vas-ra-modal-status"></span>' +
                '</div>' +
                '<button type="button" class="vas-ra-modal-close" aria-label="' + escapeHtml(lbl("VAS_090_Close", "Close")) + '">' +
                '<svg viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="1.8" stroke-linecap="round" stroke-linejoin="round"><line x1="18" y1="6" x2="6" y2="18"/><line x1="6" y1="6" x2="18" y2="18"/></svg>' +
                '</button>' +
                '</div>' +
                '<div class="vas-ra-modal-body"></div>' +
                '<div class="vas-ra-modal-busy vas-ra-hidden"><div class="vis-busyindicatorinnerwrap"><i class="vis_widgetloader"></i></div></div>' +
                '</div>' +
                '</div>'
            );

            $dialogBody = $dialog.find('.vas-ra-modal-body');
            $dialogTitle = $dialog.find('.vas-ra-modal-title');
            $dialogStatus = $dialog.find('.vas-ra-modal-status');
            $dialogBusy = $dialog.find('.vas-ra-modal-busy');

            $dialog.find('.vas-ra-modal-close').on('click', closeDialog);
            $dialog.find('.vas-ra-scrim').on('click', closeDialog);
            $dialog.on('click', '.vas-ra-po-row', function () { selectPurchaseOrder($(this).data('poid')); });
            $dialog.on('click', '.vas-ra-po-prev', function () { if (poPageNo > 1) { loadOpenPOLines(poPageNo - 1); } });
            $dialog.on('click', '.vas-ra-po-next', function () { if (poPageNo < poTotalPages) { loadOpenPOLines(poPageNo + 1); } });
            $dialog.on('click', '.vas-ra-back-po', function () { currentPO = null; currentLines = []; renderReceiveAgainstPO(); });
            $dialog.on('change', '.vas-ra-doctype-select', function () {
                poSelectedDocTypeId = Number($(this).val() || 0);
            });
            $dialog.on('change', '.vas-ra-wh-select', function () {
                poSelectedWarehouseId = Number($(this).val() || 0);
                /* Another warehouse means another locator list: previously
                   chosen locators no longer apply. */
                for (var key in lineEntry) {
                    if (lineEntry.hasOwnProperty(key)) { lineEntry[key].locatorId = 0; }
                }
                loadWarehouseLocators(poSelectedWarehouseId);
            });
            $dialog.on('input', '.vas-ra-rcv-input', function () {
                var id = Number($(this).data('polineid'));
                if (!lineEntry[id]) { lineEntry[id] = {}; }
                lineEntry[id].qty = String($(this).val() || "");
            });
            $dialog.on('change', '.vas-ra-loc-select', function () {
                var id = Number($(this).data('polineid'));
                if (!lineEntry[id]) { lineEntry[id] = {}; }
                lineEntry[id].locatorId = Number($(this).val() || 0);
            });
            $dialog.on('click', '.vas-ra-rcv-prev', function () { if (rcvPageNo > 1) { rcvPageNo--; renderReceiveLines(); } });
            $dialog.on('click', '.vas-ra-rcv-next', function () { rcvPageNo++; renderReceiveLines(); });
            $dialog.on('click', '.vas-ra-make-grn', makeGRN);
            $dialog.on('click', '.vas-ra-gc-row', function () { openGRNConfirmationDetail(Number($(this).data('gcid'))); });
            $dialog.on('click', '.vas-ra-gc-prev', function () { if (gcPageNo > 1) { loadGRNConfirmations(gcPageNo - 1); } });
            $dialog.on('click', '.vas-ra-gc-next', function () { if (gcPageNo < gcTotalPages) { loadGRNConfirmations(gcPageNo + 1); } });
            $dialog.on('click', '.vas-ra-gc-back-list', function () { gcDetail = null; gcLineIndex = -1; renderGRNConfirmList(); });
            $dialog.on('click', '.vas-ra-gc-line-row', function () { openGRNConfirmationLine(Number($(this).data('lineidx'))); });
            $dialog.on('click', '.vas-ra-gc-back-detail', function () { gcLineIndex = -1; openGRNConfirmationDetail(gcDetail ? gcDetail.header.confirmId : 0); });
            $dialog.on('click', '.vas-ra-gc-complete', completeGRNConfirmation);
            $dialog.on('click', '.vas-ra-gc-dispute', disputeGRNConfirmation);
            $dialog.on('click', '.vas-ra-gc-save-line', saveGRNConfirmationLine);
            $dialog.on('input', '.vas-ra-gc-qty', paintGRNConfirmDifference);
            $dialog.on('input', '.vas-ra-label-search', function () {
                var searchText = $(this).val();
                clearTimeout(labelSearchTimer);
                labelSearchTimer = setTimeout(function () {
                    searchGRNLabels(searchText, 1);
                }, 250);
            });
            $dialog.on('input', '.vas-ra-po-search', function () {
                var searchText = $(this).val();
                clearTimeout(poSearchTimer);
                poSearchTimer = setTimeout(function () {
                    poSearchText = searchText;
                    loadOpenPOLines(1);
                }, 250);
            });
            $dialog.on('click', '.vas-ra-label-row', function () { queueGRNLabelPrint($(this).data('grnid')); });
            $dialog.on('click', '.vas-ra-label-prev', function () { if (labelPageNo > 1) { searchGRNLabels(labelSearchText, labelPageNo - 1); } });
            $dialog.on('click', '.vas-ra-label-next', function () { if (labelPageNo < labelTotalPages) { searchGRNLabels(labelSearchText, labelPageNo + 1); } });
            $(document).on('keydown.vas-ra', function (e) {
                if (e.key === 'Escape' && !$dialog.hasClass('vas-ra-hidden')) { closeDialog(); }
            });
            $(window).on('resize.vas-ra', syncOpenModalPageSize);

            $('body').append($dialog);
        }

        function openReceiveAgainstPOModal() {
            currentPO = null;
            currentLines = [];
            poSearchText = "";
            setModal(lbl("VAS_090_ReceiveAgainstPO", "Receive Against PO"), "", "");
            $dialogBody.html('<div class="vas-ra-empty">' + escapeHtml(lbl("VAS_090_Loading", "Loading...")) + '</div>');
            openDialog();
            loadOpenPOLines(1);
        }

        function loadOpenPOLines(page) {
            poLoading = true;
            showDialogBusy(true);
            $.ajax({
                url: VIS.Application.contextUrl + 'VAS_090_ReceivingActionsWidget/GetOpenPurchaseOrders',
                type: 'GET',
                cache: false,
                data: { pageNo: page, pageSize: poPageSize, searchText: poSearchText },
                success: function (res) {
                    var data = parseResponse(res);
                    showDialogBusy(false);
                    if (!data || data.error) {
                        poLoading = false;
                        $dialogBody.html('<div class="vas-ra-error">' + escapeHtml(data && data.error ? data.error : lbl("VAS_090_NoDataAvailable", "No data available")) + '</div>');
                        return;
                    }

                    purchaseOrders = data.rows || [];
                    purchaseOrdersById = {};
                    for (var i = 0; i < purchaseOrders.length; i++) {
                        purchaseOrdersById[purchaseOrders[i].poId] = purchaseOrders[i];
                    }
                    poPageNo = Number(data.pageNo || page);
                    poTotalPages = Number(data.totalPages || 0);
                    poTotalRecords = Number(data.totalRecords || 0);
                    renderReceiveAgainstPO(data);
                    poLoading = false;
                    window.setTimeout(syncPOPageSize, 0);
                },
                error: function () {
                    showDialogBusy(false);
                    poLoading = false;
                    $dialogBody.html('<div class="vas-ra-error">' + escapeHtml(lbl("VAS_090_NoDataAvailable", "No data available")) + '</div>');
                }
            });
        }

        function renderReceiveAgainstPO(data) {
            if (currentPO) {
                renderReceiveLines();
                return;
            }

            var rows = data && data.rows ? data.rows : purchaseOrders;
            /* Search pill styled after the Product Search widget's bar; the
               old instruction note above the list is removed. */
            var html =
                '<div class="vas-ra-search-pill">' +
                '<span class="vas-ra-search-pill-ic">' + icon("search") + '</span>' +
                '<input class="vas-ra-po-search" type="text" placeholder="' + escapeHtml(lbl("VAS_090_POSearchPlaceholder", "PO number or supplier name...")) + '"/>' +
                '</div>' +
                '<div class="vas-ra-po-list">';

            if (!rows || rows.length === 0) {
                html += '<div class="vas-ra-empty">' + escapeHtml(lbl("VAS_090_NoOpenPOs", "No open purchase orders available.")) + '</div>';
            } else {
                for (var i = 0; i < rows.length; i++) {
                    var po = rows[i];
                    /* Review #47 (design reference): card rows like the GRN
                       confirmation list - icon tile, PO number over its
                       supplier, line count over a status pill, chevron. */
                    /* No "-" placeholders in the meta line: the dock joins in
                       only when it carries a real value. */
                    var dockName = (po.dockName && po.dockName !== "-") ? po.dockName : "";
                    var meta = (dockName ? dockName + " · " : "") + (po.supplier || "");
                    var openLineCount = Number(po.openLineCount || 0);
                    var lineText = openLineCount + ' ' + (openLineCount === 1 ? lbl("VAS_090_Line", "line") : lbl("VAS_090_Lines2", "lines"));
                    html +=
                        '<button type="button" class="vas-ra-po-row" data-poid="' + escapeHtml(po.poId) + '">' +
                        '<span class="vas-ra-gc-ic">' + icon("file") + '</span>' +
                        '<span class="vas-ra-gc-main">' +
                        '<span class="vas-ra-gc-ref" title="' + escapeHtml(po.poNo || "-") + '">' + escapeHtml(po.poNo || "-") + '</span>' +
                        '<span class="vas-ra-gc-meta" title="' + escapeHtml(meta) + '">' + escapeHtml(meta) + '</span>' +
                        '</span>' +
                        '<span class="vas-ra-gc-side">' +
                        '<span class="vas-ra-gc-lines">' + escapeHtml(lineText) + '</span>' +
                        '<span class="vas-ra-pill info">' + escapeHtml(lbl("VAS_090_Pending", "Pending")) + '</span>' +
                        '</span>' +
                        '<span class="vas-ra-gc-chev">' + icon("chevR") + '</span>' +
                        '</button>';
                }
            }

            html += '</div>' + receivePagerHtml();
            $dialogBody.html(html);

            /* Keep the typed text (and caret at the end) across list re-renders. */
            var $poSearch = $dialogBody.find('.vas-ra-po-search');
            if ($poSearch.val() !== poSearchText) { $poSearch.val(poSearchText); }
            if (poSearchText) {
                var el = $poSearch[0];
                $poSearch.trigger('focus');
                if (el && el.setSelectionRange) { el.setSelectionRange(el.value.length, el.value.length); }
            }
        }

        function receivePagerHtml() {
            if (poTotalPages <= 1) { return ""; }
            var from = (poPageNo - 1) * poPageSize + 1;
            var to = Math.min(poPageNo * poPageSize, poTotalRecords);
            return '<div class="vas-ra-modal-pager">' +
                '<span>' + escapeHtml(lbl("VAS_090_Showing", "Showing") + ' ' + from + '-' + to + ' ' + lbl("VAS_090_Of", "of") + ' ' + poTotalRecords) + '</span>' +
                '<button type="button" class="vas-ra-pgbtn vas-ra-po-prev"' + (poPageNo <= 1 ? ' disabled' : '') + '>' + icon("chevL") + '</button>' +
                '<span class="vas-ra-pgtext">' + escapeHtml(poPageNo + ' ' + lbl("VAS_090_Of", "of") + ' ' + poTotalPages) + '</span>' +
                '<button type="button" class="vas-ra-pgbtn vas-ra-po-next"' + (poPageNo >= poTotalPages ? ' disabled' : '') + '>' + icon("chevR") + '</button>' +
                '</div>';
        }

        function selectPurchaseOrder(poId) {
            currentPO = purchaseOrdersById[poId];
            if (!currentPO) { return; }

            setModal(currentPO.poNo + " - " + currentPO.supplier, currentPO.poNo, "info");
            showDialogBusy(true);

            $.ajax({
                url: VIS.Application.contextUrl + 'VAS_090_ReceivingActionsWidget/GetPurchaseOrderLines',
                type: 'GET',
                cache: false,
                data: { poId: currentPO.poId },
                success: function (res) {
                    var data = parseResponse(res);
                    showDialogBusy(false);
                    if (!data || data.error) {
                        $dialogBody.html('<div class="vas-ra-error">' + escapeHtml(data && data.error ? data.error : lbl("VAS_090_NoDataAvailable", "No data available")) + '</div>');
                        return;
                    }
                    currentLines = data.rows || [];
                    /* Review #49: header extras - selectable document type and the
                       warehouses of the org the PO was created in. */
                    poDocTypes = data.docTypes || [];
                    poSelectedDocTypeId = Number(data.defaultDocTypeId || 0);
                    if (!poSelectedDocTypeId && poDocTypes.length) {
                        poSelectedDocTypeId = Number(poDocTypes[0].docTypeId);
                    }
                    poWarehouses = data.warehouses || [];
                    poSelectedWarehouseId = Number(data.defaultWarehouseId || 0);
                    if (!poSelectedWarehouseId && poWarehouses.length) {
                        poSelectedWarehouseId = Number(poWarehouses[0].warehouseId);
                    }
                    poLocators = [];
                    rcvPageNo = 1;
                    lineEntry = {};
                    for (var i = 0; i < currentLines.length; i++) {
                        lineEntry[currentLines[i].poLineId] = {
                            qty: toInputValue(currentLines[i].defaultReceivedQty),
                            locatorId: 0
                        };
                    }
                    renderReceiveLines();
                    loadWarehouseLocators(poSelectedWarehouseId);
                },
                error: function () {
                    showDialogBusy(false);
                    $dialogBody.html('<div class="vas-ra-error">' + escapeHtml(lbl("VAS_090_NoDataAvailable", "No data available")) + '</div>');
                }
            });
        }

        function warehouseSelectHtml() {
            var options = "";
            for (var i = 0; i < poWarehouses.length; i++) {
                var warehouse = poWarehouses[i];
                options +=
                    '<option value="' + escapeHtml(warehouse.warehouseId) + '"' +
                    (Number(warehouse.warehouseId) === poSelectedWarehouseId ? ' selected' : '') + '>' +
                    escapeHtml(warehouse.warehouseName || "-") +
                    '</option>';
            }
            return '<select class="vas-ra-field-select vas-ra-wh-select">' + options + '</select>';
        }

        function docTypeSelectHtml() {
            var options = "";
            for (var i = 0; i < poDocTypes.length; i++) {
                var docType = poDocTypes[i];
                options +=
                    '<option value="' + escapeHtml(docType.docTypeId) + '"' +
                    (Number(docType.docTypeId) === poSelectedDocTypeId ? ' selected' : '') + '>' +
                    escapeHtml(docType.docTypeName || "-") +
                    '</option>';
            }
            if (!options) {
                options = '<option value="0">-</option>';
            }
            return '<select class="vas-ra-field-select vas-ra-doctype-select">' + options + '</select>';
        }

        function locatorSelectHtml(poLineId) {
            var chosen = lineEntry[poLineId] ? Number(lineEntry[poLineId].locatorId || 0) : 0;
            var options = "";
            for (var i = 0; i < poLocators.length; i++) {
                var locator = poLocators[i];
                var selected = chosen > 0 ? Number(locator.locatorId) === chosen : locator.isDefault;
                options +=
                    '<option value="' + escapeHtml(locator.locatorId) + '"' + (selected ? ' selected' : '') + '>' +
                    escapeHtml(locator.locatorName || "-") +
                    '</option>';
            }
            if (!options) {
                options = '<option value="0">-</option>';
            }
            return '<select class="vas-ra-line-select vas-ra-loc-select" data-polineid="' + escapeHtml(poLineId) + '">' + options + '</select>';
        }

        /* Review #49: header shows Document Type + Warehouse (of the PO's org)
           instead of Supplier / PO / Dock / Supplier Reference; every line has
           a Locator choice that follows the selected warehouse. */
        function renderReceiveLines() {
            /* Header fields styled like the Quality Control form: icon tile +
               label + value on an underline. Both Document Type and Warehouse are
               editable, so both carry the BLUE (.active) underline - the Quality
               Control form convention for changeable fields. */
            var html =
                '<div class="vas-ra-form">' +
                '<div class="vas-ra-hdr-field active">' +
                '<div class="vas-ra-qc-ico">' + icon("file") + '</div>' +
                '<div class="vas-ra-qc-main">' +
                '<div class="vas-ra-field-label">' + escapeHtml(lbl("VAS_090_DocumentType", "Document Type")) + '</div>' +
                '<div class="vas-ra-field-value">' + docTypeSelectHtml() + '</div>' +
                '</div>' +
                '</div>' +
                '<div class="vas-ra-hdr-field active">' +
                '<div class="vas-ra-qc-ico">' + icon("package") + '</div>' +
                '<div class="vas-ra-qc-main">' +
                '<div class="vas-ra-field-label">' + escapeHtml(lbl("VAS_090_Warehouse", "Warehouse")) + '</div>' +
                '<div class="vas-ra-field-value">' + warehouseSelectHtml() + '</div>' +
                '</div>' +
                '</div>' +
                '</div>' +
                '<div class="vas-ra-lines-title">' + escapeHtml(lbl("VAS_090_ReceivedLines", "Received Lines")) + '</div>' +
                '<div class="vas-ra-receive-table">' +
                '<div class="vas-ra-receive-row head"><span>' + escapeHtml(lbl("VAS_090_Item", "Item")) + '</span><span>' + escapeHtml(lbl("VAS_090_Attribute", "Attribute")) + '</span><span>' + escapeHtml(lbl("VAS_090_POQty", "PO Qty")) + '</span><span>' + escapeHtml(lbl("VAS_090_OpenQty", "Open Qty")) + '</span><span>' + escapeHtml(lbl("VAS_090_Locator", "Locator")) + '</span><span>' + escapeHtml(lbl("VAS_090_Received", "Received")) + '</span></div>';

            var rcvTotalPages = Math.max(1, Math.ceil((currentLines || []).length / RCV_PAGE_SIZE));
            if (rcvPageNo > rcvTotalPages) { rcvPageNo = rcvTotalPages; }
            if (rcvPageNo < 1) { rcvPageNo = 1; }

            if (!currentLines || currentLines.length === 0) {
                html += '<div class="vas-ra-empty">' + escapeHtml(lbl("VAS_090_NoOpenPOLines", "No open PO lines available.")) + '</div>';
            } else {
                /* Max 6 data lines visible - further lines are paged, never
                   scrolled. Typed values persist in lineEntry across pages. */
                var start = (rcvPageNo - 1) * RCV_PAGE_SIZE;
                var end = Math.min(start + RCV_PAGE_SIZE, currentLines.length);
                for (var i = start; i < end; i++) {
                    var line = currentLines[i];
                    /* Empty attribute instances carry dash-only descriptions
                       ("-", "---"): show nothing for those. */
                    var attributeText = (line.attributeName && !/^-+$/.test(String(line.attributeName).trim())) ? line.attributeName : "";
                    var entry = lineEntry[line.poLineId] || {};
                    var qtyValue = entry.qty != null ? entry.qty : toInputValue(line.defaultReceivedQty);
                    html +=
                        '<div class="vas-ra-receive-row">' +
                        '<span class="vas-ra-line-name" title="' + escapeHtml(line.itemName || "-") + '">' + escapeHtml(line.itemName || "-") + '</span>' +
                        '<span class="vas-ra-line-attr" title="' + escapeHtml(attributeText) + '">' + escapeHtml(attributeText) + '</span>' +
                        '<span class="num">' + escapeHtml(formatQtyWithUom(line.poQty, line.uom)) + '</span>' +
                        '<span class="num">' + escapeHtml(formatQtyWithUom(line.openQty, line.uom)) + '</span>' +
                        '<span>' + locatorSelectHtml(line.poLineId) + '</span>' +
                        '<span><input class="vas-ra-rcv-input" type="number" min="0" step="any" value="' + escapeHtml(qtyValue) + '" data-polineid="' + escapeHtml(line.poLineId) + '" data-openqty="' + escapeHtml(toInputValue(line.openQty)) + '"/></span>' +
                        '</div>';
                }
            }

            html += '</div>';

            if (rcvTotalPages > 1) {
                html +=
                    '<div class="vas-ra-modal-pager">' +
                    '<button type="button" class="vas-ra-pgbtn vas-ra-rcv-prev"' + (rcvPageNo <= 1 ? ' disabled' : '') + '>' + icon("chevL") + '</button>' +
                    '<span class="vas-ra-pgtext">' + escapeHtml(rcvPageNo + ' ' + lbl("VAS_090_Of", "of") + ' ' + rcvTotalPages) + '</span>' +
                    '<button type="button" class="vas-ra-pgbtn vas-ra-rcv-next"' + (rcvPageNo >= rcvTotalPages ? ' disabled' : '') + '>' + icon("chevR") + '</button>' +
                    '</div>';
            }

            html +=
                '<div class="vas-ra-action-footer">' +
                '<button type="button" class="vas-ra-secondary vas-ra-back-po">' + icon("chevL") + escapeHtml(lbl("VAS_090_Back", "Back")) + '</button>' +
                '<button type="button" class="vas-ra-primary vas-ra-make-grn">' + icon("check") + escapeHtml(lbl("VAS_090_MakeGRN", "Make GRN")) + '</button>' +
                '</div>';

            $dialogBody.html(html);
        }

        /* Review #49: line locators follow the selected warehouse; changing the
           warehouse reloads its locators into every line's dropdown. */
        function loadWarehouseLocators(warehouseId) {
            if (!warehouseId) {
                poLocators = [];
                refreshLocatorSelects();
                return;
            }

            $.ajax({
                url: VIS.Application.contextUrl + 'VAS_090_ReceivingActionsWidget/GetWarehouseLocators',
                type: 'GET',
                cache: false,
                data: { warehouseId: warehouseId },
                success: function (res) {
                    var data = parseResponse(res);
                    poLocators = (data && !data.error && data.rows) ? data.rows : [];
                    refreshLocatorSelects();
                },
                error: function () {
                    poLocators = [];
                    refreshLocatorSelects();
                }
            });
        }

        function refreshLocatorSelects() {
            $dialogBody.find('.vas-ra-loc-select').each(function () {
                var $select = $(this);
                $select.html($(locatorSelectHtml($select.data('polineid'))).html());
            });
        }

        function makeGRN() {
            if (!currentPO) { return; }

            var lines = [];
            var invalidMessage = "";

            /* The table is paged (max 6 lines visible): quantities and locators
               are read from the lineEntry state, which covers EVERY line of the
               PO, not just the visible page. */
            for (var i = 0; i < currentLines.length; i++) {
                var line = currentLines[i];
                var entry = lineEntry[line.poLineId] || {};
                var raw = String(entry.qty != null ? entry.qty : toInputValue(line.defaultReceivedQty)).replace(/,/g, "");
                var qty = Number(raw);
                var openQty = Number(line.openQty || 0);

                if (!isFinite(qty) || qty < 0) {
                    invalidMessage = lbl("VAS_090_ReceivedQtyInvalid", "Received quantity cannot be negative.");
                    break;
                }

                if (qty > openQty + 0.000001) {
                    invalidMessage = lbl("VAS_090_ReceivedQtyTooHigh", "Received quantity cannot be greater than open quantity.");
                    break;
                }

                if (qty > 0) {
                    lines.push({ poLineId: Number(line.poLineId), receivedQty: qty, locatorId: Number(entry.locatorId || 0) });
                }
            }

            if (invalidMessage) {
                notify(invalidMessage);
                return;
            }

            if (lines.length === 0) {
                notify(lbl("VAS_090_EnterReceivedQuantity", "Enter received quantity for at least one line."));
                return;
            }

            showDialogBusy(true);
            $dialogBody.find('.vas-ra-make-grn').prop('disabled', true);

            $.ajax({
                url: VIS.Application.contextUrl + 'VAS_090_ReceivingActionsWidget/CreateGRN',
                type: 'POST',
                cache: false,
                data: { poId: currentPO.poId, linesJson: JSON.stringify(lines), warehouseId: poSelectedWarehouseId, docTypeId: poSelectedDocTypeId },
                success: function (res) {
                    var data = parseResponse(res);
                    if (!data || data.error) {
                        notify(data && data.error ? data.error : lbl("VAS_090_SaveFailed", "Save failed."));
                        return;
                    }

                    /* The receipt is created AND completed server-side; show the
                       real resulting document status, not a fixed placeholder. */
                    var statusText = data.docStatusName || lbl("VAS_090_Completed", "Completed");
                    setModal(lbl("VAS_090_GRNCreatedFrom", "GRN created from") + " " + currentPO.poNo, statusText, "ok");
                    $dialogBody.html(
                        '<div class="vas-ra-form">' +
                        fieldHtml(lbl("VAS_090_FromPO", "From PO"), currentPO.poNo, true) +
                        fieldHtml(lbl("VAS_090_Supplier", "Supplier"), currentPO.supplier) +
                        fieldHtml(lbl("VAS_090_Lines", "Lines"), String(lines.length)) +
                        fieldHtml(lbl("VAS_090_NewGRNNo", "New GRN #"), data.grnNo || data.shipmentId || "-") +
                        '</div>'
                    );
                    $(document).trigger('VAS_GRNCreated');
                    $(document).trigger('vas-receiving-actions-updated');
                },
                error: function () {
                    notify(lbl("VAS_090_SaveFailed", "Save failed."));
                },
                complete: function () {
                    showDialogBusy(false);
                    if ($dialogBody) { $dialogBody.find('.vas-ra-make-grn').prop('disabled', false); }
                }
            });
        }

        /* ── Review #30: Complete GRN Confirmation flow (list → detail → line) ── */

        function gcStatusPill(status) {
            if (status === "dispute") { return { tone: "warn", text: lbl("VAS_090_InDispute", "In Dispute") }; }
            if (status === "completed") { return { tone: "ok", text: lbl("VAS_090_Completed", "Completed") }; }
            return { tone: "info", text: lbl("VAS_090_Pending", "Pending") };
        }

        function gcLineStatusPill(line) {
            var difference = Number(line.targetQty || 0) - Number(line.confirmedQty || 0) - Number(line.scrappedQty || 0);
            if (difference === 0 && Number(line.scrappedQty || 0) === 0) { return { tone: "ok", text: lbl("VAS_090_Matched", "Matched") }; }
            if (Number(line.confirmedQty || 0) < Number(line.targetQty || 0)) { return { tone: "bad", text: lbl("VAS_090_Short", "Short") }; }
            return { tone: "warn", text: lbl("VAS_090_Reviewed", "Reviewed") };
        }

        function openGRNConfirmModal() {
            gcDetail = null;
            gcLineIndex = -1;
            setModal(lbl("VAS_090_PendingGRNConfirmations", "Pending GRN Confirmations"), "", "");
            $dialogBody.html('<div class="vas-ra-empty">' + escapeHtml(lbl("VAS_090_Loading", "Loading...")) + '</div>');
            openDialog();
            loadGRNConfirmations(1);
        }

        function loadGRNConfirmations(page) {
            gcLoading = true;
            showDialogBusy(true);
            $.ajax({
                url: VIS.Application.contextUrl + 'VAS_090_ReceivingActionsWidget/GetGRNConfirmations',
                type: 'GET',
                cache: false,
                data: { pageNo: page, pageSize: gcPageSize },
                success: function (res) {
                    var data = parseResponse(res);
                    showDialogBusy(false);
                    gcLoading = false;
                    if (!data || data.error) {
                        $dialogBody.html('<div class="vas-ra-error">' + escapeHtml(data && data.error ? data.error : lbl("VAS_090_NoDataAvailable", "No data available")) + '</div>');
                        return;
                    }

                    gcRows = data.rows || [];
                    gcRowsById = {};
                    for (var i = 0; i < gcRows.length; i++) {
                        gcRowsById[gcRows[i].confirmId] = gcRows[i];
                    }
                    gcPageNo = Number(data.pageNo || page);
                    gcTotalPages = Number(data.totalPages || 0);
                    gcTotalRecords = Number(data.totalRecords || 0);
                    gcPendingCount = Number(data.pendingCount || 0);
                    renderGRNConfirmList();
                    window.setTimeout(syncGCPageSize, 0);
                },
                error: function () {
                    showDialogBusy(false);
                    gcLoading = false;
                    $dialogBody.html('<div class="vas-ra-error">' + escapeHtml(lbl("VAS_090_NoDataAvailable", "No data available")) + '</div>');
                }
            });
        }

        function gcPagerHtml() {
            if (gcTotalPages <= 1) { return ""; }
            var from = (gcPageNo - 1) * gcPageSize + 1;
            var to = Math.min(gcPageNo * gcPageSize, gcTotalRecords);
            return '<div class="vas-ra-modal-pager">' +
                '<span>' + escapeHtml(lbl("VAS_090_Showing", "Showing") + ' ' + from + '-' + to + ' ' + lbl("VAS_090_Of", "of") + ' ' + gcTotalRecords) + '</span>' +
                '<button type="button" class="vas-ra-pgbtn vas-ra-gc-prev"' + (gcPageNo <= 1 ? ' disabled' : '') + '>' + icon("chevL") + '</button>' +
                '<span class="vas-ra-pgtext">' + escapeHtml(gcPageNo + ' ' + lbl("VAS_090_Of", "of") + ' ' + gcTotalPages) + '</span>' +
                '<button type="button" class="vas-ra-pgbtn vas-ra-gc-next"' + (gcPageNo >= gcTotalPages ? ' disabled' : '') + '>' + icon("chevR") + '</button>' +
                '</div>';
        }

        function renderGRNConfirmList() {
            setModal(
                lbl("VAS_090_PendingGRNConfirmations", "Pending GRN Confirmations"),
                gcPendingCount + ' ' + lbl("VAS_090_Pending2", "pending"),
                "info"
            );

            var html =
                '<div class="vas-ra-note">' + icon("clipboard") + '<span>' + escapeHtml(lbl("VAS_090_SelectConfirmationNote", "Select a GRN confirmation to review its lines and complete it.")) + '</span></div>' +
                '<div class="vas-ra-gc-list">';

            if (!gcRows || gcRows.length === 0) {
                html += '<div class="vas-ra-empty">' + escapeHtml(lbl("VAS_090_NoConfirmations", "No GRN confirmations available.")) + '</div>';
            } else {
                for (var i = 0; i < gcRows.length; i++) {
                    var row = gcRows[i];
                    var pill = gcStatusPill(row.status);
                    var meta = (row.grnNo || "-") + " · " + (row.supplier || "-");
                    var lineText = row.lineCount + ' ' + (row.lineCount === 1 ? lbl("VAS_090_Line", "line") : lbl("VAS_090_Lines2", "lines"));
                    html +=
                        '<button type="button" class="vas-ra-gc-row" data-gcid="' + escapeHtml(row.confirmId) + '">' +
                        '<span class="vas-ra-gc-ic">' + icon("clipboard") + '</span>' +
                        '<span class="vas-ra-gc-main">' +
                        '<span class="vas-ra-gc-ref" title="' + escapeHtml(row.confirmNo || "-") + '">' + escapeHtml(row.confirmNo || "-") + '</span>' +
                        '<span class="vas-ra-gc-meta" title="' + escapeHtml(meta) + '">' + escapeHtml(meta) + '</span>' +
                        '</span>' +
                        '<span class="vas-ra-gc-side">' +
                        '<span class="vas-ra-gc-lines">' + escapeHtml(lineText) + '</span>' +
                        '<span class="vas-ra-pill ' + pill.tone + '">' + escapeHtml(pill.text) + '</span>' +
                        '</span>' +
                        '<span class="vas-ra-gc-chev">' + icon("chevR") + '</span>' +
                        '</button>';
                }
            }

            html += '</div>' + gcPagerHtml();
            $dialogBody.html(html);
        }

        function openGRNConfirmationDetail(confirmId) {
            if (!confirmId) { return; }

            showDialogBusy(true);
            $.ajax({
                url: VIS.Application.contextUrl + 'VAS_090_ReceivingActionsWidget/GetGRNConfirmationDetail',
                type: 'GET',
                cache: false,
                data: { confirmId: confirmId },
                success: function (res) {
                    var data = parseResponse(res);
                    showDialogBusy(false);
                    if (!data || data.error || !data.header) {
                        $dialogBody.html('<div class="vas-ra-error">' + escapeHtml(data && data.error ? data.error : lbl("VAS_090_NoDataAvailable", "No data available")) + '</div>');
                        return;
                    }

                    gcDetail = data;
                    gcLineIndex = -1;
                    renderGRNConfirmDetail();
                },
                error: function () {
                    showDialogBusy(false);
                    $dialogBody.html('<div class="vas-ra-error">' + escapeHtml(lbl("VAS_090_NoDataAvailable", "No data available")) + '</div>');
                }
            });
        }

        function renderGRNConfirmDetail() {
            if (!gcDetail || !gcDetail.header) { return; }

            var header = gcDetail.header;
            var pill = gcStatusPill(header.status);
            setModal((header.confirmNo || "-") + " - " + (header.grnNo || "-"), pill.text, pill.tone);

            var html =
                '<div class="vas-ra-modal-back vas-ra-gc-back-list">' + icon("chevL") + '<span>' + escapeHtml(lbl("VAS_090_BackToConfirmations", "Back to pending confirmations")) + '</span></div>' +
                '<div class="vas-ra-form">' +
                fieldHtml(lbl("VAS_090_SourceGRN", "Source GRN"), header.grnNo, true) +
                fieldHtml(lbl("VAS_090_Supplier", "Supplier"), header.supplier) +
                fieldHtml(lbl("VAS_090_Warehouse", "Warehouse"), header.warehouseName) +
                fieldHtml(lbl("VAS_090_Date", "Date"), header.docDate) +
                '</div>' +
                '<div class="vas-ra-lines-title">' + escapeHtml(lbl("VAS_090_ConfirmationLines", "Confirmation Lines")) + '</div>' +
                '<div class="vas-ra-gc-list">';

            var lines = gcDetail.lines || [];
            if (lines.length === 0) {
                html += '<div class="vas-ra-empty">' + escapeHtml(lbl("VAS_090_NoConfirmationLines", "No confirmation lines available.")) + '</div>';
            } else {
                for (var i = 0; i < lines.length; i++) {
                    var line = lines[i];
                    var linePill = gcLineStatusPill(line);
                    var lineRef = lbl("VAS_090_Line", "line").charAt(0).toUpperCase() + lbl("VAS_090_Line", "line").slice(1) + ' ' + (line.lineNo || "-");
                    var lineMeta = (line.productName || "-") + " · " + (line.uomName || "-");
                    html +=
                        '<button type="button" class="vas-ra-gc-row vas-ra-gc-line-row" data-lineidx="' + i + '">' +
                        '<span class="vas-ra-gc-ic">' + icon("file") + '</span>' +
                        '<span class="vas-ra-gc-main">' +
                        '<span class="vas-ra-gc-ref" title="' + escapeHtml(lineRef) + '">' + escapeHtml(lineRef) + '</span>' +
                        '<span class="vas-ra-gc-meta" title="' + escapeHtml(lineMeta) + '">' + escapeHtml(lineMeta) + '</span>' +
                        '</span>' +
                        '<span class="vas-ra-gc-side">' +
                        '<span class="vas-ra-gc-lines">' + escapeHtml(formatQty(line.confirmedQty) + ' / ' + formatQty(line.targetQty)) + '</span>' +
                        '<span class="vas-ra-pill ' + linePill.tone + '">' + escapeHtml(linePill.text) + '</span>' +
                        '</span>' +
                        '<span class="vas-ra-gc-chev">' + icon("chevR") + '</span>' +
                        '</button>';
                }
            }

            html += '</div>';

            var isCompleted = header.status === "completed";
            if (isCompleted) {
                html +=
                    '<div class="vas-ra-action-footer">' +
                    '<span class="vas-ra-note vas-ra-gc-done">' + icon("check") + '<span>' + escapeHtml(lbl("VAS_090_ConfirmationCompleted", "This GRN confirmation is completed.")) + '</span></span>' +
                    '</div>';
            } else {
                html +=
                    '<div class="vas-ra-action-footer">' +
                    '<button type="button" class="vas-ra-warn vas-ra-gc-dispute">' + icon("edit") + escapeHtml(lbl("VAS_090_MarkInDispute", "Mark In Dispute")) + '</button>' +
                    '<button type="button" class="vas-ra-primary vas-ra-gc-complete">' + icon("check") + escapeHtml(lbl("VAS_090_CompleteGRNConfirmation", "Complete GRN Confirmation")) + '</button>' +
                    '</div>';
            }

            $dialogBody.html(html);
        }

        function openGRNConfirmationLine(lineIndex) {
            if (!gcDetail || !gcDetail.lines || !gcDetail.lines[lineIndex]) { return; }
            if (gcDetail.header.status === "completed") { return; }

            gcLineIndex = lineIndex;
            var line = gcDetail.lines[lineIndex];

            setModal(lbl("VAS_090_ReviewConfirmationLine", "Review Confirmation Line"), "", "");

            function inputField(iconName, label, value, key, required) {
                var iconCell = iconName ? '<div class="vas-ra-qc-ico">' + icon(iconName) + '</div>' : '';
                return '<div class="vas-ra-qc-field active">' +
                    iconCell +
                    '<div class="vas-ra-qc-main">' +
                    '<div class="vas-ra-qc-label' + (required ? ' req' : '') + '">' + escapeHtml(label) + '</div>' +
                    '<input class="vas-ra-gc-inp vas-ra-gc-qty" type="number" min="0" step="any" data-k="' + key + '" value="' + escapeHtml(toInputValue(value)) + '"/>' +
                    '</div>' +
                    '</div>';
            }

            var lineRefText = (line.lineNo || "-") + " - " + (line.productName || "-");
            var left =
                disabledQAField("file", lbl("VAS_090_GRNLine", "GRN Line"), lineRefText) +
                disabledQAField("package", lbl("VAS_090_AttributeSetInstance", "Attribute Set Instance"), line.attributeSetInstance || "-") +
                inputField("scale", lbl("VAS_090_TargetQuantity", "Target Quantity"), line.targetQty, "target", true) +
                '<div class="vas-ra-qc-field disabled">' +
                '<div class="vas-ra-qc-ico">' + icon("clipboard") + '</div>' +
                '<div class="vas-ra-qc-main">' +
                '<div class="vas-ra-qc-label">' + escapeHtml(lbl("VAS_090_Difference", "Difference")) + '</div>' +
                '<div class="vas-ra-qc-value right vas-ra-gc-diff">-</div>' +
                '</div>' +
                '</div>' +
                inputField("package", lbl("VAS_090_ScrappedQuantity", "Scrapped Quantity"), line.scrappedQty, "scrapped", false);

            var right =
                disabledQAField("shield", lbl("VAS_090_UOM", "UOM"), line.uomName || "-") +
                disabledQAField("search", lbl("VAS_090_ScrapLocator", "Scrap Locator"), line.locatorValue || "-") +
                inputField("edit", lbl("VAS_090_ConfirmedQuantity", "Confirmed Quantity"), line.confirmedQty, "confirmed", true) +
                '<div class="vas-ra-qc-field active note">' +
                '<div class="vas-ra-qc-main">' +
                '<div class="vas-ra-qc-label">' + escapeHtml(lbl("VAS_090_Description", "Description")) + '</div>' +
                '<textarea class="vas-ra-desc" rows="2" placeholder="' + escapeHtml(lbl("VAS_090_AddNoteOptional", "Add a note (optional)")) + '">' + escapeHtml(line.description || "") + '</textarea>' +
                '</div>' +
                '</div>';

            $dialogBody.html(
                '<div class="vas-ra-modal-back vas-ra-gc-back-detail">' + icon("chevL") + '<span>' + escapeHtml(lbl("VAS_090_BackTo", "Back to") + ' ' + (gcDetail.header.confirmNo || "-")) + '</span></div>' +
                '<div class="vas-ra-qc-headline">' + escapeHtml(lbl("VAS_090_ReviewConfirmationLine", "Review Confirmation Line")) + '</div>' +
                '<div class="vas-ra-qc-grid">' +
                '<div class="vas-ra-qc-col">' + left + '</div>' +
                '<div class="vas-ra-qc-col">' + right + '</div>' +
                '</div>' +
                '<div class="vas-ra-action-footer right">' +
                '<button type="button" class="vas-ra-primary vas-ra-gc-save-line">' + icon("check") + escapeHtml(lbl("VAS_090_SaveLine", "Save Line")) + '</button>' +
                '</div>'
            );

            paintGRNConfirmDifference();
        }

        /* Computed field: Difference = Target - Confirmed - Scrapped (same rule
           the model applies on save), repainted as the quantities change. */
        function paintGRNConfirmDifference() {
            var $diff = $dialogBody.find('.vas-ra-gc-diff');
            if (!$diff.length) { return; }

            var target = Number($dialogBody.find('.vas-ra-gc-qty[data-k="target"]').val()) || 0;
            var confirmed = Number($dialogBody.find('.vas-ra-gc-qty[data-k="confirmed"]').val()) || 0;
            var scrapped = Number($dialogBody.find('.vas-ra-gc-qty[data-k="scrapped"]').val()) || 0;
            $diff.text(formatQty(target - confirmed - scrapped));
        }

        function saveGRNConfirmationLine() {
            if (!gcDetail || gcLineIndex < 0 || !gcDetail.lines[gcLineIndex]) { return; }

            var line = gcDetail.lines[gcLineIndex];
            var target = Number($dialogBody.find('.vas-ra-gc-qty[data-k="target"]').val());
            var confirmed = Number($dialogBody.find('.vas-ra-gc-qty[data-k="confirmed"]').val());
            var scrapped = Number($dialogBody.find('.vas-ra-gc-qty[data-k="scrapped"]').val()) || 0;

            if (!isFinite(target) || !isFinite(confirmed) || target < 0 || confirmed < 0 || scrapped < 0) {
                notify(lbl("VAS_090_InvalidQuantity", "Quantities must be zero or positive numbers."));
                return;
            }

            showDialogBusy(true);
            $dialogBody.find('.vas-ra-gc-save-line').prop('disabled', true);

            $.ajax({
                url: VIS.Application.contextUrl + 'VAS_090_ReceivingActionsWidget/SaveGRNConfirmationLine',
                type: 'POST',
                cache: false,
                data: {
                    lineConfirmId: line.lineConfirmId,
                    targetQty: String(target),
                    confirmedQty: String(confirmed),
                    scrappedQty: String(scrapped),
                    description: $dialogBody.find('.vas-ra-desc').val() || ""
                },
                success: function (res) {
                    var data = parseResponse(res);
                    if (!data || data.error) {
                        notify(data && data.error ? data.error : lbl("VAS_090_SaveFailed", "Save failed."));
                        return;
                    }

                    $(document).trigger('vas-receiving-actions-updated');
                    // Back to the detail state with fresh line values/statuses.
                    gcLineIndex = -1;
                    openGRNConfirmationDetail(gcDetail.header.confirmId);
                },
                error: function () {
                    notify(lbl("VAS_090_SaveFailed", "Save failed."));
                },
                complete: function () {
                    showDialogBusy(false);
                    if ($dialogBody) { $dialogBody.find('.vas-ra-gc-save-line').prop('disabled', false); }
                }
            });
        }

        function completeGRNConfirmation() {
            resolveGRNConfirmation('CompleteGRNConfirmation', '.vas-ra-gc-complete');
        }

        function disputeGRNConfirmation() {
            resolveGRNConfirmation('DisputeGRNConfirmation', '.vas-ra-gc-dispute');
        }

        function resolveGRNConfirmation(action, buttonSelector) {
            if (!gcDetail || !gcDetail.header || gcDetail.header.status === "completed") { return; }

            showDialogBusy(true);
            $dialogBody.find(buttonSelector).prop('disabled', true);

            $.ajax({
                url: VIS.Application.contextUrl + 'VAS_090_ReceivingActionsWidget/' + action,
                type: 'POST',
                cache: false,
                data: { confirmId: gcDetail.header.confirmId },
                success: function (res) {
                    var data = parseResponse(res);
                    if (!data || data.error) {
                        notify(data && data.error ? data.error : lbl("VAS_090_SaveFailed", "Save failed."));
                        return;
                    }

                    $(document).trigger('vas-receiving-actions-updated');
                    // Re-open the detail state so status, badge, and the action
                    // row reflect the confirmation's new state.
                    openGRNConfirmationDetail(gcDetail.header.confirmId);
                },
                error: function () {
                    notify(lbl("VAS_090_SaveFailed", "Save failed."));
                },
                complete: function () {
                    showDialogBusy(false);
                    if ($dialogBody) { $dialogBody.find(buttonSelector).prop('disabled', false); }
                }
            });
        }

        function openPrintGRNLabelModal() {
            labelRows = [];
            labelRowsById = {};
            labelSearchText = "";
            labelPageNo = 1;
            labelTotalPages = 0;
            labelTotalRecords = 0;
            setModal(lbl("VAS_090_PrintGRNLabel", "Print GRN Label"), "", "");
            openDialog();
            renderGRNLabelResults([]);
            searchGRNLabels("", 1);
        }

        function searchGRNLabels(searchText, page) {
            labelSearchText = searchText || "";
            labelLoading = true;
            showDialogBusy(true);

            $.ajax({
                url: VIS.Application.contextUrl + 'VAS_090_ReceivingActionsWidget/SearchGRNLabels',
                type: 'GET',
                cache: false,
                data: { searchText: labelSearchText, pageNo: page, pageSize: labelPageSize },
                success: function (res) {
                    var data = parseResponse(res);
                    showDialogBusy(false);
                    if (!data || data.error) {
                        $dialogBody.html('<div class="vas-ra-error">' + escapeHtml(data && data.error ? data.error : lbl("VAS_090_NoDataAvailable", "No data available")) + '</div>');
                        return;
                    }

                    labelRows = data.rows || [];
                    labelRowsById = {};
                    for (var i = 0; i < labelRows.length; i++) {
                        labelRowsById[labelRows[i].grnId] = labelRows[i];
                    }
                    labelPageNo = Number(data.pageNo || page);
                    labelTotalPages = Number(data.totalPages || 0);
                    labelTotalRecords = Number(data.totalRecords || 0);
                    renderGRNLabelResults(labelRows);
                    labelLoading = false;
                    window.setTimeout(syncLabelPageSize, 0);
                },
                error: function () {
                    showDialogBusy(false);
                    labelLoading = false;
                    $dialogBody.html('<div class="vas-ra-error">' + escapeHtml(lbl("VAS_090_NoDataAvailable", "No data available")) + '</div>');
                }
            });
        }

        function renderGRNLabelResults(rows) {
            if ($dialogBody.find('.vas-ra-label-shell').length === 0) {
                $dialogBody.html(
                    '<div class="vas-ra-label-shell">' +
                    '<div class="vas-ra-search-pill">' +
                    '<span class="vas-ra-search-pill-ic">' + icon("search") + '</span>' +
                    '<input class="vas-ra-label-search" type="text" placeholder="' + escapeHtml(lbl("VAS_090_GRNSearchPlaceholder", "GRN number or supplier name...")) + '"/>' +
                    '</div>' +
                    '<table class="vas-ra-label-table">' +
                    '<thead><tr><th>' + escapeHtml(lbl("VAS_090_GRNNo", "GRN #")) + '</th><th>' + escapeHtml(lbl("VAS_090_CustomerSupplier", "Customer / Supplier")) + '</th><th class="r">' + escapeHtml(lbl("VAS_090_Qty", "Qty")) + '</th><th class="r">' + escapeHtml(lbl("VAS_090_Label", "Label")) + '</th></tr></thead>' +
                    '<tbody class="vas-ra-label-rows"></tbody>' +
                    '</table>' +
                    '<div class="vas-ra-label-pager"></div>' +
                    '</div>'
                );
            }

            var $input = $dialogBody.find('.vas-ra-label-search');
            if ($input.val() !== labelSearchText) { $input.val(labelSearchText); }

            var body = "";
            if (!rows || rows.length === 0) {
                body = '<tr><td colspan="4" class="vas-ra-label-empty">' + escapeHtml(lbl("VAS_090_NoGRNsMatch", "No GRN matches that search.")) + '</td></tr>';
            } else {
                for (var i = 0; i < rows.length; i++) {
                    var r = rows[i];
                    body +=
                        '<tr class="vas-ra-label-row" data-grnid="' + escapeHtml(r.grnId) + '">' +
                        '<td class="s" title="' + escapeHtml(r.grnNo || "-") + '">' + escapeHtml(r.grnNo || "-") + '</td>' +
                        '<td title="' + escapeHtml(r.partyName || "-") + '">' + escapeHtml(r.partyName || "-") + '</td>' +
                        '<td class="r">' + escapeHtml(formatQty(r.receivedQty)) + '</td>' +
                        '<td class="r"><span class="vas-ra-pill info">' + escapeHtml(lbl("VAS_090_Print", "Print")) + '</span></td>' +
                        '</tr>';
                }
            }
            $dialogBody.find('.vas-ra-label-rows').html(body);
            $dialogBody.find('.vas-ra-label-pager').html(labelPagerHtml());
        }

        function labelPagerHtml() {
            if (labelTotalPages <= 1) { return ""; }
            /* No "Showing X-Y of Z" info line here - keeps the print popup
               compact so the table never forces a scrollbar. */
            return '<div class="vas-ra-modal-pager">' +
                '<button type="button" class="vas-ra-pgbtn vas-ra-label-prev"' + (labelPageNo <= 1 ? ' disabled' : '') + '>' + icon("chevL") + '</button>' +
                '<span class="vas-ra-pgtext">' + escapeHtml(labelPageNo + ' ' + lbl("VAS_090_Of", "of") + ' ' + labelTotalPages) + '</span>' +
                '<button type="button" class="vas-ra-pgbtn vas-ra-label-next"' + (labelPageNo >= labelTotalPages ? ' disabled' : '') + '>' + icon("chevR") + '</button>' +
                '</div>';
        }

        function queueGRNLabelPrint(grnId) {
            var row = labelRowsById[grnId];
            showDialogBusy(true);

            $.ajax({
                url: VIS.Application.contextUrl + 'VAS_090_ReceivingActionsWidget/QueueGRNLabelPrint',
                type: 'POST',
                cache: false,
                data: { grnId: grnId },
                success: function (res) {
                    var data = parseResponse(res);
                    showDialogBusy(false);
                    if (!data || data.error) {
                        notify(data && data.error ? data.error : lbl("VAS_090_PrintFailed", "Print failed."));
                        return;
                    }

                    $(document).trigger('vas-receiving-actions-updated');

                    /* Review #27: selecting a GRN opens the label PDF straight in the
                       browser's own PDF viewer (VIS.APrint.startPdf -> window.open of
                       the PDF). No second confirmation panel/modal is shown - the
                       search modal is closed so the flow never goes past one modal. */
                    closeDialog();
                    launchGRNLabelPrint(data, grnId);
                },
                error: function () {
                    showDialogBusy(false);
                    notify(lbl("VAS_090_PrintFailed", "Print failed."));
                }
            });
        }

        /* Runs the DTD001_GRNReport process through the framework's print
           format pipeline (JsonData/GeneratePrint), same approach as
           VAS_065_APInvoicePanel's downloadInvoicePDF: the PDF is written to
           TempDownload and opened straight from there. When the process is
           not configured the user gets a clear message instead of a silent
           no-op. */
        function launchGRNLabelPrint(data, grnId) {
            var processId = Number((data && data.AD_Process_ID) || 0);
            var tableId = Number((data && data.AD_Table_ID) || 0);
            var recordId = Number(grnId || 0);

            if (processId <= 0 || tableId <= 0 || recordId <= 0) {
                notify(lbl("VAS_090_PrintProcessNotFound", "Print process is not configured."));
                return;
            }

            var windowNo = widgetSelf && widgetSelf.windowNo ? widgetSelf.windowNo : 0;

            showBusy(true);
            $.ajax({
                url: VIS.Application.contextUrl + 'JsonData/GeneratePrint/',
                dataType: 'json',
                data: {
                    AD_Process_ID: processId,
                    Name: "Print",
                    AD_Table_ID: tableId,
                    Record_ID: recordId,
                    WindowNo: windowNo,
                    filetype: "P",
                    actionOrigin: "W",
                    originName: "GRN Label"
                },
                success: function (raw) {
                    var res = (typeof raw === "string") ? jQuery.parseJSON(raw) : raw;
                    showBusy(false);
                    if (!res) { return; }

                    /* GeneratePrint writes to TempDownload and returns the file name/path. */
                    var file = res.ReportFilePath || res.FilePath || res.FileName || res.fileName || res.path;
                    if (!file) {
                        var errorText = res.ErrorText || (res.ReportProcessInfo && res.ReportProcessInfo.Summary) || lbl("VAS_090_PrintFailed", "Print failed.");
                        notify(errorText);
                        return;
                    }

                    window.open(VIS.Application.contextUrl + file, "_blank");
                },
                error: function () {
                    showBusy(false);
                    notify(lbl("VAS_090_PrintFailed", "Print failed."));
                }
            });
        }

        this.refreshWidget = function () {
            showBusy(false);
        };

        this.getRoot = function () { return $root; };

        this.disposeComponent = function () {
            clearTimeout(labelSearchTimer);
            $(document).off('keydown.vas-ra');
            $('body').removeClass('vas-ra-body-lock');
            if ($dialog) { $dialog.remove(); $dialog = null; }
            $root.remove();
        };
    };

    VAS.VAS_090_ReceivingActionsWidget.prototype.init = function (windowNo, frame) {
        this.frame = frame;
        this.AD_UserHomeWidgetID = frame.widgetInfo.AD_UserHomeWidgetID;
        this.windowNo = windowNo;
        this.Initalize();
        this.frame.getContentGrid().append(this.getRoot());
        ensureDashInlineSizeVar(this.getRoot());
    };

    VAS.VAS_090_ReceivingActionsWidget.prototype.widgetSizeChange = function (height, width) { };

    VAS.VAS_090_ReceivingActionsWidget.prototype.refreshWidget = function () {
        this.refreshWidget();
    };

    VAS.VAS_090_ReceivingActionsWidget.prototype.dispose = function () {
        this.disposeComponent();
        if (this.frame) { this.frame.dispose(); }
        this.frame = null;
    };

})(VAS, jQuery);
