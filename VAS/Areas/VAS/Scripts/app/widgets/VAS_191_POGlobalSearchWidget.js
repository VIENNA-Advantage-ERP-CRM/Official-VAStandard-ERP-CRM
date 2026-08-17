/************************************************************
 * Module Name    : VAS_191_POGlobalSearchWidget
 * Purpose        : Purchase Order Dashboard - Widget 01: Global Search
 *                  Full-width (9x1) search across purchase orders,
 *                  vendors, products, warehouses, representatives,
 *                  requisitions, order references, descriptions, and locations.
 *                  Selecting a result navigates directly to the record
 *                  on the Purchase Order screen.
 * Chronological  : 2026-08-17 Created
 * 
 * SUMMARY MESSAGE TABLE:
 *  # | Default Text                                                                      | Message Key
 * ---+-----------------------------------------------------------------------------------+-------------------------------
 *  1 | Search product, vendor, location, representative, PO reference, description...   | VAS_191_Placeholder
 *  2 | Esc                                                                               | VAS_191_EscHint
 *  3 | Recent across purchase                                                            | VAS_191_RecentAcrossPurchase
 *  4 | Purchase order                                                                    | VAS_191_PurchaseOrder
 *  5 | Vendor                                                                            | VAS_191_Vendor
 *  6 | Product                                                                           | VAS_191_Product
 *  7 | Representative                                                                    | VAS_191_Representative
 *  8 | Warehouse                                                                         | VAS_191_Warehouse
 *  9 | Requisition                                                                       | VAS_191_Requisition
 * 10 | Order reference                                                                   | VAS_191_OrderReference
 * 11 | Description                                                                       | VAS_191_Description
 * 12 | Location                                                                          | VAS_191_Location
 * 13 | Showing {0} of {1} matches — refine your search                                   | VAS_191_ShowingMatches
 * 14 | No purchase records match “{0}”. Try a PO number, vendor, product, warehouse...  | VAS_191_NoMatches
 * 15 | Unable to complete search. Please try again.                                      | VAS_191_SearchError
 * 16 | Drafted                                                                           | VAS_191_Drafted
 * 17 | In Progress                                                                       | VAS_191_InProgress
 * 18 | Completed                                                                         | VAS_191_Completed
 * 19 | Closed                                                                            | VAS_191_Closed
 * 20 | Voided                                                                            | VAS_191_Voided
 * 21 | Searching...                                                                      | VAS_191_Searching
 ************************************************************/

; VAS = window.VAS || {};

