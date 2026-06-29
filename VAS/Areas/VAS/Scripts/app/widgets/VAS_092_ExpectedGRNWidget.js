/**
 * Expected GRN Widget (Material Receipt / GRN dashboard)
 * Purpose - 3x2 glass document list of completed vendor purchase orders whose
 *           promised (expected) date is today or later and that still have open
 *           quantity to receive. Each row shows PO number, supplier, ship-to
 *           address, destination warehouse, line count and PO value. A row click
 *           opens the shared GRN line-entry modal (Item | PO Qty | Received
 *           editable input, defaulting to open qty) whose "Select & Make GRN"
 *           button creates and completes the receipt.
 * Backend - VAS_092_ExpectedGRNWidget/GetExpectedPurchaseOrders
 *           VAS_092_ExpectedGRNWidget/GetPurchaseOrderLines
 *           VAS_092_ExpectedGRNWidget/CreateGRN
 * Summary Message Table: see Labels / Message Keys below.
 *
 * Labels / Message Keys
 *  #  | Current Text                                     | Message Key
 * ----+--------------------------------------------------+-----------------------------------
 *  1  | Expected GRN                                      | VAS_ExpectedGRN
 *  2  | GRN Count                                         | VAS_GRNCount
 *  3  | No of Lines                                       | VAS_NoOfLines
 *  4  | Showing                                           | VAS_Showing
 *  5  | of                                                | VAS_Of
 *  6  | No data available                                | VAS_NoDataAvailable
 *  7  | Back                                              | VAS_Back
 *  8  | Close                                             | VAS_Close
 *  9  | Item                                              | VAS_Item
 * 10  | PO Qty                                            | VAS_POQty
 * 11  | Received                                          | VAS_Received
 * 12  | UOM                                               | VAS_Uom
 * 13  | Enter received quantity against each PO line...   | VAS_EnterReceivedQtyAgainstLine
 * 14  | Select & Make GRN                                 | VAS_SelectAndMakeGRN
 * 15  | Received quantity cannot be negative.             | VAS_NegativeReceivedQty
 * 16  | Enter received quantity for at least one line.    | VAS_ReceivedQtyRequired
 * 17  | Received quantity cannot be greater than open...  | VAS_ReceivedQtyTooHigh
 * 18  | GRN could not be created.                         | VAS_GRNCouldNotBeCreated
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

    VAS.VAS_092_ExpectedGRNWidget = function () {

        this.frame;
        this.windowNo;
        this.widgetInfo;

        var $self = this;
        var $root = $('<div class="vas-egrn-root">');
        var $body, $busy, $footer, $pageInfo, $pageText, $prevBtn, $nextBtn, $countPill;
        var $dialog, $dialogBody, $dialogTitle, $dialogBadge, $dialogBusy;

        var ROWS = [];
        var rowsById = {};
        var currentPO = null;
        var currentLines = [];
        var pageNo = 1;
        var pageSize = 3;
        var totalPages = 0;
        var totalRecords = 0;
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

        /* Format a currency amount using the precision read from the system (never hard-coded). */
        function formatMoney(value, precision, symbol) {
            var p = Number(precision);
            if (!isFinite(p) || p < 0) { p = 2; }
            var num = Number(value || 0).toLocaleString(window.navigator.language, {
                minimumFractionDigits: p,
                maximumFractionDigits: p
            });
            return (symbol ? symbol + ' ' : '') + num;
        }

        function toInputValue(value) {
            var num = Number(value || 0);
            if (!isFinite(num)) { return "0"; }
            return String(Math.round(num * 1000000) / 1000000);
        }

        function showBusy(show) {
            if (!$busy || !$busy[0]) { return; }
            $busy.toggleClass('vas-egrn-hidden', !show);
        }

        function showDialogBusy(show) {
            if (!$dialogBusy || !$dialogBusy[0]) { return; }
            $dialogBusy.toggleClass('vas-egrn-hidden', !show);
        }

        function setBadge(text, cls) {
            if (!$dialogBadge) { return; }
            if (!text) {
                $dialogBadge.addClass('vas-egrn-hidden').empty();
                return;
            }
            $dialogBadge
                .html('<span class="vas-egrn-pill ' + escapeHtml(cls || "info") + '">' + escapeHtml(text) + '</span>')
                .removeClass('vas-egrn-hidden');
        }

        this.initalize = function () {
            createWidget();
            createDialog();
        };

        function createWidget() {
            var chevL = '<svg viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2" stroke-linecap="round" stroke-linejoin="round"><polyline points="15 18 9 12 15 6"/></svg>';
            var chevR = '<svg viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2" stroke-linecap="round" stroke-linejoin="round"><polyline points="9 18 15 12 9 6"/></svg>';

            var $card = $('<div class="vas-egrn-card vas-widget-bg">');

            var $header = $(
                '<div class="vas-egrn-head">' +
                '<span class="vas-egrn-ico">' +
                '<svg viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="1.8" stroke-linecap="round" stroke-linejoin="round"><path d="M3 7l9-4 9 4-9 4-9-4z"/><path d="M3 7v10l9 4 9-4V7"/><path d="M12 11v10"/></svg>' +
                '</span>' +
                '<div class="vas-egrn-titles">' +
                '<div class="vas-egrn-title">' + escapeHtml(lbl("VAS_ExpectedGRN", "Expected GRN")) + '</div>' +
                '</div>' +
                '<span class="vas-egrn-count vas-egrn-hidden"></span>' +
                '</div>'
            );

            $body = $(
                '<div class="vas-egrn-body">' +
                '<div class="vas-egrn-rows"></div>' +
                '</div>'
            );

            $footer = $(
                '<div class="vas-egrn-foot vas-egrn-hidden">' +
                '<span class="vas-egrn-foot-info"></span>' +
                '<div class="vas-egrn-pager">' +
                '<button type="button" class="vas-egrn-pgbtn vas-egrn-prev" aria-label="' + escapeHtml(lbl("VAS_Previous", "Previous")) + '">' + chevL + '</button>' +
                '<span class="vas-egrn-pgtext"></span>' +
                '<button type="button" class="vas-egrn-pgbtn vas-egrn-next" aria-label="' + escapeHtml(lbl("VAS_Next", "Next")) + '">' + chevR + '</button>' +
                '</div>' +
                '</div>'
            );

            $countPill = $header.find('.vas-egrn-count');
            $pageInfo = $footer.find('.vas-egrn-foot-info');
            $pageText = $footer.find('.vas-egrn-pgtext');
            $prevBtn = $footer.find('.vas-egrn-prev');
            $nextBtn = $footer.find('.vas-egrn-next');

            $card.append($header).append($body).append($footer);
            $root.append($card);

            $busy = $('<div class="vas-egrn-busy vas-egrn-hidden"><div class="vis-busyindicatorinnerwrap"><i class="vis_widgetloader"></i></div></div>');
            $root.append($busy);

            $body.on('click', '.vas-egrn-row', function () {
                selectPO(Number($(this).data('poid') || 0));
            });
            $prevBtn.on('click', function () {
                if (!loading && pageNo > 1) { loadExpected(pageNo - 1); }
            });
            $nextBtn.on('click', function () {
                if (!loading && pageNo < totalPages) { loadExpected(pageNo + 1); }
            });
        }

        function loadExpected(page) {
            loading = true;
            showBusy(true);
            if ($prevBtn) { $prevBtn.prop("disabled", true); }
            if ($nextBtn) { $nextBtn.prop("disabled", true); }

            $.ajax({
                url: VIS.Application.contextUrl + 'VAS_092_ExpectedGRNWidget/GetExpectedPurchaseOrders',
                type: 'GET',
                cache: false,
                data: { pageNo: page, pageSize: pageSize },
                success: function (res) {
                    var data = parseResponse(res);
                    loading = false;
                    showBusy(false);

                    if (!data || data.error) {
                        renderRows([], 0);
                        return;
                    }

                    pageNo = Number(data.pageNo || page);
                    totalPages = Number(data.totalPages || 0);
                    renderRows(data.rows || [], Number(data.totalRecords || 0));
                },
                error: function () {
                    loading = false;
                    showBusy(false);
                    renderRows([], 0);
                }
            });
        }

        function fileIcon() {
            return '<svg viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="1.8" stroke-linecap="round" stroke-linejoin="round"><path d="M14 2H6a2 2 0 0 0-2 2v16a2 2 0 0 0 2 2h12a2 2 0 0 0 2-2V8z"/><polyline points="14 2 14 8 20 8"/></svg>';
        }

        function checkIcon() {
            return '<svg viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2.2" stroke-linecap="round" stroke-linejoin="round"><polyline points="20 6 9 17 4 12"/></svg>';
        }

        function renderRows(rows, totalCount) {
            ROWS = rows || [];
            totalRecords = Number(totalCount || 0);
            rowsById = {};

            if ($countPill) {
                if (totalRecords > 0) {
                    $countPill.text(lbl("VAS_GRNCount", "GRN Count") + ' ' + totalRecords).removeClass('vas-egrn-hidden');
                } else {
                    $countPill.addClass('vas-egrn-hidden').empty();
                }
            }

            var $rows = $body.find('.vas-egrn-rows');
            $rows.empty();

            if (ROWS.length === 0) {
                $rows.append('<div class="vas-egrn-empty">' + escapeHtml(lbl("VAS_NoDataAvailable", "No data available")) + '</div>');
                updatePager();
                return;
            }

            for (var i = 0; i < ROWS.length; i++) {
                var r = ROWS[i];
                rowsById[r.poId] = r;

                var valueText = formatMoney(r.poValue, r.stdPrecision, r.curSymbol);

                $rows.append(
                    '<button type="button" class="vas-egrn-row" data-poid="' + escapeHtml(r.poId) + '">' +
                    '<span class="vas-egrn-fi">' + fileIcon() + '</span>' +
                    '<div class="vas-egrn-main">' +
                    '<div class="vas-egrn-top">' +
                    '<span class="vas-egrn-no" title="' + escapeHtml(r.poNo) + '">' + escapeHtml(r.poNo) + '</span>' +
                    '<span class="vas-egrn-qty" title="' + escapeHtml(lbl("VAS_NoOfLines", "No of Lines")) + '">' + escapeHtml(r.lineCount) + '</span>' +
                    '</div>' +
                    '<div class="vas-egrn-mid">' +
                    '<span class="vas-egrn-party" title="' + escapeHtml(r.supplier) + '">' + escapeHtml(r.supplier) + '</span>' +
                    '<span class="vas-egrn-val" title="' + escapeHtml(valueText) + '">' + escapeHtml(valueText) + '</span>' +
                    '</div>' +
                    '<div class="vas-egrn-sub">' +
                    '<span class="vas-egrn-addr" title="' + escapeHtml(r.addressLine) + '">' + escapeHtml(r.addressLine || '-') + '</span>' +
                    '<span class="vas-egrn-wh" title="' + escapeHtml(r.warehouseName) + '">' + escapeHtml(r.warehouseName || '-') + '</span>' +
                    '</div>' +
                    '</div>' +
                    '</button>'
                );
            }

            updatePager();
        }

        function updatePager() {
            var shownPages = totalPages > 0 ? totalPages : 1;
            if ($pageText) {
                $pageText.text(pageNo + ' ' + lbl("VAS_Of", "of") + ' ' + shownPages);
            }
            if ($pageInfo) {
                var from = totalRecords > 0 ? ((pageNo - 1) * pageSize + 1) : 0;
                var to = totalRecords > 0 ? Math.min(pageNo * pageSize, totalRecords) : 0;
                $pageInfo.text(lbl("VAS_Showing", "Showing") + ' ' + from + '-' + to + ' ' + lbl("VAS_Of", "of") + ' ' + totalRecords);
            }
            if ($prevBtn) { $prevBtn.prop('disabled', loading || pageNo <= 1); }
            if ($nextBtn) { $nextBtn.prop('disabled', loading || pageNo >= shownPages); }
            // Footer/pager stays visible at all times, even with no data.
            if ($footer) { $footer.removeClass('vas-egrn-hidden'); }
        }

        function createDialog() {
            $dialog = $(
                '<div class="vas-egrn-dialog vas-egrn-hidden" role="dialog" aria-modal="true">' +
                '<div class="vas-egrn-scrim"></div>' +
                '<div class="vas-egrn-modal">' +
                '<div class="vas-egrn-modal-head">' +
                '<div class="vas-egrn-modal-title-wrap">' +
                '<button type="button" class="vas-egrn-back" aria-label="' + escapeHtml(lbl("VAS_Back", "Back")) + '">' +
                '<svg viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2" stroke-linecap="round" stroke-linejoin="round"><polyline points="15 18 9 12 15 6"/></svg>' +
                '</button>' +
                '<h3 class="vas-egrn-modal-title"></h3>' +
                '<span class="vas-egrn-modal-badge vas-egrn-hidden"></span>' +
                '</div>' +
                '<button type="button" class="vas-egrn-modal-close" aria-label="' + escapeHtml(lbl("VAS_Close", "Close")) + '">' +
                '<svg viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="1.8" stroke-linecap="round" stroke-linejoin="round"><line x1="18" y1="6" x2="6" y2="18"/><line x1="6" y1="6" x2="18" y2="18"/></svg>' +
                '</button>' +
                '</div>' +
                '<div class="vas-egrn-modal-body"></div>' +
                '<div class="vas-egrn-modal-busy vas-egrn-hidden"><div class="vis-busyindicatorinnerwrap"><i class="vis_widgetloader"></i></div></div>' +
                '</div>' +
                '</div>'
            );

            $dialogBody = $dialog.find('.vas-egrn-modal-body');
            $dialogTitle = $dialog.find('.vas-egrn-modal-title');
            $dialogBadge = $dialog.find('.vas-egrn-modal-badge');
            $dialogBusy = $dialog.find('.vas-egrn-modal-busy');

            $dialog.find('.vas-egrn-modal-close').on('click', closeDialog);
            $dialog.find('.vas-egrn-scrim').on('click', closeDialog);
            $dialog.find('.vas-egrn-back').on('click', closeDialog);

            $dialogBody.on('input', '.vas-egrn-rcv-in', validateLines);
            $dialogBody.on('click', '.vas-egrn-create-btn', createGRN);

            $(document).on('keydown.vas-egrn', function (e) {
                if (e.key === 'Escape' && !$dialog.hasClass('vas-egrn-hidden')) { closeDialog(); }
            });

            $('body').append($dialog);
        }

        function selectPO(poId) {
            var po = rowsById[poId];
            if (!po || !$dialog) { return; }

            currentPO = po;
            currentLines = [];

            $dialogTitle.text(po.poNo);
            setBadge(lbl("VAS_NoOfLines", "No of Lines") + ' ' + po.lineCount, "info");

            $dialog.removeClass('vas-egrn-hidden');
            $('body').addClass('vas-egrn-body-lock');
            $dialogBody.html('');
            showDialogBusy(true);

            $.ajax({
                url: VIS.Application.contextUrl + 'VAS_092_ExpectedGRNWidget/GetPurchaseOrderLines',
                type: 'GET',
                cache: false,
                data: { poId: po.poId },
                success: function (res) {
                    var data = parseResponse(res);
                    showDialogBusy(false);
                    if (data.error) {
                        VIS.ADialog.error("", false, data.error, "");
                        return;
                    }
                    currentLines = data.rows || [];
                    renderLineEntry();
                },
                error: function () {
                    showDialogBusy(false);
                    renderLineEntry();
                }
            });
        }

        function fieldHtml(label, value, strong) {
            var shown = value == null || value === "" ? "-" : value;
            return '<div class="vas-egrn-field">' +
                '<div class="vas-egrn-field-lbl">' + escapeHtml(label) + '</div>' +
                '<div class="vas-egrn-field-val' + (strong ? ' strong' : '') + '" title="' + escapeHtml(shown) + '">' + escapeHtml(shown) + '</div>' +
                '</div>';
        }

        function renderLineEntry() {
            if (!currentPO) { return; }

            if (currentLines.length === 0) {
                $dialogBody.html('<div class="vas-egrn-empty">' + escapeHtml(lbl("VAS_NoDataAvailable", "No data available")) + '</div>');
                return;
            }

            var fields =
                '<div class="vas-egrn-form-grid">' +
                fieldHtml(lbl("VAS_ExpectedGRN", "Expected GRN"), currentPO.poNo, true) +
                fieldHtml(lbl("Vendor", "Supplier"), currentPO.supplier) +
                fieldHtml(lbl("VAS_VendorLocation", "Address"), currentPO.addressLine) +
                fieldHtml(lbl("VAS_NoOfLines", "No of Lines"), String(currentLines.length)) +
                '</div>';

            var rows = '';
            for (var i = 0; i < currentLines.length; i++) {
                var line = currentLines[i];
                rows +=
                    '<div class="vas-egrn-rcv-line" data-lineid="' + escapeHtml(line.poLineId) + '" data-openqty="' + escapeHtml(line.openQty) + '">' +
                    '<div class="vas-egrn-rcv-name" title="' + escapeHtml(line.itemName) + '">' + escapeHtml(line.itemName) + '</div>' +
                    '<div class="vas-egrn-rcv-po">' + escapeHtml(formatQty(line.poQty)) + '</div>' +
                    '<input class="vas-egrn-rcv-in" type="number" min="0" max="' + escapeHtml(line.openQty) + '" step="any" value="' + escapeHtml(toInputValue(line.defaultReceivedQty)) + '" aria-label="' + escapeHtml(lbl("VAS_Received", "Received")) + '"/>' +
                    '<div class="vas-egrn-rcv-uom" title="' + escapeHtml(line.uom) + '">' + escapeHtml(line.uom) + '</div>' +
                    '</div>';
            }

            $dialogBody.html(
                fields +
                '<div class="vas-egrn-note">' + fileIcon() + '<span>' + escapeHtml(lbl("VAS_EnterReceivedQtyAgainstLine", "Enter received quantity against each PO line, then create the GRN.")) + '</span></div>' +
                '<div class="vas-egrn-rcv-line vas-egrn-rcv-head">' +
                '<div>' + escapeHtml(lbl("VAS_Item", "Item")) + '</div>' +
                '<div>' + escapeHtml(lbl("VAS_POQty", "PO Qty")) + '</div>' +
                '<div>' + escapeHtml(lbl("VAS_Received", "Received")) + '</div>' +
                '<div>' + escapeHtml(lbl("VAS_Uom", "UOM")) + '</div>' +
                '</div>' +
                '<div class="vas-egrn-lines">' + rows + '</div>' +
                '<div class="vas-egrn-error vas-egrn-hidden"></div>' +
                '<div class="vas-egrn-action"><button type="button" class="vas-egrn-create-btn">' + checkIcon() + '<span>' + escapeHtml(lbl("VAS_SelectAndMakeGRN", "Select & Make GRN")) + '</span></button></div>'
            );

            validateLines();
        }

        function collectLines() {
            var lines = [];
            var invalid = false;
            var message = "";

            $dialogBody.find('.vas-egrn-rcv-line[data-lineid]').each(function () {
                var $row = $(this);
                var lineId = Number($row.data('lineid') || 0);
                var openQty = Number($row.data('openqty') || 0);
                var qty = Number($row.find('.vas-egrn-rcv-in').val() || 0);

                $row.removeClass('invalid');

                if (!isFinite(qty) || qty < 0) {
                    invalid = true;
                    message = lbl("VAS_NegativeReceivedQty", "Received quantity cannot be negative.");
                    $row.addClass('invalid');
                    return;
                }

                if (qty > openQty) {
                    invalid = true;
                    message = lbl("VAS_ReceivedQtyTooHigh", "Received quantity cannot be greater than open quantity.");
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
            var $error = $dialogBody.find('.vas-egrn-error');
            var $button = $dialogBody.find('.vas-egrn-create-btn');

            if (result.invalid) {
                $error.text(result.message).removeClass('vas-egrn-hidden');
                $button.prop('disabled', true);
                return false;
            }

            if (result.lines.length === 0) {
                $error.text(lbl("VAS_ReceivedQtyRequired", "Enter received quantity for at least one line.")).removeClass('vas-egrn-hidden');
                $button.prop('disabled', true);
                return false;
            }

            $error.addClass('vas-egrn-hidden').empty();
            $button.prop('disabled', false);
            return true;
        }

        function createGRN() {
            if (!currentPO || !validateLines()) { return; }

            var result = collectLines();
            var $button = $dialogBody.find('.vas-egrn-create-btn');

            $button.prop('disabled', true);
            showDialogBusy(true);

            $.ajax({
                url: VIS.Application.contextUrl + 'VAS_092_ExpectedGRNWidget/CreateGRN',
                type: 'POST',
                data: {
                    poId: currentPO.poId,
                    linesJson: JSON.stringify(result.lines)
                },
                success: function (res) {
                    var data = parseResponse(res);
                    showDialogBusy(false);
                    if (data.error || data.success === false) {
                        VIS.ADialog.error("", false, data.message || data.error || lbl("VAS_GRNCouldNotBeCreated", "GRN could not be created."), "");
                        $button.prop('disabled', false);
                        return;
                    }

                    closeDialog();
                    $self.refreshWidget();
                    $(document).trigger('VAS_GRNCreated', [data]);
                },
                error: function () {
                    showDialogBusy(false);
                    VIS.ADialog.error("", false, lbl("VAS_GRNCouldNotBeCreated", "GRN could not be created."), "");
                    $button.prop('disabled', false);
                }
            });
        }

        function closeDialog() {
            if (!$dialog) { return; }
            currentPO = null;
            currentLines = [];
            $dialog.addClass('vas-egrn-hidden');
            $('body').removeClass('vas-egrn-body-lock');
        }

        this.refreshWidget = function () {
            pageNo = 1;
            totalPages = 0;
            loadExpected(1);
        };

        this.getRoot = function () { return $root; };

        this.disposeComponent = function () {
            $(document).off('keydown.vas-egrn');
            $('body').removeClass('vas-egrn-body-lock');
            if ($dialog) { $dialog.remove(); $dialog = null; }
            $root.remove();
        };
    };

    VAS.VAS_092_ExpectedGRNWidget.prototype.widgetFirevalueChanged = function (value) {
        if (this.listener) { this.listener.widgetFirevalueChanged(value); }
    };

    VAS.VAS_092_ExpectedGRNWidget.prototype.addChangeListener = function (listener) {
        this.listener = listener;
    };

    VAS.VAS_092_ExpectedGRNWidget.prototype.init = function (windowNo, frame) {
        this.frame = frame;
        this.widgetInfo = frame.widgetInfo;
        this.AD_UserHomeWidgetID = frame.widgetInfo.AD_UserHomeWidgetID;
        this.windowNo = windowNo;
        this.initalize();
        this.frame.getContentGrid().append(this.getRoot());
        ensureDashInlineSizeVar(this.getRoot());
        var ssef = this;
        window.setTimeout(function () {
            ssef.refreshWidget();
        }, 50);
    };

    VAS.VAS_092_ExpectedGRNWidget.prototype.widgetSizeChange = function (widget) {
        this.widgetInfo = widget;
    };

    VAS.VAS_092_ExpectedGRNWidget.prototype.refreshWidget = function () {
        this.refreshWidget();
    };

    VAS.VAS_092_ExpectedGRNWidget.prototype.dispose = function () {
        this.disposeComponent();
        if (this.frame) { this.frame.dispose(); }
        this.frame = null;
        this.windowNo = null;
    };

})(VAS, jQuery);
