/************************************************************
 * Module Name    : VAS
 * Purpose        : Purchase Order Global Search Widget
 *                  Full-width 9x1 dashboard search bar that searches across
 *                  purchase records (PO number, vendor, product, warehouse,
 *                  representative, PO reference, description, requisition,
 *                  location). Clicking a result navigates to the order.
 *
 *                  Uses the shared vas-dssrch-* document-search design
 *                  (Content/VAS_DocumentSearchWidgets.css) so this widget looks
 *                  and behaves exactly like VAS_067 / VAS_068 / VAS_069 /
 *                  VAS_070 / VAS_091.
 * chronological  : Development
 *
 * AD_Message keys used:
 *   VAS_203_Placeholder        => "Search product, vendor, location, representative, PO reference, description, warehouse..."
 *   VAS_DocSearch_TypeToSearch => "Type at least 2 characters to search"
 *   VAS_DocSearch_NoResults    => "No matching documents"
 *   VAS_DocSearch_Error        => "Search failed. Please try again."
 *   VAS_DocSearch_Results      => "results"
 ***********************************************************/
; VAS = window.VAS || {};
; (function (VAS, $) {

    VAS.VAS_203_POGlobalSearchWidget = function () {
        // ---- Per-widget configuration ----
        var ENDPOINT = 'VAS/VAS_203_POGlobalSearchWidget/SearchPurchaseOrders';
        var WINDOW_NAME = 'VAS_PurchaseOrder';
        var TAB_ID = 1002398;
        var TABLE_NAME = 'C_Order';
        var ZOOM_TABLE_ID = 259; // C_Order

        this.frame;
        this.windowNo;
        this.listener = null;
        this.widgetInfo = null;

        var $self = this;
        var $bsyDiv;
        var $root = $('<div class="h-100 w-100 vas-widget-bg vas-dssrch-root">');
        var $bar, $input, $panel;
        var widgetID = null;

        // Nothing is searched or shown until the user has typed this many
        // characters - same contract as the other document search widgets.
        var MIN_LEN = 2;
        var debounceTimer = null;
        // QA sheet #20: a result opens the PO detail modal instead of navigating.
        var $detailModal = null;
        var requestSeq = 0;
        var currentItems = [];
        var activeItemIndex = -1;

        /* Group -> chip tone. The shared stylesheet ships chip-order / -invoice /
           -payment / -receipt / -cash / -gljournal; the purchase-specific groups map
           onto those tones plus the few extra ones in this widget's own CSS. */
        var GROUPS = {
            'Purchase order': { chip: 'order', key: 'VAS_203_PurchaseOrder', fallback: 'Purchase order' },
            'Vendor': { chip: 'vendor', key: 'VAS_203_Vendor', fallback: 'Vendor' },
            'Product': { chip: 'product', key: 'VAS_203_Product', fallback: 'Product' },
            'Representative': { chip: 'rep', key: 'VAS_203_Representative', fallback: 'Representative' },
            'Warehouse': { chip: 'warehouse', key: 'VAS_203_Warehouse', fallback: 'Warehouse' },
            'Requisition': { chip: 'requisition', key: 'VAS_203_Requisition', fallback: 'Requisition' },
            'Order reference': { chip: 'order', key: 'VAS_203_OrderReference', fallback: 'Order reference' },
            'Description': { chip: 'description', key: 'VAS_203_Description', fallback: 'Description' },
            'Location': { chip: 'warehouse', key: 'VAS_203_Location', fallback: 'Location' },
            'Recent': { chip: 'order', key: 'VAS_203_RecentAcrossPurchase', fallback: 'Recent across purchase' }
        };

        function msg(key, fallback) {
            return VIS.Msg.getMsg(key);
        }

        function dsEsc(str) {
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

        /* ---- Initialise ---- */
        this.initalize = function () {
            widgetID = (VIS.Utility.Util.getValueOfInt(this.widgetInfo.AD_UserHomeWidgetID) !== 0
                ? this.widgetInfo.AD_UserHomeWidgetID
                : $self.windowNo);
            createBusyIndicator();
            buildShell();
            $bsyDiv[0].style.visibility = 'hidden';
        };

        this.intialLoad = function () {
            $bsyDiv[0].style.visibility = 'hidden';
        };

        function createBusyIndicator() {
            $bsyDiv = $('<div class="vis-busyindicatorouterwrap vas-widget-busy-wrap"><div class="vis-busyindicatorinnerwrap"><i class="vis_widgetloader"></i></div></div>');
            $bsyDiv[0].style.visibility = 'hidden';
            $root.append($bsyDiv);
        }

        function buildShell() {
            var placeholder = msg('VAS_203_Placeholder',
                'Search product, vendor, location, representative, PO reference, description, warehouse...');

            $bar = $('<div class="vas-dssrch-bar" id="vas_dssrch_bar_' + widgetID + '">');
            $bar.append(
                '<svg class="vas-dssrch-icon" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2" stroke-linecap="round" stroke-linejoin="round">' +
                    '<circle cx="11" cy="11" r="7"></circle><line x1="21" y1="21" x2="16.65" y2="16.65"></line>' +
                '</svg>' +
                '<span class="vas-dssrch-spin"></span>'
            );
            $input = $('<input type="text" class="vas-dssrch-input" autocomplete="off" spellcheck="false">')
                .attr('placeholder', placeholder);
            $bar.append($input);

            var $clear = $('<button type="button" class="vas-dssrch-clear" tabindex="-1">&#215;</button>');
            $bar.append($clear);
            $root.append($bar);

            // Dropdown lives on <body> so the dashboard cell's overflow:hidden
            // cannot clip it; positioned as a fixed popover under the bar.
            $panel = $('<div class="vas-dssrch-panel" id="vas_dssrch_panel_' + widgetID + '">');
            $('body').append($panel);

            wireEvents($clear);
        }

        function wireEvents($clear) {
            $input.on('input', function () {
                var term = $.trim($input.val());
                $bar.toggleClass('vas-dssrch-has-text', term.length > 0);
                scheduleSearch(term);
            });

            $input.on('focus', function () {
                // Focusing must not reveal anything on its own - only re-open a list
                // that a qualifying term already produced.
                var term = $.trim($input.val());
                if (term.length >= MIN_LEN && $panel.children().length > 0) { openPanel(); }
            });

            $input.on('keydown', function (e) {
                if (e.key === 'Escape' || e.keyCode === 27) {
                    closePanel();
                } else if (e.key === 'ArrowDown' || e.keyCode === 40) {
                    e.preventDefault();
                    navigateItems(1);
                } else if (e.key === 'ArrowUp' || e.keyCode === 38) {
                    e.preventDefault();
                    navigateItems(-1);
                } else if (e.key === 'Enter' || e.keyCode === 13) {
                    if (activeItemIndex >= 0 && activeItemIndex < currentItems.length) {
                        e.preventDefault();
                        var sel = currentItems[activeItemIndex];
                        if (sel && sel.orderId) { goToRecord(sel.orderId, sel.orderLineId); }
                    }
                }
            });

            $clear.on('click', function () {
                $input.val('');
                $bar.removeClass('vas-dssrch-has-text');
                setBusy(false);
                closePanel();
                $input.focus();
            });

            $self._onDocClick = function (e) {
                if (!$panel.hasClass('vas-dssrch-open')) { return; }
                if ($bar[0].contains(e.target) || $panel[0].contains(e.target)) { return; }
                closePanel();
            };
            $self._onReflow = function () {
                if ($panel.hasClass('vas-dssrch-open')) { positionPanel(); }
            };
            document.addEventListener('mousedown', $self._onDocClick, true);
            window.addEventListener('resize', $self._onReflow, true);
            window.addEventListener('scroll', $self._onReflow, true);

            $self._teardown = function () {
                closePurchaseOrderDetail();   // QA #20 modal lives on <body>
                document.removeEventListener('mousedown', $self._onDocClick, true);
                window.removeEventListener('resize', $self._onReflow, true);
                window.removeEventListener('scroll', $self._onReflow, true);
                if (debounceTimer) { window.clearTimeout(debounceTimer); debounceTimer = null; }
                if ($panel) { $panel.remove(); $panel = null; }
                $root.remove();
            };
        }

        /* ---- Debounced search ---- */
        function scheduleSearch(term) {
            if (debounceTimer) { window.clearTimeout(debounceTimer); debounceTimer = null; }
            if (term.length === 0) { setBusy(false); closePanel(); return; }
            if (term.length < MIN_LEN) { setBusy(false); renderHint(); return; }
            setBusy(true);
            debounceTimer = window.setTimeout(function () { runSearch(term); }, 280);
        }

        function runSearch(term) {
            var mySeq = ++requestSeq;
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
                        renderResults(payload.results, payload.totalCount || payload.results.length);
                    } else if (res.error || res.success === false) {
                        renderError();
                    } else {
                        renderResults([], 0);
                    }
                },
                error: function () {
                    if (mySeq !== requestSeq) { return; }
                    setBusy(false);
                    renderError();
                }
            });
        }

        /* ---- Rendering (shared vas-dssrch-* markup) ---- */
        function renderHint() {
            currentItems = [];
            activeItemIndex = -1;
            $panel.html(stateHtml(searchSvg(), msg('VAS_DocSearch_TypeToSearch', 'Type at least 2 characters to search'), false));
            openPanel();
        }

        function renderError() {
            currentItems = [];
            activeItemIndex = -1;
            $panel.html(stateHtml(alertSvg(), msg('VAS_DocSearch_Error', 'Search failed. Please try again.'), true));
            openPanel();
        }

        function renderResults(items, totalCount) {
            currentItems = items || [];
            activeItemIndex = -1;

            if (!items || items.length === 0) {
                $panel.html(stateHtml(searchSvg(), msg('VAS_DocSearch_NoResults', 'No matching documents'), false));
                openPanel();
                return;
            }

            var html = '<div class="vas-dssrch-count"></div><div class="vas-dssrch-list">';
            for (var i = 0; i < items.length; i++) {
                html += buildRow(items[i], i);
            }
            html += '</div>';
            $panel.html(html);

            var text = items.length + ((totalCount > items.length) ? '+' : '') + ' ' + msg('VAS_DocSearch_Results', 'results');
            $panel.find('.vas-dssrch-count').text(text);

            bindRowClicks($panel.find('.vas-dssrch-list .vas-dssrch-row'));
            $panel.scrollTop(0);
            openPanel();
        }

        function buildRow(item, idx) {
            var groupName = item.group || item.groupKey || 'Purchase order';
            var cfg = GROUPS[item.groupKey] || GROUPS[groupName] || GROUPS['Purchase order'];
            var label = msg(cfg.key, cfg.fallback);
            var hasZoom = VIS.Utility.Util.getValueOfInt(item.orderId) > 0;

            return (
                '<div class="vas-dssrch-row' + (hasZoom ? '' : ' vas-dssrch-nozoom') + '"' +
                    ' data-idx="' + idx + '"' +
                    ' data-id="' + dsEsc(item.orderId) + '"' +
                    (item.orderLineId ? ' data-line-id="' + dsEsc(item.orderLineId) + '"' : '') + '>' +
                    '<span class="vas-dssrch-chip vas-dssrch-chip-' + dsEsc(cfg.chip) + '">' + dsEsc(label) + '</span>' +
                    '<div class="vas-dssrch-main">' +
                        '<div class="vas-dssrch-docline">' +
                            '<span class="vas-dssrch-docno">' + dsEsc(item.title || '') + '</span>' +
                        '</div>' +
                        '<div class="vas-dssrch-title">' + dsEsc(item.subtitle || '') + '</div>' +
                    '</div>' +
                    '<div class="vas-dssrch-meta">' +
                        '<div class="vas-dssrch-amount">' + dsEsc(item.value || '') + '</div>' +
                    '</div>' +
                '</div>'
            );
        }

        function bindRowClicks($rows) {
            $rows.on('click', function () {
                if ($(this).hasClass('vas-dssrch-nozoom')) { return; }
                goToRecord(
                    VIS.Utility.Util.getValueOfInt($(this).attr('data-id')),
                    VIS.Utility.Util.getValueOfInt($(this).attr('data-line-id'))
                );
            });
            $rows.on('mouseenter', function () {
                setActiveItem(VIS.Utility.Util.getValueOfInt($(this).attr('data-idx')));
            });
        }

        function navigateItems(delta) {
            if (!currentItems.length || !$panel.hasClass('vas-dssrch-open')) { return; }
            var idx = activeItemIndex + delta;
            if (idx < 0) { idx = currentItems.length - 1; }
            if (idx >= currentItems.length) { idx = 0; }
            setActiveItem(idx);
        }

        function setActiveItem(index) {
            activeItemIndex = index;
            var $rows = $panel.find('.vas-dssrch-list .vas-dssrch-row');
            $rows.removeClass('vas-dssrch-row-accent');
            if (index >= 0 && index < $rows.length) {
                $rows.eq(index).addClass('vas-dssrch-row-accent');
            }
        }
        /* QA sheet #20: clicking a result opens the PO Search DETAIL modal for that
           document, not a navigation to the Purchase Order screen. The screen stays
           reachable from the modal's "Open record" button. */
        function goToRecord(orderId, orderLineId) {
            if (!orderId) { return; }
            closePanel();
            openPurchaseOrderDetail(orderId);
        }


        /* Opens the Purchase Order window for a record - used by the detail modal's "Open record". */
        function openRecordInWindow(orderId) {
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
                console.warn("VAS_203_POGlobalSearchWidget: widgetFirevalueChanged failed", ex);
            }

            if (!navigated) {
                try {
                    if (VIS && VIS.AEnv && typeof VIS.AEnv.zoom === 'function') {
                        VIS.AEnv.zoom(ZOOM_TABLE_ID, orderId);
                    }
                } catch (ex2) {
                    console.warn("VAS_203_POGlobalSearchWidget: VIS.AEnv.zoom fallback failed", ex2);
                }
            }
        }

        /* The detail modal (QA #20) keeps its own markup helpers; the search UI above
           uses msg()/dsEsc(), so these aliases keep the ported modal code unchanged. */
        function lbl(key, fallback) { return msg(key, fallback); }
        function escapeHtml(value) { return dsEsc(value); }


        function closePurchaseOrderDetail() {
            $(document).off('keydown.vas-203-dtl');
            if ($detailModal) { $detailModal.remove(); $detailModal = null; }
        }


        function fmtMoney(value, symbol, iso, precision) {
            var num = Number(value || 0);
            var sym = symbol || '';
            if (!sym && iso) { sym = iso; }
            var prec = (typeof precision === 'number' && precision >= 0) ? precision : 2;
            return sym + ' ' + num.toLocaleString(window.navigator.language, { minimumFractionDigits: 0, maximumFractionDigits: prec });
        }


        function fmtQty(value) {
            return Number(value || 0).toLocaleString(window.navigator.language);
        }


        function openPurchaseOrderDetail(orderId) {
            if ($detailModal) { $detailModal.remove(); }

            $detailModal = $(
                '<div class="vas-203-dtl-overlay" role="dialog" aria-modal="true">' +
                    '<div class="vas-203-dtl-card">' +
                        '<div class="vas-203-dtl-head">' +
                            '<div class="vas-203-dtl-headtxt">' +
                                '<h3 class="vas-203-dtl-title">' + escapeHtml(lbl('VAS_Loading', 'Loading...')) + '</h3>' +
                                '<div class="vas-203-dtl-sub"></div>' +
                            '</div>' +
                            '<button type="button" class="vas-203-dtl-close" aria-label="' + escapeHtml(lbl('VAS_Close', 'Close')) + '">&times;</button>' +
                        '</div>' +
                        '<div class="vas-203-dtl-body">' +
                            '<div class="vas-203-dtl-msg">' + escapeHtml(lbl('VAS_Loading', 'Loading...')) + '</div>' +
                        '</div>' +
                    '</div>' +
                '</div>'
            );

            $detailModal.find('.vas-203-dtl-close').on('click', closePurchaseOrderDetail);
            $detailModal.on('click', function (e) {
                if (e.target === this) { closePurchaseOrderDetail(); }
            });
            $(document).on('keydown.vas-203-dtl', function (e) {
                if (e.key === 'Escape' || e.keyCode === 27) { closePurchaseOrderDetail(); }
            });

            $('body').append($detailModal);

            $.ajax({
                url: VIS.Application.contextUrl + 'VAS_203_POGlobalSearchWidget/GetPurchaseOrderDetail',
                type: 'GET',
                data: { orderId: orderId },
                cache: false,
                success: function (res) {
                    var data = parseResponse(res);
                    var payload = data && data.data ? data.data : data;
                    if (!payload || payload.error || !payload.header) {
                        $detailModal.find('.vas-203-dtl-body').html(
                            '<div class="vas-203-dtl-msg">' + escapeHtml(lbl('VAS_CouldntLoad', "Couldn't load")) + '</div>');
                        return;
                    }
                    renderPurchaseOrderDetail(payload.header, payload.lines || []);
                },
                error: function () {
                    if ($detailModal) {
                        $detailModal.find('.vas-203-dtl-body').html(
                            '<div class="vas-203-dtl-msg">' + escapeHtml(lbl('VAS_CouldntLoad', "Couldn't load")) + '</div>');
                    }
                }
            });
        }


        function renderPurchaseOrderDetail(header, lines) {
            var title = header.documentNo || (lbl('VAS_PONo', 'PO No') + ' #' + header.orderId);
            $detailModal.find('.vas-203-dtl-title').text(title);
            $detailModal.find('.vas-203-dtl-title').attr('title', title);
            $detailModal.find('.vas-203-dtl-sub').text(
                (header.vendorName || '—') + ' · ' + (header.orderDateDisplay || '—') + ' · ' + (header.docStatusLabel || ''));

            var totalOrdered = 0, totalPending = 0, totalAmount = 0;
            var k, ln;
            for (k = 0; k < lines.length; k++) {
                ln = lines[k];
                totalOrdered += Number(ln.qtyOrdered || 0);
                totalPending += Number(ln.qtyPending || 0);
                totalAmount += Number(ln.lineNetAmt || 0);
            }

            var amtFmt = fmtMoney(totalAmount, header.currencySymbol, header.currencyIso, header.stdPrecision);

            var html = '<div class="vas-203-dtl-summary">' +
                '<div class="vas-203-dtl-cell"><span class="vas-203-dtl-lbl">' + escapeHtml(lbl('VAS_Vendor', 'Vendor')) + '</span><span class="vas-203-dtl-val">' + escapeHtml(header.vendorName || '—') + '</span></div>' +
                '<div class="vas-203-dtl-cell"><span class="vas-203-dtl-lbl">' + escapeHtml(lbl('VAS_Warehouse', 'Warehouse')) + '</span><span class="vas-203-dtl-val">' + escapeHtml(header.warehouseName || '—') + '</span></div>' +
                '<div class="vas-203-dtl-cell"><span class="vas-203-dtl-lbl">' + escapeHtml(lbl('VAS_Status', 'Status')) + '</span><span class="vas-203-dtl-val">' + escapeHtml(header.docStatusLabel || '—') + '</span></div>' +
                '<div class="vas-203-dtl-cell"><span class="vas-203-dtl-lbl">' + escapeHtml(lbl('VAS_TotalAmount', 'Amount')) + '</span><span class="vas-203-dtl-val">' + escapeHtml(amtFmt) + '</span></div>' +
                '<div class="vas-203-dtl-cell"><span class="vas-203-dtl-lbl">' + escapeHtml(lbl('VAS_QtyOrdered', 'Qty ordered')) + '</span><span class="vas-203-dtl-val">' + escapeHtml(fmtQty(totalOrdered)) + '</span></div>' +
                '<div class="vas-203-dtl-cell"><span class="vas-203-dtl-lbl">' + escapeHtml(lbl('VAS_QtyPending', 'Qty pending')) + '</span><span class="vas-203-dtl-val">' + escapeHtml(fmtQty(totalPending)) + '</span></div>' +
                '</div>';

            html += '<div class="vas-203-dtl-sect">' + escapeHtml(lbl('VAS_PurchaseOrderLines', 'Purchase order lines')) + ' (' + lines.length + ')</div>';

            if (lines.length === 0) {
                html += '<div class="vas-203-dtl-msg">' + escapeHtml(lbl('VAS_203_NoLines', 'No order lines.')) + '</div>';
            } else {
                html += '<div class="vas-203-dtl-tblwrap"><table class="vas-203-dtl-table"><thead><tr>' +
                    '<th>#</th>' +
                    '<th>' + escapeHtml(lbl('VAS_Product', 'Product')) + '</th>' +
                    '<th>' + escapeHtml(lbl('VAS_Attribute', 'Attribute')) + '</th>' +
                    '<th>' + escapeHtml(lbl('VAS_UoM', 'UoM')) + '</th>' +
                    '<th class="r">' + escapeHtml(lbl('VAS_Ordered', 'Ordered')) + '</th>' +
                    '<th class="r">' + escapeHtml(lbl('VAS_Received', 'Received')) + '</th>' +
                    '<th class="r">' + escapeHtml(lbl('VAS_Pending', 'Pending')) + '</th>' +
                    '<th class="r">' + escapeHtml(lbl('VAS_Amount', 'Amount')) + '</th>' +
                    '</tr></thead><tbody>';
                for (k = 0; k < lines.length; k++) {
                    ln = lines[k];
                    html += '<tr>' +
                        '<td>' + escapeHtml(String(ln.lineNo || (k + 1))) + '</td>' +
                        '<td title="' + escapeHtml(ln.productName || '') + '">' + escapeHtml(ln.productName || '—') + '</td>' +
                        '<td>' + escapeHtml(ln.attribute || '—') + '</td>' +
                        '<td>' + escapeHtml(ln.uom || '—') + '</td>' +
                        '<td class="r">' + escapeHtml(fmtQty(ln.qtyOrdered)) + '</td>' +
                        '<td class="r">' + escapeHtml(fmtQty(ln.qtyDelivered)) + '</td>' +
                        '<td class="r">' + escapeHtml(fmtQty(ln.qtyPending)) + '</td>' +
                        '<td class="r" title="' + escapeHtml(fmtMoney(ln.lineNetAmt, header.currencySymbol, header.currencyIso, header.stdPrecision)) + '">' + escapeHtml(fmtMoney(ln.lineNetAmt, header.currencySymbol, header.currencyIso, header.stdPrecision)) + '</td>' +
                        '</tr>';
                }
                html += '</tbody></table></div>';
            }

            html += '<div class="vas-203-dtl-foot">' +
                '<button type="button" class="vas-203-dtl-openbtn">' + escapeHtml(lbl('VAS_OpenRecord', 'Open record')) + '</button>' +
                '<button type="button" class="vas-203-dtl-closebtn">' + escapeHtml(lbl('VAS_Close', 'Close')) + '</button>' +
                '</div>';

            $detailModal.find('.vas-203-dtl-body').html(html);
            $detailModal.find('.vas-203-dtl-openbtn').on('click', function () {
                closePurchaseOrderDetail();
                openRecordInWindow(header.orderId || orderId);
            });
            $detailModal.find('.vas-203-dtl-closebtn').on('click', closePurchaseOrderDetail);
        }

        /* ---- Panel helpers ---- */
        function openPanel() { positionPanel(); $panel.addClass('vas-dssrch-open'); $bar.addClass('vas-dssrch-bar-focus'); }
        function closePanel() { $panel.removeClass('vas-dssrch-open'); $bar.removeClass('vas-dssrch-bar-focus'); activeItemIndex = -1; }
        function positionPanel() {
            if (!$bar || !$bar[0]) { return; }
            var rect = $bar[0].getBoundingClientRect();
            $panel.css({ left: Math.round(rect.left) + 'px', top: Math.round(rect.bottom + 6) + 'px', width: Math.round(rect.width) + 'px' });
        }
        function setBusy(on) { $bar.toggleClass('vas-dssrch-busy', !!on); }

        function searchSvg() {
            return '<svg viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="1.8" stroke-linecap="round" stroke-linejoin="round"><circle cx="11" cy="11" r="7"></circle><line x1="21" y1="21" x2="16.65" y2="16.65"></line></svg>';
        }
        function alertSvg() {
            return '<svg viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="1.8" stroke-linecap="round" stroke-linejoin="round"><circle cx="12" cy="12" r="10"></circle><line x1="12" y1="8" x2="12" y2="12"></line><line x1="12" y1="16" x2="12.01" y2="16"></line></svg>';
        }
        function stateHtml(svg, text, isError) {
            return '<div class="vas-dssrch-state' + (isError ? ' vas-dssrch-state-error' : '') + '">' + svg + dsEsc(text) + '</div>';
        }

        this.refreshWidget = function () {
            if (debounceTimer) { window.clearTimeout(debounceTimer); debounceTimer = null; }
            requestSeq++;
            currentItems = [];
            activeItemIndex = -1;
            if ($input) { $input.val(''); }
            if ($bar) { $bar.removeClass('vas-dssrch-has-text'); }
            setBusy(false);
            closePanel();
        };

        this.getRoot = function () {
            return $root;
        };
    };

    VAS.VAS_203_POGlobalSearchWidget.prototype.init = function (windowNo, frame) {
        this.frame = frame;
        this.widgetInfo = frame.widgetInfo;
        this.windowNo = windowNo;
        this.initalize();
        this.frame.getContentGrid().append(this.getRoot());
        var self = this;
        window.setTimeout(function () { self.intialLoad(); }, 50);
    };

    VAS.VAS_203_POGlobalSearchWidget.prototype.refreshWidget = function () { this.refreshWidget(); };

    VAS.VAS_203_POGlobalSearchWidget.prototype.addChangeListener = function (listener) {
        this.listener = listener;
    };

    VAS.VAS_203_POGlobalSearchWidget.prototype.widgetFirevalueChanged = function (value) {
        if (this.listener && typeof this.listener.widgetFirevalueChanged === 'function') {
            this.listener.widgetFirevalueChanged(value);
        }
    };

    VAS.VAS_203_POGlobalSearchWidget.prototype.widgetSizeChange = function (widget) {
        this.widgetInfo = widget;
    };

    VAS.VAS_203_POGlobalSearchWidget.prototype.dispose = function () {
        if (this._teardown) { this._teardown(); }
        if (this.frame) { this.frame.dispose(); }
        this.frame = null;
        this.windowNo = null;
    };

})(VAS, jQuery);
