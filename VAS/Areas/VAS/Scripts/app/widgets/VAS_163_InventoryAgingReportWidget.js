/**
 * Inventory Aging Report Widget & Modal (Inventory Count Dashboard)
 * Purpose - 3x2 glass card categorizing on-hand stock into 4 age buckets (0-30, 31-90, 91-180, 180+ days).
 *           Clicking a bucket opens a detailed product breakdown modal with warehouse filter.
 * Prefix  - VAS_163_
 *
 * Labels / Message Keys Table:
 *  #  | Fallback Text                                    | Message Key
 * ----+--------------------------------------------------+-----------------------------------
 *  1  | Inventory Aging Report                           | VAS_163_InventoryAgingReport
 *  2  | All Warehouses                                   | VAS_163_AllWarehouses
 *  3  | Fresh Stock (0-30 Days)                          | VAS_163_FreshStock0_30
 *  4  | Moderate Age (31-90 Days)                        | VAS_163_ModerateAge31_90
 *  5  | Aging Stock (91-180 Days)                        | VAS_163_AgingStock91_180
 *  6  | Dead Stock (180+ Days)                           | VAS_163_DeadStock180_Plus
 *  7  | Product Name & Attribute                         | VAS_163_ProductNameAttribute
 *  8  | Warehouse                                        | VAS_163_Warehouse
 *  9  | Locator                                          | VAS_163_Locator
 * 10  | Age                                              | VAS_163_Age
 * 11  | Qty On Hand                                      | VAS_163_QtyOnHand
 * 12  | Page                                             | VAS_163_Page
 * 13  | of                                               | VAS_163_Of
 * 14  | Close                                            | VAS_163_Close
 * 15  | No stock items found in this age bucket          | VAS_163_NoStockInBucket
 * 16  | Unable to load inventory aging summary           | VAS_163_UnableToLoadAgingSummary
 */
; VAS = window.VAS || {};

