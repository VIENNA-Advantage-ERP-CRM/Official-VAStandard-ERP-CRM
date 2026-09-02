/**
 * Location Wise Inventory Count Widget & Detail Modal (Inventory Count Dashboard)
 * Purpose - 3x2 glass card showing location-wise inventory counts and counted quantities.
 *           Clicking a row opens a detailed breakdown modal of all count lines for that location.
 * Prefix  - VAS_165_
 *
 * Labels / Message Keys Table:
 *  #  | Fallback Text                                    | Message Key
 * ----+--------------------------------------------------+-----------------------------------
 *  1  | Location Wise Inventory Count                    | VAS_165_LocationWiseInventoryCount
 *  2  | Location Code                                    | VAS_165_LocationCode
 *  3  | Warehouse Name                                   | VAS_165_WarehouseName
 *  4  | Count Sessions                                   | VAS_165_CountSessions
 *  5  | Qty Counted                                      | VAS_165_QtyCounted
 *  6  | Product Name & Variant                           | VAS_165_ProductNameVariant
 *  7  | Inventory Type                                   | VAS_165_InventoryType
 *  8  | Quantity                                         | VAS_165_Quantity
 *  9  | Total Items                                      | VAS_165_TotalItems
 * 10  | Sessions                                         | VAS_165_Sessions
 * 11  | Page                                             | VAS_165_Page
 * 12  | of                                               | VAS_165_Of
 * 13  | Close                                            | VAS_165_Close
 * 14  | No location count records found                  | VAS_165_NoLocationCountRecords
 * 15  | Unable to load location count summary            | VAS_165_UnableToLoadLocationSummary
 * 16  | Locator                                          | M_Locator_ID (global VA element key)
 */
; VAS = window.VAS || {};

