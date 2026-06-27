/**
 * Receiving Actions Widget (Material Receipt / GRN dashboard)
 * Purpose - 3x2 action launcher for receiving against PO, QA inspection,
 *           and GRN label print/search flows.
 * Backend - VAS_090_ReceivingActionsWidget/GetOpenPurchaseOrders
 *           VAS_090_ReceivingActionsWidget/GetPurchaseOrderLines
 *           VAS_NewGRNWidget/CreateGRN
 *           VAS_QAHoldsWidget/GetQAHolds
 *           VAS_QAHoldsWidget/SaveQAResult
 *           VAS_090_ReceivingActionsWidget/SearchGRNLabels
 *           VAS_090_ReceivingActionsWidget/QueueGRNLabelPrint
 * Summary Message Table: see Labels / Message Keys below.
 *
 * Labels / Message Keys
 *  #  | Current Text              | Message Key
 * ----+---------------------------+------------------------------
 *  1  | Receiving Actions         | VAS_090_ReceivingActions
 *  2  | Receive Against PO        | VAS_090_ReceiveAgainstPO
 *  3  | Enter received quantities | VAS_090_EnterReceivedQuantities
 *  4  | Pass QA Inspection        | VAS_090_PassQAInspection
 *  5  | Verify a held receipt     | VAS_090_VerifyHeldReceipt
 *  6  | Print GRN Label           | VAS_090_PrintGRNLabel
 *  7  | Search a GRN to print     | VAS_090_SearchGRNToPrint
 *  8  | Make GRN                  | VAS_090_MakeGRN
 *  9  | Quality Control           | VAS_090_QualityControl
 * 10  | Save QA Result            | VAS_090_SaveQAResult
 * 11  | Print Label               | VAS_090_PrintLabel
 * 12  | GRN number or supplier... | VAS_090_GRNSearchPlaceholder
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

        var $root = $('<div class="vas-ra-root">');
        var $busy;
        var $dialog, $dialogBody, $dialogTitle, $dialogStatus, $dialogBusy;

        var purchaseOrders = [];
        var purchaseOrdersById = {};
        var currentPO = null;
        var currentLines = [];
        var poPageNo = 1;
        var poTotalPages = 0;
        var poTotalRecords = 0;

        var qaRecord = null;
        var qaPageNo = 1;
        var qaTotalPages = 0;
        var qaTotalRecords = 0;

        var labelRows = [];
        var labelRowsById = {};
        var labelSearchText = "";
        var labelSearchTimer = null;
        var labelPageNo = 1;
        var labelTotalPages = 0;
        var labelTotalRecords = 0;

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
            qaRecord = null;
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
                '<button type="button" class="vas-ra-action-btn" data-action="qa">' +
                '<span class="vas-ra-action-ico">' + icon("shield") + '</span>' +
                '<span class="vas-ra-action-main"><span class="vas-ra-action-title">' + escapeHtml(lbl("VAS_090_PassQAInspection", "Pass QA Inspection")) + '</span><span class="vas-ra-action-sub">' + escapeHtml(lbl("VAS_090_VerifyHeldReceipt", "Verify a held receipt")) + '</span></span>' +
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
                if (action === 'qa') { openPassQAModal(); }
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
            $dialog.on('click', '.vas-ra-make-grn', makeGRN);
            $dialog.on('click', '.vas-ra-qa-prev', function () { if (qaPageNo > 1) { loadQAHolds(qaPageNo - 1); } });
            $dialog.on('click', '.vas-ra-qa-next', function () { if (qaPageNo < qaTotalPages) { loadQAHolds(qaPageNo + 1); } });
            $dialog.on('change', '.vas-ra-actual', updateActualTone);
            $dialog.on('click', '.vas-ra-save-qa', saveQAResult);
            $dialog.on('input', '.vas-ra-label-search', function () {
                var searchText = $(this).val();
                clearTimeout(labelSearchTimer);
                labelSearchTimer = setTimeout(function () {
                    searchGRNLabels(searchText, 1);
                }, 250);
            });
            $dialog.on('click', '.vas-ra-label-row', function () { queueGRNLabelPrint($(this).data('grnid')); });
            $dialog.on('click', '.vas-ra-label-prev', function () { if (labelPageNo > 1) { searchGRNLabels(labelSearchText, labelPageNo - 1); } });
            $dialog.on('click', '.vas-ra-label-next', function () { if (labelPageNo < labelTotalPages) { searchGRNLabels(labelSearchText, labelPageNo + 1); } });
            $(document).on('keydown.vas-ra', function (e) {
                if (e.key === 'Escape' && !$dialog.hasClass('vas-ra-hidden')) { closeDialog(); }
            });

            $('body').append($dialog);
        }

        function openReceiveAgainstPOModal() {
            currentPO = null;
            currentLines = [];
            setModal(lbl("VAS_090_ReceiveAgainstPO", "Receive Against PO"), "", "");
            $dialogBody.html('<div class="vas-ra-empty">' + escapeHtml(lbl("VAS_090_Loading", "Loading...")) + '</div>');
            openDialog();
            loadOpenPOLines(1);
        }

        function loadOpenPOLines(page) {
            showDialogBusy(true);
            $.ajax({
                url: VIS.Application.contextUrl + 'VAS_090_ReceivingActionsWidget/GetOpenPurchaseOrders',
                type: 'GET',
                cache: false,
                data: { pageNo: page, pageSize: 8 },
                success: function (res) {
                    var data = parseResponse(res);
                    showDialogBusy(false);
                    if (!data || data.error) {
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
                },
                error: function () {
                    showDialogBusy(false);
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
            var html =
                '<div class="vas-ra-note">' + icon("file") + '<span>' + escapeHtml(lbl("VAS_090_SelectPOThenReceive", "Select an open purchase order, enter received quantities, then create the GRN.")) + '</span></div>' +
                '<div class="vas-ra-po-list">';

            if (!rows || rows.length === 0) {
                html += '<div class="vas-ra-empty">' + escapeHtml(lbl("VAS_090_NoOpenPOs", "No open purchase orders available.")) + '</div>';
            } else {
                for (var i = 0; i < rows.length; i++) {
                    var po = rows[i];
                    var meta = (po.dockName || "-") + " - " + (po.openLineCount || 0) + " " + lbl("VAS_090_OpenLines", "open lines");
                    html +=
                        '<button type="button" class="vas-ra-po-row" data-poid="' + escapeHtml(po.poId) + '">' +
                        '<span class="vas-ra-po-main">' +
                        '<span class="vas-ra-po-no" title="' + escapeHtml(po.poNo || "-") + '">' + escapeHtml(po.poNo || "-") + '</span>' +
                        '<span class="vas-ra-po-supplier" title="' + escapeHtml(po.supplier || "-") + '">' + escapeHtml(po.supplier || "-") + '</span>' +
                        '</span>' +
                        '<span class="vas-ra-po-meta" title="' + escapeHtml(meta) + '">' + escapeHtml(meta) + '</span>' +
                        '</button>';
                }
            }

            html += '</div>' + receivePagerHtml();
            $dialogBody.html(html);
        }

        function receivePagerHtml() {
            if (poTotalPages <= 1) { return ""; }
            var from = (poPageNo - 1) * 8 + 1;
            var to = Math.min(poPageNo * 8, poTotalRecords);
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
                    renderReceiveLines();
                },
                error: function () {
                    showDialogBusy(false);
                    $dialogBody.html('<div class="vas-ra-error">' + escapeHtml(lbl("VAS_090_NoDataAvailable", "No data available")) + '</div>');
                }
            });
        }

        function renderReceiveLines() {
            var html =
                '<div class="vas-ra-form">' +
                fieldHtml(lbl("VAS_090_PO", "PO"), currentPO.poNo, true) +
                fieldHtml(lbl("VAS_090_Supplier", "Supplier"), currentPO.supplier) +
                fieldHtml(lbl("VAS_090_Dock", "Dock"), currentPO.dockName || currentPO.warehouseName) +
                fieldHtml(lbl("VAS_090_SupplierReference", "Supplier Reference"), currentPO.supplierReference) +
                '</div>' +
                '<div class="vas-ra-note">' + icon("file") + '<span>' + escapeHtml(lbl("VAS_090_ReceiveQtyNote", "Enter received quantity against each PO line, then create the GRN.")) + '</span></div>' +
                '<div class="vas-ra-lines-title">' + escapeHtml(lbl("VAS_090_ReceivedLines", "Received Lines")) + '</div>' +
                '<div class="vas-ra-receive-table">' +
                '<div class="vas-ra-receive-row head"><span>' + escapeHtml(lbl("VAS_090_Item", "Item")) + '</span><span>' + escapeHtml(lbl("VAS_090_POQty", "PO Qty")) + '</span><span>' + escapeHtml(lbl("VAS_090_AlreadyReceived", "Already Received")) + '</span><span>' + escapeHtml(lbl("VAS_090_OpenQty", "Open Qty")) + '</span><span>' + escapeHtml(lbl("VAS_090_Received", "Received")) + '</span></div>';

            if (!currentLines || currentLines.length === 0) {
                html += '<div class="vas-ra-empty">' + escapeHtml(lbl("VAS_090_NoOpenPOLines", "No open PO lines available.")) + '</div>';
            } else {
                for (var i = 0; i < currentLines.length; i++) {
                    var line = currentLines[i];
                    html +=
                        '<div class="vas-ra-receive-row">' +
                        '<span class="vas-ra-line-name" title="' + escapeHtml(line.itemName || "-") + '">' + escapeHtml(line.itemName || "-") + '</span>' +
                        '<span class="num">' + escapeHtml(formatQtyWithUom(line.poQty, line.uom)) + '</span>' +
                        '<span class="num">' + escapeHtml(formatQtyWithUom(line.alreadyReceivedQty, line.uom)) + '</span>' +
                        '<span class="num">' + escapeHtml(formatQtyWithUom(line.openQty, line.uom)) + '</span>' +
                        '<span><input class="vas-ra-rcv-input" type="number" min="0" step="any" value="' + escapeHtml(toInputValue(line.defaultReceivedQty)) + '" data-polineid="' + escapeHtml(line.poLineId) + '" data-openqty="' + escapeHtml(toInputValue(line.openQty)) + '"/></span>' +
                        '</div>';
                }
            }

            html +=
                '</div>' +
                '<div class="vas-ra-action-footer">' +
                '<button type="button" class="vas-ra-secondary vas-ra-back-po">' + icon("chevL") + escapeHtml(lbl("VAS_090_Back", "Back")) + '</button>' +
                '<button type="button" class="vas-ra-primary vas-ra-make-grn">' + icon("check") + escapeHtml(lbl("VAS_090_MakeGRN", "Make GRN")) + '</button>' +
                '</div>';

            $dialogBody.html(html);
        }

        function makeGRN() {
            if (!currentPO) { return; }

            var lines = [];
            var invalidMessage = "";

            $dialogBody.find('.vas-ra-rcv-input').each(function () {
                var $input = $(this);
                var raw = String($input.val() || "").replace(/,/g, "");
                var qty = Number(raw);
                var openQty = Number($input.data('openqty') || 0);

                if (!isFinite(qty) || qty < 0) {
                    invalidMessage = lbl("VAS_090_ReceivedQtyInvalid", "Received quantity cannot be negative.");
                    return false;
                }

                if (qty > openQty + 0.000001) {
                    invalidMessage = lbl("VAS_090_ReceivedQtyTooHigh", "Received quantity cannot be greater than open quantity.");
                    return false;
                }

                if (qty > 0) {
                    lines.push({ poLineId: Number($input.data('polineid')), receivedQty: qty });
                }
            });

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
                url: VIS.Application.contextUrl + 'VAS_NewGRNWidget/CreateGRN',
                type: 'POST',
                cache: false,
                data: { poId: currentPO.poId, linesJson: JSON.stringify(lines) },
                success: function (res) {
                    var data = parseResponse(res);
                    if (!data || data.error) {
                        notify(data && data.error ? data.error : lbl("VAS_090_SaveFailed", "Save failed."));
                        return;
                    }

                    setModal(lbl("VAS_090_GRNCreatedFrom", "GRN created from") + " " + currentPO.poNo, lbl("VAS_090_Unloading", "Unloading"), "info");
                    $dialogBody.html(
                        '<div class="vas-ra-form">' +
                        fieldHtml(lbl("VAS_090_FromPO", "From PO"), currentPO.poNo, true) +
                        fieldHtml(lbl("VAS_090_Supplier", "Supplier"), currentPO.supplier) +
                        fieldHtml(lbl("VAS_090_Lines", "Lines"), String(lines.length)) +
                        fieldHtml(lbl("VAS_090_NewGRNNo", "New GRN #"), data.grnNo || data.shipmentId || "-") +
                        fieldHtml(lbl("VAS_090_Dock", "Dock"), currentPO.dockName || currentPO.warehouseName) +
                        fieldHtml(lbl("VAS_090_Status", "Status"), lbl("VAS_090_Unloading", "Unloading")) +
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

        function openPassQAModal() {
            qaRecord = null;
            setModal(lbl("VAS_090_QualityControl", "Quality Control"), lbl("VAS_090_QualityCheck", "Quality check"), "warn");
            $dialogBody.html('<div class="vas-ra-empty">' + escapeHtml(lbl("VAS_090_Loading", "Loading...")) + '</div>');
            openDialog();
            loadQAHolds(1);
        }

        function loadQAHolds(page) {
            showDialogBusy(true);
            $.ajax({
                url: VIS.Application.contextUrl + 'VAS_QAHoldsWidget/GetQAHolds',
                type: 'GET',
                cache: false,
                data: { pageNo: page, pageSize: 1 },
                success: function (res) {
                    var data = parseResponse(res);
                    showDialogBusy(false);
                    if (!data || data.error) {
                        $dialogBody.html('<div class="vas-ra-error">' + escapeHtml(data && data.error ? data.error : lbl("VAS_090_NoDataAvailable", "No data available")) + '</div>');
                        return;
                    }

                    qaPageNo = Number(data.pageNo || page);
                    qaTotalPages = Number(data.totalPages || 0);
                    qaTotalRecords = Number(data.totalRecords || 0);
                    qaRecord = data.rows && data.rows.length ? data.rows[0] : null;
                    renderQAModal(qaRecord, data.message || "");
                },
                error: function () {
                    showDialogBusy(false);
                    $dialogBody.html('<div class="vas-ra-error">' + escapeHtml(lbl("VAS_090_NoDataAvailable", "No data available")) + '</div>');
                }
            });
        }

        function lineReference(record) {
            if (record.confirmationNo) { return record.confirmationNo; }
            var line = record.lineNo ? record.lineNo + " - " : "";
            return line + (record.itemName || record.productName || "-") + " - " + (record.grnNo || "-");
        }

        function renderQAModal(record, message) {
            if (!record) {
                $dialogBody.html('<div class="vas-ra-empty">' + escapeHtml(message || lbl("VAS_090_NoQAHolds", "No QA holds available.")) + '</div>');
                return;
            }

            var actual = record.actualValue || "";
            var qaDate = record.qaQcDate || todayYmd();
            var saveDisabled = record.qaRecordId > 0 ? "" : " disabled";
            var saveTitle = record.qaRecordId > 0 ? "" : ' title="' + escapeHtml(lbl("VAS_090_QARecordMissing", "QA inspection record is missing.")) + '"';

            var left =
                disabledQAField("file", lbl("VAS_090_GRNConfirmationLine", "GRN Confirmation Line"), lineReference(record)) +
                disabledQAField("scale", lbl("VAS_090_QuantityToVerify", "Quantity To Verify"), formatQty(record.quantityToVerify || record.heldQty), true) +
                disabledQAField("clipboard", lbl("VAS_090_TestParameter", "Test Parameter"), record.testParameter || record.testParameterId || "-") +
                disabledQAField("edit", lbl("VAS_090_AcceptableValue", "Acceptable Value"), record.acceptableValue || record.acceptableValueId || "-") +
                '<div class="vas-ra-qc-field active">' +
                '<div class="vas-ra-qc-ico">' + icon("edit") + '</div>' +
                '<div class="vas-ra-qc-main">' +
                '<div class="vas-ra-qc-label req">' + escapeHtml(lbl("VAS_090_ActualValue", "Actual Value")) + '</div>' +
                '<div class="vas-ra-select-wrap">' +
                '<select class="vas-ra-actual">' +
                '<option value="">' + escapeHtml(lbl("VAS_090_SelectResult", "Select result...")) + '</option>' +
                '<option value="Working Fine"' + (actual === "Working Fine" ? " selected" : "") + '>' + escapeHtml(lbl("VAS_090_WorkingFine", "Working Fine")) + '</option>' +
                '<option value="Not Satisfactory"' + (actual === "Not Satisfactory" ? " selected" : "") + '>' + escapeHtml(lbl("VAS_090_NotSatisfactory", "Not Satisfactory")) + '</option>' +
                '</select>' +
                '<span class="vas-ra-select-arr">' + icon("chevD") + '</span>' +
                '</div>' +
                '</div>' +
                '<button type="button" class="vas-ra-kebab" aria-hidden="true" tabindex="-1">' + icon("kebab") + '</button>' +
                '</div>';

            var right =
                disabledQAField("package", lbl("VAS_090_Product", "Product"), record.productName || record.itemName) +
                '<div class="vas-ra-qc-field active">' +
                '<div class="vas-ra-qc-ico">' + icon("calendar") + '</div>' +
                '<div class="vas-ra-qc-main">' +
                '<div class="vas-ra-qc-label">' + escapeHtml(lbl("VAS_090_QAQCDate", "QA/QC Date")) + '</div>' +
                '<input class="vas-ra-date" type="date" value="' + escapeHtml(qaDate) + '"/>' +
                '</div>' +
                '</div>' +
                '<div class="vas-ra-qc-field active note">' +
                '<div class="vas-ra-qc-main">' +
                '<div class="vas-ra-qc-label">' + escapeHtml(lbl("VAS_090_Description", "Description")) + '</div>' +
                '<textarea class="vas-ra-desc" rows="2" placeholder="' + escapeHtml(lbl("VAS_090_AddNoteOptional", "Add a note (optional)")) + '">' + escapeHtml(record.description || "") + '</textarea>' +
                '</div>' +
                '</div>';

            $dialogBody.html(
                qaPagerHtml() +
                '<div class="vas-ra-qc-headline">' + escapeHtml(lbl("VAS_090_QualityControl", "Quality Control")) + '</div>' +
                '<div class="vas-ra-qc-grid">' +
                '<div class="vas-ra-qc-col">' + left + '</div>' +
                '<div class="vas-ra-qc-col">' + right + '</div>' +
                '</div>' +
                '<div class="vas-ra-action-footer right">' +
                '<button type="button" class="vas-ra-primary vas-ra-save-qa"' + saveDisabled + saveTitle + '>' + icon("check") + escapeHtml(lbl("VAS_090_SaveQAResult", "Save QA Result")) + '</button>' +
                '</div>'
            );

            updateActualTone();
        }

        function qaPagerHtml() {
            if (qaTotalPages <= 1) { return ""; }
            return '<div class="vas-ra-modal-pager top">' +
                '<span>' + escapeHtml(lbl("VAS_090_QAHold", "QA hold") + ' ' + qaPageNo + ' ' + lbl("VAS_090_Of", "of") + ' ' + qaTotalRecords) + '</span>' +
                '<button type="button" class="vas-ra-pgbtn vas-ra-qa-prev"' + (qaPageNo <= 1 ? ' disabled' : '') + '>' + icon("chevL") + '</button>' +
                '<button type="button" class="vas-ra-pgbtn vas-ra-qa-next"' + (qaPageNo >= qaTotalPages ? ' disabled' : '') + '>' + icon("chevR") + '</button>' +
                '</div>';
        }

        function updateActualTone() {
            var $field = $dialogBody.find('.vas-ra-actual').closest('.vas-ra-qc-field');
            var value = $dialogBody.find('.vas-ra-actual').val();
            $field.removeClass('res-ok res-bad');
            if (value === "Working Fine") { $field.addClass('res-ok'); }
            if (value === "Not Satisfactory") { $field.addClass('res-bad'); }
        }

        function saveQAResult() {
            if (!qaRecord || qaRecord.qaRecordId <= 0) { return; }

            var actualValue = $dialogBody.find('.vas-ra-actual').val();
            if (!actualValue) {
                notify(lbl("VAS_090_SelectQAActualValue", "Select an actual value before saving."));
                return;
            }

            showDialogBusy(true);
            $dialogBody.find('.vas-ra-save-qa').prop('disabled', true);

            $.ajax({
                url: VIS.Application.contextUrl + 'VAS_QAHoldsWidget/SaveQAResult',
                type: 'POST',
                cache: false,
                data: {
                    qaRecordId: qaRecord.qaRecordId,
                    actualValue: actualValue,
                    qaQcDate: $dialogBody.find('.vas-ra-date').val(),
                    description: $dialogBody.find('.vas-ra-desc').val()
                },
                success: function (res) {
                    var data = parseResponse(res);
                    if (!data || data.error) {
                        notify(data && data.error ? data.error : lbl("VAS_090_SaveFailed", "Save failed."));
                        return;
                    }

                    closeDialog();
                    $(document).trigger('vas-qaholds-updated');
                    $(document).trigger('vas-receiving-actions-updated');
                },
                error: function () {
                    notify(lbl("VAS_090_SaveFailed", "Save failed."));
                },
                complete: function () {
                    showDialogBusy(false);
                    if ($dialogBody) { $dialogBody.find('.vas-ra-save-qa').prop('disabled', false); }
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
            showDialogBusy(true);

            $.ajax({
                url: VIS.Application.contextUrl + 'VAS_090_ReceivingActionsWidget/SearchGRNLabels',
                type: 'GET',
                cache: false,
                data: { searchText: labelSearchText, pageNo: page, pageSize: 5 },
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
                },
                error: function () {
                    showDialogBusy(false);
                    $dialogBody.html('<div class="vas-ra-error">' + escapeHtml(lbl("VAS_090_NoDataAvailable", "No data available")) + '</div>');
                }
            });
        }

        function renderGRNLabelResults(rows) {
            if ($dialogBody.find('.vas-ra-label-shell').length === 0) {
                $dialogBody.html(
                    '<div class="vas-ra-label-shell">' +
                    '<div class="vas-ra-note">' + icon("search") + '<span>' + escapeHtml(lbl("VAS_090_LabelSearchNote", "Search by GRN number or customer/supplier, then pick a GRN to print its label.")) + '</span></div>' +
                    '<div class="vas-ra-search"><input class="vas-ra-label-search" type="text" placeholder="' + escapeHtml(lbl("VAS_090_GRNSearchPlaceholder", "GRN number or supplier name...")) + '"/></div>' +
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
            var from = (labelPageNo - 1) * 5 + 1;
            var to = Math.min(labelPageNo * 5, labelTotalRecords);
            return '<div class="vas-ra-modal-pager">' +
                '<span>' + escapeHtml(lbl("VAS_090_Showing", "Showing") + ' ' + from + '-' + to + ' ' + lbl("VAS_090_Of", "of") + ' ' + labelTotalRecords) + '</span>' +
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

                    setModal(lbl("VAS_090_PrintLabel", "Print Label") + " - " + (data.grnNo || (row ? row.grnNo : "")), data.status || lbl("VAS_090_Queued", "Queued"), "ok");
                    $dialogBody.html(
                        '<div class="vas-ra-form">' +
                        fieldHtml(lbl("VAS_090_GRN", "GRN"), data.grnNo || "-", true) +
                        fieldHtml(lbl("VAS_090_Copies", "Copies"), data.copies || 1) +
                        fieldHtml(lbl("VAS_090_Format", "Format"), data.printFormat || "Put-away label 4x6") +
                        fieldHtml(lbl("VAS_090_Printer", "Printer"), data.printer || "Default printer") +
                        fieldHtml(lbl("VAS_090_Includes", "Includes"), data.includes || "Barcode + locator") +
                        fieldHtml(lbl("VAS_090_Status", "Status"), data.status || "Queued") +
                        '</div>'
                    );
                    $(document).trigger('vas-receiving-actions-updated');
                },
                error: function () {
                    showDialogBusy(false);
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
