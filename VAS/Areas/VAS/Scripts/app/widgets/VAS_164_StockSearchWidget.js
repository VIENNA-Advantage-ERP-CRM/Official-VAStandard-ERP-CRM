/**
 * Stock Search Widget & Detail Modal (Inventory Count Dashboard)
 * Purpose - Full-width 9x1 dashboard search bar widget with body-appended
 *           fixed suggestions popover panel and Stock Detail Modal.
 * Prefix  - VAS_164_
 *
 * Labels / Message Keys Table:
 *  #  | Fallback Text                                    | Message Key
 * ----+--------------------------------------------------+-----------------------------------
 *  1  | Stock Search                                     | VAS_164_StockSearch
 *  2  | Search products by name, code, or category...    | VAS_164_SearchProductsPlaceholder
 *  3  | Product Code                                     | VAS_164_ProductCode
 *  4  | Product Name                                     | VAS_164_ProductName
 *  5  | Attribute                                        | VAS_164_Attribute
 *  6  | Category                                         | VAS_164_Category
 *  7  | Unit of Measure                                  | VAS_164_UnitOfMeasure
 *  8  | Total On-Hand Qty                                | VAS_164_TotalOnHandQty
 *  9  | Locator Code                                     | VAS_164_LocatorCode
 * 10  | Warehouse Name                                   | VAS_164_WarehouseName
 * 11  | Qty On Hand                                      | VAS_164_QtyOnHand
 * 12  | Page                                             | VAS_164_Page
 * 13  | of                                               | VAS_164_Of
 * 14  | Close                                            | VAS_164_Close
 * 15  | No stockable products matching search            | VAS_164_NoProductsMatchingSearch
 * 16  | Unable to load product details                   | VAS_164_UnableToLoadProductDetails
 */
; VAS = window.VAS || {};

