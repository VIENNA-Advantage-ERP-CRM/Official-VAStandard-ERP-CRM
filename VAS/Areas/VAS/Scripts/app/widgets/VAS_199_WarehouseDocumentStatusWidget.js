/**
 * Warehouse wise PO · Document Status Widget (Purchase Order Dashboard - Widget 09)
 * Purpose - 4x2 matrix widget displaying receiving warehouses against real C_Order
 *           document statuses (Drafted, In Process, Completed, Closed) for a selected
 *           month/year. Clicking any warehouse row opens a drill-down modal listing
 *           all purchase orders for that warehouse, with full PO line inspection and
 *           direct record navigation into the VAS_PurchaseOrder window.
 * Prefix  - VAS_199_
 *
 * Message Keys / Localization Table:
 *  #  | Default English Text                              | Message Key
 * ----+---------------------------------------------------+-----------------------------------
 *  1  | Warehouse wise PO · Document Status               | VAS_199_WarehouseDocumentStatus
 *  2  | Warehouse                                         | VAS_199_Warehouse
 *  3  | Drafted                                           | VAS_199_Drafted
 *  4  | In Process                                        | VAS_199_InProcess
 *  5  | Completed                                         | VAS_199_Completed
 *  6  | Closed                                            | VAS_199_Closed
 *  7  | Total                                             | VAS_199_Total
 *  8  | Total documents                                   | VAS_199_TotalDocuments
 *  9  | documents                                         | VAS_199_Documents
 * 10  | select a warehouse                                | VAS_199_SelectWarehouse
 * 11  | Showing                                           | VAS_199_Showing
 * 12  | of                                                | VAS_199_Of
 * 13  | Previous                                          | VAS_199_Previous
 * 14  | Next                                              | VAS_199_Next
 * 15  | Purchase orders for this warehouse                | VAS_199_POForWarehouse
 * 16  | Purchase orders                                   | VAS_199_PurchaseOrders
 * 17  | PO No                                             | VAS_199_PONo
 * 18  | PO date                                           | VAS_199_PODate
 * 19  | Vendor                                            | VAS_199_Vendor
 * 20  | Representative                                    | VAS_199_Representative
 * 21  | Value                                             | VAS_199_Value
 * 22  | Delivery                                          | VAS_199_Delivery
 * 23  | Status                                            | VAS_199_Status
 * 24  | View lines of                                     | VAS_199_ViewLinesOf
 * 25  | Open record                                       | VAS_199_OpenRecord
 * 26  | Expected on                                       | VAS_199_ExpectedOn
 * 27  | PO value                                          | VAS_199_POValue
 * 28  | Created by                                        | VAS_199_CreatedBy
 * 29  | Document status                                   | VAS_199_DocumentStatus
 * 30  | Delivery status                                   | VAS_199_DeliveryStatus
 * 31  | Purchase order lines                              | VAS_199_POLines
 * 32  | Product                                           | VAS_199_Product
 * 33  | Attribute                                         | VAS_199_Attribute
 * 34  | UoM                                               | VAS_199_UoM
 * 35  | Ordered                                           | VAS_199_Ordered
 * 36  | Received                                          | VAS_199_Received
 * 37  | Pending                                           | VAS_199_Pending
 * 38  | Rate                                              | VAS_199_Rate
 * 39  | Amount                                            | VAS_199_Amount
 * 40  | Line status                                       | VAS_199_LineStatus
 * 41  | Partial received                                  | VAS_199_PartialReceived
 * 42  | Fully delivered                                   | VAS_199_FullyDelivered
 * 43  | Not applicable                                    | VAS_199_NotApplicable
 * 44  | Back                                              | VAS_199_Back
 * 45  | Close                                             | VAS_199_Close
 * 46  | No purchase orders found for this period          | VAS_199_NoOrdersFound
 * 47  | No order lines found                              | VAS_199_NoLinesFound
 * 48  | Loading purchase orders...                        | VAS_199_LoadingOrders
 * 49  | Loading lines...                                  | VAS_199_LoadingLines
 * 50  | select a PO number to open the record             | VAS_199_SelectPOToOpen
 * 51  | lines of                                          | VAS_199_LinesOf
 * 52  | qty ordered                                       | VAS_199_QtyOrdered
 * 53  | Month                                             | Month
 * 54  | Year                                              | Year
 */
; VAS = window.VAS || {};