; (function (VAS, $) {

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

    VAS.VAS_163_InventoryAgingReportWidget = function () {

        this.frame;
        this.windowNo;
        this.AD_UserHomeWidgetID = 0;

        var $self = this;
        var $wrapper = $('<div class="vas-invaging-container">');
        var $root = $('<div class="vas-invaging-root">');
        var $whSelect;

        var selectedWarehouseId = null;
        var selectedWarehouseName = "All Warehouses";
        var warehousesList = [];
        var summaryData = { b0_30: 0, b31_90: 0, b91_180: 0, b180_plus: 0, totalProducts: 0 };

        var $bucketBtns = {};
        var widgetObserver = null;
        var $modalOverlay = null;

        var bucketsConfig = [
            { id: "0-30", label: "0-30 days", meta: "Fresh stock", color: "#20A464" },
            { id: "31-90", label: "31-90 days", meta: "Normal turnover", color: "#0083DA" },
            { id: "91-180", label: "91-180 days", meta: "Slow moving - watch", color: "#D78B10" },
            { id: "180+", label: "180+ days", meta: "Dead stock", color: "#D14545" }
        ];

        this.Initalize = function () {
            createWidget();
            loadWarehouses();
        };

        function createWidget() {
            // Header Row
            var $headerRow = $('<div class="vas-invaging-header-row">');

            var $leftCluster = $('<div class="vas-invaging-left-cluster">');
            var $iconWell = $('<div class="vas-invaging-icon-well"><i class="fa fa-clock-o"></i></div>');
            var $titleBlock = $('<div class="vas-invaging-title-block">');
            var $title = $('<h3 class="vas-invaging-title">Inventory Aging Report</h3>');
            var $subtitle = $('<span class="vas-invaging-subtitle">Products on hand by age</span>');
            $titleBlock.append($title).append($subtitle);
            $leftCluster.append($iconWell).append($titleBlock);

            // Native Warehouse Select Control (Unclipped by container overflow:hidden)
            $whSelect = $('<select class="vas-invaging-wh-select">');
            $whSelect.append('<option value="">All Warehouses</option>');

            $headerRow.append($leftCluster).append($whSelect);
            $root.append($headerRow);

            // Body — 4 Stacked Bucket Rows
            var $body = $('<div class="vas-invaging-body">');

            for (var i = 0; i < bucketsConfig.length; i++) {
                var b = bucketsConfig[i];
                var $btn = $('<button type="button" class="vas-invaging-bucket-btn" data-id="' + b.id + '" aria-label="View ' + b.label + ' products">');

                var $topLine = $('<div class="vas-invaging-top-line">');
                var $labelCluster = $('<div class="vas-invaging-label-cluster">');
                var $dot = $('<div class="vas-invaging-dot" style="background-color: ' + b.color + ';"></div>');
                var $bTitle = $('<span class="vas-invaging-bucket-title">' + b.label + '</span>');
                $labelCluster.append($dot).append($bTitle);

                var $countText = $('<span class="vas-invaging-count-text" style="color: ' + b.color + ';">0 products</span>');
                $topLine.append($labelCluster).append($countText);

                var $metaText = $('<div class="vas-invaging-meta-text">' + b.meta + '</div>');

                var $barTrack = $('<div class="vas-invaging-bar-track">');
                var $barFill = $('<div class="vas-invaging-bar-fill" style="background-color: ' + b.color + '; width: 0%;"></div>');
                $barTrack.append($barFill);

                $btn.append($topLine).append($metaText).append($barTrack);
                $body.append($btn);

                $bucketBtns[b.id] = {
                    $btn: $btn,
                    $countText: $countText,
                    $barFill: $barFill,
                    config: b
                };

                (function (bConfig, $b) {
                    $b.on('click', function () {
                        openDetailModal(bConfig, $b);
                    });
                })(b, $btn);
            }

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

            // Dropdown Change Handler
            $whSelect.on('change', function () {
                var val = $(this).val();
                selectedWarehouseId = val ? parseInt(val, 10) : null;

                if (selectedWarehouseId) {
                    var selectedOpt = $(this).find('option:selected').text();
                    selectedWarehouseName = selectedOpt || "Warehouse";
                } else {
                    selectedWarehouseName = "All Warehouses";
                }

                loadSummary();
            });
        }

        function loadWarehouses() {
            $.ajax({
                url: VIS.Application.contextUrl + "VAS_163_InventoryAgingReportWidget/GetWarehouses",
                type: "GET",
                dataType: "json",
                success: function (res) {
                    if (res && res.warehouses) {
                        warehousesList = res.warehouses;
                        renderWarehouseOptions();
                    }
                    loadSummary();
                },
                error: function (err) {
                    console.error("VAS_163_InventoryAgingReportWidget: Error loading warehouses", err);
                    loadSummary();
                }
            });
        }

        function renderWarehouseOptions() {
            $whSelect.empty();
            $whSelect.append('<option value="">All Warehouses</option>');

            for (var i = 0; i < warehousesList.length; i++) {
                var wh = warehousesList[i];
                var $opt = $('<option value="' + wh.warehouseId + '">' + wh.name + '</option>');
                if (selectedWarehouseId && selectedWarehouseId === wh.warehouseId) {
                    $opt.attr('selected', 'selected');
                }
                $whSelect.append($opt);
            }
        }

        function loadSummary() {
            var url = VIS.Application.contextUrl + "VAS_163_InventoryAgingReportWidget/GetAgingSummary";
            if (selectedWarehouseId) {
                url += "?warehouseId=" + selectedWarehouseId;
            }

            $.ajax({
                url: url,
                type: "GET",
                dataType: "json",
                success: function (res) {
                    if (res) {
                        summaryData = res;
                        updateWidgetUI();
                    }
                },
                error: function (err) {
                    console.error("VAS_163_InventoryAgingReportWidget: Error loading summary", err);
                }
            });
        }

        function updateWidgetUI() {
            var total = summaryData.totalProducts || 0;

            for (var i = 0; i < bucketsConfig.length; i++) {
                var b = bucketsConfig[i];
                var count = 0;

                if (b.id === "0-30") count = summaryData.b0_30 || 0;
                else if (b.id === "31-90") count = summaryData.b31_90 || 0;
                else if (b.id === "91-180") count = summaryData.b91_180 || 0;
                else if (b.id === "180+") count = summaryData.b180_plus || 0;

                var btnObj = $bucketBtns[b.id];
                if (btnObj) {
                    btnObj.$countText.text(count.toLocaleString() + (count === 1 ? " product" : " products"));

                    var pct = total > 0 ? Math.round((count / total) * 100) : 0;
                    btnObj.$barFill.css("width", pct + "%");
                }
            }
        }

        function openDetailModal(bConfig, $originBtn) {
            if ($modalOverlay) {
                $modalOverlay.remove();
            }

            var $overlay = $('<div class="vas-invaging-modal-overlay">');
            var $dialog = $('<div class="vas-invaging-modal-dialog" role="dialog" aria-modal="true">');

            // Header (Chrome 56px)
            var $header = $('<div class="vas-invaging-modal-header">');
            var $headerLeft = $('<div class="vas-invaging-modal-header-left">');
            var $title = $('<h3 class="vas-invaging-modal-title">Inventory Aging · ' + bConfig.label + '</h3>');
            var $pill = $('<span class="vas-invaging-modal-pill">Loading...</span>');
            $headerLeft.append($title).append($pill);

            var $closeBtn = $('<button type="button" class="vas-invaging-modal-close" aria-label="Close modal">&times;</button>');
            $header.append($headerLeft).append($closeBtn);
            $dialog.append($header);

            // Summary Field Grid
            var $summaryGrid = $(
                '<div class="vas-invaging-summary-grid">' +
                '<div class="vas-invaging-summary-cell"><span class="vas-invaging-summary-label">Age Bucket</span><span class="vas-invaging-summary-val" style="color:' + bConfig.color + ';">' + bConfig.label + '</span></div>' +
                '<div class="vas-invaging-summary-cell"><span class="vas-invaging-summary-label">Category Status</span><span class="vas-invaging-summary-val">' + bConfig.meta + '</span></div>' +
                '<div class="vas-invaging-summary-cell"><span class="vas-invaging-summary-label">Scope</span><span class="vas-invaging-summary-val">All Stocked Items</span></div>' +
                '<div class="vas-invaging-summary-cell"><span class="vas-invaging-summary-label">Warehouse</span><span class="vas-invaging-summary-val">' + selectedWarehouseName + '</span></div>' +
                '</div>'
            );
            $dialog.append($summaryGrid);

            // Modal Body
            var $body = $('<div class="vas-invaging-modal-body">');
            $body.html('<div class="vas-invaging-message">Loading products...</div>');
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
                if ($(e.target).hasClass('vas-invaging-modal-overlay')) {
                    closeModal();
                }
            });

            $(document).off('keydown.vas-invaging').on('keydown.vas-invaging', function (e) {
                if (e.key === 'Escape' && $modalOverlay) {
                    closeModal();
                }
            });

            // Fetch Bucket Details
            var url = VIS.Application.contextUrl + "VAS_163_InventoryAgingReportWidget/GetBucketDetail?bucketId=" + encodeURIComponent(bConfig.id);
            if (selectedWarehouseId) {
                url += "&warehouseId=" + selectedWarehouseId;
            }

            $.ajax({
                url: url,
                type: "GET",
                dataType: "json",
                success: function (res) {
                    if (res && res.details) {
                        var totalCount = res.totalCount || res.details.length;
                        $pill.text(totalCount === 1 ? "1 product" : totalCount + " products");
                        renderModalGrid($body, res.details, totalCount);
                    } else {
                        $pill.text("0 products");
                        $body.html('<div class="vas-invaging-message">No products found in this age bucket.</div>');
                    }
                },
                error: function (err) {
                    console.error("VAS_163_InventoryAgingReportWidget: Error loading bucket detail", err);
                    $pill.text("0 products");
                    $body.html('<div class="vas-invaging-message">Unable to load product list.</div>');
                }
            });
        }

        function renderModalGrid($body, details, totalCount) {
            $body.empty();

            var modalPage = 1;

            function getAdaptivePageSize() {
                var bh = $body.height() || 400;
                var available = bh - 90;
                return Math.max(3, Math.floor(available / 54));
            }

            var modalPageSize = getAdaptivePageSize();

            var $sectionTitle = $('<div class="vas-invaging-table-header">Products in this bucket</div>');
            $body.append($sectionTitle);

            var $headerGrid = $(
                '<div class="vas-invaging-modal-grid-template vas-invaging-header-row" style="padding: 0.375em 0.5em; border-bottom: 1px solid #D7D7D7;">' +
                '<div class="vas-invaging-modal-th">Item / Attributes</div>' +
                '<div class="vas-invaging-modal-th">Warehouse</div>' +
                '<div class="vas-invaging-modal-th">Locator</div>' +
                '<div class="vas-invaging-modal-th vas-invaging-modal-th-right">Qty</div>' +
                '</div>'
            );
            $body.append($headerGrid);

            var $rowsContainer = $('<div class="vas-invaging-modal-rows-container" style="flex: 1; display: flex; flex-direction: column; overflow: hidden;"></div>');
            $body.append($rowsContainer);

            var $footer = $(
                '<div class="vas-invaging-modal-footer">' +
                '<div class="vas-invaging-modal-footer-text vas-m-helper">Showing 0-0 of 0 products</div>' +
                '<div class="vas-invaging-pager">' +
                '<button type="button" class="vas-invaging-pager-btn vas-m-prev" aria-label="Previous page">&lsaquo;</button>' +
                '<span class="vas-invaging-pager-info vas-m-info">1 of 1</span>' +
                '<button type="button" class="vas-invaging-pager-btn vas-m-next" aria-label="Next page">&rsaquo;</button>' +
                '</div>' +
                '</div>'
            );
            $body.append($footer);

            function updateModalPage() {
                $rowsContainer.empty();
                modalPageSize = getAdaptivePageSize();
                var totalPages = Math.ceil(totalCount / modalPageSize) || 1;

                if (modalPage > totalPages) modalPage = totalPages;
                if (modalPage < 1) modalPage = 1;

                var start = (modalPage - 1) * modalPageSize;
                var end = Math.min(start + modalPageSize, totalCount);
                var pageItems = details.slice(start, end);

                for (var i = 0; i < pageItems.length; i++) {
                    var item = pageItems[i];
                    var formattedQty = Number(item.qty || 0).toLocaleString();

                    var $mRow = $(
                        '<div class="vas-invaging-modal-grid-template vas-invaging-modal-data-row">' +
                        '<div class="vas-invaging-cell">' +
                        '<div class="vas-invaging-prod-name" title="' + item.product + '">' + item.product + '</div>' +
                        '<div class="vas-invaging-prod-attr" title="' + item.attribute + '">' + item.attribute + '</div>' +
                        '</div>' +
                        '<div class="vas-invaging-cell vas-invaging-cell-text" title="' + item.warehouse + '">' + item.warehouse + '</div>' +
                        '<div class="vas-invaging-cell vas-invaging-cell-text" title="' + item.locator + '">' + item.locator + '</div>' +
                        '<div class="vas-invaging-cell vas-invaging-cell-qty" title="' + formattedQty + '">' + formattedQty + '</div>' +
                        '</div>'
                    );
                    $rowsContainer.append($mRow);
                }

                $footer.find('.vas-m-helper').text('Showing ' + (totalCount > 0 ? (start + 1) : 0) + '–' + end + ' of ' + totalCount + (totalCount === 1 ? ' product' : ' products'));
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
                    $footer.find('.vas-invaging-pager').hide();
                } else {
                    $footer.find('.vas-invaging-pager').show();
                }
            }

            updateModalPage();

            $(window).off('resize.vas-invaging-modal').on('resize.vas-invaging-modal', function () {
                if ($modalOverlay) {
                    updateModalPage();
                }
            });
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
            $(window).off('resize.vas-invaging-modal');
            $wrapper.remove();
        };
    };

    VAS.VAS_163_InventoryAgingReportWidget.prototype.init = function (windowNo, frame) {
        this.frame = frame;
        this.AD_UserHomeWidgetID = frame.widgetInfo.AD_UserHomeWidgetID;
        this.windowNo = windowNo;
        this.Initalize();
        this.frame.getContentGrid().append(this.getRoot());
        ensureDashInlineSizeVar(this.getRoot());
    };

    VAS.VAS_163_InventoryAgingReportWidget.prototype.widgetSizeChange = function (height, width) { };

    VAS.VAS_163_InventoryAgingReportWidget.prototype.refreshWidget = function () {
        this.refreshData();
    };

    VAS.VAS_163_InventoryAgingReportWidget.prototype.dispose = function () {
        this.disposeComponent();
        if (this.frame) { this.frame.dispose(); }
        this.frame = null;
    };

})(VAS, jQuery);
