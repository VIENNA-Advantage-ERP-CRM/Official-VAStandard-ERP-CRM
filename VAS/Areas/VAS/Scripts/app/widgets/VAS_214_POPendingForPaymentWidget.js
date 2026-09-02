/**
 * VAS_214_POPendingForPaymentWidget
 * Purchase Order Dashboard — Widget 12: PO Pending for Payment
 * Widget size: 6 columns x 2 rows (6x2 Wide Grid Queue Widget).
 * Operational Purchase Order queue of received Purchase Orders
 * whose payment has not yet been fully released (balance_due > 0).
 *
 * Summary Message Table
 *  # | Current Text                                            | Message Key
 * ---+---------------------------------------------------------+-----------------------------------
 *  1 | PO Pending for Payment                                  | VAS_POPendingForPayment
 *  2 | Received against PO, payment not yet released           | VAS_POPendingForPaymentSub
 *  3 | due                                                     | VAS_Due
 *  4 | PO No                                                   | VAS_PONumber
 *  5 | PO date                                                 | VAS_PODate
 *  6 | Vendor                                                  | VAS_Vendor
 *  7 | Warehouse                                               | VAS_Warehouse
 *  8 | Received on                                             | VAS_ReceivedOn
 *  9 | Payment due                                             | VAS_PaymentDue
 * 10 | Amount                                                  | VAS_Amount
 * 11 | Balance                                                 | VAS_Balance
 * 12 | Paid                                                    | VAS_Paid
 * 13 | Total payable                                           | VAS_TotalPayable
 * 14 | Overdue                                                 | VAS_Overdue
 * 15 | oldest due first                                        | VAS_OldestDueFirst
 * 16 | select a PO number to open the record                   | VAS_SelectPOToOpen
 * 17 | Showing                                                 | VAS_Showing
 * 18 | of                                                      | VAS_Of
 * 19 | No POs pending for payment found                        | VAS_NoPOsPendingPayment
 * 20 | Loading...                                              | VAS_Loading
 * 21 | Couldn't load data                                      | VAS_CouldntLoad
 * 22 | Retry                                                   | VAS_Retry
 * 23 | Back                                                    | VAS_Back
 * 24 | Close                                                   | VAS_Close
 * 25 | Open Record                                             | VAS_OpenRecord
 * 26 | Lines                                                   | VAS_Lines
 * 27 | lines of                                                | VAS_LinesOf
 * 28 | Product                                                 | VAS_Product
 * 29 | Attribute                                               | VAS_Attribute
 * 30 | UoM                                                     | VAS_UOM
 * 31 | Ordered                                                 | VAS_Ordered
 * 32 | Received                                                | VAS_Received
 * 33 | Pending                                                 | VAS_Pending
 * 34 | Rate                                                    | VAS_Rate
 * 35 | Line status                                             | VAS_LineStatus
 * 36 | Purchase order lines                                    | VAS_PurchaseOrderLines
 * 37 | qty ordered                                             | VAS_QtyOrdered
 * 38 | qty pending                                             | VAS_QtyPending
 * 39 | Created by                                              | VAS_CreatedBy
 * 40 | Document status                                         | VAS_DocumentStatus
 * 41 | Delivery status                                         | VAS_DeliveryStatus
 * 42 | Completed                                               | VAS_Completed
 * 43 | Partial                                                 | VAS_Partial
 * 44 | Partial received                                        | VAS_PartialReceived
 * 45 | Previous                                                | VAS_Previous
 * 46 | Next                                                    | VAS_Next
 * 47 | Fully delivered                                         | VAS_FullyDelivered
 * 48 | Not applicable                                          | VAS_NotApplicable
 * 49 | Closed                                                  | VAS_Closed
 * 50 | In process                                              | VAS_InProcess
 * 51 | Drafted                                                 | VAS_Drafted
 * 52 | Voided                                                  | VAS_Voided
 * 53 | No records found                                        | VAS_NoRecords
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
        write();
    }

    function lbl(key, fallback) {
        if (window.VIS && VIS.Msg && VIS.Msg.getMsg) {
            var msg = VIS.Msg.getMsg(key);
            // VIS.Msg.getMsg returns "[KEY]" when the AD_Message row is missing;
            // that must fall through to the English fallback, not render as-is.
            if (msg && msg !== key && msg !== '[' + key + ']' && msg.charAt(0) !== '['
                && msg.indexOf('**') === -1) {
                return msg;
            }
        }
        return fallback;
    }

    function getDocStatusDisplay(status) {
        if (!status) { return lbl('VAS_Completed', 'Completed'); }
        if (status === 'CO' || status === 'Completed') { return lbl('VAS_Completed', 'Completed'); }
        if (status === 'CL' || status === 'Closed') { return lbl('VAS_Closed', 'Closed'); }
        if (status === 'IP' || status === 'In process' || status === 'In Process') { return lbl('VAS_InProcess', 'In process'); }
        if (status === 'DR' || status === 'Drafted') { return lbl('VAS_Drafted', 'Drafted'); }
        if (status === 'VO' || status === 'Voided') { return lbl('VAS_Voided', 'Voided'); }
        return status;
    }

    function getDeliveryStatusDisplay(status, totalOrdered, totalDelivered) {
        if (status === 'CL' || status === 'Closed' || status === 'VO' || status === 'Voided') {
            return lbl('VAS_NotApplicable', 'Not applicable');
        }
        if (totalOrdered > 0 && totalDelivered >= totalOrdered) {
            return lbl('VAS_FullyDelivered', 'Fully delivered');
        }
        if (totalDelivered > 0 && totalDelivered < totalOrdered) {
            return lbl('VAS_Partial', 'Partial');
        }
        if (status === 'CO' || status === 'Completed') {
            return lbl('VAS_FullyDelivered', 'Fully delivered');
        }
        return lbl('VAS_Pending', 'Pending');
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
        if (typeof data === 'string' && data.length) {
            try { data = JSON.parse(data); } catch (e) { }
        }
        if (typeof data === 'string' && data.length) {
            try { data = JSON.parse(data); } catch (e) { }
        }
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

        // Compact formatting for high values
        if (curIso === 'INR' || sym === '₹') {
            if (Math.abs(val) >= 10000000) {
                return sym + ' ' + (val / 10000000).toFixed(2) + ' Cr';
            }
            if (Math.abs(val) >= 100000) {
                return sym + ' ' + (val / 100000).toFixed(2) + ' L';
            }
        } else {
            if (Math.abs(val) >= 1000000) {
                return sym + ' ' + (val / 1000000).toFixed(2) + ' M';
            }
            if (Math.abs(val) >= 10000) {
                return sym + ' ' + (val / 1000).toFixed(2) + ' k';
            }
        }

        var formatted = val.toLocaleString(window.navigator.language, {
            minimumFractionDigits: p,
            maximumFractionDigits: p
        });

        return sym ? (sym + ' ' + formatted) : formatted;
    }

    var ICON_BACK = '<svg width="20" height="20" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2" stroke-linecap="round" stroke-linejoin="round"><path d="M19 12H5M12 19l-7-7 7-7"/></svg>';
    var ICON_CLOSE = '<svg width="20" height="20" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2" stroke-linecap="round"><path d="M18 6 6 18M6 6l12 12"/></svg>';
    var ICON_PREV = '<svg viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2.4" stroke-linecap="round"><polyline points="15 18 9 12 15 6"/></svg>';
    var ICON_NEXT = '<svg viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2.4" stroke-linecap="round"><polyline points="9 18 15 12 9 6"/></svg>';
    var ICON_LINES = '<svg viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2" stroke-linecap="round"><path d="M8 6h13M8 12h13M8 18h13M3 6h.01M3 12h.01M3 18h.01"/></svg>';

    var GRID_COLUMNS = 'minmax(0, 1.1fr) minmax(0, 0.95fr) minmax(0, 1.7fr) minmax(0, 1.1fr) minmax(0, 0.95fr) minmax(0, 1fr) minmax(0, 1fr)';

    VAS.VAS_214_POPendingForPaymentWidget = function () {

        this.frame = null;
        this.windowNo = 0;
        this.AD_UserHomeWidgetID = 0;

        var $self = this;
        var $root = $('<div class="vas-214-root"></div>');
        var $card = null;
        var $payHead = null;
        var $payBody = null;
        var $payPill = null;
        var $payHelper = null;
        var $payPage = null;
        var $prevBtn = null;
        var $nextBtn = null;
        var $busy = null;

        var queueData = {
            totalDue: 0,
            totalCount: 0,
            baseCurrency: { CurrencyID: 0, CurSymbol: '', ISO_Code: '', StdPrecision: 2 },
            records: []
        };

        var currentPage = 0;
        var pageSize = 5;
        var totalPages = 1;

        // Modal Stack State
        var modalStack = [];
        var currentModalCfg = null;
        var $modalHost = null;
        var MT = {};
        var MT_SEQ = 0;
        // Rows per page in the modal table. The stylesheet sizes the table body to
        // exactly this many rows (--vas-214-rows), so the two must stay in step.
        var MAX_MODAL_ROWS = 6;

        function showBusy(show) {
            if (!$busy) { return; }
            $busy.toggleClass('vas-214-hidden', !show);
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
            var title = lbl('VAS_POPendingForPayment', 'PO Pending for Payment');
            var subtitle = lbl('VAS_POPendingForPaymentSub', 'Received against PO, payment not yet released');

            $card = $('<div class="vas-214-card"></div>');

            var $head = $(
                '<div class="vas-214-head">' +
                    '<div class="vas-214-head-txt">' +
                        '<p class="vas-214-title">' + esc(title) + '</p>' +
                        '<p class="vas-214-sub">' + esc(subtitle) + '</p>' +
                    '</div>' +
                    '<span class="vas-214-hpill vas-214-hpill-danger">—</span>' +
                '</div>'
            );
            $payPill = $head.find('.vas-214-hpill');
            $card.append($head);

            var $tbl = $('<div class="vas-214-tbl"></div>');
            $payHead = $('<div class="vas-214-thead" style="grid-template-columns:' + GRID_COLUMNS + ';"></div>');
            $payBody = $('<div class="vas-214-tbody"></div>');

            renderTableHeaders();

            $tbl.append($payHead);
            $tbl.append($payBody);
            $card.append($tbl);

            var $foot = $(
                '<div class="vas-214-wfoot">' +
                    '<span class="vas-214-helper"></span>' +
                    '<div class="vas-214-pager">' +
                        '<button type="button" class="vas-214-pbtn vas-214-prev-btn" aria-label="' + esc(lbl('VAS_Previous', 'Previous')) + '" disabled>' +
                            ICON_PREV +
                        '</button>' +
                        '<span class="vas-214-ptxt">1 of 1</span>' +
                        '<button type="button" class="vas-214-pbtn vas-214-next-btn" aria-label="' + esc(lbl('VAS_Next', 'Next')) + '" disabled>' +
                            ICON_NEXT +
                        '</button>' +
                    '</div>' +
                '</div>'
            );

            $payHelper = $foot.find('.vas-214-helper');
            $payPage = $foot.find('.vas-214-ptxt');
            $prevBtn = $foot.find('.vas-214-prev-btn');
            $nextBtn = $foot.find('.vas-214-next-btn');

            $prevBtn.on('click', function (e) {
                e.stopPropagation();
                if (currentPage > 0) {
                    currentPage--;
                    renderWidgetPage();
                }
            });

            $nextBtn.on('click', function (e) {
                e.stopPropagation();
                if (currentPage < totalPages - 1) {
                    currentPage++;
                    renderWidgetPage();
                }
            });

            $card.append($foot);
            $root.append($card);

            // Delegate row & link click events
            $payBody.on('click', '.vas-214-trow', function (e) {
                var poId = parseInt($(this).attr('data-po-id'), 10);
                var poNo = $(this).attr('data-po-no');
                if (poId) {
                    openRecordModal(poId, poNo);
                }
            });

            $busy = $(
                '<div class="vas-214-busy vas-214-hidden">' +
                    '<div class="vis-busyindicatorinnerwrap"><i class="vis_widgetloader"></i></div>' +
                '</div>'
            );
            $root.append($busy);
        }

        function renderTableHeaders() {
            var colHeaders = [
                { label: lbl('VAS_PONumber', 'PO No'), align: 'left' },
                { label: lbl('VAS_PODate', 'PO date'), align: 'left' },
                { label: lbl('VAS_Vendor', 'Vendor'), align: 'left' },
                { label: lbl('VAS_Warehouse', 'Warehouse'), align: 'left' },
                { label: lbl('VAS_ReceivedOn', 'Received on'), align: 'left' },
                { label: lbl('VAS_PaymentDue', 'Payment due'), align: 'left' },
                { label: lbl('VAS_Balance', 'Balance'), align: 'right' }
            ];

            var h = '';
            for (var i = 0; i < colHeaders.length; i++) {
                var col = colHeaders[i];
                var rightClass = col.align === 'right' ? ' vas-214-right' : '';
                h += '<span class="vas-214-cell' + rightClass + '" title="' + esc(col.label) + '">' + esc(col.label) + '</span>';
            }
            $payHead.html(h);
        }

        function loadQueueData() {
            showBusy(true);

            $.ajax({
                url: VIS.Application.contextUrl + 'VAS_214_POPendingForPaymentWidget/GetPOPendingForPayment',
                type: 'GET',
                cache: false,
                success: function (res) {
                    showBusy(false);
                    var data = parseResponse(res);

                    if (data && data.success) {
                        queueData = data;
                        currentPage = 0;
                        updateWidgetSummary();
                        renderWidgetPage();
                    } else {
                        renderErrorState(data.error || lbl('VAS_CouldntLoad', "Couldn't load data"));
                    }
                },
                error: function (xhr, status, err) {
                    showBusy(false);
                    renderErrorState(lbl('VAS_CouldntLoad', "Couldn't load data"));
                }
            });
        }

        function updateWidgetSummary() {
            var cur = queueData.baseCurrency || {};
            var totalDueFormatted = formatMoney(queueData.totalDue, cur.CurSymbol, cur.ISO_Code, cur.StdPrecision);
            $payPill.text(totalDueFormatted + ' ' + lbl('VAS_Due', 'due'));
        }

        function renderWidgetPage() {
            var records = queueData.records || [];
            var total = records.length;
            totalPages = Math.max(1, Math.ceil(total / pageSize));

            if (currentPage >= totalPages) {
                currentPage = totalPages - 1;
            }
            if (currentPage < 0) {
                currentPage = 0;
            }

            if (total === 0) {
                $payBody.html('<div class="vas-214-empty-state">' + esc(lbl('VAS_NoPOsPendingPayment', 'No POs pending for payment found')) + '</div>');
                $payHelper.text(lbl('VAS_NoPOsPendingPayment', 'No POs pending for payment found'));
                $payPage.text('1 of 1');
                $prevBtn.prop('disabled', true);
                $nextBtn.prop('disabled', true);
                return;
            }

            var startIdx = currentPage * pageSize;
            var endIdx = Math.min(startIdx + pageSize, total);
            var pageRecords = records.slice(startIdx, endIdx);

            var rowsHtml = '';
            for (var i = 0; i < pageRecords.length; i++) {
                var p = pageRecords[i];
                var poNo = p.PurchaseOrderNo || ('PO-' + p.PurchaseOrderID);
                var poDateDisplay = p.OrderDateShort || p.OrderDateDisplay || p.OrderDate;
                var vendor = p.VendorName || '—';
                var wh = p.WarehouseName || '—';
                var recdOn = p.ReceivedOnShort || p.ReceivedOnDisplay || p.ReceivedOn || '—';

                var dueCellHtml = '';
                if (p.IsOverdue) {
                    var overdueLabel = lbl('VAS_Overdue', 'Overdue') + ' ' + p.OverdueDays + 'd';
                    dueCellHtml = '<span class="vas-214-chip vas-214-chip-risk" title="' + esc(overdueLabel) + '">' + esc(overdueLabel) + '</span>';
                } else {
                    var dueText = p.PaymentDueShort || p.PaymentDueDisplay || p.PaymentDue || '—';
                    dueCellHtml = '<span class="vas-214-c-std" title="' + esc(dueText) + '">' + esc(dueText) + '</span>';
                }

                var balFormatted = formatMoney(p.BalanceDue, p.CurrencySymbol, p.CurrencyISO, p.StdPrecision);
                var paidFormatted = formatMoney(p.PaidAmount, p.CurrencySymbol, p.CurrencyISO, p.StdPrecision);
                var paidText = lbl('VAS_Paid', 'Paid') + ' ' + paidFormatted;

                rowsHtml +=
                    '<div class="vas-214-trow" data-po-id="' + p.PurchaseOrderID + '" data-po-no="' + esc(poNo) + '" style="grid-template-columns:' + GRID_COLUMNS + ';">' +
                        '<span class="vas-214-cell vas-214-c-link" title="' + esc(poNo) + '">' +
                            '<button type="button" class="vas-214-lnk" data-po-id="' + p.PurchaseOrderID + '" data-po-no="' + esc(poNo) + '" title="' + esc(poNo) + '">' +
                                esc(poNo) +
                            '</button>' +
                        '</span>' +
                        '<span class="vas-214-cell vas-214-c-std" title="' + esc(p.OrderDateDisplay || poDateDisplay) + '">' + esc(poDateDisplay) + '</span>' +
                        '<span class="vas-214-cell vas-214-c-std" title="' + esc(vendor) + '">' + esc(vendor) + '</span>' +
                        '<span class="vas-214-cell vas-214-c-dark" title="' + esc(wh) + '">' + esc(wh) + '</span>' +
                        '<span class="vas-214-cell vas-214-c-std" title="' + esc(p.ReceivedOnDisplay || recdOn) + '">' + esc(recdOn) + '</span>' +
                        '<span class="vas-214-cell" title="' + esc(p.PaymentDueDisplay || '') + '">' + dueCellHtml + '</span>' +
                        '<div class="vas-214-cell vas-214-right">' +
                            '<span class="vas-214-c-emph" title="' + esc(balFormatted) + '">' + esc(balFormatted) + '</span>' +
                            '<div class="vas-214-sub-txt" title="' + esc(paidText) + '">' + esc(paidText) + '</div>' +
                        '</div>' +
                    '</div>';
            }

            $payBody.html(rowsHtml);

            var showingFrom = startIdx + 1;
            var showingTo = endIdx;
            var helperString = lbl('VAS_Showing', 'Showing') + ' ' + showingFrom + '–' + showingTo + ' ' + lbl('VAS_Of', 'of') + ' ' + total + ' · ' + lbl('VAS_OldestDueFirst', 'oldest due first') + ' · ' + lbl('VAS_SelectPOToOpen', 'select a PO number to open the record');
            $payHelper.text(helperString);

            $payPage.text((currentPage + 1) + ' ' + lbl('VAS_Of', 'of') + ' ' + totalPages);
            $prevBtn.prop('disabled', currentPage === 0);
            $nextBtn.prop('disabled', currentPage >= totalPages - 1);
        }

        function renderErrorState(msg) {
            $payBody.html(
                '<div class="vas-214-error-state">' +
                    '<span>' + esc(msg) + '</span>' +
                    '<button type="button" class="vas-214-retry-btn">' + esc(lbl('VAS_Retry', 'Retry')) + '</button>' +
                '</div>'
            );
            $payBody.find('.vas-214-retry-btn').on('click', function () {
                loadQueueData();
            });
        }

        /* ============================================================
           PO RECORD NAVIGATION (ZOOM / WINDOW LAUNCH)
           ============================================================ */
        function openPurchaseOrderRecord(orderId) {
            if (!orderId) { return; }
            // Navigating away must dismiss the popup: the record opens behind it
            // otherwise, leaving the dialog stranded over the window it just opened.
            closeModal();

            // 1. Tab Panel / Widget Event firing
            try {
                if ($self.listener) {
                    var windowParam = {
                        "action": "openRecord",
                        "AD_Table_ID": "259",
                        "Record_ID": String(orderId),
                        "WindowName": "VAS_PurchaseOrder",
                        "AD_Window_ID": "181",
                        "AD_Tab_ID": "1002398",
                        "TabIndex": "0"
                    };
                    $self.widgetFirevalueChanged(windowParam);
                }
            } catch (e) { }

            // 2. Standard VIS Zoom Manager
            try {
                if (window.VIS && VIS.ZoomManager && VIS.ZoomManager.zoom) {
                    VIS.ZoomManager.zoom(259, orderId);
                    return;
                }
            } catch (e) { }

            // 3. Fallback View Manager
            try {
                if (window.VIS && VIS.viewManager && VIS.viewManager.startWindow) {
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
                '<div class="vas-214-mask" role="dialog" aria-modal="true">' +
                    '<div class="vas-214-modal">' +
                        '<div class="vas-214-modal-header">' +
                            '<div class="vas-214-htxt-wrap">' +
                                '<button type="button" class="vas-214-xbtn vas-214-back-btn" aria-label="' + esc(lbl('VAS_Back', 'Back')) + '" style="display:none;">' +
                                    ICON_BACK +
                                '</button>' +
                                '<div class="vas-214-htxt">' +
                                    '<h2 class="vas-214-mtitle"></h2>' +
                                    '<div class="vas-214-msub"></div>' +
                                '</div>' +
                            '</div>' +
                            '<div class="vas-214-hact">' +
                                '<button type="button" class="vas-214-xbtn vas-214-close-btn" aria-label="' + esc(lbl('VAS_Close', 'Close')) + '">' +
                                    ICON_CLOSE +
                                '</button>' +
                            '</div>' +
                        '</div>' +
                        '<div class="vas-214-modal-body"></div>' +
                        '<div class="vas-214-modal-foot"></div>' +
                    '</div>' +
                '</div>';

            $modalHost = $(html);
            $('body').append($modalHost);

            $modalHost.find('.vas-214-close-btn').on('click', closeModal);
            $modalHost.find('.vas-214-back-btn').on('click', popModal);

            $modalHost.on('click', function (e) {
                if (e.target === this) { closeModal(); }
                if ($(e.target).closest('[data-vas-close]').length) { closeModal(); }
            });

            $(document).on('keydown.vas214', function (e) {
                if (e.key === 'Escape') { closeModal(); }
            });

            $(window).on('resize.vas214', function () {
                if ($modalHost && $modalHost.hasClass('vas-214-open')) {
                    fitAllTables();
                }
            });

            // Delegate table pagination & links in modal
            $modalHost.find('.vas-214-modal-body').on('click', function (e) {
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
                    return;
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

            var $backBtn = $modalHost.find('.vas-214-back-btn');
            if (modalStack.length > 0) {
                $backBtn.show();
            } else {
                $backBtn.hide();
            }

            var $modal = $modalHost.find('.vas-214-modal');
            $modal.removeClass('vas-214-modal-sm vas-214-modal-md');
            if (cfg.size === 'sm') { $modal.addClass('vas-214-modal-sm'); }
            if (cfg.size === 'md') { $modal.addClass('vas-214-modal-md'); }

            $modalHost.find('.vas-214-mtitle').text(cfg.title || '');
            $modalHost.find('.vas-214-msub').text(cfg.subtitle || '');
            $modalHost.find('.vas-214-modal-body').html(cfg.body || '');
            $modalHost.find('.vas-214-modal-foot').html(cfg.foot || '<span class="vas-214-foot-note"></span><button type="button" class="vas-214-btn" data-vas-close="1">' + esc(lbl('VAS_Close', 'Close')) + '</button>');

            $modalHost.addClass('vas-214-open');
            drawAllTables();

            requestAnimationFrame(function () {
                fitAllTables();
                requestAnimationFrame(fitAllTables);
            });

            if (cfg.after) { cfg.after(); }
        }

        function popModal() {
            var prevCfg = modalStack.pop();
            if (!prevCfg) {
                closeModal();
                return;
            }
            if (prevCfg.reopen) {
                prevCfg.reopen();
            } else {
                openModal(prevCfg, true);
            }
        }

        function closeModal() {
            if ($modalHost) {
                $modalHost.removeClass('vas-214-open');
            }
            modalStack = [];
            currentModalCfg = null;
        }

        /* ============================================================
           PAGED TABLE BUILDER (DYNAMIC CLIENT HEIGHT FIT)
           ============================================================ */
        function pagedTable(cols, rows, opts) {
            opts = opts || {};
            var id = 'vas214_mt_' + (++MT_SEQ);
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
            return '<div class="vas-214-mtwrap' + (opts.fixed ? ' vas-214-fixed' : '') + '" id="' + id + '"></div>';
        }

        function cellHTML(cell, col) {
            if (cell && typeof cell === 'object') {
                if (cell.link) {
                    return '<span class="vas-214-cell"><button type="button" class="vas-214-lnk" data-po-id="' + esc(cell.id) + '" data-po-no="' + esc(cell.link) + '" title="' + esc(cell.link) + '">' + esc(cell.link) + '</button></span>';
                }
                if (cell.icon) {
                    return '<span class="vas-214-cell vas-214-center"><button type="button" class="vas-214-iconbtn" data-lines-po-id="' + esc(cell.id) + '" data-lines-po-no="' + esc(cell.icon) + '" title="' + esc(lbl('VAS_Lines', 'Lines')) + '">' + ICON_LINES + '</button></span>';
                }
                if (cell.chip) {
                    return '<span class="vas-214-cell" title="' + esc(cell.text) + '"><span class="vas-214-chip ' + esc(cell.chip) + '">' + esc(cell.text) + '</span></span>';
                }
            }
            var alignClass = (col && col.align === 'right') ? ' vas-214-right' : '';
            var textClass = (col && col.cls) ? (' ' + col.cls) : ' vas-214-c-std';
            return '<span class="vas-214-cell' + alignClass + textClass + '" title="' + esc(cell) + '">' + esc(cell) + '</span>';
        }

        function drawTable(id) {
            var t = MT[id];
            var el = document.getElementById(id);
            if (!t || !el) { return; }

            var tpl = t.cols.map(function (c) { return 'minmax(0,' + (c.w || 1) + 'fr)'; }).join(' ');
            var totalRows = t.rows.length;
            var pages = Math.max(1, Math.ceil(totalRows / t.size));
            if (t.page > pages - 1) { t.page = pages - 1; }

            var s = t.page * t.size;
            var slice = t.rows.slice(s, s + t.size);

            var h = '<div class="vas-214-mtbl">' +
                        '<div class="vas-214-mrow vas-214-mhead" style="grid-template-columns:' + tpl + '">';
            for (var ci = 0; ci < t.cols.length; ci++) {
                var c = t.cols[ci];
                var cAlign = c.align === 'right' ? ' vas-214-right' : '';
                h += '<span class="vas-214-cell' + cAlign + '" title="' + esc(c.label || '') + '">' + esc(c.label || '') + '</span>';
            }
            h += '</div>' +
                 '<div class="vas-214-mbody">';

            if (slice.length === 0) {
                h += '<div class="vas-214-empty-state">' + esc(lbl('VAS_NoPOsPendingPayment', 'No records found')) + '</div>';
            } else {
                for (var ri = 0; ri < slice.length; ri++) {
                    var r = slice[ri];
                    h += '<div class="vas-214-mrow" style="grid-template-columns:' + tpl + '">';
                    for (var cj = 0; cj < t.cols.length; cj++) {
                        h += cellHTML(r[cj], t.cols[cj]);
                    }
                    h += '</div>';
                }
            }
            h += '</div></div>';

            var showingFrom = totalRows > 0 ? (s + 1) : 0;
            var showingTo = totalRows > 0 ? (s + slice.length) : 0;
            var showingText = lbl('VAS_Showing', 'Showing') + ' ' + showingFrom + '–' + showingTo + ' ' + lbl('VAS_Of', 'of') + ' ' + totalRows;
            if (t.label) { showingText += ' · ' + t.label; }

            h += '<div class="vas-214-mtfoot">' +
                    '<span class="vas-214-helper">' + esc(showingText) + '</span>';

            if (pages > 1) {
                h += '<span class="vas-214-pager">' +
                        '<button type="button" class="vas-214-pbtn" data-mt="' + id + '" data-dir="-1"' + (t.page === 0 ? ' disabled' : '') + ' aria-label="' + esc(lbl('VAS_Previous', 'Previous')) + '">' + ICON_PREV + '</button>' +
                        '<span class="vas-214-ptxt">' + (t.page + 1) + ' ' + lbl('VAS_Of', 'of') + ' ' + pages + '</span>' +
                        '<button type="button" class="vas-214-pbtn" data-mt="' + id + '" data-dir="1"' + (t.page >= pages - 1 ? ' disabled' : '') + ' aria-label="' + esc(lbl('VAS_Next', 'Next')) + '">' + ICON_NEXT + '</button>' +
                     '</span>';
            } else {
                h += '<span></span>';
            }
            h += '</div>';

            el.innerHTML = h;
        }

        function drawAllTables() {
            Object.keys(MT).forEach(function (id) {
                if (document.getElementById(id)) { drawTable(id); }
            });
        }

        /* Rows per page are fixed and the stylesheet sizes the table body to exactly
           that many rows, so there is nothing to measure or re-fit. The old body
           derived the count from a rendered row height, but the rows are sized by
           the stylesheet, so it was measuring its own output. */
        function fitTable(id) { return; }

        function fitAllTables() {
            Object.keys(MT).forEach(function (id) {
                if (document.getElementById(id)) { fitTable(id); }
            });
        }

        function mstatsHtml(items) {
            var h = '<div class="vas-214-mstats">';
            for (var i = 0; i < items.length; i++) {
                var it = items[i];
                h += '<div class="vas-214-mstat">' +
                        '<div class="vas-214-mstat-l">' + esc(it.l) + '</div>' +
                        '<div class="vas-214-mstat-v" title="' + esc(it.v) + '">' + esc(it.v) + '</div>' +
                     '</div>';
            }
            h += '</div>';
            return h;
        }

        /* ============================================================
           MODAL: PURCHASE ORDER RECORD & DETAILS
           ============================================================ */
        function openRecordModal(poId, poNo) {
            var po = (queueData.records || []).find(function (r) { return r.PurchaseOrderID === poId; });

            $.ajax({
                url: VIS.Application.contextUrl + 'VAS_214_POPendingForPaymentWidget/GetPODetail?C_Order_ID=' + poId,
                type: 'GET',
                cache: false,
                success: function (res) {
                    var data = parseResponse(res);
                    var hdr = data.header || {};

                    var vendor = hdr.VendorName || (po ? po.VendorName : '—');
                    var dateDisplay = hdr.OrderDateDisplay || (po ? po.OrderDateDisplay : '—');
                    var whName = hdr.WarehouseName || (po ? po.WarehouseName : '—');
                    var createdBy = (hdr.CreatedBy || '') + (hdr.CreatedOn ? (' · ' + hdr.CreatedOn) : '');

                    var totalPayableFmt = po ? formatMoney(po.TotalPayable, po.CurrencySymbol, po.CurrencyISO, po.StdPrecision) : '—';
                    var paidFmt = po ? formatMoney(po.PaidAmount, po.CurrencySymbol, po.CurrencyISO, po.StdPrecision) : '—';
                    var balFmt = po ? formatMoney(po.BalanceDue, po.CurrencySymbol, po.CurrencyISO, po.StdPrecision) : '—';
                    var dueDisplay = po ? (po.PaymentDueDisplay || po.PaymentDueShort || '—') : '—';

                    // Fetch PO line items
                    $.ajax({
                        url: VIS.Application.contextUrl + 'VAS_214_POPendingForPaymentWidget/GetPOLines?C_Order_ID=' + poId,
                        type: 'GET',
                        cache: false,
                        success: function (lineRes) {
                            var lineData = parseResponse(lineRes);
                            var lines = lineData.lines || [];

                            var totalOrderedQty = 0;
                            var totalDeliveredQty = 0;
                            var totalPendingQty = 0;
                            lines.forEach(function (l) {
                                totalOrderedQty += Number(l.OrderedQty || 0);
                                totalDeliveredQty += Number(l.DeliveredQty || 0);
                                totalPendingQty += Number(l.PendingQty || 0);
                            });

                            var rawDocStatus = hdr.DocStatus || (po ? po.DocStatus : 'CO');
                            var docStatusTxt = getDocStatusDisplay(rawDocStatus);
                            var delivStatusTxt = getDeliveryStatusDisplay(rawDocStatus, totalOrderedQty, totalDeliveredQty);

                            var headerStatsHtml = mstatsHtml([
                                { l: lbl('VAS_TotalPayable', 'Total payable'), v: totalPayableFmt },
                                { l: lbl('VAS_Paid', 'Paid'), v: paidFmt },
                                { l: lbl('VAS_Balance', 'Balance'), v: balFmt },
                                { l: lbl('VAS_PaymentDue', 'Payment due'), v: dueDisplay },
                                { l: lbl('VAS_Vendor', 'Vendor'), v: vendor },
                                { l: lbl('VAS_PODate', 'PO date'), v: dateDisplay },
                                { l: lbl('VAS_Warehouse', 'Warehouse'), v: whName },
                                { l: lbl('VAS_CreatedBy', 'Created by'), v: createdBy || '—' },
                                { l: lbl('VAS_DocumentStatus', 'Document status'), v: docStatusTxt },
                                { l: lbl('VAS_DeliveryStatus', 'Delivery status'), v: delivStatusTxt }
                            ]);

                            var lineCols = [
                                { label: '#', w: 0.3, align: 'right' },
                                { label: lbl('VAS_Product', 'Product'), w: 1.5, cls: 'vas-214-c-prim' },
                                { label: lbl('VAS_Attribute', 'Attribute'), w: 1.2 },
                                { label: lbl('VAS_UOM', 'UoM'), w: 0.5 },
                                { label: lbl('VAS_Ordered', 'Ordered'), w: 0.7, align: 'right' },
                                { label: lbl('VAS_Received', 'Received'), w: 0.7, align: 'right' },
                                { label: lbl('VAS_Pending', 'Pending'), w: 0.7, align: 'right', cls: 'vas-214-c-prim' },
                                { label: lbl('VAS_Rate', 'Rate'), w: 0.7, align: 'right' },
                                { label: lbl('VAS_Amount', 'Amount'), w: 0.9, align: 'right', cls: 'vas-214-c-emph' },
                                { label: lbl('VAS_LineStatus', 'Line status'), w: 1.0 }
                            ];

                            var lineRows = lines.map(function (l, idx) {
                                var rateFmt = formatMoney(l.PriceActual, l.CurrencySymbol, l.CurrencyISO, l.StdPrecision);
                                var amtFmt = formatMoney(l.LineNetAmt, l.CurrencySymbol, l.CurrencyISO, l.StdPrecision);

                                var chipClass = 'vas-214-chip-neutral';
                                var statusTxt = l.LineStatus;
                                if (l.LineStatus === 'Received') {
                                    chipClass = 'vas-214-chip-ok';
                                    statusTxt = lbl('VAS_Received', 'Received');
                                } else if (l.LineStatus === 'Partial received') {
                                    chipClass = 'vas-214-chip-warn';
                                    statusTxt = lbl('VAS_PartialReceived', 'Partial received');
                                } else {
                                    statusTxt = lbl('VAS_Pending', 'Pending');
                                }

                                return [
                                    String(l.LineNo || (idx + 1)),
                                    l.ProductName || l.ProductSKU || '—',
                                    l.Attribute || '',
                                    l.UOM || '—',
                                    formatDecimal(l.OrderedQty),
                                    nsDash(l, formatDecimal(l.DeliveredQty)),
                                    nsDash(l, formatDecimal(l.PendingQty)),
                                    rateFmt,
                                    amtFmt,
                                    nsDash(l, null) ? '–' : { chip: chipClass, text: statusTxt }
                                ];
                            });

                            var bodyHtml =
                                headerStatsHtml +
                                '<div class="vas-214-msec">' + esc(lbl('VAS_PurchaseOrderLines', 'Purchase order lines')) + '</div>' +
                                pagedTable(lineCols, lineRows, { label: lbl('VAS_LinesOf', 'lines of') + ' ' + poNo });

                            var footNote = lines.length + ' ' + lbl('VAS_Lines', 'lines') + ' · ' +
                                           formatNumber(totalOrderedQty) + ' ' + lbl('VAS_QtyOrdered', 'qty ordered') + ' · ' +
                                           delivStatusTxt;

                            openModal({
                                child: false,
                                title: poNo,
                                subtitle: vendor + ' · ' + dateDisplay + ' · ' + docStatusTxt,
                                body: bodyHtml,
                                foot: '<span class="vas-214-foot-note">' + esc(footNote) + '</span>' +
                                      '<span><button type="button" class="vas-214-btn vas-214-btn-primary vas-214-btn-open-record">' + esc(lbl('VAS_OpenRecord', 'Open Record')) + '</button> ' +
                                      '<button type="button" class="vas-214-btn" data-vas-close="1">' + esc(lbl('VAS_Close', 'Close')) + '</button></span>',
                                after: function () {
                                    $modalHost.find('.vas-214-btn-open-record').on('click', function () {
                                        openPurchaseOrderRecord(poId);
                                    });
                                }
                            });
                        }
                    });
                }
            });
        }

        this.refreshWidget = function () {
            loadQueueData();
        };

        this.getRoot = function () { return $root; };

        this.disposeComponent = function () {
            $(document).off('keydown.vas214');
            $(window).off('resize.vas214');
            if ($modalHost) {
                $modalHost.remove();
                $modalHost = null;
            }
            $root.remove();
        };
    };

    VAS.VAS_214_POPendingForPaymentWidget.prototype.widgetFirevalueChanged = function (value) {
        if (this.listener) { this.listener.widgetFirevalueChanged(value); }
    };

    VAS.VAS_214_POPendingForPaymentWidget.prototype.addChangeListener = function (listener) {
        this.listener = listener;
    };

    VAS.VAS_214_POPendingForPaymentWidget.prototype.init = function (windowNo, frame) {
        this.frame = frame;
        this.AD_UserHomeWidgetID = frame.widgetInfo.AD_UserHomeWidgetID;
        this.windowNo = windowNo;
        this.Initalize();
        this.frame.getContentGrid().append(this.getRoot());
        ensureDashInlineSizeVar(this.getRoot());
    };

    VAS.VAS_214_POPendingForPaymentWidget.prototype.widgetSizeChange = function (height, width) { };

    VAS.VAS_214_POPendingForPaymentWidget.prototype.refreshWidget = function () {
        this.refreshWidget();
    };

    VAS.VAS_214_POPendingForPaymentWidget.prototype.dispose = function () {
        this.disposeComponent();
        if (this.frame) { this.frame.dispose(); }
        this.frame = null;
    };


    /* A charge line, or a product that is not of Item type, is never received:
       received, pending and line status render as a dash instead of a figure. */
    function nsDash(l, v) {
        return (l && (l.IsNonStock || l.isNonStock)) ? '–' : v;
    }

})(VAS, jQuery);
