/**
 * VAS_216_POQueueWidget
 * Purchase Order Dashboard — Widget 14: PO Queue
 * Widget size: 6 columns x 3 rows (6x3 Queue Table).
 * Operational worklist of live purchase orders ordered by expected delivery date.
 * Excludes completed, closed, voided, and reversed documents (CO, CL, VO, RE).
 *
 * Summary Message Table
 *  # | Current Text                                            | Message Key
 * ---+---------------------------------------------------------+-----------------------------------
 *  1 | PO Queue                                                | VAS_POQueue
 *  2 | Live purchase orders by expected delivery date          | VAS_POQueueSubtitle
 *  3 | Month                                                   | VAS_Month
 *  4 | Year                                                    | VAS_Year
 *  5 | PO No                                                   | VAS_PONumber
 *  6 | PO date                                                 | VAS_PODate
 *  7 | Vendor                                                  | VAS_Vendor
 *  8 | Warehouse                                               | VAS_Warehouse
 *  9 | Requisition                                             | VAS_Requisition
 * 10 | Representative                                          | VAS_Representative
 * 11 | Expected                                                | VAS_Expected
 * 12 | Value                                                   | VAS_Value
 * 13 | Status                                                  | VAS_Status
 * 14 | Showing                                                 | VAS_Showing
 * 15 | of                                                      | VAS_Of
 * 16 | select a PO number to open the record                   | VAS_SelectPORecordHelper
 * 17 | Previous                                                | VAS_Previous
 * 18 | Next                                                    | VAS_Next
 * 19 | No live purchase orders found for the selected period   | VAS_NoLivePOsFound
 * 20 | Loading...                                              | VAS_Loading
 * 21 | Couldn't load data                                      | VAS_CouldntLoad
 * 22 | Expected on                                             | VAS_ExpectedOn
 * 23 | PO value                                                | VAS_POValue
 * 24 | Created by                                              | VAS_CreatedBy
 * 25 | Document status                                         | VAS_DocumentStatus
 * 26 | Delivery status                                         | VAS_DeliveryStatus
 * 27 | Purchase order lines                                    | VAS_PurchaseOrderLines
 * 28 | Product                                                 | VAS_Product
 * 29 | Attribute                                               | VAS_Attribute
 * 30 | UoM                                                     | VAS_UOM
 * 31 | Ordered                                                 | VAS_Ordered
 * 32 | Received                                                | VAS_Received
 * 33 | Pending                                                 | VAS_Pending
 * 34 | Rate                                                    | VAS_Rate
 * 35 | Amount                                                  | VAS_Amount
 * 36 | Line status                                             | VAS_LineStatus
 * 37 | Partial                                                 | VAS_Partial
 * 38 | Partial received                                        | VAS_PartialReceived
 * 39 | Completed                                               | VAS_Completed
 * 40 | Closed                                                  | VAS_Closed
 * 41 | Drafted                                                 | VAS_Drafted
 * 42 | In process                                              | VAS_InProcess
 * 43 | Voided                                                  | VAS_Voided
 * 44 | Fully delivered                                         | VAS_FullyDelivered
 * 45 | Not applicable                                          | VAS_NotApplicable
 * 46 | Lines                                                   | VAS_Lines
 * 47 | qty ordered                                             | VAS_QtyOrdered
 * 48 | Open Record                                             | VAS_OpenRecord
 * 49 | Back                                                    | VAS_Back
 * 50 | Close                                                   | VAS_Close
 * 51 | lines of                                                | VAS_LinesOf
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

    function lbl(key, fallback) {
        var translated = VIS.Msg.getMsg(key);
        return (translated && translated.charAt(0) !== '[') ? translated : fallback;
    }

    function esc(s) {
        return String(s == null ? '' : s)
            .replace(/&/g, '&amp;')
            .replace(/</g, '&lt;')
            .replace(/>/g, '&gt;')
            .replace(/"/g, '&quot;')
            .replace(/'/g, '&#039;');
    }

    function parseResponse(res) {
        var data = res;
        if (typeof data === 'string' && data.length) { data = JSON.parse(data); }
        if (typeof data === 'string' && data.length) { data = JSON.parse(data); }
        return data || {};
    }

    function formatNumber(v) {
        var n = Number(v || 0);
        if (!isFinite(n)) { n = 0; }
        return Math.round(n).toLocaleString(window.navigator.language);
    }

    function formatDecimal(v, decimals) {
        var n = Number(v || 0);
        if (!isFinite(n)) { n = 0; }
        return n.toLocaleString(window.navigator.language, {
            minimumFractionDigits: decimals != null ? decimals : 0,
            maximumFractionDigits: decimals != null ? decimals : 2
        });
    }

    function formatMoney(amount, curSymbol, curIso, precision) {
        var val = Number(amount || 0);
        if (!isFinite(val)) { val = 0; }

        var p = precision != null ? precision : 2;
        var sym = curSymbol || curIso || '';

        if (Math.abs(val) >= 10000000) {
            return (sym ? sym + ' ' : '') + (val / 10000000).toFixed(2) + ' Cr';
        }
        if (Math.abs(val) >= 100000) {
            return (sym ? sym + ' ' : '') + (val / 100000).toFixed(2) + ' L';
        }

        var formatted = val.toLocaleString(window.navigator.language, {
            minimumFractionDigits: p,
            maximumFractionDigits: p
        });

        return sym ? (sym + ' ' + formatted) : formatted;
    }

    function formatDateShort(dateStr) {
        if (!dateStr) { return '—'; }
        var dt = new Date(dateStr);
        if (isNaN(dt.getTime())) { return dateStr; }
        var day = ('0' + dt.getDate()).slice(-2);
        var mName = dt.toLocaleString(window.navigator.language || 'default', { month: 'short' });
        return day + ' ' + mName;
    }

    function formatDateFull(dateStr) {
        if (!dateStr) { return '—'; }
        var dt = new Date(dateStr);
        if (isNaN(dt.getTime())) { return dateStr; }
        var day = ('0' + dt.getDate()).slice(-2);
        var mName = dt.toLocaleString(window.navigator.language || 'default', { month: 'short' });
        return day + ' ' + mName + ' ' + dt.getFullYear();
    }

    function getDocStatusInfo(docStatus) {
        var code = (docStatus || '').toUpperCase();
        switch (code) {
            case 'DR':
                return { text: lbl('VAS_Drafted', 'Drafted'), chip: 'vas-216-chip-neutral' };
            case 'IP':
                return { text: lbl('VAS_InProcess', 'In process'), chip: 'vas-216-chip-prop' };
            case 'CO':
                return { text: lbl('VAS_Completed', 'Completed'), chip: 'vas-216-chip-ok' };
            case 'CL':
                return { text: lbl('VAS_Closed', 'Closed'), chip: 'vas-216-chip-ok' };
            case 'VO':
                return { text: lbl('VAS_Voided', 'Voided'), chip: 'vas-216-chip-risk' };
            case 'RE':
                return { text: lbl('VAS_Reversed', 'Reversed'), chip: 'vas-216-chip-risk' };
            case 'WC':
                return { text: lbl('VAS_WaitingConfirmation', 'Waiting Confirmation'), chip: 'vas-216-chip-prop' };
            case 'WP':
                return { text: lbl('VAS_WaitingPayment', 'Waiting Payment'), chip: 'vas-216-chip-prop' };
            default:
                var translated = VIS.Msg.getMsg(docStatus);
                return { text: (translated && translated.charAt(0) !== '[') ? translated : docStatus, chip: 'vas-216-chip-neutral' };
        }
    }

    function getDeliveryStatusInfo(status) {
        var st = (status || '').toLowerCase();
        if (st === 'fully delivered' || st === 'full') {
            return { text: lbl('VAS_FullyDelivered', 'Fully delivered'), chip: 'vas-216-chip-ok' };
        } else if (st === 'partial') {
            return { text: lbl('VAS_Partial', 'Partial'), chip: 'vas-216-chip-warn' };
        } else if (st === 'not applicable' || st === 'na') {
            return { text: lbl('VAS_NotApplicable', 'Not applicable'), chip: 'vas-216-chip-neutral' };
        } else {
            return { text: lbl('VAS_Pending', 'Pending'), chip: 'vas-216-chip-neutral' };
        }
    }

    function getLineStatusInfo(status) {
        var st = (status || '').toLowerCase();
        if (st === 'received') {
            return { text: lbl('VAS_Received', 'Received'), chip: 'vas-216-chip-ok' };
        } else if (st === 'partial received' || st === 'partial') {
            return { text: lbl('VAS_PartialReceived', 'Partial received'), chip: 'vas-216-chip-warn' };
        } else if (st === 'drafted') {
            return { text: lbl('VAS_Drafted', 'Drafted'), chip: 'vas-216-chip-neutral' };
        } else if (st === 'voided') {
            return { text: lbl('VAS_Voided', 'Voided'), chip: 'vas-216-chip-risk' };
        } else {
            return { text: lbl('VAS_Pending', 'Pending'), chip: 'vas-216-chip-neutral' };
        }
    }

    var ICON_BACK = '<svg width="20" height="20" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2" stroke-linecap="round" stroke-linejoin="round"><path d="M19 12H5M12 19l-7-7 7-7"/></svg>';
    var ICON_CLOSE = '<svg width="20" height="20" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2" stroke-linecap="round"><path d="M18 6 6 18M6 6l12 12"/></svg>';
    var ICON_PREV = '<svg viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2.4" stroke-linecap="round"><polyline points="15 18 9 12 15 6"/></svg>';
    var ICON_NEXT = '<svg viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2.4" stroke-linecap="round"><polyline points="9 18 15 12 9 6"/></svg>';
    var ICON_EMPTY = '<svg class="vas-216-empty-icon" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-linecap="round" stroke-linejoin="round"><rect width="18" height="18" x="3" y="3" rx="2"/><path d="M3 9h18M9 21V9"/></svg>';

    VAS.VAS_216_POQueueWidget = function () {

        this.frame = null;
        this.windowNo = 0;

        var $self = this;
        var $root = $('<div class="vas-216-root">');
        var $card = null;
        var $monthSelect = null;
        var $yearSelect = null;
        var $tableHead = null;
        var $tableBody = null;
        var $helper = null;
        var $pageText = null;
        var $prevBtn = null;
        var $nextBtn = null;
        var $busy = null;

        var currentPage = 1;
        var pageSize = 7;
        var totalRecords = 0;
        var totalPages = 1;
        var currentMonth = new Date().getMonth() + 1;
        var currentYear = new Date().getFullYear();

        var baseCurrency = { CurrencyID: 0, CurSymbol: '', ISO_Code: '', StdPrecision: 2 };
        var loadedRecords = [];

        // Modal engine state
        var $modalHost = null;
        var modalStack = [];
        var currentModalCfg = null;
        var MT = {};
        var MT_SEQ = 0;
        var MAX_MODAL_ROWS = 10;

        function showBusy(show) {
            if (!$busy) { return; }
            $busy.toggleClass('vas-216-hidden', !show);
        }

        this.Initalize = function () {
            createWidgetHtml();
            setupResizeObserver();
            loadQueueData();
        };

        function setupResizeObserver() {
            if (typeof ResizeObserver === 'undefined') { return; }
            try {
                var ro = new ResizeObserver(function (entries) {
                    for (var i = 0; i < entries.length; i++) {
                        var width = entries[i].contentRect.width;
                        if (width > 0 && $root[0]) {
                            $root[0].style.setProperty('--widget-inline-size', width + 'px');
                        }
                    }
                });
                ro.observe($root[0]);
            } catch (e) { }
        }

        function createWidgetHtml() {
            var title = lbl('VAS_POQueue', 'PO Queue');
            var subtitle = lbl('VAS_POQueueSubtitle', 'Live purchase orders by expected delivery date');

            $card = $(
                '<div class="vas-216-card">' +
                    '<div class="vas-216-head">' +
                        '<div class="vas-216-head-txt">' +
                            '<p class="vas-216-title">' + esc(title) + '</p>' +
                            '<p class="vas-216-sub">' + esc(subtitle) + '</p>' +
                        '</div>' +
                        '<div class="vas-216-mfilter">' +
                            '<select class="vas-216-msel vas-216-month-sel" aria-label="' + esc(lbl('VAS_Month', 'Month')) + '"></select>' +
                            '<select class="vas-216-msel vas-216-year-sel" aria-label="' + esc(lbl('VAS_Year', 'Year')) + '"></select>' +
                        '</div>' +
                    '</div>' +
                    '<div class="vas-216-tbl">' +
                        '<div class="vas-216-trow vas-216-thead"></div>' +
                        '<div class="vas-216-tbody"></div>' +
                    '</div>' +
                    '<div class="vas-216-wfoot">' +
                        '<span class="vas-216-helper"></span>' +
                        '<div class="vas-216-pager">' +
                            '<button type="button" class="vas-216-pbtn vas-216-pbtn-prev" data-dir="-1" aria-label="' + esc(lbl('VAS_Previous', 'Previous')) + '">' + ICON_PREV + '</button>' +
                            '<span class="vas-216-ptxt">1 ' + esc(lbl('VAS_Of', 'of')) + ' 1</span>' +
                            '<button type="button" class="vas-216-pbtn vas-216-pbtn-next" data-dir="1" aria-label="' + esc(lbl('VAS_Next', 'Next')) + '">' + ICON_NEXT + '</button>' +
                        '</div>' +
                    '</div>' +
                '</div>'
            );

            $monthSelect = $card.find('.vas-216-month-sel');
            $yearSelect = $card.find('.vas-216-year-sel');
            $tableHead = $card.find('.vas-216-thead');
            $tableBody = $card.find('.vas-216-tbody');
            $helper = $card.find('.vas-216-helper');
            $pageText = $card.find('.vas-216-ptxt');
            $prevBtn = $card.find('.vas-216-pbtn-prev');
            $nextBtn = $card.find('.vas-216-pbtn-next');

            populateMonthYearDropdowns();
            renderTableHeaders();

            $monthSelect.on('change', function () {
                currentMonth = parseInt($(this).val(), 10);
                currentPage = 1;
                loadQueueData();
            });

            $yearSelect.on('change', function () {
                currentYear = parseInt($(this).val(), 10);
                currentPage = 1;
                loadQueueData();
            });

            $prevBtn.on('click', function () {
                if (currentPage > 1) {
                    currentPage--;
                    loadQueueData();
                }
            });

            $nextBtn.on('click', function () {
                if (currentPage < totalPages) {
                    currentPage++;
                    loadQueueData();
                }
            });

            // Delegate row clicks to open PO detail modal
            $tableBody.on('click', '.vas-216-trow', function (e) {
                var poId = parseInt($(this).attr('data-po-id'), 10);
                var poNo = $(this).attr('data-po-no');
                if (poId > 0) {
                    openRecordModal(poId, poNo);
                }
            });

            $root.append($card);

            $busy = $(
                '<div class="vas-216-busy vas-216-hidden">' +
                    '<div class="vis-busyindicatorinnerwrap"><i class="vis_widgetloader"></i></div>' +
                '</div>'
            );
            $root.append($busy);
        }

        function populateMonthYearDropdowns() {
            var monthNames = [];
            for (var m = 0; m < 12; m++) {
                var d = new Date(2026, m, 1);
                var mName = d.toLocaleString(window.navigator.language || 'default', { month: 'long' });
                monthNames.push(mName);
            }

            var mHtml = '';
            for (var i = 0; i < 12; i++) {
                var mVal = i + 1;
                var sel = (mVal === currentMonth) ? ' selected' : '';
                mHtml += '<option value="' + mVal + '"' + sel + '>' + esc(monthNames[i]) + '</option>';
            }
            $monthSelect.html(mHtml);

            var nowYear = new Date().getFullYear();
            var yHtml = '';
            for (var y = nowYear - 2; y <= nowYear + 2; y++) {
                var ySel = (y === currentYear) ? ' selected' : '';
                yHtml += '<option value="' + y + '"' + ySel + '>' + y + '</option>';
            }
            $yearSelect.html(yHtml);
        }

        function renderTableHeaders() {
            var headers = [
                { label: lbl('VAS_PONumber', 'PO No'), align: 'left' },
                { label: lbl('VAS_PODate', 'PO date'), align: 'left' },
                { label: lbl('VAS_Vendor', 'Vendor'), align: 'left' },
                { label: lbl('VAS_Warehouse', 'Warehouse'), align: 'left' },
                { label: lbl('VAS_Requisition', 'Requisition'), align: 'left' },
                { label: lbl('VAS_Representative', 'Representative'), align: 'left' },
                { label: lbl('VAS_Expected', 'Expected'), align: 'left' },
                { label: lbl('VAS_Value', 'Value'), align: 'right' },
                { label: lbl('VAS_Status', 'Status'), align: 'left' }
            ];

            var hHtml = '';
            for (var i = 0; i < headers.length; i++) {
                var h = headers[i];
                var alignCls = (h.align === 'right') ? ' vas-216-right' : '';
                hHtml += '<span class="vas-216-cell' + alignCls + '" title="' + esc(h.label) + '">' + esc(h.label) + '</span>';
            }
            $tableHead.html(hHtml);
        }

        function loadQueueData() {
            showBusy(true);

            var params = {
                pageNo: currentPage,
                pageSize: pageSize,
                month: currentMonth,
                year: currentYear
            };

            $.ajax({
                url: VIS.Application.contextUrl + 'VAS_216_POQueueWidget/GetPOQueueData',
                type: 'GET',
                data: params,
                cache: false,
                success: function (res) {
                    var data = parseResponse(res);
                    if (data.error || data.success === false) {
                        renderErrorState(data.error || lbl('VAS_CouldntLoad', "Couldn't load data"));
                        return;
                    }

                    totalRecords = data.totalRecords || 0;
                    totalPages = data.totalPages || 1;
                    currentPage = data.pageNo || 1;
                    baseCurrency = data.currency || baseCurrency;
                    loadedRecords = data.records || [];

                    renderTableRows(loadedRecords);
                    renderPager();
                },
                error: function () {
                    renderErrorState(lbl('VAS_CouldntLoad', "Couldn't load data"));
                },
                complete: function () {
                    showBusy(false);
                }
            });
        }

        function renderTableRows(records) {
            if (!records || records.length === 0) {
                var emptyMsg = lbl('VAS_NoLivePOsFound', 'No live purchase orders found for the selected period');
                $tableBody.html(
                    '<div class="vas-216-empty-state">' +
                        ICON_EMPTY +
                        '<span>' + esc(emptyMsg) + '</span>' +
                    '</div>'
                );
                return;
            }

            var rowsHtml = '';
            for (var i = 0; i < records.length; i++) {
                var r = records[i];

                var poNo = r.DocumentNo || '—';
                var poDateShort = formatDateShort(r.OrderDate);
                var poDateFull = formatDateFull(r.OrderDate);
                var expDateShort = formatDateShort(r.PromisedDate);
                var expDateFull = formatDateFull(r.PromisedDate);
                var vendor = r.VendorName || '—';
                var warehouse = r.WarehouseName || '—';
                var rep = r.RepresentativeName || '—';
                var firstReq = r.FirstRequisition || '—';
                var allReqs = r.AllRequisitions || firstReq;

                var curSym = r.CurrencySymbol || baseCurrency.CurSymbol;
                var curIso = r.CurrencyISO || baseCurrency.ISO_Code;
                var curPrec = r.CurrencyPrecision != null ? r.CurrencyPrecision : baseCurrency.StdPrecision;
                var formattedVal = formatMoney(r.POValue, curSym, curIso, curPrec);

                var stInfo = getDocStatusInfo(r.DocStatus);

                rowsHtml +=
                    '<div class="vas-216-trow" data-po-id="' + r.PurchaseOrderID + '" data-po-no="' + esc(poNo) + '">' +
                        '<span class="vas-216-cell vas-216-c-link" title="' + esc(poNo) + '">' + esc(poNo) + '</span>' +
                        '<span class="vas-216-cell vas-216-c-std" title="' + esc(poDateFull) + '">' + esc(poDateShort) + '</span>' +
                        '<span class="vas-216-cell vas-216-c-std" title="' + esc(vendor) + '">' + esc(vendor) + '</span>' +
                        '<span class="vas-216-cell vas-216-c-dark" title="' + esc(warehouse) + '">' + esc(warehouse) + '</span>' +
                        '<span class="vas-216-cell vas-216-c-std" title="' + esc(allReqs) + '">' + esc(firstReq) + '</span>' +
                        '<span class="vas-216-cell vas-216-c-dark" title="' + esc(rep) + '">' + esc(rep) + '</span>' +
                        '<span class="vas-216-cell vas-216-c-std" title="' + esc(expDateFull) + '">' + esc(expDateShort) + '</span>' +
                        '<span class="vas-216-cell vas-216-right vas-216-c-emph" title="' + esc(formattedVal) + '">' + esc(formattedVal) + '</span>' +
                        '<span class="vas-216-cell" title="' + esc(stInfo.text) + '"><span class="vas-216-chip ' + stInfo.chip + '">' + esc(stInfo.text) + '</span></span>' +
                    '</div>';
            }

            $tableBody.html(rowsHtml);
        }

        function renderPager() {
            var startIdx = totalRecords > 0 ? (currentPage - 1) * pageSize + 1 : 0;
            var endIdx = Math.min(currentPage * pageSize, totalRecords);

            var helperText = lbl('VAS_Showing', 'Showing') + ' ' +
                             startIdx + '–' + endIdx + ' ' +
                             lbl('VAS_Of', 'of') + ' ' + totalRecords + ' · ' +
                             lbl('VAS_SelectPORecordHelper', 'select a PO number to open the record');

            $helper.text(helperText);
            $helper.attr('title', helperText);

            $pageText.text(currentPage + ' ' + lbl('VAS_Of', 'of') + ' ' + totalPages);

            $prevBtn.prop('disabled', currentPage <= 1);
            $nextBtn.prop('disabled', currentPage >= totalPages || totalRecords === 0);
        }

        function renderErrorState(msg) {
            $tableBody.html(
                '<div class="vas-216-empty-state">' +
                    '<span>' + esc(msg) + '</span>' +
                '</div>'
            );
            $helper.text('—');
            $pageText.text('1 ' + lbl('VAS_Of', 'of') + ' 1');
            $prevBtn.prop('disabled', true);
            $nextBtn.prop('disabled', true);
        }

        /* ============================================================
           RECORD NAVIGATION TO PURCHASE ORDER WINDOW
           ============================================================ */
        function openPurchaseOrderRecord(orderId) {
            if (!orderId) { return; }

            // 1. Fire value changed if tab panel / dashboard router is listening
            try {
                if ($self.widgetFirevalueChanged) {
                    var windowParam = {
                        "Record_ID": orderId,
                        "C_Order_ID": orderId,
                        "AD_Table_ID": 259,
                        "WindowName": "VAS_PurchaseOrder",
                        "AD_Tab_ID": 1002398,
                        "TabWhereClause": "C_Order.C_Order_ID = " + orderId,
                        "TabLayout": "N",
                        "TabIndex": "0"
                    };
                    $self.widgetFirevalueChanged(windowParam);
                }
            } catch (e) { }

            // 2. Standard VIS Zoom / Window Open
            try {
                if (VIS && VIS.ZoomManager && VIS.ZoomManager.zoom) {
                    VIS.ZoomManager.zoom(259, orderId);
                    return;
                }
            } catch (e) { }

            try {
                if (VIS && VIS.viewManager && VIS.viewManager.startWindow) {
                    var action = new VIS.AActionItem();
                    action.setAD_Table_ID(259);
                    action.setRecord_ID(orderId);
                    action.setWindowName("VAS_PurchaseOrder");
                    action.setAD_Tab_ID(1002398);
                    VIS.viewManager.startWindow(0, action);
                }
            } catch (e) { }
        }

        /* ============================================================
           MODAL ENGINE & DRILL-DOWN STACK
           ============================================================ */
        function ensureModalHost() {
            if ($modalHost && $modalHost[0]) { return; }

            var html =
                '<div class="vas-216-mask" role="dialog" aria-modal="true">' +
                    '<div class="vas-216-modal">' +
                        '<div class="vas-216-modal-header">' +
                            '<div class="vas-216-htxt-wrap">' +
                                '<button type="button" class="vas-216-xbtn vas-216-back-btn" aria-label="' + esc(lbl('VAS_Back', 'Back')) + '" style="display:none;">' +
                                    ICON_BACK +
                                '</button>' +
                                '<div class="vas-216-htxt">' +
                                    '<h2 class="vas-216-mtitle"></h2>' +
                                    '<div class="vas-216-msub"></div>' +
                                '</div>' +
                            '</div>' +
                            '<div class="vas-216-hact">' +
                                '<button type="button" class="vas-216-xbtn vas-216-close-btn" aria-label="' + esc(lbl('VAS_Close', 'Close')) + '">' +
                                    ICON_CLOSE +
                                '</button>' +
                            '</div>' +
                        '</div>' +
                        '<div class="vas-216-modal-body"></div>' +
                        '<div class="vas-216-modal-foot"></div>' +
                    '</div>' +
                '</div>';

            $modalHost = $(html);
            $('body').append($modalHost);

            $modalHost.find('.vas-216-close-btn').on('click', closeModal);
            $modalHost.find('.vas-216-back-btn').on('click', popModal);

            $modalHost.on('click', function (e) {
                if (e.target === this) { closeModal(); }
                if ($(e.target).closest('[data-vas-close]').length) { closeModal(); }
            });

            $(document).on('keydown.vas216', function (e) {
                if (e.key === 'Escape') { closeModal(); }
            });

            $(window).on('resize.vas216', function () {
                if ($modalHost && $modalHost.hasClass('vas-216-open')) {
                    fitAllTables();
                }
            });

            // Delegate table pagination buttons inside modal
            $modalHost.find('.vas-216-modal-body').on('click', function (e) {
                var $pg = $(e.target).closest('[data-mt]');
                if ($pg.length) {
                    var mtId = $pg.attr('data-mt');
                    var dir = parseInt($pg.attr('data-dir'), 10);
                    var t = MT[mtId];
                    if (t) {
                        var pages = Math.ceil(t.rows.length / t.size);
                        t.page = Math.min(pages - 1, Math.max(0, t.page + dir));
                        drawTable(mtId);
                    }
                }
            });
        }

        function openModal(cfg, isBack) {
            ensureModalHost();

            if (!isBack) {
                if (cfg.child && currentModalCfg) {
                    modalStack.push(currentModalCfg);
                } else if (!cfg.child) {
                    modalStack = [];
                }
                currentModalCfg = cfg;
            } else {
                currentModalCfg = cfg;
            }

            var $backBtn = $modalHost.find('.vas-216-back-btn');
            if (modalStack.length > 0) {
                $backBtn.show();
            } else {
                $backBtn.hide();
            }

            var $mBody = $modalHost.find('.vas-216-modal-body');
            var $mFoot = $modalHost.find('.vas-216-modal-foot');
            var $modal = $modalHost.find('.vas-216-modal');

            $mBody.attr('class', 'vas-216-modal-body' + (cfg.bodyClass ? ' ' + cfg.bodyClass : ''));
            $modal.attr('class', 'vas-216-modal' + (cfg.size ? ' vas-216-' + cfg.size : ''));

            $modalHost.find('.vas-216-mtitle').text(cfg.title || '');
            $modalHost.find('.vas-216-msub').text(cfg.subtitle || '');

            $mBody.html(cfg.body || '');
            $mFoot.html(cfg.foot || '<span class="vas-216-foot-note"></span><button type="button" class="vas-216-btn" data-vas-close="1">' + esc(lbl('VAS_Close', 'Close')) + '</button>');

            $modalHost.addClass('vas-216-open');

            drawAllTables();

            requestAnimationFrame(function () {
                fitAllTables();
                requestAnimationFrame(fitAllTables);
            });

            if (cfg.after) { cfg.after(); }
        }

        function closeModal() {
            if ($modalHost) {
                $modalHost.removeClass('vas-216-open');
            }
            modalStack = [];
            currentModalCfg = null;
        }

        function popModal() {
            var prev = modalStack.pop();
            if (!prev) {
                closeModal();
                return;
            }
            openModal(prev, true);
        }

        function pagedTable(cols, rows, opts) {
            opts = opts || {};
            var id = 'vas216_mt_' + (++MT_SEQ);
            var initialSize = Math.min(MAX_MODAL_ROWS, opts.size || MAX_MODAL_ROWS);

            MT[id] = {
                cols: cols,
                rows: rows,
                size: initialSize,
                max: initialSize,
                page: 0,
                label: opts.label || '',
                fixed: !!opts.fixed
            };

            return '<div class="vas-216-mtwrap' + (opts.fixed ? ' vas-216-fixed' : '') + '" id="' + id + '"></div>';
        }

        function drawTable(id) {
            var t = MT[id];
            var el = document.getElementById(id);
            if (!t || !el) { return; }

            var tpl = t.cols.map(function (c) { return 'minmax(0,' + (c.w || 1) + 'fr)'; }).join(' ');
            var pages = Math.max(1, Math.ceil(t.rows.length / t.size));
            if (t.page > pages - 1) { t.page = pages - 1; }

            var s = t.page * t.size;
            var slice = t.rows.slice(s, s + t.size);

            var headHtml = '<div class="vas-216-mtbl"><div class="vas-216-mrow vas-216-mhead" style="grid-template-columns:' + tpl + '">';
            for (var ci = 0; ci < t.cols.length; ci++) {
                var col = t.cols[ci];
                var rCls = col.align === 'right' ? ' vas-216-right' : (col.align === 'center' ? ' vas-216-center' : '');
                headHtml += '<span class="vas-216-cell' + rCls + '" title="' + esc(col.label || '') + '">' + esc(col.label || '') + '</span>';
            }
            headHtml += '</div><div class="vas-216-mbody">';

            var bodyRowsHtml = '';
            for (var ri = 0; ri < slice.length; ri++) {
                var rowData = slice[ri];
                bodyRowsHtml += '<div class="vas-216-mrow" style="grid-template-columns:' + tpl + '">';
                for (var rci = 0; rci < t.cols.length; rci++) {
                    var cell = rowData[rci];
                    var colDef = t.cols[rci];
                    var alignCls = colDef.align === 'right' ? ' vas-216-right' : (colDef.align === 'center' ? ' vas-216-center' : '');
                    var customCls = colDef.cls ? ' ' + colDef.cls : '';

                    if (cell && typeof cell === 'object' && cell.chip) {
                        bodyRowsHtml += '<span class="vas-216-cell' + alignCls + '" title="' + esc(cell.text) + '"><span class="vas-216-chip ' + cell.chip + '">' + esc(cell.text) + '</span></span>';
                    } else {
                        var cellVal = cell != null ? String(cell) : '—';
                        bodyRowsHtml += '<span class="vas-216-cell' + alignCls + customCls + '" title="' + esc(cellVal) + '">' + esc(cellVal) + '</span>';
                    }
                }
                bodyRowsHtml += '</div>';
            }

            headHtml += bodyRowsHtml + '</div></div>';

            var helperText = lbl('VAS_Showing', 'Showing') + ' ' + (t.rows.length > 0 ? (s + 1) : 0) + '–' + (s + slice.length) + ' ' +
                             lbl('VAS_Of', 'of') + ' ' + t.rows.length +
                             (t.label ? ' · ' + t.label : '');

            var footHtml = '<div class="vas-216-mtfoot"><span class="vas-216-helper">' + esc(helperText) + '</span>';
            if (pages > 1) {
                footHtml +=
                    '<span class="vas-216-pager">' +
                        '<button type="button" class="vas-216-pbtn" data-mt="' + id + '" data-dir="-1"' + (t.page === 0 ? ' disabled' : '') + ' aria-label="' + esc(lbl('VAS_Previous', 'Previous')) + '">' + ICON_PREV + '</button>' +
                        '<span class="vas-216-ptxt">' + (t.page + 1) + ' ' + lbl('VAS_Of', 'of') + ' ' + pages + '</span>' +
                        '<button type="button" class="vas-216-pbtn" data-mt="' + id + '" data-dir="1"' + (t.page >= pages - 1 ? ' disabled' : '') + ' aria-label="' + esc(lbl('VAS_Next', 'Next')) + '">' + ICON_NEXT + '</button>' +
                    '</span>';
            } else {
                footHtml += '<span></span>';
            }
            footHtml += '</div>';

            el.innerHTML = headHtml + footHtml;
        }

        function drawAllTables() {
            Object.keys(MT).forEach(function (id) {
                if (document.getElementById(id)) { drawTable(id); }
            });
        }

        function fitTable(id) {
            var t = MT[id];
            var el = document.getElementById(id);
            if (!t || !el || t.fixed) { return; }

            var avail = el.clientHeight;
            if (avail < 40) { return; }

            var head = el.querySelector('.vas-216-mhead');
            var foot = el.querySelector('.vas-216-mtfoot');
            var row = el.querySelector('.vas-216-mbody .vas-216-mrow');
            if (!head || !row) { return; }

            var rowH = row.getBoundingClientRect().height || 30;
            var used = head.getBoundingClientRect().height + (foot ? foot.getBoundingClientRect().height + 8 : 0);
            var n = Math.floor((avail - used) / rowH);
            n = Math.max(2, Math.min(t.max, n));

            if (n !== t.size) {
                t.size = n;
                drawTable(id);
            }
        }

        function fitAllTables() {
            Object.keys(MT).forEach(function (id) {
                if (document.getElementById(id)) { fitTable(id); }
            });

            if ($modalHost) {
                var mBody = $modalHost.find('.vas-216-modal-body')[0];
                if (mBody) {
                    $(mBody).removeClass('vas-216-overflowing');
                    if (mBody.scrollHeight > mBody.clientHeight + 2) {
                        $(mBody).addClass('vas-216-overflowing');
                    }
                }
            }
        }

        function mstatsHtml(items) {
            var html = '<div class="vas-216-mstats">';
            for (var i = 0; i < items.length; i++) {
                var s = items[i];
                html +=
                    '<div class="vas-216-mstat">' +
                        '<div class="vas-216-l" title="' + esc(s.l) + '">' + esc(s.l) + '</div>' +
                        '<div class="vas-216-v" title="' + esc(s.v) + '">' + esc(s.v) + '</div>' +
                    '</div>';
            }
            html += '</div>';
            return html;
        }

        /* ============================================================
           PO RECORD MODAL & LINES DRILL-DOWN
           ============================================================ */
        function openRecordModal(poId, poNo) {
            showBusy(true);

            $.ajax({
                url: VIS.Application.contextUrl + 'VAS_216_POQueueWidget/GetPODetail?C_Order_ID=' + poId,
                type: 'GET',
                cache: false,
                success: function (res) {
                    var data = parseResponse(res);
                    var hdr = data.header || {};
                    var deliveryStatus = data.deliveryStatus || 'Pending';
                    var totalOrdered = data.totalOrderedQty || 0;

                    var vendor = hdr.VendorName || '—';
                    var dateDisplay = formatDateFull(hdr.OrderDate);
                    var expDateDisplay = formatDateFull(hdr.PromisedDate);
                    var whName = hdr.WarehouseName || '—';
                    var createdBy = (hdr.CreatedByName || '') + (hdr.CreatedDate ? (' · ' + formatDateFull(hdr.CreatedDate)) : '');

                    var curSym = hdr.CurrencySymbol || baseCurrency.CurSymbol;
                    var curIso = hdr.CurrencyISO || baseCurrency.ISO_Code;
                    var curPrec = hdr.CurrencyPrecision != null ? hdr.CurrencyPrecision : baseCurrency.StdPrecision;
                    var formattedVal = formatMoney(hdr.POValue, curSym, curIso, curPrec);

                    var docStatusInfo = getDocStatusInfo(hdr.DocStatus);
                    var delivStatusInfo = getDeliveryStatusInfo(deliveryStatus);

                    var headerStats = mstatsHtml([
                        { l: lbl('VAS_Vendor', 'Vendor'), v: vendor },
                        { l: lbl('VAS_PODate', 'PO date'), v: dateDisplay },
                        { l: lbl('VAS_ExpectedOn', 'Expected on'), v: expDateDisplay },
                        { l: lbl('VAS_POValue', 'PO value'), v: formattedVal },
                        { l: lbl('VAS_Warehouse', 'Warehouse'), v: whName },
                        { l: lbl('VAS_CreatedBy', 'Created by'), v: createdBy || '—' },
                        { l: lbl('VAS_DocumentStatus', 'Document status'), v: docStatusInfo.text },
                        { l: lbl('VAS_DeliveryStatus', 'Delivery status'), v: delivStatusInfo.text }
                    ]);

                    // Load PO lines for the record modal table
                    $.ajax({
                        url: VIS.Application.contextUrl + 'VAS_216_POQueueWidget/GetPOLines?C_Order_ID=' + poId,
                        type: 'GET',
                        cache: false,
                        success: function (lineRes) {
                            var lineData = parseResponse(lineRes);
                            var lines = lineData.lines || [];

                            var lineCols = [
                                { label: '#', w: 0.3, align: 'right' },
                                { label: lbl('VAS_Product', 'Product'), w: 1.5, cls: 'vas-216-c-prim' },
                                { label: lbl('VAS_Attribute', 'Attribute'), w: 1.2 },
                                { label: lbl('VAS_UOM', 'UoM'), w: 0.5 },
                                { label: lbl('VAS_Ordered', 'Ordered'), w: 0.7, align: 'right' },
                                { label: lbl('VAS_Received', 'Received'), w: 0.7, align: 'right' },
                                { label: lbl('VAS_Pending', 'Pending'), w: 0.7, align: 'right', cls: 'vas-216-c-prim' },
                                { label: lbl('VAS_Rate', 'Rate'), w: 0.7, align: 'right' },
                                { label: lbl('VAS_Amount', 'Amount'), w: 0.9, align: 'right', cls: 'vas-216-c-emph' },
                                { label: lbl('VAS_LineStatus', 'Line status'), w: 1.0 }
                            ];

                            var lineRows = lines.map(function (l, idx) {
                                var rateFmt = formatMoney(l.Rate, curSym, curIso, curPrec);
                                var amtFmt = formatMoney(l.LineAmount, curSym, curIso, curPrec);
                                var lStInfo = getLineStatusInfo(l.LineStatus);

                                return [
                                    String(l.LineNo || (idx + 1)),
                                    l.ProductName || l.ProductCode || '—',
                                    l.AttributeDescription || '—',
                                    l.UOM || '—',
                                    formatDecimal(l.OrderedQty),
                                    formatDecimal(l.ReceivedQty),
                                    formatDecimal(l.PendingQty),
                                    rateFmt,
                                    amtFmt,
                                    { chip: lStInfo.chip, text: lStInfo.text }
                                ];
                            });

                            var bodyHtml =
                                headerStats +
                                '<div class="vas-216-msec">' + esc(lbl('VAS_PurchaseOrderLines', 'Purchase order lines')) + '</div>' +
                                pagedTable(lineCols, lineRows, { label: lbl('VAS_LinesOf', 'lines of') + ' ' + (hdr.DocumentNo || poNo) });

                            var footNote = lines.length + ' ' + lbl('VAS_Lines', 'lines') + ' · ' +
                                           formatNumber(totalOrdered) + ' ' + lbl('VAS_QtyOrdered', 'qty ordered') + ' · ' +
                                           delivStatusInfo.text;

                            openModal({
                                child: false,
                                title: hdr.DocumentNo || poNo,
                                subtitle: vendor + ' · ' + dateDisplay + ' · ' + docStatusInfo.text,
                                body: bodyHtml,
                                foot: '<span class="vas-216-foot-note">' + esc(footNote) + '</span>' +
                                      '<span><button type="button" class="vas-216-btn vas-216-btn-primary vas-216-btn-open-record">' + esc(lbl('VAS_OpenRecord', 'Open Record')) + '</button> ' +
                                      '<button type="button" class="vas-216-btn" data-vas-close="1">' + esc(lbl('VAS_Close', 'Close')) + '</button></span>',
                                after: function () {
                                    $modalHost.find('.vas-216-btn-open-record').on('click', function () {
                                        openPurchaseOrderRecord(poId);
                                    });
                                }
                            });
                        },
                        complete: function () {
                            showBusy(false);
                        }
                    });
                },
                error: function () {
                    showBusy(false);
                }
            });
        }

        this.refreshWidget = function () {
            loadQueueData();
        };

        this.getRoot = function () { return $root; };

        this.disposeComponent = function () {
            if ($monthSelect) { $monthSelect.off('change'); }
            if ($yearSelect) { $yearSelect.off('change'); }
            if ($prevBtn) { $prevBtn.off('click'); }
            if ($nextBtn) { $nextBtn.off('click'); }
            if ($tableBody) { $tableBody.off('click'); }
            $(document).off('keydown.vas216');
            $(window).off('resize.vas216');
            if ($modalHost) {
                $modalHost.remove();
                $modalHost = null;
            }
            $root.remove();
        };
    };

    VAS.VAS_216_POQueueWidget.prototype.widgetFirevalueChanged = function (value) {
        if (this.listener) { this.listener.widgetFirevalueChanged(value); }
    };

    VAS.VAS_216_POQueueWidget.prototype.addChangeListener = function (listener) {
        this.listener = listener;
    };

    VAS.VAS_216_POQueueWidget.prototype.init = function (windowNo, frame) {
        this.frame = frame;
        this.AD_UserHomeWidgetID = frame.widgetInfo.AD_UserHomeWidgetID;
        this.windowNo = windowNo;
        this.Initalize();
        this.frame.getContentGrid().append(this.getRoot());
        ensureDashInlineSizeVar(this.getRoot());
    };

    VAS.VAS_216_POQueueWidget.prototype.widgetSizeChange = function (height, width) { };

    VAS.VAS_216_POQueueWidget.prototype.refreshWidget = function () {
        this.refreshWidget();
    };

    VAS.VAS_216_POQueueWidget.prototype.dispose = function () {
        this.disposeComponent();
        if (this.frame) { this.frame.dispose(); }
        this.frame = null;
    };

})(VAS, jQuery);
