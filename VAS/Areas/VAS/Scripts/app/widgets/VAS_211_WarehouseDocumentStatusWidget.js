/**
 * Warehouse wise PO · Document Status Widget (Purchase Order Dashboard - Widget 09)
 * Purpose - 4x2 matrix widget displaying receiving warehouses against real C_Order
 *           document statuses (Drafted, In Process, Completed, Closed) for a selected
 *           month/year. Clicking any warehouse row opens a drill-down modal listing
 *           all purchase orders for that warehouse, with full PO line inspection and
 *           direct record navigation into the VAS_PurchaseOrder window.
 * Prefix  - VAS_211_
 *
 * Message Keys / Localization Table:
 *  #  | Default English Text                              | Message Key
 * ----+---------------------------------------------------+-----------------------------------
 *  1  | Warehouse wise PO · Document Status               | VAS_211_WarehouseDocumentStatus
 *  2  | Warehouse                                         | VAS_211_Warehouse
 *  3  | Drafted                                           | VAS_211_Drafted
 *  4  | In Process                                        | VAS_211_InProcess
 *  5  | Completed                                         | VAS_211_Completed
 *  6  | Closed                                            | VAS_211_Closed
 *  7  | Total                                             | VAS_211_Total
 *  8  | Total documents                                   | VAS_211_TotalDocuments
 *  9  | documents                                         | VAS_211_Documents
 * 10  | select a warehouse                                | VAS_211_SelectWarehouse
 * 11  | Showing                                           | VAS_211_Showing
 * 12  | of                                                | VAS_211_Of
 * 13  | Previous                                          | VAS_211_Previous
 * 14  | Next                                              | VAS_211_Next
 * 15  | Purchase orders for this warehouse                | VAS_211_POForWarehouse
 * 16  | Purchase orders                                   | VAS_211_PurchaseOrders
 * 17  | PO No                                             | VAS_211_PONo
 * 18  | PO date                                           | VAS_211_PODate
 * 19  | Vendor                                            | VAS_211_Vendor
 * 20  | Representative                                    | VAS_211_Representative
 * 21  | Value                                             | VAS_211_Value
 * 22  | Delivery                                          | VAS_211_Delivery
 * 23  | Status                                            | VAS_211_Status
 * 24  | View lines of                                     | VAS_211_ViewLinesOf
 * 25  | Open record                                       | VAS_211_OpenRecord
 * 26  | Expected on                                       | VAS_211_ExpectedOn
 * 27  | PO value                                          | VAS_211_POValue
 * 28  | Created by                                        | VAS_211_CreatedBy
 * 29  | Document status                                   | VAS_211_DocumentStatus
 * 30  | Delivery status                                   | VAS_211_DeliveryStatus
 * 31  | Purchase order lines                              | VAS_211_POLines
 * 32  | Product                                           | VAS_211_Product
 * 33  | Attribute                                         | VAS_211_Attribute
 * 34  | UoM                                               | VAS_211_UoM
 * 35  | Ordered                                           | VAS_211_Ordered
 * 36  | Received                                          | VAS_211_Received
 * 37  | Pending                                           | VAS_211_Pending
 * 38  | Rate                                              | VAS_211_Rate
 * 39  | Amount                                            | VAS_211_Amount
 * 40  | Line status                                       | VAS_211_LineStatus
 * 41  | Partial received                                  | VAS_211_PartialReceived
 * 42  | Fully delivered                                   | VAS_211_FullyDelivered
 * 43  | Not applicable                                    | VAS_211_NotApplicable
 * 44  | Back                                              | VAS_211_Back
 * 45  | Close                                             | VAS_211_Close
 * 46  | No purchase orders found for this period          | VAS_211_NoOrdersFound
 * 47  | No order lines found                              | VAS_211_NoLinesFound
 * 48  | Loading purchase orders...                        | VAS_211_LoadingOrders
 * 49  | Loading lines...                                  | VAS_211_LoadingLines
 * 50  | select a PO number to open the record             | VAS_211_SelectPOToOpen
 * 51  | lines of                                          | VAS_211_LinesOf
 * 52  | qty ordered                                       | VAS_211_QtyOrdered
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

    /* The controller returns dates through ASP.NET MVC's JavaScriptSerializer, which
       writes DateTime as "/Date(1755302400000)/". new Date() cannot parse that form, so
       the raw string used to be printed instead of a date. Unwrap it first, then fall
       back to the native parser for ISO / already-formatted values. */
    function parseDateValue(dateVal) {
        if (dateVal === null || dateVal === undefined || dateVal === '') { return null; }
        if (dateVal instanceof Date) { return isNaN(dateVal.getTime()) ? null : dateVal; }
        if (typeof dateVal === 'number') { return new Date(dateVal); }

        var raw = String(dateVal);
        var msMatch = /\/Date\((-?\d+)([+-]\d{4})?\)\//.exec(raw);
        if (msMatch) {
            var parsed = new Date(parseInt(msMatch[1], 10));
            return isNaN(parsed.getTime()) ? null : parsed;
        }

        var d = new Date(raw);
        return isNaN(d.getTime()) ? null : d;
    }

    var DASH = '–';

    function formatDate(dateVal) {
        var d = parseDateValue(dateVal);
        if (!d) { return (dateVal ? String(dateVal) : '—'); }
        return d.toLocaleDateString(window.navigator.language, { day: '2-digit', month: 'short', year: 'numeric' });
    }

    function getDocStatusText(code) {
        if (!code) { return ''; }
        var c = String(code).toUpperCase();
        if (c === 'DR') { return lbl('VAS_211_Drafted', 'Drafted'); }
        if (c === 'IP') { return lbl('VAS_211_InProcess', 'In Process'); }
        if (c === 'CO') { return lbl('VAS_211_Completed', 'Completed'); }
        if (c === 'CL') { return lbl('VAS_211_Closed', 'Closed'); }
        return code;
    }

    function getDeliveryStatusText(status) {
        if (!status) { return ''; }
        if (status === 'Fully delivered') { return lbl('VAS_211_FullyDelivered', 'Fully delivered'); }
        if (status === 'Partial') { return lbl('VAS_211_Partial', 'Partial'); }
        if (status === 'Pending') { return lbl('VAS_211_Pending', 'Pending'); }
        if (status === 'Not applicable') { return lbl('VAS_211_NotApplicable', 'Not applicable'); }
        return status;
    }

    function getLineStatusText(status) {
        if (!status) { return ''; }
        if (status === 'Drafted') { return lbl('VAS_211_Drafted', 'Drafted'); }
        if (status === 'Received') { return lbl('VAS_211_Received', 'Received'); }
        if (status === 'Partial received') { return lbl('VAS_211_PartialReceived', 'Partial received'); }
        if (status === 'Pending') { return lbl('VAS_211_Pending', 'Pending'); }
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

    VAS.VAS_211_WarehouseDocumentStatusWidget = function () {

        this.frame;
        this.windowNo;
        this.AD_UserHomeWidgetID = 0;

        var $self = this;
        var $wrapper = $('<div class="vas-211-container">');
        var $root = $('<div class="vas-211-root">');

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
        // Rows per page in the modal table. The stylesheet sizes the table body to
        // exactly this many rows (--vas-211-rows), so the two must stay in step.
        var ORDERS_PAGE_SIZE = 6;

        // PO Lines pagination state
        var activeOrderHeader = null;
        var activeOrderLines = [];
        var activeOrderStats = {};
        var linesPage = 1;
        var LINES_PAGE_SIZE = 6;

        this.Initalize = function () {
            buildWidget();
            loadYears();
        };

        function buildWidget() {
            var $head = $('<div class="vas-211-head">');

            var $headTxt = $('<div class="vas-211-head-txt">');
            var $title = $('<h3 class="vas-211-title">').text(lbl('VAS_211_WarehouseDocumentStatus', 'Warehouse wise PO · Document Status'));
            $headTxt.append($title);

            var $mfilter = $('<div class="vas-211-mfilter">');
            $monthSelect = $('<select class="vas-211-msel vas-211-month" aria-label="' + escapeHtml(lbl('Month', 'Month')) + '">');
            $yearSelect = $('<select class="vas-211-msel vas-211-year" aria-label="' + escapeHtml(lbl('Year', 'Year')) + '">');

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
            var $tbl = $('<div class="vas-211-tbl">');
            var $thead = $(
                '<div class="vas-211-trow vas-211-thead">' +
                    '<span class="vas-211-cell" title="' + escapeHtml(lbl('VAS_211_Warehouse', 'Warehouse')) + '">' + escapeHtml(lbl('VAS_211_Warehouse', 'Warehouse')) + '</span>' +
                    '<span class="vas-211-cell vas-211-right" title="' + escapeHtml(lbl('VAS_211_Drafted', 'Drafted')) + '">' + escapeHtml(lbl('VAS_211_Drafted', 'Drafted')) + '</span>' +
                    '<span class="vas-211-cell vas-211-right" title="' + escapeHtml(lbl('VAS_211_InProcess', 'In Process')) + '">' + escapeHtml(lbl('VAS_211_InProcess', 'In Process')) + '</span>' +
                    '<span class="vas-211-cell vas-211-right" title="' + escapeHtml(lbl('VAS_211_Completed', 'Completed')) + '">' + escapeHtml(lbl('VAS_211_Completed', 'Completed')) + '</span>' +
                    '<span class="vas-211-cell vas-211-right" title="' + escapeHtml(lbl('VAS_211_Closed', 'Closed')) + '">' + escapeHtml(lbl('VAS_211_Closed', 'Closed')) + '</span>' +
                    '<span class="vas-211-cell vas-211-right" title="' + escapeHtml(lbl('VAS_211_Total', 'Total')) + '">' + escapeHtml(lbl('VAS_211_Total', 'Total')) + '</span>' +
                '</div>'
            );
            $tbl.append($thead);

            $rowsContainer = $('<div class="vas-211-tbody">');
            $tbl.append($rowsContainer);
            $root.append($tbl);

            // Widget Footer Pager
            $footer = $(
                '<div class="vas-211-wfoot">' +
                    '<span class="vas-211-helper"></span>' +
                    '<div class="vas-211-pager">' +
                        '<button type="button" class="vas-211-pbtn vas-211-prev" aria-label="' + escapeHtml(lbl('VAS_211_Previous', 'Previous')) + '">' + ICON_CHEV_LEFT + '</button>' +
                        '<span class="vas-211-ptxt"></span>' +
                        '<button type="button" class="vas-211-pbtn vas-211-next" aria-label="' + escapeHtml(lbl('VAS_211_Next', 'Next')) + '">' + ICON_CHEV_RIGHT + '</button>' +
                    '</div>' +
                '</div>'
            );
            $root.append($footer);

            $busy = $('<div class="vas-211-busy vas-211-hidden"><div class="vis-busyindicatorinnerwrap"><i class="vis_widgetloader"></i></div></div>');
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

            $footer.find('.vas-211-prev').on('click', function (e) {
                e.stopPropagation();
                if (currentPage > 1) {
                    currentPage--;
                    renderRows();
                }
            });

            $footer.find('.vas-211-next').on('click', function (e) {
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
                $busy.toggleClass('vas-211-hidden', !show);
            }
        }

        function loadYears() {
            $.ajax({
                url: VIS.Application.contextUrl + 'VAS_211_WarehouseDocumentStatusWidget/GetAvailableYears',
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
                url: VIS.Application.contextUrl + 'VAS_211_WarehouseDocumentStatusWidget/GetWarehouseDocumentStatus',
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
                $rowsContainer.html('<div class="vas-211-empty">' + escapeHtml(lbl('VAS_211_NoOrdersFound', 'No purchase orders found for this period')) + '</div>');
                $footer.find('.vas-211-helper').text(lbl('VAS_211_Showing', 'Showing') + ' 0 ' + lbl('VAS_211_Of', 'of') + ' 0');
                $footer.find('.vas-211-ptxt').text('1 ' + lbl('VAS_211_Of', 'of') + ' 1');
                $footer.find('.vas-211-prev').prop('disabled', true);
                $footer.find('.vas-211-next').prop('disabled', true);
                return;
            }

            var start = (currentPage - 1) * PAGE_SIZE;
            var end = Math.min(start + PAGE_SIZE, total);
            var pageSlice = warehouseData.slice(start, end);

            for (var i = 0; i < pageSlice.length; i++) {
                var r = pageSlice[i];
                var $row = $(
                    '<div class="vas-211-trow vas-211-pickable" data-whid="' + r.warehouseId + '" data-whname="' + escapeHtml(r.warehouseName) + '">' +
                        '<span class="vas-211-cell vas-211-c-prim" title="' + escapeHtml(r.warehouseName) + '">' + escapeHtml(r.warehouseName) + '</span>' +
                        '<span class="vas-211-cell vas-211-right vas-211-c-std" title="' + r.drafted + '">' + formatNumber(r.drafted) + '</span>' +
                        '<span class="vas-211-cell vas-211-right vas-211-c-dark" title="' + r.inProcess + '">' + formatNumber(r.inProcess) + '</span>' +
                        '<span class="vas-211-cell vas-211-right vas-211-c-dark" title="' + r.completed + '">' + formatNumber(r.completed) + '</span>' +
                        '<span class="vas-211-cell vas-211-right vas-211-c-std" title="' + r.closed + '">' + formatNumber(r.closed) + '</span>' +
                        '<span class="vas-211-cell vas-211-right vas-211-c-emph" title="' + r.total + '">' + formatNumber(r.total) + '</span>' +
                    '</div>'
                );

                (function (whItem) {
                    $row.on('click', function () {
                        openWarehouseModal(whItem);
                    });
                })(r);

                $rowsContainer.append($row);
            }

            var helperText = lbl('VAS_211_Showing', 'Showing') + ' ' + (start + 1) + '–' + end + ' ' + lbl('VAS_211_Of', 'of') + ' ' + total + ' · ' + formatNumber(totalDocuments) + ' ' + lbl('VAS_211_Documents', 'documents') + ' · ' + lbl('VAS_211_SelectWarehouse', 'select a warehouse');
            $footer.find('.vas-211-helper').text(helperText);
            $footer.find('.vas-211-ptxt').text(currentPage + ' ' + lbl('VAS_211_Of', 'of') + ' ' + totalPages);

            $footer.find('.vas-211-prev').prop('disabled', currentPage <= 1);
            $footer.find('.vas-211-next').prop('disabled', currentPage >= totalPages);
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
                '<div class="vas-211-mask" role="dialog" aria-modal="true">' +
                    '<div class="vas-211-modal" id="vas_211_modal">' +
                        '<div class="vas-211-modal-header">' +
                            '<div class="vas-211-htxt-wrap">' +
                                '<button type="button" class="vas-211-xbtn vas-211-mback" aria-label="' + escapeHtml(lbl('VAS_211_Back', 'Back')) + '" style="display:none;">' + ICON_BACK + '</button>' +
                                '<div class="vas-211-htxt">' +
                                    '<h2 class="vas-211-mtitle"></h2>' +
                                    '<div class="vas-211-msub"></div>' +
                                '</div>' +
                            '</div>' +
                            '<div class="vas-211-hact">' +
                                '<button type="button" class="vas-211-xbtn vas-211-mclose" aria-label="' + escapeHtml(lbl('VAS_211_Close', 'Close')) + '">' + ICON_CLOSE + '</button>' +
                            '</div>' +
                        '</div>' +
                        '<div class="vas-211-modal-body"></div>' +
                        '<div class="vas-211-modal-foot"></div>' +
                    '</div>' +
                '</div>'
            );

            $('body').append($modalMask);

            $modalMask.find('.vas-211-mclose').on('click', closeModal);
            $modalMask.find('.vas-211-mback').on('click', backModal);
            $modalMask.on('click', function (e) {
                if (e.target === this) { closeModal(); }
            });

            $(document).on('keydown.vas211', function (e) {
                if (e.key === 'Escape' && $modalMask && $modalMask.hasClass('vas-211-open')) {
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

            var $mBack = $modalMask.find('.vas-211-mback');
            if (modalStack.length > 0) {
                $mBack.show();
            } else {
                $mBack.hide();
            }

            var $dialog = $modalMask.find('.vas-211-modal');
            $dialog.removeClass('vas-211-modal-sm vas-211-modal-md');
            if (config.size) {
                $dialog.addClass('vas-211-modal-' + config.size);
            }

            $modalMask.find('.vas-211-mtitle').text(config.title || '');
            $modalMask.find('.vas-211-msub').text(config.subtitle || '');
            $modalMask.find('.vas-211-modal-body').html(config.bodyHtml || '');
            $modalMask.find('.vas-211-modal-foot').html(config.footHtml || '<span class="vas-211-foot-note"></span><button type="button" class="vas-211-btn vas-211-btn-close">' + escapeHtml(lbl('VAS_211_Close', 'Close')) + '</button>');

            $modalMask.find('.vas-211-btn-close').on('click', closeModal);
            $modalMask.find('.vas-211-btn-back').on('click', backModal);

            $modalMask.addClass('vas-211-open');

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
                $modalMask.removeClass('vas-211-open');
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
            var subtitle = lbl('VAS_211_POForWarehouse', 'Purchase orders for this warehouse') + ' · ' + periodTxt;

            var statsHtml =
                '<div class="vas-211-mstats">' +
                    '<div class="vas-211-mstat"><div class="vas-211-l">' + escapeHtml(lbl('VAS_211_TotalDocuments', 'Total documents')) + '</div><div class="vas-211-v" id="vas211_st_tot">' + formatNumber(whItem.total) + '</div></div>' +
                    '<div class="vas-211-mstat"><div class="vas-211-l">' + escapeHtml(lbl('VAS_211_Drafted', 'Drafted')) + '</div><div class="vas-211-v" id="vas211_st_dr">' + formatNumber(whItem.drafted) + '</div></div>' +
                    '<div class="vas-211-mstat"><div class="vas-211-l">' + escapeHtml(lbl('VAS_211_InProcess', 'In Process')) + '</div><div class="vas-211-v" id="vas211_st_ip">' + formatNumber(whItem.inProcess) + '</div></div>' +
                    '<div class="vas-211-mstat"><div class="vas-211-l">' + escapeHtml(lbl('VAS_211_Completed', 'Completed')) + '</div><div class="vas-211-v" id="vas211_st_co">' + formatNumber(whItem.completed) + '</div></div>' +
                    '<div class="vas-211-mstat"><div class="vas-211-l">' + escapeHtml(lbl('VAS_211_Closed', 'Closed')) + '</div><div class="vas-211-v" id="vas211_st_cl">' + formatNumber(whItem.closed) + '</div></div>' +
                '</div>';

            var bodyHtml =
                statsHtml +
                '<div class="vas-211-msec">' + escapeHtml(lbl('VAS_211_PurchaseOrders', 'Purchase orders')) + '</div>' +
                '<div class="vas-211-mtwrap" id="vas211_orders_wrap">' +
                    '<div class="vas-211-mtbl">' +
                        '<div class="vas-211-mrow vas-211-mhead vas-211-orders-grid">' +
                            '<span class="vas-211-cell"></span>' +
                            '<span class="vas-211-cell vas-211-c-link" title="' + escapeHtml(lbl('VAS_211_PONo', 'PO No')) + '">' + escapeHtml(lbl('VAS_211_PONo', 'PO No')) + '</span>' +
                            '<span class="vas-211-cell" title="' + escapeHtml(lbl('VAS_211_PODate', 'PO date')) + '">' + escapeHtml(lbl('VAS_211_PODate', 'PO date')) + '</span>' +
                            '<span class="vas-211-cell" title="' + escapeHtml(lbl('VAS_211_Vendor', 'Vendor')) + '">' + escapeHtml(lbl('VAS_211_Vendor', 'Vendor')) + '</span>' +
                            '<span class="vas-211-cell" title="' + escapeHtml(lbl('VAS_211_Warehouse', 'Warehouse')) + '">' + escapeHtml(lbl('VAS_211_Warehouse', 'Warehouse')) + '</span>' +
                            '<span class="vas-211-cell" title="' + escapeHtml(lbl('VAS_211_Representative', 'Representative')) + '">' + escapeHtml(lbl('VAS_211_Representative', 'Representative')) + '</span>' +
                            '<span class="vas-211-cell vas-211-right vas-211-c-emph" title="' + escapeHtml(lbl('VAS_211_Value', 'Value')) + '">' + escapeHtml(lbl('VAS_211_Value', 'Value')) + '</span>' +
                            '<span class="vas-211-cell" title="' + escapeHtml(lbl('VAS_211_Delivery', 'Delivery')) + '">' + escapeHtml(lbl('VAS_211_Delivery', 'Delivery')) + '</span>' +
                            '<span class="vas-211-cell" title="' + escapeHtml(lbl('VAS_211_Status', 'Status')) + '">' + escapeHtml(lbl('VAS_211_Status', 'Status')) + '</span>' +
                        '</div>' +
                        '<div class="vas-211-mbody" id="vas211_orders_body">' +
                            '<div class="vas-211-loading">' + escapeHtml(lbl('VAS_211_LoadingOrders', 'Loading purchase orders...')) + '</div>' +
                        '</div>' +
                    '</div>' +
                    '<div class="vas-211-mtfoot" id="vas211_orders_foot">' +
                        '<span class="vas-211-helper" id="vas211_orders_helper"></span>' +
                        '<span class="vas-211-pager" id="vas211_orders_pager" style="display:none;">' +
                            '<button type="button" class="vas-211-pbtn" id="vas211_ord_prev" aria-label="' + escapeHtml(lbl('VAS_211_Previous', 'Previous')) + '">' + ICON_CHEV_LEFT + '</button>' +
                            '<span class="vas-211-ptxt" id="vas211_ord_ptxt"></span>' +
                            '<button type="button" class="vas-211-pbtn" id="vas211_ord_next" aria-label="' + escapeHtml(lbl('VAS_211_Next', 'Next')) + '">' + ICON_CHEV_RIGHT + '</button>' +
                        '</span>' +
                    '</div>' +
                '</div>';

            var footHtml =
                '<span class="vas-211-foot-note" id="vas211_orders_footnote">' + escapeHtml(whItem.warehouseName + ' · ' + periodTxt) + '</span>' +
                '<button type="button" class="vas-211-btn vas-211-btn-close">' + escapeHtml(lbl('VAS_211_Close', 'Close')) + '</button>';

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
                url: VIS.Application.contextUrl + 'VAS_211_WarehouseDocumentStatusWidget/GetWarehouseOrders',
                type: 'GET',
                cache: false,
                data: { warehouseId: activeWarehouseId, month: selectedMonth, year: selectedYear },
                success: function (res) {
                    var data = typeof res === 'string' ? JSON.parse(res) : res;
                    if (data && !data.error) {
                        activeWarehouseOrders = data.orders || [];
                        $('#vas211_st_tot').text(formatNumber(data.totalDocuments));
                        $('#vas211_st_dr').text(formatNumber(data.drafted));
                        $('#vas211_st_ip').text(formatNumber(data.inProcess));
                        $('#vas211_st_co').text(formatNumber(data.completed));
                        $('#vas211_st_cl').text(formatNumber(data.closed));
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
            var $body = $('#vas211_orders_body');
            var $foot = $('#vas211_orders_foot');
            var $helper = $('#vas211_orders_helper');
            var $pager = $('#vas211_orders_pager');
            var $ptxt = $('#vas211_ord_ptxt');
            var $prev = $('#vas211_ord_prev');
            var $next = $('#vas211_ord_next');

            if (!$body.length) { return; }
            $body.empty();

            var total = activeWarehouseOrders.length;
            var totalPages = Math.max(1, Math.ceil(total / ORDERS_PAGE_SIZE));

            if (ordersPage > totalPages) { ordersPage = totalPages; }
            if (ordersPage < 1) { ordersPage = 1; }

            if (total === 0) {
                $body.html('<div class="vas-211-empty">' + escapeHtml(lbl('VAS_211_NoOrdersFound', 'No purchase orders found for this period')) + '</div>');
                $helper.text(lbl('VAS_211_Showing', 'Showing') + ' 0 ' + lbl('VAS_211_Of', 'of') + ' 0');
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
                    '<div class="vas-211-mrow vas-211-orders-grid">' +
                        '<span class="vas-211-cell vas-211-center">' +
                            '<button type="button" class="vas-211-iconbtn" data-orderid="' + o.orderId + '" title="' + escapeHtml(lbl('VAS_211_ViewLinesOf', 'View lines of') + ' ' + o.documentNo) + '">' + ICON_LINES + '</button>' +
                        '</span>' +
                        '<span class="vas-211-cell">' +
                            '<button type="button" class="vas-211-lnk" data-orderid="' + o.orderId + '" title="' + escapeHtml(lbl('VAS_211_OpenRecord', 'Open record') + ' ' + o.documentNo) + '">' + escapeHtml(o.documentNo) + '</button>' +
                        '</span>' +
                        '<span class="vas-211-cell vas-211-c-std" title="' + escapeHtml(formatDate(o.dateOrdered)) + '">' + escapeHtml(formatDate(o.dateOrdered)) + '</span>' +
                        '<span class="vas-211-cell" title="' + escapeHtml(o.vendorName) + '">' + escapeHtml(o.vendorName) + '</span>' +
                        '<span class="vas-211-cell" title="' + escapeHtml(o.warehouseName) + '">' + escapeHtml(o.warehouseName) + '</span>' +
                        '<span class="vas-211-cell vas-211-c-std" title="' + escapeHtml(o.salesRepName) + '">' + escapeHtml(o.salesRepName) + '</span>' +
                        '<span class="vas-211-cell vas-211-right vas-211-c-emph" title="' + escapeHtml(formatAmount(o.orderTotal, o.currencySymbol)) + '">' + escapeHtml(formatAmount(o.orderTotal, o.currencySymbol)) + '</span>' +
                        '<span class="vas-211-cell" title="' + escapeHtml(deliveryStatusDisplay) + '"><span class="vas-211-chip vas-211-' + o.deliveryChip + '">' + escapeHtml(deliveryStatusDisplay) + '</span></span>' +
                        '<span class="vas-211-cell" title="' + escapeHtml(docStatusDisplay) + '"><span class="vas-211-chip vas-211-' + o.docStatusChip + '">' + escapeHtml(docStatusDisplay) + '</span></span>' +
                    '</div>'
                );

                (function (poItem) {
                    $row.find('.vas-211-iconbtn').on('click', function (e) {
                        e.stopPropagation();
                        openOrderLinesModal(poItem);
                    });
                    $row.find('.vas-211-lnk').on('click', function (e) {
                        e.stopPropagation();
                        zoomToPurchaseOrder(poItem.orderId);
                    });
                })(o);

                $body.append($row);
            }

            var helperText = lbl('VAS_211_Showing', 'Showing') + ' ' + (start + 1) + '–' + end + ' ' + lbl('VAS_211_Of', 'of') + ' ' + total + ' · ' + lbl('VAS_211_SelectPOToOpen', 'select a PO number to open the record');
            $helper.text(helperText);

            if (totalPages > 1) {
                $pager.show();
                $ptxt.text(ordersPage + ' ' + lbl('VAS_211_Of', 'of') + ' ' + totalPages);
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
            var title = lbl('VAS_211_POLines', 'Lines') + ' · ' + poItem.documentNo;
            var docStatusDisp = getDocStatusText(poItem.docStatusCode || poItem.docStatusName);
            var delivStatusDisp = getDeliveryStatusText(poItem.deliveryStatus);
            var subtitle = (poItem.vendorName || '') + ' · ' + formatDate(poItem.dateOrdered) + ' · ' + delivStatusDisp;

            var bodyHtml =
                '<div class="vas-211-polink">' +
                    escapeHtml(lbl('VAS_211_PurchaseOrders', 'Purchase order')) + ' ' +
                    '<button type="button" class="vas-211-lnk" id="vas211_hdr_link" data-orderid="' + orderId + '">' + escapeHtml(poItem.documentNo) + '</button> · ' +
                    escapeHtml(formatDate(poItem.dateOrdered)) + ' · ' +
                    escapeHtml(docStatusDisp) +
                '</div>' +
                '<div class="vas-211-mstats" id="vas211_lines_stats">' +
                    '<div class="vas-211-mstat"><div class="vas-211-l">' + escapeHtml(lbl('VAS_211_POLines', 'Lines')) + '</div><div class="vas-211-v" id="vas211_ln_cnt">' + formatNumber(poItem.lineCount) + '</div></div>' +
                    '<div class="vas-211-mstat"><div class="vas-211-l">' + escapeHtml(lbl('VAS_211_POValue', 'PO value')) + '</div><div class="vas-211-v" id="vas211_ln_val">' + formatAmount(poItem.orderTotal, poItem.currencySymbol) + '</div></div>' +
                    '<div class="vas-211-mstat"><div class="vas-211-l">' + escapeHtml(lbl('VAS_211_Ordered', 'Qty ordered')) + '</div><div class="vas-211-v" id="vas211_ln_ord">' + formatNumber(poItem.qtyOrdered) + '</div></div>' +
                    '<div class="vas-211-mstat"><div class="vas-211-l">' + escapeHtml(lbl('VAS_211_Pending', 'Qty pending')) + '</div><div class="vas-211-v" id="vas211_ln_pnd">' + formatNumber(poItem.qtyPending) + '</div></div>' +
                '</div>' +
                '<div class="vas-211-msec">' + escapeHtml(lbl('VAS_211_POLines', 'Purchase order lines')) + '</div>' +
                '<div class="vas-211-mtwrap" id="vas211_lines_wrap">' +
                    '<div class="vas-211-mtbl">' +
                        '<div class="vas-211-mrow vas-211-mhead vas-211-lines-grid">' +
                            '<span class="vas-211-cell vas-211-right" title="#">#</span>' +
                            '<span class="vas-211-cell vas-211-c-prim" title="' + escapeHtml(lbl('VAS_211_Product', 'Product')) + '">' + escapeHtml(lbl('VAS_211_Product', 'Product')) + '</span>' +
                            '<span class="vas-211-cell" title="' + escapeHtml(lbl('VAS_211_Attribute', 'Attribute')) + '">' + escapeHtml(lbl('VAS_211_Attribute', 'Attribute')) + '</span>' +
                            '<span class="vas-211-cell" title="' + escapeHtml(lbl('VAS_211_UoM', 'UoM')) + '">' + escapeHtml(lbl('VAS_211_UoM', 'UoM')) + '</span>' +
                            '<span class="vas-211-cell vas-211-right" title="' + escapeHtml(lbl('VAS_211_Ordered', 'Ordered')) + '">' + escapeHtml(lbl('VAS_211_Ordered', 'Ordered')) + '</span>' +
                            '<span class="vas-211-cell vas-211-right" title="' + escapeHtml(lbl('VAS_211_Received', 'Received')) + '">' + escapeHtml(lbl('VAS_211_Received', 'Received')) + '</span>' +
                            '<span class="vas-211-cell vas-211-right vas-211-c-prim" title="' + escapeHtml(lbl('VAS_211_Pending', 'Pending')) + '">' + escapeHtml(lbl('VAS_211_Pending', 'Pending')) + '</span>' +
                            '<span class="vas-211-cell vas-211-right" title="' + escapeHtml(lbl('VAS_211_Rate', 'Rate')) + '">' + escapeHtml(lbl('VAS_211_Rate', 'Rate')) + '</span>' +
                            '<span class="vas-211-cell vas-211-right vas-211-c-emph" title="' + escapeHtml(lbl('VAS_211_Amount', 'Amount')) + '">' + escapeHtml(lbl('VAS_211_Amount', 'Amount')) + '</span>' +
                            '<span class="vas-211-cell" title="' + escapeHtml(lbl('VAS_211_LineStatus', 'Line status')) + '">' + escapeHtml(lbl('VAS_211_LineStatus', 'Line status')) + '</span>' +
                        '</div>' +
                        '<div class="vas-211-mbody" id="vas211_lines_body">' +
                            '<div class="vas-211-loading">' + escapeHtml(lbl('VAS_211_LoadingLines', 'Loading lines...')) + '</div>' +
                        '</div>' +
                    '</div>' +
                    '<div class="vas-211-mtfoot" id="vas211_lines_foot">' +
                        '<span class="vas-211-helper" id="vas211_lines_helper"></span>' +
                        '<span class="vas-211-pager" id="vas211_lines_pager" style="display:none;">' +
                            '<button type="button" class="vas-211-pbtn" id="vas211_ln_prev" aria-label="' + escapeHtml(lbl('VAS_211_Previous', 'Previous')) + '">' + ICON_CHEV_LEFT + '</button>' +
                            '<span class="vas-211-ptxt" id="vas211_ln_ptxt"></span>' +
                            '<button type="button" class="vas-211-pbtn" id="vas211_ln_next" aria-label="' + escapeHtml(lbl('VAS_211_Next', 'Next')) + '">' + ICON_CHEV_RIGHT + '</button>' +
                        '</span>' +
                    '</div>' +
                '</div>';

            var footHtml =
                '<span class="vas-211-foot-note" id="vas211_lines_footnote">' + escapeHtml(poItem.documentNo + ' · ' + poItem.vendorName) + '</span>' +
                '<span>' +
                    '<button type="button" class="vas-211-btn vas-211-btn-back">' + escapeHtml(lbl('VAS_211_Back', 'Back')) + '</button> ' +
                    '<button type="button" class="vas-211-btn vas-211-btn-close">' + escapeHtml(lbl('VAS_211_Close', 'Close')) + '</button>' +
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
                    $('#vas211_hdr_link').on('click', function () {
                        zoomToPurchaseOrder(orderId);
                    });
                    fetchOrderLines(orderId, poItem);
                }
            });
        }

        function fetchOrderLines(orderId, fallbackPo) {
            $.ajax({
                url: VIS.Application.contextUrl + 'VAS_211_WarehouseDocumentStatusWidget/GetOrderLines',
                type: 'GET',
                cache: false,
                data: { orderId: orderId },
                success: function (res) {
                    var data = typeof res === 'string' ? JSON.parse(res) : res;
                    if (data && !data.error) {
                        activeOrderHeader = data.header || fallbackPo;
                        activeOrderLines = data.lines || [];
                        $('#vas211_ln_cnt').text(formatNumber(data.totalLines));
                        if (activeOrderHeader) {
                            $('#vas211_ln_val').text(formatAmount(activeOrderHeader.grandTotal, activeOrderHeader.currencySymbol));
                        }
                        $('#vas211_ln_ord').text(formatNumber(data.totalQtyOrdered));
                        $('#vas211_ln_pnd').text(formatNumber(data.totalQtyPending));
                        var delStatusLabel = getDeliveryStatusText(data.deliveryStatus);
                        $('#vas211_lines_footnote').text(activeOrderLines.length + ' ' + lbl('VAS_211_POLines', 'lines') + ' · ' + formatNumber(data.totalQtyOrdered) + ' ' + lbl('VAS_211_QtyOrdered', 'qty ordered') + ' · ' + delStatusLabel);
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
            var $body = $('#vas211_lines_body');
            var $helper = $('#vas211_lines_helper');
            var $pager = $('#vas211_lines_pager');
            var $ptxt = $('#vas211_ln_ptxt');
            var $prev = $('#vas211_ln_prev');
            var $next = $('#vas211_ln_next');

            if (!$body.length) { return; }
            $body.empty();

            var total = activeOrderLines.length;
            var totalPages = Math.max(1, Math.ceil(total / LINES_PAGE_SIZE));

            if (linesPage > totalPages) { linesPage = totalPages; }
            if (linesPage < 1) { linesPage = 1; }

            if (total === 0) {
                $body.html('<div class="vas-211-empty">' + escapeHtml(lbl('VAS_211_NoLinesFound', 'No order lines found')) + '</div>');
                $helper.text(lbl('VAS_211_Showing', 'Showing') + ' 0 ' + lbl('VAS_211_Of', 'of') + ' 0');
                $pager.hide();
                return;
            }

            var start = (linesPage - 1) * LINES_PAGE_SIZE;
            var end = Math.min(start + LINES_PAGE_SIZE, total);
            var pageSlice = activeOrderLines.slice(start, end);
            var curSymbol = activeOrderHeader ? activeOrderHeader.currencySymbol : '';

            for (var i = 0; i < pageSlice.length; i++) {
                var l = pageSlice[i];
                /* Charge lines and non-Item products (service / resource / expense) are never
                   received, so Received, Pending and Line status show a dash instead of a
                   figure or a status chip. Attribute stays blank when the line has none. */
                var isNonStock = !!l.isNonStock;
                var attrDisp = l.attributeDesc ? String(l.attributeDesc) : '';
                var lineStatusDisplay = isNonStock ? DASH : getLineStatusText(l.lineStatus);
                var receivedDisp = isNonStock ? DASH : formatNumber(l.qtyDelivered);
                var pendingDisp = isNonStock ? DASH : formatNumber(l.qtyPending);
                var statusCell = isNonStock
                    ? '<span class="vas-211-cell vas-211-c-std" title="' + DASH + '">' + DASH + '</span>'
                    : '<span class="vas-211-cell" title="' + escapeHtml(lineStatusDisplay) + '"><span class="vas-211-chip vas-211-' + l.lineChip + '">' + escapeHtml(lineStatusDisplay) + '</span></span>';
                var $row = $(
                    '<div class="vas-211-mrow vas-211-lines-grid">' +
                        '<span class="vas-211-cell vas-211-right vas-211-c-std" title="' + l.lineIndex + '">' + l.lineIndex + '</span>' +
                        '<span class="vas-211-cell vas-211-c-prim" title="' + escapeHtml(l.productName) + '">' + escapeHtml(l.productName) + '</span>' +
                        '<span class="vas-211-cell vas-211-c-std" title="' + escapeHtml(attrDisp) + '">' + escapeHtml(attrDisp) + '</span>' +
                        '<span class="vas-211-cell" title="' + escapeHtml(l.uomName) + '">' + escapeHtml(l.uomName) + '</span>' +
                        '<span class="vas-211-cell vas-211-right" title="' + formatNumber(l.qtyOrdered) + '">' + formatNumber(l.qtyOrdered) + '</span>' +
                        '<span class="vas-211-cell vas-211-right" title="' + receivedDisp + '">' + receivedDisp + '</span>' +
                        '<span class="vas-211-cell vas-211-right vas-211-c-prim" title="' + pendingDisp + '">' + pendingDisp + '</span>' +
                        '<span class="vas-211-cell vas-211-right" title="' + formatAmount(l.priceActual, curSymbol) + '">' + formatAmount(l.priceActual, curSymbol) + '</span>' +
                        '<span class="vas-211-cell vas-211-right vas-211-c-emph" title="' + formatAmount(l.lineNetAmt, curSymbol) + '">' + formatAmount(l.lineNetAmt, curSymbol) + '</span>' +
                        statusCell +
                    '</div>'
                );
                $body.append($row);
            }

            var helperText = lbl('VAS_211_Showing', 'Showing') + ' ' + (start + 1) + '–' + end + ' ' + lbl('VAS_211_Of', 'of') + ' ' + total + ' · ' + lbl('VAS_211_LinesOf', 'lines of') + ' ' + (activeOrderHeader ? activeOrderHeader.documentNo : '');
            $helper.text(helperText);

            if (totalPages > 1) {
                $pager.show();
                $ptxt.text(linesPage + ' ' + lbl('VAS_211_Of', 'of') + ' ' + totalPages);
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
            $(document).off('keydown.vas211');
            if ($modalMask) {
                $modalMask.remove();
                $modalMask = null;
            }
            $root.remove();
            $wrapper.remove();
        };
    };

    VAS.VAS_211_WarehouseDocumentStatusWidget.prototype.init = function (windowNo, frame) {
        this.frame = frame;
        this.AD_UserHomeWidgetID = frame.widgetInfo ? frame.widgetInfo.AD_UserHomeWidgetID : 0;
        this.windowNo = windowNo;
        this.Initalize();
        this.frame.getContentGrid().append(this.getRoot());
        ensureDashInlineSizeVar(this.getRoot());
    };

    VAS.VAS_211_WarehouseDocumentStatusWidget.prototype.widgetSizeChange = function (height, width) { };

    VAS.VAS_211_WarehouseDocumentStatusWidget.prototype.refreshWidget = function () {
        this.refreshWidget();
    };

    VAS.VAS_211_WarehouseDocumentStatusWidget.prototype.addChangeListener = function (listener) {
        this.listener = listener;
    };

    VAS.VAS_211_WarehouseDocumentStatusWidget.prototype.widgetFirevalueChanged = function (value) {
        if (this.listener) {
            this.listener.widgetFirevalueChanged(value);
        }
    };

    VAS.VAS_211_WarehouseDocumentStatusWidget.prototype.dispose = function () {
        this.disposeComponent();
        if (this.frame) {
            this.frame.dispose();
        }
        this.frame = null;
    };

    // Alias registration for dynamic widget loading
    VAS.VAS_WarehouseDocumentStatusWidget = VAS.VAS_211_WarehouseDocumentStatusWidget;

})(VAS, jQuery);
