/**
 * New GRN Widget
 * 1x1 quick action. Opens the documented receive flow:
 * pick open PO -> load open lines -> enter received quantities -> create GRN.
 *
 * Backend - VAS_082_NewGRNWidget/GetOpenPurchaseOrders, GetPurchaseOrderLines, CreateGRN
 * Summary Message Table: see Labels / Message Keys below.
 *
 * Labels / Message Keys
 *  #  | Current Text                                     | Message Key
 * ----+--------------------------------------------------+-----------------------------------
 *  1  | New GRN                                          | VAS_082_NewGRN
 *  2  | Receive against a PO                             | VAS_082_ReceiveAgainstPO
 *  3  | Back                                             | VAS_082_Back
 *  4  | Close                                            | VAS_082_Close
 *  5  | No data available                                | VAS_082_NoDataAvailable
 *  6  | Select a purchase order                          | VAS_082_SelectPurchaseOrder
 *  7  | Purchase Order                                   | VAS_082_PurchaseOrder
 *  8  | Supplier                                         | VAS_082_Supplier
 *  9  | Warehouse                                        | VAS_082_Warehouse
 * 10  | Lines                                            | VAS_082_Lines
 * 11  | Enter received quantity against each PO line...  | VAS_082_EnterReceivedQtyAgainstLine
 * 12  | Item                                             | VAS_082_Item
 * 13  | PO Qty                                           | VAS_082_POQty
 * 14  | Already Received                                 | VAS_082_AlreadyReceived
 * 15  | Received                                         | VAS_082_Received
 * 16  | UOM                                              | VAS_082_UOM
 * 17  | Make GRN                                         | VAS_082_MakeGRN
 * 18  | Received quantity cannot be negative.            | VAS_082_NegativeReceivedQty
 * 19  | Enter received quantity for at least one line.   | VAS_082_ReceivedQtyRequired
 * 20  | Received quantity cannot be greater than open... | VAS_082_ReceivedQtyTooHigh
 * 21  | GRN could not be created.                        | VAS_082_GRNCouldNotBeCreated
 * 22  | Unable to open the GRN window.                   | VAS_082_CouldntOpenWindow
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

    VAS.VAS_082_NewGRNWidget = function () {

        this.frame;
        this.windowNo;

        var $self = this;
        var $root = $('<div class="vas-ngrn-root">');
        var $dialog;
        var $dialogBody;
        var $dialogTitle;
        var $dialogBadge;
        var $dialogBusy;

        var purchaseOrders = [];
        var purchaseOrdersById = {};
        var currentPO = null;
        var currentLines = [];
        var grnWindowId = 0;
        var pageNo = 1;
        var pageSize = 8;
        var totalPages = 0;
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
            return data || {};
        }

        function formatQty(value) {
            return Number(value || 0).toLocaleString(window.navigator.language, { maximumFractionDigits: 2 });
        }

        function toInputValue(value) {
            var num = Number(value || 0);
            if (!isFinite(num)) { return "0"; }
            return String(Math.round(num * 1000000) / 1000000);
        }

        function showDialogBusy(show) {
            if (!$dialogBusy || !$dialogBusy[0]) { return; }
            $dialogBusy.toggleClass('vas-ngrn-hidden', !show);
        }

        function setBadge(text, cls) {
            if (!$dialogBadge) { return; }
            if (!text) {
                $dialogBadge.addClass('vas-ngrn-hidden').empty();
                return;
            }
            $dialogBadge
                .html('<span class="vas-ngrn-pill ' + escapeHtml(cls || "info") + '">' + escapeHtml(text) + '</span>')
                .removeClass('vas-ngrn-hidden');
        }

        this.Initalize = function () {
            createWidget();
            createDialog();
            loadGrnWindowId(null);
        };

        // Review #17: clicking New GRN navigates straight to the Material Receipt
        // (GRN) window - no modal is opened.
        function loadGrnWindowId(onReady) {
            $.ajax({
                url: VIS.Application.contextUrl + 'VAS_082_NewGRNWidget/GetGrnWindowId',
                type: 'GET',
                cache: false,
                success: function (response) {
                    var result = parseResponse(response);
                    grnWindowId = Number((result && result.windowId) || 0);
                    if (onReady) { onReady(grnWindowId); }
                },
                error: function () {
                    if (onReady) { onReady(0); }
                }
            });
        }

        /* The GRN window must open on a NEW record and must never be duplicated.
           viewManager.startWindow only reuses windows in its closed-window cache;
           an already-OPEN window gets a second instance on every click. So:
           - if the GRN window this widget opened is still open, bring that SAME
             window to the front and start another new record in it (cmd_new);
           - otherwise open it once with a "new record" query so the CORE itself
             auto-starts the blank record when the tab loads (buildNewRecordQuery).
           The opened-window reference is kept on a WINDOW-GLOBAL (keyed by window
           id), not on widget-instance state, so it survives the dashboard
           re-creating the widget - otherwise the reference resets and a new window
           opens every time. */
        var GRN_VIEW_STORE = '__vasGrnViews';

        function rememberGrnView(windowId, view) {
            if (!window[GRN_VIEW_STORE]) { window[GRN_VIEW_STORE] = {}; }
            window[GRN_VIEW_STORE][windowId] = view || null;
        }

        function recallGrnView(windowId) {
            return window[GRN_VIEW_STORE] ? window[GRN_VIEW_STORE][windowId] : null;
        }

        function startNewRecordIn(view) {
            try {
                if (view && view.cPanel && view.cPanel.cmd_new) {
                    view.cPanel.cmd_new(false);
                    return true;
                }
            } catch (e) { /* window still open; user can press New */ }
            return false;
        }

        // The taskbar LI carries the view id and is removed on close, so its
        // presence in the DOM means that window is still open.
        function isViewStillOpen(view) {
            if (!view || !view.getId) { return false; }
            var li = document.getElementById(String(view.getId()));
            return !!(li && li.tagName === 'LI');
        }

        function focusView(view) {
            var viewId = view.getId();
            var li = document.getElementById(String(viewId));
            if (li && $(li).hasClass('vis-app-f-selected')) { return; }
            if (VIS.desktopMgr && VIS.desktopMgr.toggleContainer) {
                VIS.desktopMgr.toggleContainer(viewId);
            }
            if (VIS.desktopMgr && VIS.desktopMgr.activateTaskBarItemUsingID) {
                VIS.desktopMgr.activateTaskBarItemUsingID({ data: function () { return viewId; } });
            }
        }

        /* Core-native "open on new record": a query flagged as a new-record query
           loads no rows ("2=3"), and GridController.queryCompleted ->
           checkInsertNewRow() then auto-starts a blank record (dataNew) as soon
           as the tab finishes loading. No onLoad/cmd_new timing needed - this is
           the same path the core itself uses for zoom-to-new-record. */
        function buildNewRecordQuery() {
            var query = null;
            try {
                query = new VIS.Query("M_InOut");
                query.addRestriction(VIS.Query.prototype.NEWRECORD); // "2=3" -> loads no rows
                // addRestriction only auto-flags against the static VIS.Query.NEWRECORD,
                // which this core never assigns (only the prototype constant exists),
                // so set the flag checkInsertNewRow() reads directly.
                query.newRecord = true;
                if (query.setRecordCount) { query.setRecordCount(0); }
            } catch (e) { query = null; }
            return query;
        }

        function startWindowById(windowId) {
            // Reuse the window we already opened, if it is still open: focus it
            // and start a new record in it - no second window.
            var existing = recallGrnView(windowId);
            if (existing && isViewStillOpen(existing)) {
                try {
                    focusView(existing);
                    startNewRecordIn(existing);
                    return;
                } catch (e) { /* fall through and open it again */ }
            }

            var newRecordQuery = buildNewRecordQuery();

            var view = null;
            if (VIS.viewManager && VIS.viewManager.startWindow) {
                view = VIS.viewManager.startWindow(windowId, newRecordQuery);
            }
            else if (VIS.AEnv && VIS.AEnv.startWindow) {
                view = VIS.AEnv.startWindow(windowId, newRecordQuery);
            }

            rememberGrnView(windowId, view);
        }

        // Open the widget's configured window (the Material Receipt / GRN window)
        // directly on a NEW record through the widget framework's value-changed
        // channel. The host reuses the same window and starts a blank record
        // (IsTabInNewMode) - no duplicate window is opened.
        function openGrnNewRecord() {
            var windowParam = {
                "IsTabInNewMode": "true",
                "TabIndex": "0"
            };
            $self.widgetFirevalueChanged(windowParam);
        }

        function openGrnWindow() {
            if (grnWindowId > 0) {
                startWindowById(grnWindowId);
                return;
            }
            loadGrnWindowId(function (windowId) {
                if (windowId > 0) {
                    startWindowById(windowId);
                }
                else {
                    VIS.ADialog.error(
                        'VAS_082_CouldntOpenWindow',
                        true,
                        '',
                        lbl('VAS_082_CouldntOpenWindow', 'Unable to open the GRN window.')
                    );
                }
            });
        }

        function createWidget() {
            var $card = $(
                '<button type="button" class="vas-ngrn-card vas-widget-bg" aria-label="' + escapeHtml(lbl("VAS_082_NewGRN", "New GRN")) + '">' +
                '<div class="vas-ngrn-iconrow">' +
                '<span class="vas-ngrn-ico">' +
                '<svg viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2.2" stroke-linecap="round" stroke-linejoin="round"><line x1="12" y1="5" x2="12" y2="19"/><line x1="5" y1="12" x2="19" y2="12"/></svg>' +
                '</span>' +
                '</div>' +
                '<div class="vas-ngrn-text">' +
                '<div class="vas-ngrn-title">' + escapeHtml(lbl("VAS_082_NewGRN", "New GRN")) + '</div>' +
                '<div class="vas-ngrn-sub">' + escapeHtml(lbl("VAS_082_ReceiveAgainstPO", "Receive against a PO")) + '</div>' +
                '</div>' +
                '</button>'
            );

            // Open the GRN window on a NEW record via the widget framework's
            // value-changed channel (IsTabInNewMode), reusing the same window
            // instead of opening a duplicate each click.
            $card.on('click', function () { openGrnNewRecord(); });
            $root.append($card);
        }

        function createDialog() {
            $dialog = $(
                '<div class="vas-ngrn-dialog vas-ngrn-hidden" role="dialog" aria-modal="true">' +
                '<div class="vas-ngrn-scrim"></div>' +
                '<div class="vas-ngrn-card-modal">' +
                '<div class="vas-ngrn-header">' +
                '<div class="vas-ngrn-title-left">' +
                '<button type="button" class="vas-ngrn-back vas-ngrn-hidden" aria-label="' + escapeHtml(lbl("VAS_082_Back", "Back")) + '">' +
                '<svg viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2" stroke-linecap="round" stroke-linejoin="round"><polyline points="15 18 9 12 15 6"/></svg>' +
                '</button>' +
                '<h3 class="vas-ngrn-modal-title"></h3>' +
                '<span class="vas-ngrn-modal-badge vas-ngrn-hidden"></span>' +
                '</div>' +
                '<button type="button" class="vas-ngrn-close" aria-label="' + escapeHtml(lbl("VAS_082_Close", "Close")) + '">' +
                '<svg viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="1.8" stroke-linecap="round" stroke-linejoin="round"><line x1="18" y1="6" x2="6" y2="18"/><line x1="6" y1="6" x2="18" y2="18"/></svg>' +
                '</button>' +
                '</div>' +
                '<div class="vas-ngrn-body"></div>' +
                '<div class="vas-ngrn-busy vas-ngrn-hidden"><div class="vis-busyindicatorinnerwrap"><i class="vis_widgetloader"></i></div></div>' +
                '</div>' +
                '</div>'
            );

            $dialogBody = $dialog.find('.vas-ngrn-body');
            $dialogTitle = $dialog.find('.vas-ngrn-modal-title');
            $dialogBadge = $dialog.find('.vas-ngrn-modal-badge');
            $dialogBusy = $dialog.find('.vas-ngrn-busy');

            $dialog.find('.vas-ngrn-close').on('click', closeDialog);
            $dialog.find('.vas-ngrn-scrim').on('click', closeDialog);
            $dialog.find('.vas-ngrn-back').on('click', showPOStep);

            $dialogBody.on('click', '.vas-ngrn-po-row', function () {
                selectPO(Number($(this).data('poid') || 0));
            });
            $dialogBody.on('input', '.vas-ngrn-rcv-in', validateLines);
            $dialogBody.on('click', '.vas-ngrn-create-btn', createGRN);
            $dialogBody.on('click', '.vas-ngrn-prev', function () {
                if (pageNo > 1) { loadPOs(pageNo - 1); }
            });
            $dialogBody.on('click', '.vas-ngrn-next', function () {
                if (pageNo < totalPages) { loadPOs(pageNo + 1); }
            });

            $(document).on('keydown.vas-ngrn', function (e) {
                if (e.key === 'Escape' && !$dialog.hasClass('vas-ngrn-hidden')) { closeDialog(); }
            });
            $(window).on('resize.vas-ngrn', syncPOPageSize);

            $('body').append($dialog);
        }

        function openDialog() {
            if (!$dialog) { return; }
            currentPO = null;
            currentLines = [];
            $dialog.removeClass('vas-ngrn-hidden');
            $('body').addClass('vas-ngrn-body-lock');
            showPOStep();
            loadPOs(1);
        }

        function closeDialog() {
            if (!$dialog) { return; }
            $dialog.addClass('vas-ngrn-hidden');
            $('body').removeClass('vas-ngrn-body-lock');
        }

        function showPOStep() {
            currentPO = null;
            currentLines = [];
            $dialog.find('.vas-ngrn-back').addClass('vas-ngrn-hidden');
            $dialogTitle.text(lbl("VAS_082_NewGRN", "New GRN"));
            setBadge("", "");
            renderPOList();
        }

        function measurePOPageSize() {
            if (!$dialog || $dialog.hasClass('vas-ngrn-hidden') || currentPO) { return pageSize; }

            var $list = $dialogBody.find('.vas-ngrn-po-list');
            if (!$list.length) { return pageSize; }

            var available = Math.floor($dialogBody.innerHeight());
            available -= Math.ceil($dialogBody.find('.vas-ngrn-step-hint:first').outerHeight(true) || 0);
            available -= Math.ceil($dialogBody.find('.vas-ngrn-pager:first').outerHeight(true) || 0);
            available -= 12;

            var $sample = $list.find('.vas-ngrn-po-row:first');
            var rowHeight = $sample.length ? Math.ceil($sample.outerHeight(true)) : 56;
            if (rowHeight <= 0) { rowHeight = 56; }

            return Math.max(3, Math.floor(available / rowHeight));
        }

        function syncPOPageSize() {
            if (loading || currentPO) { return; }

            var nextPageSize = measurePOPageSize();
            if (nextPageSize === pageSize) { return; }

            var firstRecord = ((pageNo - 1) * pageSize) + 1;
            pageSize = nextPageSize;
            loadPOs(Math.max(1, Math.ceil(firstRecord / pageSize)));
        }

        function loadPOs(targetPage) {
            loading = true;
            pageNo = targetPage || 1;
            showDialogBusy(true);

            $.ajax({
                url: VIS.Application.contextUrl + 'VAS_082_NewGRNWidget/GetOpenPurchaseOrders',
                type: 'GET',
                data: { pageNo: pageNo, pageSize: pageSize },
                success: function (res) {
                    var data = parseResponse(res);
                    if (data.error) {
                        VIS.ADialog.error("", false, data.error, "");
                        return;
                    }

                    purchaseOrders = data.rows || [];
                    purchaseOrdersById = {};
                    totalPages = Number(data.totalPages || 0);

                    for (var i = 0; i < purchaseOrders.length; i++) {
                        purchaseOrdersById[purchaseOrders[i].poId] = purchaseOrders[i];
                    }

                    renderPOList();
                },
                error: function () {
                    VIS.ADialog.error("", false, lbl("VAS_082_NoDataAvailable", "No data available"), "");
                },
                complete: function () {
                    loading = false;
                    showDialogBusy(false);
                }
            });
        }

        function renderPOList() {
            if (!$dialogBody) { return; }

            if (loading && purchaseOrders.length === 0) {
                $dialogBody.html('');
                return;
            }

            if (purchaseOrders.length === 0) {
                $dialogBody.html('<div class="vas-ngrn-empty">' + escapeHtml(lbl("VAS_082_NoDataAvailable", "No data available")) + '</div>');
                return;
            }

            var rows = '';
            for (var i = 0; i < purchaseOrders.length; i++) {
                var po = purchaseOrders[i];
                rows +=
                    '<button type="button" class="vas-ngrn-po-row" data-poid="' + escapeHtml(po.poId) + '">' +
                    '<div class="vas-ngrn-po-main">' +
                    '<span class="vas-ngrn-po-no">' + escapeHtml(po.poNo) + '</span>' +
                    '<span class="vas-ngrn-po-vendor">' + escapeHtml(po.supplier) + '</span>' +
                    '</div>' +
                    '<div class="vas-ngrn-po-side">' +
                    (po.warehouseName ? '<span class="vas-ngrn-po-wh">' + escapeHtml(po.warehouseName) + '</span>' : '') +
                    '<span class="vas-ngrn-po-badge">' + escapeHtml(po.openLineCount) + '</span>' +
                    '</div>' +
                    '</button>';
            }

            var paging = '';
            if (totalPages > 1) {
                paging =
                    '<div class="vas-ngrn-pager">' +
                    '<button type="button" class="vas-ngrn-page-btn vas-ngrn-prev" ' + (pageNo <= 1 ? 'disabled' : '') + '>&lsaquo;</button>' +
                    '<span>' + escapeHtml(pageNo) + ' / ' + escapeHtml(totalPages) + '</span>' +
                    '<button type="button" class="vas-ngrn-page-btn vas-ngrn-next" ' + (pageNo >= totalPages ? 'disabled' : '') + '>&rsaquo;</button>' +
                    '</div>';
            }

            $dialogBody.html(
                '<div class="vas-ngrn-step-hint">' + escapeHtml(lbl("VAS_082_SelectPurchaseOrder", "Select a purchase order")) + '</div>' +
                '<div class="vas-ngrn-po-list">' + rows + '</div>' +
                paging
            );
            window.setTimeout(syncPOPageSize, 0);
        }

        function selectPO(poId) {
            var po = purchaseOrdersById[poId];
            if (!po) { return; }

            currentPO = po;
            currentLines = [];
            $dialog.find('.vas-ngrn-back').removeClass('vas-ngrn-hidden');
            $dialogTitle.text(lbl("VAS_082_ReceiveAgainstPO", "Receive Against PO"));
            setBadge(po.poNo, "info");
            showDialogBusy(true);

            $.ajax({
                url: VIS.Application.contextUrl + 'VAS_082_NewGRNWidget/GetPurchaseOrderLines',
                type: 'GET',
                data: { poId: po.poId },
                success: function (res) {
                    var data = parseResponse(res);
                    if (data.error) {
                        VIS.ADialog.error("", false, data.error, "");
                        return;
                    }

                    currentLines = data.rows || [];
                    renderLineEntry();
                },
                error: function () {
                    VIS.ADialog.error("", false, lbl("VAS_082_NoDataAvailable", "No data available"), "");
                },
                complete: function () {
                    showDialogBusy(false);
                }
            });
        }

        function renderLineEntry() {
            if (!currentPO) { return; }

            if (currentLines.length === 0) {
                $dialogBody.html('<div class="vas-ngrn-empty">' + escapeHtml(lbl("VAS_082_NoDataAvailable", "No data available")) + '</div>');
                return;
            }

            var fields =
                '<div class="vas-ngrn-form-grid">' +
                fieldHtml(lbl("VAS_082_PurchaseOrder", "Purchase Order"), currentPO.poNo, true) +
                fieldHtml(lbl("VAS_082_Supplier", "Supplier"), currentPO.supplier) +
                fieldHtml(lbl("VAS_082_Warehouse", "Warehouse"), currentPO.warehouseName || "-") +
                fieldHtml(lbl("VAS_082_Lines", "Lines"), String(currentLines.length)) +
                '</div>';

            var rows = '';
            for (var i = 0; i < currentLines.length; i++) {
                var line = currentLines[i];
                rows +=
                    '<div class="vas-ngrn-rcv-line" data-lineid="' + escapeHtml(line.poLineId) + '" data-openqty="' + escapeHtml(line.openQty) + '">' +
                    '<div class="vas-ngrn-rcv-name" title="' + escapeHtml(line.itemName) + '">' + escapeHtml(line.itemName) + '</div>' +
                    '<div class="vas-ngrn-rcv-po">' + escapeHtml(formatQty(line.poQty)) + '</div>' +
                    '<div class="vas-ngrn-rcv-po">' + escapeHtml(formatQty(line.alreadyReceivedQty)) + '</div>' +
                    '<input class="vas-ngrn-rcv-in" type="number" min="0" max="' + escapeHtml(line.openQty) + '" step="any" value="' + escapeHtml(toInputValue(line.defaultReceivedQty)) + '" aria-label="Received quantity"/>' +
                    '<div class="vas-ngrn-rcv-uom">' + escapeHtml(line.uom) + '</div>' +
                    '</div>';
            }

            $dialogBody.html(
                fields +
                '<div class="vas-ngrn-note">' + fileIcon() + '<span>' + escapeHtml(lbl("VAS_082_EnterReceivedQtyAgainstLine", "Enter received quantity against each PO line, then create the GRN.")) + '</span></div>' +
                '<div class="vas-ngrn-rcv-line vas-ngrn-rcv-head">' +
                '<div>' + escapeHtml(lbl("VAS_082_Item", "Item")) + '</div>' +
                '<div>' + escapeHtml(lbl("VAS_082_POQty", "PO Qty")) + '</div>' +
                '<div>' + escapeHtml(lbl("VAS_082_AlreadyReceived", "Already Received")) + '</div>' +
                '<div>' + escapeHtml(lbl("VAS_082_Received", "Received")) + '</div>' +
                '<div>' + escapeHtml(lbl("VAS_082_UOM", "UOM")) + '</div>' +
                '</div>' +
                '<div class="vas-ngrn-lines">' + rows + '</div>' +
                '<div class="vas-ngrn-error vas-ngrn-hidden"></div>' +
                '<div class="vas-ngrn-action"><button type="button" class="vas-ngrn-create-btn">' + checkIcon() + '<span>' + escapeHtml(lbl("VAS_082_MakeGRN", "Make GRN")) + '</span></button></div>'
            );

            validateLines();
        }

        function fieldHtml(label, value, strong) {
            return '<div class="vas-ngrn-field">' +
                '<div class="vas-ngrn-field-label">' + escapeHtml(label) + '</div>' +
                '<div class="vas-ngrn-field-value ' + (strong ? "strong" : "") + '">' + escapeHtml(value || "-") + '</div>' +
                '</div>';
        }

        function collectLines() {
            var lines = [];
            var invalid = false;
            var message = "";

            $dialogBody.find('.vas-ngrn-rcv-line[data-lineid]').each(function () {
                var $row = $(this);
                var lineId = Number($row.data('lineid') || 0);
                var openQty = Number($row.data('openqty') || 0);
                var qty = Number($row.find('.vas-ngrn-rcv-in').val() || 0);

                $row.removeClass('invalid');

                if (!isFinite(qty) || qty < 0) {
                    invalid = true;
                    message = lbl("VAS_082_NegativeReceivedQty", "Received quantity cannot be negative.");
                    $row.addClass('invalid');
                    return;
                }

                if (qty > openQty) {
                    invalid = true;
                    message = lbl("VAS_082_ReceivedQtyTooHigh", "Received quantity cannot be greater than open quantity.");
                    $row.addClass('invalid');
                    return;
                }

                if (qty > 0) {
                    lines.push({ poLineId: lineId, receivedQty: qty });
                }
            });

            return { lines: lines, invalid: invalid, message: message };
        }

        function validateLines() {
            var result = collectLines();
            var $error = $dialogBody.find('.vas-ngrn-error');
            var $button = $dialogBody.find('.vas-ngrn-create-btn');

            if (result.invalid) {
                $error.text(result.message).removeClass('vas-ngrn-hidden');
                $button.prop('disabled', true);
                return false;
            }

            if (result.lines.length === 0) {
                $error.text(lbl("VAS_082_ReceivedQtyRequired", "Enter received quantity for at least one line.")).removeClass('vas-ngrn-hidden');
                $button.prop('disabled', true);
                return false;
            }

            $error.addClass('vas-ngrn-hidden').empty();
            $button.prop('disabled', false);
            return true;
        }

        function createGRN() {
            if (!currentPO || !validateLines()) { return; }

            var result = collectLines();
            var $button = $dialogBody.find('.vas-ngrn-create-btn');

            $button.prop('disabled', true);
            showDialogBusy(true);

            $.ajax({
                url: VIS.Application.contextUrl + 'VAS_082_NewGRNWidget/CreateGRN',
                type: 'POST',
                data: {
                    poId: currentPO.poId,
                    linesJson: JSON.stringify(result.lines)
                },
                success: function (res) {
                    var data = parseResponse(res);
                    if (data.error || data.success === false) {
                        VIS.ADialog.error("", false, data.message || data.error || lbl("VAS_082_GRNCouldNotBeCreated", "GRN could not be created."), "");
                        $button.prop('disabled', false);
                        return;
                    }

                    closeDialog();
                    $self.refreshWidget();
                    $(document).trigger('VAS_GRNCreated', [data]);
                },
                error: function () {
                    VIS.ADialog.error("", false, lbl("VAS_082_GRNCouldNotBeCreated", "GRN could not be created."), "");
                    $button.prop('disabled', false);
                },
                complete: function () {
                    showDialogBusy(false);
                }
            });
        }

        function fileIcon() {
            return '<svg viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="1.8" stroke-linecap="round" stroke-linejoin="round"><path d="M14 2H6a2 2 0 0 0-2 2v16a2 2 0 0 0 2 2h12a2 2 0 0 0 2-2V8z"/><polyline points="14 2 14 8 20 8"/></svg>';
        }

        function checkIcon() {
            return '<svg viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2.2" stroke-linecap="round" stroke-linejoin="round"><polyline points="20 6 9 17 4 12"/></svg>';
        }

        this.refreshWidget = function () {
            purchaseOrders = [];
            purchaseOrdersById = {};
            currentPO = null;
            currentLines = [];
            pageNo = 1;
            totalPages = 0;
        };

        this.getRoot = function () { return $root; };

        this.disposeComponent = function () {
            $(document).off('keydown.vas-ngrn');
            $(window).off('resize.vas-ngrn');
            $('body').removeClass('vas-ngrn-body-lock');
            if ($dialog) { $dialog.remove(); $dialog = null; }
            $root.remove();
        };
    };

    VAS.VAS_082_NewGRNWidget.prototype.widgetFirevalueChanged = function (value) {
        if (this.listener) { this.listener.widgetFirevalueChanged(value); }
    };

    VAS.VAS_082_NewGRNWidget.prototype.addChangeListener = function (listener) {
        this.listener = listener;
    };

    VAS.VAS_082_NewGRNWidget.prototype.init = function (windowNo, frame) {
        this.frame = frame;
        this.AD_UserHomeWidgetID = frame.widgetInfo.AD_UserHomeWidgetID;
        this.windowNo = windowNo;
        this.Initalize();
        this.frame.getContentGrid().append(this.getRoot());
        ensureDashInlineSizeVar(this.getRoot());
    };

    VAS.VAS_082_NewGRNWidget.prototype.widgetSizeChange = function (height, width) { };

    VAS.VAS_082_NewGRNWidget.prototype.refreshWidget = function () {
        this.refreshWidget();
    };

    VAS.VAS_082_NewGRNWidget.prototype.dispose = function () {
        this.disposeComponent();
        if (this.frame) { this.frame.dispose(); }
        this.frame = null;
    };

})(VAS, jQuery);
