/**
 * Warehouse wise count Widget & Detail Modal (Physical Inventory / Inventory Count Dashboard)
 * Purpose - 4x2 glass card showing warehouse-wise inventory counts, locators, and counted quantities.
 *           Clicking a row opens a detailed breakdown modal of all count lines for that warehouse.
 * Prefix  - VAS_161_
 *
 * Labels / Message Keys Table:
 *  #  | Fallback Text                                    | Message Key
 * ----+--------------------------------------------------+-----------------------------------
 *  1  | Warehouse wise count                             | VAS_161_WarehouseWiseCount
 *  2  | Warehouse                                        | VAS_161_Warehouse
 *  3  | Locators                                         | VAS_161_Locators
 *  4  | Count Sessions                                   | VAS_161_CountSessions
 *  5  | Qty Counted                                      | VAS_161_QtyCounted
 *  6  | Product Name & Variant                           | VAS_161_ProductNameVariant
 *  7  | Inventory Type                                   | VAS_161_InventoryType
 *  8  | Quantity                                         | VAS_161_Quantity
 *  9  | Total Items                                      | VAS_161_TotalItems
 * 10  | Sessions                                         | VAS_161_Sessions
 * 11  | Page                                             | VAS_161_Page
 * 12  | of                                               | VAS_161_Of
 * 13  | Close                                            | VAS_161_Close
 * 14  | No warehouse count records found                 | VAS_161_NoWHCountRecords
 * 15  | Unable to load warehouse count summary           | VAS_161_UnableToLoadWHSummary
 */
; VAS = window.VAS || {};

