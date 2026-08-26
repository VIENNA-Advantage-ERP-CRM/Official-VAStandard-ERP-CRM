/**
 * Transfers by Warehouse Widget (Material Transfer Dashboard)
 * Purpose - 3x2 bar-list widget showing total stock moved per warehouse for a
 *           selected month/year. Each row is clickable and opens a modal with
 *           per-transfer detail. Month / Year selects choose the period,
 *           matching VAS_165_LocationWiseInventoryCountWidget.
 * Prefix  - VAS_173_
 */
; VAS = window.VAS || {};

; (function (VAS, $) {

    function ensureDashInlineSizeVar($el) {
        if (window.__vasDashInlineSizeObserver) { return; }
        if (typeof ResizeObserver === 'undefined') { return; }
        var container = $el.closest('.vis-widget-container, [data-dashboard-container]')[0];
        if (!container) { return; }
        var write = function () {
            document.documentElement.style.setProperty('--dash-inline-size', container.clientWidth + 'px');
        };
        window.__vasDashInlineSizeObserver = new ResizeObserver(write);
        window.__vasDashInlineSizeObserver.observe(container);
        write();
    }

    function lbl(key, fallback) {
        var t = VIS.Msg.getMsg(key);
        return (t && t.charAt(0) !== '[') ? t : fallback;
    }

    function escapeHtml(v) {
        return String(v == null ? '' : v)
            .replace(/&/g, '&amp;').replace(/</g, '&lt;')
            .replace(/>/g, '&gt;').replace(/"/g, '&quot;').replace(/'/g, '&#039;');
    }

    function monthNames() {
        return lbl('VAS_173_Months', 'Jan,Feb,Mar,Apr,May,Jun,Jul,Aug,Sep,Oct,Nov,Dec').split(',');
    }

    function formatDate(iso) {
        if (!iso) { return '—'; }
        var d = new Date(iso);
        if (isNaN(d)) { return iso; }
        return d.getDate() + ' ' + monthNames()[d.getMonth()] + ' ' + d.getFullYear();
    }

// ===== NEW CODE START — currency format (agent C07, 2026-08-19) =====
    function formatCurrency(val, currencyObj) {
        var num = parseFloat(val);
        if (isNaN(num) || num === null || num === undefined) { num = 0; }
        var iso = (currencyObj && currencyObj.iso) ? currencyObj.iso.toUpperCase() : '';
        var symbol = (currencyObj && currencyObj.symbol) ? currencyObj.symbol : (iso || '$');

        var isIndian = ['INR', 'PKR', 'BDT', 'NPR', 'BTN', 'LKR'].indexOf(iso) !== -1;
        var formattedNum = '';

        var absVal = Math.abs(num);
        if (isIndian) {
            if (absVal >= 10000000) {
                formattedNum = (num / 10000000).toFixed(2) + ' Cr';
            } else if (absVal >= 100000) {
                formattedNum = (num / 100000).toFixed(2) + ' L';
            } else {
                formattedNum = num.toLocaleString('en-IN', { minimumFractionDigits: 2, maximumFractionDigits: 2 });
            }
        } else {
            if (absVal >= 1000000000) {
                formattedNum = (num / 1000000000).toFixed(2) + ' B';
            } else if (absVal >= 1000000) {
                formattedNum = (num / 1000000).toFixed(2) + ' M';
            } else {
                formattedNum = num.toLocaleString(window.navigator.language || 'en-US', { minimumFractionDigits: 2, maximumFractionDigits: 2 });
            }
        }

        return symbol + ' ' + formattedNum;
    }
// ===== NEW CODE END — currency format =====


    var PALETTE = ['#9ECBF5', '#A7E3C9', '#F6CBA0', '#CBBDF0', '#F5B8C9'];

    VAS.VAS_173_TransfersByWarehouseWidget = function () {

        this.frame;
        this.windowNo;

        var $self = this;
        var $wrapper = $('<div class="vas-173-container">');
        var $root = $('<div class="vas-173-root">');
        var widgetObserver = null;

        var now = new Date();
        var selMonth = now.getMonth() + 1;
        var selYear  = now.getFullYear();

        var allWarehouses = [];
        var PAGE_SIZE = 4;
        var currentPage = 1;

        var $modal = null;
        var activeWH = null;
        var modalPage = 1;
        var MODAL_PAGE_SIZE = 8;

        var YEAR_MIN = 2024;

        function buildWidget() {
            var monthLabel = monthNames()[selMonth - 1] + ' ' + selYear;
            $root.html(
                '<div class="vas-173-header-row">' +
                    '<div class="vas-173-left-cluster">' +
                        '<div class="vas-173-icon-well">' +
                            '<svg viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2" stroke-linecap="round" stroke-linejoin="round">' +
                                '<path d="M22 8.35V20a2 2 0 0 1-2 2H4a2 2 0 0 1-2-2V8.35A2 2 0 0 1 3.26 6.5l8-4a2 2 0 0 1 1.48 0l8 4A2 2 0 0 1 22 8.35z"/>' +
                                '<path d="M6 18h12v-6H6v6z"/>' +
                            '</svg>' +
                        '</div>' +
                        '<div class="vas-173-title-block">' +
                            '<div class="vas-173-title">' + escapeHtml(lbl('VAS_173_TransfersByWarehouse', 'Transfers by Warehouse')) + '</div>' +
                            '<div class="vas-173-subtitle" id="vas173-sub">' + escapeHtml(lbl('VAS_173_StockMoved', 'Stock moved') + ' · ' + monthLabel + ' (qty)') + '</div>' +
                        '</div>' +
                    '</div>' +
                    '<div class="vas-173-filter-cluster">' +
                        '<select class="vas-173-select" id="vas173-m-sel" aria-label="' + escapeHtml(lbl('VAS_173_Month', 'Month')) + '">' + monthOptions() + '</select>' +
                        '<select class="vas-173-select" id="vas173-y-sel" aria-label="' + escapeHtml(lbl('VAS_173_Year', 'Year')) + '">' + yearOptions() + '</select>' +
                    '</div>' +
                '</div>' +
                '<div class="vas-173-body"></div>' +
                '<div class="vas-173-footer"></div>'
            );

            $root.find('#vas173-m-sel').on('change', function () {
                selMonth = parseInt($(this).val(), 10);
                currentPage = 1;
                updatePeriodLabel();
                loadData();
            });

            $root.find('#vas173-y-sel').on('change', function () {
                selYear = parseInt($(this).val(), 10);
                currentPage = 1;
                updatePeriodLabel();
                loadData();
            });

            $wrapper.append($root);

            // Self-Sizing Observer — feeds --widget-inline-size, which the root
            // font-size clamp reads. Without it the CSS falls back to 380px and
            // the whole widget renders smaller than VAS_165 / VAS_161.
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

        /* Option builders for the Month / Year selects (VAS_165 pattern). */
        function monthOptions() {
            var months = monthNames();
            var html = '';
            for (var m = 1; m <= 12; m++) {
                html += '<option value="' + m + '"' + (m === selMonth ? ' selected' : '') + '>' +
                        escapeHtml(months[m - 1]) + '</option>';
            }
            return html;
        }

        function yearOptions() {
            var html = '';
            for (var y = now.getFullYear(); y >= YEAR_MIN; y--) {
                html += '<option value="' + y + '"' + (y === selYear ? ' selected' : '') + '>' + y + '</option>';
            }
            return html;
        }

        function updatePeriodLabel() {
            var text = monthNames()[selMonth - 1] + ' ' + selYear;
            $root.find('#vas173-sub').text(lbl('VAS_173_StockMoved', 'Stock moved') + ' · ' + text + ' (qty)');
        }

        function renderList() {
            var $body = $root.find('.vas-173-body');
            var $footer = $root.find('.vas-173-footer');
            var total = allWarehouses.length;
            var totalPages = Math.max(1, Math.ceil(total / PAGE_SIZE));
            currentPage = Math.min(currentPage, totalPages);

            if (total === 0) {
                $body.html('<div class="vas-173-empty">' + escapeHtml(lbl('VAS_173_NoTransfers', 'No transfers in') + ' ' + monthNames()[selMonth - 1] + ' ' + selYear) + '</div>');
                renderFooter($footer, 0, 0, 0, 1, 1);
                return;
            }

            var start = (currentPage - 1) * PAGE_SIZE;
            var end = Math.min(start + PAGE_SIZE, total);
            var slice = allWarehouses.slice(start, end);

            var html = '<div class="vas-173-list">';
            for (var i = 0; i < slice.length; i++) {
                var wh = slice[i];
                var color = PALETTE[wh.PaletteIndex % PALETTE.length];
                var movesTxt = wh.Moves === 1 ? ('1 ' + lbl('VAS_173_Move', 'move')) : (wh.Moves + ' ' + lbl('VAS_173_Moves', 'moves'));

                html += '<button type="button" class="vas-173-row" data-idx="' + (start + i) + '">' +
                    '<span class="vas-173-dot" style="background-color:' + color + '"></span>' +
                    '<span class="vas-173-wh-name" title="' + escapeHtml(wh.WarehouseName) + '">' + escapeHtml(wh.WarehouseName) + '</span>' +
                    '<span class="vas-173-moves-meta">' + escapeHtml(movesTxt) + '</span>' +
                    '<div class="vas-173-bar-track"><div class="vas-173-bar-fill" style="width:' + (wh.BarFill || 0) + '%; background-color:' + color + '"></div></div>' +
                    '<div class="vas-173-qty-share">' +
                        '<span class="vas-173-qty-val">' + Number(wh.TotalQty || 0).toLocaleString(window.navigator.language) + '</span>' +
                        '<span class="vas-173-share-pct">' + (wh.Share || 0) + '%</span>' +
                    '</div>' +
                '</button>';
            }
            html += '</div>';
            $body.html(html);

            $body.find('.vas-173-row').on('click', function () {
                var idx = parseInt($(this).attr('data-idx'), 10);
                openModal(allWarehouses[idx]);
            });

            renderFooter($footer, start + 1, end, total, currentPage, totalPages);
        }

        function renderFooter($footer, s, e, total, page, totalPages) {
            var showingTxt = total === 0 ? '' :
                lbl('VAS_173_Showing', 'Showing') + ' ' + s + '–' + e + ' ' + lbl('VAS_173_Of', 'of') + ' ' + total;
            $footer.html(
                '<span class="vas-173-footer-helper">' + escapeHtml(showingTxt) + '</span>' +
                '<span class="vas-173-pager">' +
                    '<button type="button" class="vas-173-pager-btn" id="vas173-prev"' + (page <= 1 ? ' disabled' : '') + '>&#8249;</button>' +
                    '<span class="vas-173-pager-label">' + page + ' ' + lbl('VAS_173_Of', 'of') + ' ' + totalPages + '</span>' +
                    '<button type="button" class="vas-173-pager-btn" id="vas173-next"' + (page >= totalPages ? ' disabled' : '') + '>&#8250;</button>' +
                '</span>'
            );
            $footer.find('#vas173-prev').on('click', function () { if (currentPage > 1) { currentPage--; renderList(); } });
            $footer.find('#vas173-next').on('click', function () { if (currentPage < totalPages) { currentPage++; renderList(); } });
        }

        var orgCurrency = null;

        function loadData() {
            $.ajax({
                url: VIS.Application.contextUrl + 'VAS_173_TransfersByWarehouseWidget/GetWarehouseTransfers',
                type: 'GET',
                data: { month: selMonth, year: selYear },
                cache: false,
                success: function (res) {
                    var data = typeof res === 'string' ? JSON.parse(res) : res;
                    if (data && typeof data === 'string') { data = JSON.parse(data); }
                    if (data && data.currency) { orgCurrency = data.currency; }
                    var raw = (data && !data.error && Array.isArray(data.warehouses)) ? data.warehouses : [];
                    
                    for (var i = 0; i < raw.length; i++) {
                        raw[i].PaletteIndex = i;
                    }
                    allWarehouses = raw;
                    currentPage = 1;
                    renderList();
                },
                error: function () {
                    allWarehouses = [];
                    renderList();
                }
            });
        }

        function openModal(wh) {
            if (!wh) { return; }
            activeWH = wh;
            modalPage = 1;
            renderModal();
        }

        function renderModal() {
            var wh = activeWH;
            var color = PALETTE[wh.PaletteIndex % PALETTE.length];
            var periodTxt = monthNames()[selMonth - 1] + ' ' + selYear;
            var transfers = wh.Transfers || [];
            var totalLines = transfers.length;
            var totalPages = Math.max(1, Math.ceil(totalLines / MODAL_PAGE_SIZE));
            modalPage = Math.min(modalPage, totalPages);

            var start = totalLines === 0 ? 0 : (modalPage - 1) * MODAL_PAGE_SIZE;
            var end = Math.min(start + MODAL_PAGE_SIZE, totalLines);
            var slice = transfers.slice(start, end);

            var rowsHtml = '';
            for (var i = 0; i < slice.length; i++) {
                var t = slice[i];
                rowsHtml +=
                    '<tr>' +
                        '<td style="text-align:left;">' +
                            '<div class="vas-173-from-to">' + escapeHtml((t.FromWH || '') + ' → ' + (t.ToWH || '')) + '</div>' +
                            '<div class="vas-173-loc-sub">' + escapeHtml((t.FromLocator || '') + ' → ' + (t.ToLocator || '')) + '</div>' +
                        '</td>' +
                        '<td style="text-align:left;">' + escapeHtml(formatDate(t.MoveDate)) + '</td>' +
                        '<td style="text-align:right;">' + (t.Products || 0) + '</td>' +
                        '<td style="text-align:right; font-weight:700;">' + Number(t.Qty || 0).toLocaleString(window.navigator.language) + '</td>' +
                        '<td style="text-align:left;">' + escapeHtml(t.DoneBy || '—') + '</td>' +
                        '<td style="text-align:left;">' + escapeHtml(t.RequestedBy || '—') + '</td>' +
                    '</tr>';
            }

            var showingModalTxt = totalLines === 0 ? '' :
                lbl('VAS_173_Showing', 'Showing') + ' ' + (start + 1) + '–' + end + ' ' + lbl('VAS_173_Of', 'of') + ' ' + totalLines;

            var modalHtml =
                '<div class="vas-173-modal-scrim">' +
                    '<div class="vas-173-modal">' +
                        '<div class="vas-173-modal-header">' +
                            '<span class="vas-173-modal-title">' + escapeHtml(wh.WarehouseName + ' · ' + lbl('VAS_173_Transfers', 'Transfers')) + '</span>' +
                            '<span class="vas-173-info-pill">' + escapeHtml(periodTxt) + '</span>' +
                            '<button type="button" class="vas-173-modal-close" id="vas173-modal-close">&#215;</button>' +
                        '</div>' +
                        '<div class="vas-173-modal-body">' +
                            '<div class="vas-173-summary-strip">' +
                                '<div class="vas-173-sum-cell" style="background-color:' + color + '33; border-color:' + color + ';">' +
                                    '<span class="vas-173-sum-label">' + escapeHtml(lbl('VAS_173_Warehouse', 'Warehouse')) + '</span>' +
                                    '<span class="vas-173-sum-val">' + escapeHtml(wh.WarehouseName) + '</span>' +
                                '</div>' +
                                '<div class="vas-173-sum-cell">' +
                                    '<span class="vas-173-sum-label">' + escapeHtml(lbl('VAS_173_Period', 'Period')) + '</span>' +
                                    '<span class="vas-173-sum-val">' + escapeHtml(periodTxt) + '</span>' +
                                '</div>' +
                                '<div class="vas-173-sum-cell">' +
                                    '<span class="vas-173-sum-label">' + escapeHtml(lbl('VAS_173_Moves', 'Moves')) + '</span>' +
                                    '<span class="vas-173-sum-val">' + (wh.Moves || 0) + '</span>' +
                                '</div>' +
                                '<div class="vas-173-sum-cell">' +
                                    '<span class="vas-173-sum-label">' + escapeHtml(lbl('VAS_173_TotalQty', 'Total Qty')) + '</span>' +
                                    '<span class="vas-173-sum-val">' + Number(wh.TotalQty || 0).toLocaleString(window.navigator.language) + '</span>' +
                                '</div>' +
                            '</div>' +
                            '<div class="vas-173-modal-section-heading">' + escapeHtml(lbl('VAS_173_TransferLines', 'Transfer Lines')) + '</div>' +
                            '<div class="vas-173-modal-tbl-container">' +
                                '<table class="vas-173-tbl">' +
                                    '<colgroup>' +
                                        '<col style="width: 32%;">' +
                                        '<col style="width: 18%;">' +
                                        '<col style="width: 10%;">' +
                                        '<col style="width: 12%;">' +
                                        '<col style="width: 14%;">' +
                                        '<col style="width: 14%;">' +
                                    '</colgroup>' +
                                    '<thead>' +
                                        '<tr>' +
                                            '<th style="text-align:left;">' + escapeHtml(lbl('VAS_173_FromTo', 'From → To')) + '</th>' +
                                            '<th style="text-align:left;">' + escapeHtml(lbl('VAS_173_MoveDate', 'Move Date')) + '</th>' +
                                            '<th style="text-align:right;">' + escapeHtml(lbl('VAS_173_Products', 'Products')) + '</th>' +
                                            '<th style="text-align:right;">' + escapeHtml(lbl('VAS_173_Qty', 'Qty')) + '</th>' +
                                            '<th style="text-align:left;">' + escapeHtml(lbl('VAS_173_DoneBy', 'Done By')) + '</th>' +
                                            '<th style="text-align:left;">' + escapeHtml(lbl('VAS_173_RequestedBy', 'Requested By')) + '</th>' +
                                        '</tr>' +
                                    '</thead>' +
                                    '<tbody>' + rowsHtml + '</tbody>' +
                                '</table>' +
                            '</div>' +
                            '<div class="vas-173-modal-footer">' +
                                '<span class="vas-173-footer-helper">' + escapeHtml(showingModalTxt) + '</span>' +
                                '<span class="vas-173-pager">' +
                                    '<button type="button" class="vas-173-pager-btn" id="vas173-m-prev"' + (modalPage <= 1 ? ' disabled' : '') + '>&#8249;</button>' +
                                    '<span class="vas-173-pager-label">' + modalPage + ' ' + lbl('VAS_173_Of', 'of') + ' ' + totalPages + '</span>' +
                                    '<button type="button" class="vas-173-pager-btn" id="vas173-m-next"' + (modalPage >= totalPages ? ' disabled' : '') + '>&#8250;</button>' +
                                '</span>' +
                            '</div>' +
                        '</div>' +
                    '</div>' +
                '</div>';

            if ($modal) { $modal.remove(); }
            $modal = $(modalHtml);
            $('body').append($modal);

            $modal.find('#vas173-modal-close').on('click', closeModal);
            $modal.find('.vas-173-modal-scrim').on('click', function (e) { if (e.target === this) { closeModal(); } });
            $modal.find('#vas173-m-prev').on('click', function () { if (modalPage > 1) { modalPage--; renderModal(); } });
            $modal.find('#vas173-m-next').on('click', function () { if (modalPage < totalPages) { modalPage++; renderModal(); } });
        }

        function closeModal() {
            if ($modal) { $modal.remove(); $modal = null; }
            activeWH = null;
        }

        this.Initalize = function () {
            buildWidget();
            loadData();
        };

        this.refreshWidget = function () { loadData(); };

        this.getRoot = function () { return $wrapper; };

        this.disposeComponent = function () {
            if (widgetObserver) {
                widgetObserver.disconnect();
                widgetObserver = null;
            }
            closeModal();
            $root.off();
            $root.remove();
            $wrapper.remove();
        };
    };

    VAS.VAS_173_TransfersByWarehouseWidget.prototype.widgetFirevalueChanged = function (value) {
        if (this.listener) { this.listener.widgetFirevalueChanged(value); }
    };

    VAS.VAS_173_TransfersByWarehouseWidget.prototype.addChangeListener = function (listener) {
        this.listener = listener;
    };

    VAS.VAS_173_TransfersByWarehouseWidget.prototype.init = function (windowNo, frame) {
        this.frame = frame;
        this.AD_UserHomeWidgetID = frame.widgetInfo.AD_UserHomeWidgetID;
        this.windowNo = windowNo;
        this.Initalize();
        this.frame.getContentGrid().append(this.getRoot());
        ensureDashInlineSizeVar(this.getRoot());
    };

    VAS.VAS_173_TransfersByWarehouseWidget.prototype.widgetSizeChange = function (height, width) { };

    VAS.VAS_173_TransfersByWarehouseWidget.prototype.refreshWidget = function () {
        this.refreshWidget();
    };

    VAS.VAS_173_TransfersByWarehouseWidget.prototype.dispose = function () {
        this.disposeComponent();
        if (this.frame) { this.frame.dispose(); }
        this.frame = null;
    };

})(VAS, jQuery);