; (function (VAS, $) {

    function ensureDashInlineSizeVar($el) {
        if (window.__vasDashInlineSizeObserver) { return; }
        if (typeof ResizeObserver === 'undefined') { return; }

        var container = $el.closest('.vis-widget-container, [data-dashboard-container], .canvas')[0];
        if (!container) { return; }

        var write = function () {
            document.documentElement.style.setProperty('--dash-inline-size', container.clientWidth + 'px');
        };

        window.__vasDashInlineSizeObserver = new ResizeObserver(write);
        window.__vasDashInlineSizeObserver.observe(container);
        write();
    }

    VAS.VAS_191_POGlobalSearchWidget = function () {
        var ENDPOINT = 'VAS/VAS_191_POGlobalSearchWidget/SearchPurchaseOrders';
        var WINDOW_NAME = 'VAS_PurchaseOrder';
        var TAB_ID = 1002398;
        var TABLE_NAME = 'C_Order';

        this.frame = null;
        this.windowNo = null;
        this.listener = null;
        this.widgetInfo = null;

        var $self = this;
        var $root = $('<div class="vas-191-posrch-root"></div>');
        var $shell, $box, $input, $spin, $kbd, $results;
        var searchTimer = null;
        var requestSeq = 0;
        var currentItems = [];
        var activeItemIndex = -1;

        var GICON = {
            'Purchase order': { bg: '#EAF8FF', char: 'P', key: 'VAS_191_PurchaseOrder', fallback: 'Purchase order' },
            'Vendor': { bg: '#E7F7EF', char: 'V', key: 'VAS_191_Vendor', fallback: 'Vendor' },
            'Product': { bg: '#FFF6E2', char: 'P', key: 'VAS_191_Product', fallback: 'Product' },
            'Representative': { bg: '#EFEEFF', char: 'R', key: 'VAS_191_Representative', fallback: 'Representative' },
            'Warehouse': { bg: '#F1F4F8', char: 'W', key: 'VAS_191_Warehouse', fallback: 'Warehouse' },
            'Requisition': { bg: '#E7F7EF', char: 'R', key: 'VAS_191_Requisition', fallback: 'Requisition' },
            'Order reference': { bg: '#EAF8FF', char: 'O', key: 'VAS_191_OrderReference', fallback: 'Order reference' },
            'Description': { bg: '#FCEFEF', char: 'D', key: 'VAS_191_Description', fallback: 'Description' },
            'Location': { bg: '#F1F4F8', char: 'L', key: 'VAS_191_Location', fallback: 'Location' },
            'Recent': { bg: '#EAF8FF', char: 'P', key: 'VAS_191_RecentAcrossPurchase', fallback: 'Recent across purchase' }
        };

        function lbl(key, fallback) {
            var msg = VIS.Msg.getMsg(key);
            return (msg && msg !== key && msg !== '[' + key + ']') ? msg : fallback;
        }

        function escapeHtml(str) {
            return String(str == null ? '' : str)
                .replace(/&/g, '&amp;')
                .replace(/</g, '&lt;')
                .replace(/>/g, '&gt;')
                .replace(/"/g, '&quot;')
                .replace(/'/g, '&#39;');
        }

        function parseResponse(res) {
            var data = res;
            if (typeof data === 'string' && data.length > 0) {
                try { data = JSON.parse(data); } catch (e) { }
            }
            if (typeof data === 'string' && data.length > 0) {
                try { data = JSON.parse(data); } catch (e) { }
            }
            return data || {};
        }

        this.initalize = function () {
            buildWidget();
            ensureDashInlineSizeVar($root);
        };

        function buildWidget() {
            var placeholder = lbl('VAS_191_Placeholder', 'Search product, vendor, location, representative, PO reference, description, warehouse…');
            var escHint = lbl('VAS_191_EscHint', 'Esc');

            $shell = $('<div class="vas-191-posrch-shell"></div>');
            $box = $('<div class="vas-191-posrch-box"></div>');

            var searchIconSvg = '<svg class="vas-191-posrch-ico" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2.2" stroke-linecap="round"><circle cx="11" cy="11" r="7"/><path d="m20 20-3.2-3.2"/></svg>';
            $input = $('<input type="search" class="vas-191-posrch-input" autocomplete="off" spellcheck="false" />')
                .attr('placeholder', placeholder)
                .attr('aria-label', placeholder);
            $spin = $('<span class="vas-191-posrch-spin"></span>');
            $kbd = $('<span class="vas-191-posrch-kbd">' + escapeHtml(escHint) + '</span>');
            $results = $('<div class="vas-191-posrch-results"></div>');

            $box.append(searchIconSvg);
            $box.append($input);
            $box.append($spin);
            $box.append($kbd);

            $shell.append($box);
            $shell.append($results);
            $root.append($shell);

            bindEvents();
        }

        function bindEvents() {
            $input.on('focus', function () {
                var q = $.trim($input.val());
                scheduleSearch(q, 0);
            });

            $input.on('input', function () {
                var q = $.trim($input.val());
                scheduleSearch(q, 180);
            });

            $input.on('keydown', function (e) {
                if (e.key === 'Escape' || e.keyCode === 27) {
                    e.preventDefault();
                    closeResults();
                } else if (e.key === 'ArrowDown' || e.keyCode === 40) {
                    e.preventDefault();
                    navigateItems(1);
                } else if (e.key === 'ArrowUp' || e.keyCode === 38) {
                    e.preventDefault();
                    navigateItems(-1);
                } else if (e.key === 'Enter' || e.keyCode === 13) {
                    if (activeItemIndex >= 0 && activeItemIndex < currentItems.length) {
                        e.preventDefault();
                        var selected = currentItems[activeItemIndex];
                        if (selected && selected.orderId) {
                            goToRecord(selected.orderId, selected.orderLineId);
                        }
                    }
                }
            });

            $kbd.on('click', function () {
                $input.val('');
                closeResults();
                $input.blur();
            });

            $results.on('click', '.vas-191-posrch-ritem', function (e) {
                e.preventDefault();
                var orderId = VIS.Utility.Util.getValueOfInt($(this).attr('data-order-id'));
                var orderLineId = VIS.Utility.Util.getValueOfInt($(this).attr('data-order-line-id'));
                if (orderId > 0) {
                    goToRecord(orderId, orderLineId);
                }
            });

            $results.on('mouseenter', '.vas-191-posrch-ritem', function () {
                var idx = VIS.Utility.Util.getValueOfInt($(this).attr('data-idx'));
                setActiveItem(idx);
            });

            $self._onDocClick = function (e) {
                if (!$results.hasClass('vas-191-open')) { return; }
                if ($shell[0].contains(e.target)) { return; }
                closeResults();
            };

            document.addEventListener('mousedown', $self._onDocClick, true);
        }

        function scheduleSearch(term, delay) {
            if (searchTimer) {
                window.clearTimeout(searchTimer);
                searchTimer = null;
            }
            if (delay === 0) {
                fetchResults(term);
            } else {
                setBusy(true);
                searchTimer = window.setTimeout(function () {
                    fetchResults(term);
                }, delay);
            }
        }

        function setBusy(busy) {
            $box.toggleClass('vas-191-busy', !!busy);
        }

        function fetchResults(term) {
            var mySeq = ++requestSeq;
            setBusy(true);

            $.ajax({
                url: VIS.Application.contextUrl + ENDPOINT,
                type: 'GET',
                data: { query: term },
                dataType: 'json',
                cache: false,
                success: function (raw) {
                    if (mySeq !== requestSeq) { return; }
                    setBusy(false);

                    var res = parseResponse(raw);
                    var payload = res.data || res;
                    if (payload && payload.results) {
                        renderResults(term, payload.results, payload.totalCount || payload.results.length);
                    } else if (res.error || res.success === false) {
                        renderError(res.error || res.message);
                    } else {
                        renderResults(term, [], 0);
                    }
                },
                error: function () {
                    if (mySeq !== requestSeq) { return; }
                    setBusy(false);
                    renderError(lbl('VAS_191_SearchError', 'Unable to complete search. Please try again.'));
                }
            });
        }

        function renderResults(query, items, totalCount) {
            currentItems = items || [];
            activeItemIndex = -1;

            if (!items || items.length === 0) {
                if (query) {
                    var emptyTemplate = lbl('VAS_191_NoMatches', 'No purchase records match “{0}”. Try a PO number, vendor, product, warehouse or representative.');
                    var emptyText = emptyTemplate.replace('{0}', query);
                    $results.html('<div class="vas-191-posrch-rempty">' + escapeHtml(emptyText) + '</div>');
                } else {
                    $results.empty();
                    closeResults();
                    return;
                }
                openResults();
                return;
            }

            var html = '';
            var currentGroup = '';
            var isRecentMode = !query;

            if (isRecentMode) {
                html += '<div class="vas-191-posrch-rgroup">' + escapeHtml(lbl('VAS_191_RecentAcrossPurchase', 'Recent across purchase')) + '</div>';
            }

            for (var i = 0; i < items.length; i++) {
                var item = items[i];
                var groupName = item.group || item.groupKey || 'Purchase order';
                var iconCfg = GICON[item.groupKey] || GICON[groupName] || GICON['Purchase order'];
                var translatedGroup = lbl(iconCfg.key, iconCfg.fallback);

                if (!isRecentMode && groupName !== currentGroup) {
                    currentGroup = groupName;
                    html += '<div class="vas-191-posrch-rgroup">' + escapeHtml(translatedGroup) + '</div>';
                }

                html += '<button class="vas-191-posrch-ritem" type="button"'
                    + ' data-idx="' + i + '"'
                    + ' data-order-id="' + escapeHtml(item.orderId) + '"'
                    + (item.orderLineId ? ' data-order-line-id="' + escapeHtml(item.orderLineId) + '"' : '')
                    + ' aria-label="' + escapeHtml(item.title + ' ' + item.subtitle) + '">'
                    + '<span class="vas-191-posrch-ico-badge" style="background:' + iconCfg.bg + '">' + escapeHtml(iconCfg.char) + '</span>'
                    + '<span class="vas-191-posrch-txt">'
                    + '<span class="vas-191-posrch-t1">' + escapeHtml(item.title) + '</span>'
                    + '<span class="vas-191-posrch-t2">' + escapeHtml(item.subtitle) + '</span>'
                    + '</span>'
                    + '<span class="vas-191-posrch-t3">' + escapeHtml(item.value || '') + '</span>'
                    + '</button>';
            }

            if (totalCount > items.length) {
                var moreTemplate = lbl('VAS_191_ShowingMatches', 'Showing {0} of {1} matches — refine your search');
                var moreText = moreTemplate.replace('{0}', items.length).replace('{1}', totalCount);
                html += '<div class="vas-191-posrch-rfooter">' + escapeHtml(moreText) + '</div>';
            }

            $results.html(html);
            openResults();
        }

        function renderError(message) {
            currentItems = [];
            activeItemIndex = -1;
            var errText = message || lbl('VAS_191_SearchError', 'Unable to complete search. Please try again.');
            $results.html('<div class="vas-191-posrch-rempty vas-191-posrch-error">' + escapeHtml(errText) + '</div>');
            openResults();
        }

        function openResults() {
            $results.addClass('vas-191-open');
            $box.addClass('vas-191-focus');
        }

        function closeResults() {
            $results.removeClass('vas-191-open');
            $box.removeClass('vas-191-focus');
            activeItemIndex = -1;
        }

        function navigateItems(delta) {
            if (!currentItems || currentItems.length === 0 || !$results.hasClass('vas-191-open')) { return; }
            var newIdx = activeItemIndex + delta;
            if (newIdx < 0) { newIdx = currentItems.length - 1; }
            if (newIdx >= currentItems.length) { newIdx = 0; }
            setActiveItem(newIdx);
        }

        function setActiveItem(index) {
            activeItemIndex = index;
            var $items = $results.find('.vas-191-posrch-ritem');
            $items.removeClass('vas-191-active');
            if (index >= 0 && index < $items.length) {
                var $target = $items.eq(index);
                $target.addClass('vas-191-active');
            }
        }

        function goToRecord(orderId, orderLineId) {
            if (!orderId) { return; }
            closeResults();

            var navigated = false;
            try {
                if ($self.listener && typeof $self.widgetFirevalueChanged === 'function') {
                    $self.widgetFirevalueChanged({
                        "TabWhereClause": TABLE_NAME + "." + TABLE_NAME + "_ID=" + orderId,
                        "TabLayout": "Y",
                        "TabIndex": "0",
                        "AD_Tab_ID": TAB_ID,
                        "ActionName": WINDOW_NAME,
                        "ActionType": "W"
                    });
                    navigated = true;
                }
            } catch (ex) {
                console.warn("VAS_191_POGlobalSearchWidget: widgetFirevalueChanged failed", ex);
            }

            if (!navigated) {
                try {
                    if (VIS && VIS.AEnv && typeof VIS.AEnv.zoom === 'function') {
                        VIS.AEnv.zoom(259, orderId); // 259 is C_Order Table ID
                    }
                } catch (ex2) {
                    console.warn("VAS_191_POGlobalSearchWidget: VIS.AEnv.zoom fallback failed", ex2);
                }
            }
        }

        this.refreshWidget = function () {
            if (searchTimer) {
                window.clearTimeout(searchTimer);
                searchTimer = null;
            }
            requestSeq++;
            currentItems = [];
            activeItemIndex = -1;
            if ($input) { $input.val(''); }
            setBusy(false);
            closeResults();
        };

        this.getRoot = function () {
            return $root;
        };

        this.disposeComponent = function () {
            if (searchTimer) {
                window.clearTimeout(searchTimer);
                searchTimer = null;
            }
            if ($self._onDocClick) {
                document.removeEventListener('mousedown', $self._onDocClick, true);
                $self._onDocClick = null;
            }
            $root.off();
            $root.remove();
        };
    };

    VAS.VAS_191_POGlobalSearchWidget.prototype.init = function (windowNo, frame) {
        this.frame = frame;
        this.widgetInfo = frame.widgetInfo;
        this.windowNo = windowNo;
        this.initalize();
        this.frame.getContentGrid().append(this.getRoot());
    };

    VAS.VAS_191_POGlobalSearchWidget.prototype.refreshWidget = function () {
        this.refreshWidget();
    };

    VAS.VAS_191_POGlobalSearchWidget.prototype.addChangeListener = function (listener) {
        this.listener = listener;
    };

    VAS.VAS_191_POGlobalSearchWidget.prototype.widgetFirevalueChanged = function (value) {
        if (this.listener && typeof this.listener.widgetFirevalueChanged === 'function') {
            this.listener.widgetFirevalueChanged(value);
        }
    };

    VAS.VAS_191_POGlobalSearchWidget.prototype.widgetSizeChange = function (widget) {
        this.widgetInfo = widget;
    };

    VAS.VAS_191_POGlobalSearchWidget.prototype.dispose = function () {
        this.disposeComponent();
        if (this.frame) {
            this.frame.dispose();
        }
        this.frame = null;
        this.windowNo = null;
    };

})(VAS, jQuery);
