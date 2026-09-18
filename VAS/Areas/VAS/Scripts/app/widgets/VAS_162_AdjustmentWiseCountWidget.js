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

    /* Lines the popup holds before paging. MUST stay in step with --vas-rows in
       VAS_162_AdjustmentWiseCountWidget.css, which sizes the dialog to exactly this many rows. */
    var MODAL_PAGE_SIZE = 7;
    /* Window name / search key for the Inventory Count screen - the same target VAS_156 and
       VAS_158 navigate to. Window IDs differ per installation, so it is resolved by NAME.
       Hardcoding an id is what made VAS_160 answer "With your current role and settings, you
       cannot view this information." */
    var COUNT_WINDOW_NAME = "VAS_PhysicalInventory";

    /* Product and attribute text comes from the database and was previously concatenated straight
       into innerHTML. The source prompt requires the opposite: "Render database text through
       textContent or equivalent safe DOM APIs. Do not insert product or attribute text with
       unsanitized innerHTML." Escaping every interpolated value satisfies that without restructuring
       the grid builder. */
    function esc(value) {
        return String(value == null ? "" : value)
            .replace(/&/g, "&amp;")
            .replace(/</g, "&lt;")
            .replace(/>/g, "&gt;")
            .replace(/"/g, "&quot;")
            .replace(/'/g, "&#039;");
    }

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
        var $modalOriginTile = null;

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

        /* Modal teardown. Lifted out of openDetailModal's closure so the document-number
       handlers created in renderDetailGrid - a sibling function - can close the popup after
       navigating. openDetailModal's local closeModal now just delegates here. */
        function closeDetailModal() {
            if (!$modalOverlay) { return; }

            $modalOverlay.remove();
            $modalOverlay = null;

            if ($modalOriginTile) {
                $modalOriginTile.focus();
            }
        }

        /* The framework navigates IN-PLACE only when the payload's ActionName equals the name of
           the window currently HOSTING this widget; otherwise VIS.dynamicWidget resolves
           ActionName through UserPreference/GetWindowID and opens that window. Resolve the host
           name from the listener chain. Established pattern - VAS_156 / VAS_158. */
        function hostWindowName() {
            try {
                var l = $self.listener;
                for (var i = 0; i < 6 && l; i++) {
                    if (l.apanel && l.apanel.gridWindow && l.apanel.gridWindow.getName) {
                        return l.apanel.gridWindow.getName();
                    }
                    if (l.gridWindow && l.gridWindow.getName) { return l.gridWindow.getName(); }
                    l = l.listener;
                }
            } catch (e) { }
            return '';
        }

        /* Open the Inventory Count screen on the clicked document.

           The id comes from the row (M_Inventory_ID), not from the document number text. Several
           adjustment lines share one document, and M_Inventory_ID is the key the navigation
           restriction needs - re-deriving it from the visible text would cost a second lookup and
           lean on a DocumentNo uniqueness the schema does not enforce.

           The navigation happens on the screen behind the popup, so the popup is closed straight
           after - otherwise it sits on top of the record it just opened. */
        function openInventoryWindow(inventoryId) {
            if (!inventoryId) { return; }

            $self.widgetFirevalueChanged({
                "TabWhereClause": "M_Inventory.M_Inventory_ID=" + Number(inventoryId),
                "TabLayout": "Y",   /* 'N' Grid, 'Y' Single, 'C' Card */
                "TabIndex": "0",
                "ActionName": hostWindowName() || COUNT_WINDOW_NAME,
                "ActionType": "W"
            });

            closeDetailModal();
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
            $modalOriginTile = $originTile || null;

            $closeBtn.focus();

            var closeModal = function () {
                closeDetailModal();
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
                        // Render the grid with no rows rather than replacing the body: the popup
                        // keeps its fixed height instead of collapsing to a single message line.
                        renderDetailGrid($body, []);
                    }
                },
                error: function (err) {
                    console.error("VAS_162_AdjustmentWiseCountWidget: Error loading details", err);
                    $body.html('<div class="vas-adjwisecount-message">Unable to load details.</div>');
                }
            });
        }

        /* Modal grid, paginated. The endpoint returns the whole result set, so paging is applied
           here on the client - consistent with the other Inventory Count modals (VAS_156, VAS_158).
           The source prompt describes server-side paging via :p_offset / :p_page_size; that would
           need a new endpoint signature and is noted as a follow-up rather than done here.

           Document No. is the added sixth column. The prompt says "Do not add warehouse, document
           number, price, cost, amount, user, or other columns... Keep the approved five-column
           design" - the user asked for it explicitly on 2026-08-16, so it is included. */
        function renderDetailGrid($body, details) {
            $body.empty();

            var pageNo = 1;
            var pageSize = MODAL_PAGE_SIZE;
            var total = details.length;
            var totalPages = Math.max(1, Math.ceil(total / pageSize));

            var $grid = $('<div class="vas-adjwisecount-grid">');
            var $headerRow = $(
                '<div class="vas-adjwisecount-grid-row vas-adjwisecount-header-row">' +
                '<div class="vas-adjwisecount-th">' + esc(lbl("VAS_DocNo", "Document No")) + '</div>' +
                '<div class="vas-adjwisecount-th">' + esc(lbl("VAS_Product", "Product")) + '</div>' +
                '<div class="vas-adjwisecount-th">' + esc(lbl("VAS_Attribute", "Attribute")) + '</div>' +
                '<div class="vas-adjwisecount-th">' + esc(lbl("VAS_Qty", "Qty")) + '</div>' +
                '<div class="vas-adjwisecount-th">' + esc(lbl("VAS_DiffQty", "Diff Qty")) + '</div>' +
                '<div class="vas-adjwisecount-th">' + esc(lbl("VAS_AsOnDateCount", "As on Date Count")) + '</div>' +
                '</div>'
            );
            var $rows = $('<div class="vas-adjwisecount-rows">');
            $grid.append($headerRow).append($rows);
            $body.append($grid);

            var $footer = $(
                '<div class="vas-adjwisecount-modal-footer">' +
                '<div class="vas-adjwisecount-footer-text"></div>' +
                '<div class="vas-adjwisecount-pager">' +
                '<button type="button" class="vas-adjwisecount-pager-btn vas-adjwisecount-prev" aria-label="' + esc(lbl("VAS_Previous", "Previous")) + '">&lsaquo;</button>' +
                '<span class="vas-adjwisecount-pager-info">1 / 1</span>' +
                '<button type="button" class="vas-adjwisecount-pager-btn vas-adjwisecount-next" aria-label="' + esc(lbl("VAS_Next", "Next")) + '">&rsaquo;</button>' +
                '</div>' +
                '</div>'
            );
            $body.append($footer);

            /* Invisible spacers so a short page occupies the same height as a full one. The
               dialog height is fixed by the CSS row arithmetic, so these only keep the divider
               rhythm - but without them the last page shows a bare gap under the final row. */
            function appendFillerRows($container, count) {
                for (var f = 0; f < count; f++) {
                    $container.append(
                        '<div class="vas-adjwisecount-grid-row vas-adjwisecount-data-row vas-adjwisecount-filler" aria-hidden="true">' +
                        '<div class="vas-adjwisecount-cell">&nbsp;</div>' +
                        '<div class="vas-adjwisecount-cell">&nbsp;</div>' +
                        '<div class="vas-adjwisecount-cell">&nbsp;</div>' +
                        '<div class="vas-adjwisecount-cell">&nbsp;</div>' +
                        '<div class="vas-adjwisecount-cell">&nbsp;</div>' +
                        '<div class="vas-adjwisecount-cell">&nbsp;</div>' +
                        '</div>'
                    );
                }
            }

            function paint() {
                $rows.empty();

                if (total === 0) {
                    $rows.append('<div class="vas-adjwisecount-message">' +
                        esc(lbl("VAS_162_NoAdjustments", "No adjustments for the selected period.")) +
                        '</div>');
                    appendFillerRows($rows, Math.max(0, pageSize - 1));
                    $footer.find('.vas-adjwisecount-footer-text').text('');
                    $footer.find('.vas-adjwisecount-pager').hide();
                    return;
                }

                var start = (pageNo - 1) * pageSize;
                var end = Math.min(total, start + pageSize);

                for (var i = start; i < end; i++) {
                    var item = details[i];
                    var diffVal = Number(item.diffQty || 0);
                    var diffClass = "vas-adjwisecount-cell-num";
                    var diffText = String(diffVal);

                    if (diffVal > 0) {
                        diffClass = "vas-adjwisecount-diff-pos";
                        diffText = "+" + diffVal;
                    } else if (diffVal < 0) {
                        diffClass = "vas-adjwisecount-diff-neg";
                    }

                    // Attribute stays blank when the line has none - no "Standard" placeholder.
                    var attr = item.attribute || "";

                    var $row = $(
                        '<div class="vas-adjwisecount-grid-row vas-adjwisecount-data-row">' +
                        '<div class="vas-adjwisecount-cell vas-adjwisecount-cell-doc vas-adjwisecount-doclink" role="link" tabindex="0" title="' + esc(item.documentNo) + '">' + esc(item.documentNo) + '</div>' +
                        '<div class="vas-adjwisecount-cell vas-adjwisecount-cell-product" title="' + esc(item.product) + '">' + esc(item.product) + '</div>' +
                        '<div class="vas-adjwisecount-cell vas-adjwisecount-cell-attr" title="' + esc(attr) + '">' + esc(attr) + '</div>' +
                        '<div class="vas-adjwisecount-cell vas-adjwisecount-cell-num" title="' + esc(item.qty) + '">' + esc(item.qty) + '</div>' +
                        '<div class="vas-adjwisecount-cell ' + diffClass + '" title="' + esc(diffText) + '">' + esc(diffText) + '</div>' +
                        '<div class="vas-adjwisecount-cell vas-adjwisecount-cell-num" title="' + esc(item.asOnDateCount) + '">' + esc(item.asOnDateCount) + '</div>' +
                        '</div>'
                    );

                    (function ($r, record) {
                        $r.find('.vas-adjwisecount-doclink').on('click keydown', function (e) {
                            if (e.type === 'click' || e.key === 'Enter' || e.key === ' ') {
                                e.preventDefault();
                                openInventoryWindow(record.inventoryId);
                            }
                        });
                    })($row, item);

                    $rows.append($row);
                }

                // Hold the popup at MODAL_PAGE_SIZE lines regardless of what this page holds.
                appendFillerRows($rows, Math.max(0, pageSize - (end - start)));

                $footer.find('.vas-adjwisecount-footer-text').text(
                    total === 0 ? '' : (start + 1) + '–' + end + ' ' + lbl("VAS_Of", "of") + ' ' + total);
                $footer.find('.vas-adjwisecount-pager-info').text(pageNo + ' / ' + totalPages);
                $footer.find('.vas-adjwisecount-prev').prop('disabled', pageNo <= 1);
                $footer.find('.vas-adjwisecount-next').prop('disabled', pageNo >= totalPages);
                $footer.find('.vas-adjwisecount-pager').toggle(totalPages > 1);
            }

            $footer.find('.vas-adjwisecount-prev').on('click', function () {
                if (pageNo > 1) { pageNo--; paint(); }
            });
            $footer.find('.vas-adjwisecount-next').on('click', function () {
                if (pageNo < totalPages) { pageNo++; paint(); }
            });

            paint();
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

    /* Required for the navigation channel: widgetFirevalueChanged is what the widget calls,
       addChangeListener is how the host registers itself. VAS_162 had neither, so even a correct
       payload had nowhere to go. Same gap VAS_158 and VAS_160 had. */
    VAS.VAS_162_AdjustmentWiseCountWidget.prototype.widgetFirevalueChanged = function (value) {
        if (this.listener) { this.listener.widgetFirevalueChanged(value); }
    };

    VAS.VAS_162_AdjustmentWiseCountWidget.prototype.addChangeListener = function (listener) {
        this.listener = listener;
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

