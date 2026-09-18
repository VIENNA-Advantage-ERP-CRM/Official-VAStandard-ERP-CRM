/**
 * Counted MTD Widget (Physical Inventory / Inventory Count Dashboard)
 * Purpose - Read-only 2x1 glass KPI tile showing month-to-date completed count lines
 *           and distinct products counted.
 * Prefix  - VAS_156_
 *
 * Labels / Message Keys Table:
 *  #  | Fallback Text                                    | Message Key
 * ----+--------------------------------------------------+-----------------------------------
 *  1  | Counted MTD                                      | VAS_156_CountedMTD
 *  2  | No products counted yet this month               | VAS_156_NoProductsCounted
 *  3  | product counted this month                       | VAS_156_ProductCountedMonth
 *  4  | products counted this month                      | VAS_156_ProductsCountedMonth
 *  5  | Unable to load count data                        | VAS_156_UnableToLoadData
 */
; VAS = window.VAS || {};

; (function (VAS, $) {

    // Window name/search key for the Inventory Count screen, same target VAS_158 navigates to.
    var COUNT_WINDOW_NAME = "VAS_PhysicalInventory";
    /* Lines the popup holds before paging. MUST stay in step with --vas-rows in
       VAS_156_CountedMTDWidget.css, which sizes the dialog to exactly this many rows. */
    var MODAL_PAGE_SIZE = 7;

    VAS.VAS_156_CountedMTDWidget = function () {

        this.frame;
        this.windowNo;

        var $self = this;
        var $wrapper = $('<div class="vas-counted-mtd-container">');
        var $root = $('<div class="vas-counted-mtd-root">');
        var $valueEl;
        var $metaEl;
        var $busy;
        var widgetObserver = null;
        var $modal = null;
        var detailRows = [];
        var modalPageNo = 1;
        var modalTotalPages = 1;

        function lbl(key, fallback) {
            var t = VIS.Msg.getMsg(key);
            return (t && t.charAt(0) !== '[') ? t : fallback;
        }

        function showBusy(show) {
            if (!$busy || !$busy[0]) { return; }
            $busy.toggleClass('vas-counted-mtd-hidden', !show);
        }

        function formatCount(value) {
            return Number(value || 0).toLocaleString(window.navigator.language);
        }

        function escapeHtml(value) {
            return String(value == null ? "" : value)
                .replace(/&/g, "&amp;")
                .replace(/</g, "&lt;")
                .replace(/>/g, "&gt;")
                .replace(/"/g, "&quot;")
                .replace(/'/g, "&#039;");
        }

        function setupWidgetSizeObserver() {
            if (typeof ResizeObserver === 'undefined') { return; }
            widgetObserver = new ResizeObserver(function (entries) {
                for (var i = 0; i < entries.length; i++) {
                    var width = entries[i].contentRect.width;
                    if (width > 0) {
                        $root[0].style.setProperty('--widget-inline-size', width + 'px');
                    }
                }
            });
            widgetObserver.observe($wrapper[0]);
        }

        this.Initalize = function () {
            createWidget();
            loadKpi();
        };

        function loadKpi() {
            showBusy(true);

            $.ajax({
                url: VIS.Application.contextUrl + 'VAS_156_CountedMTDWidget/GetCountedMTDData',
                type: 'GET',
                cache: false,
                success: function (res) {
                    var data = typeof res === 'string' ? JSON.parse(res) : res;
                    if (data && typeof data === 'string') { data = JSON.parse(data); }

                    if (data && data.error) { setError(); return; }

                    renderMetric(data || {});
                },
                error: function () { setError(); },
                complete: function () { showBusy(false); }
            });
        }

        function renderMetric(data) {
            var lines = Number(data.countedLines || 0);
            var products = Number(data.productsCounted || 0);

            if ($valueEl) {
                $valueEl.text(formatCount(lines));
                $valueEl.attr('title', formatCount(lines));
            }

            if ($metaEl) {
                var metaMsg = "";
                if (products === 0) {
                    metaMsg = lbl("VAS_156_NoProductsCounted", "No products counted yet this month");
                } else if (products === 1) {
                    metaMsg = "1 " + lbl("VAS_156_ProductCountedMonth", "product counted this month");
                } else {
                    metaMsg = formatCount(products) + " " + lbl("VAS_156_ProductsCountedMonth", "products counted this month");
                }
                $metaEl.text(metaMsg);
                $metaEl.attr('title', metaMsg);
            }
        }

        function setError() {
            if ($valueEl) {
                $valueEl.text('—');
                $valueEl.removeAttr('title');
            }
            if ($metaEl) {
                var errText = lbl("VAS_156_UnableToLoadData", "Unable to load count data");
                $metaEl.text(errText);
                $metaEl.attr('title', errText);
            }
        }

        /* ---- Drill-down modal ------------------------------------------------------------------
           Mirrors VAS_158_OpenCountSheetsWidget: same dialog chrome, same shared CSS grid for the
           header and data rows, same 24px/14px pager, row click navigates to VAS_PhysicalInventory
           at that document.

           NOTE: the source prompt (01-counted-mtd-claude-prompt.txt) says "This widget is
           non-interactive. Do not add a click action, hover lift, modal, row navigation, or button
           cursor." The user requested the modal on 2026-08-16; user instruction wins. */

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

        function openInventoryRecord(inventoryId) {
            if (!inventoryId) { return; }
            $self.widgetFirevalueChanged({
                "TabWhereClause": "M_Inventory.M_Inventory_ID=" + Number(inventoryId),
                "TabLayout": "Y",
                "TabIndex": "0",
                "ActionName": hostWindowName() || COUNT_WINDOW_NAME,
                "ActionType": "W"
            });
        }

        /* The status chip was rendering "Completed;" / "Closed;".

           VIS.Msg.getMsg returns VIS.I18N.labels[key] split on the six-space MsgText/MsgTip
           separator and hands back the MsgText half - and the seeded MsgText for these
           document-status labels carries a trailing separator character. It is part of the label
           data, not of the status, so it is stripped at the point of display. Harmless when the
           label has no trailing punctuation. */
        function stripTrailingSeparator(text) {
            return String(text == null ? '' : text).replace(/[\s;:,]+$/, '');
        }

        function statusLabel(code) {
            if (code === 'CO') { return stripTrailingSeparator(lbl("Completed", "Completed")); }
            if (code === 'CL') { return stripTrailingSeparator(lbl("Closed", "Closed")); }
            return code || '';
        }

        /* Invisible spacers so a short page occupies the same height as a full one. */
        function fillerRowsHtml(count) {
            var html = '';
            for (var f = 0; f < count; f++) {
                html += '<div class="vas-counted-mtd-grid-row vas-counted-mtd-data-row vas-counted-mtd-filler" aria-hidden="true">' +
                    '<div class="vas-counted-mtd-cell">&nbsp;</div>' +
                    '<div class="vas-counted-mtd-cell">&nbsp;</div>' +
                    '<div class="vas-counted-mtd-cell">&nbsp;</div>' +
                    '<div class="vas-counted-mtd-cell">&nbsp;</div>' +
                    '<div class="vas-counted-mtd-cell">&nbsp;</div>' +
                    '<div class="vas-counted-mtd-cell">&nbsp;</div>' +
                    '</div>';
            }
            return html;
        }

        function renderModalPage() {
            if (!$modal) { return; }

            var $tbody = $modal.find('.vas-counted-mtd-rows');
            var total = detailRows.length;
            modalTotalPages = Math.max(1, Math.ceil(total / MODAL_PAGE_SIZE));
            if (modalPageNo > modalTotalPages) { modalPageNo = modalTotalPages; }
            if (modalPageNo < 1) { modalPageNo = 1; }

            if (total === 0) {
                $tbody.html(fillerRowsHtml(Math.max(0, MODAL_PAGE_SIZE - 1)) +
                    '<div class="vas-counted-mtd-empty">' +
                    escapeHtml(lbl("VAS_156_NoProductsCounted", "No products counted yet this month")) + '</div>');
                $modal.find('.vas-counted-mtd-footer-text').text('');
                $modal.find('.vas-counted-mtd-pager').hide();
                return;
            }

            var start = (modalPageNo - 1) * MODAL_PAGE_SIZE;
            var end = Math.min(total, start + MODAL_PAGE_SIZE);
            var html = '';

            for (var i = start; i < end; i++) {
                var r = detailRows[i];
                html +=
                    '<div class="vas-counted-mtd-grid-row vas-counted-mtd-data-row" data-invid="' + Number(r.InventoryId) + '">' +
                    '<div class="vas-counted-mtd-cell vas-counted-mtd-cell-doc" title="' + escapeHtml(r.DocumentNo) + '">' + escapeHtml(r.DocumentNo) + '</div>' +
                    '<div class="vas-counted-mtd-cell" title="' + escapeHtml(r.Warehouse) + '">' + escapeHtml(r.Warehouse) + '</div>' +
                    '<div class="vas-counted-mtd-cell">' + escapeHtml(r.MovementDate) + '</div>' +
                    '<div class="vas-counted-mtd-cell vas-counted-mtd-cell-num">' + formatCount(r.Lines) + '</div>' +
                    '<div class="vas-counted-mtd-cell vas-counted-mtd-cell-num">' + formatCount(r.Products) + '</div>' +
                    '<div class="vas-counted-mtd-cell"><span class="vas-counted-mtd-status-chip">' + escapeHtml(statusLabel(r.DocStatus)) + '</span></div>' +
                    '</div>';
            }

            $tbody.html(html + fillerRowsHtml(Math.max(0, MODAL_PAGE_SIZE - (end - start))));
            $modal.find('.vas-counted-mtd-footer-text').text(
                (start + 1) + '–' + end + ' ' + lbl("VAS_Of", "of") + ' ' + total);
            $modal.find('.vas-counted-mtd-pager-info').text(modalPageNo + ' / ' + modalTotalPages);
            $modal.find('.vas-counted-mtd-prev').prop('disabled', modalPageNo <= 1);
            $modal.find('.vas-counted-mtd-next').prop('disabled', modalPageNo >= modalTotalPages);
            $modal.find('.vas-counted-mtd-pager').toggle(modalTotalPages > 1);
        }

        function openDetailModal() {
            $(document).off('keydown.vas-counted-mtd');
            if ($modal) { $modal.remove(); }

            $modal = $(
                '<div class="vas-counted-mtd-modal-overlay" role="dialog" aria-modal="true">' +
                '<div class="vas-counted-mtd-modal-dialog">' +
                '<div class="vas-counted-mtd-modal-header">' +
                '<div class="vas-counted-mtd-modal-title">' + escapeHtml(lbl("VAS_156_CountedMTD", "Counted MTD")) + '</div>' +
                '<button type="button" class="vas-counted-mtd-modal-close" aria-label="' + escapeHtml(lbl("VAS_Close", "Close")) + '">&times;</button>' +
                '</div>' +
                '<div class="vas-counted-mtd-modal-body">' +
                '<div class="vas-counted-mtd-grid-row vas-counted-mtd-header-row">' +
                '<div class="vas-counted-mtd-th">' + escapeHtml(lbl("VAS_DocNo", "Document No")) + '</div>' +
                '<div class="vas-counted-mtd-th">' + escapeHtml(lbl("VAS_Warehouse", "Warehouse")) + '</div>' +
                '<div class="vas-counted-mtd-th">' + escapeHtml(lbl("VAS_Date", "Date")) + '</div>' +
                '<div class="vas-counted-mtd-th vas-counted-mtd-th-right">' + escapeHtml(lbl("VAS_Lines", "Lines")) + '</div>' +
                '<div class="vas-counted-mtd-th vas-counted-mtd-th-right">' + escapeHtml(lbl("VAS_Products", "Products")) + '</div>' +
                '<div class="vas-counted-mtd-th">' + escapeHtml(lbl("VAS_Status", "Status")) + '</div>' +
                '</div>' +
                '<div class="vas-counted-mtd-rows"></div>' +
                '<div class="vas-counted-mtd-modal-footer">' +
                '<div class="vas-counted-mtd-footer-text"></div>' +
                '<div class="vas-counted-mtd-pager">' +
                '<button type="button" class="vas-counted-mtd-pager-btn vas-counted-mtd-prev">&lsaquo;</button>' +
                '<span class="vas-counted-mtd-pager-info">1 / 1</span>' +
                '<button type="button" class="vas-counted-mtd-pager-btn vas-counted-mtd-next">&rsaquo;</button>' +
                '</div>' +
                '</div>' +
                '</div>' +
                '</div>' +
                '</div>'
            );

            function closeModal() {
                $(document).off('keydown.vas-counted-mtd');
                if ($modal) { $modal.remove(); $modal = null; }
            }

            // Close button and scrim need separate handlers: the button's own glyph is the event
            // target, so an `e.target === this` guard on a combined handler makes it a dead zone.
            $modal.find('.vas-counted-mtd-modal-close').on('click', function (e) {
                e.stopPropagation();
                closeModal();
            });
            $modal.on('click', function (e) { if (e.target === this) { closeModal(); } });
            $(document).on('keydown.vas-counted-mtd', function (e) {
                if (e.key === 'Escape' || e.keyCode === 27) { closeModal(); }
            });

            $modal.find('.vas-counted-mtd-prev').on('click', function () {
                if (modalPageNo > 1) { modalPageNo--; renderModalPage(); }
            });
            $modal.find('.vas-counted-mtd-next').on('click', function () {
                if (modalPageNo < modalTotalPages) { modalPageNo++; renderModalPage(); }
            });

            $modal.on('click', '.vas-counted-mtd-data-row', function () {
                var invId = Number($(this).data('invid') || 0);
                closeModal();
                openInventoryRecord(invId);
            });

            $('body').append($modal);
            $modal.find('.vas-counted-mtd-rows').html(
                '<div class="vas-counted-mtd-empty">' + escapeHtml(lbl("VAS_Loading", "Loading...")) + '</div>');

            $.ajax({
                url: VIS.Application.contextUrl + 'VAS_156_CountedMTDWidget/GetCountedMTDDetails',
                type: 'GET',
                cache: false,
                success: function (res) {
                    var data = res;
                    if (typeof data === 'string') { data = JSON.parse(data); }
                    if (typeof data === 'string') { data = JSON.parse(data); }
                    detailRows = (data && data.data) ? data.data : [];
                    modalPageNo = 1;
                    renderModalPage();
                },
                error: function () {
                    detailRows = [];
                    renderModalPage();
                }
            });
        }

        function createWidget() {
            var $card = $(
                '<div class="vas-counted-mtd-card vas-counted-mtd-clickable" role="button" tabindex="0">' +
                '<div class="vas-counted-mtd-label">' + escapeHtml(lbl("VAS_156_CountedMTD", "Counted MTD")) + '</div>' +
                '<div class="vas-counted-mtd-value">—</div>' +
                '<div class="vas-counted-mtd-meta"></div>' +
                '</div>'
            );

            $valueEl = $card.find('.vas-counted-mtd-value');
            $metaEl = $card.find('.vas-counted-mtd-meta');

            $card.on('click', function () { openDetailModal(); });
            $card.on('keydown', function (e) {
                if (e.key === 'Enter' || e.key === ' ' || e.keyCode === 13 || e.keyCode === 32) {
                    e.preventDefault();
                    openDetailModal();
                }
            });

            $root.append($card);

            $busy = $('<div class="vas-counted-mtd-busy vas-counted-mtd-hidden"><div class="vis-busyindicatorinnerwrap"><i class="vis_widgetloader"></i></div></div>');
            $root.append($busy);

            $wrapper.append($root);
            setupWidgetSizeObserver();
        }

        this.refreshWidget = function () {
            loadKpi();
        };

        this.getRoot = function () { return $wrapper; };

        this.disposeComponent = function () {
            if (widgetObserver) {
                widgetObserver.disconnect();
                widgetObserver = null;
            }
            // The modal lives on <body>, so detaching $wrapper alone would leak it.
            $(document).off('keydown.vas-counted-mtd');
            if ($modal) { $modal.remove(); $modal = null; }
            $wrapper.remove();
        };
    };

    // Required for the drill-down navigation channel; neither existed on this widget before.
    VAS.VAS_156_CountedMTDWidget.prototype.widgetFirevalueChanged = function (value) {
        if (this.listener) { this.listener.widgetFirevalueChanged(value); }
    };

    VAS.VAS_156_CountedMTDWidget.prototype.addChangeListener = function (listener) {
        this.listener = listener;
    };

    VAS.VAS_156_CountedMTDWidget.prototype.init = function (windowNo, frame) {
        this.frame = frame;
        this.AD_UserHomeWidgetID = frame.widgetInfo.AD_UserHomeWidgetID;
        this.windowNo = windowNo;
        this.Initalize();
        this.frame.getContentGrid().append(this.getRoot());
    };

    VAS.VAS_156_CountedMTDWidget.prototype.widgetSizeChange = function (height, width) { };

    VAS.VAS_156_CountedMTDWidget.prototype.refreshWidget = function () {
        this.refreshWidget();
    };

    VAS.VAS_156_CountedMTDWidget.prototype.dispose = function () {
        this.disposeComponent();
        if (this.frame) { this.frame.dispose(); }
        this.frame = null;
    };

})(VAS, jQuery);