; (function (VAS, $) {

    /* Same helper as VAS_156/159/160/161/162. VIS.Msg.getMsg returns the bracketed key
       ("[M_Locator_ID]") - not null - when the AD_Message row is missing, so the fallback has to
       test for the leading '[' rather than rely on a null check. M_Locator_ID is the global VA
       element key for the word "Locator" and already exists in AD_Message. */
    function lbl(key, fallback) {
        var t = VIS.Msg.getMsg(key);
        return (t && t.charAt(0) !== '[') ? t : fallback;
    }

    /* Copied verbatim from VAS_159/VAS_160. Locator names are free-text database values that get
       concatenated into innerHTML, so an unescaped quote or angle bracket in the data would break
       the markup. */
    function escapeHtml(value) {
        return String(value == null ? "" : value)
            .replace(/&/g, "&amp;")
            .replace(/</g, "&lt;")
            .replace(/>/g, "&gt;")
            .replace(/"/g, "&quot;")
            .replace(/'/g, "&#039;");
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

    VAS.VAS_165_LocationWiseInventoryCountWidget = function () {

        this.frame;
        this.windowNo;
        this.AD_UserHomeWidgetID = 0;

        var $self = this;
        var $wrapper = $('<div class="vas-locwisecount-container">');
        var $root = $('<div class="vas-locwisecount-root">');
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

        /* CARD page size. Starts at MODAL_PAGE_ROWS only so the first paint has a row to measure;
           from then on it is whatever actually fits (see summaryRowsThatFit). */
        var pageSize = MODAL_PAGE_ROWS;

        /* Height of one rendered card row, px. Cached; cleared by the ResizeObserver, which must
           also fire on WIDTH changes - row font-size is driven by --widget-inline-size. */
        var measuredRowHeight = 0;
        /* Re-entrancy guard: the repaint mutates the subtree the ResizeObserver watches. */
        var refittingRows = false;
        var widgetObserver = null;
        var $modalOverlay = null;

        this.Initalize = function () {
            createWidget();
            loadYears();
        };

        function createWidget() {
            // Header Row
            var $headerRow = $('<div class="vas-locwisecount-header-row">');

            var $leftCluster = $('<div class="vas-locwisecount-left-cluster">');
            var $iconWell = $('<div class="vas-locwisecount-icon-well"><i class="fa fa-map-marker"></i></div>');
            var $titleBlock = $('<div class="vas-locwisecount-title-block">');
            var $title = $('<h3 class="vas-locwisecount-title">Location Wise Count</h3>');
            $subtitle = $('<span class="vas-locwisecount-subtitle">Locations with active inventory counts</span>');
            $titleBlock.append($title).append($subtitle);
            $leftCluster.append($iconWell).append($titleBlock);

            var $filterCluster = $('<div class="vas-locwisecount-filter-cluster">');
            $monthSelect = $('<select class="vas-locwisecount-select">');
            $yearSelect = $('<select class="vas-locwisecount-select">');

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
            var $body = $('<div class="vas-locwisecount-body">');
            var $headerGrid = $(
                '<div class="vas-locwisecount-grid-template vas-locwisecount-header-row">' +
                '<div class="vas-locwisecount-th">Locator</div>' +
                '<div class="vas-locwisecount-th">Warehouse</div>' +
                '<div class="vas-locwisecount-th vas-locwisecount-th-right">Counts</div>' +
                '<div class="vas-locwisecount-th vas-locwisecount-th-right">Qty Counted</div>' +
                '</div>'
            );
            $body.append($headerGrid);

            $rowsContainer = $('<div class="vas-locwisecount-rows-container">');
            $body.append($rowsContainer);

            // Footer / Pager Band
            $footer = $(
                '<div class="vas-locwisecount-footer">' +
                '<div class="vas-locwisecount-footer-text"></div>' +
                '<div class="vas-locwisecount-pager">' +
                '<button type="button" class="vas-locwisecount-pager-btn vas-prev" aria-label="Previous page">&lsaquo;</button>' +
                '<span class="vas-locwisecount-pager-info"></span>' +
                '<button type="button" class="vas-locwisecount-pager-btn vas-next" aria-label="Next page">&rsaquo;</button>' +
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

                    /* Row height follows the widget's size, so drop the cached measurement and
                       repaint at the new fit. The guard is required because this repaint mutates
                       the observed subtree. */
                    if (refittingRows) { return; }
                    if (!summaryData || summaryData.length === 0) { return; }

                    refittingRows = true;
                    try {
                        measuredRowHeight = 0;
                        renderSummaryPage();
                    } finally {
                        refittingRows = false;
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
                url: VIS.Application.contextUrl + "VAS_165_LocationWiseInventoryCountWidget/GetAvailableYears",
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
                    console.error("VAS_165_LocationWiseInventoryCountWidget: Error loading years", err);
                    loadSummary();
                }
            });
        }

        function loadSummary() {
            var monthsFull = ["January", "February", "March", "April", "May", "June", "July", "August", "September", "October", "November", "December"];
            var mName = monthsFull[selectedMonth - 1] || "";
            $subtitle.text("Locations with active inventory counts · " + mName + " " + selectedYear);

            $.ajax({
                url: VIS.Application.contextUrl + "VAS_165_LocationWiseInventoryCountWidget/GetLocationSummary?month=" + selectedMonth + "&year=" + selectedYear,
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
                    console.error("VAS_165_LocationWiseInventoryCountWidget: Error loading summary", err);
                    summaryData = [];
                    renderSummaryPage();
                }
            });
        }

        function appendFillerRows($rowsContainer, count) {
            for (var f = 0; f < count; f++) {
                $rowsContainer.append(
                    '<div class="vas-locwisecount-row-btn vas-locwisecount-grid-template vas-locwisecount-filler" aria-hidden="true">' +
                    '<div class="vas-locwisecount-cell">&nbsp;</div>' +
                    '<div class="vas-locwisecount-cell">&nbsp;</div>' +
                    '<div class="vas-locwisecount-cell">&nbsp;</div>' +
                    '<div class="vas-locwisecount-cell">&nbsp;</div>' +
                    '</div>'
                );
            }
        }

        /* How many summary rows actually fit the card at its current size.

           The CARD's height comes from the dashboard grid, so it can be measured. The detail
           POPUP is the opposite case - its height comes from its row count, so measuring there
           would be circular; MODAL_PAGE_ROWS still governs the popup and is unchanged.

           The card was reusing that same fixed 7 while only about five rows physically fit, so the
           footer read "1-7 of 12" against five visible rows (reported 2026-08-29). Identical
           defect and identical fix to VAS_161. */
        function summaryRowsThatFit() {
            var el = $rowsContainer && $rowsContainer[0];
            if (!el) { return MODAL_PAGE_ROWS; }

            var available = el.clientHeight;
            if (!available) { return MODAL_PAGE_ROWS; }

            if (!measuredRowHeight) {
                /* Probe a REAL row - filler rows share .vas-locwisecount-row-btn. */
                var probe = el.querySelector('.vas-locwisecount-row-btn:not(.vas-locwisecount-filler)');
                if (probe) {
                    var h = probe.getBoundingClientRect().height;
                    if (h > 0) { measuredRowHeight = h; }
                }
            }

            if (!measuredRowHeight) { return MODAL_PAGE_ROWS; }

            /* Half a pixel of slack absorbs sub-pixel row heights. */
            return Math.max(1, Math.floor((available + 0.5) / measuredRowHeight));
        }

        function renderSummaryPage(isRefit) {
            $rowsContainer.empty();

            if (!summaryData || summaryData.length === 0) {
                $rowsContainer.html('<div class="vas-locwisecount-message">No location counts recorded for this period. Pick another month to review earlier counts.</div>');
                appendFillerRows($rowsContainer, Math.max(0, pageSize - 1));
                $footer.find('.vas-locwisecount-footer-text').text('Showing 0 of 0');
                $footer.find('.vas-locwisecount-pager').hide();
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

                /* Display the locator NAME. item.locator remains the CODE and is what
                   openDetailModal sends back as locatorCode - the endpoint filters on loc.Value,
                   so displaying the name must not change what is sent. */
                var locName = item.locatorName || item.locator;

                var $row = $(
                    '<button type="button" class="vas-locwisecount-row-btn vas-locwisecount-grid-template" aria-label="Open count details for locator ' + locName + '">' +
                    '<div class="vas-locwisecount-cell vas-locwisecount-loc-title" title="' + locName + '">' + locName + '</div>' +
                    '<div class="vas-locwisecount-cell vas-locwisecount-wh-text" title="' + item.warehouse + '">' + item.warehouse + '</div>' +
                    '<div class="vas-locwisecount-cell vas-locwisecount-counts-num" title="' + item.sessionCount + '">' + item.sessionCount + '</div>' +
                    '<div class="vas-locwisecount-cell vas-locwisecount-qty-num" title="' + formattedQty + '">' + formattedQty + '</div>' +
                    '</button>'
                );

                (function (locItem, $r) {
                    $r.on('click', function () {
                        openDetailModal(locItem, $r);
                    });
                })(item, $row);

                $rowsContainer.append($row);
            }

            /* Rows are on screen now, so one can be measured. If the fit differs from what was
               just rendered, adopt it and repaint once. isRefit stops the second pass from
               measuring again, so this cannot loop. */
            if (!isRefit) {
                var fit = summaryRowsThatFit();
                if (fit !== pageSize) {
                    pageSize = fit;
                    var refitPages = Math.ceil(totalItems / pageSize) || 1;
                    if (currentPage > refitPages) { currentPage = refitPages; }
                    renderSummaryPage(true);
                    return;
                }
            }

            appendFillerRows($rowsContainer, Math.max(0, pageSize - pageItems.length));

            var $footerText = $footer.find('.vas-locwisecount-footer-text');
            var $pagerInfo = $footer.find('.vas-locwisecount-pager-info');
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
                $footer.find('.vas-locwisecount-pager').hide();
            } else {
                $footer.find('.vas-locwisecount-pager').show();
            }
        }

        function openDetailModal(locItem, $originBtn) {
            if ($modalOverlay) {
                $modalOverlay.remove();
            }

            var monthsFull = ["January", "February", "March", "April", "May", "June", "July", "August", "September", "October", "November", "December"];
            var monthName = monthsFull[selectedMonth - 1] || "";

            var $overlay = $('<div class="vas-locwisecount-modal-overlay">');
            var $dialog = $('<div class="vas-locwisecount-modal-dialog" role="dialog" aria-modal="true">');

            // Modal Header Chrome
            var $header = $('<div class="vas-locwisecount-modal-header">');
            var $headerLeft = $('<div class="vas-locwisecount-modal-header-left">');
            /* Show the locator NAME, using the SAME fallback the card row uses (see
               renderSummaryPage). locItem.locator stays the CODE and is still what the detail
               request below sends as locatorCode - only the DISPLAY changes. 3 of 31 locators on
               DB 2 have a NULL LocatorCombination, so the fallback to the code is load-bearing:
               without it those headings would read "Locator" with nothing after it. */
            var locName = escapeHtml(locItem.locatorName || locItem.locator);
            var $title = $('<h3 class="vas-locwisecount-modal-title" title="' + locName + '">' + escapeHtml(lbl('M_Locator_ID', 'Locator')) + ' ' + locName + '</h3>');
            var $subtitle = $('<span class="vas-locwisecount-modal-subtitle">' + locItem.warehouse + ' · ' + monthName + ' ' + selectedYear + '</span>');
            $headerLeft.append($title).append($subtitle);

            var $closeBtn = $('<button type="button" class="vas-locwisecount-modal-close" aria-label="Close modal">&times;</button>');
            $header.append($headerLeft).append($closeBtn);
            $dialog.append($header);

            // Modal Body
            var $body = $('<div class="vas-locwisecount-modal-body">');
            $body.html('<div class="vas-locwisecount-message">Loading location details...</div>');
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
                if ($(e.target).hasClass('vas-locwisecount-modal-overlay')) {
                    closeModal();
                }
            });

            $(document).off('keydown.vas-locwisecount').on('keydown.vas-locwisecount', function (e) {
                if (e.key === 'Escape' && $modalOverlay) {
                    closeModal();
                }
            });

            // Fetch Detail Lines
            $.ajax({
                url: VIS.Application.contextUrl + "VAS_165_LocationWiseInventoryCountWidget/GetLocationDetail?month=" + selectedMonth + "&year=" + selectedYear + "&locatorCode=" + encodeURIComponent(locItem.locator),
                type: "GET",
                dataType: "json",
                success: function (res) {
                    if (res && res.lines && res.lines.length > 0) {
                        $subtitle.text(locItem.warehouse + ' · ' + monthName + ' ' + selectedYear + ' · ' + res.lines.length + ' records');
                        renderModalGrid($body, res);
                    } else {
                        $body.html('<div class="vas-locwisecount-message">No count lines found for this location.</div>');
                    }
                },
                error: function (err) {
                    console.error("VAS_165_LocationWiseInventoryCountWidget: Error loading detail", err);
                    $body.html('<div class="vas-locwisecount-message">Unable to load count lines.</div>');
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
                '<div class="vas-locwisecount-modal-grid-template vas-locwisecount-header-row">' +
                '<div class="vas-locwisecount-th">Product / Attribute</div>' +
                '<div class="vas-locwisecount-th">Locator</div>' +
                '<div class="vas-locwisecount-th">Inventory Type</div>' +
                '<div class="vas-locwisecount-th vas-locwisecount-th-right">Qty</div>' +
                '</div>'
            );
            $body.append($headerGrid);

            var $modalRowsContainer = $('<div class="vas-locwisecount-modal-rows-container"></div>');
            $body.append($modalRowsContainer);

            var $footer = $(
                '<div class="vas-locwisecount-modal-footer">' +
                '<div class="vas-locwisecount-modal-footer-left">' +
                '<span class="vas-locwisecount-footer-text vas-modal-helper"></span>' +
                '<span class="vas-locwisecount-modal-total-qty">Total qty ' + Number(totalQty).toLocaleString() + '</span>' +
                '</div>' +
                '<div class="vas-locwisecount-pager">' +
                '<button type="button" class="vas-locwisecount-pager-btn vas-m-prev" aria-label="Previous page">&lsaquo;</button>' +
                '<span class="vas-locwisecount-pager-info vas-m-info"></span>' +
                '<button type="button" class="vas-locwisecount-pager-btn vas-m-next" aria-label="Next page">&rsaquo;</button>' +
                '</div>' +
                '</div>'
            );
            $body.append($footer);

            function appendModalFillerRows($container, count) {
                for (var f = 0; f < count; f++) {
                    $container.append(
                        '<div class="vas-locwisecount-modal-grid-template vas-locwisecount-modal-data-row vas-locwisecount-filler" aria-hidden="true">' +
                        '<div class="vas-locwisecount-cell"><div class="vas-locwisecount-prod-title">&nbsp;</div>' +
                        '<div class="vas-locwisecount-prod-attr">&nbsp;</div></div>' +
                        '<div class="vas-locwisecount-cell">&nbsp;</div>' +
                        '<div class="vas-locwisecount-cell">&nbsp;</div>' +
                        '<div class="vas-locwisecount-cell">&nbsp;</div>' +
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
                    var chipClass = isCharge ? 'vas-locwisecount-chip-neutral' : 'vas-locwisecount-chip-info';

                    var $mRow = $(
                        '<div class="vas-locwisecount-modal-grid-template vas-locwisecount-modal-data-row">' +
                        '<div class="vas-locwisecount-cell">' +
                        '<div class="vas-locwisecount-prod-title" title="' + line.product + '">' + line.product + '</div>' +
                        /* Blank when the line has no attribute - the controller no longer
                           substitutes "Standard", which read as a product category. The element
                           is still rendered so the row keeps its height. */
                        '<div class="vas-locwisecount-prod-attr" title="' + (line.attribute || '') + '">' + (line.attribute || '&nbsp;') + '</div>' +
                        '</div>' +
                        '<div class="vas-locwisecount-cell vas-locwisecount-wh-text" title="' + line.locator + '">' + line.locator + '</div>' +
                        '<div class="vas-locwisecount-cell"><span class="' + chipClass + '">' + line.inventoryType + '</span></div>' +
                        '<div class="vas-locwisecount-cell vas-locwisecount-qty-num" title="' + formattedQty + '">' + formattedQty + '</div>' +
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
                    $footer.find('.vas-locwisecount-pager').hide();
                } else {
                    $footer.find('.vas-locwisecount-pager').show();
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

    VAS.VAS_165_LocationWiseInventoryCountWidget.prototype.init = function (windowNo, frame) {
        this.frame = frame;
        this.AD_UserHomeWidgetID = frame.widgetInfo.AD_UserHomeWidgetID;
        this.windowNo = windowNo;
        this.Initalize();
        this.frame.getContentGrid().append(this.getRoot());
        ensureDashInlineSizeVar(this.getRoot());
    };

    VAS.VAS_165_LocationWiseInventoryCountWidget.prototype.widgetSizeChange = function (height, width) { };

    VAS.VAS_165_LocationWiseInventoryCountWidget.prototype.refreshWidget = function () {
        this.refreshData();
    };

    VAS.VAS_165_LocationWiseInventoryCountWidget.prototype.dispose = function () {
        this.disposeComponent();
        if (this.frame) { this.frame.dispose(); }
        this.frame = null;
    };

})(VAS, jQuery);
