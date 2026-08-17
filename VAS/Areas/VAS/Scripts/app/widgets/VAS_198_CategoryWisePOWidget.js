/**
 * VAS_198_CategoryWisePOWidget
 * 2x2 Category Wise PO Donut Widget for Purchase Order dashboard.
 * Displays monthly converted PO line value grouped by M_Product_Category (top 3 + Other),
 * with interactive donut hover/click, Category Drill-down Modal, and PO Lines Modal.
 *
 * Summary Message Table
 *  # | Current Text                          | Message Key
 * ---+---------------------------------------+------------------------------------
 *  1 | Category wise PO                      | VAS_198_CategoryWisePO
 *  2 | Total PO value                        | VAS_198_TotalPOValue
 *  3 | Category value                        | VAS_198_CategoryValue
 *  4 | Share                                 | VAS_198_Share
 *  5 | POs                                   | VAS_198_POs
 *  6 | Vendors                               | VAS_198_Vendors
 *  7 | Purchase orders in this category      | VAS_198_PurchaseOrdersInCategory
 *  8 | Purchase order lines                  | VAS_198_PurchaseOrderLines
 *  9 | Showing                               | VAS_198_Showing
 * 10 | of                                    | VAS_198_Of
 * 11 | Page                                  | VAS_198_Page
 * 12 | Close                                 | VAS_198_Close
 * 13 | Back                                  | VAS_198_Back
 * 14 | PO No                                 | VAS_198_PONo
 * 15 | PO date                               | VAS_198_PODate
 * 16 | Vendor                                | VAS_198_Vendor
 * 17 | Warehouse                             | VAS_198_Warehouse
 * 18 | Representative                        | VAS_198_Representative
 * 19 | Value                                 | VAS_198_Value
 * 20 | Delivery                              | VAS_198_Delivery
 * 21 | Status                                | VAS_198_Status
 * 22 | Product                               | VAS_198_Product
 * 23 | Attribute                             | VAS_198_Attribute
 * 24 | UoM                                   | VAS_198_UoM
 * 25 | Ordered                               | VAS_198_Ordered
 * 26 | Received                              | VAS_198_Received
 * 27 | Pending                               | VAS_198_Pending
 * 28 | Rate                                  | VAS_198_Rate
 * 29 | Amount                                | VAS_198_Amount
 * 30 | Line status                           | VAS_198_LineStatus
 * 31 | Lines                                 | VAS_198_Lines
 * 32 | Qty ordered                           | VAS_198_QtyOrdered
 * 33 | Qty pending                           | VAS_198_QtyPending
 * 34 | No purchase orders for this period.   | VAS_198_NoData
 * 35 | select a PO number to open record     | VAS_198_SelectPOToOpen
 * 36 | Other                                 | VAS_198_Other
 * 37 | Uncategorised                         | VAS_198_Uncategorised
 * 38 | Loading...                            | VAS_198_Loading
 * 39 | Purchase order                        | VAS_198_PurchaseOrder
 * 40 | Category purchase orders              | VAS_198_CategoryPurchaseOrders
 * 41 | % of PO value                         | VAS_198_OfPOValue
 * 42 | Error loading lines.                  | VAS_198_ErrorLoadingLines
 * 43 | No lines available.                   | VAS_198_NoLinesAvailable
 * 44 | lines of                              | VAS_198_LinesOf
 * 45 | PO value                              | VAS_198_POValue
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

    VAS.VAS_198_CategoryWisePOWidget = function () {

        this.frame;
        this.windowNo;

        var $self = this;
        var $root = $('<div class="vas-cpow-root">');
        var $card;
        var $monthSelect;
        var $yearSelect;
        var $donutWrap;
        var $donutSvg;
        var $donutCtr;
        var $busy;
        var $modal;

        var now = new Date();
        var selectedMonth = now.getMonth() + 1;
        var selectedYear = now.getFullYear();

        var categoriesData = [];
        var monthTotalValue = 0;
        var curSymbol = "₹";
        var curIso = "INR";
        var stdPrecision = 2;

        var modalStack = [];
        var currentModalConfig = null;

        var monthNamesFull = [
            "January", "February", "March", "April", "May", "June",
            "July", "August", "September", "October", "November", "December"
        ];
        var monthNamesShort = [
            "Jan", "Feb", "Mar", "Apr", "May", "Jun",
            "Jul", "Aug", "Sep", "Oct", "Nov", "Dec"
        ];

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

        function formatCurrency(val) {
            var num = Number(val || 0);
            var sym = curSymbol ? (curSymbol + " ") : "";
            if (curSymbol === "₹" || curIso === "INR") {
                if (num >= 10000000) {
                    return sym + (num / 10000000).toFixed(2) + " Cr";
                } else if (num >= 100000) {
                    return sym + (num / 100000).toFixed(2) + " L";
                } else if (num >= 1000) {
                    return sym + (num / 1000).toFixed(1) + " k";
                }
                return sym + Math.round(num).toLocaleString(window.navigator.language);
            } else {
                if (num >= 1000000) {
                    return sym + (num / 1000000).toFixed(2) + " M";
                } else if (num >= 1000) {
                    return sym + (num / 1000).toFixed(1) + " k";
                }
                return sym + num.toLocaleString(window.navigator.language, {
                    minimumFractionDigits: 0,
                    maximumFractionDigits: stdPrecision
                });
            }
        }

        function formatNumber(val) {
            var num = Number(val || 0);
            return num.toLocaleString(window.navigator.language);
        }

        function periodLabel() {
            var mName = monthNamesShort[selectedMonth - 1] || "";
            return mName + " " + selectedYear;
        }

        function showBusy(show) {
            if (!$busy || !$busy[0]) { return; }
            $busy.toggleClass('vas-cpow-hidden', !show);
        }

        this.Initalize = function () {
            createWidget();
            loadCategoryData();
        };

        function createWidget() {
            var title = lbl("VAS_198_CategoryWisePO", "Category wise PO");

            $card = $(
                '<section class="vas-cpow-card vas-widget-bg">' +
                '  <div class="vas-cpow-head">' +
                '    <div class="vas-cpow-head-txt">' +
                '      <p class="vas-cpow-title">' + escapeHtml(title) + '</p>' +
                '    </div>' +
                '    <div class="vas-cpow-mfilter">' +
                '      <select class="vas-cpow-msel vas-cpow-m-sel" aria-label="Month"></select>' +
                '      <select class="vas-cpow-msel vas-cpow-y-sel" aria-label="Year"></select>' +
                '    </div>' +
                '  </div>' +
                '  <div class="vas-cpow-donutwrap">' +
                '    <div class="vas-cpow-donut" style="width:100%;max-width:11.5em;aspect-ratio:1;">' +
                '      <svg viewBox="0 0 42 42" width="100%" height="100%" style="transform:rotate(-90deg);" class="vas-cpow-svg"></svg>' +
                '      <div class="vas-cpow-ctr"></div>' +
                '    </div>' +
                '  </div>' +
                '</section>'
            );

            $monthSelect = $card.find('.vas-cpow-m-sel');
            $yearSelect = $card.find('.vas-cpow-y-sel');
            $donutWrap = $card.find('.vas-cpow-donutwrap');
            $donutSvg = $card.find('.vas-cpow-svg');
            $donutCtr = $card.find('.vas-cpow-ctr');

            // Populate Month selector with full month names for arrow-less standard
            for (var m = 1; m <= 12; m++) {
                var mLabel = monthNamesFull[m - 1];
                $monthSelect.append('<option value="' + m + '" ' + (m === selectedMonth ? 'selected' : '') + '>' + escapeHtml(mLabel) + '</option>');
            }

            // Populate Year selector
            var curYear = now.getFullYear();
            for (var y = curYear - 3; y <= curYear + 1; y++) {
                $yearSelect.append('<option value="' + y + '" ' + (y === selectedYear ? 'selected' : '') + '>' + y + '</option>');
            }

            $monthSelect.on('change', function () {
                selectedMonth = Number($(this).val());
                loadCategoryData();
            });

            $yearSelect.on('change', function () {
                selectedYear = Number($(this).val());
                loadCategoryData();
            });

            $root.append($card);

            $busy = $('<div class="vas-cpow-busy vas-cpow-hidden"><div class="vis-busyindicatorinnerwrap"><i class="vis_widgetloader"></i></div></div>');
            $root.append($busy);
        }

        function loadCategoryData() {
            showBusy(true);

            $.ajax({
                url: VIS.Application.contextUrl + 'VAS_198_CategoryWisePOWidget/GetCategoryWisePO',
                type: 'GET',
                data: { month: selectedMonth, year: selectedYear },
                cache: false,
                success: function (res) {
                    var data = parseResponse(res);
                    categoriesData = data.categories || [];
                    monthTotalValue = Number(data.totalValue || 0);
                    curSymbol = data.curSymbol || "₹";
                    curIso = data.curIso || "INR";
                    stdPrecision = data.stdPrecision != null ? data.stdPrecision : 2;
                    renderDonut();
                },
                error: function () {
                    categoriesData = [];
                    monthTotalValue = 0;
                    renderDonut();
                },
                complete: function () {
                    showBusy(false);
                }
            });
        }

        function renderDonut() {
            if (!$donutSvg || !$donutSvg[0]) { return; }

            var totalCaption = lbl("VAS_198_TotalPOValue", "Total PO value");
            var defaultCenterHtml = '<b>' + escapeHtml(formatCurrency(monthTotalValue)) + '</b><span>' + escapeHtml(totalCaption) + '</span>';

            if (!categoriesData || categoriesData.length === 0 || monthTotalValue <= 0) {
                var emptyTrack = '<circle cx="21" cy="21" r="15.9" fill="none" stroke="#EEF3F8" stroke-width="8" />';
                $donutSvg.html(emptyTrack);
                $donutCtr.html(defaultCenterHtml);
                return;
            }

            var trackCircle = '<circle cx="21" cy="21" r="15.9" fill="none" stroke="#EEF3F8" stroke-width="8" />';
            var segmentsHtml = trackCircle;
            var cumulativeOffset = 0;

            for (var i = 0; i < categoriesData.length; i++) {
                var cat = categoriesData[i];
                var pct = Number(cat.Share || 0);
                if (pct <= 0) { continue; }

                var dashArray = (pct > 0.8 ? (pct - 0.8) : pct) + ' ' + (100 - (pct > 0.8 ? (pct - 0.8) : pct));
                var strokeColor = cat.Color || '#A9D2FF';

                segmentsHtml +=
                    '<circle class="vas-cpow-seg" data-idx="' + i + '" cx="21" cy="21" r="15.9" fill="none" ' +
                    'stroke="' + strokeColor + '" stroke-width="8" ' +
                    'stroke-dasharray="' + dashArray + '" ' +
                    'stroke-dashoffset="' + (-cumulativeOffset) + '">' +
                    '<title>' + escapeHtml(cat.CategoryName) + ' — ' + pct + '%</title>' +
                    '</circle>';

                cumulativeOffset += pct;
            }

            $donutSvg.html(segmentsHtml);
            $donutCtr.html(defaultCenterHtml);

            // Hover and click listeners
            $donutSvg.off('mouseover mouseout click');

            $donutSvg.on('mouseover', '.vas-cpow-seg', function () {
                var idx = Number($(this).data('idx'));
                var item = categoriesData[idx];
                if (!item) { return; }

                var hoverHtml = '<b>' + item.Share + '%</b><span>' + escapeHtml(item.CategoryName) + ' · ' + escapeHtml(formatCurrency(item.CategoryValue)) + '</span>';
                $donutCtr.html(hoverHtml);
            });

            $donutSvg.on('mouseout', function () {
                $donutCtr.html(defaultCenterHtml);
            });

            $donutSvg.on('click', '.vas-cpow-seg', function () {
                var idx = Number($(this).data('idx'));
                var item = categoriesData[idx];
                if (!item) { return; }

                openCategoryModal(item);
            });
        }

        /* ============================================================
           MODAL ENGINE & DRILL-DOWNS
           ============================================================ */

        function openModalShell(cfg, isBack) {
            if (!isBack) {
                if (cfg.isChild && currentModalConfig) {
                    modalStack.push(currentModalConfig);
                } else {
                    modalStack = [];
                }
                currentModalConfig = cfg;
            } else {
                currentModalConfig = cfg;
            }

            if ($modal) { $modal.remove(); }

            var hasBack = modalStack.length > 0;
            var backBtnHtml = hasBack
                ? '<button type="button" class="vas-cpow-xbtn vas-cpow-m-back" aria-label="' + escapeHtml(lbl("VAS_198_Back", "Back")) + '">' +
                  '<svg width="20" height="20" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2" stroke-linecap="round" stroke-linejoin="round"><path d="M19 12H5M12 19l-7-7 7-7"/></svg>' +
                  '</button>'
                : '';

            var modalHtml =
                '<div class="vas-cpow-mask open" role="dialog" aria-modal="true">' +
                '  <div class="vas-cpow-modal ' + (cfg.size ? cfg.size : '') + '">' +
                '    <div class="vas-cpow-modal-header">' +
                '      <div style="display:flex;align-items:center;gap:8px;min-width:0;">' +
                '        ' + backBtnHtml +
                '        <div class="vas-cpow-htxt">' +
                '          <h2 class="vas-cpow-m-title" title="' + escapeHtml(cfg.title) + '">' + escapeHtml(cfg.title) + '</h2>' +
                '          <div class="vas-cpow-m-sub" title="' + escapeHtml(cfg.subtitle || '') + '">' + escapeHtml(cfg.subtitle || '') + '</div>' +
                '        </div>' +
                '      </div>' +
                '      <div class="vas-cpow-hact">' +
                '        <button type="button" class="vas-cpow-xbtn vas-cpow-m-close" aria-label="' + escapeHtml(lbl("VAS_198_Close", "Close")) + '">' +
                '          <svg width="20" height="20" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2" stroke-linecap="round"><path d="M18 6 6 18M6 6l12 12"/></svg>' +
                '        </button>' +
                '      </div>' +
                '    </div>' +
                '    <div class="vas-cpow-modal-body ' + (cfg.bodyClass || '') + '">' +
                '      ' + (cfg.bodyHtml || '') +
                '    </div>' +
                '    <div class="vas-cpow-modal-foot">' +
                '      <span class="vas-cpow-foot-note">' + escapeHtml(cfg.footNote || '') + '</span>' +
                '      <button type="button" class="vas-cpow-btn vas-cpow-m-close-btn">' + escapeHtml(lbl("VAS_198_Close", "Close")) + '</button>' +
                '    </div>' +
                '  </div>' +
                '</div>';

            $modal = $(modalHtml);
            $('body').append($modal);

            $modal.find('.vas-cpow-m-close, .vas-cpow-m-close-btn').on('click', closeModal);
            $modal.find('.vas-cpow-m-back').on('click', backModal);

            $modal.on('click', function (e) {
                if (e.target === this) { closeModal(); }
            });

            $(document).off('keydown.vas-cpow');
            $(document).on('keydown.vas-cpow', function (e) {
                if (e.key === 'Escape' || e.keyCode === 27) { closeModal(); }
            });

            if (cfg.afterRender) { cfg.afterRender($modal); }
        }

        function closeModal() {
            $(document).off('keydown.vas-cpow');
            if ($modal) { $modal.remove(); $modal = null; }
            modalStack = [];
            currentModalConfig = null;
        }

        function backModal() {
            var prev = modalStack.pop();
            if (!prev) { closeModal(); return; }
            if (prev.reopen) { prev.reopen(); }
            else { openModalShell(prev, true); }
        }

        function openRecord(orderId) {
            if (!orderId) { return; }
            var param = {
                "Record_ID": orderId,
                "TabIndex": "0"
            };
            $self.widgetFirevalueChanged(param);
        }

        /* ---------- CATEGORY PO DRILL-DOWN MODAL ---------- */
        function openCategoryModal(catItem) {
            var catIds = catItem.CategoryIds ? catItem.CategoryIds.join(',') : String(catItem.CategoryId);
            var subtitle = lbl("VAS_198_CategoryPurchaseOrders", "Category purchase orders") + ' · ' + periodLabel() + ' · ' + catItem.Share + lbl("VAS_198_OfPOValue", "% of PO value");

            var statStripHtml =
                '<div class="vas-cpow-mstats">' +
                '  <div class="vas-cpow-mstat"><div class="l">' + escapeHtml(lbl("VAS_198_CategoryValue", "Category value")) + '</div><div class="v">' + escapeHtml(formatCurrency(catItem.CategoryValue)) + '</div></div>' +
                '  <div class="vas-cpow-mstat"><div class="l">' + escapeHtml(lbl("VAS_198_Share", "Share")) + '</div><div class="v">' + catItem.Share + '%</div></div>' +
                '  <div class="vas-cpow-mstat"><div class="l">' + escapeHtml(lbl("VAS_198_POs", "POs")) + '</div><div class="v">' + formatNumber(catItem.PoCount) + '</div></div>' +
                '  <div class="vas-cpow-mstat"><div class="l">' + escapeHtml(lbl("VAS_198_Vendors", "Vendors")) + '</div><div class="v">' + formatNumber(catItem.VendorCount) + '</div></div>' +
                '</div>';

            var bodyHtml =
                statStripHtml +
                '<div class="vas-cpow-msec">' + escapeHtml(lbl("VAS_198_PurchaseOrdersInCategory", "Purchase orders in this category")) + '</div>' +
                '<div class="vas-cpow-table-container">' +
                '  <div class="vas-cpow-mtbl">' +
                '    <div class="vas-cpow-mrow vas-cpow-mhead">' +
                '      <span class="vas-cpow-cell center vas-cpow-col-icon"></span>' +
                '      <span class="vas-cpow-cell vas-cpow-col-pono">' + escapeHtml(lbl("VAS_198_PONo", "PO No")) + '</span>' +
                '      <span class="vas-cpow-cell vas-cpow-col-podate">' + escapeHtml(lbl("VAS_198_PODate", "PO date")) + '</span>' +
                '      <span class="vas-cpow-cell vas-cpow-col-vendor">' + escapeHtml(lbl("VAS_198_Vendor", "Vendor")) + '</span>' +
                '      <span class="vas-cpow-cell vas-cpow-col-wh">' + escapeHtml(lbl("VAS_198_Warehouse", "Warehouse")) + '</span>' +
                '      <span class="vas-cpow-cell vas-cpow-col-rep">' + escapeHtml(lbl("VAS_198_Representative", "Representative")) + '</span>' +
                '      <span class="vas-cpow-cell right vas-cpow-col-val">' + escapeHtml(lbl("VAS_198_Value", "Value")) + '</span>' +
                '      <span class="vas-cpow-cell vas-cpow-col-deliv">' + escapeHtml(lbl("VAS_198_Delivery", "Delivery")) + '</span>' +
                '      <span class="vas-cpow-cell vas-cpow-col-status">' + escapeHtml(lbl("VAS_198_Status", "Status")) + '</span>' +
                '    </div>' +
                '    <div class="vas-cpow-mbody vas-cpow-po-mbody">' +
                '      <div class="vas-cpow-mrow"><span class="vas-cpow-cell" style="grid-column:1/-1;text-align:center;padding:1em;">' + escapeHtml(lbl("VAS_198_Loading", "Loading...")) + '</span></div>' +
                '    </div>' +
                '  </div>' +
                '  <div class="vas-cpow-mtfoot">' +
                '    <span class="vas-cpow-helper vas-cpow-po-helper"></span>' +
                '    <span class="vas-cpow-pager vas-cpow-po-pager">' +
                '      <button type="button" class="vas-cpow-pbtn vas-cpow-po-prev" disabled aria-label="Previous"><svg viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2.4" stroke-linecap="round"><polyline points="15 18 9 12 15 6"/></svg></button>' +
                '      <span class="vas-cpow-ptxt vas-cpow-po-ptxt">1 of 1</span>' +
                '      <button type="button" class="vas-cpow-pbtn vas-cpow-po-next" disabled aria-label="Next"><svg viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2.4" stroke-linecap="round"><polyline points="9 18 15 12 9 6"/></svg></button>' +
                '    </span>' +
                '  </div>' +
                '</div>';

            var cfg = {
                isChild: false,
                title: catItem.CategoryName,
                subtitle: subtitle,
                bodyHtml: bodyHtml,
                reopen: function () { openCategoryModal(catItem); },
                afterRender: function ($m) {
                    loadCategoryPODrilldownData($m, catItem, catIds);
                }
            };

            openModalShell(cfg, false);
        }

        function loadCategoryPODrilldownData($m, catItem, catIds) {
            var poList = [];
            var poPage = 0;
            var poPageSize = 8;

            var iconLines = '<svg viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2" stroke-linecap="round"><path d="M8 6h13M8 12h13M8 18h13M3 6h.01M3 12h.01M3 18h.01"/></svg>';

            function renderPOTable() {
                var $mbody = $m.find('.vas-cpow-po-mbody');
                var $helper = $m.find('.vas-cpow-po-helper');
                var $ptxt = $m.find('.vas-cpow-po-ptxt');
                var $prev = $m.find('.vas-cpow-po-prev');
                var $next = $m.find('.vas-cpow-po-next');

                if (poList.length === 0) {
                    $mbody.html('<div class="vas-cpow-mrow"><span class="vas-cpow-cell" style="grid-column:1/-1;text-align:center;padding:1.5em;color:#5F7283;">' + escapeHtml(lbl("VAS_198_NoData", "No purchase orders for this period.")) + '</span></div>');
                    $helper.text('0 ' + lbl("VAS_198_Of", "of") + ' 0');
                    $ptxt.text('1 ' + lbl("VAS_198_Of", "of") + ' 1');
                    $prev.prop('disabled', true);
                    $next.prop('disabled', true);
                    return;
                }

                var totalPages = Math.max(1, Math.ceil(poList.length / poPageSize));
                if (poPage >= totalPages) { poPage = totalPages - 1; }
                if (poPage < 0) { poPage = 0; }

                var start = poPage * poPageSize;
                var slice = poList.slice(start, start + poPageSize);

                var rowsHtml = '';
                for (var i = 0; i < slice.length; i++) {
                    var p = slice[i];
                    rowsHtml +=
                        '<div class="vas-cpow-mrow">' +
                        '  <span class="vas-cpow-cell center vas-cpow-col-icon">' +
                        '    <button type="button" class="vas-cpow-iconbtn" data-orderid="' + p.OrderId + '" title="View lines of ' + escapeHtml(p.DocumentNo) + '">' + iconLines + '</button>' +
                        '  </span>' +
                        '  <span class="vas-cpow-cell vas-cpow-col-pono">' +
                        '    <button type="button" class="vas-cpow-lnk" data-orderid="' + p.OrderId + '" title="Open record ' + escapeHtml(p.DocumentNo) + '">' + escapeHtml(p.DocumentNo) + '</button>' +
                        '  </span>' +
                        '  <span class="vas-cpow-cell c-std vas-cpow-col-podate" title="' + escapeHtml(p.DateOrderedFull) + '">' + escapeHtml(p.DateOrderedFull) + '</span>' +
                        '  <span class="vas-cpow-cell c-std vas-cpow-col-vendor" title="' + escapeHtml(p.Vendor) + '">' + escapeHtml(p.Vendor) + '</span>' +
                        '  <span class="vas-cpow-cell c-std vas-cpow-col-wh" title="' + escapeHtml(p.Warehouse) + '">' + escapeHtml(p.Warehouse) + '</span>' +
                        '  <span class="vas-cpow-cell c-std vas-cpow-col-rep" title="' + escapeHtml(p.Representative) + '">' + escapeHtml(p.Representative) + '</span>' +
                        '  <span class="vas-cpow-cell right c-emph vas-cpow-col-val" title="' + escapeHtml(formatCurrency(p.ValueNum)) + '">' + escapeHtml(formatCurrency(p.ValueNum)) + '</span>' +
                        '  <span class="vas-cpow-cell vas-cpow-col-deliv" title="' + escapeHtml(p.DeliveryStatus) + '"><span class="vas-cpow-chip ' + p.DeliveryChip + '">' + escapeHtml(p.DeliveryStatus) + '</span></span>' +
                        '  <span class="vas-cpow-cell vas-cpow-col-status" title="' + escapeHtml(p.StatusLabel) + '"><span class="vas-cpow-chip ' + p.StatusChip + '">' + escapeHtml(p.StatusLabel) + '</span></span>' +
                        '</div>';
                }

                $mbody.html(rowsHtml);

                var showingTxt = lbl("VAS_198_Showing", "Showing") + ' ' + (start + 1) + '–' + (start + slice.length) + ' ' +
                                 lbl("VAS_198_Of", "of") + ' ' + poList.length + ' · ' + catItem.CategoryName;
                $helper.text(showingTxt);
                $ptxt.text((poPage + 1) + ' ' + lbl("VAS_198_Of", "of") + ' ' + totalPages);
                $prev.prop('disabled', poPage === 0);
                $next.prop('disabled', poPage >= totalPages - 1);
            }

            $m.find('.vas-cpow-po-prev').on('click', function () {
                if (poPage > 0) { poPage--; renderPOTable(); }
            });
            $m.find('.vas-cpow-po-next').on('click', function () {
                var totalPages = Math.ceil(poList.length / poPageSize);
                if (poPage < totalPages - 1) { poPage++; renderPOTable(); }
            });

            $m.on('click', '.vas-cpow-lnk', function () {
                var orderId = Number($(this).data('orderid'));
                openRecord(orderId);
            });

            $m.on('click', '.vas-cpow-iconbtn', function () {
                var orderId = Number($(this).data('orderid'));
                openPOLinesModal(orderId);
            });

            // Fetch drilldown list from server
            $.ajax({
                url: VIS.Application.contextUrl + 'VAS_198_CategoryWisePOWidget/GetCategoryPODrillDown',
                type: 'GET',
                data: { month: selectedMonth, year: selectedYear, categoryIds: catIds },
                cache: false,
                success: function (res) {
                    var data = parseResponse(res);
                    poList = data.records || [];
                    poPage = 0;
                    renderPOTable();
                },
                error: function () {
                    poList = [];
                    poPage = 0;
                    renderPOTable();
                }
            });
        }

        /* ---------- PO LINES MODAL (CHILD MODAL) ---------- */
        function openPOLinesModal(orderId) {
            var cfg = {
                isChild: true,
                size: 'md',
                title: lbl("VAS_198_Lines", "Lines") + ' · ...',
                subtitle: '',
                bodyHtml: '<div class="vas-cpow-polines-loader" style="text-align:center;padding:2em;color:#5F7283;">' + escapeHtml(lbl("VAS_198_Loading", "Loading...")) + '</div>',
                afterRender: function ($m) {
                    loadPOLinesData($m, orderId);
                }
            };

            openModalShell(cfg, false);
        }

        function loadPOLinesData($m, orderId) {
            $.ajax({
                url: VIS.Application.contextUrl + 'VAS_198_CategoryWisePOWidget/GetPOLines',
                type: 'GET',
                data: { orderId: orderId },
                cache: false,
                success: function (res) {
                    var data = parseResponse(res);
                    var header = data.header || {};
                    var lines = data.lines || [];

                    renderPOLinesModalContent($m, header, lines);
                },
                error: function () {
                    $m.find('.vas-cpow-modal-body').html('<div style="text-align:center;padding:2em;color:#A33F3F;">' + escapeHtml(lbl("VAS_198_ErrorLoadingLines", "Error loading lines.")) + '</div>');
                }
            });
        }

        function renderPOLinesModalContent($m, header, lines) {
            $m.find('.vas-cpow-m-title').text(lbl("VAS_198_Lines", "Lines") + ' · ' + (header.DocumentNo || ''));
            $m.find('.vas-cpow-m-sub').text((header.Vendor || '') + ' · ' + (header.DateOrdered || '') + ' · ' + (header.DeliveryStatus || ''));

            var polinkHtml =
                '<div class="vas-cpow-polink">' +
                '  ' + escapeHtml(lbl("VAS_198_PurchaseOrder", "Purchase order")) + ' ' +
                '  <button type="button" class="vas-cpow-lnk vas-cpow-open-po" data-orderid="' + header.OrderId + '">' + escapeHtml(header.DocumentNo) + '</button>' +
                '  · ' + escapeHtml(header.DateOrdered || '') + ' · ' + escapeHtml(header.DocStatusLabel || header.DocStatus || '') +
                '</div>';

            var statStripHtml =
                '<div class="vas-cpow-mstats">' +
                '  <div class="vas-cpow-mstat"><div class="l">' + escapeHtml(lbl("VAS_198_Lines", "Lines")) + '</div><div class="v">' + lines.length + '</div></div>' +
                '  <div class="vas-cpow-mstat"><div class="l">' + escapeHtml(lbl("VAS_198_POValue", "PO value")) + '</div><div class="v">' + escapeHtml(formatCurrency(header.TotalValue)) + '</div></div>' +
                '  <div class="vas-cpow-mstat"><div class="l">' + escapeHtml(lbl("VAS_198_QtyOrdered", "Qty ordered")) + '</div><div class="v">' + formatNumber(header.TotalQtyOrdered) + '</div></div>' +
                '  <div class="vas-cpow-mstat"><div class="l">' + escapeHtml(lbl("VAS_198_QtyPending", "Qty pending")) + '</div><div class="v">' + formatNumber(header.TotalQtyPending) + '</div></div>' +
                '</div>';

            var bodyHtml =
                polinkHtml +
                statStripHtml +
                '<div class="vas-cpow-msec">' + escapeHtml(lbl("VAS_198_PurchaseOrderLines", "Purchase order lines")) + '</div>' +
                '<div class="vas-cpow-table-container">' +
                '  <div class="vas-cpow-mtbl">' +
                '    <div class="vas-cpow-mrow vas-cpow-lines-mhead">' +
                '      <span class="vas-cpow-cell right vas-cpow-lcol-seq">#</span>' +
                '      <span class="vas-cpow-cell vas-cpow-lcol-prod">' + escapeHtml(lbl("VAS_198_Product", "Product")) + '</span>' +
                '      <span class="vas-cpow-cell vas-cpow-lcol-attr">' + escapeHtml(lbl("VAS_198_Attribute", "Attribute")) + '</span>' +
                '      <span class="vas-cpow-cell vas-cpow-lcol-uom">' + escapeHtml(lbl("VAS_198_UoM", "UoM")) + '</span>' +
                '      <span class="vas-cpow-cell right vas-cpow-lcol-ord">' + escapeHtml(lbl("VAS_198_Ordered", "Ordered")) + '</span>' +
                '      <span class="vas-cpow-cell right vas-cpow-lcol-recd">' + escapeHtml(lbl("VAS_198_Received", "Received")) + '</span>' +
                '      <span class="vas-cpow-cell right vas-cpow-lcol-pend">' + escapeHtml(lbl("VAS_198_Pending", "Pending")) + '</span>' +
                '      <span class="vas-cpow-cell right vas-cpow-lcol-rate">' + escapeHtml(lbl("VAS_198_Rate", "Rate")) + '</span>' +
                '      <span class="vas-cpow-cell right vas-cpow-lcol-amt">' + escapeHtml(lbl("VAS_198_Amount", "Amount")) + '</span>' +
                '      <span class="vas-cpow-cell vas-cpow-lcol-st">' + escapeHtml(lbl("VAS_198_LineStatus", "Line status")) + '</span>' +
                '    </div>' +
                '    <div class="vas-cpow-mbody vas-cpow-lines-mbody"></div>' +
                '  </div>' +
                '  <div class="vas-cpow-mtfoot">' +
                '    <span class="vas-cpow-helper vas-cpow-lines-helper"></span>' +
                '    <span class="vas-cpow-pager vas-cpow-lines-pager">' +
                '      <button type="button" class="vas-cpow-pbtn vas-cpow-lines-prev" disabled aria-label="Previous"><svg viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2.4" stroke-linecap="round"><polyline points="15 18 9 12 15 6"/></svg></button>' +
                '      <span class="vas-cpow-ptxt vas-cpow-lines-ptxt">1 of 1</span>' +
                '      <button type="button" class="vas-cpow-pbtn vas-cpow-lines-next" disabled aria-label="Next"><svg viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2.4" stroke-linecap="round"><polyline points="9 18 15 12 9 6"/></svg></button>' +
                '    </span>' +
                '  </div>' +
                '</div>';

            $m.find('.vas-cpow-modal-body').html(bodyHtml);
            $m.find('.vas-cpow-foot-note').text((header.DocumentNo || '') + ' · ' + (header.Vendor || ''));

            var linesPage = 0;
            var linesPageSize = 8;

            function renderLinesTable() {
                var $linesBody = $m.find('.vas-cpow-lines-mbody');
                var $linesHelper = $m.find('.vas-cpow-lines-helper');
                var $linesPtxt = $m.find('.vas-cpow-lines-ptxt');
                var $lPrev = $m.find('.vas-cpow-lines-prev');
                var $lNext = $m.find('.vas-cpow-lines-next');

                if (lines.length === 0) {
                    $linesBody.html('<div class="vas-cpow-mrow"><span class="vas-cpow-cell" style="grid-column:1/-1;text-align:center;padding:1.5em;color:#5F7283;">' + escapeHtml(lbl("VAS_198_NoLinesAvailable", "No lines available.")) + '</span></div>');
                    $linesHelper.text('0 ' + lbl("VAS_198_Of", "of") + ' 0');
                    $linesPtxt.text('1 ' + lbl("VAS_198_Of", "of") + ' 1');
                    $lPrev.prop('disabled', true);
                    $lNext.prop('disabled', true);
                    return;
                }

                var totalPages = Math.max(1, Math.ceil(lines.length / linesPageSize));
                if (linesPage >= totalPages) { linesPage = totalPages - 1; }
                if (linesPage < 0) { linesPage = 0; }

                var start = linesPage * linesPageSize;
                var slice = lines.slice(start, start + linesPageSize);

                var rowsHtml = '';
                for (var j = 0; j < slice.length; j++) {
                    var l = slice[j];
                    rowsHtml +=
                        '<div class="vas-cpow-mrow vas-cpow-lines-mrow">' +
                        '  <span class="vas-cpow-cell right c-std vas-cpow-lcol-seq">' + (start + j + 1) + '</span>' +
                        '  <span class="vas-cpow-cell c-prim vas-cpow-lcol-prod" title="' + escapeHtml(l.ProductName) + '">' + escapeHtml(l.ProductName) + '</span>' +
                        '  <span class="vas-cpow-cell c-std vas-cpow-lcol-attr" title="' + escapeHtml(l.Attribute || '—') + '">' + escapeHtml(l.Attribute || '—') + '</span>' +
                        '  <span class="vas-cpow-cell c-std vas-cpow-lcol-uom" title="' + escapeHtml(l.Uom || '—') + '">' + escapeHtml(l.Uom || '—') + '</span>' +
                        '  <span class="vas-cpow-cell right c-std vas-cpow-lcol-ord">' + formatNumber(l.QtyOrdered) + '</span>' +
                        '  <span class="vas-cpow-cell right c-std vas-cpow-lcol-recd">' + formatNumber(l.QtyDelivered) + '</span>' +
                        '  <span class="vas-cpow-cell right c-prim vas-cpow-lcol-pend">' + formatNumber(l.QtyPending) + '</span>' +
                        '  <span class="vas-cpow-cell right c-std vas-cpow-lcol-rate">' + escapeHtml(formatCurrency(l.Rate)) + '</span>' +
                        '  <span class="vas-cpow-cell right c-emph vas-cpow-lcol-amt">' + escapeHtml(formatCurrency(l.Amount)) + '</span>' +
                        '  <span class="vas-cpow-cell vas-cpow-lcol-st" title="' + escapeHtml(l.LineStatus) + '"><span class="vas-cpow-chip ' + l.LineStatusChip + '">' + escapeHtml(l.LineStatus) + '</span></span>' +
                        '</div>';
                }

                $linesBody.html(rowsHtml);

                var showingTxt = lbl("VAS_198_Showing", "Showing") + ' ' + (start + 1) + '–' + (start + slice.length) + ' ' +
                                 lbl("VAS_198_Of", "of") + ' ' + lines.length + ' · ' + lbl("VAS_198_LinesOf", "lines of") + ' ' + header.DocumentNo;
                $linesHelper.text(showingTxt);
                $linesPtxt.text((linesPage + 1) + ' ' + lbl("VAS_198_Of", "of") + ' ' + totalPages);
                $lPrev.prop('disabled', linesPage === 0);
                $lNext.prop('disabled', linesPage >= totalPages - 1);
            }

            renderLinesTable();

            $m.find('.vas-cpow-lines-prev').on('click', function () {
                if (linesPage > 0) { linesPage--; renderLinesTable(); }
            });
            $m.find('.vas-cpow-lines-next').on('click', function () {
                var totalPages = Math.ceil(lines.length / linesPageSize);
                if (linesPage < totalPages - 1) { linesPage++; renderLinesTable(); }
            });

            $m.find('.vas-cpow-open-po').on('click', function () {
                var oId = Number($(this).data('orderid'));
                openRecord(oId);
            });
        }

        this.refreshWidget = function () {
            loadCategoryData();
        };

        this.getRoot = function () { return $root; };

        this.disposeComponent = function () {
            $(document).off('keydown.vas-cpow');
            if ($modal) { $modal.remove(); $modal = null; }
            $root.remove();
        };
    };

    VAS.VAS_198_CategoryWisePOWidget.prototype.widgetFirevalueChanged = function (value) {
        if (this.listener) { this.listener.widgetFirevalueChanged(value); }
    };

    VAS.VAS_198_CategoryWisePOWidget.prototype.addChangeListener = function (listener) {
        this.listener = listener;
    };

    VAS.VAS_198_CategoryWisePOWidget.prototype.init = function (windowNo, frame) {
        this.frame = frame;
        this.AD_UserHomeWidgetID = frame.widgetInfo.AD_UserHomeWidgetID;
        this.windowNo = windowNo;
        this.Initalize();
        this.frame.getContentGrid().append(this.getRoot());
        ensureDashInlineSizeVar(this.getRoot());
    };

    VAS.VAS_198_CategoryWisePOWidget.prototype.widgetSizeChange = function (height, width) { };

    VAS.VAS_198_CategoryWisePOWidget.prototype.refreshWidget = function () {
        this.refreshWidget();
    };

    VAS.VAS_198_CategoryWisePOWidget.prototype.dispose = function () {
        this.disposeComponent();
        if (this.frame) { this.frame.dispose(); }
        this.frame = null;
    };

})(VAS, jQuery);
