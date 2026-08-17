/**
 * Expected Fulfilments Widget (Delivery Order dashboard)
 * Widget number 149.
 * Widget size: 3 columns x 2 rows.
 * Sibling of Due Fulfilments - identical behaviour with the opposite date
 * filter: lists UPCOMING open sales-order fulfilments (promised date today or
 * in the future) as a paged document list; clicking a row opens the "Generate
 * Delivery Order" modal that converts the fulfilment into a DRAFT customer
 * Delivery Order (M_InOut + M_InOutLine). The widget never releases the
 * document. Lists are paged - never an inner scrollbar. Plain DOM/jQuery at
 * the framework boundary only.
 * Backend - VAS_149_ExpectedFulfilmentsWidget/{GetFulfilments, GetModalData,
 *           GetWarehouseData, GetLocatorOnHand, CreateDeliveryOrder}
 * Summary Message Table
 *  #  | Current Text                                  | Message Key
 * ----+-----------------------------------------------+---------------------------
 *  1  | Expected Fulfilments                          | VAS_149_EXF_Title
 *  2  | Fulfilments                                   | VAS_149_EXF_CountLabel
 *  3  | No expected fulfilments                       | VAS_149_EXF_Empty
 *  4  | Showing                                       | VAS_149_EXF_Showing
 *  5  | of                                            | VAS_149_EXF_Of
 *  6  | lines                                         | VAS_149_EXF_Lines
 *  7  | New DO . Draft                                | VAS_149_EXF_NewDoDraft
 *  8  | Document Type                                 | VAS_149_EXF_DocType
 *  9  | Priority                                      | VAS_149_EXF_Priority
 * 10  | Warehouse                                     | VAS_149_EXF_Warehouse
 * 11  | Check the lines to include, adjust quantities if needed, then generate the delivery order. Short lines may be available from another warehouse. | VAS_149_EXF_Instruction
 * 12  | Available                                     | VAS_149_EXF_Available
 * 13  | Short                                         | VAS_149_EXF_Short
 * 14  | Shipment                                      | VAS_149_EXF_Shipment
 * 15  | Shipping Method                               | VAS_149_EXF_ShippingMethod
 * 16  | Freight Category                              | VAS_149_EXF_FreightCategory
 * 17  | No. of Packages                               | VAS_149_EXF_Packages
 * 18  | Gross Weight (kg)                             | VAS_149_EXF_GrossWeight
 * 19  | Tare Weight (kg)                              | VAS_149_EXF_TareWeight
 * 20  | of {t} lines selected                         | VAS_149_EXF_SelSummary
 * 21  | qty                                           | VAS_149_EXF_Qty
 * 22  | DO will be created in Draft                   | VAS_149_EXF_WillBeDraft
 * 23  | Generate Delivery Order                       | VAS_149_EXF_Generate
 * 24  | Delivery Order Created - Draft                | VAS_149_EXF_CreatedTitle
 * 25  | Draft - pending release                       | VAS_149_EXF_DraftPending
 * 26  | Delivery Order could not be created.          | VAS_149_EXF_CreateFailed
 * 27  | Close                                         | VAS_149_EXF_Close
 * 28  | New DO                                        | VAS_149_EXF_NewDoNo
 * 29  | Source Fulfilment                             | VAS_149_EXF_SourceFul
 * 30  | Customer                                      | VAS_149_EXF_Customer
 * 31  | Ship-from                                     | VAS_149_EXF_ShipFrom
 * 32  | Lines                                         | VAS_149_EXF_LinesLbl
 * 33  | Total Qty                                     | VAS_149_EXF_TotalQty
 * 34  | Delivery Order Lines                          | VAS_149_EXF_DoLines
 * 35  | Item                                          | VAS_149_EXF_Item
 * 36  | Locator                                       | VAS_149_EXF_Locator
 * 37  | Status                                        | VAS_149_EXF_Status
 * 38  | Data unavailable                             | VAS_149_EXF_DataUnavailable
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

    VAS.VAS_149_ExpectedFulfilmentsWidget = function () {

        this.frame;
        this.windowNo;

        var $self = this;
        var $root = $('<div class="vas-exf-root">');
        var $dialog;

        // widget list state
        var pageNo = 1;
        var pageSize = 5;
        var totalPages = 0;
        var totalRecords = 0;
        var fulfilments = [];
        var loading = false;

        // modal state
        var modalOrderId = 0;
        var modalHeader = null;
        var modalData = null;
        var lineState = {};
        var lineOrder = [];
        var linePage = 1;
        var linePageSize = 5;
        var selectedWarehouseId = 0;

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

        function formatQty(value) {
            return Number(value || 0).toLocaleString('en-IN', { maximumFractionDigits: 2 });
        }

        var currency = {};

        // Formats an amount in the system currency (the session base currency,
        // returned by the controller) instead of a hardcoded rupee. Falls back to a
        // plain grouped number if the currency is unavailable.
        function formatMoney(value) {
            var n = Number(value || 0);
            var num = n.toLocaleString('en-US', { maximumFractionDigits: 0 });
            var token = currency.iso || currency.symbol || '';
            return token ? (token + ' ' + num) : num;
        }

        /* ================= WIDGET (3x2 list) ================= */

        this.Initalize = function () {
            buildWidget();
            loadFulfilments(1);
        };

        function buildWidget() {
            $root.html(
                '<div class="vas-exf-card vas-widget-bg">' +
                '<div class="vas-exf-head">' +
                '<span class="vas-exf-ico">' + planeIcon() + '</span>' +
                '<span class="vas-exf-title">' + escapeHtml(lbl('VAS_149_EXF_Title', 'Expected Fulfilments')) + '</span>' +
                '<span class="vas-exf-spacer"></span>' +
                '<span class="vas-exf-pill"></span>' +
                '</div>' +
                '<div class="vas-exf-body"></div>' +
                '<div class="vas-exf-foot"></div>' +
                '</div>'
            );
        }

        function loadFulfilments(target) {
            loading = true;
            pageNo = target || 1;
            $.ajax({
                url: VIS.Application.contextUrl + 'VAS_149_ExpectedFulfilmentsWidget/GetFulfilments',
                type: 'GET',
                data: { pageNo: pageNo, pageSize: pageSize },
                success: function (res) {
                    var data = parseResponse(res);
                    if (!data || data.error) { renderError(); return; }
                    fulfilments = data.rows || [];
                    totalPages = Number(data.totalPages || 0);
                    totalRecords = Number(data.totalRecords || 0);
                    currency = data.currency || {};
                    renderList();
                },
                error: function () { renderError(); },
                complete: function () { loading = false; }
            });
        }

        function renderError() {
            $root.find('.vas-exf-pill').text('');
            $root.find('.vas-exf-body').html('<div class="vas-exf-empty">' + escapeHtml(lbl('VAS_149_EXF_DataUnavailable', 'Data unavailable')) + '</div>');
            $root.find('.vas-exf-foot').html('');
        }

        function renderList() {
            $root.find('.vas-exf-pill').text(lbl('VAS_149_EXF_CountLabel', 'Fulfilments') + ' ' + totalRecords);

            var $body = $root.find('.vas-exf-body');
            if (!fulfilments.length) {
                $body.html('<div class="vas-exf-empty">' + escapeHtml(lbl('VAS_149_EXF_Empty', 'No expected fulfilments')) + '</div>');
                $root.find('.vas-exf-foot').html('');
                return;
            }

            var rows = '';
            for (var i = 0; i < fulfilments.length; i++) {
                var f = fulfilments[i];
                rows +=
                    '<button type="button" class="vas-exf-row" data-id="' + escapeHtml(f.fulfilmentId) + '">' +
                    '<span class="vas-exf-fi">' + fileIcon() + '</span>' +
                    '<span class="vas-exf-main">' +
                    '<span class="vas-exf-r1">' +
                    '<span class="vas-exf-no">' + escapeHtml(f.fulfilmentNo) + '</span>' +
                    '<span class="vas-exf-cust">' + escapeHtml(f.customerName) + '</span>' +
                    '<span class="vas-exf-val">' + escapeHtml(formatMoney(f.fulfilmentValue)) + '</span>' +
                    '</span>' +
                    '<span class="vas-exf-r2">' +
                    '<span class="vas-exf-wh">' + escapeHtml(f.warehouseName || '-') + '</span>' +
                    '<span class="vas-exf-addr">' + escapeHtml(f.shipToAddress || '-') + '</span>' +
                    '<span class="vas-exf-lc">' + escapeHtml(f.lineCount) + ' ' + escapeHtml(lbl('VAS_149_EXF_Lines', 'lines')) + '</span>' +
                    '</span>' +
                    '</span>' +
                    '</button>';
            }
            $body.html(rows);

            var first = totalRecords === 0 ? 0 : ((pageNo - 1) * pageSize) + 1;
            var last = Math.min(pageNo * pageSize, totalRecords);
            $root.find('.vas-exf-foot').html(
                '<span class="vas-exf-help">' + escapeHtml(lbl('VAS_149_EXF_Showing', 'Showing')) + ' ' + first + '–' + last + ' ' + escapeHtml(lbl('VAS_149_EXF_Of', 'of')) + ' ' + totalRecords + '</span>' +
                pagerHtml('vas-exf-prev', 'vas-exf-next', pageNo, totalPages)
            );
        }

        /* ================= MODAL ================= */

        function openModal(orderId) {
            modalOrderId = orderId;
            lineState = {}; lineOrder = []; linePage = 1;
            ensureDialog();
            $dialog.removeClass('vas-exf-hidden');
            $('body').addClass('vas-exf-body-lock');
            showModalBusy(true);
            setModalContent('<div class="vas-exf-mbody"></div>');

            $.ajax({
                url: VIS.Application.contextUrl + 'VAS_149_ExpectedFulfilmentsWidget/GetModalData',
                type: 'GET',
                data: { orderId: orderId },
                success: function (res) {
                    var data = parseResponse(res);
                    if (!data || data.error) { VIS.ADialog.error("", false, (data && data.error) || lbl('VAS_149_EXF_DataUnavailable', 'Data unavailable'), ""); closeDialog(); return; }
                    modalHeader = data.header;
                    modalData = data;
                    selectedWarehouseId = Number(data.header.defaultWarehouseId || 0);
                    ingestLines(data.lines);
                    renderModal();
                },
                error: function () { VIS.ADialog.error("", false, lbl('VAS_149_EXF_DataUnavailable', 'Data unavailable'), ""); closeDialog(); },
                complete: function () { showModalBusy(false); }
            });
        }

        function ingestLines(lines) {
            lineOrder = [];
            var kept = {};
            for (var i = 0; i < (lines || []).length; i++) {
                var ln = lines[i];
                var id = ln.orderLineId;
                lineOrder.push(id);
                var prev = lineState[id];
                kept[id] = {
                    orderLineId: id,
                    productId: ln.productId,
                    productName: ln.productName,
                    uomName: ln.uomName || 'Each',
                    requiredQty: Number(ln.requiredQty || 0),
                    onHandQty: Number(ln.onHandQty || 0),
                    locatorId: Number(ln.defaultLocatorId || 0),
                    qty: prev && prev.qtyTouched ? prev.qty : Number(ln.requiredQty || 0),
                    qtyTouched: prev ? prev.qtyTouched : false,
                    stockStatus: ln.stockStatus
                };
                kept[id].checked = (ln.stockStatus === 'Available');
            }
            lineState = kept;
        }

        function ensureDialog() {
            if ($dialog) { return; }
            $dialog = $(
                '<div class="vas-exf-dialog vas-exf-hidden" role="dialog" aria-modal="true">' +
                '<div class="vas-exf-scrim"></div>' +
                '<div class="vas-exf-modal">' +
                '<div class="vas-exf-mtitle">' +
                '<div class="vas-exf-mtitle-l"><h3 class="vas-exf-mt-text"></h3><span class="vas-exf-mt-pill"></span></div>' +
                '<button type="button" class="vas-exf-mclose" aria-label="' + escapeHtml(lbl('VAS_149_EXF_Close', 'Close')) + '">' + closeIcon() + '</button>' +
                '</div>' +
                '<div class="vas-exf-mcontent"></div>' +
                '<div class="vas-exf-mbusy vas-exf-hidden"><div class="vis-busyindicatorinnerwrap"><i class="vis_widgetloader"></i></div></div>' +
                '</div>' +
                '</div>'
            );
            $dialog.find('.vas-exf-mclose').on('click', closeDialog);
            $dialog.find('.vas-exf-scrim').on('click', closeDialog);
            $(document).on('keydown.vas-exf', function (e) {
                if (e.key === 'Escape' && $dialog && !$dialog.hasClass('vas-exf-hidden')) { closeDialog(); }
            });

            var $c = $dialog.find('.vas-exf-mcontent');
            $c.on('change', '.vas-exf-f-wh', onWarehouseChange);
            $c.on('click', '.vas-exf-chk', function () { onToggleLine(Number($(this).data('id'))); });
            $c.on('change', '.vas-exf-loc', function () { onLocatorChange(Number($(this).data('id')), Number($(this).val())); });
            $c.on('input', '.vas-exf-qty', function () { onQtyChange(Number($(this).data('id')), $(this).val()); });
            $c.on('click', '.vas-exf-lprev', function () { if (linePage > 1) { linePage--; renderLineList(); } });
            $c.on('click', '.vas-exf-lnext', function () { if (linePage < lineTotalPages()) { linePage++; renderLineList(); } });
            $c.on('click', '.vas-exf-gen', onGenerate);
            $c.on('click', '.vas-exf-doneclose', function () { closeDialog(); $self.refreshWidget(); });

            $('body').append($dialog);
        }

        function setModalContent(html) { $dialog.find('.vas-exf-mcontent').html(html); }
        function showModalBusy(show) { if ($dialog) { $dialog.find('.vas-exf-mbusy').toggleClass('vas-exf-hidden', !show); } }

        function closeDialog() {
            if (!$dialog) { return; }
            $dialog.addClass('vas-exf-hidden');
            $('body').removeClass('vas-exf-body-lock');
        }

        function optionList(items, idKey, nameKey, selectedVal) {
            var out = '';
            for (var i = 0; i < (items || []).length; i++) {
                var v = items[i][idKey];
                var sel = (String(v) === String(selectedVal)) ? ' selected' : '';
                out += '<option value="' + escapeHtml(v) + '"' + sel + '>' + escapeHtml(items[i][nameKey]) + '</option>';
            }
            return out;
        }

        function renderModal() {
            $dialog.find('.vas-exf-mt-text').text((modalHeader.fulfilmentNo || '') + ' - ' + (modalHeader.customerName || ''));
            $dialog.find('.vas-exf-mt-pill').text(lbl('VAS_149_EXF_NewDoDraft', 'New DO · Draft'));

            var defaultPriority = modalHeader.defaultPriorityRule || '5';
            var header =
                '<div class="vas-exf-optrow">' +
                field3(lbl('VAS_149_EXF_DocType', 'Document Type'), '<select class="vas-exf-f vas-exf-f-doc">' + optionList(modalData.docTypes, 'id', 'name', '') + '</select>') +
                field3(lbl('VAS_149_EXF_Priority', 'Priority'), '<select class="vas-exf-f vas-exf-f-pri">' + optionList(modalData.priorities, 'value', 'name', defaultPriority) + '</select>') +
                field3(lbl('VAS_149_EXF_Warehouse', 'Warehouse'), '<select class="vas-exf-f vas-exf-f-wh">' + optionList(modalData.warehouses, 'id', 'name', selectedWarehouseId) + '</select>') +
                '</div>';

            var note = '<div class="vas-exf-note"><span>' + escapeHtml(lbl('VAS_149_EXF_Instruction', 'Check the lines to include, adjust quantities if needed, then generate the delivery order. Short lines may be available from another warehouse.')) + '</span></div>';

            var shipment =
                '<div class="vas-exf-shhead">' + escapeHtml(lbl('VAS_149_EXF_Shipment', 'Shipment')) + '</div>' +
                '<div class="vas-exf-shiprow">' +
                field5(lbl('VAS_149_EXF_ShippingMethod', 'Shipping Method'), '<select class="vas-exf-f vas-exf-f-ship">' + optionList(modalData.shippingMethods, 'value', 'name', 'D') + '</select>') +
                field5(lbl('VAS_149_EXF_FreightCategory', 'Freight Category'), '<select class="vas-exf-f vas-exf-f-frt"><option value="0"></option>' + optionList(modalData.freightCategories, 'id', 'name', '') + '</select>') +
                field5(lbl('VAS_149_EXF_Packages', 'No. of Packages'), '<input type="number" min="0" class="vas-exf-f vas-exf-f-pkg" placeholder="0" />') +
                field5(lbl('VAS_149_EXF_GrossWeight', 'Gross Weight (kg)'), '<input type="number" min="0" step="any" class="vas-exf-f vas-exf-f-gw" placeholder="0.00" />') +
                field5(lbl('VAS_149_EXF_TareWeight', 'Tare Weight (kg)'), '<input type="number" min="0" step="any" class="vas-exf-f vas-exf-f-tw" placeholder="0.00" />') +
                '</div>';

            setModalContent(
                '<div class="vas-exf-mbody">' +
                header + note +
                '<div class="vas-exf-lines"></div>' +
                '<div class="vas-exf-linepager"></div>' +
                shipment +
                '</div>' +
                '<div class="vas-exf-mfoot">' +
                '<div class="vas-exf-sum"></div>' +
                '<button type="button" class="vas-exf-gen">' + sendIcon() + '<span>' + escapeHtml(lbl('VAS_149_EXF_Generate', 'Generate Delivery Order')) + '</span></button>' +
                '</div>'
            );
            renderLineList();
        }

        function lineTotalPages() { return Math.max(1, Math.ceil(lineOrder.length / linePageSize)); }

        function renderLineList() {
            var tp = lineTotalPages();
            if (linePage > tp) { linePage = tp; }
            var start = (linePage - 1) * linePageSize;
            var pageIds = lineOrder.slice(start, start + linePageSize);

            var rows = '';
            for (var i = 0; i < pageIds.length; i++) {
                rows += lineRowHtml(lineState[pageIds[i]]);
            }
            $dialog.find('.vas-exf-lines').html(rows);

            var $lp = $dialog.find('.vas-exf-linepager');
            if (lineOrder.length > linePageSize) {
                var first = start + 1;
                var last = Math.min(start + linePageSize, lineOrder.length);
                $lp.html(
                    '<span class="vas-exf-help">' + escapeHtml(lbl('VAS_149_EXF_Showing', 'Showing')) + ' ' + first + '–' + last + ' ' + escapeHtml(lbl('VAS_149_EXF_Of', 'of')) + ' ' + lineOrder.length + ' ' + escapeHtml(lbl('VAS_149_EXF_Lines', 'lines')) + '</span>' +
                    pagerHtml('vas-exf-lprev', 'vas-exf-lnext', linePage, tp)
                );
            } else {
                $lp.html('');
            }
            updateSummary();
        }

        function badgeFor(line) {
            var qty = Number(line.qty || 0);
            var ok = Number(line.onHandQty || 0) >= qty && qty > 0;
            return ok
                ? '<span class="vas-exf-badge ok">' + escapeHtml(lbl('VAS_149_EXF_Available', 'Available')) + '</span>'
                : '<span class="vas-exf-badge bad">' + escapeHtml(lbl('VAS_149_EXF_Short', 'Short')) + '</span>';
        }

        function lineRowHtml(line) {
            var disabled = line.checked ? '' : ' disabled';
            var rowCls = 'vas-exf-line' + (line.checked ? (Number(line.onHandQty) >= Number(line.qty) ? ' avail' : ' short') : ' off');
            var locOpts = optionList(modalData.locators, 'locatorId', 'locatorName', line.locatorId);
            return '<div class="' + rowCls + '" data-id="' + escapeHtml(line.orderLineId) + '">' +
                '<button type="button" class="vas-exf-chk' + (line.checked ? ' on' : '') + '" data-id="' + escapeHtml(line.orderLineId) + '" aria-label="Include ' + escapeHtml(line.productName) + '">' + (line.checked ? checkIcon() : '') + '</button>' +
                                '<span class="vas-exf-lname" title="' + escapeHtml(line.productName) + '">' + escapeHtml(line.productName) + '</span>' +
                badgeFor(line) +
                '<select class="vas-exf-loc" data-id="' + escapeHtml(line.orderLineId) + '" aria-label="Locator for ' + escapeHtml(line.productName) + '"' + disabled + '>' + locOpts + '</select>' +
                '<span class="vas-exf-qtywrap"><input type="number" min="1" class="vas-exf-qty" data-id="' + escapeHtml(line.orderLineId) + '" value="' + escapeHtml(line.qty) + '" aria-label="Quantity for ' + escapeHtml(line.productName) + '"' + disabled + ' />' +
                '<span class="vas-exf-uom">' + escapeHtml(line.uomName) + '</span></span>' +
                '</div>';
        }

        function onToggleLine(id) {
            var line = lineState[id];
            if (!line) { return; }
            line.checked = !line.checked;
            renderLineList();
        }

        function onQtyChange(id, val) {
            var line = lineState[id];
            if (!line) { return; }
            var n = Number(val);
            line.qty = isFinite(n) && n > 0 ? n : 0;
            line.qtyTouched = true;
            refreshLineRow(id);
            updateSummary();
        }

        function onLocatorChange(id, locatorId) {
            var line = lineState[id];
            if (!line) { return; }
            line.locatorId = locatorId;
            $.ajax({
                url: VIS.Application.contextUrl + 'VAS_149_ExpectedFulfilmentsWidget/GetLocatorOnHand',
                type: 'GET',
                data: { productId: line.productId, locatorId: locatorId },
                success: function (res) {
                    var data = parseResponse(res);
                    line.onHandQty = Number((data && data.onHandQty) || 0);
                    refreshLineRow(id);
                    updateSummary();
                }
            });
        }

        function refreshLineRow(id) {
            var line = lineState[id];
            var $row = $dialog.find('.vas-exf-line[data-id="' + id + '"]');
            if (!$row.length) { return; }
            $row.removeClass('avail short off');
            $row.addClass(line.checked ? (Number(line.onHandQty) >= Number(line.qty) ? 'avail' : 'short') : 'off');
            $row.find('.vas-exf-badge').replaceWith(badgeFor(line));
        }

        function onWarehouseChange() {
            var whId = Number($dialog.find('.vas-exf-f-wh').val());
            if (!whId) { return; }
            selectedWarehouseId = whId;
            showModalBusy(true);
            $.ajax({
                url: VIS.Application.contextUrl + 'VAS_149_ExpectedFulfilmentsWidget/GetWarehouseData',
                type: 'GET',
                data: { orderId: modalOrderId, warehouseId: whId },
                success: function (res) {
                    var data = parseResponse(res);
                    if (!data || data.error) { return; }
                    modalData.locators = data.locators || [];
                    ingestLines(data.lines);
                    linePage = 1;
                    renderLineList();
                },
                complete: function () { showModalBusy(false); }
            });
        }

        function selectedLines() {
            var out = [];
            for (var i = 0; i < lineOrder.length; i++) {
                var line = lineState[lineOrder[i]];
                if (line && line.checked && Number(line.qty) > 0) {
                    out.push({ orderLineId: line.orderLineId, locatorId: line.locatorId, qty: Number(line.qty) });
                }
            }
            return out;
        }

        function updateSummary() {
            var sel = selectedLines();
            var total = lineOrder.length;
            var qtySum = 0;
            for (var i = 0; i < sel.length; i++) { qtySum += sel[i].qty; }
            $dialog.find('.vas-exf-sum').html(
                '<b>' + sel.length + '</b> ' + escapeHtml(lbl('VAS_149_EXF_SelSummary', 'of {t} lines selected').replace('{t}', total)) +
                ' · ' + escapeHtml(lbl('VAS_149_EXF_Qty', 'qty')) + ' <b>' + formatQty(qtySum) + '</b>' +
                ' · ' + escapeHtml(lbl('VAS_149_EXF_WillBeDraft', 'DO will be created in Draft'))
            );
            $dialog.find('.vas-exf-gen').prop('disabled', sel.length === 0);
        }

        function onGenerate() {
            var sel = selectedLines();
            if (sel.length === 0) { return; }

            var payload = {
                orderId: modalOrderId,
                warehouseId: selectedWarehouseId,
                docTypeId: Number($dialog.find('.vas-exf-f-doc').val() || 0),
                priorityRule: $dialog.find('.vas-exf-f-pri').val() || '',
                deliveryViaRule: $dialog.find('.vas-exf-f-ship').val() || '',
                freightCategoryId: Number($dialog.find('.vas-exf-f-frt').val() || 0),
                noPackages: Number($dialog.find('.vas-exf-f-pkg').val() || 0),
                grossWeight: Number($dialog.find('.vas-exf-f-gw').val() || 0),
                tareWeight: Number($dialog.find('.vas-exf-f-tw').val() || 0),
                linesJson: JSON.stringify(sel)
            };

            $dialog.find('.vas-exf-gen').prop('disabled', true);
            showModalBusy(true);
            $.ajax({
                url: VIS.Application.contextUrl + 'VAS_149_ExpectedFulfilmentsWidget/CreateDeliveryOrder',
                type: 'POST',
                data: payload,
                success: function (res) {
                    var data = parseResponse(res);
                    if (!data || data.error || data.success === false) {
                        VIS.ADialog.error("", false, (data && (data.message || data.error)) || lbl('VAS_149_EXF_CreateFailed', 'Delivery Order could not be created.'), "");
                        $dialog.find('.vas-exf-gen').prop('disabled', false);
                        return;
                    }
                    renderConfirmation(data, sel);
                },
                error: function () {
                    VIS.ADialog.error("", false, lbl('VAS_149_EXF_CreateFailed', 'Delivery Order could not be created.'), "");
                    $dialog.find('.vas-exf-gen').prop('disabled', false);
                },
                complete: function () { showModalBusy(false); }
            });
        }

        function renderConfirmation(result, sel) {
            $dialog.find('.vas-exf-mt-text').text(lbl('VAS_149_EXF_CreatedTitle', 'Delivery Order Created - Draft'));
            $dialog.find('.vas-exf-mt-pill').text(lbl('VAS_149_EXF_DraftPending', 'Draft - pending release'));

            var qtySum = 0;
            for (var i = 0; i < sel.length; i++) { qtySum += sel[i].qty; }

            var whName = $dialog.find('.vas-exf-f-wh option:selected').text();
            var docName = $dialog.find('.vas-exf-f-doc option:selected').text();
            var priName = $dialog.find('.vas-exf-f-pri option:selected').text();

            var grid =
                '<div class="vas-exf-cgrid">' +
                cfield(lbl('VAS_149_EXF_NewDoNo', 'New DO'), result.deliveryOrderNo, true) +
                cfield(lbl('VAS_149_EXF_Status', 'Status'), lbl('VAS_149_EXF_DraftPending', 'Draft - pending release'), true) +
                cfield(lbl('VAS_149_EXF_SourceFul', 'Source Fulfilment'), modalHeader.fulfilmentNo) +
                cfield(lbl('VAS_149_EXF_Customer', 'Customer'), modalHeader.customerName) +
                cfield(lbl('VAS_149_EXF_ShipFrom', 'Ship-from'), whName) +
                cfield(lbl('VAS_149_EXF_DocType', 'Document Type'), docName) +
                cfield(lbl('VAS_149_EXF_Priority', 'Priority'), priName) +
                cfield(lbl('VAS_149_EXF_LinesLbl', 'Lines'), result.lineCount + ' ' + lbl('VAS_149_EXF_Of', 'of') + ' ' + lineOrder.length) +
                cfield(lbl('VAS_149_EXF_TotalQty', 'Total Qty'), formatQty(result.totalQty || qtySum)) +
                '</div>';

            var lineRows = '';
            for (var j = 0; j < sel.length; j++) {
                var line = lineState[sel[j].orderLineId];
                var locName = '';
                for (var k = 0; k < modalData.locators.length; k++) {
                    if (Number(modalData.locators[k].locatorId) === Number(line.locatorId)) { locName = modalData.locators[k].locatorName; break; }
                }
                lineRows +=
                    '<tr><td class="s">' + escapeHtml(line.productName) + '</td>' +
                    '<td>' + escapeHtml(locName || '-') + '</td>' +
                    '<td class="r">' + escapeHtml(formatQty(sel[j].qty)) + '</td>' +
                    '<td>' + (Number(line.onHandQty) >= Number(sel[j].qty) ? escapeHtml(lbl('VAS_149_EXF_Available', 'Available')) : escapeHtml(lbl('VAS_149_EXF_Short', 'Short'))) + '</td></tr>';
            }

            setModalContent(
                '<div class="vas-exf-mbody">' +
                grid +
                '<div class="vas-exf-chead">' + escapeHtml(lbl('VAS_149_EXF_DoLines', 'Delivery Order Lines')) + '</div>' +
                '<table class="vas-exf-ctable"><thead><tr>' +
                '<th>' + escapeHtml(lbl('VAS_149_EXF_Item', 'Item')) + '</th>' +
                '<th>' + escapeHtml(lbl('VAS_149_EXF_Locator', 'Locator')) + '</th>' +
                '<th class="r">' + escapeHtml(lbl('VAS_149_EXF_Qty', 'Qty')) + '</th>' +
                '<th>' + escapeHtml(lbl('VAS_149_EXF_Status', 'Status')) + '</th>' +
                '</tr></thead><tbody>' + lineRows + '</tbody></table>' +
                '</div>' +
                '<div class="vas-exf-mfoot vas-exf-mfoot-end">' +
                '<button type="button" class="vas-exf-doneclose vas-exf-gen">' + escapeHtml(lbl('VAS_149_EXF_Close', 'Close')) + '</button>' +
                '</div>'
            );
        }

        /* ================= helpers / icons ================= */

        function field3(label, control) {
            return '<div class="vas-exf-field"><label class="vas-exf-flabel">' + escapeHtml(label) + '</label>' + control + '</div>';
        }
        function field5(label, control) { return field3(label, control); }
        function cfield(label, value, strong) {
            return '<div class="vas-exf-cf"><div class="vas-exf-cl">' + escapeHtml(label) + '</div><div class="vas-exf-cv' + (strong ? ' s' : '') + '">' + escapeHtml(value || '-') + '</div></div>';
        }
        function pagerHtml(prevCls, nextCls, page, pages) {
            return '<span class="vas-exf-pager">' +
                '<button type="button" class="vas-exf-pgbtn ' + prevCls + '"' + (page <= 1 ? ' disabled' : '') + '>' + chevL() + '</button>' +
                '<span class="vas-exf-pgtext">' + page + ' ' + escapeHtml(lbl('VAS_149_EXF_Of', 'of')) + ' ' + Math.max(pages, 1) + '</span>' +
                '<button type="button" class="vas-exf-pgbtn ' + nextCls + '"' + (page >= pages ? ' disabled' : '') + '>' + chevR() + '</button>' +
                '</span>';
        }
        function planeIcon() { return '<svg viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2" stroke-linecap="round" stroke-linejoin="round"><line x1="22" y1="2" x2="11" y2="13"/><polygon points="22 2 15 22 11 13 2 9 22 2"/></svg>'; }
        function fileIcon() { return '<svg viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="1.8" stroke-linecap="round" stroke-linejoin="round"><path d="M14 2H6a2 2 0 0 0-2 2v16a2 2 0 0 0 2 2h12a2 2 0 0 0 2-2V8z"/><polyline points="14 2 14 8 20 8"/></svg>'; }
        function checkIcon() { return '<svg viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2.4" stroke-linecap="round" stroke-linejoin="round"><polyline points="20 6 9 17 4 12"/></svg>'; }
        function sendIcon() { return '<svg viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2" stroke-linecap="round" stroke-linejoin="round"><line x1="22" y1="2" x2="11" y2="13"/><polygon points="22 2 15 22 11 13 2 9 22 2"/></svg>'; }
        function closeIcon() { return '<svg viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="1.8" stroke-linecap="round" stroke-linejoin="round"><line x1="18" y1="6" x2="6" y2="18"/><line x1="6" y1="6" x2="18" y2="18"/></svg>'; }
        function chevL() { return '<svg viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2" stroke-linecap="round" stroke-linejoin="round"><polyline points="15 18 9 12 15 6"/></svg>'; }
        function chevR() { return '<svg viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2" stroke-linecap="round" stroke-linejoin="round"><polyline points="9 18 15 12 9 6"/></svg>'; }

        /* ================= events ================= */

        $root.on('click', '.vas-exf-row', function () { openModal(Number($(this).data('id'))); });
        $root.on('click', '.vas-exf-prev', function () { if (pageNo > 1) { loadFulfilments(pageNo - 1); } });
        $root.on('click', '.vas-exf-next', function () { if (pageNo < totalPages) { loadFulfilments(pageNo + 1); } });

        /* ================= lifecycle ================= */

        this.refreshWidget = function () { loadFulfilments(1); };
        this.getRoot = function () { return $root; };
        this.disposeComponent = function () {
            $(document).off('keydown.vas-exf');
            $('body').removeClass('vas-exf-body-lock');
            if ($dialog) { $dialog.remove(); $dialog = null; }
            $root.remove();
        };
    };

    VAS.VAS_149_ExpectedFulfilmentsWidget.prototype.widgetFirevalueChanged = function (value) {
        if (this.listener) { this.listener.widgetFirevalueChanged(value); }
    };
    VAS.VAS_149_ExpectedFulfilmentsWidget.prototype.addChangeListener = function (listener) { this.listener = listener; };
    VAS.VAS_149_ExpectedFulfilmentsWidget.prototype.init = function (windowNo, frame) {
        this.frame = frame;
        this.AD_UserHomeWidgetID = frame.widgetInfo.AD_UserHomeWidgetID;
        this.windowNo = windowNo;
        this.Initalize();
        this.frame.getContentGrid().append(this.getRoot());
        ensureDashInlineSizeVar(this.getRoot());
    };
    VAS.VAS_149_ExpectedFulfilmentsWidget.prototype.widgetSizeChange = function (height, width) { };
    VAS.VAS_149_ExpectedFulfilmentsWidget.prototype.refreshWidget = function () { this.refreshWidget(); };
    VAS.VAS_149_ExpectedFulfilmentsWidget.prototype.dispose = function () {
        this.disposeComponent();
        if (this.frame) { this.frame.dispose(); }
        this.frame = null;
    };

})(VAS, jQuery);
