/**
 * VAS_193_POCompletedMTDWidget
 * 2x1 KPI Widget & Drill-down modal for Purchase Orders dashboard.
 * Displays count of purchase orders reaching terminal states (Completed / Closed) Month-to-Date.
 *
 * =========================================================================
 * Summary Message Table
 *  # | Fallback Text                                    | Message Key
 * ---+--------------------------------------------------+-----------------------------------
 *  1 | Purchase Orders Completed MTD                   | VAS_193_POCompletedMTD
 *  2 | received in full                                 | VAS_193_ReceivedInFull
 *  3 | closed short                                     | VAS_193_ClosedShort
 *  4 | Completed and closed documents                   | VAS_193_CompletedAndClosedDocs
 *  5 | Completed                                        | VAS_193_Completed
 *  6 | Closed                                           | VAS_193_Closed
 *  7 | Received value                                   | VAS_193_ReceivedValue
 *  8 | On-time closure                                  | VAS_193_OnTimeClosure
 *  9 | Closed documents carry no delivery — the balance quantity is written off at closure. | VAS_193_ClosedDocNote
 * 10 | Documents                                        | VAS_193_Documents
 * 11 | Lines                                            | VAS_193_Lines
 * 12 | Purchase order lines                             | VAS_193_POLines
 * 13 | Showing                                          | VAS_193_Showing
 * 14 | of                                               | VAS_193_Of
 * 15 | select a PO number to open the record            | VAS_193_SelectToOpen
 * 16 | Close                                            | VAS_193_Close
 * 17 | Back                                             | VAS_193_Back
 * 18 | Open in Window                                   | VAS_193_OpenInWindow
 * 19 | PO No                                            | VAS_193_PONo
 * 20 | PO date                                          | VAS_193_PODate
 * 21 | Vendor                                           | VAS_193_Vendor
 * 22 | Warehouse                                        | VAS_193_Warehouse
 * 23 | Representative                                   | VAS_193_Representative
 * 24 | Value                                            | VAS_193_Value
 * 25 | Delivery                                         | VAS_193_Delivery
 * 26 | Status                                           | VAS_193_Status
 * 27 | Product                                          | VAS_193_Product
 * 28 | Attribute                                        | VAS_193_Attribute
 * 29 | UoM                                              | VAS_193_UOM
 * 30 | Ordered                                          | VAS_193_Ordered
 * 31 | Received                                         | VAS_193_Received
 * 32 | Pending                                          | VAS_193_Pending
 * 33 | Rate                                             | VAS_193_Rate
 * 34 | Amount                                           | VAS_193_Amount
 * 35 | Line status                                      | VAS_193_LineStatus
 * 36 | Couldn't load                                    | VAS_193_CouldntLoad
 * 37 | No records found                                 | VAS_193_NoRecordsFound
 * 38 | Expected on                                      | VAS_193_ExpectedOn
 * 39 | PO value                                         | VAS_193_POValue
 * 40 | Qty ordered                                      | VAS_193_QtyOrdered
 * 41 | Qty pending                                      | VAS_193_QtyPending
 * 42 | Document status                                  | VAS_193_DocumentStatus
 * 43 | Delivery status                                  | VAS_193_DeliveryStatus
 * 44 | lines of                                         | VAS_193_LinesOf
 * 45 | Purchase order                                   | VAS_193_PurchaseOrder
 * 46 | Previous                                         | VAS_193_Previous
 * 47 | Next                                             | VAS_193_Next
 * =========================================================================
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

    VAS.VAS_193_POCompletedMTDWidget = function () {

        this.frame;
        this.windowNo;

        var $self = this;
        var $wrapper = $('<div class="vas-w193-container">');
        var $root = $('<div class="vas-w193-root">');
        var $card = null;
        var $valueEl = null;
        var $metaEl = null;
        var $busy = null;

        var widgetObserver = null;
        var cachedData = null;
        var poRegistry = {};

        // Modal State Stack
        var CFGSTACK = [];
        var CURCFG = null;
        var $activeModal = null;

        var PAGE_SIZE = 10;
        var currentTablePage = 0;
        var currentLinesPage = 0;

        var MONTH_NAMES = ['Jan', 'Feb', 'Mar', 'Apr', 'May', 'Jun', 'Jul', 'Aug', 'Sep', 'Oct', 'Nov', 'Dec'];

        var ZOOM_WINDOW_NAME = 'VAS_PurchaseOrder';
        var ZOOM_WINDOW_FALLBACK = 'Purchase Order';
        var ZOOM_TABLE = 'C_Order';

        function lbl(key, fallback) {
            var msg = VIS.Msg.getMsg(key);
            return (msg && msg !== key && msg !== '[' + key + ']' && msg.charAt(0) !== '[') ? msg : fallback;
        }

        function escapeHtml(value) {
            return String(value == null ? '' : value)
                .replace(/&/g, '&amp;')
                .replace(/</g, '&lt;')
                .replace(/>/g, '&gt;')
                .replace(/"/g, '&quot;')
                .replace(/'/g, '&#039;');
        }

        function showBusy(show) {
            if (!$busy || !$busy[0]) { return; }
            $busy.toggleClass('vas-w193-hidden', !show);
        }

        function formatCount(value) {
            var n = Number(value || 0);
            return n.toLocaleString(window.navigator.language);
        }

        function formatMoney(value, symbol) {
            var v = Number(value || 0);
            var sym = symbol || '₹';
            if (v >= 10000000) {
                return sym + ' ' + (v / 10000000).toFixed(2) + ' Cr';
            }
            if (v >= 100000) {
                return sym + ' ' + (v / 100000).toFixed(2) + ' L';
            }
            return sym + ' ' + Math.round(v).toLocaleString(window.navigator.language);
        }

        function setupWidgetSizeObserver() {
            if (typeof ResizeObserver === 'undefined') { return; }
            try {
                widgetObserver = new ResizeObserver(function (entries) {
                    for (var i = 0; i < entries.length; i++) {
                        var width = entries[i].contentRect.width;
                        if (width > 0 && $root[0]) {
                            $root[0].style.setProperty('--widget-inline-size', width + 'px');
                        }
                    }
                });
                widgetObserver.observe($wrapper[0]);
            } catch (e) { }
        }

        this.Initalize = function () {
            createWidget();
            setupWidgetSizeObserver();
            loadKpi();
        };

        function createWidget() {
            var title = lbl("VAS_193_POCompletedMTD", "Purchase Orders Completed MTD");

            $card = $(
                '<button type="button" class="w c2 r1 kpi border-ok clickable vas-w193-card vas-widget-bg" aria-label="' + escapeHtml(title) + '">' +
                    '<svg class="vas-w193-opencue" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2.2" stroke-linecap="round" stroke-linejoin="round">' +
                        '<path d="M7 17 17 7M9 7h8v8"/>' +
                    '</svg>' +
                    '<p class="w-title vas-w193-title">' + escapeHtml(title) + '</p>' +
                    '<p class="kpi-val ok vas-w193-val">—</p>' +
                    '<p class="kpi-meta vas-w193-meta"></p>' +
                '</button>'
            );

            $valueEl = $card.find('.vas-w193-val');
            $metaEl = $card.find('.vas-w193-meta');

            $card.on('click', function (e) {
                e.preventDefault();
                openCompletedModal();
            });

            $card.on('keydown', function (e) {
                if (e.key === 'Enter' || e.key === ' ') {
                    e.preventDefault();
                    openCompletedModal();
                }
            });

            $root.append($card);

            $busy = $('<div class="vas-w193-busy vas-w193-hidden"><div class="vis-busyindicatorinnerwrap"><i class="vis_widgetloader"></i></div></div>');
            $root.append($busy);

            $wrapper.append($root);
        }

        function loadKpi() {
            showBusy(true);

            $.ajax({
                url: VIS.Application.contextUrl + 'VAS_193_POCompletedMTDWidget/GetPOCompletedMTDData',
                type: 'GET',
                cache: false,
                success: function (res) {
                    var data = res;
                    if (typeof data === 'string') {
                        try { data = JSON.parse(data); } catch (e) { }
                    }
                    if (typeof data === 'string') {
                        try { data = JSON.parse(data); } catch (e) { }
                    }

                    if (data && data.error) {
                        setError();
                        return;
                    }

                    cachedData = data || {};
                    poRegistry = {};
                    if (cachedData.records && Array.isArray(cachedData.records)) {
                        for (var i = 0; i < cachedData.records.length; i++) {
                            var r = cachedData.records[i];
                            poRegistry[r.PurchaseOrderId] = r;
                            poRegistry[r.PurchaseOrderNumber] = r;
                        }
                    }

                    renderMetric(cachedData);
                },
                error: function () {
                    setError();
                },
                complete: function () {
                    showBusy(false);
                }
            });
        }

        function renderMetric(data) {
            var total = Number(data.totalCount || 0);
            var receivedVal = Number(data.receivedValue || 0);
            var closedShort = Number(data.closedShortCount || 0);
            var curSym = data.currencySymbol || '₹';

            if ($valueEl) {
                $valueEl.text(formatCount(total));
                $valueEl.attr('title', formatCount(total));
            }

            if ($metaEl) {
                var recText = formatMoney(receivedVal, curSym) + ' ' + lbl("VAS_193_ReceivedInFull", "received in full");
                var shortText = formatCount(closedShort) + ' ' + lbl("VAS_193_ClosedShort", "closed short");
                var metaString = recText + ' · ' + shortText;
                $metaEl.text(metaString);
                $metaEl.attr('title', metaString);
            }

            if ($card) {
                $card.prop('disabled', false);
            }
        }

        function setError() {
            if ($valueEl) {
                $valueEl.text('—');
                $valueEl.removeAttr('title');
            }
            if ($metaEl) {
                var errText = lbl("VAS_193_CouldntLoad", "Couldn't load");
                $metaEl.text(errText);
                $metaEl.attr('title', errText);
            }
            if ($card) {
                $card.prop('disabled', false);
            }
        }

        /* =========================================================================
           MODAL ENGINE & DRILL-DOWNS
           ========================================================================= */

        function zoomToPurchaseOrder(cOrderId) {
            if (!cOrderId) { return; }
            closeModal();
            var navigated = false;
            try {
                if ($self.listener && typeof $self.widgetFirevalueChanged === 'function') {
                    $self.widgetFirevalueChanged({
                        "TabWhereClause": ZOOM_TABLE + "." + ZOOM_TABLE + "_ID=" + cOrderId,
                        "TabLayout": "Y",
                        "TabIndex": "0",
                        "AD_Tab_ID": 1002398,
                        "ActionName": ZOOM_WINDOW_NAME,
                        "ActionType": "W"
                    });
                    navigated = true;
                }
            } catch (e) { }

            if (!navigated) {
                try {
                    if (window.VAS && VAS.ZoomUtil && typeof VAS.ZoomUtil.zoomToRecord === 'function') {
                        VAS.ZoomUtil.zoomToRecord(ZOOM_TABLE + "_ID", cOrderId, 0, ZOOM_WINDOW_NAME, ZOOM_WINDOW_FALLBACK);
                    } else if (window.VIS && VIS.AEnv && typeof VIS.AEnv.zoom === 'function') {
                        VIS.AEnv.zoom(259, cOrderId);
                    } else if (window.VIS && VIS.viewManager && typeof VIS.viewManager.startWindow === 'function') {
                        VIS.viewManager.startWindow(143, cOrderId);
                    }
                } catch (e2) { }
            }
        }

        function openModal(cfg, isBack) {
            if (!isBack) {
                if (cfg.child && CURCFG) {
                    CFGSTACK.push(CURCFG);
                } else if (!cfg.child) {
                    CFGSTACK = [];
                }
                CURCFG = cfg;
            } else {
                CURCFG = cfg;
            }

            if (!$activeModal) {
                createModalShell();
            }

            var $mask = $activeModal;
            var $modal = $mask.find('.vas-w193-modal');
            var $mBack = $mask.find('#vas193-mBack');
            var $mTitle = $mask.find('#vas193-mTitle');
            var $mSub = $mask.find('#vas193-mSub');
            var $mBody = $mask.find('#vas193-mBody');
            var $mFoot = $mask.find('#vas193-mFoot');

            $mBack.toggle(CFGSTACK.length > 0);
            $modal.removeClass('sm md').addClass(cfg.size ? cfg.size : '');
            $mBody.attr('class', 'vas-w193-modal-body modal-body' + (cfg.bodyClass ? ' ' + cfg.bodyClass : ''));

            $mTitle.text(cfg.title || '');
            $mSub.text(cfg.subtitle || '');
            $mBody.html(cfg.body || '');
            $mFoot.html(cfg.foot || '<span class="vas-w193-foot-note foot-note"></span><button class="vas-w193-btn btn" data-close="1">' + escapeHtml(lbl("VAS_193_Close", "Close")) + '</button>');

            $mask.addClass('vas-w193-mask-open open');

            if (cfg.after) {
                cfg.after($mask);
            }
        }

        function backModal() {
            var prevCfg = CFGSTACK.pop();
            if (!prevCfg) {
                closeModal();
                return;
            }
            openModal(prevCfg, true);
        }

        function closeModal() {
            if ($activeModal) {
                $activeModal.removeClass('vas-w193-mask-open open');
                $activeModal.remove();
                $activeModal = null;
            }
            CFGSTACK = [];
            CURCFG = null;
        }

        function createModalShell() {
            var shellHtml =
                '<div class="vas-w193-mask mask" id="vas193-mask" role="dialog" aria-modal="true">' +
                    '<div class="vas-w193-modal modal" id="vas193-modal">' +
                        '<div class="vas-w193-modal-header modal-header">' +
                            '<div style="display:flex;align-items:center;gap:8px;min-width:0">' +
                                '<button type="button" class="vas-w193-xbtn xbtn" id="vas193-mBack" aria-label="' + escapeHtml(lbl("VAS_193_Back", "Back")) + '" style="display:none;">' +
                                    '<svg width="20" height="20" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2" stroke-linecap="round" stroke-linejoin="round">' +
                                        '<path d="M19 12H5M12 19l-7-7 7-7"/>' +
                                    '</svg>' +
                                '</button>' +
                                '<div class="vas-w193-htxt htxt">' +
                                    '<h2 id="vas193-mTitle">Title</h2>' +
                                    '<div class="vas-w193-msub msub" id="vas193-mSub"></div>' +
                                '</div>' +
                            '</div>' +
                            '<div class="vas-w193-hact haact">' +
                                '<button type="button" class="vas-w193-xbtn xbtn" id="vas193-mClose" aria-label="' + escapeHtml(lbl("VAS_193_Close", "Close")) + '">' +
                                    '<svg width="20" height="20" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2" stroke-linecap="round">' +
                                        '<path d="M18 6 6 18M6 6l12 12"/>' +
                                    '</svg>' +
                                '</button>' +
                            '</div>' +
                        '</div>' +
                        '<div class="vas-w193-modal-body modal-body" id="vas193-mBody"></div>' +
                        '<div class="vas-w193-modal-foot modal-foot" id="vas193-mFoot"></div>' +
                    '</div>' +
                '</div>';

            $activeModal = $(shellHtml);
            $('body').append($activeModal);

            $activeModal.find('#vas193-mBack').on('click', function () { backModal(); });
            $activeModal.find('#vas193-mClose').on('click', function () { closeModal(); });
            $activeModal.on('click', function (e) {
                if (e.target === this || $(e.target).closest('[data-close]').length > 0) {
                    closeModal();
                }
            });

            $(document).off('keydown.vas193modal').on('keydown.vas193modal', function (e) {
                if (e.key === 'Escape') {
                    closeModal();
                }
            });
        }

        /* 1. Main Drill-down Modal (PO Completed MTD) */
        function openCompletedModal() {
            var data = cachedData || {};
            var records = data.records || [];
            currentTablePage = 0;

            var now = new Date();
            var dayPad = (now.getDate() < 10 ? '0' : '') + now.getDate();
            var monStr = MONTH_NAMES[now.getMonth()];
            var yearStr = now.getFullYear();

            var title = lbl("VAS_193_POCompletedMTD", "Purchase Orders Completed MTD");
            var subtitle = lbl("VAS_193_CompletedAndClosedDocs", "Completed and closed documents") + ' · 01–' + dayPad + ' ' + monStr + ' ' + yearStr;

            var completedVal = formatCount(data.completedCount || 0);
            var closedVal = formatCount(data.closedCount || 0);
            var receivedFormatted = formatMoney(data.receivedValue || 0, data.currencySymbol || '₹');
            var onTimeStr = (data.onTimePercent != null) ? (data.onTimePercent + '%') : '—';

            var statStripHtml =
                '<div class="vas-w193-mstats mstats">' +
                    '<div class="vas-w193-mstat mstat">' +
                        '<div class="vas-w193-mstat-l l">' + escapeHtml(lbl("VAS_193_Completed", "Completed")) + '</div>' +
                        '<div class="vas-w193-mstat-v v">' + escapeHtml(completedVal) + '</div>' +
                    '</div>' +
                    '<div class="vas-w193-mstat mstat">' +
                        '<div class="vas-w193-mstat-l l">' + escapeHtml(lbl("VAS_193_Closed", "Closed")) + '</div>' +
                        '<div class="vas-w193-mstat-v v">' + escapeHtml(closedVal) + '</div>' +
                    '</div>' +
                    '<div class="vas-w193-mstat mstat">' +
                        '<div class="vas-w193-mstat-l l">' + escapeHtml(lbl("VAS_193_ReceivedValue", "Received value")) + '</div>' +
                        '<div class="vas-w193-mstat-v v">' + escapeHtml(receivedFormatted) + '</div>' +
                    '</div>' +
                    '<div class="vas-w193-mstat mstat">' +
                        '<div class="vas-w193-mstat-l l">' + escapeHtml(lbl("VAS_193_OnTimeClosure", "On-time closure")) + '</div>' +
                        '<div class="vas-w193-mstat-v v">' + escapeHtml(onTimeStr) + '</div>' +
                    '</div>' +
                '</div>';

            var noteHtml =
                '<div class="vas-w193-mnote mnote">' +
                    escapeHtml(lbl("VAS_193_ClosedDocNote", "Closed documents carry no delivery — the balance quantity is written off at closure.")) +
                '</div>';

            var secHtml = '<div class="vas-w193-msec msec">' + escapeHtml(lbl("VAS_193_Documents", "Documents")) + '</div>';

            var bodyHtml =
                statStripHtml +
                noteHtml +
                secHtml +
                '<div class="vas-w193-tbl-wrap" id="vas193-doc-tbl-wrap"></div>';

            var footHtml =
                '<span class="vas-w193-foot-note foot-note" id="vas193-doc-foot-note"></span>' +
                '<span style="display:flex;align-items:center;gap:8px;">' +
                    '<span class="vas-w193-pager pager" id="vas193-doc-pager"></span>' +
                    '<button type="button" class="vas-w193-btn btn" data-close="1">' + escapeHtml(lbl("VAS_193_Close", "Close")) + '</button>' +
                '</span>';

            openModal({
                child: false,
                title: title,
                subtitle: subtitle,
                body: bodyHtml,
                foot: footHtml,
                after: function ($m) {
                    renderMainDocTable($m, records, data.currencySymbol);
                }
            });
        }

        function renderMainDocTable($m, records, curSym) {
            var sym = curSym || '₹';
            var totalRows = records.length;
            var totalPages = Math.max(1, Math.ceil(totalRows / PAGE_SIZE));
            currentTablePage = Math.min(currentTablePage, totalPages - 1);
            if (currentTablePage < 0) { currentTablePage = 0; }

            var s = currentTablePage * PAGE_SIZE;
            var slice = records.slice(s, s + PAGE_SIZE);

            var rowsHtml = '';
            for (var i = 0; i < slice.length; i++) {
                var p = slice[i];
                var valFormatted = formatMoney(p.PoValueConverted || p.PoValueDocumentCurrency, sym);

                rowsHtml +=
                    '<div class="vas-w193-trow vas-w193-doc-grid trow">' +
                        '<span class="vas-w193-cell cell center">' +
                            '<button type="button" class="vas-w193-iconbtn iconbtn" data-lines="' + p.PurchaseOrderId + '" title="' + escapeHtml(lbl("VAS_193_LinesOf", "lines of") + ' ' + p.PurchaseOrderNumber) + '">' +
                                '<svg viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2" stroke-linecap="round">' +
                                    '<path d="M8 6h13M8 12h13M8 18h13M3 6h.01M3 12h.01M3 18h.01"/>' +
                                '</svg>' +
                            '</button>' +
                        '</span>' +
                        '<span class="vas-w193-cell cell">' +
                            '<button type="button" class="vas-w193-lnk lnk" data-poid="' + p.PurchaseOrderId + '" title="' + escapeHtml(p.PurchaseOrderNumber) + '">' +
                                escapeHtml(p.PurchaseOrderNumber) +
                            '</button>' +
                        '</span>' +
                        '<span class="vas-w193-cell cell c-std" title="' + escapeHtml(p.OrderDateFormatted) + '">' + escapeHtml(p.OrderDateFormatted) + '</span>' +
                        '<span class="vas-w193-cell cell c-std" title="' + escapeHtml(p.VendorName) + '">' + escapeHtml(p.VendorName) + '</span>' +
                        '<span class="vas-w193-cell cell c-std" title="' + escapeHtml(p.WarehouseName) + '">' + escapeHtml(p.WarehouseName) + '</span>' +
                        '<span class="vas-w193-cell cell c-std" title="' + escapeHtml(p.RepresentativeName) + '">' + escapeHtml(p.RepresentativeName) + '</span>' +
                        '<span class="vas-w193-cell cell right c-emph" title="' + escapeHtml(valFormatted) + '">' + escapeHtml(valFormatted) + '</span>' +
                        '<span class="vas-w193-cell cell" title="' + escapeHtml(p.DeliveryText) + '">' +
                            '<span class="vas-w193-chip chip ' + p.DeliveryChip + '">' + escapeHtml(p.DeliveryText) + '</span>' +
                        '</span>' +
                        '<span class="vas-w193-cell cell" title="' + escapeHtml(p.DocStatusText) + '">' +
                            '<span class="vas-w193-chip chip ' + p.DocStatusChip + '">' + escapeHtml(p.DocStatusText) + '</span>' +
                        '</span>' +
                    '</div>';
            }

            if (slice.length === 0) {
                rowsHtml = '<div style="padding: 1.5em; text-align: center; color: #5F7283; font-size: 0.8125em;">' +
                    escapeHtml(lbl("VAS_193_NoRecordsFound", "No records found")) + '</div>';
            }

            var tableHtml =
                '<div class="vas-w193-tbl mtbl">' +
                    '<div class="vas-w193-trow vas-w193-doc-grid mhead thead">' +
                        '<span class="vas-w193-cell cell center"></span>' +
                        '<span class="vas-w193-cell cell" title="' + escapeHtml(lbl("VAS_193_PONo", "PO No")) + '">' + escapeHtml(lbl("VAS_193_PONo", "PO No")) + '</span>' +
                        '<span class="vas-w193-cell cell" title="' + escapeHtml(lbl("VAS_193_PODate", "PO date")) + '">' + escapeHtml(lbl("VAS_193_PODate", "PO date")) + '</span>' +
                        '<span class="vas-w193-cell cell" title="' + escapeHtml(lbl("VAS_193_Vendor", "Vendor")) + '">' + escapeHtml(lbl("VAS_193_Vendor", "Vendor")) + '</span>' +
                        '<span class="vas-w193-cell cell" title="' + escapeHtml(lbl("VAS_193_Warehouse", "Warehouse")) + '">' + escapeHtml(lbl("VAS_193_Warehouse", "Warehouse")) + '</span>' +
                        '<span class="vas-w193-cell cell" title="' + escapeHtml(lbl("VAS_193_Representative", "Representative")) + '">' + escapeHtml(lbl("VAS_193_Representative", "Representative")) + '</span>' +
                        '<span class="vas-w193-cell cell right" title="' + escapeHtml(lbl("VAS_193_Value", "Value")) + '">' + escapeHtml(lbl("VAS_193_Value", "Value")) + '</span>' +
                        '<span class="vas-w193-cell cell" title="' + escapeHtml(lbl("VAS_193_Delivery", "Delivery")) + '">' + escapeHtml(lbl("VAS_193_Delivery", "Delivery")) + '</span>' +
                        '<span class="vas-w193-cell cell" title="' + escapeHtml(lbl("VAS_193_Status", "Status")) + '">' + escapeHtml(lbl("VAS_193_Status", "Status")) + '</span>' +
                    '</div>' +
                    '<div class="vas-w193-tbody tbody mbody">' +
                        rowsHtml +
                    '</div>' +
                '</div>';

            $m.find('#vas193-doc-tbl-wrap').html(tableHtml);

            // Foot note & pager
            var noteTxt = totalRows === 0 ? '' :
                lbl("VAS_193_Showing", "Showing") + ' ' + (s + 1) + '–' + (s + slice.length) + ' ' + lbl("VAS_193_Of", "of") + ' ' + totalRows + ' · ' + lbl("VAS_193_SelectToOpen", "select a PO number to open the record");
            $m.find('#vas193-doc-foot-note').text(noteTxt);

            var pagerHtml = '';
            if (totalPages > 1) {
                pagerHtml =
                    '<button type="button" class="vas-w193-pbtn pbtn" id="vas193-pg-prev"' + (currentTablePage === 0 ? ' disabled' : '') + ' aria-label="' + escapeHtml(lbl("VAS_193_Previous", "Previous")) + '">' +
                        '<svg viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2.4" stroke-linecap="round"><polyline points="15 18 9 12 15 6"/></svg>' +
                    '</button>' +
                    '<span class="vas-w193-ptxt ptxt">' + (currentTablePage + 1) + ' ' + lbl("VAS_193_Of", "of") + ' ' + totalPages + '</span>' +
                    '<button type="button" class="vas-w193-pbtn pbtn" id="vas193-pg-next"' + (currentTablePage >= totalPages - 1 ? ' disabled' : '') + ' aria-label="' + escapeHtml(lbl("VAS_193_Next", "Next")) + '">' +
                        '<svg viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2.4" stroke-linecap="round"><polyline points="9 18 15 12 9 6"/></svg>' +
                    '</button>';
            }
            $m.find('#vas193-doc-pager').html(pagerHtml);

            // Wire events
            $m.find('#vas193-pg-prev').on('click', function () {
                if (currentTablePage > 0) {
                    currentTablePage--;
                    renderMainDocTable($m, records, sym);
                }
            });
            $m.find('#vas193-pg-next').on('click', function () {
                if (currentTablePage < totalPages - 1) {
                    currentTablePage++;
                    renderMainDocTable($m, records, sym);
                }
            });
            $m.find('.vas-w193-lnk[data-poid]').on('click', function () {
                var poid = Number($(this).attr('data-poid'));
                openRecordModal(poid);
            });
            $m.find('.vas-w193-iconbtn[data-lines]').on('click', function () {
                var poid = Number($(this).attr('data-lines'));
                openLinesModal(poid);
            });
        }

        /* 2. PO Record View Modal */
        function openRecordModal(cOrderId) {
            var po = poRegistry[cOrderId];
            if (!po) { return; }

            showBusy(true);

            $.ajax({
                url: VIS.Application.contextUrl + 'VAS_193_POCompletedMTDWidget/GetPOLines',
                type: 'GET',
                data: { C_Order_ID: cOrderId },
                cache: false,
                success: function (res) {
                    var data = res;
                    if (typeof data === 'string') {
                        try { data = JSON.parse(data); } catch (e) { }
                    }
                    if (typeof data === 'string') {
                        try { data = JSON.parse(data); } catch (e) { }
                    }

                    renderRecordModalView(po, data || {});
                },
                error: function () {
                    renderRecordModalView(po, { lines: [] });
                },
                complete: function () {
                    showBusy(false);
                }
            });
        }

        function renderRecordModalView(po, lineData) {
            var lines = lineData.lines || [];
            currentLinesPage = 0;
            var curSym = lineData.currencySymbol || cachedData.currencySymbol || '₹';

            var title = po.PurchaseOrderNumber;
            var subtitle = po.VendorName + ' · ' + po.OrderDateFormatted + ' · ' + po.DocStatusText;

            var statsHtml =
                '<div class="vas-w193-mstats mstats" style="grid-template-columns: repeat(4, minmax(0, 1fr));">' +
                    '<div class="vas-w193-mstat mstat">' +
                        '<div class="vas-w193-mstat-l l">' + escapeHtml(lbl("VAS_193_Vendor", "Vendor")) + '</div>' +
                        '<div class="vas-w193-mstat-v v" title="' + escapeHtml(po.VendorName) + '">' + escapeHtml(po.VendorName) + '</div>' +
                    '</div>' +
                    '<div class="vas-w193-mstat mstat">' +
                        '<div class="vas-w193-mstat-l l">' + escapeHtml(lbl("VAS_193_PODate", "PO date")) + '</div>' +
                        '<div class="vas-w193-mstat-v v">' + escapeHtml(po.OrderDateFormatted) + '</div>' +
                    '</div>' +
                    '<div class="vas-w193-mstat mstat">' +
                        '<div class="vas-w193-mstat-l l">' + escapeHtml(lbl("VAS_193_ExpectedOn", "Expected on")) + '</div>' +
                        '<div class="vas-w193-mstat-v v">' + escapeHtml(po.PromisedDateFormatted || '—') + '</div>' +
                    '</div>' +
                    '<div class="vas-w193-mstat mstat">' +
                        '<div class="vas-w193-mstat-l l">' + escapeHtml(lbl("VAS_193_POValue", "PO value")) + '</div>' +
                        '<div class="vas-w193-mstat-v v">' + escapeHtml(formatMoney(po.PoValueConverted || po.PoValueDocumentCurrency, curSym)) + '</div>' +
                    '</div>' +
                    '<div class="vas-w193-mstat mstat">' +
                        '<div class="vas-w193-mstat-l l">' + escapeHtml(lbl("VAS_193_Warehouse", "Warehouse")) + '</div>' +
                        '<div class="vas-w193-mstat-v v" title="' + escapeHtml(po.WarehouseName) + '">' + escapeHtml(po.WarehouseName) + '</div>' +
                    '</div>' +
                    '<div class="vas-w193-mstat mstat">' +
                        '<div class="vas-w193-mstat-l l">' + escapeHtml(lbl("VAS_193_Representative", "Representative")) + '</div>' +
                        '<div class="vas-w193-mstat-v v" title="' + escapeHtml(po.RepresentativeName) + '">' + escapeHtml(po.RepresentativeName) + '</div>' +
                    '</div>' +
                    '<div class="vas-w193-mstat mstat">' +
                        '<div class="vas-w193-mstat-l l">' + escapeHtml(lbl("VAS_193_DocumentStatus", "Document status")) + '</div>' +
                        '<div class="vas-w193-mstat-v v">' + escapeHtml(po.DocStatusText) + '</div>' +
                    '</div>' +
                    '<div class="vas-w193-mstat mstat">' +
                        '<div class="vas-w193-mstat-l l">' + escapeHtml(lbl("VAS_193_DeliveryStatus", "Delivery status")) + '</div>' +
                        '<div class="vas-w193-mstat-v v">' + escapeHtml(po.DeliveryText) + '</div>' +
                    '</div>' +
                '</div>';

            var secHtml = '<div class="vas-w193-msec msec">' + escapeHtml(lbl("VAS_193_POLines", "Purchase order lines")) + '</div>';

            var bodyHtml =
                statsHtml +
                secHtml +
                '<div class="vas-w193-tbl-wrap" id="vas193-lines-tbl-wrap"></div>';

            var totalOrderedQty = Number(lineData.totalOrderedQty || po.OrderedQty || 0);

            var footHtml =
                '<span class="vas-w193-foot-note foot-note" id="vas193-lines-foot-note">' +
                    lines.length + ' ' + lbl("VAS_193_Lines", "lines") + ' · ' +
                    formatCount(totalOrderedQty) + ' ' + lbl("VAS_193_Ordered", "ordered") + ' · ' +
                    po.DeliveryText +
                '</span>' +
                '<span style="display:flex;align-items:center;gap:8px;">' +
                    '<span class="vas-w193-pager pager" id="vas193-lines-pager"></span>' +
                    '<button type="button" class="vas-w193-btn vas-w193-btn-primary btn btn-primary" id="vas193-btn-zoom">' +
                        escapeHtml(lbl("VAS_193_OpenInWindow", "Open in Window")) +
                    '</button>' +
                    '<button type="button" class="vas-w193-btn btn" id="vas193-btn-back">' + escapeHtml(lbl("VAS_193_Back", "Back")) + '</button>' +
                    '<button type="button" class="vas-w193-btn btn" data-close="1">' + escapeHtml(lbl("VAS_193_Close", "Close")) + '</button>' +
                '</span>';

            openModal({
                child: true,
                title: title,
                subtitle: subtitle,
                body: bodyHtml,
                foot: footHtml,
                after: function ($m) {
                    $m.find('#vas193-btn-back').on('click', function () { backModal(); });
                    $m.find('#vas193-btn-zoom').on('click', function () { zoomToPurchaseOrder(po.PurchaseOrderId); });
                    renderLinesTable($m, lines, curSym);
                }
            });
        }

        /* 3. Lines Quick View Modal */
        function openLinesModal(cOrderId) {
            var po = poRegistry[cOrderId];
            if (!po) { return; }

            showBusy(true);

            $.ajax({
                url: VIS.Application.contextUrl + 'VAS_193_POCompletedMTDWidget/GetPOLines',
                type: 'GET',
                data: { C_Order_ID: cOrderId },
                cache: false,
                success: function (res) {
                    var data = res;
                    if (typeof data === 'string') {
                        try { data = JSON.parse(data); } catch (e) { }
                    }
                    if (typeof data === 'string') {
                        try { data = JSON.parse(data); } catch (e) { }
                    }

                    renderLinesModalView(po, data || {});
                },
                error: function () {
                    renderLinesModalView(po, { lines: [] });
                },
                complete: function () {
                    showBusy(false);
                }
            });
        }

        function renderLinesModalView(po, lineData) {
            var lines = lineData.lines || [];
            currentLinesPage = 0;
            var curSym = lineData.currencySymbol || cachedData.currencySymbol || '₹';

            var title = lbl("VAS_193_Lines", "Lines") + ' · ' + po.PurchaseOrderNumber;
            var subtitle = po.VendorName + ' · ' + po.OrderDateFormatted + ' · ' + po.DeliveryText;

            var poLinkHtml =
                '<div class="vas-w193-polink polink" style="font-size: 0.75em; color: #5F7283; margin-bottom: 0.25em;">' +
                    escapeHtml(lbl("VAS_193_PurchaseOrder", "Purchase order")) + ' <button type="button" class="vas-w193-lnk lnk" id="vas193-lnk-po-rec" style="display:inline;padding:0 4px;">' +
                        escapeHtml(po.PurchaseOrderNumber) +
                    '</button>' +
                    ' · ' + escapeHtml(po.OrderDateFormatted) + ' · ' + escapeHtml(po.DocStatusText) +
                '</div>';

            var totalOrderedQty = Number(lineData.totalOrderedQty || po.OrderedQty || 0);
            var totalPendingQty = Number(lineData.totalPendingQty || po.PendingQty || 0);
            var poValFormatted = formatMoney(po.PoValueConverted || po.PoValueDocumentCurrency, curSym);

            var statsHtml =
                '<div class="vas-w193-mstats mstats" style="grid-template-columns: repeat(4, minmax(0, 1fr));">' +
                    '<div class="vas-w193-mstat mstat">' +
                        '<div class="vas-w193-mstat-l l">' + escapeHtml(lbl("VAS_193_Lines", "Lines")) + '</div>' +
                        '<div class="vas-w193-mstat-v v">' + lines.length + '</div>' +
                    '</div>' +
                    '<div class="vas-w193-mstat mstat">' +
                        '<div class="vas-w193-mstat-l l">' + escapeHtml(lbl("VAS_193_POValue", "PO value")) + '</div>' +
                        '<div class="vas-w193-mstat-v v">' + escapeHtml(poValFormatted) + '</div>' +
                    '</div>' +
                    '<div class="vas-w193-mstat mstat">' +
                        '<div class="vas-w193-mstat-l l">' + escapeHtml(lbl("VAS_193_QtyOrdered", "Qty ordered")) + '</div>' +
                        '<div class="vas-w193-mstat-v v">' + escapeHtml(formatCount(totalOrderedQty)) + '</div>' +
                    '</div>' +
                    '<div class="vas-w193-mstat mstat">' +
                        '<div class="vas-w193-mstat-l l">' + escapeHtml(lbl("VAS_193_QtyPending", "Qty pending")) + '</div>' +
                        '<div class="vas-w193-mstat-v v">' + escapeHtml(formatCount(totalPendingQty)) + '</div>' +
                    '</div>' +
                '</div>';

            var secHtml = '<div class="vas-w193-msec msec">' + escapeHtml(lbl("VAS_193_POLines", "Purchase order lines")) + '</div>';

            var bodyHtml =
                poLinkHtml +
                statsHtml +
                secHtml +
                '<div class="vas-w193-tbl-wrap" id="vas193-lines-tbl-wrap"></div>';

            var footHtml =
                '<span class="vas-w193-foot-note foot-note">' + escapeHtml(po.PurchaseOrderNumber + ' · ' + po.VendorName) + '</span>' +
                '<span style="display:flex;align-items:center;gap:8px;">' +
                    '<span class="vas-w193-pager pager" id="vas193-lines-pager"></span>' +
                    '<button type="button" class="vas-w193-btn btn" id="vas193-btn-back">' + escapeHtml(lbl("VAS_193_Back", "Back")) + '</button>' +
                    '<button type="button" class="vas-w193-btn btn" data-close="1">' + escapeHtml(lbl("VAS_193_Close", "Close")) + '</button>' +
                '</span>';

            openModal({
                child: true,
                size: 'md',
                title: title,
                subtitle: subtitle,
                body: bodyHtml,
                foot: footHtml,
                after: function ($m) {
                    $m.find('#vas193-btn-back').on('click', function () { backModal(); });
                    $m.find('#vas193-lnk-po-rec').on('click', function () { openRecordModal(po.PurchaseOrderId); });
                    renderLinesTable($m, lines, curSym);
                }
            });
        }

        function renderLinesTable($m, lines, curSym) {
            var sym = curSym || '₹';
            var totalRows = lines.length;
            var totalPages = Math.max(1, Math.ceil(totalRows / PAGE_SIZE));
            currentLinesPage = Math.min(currentLinesPage, totalPages - 1);
            if (currentLinesPage < 0) { currentLinesPage = 0; }

            var s = currentLinesPage * PAGE_SIZE;
            var slice = lines.slice(s, s + PAGE_SIZE);

            var rowsHtml = '';
            for (var i = 0; i < slice.length; i++) {
                var l = slice[i];
                var rateFormatted = sym + ' ' + formatCount(l.PriceActual);
                var amtFormatted = formatMoney(l.LineNetAmt, sym);

                rowsHtml +=
                    '<div class="vas-w193-trow vas-w193-line-grid trow">' +
                        '<span class="vas-w193-cell cell right c-std" title="' + l.LineNo + '">' + (s + i + 1) + '</span>' +
                        '<span class="vas-w193-cell cell c-prim" title="' + escapeHtml(l.ProductName) + '">' + escapeHtml(l.ProductName) + '</span>' +
                        '<span class="vas-w193-cell cell c-std" title="' + escapeHtml(l.Attribute) + '">' + escapeHtml(l.Attribute) + '</span>' +
                        '<span class="vas-w193-cell cell c-std" title="' + escapeHtml(l.UOM) + '">' + escapeHtml(l.UOM) + '</span>' +
                        '<span class="vas-w193-cell cell right c-std" title="' + formatCount(l.QtyOrdered) + '">' + formatCount(l.QtyOrdered) + '</span>' +
                        '<span class="vas-w193-cell cell right c-std" title="' + formatCount(l.QtyDelivered) + '">' + formatCount(l.QtyDelivered) + '</span>' +
                        '<span class="vas-w193-cell cell right c-prim" title="' + formatCount(l.QtyPending) + '">' + formatCount(l.QtyPending) + '</span>' +
                        '<span class="vas-w193-cell cell right c-std" title="' + escapeHtml(rateFormatted) + '">' + escapeHtml(rateFormatted) + '</span>' +
                        '<span class="vas-w193-cell cell right c-emph" title="' + escapeHtml(amtFormatted) + '">' + escapeHtml(amtFormatted) + '</span>' +
                        '<span class="vas-w193-cell cell" title="' + escapeHtml(l.LineStatus) + '">' +
                            '<span class="vas-w193-chip chip ' + l.LineStatusChip + '">' + escapeHtml(l.LineStatus) + '</span>' +
                        '</span>' +
                    '</div>';
            }

            if (slice.length === 0) {
                rowsHtml = '<div style="padding: 1.5em; text-align: center; color: #5F7283; font-size: 0.8125em;">' +
                    escapeHtml(lbl("VAS_193_NoRecordsFound", "No records found")) + '</div>';
            }

            var tableHtml =
                '<div class="vas-w193-tbl mtbl">' +
                    '<div class="vas-w193-trow vas-w193-line-grid mhead thead">' +
                        '<span class="vas-w193-cell cell right" title="#">#</span>' +
                        '<span class="vas-w193-cell cell" title="' + escapeHtml(lbl("VAS_193_Product", "Product")) + '">' + escapeHtml(lbl("VAS_193_Product", "Product")) + '</span>' +
                        '<span class="vas-w193-cell cell" title="' + escapeHtml(lbl("VAS_193_Attribute", "Attribute")) + '">' + escapeHtml(lbl("VAS_193_Attribute", "Attribute")) + '</span>' +
                        '<span class="vas-w193-cell cell" title="' + escapeHtml(lbl("VAS_193_UOM", "UoM")) + '">' + escapeHtml(lbl("VAS_193_UOM", "UoM")) + '</span>' +
                        '<span class="vas-w193-cell cell right" title="' + escapeHtml(lbl("VAS_193_Ordered", "Ordered")) + '">' + escapeHtml(lbl("VAS_193_Ordered", "Ordered")) + '</span>' +
                        '<span class="vas-w193-cell cell right" title="' + escapeHtml(lbl("VAS_193_Received", "Received")) + '">' + escapeHtml(lbl("VAS_193_Received", "Received")) + '</span>' +
                        '<span class="vas-w193-cell cell right" title="' + escapeHtml(lbl("VAS_193_Pending", "Pending")) + '">' + escapeHtml(lbl("VAS_193_Pending", "Pending")) + '</span>' +
                        '<span class="vas-w193-cell cell right" title="' + escapeHtml(lbl("VAS_193_Rate", "Rate")) + '">' + escapeHtml(lbl("VAS_193_Rate", "Rate")) + '</span>' +
                        '<span class="vas-w193-cell cell right" title="' + escapeHtml(lbl("VAS_193_Amount", "Amount")) + '">' + escapeHtml(lbl("VAS_193_Amount", "Amount")) + '</span>' +
                        '<span class="vas-w193-cell cell" title="' + escapeHtml(lbl("VAS_193_LineStatus", "Line status")) + '">' + escapeHtml(lbl("VAS_193_LineStatus", "Line status")) + '</span>' +
                    '</div>' +
                    '<div class="vas-w193-tbody tbody mbody">' +
                        rowsHtml +
                    '</div>' +
                '</div>';

            $m.find('#vas193-lines-tbl-wrap').html(tableHtml);

            var pagerHtml = '';
            if (totalPages > 1) {
                pagerHtml =
                    '<button type="button" class="vas-w193-pbtn pbtn" id="vas193-lines-pg-prev"' + (currentLinesPage === 0 ? ' disabled' : '') + ' aria-label="' + escapeHtml(lbl("VAS_193_Previous", "Previous")) + '">' +
                        '<svg viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2.4" stroke-linecap="round"><polyline points="15 18 9 12 15 6"/></svg>' +
                    '</button>' +
                    '<span class="vas-w193-ptxt ptxt">' + (currentLinesPage + 1) + ' ' + lbl("VAS_193_Of", "of") + ' ' + totalPages + '</span>' +
                    '<button type="button" class="vas-w193-pbtn pbtn" id="vas193-lines-pg-next"' + (currentLinesPage >= totalPages - 1 ? ' disabled' : '') + ' aria-label="' + escapeHtml(lbl("VAS_193_Next", "Next")) + '">' +
                        '<svg viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2.4" stroke-linecap="round"><polyline points="9 18 15 12 9 6"/></svg>' +
                    '</button>';
            }
            $m.find('#vas193-lines-pager').html(pagerHtml);

            $m.find('#vas193-lines-pg-prev').on('click', function () {
                if (currentLinesPage > 0) {
                    currentLinesPage--;
                    renderLinesTable($m, lines, sym);
                }
            });
            $m.find('#vas193-lines-pg-next').on('click', function () {
                if (currentLinesPage < totalPages - 1) {
                    currentLinesPage++;
                    renderLinesTable($m, lines, sym);
                }
            });
        }

        this.refreshWidget = function () {
            loadKpi();
        };

        this.getRoot = function () {
            return $wrapper;
        };

        this.disposeComponent = function () {
            if (widgetObserver && $wrapper[0]) {
                widgetObserver.unobserve($wrapper[0]);
                widgetObserver = null;
            }
            if ($card) {
                $card.off();
            }
            closeModal();
            $(document).off('keydown.vas193modal');
            $root.remove();
            $wrapper.remove();
        };
    };

    VAS.VAS_193_POCompletedMTDWidget.prototype.widgetFirevalueChanged = function (value) {
        if (this.listener) {
            this.listener.widgetFirevalueChanged(value);
        }
    };

    VAS.VAS_193_POCompletedMTDWidget.prototype.addChangeListener = function (listener) {
        this.listener = listener;
    };

    VAS.VAS_193_POCompletedMTDWidget.prototype.init = function (windowNo, frame) {
        this.frame = frame;
        this.AD_UserHomeWidgetID = frame.widgetInfo.AD_UserHomeWidgetID;
        this.windowNo = windowNo;
        this.Initalize();
        this.frame.getContentGrid().append(this.getRoot());
        ensureDashInlineSizeVar(this.getRoot());
    };

    VAS.VAS_193_POCompletedMTDWidget.prototype.widgetSizeChange = function (height, width) { };

    VAS.VAS_193_POCompletedMTDWidget.prototype.refreshWidget = function () {
        this.refreshWidget();
    };

    VAS.VAS_193_POCompletedMTDWidget.prototype.dispose = function () {
        this.disposeComponent();
        if (this.frame) {
            this.frame.dispose();
        }
        this.frame = null;
    };

})(VAS, jQuery);
