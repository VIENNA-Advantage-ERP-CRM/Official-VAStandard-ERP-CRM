/**
 * Positive Variance Qty KPI Widget & Modal (Inventory Count Dashboard)
 * Purpose - Read-only 2x1 glass KPI tile summing all positive count differences (diff > 0).
 *           Clicking opens an interactive paged modal breaking down all contributing lines.
 * Prefix  - VAS_159_
 *
 * Labels / Message Keys Table:
 *  #  | Fallback Text                                    | Message Key
 * ----+--------------------------------------------------+-----------------------------------
 *  1  | Positive Variance Qty                            | VAS_159_PositiveVarianceQty
 *  2  | No positive count variances recorded MTD         | VAS_159_NoPositiveVariancesMTD
 *  3  | line contributing surplus units                  | VAS_159_LineContributingSurplus
 *  4  | lines contributing surplus units                 | VAS_159_LinesContributingSurplus
 *  5  | Document No                                      | VAS_159_DocumentNo
 *  6  | Item / Product                                   | VAS_159_ItemProduct
 *  7  | Warehouse                                        | VAS_159_Warehouse
 *  8  | Locator                                          | VAS_159_Locator
 *  9  | Qty Counted                                      | VAS_159_QtyCounted
 * 10  | Surplus Qty                                      | VAS_159_SurplusQty
 * 11  | Page                                             | VAS_159_Page
 * 12  | of                                               | VAS_159_Of
 * 13  | Close                                            | VAS_159_Close
 * 14  | No positive variance records available           | VAS_159_NoPositiveVarianceRecords
 * 15  | Unable to load positive variance data            | VAS_159_UnableToLoadPositiveVariance
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

    VAS.VAS_159_PositiveVarianceQtyWidget = function () {

        this.frame;
        this.windowNo;

        var $self = this;
        var $wrapper = $('<div class="vas-pos-var-container">');
        var $root = $('<div class="vas-pos-var-root vas-pos-var-clickable" role="button" tabindex="0" aria-haspopup="dialog">');
        var $valueEl;
        var $metaEl;
        var $busy;
        var widgetObserver = null;

        // Modal elements
        var $modalOverlay = null;
        var $modalBody = null;
        var $modalFooterHelper = null;
        var $btnPrev = null;
        var $btnNext = null;
        var $pageText = null;

        var currentPage = 1;
        var currentPageSize = 8;
        var currentTotalRows = 0;
        var isModalOpen = false;

        function lbl(key, fallback) {
            var t = VIS.Msg.getMsg(key);
            return (t && t.charAt(0) !== '[') ? t : fallback;
        }

        function showBusy(show) {
            if (!$busy || !$busy[0]) { return; }
            $busy.toggleClass('vas-pos-var-hidden', !show);
        }

        function formatQty(value) {
            var n = Number(value || 0);
            var str = n.toLocaleString(window.navigator.language);
            return (n >= 0 ? '+' : '') + str;
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
            loadSummary();
        };

        function loadSummary() {
            showBusy(true);

            $.ajax({
                url: VIS.Application.contextUrl + 'VAS_159_PositiveVarianceQtyWidget/GetPositiveVarianceSummary',
                type: 'GET',
                cache: false,
                success: function (res) {
                    var data = typeof res === 'string' ? JSON.parse(res) : res;
                    if (data && typeof data === 'string') { data = JSON.parse(data); }

                    if (data && data.error) { setError(); return; }

                    renderSummary(data || {});
                },
                error: function () { setError(); },
                complete: function () { showBusy(false); }
            });
        }

        function renderSummary(data) {
            var varianceQty = Number(data.varianceQty || 0);

            if ($valueEl) {
                $valueEl.text(formatQty(varianceQty));
                $valueEl.attr('title', formatQty(varianceQty));
            }

            if ($metaEl) {
                var metaMsg = lbl("VAS_159_UnitsCountedOverBook", "units counted over book qty");
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
                var errText = lbl("VAS_159_UnableToLoadPositiveVar", "Unable to load positive variance");
                $metaEl.text(errText);
                $metaEl.attr('title', errText);
            }
        }

        function createWidget() {
            var $card = $(
                '<div class="vas-pos-var-card">' +
                '<div class="vas-pos-var-label">' + escapeHtml(lbl("VAS_159_PositiveVarianceQty", "Positive Variance Qty")) + '</div>' +
                '<div class="vas-pos-var-value">—</div>' +
                '<div class="vas-pos-var-meta"></div>' +
                '</div>'
            );

            $valueEl = $card.find('.vas-pos-var-value');
            $metaEl = $card.find('.vas-pos-var-meta');

            $root.append($card);

            $busy = $('<div class="vas-pos-var-busy vas-pos-var-hidden"><div class="vis-busyindicatorinnerwrap"><i class="vis_widgetloader"></i></div></div>');
            $root.append($busy);

            $wrapper.append($root);
            setupWidgetSizeObserver();
            bindWidgetEvents();
        }

        function bindWidgetEvents() {
            $root.on('click', function (e) {
                e.preventDefault();
                openModal();
            });

            $root.on('keydown', function (e) {
                if (e.key === 'Enter' || e.key === ' ') {
                    e.preventDefault();
                    openModal();
                }
            });
        }

        // ==========================================
        // MODAL BREAKDOWN IMPLEMENTATION
        // ==========================================

        function openModal() {
            if (!($modalOverlay && $modalOverlay.length)) {
                buildModalDOM();
            }

            currentPage = 1;
            calculateAdaptivePageSize();

            $modalOverlay.addClass('vas-pos-var-modal-open');
            isModalOpen = true;

            fetchModalPage(currentPage);

            // Set focus on close button
            setTimeout(function () {
                $modalOverlay.find('.vas-pos-var-modal-close').focus();
            }, 50);
        }

        function closeModal() {
            if ($modalOverlay) {
                $modalOverlay.removeClass('vas-pos-var-modal-open');
            }
            isModalOpen = false;
            $root.focus();
        }

        function calculateAdaptivePageSize() {
            var vh = $(window).height();
            var availableHeight = vh - 172;
            var approxRowHeight = 36;
            var calcRows = Math.floor(availableHeight / approxRowHeight);

            currentPageSize = Math.max(3, Math.min(8, calcRows));
        }

        function buildModalDOM() {
            var modalHtml = $(
                '<div class="vas-pos-var-overlay" role="dialog" aria-modal="true">' +
                '  <div class="vas-pos-var-modal-scrim"></div>' +
                '  <div class="vas-pos-var-dialog">' +
                '    <div class="vas-pos-var-modal-header">' +
                '      <h3>' + escapeHtml(lbl("VAS_159_PosVarLines", "Positive Variance Lines")) + '</h3>' +
                '      <button class="vas-pos-var-modal-close" aria-label="Close dialog">&times;</button>' +
                '    </div>' +
                '    <div class="vas-pos-var-modal-grid-header">' +
                '      <span>' + escapeHtml(lbl("VAS_159_DocNo", "Document No.")) + '</span>' +
                '      <span>' + escapeHtml(lbl("VAS_159_Product", "Product")) + '</span>' +
                '      <span>' + escapeHtml(lbl("VAS_159_WH", "WH")) + '</span>' +
                '      <span>' + escapeHtml(lbl("VAS_159_Locator", "Locator")) + '</span>' +
                '      <span class="vas-pos-var-tr">' + escapeHtml(lbl("VAS_159_Qty", "Qty")) + '</span>' +
                '      <span class="vas-pos-var-tr">' + escapeHtml(lbl("VAS_159_Diff", "Diff")) + '</span>' +
                '    </div>' +
                '    <div class="vas-pos-var-modal-body"></div>' +
                '    <div class="vas-pos-var-modal-footer">' +
                '      <div class="vas-pos-var-footer-helper">Loading...</div>' +
                '      <div class="vas-pos-var-pager">' +
                '        <button class="vas-pos-var-pgbtn vas-pos-var-prev" aria-label="Previous page">' +
                '          <svg viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2"><polyline points="15 18 9 12 15 6"></polyline></svg>' +
                '        </button>' +
                '        <span class="vas-pos-var-pgtext">1 of 1</span>' +
                '        <button class="vas-pos-var-pgbtn vas-pos-var-next" aria-label="Next page">' +
                '          <svg viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2"><polyline points="9 18 15 12 9 6"></polyline></svg>' +
                '        </button>' +
                '      </div>' +
                '    </div>' +
                '  </div>' +
                '</div>'
            );

            $('body').append(modalHtml);
            $modalOverlay = modalHtml;

            $modalBody = $modalOverlay.find('.vas-pos-var-modal-body');
            $modalFooterHelper = $modalOverlay.find('.vas-pos-var-footer-helper');
            $btnPrev = $modalOverlay.find('.vas-pos-var-prev');
            $btnNext = $modalOverlay.find('.vas-pos-var-next');
            $pageText = $modalOverlay.find('.vas-pos-var-pgtext');

            bindModalEvents();
        }

        function bindModalEvents() {
            $modalOverlay.find('.vas-pos-var-modal-close, .vas-pos-var-modal-scrim').on('click', function (e) {
                e.preventDefault();
                closeModal();
            });

            $btnPrev.on('click', function () {
                if (currentPage > 1) {
                    currentPage--;
                    fetchModalPage(currentPage);
                }
            });

            $btnNext.on('click', function () {
                var maxPages = Math.ceil(currentTotalRows / currentPageSize) || 1;
                if (currentPage < maxPages) {
                    currentPage++;
                    fetchModalPage(currentPage);
                }
            });

            $(window).on('keydown.vasPosVarModal', function (e) {
                if (isModalOpen && e.key === 'Escape') {
                    closeModal();
                }
            });

            $(window).on('resize.vasPosVarModal', function () {
                if (isModalOpen) {
                    var oldSize = currentPageSize;
                    calculateAdaptivePageSize();
                    if (oldSize !== currentPageSize) {
                        fetchModalPage(currentPage);
                    }
                }
            });
        }

        function fetchModalPage(page) {
            renderSkeletons();

            $.ajax({
                url: VIS.Application.contextUrl + 'VAS_159_PositiveVarianceQtyWidget/GetPositiveVarianceData',
                type: 'GET',
                data: { page: page, pageSize: currentPageSize },
                cache: false,
                success: function (res) {
                    var data = typeof res === 'string' ? JSON.parse(res) : res;
                    if (data && typeof data === 'string') { data = JSON.parse(data); }

                    if (data && data.rows) {
                        renderModalRows(data);
                    } else {
                        renderModalEmpty();
                    }
                },
                error: function () {
                    renderModalError();
                }
            });
        }

        function renderSkeletons() {
            $modalBody.empty();
            for (var i = 0; i < currentPageSize; i++) {
                $modalBody.append('<div class="vas-pos-var-row vas-pos-var-skel-row"><div class="vas-pos-var-skel-bar"></div></div>');
            }
            $modalFooterHelper.text("Loading...");
        }

        function renderModalRows(data) {
            $modalBody.empty();
            var rows = data.rows || [];
            currentTotalRows = Number(data.totalRows || 0);

            if (rows.length === 0) {
                renderModalEmpty();
                return;
            }

            var totalPages = Math.ceil(currentTotalRows / currentPageSize) || 1;
            currentPage = Math.min(currentPage, totalPages);

            for (var i = 0; i < rows.length; i++) {
                var item = rows[i];
                var productTooltip = item.productCode ? (item.productCode + ' - ' + item.productName) : item.productName;
                var locatorDisplay = item.locator || '—';

                var $row = $(
                    '<div class="vas-pos-var-row" role="button" tabindex="0" data-inv-id="' + item.mInventoryId + '" data-line-id="' + item.mInventoryLineId + '">' +
                    '  <span class="vas-pos-var-cell-doc" title="' + escapeHtml(item.documentNo) + '">' + escapeHtml(item.documentNo) + '</span>' +
                    '  <span class="vas-pos-var-cell" title="' + escapeHtml(productTooltip) + '">' + escapeHtml(item.productName) + '</span>' +
                    '  <span class="vas-pos-var-cell" title="' + escapeHtml(item.warehouse) + '">' + escapeHtml(item.warehouse) + '</span>' +
                    '  <span class="vas-pos-var-cell" title="' + escapeHtml(locatorDisplay) + '">' + escapeHtml(locatorDisplay) + '</span>' +
                    '  <span class="vas-pos-var-cell vas-pos-var-tr" title="' + item.qtyCount + '">' + Number(item.qtyCount).toLocaleString() + '</span>' +
                    '  <span class="vas-pos-var-cell-diff vas-pos-var-tr" title="' + formatQty(item.differenceQty) + '">' + escapeHtml(formatQty(item.differenceQty)) + '</span>' +
                    '</div>'
                );

                (function ($r, record) {
                    $r.on('click keydown', function (e) {
                        if (e.type === 'click' || e.key === 'Enter' || e.key === ' ') {
                            e.preventDefault();
                            navigateToRecord(record.mInventoryId, record.mInventoryLineId);
                        }
                    });
                })($row, item);

                $modalBody.append($row);
            }

            var startIdx = ((currentPage - 1) * currentPageSize) + 1;
            var endIdx = Math.min(currentTotalRows, currentPage * currentPageSize);
            $modalFooterHelper.text("Showing " + startIdx + "–" + endIdx + " of " + currentTotalRows);
            $pageText.text(currentPage + " of " + totalPages);

            $btnPrev.prop('disabled', currentPage <= 1);
            $btnNext.prop('disabled', currentPage >= totalPages);
        }

        function renderModalEmpty() {
            $modalBody.html('<div class="vas-pos-var-empty">' + escapeHtml(lbl("VAS_159_NoPosVarLinesMonth", "No positive variance lines this month")) + '</div>');
            $modalFooterHelper.text("Showing 0 of 0");
            $pageText.text("1 of 1");
            $btnPrev.prop('disabled', true);
            $btnNext.prop('disabled', true);
        }

        function renderModalError() {
            $modalBody.html('<div class="vas-pos-var-error">' + escapeHtml(lbl("VAS_159_UnableToLoadModalData", "Unable to load positive variance lines.")) + ' <button class="vas-pos-var-retry-btn">Retry</button></div>');
            $modalBody.find('.vas-pos-var-retry-btn').on('click', function () {
                fetchModalPage(currentPage);
            });
            $modalFooterHelper.text("Error");
        }

        function navigateToRecord(mInventoryId, mInventoryLineId) {
            closeModal();

            if (VIS && VIS.viewManager) {
                var windowId = 168;
                var query = new VIS.Query();
                query.addRestriction("M_Inventory_ID", "==", mInventoryId);
                VIS.viewManager.startWindow(windowId, query);
            }
        }

        this.refreshData = function () {
            loadSummary();
        };

        this.getRoot = function () { return $wrapper; };

        this.disposeComponent = function () {
            if (widgetObserver) {
                widgetObserver.disconnect();
                widgetObserver = null;
            }
            if ($modalOverlay) {
                $(window).off('.vasPosVarModal');
                $modalOverlay.remove();
                $modalOverlay = null;
            }
            $wrapper.remove();
        };
    };

    VAS.VAS_159_PositiveVarianceQtyWidget.prototype.init = function (windowNo, frame) {
        this.frame = frame;
        this.AD_UserHomeWidgetID = frame.widgetInfo.AD_UserHomeWidgetID;
        this.windowNo = windowNo;
        this.Initalize();
        this.frame.getContentGrid().append(this.getRoot());
        ensureDashInlineSizeVar(this.getRoot());
    };

    VAS.VAS_159_PositiveVarianceQtyWidget.prototype.widgetSizeChange = function (height, width) { };

    VAS.VAS_159_PositiveVarianceQtyWidget.prototype.refreshWidget = function () {
        this.refreshData();
    };

    VAS.VAS_159_PositiveVarianceQtyWidget.prototype.dispose = function () {
        this.disposeComponent();
        if (this.frame) { this.frame.dispose(); }
        this.frame = null;
    };

})(VAS, jQuery);
