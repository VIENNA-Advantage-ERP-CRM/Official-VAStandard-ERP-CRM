/**
 * Adjustment Wise Count Widget (Physical Inventory / Inventory Count Dashboard)
 * Purpose - 3x2 glass card summary of stock adjustments split by As-on-Date Count vs Quantity Difference.
 * Prefix  - VAS_162_
 *
 * Labels / Message Keys Table:
 *  #  | Fallback Text                                    | Message Key
 * ----+--------------------------------------------------+-----------------------------------
 *  1  | Adjustment Wise Count                            | VAS_162_AdjustmentWiseCount
 *  2  | As-on-Date Count                                 | VAS_162_AsOnDateCount
 *  3  | Quantity Difference                              | VAS_162_QuantityDifference
 *  4  | Total Count Sessions                             | VAS_162_TotalCountSessions
 *  5  | Net Difference Qty                               | VAS_162_NetDifferenceQty
 *  6  | Product                                          | VAS_162_Product
 *  7  | Attribute                                        | VAS_162_Attribute
 *  8  | Book Qty                                         | VAS_162_BookQty
 *  9  | Count Qty                                        | VAS_162_CountQty
 * 10  | Diff Qty                                         | VAS_162_DiffQty
 * 11  | Close                                            | VAS_162_Close
 * 12  | No adjustment records available                  | VAS_162_NoAdjustmentRecords
 * 13  | Unable to load adjustment summary                | VAS_162_UnableToLoadAdjSummary
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

    VAS.VAS_162_AdjustmentWiseCountWidget = function () {

        this.frame;
        this.windowNo;
        this.AD_UserHomeWidgetID = 0;

        var $self = this;
        var $wrapper = $('<div class="vas-adjwisecount-container">');
        var $root = $('<div class="vas-adjwisecount-root">');
        var $monthSelect;
        var $yearSelect;
        var $asOnCountVal;
        var $asOnCountMeta;
        var $qtyDiffVal;
        var $qtyDiffMeta;

        var now = new Date();
        var selectedMonth = now.getMonth() + 1; // 1-12
        var selectedYear = now.getFullYear();
        var widgetObserver = null;
        var $modalOverlay = null;

        this.Initalize = function () {
            createWidget();
            loadYears();
        };

        function createWidget() {
            // Header Row
            var $headerRow = $('<div class="vas-adjwisecount-header-row">');

            var $leftCluster = $('<div class="vas-adjwisecount-left-cluster">');
            var $iconWell = $('<div class="vas-adjwisecount-icon-well"><i class="fa fa-list-alt"></i></div>');
            var $title = $('<h3 class="vas-adjwisecount-title">Adjustment Wise Count</h3>');
            $leftCluster.append($iconWell).append($title);

            var $filterCluster = $('<div class="vas-adjwisecount-filter-cluster">');
            $monthSelect = $('<select class="vas-adjwisecount-select">');
            $yearSelect = $('<select class="vas-adjwisecount-select">');

            var months = ["January", "February", "March", "April", "May", "June", "July", "August", "September", "October", "November", "December"];
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

            // Body Container (2 Tiles)
            var $body = $('<div class="vas-adjwisecount-body">');

            // Tile 1 — As on Date Count
            var $tile1 = $('<div class="vas-adjwisecount-tile" role="button" tabindex="0" aria-label="As on Date Count Tile">');
            var $tile1Left = $('<div class="vas-adjwisecount-tile-left">');
            var $tile1Label = $('<div class="vas-adjwisecount-tile-label">As on Date Count</div>');
            $asOnCountMeta = $('<div class="vas-adjwisecount-tile-meta">Loading...</div>');
            $tile1Left.append($tile1Label).append($asOnCountMeta);
            $asOnCountVal = $('<div class="vas-adjwisecount-tile-val-blue">--</div>');
            $tile1.append($tile1Left).append($asOnCountVal);

            // Tile 2 — Quantity Difference
            var $tile2 = $('<div class="vas-adjwisecount-tile" role="button" tabindex="0" aria-label="Quantity Difference Tile">');
            var $tile2Left = $('<div class="vas-adjwisecount-tile-left">');
            var $tile2Label = $('<div class="vas-adjwisecount-tile-label">Quantity Difference</div>');
            $qtyDiffMeta = $('<div class="vas-adjwisecount-tile-meta">Loading...</div>');
            $tile2Left.append($tile2Label).append($qtyDiffMeta);
            $qtyDiffVal = $('<div class="vas-adjwisecount-tile-val-amber">--</div>');
            $tile2.append($tile2Left).append($qtyDiffVal);

            $body.append($tile1).append($tile2);
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
                loadSummary();
            });

            $yearSelect.on('change', function () {
                selectedYear = parseInt($(this).val(), 10);
                loadSummary();
            });

            // Tile Click Handlers
            $tile1.on('click', function () {
                openDetailModal('AS_ON_DATE', 'As on Date Count', $tile1);
            });

            $tile1.on('keydown', function (e) {
                if (e.key === 'Enter' || e.key === ' ') {
                    e.preventDefault();
                    openDetailModal('AS_ON_DATE', 'As on Date Count', $tile1);
                }
            });

            $tile2.on('click', function () {
                openDetailModal('QTY_DIFF', 'Quantity Difference', $tile2);
            });

            $tile2.on('keydown', function (e) {
                if (e.key === 'Enter' || e.key === ' ') {
                    e.preventDefault();
                    openDetailModal('QTY_DIFF', 'Quantity Difference', $tile2);
                }
            });
        }

        function loadYears() {
            $.ajax({
                url: VIS.Application.contextUrl + "VAS_162_AdjustmentWiseCountWidget/GetAvailableYears",
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
                    console.error("VAS_162_AdjustmentWiseCountWidget: Error loading years", err);
                    loadSummary();
                }
            });
        }

        function loadSummary() {
            $.ajax({
                url: VIS.Application.contextUrl + "VAS_162_AdjustmentWiseCountWidget/GetSummary?month=" + selectedMonth + "&year=" + selectedYear,
                type: "GET",
                dataType: "json",
                success: function (res) {
                    if (res) {
                        var asOnCount = res.asOnDateRecordCount || 0;
                        var qtyDiffCount = res.qtyDiffRecordCount || 0;
                        var netDiff = res.netDiffQty || 0;

                        $asOnCountVal.text(asOnCount);
                        if (asOnCount === 0) {
                            $asOnCountMeta.text("No adjustments this period");
                        } else {
                            $asOnCountMeta.text(asOnCount + " adjustments recorded");
                        }

                        $qtyDiffVal.text(qtyDiffCount);
                        if (qtyDiffCount === 0) {
                            $qtyDiffMeta.text("No adjustments this period");
                        } else {
                            var signPrefix = netDiff > 0 ? "+" : "";
                            $qtyDiffMeta.text("Net diff qty: " + signPrefix + netDiff);
                        }
                    }
                },
                error: function (err) {
                    console.error("VAS_162_AdjustmentWiseCountWidget: Error loading summary", err);
                    $asOnCountVal.text("0");
                    $asOnCountMeta.text("No adjustments this period");
                    $qtyDiffVal.text("0");
                    $qtyDiffMeta.text("No adjustments this period");
                }
            });
        }

        function openDetailModal(typeCode, typeTitle, $originTile) {
            if ($modalOverlay) {
                $modalOverlay.remove();
            }

            var months = ["January", "February", "March", "April", "May", "June", "July", "August", "September", "October", "November", "December"];
            var monthName = months[selectedMonth - 1] || "";

            var $overlay = $('<div class="vas-adjwisecount-modal-overlay">');
            var $dialog = $('<div class="vas-adjwisecount-modal-dialog" role="dialog" aria-modal="true">');

            // Modal Header Chrome
            var $header = $('<div class="vas-adjwisecount-modal-header">');
            var $headerLeft = $('<div class="vas-adjwisecount-modal-header-left">');
            var $title = $('<h3 class="vas-adjwisecount-modal-title">' + typeTitle + '</h3>');
            var $subtitle = $('<span class="vas-adjwisecount-modal-subtitle">' + monthName + ' ' + selectedYear + '</span>');
            $headerLeft.append($title).append($subtitle);

            var $closeBtn = $('<button type="button" class="vas-adjwisecount-modal-close" aria-label="Close modal">&times;</button>');
            $header.append($headerLeft).append($closeBtn);
            $dialog.append($header);

            // Modal Body
            var $body = $('<div class="vas-adjwisecount-modal-body">');
            $body.html('<div class="vas-adjwisecount-message">Loading details...</div>');
            $dialog.append($body);

            $overlay.append($dialog);
            $('body').append($overlay);
            $modalOverlay = $overlay;

            $closeBtn.focus();

            var closeModal = function () {
                $overlay.remove();
                $modalOverlay = null;
                if ($originTile) {
                    $originTile.focus();
                }
            };

            $closeBtn.on('click', closeModal);
            $overlay.on('click', function (e) {
                if ($(e.target).hasClass('vas-adjwisecount-modal-overlay')) {
                    closeModal();
                }
            });

            $(document).off('keydown.vas-adjwisecount').on('keydown.vas-adjwisecount', function (e) {
                if (e.key === 'Escape' && $modalOverlay) {
                    closeModal();
                }
            });

            // Fetch Detail Lines
            $.ajax({
                url: VIS.Application.contextUrl + "VAS_162_AdjustmentWiseCountWidget/GetDetails?type=" + typeCode + "&month=" + selectedMonth + "&year=" + selectedYear,
                type: "GET",
                dataType: "json",
                success: function (res) {
                    if (res && res.details && res.details.length > 0) {
                        $subtitle.text(monthName + ' ' + selectedYear + ' · ' + res.details.length + ' records');
                        renderDetailGrid($body, res.details);
                    } else {
                        $body.html('<div class="vas-adjwisecount-message">No adjustments for the selected period.</div>');
                    }
                },
                error: function (err) {
                    console.error("VAS_162_AdjustmentWiseCountWidget: Error loading details", err);
                    $body.html('<div class="vas-adjwisecount-message">Unable to load details.</div>');
                }
            });
        }

        function renderDetailGrid($body, details) {
            $body.empty();

            var $headerRow = $(
                '<div class="vas-adjwisecount-grid-row vas-adjwisecount-header-row">' +
                '<div class="vas-adjwisecount-th">Product</div>' +
                '<div class="vas-adjwisecount-th">Attribute</div>' +
                '<div class="vas-adjwisecount-th">Qty</div>' +
                '<div class="vas-adjwisecount-th">Diff Qty</div>' +
                '<div class="vas-adjwisecount-th">As on Date Count</div>' +
                '</div>'
            );
            $body.append($headerRow);

            for (var i = 0; i < details.length; i++) {
                var item = details[i];
                var diffVal = item.diffQty || 0;
                var diffClass = "vas-adjwisecount-cell-num";
                var diffText = diffVal;

                if (diffVal > 0) {
                    diffClass = "vas-adjwisecount-diff-pos";
                    diffText = "+" + diffVal;
                } else if (diffVal < 0) {
                    diffClass = "vas-adjwisecount-diff-neg";
                    diffText = diffVal;
                }

                var $row = $(
                    '<div class="vas-adjwisecount-grid-row vas-adjwisecount-data-row">' +
                    '<div class="vas-adjwisecount-cell vas-adjwisecount-cell-product" title="' + item.product + '">' + item.product + '</div>' +
                    '<div class="vas-adjwisecount-cell vas-adjwisecount-cell-attr" title="' + item.attribute + '">' + item.attribute + '</div>' +
                    '<div class="vas-adjwisecount-cell vas-adjwisecount-cell-num" title="' + item.qty + '">' + item.qty + '</div>' +
                    '<div class="vas-adjwisecount-cell ' + diffClass + '" title="' + diffText + '">' + diffText + '</div>' +
                    '<div class="vas-adjwisecount-cell vas-adjwisecount-cell-num" title="' + item.asOnDateCount + '">' + item.asOnDateCount + '</div>' +
                    '</div>'
                );
                $body.append($row);
            }
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

    VAS.VAS_162_AdjustmentWiseCountWidget.prototype.init = function (windowNo, frame) {
        this.frame = frame;
        this.AD_UserHomeWidgetID = frame.widgetInfo.AD_UserHomeWidgetID;
        this.windowNo = windowNo;
        this.Initalize();
        this.frame.getContentGrid().append(this.getRoot());
        ensureDashInlineSizeVar(this.getRoot());
    };

    VAS.VAS_162_AdjustmentWiseCountWidget.prototype.widgetSizeChange = function (height, width) { };

    VAS.VAS_162_AdjustmentWiseCountWidget.prototype.refreshWidget = function () {
        this.refreshData();
    };

    VAS.VAS_162_AdjustmentWiseCountWidget.prototype.dispose = function () {
        this.disposeComponent();
        if (this.frame) { this.frame.dispose(); }
        this.frame = null;
    };

})(VAS, jQuery);