; (function (VAS, $) {

    // Message-key lookup with a fallback, matching the other VAS widgets. This file had none, which
    // is why its title was a hardcoded literal.
    function lbl(key, fallback) {
        var t = VIS.Msg.getMsg(key);
        return (t && t.charAt(0) !== '[') ? t : fallback;
    }

    function ensureDashInlineSizeVar($el) {
        var container = $el.closest('.vis-widget-container, [data-dashboard-container], .vis-widget-body, body')[0] || document.documentElement;
        var write = function () {
            var w = container.clientWidth || window.innerWidth;
            if (w > 0) {
                document.documentElement.style.setProperty('--dash-inline-size', w + 'px');
            }
        };

        if (!window.__vasDashInlineSizeObserver && typeof ResizeObserver !== 'undefined') {
            window.__vasDashInlineSizeObserver = new ResizeObserver(write);
            window.__vasDashInlineSizeObserver.observe(container);
            window.addEventListener('resize', write);
        }
        write();
    }

    VAS.VAS_161_WHWiseCountWidget = function () {

        this.frame;
        this.windowNo;
        this.AD_UserHomeWidgetID = 0;

        var $self = this;
        var $wrapper = $('<div class="vas-whwisecount-container">');
        var $root = $('<div class="vas-whwisecount-root">');
        var $monthSelect;
        var $yearSelect;
        var $subtitle;
        var $rowsContainer;
        var $footer;

        var now = new Date();
        var selectedMonth = now.getMonth() + 1; // 1-12
        var selectedYear = now.getFullYear();
        var summaryData = [];
        var currentPage = 1;
        /* The popup ALWAYS holds exactly MODAL_PAGE_ROWS lines. Its size is fixed by that count and
           does NOT change with how much data there is: a page with 4 real rows renders 4 rows plus 3
           invisible filler rows, so the dialog is the same height as a page with 7. Anything past
           MODAL_PAGE_ROWS goes to the next page via the pager.
           A fixed count (not a measured one) is required because the dialog is content-sized -
           measuring the container would be circular, since its height comes from the rows. */
        var MODAL_PAGE_ROWS = 7;
        var pageSize = MODAL_PAGE_ROWS;
        var widgetObserver = null;
        var $modalOverlay = null;

        this.Initalize = function () {
            createWidget();
            loadYears();
        };

        function createWidget() {
            // Header Row
            var $headerRow = $('<div class="vas-whwisecount-header-row">');

            var $leftCluster = $('<div class="vas-whwisecount-left-cluster">');
            var $iconWell = $('<div class="vas-whwisecount-icon-well"><i class="fa fa-building-o"></i></div>');
            var $titleBlock = $('<div class="vas-whwisecount-title-block">');
            // Renamed "WH wise Count" -> "Warehouse wise count" (2026-08-16, user request).
            // Routed through the message key instead of a hardcoded literal (Rule 8).
            var $title = $('<h3 class="vas-whwisecount-title"></h3>').text(lbl("VAS_161_WarehouseWiseCount", "Warehouse wise count"));
            $subtitle = $('<span class="vas-whwisecount-subtitle">Warehouses with active count locators</span>');
            $titleBlock.append($title).append($subtitle);
            $leftCluster.append($iconWell).append($titleBlock);

            var $filterCluster = $('<div class="vas-whwisecount-filter-cluster">');
            $monthSelect = $('<select class="vas-whwisecount-select">');
            $yearSelect = $('<select class="vas-whwisecount-select">');

            var months = ["Jan", "Feb", "Mar", "Apr", "May", "Jun", "Jul", "Aug", "Sep", "Oct", "Nov", "Dec"];
            for (var m = 0; m < months.length; m++) {
                var $opt = $('<option value="' + (m + 1) + '">' + months[m] + '</option>');
                if (m + 1 === selectedMonth) {
                    $opt.attr('selected', 'selected');
                }
                $monthSelect.append($opt);
            }

            $filterCluster.append($monthSelect).append($yearSelect);
            $headerRow.append($leftCluster).append($filterCluster);
            $root.append($headerRow);

            // Body Grid Container
            var $body = $('<div class="vas-whwisecount-body">');
            var $headerGrid = $(
                '<div class="vas-whwisecount-grid-template vas-whwisecount-header-row">' +
                '<div class="vas-whwisecount-th">Warehouse</div>' +
                '<div class="vas-whwisecount-th">Count Locators</div>' +
                '<div class="vas-whwisecount-th vas-whwisecount-th-right">Counts</div>' +
                '<div class="vas-whwisecount-th vas-whwisecount-th-right">Qty Counted</div>' +
                '</div>'
            );
            $body.append($headerGrid);

            $rowsContainer = $('<div class="vas-whwisecount-rows-container">');
            $body.append($rowsContainer);

            // Footer / Pager Band
            $footer = $(
                '<div class="vas-whwisecount-footer">' +
                '<div class="vas-whwisecount-footer-text"></div>' +
                '<div class="vas-whwisecount-pager">' +
                '<button type="button" class="vas-whwisecount-pager-btn vas-prev" aria-label="Previous page">&lsaquo;</button>' +
                '<span class="vas-whwisecount-pager-info"></span>' +
                '<button type="button" class="vas-whwisecount-pager-btn vas-next" aria-label="Next page">&rsaquo;</button>' +
                '</div>' +
                '</div>'
            );
            $body.append($footer);

            $root.append($body);
            $wrapper.append($root);

            // Self-Sizing Observer
            if (window.ResizeObserver && $wrapper[0]) {
                widgetObserver = new ResizeObserver(function (entries) {
                    for (var i = 0; i < entries.length; i++) {
                        var width = entries[i].contentRect.width;
                        if (width > 0 && $root[0]) {
                            $root[0].style.setProperty('--widget-inline-size', width + 'px');
                        }
                    }
                });
                widgetObserver.observe($wrapper[0]);
            }

            // Event Handlers for Period Select
            $monthSelect.on('change', function () {
                selectedMonth = parseInt($(this).val(), 10);
                currentPage = 1;
                loadSummary();
            });

            $yearSelect.on('change', function () {
                selectedYear = parseInt($(this).val(), 10);
                currentPage = 1;
                loadSummary();
            });
        }

        function loadYears() {
            $.ajax({
                url: VIS.Application.contextUrl + "VAS_161_WHWiseCountWidget/GetAvailableYears",
                type: "GET",
                dataType: "json",
                success: function (res) {
                    if (res && res.years && res.years.length > 0) {
                        $yearSelect.empty();
                        for (var y = 0; y < res.years.length; y++) {
                            var yearVal = res.years[y];
                            var $opt = $('<option value="' + yearVal + '">' + yearVal + '</option>');
                            if (yearVal === selectedYear) {
                                $opt.attr('selected', 'selected');
                            }
                            $yearSelect.append($opt);
                        }
                    }
                    loadSummary();
                },
                error: function (err) {
                    console.error("VAS_161_WHWiseCountWidget: Error loading years", err);
                    loadSummary();
                }
            });
        }

        function loadSummary() {
            var monthsFull = ["January", "February", "March", "April", "May", "June", "July", "August", "September", "October", "November", "December"];
            var mName = monthsFull[selectedMonth - 1] || "";
            $subtitle.text("Warehouses with active count locators · " + mName + " " + selectedYear);

            $.ajax({
                url: VIS.Application.contextUrl + "VAS_161_WHWiseCountWidget/GetWarehouseSummary?month=" + selectedMonth + "&year=" + selectedYear,
                type: "GET",
                dataType: "json",
                success: function (res) {
                    if (res && res.data) {
                        summaryData = res.data;
                        renderSummaryPage();
                    } else {
                        summaryData = [];
                        renderSummaryPage();
                    }
                },
                error: function (err) {
                    console.error("VAS_161_WHWiseCountWidget: Error loading summary", err);
                    summaryData = [];
                    renderSummaryPage();
                }
            });
        }

        /* The popup is a fixed MODAL_PAGE_ROWS lines tall. Short pages and the empty state are
           padded with the spacers below so the height never changes while paging. */

        /* Invisible spacers that hold the table at a constant height. */
        function appendFillerRows($rowsContainer, count) {
            for (var f = 0; f < count; f++) {
                $rowsContainer.append(
                    '<div class="vas-whwisecount-row-btn vas-whwisecount-grid-template vas-whwisecount-filler" aria-hidden="true">' +
                    '<div class="vas-whwisecount-cell">&nbsp;</div>' +
                    '<div class="vas-whwisecount-cell">&nbsp;</div>' +
                    '<div class="vas-whwisecount-cell">&nbsp;</div>' +
                    '<div class="vas-whwisecount-cell">&nbsp;</div>' +
                    '</div>'
                );
            }
        }

        function renderSummaryPage() {
            $rowsContainer.empty();

            pageSize = MODAL_PAGE_ROWS;

            if (!summaryData || summaryData.length === 0) {
                $rowsContainer.html('<div class="vas-whwisecount-message">No inventory counts recorded for this period. Pick another month to review earlier counts.</div>');
                // Hold the dialog's full height even with nothing to show (global standard).
                appendFillerRows($rowsContainer, Math.max(0, pageSize - 1));
                $footer.find('.vas-whwisecount-footer-text').text('Showing 0 of 0');
                $footer.find('.vas-whwisecount-pager').hide();
                return;
            }

            var totalItems = summaryData.length;
            var totalPages = Math.ceil(totalItems / pageSize) || 1;

            if (currentPage > totalPages) currentPage = totalPages;
            if (currentPage < 1) currentPage = 1;

            var startIndex = (currentPage - 1) * pageSize;
            var endIndex = Math.min(startIndex + pageSize, totalItems);
            var pageItems = summaryData.slice(startIndex, endIndex);

            for (var i = 0; i < pageItems.length; i++) {
                var item = pageItems[i];
                var formattedQty = Number(item.totalQtyCounted || 0).toLocaleString();

                var $row = $(
                    '<button type="button" class="vas-whwisecount-row-btn vas-whwisecount-grid-template" aria-label="Open count details for ' + item.warehouseName + '">' +
                    '<div class="vas-whwisecount-cell">' +
                    '<div class="vas-whwisecount-wh-title" title="' + item.warehouseName + '">' + item.warehouseName + '</div>' +
                    '<div class="vas-whwisecount-wh-code" title="' + item.warehouseCode + '">' + item.warehouseCode + '</div>' +
                    '</div>' +
                    '<div class="vas-whwisecount-cell vas-whwisecount-locators-text" title="' + item.locators + '">' + item.locators + '</div>' +
                    '<div class="vas-whwisecount-cell vas-whwisecount-counts-num" title="' + item.sessionCount + '">' + item.sessionCount + '</div>' +
                    '<div class="vas-whwisecount-cell vas-whwisecount-qty-num" title="' + formattedQty + '">' + formattedQty + '</div>' +
                    '</button>'
                );

                (function (whItem, $r) {
                    $r.on('click', function () {
                        openDetailModal(whItem, $r);
                    });
                })(item, $row);

                $rowsContainer.append($row);
            }

            // Pad the last (or only) page so the table height is identical on every page.
            appendFillerRows($rowsContainer, Math.max(0, pageSize - pageItems.length));

            var $footerText = $footer.find('.vas-whwisecount-footer-text');
            var $pagerInfo = $footer.find('.vas-whwisecount-pager-info');
            var $btnPrev = $footer.find('.vas-prev');
            var $btnNext = $footer.find('.vas-next');

            $footerText.text('Showing ' + (startIndex + 1) + '–' + endIndex + ' of ' + totalItems);
            $pagerInfo.text(currentPage + ' of ' + totalPages);

            $btnPrev.prop('disabled', currentPage === 1);
            $btnNext.prop('disabled', currentPage === totalPages);

            $btnPrev.off('click').on('click', function () {
                if (currentPage > 1) {
                    currentPage--;
                    renderSummaryPage();
                }
            });

            $btnNext.off('click').on('click', function () {
                if (currentPage < totalPages) {
                    currentPage++;
                    renderSummaryPage();
                }
            });

            if (totalPages <= 1) {
                $footer.find('.vas-whwisecount-pager').hide();
            } else {
                $footer.find('.vas-whwisecount-pager').show();
            }
        }

        function openDetailModal(whItem, $originBtn) {
            if ($modalOverlay) {
                $modalOverlay.remove();
            }

            var monthsFull = ["January", "February", "March", "April", "May", "June", "July", "August", "September", "October", "November", "December"];
            var monthName = monthsFull[selectedMonth - 1] || "";

            var $overlay = $('<div class="vas-whwisecount-modal-overlay">');
            var $dialog = $('<div class="vas-whwisecount-modal-dialog" role="dialog" aria-modal="true">');

            // Modal Header Chrome
            var $header = $('<div class="vas-whwisecount-modal-header">');
            var $headerLeft = $('<div class="vas-whwisecount-modal-header-left">');
            var $title = $('<h3 class="vas-whwisecount-modal-title" title="' + whItem.warehouseName + '">' + whItem.warehouseName + '</h3>');
            var $subtitle = $('<span class="vas-whwisecount-modal-subtitle">' + whItem.warehouseCode + ' · ' + monthName + ' ' + selectedYear + '</span>');
            $headerLeft.append($title).append($subtitle);

            var $closeBtn = $('<button type="button" class="vas-whwisecount-modal-close" aria-label="Close modal">&times;</button>');
            $header.append($headerLeft).append($closeBtn);
            $dialog.append($header);

            // Modal Body
            var $body = $('<div class="vas-whwisecount-modal-body">');
            $body.html('<div class="vas-whwisecount-message">Loading warehouse details...</div>');
            $dialog.append($body);

            $overlay.append($dialog);
            $('body').append($overlay);
            $modalOverlay = $overlay;

            $closeBtn.focus();

            var closeModal = function () {
                $overlay.remove();
                $modalOverlay = null;
                if ($originBtn) {
                    $originBtn.focus();
                }
            };

            $closeBtn.on('click', closeModal);
            $overlay.on('click', function (e) {
                if ($(e.target).hasClass('vas-whwisecount-modal-overlay')) {
                    closeModal();
                }
            });

            $(document).off('keydown.vas-whwisecount').on('keydown.vas-whwisecount', function (e) {
                if (e.key === 'Escape' && $modalOverlay) {
                    closeModal();
                }
            });

            // Fetch Detail Lines
            $.ajax({
                url: VIS.Application.contextUrl + "VAS_161_WHWiseCountWidget/GetWarehouseDetail?month=" + selectedMonth + "&year=" + selectedYear + "&warehouseId=" + whItem.warehouseId,
                type: "GET",
                dataType: "json",
                success: function (res) {
                    if (res && res.lines && res.lines.length > 0) {
                        $subtitle.text(whItem.warehouseCode + ' · ' + monthName + ' ' + selectedYear + ' · ' + (res.locatorCount || 0) + ' locators');
                        renderModalGrid($body, res);
                    } else {
                        $body.html('<div class="vas-whwisecount-message">No count lines found for this warehouse.</div>');
                    }
                },
                error: function (err) {
                    console.error("VAS_161_WHWiseCountWidget: Error loading detail", err);
                    $body.html('<div class="vas-whwisecount-message">Unable to load count lines.</div>');
                }
            });
        }

        function renderModalGrid($body, resData) {
            $body.empty();

            var lines = resData.lines || [];
            var totalLines = resData.totalLines || lines.length;
            var sessionCount = resData.sessionCount || 0;
            var totalQty = resData.totalQty || 0;

            var modalPage = 1;
            /* The detail popup ALWAYS holds exactly this many lines. Short pages are padded with
               invisible filler rows below, so the popup is the SAME height whether a page carries
               7 records or 3 - it no longer shrinks to fit the last page. */
            var modalPageSize = MODAL_PAGE_ROWS;

            var $headerGrid = $(
                '<div class="vas-whwisecount-modal-grid-template vas-whwisecount-header-row">' +
                '<div class="vas-whwisecount-th">Product / Attribute</div>' +
                '<div class="vas-whwisecount-th">Locator</div>' +
                '<div class="vas-whwisecount-th">Inventory Type</div>' +
                '<div class="vas-whwisecount-th vas-whwisecount-th-right">Qty</div>' +
                '</div>'
            );
            $body.append($headerGrid);

            var $modalRowsContainer = $('<div class="vas-whwisecount-modal-rows-container"></div>');
            $body.append($modalRowsContainer);

            var $footer = $(
                '<div class="vas-whwisecount-modal-footer">' +
                '<div class="vas-whwisecount-modal-footer-left">' +
                '<span class="vas-whwisecount-footer-text vas-modal-helper"></span>' +
                '<span class="vas-whwisecount-modal-total-qty">Total qty ' + Number(totalQty).toLocaleString() + '</span>' +
                '</div>' +
                '<div class="vas-whwisecount-pager">' +
                '<button type="button" class="vas-whwisecount-pager-btn vas-m-prev" aria-label="Previous page">&lsaquo;</button>' +
                '<span class="vas-whwisecount-pager-info vas-m-info"></span>' +
                '<button type="button" class="vas-whwisecount-pager-btn vas-m-next" aria-label="Next page">&rsaquo;</button>' +
                '</div>' +
                '</div>'
            );
            $body.append($footer);

            function appendModalFillerRows($container, count) {
                for (var f = 0; f < count; f++) {
                    $container.append(
                        '<div class="vas-whwisecount-modal-grid-template vas-whwisecount-modal-data-row vas-whwisecount-filler" aria-hidden="true">' +
                        '<div class="vas-whwisecount-cell"><div class="vas-whwisecount-prod-title">&nbsp;</div>' +
                        '<div class="vas-whwisecount-prod-attr">&nbsp;</div></div>' +
                        '<div class="vas-whwisecount-cell">&nbsp;</div>' +
                        '<div class="vas-whwisecount-cell">&nbsp;</div>' +
                        '<div class="vas-whwisecount-cell">&nbsp;</div>' +
                        '</div>'
                    );
                }
            }

            function updateModalPage() {
                $modalRowsContainer.empty();
                var totalPages = Math.ceil(totalLines / modalPageSize) || 1;

                if (modalPage > totalPages) modalPage = totalPages;
                if (modalPage < 1) modalPage = 1;

                var start = (modalPage - 1) * modalPageSize;
                var end = Math.min(start + modalPageSize, totalLines);
                var paged = lines.slice(start, end);

                for (var i = 0; i < paged.length; i++) {
                    var line = paged[i];
                    var formattedQty = Number(line.qty || 0).toLocaleString();
                    var isCharge = line.inventoryType === 'Charge Account';
                    var chipClass = isCharge ? 'vas-whwisecount-chip-neutral' : 'vas-whwisecount-chip-info';

                    var $mRow = $(
                        '<div class="vas-whwisecount-modal-grid-template vas-whwisecount-modal-data-row">' +
                        '<div class="vas-whwisecount-cell">' +
                        '<div class="vas-whwisecount-prod-title" title="' + line.product + '">' + line.product + '</div>' +
                        '<div class="vas-whwisecount-prod-attr" title="' + line.attribute + '">' + line.attribute + '</div>' +
                        '</div>' +
                        '<div class="vas-whwisecount-cell vas-whwisecount-locators-text" title="' + line.locator + '">' + line.locator + '</div>' +
                        '<div class="vas-whwisecount-cell"><span class="' + chipClass + '">' + line.inventoryType + '</span></div>' +
                        '<div class="vas-whwisecount-cell vas-whwisecount-qty-num" title="' + formattedQty + '">' + formattedQty + '</div>' +
                        '</div>'
                    );
                    $modalRowsContainer.append($mRow);
                }

                // Hold the popup at MODAL_PAGE_ROWS lines regardless of how many this page has.
                appendModalFillerRows($modalRowsContainer, Math.max(0, modalPageSize - paged.length));

                $footer.find('.vas-modal-helper').text('Showing ' + (start + 1) + '–' + end + ' of ' + totalLines + ' · ' + sessionCount + ' count sessions');
                $footer.find('.vas-m-info').text(modalPage + ' of ' + totalPages);

                var $mPrev = $footer.find('.vas-m-prev');
                var $mNext = $footer.find('.vas-m-next');

                $mPrev.prop('disabled', modalPage === 1);
                $mNext.prop('disabled', modalPage === totalPages);

                $mPrev.off('click').on('click', function () {
                    if (modalPage > 1) {
                        modalPage--;
                        updateModalPage();
                    }
                });

                $mNext.off('click').on('click', function () {
                    if (modalPage < totalPages) {
                        modalPage++;
                        updateModalPage();
                    }
                });

                if (totalPages <= 1) {
                    $footer.find('.vas-whwisecount-pager').hide();
                } else {
                    $footer.find('.vas-whwisecount-pager').show();
                }
            }

            updateModalPage();
        }

        this.getRoot = function () {
            return $wrapper;
        };

        this.refreshData = function () {
            loadSummary();
        };

        this.disposeComponent = function () {
            if (widgetObserver) {
                widgetObserver.disconnect();
                widgetObserver = null;
            }
            if ($modalOverlay) {
                $modalOverlay.remove();
                $modalOverlay = null;
            }
            $wrapper.remove();
        };
    };

    VAS.VAS_161_WHWiseCountWidget.prototype.init = function (windowNo, frame) {
        this.frame = frame;
        this.AD_UserHomeWidgetID = frame.widgetInfo.AD_UserHomeWidgetID;
        this.windowNo = windowNo;
        this.Initalize();
        this.frame.getContentGrid().append(this.getRoot());
        ensureDashInlineSizeVar(this.getRoot());
    };

    VAS.VAS_161_WHWiseCountWidget.prototype.widgetSizeChange = function (height, width) { };

    VAS.VAS_161_WHWiseCountWidget.prototype.refreshWidget = function () {
        this.refreshData();
    };

    VAS.VAS_161_WHWiseCountWidget.prototype.dispose = function () {
        this.disposeComponent();
        if (this.frame) { this.frame.dispose(); }
        this.frame = null;
    };

})(VAS, jQuery);