; (function (VAS, $) {

    function ensureDashInlineSizeVar($el) {
        if (window.__vasDashInlineSizeObserver) { return; }
        if (typeof ResizeObserver === 'undefined') { return; }
        var container = $el.closest('.vis-widget-container, [data-dashboard-container], .vis-widget-body, body')[0];
        if (!container) { return; }
        var write = function () {
            var w = container.clientWidth || window.innerWidth;
            if (w > 0) {
                document.documentElement.style.setProperty('--dash-inline-size', w + 'px');
            }
        };
        window.__vasDashInlineSizeObserver = new ResizeObserver(write);
        window.__vasDashInlineSizeObserver.observe(container);
        window.addEventListener('resize', write);
        write();
    }

    function lbl(key, fallback) {
        var t = VIS.Msg.getMsg(key);
        return (t && t !== key && t !== '[' + key + ']') ? t : fallback;
    }

    function escapeHtml(value) {
        return String(value == null ? '' : value)
            .replace(/&/g, '&amp;')
            .replace(/</g, '&lt;')
            .replace(/>/g, '&gt;')
            .replace(/"/g, '&quot;')
            .replace(/'/g, '&#039;');
    }

    function formatNumber(value) {
        var n = Number(value || 0);
        return isNaN(n) ? '0' : n.toLocaleString(window.navigator.language);
    }

    function formatAmount(value, curSymbol) {
        var n = Number(value || 0);
        var sym = curSymbol ? (curSymbol + ' ') : '';
        if (isNaN(n)) { return sym + '0'; }
        return sym + n.toLocaleString(window.navigator.language, { minimumFractionDigits: 2, maximumFractionDigits: 2 });
    }

    function formatDate(dateVal) {
        if (!dateVal) { return '—'; }
        var d = new Date(dateVal);
        if (isNaN(d.getTime())) { return String(dateVal); }
        return d.toLocaleDateString(window.navigator.language, { day: '2-digit', month: 'short', year: 'numeric' });
    }

    function getDocStatusText(code) {
        if (!code) { return ''; }
        var c = String(code).toUpperCase();
        if (c === 'DR') { return lbl('VAS_199_Drafted', 'Drafted'); }
        if (c === 'IP') { return lbl('VAS_199_InProcess', 'In Process'); }
        if (c === 'CO') { return lbl('VAS_199_Completed', 'Completed'); }
        if (c === 'CL') { return lbl('VAS_199_Closed', 'Closed'); }
        return code;
    }

    function getDeliveryStatusText(status) {
        if (!status) { return ''; }
        if (status === 'Fully delivered') { return lbl('VAS_199_FullyDelivered', 'Fully delivered'); }
        if (status === 'Partial') { return lbl('VAS_199_Partial', 'Partial'); }
        if (status === 'Pending') { return lbl('VAS_199_Pending', 'Pending'); }
        if (status === 'Not applicable') { return lbl('VAS_199_NotApplicable', 'Not applicable'); }
        return status;
    }

    function getLineStatusText(status) {
        if (!status) { return ''; }
        if (status === 'Drafted') { return lbl('VAS_199_Drafted', 'Drafted'); }
        if (status === 'Received') { return lbl('VAS_199_Received', 'Received'); }
        if (status === 'Partial received') { return lbl('VAS_199_PartialReceived', 'Partial received'); }
        if (status === 'Pending') { return lbl('VAS_199_Pending', 'Pending'); }
        return status;
    }

    function getLocalizedMonthName(monthIndex) {
        try {
            var d = new Date(2026, monthIndex, 1);
            var lang = (VIS && VIS.Env) ? VIS.Env.getLanguage() : (window.navigator.language || 'en-US');
            return d.toLocaleString(lang, { month: 'long' });
        } catch (e) {
            return MONTH_NAMES[monthIndex] || ('Month ' + (monthIndex + 1));
        }
    }

    var MONTH_NAMES = ['January', 'February', 'March', 'April', 'May', 'June', 'July', 'August', 'September', 'October', 'November', 'December'];
    var MONTH_SHORT = ['Jan', 'Feb', 'Mar', 'Apr', 'May', 'Jun', 'Jul', 'Aug', 'Sep', 'Oct', 'Nov', 'Dec'];

    var ICON_LINES = '<svg viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2" stroke-linecap="round" stroke-linejoin="round"><path d="M8 6h13M8 12h13M8 18h13M3 6h.01M3 12h.01M3 18h.01"/></svg>';
    var ICON_BACK = '<svg viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2" stroke-linecap="round" stroke-linejoin="round"><path d="M19 12H5M12 19l-7-7 7-7"/></svg>';
    var ICON_CLOSE = '<svg viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2" stroke-linecap="round"><path d="M18 6 6 18M6 6l12 12"/></svg>';
    var ICON_CHEV_LEFT = '<svg viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2.4" stroke-linecap="round"><polyline points="15 18 9 12 15 6"/></svg>';
    var ICON_CHEV_RIGHT = '<svg viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2.4" stroke-linecap="round"><polyline points="9 18 15 12 9 6"/></svg>';

    VAS.VAS_199_WarehouseDocumentStatusWidget = function () {

        this.frame;
        this.windowNo;
        this.AD_UserHomeWidgetID = 0;

        var $self = this;
        var $wrapper = $('<div class="vas-199-container">');
        var $root = $('<div class="vas-199-root">');

        var $monthSelect;
        var $yearSelect;
        var $bodyGrid;
        var $rowsContainer;
        var $footer;
        var $busy;

        var now = new Date();
        var selectedMonth = now.getMonth() + 1; // 1-12
        var selectedYear = now.getFullYear();

        var warehouseData = [];
        var totalDocuments = 0;
        var currentPage = 1;
        var PAGE_SIZE = 4;

        // Modal drilldown state
        var $modalMask = null;
        var modalStack = [];
        var currentModalConfig = null;

        // PO List pagination state
        var activeWarehouseId = 0;
        var activeWarehouseName = '';
        var activeWarehouseOrders = [];
        var activeWarehouseStats = {};
        var ordersPage = 1;
        var ORDERS_PAGE_SIZE = 10;

        // PO Lines pagination state
        var activeOrderHeader = null;
        var activeOrderLines = [];
        var activeOrderStats = {};
        var linesPage = 1;
        var LINES_PAGE_SIZE = 8;

        this.Initalize = function () {
            buildWidget();
            loadYears();
        };

        function buildWidget() {
            var $head = $('<div class="vas-199-head">');

            var $headTxt = $('<div class="vas-199-head-txt">');
            var $title = $('<h3 class="vas-199-title">').text(lbl('VAS_199_WarehouseDocumentStatus', 'Warehouse wise PO · Document Status'));
            $headTxt.append($title);

            var $mfilter = $('<div class="vas-199-mfilter">');
            $monthSelect = $('<select class="vas-199-msel vas-199-month" aria-label="' + escapeHtml(lbl('Month', 'Month')) + '">');
            $yearSelect = $('<select class="vas-199-msel vas-199-year" aria-label="' + escapeHtml(lbl('Year', 'Year')) + '">');

            for (var m = 0; m < 12; m++) {
                var monthLabel = getLocalizedMonthName(m);
                var $optM = $('<option value="' + (m + 1) + '">' + escapeHtml(monthLabel) + '</option>');
                if (m + 1 === selectedMonth) {
                    $optM.attr('selected', 'selected');
                }
                $monthSelect.append($optM);
            }

            $mfilter.append($monthSelect).append($yearSelect);
            $head.append($headTxt).append($mfilter);
            $root.append($head);

            // Table Matrix Container
            var $tbl = $('<div class="vas-199-tbl">');
            var $thead = $(
                '<div class="vas-199-trow vas-199-thead">' +
                    '<span class="vas-199-cell" title="' + escapeHtml(lbl('VAS_199_Warehouse', 'Warehouse')) + '">' + escapeHtml(lbl('VAS_199_Warehouse', 'Warehouse')) + '</span>' +
                    '<span class="vas-199-cell vas-199-right" title="' + escapeHtml(lbl('VAS_199_Drafted', 'Drafted')) + '">' + escapeHtml(lbl('VAS_199_Drafted', 'Drafted')) + '</span>' +
                    '<span class="vas-199-cell vas-199-right" title="' + escapeHtml(lbl('VAS_199_InProcess', 'In Process')) + '">' + escapeHtml(lbl('VAS_199_InProcess', 'In Process')) + '</span>' +
                    '<span class="vas-199-cell vas-199-right" title="' + escapeHtml(lbl('VAS_199_Completed', 'Completed')) + '">' + escapeHtml(lbl('VAS_199_Completed', 'Completed')) + '</span>' +
                    '<span class="vas-199-cell vas-199-right" title="' + escapeHtml(lbl('VAS_199_Closed', 'Closed')) + '">' + escapeHtml(lbl('VAS_199_Closed', 'Closed')) + '</span>' +
                    '<span class="vas-199-cell vas-199-right" title="' + escapeHtml(lbl('VAS_199_Total', 'Total')) + '">' + escapeHtml(lbl('VAS_199_Total', 'Total')) + '</span>' +
                '</div>'
            );
            $tbl.append($thead);

            $rowsContainer = $('<div class="vas-199-tbody">');
            $tbl.append($rowsContainer);
            $root.append($tbl);

            // Widget Footer Pager
            $footer = $(
                '<div class="vas-199-wfoot">' +
                    '<span class="vas-199-helper"></span>' +
                    '<div class="vas-199-pager">' +
                        '<button type="button" class="vas-199-pbtn vas-199-prev" aria-label="' + escapeHtml(lbl('VAS_199_Previous', 'Previous')) + '">' + ICON_CHEV_LEFT + '</button>' +
                        '<span class="vas-199-ptxt"></span>' +
                        '<button type="button" class="vas-199-pbtn vas-199-next" aria-label="' + escapeHtml(lbl('VAS_199_Next', 'Next')) + '">' + ICON_CHEV_RIGHT + '</button>' +
                    '</div>' +
                '</div>'
            );
            $root.append($footer);

            $busy = $('<div class="vas-199-busy vas-199-hidden"><div class="vis-busyindicatorinnerwrap"><i class="vis_widgetloader"></i></div></div>');
            $root.append($busy);

            $wrapper.append($root);

            // Wire Period Filter Events
            $monthSelect.on('change', function (e) {
                e.stopPropagation();
                selectedMonth = parseInt($(this).val(), 10);
                currentPage = 1;
                loadData();
            });

            $yearSelect.on('change', function (e) {
                e.stopPropagation();
                selectedYear = parseInt($(this).val(), 10);
                currentPage = 1;
                loadData();
            });

            $footer.find('.vas-199-prev').on('click', function (e) {
                e.stopPropagation();
                if (currentPage > 1) {
                    currentPage--;
                    renderRows();
                }
            });

            $footer.find('.vas-199-next').on('click', function (e) {
                e.stopPropagation();
                var totalPages = Math.max(1, Math.ceil(warehouseData.length / PAGE_SIZE));
                if (currentPage < totalPages) {
                    currentPage++;
                    renderRows();
                }
            });
        }

        function setBusy(show) {
            if ($busy) {
                $busy.toggleClass('vas-199-hidden', !show);
            }
        }

        function loadYears() {
            $.ajax({
                url: VIS.Application.contextUrl + 'VAS_199_WarehouseDocumentStatusWidget/GetAvailableYears',
                type: 'GET',
                cache: false,
                success: function (res) {
                    var data = typeof res === 'string' ? JSON.parse(res) : res;
                    if (data && data.years && data.years.length > 0) {
                        $yearSelect.empty();
                        for (var y = 0; y < data.years.length; y++) {
                            var yearVal = data.years[y];
                            var $optY = $('<option value="' + yearVal + '">' + yearVal + '</option>');
                            if (yearVal === selectedYear) {
                                $optY.attr('selected', 'selected');
                            }
                            $yearSelect.append($optY);
                        }
                    }
                    loadData();
                },
                error: function () {
                    loadData();
                }
            });
        }

        function loadData() {
            setBusy(true);
            $.ajax({
                url: VIS.Application.contextUrl + 'VAS_199_WarehouseDocumentStatusWidget/GetWarehouseDocumentStatus',
                type: 'GET',
                cache: false,
                data: { month: selectedMonth, year: selectedYear },
                success: function (res) {
                    setBusy(false);
                    var data = typeof res === 'string' ? JSON.parse(res) : res;
                    if (data && !data.error) {
                        warehouseData = data.warehouses || [];
                        totalDocuments = Number(data.totalDocuments || 0);
                    } else {
                        warehouseData = [];
                        totalDocuments = 0;
                    }
                    renderRows();
                },
                error: function () {
                    setBusy(false);
                    warehouseData = [];
                    totalDocuments = 0;
                    renderRows();
                }
            });
        }

        function renderRows() {
            $rowsContainer.empty();
            var total = warehouseData.length;
            var totalPages = Math.max(1, Math.ceil(total / PAGE_SIZE));

            if (currentPage > totalPages) { currentPage = totalPages; }
            if (currentPage < 1) { currentPage = 1; }

            if (total === 0) {
                $rowsContainer.html('<div class="vas-199-empty">' + escapeHtml(lbl('VAS_199_NoOrdersFound', 'No purchase orders found for this period')) + '</div>');
                $footer.find('.vas-199-helper').text(lbl('VAS_199_Showing', 'Showing') + ' 0 ' + lbl('VAS_199_Of', 'of') + ' 0');
                $footer.find('.vas-199-ptxt').text('1 ' + lbl('VAS_199_Of', 'of') + ' 1');
                $footer.find('.vas-199-prev').prop('disabled', true);
                $footer.find('.vas-199-next').prop('disabled', true);
                return;
            }

            var start = (currentPage - 1) * PAGE_SIZE;
            var end = Math.min(start + PAGE_SIZE, total);
            var pageSlice = warehouseData.slice(start, end);

            for (var i = 0; i < pageSlice.length; i++) {
                var r = pageSlice[i];
                var $row = $(
                    '<div class="vas-199-trow vas-199-pickable" data-whid="' + r.warehouseId + '" data-whname="' + escapeHtml(r.warehouseName) + '">' +
                        '<span class="vas-199-cell vas-199-c-prim" title="' + escapeHtml(r.warehouseName) + '">' + escapeHtml(r.warehouseName) + '</span>' +
                        '<span class="vas-199-cell vas-199-right vas-199-c-std" title="' + r.drafted + '">' + formatNumber(r.drafted) + '</span>' +
                        '<span class="vas-199-cell vas-199-right vas-199-c-dark" title="' + r.inProcess + '">' + formatNumber(r.inProcess) + '</span>' +
                        '<span class="vas-199-cell vas-199-right vas-199-c-dark" title="' + r.completed + '">' + formatNumber(r.completed) + '</span>' +
                        '<span class="vas-199-cell vas-199-right vas-199-c-std" title="' + r.closed + '">' + formatNumber(r.closed) + '</span>' +
                        '<span class="vas-199-cell vas-199-right vas-199-c-emph" title="' + r.total + '">' + formatNumber(r.total) + '</span>' +
                    '</div>'
                );

                (function (whItem) {
                    $row.on('click', function () {
                        openWarehouseModal(whItem);
                    });
                })(r);

                $rowsContainer.append($row);
            }

            var helperText = lbl('VAS_199_Showing', 'Showing') + ' ' + (start + 1) + '–' + end + ' ' + lbl('VAS_199_Of', 'of') + ' ' + total + ' · ' + formatNumber(totalDocuments) + ' ' + lbl('VAS_199_Documents', 'documents') + ' · ' + lbl('VAS_199_SelectWarehouse', 'select a warehouse');
            $footer.find('.vas-199-helper').text(helperText);
            $footer.find('.vas-199-ptxt').text(currentPage + ' ' + lbl('VAS_199_Of', 'of') + ' ' + totalPages);

            $footer.find('.vas-199-prev').prop('disabled', currentPage <= 1);
            $footer.find('.vas-199-next').prop('disabled', currentPage >= totalPages);
        }

        /* ============================================================
           MODAL DRILL-DOWN SYSTEM (Warehouse POs & PO Lines)
           ============================================================ */

        function getPeriodLabel() {
            return MONTH_SHORT[selectedMonth - 1] + ' ' + selectedYear;
        }

        function ensureModalShell() {
            if ($modalMask && $modalMask[0]) { return; }

            $modalMask = $(
                '<div class="vas-199-mask" role="dialog" aria-modal="true">' +
                    '<div class="vas-199-modal" id="vas_199_modal">' +
                        '<div class="vas-199-modal-header">' +
                            '<div class="vas-199-htxt-wrap">' +
                                '<button type="button" class="vas-199-xbtn vas-199-mback" aria-label="' + escapeHtml(lbl('VAS_199_Back', 'Back')) + '" style="display:none;">' + ICON_BACK + '</button>' +
                                '<div class="vas-199-htxt">' +
                                    '<h2 class="vas-199-mtitle"></h2>' +
                                    '<div class="vas-199-msub"></div>' +
                                '</div>' +
                            '</div>' +
                            '<div class="vas-199-hact">' +
                                '<button type="button" class="vas-199-xbtn vas-199-mclose" aria-label="' + escapeHtml(lbl('VAS_199_Close', 'Close')) + '">' + ICON_CLOSE + '</button>' +
                            '</div>' +
                        '</div>' +
                        '<div class="vas-199-modal-body"></div>' +
                        '<div class="vas-199-modal-foot"></div>' +
                    '</div>' +
                '</div>'
            );

            $('body').append($modalMask);

            $modalMask.find('.vas-199-mclose').on('click', closeModal);
            $modalMask.find('.vas-199-mback').on('click', backModal);
            $modalMask.on('click', function (e) {
                if (e.target === this) { closeModal(); }
            });

            $(document).on('keydown.vas199', function (e) {
                if (e.key === 'Escape' && $modalMask && $modalMask.hasClass('vas-199-open')) {
                    closeModal();
                }
            });
        }

        function openModalShell(config, isBack) {
            ensureModalShell();

            if (!isBack) {
                if (config.child && currentModalConfig) {
                    modalStack.push(currentModalConfig);
                } else if (!config.child) {
                    modalStack = [];
                }
            }
            currentModalConfig = config;

            var $mBack = $modalMask.find('.vas-199-mback');
            if (modalStack.length > 0) {
                $mBack.show();
            } else {
                $mBack.hide();
            }

            var $dialog = $modalMask.find('.vas-199-modal');
            $dialog.removeClass('vas-199-modal-sm vas-199-modal-md');
            if (config.size) {
                $dialog.addClass('vas-199-modal-' + config.size);
            }

            $modalMask.find('.vas-199-mtitle').text(config.title || '');
            $modalMask.find('.vas-199-msub').text(config.subtitle || '');
            $modalMask.find('.vas-199-modal-body').html(config.bodyHtml || '');
            $modalMask.find('.vas-199-modal-foot').html(config.footHtml || '<span class="vas-199-foot-note"></span><button type="button" class="vas-199-btn vas-199-btn-close">' + escapeHtml(lbl('VAS_199_Close', 'Close')) + '</button>');

            $modalMask.find('.vas-199-btn-close').on('click', closeModal);
            $modalMask.find('.vas-199-btn-back').on('click', backModal);

            $modalMask.addClass('vas-199-open');

            if (typeof config.afterRender === 'function') {
                config.afterRender();
            }
        }

        function backModal() {
            var prev = modalStack.pop();
            if (!prev) {
                closeModal();
                return;
            }
            if (typeof prev.reopen === 'function') {
                prev.reopen();
            } else {
                openModalShell(prev, true);
            }
        }

        function closeModal() {
            if ($modalMask) {
                $modalMask.removeClass('vas-199-open');
            }
            modalStack = [];
            currentModalConfig = null;
        }

        // Drilldown 1: Warehouse Orders Modal
        function openWarehouseModal(whItem) {
            activeWarehouseId = whItem.warehouseId;
            activeWarehouseName = whItem.warehouseName;
            activeWarehouseStats = whItem;
            ordersPage = 1;

            var periodTxt = getPeriodLabel();
            var subtitle = lbl('VAS_199_POForWarehouse', 'Purchase orders for this warehouse') + ' · ' + periodTxt;

            var statsHtml =
                '<div class="vas-199-mstats">' +
                    '<div class="vas-199-mstat"><div class="vas-199-l">' + escapeHtml(lbl('VAS_199_TotalDocuments', 'Total documents')) + '</div><div class="vas-199-v" id="vas199_st_tot">' + formatNumber(whItem.total) + '</div></div>' +
                    '<div class="vas-199-mstat"><div class="vas-199-l">' + escapeHtml(lbl('VAS_199_Drafted', 'Drafted')) + '</div><div class="vas-199-v" id="vas199_st_dr">' + formatNumber(whItem.drafted) + '</div></div>' +
                    '<div class="vas-199-mstat"><div class="vas-199-l">' + escapeHtml(lbl('VAS_199_InProcess', 'In Process')) + '</div><div class="vas-199-v" id="vas199_st_ip">' + formatNumber(whItem.inProcess) + '</div></div>' +
                    '<div class="vas-199-mstat"><div class="vas-199-l">' + escapeHtml(lbl('VAS_199_Completed', 'Completed')) + '</div><div class="vas-199-v" id="vas199_st_co">' + formatNumber(whItem.completed) + '</div></div>' +
                    '<div class="vas-199-mstat"><div class="vas-199-l">' + escapeHtml(lbl('VAS_199_Closed', 'Closed')) + '</div><div class="vas-199-v" id="vas199_st_cl">' + formatNumber(whItem.closed) + '</div></div>' +
                '</div>';

            var bodyHtml =
                statsHtml +
                '<div class="vas-199-msec">' + escapeHtml(lbl('VAS_199_PurchaseOrders', 'Purchase orders')) + '</div>' +
                '<div class="vas-199-mtwrap" id="vas199_orders_wrap">' +
                    '<div class="vas-199-mtbl">' +
                        '<div class="vas-199-mrow vas-199-mhead vas-199-orders-grid">' +
                            '<span class="vas-199-cell"></span>' +
                            '<span class="vas-199-cell vas-199-c-link" title="' + escapeHtml(lbl('VAS_199_PONo', 'PO No')) + '">' + escapeHtml(lbl('VAS_199_PONo', 'PO No')) + '</span>' +
                            '<span class="vas-199-cell" title="' + escapeHtml(lbl('VAS_199_PODate', 'PO date')) + '">' + escapeHtml(lbl('VAS_199_PODate', 'PO date')) + '</span>' +
                            '<span class="vas-199-cell" title="' + escapeHtml(lbl('VAS_199_Vendor', 'Vendor')) + '">' + escapeHtml(lbl('VAS_199_Vendor', 'Vendor')) + '</span>' +
                            '<span class="vas-199-cell" title="' + escapeHtml(lbl('VAS_199_Warehouse', 'Warehouse')) + '">' + escapeHtml(lbl('VAS_199_Warehouse', 'Warehouse')) + '</span>' +
                            '<span class="vas-199-cell" title="' + escapeHtml(lbl('VAS_199_Representative', 'Representative')) + '">' + escapeHtml(lbl('VAS_199_Representative', 'Representative')) + '</span>' +
                            '<span class="vas-199-cell vas-199-right vas-199-c-emph" title="' + escapeHtml(lbl('VAS_199_Value', 'Value')) + '">' + escapeHtml(lbl('VAS_199_Value', 'Value')) + '</span>' +
                            '<span class="vas-199-cell" title="' + escapeHtml(lbl('VAS_199_Delivery', 'Delivery')) + '">' + escapeHtml(lbl('VAS_199_Delivery', 'Delivery')) + '</span>' +
                            '<span class="vas-199-cell" title="' + escapeHtml(lbl('VAS_199_Status', 'Status')) + '">' + escapeHtml(lbl('VAS_199_Status', 'Status')) + '</span>' +
                        '</div>' +
                        '<div class="vas-199-mbody" id="vas199_orders_body">' +
                            '<div class="vas-199-loading">' + escapeHtml(lbl('VAS_199_LoadingOrders', 'Loading purchase orders...')) + '</div>' +
                        '</div>' +
                    '</div>' +
                    '<div class="vas-199-mtfoot" id="vas199_orders_foot">' +
                        '<span class="vas-199-helper" id="vas199_orders_helper"></span>' +
                        '<span class="vas-199-pager" id="vas199_orders_pager" style="display:none;">' +
                            '<button type="button" class="vas-199-pbtn" id="vas199_ord_prev" aria-label="' + escapeHtml(lbl('VAS_199_Previous', 'Previous')) + '">' + ICON_CHEV_LEFT + '</button>' +
                            '<span class="vas-199-ptxt" id="vas199_ord_ptxt"></span>' +
                            '<button type="button" class="vas-199-pbtn" id="vas199_ord_next" aria-label="' + escapeHtml(lbl('VAS_199_Next', 'Next')) + '">' + ICON_CHEV_RIGHT + '</button>' +
                        '</span>' +
                    '</div>' +
                '</div>';

            var footHtml =
                '<span class="vas-199-foot-note" id="vas199_orders_footnote">' + escapeHtml(whItem.warehouseName + ' · ' + periodTxt) + '</span>' +
                '<button type="button" class="vas-199-btn vas-199-btn-close">' + escapeHtml(lbl('VAS_199_Close', 'Close')) + '</button>';

            openModalShell({
                child: false,
                title: whItem.warehouseName,
                subtitle: subtitle,
                bodyHtml: bodyHtml,
                footHtml: footHtml,
                reopen: function () {
                    openWarehouseModal(whItem);
                },
                afterRender: function () {
                    fetchWarehouseOrders();
                }
            });
        }

        function fetchWarehouseOrders() {
            $.ajax({
                url: VIS.Application.contextUrl + 'VAS_199_WarehouseDocumentStatusWidget/GetWarehouseOrders',
                type: 'GET',
                cache: false,
                data: { warehouseId: activeWarehouseId, month: selectedMonth, year: selectedYear },
                success: function (res) {
                    var data = typeof res === 'string' ? JSON.parse(res) : res;
                    if (data && !data.error) {
                        activeWarehouseOrders = data.orders || [];
                        $('#vas199_st_tot').text(formatNumber(data.totalDocuments));
                        $('#vas199_st_dr').text(formatNumber(data.drafted));
                        $('#vas199_st_ip').text(formatNumber(data.inProcess));
                        $('#vas199_st_co').text(formatNumber(data.completed));
                        $('#vas199_st_cl').text(formatNumber(data.closed));
                    } else {
                        activeWarehouseOrders = [];
                    }
                    renderOrdersPage();
                },
                error: function () {
                    activeWarehouseOrders = [];
                    renderOrdersPage();
                }
            });
        }

        function renderOrdersPage() {
            var $body = $('#vas199_orders_body');
            var $foot = $('#vas199_orders_foot');
            var $helper = $('#vas199_orders_helper');
            var $pager = $('#vas199_orders_pager');
            var $ptxt = $('#vas199_ord_ptxt');
            var $prev = $('#vas199_ord_prev');
            var $next = $('#vas199_ord_next');

            if (!$body.length) { return; }
            $body.empty();

            var total = activeWarehouseOrders.length;
            var totalPages = Math.max(1, Math.ceil(total / ORDERS_PAGE_SIZE));

            if (ordersPage > totalPages) { ordersPage = totalPages; }
            if (ordersPage < 1) { ordersPage = 1; }

            if (total === 0) {
                $body.html('<div class="vas-199-empty">' + escapeHtml(lbl('VAS_199_NoOrdersFound', 'No purchase orders found for this period')) + '</div>');
                $helper.text(lbl('VAS_199_Showing', 'Showing') + ' 0 ' + lbl('VAS_199_Of', 'of') + ' 0');
                $pager.hide();
                return;
            }

            var start = (ordersPage - 1) * ORDERS_PAGE_SIZE;
            var end = Math.min(start + ORDERS_PAGE_SIZE, total);
            var pageSlice = activeWarehouseOrders.slice(start, end);

            for (var i = 0; i < pageSlice.length; i++) {
                var o = pageSlice[i];
                var docStatusDisplay = getDocStatusText(o.docStatusCode);
                var deliveryStatusDisplay = getDeliveryStatusText(o.deliveryStatus);
                var $row = $(
                    '<div class="vas-199-mrow vas-199-orders-grid">' +
                        '<span class="vas-199-cell vas-199-center">' +
                            '<button type="button" class="vas-199-iconbtn" data-orderid="' + o.orderId + '" title="' + escapeHtml(lbl('VAS_199_ViewLinesOf', 'View lines of') + ' ' + o.documentNo) + '">' + ICON_LINES + '</button>' +
                        '</span>' +
                        '<span class="vas-199-cell">' +
                            '<button type="button" class="vas-199-lnk" data-orderid="' + o.orderId + '" title="' + escapeHtml(lbl('VAS_199_OpenRecord', 'Open record') + ' ' + o.documentNo) + '">' + escapeHtml(o.documentNo) + '</button>' +
                        '</span>' +
                        '<span class="vas-199-cell vas-199-c-std" title="' + escapeHtml(formatDate(o.dateOrdered)) + '">' + escapeHtml(formatDate(o.dateOrdered)) + '</span>' +
                        '<span class="vas-199-cell" title="' + escapeHtml(o.vendorName) + '">' + escapeHtml(o.vendorName) + '</span>' +
                        '<span class="vas-199-cell" title="' + escapeHtml(o.warehouseName) + '">' + escapeHtml(o.warehouseName) + '</span>' +
                        '<span class="vas-199-cell vas-199-c-std" title="' + escapeHtml(o.salesRepName) + '">' + escapeHtml(o.salesRepName) + '</span>' +
                        '<span class="vas-199-cell vas-199-right vas-199-c-emph" title="' + escapeHtml(formatAmount(o.orderTotal, o.currencySymbol)) + '">' + escapeHtml(formatAmount(o.orderTotal, o.currencySymbol)) + '</span>' +
                        '<span class="vas-199-cell" title="' + escapeHtml(deliveryStatusDisplay) + '"><span class="vas-199-chip vas-199-' + o.deliveryChip + '">' + escapeHtml(deliveryStatusDisplay) + '</span></span>' +
                        '<span class="vas-199-cell" title="' + escapeHtml(docStatusDisplay) + '"><span class="vas-199-chip vas-199-' + o.docStatusChip + '">' + escapeHtml(docStatusDisplay) + '</span></span>' +
                    '</div>'
                );

                (function (poItem) {
                    $row.find('.vas-199-iconbtn').on('click', function (e) {
                        e.stopPropagation();
                        openOrderLinesModal(poItem);
                    });
                    $row.find('.vas-199-lnk').on('click', function (e) {
                        e.stopPropagation();
                        zoomToPurchaseOrder(poItem.orderId);
                    });
                })(o);

                $body.append($row);
            }

            var helperText = lbl('VAS_199_Showing', 'Showing') + ' ' + (start + 1) + '–' + end + ' ' + lbl('VAS_199_Of', 'of') + ' ' + total + ' · ' + lbl('VAS_199_SelectPOToOpen', 'select a PO number to open the record');
            $helper.text(helperText);

            if (totalPages > 1) {
                $pager.show();
                $ptxt.text(ordersPage + ' ' + lbl('VAS_199_Of', 'of') + ' ' + totalPages);
                $prev.prop('disabled', ordersPage <= 1);
                $next.prop('disabled', ordersPage >= totalPages);

                $prev.off('click').on('click', function () {
                    if (ordersPage > 1) {
                        ordersPage--;
                        renderOrdersPage();
                    }
                });

                $next.off('click').on('click', function () {
                    if (ordersPage < totalPages) {
                        ordersPage++;
                        renderOrdersPage();
                    }
                });
            } else {
                $pager.hide();
            }
        }

        // Drilldown 2: Order Lines Modal
        function openOrderLinesModal(poItem) {
            linesPage = 1;
            var orderId = poItem.orderId;
            var title = lbl('VAS_199_POLines', 'Lines') + ' · ' + poItem.documentNo;
            var docStatusDisp = getDocStatusText(poItem.docStatusCode || poItem.docStatusName);
            var delivStatusDisp = getDeliveryStatusText(poItem.deliveryStatus);
            var subtitle = (poItem.vendorName || '') + ' · ' + formatDate(poItem.dateOrdered) + ' · ' + delivStatusDisp;

            var bodyHtml =
                '<div class="vas-199-polink">' +
                    escapeHtml(lbl('VAS_199_PurchaseOrders', 'Purchase order')) + ' ' +
                    '<button type="button" class="vas-199-lnk" id="vas199_hdr_link" data-orderid="' + orderId + '">' + escapeHtml(poItem.documentNo) + '</button> · ' +
                    escapeHtml(formatDate(poItem.dateOrdered)) + ' · ' +
                    escapeHtml(docStatusDisp) +
                '</div>' +
                '<div class="vas-199-mstats" id="vas199_lines_stats">' +
                    '<div class="vas-199-mstat"><div class="vas-199-l">' + escapeHtml(lbl('VAS_199_POLines', 'Lines')) + '</div><div class="vas-199-v" id="vas199_ln_cnt">' + formatNumber(poItem.lineCount) + '</div></div>' +
                    '<div class="vas-199-mstat"><div class="vas-199-l">' + escapeHtml(lbl('VAS_199_POValue', 'PO value')) + '</div><div class="vas-199-v" id="vas199_ln_val">' + formatAmount(poItem.orderTotal, poItem.currencySymbol) + '</div></div>' +
                    '<div class="vas-199-mstat"><div class="vas-199-l">' + escapeHtml(lbl('VAS_199_Ordered', 'Qty ordered')) + '</div><div class="vas-199-v" id="vas199_ln_ord">' + formatNumber(poItem.qtyOrdered) + '</div></div>' +
                    '<div class="vas-199-mstat"><div class="vas-199-l">' + escapeHtml(lbl('VAS_199_Pending', 'Qty pending')) + '</div><div class="vas-199-v" id="vas199_ln_pnd">' + formatNumber(poItem.qtyPending) + '</div></div>' +
                '</div>' +
                '<div class="vas-199-msec">' + escapeHtml(lbl('VAS_199_POLines', 'Purchase order lines')) + '</div>' +
                '<div class="vas-199-mtwrap" id="vas199_lines_wrap">' +
                    '<div class="vas-199-mtbl">' +
                        '<div class="vas-199-mrow vas-199-mhead vas-199-lines-grid">' +
                            '<span class="vas-199-cell vas-199-right" title="#">#</span>' +
                            '<span class="vas-199-cell vas-199-c-prim" title="' + escapeHtml(lbl('VAS_199_Product', 'Product')) + '">' + escapeHtml(lbl('VAS_199_Product', 'Product')) + '</span>' +
                            '<span class="vas-199-cell" title="' + escapeHtml(lbl('VAS_199_Attribute', 'Attribute')) + '">' + escapeHtml(lbl('VAS_199_Attribute', 'Attribute')) + '</span>' +
                            '<span class="vas-199-cell" title="' + escapeHtml(lbl('VAS_199_UoM', 'UoM')) + '">' + escapeHtml(lbl('VAS_199_UoM', 'UoM')) + '</span>' +
                            '<span class="vas-199-cell vas-199-right" title="' + escapeHtml(lbl('VAS_199_Ordered', 'Ordered')) + '">' + escapeHtml(lbl('VAS_199_Ordered', 'Ordered')) + '</span>' +
                            '<span class="vas-199-cell vas-199-right" title="' + escapeHtml(lbl('VAS_199_Received', 'Received')) + '">' + escapeHtml(lbl('VAS_199_Received', 'Received')) + '</span>' +
                            '<span class="vas-199-cell vas-199-right vas-199-c-prim" title="' + escapeHtml(lbl('VAS_199_Pending', 'Pending')) + '">' + escapeHtml(lbl('VAS_199_Pending', 'Pending')) + '</span>' +
                            '<span class="vas-199-cell vas-199-right" title="' + escapeHtml(lbl('VAS_199_Rate', 'Rate')) + '">' + escapeHtml(lbl('VAS_199_Rate', 'Rate')) + '</span>' +
                            '<span class="vas-199-cell vas-199-right vas-199-c-emph" title="' + escapeHtml(lbl('VAS_199_Amount', 'Amount')) + '">' + escapeHtml(lbl('VAS_199_Amount', 'Amount')) + '</span>' +
                            '<span class="vas-199-cell" title="' + escapeHtml(lbl('VAS_199_LineStatus', 'Line status')) + '">' + escapeHtml(lbl('VAS_199_LineStatus', 'Line status')) + '</span>' +
                        '</div>' +
                        '<div class="vas-199-mbody" id="vas199_lines_body">' +
                            '<div class="vas-199-loading">' + escapeHtml(lbl('VAS_199_LoadingLines', 'Loading lines...')) + '</div>' +
                        '</div>' +
                    '</div>' +
                    '<div class="vas-199-mtfoot" id="vas199_lines_foot">' +
                        '<span class="vas-199-helper" id="vas199_lines_helper"></span>' +
                        '<span class="vas-199-pager" id="vas199_lines_pager" style="display:none;">' +
                            '<button type="button" class="vas-199-pbtn" id="vas199_ln_prev" aria-label="' + escapeHtml(lbl('VAS_199_Previous', 'Previous')) + '">' + ICON_CHEV_LEFT + '</button>' +
                            '<span class="vas-199-ptxt" id="vas199_ln_ptxt"></span>' +
                            '<button type="button" class="vas-199-pbtn" id="vas199_ln_next" aria-label="' + escapeHtml(lbl('VAS_199_Next', 'Next')) + '">' + ICON_CHEV_RIGHT + '</button>' +
                        '</span>' +
                    '</div>' +
                '</div>';

            var footHtml =
                '<span class="vas-199-foot-note" id="vas199_lines_footnote">' + escapeHtml(poItem.documentNo + ' · ' + poItem.vendorName) + '</span>' +
                '<span>' +
                    '<button type="button" class="vas-199-btn vas-199-btn-back">' + escapeHtml(lbl('VAS_199_Back', 'Back')) + '</button> ' +
                    '<button type="button" class="vas-199-btn vas-199-btn-close">' + escapeHtml(lbl('VAS_199_Close', 'Close')) + '</button>' +
                '</span>';

            openModalShell({
                child: true,
                size: 'md',
                title: title,
                subtitle: subtitle,
                bodyHtml: bodyHtml,
                footHtml: footHtml,
                reopen: function () {
                    openOrderLinesModal(poItem);
                },
                afterRender: function () {
                    $('#vas199_hdr_link').on('click', function () {
                        zoomToPurchaseOrder(orderId);
                    });
                    fetchOrderLines(orderId, poItem);
                }
            });
        }

        function fetchOrderLines(orderId, fallbackPo) {
            $.ajax({
                url: VIS.Application.contextUrl + 'VAS_199_WarehouseDocumentStatusWidget/GetOrderLines',
                type: 'GET',
                cache: false,
                data: { orderId: orderId },
                success: function (res) {
                    var data = typeof res === 'string' ? JSON.parse(res) : res;
                    if (data && !data.error) {
                        activeOrderHeader = data.header || fallbackPo;
                        activeOrderLines = data.lines || [];
                        $('#vas199_ln_cnt').text(formatNumber(data.totalLines));
                        if (activeOrderHeader) {
                            $('#vas199_ln_val').text(formatAmount(activeOrderHeader.grandTotal, activeOrderHeader.currencySymbol));
                        }
                        $('#vas199_ln_ord').text(formatNumber(data.totalQtyOrdered));
                        $('#vas199_ln_pnd').text(formatNumber(data.totalQtyPending));
                        var delStatusLabel = getDeliveryStatusText(data.deliveryStatus);
                        $('#vas199_lines_footnote').text(activeOrderLines.length + ' ' + lbl('VAS_199_POLines', 'lines') + ' · ' + formatNumber(data.totalQtyOrdered) + ' ' + lbl('VAS_199_QtyOrdered', 'qty ordered') + ' · ' + delStatusLabel);
                    } else {
                        activeOrderLines = [];
                    }
                    renderLinesPage();
                },
                error: function () {
                    activeOrderLines = [];
                    renderLinesPage();
                }
            });
        }

        function renderLinesPage() {
            var $body = $('#vas199_lines_body');
            var $helper = $('#vas199_lines_helper');
            var $pager = $('#vas199_lines_pager');
            var $ptxt = $('#vas199_ln_ptxt');
            var $prev = $('#vas199_ln_prev');
            var $next = $('#vas199_ln_next');

            if (!$body.length) { return; }
            $body.empty();

            var total = activeOrderLines.length;
            var totalPages = Math.max(1, Math.ceil(total / LINES_PAGE_SIZE));

            if (linesPage > totalPages) { linesPage = totalPages; }
            if (linesPage < 1) { linesPage = 1; }

            if (total === 0) {
                $body.html('<div class="vas-199-empty">' + escapeHtml(lbl('VAS_199_NoLinesFound', 'No order lines found')) + '</div>');
                $helper.text(lbl('VAS_199_Showing', 'Showing') + ' 0 ' + lbl('VAS_199_Of', 'of') + ' 0');
                $pager.hide();
                return;
            }

            var start = (linesPage - 1) * LINES_PAGE_SIZE;
            var end = Math.min(start + LINES_PAGE_SIZE, total);
            var pageSlice = activeOrderLines.slice(start, end);
            var curSymbol = activeOrderHeader ? activeOrderHeader.currencySymbol : '';

            for (var i = 0; i < pageSlice.length; i++) {
                var l = pageSlice[i];
                var lineStatusDisplay = getLineStatusText(l.lineStatus);
                var $row = $(
                    '<div class="vas-199-mrow vas-199-lines-grid">' +
                        '<span class="vas-199-cell vas-199-right vas-199-c-std" title="' + l.lineIndex + '">' + l.lineIndex + '</span>' +
                        '<span class="vas-199-cell vas-199-c-prim" title="' + escapeHtml(l.productName) + '">' + escapeHtml(l.productName) + '</span>' +
                        '<span class="vas-199-cell vas-199-c-std" title="' + escapeHtml(l.attributeDesc) + '">' + escapeHtml(l.attributeDesc) + '</span>' +
                        '<span class="vas-199-cell" title="' + escapeHtml(l.uomName) + '">' + escapeHtml(l.uomName) + '</span>' +
                        '<span class="vas-199-cell vas-199-right" title="' + formatNumber(l.qtyOrdered) + '">' + formatNumber(l.qtyOrdered) + '</span>' +
                        '<span class="vas-199-cell vas-199-right" title="' + formatNumber(l.qtyDelivered) + '">' + formatNumber(l.qtyDelivered) + '</span>' +
                        '<span class="vas-199-cell vas-199-right vas-199-c-prim" title="' + formatNumber(l.qtyPending) + '">' + formatNumber(l.qtyPending) + '</span>' +
                        '<span class="vas-199-cell vas-199-right" title="' + formatAmount(l.priceActual, curSymbol) + '">' + formatAmount(l.priceActual, curSymbol) + '</span>' +
                        '<span class="vas-199-cell vas-199-right vas-199-c-emph" title="' + formatAmount(l.lineNetAmt, curSymbol) + '">' + formatAmount(l.lineNetAmt, curSymbol) + '</span>' +
                        '<span class="vas-199-cell" title="' + escapeHtml(lineStatusDisplay) + '"><span class="vas-199-chip vas-199-' + l.lineChip + '">' + escapeHtml(lineStatusDisplay) + '</span></span>' +
                    '</div>'
                );
                $body.append($row);
            }

            var helperText = lbl('VAS_199_Showing', 'Showing') + ' ' + (start + 1) + '–' + end + ' ' + lbl('VAS_199_Of', 'of') + ' ' + total + ' · ' + lbl('VAS_199_LinesOf', 'lines of') + ' ' + (activeOrderHeader ? activeOrderHeader.documentNo : '');
            $helper.text(helperText);

            if (totalPages > 1) {
                $pager.show();
                $ptxt.text(linesPage + ' ' + lbl('VAS_199_Of', 'of') + ' ' + totalPages);
                $prev.prop('disabled', linesPage <= 1);
                $next.prop('disabled', linesPage >= totalPages);

                $prev.off('click').on('click', function () {
                    if (linesPage > 1) {
                        linesPage--;
                        renderLinesPage();
                    }
                });

                $next.off('click').on('click', function () {
                    if (linesPage < totalPages) {
                        linesPage++;
                        renderLinesPage();
                    }
                });
            } else {
                $pager.hide();
            }
        }

        // Direct record navigation to VAS_PurchaseOrder screen
        function zoomToPurchaseOrder(orderId) {
            if (!orderId) { return; }
            closeModal();
            try {
                $self.widgetFirevalueChanged({
                    "TabWhereClause": "C_Order.C_Order_ID=" + orderId,
                    "TabLayout": "Y",
                    "TabIndex": "0",
                    "ActionName": "VAS_PurchaseOrder",
                    "ActionType": "W"
                });
            } catch (e) {
                /* zoom is best-effort */
            }
        }

        this.getRoot = function () {
            return $wrapper;
        };

        this.refreshWidget = function () {
            loadData();
        };

        this.disposeComponent = function () {
            closeModal();
            $(document).off('keydown.vas199');
            if ($modalMask) {
                $modalMask.remove();
                $modalMask = null;
            }
            $root.remove();
            $wrapper.remove();
        };
    };

    VAS.VAS_199_WarehouseDocumentStatusWidget.prototype.init = function (windowNo, frame) {
        this.frame = frame;
        this.AD_UserHomeWidgetID = frame.widgetInfo ? frame.widgetInfo.AD_UserHomeWidgetID : 0;
        this.windowNo = windowNo;
        this.Initalize();
        this.frame.getContentGrid().append(this.getRoot());
        ensureDashInlineSizeVar(this.getRoot());
    };

    VAS.VAS_199_WarehouseDocumentStatusWidget.prototype.widgetSizeChange = function (height, width) { };

    VAS.VAS_199_WarehouseDocumentStatusWidget.prototype.refreshWidget = function () {
        this.refreshWidget();
    };

    VAS.VAS_199_WarehouseDocumentStatusWidget.prototype.addChangeListener = function (listener) {
        this.listener = listener;
    };

    VAS.VAS_199_WarehouseDocumentStatusWidget.prototype.widgetFirevalueChanged = function (value) {
        if (this.listener) {
            this.listener.widgetFirevalueChanged(value);
        }
    };

    VAS.VAS_199_WarehouseDocumentStatusWidget.prototype.dispose = function () {
        this.disposeComponent();
        if (this.frame) {
            this.frame.dispose();
        }
        this.frame = null;
    };

    // Alias registration for dynamic widget loading
    VAS.VAS_WarehouseDocumentStatusWidget = VAS.VAS_199_WarehouseDocumentStatusWidget;

})(VAS, jQuery);
