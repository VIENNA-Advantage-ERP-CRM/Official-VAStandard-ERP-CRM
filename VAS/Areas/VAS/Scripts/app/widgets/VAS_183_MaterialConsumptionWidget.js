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
        var $monthBtn;
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

        function formatINR(value) {
            var val = Number(value || 0);
            if (val >= 100000) {
                return '₹' + (val / 100000).toFixed(2) + 'L';
            } else if (val >= 1000) {
                return '₹' + (val / 1000).toFixed(1) + 'K';
            }
            return '₹' + val.toLocaleString(window.navigator.language);
        }

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
                    if (warehouses.length > 0) {
                        selectedWarehouse = warehouses[0];
                        if ($whBtn) { $whBtn.text(selectedWarehouse.shortName); }
                    }
                    loadLocatorSummary();
                },
                error: function () { loadLocatorSummary(); }
            });
        }

        function loadLocatorSummary() {
            showBusy(true);
            var whId = selectedWarehouse ? selectedWarehouse.warehouseId : 0;

            $.ajax({
                url: VIS.Application.contextUrl + 'VAS_183_MaterialConsumptionWidget/GetLocatorSummary',
                type: 'GET',
                data: { warehouseId: whId, month: selectedMonth, year: selectedYear },
                cache: false,
                success: function (res) {
                    var data = parseResponse(res);
                    locatorsData = data.locators || [];
                    pageNo = 1;
                    renderLocatorList();
                },
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
                success: function (res) {
                    var data = parseResponse(res);
                    $modal.find('.vas-mcw-m-qty').text(formatQty(data.totalQty));
                    $modal.find('.vas-mcw-m-val').text(formatINR(data.totalValue));
                    $modal.find('.vas-mcw-m-items').text(data.distinctItemCount || 0);

                    modalItemData = data.items || [];
                    modalPageNo = 1;
                    renderModalTable();
                }
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
                '<div>' +
                '<div class="vas-mcw-title">' + escapeHtml(title) + '</div>' +
                '<div class="vas-mcw-sub">' + escapeHtml(sub) + '</div>' +
                '</div>' +
                '</div>' +
                '<div class="vas-mcw-controls">' +
                '<button type="button" class="vas-mcw-pill-btn vas-mcw-wh-btn">Main</button>' +
                '<button type="button" class="vas-mcw-pill-btn vas-mcw-month-btn">' + escapeHtml(formatMonthLabel(selectedMonth, selectedYear)) + '</button>' +
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
            $monthBtn = $card.find('.vas-mcw-month-btn');

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
