/**
 * VAS_183_MaterialConsumptionWidget
 * 3x2 Summary & Breakdown Widget for Inventory Use dashboard.
 * Displays warehouse/locator-wise consumption with progress bars, compact INR values,
 * and opens the Locator Detail Modal for item-level breakdown.
 *
 * Summary Message Table
 *  # | Current Text                           | Message Key
 * ---+----------------------------------------+-----------------------------------
 *  1 | Consumption                            | VAS_183_Consumption
 *  2 | Warehouse / locator wise              | VAS_183_WarehouseLocatorWise
 *  3 | Consumption by item                    | VAS_183_ConsumptionByItem
 *  4 | Distinct items                         | VAS_183_DistinctItems
 *  5 | No consumption records                 | VAS_183_NoConsumptionRecords
 *  6 | Close                                  | VAS_183_Close
 *  7 | Month                                  | VAS_183_Month
 *  8 | Consumed Qty                           | VAS_183_ConsumedQty
 *  9 | Consumption Value                      | VAS_183_ConsumptionValue
 * 10 | Item                                   | VAS_183_Item
 * 11 | Consumed                               | VAS_183_Consumed
 * 12 | UoM                                    | VAS_183_UoM
 * 13 | Attributes                             | VAS_183_Attributes
 * 14 | Each                                   | VAS_183_Each
 * 15 | Loading...                             | VAS_183_Loading
 * 16 | No items found                         | VAS_183_NoItemsFound
 * 17 | of                                     | VAS_183_Of
 * 18 | Page                                   | VAS_183_Page
 * 19 | lines                                  | VAS_183_Lines
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

    VAS.VAS_183_MaterialConsumptionWidget = function () {

        this.frame;
        this.windowNo;

        var $self = this;
        var $root = $('<div class="vas-mcw-root">');
        var $card;
        var $body;
        var $footHelper;
        var $pagerText;
        var $prevBtn;
        var $nextBtn;
        var $whBtn;
        var $whLbl;
        var $whMenu;
        var $monthBtn;
        var $monthLbl;
        var $monthMenu;
        var monthOptions = [];
        var $busy;
        var $modal;

        var warehouses = [];
        var selectedWarehouse = null;
        var selectedMonth = DateTimeNowMonth();
        var selectedYear = DateTimeNowYear();

        var locatorsData = [];
        var pageNo = 1;
        var pageSize = 4;
        var totalPages = 1;

        function DateTimeNowMonth() { return new Date().getMonth() + 1; }
        function DateTimeNowYear() { return new Date().getFullYear(); }

        function label(key, fallback) {
            var translated = VIS.Msg.getMsg(key);
            return (translated && translated.charAt(0) !== '[') ? translated : fallback;
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
            var n = Number(value || 0);
            return n.toLocaleString(window.navigator.language);
        }

// ===== NEW CODE START — currency format (agent A05, 2026-08-19) =====
        var currencyInfo = { iso: "INR", symbol: "₹" };

        function formatCurrency(value) {
            var val = Number(value || 0);
            var absVal = Math.abs(val);
            var sign = val < 0 ? "-" : "";
            var symbol = currencyInfo.symbol || currencyInfo.iso || "₹";
            var iso = (currencyInfo.iso || "INR").toUpperCase();
            var indianIsos = ["INR", "PKR", "BDT", "NPR", "BTN", "LKR"];

            function trimFixed(num, decimals) {
                var str = num.toFixed(decimals);
                return str.replace(/\.?0+$/, "");
            }

            var compact = "";
            var exact = "";

            if (indianIsos.indexOf(iso) !== -1) {
                exact = sign + symbol + val.toLocaleString('en-IN', { minimumFractionDigits: 2, maximumFractionDigits: 2 });
                if (absVal >= 10000000) {
                    compact = sign + symbol + trimFixed(absVal / 10000000, 2) + 'Cr';
                } else if (absVal >= 100000) {
                    compact = sign + symbol + trimFixed(absVal / 100000, 2) + 'L';
                } else if (absVal >= 1000) {
                    compact = sign + symbol + trimFixed(absVal / 1000, 1) + 'K';
                } else {
                    compact = sign + symbol + absVal.toLocaleString('en-IN');
                }
            } else {
                exact = sign + symbol + val.toLocaleString(window.navigator.language, { minimumFractionDigits: 2, maximumFractionDigits: 2 });
                if (absVal >= 1000000000) {
                    compact = sign + symbol + trimFixed(absVal / 1000000000, 2) + 'B';
                } else if (absVal >= 1000000) {
                    compact = sign + symbol + trimFixed(absVal / 1000000, 2) + 'M';
                } else if (absVal >= 1000) {
                    compact = sign + symbol + trimFixed(absVal / 1000, 1) + 'K';
                } else {
                    compact = sign + symbol + absVal.toLocaleString(window.navigator.language);
                }
            }

            return { compact: compact, exact: exact };
        }

        function formatINR(value) {
            return formatCurrency(value).compact;
        }
// ===== NEW CODE END — currency format =====
// ----- OLD CODE (kept for rollback, do not delete) -----
/*
        function formatINR(value) {
            var val = Number(value || 0);
            if (val >= 100000) {
                return '₹' + (val / 100000).toFixed(2) + 'L';
            } else if (val >= 1000) {
                return '₹' + (val / 1000).toFixed(1) + 'K';
            }
            return '₹' + val.toLocaleString(window.navigator.language);
        }
*/
// ----- END OLD CODE -----

        function formatMonthLabel(m, y) {
            var monthNames = ["Jan", "Feb", "Mar", "Apr", "May", "Jun", "Jul", "Aug", "Sep", "Oct", "Nov", "Dec"];
            var name = monthNames[Math.max(0, Math.min(11, m - 1))];
            return name + ' ' + y;
        }

        function showBusy(show) {
            if (!$busy || !$busy[0]) { return; }
            $busy.toggleClass('vas-mcw-hidden', !show);
        }

        this.Initalize = function () {
            createWidget();
            setupResizeObserver();
            loadWarehouses();
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

        function loadWarehouses() {
            showBusy(true);

            $.ajax({
                url: VIS.Application.contextUrl + 'VAS_183_MaterialConsumptionWidget/GetWarehouses',
                type: 'GET',
                cache: false,
                success: function (res) {
                    var data = parseResponse(res);
                    warehouses = data.warehouses || [];
                    populateWarehouseOptions();
                    loadLocatorSummary();
                },
                error: function () { loadLocatorSummary(); }
            });
        }

        /* ---- Header filter dropdowns ----------------------------------------------------------
           Both menus are appended to <body> rather than next to their button. .vas-mcw-card sets
           overflow:hidden (widgets must have no inner scrollbars), which would clip a menu
           positioned inside the card. Body-appending + fixed positioning from the button's
           bounding rect is the pattern already used by VAS_164_StockSearchWidget for exactly this
           reason, and it keeps the menu looking the way the design specifies. */

        function buildFilterMenus() {
            $whMenu = $('<div class="vas-mcw-menu vas-mcw-wh-menu" role="menu">');
            $monthMenu = $('<div class="vas-mcw-menu vas-mcw-month-menu" role="menu">');
            $('body').append($whMenu).append($monthMenu);

            buildMonthOptions();
            renderWarehouseMenu();
            renderMonthMenu();

            $whBtn.on('click', function (e) {
                e.stopPropagation();
                closeMenu($monthMenu, $monthBtn);
                toggleMenu($whMenu, $whBtn);
            });

            $monthBtn.on('click', function (e) {
                e.stopPropagation();
                closeMenu($whMenu, $whBtn);
                toggleMenu($monthMenu, $monthBtn);
            });

            $whMenu.on('click', 'button', function (e) {
                e.stopPropagation();
                var whId = Number($(this).data('whid') || 0);
                for (var i = 0; i < warehouses.length; i++) {
                    if (Number(warehouses[i].warehouseId) === whId) { selectedWarehouse = warehouses[i]; break; }
                }
                closeMenu($whMenu, $whBtn);
                renderWarehouseMenu();
                pageNo = 1;
                loadLocatorSummary();
            });

            $monthMenu.on('click', 'button', function (e) {
                e.stopPropagation();
                var idx = Number($(this).data('midx') || 0);
                var opt = monthOptions[idx];
                if (!opt) { return; }
                selectedMonth = opt.month;
                selectedYear = opt.year;
                if ($monthLbl) { $monthLbl.text(formatMonthLabel(selectedMonth, selectedYear)); }
                closeMenu($monthMenu, $monthBtn);
                renderMonthMenu();
                pageNo = 1;
                loadLocatorSummary();
            });

            // One menu open at a time; clicking anywhere outside closes both.
            $self._onDocClickMcw = function () {
                closeMenu($whMenu, $whBtn);
                closeMenu($monthMenu, $monthBtn);
            };
            $self._onReflowMcw = function () {
                if ($whMenu.hasClass('vas-mcw-menu-open')) { positionMenu($whMenu, $whBtn); }
                if ($monthMenu.hasClass('vas-mcw-menu-open')) { positionMenu($monthMenu, $monthBtn); }
            };
            document.addEventListener('mousedown', $self._onDocClickMcw, true);
            window.addEventListener('resize', $self._onReflowMcw, true);
            window.addEventListener('scroll', $self._onReflowMcw, true);
        }

        function positionMenu($menu, $btn) {
            if (!$btn || !$btn[0]) { return; }
            var rect = $btn[0].getBoundingClientRect();
            // Right-aligned to the button, as in the source design.
            $menu.css({
                top: Math.round(rect.bottom + 4) + 'px',
                left: Math.round(rect.right - $menu.outerWidth()) + 'px',
                // Inherit the card's fluid font size so menu text scales with the widget.
                'font-size': $card.length ? window.getComputedStyle($card[0]).fontSize : ''
            });
        }

        function toggleMenu($menu, $btn) {
            if ($menu.hasClass('vas-mcw-menu-open')) { closeMenu($menu, $btn); return; }
            $menu.addClass('vas-mcw-menu-open');
            positionMenu($menu, $btn);
            $btn.attr('aria-expanded', 'true');
        }

        function closeMenu($menu, $btn) {
            if (!$menu) { return; }
            $menu.removeClass('vas-mcw-menu-open');
            if ($btn) { $btn.attr('aria-expanded', 'false'); }
        }

        /* Last 12 months, newest first - the only period control this widget has. */
        function buildMonthOptions() {
            monthOptions = [];
            var now = new Date();
            for (var i = 0; i < 12; i++) {
                var d = new Date(now.getFullYear(), now.getMonth() - i, 1);
                monthOptions.push({ month: d.getMonth() + 1, year: d.getFullYear() });
            }
        }

        function renderMonthMenu() {
            if (!$monthMenu) { return; }
            var html = '';
            for (var i = 0; i < monthOptions.length; i++) {
                var opt = monthOptions[i];
                var isOn = (opt.month === selectedMonth && opt.year === selectedYear);
                html += '<button type="button" role="menuitem" data-midx="' + i + '"' +
                    (isOn ? ' class="vas-mcw-menu-on"' : '') + '>' +
                    escapeHtml(formatMonthLabel(opt.month, opt.year)) + '</button>';
            }
            $monthMenu.html(html);
        }

        /* Fills the warehouse menu from whatever GetWarehouses returned and selects the first entry.
           Called after the fetch resolves, because the menu exists from createWidget() but the list
           does not. */
        function populateWarehouseOptions() {
            selectedWarehouse = warehouses.length > 0 ? warehouses[0] : null;
            renderWarehouseMenu();
        }

        function renderWarehouseMenu() {
            if ($whLbl) {
                $whLbl.text(selectedWarehouse
                    ? (selectedWarehouse.shortName || selectedWarehouse.name || '')
                    : label("VAS_NoWarehouse", "No warehouse"));
            }
            if (!$whMenu) { return; }

            var html = '';
            for (var i = 0; i < warehouses.length; i++) {
                var wh = warehouses[i];
                var isOn = selectedWarehouse && Number(wh.warehouseId) === Number(selectedWarehouse.warehouseId);
                html += '<button type="button" role="menuitem" data-whid="' + Number(wh.warehouseId) + '"' +
                    (isOn ? ' class="vas-mcw-menu-on"' : '') + '>' +
                    escapeHtml(wh.shortName || wh.name || ('#' + wh.warehouseId)) + '</button>';
            }
            $whMenu.html(html);
        }

        function loadLocatorSummary() {
            showBusy(true);
            var whId = selectedWarehouse ? selectedWarehouse.warehouseId : 0;

            $.ajax({
                url: VIS.Application.contextUrl + 'VAS_183_MaterialConsumptionWidget/GetLocatorSummary',
                type: 'GET',
                data: { warehouseId: whId, month: selectedMonth, year: selectedYear },
                cache: false,
                // ===== NEW CODE START — currency format (agent A05, 2026-08-19) =====
                success: function (res) {
                    var data = parseResponse(res);
                    if (data.currency) { currencyInfo = data.currency; }
                    locatorsData = data.locators || [];
                    pageNo = 1;
                    renderLocatorList();
                },
                // ===== NEW CODE END — currency format =====
                // ----- OLD CODE (kept for rollback, do not delete) -----
                /*
                success: function (res) {
                    var data = parseResponse(res);
                    locatorsData = data.locators || [];
                    pageNo = 1;
                    renderLocatorList();
                },
                */
                // ----- END OLD CODE -----

                error: function () {
                    locatorsData = [];
                    renderLocatorList();
                },
                complete: function () { showBusy(false); }
            });
        }

        function renderLocatorList() {
            if (!$body) { return; }

            totalPages = Math.max(1, Math.ceil(locatorsData.length / pageSize));
            if (pageNo > totalPages) { pageNo = totalPages; }

            if (locatorsData.length === 0) {
                $body.html('<div class="vas-mcw-empty">' + escapeHtml(label("VAS_183_NoConsumptionRecords", "No consumption records")) + '</div>');
                if ($footHelper) { $footHelper.text(formatMonthLabel(selectedMonth, selectedYear) + ' - 0 locators'); }
                if ($pagerText) { $pagerText.text('1 of 1'); }
                if ($prevBtn) { $prevBtn.prop('disabled', true); }
                if ($nextBtn) { $nextBtn.prop('disabled', true); }
                return;
            }

            var maxQty = 1;
            for (var i = 0; i < locatorsData.length; i++) {
                if (locatorsData[i].totalQty > maxQty) { maxQty = locatorsData[i].totalQty; }
            }

            var startIndex = (pageNo - 1) * pageSize;
            var endIndex = Math.min(locatorsData.length, startIndex + pageSize);
            var rowsHtml = '';

            for (var j = startIndex; j < endIndex; j++) {
                var loc = locatorsData[j];
                var pct = Math.max(6, Math.round((loc.totalQty / maxQty) * 100));

                // ===== NEW CODE START — currency format (agent A05, 2026-08-19) =====
                var formattedVal = formatCurrency(loc.totalValue);
                rowsHtml +=
                    '<button type="button" class="vas-mcw-row" data-locid="' + loc.locatorId + '" data-code="' + escapeHtml(loc.locatorCode) + '" data-name="' + escapeHtml(loc.locatorName) + '">' +
                    '<div class="vas-mcw-row-left">' +
                    '<div class="vas-mcw-row-label" title="' + escapeHtml(loc.locatorCode + ' - ' + loc.locatorName) + '">' + escapeHtml(loc.locatorCode + ' - ' + loc.locatorName) + '</div>' +
                    '<div class="vas-mcw-bar-track"><div class="vas-mcw-bar-fill" style="width:' + pct + '%;"></div></div>' +
                    '</div>' +
                    '<div class="vas-mcw-row-right">' +
                    '<div class="vas-mcw-row-qty">' + escapeHtml(formatQty(loc.totalQty)) + '</div>' +
                    '<div class="vas-mcw-row-val" title="' + escapeHtml(formattedVal.exact) + '">' + escapeHtml(formattedVal.compact) + '</div>' +
                    '</div>' +
                    '</button>';
                // ===== NEW CODE END — currency format =====
                // ----- OLD CODE (kept for rollback, do not delete) -----
                /*
                rowsHtml +=
                    '<button type="button" class="vas-mcw-row" data-locid="' + loc.locatorId + '" data-code="' + escapeHtml(loc.locatorCode) + '" data-name="' + escapeHtml(loc.locatorName) + '">' +
                    '<div class="vas-mcw-row-left">' +
                    '<div class="vas-mcw-row-label" title="' + escapeHtml(loc.locatorCode + ' - ' + loc.locatorName) + '">' + escapeHtml(loc.locatorCode + ' - ' + loc.locatorName) + '</div>' +
                    '<div class="vas-mcw-bar-track"><div class="vas-mcw-bar-fill" style="width:' + pct + '%;"></div></div>' +
                    '</div>' +
                    '<div class="vas-mcw-row-right">' +
                    '<div class="vas-mcw-row-qty">' + escapeHtml(formatQty(loc.totalQty)) + '</div>' +
                    '<div class="vas-mcw-row-val">' + escapeHtml(formatINR(loc.totalValue)) + '</div>' +
                    '</div>' +
                    '</button>';
                */
                // ----- END OLD CODE -----
            }

            $body.html(rowsHtml);

            if ($footHelper) {
                $footHelper.text(formatMonthLabel(selectedMonth, selectedYear) + ' - ' + locatorsData.length + ' locators');
            }
            if ($pagerText) {
                $pagerText.text(pageNo + ' of ' + totalPages);
            }
            if ($prevBtn) { $prevBtn.prop('disabled', pageNo <= 1); }
            if ($nextBtn) { $nextBtn.prop('disabled', pageNo >= totalPages); }
        }

        function openLocatorDetailModal(locatorId, locatorCode, locatorName) {
            var whName = selectedWarehouse ? selectedWarehouse.fullName : "Warehouse";
            var whId = selectedWarehouse ? selectedWarehouse.warehouseId : 0;
            var monthLabel = formatMonthLabel(selectedMonth, selectedYear);

            $(document).off("keydown.vas-mcw-modal"); if ($modal) { $modal.remove(); }

            $modal = $(
                '<div class="vas-mcw-modal-overlay" role="dialog" aria-modal="true">' +
                '<div class="vas-mcw-modal-card">' +
                '<div class="vas-mcw-modal-head">' +
                '<div class="vas-mcw-modal-title-wrap">' +
                '<h3 class="vas-mcw-modal-title" title="' + escapeHtml(whName + ' - ' + locatorCode + ' ' + locatorName) + '">' + escapeHtml(whName + ' - ' + locatorCode + ' ' + locatorName) + '</h3>' +
                '<span class="vas-mcw-modal-badge">' + escapeHtml(monthLabel) + '</span>' +
                '</div>' +
                '<button type="button" class="vas-mcw-modal-close" aria-label="' + escapeHtml(label("VAS_183_Close", "Close")) + '">' +
                '<svg viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2"><line x1="18" y1="6" x2="6" y2="18"/><line x1="6" y1="6" x2="18" y2="18"/></svg>' +
                '</button>' +
                '</div>' +
                '<div class="vas-mcw-modal-body">' +
                '<div class="vas-mcw-summary-grid">' +
                '<div class="vas-mcw-summary-field"><div class="vas-mcw-field-lbl">' + escapeHtml(label("VAS_183_Month", "Month")) + '</div><div class="vas-mcw-field-val">' + escapeHtml(monthLabel) + '</div></div>' +
                '<div class="vas-mcw-summary-field"><div class="vas-mcw-field-lbl">' + escapeHtml(label("VAS_183_ConsumedQty", "Consumed Qty")) + '</div><div class="vas-mcw-field-val vas-mcw-m-qty">—</div></div>' +
                '<div class="vas-mcw-summary-field"><div class="vas-mcw-field-lbl">' + escapeHtml(label("VAS_183_ConsumptionValue", "Consumption Value")) + '</div><div class="vas-mcw-field-val vas-mcw-m-val">—</div></div>' +
                '<div class="vas-mcw-summary-field"><div class="vas-mcw-field-lbl">' + escapeHtml(label("VAS_183_DistinctItems", "Distinct Items")) + '</div><div class="vas-mcw-field-val vas-mcw-m-items">—</div></div>' +
                '</div>' +
                '<div class="vas-mcw-table-head-title">' + escapeHtml(label("VAS_183_ConsumptionByItem", "Consumption by item")) + '</div>' +
                '<table class="vas-mcw-mini-table">' +
                '<thead><tr>' +
                '<th>' + escapeHtml(label("VAS_183_Item", "Item")) + '</th>' +
                '<th>' + escapeHtml(label("VAS_183_Consumed", "Consumed")) + '</th>' +
                '<th>' + escapeHtml(label("VAS_183_UoM", "UoM")) + '</th>' +
                '<th>' + escapeHtml(label("VAS_183_Attributes", "Attributes")) + '</th>' +
                '</tr></thead>' +
                '<tbody class="vas-mcw-m-tbody"><tr><td colspan="4" class="vas-mcw-m-msgcell">' + escapeHtml(label("VAS_183_Loading", "Loading...")) + '</td></tr></tbody>' +
                '</table>' +
                '<div class="vas-mcw-foot vas-mcw-modal-foot">' +
                '<div class="vas-mcw-foot-helper vas-mcw-modal-helper">' + escapeHtml('0 ' + label("VAS_183_Of", "of") + ' 0 ' + label("VAS_183_Lines", "lines")) + '</div>' +
                '<div class="vas-mcw-pager">' +
                '<button type="button" class="vas-mcw-pager-btn vas-mcw-m-prev" disabled>&lsaquo;</button>' +
                '<span class="vas-mcw-pager-txt vas-mcw-m-pager-txt">' + escapeHtml(label("VAS_183_Page", "Page") + ' 1 ' + label("VAS_183_Of", "of") + ' 1') + '</span>' +
                '<button type="button" class="vas-mcw-pager-btn vas-mcw-m-next" disabled>&rsaquo;</button>' +
                '</div>' +
                '</div>' +
                '</div>' +
                '</div>' +
                '</div>'
            );

            var modalItemData = [];
            var modalPageNo = 1;
            var modalPageSize = 5;

            function renderModalTable() {
                var mTotalPages = Math.max(1, Math.ceil(modalItemData.length / modalPageSize));
                if (modalPageNo > mTotalPages) { modalPageNo = mTotalPages; }

                var $tbody = $modal.find('.vas-mcw-m-tbody');
                var $mHelper = $modal.find('.vas-mcw-modal-helper');
                var $mPagerTxt = $modal.find('.vas-mcw-m-pager-txt');
                var $mPrev = $modal.find('.vas-mcw-m-prev');
                var $mNext = $modal.find('.vas-mcw-m-next');

                var ofTxt = label("VAS_183_Of", "of");
                var linesTxt = label("VAS_183_Lines", "lines");
                var pageTxt = label("VAS_183_Page", "Page");

                /* The popup is exactly one page tall, so every page must render modalPageSize rows.
                   Pages holding fewer records are padded with spacer rows to stop the modal from
                   shrinking (and the footer from jumping) on the last page or an empty result.
                   The helper counts rows on THIS page so the numbers can't be read as a promise
                   of how many are visible. */
                function fillerRows(count) {
                    var html = '';
                    for (var f = 0; f < count; f++) {
                        html += '<tr class="vas-mcw-m-filler" aria-hidden="true"><td colspan="4">&nbsp;</td></tr>';
                    }
                    return html;
                }

                if (modalItemData.length === 0) {
                    $tbody.html('<tr><td colspan="4" class="vas-mcw-m-msgcell">' +
                        escapeHtml(label("VAS_183_NoItemsFound", "No items found")) + '</td></tr>' +
                        fillerRows(modalPageSize - 1));
                    $mHelper.text('0 ' + ofTxt + ' 0 ' + linesTxt);
                    $mPagerTxt.text(pageTxt + ' 1 ' + ofTxt + ' 1');
                    $mPrev.prop('disabled', true);
                    $mNext.prop('disabled', true);
                    return;
                }

                var mStart = (modalPageNo - 1) * modalPageSize;
                var mEnd = Math.min(modalItemData.length, mStart + modalPageSize);
                var tbodyHtml = '';

                for (var k = mStart; k < mEnd; k++) {
                    var item = modalItemData[k];
                    tbodyHtml +=
                        '<tr>' +
                        '<td class="vas-mcw-item-col" title="' + escapeHtml(item.productName) + '">' + escapeHtml(item.productName) + '</td>' +
                        '<td>' + escapeHtml(formatQty(item.consumedQty)) + '</td>' +
                        '<td>' + escapeHtml(item.uomName || label("VAS_183_Each", "Each")) + '</td>' +
                        '<td class="vas-mcw-attr-col" title="' + escapeHtml(item.attributes || "-") + '">' + escapeHtml(item.attributes || "-") + '</td>' +
                        '</tr>';
                }

                $tbody.html(tbodyHtml + fillerRows(modalPageSize - (mEnd - mStart)));
                $mHelper.text((mEnd - mStart) + ' ' + ofTxt + ' ' + modalItemData.length + ' ' + linesTxt);
                $mPagerTxt.text(pageTxt + ' ' + modalPageNo + ' ' + ofTxt + ' ' + mTotalPages);
                $mPrev.prop('disabled', modalPageNo <= 1);
                $mNext.prop('disabled', modalPageNo >= mTotalPages);
            }

            /* The close button and the scrim need separate handlers. Binding them together under
               an `e.target === this` guard made the button a dead zone: it contains an <svg> that
               fills it, so e.target is the icon and never the button, and the guard rejected the
               click unless it landed on the thin padding ring. The same call also tried to match
               the overlay with .find(), which only searches descendants -- $modal IS the overlay,
               so the scrim click never bound at all. */
            function closeModal() {
                $(document).off('keydown.vas-mcw-modal');
                if ($modal) { $modal.remove(); }
            }

            $modal.find('.vas-mcw-modal-close').on('click', function (e) {
                e.stopPropagation();
                closeModal();
            });

            $modal.on('click', function (e) {
                if (e.target === this) { closeModal(); }
            });

            $(document).on('keydown.vas-mcw-modal', function (e) {
                if (e.key === 'Escape' || e.keyCode === 27) { closeModal(); }
            });

            $modal.find('.vas-mcw-m-prev').on('click', function () {
                if (modalPageNo > 1) { modalPageNo--; renderModalTable(); }
            });
            $modal.find('.vas-mcw-m-next').on('click', function () {
                var mTotalPages = Math.ceil(modalItemData.length / modalPageSize);
                if (modalPageNo < mTotalPages) { modalPageNo++; renderModalTable(); }
            });

            $('body').append($modal);

            $.ajax({
                url: VIS.Application.contextUrl + 'VAS_183_MaterialConsumptionWidget/GetLocatorDetails',
                type: 'GET',
                data: { warehouseId: whId, locatorId: locatorId, month: selectedMonth, year: selectedYear },
                cache: false,
                // ===== NEW CODE START — currency format (agent A05, 2026-08-19) =====
                success: function (res) {
                    var data = parseResponse(res);
                    if (data.currency) { currencyInfo = data.currency; }
                    var formattedModalVal = formatCurrency(data.totalValue);
                    $modal.find('.vas-mcw-m-qty').text(formatQty(data.totalQty));
                    $modal.find('.vas-mcw-m-val').text(formattedModalVal.compact).attr('title', formattedModalVal.exact);
                    $modal.find('.vas-mcw-m-items').text(data.distinctItemCount || 0);

                    modalItemData = data.items || [];
                    modalPageNo = 1;
                    renderModalTable();
                }
                // ===== NEW CODE END — currency format =====
                // ----- OLD CODE (kept for rollback, do not delete) -----
                /*
                success: function (res) {
                    var data = parseResponse(res);
                    $modal.find('.vas-mcw-m-qty').text(formatQty(data.totalQty));
                    $modal.find('.vas-mcw-m-val').text(formatINR(data.totalValue));
                    $modal.find('.vas-mcw-m-items').text(data.distinctItemCount || 0);

                    modalItemData = data.items || [];
                    modalPageNo = 1;
                    renderModalTable();
                }
                */
                // ----- END OLD CODE -----
            });
        }

        function createWidget() {
            var title = label("VAS_183_Consumption", "Consumption");
            var sub = label("VAS_183_WarehouseLocatorWise", "Warehouse / locator wise");

            $card = $(
                '<div class="vas-mcw-card vas-widget-bg">' +
                '<div class="vas-mcw-head">' +
                '<div class="vas-mcw-head-left">' +
                '<span class="vas-mcw-ico" aria-hidden="true">' +
                '<svg viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2" stroke-linecap="round" stroke-linejoin="round"><path d="M21 16V8a2 2 0 0 0-1-1.73l-7-4a2 2 0 0 0-2 0l-7 4A2 2 0 0 0 3 8v8a2 2 0 0 0 1 1.73l7 4a2 2 0 0 0 2 0l7-4A2 2 0 0 0 21 16z"></path></svg>' +
                '</span>' +
                // Classed so it can carry min-width:0; as an unstyled flex child it would hold the
                // header open at the title's nowrap width and push the three filters over it -
                // the same defect that was reported on VAS_186.
                '<div class="vas-mcw-head-text">' +
                '<div class="vas-mcw-title">' + escapeHtml(title) + '</div>' +
                '<div class="vas-mcw-sub">' + escapeHtml(sub) + '</div>' +
                '</div>' +
                '</div>' +
                // Two pill buttons with chevrons, exactly as the source design specifies. They were
                // already here and correct - what was missing was that neither had a single click
                // handler bound to it, so the menus they are supposed to toggle never existed.
                '<div class="vas-mcw-controls">' +
                '<div class="vas-mcw-dd">' +
                '<button type="button" class="vas-mcw-pill-btn vas-mcw-wh-btn" aria-haspopup="true" aria-expanded="false">' +
                '<span class="vas-mcw-wh-lbl"></span>' +
                '<svg viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2" stroke-linecap="round" stroke-linejoin="round"><polyline points="6 9 12 15 18 9"/></svg>' +
                '</button>' +
                '</div>' +
                '<div class="vas-mcw-dd">' +
                '<button type="button" class="vas-mcw-pill-btn vas-mcw-month-btn" aria-haspopup="true" aria-expanded="false" title="' + escapeHtml(label("VAS_Month", "Month")) + '">' +
                '<svg viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2" stroke-linecap="round" stroke-linejoin="round"><rect x="3" y="4" width="18" height="18" rx="2" ry="2"/><line x1="16" y1="2" x2="16" y2="6"/><line x1="8" y1="2" x2="8" y2="6"/><line x1="3" y1="10" x2="21" y2="10"/></svg>' +
                '<span class="vas-mcw-month-lbl">' + escapeHtml(formatMonthLabel(selectedMonth, selectedYear)) + '</span>' +
                '<svg viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2" stroke-linecap="round" stroke-linejoin="round"><polyline points="6 9 12 15 18 9"/></svg>' +
                '</button>' +
                '</div>' +
                '</div>' +
                '</div>' +
                '<div class="vas-mcw-body"></div>' +
                '<div class="vas-mcw-foot">' +
                '<div class="vas-mcw-foot-helper"></div>' +
                '<div class="vas-mcw-pager">' +
                '<button type="button" class="vas-mcw-pager-btn vas-mcw-prev">&lsaquo;</button>' +
                '<span class="vas-mcw-pager-txt">1 of 1</span>' +
                '<button type="button" class="vas-mcw-pager-btn vas-mcw-next">&rsaquo;</button>' +
                '</div>' +
                '</div>' +
                '</div>'
            );

            $body = $card.find('.vas-mcw-body');
            $footHelper = $card.find('.vas-mcw-foot-helper');
            $pagerText = $card.find('.vas-mcw-pager-txt');
            $prevBtn = $card.find('.vas-mcw-prev');
            $nextBtn = $card.find('.vas-mcw-next');
            $whBtn = $card.find('.vas-mcw-wh-btn');
            $whLbl = $card.find('.vas-mcw-wh-lbl');
            $monthBtn = $card.find('.vas-mcw-month-btn');
            $monthLbl = $card.find('.vas-mcw-month-lbl');

            buildFilterMenus();

            $prevBtn.on('click', function () {
                if (pageNo > 1) { pageNo--; renderLocatorList(); }
            });

            $nextBtn.on('click', function () {
                if (pageNo < totalPages) { pageNo++; renderLocatorList(); }
            });

            $body.on('click', '.vas-mcw-row', function () {
                var locId = Number($(this).data('locid') || 0);
                var code = $(this).data('code');
                var name = $(this).data('name');
                openLocatorDetailModal(locId, code, name);
            });

            $root.append($card);

            $busy = $('<div class="vas-mcw-busy vas-mcw-hidden"><div class="vis-busyindicatorinnerwrap"><i class="vis_widgetloader"></i></div></div>');
            $root.append($busy);
        }

        this.refreshWidget = function () {
            loadLocatorSummary();
        };

        this.getRoot = function () { return $root; };

        this.disposeComponent = function () {
            // The two menus live on <body>, so they must be removed explicitly with their
            // document/window listeners - detaching $root alone would leak both.
            document.removeEventListener('mousedown', $self._onDocClickMcw, true);
            window.removeEventListener('resize', $self._onReflowMcw, true);
            window.removeEventListener('scroll', $self._onReflowMcw, true);
            if ($whMenu) { $whMenu.remove(); }
            if ($monthMenu) { $monthMenu.remove(); }

            $(document).off("keydown.vas-mcw-modal"); if ($modal) { $modal.remove(); }
            $root.remove();
        };
    };

    VAS.VAS_183_MaterialConsumptionWidget.prototype.widgetFirevalueChanged = function (value) {
        if (this.listener) { this.listener.widgetFirevalueChanged(value); }
    };

    VAS.VAS_183_MaterialConsumptionWidget.prototype.addChangeListener = function (listener) {
        this.listener = listener;
    };

    VAS.VAS_183_MaterialConsumptionWidget.prototype.init = function (windowNo, frame) {
        this.frame = frame;
        this.AD_UserHomeWidgetID = frame.widgetInfo.AD_UserHomeWidgetID;
        this.windowNo = windowNo;
        this.Initalize();
        this.frame.getContentGrid().append(this.getRoot());
        ensureDashInlineSizeVar(this.getRoot());
    };

    VAS.VAS_183_MaterialConsumptionWidget.prototype.widgetSizeChange = function (height, width) { };

    VAS.VAS_183_MaterialConsumptionWidget.prototype.refreshWidget = function () {
        this.refreshWidget();
    };

    VAS.VAS_183_MaterialConsumptionWidget.prototype.dispose = function () {
        this.disposeComponent();
        if (this.frame) { this.frame.dispose(); }
        this.frame = null;
    };

})(VAS, jQuery);