; (function (VAS, $) {

    // Window name/search key for the Inventory Count screen - the counts tab navigates here.
    var COUNT_WINDOW_NAME = "VAS_PhysicalInventory";

    /* Database text was being concatenated straight into innerHTML throughout this widget. The
       source prompt requires the opposite: "Use textContent when rendering database text. If a
       template string is retained for repeated markup, escape every database value before
       insertion." */
    function esc(value) {
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

    VAS.VAS_164_StockSearchWidget = function () {

        this.frame;
        this.windowNo;
        this.AD_UserHomeWidgetID = 0;

        var $self = this;
        var widgetID = null;
        var $wrapper = $('<div class="vas-stocksearch-container">');
        var $root = $('<div class="vas-stocksearch-root">');
        var $bar;
        var $searchInput;
        var $clearBtn;
        var $panel;

        var searchTimeout = null;
        var widgetObserver = null;
        var $modalOverlay = null;

        this.Initalize = function () {
            widgetID = (VIS.Utility.Util.getValueOfInt(this.AD_UserHomeWidgetID) !== 0
                ? this.AD_UserHomeWidgetID
                : $self.windowNo);

            createWidget();
            wireEvents();
        };

        function createWidget() {
            $bar = $('<div class="vas-stocksearch-bar" id="vas_stocksearch_bar_' + widgetID + '">');
            var $icon = $('<i class="fa fa-search vas-stocksearch-icon"></i>');

            $searchInput = $('<input type="text" class="vas-stocksearch-input" placeholder="Search product / stock by name, code, locator..." autocomplete="off">');
            $clearBtn = $('<button type="button" class="vas-stocksearch-clear" title="Clear search">&times;</button>');
            var $hint = $('<span class="vas-stocksearch-hint">Select a result for full detail</span>');

            $bar.append($icon).append($searchInput).append($clearBtn).append($hint);
            $root.append($bar);
            $wrapper.append($root);

            // Suggestions Popover Panel (Appended to <body> to escape cell overflow:hidden)
            $panel = $('<div class="vas-stocksearch-panel" id="vas_stocksearch_panel_' + widgetID + '">');
            $('body').append($panel);

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
        }

        function wireEvents() {
            $searchInput.on('input', function () {
                var val = $.trim($(this).val());
                $bar.toggleClass('vas-stocksearch-has-text', val.length > 0);
                if (searchTimeout) clearTimeout(searchTimeout);
                searchTimeout = setTimeout(function () {
                    fetchSuggestions(val);
                }, 200);
            });

            $searchInput.on('focus', function () {
                var val = $.trim($(this).val());
                // Nothing typed yet -> show nothing. Focusing the empty bar used to fire a blank
                // query, which returned the whole product list as "suggestions".
                if (val.length === 0) { closePanel(); return; }
                fetchSuggestions(val);
            });

            $searchInput.on('keydown', function (e) {
                if (e.key === 'Escape' || e.keyCode === 27) {
                    closePanel();
                }
            });

            $clearBtn.on('click', function () {
                $searchInput.val('');
                $bar.removeClass('vas-stocksearch-has-text');
                closePanel();
                $searchInput.focus();
            });

            $self._onDocClick = function (e) {
                if (!$panel.hasClass('vas-stocksearch-open')) return;
                if ($bar[0].contains(e.target) || $panel[0].contains(e.target)) return;
                closePanel();
            };

            $self._onReflow = function () {
                if ($panel.hasClass('vas-stocksearch-open')) {
                    positionPanel();
                }
            };

            document.addEventListener('mousedown', $self._onDocClick, true);
            window.addEventListener('resize', $self._onReflow, true);
            window.addEventListener('scroll', $self._onReflow, true);
        }

        function positionPanel() {
            if (!$bar || !$bar[0]) return;
            var rect = $bar[0].getBoundingClientRect();
            $panel.css({
                left: Math.round(rect.left) + 'px',
                top: Math.round(rect.bottom + 6) + 'px',
                width: Math.round(rect.width) + 'px'
            });
        }

        function openPanel() {
            positionPanel();
            $panel.addClass('vas-stocksearch-open');
            $bar.addClass('vas-stocksearch-bar-focus');
        }

        function closePanel() {
            $panel.removeClass('vas-stocksearch-open');
            $bar.removeClass('vas-stocksearch-bar-focus');
        }

        function fetchSuggestions(query) {
            // Guard the fetch itself as well as the focus handler, so the debounced input path and
            // any future caller can't request suggestions for an empty term either.
            if (!query || $.trim(query).length === 0) {
                renderDropdown([]);
                closePanel();
                return;
            }

            $.ajax({
                url: VIS.Application.contextUrl + "VAS_164_StockSearchWidget/SearchProducts?query=" + encodeURIComponent(query || ""),
                type: "GET",
                dataType: "json",
                success: function (res) {
                    if (res && res.items) {
                        renderDropdown(res.items);
                    } else {
                        renderDropdown([]);
                    }
                },
                error: function (err) {
                    console.error("VAS_164_StockSearchWidget: Error searching products", err);
                    renderDropdown([]);
                }
            });
        }

        function renderDropdown(items) {
            $panel.empty();

            if (!items || items.length === 0) {
                $panel.html('<div class="vas-stocksearch-empty">No items match.</div>');
                openPanel();
                return;
            }

            for (var i = 0; i < items.length; i++) {
                var item = items[i];
                var formattedQty = Number(item.onHand || 0).toLocaleString();

                // Product Type dropped from the meta line (user request): every result is
                // ProductType = 'I' by definition, so the label carried no information.
                var meta = item.code + ' - ' + item.category;

                var $row = $(
                    '<div class="vas-stocksearch-item">' +
                    '<div class="vas-stocksearch-item-left">' +
                    '<div class="vas-stocksearch-pkg-tile"><i class="fa fa-cube"></i></div>' +
                    '<div class="vas-stocksearch-item-info">' +
                    '<span class="vas-stocksearch-item-name" title="' + esc(item.name) + '">' + esc(item.name) + '</span>' +
                    '<span class="vas-stocksearch-item-meta" title="' + esc(meta) + '">' + esc(meta) + '</span>' +
                    '</div>' +
                    '</div>' +
                    '<span class="vas-stocksearch-item-qty">' + esc(formattedQty) + ' ' + esc(item.uom) + '</span>' +
                    '</div>'
                );

                (function (prodItem, $r) {
                    $r.on('click', function (e) {
                        e.stopPropagation();
                        closePanel();
                        $searchInput.val(prodItem.name);
                        $bar.addClass('vas-stocksearch-has-text');
                        openStockDetailModal(prodItem);
                    });
                })(item, $row);

                $panel.append($row);
            }

            openPanel();
        }

        function openStockDetailModal(prodItem) {
            if ($modalOverlay) {
                $modalOverlay.remove();
            }

            var $overlay = $('<div class="vas-stocksearch-modal-overlay">');
            var $dialog = $('<div class="vas-stocksearch-modal-dialog" role="dialog" aria-modal="true">');

            // Header (56px)
            var $header = $('<div class="vas-stocksearch-modal-header">');
            var $headerLeft = $('<div class="vas-stocksearch-modal-header-left">');
            var $title = $('<h3 class="vas-stocksearch-modal-title" title="' + prodItem.name + '">' + prodItem.name + '</h3>');
            var $codePill = $('<span class="vas-stocksearch-code-pill">' + prodItem.code + '</span>');

            var statusClass = "vas-stocksearch-status-active";
            if (prodItem.status === "Inactive") {
                statusClass = "vas-stocksearch-status-inactive";
            } else if (prodItem.status === "Discontinued") {
                statusClass = "vas-stocksearch-status-discontinued";
            }
            var $statusPill = $('<span class="' + statusClass + '">' + prodItem.status + '</span>');

            $headerLeft.append($title).append($codePill).append($statusPill);

            var $closeBtn = $('<button type="button" class="vas-stocksearch-modal-close" aria-label="Close modal">&times;</button>');
            $header.append($headerLeft).append($closeBtn);
            $dialog.append($header);

            // Read-Only Form Grid (3 Columns x 2 Rows = 6 Fields)
            var formattedOnHand = Number(prodItem.onHand || 0).toLocaleString();

            /* Product Type field removed (user request): the search returns only ProductType = 'I',
               so the field always read "Item".
               "Category" relabelled "Product Category" - it is M_Product_Category.Name.
               "On Hand" no longer appends the UoM, because UoM is already its own field. */
            var $formGrid = $(
                '<div class="vas-stocksearch-form-grid">' +
                '<div class="vas-stocksearch-field"><span class="vas-stocksearch-label">Product Code</span><span class="vas-stocksearch-val">' + esc(prodItem.code || "-") + '</span></div>' +
                '<div class="vas-stocksearch-field"><span class="vas-stocksearch-label">Name</span><span class="vas-stocksearch-val" title="' + esc(prodItem.name) + '">' + esc(prodItem.name || "-") + '</span></div>' +
                '<div class="vas-stocksearch-field"><span class="vas-stocksearch-label">Product Category</span><span class="vas-stocksearch-val" title="' + esc(prodItem.category) + '">' + esc(prodItem.category || "-") + '</span></div>' +
                '<div class="vas-stocksearch-field"><span class="vas-stocksearch-label">UoM</span><span class="vas-stocksearch-val">' + esc(prodItem.uom || "-") + '</span></div>' +
                '<div class="vas-stocksearch-field"><span class="vas-stocksearch-label">On Hand</span><span class="vas-stocksearch-val vas-stocksearch-val-bold">' + esc(formattedOnHand) + '</span></div>' +
                '</div>'
            );
            $dialog.append($formGrid);

            /* Two tabs: the existing locator breakdown, and the new inventory-count history.
               Each tab keeps its own pane so switching does not refetch. */
            var $tabs = $(
                '<div class="vas-stocksearch-tabs" role="tablist">' +
                '<button type="button" class="vas-stocksearch-tab vas-stocksearch-tab-active" data-tab="locators" role="tab">Locators</button>' +
                '<button type="button" class="vas-stocksearch-tab" data-tab="counts" role="tab">Inventory Counts</button>' +
                '</div>'
            );
            $dialog.append($tabs);

            var $body = $('<div class="vas-stocksearch-modal-body">');

            var $locContent = $('<div class="vas-stocksearch-pane vas-stocksearch-pane-locators">');
            $locContent.html('<div class="vas-stocksearch-empty">Loading locators...</div>');

            var $countContent = $('<div class="vas-stocksearch-pane vas-stocksearch-pane-counts vas-stocksearch-pane-hidden">');
            $countContent.html('<div class="vas-stocksearch-empty">Loading inventory counts...</div>');

            $body.append($locContent).append($countContent);
            $dialog.append($body);

            var countsLoaded = false;

            $tabs.on('click', '.vas-stocksearch-tab', function () {
                var tab = $(this).data('tab');
                $tabs.find('.vas-stocksearch-tab').removeClass('vas-stocksearch-tab-active');
                $(this).addClass('vas-stocksearch-tab-active');

                $locContent.toggleClass('vas-stocksearch-pane-hidden', tab !== 'locators');
                $countContent.toggleClass('vas-stocksearch-pane-hidden', tab !== 'counts');

                // Fetch the history the first time its tab is opened.
                if (tab === 'counts' && !countsLoaded) {
                    countsLoaded = true;
                    fetchCountHistory(prodItem, $countContent);
                }
            });
            $overlay.append($dialog);
            $('body').append($overlay);
            $modalOverlay = $overlay;

            $closeBtn.focus();

            var closeModal = function () {
                $overlay.remove();
                $modalOverlay = null;
                if ($searchInput) {
                    $searchInput.focus();
                }
            };

            $closeBtn.on('click', closeModal);
            $overlay.on('click', function (e) {
                if ($(e.target).hasClass('vas-stocksearch-modal-overlay')) {
                    closeModal();
                }
            });

            $(document).off('keydown.vas-stocksearch').on('keydown.vas-stocksearch', function (e) {
                if (e.key === 'Escape' && $modalOverlay) {
                    closeModal();
                }
            });

            // Fetch Locators
            $.ajax({
                url: VIS.Application.contextUrl + "VAS_164_StockSearchWidget/GetProductLocators?productId=" + prodItem.productId,
                type: "GET",
                dataType: "json",
                success: function (res) {
                    if (res && res.locators) {
                        renderLocatorsTable($locContent, res.locators, res.totalLocators || res.locators.length, prodItem.uom);
                    } else {
                        $locContent.html('<div class="vas-stocksearch-empty">No locator stock recorded.</div>');
                    }
                },
                error: function (err) {
                    console.error("VAS_164_StockSearchWidget: Error loading locators", err);
                    $locContent.html('<div class="vas-stocksearch-empty">Unable to load locator details.</div>');
                }
            });
        }

        /* ---- Inventory Counts tab -------------------------------------------------------------
           Previous physical counts for this product. A row click navigates to the count document on
           the VAS_PhysicalInventory screen, resolved by window NAME through the widget channel -
           window IDs differ per instance, so a hardcoded id lands on the wrong screen. */
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

        function openCountRecord(inventoryId) {
            if (!inventoryId) { return; }
            if ($modalOverlay) { $modalOverlay.remove(); $modalOverlay = null; }
            $self.widgetFirevalueChanged({
                "TabWhereClause": "M_Inventory.M_Inventory_ID=" + Number(inventoryId),
                "TabLayout": "Y",
                "TabIndex": "0",
                "ActionName": hostWindowName() || COUNT_WINDOW_NAME,
                "ActionType": "W"
            });
        }

        function fetchCountHistory(prodItem, $container) {
            $.ajax({
                url: VIS.Application.contextUrl + "VAS_164_StockSearchWidget/GetProductCountHistory?productId=" + prodItem.productId,
                type: "GET",
                dataType: "json",
                success: function (res) {
                    renderCountsTable($container, (res && res.counts) ? res.counts : []);
                },
                error: function (err) {
                    console.error("VAS_164_StockSearchWidget: Error loading count history", err);
                    $container.html('<div class="vas-stocksearch-empty">Unable to load inventory counts.</div>');
                }
            });
        }

        function renderCountsTable($container, counts) {
            $container.empty();

            if (!counts || counts.length === 0) {
                $container.html('<div class="vas-stocksearch-empty">No inventory counts recorded for this item.</div>');
                return;
            }

            var $table = $(
                '<table class="vas-stocksearch-table">' +
                '<thead><tr>' +
                '<th>Document No</th>' +
                '<th>Date</th>' +
                '<th>Warehouse</th>' +
                '<th>Attribute</th>' +
                '<th class="right">Book</th>' +
                '<th class="right">Counted</th>' +
                '<th class="right">Variance</th>' +
                '</tr></thead><tbody></tbody></table>'
            );
            var $tbody = $table.find('tbody');

            for (var i = 0; i < counts.length; i++) {
                var c = counts[i];
                var variance = Number(c.variance || 0);
                var varClass = variance > 0 ? 'vas-stocksearch-var-pos'
                    : (variance < 0 ? 'vas-stocksearch-var-neg' : '');
                var varText = variance > 0 ? ('+' + variance) : String(variance);

                var $tr = $(
                    '<tr class="vas-stocksearch-count-row" data-invid="' + Number(c.inventoryId) + '" title="Open this count document">' +
                    '<td class="loc-code">' + esc(c.documentNo) + '</td>' +
                    '<td>' + esc(c.movementDate) + '</td>' +
                    '<td class="wh-name" title="' + esc(c.warehouse) + '">' + esc(c.warehouse) + '</td>' +
                    '<td class="attr-val" title="' + esc(c.attribute || "") + '">' + esc(c.attribute || "") + '</td>' +
                    '<td class="qty-val">' + esc(Number(c.qtyBook || 0).toLocaleString()) + '</td>' +
                    '<td class="qty-val">' + esc(Number(c.qtyCount || 0).toLocaleString()) + '</td>' +
                    '<td class="qty-val ' + varClass + '">' + esc(varText) + '</td>' +
                    '</tr>'
                );
                $tbody.append($tr);
            }

            $container.append($table);

            $tbody.on('click', '.vas-stocksearch-count-row', function () {
                openCountRecord(Number($(this).data('invid') || 0));
            });
        }

        function renderLocatorsTable($container, locators, totalCount, uom) {
            $container.empty();

            if (!locators || locators.length === 0) {
                $container.html('<div class="vas-stocksearch-empty">No locators found for this item.</div>');
                return;
            }

            var currentPage = 1;

            function getAdaptivePageSize() {
                var bh = $container.height() || 300;
                var available = bh - 50;
                return Math.max(3, Math.floor(available / 38));
            }

            var pageSize = getAdaptivePageSize();


            var $table = $(
                '<table class="vas-stocksearch-table">' +
                '<thead>' +
                '<tr>' +
                '<th>Locator</th>' +
                '<th>Warehouse</th>' +
                '<th>Attribute</th>' +
                '<th class="right">Qty</th>' +
                '</tr>' +
                '</thead>' +
                '<tbody></tbody>' +
                '</table>'
            );
            var $tbody = $table.find('tbody');
            $container.append($table);

            var $pagerRow = $(
                '<div class="vas-stocksearch-pager-row">' +
                '<span class="vas-stocksearch-pager-text"></span>' +
                '<div class="vas-stocksearch-pager">' +
                '<button type="button" class="vas-stocksearch-pager-btn vas-p-prev" aria-label="Previous page">&lsaquo;</button>' +
                '<span class="vas-stocksearch-pager-info vas-p-info"></span>' +
                '<button type="button" class="vas-stocksearch-pager-btn vas-p-next" aria-label="Next page">&rsaquo;</button>' +
                '</div>' +
                '</div>'
            );
            $container.append($pagerRow);

            function updatePage() {
                $tbody.empty();
                pageSize = getAdaptivePageSize();
                var totalPages = Math.ceil(totalCount / pageSize) || 1;

                if (currentPage > totalPages) currentPage = totalPages;
                if (currentPage < 1) currentPage = 1;

                var start = (currentPage - 1) * pageSize;
                var end = Math.min(start + pageSize, totalCount);
                var pageItems = locators.slice(start, end);

                for (var i = 0; i < pageItems.length; i++) {
                    var item = pageItems[i];
                    var formattedQty = Number(item.qty || 0).toLocaleString();

                    var $tr = $(
                        '<tr>' +
                        // Qty no longer carries the UoM suffix - UoM is its own field in the header
                        // grid. Attribute is blank when the stock line has none.
                        '<td class="loc-code" title="' + esc(item.locator) + '">' + esc(item.locator) + '</td>' +
                        '<td class="wh-name" title="' + esc(item.warehouse) + '">' + esc(item.warehouse) + '</td>' +
                        '<td class="attr-val" title="' + esc(item.attribute || "") + '">' + esc(item.attribute || "") + '</td>' +
                        '<td class="qty-val" title="' + esc(formattedQty) + '">' + esc(formattedQty) + '</td>' +
                        '</tr>'
                    );
                    $tbody.append($tr);
                }

                $pagerRow.find('.vas-stocksearch-pager-text').text(totalCount + (totalCount === 1 ? ' locator' : ' locators'));
                $pagerRow.find('.vas-p-info').text(currentPage + ' / ' + totalPages);

                var $btnPrev = $pagerRow.find('.vas-p-prev');
                var $btnNext = $pagerRow.find('.vas-p-next');

                $btnPrev.prop('disabled', currentPage === 1);
                $btnNext.prop('disabled', currentPage === totalPages);

                $btnPrev.off('click').on('click', function () {
                    if (currentPage > 1) {
                        currentPage--;
                        updatePage();
                    }
                });

                $btnNext.off('click').on('click', function () {
                    if (currentPage < totalPages) {
                        currentPage++;
                        updatePage();
                    }
                });

                if (totalCount <= pageSize) {
                    $pagerRow.find('.vas-stocksearch-pager').hide();
                } else {
                    $pagerRow.find('.vas-stocksearch-pager').show();
                }
            }

            updatePage();

            $(window).off('resize.vas-stocksearch-loc').on('resize.vas-stocksearch-loc', function () {
                if ($modalOverlay) {
                    updatePage();
                }
            });
        }

        this.getRoot = function () {
            return $wrapper;
        };

        this.refreshData = function () { };

        this.disposeComponent = function () {
            if (widgetObserver) {
                widgetObserver.disconnect();
                widgetObserver = null;
            }
            if ($modalOverlay) {
                $modalOverlay.remove();
                $modalOverlay = null;
            }
            if ($panel) {
                $panel.remove();
            }
            document.removeEventListener('mousedown', $self._onDocClick, true);
            window.removeEventListener('resize', $self._onReflow, true);
            window.removeEventListener('scroll', $self._onReflow, true);
            $(window).off('resize.vas-stocksearch-loc');
            $wrapper.remove();
        };
    };

    // Required for the Inventory Counts tab to navigate; neither existed on this widget before.
    VAS.VAS_164_StockSearchWidget.prototype.widgetFirevalueChanged = function (value) {
        if (this.listener) { this.listener.widgetFirevalueChanged(value); }
    };

    VAS.VAS_164_StockSearchWidget.prototype.addChangeListener = function (listener) {
        this.listener = listener;
    };

    VAS.VAS_164_StockSearchWidget.prototype.init = function (windowNo, frame) {
        this.frame = frame;
        this.AD_UserHomeWidgetID = frame.widgetInfo.AD_UserHomeWidgetID;
        this.windowNo = windowNo;
        this.Initalize();
        this.frame.getContentGrid().append(this.getRoot());
        ensureDashInlineSizeVar(this.getRoot());
    };

    VAS.VAS_164_StockSearchWidget.prototype.widgetSizeChange = function (height, width) { };

    VAS.VAS_164_StockSearchWidget.prototype.refreshWidget = function () {
        this.refreshData();
    };

    VAS.VAS_164_StockSearchWidget.prototype.dispose = function () {
        this.disposeComponent();
        if (this.frame) { this.frame.dispose(); }
        this.frame = null;
    };

})(VAS, jQuery);
