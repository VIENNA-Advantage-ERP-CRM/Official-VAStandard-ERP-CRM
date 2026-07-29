/**
 * Open Count Sheets KPI Widget & Modal (Inventory Count Dashboard)
 * Purpose - Read-only 2x1 glass KPI tile showing draft count sheets with modal breakdown.
 * Prefix  - VAS_158_
 *
 * Labels / Message Keys Table:
 *  #  | Fallback Text                                    | Message Key
 * ----+--------------------------------------------------+-----------------------------------
 *  1  | Open Count Sheets                                | VAS_158_OpenCountSheets
 *  2  | All count sheets posted or completed             | VAS_158_AllCountSheetsPosted
 *  3  | draft physical inventory count sheet             | VAS_158_DraftCountSheet
 *  4  | draft physical inventory count sheets            | VAS_158_DraftCountSheets
 *  5  | Document No                                      | VAS_158_DocumentNo
 *  6  | Warehouse                                        | VAS_158_Warehouse
 *  7  | Location                                         | VAS_158_Location
 *  8  | Lines                                            | VAS_158_Lines
 *  9  | Started                                          | VAS_158_Started
 * 10  | Status                                           | VAS_158_Status
 * 11  | Page                                             | VAS_158_Page
 * 12  | of                                               | VAS_158_Of
 * 13  | Close                                            | VAS_158_Close
 * 14  | No draft count sheets available                  | VAS_158_NoDraftCountSheets
 * 15  | Unable to load count sheets                      | VAS_158_UnableToLoadCountSheets
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

    VAS.VAS_158_OpenCountSheetsWidget = function () {

        this.frame;
        this.windowNo;
        this.AD_UserHomeWidgetID = 0;

        var $self = this;
        var $wrapper = $('<div class="vas-opencountsheets-container">');
        var $root = $('<div class="vas-opencountsheets-root" role="button" tabindex="0" aria-haspopup="dialog" aria-label="Open Count Sheets Widget">');
        var $valEl;
        var draftCount = 0;
        var draftList = [];
        var currentPage = 1;
        var pageSize = 8;
        var $modalOverlay = null;
        var widgetObserver = null;

        this.Initalize = function () {
            createWidget();
            loadData();
        };

        function createWidget() {
            var $label = $('<div class="vas-opencountsheets-label">Open Count Sheets</div>');
            $valEl = $('<div class="vas-opencountsheets-value">--</div>');
            var $meta = $('<div class="vas-opencountsheets-meta">Tap to view drafted counts</div>');

            $root.append($label).append($valEl).append($meta);
            $wrapper.append($root);

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

            $root.on('click', function () {
                openModal();
            });

            $root.on('keydown', function (e) {
                if (e.key === 'Enter' || e.key === ' ') {
                    e.preventDefault();
                    openModal();
                }
            });
        }

        function loadData() {
            $.ajax({
                url: VIS.Application.contextUrl + "VAS_158_OpenCountSheetsWidget/GetDraftCount",
                type: "GET",
                dataType: "json",
                success: function (res) {
                    if (res && typeof res.count !== 'undefined') {
                        draftCount = res.count;
                        if ($valEl) {
                            $valEl.text(draftCount);
                        }
                    }
                },
                error: function (err) {
                    console.error("VAS_158_OpenCountSheetsWidget: Error loading draft count", err);
                    if ($valEl) {
                        $valEl.text("0");
                    }
                }
            });
        }

        function openModal() {
            if ($modalOverlay) {
                $modalOverlay.remove();
            }

            var $overlay = $('<div class="vas-opencountsheets-modal-overlay">');
            var $dialog = $('<div class="vas-opencountsheets-modal-dialog" role="dialog" aria-modal="true" aria-labelledby="vas-opencountsheets-modal-title">');

            var $header = $('<div class="vas-opencountsheets-modal-header">');
            var $title = $('<h3 id="vas-opencountsheets-modal-title" class="vas-opencountsheets-modal-title">Drafted Count Sheets</h3>');
            var $closeBtn = $('<button type="button" class="vas-opencountsheets-modal-close" aria-label="Close modal">&times;</button>');

            $header.append($title).append($closeBtn);
            $dialog.append($header);

            var $body = $('<div class="vas-opencountsheets-modal-body">');
            $body.html('<div class="vas-opencountsheets-message">Loading drafted count sheets...</div>');
            $dialog.append($body);

            $overlay.append($dialog);
            $('body').append($overlay);
            $modalOverlay = $overlay;

            $closeBtn.focus();

            var closeModal = function () {
                $overlay.remove();
                $modalOverlay = null;
                if ($root) {
                    $root.focus();
                }
                loadData();
            };

            $closeBtn.on('click', closeModal);
            $overlay.on('click', function (e) {
                if ($(e.target).hasClass('vas-opencountsheets-modal-overlay')) {
                    closeModal();
                }
            });

            $(document).off('keydown.vas-opencountsheets').on('keydown.vas-opencountsheets', function (e) {
                if (e.key === 'Escape' && $modalOverlay) {
                    closeModal();
                }
            });

            $.ajax({
                url: VIS.Application.contextUrl + "VAS_158_OpenCountSheetsWidget/GetDraftSheetsList",
                type: "GET",
                dataType: "json",
                success: function (res) {
                    if (res && res.data) {
                        draftList = res.data;
                        currentPage = 1;
                        renderModalContent($body);
                    } else {
                        $body.html('<div class="vas-opencountsheets-message">No drafted count sheets found.</div>');
                    }
                },
                error: function (err) {
                    console.error("VAS_158_OpenCountSheetsWidget: Error loading draft list", err);
                    $body.html('<div class="vas-opencountsheets-message">Unable to load drafted count sheets.</div>');
                }
            });
        }

        function renderModalContent($body) {
            $body.empty();

            if (!draftList || draftList.length === 0) {
                $body.html('<div class="vas-opencountsheets-message">No drafted count sheets</div>');
                return;
            }

            var $headerRow = $(
                '<div class="vas-opencountsheets-grid-row vas-opencountsheets-header-row">' +
                '<div class="vas-opencountsheets-th">Count Sheet</div>' +
                '<div class="vas-opencountsheets-th">Warehouse</div>' +
                '<div class="vas-opencountsheets-th">Locator</div>' +
                '<div class="vas-opencountsheets-th vas-opencountsheets-th-right">Lines</div>' +
                '<div class="vas-opencountsheets-th">Started</div>' +
                '<div class="vas-opencountsheets-th">Status</div>' +
                '</div>'
            );
            $body.append($headerRow);

            var $rowsContainer = $('<div class="vas-opencountsheets-rows-container" style="flex: 1; display: flex; flex-direction: column;"></div>');
            $body.append($rowsContainer);

            var $footer = $(
                '<div class="vas-opencountsheets-modal-footer">' +
                '<div class="vas-opencountsheets-footer-text"></div>' +
                '<div class="vas-opencountsheets-pager">' +
                '<button type="button" class="vas-opencountsheets-pager-btn vas-prev" aria-label="Previous page">&lsaquo;</button>' +
                '<span class="vas-opencountsheets-pager-info"></span>' +
                '<button type="button" class="vas-opencountsheets-pager-btn vas-next" aria-label="Next page">&rsaquo;</button>' +
                '</div>' +
                '</div>'
            );
            $body.append($footer);

            updatePageRender($rowsContainer, $footer);
        }

        function updatePageRender($rowsContainer, $footer) {
            $rowsContainer.empty();

            var totalItems = draftList.length;
            var totalPages = Math.ceil(totalItems / pageSize) || 1;

            if (currentPage > totalPages) {
                currentPage = totalPages;
            }
            if (currentPage < 1) {
                currentPage = 1;
            }

            var startIndex = (currentPage - 1) * pageSize;
            var endIndex = Math.min(startIndex + pageSize, totalItems);
            var pageItems = draftList.slice(startIndex, endIndex);

            for (var i = 0; i < pageItems.length; i++) {
                var item = pageItems[i];
                var $row = $(
                    '<div class="vas-opencountsheets-grid-row vas-opencountsheets-data-row" data-id="' + item.InventoryId + '">' +
                    '<div class="vas-opencountsheets-cell vas-opencountsheets-cell-doc" title="' + item.DocumentNo + '">' + item.DocumentNo + '</div>' +
                    '<div class="vas-opencountsheets-cell vas-opencountsheets-cell-text" title="' + item.Warehouse + '">' + item.Warehouse + '</div>' +
                    '<div class="vas-opencountsheets-cell vas-opencountsheets-cell-text" title="' + item.Locator + '">' + item.Locator + '</div>' +
                    '<div class="vas-opencountsheets-cell vas-opencountsheets-cell-lines" title="' + item.Lines + '">' + item.Lines + '</div>' +
                    '<div class="vas-opencountsheets-cell vas-opencountsheets-cell-date" title="' + item.Started + '">' + item.Started + '</div>' +
                    '<div class="vas-opencountsheets-cell"><span class="vas-opencountsheets-status-chip">' + item.Status + '</span></div>' +
                    '</div>'
                );

                (function (invId) {
                    $row.on('click', function () {
                        openInventoryWindow(invId);
                    });
                })(item.InventoryId);

                $rowsContainer.append($row);
            }

            var $footerText = $footer.find('.vas-opencountsheets-footer-text');
            var $pagerInfo = $footer.find('.vas-opencountsheets-pager-info');
            var $btnPrev = $footer.find('.vas-prev');
            var $btnNext = $footer.find('.vas-next');

            if (totalItems > 0) {
                $footerText.text('Showing ' + (startIndex + 1) + '–' + endIndex + ' of ' + totalItems);
            } else {
                $footerText.text('Showing 0 of 0');
            }

            $pagerInfo.text(currentPage + ' of ' + totalPages);
            $btnPrev.prop('disabled', currentPage === 1);
            $btnNext.prop('disabled', currentPage === totalPages);

            $btnPrev.off('click').on('click', function () {
                if (currentPage > 1) {
                    currentPage--;
                    updatePageRender($rowsContainer, $footer);
                }
            });

            $btnNext.off('click').on('click', function () {
                if (currentPage < totalPages) {
                    currentPage++;
                    updatePageRender($rowsContainer, $footer);
                }
            });

            if (totalPages <= 1) {
                $footer.find('.vas-opencountsheets-pager').hide();
            } else {
                $footer.find('.vas-opencountsheets-pager').show();
            }
        }

        function openInventoryWindow(inventoryId) {
            if (!inventoryId) return;

            var windowId = 168;
            if (VIS.context && VIS.context.getWindowId) {
                windowId = VIS.context.getWindowId("M_Inventory") || 168;
            }

            var query = new VIS.Query();
            query.addRestriction("M_Inventory_ID", VIS.Query.prototype.EQUAL, inventoryId);

            if (VIS.viewManager) {
                VIS.viewManager.startWindow(windowId, query);
            }
        }

        this.getRoot = function () {
            return $wrapper;
        };

        this.refreshData = function () {
            loadData();
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

    VAS.VAS_158_OpenCountSheetsWidget.prototype.init = function (windowNo, frame) {
        this.frame = frame;
        this.AD_UserHomeWidgetID = frame.widgetInfo.AD_UserHomeWidgetID;
        this.windowNo = windowNo;
        this.Initalize();
        this.frame.getContentGrid().append(this.getRoot());
        ensureDashInlineSizeVar(this.getRoot());
    };

    VAS.VAS_158_OpenCountSheetsWidget.prototype.widgetSizeChange = function (height, width) { };

    VAS.VAS_158_OpenCountSheetsWidget.prototype.refreshWidget = function () {
        this.refreshData();
    };

    VAS.VAS_158_OpenCountSheetsWidget.prototype.dispose = function () {
        this.disposeComponent();
        if (this.frame) { this.frame.dispose(); }
        this.frame = null;
    };

})(VAS, jQuery);
